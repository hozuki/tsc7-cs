// ported from: internal/api/callbackfs.go:1-243（整文件）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的匿名结构体（`struct{ Content *string }` / `struct{ Files, Directories []string }` /
//    `struct{ Path, Data string }`）在 C# 里必须**具名** → 本文件底部三个 private 嵌套类
//    （`ReadFileWrapper`/`AccessibleEntriesWrapper`/`WriteFileParams`），每个都实现
//    `IJsonMarshaler`/`IJsonUnmarshaler`（本仓没有反射，见 docs/json.md §2）。字段名与 Go 的 json tag 一致。
//
// 2) `vfs.FS.ReadFile(path) (contents string, ok bool)` 在本仓是 `bool TryReadFile(string path, out string contents)`
//    （Vfs/Vfs.cs:42 的既定映射），所以 `ReadFile` 的实现体写成 `TryReadFile`。
//    `Stat(path)` 返回 `FileInfo?`（Go 的 nil）。
//
// 3) `fs.call(...)` 返回 `([]byte, error)`（Go 的 json.Value）→ C# 返回 `byte[]`、失败抛异常。
//    Go 的 `panic(err)`（vfs.FS 的方法没有 error 通道，只能 panic）→ C# 的 `Debug.Fail(...)`
//    （§3.2：panic → DebugFailureException）。**这里是刻意的**：这些错误确实"不该发生"，
//    与 Go 一样是不可恢复的编程错误，而不是要静默吞掉的分支。
//
// 4) `ctx context.Context` → `Context`；`ipc.Conn` → `Ipc.Conn`。Go 的 `conn == nil` 检查保留（连接尚未建立时
//    的回调调用是使用错误，Go 返回错误、C# 抛 `CallbackFsException`，调用点同样会 panic/Debug.Fail）。
//
// 5) Go 的 `string(result) != "null"` 判断逐字保留（不是"解出来是 null"的判断）：Go 用原始字节与字面量 `null`
//    比较，`"null "`（带空格）会走解析路径；C# 同样按 UTF-8 解码后的字符串比较，保证两边分支一致。
using System.Text;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.Json;
using TypeScript.LsProto;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;

namespace TypeScript.Api;

/// <summary>Go 的 `fmt.Errorf("CallbackFS: %s called before connection set", name)`。</summary>
public sealed class CallbackFsException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Go: <c>type callbackFS struct</c>（callbackfs.go:20-27）：包住一个基础文件系统，把某些操作通过 RPC
/// 回调委派给客户端（于是客户端可以提供虚拟文件系统，例如内存文件）。
/// 要启用的回调在构造时给出（`--callbacks` CLI 参数）；连接在传输建立之后由 <see cref="SetConnection"/> 注入。
/// </summary>
internal sealed class CallbackFs(Vfs.FS @base, Dictionary<string, bool> enabledCallbacks) : Vfs.FS
{
    /// <summary>Go: <c>base vfs.FS</c>。</summary>
    private readonly Vfs.FS _base = @base;

    /// <summary>Go: <c>enabledCallbacks map[string]bool</c>。</summary>
    private readonly Dictionary<string, bool> _enabledCallbacks = enabledCallbacks;

    /// <summary>Go: <c>conn ipc.Conn</c>（连接建立后才设置）。</summary>
    private Ipc.Conn? _conn;

    /// <summary>Go: <c>ctx context.Context</c>（连接建立后才设置；Go 的零值是 nil context）。</summary>
    private Context? _ctx;

    // Callback names that can be enabled

    /// <summary>Go: <c>callbackReadFile = "readFile"</c>。</summary>
    internal const string CallbackReadFile = "readFile";

    /// <summary>Go: <c>callbackFileExists = "fileExists"</c>。</summary>
    internal const string CallbackFileExists = "fileExists";

    /// <summary>Go: <c>callbackDirectoryExists = "directoryExists"</c>。</summary>
    internal const string CallbackDirectoryExists = "directoryExists";

    /// <summary>Go: <c>callbackGetAccessibleEntries = "getAccessibleEntries"</c>。</summary>
    internal const string CallbackGetAccessibleEntries = "getAccessibleEntries";

    /// <summary>Go: <c>callbackRealpath = "realpath"</c>。</summary>
    internal const string CallbackRealpath = "realpath";

    /// <summary>Go: <c>callbackWriteFile = "writeFile"</c>。</summary>
    internal const string CallbackWriteFile = "writeFile";

    /// <summary>Go: <c>callbackRemoveFile = "removeFile"</c>。</summary>
    internal const string CallbackRemoveFile = "removeFile";

    /// <summary>Go: <c>func isCallbackName(name string) bool</c>（callbackfs.go:41-53）。</summary>
    private static bool IsCallbackName(string name)
    {
        switch (name)
        {
            case CallbackReadFile:
            case CallbackFileExists:
            case CallbackDirectoryExists:
            case CallbackGetAccessibleEntries:
            case CallbackRealpath:
            case CallbackWriteFile:
            case CallbackRemoveFile:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func newCallbackFS(base vfs.FS, callbacks []string) *callbackFS</c>（callbackfs.go:58-70）。
    /// 未知回调名在 Go 里 panic → C# 抛 <see cref="DebugFailureException"/>（<see cref="Debug.Fail"/>）。</summary>
    public static CallbackFs NewCallbackFs(Vfs.FS @base, string[] callbacks)
    {
        var enabled = new Dictionary<string, bool>(callbacks.Length, StringComparer.Ordinal);
        foreach (var cb in callbacks)
        {
            if (!IsCallbackName(cb))
            {
                Debug.Fail("unknown callback name: " + cb);
            }

            enabled[cb] = true;
        }

        return new CallbackFs(@base, enabled);
    }

    /// <summary>Go: <c>func (fs *callbackFS) SetConnection(ctx context.Context, conn ipc.Conn)</c>（callbackfs.go:72-77）：
    /// 必须在传输连接建立之后、任何需要回调的文件系统操作之前调用。</summary>
    public void SetConnection(Context ctx, Ipc.Conn conn)
    {
        _ctx = ctx;
        _conn = conn;
    }

    /// <summary>Go: <c>func (fs *callbackFS) isEnabled(name string) bool</c>（callbackfs.go:79-81）。</summary>
    private bool IsEnabled(string name) => _enabledCallbacks.ContainsKey(name);

    /// <summary>Go: <c>func (fs *callbackFS) call(name string, arg any) ([]byte, error)</c>（callbackfs.go:83-91）。</summary>
    private byte[] Call(string name, object? arg)
    {
        if (_conn is null)
        {
            throw new CallbackFsException($"CallbackFS: {name} called before connection set");
        }

        return _conn.Call(_ctx!, name, arg) ?? [];
    }

    /// <summary>Go: <c>func (fs *callbackFS) UseCaseSensitiveFileNames() bool</c>（callbackfs.go:93-95）。</summary>
    public bool UseCaseSensitiveFileNames() => _base.UseCaseSensitiveFileNames();

    /// <summary>Go: <c>func (fs *callbackFS) ReadFile(path string) (contents string, ok bool)</c>（callbackfs.go:97-121）。
    /// readFile 回调使用带包装的响应格式以区分三种状态：undefined（回落到真实 FS，wire 上是 null 或空）、
    /// null（找不到且不回退，`{"content": null}`）、字符串内容（`{"content": "..."}`）。
    /// 本仓 `vfs.FS.ReadFile` 命名为 <see cref="Vfs.FS.TryReadFile"/>（文件头第 2 条）。</summary>
    public bool TryReadFile(string path, out string contents)
    {
        if (IsEnabled(CallbackReadFile))
        {
            byte[] result;
            try
            {
                result = Call(CallbackReadFile, path);
            }
            catch (Exception err)
            {
                Debug.Fail(err.Message);
                throw;
            }

            if (result.Length > 0 && Encoding.UTF8.GetString(result) != "null")
            {
                var wrapper = new ReadFileWrapper();
                try
                {
                    UnmarshalInto(result, ref wrapper);
                }
                catch (Exception err)
                {
                    Debug.Fail(err.Message);
                    throw;
                }

                if (wrapper.Content is null)
                {
                    contents = "";
                    return false;
                }

                contents = wrapper.Content;
                return true;
            }
        }

        return _base.TryReadFile(path, out contents);
    }

    /// <summary>Go: <c>func (fs *callbackFS) FileExists(path string) bool</c>（callbackfs.go:123-133）。</summary>
    public bool FileExists(string path)
    {
        if (IsEnabled(CallbackFileExists))
        {
            byte[] result;
            try
            {
                result = Call(CallbackFileExists, path);
            }
            catch (Exception err)
            {
                Debug.Fail(err.Message);
                throw;
            }

            if (result.Length > 0 && Encoding.UTF8.GetString(result) != "null")
            {
                return Encoding.UTF8.GetString(result) == "true";
            }
        }

        return _base.FileExists(path);
    }

    /// <summary>Go: <c>func (fs *callbackFS) DirectoryExists(path string) bool</c>（callbackfs.go:135-145）。</summary>
    public bool DirectoryExists(string path)
    {
        if (IsEnabled(CallbackDirectoryExists))
        {
            byte[] result;
            try
            {
                result = Call(CallbackDirectoryExists, path);
            }
            catch (Exception err)
            {
                Debug.Fail(err.Message);
                throw;
            }

            if (result.Length > 0 && Encoding.UTF8.GetString(result) != "null")
            {
                return Encoding.UTF8.GetString(result) == "true";
            }
        }

        return _base.DirectoryExists(path);
    }

    /// <summary>Go: <c>func (fs *callbackFS) GetAccessibleEntries(path string) vfs.Entries</c>（callbackfs.go:147-172）。</summary>
    public Vfs.Entries GetAccessibleEntries(string path)
    {
        if (IsEnabled(CallbackGetAccessibleEntries))
        {
            byte[] result;
            try
            {
                result = Call(CallbackGetAccessibleEntries, path);
            }
            catch (Exception err)
            {
                Debug.Fail(err.Message);
                throw;
            }

            if (result.Length > 0)
            {
                // Go: `var rawEntries *struct{…}`（JSON null 时保持 nil，落到 base 分支）。
                AccessibleEntriesWrapper? rawEntries = null;
                try
                {
                    var dec = new JsonDecoder(result);
                    if (dec.PeekKind() == JsonKind.Null)
                    {
                        dec.ReadToken();
                    }
                    else
                    {
                        rawEntries = UnmarshalNew<AccessibleEntriesWrapper>(result);
                    }
                }
                catch (Exception err)
                {
                    Debug.Fail(err.Message);
                    throw;
                }

                if (rawEntries is not null)
                {
                    return new Vfs.Entries
                    {
                        Files = rawEntries.Files ?? [],
                        Directories = rawEntries.Directories ?? [],
                    };
                }
            }
        }

        return _base.GetAccessibleEntries(path);
    }

    /// <summary>Go: <c>func (fs *callbackFS) Realpath(path string) string</c>（callbackfs.go:174-186）。</summary>
    public string Realpath(string path)
    {
        if (IsEnabled(CallbackRealpath))
        {
            byte[] result;
            try
            {
                result = Call(CallbackRealpath, path);
            }
            catch (Exception err)
            {
                Debug.Fail(err.Message);
                throw;
            }

            if (result.Length > 0 && Encoding.UTF8.GetString(result) != "null")
            {
                try
                {
                    return Unmarshal<string>(result) ?? "";
                }
                catch (Exception err)
                {
                    Debug.Fail(err.Message);
                    throw;
                }
            }
        }

        return _base.Realpath(path);
    }

    /// <summary>Go: <c>func (fs *callbackFS) WriteFile(path string, data string) error</c>（callbackfs.go:188-202）。</summary>
    public void WriteFile(string path, string data)
    {
        if (IsEnabled(CallbackWriteFile))
        {
            var payload = new WriteFileParams { Path = path, Data = data };

            Call(CallbackWriteFile, payload);
            return;
        }

        _base.WriteFile(path, data);
    }

    /// <summary>Go: <c>func (fs *callbackFS) AppendFile(path string, data string) error</c>（callbackfs.go:204-207）：
    /// 始终交给 base（没有对应的回调）。</summary>
    public void AppendFile(string path, string data)
    {
        _base.AppendFile(path, data);
    }

    /// <summary>Go: <c>func (fs *callbackFS) Remove(path string) error</c>（callbackfs.go:209-215）。</summary>
    public void Remove(string path)
    {
        if (IsEnabled(CallbackRemoveFile))
        {
            Call(CallbackRemoveFile, path);
            return;
        }

        _base.Remove(path);
    }

    /// <summary>Go: <c>func (fs *callbackFS) Chtimes(path string, aTime, mTime time.Time) error</c>（callbackfs.go:217-220）：
    /// 始终交给 base（没有对应的回调）。</summary>
    public void Chtimes(string path, DateTime aTime, DateTime mTime)
    {
        _base.Chtimes(path, aTime, mTime);
    }

    /// <summary>Go: <c>func (fs *callbackFS) Stat(path string) vfs.FileInfo</c>（callbackfs.go:222-225）：
    /// 始终交给 base（没有对应的回调）。</summary>
    public Vfs.FileInfo? Stat(string path)
    {
        return _base.Stat(path);
    }

    /// <summary>Go 的匿名结构体 <c>struct{ Content *string `json:"content"` }</c>（callbackfs.go:106-108）。</summary>
    private sealed class ReadFileWrapper : IJsonUnmarshaler
    {
        public string? Content;

        internal static readonly StructSpec Spec = new(
        [
            new StructFieldSpec("content", false, false,
                static (target, dec) =>
                {
                    var t = (ReadFileWrapper)target;
                    if (dec.PeekKind() == JsonKind.Null)
                    {
                        dec.ReadToken();
                        t.Content = null;
                    }
                    else
                    {
                        t.Content = dec.Decode<string>();
                    }
                }
            ),
        ],
        []);

        public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);
    }

    /// <summary>Go 的匿名结构体 <c>struct{ Files []string `json:"files"`; Directories []string `json:"directories"` }</c>
    /// （callbackfs.go:154-157）。</summary>
    private sealed class AccessibleEntriesWrapper : IJsonUnmarshaler
    {
        public string[]? Files;

        public string[]? Directories;

        internal static readonly StructSpec Spec = new(
        [
            new StructFieldSpec("files", false, false,
                static (target, dec) =>
                {
                    var t = (AccessibleEntriesWrapper)target;
                    if (dec.PeekKind() == JsonKind.Null)
                    {
                        dec.ReadToken();
                        t.Files = null;
                    }
                    else
                    {
                        t.Files = DecodeStringArray(dec);
                    }
                }
            ),
            new StructFieldSpec("directories", false, false,
                static (target, dec) =>
                {
                    var t = (AccessibleEntriesWrapper)target;
                    if (dec.PeekKind() == JsonKind.Null)
                    {
                        dec.ReadToken();
                        t.Directories = null;
                    }
                    else
                    {
                        t.Directories = DecodeStringArray(dec);
                    }
                }
            ),
        ],
        []);

        private static string[] DecodeStringArray(JsonDecoder dec)
        {
            var result = new List<string>();
            dec.ReadToken();
            while (dec.PeekKind() != JsonKind.EndArray)
            {
                result.Add(dec.Decode<string>() ?? "");
            }

            dec.ReadToken();
            return [.. result];
        }

        public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);
    }

    /// <summary>Go 的匿名结构体 <c>struct{ Path string `json:"path"`; Data string `json:"data"` }</c>
    /// （callbackfs.go:190-193）。</summary>
    private sealed class WriteFileParams : IJsonMarshaler
    {
        public string Path = "";

        public string Data = "";

        internal static readonly StructSpec Spec = new(
        [],
        [
            static (target, enc) =>
            {
                var t = (WriteFileParams)target;
                enc.WriteToken(JsonToken.String("path"));
                MarshalEncode(enc, t.Path);
            },
            static (target, enc) =>
            {
                var t = (WriteFileParams)target;
                enc.WriteToken(JsonToken.String("data"));
                MarshalEncode(enc, t.Data);
            },
        ]);

        public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
    }
}

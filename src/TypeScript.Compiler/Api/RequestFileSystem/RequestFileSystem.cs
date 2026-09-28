// ported from: internal/api/requestfilesystem/requestfilesystem.go:1-714
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `type Kind string` + `KindFull`/`KindLayer`（requestfilesystem.go:15-24）→ C# 的 **`readonly struct Kind`**
//    （包一个 string，`Value`，静态字段 `Kind.Full`/`Kind.Layer`），照 `LsProto.CodeActionKind`
//    （Lsp.Generated.Enums.cs:779）的先例。理由：Go 的 Kind 是**具名 string 类型**，`default(Kind)`（Go 的零值
//    `""`）与任意未知字符串都必须原样保留，`newRequestFileSystemWorker` 的
//    `unknown request filesystem kind %q` 分支才能与 Go 逐行一致；C# 的 enum 承载不了未知字符串。
//    成员名按契约「枚举去前缀」：Go 的 `KindFull`/`KindLayer` → `Kind.Full`/`Kind.Layer`
//    （Session.Snapshot.cs 已按 `Kind.Full` 调用）。
// 2) 两个 Go 类型只靠大小写区分：`RequestFileSystem`（导出的参数消息，参与 JSON）与 `requestFileSystem`
//    （未导出的 FS 实现）→ C# 里后者叫 **`RequestFileSystemImpl`**（C# 标识符大小写敏感但不能只靠大小写消歧，
//    且 `RequestFileSystem` 这个名字已被消息类型占用）。
// 3) JSON 编解码：每个消息类实现 `IJsonMarshaler, IJsonUnmarshaler` + 显式 `StructSpec`（AOT，无反射）。
//    - `Files`（`nonnil:"true"`，无 omit）→ 不可空 `Dictionary<string,string>`，恒写出；
//    - `Directories`/`Symlinks`（`omitempty`，map）→ 可空字典，null 或空都不写；
//    - `RemovedPaths`（`omitempty`，slice）→ `string[]?`，null 或空都不写；
//    - `RequestSymlink.Host`（`omitempty`，bool）→ false 不写；
//    - `RequestDirectoryEntries.Files`/`Directories`（`nonnil:"true"`）→ 不可空 `string[]`，字段缺失时保持 `[]`
//      （Go 的 nonnil 保证非 nil）；显式 `null` 按契约（rejectNull=true）拒绝。
//      补充事实：Go 侧 `nonnil:"true"` 在本仓的 internal/json 里**没有实现者**（全仓 grep 只有 proto.go 与
//      requestfilesystem.go 两处出现该字符串），即 Go 收到显式 `null` 会得到 nil 切片/map 而不报错；
//      契约 §3 统一要求"切片/map 字段拒绝显式 null"，本文件以及同批 Proto.Types 都按契约写 rejectNull=true。
// 4) `vfs.FS` 的 `(string, bool)`/`error` → C# 的 `TryReadFile(path, out contents)` / 抛异常；
//    Go 的 `vfs.ErrInvalid`（= `fs.ErrInvalid`）按 Vfs/Vfs.cs 的映射抛 `ArgumentException("invalid argument")`。
// 5) Go 用**值接收者**的方法（拷贝整个结构体）在 C# 里是普通实例方法。只有 `applyTo` 与 `WithBaseFileSystem`
//    会改字段：Go 里前者"改副本并返回副本"（调用点 `compacted := fileSystem.applyTo(...)` 立刻丢掉原对象）、
//    后者"浅拷贝 + 换 base"，C# 落成"原地改并返回自身"与 `MemberwiseClone`（唯一观察者就是返回值，语义等价）。
// 6) `base vfs.FS` 可为 nil → C# `FS? Base`；Go 里默认非 nil 的位置（`s.baseFileSystem()`、`s.base.WriteFile`）
//    用 `!` 保留同样的假设（Go 到那里是 nil 解引用 panic，C# 是 NullReferenceException）。
// 7) `Overlays()`：Go 在 base 不是 `project.LayeredFileSystem` 时返回 nil map；C# 的
//    `Project.LayeredFileSystem.Overlays()` 返回类型**不可空**（`Project/Snapshot.cs:233` 的
//    `ProcessFileChanges(…, Dictionary<Path, Overlay> previousOverlays, …)` 与 `Project/Session.cs:1009` 的
//    `snapshot.Overlays().Count` 都按不可空用），返回 nil 会变成 NullReferenceException ⇒ 改返回**空字典**。
//    对 Go 的所有调用点（`range` / `m[path]` 读）语义等价。
// 8) `localPathInfo` / `requestPathLookup.info`：Go 的 `requestFile`/`requestDirectory` 自己就是 `vfs.FileInfo`，
//    而 C# 的 `TypeScript.Vfs.FileInfo` 是 sealed 数据类（Vfs/Vfs.cs:70）⇒ 这里存的是 `IRequestEntry?`
//    （即 Go 的 `node.entry` 本身），`info.IsDir()` 与 `node.entry.(vfs.FileInfo)` 落成 PathTree.cs 的扩展方法
//    `IsDir()`/`FileInfo()`；`Stat` 返回 `entry.FileInfo()`（每次按需构造，先例 OverlayFs.cs:590-615）。
// 9) 错误：Go 的 `fmt.Errorf(...)`（session.go:1237-1239 只按 `%w: %w` 包一层 `ErrClientError` 后返回）
//    → 直接抛 `ClientErrorException`（Session.Snapshot.cs 不再包装，异常类型即最终语义）；错误文本的 `%q`
//    落成插值里的 `\"{x}\"`（本仓惯例，如 Session.Checker.cs:199）。
// 10) `slices.*`/`maps.Clone` → `List<T>`/`Array.Sort(…, StringComparer.Ordinal)`/`new Dictionary<>(…)`；
//    `slices.DeleteFunc` 的"原地压缩"由 `PathTree.DeleteEqualNames` 与各处的局部循环照做
//    （Go 的 `slices.Sort`/`strings` 比较都是逐字节序 ⇒ 一律 `StringComparison.Ordinal`/`StringComparer.Ordinal`）。
// 11) 未导出包级函数的落点（契约 §「命名规则」）：`newRequestFileSystemWorker`/`getRequestFileSystem`/
//    `cloneEntries`/`mergeEntries`/`statFileSystem` → `RequestFileSystemImpl` 的 `internal static` 成员；
//    `NewForUpdate`/`HasFullFileSystem`（导出）→ 消息类型 `RequestFileSystem` 的 `public static` 成员
//    （调用点即 `RequestFileSystem.NewForUpdate(...)`，与 Go 的 `requestfilesystem.NewForUpdate` 一致）。
// 12) `mergeEntries(base, overlay, equal func(string,string) bool)` 的 `equal` 参数 → `bool caseSensitive`
//    （见 PathTree.cs 文件头第 8 条：两个调用点的 equal 是同一形状，传 bool 免去闭包分配）。
// 13) 本包 Go 侧**没有任何锁/原子量**（requestFileSystem 是构造后只读的查询结构）⇒ 不涉及 Lock/RWMutex。
using TypeScript.Json;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;
using VfsFileInfo = TypeScript.Vfs.FileInfo;

namespace TypeScript.Api.RequestFileSystem;

/// <summary>
/// Go: <c>type Kind string</c>（requestfilesystem.go:15-24）。C# 用包 string 的 readonly struct
/// （见文件头第 1 条）；`default(Kind)` 即 Go 的零值 <c>""</c>。
/// </summary>
public readonly struct Kind : IEquatable<Kind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>Kind(value)</c> 转换。</summary>
    public Kind(string value)
    {
        _value = value;
    }

    /// <summary>Go 的 <c>string(kind)</c>。</summary>
    public string Value => _value ?? "";

    /// <summary>Go: <c>KindFull Kind = "full"</c>（requestfilesystem.go:19）：提供的文件系统是"完整且权威"的。</summary>
    public static readonly Kind Full = new("full");

    /// <summary>Go: <c>KindLayer Kind = "layer"</c>（requestfilesystem.go:21）：先查提供的文件系统，再回落宿主。</summary>
    public static readonly Kind Layer = new("layer");

    public static implicit operator Kind(string value) => new(value);

    public static explicit operator string(Kind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(Kind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Kind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(Kind left, Kind right) => left.Equals(right);

    public static bool operator !=(Kind left, Kind right) => !left.Equals(right);

    /// <summary>Go: 具名 string 类型按 JSON 字符串写出（requestfilesystem.go:46 的 <c>json:"kind"</c>）。</summary>
    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(JsonToken.String(Value));
    }
}

/// <summary>Go 的 <c>type RequestDirectoryEntries struct</c>（requestfilesystem.go:26-30）：缓存的目录列举结果，
/// 条目名相对该目录，与 <c>vfs.GetAccessibleEntries</c> 一致。</summary>
public sealed class RequestDirectoryEntries : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Files []string `json:"files" nonnil:"true"`</c>。</summary>
    public string[] Files = [];

    /// <summary>Go: <c>Directories []string `json:"directories" nonnil:"true"`</c>。</summary>
    public string[] Directories = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("files", false, true,
            static (target, dec) =>
            {
                var t = (RequestDirectoryEntries)target;
                t.Files = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "") ?? [];
            }
        ),
        new StructFieldSpec("directories", false, true,
            static (target, dec) =>
            {
                var t = (RequestDirectoryEntries)target;
                t.Directories = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "") ?? [];
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (RequestDirectoryEntries)target;
            enc.WriteToken(JsonToken.String("files"));
            EncodeNames(enc, t.Files);
        },
        static (target, enc) =>
        {
            var t = (RequestDirectoryEntries)target;
            enc.WriteToken(JsonToken.String("directories"));
            EncodeNames(enc, t.Directories);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);

    /// <summary>Go 的 <c>[]string</c> 字段的写出（生成代码的形状：BeginArray / 逐项 / EndArray）。</summary>
    private static void EncodeNames(JsonEncoder enc, string[] values)
    {
        enc.WriteToken(BeginArray);
        foreach (var value in values)
        {
            enc.WriteToken(JsonToken.String(value));
        }

        enc.WriteToken(EndArray);
    }
}

/// <summary>Go 的 <c>type RequestSymlink struct</c>（requestfilesystem.go:32-41）。</summary>
public sealed class RequestSymlink : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Target string `json:"target"`</c>：相对链接所在目录解析（原生符号链接语义）。</summary>
    public string Target = "";

    /// <summary>Go: <c>Host bool `json:"host,omitempty"`</c>：让目标走宿主文件系统 —— 完整文件系统访问请求外路径的唯一途径。</summary>
    public bool Host;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("target", false, false,
            static (target, dec) =>
            {
                var t = (RequestSymlink)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Target = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("host", false, false,
            static (target, dec) =>
            {
                var t = (RequestSymlink)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Host = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (RequestSymlink)target;
            enc.WriteToken(JsonToken.String("target"));
            MarshalEncode(enc, t.Target);
        },
        static (target, enc) =>
        {
            var t = (RequestSymlink)target;
            if (!t.Host) { return; }
            enc.WriteToken(JsonToken.String("host"));
            enc.WriteToken(JsonToken.Bool(t.Host));
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go 的 <c>type RequestFileSystem struct</c>（requestfilesystem.go:43-59）：为"创建快照"的请求提供
/// 文件内容，并可选提供目录列举。</summary>
public sealed class RequestFileSystem : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Kind Kind `json:"kind"`</c>。</summary>
    public Kind Kind;

    /// <summary>Go: <c>Files map[string]string `json:"files" nonnil:"true"`</c>：文件名 → 完整内容。</summary>
    public Dictionary<string, string> Files = [];

    /// <summary>Go: <c>Directories map[string]RequestDirectoryEntries `json:"directories,omitempty"`</c>：
    /// 目录名 → 完整列举结果；省略列举时由 `Files` 隐含的目录结构推导。</summary>
    public Dictionary<string, RequestDirectoryEntries>? Directories;

    /// <summary>Go: <c>Symlinks map[string]RequestSymlink `json:"symlinks,omitempty"`</c>：
    /// 链接路径 → 本文件系统或宿主文件系统里的目标。</summary>
    public Dictionary<string, RequestSymlink>? Symlinks;

    /// <summary>Go: <c>RemovedPaths []string `json:"removedPaths,omitempty"`</c>：即便在底层快照/宿主里存在，
    /// 也必须当作"缺失"的文件或目录树。</summary>
    public string[]? RemovedPaths;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("kind", false, false,
            static (target, dec) =>
            {
                var t = (RequestFileSystem)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Kind = new Kind(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("files", false, true,
            static (target, dec) =>
            {
                var t = (RequestFileSystem)target;
                t.Files = DecodeStringMap(dec);
            }
        ),
        new StructFieldSpec("directories", false, true,
            static (target, dec) =>
            {
                var t = (RequestFileSystem)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Directories = null; }
                else
                {
                    var map = new Dictionary<string, RequestDirectoryEntries>();
                    dec.ReadToken();
                    while (dec.PeekKind() != JsonKind.EndObject)
                    {
                        var key = dec.ReadName();
                        map[key] = dec.DecodeNew<RequestDirectoryEntries>();
                    }

                    dec.ReadToken();
                    t.Directories = map;
                }
            }
        ),
        new StructFieldSpec("symlinks", false, true,
            static (target, dec) =>
            {
                var t = (RequestFileSystem)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Symlinks = null; }
                else
                {
                    var map = new Dictionary<string, RequestSymlink>();
                    dec.ReadToken();
                    while (dec.PeekKind() != JsonKind.EndObject)
                    {
                        var key = dec.ReadName();
                        map[key] = dec.DecodeNew<RequestSymlink>();
                    }

                    dec.ReadToken();
                    t.Symlinks = map;
                }
            }
        ),
        new StructFieldSpec("removedPaths", false, true,
            static (target, dec) =>
            {
                var t = (RequestFileSystem)target;
                t.RemovedPaths = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "");
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (RequestFileSystem)target;
            enc.WriteToken(JsonToken.String("kind"));
            MarshalEncode(enc, t.Kind);
        },
        static (target, enc) =>
        {
            var t = (RequestFileSystem)target;
            enc.WriteToken(JsonToken.String("files"));
            enc.WriteToken(BeginObject);
            foreach (var pair in t.Files)
            {
                enc.WriteToken(JsonToken.String(pair.Key));
                enc.WriteToken(JsonToken.String(pair.Value));
            }

            enc.WriteToken(EndObject);
        },
        static (target, enc) =>
        {
            var t = (RequestFileSystem)target;
            if (t.Directories is null || t.Directories.Count == 0) { return; }
            enc.WriteToken(JsonToken.String("directories"));
            enc.WriteToken(BeginObject);
            foreach (var pair in t.Directories)
            {
                enc.WriteToken(JsonToken.String(pair.Key));
                MarshalEncode(enc, pair.Value);
            }

            enc.WriteToken(EndObject);
        },
        static (target, enc) =>
        {
            var t = (RequestFileSystem)target;
            if (t.Symlinks is null || t.Symlinks.Count == 0) { return; }
            enc.WriteToken(JsonToken.String("symlinks"));
            enc.WriteToken(BeginObject);
            foreach (var pair in t.Symlinks)
            {
                enc.WriteToken(JsonToken.String(pair.Key));
                MarshalEncode(enc, pair.Value);
            }

            enc.WriteToken(EndObject);
        },
        static (target, enc) =>
        {
            var t = (RequestFileSystem)target;
            if (t.RemovedPaths is null || t.RemovedPaths.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("removedPaths"));
            enc.WriteToken(BeginArray);
            foreach (var value in t.RemovedPaths)
            {
                enc.WriteToken(JsonToken.String(value));
            }

            enc.WriteToken(EndArray);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);

    /// <summary>Go: <c>map[string]string</c> 字段的读出（生成代码的形状）。</summary>
    private static Dictionary<string, string> DecodeStringMap(JsonDecoder dec)
    {
        var map = new Dictionary<string, string>();
        dec.ReadToken();
        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var key = dec.ReadName();
            map[key] = dec.Decode<string>() ?? "";
        }

        dec.ReadToken();
        return map;
    }

    /// <summary>Go: <c>func NewForUpdate(params *RequestFileSystem, base vfs.FS, currentDirectory string, fileChanges *project.FileChangeSummary) (vfs.FS, error)</c>
    /// （requestfilesystem.go:91-115）：为快照更新创建请求文件系统。层叠在请求文件系统之上时会**急切压平**，
    /// 使结果不再保留其 base 快照的文件系统。Go 的 <c>(vfs.FS, error)</c> → 正常路径返回 FS、错误路径抛异常。</summary>
    public static FS NewForUpdate(RequestFileSystem? @params, FS @base, string currentDirectory, FileChangeSummary fileChanges)
    {
        if (@params is null)
        {
            return @base;
        }

        var baseFileSystem = @base;
        if (@params.Kind == Kind.Full)
        {
            if (RequestFileSystemImpl.GetRequestFileSystem(@base) is { } requestBase)
            {
                baseFileSystem = requestBase.Base!;
            }
        }

        var fileSystem = RequestFileSystemImpl.NewRequestFileSystemWorker(@params, baseFileSystem, currentDirectory);
        if (RequestFileSystemImpl.GetRequestFileSystem(baseFileSystem) is { } baseRequestFileSystem)
        {
            fileSystem = fileSystem.ApplyTo(baseRequestFileSystem);
        }

        if (@params.Kind == Kind.Layer)
        {
            FileChanges.AddFileChanges(fileChanges, @params, baseFileSystem, fileSystem, currentDirectory);
        }

        return fileSystem;
    }

    /// <summary>Go: <c>func HasFullFileSystem(fileSystem vfs.FS) bool</c>（requestfilesystem.go:117-120）：
    /// fileSystem 是否包含一个完整的请求文件系统。</summary>
    public static bool HasFullFileSystem(FS fileSystem)
    {
        var requestFileSystem = RequestFileSystemImpl.GetRequestFileSystem(fileSystem);
        return requestFileSystem is not null && requestFileSystem.Kind == Kind.Full;
    }
}

/// <summary>Go 的 <c>type requestFileSystem struct</c>（requestfilesystem.go:60-66，见文件头第 2 条）：
/// 要么是完整文件系统，要么是宿主文件系统之上的一层。它的 base 总是宿主（宿主可能是 callback 文件系统）；
/// 继承来的请求条目会被压平成路径。</summary>
internal sealed partial class RequestFileSystemImpl : LayeredFileSystem, RebasableFileSystem, FileChangeExpander
{
    /// <summary>Go: <c>kind Kind</c>。</summary>
    internal Kind Kind;

    /// <summary>Go: <c>base vfs.FS</c>（可为 nil，见文件头第 6 条）。</summary>
    internal FS? Base;

    /// <summary>Go: <c>currentDirectory string</c>。</summary>
    internal string CurrentDirectory = "";

    /// <summary>Go: <c>useCaseSensitiveNames bool</c>。</summary>
    internal bool UseCaseSensitiveNames;

    /// <summary>Go: <c>paths *requestPathNode</c>（恒非 null：新建时即赋一个空节点，`applyTo` 只接受 compose 的结果）。</summary>
    internal RequestPathNode Paths = new();

    /// <summary>Go: <c>func getRequestFileSystem(fileSystem vfs.FS) *requestFileSystem</c>（requestfilesystem.go:83-86）。</summary>
    internal static RequestFileSystemImpl? GetRequestFileSystem(FS fileSystem)
    {
        return fileSystem as RequestFileSystemImpl;
    }

    /// <summary>Go: <c>func newRequestFileSystemWorker(params *RequestFileSystem, base vfs.FS, currentDirectory string) (*requestFileSystem, error)</c>
    /// （requestfilesystem.go:122-205）。</summary>
    internal static RequestFileSystemImpl NewRequestFileSystemWorker(RequestFileSystem @params, FS @base, string currentDirectory)
    {
        if (@params.Kind != Kind.Full && @params.Kind != Kind.Layer)
        {
            throw new ClientErrorException($"unknown request filesystem kind \"{@params.Kind.Value}\"");
        }

        var result = new RequestFileSystemImpl
        {
            Kind = @params.Kind,
            Base = @base,
            CurrentDirectory = currentDirectory,
            UseCaseSensitiveNames = @base.UseCaseSensitiveFileNames(),
            Paths = new RequestPathNode(),
        };
        result.RegisterDirectory(currentDirectory);
        var fileNames = new List<string>(@params.Files.Keys);
        fileNames.Sort(StringComparer.Ordinal);
        foreach (var fileName in fileNames)
        {
            var content = @params.Files[fileName];
            var absoluteFileName = result.ToAbsolutePath(fileName);
            var path = result.ToPath(absoluteFileName);
            var node = result.Paths.Ensure(path);
            if (node.Entry is RequestFileEntry existing)
            {
                throw new ClientErrorException($"duplicate request filesystem file path \"{existing.FileName}\" and \"{absoluteFileName}\"");
            }

            node.Entry = new RequestFileEntry(absoluteFileName, content);
            result.RegisterDirectory(GetDirectoryPath(absoluteFileName));
        }

        var seenDirectories = new HashSet<Path>();
        var listedDirectories = new List<string>();
        if (@params.Directories is not null)
        {
            foreach (var (directoryName, entries) in @params.Directories)
            {
                var absoluteDirectoryName = result.ToAbsolutePath(directoryName);
                var path = result.ToPath(absoluteDirectoryName);
                var node = result.Paths.Ensure(path);
                if (!seenDirectories.Add(path))
                {
                    throw new ClientErrorException($"duplicate request filesystem directory path \"{absoluteDirectoryName}\"");
                }

                if (node.Entry is not RequestFileEntry)
                {
                    node.Entry = new RequestDirectoryEntry(
                        absoluteDirectoryName,
                        new Entries(CloneNames(entries.Files), CloneNames(entries.Directories)));
                }

                result.RegisterDirectory(GetDirectoryPath(absoluteDirectoryName));
                foreach (var child in entries.Directories)
                {
                    listedDirectories.Add(CombinePaths(absoluteDirectoryName, child));
                }
            }
        }

        if (@params.Symlinks is not null)
        {
            foreach (var linkName in @params.Symlinks.Keys)
            {
                result.RegisterDirectory(GetDirectoryPath(result.ToAbsolutePath(linkName)));
            }
        }

        var seenSymlinks = new Dictionary<Path, string>();
        if (@params.Symlinks is not null)
        {
            foreach (var (linkName, symlink) in @params.Symlinks)
            {
                var absoluteLinkName = result.ToAbsolutePath(linkName);
                var path = result.ToPath(absoluteLinkName);
                var node = result.Paths.Ensure(path);
                if (seenSymlinks.TryGetValue(path, out var existing))
                {
                    throw new ClientErrorException($"duplicate request filesystem symlink path \"{existing}\" and \"{absoluteLinkName}\"");
                }

                seenSymlinks[path] = absoluteLinkName;
                var targetDirectory = GetDirectoryPath(absoluteLinkName);
                var absoluteTarget = result.ToAbsolutePathFrom(symlink.Target, targetDirectory);
                if (node.Entry is null)
                {
                    node.Entry = new RequestSymlinkEntry
                    {
                        LinkName = absoluteLinkName,
                        Target = absoluteTarget,
                        Host = symlink.Host,
                    };
                }
            }
        }

        foreach (var directoryName in listedDirectories)
        {
            result.RegisterDirectory(directoryName);
        }

        if (@params.RemovedPaths is not null)
        {
            foreach (var path in @params.RemovedPaths)
            {
                result.Paths.Ensure(result.ToPath(result.ToAbsolutePath(path))).Fallback = RequestFallback.Missing;
            }
        }

        result.Paths = PathTree.ComposeRequestPaths(null, result.Paths, RequestFallback.Allowed, result.UseCaseSensitiveNames);
        return result;
    }

    /// <summary>Go: <c>func cloneEntries(entries vfs.Entries) vfs.Entries</c>（requestfilesystem.go:413-425）。</summary>
    internal static Entries CloneEntries(Entries entries)
    {
        var result = new Entries(CloneNames(entries.Files), CloneNames(entries.Directories));
        if (entries.Symlinks is not null)
        {
            result.Symlinks = new HashSet<string>(entries.Symlinks);
        }

        return result;
    }

    /// <summary>Go: <c>func mergeEntries(base vfs.Entries, overlay vfs.Entries, equal func(string, string) bool) vfs.Entries</c>
    /// （requestfilesystem.go:538-576，见文件头第 12 条：`equal` → `bool caseSensitive`）。</summary>
    internal static Entries MergeEntries(Entries baseEntries, Entries overlay, bool caseSensitive)
    {
        var result = CloneEntries(baseEntries);
        var symlinks = result.Symlinks;
        if (symlinks is null)
        {
            symlinks = [];
            result.Symlinks = symlinks;
        }

        foreach (var name in overlay.Files)
        {
            result.Directories = PathTree.DeleteEqualNames(result.Directories, name, caseSensitive);
            if (!ContainsName(result.Files, name, caseSensitive))
            {
                result.Files = [.. result.Files, name];
            }

            DeleteSymlinkName(symlinks, name, caseSensitive);
        }

        foreach (var name in overlay.Directories)
        {
            result.Files = PathTree.DeleteEqualNames(result.Files, name, caseSensitive);
            if (!ContainsName(result.Directories, name, caseSensitive))
            {
                result.Directories = [.. result.Directories, name];
            }

            DeleteSymlinkName(symlinks, name, caseSensitive);
        }

        if (overlay.Symlinks is not null)
        {
            foreach (var name in overlay.Symlinks)
            {
                symlinks.Add(name);
            }
        }

        Array.Sort(result.Files, StringComparer.Ordinal);
        Array.Sort(result.Directories, StringComparer.Ordinal);
        return result;
    }

    /// <summary>Go: <c>func statFileSystem(fileSystem vfs.FS, path string) vfs.FileInfo</c>（requestfilesystem.go:700-714）。</summary>
    internal static VfsFileInfo? StatFileSystem(FS? fileSystem, string path)
    {
        if (fileSystem is null)
        {
            return null;
        }

        if (fileSystem.Stat(path) is { } info)
        {
            return info;
        }

        if (fileSystem.DirectoryExists(path))
        {
            return new RequestDirectoryEntry(path).Info();
        }

        if (fileSystem.FileExists(path))
        {
            return new RequestFileEntry { FileName = path }.Info();
        }

        return null;
    }

    /// <summary>Go 的 <c>slices.Clone(values)</c>（nil/空统一成空数组，见文件头第 10 条）。</summary>
    private static string[] CloneNames(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return [];
        }

        return (string[])values.Clone();
    }

    /// <summary>Go 的 <c>slices.ContainsFunc(values, func(value string) bool { return equal(value, name) })</c>。</summary>
    private static bool ContainsName(string[] values, string name, bool caseSensitive)
    {
        foreach (var value in values)
        {
            if (PathTree.EqualNames(value, name, caseSensitive))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go 的 <c>deleteSymlink(name)</c>（requestfilesystem.go:551-557）。</summary>
    private static void DeleteSymlinkName(HashSet<string> symlinks, string name, bool caseSensitive)
    {
        foreach (var existingName in symlinks.ToArray())
        {
            if (PathTree.EqualNames(existingName, name, caseSensitive))
            {
                symlinks.Remove(existingName);
            }
        }
    }

    /// <summary>Go: <c>func (s *requestFileSystem) BaseFileSystem() vfs.FS</c>（requestfilesystem.go:211-213；
    /// 未导出的值接收者版本 `baseFileSystem()` 与它返回同一字段，合并到这里，见文件头第 5、6 条）。</summary>
    public FS BaseFileSystem()
    {
        return Base!;
    }

    /// <summary>Go: <c>func (s *requestFileSystem) WithBaseFileSystem(base vfs.FS) project.LayeredFileSystem</c>
    /// （requestfilesystem.go:215-219）：Go 是 `clone := *s; clone.base = base; return &clone`（浅拷贝）。</summary>
    public LayeredFileSystem WithBaseFileSystem(FS @base)
    {
        var clone = (RequestFileSystemImpl)MemberwiseClone();
        clone.Base = @base;
        return clone;
    }

    /// <summary>Go: <c>func (s *requestFileSystem) Overlays() map[tspath.Path]*project.Overlay</c>
    /// （requestfilesystem.go:221-238，见文件头第 7 条：base 非 LayeredFileSystem 时返回空字典而不是 nil）。</summary>
    public Dictionary<Path, Overlay> Overlays()
    {
        var result = new Dictionary<Path, Overlay>();
        if (Base is not LayeredFileSystem layered)
        {
            return result;
        }

        foreach (var (path, overlay) in layered.Overlays())
        {
            var lookup = LookupPath(overlay.FileName());
            if (lookup.FileSystem is null || ToPath(lookup.Path) != path)
            {
                continue;
            }

            result[path] = overlay;
        }

        return result;
    }

    /// <summary>Go: <c>func (s requestFileSystem) applyTo(base requestFileSystem) requestFileSystem</c>
    /// （requestfilesystem.go:240-245，见文件头第 5 条）。</summary>
    internal RequestFileSystemImpl ApplyTo(RequestFileSystemImpl @base)
    {
        Paths = PathTree.ComposeRequestPaths(@base.Paths, Paths, RequestFallback.Allowed, UseCaseSensitiveNames);
        Kind = @base.Kind;
        Base = @base.Base;
        return this;
    }

    /// <summary>Go: <c>func (s requestFileSystem) blocksFallback(path string) bool</c>（requestfilesystem.go:247-250）。</summary>
    internal bool BlocksFallback(string path)
    {
        var (_, fallback) = RequestPathNode.Lookup(Paths, ToPath(path));
        return fallback == RequestFallback.Missing;
    }

    /// <summary>Go: <c>func (s requestFileSystem) toAbsolutePath(path string) string</c>（requestfilesystem.go:252-254）。</summary>
    internal string ToAbsolutePath(string path)
    {
        return ToAbsolutePathFrom(path, CurrentDirectory);
    }

    /// <summary>Go: <c>func (s requestFileSystem) toAbsolutePathFrom(path string, currentDirectory string) string</c>
    /// （requestfilesystem.go:256-262）。</summary>
    internal string ToAbsolutePathFrom(string path, string currentDirectory)
    {
        var absolutePath = GetNormalizedAbsolutePath(path, currentDirectory);
        if (IsDiskPathRoot(absolutePath))
        {
            return absolutePath;
        }

        return RemoveTrailingDirectorySeparator(absolutePath);
    }

    /// <summary>Go: <c>func (s requestFileSystem) toPath(path string) tspath.Path</c>（requestfilesystem.go:264-266）。
    /// 注意：本方法名与 `TSPath.ToPath` 同名，方法成员会遮蔽 `using static` 导入的同名函数 ⇒ 必须全限定调用。</summary>
    internal Path ToPath(string path)
    {
        return TypeScript.TSPath.TSPath.ToPath(path, CurrentDirectory, UseCaseSensitiveNames);
    }

    /// <summary>Go: <c>func (s requestFileSystem) registerDirectory(directoryName string)</c>（requestfilesystem.go:268-282）：
    /// 沿路径向上登记目录节点，遇到已有 entry 即停。</summary>
    internal void RegisterDirectory(string directoryName)
    {
        directoryName = ToAbsolutePath(directoryName);
        while (true)
        {
            var node = Paths.Ensure(ToPath(directoryName));
            if (node.Entry is not null)
            {
                return;
            }

            node.Entry = new RequestDirectoryEntry(directoryName);
            var parentName = GetDirectoryPath(directoryName);
            if (parentName == directoryName)
            {
                return;
            }

            directoryName = parentName;
        }
    }

    /// <summary>Go: <c>func (s requestFileSystem) resolvePath(path string) resolvedRequestPath</c>（requestfilesystem.go:284-316）：
    /// 跟随符号链接（带环检测），算出 path 是否落到宿主、是否有效。</summary>
    internal ResolvedRequestPath ResolvePath(string path)
    {
        path = ToAbsolutePath(path);
        var result = new ResolvedRequestPath { Path = path, Ok = true };
        var seen = new HashSet<Path>();
        while (true)
        {
            var canonicalPath = ToPath(result.Path);
            if (RequestPathNode.ContainsFileAncestor(Paths, canonicalPath))
            {
                result.Ok = false;
                return result;
            }

            var (matchPath, match) = RequestPathNode.FirstSymlink(Paths, canonicalPath);
            if (match is null)
            {
                result.Host = IsHostPath(result.Path);
                return result;
            }

            if (!seen.Add(matchPath))
            {
                result.Ok = false;
                return result;
            }

            result.FollowedSymlink = true;
            if (!TryTrimFilePathPrefix(result.Path, match.LinkName, UseCaseSensitiveNames, out var suffix))
            {
                result.Ok = false;
                return result;
            }

            result.Path = ToAbsolutePath(CombinePaths(match.Target, TrimPrefixSlash(suffix)));
            if (match.Host)
            {
                result.Host = true;
                return result;
            }
        }
    }

    /// <summary>Go: <c>func (s requestFileSystem) isHostPath(path string) bool</c>（requestfilesystem.go:318-327）：
    /// path 是否落在某个 `host: true` 的符号链接目标之内。</summary>
    internal bool IsHostPath(string path)
    {
        var canonicalPath = ToPath(path);
        var found = false;
        RequestPathNode.WalkSymlinks(Paths, (_, symlink) =>
        {
            if (symlink.Host && PathTree.RequestPathContains(ToPath(symlink.Target), canonicalPath))
            {
                found = true;
            }
        });
        return found;
    }

    /// <summary>Go: <c>func (s requestFileSystem) aliasesForPath(path string) []string</c>（requestfilesystem.go:329-360）：
    /// 逐层展开"指向 path 的符号链接"得到别名（广度优先、去重、只保留能解析的）。</summary>
    internal string[] AliasesForPath(string path)
    {
        var symlinks = new List<RequestSymlinkEntry>();
        RequestPathNode.WalkSymlinks(Paths, (_, symlink) => symlinks.Add(symlink));

        var seen = new HashSet<Path> { ToPath(path) };
        var queue = new Queue<string>();
        queue.Enqueue(ToAbsolutePath(path));
        var aliases = new List<string>();
        while (queue.Count > 0)
        {
            var candidate = queue.Dequeue();
            foreach (var symlink in symlinks)
            {
                if (!TryTrimFilePathPrefix(candidate, symlink.Target, UseCaseSensitiveNames, out var suffix))
                {
                    continue;
                }

                if (suffix != "" && !HasTrailingDirectorySeparator(symlink.Target) && !suffix.StartsWith('/'))
                {
                    continue;
                }

                var alias = ToAbsolutePath(CombinePaths(symlink.LinkName, TrimPrefixSlash(suffix)));
                var aliasPath = ToPath(alias);
                if (seen.Contains(aliasPath))
                {
                    continue;
                }

                if (!ResolvePath(alias).Ok)
                {
                    continue;
                }

                seen.Add(aliasPath);
                aliases.Add(alias);
                queue.Enqueue(alias);
            }
        }

        return [.. aliases];
    }

    /// <summary>Go: <c>func (s requestFileSystem) localPathInfo(path string) (vfs.FileInfo, requestFallback)</c>
    /// （requestfilesystem.go:362-369，见文件头第 8 条：返回的是 entry 本身，Go 的 `(vfs.FileInfo)` 断言等价于
    /// "entry 是 requestFile 或 requestDirectory"）。</summary>
    internal (IRequestEntry? Info, RequestFallback Fallback) LocalPathInfo(string path)
    {
        var (node, fallback) = RequestPathNode.Lookup(Paths, ToPath(path));
        if (node is null)
        {
            return (null, fallback);
        }

        return (node.Entry is RequestFileEntry or RequestDirectoryEntry ? node.Entry : null, fallback);
    }

    /// <summary>Go: <c>func (s requestFileSystem) lookupPath(path string) requestPathLookup</c>（requestfilesystem.go:371-400）：
    /// 先查本地树，再跟随符号链接解析；命中宿主时给出回落到哪个 FS。</summary>
    internal RequestPathLookup LookupPath(string path)
    {
        var absolutePath = ToAbsolutePath(path);
        var (info, pathFallback) = LocalPathInfo(absolutePath);
        if (info is not null)
        {
            return new RequestPathLookup { Path = absolutePath, Info = info, Ok = true };
        }

        if (pathFallback == RequestFallback.Missing)
        {
            return new RequestPathLookup();
        }

        var resolved = ResolvePath(path);
        if (!resolved.Ok)
        {
            return new RequestPathLookup();
        }

        var result = new RequestPathLookup { Path = resolved.Path, FollowedSymlink = resolved.FollowedSymlink, Ok = true };
        var (resolvedInfo, resolvedFallback) = LocalPathInfo(resolved.Path);
        if (!resolved.Host && resolvedInfo is not null)
        {
            result.Info = resolvedInfo;
        }
        else if (resolved.Host || Kind == Kind.Layer)
        {
            if (resolvedFallback == RequestFallback.Missing)
            {
                return new RequestPathLookup();
            }

            result.FileSystem = Base;
            result.Ok = result.FileSystem is not null;
        }

        return result;
    }

    /// <summary>Go: <c>func (s requestFileSystem) mutationPath(path string) (vfs.FS, string, bool)</c>
    /// （requestfilesystem.go:402-411）：只有 layer 文件系统允许写，且写到底层宿主。</summary>
    internal (FS? FileSystem, string Path, bool Ok) MutationPath(string path)
    {
        if (Kind != Kind.Layer)
        {
            return (null, "", false);
        }

        var resolved = ResolvePath(path);
        if (!resolved.Ok)
        {
            return (null, "", false);
        }

        return (Base, resolved.Path, Base is not null);
    }

    /// <summary>Go: <c>func (s requestFileSystem) UseCaseSensitiveFileNames() bool</c>（requestfilesystem.go:427-429）。</summary>
    public bool UseCaseSensitiveFileNames()
    {
        return UseCaseSensitiveNames;
    }

    /// <summary>Go: <c>func (s requestFileSystem) GetFile(fileName string) project.FileHandle</c>（requestfilesystem.go:431-433）。</summary>
    public FileHandle? GetFile(string fileName)
    {
        return GetFileByPath(fileName, ToPath(fileName));
    }

    /// <summary>Go: <c>func (s requestFileSystem) GetFileByPath(fileName string, _ tspath.Path) project.FileHandle</c>
    /// （requestfilesystem.go:435-453）。</summary>
    public FileHandle? GetFileByPath(string fileName, Path path)
    {
        var lookup = LookupPath(fileName);
        if (!lookup.Ok || (lookup.Info is not null && lookup.Info.IsDir()))
        {
            return null;
        }

        if (lookup.FileSystem is not null)
        {
            if (lookup.FileSystem is FileHandleSource source)
            {
                return source.GetFile(lookup.Path);
            }

            if (lookup.FileSystem.TryReadFile(lookup.Path, out var content))
            {
                return CachedFile.NewCachedFileHandle(fileName, content);
            }

            return null;
        }

        if (lookup.Info is RequestFileEntry file)
        {
            return CachedFile.NewCachedFileHandle(fileName, file.Content);
        }

        return null;
    }

    /// <summary>Go: <c>func (s requestFileSystem) ReadFile(fileName string) (string, bool)</c>（requestfilesystem.go:455-467）。</summary>
    public bool TryReadFile(string path, out string contents)
    {
        var lookup = LookupPath(path);
        if (!lookup.Ok || (lookup.Info is not null && lookup.Info.IsDir()))
        {
            contents = "";
            return false;
        }

        if (lookup.FileSystem is not null)
        {
            return lookup.FileSystem.TryReadFile(lookup.Path, out contents);
        }

        if (lookup.Info is RequestFileEntry file)
        {
            contents = file.Content;
            return true;
        }

        contents = "";
        return false;
    }

    /// <summary>Go: <c>func (s requestFileSystem) FileExists(fileName string) bool</c>（requestfilesystem.go:469-475）。</summary>
    public bool FileExists(string path)
    {
        var lookup = LookupPath(path);
        if (!lookup.Ok || (lookup.Info is not null && lookup.Info.IsDir()))
        {
            return false;
        }

        return lookup.Info is not null
            || (lookup.FileSystem is not null && lookup.FileSystem.FileExists(lookup.Path));
    }

    /// <summary>Go: <c>func (s requestFileSystem) DirectoryExists(directoryName string) bool</c>（requestfilesystem.go:477-483）。</summary>
    public bool DirectoryExists(string path)
    {
        var lookup = LookupPath(path);
        if (!lookup.Ok || (lookup.Info is not null && !lookup.Info.IsDir()))
        {
            return false;
        }

        return lookup.Info is not null
            || (lookup.FileSystem is not null && lookup.FileSystem.DirectoryExists(lookup.Path));
    }

    /// <summary>Go: <c>func (s requestFileSystem) GetAccessibleEntries(directoryName string) vfs.Entries</c>
    /// （requestfilesystem.go:485-504）。</summary>
    public Entries GetAccessibleEntries(string directoryName)
    {
        var lookup = LookupPath(directoryName);
        if (!lookup.Ok || (lookup.Info is not null && !lookup.Info.IsDir()))
        {
            return new Entries([], [], new HashSet<string>());
        }

        Entries result;
        if (lookup.FileSystem is not null)
        {
            result = RemoveEntries(lookup.Path, lookup.FileSystem.GetAccessibleEntries(lookup.Path));
        }
        else
        {
            var (localEntries, @explicit, _) = GetLocalEntries(lookup.Path);
            result = localEntries;
            if (Kind == Kind.Layer && !@explicit && !BlocksFallback(directoryName) && !BlocksFallback(lookup.Path))
            {
                result = RemoveEntries(lookup.Path, BaseFileSystem().GetAccessibleEntries(lookup.Path));
                result = MergeEntries(result, localEntries, UseCaseSensitiveNames);
            }

            result = AddSymlinkEntries(lookup.Path, result);
        }

        return FilterLocalEntries(directoryName, result);
    }

    /// <summary>Go: <c>func (s requestFileSystem) filterLocalEntries(directoryName string, entries vfs.Entries) vfs.Entries</c>
    /// （requestfilesystem.go:506-525）：剔除"既不在本地、又被 removedPaths 挡掉"的条目。</summary>
    internal Entries FilterLocalEntries(string directoryName, Entries entries)
    {
        var result = CloneEntries(entries);
        result.Files = Filter(result.Files);
        result.Directories = Filter(result.Directories);
        var symlinks = result.Symlinks;
        if (symlinks is not null)
        {
            foreach (var name in symlinks.ToArray())
            {
                if (Filter([name]).Length == 0)
                {
                    symlinks.Remove(name);
                }
            }
        }

        return result;

        string[] Filter(string[] values)
        {
            var write = 0;
            for (var read = 0; read < values.Length; read++)
            {
                var fileName = CombinePaths(directoryName, values[read]);
                var (info, _) = LocalPathInfo(fileName);
                if (info is null && BlocksFallback(fileName))
                {
                    continue;
                }

                values[write++] = values[read];
            }

            if (write == values.Length)
            {
                return values;
            }

            var trimmed = new string[write];
            Array.Copy(values, trimmed, write);
            return trimmed;
        }
    }

    /// <summary>Go: <c>func (s requestFileSystem) getLocalEntries(directoryName string) (entries vfs.Entries, explicit bool, ok bool)</c>
    /// （requestfilesystem.go:527-536）。</summary>
    internal (Entries Entries, bool Explicit, bool Ok) GetLocalEntries(string directoryName)
    {
        var (node, _) = RequestPathNode.Lookup(Paths, ToPath(directoryName));
        var (entries, ok) = RequestPathNode.Entries(node);
        var @explicit = false;
        if (node?.Entry is RequestDirectoryEntry directory)
        {
            @explicit = directory.Listing is not null;
        }

        return (entries, @explicit, ok);
    }

    /// <summary>Go: <c>func (s requestFileSystem) removeEntries(directoryName string, entries vfs.Entries) vfs.Entries</c>
    /// （requestfilesystem.go:578-593）：剔除被 removedPaths 挡掉的条目。</summary>
    internal Entries RemoveEntries(string directoryName, Entries entries)
    {
        var result = CloneEntries(entries);
        result.Files = Filter(result.Files);
        result.Directories = Filter(result.Directories);
        var symlinks = result.Symlinks;
        if (symlinks is not null)
        {
            foreach (var name in symlinks.ToArray())
            {
                if (BlocksFallback(CombinePaths(directoryName, name)))
                {
                    symlinks.Remove(name);
                }
            }
        }

        return result;

        string[] Filter(string[] values)
        {
            var write = 0;
            for (var read = 0; read < values.Length; read++)
            {
                if (!BlocksFallback(CombinePaths(directoryName, values[read])))
                {
                    values[write++] = values[read];
                }
            }

            if (write == values.Length)
            {
                return values;
            }

            var trimmed = new string[write];
            Array.Copy(values, trimmed, write);
            return trimmed;
        }
    }

    /// <summary>Go: <c>func (s requestFileSystem) addSymlinkEntries(directoryName string, entries vfs.Entries) vfs.Entries</c>
    /// （requestfilesystem.go:595-633）：把该目录下直接子级的符号链接补进列举结果。</summary>
    internal Entries AddSymlinkEntries(string directoryName, Entries entries)
    {
        var result = CloneEntries(entries);
        var symlinks = result.Symlinks;
        if (symlinks is null)
        {
            symlinks = [];
            result.Symlinks = symlinks;
        }

        var (node, _) = RequestPathNode.Lookup(Paths, ToPath(directoryName));
        var links = new List<RequestSymlinkEntry>();
        var children = node?.Children;
        if (children is not null)
        {
            foreach (var child in children.Values)
            {
                if (child.Entry is RequestSymlinkEntry symlink)
                {
                    links.Add(symlink);
                }
            }
        }

        if (links.Count == 0)
        {
            return result;
        }

        foreach (var symlink in links)
        {
            var name = GetBaseFileName(symlink.LinkName);
            result.Files = DeleteEntryName(result.Files, name);
            result.Directories = DeleteEntryName(result.Directories, name);
            foreach (var existingName in symlinks.ToArray())
            {
                if (EqualEntryNames(existingName, name))
                {
                    symlinks.Remove(existingName);
                }
            }

            if (DirectoryExists(symlink.LinkName))
            {
                result.Directories = [.. result.Directories, name];
                symlinks.Add(name);
            }
            else if (FileExists(symlink.LinkName))
            {
                result.Files = [.. result.Files, name];
                symlinks.Add(name);
            }
        }

        Array.Sort(result.Files, StringComparer.Ordinal);
        Array.Sort(result.Directories, StringComparer.Ordinal);
        return result;
    }

    /// <summary>Go: <c>func (s requestFileSystem) deleteEntryName(values []string, value string) []string</c>
    /// （requestfilesystem.go:635-637）。</summary>
    internal string[] DeleteEntryName(string[] values, string value)
    {
        return PathTree.DeleteEqualNames(values, value, UseCaseSensitiveNames);
    }

    /// <summary>Go: <c>func (s requestFileSystem) equalEntryNames(left string, right string) bool</c>（requestfilesystem.go:639-641）。</summary>
    internal bool EqualEntryNames(string left, string right)
    {
        return PathTree.EqualNames(left, right, UseCaseSensitiveNames);
    }

    /// <summary>Go: <c>func (s requestFileSystem) Realpath(path string) string</c>（requestfilesystem.go:643-655）。</summary>
    public string Realpath(string path)
    {
        var lookup = LookupPath(path);
        if (!lookup.Ok)
        {
            return path;
        }

        if (lookup.FileSystem is not null)
        {
            return lookup.FileSystem.Realpath(lookup.Path);
        }

        if (lookup.Info is not null || !lookup.FollowedSymlink)
        {
            return lookup.Path;
        }

        return path;
    }

    /// <summary>Go: <c>func (s requestFileSystem) WriteFile(fileName string, data string) error</c>（requestfilesystem.go:657-663）。</summary>
    public void WriteFile(string path, string data)
    {
        var (host, target, ok) = MutationPath(path);
        if (!ok)
        {
            throw new ArgumentException("invalid argument");
        }

        host!.WriteFile(target, data);
    }

    /// <summary>Go: <c>func (s requestFileSystem) AppendFile(fileName string, data string) error</c>（requestfilesystem.go:665-671）。</summary>
    public void AppendFile(string path, string data)
    {
        var (host, target, ok) = MutationPath(path);
        if (!ok)
        {
            throw new ArgumentException("invalid argument");
        }

        host!.AppendFile(target, data);
    }

    /// <summary>Go: <c>func (s requestFileSystem) Remove(path string) error</c>（requestfilesystem.go:673-679）。</summary>
    public void Remove(string path)
    {
        var (host, target, ok) = MutationPath(path);
        if (!ok)
        {
            throw new ArgumentException("invalid argument");
        }

        host!.Remove(target);
    }

    /// <summary>Go: <c>func (s requestFileSystem) Chtimes(path string, aTime time.Time, mTime time.Time) error</c>
    /// （requestfilesystem.go:681-687）。</summary>
    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        var (host, target, ok) = MutationPath(path);
        if (!ok)
        {
            throw new ArgumentException("invalid argument");
        }

        host!.Chtimes(target, accessTime, modificationTime);
    }

    /// <summary>Go: <c>func (s requestFileSystem) Stat(path string) vfs.FileInfo</c>（requestfilesystem.go:689-698）。</summary>
    public VfsFileInfo? Stat(string path)
    {
        var lookup = LookupPath(path);
        if (!lookup.Ok)
        {
            return null;
        }

        if (lookup.FileSystem is not null)
        {
            return StatFileSystem(lookup.FileSystem, lookup.Path);
        }

        return lookup.Info.FileInfo();
    }

    /// <summary>Go 的 <c>strings.TrimPrefix(s, "/")</c>。</summary>
    private static string TrimPrefixSlash(string value)
    {
        return value.StartsWith('/') ? value[1..] : value;
    }
}

/// <summary>Go 的 <c>type resolvedRequestPath struct</c>（requestfilesystem.go:68-73）。</summary>
internal sealed class ResolvedRequestPath
{
    /// <summary>Go: <c>path</c>（解析后的绝对路径）。</summary>
    internal string Path = "";

    /// <summary>Go: <c>followedSymlink</c>：路径是否经过符号链接。</summary>
    internal bool FollowedSymlink;

    /// <summary>Go: <c>host</c>：是否落到宿主文件系统。</summary>
    internal bool Host;

    /// <summary>Go: <c>ok</c>：解析是否有效（false 表示路径无效，例如穿过普通文件或链接环）。</summary>
    internal bool Ok;
}

/// <summary>Go 的 <c>type requestPathLookup struct</c>（requestfilesystem.go:75-81，见文件头第 8 条：
/// `info` 在 C# 侧存 entry，因为 `Vfs.FileInfo` 是 sealed 数据类）。</summary>
internal sealed class RequestPathLookup
{
    /// <summary>Go: <c>path</c>。</summary>
    internal string Path = "";

    /// <summary>Go: <c>info vfs.FileInfo</c>（本地命中时的条目；宿主命中或无效时为 null）。</summary>
    internal IRequestEntry? Info;

    /// <summary>Go: <c>fileSystem vfs.FS</c>（非 nil 表示应从底层 FS 读）。</summary>
    internal FS? FileSystem;

    /// <summary>Go: <c>followedSymlink</c>。</summary>
    internal bool FollowedSymlink;

    /// <summary>Go: <c>ok</c>。</summary>
    internal bool Ok;
}

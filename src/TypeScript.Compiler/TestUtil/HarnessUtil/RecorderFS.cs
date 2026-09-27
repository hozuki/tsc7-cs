// ported from: internal/testutil/harnessutil/recorderfs.go
//
// 与 Go 的差异：
// 1) Go 的 `OutputRecorderFS` 用**结构体嵌入** `vfs.FS` 自动转发其余方法；C# 的接口没有嵌入，逐个方法显式委托
//    （与 Vfs/WrapVfs/WrapVfs.cs 的 WrappedFS 同款）。只有 `WriteFile` 改写，`Outputs` 是新增的读取入口。
// 2) Go 的 `outputsMap` 初值是 nil map（首次写入才 make）；C# 在字段初始化器里就建空字典（nil map 的读语义
//    与空字典一致，写语义这里也等价）。
// 3) `outputsMut sync.Mutex` → C# 的 `lock (_outputsMut)`（`object` 锁对象）。
// 4) `slices.Clone(fs.outputs)` → `[.. _outputs]`（浅拷贝，与 Go 一致：元素是 *TestFile 指针）。
namespace TypeScript.TestUtil.HarnessUtil;

using TypeScript.Vfs;

/// <summary>Go: <c>type OutputRecorderFS struct { vfs.FS; outputsMut sync.Mutex; outputsMap map[string]int; outputs []*TestFile }</c>（recorderfs.go:10）。</summary>
public sealed class OutputRecorderFS(FS fs) : FS
{
    private readonly FS _fs = fs;
    private readonly object _outputsMut = new();
    private readonly Dictionary<string, int> _outputsMap = new();
    private readonly List<TestFile> _outputs = [];

    public bool UseCaseSensitiveFileNames() => _fs.UseCaseSensitiveFileNames();

    public bool FileExists(string path) => _fs.FileExists(path);

    public bool TryReadFile(string path, out string contents) => _fs.TryReadFile(path, out contents);

    /// <summary>Go: <c>func (fs *OutputRecorderFS) WriteFile(path string, data string) error</c>（recorderfs.go:21）。</summary>
    public void WriteFile(string path, string data)
    {
        _fs.WriteFile(path, data);
        path = _fs.Realpath(path);
        lock (_outputsMut)
        {
            if (_outputsMap.TryGetValue(path, out var index))
            {
                _outputs[index] = new TestFile { UnitName = path, Content = data };
            }
            else
            {
                _outputsMap[path] = _outputs.Count;
                _outputs.Add(new TestFile { UnitName = path, Content = data });
            }
        }
    }

    public void AppendFile(string path, string data) => _fs.AppendFile(path, data);

    public void Remove(string path) => _fs.Remove(path);

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime) => _fs.Chtimes(path, accessTime, modificationTime);

    public bool DirectoryExists(string path) => _fs.DirectoryExists(path);

    public Entries GetAccessibleEntries(string path) => _fs.GetAccessibleEntries(path);

    public FileInfo? Stat(string path) => _fs.Stat(path);

    public string Realpath(string path) => _fs.Realpath(path);

    /// <summary>Go: <c>func (fs *OutputRecorderFS) Outputs() []*TestFile</c>（recorderfs.go:41）。</summary>
    public TestFile[] Outputs()
    {
        lock (_outputsMut)
        {
            return [.. _outputs];
        }
    }
}

public static partial class HarnessUtil
{
    /// <summary>Go: <c>func NewOutputRecorderFS(fs vfs.FS) vfs.FS</c>（recorderfs.go:17）。</summary>
    public static FS NewOutputRecorderFS(FS fs) => new OutputRecorderFS(fs);
}

// ported from: internal/vfs/vfsmock/mock_generated.go
//
// Go 侧由 github.com/matryer/moq 生成（生成物文件头标明 DO NOT EDIT）；C# 侧没有 AOT 安全的 mock 生成器，手工移植，
// 结构与生成物逐行对应：每个方法一个 Func/Action 字段（名字照抄 Go 的 XXXFunc）+ 一个 XXXCalls() 访问器。
//
// 偏离：
//   - Go 的匿名结构体调用记录 → C# 值元组数组；访问器返回**拷贝**（Go 返回的是别名切片）。
//   - Go 的 error 返回值 → 异常（字段类型因此是 Action/Func，而不是 Func<..., error>）。
//   - Go 的 <c>panic("FSMock.XFunc: method is nil …")</c> → <c>Debug.Fail</c>（§3.2）；Debug.Fail 必抛，
//     所以它之后的委托调用点用 `!` 标注"已证明非空"（与 Go 的 panic 后继续执行是同一语义）。
// 文件名以 .Generated.cs 结尾 ⇒ Roslyn 按"生成代码"处理，不继承项目的 nullable 上下文，必须显式声明。
#nullable enable

namespace TypeScript.Vfs.VfsMock;

using TypeScript.Vfs;

public sealed class FSMock : FS
{
    /// <summary>Go: <c>AppendFileFunc</c>。</summary>
    public Action<string, string>? AppendFileFunc;

    /// <summary>Go: <c>ChtimesFunc</c>。</summary>
    public Action<string, DateTime, DateTime>? ChtimesFunc;

    /// <summary>Go: <c>DirectoryExistsFunc</c>。</summary>
    public Func<string, bool>? DirectoryExistsFunc;

    /// <summary>Go: <c>FileExistsFunc</c>。</summary>
    public Func<string, bool>? FileExistsFunc;

    /// <summary>Go: <c>GetAccessibleEntriesFunc</c>。</summary>
    public Func<string, Entries>? GetAccessibleEntriesFunc;

    /// <summary>Go: <c>ReadFileFunc</c>（接口方法在 C# 里叫 <see cref="TryReadFile"/>）。</summary>
    public TryReadFileFunc? ReadFileFunc;

    /// <summary>Go: <c>RealpathFunc</c>。</summary>
    public Func<string, string>? RealpathFunc;

    /// <summary>Go: <c>RemoveFunc</c>。</summary>
    public Action<string>? RemoveFunc;

    /// <summary>Go: <c>StatFunc</c>。</summary>
    public Func<string, FileInfo?>? StatFunc;

    /// <summary>Go: <c>UseCaseSensitiveFileNamesFunc</c>。</summary>
    public Func<bool>? UseCaseSensitiveFileNamesFunc;

    /// <summary>Go: <c>WriteFileFunc</c>。</summary>
    public Action<string, string>? WriteFileFunc;

    private readonly object _callsLock = new();
    private readonly List<(string Path, string Data)> _appendFileCalls = [];
    private readonly List<(string Path, DateTime ATime, DateTime MTime)> _chtimesCalls = [];
    private readonly List<string> _directoryExistsCalls = [];
    private readonly List<string> _fileExistsCalls = [];
    private readonly List<string> _getAccessibleEntriesCalls = [];
    private readonly List<string> _readFileCalls = [];
    private readonly List<string> _realpathCalls = [];
    private readonly List<string> _removeCalls = [];
    private readonly List<string> _statCalls = [];
    private int _useCaseSensitiveFileNamesCalls;
    private readonly List<(string Path, string Data)> _writeFileCalls = [];

    /// <summary>Go: <c>FSMock.AppendFile</c>。</summary>
    public void AppendFile(string path, string data)
    {
        if (AppendFileFunc == null)
        {
            Debug.Fail("FSMock.AppendFileFunc: method is nil but FS.AppendFile was just called");
        }

        lock (_callsLock)
        {
            _appendFileCalls.Add((path, data));
        }

        AppendFileFunc!(path, data);
    }

    /// <summary>Go: <c>FSMock.AppendFileCalls()</c>。</summary>
    public (string Path, string Data)[] AppendFileCalls()
    {
        lock (_callsLock)
        {
            return _appendFileCalls.ToArray();
        }
    }

    /// <summary>Go: <c>FSMock.Chtimes</c>。</summary>
    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        if (ChtimesFunc == null)
        {
            Debug.Fail("FSMock.ChtimesFunc: method is nil but FS.Chtimes was just called");
        }

        lock (_callsLock)
        {
            _chtimesCalls.Add((path, accessTime, modificationTime));
        }

        ChtimesFunc!(path, accessTime, modificationTime);
    }

    /// <summary>Go: <c>FSMock.ChtimesCalls()</c>。</summary>
    public (string Path, DateTime ATime, DateTime MTime)[] ChtimesCalls()
    {
        lock (_callsLock)
        {
            return _chtimesCalls.ToArray();
        }
    }

    /// <summary>Go: <c>FSMock.DirectoryExists</c>。</summary>
    public bool DirectoryExists(string path)
    {
        if (DirectoryExistsFunc == null)
        {
            Debug.Fail("FSMock.DirectoryExistsFunc: method is nil but FS.DirectoryExists was just called");
        }

        lock (_callsLock)
        {
            _directoryExistsCalls.Add(path);
        }

        return DirectoryExistsFunc!(path);
    }

    /// <summary>Go: <c>FSMock.DirectoryExistsCalls()</c>。</summary>
    public string[] DirectoryExistsCalls()
    {
        lock (_callsLock)
        {
            return _directoryExistsCalls.ToArray();
        }
    }

    /// <summary>Go: <c>FSMock.FileExists</c>。</summary>
    public bool FileExists(string path)
    {
        if (FileExistsFunc == null)
        {
            Debug.Fail("FSMock.FileExistsFunc: method is nil but FS.FileExists was just called");
        }

        lock (_callsLock)
        {
            _fileExistsCalls.Add(path);
        }

        return FileExistsFunc!(path);
    }

    /// <summary>Go: <c>FSMock.FileExistsCalls()</c>。</summary>
    public string[] FileExistsCalls()
    {
        lock (_callsLock)
        {
            return _fileExistsCalls.ToArray();
        }
    }

    /// <summary>Go: <c>FSMock.GetAccessibleEntries</c>。</summary>
    public Entries GetAccessibleEntries(string path)
    {
        if (GetAccessibleEntriesFunc == null)
        {
            Debug.Fail("FSMock.GetAccessibleEntriesFunc: method is nil but FS.GetAccessibleEntries was just called");
        }

        lock (_callsLock)
        {
            _getAccessibleEntriesCalls.Add(path);
        }

        return GetAccessibleEntriesFunc!(path);
    }

    /// <summary>Go: <c>FSMock.GetAccessibleEntriesCalls()</c>。</summary>
    public string[] GetAccessibleEntriesCalls()
    {
        lock (_callsLock)
        {
            return _getAccessibleEntriesCalls.ToArray();
        }
    }

    /// <summary>Go: <c>FSMock.ReadFile</c>。</summary>
    public bool TryReadFile(string path, out string contents)
    {
        if (ReadFileFunc == null)
        {
            Debug.Fail("FSMock.ReadFileFunc: method is nil but FS.ReadFile was just called");
        }

        lock (_callsLock)
        {
            _readFileCalls.Add(path);
        }

        return ReadFileFunc!(path, out contents);
    }

    /// <summary>Go: <c>FSMock.ReadFileCalls()</c>。</summary>
    public string[] ReadFileCalls()
    {
        lock (_callsLock)
        {
            return _readFileCalls.ToArray();
        }
    }

    /// <summary>Go: <c>FSMock.Realpath</c>。</summary>
    public string Realpath(string path)
    {
        if (RealpathFunc == null)
        {
            Debug.Fail("FSMock.RealpathFunc: method is nil but FS.Realpath was just called");
        }

        lock (_callsLock)
        {
            _realpathCalls.Add(path);
        }

        return RealpathFunc!(path);
    }

    /// <summary>Go: <c>FSMock.RealpathCalls()</c>。</summary>
    public string[] RealpathCalls()
    {
        lock (_callsLock)
        {
            return _realpathCalls.ToArray();
        }
    }

    /// <summary>Go: <c>FSMock.Remove</c>。</summary>
    public void Remove(string path)
    {
        if (RemoveFunc == null)
        {
            Debug.Fail("FSMock.RemoveFunc: method is nil but FS.Remove was just called");
        }

        lock (_callsLock)
        {
            _removeCalls.Add(path);
        }

        RemoveFunc!(path);
    }

    /// <summary>Go: <c>FSMock.RemoveCalls()</c>。</summary>
    public string[] RemoveCalls()
    {
        lock (_callsLock)
        {
            return _removeCalls.ToArray();
        }
    }

    /// <summary>Go: <c>FSMock.Stat</c>。</summary>
    public FileInfo? Stat(string path)
    {
        if (StatFunc == null)
        {
            Debug.Fail("FSMock.StatFunc: method is nil but FS.Stat was just called");
        }

        lock (_callsLock)
        {
            _statCalls.Add(path);
        }

        return StatFunc!(path);
    }

    /// <summary>Go: <c>FSMock.StatCalls()</c>。</summary>
    public string[] StatCalls()
    {
        lock (_callsLock)
        {
            return _statCalls.ToArray();
        }
    }

    /// <summary>Go: <c>FSMock.UseCaseSensitiveFileNames</c>。</summary>
    public bool UseCaseSensitiveFileNames()
    {
        if (UseCaseSensitiveFileNamesFunc == null)
        {
            Debug.Fail("FSMock.UseCaseSensitiveFileNamesFunc: method is nil but FS.UseCaseSensitiveFileNames was just called");
        }

        lock (_callsLock)
        {
            _useCaseSensitiveFileNamesCalls++;
        }

        return UseCaseSensitiveFileNamesFunc!();
    }

    /// <summary>Go: <c>FSMock.UseCaseSensitiveFileNamesCalls()</c>（Go 返回 []struct{}，调用点只用 len()，
    /// 所以 C# 直接返回调用次数）。</summary>
    public int UseCaseSensitiveFileNamesCalls()
    {
        lock (_callsLock)
        {
            return _useCaseSensitiveFileNamesCalls;
        }
    }

    /// <summary>Go: <c>FSMock.WriteFile</c>。</summary>
    public void WriteFile(string path, string data)
    {
        if (WriteFileFunc == null)
        {
            Debug.Fail("FSMock.WriteFileFunc: method is nil but FS.WriteFile was just called");
        }

        lock (_callsLock)
        {
            _writeFileCalls.Add((path, data));
        }

        WriteFileFunc!(path, data);
    }

    /// <summary>Go: <c>FSMock.WriteFileCalls()</c>。</summary>
    public (string Path, string Data)[] WriteFileCalls()
    {
        lock (_callsLock)
        {
            return _writeFileCalls.ToArray();
        }
    }
}

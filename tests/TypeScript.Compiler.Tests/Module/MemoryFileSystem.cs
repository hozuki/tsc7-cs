// 测试夹具：module 解析器需要的宿主桩与两个"闸门 FS"包装器。
//
// 内存文件系统直接用 Vfs 切片的 `TypeScript.Vfs.VfsTest.VfsTest.FromMap`（对应 Go 的
// `internal/vfs/vfstest.FromMap`，Go 测试里也是用它）；本文件只放测试专用的宿主桩，
// 以及 blockingFS / flipFileExistsFS 的等价物（用于确定性地复现 package.json info-cache 的竞态）。
using TypeScript.Module;
using TypeScript.Vfs;

namespace TypeScript.Tests;

/// <summary>Go 的 <c>resolutionHostStub</c>。</summary>
internal sealed class ResolutionHostStub(FS fs, string cwd) : ResolutionHost
{
    public FS FS() => fs;

    public string GetCurrentDirectory() => cwd;
}

/// <summary>
/// Go 的 <c>blockingFS</c>：把 <c>targetPath</c> 上的 FileExists 挡在闸门后面，用来确定性地复现
/// package.json info-cache 的插入竞态（https://github.com/microsoft/TypeScript/tsc/issues/3526）。
/// </summary>
internal sealed class BlockingFileSystem(FS inner, string targetPath) : FS
{
    private readonly SemaphoreSlim _arrived = new(0);
    private readonly ManualResetEventSlim _gate = new(false);

    internal SemaphoreSlim Arrived => _arrived;

    internal void ReleaseGate() => _gate.Set();

    public bool FileExists(string path)
    {
        if (path == targetPath)
        {
            _arrived.Release();
            FileSystemTestGates.Wait(_gate, "FileExists gate");
        }

        return inner.FileExists(path);
    }

    public bool UseCaseSensitiveFileNames() => inner.UseCaseSensitiveFileNames();

    public bool TryReadFile(string path, out string contents) => inner.TryReadFile(path, out contents);

    public bool DirectoryExists(string path) => inner.DirectoryExists(path);

    public string Realpath(string path) => inner.Realpath(path);

    public Entries GetAccessibleEntries(string path) => inner.GetAccessibleEntries(path);

    public void WriteFile(string path, string data) => inner.WriteFile(path, data);

    public void AppendFile(string path, string data) => inner.AppendFile(path, data);

    public void Remove(string path) => inner.Remove(path);

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime) => inner.Chtimes(path, accessTime, modificationTime);

    public Vfs.FileInfo? Stat(string path) => inner.Stat(path);
}

/// <summary>
/// Go 的 <c>flipFileExistsFS</c>：对 <c>targetPath</c> 的第一次 FileExists 返回 false、第二次返回真实结果，
/// 两次都在各自的闸门处等待；ReadFile 也在自己的闸门处等待。用来复现 "nil Contents 被 LoadOrStore 交给
/// 后来的调用方" 的竞态（https://github.com/microsoft/TypeScript/tsc/issues/1290）。
/// </summary>
internal sealed class FlipFileExistsFileSystem(FS inner, string targetPath) : FS
{
    private int _callCount;
    private readonly SemaphoreSlim _firstArrived = new(0);
    private readonly SemaphoreSlim _secondArrived = new(0);
    private readonly ManualResetEventSlim _firstGate = new(false);
    private readonly ManualResetEventSlim _secondGate = new(false);
    private readonly SemaphoreSlim _readArrived = new(0);
    private readonly ManualResetEventSlim _readGate = new(false);

    internal SemaphoreSlim FirstArrived => _firstArrived;

    internal SemaphoreSlim SecondArrived => _secondArrived;

    internal SemaphoreSlim ReadArrived => _readArrived;

    internal void ReleaseFirstGate() => _firstGate.Set();

    internal void ReleaseSecondGate() => _secondGate.Set();

    internal void ReleaseReadGate() => _readGate.Set();

    public bool FileExists(string path)
    {
        if (path == targetPath)
        {
            var callCount = Interlocked.Increment(ref _callCount);
            if (callCount == 1)
            {
                _firstArrived.Release();
                FileSystemTestGates.Wait(_firstGate, "first FileExists gate");
                return false; // first caller: simulate "file not yet visible"
            }

            if (callCount == 2)
            {
                _secondArrived.Release();
                FileSystemTestGates.Wait(_secondGate, "second FileExists gate");
                return inner.FileExists(path); // second caller: file is visible
            }
        }

        return inner.FileExists(path);
    }

    public bool TryReadFile(string path, out string contents)
    {
        if (path == targetPath)
        {
            _readArrived.Release();
            FileSystemTestGates.Wait(_readGate, "ReadFile gate");
        }

        return inner.TryReadFile(path, out contents);
    }

    public bool UseCaseSensitiveFileNames() => inner.UseCaseSensitiveFileNames();

    public bool DirectoryExists(string path) => inner.DirectoryExists(path);

    public string Realpath(string path) => inner.Realpath(path);

    public Entries GetAccessibleEntries(string path) => inner.GetAccessibleEntries(path);

    public void WriteFile(string path, string data) => inner.WriteFile(path, data);

    public void AppendFile(string path, string data) => inner.AppendFile(path, data);

    public void Remove(string path) => inner.Remove(path);

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime) => inner.Chtimes(path, accessTime, modificationTime);

    public Vfs.FileInfo? Stat(string path) => inner.Stat(path);
}

/// <summary>
/// 把 Go 测试里的"等同步点 + 超时即失败"落成 C#：Go 用 <c>select</c> + <c>t.Context().Done()</c>，
/// 这里用 <see cref="ManualResetEventSlim.Wait(TimeSpan)"/> 的返回值，避免测试挂死。
/// </summary>
internal static class FileSystemTestGates
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    internal static void Wait(ManualResetEventSlim gate, string description)
    {
        if (!gate.Wait(Timeout))
        {
            throw new TimeoutException($"timed out waiting for {description}");
        }
    }
}

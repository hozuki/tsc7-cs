// ported from: internal/fswatch/windows.go
//
// C# 映射与偏离（逐条）：
// 1) Go 用 `golang.org/x/sys/windows` 的封装（CreateFile/ReadDirectoryChanges/GetOverlappedResult/CancelIoEx…）→
//    C# 直接 `DllImport` kernel32 的同名函数。**不使用 `unsafe`**（本项目未开 AllowUnsafeBlocks）：异步读的
//    OVERLAPPED 结构用 `Marshal.AllocHGlobal(IntPtr.Size * 4)` 自己分配、`hEvent` 写在偏移 `3 * IntPtr.Size`，
//    FILE_NOTIFY_INFORMATION 链用 `Marshal.ReadInt32` 解析（等价于 Go 的 `unsafe.Pointer` 重解释）。
// 2) Go 的 `windows.Overlapped`/`windowsRead.buf` 由 GC 保活，C# 侧是**手动分配的非托管内存**，所以在
//    `GetOverlappedResult` 返回后必须显式释放（`WindowsRead.Dispose`，已确认内核不再引用它们）。
// 3) Go 的 `CreateEvent(nil, 1, 0, nil)` 手动重置事件 → `CreateEventW` + 用完 `CloseHandle`（Go 里是
//    `windows.CloseHandle(r.event)`）。
// 4) Go 的 `chan struct{}` 停止信号 → `ManualResetEventSlim`；Go 里"select stopCh / stopWait"的守护 goroutine →
//    `Task.Run` + `WaitHandle.WaitAny([stopEvent, stopWait])`。
// 5) Go 的 `windowsRead.wait` 返回值 `(uint32, error, error)` → C# 元组 `(uint Bytes, Exception? WaitErr,
//    Exception? CompletionErr)`；完成错误用 `Win32Exception`（`NativeErrorCode` 就是 errno，对应 Go 的
//    `errors.Is(err, windows.ERROR_X)`，本文件的 `IsWinError` 沿 InnerException 链查 NativeErrorCode）。
// 6) Go 的 `windows.UTF16ToString(nameSlice)`（遇到 NUL 截断、孤立代理项替换成 U+FFFD）→ C# 的
//    `ReadNotifyName`（按 FILE_NOTIFY_INFORMATION.FileNameLength/2 个代码单元逐个取、遇到 NUL 停止；**不做**
//    U+FFFD 替换，因为 C# 字符串本就是 UTF-16 代码单元序列，保留原样更贴近 Win32 契约）。
// 7) Go 的 `windows.GetFileAttributes` / `GetFileAttributesEx` 判定（MODIFIED 分支与 ACCESS_DENIED 分支）→
//    C# 的 `File.GetAttributes`（同一 Win32 调用的托管封装）；失败即"文件已不存在"。
// 8) Go 的 `windowsHandle` 无 C# 对应物（Go 的 `windows.Handle` 是裸 uintptr）→ C# 用裸 `IntPtr` 持有句柄，
//    `Run()` 的 finally 里 `CloseHandle`（与 Go 的 `defer windows.CloseHandle(s.handle)` 一致）；
//    `Stop()` 里的 `CancelIoEx` 用同一个裸值（Go 亦然；句柄已关时该调用只是失败并被忽略）。
// 9) Go 的 `init()`（`windowsWatcher.factory = …`）→ `[ModuleInitializer]`（parser/jsdoc.go 已有同样先例），
//    按 `OperatingSystem.IsWindows()` 守卫（C# 没有构建标签）。
// 10) Go 的 `windowsRead.wait` 会为**每个在途读**另起一个 goroutine（select stopCh → `CancelIoEx(&r.overlapped)`）；
//     C# 里省掉它，只依赖 `stopLocked()` 的 `CancelIoEx(handle, NULL)`（Go 的 stopLocked 也做同样的事，而同一句柄
//     同时只有一个在途读）——否则取消回调会去碰可能已被我们释放的 OVERLAPPED 非托管内存（无 unsafe 的代价）。
//     可观察行为不变：`Stop()` 之后在途读以 ERROR_OPERATION_ABORTED 完成，`run` 线程照样退出。
namespace TypeScript.FsWatch;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal sealed class WindowsBackend : WatcherBase
{
    /// <summary>Go: <c>func init() { windowsWatcher.factory = func() watcherImpl { return newWindowsBackend() } }</c>。</summary>
    [SuppressMessage("Usage", "CA2255", Justification = "Go 的 windows.go init()：模块初始化器等价于「包被加载即注册」（见 Windows.cs 文件头第 9 条）。")]
    [ModuleInitializer]
    internal static void InitializeWindowsWatcher()
    {
        if (OperatingSystem.IsWindows())
        {
            FsWatch.WindowsWatcherInstance.Factory = static () => new WindowsBackend();
        }
    }

    internal WindowsBackend()
    {
        Init(this);
    }

    /// <summary>Go: <c>func (b *windowsBackend) start() error</c>。每个 watch 自带线程，没有共享事件循环要启动。</summary>
    public override void Start() => NotifyStarted();

    /// <summary>Go: <c>func (b *windowsBackend) shutdown()</c>：什么也不做（每个 watch 的线程由 closeWatch 停止）。</summary>
    public override void Shutdown()
    {
    }

    /// <summary>Go: <c>func (b *windowsBackend) subscribe(w *dirWatch) error</c>。</summary>
    public override void Subscribe(DirWatch w)
    {
        var sub = new WindowsSubscription(this, w);

        // 在 subscribe 里**同步**武装第一次 ReadDirectoryChangesW：这样调用方在 subscribe 返回后立刻做的文件操作
        // 一定被观察到（放到 run() 里会与线程启动竞争，偶尔丢掉最初的 create 事件或把它看成多余的 modify）。
        WindowsRead first;
        try
        {
            // subscribe 一定在 stop() 之前调用，因此 beginRead 不可能返回 null（Go 的 s.first 同理）。
            first = sub.BeginRead()!;
        }
        catch
        {
            sub.CloseHandle();
            throw;
        }

        sub.First = first;
        w._state = sub;
        sub.Start();
    }

    /// <summary>Go: <c>func (b *windowsBackend) closeWatch(w *dirWatch) error</c>。停止 watch 线程并等它结束，
    /// 保证返回时目录句柄已关闭。</summary>
    public override void CloseWatch(DirWatch w)
    {
        var sub = w._state as WindowsSubscription;
        w._state = null;
        sub?.StopAndWait();
    }
}

/// <summary>Go: <c>var errGetFileInfo / errReadChanges / errGetOverlappedResult / errUnknown</c>。</summary>
internal static class WindowsErrors
{
    internal static readonly IOException GetFileInfo = new("could not get file information");
    internal static readonly IOException ReadChanges = new("failed to read changes");
    internal static readonly IOException GetOverlappedResult = new("GetOverlappedResult failed");
    internal static readonly IOException Unknown = new("unknown error");
}

internal sealed class WindowsSubscription
{
    private const int _defaultBufSize = 1024 * 1024;
    private const int _networkBufSize = 64 * 1024;

    private const uint _fileListDirectory = 0x0001;
    private const uint _fileShareRead = 0x0000_0001;
    private const uint _fileShareWrite = 0x0000_0002;
    private const uint _fileShareDelete = 0x0000_0004;
    private const uint _openExisting = 3;
    private const uint _fileFlagBackupSemantics = 0x0200_0000;
    private const uint _fileFlagOverlapped = 0x4000_0000;
    private const uint _fileAttributeDirectory = 0x0000_0010;
    private const uint _notifyChangeFilter =
        0x0000_0001 // FILE_NOTIFY_CHANGE_FILE_NAME
        | 0x0000_0002 // FILE_NOTIFY_CHANGE_DIR_NAME
        | 0x0000_0008 // FILE_NOTIFY_CHANGE_SIZE
        | 0x0000_0010; // FILE_NOTIFY_CHANGE_LAST_WRITE

    private const uint _fileActionAdded = 0x0000_0001;
    private const uint _fileActionRemoved = 0x0000_0002;
    private const uint _fileActionModified = 0x0000_0003;
    private const uint _fileActionRenamedOldName = 0x0000_0004;
    private const uint _fileActionRenamedNewName = 0x0000_0005;

    private const int _errorOperationAborted = 995;
    private const int _errorInvalidParameter = 87;
    private const int _errorNotifyEnumDir = 1026;
    private const int _errorAccessDenied = 5;

    private readonly object _mu = new();
    private readonly WindowsBackend _watcherImpl;
    private readonly DirWatch _dirWatch;
    private readonly IntPtr _handle;
    private readonly ManualResetEventSlim _stopEvent = new(false);
    private readonly ManualResetEventSlim _doneEvent = new(false);
    private bool _stopped;
    private int _bufBytes = _defaultBufSize;

    internal WindowsRead? First;

    internal WindowsSubscription(WindowsBackend watcherImpl, DirWatch w)
    {
        _watcherImpl = watcherImpl;
        _dirWatch = w;

        _handle = CreateFileW(
            w._physicalDir,
            _fileListDirectory,
            _fileShareRead | _fileShareWrite | _fileShareDelete,
            IntPtr.Zero,
            _openExisting,
            _fileFlagBackupSemantics | _fileFlagOverlapped,
            IntPtr.Zero);
        if (_handle == _invalidHandleValue)
        {
            throw new DirWatchError(new IOException("invalid handle", new Win32Exception(Marshal.GetLastPInvokeError())), w);
        }

        if (!GetFileInformationByHandle(_handle, out var info))
        {
            CloseHandle();
            throw new DirWatchError(WindowsErrors.GetFileInfo, w);
        }

        if ((info.FileAttributes & _fileAttributeDirectory) == 0)
        {
            CloseHandle();
            throw new DirWatchError(new IOException($"{w._physicalDir}: not a directory"), w);
        }
    }

    internal void CloseHandle()
    {
        if (_handle != IntPtr.Zero && _handle != _invalidHandleValue)
        {
            _ = CloseHandleNative(_handle);
        }
    }

    internal void Start() => _ = Task.Run(Run);

    /// <summary>Go: <c>func (s *windowsSubscription) beginRead() (*windowsRead, error)</c>（停止后返回 null）。</summary>
    internal WindowsRead? BeginRead()
    {
        int bufSize;
        lock (_mu)
        {
            if (_stopped)
            {
                return null;
            }

            bufSize = _bufBytes;
        }

        var read = new WindowsRead(bufSize);
        read.Event = CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
        if (read.Event == IntPtr.Zero)
        {
            read.Dispose();
            throw new DirWatchError(new IOException("CreateEvent failed", new Win32Exception(Marshal.GetLastPInvokeError())), _dirWatch);
        }

        Marshal.WriteIntPtr(read.Overlapped, IntPtr.Size * 3, read.Event);

        var ok = ReadDirectoryChangesW(
            _handle,
            read.Buffer,
            (uint)bufSize,
            _dirWatch._recursive,
            _notifyChangeFilter,
            out _,
            read.Overlapped,
            IntPtr.Zero);

        if (!ok)
        {
            var errno = Marshal.GetLastPInvokeError();
            if (errno != _errorIoPending)
            {
                read.Dispose();
                throw new DirWatchError(new IOException("failed to read changes", new Win32Exception(errno)), _dirWatch);
            }
        }

        return read;
    }

    private const int _errorIoPending = 997;

    /// <summary>
    /// Go: <c>func (r *windowsRead) wait(s *windowsSubscription) (uint32, error, error)</c>。
    /// 偏离：Go 为每个读额外起一个 goroutine 监听 stopCh 并 <c>CancelIoEx(&amp;r.overlapped)</c>；C# 侧只依赖
    /// <see cref="StopLocked"/> 的 <c>CancelIoEx(handle, NULL)</c>（Go 的 stopLocked 同样这么做，且同一句柄同时
    /// 只有一个在途读）。这样取消回调不会去碰可能已被释放的 OVERLAPPED 非托管内存（无 unsafe 的代价）。
    /// </summary>
    internal (uint Bytes, Exception? WaitErr, Exception? CompletionErr) WaitAndComplete(WindowsRead read)
    {
        var waitResult = WaitForSingleObject(read.Event, _infinite);
        Exception? waitErr = waitResult == _waitObject0
            ? null
            : new Win32Exception(Marshal.GetLastPInvokeError());

        var completed = GetOverlappedResult(_handle, read.Overlapped, out var bytes, false);
        Exception? completionErr = completed
            ? null
            : new Win32Exception(Marshal.GetLastPInvokeError());

        _ = CloseHandleNative(read.Event);
        read.Event = IntPtr.Zero;
        return (bytes, waitErr, completionErr);
    }

    /// <summary>
    /// Go: <c>func (s *windowsSubscription) run()</c>，每 watch 一个 goroutine（C# 侧是一条 Task）。
    /// 目录句柄在本方法（而不是 stop()）里关闭：保证在途的 ReadDirectoryChangesW 已完成、GetOverlappedResult
    /// 已返回之后句柄才失效（Windows 上另一个线程中途关闭句柄是未定义行为）。
    /// </summary>
    private void Run()
    {
        try
        {
            if (First is null)
            {
                // subscribe 一定会在起线程前武装第一次读；这里守护这个不变量，而不是静默产出一个既不投事件
                // 也不报错的 watch。
                Fatal(new DirWatchError(new IOException("fswatch: windows: missing initial read"), _dirWatch));
                return;
            }

            var current = First;
            First = null;
            while (true)
            {
                uint bytes;
                Exception? waitErr;
                Exception? completionErr;
                (bytes, waitErr, completionErr) = WaitAndComplete(current);

                if (waitErr is not null && completionErr is not null)
                {
                    current.Dispose();
                    Fatal(new DirWatchError(WindowsErrors.GetOverlappedResult, _dirWatch));
                    return;
                }

                bool stopped;
                lock (_mu)
                {
                    stopped = _stopped;
                }

                if (stopped)
                {
                    current.Dispose();
                    return;
                }

                if (completionErr is not null)
                {
                    var shouldStop = ProcessCompletion(completionErr, current.Buffer, current.BufferLength, bytes);
                    current.Dispose();
                    if (shouldStop)
                    {
                        return;
                    }

                    WindowsRead? next;
                    try
                    {
                        next = BeginRead();
                    }
                    catch (Exception ex)
                    {
                        Fatal(ex);
                        return;
                    }

                    if (next is null)
                    {
                        return;
                    }

                    current = next;
                    continue;
                }

                WindowsRead? following;
                try
                {
                    following = BeginRead();
                }
                catch (Exception ex)
                {
                    current.Dispose();
                    Fatal(ex);
                    return;
                }

                if (following is null)
                {
                    current.Dispose();
                    return;
                }

                var stop = ProcessCompletion(null, current.Buffer, current.BufferLength, bytes);
                current.Dispose();
                if (stop)
                {
                    following.Dispose();
                    return;
                }

                current = following;
            }
        }
        finally
        {
            CloseHandle();
            _doneEvent.Set();
        }
    }

    /// <summary>
    /// Go: <c>func (s *windowsSubscription) processCompletion(callErr error, buf []byte, bytes uint32) (stop bool)</c>。
    /// </summary>
    private bool ProcessCompletion(Exception? callErr, IntPtr buf, int bufLength, uint bytes)
    {
        if (callErr is not null)
        {
            if (IsWinError(callErr, _errorOperationAborted))
            {
                return true;
            }

            if (IsWinError(callErr, _errorInvalidParameter))
            {
                lock (_mu)
                {
                    _bufBytes = _networkBufSize;
                }

                return false;
            }

            if (IsWinError(callErr, _errorNotifyEnumDir))
            {
                _dirWatch._events.SetError(FsWatchException.Overflow());
                _dirWatch.Notify();
                return false;
            }

            if (IsWinError(callErr, _errorAccessDenied))
            {
                // 可能是被监听目录被删了；确认后处理。
                FileAttributes attrs;
                try
                {
                    attrs = File.GetAttributes(_dirWatch._physicalDir);
                }
                catch (Exception)
                {
                    attrs = (FileAttributes)(-1);
                }

                if (attrs == (FileAttributes)(-1) || !attrs.HasFlag(FileAttributes.Directory))
                {
                    _dirWatch._events.Remove(_dirWatch._dir);
                    _dirWatch._events.SetError(new FsWatchException(
                        FsWatchErrorKind.WatchTerminated,
                        "fswatch: watch terminated: watched directory removed"));
                    _dirWatch.Notify();
                    Stop();
                    return true;
                }
            }

            Fatal(new DirWatchError(WindowsErrors.Unknown, _dirWatch));
            return true;
        }

        // 遍历 FILE_NOTIFY_INFORMATION 链。
        uint offset = 0;
        var length = bytes == 0 ? (uint)bufLength : bytes;
        while (offset < length)
        {
            var baseOffset = (int)offset;
            var nextEntryOffset = (uint)Marshal.ReadInt32(buf, baseOffset);
            var action = (uint)Marshal.ReadInt32(buf, baseOffset + 4);
            var fileNameLength = Marshal.ReadInt32(buf, baseOffset + 8);
            var name = ReadNotifyName(buf, baseOffset + 12, fileNameLength / 2);

            ProcessOne(action, name);

            if (nextEntryOffset == 0)
            {
                break;
            }

            offset += nextEntryOffset;
        }

        _dirWatch.Notify();
        return false;
    }

    /// <summary>Go: <c>windows.UTF16ToString(nameSlice)</c>（见文件头第 6 条）。</summary>
    private static string ReadNotifyName(IntPtr buf, int offset, int charCount)
    {
        var builder = new System.Text.StringBuilder(charCount > 0 ? charCount : 0);
        for (var i = 0; i < charCount; i++)
        {
            var ch = (char)Marshal.ReadInt16(buf, offset + (i * 2));
            if (ch == '\0')
            {
                break;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private int BufferLength
    {
        get
        {
            lock (_mu)
            {
                return _bufBytes;
            }
        }
    }

    /// <summary>Go: <c>func (s *windowsSubscription) processOne(action uint32, name string)</c>。</summary>
    private void ProcessOne(uint action, string name)
    {
        var path = _dirWatch._dir + "\\" + name;
        var watchPath = _dirWatch._physicalDir + "\\" + name;
        switch (action)
        {
            case _fileActionAdded:
            case _fileActionRenamedNewName:
                // 即使文件在我们查看时已经消失也要发事件：内核说它被加过，而后面的 REMOVED 必须能在
                // eventList 里找到这个条目，create+delete 才能成对抵消。
                _dirWatch._events.Create(path);
                break;

            case _fileActionModified:
                FileAttributes attrs;
                try
                {
                    attrs = File.GetAttributes(watchPath);
                }
                catch (Exception)
                {
                    return;
                }

                if (!attrs.HasFlag(FileAttributes.Directory))
                {
                    _dirWatch._events.Update(path);
                }

                break;

            case _fileActionRemoved:
            case _fileActionRenamedOldName:
                var seq = _dirWatch._events.RemoveAndGetSequence(path);
                if (_dirWatch.TerminateCallbacksForDeletedRoot(
                        path,
                        seq,
                        new FsWatchException(FsWatchErrorKind.WatchTerminated, "fswatch: watch terminated: watched directory removed")))
                {
                    _dirWatch.Notify();
                }

                break;
        }
    }

    /// <summary>
    /// Go: <c>func (s *windowsSubscription) fatal(err error)</c>。run 里直接同步调用 handleWatcherError 会死锁
    /// （handleWatcherError → closeWatch 等 doneCh，而 doneCh 要等 run 返回才关闭），所以另起一个 Task 做清理。
    /// </summary>
    private void Fatal(Exception err)
    {
        var werr = new DirWatchError(err, _dirWatch);
        _ = Task.Run(() => _watcherImpl.HandleWatcherError(werr));
        Stop();
    }

    private void StopLocked()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _stopEvent.Set();
        // 取消在途 IO 让 wait 返回；run 线程在它的收尾里关闭句柄。
        _ = CancelIoEx(_handle, IntPtr.Zero);
    }

    internal void Stop()
    {
        lock (_mu)
        {
            StopLocked();
        }
    }

    internal void StopAndWait()
    {
        Stop();
        _doneEvent.Wait();
    }

    private static bool IsWinError(Exception? ex, int code)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is Win32Exception w32 && w32.NativeErrorCode == code)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly IntPtr _invalidHandleValue = new(-1);
    private const uint _infinite = 0xFFFF_FFFF;
    private const uint _waitObject0 = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(IntPtr hFile, out ByHandleFileInformation lpFileInformation);

    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
    private static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, IntPtr lpName);

    [DllImport("kernel32.dll", EntryPoint = "ReadDirectoryChangesW", SetLastError = true)]
    private static extern bool ReadDirectoryChangesW(
        IntPtr hDirectory,
        IntPtr lpBuffer,
        uint nBufferLength,
        bool bWatchSubtree,
        uint dwNotifyFilter,
        out uint lpBytesReturned,
        IntPtr lpOverlapped,
        IntPtr lpCompletionRoutine);

    [DllImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", EntryPoint = "GetOverlappedResult", SetLastError = true)]
    private static extern bool GetOverlappedResult(IntPtr hFile, IntPtr lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32.dll", EntryPoint = "CancelIoEx", SetLastError = true)]
    private static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static extern bool CloseHandleNative(IntPtr hObject);
}

    /// <summary>Go: <c>type windowsRead struct</c>。一次在途的 ReadDirectoryChangesW（缓冲 + OVERLAPPED + 事件）。</summary>
internal sealed class WindowsRead : IDisposable
{
    internal IntPtr Buffer;
    internal IntPtr Overlapped;
    internal IntPtr Event;
    private readonly int _bufferLength;
    private bool _released;

    internal WindowsRead(int bufferSize)
    {
        _bufferLength = bufferSize;
        Buffer = Marshal.AllocHGlobal(bufferSize);
        Overlapped = Marshal.AllocHGlobal(IntPtr.Size * 4);
        for (var i = 0; i < 4; i++)
        {
            Marshal.WriteIntPtr(Overlapped, i * IntPtr.Size, IntPtr.Zero);
        }
    }

    /// <summary>Go: <c>len(r.buf)</c>（processCompletion 在 completion 返回 0 字节时用它当链长度）。</summary>
    internal int BufferLength => _bufferLength;

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;

        if (Event != IntPtr.Zero)
        {
            _ = CloseHandleNative(Event);
            Event = IntPtr.Zero;
        }

        if (Buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Buffer);
            Buffer = IntPtr.Zero;
        }

        if (Overlapped != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Overlapped);
            Overlapped = IntPtr.Zero;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static extern bool CloseHandleNative(IntPtr hObject);
}

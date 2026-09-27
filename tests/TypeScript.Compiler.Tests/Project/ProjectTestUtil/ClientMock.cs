// ported from: internal/testutil/projecttestutil/clientmock_generated.go:1-645
//
// Go 侧由 github.com/matryer/moq 生成（生成物文件头标明 DO NOT EDIT）；C# 侧没有 AOT 安全的 mock 生成器，
// 手工移植，结构与生成物逐行对应：每个方法一个 Func/Action 字段（名字照抄 Go 的 XXXFunc）+ 一个 XXXCalls()
// 访问器（与 Vfs/VfsMock/Mock.Generated.cs 的手工移植同款）。
//
// 与 Go 的差异（逐条）：
//
// 1) Go 的匿名结构体调用记录 → C# 值元组数组；访问器返回**拷贝**（Go 返回的是别名切片，见
//    Vfs/Mock.Generated.cs 文件头第 1 条）。`GetLocaleCalls()`/`IsActiveCalls()` 的 Go 返回类型是
//    `[]struct{}`（空结构体切片），调用点只对它们取 `len(...)` ⇒ C# 直接返回 **int**（调用点写
//    `utils.Client().GetLocaleCalls()` 就是 Go 的 `len(utils.Client().GetLocaleCalls())`）。
// 2) `error` 返回值按 docs/architecture.md §3.2 落成**异常**（Project/Client.cs 文件头第 1 条），所以
//    PublishDiagnostics/RefreshCodeLens/… 在 C# 的 Client 接口上是 `void`，这里的 Func 字段相应是 Action。
// 3) Go 的 moq 生成物在 `XXXFunc == nil` 时返回**零值**（这就是 projecttestutil 这两个 mock 与
//    vfs/vfsmock 生成物的区别：后者 panic("…: method is nil but …")，C# 侧落成 Debug.Fail）。
//    这里照 Go 返回零值：void 方法什么都不做；`GetLocale` 返回 `default(Loc)`（= Go 的零值 ""，
//    见 Locale/Locale.cs 文件头：`Default` 就是零值）；`IsActive` 返回 false。
// 4) `ctx context.Context` → `TypeScript.Core.Context`；`id project.WatcherID` → `TypeScript.Project.WatcherID`。
// 5) `locale.Locale` → 别名 `Loc`：命名空间 `TypeScript.Locale` 与类型 `TypeScript.Locale.Locale` 同名，
//    裸写 `Locale` 会命中命名空间（CS0118）——Project/Client.cs 文件头第 4 条已记录该坑。
// 6) `lsproto.TelemetryEvent` 是 Go 的**类型别名**（lsp_generated.go:11750），C# 生成物不给别名 ⇒ 这里用
//    `using TelemetryEvent = …OrNull;`（生成物的完整联合类型名）保留可读性，与 Client.cs 一致。
// 7) `ProgressStart(message *diagnostics.Message, args ...any)` 的 `...any` → `params object?[]`（Client.cs
//    文件头第 2 条）；委托类型里 `params` 无法表达，故字段是 `Action<Diagnostics.Message, object?[]>`。
// 8) 文件名不以 `.Generated.cs` 结尾 ⇒ Roslyn 不按"生成代码"处理，本文件继承项目 nullable 上下文（无需
//    `#nullable enable`），命名也按 C# 习惯（公开字段照 Go 的 XXXFunc 原名，本来就是 PascalCase）。
namespace TypeScript.Tests.ProjectTestUtil;

using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using Loc = TypeScript.Locale.Locale;
using TelemetryEvent = TypeScript.LsProto.RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull;

/// <summary>Go: <c>projecttestutil.ClientMock</c>（moq 生成的 <c>project.Client</c> 实现）。</summary>
public sealed class ClientMock : Client
{
    /// <summary>Go: <c>GetLocaleFunc</c>。</summary>
    public Func<Loc>? GetLocaleFunc;

    /// <summary>Go: <c>IsActiveFunc</c>。</summary>
    public Func<bool>? IsActiveFunc;

    /// <summary>Go: <c>ProgressFinishFunc</c>。</summary>
    public Action<Diagnostics.Message, object?[]>? ProgressFinishFunc;

    /// <summary>Go: <c>ProgressStartFunc</c>。</summary>
    public Action<Diagnostics.Message, object?[]>? ProgressStartFunc;

    /// <summary>Go: <c>PublishDiagnosticsFunc</c>。</summary>
    public Action<Context, PublishDiagnosticsParams>? PublishDiagnosticsFunc;

    /// <summary>Go: <c>RefreshCodeLensFunc</c>。</summary>
    public Action<Context>? RefreshCodeLensFunc;

    /// <summary>Go: <c>RefreshDiagnosticsFunc</c>。</summary>
    public Action<Context>? RefreshDiagnosticsFunc;

    /// <summary>Go: <c>RefreshInlayHintsFunc</c>。</summary>
    public Action<Context>? RefreshInlayHintsFunc;

    /// <summary>Go: <c>RegisterContentMapperExtensionsFunc</c>。</summary>
    public Action<Context, string[]>? RegisterContentMapperExtensionsFunc;

    /// <summary>Go: <c>SendTelemetryFunc</c>。</summary>
    public Action<Context, TelemetryEvent>? SendTelemetryFunc;

    /// <summary>Go: <c>SetLocaleFunc</c>。</summary>
    public Action<string>? SetLocaleFunc;

    /// <summary>Go: <c>UnwatchFilesFunc</c>。</summary>
    public Action<Context, WatcherID>? UnwatchFilesFunc;

    /// <summary>Go: <c>WatchFilesFunc</c>。</summary>
    public Action<Context, WatcherID, FileSystemWatcher[]>? WatchFilesFunc;

    private readonly Lock _callsLock = new();
    private readonly List<object?> _getLocaleCalls = [];
    private readonly List<object?> _isActiveCalls = [];
    private readonly List<(Diagnostics.Message Message, object?[] Args)> _progressFinishCalls = [];
    private readonly List<(Diagnostics.Message Message, object?[] Args)> _progressStartCalls = [];
    private readonly List<(Context Ctx, PublishDiagnosticsParams Params)> _publishDiagnosticsCalls = [];
    private readonly List<Context> _refreshCodeLensCalls = [];
    private readonly List<Context> _refreshDiagnosticsCalls = [];
    private readonly List<Context> _refreshInlayHintsCalls = [];
    private readonly List<(Context Ctx, string[] Extensions)> _registerContentMapperExtensionsCalls = [];
    private readonly List<(Context Ctx, TelemetryEvent Telemetry)> _sendTelemetryCalls = [];
    private readonly List<string> _setLocaleCalls = [];
    private readonly List<(Context Ctx, WatcherID ID)> _unwatchFilesCalls = [];
    private readonly List<(Context Ctx, WatcherID ID, FileSystemWatcher[] Watchers)> _watchFilesCalls = [];

    /// <summary>Go: <c>ClientMock.GetLocale</c>。</summary>
    public Loc GetLocale()
    {
        lock (_callsLock)
        {
            _getLocaleCalls.Add(null);
        }

        if (GetLocaleFunc is null)
        {
            // Go: var localeOut locale.Locale; return localeOut（零值）。
            return default;
        }

        return GetLocaleFunc();
    }

    /// <summary>Go: <c>ClientMock.GetLocaleCalls()</c>。</summary>
    public int GetLocaleCalls()
    {
        lock (_callsLock)
        {
            return _getLocaleCalls.Count;
        }
    }

    /// <summary>Go: <c>ClientMock.IsActive</c>。</summary>
    public bool IsActive()
    {
        lock (_callsLock)
        {
            _isActiveCalls.Add(null);
        }

        return IsActiveFunc?.Invoke() ?? false;
    }

    /// <summary>Go: <c>ClientMock.IsActiveCalls()</c>。</summary>
    public int IsActiveCalls()
    {
        lock (_callsLock)
        {
            return _isActiveCalls.Count;
        }
    }

    /// <summary>Go: <c>ClientMock.ProgressFinish</c>。</summary>
    public void ProgressFinish(Diagnostics.Message message, params object?[] args)
    {
        lock (_callsLock)
        {
            _progressFinishCalls.Add((message, args));
        }

        ProgressFinishFunc?.Invoke(message, args);
    }

    /// <summary>Go: <c>ClientMock.ProgressFinishCalls()</c>。</summary>
    public (Diagnostics.Message Message, object?[] Args)[] ProgressFinishCalls()
    {
        lock (_callsLock)
        {
            return _progressFinishCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.ProgressStart</c>。</summary>
    public void ProgressStart(Diagnostics.Message message, params object?[] args)
    {
        lock (_callsLock)
        {
            _progressStartCalls.Add((message, args));
        }

        ProgressStartFunc?.Invoke(message, args);
    }

    /// <summary>Go: <c>ClientMock.ProgressStartCalls()</c>。</summary>
    public (Diagnostics.Message Message, object?[] Args)[] ProgressStartCalls()
    {
        lock (_callsLock)
        {
            return _progressStartCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.PublishDiagnostics</c>。</summary>
    public void PublishDiagnostics(Context ctx, PublishDiagnosticsParams @params)
    {
        lock (_callsLock)
        {
            _publishDiagnosticsCalls.Add((ctx, @params));
        }

        PublishDiagnosticsFunc?.Invoke(ctx, @params);
    }

    /// <summary>Go: <c>ClientMock.PublishDiagnosticsCalls()</c>。</summary>
    public (Context Ctx, PublishDiagnosticsParams Params)[] PublishDiagnosticsCalls()
    {
        lock (_callsLock)
        {
            return _publishDiagnosticsCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.RefreshCodeLens</c>。</summary>
    public void RefreshCodeLens(Context ctx)
    {
        lock (_callsLock)
        {
            _refreshCodeLensCalls.Add(ctx);
        }

        RefreshCodeLensFunc?.Invoke(ctx);
    }

    /// <summary>Go: <c>ClientMock.RefreshCodeLensCalls()</c>。</summary>
    public Context[] RefreshCodeLensCalls()
    {
        lock (_callsLock)
        {
            return _refreshCodeLensCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.RefreshDiagnostics</c>。</summary>
    public void RefreshDiagnostics(Context ctx)
    {
        lock (_callsLock)
        {
            _refreshDiagnosticsCalls.Add(ctx);
        }

        RefreshDiagnosticsFunc?.Invoke(ctx);
    }

    /// <summary>Go: <c>ClientMock.RefreshDiagnosticsCalls()</c>。</summary>
    public Context[] RefreshDiagnosticsCalls()
    {
        lock (_callsLock)
        {
            return _refreshDiagnosticsCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.RefreshInlayHints</c>。</summary>
    public void RefreshInlayHints(Context ctx)
    {
        lock (_callsLock)
        {
            _refreshInlayHintsCalls.Add(ctx);
        }

        RefreshInlayHintsFunc?.Invoke(ctx);
    }

    /// <summary>Go: <c>ClientMock.RefreshInlayHintsCalls()</c>。</summary>
    public Context[] RefreshInlayHintsCalls()
    {
        lock (_callsLock)
        {
            return _refreshInlayHintsCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.RegisterContentMapperExtensions</c>。</summary>
    public void RegisterContentMapperExtensions(Context ctx, string[] extensions)
    {
        lock (_callsLock)
        {
            _registerContentMapperExtensionsCalls.Add((ctx, extensions));
        }

        RegisterContentMapperExtensionsFunc?.Invoke(ctx, extensions);
    }

    /// <summary>Go: <c>ClientMock.RegisterContentMapperExtensionsCalls()</c>。</summary>
    public (Context Ctx, string[] Extensions)[] RegisterContentMapperExtensionsCalls()
    {
        lock (_callsLock)
        {
            return _registerContentMapperExtensionsCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.SendTelemetry</c>。</summary>
    public void SendTelemetry(Context ctx, TelemetryEvent telemetry)
    {
        lock (_callsLock)
        {
            _sendTelemetryCalls.Add((ctx, telemetry));
        }

        SendTelemetryFunc?.Invoke(ctx, telemetry);
    }

    /// <summary>Go: <c>ClientMock.SendTelemetryCalls()</c>。</summary>
    public (Context Ctx, TelemetryEvent Telemetry)[] SendTelemetryCalls()
    {
        lock (_callsLock)
        {
            return _sendTelemetryCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.SetLocale</c>（Go 的形参名 <c>localeMoqParam</c>）。</summary>
    public void SetLocale(string localeMoqParam)
    {
        lock (_callsLock)
        {
            _setLocaleCalls.Add(localeMoqParam);
        }

        SetLocaleFunc?.Invoke(localeMoqParam);
    }

    /// <summary>Go: <c>ClientMock.SetLocaleCalls()</c>。</summary>
    public string[] SetLocaleCalls()
    {
        lock (_callsLock)
        {
            return _setLocaleCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.UnwatchFiles</c>。</summary>
    public void UnwatchFiles(Context ctx, WatcherID id)
    {
        lock (_callsLock)
        {
            _unwatchFilesCalls.Add((ctx, id));
        }

        UnwatchFilesFunc?.Invoke(ctx, id);
    }

    /// <summary>Go: <c>ClientMock.UnwatchFilesCalls()</c>。</summary>
    public (Context Ctx, WatcherID ID)[] UnwatchFilesCalls()
    {
        lock (_callsLock)
        {
            return _unwatchFilesCalls.ToArray();
        }
    }

    /// <summary>Go: <c>ClientMock.WatchFiles</c>。</summary>
    public void WatchFiles(Context ctx, WatcherID id, FileSystemWatcher[] watchers)
    {
        lock (_callsLock)
        {
            _watchFilesCalls.Add((ctx, id, watchers));
        }

        WatchFilesFunc?.Invoke(ctx, id, watchers);
    }

    /// <summary>Go: <c>ClientMock.WatchFilesCalls()</c>。</summary>
    public (Context Ctx, WatcherID ID, FileSystemWatcher[] Watchers)[] WatchFilesCalls()
    {
        lock (_callsLock)
        {
            return _watchFilesCalls.ToArray();
        }
    }
}

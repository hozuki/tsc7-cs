// ported from: internal/project/client.go:1-27
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `error` 返回值 → C# 的**异常**（architecture.md §3.2；仓内先例：`ContentMapper.Host.Transform/Close`
//    都是 `(T, error)/error` → 直接抛）。因此 `WatchFiles`/`UnwatchFiles`/`RegisterContentMapperExtensions`/
//    `RefreshDiagnostics`/`PublishDiagnostics`/`RefreshInlayHints`/`RefreshCodeLens`/`SendTelemetry` 都是 `void`：
//    session.go 里 `if err := s.client.X(...); err != nil { ... }` 的调用点在 C# 里写成 `try { … } catch (…)`。
//
// 2) `ProgressStart(message *diagnostics.Message, args ...any)` → `ProgressStart(Diagnostics.Message message, params object?[] args)`
//    （`...any` → `params object?[]`，仓内既有约定，如 `Ast/Diagnostic.cs` 的 `NewDiagnostic`）。
//
// 3) `ctx context.Context` → `TypeScript.Core.Context`；`id WatcherID` → `WatcherID`（Go 的 `type WatcherID string`
//    在 watch.go:155，由 watch.go 的移植提供，见 src/TypeScript.Compiler/Project/Watch.cs）。
//
// 4) `locale.Locale` → **别名 `Loc`**：命名空间 `TypeScript.Locale` 与类型 `TypeScript.Locale.Locale` 同名，
//    在 `TypeScript.Project` 里裸写 `Locale` 会命中命名空间（CS0118）——这是 Locale/Locale.cs 文件头
//    记录的坑，那里给的解法就是别名 `Loc`（别名不能也叫 `Locale`，同样会被命名空间吃掉）。
//
// 5) `TelemetryEvent` 是 Go 的**类型别名**（lsp_generated.go:11750），C# 侧生成物不给别名 ⇒ 这里用
//    `using TelemetryEvent = …OrNull;`（生成物的完整类型名）保留可读性。
//
// 6) `[]*lsproto.FileSystemWatcher` → `LsProto.FileSystemWatcher[]`；必须**限定**命名空间，因为
//    `ImplicitUsings` 引入了 `System.IO.FileSystemWatcher`（歧义 → CS0104）。
using Loc = TypeScript.Locale.Locale;
using TelemetryEvent = TypeScript.LsProto.RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull;

namespace TypeScript.Project;

/// <summary>Go 的 <c>Client</c>：project 系统对 LSP 客户端（编辑器）的依赖面。</summary>
public interface Client
{
    void WatchFiles(Core.Context ctx, WatcherID id, LsProto.FileSystemWatcher[] watchers);

    void UnwatchFiles(Core.Context ctx, WatcherID id);

    void RegisterContentMapperExtensions(Core.Context ctx, string[] extensions);

    void RefreshDiagnostics(Core.Context ctx);

    void PublishDiagnostics(Core.Context ctx, LsProto.PublishDiagnosticsParams @params);

    void RefreshInlayHints(Core.Context ctx);

    void RefreshCodeLens(Core.Context ctx);

    void ProgressStart(Diagnostics.Message message, params object?[] args);

    void ProgressFinish(Diagnostics.Message message, params object?[] args);

    void SendTelemetry(Core.Context ctx, TelemetryEvent telemetry);

    bool IsActive();

    /// <summary>SetLocale updates the locale used for diagnostic messages（Go: <c>SetLocale(locale string)</c>）。</summary>
    void SetLocale(string locale);

    /// <summary>GetLocale returns the current display locale for diagnostic messages。</summary>
    Loc GetLocale();
}

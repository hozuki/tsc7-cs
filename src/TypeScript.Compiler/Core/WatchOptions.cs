// ported from: internal/core/watchoptions.go
//
// 偏离 Go 的地方：
// 1) Go 的 `time.Duration` → C# `TimeSpan`（同样的“时间跨度”语义；Go 里用毫秒数表示）。
// 2) `WatchInterval()` 在 Go 里是 nil 接收者安全的方法（nil → 2000ms）。C# 实例方法不能在 null 上分派，
//    `w != nil` 分支是死代码已删除；调用点写 `w?.WatchInterval() ?? TimeSpan.FromMilliseconds(2000)`。
//    （TSGO 当前没有任何调用点，见移植报告。）
// 3) Go 里 WatchOptions 以 `*core.WatchOptions` 使用（tsoptions 用 `&core.WatchOptions{}` 解析配置）→ C# class。
// 4) JSON tag 见 CompilerOptions.cs 文件头（Interval → "watchInterval"、FileKind → "watchFile"、
//    SyncWatchDir → "synchronousWatchDirectory"…）：这些字段都**不带** omitzero/omitempty，所以永远写出
//    （nil 指针/切片 → null、Tristate Unknown → null）。字段级映射由 `artifacts/api-codegen/gen-core-json.mjs`
//    生成到 `Core/BuildOptionsJson.Generated.cs`（本类据此声明为 partial 并实现 IJsonMarshaler/IJsonUnmarshaler）。
using TypeScript.Json;

namespace TypeScript.Core;

public sealed partial class WatchOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public int? Interval;
    public WatchFileKind FileKind;
    public WatchDirectoryKind DirectoryKind;
    public PollingKind FallbackPolling;
    public Tristate SyncWatchDir;
    public string[]? ExcludeDir;
    public string[]? ExcludeFiles;

    public TimeSpan WatchInterval()
    {
        var watchInterval = TimeSpan.FromMilliseconds(2000);
        if (Interval is int interval)
        {
            watchInterval = TimeSpan.FromMilliseconds(interval);
        }
        return watchInterval;
    }
}

public enum WatchFileKind : int
{
    None = 0,
    FixedPollingInterval = 1,
    PriorityPollingInterval = 2,
    DynamicPriorityPolling = 3,
    FixedChunkSizePolling = 4,
    UseFsEvents = 5,
    UseFsEventsOnParentDirectory = 6,
}

public enum WatchDirectoryKind : int
{
    None = 0,
    UseFsEvents = 1,
    FixedPollingInterval = 2,
    DynamicPriorityPolling = 3,
    FixedChunkSizePolling = 4,
}

public enum PollingKind : int
{
    None = 0,
    FixedInterval = 1,
    PriorityInterval = 2,
    DynamicPriority = 3,
    FixedChunkSize = 4,
}

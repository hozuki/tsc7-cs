// ported from: internal/execute/build/uptodatestatus.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) 未导出类型/字段 → C# 习惯命名：`upToDateStatus` → `UpToDateStatus`、`inputOutputName` → `InputOutputName`、
//    `fileAndTime` → `FileAndTime`、`inputOutputFileAndTime` → `InputOutputFileAndTime`、`upstreamErrors` → `UpstreamErrors`；
//    未导出字段 → `_camelCase`（`_kind`/`_data`/`_input`/`_output`/`_buildInfo`/`_ref`/`_refHasUpstreamErrors`）。
// 2) 枚举成员名 = Go 常量名去掉类型前缀（§3.3）：`upToDateStatusTypeConfigFileNotFound` → `ConfigFileNotFound`；
//    枚举底层类型照 Go 的 `uint16`，数值照 iota。
// 3) Go 的 `data any` + 类型断言 → `object? _data` + C# 模式匹配；断言失败与 Go 的类型断言 panic 一致
//    （`inputOutputFileAndTime()` 之类返回 nil，最后一处 `data.(string)` 会抛 InvalidCastException，对应 Go 的 panic）。
// 4) `panic(...)` → `Debug.Fail(...)`（§3.2）。
namespace TypeScript.Execute.Build;

using global::System;

/// <summary>Go: <c>type upToDateStatusType uint16</c>（uptodatestatus.go:5-50）。</summary>
internal enum UpToDateStatusType : ushort
{
    // Errors:

    /// <summary>config file was not found</summary>
    ConfigFileNotFound = 0,
    /// <summary>found errors during build</summary>
    BuildErrors = 1,
    /// <summary>did not build because upstream project has errors - and we have option to stop build on upstream errors</summary>
    UpstreamErrors = 2,

    /// <summary>Its all good, no work to do</summary>
    UpToDate = 3,

    // Pseudo-builds - touch timestamps, no actual build:

    /// <summary>The project appears out of date because its upstream inputs are newer than its outputs,
    /// but all of its outputs are actually newer than the previous identical outputs of its (.d.ts) inputs.
    /// This means we can Pseudo-build (just touch timestamps), as if we had actually built this project.</summary>
    UpToDateWithUpstreamTypes = 4,
    /// <summary>The project appears up to date and even though input file changed, its text didnt so just need to update timestamps</summary>
    UpToDateWithInputFileText = 5,

    // Needs build:

    /// <summary>input file is missing</summary>
    InputFileMissing = 6,
    /// <summary>output file is missing</summary>
    OutputMissing = 7,
    /// <summary>input file is newer than output file</summary>
    InputFileNewer = 8,
    /// <summary>build info is out of date as we need to emit some files</summary>
    OutOfDateBuildInfoWithPendingEmit = 9,
    /// <summary>build info indicates that project has errors and they need to be reported</summary>
    OutOfDateBuildInfoWithErrors = 10,
    /// <summary>build info options indicate there is work to do based on changes in options</summary>
    OutOfDateOptions = 11,
    /// <summary>file was root when built but not any more</summary>
    OutOfDateRoots = 12,
    /// <summary>buildInfo.version mismatch with current ts version</summary>
    TsVersionOutputOfDate = 13,
    /// <summary>build because --force was specified</summary>
    ForceBuild = 14,

    /// <summary>solution file</summary>
    Solution = 15,
}

/// <summary>Go: <c>type inputOutputName struct</c>（uptodatestatus.go:54-57）。</summary>
internal sealed class InputOutputName
{
    internal string _input = "";
    internal string _output = "";
}

/// <summary>Go: <c>type fileAndTime struct</c>（uptodatestatus.go:59-62）。</summary>
internal sealed class FileAndTime
{
    internal string _file = "";
    internal DateTime _time;
}

/// <summary>Go: <c>type inputOutputFileAndTime struct</c>（uptodatestatus.go:64-68）。</summary>
internal sealed class InputOutputFileAndTime
{
    internal FileAndTime _input = new();
    internal FileAndTime _output = new();
    internal string _buildInfo = "";
}

/// <summary>Go: <c>type upstreamErrors struct</c>（uptodatestatus.go:70-73）。</summary>
internal sealed class UpstreamErrors
{
    internal string _ref = "";
    internal bool _refHasUpstreamErrors;
}

/// <summary>Go: <c>type upToDateStatus struct</c>（uptodatestatus.go:75-78）。</summary>
internal sealed class UpToDateStatus
{
    internal UpToDateStatusType _kind;
    internal object? _data;

    /// <summary>Go: <c>func (s *upToDateStatus) isError() bool</c>（uptodatestatus.go:80-89）。</summary>
    internal bool IsError() => _kind switch
    {
        UpToDateStatusType.ConfigFileNotFound or UpToDateStatusType.BuildErrors or UpToDateStatusType.UpstreamErrors => true,
        _ => false,
    };

    /// <summary>Go: <c>func (s *upToDateStatus) isPseudoBuild() bool</c>（uptodatestatus.go:91-99）。</summary>
    internal bool IsPseudoBuild() => _kind switch
    {
        UpToDateStatusType.UpToDateWithUpstreamTypes or UpToDateStatusType.UpToDateWithInputFileText => true,
        _ => false,
    };

    /// <summary>Go: <c>func (s *upToDateStatus) inputOutputFileAndTime() *inputOutputFileAndTime</c>（uptodatestatus.go:101-107）。</summary>
    internal InputOutputFileAndTime? InputOutputFileAndTime() => _data as InputOutputFileAndTime;

    /// <summary>Go: <c>func (s *upToDateStatus) inputOutputName() *inputOutputName</c>（uptodatestatus.go:109-115）。</summary>
    internal InputOutputName? InputOutputName() => _data as InputOutputName;

    /// <summary>Go: <c>func (s *upToDateStatus) oldestOutputFileName() string</c>（uptodatestatus.go:117-129）。</summary>
    internal string OldestOutputFileName()
    {
        if (!IsPseudoBuild() && _kind != UpToDateStatusType.UpToDate)
        {
            Debug.Fail("only valid for up to date status of pseudo-build or up to date");
        }

        if (InputOutputFileAndTime() is { } inputOutputFileAndTime)
        {
            return inputOutputFileAndTime._output._file;
        }
        if (InputOutputName() is { } inputOutputName)
        {
            return inputOutputName._output;
        }
        return (string)_data!;
    }

    /// <summary>Go: <c>func (s *upToDateStatus) upstreamErrors() *upstreamErrors</c>（uptodatestatus.go:131-133）。</summary>
    internal UpstreamErrors UpstreamErrors() => (UpstreamErrors)_data!;
}

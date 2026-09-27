// ported from: internal/tsoptions/declswatch.go
//
// 映射约定（docs/architecture.md §3.3）：`[]*CommandLineOption` → `CommandLineOption[]`；导出的 `OptionsForWatch` →
// `public static readonly`。`Kind: CommandLineOptionTypeNumber/Enum/Boolean/List` →
// `CommandLineOptionKind.Number/Enum/Boolean/List`；`DefaultValueDescription: core.WatchFileKindUseFsEvents` →
// `WatchFileKind.UseFsEvents`（Go 的包级常量在 C# 是 enum 成员，装箱后与 Go 的 `any` 持有同一枚举值）、
// `core.WatchDirectoryKindUseFsEvents` → `WatchDirectoryKind.UseFsEvents`、`core.PollingKindPriorityInterval` →
// `PollingKind.PriorityInterval`；`diagnostics.X` → `Diagnostics.X`。未导出字段 `allowConfigDirTemplateSubstitution`
// → CommandLineOption.cs 里的 `_allowConfigDirTemplateSubstitution`。
// Go 注释里被注释掉的 ElementOptions（excludeDirectories/excludeFiles 的 element）对应 CommandLineOption.cs 的
// `_commandLineOptionElements` 表（Go 同样把它们注册在 `commandLineOptionElements`），注释照抄保留。
using TypeScript.Core;
using static TypeScript.Diagnostics.Diagnostics;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    // Go: declswatch.go:8-88
    public static readonly CommandLineOption[] OptionsForWatch =
    [
        new()
        {
            Name = "watchInterval",
            Kind = CommandLineOptionKind.Number,
            Category = Watch_and_Build_Modes,
        },
        new()
        {
            Name = "watchFile",
            Kind = CommandLineOptionKind.Enum,
            // new Map(Object.entries({
            //     fixedpollinginterval: WatchFileKind.FixedPollingInterval,
            //     prioritypollinginterval: WatchFileKind.PriorityPollingInterval,
            //     dynamicprioritypolling: WatchFileKind.DynamicPriorityPolling,
            //     fixedchunksizepolling: WatchFileKind.FixedChunkSizePolling,
            //     usefsevents: WatchFileKind.UseFsEvents,
            //     usefseventsonparentdirectory: WatchFileKind.UseFsEventsOnParentDirectory,
            // })),
            Category = Watch_and_Build_Modes,
            Description = Specify_how_the_TypeScript_watch_mode_works,
            DefaultValueDescription = WatchFileKind.UseFsEvents,
        },
        new()
        {
            Name = "watchDirectory",
            Kind = CommandLineOptionKind.Enum,
            // new Map(Object.entries({
            //     usefsevents: WatchDirectoryKind.UseFsEvents,
            //     fixedpollinginterval: WatchDirectoryKind.FixedPollingInterval,
            //     dynamicprioritypolling: WatchDirectoryKind.DynamicPriorityPolling,
            //     fixedchunksizepolling: WatchDirectoryKind.FixedChunkSizePolling,
            // })),
            Category = Watch_and_Build_Modes,
            Description = Specify_how_directories_are_watched_on_systems_that_lack_recursive_file_watching_functionality,
            DefaultValueDescription = WatchDirectoryKind.UseFsEvents,
        },
        new()
        {
            Name = "fallbackPolling",
            Kind = CommandLineOptionKind.Enum,
            // new Map(Object.entries({
            //     fixedinterval: PollingWatchKind.FixedInterval,
            //     priorityinterval: PollingWatchKind.PriorityInterval,
            //     dynamicpriority: PollingWatchKind.DynamicPriority,
            //     fixedchunksize: PollingWatchKind.FixedChunkSize,
            // })),
            Category = Watch_and_Build_Modes,
            Description = Specify_what_approach_the_watcher_should_use_if_the_system_runs_out_of_native_file_watchers,
            DefaultValueDescription = PollingKind.PriorityInterval,
        },
        new()
        {
            Name = "synchronousWatchDirectory",
            Kind = CommandLineOptionKind.Boolean,
            Category = Watch_and_Build_Modes,
            Description = Synchronously_call_callbacks_and_update_the_state_of_directory_watchers_on_platforms_that_don_t_support_recursive_watching_natively,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "excludeDirectories",
            Kind = CommandLineOptionKind.List,
            // element: {
            //     Name: "excludeDirectory",
            //     Kind: "string",
            //     isFilePath: true,
            //     extraValidation: specToDiagnostic,
            // },
            _allowConfigDirTemplateSubstitution = true,
            Category = Watch_and_Build_Modes,
            Description = Remove_a_list_of_directories_from_the_watch_process,
        },
        new()
        {
            Name = "excludeFiles",
            Kind = CommandLineOptionKind.List,
            // element: {
            //     Name: "excludeFile",
            //     Kind: "string",
            //     isFilePath: true,
            //     extraValidation: specToDiagnostic,
            // },
            _allowConfigDirTemplateSubstitution = true,
            Category = Watch_and_Build_Modes,
            Description = Remove_a_list_of_files_from_the_watch_mode_s_processing,
        },
    ];
}
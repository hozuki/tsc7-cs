// ported from: internal/tsoptions/declsbuild.go
//
// 映射约定（docs/architecture.md §2/§3.3）：导出的 `TscBuildOption`/`OptionsForBuild`/`BuildOpts` →
// `public static readonly`；`Kind: "boolean"` → `CommandLineOptionKind.Boolean`。
// **偏离**：`BuildOpts = slices.Concat(commonOptionsWithBuild, OptionsForBuild)` 跨 partial 文件（前者在
// DeclsCompiler.cs），而 C# 的静态初始化顺序跨文件未定义（Go 按依赖顺序解析），所以这里用 `Lazy<T>` + 只读属性
// （与 NameMap.cs / Diagnostics.cs 同一处模式）——读取方写法不变（`BuildOpts` 仍是一个 `CommandLineOption[]`）。
// `TscBuildOption` 与 `OptionsForBuild` 在**同一个文件**里、文本顺序在前，是安全的；Go 的 `&TscBuildOption`
// 身份比较在 C# 里就是引用比较（`option != TscBuildOption`），execute/tsc 的 help.go 按此移植。
using System;
using static TypeScript.Diagnostics.Diagnostics;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    // Go: declsbuild.go:9-17
    public static readonly CommandLineOption TscBuildOption = new()
    {
        Name = "build",
        Kind = CommandLineOptionKind.Boolean,
        ShortName = "b",
        ShowInSimplifiedHelpView = true,
        Category = Command_line_Options,
        Description = Build_one_or_more_projects_and_their_dependencies_if_out_of_date,
        DefaultValueDescription = false,
    };

    // Go: declsbuild.go:19-67
    public static readonly CommandLineOption[] OptionsForBuild =
    [
        TscBuildOption,
        new()
        {
            Name = "verbose",
            ShortName = "v",
            Category = Command_line_Options,
            Description = Enable_verbose_logging,
            Kind = CommandLineOptionKind.Boolean,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "dry",
            ShortName = "d",
            Category = Command_line_Options,
            Description = Show_what_would_be_built_or_deleted_if_specified_with_clean,
            Kind = CommandLineOptionKind.Boolean,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "force",
            ShortName = "f",
            Category = Command_line_Options,
            Description = Build_all_projects_including_those_that_appear_to_be_up_to_date,
            Kind = CommandLineOptionKind.Boolean,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "clean",
            Category = Command_line_Options,
            Description = Delete_the_outputs_of_all_projects,
            Kind = CommandLineOptionKind.Boolean,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "builders",
            Kind = CommandLineOptionKind.Number,
            Category = Command_line_Options,
            Description = Set_the_number_of_projects_to_build_concurrently,
            DefaultValueDescription = X_4_unless_singleThreaded_is_passed,
            _minValue = 1,
        },
        new()
        {
            Name = "stopBuildOnErrors",
            Category = Command_line_Options,
            Description = Skip_building_downstream_projects_on_error_in_upstream_project,
            Kind = CommandLineOptionKind.Boolean,
            DefaultValueDescription = false,
        },
    ];

    // Go: declsbuild.go:69（var BuildOpts = slices.Concat(commonOptionsWithBuild, OptionsForBuild)，见文件头）
    public static CommandLineOption[] BuildOpts => _buildOpts.Value;

    // `!`：两个来源都已初始化完毕（一个是同文件里文本顺序在前的字段、另一个是 DeclsCompiler.cs 的只读属性），
    // 但 Roslyn 对"跨 partial 文件的静态初始化顺序"取保守近似，会报 CS8602（nullable 分析）——这是假警报。
    private static readonly Lazy<CommandLineOption[]> _buildOpts = new(static () => [.. CommonOptionsWithBuild!, .. OptionsForBuild!]);
}
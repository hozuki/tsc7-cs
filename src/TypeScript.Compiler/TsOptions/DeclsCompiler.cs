// ported from: internal/tsoptions/declscompiler.go
//
// 映射约定（docs/architecture.md §2/§3.3/§3.6）：
// 1) `[]*CommandLineOption` → `CommandLineOption[]`；导出的 `OptionsDeclarations` → `public static readonly`。
//    未导出的 `commonOptionsWithBuild`/`optionsForCompiler` 按 §2 转 PascalCase，但 **.editorconfig 规定非公开*字段*
//    必须 `_camelCase`（IDE1006 在 TreatWarningsAsErrors 下是 error）**，所以改成只读的 `internal static` 属性
//    （属性受 PascalCase 规则约束）——名字与 ticket 契约一致，读取方写法不变。
// 2) `Kind: CommandLineOptionTypeBoolean` / `Kind: "boolean"` → `Kind = CommandLineOptionKind.Boolean`（§3.3）：
//    Go 的字符串类型枚举在 C# 是 enum。`extraValidation: extraValidationSpec` → `_extraValidation = ExtraValidation.Spec`。
// 3) 未导出字段沿用 CommandLineOption.cs 里的 `internal _camelCase` 字段名（transpileOptionValue → `_transpileOptionValue`、
//    strictFlag → `_strictFlag` 等），与 Go 逐字对应；导出字段（AffectsXxx/Category/Description/…）名字照抄。
// 4) 常量：`diagnostics.X` → **裸名 X**，靠 `using static TypeScript.Diagnostics.Diagnostics;`（§2 的包级函数同名
//    陷阱：写 `Diagnostics.X` 会先解析到**命名空间** `TypeScript.Diagnostics`，实测 CS0234；`using Diagnostics = …`
//    别名在独立工程里可行但在本仓的 partial/命名空间组合下仍被命名空间盖掉）。仓库既有同款：Errors.cs、
//    CommandLineParser.cs、ContentMappers.cs。
//    `core.TSUnknown`/`core.TSTrue` →
//    `Tristate.TSUnknown`/`Tristate.TSTrue`，`core.WatchFileKindUseFsEvents` → `WatchFileKind.UseFsEvents`
//    （Go 的包级常量在 C# 是 enum 成员，§3.3）；`core.ScriptTargetLatestStandard` → 裸名 `ScriptTargetLatestStandard`
//    （Core 的 const，靠 `using static TypeScript.Core.Core`；**不能**写 `Core.Xxx`——那会解析到命名空间
//    `TypeScript.Core` 而不是静态类，§2 的 Debug 同款陷阱）。
// 5) **唯一偏离 Go 行序之处**：Go 的 `OptionsDeclarations`（第 11 行）文本上在两个数组之前，Go 的包级初始化按
//    **依赖顺序**解析；C# 的静态初始化按**文件内文本顺序**（跨 partial 文件未定义），所以这里把
//    `OptionsDeclarations` 排在两个数组之后。语义相同（都是 Concat 那两个数组）。
// 6) 文件尾 Go 的 4 个 reflect 函数（1211-1272）：Go 用 `reflect.Value` 遍历 `core.CompilerOptions` 的导出字段、
//    用 `reflect.DeepEqual` 比较值；C# 禁反射（§3.6）→ 改为遍历显式字段表 `CompilerOptionFields`
//    （TsOptionsConfig 生成，顺序 = Go struct 字段顺序，下标 i 与 Go 的 `optionsValue.Field(i)` 一一对应），
//    `DeepEqual` 由字段表自带的 `Changed` 承担。Go 的 `func(option, value reflect.Value, i int) bool` →
//    `Func<CommandLineOption, object?, int, bool>`（`object?` 即 `value.Interface()` 的装箱值）。
using System;
using TypeScript.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Core.Core;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    // Go: declscompiler.go:13-261
    internal static CommandLineOption[] CommonOptionsWithBuild { get; } =
    [
        //******* commonOptionsWithBuild *******
        new()
        {
            Name = "help",
            ShortName = "h",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = true,
            IsCommandLineOnly = true,
            Category = Command_line_Options,
            Description = Print_this_message,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "help",
            ShortName = "?",
            Kind = CommandLineOptionKind.Boolean,
            IsCommandLineOnly = true,
            Category = Command_line_Options,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "watch",
            ShortName = "w",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = true,
            IsCommandLineOnly = true,
            Category = Command_line_Options,
            Description = Watch_input_files,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "preserveWatchOutput",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = false,
            Category = Output_Formatting,
            Description = Disable_wiping_the_console_in_watch_mode,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "listFiles",
            Kind = CommandLineOptionKind.Boolean,
            Category = Compiler_Diagnostics,
            Description = Print_all_of_the_files_read_during_the_compilation,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "explainFiles",
            Kind = CommandLineOptionKind.Boolean,
            Category = Compiler_Diagnostics,
            Description = Print_files_read_during_the_compilation_including_why_it_was_included,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "listEmittedFiles",
            Kind = CommandLineOptionKind.Boolean,
            Category = Compiler_Diagnostics,
            Description = Print_the_names_of_emitted_files_after_a_compilation,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "pretty",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = true,
            Category = Output_Formatting,
            Description = Enable_color_and_formatting_in_TypeScript_s_output_to_make_compiler_errors_easier_to_read,
            DefaultValueDescription = true,
        },
        new()
        {
            Name = "traceResolution",
            Kind = CommandLineOptionKind.Boolean,
            Category = Compiler_Diagnostics,
            Description = Log_paths_used_during_the_moduleResolution_process,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "diagnostics",
            Kind = CommandLineOptionKind.Boolean,
            Category = Compiler_Diagnostics,
            Description = Output_compiler_performance_information_after_building,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "extendedDiagnostics",
            Kind = CommandLineOptionKind.Boolean,
            Category = Compiler_Diagnostics,
            Description = Output_more_detailed_compiler_performance_information_after_building,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "generateCpuProfile",
            Kind = CommandLineOptionKind.String,
            IsFilePath = true,
            Category = Compiler_Diagnostics,
            Description = Emit_a_v8_CPU_profile_of_the_compiler_run_for_debugging,
            DefaultValueDescription = "profile.cpuprofile",
        },
        new()
        {
            Name = "generateTrace",
            Kind = CommandLineOptionKind.String,
            IsFilePath = true,
            Category = Compiler_Diagnostics,
            Description = Generates_an_event_trace_and_a_list_of_types,
        },
        new()
        {
            Name = "incremental",
            ShortName = "i",
            Kind = CommandLineOptionKind.Boolean,
            Category = Projects,
            Description = Save_tsbuildinfo_files_to_allow_for_incremental_compilation_of_projects,
            _transpileOptionValue = Tristate.TSUnknown,
            DefaultValueDescription = X_false_unless_composite_is_set,
        },
        new()
        {
            Name = "declaration",
            ShortName = "d",
            Kind = CommandLineOptionKind.Boolean,
            // Not setting affectsEmit because we calculate this flag might not affect full emit
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = Emit,
            _transpileOptionValue = Tristate.TSUnknown,
            Description = Generate_d_ts_files_from_TypeScript_and_JavaScript_files_in_your_project,
            DefaultValueDescription = X_false_unless_composite_is_set,
        },
        new()
        {
            Name = "declarationMap",
            Kind = CommandLineOptionKind.Boolean,
            // Not setting affectsEmit because we calculate this flag might not affect full emit
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = Emit,
            DefaultValueDescription = false,
            Description = Create_sourcemaps_for_d_ts_files,
        },
        new()
        {
            Name = "emitDeclarationOnly",
            Kind = CommandLineOptionKind.Boolean,
            // Not setting affectsEmit because we calculate this flag might not affect full emit
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = Emit,
            Description = Only_output_d_ts_files_and_not_JavaScript_files,
            _transpileOptionValue = Tristate.TSUnknown,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "sourceMap",
            Kind = CommandLineOptionKind.Boolean,
            // Not setting affectsEmit because we calculate this flag might not affect full emit
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = Emit,
            DefaultValueDescription = false,
            Description = Create_source_map_files_for_emitted_JavaScript_files,
        },
        new()
        {
            Name = "inlineSourceMap",
            Kind = CommandLineOptionKind.Boolean,
            // Not setting affectsEmit because we calculate this flag might not affect full emit
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Include_sourcemap_files_inside_the_emitted_JavaScript,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noCheck",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = false,
            Category = Compiler_Diagnostics,
            Description = Disable_full_type_checking_only_critical_parse_and_emit_errors_will_be_reported,
            _transpileOptionValue = Tristate.TSTrue,
            DefaultValueDescription = false,
            // Not setting affectsSemanticDiagnostics or affectsBuildInfo because we dont want all diagnostics to go away, its handled in builder
        },
        new()
        {
            Name = "deduplicatePackages",
            Kind = CommandLineOptionKind.Boolean,
            Category = Type_Checking,
            Description = Deduplicate_packages_with_the_same_name_and_version,
            DefaultValueDescription = true,
            AffectsProgramStructure = true,
        },
        new()
        {
            Name = "noEmit",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = true,
            Category = Emit,
            Description = Disable_emitting_files_from_a_compilation,
            _transpileOptionValue = Tristate.TSUnknown,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "assumeChangesOnlyAffectDirectDependencies",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Watch_and_Build_Modes,
            Description = Have_recompiles_in_projects_that_use_incremental_and_watch_mode_assume_that_changes_within_a_file_will_only_affect_files_directly_depending_on_it,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "locale",
            Kind = CommandLineOptionKind.String,
            Category = Command_line_Options,
            IsCommandLineOnly = true,
            Description = Set_the_language_of_the_messaging_from_TypeScript_This_does_not_affect_emit,
            DefaultValueDescription = Platform_specific,
            _extraValidation = ExtraValidation.Locale,
        },
        new()
        {
            Name = "quiet",
            ShortName = "q",
            Kind = CommandLineOptionKind.Boolean,
            Category = Command_line_Options,
            Description = Do_not_print_diagnostics,
        },
        new()
        {
            Name = "singleThreaded",
            Kind = CommandLineOptionKind.Boolean,
            Category = Command_line_Options,
            Description = Run_in_single_threaded_mode,
        },
        new()
        {
            Name = "pprofDir",
            Kind = CommandLineOptionKind.String,
            IsFilePath = true,
            Category = Command_line_Options,
            Description = Generate_pprof_CPU_Slashmemory_profiles_to_the_given_directory,
        },
        new()
        {
            Name = "checkers",
            Kind = CommandLineOptionKind.Number,
            Category = Command_line_Options,
            Description = Set_the_number_of_checkers_per_project,
            DefaultValueDescription = X_4_unless_singleThreaded_is_passed,
            _minValue = 1,
        },
        new()
        {
            Name = "runExternalCode",
            Kind = CommandLineOptionKind.Boolean,
            Category = Command_line_Options,
            IsCommandLineOnly = true,
            Description = Allow_loading_external_content_mapper_plugins_that_execute_code_during_compilation,
            DefaultValueDescription = false,
        },
    ];

    // Go: declscompiler.go:263-1209
    internal static CommandLineOption[] OptionsForCompiler { get; } =
    [
        //******* compilerOptions not common with --build *******
        // CommandLine only options
        new()
        {
            Name = "all",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = true,
            Category = Command_line_Options,
            Description = Show_all_compiler_options,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "version",
            ShortName = "v",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = true,
            Category = Command_line_Options,
            Description = Print_the_compiler_s_version,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "init",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = true,
            Category = Command_line_Options,
            Description = Initializes_a_TypeScript_project_and_creates_a_tsconfig_json_file,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "project",
            ShortName = "p",
            Kind = CommandLineOptionKind.String,
            IsFilePath = true,
            ShowInSimplifiedHelpView = true,
            Category = Command_line_Options,
            Description = Compile_the_project_given_the_path_to_its_configuration_file_or_to_a_folder_with_a_tsconfig_json,
        },
        new()
        {
            Name = "showConfig",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = true,
            Category = Command_line_Options,
            IsCommandLineOnly = true,
            Description = Print_the_final_configuration_instead_of_building,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "listFilesOnly",
            Kind = CommandLineOptionKind.Boolean,
            Category = Command_line_Options,
            IsCommandLineOnly = true,
            Description = Print_names_of_files_that_are_part_of_the_compilation_and_then_stop_processing,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "ignoreConfig",
            Kind = CommandLineOptionKind.Boolean,
            ShowInSimplifiedHelpView = true,
            Category = Command_line_Options,
            IsCommandLineOnly = true,
            Description = Ignore_the_tsconfig_found_and_build_with_commandline_options_and_files,
            DefaultValueDescription = false,
        },
        // Basic
        // targetOptionDeclaration,
        new()
        {
            Name = "target",
            ShortName = "t",
            // Go: // targetOptionMap
            Kind = CommandLineOptionKind.Enum,
            AffectsSourceFile = true,
            AffectsModuleResolution = true,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = Language_and_Environment,
            Description = Set_the_JavaScript_language_version_for_emitted_JavaScript_and_include_compatible_library_declarations,
            DefaultValueDescription = ScriptTargetLatestStandard,
        },
        // moduleOptionDeclaration,
        new()
        {
            Name = "module",
            ShortName = "m",
            // Go: // moduleOptionMap
            Kind = CommandLineOptionKind.Enum,
            AffectsModuleResolution = true,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = Modules,
            Description = Specify_what_module_code_is_generated,
            DefaultValueDescription = Tristate.TSUnknown,
        },
        new()
        {
            Name = "lib",
            Kind = CommandLineOptionKind.List,
            // elements: &CommandLineOption{
            // 	name:                    "lib",
            // 	kind:                   CommandLineOptionTypeEnum, // libMap,
            // 	defaultValueDescription: core.TSUnknown,
            // },
            AffectsProgramStructure = true,
            ShowInSimplifiedHelpView = true,
            Category = Language_and_Environment,
            Description = Specify_a_set_of_bundled_library_declaration_files_that_describe_the_target_runtime_environment,
            _transpileOptionValue = Tristate.TSUnknown,
        },
        new()
        {
            Name = "allowJs",
            Kind = CommandLineOptionKind.Boolean,
            _allowJsFlag = true,
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = JavaScript_Support,
            Description = Allow_JavaScript_files_to_be_a_part_of_your_program_Use_the_checkJs_option_to_get_errors_from_these_files,
            DefaultValueDescription = X_false_unless_checkJs_is_set,
        },
        new()
        {
            Name = "checkJs",
            Kind = CommandLineOptionKind.Boolean,
            AffectsModuleResolution = true,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = JavaScript_Support,
            Description = Enable_error_reporting_in_type_checked_JavaScript_files,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "jsx",
            // Go: // jsxOptionMap,
            Kind = CommandLineOptionKind.Enum,
            AffectsSourceFile = true,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            AffectsModuleResolution = true,
            // The checker emits an error when it sees JSX but this option is not set in compilerOptions.
            // This is effectively a semantic error, so mark this option as affecting semantic diagnostics
            // so we know to refresh errors when this option is changed.
            AffectsSemanticDiagnostics = true,
            ShowInSimplifiedHelpView = true,
            Category = Language_and_Environment,
            Description = Specify_what_JSX_code_is_generated,
            DefaultValueDescription = Tristate.TSUnknown,
        },
        new()
        {
            Name = "outFile",
            Kind = CommandLineOptionKind.String,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            AffectsDeclarationPath = true,
            IsFilePath = true,
            ShowInSimplifiedHelpView = true,
            Category = Emit,
            Description = Specify_a_file_that_bundles_all_outputs_into_one_JavaScript_file_If_declaration_is_true_also_designates_a_file_that_bundles_all_d_ts_output,
            _transpileOptionValue = Tristate.TSUnknown,
        },
        new()
        {
            Name = "outDir",
            Kind = CommandLineOptionKind.String,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            AffectsDeclarationPath = true,
            IsFilePath = true,
            ShowInSimplifiedHelpView = true,
            Category = Emit,
            Description = Specify_an_output_folder_for_all_emitted_files,
        },
        new()
        {
            Name = "rootDir",
            Kind = CommandLineOptionKind.String,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            AffectsDeclarationPath = true,
            IsFilePath = true,
            Category = Modules,
            Description = Specify_the_root_folder_within_your_source_files,
            DefaultValueDescription = Computed_from_the_list_of_input_files,
        },
        new()
        {
            Name = "composite",
            Kind = CommandLineOptionKind.Boolean,
            // Not setting affectsEmit because we calculate this flag might not affect full emit
            AffectsBuildInfo = true,
            IsTSConfigOnly = true,
            Category = Projects,
            _transpileOptionValue = Tristate.TSUnknown,
            DefaultValueDescription = false,
            Description = Enable_constraints_that_allow_a_TypeScript_project_to_be_used_with_project_references,
        },
        new()
        {
            Name = "tsBuildInfoFile",
            Kind = CommandLineOptionKind.String,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            IsFilePath = true,
            Category = Projects,
            _transpileOptionValue = Tristate.TSUnknown,
            DefaultValueDescription = ".tsbuildinfo",
            Description = Specify_the_path_to_tsbuildinfo_incremental_compilation_file,
        },
        new()
        {
            Name = "removeComments",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = Emit,
            DefaultValueDescription = false,
            Description = Disable_emitting_comments,
        },
        new()
        {
            Name = "importHelpers",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            AffectsSourceFile = true,
            Category = Emit,
            Description = Allow_importing_helper_functions_from_tslib_once_per_project_instead_of_including_them_per_file,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "downlevelIteration",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Emit_more_compliant_but_verbose_and_less_performant_JavaScript_for_iteration,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "isolatedModules",
            Kind = CommandLineOptionKind.Boolean,
            Category = Interop_Constraints,
            Description = Ensure_that_each_file_can_be_safely_transpiled_without_relying_on_other_imports,
            _transpileOptionValue = Tristate.TSTrue,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "verbatimModuleSyntax",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Interop_Constraints,
            Description = Do_not_transform_or_elide_any_imports_or_exports_not_marked_as_type_only_ensuring_they_are_written_in_the_output_file_s_format_based_on_the_module_setting,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "isolatedDeclarations",
            Kind = CommandLineOptionKind.Boolean,
            Category = Interop_Constraints,
            Description = Require_sufficient_annotation_on_exports_so_other_tools_can_trivially_generate_declaration_files,
            DefaultValueDescription = false,
            AffectsBuildInfo = true,
            AffectsSemanticDiagnostics = true,
        },
        new()
        {
            Name = "erasableSyntaxOnly",
            Kind = CommandLineOptionKind.Boolean,
            Category = Interop_Constraints,
            Description = Do_not_allow_runtime_constructs_that_are_not_part_of_ECMAScript,
            DefaultValueDescription = false,
            AffectsBuildInfo = true,
            AffectsSemanticDiagnostics = true,
        },
        new()
        {
            Name = "libReplacement",
            Kind = CommandLineOptionKind.Boolean,
            AffectsProgramStructure = true,
            Category = Language_and_Environment,
            Description = Enable_lib_replacement,
            DefaultValueDescription = false,
        },
        // Strict Type Checks
        new()
        {
            Name = "strict",
            Kind = CommandLineOptionKind.Boolean,
            // Though this affects semantic diagnostics, affectsSemanticDiagnostics is not set here
            // The value of each strictFlag depends on own strictFlag value or this and never accessed directly.
            // But we need to store `strict` in builf info, even though it won't be examined directly, so that the
            // flags it controls (e.g. `strictNullChecks`) will be retrieved correctly
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = Type_Checking,
            Description = Enable_all_strict_type_checking_options,
            DefaultValueDescription = true,
        },
        new()
        {
            Name = "noImplicitAny",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            _strictFlag = true,
            Category = Type_Checking,
            Description = Enable_error_reporting_for_expressions_and_declarations_with_an_implied_any_type,
            DefaultValueDescription = X_true_unless_strict_is_false,
        },
        new()
        {
            Name = "strictNullChecks",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            _strictFlag = true,
            Category = Type_Checking,
            Description = When_type_checking_take_into_account_null_and_undefined,
            DefaultValueDescription = X_true_unless_strict_is_false,
        },
        new()
        {
            Name = "strictFunctionTypes",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            _strictFlag = true,
            Category = Type_Checking,
            Description = When_assigning_functions_check_to_ensure_parameters_and_the_return_values_are_subtype_compatible,
            DefaultValueDescription = X_true_unless_strict_is_false,
        },
        new()
        {
            Name = "strictBindCallApply",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            _strictFlag = true,
            Category = Type_Checking,
            Description = Check_that_the_arguments_for_bind_call_and_apply_methods_match_the_original_function,
            DefaultValueDescription = X_true_unless_strict_is_false,
        },
        new()
        {
            Name = "strictPropertyInitialization",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            _strictFlag = true,
            Category = Type_Checking,
            Description = Check_for_class_properties_that_are_declared_but_not_set_in_the_constructor,
            DefaultValueDescription = X_true_unless_strict_is_false,
        },
        new()
        {
            Name = "strictBuiltinIteratorReturn",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            _strictFlag = true,
            Category = Type_Checking,
            Description = Built_in_iterators_are_instantiated_with_a_TReturn_type_of_undefined_instead_of_any,
            DefaultValueDescription = X_true_unless_strict_is_false,
        },
        new()
        {
            Name = "noImplicitThis",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            _strictFlag = true,
            Category = Type_Checking,
            Description = Enable_error_reporting_when_this_is_given_the_type_any,
            DefaultValueDescription = X_true_unless_strict_is_false,
        },
        new()
        {
            Name = "useUnknownInCatchVariables",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            _strictFlag = true,
            Category = Type_Checking,
            Description = Default_catch_clause_variables_as_unknown_instead_of_any,
            DefaultValueDescription = X_true_unless_strict_is_false,
        },
        new()
        {
            Name = "alwaysStrict",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSourceFile = true,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Ensure_use_strict_is_always_emitted,
            DefaultValueDescription = true,
        },
        new()
        {
            Name = "stableTypeOrdering",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Ensure_types_are_ordered_stably_and_deterministically_across_compilations,
            DefaultValueDescription = true,
        },
        // Additional Checks
        new()
        {
            Name = "noUnusedLocals",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Enable_error_reporting_when_local_variables_aren_t_read,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noUnusedParameters",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Raise_an_error_when_a_function_parameter_isn_t_read,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "exactOptionalPropertyTypes",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Interpret_optional_property_types_as_written_rather_than_adding_undefined,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noImplicitReturns",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Enable_error_reporting_for_codepaths_that_do_not_explicitly_return_in_a_function,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noFallthroughCasesInSwitch",
            Kind = CommandLineOptionKind.Boolean,
            AffectsBindDiagnostics = true,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Enable_error_reporting_for_fallthrough_cases_in_switch_statements,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noUncheckedIndexedAccess",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Add_undefined_to_a_type_when_accessed_using_an_index,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noImplicitOverride",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Ensure_overriding_members_in_derived_classes_are_marked_with_an_override_modifier,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noPropertyAccessFromIndexSignature",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = false,
            Category = Type_Checking,
            Description = Enforces_using_indexed_accessors_for_keys_declared_using_an_indexed_type,
            DefaultValueDescription = false,
        },
        // Module Resolution
        new()
        {
            Name = "moduleResolution",
            Kind = CommandLineOptionKind.Enum,
            //    new Map(Object.entries({
            //         // N.B. The first entry specifies the value shown in `tsc --init`
            //         node10: ModuleResolutionKind.Node10,
            //         node: ModuleResolutionKind.Node10,
            //         classic: ModuleResolutionKind.Classic,
            //         node16: ModuleResolutionKind.Node16,
            //         nodenext: ModuleResolutionKind.NodeNext,
            //         bundler: ModuleResolutionKind.Bundler,
            //     })),
            AffectsModuleResolution = true,
            Category = Modules,
            Description = Specify_how_TypeScript_looks_up_a_file_from_a_given_module_specifier,
            DefaultValueDescription = X_nodenext_if_module_is_nodenext_node16_if_module_is_node16_or_node18_otherwise_bundler,
        },
        new()
        {
            Name = "baseUrl",
            Kind = CommandLineOptionKind.String,
            AffectsModuleResolution = true,
            IsFilePath = true,
            Category = Modules,
            Description = Specify_the_base_directory_to_resolve_non_relative_module_names,
        },
        new()
        {
            // this option can only be specified in tsconfig.json
            // use type = object to copy the value as-is
            Name = "paths",
            Kind = CommandLineOptionKind.Object,
            AffectsModuleResolution = true,
            _allowConfigDirTemplateSubstitution = true,
            IsTSConfigOnly = true,
            Category = Modules,
            Description = Specify_a_set_of_entries_that_re_map_imports_to_additional_lookup_locations,
            _transpileOptionValue = Tristate.TSUnknown,
        },
        new()
        {
            // this option can only be specified in tsconfig.json
            // use type = object to copy the value as-is
            Name = "rootDirs",
            Kind = CommandLineOptionKind.List,
            IsTSConfigOnly = true,
            AffectsModuleResolution = true,
            _allowConfigDirTemplateSubstitution = true,
            Category = Modules,
            Description = Allow_multiple_folders_to_be_treated_as_one_when_resolving_modules,
            _transpileOptionValue = Tristate.TSUnknown,
            DefaultValueDescription = Computed_from_the_list_of_input_files,
        },
        new()
        {
            Name = "typeRoots",
            Kind = CommandLineOptionKind.List,
            AffectsModuleResolution = true,
            _allowConfigDirTemplateSubstitution = true,
            Category = Modules,
            Description = Specify_multiple_folders_that_act_like_Slashnode_modules_Slash_types,
        },
        new()
        {
            Name = "types",
            Kind = CommandLineOptionKind.List,
            AffectsProgramStructure = true,
            ShowInSimplifiedHelpView = true,
            Category = Modules,
            Description = Specify_type_package_names_to_be_included_without_being_referenced_in_a_source_file,
            _transpileOptionValue = Tristate.TSUnknown,
        },
        new()
        {
            Name = "allowSyntheticDefaultImports",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Interop_Constraints,
            Description = Allow_import_x_from_y_when_a_module_doesn_t_have_a_default_export,
            DefaultValueDescription = true,
        },
        new()
        {
            Name = "esModuleInterop",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            ShowInSimplifiedHelpView = true,
            Category = Interop_Constraints,
            Description = Emit_additional_JavaScript_to_ease_support_for_importing_CommonJS_modules_This_enables_allowSyntheticDefaultImports_for_type_compatibility,
            DefaultValueDescription = true,
        },
        new()
        {
            Name = "preserveSymlinks",
            Kind = CommandLineOptionKind.Boolean,
            Category = Interop_Constraints,
            Description = Disable_resolving_symlinks_to_their_realpath_This_correlates_to_the_same_flag_in_node,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "allowUmdGlobalAccess",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Modules,
            Description = Allow_accessing_UMD_globals_from_modules,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "moduleSuffixes",
            Kind = CommandLineOptionKind.List,
            _listPreserveFalsyValues = true,
            AffectsModuleResolution = true,
            Category = Modules,
            Description = List_of_file_name_suffixes_to_search_when_resolving_a_module,
        },
        new()
        {
            Name = "allowImportingTsExtensions",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Modules,
            Description = Allow_imports_to_include_TypeScript_file_extensions_Requires_moduleResolution_bundler_and_either_noEmit_or_emitDeclarationOnly_to_be_set,
            DefaultValueDescription = false,
            _transpileOptionValue = Tristate.TSUnknown,
        },
        new()
        {
            Name = "rewriteRelativeImportExtensions",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Modules,
            Description = Rewrite_ts_tsx_mts_and_cts_file_extensions_in_relative_import_paths_to_their_JavaScript_equivalent_in_output_files,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "resolvePackageJsonExports",
            Kind = CommandLineOptionKind.Boolean,
            AffectsModuleResolution = true,
            Category = Modules,
            Description = Use_the_package_json_exports_field_when_resolving_package_imports,
            DefaultValueDescription = X_true_when_moduleResolution_is_node16_nodenext_or_bundler_otherwise_false,
        },
        new()
        {
            Name = "resolvePackageJsonImports",
            Kind = CommandLineOptionKind.Boolean,
            AffectsModuleResolution = true,
            Category = Modules,
            Description = Use_the_package_json_imports_field_when_resolving_imports,
            DefaultValueDescription = X_true_when_moduleResolution_is_node16_nodenext_or_bundler_otherwise_false,
        },
        new()
        {
            Name = "customConditions",
            Kind = CommandLineOptionKind.List,
            AffectsModuleResolution = true,
            Category = Modules,
            Description = Conditions_to_set_in_addition_to_the_resolver_specific_defaults_when_resolving_imports,
        },
        new()
        {
            Name = "noUncheckedSideEffectImports",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Modules,
            Description = Check_side_effect_imports,
            DefaultValueDescription = true,
        },
        // Source Maps
        new()
        {
            Name = "sourceRoot",
            Kind = CommandLineOptionKind.String,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Specify_the_root_path_for_debuggers_to_find_the_reference_source_code,
        },
        new()
        {
            Name = "mapRoot",
            Kind = CommandLineOptionKind.String,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Specify_the_location_where_debugger_should_locate_map_files_instead_of_generated_locations,
        },
        new()
        {
            Name = "inlineSources",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Include_source_code_in_the_sourcemaps_inside_the_emitted_JavaScript,
            DefaultValueDescription = false,
        },
        // Experimental
        new()
        {
            Name = "experimentalDecorators",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Language_and_Environment,
            Description = Enable_experimental_support_for_legacy_experimental_decorators,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "emitDecoratorMetadata",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Language_and_Environment,
            Description = Emit_design_type_metadata_for_decorated_declarations_in_source_files,
            DefaultValueDescription = false,
        },
        // Advanced
        new()
        {
            Name = "jsxFactory",
            Kind = CommandLineOptionKind.String,
            Category = Language_and_Environment,
            Description = Specify_the_JSX_factory_function_used_when_targeting_React_JSX_emit_e_g_React_createElement_or_h,
            DefaultValueDescription = "`React.createElement`",
        },
        new()
        {
            Name = "jsxFragmentFactory",
            Kind = CommandLineOptionKind.String,
            Category = Language_and_Environment,
            Description = Specify_the_JSX_Fragment_reference_used_for_fragments_when_targeting_React_JSX_emit_e_g_React_Fragment_or_Fragment,
            DefaultValueDescription = "React.Fragment",
        },
        new()
        {
            Name = "jsxImportSource",
            Kind = CommandLineOptionKind.String,
            AffectsSemanticDiagnostics = true,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            AffectsModuleResolution = true,
            AffectsSourceFile = true,
            Category = Language_and_Environment,
            Description = Specify_module_specifier_used_to_import_the_JSX_factory_functions_when_using_jsx_Colon_react_jsx_Asterisk,
            DefaultValueDescription = "react",
        },
        new()
        {
            Name = "resolveJsonModule",
            Kind = CommandLineOptionKind.Boolean,
            AffectsModuleResolution = true,
            Category = Modules,
            Description = Enable_importing_json_files,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "allowArbitraryExtensions",
            Kind = CommandLineOptionKind.Boolean,
            AffectsProgramStructure = true,
            Category = Modules,
            Description = Enable_importing_files_with_any_extension_provided_a_declaration_file_is_present,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "reactNamespace",
            Kind = CommandLineOptionKind.String,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Language_and_Environment,
            Description = Specify_the_object_invoked_for_createElement_This_only_applies_when_targeting_react_JSX_emit,
            DefaultValueDescription = "`React`",
        },
        new()
        {
            Name = "skipDefaultLibCheck",
            Kind = CommandLineOptionKind.Boolean,
            // We need to store these to determine whether `lib` files need to be rechecked
            AffectsBuildInfo = true,
            Category = Completeness,
            Description = Skip_type_checking_d_ts_files_that_are_included_with_TypeScript,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "emitBOM",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Emit_a_UTF_8_Byte_Order_Mark_BOM_in_the_beginning_of_output_files,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "newLine",
            // Go: // newLineOptionMap,
            Kind = CommandLineOptionKind.Enum,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Set_the_newline_character_for_emitting_files,
            DefaultValueDescription = "lf",
        },
        new()
        {
            Name = "noErrorTruncation",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Output_Formatting,
            Description = Disable_truncating_types_in_error_messages,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noLib",
            Kind = CommandLineOptionKind.Boolean,
            Category = Language_and_Environment,
            AffectsProgramStructure = true,
            Description = Disable_including_any_library_files_including_the_default_lib_d_ts,
            // We are not returning a sourceFile for lib file when asked by the program,
            // so pass --noLib to avoid reporting a file not found error.
            _transpileOptionValue = Tristate.TSTrue,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noResolve",
            Kind = CommandLineOptionKind.Boolean,
            AffectsModuleResolution = true,
            Category = Modules,
            Description = Disallow_import_s_require_s_or_reference_s_from_expanding_the_number_of_files_TypeScript_should_add_to_a_project,
            // We are not doing a full typecheck, we are not resolving the whole context,
            // so pass --noResolve to avoid reporting missing file errors.
            _transpileOptionValue = Tristate.TSTrue,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "stripInternal",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Disable_emitting_declarations_that_have_internal_in_their_JSDoc_comments,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "disableSizeLimit",
            Kind = CommandLineOptionKind.Boolean,
            AffectsProgramStructure = true,
            Category = Editor_Support,
            Description = Remove_the_20mb_cap_on_total_source_code_size_for_JavaScript_files_in_the_TypeScript_language_server,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "disableSourceOfProjectReferenceRedirect",
            Kind = CommandLineOptionKind.Boolean,
            IsTSConfigOnly = true,
            Category = Projects,
            Description = Disable_preferring_source_files_instead_of_declaration_files_when_referencing_composite_projects,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "disableSolutionSearching",
            Kind = CommandLineOptionKind.Boolean,
            IsTSConfigOnly = true,
            Category = Projects,
            Description = Opt_a_project_out_of_multi_project_reference_checking_when_editing,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "disableReferencedProjectLoad",
            Kind = CommandLineOptionKind.Boolean,
            IsTSConfigOnly = true,
            Category = Projects,
            Description = Reduce_the_number_of_projects_loaded_automatically_by_TypeScript,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noEmitHelpers",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Disable_generating_custom_helper_functions_like_extends_in_compiled_output,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "noEmitOnError",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            _transpileOptionValue = Tristate.TSUnknown,
            Description = Disable_emitting_files_if_any_type_checking_errors_are_reported,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "preserveConstEnums",
            Kind = CommandLineOptionKind.Boolean,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Emit,
            Description = Disable_erasing_const_enum_declarations_in_generated_code,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "declarationDir",
            Kind = CommandLineOptionKind.String,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            AffectsDeclarationPath = true,
            IsFilePath = true,
            Category = Emit,
            _transpileOptionValue = Tristate.TSUnknown,
            Description = Specify_the_output_directory_for_generated_declaration_files,
        },
        new()
        {
            Name = "skipLibCheck",
            Kind = CommandLineOptionKind.Boolean,
            // We need to store these to determine whether `lib` files need to be rechecked
            AffectsBuildInfo = true,
            Category = Completeness,
            Description = Skip_type_checking_all_d_ts_files,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "allowUnusedLabels",
            Kind = CommandLineOptionKind.Boolean,
            AffectsBindDiagnostics = true,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Disable_error_reporting_for_unused_labels,
            DefaultValueDescription = Tristate.TSUnknown,
        },
        new()
        {
            Name = "allowUnreachableCode",
            Kind = CommandLineOptionKind.Boolean,
            AffectsBindDiagnostics = true,
            AffectsSemanticDiagnostics = true,
            AffectsBuildInfo = true,
            Category = Type_Checking,
            Description = Disable_error_reporting_for_unreachable_code,
            DefaultValueDescription = Tristate.TSUnknown,
        },
        new()
        {
            Name = "forceConsistentCasingInFileNames",
            Kind = CommandLineOptionKind.Boolean,
            AffectsModuleResolution = true,
            Category = Interop_Constraints,
            Description = Ensure_that_casing_is_correct_in_imports,
            DefaultValueDescription = true,
        },
        new()
        {
            Name = "maxNodeModuleJsDepth",
            Kind = CommandLineOptionKind.Number,
            AffectsModuleResolution = true,
            Category = JavaScript_Support,
            Description = Specify_the_maximum_folder_depth_used_for_checking_JavaScript_files_from_node_modules_Only_applicable_with_allowJs,
            DefaultValueDescription = 0,
        },
        new()
        {
            Name = "useDefineForClassFields",
            Kind = CommandLineOptionKind.Boolean,
            AffectsSemanticDiagnostics = true,
            AffectsEmit = true,
            AffectsBuildInfo = true,
            Category = Language_and_Environment,
            Description = Emit_ECMAScript_standard_compliant_class_fields,
            DefaultValueDescription = X_true_for_ES2022_and_above_including_ESNext,
        },
        new()
        {
            // A list of plugins to load in the language service
            Name = "plugins",
            Kind = CommandLineOptionKind.List,
            IsTSConfigOnly = true,
            Description = Specify_a_list_of_language_service_plugins_to_include,
            Category = Editor_Support,
        },
        new()
        {
            Name = "moduleDetection",
            Kind = CommandLineOptionKind.Enum,
            AffectsSourceFile = true,
            AffectsModuleResolution = true,
            Description = Control_what_method_is_used_to_detect_module_format_JS_files,
            Category = Language_and_Environment,
            DefaultValueDescription = X_auto_Colon_Treat_files_with_imports_exports_import_meta_jsx_with_jsx_Colon_react_jsx_or_esm_format_with_module_Colon_node16_as_modules,
        },
        new()
        {
            Name = "ignoreDeprecations",
            Kind = CommandLineOptionKind.String,
            DefaultValueDescription = Tristate.TSUnknown,
        },
    ];

    // Go: declscompiler.go:11（var OptionsDeclarations = slices.Concat(...)，见文件头 5）
    public static readonly CommandLineOption[] OptionsDeclarations = [.. CommonOptionsWithBuild, .. OptionsForCompiler];

    // Go: optionsHaveChanges（declscompiler.go:1213-1232）
    // Go 用 reflect 取 oldOptions 的第 i 个字段与新值比较；C# 用显式字段表（下标一一对应，见文件头 6）。
    internal static bool OptionsHaveChanges(CompilerOptions? oldOptions, CompilerOptions? newOptions, Func<CommandLineOption, bool> declFilter)
    {
        if (ReferenceEquals(oldOptions, newOptions))
        {
            return false;
        }
        if (oldOptions is null || newOptions is null)
        {
            return true;
        }
        // Go: ForEachCompilerOptionValue(newOptions, declFilter, func(option, value, i) {
        //         newValue := value.Interface(); oldValue := oldOptionsValue.Field(i).Interface(); ... })
        // C# 的 `value` 就是 newOptions 的字段值（= Go 的 newValue），oldOptions 的对应字段按同一下标 i 从字段表取。
        return ForEachCompilerOptionValue(newOptions, declFilter, (option, value, i) =>
        {
            var field = CompilerOptionFields[i];
            if (option._strictFlag)
            {
                // Go: oldOptions.GetStrictOptionValue(oldValue.(core.Tristate)) != newOptions.GetStrictOptionValue(newValue.(core.Tristate))
                return oldOptions.GetStrictOptionValue(field.GetTristate!(oldOptions)) != newOptions.GetStrictOptionValue(field.GetTristate!(newOptions));
            }
            if (option._allowJsFlag)
            {
                return oldOptions.GetAllowJS() != newOptions.GetAllowJS();
            }
            // Go: !reflect.DeepEqual(newValue, oldValue)
            return field.Changed(oldOptions, newOptions);
        });
    }

    // Go: ForEachCompilerOptionValue（declscompiler.go:1234-1248）
    public static bool ForEachCompilerOptionValue(CompilerOptions options, Func<CommandLineOption, bool> declFilter, Func<CommandLineOption, object?, int, bool> fn)
    {
        // Go: optionsValue := reflect.ValueOf(options).Elem(); for i := range optionsValue.NumField()
        for (var i = 0; i < CompilerOptionFields.Length; i++)
        {
            var field = CompilerOptionFields[i];
            var optionDeclaration = CommandLineCompilerOptionsMap.Get(field.Name);
            if (optionDeclaration is not null && declFilter(optionDeclaration) && fn(optionDeclaration, field.Get(options), i))
            {
                return true;
            }
        }
        return false;
    }

    // Go: CompilerOptionsAffectSemanticDiagnostics（declscompiler.go:1250-1257）
    public static bool CompilerOptionsAffectSemanticDiagnostics(CompilerOptions? oldOptions, CompilerOptions? newOptions)
    {
        return OptionsHaveChanges(oldOptions, newOptions, static option => option.AffectsSemanticDiagnostics);
    }

    // Go: CompilerOptionsAffectDeclarationPath（declscompiler.go:1259-1266）
    public static bool CompilerOptionsAffectDeclarationPath(CompilerOptions? oldOptions, CompilerOptions? newOptions)
    {
        return OptionsHaveChanges(oldOptions, newOptions, static option => option.AffectsDeclarationPath);
    }

    // Go: CompilerOptionsAffectEmit（declscompiler.go:1268-1272）
    public static bool CompilerOptionsAffectEmit(CompilerOptions? oldOptions, CompilerOptions? newOptions)
    {
        return OptionsHaveChanges(oldOptions, newOptions, static option => option.AffectsEmit);
    }
}
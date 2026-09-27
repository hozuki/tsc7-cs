// ported from: internal/tsoptions/diagnostics.go
//
// 映射约定（docs/architecture.md §2/§3.2）：
// 1) Go 里 `ParseCommandLineWorkerDiagnostics.didYouMean` 是**值**结构体 → C# 用 class 字段（引用语义；只有本包构造，
//    没有"复制结构体"的调用点）。未导出字段 → `internal` 的 `_camelCase`。
// 2) `optionsNameMap` + `sync.Once`（`optionsNameMapOnce`）在 Go 里**只有声明、没有任何读取点**（全仓库 grep 确认），
//    属于死代码，故不移植（见报告"未移植符号"）。
// 3) 包级 var 里 `CompilerOptionsDidYouMeanDiagnostics` 是导出的 → `public static`；另两个未导出 → `internal static`。
//    它们都引用 decls*.go（别的 partial 文件）的声明表，故用 `Lazy<T>` 规避 partial 跨文件初始化顺序未定义的问题。
using TypeScript.Diagnostics;
using static TypeScript.Diagnostics.Diagnostics;

namespace TypeScript.TsOptions;

public sealed class DidYouMeanOptionsDiagnostics
{
    internal AlternateModeDiagnostics? _alternateMode;
    public CommandLineOption[] OptionDeclarations = [];
    public Message? UnknownOptionDiagnostic;
    public Message? UnknownDidYouMeanDiagnostic;
}

public sealed class AlternateModeDiagnostics
{
    internal Message? _diagnostic;
    internal NameMap? _optionsNameMap;
}

public sealed class ParseCommandLineWorkerDiagnostics
{
    internal DidYouMeanOptionsDiagnostics _didYouMean = new();
    public Message? OptionTypeMismatchDiagnostic;
}

public static partial class TsOptions
{
    public static ParseCommandLineWorkerDiagnostics CompilerOptionsDidYouMeanDiagnostics =>
        _compilerOptionsDidYouMeanDiagnostics.Value;

    private static readonly Lazy<ParseCommandLineWorkerDiagnostics> _compilerOptionsDidYouMeanDiagnostics =
        new(() => GetParseCommandLineWorkerDiagnostics(OptionsDeclarations));

    internal static ParseCommandLineWorkerDiagnostics GetParseCommandLineWorkerDiagnostics(CommandLineOption[] decls)
    {
        // this will only return the correct diagnostics for `compiler` mode, and is factored into a function for testing reasons.
        return new ParseCommandLineWorkerDiagnostics
        {
            _didYouMean = new DidYouMeanOptionsDiagnostics
            {
                _alternateMode = new AlternateModeDiagnostics
                {
                    _diagnostic = Compiler_option_0_may_only_be_used_with_build,
                    _optionsNameMap = BuildNameMap,
                },
                OptionDeclarations = decls,
                UnknownOptionDiagnostic = Unknown_compiler_option_0,
                UnknownDidYouMeanDiagnostic = Unknown_compiler_option_0_Did_you_mean_1,
            },
            OptionTypeMismatchDiagnostic = Compiler_option_0_expects_an_argument,
        };
    }

    internal static ParseCommandLineWorkerDiagnostics WatchOptionsDidYouMeanDiagnostics =>
        _watchOptionsDidYouMeanDiagnostics.Value;

    private static readonly Lazy<ParseCommandLineWorkerDiagnostics> _watchOptionsDidYouMeanDiagnostics =
        new(() => new ParseCommandLineWorkerDiagnostics
        {
            _didYouMean = new DidYouMeanOptionsDiagnostics
            {
                // no alternateMode
                OptionDeclarations = OptionsForWatch,
                UnknownOptionDiagnostic = Unknown_watch_option_0,
                UnknownDidYouMeanDiagnostic = Unknown_watch_option_0_Did_you_mean_1,
            },
            OptionTypeMismatchDiagnostic = Watch_option_0_requires_a_value_of_type_1,
        });

    internal static ParseCommandLineWorkerDiagnostics BuildOptionsDidYouMeanDiagnostics =>
        _buildOptionsDidYouMeanDiagnostics.Value;

    private static readonly Lazy<ParseCommandLineWorkerDiagnostics> _buildOptionsDidYouMeanDiagnostics =
        new(() => new ParseCommandLineWorkerDiagnostics
        {
            _didYouMean = new DidYouMeanOptionsDiagnostics
            {
                _alternateMode = new AlternateModeDiagnostics
                {
                    _diagnostic = Compiler_option_0_may_not_be_used_with_build,
                    _optionsNameMap = CompilerNameMap,
                },
                OptionDeclarations = BuildOpts,
                UnknownOptionDiagnostic = Unknown_build_option_0,
                UnknownDidYouMeanDiagnostic = Unknown_build_option_0_Did_you_mean_1,
            },
            OptionTypeMismatchDiagnostic = Build_option_0_requires_a_value_of_type_1,
        });
}

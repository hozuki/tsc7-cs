// ported from: internal/tsoptions/errors.go
//
// 映射约定（docs/architecture.md §2/§3.2）：
// 1) Go 的 `args ...any` → `params object?[] args`；`*ast.SourceFile`/`*ast.Node`/`*diagnostics.Message` 可空参数 →
//    `SourceFile?`/`Node?`/`Message?`（Go 的 nil 显式传入即"没有源码位置"，走 compiler diagnostic 分支）。
// 2) Go 的可变参数里 `slices.Collect(opt.EnumMap().Keys())` → 数组（`[.. map.Keys()]`）。
// 3) `strings.ToLower`/`strings.Join` 见 §3.4/§3.2：前者用 `StringUtil.ToLowerJS`（JS 语义），后者用 `string.Join`。
// 4) `createDiagnosticForInvalidEnumType`/`formatEnumTypeKeys`/`getCompilerOptionValueTypeString`/`createUnknownOptionError`
//    /`extraKeyDiagnostics`/`extraKeyDidYouMeanDiagnostics` 在 Go 里未导出 → C# `internal`（PascalCase 方法名；
//    tsconfigparsing.go 分片按这些名字调用）。`CreateDiagnosticForNodeInSourceFile[OrCompilerDiagnostic]` 导出 → public。
// 5) Go 里 `option.Elements()`/`opt.EnumMap()` 为 nil 时是 panic（nil 解引用）；C# 侧用 `!` 保留同样前提并注明。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    internal static Diagnostic CreateDiagnosticForInvalidEnumType(CommandLineOption opt, SourceFile? sourceFile, Node? node)
    {
        var enumMap = opt.EnumMap()!; // Go: opt.EnumMap() 直接解引用（enum 类型选项必有 enum map）
        var namesOfType = enumMap.Keys().ToArray();
        var stringNames = FormatEnumTypeKeys(opt, namesOfType);
        var optName = "--" + opt.Name;
        return CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(sourceFile, node, Argument_for_0_option_must_be_Colon_1, optName, stringNames);
    }

    internal static string FormatEnumTypeKeys(CommandLineOption opt, string[] keys)
    {
        var deprecatedKeys = opt.DeprecatedKeys();
        if (deprecatedKeys is not null)
        {
            keys = Filter(keys, key => !deprecatedKeys.Has(key));
        }
        return "'" + string.Join("', '", keys) + "'";
    }

    internal static string GetCompilerOptionValueTypeString(CommandLineOption option)
    {
        switch (option.Kind)
        {
            case CommandLineOptionKind.ListOrElement:
                return GetCompilerOptionValueTypeString(option.Elements()!) + " or Array";
            case CommandLineOptionKind.List:
                return "Array";
            default:
                return option.Kind.String();
        }
    }

    // createUnknownOptionError creates a diagnostic for an unknown option. If
    // unknownDidYouMeanDiagnostic and optionsNameMap are provided, it also checks
    // for a spelling suggestion and emits a "did you mean" diagnostic instead.
    internal static Diagnostic CreateUnknownOptionError(
        string unknownOption,
        Message unknownOptionDiagnostic,
        string unknownOptionErrorText, // optional
        Node? node, // optional
        SourceFile? sourceFile, // optional
        AlternateModeDiagnostics? alternateMode, // optional
        Message? unknownDidYouMeanDiagnostic, // optional; null skips suggestion
        TsOptions.CommandLineOptionNameMap? optionsNameMap) // optional; null skips suggestion
    {
        if (alternateMode is not null && alternateMode._optionsNameMap is not null)
        {
            var otherOption = alternateMode._optionsNameMap.Get(ToLowerJS(unknownOption));
            if (otherOption is not null)
            {
                // tscbuildoption
                // Go: `alternateMode.diagnostic` 在两处构造（Diagnostics.cs）里都显式赋值，恒非 nil。
                var diagnostic = alternateMode._diagnostic!;
                if (otherOption.Name == "build")
                {
                    diagnostic = Option_build_must_be_the_first_command_line_argument;
                }
                return CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(sourceFile, node, diagnostic, unknownOption);
            }
        }
        if (unknownOptionErrorText == "")
        {
            unknownOptionErrorText = unknownOption;
        }
        if (unknownDidYouMeanDiagnostic is not null && optionsNameMap is not null)
        {
            var possibleOption = optionsNameMap.GetSpellingSuggestion(unknownOption);
            if (possibleOption is not null)
            {
                return CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(sourceFile, node, unknownDidYouMeanDiagnostic, unknownOptionErrorText, possibleOption.Name);
            }
        }
        return CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(sourceFile, node, unknownOptionDiagnostic, unknownOptionErrorText);
    }

    public static Diagnostic CreateDiagnosticForNodeInSourceFile(SourceFile sourceFile, Node node, Message message, params object?[] args)
    {
        return Ast.Ast.NewDiagnostic(sourceFile, NewTextRange(SkipTrivia(sourceFile.Text(), node.Loc.Pos()), node.End()), message, args);
    }

    public static Diagnostic CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(SourceFile? sourceFile, Node? node, Message message, params object?[] args)
    {
        if (sourceFile is not null && node is not null)
        {
            return CreateDiagnosticForNodeInSourceFile(sourceFile, node, message, args);
        }
        return Ast.Ast.NewCompilerDiagnostic(message, args);
    }

    internal static Message? ExtraKeyDiagnostics(string s) => s switch
    {
        "compilerOptions" => Unknown_compiler_option_0,
        "watchOptions" => Unknown_watch_option_0,
        "typeAcquisition" => Unknown_type_acquisition_option_0,
        "buildOptions" => Unknown_build_option_0,
        _ => null,
    };

    internal static Message? ExtraKeyDidYouMeanDiagnostics(string s) => s switch
    {
        "compilerOptions" => Unknown_compiler_option_0_Did_you_mean_1,
        "watchOptions" => Unknown_watch_option_0_Did_you_mean_1,
        "typeAcquisition" => Unknown_type_acquisition_option_0_Did_you_mean_1,
        "buildOptions" => Unknown_build_option_0_Did_you_mean_1,
        _ => null,
    };
}

internal sealed partial class CommandLineParser
{
    internal Diagnostic CreateUnknownOptionError(string unknownOption, string unknownOptionErrorText, Node? node, SourceFile? sourceFile)
    {
        return TsOptions.CreateUnknownOptionError(
            unknownOption,
            UnknownOptionDiagnostic()!,
            unknownOptionErrorText,
            node,
            sourceFile,
            AlternateMode(),
            UnknownDidYouMeanDiagnostic(),
            TsOptions.CommandLineOptionsToMap(_workerDiagnostics._didYouMean.OptionDeclarations));
    }
}

// ported from: internal/tsoptions/commandlineoption.go
//
// 映射约定（docs/architecture.md §2/§3）：
// 1) Go 的 `type CommandLineOptionKind string` + 一组字符串常量 → C# `enum CommandLineOptionKind`（§3.3：成员名去掉
//    `CommandLineOptionType` 前缀）。**偏离说明**：Go 的底层类型是 string（`opt.Kind == "boolean"`、`string(option.Kind)`
//    会把 "listOrElement"/"enum" 这类字面量写进诊断），C# enum 没有字符串值，所以照 §3.3 的 stringer 约定补一个
//    扩展方法 `String(this CommandLineOptionKind)`，逐字返回 Go 的字面量（"string"/"number"/"boolean"/"object"/
//    "list"/"listOrElement"/"enum"）；调用点照 Go 写 `option.Kind.String()` 或 `kind.String()`。
// 2) Go 的 `type extraValidation string`（值 ""/"spec"/"locale"）同上 → `enum ExtraValidation`（None/Spec/Locale）。
//    它的字符串值从不参与输出（只在 tsconfigparsing.go 的 switch 里比较），所以不需要 stringer。
// 3) Go 的 `type CompilerOptionsValue any` 是别名 → C# 无别名，一律直接写 `object?`（与 harnessutil 的用法一致）。
// 4) 未导出字段（extraValidation/minValue/allowConfigDirTemplateSubstitution/allowJsFlag/strictFlag/
//    transpileOptionValue/listPreserveFalsyValues）→ `internal` 的 `_camelCase` 字段：decls*.go、tsconfigparsing.go
//    两个分片会在对象初始化器里设置它们、并读 `_extraValidation` / `_listPreserveFalsyValues` 等，名字必须一致
//    （见移植报告的"跨分片共享成员名"一节）。导出字段 `ElementOptions` 照抄 Go 名字。
// 5) `DeprecatedKeys/EnumMap/Elements` 返回 nil 的分支保留：Go 的 nil map/指针 → C# `?` 返回类型。
using System;
using System.Collections.Generic;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Collections.Collections;

namespace TypeScript.TsOptions;

/// <summary>Go 的 <c>type CommandLineOptionKind string</c>（字面量见 <c>String(this CommandLineOptionKind)</c>）。</summary>
public enum CommandLineOptionKind
{
    String,
    Number,
    Boolean,
    Object,
    List,
    ListOrElement,
    Enum, // map
}

/// <summary>Go 的 <c>type extraValidation string</c>（只用于 <c>validateJsonOptionValue</c> 的分支）。</summary>
public enum ExtraValidation
{
    None,
    Spec,
    Locale,
}

public sealed class CommandLineOption
{
    public string Name = "";
    public string ShortName = "";
    public CommandLineOptionKind Kind;

    // used in parsing
    public bool IsFilePath;
    public bool IsTSConfigOnly;
    public bool IsCommandLineOnly;

    // used in output
    public Message? Description;
    public object? DefaultValueDescription;
    public bool ShowInSimplifiedHelpView;

    // used in output in serializing and generate tsconfig
    public Message? Category;

    // What kind of extra validation `validateJsonOptionValue` should do
    internal ExtraValidation _extraValidation;

    // checks that option with number type has value >= minValue
    internal int _minValue;

    // true or undefined
    // used for configDirTemplateSubstitutionOptions
    internal bool _allowConfigDirTemplateSubstitution;

    // used for filter in compilerrunner
    public bool AffectsDeclarationPath;
    public bool AffectsProgramStructure;
    public bool AffectsSemanticDiagnostics;
    public bool AffectsBuildInfo;
    public bool AffectsBindDiagnostics;
    public bool AffectsSourceFile;
    public bool AffectsModuleResolution;
    public bool AffectsEmit;

    internal bool _allowJsFlag;
    internal bool _strictFlag;

    // used in transpileoptions worker
    // todo: revisit to see if this can be reduced to boolean
    internal Tristate _transpileOptionValue;

    // used for CommandLineOptionTypeList
    internal bool _listPreserveFalsyValues;

    // used for compilerOptionsDeclaration
    public TsOptions.CommandLineOptionNameMap? ElementOptions;

    public Set<string>? DeprecatedKeys()
    {
        if (Kind != CommandLineOptionKind.Enum)
        {
            return null;
        }
        return TsOptions._commandLineOptionDeprecated[Name];
    }

    public OrderedMap<string, object?>? EnumMap()
    {
        if (Kind != CommandLineOptionKind.Enum)
        {
            return null;
        }
        return TsOptions._commandLineOptionEnumMap.Value[Name];
    }

    public CommandLineOption? Elements()
    {
        if (Kind != CommandLineOptionKind.List && Kind != CommandLineOptionKind.ListOrElement)
        {
            return null;
        }
        return TsOptions._commandLineOptionElements[Name];
    }

    public bool DisallowNullOrUndefined() => Name == "extends";
}

public static partial class TsOptions
{
    /// <summary>Go 的 <c>string(CommandLineOptionKind)</c>：诊断里出现的字面量必须与 Go 逐字一致。</summary>
    public static string String(this CommandLineOptionKind kind) => kind switch
    {
        CommandLineOptionKind.String => "string",
        CommandLineOptionKind.Number => "number",
        CommandLineOptionKind.Boolean => "boolean",
        CommandLineOptionKind.Object => "object",
        CommandLineOptionKind.List => "list",
        CommandLineOptionKind.ListOrElement => "listOrElement",
        CommandLineOptionKind.Enum => "enum",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // CommandLineOption.Elements()
    internal static readonly Dictionary<string, CommandLineOption> _commandLineOptionElements = new()
    {
        ["lib"] = new()
        {
            Name = "lib",
            Kind = CommandLineOptionKind.Enum, // libMap,
            DefaultValueDescription = Tristate.TSUnknown,
        },
        ["rootDirs"] = new()
        {
            Name = "rootDirs",
            Kind = CommandLineOptionKind.String,
            IsFilePath = true,
        },
        ["typeRoots"] = new()
        {
            Name = "typeRoots",
            Kind = CommandLineOptionKind.String,
            IsFilePath = true,
        },
        ["types"] = new()
        {
            Name = "types",
            Kind = CommandLineOptionKind.String,
        },
        ["moduleSuffixes"] = new()
        {
            Name = "moduleSuffixes",
            Kind = CommandLineOptionKind.String,
        },
        ["customConditions"] = new()
        {
            Name = "condition",
            Kind = CommandLineOptionKind.String,
        },
        ["plugins"] = new()
        {
            Name = "plugin",
            Kind = CommandLineOptionKind.Object,
        },
        // For tsconfig root options
        ["references"] = new()
        {
            Name = "references",
            Kind = CommandLineOptionKind.Object,
        },
        ["contentMappers"] = new()
        {
            Name = "contentMappers",
            Kind = CommandLineOptionKind.Object,
        },
        ["files"] = new()
        {
            Name = "files",
            Kind = CommandLineOptionKind.String,
        },
        ["include"] = new()
        {
            Name = "include",
            Kind = CommandLineOptionKind.String,
        },
        ["exclude"] = new()
        {
            Name = "exclude",
            Kind = CommandLineOptionKind.String,
        },
        ["extends"] = new()
        {
            Name = "extends",
            Kind = CommandLineOptionKind.String,
        },
        // For Watch options
        ["excludeDirectories"] = new()
        {
            Name = "excludeDirectory",
            Kind = CommandLineOptionKind.String,
            IsFilePath = true,
            _extraValidation = ExtraValidation.Spec,
        },
        ["excludeFiles"] = new()
        {
            Name = "excludeFile",
            Kind = CommandLineOptionKind.String,
            IsFilePath = true,
            _extraValidation = ExtraValidation.Spec,
        },
        // Test infra options
        ["libFiles"] = new()
        {
            Name = "libFiles",
            Kind = CommandLineOptionKind.String,
        },
    };

    // CommandLineOption.EnumMap()
    // Lazy：值跨 partial 文件引用 EnumMaps.cs 的 `LibMap`/`_moduleResolutionOptionMap` 等，而 C# 的 partial 类型
    // 在不同文件里的字段初始化顺序未定义（在工厂里读取时机晚于全部字段初始化，故安全）。
    internal static readonly Lazy<Dictionary<string, OrderedMap<string, object?>>> _commandLineOptionEnumMap =
        new(() => new Dictionary<string, OrderedMap<string, object?>>
        {
            ["lib"] = LibMap!,
            ["moduleResolution"] = _moduleResolutionOptionMap!,
            ["module"] = _moduleOptionMap!,
            ["target"] = _targetOptionMap!,
            ["moduleDetection"] = _moduleDetectionOptionMap!,
            ["jsx"] = _jsxOptionMap!,
            ["newLine"] = _newLineOptionMap!,
            ["watchFile"] = _watchFileEnumMap!,
            ["watchDirectory"] = _watchDirectoryEnumMap!,
            ["fallbackPolling"] = _fallbackEnumMap!,
        });

    // CommandLineOption.DeprecatedKeys()
    internal static readonly Dictionary<string, Set<string>> _commandLineOptionDeprecated = new()
    {
        ["module"] = NewSetFromItems("none", "amd", "system", "umd"),
        ["moduleResolution"] = NewSetFromItems("node", "classic", "node10"),
        ["target"] = NewSetFromItems("es5"),
    };
}

// ported from: internal/ls/string_completions.go:1-2247
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **同名协议类型**：本命名空间里已有本地的 `CompletionItem`/`CompletionList`（completions.go 的补全条目聚合类型，
//    见 `Ls/Completions.cs`），C# 中同命名空间的类型优先于 using 导入 ⇒ Go 的 `*lsproto.CompletionItem` 一律写
//    `LsProto.CompletionItem`；Go 的 `*CompletionList`（本地类型，含 `IsIncomplete`/`ItemDefaults`/`Items`）裸写
//    `CompletionList`。`*lsproto.Range` 仍用文件头别名 `Range`。
// 2) Go 的 `iter.Seq[string]`（`addCompletionEntriesFromPathsOrExportsOrImports` 的 `keys` 形参）在 C# 里落成
//    `IEnumerable<string>`（`OrderedMap.Keys()` 正好返回它）；`getPatternsForKey func(key string) []string` →
//    `Func<string, string[]>`；`comparePaths func(a, b string) stringutil.Comparison` → `System.Comparison<string>`
//    （StringUtil/Compare.cs 已冻结：`func(a,b string) Comparison` 就是 `Comparison<string>`）。
// 3) `map[string]moduleCompletionNameAndKind` → `Dictionary<string, ModuleCompletionNameAndKind>`；
//    `slices.Collect(maps.Values(result.names))` → `[.. result._names.Values]`。C# 字典按键的插入序枚举，Go 是随机序；
//    这些名字最终由客户端排序（Go 侧同样无序），无行为差异。
// 4) `type moduleCompletionKind int` / `type referenceKind int` → `internal enum ModuleCompletionKind` / `ReferenceKind`；
//    Go 的 `existing.kind < entry.kind` 是数值比较，C# 的枚举没有关系运算符 ⇒ 写 `(int)… < (int)…`（数值照抄：0/1/2 与 0/1）。
// 5) `(T, bool)` → C# 元组（`PathCompletionReplacementSpan`、`ParseTripleSlashDirectiveFragment`）；`*T` → `T?`。
// 6) 闭包返回 `(any, bool)`（`ForEachAncestorDirectoryStoppingAtGlobalCache` 的回调）→ `Func<string, (object? Result, bool Stop)>`；
//    Go 在闭包里给 `ancestorLookup` 重新赋值 ⇒ C# 不能就地写 local function，落成可重新赋值的委托变量。
// 7) `strings.TrimPrefix` / `strings.LastIndexFunc` / `strings.TrimLeftFunc` 本仓没有对应 helper（先例
//    `Scanner/Utilities.cs:262` 的 `TrimLeftWhiteSpaceLike` 也是就地写的）：`TrimPrefix` 写成 `StartsWith + 切片`、
//    `LastIndexFunc` 写成倒序下标循环、`TrimLeftFunc` 写成局部裁剪循环；`strings.TrimSpace` → `string.Trim()`。
// 8) `core.ResolutionMode` → C# `ModuleKind`（ModuleSpecifiers/Types.cs 冻结的映射）；`core.ResolutionModeNone` → `ModuleKind.None`。
// 9) `tspath.TrimFilePathPrefix(path, prefix, useCaseSensitive)`（Go 的 `(string, bool)`）→ 本仓
//    `TryTrimFilePathPrefix(path, prefix, useCaseSensitive, out suffix)`。
// 10) `program.CommonSourceDirectory`（Go 里是 `func() string` 字段）→ C# 方法组 `program.CommonSourceDirectory`。
// 11) `checker.StringLiteralType` 在本仓没有对应接口（`Type` 即其实现）⇒ `GetStringLiteralTypes`/`FromContextualType`
//     用 `Type[]`（探针桩的自查签名也是 `Type[]`）。
// 12) `packagejson.` 前缀：`ExportsOrImports`/`JSONValueType`/`JSONValue` 直接裸名（`using TypeScript.PackageJson;`）；
//     Go 的 `packagejson.JSONValueTypeObject` → `JSONValueType.Object`。**字段组要走子对象**：Go 里 `Imports`/`Exports`/
//     `TypesVersions` 直接挂在 package.json 内容 struct 上，本仓按 Go 侧的 `PathFields`/`DependencyFields` 分组
//     ⇒ 写 `Contents.PathFields.Imports|Exports|TypesVersions`、`Contents.DependencyFields.RangeDependencies(...)`。
// 13) `tsoptions.GetSupportedExtensions*` → `using static TypeScript.TsOptions.TsOptions;` 后裸名
//     （本仓这两个包级函数落在 `TypeScript.TsOptions.TsOptions` 上；它同时是命名空间名 ⇒ **不能**写成 `TsOptions.X` 限定）；
//     `module.` 的 GetConditions/ComparePatternKeys/UnmangleScopedPackageName/IsApplicableVersionedTypesKey/
//     TryGetJSExtensionForFile 用 `using static TypeScript.Module.Module;` 后裸名。
// 14) `printer.QuoteChar` → `QuoteChar`（`using static TypeScript.Printer.Printer;`），成员 `Backtick/SingleQuote/DoubleQuote`。
// 15) `panic(err)` → `throw err;`（Go 的 panic 以值终止控制流，C# 侧的对应物就是把该异常抛出去；仓库既有做法是
//     `panic(其他值)` → `Debug.Fail`/`DebugFailureException`，但这里 Go 抛的本来就是 error 值）。
// 16) `pathCompletion`/`pathCompletions`/`stringLiteralCompletions`/`completionsFromTypes`/`completionsFromProperties`/
//     `moduleCompletionNameAndKindSet`/`moduleCompletionNameAndKind`/`extensionOptions` 都是 struct → `internal sealed class`；
//     Go 未导出字段 → `_camelCase`（artifacts/ls-g1-helpers.md §0）。
// 17) Go 的 `map[string]bool`（`seen`）→ `Dictionary<string, bool>`；Go 的 `map[key]bool`（`deduplicateModuleCompletions`）
//     键是**匿名 struct** ⇒ C# 用值语义元组 `(string Name, ModuleCompletionKind Kind, string Extension)` 当字典键。
// 18) `println`/`fmt.Sprintf` 只在 panic 消息里出现 → `$"…"`。
// 19) 本文件不调 `l.host` 的可空成员：`_host` 恒非空（见 `Ls/LanguageService.cs`）。
// 20) Go 的指针相等（`parent.Name() == node`、`node == ast.SkipParentheses(argumentExpression)`、
//     `typeNode != current`）显式落成 `ReferenceEquals(...)`（`Node` 未重载 `==`，显式写更贴合 Go 的指针语义）；
//     `ast.NodeFactory{}` 用文件头别名 `NodeFactory = TypeScript.Ast.NodeFactory`（`TypeScript.Printer.NodeFactory` 同名）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Module;
using TypeScript.ModuleSpecifiers;
using TypeScript.PackageJson;
using TypeScript.Printer;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using TypeScript.StringUtil;
using TypeScript.TsOptions;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Ls.Ls;
using static TypeScript.Module.Module;
using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using CheckerType = TypeScript.Checker.Checker;
using Kind = TypeScript.Ast.Kind;
using ModuleSpecifiersUserPreferences = TypeScript.ModuleSpecifiers.UserPreferences;
using NodeFactory = TypeScript.Ast.NodeFactory;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

/// <summary>Go: <c>type completionsFromTypes struct</c>（string_completions.go:28-31）。</summary>
internal sealed class CompletionsFromTypes
{
    internal Type[] _types = [];
    internal bool _isNewIdentifier;
}

/// <summary>Go: <c>type completionsFromProperties struct</c>（string_completions.go:33-36）。</summary>
internal sealed class CompletionsFromProperties
{
    internal Symbol[] _symbols = [];
    internal bool _hasIndexSignature;
}

/// <summary>Go: <c>type pathCompletion struct</c>（string_completions.go:38-42）。</summary>
internal sealed class PathCompletion
{
    internal string _name = "";
    internal ScriptElementKind _kind;
    internal string _extension = "";
}

/// <summary>Go: <c>type pathCompletions struct</c>（string_completions.go:44-47）。</summary>
internal sealed class PathCompletions
{
    internal PathCompletion[] _entries = [];
    internal Range? _replacementSpan;
}

/// <summary>Go: <c>type stringLiteralCompletions struct</c>（string_completions.go:49-53）。</summary>
internal sealed class StringLiteralCompletions
{
    internal CompletionsFromTypes? _fromTypes;
    internal CompletionsFromProperties? _fromProperties;
    internal PathCompletions? _fromPaths;
}

/// <summary>Go: <c>type moduleCompletionKind int</c>（string_completions.go:1224-1230）：数值照抄 iota。</summary>
internal enum ModuleCompletionKind
{
    Directory = 0,
    File = 1,
    ExternalModuleName = 2,
}

/// <summary>Go: <c>type moduleCompletionNameAndKind struct</c>（string_completions.go:1232-1236）。</summary>
internal sealed class ModuleCompletionNameAndKind
{
    internal string _name = "";
    internal ModuleCompletionKind _kind;
    internal string _extension = "";
}

/// <summary>Go: <c>type moduleCompletionNameAndKindSet struct</c>（string_completions.go:1238-1253）。
/// Go 的 `map[string]moduleCompletionNameAndKind` 语义：同名条目只保留 kind 更大的那个（`names` 是键值 map，
/// 不是集合）。</summary>
internal sealed class ModuleCompletionNameAndKindSet
{
    internal Dictionary<string, ModuleCompletionNameAndKind> _names = [];

    /// <summary>Go: <c>func (s *moduleCompletionNameAndKindSet) add(entry moduleCompletionNameAndKind)</c>（string_completions.go:1249-1254）。</summary>
    internal void Add(ModuleCompletionNameAndKind entry)
    {
        if (!_names.TryGetValue(entry._name, out var existing) || (int)existing._kind < (int)entry._kind)
        {
            _names[entry._name] = entry;
        }
    }
}

/// <summary>Go: <c>type extensionOptions struct</c>（string_completions.go:1256-1262）。</summary>
internal sealed class ExtensionOptions
{
    internal string[] _extensionsToSearch = [];
    internal ReferenceKind _referenceKind;
    internal SourceFile _importingSourceFile = null!;
    internal ImportModuleSpecifierEndingPreference _endingPreference;
    internal ModuleKind _resolutionMode;
}

/// <summary>Go: <c>type referenceKind int</c>（string_completions.go:1264-1270）：数值照抄 iota。</summary>
internal enum ReferenceKind
{
    FileName = 0,
    ModuleSpecifier = 1,
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) getStringLiteralCompletions(ctx, file, position, contextToken, checker, compilerOptions, includeSymbols) *CompletionList</c>（string_completions.go:57-93）。</summary>
    internal CompletionList? GetStringLiteralCompletions(
        Context ctx,
        SourceFile file,
        int position,
        Node? contextToken,
        CheckerType checker,
        CompilerOptions compilerOptions,
        bool includeSymbols)
    {
        if (IsInReferenceComment(file, position))
        {
            var completion = GetTripleSlashReferenceCompletions(file, position, GetProgram()!, checker);
            return ConvertPathCompletions(ctx, completion, file, position);
        }

        if (IsInString(file, position, contextToken))
        {
            if (contextToken is null || !IsStringLiteralLike(contextToken))
            {
                return null;
            }

            var entries = GetStringLiteralCompletionEntries(
                ctx,
                file,
                contextToken,
                position,
                checker);
            return ConvertStringLiteralCompletions(
                ctx,
                entries,
                contextToken,
                file,
                position,
                checker,
                compilerOptions,
                includeSymbols);
        }

        return null;
    }

    /// <summary>Go: <c>func (l *LanguageService) convertStringLiteralCompletions(ctx, completion, contextToken, file, position, typeChecker, options, includeSymbols) *CompletionList</c>（string_completions.go:95-204）。</summary>
    internal CompletionList? ConvertStringLiteralCompletions(
        Context ctx,
        StringLiteralCompletions? completion,
        Node contextToken,
        SourceFile file,
        int position,
        CheckerType typeChecker,
        CompilerOptions options,
        bool includeSymbols)
    {
        if (completion is null)
        {
            return null;
        }

        var optionalReplacementRange = CreateRangeFromStringLiteralLikeContent(file, contextToken, position);
        if (completion._fromPaths is not null)
        {
            return ConvertPathCompletions(ctx, completion._fromPaths, file, position);
        }

        if (completion._fromProperties is not null)
        {
            var fromProperties = completion._fromProperties;
            var data = new CompletionDataData
            {
                _symbols = fromProperties._symbols,
                _completionKind = CompletionKind.String,
                _isNewIdentifierLocation = fromProperties._hasIndexSignature,
                _location = file.AsNode(),
                _contextToken = contextToken,
            };
            var (_, items, err) = GetCompletionEntriesFromSymbols(
                ctx,
                typeChecker,
                data,
                contextToken, /*replacementToken*/
                position,
                file,
                options,
                includeSymbols /*includeSymbols*/);
            if (err is not null)
            {
                throw err; // Go: panic(err)
            }

            var defaultCommitCharacters = GetDefaultCommitCharacters(fromProperties._hasIndexSignature);
            var itemDefaults = SetItemDefaults(
                ctx,
                position,
                file,
                items,
                defaultCommitCharacters,
                optionalReplacementRange!);
            return new CompletionList
            {
                IsIncomplete = false,
                ItemDefaults = itemDefaults,
                Items = items,
            };
        }

        if (completion._fromTypes is not null)
        {
            var fromTypes = completion._fromTypes;
            QuoteChar quoteChar;
            if (contextToken.Kind == Kind.NoSubstitutionTemplateLiteral)
            {
                quoteChar = QuoteChar.Backtick;
            }
            else if (contextToken.Text().StartsWith("'", StringComparison.Ordinal))
            {
                quoteChar = QuoteChar.SingleQuote;
            }
            else
            {
                quoteChar = QuoteChar.DoubleQuote;
            }

            var items = Map(fromTypes._types, t =>
            {
                var name = EscapeString((string)t.AsLiteralType()!.Value()!, quoteChar);
                var lspItem = CreateLSPCompletionItem(
                    ctx,
                    name,
                    "", /*insertText*/
                    "", /*filterText*/
                    SortTextLocationPriority,
                    ScriptElementKind.String,
                    ScriptElementKindModifier.None,
                    GetReplacementRangeForContextToken(file, contextToken, position),
                    null!, /*commitCharacters*/
                    null!, /*labelDetails*/
                    file,
                    position,
                    false, /*isMemberCompletion*/
                    false, /*isSnippet*/
                    false, /*hasAction*/
                    false, /*preselect*/
                    "",    /*source*/
                    null!, /*autoImportEntryData*/
                    null!, /*additionalTextEdits*/
                    null /*detail*/);
                return new CompletionItem
                {
                    _completionItem = lspItem,
                };
            });
            var defaultCommitCharacters = GetDefaultCommitCharacters(fromTypes._isNewIdentifier);
            var itemDefaults = SetItemDefaults(
                ctx,
                position,
                file,
                items,
                defaultCommitCharacters,
                null! /*optionalReplacementSpan*/);
            return new CompletionList
            {
                IsIncomplete = false,
                ItemDefaults = itemDefaults,
                Items = items,
            };
        }

        return null;
    }

    /// <summary>Go: <c>func (l *LanguageService) convertPathCompletions(ctx, completion, file, position) *CompletionList</c>（string_completions.go:206-261）。</summary>
    internal CompletionList? ConvertPathCompletions(
        Context ctx,
        PathCompletions? completion,
        SourceFile file,
        int position)
    {
        if (completion is null)
        {
            return null;
        }

        var isNewIdentifierLocation = true; // The user may type in a path that doesn't yet exist, creating a "new identifier" with respect to the collection of identifiers the server is aware of.
        var defaultCommitCharacters = GetDefaultCommitCharacters(isNewIdentifierLocation);
        var items = Map(completion._entries, pathCompletion =>
        {
            var detail = pathCompletion._name;
            if (!pathCompletion._name.EndsWith(pathCompletion._extension, StringComparison.Ordinal))
            {
                detail += pathCompletion._extension;
            }

            var lspItem = CreateLSPCompletionItem(
                ctx,
                pathCompletion._name,
                "", /*insertText*/
                "", /*filterText*/
                SortTextLocationPriority,
                pathCompletion._kind,
                KindModifiersFromExtension(pathCompletion._extension),
                completion._replacementSpan!,
                null!, /*commitCharacters*/
                null!, /*labelDetails*/
                file,
                position,
                false, /*isMemberCompletion*/
                false, /*isSnippet*/
                false, /*hasAction*/
                false, /*preselect*/
                "",    /*source*/
                null!, /*autoImportEntryData*/
                null!, /*additionalTextEdits*/
                detail);
            return new CompletionItem
            {
                _completionItem = lspItem,
            };
        });
        var itemDefaults = SetItemDefaults(
            ctx,
            position,
            file,
            items,
            defaultCommitCharacters,
            null! /*optionalReplacementSpan*/);
        return new CompletionList
        {
            IsIncomplete = false,
            ItemDefaults = itemDefaults,
            Items = items,
        };
    }

    /// <summary>Go: <c>func (l *LanguageService) getStringLiteralCompletionEntries(ctx, file, node, position, typeChecker) *stringLiteralCompletions</c>（string_completions.go:263-438）。</summary>
    internal StringLiteralCompletions? GetStringLiteralCompletionEntries(
        Context ctx,
        SourceFile file,
        Node node,
        int position,
        CheckerType typeChecker)
    {
        var parent = WalkUpParentheses(node.Parent!);
        switch (parent.Kind)
        {
            case Kind.LiteralType:
            {
                var grandparent = WalkUpParentheses(parent.Parent!);
                if (grandparent.Kind == Kind.ImportType)
                {
                    return GetStringLiteralCompletionsFromModuleNames(
                        file,
                        node,
                        GetProgram()!,
                        typeChecker);
                }

                return FromUnionableLiteralType(grandparent, parent, position, typeChecker);
            }

            case Kind.PropertyAssignment:
            {
                if (IsObjectLiteralExpression(parent.Parent!) && ReferenceEquals(parent.Name(), node))
                {
                    // Get quoted name of properties of the object literal expression
                    // i.e. interface ConfigFiles {
                    //          'jspm:dev': string
                    //      }
                    //      let files: ConfigFiles = {
                    //          '/*completion position*/'
                    //      }
                    //
                    //      function foo(c: ConfigFiles) {}
                    //      foo({
                    //          '/*completion position*/'
                    //      });
                    return new StringLiteralCompletions
                    {
                        _fromProperties = StringLiteralCompletionsForObjectLiteral(typeChecker, parent.Parent!),
                    };
                }

                if (FindAncestor(parent.Parent, IsCallLikeExpression) is not null)
                {
                    var uniques = new Set<string>();
                    Type[] stringLiteralTypes =
                    [
                        .. GetStringLiteralTypes(typeChecker.GetContextualType(node, ContextFlags.None), uniques, typeChecker),
                        .. GetStringLiteralTypes(typeChecker.GetContextualType(node, ContextFlags.IgnoreNodeInferences), uniques, typeChecker),
                    ];
                    return ToStringLiteralCompletionsFromTypes(stringLiteralTypes);
                }

                return new StringLiteralCompletions
                {
                    _fromTypes = FromContextualType(ContextFlags.None, node, typeChecker),
                };
            }

            case Kind.ElementAccessExpression:
            {
                var expression = parent.Expression()!;
                var argumentExpression = parent.AsElementAccessExpression()!.ArgumentExpression;
                if (ReferenceEquals(node, SkipParentheses(argumentExpression)))
                {
                    // Get all names of properties on the expression
                    // i.e. interface A {
                    //      'prop1': string
                    // }
                    // let a: A;
                    // a['/*completion position*/']
                    var t = typeChecker.GetTypeAtLocation(expression);
                    return new StringLiteralCompletions
                    {
                        _fromProperties = StringLiteralCompletionsFromProperties(t, typeChecker),
                    };
                }

                return null;
            }

            case Kind.CallExpression:
            case Kind.NewExpression:
            case Kind.JsxAttribute:
            {
                if (!IsRequireCallArgument(node) && !IsImportCall(parent))
                {
                    Node argumentNode;
                    if (parent.Kind == Kind.JsxAttribute)
                    {
                        argumentNode = parent.Parent!;
                    }
                    else
                    {
                        argumentNode = node;
                    }

                    var argumentInfo = GetArgumentInfoForCompletions(argumentNode, position, file, typeChecker);
                    // Get string literal completions from specialized signatures of the target
                    // i.e. declare function f(a: 'A');
                    // f("/*completion position*/")
                    if (argumentInfo is null)
                    {
                        return null;
                    }

                    var result = GetStringLiteralCompletionsFromSignature(argumentInfo._invocation, node, argumentInfo, typeChecker);
                    if (result is not null)
                    {
                        return new StringLiteralCompletions
                        {
                            _fromTypes = result,
                        };
                    }

                    return new StringLiteralCompletions
                    {
                        _fromTypes = FromContextualType(ContextFlags.None, node, typeChecker),
                    };
                }

                // is `require("")` or `require(""` or `import("")`
                goto case Kind.ImportDeclaration;
            }

            case Kind.ImportDeclaration:
            case Kind.ExportDeclaration:
            case Kind.ExternalModuleReference:
            case Kind.JSDocImportTag:
            {
                // Get all known external module names or complete a path to a module
                // i.e. import * as ns from "/*completion position*/";
                //      var y = import("/*completion position*/");
                //      import x = require("/*completion position*/");
                //      var y = require("/*completion position*/");
                //      export * from "/*completion position*/";
                return GetStringLiteralCompletionsFromModuleNames(file, node, GetProgram()!, typeChecker);
            }

            case Kind.CaseClause:
            {
                var tracker = NewCaseClauseTracker(typeChecker, parent.Parent!.AsCaseBlock()!.Clauses?.Nodes);
                var contextualTypes = FromContextualType(ContextFlags.IgnoreNodeInferences, node, typeChecker);
                if (contextualTypes is null)
                {
                    return null;
                }

                var literals = Filter(contextualTypes._types, t => !tracker.HasValue(t.AsLiteralType()!.Value()!));
                return new StringLiteralCompletions
                {
                    _fromTypes = new CompletionsFromTypes
                    {
                        _types = literals,
                        _isNewIdentifier = false,
                    },
                };
            }

            case Kind.ImportSpecifier:
            case Kind.ExportSpecifier:
            {
                // Complete string aliases in `import { "|" } from` and `export { "|" } from`
                var specifier = parent;
                if (specifier.PropertyName() is { } propertyName && !ReferenceEquals(node, propertyName))
                {
                    return null; // Don't complete in `export { "..." as "|" } from`
                }

                var namedImportsOrExports = specifier.Parent!;
                Node? moduleSpecifier;
                if (namedImportsOrExports.Kind == Kind.NamedImports)
                {
                    moduleSpecifier = namedImportsOrExports.Parent!.Parent;
                }
                else
                {
                    moduleSpecifier = namedImportsOrExports.Parent;
                }

                if (moduleSpecifier is null)
                {
                    return null;
                }

                var moduleSpecifierSymbol = typeChecker.GetSymbolAtLocation(moduleSpecifier);
                if (moduleSpecifierSymbol is null)
                {
                    return null;
                }

                var exports = typeChecker.GetExportsAndPropertiesOfModule(moduleSpecifierSymbol);
                var existing = NewSetFromItems(Map(namedImportsOrExports.Elements(), n => n.PropertyNameOrName()!.Text()));
                var uniques = Filter(exports, e => e.Name != InternalSymbolNameDefault && !existing.Has(e.Name));
                return new StringLiteralCompletions
                {
                    _fromProperties = new CompletionsFromProperties
                    {
                        _symbols = uniques,
                        _hasIndexSignature = false,
                    },
                };
            }

            case Kind.BinaryExpression:
            {
                if (parent.AsBinaryExpression()!.OperatorToken!.Kind == Kind.InKeyword)
                {
                    var t = typeChecker.GetTypeAtLocation(parent.AsBinaryExpression()!.Right!);
                    var properties = GetPropertiesForCompletion(t, typeChecker);
                    return new StringLiteralCompletions
                    {
                        _fromProperties = new CompletionsFromProperties
                        {
                            _symbols = Filter(properties, s => s.ValueDeclaration is null || !IsPrivateIdentifierClassElementDeclaration(s.ValueDeclaration)),
                            _hasIndexSignature = false,
                        },
                    };
                }

                return new StringLiteralCompletions
                {
                    _fromTypes = FromContextualType(ContextFlags.None, node, typeChecker),
                };
            }

            default:
            {
                var result = FromContextualType(ContextFlags.IgnoreNodeInferences, node, typeChecker);
                if (result is not null)
                {
                    return new StringLiteralCompletions
                    {
                        _fromTypes = result,
                    };
                }

                return new StringLiteralCompletions
                {
                    _fromTypes = FromContextualType(ContextFlags.None, node, typeChecker),
                };
            }
        }
    }
}

public static partial class Ls
{
    /// <summary>Go: <c>func fromContextualType(contextFlags checker.ContextFlags, node *ast.Node, typeChecker *checker.Checker) *completionsFromTypes</c>（string_completions.go:440-444）。</summary>
    internal static CompletionsFromTypes? FromContextualType(ContextFlags contextFlags, Node node, CheckerType typeChecker)
    {
        // Get completion for string literal from string literal type
        // i.e. var x: "hi" | "hello" = "/*completion position*/"
        return ToCompletionsFromTypes(GetStringLiteralTypes(GetContextualTypeFromParent(node, typeChecker, contextFlags), null, typeChecker));
    }

    /// <summary>Go: <c>func toCompletionsFromTypes(types []*checker.StringLiteralType) *completionsFromTypes</c>（string_completions.go:446-454）。</summary>
    internal static CompletionsFromTypes? ToCompletionsFromTypes(Type[]? types)
    {
        if (types is null || types.Length == 0)
        {
            return null;
        }

        return new CompletionsFromTypes
        {
            _types = types,
            _isNewIdentifier = false,
        };
    }

    /// <summary>Go: <c>func toStringLiteralCompletionsFromTypes(types []*checker.StringLiteralType) *stringLiteralCompletions</c>（string_completions.go:456-464）。</summary>
    internal static StringLiteralCompletions? ToStringLiteralCompletionsFromTypes(Type[]? types)
    {
        var result = ToCompletionsFromTypes(types);
        if (result is null)
        {
            return null;
        }

        return new StringLiteralCompletions
        {
            _fromTypes = result,
        };
    }

    /// <summary>Go: <c>func fromUnionableLiteralType(grandparent, parent *ast.Node, position int, typeChecker *checker.Checker) *stringLiteralCompletions</c>（string_completions.go:466-553）。</summary>
    internal static StringLiteralCompletions? FromUnionableLiteralType(
        Node grandparent,
        Node parent,
        int position,
        CheckerType typeChecker)
    {
        switch (grandparent.Kind)
        {
            case Kind.CallExpression:
            case Kind.ExpressionWithTypeArguments:
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
            case Kind.NewExpression:
            case Kind.TaggedTemplateExpression:
            case Kind.TypeReference:
            {
                var typeArgument = FindAncestor(parent, n => n.Parent == grandparent);
                if (typeArgument is not null)
                {
                    var t = typeChecker.GetTypeArgumentConstraint(typeArgument);
                    return new StringLiteralCompletions
                    {
                        _fromTypes = new CompletionsFromTypes
                        {
                            _types = GetStringLiteralTypes(t, null, typeChecker),
                            _isNewIdentifier = false,
                        },
                    };
                }

                return null;
            }

            case Kind.IndexedAccessType:
            {
                // Get all apparent property names
                // i.e. interface Foo {
                //          foo: string;
                //          bar: string;
                //      }
                //      let x: Foo["/*completion position*/"]
                var indexType = grandparent.AsIndexedAccessTypeNode()!.IndexType!;
                var objectType = grandparent.AsIndexedAccessTypeNode()!.ObjectType!;
                if (!indexType.Loc.ContainsInclusive(position))
                {
                    return null;
                }

                var t = typeChecker.GetTypeFromTypeNode(objectType);
                return new StringLiteralCompletions
                {
                    _fromProperties = StringLiteralCompletionsFromProperties(t, typeChecker),
                };
            }

            case Kind.UnionType:
            {
                var result = FromUnionableLiteralType(
                    WalkUpParentheses(grandparent.Parent!),
                    parent,
                    position,
                    typeChecker);
                if (result is null)
                {
                    return null;
                }

                var alreadyUsedTypes = GetAlreadyUsedTypesInStringLiteralUnion(grandparent.AsUnionTypeNode()!, parent.AsLiteralTypeNode()!);
                if (result._fromProperties is not null)
                {
                    var fromProperties = result._fromProperties;
                    return new StringLiteralCompletions
                    {
                        _fromProperties = new CompletionsFromProperties
                        {
                            _symbols = Filter(
                                fromProperties._symbols,
                                s => Array.IndexOf(alreadyUsedTypes, s.Name) < 0),
                            _hasIndexSignature = fromProperties._hasIndexSignature,
                        },
                    };
                }

                if (result._fromTypes is not null)
                {
                    var fromTypes = result._fromTypes;
                    return new StringLiteralCompletions
                    {
                        _fromTypes = new CompletionsFromTypes
                        {
                            _types = Filter(fromTypes._types, t => Array.IndexOf(alreadyUsedTypes, (string)t.AsLiteralType()!.Value()!) < 0),
                            _isNewIdentifier = false,
                        },
                    };
                }

                return null;
            }

            case Kind.PropertySignature:
            {
                return new StringLiteralCompletions
                {
                    _fromTypes = new CompletionsFromTypes
                    {
                        _types = GetStringLiteralTypes(GetConstraintOfTypeArgumentProperty(grandparent, typeChecker), null, typeChecker),
                        _isNewIdentifier = false,
                    },
                };
            }

            default:
            {
                return null;
            }
        }
    }

    /// <summary>Go: <c>func stringLiteralCompletionsForObjectLiteral(typeChecker *checker.Checker, objectLiteralExpression *ast.ObjectLiteralExpressionNode) *completionsFromProperties</c>（string_completions.go:555-576）。</summary>
    internal static CompletionsFromProperties? StringLiteralCompletionsForObjectLiteral(
        CheckerType typeChecker,
        Node objectLiteralExpression)
    {
        var contextualType = typeChecker.GetContextualType(objectLiteralExpression, ContextFlags.None);
        if (contextualType is null)
        {
            return null;
        }

        var completionsType = typeChecker.GetContextualType(objectLiteralExpression, ContextFlags.IgnoreNodeInferences);
        var symbols = GetPropertiesForObjectExpression(
            contextualType,
            completionsType!,
            objectLiteralExpression,
            typeChecker);

        return new CompletionsFromProperties
        {
            _symbols = symbols,
            _hasIndexSignature = HasIndexSignature(contextualType, typeChecker),
        };
    }

    /// <summary>Go: <c>func stringLiteralCompletionsFromProperties(t *checker.Type, typeChecker *checker.Checker) *completionsFromProperties</c>（string_completions.go:578-585）。</summary>
    internal static CompletionsFromProperties StringLiteralCompletionsFromProperties(Type t, CheckerType typeChecker)
    {
        return new CompletionsFromProperties
        {
            _symbols = Filter(typeChecker.GetApparentProperties(t), s => !(s.ValueDeclaration is not null && IsPrivateIdentifierClassElementDeclaration(s.ValueDeclaration))),
            _hasIndexSignature = HasIndexSignature(t, typeChecker),
        };
    }

    // ── 配置项名（overrides/module specifiers）───────────────────────────────────────────

    /// <summary>Go: <c>func getFragmentDirectory(fragment string) string</c>（string_completions.go:875-883）。</summary>
    internal static string GetFragmentDirectory(string fragment)
    {
        if (!ContainsSlash(fragment))
        {
            return "";
        }

        if (HasTrailingDirectorySeparator(fragment))
        {
            return fragment;
        }

        return GetDirectoryPath(fragment);
    }

    /// <summary>Go: <c>func getPatternFromFirstMatchingCondition(target *packagejson.ExportsOrImports, conditions []string) string</c>（string_completions.go:885-902）。</summary>
    internal static string GetPatternFromFirstMatchingCondition(ExportsOrImports target, string[] conditions)
    {
        if (target.Type == JSONValueType.String)
        {
            return target.AsString();
        }

        if (target.Type == JSONValueType.Object)
        {
            var obj = target.AsObject()!;
            foreach (var condition in obj.Keys())
            {
                if (condition == "default" || Array.IndexOf(conditions, condition) >= 0 ||
                    (Array.IndexOf(conditions, "types") >= 0 && IsApplicableVersionedTypesKey(condition)))
                {
                    var (pattern, ok) = obj.Get(condition);
                    if (ok)
                    {
                        return GetPatternFromFirstMatchingCondition(pattern!, conditions);
                    }
                }
            }
        }

        return "";
    }

    /// <summary>Go: <c>func getAmbientModuleCompletions(fragment string, fragmentDirectory string, typeChecker *checker.Checker) []string</c>（string_completions.go:904-921）。</summary>
    internal static string[] GetAmbientModuleCompletions(string fragment, string fragmentDirectory, CheckerType typeChecker)
    {
        var ambientModules = typeChecker.GetAmbientModules();
        var nonRelativeModuleNames = new List<string>();
        foreach (var sym in ambientModules)
        {
            var moduleName = GetAmbientModuleName(sym);
            if (moduleName.StartsWith(fragment, StringComparison.Ordinal) && !moduleName.Contains('*'))
            {
                nonRelativeModuleNames.Add(moduleName);
            }
        }

        if (fragmentDirectory != "")
        {
            var moduleNameWithSeparator = EnsureTrailingDirectorySeparator(fragmentDirectory);
            for (var i = 0; i < nonRelativeModuleNames.Count; i++)
            {
                var moduleName = nonRelativeModuleNames[i];
                nonRelativeModuleNames[i] = moduleName.StartsWith(moduleNameWithSeparator, StringComparison.Ordinal)
                    ? moduleName[moduleNameWithSeparator.Length..]
                    : moduleName;
            }
        }

        return nonRelativeModuleNames.ToArray();
    }

    /// <summary>Go: <c>func getAmbientModuleName(symbol *ast.Symbol) string</c>（string_completions.go:923-929）。</summary>
    internal static string GetAmbientModuleName(Symbol symbol)
    {
        var declaration = GetNonAugmentationDeclaration(symbol);
        if (declaration is not null && IsModuleWithStringLiteralName(declaration))
        {
            return declaration.Name()!.Text();
        }

        return StripQuotes(symbol.Name);
    }

    /// <summary>Go: <c>func tryRemoveDirectoryPrefix(path string, prefix string, useCaseSensitiveFileNames bool) *string</c>（string_completions.go:1000-1009）。</summary>
    internal static string? TryRemoveDirectoryPrefix(string path, string prefix, bool useCaseSensitiveFileNames)
    {
        if (!TryTrimFilePathPrefix(path, prefix, useCaseSensitiveFileNames, out var withoutPrefix))
        {
            return null;
        }

        if (withoutPrefix.StartsWith('/') || withoutPrefix.StartsWith('\\'))
        {
            withoutPrefix = withoutPrefix[1..];
        }

        return withoutPrefix;
    }

    /// <summary>Go: <c>func getSupportedExtensionsForModuleResolution(options *core.CompilerOptions, extraExtensions []string, checker *checker.Checker) []string</c>（string_completions.go:1050-1072）。</summary>
    internal static string[] GetSupportedExtensionsForModuleResolution(CompilerOptions options, string[] extraExtensions, CheckerType? checker)
    {
        /** file extensions from ambient modules declarations e.g. *.css */
        string[] extensions = [];
        if (checker is not null)
        {
            var ambientModules = checker.GetAmbientModules();
            foreach (var module in ambientModules)
            {
                var name = GetAmbientModuleName(module);
                if (!name.StartsWith("*.", StringComparison.Ordinal) || name.Contains('/'))
                {
                    continue;
                }

                extensions = [.. extensions, name[1..]];
            }
        }

        var supportedExtensions = GetSupportedExtensions(options, extraExtensions);
        foreach (var ext in supportedExtensions)
        {
            extensions = [.. extensions, .. ext];
        }

        var moduleResolution = options.GetModuleResolutionKind();
        if (ModuleResolutionUsesNodeModules(moduleResolution))
        {
            return Flatten(GetSupportedExtensionsWithJsonIfResolveJsonModule(options, [extensions]));
        }

        return extensions;
    }

    /// <summary>Go: <c>func moduleResolutionUsesNodeModules(moduleResolution core.ModuleResolutionKind) bool</c>（string_completions.go:1074-1078）。</summary>
    internal static bool ModuleResolutionUsesNodeModules(ModuleResolutionKind moduleResolution)
    {
        return ((int)moduleResolution >= (int)ModuleResolutionKind.Node16 && (int)moduleResolution <= (int)ModuleResolutionKind.NodeNext) ||
            moduleResolution == ModuleResolutionKind.Bundler;
    }

    /// <summary>Returns true if the path is explicitly relative (i.e. relative to . or ..)（string_completions.go:1080-1082）。</summary>
    internal static bool IsPathRelativeToScript(string path)
    {
        return path.StartsWith("./", StringComparison.Ordinal) || path.StartsWith("../", StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func getBaseDirectoriesFromRootDirs(rootDirs []string, basePath string, scriptDirectory string, ignoreCase bool) []string</c>（string_completions.go:1155-1193）。</summary>
    internal static string[] GetBaseDirectoriesFromRootDirs(string[] rootDirs, string basePath, string scriptDirectory, bool ignoreCase)
    {
        // Make all paths absolute/normalized if they are not already
        var normalizedRootDirs = new string[rootDirs.Length];
        for (var i = 0; i < rootDirs.Length; i++)
        {
            var rootDirectory = rootDirs[i];
            var normalizedPath = IsRootedDiskPath(rootDirectory)
                ? rootDirectory
                : CombinePaths(basePath, rootDirectory);
            normalizedRootDirs[i] = EnsureTrailingDirectorySeparator(NormalizePath(normalizedPath));
        }

        // Determine the path to the directory containing the script relative to the root directory it is contained within
        var relativeDirectory = "";
        var comparePathsOptions = new ComparePathsOptions(!ignoreCase, basePath);
        foreach (var rootDirectory in normalizedRootDirs)
        {
            if (ContainsPath(rootDirectory, scriptDirectory, comparePathsOptions))
            {
                relativeDirectory = rootDirectory.Length > scriptDirectory.Length
                    ? ""
                    : scriptDirectory[rootDirectory.Length..];
                break;
            }
        }

        // Now find a path for each potential directory that is to be merged with the one containing the script
        var directories = new List<string>();
        foreach (var rootDirectory in normalizedRootDirs)
        {
            directories.Add(RemoveTrailingDirectorySeparator(CombinePaths(rootDirectory, relativeDirectory)));
        }

        directories.Add(RemoveTrailingDirectorySeparator(scriptDirectory));

        return DeduplicateStrings(directories.ToArray());
    }

    /// <summary>Go: <c>func deduplicateStrings(slice []string) []string</c>（string_completions.go:1195-1208）。</summary>
    internal static string[] DeduplicateStrings(string[] slice)
    {
        if (slice.Length <= 1)
        {
            return slice;
        }

        var seen = new Dictionary<string, bool>();
        var result = new List<string>();
        foreach (var s in slice)
        {
            if (!seen.ContainsKey(s))
            {
                seen[s] = true;
                result.Add(s);
            }
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func deduplicateModuleCompletions(completions []moduleCompletionNameAndKind) []moduleCompletionNameAndKind</c>（string_completions.go:1210-1222）。
    /// Go 的匿名 struct 键（name/kind/extension 字段级值比较）→ C# 值语义元组。</summary>
    internal static ModuleCompletionNameAndKind[] DeduplicateModuleCompletions(ModuleCompletionNameAndKind[] completions)
    {
        if (completions.Length <= 1)
        {
            return completions;
        }

        var seen = new Dictionary<(string Name, ModuleCompletionKind Kind, string Extension), bool>();
        var result = new List<ModuleCompletionNameAndKind>();
        foreach (var c in completions)
        {
            var k = (Name: c._name, Kind: c._kind, Extension: c._extension);
            if (!seen.ContainsKey(k))
            {
                seen[k] = true;
                result.Add(c);
            }
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func getFileExtension(fileName string) string</c>（string_completions.go:1598-1605）。</summary>
    internal static string GetFileExtension(string fileName)
    {
        var extension = TryGetExtensionFromPath(fileName);
        if (extension == "")
        {
            extension = GetAnyExtensionFromPath(fileName, [], false /*ignoreCase*/);
        }

        return extension;
    }

    /// <summary>Go: <c>func containsSlash(fragment string) bool</c>（string_completions.go:1822-1824）。</summary>
    internal static bool ContainsSlash(string fragment)
    {
        return fragment.Contains(DirectorySeparator);
    }

    /// <summary>Go: <c>func withoutStartAndEnd(s string, start string, end string) *string</c>（string_completions.go:1826-1832）。</summary>
    internal static string? WithoutStartAndEnd(string s, string start, string end)
    {
        if (s.StartsWith(start, StringComparison.Ordinal) && s.EndsWith(end, StringComparison.Ordinal) && s.Length >= start.Length + end.Length)
        {
            return s[start.Length..(s.Length - end.Length)];
        }

        return null;
    }

    /// <summary>Go: <c>func removeLeadingDirectorySeparator(path string) string</c>（string_completions.go:1834-1836）。</summary>
    internal static string RemoveLeadingDirectorySeparator(string path)
    {
        return path.StartsWith(DirectorySeparator) ? path[1..] : path; // Go: strings.TrimPrefix(path, string(tspath.DirectorySeparator))
    }

    /// <summary>Go: <c>func getPossibleOriginalInputPathWithoutChangingExt(filePath string, ignoreCase bool, outputDir string, getCommonSourceDirectory func() string) string</c>（string_completions.go:1838-1853）。</summary>
    internal static string GetPossibleOriginalInputPathWithoutChangingExt(
        string filePath,
        bool ignoreCase,
        string outputDir,
        Func<string> getCommonSourceDirectory)
    {
        if (outputDir != "")
        {
            return ResolvePath(
                getCommonSourceDirectory(),
                GetRelativePathFromDirectory(outputDir, filePath, new ComparePathsOptions(!ignoreCase)));
        }

        return filePath;
    }

    /// <summary>Go: <c>func getFilenameWithExtensionOption(name string, program *compiler.Program, extensionOptions *extensionOptions, isExportsOrImportsWildcard bool) (string, string)</c>（string_completions.go:1855-1909）。</summary>
    internal static (string Name, string Extension) GetFilenameWithExtensionOption(
        string name,
        Program program,
        ExtensionOptions extensionOptions,
        bool isExportsOrImportsWildcard)
    {
        var nonJSResult = TryGetRealFileNameForNonJSDeclarationFileName(name);
        if (nonJSResult != "")
        {
            return (nonJSResult, TryGetExtensionFromPath(nonJSResult));
        }

        if (extensionOptions._referenceKind == ReferenceKind.FileName)
        {
            return (name, TryGetExtensionFromPath(name));
        }

        var allowedEndings = GetAllowedEndingsInPreferredOrder(
            new ModuleSpecifiersUserPreferences { ImportModuleSpecifierEnding = extensionOptions._endingPreference },
            program,
            program.Options(),
            extensionOptions._importingSourceFile,
            "", /*oldImportSpecifier*/
            extensionOptions._resolutionMode);

        if (isExportsOrImportsWildcard)
        {
            // If we're completing `import {} from "foo/|"` and subpaths are available via `"exports": { "./*": "./src/*" }`,
            // the completion must be a (potentially extension-swapped) file name. Dropping extensions and index files is not allowed.
            allowedEndings = Filter(allowedEndings, e => e != ModuleSpecifierEnding.Minimal && e != ModuleSpecifierEnding.Index);
        }

        if (allowedEndings.Length > 0 && allowedEndings[0] == ModuleSpecifierEnding.TsExtension)
        {
            if (FileExtensionIsOneOf(name, SupportedTSImplementationExtensions))
            {
                return (name, TryGetExtensionFromPath(name));
            }

            var outputExtension = TryGetJSExtensionForFile(name, program.Options());
            if (outputExtension != "")
            {
                return (ChangeExtension(name, outputExtension), outputExtension);
            }

            return (name, TryGetExtensionFromPath(name));
        }

        if (!isExportsOrImportsWildcard &&
            allowedEndings.Length > 0 &&
            (allowedEndings[0] == ModuleSpecifierEnding.Minimal || allowedEndings[0] == ModuleSpecifierEnding.Index) &&
            FileExtensionIsOneOf(name, [ExtensionJs, ExtensionJsx, ExtensionTs, ExtensionTsx, ExtensionDts]))
        {
            return (RemoveFileExtension(name), TryGetExtensionFromPath(name));
        }

        var outputExt = TryGetJSExtensionForFile(name, program.Options());
        if (outputExt != "")
        {
            return (ChangeExtension(name, outputExt), outputExt);
        }

        return (name, TryGetExtensionFromPath(name));
    }

    /// <summary>Go: <c>func walkUpParentheses(node *ast.Node) *ast.Node</c>（string_completions.go:1911-1920）。</summary>
    internal static Node WalkUpParentheses(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ParenthesizedType:
                return WalkUpParenthesizedTypes(node)!;
            case Kind.ParenthesizedExpression:
                return WalkUpParenthesizedExpressions(node)!;
            default:
                return node;
        }
    }

    /// <summary>Go: <c>func getStringLiteralTypes(t *checker.Type, uniques *collections.Set[string], typeChecker *checker.Checker) []*checker.StringLiteralType</c>（string_completions.go:1922-1941）。</summary>
    internal static Type[] GetStringLiteralTypes(Type? t, Set<string>? uniques, CheckerType typeChecker)
    {
        if (t is null)
        {
            return [];
        }

        uniques ??= new Set<string>();
        t = SkipConstraint(t, typeChecker);
        if (t.IsUnion())
        {
            var types = new List<Type>();
            foreach (var elementType in t.Types())
            {
                types.AddRange(GetStringLiteralTypes(elementType, uniques, typeChecker));
            }

            return types.ToArray();
        }

        if (t.IsStringLiteral() && !t.IsEnumLiteral() && uniques.AddIfAbsent((string)t.AsLiteralType()!.Value()!))
        {
            return [t];
        }

        return [];
    }

    /// <summary>Go: <c>func getAlreadyUsedTypesInStringLiteralUnion(union *ast.UnionTypeNodeNode, current *ast.LiteralTypeNodeNode) []string</c>（string_completions.go:1943-1956）。</summary>
    internal static string[] GetAlreadyUsedTypesInStringLiteralUnion(UnionTypeNode union, LiteralTypeNode current)
    {
        var typesList = union.Types;
        if (typesList is null)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var typeNode in typesList.Nodes ?? [])
        {
            if (!ReferenceEquals(typeNode, current) && IsLiteralTypeNode(typeNode) &&
                IsStringLiteral(typeNode.AsLiteralTypeNode()!.Literal!))
            {
                values.Add(typeNode.AsLiteralTypeNode()!.Literal!.Text());
            }
        }

        return values.ToArray();
    }

    /// <summary>Go: <c>func hasIndexSignature(t *checker.Type, typeChecker *checker.Checker) bool</c>（string_completions.go:1958-1960）。</summary>
    internal static bool HasIndexSignature(Type t, CheckerType typeChecker)
    {
        return typeChecker.GetStringIndexType(t) is not null || typeChecker.GetNumberIndexType(t) is not null;
    }

    /// <summary>Matches
    /// <code>
    /// require(""
    /// require("")
    /// </code>
    /// （string_completions.go:1962-1969）。</summary>
    internal static bool IsRequireCallArgument(Node node)
    {
        return IsCallExpression(node.Parent!) && (node.Parent!.Arguments()?.Length ?? 0) > 0 && ReferenceEquals(node.Parent.Arguments()![0], node) &&
            IsIdentifier(node.Parent.Expression()!) && node.Parent.Expression()!.Text() == "require";
    }

    /// <summary>Go: <c>func kindModifiersFromExtension(extension string) lsutil.ScriptElementKindModifier</c>（string_completions.go:1971-2002）。</summary>
    internal static ScriptElementKindModifier KindModifiersFromExtension(string extension)
    {
        switch (extension)
        {
            case ExtensionDts:
                return ScriptElementKindModifier.Dts;
            case ExtensionJs:
                return ScriptElementKindModifier.Js;
            case ExtensionJson:
                return ScriptElementKindModifier.Json;
            case ExtensionJsx:
                return ScriptElementKindModifier.Jsx;
            case ExtensionTs:
                return ScriptElementKindModifier.Ts;
            case ExtensionTsx:
                return ScriptElementKindModifier.Tsx;
            case ExtensionDmts:
                return ScriptElementKindModifier.Dmts;
            case ExtensionMjs:
                return ScriptElementKindModifier.Mjs;
            case ExtensionMts:
                return ScriptElementKindModifier.Mts;
            case ExtensionDcts:
                return ScriptElementKindModifier.Dcts;
            case ExtensionCjs:
                return ScriptElementKindModifier.Cjs;
            case ExtensionCts:
                return ScriptElementKindModifier.Cts;
            case ExtensionTsBuildInfo:
                throw new Exception($"Extension {ExtensionTsBuildInfo} is unsupported.");
            default:
                return ScriptElementKindModifier.None;
        }
    }

    /// <summary>Go: <c>func getStringLiteralCompletionsFromSignature(call *ast.CallLikeExpression, arg *ast.StringLiteralLike, argumentInfo *argumentInfoForCompletions, typeChecker *checker.Checker) *completionsFromTypes</c>（string_completions.go:2004-2044）。</summary>
    internal static CompletionsFromTypes? GetStringLiteralCompletionsFromSignature(
        Node call,
        Node arg,
        ArgumentInfoForCompletions argumentInfo,
        CheckerType typeChecker)
    {
        var isNewIdentifier = false;
        var uniques = new Set<string>();
        Node editingArgument;
        if (IsJsxOpeningLikeElement(call))
        {
            editingArgument = FindAncestor(arg.Parent, IsJsxAttribute)
                ?? throw new Exception("Expected jsx opening-like element to have a jsx attribute as ancestor."); // Go: panic(...)
        }
        else
        {
            editingArgument = arg;
        }

        var candidates = typeChecker.GetCandidateSignaturesForStringLiteralCompletions(call, editingArgument);
        var types = new List<Type>();
        foreach (var candidate in candidates)
        {
            if (!candidate.HasRestParameter() && argumentInfo._argumentCount > candidate.Parameters().Length)
            {
                continue;
            }

            var t = typeChecker.GetTypeParameterAtPosition(candidate, argumentInfo._argumentIndex);
            if (IsJsxOpeningLikeElement(call))
            {
                var propType = typeChecker.GetTypeOfPropertyOfType(t, editingArgument.AsJsxAttribute()!.Name()!.Text());
                if (propType is not null)
                {
                    t = propType;
                }
            }

            isNewIdentifier = isNewIdentifier || t.IsString();
            types.AddRange(GetStringLiteralTypes(t, uniques, typeChecker));
        }

        if (types.Count > 0)
        {
            return new CompletionsFromTypes
            {
                _types = types.ToArray(),
                _isNewIdentifier = isNewIdentifier,
            };
        }

        return null;
    }

    /// <summary>Go: <c>func isInReferenceComment(file *ast.SourceFile, position int) bool</c>（string_completions.go:2105-2112）。</summary>
    internal static bool IsInReferenceComment(SourceFile file, int position)
    {
        var commentRange = IsInComment(file, position, GetTokenAtPosition(file, position));
        if (commentRange is null)
        {
            return false;
        }

        var commentText = file.Text()[commentRange.Range.Pos()..commentRange.Range.End()];
        return HasTripleSlashPrefix(commentText);
    }

    /// <summary>Go: <c>func hasTripleSlashPrefix(commentText string) bool</c>（string_completions.go:2114-2116）。</summary>
    internal static bool HasTripleSlashPrefix(string commentText)
    {
        return commentText.StartsWith("///", StringComparison.Ordinal) &&
            commentText[3..].Trim().StartsWith("<", StringComparison.Ordinal); // Go: strings.TrimSpace(commentText[3:])
    }

    /// <summary>Matches a triple slash reference directive with an incomplete string literal for its path.
    /// Used to determine if the caret is currently within the string literal and capture the literal
    /// fragment for completions.
    /// For example, this matches
    /// <code>
    /// /// &lt;reference path="fragment
    /// </code>
    /// but not
    /// <code>
    /// /// &lt;reference path="fragment"
    /// </code>
    /// Returns (prefix, kind, toComplete, ok) where:
    /// <list type="bullet">
    /// <item>prefix is everything up to and including the opening quote</item>
    /// <item>kind is either "path" or "types"</item>
    /// <item>toComplete is the fragment after the opening quote</item>
    /// <item>ok indicates whether the match was successful</item>
    /// </list>
    /// （string_completions.go:2118-2186）。</summary>
    internal static (string Prefix, string Kind, string ToComplete, bool Ok) ParseTripleSlashDirectiveFragment(string text)
    {
        string rest = text;
        if (!rest.StartsWith("///", StringComparison.Ordinal))
        {
            return ("", "", "", false);
        }

        rest = rest[3..];

        // 局部函数：Go 的 strings.TrimLeftFunc(rest, stringutil.IsWhiteSpaceLike)
        static string TrimLeftWhiteSpaceLike(string s)
        {
            var start = 0;
            while (start < s.Length && IsWhiteSpaceLike(s[start]))
            {
                start++;
            }

            return start == 0 ? s : s[start..];
        }

        rest = TrimLeftWhiteSpaceLike(rest);

        // <reference
        if (!rest.StartsWith("<reference", StringComparison.Ordinal))
        {
            return ("", "", "", false);
        }

        rest = rest["<reference".Length..];

        if (rest.Length == 0 || !IsWhiteSpaceLike(rest[0]))
        {
            return ("", "", "", false);
        }

        rest = TrimLeftWhiteSpaceLike(rest);

        // path or types
        string kind;
        if (rest.StartsWith("path", StringComparison.Ordinal))
        {
            kind = "path";
            rest = rest["path".Length..];
        }
        else if (rest.StartsWith("types", StringComparison.Ordinal))
        {
            kind = "types";
            rest = rest["types".Length..];
        }
        else
        {
            return ("", "", "", false);
        }

        // Skip optional whitespace, then must have "="
        rest = TrimLeftWhiteSpaceLike(rest);
        if (!rest.StartsWith('='))
        {
            return ("", "", "", false);
        }

        rest = rest[1..];

        // Skip optional whitespace, then must have opening quote (' or ")
        rest = TrimLeftWhiteSpaceLike(rest);
        if (rest.Length == 0 || (rest[0] != '\'' && rest[0] != '"'))
        {
            return ("", "", "", false);
        }

        rest = rest[1..];

        // The toComplete part is everything after the opening quote
        if (rest.IndexOf('\'') >= 0 || rest.IndexOf('"') >= 0) // Go: strings.ContainsAny(rest, `'"`)
        {
            return ("", "", "", false);
        }

        var toComplete = rest;
        var prefix = text[..(text.Length - toComplete.Length)];
        return (prefix, kind, toComplete, true);
    }

    /// <summary>Go: <c>func isAnyDirectorySeparator(r rune) bool</c>（string_completions.go:645-647）。</summary>
    internal static bool IsAnyDirectorySeparator(char r)
    {
        return r == '/' || r == '\\';
    }

    /// <summary>Replace everything after the last directory separator that appears
    /// （Go: <c>func getDirectoryFragmentRange(text string, textStart int) *core.TextRange</c>，string_completions.go:650-661）。</summary>
    internal static TextRange? GetDirectoryFragmentRange(string text, int textStart)
    {
        // Go: strings.LastIndexFunc(text, isAnyDirectorySeparator)
        var index = -1;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (IsAnyDirectorySeparator(text[i]))
            {
                index = i;
                break;
            }
        }

        var offset = 0;
        if (index != -1)
        {
            offset = index + 1;
        }

        var length = text.Length - offset;
        if (length == 0)
        {
            return null;
        }

        return new TextRange(textStart + offset, textStart + offset + length);
    }

    /// <summary>Go: <c>func toPathCompletions(names []moduleCompletionNameAndKind) []*pathCompletion</c>（string_completions.go:612-620）。</summary>
    internal static PathCompletion[] ToPathCompletions(ModuleCompletionNameAndKind[] names)
    {
        return Map(names, nameAndKind => new PathCompletion
        {
            _name = nameAndKind._name,
            _kind = ModuletToScriptElementKind(nameAndKind._kind),
            _extension = nameAndKind._extension,
        });
    }

    /// <summary>Go: <c>func moduletToScriptElementKind(kind moduleCompletionKind) lsutil.ScriptElementKind</c>（string_completions.go:633-643）。
    /// ⚠ Go 的函数名里 "modulet" 是上游拼写（`module` + `t`，来自 `kind` 的后缀误抄），此处 1:1 保留。</summary>
    internal static ScriptElementKind ModuletToScriptElementKind(ModuleCompletionKind kind)
    {
        switch (kind)
        {
            case ModuleCompletionKind.Directory:
                return ScriptElementKind.Directory;
            case ModuleCompletionKind.File:
                return ScriptElementKind.ScriptElement;
            case ModuleCompletionKind.ExternalModuleName:
                return ScriptElementKind.ExternalModuleName;
        }

        throw new Exception($"Unknown moduleCompletionKind: {(int)kind}");
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) getStringLiteralCompletionsFromModuleNames(file, node, program, checker) *stringLiteralCompletions</c>（string_completions.go:587-610）。</summary>
    internal StringLiteralCompletions? GetStringLiteralCompletionsFromModuleNames(
        SourceFile file,
        Node node,
        Program program,
        CheckerType checker)
    {
        var textStart = GetStartOfNode(node, file, false /*includeJSDoc*/) + 1;
        var (replacementSpan, ok) = PathCompletionReplacementSpan(file, GetDirectoryFragmentRange(node.Text(), textStart));
        if (!ok)
        {
            return null;
        }

        var nameAndKinds = GetStringLiteralCompletionsFromModuleNamesWorker(
            file,
            node,
            program,
            checker);
        return new StringLiteralCompletions
        {
            _fromPaths = new PathCompletions
            {
                _entries = ToPathCompletions(nameAndKinds),
                _replacementSpan = replacementSpan,
            },
        };
    }

    /// <summary>Go: <c>func (l *LanguageService) pathCompletionReplacementSpan(file *ast.SourceFile, textRange *core.TextRange) (*lsproto.Range, bool)</c>（string_completions.go:622-631）。</summary>
    internal (Range? Range, bool Ok) PathCompletionReplacementSpan(SourceFile file, TextRange? textRange)
    {
        if (textRange is null)
        {
            return (null, true);
        }

        var (lspRange, fidelity) = CreateLspRangeFromBounds(textRange.Value.Pos(), textRange.Value.End(), file);
        if (!fidelity.IsExact())
        {
            return (null, false);
        }

        return (lspRange, true);
    }

    /// <summary>Go: <c>func (l *LanguageService) getStringLiteralCompletionsFromModuleNamesWorker(file, node, program, checker) []moduleCompletionNameAndKind</c>（string_completions.go:663-705）。</summary>
    internal ModuleCompletionNameAndKind[] GetStringLiteralCompletionsFromModuleNamesWorker(
        SourceFile file,
        Node node,
        Program program,
        CheckerType checker)
    {
        var literalValue = NormalizeSlashes(node.Text());
        var mode = ModuleKind.None;
        if (IsStringLiteralLike(node))
        {
            mode = program.GetModeForUsageLocation(file, node);
        }

        var scriptPath = file.Path();
        var scriptDirectory = scriptPath.GetDirectoryPath();
        var options = program.Options();
        var extensionOptions = GetExtensionOptions(options, ReferenceKind.ModuleSpecifier, file, mode, checker);

        if (IsPathRelativeToScript(literalValue) ||
            ((options.Paths?.Size() ?? 0) == 0 && (IsRootedDiskPath(literalValue) || IsUrl(literalValue))))
        {
            return GetCompletionEntriesForRelativeModules(
                literalValue,
                scriptDirectory.Value,
                program,
                scriptPath,
                extensionOptions);
        }

        return GetCompletionEntriesForNonRelativeModules(
            literalValue,
            scriptDirectory.Value,
            mode,
            program,
            checker,
            extensionOptions);
    }

    /// <summary>Check all of the declared modules and those in node modules. Possible sources of modules:
    /// <list type="bullet">
    /// <item>Modules that are found by the type checker</item>
    /// <item>Modules found via patterns from "paths" compiler option</item>
    /// <item>Modules from node_modules (i.e. those listed in package.json)
    ///   This includes all files that are found in node_modules/moduleName/ with acceptable file extensions</item>
    /// </list>
    /// （Go: <c>func (l *LanguageService) getCompletionEntriesForNonRelativeModules(fragment, scriptPath string, mode core.ResolutionMode, program *compiler.Program, typeChecker *checker.Checker, extensionOptions *extensionOptions) []moduleCompletionNameAndKind</c>，string_completions.go:707-873）。</summary>
    internal ModuleCompletionNameAndKind[] GetCompletionEntriesForNonRelativeModules(
        string fragment,
        string scriptPath,
        ModuleKind mode,
        Program program,
        CheckerType typeChecker,
        ExtensionOptions extensionOptions)
    {
        var compilerOptions = program.Options();
        var paths = compilerOptions.Paths;

        var result = new ModuleCompletionNameAndKindSet { _names = [] };
        var moduleResolution = compilerOptions.GetModuleResolutionKind();

        if (paths is not null && paths.Size() > 0)
        {
            var absolute = compilerOptions.GetPathsBasePath(program.GetCurrentDirectory());
            AddCompletionEntriesFromPaths(result, program, fragment, absolute, extensionOptions, paths);
        }

        var fragmentDirectory = GetFragmentDirectory(fragment);
        foreach (var ambientName in GetAmbientModuleCompletions(fragment, fragmentDirectory, typeChecker))
        {
            result.Add(new ModuleCompletionNameAndKind
            {
                _name = ambientName,
                _kind = ModuleCompletionKind.ExternalModuleName,
            });
        }

        GetCompletionEntriesFromTypings(program, scriptPath, fragmentDirectory, extensionOptions, result);

        if (ModuleResolutionUsesNodeModules(moduleResolution))
        {
            // If looking for a global package name, don't just include everything in `node_modules` because that includes dependencies' own dependencies.
            // (But do if we didn't find anything, e.g. 'package.json' missing.)
            var foundGlobal = false;
            if (fragmentDirectory == "")
            {
                foreach (var moduleName in EnumerateNodeModulesVisibleToScript(scriptPath))
                {
                    var moduleResult = new ModuleCompletionNameAndKind
                    {
                        _name = moduleName,
                        _kind = ModuleCompletionKind.ExternalModuleName,
                    };
                    if (!result._names.ContainsKey(moduleResult._name))
                    {
                        foundGlobal = true;
                        result.Add(moduleResult);
                    }
                }
            }

            if (!foundGlobal)
            {
                var resolvePackageJsonExports = compilerOptions.GetResolvePackageJsonExports();
                var resolvePackageJsonImports = compilerOptions.GetResolvePackageJsonImports();
                var seenPackageScope = false;
                var conditions = GetConditions(compilerOptions, mode);

                // Returns true if the search should stop.
                Func<ExportsOrImports?, string, string, bool, bool, bool> exportsOrImportsLookup = (lookupTable, lookupFragment, baseDirectory, isExports, isImports) =>
                {
                    if (lookupTable is null || lookupTable.Type != JSONValueType.Object)
                    {
                        return lookupTable is not null && lookupTable.Type != JSONValueType.NotPresent;
                    }

                    var keys = lookupTable.AsObject()!.Keys();
                    AddCompletionEntriesFromPathsOrExportsOrImports(
                        result,
                        program,
                        isExports,
                        isImports,
                        lookupFragment,
                        baseDirectory,
                        extensionOptions,
                        keys,
                        key =>
                        {
                            var (keyValue, ok) = lookupTable.AsObject()!.Get(key);
                            if (!ok)
                            {
                                return [];
                            }

                            var pattern = GetPatternFromFirstMatchingCondition(keyValue!, conditions);
                            if (pattern == "")
                            {
                                return [];
                            }

                            if (key.EndsWith("/", StringComparison.Ordinal) && pattern.EndsWith("/", StringComparison.Ordinal))
                            {
                                return [pattern + "*"];
                            }

                            return [pattern];
                        },
                        ComparePatternKeys);
                    return true;
                };

                void importsLookup(string directory)
                {
                    if (resolvePackageJsonImports && !seenPackageScope)
                    {
                        var packageFile = CombinePaths(directory, "package.json");
                        var packageJsonInfo = program.GetPackageJsonInfo(packageFile);
                        if (packageJsonInfo is not null && packageJsonInfo.Exists())
                        {
                            seenPackageScope = true;
                            exportsOrImportsLookup(packageJsonInfo.Contents!.PathFields.Imports, fragment, directory, false /*isExports*/, true /*isImports*/);
                        }
                    }
                }

                Func<string, (object? Result, bool Stop)> ancestorLookup = ancestor =>
                {
                    var nodeModules = CombinePaths(ancestor, "node_modules");
                    if (_host.DirectoryExists(nodeModules))
                    {
                        GetCompletionEntriesForDirectoryFragment(
                            fragment,
                            nodeModules,
                            extensionOptions,
                            program,
                            false, /* moduleSpecifierIsRelative */
                            "",
                            result);
                    }

                    importsLookup(ancestor);
                    return (null, false);
                };

                if (fragmentDirectory != "" && resolvePackageJsonExports)
                {
                    var nodeModulesDirectoryOrImportsLookup = ancestorLookup;
                    ancestorLookup = ancestor =>
                    {
                        var components = GetPathComponents(fragment, "");
                        components = components[1..]; // shift off empty root
                        if (components.Length == 0)
                        {
                            nodeModulesDirectoryOrImportsLookup(ancestor);
                            return (null, false);
                        }

                        var packagePath = components[0];
                        components = components[1..];
                        if (packagePath.StartsWith("@", StringComparison.Ordinal))
                        {
                            if (components.Length == 0)
                            {
                                nodeModulesDirectoryOrImportsLookup(ancestor);
                                return (null, false);
                            }

                            var subName = components[0];
                            components = components[1..];
                            packagePath = CombinePaths(packagePath, subName);
                        }

                        if (resolvePackageJsonImports && packagePath.StartsWith("#", StringComparison.Ordinal))
                        {
                            importsLookup(ancestor);
                            return (null, false);
                        }

                        var packageDirectory = CombinePaths(ancestor, "node_modules", packagePath);
                        var packageFile = CombinePaths(packageDirectory, "package.json");
                        var packageJsonInfo = program.GetPackageJsonInfo(packageFile);
                        if (packageJsonInfo is not null && packageJsonInfo.Exists())
                        {
                            var fragmentSubpath = string.Join("/", components);
                            if (components.Length > 0 && HasTrailingDirectorySeparator(fragment))
                            {
                                fragmentSubpath += "/";
                            }

                            if (exportsOrImportsLookup(
                                packageJsonInfo.Contents!.PathFields.Exports,
                                fragmentSubpath,
                                packageDirectory,
                                true, /*isExports*/
                                false /*isImports*/))
                            {
                                return (null, false);
                            }
                        }

                        nodeModulesDirectoryOrImportsLookup(ancestor);
                        return (null, false);
                    };
                }

                var globalCacheLocation = program.GetGlobalTypingsCacheLocation();
                ForEachAncestorDirectoryStoppingAtGlobalCache<object?>(globalCacheLocation, scriptPath, ancestorLookup);
            }
        }

        return [.. result._names.Values];
    }

    /// <summary>Go: <c>func (l *LanguageService) getCompletionEntriesFromTypings(program *compiler.Program, scriptPath string, fragmentDirectory string, extensionOptions *extensionOptions, result *moduleCompletionNameAndKindSet)</c>（string_completions.go:931-953）。</summary>
    internal void GetCompletionEntriesFromTypings(
        Program program,
        string scriptPath,
        string fragmentDirectory,
        ExtensionOptions extensionOptions,
        ModuleCompletionNameAndKindSet result)
    {
        var options = program.Options();
        var seen = new Dictionary<string, bool>();

        var (typeRoots, _) = options.GetEffectiveTypeRoots(program.GetCurrentDirectory());

        foreach (var root in typeRoots)
        {
            GetCompletionEntriesFromTypingsDirectories(root, options, fragmentDirectory, extensionOptions, program, seen, result);
        }

        var globalCacheLocation = program.GetGlobalTypingsCacheLocation();
        ForEachAncestorDirectoryStoppingAtGlobalCache<object?>(globalCacheLocation, scriptPath, directory =>
        {
            var typesDir = CombinePaths(directory, "node_modules/@types");
            GetCompletionEntriesFromTypingsDirectories(typesDir, options, fragmentDirectory, extensionOptions, program, seen, result);
            return (null, false);
        });
    }

    /// <summary>Go: <c>func (l *LanguageService) getCompletionEntriesFromTypingsDirectories(directory string, options *core.CompilerOptions, fragmentDirectory string, extensionOptions *extensionOptions, program *compiler.Program, seen map[string]bool, result *moduleCompletionNameAndKindSet)</c>（string_completions.go:955-998）。</summary>
    internal void GetCompletionEntriesFromTypingsDirectories(
        string directory,
        CompilerOptions options,
        string fragmentDirectory,
        ExtensionOptions extensionOptions,
        Program program,
        Dictionary<string, bool> seen,
        ModuleCompletionNameAndKindSet result)
    {
        if (!_host.DirectoryExists(directory))
        {
            return;
        }

        foreach (var typeDirectoryName in GetDirectories(directory))
        {
            var packageName = UnmangleScopedPackageName(typeDirectoryName);
            if (options.Types is { Length: > 0 } types && Array.IndexOf(types, packageName) < 0)
            {
                continue;
            }

            if (fragmentDirectory == "")
            {
                if (!seen.ContainsKey(packageName))
                {
                    result.Add(new ModuleCompletionNameAndKind
                    {
                        _name = packageName,
                        _kind = ModuleCompletionKind.ExternalModuleName,
                    });
                    seen[packageName] = true;
                }
            }
            else
            {
                var baseDirectory = CombinePaths(directory, typeDirectoryName);
                var remainingFragment = TryRemoveDirectoryPrefix(fragmentDirectory, packageName, program.UseCaseSensitiveFileNames());
                if (remainingFragment is not null)
                {
                    GetCompletionEntriesForDirectoryFragment(
                        remainingFragment,
                        baseDirectory,
                        extensionOptions,
                        program,
                        false,
                        "",
                        result);
                }
            }
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) enumerateNodeModulesVisibleToScript(scriptPath string) []string</c>（string_completions.go:1011-1030）。</summary>
    internal string[] EnumerateNodeModulesVisibleToScript(string scriptPath)
    {
        var result = new List<string>();
        var globalCacheLocation = _program!.GetGlobalTypingsCacheLocation();

        ForEachAncestorDirectoryStoppingAtGlobalCache<object?>(globalCacheLocation, scriptPath, directory =>
        {
            var packageJsonPath = CombinePaths(directory, "package.json");
            var packageJsonInfo = _program!.GetPackageJsonInfo(packageJsonPath);
            if (packageJsonInfo is not null && packageJsonInfo.Exists() && packageJsonInfo.Contents is not null)
            {
                packageJsonInfo.Contents.DependencyFields.RangeDependencies((name, version, dependencyField) =>
                {
                    if (!name.StartsWith("@types/", StringComparison.Ordinal))
                    {
                        result.Add(name);
                    }

                    return true;
                });
            }

            return (null, false);
        });

        return result.ToArray();
    }

    /// <summary>Go: <c>func (l *LanguageService) getExtensionOptions(options *core.CompilerOptions, referenceKind referenceKind, file *ast.SourceFile, mode core.ResolutionMode, checker *checker.Checker) *extensionOptions</c>（string_completions.go:1032-1048）。</summary>
    internal ExtensionOptions GetExtensionOptions(
        CompilerOptions options,
        ReferenceKind referenceKind,
        SourceFile file,
        ModuleKind mode,
        CheckerType? checker)
    {
        var extensionsToSearch = GetSupportedExtensionsForModuleResolution(options, GetProgram()!.CommandLine().ContentMapperExtensions(), checker);

        return new ExtensionOptions
        {
            _extensionsToSearch = extensionsToSearch,
            _referenceKind = referenceKind,
            _importingSourceFile = file,
            _endingPreference = UserPreferences().ImportModuleSpecifierEnding,
            _resolutionMode = mode,
        };
    }

    /// <summary>Go: <c>func (l *LanguageService) getCompletionEntriesForRelativeModules(literalValue string, scriptDirectory string, program *compiler.Program, scriptPath tspath.Path, extensionOptions *extensionOptions) []moduleCompletionNameAndKind</c>（string_completions.go:1084-1113）。</summary>
    internal ModuleCompletionNameAndKind[] GetCompletionEntriesForRelativeModules(
        string literalValue,
        string scriptDirectory,
        Program program,
        TSPath.Path scriptPath,
        ExtensionOptions extensionOptions)
    {
        var options = program.Options();
        if ((options.RootDirs?.Length ?? 0) > 0)
        {
            return GetCompletionEntriesForDirectoryFragmentWithRootDirs(
                options.RootDirs!,
                literalValue,
                scriptDirectory,
                program,
                scriptPath.Value,
                extensionOptions);
        }

        var result = GetCompletionEntriesForDirectoryFragment(
            literalValue,
            scriptDirectory,
            extensionOptions,
            program,
            true, /*moduleSpecifierIsRelative*/
            scriptPath.Value,
            new ModuleCompletionNameAndKindSet { _names = [] });
        return [.. result._names.Values];
    }

    /// <summary>Go: <c>func (l *LanguageService) getCompletionEntriesForDirectoryFragmentWithRootDirs(rootDirs []string, fragment string, scriptDirectory string, program *compiler.Program, exclude string, extensionOptions *extensionOptions) []moduleCompletionNameAndKind</c>（string_completions.go:1115-1153）。</summary>
    internal ModuleCompletionNameAndKind[] GetCompletionEntriesForDirectoryFragmentWithRootDirs(
        string[] rootDirs,
        string fragment,
        string scriptDirectory,
        Program program,
        string exclude,
        ExtensionOptions extensionOptions)
    {
        var options = program.Options();
        var basePath = options.Project != "" ? options.Project : program.GetCurrentDirectory();
        var ignoreCase = !program.UseCaseSensitiveFileNames();
        var baseDirectories = GetBaseDirectoriesFromRootDirs(rootDirs, basePath, scriptDirectory, ignoreCase);

        var allCompletions = new List<ModuleCompletionNameAndKind>();
        foreach (var baseDirectory in baseDirectories)
        {
            var result = GetCompletionEntriesForDirectoryFragment(
                fragment,
                baseDirectory,
                extensionOptions,
                program,
                true, /*moduleSpecifierIsRelative*/
                exclude,
                new ModuleCompletionNameAndKindSet { _names = [] });
            allCompletions.AddRange(result._names.Values);
        }

        // Deduplicate based on name, kind, and extension
        return DeduplicateModuleCompletions(allCompletions.ToArray());
    }

    /// <summary>Go: <c>func (l *LanguageService) getCompletionEntriesForDirectoryFragment(fragment string, scriptDirectory string, extensionOptions *extensionOptions, program *compiler.Program, moduleSpecifierIsRelative bool, exclude string, result *moduleCompletionNameAndKindSet) *moduleCompletionNameAndKindSet</c>（string_completions.go:1272-1371）。</summary>
    internal ModuleCompletionNameAndKindSet GetCompletionEntriesForDirectoryFragment(
        string fragment,
        string scriptDirectory,
        ExtensionOptions extensionOptions,
        Program program,
        bool moduleSpecifierIsRelative,
        string exclude,
        ModuleCompletionNameAndKindSet result)
    {
        fragment = NormalizeSlashes(fragment);

        // Remove the basename from the path.
        // We don't use the basename to filter completions: the client is responsible for that filtering.
        if (!HasTrailingDirectorySeparator(fragment))
        {
            fragment = GetDirectoryPath(fragment);
        }

        if (fragment == "")
        {
            fragment = ".";
        }

        fragment = EnsureTrailingDirectorySeparator(fragment);

        var baseDirectory = ResolvePath(scriptDirectory, fragment);
        if (!moduleSpecifierIsRelative)
        {
            // Check for a version redirect.
            var packageJsonDirectory = program.GetNearestAncestorDirectoryWithPackageJson(baseDirectory);
            if (packageJsonDirectory != "")
            {
                var packageJsonPath = CombinePaths(packageJsonDirectory, "package.json");
                var packageJsonInfo = program.GetPackageJsonInfo(packageJsonPath);
                if (packageJsonInfo is not null && packageJsonInfo.Contents is not null
                    && packageJsonInfo.Contents.PathFields.TypesVersions.Type == JSONValueType.Object)
                {
                    var versionPaths = packageJsonInfo.Contents.GetVersionPaths(null);
                    var paths = versionPaths.GetPaths();
                    if ((paths?.Size() ?? 0) > 0)
                    {
                        var pathInPackage = baseDirectory[EnsureTrailingDirectorySeparator(packageJsonDirectory).Length..];
                        if (AddCompletionEntriesFromPaths(result, program, pathInPackage, packageJsonDirectory, extensionOptions, paths!))
                        {
                            // One of the `versionPaths` was matched, which will block relative resolution
                            // to files and folders from here.
                            // All reachable paths given the pattern match are already added.
                            return result;
                        }
                    }
                }
            }
        }

        if (!_host.DirectoryExists(baseDirectory))
        {
            return result;
        }

        // Enumerate all available files.
        var files = ReadDirectory(
            baseDirectory,
            extensionOptions._extensionsToSearch,
            ["./*"]); /*include*/

        foreach (var filePath in files)
        {
            if (ComparePaths(exclude, filePath, new ComparePathsOptions(program.UseCaseSensitiveFileNames(), program.GetCurrentDirectory())) == 0)
            {
                continue; // Avoid self-imports
            }

            var (name, extension) = GetFilenameWithExtensionOption(
                GetBaseFileName(filePath),
                program,
                extensionOptions,
                false /*isExportsOrImportsWildcard*/);
            result.Add(
                new ModuleCompletionNameAndKind
                {
                    _name = name,
                    _kind = ModuleCompletionKind.File,
                    _extension = extension,
                });
        }

        // Get folder completion as well.
        var directories = GetDirectories(baseDirectory);

        foreach (var directory in directories)
        {
            var directoryName = GetBaseFileName(directory);
            if (directoryName != "@types")
            {
                result.Add(
                    new ModuleCompletionNameAndKind
                    {
                        _name = directoryName,
                        _kind = ModuleCompletionKind.Directory,
                    });
            }
        }

        return result;
    }

    /// <summary>Go: <c>func (l *LanguageService) addCompletionEntriesFromPaths(result *moduleCompletionNameAndKindSet, program *compiler.Program, fragment string, baseDirectory string, extensionOptions *extensionOptions, paths *collections.OrderedMap[string, []string]) bool</c>（string_completions.go:1373-1411）。
    /// Returns true if `fragment` was a match for any `paths`（which should indicate whether any other path completions should be offered）。</summary>
    internal bool AddCompletionEntriesFromPaths(
        ModuleCompletionNameAndKindSet result,
        Program program,
        string fragment,
        string baseDirectory,
        ExtensionOptions extensionOptions,
        OrderedMap<string, string[]> paths)
    {
        Func<string, string[]> getPatternsForKeys = key => paths.GetOrZero(key) ?? [];
        Comparison<string> comparePaths = (a, b) =>
        {
            var patternA = TryParsePattern(a);
            var patternB = TryParsePattern(b);
            var lengthA = a.Length;
            if (patternA.StarIndex != -1)
            {
                lengthA = patternA.StarIndex;
            }

            var lengthB = b.Length;
            if (patternB.StarIndex != -1)
            {
                lengthB = patternB.StarIndex;
            }

            // Go: cmp.Compare(lengthB, lengthA)
            return lengthB < lengthA ? -1 : lengthB > lengthA ? 1 : 0;
        };
        return AddCompletionEntriesFromPathsOrExportsOrImports(
            result,
            program,
            false, /*isExports*/
            false, /*isImports*/
            fragment,
            baseDirectory,
            extensionOptions,
            paths.Keys(),
            getPatternsForKeys,
            comparePaths);
    }

    /// <summary>Go: <c>func (l *LanguageService) addCompletionEntriesFromPathsOrExportsOrImports(result *moduleCompletionNameAndKindSet, program *compiler.Program, isExports bool, isImports bool, fragment string, baseDirectory string, extensionOptions *extensionOptions, keys iter.Seq[string], getPatternsForKey func(key string) []string, comparePaths func(a, b string) stringutil.Comparison) bool</c>（string_completions.go:1413-1498）。
    /// Returns true if `fragment` was a match for any `paths`（which should indicate whether any other path completions should be offered）。</summary>
    internal bool AddCompletionEntriesFromPathsOrExportsOrImports(
        ModuleCompletionNameAndKindSet result,
        Program program,
        bool isExports,
        bool isImports,
        string fragment,
        string baseDirectory,
        ExtensionOptions extensionOptions,
        IEnumerable<string> keys,
        Func<string, string[]> getPatternsForKey,
        Comparison<string> comparePaths)
    {
        var pathResults = new List<(ModuleCompletionNameAndKind[] Results, bool Matched)>();
        string? matchedPath = null;
        foreach (var key in keys)
        {
            if (key == ".")
            {
                continue;
            }

            var normalizedKey = key.StartsWith("./", StringComparison.Ordinal) ? key[2..] : key; // Remove leading "./"
            if ((isExports || isImports) && key.EndsWith("/", StringComparison.Ordinal)) // Normalize trailing "/" to "/*"
            {
                normalizedKey = normalizedKey + "*";
            }

            var patterns = getPatternsForKey(key);
            if (patterns.Length > 0)
            {
                var pathPattern = TryParsePattern(normalizedKey);
                if (!pathPattern.IsValid())
                {
                    continue;
                }

                var isMatch = pathPattern.Matches(fragment);
                var isLongestMatch = false;
                if (isMatch)
                {
                    if (matchedPath is null)
                    {
                        isLongestMatch = true;
                    }
                    else
                    {
                        isLongestMatch = comparePaths(normalizedKey, matchedPath) == ComparisonLessThan;
                    }
                }

                if (isLongestMatch)
                {
                    // If this is a higher priority match than anything we've seen so far, previous results from matches are invalid, e.g.
                    // for `import {} from "some-package/|"` with a typesVersions:
                    // {
                    //   "bar/*": ["bar/*"], // <-- 1. We add 'bar', but 'bar/*' doesn't match yet.
                    //   "*": ["dist/*"],    // <-- 2. We match here and add files from dist. 'bar' is still ok because it didn't come from a match.
                    //   "foo/*": ["foo/*"]  // <-- 3. We matched '*' earlier and added results from dist, but if 'foo/*' also matched,
                    // }                               results in dist would not be visible. 'bar' still stands because it didn't come from a match.
                    //                                 This is especially important if `dist/foo` is a folder, because if we fail to clear results
                    //                                 added by the '*' match, after typing `"some-package/foo/|"` we would get file results from both
                    //                                 ./dist/foo and ./foo, when only the latter will actually be resolvable.
                    //                                 See pathCompletionsTypesVersionsWildcard6.ts.
                    matchedPath = normalizedKey;
                    pathResults = [.. Filter(pathResults.ToArray(), pr => !pr.Matched)];
                }

                if (pathPattern.StarIndex == -1
                    || matchedPath is null
                    || comparePaths(normalizedKey, matchedPath) != ComparisonGreaterThan)
                {
                    pathResults.Add((
                        GetCompletionsForPathMapping(
                            normalizedKey,
                            patterns,
                            fragment,
                            baseDirectory,
                            isExports,
                            isImports,
                            extensionOptions,
                            program),
                        isMatch));
                }
            }
        }

        foreach (var (results, _) in pathResults)
        {
            foreach (var res in results)
            {
                result.Add(res);
            }
        }

        return matchedPath is not null;
    }

    /// <summary>Go: <c>func (l *LanguageService) getCompletionsForPathMapping(path string, patterns []string, fragment string, packageDirectory string, isExports bool, isImports bool, extensionOptions *extensionOptions, program *compiler.Program) []moduleCompletionNameAndKind</c>（string_completions.go:1500-1596）。</summary>
    internal ModuleCompletionNameAndKind[] GetCompletionsForPathMapping(
        string path,
        string[] patterns,
        string fragment,
        string packageDirectory,
        bool isExports,
        bool isImports,
        ExtensionOptions extensionOptions,
        Program program)
    {
        var fragmentDirectory = GetFragmentDirectory(fragment);
        if (fragmentDirectory != "")
        {
            fragmentDirectory = EnsureTrailingDirectorySeparator(fragmentDirectory);
        }

        ModuleCompletionNameAndKind[] JustPathMappingName(string name, ModuleCompletionKind kind, string extension)
        {
            if (name.StartsWith(fragment, StringComparison.Ordinal))
            {
                name = RemoveTrailingDirectorySeparator(name);
                if (fragmentDirectory != "")
                {
                    name = name.StartsWith(fragmentDirectory, StringComparison.Ordinal) ? name[fragmentDirectory.Length..] : name;
                }

                return
                [
                    new ModuleCompletionNameAndKind
                    {
                        _name = name,
                        _kind = kind,
                        _extension = extension,
                    },
                ];
            }

            return [];
        }

        var parsedPath = TryParsePattern(path);
        if (!parsedPath.IsValid())
        {
            return [];
        }

        // No stars in the pattern.
        if (parsedPath.StarIndex == -1)
        {
            // For a path mapping "foo": ["/x/y/z.ts"], add "foo" itself as a completion.
            var pattern = FirstOrNil(patterns);
            var extension = GetFileExtension(pattern!);
            return JustPathMappingName(path, ModuleCompletionKind.File, extension);
        }

        var pathPrefix = parsedPath.Text[..parsedPath.StarIndex];
        var pathSuffix = parsedPath.Text[(parsedPath.StarIndex + 1)..];
        if (!fragment.StartsWith(pathPrefix, StringComparison.Ordinal))
        {
            // Fragment doesn't match the path mapping prefix at all:
            // we cannot extend it via this path.
            if (!pathPrefix.StartsWith(fragment, StringComparison.Ordinal))
            {
                return [];
            }

            var starIsFullPathComponent = path.EndsWith("/*", StringComparison.Ordinal);
            if (starIsFullPathComponent)
            {
                return JustPathMappingName(pathPrefix, ModuleCompletionKind.Directory, "" /*extension*/);
            }

            // If path is e.g. `foo/bar/*`, and fragment is `foo/b`, then remaining directory prefix is `bar/`,
            var remainingDirectoryPrefix = pathPrefix[fragmentDirectory.Length..];
            var completions = new List<ModuleCompletionNameAndKind>();
            foreach (var pattern in patterns)
            {
                var modules = GetModulesForPathsPattern(
                    "", /*fragment*/
                    packageDirectory,
                    pattern,
                    isExports,
                    isImports,
                    extensionOptions,
                    program);
                for (var i = 0; i < modules.Length; i++)
                {
                    modules[i]._name = remainingDirectoryPrefix + modules[i]._name
                        + (modules[i]._kind == ModuleCompletionKind.File ? pathSuffix : "");
                }

                completions.AddRange(modules);
            }

            return completions.ToArray();
        }

        var remainingFragment = fragment[pathPrefix.Length..];
        var remainingDirectoryFragment = "";
        if (!fragmentDirectory.StartsWith(pathPrefix, StringComparison.Ordinal))
        {
            remainingDirectoryFragment = pathPrefix[fragmentDirectory.Length..];
        }

        return FlatMap(
            patterns,
            pattern =>
            {
                var modules = GetModulesForPathsPattern(
                    remainingFragment,
                    packageDirectory,
                    pattern,
                    isExports,
                    isImports,
                    extensionOptions,
                    program);
                for (var i = 0; i < modules.Length; i++)
                {
                    modules[i]._name = remainingDirectoryFragment + modules[i]._name
                        + (modules[i]._kind == ModuleCompletionKind.File ? pathSuffix : "");
                }

                return modules;
            });
    }

    /// <summary>Go: <c>func (l *LanguageService) getModulesForPathsPattern(fragment string, packageDirectory string, pattern string, isExports bool, isImports bool, extensionOptions *extensionOptions, program *compiler.Program) []moduleCompletionNameAndKind</c>（string_completions.go:1611-1820）。
    /// The input fragment is relative to the path pattern's prefix:
    /// e.g. if path = "bar/_*/baz", and fragment = "bar/_dir", then fragment is "dir".
    /// The names are relative to the path pattern's prefix and fragment directory :
    /// e.g. if path = "bar/_*/baz", and fragment = "bar/_dir/a", and we find result "abd",
    /// the result should be interpreted as "bar/_dir/abd".</summary>
    internal ModuleCompletionNameAndKind[] GetModulesForPathsPattern(
        string fragment,
        string packageDirectory,
        string pattern,
        bool isExports,
        bool isImports,
        ExtensionOptions extensionOptions,
        Program program)
    {
        var parsed = TryParsePattern(pattern);
        if (!parsed.IsValid() || parsed.StarIndex == -1)
        {
            return [];
        }

        var prefix = parsed.Text[..parsed.StarIndex];
        var suffix = parsed.Text[(parsed.StarIndex + 1)..];

        // The prefix has two effective parts: the directory path and the base component after the filepath that is not a
        // full directory component. For example: directory/path/of/prefix/base*
        var normalizedPrefix = ResolvePath(prefix);
        string normalizedPrefixDirectory;
        string normalizedPrefixBase;
        if (HasTrailingDirectorySeparator(prefix))
        {
            normalizedPrefixDirectory = normalizedPrefix;
            normalizedPrefixBase = "";
        }
        else
        {
            normalizedPrefixDirectory = GetDirectoryPath(normalizedPrefix);
            normalizedPrefixBase = GetBaseFileName(normalizedPrefix);
        }

        var fragmentHasPath = ContainsSlash(fragment);
        var fragmentDirectory = "";
        if (fragmentHasPath)
        {
            fragmentDirectory = HasTrailingDirectorySeparator(fragment) ? fragment : GetDirectoryPath(fragment);
        }

        var options = program.Options();
        var ignoreCase = !program.UseCaseSensitiveFileNames();
        var outDir = options.OutDir;
        var declarationDir = options.DeclarationDir;

        // Try and expand the prefix to include any path from the fragment so that we can limit the readDirectory call
        var expandedPrefixDirectory = fragmentHasPath
            ? CombinePaths(normalizedPrefixDirectory, normalizedPrefixBase + fragmentDirectory)
            : normalizedPrefixDirectory;
        // Need to normalize after combining: If we combinePaths("a", "../b"), we want "b" and not "a/../b".
        var baseDirectory = NormalizePath(CombinePaths(packageDirectory, expandedPrefixDirectory));

        var possibleInputBaseDirectoryForOutDir = "";
        var possibleInputBaseDirectoryForDeclarationDir = "";
        if (isImports)
        {
            if (outDir != "")
            {
                possibleInputBaseDirectoryForOutDir = GetPossibleOriginalInputPathWithoutChangingExt(
                    baseDirectory,
                    ignoreCase,
                    outDir,
                    program.CommonSourceDirectory);
            }

            if (declarationDir != "")
            {
                possibleInputBaseDirectoryForDeclarationDir = GetPossibleOriginalInputPathWithoutChangingExt(
                    baseDirectory,
                    ignoreCase,
                    declarationDir,
                    program.CommonSourceDirectory);
            }
        }

        var normalizedSuffix = NormalizePath(suffix);

        var declarationExtension = "";
        string[] inputExtensions = [];
        if (normalizedSuffix != "")
        {
            declarationExtension = GetDeclarationEmitExtensionForPath("_" + normalizedSuffix);
            inputExtensions = GetPossibleOriginalInputExtensionForExtension("_" + normalizedSuffix);
        }

        var matchingSuffixes = new List<string>();
        if (declarationExtension != "")
        {
            matchingSuffixes.Add(ChangeExtension(normalizedSuffix, declarationExtension));
        }

        foreach (var ext in inputExtensions)
        {
            matchingSuffixes.Add(ChangeExtension(normalizedSuffix, ext));
        }

        matchingSuffixes.Add(normalizedSuffix);

        // If we have a suffix, then we read the directory all the way down to avoid returning completions for
        // directories that don't contain files that would match the suffix. A previous comment here was concerned
        // about the case where `normalizedSuffix` includes a `?` character, which should be interpreted literally,
        // but will match any single character as part of the `include` pattern in `tryReadDirectory`. This is not
        // a problem, because (in the extremely unusual circumstance where the suffix has a `?` in it) a `?`
        // interpreted as "any character" can only return *too many* results as compared to the literal
        // interpretation, so we can filter those superfluous results out via `trimPrefixAndSuffix` as we've always
        // done.
        var includeGlobs = new List<string>();
        if (normalizedSuffix != "")
        {
            foreach (var matchingSuffix in matchingSuffixes)
            {
                includeGlobs.Add("**/*" + matchingSuffix);
            }
        }
        else
        {
            includeGlobs.Add("./*");
        }

        var isExportsOrImportsWildcard = (isExports || isImports) && pattern.EndsWith("/*", StringComparison.Ordinal);

        string TrimPrefixAndSuffix(string path, string prefixStr)
        {
            foreach (var matchingSuffix in matchingSuffixes)
            {
                var inner = WithoutStartAndEnd(NormalizePath(path), prefixStr, matchingSuffix);
                if (inner is null)
                {
                    continue;
                }

                return RemoveLeadingDirectorySeparator(inner);
            }

            return "";
        }

        ModuleCompletionNameAndKind[] GetMatchesWithPrefix(string directory)
        {
            var completePrefix = fragmentHasPath
                ? directory
                : EnsureTrailingDirectorySeparator(directory) + normalizedPrefixBase;

            var matches = ReadDirectory(
                directory,
                extensionOptions._extensionsToSearch,
                includeGlobs.ToArray());

            var result = new List<ModuleCompletionNameAndKind>();
            foreach (var match in matches)
            {
                var trimmedWithPattern = TrimPrefixAndSuffix(match, completePrefix);
                if (trimmedWithPattern != "")
                {
                    if (ContainsSlash(trimmedWithPattern))
                    {
                        var pathComponents = GetPathComponents(RemoveLeadingDirectorySeparator(trimmedWithPattern), "");
                        if (pathComponents.Length > 1)
                        {
                            result.Add(new ModuleCompletionNameAndKind
                            {
                                _name = pathComponents[1],
                                _kind = ModuleCompletionKind.Directory,
                            });
                        }
                    }
                    else
                    {
                        var (name, extension) = GetFilenameWithExtensionOption(
                            trimmedWithPattern,
                            program,
                            extensionOptions,
                            isExportsOrImportsWildcard);
                        if (extension == "")
                        {
                            extension = GetFileExtension(match);
                        }

                        result.Add(new ModuleCompletionNameAndKind
                        {
                            _name = name,
                            _kind = ModuleCompletionKind.File,
                            _extension = extension,
                        });
                    }
                }
            }

            return result.ToArray();
        }

        ModuleCompletionNameAndKind[] GetDirectoryMatches(string directoryName)
        {
            var directories = GetDirectories(directoryName);
            var result = new List<ModuleCompletionNameAndKind>();
            foreach (var dir in directories)
            {
                if (dir != "node_modules")
                {
                    result.Add(new ModuleCompletionNameAndKind
                    {
                        _name = dir,
                        _kind = ModuleCompletionKind.Directory,
                    });
                }
            }

            return result.ToArray();
        }

        var matches = new List<ModuleCompletionNameAndKind>();
        matches.AddRange(GetMatchesWithPrefix(baseDirectory));

        if (possibleInputBaseDirectoryForOutDir != "")
        {
            matches.AddRange(GetMatchesWithPrefix(possibleInputBaseDirectoryForOutDir));
        }

        if (possibleInputBaseDirectoryForDeclarationDir != "")
        {
            matches.AddRange(GetMatchesWithPrefix(possibleInputBaseDirectoryForDeclarationDir));
        }

        // If we had a suffix, we already recursively searched for all possible files that could match
        // it and returned the directories leading to those files. Otherwise, assume any directory could
        // have something valid to import.
        if (normalizedSuffix == "")
        {
            matches.AddRange(GetDirectoryMatches(baseDirectory));
            if (possibleInputBaseDirectoryForOutDir != "")
            {
                matches.AddRange(GetDirectoryMatches(possibleInputBaseDirectoryForOutDir));
            }

            if (possibleInputBaseDirectoryForDeclarationDir != "")
            {
                matches.AddRange(GetDirectoryMatches(possibleInputBaseDirectoryForDeclarationDir));
            }
        }

        return matches.ToArray();
    }

    /// <summary>Go: <c>func (l *LanguageService) getStringLiteralCompletionDetails(ctx context.Context, checker *checker.Checker, item *lsproto.CompletionItem, name string, file *ast.SourceFile, position int, contextToken *ast.Node, docFormat lsproto.MarkupKind) *lsproto.CompletionItem</c>（string_completions.go:2046-2070）。</summary>
    internal LsProto.CompletionItem? GetStringLiteralCompletionDetails(
        Context ctx,
        CheckerType checker,
        LsProto.CompletionItem item,
        string name,
        SourceFile file,
        int position,
        Node? contextToken,
        MarkupKind docFormat)
    {
        if (contextToken is null || !IsStringLiteralLike(contextToken))
        {
            return item;
        }

        var completions = GetStringLiteralCompletionEntries(
            ctx,
            file,
            contextToken,
            position,
            checker);
        if (completions is null)
        {
            return item;
        }

        return StringLiteralCompletionDetails(item, name, contextToken, position, completions, file, checker, docFormat);
    }

    /// <summary>Go: <c>func (l *LanguageService) stringLiteralCompletionDetails(item *lsproto.CompletionItem, name string, location *ast.Node, position int, completion *stringLiteralCompletions, file *ast.SourceFile, checker *checker.Checker, docFormat lsproto.MarkupKind) *lsproto.CompletionItem</c>（string_completions.go:2072-2103）。</summary>
    internal LsProto.CompletionItem StringLiteralCompletionDetails(
        LsProto.CompletionItem item,
        string name,
        Node location,
        int position,
        StringLiteralCompletions completion,
        SourceFile file,
        CheckerType checker,
        MarkupKind docFormat)
    {
        if (completion._fromPaths is not null)
        {
            // Path completions have eagerly-resolved details so the client can show an accurate icon
            // for items of file kind based on the file extension provided in the item detail.
            return item;
        }

        if (completion._fromProperties is not null)
        {
            var properties = completion._fromProperties;
            foreach (var symbol in properties._symbols)
            {
                if (symbol.Name == name)
                {
                    return CreateCompletionDetailsForSymbol(item, symbol, checker, location, position, docFormat);
                }
            }
        }
        else if (completion._fromTypes is not null)
        {
            var types = completion._fromTypes;
            foreach (var t in types._types)
            {
                if ((string)t.AsLiteralType()!.Value()! == name)
                {
                    return CreateCompletionDetails(item, name, "" /*documentation*/, docFormat);
                }
            }
        }

        return item;
    }

    /// <summary>Go: <c>func (l *LanguageService) getTripleSlashReferenceCompletions(file *ast.SourceFile, position int, program *compiler.Program, checker *checker.Checker) *pathCompletions</c>（string_completions.go:2188-2247）。</summary>
    internal PathCompletions? GetTripleSlashReferenceCompletions(
        SourceFile file,
        int position,
        Program program,
        CheckerType checker)
    {
        var compilerOptions = program.Options();
        var token = GetTokenAtPosition(file, position);
        var commentRanges = GetLeadingCommentRanges(new NodeFactory(), file.Text(), token.Pos());

        CommentRange? foundRange = null;
        foreach (var commentRange in commentRanges)
        {
            if (position >= commentRange.Range.Pos() && position <= commentRange.Range.End())
            {
                foundRange = commentRange;
                break;
            }
        }

        if (foundRange is null)
        {
            return null;
        }

        var text = file.Text()[foundRange.Range.Pos()..position];
        var (prefix, kind, toComplete, ok) = ParseTripleSlashDirectiveFragment(text);
        if (!ok)
        {
            return null;
        }

        var (replacementSpan, replacementOk) = PathCompletionReplacementSpan(
            file,
            GetDirectoryFragmentRange(toComplete, foundRange.Range.Pos() + prefix.Length));
        if (!replacementOk)
        {
            return null;
        }

        var scriptPath = GetDirectoryPath(file.Path().Value);

        ModuleCompletionNameAndKind[] names = [];
        if (kind == "path")
        {
            var extensionOptions = GetExtensionOptions(compilerOptions, ReferenceKind.FileName, file, ModuleKind.None, null /*checker*/);
            var result = GetCompletionEntriesForDirectoryFragment(
                toComplete,
                scriptPath,
                extensionOptions,
                program,
                true, /*moduleSpecifierIsRelative*/
                file.Path().Value,
                new ModuleCompletionNameAndKindSet { _names = [] });
            names = [.. result._names.Values];
        }
        else if (kind == "types")
        {
            var extensionOptions = GetExtensionOptions(compilerOptions, ReferenceKind.ModuleSpecifier, file, ModuleKind.None, null /*checker*/);
            var result = new ModuleCompletionNameAndKindSet { _names = [] };
            GetCompletionEntriesFromTypings(program, scriptPath, GetFragmentDirectory(toComplete), extensionOptions, result);
            names = [.. result._names.Values];
        }

        return new PathCompletions
        {
            _entries = ToPathCompletions(names),
            _replacementSpan = replacementSpan,
        };
    }
}
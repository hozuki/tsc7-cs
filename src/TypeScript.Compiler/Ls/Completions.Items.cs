// ported from: internal/ls/completions.go:1802-2473 + 4840-6904（分节 B）
//
// 本文件是 `completions.go` 的 **items 分节**：Go 行 1802-2473（completionInfoFromData /
// getCompletionEntriesFromSymbols / completionNameForLiteral / createCompletionItem 等）
// ∪ 4840-6904（setItemDefaults / createLSPCompletionItem / ResolveCompletionItem / getCompletionItemDetails /
// JSDoc 补全 / getExhaustiveCaseSnippets / typeNodeToExpression / createSnippetPrinter）。
// Go 里同文件的其他行段属分节 A（`Completions.cs`：全部 type 声明 + 入口 + getCompletionData）与
// 分节 C（`Completions.Symbols.cs`：2474-4839）；类型一律复用 A 的声明，B **不重复声明任何类型**（CS0101）。
//
// 与 Go 的偏离（逐条说明）：
//
//  1) Go 的**联合类型别名**（`*ast.Expression` / `*ast.Declaration` / `*ast.CaseOrDefaultClauseNode` /
//     `*ast.BindingPatternNode` / `*ast.TokenNode` … 全是 `= Node`）⇒ C# 参数/返回一律写 `Node`。
//  2) Go 的 `completionItem`（本文件里的本地类型）与 `lsproto.CompletionItem`（协议类型）在 Go 里靠包名区分；
//     C# 同命名空间的 `TypeScript.Ls.CompletionItem` 会**遮蔽** `using TypeScript.LsProto;` 引入的同名类型
//     ⇒ 协议类型一律写 `LsProto.CompletionItem` / `LsProto.CompletionList` / `LsProto.CompletionItemDefaults`，
//     本地类型裸写 `CompletionItem` / `CompletionList`。
//  3) `*T` → `T?`；`*[]string` → `string[]?`；`(T, error)` → `(T, Exception? Error)`。
//     ⚠ 唯一有语义代价的一处：Go 的 `setItemDefaults(ctx, …, &data.defaultCommitCharacters, …)` 传的是
//     **字段地址**（恒非 nil），而切片本身可能为 nil；C# 把 `*[]string` 映射成 `string[]?` 后，"指针非 nil 但切片为 nil"
//     与"传 nil"无法区分 ⇒ `completionInfoFromData` 传 `data._defaultCommitCharacters` 为 nil 时，
//     Go 会走"给每个 item 写 nil"的分支（对客户端而言与不写等价，只有 Warning/String 类 item 的空数组会被清成 nil）。
//     其余 4 个调用点传的都是非 nil 局部（`getDefaultCommitCharacters` 恒返回非 nil）⇒ 完全一致。
//  4) Go 的 `*int`（`tabstopCounter` / `counter`，Go 里靠指针改调用方变量）→ C# 的 `ref int`。
//     对应地 `getJSDocParamAnnotation` 里 `if isSnippet { debug.Assert(tabstopCounter != nil) }` 恒真（ref 形参不可能为 nil），
//     已按 Go 原文保留为注释。
//  5) `sync.OnceValue(f)` → `Lazy<T>` + 调用点 `.Value`（先例 FindAllReferences.cs 文件头第 4 条）。
//     Go 的包级未导出 var `jsDocTagNames` → `_jsDocTagNames`；`jsDocTagNameCompletionItems`/`jsDocTagCompletionItems`
//     → `_jsDocTagNameCompletionItems`/`_jsDocTagCompletionItems`（`Lazy<LsProto.CompletionItem[]>`）。
//  6) `panic(fmt.Sprintf(…))` → `Debug.Fail(…)` + `throw new UnreachableException()`（本仓既定写法；
//     `debug.Fail` → `Debug.Fail`，禁止 `using System.Diagnostics;`，故 `Debug` 与 `UnreachableException` 全限定）。
//  7) `switch v := value.(type)` 里 Go 改的是**值拷贝**（PseudoBigInt 分支的 `v.Negative = false`）⇒ C# 直接取
//     `v.Base10Value`（`Negative` 为真时 Base10Value 必非空，等价于清符号后的 `v.String()`）。
//  8) `completionNameForLiteral` 的数字分支：Go 是 `core.StringifyJson(literal, "", "")`（encoding/json 把
//     `jsnum.Number` 当 float64 输出，失败时返回空串且错误被 `_` 丢弃）⇒ C# 用 JSON 编码器自己的浮点格式化
//     `TypeScript.Json.FloatFormat.Format((double)number)`，非有限值时显式返回 ""（对应 Go 的失败分支）。
//  9) `slices.Clone(allCommitCharacters)` → `(string[])AllCommitCharacters.Clone()`；
//     `slices.Index(elements, x)` → `Array.IndexOf(elements, x)`。
// 10) Go 的零值 `collections.Set[T]` 结构体（`var uniques collections.Set[string]`）⇒ C# 必须 `new Set<string>()`
//     （`Set<T>` 是可空接收者语义的类；`Set(int hint)` 对应 `NewSetWithSizeHint`）。
// 11) `map[string]bool`（`uniqueNamesMap`）→ `Dictionary<string,bool>` + `GetValueOrDefault(name)`：
//     缺键 = false，与 Go 读 map 一致。`data._symbolToSortTextMap` 是 `Dictionary<SymbolId,string>`（见 A 的声明）。
// 12) Go 的 `append(sortedEntries, x)` 链式追加（slice 头会被替换）⇒ C# 在函数内用 `List<CompletionItem>` 累积、
//     结束时一次 `ToArray()`（避免 O(n²) 复制；元素与顺序逐一对应）。
// 13) `core.MapNonNil(slice, f)` 的 C# 版要求 `U` 非空 ⇒ 两处 `MapNonNil(fun.Parameters(), …)` 改写为显式
//     `foreach` + `List<T>`（过滤 nil 的语义与顺序不变）。
// 14) Go 在嵌套块里重复声明 `useBraces := …`（遮蔽外层同名变量）⇒ C# 禁止内层块与外层局部重名（CS0136），
//     `createCompletionItem` 的 JSX 分支改名 `useBracesJsx`（用途与外层互不重叠）。
// 15) `utf8.DecodeLastRuneInString` 只用于判断"数字字面量末尾是不是 `.`"（数字字面量全 ASCII）⇒ 逐 UTF-16 单元取末字符。
// 16) `strings.TrimSpace` → `Trim()`；`strings.Join(xs, s)` → `string.Join(s, xs)`；`strings.Contains(s, "\n")` →
//     `s.Contains('\n')`；`strings.HasPrefix` → `StartsWith(…, StringComparison.Ordinal)`；`strings.TrimPrefix(s, "@")`
//     在 C# 没有对应物 ⇒ 写成 `s.StartsWith("@", …) ? s[1..] : s`（只删一次前缀，与 TrimPrefix 一致）。
// 17) `file.Text()[pos:end]` → `file.Text().Substring(pos, end - pos)`。
// 18) `debug.Assert(x != nil, …)` → `Debug.Assert(x is not null, …)`；`Debug` 是全限定/裸名（namespace TypeScript 下的静态类）。
// 19) `newCaseClauseTracker(typeChecker, clauses)` → `NewCaseClauseTracker(typeChecker, clauses)`（G1 落在 Ls/Utilities.cs），
//     `tracker.hasValue(x)`/`addValue(x)` → `HasValue(x)`/`AddValue(x)`（`object` 参数，Go 的 any）。
// 20) Go 的 `l.createLspRangeFromBounds/Node/Position` → `Ls.LanguageService.CreateLspRangeFromBounds/…`，返回
//     `(Range Range, Fidelity Fidelity)`；`&lspRange`（取地址）→ 直接赋 `Range` 值。`Fidelity.IsExact()` 是
//     `TypeScript.SpanMap` 的扩展方法（顶层 `using TypeScript.SpanMap;`）。
// 21) `data.propertyAccessToConvert.Name().Text()` 等 Node 访问器在 C# 侧返回可空 ⇒ 按 Go 的解引用语义补 `!`。
// 22) `IsNonContextualKeyword` 在 `Ast` 与 `LsUtil` 两个静态类里都有（Go 的 ast/lsutil 各一套）⇒ 同时
//     `using static` 两者时裸名会 CS0121 二义：Ast 的那处写全限定 `TypeScript.Ast.Ast.IsNonContextualKeyword(…)`，
//     lsutil 的写 `LsUtil.LsUtil.IsNonContextualKeyword(…)`。
// 23) `tspath.IsDynamicFileName` → `TypeScript.TSPath.TSPath.IsDynamicFileName(…)`（同 A 的写法，避免限定名歧义）。
// 24) 本地类型名冲突：Go 的未导出 `codeAction` 已在 A 里改名 `CompletionCodeAction`，本文件不涉及。
// 25) 深递归：`TypeNodeToExpression`/`EntityNameToExpression`/`JsDocParam*Worker` 沿 AST 递归，入口与 Go 一样由调用方
//     跑在 64 MiB 大栈线程上（本仓既定做法）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using TypeScript.JsNum;
using static TypeScript.JsNum.JsNum;
using static TypeScript.Ls.Ls;
using static TypeScript.LsProto.LsProto;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using CheckerType = TypeScript.Checker.Checker;
using Kind = TypeScript.Ast.Kind;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeFactory = TypeScript.Ast.NodeFactory;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

public static partial class Ls
{
    /// <summary>Go: <c>func keywordCompletionData(keywordFilters, filterOutTSOnlyKeywords, isNewIdentifierLocation) *completionDataKeyword</c>（completions.go:1802-1811）。</summary>
    internal static CompletionDataKeyword KeywordCompletionData(
        KeywordCompletionFilters keywordFilters,
        bool filterOutTSOnlyKeywords,
        bool isNewIdentifierLocation)
    {
        return new CompletionDataKeyword
        {
            _keywordCompletions = GetKeywordCompletions(keywordFilters, filterOutTSOnlyKeywords)!,
            _isNewIdentifierLocation = isNewIdentifierLocation,
        };
    }

    /// <summary>Go: <c>func getDefaultCommitCharacters(isNewIdentifierLocation bool) []string</c>（completions.go:1813-1818）。</summary>
    internal static string[] GetDefaultCommitCharacters(bool isNewIdentifierLocation)
    {
        if (isNewIdentifierLocation)
        {
            return [];
        }

        return (string[])AllCommitCharacters.Clone();
    }

    /// <summary>Go: <c>func completionNameForLiteral(file, preferences, literal literalValue) string</c>（completions.go:2126-2141）。</summary>
    internal static string CompletionNameForLiteral(SourceFile file, UserPreferences preferences, object? literal)
    {
        switch (literal)
        {
            case string text:
                return Quote(file, preferences, text);
            case Number number:
                // Go: name, _ := core.StringifyJson(literal, "", "") —— 等价于"把 double 按 JSON 的浮点形状输出"；
                // 非有限值时 Go 的 StringifyJson 返回空串（error 被丢弃），这里显式对应。
                if (!double.IsFinite((double)number))
                {
                    return "";
                }

                return TypeScript.Json.FloatFormat.Format((double)number);
            case PseudoBigInt bigInt:
                return bigInt.String() + "n";
            default:
                throw new Exception($"Unhandled literal value: {literal}");
        }
    }

    /// <summary>Go: <c>func getInsertTextAndReplacementSpanForImportCompletion(fix, importKind, importStatementCompletion, useSemicolons, file, preferences, isSnippet) (string, *lsproto.Range)</c>（completions.go:2143-2163）。</summary>
    internal static (string InsertText, Range? ReplacementSpan) GetInsertTextAndReplacementSpanForImportCompletion(
        Fix fix,
        ImportKind importKind,
        ImportStatementCompletionInfo importStatementCompletion,
        bool useSemicolons,
        SourceFile file,
        UserPreferences preferences,
        bool isSnippet)
    {
        var quotedModuleSpecifier = EscapeSnippetText(Quote(file, preferences, fix.AutoImportFix.ModuleSpecifier));
        var tabStop = IfElse(isSnippet, "$1", "");
        var suffix = IfElse(useSemicolons, ";", "");
        var topLevelTypeOnlyText = IfElse(importStatementCompletion._isTopLevelTypeOnly, " " + TokenToString(Kind.TypeKeyword) + " ", " ");
        var name = EscapeSnippetText(fix.AutoImportFix.Name);
        var replacementSpan = importStatementCompletion._replacementSpan;

        switch (importKind)
        {
            case ImportKind.CommonJS:
                return ($"import{topLevelTypeOnlyText}{name}{tabStop} = require({quotedModuleSpecifier}){suffix}", replacementSpan);
            case ImportKind.Default:
                return ($"import{topLevelTypeOnlyText}{name}{tabStop} from {quotedModuleSpecifier}{suffix}", replacementSpan);
            case ImportKind.Namespace:
                return ($"import{topLevelTypeOnlyText}* as {name} from {quotedModuleSpecifier}{suffix}", replacementSpan);
            case ImportKind.Named:
                return ($"import{topLevelTypeOnlyText}{{ {IfElse(importStatementCompletion._couldBeTypeOnlyImportSpecifier, TokenToString(Kind.TypeKeyword) + " ", "")}{name}{tabStop} }} from {quotedModuleSpecifier}{suffix}", replacementSpan);
            default:
                throw new Exception("unhandled import kind: " + importKind);
        }
    }

    /// <summary>Go: <c>func createCompletionItemForLiteral(file, preferences, literal literalValue) *lsproto.CompletionItem</c>（completions.go:2165-2176）。</summary>
    internal static LsProto.CompletionItem CreateCompletionItemForLiteral(
        SourceFile file,
        UserPreferences preferences,
        object? literal)
    {
        return new LsProto.CompletionItem
        {
            Label = CompletionNameForLiteral(file, preferences, literal),
            Kind = CompletionItemKind.Constant,
            SortText = SortTextLocationPriority,
            CommitCharacters = [],
        };
    }

    // ── isCompletionListBlocker 及其谓词（completions.go:5195-5420）──────────────────────────────────

    /// <summary>Go: <c>func isCompletionListBlocker(contextToken, previousToken, location, file, position, typeChecker) bool</c>（completions.go:5195-5207）。</summary>
    internal static bool IsCompletionListBlocker(
        Node contextToken,
        Node previousToken,
        Node location,
        SourceFile file,
        int position,
        CheckerType typeChecker)
    {
        return IsInStringOrRegularExpressionOrTemplateLiteral(contextToken, position)
            || IsSolelyIdentifierDefinitionLocation(contextToken, previousToken, file, position, typeChecker)
            || IsDotOfNumericLiteral(contextToken, file)
            || IsInJsxText(contextToken, location)
            || IsBigIntLiteral(contextToken);
    }

    /// <summary>Go: <c>func isInStringOrRegularExpressionOrTemplateLiteral(contextToken, position) bool</c>（completions.go:5210-5219）。</summary>
    internal static bool IsInStringOrRegularExpressionOrTemplateLiteral(Node contextToken, int position)
    {
        // To be "in" one of these literals, the position has to be:
        //   1. entirely within the token text.
        //   2. at the end position of an unterminated token.
        //   3. at the end of a regular expression (due to trailing flags like '/foo/g').
        return ((IsRegularExpressionLiteral(contextToken) || IsStringTextContainingNode(contextToken))
                && contextToken.Loc.ContainsExclusive(position))
            || (position == contextToken.End()
                && (IsUnterminatedLiteral(contextToken) || IsRegularExpressionLiteral(contextToken)));
    }

    /// <summary>Go: <c>func isSolelyIdentifierDefinitionLocation(contextToken, previousToken, file, position, typeChecker) bool</c>（completions.go:5222-5363）。
    /// true if we are certain that the currently edited location must define a new location; false otherwise。</summary>
    internal static bool IsSolelyIdentifierDefinitionLocation(
        Node contextToken,
        Node previousToken,
        SourceFile file,
        int position,
        CheckerType typeChecker)
    {
        var parent = contextToken.Parent!;
        var containingNodeKind = parent.Kind;
        switch (contextToken.Kind)
        {
            case Kind.CommaToken:
                return containingNodeKind == Kind.VariableDeclaration
                    || IsVariableDeclarationListButNotTypeArgument(contextToken, file, typeChecker)
                    || containingNodeKind == Kind.VariableStatement
                    || containingNodeKind == Kind.EnumDeclaration // enum a { foo, |
                    || IsFunctionLikeButNotConstructor(containingNodeKind)
                    || containingNodeKind == Kind.InterfaceDeclaration // interface A<T, |
                    || containingNodeKind == Kind.ArrayBindingPattern // var [x, y|
                    || containingNodeKind == Kind.TypeAliasDeclaration // type Map, K, |
                    // class A<T, |
                    // var C = class D<T, |
                    || (IsClassLike(parent)
                        && parent.TypeParameterList() is not null
                        && parent.TypeParameterList()!.End() >= contextToken.Pos());
            case Kind.DotToken:
                return containingNodeKind == Kind.ArrayBindingPattern; // var [.|
            case Kind.ColonToken:
                return containingNodeKind == Kind.BindingElement; // var {x :html|
            case Kind.OpenBracketToken:
                return containingNodeKind == Kind.ArrayBindingPattern; // var [x|
            case Kind.OpenParenToken:
                return containingNodeKind == Kind.CatchClause || IsFunctionLikeButNotConstructor(containingNodeKind);
            case Kind.OpenBraceToken:
                return containingNodeKind == Kind.EnumDeclaration; // enum a { |
            case Kind.LessThanToken:
                return containingNodeKind == Kind.ClassDeclaration // class A< |
                    || containingNodeKind == Kind.ClassExpression // var C = class D< |
                    || containingNodeKind == Kind.InterfaceDeclaration // interface A< |
                    || containingNodeKind == Kind.TypeAliasDeclaration // type List< |
                    || IsFunctionLikeKind(containingNodeKind);
            case Kind.StaticKeyword:
                return containingNodeKind == Kind.PropertyDeclaration
                    && !IsClassLike(parent.Parent!);
            case Kind.DotDotDotToken:
                return containingNodeKind == Kind.Parameter
                    || (parent.Parent is not null && parent.Parent.Kind == Kind.ArrayBindingPattern); // var [...z|
            case Kind.PublicKeyword:
            case Kind.PrivateKeyword:
            case Kind.ProtectedKeyword:
                return containingNodeKind == Kind.Parameter && !IsConstructorDeclaration(parent.Parent!);
            case Kind.AsKeyword:
                return containingNodeKind == Kind.ImportSpecifier
                    || containingNodeKind == Kind.ExportSpecifier
                    || containingNodeKind == Kind.NamespaceImport;
            case Kind.GetKeyword:
            case Kind.SetKeyword:
                return !IsFromObjectTypeDeclaration(contextToken);
            case Kind.Identifier:
                if ((containingNodeKind == Kind.ImportSpecifier || containingNodeKind == Kind.ExportSpecifier)
                    && contextToken == parent.Name()
                    && contextToken.Text() == "type")
                {
                    // import { type | }
                    return false;
                }

                var ancestorVariableDeclaration = FindAncestor(parent, n => IsVariableDeclaration(n));
                if (ancestorVariableDeclaration is not null && GetLineEndOfPosition(file, contextToken.End()) < position)
                {
                    // let a
                    // |
                    return false;
                }

                break;
            case Kind.ClassKeyword:
            case Kind.EnumKeyword:
            case Kind.InterfaceKeyword:
            case Kind.FunctionKeyword:
            case Kind.VarKeyword:
            case Kind.ImportKeyword:
            case Kind.LetKeyword:
            case Kind.ConstKeyword:
            case Kind.InferKeyword:
                return true;
            case Kind.TypeKeyword:
                // import { type foo| }
                return containingNodeKind != Kind.ImportSpecifier;
            case Kind.AsteriskToken:
                return IsFunctionLike(parent) && !IsMethodDeclaration(parent);
        }

        var tokenKind = KeywordForNode(contextToken);
        // If the previous token is keyword corresponding to class member completion keyword
        // there will be completion available here
        if (IsClassMemberCompletionKeyword(tokenKind) && IsFromObjectTypeDeclaration(contextToken))
        {
            return false;
        }

        if (IsConstructorParameterCompletion(contextToken))
        {
            // constructor parameter completion is available only if
            // - its modifier of the constructor parameter or
            // - its name of the parameter and not being edited
            // eg. constructor(a |<- this shouldnt show completion
            if (!IsIdentifier(contextToken)
                || IsParameterPropertyModifier(tokenKind)
                || IsCurrentlyEditingNode(contextToken, file, position))
            {
                return false;
            }
        }

        // Previous token may have been a keyword that was converted to an identifier.
        switch (KeywordForNode(contextToken))
        {
            case Kind.AbstractKeyword:
            case Kind.ClassKeyword:
            case Kind.DeclareKeyword:
            case Kind.EnumKeyword:
            case Kind.FunctionKeyword:
            case Kind.InterfaceKeyword:
            case Kind.LetKeyword:
            case Kind.PrivateKeyword:
            case Kind.ProtectedKeyword:
            case Kind.PublicKeyword:
            case Kind.StaticKeyword:
            case Kind.VarKeyword:
                return true;
            case Kind.AsyncKeyword:
                return IsPropertyDeclaration(contextToken.Parent!);
        }

        // If we are inside a class declaration, and `constructor` is totally not present,
        // but we request a completion manually at a whitespace...
        var ancestorClassLike = FindAncestor(parent, n => IsClassLike(n));
        if (ancestorClassLike is not null
            && contextToken == previousToken
            && IsPreviousPropertyDeclarationTerminated(contextToken, file, position))
        {
            // Don't block completions.
            return false;
        }

        var ancestorPropertyDeclaration = FindAncestor(parent, n => IsPropertyDeclaration(n));
        // If we are inside a class declaration and typing `constructor` after property declaration...
        if (ancestorPropertyDeclaration is not null
            && contextToken != previousToken
            && IsClassLike(previousToken.Parent!.Parent!)
            // And the cursor is at the token...
            && position <= previousToken.End())
        {
            // If we are sure that the previous property declaration is terminated according to newline or semicolon...
            if (IsPreviousPropertyDeclarationTerminated(contextToken, file, previousToken.End()))
            {
                // Don't block completions.
                return false;
            }
            else if (contextToken.Kind != Kind.EqualsToken
                // Should not block: `class C { blah = c/**/ }`
                // But should block: `class C { blah = somewhat c/**/ }` and `class C { blah: SomeType c/**/ }`
                && (IsInitializedProperty(ancestorPropertyDeclaration) || ancestorPropertyDeclaration.Type() is not null))
            {
                return true;
            }
        }

        if (tokenKind == Kind.ConstKeyword)
        {
            return true;
        }

        return IsDeclarationName(contextToken)
            && !IsShorthandPropertyAssignment(parent)
            && !IsJsxAttribute(parent)
            // Don't block completions if we're in `class C /**/`, `interface I /**/` or `<T /**/>` ,
            // because we're *past* the end of the identifier and might want to complete `extends`.
            // If `contextToken !== previousToken`, this is `class C ex/**/`, `interface I ex/**/` or `<T ex/**/>`.
            && !((IsClassLike(parent) || IsInterfaceDeclaration(parent) || IsTypeParameterDeclaration(parent))
                && (contextToken != previousToken || position > previousToken.End()));
    }

    /// <summary>Go: <c>func isVariableDeclarationListButNotTypeArgument(node, file, typeChecker) bool</c>（completions.go:5366-5369）。</summary>
    internal static bool IsVariableDeclarationListButNotTypeArgument(Node node, SourceFile file, CheckerType typeChecker)
    {
        return node.Parent!.Kind == Kind.VariableDeclarationList
            && !IsPossiblyTypeArgumentPosition(node, file, typeChecker);
    }

    /// <summary>Go: <c>func isFunctionLikeButNotConstructor(kind ast.Kind) bool</c>（completions.go:5371-5373）。</summary>
    internal static bool IsFunctionLikeButNotConstructor(Kind kind)
    {
        return IsFunctionLikeKind(kind) && kind != Kind.Constructor;
    }

    /// <summary>Go: <c>func isPreviousPropertyDeclarationTerminated(contextToken, file, position) bool</c>（completions.go:5375-5379）。</summary>
    internal static bool IsPreviousPropertyDeclarationTerminated(Node contextToken, SourceFile file, int position)
    {
        return contextToken.Kind != Kind.EqualsToken
            && (contextToken.Kind == Kind.SemicolonToken
                || GetLineOfPosition(file, contextToken.End()) != GetLineOfPosition(file, position));
    }

    /// <summary>Go: <c>func isDotOfNumericLiteral(contextToken, file) bool</c>（completions.go:5381-5389）。</summary>
    internal static bool IsDotOfNumericLiteral(Node contextToken, SourceFile file)
    {
        if (contextToken.Kind == Kind.NumericLiteral)
        {
            var text = file.Text().Substring(contextToken.Pos(), contextToken.End() - contextToken.Pos());
            // Go: r, _ := utf8.DecodeLastRuneInString(text); return r == '.'
            return text.Length != 0 && text[^1] == '.';
        }

        return false;
    }

    /// <summary>Go: <c>func isInJsxText(contextToken, location) bool</c>（completions.go:5391-5421）。</summary>
    internal static bool IsInJsxText(Node contextToken, Node location)
    {
        if (contextToken.Kind == Kind.JsxText)
        {
            return true;
        }

        if (contextToken.Kind == Kind.GreaterThanToken && contextToken.Parent is not null)
        {
            // <Component<string> /**/ />
            // <Component<string> /**/ ><Component>
            // - contextToken: GreaterThanToken (before cursor)
            // - location: JsxSelfClosingElement or JsxOpeningElement
            // - contextToken.parent === location
            if (location == contextToken.Parent && IsJsxOpeningLikeElement(location))
            {
                return false;
            }

            if (contextToken.Parent.Kind == Kind.JsxOpeningElement)
            {
                // <div>/**/
                // - contextToken: GreaterThanToken (before cursor)
                // - location: JSXElement
                // - different parents (JSXOpeningElement, JSXElement)
                return location.Parent!.Kind != Kind.JsxOpeningElement;
            }

            if (contextToken.Parent.Kind == Kind.JsxClosingElement
                || contextToken.Parent.Kind == Kind.JsxSelfClosingElement)
            {
                return contextToken.Parent.Parent is not null
                    && contextToken.Parent.Parent.Kind == Kind.JsxElement;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func getArgumentInfoForCompletions(node, position, file, typeChecker) *argumentInfoForCompletions</c>（completions.go:5453-5466）。</summary>
    internal static ArgumentInfoForCompletions? GetArgumentInfoForCompletions(
        Node node,
        int position,
        SourceFile file,
        CheckerType typeChecker)
    {
        var info = GetImmediatelyContainingArgumentInfo(node, position, file, typeChecker);
        if (info is null || info._isTypeParameterList || info._invocation._callInvocation is null)
        {
            return null;
        }

        return new ArgumentInfoForCompletions
        {
            _invocation = info._invocation._callInvocation._node,
            _argumentIndex = info._argumentIndex,
            _argumentCount = info._argumentCount,
        };
    }

    /// <summary>Go: <c>func getCompletionDocumentationFormat(ctx context.Context) lsproto.MarkupKind</c>（completions.go:5512-5514）。</summary>
    internal static MarkupKind GetCompletionDocumentationFormat(Context ctx)
    {
        return PreferredMarkupKind(GetClientCapabilities().TextDocument.Completion.CompletionItem.DocumentationFormat);
    }

    /// <summary>Go: <c>func createSimpleDetails(item, name, docFormat) *lsproto.CompletionItem</c>（completions.go:5698-5704）。</summary>
    internal static LsProto.CompletionItem CreateSimpleDetails(
        LsProto.CompletionItem item,
        string name,
        MarkupKind docFormat)
    {
        return CreateCompletionDetails(item, name, "" /*documentation*/, docFormat);
    }

    /// <summary>Go: <c>func createCompletionDetails(item, detail, documentation, docFormat) *lsproto.CompletionItem</c>（completions.go:5706-5725）。</summary>
    internal static LsProto.CompletionItem CreateCompletionDetails(
        LsProto.CompletionItem item,
        string detail,
        string documentation,
        MarkupKind docFormat)
    {
        // !!! fill in additionalTextEdits from code actions
        if (item.Detail is null && detail != "")
        {
            item.Detail = detail;
        }

        if (documentation != "")
        {
            item.Documentation = new StringOrMarkupContent
            {
                MarkupContent = new MarkupContent
                {
                    Kind = docFormat,
                    Value = documentation,
                },
            };
        }

        return item;
    }

    /// <summary>Go: <c>func couldBeTypeOnlyImportSpecifier(importSpecifier, contextToken) bool</c>（completions.go:5879-5881）。</summary>
    internal static bool CouldBeTypeOnlyImportSpecifier(Node importSpecifier, Node contextToken)
    {
        return IsImportSpecifier(importSpecifier)
            && (importSpecifier.IsTypeOnly()
                || (contextToken == importSpecifier.Name() && IsTypeKeywordTokenOrIdentifier(contextToken)));
    }

    /// <summary>Go: <c>func canCompleteFromNamedBindings(namedBindings *ast.NamedImportBindings) bool</c>（completions.go:5883-5909）。</summary>
    internal static bool CanCompleteFromNamedBindings(Node namedBindings)
    {
        if (!IsModuleSpecifierMissingOrEmpty(namedBindings.Parent!.Parent!.ModuleSpecifier()!)
            || namedBindings.Parent!.Name() is not null)
        {
            return false;
        }

        if (IsNamedImports(namedBindings))
        {
            // We can only complete on named imports if there are no other named imports already,
            // but parser recovery sometimes puts later statements in the named imports list, so
            // we try to only consider the probably-valid ones.
            var invalidNamedImport = GetPotentiallyInvalidImportSpecifier(namedBindings);
            var elements = namedBindings.Elements() ?? [];
            var validImports = elements.Length;
            if (invalidNamedImport is not null)
            {
                validImports = Array.IndexOf(elements, invalidNamedImport);
            }

            return validImports < 2 && validImports > -1;
        }

        return true;
    }

    /// <summary>Go: <c>func getPotentiallyInvalidImportSpecifier(namedBindings *ast.NamedImportBindings) *ast.Node</c>（completions.go:5911-5919）。
    /// Tries to identify the first named import that is not really a named import, but rather
    /// just parser recovery for a situation like:
    ///
    ///     import { Foo|
    ///     interface Bar {}
    ///
    /// in which `Foo`, `interface`, and `Bar` are all parsed as import specifiers. The caller
    /// will also check if this token is on a separate line from the rest of the import。</summary>
    internal static Node? GetPotentiallyInvalidImportSpecifier(Node? namedBindings)
    {
        if (namedBindings is null || namedBindings.Kind != Kind.NamedImports)
        {
            return null;
        }

        return Find(namedBindings.Elements(), e =>
            e.PropertyName() is null
            && LsUtil.LsUtil.IsNonContextualKeyword(StringToToken(e.Name()!.Text()))
            && FindPrecedingToken(GetSourceFileOfNode(namedBindings)!, e.Name()!.Pos())!.Kind != Kind.CommaToken);
    }

    /// <summary>Go: <c>func isModuleSpecifierMissingOrEmpty(specifier *ast.Expression) bool</c>（completions.go:5921-5933）。</summary>
    internal static bool IsModuleSpecifierMissingOrEmpty(Node specifier)
    {
        if (NodeIsMissing(specifier))
        {
            return true;
        }

        var node = specifier;
        if (IsExternalModuleReference(node))
        {
            node = node.Expression()!;
        }

        if (!IsStringLiteralLike(node))
        {
            return true;
        }

        return node.Text() == "";
    }

    /// <summary>Go: <c>func hasDocComment(file, position) bool</c>（completions.go:5935-5938）。</summary>
    internal static bool HasDocComment(SourceFile file, int position)
    {
        var token = GetTokenAtPosition(file, position);
        return FindAncestor(token, n => n.IsJSDoc()) is not null;
    }

    /// <summary>Go: <c>func getJSDocTagAtPosition(node, position) *ast.Node</c>（completions.go:5941-5951）。
    /// Get the corresponding JSDocTag node if the position is in a JSDoc comment。</summary>
    internal static Node? GetJSDocTagAtPosition(Node node, int position)
    {
        return FindAncestorOrQuit(node, n =>
        {
            if (IsJSDocTag(n) && n.Loc.ContainsInclusive(position))
            {
                return FindAncestorResult.True;
            }

            if (n.IsJSDoc())
            {
                return FindAncestorResult.Quit;
            }

            return FindAncestorResult.False;
        });
    }

    /// <summary>Go: <c>func tryGetTypeExpressionFromTag(tag) *ast.Node</c>（completions.go:5953-5969）。</summary>
    internal static Node? TryGetTypeExpressionFromTag(Node tag)
    {
        if (IsTagWithTypeExpression(tag))
        {
            Node? typeExpression;
            if (IsJSDocTemplateTag(tag))
            {
                typeExpression = tag.AsJSDocTemplateTag()!.Constraint;
            }
            else
            {
                typeExpression = tag.TypeExpression();
            }

            if (typeExpression is not null && typeExpression.Kind == Kind.JSDocTypeExpression)
            {
                return typeExpression;
            }
        }

        if (IsJSDocAugmentsTag(tag) || IsJSDocImplementsTag(tag))
        {
            return tag.ClassName();
        }

        return null;
    }

    /// <summary>Go: <c>func isTagWithTypeExpression(tag) bool</c>（completions.go:5971-5981）。</summary>
    internal static bool IsTagWithTypeExpression(Node tag)
    {
        switch (tag.Kind)
        {
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
            case Kind.JSDocReturnTag:
            case Kind.JSDocTypeTag:
            case Kind.JSDocTypedefTag:
            case Kind.JSDocThrowsTag:
            case Kind.JSDocSatisfiesTag:
                return true;
            case Kind.JSDocTemplateTag:
                return tag.AsJSDocTemplateTag()!.Constraint is not null;
            default:
                return false;
        }
    }

    // ── JSDoc 标签名补全（completions.go:6005-6124）─────────────────────────────────────────────────

    /// <summary>Go: <c>var jsDocTagNames []string</c>（completions.go:6005-6091）。</summary>
    internal static readonly string[] _jsDocTagNames =
    [
        "abstract",
        "access",
        "alias",
        "argument",
        "async",
        "augments",
        "author",
        "borrows",
        "callback",
        "class",
        "classdesc",
        "constant",
        "constructor",
        "constructs",
        "copyright",
        "default",
        "deprecated",
        "description",
        "emits",
        "enum",
        "event",
        "example",
        "exports",
        "extends",
        "external",
        "field",
        "file",
        "fileoverview",
        "fires",
        "function",
        "generator",
        "global",
        "hideconstructor",
        "host",
        "ignore",
        "implements",
        "import",
        "inheritdoc",
        "inner",
        "instance",
        "interface",
        "kind",
        "lends",
        "license",
        "link",
        "linkcode",
        "linkplain",
        "listens",
        "member",
        "memberof",
        "method",
        "mixes",
        "module",
        "name",
        "namespace",
        "overload",
        "override",
        "package",
        "param",
        "private",
        "prop",
        "property",
        "protected",
        "public",
        "readonly",
        "requires",
        "returns",
        "satisfies",
        "see",
        "since",
        "static",
        "summary",
        "template",
        "this",
        "throws",
        "todo",
        "tutorial",
        "type",
        "typedef",
        "var",
        "variation",
        "version",
        "virtual",
        "yields",
    ];

    /// <summary>Go: <c>var jsDocTagNameCompletionItems = sync.OnceValue(…)</c>（completions.go:6093-6106）。</summary>
    internal static readonly Lazy<LsProto.CompletionItem[]> _jsDocTagNameCompletionItems = new(() =>
    {
        var items = new List<LsProto.CompletionItem>(_jsDocTagNames.Length);
        foreach (var tagName in _jsDocTagNames)
        {
            items.Add(new LsProto.CompletionItem
            {
                Label = tagName,
                Kind = CompletionItemKind.Keyword,
                SortText = SortTextLocationPriority,
            });
        }

        return items.ToArray();
    });

    /// <summary>Go: <c>var jsDocTagCompletionItems = sync.OnceValue(…)</c>（completions.go:6108-6117）。</summary>
    internal static readonly Lazy<LsProto.CompletionItem[]> _jsDocTagCompletionItems = new(() =>
    {
        var items = new List<LsProto.CompletionItem>(_jsDocTagNames.Length);
        foreach (var tagName in _jsDocTagNames)
        {
            items.Add(new LsProto.CompletionItem
            {
                Label = "@" + tagName,
                Kind = CompletionItemKind.Keyword,
                SortText = SortTextLocationPriority,
            });
        }

        return items.ToArray();
    });

    /// <summary>Go: <c>func getJSDocTagNameCompletions() []*CompletionItem</c>（completions.go:6118-6120）。</summary>
    internal static CompletionItem[] GetJSDocTagNameCompletions()
    {
        return CloneItems(_jsDocTagNameCompletionItems.Value)!;
    }

    /// <summary>Go: <c>func getJSDocTagCompletions() []*CompletionItem</c>（completions.go:6122-6124）。</summary>
    internal static CompletionItem[] GetJSDocTagCompletions()
    {
        return CloneItems(_jsDocTagCompletionItems.Value)!;
    }

    // ── JSDoc 参数补全（completions.go:6126-6566）──────────────────────────────────────────────────

    /// <summary>Go: <c>func getJSDocParameterCompletions(ctx, file, position, typeChecker, options, preferences, tagNameOnly) []*CompletionItem</c>（completions.go:6126-6267）。</summary>
    internal static CompletionItem[] GetJSDocParameterCompletions(
        Context ctx,
        SourceFile file,
        int position,
        CheckerType typeChecker,
        CompilerOptions options,
        UserPreferences preferences,
        bool tagNameOnly)
    {
        var currentToken = GetTokenAtPosition(file, position);
        if (!IsJSDocTag(currentToken) && !currentToken.IsJSDoc())
        {
            return [];
        }

        Node jsDoc;
        if (currentToken.IsJSDoc())
        {
            jsDoc = currentToken;
        }
        else
        {
            jsDoc = currentToken.Parent!;
        }

        if (!jsDoc.IsJSDoc())
        {
            return [];
        }

        var fun = jsDoc.Parent!;
        if (!IsFunctionLike(fun))
        {
            return [];
        }

        var isJS = IsSourceFileJS(file);
        // isSnippet := clientSupportsItemSnippet(clientOptions)
        var isSnippet = false; // !!! need snippet printer
        var paramTagCount = 0;
        var tags = jsDoc.AsJSDoc()!.Tags?.Nodes ?? [];
        foreach (var tag in tags)
        {
            if (IsJSDocParameterTag(tag)
                && GetStartOfNode(tag, file, false /*includeJSDoc*/) < position
                && IsIdentifier(tag.Name()!))
            {
                paramTagCount++;
            }
        }

        var paramIndex = -1;
        // Go: core.MapNonNil(fun.Parameters(), …)（见文件头第 13 条：C# 的 U 必须非空 ⇒ 显式 foreach + List）
        var result = new List<CompletionItem>();
        foreach (var param in fun.Parameters() ?? [])
        {
            paramIndex++;
            if (paramIndex < paramTagCount)
            {
                // This parameter is already annotated.
                continue;
            }

            if (IsIdentifier(param.Name()!)) // Named parameter
            {
                var tabstopCounter = 1;
                var paramName = param.Name()!.Text();
                var displayText = GetJSDocParamAnnotation(
                    paramName,
                    param.Initializer(),
                    param.AsParameterDeclaration()!.DotDotDotToken,
                    isJS,
                    false /*isObject*/,
                    false /*isSnippet*/,
                    typeChecker,
                    options,
                    preferences,
                    ref tabstopCounter);
                var snippetText = "";
                if (isSnippet)
                {
                    snippetText = GetJSDocParamAnnotation(
                        paramName,
                        param.Initializer(),
                        param.AsParameterDeclaration()!.DotDotDotToken,
                        isJS,
                        false /*isObject*/,
                        true /*isSnippet*/,
                        typeChecker,
                        options,
                        preferences,
                        ref tabstopCounter);
                }

                if (tagNameOnly) // Remove `@`
                {
                    displayText = displayText[1..];
                    if (snippetText != "")
                    {
                        snippetText = snippetText[1..];
                    }
                }

                result.Add(new CompletionItem
                {
                    _completionItem = new LsProto.CompletionItem
                    {
                        Label = displayText,
                        Kind = CompletionItemKind.Variable,
                        SortText = SortTextLocationPriority,
                        InsertText = StrPtrTo(snippetText),
                        InsertTextFormat = isSnippet ? InsertTextFormat.Snippet : (InsertTextFormat?)null,
                    },
                });
            }
            else if (paramIndex == paramTagCount)
            {
                // Destructuring parameter; do it positionally
                var paramPath = $"param{paramIndex}";
                var displayTextResult = GenerateJSDocParamTagsForDestructuring(
                    paramPath,
                    param.Name()!,
                    param.Initializer(),
                    param.AsParameterDeclaration()!.DotDotDotToken,
                    isJS,
                    false /*isSnippet*/,
                    typeChecker,
                    options,
                    preferences);
                var snippetText = "";
                if (isSnippet)
                {
                    var snippetTextResult = GenerateJSDocParamTagsForDestructuring(
                        paramPath,
                        param.Name()!,
                        param.Initializer(),
                        param.AsParameterDeclaration()!.DotDotDotToken,
                        isJS,
                        true /*isSnippet*/,
                        typeChecker,
                        options,
                        preferences);
                    snippetText = string.Join(options.NewLine.GetNewLineCharacter() + "* ", snippetTextResult);
                }

                var displayText = string.Join(options.NewLine.GetNewLineCharacter() + "* ", displayTextResult);
                if (tagNameOnly) // Remove `@`
                {
                    displayText = displayText.StartsWith("@", StringComparison.Ordinal) ? displayText[1..] : displayText;
                    snippetText = snippetText.StartsWith("@", StringComparison.Ordinal) ? snippetText[1..] : snippetText;
                }

                result.Add(new CompletionItem
                {
                    _completionItem = new LsProto.CompletionItem
                    {
                        Label = displayText,
                        Kind = CompletionItemKind.Variable,
                        SortText = SortTextLocationPriority,
                        InsertText = StrPtrTo(snippetText),
                        InsertTextFormat = isSnippet ? InsertTextFormat.Snippet : (InsertTextFormat?)null,
                    },
                });
            }
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func getJSDocParamAnnotation(paramName, initializer, dotDotDotToken, isJS, isObject, isSnippet, typeChecker, options, preferences, tabstopCounter *int) string</c>（completions.go:6269-6350）。</summary>
    internal static string GetJSDocParamAnnotation(
        string paramName,
        Node? initializer,
        Node? dotDotDotToken,
        bool isJS,
        bool isObject,
        bool isSnippet,
        CheckerType typeChecker,
        CompilerOptions options,
        UserPreferences preferences,
        ref int tabstopCounter)
    {
        // Go: `if isSnippet { debug.Assert(tabstopCounter != nil) }` —— C# 用 `ref int` 取代 Go 的 `*int`，
        // 形参不可能为 nil，该断言恒真（见文件头第 4 条）。
        if (initializer is not null)
        {
            paramName = GetJSDocParamNameWithInitializer(paramName, initializer);
        }

        if (isSnippet)
        {
            paramName = EscapeSnippetText(paramName);
        }

        if (isJS)
        {
            var t = "*";
            if (isObject)
            {
                Debug.Assert(dotDotDotToken is null, "Cannot annotate a rest parameter with type 'object'.");
                t = "object";
            }
            else
            {
                if (initializer is not null)
                {
                    var inferredType = typeChecker.GetTypeAtLocation(initializer.Parent!);
                    if ((inferredType.Flags() & (TypeFlags.Any | TypeFlags.Void)) == 0)
                    {
                        var file = GetSourceFileOfNode(initializer)!;
                        var quotePreference = GetQuotePreference(file, preferences);
                        var builderFlags = IfElse(
                            quotePreference == QuotePreferenceSingle,
                            NodeBuilderFlags.UseSingleQuotesForStringLiteralType,
                            NodeBuilderFlags.None);
                        var typeNode = typeChecker.TypeToTypeNode(
                            inferredType,
                            FindAncestor(initializer, n => IsFunctionLike(n)),
                            builderFlags,
                            null /*idToSymbol*/);
                        if (typeNode is not null)
                        {
                            var emitContext = NewEmitContext();
                            // !!! snippet p
                            var snippetPrinter = NewPrinter(new PrinterOptions
                            {
                                RemoveComments = true,
                                // !!!
                                // Module: options.Module,
                                // ModuleResolution: options.ModuleResolution,
                                // Target: options.Target,
                            }, new PrintHandlers(), emitContext);
                            emitContext.SetEmitFlags(typeNode, EmitFlags.SingleLine);
                            t = snippetPrinter.Emit(typeNode, file);
                        }
                    }
                }

                if (isSnippet && t == "*")
                {
                    t = $"${{{tabstopCounter}:{t}}}";
                    tabstopCounter++;
                }
            }

            var dotDotDot = IfElse(!isObject && dotDotDotToken is not null, "...", "");
            var description = "";
            if (isSnippet)
            {
                description = "${" + tabstopCounter + "}";
                tabstopCounter++;
            }

            return $"@param {{{dotDotDot}{t}}} {paramName} {description}";
        }
        else
        {
            var description = "";
            if (isSnippet)
            {
                description = "${" + tabstopCounter + "}";
                tabstopCounter++;
            }

            return $"@param {paramName} {description}";
        }
    }

    /// <summary>Go: <c>func getJSDocParamNameWithInitializer(paramName, initializer) string</c>（completions.go:6352-6358）。</summary>
    internal static string GetJSDocParamNameWithInitializer(string paramName, Node initializer)
    {
        var initializerText = GetTextOfNode(initializer).Trim();
        if (initializerText.Contains('\n') || initializerText.Length > 80)
        {
            return $"[{paramName}]";
        }

        return $"[{paramName}={initializerText}]";
    }

    /// <summary>Go: <c>func generateJSDocParamTagsForDestructuring(path, pattern, initializer, dotDotDotToken, isJS, isSnippet, typeChecker, options, preferences) []string</c>（completions.go:6360-6398）。</summary>
    internal static string[] GenerateJSDocParamTagsForDestructuring(
        string path,
        Node pattern,
        Node? initializer,
        Node? dotDotDotToken,
        bool isJS,
        bool isSnippet,
        CheckerType typeChecker,
        CompilerOptions options,
        UserPreferences preferences)
    {
        var tabstopCounter = 1;
        if (!isJS)
        {
            return
            [
                GetJSDocParamAnnotation(
                    path,
                    initializer,
                    dotDotDotToken,
                    isJS,
                    false /*isObject*/,
                    isSnippet,
                    typeChecker,
                    options,
                    preferences,
                    ref tabstopCounter),
            ];
        }

        return JsDocParamPatternWorker(
            path,
            pattern,
            initializer,
            dotDotDotToken,
            isJS,
            isSnippet,
            typeChecker,
            options,
            preferences,
            ref tabstopCounter);
    }

    /// <summary>Go: <c>func jsDocParamPatternWorker(path, pattern, initializer, dotDotDotToken, isJS, isSnippet, typeChecker, options, preferences, counter *int) []string</c>（completions.go:6400-6467）。</summary>
    internal static string[] JsDocParamPatternWorker(
        string path,
        Node pattern,
        Node? initializer,
        Node? dotDotDotToken,
        bool isJS,
        bool isSnippet,
        CheckerType typeChecker,
        CompilerOptions options,
        UserPreferences preferences,
        ref int counter)
    {
        if (IsObjectBindingPattern(pattern) && dotDotDotToken is null)
        {
            var childCounter = counter;
            var rootParam = GetJSDocParamAnnotation(
                path,
                initializer,
                dotDotDotToken,
                isJS,
                true /*isObject*/,
                isSnippet,
                typeChecker,
                options,
                preferences,
                ref childCounter);
            List<string>? childTags = [];
            foreach (var element in pattern.Elements() ?? [])
            {
                var elementTags = JsDocParamElementWorker(
                    path,
                    element,
                    initializer,
                    dotDotDotToken,
                    isJS,
                    isSnippet,
                    typeChecker,
                    options,
                    preferences,
                    ref childCounter);
                if (elementTags.Length == 0)
                {
                    childTags = null;
                    break;
                }

                childTags.AddRange(elementTags);
            }

            if (childTags is not null && childTags.Count > 0)
            {
                counter = childCounter;
                return [rootParam, .. childTags];
            }
        }

        return
        [
            GetJSDocParamAnnotation(
                path,
                initializer,
                dotDotDotToken,
                isJS,
                false /*isObject*/,
                isSnippet,
                typeChecker,
                options,
                preferences,
                ref counter),
        ];
    }

    /// <summary>Go: <c>func jsDocParamElementWorker(path, element, initializer, dotDotDotToken, isJS, isSnippet, typeChecker, options, preferences, counter *int) []string</c>（completions.go:6469-6525）。
    /// Assumes binding element is inside object binding pattern.
    /// We can't deeply annotate an array binding pattern。</summary>
    internal static string[] JsDocParamElementWorker(
        string path,
        Node element,
        Node? initializer,
        Node? dotDotDotToken,
        bool isJS,
        bool isSnippet,
        CheckerType typeChecker,
        CompilerOptions options,
        UserPreferences preferences,
        ref int counter)
    {
        if (IsIdentifier(element.Name()!)) // `{ b }` or `{ b: newB }`
        {
            string propertyName;
            if (element.PropertyName() is not null)
            {
                TryGetTextOfPropertyName(element.PropertyName(), out propertyName);
            }
            else
            {
                propertyName = element.Name()!.Text();
            }

            if (propertyName == "")
            {
                return [];
            }

            var paramName = $"{path}.{propertyName}";
            return
            [
                GetJSDocParamAnnotation(
                    paramName,
                    element.Initializer(),
                    element.AsBindingElement()!.DotDotDotToken,
                    isJS,
                    false /*isObject*/,
                    isSnippet,
                    typeChecker,
                    options,
                    preferences,
                    ref counter),
            ];
        }
        else if (element.PropertyName() is not null) // `{ b: {...} }` or `{ b: [...] }`
        {
            TryGetTextOfPropertyName(element.PropertyName(), out var propertyName);
            if (propertyName == "")
            {
                return [];
            }

            return JsDocParamPatternWorker(
                $"{path}.{propertyName}",
                element.Name()!,
                element.Initializer(),
                element.AsBindingElement()!.DotDotDotToken,
                isJS,
                isSnippet,
                typeChecker,
                options,
                preferences,
                ref counter);
        }

        return [];
    }

    /// <summary>Go: <c>func getJSDocParameterNameCompletions(tag *ast.JSDocParameterOrPropertyTag) []*CompletionItem</c>（completions.go:6527-6566）。</summary>
    internal static CompletionItem[] GetJSDocParameterNameCompletions(JSDocParameterOrPropertyTag tag)
    {
        if (!IsIdentifier(tag.Name()!))
        {
            return [];
        }

        var nameThusFar = tag.Name()!.Text();
        var jsDoc = tag.Parent!;
        var fn = jsDoc.Parent!;
        if (!IsFunctionLike(fn))
        {
            return [];
        }

        var tags = jsDoc.AsJSDoc()!.Tags?.Nodes ?? [];

        // Go: core.MapNonNil(fn.Parameters(), …)（见文件头第 13 条）
        var result = new List<CompletionItem>();
        foreach (var param in fn.Parameters() ?? [])
        {
            if (!IsIdentifier(param.Name()!))
            {
                continue;
            }

            var name = param.Name()!.Text();
            if (Some(tags, t => t != tag.AsNode()
                    && IsJSDocParameterTag(t)
                    && IsIdentifier(t.Name()!)
                    && t.Name()!.Text() == name)
                || (nameThusFar != "" && !name.StartsWith(nameThusFar, StringComparison.Ordinal)))
            {
                continue;
            }

            result.Add(new CompletionItem
            {
                _completionItem = new LsProto.CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Variable,
                    SortText = SortTextLocationPriority,
                },
            });
        }

        return result.ToArray();
    }

    // ── case 补全的表达式构造（completions.go:6706-6792）────────────────────────────────────────────

    /// <summary>Go: <c>func typeNodeToExpression(typeNode, target, quotePreference, factory) *ast.Expression</c>（completions.go:6706-6766）。</summary>
    internal static Node? TypeNodeToExpression(
        Node typeNode,
        ScriptTarget target,
        QuotePreference quotePreference,
        NodeFactory factory)
    {
        switch (typeNode.Kind)
        {
            case Kind.TypeReference:
            {
                var typeName = typeNode.AsTypeReferenceNode()!.TypeName!;
                return EntityNameToExpression(typeName, target, quotePreference, factory);
            }

            case Kind.IndexedAccessType:
            {
                var objectExpression = TypeNodeToExpression(
                    typeNode.AsIndexedAccessTypeNode()!.ObjectType!,
                    target,
                    quotePreference,
                    factory);
                var indexExpression = TypeNodeToExpression(
                    typeNode.AsIndexedAccessTypeNode()!.IndexType!,
                    target,
                    quotePreference,
                    factory);
                if (objectExpression is not null && indexExpression is not null)
                {
                    return factory.NewElementAccessExpression(objectExpression, null /*questionDotToken*/, indexExpression, NodeFlags.None);
                }

                return null;
            }

            case Kind.LiteralType:
            {
                var literal = typeNode.AsLiteralTypeNode()!.Literal!;
                switch (literal.Kind)
                {
                    case Kind.StringLiteral:
                    {
                        var expr = factory.NewStringLiteral(
                            literal.Text(),
                            quotePreference == QuotePreferenceSingle ? TokenFlags.SingleQuote : TokenFlags.None);
                        return expr;
                    }

                    case Kind.NumericLiteral:
                    {
                        var expr = factory.NewNumericLiteral(literal.Text(), literal.AsNumericLiteral()!.TokenFlags);
                        return expr;
                    }

                    default:
                        return null;
                }
            }

            case Kind.ParenthesizedType:
            {
                var expr = TypeNodeToExpression(
                    typeNode.AsParenthesizedTypeNode()!.Type!,
                    target,
                    quotePreference,
                    factory);
                if (expr is null)
                {
                    return null;
                }

                if (IsIdentifier(expr))
                {
                    return expr;
                }

                return factory.NewParenthesizedExpression(expr);
            }

            case Kind.TypeQuery:
                return EntityNameToExpression(typeNode.AsTypeQueryNode()!.ExprName!, target, quotePreference, factory);

            case Kind.ImportType:
                Debug.Fail("We should not get an import type after calling 'typeToAutoImportableTypeNode'.");
                return null;
        }

        return null;
    }

    /// <summary>Go: <c>func entityNameToExpression(entityName, target, quotePreference, factory) *ast.Expression</c>（completions.go:6768-6781）。</summary>
    internal static Node? EntityNameToExpression(
        Node entityName,
        ScriptTarget target,
        QuotePreference quotePreference,
        NodeFactory factory)
    {
        if (IsIdentifier(entityName))
        {
            return entityName;
        }

        return factory.NewPropertyAccessExpression(
            EntityNameToExpression(entityName.AsQualifiedName()!.Left!, target, quotePreference, factory),
            null /*questionDotToken*/,
            entityName.AsQualifiedName()!.Right!,
            NodeFlags.None);
    }

    /// <summary>Go: <c>func createSnippetPrinter(options printer.PrinterOptions, emitContext *printer.EmitContext) *snippetPrinter</c>（completions.go:6836-6853）。</summary>
    internal static SnippetPrinter CreateSnippetPrinter(PrinterOptions options, EmitContext? emitContext)
    {
        if (emitContext is null)
        {
            emitContext = NewEmitContext();
        }

        var baseWriter = NewChangeTrackerWriter(options.NewLine.GetNewLineCharacter(), -1);
        var printer = NewPrinter(options, baseWriter.GetPrintHandlers(), emitContext);
        var writer = new SnippetEmitTextWriter
        {
            _changeTrackerWriter = baseWriter,
        };
        return new SnippetPrinter
        {
            _baseWriter = baseWriter,
            _emitContext = emitContext,
            _printer = printer,
            _writer = writer,
            _factory = emitContext.Factory.AsNodeFactory(),
        };
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) completionInfoFromData(ctx, typeChecker, file, compilerOptions, data, position, optionalReplacementSpan, includeSymbols) (*CompletionList, error)</c>（completions.go:1820-1953）。</summary>
    internal (CompletionList? List, Exception? Error) CompletionInfoFromData(
        Context ctx,
        CheckerType typeChecker,
        SourceFile file,
        CompilerOptions compilerOptions,
        CompletionDataData data,
        int position,
        Range? optionalReplacementSpan,
        bool includeSymbols)
    {
        var keywordFilters = data._keywordFilters;
        var isNewIdentifierLocation = data._isNewIdentifierLocation;
        var contextToken = data._contextToken;
        var literals = data._literals;
        var preferences = UserPreferences();

        // Verify if the file is JSX language variant
        if (file.LanguageVariant == LanguageVariant.JSX)
        {
            var list = GetJsxClosingTagCompletion(ctx, data._location, file, position);
            if (list is not null)
            {
                return (list, null);
            }
        }

        // When the completion is for the expression of a case clause (e.g. `case |`),
        // filter literals & enum symbols whose values are already present in existing case clauses.
        var caseClause = FindAncestor(contextToken, n => IsCaseClause(n));
        if (caseClause is not null
            && (contextToken!.Kind == Kind.CaseKeyword
                || IsNodeDescendantOf(contextToken, caseClause.Expression())))
        {
            var tracker = NewCaseClauseTracker(typeChecker, caseClause.Parent!.AsCaseBlock()!.Clauses!.Nodes);
            literals = Filter(literals, literal => !tracker.HasValue(literal));
            data._symbols = Filter(data._symbols, symbol =>
            {
                if (symbol.ValueDeclaration is not null && IsEnumMember(symbol.ValueDeclaration))
                {
                    var value = typeChecker.GetConstantValue(symbol.ValueDeclaration);
                    if (value is not null && tracker.HasValue(value))
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        var isChecked = IsCheckedFile(file, compilerOptions);
        if (isChecked && !isNewIdentifierLocation && data._symbols.Length == 0 && keywordFilters == KeywordCompletionFilters.None)
        {
            return (null, null);
        }

        var (uniqueNames, sortedEntriesFromSymbols, err) = GetCompletionEntriesFromSymbols(
            ctx,
            typeChecker,
            data,
            null, /*replacementToken*/
            position,
            file,
            compilerOptions,
            includeSymbols);
        if (err is not null)
        {
            return (null, err);
        }

        var sortedEntries = new List<CompletionItem>(sortedEntriesFromSymbols);

        if (data._keywordFilters != KeywordCompletionFilters.None)
        {
            var keywordCompletions = GetKeywordCompletions(data._keywordFilters, !data._insideJSDocTagTypeExpression && IsSourceFileJS(file));
            foreach (var keywordEntry in keywordCompletions ?? [])
            {
                if (data._isTypeOnlyLocation && Ls.IsTypeKeyword(StringToToken(keywordEntry._completionItem.Label))
                    || !data._isTypeOnlyLocation && IsContextualKeywordInAutoImportableExpressionSpace(keywordEntry._completionItem.Label)
                    || !uniqueNames.Has(keywordEntry._completionItem.Label))
                {
                    uniqueNames.Add(keywordEntry._completionItem.Label);
                    sortedEntries.Add(keywordEntry);
                }
            }
        }

        foreach (var keywordEntry in GetContextualKeywords(file, contextToken!, position))
        {
            if (!uniqueNames.Has(keywordEntry.Label))
            {
                uniqueNames.Add(keywordEntry.Label);
                sortedEntries.Add(new CompletionItem { _completionItem = keywordEntry });
            }
        }

        foreach (var literal in literals)
        {
            var literalEntry = CreateCompletionItemForLiteral(file, preferences, literal);
            uniqueNames.Add(literalEntry.Label);
            sortedEntries.Add(new CompletionItem { _completionItem = literalEntry });
        }

        if (!isChecked)
        {
            var jsEntries = GetJSCompletionEntries(
                ctx,
                file,
                position,
                uniqueNames,
                sortedEntries.ToArray());
            sortedEntries.Clear();
            sortedEntries.AddRange(jsEntries);
        }

        if (contextToken is not null && !data._isRightOfOpenTag && !data._isRightOfDotOrQuestionDot)
        {
            var caseBlock = FindAncestorKind(contextToken, Kind.CaseBlock);
            if (caseBlock is not null)
            {
                var (casesItem, casesError) = GetExhaustiveCaseSnippets(
                    ctx,
                    caseBlock.AsCaseBlock()!,
                    file,
                    position,
                    compilerOptions,
                    GetProgram()!,
                    typeChecker);
                if (casesError is not null)
                {
                    return (null, casesError);
                }

                if (casesItem is not null)
                {
                    sortedEntries.Add(new CompletionItem { _completionItem = casesItem });
                }
            }
        }

        var items = sortedEntries.ToArray();
        var itemDefaults = SetItemDefaults(
            ctx,
            position,
            file,
            items,
            data._defaultCommitCharacters,
            optionalReplacementSpan);

        return (new CompletionList
        {
            IsIncomplete = data._hasUnresolvedAutoImports,
            ItemDefaults = itemDefaults,
            Items = items,
        }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) getCompletionEntriesFromSymbols(ctx, typeChecker, data, replacementToken, position, file, compilerOptions, includeSymbols) (uniqueNames, sortedEntries, err)</c>（completions.go:1955-2124）。</summary>
    internal (Set<string> UniqueNames, CompletionItem[] SortedEntries, Exception? Error) GetCompletionEntriesFromSymbols(
        Context ctx,
        CheckerType typeChecker,
        CompletionDataData data,
        Node? replacementToken,
        int position,
        SourceFile file,
        CompilerOptions compilerOptions,
        bool includeSymbols)
    {
        var closestSymbolDeclaration = GetClosestSymbolDeclaration(data._contextToken!, data._location);
        var useSemicolons = ProbablyUsesSemicolons(file);
        var preferences = UserPreferences();
        var isMemberCompletion = IsMemberCompletionKind(data._completionKind);
        var sortedEntries = new List<CompletionItem>(data._symbols.Length + data._autoImports.Length);
        // Tracks unique names.
        // Value is set to false for global variables or completions from external module exports, because we can have multiple of those;
        // true otherwise. Based on the order we add things we will always see locals first, then globals, then module exports.
        // So adding a completion for a local will prevent us from adding completions for external module exports sharing the same name.
        var uniques = new Dictionary<string, bool>();
        for (var index = 0; index < data._symbols.Length; index++)
        {
            var symbol = data._symbols[index];
            var origin = data._symbolToOriginInfoMap.GetValueOrDefault(index);
            var (name, needsConvertPropertyAccess) = GetCompletionEntryDisplayNameForSymbol(
                file,
                preferences,
                symbol,
                origin!,
                data._completionKind,
                data._isJsxIdentifierExpected);
            if (name == ""
                || (uniques.GetValueOrDefault(name) && (origin is null || !OriginIsObjectLiteralMethod(origin)))
                || (data._completionKind == CompletionKind.Global
                    && !ShouldIncludeSymbol(symbol, data, closestSymbolDeclaration!, file, typeChecker, compilerOptions)))
            {
                continue;
            }

            // When in a value location in a JS file, ignore symbols that definitely seem to be type-only.
            if (!data._isTypeOnlyLocation && IsSourceFileJS(file) && SymbolAppearsToBeTypeOnly(symbol, typeChecker))
            {
                continue;
            }

            var originalSortText = data._symbolToSortTextMap.GetValueOrDefault(GetSymbolId(symbol));
            if (string.IsNullOrEmpty(originalSortText))
            {
                originalSortText = SortTextLocationPriority;
            }

            var sortText = IsDeprecated(symbol, typeChecker)
                ? DeprecateSortText(originalSortText!)
                : originalSortText!;
            var (entry, err) = CreateCompletionItem(
                ctx,
                typeChecker,
                symbol,
                sortText,
                replacementToken,
                data,
                position,
                file,
                name,
                needsConvertPropertyAccess,
                origin,
                useSemicolons,
                compilerOptions,
                isMemberCompletion);
            if (err is not null)
            {
                // Go: `return uniqueNames, nil, err`（错误路径上切片无意义；C# 的非空数组签名返回空数组，见文件头第 3 条）。
                return (new Set<string>(), [], err);
            }

            if (entry is null)
            {
                continue;
            }

            // True for locals; false for globals, module exports from other files, `this.` completions.
            var shouldShadowLaterSymbols = (origin is null || OriginIsTypeOnlyAlias(origin))
                && !(symbol.Parent is null
                    && !Some(symbol.Declarations, d => GetSourceFileOfNode(d) == file));
            uniques[name] = shouldShadowLaterSymbols;
            Ast.Symbol? sym = includeSymbols ? symbol : null;
            sortedEntries.Add(new CompletionItem { _completionItem = entry, Symbol = sym });
        }

        foreach (var autoImport in data._autoImports)
        {
            // !!! check for type-only in JS
            // !!! deprecation

            Range? replacementSpan = null;
            var insertText = "";
            var filterText = "";
            var isSnippet = false;
            var sortText = SortTextAutoImportSuggestions;

            if (data._importStatementCompletion is not null)
            {
                isSnippet = ClientSupportsItemSnippet(ctx);
                (insertText, replacementSpan) = GetInsertTextAndReplacementSpanForImportCompletion(
                    autoImport.Fix!,
                    AutoImport.GetImportKindForImportStatement(file, autoImport.Export!, GetProgram()!),
                    data._importStatementCompletion,
                    useSemicolons,
                    file,
                    preferences,
                    isSnippet);
                // The edit range covers the whole import statement typed so far, and clients match that text against the
                // filter text, so it has to be the statement being inserted (as in Strada), not just the bare name.
                filterText = insertText;
                sortText = SortTextLocationPriority;
            }

            // Non-contextual keywords (e.g., `function`, `class`, `const`) cannot be used as identifiers,
            // so auto-imports with these names should not shadow keyword completions.
            var token = StringToToken(autoImport.Fix!.AutoImportFix.Name);
            if (token != Kind.Unknown && TypeScript.Ast.Ast.IsNonContextualKeyword(token))
            {
                continue;
            }

            if (!autoImport.Export!.IsUnresolvedAlias())
            {
                if (data._isTypeOnlyLocation)
                {
                    if ((autoImport.Export!.Flags & SymbolFlags.Type) == 0 && (autoImport.Export!.Flags & SymbolFlags.Module) == 0)
                    {
                        continue;
                    }
                }
                else if (data._importStatementCompletion is null && (autoImport.Export!.Flags & SymbolFlags.Value) == 0)
                {
                    continue;
                }
            }

            var entry = CreateLSPCompletionItem(
                ctx,
                autoImport.Fix!.AutoImportFix.Name,
                insertText,
                filterText,
                sortText,
                autoImport.Export!.ScriptElementKind,
                autoImport.Export!.ScriptElementKindModifiers,
                replacementSpan,
                null,
                new CompletionItemLabelDetails
                {
                    Description = autoImport.Fix!.AutoImportFix.ModuleSpecifier,
                },
                file,
                position,
                false, /*isMemberCompletion*/
                isSnippet,
                data._importStatementCompletion is null, /*hasAction*/
                false, /*preselect*/
                autoImport.Fix!.AutoImportFix.ModuleSpecifier,
                autoImport.Fix!.AutoImportFix,
                null, /*additionalTextEdits*/
                null /*detail*/);

            entry.Data!.IsImportStatementCompletion = data._importStatementCompletion is not null;

            if (!uniques.GetValueOrDefault(autoImport.Fix!.AutoImportFix.Name))
            {
                uniques[autoImport.Fix!.AutoImportFix.Name] = false;
                sortedEntries.Add(new CompletionItem { _completionItem = entry });
            }
        }

        var uniqueSet = new Set<string>(uniques.Count);
        foreach (var name in uniques.Keys)
        {
            uniqueSet.Add(name);
        }

        return (uniqueSet, sortedEntries.ToArray(), null);
    }

    /// <summary>Go: <c>func (l *LanguageService) createCompletionItem(ctx, typeChecker, symbol, sortText, replacementToken, data, position, file, name, needsConvertPropertyAccess, origin, useSemicolons, compilerOptions, isMemberCompletion) (*lsproto.CompletionItem, error)</c>（completions.go:2178-2464）。</summary>
    internal (LsProto.CompletionItem? Value0, Exception? Error) CreateCompletionItem(
        Context ctx,
        CheckerType typeChecker,
        Ast.Symbol symbol,
        string sortText,
        Node? replacementToken,
        CompletionDataData data,
        int position,
        SourceFile file,
        string name,
        bool needsConvertPropertyAccess,
        SymbolOriginInfo? origin,
        bool useSemicolons,
        CompilerOptions compilerOptions,
        bool isMemberCompletion)
    {
        var contextToken = data._contextToken;
        var insertText = "";
        var filterText = "";
        var replacementSpan = GetReplacementRangeForContextToken(file, replacementToken!, position);
        var isSnippet = false;
        var hasAction = false;
        var source = GetSourceFromOrigin(origin!);
        CompletionItemLabelDetails? labelDetails = null;
        var preferences = UserPreferences();
        var insertQuestionDot = OriginIsNullableMember(origin!);
        var useBraces = OriginIsSymbolMember(origin!) || needsConvertPropertyAccess;
        if (OriginIsThisTypeNode(origin!))
        {
            if (needsConvertPropertyAccess)
            {
                insertText = $"this{IfElse(insertQuestionDot, "?.", "")}[{QuotePropertyName(file, preferences, name)}]";
            }
            else
            {
                insertText = $"this{IfElse(insertQuestionDot, "?.", ".")}{name}";
            }
        }
        else if (data._propertyAccessToConvert is not null && (useBraces || insertQuestionDot))
        {
            // We should only have needsConvertPropertyAccess if there's a property access to convert. But see microsoft/TypeScript#21790.
            // Somehow there was a global with a non-identifier name. Hopefully someone will complain about getting a "foo bar" global completion and provide a repro.
            if (useBraces)
            {
                if (needsConvertPropertyAccess)
                {
                    insertText = $"[{QuotePropertyName(file, preferences, name)}]";
                }
                else
                {
                    insertText = $"[{name}]";
                }
            }
            else
            {
                insertText = name;
            }

            if (insertQuestionDot || data._propertyAccessToConvert.QuestionDotToken() is not null)
            {
                insertText = "?." + insertText;
            }

            var dot = FindChildOfKind(data._propertyAccessToConvert, Kind.DotToken, file);
            if (dot is null)
            {
                dot = FindChildOfKind(data._propertyAccessToConvert, Kind.QuestionDotToken, file);
            }

            if (dot is null)
            {
                return (null, null);
            }

            // If the text after the '.' starts with this name, write over it. Else, add new text.
            int end;
            if (name.StartsWith(data._propertyAccessToConvert.Name()!.Text(), StringComparison.Ordinal))
            {
                end = data._propertyAccessToConvert.End();
            }
            else
            {
                end = dot.End();
            }

            var (lspRange, fidelity) = CreateLspRangeFromBounds(GetStartOfNode(dot, file, false /*includeJSDoc*/), end, file);
            if (!fidelity.IsExact())
            {
                return (null, null);
            }

            replacementSpan = lspRange;
        }

        if (data._jsxInitializer._isInitializer)
        {
            if (insertText == "")
            {
                insertText = name;
            }

            insertText = $"{{{insertText}}}";
            if (data._jsxInitializer._initializer is not null)
            {
                var (lspRange, fidelity) = CreateLspRangeFromNode(data._jsxInitializer._initializer, file);
                if (!fidelity.IsExact())
                {
                    return (null, null);
                }

                replacementSpan = lspRange;
            }
        }

        if (OriginIsPromise(origin!) && data._propertyAccessToConvert is not null)
        {
            if (insertText == "")
            {
                insertText = name;
            }

            var precedingToken = FindPrecedingToken(file, data._propertyAccessToConvert.Pos());
            var awaitText = "";
            if (precedingToken is not null && PositionIsASICandidate(precedingToken.End(), precedingToken.Parent, file))
            {
                awaitText = ";";
            }

            awaitText += "(await " + GetTextOfNode(data._propertyAccessToConvert.Expression()!) + ")";
            if (needsConvertPropertyAccess)
            {
                insertText = awaitText + insertText;
            }
            else
            {
                var dotStr = IfElse(insertQuestionDot, "?.", ".");
                insertText = awaitText + dotStr + insertText;
            }

            var isInAwaitExpression = IsAwaitExpression(data._propertyAccessToConvert.Parent!);
            var wrapNode = IfElse(
                isInAwaitExpression,
                data._propertyAccessToConvert.Parent!,
                data._propertyAccessToConvert.Expression()!);
            var (lspRange, fidelity) = CreateLspRangeFromBounds(
                GetStartOfNode(wrapNode, file, false /*includeJSDoc*/),
                data._propertyAccessToConvert.End(),
                file);
            if (!fidelity.IsExact())
            {
                return (null, null);
            }

            replacementSpan = lspRange;
        }

        if (OriginIsTypeOnlyAlias(origin!))
        {
            hasAction = true;
        }

        // Provide object member completions when missing commas, and insert missing commas.
        // For example:
        //
        //    interface I {
        //        a: string;
        //        b: number
        //     }
        //
        //     const cc: I = { a: "red" | }
        //
        // Completion should add a comma after "red" and provide completions for b
        if (data._completionKind == CompletionKind.ObjectPropertyDeclaration
            && contextToken is not null
            && !NodeHasKind(FindPrecedingTokenEx(file, contextToken.Pos(), contextToken, false /*excludeJSDoc*/), Kind.CommaToken))
        {
            if (IsMethodDeclaration(contextToken.Parent!.Parent!)
                || IsGetAccessorDeclaration(contextToken.Parent!.Parent!)
                || IsSetAccessorDeclaration(contextToken.Parent!.Parent!)
                || IsSpreadAssignment(contextToken.Parent!)
                || GetLastToken(FindAncestor(contextToken.Parent, n => IsPropertyAssignment(n)), file) == contextToken
                || (IsShorthandPropertyAssignment(contextToken.Parent!)
                    && GetLineOfPosition(file, contextToken.End()) != GetLineOfPosition(file, position)))
            {
                source = _completionSourceObjectLiteralMemberWithComma;
                hasAction = true;
            }
        }

        TextEdit?[]? additionalTextEdits = null;
        if (preferences.IncludeCompletionsWithClassMemberSnippets.IsTrue()
            && data._completionKind == CompletionKind.MemberLike
            && IsClassLikeMemberCompletion(symbol, data._location, file))
        {
            var (memberCompletionEntry, err) = GetEntryForMemberCompletion(
                ctx,
                typeChecker,
                symbol,
                name,
                data._location,
                position,
                contextToken!,
                file);
            if (err is not null)
            {
                return (null, err);
            }

            if (memberCompletionEntry is null)
            {
                return (null, null);
            }

            insertText = memberCompletionEntry._insertText;
            filterText = memberCompletionEntry._filterText;
            isSnippet = memberCompletionEntry._isSnippet;
            if (memberCompletionEntry._additionalTextEdits.Length > 0)
            {
                additionalTextEdits = memberCompletionEntry._additionalTextEdits;
                hasAction = true;
                source = _completionSourceClassMemberSnippet;
            }
        }

        if (OriginIsObjectLiteralMethod(origin!))
        {
            insertText = origin!.AsObjectLiteralMethod()._insertText;
            isSnippet = origin!.AsObjectLiteralMethod()._isSnippet;
            labelDetails = origin!.AsObjectLiteralMethod()._labelDetails;
            if (!ClientSupportsItemLabelDetails(ctx))
            {
                name = name + origin!.AsObjectLiteralMethod()._labelDetails!.Detail;
                labelDetails = null;
            }

            source = _completionSourceObjectLiteralMethodSnippet;
            sortText = SortBelow(sortText);
        }

        if (data._isJsxIdentifierExpected
            && !data._isRightOfOpenTag
            && ClientSupportsItemSnippet(ctx)
            && preferences.JsxAttributeCompletionStyle != JsxAttributeCompletionStyleNone
            && !(data._location.Parent is not null
                && IsJsxAttribute(data._location.Parent)
                && data._location.Parent.Initializer() is not null))
        {
            // Go 在内层块里重名声明 `useBraces`（遮蔽外层）⇒ C# 禁止同名（CS0136），改名 `useBracesJsx`（文件头第 14 条）。
            var useBracesJsx = preferences.JsxAttributeCompletionStyle == JsxAttributeCompletionStyleBraces;
            var t = typeChecker.GetTypeOfSymbolAtLocation(symbol, data._location);

            // If is boolean like or undefined, don't return a snippet, we want to return just the completion.
            if (preferences.JsxAttributeCompletionStyle == JsxAttributeCompletionStyleAuto
                && !t.IsBooleanLike()
                && !(t.IsUnion() && Some(t.Types(), x => x.IsBooleanLike())))
            {
                if (t.IsStringLike()
                    || (t.IsUnion()
                        && Every(
                            t.Types(),
                            x => (x.Flags() & (TypeFlags.StringLike | TypeFlags.Undefined)) != 0
                                || IsStringAndEmptyAnonymousObjectIntersection(typeChecker, x))))
                {
                    // If type is string-like or undefined, use quotes.
                    insertText = $"{EscapeSnippetText(name)}={Quote(file, preferences, "$1")}";
                    isSnippet = true;
                }
                else
                {
                    // Use braces for everything else.
                    useBracesJsx = true;
                }
            }

            if (useBracesJsx)
            {
                insertText = EscapeSnippetText(name) + "={$1}";
                isSnippet = true;
            }
        }

        var parentNamedImportOrExport = FindAncestor(data._location, n => IsNamedImportsOrExports(n));
        if (parentNamedImportOrExport is not null)
        {
            if (!IsIdentifierText(name, LanguageVariant.Standard))
            {
                insertText = QuotePropertyName(file, preferences, name);

                if (parentNamedImportOrExport.Kind == Kind.NamedImports)
                {
                    // Check if it is `import { ^here as name } from '...'``.
                    // We have to access the scanner here to check if it is `{ ^here as name }`` or `{ ^here, as, name }`.
                    var scanner = NewScanner();
                    scanner.SetText(file.Text());
                    scanner.ResetPos(position);
                    if (!(scanner.Scan() == Kind.AsKeyword && scanner.Scan() == Kind.Identifier))
                    {
                        insertText += " as " + GenerateIdentifierForArbitraryString(name);
                    }
                }
            }
            else if (parentNamedImportOrExport.Kind == Kind.NamedImports)
            {
                var possibleToken = StringToToken(name);
                if (possibleToken != Kind.Unknown
                    && (possibleToken == Kind.AwaitKeyword || LsUtil.LsUtil.IsNonContextualKeyword(possibleToken)))
                {
                    insertText = $"{name} as {name}_";
                }
            }
        }

        // Commit characters

        var elementKind = GetSymbolKind(typeChecker, symbol, data._location);
        string[]? commitCharacters = null;
        if (ClientSupportsItemCommitCharacters(ctx))
        {
            if (elementKind == ScriptElementKind.Warning || elementKind == ScriptElementKind.String)
            {
                commitCharacters = [];
            }
            else if (!ClientSupportsDefaultCommitCharacters(ctx))
            {
                commitCharacters = data._defaultCommitCharacters;
            }

            // Otherwise use the completion list default.
        }

        var preselect = IsRecommendedCompletionMatch(symbol, data._recommendedCompletion!, typeChecker);
        var kindModifiers = GetSymbolModifiers(typeChecker, symbol);

        return (CreateLSPCompletionItem(
            ctx,
            name,
            insertText,
            filterText,
            sortText,
            elementKind,
            kindModifiers,
            replacementSpan,
            commitCharacters,
            labelDetails,
            file,
            position,
            isMemberCompletion,
            isSnippet,
            hasAction,
            preselect,
            source,
            null, /*autoImportFix*/
            additionalTextEdits,
            null /*detail*/), null);
    }

    /// <summary>Go: <c>func (l *LanguageService) setItemDefaults(ctx, position, file, items, defaultCommitCharacters *[]string, optionalReplacementSpan) *lsproto.CompletionItemDefaults</c>（completions.go:4840-4911）。</summary>
    internal LsProto.CompletionItemDefaults? SetItemDefaults(
        Context ctx,
        int position,
        SourceFile file,
        CompletionItem[] items,
        string[]? defaultCommitCharacters,
        Range? optionalReplacementSpan)
    {
        LsProto.CompletionItemDefaults? itemDefaults = null;
        if (defaultCommitCharacters is not null)
        {
            var supportsItemCommitCharacters = ClientSupportsItemCommitCharacters(ctx);
            if (ClientSupportsDefaultCommitCharacters(ctx) && supportsItemCommitCharacters)
            {
                itemDefaults = new LsProto.CompletionItemDefaults
                {
                    CommitCharacters = defaultCommitCharacters,
                };
            }
            else if (supportsItemCommitCharacters)
            {
                foreach (var item in items)
                {
                    item._completionItem.CommitCharacters ??= defaultCommitCharacters;
                }
            }
        }

        if (optionalReplacementSpan is not null)
        {
            // Ported from vscode ts extension.
            var (end, fidelity) = CreateLspPosition(position, file);
            if (!fidelity.IsExact())
            {
                return itemDefaults;
            }

            var insertRange = new Range
            {
                Start = optionalReplacementSpan.Start,
                End = end,
            };
            if (ClientSupportsDefaultEditRange(ctx))
            {
                itemDefaults = OrElse(itemDefaults, new LsProto.CompletionItemDefaults())!;
                itemDefaults.EditRange = new RangeOrEditRangeWithInsertReplace
                {
                    EditRangeWithInsertReplace = new EditRangeWithInsertReplace
                    {
                        Insert = insertRange,
                        Replace = optionalReplacementSpan,
                    },
                };
                foreach (var item in items)
                {
                    // If `editRange` is set, `insertText` is ignored by the client, so we need to
                    // provide `textEdit` instead.
                    if (item._completionItem.InsertText is not null && item._completionItem.TextEdit is null)
                    {
                        item._completionItem.TextEdit = new TextEditOrInsertReplaceEdit
                        {
                            InsertReplaceEdit = new InsertReplaceEdit
                            {
                                NewText = item._completionItem.InsertText,
                                Insert = insertRange,
                                Replace = optionalReplacementSpan,
                            },
                        };
                        item._completionItem.InsertText = null;
                    }
                }
            }
            else if (ClientSupportsItemInsertReplace(ctx))
            {
                foreach (var item in items)
                {
                    if (item._completionItem.TextEdit is null)
                    {
                        item._completionItem.TextEdit = new TextEditOrInsertReplaceEdit
                        {
                            InsertReplaceEdit = new InsertReplaceEdit
                            {
                                NewText = OrElse(item._completionItem.InsertText, item._completionItem.Label)!,
                                Insert = insertRange,
                                Replace = optionalReplacementSpan,
                            },
                        };
                    }
                }
            }
        }

        return itemDefaults;
    }

    /// <summary>Go: <c>func (l *LanguageService) specificKeywordCompletionInfo(ctx, position, file, items, isNewIdentifierLocation, optionalReplacementSpan) *CompletionList</c>（completions.go:4913-4935）。</summary>
    internal CompletionList SpecificKeywordCompletionInfo(
        Context ctx,
        int position,
        SourceFile file,
        CompletionItem[] items,
        bool isNewIdentifierLocation,
        Range? optionalReplacementSpan)
    {
        var defaultCommitCharacters = GetDefaultCommitCharacters(isNewIdentifierLocation);
        var itemDefaults = SetItemDefaults(
            ctx,
            position,
            file,
            items,
            defaultCommitCharacters,
            optionalReplacementSpan);
        return new CompletionList
        {
            IsIncomplete = false,
            ItemDefaults = itemDefaults,
            Items = items,
        };
    }

    /// <summary>Go: <c>func (l *LanguageService) getJsxClosingTagCompletion(ctx, location, file, position) *CompletionList</c>（completions.go:4937-5021）。</summary>
    internal CompletionList? GetJsxClosingTagCompletion(Context ctx, Node location, SourceFile file, int position)
    {
        // We wanna walk up the tree till we find a JSX closing element.
        var jsxClosingElement = FindAncestorOrQuit(location, node =>
        {
            switch (node.Kind)
            {
                case Kind.JsxClosingElement:
                    return FindAncestorResult.True;
                case Kind.LessThanSlashToken:
                case Kind.GreaterThanToken:
                case Kind.Identifier:
                case Kind.PropertyAccessExpression:
                    return FindAncestorResult.False;
                default:
                    return FindAncestorResult.Quit;
            }
        });

        if (jsxClosingElement is null)
        {
            return null;
        }

        // In the TypeScript JSX element, if such element is not defined. When users query for completion at closing tag,
        // instead of simply giving unknown value, the completion will return the tag-name of an associated opening-element.
        // For example:
        //     var x = <div> </ /*1*/
        // The completion list at "1" will contain "div>" with type any
        // And at `<div> </ /*1*/ >` (with a closing `>`), the completion list will contain "div".
        // And at property access expressions `<MainComponent.Child> </MainComponent. /*1*/ >` the completion will
        // return full closing tag with an optional replacement span
        // For example:
        //     var x = <MainComponent.Child> </     MainComponent /*1*/  >
        //     var y = <MainComponent.Child> </   /*2*/   MainComponent >
        // the completion list at "1" and "2" will contain "MainComponent.Child" with a replacement span of closing tag name
        var hasClosingAngleBracket = FindChildOfKind(jsxClosingElement, Kind.GreaterThanToken, file) is not null;
        var tagName = jsxClosingElement.Parent!.AsJsxElement()!.OpeningElement!.TagName()!;
        var closingTag = GetTextOfNode(tagName);
        var fullClosingTag = closingTag + IfElse(hasClosingAngleBracket, "", ">");
        var (optionalReplacementSpan, fidelity) = CreateLspRangeFromNode(jsxClosingElement.TagName()!, file);
        if (!fidelity.IsExact())
        {
            return null;
        }

        var defaultCommitCharacters = GetDefaultCommitCharacters(false /*isNewIdentifierLocation*/);

        var lspItem = CreateLSPCompletionItem(
            ctx,
            fullClosingTag, /*name*/
            "", /*insertText*/
            "", /*filterText*/
            SortTextLocationPriority,
            ScriptElementKind.ClassElement,
            ScriptElementKindModifier.None, /*kindModifiers*/
            null, /*replacementSpan*/
            null, /*commitCharacters*/
            null, /*labelDetails*/
            file,
            position,
            true, /*isMemberCompletion*/
            false, /*isSnippet*/
            false, /*hasAction*/
            false, /*preselect*/
            "", /*source*/
            null, /*autoImportEntryData*/ // !!! jsx autoimports
            null, /*additionalTextEdits*/
            null /*detail*/);
        var item = new CompletionItem
        {
            _completionItem = lspItem,
        };
        var items = new[] { item };
        var itemDefaults = SetItemDefaults(
            ctx,
            position,
            file,
            items,
            defaultCommitCharacters,
            optionalReplacementSpan);

        return new CompletionList
        {
            IsIncomplete = false,
            ItemDefaults = itemDefaults,
            Items = items,
        };
    }

    /// <summary>Go: <c>func (l *LanguageService) createLSPCompletionItem(ctx, name, insertText, filterText, sortText, elementKind, kindModifiers, replacementSpan, commitCharacters, labelDetails, file, position, isMemberCompletion, isSnippet, hasAction, preselect, source, autoImportFix, additionalTextEdits, detail) *lsproto.CompletionItem</c>（completions.go:5023-5117）。</summary>
    internal LsProto.CompletionItem CreateLSPCompletionItem(
        Context ctx,
        string name,
        string insertText,
        string filterText,
        string sortText,
        ScriptElementKind elementKind,
        ScriptElementKindModifier kindModifiers,
        Range? replacementSpan,
        string[]? commitCharacters,
        CompletionItemLabelDetails? labelDetails,
        SourceFile file,
        int position,
        bool isMemberCompletion,
        bool isSnippet,
        bool hasAction,
        bool preselect,
        string source,
        AutoImportFix? autoImportFix,
        TextEdit?[]? additionalTextEdits,
        string? detail)
    {
        var kind = GetCompletionsSymbolKind(elementKind);
        var data = new CompletionItemData
        {
            FileName = file.OriginalFileName(),
            Position = position,
            SupplementalFileIndex = SupplementalFileIndex(file),
            Source = source,
            Name = name,
            AutoImport = autoImportFix,
        };

        // Text edit
        TextEditOrInsertReplaceEdit? textEdit = null;
        if (replacementSpan is not null)
        {
            textEdit = new TextEditOrInsertReplaceEdit
            {
                TextEdit = new TextEdit
                {
                    NewText = IfElse(insertText == "", name, insertText),
                    Range = replacementSpan,
                },
            };
        }

        // Filter text

        // Ported from vscode ts extension.
        var (wordSize, wordStart) = GetWordLengthAndStart(file, position);
        var dotAccessor = GetDotAccessor(file, position - wordSize);
        if (filterText == "")
        {
            filterText = GetFilterText(file, position, insertText, name, wordStart, dotAccessor);
        }

        // Adjustements based on kind modifiers.
        CompletionItemTag[]? tags = null;

        // Copied from vscode ts extension: `MyCompletionItem.constructor`.
        if (isMemberCompletion && (kindModifiers & ScriptElementKindModifier.Optional) != 0)
        {
            if (insertText == "")
            {
                insertText = name;
            }

            if (filterText == "" || isSnippet)
            {
                filterText = name;
            }

            name = name + "?";
        }

        if ((kindModifiers & ScriptElementKindModifier.Deprecated) != 0)
        {
            tags = [CompletionItemTag.Deprecated];
        }

        if (hasAction && source != "")
        {
            // !!! adjust label like vscode does
        }

        // Client assumes plain text by default.
        InsertTextFormat? insertTextFormat = null;
        if (isSnippet)
        {
            insertTextFormat = InsertTextFormat.Snippet;
        }

        return new LsProto.CompletionItem
        {
            Label = name,
            LabelDetails = labelDetails,
            Kind = kind,
            Tags = tags,
            Detail = detail,
            Preselect = BoolToPtr(preselect),
            SortText = sortText,
            FilterText = StrPtrTo(filterText),
            InsertText = StrPtrTo(insertText),
            InsertTextFormat = insertTextFormat,
            TextEdit = textEdit,
            CommitCharacters = commitCharacters,
            AdditionalTextEdits = additionalTextEdits,
            Data = data,
        };
    }

    /// <summary>Go: <c>func (l *LanguageService) getLabelCompletionsAtPosition(ctx, node, file, position, optionalReplacementSpan) *CompletionList</c>（completions.go:5119-5144）。</summary>
    internal CompletionList? GetLabelCompletionsAtPosition(
        Context ctx,
        Node node,
        SourceFile file,
        int position,
        Range? optionalReplacementSpan)
    {
        var items = GetLabelStatementCompletions(ctx, node, file, position);
        if (items.Length == 0)
        {
            return null;
        }

        var defaultCommitCharacters = GetDefaultCommitCharacters(false /*isNewIdentifierLocation*/);
        var itemDefaults = SetItemDefaults(
            ctx,
            position,
            file,
            items,
            defaultCommitCharacters,
            optionalReplacementSpan);
        return new CompletionList
        {
            IsIncomplete = false,
            ItemDefaults = itemDefaults,
            Items = items,
        };
    }

    /// <summary>Go: <c>func (l *LanguageService) getLabelStatementCompletions(ctx, node, file, position) []*CompletionItem</c>（completions.go:5146-5193）。</summary>
    internal CompletionItem[] GetLabelStatementCompletions(
        Context ctx,
        Node node,
        SourceFile file,
        int position)
    {
        // Go 的 `var uniques collections.Set[string]` 是零值可用的结构体 ⇒ C# 必须 new（文件头第 10 条）。
        var uniques = new Set<string>();
        var items = new List<CompletionItem>();
        var current = node;
        while (current is not null)
        {
            if (IsFunctionLike(current))
            {
                break;
            }

            if (IsLabeledStatement(current))
            {
                var name = current.Label()!.Text();
                if (!uniques.Has(name))
                {
                    uniques.Add(name);
                    var lspItem = CreateLSPCompletionItem(
                        ctx,
                        name,
                        "", /*insertText*/
                        "", /*filterText*/
                        SortTextLocationPriority,
                        ScriptElementKind.Label,
                        ScriptElementKindModifier.None, /*kindModifiers*/
                        null, /*replacementSpan*/
                        null, /*commitCharacters*/
                        null, /*labelDetails*/
                        file,
                        position,
                        false, /*isMemberCompletion*/
                        false, /*isSnippet*/
                        false, /*hasAction*/
                        false, /*preselect*/
                        "", /*source*/
                        null, /*autoImportEntryData*/
                        null, /*additionalTextEdits*/
                        null /*detail*/);
                    items.Add(new CompletionItem
                    {
                        _completionItem = lspItem,
                    });
                }
            }

            current = current.Parent;
        }

        return items.ToArray();
    }

    /// <summary>Go: <c>func (l *LanguageService) ResolveCompletionItem(ctx, item *lsproto.CompletionItem, data *lsproto.CompletionItemData) (*lsproto.CompletionItem, error)</c>（completions.go:5489-5510）。</summary>
    internal (LsProto.CompletionItem? Value0, Exception? Error) ResolveCompletionItem(
        Context ctx,
        LsProto.CompletionItem item,
        CompletionItemData data)
    {
        if (data is null)
        {
            return (null, new Exception("completion item data is nil"));
        }

        var (program, file) = TryGetProgramAndFile(data.FileName);
        if (file is null)
        {
            return (null, new Exception($"file not found: {data.FileName}"));
        }

        file = SourceFileForSupplementalFileIndex(file, data.SupplementalFileIndex);
        if (file is null)
        {
            return (null, new Exception($"supplemental source file index not found: {data.SupplementalFileIndex}"));
        }

        var (checker, done) = program!.GetTypeCheckerForFile(ctx, file);
        try
        {
            return (GetCompletionItemDetails(ctx, program!, checker, data.Position, file, item, data), null);
        }
        finally
        {
            done();
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) getCompletionItemDetails(ctx, program, checker, position, file, item, data) *lsproto.CompletionItem</c>（completions.go:5516-5607）。</summary>
    internal LsProto.CompletionItem? GetCompletionItemDetails(
        Context ctx,
        Program program,
        CheckerType checker,
        int position,
        SourceFile file,
        LsProto.CompletionItem item,
        CompletionItemData data)
    {
        var docFormat = GetCompletionDocumentationFormat(ctx);
        var (contextToken, previousToken) = GetRelevantTokens(position, file);
        if (IsInString(file, position, previousToken))
        {
            return GetStringLiteralCompletionDetails(
                ctx,
                checker,
                item,
                data.Name,
                file,
                position,
                contextToken,
                docFormat);
        }

        if (data.AutoImport is not null)
        {
            if (data.IsImportStatementCompletion)
            {
                return item;
            }

            // Auto-imports in content-mapped files are evaluated eagerly so edits outside
            // of verbatim spans can cause the completion item to be filtered out entirely.
            // Only real files take this code path, so the final Edits() is guaranteed ok.
            var (edits, description, _) = new Fix { AutoImportFix = data.AutoImport }.Edits(ctx, file, program.Options(), FormatOptions(), _converters!, UserPreferences());
            item.AdditionalTextEdits = edits;
            item.Detail = StrPtrTo(description);
            return item;
        }

        // Compute all the completion symbols again.
        var symbolCompletion = GetSymbolCompletionFromItemData(
            ctx,
            checker,
            file,
            position,
            data);
        var preferences = UserPreferences();

        if (symbolCompletion._request is not null)
        {
            switch (symbolCompletion._request)
            {
                case CompletionDataJsDocTagName:
                case CompletionDataJsDocTag:
                case CompletionDataJsDocParameterName:
                    return CreateSimpleDetails(item, data.Name, docFormat);
                case CompletionDataKeyword request:
                    if (Some(request._keywordCompletions, c => c._completionItem.Label == data.Name))
                    {
                        return CreateSimpleDetails(item, data.Name, docFormat);
                    }

                    return item;
                default:
                    throw new Exception($"Unexpected completion data type: {symbolCompletion._request.GetType().Name}");
            }
        }
        else if (symbolCompletion._symbol is not null)
        {
            var symbolDetails = symbolCompletion._symbol;
            return CreateCompletionDetailsForSymbol(
                item,
                symbolDetails._symbol,
                checker,
                symbolDetails._location,
                position,
                docFormat);
        }
        else if (symbolCompletion._literal is not null)
        {
            var literal = symbolCompletion._literal;
            return CreateSimpleDetails(item, CompletionNameForLiteral(file, preferences, literal), docFormat);
        }
        else if (symbolCompletion._cases is not null)
        {
            return item;
        }
        else
        {
            // Didn't find a symbol with this name.  See if we can find a keyword instead.
            if (Some(AllKeywordCompletions.Value, c => c.Label == data.Name))
            {
                return CreateSimpleDetails(item, data.Name, docFormat);
            }

            return item;
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) getSymbolCompletionFromItemData(ctx, ch, file, position, itemData) detailsData</c>（completions.go:5626-5696）。</summary>
    internal DetailsData GetSymbolCompletionFromItemData(
        Context ctx,
        CheckerType ch,
        SourceFile file,
        int position,
        CompletionItemData itemData)
    {
        if (itemData.Source == SourceSwitchCases)
        {
            return new DetailsData
            {
                _cases = new object(),
            };
        }

        var (completionData, err) = GetCompletionDataWorker(ctx, ch, file, position, UserPreferences(), true /*forItemResolve*/);
        if (err is not null)
        {
            // Go: panic(err)
            Debug.Fail("getCompletionData failed: " + err.Message);
            throw new System.Diagnostics.UnreachableException();
        }

        if (completionData is null)
        {
            return new DetailsData();
        }

        if (completionData is not CompletionDataData data)
        {
            return new DetailsData
            {
                _request = completionData,
            };
        }

        var preferences = UserPreferences();
        object? literal = null;
        foreach (var candidate in data._literals)
        {
            if (CompletionNameForLiteral(file, preferences, candidate) == itemData.Name)
            {
                literal = candidate;
                break;
            }
        }

        if (literal is not null)
        {
            return new DetailsData
            {
                _literal = literal,
            };
        }

        // Find the symbol with the matching entry name.
        // We don't need to perform character checks here because we're only comparing the
        // name against 'entryName' (which is known to be good), not building a new
        // completion entry.
        for (var index = 0; index < data._symbols.Length; index++)
        {
            var symbol = data._symbols[index];
            var origin = data._symbolToOriginInfoMap.GetValueOrDefault(index);
            var (displayName, _) = GetCompletionEntryDisplayNameForSymbol(file, preferences, symbol, origin!, data._completionKind, data._isJsxIdentifierExpected);
            if (displayName == itemData.Name
                && ((itemData.Source == _completionSourceClassMemberSnippet && (symbol.Flags & SymbolFlags.ClassMember) != 0)
                    || (itemData.Source == _completionSourceObjectLiteralMethodSnippet && (symbol.Flags & (SymbolFlags.Property | SymbolFlags.Method)) != 0)
                    || GetSourceFromOrigin(origin!) == itemData.Source
                    || itemData.Source == _completionSourceObjectLiteralMemberWithComma))
            {
                return new DetailsData
                {
                    _symbol = new SymbolDetails
                    {
                        _symbol = symbol,
                        _location = data._location,
                        _origin = origin,
                        _previousToken = data._previousToken,
                        _contextToken = data._contextToken,
                        _jsxInitializer = data._jsxInitializer,
                        _isTypeOnlyLocation = data._isTypeOnlyLocation,
                    },
                };
            }
        }

        return new DetailsData();
    }

    /// <summary>Go: <c>func (l *LanguageService) createCompletionDetailsForSymbol(item, symbol, checker, location, position, docFormat) *lsproto.CompletionItem</c>（completions.go:5734-5744）。</summary>
    internal LsProto.CompletionItem CreateCompletionDetailsForSymbol(
        LsProto.CompletionItem item,
        Ast.Symbol symbol,
        CheckerType checker,
        Node location,
        int position,
        MarkupKind docFormat)
    {
        var (quickInfo, documentation, _, _) = GetQuickInfoAndDocumentationForSymbol(
            checker,
            symbol,
            location,
            docFormat,
            null!,
            false /*vsCapability*/);
        return CreateCompletionDetails(item, quickInfo, documentation, docFormat);
    }

    /// <summary>Go: <c>func (l *LanguageService) getImportStatementCompletionInfo(contextToken, sourceFile) importStatementCompletionInfo</c>（completions.go:5746-5819）。</summary>
    internal ImportStatementCompletionInfo GetImportStatementCompletionInfo(Node contextToken, SourceFile sourceFile)
    {
        var result = new ImportStatementCompletionInfo();
        Node? candidate = null;
        var parent = contextToken.Parent!;
        if (IsImportEqualsDeclaration(parent))
        {
            // import Foo |
            // import Foo f|
            var lastToken = GetLastToken(parent, sourceFile);
            if (contextToken.Kind == Kind.Identifier && lastToken != contextToken)
            {
                result._keywordCompletion = Kind.FromKeyword;
                result._isKeywordOnlyCompletion = true;
            }
            else
            {
                if (contextToken.Kind != Kind.TypeKeyword)
                {
                    result._keywordCompletion = Kind.TypeKeyword;
                }

                if (IsModuleSpecifierMissingOrEmpty(parent.AsImportEqualsDeclaration()!.ModuleReference!))
                {
                    candidate = parent;
                }
            }
        }
        else if (CouldBeTypeOnlyImportSpecifier(parent, contextToken) && CanCompleteFromNamedBindings(parent.Parent!))
        {
            candidate = parent;
        }
        else if (IsNamedImports(parent) || IsNamespaceImport(parent))
        {
            if (!parent.Parent!.IsTypeOnly()
                && (contextToken.Kind == Kind.OpenBraceToken
                    || contextToken.Kind == Kind.ImportKeyword
                    || contextToken.Kind == Kind.CommaToken))
            {
                result._keywordCompletion = Kind.TypeKeyword;
            }

            if (CanCompleteFromNamedBindings(parent))
            {
                // At `import { ... } |` or `import * as Foo |`, the only possible completion is `from`
                if (contextToken.Kind == Kind.CloseBraceToken || contextToken.Kind == Kind.Identifier)
                {
                    result._isKeywordOnlyCompletion = true;
                    result._keywordCompletion = Kind.FromKeyword;
                }
                else
                {
                    candidate = parent.Parent.Parent;
                }
            }
        }
        else if ((IsExportDeclaration(parent) && contextToken.Kind == Kind.AsteriskToken)
            || (IsNamedExports(parent) && contextToken.Kind == Kind.CloseBraceToken))
        {
            result._isKeywordOnlyCompletion = true;
            result._keywordCompletion = Kind.FromKeyword;
        }
        else if (contextToken.Kind == Kind.ImportKeyword)
        {
            if (IsSourceFile(parent))
            {
                // A lone import keyword with nothing following it does not parse as a statement at all
                result._keywordCompletion = Kind.TypeKeyword;
                candidate = contextToken;
            }
            else if (IsImportDeclaration(parent))
            {
                // `import s| from`
                result._keywordCompletion = Kind.TypeKeyword;
                if (IsModuleSpecifierMissingOrEmpty(parent.ModuleSpecifier()!))
                {
                    candidate = parent;
                }
            }
        }

        if (candidate is not null)
        {
            result._isNewIdentifierLocation = true;
            result._replacementSpan = GetSingleLineReplacementSpanForImportCompletionNode(candidate);
            result._couldBeTypeOnlyImportSpecifier = CouldBeTypeOnlyImportSpecifier(candidate, contextToken);
            if (IsImportDeclaration(candidate))
            {
                var importClause = candidate.ImportClause();
                if (importClause is not null)
                {
                    result._isTopLevelTypeOnly = importClause.IsTypeOnly();
                }
            }
            else if (candidate.Kind == Kind.ImportEqualsDeclaration)
            {
                result._isTopLevelTypeOnly = candidate.IsTypeOnly();
            }
        }
        else
        {
            result._isNewIdentifierLocation = result._keywordCompletion == Kind.TypeKeyword;
        }

        return result;
    }

    /// <summary>Go: <c>func (l *LanguageService) getSingleLineReplacementSpanForImportCompletionNode(node) *lsproto.Range</c>（completions.go:5821-5877）。</summary>
    internal Range? GetSingleLineReplacementSpanForImportCompletionNode(Node node)
    {
        // node is ImportDeclaration | ImportEqualsDeclaration | ImportSpecifier | JSDocImportTag | Token<SyntaxKind.ImportKeyword>
        var ancestor = FindAncestor(node, n => IsImportDeclaration(n) || IsImportEqualsDeclaration(n) || IsJSDocImportTag(n));
        if (ancestor is not null)
        {
            node = ancestor;
        }

        var sourceFile = GetSourceFileOfNode(node)!;
        // Use token position (excluding JSDoc/trivia) instead of node.Pos() to avoid including JSDoc comments
        var tokenPos = GetTokenPosOfNode(node, sourceFile, false /*includeJSDoc*/);
        if (GetLinesBetweenPositions(sourceFile, tokenPos, node.End()) == 0)
        {
            var (lspRange, fidelity) = CreateLspRangeFromNode(node, sourceFile);
            if (!fidelity.IsExact())
            {
                return null;
            }

            return lspRange;
        }

        if (node.Kind == Kind.ImportKeyword || node.Kind == Kind.ImportSpecifier)
        {
            // Go: panic("ImportKeyword was necessarily on one line; ImportSpecifier was necessarily parented in an ImportDeclaration")
            Debug.Fail("ImportKeyword was necessarily on one line; ImportSpecifier was necessarily parented in an ImportDeclaration");
            throw new System.Diagnostics.UnreachableException();
        }

        // Guess which point in the import might actually be a later statement parsed as part of the import
        // during parser recovery - either in the middle of named imports, or the module specifier.
        Node? potentialSplitPoint;
        if (node.Kind == Kind.ImportDeclaration || node.Kind == Kind.JSDocImportTag)
        {
            Node? specifier = null;
            var importClause = node.ImportClause();
            if (importClause is not null)
            {
                specifier = GetPotentiallyInvalidImportSpecifier(importClause.AsImportClause()!.NamedBindings);
            }

            if (specifier is not null)
            {
                potentialSplitPoint = specifier;
            }
            else
            {
                potentialSplitPoint = node.ModuleSpecifier();
            }
        }
        else
        {
            potentialSplitPoint = node.AsImportEqualsDeclaration()!.ModuleReference;
        }

        var withoutModuleSpecifier = NewTextRange(
            GetTokenPosOfNode(GetFirstToken(node, sourceFile)!, sourceFile, false),
            potentialSplitPoint!.Pos());
        // The module specifier/reference was previously found to be missing, empty, or
        // not a string literal - in this last case, it's likely that statement on a following
        // line was parsed as the module specifier of a partially-typed import, e.g.
        //   import Foo|
        //   interface Blah {}
        // This appears to be a multiline-import, and editors can't replace multiple lines.
        // But if everything but the "module specifier" is on one line, by this point we can
        // assume that the "module specifier" is actually just another statement, and return
        // the single-line range of the import excluding that probable statement.
        if (GetLinesBetweenPositions(sourceFile, withoutModuleSpecifier.Pos(), withoutModuleSpecifier.End()) == 0)
        {
            var (lspRange, fidelity) = CreateLspRangeFromBounds(withoutModuleSpecifier.Pos(), withoutModuleSpecifier.End(), sourceFile);
            if (!fidelity.IsExact())
            {
                return null;
            }

            return lspRange;
        }

        return null;
    }

    /// <summary>Go: <c>func (l *LanguageService) jsDocCompletionInfo(ctx, position, file, items) *CompletionList</c>（completions.go:5983-6003）。</summary>
    internal CompletionList JsDocCompletionInfo(
        Context ctx,
        int position,
        SourceFile file,
        CompletionItem[] items)
    {
        var defaultCommitCharacters = GetDefaultCommitCharacters(false /*isNewIdentifierLocation*/);
        var itemDefaults = SetItemDefaults(
            ctx,
            position,
            file,
            items,
            defaultCommitCharacters,
            null /*optionalReplacementSpan*/);
        return new CompletionList
        {
            IsIncomplete = false,
            ItemDefaults = itemDefaults,
            Items = items,
        };
    }

    /// <summary>Go: <c>func (l *LanguageService) getExhaustiveCaseSnippets(ctx, caseBlock, file, position, options, program, c) (*lsproto.CompletionItem, error)</c>（completions.go:6568-6704）。</summary>
    internal (LsProto.CompletionItem? Value0, Exception? Error) GetExhaustiveCaseSnippets(
        Context ctx,
        CaseBlock caseBlock,
        SourceFile file,
        int position,
        CompilerOptions options,
        Program program,
        CheckerType c)
    {
        var clauses = caseBlock.Clauses!.Nodes;
        var switchType = c.GetTypeAtLocation(caseBlock.AsNode().Parent!.Expression()!);
        if (switchType is not null && switchType.IsUnion() && Every(switchType.Types(), t => Ls.IsLiteral(t)))
        {
            // Collect constant values in existing clauses.
            var tracker = NewCaseClauseTracker(c, clauses);
            var target = options.GetEmitScriptTarget();
            var quotePreference = GetQuotePreference(file, UserPreferences());
            // Tolerate a nil import adder in untitled files.
            ImportAdder? importAdder = null;
            if (!TypeScript.TSPath.TSPath.IsDynamicFileName(file.FileName()))
            {
                var (view, viewError) = GetPreparedAutoImportView(file, c);
                if (viewError is not null)
                {
                    return (null, viewError);
                }

                if (view is not null)
                {
                    importAdder = AutoImport.NewImportAdder(
                        ctx,
                        program,
                        c,
                        file,
                        view,
                        FormatOptions(),
                        _converters!,
                        UserPreferences());
                }
            }

            var elements = new List<Node>();
            var factory = NewNodeFactory(new NodeFactoryHooks());
            foreach (var t in switchType.Types())
            {
                // Enums
                if (t.IsEnumLiteral())
                {
                    Debug.Assert(t.Symbol() is not null, "An enum member type should have a symbol");
                    Debug.Assert(t.Symbol()!.Parent is not null, "An enum member type should have a parent symbol (the enum symbol)");
                    // Filter existing enums by their values
                    object? enumValue = null;
                    if (t.Symbol()!.ValueDeclaration is not null)
                    {
                        enumValue = c.GetConstantValue(t.Symbol()!.ValueDeclaration!);
                    }

                    if (enumValue is not null)
                    {
                        if (tracker.HasValue(enumValue))
                        {
                            continue;
                        }

                        tracker.AddValue(enumValue);
                    }

                    var typeNode = AutoImport.TypeToAutoImportableTypeNode(c, importAdder!, t, caseBlock.AsNode());
                    if (typeNode is null)
                    {
                        return (null, null);
                    }

                    var expr = TypeNodeToExpression(typeNode, target, quotePreference, factory);
                    if (expr is null)
                    {
                        return (null, null);
                    }

                    elements.Add(expr);
                }
                else if (!tracker.HasValue(t.AsLiteralType()!.Value()!)) // Literals
                {
                    switch (t.AsLiteralType()!.Value())
                    {
                        case PseudoBigInt bigIntValue:
                        {
                            Node bigInt;
                            if (bigIntValue.Negative)
                            {
                                // Go: v.Negative = false 改的是值拷贝，等价于取无符号的 base10 文本（文件头第 7 条）。
                                bigInt = factory.NewPrefixUnaryExpression(
                                    Kind.MinusToken,
                                    factory.NewBigIntLiteral(bigIntValue.Base10Value + "n", TokenFlags.None));
                            }
                            else
                            {
                                bigInt = factory.NewBigIntLiteral(bigIntValue.String() + "n", TokenFlags.None);
                            }

                            elements.Add(bigInt);
                            break;
                        }

                        case Number numberValue:
                        {
                            Node number;
                            if (numberValue < 0)
                            {
                                number = factory.NewPrefixUnaryExpression(
                                    Kind.MinusToken,
                                    factory.NewNumericLiteral(numberValue.Abs().String(), TokenFlags.None));
                            }
                            else
                            {
                                number = factory.NewNumericLiteral(numberValue.String(), TokenFlags.None);
                            }

                            elements.Add(number);
                            break;
                        }

                        case string stringValue:
                        {
                            var literal = factory.NewStringLiteral(
                                stringValue,
                                quotePreference == QuotePreferenceSingle ? TokenFlags.SingleQuote : TokenFlags.None);
                            elements.Add(literal);
                            break;
                        }
                    }
                }
            }

            if (elements.Count == 0)
            {
                return (null, null);
            }

            var newClauses = new Node[elements.Count];
            for (var i = 0; i < elements.Count; i++)
            {
                newClauses[i] = factory.NewCaseOrDefaultClause(Kind.CaseClause, elements[i], factory.NewNodeList(null));
            }

            var newLineChar = FormatOptions().NewLineCharacter;
            var snippetPrinter = CreateSnippetPrinter(new PrinterOptions
            {
                RemoveComments = true,
                NewLine = GetNewLineKind(newLineChar),
            }, null /*emitContext*/);
            string PrintNode(Node node) => snippetPrinter.PrintAndFormatNode(ctx, node, file);
            var insertText = string.Join(newLineChar, MapIndex(newClauses, (clause, i) =>
            {
                if (ClientSupportsItemSnippet(ctx))
                {
                    return $"{PrintNode(clause)}${i + 1}";
                }

                return snippetPrinter.PrintUnescapedNode(clause);
            }));

            var firstClause = snippetPrinter.PrintUnescapedNode(newClauses[0]);
            var name = firstClause + " ...";

            TextEdit?[]? additionalTextEdits = null;
            if (importAdder is not null)
            {
                var edits = importAdder.Edits();
                if (edits.Length != 0)
                {
                    additionalTextEdits = edits;
                }
            }

            return (new LsProto.CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Snippet,
                SortText = SortTextGlobalsOrKeywords,
                InsertText = StrPtrTo(insertText),
                AdditionalTextEdits = additionalTextEdits,
                InsertTextFormat = ClientSupportsItemSnippet(ctx) ? InsertTextFormat.Snippet : (InsertTextFormat?)null,
                Data = new CompletionItemData
                {
                    FileName = file.OriginalFileName(),
                    Position = position,
                    SupplementalFileIndex = SupplementalFileIndex(file),
                    Name = name,
                    Source = SourceSwitchCases,
                },
            }, null);
        }

        return (null, null);
    }
}

// ported from: internal/ls/utilities.go:1-1411
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级函数 → `public static partial class Ls` 的成员（同 Constants.cs / Diagnostics.cs）：Go 导出的
//    （`IsInString` / `RangeContainsRange`）→ `public static` + PascalCase 原名；Go 未导出的 → `internal static` + PascalCase。
//    **方法一律不加 `_` 前缀**：.editorconfig 的 `members_should_be_pascal_case` 对 method/property/event 强制 PascalCase
//    （docs/architecture.md §2，IDE1006 在 TreatWarningsAsErrors 下是错误）。只有 Go 未导出的*字段*（internal 亦然）才带 `_`
//    （见 `_quoteReplacer` / `_typeKeywords`）。
// 2) 多返回值 → C# 元组：`(lsproto.Range, spanmap.Fidelity)` → `(Range Range, Fidelity Fidelity)`；`(T, bool)` → `(T, bool)`。
// 3) `*ast.Node` 形参/返回值 → `Node?`（保留 Go 的 nil 语义）；`[]*T` 可为 nil → `T[]?`。生成物里的
//    `Ast.IsXxx(Node node)` 是**非空**形参 ⇒ 传可空值时写 `!`（等价 Go 里"断言非 nil 后解引用"）。
// 4) `node.AsXxx()` 在 C# 返回可空（`this as Xxx`）⇒ 按 Go 类型断言成功的语义补 `!`（如 `parent.AsBinaryExpression()!`）。
// 5) `core.TextPos(x)` 由 `Core.TextPos` 的隐式转换承接（Core/Text.cs:34,36），所以直接传 int。
// 6) Go 的 `debug.Assert(cond, msg)` → `Debug.Assert(cond, msg)`（`TypeScript.Debug`；禁 `System.Diagnostics`）。
// 7) `&ast.NodeFactory{}`（Go 的零值字面量接收者）→ `new NodeFactory()`（先例 Format/Indent.cs:186）。
// 8) Go 的 `strings.LastIndex/LastIndexByte` 是**字节**偏移，C# 的 `string.LastIndexOf` 是 **UTF-16** 偏移；
//    本文件把它们的结果当 `core.TextPos` 用（Go 里本就是 ASCII 假设下的快速路径）⇒ C# 侧直接拿到 UTF-16 偏移，
//    反而与 `FindPrecedingToken` 的期望一致。这是有意的对齐，不是行为回归。
// 9) `iter.Seq[ast.CommentRange]` → `IEnumerable<CommentRange>?`（Go 的 nil 序列 = C# 的 null）。
// 10) `collections.Set[T]{}` 的 Go 零值 → `new Set<T>()`（C# 必须显式构造；空集合语义相同）。
// 11) caseClauseTracker：Go 的接口 + 类型别名 `trackerAddValue/trackerHasValue = any` + `panic("%T")` → C# 的
//     `ICaseClauseTracker` + `object` 参数 + `UnsupportedType`（`Debug.Fail` + throw）——语义一致。
// 12) **跨文件 helper 的就地落地**：下面 4 个 Go 里不在本文件的 helper 被 utilities.go 直接引用，且是后续切片
//     （G4/G6/G8）也要用的共享 helper；为满足"G1 是唯一前置、后 9 组不得重复定义（CS0111）"，G1 统一把它们落在
//     本文件并在此备案，**对应 Go 文件的移植者请勿重复声明**：
//       * `GetRangeOfEnclosingComment` ← format.go:257-300（G4 的 Format.cs 只落其余成员）
//       * `GetSwitchedType` / `IsEqualityOperatorKind` ← completions.go:3557-3565（G8 的 Completions*.cs 只落其余成员）
//       * `GetRangeOfNode` ← findallreferences.go:333-352（G6 的 FindAllReferences.cs 只落其余成员）
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.JsNum;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

public static partial class Ls
{
    // Go: var quoteReplacer = strings.NewReplacer("'", `\'`, `\"`, `"`)
    // Go 的 Replacer 一次遍历替换全部模式；这里的两次 Replace 顺序安全（第一趟产出的 `\'` 不含第二趟的模式 `\"`）。
    internal static readonly Func<string, string> _quoteReplacer =
        s => s.Replace("'", "\\'").Replace("\\\"", "\"");

    public static bool IsInString(SourceFile sourceFile, int position, Node? previousToken)
    {
        if (previousToken is not null && IsStringTextContainingNode(previousToken))
        {
            var start = GetStartOfNode(previousToken, sourceFile, false /*includeJSDoc*/);
            var end = previousToken.End();

            // To be "in" one of these literals, the position has to be:
            //   1. entirely within the token text.
            //   2. at the end position of an unterminated token.
            //   3. at the end of a regular expression (due to trailing flags like '/foo/g').
            if (start < position && position < end)
            {
                return true;
            }

            if (position == end)
            {
                return IsUnterminatedLiteral(previousToken);
            }
        }

        return false;
    }

    internal static bool IsModuleSpecifierLike(Node? node)
    {
        if (!IsStringLiteralLike(node))
        {
            return false;
        }

        var parent = node!.Parent!;
        if (IsRequireCall(parent, false /*requireStringLiteralLikeArgument*/) || IsImportCall(parent))
        {
            return parent.Arguments()![0] == node;
        }

        return parent.Kind == Kind.ExternalModuleReference ||
            parent.Kind == Kind.ImportDeclaration ||
            parent.Kind == Kind.JSImportDeclaration;
    }

    internal static Symbol? GetNonModuleSymbolOfMergedModuleSymbol(Symbol? symbol)
    {
        if (symbol!.Declarations is null || symbol.Declarations.Length == 0 || (symbol.Flags & (SymbolFlags.Module | SymbolFlags.Transient)) == 0)
        {
            return null;
        }

        var decl = Find(symbol.Declarations, d => !IsSourceFile(d) && !IsModuleDeclaration(d));
        return decl?.Symbol;
    }

    internal static Symbol? GetLocalSymbolForExportSpecifier(Node? referenceLocation, Symbol? referenceSymbol, ExportSpecifier exportSpecifier, Checker.Checker ch)
    {
        if (IsExportSpecifierAlias(referenceLocation, exportSpecifier))
        {
            var symbol = ch.GetExportSpecifierLocalTargetSymbol(exportSpecifier.AsNode());
            if (symbol is not null)
            {
                return symbol;
            }
        }

        return referenceSymbol;
    }

    internal static bool IsExportSpecifierAlias(Node? referenceLocation, ExportSpecifier exportSpecifier)
    {
        Debug.Assert(
            exportSpecifier.PropertyName == referenceLocation || exportSpecifier.Name() == referenceLocation,
            "referenceLocation is not export specifier name or property name");
        var propertyName = exportSpecifier.PropertyName;
        if (propertyName is not null)
        {
            // Given `export { foo as bar } [from "someModule"]`: It's an alias at `foo`, but at `bar` it's a new symbol.
            return propertyName == referenceLocation;
        }
        else
        {
            // `export { foo } from "foo"` is a re-export.
            // `export { foo };` is not a re-export, it creates an alias for the local variable `foo`.
            return exportSpecifier.Parent!.Parent!.ModuleSpecifier() is null;
        }
    }

    internal static CommentRange? IsInComment(SourceFile file, int position, Node? tokenAtPosition)
    {
        return GetRangeOfEnclosingComment(file, position, FindPrecedingToken(file, position), tokenAtPosition);
    }

    internal static bool PositionBelongsToNode(Node candidate, int position, SourceFile file)
    {
        return TypeScript.LsUtil.LsUtil.PositionBelongsToNode(candidate, position, file);
    }

    /// <summary>Go: <c>type PossibleTypeArgumentInfo struct { called *ast.IdentifierNode; nTypeArguments int }</c>。</summary>
    internal sealed class PossibleTypeArgumentInfo(Node called, int nTypeArguments)
    {
        internal Node _called = called;
        internal int _nTypeArguments = nTypeArguments;
    }

    /// <summary>Get info for an expression like `f &lt;` that may be the start of type arguments.</summary>
    internal static PossibleTypeArgumentInfo? GetPossibleTypeArgumentsInfo(Node? tokenIn, SourceFile sourceFile)
    {
        // This is a rare case, but one that saves on a _lot_ of work if true - if the source file has _no_ `<` character,
        // then there obviously can't be any type arguments - no expensive brace-matching backwards scanning required
        if (sourceFile.Text().LastIndexOf('<') == -1)
        {
            return null;
        }

        var token = tokenIn;
        // This function determines if the node could be a type argument position
        // When editing, it is common to have an incomplete type argument list (e.g. missing ">"),
        // so the tree can have any shape depending on the tokens before the current node.
        // Instead, scanning for an identifier followed by a "<" before current node
        // will typically give us better results than inspecting the tree.
        // Note that we also balance out the already provided type arguments, arrays, object literals while doing so.
        var remainingLessThanTokens = 0;
        var nTypeArguments = 0;
        while (token is not null)
        {
            switch (token.Kind)
            {
                case Kind.LessThanToken:
                    // Found the beginning of the generic argument expression
                    token = FindPrecedingToken(sourceFile, token.Pos());
                    if (token is not null && token.Kind == Kind.QuestionDotToken)
                    {
                        token = FindPrecedingToken(sourceFile, token.Pos());
                    }

                    if (token is null || !IsIdentifier(token))
                    {
                        return null;
                    }

                    if (remainingLessThanTokens == 0)
                    {
                        if (IsDeclarationName(token))
                        {
                            return null;
                        }

                        return new PossibleTypeArgumentInfo(token, nTypeArguments);
                    }

                    remainingLessThanTokens--;
                    break;
                case Kind.GreaterThanGreaterThanGreaterThanToken:
                    remainingLessThanTokens += 3;
                    break;
                case Kind.GreaterThanGreaterThanToken:
                    remainingLessThanTokens += 2;
                    break;
                case Kind.GreaterThanToken:
                    remainingLessThanTokens++;
                    break;
                case Kind.CloseBraceToken:
                    // This can be object type, skip until we find the matching open brace token
                    token = FindPrecedingMatchingToken(token, Kind.OpenBraceToken, sourceFile);
                    if (token is null)
                    {
                        return null;
                    }

                    break;
                case Kind.CloseParenToken:
                    // This can be object type, skip until we find the matching open paren token
                    token = FindPrecedingMatchingToken(token, Kind.OpenParenToken, sourceFile);
                    if (token is null)
                    {
                        return null;
                    }

                    break;
                case Kind.CloseBracketToken:
                    // This can be object type, skip until we find the matching open bracket token
                    token = FindPrecedingMatchingToken(token, Kind.OpenBracketToken, sourceFile);
                    if (token is null)
                    {
                        return null;
                    }

                    break;
                case Kind.CommaToken:
                    // Valid tokens in a type name. Skip.
                    nTypeArguments++;
                    break;
                case Kind.EqualsGreaterThanToken or Kind.Identifier or Kind.StringLiteral or Kind.NumericLiteral
                    or Kind.BigIntLiteral or Kind.TrueKeyword or Kind.FalseKeyword or Kind.TypeOfKeyword or Kind.ExtendsKeyword
                    or Kind.KeyOfKeyword or Kind.DotToken or Kind.BarToken or Kind.QuestionToken or Kind.ColonToken:
                    // do nothing
                    break;
                default:
                    if (!IsTypeNode(token))
                    {
                        // Invalid token in type
                        return null;
                    }

                    break;
            }

            token = FindPrecedingToken(sourceFile, token.Pos());
        }

        return null;
    }

    internal static bool IsNameOfModuleDeclaration(Node? node)
    {
        if (node!.Parent!.Kind != Kind.ModuleDeclaration)
        {
            return false;
        }

        return node.Parent.Name() == node;
    }

    internal static bool IsExpressionOfExternalModuleImportEqualsDeclaration(Node? node)
    {
        return IsExternalModuleImportEqualsDeclaration(node!.Parent!.Parent!) &&
            GetExternalModuleImportEqualsDeclarationExpression(node.Parent.Parent) == node;
    }

    internal static bool IsNamespaceReference(Node? node)
    {
        return IsQualifiedNameNamespaceReference(node) || IsPropertyAccessNamespaceReference(node);
    }

    internal static bool IsQualifiedNameNamespaceReference(Node? node)
    {
        var root = node!;
        var isLastClause = true;
        if (root.Parent!.Kind == Kind.QualifiedName)
        {
            while (root.Parent is not null && root.Parent.Kind == Kind.QualifiedName)
            {
                root = root.Parent;
            }

            isLastClause = root.AsQualifiedName()!.Right == node;
        }

        return root.Parent!.Kind == Kind.TypeReference && !isLastClause;
    }

    internal static bool IsPropertyAccessNamespaceReference(Node? node)
    {
        var root = node!;
        var isLastClause = true;
        if (root.Parent!.Kind == Kind.PropertyAccessExpression)
        {
            while (root.Parent is not null && root.Parent.Kind == Kind.PropertyAccessExpression)
            {
                root = root.Parent;
            }

            isLastClause = root.Name() == node;
        }

        if (!isLastClause && root.Parent!.Kind == Kind.ExpressionWithTypeArguments && root.Parent.Parent!.Kind == Kind.HeritageClause)
        {
            var decl = root.Parent.Parent.Parent!;
            return (decl.Kind == Kind.ClassDeclaration && root.Parent.Parent.AsHeritageClause()!.Token == Kind.ImplementsKeyword) ||
                (decl.Kind == Kind.InterfaceDeclaration && root.Parent.Parent.AsHeritageClause()!.Token == Kind.ExtendsKeyword);
        }

        return false;
    }

    internal static bool IsThis(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.ThisKeyword:
                // case ast.KindThisType: TODO: GH#9267
                return true;
            case Kind.Identifier:
                // 'this' as a parameter
                return node.Text() == "this" && node.Parent!.Kind == Kind.Parameter;
            default:
                return false;
        }
    }

    internal static bool IsTypeReference(Node? node)
    {
        if (IsRightSideOfQualifiedNameOrPropertyAccess(node!))
        {
            node = node!.Parent;
        }

        switch (node!.Kind)
        {
            case Kind.ThisKeyword:
                return !IsExpressionNode(node);
            case Kind.ThisType:
                return true;
        }

        switch (node.Parent!.Kind)
        {
            case Kind.TypeReference:
                return true;
            case Kind.ImportType:
                return !node.Parent.AsImportTypeNode()!.IsTypeOf;
            case Kind.ExpressionWithTypeArguments:
                return IsPartOfTypeNode(node.Parent);
        }

        return false;
    }

    internal static bool IsInRightSideOfInternalImportEqualsDeclaration(Node? node)
    {
        if (node!.Parent is null)
        {
            return false;
        }

        while (node.Parent!.Kind == Kind.QualifiedName)
        {
            node = node.Parent;
        }

        return IsInternalModuleImportEqualsDeclaration(node.Parent) && node.Parent.AsImportEqualsDeclaration()!.ModuleReference == node;
    }

    internal static TextRange CreateRangeFromNode(Node node, SourceFile file)
    {
        return NewTextRange(GetTokenPosOfNode(node, file, false /*includeJSDoc*/), node.End());
    }

    internal static string Quote(SourceFile file, UserPreferences preferences, string text)
    {
        // Editors can pass in undefined or empty string - we want to infer the preference in those cases.
        var quotePreference = GetQuotePreference(file, preferences);
        var quoted = StringifyJson(text, "" /*prefix*/, "" /*indent*/);
        if (quotePreference == QuotePreferenceSingle)
        {
            quoted = "'" + _quoteReplacer(StripQuotes(quoted)) + "'";
        }

        return quoted;
    }

    // Go: var typeKeywords *collections.Set[ast.Kind] = collections.NewSetFromItems(...)
    internal static readonly Set<Kind> _typeKeywords = NewSetFromItems(
        Kind.AnyKeyword,
        Kind.AssertsKeyword,
        Kind.BigIntKeyword,
        Kind.BooleanKeyword,
        Kind.FalseKeyword,
        Kind.InferKeyword,
        Kind.KeyOfKeyword,
        Kind.NeverKeyword,
        Kind.NullKeyword,
        Kind.NumberKeyword,
        Kind.ObjectKeyword,
        Kind.ReadonlyKeyword,
        Kind.StringKeyword,
        Kind.SymbolKeyword,
        Kind.TypeOfKeyword,
        Kind.TrueKeyword,
        Kind.VoidKeyword,
        Kind.UndefinedKeyword,
        Kind.UniqueKeyword,
        Kind.UnknownKeyword);

    internal static bool IsTypeKeyword(Kind kind)
    {
        return _typeKeywords.Has(kind);
    }

    internal static bool IsSeparator(Node? node, Node? candidate)
    {
        return candidate is not null &&
            node!.Parent is not null &&
            (candidate.Kind == Kind.CommaToken || (candidate.Kind == Kind.SemicolonToken && node.Parent.Kind == Kind.ObjectLiteralExpression));
    }

    internal static bool IsLiteralNameOfPropertyDeclarationOrIndexAccess(Node? node)
    {
        switch (node!.Parent!.Kind)
        {
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.PropertyAssignment:
            case Kind.EnumMember:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.ModuleDeclaration:
                return GetNameOfDeclaration(node.Parent) == node;
            case Kind.ElementAccessExpression:
                return node.Parent.AsElementAccessExpression()!.ArgumentExpression == node;
            case Kind.ComputedPropertyName:
                return true;
            case Kind.LiteralType:
                return node.Parent.Parent!.Kind == Kind.IndexedAccessType;
            default:
                return false;
        }
    }

    internal static bool IsObjectBindingElementWithoutPropertyName(Node? bindingElement)
    {
        return bindingElement!.Kind == Kind.BindingElement &&
            bindingElement.Parent!.Kind == Kind.ObjectBindingPattern &&
            bindingElement.Name()!.Kind == Kind.Identifier &&
            bindingElement.PropertyName() is null;
    }

    internal static bool IsRightSideOfPropertyAccess(Node? node)
    {
        return node!.Parent is not null && node.Parent.Kind == Kind.PropertyAccessExpression && node.Parent.Name() == node;
    }

    internal static bool IsStaticSymbol(Symbol? symbol)
    {
        if (symbol!.ValueDeclaration is null)
        {
            return false;
        }

        var modifierFlags = symbol.ValueDeclaration.ModifierFlags;
        return (modifierFlags & ModifierFlags.Static) != 0;
    }

    internal static bool IsImplementation(Node? node)
    {
        if ((node!.Flags & NodeFlags.Ambient) != 0)
        {
            return !(node.Kind == Kind.InterfaceDeclaration || node.Kind == Kind.TypeAliasDeclaration);
        }

        if (IsVariableLike(node))
        {
            return HasInitializer(node);
        }

        if (IsFunctionLikeDeclaration(node))
        {
            return node.Body() is not null;
        }

        return IsClassLike(node) || IsModuleOrEnumDeclaration(node);
    }

    internal static bool IsImplementationExpression(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.ParenthesizedExpression:
                return IsImplementationExpression(node.Expression());
            case Kind.ArrowFunction:
            case Kind.FunctionExpression:
            case Kind.ObjectLiteralExpression:
            case Kind.ClassExpression:
            case Kind.ArrayLiteralExpression:
                return true;
            default:
                return false;
        }
    }

    internal static bool IsReadonlyTypeOperator(Node? node)
    {
        return node!.Kind == Kind.ReadonlyKeyword &&
            node.Parent!.Kind == Kind.TypeOperator &&
            node.Parent.AsTypeOperatorNode()!.Operator == Kind.ReadonlyKeyword;
    }

    internal static bool IsJumpStatementTarget(Node? node)
    {
        var parent = node!.Parent!;
        return node.Kind == Kind.Identifier && IsBreakOrContinueStatement(parent) && parent.Label() == node;
    }

    internal static bool IsLabelOfLabeledStatement(Node? node)
    {
        return node!.Kind == Kind.Identifier && node.Parent!.Kind == Kind.LabeledStatement && node.Parent.Label() == node;
    }

    internal static FileReference? FindReferenceInPosition(FileReference[]? refs, int pos)
    {
        return Find(refs, @ref => @ref.Range.ContainsInclusive(pos));
    }

    internal static Node? GetContainingNodeIfInHeritageClause(Node? node)
    {
        if (node!.Kind == Kind.Identifier || node.Kind == Kind.QualifiedName || node.Kind == Kind.PropertyAccessExpression)
        {
            return GetContainingNodeIfInHeritageClause(node.Parent);
        }

        var n = node!;
        var parent = n.Parent!;
        var grandParent = parent.Parent!;
        if ((n.Kind == Kind.ExpressionWithTypeArguments || n.Kind == Kind.TypeReference) &&
            IsHeritageClause(parent) &&
            (IsClassLike(grandParent) || grandParent.Kind == Kind.InterfaceDeclaration))
        {
            return grandParent;
        }

        return null;
    }

    internal static Node? GetContainerNode(Node? node)
    {
        for (var parent = node!.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent.Kind)
            {
                case Kind.SourceFile:
                case Kind.MethodDeclaration:
                case Kind.MethodSignature:
                case Kind.FunctionDeclaration:
                case Kind.FunctionExpression:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                case Kind.ClassDeclaration:
                case Kind.InterfaceDeclaration:
                case Kind.EnumDeclaration:
                case Kind.ModuleDeclaration:
                    return parent;
            }
        }

        return null;
    }

    internal static Node? GetAdjustedLocation(Node? node, bool forRename, SourceFile? sourceFile)
    {
        // todo: check if this function needs to be changed for jsdoc updates
        var parent = node!.Parent!;
        // /**/<modifier> [|name|] ...
        // /**/<modifier> <class|interface|type|enum|module|namespace|function|get|set> [|name|] ...
        // /**/<class|interface|type|enum|module|namespace|function|get|set> [|name|] ...
        // /**/import [|name|] = ...
        //
        // NOTE: If the node is a modifier, we don't adjust its location if it is the `default` modifier as that is handled
        // specially by `getSymbolAtLocation`.
        bool IsModifierFn(Node n)
        {
            if (IsModifier(n) && (forRename || n.Kind != Kind.DefaultKeyword))
            {
                return CanHaveModifiers(parent) && Array.IndexOf(parent.ModifierNodes() ?? [], n) >= 0;
            }

            switch (n.Kind)
            {
                case Kind.ClassKeyword:
                    return IsClassDeclaration(parent) || IsClassExpression(n);
                case Kind.FunctionKeyword:
                    return IsFunctionDeclaration(parent) || IsFunctionExpression(n);
                case Kind.InterfaceKeyword:
                    return IsInterfaceDeclaration(parent);
                case Kind.EnumKeyword:
                    return IsEnumDeclaration(parent);
                case Kind.TypeKeyword:
                    return IsTypeAliasDeclaration(parent);
                case Kind.NamespaceKeyword:
                case Kind.ModuleKeyword:
                    return IsModuleDeclaration(parent);
                case Kind.ImportKeyword:
                    return IsImportEqualsDeclaration(parent);
                case Kind.GetKeyword:
                    return IsGetAccessorDeclaration(parent);
                case Kind.SetKeyword:
                    return IsSetAccessorDeclaration(parent);
            }

            return false;
        }

        if (IsModifierFn(node))
        {
            if (sourceFile is null)
            {
                sourceFile = GetSourceFileOfNode(node)!;
            }

            var location = GetAdjustedLocationForDeclaration(parent, forRename, sourceFile);
            if (location is not null)
            {
                return location;
            }
        }

        // /**/<var|let|const> [|name|] ...
        if ((node.Kind == Kind.VarKeyword || node.Kind == Kind.ConstKeyword || node.Kind == Kind.LetKeyword) &&
            IsVariableDeclarationList(parent) && parent.AsVariableDeclarationList()!.Declarations!.Nodes!.Length == 1)
        {
            var declaration = parent.AsVariableDeclarationList()!.Declarations!.Nodes![0].AsVariableDeclaration()!;
            if (IsIdentifier(declaration.Name()!))
            {
                return declaration.Name();
            }
        }

        if (node.Kind == Kind.TypeKeyword)
        {
            // import /**/type [|name|] from ...;
            // import /**/type { [|name|] } from ...;
            // import /**/type { propertyName as [|name|] } from ...;
            // import /**/type ... from "[|module|]";
            if (IsImportClause(parent) && parent.IsTypeOnly())
            {
                var location = GetAdjustedLocationForImportDeclaration(parent.Parent!.AsImportDeclaration()!, forRename);
                if (location is not null)
                {
                    return location;
                }
            }

            // export /**/type { [|name|] } from ...;
            // export /**/type { propertyName as [|name|] } from ...;
            // export /**/type * from "[|module|]";
            // export /**/type * as ... from "[|module|]";
            if (IsExportDeclaration(parent) && parent.IsTypeOnly())
            {
                var location = GetAdjustedLocationForExportDeclaration(parent.AsExportDeclaration()!, forRename);
                if (location is not null)
                {
                    return location;
                }
            }
        }

        // import { propertyName /**/as [|name|] } ...
        // import * /**/as [|name|] ...
        // export { propertyName /**/as [|name|] } ...
        // export * /**/as [|name|] ...
        if (node.Kind == Kind.AsKeyword)
        {
            if ((parent.Kind == Kind.ImportSpecifier && parent.PropertyName() is not null) ||
                (parent.Kind == Kind.ExportSpecifier && parent.PropertyName() is not null) ||
                parent.Kind == Kind.NamespaceImport ||
                parent.Kind == Kind.NamespaceExport)
            {
                return parent.Name();
            }

            if (parent.Kind == Kind.ExportDeclaration)
            {
                var exportClause = parent.AsExportDeclaration()!.ExportClause;
                if (exportClause is not null && exportClause.Kind == Kind.NamespaceExport)
                {
                    return exportClause.Name();
                }
            }
        }

        // /**/import [|name|] from ...;
        // /**/import { [|name|] } from ...;
        // /**/import { propertyName as [|name|] } from ...;
        // /**/import ... from "[|module|]";
        // /**/import "[|module|]";
        if (node.Kind == Kind.ImportKeyword && parent.Kind == Kind.ImportDeclaration)
        {
            var location = GetAdjustedLocationForImportDeclaration(parent.AsImportDeclaration()!, forRename);
            if (location is not null)
            {
                return location;
            }
        }

        if (node.Kind == Kind.ExportKeyword)
        {
            // /**/export { [|name|] } ...;
            // /**/export { propertyName as [|name|] } ...;
            // /**/export * from "[|module|]";
            // /**/export * as ... from "[|module|]";
            if (parent.Kind == Kind.ExportDeclaration)
            {
                var location = GetAdjustedLocationForExportDeclaration(parent.AsExportDeclaration()!, forRename);
                if (location is not null)
                {
                    return location;
                }
            }

            // NOTE: We don't adjust the location of the `default` keyword as that is handled specially by `getSymbolAtLocation`.
            // /**/export default [|name|];
            // /**/export = [|name|];
            if (parent.Kind == Kind.ExportAssignment)
            {
                return SkipOuterExpressions(parent.Expression(), OuterExpressionKinds.All);
            }
        }

        // import name = /**/require("[|module|]");
        if (node.Kind == Kind.RequireKeyword && parent.Kind == Kind.ExternalModuleReference)
        {
            return parent.Expression();
        }

        // import ... /**/from "[|module|]";
        // export ... /**/from "[|module|]";
        if (node.Kind == Kind.FromKeyword)
        {
            if ((parent.Kind == Kind.ImportDeclaration || parent.Kind == Kind.ExportDeclaration) && parent.ModuleSpecifier() is not null)
            {
                return parent.ModuleSpecifier();
            }
        }

        // class ... /**/extends [|name|] ...
        // class ... /**/implements [|name|] ...
        // class ... /**/implements name1, name2 ...
        // interface ... /**/extends [|name|] ...
        // interface ... /**/extends name1, name2 ...
        if ((node.Kind == Kind.ExtendsKeyword || node.Kind == Kind.ImplementsKeyword) &&
            parent.Kind == Kind.HeritageClause && parent.AsHeritageClause()!.Token == node.Kind)
        {
            Node? GetAdjustedLocationForHeritageClause(HeritageClause clause)
            {
                // /**/extends [|name|]
                // /**/implements [|name|]
                if (clause.Types!.Nodes!.Length == 1)
                {
                    return GetHeritageClauseElementName(clause.Types.Nodes![0]);
                }

                // fall through `getAdjustedLocation`
                //    /**/extends name1, name2 ...
                //    /**/implements name1, name2 ...
                return null;
            }

            var location = GetAdjustedLocationForHeritageClause(parent.AsHeritageClause()!);
            if (location is not null)
            {
                return location;
            }
        }

        if (node.Kind == Kind.ExtendsKeyword)
        {
            // ... <T /**/extends [|U|]> ...
            if (parent.Kind == Kind.TypeParameter)
            {
                var constraint = parent.AsTypeParameterDeclaration()!.Constraint;
                if (constraint is not null && constraint.Kind == Kind.TypeReference)
                {
                    return constraint.AsTypeReferenceNode()!.TypeName;
                }
            }

            // ... T /**/extends [|U|] ? ...
            if (parent.Kind == Kind.ConditionalType)
            {
                var extendsType = parent.AsConditionalTypeNode()!.ExtendsType;
                if (extendsType is not null && extendsType.Kind == Kind.TypeReference)
                {
                    return extendsType.AsTypeReferenceNode()!.TypeName;
                }
            }
        }

        // ... T extends /**/infer [|U|] ? ...
        if (node.Kind == Kind.InferKeyword && parent.Kind == Kind.InferType)
        {
            return parent.AsInferTypeNode()!.TypeParameter!.Name();
        }

        // { [ [|K|] /**/in keyof T]: ... }
        if (node.Kind == Kind.InKeyword && parent.Kind == Kind.TypeParameter && parent.Parent!.Kind == Kind.MappedType)
        {
            return parent.Name();
        }

        // /**/keyof [|T|]
        if (node.Kind == Kind.KeyOfKeyword && parent.Kind == Kind.TypeOperator && parent.AsTypeOperatorNode()!.Operator == Kind.KeyOfKeyword)
        {
            var parentType = parent.Type();
            if (parentType is not null && parentType.Kind == Kind.TypeReference)
            {
                return parentType.AsTypeReferenceNode()!.TypeName;
            }
        }

        // /**/readonly [|name|][]
        if (node.Kind == Kind.ReadonlyKeyword && parent.Kind == Kind.TypeOperator && parent.AsTypeOperatorNode()!.Operator == Kind.ReadonlyKeyword)
        {
            var parentType = parent.Type();
            if (parentType is not null && parentType.Kind == Kind.ArrayType && parentType.AsArrayTypeNode()!.ElementType!.Kind == Kind.TypeReference)
            {
                return parentType.AsArrayTypeNode()!.ElementType!.AsTypeReferenceNode()!.TypeName;
            }
        }

        if (!forRename)
        {
            // /**/new [|name|]
            // /**/void [|name|]
            // /**/void obj.[|name|]
            // /**/typeof [|name|]
            // /**/typeof obj.[|name|]
            // /**/await [|name|]
            // /**/await obj.[|name|]
            // /**/yield [|name|]
            // /**/yield obj.[|name|]
            // /**/delete obj.[|name|]
            if ((node.Kind == Kind.NewKeyword && parent.Kind == Kind.NewExpression) ||
                (node.Kind == Kind.VoidKeyword && parent.Kind == Kind.VoidExpression) ||
                (node.Kind == Kind.TypeOfKeyword && parent.Kind == Kind.TypeOfExpression) ||
                (node.Kind == Kind.AwaitKeyword && parent.Kind == Kind.AwaitExpression) ||
                (node.Kind == Kind.YieldKeyword && parent.Kind == Kind.YieldExpression) ||
                (node.Kind == Kind.DeleteKeyword && parent.Kind == Kind.DeleteExpression))
            {
                var expr = parent.Expression();
                if (expr is not null)
                {
                    return SkipOuterExpressions(expr, OuterExpressionKinds.All);
                }
            }

            // left /**/in [|name|]
            // left /**/instanceof [|name|]
            if ((node.Kind == Kind.InKeyword || node.Kind == Kind.InstanceOfKeyword) &&
                parent.Kind == Kind.BinaryExpression && parent.AsBinaryExpression()!.OperatorToken == node)
            {
                return SkipOuterExpressions(parent.AsBinaryExpression()!.Right, OuterExpressionKinds.All);
            }

            // left /**/as [|name|]
            if (node.Kind == Kind.AsKeyword && parent.Kind == Kind.AsExpression)
            {
                var asExprType = parent.Type();
                if (asExprType is not null && asExprType.Kind == Kind.TypeReference)
                {
                    return asExprType.AsTypeReferenceNode()!.TypeName;
                }
            }

            // for (... /**/in [|name|])
            // for (... /**/of [|name|])
            if ((node.Kind == Kind.InKeyword && parent.Kind == Kind.ForInStatement) ||
                (node.Kind == Kind.OfKeyword && parent.Kind == Kind.ForOfStatement))
            {
                return SkipOuterExpressions(parent.Expression(), OuterExpressionKinds.All);
            }
        }

        return node;
    }

    internal static Node? GetAdjustedLocationForDeclaration(Node? node, bool forRename, SourceFile sourceFile)
    {
        if (node!.Name() is not null)
        {
            return node.Name();
        }

        if (forRename)
        {
            return null;
        }

        switch (node.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.FunctionDeclaration:
                // for class and function declarations, use the `default` modifier
                // when the declaration is unnamed.
                // ⚠ Go 的谓词忽略了形参、直接看外层 `node.Kind`（class/function 声明不可能是 `default` 关键字节点）
                // ⇒ Go 侧这里恒返回 nil。1:1 保留该行为（谓词内联，见 go:internal/ls/utilities.go:566）。
                return Find(node.ModifierNodes(), _ => node.Kind == Kind.DefaultKeyword);
            case Kind.ClassExpression:
                // for class expressions, use the `class` keyword when the class is unnamed
                return FindChildOfKind(node, Kind.ClassKeyword, sourceFile);
            case Kind.FunctionExpression:
                // for function expressions, use the `function` keyword when the function is unnamed
                return FindChildOfKind(node, Kind.FunctionKeyword, sourceFile);
            case Kind.Constructor:
                return node;
        }

        return null;
    }

    internal static Node? GetAdjustedLocationForImportDeclaration(ImportDeclaration node, bool forRename)
    {
        if (node.ImportClause is not null)
        {
            if (node.ImportClause.Name() is not null)
            {
                if (node.ImportClause.AsImportClause()!.NamedBindings is not null)
                {
                    // do not adjust if we have both a name and named bindings
                    return null;
                }

                // /**/import [|name|] from ...;
                // import /**/type [|name|] from ...;
                return node.ImportClause.Name();
            }

            // /**/import { [|name|] } from ...;
            // /**/import { propertyName as [|name|] } from ...;
            // /**/import * as [|name|] from ...;
            // import /**/type { [|name|] } from ...;
            // import /**/type { propertyName as [|name|] } from ...;
            // import /**/type * as [|name|] from ...;
            var namedBindings = node.ImportClause.AsImportClause()!.NamedBindings;
            if (namedBindings is not null)
            {
                switch (namedBindings.Kind)
                {
                    case Kind.NamedImports:
                        // do nothing if there is more than one binding
                        var elements = namedBindings.Elements();
                        if (elements is null || elements.Length != 1)
                        {
                            return null;
                        }

                        return elements[0].Name();

                    case Kind.NamespaceImport:
                        return namedBindings.Name();
                }
            }
        }

        if (!forRename)
        {
            // /**/import "[|module|]";
            // /**/import ... from "[|module|]";
            // import /**/type ... from "[|module|]";
            return node.ModuleSpecifier;
        }

        return null;
    }

    internal static Node? GetAdjustedLocationForExportDeclaration(ExportDeclaration node, bool forRename)
    {
        if (node.ExportClause is not null)
        {
            // /**/export { [|name|] } ...
            // /**/export { propertyName as [|name|] } ...
            // /**/export * as [|name|] ...
            // export /**/type { [|name|] } from ...
            // export /**/type { propertyName as [|name|] } from ...
            // export /**/type * as [|name|] ...
            switch (node.ExportClause.Kind)
            {
                case Kind.NamedExports:
                    // do nothing if there is more than one binding
                    var elements = node.ExportClause.Elements();
                    if (elements is null || elements.Length != 1)
                    {
                        return null;
                    }

                    return elements[0].Name();
                case Kind.NamespaceExport:
                    return node.ExportClause.Name();
            }
        }

        if (!forRename)
        {
            // /**/export * from "[|module|]";
            // export /**/type * from "[|module|]";
            return node.ModuleSpecifier;
        }

        return null;
    }

    internal static bool SymbolFlagsHaveMeaning(SymbolFlags flags, SemanticMeaning meaning)
    {
        if (meaning == SemanticMeaning.All)
        {
            return true;
        }

        if (((int)meaning & (int)SemanticMeaning.Value) != 0)
        {
            return ((uint)flags & (uint)SymbolFlags.Value) != 0;
        }

        if (((int)meaning & (int)SemanticMeaning.Type) != 0)
        {
            return ((uint)flags & (uint)SymbolFlags.Type) != 0;
        }

        if (((int)meaning & (int)SemanticMeaning.Namespace) != 0)
        {
            return ((uint)flags & (uint)SymbolFlags.Namespace) != 0;
        }

        return false;
    }

    internal static SemanticMeaning GetMeaningFromLocation(Node? node)
    {
        // todo: check if this function needs to be changed for jsdoc updates
        node = GetAdjustedLocation(GetReparsedNodeForNode(node), false /*forRename*/, null);
        var parent = node!.Parent!;
        if (IsSourceFile(node))
        {
            return SemanticMeaning.Value;
        }

        if (NodeKindIs(parent, Kind.ExportAssignment, Kind.ExportSpecifier, Kind.ExternalModuleReference, Kind.ImportSpecifier, Kind.ImportClause) ||
            (parent.Kind == Kind.ImportEqualsDeclaration && node == parent.Name()))
        {
            return SemanticMeaning.All;
        }

        if (IsInRightSideOfInternalImportEqualsDeclaration(node))
        {
            //     import a = |b|; // Namespace
            //     import a = |b.c|; // Value, type, namespace
            //     import a = |b.c|.d; // Namespace
            var name = node;
            if (node.Kind != Kind.QualifiedName)
            {
                name = IfElse(
                    node.Parent!.Kind == Kind.QualifiedName && node.Parent.AsQualifiedName()!.Right == node,
                    node.Parent,
                    (Node?)null);
            }

            if (name is not null && name.Parent!.Kind == Kind.ImportEqualsDeclaration)
            {
                return SemanticMeaning.All;
            }

            return SemanticMeaning.Namespace;
        }

        if (IsDeclarationName(node))
        {
            return GetMeaningFromDeclaration(parent);
        }

        if (IsEntityName(node) && IsJSDocNameReferenceContext(node))
        {
            return SemanticMeaning.All;
        }

        if (IsTypeReference(node))
        {
            return SemanticMeaning.Type;
        }

        if (IsNamespaceReference(node))
        {
            return SemanticMeaning.Namespace;
        }

        if (IsTypeParameterDeclaration(parent))
        {
            return SemanticMeaning.Type;
        }

        if (IsLiteralTypeNode(parent))
        {
            // This might be T["name"], which is actually referencing a property and not a type. So allow both meanings.
            return SemanticMeaning.Type | SemanticMeaning.Value;
        }

        return SemanticMeaning.Value;
    }

    internal static SemanticMeaning GetMeaningFromDeclaration(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.BindingElement:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.CatchClause:
            case Kind.JsxAttribute:
                return SemanticMeaning.Value;

            case Kind.TypeParameter:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.TypeLiteral:
                return SemanticMeaning.Type;

            case Kind.EnumMember:
            case Kind.ClassDeclaration:
                return SemanticMeaning.Value | SemanticMeaning.Type;

            case Kind.ModuleDeclaration:
                if (IsAmbientModule(node))
                {
                    return SemanticMeaning.Namespace | SemanticMeaning.Value;
                }
                else if (GetModuleInstanceState(node) == ModuleInstanceState.Instantiated)
                {
                    return SemanticMeaning.Namespace | SemanticMeaning.Value;
                }
                else
                {
                    return SemanticMeaning.Namespace;
                }

            case Kind.EnumDeclaration:
            case Kind.NamedImports:
            case Kind.ImportSpecifier:
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ExportAssignment:
            case Kind.ExportDeclaration:
                return SemanticMeaning.All;

            // An external module can be a Value
            case Kind.SourceFile:
                return SemanticMeaning.Namespace | SemanticMeaning.Value;
        }

        return SemanticMeaning.All;
    }

    internal static SemanticMeaning GetIntersectingMeaningFromDeclarations(Node? node, Symbol symbol, SemanticMeaning defaultMeaning)
    {
        if (node is null)
        {
            return defaultMeaning;
        }

        var meaning = GetMeaningFromLocation(node);
        var declarations = symbol.Declarations;
        if (declarations is null || declarations.Length == 0)
        {
            return meaning;
        }

        var lastIterationMeaning = meaning;

        // !!! TODO check if the port is correct and the for loop is needed
        SemanticMeaning Iteration(SemanticMeaning m)
        {
            foreach (var declaration in declarations)
            {
                var declarationMeaning = GetMeaningFromDeclaration(declaration);

                if ((declarationMeaning & m) != 0)
                {
                    m |= declarationMeaning;
                }
            }

            return m;
        }

        meaning = Iteration(meaning);

        while (meaning != lastIterationMeaning)
        {
            // The result is order-sensitive, for instance if initialMeaning == Namespace, and declarations = [class, instantiated module]
            // we need to consider both as the initialMeaning intersects with the module in the namespace space, and the module
            // intersects with the class in the value space.
            // To achieve that we will keep iterating until the result stabilizes.

            // Remember the last meaning
            lastIterationMeaning = meaning;
            meaning = Iteration(meaning);
        }

        return meaning;
    }

    /// <summary>Returns the node in an `extends` or `implements` clause of a class or interface.</summary>
    internal static Node[]? GetAllSuperTypeNodes(Node? node)
    {
        if (IsInterfaceDeclaration(node!))
        {
            return GetHeritageElements(node, Kind.ExtendsKeyword);
        }

        if (IsClassLike(node))
        {
            return Concatenate(
                SingleElementSlice(GetClassExtendsHeritageElement(node)!),
                GetImplementsHeritageClauseElements(node));
        }

        return null;
    }

    internal static Symbol[]? GetParentSymbolsOfPropertyAccess(Node? location, Symbol? symbol, Checker.Checker ch)
    {
        if (!IsRightSideOfPropertyAccess(location))
        {
            return null;
        }

        var lhsType = ch.GetTypeAtLocation(location!.Parent!.Expression()!);
        if (lhsType is null)
        {
            return null;
        }

        Type[]? possibleSymbols = null;
        if ((lhsType.Flags() & TypeFlags.UnionOrIntersection) != 0)
        {
            possibleSymbols = lhsType.Types();
        }
        else if (lhsType.Symbol() != symbol!.Parent)
        {
            possibleSymbols = [lhsType];
        }

        return MapNonNil(possibleSymbols, t =>
        {
            var s = t.Symbol();
            if (s is not null && (s.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
            {
                return s;
            }

            return null!;
        });
    }

    /// <summary>
    /// Find symbol of the given property-name and add the symbol to the given result array.
    /// </summary>
    /// <param name="symbol">a symbol to start searching for the given propertyName</param>
    /// <param name="propertyName">a name of property to search for</param>
    /// <param name="cb">a cache of symbol from previous iterations of calling this function to prevent infinite revisiting of the same symbol.
    /// The value of previousIterationSymbol is undefined when the function is first called.</param>
    internal static Symbol? GetPropertySymbolsFromBaseTypes(Symbol? symbol, string propertyName, Checker.Checker checker, Func<Symbol, Symbol?> cb)
    {
        var seen = new Set<Symbol>();

        Symbol? Recur(Symbol? sym)
        {
            // Use `addToSeen` to ensure we don't infinitely recurse in this situation:
            //      interface C extends C {
            //          /*findRef*/propName: string;
            //      }
            if (sym is null || (sym.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) == 0 || !seen.AddIfAbsent(sym))
            {
                return null;
            }

            foreach (var declaration in sym.Declarations ?? [])
            {
                foreach (var typeReference in GetAllSuperTypeNodes(declaration) ?? [])
                {
                    var propertyType = checker.GetTypeAtLocation(typeReference);
                    if (propertyType is not null && propertyType.Symbol() is not null)
                    {
                        // Visit the typeReference as well to see if it directly or indirectly uses that property
                        var propertySymbol = checker.GetPropertyOfType(propertyType, propertyName);
                        if (propertySymbol is not null)
                        {
                            foreach (var rootSymbol in checker.GetRootSymbols(propertySymbol))
                            {
                                var result = cb(rootSymbol);
                                if (result is not null)
                                {
                                    return result;
                                }
                            }
                        }

                        var recurred = Recur(propertyType.Symbol());
                        if (recurred is not null)
                        {
                            return recurred;
                        }
                    }
                }
            }

            return null;
        }

        return Recur(symbol);
    }

    internal static Symbol? GetPropertySymbolFromBindingElement(Checker.Checker checker, Node? bindingElement)
    {
        var typeOfPattern = checker.GetTypeAtLocation(bindingElement!.Parent!);
        if (typeOfPattern is not null)
        {
            return checker.GetPropertyOfType(typeOfPattern, bindingElement.Name()!.Text());
        }

        return null;
    }

    internal static Symbol? GetPropertySymbolOfObjectBindingPatternWithoutPropertyName(Symbol? symbol, Checker.Checker checker)
    {
        var bindingElement = GetDeclarationOfKind(symbol, Kind.BindingElement);
        if (bindingElement is not null && IsObjectBindingElementWithoutPropertyName(bindingElement))
        {
            return GetPropertySymbolFromBindingElement(checker, bindingElement);
        }

        return null;
    }

    internal static Node? GetTargetLabel(Node? referenceNode, string labelName)
    {
        // todo: rewrite as `ast.FindAncestor`
        while (referenceNode is not null)
        {
            if (referenceNode.Kind == Kind.LabeledStatement && referenceNode.Label()!.Text() == labelName)
            {
                return referenceNode.Label();
            }

            referenceNode = referenceNode.Parent;
        }

        return null;
    }

    internal static Type SkipConstraint(Type t, Checker.Checker typeChecker)
    {
        if (t.IsTypeParameter())
        {
            var c = typeChecker.GetBaseConstraintOfType(t);
            if (c is not null)
            {
                return c;
            }
        }

        return t;
    }

    internal interface ICaseClauseTracker
    {
        void AddValue(object value);

        bool HasValue(object value);
    }

    internal sealed class CaseClauseTrackerState : ICaseClauseTracker
    {
        internal Set<string> _existingStrings = new();
        internal Set<Number> _existingNumbers = new();
        internal Set<PseudoBigInt> _existingBigInts = new();

        public void AddValue(object value)
        {
            switch (value)
            {
                case string v:
                    _existingStrings.Add(v);
                    break;
                case Number v:
                    _existingNumbers.Add(v);
                    break;
                default:
                    UnsupportedType(value);
                    break;
            }
        }

        public bool HasValue(object value)
        {
            switch (value)
            {
                case string v:
                    return _existingStrings.Has(v);
                case Number v:
                    return _existingNumbers.Has(v);
                case PseudoBigInt v:
                    return _existingBigInts.Has(v);
                default:
                    return UnsupportedType(value);
            }
        }
    }

    /// <summary>Go 的 `panic(fmt.Sprintf("Unsupported type: %T", v))`（caseClauseTracker 的兜底分支）。</summary>
    internal static bool UnsupportedType(object value)
    {
        Debug.Fail("Unsupported type: " + value.GetType());
        throw new System.Diagnostics.UnreachableException();
    }

    internal static ICaseClauseTracker NewCaseClauseTracker(Checker.Checker typeChecker, Node[]? clauses)
    {
        var c = new CaseClauseTrackerState
        {
            _existingStrings = new Set<string>(),
            _existingNumbers = new Set<Number>(),
            _existingBigInts = new Set<PseudoBigInt>(),
        };
        foreach (var clause in clauses ?? [])
        {
            if (!IsDefaultClause(clause))
            {
                var expression = SkipParentheses(clause.Expression());
                if (IsLiteralExpression(expression))
                {
                    switch (expression!.Kind)
                    {
                        case Kind.NoSubstitutionTemplateLiteral:
                        case Kind.StringLiteral:
                            c._existingStrings.Add(expression.Text());
                            break;
                        case Kind.NumericLiteral:
                            c._existingNumbers.Add(FromString(expression.Text()));
                            break;
                        case Kind.BigIntLiteral:
                            c._existingBigInts.Add(ParseValidBigInt(expression.Text()));
                            break;
                    }
                }
                else
                {
                    var symbol = typeChecker.GetSymbolAtLocation(clause.Expression()!);
                    if (symbol is not null && symbol.ValueDeclaration is not null && IsEnumMember(symbol.ValueDeclaration))
                    {
                        var enumValue = typeChecker.GetConstantValue(symbol.ValueDeclaration);
                        if (enumValue is not null)
                        {
                            c.AddValue(enumValue);
                        }
                    }
                }
            }
        }

        return c;
    }

    public static bool RangeContainsRange(TextRange r1, TextRange r2)
    {
        return StartEndContainsRange(r1.Pos(), r1.End(), r2);
    }

    internal static bool StartEndContainsRange(int start, int end, TextRange textRange)
    {
        return start <= textRange.Pos() && end >= textRange.End();
    }

    internal static Signature[] GetPossibleGenericSignatures(Node called, int typeArgumentCount, Checker.Checker c)
    {
        var typeAtLocation = c.GetTypeAtLocation(called);
        if (IsOptionalChain(called.Parent!))
        {
            typeAtLocation = RemoveOptionality(typeAtLocation, IsOptionalChainRoot(called.Parent!), true /*isOptionalChain*/, c);
        }

        var signatures = IsNewExpression(called.Parent!)
            ? c.GetSignaturesOfType(typeAtLocation, SignatureKind.Construct)
            : c.GetSignaturesOfType(typeAtLocation, SignatureKind.Call);
        // Go: `s.TypeParameters() != nil && len(...) >= typeArgumentCount`（Go 里"没有类型参数"是 nil 切片；
        // C# 的 `Signature.TypeParameters()` 恒非空——无参时是 `[]` ⇒ 用 `Length > 0` 复刻 `!= nil`）。
        return Filter(signatures, s => s.TypeParameters().Length > 0 && s.TypeParameters().Length >= typeArgumentCount);
    }

    internal static Type RemoveOptionality(Type t, bool isOptionalExpression, bool isOptionalChain, Checker.Checker c)
    {
        if (isOptionalExpression)
        {
            return c.GetNonNullableType(t);
        }
        else if (isOptionalChain)
        {
            return c.GetNonOptionalType(t)!;
        }

        return t;
    }

    internal static bool IsNoSubstitutionTemplateLiteral(Node? node)
    {
        return node!.Kind == Kind.NoSubstitutionTemplateLiteral;
    }

    internal static bool IsTaggedTemplateExpression(Node? node)
    {
        return node!.Kind == Kind.TaggedTemplateExpression;
    }

    internal static bool IsInsideTemplateLiteral(Node? node, int position, SourceFile sourceFile)
    {
        return IsTemplateLiteralKind(node!.Kind) &&
            ((GetTokenPosOfNode(node, sourceFile, false) < position && position < node.End()) ||
                (IsUnterminatedLiteral(node) && position == node.End()));
    }

    // Pseudo-literals
    internal static bool IsTemplateHead(Node? node)
    {
        return node!.Kind == Kind.TemplateHead;
    }

    internal static bool IsTemplateTail(Node? node)
    {
        return node!.Kind == Kind.TemplateTail;
    }

    internal static Node? FindPrecedingMatchingToken(Node? token, Kind matchingTokenKind, SourceFile sourceFile)
    {
        var closeTokenText = TokenToString(token!.Kind);
        var matchingTokenText = TokenToString(matchingTokenKind);
        // Text-scan based fast path - can be bamboozled by comments and other trivia, but often provides
        // a good, fast approximation without too much extra work in the cases where it fails.
        var bestGuessIndex = sourceFile.Text().LastIndexOf(matchingTokenText, StringComparison.Ordinal);
        if (bestGuessIndex == -1)
        {
            return null; // if the token text doesn't appear in the file, there can't be a match - super fast bail
        }

        // we can only use the textual result directly if we didn't have to count any close tokens within the range
        if (sourceFile.Text().LastIndexOf(closeTokenText, StringComparison.Ordinal) < bestGuessIndex)
        {
            var nodeAtGuess = FindPrecedingToken(sourceFile, bestGuessIndex + 1);
            if (nodeAtGuess is not null && nodeAtGuess.Kind == matchingTokenKind)
            {
                return nodeAtGuess;
            }
        }

        var tokenKind = token.Kind;
        var remainingMatchingTokens = 0;
        while (true)
        {
            var preceding = FindPrecedingToken(sourceFile, token.Pos());
            if (preceding is null)
            {
                return null;
            }

            token = preceding;
            switch (token.Kind)
            {
                case var k when k == matchingTokenKind:
                    if (remainingMatchingTokens == 0)
                    {
                        return token;
                    }

                    remainingMatchingTokens--;
                    break;
                case var k when k == tokenKind:
                    remainingMatchingTokens++;
                    break;
            }
        }
    }

    internal static NodeList? FindContainingList(Node? node, SourceFile file)
    {
        // The node might be a list element (nonsynthetic) or a comma (synthetic). Either way, it will
        // be parented by the container of the SyntaxList, not the SyntaxList itself.
        NodeList? list = null;
        Node? VisitNode(Node n, NodeVisitor v)
        {
            return n;
        }

        NodeList? VisitNodes(NodeList nodes, NodeVisitor v)
        {
            if (nodes is not null && RangeContainsRange(nodes.Loc, node!.Loc))
            {
                list = nodes;
            }

            return nodes;
        }

        VisitEachChildAndJSDoc(node!.Parent!, file, VisitNode, VisitNodes);
        return list;
    }

    internal static IEnumerable<CommentRange>? GetLeadingCommentRangesOfNode(Node node, SourceFile file)
    {
        if (node.Kind == Kind.JsxText)
        {
            return null;
        }

        return GetLeadingCommentRanges(new NodeFactory(), file.Text(), node.Pos());
    }

    /// <summary>Equivalent to Strada's <c>node.getChildren()</c> for non-JSDoc nodes.</summary>
    internal static Node[]? GetChildrenFromNonJSDocNode(Node node, SourceFile sourceFile)
    {
        var childNodes = new List<Node>();
        node.ForEachChild(child =>
        {
            childNodes.Add(child);
            return false;
        });

        // If the node has no children, don't scan for tokens.
        // This prevents creating tokens for leaf nodes' own text.
        if (childNodes.Count == 0)
        {
            return null;
        }

        var children = new List<Node>();
        var pos = node.Pos();
        foreach (var child in childNodes)
        {
            var sc = GetScannerForSourceFile(sourceFile, pos);
            while (pos < child.Pos())
            {
                var token = sc.Token();
                var tokenFullStart = sc.TokenFullStart();
                var tokenEnd = sc.TokenEnd();
                children.Add(sourceFile.GetOrCreateToken(token, tokenFullStart, tokenEnd, node, sc.TokenFlags));
                pos = tokenEnd;
                sc.Scan();
            }

            children.Add(child);
            pos = child.End();
        }

        var trailing = GetScannerForSourceFile(sourceFile, pos);
        while (pos < node.End())
        {
            var token = trailing.Token();
            var tokenFullStart = trailing.TokenFullStart();
            var tokenEnd = trailing.TokenEnd();
            children.Add(sourceFile.GetOrCreateToken(token, tokenFullStart, tokenEnd, node, trailing.TokenFlags));
            pos = tokenEnd;
            trailing.Scan();
        }

        return children.ToArray();
    }

    /// <summary>Returns the containing object literal property declaration given a possible name node, e.g. "a" in x = { "a": 1 }</summary>
    internal static Node? GetContainingObjectLiteralElement(Node? node)
    {
        var element = GetContainingObjectLiteralElementWorker(node);
        if (element is not null && (IsObjectLiteralExpression(element.Parent!) || IsJsxAttributes(element.Parent!)))
        {
            return element;
        }

        return null;
    }

    internal static Node? GetContainingObjectLiteralElementWorker(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.NumericLiteral:
                if (node.Parent!.Kind == Kind.ComputedPropertyName)
                {
                    if (IsObjectLiteralOrJsxElement(node.Parent.Parent!))
                    {
                        return node.Parent.Parent;
                    }

                    return null;
                }

                goto case Kind.Identifier;
            case Kind.Identifier:
            case Kind.JsxNamespacedName:
                var parent = node.Parent!;
                if (IsObjectLiteralOrJsxElement(parent) &&
                    (parent.Parent!.Kind == Kind.ObjectLiteralExpression || parent.Parent.Kind == Kind.JsxAttributes) &&
                    parent.Name() == node)
                {
                    return parent;
                }

                break;
        }

        return null;
    }

    internal static bool IsObjectLiteralOrJsxElement(Node node)
    {
        return IsObjectLiteralElement(node) || IsJsxAttribute(node) || IsJsxSpreadAttribute(node);
    }

    /// <summary>Return a function that returns true if the given node has not been seen.</summary>
    internal static Func<Node, bool> NodeSeenTracker()
    {
        var seen = new Set<Node>();
        return node => seen.AddIfAbsent(node);
    }

    /// <summary>FindAllReferences.toContextSpan</summary>
    internal static TextRange? ToContextRange(TextRange? textRange, SourceFile contextFile, Node? context)
    {
        if (context is null)
        {
            return textRange;
        }

        // !!! isContextWithStartAndEndNode
        var range = textRange!.Value;
        var contextRange = GetRangeOfNode(context, contextFile, null /*endNode*/);
        if (contextRange.Pos() != range.Pos() || contextRange.End() != range.End())
        {
            return contextRange;
        }

        return null;
    }

    internal static RefInfo? GetReferenceAtPosition(SourceFile sourceFile, int position, Program program)
    {
        var referencePath = FindReferenceInPosition(sourceFile.ReferencedFiles, position);
        if (referencePath is not null)
        {
            var file = program.GetSourceFileFromReference(sourceFile, referencePath);
            if (file is not null)
            {
                return new RefInfo(referencePath, file.FileName(), file, false);
            }

            return null;
        }

        var typeReferenceDirective = FindReferenceInPosition(sourceFile.TypeReferenceDirectives, position);
        if (typeReferenceDirective is not null)
        {
            var reference = program.GetResolvedTypeReferenceDirectiveFromTypeReferenceDirective(typeReferenceDirective, sourceFile);
            if (reference is not null)
            {
                var file = program.GetSourceFile(reference.ResolvedFileName);
                if (file is not null)
                {
                    return new RefInfo(typeReferenceDirective, file.FileName(), file, false);
                }
            }

            return null;
        }

        var libReferenceDirective = FindReferenceInPosition(sourceFile.LibReferenceDirectives, position);
        if (libReferenceDirective is not null)
        {
            var file = program.GetLibFileFromReference(libReferenceDirective);
            if (file is not null)
            {
                return new RefInfo(libReferenceDirective, file.FileName(), file, false);
            }

            return null;
        }

        if (sourceFile.Imports().Length == 0 && (sourceFile.ModuleAugmentations is null || sourceFile.ModuleAugmentations.Length == 0))
        {
            return null;
        }

        var node = GetTouchingToken(sourceFile, position);
        if (!IsModuleSpecifierLike(node) || !IsExternalModuleNameRelative(node.Text()))
        {
            return null;
        }

        var resolution = program.GetResolvedModuleFromModuleSpecifier(sourceFile, node);
        if (resolution is not null)
        {
            var verifiedFileName = resolution.ResolvedFileName;
            var fileName = resolution.ResolvedFileName;
            if (fileName == "")
            {
                fileName = ResolvePath(GetDirectoryPath(sourceFile.FileName()), node.Text());
            }

            return new RefInfo(null, fileName, program.GetSourceFile(fileName), verifiedFileName != "");
        }

        return null;
    }

    internal static Type? GetContextualTypeFromParent(Node? node, Checker.Checker typeChecker, ContextFlags contextFlags)
    {
        var parent = WalkUpParenthesizedExpressions(node!.Parent);
        switch (parent!.Kind)
        {
            case Kind.NewExpression:
                return typeChecker.GetContextualType(parent, contextFlags);
            case Kind.BinaryExpression:
                var binary = parent.AsBinaryExpression()!;
                if (IsEqualityOperatorKind(binary.OperatorToken!.Kind))
                {
                    return typeChecker.GetTypeAtLocation(
                        IfElse(node == binary.Right, binary.Left, binary.Right)!);
                }

                return typeChecker.GetContextualType(node, contextFlags);
            case Kind.CaseClause:
                return GetSwitchedType(parent.AsCaseOrDefaultClause()!, typeChecker);
            default:
                return typeChecker.GetContextualType(node, contextFlags);
        }
    }

    internal static Type? GetContextualTypeFromParentOrAncestorTypeNode(Node? node, Checker.Checker typeChecker)
    {
        if ((node!.Flags & NodeFlags.JSDoc) != 0 && (node.Flags & NodeFlags.JavaScriptFile) == 0)
        {
            return null;
        }

        var contextualType = GetContextualTypeFromParent(node, typeChecker, ContextFlags.None);
        if (contextualType is not null)
        {
            return contextualType;
        }

        var ancestorTypeNode = GetAncestorTypeNode(node);
        if (ancestorTypeNode is not null)
        {
            return typeChecker.GetTypeAtLocation(ancestorTypeNode);
        }

        return null;
    }

    internal static Node? GetAncestorTypeNode(Node? node)
    {
        Node? lastTypeNode = null;
        FindAncestor(node, n =>
        {
            if (IsTypeNode(n))
            {
                lastTypeNode = n;
            }

            return !IsQualifiedName(n.Parent!) && !IsTypeNode(n.Parent) && !IsTypeElement(n.Parent);
        });
        return lastTypeNode;
    }

    internal static bool IsSourceFileWithGlobalExports(Node? node)
    {
        return node is not null && IsSourceFile(node) && node.AsSourceFile()!.GlobalExports is not null;
    }

    // ── 以下 4 个 helper 在 Go 里位于别的文件，见文件头第 12 条（G1 统一落地，后续切片请勿重复声明）──

    /// <summary>go:internal/ls/format.go:257-300（G4 的 Format.cs 只落其余成员）。</summary>
    internal static CommentRange? GetRangeOfEnclosingComment(
        SourceFile file,
        int position,
        Node? precedingToken,
        Node? tokenAtPosition)
    {
        var jsdoc = FindAncestor(tokenAtPosition, n => n.IsJSDoc());
        if (jsdoc is not null)
        {
            tokenAtPosition = jsdoc.Parent;
        }

        var tokenStart = GetStartOfNode(tokenAtPosition!, file, false /*includeJSDoc*/);
        if (tokenStart <= position && position < tokenAtPosition!.End())
        {
            return null;
        }

        // Between two consecutive tokens, all comments are either trailing on the former
        // or leading on the latter (and none are in both lists).
        IEnumerable<CommentRange>? trailingRangesOfPreviousToken = null;
        if (precedingToken is not null)
        {
            trailingRangesOfPreviousToken = GetTrailingCommentRanges(new NodeFactory(), file.Text(), precedingToken.End());
        }

        var leadingRangesOfNextToken = GetLeadingCommentRangesOfNode(tokenAtPosition!, file);
        var commentRanges = ConcatenateSeq(trailingRangesOfPreviousToken, leadingRangesOfNextToken);
        foreach (var commentRange in commentRanges)
        {
            // The end marker of a single-line comment does not include the newline character.
            // In the following case where the cursor is at `^`, we are inside a comment:
            //
            //    // asdf   ^\n
            //
            // But for closed multi-line comments, we don't want to be inside the comment in the following case:
            //
            //    /* asdf */^
            //
            // Internally, we represent the end of the comment prior to the newline and at the '/', respectively.
            //
            // However, unterminated multi-line comments lack a `/`, end at the end of the file, and *do* contain their end.
            if (commentRange.Range.ContainsExclusive(position) ||
                (position == commentRange.Range.End() &&
                    (commentRange.Kind == Kind.SingleLineCommentTrivia || position == file.Text().Length)))
            {
                return commentRange;
            }
        }

        return null;
    }

    /// <summary>go:internal/ls/completions.go:3557-3559（G8 的 Completions*.cs 只落其余成员）。</summary>
    internal static Type? GetSwitchedType(CaseOrDefaultClause caseClause, Checker.Checker typeChecker)
    {
        return typeChecker.GetTypeAtLocation(caseClause.Parent!.Parent!.Expression()!);
    }

    /// <summary>go:internal/ls/completions.go:3561-3565（G8 的 Completions*.cs 只落其余成员）。</summary>
    internal static bool IsEqualityOperatorKind(Kind kind)
    {
        switch (kind)
        {
            case Kind.EqualsEqualsEqualsToken:
            case Kind.EqualsEqualsToken:
            case Kind.ExclamationEqualsEqualsToken:
            case Kind.ExclamationEqualsToken:
                return true;
            default:
                return false;
        }
    }

    /// <summary>go:internal/ls/findallreferences.go:333-352（G6 的 FindAllReferences.cs 只落其余成员）。</summary>
    internal static TextRange GetRangeOfNode(Node node, SourceFile? sourceFile, Node? endNode)
    {
        sourceFile ??= GetSourceFileOfNode(node)!;
        var start = GetTokenPosOfNode(node, sourceFile, false /*includeJsDoc*/);
        var end = IfElse(endNode is not null, endNode, node)!.End();
        if (IsStringLiteralLike(node) && (end - start) > 2)
        {
            if (endNode is not null)
            {
                throw new DebugFailureException("endNode is not nil for stringLiteralLike");
            }

            start += 1;
            end -= 1;
        }

        if (endNode is not null && endNode.Kind == Kind.CaseBlock)
        {
            end = endNode.Pos();
        }

        return NewTextRange(start, end);
    }
}

/// <summary>utilities.go 里挂在 `*LanguageService` 上的成员（`LanguageService` 的成员一律 PascalCase，见文件头第 1 条）。</summary>
public sealed partial class LanguageService
{
    internal (Range Range, Fidelity Fidelity) CreateLspRangeFromNode(Node node, SourceFile file)
    {
        return CreateLspRangeFromBounds(GetTokenPosOfNode(node, file, false /*includeJSDoc*/), node.End(), file);
    }

    internal (Range Range, Fidelity Fidelity) CreateLspRangeFromNodeForFeature(Node node, SourceFile file, Feature feature)
    {
        return _converters!.ToLSPRangeForFeature(file, Ls.CreateRangeFromNode(node, file), feature);
    }

    internal (Range Range, Fidelity Fidelity) CreateLspRangeFromBounds(int start, int end, SourceFile file)
    {
        return _converters!.ToLSPRange(file, NewTextRange(start, end));
    }

    internal (Range Range, Fidelity Fidelity) CreateLspRangeFromRange(TextRange textRange, LsConv.Script script)
    {
        return _converters!.ToLSPRange(script, textRange);
    }

    internal (Position Position, Fidelity Fidelity) CreateLspPosition(int position, SourceFile file)
    {
        return _converters!.ToLSPPosition(file, new TextPos(position));
    }
}

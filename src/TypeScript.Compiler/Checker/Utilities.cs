// ported from: internal/checker/utilities.go:1-349（诊断构造 + 基础节点/符号/类型判定）
//
// 本片是 utilities.go 的 4 个 partial 片段之一（其余见 Utilities.Compare.cs / Utilities.Operators.cs /
// Utilities.Misc.cs / Utilities.Features.cs / Utilities.Program.cs）。类形状见 docs/checker-plan.md §5.2：
// 单一非 static `public sealed partial class Checker`；Go 的包级函数 → 该类的 `public static`（Go 导出）/
// `internal static`（Go 未导出）成员，调用点可 `using static TypeScript.Checker.Checker;`。
//
// 与 Go 的机械改写（全片通用，不逐条重复标注）：
// 1) `panic("Unhandled case in x")` → `Debug.Fail("Unhandled case in x")` + `throw new System.Diagnostics.UnreachableException()`
//    （`Debug.Fail` 无 `[DoesNotReturn]`，其后必须补 throw 才能满足返回值控制流；parser/binder 先例）。
// 2) Go 的 `a != nil && f(a) && b != nil && g(b)` 链 → C# 局部变量 + 提前 return。**理由**：C# 的可空流分析
//    不跨方法调用跟踪**字段**状态（`node.Parent` 是字段），直接照抄会得到 CS8604（本项目
//    TreatWarningsAsErrors）。短路顺序与 Go 完全一致，语义不变。
// 3) `len(x)`（x 是 Go 切片）→ C# `(x?.Length ?? 0)`（`Node.Properties()`/`Elements()`/`TypeArguments()`
//    返回 `Node[]?`，Go 的 len(nil)==0）。
// 4) `map[K]V` 读缺失键（Go 返回零值）→ `GetValueOrDefault`（`Dictionary` 索引器会抛异常，docs/architecture.md §3.2）。
// 5) `*ast.Node` 参数在 Go 里可能为 nil 的写 `Node?`；调用 C# Ast 的非空形参（如 `IsIdentifier(Node node)`）前
//    用 `!`（Go 在这些位置要么已判 nil，要么本来就是语法不变量）。
// 6) 命名（CS0111 避让）：Go 的包级 `declarationBelongsToPrivateAmbientMember`（utilities.go:333）与 checker.go:18367 的
//    **实例方法**同名 → 包级那个加 `Worker` 后缀（docs/checker-plan.md §5.2），故为
//    `DeclarationBelongsToPrivateAmbientMemberWorker`（artifacts/checker-method-collisions.md 追加表）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

/// <summary>Go: <c>type AssignmentKind int32</c> + `AssignmentKindNone/Definite/Compound`（utilities.go:79-85）。</summary>
public enum AssignmentKind
{
    None = 0,
    Definite = 1,
    Compound = 2,
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func NewDiagnosticForNode(node *ast.Node, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>（utilities.go:21-29）。</summary>
    public static Diagnostic NewDiagnosticForNode(Node? node, Message message, params object?[] args)
    {
        SourceFile? file = null;
        var loc = default(TextRange);
        if (node is not null)
        {
            file = GetSourceFileOfNode(node);
            loc = GetErrorRangeForNode(file!, node);
        }
        return NewDiagnostic(file, loc, message, args);
    }

    /// <summary>Go: <c>func NewDiagnosticChainForNode(chain *ast.Diagnostic, node *ast.Node, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>（utilities.go:31-36）。</summary>
    public static Diagnostic NewDiagnosticChainForNode(Diagnostic? chain, Node? node, Message message, params object?[] args)
    {
        if (chain is not null)
        {
            return NewDiagnosticChain(chain, message, args);
        }
        return NewDiagnosticForNode(node, message, args);
    }

    /// <summary>Go: <c>func findInMap[K comparable, V any](m map[K]V, predicate func(V) bool) V</c>（utilities.go:38-45）。</summary>
    internal static V FindInMap<K, V>(Dictionary<K, V> m, Func<V, bool> predicate)
        where K : notnull
    {
        foreach (var value in m.Values)
        {
            if (predicate(value))
            {
                return value;
            }
        }
        // Go: `return *new(V)` —— V 的零值（引用类型是 null，值类型是零）。
        return default!;
    }

    /// <summary>Go: <c>func tokenIsIdentifierOrKeyword(token ast.Kind) bool</c>（utilities.go:47-49）。</summary>
    internal static bool TokenIsIdentifierOrKeyword(Kind token)
    {
        return (int)token >= (int)Kind.Identifier;
    }

    /// <summary>Go: <c>func tokenIsIdentifierOrKeywordOrGreaterThan(token ast.Kind) bool</c>（utilities.go:51-53）。</summary>
    internal static bool TokenIsIdentifierOrKeywordOrGreaterThan(Kind token)
    {
        return token == Kind.GreaterThanToken || TokenIsIdentifierOrKeyword(token);
    }

    /// <summary>Go: <c>func hasOverrideModifier(node *ast.Node) bool</c>（utilities.go:55-57）。</summary>
    internal static bool HasOverrideModifier(Node node)
    {
        return HasSyntacticModifier(node, ModifierFlags.Override);
    }

    /// <summary>Go: <c>func hasAsyncModifier(node *ast.Node) bool</c>（utilities.go:59-61）。</summary>
    internal static bool HasAsyncModifier(Node node)
    {
        return HasSyntacticModifier(node, ModifierFlags.Async);
    }

    /// <summary>Go: <c>func getSelectedModifierFlags(node *ast.Node, flags ast.ModifierFlags) ast.ModifierFlags</c>（utilities.go:63-65）。</summary>
    internal static ModifierFlags GetSelectedModifierFlags(Node node, ModifierFlags flags)
    {
        return node.ModifierFlags & flags;
    }

    /// <summary>Go: <c>func hasReadonlyModifier(node *ast.Node) bool</c>（utilities.go:67-69）。</summary>
    internal static bool HasReadonlyModifier(Node node)
    {
        return HasModifier(node, ModifierFlags.Readonly);
    }

    /// <summary>Go: <c>func isStaticPrivateIdentifierProperty(s *ast.Symbol) bool</c>（utilities.go:71-73）。</summary>
    internal static bool IsStaticPrivateIdentifierProperty(Symbol s)
    {
        return s.ValueDeclaration is not null && IsPrivateIdentifierClassElementDeclaration(s.ValueDeclaration) && IsStatic(s.ValueDeclaration);
    }

    /// <summary>Go: <c>func isEmptyObjectLiteral(expression *ast.Node) bool</c>（utilities.go:75-77）。</summary>
    internal static bool IsEmptyObjectLiteral(Node expression)
    {
        return IsObjectLiteralExpression(expression) && (expression.Properties()?.Length ?? 0) == 0;
    }

    // Go: `type AssignmentTarget = ast.Node`（utilities.go:87，纯别名）→ 不移植：C# 没有类型别名，
    // 所有出现处直接写 `Node`（docs/architecture.md §3.2 对 `ResolutionMode = ModuleKind` 的同一处理）。
    // `AssignmentKind`（utilities.go:79-85）是命名空间级类型，见本文件顶部。

    /// <summary>Go: <c>func getAssignmentTargetKind(node *ast.Node) AssignmentKind</c>（utilities.go:89-107）。</summary>
    internal static AssignmentKind GetAssignmentTargetKind(Node node)
    {
        var target = GetAssignmentTarget(node);
        if (target is null)
        {
            return AssignmentKind.None;
        }
        switch (target.Kind)
        {
            case Kind.BinaryExpression:
                var binaryOperator = target.AsBinaryExpression()!.OperatorToken!.Kind;
                if (binaryOperator == Kind.EqualsToken || IsLogicalOrCoalescingAssignmentOperator(binaryOperator))
                {
                    return AssignmentKind.Definite;
                }
                return AssignmentKind.Compound;
            case Kind.PrefixUnaryExpression:
            case Kind.PostfixUnaryExpression:
                return AssignmentKind.Compound;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                return AssignmentKind.Definite;
        }
        Debug.Fail("Unhandled case in getAssignmentTargetKind");
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Go: <c>func isDeleteTarget(node *ast.Node) bool</c>（utilities.go:109-115）。</summary>
    internal static bool IsDeleteTarget(Node node)
    {
        if (!IsAccessExpression(node))
        {
            return false;
        }
        var parent = WalkUpParenthesizedExpressions(node.Parent);
        return parent is not null && parent.Kind == Kind.DeleteExpression;
    }

    /// <summary>Go: <c>func isInCompoundLikeAssignment(node *ast.Node) bool</c>（utilities.go:117-120）。</summary>
    internal static bool IsInCompoundLikeAssignment(Node node)
    {
        var target = GetAssignmentTarget(node);
        return target is not null && IsAssignmentExpression(target /*excludeCompoundAssignment*/, true) && IsCompoundLikeAssignment(target);
    }

    /// <summary>Go: <c>func isCompoundLikeAssignment(assignment *ast.Node) bool</c>（utilities.go:122-125）。</summary>
    internal static bool IsCompoundLikeAssignment(Node assignment)
    {
        var right = SkipParentheses(assignment.AsBinaryExpression()!.Right)!;
        return right.Kind == Kind.BinaryExpression && IsShiftOperatorOrHigher(right.AsBinaryExpression()!.OperatorToken!.Kind);
    }

    /// <summary>Go: <c>func isConstTypeReference(node *ast.Node) bool</c>（utilities.go:127-129）。</summary>
    internal static bool IsConstTypeReference(Node node)
    {
        if (!IsTypeReferenceNode(node) || (node.TypeArguments()?.Length ?? 0) != 0)
        {
            return false;
        }
        var typeName = node.AsTypeReferenceNode()!.TypeName!;
        return IsIdentifier(typeName) && typeName.Text() == "const";
    }

    /// <summary>
    /// Go: <c>func isConstTypeReferenceName(node *ast.Node) bool</c>（utilities.go:131-139）。
    /// 判断 node 是否是 `const` 断言的 `const` 类型名（`x as const` / `&lt;const&gt;x`），它不应被当作真实名字解析。
    /// </summary>
    internal static bool IsConstTypeReferenceName(Node? node)
    {
        if (node is null || !IsIdentifier(node))
        {
            return false;
        }
        var parent = node.Parent;
        if (parent is null || !IsConstTypeReference(parent))
        {
            return false;
        }
        var grandParent = parent.Parent;
        return grandParent is not null && IsAssertionExpression(grandParent);
    }

    /// <summary>
    /// Go: <c>func isExportAssignmentExpressionName(node *ast.Node) bool</c>（utilities.go:141-152）。
    /// 判断 node 是否是 `export =` / `export default` 赋值表达式的（根实体名）。在那里引用命名空间或纯类型名是合法的，
    /// 由 checkExportAssignment 判定是否为错误，所以 checkIdentifier 不应对它报值使用错误。
    /// </summary>
    internal static bool IsExportAssignmentExpressionName(Node? node)
    {
        if (node is null)
        {
            return false;
        }
        var current = node;
        while (current.Parent is not null && IsPropertyAccessOrQualifiedName(current.Parent))
        {
            current = current.Parent;
        }
        return current.Parent is not null && IsExportAssignment(current.Parent) && current.Parent.Expression() == current;
    }

    /// <summary>Go: <c>func GetSingleVariableOfVariableStatement(node *ast.Node) *ast.Node</c>（utilities.go:154-159）。</summary>
    public static Node? GetSingleVariableOfVariableStatement(Node node)
    {
        if (!IsVariableStatement(node))
        {
            return null;
        }
        return FirstOrNil(node.AsVariableStatement()!.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes);
    }

    /// <summary>Go: <c>func isTypeReferenceIdentifier(node *ast.Node) bool</c>（utilities.go:161-166）。</summary>
    internal static bool IsTypeReferenceIdentifier(Node node)
    {
        while (node.Parent!.Kind == Kind.QualifiedName)
        {
            node = node.Parent!;
        }
        return IsTypeReferenceNode(node.Parent!);
    }

    /// <summary>Go: <c>func IsInTypeQuery(node *ast.Node) bool</c>（utilities.go:168-181）。</summary>
    public static bool IsInTypeQuery(Node node)
    {
        // TypeScript 1.0 spec (April 2014): 3.6.3
        // A type query consists of the keyword typeof followed by an expression.
        // The expression is restricted to a single identifier or a sequence of identifiers separated by periods
        return FindAncestorOrQuit(node, n =>
        {
            switch (n.Kind)
            {
                case Kind.TypeQuery:
                    return FindAncestorResult.True;
                case Kind.Identifier:
                case Kind.QualifiedName:
                    return FindAncestorResult.False;
            }
            return FindAncestorResult.Quit;
        }) is not null;
    }

    /// <summary>Go: <c>func canHaveLocals(node *ast.Node) bool</c>（utilities.go:183-195）。</summary>
    internal static bool CanHaveLocals(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ArrowFunction:
            case Kind.Block:
            case Kind.CallSignature:
            case Kind.CaseBlock:
            case Kind.CatchClause:
            case Kind.ClassStaticBlockDeclaration:
            case Kind.ConditionalType:
            case Kind.Constructor:
            case Kind.ConstructorType:
            case Kind.ConstructSignature:
            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.FunctionType:
            case Kind.GetAccessor:
            case Kind.IndexSignature:
            case Kind.JSDocSignature:
            case Kind.MappedType:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.ModuleDeclaration:
            case Kind.SetAccessor:
            case Kind.SourceFile:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func isShorthandAmbientModuleSymbol(moduleSymbol *ast.Symbol) bool</c>（utilities.go:197-199）。</summary>
    internal static bool IsShorthandAmbientModuleSymbol(Symbol moduleSymbol)
    {
        return IsShorthandAmbientModule(moduleSymbol.ValueDeclaration);
    }

    /// <summary>Go: <c>func isShorthandAmbientModule(node *ast.Node) bool</c>（utilities.go:201-204）。</summary>
    internal static bool IsShorthandAmbientModule(Node? node)
    {
        // The only kind of module that can be missing a body is a shorthand ambient module.
        return node is not null && node.Kind == Kind.ModuleDeclaration && node.Body() is null;
    }

    /// <summary>Go: <c>func getAliasDeclarationFromName(node *ast.Node) *ast.Node</c>（utilities.go:206-215）。</summary>
    internal static Node? GetAliasDeclarationFromName(Node node)
    {
        switch (node.Parent!.Kind)
        {
            case Kind.ImportClause:
            case Kind.ImportSpecifier:
            case Kind.NamespaceImport:
            case Kind.ExportSpecifier:
            case Kind.ExportAssignment:
            case Kind.ImportEqualsDeclaration:
            case Kind.NamespaceExport:
                return node.Parent;
            case Kind.QualifiedName:
                return GetAliasDeclarationFromName(node.Parent!);
        }
        return null;
    }

    /// <summary>Go: <c>func entityNameToString(name *ast.Node) string</c>（utilities.go:217-219）。</summary>
    internal static string EntityNameToString(Node name)
    {
        // Go 写 `ast.EntityNameToString(...)`；本类内已有同名的单参重载（这就是 Go 的 `entityNameToString`），
        // 简单名查找会先命中本类成员，所以这里必须写全名（binder.go 的 `IsSignedNumericLiteral` 同一先例）。
        return TypeScript.Ast.Ast.EntityNameToString(name, GetTextOfNode);
    }

    /// <summary>Go: <c>func getContainingQualifiedNameNode(node *ast.Node) *ast.Node</c>（utilities.go:221-226）。</summary>
    internal static Node GetContainingQualifiedNameNode(Node node)
    {
        while (IsQualifiedName(node.Parent!))
        {
            node = node.Parent!;
        }
        return node;
    }

    /// <summary>Go: <c>func isSideEffectImport(node *ast.Node) bool</c>（utilities.go:228-231）。</summary>
    internal static bool IsSideEffectImport(Node node)
    {
        var ancestor = FindAncestor(node, n => IsImportDeclaration(n));
        return ancestor is not null && ancestor.ImportClause() is null;
    }

    /// <summary>Go: <c>func getExternalModuleRequireArgument(node *ast.Node) *ast.Node</c>（utilities.go:233-238）。</summary>
    internal static Node? GetExternalModuleRequireArgument(Node node)
    {
        if (IsVariableDeclarationInitializedToRequire(node))
        {
            return node.Initializer()!.Arguments()![0];
        }
        return null;
    }

    /// <summary>Go: <c>func isRightSideOfAccessExpression(node *ast.Node) bool</c>（utilities.go:240-243）。</summary>
    internal static bool IsRightSideOfAccessExpression(Node node)
    {
        var parent = node.Parent;
        return parent is not null &&
            (IsPropertyAccessExpression(parent) && parent.Name() == node ||
                IsElementAccessExpression(parent) && parent.AsElementAccessExpression()!.ArgumentExpression == node);
    }

    /// <summary>Go: <c>func isTopLevelInExternalModuleAugmentation(node *ast.Node) bool</c>（utilities.go:245-247）。</summary>
    internal static bool IsTopLevelInExternalModuleAugmentation(Node? node)
    {
        if (node is null)
        {
            return false;
        }
        var parent = node.Parent;
        return parent is not null && IsModuleBlock(parent) && parent.Parent is not null && IsExternalModuleAugmentation(parent.Parent);
    }

    /// <summary>Go: <c>func isSyntacticDefault(node *ast.Node) bool</c>（utilities.go:249-254）。</summary>
    internal static bool IsSyntacticDefault(Node node)
    {
        return IsExportAssignment(node) && !node.AsExportAssignment()!.IsExportEquals ||
            HasSyntacticModifier(node, ModifierFlags.Default) ||
            IsExportSpecifier(node) ||
            IsNamespaceExport(node);
    }

    /// <summary>Go: <c>func hasExportAssignmentSymbol(moduleSymbol *ast.Symbol) bool</c>（utilities.go:256-258）。</summary>
    internal static bool HasExportAssignmentSymbol(Symbol moduleSymbol)
    {
        return moduleSymbol.Exports is not null && moduleSymbol.Exports.GetValueOrDefault(InternalSymbolNameExportEquals) is not null;
    }

    /// <summary>Go: <c>func isTypeAlias(node *ast.Node) bool</c>（utilities.go:260-262）。</summary>
    internal static bool IsTypeAlias(Node node)
    {
        return IsTypeOrJSTypeAliasDeclaration(node);
    }

    /// <summary>Go: <c>func hasOnlyExpressionInitializer(node *ast.Node) bool</c>（utilities.go:264-270）。</summary>
    internal static bool HasOnlyExpressionInitializer(Node node)
    {
        switch (node.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.BindingElement:
            case Kind.PropertyDeclaration:
            case Kind.PropertyAssignment:
            case Kind.EnumMember:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func hasDotDotDotToken(node *ast.Node) bool</c>（utilities.go:272-284）。</summary>
    internal static bool HasDotDotDotToken(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Parameter:
                return node.AsParameterDeclaration()!.DotDotDotToken is not null;
            case Kind.BindingElement:
                return node.AsBindingElement()!.DotDotDotToken is not null;
            case Kind.NamedTupleMember:
                return node.AsNamedTupleMember()!.DotDotDotToken is not null;
            case Kind.JsxExpression:
                return node.AsJsxExpression()!.DotDotDotToken is not null;
        }
        return false;
    }

    /// <summary>Go: <c>func IsTypeAny(t *Type) bool</c>（utilities.go:286-288）。</summary>
    public static bool IsTypeAny(Type? t)
    {
        return t is not null && (t._flags & TypeFlags.Any) != 0;
    }

    /// <summary>Go: <c>func isJSDocOptionalParameter(node *ast.ParameterDeclaration) bool</c>（utilities.go:290-292）。</summary>
    internal static bool IsJSDocOptionalParameter(ParameterDeclaration node)
    {
        return false; // !!!
    }

    /// <summary>Go: <c>func isExclamationToken(node *ast.Node) bool</c>（utilities.go:294-296）。</summary>
    internal static bool IsExclamationToken(Node? node)
    {
        return node is not null && node.Kind == Kind.ExclamationToken;
    }

    /// <summary>Go: <c>func isOptionalDeclaration(declaration *ast.Node) bool</c>（utilities.go:298-300）。</summary>
    internal static bool IsOptionalDeclaration(Node declaration)
    {
        return HasQuestionToken(declaration);
    }

    /// <summary>Go: <c>func (c *Checker) isOptionalParameter(node *ast.Node) bool</c>（utilities.go:302-327）。</summary>
    internal bool IsOptionalParameter(Node node)
    {
        // !!! TODO: JSDoc support
        if (IsParameterDeclaration(node) && node.QuestionToken() is not null)
        {
            return true;
        }
        if (!IsParameterDeclaration(node))
        {
            return false;
        }
        if (node.Initializer() is not null)
        {
            var signature = GetSignatureFromDeclarationWorker(node.Parent!);
            var parameterIndex = FindIndex(node.Parent!.Parameters(), p => p == node);
            Debug.Assert(parameterIndex >= 0);
            // Only consider syntactic or instantiated parameters as optional, not `void` parameters as this function is used
            // in grammar checks and checking for `void` too early results in parameter types widening too early
            // and causes some noImplicitAny errors to be lost.
            return parameterIndex >= GetMinArgumentCountEx(signature, MinArgumentCountFlags.StrongArityForUntypedJS | MinArgumentCountFlags.VoidIsNonOptional);
        }
        var iife = GetImmediatelyInvokedFunctionExpression(node.Parent!);
        if (iife is not null)
        {
            var parameterIndex = FindIndex(node.Parent!.Parameters(), p => p == node);
            return node.Type() is null &&
                node.AsParameterDeclaration()!.DotDotDotToken is null &&
                parameterIndex >= GetEffectiveCallArguments(iife).Length;
        }
        return false;
    }

    /// <summary>Go: <c>func isEmptyArrayLiteral(expression *ast.Node) bool</c>（utilities.go:329-331）。</summary>
    internal static bool IsEmptyArrayLiteral(Node expression)
    {
        return IsArrayLiteralExpression(expression) && (expression.Elements()?.Length ?? 0) == 0;
    }

    /// <summary>Go: <c>func declarationBelongsToPrivateAmbientMember(declaration *ast.Node) bool</c>（utilities.go:333-340）。</summary>
    internal static bool DeclarationBelongsToPrivateAmbientMemberWorker(Node declaration)
    {
        var root = GetRootDeclaration(declaration)!;
        var memberDeclaration = root;
        if (root.Kind == Kind.Parameter)
        {
            memberDeclaration = root.Parent!;
        }
        return IsPrivateWithinAmbient(memberDeclaration);
    }

    /// <summary>Go: <c>func isPrivateWithinAmbient(node *ast.Node) bool</c>（utilities.go:342-344）。</summary>
    internal static bool IsPrivateWithinAmbient(Node node)
    {
        return (HasModifier(node, ModifierFlags.Private) || IsPrivateIdentifierClassElementDeclaration(node)) && (node.Flags & NodeFlags.Ambient) != 0;
    }

    /// <summary>Go: <c>func isTypeAssertion(node *ast.Node) bool</c>（utilities.go:346-348）。</summary>
    internal static bool IsTypeAssertion(Node node)
    {
        return IsAssertionExpression(SkipParentheses(node));
    }
}

// ported from: internal/checker/utilities.go:908-1330（属性名/绑定名/字面量名判定 + 声明/流程节点判定 + minAndMax）
//
// 与 Go 的机械改写（通用规则见 Utilities.cs 文件头）：
// 1) Go 的 `isLateBoundName` 里 `name[0] == '\xfe'` → C# `'\uFFFD'`：Go 的字节 0xFE 是非法 UTF-8，
//    凡被转成合法文本（诊断格式化 / JSON 往返）都会变成 U+FFFD；C# 的 string 恒为合法 UTF-16，
//    故直接以 U+FFFD 作为哨兵字符（与 Ast 的 `InternalSymbolNamePrefix = "\uFFFD"` 同一约定，见 Ast/Symbol.cs）。
// 2) `strings.HasPrefix(s, p)` → `HasPrefix(s, p, true)`（StringUtil 的 caseSensitive 版本）。
// 3) `slices`/`min`/`max` 内建 → 手写循环 / `Math.Min`/`Math.Max`；Go 的 `(int, int)` 多返回值 → C# 命名元组。
// 4) `node.ForEachChild(traverse)`：C# 的 `Visitor`（`delegate bool Visitor(Node node)`）用局部函数的方法组转换。
// 5) Go 的 `fallthrough`（getSuperContainer 的函数声明分支）→ 直接 `return node`（fallthrough 目标就是 `return node`）。
// 6) `ast.SignatureDeclaration`/`ast.ClassLikeDeclaration` 在 Go 里是 `= Node` 的**类型别名**
//    （ast_generated.go:543/555）→ C# 一律写 `Node`。
// 7) 命名：Go 的包级 `isTypeUsableAsPropertyName`/`getPropertyNameFromType` 与 exports.go 的**导出**
//    `IsTypeUsableAsPropertyName`/`GetPropertyNameFromType` 在 C# 里同名（CS0111）→ 未导出者加 `Worker` 后缀
//    （docs/checker-plan.md §5.2），故为 `IsTypeUsableAsPropertyNameWorker`/`GetPropertyNameFromTypeWorker`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Binder.Binder;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;

public sealed partial class Checker
{
    /// <summary>Go: <c>func getContainingFunctionOrClassStaticBlock(node *ast.Node) *ast.Node</c>（utilities.go:908-910）。</summary>
    internal static Node? GetContainingFunctionOrClassStaticBlock(Node node)
    {
        return FindAncestor(node.Parent, IsFunctionLikeOrClassStaticBlockDeclaration);
    }

    /// <summary>Go: <c>func isNodeDescendantOf(node *ast.Node, ancestor *ast.Node) bool</c>（utilities.go:912-920）。</summary>
    internal static bool IsNodeDescendantOf(Node? node, Node? ancestor)
    {
        while (node is not null)
        {
            if (node == ancestor)
            {
                return true;
            }
            node = node.Parent;
        }
        return false;
    }

    /// <summary>Go: <c>func isTypeUsableAsPropertyName(t *Type) bool</c>（utilities.go:922-924）。</summary>
    internal static bool IsTypeUsableAsPropertyNameWorker(Type t)
    {
        return (t._flags & TypeFlags.StringOrNumberLiteralOrUnique) != 0;
    }

    /// <summary>Go: <c>func getPropertyNameFromType(t *Type) string</c>（utilities.go:926-939）。取得成员类型的符号名。</summary>
    internal static string GetPropertyNameFromTypeWorker(Type t)
    {
        if ((t._flags & TypeFlags.StringLiteral) != 0)
        {
            return (string)t.AsLiteralType()!._value!;
        }
        if ((t._flags & TypeFlags.NumberLiteral) != 0)
        {
            return ((Number)t.AsLiteralType()!._value!).String();
        }
        if ((t._flags & TypeFlags.UniqueESSymbol) != 0)
        {
            return t.AsUniqueESSymbolType()!._name;
        }
        Debug.Fail("Unhandled case in getPropertyNameFromType");
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Go: <c>func isNumericLiteralName(name string) bool</c>（utilities.go:941-964）。</summary>
    internal static bool IsNumericLiteralName(string name)
    {
        // The intent of numeric names is that
        //     - they are names with text in a numeric form, and that
        //     - setting properties/indexing with them is always equivalent to doing so with the numeric literal 'numLit',
        //         acquired by applying the abstract 'ToNumber' operation on the name's text.
        //
        // The subtlety is in the latter portion, as we cannot reliably say that anything that looks like a numeric literal is a numeric name.
        // In fact, it is the case that the text of the name must be equal to 'ToString(numLit)' for this to hold.
        //
        // Consider the property name '"0xF00D"'. When one indexes with '0xF00D', they are actually indexing with the value of 'ToString(0xF00D)'
        // according to the ECMAScript specification, so it is actually as if the user indexed with the string '"61453"'.
        // Thus, the text of all numeric literals equivalent to '61543' such as '0xF00D', '0xf00D', '0170015', etc. are not valid numeric names
        // because their 'ToString' representation is not equal to their original text.
        // This is motivated by ECMA-262 sections 9.3.1, 9.8.1, 11.1.5, and 11.2.1.
        //
        // Here, we test whether 'ToString(ToNumber(name))' is exactly equal to 'name'.
        // The '+' prefix operator is equivalent here to applying the abstract ToNumber operation.
        // Applying the 'toString()' method on a number gives us the abstract ToString operation on a number.
        //
        // Note that this accepts the values 'Infinity', '-Infinity', and 'NaN', and that this is intentional.
        // This is desired behavior, because when indexing with them as numeric entities, you are indexing
        // with the strings '"Infinity"', '"-Infinity"', and '"NaN"' respectively.
        return FromString(name).String() == name;
    }

    /// <summary>Go: <c>func isThisProperty(node *ast.Node) bool</c>（utilities.go:966-968）。</summary>
    internal static bool IsThisProperty(Node node)
    {
        return (IsPropertyAccessExpression(node) || IsElementAccessExpression(node)) && node.Expression()!.Kind == Kind.ThisKeyword;
    }

    /// <summary>Go: <c>func isValidNumberString(s string, roundTripOnly bool) bool</c>（utilities.go:970-976）。</summary>
    internal static bool IsValidNumberString(string s, bool roundTripOnly)
    {
        if (s == "")
        {
            return false;
        }
        var n = FromString(s);
        return !n.IsNaN() && !n.IsInf() && (!roundTripOnly || n.String() == s);
    }

    /// <summary>Go: <c>func isValidBigIntString(s string, roundTripOnly bool) bool</c>（utilities.go:978-1002）。</summary>
    internal static bool IsValidBigIntString(string s, bool roundTripOnly)
    {
        if (s == "")
        {
            return false;
        }
        var scannerInstance = NewScanner();
        scannerInstance.SetSkipTrivia(false);
        var success = true;
        scannerInstance.SetOnError((diagnostic, start, length, args) => success = false);
        scannerInstance.SetText(s + "n");
        var result = scannerInstance.Scan();
        var negative = result == Kind.MinusToken;
        if (negative)
        {
            result = scannerInstance.Scan();
        }
        var flags = scannerInstance.TokenFlags;
        // validate that
        // * scanning proceeded without error
        // * a bigint can be scanned, and that when it is scanned, it is
        // * the full length of the input string (so the scanner is one character beyond the augmented input length)
        // * it does not contain a numeric separator (the `BigInt` constructor does not accept a numeric separator in its input)
        return success && result == Kind.BigIntLiteral && scannerInstance.TokenEnd() == s.Length + 1 && (flags & TokenFlags.ContainsSeparator) == 0 &&
            (!roundTripOnly || s == PseudoBigIntToString(NewPseudoBigInt(ParsePseudoBigInt(scannerInstance.TokenValue()), negative)));
    }

    /// <summary>Go: <c>func isValidESSymbolDeclaration(node *ast.Node) bool</c>（utilities.go:1004-1012）。</summary>
    internal static bool IsValidESSymbolDeclaration(Node node)
    {
        if (IsVariableDeclaration(node))
        {
            return IsVarConst(node) && IsIdentifier(node.AsVariableDeclaration()!.Name()!) && IsVariableDeclarationInVariableStatement(node);
        }
        if (IsPropertyDeclaration(node))
        {
            return HasReadonlyModifier(node) && HasStaticModifier(node);
        }
        return IsPropertySignatureDeclaration(node) && HasReadonlyModifier(node);
    }

    /// <summary>Go: <c>func isVariableDeclarationInVariableStatement(node *ast.Node) bool</c>（utilities.go:1014-1016）。</summary>
    internal static bool IsVariableDeclarationInVariableStatement(Node node)
    {
        return IsVariableDeclarationList(node.Parent!) && IsVariableStatement(node.Parent!.Parent!);
    }

    /// <summary>Go: <c>func IsKnownSymbol(symbol *ast.Symbol) bool</c>（utilities.go:1018-1020）。</summary>
    public static bool IsKnownSymbol(Symbol symbol)
    {
        return IsLateBoundName(symbol.Name);
    }

    /// <summary>Go: <c>func IsPrivateIdentifierSymbol(symbol *ast.Symbol) bool</c>（utilities.go:1022-1027）。</summary>
    public static bool IsPrivateIdentifierSymbol(Symbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }
        return HasPrefix(symbol.Name, InternalSymbolNamePrefix + "#", true);
    }

    /// <summary>Go: <c>func isLateBoundName(name string) bool</c>（utilities.go:1029-1031）。</summary>
    internal static bool IsLateBoundName(string name)
    {
        return name.Length >= 2 && name[0] == '\uFFFD' && name[1] == '@';
    }

    /// <summary>Go: <c>func isObjectOrArrayLiteralType(t *Type) bool</c>（utilities.go:1033-1035）。</summary>
    internal static bool IsObjectOrArrayLiteralType(Type t)
    {
        return (t._objectFlags & (ObjectFlags.ObjectLiteral | ObjectFlags.ArrayLiteral)) != 0;
    }

    /// <summary>Go: <c>func getContainingClassExcludingClassDecorators(node *ast.Node) *ast.ClassLikeDeclaration</c>（utilities.go:1037-1054）。</summary>
    internal static Node? GetContainingClassExcludingClassDecorators(Node node)
    {
        var decorator = FindAncestorOrQuit(node.Parent, n =>
        {
            if (IsClassLike(n))
            {
                return FindAncestorResult.Quit;
            }
            if (IsDecorator(n))
            {
                return FindAncestorResult.True;
            }
            return FindAncestorResult.False;
        });
        if (decorator is not null && IsClassLike(decorator.Parent))
        {
            return GetContainingClass(decorator.Parent);
        }
        if (decorator is not null)
        {
            return GetContainingClass(decorator);
        }
        return GetContainingClass(node);
    }

    /// <summary>Go: <c>func isThisTypeParameter(t *Type) bool</c>（utilities.go:1056-1058）。</summary>
    internal static bool IsThisTypeParameter(Type t)
    {
        return (t._flags & TypeFlags.TypeParameter) != 0 && t.AsTypeParameter()!._isThisType;
    }

    /// <summary>Go: <c>func isClassInstanceProperty(node *ast.Node) bool</c>（utilities.go:1060-1067）。</summary>
    internal static bool IsClassInstanceProperty(Node node)
    {
        if (IsInJSFile(node) && IsExpandoPropertyDeclaration(node))
        {
            var left = node.AsBinaryExpression()!.Left!;
            return (!IsBindableStaticAccessExpression(left, false /*excludeThisKeyword*/) || !IsPrototypeAccess(left.Expression())) &&
                !IsBindableStaticNameExpression(left, true /*excludeThisKeyword*/);
        }
        return node.Parent is not null && IsClassLike(node.Parent) && IsPropertyDeclaration(node) && !HasAccessorModifier(node);
    }

    /// <summary>Go: <c>func isThisInitializedObjectBindingExpression(node *ast.Node) bool</c>（utilities.go:1069-1073）。</summary>
    internal static bool IsThisInitializedObjectBindingExpression(Node? node)
    {
        if (node is null || !(IsShorthandPropertyAssignment(node) || IsPropertyAssignment(node)))
        {
            return false;
        }
        var grandParent = node.Parent?.Parent;
        return grandParent is not null && IsBinaryExpression(grandParent) &&
            grandParent.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken &&
            grandParent.AsBinaryExpression()!.Right!.Kind == Kind.ThisKeyword;
    }

    /// <summary>Go: <c>func isThisInitializedDeclaration(node *ast.Node) bool</c>（utilities.go:1075-1077）。</summary>
    internal static bool IsThisInitializedDeclaration(Node? node)
    {
        if (node is null || !IsVariableDeclaration(node))
        {
            return false;
        }
        var initializer = node.Initializer();
        return initializer is not null && initializer.Kind == Kind.ThisKeyword;
    }

    /// <summary>Go: <c>func isInfinityOrNaNString(name string) bool</c>（utilities.go:1079-1081）。</summary>
    internal static bool IsInfinityOrNaNString(string name)
    {
        return name == "Infinity" || name == "-Infinity" || name == "NaN";
    }

    /// <summary>Go: <c>func (c *Checker) isConstantVariable(symbol *ast.Symbol) bool</c>（utilities.go:1083-1085）。</summary>
    internal bool IsConstantVariable(Symbol symbol)
    {
        return (symbol.Flags & SymbolFlags.Variable) != 0 && (GetDeclarationNodeFlagsFromSymbol(symbol) & NodeFlags.Constant) != 0;
    }

    /// <summary>Go: <c>func (c *Checker) isParameterOrMutableLocalVariable(symbol *ast.Symbol) bool</c>（utilities.go:1087-1094）。</summary>
    internal bool IsParameterOrMutableLocalVariable(Symbol symbol)
    {
        // Return true if symbol is a parameter, a catch clause variable, or a mutable local variable
        if (symbol.ValueDeclaration is not null)
        {
            var declaration = GetRootDeclaration(symbol.ValueDeclaration);
            return declaration is not null && (IsParameterDeclaration(declaration) || IsVariableDeclaration(declaration) && (IsCatchClause(declaration.Parent!) || IsMutableLocalVariableDeclaration(declaration)));
        }
        return false;
    }

    /// <summary>Go: <c>func (c *Checker) isMutableLocalVariableDeclaration(declaration *ast.Node) bool</c>（utilities.go:1096-1099）。</summary>
    internal bool IsMutableLocalVariableDeclaration(Node declaration)
    {
        // Return true if symbol is a non-exported and non-global `let` variable
        return (declaration.Parent!.Flags & NodeFlags.Let) != 0 && !((GetCombinedModifierFlags(declaration) & ModifierFlags.Export) != 0 ||
            declaration.Parent!.Parent!.Kind == Kind.VariableStatement && IsGlobalSourceFile(declaration.Parent!.Parent!.Parent!));
    }

    /// <summary>Go: <c>func isInAmbientOrTypeNode(node *ast.Node) bool</c>（utilities.go:1101-1105）。</summary>
    internal static bool IsInAmbientOrTypeNodeWorker(Node node)
    {
        return (node.Flags & NodeFlags.Ambient) != 0 || FindAncestor(node, n =>
            IsInterfaceDeclaration(n) || IsTypeOrJSTypeAliasDeclaration(n) || IsTypeLiteralNode(n)) is not null;
    }

    /// <summary>Go: <c>func isLiteralExpressionOfObject(node *ast.Node) bool</c>（utilities.go:1107-1114）。</summary>
    internal static bool IsLiteralExpressionOfObject(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ObjectLiteralExpression:
            case Kind.ArrayLiteralExpression:
            case Kind.RegularExpressionLiteral:
            case Kind.FunctionExpression:
            case Kind.ClassExpression:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func canHaveFlowNode(node *ast.Node) bool</c>（utilities.go:1116-1118）。</summary>
    internal static bool CanHaveFlowNode(Node node)
    {
        return node.FlowNodeData() is not null;
    }

    /// <summary>Go: <c>func isNonNullAccess(node *ast.Node) bool</c>（utilities.go:1120-1122）。</summary>
    internal static bool IsNonNullAccess(Node node)
    {
        return IsAccessExpression(node) && IsNonNullExpression(node.Expression()!);
    }

    /// <summary>Go: <c>func getBindingElementPropertyName(node *ast.Node) *ast.Node</c>（utilities.go:1124-1126）。</summary>
    internal static Node? GetBindingElementPropertyName(Node node)
    {
        return node.PropertyNameOrName();
    }

    /// <summary>Go: <c>func isCallChain(node *ast.Node) bool</c>（utilities.go:1128-1130）。</summary>
    internal static bool IsCallChain(Node node)
    {
        return IsCallExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;
    }

    /// <summary>Go: <c>func (c *Checker) callLikeExpressionMayHaveTypeArguments(node *ast.Node) bool</c>（utilities.go:1132-1134）。</summary>
    internal bool CallLikeExpressionMayHaveTypeArguments(Node node)
    {
        return IsCallOrNewExpression(node) || IsTaggedTemplateExpression(node) || IsJsxOpeningLikeElement(node);
    }

    /// <summary>Go: <c>func isSuperCall(n *ast.Node) bool</c>（utilities.go:1136-1138）。</summary>
    internal static bool IsSuperCall(Node n)
    {
        return IsCallExpression(n) && n.Expression()!.Kind == Kind.SuperKeyword;
    }

    /// <summary>Go: <c>func getMembersOfDeclaration(node *ast.Node) []*ast.Node</c>（utilities.go:1140-1148）。</summary>
    internal static Node[]? GetMembersOfDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case Kind.InterfaceDeclaration:
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.TypeLiteral:
                return node.Members();
            case Kind.ObjectLiteralExpression:
                return node.Properties();
        }
        return null;
    }

    /// <summary>Go: <c>func isInRightSideOfImportOrExportAssignment(node *ast.EntityName) bool</c>（utilities.go:1150-1157）。</summary>
    internal static bool IsInRightSideOfImportOrExportAssignment(Node node)
    {
        while (node.Parent!.Kind == Kind.QualifiedName)
        {
            node = node.Parent!;
        }

        return node.Parent!.Kind == Kind.ImportEqualsDeclaration && node.Parent!.AsImportEqualsDeclaration()!.ModuleReference == node ||
            node.Parent!.Kind == Kind.ExportAssignment && node.Parent!.Expression() == node;
    }

    /// <summary>Go: <c>func isJsxIntrinsicTagName(tagName *ast.Node) bool</c>（utilities.go:1159-1161）。</summary>
    internal static bool IsJsxIntrinsicTagName(Node tagName)
    {
        return IsIdentifier(tagName) && IsIntrinsicJsxName(tagName.Text()) || IsJsxNamespacedName(tagName);
    }

    /// <summary>Go: <c>func getContainingObjectLiteral(f *ast.SignatureDeclaration) *ast.Node</c>（utilities.go:1163-1172）。</summary>
    internal static Node? GetContainingObjectLiteral(Node f)
    {
        if ((f.Kind == Kind.MethodDeclaration ||
            f.Kind == Kind.GetAccessor ||
            f.Kind == Kind.SetAccessor) && f.Parent!.Kind == Kind.ObjectLiteralExpression)
        {
            return f.Parent;
        }
        if (f.Kind == Kind.FunctionExpression && f.Parent!.Kind == Kind.PropertyAssignment)
        {
            return f.Parent!.Parent;
        }
        return null;
    }

    /// <summary>Go: <c>func isImportTypeQualifierPart(node *ast.Node) *ast.Node</c>（utilities.go:1174-1186）。</summary>
    internal static Node? IsImportTypeQualifierPart(Node node)
    {
        var parent = node.Parent;
        while (parent is not null && IsQualifiedName(parent))
        {
            node = parent;
            parent = parent.Parent;
        }

        if (parent is not null && parent.Kind == Kind.ImportType && parent.AsImportTypeNode()!.Qualifier == node)
        {
            return parent;
        }

        return null;
    }

    /// <summary>Go: <c>func isInNameOfExpressionWithTypeArgumentsOrHeritageTypeReference(node *ast.Node) bool</c>（utilities.go:1188-1195）。</summary>
    internal static bool IsInNameOfExpressionWithTypeArgumentsOrHeritageTypeReference(Node node)
    {
        while (node.Parent!.Kind == Kind.PropertyAccessExpression || node.Parent!.Kind == Kind.QualifiedName)
        {
            node = node.Parent!;
        }

        return node.Parent!.Kind == Kind.ExpressionWithTypeArguments ||
            IsNameOfHeritageClauseTypeReference(node);
    }

    /// <summary>Go: <c>func getIndexSymbolFromSymbolTable(symbolTable ast.SymbolTable) *ast.Symbol</c>（utilities.go:1197-1199）。</summary>
    internal static Symbol? GetIndexSymbolFromSymbolTable(SymbolTable? symbolTable)
    {
        // Go 的 nil map 读返回零值 nil；C# 的 null Dictionary 必须先判空。
        return symbolTable?.GetValueOrDefault(InternalSymbolNameIndex);
    }

    /// <summary>
    /// Go: <c>func expressionResultIsUnused(node *ast.Node) bool</c>（utilities.go:1201-1226）。
    /// 指示 `Expression` 的结果是否会被丢弃。注意：需要 node 有有效的 `parent`。
    /// </summary>
    internal static bool ExpressionResultIsUnused(Node node)
    {
        while (true)
        {
            var parent = node.Parent;
            // walk up parenthesized expressions, but keep a pointer to the top-most parenthesized expression
            if (IsParenthesizedExpression(parent!))
            {
                node = parent!;
                continue;
            }
            // result is unused in an expression statement, `void` expression, or the initializer or incrementer of a `for` loop
            if (IsExpressionStatement(parent!) || IsVoidExpression(parent!) ||
                IsForStatement(parent!) && (parent!.Initializer() == node || parent.AsForStatement()!.Incrementor == node))
            {
                return true;
            }
            if (IsBinaryExpression(parent!) && parent!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.CommaToken)
            {
                // left side of comma is always unused
                if (node == parent.AsBinaryExpression()!.Left)
                {
                    return true;
                }
                // right side of comma is unused if parent is unused
                node = parent;
                continue;
            }
            return false;
        }
    }

    /// <summary>Go: <c>func pseudoBigIntToString(value jsnum.PseudoBigInt) string</c>（utilities.go:1228-1230）。</summary>
    internal static string PseudoBigIntToString(PseudoBigInt value)
    {
        return value.String();
    }

    /// <summary>Go: <c>func getSuperContainer(node *ast.Node, stopOnFunctions bool) *ast.Node</c>（utilities.go:1232-1262）。</summary>
    internal static Node? GetSuperContainer(Node node, bool stopOnFunctions)
    {
        Node? current = node;
        while (true)
        {
            current = current!.Parent;
            if (current is null)
            {
                return null;
            }
            switch (current.Kind)
            {
                case Kind.ComputedPropertyName:
                    current = current.Parent!;
                    break;
                case Kind.FunctionDeclaration:
                case Kind.FunctionExpression:
                case Kind.ArrowFunction:
                    if (!stopOnFunctions)
                    {
                        continue;
                    }
                    // Go 的 `fallthrough` 落到下面那组 case（其唯一语句是 `return node`）。
                    return current;
                case Kind.PropertyDeclaration:
                case Kind.PropertySignature:
                case Kind.MethodDeclaration:
                case Kind.MethodSignature:
                case Kind.Constructor:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                case Kind.ClassStaticBlockDeclaration:
                    return current;
                case Kind.Decorator:
                    // Decorators are always applied outside of the body of a class or method.
                    if (IsParameterDeclaration(current.Parent!) && IsClassElement(current.Parent!.Parent!))
                    {
                        // If the decorator's parent is a Parameter, we resolve the this container from
                        // the grandparent class declaration.
                        current = current.Parent!.Parent;
                    }
                    else if (IsClassElement(current.Parent!))
                    {
                        // If the decorator's parent is a class element, we resolve the 'this' container
                        // from the parent class declaration.
                        current = current.Parent;
                    }
                    break;
            }
        }
    }

    /// <summary>Go: <c>func forEachYieldExpression(body *ast.Node, visitor func(expr *ast.Node) bool) bool</c>（utilities.go:1264-1296）。</summary>
    internal static bool ForEachYieldExpression(Node body, Func<Node, bool> visitor)
    {
        bool Traverse(Node node)
        {
            switch (node.Kind)
            {
                case Kind.YieldExpression:
                    if (visitor(node))
                    {
                        return true;
                    }
                    var operand = node.Expression();
                    if (operand is null)
                    {
                        return false;
                    }
                    return Traverse(operand);
                case Kind.EnumDeclaration:
                case Kind.InterfaceDeclaration:
                case Kind.ModuleDeclaration:
                case Kind.TypeAliasDeclaration:
                    // These are not allowed inside a generator now, but eventually they may be allowed
                    // as local types. Regardless, skip them to avoid the work.
                    break;
                default:
                    if (IsFunctionLike(node))
                    {
                        var name = node.Name();
                        if (name is not null && IsComputedPropertyName(name))
                        {
                            // Note that we will not include methods/accessors of a class because they would require
                            // first descending into the class. This is by design.
                            return Traverse(name.Expression()!);
                        }
                    }
                    else if (!IsPartOfTypeNode(node))
                    {
                        // This is the general case, which should include mostly expressions and statements.
                        // Also includes NodeArrays.
                        return node.ForEachChild(Traverse);
                    }
                    break;
            }
            return false;
        }

        return Traverse(body);
    }

    /// <summary>Go: <c>func getEnclosingContainer(node *ast.Node) *ast.Node</c>（utilities.go:1298-1302）。</summary>
    internal static Node? GetEnclosingContainer(Node node)
    {
        return FindAncestor(node.Parent, n => (GetContainerFlags(n) & ContainerFlags.IsContainer) != 0);
    }

    /// <summary>Go: <c>func getDeclarationsOfKind(symbol *ast.Symbol, kind ast.Kind) []*ast.Node</c>（utilities.go:1304-1306）。</summary>
    internal static Node[]? GetDeclarationsOfKind(Symbol symbol, Kind kind)
    {
        return Filter(symbol.Declarations, d => d.Kind == kind);
    }

    /// <summary>Go: <c>func hasType(node *ast.Node) bool</c>（utilities.go:1308-1310）。</summary>
    internal static bool HasType(Node node)
    {
        return node.Type() is not null;
    }

    /// <summary>Go: <c>func getNonRestParameterCount(sig *Signature) int</c>（utilities.go:1312-1314）。</summary>
    internal static int GetNonRestParameterCount(Signature sig)
    {
        return (sig._parameters?.Length ?? 0) - IfElse(SignatureHasRestParameter(sig), 1, 0);
    }

    /// <summary>Go: <c>func minAndMax[T any](slice []T, getValue func(value T) int) (int, int)</c>（utilities.go:1316-1329）。</summary>
    internal static (int MinValue, int MaxValue) MinAndMax<T>(T[]? slice, Func<T, int> getValue)
    {
        var minValue = 0;
        var maxValue = 0;
        if (slice is not null)
        {
            for (var i = 0; i < slice.Length; i++)
            {
                var value = getValue(slice[i]);
                if (i == 0)
                {
                    minValue = value;
                    maxValue = value;
                }
                else
                {
                    minValue = Math.Min(minValue, value);
                    maxValue = Math.Max(maxValue, value);
                }
            }
        }
        return (minValue, maxValue);
    }
}

// ported from: internal/pseudochecker/lookup.go
//
// 与 Go 的差异（逐条）：
// - Go 的 `*ast.Node` → `Node?`/`Node`（视 Go 是否可能为 nil）；Go 的类型别名（`*ast.Expression`、
//   `*ast.ParameterDeclarationNode`）在 C# 里就是 `Node`（docs/ast.md §2）。
// - Go 里对 **派生类型字段** 的访问（`allAccessors.GetAccessor.Type`、`accessor.AsSetAccessorDeclaration().Parameters`）
//   在 C# 里必须走派生类型（`AsGetAccessorDeclaration()!.Type`）——C# 的 `Node` 上是同名的**方法** `Type()`，
//   字段只在派生类上（docs/ast.md §2 的"字段遮蔽 Node 访问器"）。
// - `debug.FailBadSyntaxKind(...)` 后面 Go 还有 `return nil`（死代码）：C# 里补 `throw new UnreachableException()`
//   满足控制流（与 Parser 各分片同款）。
// - `slices.Backward` → 逆序 for；`slices.Index` → `Array.IndexOf`（Go 的 `*Node` 切片 == 比较在 C# 里是引用相等，
//   Node 不覆写 Equals，语义一致）；Go 的形参名 `params` 是 C# 关键字 → 改名 `parameters`。
// - 未导出的包级函数（`isValueSignatureDeclaration`/`isConstContextPropagatingKind`/…）→ `PseudoChecker` 上的
//   PascalCase `private static`；导出的（`IsInConstContext`/`CouldAlreadyReferToUndefinedType`）→ `public static`。
namespace TypeScript.PseudoChecker;

using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class PseudoChecker
{
    public PseudoType GetReturnTypeOfSignature(Node signatureNode)
    {
        switch (signatureNode.Kind)
        {
            case Kind.GetAccessor:
                return GetTypeOfAccessor(signatureNode);
            case Kind.MethodDeclaration:
            case Kind.FunctionDeclaration:
            case Kind.Constructor:
            case Kind.MethodSignature:
            case Kind.CallSignature:
            case Kind.ConstructSignature:
            case Kind.SetAccessor:
            case Kind.IndexSignature:
            case Kind.FunctionType:
            case Kind.ConstructorType:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.JSDocSignature:
                return CreateReturnFromSignature(signatureNode);
            default:
                Debug.FailBadSyntaxKind(signatureNode, "Node needs to be an inferrable node");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    public PseudoType GetTypeOfAccessor(Node accessor) => TypeFromAccessor(accessor);

    public PseudoType GetTypeOfExpression(Node node) => TypeFromExpression(node);

    public PseudoType GetTypeOfDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Parameter:
                return TypeFromParameter(node.AsParameterDeclaration()!);
            case Kind.VariableDeclaration:
                return TypeFromVariable(node.AsVariableDeclaration()!);
            case Kind.PropertySignature:
            case Kind.PropertyDeclaration:
            case Kind.JSDocPropertyTag:
                return TypeFromProperty(node);
            case Kind.BindingElement:
                return PseudoType.NewPseudoTypeNoResult(node);
            case Kind.ExportAssignment:
                return TypeFromExpression(node.AsExportAssignment()!.Expression!);
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
            case Kind.BinaryExpression:
                return TypeFromExpandoProperty(node);
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
                return TypeFromPropertyAssignment(node);
            case Kind.CallExpression:
                switch (GetAssignmentDeclarationKind(node))
                {
                    // TODO: How much of the checker's getTypeFromPropertyDescriptor is worth trying to emulate over ASTs?
                    case JSDeclarationKind.ObjectDefinePropertyValue:
                        {
                            // !!!
                        }

                        break;
                    case JSDeclarationKind.ObjectDefinePropertyExports:
                        {
                            // !!!
                        }

                        break;
                }

                return PseudoType.NewPseudoTypeNoResult(node);
            default:
                Debug.FailBadSyntaxKind(node, "node needs to be an inferrable node");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    private PseudoType TypeFromPropertyAssignment(Node node)
    {
        var annotation = node.Type();
        if (annotation is not null)
        {
            return PseudoType.NewPseudoTypeDirect(annotation);
        }

        if (node.Kind == Kind.PropertyAssignment)
        {
            var init = node.Initializer();
            if (init is not null)
            {
                var expr = TypeFromExpression(init);
                if (expr.Kind != PseudoTypeKind.Inferred || expr.AsPseudoTypeInferred().ErrorNodes!.Length > 0)
                {
                    return expr;
                }

                // fallback to NoResult if PseudoTypeKindInferred without error nodes
            }
        }

        return PseudoType.NewPseudoTypeNoResult(node);
    }

    // This is _not_ redundant with the reparser; see how expandoFunctionSymbolProperty.ts and similar behaves
    private PseudoType TypeFromExpandoProperty(Node node)
    {
        var declaredType = node.Type();
        if (declaredType is not null)
        {
            return PseudoType.NewPseudoTypeDirect(declaredType);
        }

        // While `node` is an expression, as an expando, it should also always be a
        // declaration with a `.Symbol()` which requires declaration fallback handling
        return PseudoType.NewPseudoTypeNoResult(node);
    }

    private PseudoType TypeFromProperty(Node node)
    {
        var t = node.Type();
        if (t is not null)
        {
            return PseudoType.NewPseudoTypeDirect(t);
        }

        if (IsPropertyDeclaration(node))
        {
            var init = node.Initializer();
            if (init is not null && !IsContextuallyTyped(node))
            {
                // explicit fail on readonly template literals to allow for literal freshness in the future
                if (HasModifier(node, ModifierFlags.Readonly) && IsTemplateExpression(init))
                {
                    return PseudoType.NewPseudoTypeNoResult(node);
                }

                var expr = TypeFromExpression(init);
                if (expr.Kind != PseudoTypeKind.Inferred || expr.AsPseudoTypeInferred().ErrorNodes!.Length > 0)
                {
                    var postfixToken = node.AsPropertyDeclaration()!.PostfixToken;
                    if (expr.Kind != PseudoTypeKind.Direct && postfixToken is not null && postfixToken.Kind == Kind.QuestionToken)
                    {
                        // type comes from the initializer expression on a property with a `?` - add `| undefined` to the type
                        return AddUndefinedIfDefinitelyRequired(expr);
                    }

                    return expr;
                }

                // fallback to NoResult if PseudoTypeKindInferred without error nodes
            }
        }

        return PseudoType.NewPseudoTypeNoResult(node);
    }

    private PseudoType TypeFromVariable(VariableDeclaration declaration)
    {
        var t = declaration.Type;
        if (t is not null)
        {
            return PseudoType.NewPseudoTypeDirect(t);
        }

        var init = declaration.Initializer;
        if (init is not null && declaration.Symbol is not null &&
            (declaration.Symbol.Declarations!.Length == 1 || CountWhere(declaration.Symbol.Declarations, IsVariableDeclaration) == 1))
        {
            if (!IsContextuallyTyped(declaration))
            {
                // TODO: also should bail on expando declarations; reuse syntactic expando check used in declaration emit
                // TODO: Strada forces an inference fallback on `const` variables with template expression initializers, to leave space for template literal freshness in the future
                if (IsVarConst(declaration) && IsTemplateExpression(init))
                {
                    return PseudoType.NewPseudoTypeNoResult(declaration);
                }

                var expr = TypeFromExpression(init);
                if (expr.Kind != PseudoTypeKind.Inferred || expr.AsPseudoTypeInferred().ErrorNodes!.Length > 0)
                {
                    return expr;
                }

                // fallback to NoResult if PseudoTypeKindInferred without error nodes
            }
        }

        return PseudoType.NewPseudoTypeNoResult(declaration);
    }

    private PseudoType TypeFromAccessor(Node accessor)
    {
        var accessorDeclarations = GetAllAccessorDeclarationsForDeclaration(accessor, accessor.Symbol!.Declarations!);
        var accessorType = GetTypeAnnotationFromAllAccessorDeclarations(accessor, accessorDeclarations);
        if (accessorType is not null && !IsTypePredicateNode(accessorType))
        {
            return PseudoType.NewPseudoTypeDirect(accessorType);
        }

        if (accessorDeclarations.GetAccessor is not null)
        {
            var res = CreateReturnFromSignature(accessorDeclarations.GetAccessor);
            if (res.Kind == PseudoTypeKind.Inferred && res.AsPseudoTypeInferred().ErrorNodes!.Length == 0)
            {
                Node[] errorNodes = [accessorDeclarations.GetAccessor];
                if (accessorDeclarations.SetAccessor is not null)
                {
                    errorNodes = [.. errorNodes, accessorDeclarations.SetAccessor];
                }

                var inferred = res.AsPseudoTypeInferred();
                res = PseudoType.NewPseudoTypeInferredWithErrors(inferred.Expression, inferred.IsSignatureReturn, errorNodes); // Move error up to the accessor
            }

            return res;
        }

        return PseudoType.NewPseudoTypeNoResult(accessor);
    }

    private Node? GetTypeAnnotationFromAllAccessorDeclarations(Node node, AllAccessorDeclarations accessors)
    {
        var accessorType = GetTypeAnnotationFromAccessor(node);
        if (accessorType is null && node != accessors.FirstAccessor)
        {
            accessorType = GetTypeAnnotationFromAccessor(accessors.FirstAccessor);
        }

        if (accessorType is null && accessors.SecondAccessor is not null && node != accessors.SecondAccessor)
        {
            accessorType = GetTypeAnnotationFromAccessor(accessors.SecondAccessor);
        }

        return accessorType;
    }

    private static Node? GetTypeAnnotationFromAccessor(Node? node)
    {
        if (node is null)
        {
            return null;
        }

        // !!! TODO: support ripping return type off of .FullSignature
        if (node.Kind == Kind.GetAccessor)
        {
            return node.AsGetAccessorDeclaration()!.Type;
        }

        var set = node.AsSetAccessorDeclaration()!;
        if (set.Parameters is null || set.Parameters.Nodes is null || set.Parameters.Nodes.Length < 1)
        {
            return null;
        }

        var p = set.Parameters.Nodes[0];
        if (!IsParameterDeclaration(p))
        {
            return null;
        }

        return p.AsParameterDeclaration()!.Type;
    }

    private static bool IsValueSignatureDeclaration(Node node) =>
        IsFunctionExpression(node) || IsArrowFunction(node) || IsMethodDeclaration(node) || IsAccessor(node) ||
        IsFunctionDeclaration(node) || IsConstructorDeclaration(node);

    // does not return `null`, returns a `NoResult` pseudotype instead
    private PseudoType CreateReturnFromSignature(Node? fn)
    {
        if (fn is not null && IsFunctionLike(fn))
        {
            var d = fn.FunctionLikeData();
            // !!! TODO: support ripping return type off of .FullSignature
            var r = d?.Type;
            if (r is not null)
            {
                return PseudoType.NewPseudoTypeDirect(r);
            }
        }

        if (fn is not null && IsValueSignatureDeclaration(fn))
        {
            return TypeFromSingleReturnExpression(fn);
        }

        return PseudoType.NewPseudoTypeNoResult(fn);
    }

    private PseudoType TypeFromSingleReturnExpression(Node? fn)
    {
        Node? candidateExpr = null;
        if (fn is not null && !NodeIsMissing(fn.Body()))
        {
            var flags = GetFunctionFlags(fn);
            if ((flags & FunctionFlags.AsyncGenerator) != 0)
            {
                return PseudoType.NewPseudoTypeInferred(fn, true);
            }

            var body = fn.Body();
            if (body is not null && IsBlock(body))
            {
                ForEachReturnStatement(body, stmt =>
                {
                    if (stmt.Parent != body)
                    {
                        // Why bail on nested return statements?
                        candidateExpr = null;
                        return true;
                    }

                    if (candidateExpr is null)
                    {
                        candidateExpr = stmt.AsReturnStatement()!.Expression;
                    }
                    else
                    {
                        candidateExpr = null;
                        return true;
                    }

                    return false;
                });
            }
            else
            {
                candidateExpr = body;
            }
        }

        var candidate = candidateExpr;
        if (candidate is not null)
        {
            if (IsContextuallyTyped(candidate))
            {
                Node? t = null;
                if (candidate.Kind == Kind.TypeAssertionExpression)
                {
                    t = candidate.AsTypeAssertion()!.Type;
                }
                else if (candidate.Kind == Kind.AsExpression)
                {
                    t = candidate.AsAsExpression()!.Type;
                }

                if (t is not null && !IsConstTypeReference(t))
                {
                    return PseudoType.NewPseudoTypeDirect(t);
                }
            }
            else
            {
                return TypeFromExpression(candidate);
            }
        }

        return PseudoType.NewPseudoTypeInferred(fn, true);
    }

    // This is basically `checkExpression` for pseudotypes
    private PseudoType TypeFromExpression(Node node)
    {
        switch (node.Kind)
        {
            case Kind.OmittedExpression:
                return PseudoTypeUndefined;
            case Kind.ParenthesizedExpression:
                // assertions transformed on reparse, just unwrap
                return TypeFromExpression(node.AsParenthesizedExpression()!.Expression!);
            case Kind.Identifier:
                // !!! TODO: in strada, this uses symbol information to ensure `node` refers to the global `undefined` symbol instead
                // we should probably import `resolveName` and use it here to check for the same; but we have to setup some barebones pseudoglobals for that to work!
                if (node.AsIdentifier()!.Text == "undefined")
                {
                    return PseudoTypeUndefined;
                }

                break;
            case Kind.NullKeyword:
                return PseudoTypeNull;
            case Kind.ArrowFunction:
            case Kind.FunctionExpression:
                return TypeFromFunctionLikeExpression(node);
            case Kind.TypeAssertionExpression:
                return TypeFromTypeAssertion(node.AsTypeAssertion()!.Expression!, node.AsTypeAssertion()!.Type!);
            case Kind.AsExpression:
                return TypeFromTypeAssertion(node.AsAsExpression()!.Expression!, node.AsAsExpression()!.Type!);
            case Kind.PrefixUnaryExpression:
                if (IsPrimitiveLiteralValue(node, true))
                {
                    return TypeFromPrimitiveLiteralPrefix(node.AsPrefixUnaryExpression()!);
                }

                break;
            case Kind.ArrayLiteralExpression:
                return TypeFromArrayLiteral(node.AsArrayLiteralExpression()!);
            case Kind.ObjectLiteralExpression:
                return TypeFromObjectLiteral(node.AsObjectLiteralExpression()!);
            case Kind.ClassExpression:
                return PseudoType.NewPseudoTypeInferredWithErrors(node, false, [node]); // No possible annotation/directly mappable syntax
            case Kind.TemplateExpression:
                // templateLitWithHoles as const, not supported
                if (IsInConstContext(node))
                {
                    return PseudoType.NewPseudoTypeInferred(node, false);
                }

                return PseudoType.NewPseudoTypeMaybeConstLocation(node, PseudoType.NewPseudoTypeInferred(node, false), PseudoTypeString);
            case Kind.NumericLiteral:
                return PseudoType.NewPseudoTypeMaybeConstLocation(node, PseudoType.NewPseudoTypeNumericLiteral(node), PseudoTypeNumber);
            case Kind.NoSubstitutionTemplateLiteral:
                return PseudoType.NewPseudoTypeMaybeConstLocation(node, PseudoType.NewPseudoTypeStringLiteral(node), PseudoTypeString);
            case Kind.StringLiteral:
                return PseudoType.NewPseudoTypeMaybeConstLocation(node, PseudoType.NewPseudoTypeStringLiteral(node), PseudoTypeString);
            case Kind.BigIntLiteral:
                return PseudoType.NewPseudoTypeMaybeConstLocation(node, PseudoType.NewPseudoTypeBigIntLiteral(node), PseudoTypeBigInt);
            case Kind.TrueKeyword:
                return PseudoType.NewPseudoTypeMaybeConstLocation(node, PseudoTypeTrue, PseudoTypeBoolean);
            case Kind.FalseKeyword:
                return PseudoType.NewPseudoTypeMaybeConstLocation(node, PseudoTypeFalse, PseudoTypeBoolean);
        }

        return PseudoType.NewPseudoTypeInferred(node, false);
    }

    private PseudoType TypeFromObjectLiteral(ObjectLiteralExpression node)
    {
        var errorNodes = CanGetTypeFromObjectLiteral(node);
        if (errorNodes is not null)
        {
            return PseudoType.NewPseudoTypeInferredWithErrors(node, false, errorNodes);
        }

        // we are in a const context producing an object literal type, there are no shorthand or spread assignments
        if (node.Properties is null || node.Properties.Nodes is null || node.Properties.Nodes.Length == 0)
        {
            return PseudoType.NewPseudoTypeObjectLiteral(null);
        }

        var results = new List<PseudoObjectElement>(node.Properties.Nodes.Length);
        foreach (var e in node.Properties.Nodes)
        {
            switch (e.Kind)
            {
                case Kind.MethodDeclaration:
                    {
                        var method = e.AsMethodDeclaration()!;
                        var optional = method.PostfixToken is not null && method.PostfixToken.Kind == Kind.QuestionToken;
                        var fullSignature = e.FunctionLikeData()!.FullSignature;
                        if (fullSignature is not null)
                        {
                            results.Add(PseudoObjectElement.NewPseudoPropertyAssignment(
                                false,
                                e.Name(),
                                optional,
                                PseudoType.NewPseudoTypeDirect(fullSignature)));
                        }
                        else
                        {
                            results.Add(PseudoObjectElement.NewPseudoObjectMethod(
                                e,
                                e.Name(),
                                optional,
                                CloneTypeParameters(method.TypeParameters),
                                CloneParameters(e.ParameterList()),
                                CreateReturnFromSignature(e)));
                        }

                        break;
                    }

                case Kind.PropertyAssignment:
                    {
                        var assignment = e.AsPropertyAssignment()!;
                        results.Add(PseudoObjectElement.NewPseudoPropertyAssignment(
                            false,
                            e.Name(),
                            assignment.PostfixToken is not null && assignment.PostfixToken.Kind == Kind.QuestionToken,
                            TypeFromExpression(e.Initializer()!)));
                        break;
                    }

                case Kind.SetAccessor:
                case Kind.GetAccessor:
                    {
                        var member = GetAccessorMember(e, e.Name()!);
                        if (member is not null)
                        {
                            results.Add(member);
                        }

                        break;
                    }
            }
        }

        return PseudoType.NewPseudoTypeObjectLiteral(results.Count == 0 ? null : [.. results]);
    }

    // roughly analogous to typeFromObjectLiteralAccessor in strada
    private PseudoObjectElement? GetAccessorMember(Node accessor, Node name)
    {
        var allAccessors = GetAllAccessorDeclarationsForDeclaration(accessor, accessor.Symbol!.Declarations!); // TODO: node preservation for late-bound accessor pairs?

        // TODO: handle pseudo-annotations from get accessor return positions?
        if (allAccessors.GetAccessor is not null && allAccessors.GetAccessor.AsGetAccessorDeclaration()!.Type is not null &&
            allAccessors.SetAccessor is not null && allAccessors.SetAccessor.AsSetAccessorDeclaration()!.Parameters?.Nodes is { Length: > 0 } setParams &&
            setParams[0].AsParameterDeclaration()!.Type is not null)
        {
            // We have possible types for both accessors, we can't know if they are the same type so we keep both accessors
            if (IsGetAccessorDeclaration(accessor))
            {
                return PseudoObjectElement.NewPseudoGetAccessor(
                    accessor,
                    name,
                    false,
                    TypeFromAccessor(accessor));
            }

            return PseudoObjectElement.NewPseudoSetAccessor(
                accessor,
                name,
                false,
                CloneParameters(accessor.AsSetAccessorDeclaration()!.Parameters)![0]);
        }

        if (accessor == allAccessors.FirstAccessor)
        {
            // only one annotated accessor; output a property - `readonly` for a single `get` accessor
            var accessorType = TypeFromAccessor(accessor);
            var @readonly = IsGetAccessorDeclaration(accessor) && allAccessors.SecondAccessor is null;
            return PseudoObjectElement.NewPseudoPropertyAssignment(
                @readonly,
                name,
                false,
                accessorType);
        }

        return null;
    }

    /// <summary>
    /// Checks whether an object literal can be typed by the pseudochecker. Returns null if the object can be typed,
    /// or a slice of error nodes (shorthand/spread properties, non-literal computed names) that prevent typing.
    /// </summary>
    private static Node[]? CanGetTypeFromObjectLiteral(ObjectLiteralExpression node)
    {
        if (node.Properties is null || node.Properties.Nodes is null || node.Properties.Nodes.Length == 0)
        {
            return null; // empty object, ok
        }

        List<Node>? errorNodes = null;
        foreach (var e in node.Properties.Nodes)
        {
            if ((e.Flags & NodeFlags.ThisNodeHasError) != 0)
            {
                (errorNodes ??= []).Add(e);
                continue;
            }

            if (e.Kind == Kind.ShorthandPropertyAssignment || e.Kind == Kind.SpreadAssignment)
            {
                (errorNodes ??= []).Add(e);
                continue;
            }

            var name = e.Name()!;
            if ((name.Flags & NodeFlags.ThisNodeHasError) != 0)
            {
                (errorNodes ??= []).Add(name);
                continue;
            }

            if (name.Kind == Kind.PrivateIdentifier)
            {
                (errorNodes ??= []).Add(e);
                continue;
            }

            if (name.Kind == Kind.ComputedPropertyName)
            {
                var expression = name.Expression()!;
                if (!IsPrimitiveLiteralValue(expression, false))
                {
                    (errorNodes ??= []).Add(name);
                }
            }
        }

        return errorNodes is null ? null : [.. errorNodes];
    }

    private PseudoType TypeFromArrayLiteral(ArrayLiteralExpression node)
    {
        var errorNodes = CanGetTypeFromArrayLiteral(node);
        if (errorNodes is not null)
        {
            return PseudoType.NewPseudoTypeInferredWithErrors(node, false, errorNodes);
        }

        if (IsInConstContext(node) && IsContextuallyTyped(node))
        {
            return PseudoType.NewPseudoTypeInferred(node, false); // expr in an as const cast with a contextual type has variable readonly state, bail
        }

        // we are in a const context producing a tuple type, there are no spread elements
        var elements = node.Elements!.Nodes!;
        var results = new List<PseudoType>(elements.Length);
        foreach (var e in elements)
        {
            results.Add(TypeFromExpression(e));
        }

        return PseudoType.NewPseudoTypeTuple(results.Count == 0 ? null : [.. results]);
    }

    /// <summary>
    /// Checks whether an array literal can be typed by the pseudochecker. Returns null if the array can be typed, or a
    /// slice of error nodes that prevent typing. For non-const arrays, the error node is the array expression itself.
    /// For const arrays with spreads, the error node is the spread element.
    /// </summary>
    private static Node[]? CanGetTypeFromArrayLiteral(ArrayLiteralExpression node)
    {
        if (!IsInConstContext(node))
        {
            return [node];
        }

        foreach (var e in node.Elements!.Nodes!)
        {
            if (e.Kind == Kind.SpreadElement)
            {
                return [e];
            }
        }

        return null;
    }

    // See `isConstContext` in `checker.go` - this is basically any node kind mentioned in that
    private static bool IsConstContextPropagatingKind(Kind kind)
    {
        switch (kind)
        {
            case Kind.ArrayLiteralExpression:
            case Kind.ObjectLiteralExpression:
            case Kind.ParenthesizedExpression:
            case Kind.SpreadElement:
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
            case Kind.TemplateSpan:
            case Kind.PrefixUnaryExpression:
                return true;
        }

        return false;
    }

    /// <summary>
    /// Traverses up the parent chain to determine if the node is within a const context without needing any persistent
    /// traversal scope tracking (which could be unreliable in the presence of `typeof` queries anyway!).
    /// </summary>
    public static bool IsInConstContext(Node node)
    {
        // An expression is in a const context if an ancestor is a const type maybeAssertion expression
        var maybeAssertion = FindAncestor(
            node.Parent,
            n =>
                // stop traversing at assertions or anything not an array/object literal, since only those create or transfer const-ness
                IsAssertionExpression(n) || !IsConstContextPropagatingKind(n.Kind));
        return IsConstAssertion(maybeAssertion);
    }

    private PseudoType TypeFromPrimitiveLiteralPrefix(PrefixUnaryExpression node)
    {
        Node expr = node;
        if (node.Operator == Kind.PlusToken)
        {
            expr = node.Operand!;
        }

        var inner = node.Operand!;
        if (inner.Kind == Kind.BigIntLiteral)
        {
            return PseudoType.NewPseudoTypeMaybeConstLocation(node, PseudoType.NewPseudoTypeBigIntLiteral(expr), PseudoTypeBigInt);
        }

        if (inner.Kind == Kind.NumericLiteral)
        {
            return PseudoType.NewPseudoTypeMaybeConstLocation(node, PseudoType.NewPseudoTypeNumericLiteral(expr), PseudoTypeNumber);
        }

        Debug.FailBadSyntaxKind(inner);
        throw new System.Diagnostics.UnreachableException();
    }

    private PseudoType TypeFromTypeAssertion(Node expression, Node typeNode)
    {
        if (IsConstTypeReference(typeNode))
        {
            return TypeFromExpression(expression);
        }

        return PseudoType.NewPseudoTypeDirect(typeNode);
    }

    private PseudoType TypeFromFunctionLikeExpression(Node node)
    {
        var data = node.FunctionLikeData()!;
        if (data.FullSignature is not null)
        {
            return PseudoType.NewPseudoTypeDirect(data.FullSignature);
        }

        var returnType = CreateReturnFromSignature(node);
        var typeParameters = CloneTypeParameters(data.TypeParameters);
        var parameters = CloneParameters(data.Parameters);
        return PseudoType.NewPseudoTypeSingleCallSignature(
            node,
            parameters,
            typeParameters,
            returnType);
    }

    private static TypeParameterDeclaration[]? CloneTypeParameters(NodeList? nodes)
    {
        if (nodes?.Nodes is null || nodes.Nodes.Length == 0)
        {
            return null;
        }

        var result = new TypeParameterDeclaration[nodes.Nodes.Length];
        for (var i = 0; i < nodes.Nodes.Length; i++)
        {
            result[i] = nodes.Nodes[i].AsTypeParameterDeclaration()!;
        }

        return result;
    }

    private static bool IsUndefinedPseudoType(PseudoType t) =>
        t.Kind == PseudoTypeKind.Undefined ||
        (t.Kind == PseudoTypeKind.MaybeConstLocation && IsUndefinedPseudoType(t.AsPseudoTypeMaybeConstLocation().ConstType!));

    private static bool TypeNodeCouldReferToUndefined(Node node)
    {
        while (node.Kind == Kind.ParenthesizedType)
        {
            node = node.AsParenthesizedTypeNode()!.Type!;
        }

        switch (node.Kind)
        {
            // these types require symbolic/type resolution to know if they definitely do or do not refer to `undefined`, so might (or definitely do)
            case Kind.TypeReference:
            case Kind.IndexedAccessType:
            case Kind.TypeQuery:
            case Kind.OptionalType:
            case Kind.RestType:
            case Kind.ImportType:
                return true;
            case Kind.IntersectionType:
                // TODO: why is this not `core.Every`? strada treated unions and intersections the same, but logically every intersection member needs to contain a possible `undefined`
                // for the result type to contain `undefined`. Likely a bug persisting from strada.
                return Some(node.AsIntersectionTypeNode()!.Types!.Nodes, TypeNodeCouldReferToUndefined);
            case Kind.UnionType:
                return Some(node.AsUnionTypeNode()!.Types!.Nodes, TypeNodeCouldReferToUndefined);
            case Kind.ConditionalType: // suspect - should be treated as a union of both branches instead, likely a bug persisted from strada
                return true;
            case Kind.TypeOperator: // suspect - always refers to a subset of `string | number | symbol` for `keyof` or `symbol` for `unique`
                return true;
            case Kind.TypePredicate: // suspect - always refers to `never` or `boolean`, depending on kind - considered possibly-`undefined` referencing for strada compat
                return true;
            case Kind.UndefinedKeyword:
                return true;
            default: // all other keywords, literal types, function-y types, array/tuple types, type literals, template types, this types
                return false;
        }
    }

    // see this as the inverse of `canAddUndefined` in `expressionToTypeNode` in strada
    public static bool CouldAlreadyReferToUndefinedType(PseudoType t)
    {
        if (t.Kind == PseudoTypeKind.NoResult || t.Kind == PseudoTypeKind.Inferred || IsUndefinedPseudoType(t))
        {
            return true;
        }

        if (t.Kind == PseudoTypeKind.MaybeConstLocation)
        {
            var mc = t.AsPseudoTypeMaybeConstLocation();
            return CouldAlreadyReferToUndefinedType(mc.RegularType!); // if we're even asking this question, it's not a `const` location
        }

        if (t.Kind == PseudoTypeKind.Direct)
        {
            // inspect the direct type node
            var node = t.AsPseudoTypeDirect().TypeNode!;
            return TypeNodeCouldReferToUndefined(node);
        }

        if (t.Kind == PseudoTypeKind.Union)
        {
            return Some(t.AsPseudoTypeUnion().Types, CouldAlreadyReferToUndefinedType);
        }

        return false;
    }

    private static bool IsOptionalInitializedOrRestParameter(Node node)
    {
        var p = node.AsParameterDeclaration()!;
        return p.DotDotDotToken is not null || p.Initializer is not null || p.QuestionToken is not null;
    }

    /// <summary>
    /// Returns the index just past the last required parameter in the list. A parameter is "required" if it has no
    /// question token, no initializer, and no rest token. This is computed in a single reverse pass so callers can
    /// determine "has required parameter after index i" with `i+1 &lt; lastRequired` (equivalently,
    /// `i &lt; lastRequired-1`) in O(1).
    /// </summary>
    private static int LastRequiredParamIndex(Node[] parameters)
    {
        for (var i = parameters.Length - 1; i >= 0; i--)
        {
            if (!IsOptionalInitializedOrRestParameter(parameters[i]))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static PseudoType AddUndefinedIfDefinitelyRequired(PseudoType expr)
    {
        // If `expr` doesn't already contain `| undefined` or a direct/inferred type that may contain `undefined`, add `| undefined`
        // in Strada, this reached into the checker to see if `undefined` was necessary, using `isRequiredOptionalParameter` from the emit resolver,
        // but that's not required on top of the syntactic checks to get the same behavior. (If we get the type wrong, it'll mismatch later and be discarded
        // for an inference error since corsa actually validates that pseudotypes semantically match the inferred type the checker produces)
        if (CouldAlreadyReferToUndefinedType(expr))
        {
            return expr; // will just error later, more like than not, unless the `undefined` is explicit in the pseudo
        }

        // Explicitly add an `| undefined`
        return PseudoType.NewPseudoTypeUnion([expr, PseudoTypeUndefined]);
    }

    private PseudoType TypeFromParameter(ParameterDeclaration node)
    {
        var parent = node.Parent!;
        if (parent.Kind == Kind.SetAccessor)
        {
            return GetTypeOfAccessor(parent);
        }

        // Fast path: no initializer means we never need parameter position info.
        if (node.Initializer is null)
        {
            if (node.Type is not null)
            {
                return PseudoType.NewPseudoTypeDirect(node.Type);
            }

            return PseudoType.NewPseudoTypeNoResult(node);
        }

        var p = parent.Parameters()!;
        var selfIdx = Array.IndexOf(p, node);
        var lastRequired = LastRequiredParamIndex(p);
        return TypeFromParameterWorker(node, selfIdx, lastRequired);
    }

    private PseudoType TypeFromParameterWorker(ParameterDeclaration node, int selfIdx, int lastRequired)
    {
        var parent = node.Parent!;
        if (parent.Kind == Kind.SetAccessor)
        {
            return GetTypeOfAccessor(parent);
        }

        var hasRequiredAfter = selfIdx < lastRequired - 1;
        var declaredType = node.Type;
        if (declaredType is not null)
        {
            var result = PseudoType.NewPseudoTypeDirect(declaredType);

            // When the parameter has an initializer and strict null checks are enabled,
            // check if `| undefined` needs to be added because there are required parameters after this one.
            // This mirrors the checker's getTypeOfParameter which adds optionality for initialized parameters.
            if (_strictNullChecks && node.Initializer is not null && hasRequiredAfter)
            {
                return AddUndefinedIfDefinitelyRequired(result);
            }

            return result;
        }

        if (node.Initializer is not null && IsIdentifier(node.Name()!) && !IsContextuallyTyped(node))
        {
            var expr = TypeFromExpression(node.Initializer);
            var inferred = expr.AsPseudoTypeInferred();
            if (expr.Kind == PseudoTypeKind.Inferred && inferred.ErrorNodes!.Length == 0)
            {
                expr = PseudoType.NewPseudoTypeInferredWithErrors(inferred.Expression, false, [node]); // Move error up to the parameter
            }

            if (!_strictNullChecks)
            {
                return expr;
            }

            if (!hasRequiredAfter)
            {
                return expr;
            }

            // if there is a non-optional parameter after this one, a `| undefined` will need to explicitly be emitted on this parameter, if it's not already there
            return AddUndefinedIfDefinitelyRequired(expr);
        }

        // TODO: In strada, the ID checker doesn't infer a parameter type from binding pattern names, but the real checker _does_!
        // This means ID won't let you write, say, `({elem}) => false` without an annotation, even though it's trivially of type
        // `(p0: {elem: any}) => boolean` and error-free under `noImplicitAny: false`!
        // That limitation is retained here.
        return PseudoType.NewPseudoTypeNoResult(node);
    }

    private PseudoParameter[]? CloneParameters(NodeList? nodes)
    {
        if (nodes?.Nodes is null || nodes.Nodes.Length == 0)
        {
            return null;
        }

        var lastRequired = LastRequiredParamIndex(nodes.Nodes);
        var result = new PseudoParameter[nodes.Nodes.Length];
        for (var i = 0; i < nodes.Nodes.Length; i++)
        {
            var e = nodes.Nodes[i];
            var p = e.AsParameterDeclaration()!;
            var optional = p.QuestionToken is not null;
            if (!optional && p.Initializer is not null)
            {
                // A parameter with an initializer is optional only if all subsequent
                // parameters are also optional/have initializers/are rest parameters.
                // This matches the checker's isOptionalParameter semantics.
                optional = i >= lastRequired - 1;
            }

            result[i] = new PseudoParameter
            {
                Rest = p.DotDotDotToken is not null,
                Name = e.Name(),
                Optional = optional,
                Type = TypeFromParameterWorker(p, i, lastRequired),
            };
        }

        return result;
    }

    private static bool IsContextuallyTyped(Node node) =>
        FindAncestor(
            node.Parent,
            n =>
            {
                // Functions calls or parent type annotations (but not the return type of a function expression) may impact the inferred type and local inference is unreliable
                if (IsCallExpression(n))
                {
                    return true;
                }

                if (IsSatisfiesExpression(n))
                {
                    return true;
                }

                if ((IsVariableParameterOrProperty(n) || IsAssertionExpression(n)) && n.Type() is not null && !IsConstAssertion(n))
                {
                    return true;
                }

                return IsJsxElement(n) || IsJsxExpression(n);
            }) is not null;
}

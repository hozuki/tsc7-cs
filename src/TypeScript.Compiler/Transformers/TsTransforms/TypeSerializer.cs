// ported from: internal/transformers/tstransforms/typeserializer.go:1-511
//
// 与 Go 的差异（语言特性；其余见 docs/architecture.md §2/§3）：
// 1) Go 未导出类型 `metadataSerializer` / `metadataSerializerContext` → C# 按习惯命名
//    `MetadataSerializer` / `MetadataSerializerContext`（docs/architecture.md §2「未导出类型按 C# 习惯命名」；
//    Context 保持 struct：Go 里按值传递 + 就地改字段，C# 的 struct 语义一致）。
// 2) Go 的 `defer s.setContext(oldCtx)` → `try/finally`；`defer func() { s.c.x = old }()` 同理。
// 3) 包级函数（`newMetadataSerializer` / 导出的 `GetSetAccessorValueParameter` /
//    `getSetAccessorTypeAnnotationNode` / `getAccessorTypeNode` / `getParametersOfDecoratedDeclaration`）
//    落在静态类 `TsTransforms`（docs §2；导出者保持 public）。
// 4) `printer.TypeReferenceSerializationKindXxx` → `TypeReferenceSerializationKind.Xxx`
//    （常量名去类型前缀，docs §3.3；Printer/EmitResolver.cs 的实际枚举成员名）。
// 5) Go 的 `[]*ast.Node` → `Node[]?`；`for range nilSlice` 在 C# 里写成 `?? []`
//    （Go 的 nil 切片 len==0；`?? []` 命中 Array.Empty，不分配）。
// 6) Go 的 `debug.FailBadSyntaxKind(node)` 后跟 `return nil` → C# 保持两句（`Debug.Fail` 不是
//    [DoesNotReturn]，编译器需要显式 return）。
// 7) `ast.EntityName`（Go 类型别名 = *Node）→ C# 直接用 `Node`（docs/ast.md §2）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Transformers.Transformers;
using static TypeScript.Transformers.TsTransforms.TsTransforms;
// Go: printer.NodeFactory（与 ast.NodeFactory 同名不同包 → 显式限定，避免 CS0104）。
using NodeFactory = TypeScript.Printer.NodeFactory;

namespace TypeScript.Transformers.TsTransforms;

/// <summary>Go: <c>type metadataSerializerContext struct</c>（按值传递；Go 未导出类型 → internal）。</summary>
internal struct MetadataSerializerContext
{
    // Go 未导出字段 → `_camelCase`（跨类访问 → internal）。
    internal Node? _currentLexicalScope;
    internal Node? _currentNameScope;
    internal bool _serializingConditionalTypeBranch;
}

/// <summary>Go: <c>type metadataSerializer struct</c>（Go 未导出类型 → internal；成员名照 Go 的导出名）。</summary>
internal sealed class MetadataSerializer
{
    private readonly EmitResolver? _resolver;
    private readonly ScriptTarget _languageVersion;
    private readonly bool _strictNullChecks;
    private readonly NodeFactory _f;
    private readonly EmitContext _ec;
    private MetadataSerializerContext _c;

    internal MetadataSerializer(EmitResolver? resolver, NodeFactory f, EmitContext ec, ScriptTarget languageVersion, bool strictNullChecks)
    {
        _resolver = resolver;
        _languageVersion = languageVersion;
        _f = f;
        _ec = ec;
        _strictNullChecks = strictNullChecks;
    }

    private void SetContext(MetadataSerializerContext ctx)
    {
        _c = ctx;
    }

    public Node? SerializeTypeOfNode(MetadataSerializerContext ctx, Node node, Node container)
    {
        var oldCtx = _c;
        _c = ctx;
        try
        {
            return SerializeTypeOfNodeWorker(node, container);
        }
        finally
        {
            SetContext(oldCtx);
        }
    }

    public Node? SerializeParameterTypesOfNode(MetadataSerializerContext ctx, Node node, Node container)
    {
        var oldCtx = _c;
        _c = ctx;
        try
        {
            return SerializeParameterTypesOfNodeWorker(node, container);
        }
        finally
        {
            SetContext(oldCtx);
        }
    }

    public Node? SerializeReturnTypeOfNode(MetadataSerializerContext ctx, Node node)
    {
        var oldCtx = _c;
        _c = ctx;
        try
        {
            return SerializeReturnTypeOfNodeWorker(node);
        }
        finally
        {
            SetContext(oldCtx);
        }
    }

    /// <summary>
    /// Serializes the type of a node for use with decorator type metadata.
    /// </summary>
    private Node? SerializeTypeOfNodeWorker(Node node, Node container)
    {
        switch (node.Kind)
        {
            case Kind.PropertyDeclaration:
            case Kind.Parameter:
                return SerializeTypeNode(node.Type());
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                return SerializeTypeNode(GetAccessorTypeNode(node, container));
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.MethodDeclaration:
                return _f.NewIdentifier("Function");
            default:
                return _f.NewVoidZeroExpression();
        }
    }

    /// <summary>
    /// Serializes the type of a node for use with decorator type metadata.
    /// </summary>
    private Node? SerializeParameterTypesOfNodeWorker(Node node, Node container)
    {
        Node? valueDeclaration = null;
        if (IsClassLike(node))
        {
            valueDeclaration = GetFirstConstructorWithBody(node);
        }
        else if (IsFunctionLike(node) && NodeIsPresent(node.Body()))
        {
            valueDeclaration = node;
        }

        if (valueDeclaration is null)
        {
            return _f.NewArrayLiteralExpression(_f.NewNodeList([]), false);
        }

        var expressions = new List<Node>();
        var parameters = GetParametersOfDecoratedDeclaration(valueDeclaration, container)!.Nodes ?? [];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i]!;
            if (i == 0 && IsIdentifier(parameter.Name()!) && parameter.Name()!.Text() == "this")
            {
                continue;
            }

            if (parameter.AsParameterDeclaration()!.DotDotDotToken is not null)
            {
                expressions.Add(SerializeTypeNode(GetRestParameterElementType(parameter.Type()))!);
            }
            else
            {
                expressions.Add(SerializeTypeOfNodeWorker(parameter, container)!);
            }
        }

        return _f.NewArrayLiteralExpression(_f.NewNodeList(expressions.ToArray()), false);
    }

    /// <summary>
    /// Serializes the return type of a node for use with decorator type metadata.
    /// </summary>
    private Node? SerializeReturnTypeOfNodeWorker(Node node)
    {
        if (IsFunctionLike(node) && node.Type() is not null)
        {
            return SerializeTypeNode(node.Type());
        }
        else if (IsAsyncFunction(node))
        {
            return _f.NewIdentifier("Promise");
        }

        return _f.NewVoidZeroExpression();
    }

    /// <summary>
    /// Serializes a type node for use with decorator type metadata.
    ///
    /// Types are serialized in the following fashion:
    /// <list type="bullet">
    /// <item>Void types point to "undefined" (e.g. "void 0")</item>
    /// <item>Function and Constructor types point to the global "Function" constructor.</item>
    /// <item>Interface types with a call or construct signature types point to the global "Function" constructor.</item>
    /// <item>Array and Tuple types point to the global "Array" constructor.</item>
    /// <item>Type predicates and booleans point to the global "Boolean" constructor.</item>
    /// <item>String literal types and strings point to the global "String" constructor.</item>
    /// <item>Enum and number types point to the global "Number" constructor.</item>
    /// <item>Symbol types point to the global "Symbol" constructor.</item>
    /// <item>Type references to classes (or class-like variables) point to the constructor for the class.</item>
    /// <item>Anything else points to the global "Object" constructor.</item>
    /// </list>
    /// </summary>
    private Node? SerializeTypeNode(Node? node)
    {
        if (node is null)
        {
            return _f.NewIdentifier("Object");
        }

        node = SkipTypeParentheses(node)!;

        switch (node.Kind)
        {
            case Kind.VoidKeyword:
            case Kind.UndefinedKeyword:
            case Kind.NeverKeyword:
                return _f.NewVoidZeroExpression();
            case Kind.FunctionType:
            case Kind.ConstructorType:
                return _f.NewIdentifier("Function");
            case Kind.ArrayType:
            case Kind.TupleType:
                return _f.NewIdentifier("Array");
            case Kind.TypePredicate:
                if (node.AsTypePredicateNode()!.AssertsModifier is not null)
                {
                    return _f.NewVoidZeroExpression();
                }

                return _f.NewIdentifier("Boolean");
            case Kind.BooleanKeyword:
                return _f.NewIdentifier("Boolean");
            case Kind.TemplateLiteralType:
            case Kind.StringKeyword:
                return _f.NewIdentifier("String");
            case Kind.ObjectKeyword:
                return _f.NewIdentifier("Object");
            case Kind.LiteralType:
                return SerializeLiteralOfLiteralTypeNode(node.AsLiteralTypeNode()!.Literal!);
            case Kind.NumberKeyword:
                return _f.NewIdentifier("Number");
            case Kind.BigIntKeyword:
                return SerializeBigIntConstructor();
            case Kind.SymbolKeyword:
                return _f.NewIdentifier("Symbol");
            case Kind.TypeReference:
                return SerializeTypeReferenceNode(node.AsTypeReferenceNode()!);
            case Kind.IntersectionType:
                return SerializeUnionOrIntersectionConstituents(node.AsIntersectionTypeNode()!.Types!.Nodes, true);
            case Kind.UnionType:
                return SerializeUnionOrIntersectionConstituents(node.AsUnionTypeNode()!.Types!.Nodes, false);
            case Kind.ConditionalType:
            {
                var oldState = _c._serializingConditionalTypeBranch;
                _c._serializingConditionalTypeBranch = true;
                try
                {
                    return SerializeUnionOrIntersectionConstituents([node.AsConditionalTypeNode()!.TrueType!, node.AsConditionalTypeNode()!.FalseType!], false);
                }
                finally
                {
                    _c._serializingConditionalTypeBranch = oldState;
                }
            }

            case Kind.TypeOperator:
                if (node.AsTypeOperatorNode()!.Operator == Kind.ReadonlyKeyword)
                {
                    return SerializeTypeNode(node.Type());
                }

                // TODO: why is `unique symbol` not handled as `Symbol`? This falls back to `Object`
                break;
            case Kind.TypeQuery:
            case Kind.IndexedAccessType:
            case Kind.MappedType:
            case Kind.TypeLiteral:
            case Kind.AnyKeyword:
            case Kind.UnknownKeyword:
            case Kind.ThisType:
            case Kind.ImportType:
                // These types fall back to Object.
                break;

            // handle JSDoc types from an invalid parse
            case Kind.JSDocAllType:
            case Kind.JSDocVariadicType:
                // no meaningful serialization for these invalid-parse JSDoc types
                break;
            case Kind.JSDocNullableType:
            case Kind.JSDocNonNullableType:
            case Kind.JSDocOptionalType:
                return SerializeTypeNode(node.Type());
            default:
                Debug.FailBadSyntaxKind(node);
                return null;
        }

        return _f.NewIdentifier("Object");
    }

    private Node? SerializeUnionOrIntersectionConstituents(Node[]? types, bool isIntersection)
    {
        // Note when updating logic here also update `getEntityNameForDecoratorMetadata` in checker.ts so that aliases can be marked as referenced
        Node? serializedType = null;
        foreach (var typeNode in types ?? [])
        {
            var tn = SkipTypeParentheses(typeNode)!;
            if (tn.Kind == Kind.NeverKeyword)
            {
                if (isIntersection)
                {
                    return _f.NewVoidZeroExpression(); // Reduce to `never` in an intersection
                }

                continue; // Elide `never` in a union
            }

            if (tn.Kind == Kind.UnknownKeyword)
            {
                if (!isIntersection)
                {
                    return _f.NewIdentifier("Object"); // Reduce to `unknown` in a union
                }

                continue; // Elide `unknown` in an intersection
            }

            if (tn.Kind == Kind.AnyKeyword)
            {
                return _f.NewIdentifier("Object"); // Reduce to `any` in a union or intersection
            }

            if (!_strictNullChecks && ((IsLiteralTypeNode(tn) && tn.AsLiteralTypeNode()!.Literal!.Kind == Kind.NullKeyword) || tn.Kind == Kind.UndefinedKeyword))
            {
                continue; // Elide null and undefined from unions for metadata, just like what we did prior to the implementation of strict null checks
            }

            var serializedConstituent = SerializeTypeNode(tn)!;
            if (IsIdentifier(serializedConstituent) && serializedConstituent.AsIdentifier()!.Text == "Object")
            {
                // One of the individual is global object, return immediately
                return serializedConstituent;
            }

            // If there exists union that is not `void 0` expression, check if the the common type is identifier.
            // anything more complex and we will just default to Object
            if (serializedType is not null)
            {
                // Different types
                if (!EquateSerializedTypeNodes(serializedType, serializedConstituent))
                {
                    return _f.NewIdentifier("Object");
                }
            }
            else
            {
                // Initialize the union type
                serializedType = serializedConstituent;
            }
        }

        // If we were able to find common type, use it
        if (serializedType is not null)
        {
            return serializedType;
        }

        return _f.NewVoidZeroExpression(); // Fallback is only hit if all union constituents are null/undefined/never
    }

    private Node? SerializeLiteralOfLiteralTypeNode(Node node)
    {
        switch (node.Kind)
        {
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
                return _f.NewIdentifier("String");
            case Kind.PrefixUnaryExpression:
            {
                var operand = node.AsPrefixUnaryExpression()!.Operand!;
                switch (operand.Kind)
                {
                    case Kind.NumericLiteral:
                    case Kind.BigIntLiteral:
                        return SerializeLiteralOfLiteralTypeNode(operand);
                    default:
                        Debug.FailBadSyntaxKind(operand);
                        break;
                }

                break;
            }

            case Kind.NumericLiteral:
                return _f.NewIdentifier("Number");
            case Kind.BigIntLiteral:
                return SerializeBigIntConstructor();
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
                return _f.NewIdentifier("Boolean");
            case Kind.NullKeyword:
                return _f.NewVoidZeroExpression();
            default:
                Debug.FailBadSyntaxKind(node);
                return null;
        }

        return null;
    }

    /// <summary>
    /// Serializes a TypeReferenceNode to an appropriate JS constructor value for use with decorator type metadata.
    /// </summary>
    private Node? SerializeTypeReferenceNode(TypeReferenceNode node)
    {
        var serialScope = _c._currentNameScope;
        if (serialScope is null)
        {
            serialScope = _c._currentLexicalScope;
        }

        var kind = _resolver!.GetTypeReferenceSerializationKind(_ec.ParseNode(node.TypeName)!, _ec.ParseNode(serialScope)!);
        switch (kind)
        {
            case TypeReferenceSerializationKind.Unknown:
                // From conditional type type reference that cannot be resolved is Similar to any or unknown
                if (_c._serializingConditionalTypeBranch)
                {
                    return _f.NewIdentifier("Object");
                }

                var serialized = SerializeEntityNameAsExpressionFallback(node.TypeName!)!;
                var temp = _f.NewTempVariable();
                _ec.AddVariableDeclaration(temp);
                return _f.NewConditionalExpression(
                    _f.NewTypeCheck(_f.NewAssignmentExpression(temp, serialized), "function"),
                    _f.NewToken(Kind.QuestionToken),
                    temp,
                    _f.NewToken(Kind.ColonToken),
                    _f.NewIdentifier("Object")
                );

            case TypeReferenceSerializationKind.TypeWithConstructSignatureAndValue:
                return SerializeEntityNameAsExpression(node.TypeName!);

            case TypeReferenceSerializationKind.VoidNullableOrNeverType:
                return _f.NewVoidZeroExpression();

            case TypeReferenceSerializationKind.BigIntLikeType:
                return SerializeBigIntConstructor();

            case TypeReferenceSerializationKind.BooleanType:
                return _f.NewIdentifier("Boolean");

            case TypeReferenceSerializationKind.NumberLikeType:
                return _f.NewIdentifier("Number");

            case TypeReferenceSerializationKind.StringLikeType:
                return _f.NewIdentifier("String");

            case TypeReferenceSerializationKind.ArrayLikeType:
                return _f.NewIdentifier("Array");

            case TypeReferenceSerializationKind.ESSymbolType:
                return _f.NewIdentifier("Symbol");

            case TypeReferenceSerializationKind.TypeWithCallSignature:
                return _f.NewIdentifier("Function");

            case TypeReferenceSerializationKind.Promise:
                return _f.NewIdentifier("Promise");

            case TypeReferenceSerializationKind.ObjectType:
                return _f.NewIdentifier("Object");

            default:
                Debug.AssertNever(kind, "unknown type reference serialization kind");
                return null;
        }
    }

    private Node? SerializeBigIntConstructor()
    {
        if (_languageVersion >= ScriptTarget.ES2020)
        {
            return _f.NewIdentifier("BigInt");
        }

        return _f.NewConditionalExpression(
            _f.NewTypeCheck(_f.NewIdentifier("BigInt"), "function"),
            _f.NewToken(Kind.QuestionToken),
            _f.NewIdentifier("BigInt"),
            _f.NewToken(Kind.ColonToken),
            _f.NewIdentifier("Object")
        );
    }

    /// <summary>
    /// Serializes an entity name as an expression for decorator type metadata.
    /// </summary>
    private Node? SerializeEntityNameAsExpression(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                // Create a clone of the name with a new parent, and treat it as if it were
                // a source tree node for the purposes of the checker.
                var name = node.Clone(_f)!;
                name.Loc = node.Loc;
                _ec.UnsetOriginal(name); // make this identifier emulate a parse node, making it behave correctly when inspected by the module transforms
                name.Parent = _ec.ParseNode(_c._currentLexicalScope); // ensure the parent is set to a parse tree node.
                return name;
            case Kind.QualifiedName:
                return SerializeQualifiedNameAsExpression(node.AsQualifiedName()!);
        }

        return null;
    }

    /// <summary>
    /// Serializes an qualified name as an expression for decorator type metadata.
    /// </summary>
    private Node? SerializeQualifiedNameAsExpression(QualifiedName node)
    {
        return _f.NewPropertyAccessExpression(SerializeEntityNameAsExpression(node.Left!)!, null, node.Right, NodeFlags.None);
    }

    /// <summary>
    /// Serializes an entity name which may not exist at runtime, but whose access shouldn't throw.
    /// </summary>
    private Node? SerializeEntityNameAsExpressionFallback(Node node)
    {
        if (node.Kind == Kind.Identifier)
        {
            // A -> typeof A !== "undefined" && A
            var copied = SerializeEntityNameAsExpression(node)!;
            return CreateCheckedValue(copied, copied);
        }

        var qualifiedName = node.AsQualifiedName()!;
        if (qualifiedName.Left!.Kind == Kind.Identifier)
        {
            // A.B -> typeof A !== "undefined" && A.B
            return CreateCheckedValue(SerializeEntityNameAsExpression(qualifiedName.Left)!, SerializeEntityNameAsExpression(node)!);
        }

        // A.B.C -> typeof A !== "undefined" && (_a = A.B) !== void 0 && _a.C
        var left = SerializeEntityNameAsExpressionFallback(qualifiedName.Left)!;
        var temp = _f.NewTempVariable();
        _ec.AddVariableDeclaration(temp);
        return _f.NewLogicalANDExpression(
            _f.NewLogicalANDExpression(
                left.AsBinaryExpression()!.Left!,
                _f.NewStrictInequalityExpression(_f.NewAssignmentExpression(temp, left.AsBinaryExpression()!.Right!), _f.NewVoidZeroExpression())
            ),
            _f.NewPropertyAccessExpression(temp, null, qualifiedName.Right, NodeFlags.None)
        );
    }

    /// <summary>
    /// Produces an expression that results in <paramref name="right"/> if <paramref name="left"/> is not
    /// undefined at runtime:
    /// <code>
    /// typeof left !== "undefined" &amp;&amp; right
    /// </code>
    /// We use `typeof L !== "undefined"` (rather than `L !== undefined`) since `L` may not be declared.
    /// It's acceptable for this expression to result in `false` at runtime, as the result is intended to be
    /// further checked by any containing expression.
    /// </summary>
    private Node CreateCheckedValue(Node left, Node right)
    {
        return _f.NewLogicalANDExpression(
            _f.NewStrictInequalityExpression(_f.NewTypeOfExpression(left), _f.NewStringLiteral("undefined", TokenFlags.None)),
            right
        );
    }

    private bool EquateSerializedTypeNodes(Node left, Node right)
    {
        // temp vars used in fallback
        if (IsGeneratedIdentifier(_ec, left))
        {
            return IsGeneratedIdentifier(_ec, right);
        }

        // entity names
        if (IsIdentifier(left))
        {
            return IsIdentifier(right) && left.Text() == right.Text();
        }

        if (IsPropertyAccessExpression(left))
        {
            return IsPropertyAccessExpression(right) && EquateSerializedTypeNodes(left.Expression()!, right.Expression()!) && EquateSerializedTypeNodes(left.Name()!, right.Name()!);
        }

        // `void 0`
        if (IsVoidExpression(left))
        {
            return IsVoidExpression(right) && IsNumericLiteral(left.Expression()!) && IsNumericLiteral(right.Expression()!) && left.Expression()!.Text() == "0" && right.Expression()!.Text() == "0";
        }

        // `"undefined"` or `"function"` in `typeof` checks
        if (IsStringLiteral(left))
        {
            return IsStringLiteral(right) && left.Text() == right.Text();
        }

        // used in `typeof` checks for fallback
        if (IsTypeOfExpression(left))
        {
            return IsTypeOfExpression(right) && EquateSerializedTypeNodes(left.Expression()!, right.Expression()!);
        }

        // parens in `typeof` checks with temps
        if (IsParenthesizedExpression(left))
        {
            return IsParenthesizedExpression(right) && EquateSerializedTypeNodes(left.Expression()!, right.Expression()!);
        }

        // conditionals used in fallback
        if (IsConditionalExpression(left))
        {
            return IsConditionalExpression(right) &&
                EquateSerializedTypeNodes(left.AsConditionalExpression()!.Condition!, right.AsConditionalExpression()!.Condition!) &&
                EquateSerializedTypeNodes(left.AsConditionalExpression()!.WhenTrue!, right.AsConditionalExpression()!.WhenTrue!) &&
                EquateSerializedTypeNodes(left.AsConditionalExpression()!.WhenFalse!, right.AsConditionalExpression()!.WhenFalse!);
        }

        // logical binary and assignments used in fallback
        if (IsBinaryExpression(left))
        {
            return IsBinaryExpression(right) &&
                left.AsBinaryExpression()!.OperatorToken!.Kind == right.AsBinaryExpression()!.OperatorToken!.Kind &&
                EquateSerializedTypeNodes(left.AsBinaryExpression()!.Left!, right.AsBinaryExpression()!.Left!) &&
                EquateSerializedTypeNodes(left.AsBinaryExpression()!.Right!, right.AsBinaryExpression()!.Right!);
        }

        return false;
    }
}

public static partial class TsTransforms
{
    /// <summary>Go: <c>func newMetadataSerializer(resolver, f, ec, languageVersion, strictNullChecks) *metadataSerializer</c>。</summary>
    internal static MetadataSerializer NewMetadataSerializer(EmitResolver? resolver, NodeFactory f, EmitContext ec, ScriptTarget languageVersion, bool strictNullChecks)
    {
        return new MetadataSerializer(resolver, f, ec, languageVersion, strictNullChecks);
    }

    /// <summary>Go: <c>func GetSetAccessorValueParameter(node *ast.SetAccessorDeclaration) *ast.Node</c>（导出）。</summary>
    public static Node? GetSetAccessorValueParameter(SetAccessorDeclaration? node)
    {
        if (node is not null && node.Parameters?.Nodes is { Length: > 0 } parameters)
        {
            if (parameters.Length >= 2 && IsThisParameter(parameters[0]!))
            {
                return parameters[1];
            }

            return parameters[0];
        }

        return null;
    }

    /// <summary>Go: <c>func getSetAccessorTypeAnnotationNode(node *ast.SetAccessorDeclaration) *ast.Node</c>。</summary>
    internal static Node? GetSetAccessorTypeAnnotationNode(SetAccessorDeclaration? node)
    {
        var p = GetSetAccessorValueParameter(node);
        if (p is not null && p.Type() is not null)
        {
            return p.Type();
        }

        return null;
    }

    /// <summary>Go: <c>func getAccessorTypeNode(node *ast.Node, container *ast.Node) *ast.Node</c>。</summary>
    internal static Node? GetAccessorTypeNode(Node node, Node container)
    {
        var accessors = GetAllAccessorDeclarations(container.Members().AsSpan(), node);
        if (accessors.SetAccessor is not null)
        {
            return GetSetAccessorTypeAnnotationNode(accessors.SetAccessor.AsSetAccessorDeclaration());
        }

        if (accessors.GetAccessor is not null)
        {
            // Go: accessors.GetAccessor.Type（*GetAccessorDeclaration 的字段）→ C# 里 AllAccessorDeclarations
            // 的字段是 `Node?`，要回到具体节点类型才取得到字段（`Node.Type` 是方法组，会 CS0428）。
            return accessors.GetAccessor.AsGetAccessorDeclaration()!.Type;
        }

        return null;
    }

    /// <summary>Go: <c>func getParametersOfDecoratedDeclaration(node *ast.Node, container *ast.Node) *ast.NodeList</c>。</summary>
    internal static NodeList? GetParametersOfDecoratedDeclaration(Node node, Node? container)
    {
        if (container is not null && node.Kind == Kind.GetAccessor)
        {
            var acc = GetAllAccessorDeclarations(container.Members().AsSpan(), node);
            if (acc.SetAccessor is not null)
            {
                // Go: acc.SetAccessor.Parameters（*SetAccessorDeclaration 的字段）→ 同 getAccessorTypeNode 的理由。
                return acc.SetAccessor.AsSetAccessorDeclaration()!.Parameters;
            }
        }

        return node.ParameterList();
    }
}

// ported from: internal/transformers/tstransforms/metadata.go:1-390
//
// 与 Go 的差异（语言特性；其余见 docs/architecture.md §2/§3）：
// 1) `const USE_NEW_TYPE_METADATA_FORMAT = false` → **`static readonly bool`** 而不是 `const`：
//    实测 `if (constFalse) { … }` 会触发 CS0162（无法访问的代码），而本仓库 TreatWarningsAsErrors
//    （artifacts/t7-probe 验证），会直接编译失败。语义不变（该分支在 Go 里也是死代码）。
// 2) `defer tx.setParent(nil)` 系列 → `try/finally`（C# 没有 defer；LIFO 顺序照 Go 注册顺序手动嵌套）。
// 3) `NewMetadataTransformer` 是 Go 的包级函数 → 同类上的 `public static` 工厂（先例 Printer.NewPrinter）。
// 4) `core.Filter(originalNodes[restStart:], ast.IsModifier)`：Go 是零拷贝切片视图，C# 的数组区间切片
//    会复制。restStart == 0 是常见路径，这时直接传原数组（完全等价，且不分配）；否则用 `[restStart..]`。
//    另外 `Ast.IsModifier` 的形参是可空 `Node?`，不能直接做 `Func<Node,bool>` 方法组（CS8622 会被
//    TreatWarningsAsErrors 变成错误），所以传 lambda。
// 5) `len(x) > 0` / `len(x) == 0` → `x.Length > 0` / `x.Length == 0`；Go 的 nil slice 在 C# 侧用 `[]`。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.TsTransforms.TsTransforms;

namespace TypeScript.Transformers.TsTransforms;

public static partial class TsTransforms
{
    /// <summary>
    /// Go: <c>const USE_NEW_TYPE_METADATA_FORMAT = false</c>。
    /// 偏离：用 <c>static readonly</c> 而不是 <c>const</c>——`if (constFalse)` 的分支会触发 CS0162
    /// （无法访问的代码），在本仓库的 TreatWarningsAsErrors 下是编译错误
    /// （实测见 artifacts/t7-probe）。
    /// </summary>
    internal static readonly bool UseNewTypeMetadataFormat = false;
}

/// <summary>Go: <c>type MetadataTransformer struct</c>。</summary>
public sealed class MetadataTransformer : Transformer
{
    private readonly bool _legacyDecorators;
    private readonly EmitResolver? _resolver;
    private readonly ScriptTarget _languageVersion;
    private readonly bool _strictNullChecks;

    private MetadataSerializer? _serializer;
    private Node? _parent;
    private Node? _currentLexicalScope;

    private MetadataTransformer(bool legacyDecorators, EmitResolver? resolver, ScriptTarget languageVersion, bool strictNullChecks)
    {
        _legacyDecorators = legacyDecorators;
        _resolver = resolver;
        _languageVersion = languageVersion;
        _strictNullChecks = strictNullChecks;
    }

    /// <summary>Go: <c>func NewMetadataTransformer(opt *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? NewMetadataTransformer(TransformOptions opt)
    {
        var compilerOptions = opt.CompilerOptions!;
        var tx = new MetadataTransformer(
            compilerOptions.ExperimentalDecorators.IsTrue(),
            opt.EmitResolver,
            compilerOptions.GetEmitScriptTarget(),
            compilerOptions.GetStrictOptionValue(compilerOptions.StrictNullChecks));
        return tx.NewTransformer(tx.Visit, opt.Context);
    }

    private Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsDecorators) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.ClassDeclaration:
                return VisitClassDeclaration(node.AsClassDeclaration()!);
            case Kind.ClassExpression:
                return VisitClassExpression(node.AsClassExpression()!);
            case Kind.PropertyDeclaration:
                return VisitPropertyDeclaration(node.AsPropertyDeclaration()!);
            case Kind.MethodDeclaration:
                return VisitMethodDeclaration(node.AsMethodDeclaration()!);
            case Kind.SetAccessor:
                return VisitSetAccessor(node.AsSetAccessorDeclaration()!);
            case Kind.GetAccessor:
                return VisitGetAccessor(node.AsGetAccessorDeclaration()!);
            case Kind.SourceFile:
                _parent = null;
                try
                {
                    _currentLexicalScope = node;
                    try
                    {
                        _serializer = NewMetadataSerializer(_resolver, Factory(), EmitContext(), _languageVersion, _strictNullChecks);
                        var updated = Visitor().VisitEachChild(node)!;
                        EmitContext().AddEmitHelper(updated, EmitContext().ReadEmitHelpers());
                        return updated;
                    }
                    finally
                    {
                        SetCurrentLexicalScope(null);
                    }
                }
                finally
                {
                    SetParent(null);
                }

            case Kind.ModuleBlock:
            case Kind.Block:
            case Kind.CaseBlock:
            {
                var oldScope = _currentLexicalScope;
                _currentLexicalScope = node;
                try
                {
                    return Visitor().VisitEachChild(node);
                }
                finally
                {
                    SetCurrentLexicalScope(oldScope);
                }
            }

            default:
                return Visitor().VisitEachChild(node);
        }
    }

    private void SetParent(Node? node)
    {
        _parent = node;
    }

    private void SetCurrentLexicalScope(Node? node)
    {
        _currentLexicalScope = node;
    }

    private Node? VisitClassExpression(ClassExpression node)
    {
        var oldParent = _parent;
        _parent = node;
        try
        {
            if (!ClassOrConstructorParameterIsDecorated(_legacyDecorators, node))
            {
                return Visitor().VisitEachChild(node);
            }

            var modifiers = InjectClassTypeMetadata(Visitor().VisitModifiers(node.Modifiers()), node);
            return Factory().UpdateClassExpression(
                node,
                modifiers,
                Visitor().VisitNode(node.Name()),
                Visitor().VisitNodes(node.TypeParameters),
                Visitor().VisitNodes(node.HeritageClauses),
                Visitor().VisitNodes(node.Members));
        }
        finally
        {
            SetParent(oldParent);
        }
    }

    private Node? VisitClassDeclaration(ClassDeclaration node)
    {
        var oldParent = _parent;
        _parent = node;
        try
        {
            if (!ClassOrConstructorParameterIsDecorated(_legacyDecorators, node))
            {
                return Visitor().VisitEachChild(node);
            }

            var modifiers = InjectClassTypeMetadata(Visitor().VisitModifiers(node.Modifiers()), node);
            return Factory().UpdateClassDeclaration(
                node,
                modifiers,
                Visitor().VisitNode(node.Name()),
                Visitor().VisitNodes(node.TypeParameters),
                Visitor().VisitNodes(node.HeritageClauses),
                Visitor().VisitNodes(node.Members));
        }
        finally
        {
            SetParent(oldParent);
        }
    }

    private Node? VisitPropertyDeclaration(PropertyDeclaration node)
    {
        if (!HasDecorators(node))
        {
            return Visitor().VisitEachChild(node);
        }

        var modifiers = InjectClassElementTypeMetadata(Visitor().VisitModifiers(node.Modifiers()), node, _parent);
        return Factory().UpdatePropertyDeclaration(
            node,
            modifiers,
            Visitor().VisitNode(node.Name()),
            Visitor().VisitNode(node.PostfixToken),
            Visitor().VisitNode(node.Type),
            Visitor().VisitNode(node.Initializer));
    }

    private Node? VisitMethodDeclaration(MethodDeclaration node)
    {
        if (!HasDecorators(node) && GetDecoratorsOfParameters(node).Length == 0)
        {
            return Visitor().VisitEachChild(node);
        }

        var modifiers = InjectClassElementTypeMetadata(Visitor().VisitModifiers(node.Modifiers()), node, _parent);
        return Factory().UpdateMethodDeclaration(
            node,
            modifiers,
            Visitor().VisitNode(node.AsteriskToken),
            Visitor().VisitNode(node.Name()),
            Visitor().VisitNode(node.PostfixToken),
            Visitor().VisitNodes(node.TypeParameters),
            Visitor().VisitNodes(node.Parameters),
            Visitor().VisitNode(node.Type),
            Visitor().VisitNode(node.FullSignature),
            Visitor().VisitNode(node.Body));
    }

    private Node? VisitSetAccessor(SetAccessorDeclaration node)
    {
        if (!HasDecorators(node) && GetDecoratorsOfParameters(node).Length == 0)
        {
            return Visitor().VisitEachChild(node);
        }

        var modifiers = InjectClassElementTypeMetadata(Visitor().VisitModifiers(node.Modifiers()), node, _parent);
        return Factory().UpdateSetAccessorDeclaration(
            node,
            modifiers,
            Visitor().VisitNode(node.Name()),
            Visitor().VisitNodes(node.TypeParameters),
            Visitor().VisitNodes(node.Parameters),
            Visitor().VisitNode(node.Type),
            Visitor().VisitNode(node.FullSignature),
            Visitor().VisitNode(node.Body));
    }

    private Node? VisitGetAccessor(GetAccessorDeclaration node)
    {
        if (!HasDecorators(node))
        {
            return Visitor().VisitEachChild(node);
        }

        var modifiers = InjectClassElementTypeMetadata(Visitor().VisitModifiers(node.Modifiers()), node, _parent);
        return Factory().UpdateGetAccessorDeclaration(
            node,
            modifiers,
            Visitor().VisitNode(node.Name()),
            Visitor().VisitNodes(node.TypeParameters),
            Visitor().VisitNodes(node.Parameters),
            Visitor().VisitNode(node.Type),
            Visitor().VisitNode(node.FullSignature),
            Visitor().VisitNode(node.Body));
    }

    private ModifierList? InjectClassTypeMetadata(ModifierList? list, Node node)
    {
        var metadata = GetTypeMetadata(node, node);
        if (metadata.Length > 0)
        {
            Node[]? originalNodes = null;
            if (list is not null)
            {
                originalNodes = list.Nodes;
            }

            if (originalNodes is null || originalNodes.Length == 0)
            {
                var res = Factory().NewModifierList(metadata);
                if (list is not null)
                {
                    res.Loc = list.Loc;
                }

                return res;
            }

            var modifiersArray = new List<Node>();
            if (IsModifier(originalNodes[0]) && (originalNodes[0].Kind == Kind.DefaultKeyword || originalNodes[0].Kind == Kind.ExportKeyword))
            {
                modifiersArray.Add(originalNodes[0]);
                if (originalNodes.Length > 1 && (originalNodes[1].Kind == Kind.DefaultKeyword || originalNodes[1].Kind == Kind.ExportKeyword))
                {
                    modifiersArray.Add(originalNodes[1]);
                }
            }

            var restStart = modifiersArray.Count;
            var decos = Filter(originalNodes, IsDecorator);
            modifiersArray.AddRange(decos);
            modifiersArray.AddRange(metadata);
            // Go: originalNodes[restStart:]（切片视图）→ restStart == 0 时等价于原数组，避免 [0..] 的复制。
            var rest = restStart == 0 ? originalNodes : originalNodes[restStart..];
            var otherModifiers = Filter(rest, n => IsModifier(n));
            modifiersArray.AddRange(otherModifiers);
            var res2 = Factory().NewModifierList(modifiersArray.ToArray());
            res2.Loc = list!.Loc;
            return res2;
        }

        return list;
    }

    private ModifierList? InjectClassElementTypeMetadata(ModifierList? list, Node node, Node? container)
    {
        if (container is null || !IsClassLike(container))
        {
            return list;
        }

        if (!ClassElementOrClassElementParameterIsDecorated(_legacyDecorators, node, container))
        {
            return list;
        }

        var metadata = GetTypeMetadata(node, container);
        if (metadata.Length > 0)
        {
            Node[]? originalNodes = null;
            if (list is not null)
            {
                originalNodes = list.Nodes;
            }

            if (originalNodes is null || originalNodes.Length == 0)
            {
                var res = Factory().NewModifierList(metadata);
                if (list is not null)
                {
                    res.Loc = list.Loc;
                }

                return res;
            }

            var modifiersArray = new List<Node>();
            var decos = Filter(originalNodes, IsDecorator);
            modifiersArray.AddRange(decos);
            modifiersArray.AddRange(metadata);
            var modifiers = Filter(originalNodes, n => IsModifier(n));
            modifiersArray.AddRange(modifiers);
            var res2 = Factory().NewModifierList(modifiersArray.ToArray());
            res2.Loc = list!.Loc;
            return res2;
        }

        return list;
    }

    /// <summary>Gets optional type metadata for a declaration.</summary>
    private Node[] GetTypeMetadata(Node node, Node container)
    {
        // Decorator metadata is not yet supported for ES decorators.
        if (!_legacyDecorators)
        {
            return [];
        }

        if (UseNewTypeMetadataFormat)
        {
            return GetNewTypeMetadata(node, container);
        }

        return GetOldTypeMetadata(node, container);
    }

    private Node[] GetOldTypeMetadata(Node node, Node container)
    {
        var decorators = new List<Node>();
        if (ShouldAddTypeMetadata(node))
        {
            var typeMetadata = Factory().NewMetadataHelper("design:type", _serializer!.SerializeTypeOfNode(new MetadataSerializerContext { _currentLexicalScope = _currentLexicalScope, _currentNameScope = container }, node, container)!);
            decorators.Add(Factory().NewDecorator(typeMetadata));
        }

        if (ShouldAddParamTypesMetadata(node))
        {
            var paramTypesMetadata = Factory().NewMetadataHelper("design:paramtypes", _serializer!.SerializeParameterTypesOfNode(new MetadataSerializerContext { _currentLexicalScope = _currentLexicalScope, _currentNameScope = container }, node, container)!);
            decorators.Add(Factory().NewDecorator(paramTypesMetadata));
        }

        if (ShouldAddReturnTypeMetadata(node))
        {
            var returnTypeMetadata = Factory().NewMetadataHelper("design:returntype", _serializer!.SerializeReturnTypeOfNode(new MetadataSerializerContext { _currentLexicalScope = _currentLexicalScope, _currentNameScope = container }, node)!);
            decorators.Add(Factory().NewDecorator(returnTypeMetadata));
        }

        return decorators.ToArray();
    }

    private Node[] GetNewTypeMetadata(Node node, Node container)
    {
        var properties = new List<Node>();
        if (ShouldAddTypeMetadata(node))
        {
            properties.Add(Factory().NewPropertyAssignment(
                null,
                Factory().NewIdentifier("type"),
                null,
                null,
                Factory().NewArrowFunction(
                    null,
                    null,
                    Factory().NewNodeList([]),
                    null,
                    null,
                    Factory().NewToken(Kind.EqualsGreaterThanToken),
                    _serializer!.SerializeTypeOfNode(new MetadataSerializerContext { _currentLexicalScope = _currentLexicalScope, _currentNameScope = container }, node, container))));
        }

        if (ShouldAddParamTypesMetadata(node))
        {
            properties.Add(Factory().NewPropertyAssignment(
                null,
                Factory().NewIdentifier("paramTypes"),
                null,
                null,
                Factory().NewArrowFunction(
                    null,
                    null,
                    Factory().NewNodeList([]),
                    null,
                    null,
                    Factory().NewToken(Kind.EqualsGreaterThanToken),
                    _serializer!.SerializeParameterTypesOfNode(new MetadataSerializerContext { _currentLexicalScope = _currentLexicalScope, _currentNameScope = container }, node, container))));
        }

        if (ShouldAddReturnTypeMetadata(node))
        {
            properties.Add(Factory().NewPropertyAssignment(
                null,
                Factory().NewIdentifier("returnType"),
                null,
                null,
                Factory().NewArrowFunction(
                    null,
                    null,
                    Factory().NewNodeList([]),
                    null,
                    null,
                    Factory().NewToken(Kind.EqualsGreaterThanToken),
                    _serializer!.SerializeReturnTypeOfNode(new MetadataSerializerContext { _currentLexicalScope = _currentLexicalScope, _currentNameScope = container }, node))));
        }

        if (properties.Count > 0)
        {
            var typeInfoMetadata = Factory().NewMetadataHelper("design:typeinfo", Factory().NewObjectLiteralExpression(Factory().NewNodeList(properties.ToArray()), true));
            return [Factory().NewDecorator(typeInfoMetadata)];
        }

        return [];
    }

    /// <summary>
    /// Determines whether to emit the "design:type" metadata based on the node's kind.
    /// The caller should have already tested whether the node has decorators and whether the
    /// emitDecoratorMetadata compiler option is set.
    /// </summary>
    private bool ShouldAddTypeMetadata(Node node)
    {
        switch (node.Kind)
        {
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.PropertyDeclaration:
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether to emit the "design:returntype" metadata based on the node's kind.
    /// The caller should have already tested whether the node has decorators and whether the
    /// emitDecoratorMetadata compiler option is set.
    /// </summary>
    private bool ShouldAddReturnTypeMetadata(Node node)
    {
        return node.Kind == Kind.MethodDeclaration;
    }

    /// <summary>
    /// Determines whether to emit the "design:paramtypes" metadata based on the node's kind.
    /// The caller should have already tested whether the node has decorators and whether the
    /// emitDecoratorMetadata compiler option is set.
    /// </summary>
    private bool ShouldAddParamTypesMetadata(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                return GetFirstConstructorWithBody(node) is not null;
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                return true;
        }

        return false;
    }
}

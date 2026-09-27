// ported from: internal/checker/nodebuilderimpl.go:24-348（节点构造器的数据模型、核心工具与截断/展开判定）
//
// 与 Go 的差异（逐条）：
// 1) 包级函数（`newNodeBuilderImpl`/`getAccessStack`/`isClassInstanceSide`/`isIdentifierTypeReference`/
//    `arrayIsHomogeneous`/`typesAreSameReference`）→ 同一个 `public sealed partial class Checker` 里的
//    `internal static` 成员（docs/checker-plan.md §5.5；C# 没有自由函数）。
// 2) Go 未导出字段 → `_camelCase`（§5.5）：`b.ctx`→`_ctx`、`b.ch`→`_ch`、`b.f`→`_f`、`b.e`→`_e`、`b.pc`→`_pc`、
//    `b.links`→`_links`、`b.symbolLinks`→`_symbolLinks`、`b.idToSymbol`→`_idToSymbol`；
//    `t.flags`→`t._flags`、`t.objectFlags`→`t._objectFlags`、`t.symbol`→`t._symbol`、`t.alias`→`t._alias`。
// 3) 同名方法冲突（artifacts/checker-method-collisions.md）：`c.getTypeArguments`→`GetTypeArgumentsWorker`、
//    `c.getDeclaredTypeOfClassOrInterface` 无冲突保持 PascalCase（Go 导出的那几个保留原名）。
// 4) Go 的 `map[K]V` 零值可读 → C# 用 `Dictionary` + 显式初始化/`TryGetValue`。
// 5) `b._ctx._typeStack = append(...)` → `List<T>.Add`；`b._ctx._typeStack[:n]` → `RemoveRange`。
// 6) Go 的 `*string` 哨兵字段 → C# `string?`（引用类型即指针）。
// 7) `ast.NodeVisitorHooks{}` → `new NodeVisitorHooks()`；`b.cloneBindingName` 方法组 → 委托参数。
// 8) Go 的 `for i := range len(slice) - 1` → `for (var i = 0; i < count - 1; i++)`。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.JsNum;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.PseudoChecker;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Checker.Checker;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;
using AstNodeFactory = TypeScript.Ast.NodeFactory;

internal readonly struct CompositeSymbolIdentity
{
    internal readonly bool _isConstructorNode;
    internal readonly SymbolId _symbolId;
    internal readonly NodeId _nodeId;

    internal CompositeSymbolIdentity(bool isConstructorNode, SymbolId symbolId, NodeId nodeId)
    {
        _isConstructorNode = isConstructorNode;
        _symbolId = symbolId;
        _nodeId = nodeId;
    }
}

internal sealed class TrackedSymbolArgs
{
    internal Symbol _symbol = null!;
    internal Node? _enclosingDeclaration;
    internal SymbolFlags _meaning;
}

internal sealed class SerializedTypeEntry
{
    internal Node? _node;
    internal bool _truncating;
    internal int _addedLength;
    internal TrackedSymbolArgs[]? _trackedSymbols;
}

internal readonly struct CompositeTypeCacheIdentity
{
    internal readonly TypeId _typeId;
    internal readonly NodeBuilderFlags _flags;
    internal readonly NodeBuilderInternalFlags _internalFlags;

    internal CompositeTypeCacheIdentity(TypeId typeId, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags)
    {
        _typeId = typeId;
        _flags = flags;
        _internalFlags = internalFlags;
    }
}

internal sealed class NodeBuilderLinks
{
    /// <summary>Collection of types serialized at this location</summary>
    internal Dictionary<CompositeTypeCacheIdentity, SerializedTypeEntry>? _serializedTypes;

    /// <summary>If present, this is a fake scope injected into an enclosing declaration chain.</summary>
    internal string? _fakeScopeForSignatureDeclaration;
}

internal sealed class NodeBuilderSymbolLinks
{
    internal global::TypeScript.Module.ModeAwareCache<ModuleSpecifierResult>? _specifierCache;
}

internal sealed class ModuleSpecifierResult
{
    internal string _specifier = "";
    internal Type? _importAttributesType;
}

internal sealed class NodeBuilderContext
{
    internal Host? _host;
    internal SymbolTracker _tracker = null!;
    internal int _approximateLength;
    internal int _maxTruncationLength;
    internal bool _encounteredError;
    internal bool _truncating;
    internal bool _reportedDiagnostic;
    internal NodeBuilderFlags _flags;
    internal NodeBuilderInternalFlags _internalFlags;
    internal int _depth;
    internal int _maxExpansionDepth; // -1 means no expansion, 0+ = verbosity levels
    internal List<Type> _typeStack = null!;
    internal bool _canIncreaseExpansionDepth;
    internal bool _expansionTruncated;
    internal Node? _enclosingDeclaration;
    internal SourceFile? _enclosingFile;
    internal Type[]? _inferTypeParameters = null!;
    internal Set<TypeId> _visitedTypes = null!;
    internal Dictionary<CompositeSymbolIdentity, int> _symbolDepth = null!;
    internal List<TrackedSymbolArgs> _trackedSymbols = null!;
    internal TypeMapper? _mapper;
    internal List<Symbol> _reverseMappedStack = null!;
    internal Dictionary<SymbolId, Type> _enclosingSymbolTypes = null!;
    internal bool _suppressReportInferenceFallback;
    internal Dictionary<SymbolId, Symbol> _remappedSymbolReferences = null!;

    // per signature scope state
    internal CopyOnWriteMap<TypeId, Identifier> _typeParameterNames = null!;
    internal CopyOnWriteSet<string> _typeParameterNamesByText = null!;
    internal CopyOnWriteMap<string, int> _typeParameterNamesByTextNextNameCount = null!;
    internal CopyOnWriteSet<SymbolId> _typeParameterSymbolList = null!;
}

internal sealed partial class NodeBuilderImpl
{
    // host members
    internal AstNodeFactory _f = null!;
    internal Checker _ch = null!;
    internal EmitContext _e = null!;
    internal PseudoChecker _pc = null!;

    // cache
    internal LinkStore<Node, NodeBuilderLinks> _links = null!;
    internal LinkStore<Symbol, NodeBuilderSymbolLinks> _symbolLinks = null!;

    // state
    internal NodeBuilderContext? _ctx;

    // reusable visitor
    internal NodeVisitor _cloneBindingNameVisitor = null!;

    // symbols for synthesized identifiers, needed for e.g. inlay hints
    internal Dictionary<Node, Symbol> _idToSymbol = null!;

    /// <summary>Node builder utility functions (Go: <c>newNodeBuilderImpl</c>)。</summary>
    internal static NodeBuilderImpl NewNodeBuilderImpl(Checker ch, EmitContext e, Dictionary<Node, Symbol>? idToSymbol)
    {
        idToSymbol ??= [];
        var b = new NodeBuilderImpl
        {
            _f = e.Factory.AsNodeFactory(),
            _ch = ch,
            _e = e,
            _idToSymbol = idToSymbol,
            _pc = global::TypeScript.PseudoChecker.PseudoChecker.NewPseudoChecker(ch._strictNullChecks, ch._exactOptionalPropertyTypes),
            _links = new LinkStore<Node, NodeBuilderLinks>(),
            _symbolLinks = new LinkStore<Symbol, NodeBuilderSymbolLinks>(),
        };
        b._cloneBindingNameVisitor = new NodeVisitor(b.CloneBindingName, b._f, new NodeVisitorHooks());
        return b;
    }

    internal Action SaveRestoreFlags()
    {
        var savedFlags = _ctx!._flags;
        var savedInternalFlags = _ctx!._internalFlags;
        var savedDepth = _ctx!._depth;

        return () =>
        {
            _ctx!._flags = savedFlags;
            _ctx!._internalFlags = savedInternalFlags;
            _ctx!._depth = savedDepth;
        };
    }

    internal bool CheckTruncationLength()
    {
        if (_ctx!._truncating)
        {
            return _ctx!._truncating;
        }

        int maxLength;
        if ((_ctx!._flags & NodeBuilderFlags.NoTruncation) != 0)
        {
            maxLength = NoTruncationMaximumTruncationLength;
        }
        else if (_ctx!._maxTruncationLength > 0)
        {
            maxLength = _ctx!._maxTruncationLength;
        }
        else
        {
            maxLength = DefaultMaximumTruncationLength;
        }

        _ctx!._truncating = _ctx!._approximateLength > maxLength;
        return _ctx!._truncating;
    }

    /// <summary>
    /// checkTruncationLengthIfExpanding returns true if maxExpansionDepth >= 0 and truncation length exceeded.
    /// When expanding, we need to mark the output as truncated so we know not to offer further expansion.
    /// </summary>
    internal bool CheckTruncationLengthIfExpanding()
    {
        if (_ctx!._maxExpansionDepth >= 0 && CheckTruncationLength())
        {
            _ctx!._expansionTruncated = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// isExpandableType reports whether t has a named representation that could be inlined
    /// as its structural form during hover expansion. Filters out lib types.
    /// When isAlias is true, checks whether t's alias symbol is from user code (not lib).
    /// </summary>
    internal bool IsExpandableType(Type t, bool isAlias)
    {
        if (isAlias)
        {
            return !_ch.IsLibSymbolForHoverVerbosity(t._alias?.Symbol());
        }

        if (_ch.IsLibTypeForHoverVerbosity(t))
        {
            return false;
        }

        var objectFlags = t._objectFlags;
        if ((t._flags & TypeFlags.EnumLike) != 0 ||
            (objectFlags & ObjectFlags.Reference) != 0 ||
            (objectFlags & ObjectFlags.ClassOrInterface) != 0)
        {
            return true;
        }

        if ((objectFlags & ObjectFlags.Anonymous) != 0 && t._symbol is not null &&
            (t._symbol.Flags & (SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.ValueModule | SymbolFlags.Function | SymbolFlags.Method)) != 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// isTypeOnStack reports whether t is already being processed in the current expansion,
    /// excluding the last element (which is the type currently being serialized by typeToTypeNode).
    /// </summary>
    internal bool IsTypeOnStack(Type t)
    {
        for (var i = 0; i < _ctx!._typeStack.Count - 1; i++)
        {
            if (_ctx!._typeStack[i] == t)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// shouldExpandType decides whether to expand this type at the current depth.
    /// Returns true when depth &lt; maxExpansionDepth (expand now).
    /// At the boundary (depth == maxExpansionDepth), sets canIncreaseExpansionDepth
    /// to signal that a higher verbosity level would reveal more detail.
    /// Returns false when expansion is disabled (maxExpansionDepth &lt; 0), the type
    /// is not expandable, or the type is cyclic.
    /// </summary>
    internal bool ShouldExpandType(Type t, bool isAlias)
    {
        if (_ctx!._maxExpansionDepth < 0)
        {
            return false;
        }

        if (!IsExpandableType(t, isAlias))
        {
            return false;
        }

        if (IsTypeOnStack(t))
        {
            return false;
        }

        if (_ctx!._depth < _ctx!._maxExpansionDepth)
        {
            return true;
        }

        _ctx!._canIncreaseExpansionDepth = true;
        return false;
    }

    /// <summary>
    /// isActivelyExpanding reports whether the current depth is below maxExpansionDepth,
    /// meaning type-node reuse should be skipped so typeToTypeNode can expand named types.
    /// </summary>
    internal bool IsActivelyExpanding()
    {
        return _ctx!._maxExpansionDepth > 0 && _ctx!._depth < _ctx!._maxExpansionDepth;
    }

    /// <summary>
    /// checkTypeExpandability probes whether a type (or its type arguments) could be expanded,
    /// for use after type-node reuse where shouldExpandType was never called.
    /// Delegates to shouldExpandType for the actual check, then recurses into type arguments
    /// of reference types (e.g., Apple inside Promise&lt;Apple&gt;).
    /// </summary>
    internal void CheckTypeExpandability(Type? t)
    {
        if (_ctx!._maxExpansionDepth < 0 || t is null || _ctx!._canIncreaseExpansionDepth)
        {
            return;
        }

        // Push t onto the type stack so shouldExpandType's cycle detection works correctly.
        _ctx!._typeStack.Add(t);
        try
        {
            // If t is an ancestor in the current expansion, return early to avoid unbounded recursion.
            if (IsTypeOnStack(t))
            {
                return;
            }

            if (t._alias is not null)
            {
                ShouldExpandType(t, true);
            }

            if (!_ctx!._canIncreaseExpansionDepth)
            {
                ShouldExpandType(t, false);
            }

            if (_ctx!._canIncreaseExpansionDepth)
            {
                return;
            }

            // Recurse into type arguments (e.g., check Apple in Promise<Apple>).
            if ((t._objectFlags & ObjectFlags.Reference) != 0)
            {
                foreach (var arg in _ch.GetTypeArgumentsWorker(t))
                {
                    CheckTypeExpandability(arg);
                    if (_ctx!._canIncreaseExpansionDepth)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            _ctx!._typeStack.RemoveAt(_ctx!._typeStack.Count - 1);
        }
    }

    internal Node? AppendReferenceToType(Node root, Node refNode)
    {
        if (IsImportTypeNode(root))
        {
            // first shift type arguments

            // !!! In the old emitter, an Identifier could have type arguments for use with quickinfo:
            // typeArguments := root.TypeArguments
            // qualifier := root.AsImportTypeNode().Qualifier
            // if qualifier != nil {
            // 	if ast.IsIdentifier(qualifier) {
            // 		if typeArguments != getIdentifierTypeArguments(qualifier) {
            // 			qualifier = setIdentifierTypeArguments(b.f.CloneNode(qualifier), typeArguments)
            // 		}
            // 	} else {
            // 		if typeArguments != getIdentifierTypeArguments(qualifier.Right) {
            // 			qualifier = b.f.UpdateQualifiedName(qualifier, qualifier.Left, setIdentifierTypeArguments(b.f.cloneNode(qualifier.Right), typeArguments))
            // 		}
            // 	}
            // }
            // !!! Without the above, nested type args are silently elided
            var imprt = root.AsImportTypeNode()!;
            // then move qualifiers
            var ids = GetAccessStack(refNode);
            Node? qualifier = root.AsImportTypeNode()!.Qualifier;
            foreach (var id in ids)
            {
                if (qualifier is not null)
                {
                    qualifier = _f.NewQualifiedName(qualifier, id);
                }
                else
                {
                    qualifier = id;
                }
            }

            return _f.UpdateImportTypeNode(imprt, imprt.IsTypeOf, imprt.Argument, imprt.Attributes, qualifier, refNode.TypeArgumentList());
        }
        else if (IsTypeReferenceNode(root))
        {
            var typeRef = root.AsTypeReferenceNode()!;
            if ((_ctx!._flags & NodeBuilderFlags.UseInstantiationExpressions) != 0 && typeRef.TypeArguments is not null && typeRef.TypeArguments.Nodes!.Length != 0)
            {
                var expr = CreateExpressionWithTypeArguments(CreateAccessExpression(typeRef.TypeName!), typeRef.TypeArguments);
                foreach (var id in GetAccessStack(refNode))
                {
                    expr = _f.NewPropertyAccessExpression(expr, null, id, NodeFlags.None);
                }

                return expr;
            }

            var typeName = typeRef.TypeName!;
            foreach (var id in GetAccessStack(refNode))
            {
                typeName = _f.NewQualifiedName(typeName, id);
            }

            return _f.UpdateTypeReferenceNode(root.AsTypeReferenceNode()!, typeName, refNode.TypeArgumentList());
        }

        var expr2 = CreateAccessExpression(root);
        foreach (var id in GetAccessStack(refNode))
        {
            expr2 = _f.NewPropertyAccessExpression(expr2, null, id, NodeFlags.None);
        }

        return expr2;
    }

    internal Node? CreateElidedInformationPlaceholder()
    {
        _ctx!._approximateLength += 3;
        if ((_ctx!._flags & NodeBuilderFlags.NoTruncation) == 0)
        {
            return _f.NewTypeReferenceNode(_f.NewIdentifier("..."), null /*typeArguments*/);
        }

        return _e.AddSyntheticLeadingComment(_f.NewKeywordTypeNode(Kind.AnyKeyword), Kind.MultiLineCommentTrivia, "elided", false /*hasTrailingNewLine*/);
    }
}

public sealed partial class Checker : global::TypeScript.ModuleSpecifiers.CheckerShape
{
    // Go 的 `Checker` **结构化**满足 `modulespecifiers.CheckerShape`（Go 接口隐式满足）；C# 无结构化满足，
    // 故在本 partial 声明上补接口 + 显式实现（不改 C1 的类声明行，也不改 C8 的公开方法签名）。
    Symbol? global::TypeScript.ModuleSpecifiers.CheckerShape.GetSymbolAtLocation(Node node) => GetSymbolAtLocation(node);

    Symbol? global::TypeScript.ModuleSpecifiers.CheckerShape.GetAliasedSymbol(Symbol symbol) => GetAliasedSymbol(symbol);

    /// <summary>Go: <c>const defaultMaximumTruncationLength = 160</c>（nodebuilderimpl.go:119，包级常量 → partial class Checker 的静态成员）。</summary>
    internal const int DefaultMaximumTruncationLength = 160;

    /// <summary>Go: <c>const noTruncationMaximumTruncationLength = 1_000_000</c>（nodebuilderimpl.go:120）。</summary>
    internal const int NoTruncationMaximumTruncationLength = 1_000_000;

    /// <summary>
    /// Go: <c>func arrayIsHomogeneous[T any](array []T, comparer func(a, B T) bool) bool</c>。
    /// 偏离：Go 的 `[]T` → C# `IReadOnlyList&lt;T&gt;`（唯一调用点传的是 `MultiMap.Values()` 的 `List&lt;V&gt;`，
    /// 用 `T[]` 会多一次 ToArray 分配）。
    /// </summary>
    internal static bool ArrayIsHomogeneous<T>(IReadOnlyList<T> array, Func<T, T, bool> comparer)
    {
        if (array.Count < 2)
        {
            return true;
        }

        var first = array[0];
        for (var i = 1; i < array.Count; i++)
        {
            var target = array[i];
            if (!comparer(first, target))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>func typesAreSameReference(a, b *Type) bool</c>。</summary>
    internal static bool TypesAreSameReference(Type a, Type b)
    {
        return a == b || a._symbol is not null && a._symbol == b._symbol || a._alias is not null && a._alias == b._alias;
    }

    /// <summary>Go: <c>func isClassInstanceSide(c *Checker, t *Type) bool</c>。</summary>
    internal static bool IsClassInstanceSide(Checker c, Type t)
    {
        return t._symbol is not null && (t._symbol.Flags & SymbolFlags.Class) != 0 && (t == c.GetDeclaredTypeOfClassOrInterface(t._symbol) || (t._flags & TypeFlags.Object) != 0 && (t._objectFlags & ObjectFlags.IsClassInstanceClone) != 0);
    }

    /// <summary>Go: <c>func getAccessStack(ref *ast.Node) []*ast.Node</c>（Go 的参数名 `ref` 是 C# 关键字，改名 `refNode`）。</summary>
    internal static Node[] GetAccessStack(Node refNode)
    {
        Node state = refNode.AsTypeReferenceNode()!.TypeName!;
        var ids = new List<Node>();
        while (!IsIdentifier(state))
        {
            var entity = state.AsQualifiedName()!;
            ids.Insert(0, entity.Right!);
            state = entity.Left!;
        }

        ids.Insert(0, state);
        return ids.ToArray();
    }

    /// <summary>Go: <c>func isIdentifierTypeReference(node *ast.Node) bool</c>。</summary>
    internal static bool IsIdentifierTypeReference(Node node)
    {
        return IsTypeReferenceNode(node) && IsIdentifier(node.AsTypeReferenceNode()!.TypeName!);
    }
}

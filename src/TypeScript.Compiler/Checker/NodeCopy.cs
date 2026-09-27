// ported from: internal/checker/nodecopy.go
//
// 与 Go 的差异（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (b *NodeBuilderImpl) x()` → `NodeBuilderImpl`（另一片拥有的另一个 partial 类型）上的实例方法，
//    未导出方法一律 `internal` + PascalCase（`tryReuseExistingNodeHelper` → `TryReuseExistingNodeHelper`）。
// 2) 命名：Go 未导出字段 → `_camelCase`（`b.ctx` → `b._ctx`、`ctx.trackedSymbols` → `_trackedSymbols`、
//    `nodeSymbol.ExportSymbol` 保持 PascalCase 因为 Go 里是导出字段）；Go 未导出类型 → PascalCase
//    （`recoveryBoundary` → `RecoveryBoundary`、`originalRecoveryScopeState` → `OriginalRecoveryScopeState`、
//    `wrappingTracker` → `WrappingTracker`）。
// 3) Go 的切片缩容/清空（`b.ctx.trackedSymbols = b.ctx.trackedSymbols[0:top]`、`= nil`）→ C# 的 `List<T>`：
//    `RemoveRange`（就地缩容，语义与 Go 重建切片头一致，因为该 List 不会被 `_oldTrackedSymbols` 别名引用）
//    与直接换字段（`_trackedSymbols = new List<…>()` / `= bound._oldTrackedSymbols`，对应 Go 的字段赋值）。
// 4) Go 的 `slices? ` 无；`strings.Contains(name, "/node_modules/")` → `string.Contains`（序数语义一致）。
// 5) Go 的包级函数（无接收者）→ 同一个 `partial class Checker` 的 `internal static` 成员
//    （docs/checker-plan.md §5.2）：本文件的 `GetExistingNodeTreeVisitor`/`NewWrappingTracker`。
// 6) `nodebuilder.InternalFlagsAllowUnresolvedNames` → `NodeBuilderInternalFlags.AllowUnresolvedNames`
//    （`Flags`/`InternalFlags`/`SymbolTracker` 用别名，避免与本包的 `NodeBuilder` 类型同名的命名空间解析问题）。
// 7) `b.setTextRange(range, location)` 的返回是可空的 → 调用点按 Go 的赋值语义直接用（Go 的 `*Node` 也可为 nil）。
// 8) 指针相等（Go 的 `==` on `*ast.Node`）一律写 `ReferenceEquals`。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Checker.Checker;
using static TypeScript.Core.Core;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;
using NodeBuilderSymbolTracker = TypeScript.NodeBuilder.SymbolTracker;

namespace TypeScript.Checker;

public sealed partial class NodeBuilderImpl
{
    internal Node? ReuseNode(Node? node)
    {
        if (node is null)
        {
            return node;
        }

        return TryReuseExistingNodeHelper(node);
    }

    internal Node? TryJSTypeNodeToTypeNode(Node? node)
    {
        return ReuseNode(node);
    }

    internal Node? ReuseName(Node? node, bool isMethod)
    {
        var res = ReuseNode(node);
        if (res is null)
        {
            return res;
        }

        if (!TryGetTextOfPropertyName(res, out var text))
        {
            return res;
        }

        var kind = ClassifyPropertyName(text, IsStringLiteral(res), isMethod);
        if (IsIdentifier(res) && kind == PropertyNameNodeKind.Identifier)
        {
            return res;
        }

        if (IsStringLiteral(res) && kind == PropertyNameNodeKind.StringLiteral)
        {
            return res;
        }

        Node? renamed;
        switch (kind)
        {
            case PropertyNameNodeKind.Identifier:
                renamed = NewIdentifier(text, null);
                break;
            case PropertyNameNodeKind.StringLiteral:
                renamed = _f.NewStringLiteral(text, TokenFlags.None);
                break;
            default:
                return res;
        }

        _e.SetOriginal(renamed, res);
        return SetTextRange(renamed, res);
    }

    internal Node? ReuseTypeNode(Node? node)
    {
        if (node is null)
        {
            return node;
        }

        var r = ReuseNode(node);
        if (r is not null)
        {
            // After successful reuse during hover, probe the reused AST for expandable
            // type references so canIncreaseExpansionDepth is set even though
            // typeToTypeNode (and shouldExpandType) were never called.
            if (_ctx!._maxExpansionDepth >= 0 && !_ctx!._canIncreaseExpansionDepth)
            {
                WalkNodeForExpandability(node);
            }

            return r;
        }

        _ctx!._tracker.ReportInferenceFallback(node);
        var t = GetTypeFromTypeNode(node, false);
        return TypeToTypeNode(t);
    }

    /// <summary>
    /// walkNodeForExpandability walks a reused AST node tree, calling checkTypeExpandability
    /// on each type reference, type predicate, or import type node.
    /// Short-circuits once canIncreaseExpansionDepth is set.
    /// </summary>
    private void WalkNodeForExpandability(Node? node)
    {
        if (_ctx!._canIncreaseExpansionDepth || node is null)
        {
            return;
        }

        // Check these explicitly so we look into type arguments wehther or not they are in the tree or not.
        if (IsTypeReferenceNode(node) || IsExpressionWithTypeArguments(node) || IsTypePredicateNode(node) || IsImportTypeNode(node))
        {
            var t = GetTypeFromTypeNode(node, false);
            if (t is not null)
            {
                CheckTypeExpandability(t);
                if (_ctx!._canIncreaseExpansionDepth)
                {
                    return;
                }
            }
        }

        node.ForEachChild(child =>
        {
            WalkNodeForExpandability(child);
            return _ctx!._canIncreaseExpansionDepth;
        });
    }
}

/// <summary>Go: <c>type recoveryBoundary struct</c>。</summary>
internal sealed class RecoveryBoundary
{
    internal NodeBuilderContext _ctx = null!;
    internal bool _hadError;
    internal readonly List<Action> _deferredReports = new();
    internal NodeBuilderSymbolTracker? _oldTracker;
    internal List<TrackedSymbolArgs> _oldTrackedSymbols = new();
    internal readonly List<TrackedSymbolArgs> _trackedSymbols = new();
    internal bool _oldEncounteredError;
    internal int _oldApproximateLength;

    internal void MarkError(Action? f)
    {
        _hadError = true;
        if (f is not null)
        {
            _deferredReports.Add(f);
        }
    }

    internal OriginalRecoveryScopeState StartRecoveryScope()
    {
        var trackedSymbolsTop = _ctx!._trackedSymbols.Count;
        var unreportedErrorsTop = _deferredReports.Count;
        return new OriginalRecoveryScopeState(trackedSymbolsTop, unreportedErrorsTop, _hadError);
    }

    internal void EndRecoveryScope(OriginalRecoveryScopeState state)
    {
        _hadError = state._hadError;
        _ctx!._trackedSymbols.RemoveRange(state._trackedSymbolsTop, _ctx!._trackedSymbols.Count - state._trackedSymbolsTop);
        _deferredReports.RemoveRange(state._unreportedErrorsTop, _deferredReports.Count - state._unreportedErrorsTop);
    }
}

/// <summary>Go: <c>type originalRecoveryScopeState struct</c>。</summary>
internal readonly struct OriginalRecoveryScopeState
{
    internal readonly int _trackedSymbolsTop;
    internal readonly int _unreportedErrorsTop;
    internal readonly bool _hadError;

    internal OriginalRecoveryScopeState(int trackedSymbolsTop, int unreportedErrorsTop, bool hadError)
    {
        _trackedSymbolsTop = trackedSymbolsTop;
        _unreportedErrorsTop = unreportedErrorsTop;
        _hadError = hadError;
    }
}

/// <summary>Go: <c>type wrappingTracker struct</c>。</summary>
internal sealed class WrappingTracker : NodeBuilderSymbolTracker
{
    private readonly NodeBuilderSymbolTracker _wrapped;
    private readonly RecoveryBoundary _bound;

    internal WrappingTracker(NodeBuilderSymbolTracker wrapped, RecoveryBoundary bound)
    {
        _wrapped = wrapped;
        _bound = bound;
    }

    public void PopErrorFallbackNode()
    {
        _wrapped.PopErrorFallbackNode();
    }

    public void PushErrorFallbackNode(Node node)
    {
        _wrapped.PushErrorFallbackNode(node);
    }

    public void ReportCyclicStructureError()
    {
        _bound.MarkError(_wrapped.ReportCyclicStructureError);
    }

    public void ReportInaccessibleThisError()
    {
        _bound.MarkError(_wrapped.ReportInaccessibleThisError);
    }

    public void ReportInaccessibleUniqueSymbolError()
    {
        _bound.MarkError(_wrapped.ReportInaccessibleUniqueSymbolError);
    }

    public void ReportInferenceFallback(Node node)
    {
        _wrapped.ReportInferenceFallback(node); // Should this also be deferred?
    }

    public void ReportLikelyUnsafeImportRequiredError(string specifier, string symbolName)
    {
        _bound.MarkError(() => _wrapped.ReportLikelyUnsafeImportRequiredError(specifier, symbolName));
    }

    public void ReportNonSerializableProperty(string propertyName)
    {
        _bound.MarkError(() => _wrapped.ReportNonSerializableProperty(propertyName));
    }

    public void ReportNonlocalAugmentation(SourceFile containingFile, Symbol parentSymbol, Symbol augmentingSymbol)
    {
        _wrapped.ReportNonlocalAugmentation(containingFile, parentSymbol, augmentingSymbol); // Should this also be deferred?
    }

    public void ReportPrivateInBaseOfClassExpression(string propertyName)
    {
        _bound.MarkError(() => _wrapped.ReportPrivateInBaseOfClassExpression(propertyName));
    }

    public void ReportTruncationError()
    {
        _wrapped.ReportTruncationError(); // Should this also be deferred?
    }

    public bool TrackSymbol(Symbol symbol, Node enclosingDeclaration, SymbolFlags meaning)
    {
        _bound._trackedSymbols.Add(new TrackedSymbolArgs { _symbol = symbol, _enclosingDeclaration = enclosingDeclaration, _meaning = meaning });
        return false;
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func newWrappingTracker(inner nodebuilder.SymbolTracker, bound *recoveryBoundary) *wrappingTracker</c>。</summary>
    internal static WrappingTracker NewWrappingTracker(NodeBuilderSymbolTracker inner, RecoveryBoundary bound) => new(inner, bound);
}

public sealed partial class NodeBuilderImpl
{
    private RecoveryBoundary CreateRecoveryBoundary()
    {
        _ch.CheckNotCanceled();
        var bound = new RecoveryBoundary
        {
            _ctx = _ctx!,
            _oldTracker = _ctx!._tracker,
            // Go: `oldTrackedSymbols: b.ctx.trackedSymbols` 之后把字段置 nil（换的是字段，旧切片仍完整）。
            // C# 的 `List<T>` 直接换字段即可（字段可赋值，与 Go 的切片字段语义逐一对应）。
            _oldTrackedSymbols = _ctx!._trackedSymbols,
            _oldEncounteredError = _ctx!._encounteredError,
            _oldApproximateLength = _ctx!._approximateLength,
        };
        var newTracker = NewSymbolTrackerImpl(_ctx!, NewWrappingTracker(_ctx!._tracker, bound));
        _ctx!._tracker = newTracker;
        _ctx!._trackedSymbols = new List<TrackedSymbolArgs>();
        return bound;
    }

    private bool FinalizeBoundary(RecoveryBoundary bound)
    {
        _ctx!._tracker = bound._oldTracker!;
        _ctx!._trackedSymbols = bound._oldTrackedSymbols;
        _ctx!._encounteredError = bound._oldEncounteredError;
        _ctx!._approximateLength = bound._oldApproximateLength;

        foreach (var f in bound._deferredReports)
        {
            f();
        }

        if (bound._hadError)
        {
            return false;
        }

        foreach (var a in bound._trackedSymbols)
        {
            _ctx!._tracker.TrackSymbol(a._symbol, a._enclosingDeclaration!, a._meaning);
        }

        return true;
    }

    private Node? TryReuseExistingNodeHelper(Node existing)
    {
        var bound = CreateRecoveryBoundary();
        var v = GetExistingNodeTreeVisitor(this, bound); // !!! TODO: Cache visitor and just reset bound+host builder? We try this for a *lot* of nodes.
        var transformed = v.VisitNode(existing);
        if (!FinalizeBoundary(bound))
        {
            return null;
        }

        _ctx!._approximateLength += existing.Loc.End() - existing.Loc.Pos();
        return transformed;
    }

    private string GetModuleSpecifierOverride(Node parent, Node lit)
    {
        if (_ctx!._enclosingFile != GetSourceFileOfNode(lit))
        {
            var mode = ResolutionModeNone;
            if (parent.AsImportTypeNode()!.Attributes is not null)
            {
                (mode, _) = parent.AsImportTypeNode()!.Attributes!.AsImportAttributes()!.GetResolutionModeOverride(null);
            }

            var name = lit.Text();
            var originalName = name;
            var nodeSymbol = TryGetResolvedSymbolFromTypeNode(parent);
            var meaning = SymbolFlags.Type;
            if (parent.AsImportTypeNode()!.IsTypeOf)
            {
                meaning = SymbolFlags.Value;
            }

            Symbol? parentSymbol = null;
            if (nodeSymbol is not null && _ch.IsSymbolAccessible(nodeSymbol, _ctx!._enclosingDeclaration, meaning, false).Accessibility == SymbolAccessibility.Accessible)
            {
                parentSymbol = LookupSymbolChain(nodeSymbol, meaning, true)[0];
            }

            if (parentSymbol is not null && IsExternalModuleSymbol(parentSymbol))
            {
                name = GetSpecifierForModuleSymbol(parentSymbol, mode)._specifier;
            }
            else
            {
                var targetFile = _ch.GetExternalModuleFileFromDeclaration(parent);
                if (targetFile is not null)
                {
                    name = GetSpecifierForModuleSymbol(targetFile.Symbol!, mode)._specifier;
                }
            }

            if (name.Length > 0 && name.Contains("/node_modules/"))
            {
                _ctx!._encounteredError = true;
                _ctx!._tracker.ReportLikelyUnsafeImportRequiredError(name, "");
            }

            if (name != originalName)
            {
                return name;
            }
        }

        return "";
    }

    internal Node RewriteModuleSpecifier(Node parent, Node lit)
    {
        var newName = GetModuleSpecifierOverride(parent, lit);
        if (newName.Length == 0)
        {
            return lit;
        }

        var res = _f.NewStringLiteral(newName, TokenFlags.None);
        _e.SetOriginal(res, lit);
        return res;
    }

    internal Node? GetEnclosingDeclarationIgnoringFakeScope()
    {
        var enc = _ctx!._enclosingDeclaration;
        while (enc is not null && _links.Get(enc)._fakeScopeForSignatureDeclaration is not null)
        {
            enc = enc.Parent;
        }

        return enc;
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func getExistingNodeTreeVisitor(b *NodeBuilderImpl, bound *recoveryBoundary) *ast.NodeVisitor</c>。</summary>
    internal static NodeVisitor GetExistingNodeTreeVisitor(NodeBuilderImpl b, RecoveryBoundary bound)
    {
        // TODO: wrap all these closures into methods on an object so we can guarantee we reuse the same memory on each invocation by reusing/resetting the object
        // instead of re-closing-over all of these each time we need a visitor. In theory the compiler could handle this, but in practice closure inlining hasn't been reliable
        NodeVisitor visitor = null!;

        // note: also handles renaming type parameters renamed within the current context
        Node? AttachSymbolToLeftmostIdentifier(Node leftmost, Node node, Symbol? sym)
        {
            NodeVisitor vis = null!;
            Node? VisitorFunc(Node n)
            {
                if (ReferenceEquals(n, leftmost))
                {
                    Type? type = null;
                    Node? name = null;
                    if (sym is not null)
                    {
                        type = b._ch.GetDeclaredTypeOfSymbolWorker(sym);
                        if ((sym.Flags & SymbolFlags.TypeParameter) != 0)
                        {
                            name = b.TypeParameterToName(type);
                        }
                    }

                    name ??= b.NewIdentifier(n.Text(), sym);
                    name = b.SetTextRange(name, n)!;
                    b._e.AddEmitFlags(name, EmitFlags.NoAsciiEscaping);
                    return name;
                }

                return b.SetTextRange(n.VisitEachChild(vis), n);
            }

            vis = NewNodeVisitor(VisitorFunc, b._f, new NodeVisitorHooks());
            return VisitorFunc(node);
        }

        (bool IntroducesError, Node? Node, Symbol? Sym) TrackExistingEntityName(Node node, Node? overrideEnclosing)
        {
            var enclosingDeclaration = b._ctx!._enclosingDeclaration;
            if (overrideEnclosing is not null)
            {
                enclosingDeclaration = overrideEnclosing;
            }

            var introducesError = false;
            var leftmost = GetFirstIdentifier(node);
            if (IsInJSFile(node) && (IsExportsIdentifier(leftmost!) || IsModuleExportsAccessExpression(leftmost!.Parent!) || (IsQualifiedName(leftmost!.Parent!) && IsModuleIdentifier(leftmost!.Parent!.AsQualifiedName()!.Left) && IsExportsIdentifier(leftmost!.Parent!.AsQualifiedName()!.Right))))
            {
                introducesError = true;
                return (introducesError, b.SetTextRange(b._f.DeepCloneNode(node), node), null);
            }

            var meaning = GetMeaningOfEntityNameReference(node);
            Symbol? sym;
            if (IsThisIdentifier(leftmost!))
            {
                // `this` isn't a bindable identifier - skip resolution, find a relevant `this` symbol directly and avoid exhaustive scope traversal
                sym = b._ch.GetSymbolOfDeclaration(b._ch.GetThisContainer(leftmost!, false, false));
                if (b._ch.IsSymbolAccessible(sym!, leftmost, meaning, false).Accessibility != SymbolAccessibility.Accessible)
                {
                    introducesError = true;
                    b._ctx!._tracker.ReportInaccessibleThisError();
                }

                return (introducesError, AttachSymbolToLeftmostIdentifier(leftmost!, node, sym), null);
            }

            sym = b._ch.ResolveEntityName(leftmost!, meaning, true, true, null);
            if (b._ctx!._enclosingDeclaration is not null && !(sym is not null && (sym.Flags & SymbolFlags.TypeParameter) != 0))
            {
                sym = b._ch.GetExportSymbolOfValueSymbolIfExported(sym);
                // Some declarations may be transplanted to a new location.
                // When this happens we need to make sure that the name has the same meaning at both locations
                // We also check for the unknownSymbol because when we create a fake scope some parameters may actually not be usable
                // either because they are the expanded rest parameter,
                // or because they are the newly added parameters from the tuple, which might have different meanings in the original context
                var symAtLocation = b._ch.ResolveEntityName(leftmost!, meaning, true, true, b._ctx!._enclosingDeclaration);
                if
                    // Check for unusable parameters symbols
                    (ReferenceEquals(symAtLocation, b._ch._unknownSymbol) ||
                    // If the symbol is not found, but was not found in the original scope either we probably have an error, don't reuse the node
                    (symAtLocation is null && sym is not null) ||
                    // If the symbol is found both in declaration scope and in current scope then it should point to the same reference
                    (symAtLocation is not null && sym is not null && b._ch.GetSymbolIfSameReference(b._ch.GetExportSymbolOfValueSymbolIfExported(symAtLocation)!, sym) is null))
                {
                    // In isolated declaration we will not do rest parameter expansion so there is no need to report on these.
                    if (!ReferenceEquals(symAtLocation, b._ch._unknownSymbol))
                    {
                        b._ctx!._tracker.ReportInferenceFallback(node);
                    }

                    introducesError = true;
                    return (introducesError, b.SetTextRange(b._f.DeepCloneNode(node), node), sym);
                }

                sym = symAtLocation;
            }

            if (sym is not null)
            {
                // If a parameter is resolvable in the current context it is also visible, so no need to go to symbol accesibility
                if ((sym.Flags & SymbolFlags.FunctionScopedVariable) != 0 && sym.ValueDeclaration is not null)
                {
                    if (IsPartOfParameterDeclaration(sym.ValueDeclaration) || IsJSDocParameterTag(sym.ValueDeclaration))
                    {
                        return (introducesError, AttachSymbolToLeftmostIdentifier(leftmost!, node, sym), null);
                    }
                }

                if ((sym.Flags & SymbolFlags.TypeParameter) == 0 /* Type parameters are visible in the current context if they are are resolvable */ && !IsDeclarationName(node) &&
                    b._ch.IsSymbolAccessible(sym, enclosingDeclaration, meaning, false).Accessibility != SymbolAccessibility.Accessible)
                {
                    b._ctx!._tracker.ReportInferenceFallback(node);
                    introducesError = true;
                }
                else
                {
                    b._ctx!._tracker.TrackSymbol(sym, enclosingDeclaration!, meaning);
                }

                return (introducesError, AttachSymbolToLeftmostIdentifier(leftmost!, node, sym), null);
            }

            return (introducesError, b.SetTextRange(b._f.DeepCloneNode(node), node), null);
        }

        Node? TryVisitIndexedAccess(Node node)
        {
            var resultObjectType = TryVisitSimpleTypeNode(node.AsIndexedAccessTypeNode()!.ObjectType!);
            if (resultObjectType is null)
            {
                return null;
            }

            return b.SetTextRange(b._f.UpdateIndexedAccessTypeNode(node.AsIndexedAccessTypeNode()!, resultObjectType, visitor.VisitNode(node.AsIndexedAccessTypeNode()!.IndexType)), node);
        }

        Node? TryVisitKeyOf(Node node)
        {
            var to = node.AsTypeOperatorNode()!;
            var t = TryVisitSimpleTypeNode(to.Type!);
            if (t is null)
            {
                return null;
            }

            return b.SetTextRange(b._f.UpdateTypeOperatorNode(to, to.Operator, t), node);
        }

        Node? TryVisitTypeQuery(Node node)
        {
            var (introducesError, exprName, _) = TrackExistingEntityName(node.AsTypeQueryNode()!.ExprName!, null);
            if (!introducesError)
            {
                return b.SetTextRange(b._f.UpdateTypeQueryNode(
                    node.AsTypeQueryNode()!,
                    exprName,
                    visitor.VisitNodes(node.AsTypeQueryNode()!.TypeArguments)), node);
            }

            var serializedName = b.SerializeTypeName(node.AsTypeQueryNode()!.ExprName!, true, visitor.VisitNodes(node.AsTypeQueryNode()!.TypeArguments));
            if (serializedName is not null)
            {
                return b.SetTextRange(serializedName, node.AsTypeQueryNode()!.ExprName);
            }

            return null;
        }

        Node? TryVisitTypeReference(Node node)
        {
            if (IsConstTypeReference(node))
            {
                return null;
            }

            var s = b.TryGetResolvedSymbolFromTypeNode(node);
            if (s is null)
            {
                return null; // ???
            }

            if ((s.Flags & SymbolFlags.TypeParameter) != 0)
            {
                var declaredType = b._ch.GetDeclaredTypeOfSymbolWorker(s);
                if (b._ctx!._mapper is not null && !ReferenceEquals(GetMappedType(declaredType, b._ctx!._mapper), declaredType))
                {
                    return null; // refers to type parameter remapped by context (TODO improvement: just return the remapped param name?)
                }
            }

            if (!b.CanReuseExistingJSTypeNode(node, b.GetTypeFromTypeNode(node, false)))
            {
                // fallback to serialization for jsdoc types that have insufficient or incomplete type args, or are remapped by the checker in only jsdoc contexts
                // TODO: remappings like `promise` -> `Promise<any>` are static, we *could* statically remap the nodes, too. But that only matters for `isolatedDeclarations`
                // in JS, should we enable that.
                return null;
            }

            var (introducesError, newName, _) = TrackExistingEntityName(node.AsTypeReferenceNode()!.TypeName!, null);
            if (!introducesError)
            {
                var typeArguments = visitor.VisitNodes(node.AsTypeReferenceNode()!.TypeArguments);
                return b.SetTextRange(b._f.UpdateTypeReferenceNode(
                    node.AsTypeReferenceNode()!,
                    newName,
                    typeArguments), node);
            }

            var serializedName = b.SerializeTypeName(node.AsTypeReferenceNode()!.TypeName!, false, visitor.VisitNodes(node.AsTypeReferenceNode()!.TypeArguments));
            if (serializedName is not null)
            {
                return b.SetTextRange(serializedName, node.AsTypeReferenceNode()!.TypeName);
            }

            return null;
        }

        Node? TryVisitSimpleTypeNode(Node node)
        {
            var innerNode = SkipParentheses(node)!;
            switch (innerNode.Kind)
            {
                case Kind.TypeReference:
                    return TryVisitTypeReference(innerNode);
                case Kind.TypeQuery:
                    return TryVisitTypeQuery(innerNode);
                case Kind.IndexedAccessType:
                    return TryVisitIndexedAccess(innerNode);
                case Kind.TypeOperator:
                    if (innerNode.AsTypeOperatorNode()!.Operator == Kind.KeyOfKeyword)
                    {
                        return TryVisitKeyOf(innerNode);
                    }

                    break;
            }

            return visitor.VisitNode(node);
        }

        Node? VisitExistingNodeTreeSymbolsWorker(Node node)
        {
            var factory = b._f;
            // !!! TODO: the reparser *should* make all the jsdoc remapping logic here redundant,
            // assuming we only ever try to preserve reparsed nodes and never walk back to the jsdoc "originals"
            // accidentally.
            // Still, what can be ported of the logic is here, just in case.
            // Begin JSDoc handling
            if (node.Kind == Kind.JSDocTypeExpression)
            {
                // Unwrap JSDocTypeExpressions
                return visitor.VisitNode(node.AsJSDocTypeExpression()!.Type);
            }

            // !!! TODO: We don't _actually_ support jsdoc namepath types, emit `any` instead; verify we handle as gracefully as strada
            if (node.Kind == Kind.JSDocAllType /* || node.Kind == ast.JSDocNamepathType */)
            {
                return factory.NewKeywordTypeNode(Kind.AnyKeyword);
            }

            // !!! TODO: verify JSDocUnknwonType is hopefully just parsed into `unknown` upfront; the kind no longer exists
            // if node.Kind == ast.KindJSDocUnknownType {
            // 	return factory.NewKeywordTypeNode(ast.KindUnknownKeyword)
            // }
            if (node.Kind == Kind.JSDocNullableType)
            {
                var unionMembers = new Node[]
                {
                    visitor.VisitNode(node.AsJSDocNullableType()!.Type)!,
                    factory.NewLiteralTypeNode(factory.NewKeywordExpression(Kind.NullKeyword)),
                };
                return factory.NewUnionTypeNode(factory.NewNodeList(unionMembers));
            }

            if (node.Kind == Kind.JSDocOptionalType)
            {
                var unionMembers = new Node[]
                {
                    visitor.VisitNode(node.AsJSDocOptionalType()!.Type)!,
                    factory.NewKeywordTypeNode(Kind.UndefinedKeyword),
                };
                return factory.NewUnionTypeNode(factory.NewNodeList(unionMembers));
            }

            if (node.Kind == Kind.JSDocNonNullableType)
            {
                // Unwrap
                return visitor.VisitNode(node.AsJSDocNonNullableType()!.Type);
            }

            if (node.Kind == Kind.JSDocVariadicType)
            { // !!! TODO: verify this matches how jsdoc variadics are actually handled now?
                return factory.NewArrayTypeNode(visitor.VisitNode(node.AsJSDocVariadicType()!.Type));
            }

            if (node.Kind == Kind.JSDocTypeLiteral)
            {
                var members = new List<Node>();
                foreach (var t in node.AsJSDocTypeLiteral()!.JSDocPropertyTags ?? [])
                {
                    if (t.Kind != Kind.JSDocPropertyTag && t.Kind != Kind.JSDocParameterTag)
                    {
                        continue;
                    }

                    var n = t.Name();
                    Node? targetName;
                    if (IsIdentifier(n!))
                    {
                        targetName = n;
                    }
                    else
                    {
                        targetName = n!.AsQualifiedName()!.Right; // !!! TODO: without typesystem backup, doing this cast unguarded seems really suspect, even though it is what strada does
                    }

                    var name = visitor.VisitNode(targetName);
                    var shouldBeOptional = t.AsJSDocParameterOrPropertyTag()!.IsBracketed || (t.TypeExpression() is not null && t.TypeExpression()!.Kind == Kind.JSDocOptionalType);
                    Node? question = null;
                    if (shouldBeOptional)
                    {
                        question = factory.NewToken(Kind.QuestionToken);
                    }

                    var ty = visitor.VisitNode(t.TypeExpression()); // !!! TODO: alternate lookup locations for the type? serialize on demand if it doesn't serialze? strada does something funky here.

                    members.Add(factory.NewPropertySignatureDeclaration(null, name, question, ty, null));
                }

                return factory.NewTypeLiteralNode(factory.NewNodeList(members.ToArray()));
            }

            // if (ast.IsExpressionWithTypeArguments(node) || ast.IsTypeReferenceNode(node)) && ast.IsJSDocIndexSignature(node) { /// !!! TODO: JSDocIndexSignature handling hasn't been ported - readd if it's readded
            // 	args := node.TypeArguments()
            // 	if len(args) != 2 {
            // 		return factory.NewKeywordTypeNode(ast.KindAnyKeyword) // shouldn't be flagged as a jsdoc index signature in the first place
            // 	}
            // 	return factory.NewTypeLiteralNode(factory.NewNodeList([]*ast.Node{
            // 		factory.NewIndexSignatureDeclaration(nil, factory.NewNodeList([]*ast.Node{
            // 			factory.NewParameterDeclaration(nil, nil, factory.NewIdentifier("x"), nil, visitor.VisitNode(args[0]), nil),
            // 		}), visitor.VisitNode(args[1])),
            // 	}))
            // }
            // if node.Kind == ast.KindJSDocFunctionType {} // !!! no longer exists
            // End JSDoc handling

            if (IsTypeReferenceNode(node) && IsIdentifier(node.AsTypeReferenceNode()!.TypeName!) && node.AsTypeReferenceNode()!.TypeName!.AsIdentifier()!.Text == "")
            {
                var replacement = factory.NewKeywordTypeNode(Kind.AnyKeyword);
                b._e.SetOriginal(replacement, node);
                return replacement;
            }

            if (IsThisTypeNode(node))
            {
                // TODO: strada never marks `this` type nodes as an error - it calls `canReuseTypeNode` on it, but that function always returns `true` for `this`
                // type nodes, which in turn fails to verify that the `this` context is the same between the source and target locations. The conservative thing is to
                // _never_ copy a `this`. We could improve this, but strada is *definitely* wrong and overbroad here. (note that we're inling uses of `canReuseTypeNode`
                // in corsa because of the unfurled host structure meaning we don't need to defer to a host object for functionality it needs)
                // bound.markError(nil) // conservative approach
                return node;
            }

            if (IsTypeParameterDeclaration(node))
            {
                var (_, newName, _) = TrackExistingEntityName(node.Name()!, null);
                return factory.UpdateTypeParameterDeclaration(
                    node.AsTypeParameterDeclaration()!,
                    visitor.VisitModifiers(node.Modifiers()),
                    newName,
                    visitor.VisitNode(node.AsTypeParameterDeclaration()!.Constraint),
                    visitor.VisitNode(node.AsTypeParameterDeclaration()!.Expression),
                    visitor.VisitNode(node.AsTypeParameterDeclaration()!.DefaultType));
            }

            if (IsIndexedAccessTypeNode(node))
            {
                var result = TryVisitIndexedAccess(node);
                if (result is not null)
                {
                    return result;
                }

                bound.MarkError(null);
                return node;
            }

            if (IsTypeReferenceNode(node))
            {
                var result = TryVisitTypeReference(node);
                if (result is not null)
                {
                    return result;
                }

                bound.MarkError(null);
                return node;
            }

            if (IsTypeQueryNode(node))
            {
                var result = TryVisitTypeQuery(node);
                if (result is not null)
                {
                    return result;
                }

                bound.MarkError(null);
                return node;
            }

            if (IsTypeOperatorNode(node))
            {
                if (node.AsTypeOperatorNode()!.Operator == Kind.UniqueKeyword && node.AsTypeOperatorNode()!.Type!.Kind == Kind.SymbolKeyword)
                {
                    var nonFakeEnclosing = b.GetEnclosingDeclarationIgnoringFakeScope();
                    var sameScope = FindAncestor(node, a => ReferenceEquals(a, nonFakeEnclosing));
                    if (sameScope is null)
                    {
                        bound.MarkError(null);
                        return node;
                    }
                }
                else if (node.AsTypeOperatorNode()!.Operator == Kind.KeyOfKeyword)
                {
                    var result = TryVisitKeyOf(node);
                    if (result is not null)
                    {
                        return result;
                    }

                    bound.MarkError(null);
                    return node;
                }
            }

            if (IsLiteralImportTypeNode(node))
            {
                // assert keyword in imported attributes is deprecated, so we don't reuse types that contain it
                // Ex: import("pkg", { assert: {} }
                if (node.AsImportTypeNode()!.Attributes is not null && node.AsImportTypeNode()!.Attributes!.AsImportAttributes()!.Token == Kind.AssertKeyword)
                {
                    bound.MarkError(null);
                    return node;
                }

                var t = b.GetTypeFromTypeNode(node, true);
                if (t is null)
                {
                    bound.MarkError(null);
                    return node;
                }

                if (IsInJSFile(node))
                {
                    // !!! TODO: invalidate node reuse if js fallback logic used in type param list/typeof lookup (but isn't this logic gone?)
                    // s := b.ch.symbolNodeLinks.Get(node).resolvedSymbol
                }

                var originalSpec = node.AsImportTypeNode()!.Argument!.AsLiteralTypeNode()!.Literal;
                var specifier = b.RewriteModuleSpecifier(node, originalSpec!);
                if (ReferenceEquals(originalSpec, specifier))
                {
                    specifier = visitor.VisitNode(specifier)!; // visit node if not replaced
                }

                var arg = node.AsImportTypeNode()!.Argument;
                if (!ReferenceEquals(specifier, originalSpec))
                {
                    arg = factory.NewLiteralTypeNode(specifier);
                }

                return factory.UpdateImportTypeNode(
                    node.AsImportTypeNode()!,
                    node.AsImportTypeNode()!.IsTypeOf,
                    arg,
                    visitor.VisitNode(node.AsImportTypeNode()!.Attributes),
                    visitor.VisitNode(node.AsImportTypeNode()!.Qualifier),
                    visitor.VisitNodes(node.AsImportTypeNode()!.TypeArguments));
            }

            if (node.Name() is not null && node.Name()!.Kind == Kind.ComputedPropertyName && !b._ch.HasLateBindableName(node))
            {
                if (!HasDynamicName(node))
                {
                    // !!! TODO: This matches strada, but rather than recursing, this should probably fall down to later cases.
                    // Take a `["field"]` property declaration - it still needs a `: any` appended to it
                    return visitor.VisitEachChild(node);
                }

                // !!! TODO: this condition matches strada, but it just seems wrong? Or at the very least extraordinarily approximate, and doesn't flag a builder error...
                var shouldRemoveDeclaration = !(((b._ctx!._internalFlags & NodeBuilderInternalFlags.AllowUnresolvedNames) != 0) && IsEntityNameExpression(node.Name()!.AsComputedPropertyName()!.Expression) && (b._ch.CheckComputedPropertyName(node.Name()!)._flags & TypeFlags.Any) != 0);
                if (shouldRemoveDeclaration)
                {
                    return null;
                }
            }

            if ((IsFunctionLike(node) && node.Type() is null) || (IsPropertyDeclaration(node) && node.Type() is null && node.Initializer() is null) || (IsPropertySignatureDeclaration(node) && node.Type() is null && node.Initializer() is null) || (IsParameterDeclaration(node) && node.Type() is null && node.Initializer() is null))
            {
                var visited = visitor.VisitEachChild(node);
                if (ReferenceEquals(visited, node))
                {
                    visited = b.SetTextRange(node.Clone(factory), node);
                }

                node = visited!;
                var newType = factory.NewKeywordTypeNode(Kind.AnyKeyword);
                switch (node.Kind)
                {
                    case Kind.PropertyDeclaration:
                        return factory.UpdatePropertyDeclaration(
                            node.AsPropertyDeclaration()!,
                            node.Modifiers(),
                            node.Name(),
                            node.PostfixToken(),
                            newType,
                            null);
                    case Kind.PropertySignature:
                        return factory.UpdatePropertySignatureDeclaration(
                            node.AsPropertySignatureDeclaration()!,
                            node.Modifiers(),
                            node.Name(),
                            node.PostfixToken(),
                            newType,
                            null);
                    case Kind.Parameter:
                        return factory.UpdateParameterDeclaration(
                            node.AsParameterDeclaration()!,
                            null,
                            node.AsParameterDeclaration()!.DotDotDotToken,
                            node.Name(),
                            node.AsParameterDeclaration()!.QuestionToken,
                            newType,
                            null);
                    case Kind.MethodSignature:
                        return factory.UpdateMethodSignatureDeclaration(
                            node.AsMethodSignatureDeclaration()!,
                            node.Modifiers(),
                            node.Name(),
                            node.AsMethodSignatureDeclaration()!.PostfixToken,
                            node.AsMethodSignatureDeclaration()!.TypeParameters,
                            node.AsMethodSignatureDeclaration()!.Parameters,
                            newType);
                    case Kind.CallSignature:
                        return factory.UpdateCallSignatureDeclaration(
                            node.AsCallSignatureDeclaration()!,
                            node.AsCallSignatureDeclaration()!.TypeParameters,
                            node.AsCallSignatureDeclaration()!.Parameters,
                            newType);
                    case Kind.JSDocSignature:
                        return factory.UpdateJSDocSignature(
                            node.AsJSDocSignature()!,
                            node.AsJSDocSignature()!.TypeParameters,
                            node.AsJSDocSignature()!.Parameters,
                            newType);
                    case Kind.ConstructSignature:
                        return factory.UpdateConstructSignatureDeclaration(
                            node.AsConstructSignatureDeclaration()!,
                            node.AsConstructSignatureDeclaration()!.TypeParameters,
                            node.AsConstructSignatureDeclaration()!.Parameters,
                            newType);
                    case Kind.IndexSignature:
                        return factory.UpdateIndexSignatureDeclaration(
                            node.AsIndexSignatureDeclaration()!,
                            node.Modifiers(),
                            node.AsIndexSignatureDeclaration()!.Parameters,
                            newType);
                    case Kind.FunctionType:
                        return factory.UpdateFunctionTypeNode(
                            node.AsFunctionTypeNode()!,
                            node.AsFunctionTypeNode()!.TypeParameters,
                            node.AsFunctionTypeNode()!.Parameters,
                            newType);
                    case Kind.ConstructorType:
                        return factory.UpdateConstructorTypeNode(
                            node.AsConstructorTypeNode()!,
                            node.Modifiers(),
                            node.AsConstructorTypeNode()!.TypeParameters,
                            node.AsConstructorTypeNode()!.Parameters,
                            newType);
                }
            }

            if (IsComputedPropertyName(node) && IsEntityNameExpression(node.AsComputedPropertyName()!.Expression))
            {
                var (introducesError, result, _) = TrackExistingEntityName(node.AsComputedPropertyName()!.Expression!, null);
                if (!introducesError)
                {
                    return factory.UpdateComputedPropertyName(node.AsComputedPropertyName()!, result);
                }

                // !!! TODO: rewriting computed names based on evaluator/typecheck results?
                // strada's behavior seems hard to justify vs marking an error and moving on
                bound.MarkError(null);
                return visitor.VisitEachChild(node);
            }

            if (IsTypePredicateNode(node))
            {
                Node? parameterName;
                if (IsIdentifier(node.AsTypePredicateNode()!.ParameterName!))
                {
                    var (introducesError, result, _) = TrackExistingEntityName(node.AsTypePredicateNode()!.ParameterName!, null);
                    // Should not usually happen the only case is when a type predicate comes from a JSDoc type annotation with it's own parameter symbol definition.
                    // /** @type {(v: unknown) => v is undefined} */
                    // const isUndef = v => v === undefined;
                    if (introducesError)
                    {
                        bound.MarkError(null);
                    }

                    parameterName = result;
                }
                else
                {
                    parameterName = node.AsTypePredicateNode()!.ParameterName!.Clone(factory);
                }

                return factory.UpdateTypePredicateNode(
                    node.AsTypePredicateNode()!,
                    visitor.VisitNode(node.AsTypePredicateNode()!.AssertsModifier),
                    parameterName,
                    visitor.VisitNode(node.AsTypePredicateNode()!.Type));
            }

            if (IsConditionalTypeNode(node))
            {
                var checkType = visitor.VisitNode(node.AsConditionalTypeNode()!.CheckType);
                var dispose = b.EnterNewScope(node, null, b._ch.GetInferTypeParameters(node), null, null);
                var extendsType = visitor.VisitNode(node.AsConditionalTypeNode()!.ExtendsType);
                var trueType = visitor.VisitNode(node.AsConditionalTypeNode()!.TrueType);
                dispose();
                var falseType = visitor.VisitNode(node.AsConditionalTypeNode()!.FalseType);
                return factory.UpdateConditionalTypeNode(
                    node.AsConditionalTypeNode()!,
                    checkType,
                    extendsType,
                    trueType,
                    falseType);
            }

            // style applications
            if (IsTupleTypeNode(node) || ((b._ctx!._flags & NodeBuilderFlags.MultilineObjectLiterals) == 0 && IsTypeLiteralNode(node)) || IsMappedTypeNode(node))
            {
                // make tuples/types/mappedtypes single line
                var res = visitor.VisitEachChild(node);
                if (ReferenceEquals(res, node))
                {
                    res = res!.Clone(factory);
                    res = b.SetTextRange(res, node);
                }

                b._e.AddEmitFlags(res!, EmitFlags.SingleLine);
                return res;
            }

            if (IsStringLiteralLike(node))
            {
                // Preserve the original characters of the literal (e.g. emojis) in declaration emit
                // rather than escaping them as ASCII Unicode escapes. Mirrors TypeScript's behavior
                // for synthesized string literal types in the node builder (checker.ts:6853).
                var c = node.Clone(b._f)!;
                if (IsStringLiteral(node) && (b._ctx!._flags & NodeBuilderFlags.UseSingleQuotesForStringLiteralType) != 0 && (node.AsStringLiteral()!.TokenFlags & TokenFlags.SingleQuote) == 0)
                {
                    // set single quote on string literals
                    c.AsStringLiteral()!.TokenFlags ^= TokenFlags.SingleQuote;
                }

                b._e.AddEmitFlags(c, EmitFlags.NoAsciiEscaping);
                return c;
            }

            return visitor.VisitEachChild(node);
        }

        var nonLocalNode = true;
        visitor = NewNodeVisitor(VisitOuterNode, b._f, new NodeVisitorHooks
        {
            VisitNodes = (nodes, v) =>
            {
                var res = v.VisitNodes(nodes);
                if (nonLocalNode && res is not null)
                {
                    // Remove position data from node lists originating in other files
                    if (ReferenceEquals(res, nodes))
                    {
                        res = nodes!.Clone(b._f);
                    }

                    res!.Loc = NewTextRange(-1, -1);
                }

                return res;
            },
            VisitNode = (node, v) =>
            {
                // Capture if the current node is in the current file so node lists knoww if they can keep positions or not
                var oldNonLocalNode = nonLocalNode;
                nonLocalNode = b._ctx!._enclosingFile is null || b._ctx!._enclosingFile != GetSourceFileOfNode(b._e.MostOriginal(node));
                var res = v.VisitNode(node);
                nonLocalNode = oldNonLocalNode;
                return res;
            },
        });

        return visitor;

        Node? VisitOuterNode(Node node)
        {
            // If there was an error in a sibling node bail early, the result will be discarded anyway
            if (bound._hadError)
            {
                return node;
            }

            var recoveryScope = bound.StartRecoveryScope();
            var introducesNewScope = IsFunctionLike(node) || IsMappedTypeNode(node);
            Action? exit = null;
            if (introducesNewScope)
            {
                Symbol[]? parameters = null;
                Type[]? typeParameters = null;
                if (IsFunctionLike(node))
                {
                    var sig = b._ch.GetSignatureFromDeclarationWorker(node);
                    parameters = sig._parameters;
                    typeParameters = sig._typeParameters;
                }
                else if (IsConditionalTypeNode(node))
                { // !!! TODO: impossible in combination with the scope start check???
                    typeParameters = b._ch.GetInferTypeParameters(node);
                }
                else if (IsMappedTypeNode(node))
                {
                    typeParameters = new Type[] { b._ch.GetDeclaredTypeOfTypeParameter(b._ch.GetSymbolOfDeclaration(node.AsMappedTypeNode()!.TypeParameter!)!) };
                }

                exit = b.EnterNewScope(node, parameters, typeParameters, null, null);
            }

            var result = VisitExistingNodeTreeSymbolsWorker(node);
            exit?.Invoke();

            if (ReferenceEquals(result, node) && !NodeIsSynthesized(node))
            {
                result = b._f.DeepCloneNode(node); // always clone a new node
            }

            // We want to clone the subtree, so when we mark it up with __pos and __end in quickfixes,
            //  we don't get odd behavior because of reused nodes. We also need to clone to _remove_
            //  the position information if the node comes from a different file than the one the node builder
            //  is set to build for (even though we are reusing the node structure, the position information
            //  would make the printer print invalid spans for literals and identifiers, and the formatter would
            //  choke on the mismatched positonal spans between a parent and an injected child from another file).
            result = b.SetTextRange(result, node);

            if (bound._hadError)
            {
                if (IsTypeNode(node) && !IsTypePredicateNode(node))
                {
                    bound.EndRecoveryScope(recoveryScope);
                    // TODO: this fallback matches strada behavior, but it lacks any verification that the type from `node` actually matches
                    // the type we'd expect at this traversal position within the parent type.
                    var t = b.GetTypeFromTypeNode(node, false);
                    return b.TypeToTypeNode(t);
                }

                return b.SetTextRange(node.Clone(b._f), node);
            }

            return result;
        }
    }
}

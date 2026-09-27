// ported from: internal/checker/nodebuilder.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的包级函数（`s_implifyClassDeclaration` / `s_implifyModifiers` / `NewNodeBuilder` / `NewNodeBuilderEx`）
//    → `partial class Checker` 的 static 成员（Parser/Binder 先例，docs/checker-plan.md §5.5）；
//    `func (c *Checker) getNodeBuilder`/`getNodeBuilderEx` → 同一个 partial class 的实例方法（PascalCase）。
// 2) Go 未导出字段 → `internal _camelCase`（docs/checker-plan.md §5.5）：`_ctxStack`/`_host`/`_impl`/`_verbosity`。
// 3) `defer b.popContext()` → `try/finally`。
// 4) `nodebuilder.Flags`/`nodebuilder.InternalFlags` 用别名 `NodeBuilderFlags`/`NodeBuilderInternalFlags`
//    （同文件里还要用 `Ast.ModifierFlags`/`NodeFlags`，避免读者混淆）。
// 5) Go 的 `_ctxStack: make([]*NodeBuilderContext, 0, 1)` → `new List<NodeBuilderContext?>(1)`（元素可为 nil，
//    对应 Go 的 `b._impl._ctx == nil` 第一次入栈）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Checker.Checker;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;
using AstNodeFactory = TypeScript.Ast.NodeFactory;

public sealed partial class NodeBuilder
{
    internal List<NodeBuilderContext?> _ctxStack = null!;

    internal Host? _host;

    internal NodeBuilderImpl _impl = null!;

    internal VerbosityContext? _verbosity; // nil for non-hover callers

    /// <summary>EmitContext implements NodeBuilderInterface.</summary>
    public EmitContext EmitContext() => _impl._e;

    internal void EnterContext(Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        var verbosityLevel = -1;
        var maxTruncationLength = 0;
        if (_verbosity is not null)
        {
            verbosityLevel = _verbosity.Level;
            maxTruncationLength = _verbosity.MaxTruncationLength;
        }

        _ctxStack.Add(_impl._ctx);
        _impl._ctx = new NodeBuilderContext
        {
            _host = _host,
            _tracker = tracker!,
            _flags = flags,
            _internalFlags = internalFlags,
            _maxExpansionDepth = verbosityLevel,
            _maxTruncationLength = maxTruncationLength,
            _enclosingDeclaration = enclosingDeclaration,
            _enclosingFile = GetSourceFileOfNode(enclosingDeclaration),
            _inferTypeParameters = [],
            _symbolDepth = [],
            _trackedSymbols = [],
            _reverseMappedStack = [],
            _enclosingSymbolTypes = [],
            _remappedSymbolReferences = [],
            _visitedTypes = new Set<TypeId>(),
            _typeStack = [],
            _typeParameterNames = new CopyOnWriteMap<TypeId, Identifier>(),
            _typeParameterNamesByText = new CopyOnWriteSet<string>(),
            _typeParameterNamesByTextNextNameCount = new CopyOnWriteMap<string, int>(),
            _typeParameterSymbolList = new CopyOnWriteSet<SymbolId>(),
        };
        tracker = NewSymbolTrackerImpl(_impl._ctx, tracker);
        _impl._ctx._tracker = tracker;
    }

    /// <summary>propagateVerbosityOut copies expansion signals from the context to the VerbosityContext output.</summary>
    internal void PropagateVerbosityOut()
    {
        if (_verbosity is not null)
        {
            // Only set to true, never clear — multiple calls share the same VerbosityContext
            if (_impl._ctx!._canIncreaseExpansionDepth)
            {
                _verbosity.CanIncreaseVerbosity = true;
            }

            if (_impl._ctx._expansionTruncated)
            {
                _verbosity.Truncated = true;
            }
        }
    }

    internal void PopContext()
    {
        var stackSize = _ctxStack.Count;
        if (stackSize == 0)
        {
            _impl._ctx = null;
        }
        else
        {
            _impl._ctx = _ctxStack[stackSize - 1];
            _ctxStack.RemoveAt(stackSize - 1);
        }
    }

    internal Node? ExitContext(Node? result)
    {
        PropagateVerbosityOut();
        ExitContextCheck();
        try
        {
            if (_impl._ctx!._encounteredError)
            {
                return null;
            }

            return result;
        }
        finally
        {
            PopContext();
        }
    }

    internal Node[]? ExitContextSlice(Node[]? result)
    {
        PropagateVerbosityOut();
        ExitContextCheck();
        try
        {
            if (_impl._ctx!._encounteredError)
            {
                return null;
            }

            return result;
        }
        finally
        {
            PopContext();
        }
    }

    internal void ExitContextCheck()
    {
        if (_impl._ctx!._truncating && (_impl._ctx._flags & NodeBuilderFlags.NoTruncation) != 0)
        {
            _impl._ctx._tracker.ReportTruncationError();
        }
    }

    /// <summary>IndexInfoToIndexSignatureDeclaration implements NodeBuilderInterface.</summary>
    public Node? IndexInfoToIndexSignatureDeclaration(IndexInfo info, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.IndexInfoToIndexSignatureDeclarationHelper(info, null));
    }

    /// <summary>SerializeReturnTypeForSignature implements NodeBuilderInterface.</summary>
    public Node? SerializeReturnTypeForSignature(Node signatureDeclaration, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        var signature = _impl._ch.GetSignatureFromDeclarationWorker(signatureDeclaration);
        var cleanup = _impl.EnterSignatureScope(signature, out _);
        var result = _impl.SerializeReturnTypeForSignature(signature, true);
        cleanup();
        return ExitContext(result);
    }

    public Node[]? SerializeTypeParametersForSignature(Node signatureDeclaration, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        var symbol = _impl._ch.GetSymbolOfDeclaration(signatureDeclaration);
        var typeParams = SymbolToTypeParameterDeclarations(symbol!, enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContextSlice(typeParams);
    }

    /// <summary>SerializeTypeForDeclaration implements NodeBuilderInterface.</summary>
    public Node? SerializeTypeForDeclaration(Node? declaration, Symbol? symbol, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.SerializeTypeForDeclaration(declaration, null, symbol, true));
    }

    /// <summary>SerializeTypeForExpression implements NodeBuilderInterface.</summary>
    public Node? SerializeTypeForExpression(Node expr, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.SerializeTypeForExpression(expr));
    }

    /// <summary>SignatureToSignatureDeclaration implements NodeBuilderInterface.</summary>
    public Node? SignatureToSignatureDeclaration(Signature signature, Kind kind, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.SignatureToSignatureDeclarationHelper(signature, kind, null));
    }

    /// <summary>ExpandSymbolForHover produces declaration nodes for a symbol with expansion-level support.</summary>
    public Node[]? ExpandSymbolForHover(Symbol symbol, SymbolFlags meaning)
    {
        EnterContext(null, NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.MultilineObjectLiterals | NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope, NodeBuilderInternalFlags.None, null);

        // Push the declared type onto the type stack to prevent re-expansion.
        // We push a nil sentinel after the real type so that isTypeOnStack
        // (which skips the last element) still checks declaredType.
        var declaredType = _impl._ch.GetDeclaredTypeOfSymbolWorker(symbol);
        _impl._ctx!._typeStack.Add(declaredType);
        _impl._ctx._typeStack.Add(null!);

        var nodes = _impl.ExpandSymbolForHover(symbol);

        _impl._ctx._typeStack.RemoveRange(_impl._ctx._typeStack.Count - 2, 2);

        PropagateVerbosityOut();

        // Simplify declarations by applying original modifiers
        var result = new List<Node>(nodes.Length);
        foreach (var node in nodes)
        {
            switch (node.Kind)
            {
                case Kind.ClassDeclaration:
                    result.Add(SimplifyClassDeclaration(_impl._f, node, symbol));
                    break;
                case Kind.EnumDeclaration:
                    result.Add(SimplifyModifiers(_impl._f, node, IsEnumDeclaration, symbol));
                    break;
                case Kind.InterfaceDeclaration:
                    if ((meaning & SymbolFlags.Interface) != 0)
                    {
                        result.Add(SimplifyModifiers(_impl._f, node, IsInterfaceDeclaration, symbol));
                    }

                    break;
                case Kind.ModuleDeclaration:
                    result.Add(SimplifyModifiers(_impl._f, node, IsModuleDeclaration, symbol));
                    break;
            }
        }

        return ExitContextSlice(result.ToArray());
    }

    /// <summary>SymbolToEntityName implements NodeBuilderInterface.</summary>
    public Node? SymbolToEntityName(Symbol symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.SymbolToName(symbol, meaning, false));
    }

    /// <summary>SymbolToExpression implements NodeBuilderInterface.</summary>
    public Node? SymbolToExpression(Symbol symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.SymbolToExpression(symbol, meaning));
    }

    /// <summary>SymbolToNode implements NodeBuilderInterface.</summary>
    public Node? SymbolToNode(Symbol symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.SymbolToNode(symbol, meaning));
    }

    /// <summary>SymbolToParameterDeclaration implements NodeBuilderInterface.</summary>
    public Node? SymbolToParameterDeclaration(Symbol symbol, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.SymbolToParameterDeclaration(symbol, false));
    }

    /// <summary>SymbolToTypeParameterDeclarations implements NodeBuilderInterface.</summary>
    public Node[]? SymbolToTypeParameterDeclarations(Symbol symbol, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContextSlice(_impl.SymbolToTypeParameterDeclarations(symbol));
    }

    /// <summary>TypeParameterToDeclaration implements NodeBuilderInterface.</summary>
    public Node? TypeParameterToDeclaration(Type parameter, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.TypeParameterToDeclaration(parameter));
    }

    /// <summary>TypePredicateToTypePredicateNode implements NodeBuilderInterface.</summary>
    public Node? TypePredicateToTypePredicateNode(TypePredicate predicate, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.TypePredicateToTypePredicateNode(predicate));
    }

    /// <summary>TypeToTypeNode implements NodeBuilderInterface.</summary>
    public Node? TypeToTypeNode(Type typ, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.TypeToTypeNode(typ));
    }

    public Node? TryJSTypeNodeToTypeNode(Node node, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, SymbolTracker? tracker)
    {
        EnterContext(enclosingDeclaration, flags, internalFlags, tracker);
        return ExitContext(_impl.TryJSTypeNodeToTypeNode(node));
    }
}

/// <summary>
/// VerbosityContext controls hover-expansion behavior in the node builder.
/// A nil VerbosityContext means no expansion (non-hover callers).
/// Level 0 = default hover (maxExpansionDepth = 0; detects expandability without expanding).
/// Level 1+ = expansion enabled (maxExpansionDepth = Level).
/// </summary>
public sealed class VerbosityContext
{
    public int Level; // 0 = default (no expansion), 1+ = expansion depth

    public int MaxTruncationLength; // 0 = use default

    public bool CanIncreaseVerbosity; // output: whether increasing Level would reveal more

    public bool Truncated; // output: whether output was truncated
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func NewNodeBuilder(ch *Checker, e *printer.EmitContext) *NodeBuilder</c>。</summary>
    public static NodeBuilder NewNodeBuilder(Checker ch, EmitContext e) => NewNodeBuilderEx(ch, e, null /*idToSymbol*/);

    /// <summary>Go: <c>func NewNodeBuilderEx(ch *Checker, e *printer.EmitContext, idToSymbol map[*ast.IdentifierNode]*ast.Symbol) *NodeBuilder</c>。</summary>
    public static NodeBuilder NewNodeBuilderEx(Checker ch, EmitContext e, Dictionary<Node, Symbol>? idToSymbol)
    {
        var impl = NodeBuilderImpl.NewNodeBuilderImpl(ch, e, idToSymbol);
        return new NodeBuilder { _impl = impl, _ctxStack = new List<NodeBuilderContext?>(1), _host = ch._program };
    }

    /// <summary>Go: <c>func (c *Checker) getNodeBuilder() (*NodeBuilder, func())</c>。</summary>
    internal (NodeBuilder NodeBuilder, Action ReleaseNodes) GetNodeBuilder()
    {
        void ReleaseNodes()
        {
            _typeToStringNodebuilder!.EmitContext().Factory.ReleaseArenas(); // Allow any allocated nodes to be freed if they're no longer in a cache
        }

        if (_typeToStringNodebuilder is not null)
        {
            return (_typeToStringNodebuilder, ReleaseNodes);
        }

        _typeToStringNodebuilder = GetNodeBuilderEx(null /*idToSymbol*/);
        return (_typeToStringNodebuilder, ReleaseNodes);
    }

    /// <summary>Go: <c>func (c *Checker) getNodeBuilderEx(idToSymbol map[*ast.IdentifierNode]*ast.Symbol) *NodeBuilder</c>（nodebuilder.go:299）。</summary>
    internal NodeBuilder GetNodeBuilderEx(Dictionary<Node, Symbol>? idToSymbol)
    {
        // Go: `NewNodeBuilderEx(c, printer.NewEmitContext(), idToSymbol)` —— `NewEmitContext` 才把 Factory 接到自身；
        // `new EmitContext()` 的 Factory 为 null（printer.go:178 注释），会让 NodeBuilderImpl 构造时的 `e.Factory.AsNodeFactory()` NRE。
        return NewNodeBuilderEx(this, global::TypeScript.Printer.Printer.NewEmitContext(), idToSymbol);
    }

    /// <summary>Go: <c>func simplifyClassDeclaration(f *ast.NodeFactory, classDecl *ast.Node, symbol *ast.Symbol) *ast.Node</c>。</summary>
    internal static Node SimplifyClassDeclaration(AstNodeFactory f, Node classDecl, Symbol symbol)
    {
        var classDeclarations = Filter(symbol.Declarations, IsClassLike);
        var originalClassDecl = classDeclarations.Length > 0 ? classDeclarations[0] : classDecl;
        var modifiers = originalClassDecl.ModifierFlags & ~(ModifierFlags.Export | ModifierFlags.Ambient);
        var isAnonymous = IsClassExpression(originalClassDecl);
        if (isAnonymous)
        {
            var cd = classDecl.AsClassDeclaration()!;
            classDecl = f.UpdateClassDeclaration(
                cd,
                classDecl.Modifiers(),
                null,
                cd.TypeParameters,
                cd.HeritageClauses,
                cd.Members);
        }

        return ReplaceModifiers(f, classDecl, f.NewModifierList(CreateModifiersFromModifierFlags(modifiers, f.NewModifier)));
    }

    /// <summary>Go: <c>func simplifyModifiers(f *ast.NodeFactory, newDecl *ast.Node, isDeclKind func(*ast.Node) bool, symbol *ast.Symbol) *ast.Node</c>。</summary>
    internal static Node SimplifyModifiers(AstNodeFactory f, Node newDecl, Func<Node, bool> isDeclKind, Symbol symbol)
    {
        var decls = Filter(symbol.Declarations, isDeclKind);
        var declWithModifiers = decls.Length > 0 ? decls[0] : newDecl;
        var modifiers = declWithModifiers.ModifierFlags & ~(ModifierFlags.Export | ModifierFlags.Ambient);
        return ReplaceModifiers(f, newDecl, f.NewModifierList(CreateModifiersFromModifierFlags(modifiers, f.NewModifier)));
    }
}

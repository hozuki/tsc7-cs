// ported from: internal/transformers/declarations/tracker.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `SymbolTrackerSharedState`/`SymbolTrackerImpl` 是结构体（按指针使用）→ C# class（引用语义一致）。
// 2) Go 未导出字段 → `internal _camelCase`（跨类访问：DeclarationTransformer 读 `_watchedClassSymbol` 等）。
// 3) Go 的 `fallbackStack []*ast.Node` 的 `[:len-1]`/`append` → C# `List<Node>`（`RemoveAt(Count-1)`/`Add`），语义一致。
// 4) Go 的 `s.state.lateMarkedStatements = core.AppendIfUnique(...)` → C# `Node[]` + `Core.AppendIfUnique`（同 Go 切片语义）。
// 5) `checker.NewDiagnosticForNode` → C# `TypeScript.Checker.Checker.NewDiagnosticForNode`（全限定：本命名空间下
//    裸写 `Checker` 会解析到 `TypeScript.Checker` **命名空间**）。
// 6) `getIsolatedDeclarationError func(node) *Diagnostic` → C# `Func<Node, Diagnostic>`。
// 7) `IsBoundExpando`：Go 的 `GetReferencedValueDeclarationUnsafe` 形参是 `*ast.IdentifierNode`（= `*Node` 别名），
//    可以接收任意 `*Node`；C# 侧该接口形参是具体的 `Identifier`，故先做 `is not Identifier` 判断（非标识符时
//    Go 的解析器返回 nil → 本函数返回 false，行为一致）。
namespace TypeScript.Transformers.Declarations;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Declarations.Declarations;

/// <summary>Go: <c>type SymbolTrackerImpl struct</c>（实现 <c>checker.SymbolTracker</c>）。</summary>
public sealed class SymbolTrackerImpl : SymbolTracker
{
    internal EmitResolver _resolver = null!;
    internal SymbolTrackerSharedState _state = null!;
    internal DeclarationEmitHost _host = null!;
    internal List<Node> _fallbackStack = [];

    // For detecting class expression self-references during member serialization.
    // When set, TrackSymbol will record usage without reporting accessibility errors.
    internal Symbol? _watchedClassSymbol;
    internal bool _classSymbolTracked;

    internal Func<Node, Diagnostic> _getIsolatedDeclarationError = null!;

    /// <summary>PopErrorFallbackNode implements checker.SymbolTracker.</summary>
    public void PopErrorFallbackNode()
    {
        _fallbackStack.RemoveAt(_fallbackStack.Count - 1);
    }

    /// <summary>PushErrorFallbackNode implements checker.SymbolTracker.</summary>
    public void PushErrorFallbackNode(Node node)
    {
        _fallbackStack.Add(node);
    }

    /// <summary>ReportCyclicStructureError implements checker.SymbolTracker.</summary>
    public void ReportCyclicStructureError()
    {
        var location = ErrorLocation();
        if (location != null)
        {
            _state.AddDiagnostic(CreateDiagnosticForNode(location, Diagnostics.The_inferred_type_of_0_references_a_type_with_a_cyclic_structure_which_cannot_be_trivially_serialized_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback()));
        }
    }

    /// <summary>ReportInaccessibleThisError implements checker.SymbolTracker.</summary>
    public void ReportInaccessibleThisError()
    {
        var location = ErrorLocation();
        if (location != null)
        {
            _state.AddDiagnostic(CreateDiagnosticForNode(location, Diagnostics.The_inferred_type_of_0_references_an_inaccessible_1_type_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), "this"));
        }
    }

    /// <summary>ReportInaccessibleUniqueSymbolError implements checker.SymbolTracker.</summary>
    public void ReportInaccessibleUniqueSymbolError()
    {
        var location = ErrorLocation();
        if (location != null)
        {
            _state.AddDiagnostic(CreateDiagnosticForNode(location, Diagnostics.The_inferred_type_of_0_references_an_inaccessible_1_type_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), "unique symbol"));
        }
    }

    internal bool IsBoundExpando(Node node)
    {
        if (!(IsExpandoPropertyDeclaration(node) && IsPropertyAccessExpression(node.AsBinaryExpression()!.Left!)))
        {
            return false;
        }
        var leftmost = GetLeftmostExpression(node.AsBinaryExpression()!.Left!, true);
        if (leftmost is not Identifier leftmostIdentifier)
        {
            // Go 把任意 *Node 交给只认 *IdentifierNode（= *Node 别名）的接口；非标识符时解析器返回 nil。
            return false;
        }
        var reference = _resolver.GetReferencedValueDeclarationUnsafe(leftmostIdentifier);
        if (reference == null)
        {
            return false;
        }
        return _resolver.IsExpandoFunctionDeclarationUnsafe(reference);
    }

    internal bool IsChildOfBoundExpando(Node node)
    {
        return FindAncestorOrQuit(node, n =>
        {
            if (IsSourceFile(n) || IsBlock(n))
            {
                return FindAncestorResult.Quit;
            }
            return ToFindAncestorResult(IsBoundExpando(n));
        }) != null;
    }

    /// <summary>ReportInferenceFallback implements checker.SymbolTracker.</summary>
    public void ReportInferenceFallback(Node node)
    {
        if (!_state._isolatedDeclarations)
        {
            return;
        }
        if (GetSourceFileOfNode(node) != _state._currentSourceFile)
        {
            return; // Nested error on a declaration in another file - ignore, will be reemitted if file is in the output file set
        }
        if (_state._resolver.IsExpandoFunctionDeclarationUnsafe(node))
        { // within a node builder call that should already lock the checker, use the unsafe call
            _state._reportExpandoFunctionErrors!(node);
        }
        if (!IsChildOfBoundExpando(node))
        { // expando props get an error when their host is visited by the above, this prevents a follow-on error on a non-inferrable expression
            _state.AddDiagnostic(_getIsolatedDeclarationError(node));
        }
    }

    /// <summary>ReportLikelyUnsafeImportRequiredError implements checker.SymbolTracker.</summary>
    public void ReportLikelyUnsafeImportRequiredError(string specifier, string symbolName)
    {
        var location = ErrorLocation();
        if (location != null)
        {
            if (symbolName != "")
            {
                _state.AddDiagnostic(CreateDiagnosticForNode(location, Diagnostics.The_inferred_type_of_0_cannot_be_named_without_a_reference_to_2_from_1_This_is_likely_not_portable_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), specifier, symbolName));
            }
            else
            {
                _state.AddDiagnostic(CreateDiagnosticForNode(location, Diagnostics.The_inferred_type_of_0_cannot_be_named_without_a_reference_to_1_This_is_likely_not_portable_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), specifier));
            }
        }
    }

    /// <summary>ReportNonSerializableProperty implements checker.SymbolTracker.</summary>
    public void ReportNonSerializableProperty(string propertyName)
    {
        var location = ErrorLocation();
        if (location != null)
        {
            _state.AddDiagnostic(CreateDiagnosticForNode(location, Diagnostics.The_type_of_this_node_cannot_be_serialized_because_its_property_0_cannot_be_serialized, propertyName));
        }
    }

    /// <summary>ReportNonlocalAugmentation implements checker.SymbolTracker.</summary>
    public void ReportNonlocalAugmentation(SourceFile containingFile, Symbol parentSymbol, Symbol augmentingSymbol)
    {
        Node? primaryDeclaration = Find(parentSymbol.Declarations, d => GetSourceFileOfNode(d) == containingFile);
        var augmentingDeclarations = Filter(augmentingSymbol.Declarations, d => GetSourceFileOfNode(d) != containingFile);
        if (primaryDeclaration != null && augmentingDeclarations.Length > 0)
        {
            foreach (var augmentations in augmentingDeclarations)
            {
                var diag = CreateDiagnosticForNode(augmentations, Diagnostics.Declaration_augments_declaration_in_another_file_This_cannot_be_serialized);
                var related = CreateDiagnosticForNode(primaryDeclaration, Diagnostics.This_is_the_declaration_being_augmented_Consider_moving_the_augmenting_declaration_into_the_same_file);
                diag.AddRelatedInfo(related);
                _state.AddDiagnostic(diag);
            }
        }
    }

    /// <summary>ReportPrivateInBaseOfClassExpression implements checker.SymbolTracker.</summary>
    public void ReportPrivateInBaseOfClassExpression(string propertyName)
    {
        var location = ErrorLocation();
        if (location != null)
        {
            var diag = CreateDiagnosticForNode(location, Diagnostics.Property_0_of_exported_anonymous_class_type_may_not_be_private_or_protected, propertyName);
            if (IsVariableDeclaration(location.Parent!))
            {
                var related = CreateDiagnosticForNode(location, Diagnostics.Add_a_type_annotation_to_the_variable_0, ErrorDeclarationNameWithFallback());
                diag.AddRelatedInfo(related);
            }
            _state.AddDiagnostic(diag);
        }
    }

    /// <summary>ReportTruncationError implements checker.SymbolTracker.</summary>
    public void ReportTruncationError()
    {
        var location = ErrorLocation();
        if (location != null)
        {
            _state.AddDiagnostic(CreateDiagnosticForNode(location, Diagnostics.The_inferred_type_of_this_node_exceeds_the_maximum_length_the_compiler_will_serialize_An_explicit_type_annotation_is_needed));
        }
    }

    internal Node? ErrorFallbackNode()
    {
        if (_fallbackStack.Count >= 1)
        {
            return _fallbackStack[^1];
        }
        return null;
    }

    internal Node? ErrorLocation()
    {
        var location = _state._errorNameNode;
        if (location == null)
        {
            location = ErrorFallbackNode();
        }
        return location;
    }

    internal string ErrorDeclarationNameWithFallback()
    {
        if (_state._errorNameNode != null)
        {
            return TypeScript.Scanner.Scanner.DeclarationNameToString(_state._errorNameNode);
        }
        if (ErrorFallbackNode() != null && GetNameOfDeclaration(ErrorFallbackNode()!) != null)
        {
            return TypeScript.Scanner.Scanner.DeclarationNameToString(GetNameOfDeclaration(ErrorFallbackNode()!)!);
        }
        if (ErrorFallbackNode() != null && IsExportAssignment(ErrorFallbackNode()!))
        {
            if (ErrorFallbackNode()!.AsExportAssignment()!.IsExportEquals)
            {
                return "export=";
            }
            return "default";
        }
        return "(Missing)"; // same fallback declarationNameToString uses when node is zero-width (ie, nameless)
    }

    /// <summary>TrackSymbol implements checker.SymbolTracker.</summary>
    public bool TrackSymbol(Symbol symbol, Node enclosingDeclaration, SymbolFlags meaning)
    {
        if ((symbol.Flags & SymbolFlags.TypeParameter) != 0)
        {
            return false;
        }
        // When watching for a class expression symbol, record its usage without
        // reporting accessibility errors — the caller will handle visibility by
        // wrapping the class in a namespace.
        if (_watchedClassSymbol != null && symbol == _watchedClassSymbol)
        {
            _classSymbolTracked = true;
            return false;
        }
        var issuedDiagnostic = HandleSymbolAccessibilityError(_resolver.IsSymbolAccessible(symbol, enclosingDeclaration, meaning /*shouldComputeAliasToMarkVisible*/, true));
        return issuedDiagnostic;
    }

    internal bool HandleSymbolAccessibilityError(SymbolAccessibilityResult symbolAccessibilityResult)
    {
        if (symbolAccessibilityResult.Accessibility == SymbolAccessibility.Accessible)
        {
            // Add aliases back onto the possible imports list if they're not there so we can try them again with updated visibility info
            if (symbolAccessibilityResult.AliasesToMakeVisible.Length > 0)
            {
                foreach (var @ref in symbolAccessibilityResult.AliasesToMakeVisible)
                {
                    _state._lateMarkedStatements = AppendIfUnique(_state._lateMarkedStatements, @ref);
                }
            }
            // TODO: Do all these accessibility checks inside/after the first pass in the checker when declarations are enabled, if possible

            // The checker should issue errors on unresolvable names, skip the declaration emit error for using a private/unreachable name for those
        }
        else if (symbolAccessibilityResult.Accessibility != SymbolAccessibility.NotResolved)
        {
            // Report error
            var errorInfo = _state._getSymbolAccessibilityDiagnostic!(symbolAccessibilityResult);
            if (errorInfo != null)
            {
                var info = errorInfo;
                var diagNode = symbolAccessibilityResult.ErrorNode;
                if (diagNode == null)
                {
                    diagNode = errorInfo._errorNode;
                }
                if (info._typeName != null)
                {
                    _state.AddDiagnostic(CreateDiagnosticForNode(diagNode!, info._diagnosticMessage, TypeScript.Scanner.Scanner.GetTextOfNode(info._typeName), symbolAccessibilityResult.ErrorSymbolName, symbolAccessibilityResult.ErrorModuleName));
                }
                else
                {
                    _state.AddDiagnostic(CreateDiagnosticForNode(diagNode!, info._diagnosticMessage, symbolAccessibilityResult.ErrorSymbolName, symbolAccessibilityResult.ErrorModuleName));
                }
                return true;
            }
        }
        return false;
    }
}

public static partial class Declarations
{
    internal static Diagnostic CreateDiagnosticForNode(Node node, Message message, params object?[] args)
    {
        return TypeScript.Checker.Checker.NewDiagnosticForNode(node, message, args);
    }

    public static SymbolTrackerImpl NewSymbolTracker(DeclarationEmitHost host, EmitResolver resolver, SymbolTrackerSharedState state)
    {
        var tracker = new SymbolTrackerImpl
        {
            _host = host,
            _resolver = resolver,
            _state = state,
            _getIsolatedDeclarationError = CreateGetIsolatedDeclarationErrors(resolver),
        };
        return tracker;
    }
}

/// <summary>Go: <c>type SymbolTrackerSharedState struct</c>。</summary>
public sealed class SymbolTrackerSharedState
{
    internal Node[] _lateMarkedStatements = [];
    internal List<Diagnostic> _diagnostics = [];
    internal GetSymbolAccessibilityDiagnostic? _getSymbolAccessibilityDiagnostic;
    internal Node? _errorNameNode;
    internal bool _isolatedDeclarations;
    internal bool _stripInternal;
    internal SourceFile? _currentSourceFile;
    internal EmitResolver _resolver = null!;
    internal Action<Node>? _reportExpandoFunctionErrors;

    internal void AddDiagnostic(Diagnostic diag)
    {
        _diagnostics.Add(diag);
    }
}

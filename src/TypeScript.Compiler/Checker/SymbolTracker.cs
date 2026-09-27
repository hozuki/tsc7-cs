// ported from: internal/checker/symboltracker.go
//
// 与 Go 的差异（C# 语言特性，逐条）：
// 1) Go 的 `SymbolTrackerImpl` 导出类型名照抄；未导出字段 → `_camelCase`（`context` → `_context`、
//    `inner` → `_inner`），Go 导出字段 `DisableTrackSymbol` 原样保留。
// 2) Go 的 `NewSymbolTrackerImpl` 是包级函数 → 同一个 `partial class Checker` 的 `internal static` 成员
//    （docs/checker-plan.md §5.2，Parser/Binder 先例）。
// 3) Go 的 `for { t, ok := tracker.(*SymbolTrackerImpl); if !ok { break }; tracker = t.inner }` 类型断言循环
//    → C# 的 `while (tracker is SymbolTrackerImpl t)` 模式循环（语义一一对应：一直剥掉外层包装）。
// 4) `ast.Symbol`/`ast.SourceFile`/`ast.Node` 都是引用类型，nil ≡ null；`Symbol.Flags`/`Declarations` 是
//    C# 的 public 字段，`SymbolFlags` 是 `[Flags] enum`（`ast.SymbolFlagsTypeParameter` → `SymbolFlags.TypeParameter`）。
// 5) `append(this.context.trackedSymbols, …)` → `List<TrackedSymbolArgs>.Add(…)`（Go 的切片由 NodeBuilderContext
//    持有；追加/截断语义由 List 承担，见 NodeCopy.cs 里 `ExportRecoveryScope` 的 Count/RemoveRange 写法）。
using TypeScript.Ast;

namespace TypeScript.Checker;

/// <summary>Go: <c>type SymbolTrackerImpl struct</c>。</summary>
public sealed class SymbolTrackerImpl : TypeScript.NodeBuilder.SymbolTracker
{
    internal readonly NodeBuilderContext _context;
    internal readonly TypeScript.NodeBuilder.SymbolTracker? _inner;

    /// <summary>Go: <c>SymbolTrackerImpl.DisableTrackSymbol</c>（Go 导出字段）。</summary>
    public bool DisableTrackSymbol;

    internal SymbolTrackerImpl(NodeBuilderContext context, TypeScript.NodeBuilder.SymbolTracker? inner)
    {
        _context = context;
        _inner = inner;
    }

    public bool TrackSymbol(Symbol symbol, Node enclosingDeclaration, SymbolFlags meaning)
    {
        if (!DisableTrackSymbol)
        {
            if (_inner is not null && _inner.TrackSymbol(symbol, enclosingDeclaration, meaning))
            {
                OnDiagnosticReported();
                return true;
            }

            // Skip recording type parameters as they dont contribute to late painted statements
            if ((symbol.Flags & SymbolFlags.TypeParameter) == 0)
            {
                _context._trackedSymbols.Add(new TrackedSymbolArgs { _symbol = symbol, _enclosingDeclaration = enclosingDeclaration, _meaning = meaning });
            }
        }

        return false;
    }

    public void ReportInaccessibleThisError()
    {
        OnDiagnosticReported();
        _inner?.ReportInaccessibleThisError();
    }

    public void ReportPrivateInBaseOfClassExpression(string propertyName)
    {
        OnDiagnosticReported();
        _inner?.ReportPrivateInBaseOfClassExpression(propertyName);
    }

    public void ReportInaccessibleUniqueSymbolError()
    {
        OnDiagnosticReported();
        _inner?.ReportInaccessibleUniqueSymbolError();
    }

    public void ReportCyclicStructureError()
    {
        OnDiagnosticReported();
        _inner?.ReportCyclicStructureError();
    }

    public void ReportLikelyUnsafeImportRequiredError(string specifier, string symbolName)
    {
        OnDiagnosticReported();
        _inner?.ReportLikelyUnsafeImportRequiredError(specifier, symbolName);
    }

    public void ReportTruncationError()
    {
        OnDiagnosticReported();
        _inner?.ReportTruncationError();
    }

    public void ReportNonlocalAugmentation(SourceFile containingFile, Symbol parentSymbol, Symbol augmentingSymbol)
    {
        OnDiagnosticReported();
        _inner?.ReportNonlocalAugmentation(containingFile, parentSymbol, augmentingSymbol);
    }

    public void ReportNonSerializableProperty(string propertyName)
    {
        OnDiagnosticReported();
        _inner?.ReportNonSerializableProperty(propertyName);
    }

    private void OnDiagnosticReported()
    {
        _context._reportedDiagnostic = true;
    }

    public void ReportInferenceFallback(Node node)
    {
        _inner?.ReportInferenceFallback(node);
    }

    public void PushErrorFallbackNode(Node node)
    {
        _inner?.PushErrorFallbackNode(node);
    }

    public void PopErrorFallbackNode()
    {
        _inner?.PopErrorFallbackNode();
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func NewSymbolTrackerImpl(context *NodeBuilderContext, tracker nodebuilder.SymbolTracker) *SymbolTrackerImpl</c>。</summary>
    public static SymbolTrackerImpl NewSymbolTrackerImpl(NodeBuilderContext context, TypeScript.NodeBuilder.SymbolTracker? tracker)
    {
        while (tracker is SymbolTrackerImpl t)
        {
            tracker = t._inner;
        }

        return new SymbolTrackerImpl(context, tracker);
    }
}

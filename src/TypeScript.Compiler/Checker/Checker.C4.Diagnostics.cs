// ported from: internal/checker/checker.go:14183-14301
//   （GetDiagnostics / GetSuggestionDiagnostics / getDiagnostics / GetGlobalDiagnostics / addDeferredDiagnostic /
//    produceDeferredDiagnostics / addDiagnostic / addSuggestionDiagnostic / error / errorSkippedOnNoEmit /
//    errorOrSuggestion / errorAndMaybeSuggestAwait / addErrorOrSuggestion / IsDeprecatedDeclaration /
//    addDeprecatedSuggestion / addDeprecatedSuggestionWorker / isDeprecatedSymbol / hasParseDiagnostics）
//
// **本文件是 checker 全包的"错误 API"归属地**（checker.go 14183–14298 全在 C4 的窗口内；已在 IRC 广播给全员，
// C1 撤回了它的相反主张）。签名与 Go 逐字对应，只有两处是按 C# 习惯的机械映射：
//
// 1) `getDiagnostics`（未导出）与导出的 `GetDiagnostics` 同名 → `GetDiagnosticsWorker`（Worker 后缀规则，
//    `artifacts/checker-method-collisions.md`）。`component` 参数在 Go 是 `*ast.DiagnosticsCollection` 指针，
//    Go 用 `collection == &c.suggestionDiagnostics` 判定来源 → C# 直接比较集合引用（`collection == _suggestionDiagnostics`）。
// 2) Go 返回 nil 切片 → C# `[]`（§3.2：语义等同时用空数组）；`addDiagnostic`/`addSuggestionDiagnostic`/`error`
//    在 Go 里**有返回值**（`*ast.Diagnostic`），C# 保持有返回值（调用点当语句写也合法），比"void"更贴 Go。
// 3) `suggestion := *diagnostic`（Go 结构体浅拷贝）→ `diagnostic.Clone()`（`Diagnostic.Clone()` = `MemberwiseClone`，
//    语义一致：Go 的 `Diagnostic` 是值 + 指针字段，浅拷贝与 MemberwiseClone 同义）。
// 4) `c.deferredDiagnosticCallbacks = nil` → `_deferredDiagnosticCallbacks.Clear()`（C1 声明为非空的 `List<Action>`；
//    两者对后续 `append`/`Add` 的行为一致）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) GetDiagnostics(ctx context.Context, sourceFile *ast.SourceFile) []*ast.Diagnostic</c>。</summary>
    public Diagnostic[] GetDiagnostics(Context ctx, SourceFile sourceFile)
    {
        return GetDiagnosticsWorker(ctx, sourceFile, _diagnostics);
    }

    /// <summary>Go: <c>func (c *Checker) GetSuggestionDiagnostics(ctx context.Context, sourceFile *ast.SourceFile) []*ast.Diagnostic</c>。</summary>
    public Diagnostic[] GetSuggestionDiagnostics(Context ctx, SourceFile sourceFile)
    {
        return GetDiagnosticsWorker(ctx, sourceFile, _suggestionDiagnostics);
    }

    /// <summary>Go: <c>func (c *Checker) getDiagnostics(ctx context.Context, sourceFile *ast.SourceFile, collection *ast.DiagnosticsCollection) []*ast.Diagnostic</c>。</summary>
    internal Diagnostic[] GetDiagnosticsWorker(Context ctx, SourceFile sourceFile, DiagnosticsCollection collection)
    {
        CheckNotCanceled();
        var checkUnused = _compilerOptions.NoUnusedLocals.IsTrue()
            || _compilerOptions.NoUnusedParameters.IsTrue()
            || collection == _suggestionDiagnostics;
        CheckSourceFile(ctx, sourceFile, checkUnused);
        if (_wasCanceled)
        {
            return [];
        }

        return collection.GetDiagnosticsForFile(sourceFile);
    }

    /// <summary>Go: <c>func (c *Checker) GetGlobalDiagnostics() []*ast.Diagnostic</c>。</summary>
    public Diagnostic[] GetGlobalDiagnostics()
    {
        CheckNotCanceled();
        ProduceDeferredDiagnostics();
        return _diagnostics.GetGlobalDiagnostics();
    }

    /// <summary>Go: <c>func (c *Checker) addDeferredDiagnostic(callback func())</c>。</summary>
    internal void AddDeferredDiagnostic(Action callback)
    {
        _deferredDiagnosticCallbacks.Add(callback);
    }

    /// <summary>Go: <c>func (c *Checker) produceDeferredDiagnostics()</c>。</summary>
    internal void ProduceDeferredDiagnostics()
    {
        // Go: `for _, cb := range c.deferredDiagnosticCallbacks { cb() }` —— range 只对切片头做一次快照
        // （ptr/len），回调里再 `append` 不会影响本次遍历，新增的回调在循环结束后的 `= nil` 里被丢弃。
        // C# 的 `foreach (List<T>)` 会在枚举期间检测版本号变更并抛 InvalidOperationException，
        // 所以先取数组快照再遍历；本轮新增的项仍进原 list，末尾 Clear() 与 Go 的 `= nil` 同义（一并丢弃）。
        var callbacks = _deferredDiagnosticCallbacks.ToArray();
        foreach (var cb in callbacks)
        {
            cb();
        }

        _deferredDiagnosticCallbacks.Clear();
    }

    /// <summary>Go: <c>func (c *Checker) addDiagnostic(diagnostic *ast.Diagnostic) *ast.Diagnostic</c>。</summary>
    internal Diagnostic AddDiagnostic(Diagnostic diagnostic)
    {
        // Discard diagnostics created while at the maximum number of recursive TypeToString invocations.
        if (_serializationLevel < MaxSerializationLevel)
        {
            return _diagnostics.Add(diagnostic);
        }

        return diagnostic;
    }

    /// <summary>Go: <c>func (c *Checker) addSuggestionDiagnostic(diagnostic *ast.Diagnostic) *ast.Diagnostic</c>。</summary>
    internal Diagnostic AddSuggestionDiagnostic(Diagnostic diagnostic)
    {
        // Discard diagnostics created while at the maximum number of recursive TypeToString invocations.
        if (_serializationLevel < MaxSerializationLevel)
        {
            return _suggestionDiagnostics.Add(diagnostic);
        }

        return diagnostic;
    }

    /// <summary>Go: <c>func (c *Checker) error(location *ast.Node, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>。</summary>
    internal Diagnostic Error(Node? location, Message message, params object?[] args)
    {
        return AddDiagnostic(NewDiagnosticForNode(location, message, args));
    }

    /// <summary>Go: <c>func (c *Checker) errorSkippedOnNoEmit(location *ast.Node, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>。</summary>
    internal Diagnostic ErrorSkippedOnNoEmit(Node? location, Message message, params object?[] args)
    {
        var diagnostic = Error(location, message, args);
        diagnostic.SetSkippedOnNoEmit();
        return diagnostic;
    }

    /// <summary>Go: <c>func (c *Checker) errorOrSuggestion(isError bool, location *ast.Node, message *diagnostics.Message, args ...any)</c>。</summary>
    internal void ErrorOrSuggestion(bool isError, Node? location, Message message, params object?[] args)
    {
        AddErrorOrSuggestion(isError, NewDiagnosticForNode(location, message, args));
    }

    /// <summary>Go: <c>func (c *Checker) errorAndMaybeSuggestAwait(location *ast.Node, maybeMissingAwait bool, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>。</summary>
    internal Diagnostic ErrorAndMaybeSuggestAwait(Node? location, bool maybeMissingAwait, Message message, params object?[] args)
    {
        var diagnostic = Error(location, message, args);
        if (maybeMissingAwait)
        {
            diagnostic.AddRelatedInfo(CreateDiagnosticForNode(location!, Did_you_forget_to_use_await));
        }

        return diagnostic;
    }

    /// <summary>Go: <c>func (c *Checker) addErrorOrSuggestion(isError bool, diagnostic *ast.Diagnostic)</c>。</summary>
    internal void AddErrorOrSuggestion(bool isError, Diagnostic diagnostic)
    {
        if (isError)
        {
            AddDiagnostic(diagnostic);
        }
        else
        {
            var suggestion = diagnostic.Clone();
            suggestion.SetCategory(Category.Suggestion);
            AddSuggestionDiagnostic(suggestion);
        }
    }

    /// <summary>Go: <c>func (c *Checker) IsDeprecatedDeclaration(declaration *ast.Node) bool</c>。</summary>
    public bool IsDeprecatedDeclaration(Node declaration)
    {
        return IsDeprecatedDeclarationWithCachedFlags(declaration, GetCombinedNodeFlagsCached(declaration));
    }

    /// <summary>Go: <c>func (c *Checker) addDeprecatedSuggestion(location *ast.Node, declarations []*ast.Node, deprecatedEntity string) *ast.Diagnostic</c>。</summary>
    internal Diagnostic AddDeprecatedSuggestion(Node location, Node[]? declarations, string deprecatedEntity)
    {
        var diagnostic = NewDiagnosticForNode(location, X_0_is_deprecated, deprecatedEntity);
        return AddDeprecatedSuggestionWorker(declarations, diagnostic);
    }

    /// <summary>Go: <c>func (c *Checker) addDeprecatedSuggestionWorker(declarations []*ast.Node, diagnostic *ast.Diagnostic) *ast.Diagnostic</c>。</summary>
    /// <remarks>Go 的 nil 切片可 range（不迭代）→ C# 用 <c>?? []</c> 兜住 null 数组。</remarks>
    internal Diagnostic AddDeprecatedSuggestionWorker(Node[]? declarations, Diagnostic diagnostic)
    {
        foreach (var declaration in declarations ?? [])
        {
            var deprecatedTag = GetJSDocDeprecatedTag(declaration);
            if (deprecatedTag is not null)
            {
                diagnostic.AddRelatedInfo(NewDiagnosticForNode(deprecatedTag, The_declaration_was_marked_as_deprecated_here));
                break;
            }
        }

        return AddSuggestionDiagnostic(diagnostic);
    }

    /// <summary>Go: <c>func (c *Checker) isDeprecatedSymbol(symbol *ast.Symbol) bool</c>。</summary>
    internal bool IsDeprecatedSymbol(Symbol symbol)
    {
        // Go 的 `symbol.Declarations` 是 nil 切片时 `len(...) == 0`；C# 的数组为 null 必须兜住
        // （例如 binder 合成的类 `prototype` 符号只设了 Parent、没有声明，见 Binder.Declarations.cs:347）。
        var declarations = symbol.Declarations ?? [];
        var parentSymbol = GetParentOfSymbol(symbol);
        if (parentSymbol is not null && declarations.Length > 1)
        {
            if ((parentSymbol.Flags & SymbolFlags.Interface) != 0)
            {
                return Some(declarations, IsDeprecatedDeclaration);
            }

            return Every(declarations, IsDeprecatedDeclaration);
        }

        return (symbol.ValueDeclaration is not null && IsDeprecatedDeclaration(symbol.ValueDeclaration))
            || (declarations.Length != 0 && Every(declarations, IsDeprecatedDeclaration));
    }

    /// <summary>Go: <c>func (c *Checker) hasParseDiagnostics(sourceFile *ast.SourceFile) bool</c>。</summary>
    internal bool HasParseDiagnostics(SourceFile sourceFile)
    {
        return sourceFile.Diagnostics().Length > 0;
    }
}

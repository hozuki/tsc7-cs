// ported from: internal/checker/emitresolver.go:1-949
//
// 本文件是 `internal/checker/emitresolver.go` 的类型声明与前半部分（`newEmitResolver` … `GetReferencedMemberValueDeclaration`）；
// 后半部分（`CreateReturnTypeOfSignatureDeclaration` 起的节点构造族）在 `EmitResolver.NodeBuilder.cs`
// （Go 1323 行 > 1200，按逻辑段拆成两个 `partial` 分片，见 docs/architecture.md §2）。
//
// Go→C# 语义改写（逐条）：
//  1) `var _ printer.EmitResolver = (*EmitResolver)(nil)` 是 Go 的接口满足断言 → C# 直接
//     `public sealed partial class EmitResolver : PrinterEmitResolver`（`using PrinterEmitResolver =
//     TypeScript.Printer.EmitResolver;`）。注意 `EmitResolver` 这个名字同时存在于 `TypeScript.Printer`（接口），
//     本命名空间声明的类会盖住 using 引入的接口名；而 `Printer` 这个简单名又会被 using 引入的
//     `TypeScript.Printer.Printer`（类）抢走，所以只能走全限定别名。
//  2) `checkerMu *sync.Mutex`（Go 里指向 `checker.mu`）→ `private readonly Lock _checkerMu`，构造时取 `checker._mu`；
//     `Lock()`/`defer Unlock()` → `lock (_checkerMu) { … }`（`System.Threading.Lock` 支持 lock 语句，语义等价）。
//  3) Go 的 `func(node *ast.Node) bool` 字段（`isValueAliasDeclaration`/`aliasMarkingVisitor`）→ `Func<Node, bool>`，
//     构造器里赋值（方法组）。`binder.ReferenceResolver` 字段 → `ReferenceResolver?`。
//  4) link 结构体（`JSXLinks`/`DeclarationLinks`/`DeclarationFileLinks`）是 `core.LinkStore[K,V]` 的 V，
//     要求 `class, new()` → `sealed class` + Go 未导出字段 `_camelCase`。
//  5) `printer.SymbolAccessibilityResult{…}` 复合字面量 → `new SymbolAccessibilityResult { … = … }`。
//  6) Go 未导出的方法（`isDeclarationVisible`/`isEntityNameVisible`/`requiresAddingImplicitUndefined`/`isSymbolAccessible`/
//     `isOptionalParameter`）与同名导出接口方法冲突 → 未导出者加 `Worker` 后缀
//     （`IsDeclarationVisibleWorker`/`IsEntityNameVisibleWorker`/`RequiresAddingImplicitUndefinedWorker`/
//     `IsSymbolAccessibleWorker`/`IsOptionalParameterWorker`）；`hasVisibleDeclarations` 无导出同名者 → `HasVisibleDeclarations`
//     （internal，供 symbolaccessibility.go/nodebuilderimpl.go 调用）。
//  7) 包级函数 → 同一个 `partial class Checker` 的 `static` 成员；本文件经 `using static TypeScript.Checker.Checker;`
//     按裸名调用（`IsCommonJSModuleExports`/`GetMeaningOfEntityNameReference`/`IsConstEnumOrConstEnumOnlyModule`/`NoopAddVisibleAlias`）。
//  8) `slices.Collect(maps.Values(set))` → 手动填充 `Node[]`（Go 的 map 迭代顺序本就随机）。
//  9) `switch node.Kind` 里 Go 的 `break`（跳出 switch）→ C# 的 `break`；`fallthrough` → `goto case …`。
// 10) Go 的 `if node == nil` 检查保留（接口签名是非空 `Node`，但 Go 的运行期契约允许 nil 传入 parse-tree 检查前）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using System.Threading;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Evaluator;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Checker.Checker;
using static TypeScript.Evaluator.EvaluatorFunctions;
// `Printer` 这个简单名会被 using 引入的 `TypeScript.Printer.Printer`（类）抢走，所以必须用全限定别名
// 指到同名接口 `TypeScript.Printer.EmitResolver`（本命名空间里的 `EmitResolver` 类又盖住了接口名）。
using PrinterEmitResolver = TypeScript.Printer.EmitResolver;

// Links for jsx
public sealed class JSXLinks
{
    internal Node? _importRef;
}

// Links for declarations
public sealed class DeclarationLinks
{
    /// <summary>if declaration is depended upon by exported declarations</summary>
    internal Tristate _isVisible;
}

public sealed class DeclarationFileLinks
{
    /// <summary>if file has had alias visibility marked</summary>
    internal bool _aliasesMarked;
}

public sealed partial class EmitResolver : PrinterEmitResolver
{
    private readonly Checker _checker;
    private readonly Lock _checkerMu;
    private readonly Func<Node, bool> _isValueAliasDeclaration;
    private readonly Visitor _aliasMarkingVisitor;
    private ReferenceResolver? _referenceResolver;
    private readonly LinkStore<Node, JSXLinks> _jsxLinks = new();
    private readonly LinkStore<Node, DeclarationLinks> _declarationLinks = new();
    private readonly LinkStore<Node, DeclarationFileLinks> _declarationFileLinks = new();

    internal static EmitResolver NewEmitResolver(Checker checker)
    {
        return new EmitResolver(checker);
    }

    private EmitResolver(Checker checker)
    {
        _checker = checker;
        _checkerMu = checker._mu;
        _isValueAliasDeclaration = node => IsValueAliasDeclarationWorker(node);
        _aliasMarkingVisitor = AliasMarkingVisitorWorker;
    }

    // 接口 `Printer.EmitResolver` 声明的是非空 `Node`（Go 侧返回 `*ast.Node`，运行时可以是 nil；
    // 这里用 `!` 只表达"接口契约如此"，运行期行为与 Go 完全一致——消费方仍需按 Go 那样判空）。
    public Node GetJsxFactoryEntity(Node location)
    {
        lock (_checkerMu)
        {
            return _checker.GetJsxFactoryEntity(location)!;
        }
    }

    public Node GetJsxFragmentFactoryEntity(Node location)
    {
        lock (_checkerMu)
        {
            return _checker.GetJsxFragmentFactoryEntity(location)!;
        }
    }

    public bool IsOptionalParameter(Node node)
    {
        lock (_checkerMu)
        {
            return IsOptionalParameterWorker(node);
        }
    }

    public bool IsLateBound(Node node)
    {
        // TODO: Require an emitContext to construct an EmitResolver, remove all emitContext arguments
        // node = r.emitContext.ParseNode(node)
        if (node is null)
        {
            return false;
        }
        if (!IsParseTreeNode(node))
        {
            return false;
        }
        lock (_checkerMu)
        {
            var symbol = _checker.GetSymbolOfDeclaration(node);
            if (symbol is null)
            {
                return false;
            }
            return (symbol.CheckFlags & CheckFlags.Late) != 0;
        }
    }

    public Result GetEnumMemberValue(Node node)
    {
        // node = r.emitContext.ParseNode(node)
        if (!IsParseTreeNode(node))
        {
            return NewResult(null, false, false, false);
        }
        lock (_checkerMu)
        {
            _checker.ComputeEnumMemberValues(node.Parent!);
            if (!_checker._enumMemberLinks.Has(node))
            {
                return NewResult(null, false, false, false);
            }
            return _checker._enumMemberLinks.Get(node)._value;
        }
    }

    public bool IsDeclarationVisible(Node node)
    {
        // Only lock on external API func to prevent deadlocks
        lock (_checkerMu)
        {
            return IsDeclarationVisibleWorker(node);
        }
    }

    internal bool IsDeclarationVisibleWorker(Node node)
    {
        // node = r.emitContext.ParseNode(node)
        if (!IsParseTreeNode(node))
        {
            return false;
        }
        if (node is null)
        {
            return false;
        }

        var links = _declarationLinks.Get(node);
        if (links._isVisible == Tristate.TSUnknown)
        {
            if (DetermineIfDeclarationIsVisible(node))
            {
                links._isVisible = Tristate.TSTrue;
            }
            else
            {
                links._isVisible = Tristate.TSFalse;
            }
        }
        return links._isVisible == Tristate.TSTrue;
    }

    internal bool DetermineIfDeclarationIsVisible(Node node)
    {
        switch (node.Kind)
        {
            case Kind.JSDocCallbackTag:
            // case Kind.JSDocEnumTag, // !!! TODO: JSDoc @enum support?
            case Kind.JSDocTypedefTag:
                // Top-level jsdoc type aliases are considered exported
                // First parent is comment node, second is hosting declaration or token; we only care about those tokens or declarations whose parent is a source file
                return node.Parent is not null && node.Parent.Parent is not null && node.Parent.Parent.Parent is not null && IsSourceFile(node.Parent.Parent.Parent);

            case Kind.BindingElement:
                return IsDeclarationVisibleWorker(node.Parent!.Parent!);

            case Kind.VariableDeclaration:
            case Kind.ModuleDeclaration:
            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.FunctionDeclaration:
            case Kind.EnumDeclaration:
            case Kind.ImportEqualsDeclaration:
                if (IsVariableDeclaration(node))
                {
                    if (IsBindingPattern(node.Name()) &&
                        (node.Name()!.Elements()?.Length ?? 0) == 0)
                    {
                        // If the binding pattern is empty, this variable declaration is not visible
                        return false;
                    }
                    // falls through
                }
                // External module augmentation is always visible
                // A @typedef at top-level in an external module is always visible
                if (IsExternalModuleAugmentation(node) || IsImplicitlyExportedJSDocDeclaration(node))
                {
                    return true;
                }
                var parent = GetDeclarationContainer(node)!;
                // If the node is not exported or it is not ambient module element (except import declaration)
                if ((_checker.GetCombinedModifierFlagsCached(node) & ModifierFlags.Export) == 0 &&
                    !(node.Kind != Kind.ImportEqualsDeclaration && parent.Kind != Kind.SourceFile && (parent.Flags & NodeFlags.Ambient) != 0))
                {
                    return IsGlobalSourceFile(parent);
                }
                // Exported members/ambient module elements (exception import declaration) are visible if parent is visible
                return IsDeclarationVisibleWorker(parent);

            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
                if (_checker.GetEffectiveDeclarationFlags(node, ModifierFlags.Private | ModifierFlags.Protected) != 0)
                {
                    // Private/protected properties/methods are not visible
                    return false;
                }
                // Public properties/methods are visible if its parents are visible, so:
                return IsDeclarationVisibleWorker(node.Parent!);

            case Kind.Constructor:
            case Kind.ConstructSignature:
            case Kind.CallSignature:
            case Kind.IndexSignature:
            case Kind.Parameter:
            case Kind.ModuleBlock:
            case Kind.FunctionType:
            case Kind.ConstructorType:
            case Kind.TypeLiteral:
            case Kind.TypeReference:
            case Kind.ArrayType:
            case Kind.TupleType:
            case Kind.UnionType:
            case Kind.IntersectionType:
            case Kind.ParenthesizedType:
            case Kind.NamedTupleMember:
                return IsDeclarationVisibleWorker(node.Parent!);

            // Default binding, import specifier and namespace import is visible
            // only on demand so by default it is not visible
            case Kind.ImportClause:
            case Kind.NamespaceImport:
            case Kind.ImportSpecifier:
                return false;

            // Type parameters are always visible
            case Kind.TypeParameter:
                return true;
            // Source file and namespace export are always visible
            case Kind.SourceFile:
            case Kind.NamespaceExportDeclaration:
                return true;

            // Export assignments do not create name bindings outside the module
            case Kind.ExportAssignment:
                return false;

            // An `export {X}` (without a module specifier) is itself a visible re-export of
            // the named binding; it contributes to the symbol's external visibility.
            case Kind.ExportSpecifier:
                {
                    var exportDecl = node.Parent!.Parent!;
                    if (IsExportDeclaration(exportDecl) && exportDecl.AsExportDeclaration()!.ModuleSpecifier is null)
                    {
                        return IsDeclarationVisibleWorker(exportDecl.Parent!);
                    }
                    return false;
                }

            default:
                return false;
        }
    }

    public void PrecalculateDeclarationEmitVisibility(SourceFile file)
    {
        lock (_checkerMu)
        {
            if (_declarationFileLinks.Get(file.AsNode())._aliasesMarked)
            {
                return;
            }
            _declarationFileLinks.Get(file.AsNode())._aliasesMarked = true;
            // TODO: Does this even *have* to be an upfront walk? If it's not possible for an
            // import a = a.b.c statement to chain into exposing a statement in a sibling scope,
            // it could at least be pushed into scope entry -  then it wouldn't need to be recursive.
            file.AsNode().ForEachChild(_aliasMarkingVisitor);
        }
    }

    internal bool AliasMarkingVisitorWorker(Node node)
    {
        switch (node.Kind)
        {
            case Kind.BinaryExpression:
                if (IsCommonJSModuleExports(node) && IsIdentifier(node.AsBinaryExpression()!.Right!))
                {
                    MarkLinkedAliases(node.AsBinaryExpression()!.Right!);
                }
                break;
            case Kind.ExportAssignment:
                if (node.Expression()!.Kind == Kind.Identifier)
                {
                    MarkLinkedAliases(node.Expression()!);
                }
                break;
            case Kind.ExportSpecifier:
                MarkLinkedAliases(node.PropertyNameOrName()!);
                break;
        }
        return node.ForEachChild(_aliasMarkingVisitor);
    }

    // Sets the isVisible link on statements the Identifier or ExportName node points at
    // Follows chains of import d = a.b.c
    internal void MarkLinkedAliases(Node node)
    {
        Symbol? exportSymbol = null;
        if (node.Kind != Kind.StringLiteral && node.Parent is not null && (IsExportAssignment(node.Parent) || IsCommonJSModuleExports(node.Parent)))
        {
            exportSymbol = _checker._resolveName(node, node.Text(), SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias /*nameNotFoundMessage*/, null /*isUse*/, false, false);
        }
        else if (node.Parent!.Kind == Kind.ExportSpecifier)
        {
            exportSymbol = _checker.GetTargetOfExportSpecifier(node.Parent, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, false);
        }

        var visited = new HashSet<SymbolId>(); // guard against circular imports
        while (exportSymbol is not null)
        {
            if (!visited.Add(GetSymbolId(exportSymbol)))
            {
                break;
            }

            Symbol? nextSymbol = null;
            if (exportSymbol.Declarations is not null)
            {
                foreach (var declaration in exportSymbol.Declarations)
                {
                    _declarationLinks.Get(declaration)._isVisible = Tristate.TSTrue;

                    if (Ast.IsInternalModuleImportEqualsDeclaration(declaration))
                    {
                        // Add the referenced top container visible
                        var internalModuleReference = declaration.AsImportEqualsDeclaration()!.ModuleReference!;
                        var firstIdentifier = GetFirstIdentifier(internalModuleReference)!;
                        var importSymbol = _checker._resolveName(declaration, firstIdentifier.Text(), SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias /*nameNotFoundMessage*/, null /*isUse*/, false, false);
                        nextSymbol = importSymbol;
                    }
                }
            }

            exportSymbol = nextSymbol;
        }
    }

    // Go 的包级函数 `getMeaningOfEntityNameReference` → `Checker.GetMeaningOfEntityNameReference`
    // （见本文件末尾的 Checker 分片；`NodeCopy.cs` 也调用它）。

    public SymbolAccessibilityResult IsEntityNameVisible(Node entityName, Node enclosingDeclaration)
    {
        lock (_checkerMu)
        {
            return IsEntityNameVisibleWorker(entityName, enclosingDeclaration, true);
        }
    }

    internal SymbolAccessibilityResult IsEntityNameVisibleWorker(Node entityName, Node enclosingDeclaration, bool shouldComputeAliasToMakeVisible)
    {
        // node = r.emitContext.ParseNode(entityName)
        if (!IsParseTreeNode(entityName))
        {
            return new SymbolAccessibilityResult { Accessibility = SymbolAccessibility.NotAccessible };
        }

        var meaning = GetMeaningOfEntityNameReference(entityName);
        var firstIdentifier = GetFirstIdentifier(entityName)!;

        var symbol = _checker._resolveName(enclosingDeclaration, firstIdentifier.Text(), meaning, null, false, false);

        if (symbol is not null && (symbol.Flags & SymbolFlags.TypeParameter) != 0 && (meaning & SymbolFlags.Type) != 0)
        {
            return new SymbolAccessibilityResult { Accessibility = SymbolAccessibility.Accessible };
        }

        if (symbol is null && IsThisIdentifier(firstIdentifier))
        {
            var sym = _checker.GetSymbolOfDeclaration(_checker.GetThisContainer(firstIdentifier, false, false)!);
            if (IsSymbolAccessibleWorker(sym!, enclosingDeclaration, meaning, false).Accessibility == SymbolAccessibility.Accessible)
            {
                return new SymbolAccessibilityResult { Accessibility = SymbolAccessibility.Accessible };
            }
        }

        if (symbol is null)
        {
            return new SymbolAccessibilityResult
            {
                Accessibility = SymbolAccessibility.NotResolved,
                ErrorSymbolName = firstIdentifier.Text(),
                ErrorNode = firstIdentifier,
            };
        }

        var visible = HasVisibleDeclarations(symbol, shouldComputeAliasToMakeVisible);
        if (visible is not null)
        {
            return visible;
        }

        return new SymbolAccessibilityResult
        {
            Accessibility = SymbolAccessibility.NotAccessible,
            ErrorSymbolName = firstIdentifier.Text(),
            ErrorNode = firstIdentifier,
        };
    }

    // Go 的包级函数 `noopAddVisibleAlias` → `Checker.NoopAddVisibleAlias`（见本文件末尾的 Checker 分片）。

    internal SymbolAccessibilityResult? HasVisibleDeclarations(Symbol symbol, bool shouldComputeAliasToMakeVisible)
    {
        Dictionary<NodeId, Node>? aliasesToMakeVisibleSet = null;

        void addVisibleAlias(Node declaration, Node aliasingStatement)
        {
            _declarationLinks.Get(declaration)._isVisible = Tristate.TSTrue;
            aliasesToMakeVisibleSet ??= [];
            aliasesToMakeVisibleSet[GetNodeId(declaration)] = aliasingStatement;
        }

        Action<Node, Node> addVisibleAliasFn = shouldComputeAliasToMakeVisible ? addVisibleAlias : NoopAddVisibleAlias;

        if (symbol.Declarations is not null)
        {
            foreach (var declaration in symbol.Declarations)
            {
                if (IsIdentifier(declaration))
                {
                    continue;
                }
                if (!IsDeclarationVisibleWorker(declaration))
                {
                    // Mark the unexported alias as visible if its parent is visible
                    // because these kind of aliases can be used to name types in declaration file
                    var anyImportSyntax = GetAnyImportSyntax(declaration);
                    if (anyImportSyntax is not null &&
                        !HasSyntacticModifier(anyImportSyntax, ModifierFlags.Export) && // import clause without export
                        IsDeclarationVisibleWorker(anyImportSyntax.Parent!))
                    {
                        addVisibleAliasFn(declaration, anyImportSyntax);
                        continue;
                    }
                    if (IsVariableDeclaration(declaration) && IsVariableStatement(declaration.Parent!.Parent!) &&
                        !HasSyntacticModifier(declaration.Parent.Parent!, ModifierFlags.Export) && // unexported variable statement
                        IsDeclarationVisibleWorker(declaration.Parent.Parent!.Parent!))
                    {
                        addVisibleAliasFn(declaration, declaration.Parent.Parent!);
                        continue;
                    }
                    if (IsLateVisibilityPaintedStatement(declaration) && // unexported top-level statement
                        !HasSyntacticModifier(declaration, ModifierFlags.Export) &&
                        IsDeclarationVisibleWorker(declaration.Parent!))
                    {
                        addVisibleAliasFn(declaration, declaration);
                        continue;
                    }
                    if (IsBindingElement(declaration))
                    {
                        if ((symbol.Flags & SymbolFlags.Alias) != 0 && IsInJSFile(declaration) && declaration.Parent is not null && declaration.Parent.Parent is not null && // exported import-like top-level JS require statement
                            IsVariableDeclaration(declaration.Parent.Parent) &&
                            declaration.Parent.Parent.Parent is not null && IsVariableStatement(declaration.Parent.Parent.Parent) &&
                            !HasSyntacticModifier(declaration.Parent.Parent.Parent, ModifierFlags.Export) &&
                            declaration.Parent.Parent.Parent.Parent is not null && // check if the thing containing the variable statement is visible (ie, the file)
                            IsDeclarationVisibleWorker(declaration.Parent.Parent.Parent.Parent))
                        {
                            addVisibleAliasFn(declaration, declaration.Parent.Parent.Parent);
                            continue;
                        }
                        if ((symbol.Flags & SymbolFlags.BlockScopedVariable) != 0)
                        {
                            var rootDeclaration = WalkUpBindingElementsAndPatterns(declaration)!;
                            if (IsParameterDeclaration(rootDeclaration))
                            {
                                return null;
                            }
                            var variableStatement = rootDeclaration.Parent!.Parent!;
                            if (!IsVariableStatement(variableStatement))
                            {
                                return null;
                            }
                            if (HasSyntacticModifier(variableStatement, ModifierFlags.Export))
                            {
                                continue; // no alias to add, already exported
                            }
                            if (!IsDeclarationVisibleWorker(variableStatement.Parent!))
                            {
                                return null; // not visible
                            }
                            addVisibleAliasFn(declaration, variableStatement);
                            continue;
                        }
                    }

                    // Declaration is not visible
                    return null;
                }
            }
        }

        Node[] aliases;
        if (aliasesToMakeVisibleSet is null)
        {
            aliases = [];
        }
        else
        {
            aliases = new Node[aliasesToMakeVisibleSet.Count];
            var i = 0;
            foreach (var value in aliasesToMakeVisibleSet.Values)
            {
                aliases[i++] = value;
            }
        }

        return new SymbolAccessibilityResult
        {
            Accessibility = SymbolAccessibility.Accessible,
            AliasesToMakeVisible = aliases,
        };
    }

    public bool IsImplementationOfOverload(Node node)
    {
        // node = r.emitContext.ParseNode(node)
        if (!IsParseTreeNode(node))
        {
            return false;
        }
        if (NodeIsPresent(node.Body()))
        {
            if (IsGetAccessorDeclaration(node) || IsSetAccessorDeclaration(node))
            {
                return false; // Get or set accessors can never be overload implementations, but can have up to 2 signatures
            }
            lock (_checkerMu)
            {
                var symbol = _checker.GetSymbolOfDeclaration(node);
                var signaturesOfSymbol = _checker.GetSignaturesOfSymbol(symbol!);
                // If this function body corresponds to function with multiple signature, it is implementation of overload
                // e.g.: function foo(a: string): string;
                //       function foo(a: number): number;
                //       function foo(a: any) { // This is implementation of the overloads
                //           return a;
                //       }
                if (signaturesOfSymbol.Length > 1)
                {
                    return true;
                }
                // If there is single signature for the symbol, it is overload if that signature isn't coming from the node
                // e.g.: function foo(a: string): string;
                //       function foo(a: any) { // This is implementation of the overloads
                //           return a;
                //       }
                if (signaturesOfSymbol.Length == 1)
                {
                    var signature = signaturesOfSymbol[0];
                    if (ReferenceEquals(signature, _checker.GetSignatureOfFullSignatureType(node)))
                    {
                        return false;
                    }
                    var declaration = signature._declaration;
                    if (!ReferenceEquals(declaration, node) && (declaration!.Flags & NodeFlags.JSDoc) == 0)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public bool IsImportRequiredByAugmentation(ImportDeclaration decl)
    {
        // node = r.emitContext.ParseNode(node)
        if (!IsParseTreeNode(decl.AsNode()))
        {
            return false;
        }
        var file = GetSourceFileOfNode(decl.AsNode())!;
        if (file.Symbol is null)
        {
            // script file
            return false;
        }
        var importTarget = GetExternalModuleFileFromDeclaration(decl.AsNode());
        if (importTarget is null)
        {
            return false;
        }
        if (ReferenceEquals(importTarget, file))
        {
            return false;
        }
        lock (_checkerMu)
        {
            var exports = _checker.GetExportsOfModuleWorker(file.Symbol);
            foreach (var s in exports.Values)
            {
                var merged = _checker.GetMergedSymbolWorker(s);
                if (!ReferenceEquals(merged, s))
                {
                    if (merged is not null && merged.Declarations is not null && merged.Declarations.Length > 0)
                    {
                        foreach (var d in merged.Declarations)
                        {
                            var declFile = GetSourceFileOfNode(d);
                            if (ReferenceEquals(declFile, importTarget))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    public bool IsDefinitelyReferenceToGlobalSymbolObject(Node node)
    {
        if (!IsPropertyAccessExpression(node) ||
            !IsIdentifier(node.Name()!) ||
            !IsPropertyAccessExpression(node.Expression()!) && !IsIdentifier(node.Expression()!))
        {
            return false;
        }
        if (node.Expression()!.Kind == Kind.Identifier)
        {
            if (node.Expression()!.Text() != "Symbol")
            {
                return false;
            }
            lock (_checkerMu)
            {
                // Exactly `Symbol.something` and `Symbol` either does not resolve or definitely resolves to the global Symbol
                return ReferenceEquals(
                    _checker.GetResolvedSymbolWorker(node.Expression()!),
                    _checker.GetGlobalSymbolWorker("Symbol", SymbolFlags.Value | SymbolFlags.ExportValue, null /*diagnostic*/));
            }
        }
        if (node.Expression()!.Expression()!.Kind != Kind.Identifier || node.Expression()!.Expression()!.Text() != "globalThis" || node.Expression()!.Name()!.Text() != "Symbol")
        {
            return false;
        }
        lock (_checkerMu)
        {
            // Exactly `globalThis.Symbol.something` and `globalThis` resolves to the global `globalThis`
            return ReferenceEquals(_checker.GetResolvedSymbolWorker(node.Expression()!.Expression()!), _checker._globalThisSymbol);
        }
    }

    public bool RequiresAddingImplicitUndefined(Node declaration, Symbol symbol, Node? enclosingDeclaration)
    {
        if (!IsParseTreeNode(declaration))
        {
            return false;
        }
        lock (_checkerMu)
        {
            return RequiresAddingImplicitUndefinedWorker(declaration, symbol, enclosingDeclaration);
        }
    }

    public bool RequiresAddingImplicitUndefinedUnsafe(Node declaration, Symbol symbol, Node? enclosingDeclaration)
    {
        if (!IsParseTreeNode(declaration))
        {
            return false;
        }
        // NO LOCKING - only should be called in contexts that already have a checker lock
        return RequiresAddingImplicitUndefinedWorker(declaration, symbol, enclosingDeclaration);
    }

    internal bool RequiresAddingImplicitUndefinedWorker(Node declaration, Symbol? symbol, Node? enclosingDeclaration)
    {
        // node = r.emitContext.ParseNode(node)
        if (!IsParseTreeNode(declaration))
        {
            return false;
        }
        switch (declaration.Kind)
        {
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.JSDocPropertyTag:
                symbol ??= _checker.GetSymbolOfDeclaration(declaration);
                var t = _checker.GetTypeOfSymbolWorker(symbol!);
                // Go 里这一行是"探针式"取值（结果未使用），逐字保留；`_ =` 只为避免 IDE 的"值未使用"分析。
                _ = _checker._mappedSymbolLinks.Has(symbol!);
                return (symbol!.Flags & SymbolFlags.Property) != 0 && (symbol.Flags & SymbolFlags.Optional) != 0 && IsOptionalDeclaration(declaration) && _checker.ReverseMappedSymbolLinks.Has(symbol) && _checker.ReverseMappedSymbolLinks.Get(symbol)._mappedType is not null && ContainsNonMissingUndefinedType(_checker, t);
            case Kind.Parameter:
            case Kind.JSDocParameterTag:
                return RequiresAddingImplicitUndefinedWorkerImpl(declaration, enclosingDeclaration);
            default:
                throw new DebugFailureException("Node cannot possibly require adding undefined");
        }
    }

    internal bool RequiresAddingImplicitUndefinedWorkerImpl(Node parameter, Node? enclosingDeclaration) =>
        (IsRequiredInitializedParameter(parameter, enclosingDeclaration) || IsOptionalUninitializedParameterProperty(parameter)) &&
        !DeclaredParameterTypeContainsUndefined(parameter);

    internal bool DeclaredParameterTypeContainsUndefined(Node parameter)
    {
        // typeNode := getNonlocalEffectiveTypeAnnotationNode(parameter); // !!! JSDoc Support
        var typeNode = parameter.Type();
        if (typeNode is null)
        {
            return false;
        }
        var t = _checker.GetTypeFromTypeNodeWorker(typeNode);
        // allow error type here to avoid confusing errors that the annotation has to contain undefined when it does in cases like this:
        //
        // export function fn(x?: Unresolved | undefined): void {}
        return _checker.IsErrorType(t) || _checker.ContainsUndefinedType(t);
    }

    internal bool IsOptionalUninitializedParameterProperty(Node parameter) =>
        _checker._strictNullChecks &&
        IsOptionalParameterWorker(parameter) &&
        ( /*isJSDocParameterTag(parameter) ||*/ parameter.Initializer() is null) && // !!! TODO: JSDoc support
        HasSyntacticModifier(parameter, ModifierFlags.ParameterPropertyModifier);

    internal bool IsRequiredInitializedParameter(Node parameter, Node? enclosingDeclaration)
    {
        if (!_checker._strictNullChecks || IsOptionalParameterWorker(parameter) || /*isJSDocParameterTag(parameter) ||*/ parameter.Initializer() is null) // !!! TODO: JSDoc Support
        {
            return false;
        }
        if (HasSyntacticModifier(parameter, ModifierFlags.ParameterPropertyModifier))
        {
            return enclosingDeclaration is not null && IsFunctionLikeDeclaration(enclosingDeclaration);
        }
        return true;
    }

    internal bool IsOptionalParameterWorker(Node node) => _checker.IsOptionalParameter(node);

    public bool IsLiteralConstDeclaration(Node node)
    {
        // node = r.emitContext.ParseNode(node)
        if (!IsParseTreeNode(node))
        {
            return false;
        }
        if (IsDeclarationReadonly(node) || IsVariableDeclaration(node) && IsVarConst(node))
        {
            lock (_checkerMu)
            {
                var s = _checker.GetSymbolOfDeclaration(node);
                if (s is null)
                {
                    return false;
                }
                return IsFreshLiteralType(_checker.GetTypeOfSymbolWorker(s));
            }
        }
        return false;
    }

    public bool IsExpandoFunctionDeclarationUnsafe(Node node)
    {
        // node = r.emitContext.ParseNode(node)
        if (!IsParseTreeNode(node))
        {
            return false;
        }
        // this is substantially different from strada, but so is expando property checking
        var props = GetPropertiesOfContainerFunction(node);
        foreach (var p in props)
        {
            if (IsExpandoPropertyDeclaration(p.ValueDeclaration!))
            {
                return true;
            }
        }
        return false;
    }

    public bool IsExpandoFunctionDeclaration(Node node)
    {
        lock (_checkerMu)
        {
            return IsExpandoFunctionDeclarationUnsafe(node);
        }
    }

    internal SymbolAccessibilityResult IsSymbolAccessibleWorker(Symbol symbol, Node? enclosingDeclaration, SymbolFlags meaning, bool shouldComputeAliasToMarkVisible) =>
        _checker.IsSymbolAccessible(symbol, enclosingDeclaration, meaning, shouldComputeAliasToMarkVisible);

    public SymbolAccessibilityResult IsSymbolAccessible(Symbol symbol, Node enclosingDeclaration, SymbolFlags meaning, bool shouldComputeAliasToMarkVisible)
    {
        // TODO: Split into locking and non-locking API methods - only current usage is the symbol tracker, which is non-locking,
        // as all tracker calls happen within a CreateX call below, which already holds a lock
        // _checkerMu.Lock()
        // defer _checkerMu.Unlock()
        return IsSymbolAccessibleWorker(symbol, enclosingDeclaration, meaning, shouldComputeAliasToMarkVisible);
    }

    // Go 的包级函数 `isConstEnumOrConstEnumOnlyModule` → `Checker.IsConstEnumOrConstEnumOnlyModule`
    // （见本文件末尾的 Checker 分片；`Checker.C8.Aliases.cs` 也调用它）。

    public bool IsReferencedAliasDeclaration(Node node)
    {
        var c = _checker;
        if (!c._canCollectSymbolAliasAccessibilityData || !IsParseTreeNode(node))
        {
            return true;
        }

        lock (_checkerMu)
        {
            if (IsAliasSymbolDeclaration(node))
            {
                var symbol = c.GetSymbolOfDeclaration(node);
                if (symbol is not null)
                {
                    var aliasLinks = c._aliasSymbolLinks.Get(symbol);
                    if (aliasLinks._referenced)
                    {
                        return true;
                    }
                    var target = aliasLinks._aliasTarget;
                    if (target is not null && (node.ModifierFlags & ModifierFlags.Export) != 0 &&
                        (c.GetSymbolFlagsWorker(target) & SymbolFlags.Value) != 0 &&
                        (c._compilerOptions.ShouldPreserveConstEnums() || !IsConstEnumOrConstEnumOnlyModule(target)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    public bool IsValueAliasDeclaration(Node node)
    {
        var c = _checker;
        if (!c._canCollectSymbolAliasAccessibilityData || !IsParseTreeNode(node))
        {
            return true;
        }

        lock (_checkerMu)
        {
            return IsValueAliasDeclarationWorker(node);
        }
    }

    internal bool IsValueAliasDeclarationWorker(Node node)
    {
        var c = _checker;

        switch (node.Kind)
        {
            case Kind.ImportEqualsDeclaration:
                return IsAliasResolvedToValue(c.GetSymbolOfDeclaration(node), false /*excludeTypeOnlyValues*/);
            case Kind.ImportClause:
            case Kind.NamespaceImport:
            case Kind.ImportSpecifier:
            case Kind.ExportSpecifier:
                var symbol = c.GetSymbolOfDeclaration(node);
                return symbol is not null && IsAliasResolvedToValue(symbol, true /*excludeTypeOnlyValues*/);
            case Kind.ExportDeclaration:
                var exportClause = node.AsExportDeclaration()!.ExportClause;
                return exportClause is not null && (IsNamespaceExport(exportClause) ||
                    Core.Some(exportClause.Elements(), _isValueAliasDeclaration));
            case Kind.ExportAssignment:
                if (node.Expression() is not null && node.Expression()!.Kind == Kind.Identifier)
                {
                    return IsAliasResolvedToValue(c.GetSymbolOfDeclaration(node), true /*excludeTypeOnlyValues*/);
                }
                return true;
            case Kind.BinaryExpression:
                if (IsCommonJSModuleExports(node) && IsIdentifier(node.AsBinaryExpression()!.Right!))
                {
                    return IsAliasResolvedToValue(c.GetSymbolOfDeclaration(node), true /*excludeTypeOnlyValues*/);
                }
                break;
        }
        return false;
    }

    internal bool IsAliasResolvedToValue(Symbol? symbol, bool excludeTypeOnlyValues)
    {
        var c = _checker;
        if (symbol is null)
        {
            return false;
        }
        if (symbol.ValueDeclaration is not null)
        {
            var container = GetSourceFileOfNode(symbol.ValueDeclaration);
            if (container is not null)
            {
                var fileSymbol = c.GetSymbolOfDeclaration(container.AsNode());
                // Ensures cjs export assignment is setup, since this symbol may point at, and merge with, the file itself.
                // If we don't, the merge may not have yet occurred, and the flags check below will be missing flags that
                // are added as a result of the merge.
                c.ResolveExternalModuleSymbolWorker(fileSymbol, false /*dontResolveAlias*/);
            }
        }
        var target = c.GetExportSymbolOfValueSymbolIfExported(c.ResolveAliasWorker(symbol)!);
        if (ReferenceEquals(target, c._unknownSymbol))
        {
            return !excludeTypeOnlyValues || c.GetTypeOnlyAliasDeclarationWorker(symbol) is null;
        }
        // const enums and modules that contain only const enums are not considered values from the emit perspective
        // unless 'preserveConstEnums' option is set to true
        return (c.GetSymbolFlagsEx(symbol, excludeTypeOnlyValues, true /*excludeLocalMeanings*/) & SymbolFlags.Value) != 0 &&
            (c._compilerOptions.ShouldPreserveConstEnums() || !IsConstEnumOrConstEnumOnlyModule(target));
    }

    public bool IsTopLevelValueImportEqualsWithEntityName(Node node)
    {
        var c = _checker;
        if (!c._canCollectSymbolAliasAccessibilityData)
        {
            return true;
        }
        if (!IsParseTreeNode(node) || node.Kind != Kind.ImportEqualsDeclaration || node.Parent!.Kind != Kind.SourceFile)
        {
            return false;
        }
        if (IsImportEqualsDeclaration(node) &&
            (NodeIsMissing(node.AsImportEqualsDeclaration()!.ModuleReference) || node.AsImportEqualsDeclaration()!.ModuleReference!.Kind == Kind.ExternalModuleReference))
        {
            return false;
        }

        lock (_checkerMu)
        {
            return IsAliasResolvedToValue(c.GetSymbolOfDeclaration(node), false /*excludeTypeOnlyValues*/);
        }
    }

    public void MarkLinkedReferencesRecursively(SourceFile file)
    {
        if (!IsParseTreeNode(file.AsNode()))
        {
            return;
        }
        lock (_checkerMu)
        {
            if (file is not null)
            {
                bool visit(Node n)
                {
                    if (IsImportEqualsDeclaration(n) && (n.ModifierFlags & ModifierFlags.Export) == 0)
                    {
                        return false; // These are deferred and marked in a chain when referenced
                    }
                    if (IsImportDeclaration(n))
                    {
                        return false; // likewise, these are ultimately what get marked by calls on other nodes - we want to skip them
                    }
                    _checker.MarkLinkedReferences(n, ReferenceHint.Unspecified, null /*propSymbol*/, null /*parentType*/);
                    n.ForEachChild(visit);
                    return false;
                }
                file.ForEachChild(visit);
            }
        }
    }

    public SourceFile? GetExternalModuleFileFromDeclaration(Node declaration)
    {
        if (!IsParseTreeNode(declaration))
        {
            return null;
        }

        lock (_checkerMu)
        {
            return _checker.GetExternalModuleFileFromDeclaration(declaration);
        }
    }

    internal ReferenceResolver GetReferenceResolver()
    {
        if (_referenceResolver is null)
        {
            _referenceResolver = Binder.NewReferenceResolver(_checker._compilerOptions, new ReferenceResolverHooks
            {
                ResolveName = _checker._resolveName,
                GetResolvedSymbol = _checker.GetResolvedSymbolOrNil,
                GetMergedSymbol = _checker.GetMergedSymbolWorker,
                GetParentOfSymbol = _checker.GetParentOfSymbol,
                GetSymbolOfDeclaration = _checker.GetSymbolOfDeclaration,
                GetTypeOnlyAliasDeclaration = _checker.GetTypeOnlyAliasDeclarationEx,
                GetExportSymbolOfValueSymbolIfExported = _checker.GetExportSymbolOfValueSymbolIfExported,
                // Go: `GetElementAccessExpressionName: r.checker.tryGetElementAccessExpressionName`（Go 返回 `(string, bool)`；
                // C# 的 hook 形状是 `Func<ElementAccessExpression, string?>`，`(T,bool)` → `TryX(out T)`，故用 lambda 适配）。
                GetElementAccessExpressionName = expression => _checker.TryGetElementAccessExpressionName(expression, out var name) ? name : null,
            });
        }
        return _referenceResolver;
    }

    public Node? GetReferencedExportContainer(Node node, bool prefixLocals)
    {
        if (!IsParseTreeNode(node))
        {
            return null;
        }

        lock (_checkerMu)
        {
            return GetReferenceResolver().GetReferencedExportContainer(node, prefixLocals);
        }
    }

    public void SetReferencedImportDeclaration(Node node, Node @ref)
    {
        lock (_checkerMu)
        {
            _jsxLinks.Get(node)._importRef = @ref;
        }
    }

    public Node? GetReferencedImportDeclaration(Node node)
    {
        lock (_checkerMu)
        {
            if (!IsParseTreeNode(node))
            {
                return _jsxLinks.Get(node)._importRef;
            }

            var symbol = _checker.GetReferencedValueOrAliasSymbol(node);
            if (IsNonLocalAlias(symbol, SymbolFlags.Value) && _checker.GetTypeOnlyAliasDeclarationEx(symbol!, SymbolFlags.Value) is null)
            {
                return _checker.GetDeclarationOfAliasSymbol(symbol!);
            }
            return null;
        }
    }

    public Node? GetReferencedValueDeclaration(Node node)
    {
        if (!IsParseTreeNode(node))
        {
            return null;
        }

        lock (_checkerMu)
        {
            return GetReferenceResolver().GetReferencedValueDeclaration(node);
        }
    }

    public Node? GetReferencedValueDeclarationUnsafe(Identifier node) => GetReferenceResolver().GetReferencedValueDeclaration(node);

    public Node[] GetReferencedValueDeclarations(Node node)
    {
        if (!IsParseTreeNode(node))
        {
            return [];
        }

        lock (_checkerMu)
        {
            return GetReferenceResolver().GetReferencedValueDeclarations(node);
        }
    }

    // IsNameResolvable returns `true` if the given `name` resolves to any symbol at `location`
    public bool IsNameResolvable(Node location, string name)
    {
        lock (_checkerMu)
        {
            var symbol = _checker._resolveName(location, name, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/);
            return symbol is not null;
        }
    }

    public string GetElementAccessExpressionName(ElementAccessExpression? expression)
    {
        if (!IsParseTreeNode(expression))
        {
            return "";
        }

        lock (_checkerMu)
        {
            return GetReferenceResolver().GetElementAccessExpressionName(expression);
        }
    }

    public Node? GetReferencedMemberValueDeclaration(Node node)
    {
        if (!IsParseTreeNode(node))
        {
            return null;
        }

        lock (_checkerMu)
        {
            return GetReferenceResolver().GetReferencedMemberValueDeclaration(node);
        }
    }
}

/// <summary>
/// Go 的 `emitresolver.go` 里的**包级函数** → `partial class Checker` 的 static 成员（docs/architecture.md §2：
/// 包级函数进与该 package 同名的 partial class，不另开 static 类）。
/// `GetMeaningOfEntityNameReference`（`NodeCopy.cs` 调用）与 `IsConstEnumOrConstEnumOnlyModule`
/// （`Checker.C8.Aliases.cs` 调用）跨片使用，故为 `internal static`。
/// </summary>
public sealed partial class Checker
{
    internal static bool IsCommonJSModuleExports(Node node)
    {
        if (IsBinaryExpression(node) && IsExpressionStatement(node.Parent!) && IsSourceFile(node.Parent!.Parent!) &&
            node.Parent!.Parent!.AsSourceFile()!.CommonJSModuleIndicator is not null)
        {
            switch (GetAssignmentDeclarationKind(node))
            {
                case JSDeclarationKind.ModuleExports:
                case JSDeclarationKind.ExportsProperty:
                    return true;
            }
        }
        return false;
    }

    internal static SymbolFlags GetMeaningOfEntityNameReference(Node entityName)
    {
        // get symbol of the first identifier of the entityName
        if (entityName.Parent!.Kind == Kind.TypeQuery ||
            entityName.Parent.Kind == Kind.ExpressionWithTypeArguments && !IsPartOfTypeNode(entityName.Parent) ||
            entityName.Parent.Kind == Kind.ComputedPropertyName ||
            entityName.Parent.Kind == Kind.TypePredicate && entityName.Parent.AsTypePredicateNode()!.ParameterName == entityName ||
            entityName.Parent.Kind == Kind.BinaryExpression)
        {
            // Typeof value
            return SymbolFlags.Value | SymbolFlags.ExportValue;
        }
        if (entityName.Kind == Kind.QualifiedName || entityName.Kind == Kind.PropertyAccessExpression ||
            entityName.Parent.Kind == Kind.ImportEqualsDeclaration ||
            (entityName.Parent.Kind == Kind.QualifiedName && entityName.Parent.AsQualifiedName()!.Left == entityName) ||
            (entityName.Parent.Kind == Kind.PropertyAccessExpression && entityName.Parent.Expression() == entityName) ||
            (entityName.Parent.Kind == Kind.ElementAccessExpression && entityName.Parent.Expression() == entityName))
        {
            // Left identifier from type reference or TypeAlias
            // Entity name of the import declaration
            return SymbolFlags.Namespace;
        }
        // Type Reference or TypeAlias entity = Identifier
        return SymbolFlags.Type;
    }

    internal static void NoopAddVisibleAlias(Node declaration, Node aliasingStatement)
    {
    }

    internal static bool IsConstEnumOrConstEnumOnlyModule(Symbol? s) =>
        IsConstEnumSymbol(s) || (s!.Flags & SymbolFlags.ConstEnumOnlyModule) != 0;
}

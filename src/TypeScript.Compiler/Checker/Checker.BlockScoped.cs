// ported from: internal/checker/checker.go:1964-2237
//   （isBlockScopedNameDeclaredBeforeUse、isUsedInFunctionOrInstanceProperty、isImmediatelyUsedInInitializerOfBlockScopedVariable、
//     isSameScopeDescendentOf、isPropertyImmediatelyReferencedWithinDeclaration、type-only alias 三件套、
//     addTypeOnlyDeclarationRelatedInfo、getSymbol）
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `switch { case <bool 表达式>: }`（`isBlockScopedNameDeclaredBeforeUse` 里）→ C# if/else-if
//    （C# 的 switch 不支持这种"表达式 case"，改写后分支顺序与副作用求值顺序与 Go 完全一致）。
// 2) 命中 artifacts/checker-method-collisions.md 的未导出成员改名：`getTypeOfSymbol`→`GetTypeOfSymbolWorker`、
//    `getMergedSymbol`→`GetMergedSymbolWorker`、`getSymbolFlags`→`GetSymbolFlagsWorker`、
//    `resolveAlias`→`ResolveAliasWorker`、`getTypeOnlyAliasDeclaration`→`GetTypeOnlyAliasDeclarationWorker`、
//    `getImmediateAliasedSymbol`→`GetImmediateAliasedSymbolWorker`。
// 3) `ast.FindAncestorOrQuit(usage, func(current) ast.FindAncestorResult {...})` → 局部函数 + 方法组
//    （`FindAncestorResult.Quit/True/False`、`ToFindAncestorResult(bool)`）。
// 4) `debug.Assert` → `Debug.Assert`；`panic("…")` → `Debug.Fail("…")`（抛 DebugFailureException）。
// 5) 可空链上的 `ast.IsXxx(node.Parent)` 一律写 `!`（Go 里这些位置要么非 nil，要么后续解引用；Ast 的 `IsXxx`
//    参数可空性在两种实现下 `!` 都合法）。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    /// <summary>Go: checker.go:1964 <c>isBlockScopedNameDeclaredBeforeUse</c>。</summary>
    internal bool IsBlockScopedNameDeclaredBeforeUse(Node declaration, Node usage)
    {
        var declarationFile = GetSourceFileOfNode(declaration);
        var useFile = GetSourceFileOfNode(usage);
        var declContainer = GetEnclosingBlockScopeContainer(declaration);
        if (!ReferenceEquals(declarationFile, useFile))
        {
            // nodes are in different files and order cannot be determined
            return true;
        }
        // deferred usage in a type context is always OK regardless of the usage position:
        if ((usage.Flags & NodeFlags.JSDoc) != 0 || IsInTypeQuery(usage) || IsInAmbientOrTypeNode(usage))
        {
            return true;
        }
        if (declaration.Pos() <= usage.Pos() &&
            !(IsPropertyDeclaration(declaration) && IsThisProperty(usage.Parent!) && declaration.Initializer() is null && !IsExclamationToken(declaration.PostfixToken())))
        {
            // declaration is before usage
            if (declaration.Kind == Kind.BindingElement)
            {
                // still might be illegal if declaration and usage are both binding elements (eg var [a = b, b = b] = [1, 2])
                var errorBindingElement = FindAncestorKind(usage, Kind.BindingElement);
                if (errorBindingElement is not null)
                {
                    return !ReferenceEquals(FindAncestor(errorBindingElement, IsBindingElement), FindAncestor(declaration, IsBindingElement)) ||
                        declaration.Pos() < errorBindingElement.Pos();
                }
                // or it might be illegal if usage happens before parent variable is declared (eg var [a] = a)
                return IsBlockScopedNameDeclaredBeforeUse(FindAncestorKind(declaration, Kind.VariableDeclaration)!, usage);
            }
            if (declaration.Kind == Kind.VariableDeclaration)
            {
                // still might be illegal if usage is in the initializer of the variable declaration (eg var a = a)
                return !IsImmediatelyUsedInInitializerOfBlockScopedVariable(declaration, usage, declContainer);
            }
            if (IsClassLike(declaration))
            {
                // still might be illegal if the usage is within a computed property name in the class (eg class A { static p = "a"; [A.p]() {} })
                // or when used within a decorator in the class, except when used in a function that is not an IIFE
                Node? container = usage;
                while (container is not null && !ReferenceEquals(container, declaration))
                {
                    var parent = container.Parent;
                    if (IsComputedPropertyName(container) && ReferenceEquals(parent!.Parent, declaration) ||
                        !_legacyDecorators && IsDecorator(container) && (ReferenceEquals(parent, declaration) ||
                            IsMethodDeclaration(parent!) && ReferenceEquals(parent!.Parent, declaration) ||
                            IsAccessor(parent!) && ReferenceEquals(parent!.Parent, declaration) ||
                            IsPropertyDeclaration(parent!) && ReferenceEquals(parent!.Parent, declaration) ||
                            IsParameterDeclaration(parent!) && ReferenceEquals(parent!.Parent!.Parent, declaration)))
                    {
                        break;
                    }
                    container = container.Parent;
                }
                if (container is null || ReferenceEquals(container, declaration))
                {
                    return true;
                }
                if (!_legacyDecorators && IsDecorator(container))
                {
                    Node? n = usage;
                    while (n is not null && !ReferenceEquals(n, container))
                    {
                        if (IsFunctionLike(n) && GetImmediatelyInvokedFunctionExpression(n) is null)
                        {
                            break;
                        }
                        n = n.Parent;
                    }
                    return n is not null && !ReferenceEquals(n, container);
                }
                return false;
            }
            if (IsPropertyDeclaration(declaration))
            {
                // still might be illegal if a self-referencing property initializer (eg private x = this.x)
                return !IsPropertyImmediatelyReferencedWithinDeclaration(declaration, usage, false /*stopAtAnyPropertyDeclaration*/);
            }
            if (IsParameterPropertyDeclaration(declaration, declaration.Parent!))
            {
                // foo = this.bar is illegal in emitStandardClassFields when bar is a parameter property
                return !(_emitStandardClassFields && ReferenceEquals(GetContainingClass(declaration), GetContainingClass(usage)) &&
                    IsUsedInFunctionOrInstanceProperty(usage, declaration, declContainer));
            }
            return true;
        }
        // declaration is after usage, but it can still be legal if usage is deferred:
        // 1. inside an export specifier
        // 2. inside a function
        // 3. inside an instance property initializer, a reference to a non-instance property
        //    (except when emitStandardClassFields: true and the reference is to a parameter property)
        // 4. inside a static property initializer, a reference to a static method in the same class
        // 5. inside a TS export= declaration (since we will move the export statement during emit to avoid TDZ)
        if (IsExportSpecifier(usage.Parent!) || IsExportAssignment(usage.Parent!) && usage.Parent!.AsExportAssignment()!.IsExportEquals)
        {
            // export specifiers do not use the variable, they only make it available for use
            return true;
        }
        // When resolving symbols for exports, the `usage` location passed in can be the export site directly
        if (IsExportAssignment(usage) && usage.AsExportAssignment()!.IsExportEquals)
        {
            return true;
        }
        if (IsUsedInFunctionOrInstanceProperty(usage, declaration, declContainer))
        {
            if (_emitStandardClassFields && GetContainingClass(declaration) is not null &&
                (IsPropertyDeclaration(declaration) || IsParameterPropertyDeclaration(declaration, declaration.Parent!)))
            {
                return !IsPropertyImmediatelyReferencedWithinDeclaration(declaration, usage, true /*stopAtAnyPropertyDeclaration*/);
            }
            return true;
        }
        return false;
    }

    /// <summary>Go: checker.go:2053 <c>isUsedInFunctionOrInstanceProperty</c>。</summary>
    internal bool IsUsedInFunctionOrInstanceProperty(Node usage, Node declaration, Node? declContainer)
    {
        FindAncestorResult Callback(Node current)
        {
            if (ReferenceEquals(current, declContainer))
            {
                return FindAncestorResult.Quit;
            }
            if (IsFunctionLike(current))
            {
                return ToFindAncestorResult(GetImmediatelyInvokedFunctionExpression(current) is null);
            }
            if (IsClassStaticBlockDeclaration(current))
            {
                return ToFindAncestorResult(declaration.Pos() < usage.Pos());
            }
            if (current.Parent is not null && IsPropertyDeclaration(current.Parent))
            {
                var propertyDeclaration = current.Parent;
                var initializerOfProperty = ReferenceEquals(propertyDeclaration.Initializer(), current);
                if (initializerOfProperty)
                {
                    if (IsStatic(current.Parent))
                    {
                        if (IsMethodDeclaration(declaration))
                        {
                            return FindAncestorResult.True;
                        }
                        if (IsPropertyDeclaration(declaration) && ReferenceEquals(GetContainingClass(usage), GetContainingClass(declaration)))
                        {
                            var propName = declaration.Name();
                            if (propName is not null && (IsIdentifier(propName) || IsPrivateIdentifier(propName)))
                            {
                                var t = GetTypeOfSymbolWorker(GetSymbolOfDeclaration(declaration)!);
                                var staticBlocks = Filter(declaration.Parent!.Members(), IsClassStaticBlockDeclaration);
                                if (IsPropertyInitializedInStaticBlocks(propName, t, staticBlocks, declaration.Parent.Pos(), current.Pos()))
                                {
                                    return FindAncestorResult.True;
                                }
                            }
                        }
                    }
                    else
                    {
                        var isDeclarationInstanceProperty = IsPropertyDeclaration(declaration) && !IsStatic(declaration);
                        if (!isDeclarationInstanceProperty || !ReferenceEquals(GetContainingClass(usage), GetContainingClass(declaration)))
                        {
                            return FindAncestorResult.True;
                        }
                    }
                }
            }
            if (current.Parent is not null && IsDecorator(current.Parent))
            {
                var decorator = current.Parent.AsDecorator()!;
                if (ReferenceEquals(decorator.Expression, current))
                {
                    if (IsParameterDeclaration(decorator.Parent!))
                    {
                        if (IsUsedInFunctionOrInstanceProperty(decorator.Parent!.Parent!.Parent!, declaration, declContainer))
                        {
                            return FindAncestorResult.True;
                        }
                        return FindAncestorResult.Quit;
                    }
                    if (IsMethodDeclaration(decorator.Parent!))
                    {
                        if (IsUsedInFunctionOrInstanceProperty(decorator.Parent!.Parent!, declaration, declContainer))
                        {
                            return FindAncestorResult.True;
                        }
                        return FindAncestorResult.Quit;
                    }
                }
            }
            return FindAncestorResult.False;
        }

        return FindAncestorOrQuit(usage, Callback) is not null;
    }

    /// <summary>Go: checker.go:2114 <c>isImmediatelyUsedInInitializerOfBlockScopedVariable</c>（包级函数 → static 成员）。</summary>
    internal static bool IsImmediatelyUsedInInitializerOfBlockScopedVariable(Node declaration, Node usage, Node? declContainer)
    {
        switch (declaration.Parent!.Parent!.Kind)
        {
            case Kind.VariableStatement:
            case Kind.ForStatement:
            case Kind.ForOfStatement:
                // variable statement/for/for-of statement case,
                // use site should not be inside variable declaration (initializer of declaration or binding element)
                if (IsSameScopeDescendentOf(usage, declaration, declContainer))
                {
                    return true;
                }
                break;
        }
        // ForIn/ForOf case - use site should not be used in expression part
        var grandparent = declaration.Parent.Parent;
        return IsForInOrOfStatement(grandparent) && IsSameScopeDescendentOf(usage, grandparent.Expression()!, declContainer);
    }

    // Starting from 'initial' node walk up the parent chain until 'stopAt' node is reached.
    // If at any point current node is equal to 'parent' node - return true.
    // If current node is an IIFE, continue walking up.
    // Return false if 'stopAt' node is reached or isFunctionLike(current) === true.
    /// <summary>Go: checker.go:2132 <c>isSameScopeDescendentOf</c>（包级函数 → static 成员）。</summary>
    internal static bool IsSameScopeDescendentOf(Node initial, Node? parent, Node? stopAt)
    {
        if (parent is null)
        {
            return false;
        }
        for (Node? n = initial; n is not null; n = n.Parent)
        {
            if (ReferenceEquals(n, parent))
            {
                return true;
            }
            if (ReferenceEquals(n, stopAt) || IsFunctionLike(n) && (GetImmediatelyInvokedFunctionExpression(n) is null || (GetFunctionFlags(n) & FunctionFlags.AsyncGenerator) != 0))
            {
                return false;
            }
        }
        return false;
    }

    // isPropertyImmediatelyReferencedWithinDeclaration is used for detecting ES-standard class field use-before-def errors
    /// <summary>Go: checker.go:2148 <c>isPropertyImmediatelyReferencedWithinDeclaration</c>（包级函数 → static 成员）。</summary>
    internal static bool IsPropertyImmediatelyReferencedWithinDeclaration(Node declaration, Node usage, bool stopAtAnyPropertyDeclaration)
    {
        // always legal if usage is after declaration
        if (usage.End() > declaration.End())
        {
            return false;
        }
        // still might be legal if usage is deferred (e.g. x: any = () => this.x)
        // otherwise illegal if immediately referenced within the declaration (e.g. x: any = this.x)
        for (Node? node = usage; node is not null && !ReferenceEquals(node, declaration); node = node.Parent)
        {
            switch (node.Kind)
            {
                case Kind.ArrowFunction:
                    return false;
                case Kind.PropertyDeclaration:
                    // even when stopping at any property declaration, they need to come from the same class
                    return stopAtAnyPropertyDeclaration &&
                        ((IsPropertyDeclaration(declaration) && ReferenceEquals(node.Parent, declaration.Parent)) ||
                            (IsParameterPropertyDeclaration(declaration, declaration.Parent!) && ReferenceEquals(node.Parent, declaration.Parent!.Parent)));
                case Kind.Block:
                    switch (node.Parent!.Kind)
                    {
                        case Kind.MethodDeclaration:
                        case Kind.GetAccessor:
                        case Kind.SetAccessor:
                            return false;
                    }
                    break;
            }
        }
        return true;
    }

    // Return the type-only declaration node (if any) for the given alias symbol (non-transitively)
    /// <summary>Go: checker.go:2175 <c>getTypeOnlyAliasDeclaration</c>（与导出 API 同名 → Worker 后缀）。</summary>
    internal Node? GetTypeOnlyAliasDeclarationWorker(Symbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            ResolveAliasWorker(symbol);
            return _aliasSymbolLinks.Get(symbol)._typeOnlyDeclaration;
        }
        return null;
    }

    // Return the first type-only alias declaration node (if any) in the resolution chain that affects
    // the symbol for the given meaning
    /// <summary>Go: checker.go:2185 <c>getTypeOnlyAliasDeclarationEx</c>。</summary>
    internal Node? GetTypeOnlyAliasDeclarationEx(Symbol symbol, SymbolFlags meaning)
    {
        while ((symbol.Flags & SymbolFlags.Alias) != 0 && (symbol.Flags & meaning) == 0)
        {
            var resolved = ResolveAliasWorker(symbol);
            var links = _aliasSymbolLinks.Get(symbol);
            if (links._typeOnlyDeclaration is not null)
            {
                return links._typeOnlyDeclaration;
            }
            symbol = resolved;
        }
        return null;
    }

    /// <summary>Go: checker.go:2197 <c>getImmediateAliasedSymbol</c>（与导出 API 同名 → Worker 后缀）。</summary>
    internal Symbol? GetImmediateAliasedSymbolWorker(Symbol symbol)
    {
        Debug.Assert((symbol.Flags & SymbolFlags.Alias) != 0, "Should only get Alias here.");
        var links = _aliasSymbolLinks.Get(symbol);
        if (links._immediateTarget is null)
        {
            var node = GetDeclarationOfAliasSymbol(symbol);
            if (node is null)
            {
                Debug.Fail("Unexpected nil in getImmediateAliasedSymbol");
            }
            links._immediateTarget = GetTargetOfAliasDeclaration(node!);
        }
        return links._immediateTarget;
    }

    /// <summary>Go: checker.go:2210 <c>addTypeOnlyDeclarationRelatedInfo</c>。</summary>
    internal Diagnostic AddTypeOnlyDeclarationRelatedInfo(Diagnostic diagnostic, Node? typeOnlyDeclaration, string name)
    {
        if (typeOnlyDeclaration is null)
        {
            return diagnostic;
        }
        var isExport = IsExportSpecifier(typeOnlyDeclaration) || IsExportDeclaration(typeOnlyDeclaration) || IsNamespaceExport(typeOnlyDeclaration);
        return diagnostic.AddRelatedInfo(NewDiagnosticForNode(typeOnlyDeclaration, IfElse(isExport, Diagnostics.X_0_was_exported_here, Diagnostics.X_0_was_imported_here), name));
    }

    /// <summary>Go: checker.go:2218 <c>getSymbol</c>。</summary>
    internal Symbol? GetSymbol(SymbolTable symbols, string name, SymbolFlags meaning)
    {
        if ((meaning & SymbolFlags.All) != 0)
        {
            var candidate = symbols.GetValueOrDefault(name);
            var symbol = candidate is null ? null : GetMergedSymbolWorker(candidate);
            if (symbol is not null)
            {
                if ((symbol.Flags & meaning) != 0)
                {
                    return symbol;
                }
                if ((symbol.Flags & SymbolFlags.Alias) != 0)
                {
                    var targetFlags = GetSymbolFlagsWorker(symbol);
                    // `targetFlags` will be `SymbolFlags.All` if an error occurred in alias resolution; this avoids cascading errors
                    if ((targetFlags & meaning) != 0)
                    {
                        return symbol;
                    }
                }
            }
        }
        // return nil if we can't find a symbol
        return null;
    }
}

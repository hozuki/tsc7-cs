// ported from: internal/binder/binder.go:752-1305（声明绑定与名字查找：bindPropertyWorker / bindSourceFileIfExternalModule /
//   bindSourceFileAsExternalModule / bindModuleDeclaration / declareModuleSymbol / bindNamespaceExportDeclaration /
//   bindImportClause / bindExportDeclaration / bindExportAssignment / bindJsxAttributes / bindJsxAttribute /
//   setExportContextFlag / hasExportDeclarations / bindFunctionExpression / bindCallExpression / setCommonJSModuleIndicator /
//   bindClassLikeDeclaration / bindPropertyOrMethodOrAccessor / bindFunctionOrConstructorType /
//   addLateBoundAssignmentDeclarationToSymbol / bindModuleExportsAssignment / bindExpandoPropertyAssignment /
//   bindDeferredExpandoAssignments / bindCommonJSTypeExports / bindDeferredExpandoAssignment /
//   getParentOfPropertyAssignment / bindExportsOrObjectDefineProperty / getInitializerSymbol / bindThisPropertyAssignment /
//   getThisClassAndSymbolTable / bindEnumDeclaration / bindVariableDeclarationOrBindingElement / bindParameter /
//   bindFunctionDeclaration / getInferTypeContainer / bindAnonymousDeclaration / bindBlockScopedDeclaration /
//   bindTypeParameter / lookupEntity / lookupName）
//
// 与 Go 的差异（其余逐行对应）：
// - Go 的 `(b *Binder)` 方法 → `private` 实例方法（方法名 PascalCase；Go 的包级未导出函数
//   getParentOfPropertyAssignment / getInitializerSymbol → `private static`）。见 CONTRACT.md §1。
// - Go 的 `*ast.Node`/`*ast.Symbol`/`*ast.SymbolTable` → `Node?`/`Symbol?`/`SymbolTable?`（Go 里可能为 nil 的指针）。
// - 包级函数/常量按 `using static` 后的**裸名**调用（`GetExports(...)`、`SymbolFlags.Xxx`、诊断消息
//   `Modifiers_cannot_appear_here`…）：本文件在 `TypeScript.Binder` 命名空间里，写限定名 `Ast.GetExports(...)` /
//   `Diagnostics.Modifiers_cannot_appear_here` 会先解析到同级的**命名空间** `TypeScript.Ast`/`TypeScript.Diagnostics`，
//   C# 只对命名空间做成员查找、不落到静态类成员（实测 CS0234；见 Scanner/Utilities.cs 文件头第 9 条，
//   该文件的 using 也同样写在 namespace 之前）。
// - `panic(...)` → `Debug.Fail(...)`（抛 DebugFailureException）；Debug.Fail 没有 `[DoesNotReturn]`，所以其后补
//   `throw new System.Diagnostics.UnreachableException()`（与 Parser.Statements.cs、Core/CompilerOptions.cs 一致）。
// - Go 的 `&^=` → `&= ~`；Go 的 `&` 优先级高于 `==`/`!=`，C# 相反，`x&y != 0` 一律写成 `(x & y) != 0`。
// - Go map 读 `m[k]`（缺键 = nil）→ `GetValueOrDefault(k)`；整体为 nil 的 map（Go 的 nil `SymbolTable`）→ `?.`。
// - Go 的 `append(slice, x)` → 集合表达式 `[.. slice ?? [], x]`（数组不能就地变长，Go 也是返回新切片）；
//   `append(list, x)`（`_expandoAssignments` 是 `List<ExpandoAssignmentInfo>`）→
//   `.Add(new ExpandoAssignmentInfo { _node = …, _container = …, _blockScopeContainer = … })`（字段名见 CONTRACT.md §1.3）。
// - Go 的 `make(ast.SymbolTable)` → `new SymbolTable()`。
// - Go 的 `slices.Index(...)` → `Array.IndexOf(...)`（未命中同样返回 -1）；`strconv.Itoa(n)` →
//   `n.ToString(CultureInfo.InvariantCulture)`。
// - Go 的 `core.IfElse/OrElse/Some/TryParsePattern` → 裸名（`using static TypeScript.Core.Core`）；
//   `tspath.RemoveFileExtension` → 裸名（`using static TypeScript.TSPath.TSPath`）。
// - Go 的无标签 `switch { case …: }` → `if`/`else if` 链；Go 的 `fallthrough`（bindBlockScopedDeclaration）→ C# 的
//   `goto default;`（C# 的 switch 不允许落空到下一段）。
// - Go 的具名多返回值 `(classSymbol *ast.Symbol, symbolTable ast.SymbolTable)`（getThisClassAndSymbolTable）→ C# 元组
//   `(Symbol? classSymbol, SymbolTable? symbolTable)`；Go 的具名零值（nil）落成方法开头的局部变量初始化。
// - Go 的 `b.container`/`b.blockScopeContainer`/`b.thisContainer`/`b.file.Symbol` 在 Go 里是非空指针，C# 字段是 `Node?`；
//   多处解引用的地方先取一次局部变量（`var container = _container!;`）再照 Go 重复使用，等价且只写一次 `!`。
//   其余按同一手法：Go 在 `if x != nil` 之后的解引用，C# 用 `!` 或流分析后的直接访问。
// - Go 的 `&b.file.Symbol.Exports` 需要一个可 `ref` 的左值：C# 先取局部变量 `fileSymbol`（同一对象）再
//   `ref fileSymbol.Exports`（与 Ast/Utilities.cs 的 GetMembers/GetExports 同一写法，语义相同）。
// - `_notConstEnumOnlyModules` 是 `Set<Symbol>`（非空；Go 的 nil Set ≡ 空 Set，Has=false，已在 Binder.cs 声明）。
// - `getInitializerSymbol` 的参数在 Go 里可为 nil（`getInitializerSymbol(nil)` 返回 nil）→ C# 参数写 `Symbol?`。
// - `getOptionalSymbolFlagForNode`（Go:2756，主 agent 的 Binder.cs）按契约命名为 `GetOptionalSymbolFlagForNode`。
using System;
using System.Globalization;
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Binder;

public sealed partial class Binder
{
    private void BindPropertyWorker(Node node)
    {
        var isAutoAccessor = IsAutoAccessorPropertyDeclaration(node);
        var includes = IfElse(isAutoAccessor, SymbolFlags.Accessor, SymbolFlags.Property);
        var excludes = IfElse(isAutoAccessor, SymbolFlags.AccessorExcludes, SymbolFlags.PropertyExcludes);
        BindPropertyOrMethodOrAccessor(node, includes | GetOptionalSymbolFlagForNode(node), excludes);
    }

    private void BindSourceFileIfExternalModule()
    {
        SetExportContextFlag(_file.AsNode());
        if (IsExternalOrCommonJSModule(_file))
        {
            BindSourceFileAsExternalModule();
        }
        else if (IsJsonSourceFile(_file))
        {
            BindSourceFileAsExternalModule();
            // Create symbol equivalent for the module.exports = {}
            var originalSymbol = _file.Symbol;
            var fileSymbol = _file.Symbol!;
            DeclareSymbol(GetSymbolTable(ref fileSymbol.Exports), fileSymbol, _file.AsNode(), SymbolFlags.Property, SymbolFlags.All);
            _file.Symbol = originalSymbol;
        }
    }

    private void BindSourceFileAsExternalModule()
    {
        BindAnonymousDeclaration(_file.AsNode(), SymbolFlags.ValueModule, "\"" + RemoveFileExtension(_file.FileName()) + "\"");
    }

    private void BindModuleDeclaration(Node node)
    {
        SetExportContextFlag(node);
        if (IsAmbientModule(node))
        {
            if (HasSyntacticModifier(node, ModifierFlags.Export))
            {
                ErrorOnFirstToken(node, X_export_modifier_cannot_be_applied_to_ambient_modules_and_module_augmentations_since_they_are_always_visible);
            }
            if (IsModuleAugmentationExternal(node))
            {
                DeclareModuleSymbol(node);
            }
            else
            {
                var name = node.AsModuleDeclaration()!.Name()!;
                var symbol = DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.ValueModule, SymbolFlags.ValueModuleExcludes);

                if (IsStringLiteral(name))
                {
                    var attributes = node.AsModuleDeclaration()!.Attributes;
                    var pattern = TryParsePattern(name.Text());
                    if (!pattern.IsValid())
                    {
                        // An invalid pattern - must have multiple wildcards.
                        ErrorOnFirstToken(name, Pattern_0_can_have_at_most_one_Asterisk_character, name.Text());
                    }
                    else if (pattern.StarIndex >= 0)
                    {
                        _file.PatternAmbientModules = [.. _file.PatternAmbientModules ?? [], new PatternAmbientModule { Pattern = pattern, Symbol = symbol }];
                    }
                    else if (attributes is not null)
                    {
                        ErrorOnNode(name, An_ambient_module_declaration_with_import_attributes_must_use_a_pattern_name_with_an_Asterisk_character);
                    }
                }
            }
        }
        else
        {
            var state = DeclareModuleSymbol(node);
            if (state != ModuleInstanceState.NonInstantiated)
            {
                var symbol = node.Symbol!;
                // if module was already merged with some function, class or non-const enum, treat it as non-const-enum-only
                var constEnumOnlyModule = (symbol.Flags & (SymbolFlags.Function | SymbolFlags.Class | SymbolFlags.RegularEnum)) == 0 &&
                    // Current must be `const enum` only
                    state == ModuleInstanceState.ConstEnumOnly &&
                    // Can't have been set to 'false' in a previous merged symbol. ('undefined' OK)
                    !_notConstEnumOnlyModules.Has(symbol);
                if (constEnumOnlyModule)
                {
                    symbol.Flags |= SymbolFlags.ConstEnumOnlyModule;
                }
                else
                {
                    symbol.Flags &= ~SymbolFlags.ConstEnumOnlyModule;
                    _notConstEnumOnlyModules.Add(symbol);
                }
            }
        }
    }

    private ModuleInstanceState DeclareModuleSymbol(Node node)
    {
        var state = GetModuleInstanceState(node);
        var instantiated = state != ModuleInstanceState.NonInstantiated;
        DeclareSymbolAndAddToSymbolTable(node, IfElse(instantiated, SymbolFlags.ValueModule, SymbolFlags.NamespaceModule), IfElse(instantiated, SymbolFlags.ValueModuleExcludes, SymbolFlags.NamespaceModuleExcludes));
        return state;
    }

    private void BindNamespaceExportDeclaration(Node node)
    {
        if (node.Modifiers() is not null)
        {
            ErrorOnNode(node, Modifiers_cannot_appear_here);
        }
        if (!IsSourceFile(node.Parent!))
        {
            ErrorOnNode(node, Global_module_exports_may_only_appear_at_top_level);
        }
        else if (!IsExternalModule(node.Parent!.AsSourceFile()))
        {
            ErrorOnNode(node, Global_module_exports_may_only_appear_in_module_files);
        }
        else if (!node.Parent!.AsSourceFile()!.IsDeclarationFile)
        {
            ErrorOnNode(node, Global_module_exports_may_only_appear_in_declaration_files);
        }
        else
        {
            DeclareSymbol(GetSymbolTable(ref _file.GlobalExports), _file.Symbol, node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
        }
    }

    private void BindImportClause(Node node)
    {
        if (node.AsImportClause()!.Name() is not null)
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
        }
    }

    private void BindExportDeclaration(Node node)
    {
        var decl = node.AsExportDeclaration()!;
        var container = _container!;
        if (container.Symbol is null)
        {
            // Export * in some sort of block construct
            BindAnonymousDeclaration(node, SymbolFlags.ExportStar, GetDeclarationName(node));
        }
        else if (decl.ExportClause is null)
        {
            // All export * declarations are collected in an __export symbol
            DeclareSymbol(GetExports(container.Symbol), container.Symbol, node, SymbolFlags.ExportStar, SymbolFlags.None);
        }
        else if (IsNamespaceExport(decl.ExportClause!))
        {
            DeclareSymbol(GetExports(container.Symbol), container.Symbol, decl.ExportClause, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
        }
    }

    private void BindExportAssignment(Node node)
    {
        var container = _container!;
        if (container.Symbol is null && IsExportAssignment(node))
        {
            // Incorrect export assignment in some sort of block construct
            BindAnonymousDeclaration(node, SymbolFlags.Value, GetDeclarationName(node));
        }
        else
        {
            // If there is an `export default x;` alias declaration, can't `export default` anything else.
            // (In contrast, you can still have `export default function f() {}` and `export default interface I {}`.)
            var flags = IfElse(ExpressionIsAlias(node.Expression()), SymbolFlags.Alias, SymbolFlags.Property);
            var symbol = DeclareSymbol(GetExports(container.Symbol), container.Symbol, node, flags, SymbolFlags.All);
            if (node.AsExportAssignment()!.IsExportEquals)
            {
                // Ensure export assignments have a ValueDeclaration set.
                SetValueDeclaration(symbol, node);
            }
        }
    }

    private void BindJsxAttributes(Node node)
    {
        BindAnonymousDeclaration(node, SymbolFlags.ObjectLiteral, InternalSymbolNameJSXAttributes);
    }

    private void BindJsxAttribute(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        DeclareSymbolAndAddToSymbolTable(node, symbolFlags, symbolExcludes);
    }

    private void SetExportContextFlag(Node node)
    {
        // A declaration source file or ambient module declaration that contains no export declarations (but possibly regular
        // declarations with export modifiers) is an export context in which declarations are implicitly exported.
        if ((node.Flags & NodeFlags.Ambient) != 0 && !HasExportDeclarations(node))
        {
            node.Flags |= NodeFlags.ExportContext;
        }
        else
        {
            node.Flags &= ~NodeFlags.ExportContext;
        }
    }

    private bool HasExportDeclarations(Node node)
    {
        Node[]? statements = null;
        switch (node.Kind)
        {
            case Kind.SourceFile:
                statements = node.Statements();
                break;
            case Kind.ModuleDeclaration:
                var body = node.Body();
                if (body is not null && IsModuleBlock(body))
                {
                    statements = body.Statements();
                }
                break;
        }
        return Some(statements, s => IsExportDeclaration(s) || IsExportAssignment(s));
    }

    private void BindFunctionExpression(Node node)
    {
        if (!_file.IsDeclarationFile && (node.Flags & NodeFlags.Ambient) == 0 && IsAsyncFunction(node))
        {
            _emitFlags |= NodeFlags.HasAsyncFunctions;
        }
        SetFlowNode(node, _currentFlow);
        var bindingName = InternalSymbolNameFunction;
        if (IsFunctionExpression(node) && node.AsFunctionExpression()!.Name() is not null)
        {
            CheckStrictModeFunctionName(node);
            bindingName = node.AsFunctionExpression()!.Name()!.Text();
        }
        BindAnonymousDeclaration(node, SymbolFlags.Function, bindingName);
    }

    private void BindCallExpression(Node node)
    {
        // We're only inspecting call expressions to detect CommonJS modules, so we can skip
        // this check if we've already seen the module indicator
        if (_file.CommonJSModuleIndicator is null && IsRequireCall(node, false /*requireStringLiteralLikeArgument*/))
        {
            SetCommonJSModuleIndicator(node);
        }
    }

    private bool SetCommonJSModuleIndicator(Node node)
    {
        if (_file.ExternalModuleIndicator is not null && _file.ExternalModuleIndicator != _file.AsNode())
        {
            return false;
        }
        if (_file.CommonJSModuleIndicator is null)
        {
            _file.CommonJSModuleIndicator = node;
            if (_file.ExternalModuleIndicator is null)
            {
                BindSourceFileAsExternalModule();
            }
        }
        return true;
    }

    private void BindClassLikeDeclaration(Node node)
    {
        var name = node.Name();
        switch (node.Kind)
        {
            case Kind.ClassDeclaration:
                BindBlockScopedDeclaration(node, SymbolFlags.Class, SymbolFlags.ClassExcludes);
                break;
            case Kind.ClassExpression:
                var nameText = InternalSymbolNameClass;
                if (name is not null)
                {
                    nameText = name.Text();
                }
                BindAnonymousDeclaration(node, SymbolFlags.Class, nameText);
                break;
        }
        var symbol = node.Symbol!;
        // TypeScript 1.0 spec (April 2014): 8.4
        // Every class automatically contains a static property member named 'prototype', the
        // type of which is an instantiation of the class type with type Any supplied as a type
        // argument for each type parameter. It is an error to explicitly declare a static
        // property member with the name 'prototype'.
        //
        // Note: we check for this here because this class may be merging into a module.  The
        // module might have an exported variable called 'prototype'.  We can't allow that as
        // that would clash with the built-in 'prototype' for the class.
        var prototypeSymbol = NewSymbol(SymbolFlags.Property | SymbolFlags.Prototype, "prototype");
        var symbolExport = GetExports(symbol).GetValueOrDefault(prototypeSymbol.Name);
        if (symbolExport is not null)
        {
            ErrorOnNode(symbolExport.Declarations![0], Duplicate_identifier_0, SymbolName(prototypeSymbol));
        }
        GetExports(symbol)[prototypeSymbol.Name] = prototypeSymbol;
        prototypeSymbol.Parent = symbol;
    }

    private void BindPropertyOrMethodOrAccessor(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        if (!_file.IsDeclarationFile && (node.Flags & NodeFlags.Ambient) == 0 && IsAsyncFunction(node))
        {
            _emitFlags |= NodeFlags.HasAsyncFunctions;
        }
        if (_currentFlow is not null && IsObjectLiteralOrClassExpressionMethodOrAccessor(node))
        {
            SetFlowNode(node, _currentFlow);
        }
        if (HasDynamicName(node))
        {
            BindAnonymousDeclaration(node, symbolFlags, InternalSymbolNameComputed);
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable(node, symbolFlags, symbolExcludes);
        }
    }

    private void BindFunctionOrConstructorType(Node node)
    {
        // For a given function symbol "<...>(...) => T" we want to generate a symbol identical
        // to the one we would get for: { <...>(...): T }
        //
        // We do that by making an anonymous type literal symbol, and then setting the function
        // symbol as its sole member. To the rest of the system, this symbol will be indistinguishable
        // from an actual type literal symbol you would have gotten had you used the long form.
        var symbol = NewSymbol(SymbolFlags.Signature, GetDeclarationName(node));
        AddDeclarationToSymbol(symbol, node, SymbolFlags.Signature);
        var typeLiteralSymbol = NewSymbol(SymbolFlags.TypeLiteral, InternalSymbolNameType);
        AddDeclarationToSymbol(typeLiteralSymbol, node, SymbolFlags.TypeLiteral);
        typeLiteralSymbol.Members = new SymbolTable();
        typeLiteralSymbol.Members[symbol.Name] = symbol;
    }

    private void AddLateBoundAssignmentDeclarationToSymbol(Node node, Symbol symbol)
    {
        var exports = GetExports(symbol);
        var assignmentSymbol = exports.GetValueOrDefault(InternalSymbolNameAssignmentDeclaration);
        if (assignmentSymbol is null)
        {
            assignmentSymbol = NewSymbol(SymbolFlags.None, InternalSymbolNameAssignmentDeclaration);
            exports[InternalSymbolNameAssignmentDeclaration] = assignmentSymbol;
        }
        assignmentSymbol.Declarations = [.. assignmentSymbol.Declarations ?? [], node];
    }

    private void BindModuleExportsAssignment(Node node)
    {
        if (SetCommonJSModuleIndicator(node))
        {
            var container = _file.AsNode();
            var flags = IfElse(ExpressionIsAlias(node.AsBinaryExpression()!.Right), SymbolFlags.Alias, SymbolFlags.Property);
            var symbol = DeclareSymbol(GetExports(container.Symbol), container.Symbol, node, flags, 0);
            SetValueDeclaration(symbol, node);
        }
    }

    private void BindExpandoPropertyAssignment(Node node)
    {
        _expandoAssignments.Add(new ExpandoAssignmentInfo
        {
            _node = node,
            _container = _container,
            _blockScopeContainer = _blockScopeContainer,
        });
    }

    private void BindDeferredExpandoAssignments()
    {
        foreach (var info in _expandoAssignments)
        {
            _container = info._container;
            _blockScopeContainer = info._blockScopeContainer;
            BindDeferredExpandoAssignment(info._node!);
        }
    }

    // If the given module symbol has an export= symbol, promote exports with a type or namespace meaning
    // from the module symbol onto the export= symbol and, if any such exports exist, mark the export=
    // symbol as a namespace module.
    private void BindCommonJSTypeExports(Symbol moduleSymbol)
    {
        var moduleExports = moduleSymbol.Exports;
        if (moduleExports is not null)
        {
            var exportEquals = moduleExports.GetValueOrDefault(InternalSymbolNameExportEquals);
            if (exportEquals is not null)
            {
                foreach (var symbol in moduleExports.Values)
                {
                    if (symbol.Name != InternalSymbolNameExportEquals && (symbol.Flags & (SymbolFlags.Type | SymbolFlags.Namespace)) != 0)
                    {
                        GetExports(exportEquals)[symbol.Name] = symbol;
                        exportEquals.Flags |= SymbolFlags.NamespaceModule;
                    }
                }
            }
        }
    }

    private void BindDeferredExpandoAssignment(Node node)
    {
        var parent = GetParentOfPropertyAssignment(node);
        var symbol = LookupEntity(parent!, _blockScopeContainer);
        if (symbol is null)
        {
            symbol = LookupEntity(parent!, _container);
        }
        symbol = GetInitializerSymbol(symbol);
        if (symbol is not null)
        {
            if (HasDynamicName(node))
            {
                BindAnonymousDeclaration(node, SymbolFlags.Property | SymbolFlags.Assignment, InternalSymbolNameComputed);
                AddLateBoundAssignmentDeclarationToSymbol(node, symbol);
            }
            else
            {
                // We declare expandos only when there are no non-expando declarations for that name.
                var exports = GetExports(symbol);
                var existing = exports.GetValueOrDefault(GetDeclarationName(node));
                if (existing is null || (existing.Flags & SymbolFlags.Assignment) != 0)
                {
                    DeclareSymbol(exports, symbol, node, SymbolFlags.Property | SymbolFlags.Assignment, SymbolFlags.PropertyExcludes);
                }
            }
        }
    }

    private static Node? GetParentOfPropertyAssignment(Node node)
    {
        switch (node.Kind)
        {
            case Kind.BinaryExpression:
                return node.AsBinaryExpression()!.Left!.Expression();
            case Kind.CallExpression:
                return node.Arguments()![0];
        }
        Debug.Fail("Unhandled case in getParentOfPropertyAssignment");
        throw new System.Diagnostics.UnreachableException();
    }

    private void BindExportsOrObjectDefineProperty(Node node)
    {
        if (SetCommonJSModuleIndicator(node))
        {
            var container = _file.AsNode();
            var flags = IfElse(IsBinaryExpression(node) && ExpressionIsAlias(node.AsBinaryExpression()!.Right), SymbolFlags.Alias, SymbolFlags.FunctionScopedVariable);
            DeclareSymbol(GetExports(container.Symbol), container.Symbol, node, flags, SymbolFlags.FunctionScopedVariableExcludes);
        }
    }

    private static Symbol? GetInitializerSymbol(Symbol? symbol)
    {
        if (symbol is null || symbol.ValueDeclaration is null)
        {
            return null;
        }
        var declaration = symbol.ValueDeclaration;
        // For an assignment 'fn.xxx = ...', where 'fn' is a previously declared function or a previously
        // declared const variable initialized with a function expression or arrow function, we add expando
        // property declarations to the function's symbol. This also applies to class expressions in JS files,
        // and empty object literals in JS files when the declaration doesn't have a type annotation.
        if (IsFunctionDeclaration(declaration) || IsInJSFile(declaration) && IsClassDeclaration(declaration))
        {
            return symbol;
        }
        if (IsVariableDeclaration(declaration) &&
            ((declaration.Parent!.Flags & NodeFlags.Const) != 0 || IsInJSFile(declaration)))
        {
            var initializer = declaration.Initializer();
            if (IsExpandoInitializer(declaration, initializer))
            {
                return initializer!.Symbol;
            }
        }
        else if (IsBinaryExpression(declaration) && IsInJSFile(declaration))
        {
            var initializer = declaration.AsBinaryExpression()!.Right;
            if (IsExpandoInitializer(declaration, initializer))
            {
                return initializer!.Symbol;
            }
        }
        return null;
    }

    private void BindThisPropertyAssignment(Node node)
    {
        if (!IsInJSFile(node))
        {
            return;
        }
        var bin = node.AsBinaryExpression()!;
        if (IsPropertyAccessExpression(bin.Left!) && IsPrivateIdentifier(bin.Left!.AsPropertyAccessExpression()!.Name()!) ||
            _thisContainer is null)
        {
            return;
        }
        var (classSymbol, symbolTable) = GetThisClassAndSymbolTable();
        if (symbolTable is not null)
        {
            if (HasDynamicName(node))
            {
                DeclareSymbolEx(symbolTable, classSymbol, node, SymbolFlags.Property, SymbolFlags.None, true /*isReplaceableByMethod*/, true /*isComputedName*/);
                AddLateBoundAssignmentDeclarationToSymbol(node, classSymbol!);
            }
            else
            {
                DeclareSymbolEx(symbolTable, classSymbol, node, SymbolFlags.Property | SymbolFlags.Assignment, SymbolFlags.None, true /*isReplaceableByMethod*/, false /*isComputedName*/);
            }
        }
        else if (_thisContainer!.Kind != Kind.FunctionDeclaration && _thisContainer.Kind != Kind.FunctionExpression)
        {
            // !!! constructor functions
            Debug.Fail("Unhandled case in bindThisPropertyAssignment: " + _thisContainer.Kind.String());
            throw new System.Diagnostics.UnreachableException();
        }
    }

    private (Symbol? classSymbol, SymbolTable? symbolTable) GetThisClassAndSymbolTable()
    {
        var thisContainer = _thisContainer;
        if (thisContainer is null)
        {
            return (null, null);
        }
        Symbol? classSymbol = null;
        SymbolTable? symbolTable = null;
        switch (thisContainer.Kind)
        {
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
                // !!! constructor functions
                break;
            case Kind.Constructor:
            case Kind.PropertyDeclaration:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.ClassStaticBlockDeclaration:
                // this.property assignment in class member -- bind to the containing class
                classSymbol = thisContainer.Parent!.Symbol;
                if (IsStatic(thisContainer))
                {
                    symbolTable = GetExports(classSymbol);
                }
                else
                {
                    symbolTable = GetMembers(classSymbol);
                }
                break;
        }
        return (classSymbol, symbolTable);
    }

    private void BindEnumDeclaration(Node node)
    {
        if (IsEnumConst(node))
        {
            BindBlockScopedDeclaration(node, SymbolFlags.ConstEnum, SymbolFlags.ConstEnumExcludes);
        }
        else
        {
            BindBlockScopedDeclaration(node, SymbolFlags.RegularEnum, SymbolFlags.RegularEnumExcludes);
        }
    }

    private void BindVariableDeclarationOrBindingElement(Node node)
    {
        CheckStrictModeEvalOrArguments(node, node.Name());
        var name = node.Name();
        if (name is not null && !IsBindingPattern(name))
        {
            if (IsVariableDeclarationInitializedToRequire(node))
            {
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
            }
            else if (IsBlockOrCatchScoped(node))
            {
                BindBlockScopedDeclaration(node, SymbolFlags.BlockScopedVariable, SymbolFlags.BlockScopedVariableExcludes);
            }
            else if (IsPartOfParameterDeclaration(node))
            {
                // It is safe to walk up parent chain to find whether the node is a destructuring parameter declaration
                // because its parent chain has already been set up, since parents are set before descending into children.
                //
                // If node is a binding element in parameter declaration, we need to use ParameterExcludes.
                // Using ParameterExcludes flag allows the compiler to report an error on duplicate identifiers in Parameter Declaration
                // For example:
                //      function foo([a,a]) {} // Duplicate Identifier error
                //      function bar(a,a) {}   // Duplicate Identifier error, parameter declaration in this case is handled in bindParameter
                //                             // which correctly set excluded symbols
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable, SymbolFlags.ParameterExcludes);
            }
            else
            {
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable, SymbolFlags.FunctionScopedVariableExcludes);
            }
        }
    }

    private void BindParameter(Node node)
    {
        var decl = node.AsParameterDeclaration()!;
        if ((node.Flags & NodeFlags.Ambient) == 0)
        {
            // It is a SyntaxError if the identifier eval or arguments appears within a FormalParameterList of a
            // strict mode FunctionLikeDeclaration or FunctionExpression(13.1)
            CheckStrictModeEvalOrArguments(node, decl.Name());
        }
        if (IsBindingPattern(decl.Name()))
        {
            var index = Array.IndexOf(node.Parent!.Parameters()!, node);
            BindAnonymousDeclaration(node, SymbolFlags.FunctionScopedVariable, "__" + index.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable, SymbolFlags.ParameterExcludes);
        }
        // If this is a property-parameter, then also declare the property symbol into the
        // containing class.
        if (IsParameterPropertyDeclaration(node, node.Parent))
        {
            var classDeclaration = node.Parent!.Parent!;
            var flags = SymbolFlags.Property | IfElse(decl.QuestionToken is not null, SymbolFlags.Optional, SymbolFlags.None);
            DeclareSymbol(GetMembers(classDeclaration.Symbol), classDeclaration.Symbol, node, flags, SymbolFlags.PropertyExcludes);
        }
    }

    private void BindFunctionDeclaration(Node node)
    {
        if (!_file.IsDeclarationFile && (node.Flags & NodeFlags.Ambient) == 0 && IsAsyncFunction(node))
        {
            _emitFlags |= NodeFlags.HasAsyncFunctions;
        }
        CheckStrictModeFunctionName(node);
        BindBlockScopedDeclaration(node, SymbolFlags.Function, SymbolFlags.FunctionExcludes);
    }

    private Node? GetInferTypeContainer(Node node)
    {
        var extendsType = FindAncestor(node, n =>
        {
            var parent = n.Parent;
            return parent is not null && IsConditionalTypeNode(parent) && parent.AsConditionalTypeNode()!.ExtendsType == n;
        });
        if (extendsType is not null)
        {
            return extendsType.Parent;
        }
        return null;
    }

    private void BindAnonymousDeclaration(Node node, SymbolFlags symbolFlags, string name)
    {
        var symbol = NewSymbol(symbolFlags, name);
        if ((symbolFlags & (SymbolFlags.EnumMember | SymbolFlags.ClassMember)) != 0)
        {
            symbol.Parent = _container!.Symbol;
        }
        AddDeclarationToSymbol(symbol, node, symbolFlags);
    }

    private void BindBlockScopedDeclaration(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        switch (_blockScopeContainer!.Kind)
        {
            case Kind.ModuleDeclaration:
                DeclareModuleMember(node, symbolFlags, symbolExcludes);
                break;
            case Kind.SourceFile:
                if (IsExternalOrCommonJSModule(_container!.AsSourceFile()))
                {
                    DeclareModuleMember(node, symbolFlags, symbolExcludes);
                    break;
                }
                goto default;
            default:
                DeclareSymbol(GetLocals(_blockScopeContainer), null /*parent*/, node, symbolFlags, symbolExcludes);
                break;
        }
    }

    private void BindTypeParameter(Node node)
    {
        if (node.Parent!.Kind == Kind.InferType)
        {
            var container = GetInferTypeContainer(node.Parent!);
            if (container is not null)
            {
                DeclareSymbol(GetLocals(container), null /*parent*/, node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
            }
            else
            {
                BindAnonymousDeclaration(node, SymbolFlags.TypeParameter, GetDeclarationName(node));
            }
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
        }
    }

    private Symbol? LookupEntity(Node node, Node? container)
    {
        if (IsIdentifier(node))
        {
            return LookupName(node.Text(), container);
        }
        if (node.Expression()!.Kind == Kind.ThisKeyword)
        {
            var (_, symbolTable) = GetThisClassAndSymbolTable();
            if (symbolTable is not null)
            {
                var name = GetElementOrPropertyAccessName(node);
                if (name is not null)
                {
                    return symbolTable.GetValueOrDefault(name.Text());
                }
            }
            return null;
        }
        var symbol = GetInitializerSymbol(LookupEntity(node.Expression()!, container));
        if (symbol is not null && symbol.Exports is not null)
        {
            var name = GetElementOrPropertyAccessName(node);
            if (name is not null)
            {
                return symbol.Exports.GetValueOrDefault(name.Text());
            }
        }
        return null;
    }

    private Symbol? LookupName(string name, Node? container)
    {
        var localsContainer = container!.LocalsContainerData();
        if (localsContainer is not null && localsContainer.Locals?.GetValueOrDefault(name) is { } local)
        {
            return OrElse(local.ExportSymbol, local);
        }
        var declaration = container.DeclarationData();
        if (declaration is not null && declaration.Symbol is not null)
        {
            return declaration.Symbol.Exports?.GetValueOrDefault(name);
        }
        return null;
    }
}

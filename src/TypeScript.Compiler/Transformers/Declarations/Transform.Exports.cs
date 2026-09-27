// ported from: internal/transformers/declarations/transform.go:1161-2083（顶层语句/export 赋值与 CJS export/类型别名接口函数模块声明/类声明）
//
// 与 Go 的偏离（逐条，其余与 Go 逐行对应）：
// 1) 包级函数 `isCommonJSAliasExport`（Go 无接收者）→ 静态类 `Declarations` 的 internal static 成员（契约 §2 §4）。
// 2) Go 的 `defer func(){…}()` → try/finally（transformCommonJSExportWorker 的 watchedClassSymbol 恢复、
//    transformClassExpressionToDeclaration 的 enclosingDeclaration 恢复、transformClassDeclaration 的
//    enclosingDeclaration + errorFallbackNode 恢复；try/finally 的嵌套顺序与 Go defer 的 LIFO 一致）。
// 3) Go 的 `tx.state.getSymbolAccessibilityDiagnostic = func(…){…}`（闭包赋给委托字段）→ 赋 lambda 给
//    `_state._getSymbolAccessibilityDiagnostic`（`SymbolAccessibilityDiagnostic` 在 C# 是 class，Go 的
//    结构体字面量 → 对象初始化器；字段是 `_diagnosticMessage`/`_errorNode`/`_typeName`）。
// 4) Go 在某些函数里对同名局部变量做了**块内遮蔽**（C# CS0136 禁止嵌套块与后续外层块同名），这些局部变量按
//    作用域后缀区分，语义等价：transformExportAssignment/transformCommonJSExportWorker 里的
//    `exportAssignment`→`exportAssignment2`、`newId`→`newId2`、`type_`→`type_2`、`varDecl`→`varDecl2`、
//    `modList`→`modList2`、`statement`→`statement2`；transformCommonJSExportWorker 里内层 `mods`→`classMods`/
//    `noIsolationMods`（外层保持 `mods`）、内层 `classDecl` 保持、外层 `classDecl`→`unnamedClassDecl`、
//    外层 `className`→`unnamedClassName`、外层 `exportDecl`→`unnamedExportDecl`。
// 5) Go 的 `append(s, x)`（单元素）→ `AppendIfUnique(s, x)`、`append(s, xs...)`（切片展开）→ `Concatenate(s, xs)`
//    （Core.cs 无单元素 `Append`；此处 append 的都是**新建**节点，不会命中 AppendIfUnique 的去重分支）。
// 6) Go 的 `printer.AutoGenerateOptions{Flags: printer.GeneratedIdentifierFlagsOptimistic}` →
//    `new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic }`。
// 7) Go 的 `panic(fmt.Sprintf("…: %q", input.Kind))` → `Debug.Fail("…: " + input.Kind.String())`（契约 §2.2；%q 的引号忽略）。
// 8) Go 的 `diagnostics.X` → 全限定 `TypeScript.Diagnostics.Diagnostics.X`：裸写 `Diagnostics.X` 会先解析到
//    **命名空间** `TypeScript.Diagnostics`（TsOptions/DeclsCompiler.cs 的实测结论，CS0234）。
// 9) Go 的 `members, _ = tx.declareStrippingVisitor.VisitSlice(members)` → C# 的元组返回
//    `VisitSlice(...).Result`（Go 的第二个返回值 changed 在本处被丢弃）。
// 10) C# 可空性（Nullable=enable + TreatWarningsAsErrors）：Go 的 `AsXxx()` 返回可空，本文件在 Go 隐含非空处
//     用 `!` 抑制（**不改变运行时行为**，只是抑制诊断）；Go 显式的 nil 检查照抄。
// 11) Go 的 `jsFlags &^= nodebuilder.FlagsWriteClassExpressionAsTypeLiteral` → `jsFlags &= ~Flags.WriteClassExpressionAsTypeLiteral`
//     （`DeclarationEmitNodeBuilderFlags` 是 C# const，`var jsFlags = …` 取得的是副本，与原 Go 的局部副本一致）。
using System;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.Transformers.Declarations.Declarations;

namespace TypeScript.Transformers.Declarations;

public sealed partial class DeclarationTransformer : Transformer
{
    /// <summary>Go: <c>func (tx *DeclarationTransformer) visitDeclarationStatements(input *ast.Node) *ast.Node</c>（transform.go:1161-1190）。</summary>
    internal Node? VisitDeclarationStatements(Node input)
    {
        if (ShouldStripInternal(input))
        {
            return null;
        }
        switch (input.Kind)
        {
            case Kind.ExportDeclaration:
                if (IsSourceFile(input.Parent!))
                {
                    _resultHasExternalModuleIndicator = true;
                }
                _resultHasScopeMarker = true;
                // Rewrite external module names if necessary
                return Factory().UpdateExportDeclaration(
                    input.AsExportDeclaration()!,
                    input.Modifiers(),
                    input.IsTypeOnly(),
                    input.AsExportDeclaration()!.ExportClause,
                    RewriteModuleSpecifier(input, input.ModuleSpecifier()),
                    input.AsExportDeclaration()!.Attributes);
            case Kind.ExportAssignment:
                return TransformExportAssignment(input, input, input.Expression(), input.AsExportAssignment()!.IsExportEquals);
            default:
                var id = GetNodeId(EmitContext().MostOriginal(input)!);
                if (!_lateStatementReplacementMap.TryGetValue(id, out var lateReplacement) || lateReplacement is null)
                {
                    // Don't actually transform yet; just leave as original node - will be elided/swapped by late pass
                    _lateStatementReplacementMap[id] = TransformTopLevelDeclaration(input)!;
                }
                return input;
        }
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) tryGetNameOfAssignedExpression(unwrapped *ast.Node) *ast.Node</c>（transform.go:1192-1210）。</summary>
    internal Node? TryGetNameOfAssignedExpression(Node unwrapped)
    {
        Node? nameNode = null;
        var nameText = "";
        if (!IsPropertyAccessExpression(unwrapped) && unwrapped.Name() is not null)
        {
            nameText = unwrapped.Name()!.Text();
        }
        else if (IsIdentifier(unwrapped))
        {
            nameText = unwrapped.Text();
        }
        if (nameText != "" && nameText != "default")
        {
            if (_resolver.IsNameResolvable(_enclosingDeclaration!, nameText))
            {
                // create a unique name that shares the same text as its' base
                nameNode = Factory().NewUniqueNameEx(nameText, new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
            }
            else
            {
                // use the node's name as-is, since it's not otherwise in-scope
                nameNode = Factory().NewIdentifier(nameText);
            }
        }
        return nameNode;
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) getNameOfExportedAssignedExpression(unwrapped *ast.Node, isExportEquals bool) *ast.Node</c>（transform.go:1212-1225）。</summary>
    internal Node GetNameOfExportedAssignedExpression(Node unwrapped, bool isExportEquals)
    {
        var nameNode = TryGetNameOfAssignedExpression(unwrapped);
        if (nameNode is null)
        {
            // fallback to a default name
            if (isExportEquals && IsSourceFileJS(_state._currentSourceFile))
            {
                // only JS files prefer to use `_exports` for export assignments - TS has always used `_default` for both `export=` and `export default`
                nameNode = Factory().NewUniqueNameEx("_exports", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
            }
            else
            {
                nameNode = Factory().NewUniqueNameEx("_default", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
            }
        }
        _cjsExportAssignmentName = nameNode;
        return nameNode!;
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformExportAssignment(input *ast.Node, assignment *ast.Node, expression *ast.Node, isExportEquals bool) *ast.Node</c>（transform.go:1227-1296）。</summary>
    internal Node? TransformExportAssignment(Node input, Node assignment, Node? expression, bool isExportEquals)
    {
        if (IsSourceFile(input.Parent!))
        {
            _resultHasExternalModuleIndicator = true;
        }
        _resultHasScopeMarker = true;
        if (IsIdentifier(expression!) && (IsSourceFile(input.Parent!) || IsModuleBlock(input.Parent!)))
        {
            var exportAssignment = Factory().NewExportAssignment(null, isExportEquals, null, expression);
            PreserveJsDoc(exportAssignment, input);
            return exportAssignment;
        }

        // Check if the expression is a class expression - emit as a class declaration + export assignment
        var unwrapped = SkipOuterExpressions(expression, OuterExpressionKinds.ExpressionTypePassthrough)!;
        var newId = GetNameOfExportedAssignedExpression(unwrapped, isExportEquals);
        if (IsClassExpression(unwrapped))
        {
            Node[] mods = [];
            if (_needsDeclare)
            {
                mods = AppendIfUnique(mods, Factory().NewModifier(Kind.DeclareKeyword));
            }
            var classDecl = TransformClassExpressionToDeclaration(unwrapped, newId, Factory().NewModifierList(mods));
            PreserveJsDoc(classDecl, input);
            // Reuse the same name node for the export so unique names resolve consistently
            var exportAssignment = Factory().NewExportAssignment(null, isExportEquals, null, newId);
            RemoveAllComments(exportAssignment);
            return Factory().NewSyntaxList(new Node[] { exportAssignment, classDecl });
        }
        else if (IsFunctionLike(unwrapped))
        {
            // Promote function or arrow function expressions to a function declaration
            Node[] mods = [];
            if (_needsDeclare)
            {
                mods = AppendIfUnique(mods, Factory().NewModifier(Kind.DeclareKeyword));
            }
            var fullSignatureType = assignment.Type();
            var funcDecl = TransformFunctionLikeToDeclaration(unwrapped, newId, Factory().NewModifierList(mods), fullSignatureType);
            PreserveJsDoc(funcDecl, input);
            // Reuse the same name node for the export so unique names resolve consistently
            var exportAssignment = Factory().NewExportAssignment(null, isExportEquals, null, newId);
            RemoveAllComments(exportAssignment);
            return Factory().NewSyntaxList(new Node[] { exportAssignment, funcDecl });
        }

        // expression is non-identifier, create _default typed variable to reference
        _state._getSymbolAccessibilityDiagnostic = _ => new SymbolAccessibilityDiagnostic
        {
            _diagnosticMessage = TypeScript.Diagnostics.Diagnostics.Default_export_of_the_module_has_or_is_using_private_name_0,
            _errorNode = input,
        };
        _cjsExportAssignmentName = newId;
        _tracker.PushErrorFallbackNode(assignment);
        Node? type_ = null;
        Node? initializer = null;
        if (IsPrimitiveLiteralValue(UnwrapParenthesizedExpression(expression!), true))
        {
            initializer = _resolver.CreateLiteralConstValue(EmitContext(), EmitContext().ParseNode(assignment)!, _tracker);
        }
        if (initializer is null)
        {
            type_ = EnsureType(assignment, false);
        }
        var varDecl = Factory().NewVariableDeclaration(newId, null, type_, initializer);
        _tracker.PopErrorFallbackNode();
        ModifierList? modList;
        if (_needsDeclare)
        {
            modList = Factory().NewModifierList(new Node[] { Factory().NewModifier(Kind.DeclareKeyword) });
        }
        else
        {
            modList = Factory().NewModifierList(new Node[] { });
        }
        var statement = Factory().NewVariableStatement(modList, Factory().NewVariableDeclarationList(Factory().NewNodeList(new Node[] { varDecl }), NodeFlags.Const));
        var exportAssignment2 = Factory().NewExportAssignment(null, isExportEquals, null, newId);
        // Remove comments from the export declaration and copy them onto the synthetic _default declaration
        PreserveJsDoc(statement, input);
        return Factory().NewSyntaxList(new Node[] { statement, exportAssignment2 });
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformFunctionLikeToDeclaration(unwrapped *ast.Node, funcName *ast.Node, mods *ast.ModifierList, fullSignatureType *ast.Node) *ast.Node</c>（transform.go:1298-1322）。</summary>
    internal Node TransformFunctionLikeToDeclaration(Node unwrapped, Node funcName, ModifierList? mods, Node? fullSignatureType)
    {
        var d = unwrapped.FunctionLikeData()!;
        var sig = d.FullSignature;
        if (sig is null)
        {
            sig = fullSignatureType;
        }
        if (sig is null)
        {
            return Factory().NewFunctionDeclaration(
                mods,
                null,
                funcName,
                EnsureTypeParams(unwrapped, d.TypeParameters),
                UpdateParamList(unwrapped, d.Parameters),
                EnsureType(unwrapped, false),
                Visitor().VisitNode(sig),
                null);
        }
        else
        {
            // If a full signature type node is present, emit as a variable statement to reuse it
            return Factory().NewVariableStatement(
                mods,
                Factory().NewVariableDeclarationList(Factory().NewNodeList(new Node[] { Factory().NewVariableDeclaration(funcName, null, Visitor().VisitNode(sig), null) }), NodeFlags.Const));
        }
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformBinaryExpressionToExportDeclaration(input *ast.Node, name *ast.Node) *ast.Node</c>（transform.go:1324-1341）。</summary>
    internal Node TransformBinaryExpressionToExportDeclaration(Node input, Node name)
    {
        var propertyName = input.AsBinaryExpression()!.Right!;

        // track alias target so referenced declarations are included in the output
        _tracker.HandleSymbolAccessibilityError(_resolver.IsEntityNameVisible(propertyName, _enclosingDeclaration!));

        if (IsIdentifier(name) && propertyName.Text() == name.Text())
        {
            propertyName = null!;
        }

        return Factory().NewExportDeclaration(
            null,
            false,
            Factory().NewNamedExports(Factory().NewNodeList(new Node[] { Factory().NewExportSpecifier(false, propertyName, name) })),
            null,
            null);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformCommonJSExport(input *ast.Node, name *ast.Node) *ast.Node</c>（transform.go:1343-1349）。</summary>
    internal Node? TransformCommonJSExport(Node input, Node name)
    {
        var res = TransformCommonJSExportWorker(input, name);
        if (res is null)
        {
            return res;
        }
        return WrapInCJSExportNamespace(res);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformCommonJSExportWorker(input *ast.Node, name *ast.Node) *ast.Node</c>（transform.go:1351-1534）。</summary>
    internal Node? TransformCommonJSExportWorker(Node input, Node name)
    {
        var nameText = "";
        if (IsIdentifier(name) || IsStringLiteral(name))
        {
            nameText = name.Text();
        }
        if (_witnessedCjsExports.Has(nameText) && nameText != "")
        {
            return null; // Already emitted this export name
        }
        _witnessedCjsExports.Add(nameText);
        _resultHasExternalModuleIndicator = true;
        _resultHasScopeMarker = true;
        // only transform cjs exports to shorthand at the top-level of a source file, otherwise we uniformly emit nested exports with a type annotation
        if (IsCommonJSAliasExport(input) && IsExpressionStatement(input.Parent!) && IsSourceFile(input.Parent!.Parent!))
        {
            // export { name }
            // export { source as name }
            return TransformBinaryExpressionToExportDeclaration(input, name);
        }

        // Check if the RHS is a class expression - emit as a class declaration instead of a typed variable
        if (IsBinaryExpression(input))
        {
            var rhs = UnwrapParenthesizedExpression(input.AsBinaryExpression()!.Right!);
            if (IsClassExpression(rhs))
            {
                var ce = rhs.AsClassExpression()!;
                var classExprName = ce.Name();
                var hasExprName = classExprName is not null && classExprName.Text().Length > 0;

                if (hasExprName)
                {
                    // Set up TrackSymbol watch to detect if the class expression's own
                    // symbol is referenced during member type serialization.
                    _tracker._watchedClassSymbol = rhs.Symbol;
                    _tracker._classSymbolTracked = false;
                    try
                    {
                        // Serialize class members using the class expression name, which
                        // triggers TrackSymbol for any self-referential member types.
                        var className = Factory().NewIdentifier(classExprName!.Text());
                        Node[] classMods = new Node[] { Factory().NewModifier(Kind.ExportKeyword) };
                        var classDecl = TransformClassExpressionToDeclaration(rhs, className, Factory().NewModifierList(classMods));
                        PreserveJsDoc(classDecl, input);

                        // Determine if namespace isolation is needed:
                        // - The class expression name differs from the export name, OR
                        // - The class's own symbol was used in a member's serialized type
                        var namesDiffer = !IsIdentifier(name) || classExprName.Text() != name.Text();
                        var needsIsolation = namesDiffer || _tracker._classSymbolTracked;

                        if (needsIsolation)
                        {
                            var nsName = Factory().NewUniqueNameEx("_ns", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
                            Node[] nsMods = [];
                            if (_needsDeclare)
                            {
                                nsMods = AppendIfUnique(nsMods, Factory().NewModifier(Kind.DeclareKeyword));
                            }
                            var nsDecl = Factory().NewModuleDeclaration(
                                Factory().NewModifierList(nsMods),
                                Kind.NamespaceKeyword,
                                nsName,
                                null,
                                Factory().NewModuleBlock(Factory().NewNodeList(new Node[] { classDecl })));

                            var aliasBase = "_exported";
                            if (IsIdentifier(name) && IsIdentifierText("_" + name.Text(), LanguageVariant.Standard))
                            {
                                aliasBase = "_" + name.Text();
                            }
                            var importAlias = Factory().NewUniqueNameEx(aliasBase, new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
                            var qualifiedName = Factory().NewQualifiedName(nsName, className);
                            var importDecl = Factory().NewImportEqualsDeclaration(null, false, importAlias, qualifiedName);

                            var exportSpecifier = Factory().NewExportSpecifier(false, importAlias, name);
                            var exportDecl = Factory().NewExportDeclaration(null, false, Factory().NewNamedExports(Factory().NewNodeList(new Node[] { exportSpecifier })), null, null);
                            RemoveAllComments(exportDecl);

                            return Factory().NewSyntaxList(new Node[] { nsDecl, importDecl, exportDecl });
                        }

                        // No isolation needed: names match and no self-references.
                        // Update modifiers to include declare if needed.
                        Node[] noIsolationMods = [];
                        noIsolationMods = AppendIfUnique(noIsolationMods, Factory().NewModifier(Kind.ExportKeyword));
                        if (_needsDeclare)
                        {
                            noIsolationMods = AppendIfUnique(noIsolationMods, Factory().NewModifier(Kind.DeclareKeyword));
                        }
                        classDecl = Factory().UpdateClassDeclaration(
                            classDecl.AsClassDeclaration()!,
                            Factory().NewModifierList(noIsolationMods),
                            classDecl.AsClassDeclaration()!.Name(),
                            classDecl.AsClassDeclaration()!.TypeParameters,
                            classDecl.AsClassDeclaration()!.HeritageClauses,
                            classDecl.AsClassDeclaration()!.Members);
                        return classDecl;
                    }
                    finally
                    {
                        _tracker._watchedClassSymbol = null;
                        _tracker._classSymbolTracked = false;
                    }
                }
                Node[] mods = [];
                mods = AppendIfUnique(mods, Factory().NewModifier(Kind.ExportKeyword));
                if (_needsDeclare)
                {
                    mods = AppendIfUnique(mods, Factory().NewModifier(Kind.DeclareKeyword));
                }
                var unnamedClassName = name;
                if (!IsIdentifier(unnamedClassName))
                {
                    unnamedClassName = Factory().NewUniqueNameEx("_class", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
                }
                var unnamedClassDecl = TransformClassExpressionToDeclaration(rhs, unnamedClassName, Factory().NewModifierList(mods));
                PreserveJsDoc(unnamedClassDecl, input);
                if (!IsIdentifier(name))
                {
                    // Non-identifier name: emit class declaration + named export
                    var unnamedExportDecl = Factory().NewExportDeclaration(null, false, Factory().NewNamedExports(Factory().NewNodeList(new Node[] { Factory().NewExportSpecifier(false, unnamedClassName, name) })), null, null);
                    RemoveAllComments(unnamedExportDecl);
                    return Factory().NewSyntaxList(new Node[] { unnamedClassDecl, unnamedExportDecl });
                }
                return unnamedClassDecl;
            }
        }

        if (IsIdentifier(name))
        {
            if (name.Text() == "default")
            {
                // const _default: Type; export default _default;
                var newId = Factory().NewUniqueNameEx("_default", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
                _state._getSymbolAccessibilityDiagnostic = _ => new SymbolAccessibilityDiagnostic
                {
                    _diagnosticMessage = TypeScript.Diagnostics.Diagnostics.Default_export_of_the_module_has_or_is_using_private_name_0,
                    _errorNode = input,
                };
                _tracker.PushErrorFallbackNode(input);
                var type_ = EnsureType(input, false);
                var varDecl = Factory().NewVariableDeclaration(newId, null, type_, null);
                _tracker.PopErrorFallbackNode();
                ModifierList? modList;
                if (_needsDeclare)
                {
                    modList = Factory().NewModifierList(new Node[] { Factory().NewModifier(Kind.DeclareKeyword) });
                }
                else
                {
                    modList = Factory().NewModifierList(new Node[] { });
                }
                var statement = Factory().NewVariableStatement(modList, Factory().NewVariableDeclarationList(Factory().NewNodeList(new Node[] { varDecl }), NodeFlags.Const));

                var assignment = Factory().NewExportAssignment(input.Modifiers(), false, null, newId);
                // Remove comments from the export declaration and copy them onto the synthetic _default declaration
                PreserveJsDoc(statement, input);
                RemoveAllComments(assignment);
                return Factory().NewSyntaxList(new Node[] { statement, assignment });
            }
            else if (_host.GetEmitResolver().GetReferencedValueDeclaration(name) == input || _host.GetEmitResolver().GetReferencedValueDeclaration(name) is null)
            {
                // only inline to a export var if the `name` lookup points at this assignment or nothing - if it points at something else, we must use a temp name
                // export var name: Type
                _tracker.PushErrorFallbackNode(input);
                var type_ = EnsureType(input, false);
                var varDecl = Factory().NewVariableDeclaration(name, null, type_, null);
                _tracker.PopErrorFallbackNode();
                ModifierList? modList;
                if (_needsDeclare)
                {
                    modList = Factory().NewModifierList(new Node[] { Factory().NewModifier(Kind.ExportKeyword), Factory().NewModifier(Kind.DeclareKeyword) });
                }
                else
                {
                    modList = Factory().NewModifierList(new Node[] { Factory().NewModifier(Kind.ExportKeyword) });
                }
                return Factory().NewVariableStatement(modList, Factory().NewVariableDeclarationList(Factory().NewNodeList(new Node[] { varDecl }), NodeFlags.None));
            }
        }
        // const _exported: Type; export {_exported as "name"};
        var newId2 = Factory().NewUniqueNameEx("_exported", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
        _state._getSymbolAccessibilityDiagnostic = _ => new SymbolAccessibilityDiagnostic
        {
            _diagnosticMessage = TypeScript.Diagnostics.Diagnostics.Default_export_of_the_module_has_or_is_using_private_name_0,
            _errorNode = input,
        };
        _tracker.PushErrorFallbackNode(input);
        var type_2 = EnsureType(input, false);
        var varDecl2 = Factory().NewVariableDeclaration(newId2, null, type_2, null);
        _tracker.PopErrorFallbackNode();
        ModifierList? modList2;
        if (_needsDeclare)
        {
            modList2 = Factory().NewModifierList(new Node[] { Factory().NewModifier(Kind.DeclareKeyword) });
        }
        else
        {
            modList2 = Factory().NewModifierList(new Node[] { });
        }
        var statement2 = Factory().NewVariableStatement(modList2, Factory().NewVariableDeclarationList(Factory().NewNodeList(new Node[] { varDecl2 }), NodeFlags.Const));

        var assignment2 = Factory().NewExportDeclaration(null, false, Factory().NewNamedExports(Factory().NewNodeList(new Node[] { Factory().NewExportSpecifier(false, newId2, name) })), null, null);
        // Remove comments from the export declaration and copy them onto the synthetic _default declaration
        PreserveJsDoc(statement2, input);
        RemoveAllComments(assignment2);
        return Factory().NewSyntaxList(new Node[] { statement2, assignment2 });
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) wrapInCJSExportNamespace(content *ast.Node) *ast.Node</c>（transform.go:1536-1560）。</summary>
    internal Node WrapInCJSExportNamespace(Node content)
    {
        if (_cjsExportAssignmentName is null)
        {
            return content;
        }
        // Reuse the same name node so unique names resolve consistently with the class/export
        var nsName = _cjsExportAssignmentName;
        Node[] members;
        if (content.Kind == Kind.SyntaxList)
        {
            members = content.AsSyntaxList()!.Children!;
        }
        else
        {
            members = new Node[] { content };
        }
        Node[] nsMods = [];
        if (_needsDeclare)
        {
            nsMods = AppendIfUnique(nsMods, Factory().NewModifier(Kind.DeclareKeyword));
        }
        members = _declareStrippingVisitor.VisitSlice(members).Result!;
        return Factory().NewModuleDeclaration(
            Factory().NewModifierList(nsMods),
            Kind.NamespaceKeyword,
            nsName,
            null,
            Factory().NewModuleBlock(Factory().NewNodeList(members)));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformClassExpressionToDeclaration(classExpr *ast.Node, className *ast.Node, modifiers *ast.ModifierList) *ast.Node</c>（transform.go:1574-1599）。</summary>
    internal Node TransformClassExpressionToDeclaration(Node classExpr, Node className, ModifierList? modifiers)
    {
        var previousEnclosingDeclaration = _enclosingDeclaration;
        _enclosingDeclaration = classExpr;
        var previousInClassExpressionDeclaration = _inClassExpressionDeclaration;
        _inClassExpressionDeclaration = true;
        try
        {
            Node[] extraMembers = [];
            if (IsInJSFile(classExpr))
            {
                extraMembers = CollectThisPropertyAssignments(classExpr);
            }
            var members = BuildClassMembers(classExpr, extraMembers);
            var typeParameters = EnsureTypeParams(classExpr, classExpr.AsClassExpression()!.TypeParameters);
            var heritageClauses = Visitor().VisitNodes(classExpr.AsClassExpression()!.HeritageClauses);

            return Factory().NewClassDeclaration(
                modifiers,
                className,
                typeParameters,
                heritageClauses,
                members);
        }
        finally
        {
            _enclosingDeclaration = previousEnclosingDeclaration;
            _inClassExpressionDeclaration = previousInClassExpressionDeclaration;
        }
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) rewriteModuleSpecifier(parent *ast.Node, input *ast.Node) *ast.Node</c>（transform.go:1601-1607）。</summary>
    internal Node? RewriteModuleSpecifier(Node parent, Node? input)
    {
        if (input is null)
        {
            return null;
        }
        _resultHasExternalModuleIndicator = _resultHasExternalModuleIndicator || (parent.Kind != Kind.ModuleDeclaration && parent.Kind != Kind.ImportType);
        return input;
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) preserveJsDoc(updated *ast.Node, original *ast.Node)</c>（transform.go:1609-1612）。</summary>
    internal void PreserveJsDoc(Node updated, Node original)
    {
        // Copy comment range from original to updated node so JSDoc comments are preserved
        EmitContext().AssignCommentRange(updated, original);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) preservePartialJsDoc(updated *ast.Node, original *ast.Node)</c>（transform.go:1614-1628）。</summary>
    internal void PreservePartialJsDoc(Node updated, Node original)
    {
        if ((original.Flags & NodeFlags.Reparsed) == 0)
        {
            return;
        }
        var jsdoc = FirstOrNil(original.EagerJSDoc(GetSourceFileOfNode(original)));
        if (jsdoc is null)
        {
            return;
        }
        var description = GetTextOfJSDocComment(jsdoc.AsJSDoc()!.Comment);
        if (description == "")
        {
            return;
        }
        var comment = "*\n * " + description.Replace("\n", "\n * ", StringComparison.Ordinal) + "\n ";
        EmitContext().AddSyntheticLeadingComment(updated, Kind.MultiLineCommentTrivia, comment, true /*hasTrailingNewLine*/);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) removeAllComments(node *ast.Node)</c>（transform.go:1630-1635）。</summary>
    internal void RemoveAllComments(Node node)
    {
        EmitContext().AddEmitFlags(node, EmitFlags.NoComments);
        // !!! TODO: Also remove synthetic trailing/leading comments added by transforms
        // emitNode.leadingComments = undefined;
        // emitNode.trailingComments = undefined;
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) ensureType(node *ast.Node, ignorePrivate bool) *ast.Node</c>（transform.go:1637-1699）。</summary>
    internal Node? EnsureType(Node node, bool ignorePrivate)
    {
        if (!ignorePrivate && _host.GetEffectiveDeclarationFlags(EmitContext().ParseNode(node)!, ModifierFlags.Private) != 0)
        {
            // Private nodes emit no types (except private parameter properties, whose parameter types are actually visible)
            return null;
        }

        if (ShouldPrintWithInitializer(node))
        {
            // Literal const declarations will have an initializer ensured rather than a type
            return null;
        }

        // Should be removed createTypeOfDeclaration will actually now reuse the existing annotation so there is no real need to duplicate type walking
        // Left in for now to minimize diff during syntactic type node builder refactor
        if (!IsExportAssignment(node) && !IsBindingElement(node) && node.Type() is not null && (!IsParameterDeclaration(node) || !_resolver.RequiresAddingImplicitUndefined(node, null!, _enclosingDeclaration!)))
        {
            if (_state._currentSourceFile!.IsJS())
            {
                // JS types have a heap of constructs we can't directly emit into .d.ts files; the node builder contains logic to remap those where possible, so we invoke it here
                // In strada we always built js declarations symbolically, so all js type nodes went through this postprocessing
                var jsFlags = DeclarationEmitNodeBuilderFlags;
                if (_inClassExpressionDeclaration)
                {
                    jsFlags &= ~Flags.WriteClassExpressionAsTypeLiteral;
                }
                var res = _resolver.TryJSTypeNodeToTypeNode(EmitContext(), node.Type()!, _enclosingDeclaration!, jsFlags, DeclarationEmitInternalNodeBuilderFlags, _tracker);
                if (res is not null)
                {
                    return res;
                }
                // otherwise, fall back to full serialization
            }
            else
            {
                return Visitor().Visit!(node.Type()!);
            }
        }

        var oldErrorNameNode = _state._errorNameNode;
        _state._errorNameNode = node.Name();
        var oldDiag = _state._getSymbolAccessibilityDiagnostic;
        if (!_suppressNewDiagnosticContexts)
        {
            if (CanProduceDiagnostics(node))
            {
                _state._getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(node);
            }
        }
        Node? typeNode = null;

        var flags = DeclarationEmitNodeBuilderFlags;
        if (_inClassExpressionDeclaration)
        {
            flags &= ~Flags.WriteClassExpressionAsTypeLiteral;
        }
        if (HasInferredType(node))
        {
            typeNode = _resolver.CreateTypeOfDeclaration(EmitContext(), node, _enclosingDeclaration!, flags, DeclarationEmitInternalNodeBuilderFlags, _tracker);
        }
        else if (IsFunctionLike(node))
        {
            typeNode = _resolver.CreateReturnTypeOfSignatureDeclaration(EmitContext(), node, _enclosingDeclaration!, flags, DeclarationEmitInternalNodeBuilderFlags, _tracker);
        }
        else
        {
            Debug.AssertNever(node);
        }

        _state._errorNameNode = oldErrorNameNode;
        if (!_suppressNewDiagnosticContexts)
        {
            _state._getSymbolAccessibilityDiagnostic = oldDiag;
        }
        if (typeNode is null)
        {
            return Factory().NewKeywordTypeNode(Kind.AnyKeyword);
        }
        return typeNode;
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) shouldPrintWithInitializer(node *ast.Node) bool</c>（transform.go:1701-1703）。</summary>
    internal bool ShouldPrintWithInitializer(Node node)
    {
        return CanHaveLiteralInitializer(_host, node) && node.Initializer() is not null && _resolver.IsLiteralConstDeclaration(EmitContext().MostOriginal(node)!);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) checkEntityNameVisibility(entityName *ast.Node, enclosingDeclaration *ast.Node)</c>（transform.go:1705-1709）。</summary>
    internal void CheckEntityNameVisibility(Node entityName, Node enclosingDeclaration)
    {
        var visibilityResult = _resolver.IsEntityNameVisible(entityName, enclosingDeclaration);
        _tracker.HandleSymbolAccessibilityError(visibilityResult);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformTopLevelDeclaration(input *ast.Node) *ast.Node</c>（transform.go:1711-1789）。</summary>
    internal Node? TransformTopLevelDeclaration(Node input)
    {
        if (_state._lateMarkedStatements.Length > 0)
        {
            // Remove duplicates of the current statement from the deferred work queue (this was done via orderedRemoveItem in strada - why? to ensure the same backing array? microop?)
            _state._lateMarkedStatements = Filter(_state._lateMarkedStatements, node => node != input);
        }
        if (ShouldStripInternal(input))
        {
            return null;
        }
        if (input.Kind == Kind.ImportEqualsDeclaration)
        {
            return TransformImportEqualsDeclaration(input.AsImportEqualsDeclaration()!);
        }
        if (input.Kind == Kind.ImportDeclaration || input.Kind == Kind.JSImportDeclaration)
        {
            var res = TransformImportDeclaration(input.AsImportDeclaration()!);
            if (res is not null && res.Kind != Kind.ImportDeclaration)
            {
                var resClone = res.Clone(Factory());
                resClone!.Kind = Kind.ImportDeclaration;
                return resClone;
            }
            return res;
        }
        if (IsDeclaration(input) && IsDeclarationAndNotVisible(EmitContext(), _resolver, input))
        {
            return null;
        }

        // !!! TODO: JSDoc support
        // if (isJSDocImportTag(input)) return;

        // Elide implementation signatures from overload sets
        if (IsFunctionLike(input) && _resolver.IsImplementationOfOverload(input))
        {
            return null;
        }
        var original = EmitContext().MostOriginal(input)!;
        var id = GetNodeId(original);
        var isExpandoHost = _expandoHosts.ContainsKey(id);
        var hasDeferredExpandoAssignments = _deferredExpandoAssignments.ContainsKey(id);
        if (isExpandoHost || hasDeferredExpandoAssignments)
        {
            return CreateFullExpandoBlock(id);
        }

        var previousEnclosingDeclaration = _enclosingDeclaration;
        if (IsEnclosingDeclaration(input))
        {
            _enclosingDeclaration = input;
        }

        var canProduceDiagnostic = CanProduceDiagnostics(input);
        var oldDiag = _state._getSymbolAccessibilityDiagnostic;
        var oldName = _state._errorNameNode;
        if (canProduceDiagnostic)
        {
            _state._getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(input);
        }
        var saveNeedsDeclare = _needsDeclare;

        Node? result;
        switch (input.Kind)
        {
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
                result = TransformTypeAliasDeclaration(input.AsTypeAliasDeclaration()!);
                break;
            case Kind.InterfaceDeclaration:
                result = TransformInterfaceDeclaration(input.AsInterfaceDeclaration()!);
                break;
            case Kind.FunctionDeclaration:
                result = TransformFunctionDeclaration(input.AsFunctionDeclaration()!);
                break;
            case Kind.ModuleDeclaration:
                result = TransformModuleDeclaration(input.AsModuleDeclaration()!);
                break;
            case Kind.ClassDeclaration:
                result = TransformClassDeclaration(input.AsClassDeclaration()!);
                break;
            case Kind.VariableStatement:
                result = TransformVariableStatement(input.AsVariableStatement()!);
                break;
            case Kind.EnumDeclaration:
                result = TransformEnumDeclaration(input.AsEnumDeclaration()!);
                break;
            default:
                // Anything left unhandled is an error, so this should be unreachable
                Debug.Fail("Unhandled top-level node in declaration emit: " + input.Kind.String());
                result = null;
                break;
        }

        _enclosingDeclaration = previousEnclosingDeclaration;
        _state._getSymbolAccessibilityDiagnostic = oldDiag;
        _needsDeclare = saveNeedsDeclare;
        _state._errorNameNode = oldName;
        return result;
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformTypeAliasDeclaration(input *ast.TypeAliasDeclaration) *ast.Node</c>（transform.go:1791-1800）。</summary>
    internal Node TransformTypeAliasDeclaration(TypeAliasDeclaration input)
    {
        _needsDeclare = false;
        return Factory().UpdateTypeAliasDeclaration(
            input,
            EnsureModifiers(input),
            input.Name(),
            Visitor().VisitNodes(input.TypeParameters),
            Visitor().Visit!(input.Type!));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformInterfaceDeclaration(input *ast.InterfaceDeclaration) *ast.Node</c>（transform.go:1802-1811）。</summary>
    internal Node TransformInterfaceDeclaration(InterfaceDeclaration input)
    {
        return Factory().UpdateInterfaceDeclaration(
            input,
            EnsureModifiers(input),
            input.Name(),
            Visitor().VisitNodes(input.TypeParameters),
            Visitor().VisitNodes(input.HeritageClauses),
            Visitor().VisitNodes(input.Members));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformFunctionDeclaration(input *ast.FunctionDeclaration) *ast.Node</c>（transform.go:1813-1828）。</summary>
    internal Node TransformFunctionDeclaration(FunctionDeclaration input)
    {
        if (_resolver.IsExpandoFunctionDeclaration(input))
        {
            _state._reportExpandoFunctionErrors!(input);
        }
        return Factory().UpdateFunctionDeclaration(
            input,
            EnsureModifiers(input),
            null,
            input.Name(),
            EnsureTypeParams(input, input.TypeParameters),
            UpdateParamList(input, input.Parameters),
            EnsureType(input, false),
            null, /*fullSignature*/
            null);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformModuleDeclaration(input *ast.ModuleDeclaration) *ast.Node</c>（transform.go:1830-1905）。</summary>
    internal Node TransformModuleDeclaration(ModuleDeclaration input)
    {
        // !!! TODO: module declarations are now parsed into nested module objects with export modifiers
        // It'd be good to collapse those back in the declaration output, but the AST can't represent the
        // `namespace a.b.c` shape for the printer (without using invalid identifier names).
        var mods = EnsureModifiers(input);
        var saveNeedsDeclare = _needsDeclare;
        _needsDeclare = false;
        var inner = input.Body;
        var keyword = input.Keyword;
        if (keyword != Kind.GlobalKeyword && (input.Name() is null || !IsStringLiteral(input.Name()!)))
        {
            keyword = Kind.NamespaceKeyword;
        }
        var attributes = Visitor().Visit!(input.Attributes!);

        if (inner is not null && inner.Kind == Kind.ModuleBlock)
        {
            var oldNeedsScopeFix = _needsScopeFixMarker;
            var oldHasScopeFix = _resultHasScopeMarker;
            _resultHasScopeMarker = false;
            _needsScopeFixMarker = false;
            var statements = Visitor().VisitNodes(inner.StatementList());
            var lateStatements = TransformAndReplaceLatePaintedStatements(statements!);
            if ((input.Flags & NodeFlags.Ambient) != 0)
            {
                _needsScopeFixMarker = false; // If it was `declare`'d everything is implicitly exported already, ignore late printed "privates"
            }
            // With the final list of statements, there are 3 possibilities:
            // 1. There's an export assignment or export declaration in the namespace - do nothing
            // 2. Everything is exported and there are no export assignments or export declarations - strip all export modifiers
            // 3. Some things are exported, some are not, and there's no marker - add an empty marker
            if (!IsGlobalScopeAugmentation(input) && !_resultHasScopeMarker && !HasScopeMarker(lateStatements))
            {
                if (_needsScopeFixMarker)
                {
                    lateStatements = Factory().NewNodeList(Concatenate(lateStatements!.Nodes, new Node[] { CreateEmptyExports(Factory().AsNodeFactory()) }));
                }
                else
                {
                    lateStatements = _exportStrippingVisitor.VisitNodes(lateStatements);
                }
            }

            var body = Factory().UpdateModuleBlock(inner.AsModuleBlock()!, lateStatements);
            _needsDeclare = saveNeedsDeclare;
            _needsScopeFixMarker = oldNeedsScopeFix;
            _resultHasScopeMarker = oldHasScopeFix;

            return Factory().UpdateModuleDeclaration(
                input,
                mods,
                keyword,
                input.Name(),
                attributes,
                body);
        }
        if (inner is not null)
        {
            // trigger visit. ignore result (is deferred, so is just inner unless elided)
            Visitor().Visit!(inner);
            // eagerly transform nested namespaces (the nesting doesn't need any elision or painting done)
            var original = EmitContext().MostOriginal(inner)!;
            var id = GetNodeId(original);
            _lateStatementReplacementMap.TryGetValue(id, out var body);
            _lateStatementReplacementMap.Remove(id);
            return Factory().UpdateModuleDeclaration(
                input,
                mods,
                keyword,
                input.Name(),
                attributes,
                body);
        }
        return Factory().UpdateModuleDeclaration(
            input,
            mods,
            keyword,
            input.Name(),
            attributes,
            null);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) stripExportModifiers(statement *ast.Node) *ast.Node</c>（transform.go:1907-1928）。</summary>
    internal Node? StripExportModifiers(Node? statement)
    {
        if (statement is null)
        {
            return null;
        }
        var parseNode = EmitContext().ParseNode(statement);
        if (IsImportEqualsDeclaration(statement) || (parseNode is not null && _host.GetEffectiveDeclarationFlags(parseNode, ModifierFlags.Default) != 0) || !CanHaveModifiers(statement))
        {
            // `export import` statements should remain as-is, as imports are _not_ implicitly exported in an ambient namespace
            // Likewise, `export default` classes and the like and just be `default`, so we preserve their `export` modifiers, too
            return statement;
        }

        var oldFlags = GetCombinedModifierFlags(statement);
        if ((oldFlags & ModifierFlags.Export) == 0)
        {
            return statement;
        }
        var newFlags = oldFlags & (ModifierFlags.All ^ ModifierFlags.Export);
        var modifiers = CreateModifiersFromModifierFlags(newFlags, Factory().NewModifier);
        return ReplaceModifiers(Factory().AsNodeFactory(), statement, Factory().NewModifierList(modifiers));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) buildClassMembers(classNode *ast.Node, extraMembers ...*ast.Node) *ast.NodeList</c>（transform.go:1930-1988）。</summary>
    internal NodeList BuildClassMembers(Node classNode, params Node[] extraMembers)
    {
        var ctor = GetFirstConstructorWithBody(classNode);
        Node[] parameterProperties = [];
        if (ctor is not null)
        {
            var oldDiag = _state._getSymbolAccessibilityDiagnostic;
            foreach (var param in ctor.AsConstructorDeclaration()!.Parameters!.Nodes ?? [])
            {
                if (!HasSyntacticModifier(param, ModifierFlags.ParameterPropertyModifier) || ShouldStripInternal(param))
                {
                    continue;
                }
                _state._getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(param);
                if (param.Name()!.Kind == Kind.Identifier)
                {
                    var updated = Factory().NewPropertyDeclaration(
                        EnsureModifiers(param),
                        param.Name(),
                        param.QuestionToken(),
                        EnsureType(param, false),
                        EnsureNoInitializer(param));
                    PreserveJsDoc(updated, param);
                    parameterProperties = AppendIfUnique(parameterProperties, updated);
                }
                else
                {
                    // Pattern - this is currently an error, but we emit declarations for it somewhat correctly
                    parameterProperties = Concatenate(parameterProperties, WalkBindingPattern(param.Name()!.AsBindingPattern()!, param));
                }
            }
            _state._getSymbolAccessibilityDiagnostic = oldDiag;
        }

        // When the class has at least one private identifier, create a unique constant identifier to retain the nominal typing behavior
        // Prevents other classes with the same public members from being used in place of the current class
        Node? privateIdentifier = null;
        if (Some(classNode.ClassLikeData()!.Members!.Nodes, member => member.Name() is not null && IsPrivateIdentifier(member.Name()!)))
        {
            privateIdentifier = Factory().NewPropertyDeclaration(null, Factory().NewPrivateIdentifier("#private"), null, null, null);
        }

        var lateIndexes = _resolver.CreateLateBoundIndexSignatures(
            EmitContext(),
            classNode,
            _enclosingDeclaration!,
            DeclarationEmitNodeBuilderFlags,
            DeclarationEmitInternalNodeBuilderFlags,
            _tracker);

        Node[] memberNodes = privateIdentifier is not null ? new Node[] { privateIdentifier } : [];
        memberNodes = Concatenate(memberNodes, lateIndexes);
        memberNodes = Concatenate(memberNodes, parameterProperties);
        memberNodes = Concatenate(memberNodes, extraMembers);
        var visitResult = Visitor().VisitNodes(classNode.ClassLikeData()!.Members);
        if (visitResult is not null && (visitResult.Nodes?.Length ?? 0) > 0)
        {
            memberNodes = Concatenate(memberNodes, visitResult.Nodes);
        }
        return Factory().NewNodeList(memberNodes);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformClassDeclaration(input *ast.ClassDeclaration) *ast.Node</c>（transform.go:1990-2083）。</summary>
    internal Node TransformClassDeclaration(ClassDeclaration input)
    {
        var previousEnclosingDeclaration = _enclosingDeclaration;
        _enclosingDeclaration = input;
        try
        {
            _state._errorNameNode = input.Name();
            _tracker.PushErrorFallbackNode(input);
            try
            {
                var modifiers = EnsureModifiers(input);
                var typeParameters = EnsureTypeParams(input, input.TypeParameters);

                // Collect this.x property assignments from constructors and static blocks in JS files
                Node[] extraMembers = [];
                if (IsInJSFile(input))
                {
                    extraMembers = CollectThisPropertyAssignments(input);
                }

                var members = BuildClassMembers(input, extraMembers);

                var extendsClause = GetEffectiveBaseTypeNode(input);

                if (extendsClause is not null && !IsEntityNameExpression(extendsClause.AsExpressionWithTypeArguments()!.Expression) && extendsClause.AsExpressionWithTypeArguments()!.Expression!.Kind != Kind.NullKeyword)
                {
                    _tracker.ReportInferenceFallback(extendsClause.AsExpressionWithTypeArguments()!.Expression!); // Add an isolated declarations error on this extends clause
                    var oldId = "default";
                    if (NodeIsPresent(input.Name()) && IsIdentifier(input.Name()!) && input.Name()!.Text().Length > 0)
                    {
                        oldId = input.Name()!.Text();
                    }
                    var newId = Factory().NewUniqueNameEx(oldId + "_base", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
                    _state._getSymbolAccessibilityDiagnostic = _ => new SymbolAccessibilityDiagnostic
                    {
                        _diagnosticMessage = TypeScript.Diagnostics.Diagnostics.X_extends_clause_of_exported_class_0_has_or_is_using_private_name_1,
                        _errorNode = extendsClause,
                        _typeName = input.Name(),
                    };

                    var varDecl = Factory().NewVariableDeclaration(
                        newId,
                        null,
                        _resolver.CreateTypeOfExpression(EmitContext(), extendsClause.Expression()!, input, DeclarationEmitNodeBuilderFlags, DeclarationEmitInternalNodeBuilderFlags, _tracker),
                        null);
                    ModifierList? mods = null;
                    if (_needsDeclare)
                    {
                        mods = Factory().NewModifierList(new Node[] { Factory().NewModifier(Kind.DeclareKeyword) });
                    }
                    var statement = Factory().NewVariableStatement(
                        mods,
                        Factory().NewVariableDeclarationList(Factory().NewNodeList(new Node[] { varDecl }), NodeFlags.Const));
                    var newHeritageClause = Factory().UpdateHeritageClause(
                        extendsClause.Parent!.AsHeritageClause()!,
                        extendsClause.Parent!.AsHeritageClause()!.Token,
                        Factory().NewNodeList(new Node[]
                        {
                            Factory().UpdateExpressionWithTypeArguments(
                                extendsClause.AsExpressionWithTypeArguments()!,
                                newId,
                                Visitor().VisitNodes(extendsClause.AsExpressionWithTypeArguments()!.TypeArguments)),
                        }));
                    var retainedHeritageClauses = Visitor().VisitNodes(input.HeritageClauses); // should just be `implements`
                    Node[] heritageList = new Node[] { newHeritageClause };
                    if (retainedHeritageClauses is not null && (retainedHeritageClauses.Nodes?.Length ?? 0) > 0)
                    {
                        heritageList = Concatenate(heritageList, retainedHeritageClauses.Nodes);
                    }
                    var heritageClauses = Factory().NewNodeList(heritageList);

                    return Factory().NewSyntaxList(new Node[]
                    {
                        statement,
                        Factory().UpdateClassDeclaration(
                            input,
                            modifiers,
                            input.Name(),
                            typeParameters,
                            heritageClauses,
                            members),
                    });
                }

                return Factory().UpdateClassDeclaration(
                    input,
                    modifiers,
                    input.Name(),
                    typeParameters,
                    Visitor().VisitNodes(input.HeritageClauses),
                    members);
            }
            finally
            {
                _tracker.PopErrorFallbackNode();
            }
        }
        finally
        {
            _enclosingDeclaration = previousEnclosingDeclaration;
        }
    }
}

/// <summary>Go 的 package declarations 的包级函数宿主（本分片部分，见 transform.go:1562-1572）。</summary>
public static partial class Declarations
{
    /// <summary>Go: <c>func isCommonJSAliasExport(node *ast.Node) bool</c>（transform.go:1562-1572）。</summary>
    internal static bool IsCommonJSAliasExport(Node node)
    {
        if (IsBinaryExpression(node) && IsIdentifier(node.AsBinaryExpression()!.Right!))
        {
            var symbol = node.Symbol;
            if (symbol is not null && symbol.Declarations?.Length == 1)
            {
                return true;
            }
        }
        return false;
    }
}

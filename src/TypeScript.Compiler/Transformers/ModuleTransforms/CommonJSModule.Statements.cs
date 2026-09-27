// ported from: internal/transformers/moduletransforms/commonjsmodule.go:711-1363
// （顶层语句访问器：import/import=/export/export=/函数/类/变量语句，以及顶层嵌套语句访问器）
//
// 与 commonjsmodule.go:1-710 分片共享同一个 `partial class CommonJSModuleTransformer`；分片顺序不影响语义。
// 形状说明见 CommonJSModule.cs 文件头（List<Node> 承接 Go 的 slice+append、属性化访问器、TextRange? 等）。

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.ModuleTransforms;

public sealed partial class CommonJSModuleTransformer
{
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelImportDeclaration(node *ast.ImportDeclaration) *ast.Node</c>。</summary>
    private Node? VisitTopLevelImportDeclaration(ImportDeclaration node)
    {
        if (node.ImportClause is null)
        {
            // import "mod";
            var statement = Factory().NewExpressionStatement(CreateRequireCall(node.AsNode()));
            EmitContext().SetOriginal(statement, node.AsNode());
            EmitContext().AssignCommentAndSourceMapRanges(statement, node.AsNode());
            return statement;
        }

        var statements = new List<Node>();
        var variables = new List<Node>();
        var namespaceDeclaration = GetNamespaceDeclarationNode(node.AsNode());
        if (namespaceDeclaration is not null && !IsDefaultImport(node.AsNode()))
        {
            // import * as n from "mod";
            variables.Add(
                Factory().NewVariableDeclaration(
                    namespaceDeclaration.Name()!.Clone(Factory()),
                    null, /*exclamationToken*/
                    null, /*type*/
                    GetHelperExpressionForImport(node, CreateRequireCall(node.AsNode()))));
        }
        else
        {
            // import d from "mod";
            // import { x, y } from "mod";
            // import d, { x, y } from "mod";
            // import d, * as n from "mod";
            variables.Add(
                Factory().NewVariableDeclaration(
                    Factory().NewGeneratedNameForNode(node.AsNode()),
                    null, /*exclamationToken*/
                    null, /*type*/
                    GetHelperExpressionForImport(node, CreateRequireCall(node.AsNode()))));

            if (namespaceDeclaration is not null && IsDefaultImport(node.AsNode()))
            {
                variables.Add(
                    Factory().NewVariableDeclaration(
                        namespaceDeclaration.Name()!.Clone(Factory()),
                        null, /*exclamationToken*/
                        null, /*type*/
                        Factory().NewGeneratedNameForNode(node.AsNode())));
            }
        }

        var varStatement = Factory().NewVariableStatement(
            null, /*modifiers*/
            Factory().NewVariableDeclarationList(
                Factory().NewNodeList(variables.ToArray()),
                NodeFlags.Const));

        EmitContext().SetOriginal(varStatement, node.AsNode());
        EmitContext().AssignCommentAndSourceMapRanges(varStatement, node.AsNode());
        statements.Add(varStatement);
        statements = AppendExportsOfImportDeclaration(statements, node);
        return SingleOrMany(statements.ToArray(), Factory());
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelImportEqualsDeclaration(node *ast.ImportEqualsDeclaration) *ast.Node</c>。</summary>
    private Node? VisitTopLevelImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        if (!IsExternalModuleImportEqualsDeclaration(node.AsNode()))
        {
            // import m = n;
            Debug.Fail("import= for internal module references should be handled in an earlier transformer.");
        }

        var statements = new List<Node>();
        if (HasSyntacticModifier(node.AsNode(), ModifierFlags.Export))
        {
            // export import m = require("mod");
            var statement = Factory().NewExpressionStatement(
                CreateExportExpression(
                    node.Name()!,
                    CreateRequireCall(node.AsNode()),
                    node.Loc,
                    false /*liveBinding*/));

            EmitContext().SetOriginal(statement, node.AsNode());
            EmitContext().AssignCommentAndSourceMapRanges(statement, node.AsNode());
            statements.Add(statement);
        }
        else
        {
            // import m = require("mod");
            var statement = Factory().NewVariableStatement(
                null, /*modifiers*/
                Factory().NewVariableDeclarationList(
                    Factory().NewNodeList([
                        Factory().NewVariableDeclaration(
                            node.Name()!.Clone(Factory()),
                            null, /*exclamationToken*/
                            null, /*typeNode*/
                            CreateRequireCall(node.AsNode())),
                    ]),
                    NodeFlags.Const));
            EmitContext().SetOriginal(statement, node.AsNode());
            EmitContext().AssignCommentAndSourceMapRanges(statement, node.AsNode());
            statements.Add(statement);
        }

        statements = AppendExportsOfDeclaration(statements, node.AsNode(), null /*seen*/, false /*liveBinding*/);
        return SingleOrMany(statements.ToArray(), Factory());
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelExportDeclaration(node *ast.ExportDeclaration) *ast.Node</c>。</summary>
    private Node? VisitTopLevelExportDeclaration(ExportDeclaration node)
    {
        if (node.ModuleSpecifier is null)
        {
            // Elide export declarations with no module specifier as they are handled
            // elsewhere.
            return null;
        }

        var generatedName = Factory().NewGeneratedNameForNode(node.AsNode());
        if (node.ExportClause is not null && IsNamedExports(node.ExportClause))
        {
            // export { x, y } from "mod";
            var statements = new List<Node>();
            var varStatement = Factory().NewVariableStatement(
                null, /*modifiers*/
                Factory().NewVariableDeclarationList(
                    Factory().NewNodeList([
                        Factory().NewVariableDeclaration(
                            generatedName,
                            null, /*exclamationToken*/
                            null, /*type*/
                            CreateRequireCall(node.AsNode())),
                    ]),
                    NodeFlags.None));
            EmitContext().SetOriginal(varStatement, node.AsNode());
            EmitContext().AssignCommentAndSourceMapRanges(varStatement, node.AsNode());
            statements.Add(varStatement);

            foreach (var specifier in node.ExportClause.Elements() ?? [])
            {
                var specifierName = specifier.PropertyNameOrName()!;
                var exportNeedsImportDefault = ModuleExportNameIsDefault(specifierName);

                Node target;
                if (exportNeedsImportDefault)
                {
                    target = Factory().NewImportDefaultHelper(generatedName);
                }
                else
                {
                    target = generatedName;
                }

                Node exportName;
                if (IsStringLiteral(specifier.Name()!))
                {
                    exportName = Factory().NewStringLiteralFromNode(specifier.Name()!);
                }
                else
                {
                    exportName = Factory().GetExportName(specifier);
                }

                Node exportedValue;
                if (IsStringLiteral(specifierName))
                {
                    exportedValue = Factory().NewElementAccessExpression(target, null /*questionDotToken*/, specifierName, NodeFlags.None);
                }
                else
                {
                    exportedValue = Factory().NewPropertyAccessExpression(target, null /*questionDotToken*/, specifierName, NodeFlags.None);
                }
                var statement = Factory().NewExpressionStatement(
                    CreateExportExpression(
                        exportName,
                        exportedValue,
                        null, /*location*/
                        true  /*liveBinding*/));
                EmitContext().SetOriginal(statement, specifier);
                EmitContext().AssignCommentAndSourceMapRanges(statement, specifier);
                statements.Add(statement);
            }

            return SingleOrMany(statements.ToArray(), Factory());
        }

        if (node.ExportClause is not null)
        {
            // export * as ns from "mod";
            // export * as default from "mod";
            Node exportName;
            if (IsStringLiteral(node.ExportClause.Name()!))
            {
                exportName = Factory().NewStringLiteralFromNode(node.ExportClause.Name()!);
            }
            else
            {
                exportName = node.ExportClause.Name()!.Clone(Factory())!;
            }
            var statement = Factory().NewExpressionStatement(
                CreateExportExpression(
                    exportName,
                    GetHelperExpressionForExport(
                        node,
                        CreateRequireCall(node.AsNode())),
                    null,  /*location*/
                    false  /*liveBinding*/));
            EmitContext().SetOriginal(statement, node.AsNode());
            EmitContext().AssignCommentAndSourceMapRanges(statement, node.AsNode());
            return statement;
        }

        // export * from "mod";
        {
            var statement = Factory().NewExpressionStatement(
                Visitor().VisitNode(Factory().NewExportStarHelper(CreateRequireCall(node.AsNode()), Factory().NewIdentifier("exports")))!);
            EmitContext().SetOriginal(statement, node.AsNode());
            EmitContext().AssignCommentAndSourceMapRanges(statement, node.AsNode());
            return statement;
        }
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelExportAssignment(node *ast.ExportAssignment) *ast.Node</c>。</summary>
    private Node? VisitTopLevelExportAssignment(ExportAssignment node)
    {
        if (node.IsExportEquals)
        {
            return null;
        }

        return CreateExportStatement(
            Factory().NewIdentifier("default"),
            Visitor().VisitNode(node.Expression)!,
            node.Loc, /*location*/
            true,     /*allowComments*/
            false     /*liveBinding*/
        );
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelFunctionDeclaration(node *ast.FunctionDeclaration) *ast.Node</c>。</summary>
    private Node? VisitTopLevelFunctionDeclaration(FunctionDeclaration node)
    {
        if (HasSyntacticModifier(node.AsNode(), ModifierFlags.Export))
        {
            return Factory().UpdateFunctionDeclaration(
                node,
                ExtractModifiers(EmitContext(), node.Modifiers(), ~ModifierFlags.ExportDefault),
                node.AsteriskToken,
                Factory().GetDeclarationName(node.AsNode()),
                null, /*typeParameters*/
                Visitor().VisitNodes(node.Parameters),
                null, /*type*/
                null, /*fullSignature*/
                Visitor().VisitNode(node.Body));
        }
        else
        {
            return Visitor().VisitEachChild(node.AsNode());
        }
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelClassDeclaration(node *ast.ClassDeclaration) *ast.Node</c>。</summary>
    private Node? VisitTopLevelClassDeclaration(ClassDeclaration node)
    {
        var statements = new List<Node>();
        if (HasSyntacticModifier(node.AsNode(), ModifierFlags.Export))
        {
            statements.Add(Factory().UpdateClassDeclaration(
                node,
                Visitor().VisitModifiers(ExtractModifiers(EmitContext(), node.Modifiers(), ~ModifierFlags.ExportDefault)),
                Factory().GetDeclarationName(node.AsNode()),
                null, /*typeParameters*/
                Visitor().VisitNodes(node.HeritageClauses),
                Visitor().VisitNodes(node.Members)));
        }
        else
        {
            statements.Add(Visitor().VisitEachChild(node.AsNode())!);
        }
        statements = AppendExportsOfClassOrFunctionDeclaration(statements, node.AsNode());
        return SingleOrMany(statements.ToArray(), Factory());
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelVariableStatement(node *ast.VariableStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelVariableStatement(VariableStatement node)
    {
        var statements = new List<Node>();
        if (HasSyntacticModifier(node.AsNode(), ModifierFlags.Export))
        {
            // export var a = b;
            List<Node>? variables = [];
            List<Node>? expressions = [];
            ModifierList? modifiers = null;

            void CommitPendingVariables()
            {
                if (variables is { Count: > 0 })
                {
                    var variableList = Factory().NewNodeList(variables.ToArray());
                    var statement = Factory().UpdateVariableStatement(
                        node,
                        modifiers,
                        Factory().UpdateVariableDeclarationList(
                            node.DeclarationList!.AsVariableDeclarationList()!,
                            variableList,
                            node.DeclarationList.Flags));
                    if (statements.Count > 0)
                    {
                        EmitContext().AddEmitFlags(statement, EmitFlags.NoComments);
                    }
                    statements.Add(statement);
                    variables = null;
                }
            }

            void CommitPendingExpressions()
            {
                if (expressions is { Count: > 0 })
                {
                    var statement = Factory().NewExpressionStatement(Factory().InlineExpressions(expressions.ToArray())!);
                    EmitContext().AssignCommentAndSourceMapRanges(statement, node.AsNode());
                    if (statements.Count > 0)
                    {
                        EmitContext().AddEmitFlags(statement, EmitFlags.NoComments);
                    }
                    statements.Add(statement);
                    expressions = null;
                }
            }

            void PushVariable(Node variable)
            {
                CommitPendingExpressions();
                variables ??= [];
                variables.Add(variable);
            }

            void PushExpression(Node expression)
            {
                CommitPendingVariables();
                expressions ??= [];
                expressions.Add(expression);
            }

            // If we're exporting these variables, then these just become assignments to 'exports.x'.
            foreach (var variable in node.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes ?? [])
            {
                var v = variable.AsVariableDeclaration()!;
                var updatedVariable = variable;

                if (IsIdentifier(v.Name()!) && IsLocalName(EmitContext(), v.Name()!))
                {
                    // A "local name" generally means a variable declaration that *shouldn't* be
                    // converted to `exports.x = ...`, even if the declaration is exported. This
                    // usually indicates a class or function declaration that was converted into
                    // a variable declaration, as most references to the declaration will remain
                    // untransformed (i.e., `new C` rather than `new exports.C`). In these cases,
                    // an `export { x }` declaration will follow.

                    if (modifiers is null)
                    {
                        modifiers = ExtractModifiers(EmitContext(), node.Modifiers(), ~ModifierFlags.ExportDefault);
                    }

                    if (v.Initializer is not null)
                    {
                        // Go 在这里给 `range` 迭代变量 `variable` 重新赋值；C# 不允许给 foreach 变量赋值 →
                        // 用同值的局部变量承接（语义完全一致）。
                        updatedVariable = Factory().UpdateVariableDeclaration(
                            v,
                            v.Name(),
                            null, /*exclamationToken*/
                            null, /*type*/
                            CreateExportExpression(
                                v.Name()!,
                                Visitor().VisitNode(v.Initializer)!,
                                null,
                                false /*liveBinding*/));
                    }

                    PushVariable(updatedVariable);
                }
                else if (v.Initializer is not null && !IsBindingPattern(v.Name()) && (IsArrowFunction(v.Initializer) || IsFunctionExpression(v.Initializer) || IsClassExpression(v.Initializer)))
                {
                    // preserve variable declarations for functions and classes to assign names

                    PushVariable(Factory().NewVariableDeclaration(
                        v.Name(),
                        v.ExclamationToken,
                        v.Type,
                        Visitor().VisitNode(v.Initializer)));

                    var propertyAccess = Factory().NewPropertyAccessExpression(
                        Factory().NewIdentifier("exports"),
                        null, /*questionDotToken*/
                        v.Name(),
                        NodeFlags.None);
                    EmitContext().AssignCommentAndSourceMapRanges(propertyAccess, v.Name()!);

                    PushExpression(Factory().NewAssignmentExpression(
                        propertyAccess,
                        v.Name()!.Clone(Factory())!));
                }
                else if (IsIdentifier(v.Name()!))
                {
                    var expression = TransformInitializedVariable(v);
                    if (expression is not null)
                    {
                        PushExpression(Visitor().VisitNode(expression)!);
                    }
                }
                else if (IsBindingPattern(v.Name()))
                {
                    // For binding patterns with export modifier, use flattenDestructuringAssignment
                    // to decompose into individual export assignments
                    var expression = TransformInitializedVariable(v);
                    if (expression is not null)
                    {
                        PushExpression(expression);
                    }
                }
                else
                {
                    // For binding patterns, we can't do exports.{pattern} = value
                    // Just emit the assignment and let appendExportsOfVariableStatement handle the exports
                    var expression = ConvertVariableDeclarationToAssignmentExpression(EmitContext(), v);
                    if (expression is not null)
                    {
                        PushExpression(Visitor().VisitNode(expression)!);
                    }
                }
            }

            CommitPendingVariables();
            CommitPendingExpressions();
            statements = AppendExportsOfVariableStatement(statements, node);
            return SingleOrMany(statements.ToArray(), Factory());
        }
        return VisitTopLevelNestedVariableStatement(node);
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) transformInitializedVariable(node *ast.VariableDeclaration) *ast.Expression</c>。</summary>
    private Node? TransformInitializedVariable(VariableDeclaration node)
    {
        if (node.Initializer is null)
        {
            return null;
        }
        var name = node.Name()!;
        if (IsBindingPattern(name))
        {
            // Convert the binding pattern into an equivalent assignment expression and visit it
            // as a destructuring assignment. This preserves native destructuring (and therefore
            // iterator semantics for array patterns) whenever each leaf identifier can be
            // substituted to an export reference. Only when the destructuring would assign to
            // re-aliased or multi-exported names (where native destructuring cannot update all
            // targets) does `visitDestructuringAssignment` fall back to flattening.
            var assignment = ConvertVariableDeclarationToAssignmentExpression(EmitContext(), node)!;
            var grandparentNode = PushNode(assignment);
            try
            {
                return VisitDestructuringAssignment(assignment.AsBinaryExpression()!, true /*valueIsDiscarded*/);
            }
            finally
            {
                PopNode(grandparentNode);
            }
        }
        var propertyAccess = Factory().NewPropertyAccessExpression(
            Factory().NewIdentifier("exports"),
            null, /*questionDotToken*/
            name,
            NodeFlags.None);
        EmitContext().AssignCommentAndSourceMapRanges(propertyAccess, name);
        return Factory().NewAssignmentExpression(propertyAccess, node.Initializer);
    }

    // Visits a top-level nested variable statement as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedVariableStatement(node *ast.VariableStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedVariableStatement(VariableStatement node)
    {
        var statements = new List<Node>();
        statements.Add(Visitor().VisitEachChild(node.AsNode())!);
        statements = AppendExportsOfVariableStatement(statements, node);
        return SingleOrMany(statements.ToArray(), Factory());
    }

    // Visits a top-level nested `for` statement as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedForStatement(node *ast.ForStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedForStatement(ForStatement node)
    {
        if (node.Initializer is not null && IsVariableDeclarationList(node.Initializer!) && (node.Initializer.Flags & NodeFlags.BlockScoped) == 0)
        {
            var exportStatements = AppendExportsOfVariableDeclarationList([], node.Initializer.AsVariableDeclarationList()!, false /*isForInOrOfInitializer*/);
            if (exportStatements.Count > 0)
            {
                // given:
                //   export { x }
                //   for (var x = 0; ;) { }
                // emits:
                //   var x = 0;
                //   exports.x = x;
                //   for (; ;) { }

                var statements = new List<Node>();
                var varDeclList = _discardedValueVisitor.VisitNode(node.Initializer)!;
                var varStatement = Factory().NewVariableStatement(null /*modifiers*/, varDeclList);
                statements.Add(varStatement);
                statements.AddRange(exportStatements);

                var condition = Visitor().VisitNode(node.Condition);
                var incrementor = _discardedValueVisitor.VisitNode(node.Incrementor);
                var body = EmitContext().VisitIterationBody(node.Statement!, _topLevelNestedVisitor);
                statements.Add(Factory().UpdateForStatement(
                    node,
                    null, /*initializer*/
                    condition,
                    incrementor,
                    body));
                return SingleOrMany(statements.ToArray(), Factory());
            }
        }
        return Factory().UpdateForStatement(
            node,
            _discardedValueVisitor.VisitNode(node.Initializer),
            Visitor().VisitNode(node.Condition),
            _discardedValueVisitor.VisitNode(node.Incrementor),
            EmitContext().VisitIterationBody(node.Statement!, _topLevelNestedVisitor));
    }

    // Visits a top-level nested `for..in` or `for..of` statement as it may contain `var` declarations that are hoisted and
    // may still be exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedForInOrOfStatement(node *ast.ForInOrOfStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedForInOrOfStatement(ForInOrOfStatement node)
    {
        if (IsVariableDeclarationList(node.Initializer!) && (node.Initializer!.Flags & NodeFlags.BlockScoped) == 0)
        {
            var exportStatements = AppendExportsOfVariableDeclarationList([], node.Initializer.AsVariableDeclarationList()!, true /*isForInOrOfInitializer*/);
            if (exportStatements.Count > 0)
            {
                // given:
                //   export { x }
                //   for (var x in y) {
                //     ...
                //   }
                // emits:
                //   for (var x in y) {
                //     exports.x = x;
                //     ...
                //   }

                var initializer = _discardedValueVisitor.VisitNode(node.Initializer);
                var expression = Visitor().VisitNode(node.Expression);
                var body = EmitContext().VisitIterationBody(node.Statement!, _topLevelNestedVisitor);
                if (IsBlock(body!))
                {
                    var block = body!.AsBlock()!;
                    var bodyStatements = new List<Node>(exportStatements);
                    bodyStatements.AddRange(block.Statements!.Nodes ?? []);
                    var bodyStatementList = Factory().NewNodeList(bodyStatements.ToArray());
                    bodyStatementList.Loc = block.Statements!.Loc;
                    body = Factory().UpdateBlock(block, bodyStatementList, block.MultiLine);
                }
                else
                {
                    var bodyStatements = new List<Node>(exportStatements) { body! };
                    body = Factory().NewBlock(Factory().NewNodeList(bodyStatements.ToArray()), true /*multiLine*/);
                }
                return Factory().UpdateForInOrOfStatement(node, node.AwaitModifier, initializer, expression, body);
            }
        }
        return Factory().UpdateForInOrOfStatement(
            node,
            node.AwaitModifier,
            _discardedValueVisitor.VisitNode(node.Initializer),
            Visitor().VisitNode(node.Expression),
            EmitContext().VisitIterationBody(node.Statement!, _topLevelNestedVisitor));
    }

    // Visits a top-level nested `do` statement as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedDoStatement(node *ast.DoStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedDoStatement(DoStatement node)
    {
        return Factory().UpdateDoStatement(
            node,
            EmitContext().VisitIterationBody(node.Statement!, _topLevelNestedVisitor),
            Visitor().VisitNode(node.Expression));
    }

    // Visits a top-level nested `while` statement as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedWhileStatement(node *ast.WhileStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedWhileStatement(WhileStatement node)
    {
        return Factory().UpdateWhileStatement(
            node,
            Visitor().VisitNode(node.Expression),
            EmitContext().VisitIterationBody(node.Statement!, _topLevelNestedVisitor));
    }

    // Visits a top-level nested labeled statement as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedLabeledStatement(node *ast.LabeledStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedLabeledStatement(LabeledStatement node)
    {
        var statement = _topLevelNestedVisitor.VisitEmbeddedStatement(node.Statement);
        if (statement is null)
        {
            statement = Factory().NewEmptyStatement();
        }
        return Factory().UpdateLabeledStatement(node, node.Label, statement);
    }

    // Visits a top-level nested `with` statement as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedWithStatement(node *ast.WithStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedWithStatement(WithStatement node)
    {
        return Factory().UpdateWithStatement(
            node,
            Visitor().VisitNode(node.Expression),
            _topLevelNestedVisitor.VisitEmbeddedStatement(node.Statement));
    }

    // Visits a top-level nested `if` statement as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedIfStatement(node *ast.IfStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedIfStatement(IfStatement node)
    {
        var expression = Visitor().VisitNode(node.Expression);
        var thenStatement = _topLevelNestedVisitor.VisitEmbeddedStatement(node.ThenStatement);
        if (thenStatement is null)
        {
            thenStatement = Factory().NewBlock(Factory().NewNodeList(null), false /*multiLine*/);
        }
        var elseStatement = _topLevelNestedVisitor.VisitEmbeddedStatement(node.ElseStatement);
        return Factory().UpdateIfStatement(node, expression, thenStatement, elseStatement);
    }

    // Visits a top-level nested `switch` statement as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedSwitchStatement(node *ast.SwitchStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedSwitchStatement(SwitchStatement node)
    {
        return Factory().UpdateSwitchStatement(
            node,
            Visitor().VisitNode(node.Expression),
            _topLevelNestedVisitor.VisitNode(node.CaseBlock));
    }

    // Visits a top-level nested case block as it may contain `var` declarations that are hoisted and may still be exported
    // with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedCaseBlock(node *ast.CaseBlock) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedCaseBlock(CaseBlock node)
    {
        return _topLevelNestedVisitor.VisitEachChild(node.AsNode());
    }

    // Visits a top-level nested `case` or `default` clause as it may contain `var` declarations that are hoisted and may
    // still be exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedCaseOrDefaultClause(node *ast.CaseOrDefaultClause) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedCaseOrDefaultClause(CaseOrDefaultClause node)
    {
        return Factory().UpdateCaseOrDefaultClause(
            node,
            Visitor().VisitNode(node.Expression),
            _topLevelNestedVisitor.VisitNodes(node.Statements));
    }

    // Visits a top-level nested `try` statement as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedTryStatement(node *ast.TryStatement) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedTryStatement(TryStatement node)
    {
        return _topLevelNestedVisitor.VisitEachChild(node.AsNode());
    }

    // Visits a top-level nested `catch` clause as it may contain `var` declarations that are hoisted and may still be
    // exported with `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedCatchClause(node *ast.CatchClause) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedCatchClause(CatchClause node)
    {
        return Factory().UpdateCatchClause(
            node,
            node.VariableDeclaration,
            _topLevelNestedVisitor.VisitNode(node.Block));
    }

    // Visits a top-level nested block as it may contain `var` declarations that are hoisted and may still be exported with
    // `export {}`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedBlock(node *ast.Block) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedBlock(Block node)
    {
        return _topLevelNestedVisitor.VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitForStatement(node *ast.ForStatement) *ast.Node</c>。</summary>
    private Node? VisitForStatement(ForStatement node)
    {
        return Factory().UpdateForStatement(
            node,
            _discardedValueVisitor.VisitNode(node.Initializer),
            Visitor().VisitNode(node.Condition),
            _discardedValueVisitor.VisitNode(node.Incrementor),
            EmitContext().VisitIterationBody(node.Statement!, _topLevelNestedVisitor));
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitForInOrOfStatement(node *ast.ForInOrOfStatement) *ast.Node</c>。</summary>
    private Node? VisitForInOrOfStatement(ForInOrOfStatement node)
    {
        return Factory().UpdateForInOrOfStatement(
            node,
            node.AwaitModifier,
            _discardedValueVisitor.VisitNode(node.Initializer),
            Visitor().VisitNode(node.Expression),
            EmitContext().VisitIterationBody(node.Statement!, _topLevelNestedVisitor));
    }

    // Visits an expression statement whose value will be discarded at runtime.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitExpressionStatement(node *ast.ExpressionStatement) *ast.Node</c>。</summary>
    private Node? VisitExpressionStatement(ExpressionStatement node)
    {
        return _discardedValueVisitor.VisitEachChild(node.AsNode());
    }

    // Visits a `void` expression whose value will be discarded at runtime.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitVoidExpression(node *ast.VoidExpression) *ast.Node</c>。</summary>
    private Node? VisitVoidExpression(VoidExpression node)
    {
        return _discardedValueVisitor.VisitEachChild(node.AsNode());
    }
}

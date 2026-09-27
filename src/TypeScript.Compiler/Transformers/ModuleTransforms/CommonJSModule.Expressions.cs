// ported from: internal/transformers/moduletransforms/commonjsmodule.go:1364-2162
// （表达式访问器：括号/部分发射/二元/解构/前后缀自增自减/调用/tagged template/shorthand 属性/标识符，
//   以及 import()/require() 的降级与导出名查询）
//
// 与 commonjsmodule.go:1-710 / 711-1363 分片共享同一个 `partial class CommonJSModuleTransformer`；分片顺序不影响语义。
// 形状说明见 CommonJSModule.cs 文件头。
// 本片特有：
// 1) Go 的 `transformers.FlattenDestructuringAssignment(&tx.Transformer, …)` → C# `FlattenDestructuringAssignment(this, …)`
//    （C# 没有嵌入字段，`&tx.Transformer` 就是"这个变换器对象自身"）。
// 2) Go 的方法 `shouldTransformImportCall` 与包级函数 `ast.ShouldTransformImportCall` 同名 → 包级那支必须用别名
//    限定：本命名空间里裸写 `Ast` 会解析到**命名空间** TypeScript.Ast（不是静态类），所以文件顶部加了
//    `using AstStatics = global::TypeScript.Ast.Ast;`，调用点写 `AstStatics.ShouldTransformImportCall(...)`
//    （C# 类成员也会遮蔽 using static 引入的同名成员，故不能靠裸名）。
// 3) Go 的包级函数都是本包（moduletransforms）的 → C# 写 `ModuleTransforms.X(...)`（未加 `using static` 自己的
//    静态类：它与 T1 的 `Transformers` 有同名成员 `IsSimpleInlineableExpression`，加进来会 CS0104）。

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Printer;
using AstStatics = global::TypeScript.Ast.Ast; // 消歧：在本命名空间里 `Ast` 会先解析到命名空间 TypeScript.Ast，取静态类要显式限定
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.ModuleTransforms;

public sealed partial class CommonJSModuleTransformer
{
    // Visits a parenthesized expression whose value may be discarded at runtime.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitParenthesizedExpression(node *ast.ParenthesizedExpression, resultIsDiscarded bool) *ast.Node</c>。</summary>
    private Node? VisitParenthesizedExpression(ParenthesizedExpression node, bool resultIsDiscarded)
    {
        var expression = IfElse(resultIsDiscarded, _discardedValueVisitor, Visitor()).VisitNode(node.Expression);
        return Factory().UpdateParenthesizedExpression(node, expression);
    }

    // Visits a partially emitted expression whose value may be discarded at runtime.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitPartiallyEmittedExpression(node *ast.PartiallyEmittedExpression, resultIsDiscarded bool) *ast.Node</c>。</summary>
    private Node? VisitPartiallyEmittedExpression(PartiallyEmittedExpression node, bool resultIsDiscarded)
    {
        var expression = IfElse(resultIsDiscarded, _discardedValueVisitor, Visitor()).VisitNode(node.Expression);
        return Factory().UpdatePartiallyEmittedExpression(node, expression);
    }

    // Visits a binary expression whose value may be discarded, or which might contain an assignment to an exported
    // identifier.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitBinaryExpression(node *ast.BinaryExpression, resultIsDiscarded bool) *ast.Node</c>。</summary>
    private Node? VisitBinaryExpression(BinaryExpression node, bool resultIsDiscarded)
    {
        if (IsDestructuringAssignment(node.AsNode()))
        {
            return VisitDestructuringAssignment(node, resultIsDiscarded);
        }

        if (IsAssignmentExpression(node.AsNode(), false /*excludeCompoundAssignment*/))
        {
            return VisitAssignmentExpression(node);
        }

        if (IsCommaExpression(node.AsNode()))
        {
            return VisitCommaExpression(node, resultIsDiscarded);
        }

        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitAssignmentExpression(node *ast.BinaryExpression) *ast.Node</c>。</summary>
    private Node? VisitAssignmentExpression(BinaryExpression node)
    {
        // When we see an assignment expression whose left-hand side is an exported symbol,
        // we should ensure all exports of that symbol are updated with the correct value.
        //
        // - We do not transform generated identifiers unless they are file-level reserved names.
        // - We do not transform identifiers tagged with the LocalName flag.
        // - We only transform identifiers that are exported at the top level.
        if (IsIdentifier(node.Left!) &&
            (!IsGeneratedIdentifier(EmitContext(), node.Left!) || ModuleTransforms.IsFileLevelReservedGeneratedIdentifier(EmitContext(), node.Left!)) &&
            !IsLocalName(EmitContext(), node.Left!))
        {
            var exportedNames = GetExports(node.Left!);
            if (exportedNames.Count > 0)
            {
                // For each additional export of the declaration, apply an export assignment.
                Node expression = Visitor().VisitEachChild(node.AsNode())!;
                foreach (var exportName in exportedNames)
                {
                    expression = CreateExportExpression(exportName, expression, node.Loc /*location*/, false /*liveBinding*/);
                }
                return expression;
            }
        }

        return Visitor().VisitEachChild(node.AsNode());
    }

    // Visits a destructuring assignment which might target an exported identifier.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitDestructuringAssignment(node *ast.BinaryExpression, valueIsDiscarded bool) *ast.Node</c>。</summary>
    private Node? VisitDestructuringAssignment(BinaryExpression node, bool valueIsDiscarded)
    {
        if (DestructuringNeedsFlattening(node.Left!))
        {
            return FlattenDestructuringAssignment(
                this,
                node.AsNode(),
                !valueIsDiscarded, /*needsValue*/
                FlattenLevel.All,
                CreateAllExportExpressions);
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    // destructuringNeedsFlattening checks whether a destructuring assignment target contains any
    // exported identifiers that need to be flattened into individual export assignments.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) destructuringNeedsFlattening(node *ast.Node) bool</c>。</summary>
    private bool DestructuringNeedsFlattening(Node node)
    {
        if (IsObjectLiteralExpression(node))
        {
            foreach (var elem in node.Properties() ?? [])
            {
                switch (elem.Kind)
                {
                    case Kind.PropertyAssignment:
                        if (DestructuringNeedsFlattening(elem.Initializer()!))
                        {
                            return true;
                        }
                        break;
                    case Kind.ShorthandPropertyAssignment:
                        if (DestructuringNeedsFlattening(elem.Name()!))
                        {
                            return true;
                        }
                        break;
                    case Kind.SpreadAssignment:
                        if (DestructuringNeedsFlattening(elem.Expression()!))
                        {
                            return true;
                        }
                        break;
                    case Kind.MethodDeclaration:
                    case Kind.GetAccessor:
                    case Kind.SetAccessor:
                        return false;
                }
            }
        }
        else if (IsArrayLiteralExpression(node))
        {
            foreach (var elem in node.AsArrayLiteralExpression()!.Elements!.Nodes ?? [])
            {
                if (IsSpreadElement(elem))
                {
                    if (DestructuringNeedsFlattening(elem.Expression()!))
                    {
                        return true;
                    }
                }
                else if (DestructuringNeedsFlattening(elem))
                {
                    return true;
                }
            }
        }
        else if (IsIdentifier(node))
        {
            var exportedNames = GetExports(node);
            if (IsExportName(EmitContext(), node))
            {
                // The identifier is already wrapped to be an export reference; tolerate up to one
                // matching export.
                return exportedNames.Count > 1;
            }
            if (exportedNames.Count == 0)
            {
                return false;
            }
            // A single direct export whose export name matches the identifier text can be handled
            // natively: substitution will rewrite the identifier to `exports.X`, so no flattening
            // is needed. Re-aliased exports (where the export name differs from the local name) or
            // multi-exported names cannot be expressed natively in a destructuring assignment.
            if (exportedNames.Count == 1 && IsDirectExport(node) && exportedNames[0].Text() == node.Text())
            {
                return false;
            }
            return true;
        }
        return false;
    }

    // createAllExportExpressions is the callback used during destructuring flattening to create
    // export expressions for each exported identifier binding.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) createAllExportExpressions(name *ast.IdentifierNode, value *ast.Expression, location *core.TextRange) *ast.Expression</c>。</summary>
    private Node CreateAllExportExpressions(Node name, Node value, TextRange location)
    {
        var exportedNames = GetExports(name);
        if (exportedNames.Count > 0)
        {
            // If the name is directly exported (i.e., `export let x`), assign to exports.name directly.
            // Otherwise, assign to the local binding first (i.e., `let x; export { x }`).
            Node expression;
            if (IsDirectExport(name))
            {
                // Create exports.name = value to handle the direct export assignment,
                // since the Go port doesn't have an onSubstituteNode mechanism to rewrite identifiers.
                var exportName = name.Clone(Factory())!;
                EmitContext().AddEmitFlags(exportName, EmitFlags.NoComments | EmitFlags.NoSourceMap);
                var propertyAccess = Factory().NewPropertyAccessExpression(
                    Factory().NewIdentifier("exports"),
                    null, /*questionDotToken*/
                    exportName,
                    NodeFlags.None);
                EmitContext().AddEmitFlags(propertyAccess, EmitFlags.NoComments);
                expression = Factory().NewAssignmentExpression(propertyAccess, value);
                EmitContext().AssignCommentAndSourceMapRanges(expression, name);
            }
            else
            {
                expression = Factory().NewAssignmentExpression(name, value);
            }
            foreach (var exportName in exportedNames)
            {
                expression = CreateExportExpression(exportName, expression, location, false /*liveBinding*/);
            }
            return expression;
        }
        // If the identifier is directly exported but has no additional export aliases,
        // still write to exports.name.
        if (IsDirectExport(name))
        {
            var exportName = name.Clone(Factory())!;
            EmitContext().AddEmitFlags(exportName, EmitFlags.NoComments | EmitFlags.NoSourceMap);
            var propertyAccess = Factory().NewPropertyAccessExpression(
                Factory().NewIdentifier("exports"),
                null, /*questionDotToken*/
                exportName,
                NodeFlags.None);
            EmitContext().AddEmitFlags(propertyAccess, EmitFlags.NoComments);
            var result = Factory().NewAssignmentExpression(propertyAccess, value);
            EmitContext().AssignCommentAndSourceMapRanges(result, name);
            return result;
        }
        return Factory().NewAssignmentExpression(name, value);
    }

    // isDirectExport checks whether the identifier is directly exported from the source file
    // (e.g., `export let x` or `export function f()`), as opposed to being re-exported via
    // `export { x }` for a locally-declared variable.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) isDirectExport(name *ast.IdentifierNode) bool</c>。</summary>
    private bool IsDirectExport(Node name)
    {
        var exportContainer = _resolver!.GetReferencedExportContainer(EmitContext().MostOriginal(name)!, false /*prefixLocals*/);
        return exportContainer is not null && IsSourceFile(exportContainer);
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitAssignmentProperty(node *ast.PropertyAssignment) *ast.Node</c>。</summary>
    private Node? VisitAssignmentProperty(PropertyAssignment node)
    {
        return Factory().UpdatePropertyAssignment(
            node,
            null, /*modifiers*/
            Visitor().VisitNode(node.Name()),
            null, /*postfixToken*/
            null, /*typeNode*/
            _assignmentPatternVisitor.VisitNode(node.Initializer));
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitShorthandAssignmentProperty(node *ast.ShorthandPropertyAssignment) *ast.Node</c>。</summary>
    private Node? VisitShorthandAssignmentProperty(ShorthandPropertyAssignment node)
    {
        var target = VisitDestructuringAssignmentTargetNoStack(node.Name()!)!;
        if (IsIdentifier(target))
        {
            return Factory().UpdateShorthandPropertyAssignment(
                node,
                null, /*modifiers*/
                target,
                null, /*postfixToken*/
                null, /*typeNode*/
                node.EqualsToken,
                Visitor().VisitNode(node.ObjectAssignmentInitializer));
        }
        if (node.ObjectAssignmentInitializer is not null)
        {
            var equalsToken = node.EqualsToken;
            equalsToken ??= Factory().NewToken(Kind.EqualsToken);
            target = Factory().NewBinaryExpression(
                null, /*modifiers*/
                target,
                null, /*typeNode*/
                equalsToken,
                Visitor().VisitNode(node.ObjectAssignmentInitializer));
        }
        var updated = Factory().NewPropertyAssignment(
            null, /*modifiers*/
            node.Name(),
            null, /*postfixToken*/
            null, /*typeNode*/
            target);
        EmitContext().SetOriginal(updated, node.AsNode());
        EmitContext().AssignCommentAndSourceMapRanges(updated, node.AsNode());
        return updated;
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitAssignmentRestProperty(node *ast.SpreadAssignment) *ast.Node</c>。</summary>
    private Node? VisitAssignmentRestProperty(SpreadAssignment node)
    {
        return Factory().UpdateSpreadAssignment(
            node,
            VisitDestructuringAssignmentTarget(node.Expression!));
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitAssignmentRestElement(node *ast.SpreadElement) *ast.Node</c>。</summary>
    private Node? VisitAssignmentRestElement(SpreadElement node)
    {
        return Factory().UpdateSpreadElement(
            node,
            VisitDestructuringAssignmentTarget(node.Expression!));
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitAssignmentElement(node *ast.Node) *ast.Node</c>。</summary>
    private Node? VisitAssignmentElement(Node node)
    {
        if (IsBinaryExpression(node))
        {
            var n = node.AsBinaryExpression()!;
            if (n.OperatorToken!.Kind == Kind.EqualsToken)
            {
                return Factory().UpdateBinaryExpression(
                    n,
                    null, /*modifiers*/
                    VisitDestructuringAssignmentTarget(n.Left!),
                    null, /*typeNode*/
                    n.OperatorToken,
                    Visitor().VisitNode(n.Right));
            }
        }

        return VisitDestructuringAssignmentTargetNoStack(node);
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitDestructuringAssignmentTarget(node *ast.Node) *ast.Node</c>。</summary>
    private Node? VisitDestructuringAssignmentTarget(Node node)
    {
        var grandparentNode = PushNode(node);
        try
        {
            switch (node.Kind)
            {
                case Kind.ObjectLiteralExpression:
                case Kind.ArrayLiteralExpression:
                    node = VisitAssignmentPatternNoStack(node)!;
                    break;
                default:
                    node = VisitDestructuringAssignmentTargetNoStack(node)!;
                    break;
            }
            return node;
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitDestructuringAssignmentTargetNoStack(node *ast.Node) *ast.Node</c>。</summary>
    private Node? VisitDestructuringAssignmentTargetNoStack(Node node)
    {
        if (IsIdentifier(node) &&
            (!IsGeneratedIdentifier(EmitContext(), node) || ModuleTransforms.IsFileLevelReservedGeneratedIdentifier(EmitContext(), node)) &&
            !IsLocalName(EmitContext(), node))
        {
            Node expression = VisitExpressionIdentifier(node)!;
            var exportedNames = GetExports(node);
            if (exportedNames.Count > 0)
            {
                // transforms:
                //  var x;
                //  export { x }
                //  { x: x } = y
                // to:
                //  { x: { set value(v) { exports.x = x = v; } }.value } = y

                var value = Factory().NewUniqueNameEx("value", new AutoGenerateOptions
                {
                    Flags = GeneratedIdentifierFlags.Optimistic,
                });
                expression = Factory().NewAssignmentExpression(expression, value);

                foreach (var exportName in exportedNames)
                {
                    expression = CreateExportExpression(exportName, expression, null /*location*/, false /*liveBinding*/);
                }

                var statement = Factory().NewExpressionStatement(expression);
                var statementList = Factory().NewNodeList([statement]);
                var param = Factory().NewParameterDeclaration(
                    null, /*modifiers*/
                    null, /*dotDotDotToken*/
                    value,
                    null, /*questionToken*/
                    null, /*type*/
                    null /*initializer*/);
                var valueSetter = Factory().NewSetAccessorDeclaration(
                    null, /*modifiers*/
                    Factory().NewIdentifier("value"),
                    null, /*typeParameters*/
                    Factory().NewNodeList([param]),
                    null, /*returnType*/
                    null, /*fullSignature*/
                    Factory().NewBlock(statementList, false /*multiLine*/));
                var propertyList = Factory().NewNodeList([valueSetter]);
                expression = Factory().NewObjectLiteralExpression(propertyList, false /*multiLine*/);
                expression = Factory().NewPropertyAccessExpression(expression, null /*questionDotToken*/, Factory().NewIdentifier("value"), NodeFlags.None);
            }
            return expression;
        }

        return VisitNoStack(node, false /*resultIsDiscarded*/);
    }

    // Visits a comma expression whose left-hand value is always discard, and whose right-hand value may be discarded at runtime.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitCommaExpression(node *ast.BinaryExpression, resultIsDiscarded bool) *ast.Node</c>。</summary>
    private Node? VisitCommaExpression(BinaryExpression node, bool resultIsDiscarded)
    {
        var left = _discardedValueVisitor.VisitNode(node.Left);
        var right = IfElse(resultIsDiscarded, _discardedValueVisitor, Visitor()).VisitNode(node.Right);
        return Factory().UpdateBinaryExpression(node, null /*modifiers*/, left, null /*typeNode*/, node.OperatorToken, right);
    }

    // Visits a prefix unary expression that might modify an exported identifier.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitPrefixUnaryExpression(node *ast.PrefixUnaryExpression, resultIsDiscarded bool) *ast.Node</c>。</summary>
    private Node? VisitPrefixUnaryExpression(PrefixUnaryExpression node, bool resultIsDiscarded)
    {
        // When we see a prefix increment expression whose operand is an exported
        // symbol, we should ensure all exports of that symbol are updated with the correct
        // value.
        //
        // - We do not transform generated identifiers for any reason.
        // - We do not transform identifiers tagged with the LocalName flag.
        // - We do not transform identifiers that were originally the name of an enum or
        //   namespace due to how they are transformed in TypeScript.
        // - We only transform identifiers that are exported at the top level.
        if ((node.Operator == Kind.PlusPlusToken || node.Operator == Kind.MinusMinusToken) &&
            IsIdentifier(node.Operand!) &&
            !IsLocalName(EmitContext(), node.Operand!))
        {
            var exportedNames = GetExports(node.Operand!);
            if (exportedNames.Count > 0)
            {
                // given:
                //   var x = 0;
                //   export { x }
                //   ++x;
                // emits:
                //   var x = 0;
                //   exports.x = x;
                //   exports.x = ++x;
                // note:
                //   after the operation, `exports.x` will hold the value of `x` after the increment.

                Node expression = Factory().UpdatePrefixUnaryExpression(node, node.Operator, Visitor().VisitNode(node.Operand)!);
                foreach (var exportName in exportedNames)
                {
                    expression = CreateExportExpression(exportName, expression, null /*location*/, false /*liveBinding*/);
                    EmitContext().AssignCommentAndSourceMapRanges(expression, node.AsNode());
                }
                return expression;
            }
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    // Visits a postfix unary expression that might modify an exported identifier.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitPostfixUnaryExpression(node *ast.PostfixUnaryExpression, resultIsDiscarded bool) *ast.Node</c>。</summary>
    private Node? VisitPostfixUnaryExpression(PostfixUnaryExpression node, bool resultIsDiscarded)
    {
        // When we see a postfix increment expression whose operand is an exported
        // symbol, we should ensure all exports of that symbol are updated with the correct
        // value.
        //
        // - We do not transform generated identifiers for any reason.
        // - We do not transform identifiers tagged with the LocalName flag.
        // - We do not transform identifiers that were originally the name of an enum or
        //   namespace due to how they are transformed in TypeScript.
        // - We only transform identifiers that are exported at the top level.
        if ((node.Operator == Kind.PlusPlusToken || node.Operator == Kind.MinusMinusToken) &&
            IsIdentifier(node.Operand!) &&
            !IsLocalName(EmitContext(), node.Operand!))
        {
            var exportedNames = GetExports(node.Operand!);
            if (exportedNames.Count > 0)
            {
                // given (value is discarded):
                //   var x = 0;
                //   export { x }
                //   x++;
                // emits:
                //   var x = 0, y;
                //   exports.x = x;
                //   exports.x = (x++, x);
                // note:
                //   after the operation, `exports.x` will hold the value of `x` after the increment.
                //
                // given (value is not discarded):
                //   var x = 0, y;
                //   export { x }
                //   y = x++;
                // emits:
                //   var _a;
                //   var x = 0, y;
                //   exports.x = x;
                //   y = (exports.x = (_a = x++, x), _a);
                // note:
                //   after the operation, `exports.x` will hold the value of `x` after the increment, while
                //   `y` will hold the value of `x` before the increment.

                Node? temp = null;
                Node expression = Factory().UpdatePostfixUnaryExpression(node, Visitor().VisitNode(node.Operand), node.Operator);
                if (!resultIsDiscarded)
                {
                    temp = Factory().NewTempVariable();
                    EmitContext().AddVariableDeclaration(temp);

                    expression = Factory().NewAssignmentExpression(temp, expression);
                    EmitContext().AssignCommentAndSourceMapRanges(expression, node.AsNode());
                }

                expression = Factory().NewCommaExpression(expression, node.Operand!.Clone(Factory())!);
                EmitContext().AssignCommentAndSourceMapRanges(expression, node.AsNode());

                foreach (var exportName in exportedNames)
                {
                    expression = CreateExportExpression(exportName, expression, null /*location*/, false /*liveBinding*/);
                    EmitContext().AssignCommentAndSourceMapRanges(expression, node.AsNode());
                }

                if (temp is not null)
                {
                    expression = Factory().NewCommaExpression(expression, temp.AsNode());
                    EmitContext().AssignCommentAndSourceMapRanges(expression, node.AsNode());
                }

                return expression;
            }
        }

        return Visitor().VisitEachChild(node.AsNode());
    }

    // Visits a call expression that might reference an imported symbol and thus require an indirect call, or that might
    // be an `import()` or `require()` call that may need to be rewritten.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitCallExpression(node *ast.CallExpression) *ast.Node</c>。</summary>
    private Node? VisitCallExpression(CallExpression node)
    {
        var needsRewrite = false;
        if (_compilerOptions.RewriteRelativeImportExtensions.IsTrue())
        {
            if (IsImportCall(node.AsNode()) && (node.Arguments!.Nodes?.Length ?? 0) > 0 ||
                IsInJSFile(node.AsNode()) && IsRequireCall(node.AsNode(), false /*requireStringLiteralLikeArgument*/))
            {
                needsRewrite = true;
            }
        }
        if (IsImportCall(node.AsNode()) && ShouldTransformImportCall())
        {
            return VisitImportCallExpression(node, needsRewrite);
        }
        if (needsRewrite)
        {
            return ShimOrRewriteImportOrRequireCall(node.AsCallExpression()!);
        }
        if (IsIdentifier(node.Expression!))
        {
            // given:
            //   import { f } from "mod";
            //   f();
            // emits:
            //   const mod_1 = require("mod");
            //   (0, mod_1.f)();
            // note:
            //   the indirect call is applied by the printer by way of the `EFIndirectCall` emit flag.
            Node expression = VisitExpressionIdentifier(node.Expression!)!;
            var updated = Factory().UpdateCallExpression(
                node,
                expression,
                node.QuestionDotToken,
                null, /*typeArguments*/
                Visitor().VisitNodes(node.Arguments),
                node.Flags);
            if (!IsIdentifier(expression) && !IsHelperName(EmitContext(), node.Expression!))
            {
                EmitContext().AddEmitFlags(updated, EmitFlags.IndirectCall);
            }
            return updated;
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) shouldTransformImportCall() bool</c>。</summary>
    private bool ShouldTransformImportCall() =>
        AstStatics.ShouldTransformImportCall(_currentSourceFile!.FileName(), _compilerOptions, _getEmitModuleFormatOfFile!(_currentSourceFile));

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitImportCallExpression(node *ast.CallExpression, rewriteOrShim bool) *ast.Node</c>。</summary>
    private Node? VisitImportCallExpression(CallExpression node, bool rewriteOrShim)
    {
        if (_moduleKind == ModuleKind.None && _languageVersion >= ScriptTarget.ES2020)
        {
            return Visitor().VisitEachChild(node.AsNode());
        }

        var externalModuleName = ModuleTransforms.GetExternalModuleNameLiteral(Factory(), node.AsNode(), _currentSourceFile!, null /*host*/, null /*resolver*/, _compilerOptions);
        var firstArgument = Visitor().VisitNode(FirstOrNil(node.Arguments!.Nodes));

        // Only use the external module name if it differs from the first argument. This allows us to preserve the quote style of the argument on output.
        Node? argument;
        if (externalModuleName is not null && (firstArgument is null || !IsStringLiteral(firstArgument) || firstArgument.Text() != externalModuleName.Text()))
        {
            argument = externalModuleName;
        }
        else if (firstArgument is not null && rewriteOrShim)
        {
            if (IsStringLiteral(firstArgument))
            {
                argument = ModuleTransforms.RewriteModuleSpecifier(EmitContext(), firstArgument, _compilerOptions);
            }
            else
            {
                argument = Factory().NewRewriteRelativeImportExtensionsHelper(firstArgument, _compilerOptions.Jsx == JsxEmit.Preserve);
            }
        }
        else
        {
            argument = firstArgument;
        }
        return CreateImportCallExpressionCommonJS(argument);
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) createImportCallExpressionCommonJS(arg *ast.Expression) *ast.Expression</c>。</summary>
    private Node? CreateImportCallExpressionCommonJS(Node? arg)
    {
        // import(x)
        // emit as
        // Promise.resolve(`${x}`).then((s) => require(s)) /*CommonJS Require*/
        // We have to wrap require in then callback so that require is done in asynchronously
        // if we simply do require in resolve callback in Promise constructor. We will execute the loading immediately
        // If the arg is not inlineable, we have to evaluate and ToString() it in the current scope
        // Otherwise, we inline it in require() so that it's statically analyzable

        var needSyncEval = arg is not null && !ModuleTransforms.IsSimpleInlineableExpression(arg);

        var promiseResolveArguments = new List<Node>();
        if (needSyncEval)
        {
            promiseResolveArguments.Add(
                Factory().NewTemplateExpression(
                    Factory().NewTemplateHead("", "", TokenFlags.None),
                    Factory().NewNodeList([
                        Factory().NewTemplateSpan(arg!, Factory().NewTemplateTail("", "", TokenFlags.None)),
                    ])));
        }
        var promiseResolveCall = Factory().NewCallExpression(
            Factory().NewPropertyAccessExpression(
                Factory().NewIdentifier("Promise"),
                null, /*questionDotToken*/
                Factory().NewIdentifier("resolve"),
                NodeFlags.None),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            Factory().NewNodeList(promiseResolveArguments.ToArray()),
            NodeFlags.None);

        var requireArguments = new List<Node>();
        if (needSyncEval)
        {
            requireArguments.Add(Factory().NewIdentifier("s"));
        }
        else if (arg is not null)
        {
            requireArguments.Add(arg);
        }

        var requireCall = Factory().NewImportStarHelper(
            Factory().NewCallExpression(
                Factory().NewIdentifier("require"),
                null, /*questionDotToken*/
                null, /*typeArguments*/
                Factory().NewNodeList(requireArguments.ToArray()),
                NodeFlags.None));

        var parameters = new List<Node>();
        if (needSyncEval)
        {
            parameters.Add(
                Factory().NewParameterDeclaration(
                    null, /*modifiers*/
                    null, /*dotDotDotToken*/
                    Factory().NewIdentifier("s"),
                    null, /*questionToken*/
                    null, /*type*/
                    null /*initializer*/));
        }

        var function = Factory().NewArrowFunction(
            null, /*modifiers*/
            null, /*typeParameters*/
            Factory().NewNodeList(parameters.ToArray()),
            null, /*type*/
            null, /*fullSignature*/
            Factory().NewToken(Kind.EqualsGreaterThanToken), /*equalsGreaterThanToken*/
            requireCall);

        var downleveledImport = Factory().NewCallExpression(
            Factory().NewPropertyAccessExpression(
                promiseResolveCall,
                null, /*questionDotToken*/
                Factory().NewIdentifier("then"),
                NodeFlags.None),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            Factory().NewNodeList([function]),
            NodeFlags.None);
        return downleveledImport;
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) shimOrRewriteImportOrRequireCall(node *ast.CallExpression) *ast.Node</c>。</summary>
    private Node? ShimOrRewriteImportOrRequireCall(CallExpression node)
    {
        var expression = Visitor().VisitNode(node.Expression);
        var argumentsList = node.Arguments!;
        var argumentNodes = node.Arguments!.Nodes ?? [];
        if (argumentNodes.Length > 0)
        {
            var firstArgument = Visitor().VisitNode(argumentNodes[0]);
            var firstArgumentChanged = false;
            if (IsStringLiteralLike(firstArgument))
            {
                var rewritten = ModuleTransforms.RewriteModuleSpecifier(EmitContext(), firstArgument, _compilerOptions);
                firstArgumentChanged = !ReferenceEquals(rewritten, firstArgument);
                firstArgument = rewritten;
            }
            else
            {
                firstArgument = Factory().NewRewriteRelativeImportExtensionsHelper(firstArgument!, _compilerOptions.Jsx == JsxEmit.Preserve);
                firstArgumentChanged = true;
            }

            var (rest, restChanged) = Visitor().VisitSlice(argumentNodes[1..]);
            if (firstArgumentChanged || restChanged)
            {
                var arguments = new List<Node> { firstArgument! };
                arguments.AddRange(rest ?? []);
                argumentsList = Factory().NewNodeList(arguments.ToArray());
                argumentsList.Loc = node.Arguments.Loc;
            }
        }

        return Factory().UpdateCallExpression(
            node,
            expression,
            node.QuestionDotToken,
            null, /*typeArguments*/
            argumentsList,
            node.Flags);
    }

    // Visits a tagged template expression that might reference an imported symbol and thus require an indirect call.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTaggedTemplateExpression(node *ast.TaggedTemplateExpression) *ast.Node</c>。</summary>
    private Node? VisitTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        if (IsIdentifier(node.Tag!))
        {
            // given:
            //   import { f } from "mod";
            //   f``;
            // emits:
            //   const mod_1 = require("mod");
            //   (0, mod_1.f) ``;
            // note:
            //   the indirect call is applied by the printer by way of the `EFIndirectCall` emit flag.

            Node expression = VisitExpressionIdentifier(node.Tag!)!;
            var updated = Factory().UpdateTaggedTemplateExpression(
                node,
                expression,
                null, /*questionDotToken*/
                null, /*typeArguments*/
                Visitor().VisitNode(node.Template),
                node.Flags);
            if (!IsIdentifier(expression) && !IsHelperName(EmitContext(), node.Tag!))
            {
                EmitContext().AddEmitFlags(updated, EmitFlags.IndirectCall);
            }
            return updated;
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    // Visits a shorthand property assignment that might reference an imported or exported symbol.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitShorthandPropertyAssignment(node *ast.ShorthandPropertyAssignment) *ast.Node</c>。</summary>
    private Node? VisitShorthandPropertyAssignment(ShorthandPropertyAssignment node)
    {
        var name = node.Name()!;
        var exportedOrImportedName = VisitExpressionIdentifier(name)!;
        if (!ReferenceEquals(exportedOrImportedName, name))
        {
            // A shorthand property with an assignment initializer is probably part of a
            // destructuring assignment
            var expression = exportedOrImportedName;
            if (node.ObjectAssignmentInitializer is not null)
            {
                expression = Factory().NewAssignmentExpression(
                    expression,
                    Visitor().VisitNode(node.ObjectAssignmentInitializer)!);
            }
            var assignment = Factory().NewPropertyAssignment(null /*modifiers*/, name, null /*postfixToken*/, null /*typeNode*/, expression);
            assignment.Loc = node.Loc;
            EmitContext().AssignCommentAndSourceMapRanges(assignment, node.AsNode());
            return assignment;
        }
        return Factory().UpdateShorthandPropertyAssignment(
            node,
            null, /*modifiers*/
            exportedOrImportedName,
            null, /*postfixToken*/
            null, /*typeNode*/
            node.EqualsToken,
            Visitor().VisitNode(node.ObjectAssignmentInitializer));
    }

    // Visits an identifier that, if it is in an expression position, might reference an imported or exported symbol.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitIdentifier(node *ast.IdentifierNode) *ast.Node</c>。</summary>
    private Node? VisitIdentifier(Node node)
    {
        if (IsIdentifierReference(node, _parentNode!))
        {
            return VisitExpressionIdentifier(node);
        }
        return node;
    }

    // Visits an identifier in an expression position that might reference an imported or exported symbol.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitExpressionIdentifier(node *ast.IdentifierNode) *ast.Node</c>。</summary>
    private Node? VisitExpressionIdentifier(Node node)
    {
        var info = EmitContext().GetAutoGenerateInfo(node);
        if (!(info is not null && !info.Flags.HasAllowNameSubstitution()) &&
            !IsHelperName(EmitContext(), node) &&
            !IsLocalName(EmitContext(), node) &&
            !ModuleTransforms.IsDeclarationNameOfEnumOrNamespace(EmitContext(), node))
        {
            var exportContainer = _resolver!.GetReferencedExportContainer(EmitContext().MostOriginal(node)!, IsExportName(EmitContext(), node));
            if (exportContainer is not null && IsSourceFile(exportContainer))
            {
                var reference = Factory().NewPropertyAccessExpression(
                    Factory().NewIdentifier("exports"),
                    null, /*questionDotToken*/
                    node.Clone(Factory())!,
                    NodeFlags.None);
                EmitContext().AssignCommentAndSourceMapRanges(reference, node);
                reference.Loc = node.Loc;
                return reference;
            }

            var importDeclaration = _resolver.GetReferencedImportDeclaration(EmitContext().MostOriginal(node)!);
            if (importDeclaration is not null)
            {
                if (IsImportClause(importDeclaration))
                {
                    var reference = Factory().NewPropertyAccessExpression(
                        Factory().NewGeneratedNameForNode(importDeclaration.Parent!), // Go: Resolver 返回解析树声明；用 Parent 找到所属 import 声明
                        null, /*questionDotToken*/
                        Factory().NewIdentifier("default"),
                        NodeFlags.None);
                    EmitContext().AssignCommentAndSourceMapRanges(reference, node);
                    reference.Loc = node.Loc;
                    return reference;
                }
                if (IsImportSpecifier(importDeclaration))
                {
                    var name = importDeclaration.AsImportSpecifier()!.PropertyNameOrName()!;
                    var decl = FindAncestor(importDeclaration, IsImportDeclaration);
                    var target = Factory().NewGeneratedNameForNode(Coalesce(decl, importDeclaration));
                    Node reference;
                    if (IsStringLiteral(name))
                    {
                        reference = Factory().NewElementAccessExpression(
                            target,
                            null, /*questionDotToken*/
                            Factory().NewStringLiteralFromNode(name),
                            NodeFlags.None);
                    }
                    else
                    {
                        var referenceName = name.Clone(Factory())!;
                        EmitContext().AddEmitFlags(referenceName, EmitFlags.NoSourceMap | EmitFlags.NoComments);
                        reference = Factory().NewPropertyAccessExpression(
                            target,
                            null, /*questionDotToken*/
                            referenceName,
                            NodeFlags.None);
                    }
                    EmitContext().AssignCommentAndSourceMapRanges(reference, node);
                    reference.Loc = node.Loc;
                    return reference;
                }
            }
        }
        return node;
    }

    // Gets the exported names of an identifier, if it is exported.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) getExports(name *ast.IdentifierNode) []*ast.ModuleExportName</c>。</summary>
    private List<Node> GetExports(Node name)
    {
        if (!IsGeneratedIdentifier(EmitContext(), name))
        {
            var importDeclaration = _resolver!.GetReferencedImportDeclaration(EmitContext().MostOriginal(name)!);
            if (importDeclaration is not null)
            {
                return _currentModuleInfo!._exportedBindings.Get(importDeclaration) ?? [];
            }

            // An exported namespace or enum may merge with an ambient declaration, which won't show up in .js emit, so
            // we analyze all value exports of a symbol.
            var bindingsSet = new Set<Node>();
            var bindings = new List<Node>();
            var declarations = _resolver.GetReferencedValueDeclarations(EmitContext().MostOriginal(name)!);
            if (declarations is not null)
            {
                foreach (var declaration in declarations)
                {
                    var exportedBindings = _currentModuleInfo!._exportedBindings.Get(declaration) ?? [];
                    foreach (var binding in exportedBindings)
                    {
                        if (!bindingsSet.Has(binding))
                        {
                            bindingsSet.Add(binding);
                            bindings.Add(binding);
                        }
                    }
                }
                return bindings;
            }
        }
        else if (ModuleTransforms.IsFileLevelReservedGeneratedIdentifier(EmitContext(), name))
        {
            var exportSpecifiers = _currentModuleInfo!._exportSpecifiers.Get(name.Text());
            if (exportSpecifiers is not null)
            {
                var exportedNames = new List<Node>();
                foreach (var exportSpecifier in exportSpecifiers)
                {
                    exportedNames.Add(exportSpecifier.Name()!);
                }
                return exportedNames;
            }
        }
        return [];
    }
}

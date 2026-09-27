// ported from: internal/transformers/estransforms/using.go:1-800（using / await using 声明降级：
//              顶层 try/catch/finally 展开 + export 提升、块级使用、for/for-of 头部浅变换；
//              含文件末尾的包级辅助函数 using.go:733-800）
//
// 与 Go 的差异（C# 语言特性，逐条）：
// 1) Go 的类型别名（`VariableDeclarationNode`/`Statement`/`Expression`/`ExportSpecifierNode` … 都是 `Node` 的别名，
//    docs/ast.md §2）在 C# 里不存在别名 → 一律用 `Node`；Go 里"工厂方法返回具体类型"（如
//    `NewVariableDeclaration` 返回 `*VariableDeclaration`）在 C# 生成物里一律返回 `Node`，
//    因此需要具体类型处显式转换（本文件仅 `ExportSpecifier`/`Identifier` 两处需要，已逐处注明）。
// 2) Go 的 `var expressions []*ast.T` + `append` → `List<Node>` + `ToArray()`（docs/architecture.md §3.2）。
// 3) Go 的切片视图 `rest[:pos]` / `rest[pos:]` → C# 数组必须切片拷贝（`rest[..pos]` / `rest[pos..]`），
//    这是 C# 数组模型的固有限制（Go 的切片是零拷贝视图）。
// 4) `visit` 里 Go 先给参数 `node` 重新赋值再统一 `return node`；C# 的参数不可重赋值，改为每个分支直接 return（语义相同）。
// 5) Go 的 `panic("...")` → `Debug.Fail("...")`（docs/architecture.md §3.2）。
// 6) 形参名 `async` 是 C# 关键字 → `isAsync`。
// 7) 包级函数（Go 无接收者）→ 与包同名的 `public static partial class EsTransforms`（docs/architecture.md §2）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.EsTransforms;

/// <summary>Go: <c>type usingKind uint</c>（using.go:23-29）。</summary>
internal enum UsingKind
{
    /// <summary>Go: <c>usingKindNone</c>。</summary>
    None = 0,
    /// <summary>Go: <c>usingKindSync</c>。</summary>
    Sync = 1,
    /// <summary>Go: <c>usingKindAsync</c>。</summary>
    Async = 2,
}

/// <summary>Go: <c>type usingDeclarationTransformer struct</c>（using.go:10-21）。</summary>
internal sealed class UsingDeclarationTransformer : Transformer
{
    private Dictionary<string, ExportSpecifier>? _exportBindings;
    private List<string> _exportBindingNames = [];
    private List<Node> _exportVars = [];
    private Identifier? _defaultExportBinding;
    private Identifier? _exportEqualsBinding;

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) visit</c>（using.go:31-45；见文件头差异 4）。工厂方法用方法组传入 → internal。</summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsUsing) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.SourceFile:
                return VisitSourceFile(node.AsSourceFile()!);
            case Kind.Block:
                return VisitBlock(node.AsBlock()!);
            case Kind.ForStatement:
                return VisitForStatement(node.AsForStatement()!);
            case Kind.ForOfStatement:
                return VisitForOfStatement(node.AsForInOrOfStatement()!);
            default:
                return Visitor().VisitEachChild(node);
        }
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) visitSourceFile</c>（using.go:47-166）。</summary>
    private Node? VisitSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        Node? visited;
        var usingKind = GetUsingKindOfStatements(node.Statements!.Nodes ?? []);
        if (usingKind != UsingKind.None)
        {
            // Imports and exports must stay at the top level. This means we must hoist all imports, exports, and
            // top-level function declarations and bindings out of the `try` statements we generate. For example:
            //
            // given:
            //
            //  import { w } from "mod";
            //  const x = expr1;
            //  using y = expr2;
            //  const z = expr3;
            //  export function f() {
            //    console.log(z);
            //  }
            //
            // produces:
            //
            //  import { x } from "mod";        // <-- preserved
            //  const x = expr1;                // <-- preserved
            //  var y, z;                       // <-- hoisted
            //  export function f() {           // <-- hoisted
            //    console.log(z);
            //  }
            //  const env_1 = { stack: [], error: void 0, hasError: false };
            //  try {
            //    y = __addDisposableResource(env_1, expr2, false);
            //    z = expr3;
            //  }
            //  catch (e_1) {
            //    env_1.error = e_1;
            //    env_1.hasError = true;
            //  }
            //  finally {
            //    __disposeResource(env_1);
            //  }
            //
            // In this transformation, we hoist `y`, `z`, and `f` to a new outer statement list while moving all other
            // statements in the source file into the `try` block, which is the same approach we use for System module
            // emit. Unlike System module emit, we attempt to preserve all statements prior to the first top-level
            // `using` to isolate the complexity of the transformed output to only where it is necessary.
            EmitContext().StartVariableEnvironment();

            _exportBindings = [];
            _exportVars = [];

            var (prologue, rest) = Factory().SplitStandardPrologue(node.Statements!.Nodes ?? []);
            var topLevelStatements = new List<Node>();
            topLevelStatements.AddRange(Visitor().VisitSlice(prologue).Result ?? []);

            // Collect and transform any leading statements up to the first `using` or `await using`. This preserves
            // the original statement order much as is possible.

            var pos = 0;
            while (pos < rest!.Length)
            {
                var statement = rest[pos];
                if (GetUsingKind(statement) != UsingKind.None)
                {
                    if (pos > 0)
                    {
                        topLevelStatements.AddRange(Visitor().VisitSlice(rest[..pos]).Result ?? []);
                    }
                    break;
                }
                pos++;
            }

            if (pos >= rest.Length)
            {
                Debug.Fail("Should have encountered at least one 'using' statement.");
            }

            // transform the rest of the body
            var envBinding = CreateEnvBinding();
            var bodyStatements = TransformUsingDeclarations(rest[pos..], envBinding, topLevelStatements);

            // add `export {}` declarations for any hoisted bindings.
            if (_exportBindings!.Count > 0)
            {
                var exportSpecifiers = new Node[_exportBindingNames.Count];
                for (var i = 0; i < _exportBindingNames.Count; i++)
                {
                    var name = _exportBindingNames[i];
                    var specifier = _exportBindings[name];
                    Debug.Assert(specifier is not null, "Missing export binding for hoisted export name");
                    exportSpecifiers[i] = specifier!;
                }
                topLevelStatements.Add(
                    Factory().NewExportDeclaration(
                        null,   /*modifiers*/
                        false,  /*isTypeOnly*/
                        Factory().NewNamedExports(
                            Factory().NewNodeList(exportSpecifiers)
                        ),
                        null,   /*moduleSpecifier*/
                        null    /*attributes*/
                    )
                );
            }

            topLevelStatements.AddRange(EmitContext().EndVariableEnvironment());
            if (_exportVars.Count > 0)
            {
                topLevelStatements.Add(Factory().NewVariableStatement(
                    Factory().NewModifierList([
                        Factory().NewModifier(Kind.ExportKeyword),
                    ]),
                    Factory().NewVariableDeclarationList(
                        Factory().NewNodeList(_exportVars.ToArray()),
                        NodeFlags.Let
                    )
                ));
            }
            topLevelStatements.AddRange(CreateDownlevelUsingStatements(bodyStatements, envBinding, usingKind == UsingKind.Async));

            if (_exportEqualsBinding is not null)
            {
                topLevelStatements.Add(Factory().NewExportAssignment(
                    null,  /*modifiers*/
                    true,  /*isExportEquals*/
                    null,  /*typeNode*/
                    _exportEqualsBinding
                ));
            }

            visited = Factory().UpdateSourceFile(node, Factory().NewNodeList(topLevelStatements.ToArray()), node.EndOfFileToken);
        }
        else
        {
            visited = Visitor().VisitEachChild(node);
        }
        EmitContext().AddEmitHelper(visited!, EmitContext().ReadEmitHelpers());
        _exportVars = [];
        _exportBindings = null;
        _exportBindingNames = [];
        _defaultExportBinding = null;
        _exportEqualsBinding = null;
        return visited;
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) visitBlock</c>（using.go:168-186）。</summary>
    private Node? VisitBlock(Block node)
    {
        var usingKind = GetUsingKindOfStatements(node.Statements!.Nodes ?? []);
        if (usingKind != UsingKind.None)
        {
            var (prologue, rest) = Factory().SplitStandardPrologue(node.Statements!.Nodes ?? []);
            var envBinding = CreateEnvBinding();
            var statements = new List<Node>(prologue.Length + 2);
            statements.AddRange(Visitor().VisitSlice(prologue).Result ?? []);
            statements.AddRange(CreateDownlevelUsingStatements(
                TransformUsingDeclarations(rest!, envBinding, null /*topLevelStatements*/),
                envBinding,
                usingKind == UsingKind.Async
            ));
            var statementList = Factory().NewNodeList(statements.ToArray());
            statementList.Loc = node.Statements!.Loc;
            return Factory().UpdateBlock(node, statementList, node.MultiLine);
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) visitForStatement</c>（using.go:188-212）。</summary>
    private Node? VisitForStatement(ForStatement node)
    {
        if (node.Initializer is not null && IsUsingVariableDeclarationList(node.Initializer))
        {
            // given:
            //
            //  for (using x = expr; cond; incr) { ... }
            //
            // produces a shallow transformation to:
            //
            //  {
            //    using x = expr;
            //    for (; cond; incr) { ... }
            //  }
            //
            // before handing the shallow transformation back to the visitor for an in-depth transformation.
            return Visitor().VisitNode(
                Factory().NewBlock(Factory().NewNodeList([
                    Factory().NewVariableStatement(null /*modifiers*/, node.Initializer),
                    Factory().UpdateForStatement(
                        node,
                        null, /*initializer*/
                        node.Condition,
                        node.Incrementor,
                        node.Statement
                    ),
                ]), false /*multiLine*/)
            );
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) visitForOfStatement</c>（using.go:214-268）。</summary>
    private Node? VisitForOfStatement(ForInOrOfStatement node)
    {
        if (IsUsingVariableDeclarationList(node.Initializer!))
        {
            // given:
            //
            //  for (using x of y) { ... }
            //
            // produces a shallow transformation to:
            //
            //  for (const x_1 of y) {
            //    using x = x;
            //    ...
            //  }
            //
            // before handing the shallow transformation back to the visitor for an in-depth transformation.
            var forInitializer = node.Initializer!.AsVariableDeclarationList()!;
            Node? forDecl = FirstOrNil(forInitializer.Declarations!.Nodes);
            if (forDecl is null)
            {
                forDecl = Factory().NewVariableDeclaration(Factory().NewTempVariable(), null, null, null);
            }

            var isAwaitUsing = GetUsingKindOfVariableDeclarationList(forInitializer) == UsingKind.Async;
            var temp = Factory().NewGeneratedNameForNode(forDecl.Name());
            var usingVar = Factory().UpdateVariableDeclaration(forDecl.AsVariableDeclaration()!, forDecl.Name(), null /*exclamationToken*/, null /*type*/, temp);
            var usingVarList = Factory().NewVariableDeclarationList(
                Factory().NewNodeList([usingVar]),
                IfElse(isAwaitUsing, NodeFlags.AwaitUsing, NodeFlags.Using)
            );
            var usingVarStatement = Factory().NewVariableStatement(null /*modifiers*/, usingVarList);
            Node statement;
            if (IsBlock(node.Statement!))
            {
                var statements = new List<Node>((node.Statement!.AsBlock()!.Statements!.Nodes?.Length ?? 0) + 1)
                {
                    usingVarStatement,
                };
                statements.AddRange(node.Statement.AsBlock()!.Statements!.Nodes ?? []);
                statement = Factory().UpdateBlock(
                    node.Statement.AsBlock()!,
                    Factory().NewNodeList(statements.ToArray()),
                    node.Statement.AsBlock()!.MultiLine
                );
            }
            else
            {
                statement = Factory().NewBlock(
                    Factory().NewNodeList([
                        usingVarStatement,
                        node.Statement!,
                    ]),
                    true /*multiLine*/
                );
            }
            return Visitor().VisitNode(
                Factory().UpdateForInOrOfStatement(
                    node,
                    node.AwaitModifier,
                    Factory().NewVariableDeclarationList(
                        Factory().NewNodeList([
                            Factory().NewVariableDeclaration(temp, null /*exclamationToken*/, null /*type*/, null),
                        ]),
                        NodeFlags.Const
                    ),
                    node.Expression,
                    statement
                )
            );
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) transformUsingDeclarations</c>（using.go:270-352）。</summary>
    private Node[] TransformUsingDeclarations(Node[] statementsIn, Identifier envBinding, List<Node>? topLevelStatements)
    {
        var statements = new List<Node>();

        Node? Hoist(Node hoistNode)
        {
            if (topLevelStatements is null)
            {
                return hoistNode;
            }

            switch (hoistNode.Kind)
            {
                case Kind.ImportDeclaration:
                case Kind.ImportEqualsDeclaration:
                case Kind.ExportDeclaration:
                case Kind.FunctionDeclaration:
                    HoistImportOrExportOrHoistedDeclaration(hoistNode, topLevelStatements);
                    return null;
                case Kind.ExportAssignment:
                    return HoistExportAssignment(hoistNode.AsExportAssignment()!);
                case Kind.ClassDeclaration:
                    return HoistClassDeclaration(hoistNode.AsClassDeclaration()!);
                case Kind.VariableStatement:
                    return HoistVariableStatement(hoistNode.AsVariableStatement()!);
            }

            return hoistNode;
        }

        void HoistOrAppendNode(Node appendNode)
        {
            var hoisted = Hoist(appendNode);
            if (hoisted is not null)
            {
                statements.Add(hoisted);
            }
        }

        foreach (var statement in statementsIn)
        {
            var usingKind = GetUsingKind(statement);
            if (usingKind != UsingKind.None)
            {
                var varStatement = statement.AsVariableStatement()!;
                var declarationList = varStatement.DeclarationList!;
                List<Node>? declarations = [];
                foreach (var declaration in declarationList.AsVariableDeclarationList()!.Declarations!.Nodes ?? [])
                {
                    if (!IsIdentifier(declaration.Name()!))
                    {
                        // Since binding patterns are a grammar error, we reset `declarations` so we don't process this as a `using`.
                        declarations = null;
                        break;
                    }

                    var decl = declaration;
                    // perform a shallow transform for any named evaluation
                    if (IsNamedEvaluation(EmitContext(), declaration))
                    {
                        decl = TransformNamedEvaluation(EmitContext(), declaration, false /*ignoreEmptyStringLiteral*/, "" /*assignedName*/);
                    }

                    var initializer = Visitor().VisitNode(decl.Initializer());
                    initializer ??= Factory().NewVoidZeroExpression();
                    declarations.Add(Factory().UpdateVariableDeclaration(
                        decl.AsVariableDeclaration()!,
                        decl.Name(),
                        null, /*exclamationToken*/
                        null, /*type*/
                        Factory().NewAddDisposableResourceHelper(
                            envBinding,
                            initializer,
                            usingKind == UsingKind.Async
                        )
                    ));
                }

                // Only replace the statement if it was valid.
                if (declarations is { Count: > 0 })
                {
                    var varList = Factory().NewVariableDeclarationList(Factory().NewNodeList(declarations.ToArray()), NodeFlags.Const);
                    EmitContext().SetOriginal(varList, declarationList);
                    varList.Loc = declarationList.Loc;
                    HoistOrAppendNode(Factory().UpdateVariableStatement(varStatement, null /*modifiers*/, varList));
                    continue;
                }
            }

            var result = Visit(statement);
            if (result is not null)
            {
                if (result.Kind == Kind.SyntaxList)
                {
                    foreach (var child in result.AsSyntaxList()!.Children ?? [])
                    {
                        HoistOrAppendNode(child);
                    }
                }
                else
                {
                    HoistOrAppendNode(result);
                }
            }
        }
        return statements.ToArray();
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) hoistImportOrExportOrHoistedDeclaration</c>（using.go:354-357）。</summary>
    private void HoistImportOrExportOrHoistedDeclaration(Node node, List<Node> topLevelStatements)
    {
        // NOTE: `node` has already been visited
        topLevelStatements.Add(node);
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) hoistExportAssignment</c>（using.go:359-365）。</summary>
    private Node? HoistExportAssignment(ExportAssignment node)
    {
        if (node.IsExportEquals)
        {
            return HoistExportEquals(node);
        }
        else
        {
            return HoistExportDefault(node);
        }
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) hoistExportDefault</c>（using.go:367-406）。</summary>
    private Node? HoistExportDefault(ExportAssignment node)
    {
        // NOTE: `node` has already been visited
        if (_defaultExportBinding is not null)
        {
            // invalid case of multiple `export default` declarations. Don't assert here, just pass it through
            return node;
        }

        // given:
        //
        //   export default expr;
        //
        // produces:
        //
        //   // top level
        //   var default_1;
        //   export { default_1 as default };
        //
        //   // body
        //   default_1 = expr;

        _defaultExportBinding = (Identifier)Factory().NewUniqueNameEx("_default", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.ReservedInNestedScopes | GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.Optimistic });
        HoistBindingIdentifier(_defaultExportBinding /*isExport*/, true, (Identifier)Factory().NewIdentifier("default"), node);

        // give a class or function expression an assigned name, if needed.
        var expression = node.Expression!;
        var innerExpression = SkipOuterExpressions(expression, OuterExpressionKinds.All);
        if (IsNamedEvaluation(EmitContext(), innerExpression!))
        {
            innerExpression = TransformNamedEvaluation(EmitContext(), innerExpression! /*ignoreEmptyStringLiteral*/, false, "default");
            expression = Factory().RestoreOuterExpressions(expression, innerExpression, OuterExpressionKinds.All);
        }

        var assignment = Factory().NewAssignmentExpression(_defaultExportBinding, expression);
        return Factory().NewExpressionStatement(assignment);
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) hoistExportEquals</c>（using.go:408-436）。</summary>
    private Node? HoistExportEquals(ExportAssignment node)
    {
        // NOTE: `node` has already been visited
        if (_exportEqualsBinding is not null)
        {
            // invalid case of multiple `export default` declarations. Don't assert here, just pass it through
            return node;
        }

        // given:
        //
        //   export = expr;
        //
        // produces:
        //
        //   // top level
        //   var default_1;
        //
        //   try {
        //       // body
        //       default_1 = expr;
        //   } ...
        //
        //   // top level suffix
        //   export = default_1;

        _exportEqualsBinding = (Identifier)Factory().NewUniqueNameEx("_default", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.ReservedInNestedScopes | GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.Optimistic });
        EmitContext().AddVariableDeclaration(_exportEqualsBinding);

        // give a class or function expression an assigned name, if needed.
        var assignment = Factory().NewAssignmentExpression(_exportEqualsBinding, node.Expression!);
        return Factory().NewExpressionStatement(assignment);
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) hoistClassDeclaration</c>（using.go:438-517）。</summary>
    private Node? HoistClassDeclaration(ClassDeclaration node)
    {
        // NOTE: `node` has already been visited
        if (node.Name() is null && _defaultExportBinding is not null)
        {
            // invalid case of multiple `export default` declarations. Don't assert here, just pass it through
            return node;
        }

        var isExported = HasSyntacticModifier(node, ModifierFlags.Export);
        var isDefault = HasSyntacticModifier(node, ModifierFlags.Default);

        // When hoisting a class declaration at the top level of a file containing a top-level `using` statement, we
        // must first convert it to a class expression so that we can hoist the binding outside of the `try`.
        var expression = ConvertClassDeclarationToClassExpression(EmitContext(), node);
        if (node.Name() is not null)
        {
            // given:
            //
            //  using x = expr;
            //  class C {}
            //
            // produces:
            //
            //  var x, C;
            //  const env_1 = { ... };
            //  try {
            //    x = __addDisposableResource(env_1, expr, false);
            //    C = class {};
            //  }
            //  catch (e_1) {
            //    env_1.error = e_1;
            //    env_1.hasError = true;
            //  }
            //  finally {
            //    __disposeResources(env_1);
            //  }
            //
            // If the class is exported, we also produce an `export { C };`
            HoistBindingIdentifier((Identifier)Factory().GetLocalName(node), isExported && !isDefault, null /*exportAlias*/, node);
            expression = Factory().NewAssignmentExpression(Factory().GetDeclarationName(node), expression);
            EmitContext().SetOriginal(expression, node);
            EmitContext().SetSourceMapRange(expression, node.Loc);
            EmitContext().SetCommentRange(expression, node.Loc);
            if (IsNamedEvaluation(EmitContext(), expression))
            {
                expression = TransformNamedEvaluation(EmitContext(), expression, false /*ignoreEmptyStringLiteral*/, "" /*assignedName*/);
            }
        }

        if (isDefault && _defaultExportBinding is null)
        {
            // In the case of a default export, we create a temporary variable that we export as the default and then
            // assign to that variable.
            //
            // given:
            //
            //  using x = expr;
            //  export default class C {}
            //
            // produces:
            //
            //  export { default_1 as default };
            //  var x, C, default_1;
            //  const env_1 = { ... };
            //  try {
            //    x = __addDisposableResource(env_1, expr, false);
            //    default_1 = C = class {};
            //  }
            //  catch (e_1) {
            //    env_1.error = e_1;
            //    env_1.hasError = true;
            //  }
            //  finally {
            //    __disposeResources(env_1);
            //  }
            //
            // Though we will never reassign `default_1`, this most closely matches the specified runtime semantics.
            _defaultExportBinding = (Identifier)Factory().NewUniqueNameEx("_default", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.ReservedInNestedScopes | GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.Optimistic });
            HoistBindingIdentifier(_defaultExportBinding /*isExport*/, true, (Identifier)Factory().NewIdentifier("default"), node);
            expression = Factory().NewAssignmentExpression(_defaultExportBinding, expression);
            EmitContext().SetOriginal(expression, node);
            if (IsNamedEvaluation(EmitContext(), expression))
            {
                expression = TransformNamedEvaluation(EmitContext(), expression /*ignoreEmptyStringLiteral*/, false, "default");
            }
        }

        return Factory().NewExpressionStatement(expression);
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) hoistVariableStatement</c>（using.go:519-539）。</summary>
    private Node? HoistVariableStatement(VariableStatement node)
    {
        // NOTE: `node` has already been visited
        var expressions = new List<Node>();
        var isExported = HasSyntacticModifier(node, ModifierFlags.Export);
        foreach (var variable in node.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes ?? [])
        {
            HoistBindingElement(variable, isExported, variable);
            if (variable.Initializer() is not null)
            {
                expressions.Add(HoistInitializedVariable(variable.AsVariableDeclaration()!)!);
            }
        }
        if (expressions.Count > 0)
        {
            var statement = Factory().NewExpressionStatement(Factory().InlineExpressions(expressions.ToArray()));
            EmitContext().SetOriginal(statement, node);
            EmitContext().SetCommentRange(statement, node.Loc);
            EmitContext().SetSourceMapRange(statement, node.Loc);
            return statement;
        }
        return null;
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) hoistInitializedVariable</c>（using.go:541-560）。</summary>
    private Node? HoistInitializedVariable(VariableDeclaration node)
    {
        // NOTE: `node` has already been visited
        if (node.Initializer is null)
        {
            Debug.Fail("Expected initializer");
        }
        Node target;
        if (IsIdentifier(node.Name()!))
        {
            target = node.Name()!.Clone(Factory())!;
            EmitContext().SetEmitFlags(target, EmitContext().EmitFlags(target) & ~(EmitFlags.LocalName | EmitFlags.ExportName));
        }
        else
        {
            target = ConvertBindingPatternToAssignmentPattern(EmitContext(), node.Name()!.AsBindingPattern()!)!;
        }

        var assignment = Factory().NewAssignmentExpression(target, node.Initializer!);
        EmitContext().SetOriginal(assignment, node);
        EmitContext().SetCommentRange(assignment, node.Loc);
        EmitContext().SetSourceMapRange(assignment, node.Loc);
        return assignment;
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) hoistBindingElement</c>（using.go:562-573）。</summary>
    private void HoistBindingElement(Node node /*VariableDeclaration|BindingElement*/, bool isExportedDeclaration, Node? original)
    {
        // NOTE: `node` has already been visited
        if (IsBindingPattern(node.Name()!))
        {
            foreach (var element in node.Name()!.AsBindingPattern()!.Elements!.Nodes ?? [])
            {
                if (element.Name() is not null)
                {
                    HoistBindingElement(element, isExportedDeclaration, original);
                }
            }
        }
        else
        {
            HoistBindingIdentifier((Identifier)node.Name()!, isExportedDeclaration, null /*exportAlias*/, original);
        }
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) hoistBindingIdentifier</c>（using.go:575-612）。</summary>
    private void HoistBindingIdentifier(Identifier node, bool isExport, Identifier? exportAlias, Node? original)
    {
        // NOTE: `node` has already been visited
        var name = node;
        if (!IsGeneratedIdentifier(EmitContext(), node))
        {
            name = (Identifier)node.Clone(Factory())!;
        }
        if (isExport)
        {
            if (exportAlias is null && !IsLocalName(EmitContext(), name))
            {
                var varDecl = Factory().NewVariableDeclaration(name, null /*exclamationToken*/, null /*type*/, null /*initializer*/);
                if (original is not null)
                {
                    EmitContext().SetOriginal(varDecl, original);
                }
                _exportVars.Add(varDecl);
                return;
            }

            Node? localName = null;
            Node? exportName;
            if (exportAlias is not null)
            {
                localName = name;
                exportName = exportAlias;
            }
            else
            {
                exportName = name;
            }
            // Go 的 NewExportSpecifier 返回 *ExportSpecifier；C# 生成物返回 Node（见文件头差异 1）。
            var specifier = (ExportSpecifier)Factory().NewExportSpecifier(/*isTypeOnly*/ false, localName, exportName);
            if (original is not null)
            {
                EmitContext().SetOriginal(specifier, original);
            }
            _exportBindings ??= [];
            if (!_exportBindings.ContainsKey(name.Text))
            {
                _exportBindingNames.Add(name.Text);
            }
            _exportBindings[name.Text] = specifier;
        }
        EmitContext().AddVariableDeclaration(name);
    }

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) createEnvBinding</c>（using.go:614-616）。</summary>
    private Identifier CreateEnvBinding() => (Identifier)Factory().NewUniqueName("env");

    /// <summary>Go: <c>func (tx *usingDeclarationTransformer) createDownlevelUsingStatements</c>（using.go:618-731）。</summary>
    private Node[] CreateDownlevelUsingStatements(Node[] bodyStatements, Identifier envBinding, bool isAsync)
    {
        var statements = new List<Node>(2);

        // produces:
        //
        //  const env_1 = { stack: [], error: void 0, hasError: false };
        //
        var envObject = Factory().NewObjectLiteralExpression(Factory().NewNodeList([
            Factory().NewPropertyAssignment(null /*modifiers*/, Factory().NewIdentifier("stack"), null /*postfixToken*/, null /*typeNode*/, Factory().NewArrayLiteralExpression(null, false /*multiLine*/)),
            Factory().NewPropertyAssignment(null /*modifiers*/, Factory().NewIdentifier("error"), null /*postfixToken*/, null /*typeNode*/, Factory().NewVoidZeroExpression()),
            Factory().NewPropertyAssignment(null /*modifiers*/, Factory().NewIdentifier("hasError"), null /*postfixToken*/, null /*typeNode*/, Factory().NewFalseExpression()),
        ]), false /*multiLine*/);
        var envVar = Factory().NewVariableDeclaration(envBinding, null /*exclamationToken*/, null /*typeNode*/, envObject);
        var envVarList = Factory().NewVariableDeclarationList(Factory().NewNodeList([envVar]), NodeFlags.Const);
        var envVarStatement = Factory().NewVariableStatement(null /*modifiers*/, envVarList);
        statements.Add(envVarStatement);

        // when `async` is `false`, produces:
        //
        //  try {
        //    <bodyStatements>
        //  }
        //  catch (e_1) {
        //      env_1.error = e_1;
        //      env_1.hasError = true;
        //  }
        //  finally {
        //    __disposeResources(env_1);
        //  }

        // when `async` is `true`, produces:
        //
        //  try {
        //    <bodyStatements>
        //  }
        //  catch (e_1) {
        //      env_1.error = e_1;
        //      env_1.hasError = true;
        //  }
        //  finally {
        //    const result_1 = __disposeResources(env_1);
        //    if (result_1) {
        //      await result_1;
        //    }
        //  }

        // Unfortunately, it is necessary to use two properties to indicate an error because `throw undefined` is legal
        // JavaScript.
        var tryBlock = Factory().NewBlock(Factory().NewNodeList(bodyStatements), true /*multiLine*/);
        var bodyCatchBinding = Factory().NewUniqueName("e");
        var catchClause = Factory().NewCatchClause(
            Factory().NewVariableDeclaration(
                bodyCatchBinding,
                null, /*exclamationToken*/
                null, /*type*/
                null  /*initializer*/
            ),
            Factory().NewBlock(Factory().NewNodeList([
                Factory().NewExpressionStatement(
                    Factory().NewAssignmentExpression(
                        Factory().NewPropertyAccessExpression(envBinding, null, Factory().NewIdentifier("error"), NodeFlags.None),
                        bodyCatchBinding
                    )
                ),
                Factory().NewExpressionStatement(
                    Factory().NewAssignmentExpression(
                        Factory().NewPropertyAccessExpression(envBinding, null, Factory().NewIdentifier("hasError"), NodeFlags.None),
                        Factory().NewTrueExpression()
                    )
                ),
            ]), true /*multiLine*/)
        );

        Node finallyBlock;
        if (isAsync)
        {
            var result = Factory().NewUniqueName("result");
            finallyBlock = Factory().NewBlock(Factory().NewNodeList([
                Factory().NewVariableStatement(
                    null, /*modifiers*/
                    Factory().NewVariableDeclarationList(Factory().NewNodeList([
                        Factory().NewVariableDeclaration(
                            result,
                            null, /*exclamationToken*/
                            null, /*type*/
                            Factory().NewDisposeResourcesHelper(envBinding)
                        ),
                    ]), NodeFlags.Const)
                ),
                Factory().NewIfStatement(result, Factory().NewExpressionStatement(Factory().NewAwaitExpression(result)), null /*elseStatement*/),
            ]), true /*multiLine*/);
        }
        else
        {
            finallyBlock = Factory().NewBlock(Factory().NewNodeList([
                Factory().NewExpressionStatement(
                    Factory().NewDisposeResourcesHelper(envBinding)
                ),
            ]), true /*multiLine*/);
        }

        var tryStatement = Factory().NewTryStatement(tryBlock, catchClause, finallyBlock);
        statements.Add(tryStatement);
        return statements.ToArray();
    }
}

/// <summary>Go: 包级函数/类型构造（`estransforms` 包）。</summary>
public static partial class EsTransforms
{
    /// <summary>Go: <c>func newUsingDeclarationTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>（using.go:23 之前，using.go:12-21 的构造函数）。</summary>
    internal static Transformer? NewUsingDeclarationTransformer(TransformOptions opts)
    {
        var tx = new UsingDeclarationTransformer();
        return tx.NewTransformer(tx.Visit, opts.Context);
    }

    /// <summary>Go: <c>func isUsingVariableDeclarationList(node *ast.ForInitializer) bool</c>（using.go:733-735）。</summary>
    internal static bool IsUsingVariableDeclarationList(Node? node) =>
        IsVariableDeclarationList(node!) && GetUsingKindOfVariableDeclarationList(node!.AsVariableDeclarationList()!) != UsingKind.None;

    /// <summary>Go: <c>func getUsingKindOfVariableDeclarationList(node *ast.VariableDeclarationList) usingKind</c>（using.go:737-746）。</summary>
    internal static UsingKind GetUsingKindOfVariableDeclarationList(VariableDeclarationList node)
    {
        switch (node.Flags & NodeFlags.BlockScoped)
        {
            case NodeFlags.AwaitUsing:
                return UsingKind.Async;
            case NodeFlags.Using:
                return UsingKind.Sync;
            default:
                return UsingKind.None;
        }
    }

    /// <summary>Go: <c>func getUsingKindOfVariableStatement(node *ast.VariableStatement) usingKind</c>（using.go:748-750）。</summary>
    internal static UsingKind GetUsingKindOfVariableStatement(VariableStatement node) =>
        GetUsingKindOfVariableDeclarationList(node.DeclarationList!.AsVariableDeclarationList()!);

    /// <summary>Go: <c>func getUsingKind(statement *ast.Node) usingKind</c>（using.go:752-757）。</summary>
    internal static UsingKind GetUsingKind(Node statement)
    {
        if (IsVariableStatement(statement))
        {
            return GetUsingKindOfVariableStatement(statement.AsVariableStatement()!);
        }
        return UsingKind.None;
    }

    /// <summary>Go: <c>func getUsingKindOfStatements(statements []*ast.Node) usingKind</c>（using.go:759-771）。</summary>
    internal static UsingKind GetUsingKindOfStatements(Node[] statements)
    {
        var result = UsingKind.None;
        foreach (var statement in statements)
        {
            var usingKind = GetUsingKind(statement);
            if (usingKind == UsingKind.Async)
            {
                return UsingKind.Async;
            }
            if (usingKind > result)
            {
                result = usingKind;
            }
        }
        return result;
    }
}

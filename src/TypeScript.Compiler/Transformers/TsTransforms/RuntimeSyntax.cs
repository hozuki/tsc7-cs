// ported from: internal/transformers/tstransforms/runtimesyntax.go:1-995
//
// 与 Go 的差异（语言特性；其余见 docs/architecture.md §2/§3）：
// 1) Go 的嵌入 `transformers.Transformer` → 基类继承；`tx.visit` 方法组传给 `NewTransformer`。
// 2) `defer tx.popNode/popScope` → 嵌套的 `try/finally`（C# 没有 defer；注册顺序 = Go，LIFO 执行）。
//    注意 Go 里 pushNode/pushScope 在**早退分支之前**执行，所以 try 块包住整个函数体。
// 3) Go 的 `saveStatements() (T, U)` 具名返回值 → C# 元组 `(T, U)`。
// 4) Go 的 `append(slice, x...)` → C# 的 `List<Node>` + `AddRange`/`Add`（docs §3.2：Go 的切片没有
//    len<cap 视图，C# 用 List）；需要把列表交给工厂（NewNodeList/NewSyntaxList）时 `ToArray()`。
//    `addVarForDeclaration` / `transformEnumMember` / `transformConstructorBodyWorker` 在 Go 里靠
//    "返回新切片"表达追加，C# 里改成**就地修改传入的 List**（返回值/语义不变），
//    `addVarForDeclaration` 只保留 `bool` 返回值。
// 5) Go 的 `map[string]*ast.Node`（nil = 尚未记录）→ `Dictionary<string, Node>?`；
//    Go 的 map 读缺失键返回零值 → C# 用 `TryGetValue`（docs §5.4 已知坑）。
// 6) `printer.EFxxx` → `EmitFlags.Xxx`（常量名去类型前缀，docs §3.3）；`printer.NameOptions{...}` /
//    `printer.AssignedNameOptions{...}` → `NodeFactory.NameOptions` / `NodeFactory.AssignedNameOptions`
//    （Go 里是 printer 包的类型，C# 在 Printer/NodeFactory.cs 里是**嵌套**类型）。
// 7) `core.FirstResult(tx.Visitor().VisitSlice(x))` → `Visitor().VisitSlice(x).Result`
//    （Go 的 FirstResult 只是取多元返回的第一个；C# 用元组成员）。
// 8) `core.IfElse(a, b, c)` / `core.Find` / `core.Some` → `Core` 静态类的同名成员（`using static`）。
// 9) Go 的 `slices.Clone(prologue)` + `append` → `new List<Node>(prologue)`（List 自己管扩容）。
// 10) `getInnermostModuleDeclarationFromDottedModule` 是 Go 的包级函数 → 静态类 `TsTransforms`
//     （见本文件末尾的 partial；typeeraser 也用它）。
// 11) `transformers.FlattenDestructuringAssignment(this, …)`：Go 传 `&tx.Transformer`（嵌入的基类指针），
//     C# 传 `this`（继承即 is-a）。回调 `createNamespaceExportExpression` → 同名私有方法，
//     第三个参数 `location` 用 `TextRange?`（Go 的 `*core.TextRange`）。
// 12) `node.AsEnumDeclaration()` 等 As* 访问器在 C# 里返回可空，Go 的隐式假设非空 → 加 `!`。
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Transformers;
using static TypeScript.Transformers.TsTransforms.TsTransforms;
// Go: printer.NodeFactory（与 ast.NodeFactory 同名不同包 → 显式限定，避免 CS0104）。
using NodeFactory = TypeScript.Printer.NodeFactory;

namespace TypeScript.Transformers.TsTransforms;

/// <summary>Transforms TypeScript-specific runtime syntax into JavaScript-compatible syntax.</summary>
public sealed class RuntimeSyntaxTransformer : Transformer
{
    private readonly CompilerOptions _compilerOptions;
    private readonly ReferenceResolver? _resolver;
    private readonly EmitResolver? _emitResolver;

    private Node? _parentNode;
    private Node? _currentNode;
    private Node? _currentSourceFile;
    private Node? _currentScope; // SourceFile | Block | ModuleBlock | CaseBlock
    private Dictionary<string, Node>? _currentScopeFirstDeclarationsOfName;
    private Node? _currentEnum;      // Go: *ast.EnumDeclarationNode（= *Node）
    private Node? _currentNamespace; // Go: *ast.ModuleDeclarationNode（= *Node）

    private RuntimeSyntaxTransformer(CompilerOptions compilerOptions, ReferenceResolver? resolver, EmitResolver? emitResolver)
    {
        _compilerOptions = compilerOptions;
        _resolver = resolver;
        _emitResolver = emitResolver;
    }

    /// <summary>Go: <c>func NewRuntimeSyntaxTransformer(opt *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? NewRuntimeSyntaxTransformer(TransformOptions opt)
    {
        var compilerOptions = opt.CompilerOptions!;
        var emitContext = opt.Context;
        var tx = new RuntimeSyntaxTransformer(compilerOptions, opt.Resolver, opt.EmitResolver);
        return tx.NewTransformer(tx.Visit, emitContext);
    }

    /// <summary>
    /// Pushes a new child node onto the ancestor tracking stack, returning the grandparent node to be
    /// restored later via <see cref="PopNode"/>.
    /// </summary>
    private Node? PushNode(Node node)
    {
        var grandparentNode = _parentNode;
        _parentNode = _currentNode;
        _currentNode = node;
        return grandparentNode;
    }

    /// <summary>Pops the last child node off the ancestor tracking stack, restoring the grandparent node.</summary>
    private void PopNode(Node? grandparentNode)
    {
        _currentNode = _parentNode;
        _parentNode = grandparentNode;
    }

    private (Node? SavedCurrentScope, Dictionary<string, Node>? SavedCurrentScopeFirstDeclarationsOfName) PushScope(Node node)
    {
        var savedCurrentScope = _currentScope;
        var savedCurrentScopeFirstDeclarationsOfName = _currentScopeFirstDeclarationsOfName;
        switch (node.Kind)
        {
            case Kind.SourceFile:
                _currentScope = node;
                _currentSourceFile = node;
                _currentScopeFirstDeclarationsOfName = null;
                break;
            case Kind.CaseBlock:
            case Kind.ModuleBlock:
            case Kind.Block:
                _currentScope = node;
                _currentScopeFirstDeclarationsOfName = null;
                break;
            case Kind.FunctionDeclaration:
            case Kind.ClassDeclaration:
            case Kind.VariableStatement:
                RecordDeclarationInScope(node);
                break;
        }

        return (savedCurrentScope, savedCurrentScopeFirstDeclarationsOfName);
    }

    private void PopScope(Node? savedCurrentScope, Dictionary<string, Node>? savedCurrentScopeFirstDeclarationsOfName)
    {
        if (_currentScope != savedCurrentScope)
        {
            // only reset the first declaration for a name if we are exiting the scope in which it was declared
            _currentScopeFirstDeclarationsOfName = savedCurrentScopeFirstDeclarationsOfName;
        }

        _currentScope = savedCurrentScope;
    }

    /// <summary>Visits each node in the AST.</summary>
    private Node? Visit(Node node)
    {
        var grandparentNode = PushNode(node);
        try
        {
            var (savedCurrentScope, savedCurrentScopeFirstDeclarationsOfName) = PushScope(node);
            try
            {
                if ((node.SubtreeFacts & SubtreeFacts.ContainsTypeScript) == 0 && ((_currentNamespace is null && _currentEnum is null) || (node.SubtreeFacts & SubtreeFacts.ContainsIdentifier) == 0))
                {
                    return node;
                }

                Node? updated;
                switch (node.Kind)
                {
                    // TypeScript parameter property modifiers are elided
                    case Kind.PublicKeyword:
                    case Kind.PrivateKeyword:
                    case Kind.ProtectedKeyword:
                    case Kind.ReadonlyKeyword:
                    case Kind.OverrideKeyword:
                        updated = null;
                        break;
                    case Kind.EnumDeclaration:
                        updated = VisitEnumDeclaration(node.AsEnumDeclaration()!);
                        break;
                    case Kind.ModuleDeclaration:
                        updated = VisitModuleDeclaration(node.AsModuleDeclaration()!);
                        break;
                    case Kind.ClassDeclaration:
                        updated = VisitClassDeclaration(node.AsClassDeclaration()!);
                        break;
                    case Kind.ClassExpression:
                        updated = VisitClassExpression(node.AsClassExpression()!);
                        break;
                    case Kind.Constructor:
                        updated = VisitConstructorDeclaration(node.AsConstructorDeclaration()!);
                        break;
                    case Kind.FunctionDeclaration:
                        updated = VisitFunctionDeclaration(node.AsFunctionDeclaration()!);
                        break;
                    case Kind.VariableStatement:
                        updated = VisitVariableStatement(node.AsVariableStatement()!);
                        break;
                    case Kind.ExportDeclaration:
                    case Kind.ImportDeclaration:
                    case Kind.ImportClause:
                        if (_currentNamespace is not null && _currentScope is not null && _currentScope.Kind != Kind.Block)
                        {
                            // do not emit ES6 imports and exports since they are illegal inside a namespace
                            updated = null;
                        }
                        else
                        {
                            updated = Visitor().VisitEachChild(node);
                        }

                        break;
                    case Kind.ImportEqualsDeclaration:
                        if (_currentNamespace is not null && _currentScope is not null && _currentScope.Kind != Kind.Block && node.AsImportEqualsDeclaration()!.ModuleReference!.Kind == Kind.ExternalModuleReference)
                        {
                            // do not emit ES6 imports and exports since they are illegal inside a namespace
                            updated = null;
                        }
                        else if (_currentNamespace is not null && _currentScope is not null && _currentScope.Kind == Kind.Block && node.AsImportEqualsDeclaration()!.ModuleReference!.Kind != Kind.ExternalModuleReference)
                        {
                            // inside a block within a namespace, elide internal import aliases
                            updated = null;
                        }
                        else
                        {
                            updated = VisitImportEqualsDeclaration(node.AsImportEqualsDeclaration()!);
                        }

                        break;
                    case Kind.Identifier:
                        updated = VisitIdentifier(node);
                        break;
                    case Kind.ShorthandPropertyAssignment:
                        updated = VisitShorthandPropertyAssignment(node.AsShorthandPropertyAssignment()!);
                        break;
                    default:
                        updated = Visitor().VisitEachChild(node);
                        break;
                }

                return updated;
            }
            finally
            {
                PopScope(savedCurrentScope, savedCurrentScopeFirstDeclarationsOfName);
            }
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }

    /// <summary>Records that a declaration was emitted in the current scope, if it was the first declaration for the provided symbol.</summary>
    private void RecordDeclarationInScope(Node node)
    {
        switch (node.Kind)
        {
            case Kind.VariableStatement:
                RecordDeclarationInScope(node.AsVariableStatement()!.DeclarationList!);
                return;
            case Kind.VariableDeclarationList:
                foreach (var decl in node.AsVariableDeclarationList()!.Declarations!.Nodes ?? [])
                {
                    RecordDeclarationInScope(decl);
                }

                return;
            case Kind.ArrayBindingPattern:
            case Kind.ObjectBindingPattern:
                foreach (var element in node.Elements() ?? [])
                {
                    RecordDeclarationInScope(element);
                }

                return;
        }

        var name = node.Name();
        if (name is not null)
        {
            if (IsIdentifier(name))
            {
                _currentScopeFirstDeclarationsOfName ??= [];
                var text = name.Text();
                if (!_currentScopeFirstDeclarationsOfName.ContainsKey(text))
                {
                    _currentScopeFirstDeclarationsOfName[text] = node;
                }
            }
            else if (IsBindingPattern(name))
            {
                RecordDeclarationInScope(name);
            }
        }
    }

    /// <summary>Determines whether a declaration is the first declaration with the same name emitted in the current scope.</summary>
    private bool IsFirstDeclarationInScope(Node node)
    {
        var name = node.Name();
        if (name is not null && IsIdentifier(name))
        {
            var text = name.Text();
            if (_currentScopeFirstDeclarationsOfName is not null && _currentScopeFirstDeclarationsOfName.TryGetValue(text, out var firstDeclaration))
            {
                return firstDeclaration == node;
            }
        }

        return false;
    }

    private bool IsExportOfNamespace(Node node)
    {
        return _currentNamespace is not null && (_currentScope is null || _currentScope.Kind != Kind.Block) && (node.ModifierFlags & ModifierFlags.Export) != 0;
    }

    /// <summary>Gets an expression that represents a property name, such as `"foo"` for the identifier `foo`.</summary>
    private Node? GetExpressionForPropertyName(EnumMember member)
    {
        var name = member.Name()!;
        switch (name.Kind)
        {
            case Kind.PrivateIdentifier:
                return Factory().NewIdentifier("");
            case Kind.ComputedPropertyName:
                var n = name.AsComputedPropertyName()!;
                // enums don't support computed properties so we always generate the 'expression' part of the name as-is.
                return Visitor().VisitNode(n.Expression);
            case Kind.Identifier:
                return Factory().NewStringLiteral(name.Text(), TokenFlags.None);
            case Kind.StringLiteral: // !!! propagate token flags (will produce new diffs)
                return Factory().NewStringLiteral(name.Text(), TokenFlags.None);
            case Kind.NumericLiteral:
                return Factory().NewNumericLiteral(name.Text(), TokenFlags.None);
            default:
                return name;
        }
    }

    /// <summary>Gets an expression like <c>E["A"]</c> that references an enum member.</summary>
    private Node GetEnumQualifiedElement(EnumDeclaration @enum, EnumMember member)
    {
        var prop = GetNamespaceQualifiedElement(GetNamespaceContainerName(@enum), GetExpressionForPropertyName(member)!);
        EmitContext().AddEmitFlags(prop, EmitFlags.NoComments | EmitFlags.NoNestedComments | EmitFlags.NoSourceMap | EmitFlags.NoNestedSourceMaps);
        return prop;
    }

    /// <summary>Gets an expression used to refer to a namespace or enum from within the body of its declaration.</summary>
    private Node GetNamespaceContainerName(Node node)
    {
        return Factory().NewGeneratedNameForNode(node);
    }

    /// <summary>Gets an expression used to refer to an export of a namespace or a member of an enum by property name.</summary>
    private Node GetNamespaceQualifiedProperty(Node ns, Node name)
    {
        return Factory().GetNamespaceMemberName(ns, name, new NodeFactory.NameOptions { AllowSourceMaps = true });
    }

    /// <summary>Gets an expression used to refer to an export of a namespace or a member of an enum by indexed access.</summary>
    private Node GetNamespaceQualifiedElement(Node ns, Node expression)
    {
        var qualifiedName = EmitContext().Factory.NewElementAccessExpression(ns, null /*questionDotToken*/, expression, NodeFlags.None);
        EmitContext().AssignCommentAndSourceMapRanges(qualifiedName, expression);
        return qualifiedName;
    }

    /// <summary>Gets an expression used within the provided node's container for any exported references.</summary>
    private Node GetExportQualifiedReferenceToDeclaration(Node node)
    {
        if (IsExportOfNamespace(node))
        {
            return Factory().GetExternalModuleOrNamespaceExportName(GetNamespaceContainerName(_currentNamespace!), node, false /*allowComments*/, true /*allowSourceMaps*/);
        }

        return Factory().GetDeclarationNameEx(node, new NodeFactory.NameOptions { AllowSourceMaps = true });
    }

    private bool AddVarForDeclaration(List<Node> statements, Node node)
    {
        RecordDeclarationInScope(node);
        if (!IsFirstDeclarationInScope(node))
        {
            return false;
        }

        // var name;
        var name = Factory().GetLocalNameEx(node, new NodeFactory.AssignedNameOptions { AllowSourceMaps = true });
        var varDecl = Factory().NewVariableDeclaration(name, null, null, null);
        var varFlags = IfElse(_currentScope == _currentSourceFile, NodeFlags.None, NodeFlags.Let);
        var varDecls = Factory().NewVariableDeclarationList(Factory().NewNodeList([varDecl]), varFlags);
        // Replicate modifierVisitor: strip decorators, TypeScript modifiers, and export when in namespace.
        var modifierMask = ~(ModifierFlags.TypeScriptModifier | ModifierFlags.Decorator);
        if (_currentNamespace is not null)
        {
            modifierMask &= ~ModifierFlags.Export;
        }

        var modifiers = ExtractModifiers(EmitContext(), node.Modifiers(), modifierMask);
        var varStatement = Factory().NewVariableStatement(modifiers, varDecls);

        EmitContext().SetOriginal(varDecl, node);
        // !!! synthetic comments
        EmitContext().SetOriginal(varStatement, node);

        // Adjust the source map emit to match the old emitter.
        if (IsEnumDeclaration(node))
        {
            EmitContext().SetSourceMapRange(varDecls, node.Loc);
        }
        else
        {
            EmitContext().SetSourceMapRange(varStatement, node.Loc);
        }

        // Trailing comments for enum declaration should be emitted after the function closure
        // instead of the variable statement:
        //
        //     /** Leading comment*/
        //     enum E {
        //         A
        //     } // trailing comment
        //
        // Should emit:
        //
        //     /** Leading comment*/
        //     var E;
        //     (function (E) {
        //         E[E["A"] = 0] = "A";
        //     })(E || (E = {})); // trailing comment
        //
        EmitContext().SetCommentRange(varStatement, node.Loc);
        EmitContext().AddEmitFlags(varStatement, EmitFlags.NoTrailingComments);
        statements.Add(varStatement);

        return true;
    }

    private Node? VisitEnumDeclaration(EnumDeclaration node)
    {
        if (!ShouldEmitEnumDeclaration(node))
        {
            return EmitContext().NewNotEmittedStatement(node);
        }

        var statements = new List<Node>();

        // If needed, we should emit a variable declaration for the enum:
        //  var name;
        var varAdded = AddVarForDeclaration(statements, node);

        // If we emit a leading variable declaration, we should not emit leading comments for the enum body, but we should
        // still emit the comments if we are emitting to a System module.
        var emitFlags = EmitFlags.None;
        if (varAdded && (_compilerOptions.GetEmitModuleKind() != ModuleKind.System || _currentScope != _currentSourceFile))
        {
            emitFlags |= EmitFlags.NoLeadingComments;
        }

        //  x || (x = {})
        //  exports.x || (exports.x = {})
        var enumArg = Factory().NewLogicalORExpression(
            GetExportQualifiedReferenceToDeclaration(node),
            Factory().NewAssignmentExpression(
                GetExportQualifiedReferenceToDeclaration(node),
                Factory().NewObjectLiteralExpression(Factory().NewNodeList([]), false)));

        if (IsExportOfNamespace(node))
        {
            // `localName` is the expression used within this node's containing scope for any local references.
            var localName = Factory().GetLocalNameEx(node, new NodeFactory.AssignedNameOptions { AllowSourceMaps = true });

            //  x = (exports.x || (exports.x = {}))
            enumArg = Factory().NewAssignmentExpression(localName, enumArg);
        }

        // (function (name) { ... })(name || (name = {}))
        var enumParamName = Factory().NewGeneratedNameForNode(node);
        EmitContext().SetSourceMapRange(enumParamName, node.Name()!.Loc);

        var enumParam = Factory().NewParameterDeclaration(null, null, enumParamName, null, null, null);
        var enumBody = TransformEnumBody(node);
        var enumFunc = Factory().NewFunctionExpression(null, null, null, null, Factory().NewNodeList([enumParam]), null, null, enumBody);
        var enumCall = Factory().NewCallExpression(Factory().NewParenthesizedExpression(enumFunc), null, null, Factory().NewNodeList([enumArg]), NodeFlags.None);
        var enumStatement = Factory().NewExpressionStatement(enumCall);
        EmitContext().SetOriginal(enumStatement, node);
        EmitContext().AssignCommentAndSourceMapRanges(enumStatement, node);
        EmitContext().AddEmitFlags(enumStatement, emitFlags);
        statements.Add(enumStatement);
        return Factory().NewSyntaxList(statements.ToArray());
    }

    /// <summary>Transforms the body of an enum declaration.</summary>
    private Node TransformEnumBody(EnumDeclaration node)
    {
        var savedCurrentEnum = _currentEnum;
        _currentEnum = node;

        // visit the children of `node` in advance to capture any references to enum members
        node = Visitor().VisitEachChild(node)!.AsEnumDeclaration()!;

        var statements = new List<Node>();
        for (var i = 0; i < node.Members!.Nodes!.Length; i++)
        {
            //  E[E["A"] = 0] = "A";
            TransformEnumMember(statements, node, i);
        }

        var statementList = Factory().NewNodeList(statements.ToArray());
        statementList.Loc = node.Members.Loc;

        _currentEnum = savedCurrentEnum;
        return Factory().NewBlock(statementList, true /*multiline*/);
    }

    /// <summary>Transforms an enum member into a statement. It is expected that `enum` has already been visited.</summary>
    private void TransformEnumMember(List<Node> statements, EnumDeclaration @enum, int index)
    {
        var memberNode = @enum.Members!.Nodes![index];
        var member = memberNode.AsEnumMember()!;

        var savedParent = _parentNode;
        _parentNode = _currentNode;
        _currentNode = memberNode;

        //  E[E["A"] = x] = "A";
        //             ^
        Node? expression = member.Initializer; // NOTE: already visited

        bool useExplicitReverseMapping;

        var parseNode = EmitContext().ParseNode(memberNode);
        var result = _emitResolver!.GetEnumMemberValue(parseNode!);
        switch (result.Value)
        {
            case JsNum.Number value:
                expression = Coalesce(ConstantExpression(value, Factory()), expression);
                useExplicitReverseMapping = true;
                break;
            case string value:
                expression = Coalesce(ConstantExpression(value, Factory()), expression);
                useExplicitReverseMapping = false;
                break;
            default:
                if (expression is null)
                {
                    expression = Factory().NewVoidZeroExpression();
                }

                useExplicitReverseMapping = !result.IsSyntacticallyString;
                break;
        }

        // Define the enum member property:
        //  E[E["A"] = 0] = "A";
        //    ^^^^^^^^--_____
        expression = Factory().NewAssignmentExpression(
            GetEnumQualifiedElement(@enum, member),
            expression!);

        if (useExplicitReverseMapping)
        {
            //  E[E["A"] = 0] = "A";
            //  ^^--------------^^^^^
            expression = Factory().NewAssignmentExpression(
                Factory().NewElementAccessExpression(
                    GetNamespaceContainerName(@enum),
                    null, /*questionDotToken*/
                    expression,
                    NodeFlags.None),
                GetExpressionForPropertyName(member)!);
        }

        var memberStatement = Factory().NewExpressionStatement(expression);
        EmitContext().AssignCommentAndSourceMapRanges(expression, member);
        EmitContext().AssignCommentAndSourceMapRanges(memberStatement, member);
        statements.Add(memberStatement);

        _currentNode = _parentNode;
        _parentNode = savedParent;
    }

    private Node? VisitModuleDeclaration(ModuleDeclaration node)
    {
        if (!ShouldEmitModuleDeclaration(node))
        {
            return EmitContext().NewNotEmittedStatement(node);
        }

        var statements = new List<Node>();

        // If needed, we should emit a variable declaration for the module:
        //  var name;
        var varAdded = AddVarForDeclaration(statements, node);

        // If we emit a leading variable declaration, we should not emit leading comments for the module body, but we should
        // still emit the comments if we are emitting to a System module.
        var emitFlags = EmitFlags.None;
        if (varAdded && (_compilerOptions.GetEmitModuleKind() != ModuleKind.System || _currentScope != _currentSourceFile))
        {
            emitFlags |= EmitFlags.NoLeadingComments;
        }

        //  x || (x = {})
        //  exports.x || (exports.x = {})
        var moduleArg = Factory().NewLogicalORExpression(
            GetExportQualifiedReferenceToDeclaration(node),
            Factory().NewAssignmentExpression(
                GetExportQualifiedReferenceToDeclaration(node),
                Factory().NewObjectLiteralExpression(Factory().NewNodeList([]), false)));

        if (IsExportOfNamespace(node))
        {
            // `localName` is the expression used within this node's containing scope for any local references.
            var localName = Factory().GetLocalNameEx(node, new NodeFactory.AssignedNameOptions { AllowSourceMaps = true });

            //  x = (exports.x || (exports.x = {}))
            moduleArg = Factory().NewAssignmentExpression(localName, moduleArg);
        }

        // (function (name) { ... })(name || (name = {}))
        var moduleParamName = Factory().NewGeneratedNameForNode(node);
        EmitContext().SetSourceMapRange(moduleParamName, node.Name()!.Loc);

        var moduleParam = Factory().NewParameterDeclaration(null, null, moduleParamName, null, null, null);
        var moduleBody = TransformModuleBody(node, GetNamespaceContainerName(node));
        var moduleFunc = Factory().NewFunctionExpression(null, null, null, null, Factory().NewNodeList([moduleParam]), null, null, moduleBody);
        var moduleCall = Factory().NewCallExpression(Factory().NewParenthesizedExpression(moduleFunc), null, null, Factory().NewNodeList([moduleArg]), NodeFlags.None);
        var moduleStatement = Factory().NewExpressionStatement(moduleCall);
        EmitContext().SetOriginal(moduleStatement, node);
        EmitContext().AssignCommentAndSourceMapRanges(moduleStatement, node);
        EmitContext().AddEmitFlags(moduleStatement, emitFlags);
        statements.Add(moduleStatement);
        return Factory().NewSyntaxList(statements.ToArray());
    }

    private Node TransformModuleBody(ModuleDeclaration node, Node namespaceLocalName)
    {
        var savedCurrentNamespace = _currentNamespace;
        var savedCurrentScope = _currentScope;
        var savedCurrentScopeFirstDeclarationsOfName = _currentScopeFirstDeclarationsOfName;

        _currentNamespace = node;
        _currentScopeFirstDeclarationsOfName = null;

        Node[]? statements = null;
        EmitContext().StartVariableEnvironment();

        var statementsLocation = default(TextRange);
        var blockLocation = default(TextRange);
        if (node.Body is not null)
        {
            if (node.Body.Kind == Kind.ModuleBlock)
            {
                // visit the children of `node` in advance to capture any references to namespace members
                node = Visitor().VisitEachChild(node)!.AsModuleDeclaration()!;
                var body = node.Body!.AsModuleBlock()!;
                statements = body.Statements!.Nodes;
                statementsLocation = body.Statements.Loc;
                blockLocation = body.Loc;
            }
            else // node.Body.Kind == ast.KindModuleDeclaration
            {
                // !!! Strada didn't do this; why?
                // tx.currentScope = node.AsNode()
                statements = Visitor().VisitSlice([node.Body]).Result;
                var moduleBlock = GetInnermostModuleDeclarationFromDottedModule(node).Body!.AsModuleBlock()!;
                statementsLocation = moduleBlock.Statements!.Loc.WithPos(-1);
            }
        }

        _currentNamespace = savedCurrentNamespace;
        _currentScope = savedCurrentScope;
        _currentScopeFirstDeclarationsOfName = savedCurrentScopeFirstDeclarationsOfName;

        statements = EmitContext().EndAndMergeVariableEnvironment(statements);
        var statementList = Factory().NewNodeList(statements);
        statementList.Loc = statementsLocation;
        var block = Factory().NewBlock(statementList, true /*multiline*/);
        block.Loc = blockLocation;

        //  namespace hello.hi.world {
        //       function foo() {}
        //
        //       // TODO, blah
        //  }
        //
        // should be emitted as
        //
        //  var hello;
        //  (function (hello) {
        //      var hi;
        //      (function (hi) {
        //          var world;
        //          (function (world) {
        //              function foo() { }
        //              // TODO, blah
        //          })(world = hi.world || (hi.world = {}));
        //      })(hi = hello.hi || (hello.hi = {}));
        //  })(hello || (hello = {}));
        //
        // We only want to emit comment on the namespace which contains block body itself, not the containing namespaces.
        if (node.Body is null || node.Body.Kind != Kind.ModuleBlock)
        {
            EmitContext().AddEmitFlags(block, EmitFlags.NoComments);
        }

        return block;
    }

    private Node? VisitImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        if (node.ModuleReference!.Kind == Kind.ExternalModuleReference)
        {
            return Visitor().VisitEachChild(node);
        }

        var moduleReference = Factory().CreateExpressionFromEntityName(node.ModuleReference);
        EmitContext().SetEmitFlags(moduleReference, EmitFlags.NoComments | EmitFlags.NoNestedComments);
        if (!IsExportOfNamespace(node))
        {
            //  export var ${name} = ${moduleReference};
            //  var ${name} = ${moduleReference};
            var varDecl = Factory().NewVariableDeclaration(node.Name(), null /*exclamationToken*/, null /*type*/, moduleReference);
            EmitContext().SetOriginal(varDecl, node);
            var varList = Factory().NewVariableDeclarationList(Factory().NewNodeList([varDecl]), NodeFlags.None);
            var varModifiers = ExtractModifiers(EmitContext(), node.Modifiers(), ModifierFlags.Export);
            var varStatement = Factory().NewVariableStatement(varModifiers, varList);
            EmitContext().SetOriginal(varStatement, node);
            EmitContext().AssignCommentAndSourceMapRanges(varStatement, node);
            return varStatement;
        }
        else
        {
            // exports.${name} = ${moduleReference};
            var statement = CreateExportStatement(node.Name()!, moduleReference, node.Loc, node.Loc, node);
            statement.Loc = node.Loc;
            return statement;
        }
    }

    private Node? VisitVariableStatement(VariableStatement node)
    {
        if (IsExportOfNamespace(node))
        {
            var expressions = new List<Node>();
            foreach (var declaration in node.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes ?? [])
            {
                var v = declaration.AsVariableDeclaration()!;
                if (v.Initializer is null)
                {
                    continue;
                }

                if (IsBindingPattern(v.Name()!))
                {
                    var expression = FlattenDestructuringAssignment(
                        this,
                        Visitor().VisitNode(declaration)!,
                        false, /*needsValue*/
                        FlattenLevel.All,
                        CreateNamespaceExportExpression);
                    if (expression is not null)
                    {
                        expressions.Add(expression);
                    }
                }
                else
                {
                    var expression = ConvertVariableDeclarationToAssignmentExpression(EmitContext(), v);
                    if (expression is not null)
                    {
                        expressions.Add(expression);
                    }
                }
            }

            if (expressions.Count == 0)
            {
                return null;
            }

            var combined = Factory().InlineExpressions(expressions.ToArray());
            var statement = Factory().NewExpressionStatement(combined);
            EmitContext().SetOriginal(statement, node);
            EmitContext().AssignCommentAndSourceMapRanges(statement, node);

            // re-visit as the new node
            var savedCurrent = _currentNode;
            _currentNode = statement;
            statement = Visitor().VisitEachChild(statement)!;
            _currentNode = savedCurrent;
            return statement;
        }

        return Visitor().VisitEachChild(node);
    }

    // createNamespaceExportExpression creates an assignment to a namespace member for use as a
    // callback during destructuring flattening.
    //
    // 偏离 Go：`CreateAssignmentCallback`（T1 在 Transformers/Destructuring.cs 落地）把 Go 的
    // `location *core.TextRange` 落成**按值** `TextRange`，而 Go 的唯一调用点传的是 `&location`
    // （恒非 nil），所以 Go 的 `if location != nil` 分支在 C# 里恒成立、直接赋值。
    private Node CreateNamespaceExportExpression(Node exportName, Node exportValue, TextRange location)
    {
        var memberName = GetNamespaceQualifiedProperty(GetNamespaceContainerName(_currentNamespace!), exportName);
        var expression = Factory().NewAssignmentExpression(memberName, exportValue);
        expression.Loc = location;
        return expression;
    }

    private Node? VisitFunctionDeclaration(FunctionDeclaration node)
    {
        if (IsExportOfNamespace(node))
        {
            var updated = Factory().UpdateFunctionDeclaration(
                node,
                Visitor().VisitModifiers(ExtractModifiers(EmitContext(), node.Modifiers(), ~ModifierFlags.Export)),
                node.AsteriskToken,
                Visitor().VisitNode(node.Name()),
                null, /*typeParameters*/
                Visitor().VisitNodes(node.Parameters),
                null, /*returnType*/
                null, /*fullSignature*/
                Visitor().VisitNode(node.Body));
            var export = CreateExportStatementForDeclaration(node);
            if (export is not null)
            {
                return Factory().NewSyntaxList([updated, export]);
            }

            return updated;
        }

        return Visitor().VisitEachChild(node);
    }

    private ParameterDeclaration[] GetParameterProperties(Node? constructor)
    {
        var parameterProperties = new List<ParameterDeclaration>();
        if (constructor is not null)
        {
            foreach (var parameter in constructor.Parameters() ?? [])
            {
                if (IsParameterPropertyDeclaration(parameter, constructor))
                {
                    parameterProperties.Add(parameter.AsParameterDeclaration()!);
                }
            }
        }

        return parameterProperties.ToArray();
    }

    private Node? VisitClassDeclaration(ClassDeclaration node)
    {
        var exported = IsExportOfNamespace(node);
        ModifierList? modifiers;
        if (exported)
        {
            modifiers = Visitor().VisitModifiers(ExtractModifiers(EmitContext(), node.Modifiers(), ~ModifierFlags.ExportDefault));
        }
        else
        {
            modifiers = Visitor().VisitModifiers(node.Modifiers());
        }

        var name = Visitor().VisitNode(node.Name());
        if (name is null && (exported || ChildIsDecorated(_compilerOptions.ExperimentalDecorators.IsTrue(), node, null)))
        {
            name = Factory().NewGeneratedNameForNode(node);
        }

        var heritageClauses = Visitor().VisitNodes(node.HeritageClauses);
        var members = Visitor().VisitNodes(node.Members);
        var parameterProperties = GetParameterProperties(Find(node.Members!.Nodes, IsConstructorDeclaration));

        if (parameterProperties.Length > 0)
        {
            var newMembers = new List<Node>();
            foreach (var parameter in parameterProperties)
            {
                if (IsIdentifier(parameter.Name()!))
                {
                    var parameterProperty = Factory().NewPropertyDeclaration(
                        null, /*modifiers*/
                        parameter.Name()!.Clone(Factory())!,
                        null, /*questionOrExclamationToken*/
                        null, /*type*/
                        null /*initializer*/);
                    EmitContext().SetOriginal(parameterProperty, parameter);
                    newMembers.Add(parameterProperty);
                }
            }

            if (newMembers.Count > 0)
            {
                newMembers.AddRange(members!.Nodes ?? []);
                members = Factory().NewNodeList(newMembers.ToArray());
                members.Loc = node.Members.Loc;
            }
        }

        var updated = Factory().UpdateClassDeclaration(node, modifiers, name, null /*typeParameters*/, heritageClauses, members);
        if (exported)
        {
            var export = CreateExportStatementForDeclaration(node);
            if (export is not null)
            {
                return Factory().NewSyntaxList([updated, export]);
            }
        }

        return updated;
    }

    private Node? VisitClassExpression(ClassExpression node)
    {
        var modifiers = Visitor().VisitModifiers(ExtractModifiers(EmitContext(), node.Modifiers(), ~ModifierFlags.ExportDefault));
        var name = Visitor().VisitNode(node.Name());
        var heritageClauses = Visitor().VisitNodes(node.HeritageClauses);
        var members = Visitor().VisitNodes(node.Members);
        var parameterProperties = GetParameterProperties(Find(node.Members!.Nodes, IsConstructorDeclaration));

        if (parameterProperties.Length > 0)
        {
            var newMembers = new List<Node>();
            foreach (var parameter in parameterProperties)
            {
                if (IsIdentifier(parameter.Name()!))
                {
                    var parameterProperty = Factory().NewPropertyDeclaration(
                        null, /*modifiers*/
                        parameter.Name()!.Clone(Factory())!,
                        null, /*questionOrExclamationToken*/
                        null, /*type*/
                        null /*initializer*/);
                    EmitContext().SetOriginal(parameterProperty, parameter);
                    newMembers.Add(parameterProperty);
                }
            }

            if (newMembers.Count > 0)
            {
                newMembers.AddRange(members!.Nodes ?? []);
                members = Factory().NewNodeList(newMembers.ToArray());
                members.Loc = node.Members.Loc;
            }
        }

        return Factory().UpdateClassExpression(node, modifiers, name, null /*typeParameters*/, heritageClauses, members);
    }

    private Node? VisitConstructorDeclaration(ConstructorDeclaration node)
    {
        var modifiers = Visitor().VisitModifiers(node.Modifiers());
        var parameters = EmitContext().VisitParameters(node.ParameterList()!, Visitor());
        var body = VisitConstructorBody(node.Body!.AsBlock()!, node);
        return Factory().UpdateConstructorDeclaration(node, modifiers, null /*typeParameters*/, parameters, null /*returnType*/, null /*fullSignature*/, body);
    }

    private Node? VisitConstructorBody(Block body, Node constructor)
    {
        var parameterProperties = GetParameterProperties(constructor);
        if (parameterProperties.Length == 0)
        {
            return EmitContext().VisitFunctionBody(body, Visitor());
        }

        var grandparentOfBody = PushNode(body);
        var (savedCurrentScope, savedCurrentScopeFirstDeclarationsOfName) = PushScope(body);

        EmitContext().StartVariableEnvironment();
        var (prologue, rest) = Factory().SplitStandardPrologue(body.Statements!.Nodes!);
        // Go: slices.Clone(prologue) + append —— C# 用 List（自己管扩容）。
        var statements = new List<Node>(prologue);

        // Transform parameters into property assignments. Transforms this:
        //
        //  constructor (public x, public y) {
        //  }
        //
        // Into this:
        //
        //  constructor (x, y) {
        //      this.x = x;
        //      this.y = y;
        //  }
        //

        var parameterPropertyAssignments = new List<Node>();
        foreach (var parameter in parameterProperties)
        {
            if (IsIdentifier(parameter.Name()!))
            {
                var propertyName = parameter.Name()!.Clone(Factory())!;
                propertyName.Parent = parameter.Name()!.Parent; // .Parent set to get node to printback using text from original file instead of processed text; TODO: this should be achievable via EmitFlags instead
                EmitContext().AddEmitFlags(propertyName, EmitFlags.NoComments | EmitFlags.NoSourceMap);

                var localName = parameter.Name()!.Clone(Factory())!;
                localName.Parent = parameter.Name()!.Parent; // .Parent set to get node to printback using text from original file instead of processed text; TODO: this should be achievable via EmitFlags instead
                EmitContext().AddEmitFlags(localName, EmitFlags.NoComments);

                var parameterProperty = Factory().NewExpressionStatement(
                    Factory().NewAssignmentExpression(
                        Factory().NewPropertyAccessExpression(
                            Factory().NewThisExpression(),
                            null, /*questionDotToken*/
                            propertyName,
                            NodeFlags.None),
                        localName));
                EmitContext().SetOriginal(parameterProperty, parameter);
                EmitContext().AddEmitFlags(parameterProperty, EmitFlags.StartOnNewLine);
                parameterPropertyAssignments.Add(parameterProperty);
            }
        }

        var superPath = FindSuperStatementIndexPath(rest, 0);

        if (superPath.Length > 0)
        {
            statements.AddRange(TransformConstructorBodyWorker(rest!, superPath, parameterPropertyAssignments));
        }
        else
        {
            statements.AddRange(parameterPropertyAssignments);
            statements.AddRange(Visitor().VisitSlice(rest).Result ?? []);
        }

        var merged = EmitContext().EndAndMergeVariableEnvironment(statements.ToArray());
        var statementList = Factory().NewNodeList(merged);
        statementList.Loc = body.Statements.Loc;

        PopScope(savedCurrentScope, savedCurrentScopeFirstDeclarationsOfName);
        PopNode(grandparentOfBody);
        var updated = Factory().NewBlock(statementList, true /*multiline*/);
        EmitContext().SetOriginal(updated, body);
        updated.Loc = body.Loc;
        return updated;
    }

    private List<Node> TransformConstructorBodyWorker(Node[] statementsIn, int[] superPath, List<Node> initializerStatements)
    {
        var statementsOut = new List<Node>();
        var superStatementIndex = superPath[0];
        var superStatement = statementsIn[superStatementIndex];

        // visit up to the statement containing `super`
        statementsOut.AddRange(Visitor().VisitSlice(statementsIn[..superStatementIndex]).Result ?? []);

        // if the statement containing `super` is a `try` statement, transform the body of the `try` block
        if (IsTryStatement(superStatement))
        {
            var tryStatement = superStatement.AsTryStatement()!;
            var tryBlock = tryStatement.TryBlock!.AsBlock()!;

            // keep track of hierarchy as we descend
            var grandparentOfTryStatement = PushNode(tryStatement);
            var grandparentOfTryBlock = PushNode(tryBlock);
            var (savedCurrentScope, savedCurrentScopeFirstDeclarationsOfName) = PushScope(tryBlock);

            // visit the `try` block
            var tryBlockStatements = TransformConstructorBodyWorker(
                tryBlock.Statements!.Nodes!,
                superPath[1..],
                initializerStatements);

            // restore hierarchy as we ascend to the `try` statement
            PopScope(savedCurrentScope, savedCurrentScopeFirstDeclarationsOfName);
            PopNode(grandparentOfTryBlock);

            var tryBlockStatementList = Factory().NewNodeList(tryBlockStatements.ToArray());
            tryBlockStatementList.Loc = tryBlock.Statements.Loc;
            statementsOut.Add(Factory().UpdateTryStatement(
                tryStatement,
                Factory().UpdateBlock(tryBlock, tryBlockStatementList, tryBlock.MultiLine),
                Visitor().VisitNode(tryStatement.CatchClause),
                Visitor().VisitNode(tryStatement.FinallyBlock)));

            // restore hierarchy as we ascend to the parent of the `try` statement
            PopNode(grandparentOfTryStatement);
        }
        else
        {
            // visit the statement containing `super`
            statementsOut.AddRange(Visitor().VisitSlice(statementsIn[superStatementIndex..(superStatementIndex + 1)]).Result ?? []);

            // insert the initializer statements
            statementsOut.AddRange(initializerStatements);
        }

        // visit the statements after `super`
        statementsOut.AddRange(Visitor().VisitSlice(statementsIn[(superStatementIndex + 1)..]).Result ?? []);
        return statementsOut;
    }

    private Node? VisitShorthandPropertyAssignment(ShorthandPropertyAssignment node)
    {
        var name = node.Name()!;
        var exportedOrImportedName = VisitExpressionIdentifier(name);
        if (exportedOrImportedName != name)
        {
            var expression = exportedOrImportedName;
            if (node.ObjectAssignmentInitializer is not null)
            {
                var equalsToken = node.EqualsToken;
                equalsToken ??= Factory().NewToken(Kind.EqualsToken);
                expression = Factory().NewBinaryExpression(
                    null, /*modifiers*/
                    expression,
                    null, /*typeNode*/
                    equalsToken,
                    Visitor().VisitNode(node.ObjectAssignmentInitializer));
            }

            var updated = Factory().NewPropertyAssignment(null /*modifiers*/, node.Name(), null /*postfixToken*/, null /*typeNode*/, expression);
            updated.Loc = node.Loc;
            EmitContext().SetOriginal(updated, node);
            EmitContext().AssignCommentAndSourceMapRanges(updated, node);
            return updated;
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

    private Node? VisitIdentifier(Node node)
    {
        if (IsIdentifierReference(node, _parentNode!))
        {
            return VisitExpressionIdentifier(node);
        }

        return node;
    }

    private Node? VisitExpressionIdentifier(Node node)
    {
        if ((_currentEnum is not null || _currentNamespace is not null) && !IsGeneratedIdentifier(EmitContext(), node) && !IsLocalName(EmitContext(), node))
        {
            var location = EmitContext().MostOriginal(node)!;
            var container = _resolver!.GetReferencedExportContainer(location, false /*prefixLocals*/);
            if (container is not null && (IsEnumDeclaration(container) || IsModuleDeclaration(container)))
            {
                var containerName = GetNamespaceContainerName(container);

                var memberName = node.Clone(Factory())!;
                EmitContext().SetEmitFlags(memberName, EmitFlags.NoComments | EmitFlags.NoSourceMap);

                var expression = Factory().GetNamespaceMemberName(containerName, memberName, new NodeFactory.NameOptions { AllowSourceMaps = true });
                EmitContext().AssignCommentAndSourceMapRanges(expression, node);
                return expression;
            }
        }

        return node;
    }

    private Node? CreateExportStatementForDeclaration(Node node)
    {
        var exportName = Factory().GetExternalModuleOrNamespaceExportName(GetNamespaceContainerName(_currentNamespace!), node, false /*allowComments*/, true /*allowSourceMaps*/);
        var localName = Factory().GetLocalName(node);
        var expression = Factory().NewAssignmentExpression(exportName, localName);
        var exportAssignmentSourceMapRange = node.Loc;
        if (node.Name() is not null)
        {
            exportAssignmentSourceMapRange = exportAssignmentSourceMapRange.WithPos(node.Name()!.Pos());
        }

        EmitContext().SetSourceMapRange(expression, exportAssignmentSourceMapRange);

        var statement = Factory().NewExpressionStatement(expression);
        var exportStatementSourceMapRange = node.Loc.WithPos(-1);
        EmitContext().SetSourceMapRange(statement, exportStatementSourceMapRange);
        return statement;
    }

    private Node CreateExportAssignment(Node name, Node expression, TextRange exportAssignmentSourceMapRange, Node original)
    {
        var exportName = GetNamespaceQualifiedProperty(GetNamespaceContainerName(_currentNamespace!), name);
        var exportAssignment = Factory().NewAssignmentExpression(exportName, expression);
        EmitContext().SetOriginal(exportAssignment, original);
        EmitContext().SetSourceMapRange(exportAssignment, exportAssignmentSourceMapRange);
        return exportAssignment;
    }

    private Node CreateExportStatement(Node name, Node expression, TextRange exportAssignmentSourceMapRange, TextRange exportStatementSourceMapRange, Node original)
    {
        var exportStatement = Factory().NewExpressionStatement(CreateExportAssignment(name, expression, exportAssignmentSourceMapRange, original));
        EmitContext().SetOriginal(exportStatement, original);
        EmitContext().SetSourceMapRange(exportStatement, exportStatementSourceMapRange);
        return exportStatement;
    }

    private bool ShouldEmitEnumDeclaration(EnumDeclaration node)
    {
        return !IsEnumConst(node) || _compilerOptions.ShouldPreserveConstEnums();
    }

    private bool ShouldEmitModuleDeclaration(ModuleDeclaration node)
    {
        var pn = EmitContext().ParseNode(node);
        if (pn is null)
        {
            // If we can't find a parse tree node, assume the node is instantiated.
            return true;
        }

        return IsInstantiatedModule(pn, _compilerOptions.ShouldPreserveConstEnums());
    }
}

public static partial class TsTransforms
{
    /// <summary>Go: <c>func getInnermostModuleDeclarationFromDottedModule(moduleDeclaration) *ast.ModuleDeclaration</c>。</summary>
    internal static ModuleDeclaration GetInnermostModuleDeclarationFromDottedModule(ModuleDeclaration moduleDeclaration)
    {
        while (moduleDeclaration.Body is not null && moduleDeclaration.Body.Kind == Kind.ModuleDeclaration)
        {
            moduleDeclaration = moduleDeclaration.Body.AsModuleDeclaration()!;
        }

        return moduleDeclaration;
    }
}

// ported from: internal/binder/binder.go:1476-1789（bindContainer / declareCommonJSVariable / bindChildren /
//   bindEachChild / bindEach / bindNodeList / bindModifiers / bindEachStatementFunctionsFirst / setContinueTarget）
//
// 与 Go 的差异（C# 语言特性，逐条依据 docs/architecture.md §2/§3 与 docs/ast.md；其余逐行对应，含注释与分支顺序）：
// - `ContainerFlags` 是 `[Flags] enum`（§3.3）：位测试写成 `(containerFlags & ContainerFlags.X) != 0`
//   （`&` 的优先级低于 `==`/`!=`，一律加括号）；Go 的 `node.Flags &^= a | b` → `node.Flags &= ~(NodeFlags.A | NodeFlags.B)`。
// - Go 的多值 case（`case ast.KindForInStatement, ast.KindForOfStatement:`）→ C# 的连续 `case a: case b:`（同一分支体）。
// - Go 的 nil 切片 range（不迭代）→ `X ?? []`（如 `node.Statements() ?? []`、`statements!.Nodes ?? []`）；
//   `bindEach`/`bindNodeList`/`bindModifiers`/`bindEachStatementFunctionsFirst` 的 nil 参数分支保留，语义同 Go。
// - Go 的 `make(ast.SymbolTable, 1)` 容量提示在 C# 无处可写（`SymbolTable : Dictionary<string, Symbol>` 没有容量
//   构造器）→ `new SymbolTable()`；Go 的 map 读（缺键 = nil）→ `GetValueOrDefault(k)`（§3.2）。
// - Go 里可能为 nil 却被直接解引用的指针（Go 侧 nil 即 panic）→ C# 按同一点上的不变量写 `!`：
//   `_currentFlow!.Flags`、`statements!.Nodes`、`symbol.Declarations![0]`、`symbol.Members!["exports"]`、
//   `node.AsSourceFile()!`（kind 已判定，docs/ast.md §2）、`node.Symbol!`、`node.Parent!`。
// - Go 的包级函数 `ast.IsSourceFile`/`ast.IsInJSFile`/`ast.IsJSTypeAliasDeclaration`/`NodeIsPresent`/
//   `IsPotentiallyExecutableNode`/`IsDestructuringAssignment`/`HasSyntacticModifier`/
//   `GetImmediatelyInvokedFunctionExpression` 经 `using static TypeScript.Ast.Ast` 裸名调用（§2）。
// - 跨切片依赖（不在本文件内实现，按 Go 名 PascalCase 调用）：`AddToContainerChain`（W4 范围，binder.go:2535）、
//   `SetReturnFlowNode`（W4 范围，binder.go:2518，包级函数 → private static）、`IsGeneratorFunctionExpression`
//   （W4 范围，binder.go:2531）、`BindCommonJSTypeExports`（W1 范围，binder.go:1046）、以及各 `Bind*`/`Bind*Flow` 方法（W1/W3/W4）。
namespace TypeScript.Binder;

using TypeScript.Ast;
using static TypeScript.Ast.Ast;

public sealed partial class Binder
{
    // All container nodes are kept on a linked list in declaration order. This list is used by
    // the getLocalNameOfContainer function in the type checker to validate that the local name
    // used for a container is unique.
    private void BindContainer(Node node, ContainerFlags containerFlags)
    {
        // Before we recurse into a node's children, we first save the existing parent, container
        // and block-container.  Then after we pop out of processing the children, we restore
        // these saved values.
        var saveContainer = _container;
        var saveThisContainer = _thisContainer;
        var savedBlockScopeContainer = _blockScopeContainer;
        // Depending on what kind of node this is, we may have to adjust the current container
        // and block-container.   If the current node is a container, then it is automatically
        // considered the current block-container as well.  Also, for containers that we know
        // may contain locals, we eagerly initialize the .locals field. We do this because
        // it's highly likely that the .locals will be needed to place some child in (for example,
        // a parameter, or variable declaration).
        //
        // However, we do not proactively create the .locals for block-containers because it's
        // totally normal and common for block-containers to never actually have a block-scoped
        // variable in them.  We don't want to end up allocating an object for every 'block' we
        // run into when most of them won't be necessary.
        //
        // Finally, if this is a block-container, then we clear out any existing .locals object
        // it may contain within it.  This happens in incremental scenarios.  Because we can be
        // reusing a node from a previous compilation, that node may have had 'locals' created
        // for it.  We must clear this so we don't accidentally move any stale data forward from
        // a previous compilation.
        if ((containerFlags & ContainerFlags.IsContainer) != 0)
        {
            _container = node;
            _blockScopeContainer = node;
            if ((containerFlags & ContainerFlags.HasLocals) != 0)
            {
                // localsContainer := node
                // localsContainer.LocalsContainerData().locals = make(SymbolTable)
                AddToContainerChain(node);
            }
        }
        else if ((containerFlags & ContainerFlags.IsBlockScopedContainer) != 0)
        {
            _blockScopeContainer = node;
            AddToContainerChain(node);
        }

        if ((containerFlags & ContainerFlags.IsThisContainer) != 0)
        {
            _thisContainer = node;
        }

        if ((containerFlags & ContainerFlags.IsControlFlowContainer) != 0)
        {
            var saveCurrentFlow = _currentFlow;
            var saveBreakTarget = _currentBreakTarget;
            var saveContinueTarget = _currentContinueTarget;
            var saveReturnTarget = _currentReturnTarget;
            var saveExceptionTarget = _currentExceptionTarget;
            var saveActiveLabelList = _activeLabelList;
            var saveHasExplicitReturn = _hasExplicitReturn;
            var saveSeenThisKeyword = _seenThisKeyword;
            var isImmediatelyInvoked = ((containerFlags & ContainerFlags.IsFunctionExpression) != 0 &&
                !HasSyntacticModifier(node, ModifierFlags.Async) &&
                !IsGeneratorFunctionExpression(node) &&
                GetImmediatelyInvokedFunctionExpression(node) is not null) || node.Kind == Kind.ClassStaticBlockDeclaration;
            // A non-async, non-generator IIFE is considered part of the containing control flow. Return statements behave
            // similarly to break statements that exit to a label just past the statement body.
            if (!isImmediatelyInvoked)
            {
                var flowStart = NewFlowNode(FlowFlags.Start);
                _currentFlow = flowStart;
                if ((containerFlags & (ContainerFlags.IsFunctionExpression | ContainerFlags.IsObjectLiteralOrClassExpressionMethodOrAccessor)) != 0)
                {
                    flowStart.Node = node;
                }
            }
            // We create a return control flow graph for IIFEs and constructors. For constructors
            // we use the return control flow graph in strict property initialization checks.
            if (isImmediatelyInvoked || node.Kind == Kind.Constructor)
            {
                _currentReturnTarget = NewFlowNode(FlowFlags.BranchLabel);
            }
            else
            {
                _currentReturnTarget = null;
            }
            _currentExceptionTarget = null;
            _currentBreakTarget = null;
            _currentContinueTarget = null;
            _activeLabelList = null;
            _hasExplicitReturn = false;
            _seenThisKeyword = false;
            BindChildren(node);
            // Reset flags (for incremental scenarios)
            node.Flags &= ~(NodeFlags.ReachabilityAndEmitFlags | NodeFlags.ContainsThis);
            if ((_currentFlow!.Flags & FlowFlags.Unreachable) == 0 && (containerFlags & ContainerFlags.IsFunctionLike) != 0)
            {
                var bodyData = node.BodyData();
                if (bodyData is not null && NodeIsPresent(bodyData.Body))
                {
                    node.Flags |= NodeFlags.HasImplicitReturn;
                    if (_hasExplicitReturn)
                    {
                        node.Flags |= NodeFlags.HasExplicitReturn;
                    }
                    bodyData.EndFlowNode = _currentFlow;
                }
            }
            if (_seenThisKeyword)
            {
                node.Flags |= NodeFlags.ContainsThis;
            }
            if (node.Kind == Kind.SourceFile)
            {
                node.Flags |= _emitFlags;
            }
            if (_currentReturnTarget is not null)
            {
                AddAntecedent(_currentReturnTarget, _currentFlow);
                _currentFlow = FinishFlowLabel(_currentReturnTarget);
                if (node.Kind == Kind.Constructor || node.Kind == Kind.ClassStaticBlockDeclaration)
                {
                    SetReturnFlowNode(node, _currentFlow);
                }
            }
            if (!isImmediatelyInvoked)
            {
                _currentFlow = saveCurrentFlow;
            }
            _currentBreakTarget = saveBreakTarget;
            _currentContinueTarget = saveContinueTarget;
            _currentReturnTarget = saveReturnTarget;
            _currentExceptionTarget = saveExceptionTarget;
            _activeLabelList = saveActiveLabelList;
            _hasExplicitReturn = saveHasExplicitReturn;
            if ((containerFlags & ContainerFlags.PropagatesThisKeyword) != 0)
            {
                _seenThisKeyword = saveSeenThisKeyword || _seenThisKeyword;
            }
            else
            {
                _seenThisKeyword = saveSeenThisKeyword;
            }
        }
        else if ((containerFlags & ContainerFlags.IsInterface) != 0)
        {
            var saveSeenThisKeyword = _seenThisKeyword;
            _seenThisKeyword = false;
            BindChildren(node);
            // ContainsThis cannot overlap with HasExtendedUnicodeEscape on Identifier
            if (_seenThisKeyword)
            {
                node.Flags |= NodeFlags.ContainsThis;
            }
            else
            {
                node.Flags &= ~NodeFlags.ContainsThis;
            }
            _seenThisKeyword = saveSeenThisKeyword;
        }
        else
        {
            BindChildren(node);
        }

        if (IsSourceFile(node) && IsInJSFile(node))
        {
            // Binding of top-level JSTypeAliasDeclaration nodes is deferred to ensure CommonJS module
            // indicators, if any, are processed first.
            // Go: `for _, statement := range node.Statements()`（nil 切片不迭代）
            foreach (var statement in node.Statements() ?? [])
            {
                if (IsJSTypeAliasDeclaration(statement))
                {
                    BindBlockScopedDeclaration(statement, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
                }
            }
            if (_file.CommonJSModuleIndicator is not null)
            {
                DeclareCommonJSVariable("module");
                DeclareCommonJSVariable("exports");
            }
        }
        if (IsSourceFile(node) && IsExternalOrCommonJSModule(node.AsSourceFile()) || IsAmbientModule(node))
        {
            BindCommonJSTypeExports(node.Symbol!);
        }

        _container = saveContainer;
        _thisContainer = saveThisContainer;
        _blockScopeContainer = savedBlockScopeContainer;
    }

    private void DeclareCommonJSVariable(string name)
    {
        var locals = GetLocals(_file);
        if (locals.GetValueOrDefault(name) is null)
        {
            var symbol = NewSymbol(SymbolFlags.FunctionScopedVariable | SymbolFlags.ModuleExports, name);
            symbol.Declarations = NewSingleDeclaration(_file);
            symbol.ValueDeclaration = symbol.Declarations![0];
            if (name == "module")
            {
                var exportsProperty = NewSymbol(SymbolFlags.ModuleExports | SymbolFlags.Property, "exports");
                exportsProperty.Declarations = symbol.Declarations;
                exportsProperty.ValueDeclaration = symbol.ValueDeclaration;
                exportsProperty.Parent = symbol;
                symbol.Members = new SymbolTable();
                symbol.Members!["exports"] = exportsProperty;
            }
            locals[name] = symbol;
        }
    }

    private void BindChildren(Node node)
    {
        var saveInAssignmentPattern = _inAssignmentPattern;
        // Most nodes aren't valid in an assignment pattern, so we clear the value here
        // and set it before we descend into nodes that could actually be part of an assignment pattern.
        _inAssignmentPattern = false;

        if (_currentFlow == _unreachableFlow)
        {
            var flowNodeData = node.FlowNodeData();
            if (flowNodeData is not null)
            {
                flowNodeData.FlowNode = null;
            }
            if (IsPotentiallyExecutableNode(node))
            {
                node.Flags |= NodeFlags.Unreachable;
            }
            BindEachChild(node);
            _inAssignmentPattern = saveInAssignmentPattern;
            return;
        }

        if (Kind.FirstStatement <= node.Kind && node.Kind <= Kind.LastStatement)
        {
            var flowNodeData = node.FlowNodeData();
            if (flowNodeData is not null)
            {
                flowNodeData.FlowNode = _currentFlow;
            }
        }

        switch (node.Kind)
        {
            case Kind.WhileStatement:
                BindWhileStatement(node);
                break;
            case Kind.DoStatement:
                BindDoStatement(node);
                break;
            case Kind.ForStatement:
                BindForStatement(node);
                break;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                BindForInOrForOfStatement(node);
                break;
            case Kind.IfStatement:
                BindIfStatement(node);
                break;
            case Kind.ReturnStatement:
                BindReturnStatement(node);
                break;
            case Kind.ThrowStatement:
                BindThrowStatement(node);
                break;
            case Kind.BreakStatement:
                BindBreakStatement(node);
                break;
            case Kind.ContinueStatement:
                BindContinueStatement(node);
                break;
            case Kind.TryStatement:
                BindTryStatement(node);
                break;
            case Kind.SwitchStatement:
                BindSwitchStatement(node);
                break;
            case Kind.CaseBlock:
                BindCaseBlock(node);
                break;
            case Kind.CaseClause:
            case Kind.DefaultClause:
                BindCaseOrDefaultClause(node);
                break;
            case Kind.ExpressionStatement:
                BindExpressionStatement(node);
                break;
            case Kind.LabeledStatement:
                BindLabeledStatement(node);
                break;
            case Kind.PrefixUnaryExpression:
                BindPrefixUnaryExpressionFlow(node);
                break;
            case Kind.PostfixUnaryExpression:
                BindPostfixUnaryExpressionFlow(node);
                break;
            case Kind.BinaryExpression:
                if (IsDestructuringAssignment(node))
                {
                    // Carry over whether we are in an assignment pattern to
                    // binary expressions that could actually be an initializer
                    _inAssignmentPattern = saveInAssignmentPattern;
                    BindDestructuringAssignmentFlow(node);
                    return;
                }
                BindBinaryExpressionFlow(node);
                break;
            case Kind.DeleteExpression:
                BindDeleteExpressionFlow(node);
                break;
            case Kind.ConditionalExpression:
                BindConditionalExpressionFlow(node);
                break;
            case Kind.VariableDeclaration:
                BindVariableDeclarationFlow(node);
                break;
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                BindAccessExpressionFlow(node);
                break;
            case Kind.CallExpression:
                BindCallExpressionFlow(node);
                break;
            case Kind.NonNullExpression:
                BindNonNullExpressionFlow(node);
                break;
            case Kind.SourceFile:
                var sourceFile = node.AsSourceFile()!;
                BindEachStatementFunctionsFirst(sourceFile.Statements);
                Bind(sourceFile.EndOfFileToken);
                break;
            case Kind.Block:
            case Kind.ModuleBlock:
                BindEachStatementFunctionsFirst(node.StatementList());
                break;
            case Kind.BindingElement:
                BindBindingElementFlow(node);
                break;
            case Kind.Parameter:
                BindParameterFlow(node);
                break;
            case Kind.ObjectLiteralExpression:
            case Kind.ArrayLiteralExpression:
            case Kind.PropertyAssignment:
            case Kind.SpreadElement:
                _inAssignmentPattern = saveInAssignmentPattern;
                BindEachChild(node);
                break;
            default:
                BindEachChild(node);
                break;
        }
        _inAssignmentPattern = saveInAssignmentPattern;
    }

    private void BindEachChild(Node node)
    {
        node.ForEachChild(_bindFunc);
    }

    private void BindEach(Node[]? nodes)
    {
        if (nodes is not null)
        {
            for (var i = 0; i < nodes.Length; i++)
            {
                Bind(nodes[i]);
            }
        }
    }

    private void BindNodeList(NodeList? nodeList)
    {
        if (nodeList is not null)
        {
            BindEach(nodeList.Nodes);
        }
    }

    private void BindModifiers(ModifierList? modifiers)
    {
        if (modifiers is not null)
        {
            BindEach(modifiers.Nodes);
        }
    }

    private void BindEachStatementFunctionsFirst(NodeList? statements)
    {
        var nodes = statements!.Nodes ?? [];
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Kind == Kind.FunctionDeclaration)
            {
                Bind(nodes[i]);
            }
        }
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Kind != Kind.FunctionDeclaration)
            {
                Bind(nodes[i]);
            }
        }
    }

    private FlowNode? SetContinueTarget(Node node, FlowNode? target)
    {
        var label = _activeLabelList;
        while (label is not null && node.Parent!.Kind == Kind.LabeledStatement)
        {
            label._continueTarget = target;
            label = label._next;
            node = node.Parent!;
        }
        return target;
    }
}

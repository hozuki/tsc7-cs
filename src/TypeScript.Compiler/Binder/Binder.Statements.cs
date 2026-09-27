// ported from: internal/binder/binder.go:1790-2195（语句与控制流：doWithConditionalBranches / bindCondition /
//   bindIterativeStatement / isLogicalAssignmentExpression / bindAssignmentTargetFlow / bindDestructuringTargetFlow /
//   while・do・for・for-in/for-of・if・return・throw・break・continue・try・switch・caseBlock・caseOrDefaultClause /
//   expressionStatement / labeledStatement）
//
// 与 Go 的偏离（逐条；其余地方与 Go 逐行对应）：
// 1) 包级函数 `isLogicalAssignmentExpression` → `private static`；其余 `(b *Binder)` 未导出方法 → `private` 实例方法
//    （方法名 PascalCase），见 CONTRACT.md §1。
// 2) 回调：Go 的方法表达式 `(*Binder).bind` / `(*ActiveLabel).BreakTarget` / `(*ActiveLabel).ContinueTarget` 在 C# 里没有
//    对应物（实例方法组会绑定到 this，参数个数对不上 `Func<Binder, Node, bool>` / `Func<ActiveLabel, FlowNode?>`），
//    一律写 lambda：`(bb, n) => bb.Bind(n)`、`l => l.BreakTarget()`（同 Parser.Declarations.cs 文件头第 2 条）。
// 3) 可空性：`bindCondition(node *ast.Node, …)` 与 `bindIterativeStatement(node *ast.Node, …)` 在 Go 里靠 `bind(nil) == false`
//    容忍 nil（`ForStatement.Condition`/`Initializer` 缺省时就是 nil），落成 `Node?`；`doWithConditionalBranches` 的 value
//    参数 Go 不做 nil 检查，保持 `Node`（bindCondition 调用点写 `node!`，`Bind` 本身接受 `Node?`，见 CONTRACT.md §1.2）。
// 4) nil 切片：Go 的 `for … range node.Elements()` 与 `len(clauses)` 对 nil 切片等价于空切片，而 C# 的
//    `NodeList.Nodes`/`Node.Elements()` 是 `Node[]?`：`Elements()`/`Properties()` 用 `?? []` 归一；
//    `clauses[i].Statements()?.Length ?? 0` 对应 `len(clauses[i].Statements()) == 0`（docs/architecture.md §3.2）。
// 5) Go 的 `switch` 每个 case 子句自带作用域，C# 的 switch 段共享作用域 → 声明了局部变量的 case 加一层 `{ }`
//    （纯作用域，语义不变）。
// 6) `!`（null-forgiving）只用于 Go 里"无条件解引用、由构造保证非 nil"的位置：`SetContinueTarget(...)` 的返回值
//    （Go 返回 `label.breakTarget`/`target`，两者都非 nil）、`As*()` 的结果、`Node.Parent`、各语句的非空子节点、
//    `_currentFlow`（Go 直接写 `b.currentFlow.Flags`）、以及刚在同一条件里做过 `is not null` 判定的
//    `_currentReturnTarget`/`_currentExceptionTarget`/`target`。
// 7) 热路径（bindAssignmentTargetFlow / bindCaseBlock）用索引循环，不用 LINQ/foreach（docs/architecture.md §3.8）。
namespace TypeScript.Binder;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Binder
{
    private void DoWithConditionalBranches(Func<Binder, Node, bool> action, Node value, FlowNode? trueTarget, FlowNode? falseTarget)
    {
        var savedTrueTarget = _currentTrueTarget;
        var savedFalseTarget = _currentFalseTarget;
        _currentTrueTarget = trueTarget;
        _currentFalseTarget = falseTarget;
        action(this, value);
        _currentTrueTarget = savedTrueTarget;
        _currentFalseTarget = savedFalseTarget;
    }

    private void BindCondition(Node? node, FlowNode? trueTarget, FlowNode? falseTarget)
    {
        DoWithConditionalBranches((bb, n) => bb.Bind(n), node!, trueTarget, falseTarget);
        if (node is null || (!IsLogicalAssignmentExpression(node) && !IsLogicalExpression(node) && !(IsOptionalChain(node) && IsOutermostOptionalChain(node))))
        {
            AddAntecedent(trueTarget!, CreateFlowCondition(FlowFlags.TrueCondition, _currentFlow, node));
            AddAntecedent(falseTarget!, CreateFlowCondition(FlowFlags.FalseCondition, _currentFlow, node));
        }
    }

    private void BindIterativeStatement(Node? node, FlowNode? breakTarget, FlowNode? continueTarget)
    {
        var saveBreakTarget = _currentBreakTarget;
        var saveContinueTarget = _currentContinueTarget;
        _currentBreakTarget = breakTarget;
        _currentContinueTarget = continueTarget;
        Bind(node);
        _currentBreakTarget = saveBreakTarget;
        _currentContinueTarget = saveContinueTarget;
    }

    private static bool IsLogicalAssignmentExpression(Node node)
    {
        return IsLogicalOrCoalescingAssignmentExpression(SkipParentheses(node));
    }

    private void BindAssignmentTargetFlow(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ArrayLiteralExpression:
            {
                var elements = node.Elements() ?? [];
                for (var i = 0; i < elements.Length; i++)
                {
                    var e = elements[i];
                    if (e.Kind == Kind.SpreadElement)
                    {
                        BindAssignmentTargetFlow(e.Expression()!);
                    }
                    else
                    {
                        BindDestructuringTargetFlow(e);
                    }
                }
                break;
            }
            case Kind.ObjectLiteralExpression:
            {
                var properties = node.Properties() ?? [];
                for (var i = 0; i < properties.Length; i++)
                {
                    var p = properties[i];
                    switch (p.Kind)
                    {
                        case Kind.PropertyAssignment:
                            BindDestructuringTargetFlow(p.Initializer()!);
                            break;
                        case Kind.ShorthandPropertyAssignment:
                            BindAssignmentTargetFlow(p.AsShorthandPropertyAssignment()!.Name()!);
                            break;
                        case Kind.SpreadAssignment:
                            BindAssignmentTargetFlow(p.Expression()!);
                            break;
                    }
                }
                break;
            }
            default:
                if (IsNarrowableReference(node))
                {
                    _currentFlow = CreateFlowMutation(FlowFlags.Assignment, _currentFlow, node);
                }
                break;
        }
    }

    private void BindDestructuringTargetFlow(Node node)
    {
        if (IsBinaryExpression(node) && node.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken)
        {
            BindAssignmentTargetFlow(node.AsBinaryExpression()!.Left!);
        }
        else
        {
            BindAssignmentTargetFlow(node);
        }
    }

    private void BindWhileStatement(Node node)
    {
        var stmt = node.AsWhileStatement()!;
        var preWhileLabel = SetContinueTarget(node, CreateLoopLabel())!;
        var preBodyLabel = CreateBranchLabel();
        var postWhileLabel = CreateBranchLabel();
        AddAntecedent(preWhileLabel, _currentFlow);
        _currentFlow = preWhileLabel;
        BindCondition(stmt.Expression, preBodyLabel, postWhileLabel);
        _currentFlow = FinishFlowLabel(preBodyLabel);
        BindIterativeStatement(stmt.Statement, postWhileLabel, preWhileLabel);
        AddAntecedent(preWhileLabel, _currentFlow);
        _currentFlow = FinishFlowLabel(postWhileLabel);
    }

    private void BindDoStatement(Node node)
    {
        var stmt = node.AsDoStatement()!;
        var preDoLabel = CreateLoopLabel();
        var preConditionLabel = SetContinueTarget(node, CreateBranchLabel())!;
        var postDoLabel = CreateBranchLabel();
        AddAntecedent(preDoLabel, _currentFlow);
        _currentFlow = preDoLabel;
        BindIterativeStatement(stmt.Statement, postDoLabel, preConditionLabel);
        AddAntecedent(preConditionLabel, _currentFlow);
        _currentFlow = FinishFlowLabel(preConditionLabel);
        BindCondition(stmt.Expression, preDoLabel, postDoLabel);
        _currentFlow = FinishFlowLabel(postDoLabel);
    }

    private void BindForStatement(Node node)
    {
        var stmt = node.AsForStatement()!;
        Bind(stmt.Initializer);
        if (_currentFlow == _unreachableFlow)
        {
            // Unlike while/do, the for-loop initializer is bound inside this function before the loop's
            // flow graph is constructed. If it makes flow unreachable (e.g. a throwing IIFE), addAntecedent
            // will filter out the unreachable entry to preLoopLabel, leaving only the back-edge from the
            // incrementor. This creates a cycle with no exit that crashes isReachableFlowNodeWorker.
            // Bail out early and just bind the remaining children with unreachable flow.
            Bind(stmt.Condition);
            Bind(stmt.Statement);
            Bind(stmt.Incrementor);
            return;
        }
        var preLoopLabel = SetContinueTarget(node, CreateLoopLabel())!;
        var preBodyLabel = CreateBranchLabel();
        var preIncrementorLabel = CreateBranchLabel();
        var postLoopLabel = CreateBranchLabel();
        AddAntecedent(preLoopLabel, _currentFlow);
        _currentFlow = preLoopLabel;
        BindCondition(stmt.Condition, preBodyLabel, postLoopLabel);
        _currentFlow = FinishFlowLabel(preBodyLabel);
        BindIterativeStatement(stmt.Statement, postLoopLabel, preIncrementorLabel);
        AddAntecedent(preIncrementorLabel, _currentFlow);
        _currentFlow = FinishFlowLabel(preIncrementorLabel);
        Bind(stmt.Incrementor);
        AddAntecedent(preLoopLabel, _currentFlow);
        _currentFlow = FinishFlowLabel(postLoopLabel);
    }

    private void BindForInOrForOfStatement(Node node)
    {
        var stmt = node.AsForInOrOfStatement()!;
        Bind(stmt.Expression);
        if (_currentFlow == _unreachableFlow)
        {
            // Like the for-loop initializer, the for-in/for-of expression is bound before the loop's
            // flow graph is constructed. If it makes flow unreachable (e.g. a throwing IIFE), addAntecedent
            // will filter out the unreachable entry to preLoopLabel, leaving only the back-edge from the
            // loop body. This creates a cycle with no exit that crashes isReachableFlowNodeWorker.
            // Bail out early and just bind the remaining children with unreachable flow.
            Bind(stmt.Initializer);
            Bind(stmt.Statement);
            return;
        }
        var preLoopLabel = SetContinueTarget(node, CreateLoopLabel())!;
        var postLoopLabel = CreateBranchLabel();
        AddAntecedent(preLoopLabel, _currentFlow);
        _currentFlow = preLoopLabel;
        if (node.Kind == Kind.ForOfStatement)
        {
            Bind(stmt.AwaitModifier);
        }
        AddAntecedent(postLoopLabel, _currentFlow);
        Bind(stmt.Initializer);
        if (stmt.Initializer!.Kind != Kind.VariableDeclarationList)
        {
            BindAssignmentTargetFlow(stmt.Initializer!);
        }
        BindIterativeStatement(stmt.Statement, postLoopLabel, preLoopLabel);
        AddAntecedent(preLoopLabel, _currentFlow);
        _currentFlow = FinishFlowLabel(postLoopLabel);
    }

    private void BindIfStatement(Node node)
    {
        var stmt = node.AsIfStatement()!;
        var thenLabel = CreateBranchLabel();
        var elseLabel = CreateBranchLabel();
        var postIfLabel = CreateBranchLabel();
        BindCondition(stmt.Expression, thenLabel, elseLabel);
        _currentFlow = FinishFlowLabel(thenLabel);
        Bind(stmt.ThenStatement);
        AddAntecedent(postIfLabel, _currentFlow);
        _currentFlow = FinishFlowLabel(elseLabel);
        Bind(stmt.ElseStatement);
        AddAntecedent(postIfLabel, _currentFlow);
        _currentFlow = FinishFlowLabel(postIfLabel);
    }

    private void BindReturnStatement(Node node)
    {
        Bind(node.Expression());
        if (_currentReturnTarget is not null)
        {
            AddAntecedent(_currentReturnTarget, _currentFlow);
        }
        _currentFlow = _unreachableFlow;
        _hasExplicitReturn = true;
        _hasFlowEffects = true;
    }

    private void BindThrowStatement(Node node)
    {
        Bind(node.Expression());
        _currentFlow = _unreachableFlow;
        _hasFlowEffects = true;
    }

    private void BindBreakStatement(Node node)
    {
        BindBreakOrContinueStatement(node.Label(), _currentBreakTarget, (ActiveLabel l) => l.BreakTarget());
    }

    private void BindContinueStatement(Node node)
    {
        BindBreakOrContinueStatement(node.Label(), _currentContinueTarget, (ActiveLabel l) => l.ContinueTarget());
    }

    private void BindBreakOrContinueStatement(Node? label, FlowNode? currentTarget, Func<ActiveLabel, FlowNode?> getTarget)
    {
        Bind(label);
        if (label is not null)
        {
            var activeLabel = FindActiveLabel(label.Text());
            if (activeLabel is not null)
            {
                activeLabel._referenced = true;
                BindBreakOrContinueFlow(getTarget(activeLabel));
            }
        }
        else
        {
            BindBreakOrContinueFlow(currentTarget);
        }
    }

    private ActiveLabel? FindActiveLabel(string name)
    {
        for (var label = _activeLabelList; label is not null; label = label._next)
        {
            if (label._name == name)
            {
                return label;
            }
        }
        return null;
    }

    private void BindBreakOrContinueFlow(FlowNode? flowLabel)
    {
        if (flowLabel is not null)
        {
            AddAntecedent(flowLabel, _currentFlow);
            _currentFlow = _unreachableFlow;
            _hasFlowEffects = true;
        }
    }

    private void BindTryStatement(Node node)
    {
        // We conservatively assume that *any* code in the try block can cause an exception, but we only need
        // to track code that causes mutations (because only mutations widen the possible control flow type of
        // a variable). The exceptionLabel is the target label for control flows that result from exceptions.
        // We add all mutation flow nodes as antecedents of this label such that we can analyze them as possible
        // antecedents of the start of catch or finally blocks. Furthermore, we add the current control flow to
        // represent exceptions that occur before any mutations.
        var stmt = node.AsTryStatement()!;
        var saveReturnTarget = _currentReturnTarget;
        var saveExceptionTarget = _currentExceptionTarget;
        var normalExitLabel = CreateBranchLabel();
        var returnLabel = CreateBranchLabel();
        var exceptionLabel = CreateBranchLabel();
        if (stmt.FinallyBlock is not null)
        {
            _currentReturnTarget = returnLabel;
        }
        AddAntecedent(exceptionLabel, _currentFlow);
        _currentExceptionTarget = exceptionLabel;
        Bind(stmt.TryBlock);
        AddAntecedent(normalExitLabel, _currentFlow);
        if (stmt.CatchClause is not null)
        {
            // Start of catch clause is the target of exceptions from try block.
            _currentFlow = FinishFlowLabel(exceptionLabel);
            // The currentExceptionTarget now represents control flows from exceptions in the catch clause.
            // Effectively, in a try-catch-finally, if an exception occurs in the try block, the catch block
            // acts like a second try block.
            exceptionLabel = CreateBranchLabel();
            AddAntecedent(exceptionLabel, _currentFlow);
            _currentExceptionTarget = exceptionLabel;
            Bind(stmt.CatchClause);
            AddAntecedent(normalExitLabel, _currentFlow);
        }
        _currentReturnTarget = saveReturnTarget;
        _currentExceptionTarget = saveExceptionTarget;
        if (stmt.FinallyBlock is not null)
        {
            // Possible ways control can reach the finally block:
            // 1) Normal completion of try block of a try-finally or try-catch-finally
            // 2) Normal completion of catch block (following exception in try block) of a try-catch-finally
            // 3) Return in try or catch block of a try-finally or try-catch-finally
            // 4) Exception in try block of a try-finally
            // 5) Exception in catch block of a try-catch-finally
            // When analyzing a control flow graph that starts inside a finally block we want to consider all
            // five possibilities above. However, when analyzing a control flow graph that starts outside (past)
            // the finally block, we only want to consider the first two (if we're past a finally block then it
            // must have completed normally). Likewise, when analyzing a control flow graph from return statements
            // in try or catch blocks in an IIFE, we only want to consider the third. To make this possible, we
            // inject a ReduceLabel node into the control flow graph. This node contains an alternate reduced
            // set of antecedents for the pre-finally label. As control flow analysis passes by a ReduceLabel
            // node, the pre-finally label is temporarily switched to the reduced antecedent set.
            var finallyLabel = CreateBranchLabel();
            finallyLabel.Antecedents = CombineFlowLists(normalExitLabel.Antecedents, CombineFlowLists(exceptionLabel.Antecedents, returnLabel.Antecedents));
            _currentFlow = finallyLabel;
            Bind(stmt.FinallyBlock);
            if ((_currentFlow!.Flags & FlowFlags.Unreachable) != 0)
            {
                // If the end of the finally block is unreachable, the end of the entire try statement is unreachable.
                _currentFlow = _unreachableFlow;
            }
            else
            {
                // If we have an IIFE return target and return statements in the try or catch blocks, add a control
                // flow that goes back through the finally block and back through only the return statements.
                if (_currentReturnTarget is not null && returnLabel.Antecedents is not null)
                {
                    AddAntecedent(_currentReturnTarget!, CreateReduceLabel(finallyLabel, returnLabel.Antecedents, _currentFlow));
                }
                // If we have an outer exception target (i.e. a containing try-finally or try-catch-finally), add a
                // control flow that goes back through the finally block and back through each possible exception source.
                if (_currentExceptionTarget is not null && exceptionLabel.Antecedents is not null)
                {
                    AddAntecedent(_currentExceptionTarget!, CreateReduceLabel(finallyLabel, exceptionLabel.Antecedents, _currentFlow));
                }
                // If the end of the finally block is reachable, but the end of the try and catch blocks are not,
                // convert the current flow to unreachable. For example, 'try { return 1; } finally { ... }' should
                // result in an unreachable current control flow.
                if (normalExitLabel.Antecedents is not null)
                {
                    _currentFlow = CreateReduceLabel(finallyLabel, normalExitLabel.Antecedents, _currentFlow);
                }
                else
                {
                    _currentFlow = _unreachableFlow;
                }
            }
        }
        else
        {
            _currentFlow = FinishFlowLabel(normalExitLabel);
        }
    }

    private void BindSwitchStatement(Node node)
    {
        var stmt = node.AsSwitchStatement()!;
        var postSwitchLabel = CreateBranchLabel();
        Bind(stmt.Expression);
        var saveBreakTarget = _currentBreakTarget;
        var savePreSwitchCaseFlow = _preSwitchCaseFlow;
        _currentBreakTarget = postSwitchLabel;
        _preSwitchCaseFlow = _currentFlow;
        Bind(stmt.CaseBlock);
        AddAntecedent(postSwitchLabel, _currentFlow);
        var hasDefault = Some(stmt.CaseBlock!.AsCaseBlock()!.Clauses!.Nodes, c => c.Kind == Kind.DefaultClause);
        if (!hasDefault)
        {
            AddAntecedent(postSwitchLabel, CreateFlowSwitchClause(_preSwitchCaseFlow, node, 0, 0));
        }
        _currentBreakTarget = saveBreakTarget;
        _preSwitchCaseFlow = savePreSwitchCaseFlow;
        _currentFlow = FinishFlowLabel(postSwitchLabel);
    }

    private void BindCaseBlock(Node node)
    {
        var switchStatement = node.Parent!;
        var clauses = node.AsCaseBlock()!.Clauses!.Nodes ?? [];
        var isNarrowingSwitch = switchStatement.Expression()!.Kind == Kind.TrueKeyword || IsNarrowingExpression(switchStatement.Expression()!);
        var fallthroughFlow = _unreachableFlow;
        for (var i = 0; i < clauses.Length; i++)
        {
            var clauseStart = i;
            while ((clauses[i].Statements()?.Length ?? 0) == 0 && i + 1 < clauses.Length)
            {
                if (fallthroughFlow == _unreachableFlow)
                {
                    _currentFlow = _preSwitchCaseFlow;
                }
                Bind(clauses[i]);
                i++;
            }
            var preCaseLabel = CreateBranchLabel();
            var preCaseFlow = _preSwitchCaseFlow;
            if (isNarrowingSwitch)
            {
                preCaseFlow = CreateFlowSwitchClause(_preSwitchCaseFlow, switchStatement, clauseStart, i + 1);
            }
            AddAntecedent(preCaseLabel, preCaseFlow);
            AddAntecedent(preCaseLabel, fallthroughFlow);
            _currentFlow = FinishFlowLabel(preCaseLabel);
            var clause = clauses[i];
            Bind(clause);
            fallthroughFlow = _currentFlow;
            if ((_currentFlow!.Flags & FlowFlags.Unreachable) == 0 && i != clauses.Length - 1)
            {
                clause.AsCaseOrDefaultClause()!.FallthroughFlowNode = _currentFlow;
            }
        }
    }

    private void BindCaseOrDefaultClause(Node node)
    {
        var clause = node.AsCaseOrDefaultClause()!;
        if (clause.Expression is not null)
        {
            var saveCurrentFlow = _currentFlow;
            _currentFlow = _preSwitchCaseFlow;
            Bind(clause.Expression);
            _currentFlow = saveCurrentFlow;
        }
        BindEach(clause.Statements!.Nodes);
    }

    private void BindExpressionStatement(Node node)
    {
        var stmt = node.AsExpressionStatement()!;
        Bind(stmt.Expression);
        MaybeBindExpressionFlowIfCall(stmt.Expression!);
    }

    private void MaybeBindExpressionFlowIfCall(Node node)
    {
        // A top level or comma expression call expression with a dotted function name and at least one argument
        // is potentially an assertion and is therefore included in the control flow.
        if (IsCallExpression(node))
        {
            if (node.Expression()!.Kind != Kind.SuperKeyword && IsDottedName(node.Expression()))
            {
                _currentFlow = CreateFlowCall(_currentFlow, node);
            }
        }
    }

    private void BindLabeledStatement(Node node)
    {
        var stmt = node.AsLabeledStatement()!;
        var postStatementLabel = CreateBranchLabel();
        _activeLabelList = new ActiveLabel
        {
            _next = _activeLabelList,
            _name = stmt.Label!.Text(),
            _breakTarget = postStatementLabel,
            _continueTarget = null,
            _referenced = false,
        };
        Bind(stmt.Label);
        Bind(stmt.Statement);
        if (!_activeLabelList!._referenced)
        {
            // Mark the label as unused; the checker will decide whether to report it
            stmt.Label!.Flags |= NodeFlags.Unreachable;
        }
        _activeLabelList = _activeLabelList!._next;
        AddAntecedent(postStatementLabel, _currentFlow);
        _currentFlow = FinishFlowLabel(postStatementLabel);
    }
}

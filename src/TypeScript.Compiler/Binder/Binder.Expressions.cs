// ported from: internal/binder/binder.go:2196-2559（表达式绑定：前缀/后缀一元、解构赋值、二元/逻辑、delete、
//   条件表达式、变量声明、可选链、调用、非空断言、绑定元素/参数；以及 setFlowNode / setReturnFlowNode /
//   isGeneratorFunctionExpression / addToContainerChain / addDeclarationToSymbol）
//
// 与 Go 的差异（其余逐行对应；逐条依据 docs/architecture.md §2/§3、docs/ast.md 与 CONTRACT.md）：
// - 包级函数 `setFlowNode`/`setReturnFlowNode`/`isGeneratorFunctionExpression` → 同一个 partial 类里的
//   `private static`（C# 没有自由函数，包级成员与 `(b *Binder)` 方法一起放进 `public sealed partial class Binder`）；
//   `addToContainerChain`/`addDeclarationToSymbol` 是 Go 的未导出**方法** → `private` 实例方法。
// - `setFlowNode` 的写法：Go 是 `data := node.FlowNodeData(); if data != nil { data.FlowNode = flowNode }`。
//   C# 里 `FlowNodeData()` 返回接口 `IFlowNodeBase?`（成员 `FlowNode? FlowNode { get; set; }`，由各节点类
//   显式接口实现转发到同名 public 字段），因此逐行写 `var data = node.FlowNodeData(); if (data is not null) …`。
//   **不**写 `node.AsMutable().FlowNode`：`AsMutable()` 返回 `Node`，而 `Node` 上并没有 `FlowNode` 成员
//   （docs/ast.md 的 AsMutable 条目只覆盖 SetModifiers/SetExpression/SetType/SetInitializer 四个 Set* 方法，
//   没有 SetFlowNode；`FlowNodeData()` 是既有且唯一的入口）。
// - Go 的方法表达式 `(*Binder).bind` / `(*Binder).bindOptionalChainRest` 在 C# 里没有对应物（实例方法组不能转成
//   `Func<Binder, Node, bool>`）→ 传给 `DoWithConditionalBranches` 时写 `(bb, n) => bb.M(n)`（与 Parser 各分片同一写法）。
// - 局部变量名 `operator` 与 C# 关键字冲突 → `operatorKind`（本文件仅此一处重命名，仅 BindBinaryExpressionFlow 内）。
// - Go 的 `var name *ast.Node`（switch 内赋值、switch 无 default）→ C# 局部必须显式初始化 `Node? name = null;`；
//   C# 也不允许非空 case 贯穿，每个 case 补 `break;`。
// - Go 的 `symbol.Flags &^= X` → `symbol.Flags &= ~X`；Go 里 `&` 的优先级高于 `!=`，C# 相反，所以
//   `(flags & Y) != 0` 一律显式加括号。
// - `core.IfElse(a, x, y)` → `IfElse(a, x, y)`（`using static TypeScript.Core.Core`）；`core.AppendIfUnique` → `AppendIfUnique`。
// - 可空性（CONTRACT.md §1.2 已固定 `Bind(Node?)`/`BindEach(Node[]?)`/`BindNodeList(NodeList?)`）：Go 的
//   `bind`/`bindEach` 显式容忍 nil（`if node == nil { return false }`），所以 `expr.Type`（BinaryExpression.Type
//   惯常为 nil）、`param.DotDotDotToken`、`node.QuestionDotToken()`、`node.Name()`、`node.Arguments()`、
//   `call.Expression`、`call.Arguments!.Nodes`、`elem.Initializer` 一律**不加** `!` 直接传给 Bind/BindEach/BindInitializer。
//   反过来，Go 会因 nil 解引用而 panic 的地方一律加 `!`（kind 判定或 Go 的不变量已证明非空）：
//   `expr.OperatorToken!.Kind`、`expr.Left!.Kind`、`node.DeclarationData()!.Symbol`、`node.Parent!.Parent`、
//   `_lastContainer.LocalsContainerData()!.NextContainer`、`SkipParentheses(call.Expression)!`（Go 里 `expr.Kind` 会 panic）、
//   `name.Elements()!`（IsBindingPattern 已成立）、`IsIdentifier(access.Name()!)`/`IsNarrowableOperand(x!)`（参数非空）。
namespace TypeScript.Binder;

using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Binder
{
    private void BindPrefixUnaryExpressionFlow(Node node)
    {
        var expr = node.AsPrefixUnaryExpression()!;
        if (expr.Operator == Kind.ExclamationToken)
        {
            var saveTrueTarget = _currentTrueTarget;
            _currentTrueTarget = _currentFalseTarget;
            _currentFalseTarget = saveTrueTarget;
            BindEachChild(node);
            _currentFalseTarget = _currentTrueTarget;
            _currentTrueTarget = saveTrueTarget;
        }
        else
        {
            BindEachChild(node);
            if (expr.Operator == Kind.PlusPlusToken || expr.Operator == Kind.MinusMinusToken)
            {
                BindAssignmentTargetFlow(expr.Operand!);
            }
        }
    }

    private void BindPostfixUnaryExpressionFlow(Node node)
    {
        var expr = node.AsPostfixUnaryExpression()!;
        BindEachChild(node);
        if (expr.Operator == Kind.PlusPlusToken || expr.Operator == Kind.MinusMinusToken)
        {
            BindAssignmentTargetFlow(expr.Operand!);
        }
    }

    private void BindDestructuringAssignmentFlow(Node node)
    {
        var expr = node.AsBinaryExpression()!;
        if (_inAssignmentPattern)
        {
            _inAssignmentPattern = false;
            Bind(expr.OperatorToken);
            Bind(expr.Right);
            _inAssignmentPattern = true;
            Bind(expr.Left);
            Bind(expr.Type);
        }
        else
        {
            _inAssignmentPattern = true;
            Bind(expr.Left);
            Bind(expr.Type);
            _inAssignmentPattern = false;
            Bind(expr.OperatorToken);
            Bind(expr.Right);
        }
        BindAssignmentTargetFlow(expr.Left!);
    }

    private void BindBinaryExpressionFlow(Node node)
    {
        var expr = node.AsBinaryExpression()!;
        var operatorKind = expr.OperatorToken!.Kind;
        if (IsLogicalOrCoalescingBinaryOperator(operatorKind) || IsLogicalOrCoalescingAssignmentOperator(operatorKind))
        {
            if (IsTopLevelLogicalExpression(node))
            {
                var postExpressionLabel = CreateBranchLabel();
                var saveCurrentFlow = _currentFlow;
                var saveHasFlowEffects = _hasFlowEffects;
                _hasFlowEffects = false;
                BindLogicalLikeExpression(node, postExpressionLabel, postExpressionLabel);
                if (_hasFlowEffects)
                {
                    _currentFlow = FinishFlowLabel(postExpressionLabel);
                }
                else
                {
                    _currentFlow = saveCurrentFlow;
                }
                _hasFlowEffects = _hasFlowEffects || saveHasFlowEffects;
            }
            else
            {
                BindLogicalLikeExpression(node, _currentTrueTarget, _currentFalseTarget);
            }
        }
        else
        {
            Bind(expr.Left);
            Bind(expr.Type);
            if (operatorKind == Kind.CommaToken)
            {
                MaybeBindExpressionFlowIfCall(expr.Left!);
            }
            Bind(expr.OperatorToken);
            Bind(expr.Right);
            if (operatorKind == Kind.CommaToken)
            {
                MaybeBindExpressionFlowIfCall(expr.Right!);
            }
            if (IsAssignmentOperator(operatorKind) && !IsAssignmentTarget(node))
            {
                BindAssignmentTargetFlow(expr.Left!);
                if (operatorKind == Kind.EqualsToken && expr.Left!.Kind == Kind.ElementAccessExpression)
                {
                    var elementAccess = expr.Left!.AsElementAccessExpression()!;
                    if (IsNarrowableOperand(elementAccess.Expression!))
                    {
                        _currentFlow = CreateFlowMutation(FlowFlags.ArrayMutation, _currentFlow, node);
                    }
                }
            }
        }
    }

    private void BindLogicalLikeExpression(Node node, FlowNode? trueTarget, FlowNode? falseTarget)
    {
        var expr = node.AsBinaryExpression()!;
        var preRightLabel = CreateBranchLabel();
        if (expr.OperatorToken!.Kind == Kind.AmpersandAmpersandToken || expr.OperatorToken!.Kind == Kind.AmpersandAmpersandEqualsToken)
        {
            BindCondition(expr.Left, preRightLabel, falseTarget);
        }
        else
        {
            BindCondition(expr.Left, trueTarget, preRightLabel);
        }
        _currentFlow = FinishFlowLabel(preRightLabel);
        Bind(expr.OperatorToken);
        if (IsLogicalOrCoalescingAssignmentOperator(expr.OperatorToken!.Kind))
        {
            DoWithConditionalBranches((bb, n) => bb.Bind(n), expr.Right!, trueTarget, falseTarget);
            BindAssignmentTargetFlow(expr.Left!);
            AddAntecedent(trueTarget!, CreateFlowCondition(FlowFlags.TrueCondition, _currentFlow, node));
            AddAntecedent(falseTarget!, CreateFlowCondition(FlowFlags.FalseCondition, _currentFlow, node));
        }
        else
        {
            BindCondition(expr.Right, trueTarget, falseTarget);
        }
    }

    private void BindDeleteExpressionFlow(Node node)
    {
        var expr = node.AsDeleteExpression()!;
        BindEachChild(node);
        if (expr.Expression!.Kind == Kind.PropertyAccessExpression)
        {
            BindAssignmentTargetFlow(expr.Expression!);
        }
    }

    private void BindConditionalExpressionFlow(Node node)
    {
        var expr = node.AsConditionalExpression()!;
        var trueLabel = CreateBranchLabel();
        var falseLabel = CreateBranchLabel();
        var postExpressionLabel = CreateBranchLabel();
        var saveCurrentFlow = _currentFlow;
        var saveHasFlowEffects = _hasFlowEffects;
        _hasFlowEffects = false;
        BindCondition(expr.Condition, trueLabel, falseLabel);
        _currentFlow = FinishFlowLabel(trueLabel);
        Bind(expr.QuestionToken);
        Bind(expr.WhenTrue);
        AddAntecedent(postExpressionLabel, _currentFlow);
        _currentFlow = FinishFlowLabel(falseLabel);
        Bind(expr.ColonToken);
        Bind(expr.WhenFalse);
        AddAntecedent(postExpressionLabel, _currentFlow);
        if (_hasFlowEffects)
        {
            _currentFlow = FinishFlowLabel(postExpressionLabel);
        }
        else
        {
            _currentFlow = saveCurrentFlow;
        }
        _hasFlowEffects = _hasFlowEffects || saveHasFlowEffects;
    }

    private void BindVariableDeclarationFlow(Node node)
    {
        BindEachChild(node);
        if (node.Initializer() is not null || IsForInOrOfStatement(node.Parent!.Parent))
        {
            BindInitializedVariableFlow(node);
        }
    }

    private void BindInitializedVariableFlow(Node node)
    {
        Node? name = null;
        switch (node.Kind)
        {
            case Kind.VariableDeclaration:
                name = node.AsVariableDeclaration()!.Name();
                break;
            case Kind.BindingElement:
                name = node.AsBindingElement()!.Name();
                break;
        }
        if (name is not null && IsBindingPattern(name))
        {
            foreach (var child in name.Elements() ?? [])
            {
                BindInitializedVariableFlow(child);
            }
        }
        else
        {
            _currentFlow = CreateFlowMutation(FlowFlags.Assignment, _currentFlow, node);
        }
    }

    private void BindAccessExpressionFlow(Node node)
    {
        if (IsOptionalChain(node))
        {
            BindOptionalChainFlow(node);
        }
        else
        {
            BindEachChild(node);
        }
    }

    private void BindOptionalChainFlow(Node node)
    {
        if (IsTopLevelLogicalExpression(node))
        {
            var postExpressionLabel = CreateBranchLabel();
            var saveCurrentFlow = _currentFlow;
            var saveHasFlowEffects = _hasFlowEffects;
            BindOptionalChain(node, postExpressionLabel, postExpressionLabel);
            if (_hasFlowEffects)
            {
                _currentFlow = FinishFlowLabel(postExpressionLabel);
            }
            else
            {
                _currentFlow = saveCurrentFlow;
            }
            _hasFlowEffects = _hasFlowEffects || saveHasFlowEffects;
        }
        else
        {
            BindOptionalChain(node, _currentTrueTarget, _currentFalseTarget);
        }
    }

    private void BindOptionalChain(Node node, FlowNode? trueTarget, FlowNode? falseTarget)
    {
        // For an optional chain, we emulate the behavior of a logical expression:
        //
        // a?.b         -> a && a.b
        // a?.b.c       -> a && a.b.c
        // a?.b?.c      -> a && a.b && a.b.c
        // a?.[x = 1]   -> a && a[x = 1]
        //
        // To do this we descend through the chain until we reach the root of a chain (the expression with a `?.`)
        // and build it's CFA graph as if it were the first condition (`a && ...`). Then we bind the rest
        // of the node as part of the "true" branch, and continue to do so as we ascend back up to the outermost
        // chain node. We then treat the entire node as the right side of the expression.
        FlowNode? preChainLabel = null;
        if (IsOptionalChainRoot(node))
        {
            preChainLabel = CreateBranchLabel();
        }
        BindOptionalExpression(node.Expression()!, IfElse(preChainLabel is not null, preChainLabel, trueTarget), falseTarget);
        if (preChainLabel is not null)
        {
            _currentFlow = FinishFlowLabel(preChainLabel);
        }
        DoWithConditionalBranches((bb, n) => bb.BindOptionalChainRest(n), node, trueTarget, falseTarget);
        if (IsOutermostOptionalChain(node))
        {
            AddAntecedent(trueTarget!, CreateFlowCondition(FlowFlags.TrueCondition, _currentFlow, node));
            AddAntecedent(falseTarget!, CreateFlowCondition(FlowFlags.FalseCondition, _currentFlow, node));
        }
    }

    private void BindOptionalExpression(Node node, FlowNode? trueTarget, FlowNode? falseTarget)
    {
        DoWithConditionalBranches((bb, n) => bb.Bind(n), node, trueTarget, falseTarget);
        if (!IsOptionalChain(node) || IsOutermostOptionalChain(node))
        {
            AddAntecedent(trueTarget!, CreateFlowCondition(FlowFlags.TrueCondition, _currentFlow, node));
            AddAntecedent(falseTarget!, CreateFlowCondition(FlowFlags.FalseCondition, _currentFlow, node));
        }
    }

    private bool BindOptionalChainRest(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PropertyAccessExpression:
                Bind(node.QuestionDotToken());
                Bind(node.Name());
                break;
            case Kind.ElementAccessExpression:
                Bind(node.QuestionDotToken());
                Bind(node.AsElementAccessExpression()!.ArgumentExpression);
                break;
            case Kind.CallExpression:
                Bind(node.QuestionDotToken());
                BindNodeList(node.TypeArgumentList());
                BindEach(node.Arguments());
                break;
        }
        return false;
    }

    private void BindCallExpressionFlow(Node node)
    {
        var call = node.AsCallExpression()!;
        if (IsOptionalChain(node))
        {
            BindOptionalChainFlow(node);
        }
        else
        {
            // If the target of the call expression is a function expression or arrow function we have
            // an immediately invoked function expression (IIFE). Initialize the flowNode property to
            // the current control flow (which includes evaluation of the IIFE arguments).
            var expr = SkipParentheses(call.Expression)!;
            if (expr.Kind == Kind.FunctionExpression || expr.Kind == Kind.ArrowFunction)
            {
                BindNodeList(call.TypeArguments);
                BindEach(call.Arguments!.Nodes);
                Bind(call.Expression);
            }
            else
            {
                BindEachChild(node);
                if (call.Expression!.Kind == Kind.SuperKeyword)
                {
                    _currentFlow = CreateFlowCall(_currentFlow, node);
                }
            }
        }
        if (IsPropertyAccessExpression(call.Expression!))
        {
            var access = call.Expression!.AsPropertyAccessExpression()!;
            if (IsIdentifier(access.Name()!) && IsNarrowableOperand(access.Expression!) && IsPushOrUnshiftIdentifier(access.Name()))
            {
                _currentFlow = CreateFlowMutation(FlowFlags.ArrayMutation, _currentFlow, node);
            }
        }
    }

    private void BindNonNullExpressionFlow(Node node)
    {
        if (IsOptionalChain(node))
        {
            BindOptionalChainFlow(node);
        }
        else
        {
            BindEachChild(node);
        }
    }

    private void BindBindingElementFlow(Node node)
    {
        // When evaluating a binding pattern, the initializer is evaluated before the binding pattern, per:
        // - https://tc39.es/ecma262/#sec-destructuring-binding-patterns-runtime-semantics-iteratorbindinginitialization
        //   - `BindingElement: BindingPattern Initializer?`
        // - https://tc39.es/ecma262/#sec-runtime-semantics-keyedbindinginitialization
        //   - `BindingElement: BindingPattern Initializer?`
        var elem = node.AsBindingElement()!;
        Bind(elem.DotDotDotToken);
        Bind(elem.PropertyName);
        BindInitializer(elem.Initializer);
        Bind(elem.Name());
    }

    private void BindParameterFlow(Node node)
    {
        var param = node.AsParameterDeclaration()!;
        BindModifiers(param.Modifiers());
        Bind(param.DotDotDotToken);
        Bind(param.QuestionToken);
        Bind(param.Type);
        BindInitializer(param.Initializer);
        Bind(param.Name());
    }

    // a BindingElement/Parameter does not have side effects if initializers are not evaluated and used. (see GH#49759)
    private void BindInitializer(Node? node)
    {
        if (node is null)
        {
            return;
        }
        var entryFlow = _currentFlow;
        Bind(node);
        if (entryFlow == _unreachableFlow || entryFlow == _currentFlow)
        {
            return;
        }
        var exitFlow = CreateBranchLabel();
        AddAntecedent(exitFlow, entryFlow);
        AddAntecedent(exitFlow, _currentFlow);
        _currentFlow = FinishFlowLabel(exitFlow);
    }

    private static void SetFlowNode(Node node, FlowNode? flowNode)
    {
        var data = node.FlowNodeData();
        if (data is not null)
        {
            data.FlowNode = flowNode;
        }
    }

    private static void SetReturnFlowNode(Node node, FlowNode? returnFlowNode)
    {
        switch (node.Kind)
        {
            case Kind.Constructor:
                node.AsConstructorDeclaration()!.ReturnFlowNode = returnFlowNode;
                break;
            case Kind.FunctionDeclaration:
                node.AsFunctionDeclaration()!.ReturnFlowNode = returnFlowNode;
                break;
            case Kind.FunctionExpression:
                node.AsFunctionExpression()!.ReturnFlowNode = returnFlowNode;
                break;
            case Kind.ClassStaticBlockDeclaration:
                node.AsClassStaticBlockDeclaration()!.ReturnFlowNode = returnFlowNode;
                break;
        }
    }

    private static bool IsGeneratorFunctionExpression(Node node)
    {
        return IsFunctionExpression(node) && node.AsFunctionExpression()!.AsteriskToken is not null;
    }

    private void AddToContainerChain(Node? next)
    {
        if (_lastContainer is not null)
        {
            _lastContainer.LocalsContainerData()!.NextContainer = next;
        }
        _lastContainer = next;
    }

    private void AddDeclarationToSymbol(Symbol symbol, Node node, SymbolFlags symbolFlags)
    {
        symbol.Flags |= symbolFlags;
        node.DeclarationData()!.Symbol = symbol;
        if (symbol.Declarations is null)
        {
            symbol.Declarations = NewSingleDeclaration(node);
        }
        else
        {
            symbol.Declarations = AppendIfUnique(symbol.Declarations, node);
        }
        // On merge of const enum module with class or function, reset const enum only flag (namespaces will already recalculate)
        if ((symbol.Flags & SymbolFlags.ConstEnumOnlyModule) != 0 && (symbol.Flags & (SymbolFlags.Function | SymbolFlags.Class | SymbolFlags.RegularEnum)) != 0)
        {
            symbol.Flags &= ~SymbolFlags.ConstEnumOnlyModule;
            _notConstEnumOnlyModules.Add(symbol);
        }
        if ((symbolFlags & SymbolFlags.Value) != 0)
        {
            SetValueDeclaration(symbol, node);
        }
    }
}

// ported from: internal/checker/flow.go:2434-2761（类型谓词实参定位、构造函数/静态块内的 flow 类型、
//   可达性判定（isReachableFlowNode(Worker)）、isFalseExpression、super 之后的 flow 判定、
//   symbol 赋值标记（isSymbolAssignedDefinitely/isSymbolAssigned/isPastLastAssignment/
//   ensureAssignmentsMarked/hasParentWithAssignmentsMarked/markNodeAssignmentsWorker/extendAssignmentPosition））
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 字段 `_camelCase`；Go 的 map 读缺失键 → `TryGetValue`；`switch { case cond: }` → `if`/`else if` 链。
// 2) `math.MaxInt32` → `int.MaxValue`；`strings.HasPrefix` → `StringUtil.HasPrefix(s, prefix, caseInsensitive: false)`；
//    `strings.Index` → `string.IndexOf`。
// 3) `c.markNodeAssignments`（Checker 的 `Func<Node, bool>` 字段）传给 `Node.ForEachChild`（形参是 `Visitor`
//    委托）时需要 lambda：`n => _markNodeAssignments(n)`。
// 4) `switch` 的每个 case 用 `{ }` 包住，以便各分支复用 Go 里的局部名（如 `links`）。
// 5) 命中 artifacts/checker-method-collisions.md 的未导出方法用 `Worker` 后缀。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;

public sealed partial class Checker
{
    internal Node? GetTypePredicateArgument(TypePredicate predicate, Node callExpression)
    {
        if (predicate._kind == TypePredicateKind.Identifier || predicate._kind == TypePredicateKind.AssertsIdentifier)
        {
            var arguments = callExpression.Arguments() ?? Array.Empty<Node>();
            if (predicate._parameterIndex >= 0 && predicate._parameterIndex < arguments.Length)
            {
                return arguments[predicate._parameterIndex];
            }
        }
        else
        {
            var invokedExpression = SkipParentheses(callExpression.Expression())!;
            if (IsAccessExpression(invokedExpression))
            {
                return SkipParentheses(invokedExpression.Expression());
            }
        }
        return null;
    }

    internal Type? GetFlowTypeInConstructor(Symbol symbol, Node constructor)
    {
        Node accessName;
        if (HasPrefix(symbol.Name, InternalSymbolNamePrefix + "#", false))
        {
            accessName = _factory.NewPrivateIdentifier(symbol.Name[(symbol.Name.IndexOf('@') + 1)..]);
        }
        else
        {
            accessName = _factory.NewIdentifier(symbol.Name);
        }
        var reference = _factory.NewPropertyAccessExpression(_factory.NewKeywordExpression(Kind.ThisKeyword), null, accessName, NodeFlags.None);
        reference.Expression()!.Parent = reference;
        reference.Parent = constructor;
        reference.FlowNodeData()!.FlowNode = constructor.AsConstructorDeclaration()!.ReturnFlowNode;
        var flowType = GetFlowTypeOfProperty(reference, symbol);
        if (_noImplicitAny && (flowType == _autoType || flowType == _autoArrayType))
        {
            Error(symbol.ValueDeclaration, Diagnostics.Member_0_implicitly_has_an_1_type, SymbolToStringWorker(symbol), TypeToString(flowType));
        }
        // We don't infer a type if assignments are only null or undefined.
        if (EveryType(flowType, IsNullableType))
        {
            return null;
        }
        return ConvertAutoToAny(flowType);
    }

    internal Type? GetFlowTypeInStaticBlocks(Symbol symbol, Node[] staticBlocks)
    {
        Node accessName;
        if (HasPrefix(symbol.Name, InternalSymbolNamePrefix + "#", false))
        {
            accessName = _factory.NewPrivateIdentifier(symbol.Name[(symbol.Name.IndexOf('@') + 1)..]);
        }
        else
        {
            accessName = _factory.NewIdentifier(symbol.Name);
        }
        foreach (var staticBlock in staticBlocks)
        {
            var reference = _factory.NewPropertyAccessExpression(_factory.NewKeywordExpression(Kind.ThisKeyword), null, accessName, NodeFlags.None);
            reference.Expression()!.Parent = reference;
            reference.Parent = staticBlock;
            reference.FlowNodeData()!.FlowNode = staticBlock.AsClassStaticBlockDeclaration()!.ReturnFlowNode;
            var flowType = GetFlowTypeOfProperty(reference, symbol);
            if (_noImplicitAny && (flowType == _autoType || flowType == _autoArrayType))
            {
                Error(symbol.ValueDeclaration, Diagnostics.Member_0_implicitly_has_an_1_type, SymbolToStringWorker(symbol), TypeToString(flowType));
            }
            // We don't infer a type if assignments are only null or undefined.
            if (EveryType(flowType, IsNullableType))
            {
                continue;
            }
            return ConvertAutoToAny(flowType);
        }
        return null;
    }

    internal bool IsReachableFlowNode(FlowNode flow)
    {
        var f = GetFlowState();
        var result = IsReachableFlowNodeWorker(f, flow, false /*noCacheCheck*/);
        PutFlowState(f);
        _lastFlowNode = flow;
        _lastFlowNodeReachable = result;
        return result;
    }

    internal bool IsReachableFlowNodeWorker(FlowState f, FlowNode flow, bool noCacheCheck)
    {
        while (true)
        {
            if (flow == _lastFlowNode)
            {
                return _lastFlowNodeReachable;
            }
            var flags = flow.Flags;
            if ((flags & FlowFlags.Shared) != 0)
            {
                if (!noCacheCheck && f._reduceLabels.Count == 0)
                {
                    if (_flowNodeReachable.TryGetValue(flow, out var reachable))
                    {
                        return reachable;
                    }
                    var reachableResult = IsReachableFlowNodeWorker(f, flow, true /*noCacheCheck*/);
                    _flowNodeReachable[flow] = reachableResult;
                    return reachableResult;
                }
                noCacheCheck = false;
            }
            if ((flags & (FlowFlags.Assignment | FlowFlags.Condition | FlowFlags.ArrayMutation)) != 0)
            {
                flow = flow.Antecedent!;
            }
            else if ((flags & FlowFlags.Call) != 0)
            {
                var signature = GetEffectsSignature(flow.Node!);
                if (signature != null)
                {
                    var predicate = GetTypePredicateOfSignatureWorker(signature);
                    if (predicate != null && predicate._kind == TypePredicateKind.AssertsIdentifier && predicate._t == null)
                    {
                        var arguments = flow.Node!.Arguments() ?? Array.Empty<Node>();
                        if (predicate._parameterIndex >= 0 && predicate._parameterIndex < arguments.Length && IsFalseExpression(arguments[predicate._parameterIndex]))
                        {
                            return false;
                        }
                    }
                    if ((GetReturnTypeOfSignatureWorker(signature)._flags & TypeFlags.Never) != 0)
                    {
                        return false;
                    }
                }
                flow = flow.Antecedent!;
            }
            else if ((flags & FlowFlags.BranchLabel) != 0)
            {
                // A branching point is reachable if any branch is reachable.
                for (var list = GetBranchLabelAntecedents(flow, f._reduceLabels); list != null; list = list.Next)
                {
                    if (IsReachableFlowNodeWorker(f, list.Flow!, false /*noCacheCheck*/))
                    {
                        return true;
                    }
                }
                return false;
            }
            else if ((flags & FlowFlags.LoopLabel) != 0)
            {
                if (flow.Antecedents == null)
                {
                    return false;
                }
                // A loop is reachable if the control flow path that leads to the top is reachable.
                flow = flow.Antecedents.Flow!;
            }
            else if ((flags & FlowFlags.SwitchClause) != 0)
            {
                // The control flow path representing an unmatched value in a switch statement with
                // no default clause is unreachable if the switch statement is exhaustive.
                var data = flow.Node!.AsFlowSwitchClauseData();
                if (data.ClauseStart == data.ClauseEnd && IsExhaustiveSwitchStatement(data.SwitchStatement!))
                {
                    return false;
                }
                flow = flow.Antecedent!;
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                // Cache is unreliable once we start adjusting labels
                _lastFlowNode = null;
                f._reduceLabels.Add(flow.Node!.AsFlowReduceLabelData());
                var result = IsReachableFlowNodeWorker(f, flow.Antecedent!, false /*noCacheCheck*/);
                f._reduceLabels.RemoveAt(f._reduceLabels.Count - 1);
                return result;
            }
            else
            {
                return (flags & FlowFlags.Unreachable) == 0;
            }
        }
    }

    internal bool IsFalseExpression(Node expr)
    {
        var node = SkipParentheses(expr)!;
        if (node.Kind == Kind.FalseKeyword)
        {
            return true;
        }
        if (IsBinaryExpression(node))
        {
            var binary = node.AsBinaryExpression()!;
            return binary.OperatorToken!.Kind == Kind.AmpersandAmpersandToken && (IsFalseExpression(binary.Left!) || IsFalseExpression(binary.Right!)) ||
                binary.OperatorToken!.Kind == Kind.BarBarToken && IsFalseExpression(binary.Left!) && IsFalseExpression(binary.Right!);
        }
        return false;
    }

    // Return true if the given flow node is preceded by a 'super(...)' call in every possible code path
    // leading to the node.
    internal bool IsPostSuperFlowNode(FlowNode flow, bool noCacheCheck)
    {
        var f = GetFlowState();
        var result = IsPostSuperFlowNodeWorker(f, flow, noCacheCheck);
        PutFlowState(f);
        return result;
    }

    internal bool IsPostSuperFlowNodeWorker(FlowState f, FlowNode flow, bool noCacheCheck)
    {
        while (true)
        {
            var flags = flow.Flags;
            if ((flags & FlowFlags.Shared) != 0)
            {
                if (!noCacheCheck)
                {
                    if (_flowNodePostSuper.TryGetValue(flow, out var postSuper))
                    {
                        return postSuper;
                    }
                    var postSuperResult = IsPostSuperFlowNodeWorker(f, flow, true /*noCacheCheck*/);
                    _flowNodePostSuper[flow] = postSuperResult;
                }
                noCacheCheck = false;
            }
            if ((flags & (FlowFlags.Assignment | FlowFlags.Condition | FlowFlags.ArrayMutation | FlowFlags.SwitchClause)) != 0)
            {
                flow = flow.Antecedent!;
            }
            else if ((flags & FlowFlags.Call) != 0)
            {
                if (flow.Node!.Expression()!.Kind == Kind.SuperKeyword)
                {
                    return true;
                }
                flow = flow.Antecedent!;
            }
            else if ((flags & FlowFlags.BranchLabel) != 0)
            {
                for (var list = GetBranchLabelAntecedents(flow, f._reduceLabels); list != null; list = list.Next)
                {
                    if (!IsPostSuperFlowNodeWorker(f, list.Flow!, false /*noCacheCheck*/))
                    {
                        return false;
                    }
                }
                return true;
            }
            else if ((flags & FlowFlags.LoopLabel) != 0)
            {
                // A loop is post-super if the control flow path that leads to the top is post-super.
                flow = flow.Antecedents!.Flow!;
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                f._reduceLabels.Add(flow.Node!.AsFlowReduceLabelData());
                var result = IsPostSuperFlowNodeWorker(f, flow.Antecedent!, false /*noCacheCheck*/);
                f._reduceLabels.RemoveAt(f._reduceLabels.Count - 1);
                return result;
            }
            else
            {
                // Unreachable nodes are considered post-super to silence errors
                return (flags & FlowFlags.Unreachable) != 0;
            }
        }
    }

    // Check if a parameter, catch variable, or mutable local variable is definitely assigned anywhere
    internal bool IsSymbolAssignedDefinitely(Symbol symbol)
    {
        EnsureAssignmentsMarked(symbol);
        return _markedAssignmentSymbolLinks.Get(symbol)._hasDefiniteAssignment;
    }

    // Check if a parameter, catch variable, or mutable local variable is assigned anywhere
    internal bool IsSymbolAssigned(Symbol symbol)
    {
        EnsureAssignmentsMarked(symbol);
        return _markedAssignmentSymbolLinks.Get(symbol)._lastAssignmentPos != 0;
    }

    // Return true if there are no assignments to the given symbol or if the given location
    // is past the last assignment to the symbol.
    internal bool IsPastLastAssignment(Symbol symbol, Node? location)
    {
        EnsureAssignmentsMarked(symbol);
        var lastAssignmentPos = _markedAssignmentSymbolLinks.Get(symbol)._lastAssignmentPos;
        return lastAssignmentPos == 0 || location != null && lastAssignmentPos < location.Pos();
    }

    internal void EnsureAssignmentsMarked(Symbol symbol)
    {
        var parent = FindAncestor(symbol.ValueDeclaration, n => IsFunctionOrSourceFile(n));
        if (parent == null)
        {
            return;
        }
        var links = _nodeLinks.Get(parent);
        if ((links._flags & NodeCheckFlags.AssignmentsMarked) == 0)
        {
            links._flags |= NodeCheckFlags.AssignmentsMarked;
            if (!HasParentWithAssignmentsMarked(parent))
            {
                _markNodeAssignments(parent);
            }
        }
    }

    internal bool HasParentWithAssignmentsMarked(Node node)
    {
        return FindAncestor(node.Parent, n => IsFunctionOrSourceFile(n) && (_nodeLinks.Get(n)._flags & NodeCheckFlags.AssignmentsMarked) != 0) != null;
    }

    // For all assignments within the given root node, record the last assignment source position for all
    // referenced parameters and mutable local variables. When assignments occur in nested functions  or
    // references occur in export specifiers, record math.MaxInt32 as the assignment position. When
    // assignments occur in compound statements, record the ending source position of the compound statement
    // as the assignment position (this is more conservative than full control flow analysis, but requires
    // only a single walk over the AST).
    internal bool MarkNodeAssignmentsWorker(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
            {
                var assignmentKind = GetAssignmentTargetKind(node);
                if (assignmentKind != AssignmentKind.None)
                {
                    var symbol = GetResolvedSymbolWorker(node);
                    if (IsParameterOrMutableLocalVariable(symbol))
                    {
                        var links = _markedAssignmentSymbolLinks.Get(symbol);
                        if (links._lastAssignmentPos == 0 || links._lastAssignmentPos != int.MaxValue)
                        {
                            var referencingFunction = FindAncestor(node, n => IsFunctionOrSourceFile(n));
                            var declaringFunction = FindAncestor(symbol.ValueDeclaration, n => IsFunctionOrSourceFile(n));
                            if (referencingFunction == declaringFunction)
                            {
                                links._lastAssignmentPos = ExtendAssignmentPosition(node, symbol.ValueDeclaration!);
                            }
                            else
                            {
                                links._lastAssignmentPos = int.MaxValue;
                            }
                        }
                        if (assignmentKind == AssignmentKind.Definite)
                        {
                            links._hasDefiniteAssignment = true;
                        }
                    }
                }
                return false;
            }
            case Kind.ExportSpecifier:
            {
                var exportDeclaration = node.AsExportSpecifier()!.Parent!.Parent!.AsExportDeclaration()!;
                var name = node.PropertyNameOrName()!;
                if (!node.IsTypeOnly() && !exportDeclaration.IsTypeOnly && exportDeclaration.ModuleSpecifier == null && !IsStringLiteral(name))
                {
                    var symbol = ResolveEntityName(name, SymbolFlags.Value, true /*ignoreErrors*/, true /*dontResolveAlias*/, null);
                    if (symbol != null && IsParameterOrMutableLocalVariable(symbol))
                    {
                        var links = _markedAssignmentSymbolLinks.Get(symbol);
                        links._lastAssignmentPos = int.MaxValue;
                    }
                }
                return false;
            }
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.EnumDeclaration:
                return false;
        }
        if (IsTypeNode(node))
        {
            return false;
        }
        return node.ForEachChild(n => _markNodeAssignments(n));
    }

    // Extend the position of the given assignment target node to the end of any intervening variable statement,
    // expression statement, compound statement, or class declaration occurring between the node and the given
    // declaration node.
    internal int ExtendAssignmentPosition(Node node, Node declaration)
    {
        var pos = node.Pos();
        while (node != null && node.Pos() > declaration.Pos())
        {
            switch (node.Kind)
            {
                case Kind.VariableStatement:
                case Kind.ExpressionStatement:
                case Kind.IfStatement:
                case Kind.DoStatement:
                case Kind.WhileStatement:
                case Kind.ForStatement:
                case Kind.ForInStatement:
                case Kind.ForOfStatement:
                case Kind.WithStatement:
                case Kind.SwitchStatement:
                case Kind.TryStatement:
                case Kind.ClassDeclaration:
                    pos = node.End();
                    break;
            }
            node = node.Parent!;
        }
        return pos;
    }
}

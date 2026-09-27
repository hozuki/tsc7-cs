// ported from: internal/checker/flow.go:1-376（flow 图类型与状态：FlowType / SharedFlow / FlowState、
//   getFlowState / putFlowState、getFlowNodeOfNode、getFlowTypeOfReference(Ex)、getTypeAtFlowNode、
//   getBranchLabelAntecedents、getTypeAtFlowAssignment、getInitialOrAssignedType、isEmptyArrayAssignment、
//   getTypeAtFlowCall、narrowTypeByTypePredicate、narrowTypeByAssertion、getTypeAtFlowCondition）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出字段 → `_camelCase`（docs/checker-plan.md §5.5）；Go 未导出方法/包级函数 → PascalCase，
//    方法与包级函数都是 `internal`（checker 是紧耦合单包，跨 partial 文件互相调用）。
// 2) Go 的 `[]T` + append/切片截断（`s[:n]`）→ `List<T>` + `Add`/`RemoveRange`/`RemoveAt`（C# 数组没有 len<cap 视图，
//    docs/architecture.md §3.2）；Go 的 `slices.Contains` → `Array.IndexOf(...) >= 0`。
// 3) Go 的 `switch { case cond: }` 顺序求值 → C# `if`/`else if` 链（各分支条件互斥，语义等价）；Go 的 `fallthrough`
//    → C# `goto case`（见 Flow.Narrowing.cs / Flow.Keys.cs）。
// 4) Go 的 map 读缺失键返回零值 → C# `TryGetValue`（`Dictionary` 索引器读缺失键抛异常）。
// 5) 位运算优先级：Go 的 `f&X != 0` 是 `(f&X) != 0`，C# 的 `!=` 比 `&` 优先级高 → 一律显式加括号。
// 6) `nil` 指针/切片在 C# 侧的对应：Go 代码里"必然非 nil"的（BranchLabel 的前驱、Assignment 的 Antecedent 等）
//    用 `!`（与 Go 解引用 nil 即 panic 一致，docs/architecture.md §3.2）。
// 7) checker 的"导出 GetX / 未导出 getX"同名冲突按 artifacts/checker-method-collisions.md 加 `Worker` 后缀。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

/// <summary>Go 的 <c>type FlowType struct</c>（值类型；Go 的 <c>FlowType{}</c> 零值 ↔ C# 的 <c>default</c>）。</summary>
internal readonly struct FlowType
{
    internal readonly Type? _t;
    internal readonly bool _incomplete;

    internal FlowType(Type? t, bool incomplete)
    {
        _t = t;
        _incomplete = incomplete;
    }

    /// <summary>Go: <c>func (ft *FlowType) isNil() bool</c>。</summary>
    internal bool IsNil() => _t is null;
}

/// <summary>Go 的 <c>type SharedFlow struct</c>。</summary>
internal readonly struct SharedFlow
{
    internal readonly FlowNode _flow;
    internal readonly FlowType _flowType;

    internal SharedFlow(FlowNode flow, FlowType flowType)
    {
        _flow = flow;
        _flowType = flowType;
    }
}

/// <summary>Go 的 <c>type FlowState struct</c>：在 Checker 的自由链表上复用（Go 的 <c>*FlowState</c> → C# 引用类型）。</summary>
internal sealed class FlowState
{
    internal Node? _reference;
    internal Type? _declaredType;
    internal Type? _initialType;
    internal Node? _flowContainer;
    internal CacheHashKey _refKey;
    internal int _depth;
    internal int _sharedFlowStart;
    internal List<FlowReduceLabelData> _reduceLabels = new();
    internal FlowState? _next;
}

public sealed partial class Checker
{
    internal FlowType NewFlowType(Type t, bool incomplete)
    {
        if (incomplete && (t._flags & TypeFlags.Never) != 0)
        {
            t = _silentNeverType;
        }
        return new FlowType(t, incomplete);
    }

    internal FlowState GetFlowState()
    {
        var f = _freeFlowState;
        if (f == null)
        {
            f = new FlowState();
        }
        _freeFlowState = f._next;
        return f;
    }

    internal void PutFlowState(FlowState f)
    {
        // Go: *f = FlowState{reduceLabels: f.reduceLabels[:0], next: c.freeFlowState} —— 结构体字面量把其余字段一律清零。
        f._reduceLabels.Clear();
        f._next = _freeFlowState;
        f._reference = null;
        f._declaredType = null;
        f._initialType = null;
        f._flowContainer = null;
        f._refKey = default;
        f._depth = 0;
        f._sharedFlowStart = 0;
        _freeFlowState = f;
    }

    internal static FlowNode? GetFlowNodeOfNode(Node node)
    {
        var flowNodeData = node.FlowNodeData();
        if (flowNodeData != null)
        {
            return flowNodeData.FlowNode;
        }
        return null;
    }

    internal Type GetFlowTypeOfReference(Node reference, Type declaredType)
    {
        return GetFlowTypeOfReferenceEx(reference, declaredType, declaredType, null, null);
    }

    internal Type GetFlowTypeOfReferenceEx(Node reference, Type declaredType, Type? initialType, Node? flowContainer, FlowNode? flowNode)
    {
        if (_flowAnalysisDisabled)
        {
            return _errorType;
        }
        if (flowNode == null)
        {
            flowNode = GetFlowNodeOfNode(reference);
            if (flowNode == null)
            {
                return declaredType;
            }
        }
        var f = GetFlowState();
        f._reference = reference;
        f._declaredType = declaredType;
        f._initialType = Coalesce(initialType, declaredType)!;
        f._flowContainer = flowContainer;
        f._sharedFlowStart = _sharedFlows.Count;
        _flowInvocationCount++;
        var evolvedType = GetTypeAtFlowNode(f, flowNode)._t!;
        _sharedFlows.RemoveRange(f._sharedFlowStart, _sharedFlows.Count - f._sharedFlowStart);
        PutFlowState(f);
        // When the reference is 'x' in an 'x.length', 'x.push(value)', 'x.unshift(value)' or x[n] = value' operation,
        // we give type 'any[]' to 'x' instead of using the type determined by control flow analysis such that operations
        // on empty arrays are possible without implicit any errors and new element types can be inferred without
        // type mismatch errors.
        Type resultType;
        if ((evolvedType._objectFlags & ObjectFlags.EvolvingArray) != 0 && IsEvolvingArrayOperationTarget(reference))
        {
            resultType = _autoArrayType;
        }
        else
        {
            resultType = FinalizeEvolvingArrayType(evolvedType);
        }
        if (resultType == _unreachableNeverType || reference.Parent != null && IsNonNullExpression(reference.Parent!) && (resultType._flags & TypeFlags.Never) == 0 && (GetTypeWithFacts(resultType, TypeFacts.NEUndefinedOrNull)._flags & TypeFlags.Never) != 0)
        {
            return declaredType;
        }
        return resultType;
    }

    internal FlowType GetTypeAtFlowNode(FlowState f, FlowNode flow)
    {
        if (f._depth == 2000)
        {
            // We have made 2000 recursive invocations. To avoid overflowing the call stack we report an error
            // and disable further control flow analysis in the containing function or module body.
            var tr = _tracer;
            if (tr != null)
            {
                tr.Instant(Phase.CheckTypes, "getTypeAtFlowNode_DepthLimit", new Dictionary<string, object?> { ["depth"] = f._depth });
            }
            _flowAnalysisDisabled = true;
            ReportFlowControlError(f._reference!);
            return new FlowType(_errorType, false);
        }
        f._depth++;
        FlowNode? sharedFlow = null;
        while (true)
        {
            var flags = flow.Flags;
            if ((flags & FlowFlags.Shared) != 0)
            {
                // We cache results of flow type resolution for shared nodes that were previously visited in
                // the same getFlowTypeOfReference invocation. A node is considered shared when it is the
                // antecedent of more than one node.
                for (var i = f._sharedFlowStart; i < _sharedFlows.Count; i++)
                {
                    if (_sharedFlows[i]._flow == flow)
                    {
                        f._depth--;
                        return _sharedFlows[i]._flowType;
                    }
                }
                sharedFlow = flow;
            }
            FlowType t;
            if ((flags & FlowFlags.Assignment) != 0)
            {
                t = GetTypeAtFlowAssignment(f, flow);
                if (t.IsNil())
                {
                    flow = flow.Antecedent!;
                    continue;
                }
            }
            else if ((flags & FlowFlags.Call) != 0)
            {
                t = GetTypeAtFlowCall(f, flow);
                if (t.IsNil())
                {
                    flow = flow.Antecedent!;
                    continue;
                }
            }
            else if ((flags & FlowFlags.Condition) != 0)
            {
                t = GetTypeAtFlowCondition(f, flow);
            }
            else if ((flags & FlowFlags.SwitchClause) != 0)
            {
                t = GetTypeAtSwitchClause(f, flow);
            }
            else if ((flags & FlowFlags.BranchLabel) != 0)
            {
                var antecedents = GetBranchLabelAntecedents(flow, f._reduceLabels);
                if (antecedents.Next == null)
                {
                    flow = antecedents.Flow!;
                    continue;
                }
                t = GetTypeAtFlowBranchLabel(f, flow, antecedents);
            }
            else if ((flags & FlowFlags.LoopLabel) != 0)
            {
                if (flow.Antecedents!.Next == null)
                {
                    flow = flow.Antecedents!.Flow!;
                    continue;
                }
                t = GetTypeAtFlowLoopLabel(f, flow);
            }
            else if ((flags & FlowFlags.ArrayMutation) != 0)
            {
                t = GetTypeAtFlowArrayMutation(f, flow);
                if (t.IsNil())
                {
                    flow = flow.Antecedent!;
                    continue;
                }
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                f._reduceLabels.Add(flow.Node!.AsFlowReduceLabelData());
                t = GetTypeAtFlowNode(f, flow.Antecedent!);
                f._reduceLabels.RemoveAt(f._reduceLabels.Count - 1);
            }
            else if ((flags & FlowFlags.Start) != 0)
            {
                // Check if we should continue with the control flow of the containing function.
                var container = flow.Node;
                if (container != null && container != f._flowContainer && !IsPropertyAccessExpression(f._reference!) && !IsElementAccessExpression(f._reference!) && !(f._reference!.Kind == Kind.ThisKeyword && !IsArrowFunction(container)))
                {
                    flow = container.FlowNodeData()!.FlowNode!;
                    continue;
                }
                // At the top of the flow we have the initial type.
                t = new FlowType(f._initialType, false);
            }
            else
            {
                // Unreachable code errors are reported in the binding phase. Here we
                // simply return the non-auto declared type to reduce follow-on errors.
                t = new FlowType(ConvertAutoToAny(f._declaredType!), false);
            }
            if (sharedFlow != null)
            {
                // Record visited node and the associated type in the cache.
                _sharedFlows.Add(new SharedFlow(sharedFlow, t));
            }
            f._depth--;
            return t;
        }
    }

    internal static FlowList GetBranchLabelAntecedents(FlowNode flow, List<FlowReduceLabelData> reduceLabels)
    {
        var i = reduceLabels.Count;
        while (i != 0)
        {
            i--;
            var data = reduceLabels[i];
            if (data.Target == flow)
            {
                return data.Antecedents!;
            }
        }
        return flow.Antecedents!;
    }

    internal FlowType GetTypeAtFlowAssignment(FlowState f, FlowNode flow)
    {
        var node = flow.Node!;
        // Assignments only narrow the computed type if the declared type is a union type. Thus, we
        // only need to evaluate the assigned type if the declared type is a union type.
        if (IsMatchingReference(f._reference!, node))
        {
            if (!IsReachableFlowNode(flow))
            {
                return new FlowType(_unreachableNeverType, false);
            }
            if (GetAssignmentTargetKind(node) == AssignmentKind.Compound)
            {
                var flowType = GetTypeAtFlowNode(f, flow.Antecedent!);
                return NewFlowType(GetBaseTypeOfLiteralTypeWorker(flowType._t!), flowType._incomplete);
            }
            if (f._declaredType == _autoType || f._declaredType == _autoArrayType)
            {
                if (IsEmptyArrayAssignment(node))
                {
                    return new FlowType(GetEvolvingArrayType(_neverType), false);
                }
                var assignedType = GetWidenedLiteralTypeWorker(GetInitialOrAssignedType(f, flow));
                if (IsTypeAssignableToWorker(assignedType, f._declaredType!))
                {
                    return new FlowType(assignedType, false);
                }
                return new FlowType(_anyArrayType, false);
            }
            var t = f._declaredType!;
            if (IsInCompoundLikeAssignment(node))
            {
                t = GetBaseTypeOfLiteralTypeWorker(t);
            }
            if ((t._flags & TypeFlags.Union) != 0)
            {
                return new FlowType(GetAssignmentReducedType(t, GetInitialOrAssignedType(f, flow)), false);
            }
            return new FlowType(t, false);
        }
        // We didn't have a direct match. However, if the reference is a dotted name, this
        // may be an assignment to a left hand part of the reference. For example, for a
        // reference 'x.y.z', we may be at an assignment to 'x.y' or 'x'. In that case,
        // return the declared type.
        if (ContainsMatchingReference(f._reference!, node))
        {
            if (!IsReachableFlowNode(flow))
            {
                return new FlowType(_unreachableNeverType, false);
            }
            // A matching dotted name might also be an expando property on a function *expression*,
            // in which case we continue control flow analysis back to the function's declaration
            if (IsVariableDeclaration(node) && (IsInJSFile(node) || IsVarConstLike(node)))
            {
                var init = node.Initializer();
                if (init != null && IsFunctionExpressionOrArrowFunction(init))
                {
                    return GetTypeAtFlowNode(f, flow.Antecedent!);
                }
            }
            return new FlowType(f._declaredType, false);
        }
        // for (const _ in ref) acts as a nonnull on ref
        if (IsVariableDeclaration(node) && IsForInStatement(node.Parent!.Parent!) && (IsMatchingReference(f._reference!, node.Parent!.Parent!.Expression()!) || OptionalChainContainsReference(node.Parent!.Parent!.Expression()!, f._reference!)))
        {
            return new FlowType(GetNonNullableTypeIfNeeded(FinalizeEvolvingArrayType(GetTypeAtFlowNode(f, flow.Antecedent!)._t!)), false);
        }
        // Assignment doesn't affect reference
        return default;
    }

    internal Type GetInitialOrAssignedType(FlowState f, FlowNode flow)
    {
        if (IsVariableDeclaration(flow.Node!) || IsBindingElement(flow.Node!))
        {
            return GetNarrowableTypeForReference(GetInitialType(flow.Node!), f._reference!, CheckMode.Normal);
        }
        return GetNarrowableTypeForReference(GetAssignedType(flow.Node!), f._reference!, CheckMode.Normal);
    }

    internal bool IsEmptyArrayAssignment(Node node)
    {
        return IsVariableDeclaration(node) && node.Initializer() != null && IsEmptyArrayLiteral(node.Initializer()!) ||
            !IsBindingElement(node) && IsBinaryExpression(node.Parent!) && IsEmptyArrayLiteral(node.Parent!.AsBinaryExpression()!.Right!);
    }

    internal FlowType GetTypeAtFlowCall(FlowState f, FlowNode flow)
    {
        var signature = GetEffectsSignature(flow.Node!);
        if (signature != null)
        {
            var predicate = GetTypePredicateOfSignatureWorker(signature);
            if (predicate != null && (predicate._kind == TypePredicateKind.AssertsThis || predicate._kind == TypePredicateKind.AssertsIdentifier))
            {
                var flowType = GetTypeAtFlowNode(f, flow.Antecedent!);
                var t = FinalizeEvolvingArrayType(flowType._t!);
                Type narrowedType;
                if (predicate._t != null)
                {
                    narrowedType = NarrowTypeByTypePredicate(f, t, predicate, flow.Node!, true /*assumeTrue*/);
                }
                else if (predicate._kind == TypePredicateKind.AssertsIdentifier && predicate._parameterIndex >= 0 && predicate._parameterIndex < (flow.Node!.Arguments()?.Length ?? 0))
                {
                    narrowedType = NarrowTypeByAssertion(f, t, flow.Node!.Arguments()![predicate._parameterIndex]);
                }
                else
                {
                    narrowedType = t;
                }
                if (narrowedType == t)
                {
                    return flowType;
                }
                return NewFlowType(narrowedType, flowType._incomplete);
            }
            if ((GetReturnTypeOfSignatureWorker(signature)._flags & TypeFlags.Never) != 0)
            {
                return new FlowType(_unreachableNeverType, false);
            }
        }
        return default;
    }

    internal Type NarrowTypeByTypePredicate(FlowState f, Type t, TypePredicate predicate, Node callExpression, bool assumeTrue)
    {
        // Don't narrow from 'any' if the predicate type is exactly 'Object' or 'Function'
        if (predicate._t != null && !(IsTypeAny(t) && (predicate._t == _globalObjectType || predicate._t == _globalFunctionType)))
        {
            var predicateArgument = GetTypePredicateArgument(predicate, callExpression);
            if (predicateArgument != null)
            {
                if (IsMatchingReference(f._reference!, predicateArgument))
                {
                    return GetNarrowedType(t, predicate._t, assumeTrue, false /*checkDerived*/);
                }
                if (_strictNullChecks && OptionalChainContainsReference(predicateArgument, f._reference!) && (assumeTrue && !HasTypeFacts(predicate._t, TypeFacts.EQUndefined) || !assumeTrue && EveryType(predicate._t, t => IsNullableType(t))))
                {
                    t = GetAdjustedTypeWithFacts(t, TypeFacts.NEUndefinedOrNull);
                }
                var access = GetDiscriminantPropertyAccess(f, predicateArgument, t);
                if (access != null)
                {
                    return NarrowTypeByDiscriminant(t, access, t => GetNarrowedType(t, predicate._t, assumeTrue, false /*checkDerived*/));
                }
            }
        }
        return t;
    }

    internal Type NarrowTypeByAssertion(FlowState f, Type t, Node expr)
    {
        var node = SkipParentheses(expr)!;
        if (node.Kind == Kind.FalseKeyword)
        {
            return _unreachableNeverType;
        }
        if (node.Kind == Kind.BinaryExpression)
        {
            if (node.AsBinaryExpression()!.OperatorToken!.Kind == Kind.AmpersandAmpersandToken)
            {
                return NarrowTypeByAssertion(f, NarrowTypeByAssertion(f, t, node.AsBinaryExpression()!.Left!), node.AsBinaryExpression()!.Right!);
            }
            if (node.AsBinaryExpression()!.OperatorToken!.Kind == Kind.BarBarToken)
            {
                return GetUnionTypeWorker(new[] { NarrowTypeByAssertion(f, t, node.AsBinaryExpression()!.Left!), NarrowTypeByAssertion(f, t, node.AsBinaryExpression()!.Right!) });
            }
        }
        return NarrowType(f, t, node, true /*assumeTrue*/);
    }

    internal FlowType GetTypeAtFlowCondition(FlowState f, FlowNode flow)
    {
        var flowType = GetTypeAtFlowNode(f, flow.Antecedent!);
        if ((flowType._t!._flags & TypeFlags.Never) != 0)
        {
            return flowType;
        }
        // If we have an antecedent type (meaning we're reachable in some way), we first
        // attempt to narrow the antecedent type. If that produces the never type, and if
        // the antecedent type is incomplete (i.e. a transient type in a loop), then we
        // take the type guard as an indication that control *could* reach here once we
        // have the complete type. We proceed by switching to the silent never type which
        // doesn't report errors when operators are applied to it. Note that this is the
        // *only* place a silent never type is ever generated.
        var assumeTrue = (flow.Flags & FlowFlags.TrueCondition) != 0;
        var nonEvolvingType = FinalizeEvolvingArrayType(flowType._t!);
        var narrowedType = NarrowType(f, nonEvolvingType, flow.Node!, assumeTrue);
        if (narrowedType == nonEvolvingType)
        {
            return flowType;
        }
        return NewFlowType(narrowedType, flowType._incomplete);
    }
}

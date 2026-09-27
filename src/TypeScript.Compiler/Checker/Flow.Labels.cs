// ported from: internal/checker/flow.go:1253-1596（分支/循环标签的类型汇聚、evolving array 类型、
//   判别属性访问候选、流控制错误上报）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 字段 `_camelCase`；Go 的 `[]T` 视图 + append/截断 → `List<T>`（`_antecedentTypes`/`_flowLoopStack`）或
//    Go 的局部数组 + `AppendIfUnique`；传给 `getUnionOrEvolvingArrayType` 的切片视图（`c.antecedentTypes[n:]`）
//    → 显式复制成 `Type[]`（该函数的 Go 形参是 `[]*Type`）。
// 2) Go 的 `switch { case cond: }` → `if`/`else if` 链。
// 3) `slices.Contains(c.antecedentTypes[n:], x)` → `List<T>.IndexOf(x, n) >= 0`（`n == Count` 时合法，等价于空区间）。
// 4) `nil` 映射：循环标签的前驱（`flow.Antecedents`）、判别属性访问的中间结果按 Go 的"必然非 nil"用 `!`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal FlowType GetTypeAtFlowBranchLabel(FlowState f, FlowNode flow, FlowList antecedents)
    {
        var antecedentStart = _antecedentTypes.Count;
        var subtypeReduction = false;
        var seenIncomplete = false;
        FlowNode? bypassFlow = null;
        for (var list = antecedents; list != null; list = list.Next)
        {
            var antecedent = list.Flow!;
            if (bypassFlow == null && (antecedent.Flags & FlowFlags.SwitchClause) != 0 && antecedent.Node!.AsFlowSwitchClauseData().IsEmpty())
            {
                // The antecedent is the bypass branch of a potentially exhaustive switch statement.
                bypassFlow = antecedent;
                continue;
            }
            var flowType = GetTypeAtFlowNode(f, antecedent);
            // If the type at a particular antecedent path is the declared type and the
            // reference is known to always be assigned (i.e. when declared and initial types
            // are the same), there is no reason to process more antecedents since the only
            // possible outcome is subtypes that will be removed in the final union type anyway.
            if (flowType._t == f._declaredType && f._declaredType == f._initialType)
            {
                _antecedentTypes.RemoveRange(antecedentStart, _antecedentTypes.Count - antecedentStart);
                return new FlowType(flowType._t, false);
            }
            if (_antecedentTypes.IndexOf(flowType._t!, antecedentStart) < 0)
            {
                _antecedentTypes.Add(flowType._t!);
            }
            // If an antecedent type is not a subset of the declared type, we need to perform
            // subtype reduction. This happens when a "foreign" type is injected into the control
            // flow using the instanceof operator or a user defined type predicate.
            if (!IsTypeSubsetOf(flowType._t!, f._initialType!))
            {
                subtypeReduction = true;
            }
            if (flowType._incomplete)
            {
                seenIncomplete = true;
            }
        }
        if (bypassFlow != null)
        {
            var flowType = GetTypeAtFlowNode(f, bypassFlow);
            // If the bypass flow contributes a type we haven't seen yet and the switch statement
            // isn't exhaustive, process the bypass flow type. Since exhaustiveness checks increase
            // the risk of circularities, we only want to perform them when they make a difference.
            if ((flowType._t!._flags & TypeFlags.Never) == 0 && _antecedentTypes.IndexOf(flowType._t!, antecedentStart) < 0 && !IsExhaustiveSwitchStatement(bypassFlow.Node!.AsFlowSwitchClauseData().SwitchStatement!))
            {
                if (flowType._t == f._declaredType && f._declaredType == f._initialType)
                {
                    _antecedentTypes.RemoveRange(antecedentStart, _antecedentTypes.Count - antecedentStart);
                    return new FlowType(flowType._t, false);
                }
                _antecedentTypes.Add(flowType._t!);
                if (!IsTypeSubsetOf(flowType._t!, f._initialType!))
                {
                    subtypeReduction = true;
                }
                if (flowType._incomplete)
                {
                    seenIncomplete = true;
                }
            }
        }
        // Go 的 `c.antecedentTypes[antecedentStart:]` 是切片视图；C# 里显式复制成数组传给下游（只读使用）。
        var antecedentCount = _antecedentTypes.Count - antecedentStart;
        var types = new Type[antecedentCount];
        _antecedentTypes.CopyTo(antecedentStart, types, 0, antecedentCount);
        var result = NewFlowType(GetUnionOrEvolvingArrayType(f, types, subtypeReduction ? UnionReduction.Subtype : UnionReduction.Literal), seenIncomplete);
        _antecedentTypes.RemoveRange(antecedentStart, _antecedentTypes.Count - antecedentStart);
        return result;
    }

    // At flow control branch or loop junctions, if the type along every antecedent code path
    // is an evolving array type, we construct a combined evolving array type. Otherwise we
    // finalize all evolving array types.
    internal Type GetUnionOrEvolvingArrayType(FlowState f, Type[] types, UnionReduction subtypeReduction)
    {
        if (IsEvolvingArrayTypeList(types))
        {
            return GetEvolvingArrayType(GetUnionTypeWorker(Map(types, GetElementTypeOfEvolvingArrayType)));
        }
        var result = RecombineUnknownType(GetUnionTypeExWorker(SameMap(types, FinalizeEvolvingArrayType), subtypeReduction, null, null));
        if (result != f._declaredType && (result._flags & f._declaredType!._flags & TypeFlags.Union) != 0 && Same(result.AsUnionType()!._types, f._declaredType!.AsUnionType()!._types))
        {
            return f._declaredType!;
        }
        return result;
    }

    internal FlowType GetTypeAtFlowLoopLabel(FlowState f, FlowNode flow)
    {
        if (f._refKey.IsZero())
        {
            f._refKey = GetFlowReferenceKey(f);
        }
        if (f._refKey.Equals(_nonDottedNameCacheKey))
        {
            // No cache key is generated when binding patterns are in unnarrowable situations
            return new FlowType(f._declaredType, false);
        }
        var key = new FlowLoopKey { _flowNode = flow, _refKey = f._refKey };
        // If we have previously computed the control flow type for the reference at
        // this flow loop junction, return the cached type.
        if (_flowLoopCache.TryGetValue(key, out var cached))
        {
            return new FlowType(cached, false);
        }
        // If this flow loop junction and reference are already being processed, return
        // the union of the types computed for each branch so far, marked as incomplete.
        // It is possible to see an empty array in cases where loops are nested and the
        // back edge of the outer loop reaches an inner loop that is already being analyzed.
        // In such cases we restart the analysis of the inner loop, which will then see
        // a non-empty in-process array for the outer loop and eventually terminate because
        // the first antecedent of a loop junction is always the non-looping control flow
        // path that leads to the top.
        foreach (var loopInfo in _flowLoopStack)
        {
            var loopTypes = loopInfo._types;
            if (loopInfo._key!.Equals(key) && loopTypes is { Length: > 0 })
            {
                return NewFlowType(GetUnionOrEvolvingArrayType(f, loopTypes, UnionReduction.Literal), true /*incomplete*/);
            }
        }
        // Add the flow loop junction and reference to the in-process stack and analyze
        // each antecedent code path.
        var antecedentTypes = Array.Empty<Type>();
        var subtypeReduction = false;
        var firstAntecedentType = default(FlowType);
        for (var list = flow.Antecedents; list != null; list = list.Next)
        {
            FlowType flowType;
            if (firstAntecedentType.IsNil())
            {
                // The first antecedent of a loop junction is always the non-looping control
                // flow path that leads to the top.
                firstAntecedentType = GetTypeAtFlowNode(f, list.Flow!);
                flowType = firstAntecedentType;
            }
            else
            {
                // All but the first antecedent are the looping control flow paths that lead
                // back to the loop junction. We track these on the flow loop stack.
                _flowLoopStack.Add(new FlowLoopInfo { _key = key, _types = antecedentTypes });
                var saveFlowTypeCache = _flowTypeCache;
                // Go: c.flowTypeCache = nil（置空 = 关闭缓存）。Checker 的该字段在 C# 是**非空**集合（Checker.Struct.cs），
                // 所以置空改成"换成一个新的空字典"，语义与 Go 相同：递归期间的查表都落空，返回后整体还原。
                _flowTypeCache = new Dictionary<Node, Type>();
                flowType = GetTypeAtFlowNode(f, list.Flow!);
                _flowTypeCache = saveFlowTypeCache;
                _flowLoopStack.RemoveAt(_flowLoopStack.Count - 1);
                // If we see a value appear in the cache it is a sign that control flow analysis
                // was restarted and completed by checkExpressionCached. We can simply pick up
                // the resulting type and bail out.
                if (_flowLoopCache.TryGetValue(key, out var cachedType))
                {
                    return new FlowType(cachedType, false);
                }
            }
            antecedentTypes = AppendIfUnique(antecedentTypes, flowType._t!);
            // If an antecedent type is not a subset of the declared type, we need to perform
            // subtype reduction. This happens when a "foreign" type is injected into the control
            // flow using the instanceof operator or a user defined type predicate.
            if (!IsTypeSubsetOf(flowType._t!, f._initialType!))
            {
                subtypeReduction = true;
            }
            // If the type at a particular antecedent path is the declared type there is no
            // reason to process more antecedents since the only possible outcome is subtypes
            // that will be removed in the final union type anyway.
            if (flowType._t == f._declaredType)
            {
                break;
            }
        }
        // The result is incomplete if the first antecedent (the non-looping control flow path)
        // is incomplete.
        var result = GetUnionOrEvolvingArrayType(f, antecedentTypes, subtypeReduction ? UnionReduction.Subtype : UnionReduction.Literal);
        if (firstAntecedentType._incomplete)
        {
            return NewFlowType(result, true /*incomplete*/);
        }
        _flowLoopCache[key] = result;
        return new FlowType(result, false);
    }

    internal FlowType GetTypeAtFlowArrayMutation(FlowState f, FlowNode flow)
    {
        if (f._declaredType == _autoType || f._declaredType == _autoArrayType)
        {
            var node = flow.Node!;
            Node expr;
            if (IsCallExpression(node))
            {
                expr = node.Expression()!.Expression()!;
            }
            else
            {
                expr = node.AsBinaryExpression()!.Left!.Expression()!;
            }
            if (IsMatchingReference(f._reference!, GetReferenceCandidate(expr)))
            {
                var flowType = GetTypeAtFlowNode(f, flow.Antecedent!);
                if ((flowType._t!._objectFlags & ObjectFlags.EvolvingArray) != 0)
                {
                    var evolvedType = flowType._t!;
                    if (IsCallExpression(node))
                    {
                        foreach (var arg in node.Arguments() ?? Array.Empty<Node>())
                        {
                            evolvedType = AddEvolvingArrayElementType(evolvedType, arg);
                        }
                    }
                    else
                    {
                        // We must get the context free expression type so as to not recur in an uncached fashion on the LHS (which causes exponential blowup in compile time)
                        var indexType = GetContextFreeTypeOfExpression(node.AsBinaryExpression()!.Left!.AsElementAccessExpression()!.ArgumentExpression!);
                        if (IsTypeAssignableToKind(indexType, TypeFlags.NumberLike))
                        {
                            evolvedType = AddEvolvingArrayElementType(evolvedType, node.AsBinaryExpression()!.Right!);
                        }
                    }
                    return NewFlowType(evolvedType, flowType._incomplete);
                }
                return flowType;
            }
        }
        return default;
    }

    internal Node? GetDiscriminantPropertyAccess(FlowState f, Node expr, Type computedType)
    {
        // As long as the computed type is a subset of the declared type, we use the full declared type to detect
        // a discriminant property. In cases where the computed type isn't a subset, e.g because of a preceding type
        // predicate narrowing, we use the actual computed type.
        if ((f._declaredType!._flags & TypeFlags.Union) != 0 || (computedType._flags & TypeFlags.Union) != 0)
        {
            var access = GetCandidateDiscriminantPropertyAccess(f, expr);
            if (access != null)
            {
                if (TryGetAccessedPropertyName(access, out var name))
                {
                    var t = computedType;
                    if ((f._declaredType!._flags & TypeFlags.Union) != 0 && IsTypeSubsetOf(computedType, f._declaredType!))
                    {
                        t = f._declaredType!;
                    }
                    if (IsDiscriminantProperty(t, name))
                    {
                        return access;
                    }
                }
            }
        }
        return null;
    }

    internal Node? GetCandidateDiscriminantPropertyAccess(FlowState f, Node expr)
    {
        if (IsBindingPattern(f._reference!) || IsFunctionExpressionOrArrowFunction(f._reference!) || IsObjectLiteralMethod(f._reference!))
        {
            // When the reference is a binding pattern or function or arrow expression, we are narrowing a pseudo-reference in
            // getNarrowedTypeOfSymbol. An identifier for a destructuring variable declared in the same binding pattern or
            // parameter declared in the same parameter list is a candidate.
            if (IsIdentifier(expr))
            {
                var symbol = GetResolvedSymbolWorker(expr);
                var declaration = GetExportSymbolOfValueSymbolIfExported(symbol)!.ValueDeclaration;
                if (declaration != null && (IsBindingElement(declaration) || IsParameterDeclaration(declaration)) && f._reference == declaration.Parent && declaration.Initializer() == null && !HasDotDotDotToken(declaration))
                {
                    return declaration;
                }
            }
        }
        else if (IsAccessExpression(expr))
        {
            // An access expression is a candidate if the reference matches the left hand expression.
            if (IsMatchingReference(f._reference!, expr.Expression()!))
            {
                return expr;
            }
        }
        else if (IsIdentifier(expr))
        {
            var symbol = GetResolvedSymbolWorker(expr);
            if (IsConstantVariable(symbol))
            {
                var declaration = symbol.ValueDeclaration!;
                var initializer = GetCandidateVariableDeclarationInitializer(declaration);
                // Given 'const x = obj.kind', allow 'x' as an alias for 'obj.kind'
                if (initializer != null && IsAccessExpression(initializer) && IsMatchingReference(f._reference!, initializer.Expression()!))
                {
                    return initializer;
                }
                // Given 'const { kind: x } = obj', allow 'x' as an alias for 'obj.kind'
                if (IsBindingElement(declaration) && declaration.Initializer() == null)
                {
                    initializer = GetCandidateVariableDeclarationInitializer(declaration.Parent!.Parent!);
                    if (initializer != null && (IsIdentifier(initializer) || IsAccessExpression(initializer)) && IsMatchingReference(f._reference!, initializer))
                    {
                        return declaration;
                    }
                }
            }
        }
        return null;
    }

    internal static Node? GetCandidateVariableDeclarationInitializer(Node node)
    {
        if (IsVariableDeclaration(node) && node.Type() == null)
        {
            var initializer = node.Initializer();
            if (initializer != null)
            {
                return SkipParentheses(initializer);
            }
        }
        return null;
    }

    // An evolving array type tracks the element types that have so far been seen in an
    // 'x.push(value)' or 'x[n] = value' operation along the control flow graph. Evolving
    // array types are ultimately converted into manifest array types (using getFinalArrayType)
    // and never escape the getFlowTypeOfReference function.
    internal Type GetEvolvingArrayType(Type elementType)
    {
        var key = new CachedTypeKey { _kind = CachedTypeKind.EvolvingArrayType, _typeId = elementType._id };
        if (!_cachedTypes.TryGetValue(key, out var result))
        {
            result = NewObjectType(ObjectFlags.EvolvingArray, null);
            result.AsEvolvingArrayType()!._elementType = elementType;
            _cachedTypes[key] = result;
        }
        return result;
    }

    internal Type GetElementTypeOfEvolvingArrayType(Type t)
    {
        if ((t._objectFlags & ObjectFlags.EvolvingArray) != 0)
        {
            return t.AsEvolvingArrayType()!._elementType!;
        }
        return _neverType;
    }

    internal static bool IsEvolvingArrayTypeList(Type[] types)
    {
        var hasEvolvingArrayType = false;
        foreach (var t in types)
        {
            if ((t._flags & TypeFlags.Never) == 0)
            {
                if ((t._objectFlags & ObjectFlags.EvolvingArray) == 0)
                {
                    return false;
                }
                hasEvolvingArrayType = true;
            }
        }
        return hasEvolvingArrayType;
    }

    // Return true if the given node is 'x' in an 'x.length', x.push(value)', 'x.unshift(value)' or
    // 'x[n] = value' operation, where 'n' is an expression of type any, undefined, or a number-like type.
    internal bool IsEvolvingArrayOperationTarget(Node node)
    {
        var root = GetReferenceRoot(node);
        var parent = root.Parent!;
        var isLengthPushOrUnshift = IsPropertyAccessExpression(parent) && (parent.Name()!.Text() == "length" ||
            IsCallExpression(parent.Parent!) && IsIdentifier(parent.Name()!) && IsPushOrUnshiftIdentifier(parent.Name()!));
        var isElementAssignment = IsElementAccessExpression(parent) && parent.Expression() == root &&
            IsBinaryExpression(parent.Parent!) && parent.Parent!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken &&
            parent.Parent!.AsBinaryExpression()!.Left == parent && !IsAssignmentTarget(parent.Parent!) &&
            IsTypeAssignableToKind(GetTypeOfExpression(parent.AsElementAccessExpression()!.ArgumentExpression!), TypeFlags.NumberLike);
        return isLengthPushOrUnshift || isElementAssignment;
    }

    // When adding evolving array element types we do not perform subtype reduction. Instead,
    // we defer subtype reduction until the evolving array type is finalized into a manifest
    // array type.
    internal Type AddEvolvingArrayElementType(Type evolvingArrayType, Node node)
    {
        var newElementType = GetRegularTypeOfObjectLiteral(GetBaseTypeOfLiteralTypeWorker(GetContextFreeTypeOfExpression(node)));
        var elementType = evolvingArrayType.AsEvolvingArrayType()!._elementType!;
        if (IsTypeSubsetOf(newElementType, elementType))
        {
            return evolvingArrayType;
        }
        return GetEvolvingArrayType(GetUnionTypeWorker(new[] { elementType, newElementType }));
    }

    internal Type FinalizeEvolvingArrayType(Type t)
    {
        if ((t._objectFlags & ObjectFlags.EvolvingArray) != 0)
        {
            return GetFinalArrayType(t.AsEvolvingArrayType()!);
        }
        return t;
    }

    internal Type GetFinalArrayType(EvolvingArrayType t)
    {
        if (t._finalArrayType == null)
        {
            t._finalArrayType = CreateFinalArrayType(t._elementType!);
        }
        return t._finalArrayType;
    }

    internal Type CreateFinalArrayType(Type elementType)
    {
        if ((elementType._flags & TypeFlags.Never) != 0)
        {
            return _autoArrayType;
        }
        if ((elementType._flags & TypeFlags.Union) != 0)
        {
            return CreateArrayType(GetUnionTypeExWorker(elementType.Types(), UnionReduction.Subtype, null, null));
        }
        return CreateArrayType(elementType);
    }

    internal void ReportFlowControlError(Node node)
    {
        var block = FindAncestor(node, n => IsFunctionOrModuleBlock(n))!;
        var sourceFile = GetSourceFileOfNode(node)!;
        var span = GetRangeOfTokenAtPosition(sourceFile, block.StatementList()!.Pos());
        AddDiagnostic(NewDiagnostic(sourceFile, span, Diagnostics.The_containing_function_or_module_body_is_too_large_for_control_flow_analysis));
    }
}

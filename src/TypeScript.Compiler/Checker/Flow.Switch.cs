// ported from: internal/checker/flow.go:1933-2121（switch 穷尽性判定、case 见证/类型缓存、effects signature、
//   Symbol.hasInstance 方法查找、已知 symbol 名的属性名）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 字段 `_camelCase`；`switch { case cond: }` → `if`/`else if` 链；`core.IfElse` → C# 三元表达式。
// 2) Go 的 map 读缺失键 → `TryGetValue`；`slices.Contains(a, x)` → `Array.IndexOf(a, x) >= 0`。
// 3) `link` 结构体字段按 ChkTypes 的 `_camelCase`（`_exhaustiveState`/`_switchTypes`/`_witnesses`/`_effectsSignature`）。
// 4) 命中 artifacts/checker-method-collisions.md 的未导出方法用 `Worker` 后缀
//    （`GetSignaturesOfTypeWorker`/`GetBaseConstraintOrType` 等按清单）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal bool IsExhaustiveSwitchStatement(Node node)
    {
        var links = _switchStatementLinks.Get(node);
        if (links._exhaustiveState == ExhaustiveState.Unknown)
        {
            // Indicate resolution is in process
            links._exhaustiveState = ExhaustiveState.Computing;
            var isExhaustive = ComputeExhaustiveSwitchStatement(node);
            if (links._exhaustiveState == ExhaustiveState.Computing)
            {
                links._exhaustiveState = isExhaustive ? ExhaustiveState.True : ExhaustiveState.False;
            }
        }
        else if (links._exhaustiveState == ExhaustiveState.Computing)
        {
            // Resolve circularity to false
            links._exhaustiveState = ExhaustiveState.False;
        }
        return links._exhaustiveState == ExhaustiveState.True;
    }

    internal bool ComputeExhaustiveSwitchStatement(Node node)
    {
        if (IsTypeOfExpression(node.Expression()!))
        {
            var witnesses = GetSwitchClauseTypeOfWitnesses(node);
            if (witnesses == null)
            {
                return false;
            }
            var operandConstraint = GetBaseConstraintOrType(CheckExpressionCached(node.Expression()!.Expression()!));
            // Get the not-equal flags for all handled cases.
            var notEqualFacts = GetNotEqualFactsFromTypeofSwitch(0, 0, witnesses);
            if ((operandConstraint._flags & TypeFlags.AnyOrUnknown) != 0)
            {
                // We special case the top types to be exhaustive when all cases are handled.
                return (TypeFacts.AllTypeofNE & notEqualFacts) == TypeFacts.AllTypeofNE;
            }
            // A missing not-equal flag indicates that the type wasn't handled by some case.
            return !SomeType(operandConstraint, t => GetTypeFacts(t, notEqualFacts) == notEqualFacts);
        }
        var t = GetBaseConstraintOrType(CheckExpressionCached(node.Expression()!));
        if (!IsLiteralType(t))
        {
            return false;
        }
        var switchTypes = GetSwitchClauseTypes(node);
        if (switchTypes.Length == 0 || Some(switchTypes, IsNeitherUnitTypeNorNever))
        {
            return false;
        }
        return EachTypeContainedIn(MapType(t, GetRegularTypeOfLiteralType), switchTypes);
    }

    internal bool EachTypeContainedIn(Type source, Type[] types)
    {
        if ((source._flags & TypeFlags.Union) != 0)
        {
            return !Some(source.AsUnionType()!._types, t => Array.IndexOf(types, t) < 0);
        }
        return Array.IndexOf(types, source) >= 0;
    }

    // Get the type names from all cases in a switch on `typeof`. The default clause and/or duplicate type names are
    // represented as empty strings. Return nil if one or more case clause expressions are not string literals.
    internal string[]? GetSwitchClauseTypeOfWitnesses(Node node)
    {
        var links = _switchStatementLinks.Get(node);
        if (!links._witnessesComputed)
        {
            var clauses = node.AsSwitchStatement()!.CaseBlock!.AsCaseBlock()!.Clauses!.Nodes ?? [];
            // Go 用 `witnesses = nil` 表示"不是全部 case 都是字符串字面量"；C# 里用非空局部数组写入、可空别名对外。
            var witnessesArray = new string[clauses.Length];
            string[]? witnesses = witnessesArray;
            for (var i = 0; i < clauses.Length; i++)
            {
                var clause = clauses[i];
                if (clause.Kind == Kind.CaseClause)
                {
                    if (!IsStringLiteralLike(clause.Expression()))
                    {
                        witnesses = null;
                        break;
                    }
                    var text = clause.Expression()!.Text();
                    if (Array.IndexOf(witnessesArray, text) < 0)
                    {
                        witnessesArray[i] = text;
                    }
                }
            }
            links._witnesses = witnesses;
            links._witnessesComputed = true;
        }
        return links._witnesses;
    }

    // Return the combined not-equal type facts for all cases except those between the start and end indices.
    internal TypeFacts GetNotEqualFactsFromTypeofSwitch(int start, int end, string[] witnesses)
    {
        var facts = TypeFacts.None;
        for (var i = 0; i < witnesses.Length; i++)
        {
            var witness = witnesses[i];
            if ((i < start || i >= end) && witness != "")
            {
                if (!_typeofNEFacts.TryGetValue(witness, out var f))
                {
                    f = TypeFacts.TypeofNEHostObject;
                }
                facts |= f;
            }
        }
        return facts;
    }

    internal Type[] GetSwitchClauseTypes(Node node)
    {
        var links = _switchStatementLinks.Get(node);
        if (!links._switchTypesComputed)
        {
            var clauses = node.AsSwitchStatement()!.CaseBlock!.AsCaseBlock()!.Clauses!.Nodes ?? [];
            var types = new Type[clauses.Length];
            for (var i = 0; i < clauses.Length; i++)
            {
                types[i] = GetTypeOfSwitchClause(clauses[i]);
            }
            links._switchTypes = types;
            links._switchTypesComputed = true;
        }
        return links._switchTypes!;
    }

    internal Type GetTypeOfSwitchClause(Node clause)
    {
        if (clause.Kind == Kind.CaseClause)
        {
            return GetRegularTypeOfLiteralType(GetTypeOfExpression(clause.Expression()!));
        }
        return _neverType;
    }

    internal Signature? GetEffectsSignature(Node node)
    {
        var links = _signatureLinks.Get(node);
        var signature = links._effectsSignature;
        if (signature == null)
        {
            // A call expression parented by an expression statement is a potential assertion. Other call
            // expressions are potential type predicate function calls. In order to avoid triggering
            // circularities in control flow analysis, we use getTypeOfDottedName when resolving the call
            // target expression of an assertion.
            Type? funcType = null;
            if (IsBinaryExpression(node))
            {
                var rightType = CheckNonNullExpression(node.AsBinaryExpression()!.Right!);
                funcType = GetSymbolHasInstanceMethodOfObjectType(rightType);
            }
            else if (IsExpressionStatement(node.Parent!))
            {
                funcType = GetTypeOfDottedName(node.Expression()!, null /*diagnostic*/);
            }
            else if (node.Expression()!.Kind != Kind.SuperKeyword)
            {
                if (IsOptionalChain(node))
                {
                    funcType = CheckNonNullType(GetOptionalExpressionType(CheckExpression(node.Expression()!), node.Expression()!), node.Expression()!);
                }
                else
                {
                    funcType = CheckNonNullExpression(node.Expression()!);
                }
            }
            Type? apparentType = null;
            if (funcType != null)
            {
                apparentType = GetApparentTypeWorker(funcType);
            }
            var signatures = GetSignaturesOfTypeWorker(apparentType ?? _unknownType, SignatureKind.Call) ?? Array.Empty<Signature>();
            if (signatures.Length == 1 && (signatures[0]._typeParameters?.Length ?? 0) == 0)
            {
                signature = signatures[0];
            }
            else if (Some(signatures, HasTypePredicateOrNeverReturnType))
            {
                signature = GetResolvedSignatureWorker(node, null, CheckMode.Normal);
            }
            if (!(signature != null && HasTypePredicateOrNeverReturnType(signature)))
            {
                signature = _unknownSignature;
            }
            links._effectsSignature = signature;
        }
        if (signature == _unknownSignature)
        {
            return null;
        }
        return signature;
    }

    /// <summary>Get the type of the `[Symbol.hasInstance]` method of an object type.</summary>
    internal Type? GetSymbolHasInstanceMethodOfObjectType(Type t)
    {
        var hasInstancePropertyName = GetPropertyNameForKnownSymbolName("hasInstance");
        if (AllTypesAssignableToKind(t, TypeFlags.NonPrimitive))
        {
            var hasInstanceProperty = GetPropertyOfTypeWorker(t, hasInstancePropertyName);
            if (hasInstanceProperty != null)
            {
                var hasInstancePropertyType = GetTypeOfSymbolWorker(hasInstanceProperty);
                if (hasInstancePropertyType != null && (GetSignaturesOfTypeWorker(hasInstancePropertyType, SignatureKind.Call)?.Length ?? 0) != 0)
                {
                    return hasInstancePropertyType;
                }
            }
        }
        return null;
    }

    internal string GetPropertyNameForKnownSymbolName(string symbolName)
    {
        var ctorType = _getGlobalESSymbolConstructorSymbolOrNil();
        if (ctorType != null)
        {
            var uniqueType = GetTypeOfPropertyOfTypeWorker(GetTypeOfSymbolWorker(ctorType), symbolName);
            if (uniqueType != null && IsTypeUsableAsPropertyName(uniqueType))
            {
                return GetPropertyNameFromType(uniqueType);
            }
        }
        return InternalSymbolNamePrefix + "@" + symbolName;
    }
}

// ported from: internal/checker/checker.go:29323-29616（GetPromisedTypeOfPromise / getPromisedTypeOfPromiseEx /
//   getTypeOfFirstParameterOfSignature(+WithFallback) / getMappedTypeModifiers / getMappedTypeOptionality /
//   getCombinedMappedTypeOptionality / isPartialMappedType / getOptionalExpressionType / removeOptionalTypeMarker /
//   propagateOptionalTypeMarker / removeMissingType / removeMissingOrUndefinedType / removeDefinitelyFalsyTypes /
//   extractDefinitelyFalsyTypes / getDefinitelyFalsyPartOfType / getConstraintDeclaration / getTemplateLiteralType）
//
// 与 Go 的差异（逐条，一条一行理由；其余逐行对应，含注释与分支顺序）：
// - Go 未导出字段 → `_camelCase`（`_cachedTypes`/`_voidType`/`_subtypeRelation`/…，Main 终局口径；字段定义见 Checker.Struct.cs、Types*.cs）。
// - `getPromisedTypeOfPromiseEx` 的 `thisTypeForErrorOut **Type` → `out Type? thisTypeForErrorOut`：Go 用 nil 表示"不需要"，
//   C# 的 `out` 要求所有路径赋值，于是在方法开头写 `null`；两个调用点（`GetPromisedTypeOfPromise` 传 nil → `out _`；
//   `getAwaitedTypeNoAliasEx` 传 `&x` → `out var x`）语义不变。
// - Go 的 `switch { case cond: }`（无条件表达式 switch）→ C# `if` 链（语义相同；C# 的 switch 不能以任意 bool 表达式为 case）。
// - map 读（Go 缺键得 nil）→ `GetValueOrDefault(key)`；`Dictionary` 索引器缺键会抛异常，禁止使用。
// - `signature.parameters` / `t.AsTemplateLiteralType().texts|types` 等可空内部字段 → `_parameters`/`_texts`/`_types` + `!`
//   （C# 端声明为 `T[]?`，Go 端在同一点是有非 nil 不变量的）。
// - `stringutil.CombineSurrogatePairs(s)` **不移植**（docs/architecture.md §3.1：C# 的 string 已是 UTF-16，无哨兵编码）
//   → 直接写 `s`（恒等）；Go 里它把哨兵字节合回代码单元，C# 里没有对应步骤。
// - `slices.Contains(types, c.wildcardType)` → 显式 `foreach` 逐项引用比较（Go 的 `slices.Contains` 对 `*Type` 就是 `==`；
//   C# 的 `AsSpan().Contains` 走 `EqualityComparer<Type>`，语义可能不同，故用循环）。
// - `strings.Builder`/`sb.Reset()` → `StringBuilder`/`sb.Clear()`。
// - `append` 到局部切片 → `List<T>`；`core.Map/Every/FindIndex/ReplaceElement`（签名 `T[]?` + `Func<T,U>`）→ 裸名 `Map/Every/FindIndex/ReplaceElement`（`using static TypeScript.Core.Core;`）。
// - 命中 `artifacts/checker-method-collisions.md` 的跨片成员按 `Worker` 名调用：`GetTypeArgumentsWorker`、`GetSignaturesOfTypeWorker`、
//   `GetTypeOfPropertyOfTypeWorker`、`GetTypeAtPositionWorker`、`GetUnionTypeWorker`、`GetUnionTypeExWorker`；本文件定义的
//   `removeMissingOrUndefinedType`(29503) 也因同名导出者改名为 `RemoveMissingOrUndefinedTypeWorker`。
// - 局部变量遮蔽：Go 的 `for _, t := range t.Types()[1:]` / `mappedTypeModifiers` 里的临时变量在 C# 改名（`elementType`/`u`）避免 CS0136。
namespace TypeScript.Checker;

using System.Collections.Generic;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    public Type? GetPromisedTypeOfPromise(Type t)
    {
        return GetPromisedTypeOfPromiseEx(t, null, out _);
    }

    // Gets the "promised type" of a promise.
    // @param type The type of the promise.
    // @remarks The "promised type" of a type is the type of the "value" parameter of the "onfulfilled" callback.
    internal Type? GetPromisedTypeOfPromiseEx(Type t, Node? errorNode, out Type? thisTypeForErrorOut)
    {
        //  { // type
        //      then( // thenFunction
        //          onfulfilled: ( // onfulfilledParameterType
        //              value: T // valueParameterType
        //          ) => any
        //      ): any;
        //  }
        thisTypeForErrorOut = null;
        if (IsTypeAny(t))
        {
            return null;
        }
        var key = new CachedTypeKey(CachedTypeKind.PromisedTypeOfPromise, t._id);
        var cached = _cachedTypes.GetValueOrDefault(key);
        if (cached != null)
        {
            return cached;
        }
        if (IsReferenceToType(t, _getGlobalPromiseType()))
        {
            var result = GetTypeArgumentsWorker(t)[0];
            _cachedTypes[key] = result;
            return result;
        }
        // primitives with a `{ then() }` won't be unwrapped/adopted.
        if (AllTypesAssignableToKind(GetBaseConstraintOrType(t), TypeFlags.Primitive | TypeFlags.Never))
        {
            return null;
        }
        var thenFunction = GetTypeOfPropertyOfTypeWorker(t, "then");
        // TODO: GH#18217
        if (IsTypeAny(thenFunction))
        {
            return null;
        }
        Signature[]? thenSignatures = null;
        if (thenFunction != null)
        {
            thenSignatures = GetSignaturesOfTypeWorker(thenFunction, SignatureKind.Call);
        }
        if (thenSignatures == null || thenSignatures.Length == 0)
        {
            if (errorNode != null)
            {
                Error(errorNode, A_promise_must_have_a_then_method);
            }
            return null;
        }
        Type? thisTypeForError = null;
        var candidates = new List<Signature>();
        foreach (var thenSignature in thenSignatures)
        {
            var thisType = GetThisTypeOfSignature(thenSignature);
            if (thisType != null && thisType != _voidType && !IsTypeRelatedTo(t, thisType, _subtypeRelation))
            {
                thisTypeForError = thisType;
            }
            else
            {
                candidates.Add(thenSignature);
            }
        }
        if (candidates.Count == 0)
        {
            Debug.Assert(thisTypeForError != null);
            thisTypeForErrorOut = thisTypeForError;
            if (errorNode != null)
            {
                Error(errorNode, The_this_context_of_type_0_is_not_assignable_to_method_s_this_of_type_1, TypeToString(t), TypeToString(thisTypeForError!));
            }
            return null;
        }
        var onfulfilledParameterType = GetTypeWithFacts(GetUnionTypeWorker(Map(candidates.ToArray(), GetTypeOfFirstParameterOfSignature)), TypeFacts.NEUndefinedOrNull);
        if (IsTypeAny(onfulfilledParameterType))
        {
            return null;
        }
        var onfulfilledParameterSignatures = GetSignaturesOfTypeWorker(onfulfilledParameterType, SignatureKind.Call);
        // Go: len(onfulfilledParameterSignatures) == 0 —— len(nil slice) == 0 为 true，故用 ?. + ?? 0 保持语义。
        if ((onfulfilledParameterSignatures?.Length ?? 0) == 0)
        {
            if (errorNode != null)
            {
                Error(errorNode, The_first_parameter_of_the_then_method_of_a_promise_must_be_a_callback);
            }
            return null;
        }
        var result2 = GetUnionTypeExWorker(Map(onfulfilledParameterSignatures, GetTypeOfFirstParameterOfSignature), UnionReduction.Subtype, null, null);
        _cachedTypes[key] = result2;
        return result2;
    }

    internal Type GetTypeOfFirstParameterOfSignature(Signature signature)
    {
        return GetTypeOfFirstParameterOfSignatureWithFallback(signature, _neverType);
    }

    internal Type GetTypeOfFirstParameterOfSignatureWithFallback(Signature signature, Type fallbackType)
    {
        if ((signature._parameters?.Length ?? 0) > 0)
        {
            return GetTypeAtPositionWorker(signature, 0);
        }
        return fallbackType;
    }

    internal static MappedTypeModifiers GetMappedTypeModifiers(Type t)
    {
        var declaration = t.AsMappedType()!._declaration!;
        var modifiers = default(MappedTypeModifiers);
        if (declaration.ReadonlyToken != null)
        {
            modifiers |= IfElse(declaration.ReadonlyToken.Kind == Kind.MinusToken, MappedTypeModifiers.ExcludeReadonly, MappedTypeModifiers.IncludeReadonly);
        }
        if (declaration.QuestionToken != null)
        {
            modifiers |= IfElse(declaration.QuestionToken.Kind == Kind.MinusToken, MappedTypeModifiers.ExcludeOptional, MappedTypeModifiers.IncludeOptional);
        }
        return modifiers;
    }

    // Return -1, 0, or 1, where -1 means optionality is stripped (i.e. -?), 0 means optionality is unchanged, and 1 means
    // optionality is added (i.e. +?).
    internal static int GetMappedTypeOptionality(Type t)
    {
        var modifiers = GetMappedTypeModifiers(t);
        if ((modifiers & MappedTypeModifiers.ExcludeOptional) != 0)
        {
            return -1;
        }
        if ((modifiers & MappedTypeModifiers.IncludeOptional) != 0)
        {
            return 1;
        }
        return 0;
    }

    // Return -1, 0, or 1, for stripped, unchanged, or added optionality respectively. When a homomorphic mapped type doesn't
    // modify optionality, recursively consult the optionality of the type being mapped over to see if it strips or adds optionality.
    // For intersections, return -1 or 1 when all constituents strip or add optionality, otherwise return 0.
    internal int GetCombinedMappedTypeOptionality(Type t)
    {
        if ((t._objectFlags & ObjectFlags.Mapped) != 0)
        {
            var optionality = GetMappedTypeOptionality(t);
            if (optionality != 0)
            {
                return optionality;
            }
            return GetCombinedMappedTypeOptionality(GetModifiersTypeFromMappedType(t));
        }
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            var types = t.Types();
            var optionality = GetCombinedMappedTypeOptionality(types[0]);
            for (var i = 1; i < types.Length; i++)
            {
                if (GetCombinedMappedTypeOptionality(types[i]) != optionality)
                {
                    return 0;
                }
            }
            return optionality;
        }
        return 0;
    }

    internal static bool IsPartialMappedType(Type t)
    {
        return (t._objectFlags & ObjectFlags.Mapped) != 0 && (GetMappedTypeModifiers(t) & MappedTypeModifiers.IncludeOptional) != 0;
    }

    internal Type GetOptionalExpressionType(Type exprType, Node expression)
    {
        if (IsExpressionOfOptionalChainRoot(expression))
        {
            return GetNonNullableType(exprType);
        }
        if (IsOptionalChain(expression))
        {
            return RemoveOptionalTypeMarker(exprType);
        }
        return exprType;
    }

    internal Type RemoveOptionalTypeMarker(Type t)
    {
        if (_strictNullChecks)
        {
            return RemoveType(t, _optionalType);
        }
        return t;
    }

    internal Type PropagateOptionalTypeMarker(Type t, Node node, bool wasOptional)
    {
        if (wasOptional)
        {
            if (IsOutermostOptionalChain(node))
            {
                return GetOptionalType(t, false);
            }
            return AddOptionalTypeMarker(t);
        }
        return t;
    }

    internal Type RemoveMissingType(Type t, bool isOptional)
    {
        if (_exactOptionalPropertyTypes && isOptional)
        {
            return RemoveType(t, _missingType);
        }
        return t;
    }

    // Go 名字 `removeMissingOrUndefinedType`（29503）与导出者 `RemoveMissingOrUndefinedType`（exports.go:386）冲突
    // → 未导出者按 artifacts/checker-method-collisions.md 改名 `RemoveMissingOrUndefinedTypeWorker`。
    internal Type RemoveMissingOrUndefinedTypeWorker(Type t)
    {
        if (_exactOptionalPropertyTypes)
        {
            return RemoveType(t, _missingType);
        }
        return GetTypeWithFacts(t, TypeFacts.NEUndefined);
    }

    internal Type RemoveDefinitelyFalsyTypes(Type t)
    {
        return FilterType(t, u => HasTypeFacts(u, TypeFacts.Truthy));
    }

    internal Type ExtractDefinitelyFalsyTypes(Type t)
    {
        return MapType(t, GetDefinitelyFalsyPartOfType);
    }

    internal Type GetDefinitelyFalsyPartOfType(Type t)
    {
        if ((t._flags & TypeFlags.String) != 0)
        {
            return _emptyStringType;
        }
        if ((t._flags & TypeFlags.Number) != 0)
        {
            return _zeroType;
        }
        if ((t._flags & TypeFlags.BigInt) != 0)
        {
            return _zeroBigIntType;
        }
        if (t == _regularFalseType || t == _falseType ||
            (t._flags & (TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Null | TypeFlags.AnyOrUnknown)) != 0 ||
            (t._flags & TypeFlags.StringLiteral) != 0 && GetStringLiteralValue(t) == "" ||
            (t._flags & TypeFlags.NumberLiteral) != 0 && GetNumberLiteralValue(t) == 0 ||
            (t._flags & TypeFlags.BigIntLiteral) != 0 && IsZeroBigInt(t))
        {
            return t;
        }
        return _neverType;
    }

    internal Node? GetConstraintDeclaration(Type t)
    {
        if (t._symbol != null)
        {
            foreach (var d in t._symbol.Declarations ?? [])
            {
                if (IsTypeParameterDeclaration(d))
                {
                    var constraint = d.AsTypeParameterDeclaration()!.Constraint;
                    if (constraint != null)
                    {
                        return constraint;
                    }
                }
            }
        }
        return null;
    }

    internal Type GetTemplateLiteralType(string[] texts, Type[] types)
    {
        var unionIndex = FindIndex(types, t => (t._flags & (TypeFlags.Never | TypeFlags.Union)) != 0);
        if (unionIndex >= 0)
        {
            if (!CheckCrossProductUnion(types))
            {
                return _errorType;
            }
            return MapType(types[unionIndex], t => GetTemplateLiteralType(texts, ReplaceElement(types, unionIndex, t)));
        }
        foreach (var candidate in types)
        {
            if (candidate == _wildcardType)
            {
                return _wildcardType;
            }
        }
        var newTypes = new List<Type>();
        var newTexts = new List<string>();
        var sb = new StringBuilder();
        sb.Append(texts[0]);
        bool addSpans(string[] spanTexts, Type[] spanTypes)
        {
            for (var i = 0; i < spanTypes.Length; i++)
            {
                var elementType = spanTypes[i];
                if ((elementType._flags & (TypeFlags.Literal | TypeFlags.Null | TypeFlags.Undefined)) != 0)
                {
                    sb.Append(GetTemplateStringForType(elementType));
                    sb.Append(spanTexts[i + 1]);
                }
                else if ((elementType._flags & TypeFlags.TemplateLiteral) != 0)
                {
                    var template = elementType.AsTemplateLiteralType()!;
                    sb.Append(template._texts![0]);
                    if (!addSpans(template._texts!, template._types!))
                    {
                        return false;
                    }
                    sb.Append(spanTexts[i + 1]);
                }
                else if (IsGenericIndexType(elementType) || IsPatternLiteralPlaceholderType(elementType))
                {
                    newTypes.Add(elementType);
                    newTexts.Add(sb.ToString());
                    sb.Clear();
                    sb.Append(spanTexts[i + 1]);
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
        if (!addSpans(texts, types))
        {
            return _stringType;
        }
        if (newTypes.Count == 0)
        {
            return GetStringLiteralType(sb.ToString());
        }
        newTexts.Add(sb.ToString());
        if (Every(newTexts.ToArray(), t => t == ""))
        {
            if (Every(newTypes.ToArray(), u => (u._flags & TypeFlags.String) != 0))
            {
                return _stringType;
            }
            // Normalize `${Mapping<xxx>}` into Mapping<xxx>
            if (newTypes.Count == 1 && IsPatternLiteralType(newTypes[0]))
            {
                return newTypes[0];
            }
        }
        var key = GetTemplateTypeKey(newTexts.ToArray(), newTypes.ToArray());
        var t2 = _templateLiteralTypes.GetValueOrDefault(key);
        if (t2 == null)
        {
            t2 = NewTemplateLiteralType(newTexts.ToArray(), newTypes.ToArray());
            _templateLiteralTypes[key] = t2;
        }
        return t2;
    }
}

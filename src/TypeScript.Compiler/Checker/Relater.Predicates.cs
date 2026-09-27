// ported from: internal/checker/relater.go:2049-2568
// （getTypePredicateOfSignature 家族、getUnionOrIntersectionTypePredicate、typePredicateKindsMatch、
//   createTypePredicateFromTypePredicateNode、instantiateTypePredicate、newTypePredicate、
//   isResolvingReturnTypeOfSignature、findMatchingSignature(s)、compareSignaturesIdentical、
//   isMatchingSignature、compareTypeParametersIdentical、compareTypePredicatesIdentical、
//   getEffectiveConstraintOfIntersection、模板字面量推断（templateLiteralTypesDefinitelyUnrelated /
//   isTypeMatchedByTemplateLiteralType / inferTypesFromTemplateLiteralType /
//   inferFromLiteralPartsToTemplateLiteral / getStringLikeTypeForType /
//   isValidTypeForTemplateLiteralPlaceholder）、isMemberOfStringMapping、
//   applyTargetStringMappingToSource、visibilityToString）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 命名（contract §1、artifacts/checker-method-collisions.md）：Go 未导出的 `getTypePredicateOfSignature`
//    与导出的 `GetTypePredicateOfSignature` 同名 → 未导出者落成 `GetTypePredicateOfSignatureWorker`；
//    Go 包级函数 `visibilityToString` → 同一个 `partial class Checker` 的 `internal static VisibilityToString`。
// 2) `[]*T` 局部累积变量（Go 的 `append`）→ C# `List<T>`（`matches`/`constraints`），传给收 `T[]` 的成员时
//    `.ToArray()`（contract §3、先例 Checker.C4.ObjectLiteral.cs 文件头第 4 条）。
//    返回 `[]*T` 的地方仍是 `T[]`：Go 的 nil 返回在 C# 里落成 `[]`——这些返回值只被 `len()`/下标/`!= nil`
//    消费，`isTypeMatchedByTemplateLiteralType` 的 `inferences != nil` 因此改写成 `inferences.Length != 0`
//    （Go 里非 nil 的返回值至少含一个元素，语义等价）。
// 3) `stringutil.CombineSurrogatePairs` 不移植（本仓 C# string 就是 UTF-16 序列，见 StringUtil/Util.cs 文件头
//    §3.1）→ 落成恒等，`getStringLiteralType(CombineSurrogatePairs(t))` 直接写 `GetStringLiteralType(t)`。
// 4) `stringutil.DecodeJSStringRune(sourceText[pos:])` 只用到 size → 内联代理对判定（与 Scanner.Escapes.cs 的
//    `DecodeJSStringRune` 同规则：合法代理对消耗 2 个 UTF-16 代码单元，否则 1），既避免每轮 Substring 分配，
//    也不需要额外 using。Go 注释里"一次消费一个**码点**"的意图被保留。
// 5) Go 的 UTF-8 **字节**下标与切片（`texts[i]`、`t[:n]`、`t[p:]`）→ C# 的 UTF-16 **代码单元**下标与 range
//    （`[..n]`/`[p..]`）：两边都是"按同一单位取前缀/后缀/子串并比较"，行为一致（`slices.Equal` → `Core.Same`）。
// 6) `strings.Index(getSourceText(s)[p:], delim)` 返回**相对**偏移（Go 调用点 `p += d`）→ C# 用**绝对**下标的
//    `IndexOf(delim, p, StringComparison.Ordinal)`（调用点 `p = d`），语义等价。
// 7) Go 的 `a == b`（`*Type`/`*Signature`/`*TypePredicate`/字符串）逐字保留：这些类型都没有重载 `==`，
//    引用相等即 Go 的指针相等；`interfaces{source, target *Type} Ternary` 形参 → `TypeRelatedComparer`。
// 8) `findMatchingSignature` 里按 partialMatch 二选一的 `c.compareTypesSubtypeOf`/`c.compareTypesIdentical`
//    是 Go 的**方法值**：C# 里方法组需要目标类型，故显式声明 `TypeRelatedComparer compareTypes = ...;`
//    （目标类型化的条件表达式；写成 `var` 会因两个方法组没有公共类型而编译失败）。
// 9) Go 的 `switch { case ...: }`（`getTypePredicateOfSignature` 的两处、`inferTypesFromTemplateLiteralType`、
//    `isValidTypeForTemplateLiteralPlaceholder`、`isMemberOfStringMapping`、`compareTypePredicatesIdentical`）
//    → 等价的 if/else-if 链（顺序敏感处逐字保持 Go 的分支顺序）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal TypePredicate? GetTypePredicateOfSignatureWorker(Signature sig)
    {
        if (sig._resolvedTypePredicate == null)
        {
            if (sig._target != null)
            {
                var targetTypePredicate = GetTypePredicateOfSignatureWorker(sig._target!);
                if (targetTypePredicate != null)
                {
                    sig._resolvedTypePredicate = InstantiateTypePredicate(targetTypePredicate, sig._mapper);
                }
            }
            else if (sig._composite != null)
            {
                sig._resolvedTypePredicate = GetUnionOrIntersectionTypePredicate(sig._composite._signatures!, sig._composite._isUnion);
            }
            else if (sig._declaration != null)
            {
                var typeNode = sig._declaration!.Type();
                if (typeNode != null)
                {
                    if (IsTypePredicateNode(typeNode))
                    {
                        sig._resolvedTypePredicate = CreateTypePredicateFromTypePredicateNode(typeNode, sig);
                    }
                }
                else if (IsFunctionLikeDeclaration(sig._declaration) && (sig._resolvedReturnType == null || (sig._resolvedReturnType._flags & TypeFlags.Boolean) != 0) && GetParameterCount(sig) > 0)
                {
                    sig._resolvedTypePredicate = _noTypePredicate; // avoid infinite loop
                    sig._resolvedTypePredicate = GetTypePredicateFromBody(sig._declaration!);
                }
            }
            if (sig._resolvedTypePredicate == null)
            {
                sig._resolvedTypePredicate = _noTypePredicate;
            }
        }
        if (sig._resolvedTypePredicate == _noTypePredicate)
        {
            return null;
        }
        return sig._resolvedTypePredicate;
    }

    internal TypePredicate? GetUnionOrIntersectionTypePredicate(Signature[] signatures, bool isUnion)
    {
        TypePredicate? last = null;
        var types = new List<Type>();
        foreach (var sig in signatures)
        {
            var pred = GetTypePredicateOfSignatureWorker(sig);
            if (pred != null)
            {
                // Constituent type predicates must all have matching kinds. We don't create composite type predicates for assertions.
                if (pred._kind != TypePredicateKind.This && pred._kind != TypePredicateKind.Identifier || last != null && !TypePredicateKindsMatch(last, pred))
                {
                    return null;
                }
                last = pred;
                types.Add(pred._t!);
            }
            else
            {
                // In composite union signatures we permit and ignore signatures with a return type `false`.
                Type? returnType = null;
                if (isUnion)
                {
                    returnType = GetReturnTypeOfSignatureWorker(sig);
                }
                if (returnType != _falseType && returnType != _regularFalseType)
                {
                    return null;
                }
            }
        }
        if (last == null)
        {
            return null;
        }
        var compositeType = GetUnionOrIntersectionType(types.ToArray(), isUnion, UnionReduction.Literal);
        return NewTypePredicate(last._kind, last._parameterName, last._parameterIndex, compositeType);
    }

    internal bool TypePredicateKindsMatch(TypePredicate a, TypePredicate b)
    {
        return a._kind == b._kind && a._parameterIndex == b._parameterIndex;
    }

    internal TypePredicate CreateTypePredicateFromTypePredicateNode(Node node, Signature signature)
    {
        var predicateNode = node.AsTypePredicateNode()!;
        Type? t = null;
        if (predicateNode.Type != null)
        {
            t = GetTypeFromTypeNodeWorker(predicateNode.Type!);
        }
        if (IsThisTypeNode(predicateNode.ParameterName!))
        {
            var kind = IfElse(predicateNode.AssertsModifier != null, TypePredicateKind.AssertsThis, TypePredicateKind.This);
            return NewTypePredicate(kind, "" /*parameterName*/, 0 /*parameterIndex*/, t);
        }
        var kind2 = IfElse(predicateNode.AssertsModifier != null, TypePredicateKind.AssertsIdentifier, TypePredicateKind.Identifier);
        var name = predicateNode.ParameterName!.Text();
        var index = FindIndex(signature.Parameters(), p => p.Name == name);
        return NewTypePredicate(kind2, name, index, t);
    }

    internal TypePredicate InstantiateTypePredicate(TypePredicate predicate, TypeMapper? mapper)
    {
        var t = InstantiateType(predicate._t!, mapper!);
        if (t == predicate._t)
        {
            return predicate;
        }
        return NewTypePredicate(predicate._kind, predicate._parameterName, predicate._parameterIndex, t);
    }

    internal TypePredicate NewTypePredicate(TypePredicateKind kind, string parameterName, int parameterIndex, Type? t)
    {
        return new TypePredicate { _kind = kind, _parameterIndex = parameterIndex, _parameterName = parameterName, _t = t };
    }

    internal bool IsResolvingReturnTypeOfSignature(Signature signature)
    {
        if (signature._composite != null && Some(signature._composite._signatures, IsResolvingReturnTypeOfSignature))
        {
            return true;
        }
        return signature._resolvedReturnType == null && FindResolutionCycleStartIndex(signature, TypeSystemPropertyName.ResolvedReturnType) >= 0;
    }

    internal Signature[]? FindMatchingSignatures(Signature[][] signatureLists, Signature signature, int listIndex)
    {
        if (signature.TypeParameters().Length != 0)
        {
            // We require an exact match for generic signatures, so we only return signatures from the first
            // signature list and only if they have exact matches in the other signature lists.
            if (listIndex > 0)
            {
                return null;
            }
            for (var i = 1; i < signatureLists.Length; i++)
            {
                if (FindMatchingSignature(signatureLists[i], signature, false /*partialMatch*/, false /*ignoreThisTypes*/, false /*ignoreReturnTypes*/) == null)
                {
                    return null;
                }
            }
            return [signature];
        }
        Signature[]? result = null;
        for (var i = 0; i < signatureLists.Length; i++)
        {
            // Allow matching non-generic signatures to have excess parameters (as a fallback if exact parameter match is not found) and different return types.
            // Prefer matching this types if possible.
            Signature? match;
            if (i == listIndex)
            {
                match = signature;
            }
            else
            {
                match = FindMatchingSignature(signatureLists[i], signature, false /*partialMatch*/, false /*ignoreThisTypes*/, true /*ignoreReturnTypes*/);
                if (match == null)
                {
                    match = FindMatchingSignature(signatureLists[i], signature, true /*partialMatch*/, false /*ignoreThisTypes*/, true /*ignoreReturnTypes*/);
                }
            }
            if (match == null)
            {
                return null;
            }
            result = AppendIfUnique(result, match);
        }
        return result;
    }

    // Go: `findMatchingSignature(signatureList []*Signature, ...)`。`signatureList` 允许是 nil 切片
    // （`range nil` 合法，零次迭代；`getSignaturesOfType` 对没有签名的类型就返回 nil），
    // 所以参数声明为可空并按 `len(nil) == 0` 归一 —— `GetUnionSignatures` 传给
    // `FindMatchingSignatures` 的矩阵里，尚未做非空校验的靠后元素仍可能是 nil。
    internal Signature? FindMatchingSignature(Signature[]? signatureList, Signature signature, bool partialMatch, bool ignoreThisTypes, bool ignoreReturnTypes)
    {
        TypeRelatedComparer compareTypes = partialMatch ? CompareTypesSubtypeOf : CompareTypesIdentical;
        foreach (var s in signatureList ?? [])
        {
            if (CompareSignaturesIdentical(s, signature, partialMatch, ignoreThisTypes, ignoreReturnTypes, compareTypes) != Ternary.False)
            {
                return s;
            }
        }
        return null;
    }

    /**
     * See signatureRelatedTo, compareSignaturesIdentical
     */
    internal Ternary CompareSignaturesIdentical(Signature source, Signature target, bool partialMatch, bool ignoreThisTypes, bool ignoreReturnTypes, TypeRelatedComparer compareTypes)
    {
        if (source == target)
        {
            return Ternary.True;
        }
        if (!IsMatchingSignature(source, target, partialMatch))
        {
            return Ternary.False;
        }
        // Check that the two signatures have the same number of type parameters.
        var sourceTypeParameters = source.TypeParameters();
        var targetTypeParameters = target.TypeParameters();
        if (sourceTypeParameters.Length != targetTypeParameters.Length)
        {
            return Ternary.False;
        }
        // Check that type parameter constraints and defaults match. If they do, instantiate the source
        // signature with the type parameters of the target signature and continue the comparison.
        if (targetTypeParameters.Length != 0)
        {
            var mapper = NewTypeMapper(sourceTypeParameters, targetTypeParameters);
            for (var i = 0; i < targetTypeParameters.Length; i++)
            {
                var s = sourceTypeParameters[i];
                var t = targetTypeParameters[i];
                if (!(s == t || compareTypes(InstantiateType(GetConstraintOrUnknownFromTypeParameter(s), mapper), GetConstraintOrUnknownFromTypeParameter(t)) != Ternary.False &&
                    compareTypes(InstantiateType(GetDefaultOrUnknownFromTypeParameter(s), mapper), GetDefaultOrUnknownFromTypeParameter(t)) != Ternary.False))
                {
                    return Ternary.False;
                }
            }
            source = InstantiateSignatureEx(source, mapper, true /*eraseTypeParameters*/);
        }
        var result = Ternary.True;
        if (!ignoreThisTypes)
        {
            var sourceThisType = GetThisTypeOfSignature(source);
            if (sourceThisType != null)
            {
                var targetThisType = GetThisTypeOfSignature(target);
                if (targetThisType != null)
                {
                    var related = compareTypes(sourceThisType, targetThisType);
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }
                    result &= related;
                }
            }
        }
        for (var i = 0; i < GetParameterCount(target); i++)
        {
            var s = GetTypeAtPositionWorker(source, i);
            var t = GetTypeAtPositionWorker(target, i);
            var related = compareTypes(t!, s!);
            if (related == Ternary.False)
            {
                return Ternary.False;
            }
            result &= related;
        }
        if (!ignoreReturnTypes)
        {
            var sourceTypePredicate = GetTypePredicateOfSignatureWorker(source);
            var targetTypePredicate = GetTypePredicateOfSignatureWorker(target);
            if (sourceTypePredicate != null || targetTypePredicate != null)
            {
                result &= CompareTypePredicatesIdentical(sourceTypePredicate, targetTypePredicate, compareTypes);
            }
            else
            {
                result &= compareTypes(GetReturnTypeOfSignatureWorker(source), GetReturnTypeOfSignatureWorker(target));
            }
        }
        return result;
    }

    internal bool IsMatchingSignature(Signature source, Signature target, bool partialMatch)
    {
        var sourceParameterCount = GetParameterCount(source);
        var targetParameterCount = GetParameterCount(target);
        var sourceMinArgumentCount = GetMinArgumentCount(source);
        var targetMinArgumentCount = GetMinArgumentCount(target);
        var sourceHasRestParameter = HasEffectiveRestParameterWorker(source);
        var targetHasRestParameter = HasEffectiveRestParameterWorker(target);
        // A source signature matches a target signature if the two signatures have the same number of required,
        // optional, and rest parameters.
        if (sourceParameterCount == targetParameterCount && sourceMinArgumentCount == targetMinArgumentCount && sourceHasRestParameter == targetHasRestParameter)
        {
            return true;
        }
        // A source signature partially matches a target signature if the target signature has no fewer required
        // parameters
        if (partialMatch && sourceMinArgumentCount <= targetMinArgumentCount)
        {
            return true;
        }
        return false;
    }

    internal bool CompareTypeParametersIdentical(Type[]? sourceParams, Type[]? targetParams)
    {
        if ((sourceParams?.Length ?? 0) != (targetParams?.Length ?? 0))
        {
            return false;
        }
        // Go 的切片 nil/空等价（`len(nil) == 0`），这里两个长度相等；若都为 nil，Go 的 `range` 不执行循环体，
        // 直接返回 true。C# 的 `null.Length` 抛 NRE，所以按 Go 语义归一成空数组（等价于 `Signature.TypeParameters()`）。
        sourceParams ??= [];
        targetParams ??= [];
        var mapper = NewTypeMapper(targetParams, sourceParams);
        for (var i = 0; i < sourceParams.Length; i++)
        {
            var source = sourceParams[i];
            var target = targetParams[i];
            if (source == target)
            {
                continue;
            }
            // We instantiate the target type parameter constraints into the source types so we can recognize `<T, U extends T>` as the same as `<A, B extends A>`
            if (!IsTypeIdenticalTo(GetConstraintFromTypeParameter(source) ?? _unknownType, InstantiateType(GetConstraintFromTypeParameter(target) ?? _unknownType, mapper)))
            {
                return false;
            }
            // We don't compare defaults - we just use the type parameter defaults from the first signature that seems to match.
            // It might make sense to combine these defaults in the future, but doing so intelligently requires knowing
            // if the parameter is used covariantly or contravariantly (so we intersect if it's used like a parameter or union if used like a return type)
            // and, since it's just an inference _default_, just picking one arbitrarily works OK.
        }
        return true;
    }

    internal Ternary CompareTypePredicatesIdentical(TypePredicate? source, TypePredicate? target, TypeRelatedComparer compareTypes)
    {
        if (source == null || target == null || !TypePredicateKindsMatch(source, target))
        {
            return Ternary.False;
        }
        if (source._t == target._t)
        {
            return Ternary.True;
        }
        if (source._t != null && target._t != null)
        {
            return compareTypes(source._t!, target._t!);
        }
        return Ternary.False;
    }

    internal Type? GetEffectiveConstraintOfIntersection(Type[] types, bool targetIsUnion)
    {
        var constraints = new List<Type>();
        var hasDisjointDomainType = false;
        foreach (var t in types)
        {
            if ((t._flags & TypeFlags.Instantiable) != 0)
            {
                // We keep following constraints as long as we have an instantiable type that is known
                // not to be circular or infinite (hence we stop on index access types).
                var constraint = GetConstraintOfType(t);
                while (constraint != null && (constraint._flags & (TypeFlags.TypeParameter | TypeFlags.Index | TypeFlags.Conditional)) != 0)
                {
                    constraint = GetConstraintOfType(constraint);
                }
                if (constraint != null)
                {
                    constraints.Add(constraint);
                    if (targetIsUnion)
                    {
                        constraints.Add(t);
                    }
                }
            }
            else if ((t._flags & TypeFlags.DisjointDomains) != 0 || IsEmptyAnonymousObjectType(t))
            {
                hasDisjointDomainType = true;
            }
        }
        // If the target is a union type or if we are intersecting with types belonging to one of the
        // disjoint domains, we may end up producing a constraint that hasn't been examined before.
        if (constraints.Count != 0 && (targetIsUnion || hasDisjointDomainType))
        {
            if (hasDisjointDomainType)
            {
                // We add any types belong to one of the disjoint domains because they might cause the final
                // intersection operation to reduce the union constraints.
                foreach (var t in types)
                {
                    if ((t._flags & TypeFlags.DisjointDomains) != 0 || IsEmptyAnonymousObjectType(t))
                    {
                        constraints.Add(t);
                    }
                }
            }
            // The source types were normalized; ensure the result is normalized too.
            return GetNormalizedType(GetIntersectionTypeEx(constraints.ToArray(), IntersectionFlags.NoConstraintReduction, null /*alias*/), false /*writing*/);
        }
        return null;
    }

    internal bool TemplateLiteralTypesDefinitelyUnrelated(TemplateLiteralType source, TemplateLiteralType target)
    {
        // Two template literal types with differences in their starting or ending text spans are definitely unrelated.
        var sourceTexts = source._texts!;
        var targetTexts = target._texts!;
        var sourceStart = sourceTexts[0];
        var targetStart = targetTexts[0];
        var sourceEnd = sourceTexts[sourceTexts.Length - 1];
        var targetEnd = targetTexts[targetTexts.Length - 1];
        var startLen = Math.Min(sourceStart.Length, targetStart.Length);
        var endLen = Math.Min(sourceEnd.Length, targetEnd.Length);
        return sourceStart[..startLen] != targetStart[..startLen] || sourceEnd[(sourceEnd.Length - endLen)..] != targetEnd[(targetEnd.Length - endLen)..];
    }

    internal bool IsTypeMatchedByTemplateLiteralType(Type source, TemplateLiteralType target, TypeComparer compareTypes)
    {
        var inferences = InferTypesFromTemplateLiteralType(source, target, compareTypes);
        if (inferences.Length != 0)
        {
            for (var i = 0; i < inferences.Length; i++)
            {
                if (!IsValidTypeForTemplateLiteralPlaceholder(inferences[i], target._types![i], compareTypes))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    internal Type[] InferTypesFromTemplateLiteralType(Type source, TemplateLiteralType target, TypeComparer compareTypes)
    {
        if ((source._flags & TypeFlags.StringLiteral) != 0)
        {
            return InferFromLiteralPartsToTemplateLiteral([GetStringLiteralValue(source)], null, target);
        }
        if ((source._flags & TypeFlags.TemplateLiteral) != 0)
        {
            if (Same(source.AsTemplateLiteralType()!._texts, target._texts))
            {
                return MapIndex(source.AsTemplateLiteralType()!._types, (s, i) =>
                {
                    if (compareTypes(GetBaseConstraintOrType(s), GetBaseConstraintOrType(target._types![i]), false /*partialMatch*/) != Ternary.False)
                    {
                        return s;
                    }
                    return GetStringLikeTypeForType(s);
                });
            }
            return InferFromLiteralPartsToTemplateLiteral(source.AsTemplateLiteralType()!._texts!, source.AsTemplateLiteralType()!._types, target);
        }
        return [];
    }

    // This function infers from the text parts and type parts of a source literal to a target template literal. The number
    // of text parts is always one more than the number of type parts, and a source string literal is treated as a source
    // with one text part and zero type parts. The function returns an array of inferred string or template literal types
    // corresponding to the placeholders in the target template literal, or undefined if the source doesn't match the target.
    //
    // We first check that the starting source text part matches the starting target text part, and that the ending source
    // text part ends matches the ending target text part. We then iterate through the remaining target text parts, finding
    // a match for each in the source and inferring string or template literal types created from the segments of the source
    // that occur between the matches. During this iteration, seg holds the index of the current text part in the sourceTexts
    // array and pos holds the current character position in the current text part.
    //
    // Consider inference from type `<<${string}>.<${number}-${number}>>` to type `<${string}.${string}>`, i.e.
    //
    //   sourceTexts = ['<<', '>.<', '-', '>>']
    //   sourceTypes = [string, number, number]
    //   target.texts = ['<', '.', '>']
    //
    // We first match '<' in the target to the start of '<<' in the source and '>' in the target to the end of '>>' in
    // the source. The first match for the '.' in target occurs at character 1 in the source text part at index 1, and thus
    // the first inference is the template literal type `<${string}>`. The remainder of the source makes up the second
    // inference, the template literal type `<${number}-${number}>`.
    internal Type[] InferFromLiteralPartsToTemplateLiteral(string[] sourceTexts, Type[]? sourceTypes, TemplateLiteralType target)
    {
        var lastSourceIndex = sourceTexts.Length - 1;
        var sourceStartText = sourceTexts[0];
        var sourceEndText = sourceTexts[lastSourceIndex];
        var targetTexts = target._texts!;
        var lastTargetIndex = targetTexts.Length - 1;
        var targetStartText = targetTexts[0];
        var targetEndText = targetTexts[lastTargetIndex];
        if (lastSourceIndex == 0 && sourceStartText.Length < targetStartText.Length + targetEndText.Length || !sourceStartText.StartsWith(targetStartText, StringComparison.Ordinal) || !sourceEndText.EndsWith(targetEndText, StringComparison.Ordinal))
        {
            return [];
        }
        var remainingEndText = sourceEndText[..(sourceEndText.Length - targetEndText.Length)];
        var seg = 0;
        var pos = targetStartText.Length;
        var matches = new List<Type>();
        string getSourceText(int index)
        {
            if (index < lastSourceIndex)
            {
                return sourceTexts[index];
            }
            return remainingEndText;
        }
        void addMatch(int s, int p)
        {
            Type matchType;
            if (s == seg)
            {
                matchType = GetStringLiteralType(getSourceText(s)[pos..p]);
            }
            else
            {
                var matchTexts = new string[s - seg + 1];
                matchTexts[0] = sourceTexts[seg][pos..];
                Array.Copy(sourceTexts, seg + 1, matchTexts, 1, s - seg - 1);
                matchTexts[s - seg] = getSourceText(s)[..p];
                matchType = GetTemplateLiteralType(matchTexts, (sourceTypes ?? [])[seg..s]);
            }
            matches.Add(matchType);
            seg = s;
            pos = p;
        }
        for (var i = 1; i < lastTargetIndex; i++)
        {
            var delim = targetTexts[i];
            if (delim.Length > 0)
            {
                var s = seg;
                var p = pos;
                while (true)
                {
                    var d = getSourceText(s).IndexOf(delim, p, StringComparison.Ordinal);
                    if (d >= 0)
                    {
                        p = d;
                        break;
                    }
                    s++;
                    if (s == sourceTexts.Length)
                    {
                        return [];
                    }
                    p = 0;
                }
                addMatch(s, p);
                pos += delim.Length;
            }
            else
            {
                var sourceText = getSourceText(seg);
                if (pos < sourceText.Length)
                {
                    // Consume one code point at a time, matching the string iterator
                    // (`[x, ..._] = s`) rather than UTF-16 code-unit indexing (`s[0]`).
                    // Go: `_, size := stringutil.DecodeJSStringRune(sourceText[pos:])` —— size 是消耗的
                    // UTF-16 代码单元数（合法代理对 = 2，否则 = 1，与 Scanner.Escapes.cs 的
                    // DecodeJSStringRune 同规则）；这里内联以免每轮做一次 Substring。
                    var size = char.IsHighSurrogate(sourceText[pos]) && pos + 1 < sourceText.Length && char.IsLowSurrogate(sourceText[pos + 1]) ? 2 : 1;
                    addMatch(seg, pos + size);
                }
                else if (seg < lastSourceIndex)
                {
                    addMatch(seg + 1, 0);
                }
                else
                {
                    return [];
                }
            }
        }
        addMatch(lastSourceIndex, getSourceText(lastSourceIndex).Length);
        return matches.ToArray();
    }

    internal Type GetStringLikeTypeForType(Type t)
    {
        if ((t._flags & (TypeFlags.Any | TypeFlags.StringLike)) != 0)
        {
            return t;
        }
        return GetTemplateLiteralType(["", ""], [t]);
    }

    internal bool IsValidTypeForTemplateLiteralPlaceholder(Type source, Type target, TypeComparer compareTypes)
    {
        if ((target._flags & TypeFlags.Intersection) != 0)
        {
            return Every(target.Types(), t => t == _emptyTypeLiteralType || IsValidTypeForTemplateLiteralPlaceholder(source, t, compareTypes));
        }
        if ((target._flags & TypeFlags.String) != 0 || compareTypes(source, target, false) != Ternary.False)
        {
            return true;
        }
        if ((source._flags & TypeFlags.StringLiteral) != 0)
        {
            var value = GetStringLiteralValue(source);
            return (target._flags & TypeFlags.Number) != 0 && IsValidNumberString(value, false /*roundTripOnly*/) ||
                (target._flags & TypeFlags.BigInt) != 0 && IsValidBigIntString(value, false /*roundTripOnly*/) ||
                (target._flags & (TypeFlags.BooleanLiteral | TypeFlags.Nullable)) != 0 && value == target.AsIntrinsicType()!._intrinsicName ||
                (target._flags & TypeFlags.StringMapping) != 0 && IsMemberOfStringMapping(source, target) ||
                (target._flags & TypeFlags.TemplateLiteral) != 0 && IsTypeMatchedByTemplateLiteralType(source, target.AsTemplateLiteralType()!, compareTypes);
        }
        if ((source._flags & TypeFlags.TemplateLiteral) != 0)
        {
            var texts = source.AsTemplateLiteralType()!._texts!;
            return texts.Length == 2 && texts[0] == "" && texts[1] == "" && compareTypes(source.AsTemplateLiteralType()!._types![0], target, false) != Ternary.False;
        }
        return false;
    }

    internal bool IsMemberOfStringMapping(Type source, Type target)
    {
        if ((target._flags & TypeFlags.Any) != 0)
        {
            return true;
        }
        if ((target._flags & (TypeFlags.String | TypeFlags.TemplateLiteral)) != 0)
        {
            return IsTypeAssignableToWorker(source, target);
        }
        if ((target._flags & TypeFlags.StringMapping) != 0)
        {
            // We need to see whether applying the same mappings of the target
            // onto the source would produce an identical type *and* that
            // it's compatible with the inner-most non-string-mapped type.
            //
            // The intuition here is that if same mappings don't affect the source at all,
            // and the source is compatible with the unmapped target, then they must
            // still reside in the same domain.
            var (mapped, inner) = ApplyTargetStringMappingToSource(source, target);
            return mapped == source && IsMemberOfStringMapping(source, inner);
        }
        return false;
    }

    internal (Type Source, Type Inner) ApplyTargetStringMappingToSource(Type source, Type target)
    {
        var inner = target.AsStringMappingType()!._target!;
        if ((inner._flags & TypeFlags.StringMapping) != 0)
        {
            (source, inner) = ApplyTargetStringMappingToSource(source, inner);
        }
        return (GetStringMappingType(target._symbol!, source), inner);
    }

    internal static string VisibilityToString(ModifierFlags flags)
    {
        if (flags == ModifierFlags.Private)
        {
            return "private";
        }
        if (flags == ModifierFlags.Protected)
        {
            return "protected";
        }
        return "public";
    }
}

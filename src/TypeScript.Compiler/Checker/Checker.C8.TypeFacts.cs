// ported from: internal/checker/checker.go:31301-31988
//
// 与 Go 的差异（逐条，一行一条理由；其余逐行对应，含注释与分支顺序）：
// 1) 包级函数 `isZeroBigInt` → 同一个 `public sealed partial class Checker` 的 `internal static`（checker-plan §5.5；
//    C# 没有自由函数）。
// 2) 同名方法冲突（artifacts/checker-method-collisions.md）：本片定义的 `isContextSensitive`→`IsContextSensitiveWorker`、
//    `getAwaitedType`→`GetAwaitedTypeWorker`（Go 导出的 `IsContextSensitive`/`GetAwaitedType` 在 exports.go:350/272，
//    本片不定义）；本片**调用**的未导出同名者也按清单改名：`getBaseConstraintOfType`→`GetBaseConstraintOfTypeWorker`、
//    `getContextualType`→`GetContextualTypeWorker`、`getSignaturesOfType`→`GetSignaturesOfTypeWorker`、
//    `getTypeOfPropertyOfType`→`GetTypeOfPropertyOfTypeWorker`、`getUnionType`→`GetUnionTypeWorker`。
// 3) Go 未导出字段 → `_camelCase`（§5.5）：`c.strictNullChecks`→`_strictNullChecks`、`c.unknownType`→`_unknownType`、
//    `c.unknownUnionType`→`_unknownUnionType`、`c.errorType`→`_errorType`、`c.emptyObjectType`→`_emptyObjectType`、
//    `c.autoType`→`_autoType`、`c.autoArrayType`→`_autoArrayType`、`c.cachedTypes`→`_cachedTypes`、
//    `c.awaitedTypeStack`→`_awaitedTypeStack`、`c.inferenceContextInfos`→`_inferenceContextInfos`、
//    `t.flags`→`t._flags`、`t.objectFlags`→`t._objectFlags`、`t.id`→`t._id`、`t.alias`→`t._alias`、
//    `t.alias.symbol`→`t._alias._symbol`、`t.alias.typeArguments`→`t._alias._typeArguments`、
//    `resolved.signatures`→`resolved._signatures`、`resolved.members`→`resolved._members`、
//    `t.AsIntersectionType().types`→`t.AsIntersectionType()!._types`、`t.AsSubstitutionType().baseType`→`!._baseType`。
// 4) Go 的无标签 `switch { case cond: }` → C# `switch (x) { case { } when (cond): }`（先例 Checker.C2.ClassLike.cs 的
//    `GetTypeWithoutSignatures`）；Go 的多值 `case a, b:` → C# 连续 `case a: case b:`（`getAdjustedTypeWithFacts`）。
// 5) Go 把实例方法当函数值传（`core.Some(xs, c.isContextSensitive)`）→ C# 方法组（签名匹配时合法，ChkRelater 探针确认）；
//    Go 里本来就是闭包的（`ForEachReturnStatement`/`mapType`/`filterType` 的参数）照写 lambda。
// 6) `args ...any` → `params object?[]? args`：Go 的 nil 可变参数 = 零个实参，C# 转发时原样传数组（params 数组可直接传），
//    传给非 nullable 的 `NewDiagnosticChainForNode(..., params object?[] args)` 时写 `args ?? []`。
// 7) Go 的 `thisTypeForError **Type` 出参 → `out Type? thisTypeForError`（调用点 `out var thisTypeForError`；只用作
//    错误信息时 Go 传 nil → C# `out _`）。
// 8) 两个字段切片在 C# 侧是 `List<T>`（Checker.Struct.cs:266/267 `List<InferenceContextInfo>` / `List<Type>`）→ Go 的
//    `append` / `f[:n-1]` / `slices.Contains` 分别写成 `Add` / `RemoveAt(Count - 1)` / `Contains`，长度用 `Count`
//    （不是 §5.4 的 `Array.Resize`——字段本身就是 List，Go 的 len/cap 语义由 List 承担）。
// 9) `slices.Backward(slice)` → 反向索引循环（保序）。
// 10) `InferenceContextInfo{node, context}` → `new InferenceContextInfo(node, context)`；`InferenceContextInfo{}` →
//     `new InferenceContextInfo()`（Checker.Keys.cs:114-125 提供这两个构造器）；`CachedTypeKey{kind:…, typeId:…}` →
//     `new CachedTypeKey { _kind = …, _typeId = … }`（Checker.C3.Expressions.cs:200 先例）。
// 11) `jsnum.PseudoBigInt{}` → `default(PseudoBigInt)`（JsNum/PseudoBigInt.cs：零值即 0，`==` 已定义）。
// 12) Go 允许遮蔽局部（`if awaitedType := …` 之后又有 `awaitedType := …`；`for _, t := range t.Types()`；
//     `func(t *Type) …` 里的 t），C# 同名局部/参数遮蔽是 CS0136 → 改名为 `cachedAwaitedType`/`tt`/`x`。
// 13) Go `t = c.getBaseConstraintOfType(t); if t == nil { t = c.unknownType }` → 局部 `baseConstraint` + `?? _unknownType!`
//     （C# 可空流分析不接受把 `Type?` 赋回非空参数 `t`）。
// 14) Go 里可能为 nil 的 `*ast.Node`/`*diagnostics.Message` 参数 → `Node?`/`Message?`；调用 C# Ast 的非空形参
//     （`IsBlock(Node)`/`IsIdentifier(Node)`/`NodeKindIs` 等）前按不变量用 `!`（docs/ast.md §2）。
// 15) `diagnostics.X` → `Diagnostics.X`（C# 大小写敏感；先例 Checker.C2.ClassLike.cs），故本文件带 `using TypeScript.Diagnostics;`。
// 16) Go 第 31299-31300 行的文档注释属于本片第一个声明（`isContextSensitive`），随本片一并移植（在 31301 之前一行）。
// 17) `TypeFacts` 枚举（C1，checker.go:399-）成员名 = Go 常量名去 `TypeFacts` 前缀（§3.3）；本片引用的位组合逐个照抄
//     Go 的 `|` 表达式（不内联数值），`TypeFactsNone`→`TypeFacts.None`、`TypeFactsAll`→`TypeFacts.All` 等。
// 18) 类型断言 `t.data.(*X)` 在 C# 是 `t.AsX()` 后按不变量加 `!`（checker-plan §5.1）；`t.AsType()` 不移植（对象本身）。
// 19) Go 里是 **func 类型字段** 的（checker.go:837 `getGlobalNonNullableTypeAliasOrNil`、841 `getGlobalAwaitedSymbol`）在 C#
//     是委托字段（Checker.Struct.cs:283/287/288）→ 调用点写 `_getGlobalNonNullableTypeAliasOrNil()` /
//     `_getGlobalAwaitedSymbol()` / `_getGlobalAwaitedSymbolOrNil()`，不能写成方法调用。
// 20) 字段按 Go `len(nil)==0` 语义写成可空安全：`StructuredType._signatures`/`_members`（Types.Type.cs:5/3 是可空数组/可空表）
//     → `(resolved._signatures?.Length ?? 0) != 0`、`resolved._members?.GetValueOrDefault("bind")`；`TypeAlias._typeArguments`
//     （Types.Type.cs:35 可空）→ `?.Length == 1` / `!..[0]`；`GetSignaturesOfTypeWorker` 返回 `Signature[]?`
//     → `(…?.Length ?? 0) != 0`。
// 21) `MapType` 的委托签名是 `Func<Type, Type>`（返回非空），Go 的 `mapType` 回调允许返回 nil → union 分支里
//     `GetAwaitedTypeNoAliasEx(...)!`（Go 在该分支返回 nil 也会让 getUnionType 拿到 nil 而 panic，不变量成立）。
// 22) C# 实测：`switch (x) { case { } when (…) }` 之后再使用 subject，编译器把 `x` 视为可能为 null（CS8603，
//     已用探针确认）→ `unwrapAwaitedType` / `convertAutoToAny` 末尾的 `return t` 写成 `return t!`。
namespace TypeScript.Checker;

using System;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    // Returns true if the given expression contains (at any level of nesting) a function or arrow expression
    // that is subject to contextual typing.
    internal bool IsContextSensitiveWorker(Node node)
    {
        switch (node.Kind)
        {
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.MethodDeclaration:
            case Kind.FunctionDeclaration:
                return IsContextSensitiveFunctionLikeDeclaration(node);
            case Kind.ObjectLiteralExpression:
                return Some(node.Properties(), IsContextSensitiveWorker);
            case Kind.ArrayLiteralExpression:
                return Some(node.Elements(), IsContextSensitiveWorker);
            case Kind.ConditionalExpression:
                return IsContextSensitiveWorker(node.AsConditionalExpression()!.WhenTrue!) || IsContextSensitiveWorker(node.AsConditionalExpression()!.WhenFalse!);
            case Kind.BinaryExpression:
                {
                    var binary = node.AsBinaryExpression()!;
                    return NodeKindIs(binary.OperatorToken, Kind.BarBarToken, Kind.QuestionQuestionToken) && (IsContextSensitiveWorker(binary.Left!) || IsContextSensitiveWorker(binary.Right!));
                }
            case Kind.PropertyAssignment:
                return IsContextSensitiveWorker(node.Initializer()!);
            case Kind.ParenthesizedExpression:
                return IsContextSensitiveWorker(node.Expression()!);
            case Kind.JsxAttributes:
                return Some(node.Properties(), IsContextSensitiveWorker) || IsJsxOpeningElement(node.Parent!) && Some(node.Parent!.Parent!.Children()!.Nodes, IsContextSensitiveWorker);
            case Kind.JsxAttribute:
                {
                    // If there is no initializer, JSX attribute has a boolean value of true which is not context sensitive.
                    var initializer = node.Initializer();
                    return initializer != null && IsContextSensitiveWorker(initializer);
                }
            case Kind.JsxExpression:
                {
                    // It is possible to that node.expression is undefined (e.g <div x={} />)
                    var expression = node.Expression();
                    return expression != null && IsContextSensitiveWorker(expression);
                }
            case Kind.YieldExpression:
                {
                    var expression = node.Expression();
                    return expression != null && IsContextSensitiveWorker(expression);
                }
        }
        return false;
    }

    internal bool IsContextSensitiveFunctionLikeDeclaration(Node node)
    {
        return HasContextSensitiveParameters(node) || HasContextSensitiveReturnExpression(node) || HasContextSensitiveYieldExpression(node);
    }

    internal bool HasContextSensitiveReturnExpression(Node node)
    {
        if (node.TypeParameters() != null || node.Type() != null)
        {
            return false;
        }
        var body = node.Body();
        if (body == null)
        {
            return false;
        }
        if (!IsBlock(body))
        {
            return IsContextSensitiveWorker(body);
        }
        return ForEachReturnStatement(body, statement => statement.Expression() != null && IsContextSensitiveWorker(statement.Expression()!));
    }

    internal bool HasContextSensitiveYieldExpression(Node node)
    {
        return (GetFunctionFlags(node) & FunctionFlags.Generator) != 0 && node.Body() != null && ForEachYieldExpression(node.Body()!, IsContextSensitiveWorker);
    }

    internal void PushInferenceContext(Node node, InferenceContext context)
    {
        _inferenceContextInfos.Add(new InferenceContextInfo(node, context));
    }

    internal void PopInferenceContext()
    {
        var lastIndex = _inferenceContextInfos.Count - 1;
        _inferenceContextInfos[lastIndex] = new InferenceContextInfo();
        _inferenceContextInfos.RemoveAt(lastIndex);
    }

    internal InferenceContext? GetInferenceContext(Node node)
    {
        for (var i = _inferenceContextInfos.Count - 1; i >= 0; i--)
        {
            var v = _inferenceContextInfos[i];
            if (IsNodeDescendantOf(node, v._node!))
            {
                return v._context;
            }
        }
        return null;
    }

    internal TypeFacts GetTypeFacts(Type t, TypeFacts mask)
    {
        return GetTypeFactsWorker(t, mask) & mask;
    }

    internal bool HasTypeFacts(Type t, TypeFacts mask)
    {
        return GetTypeFacts(t, mask) != 0;
    }

    internal TypeFacts GetTypeFactsWorker(Type t, TypeFacts callerOnlyNeeds)
    {
        if ((t._flags & (TypeFlags.Intersection | TypeFlags.Instantiable)) != 0)
        {
            var baseConstraint = GetBaseConstraintOfTypeWorker(t);
            t = baseConstraint ?? _unknownType!;
        }
        var flags = t._flags;
        switch (t)
        {
            case { } when (flags & (TypeFlags.String | TypeFlags.StringMapping)) != 0:
                if (_strictNullChecks)
                {
                    return TypeFacts.StringStrictFacts;
                }
                return TypeFacts.StringFacts;
            case { } when (flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral)) != 0:
                {
                    var isEmpty = (flags & TypeFlags.StringLiteral) != 0 && GetStringLiteralValue(t) == "";
                    if (_strictNullChecks)
                    {
                        if (isEmpty)
                        {
                            return TypeFacts.EmptyStringStrictFacts;
                        }
                        return TypeFacts.NonEmptyStringStrictFacts;
                    }
                    if (isEmpty)
                    {
                        return TypeFacts.EmptyStringFacts;
                    }
                    return TypeFacts.NonEmptyStringFacts;
                }
            case { } when (flags & (TypeFlags.Number | TypeFlags.Enum)) != 0:
                if (_strictNullChecks)
                {
                    return TypeFacts.NumberStrictFacts;
                }
                return TypeFacts.NumberFacts;
            case { } when (flags & TypeFlags.NumberLiteral) != 0:
                {
                    var isZero = GetNumberLiteralValue(t) == 0;
                    if (_strictNullChecks)
                    {
                        if (isZero)
                        {
                            return TypeFacts.ZeroNumberStrictFacts;
                        }
                        return TypeFacts.NonZeroNumberStrictFacts;
                    }
                    if (isZero)
                    {
                        return TypeFacts.ZeroNumberFacts;
                    }
                    return TypeFacts.NonZeroNumberFacts;
                }
            case { } when (flags & TypeFlags.BigInt) != 0:
                if (_strictNullChecks)
                {
                    return TypeFacts.BigIntStrictFacts;
                }
                return TypeFacts.BigIntFacts;
            case { } when (flags & TypeFlags.BigIntLiteral) != 0:
                {
                    var isZero = IsZeroBigInt(t);
                    if (_strictNullChecks)
                    {
                        if (isZero)
                        {
                            return TypeFacts.ZeroBigIntStrictFacts;
                        }
                        return TypeFacts.NonZeroBigIntStrictFacts;
                    }
                    if (isZero)
                    {
                        return TypeFacts.ZeroBigIntFacts;
                    }
                    return TypeFacts.NonZeroBigIntFacts;
                }
            case { } when (flags & TypeFlags.Boolean) != 0:
                if (_strictNullChecks)
                {
                    return TypeFacts.BooleanStrictFacts;
                }
                return TypeFacts.BooleanFacts;
            case { } when (flags & TypeFlags.BooleanLike) != 0:
                {
                    var isFalse = t == _falseType || t == _regularFalseType;
                    if (_strictNullChecks)
                    {
                        if (isFalse)
                        {
                            return TypeFacts.FalseStrictFacts;
                        }
                        return TypeFacts.TrueStrictFacts;
                    }
                    if (isFalse)
                    {
                        return TypeFacts.FalseFacts;
                    }
                    return TypeFacts.TrueFacts;
                }
            case { } when (flags & TypeFlags.Object) != 0:
                {
                    var possibleFacts = default(TypeFacts);
                    if (_strictNullChecks)
                    {
                        possibleFacts = TypeFacts.EmptyObjectStrictFacts | TypeFacts.FunctionStrictFacts | TypeFacts.ObjectStrictFacts;
                    }
                    else
                    {
                        possibleFacts = TypeFacts.EmptyObjectFacts | TypeFacts.FunctionFacts | TypeFacts.ObjectFacts;
                    }
                    if ((callerOnlyNeeds & possibleFacts) == 0)
                    {
                        // If the caller doesn't care about any of the facts that we could possibly produce,
                        // return zero so we can skip resolving members.
                        return TypeFacts.None;
                    }
                    switch (t)
                    {
                        case { } when (t._objectFlags & ObjectFlags.Anonymous) != 0 && IsEmptyObjectType(t):
                            if (_strictNullChecks)
                            {
                                return TypeFacts.EmptyObjectStrictFacts;
                            }
                            return TypeFacts.EmptyObjectFacts;
                        case { } when IsFunctionObjectType(t):
                            if (_strictNullChecks)
                            {
                                return TypeFacts.FunctionStrictFacts;
                            }
                            return TypeFacts.FunctionFacts;
                        case { } when _strictNullChecks:
                            return TypeFacts.ObjectStrictFacts;
                    }
                    return TypeFacts.ObjectFacts;
                }
            case { } when (flags & TypeFlags.Void) != 0:
                return TypeFacts.VoidFacts;
            case { } when (flags & TypeFlags.Undefined) != 0:
                return TypeFacts.UndefinedFacts;
            case { } when (flags & TypeFlags.Null) != 0:
                return TypeFacts.NullFacts;
            case { } when (flags & TypeFlags.ESSymbolLike) != 0:
                if (_strictNullChecks)
                {
                    return TypeFacts.SymbolStrictFacts;
                }
                else
                {
                    return TypeFacts.SymbolFacts;
                }
            case { } when (flags & TypeFlags.NonPrimitive) != 0:
                if (_strictNullChecks)
                {
                    return TypeFacts.ObjectStrictFacts;
                }
                else
                {
                    return TypeFacts.ObjectFacts;
                }
            case { } when (flags & TypeFlags.Never) != 0:
                return TypeFacts.None;
            case { } when (flags & TypeFlags.Union) != 0:
                {
                    var facts = default(TypeFacts);
                    foreach (var tt in t.Types())
                    {
                        facts |= GetTypeFactsWorker(tt, callerOnlyNeeds);
                    }
                    return facts;
                }
            case { } when (flags & TypeFlags.Intersection) != 0:
                return GetIntersectionTypeFacts(t, callerOnlyNeeds);
        }
        return TypeFacts.UnknownFacts;
    }

    internal TypeFacts GetIntersectionTypeFacts(Type t, TypeFacts callerOnlyNeeds)
    {
        // When an intersection contains a primitive type we ignore object type constituents as they are
        // presumably type tags. For example, in string & { __kind__: "name" } we ignore the object type.
        var ignoreObjects = MaybeTypeOfKind(t, TypeFlags.Primitive);
        // When computing the type facts of an intersection type, certain type facts are computed as `and`
        // and others are computed as `or`.
        var oredFacts = TypeFacts.None;
        var andedFacts = TypeFacts.All;
        foreach (var tt in t.Types())
        {
            if (!(ignoreObjects && (tt._flags & TypeFlags.Object) != 0))
            {
                var f = GetTypeFactsWorker(tt, callerOnlyNeeds);
                oredFacts |= f;
                andedFacts &= f;
            }
        }
        return (oredFacts & TypeFacts.OrFactsMask) | (andedFacts & TypeFacts.AndFactsMask);
    }

    internal static bool IsZeroBigInt(Type t)
    {
        return GetBigIntLiteralValue(t) == default(PseudoBigInt);
    }

    internal bool IsFunctionObjectType(Type t)
    {
        if ((t._objectFlags & ObjectFlags.EvolvingArray) != 0)
        {
            return false;
        }
        // We do a quick check for a "bind" property before performing the more expensive subtype
        // check. This gives us a quicker out in the common case where an object type is not a function.
        var resolved = ResolveStructuredTypeMembers(t);
        return (resolved._signatures?.Length ?? 0) != 0 || resolved._members?.GetValueOrDefault("bind") != null && IsTypeSubtypeOf(t, _globalFunctionType!);
    }

    internal Type GetTypeWithFacts(Type t, TypeFacts include)
    {
        return FilterType(t, x => HasTypeFacts(x, include));
    }

    // This function is similar to getTypeWithFacts, except that in strictNullChecks mode it replaces type
    // unknown with the union {} | null | undefined (and reduces that accordingly), and it intersects remaining
    // instantiable types with {}, {} | null, or {} | undefined in order to remove null and/or undefined.
    internal Type GetAdjustedTypeWithFacts(Type t, TypeFacts facts)
    {
        var reduced = RecombineUnknownType(GetTypeWithFacts(IfElse(_strictNullChecks && (t._flags & TypeFlags.Unknown) != 0, _unknownUnionType!, t), facts));
        if (_strictNullChecks)
        {
            switch (facts)
            {
                case TypeFacts.NEUndefined:
                    return RemoveNullableByIntersection(reduced, TypeFacts.EQUndefined, TypeFacts.EQNull, TypeFacts.IsNull, _nullType!);
                case TypeFacts.NENull:
                    return RemoveNullableByIntersection(reduced, TypeFacts.EQNull, TypeFacts.EQUndefined, TypeFacts.IsUndefined, _undefinedType!);
                case TypeFacts.NEUndefinedOrNull:
                case TypeFacts.Truthy:
                    return MapType(reduced, x =>
                    {
                        if (HasTypeFacts(x, TypeFacts.EQUndefinedOrNull))
                        {
                            return GetGlobalNonNullableTypeInstantiation(x);
                        }
                        return x;
                    });
            }
        }
        return reduced;
    }

    internal Type RemoveNullableByIntersection(Type t, TypeFacts targetFacts, TypeFacts otherFacts, TypeFacts otherIncludesFacts, Type otherType)
    {
        var facts = GetTypeFacts(t, TypeFacts.EQUndefined | TypeFacts.EQNull | TypeFacts.IsUndefined | TypeFacts.IsNull);
        // Simply return the type if it never compares equal to the target nullable.
        if ((facts & targetFacts) == 0)
        {
            return t;
        }
        // By default we intersect with a union of {} and the opposite nullable.
        var emptyAndOtherUnion = GetUnionTypeWorker(new Type[] { _emptyObjectType!, otherType });
        // For each constituent type that can compare equal to the target nullable, intersect with the above union
        // if the type doesn't already include the opposite nullable and the constituent can compare equal to the
        // opposite nullable; otherwise, just intersect with {}.
        return MapType(t, x =>
        {
            if (HasTypeFacts(x, targetFacts))
            {
                if ((facts & otherIncludesFacts) == 0 && HasTypeFacts(x, otherFacts))
                {
                    return GetIntersectionType(new Type[] { x, emptyAndOtherUnion });
                }
                return GetIntersectionType(new Type[] { x, _emptyObjectType! });
            }
            return x;
        });
    }

    internal Type RecombineUnknownType(Type t)
    {
        if (t == _unknownUnionType)
        {
            return _unknownType!;
        }
        return t;
    }

    internal Type GetGlobalNonNullableTypeInstantiation(Type t)
    {
        var alias = _getGlobalNonNullableTypeAliasOrNil();
        if (alias != null)
        {
            return GetTypeAliasInstantiation(alias, new Type[] { t }, null);
        }
        return GetIntersectionType(new Type[] { t, _emptyObjectType! });
    }

    internal Type ConvertAutoToAny(Type t)
    {
        switch (t)
        {
            case { } when t == _autoType:
                return _anyType!;
            case { } when t == _autoArrayType:
                return _anyArrayType!;
        }
        return t!;
    }

    // Gets the "awaited type" of a type.
    // @param type The type to await.
    // @param withAlias When `true`, wraps the "awaited type" in `Awaited<T>` if needed.
    // @remarks The "awaited type" of an expression is its "promised type" if the expression is a
    // Promise-like type; otherwise, it is the type of the expression. This is used to reflect
    // The runtime behavior of the `await` keyword.
    internal Type CheckAwaitedType(Type t, bool withAlias, Node? errorNode, Message? diagnosticMessage)
    {
        Type? awaitedType;
        if (withAlias)
        {
            awaitedType = GetAwaitedTypeEx(t, errorNode, diagnosticMessage);
        }
        else
        {
            awaitedType = GetAwaitedTypeNoAliasEx(t, errorNode, diagnosticMessage);
        }
        if (awaitedType != null)
        {
            return awaitedType;
        }
        return _errorType!;
    }

    // Gets the "awaited type" of a type.
    //
    // The "awaited type" of an expression is its "promised type" if the expression is a
    // Promise-like type; otherwise, it is the type of the expression. If the "promised
    // type" is itself a Promise-like, the "promised type" is recursively unwrapped until a
    // non-promise type is found.
    //
    // This is used to reflect the runtime behavior of the `await` keyword.
    internal Type? GetAwaitedTypeWorker(Type t)
    {
        return GetAwaitedTypeEx(t, null, null);
    }

    internal Type? GetAwaitedTypeEx(Type t, Node? errorNode, Message? diagnosticMessage, params object?[]? args)
    {
        var awaitedType = GetAwaitedTypeNoAliasEx(t, errorNode, diagnosticMessage, args);
        if (awaitedType != null)
        {
            return CreateAwaitedTypeIfNeeded(awaitedType);
        }
        return null;
    }

    // Gets the "awaited type" of a type without introducing an `Awaited<T>` wrapper.
    internal Type? GetAwaitedTypeNoAlias(Type t)
    {
        return GetAwaitedTypeNoAliasEx(t, null, null);
    }

    internal Type? GetAwaitedTypeNoAliasEx(Type t, Node? errorNode, Message? diagnosticMessage, params object?[]? args)
    {
        if (IsTypeAny(t))
        {
            return t;
        }
        // If this is already an `Awaited<T>`, just return it. This avoids `Awaited<Awaited<T>>` in higher-order
        if (IsAwaitedTypeInstantiation(t))
        {
            return t;
        }
        // If we've already cached an awaited type, return a possible `Awaited<T>` for it.
        var key = new CachedTypeKey { _kind = CachedTypeKind.AwaitedType, _typeId = t._id };
        if (_cachedTypes.GetValueOrDefault(key) is { } cachedAwaitedType)
        {
            return cachedAwaitedType;
        }
        // For a union, get a union of the awaited types of each constituent.
        if ((t._flags & TypeFlags.Union) != 0)
        {
            if (_awaitedTypeStack.Contains(t))
            {
                if (errorNode != null)
                {
                    Error(errorNode, Diagnostics.Type_is_referenced_directly_or_indirectly_in_the_fulfillment_callback_of_its_own_then_method);
                }
                return null;
            }
            _awaitedTypeStack.Add(t);
            var mapped = MapType(t, tt => GetAwaitedTypeNoAliasEx(tt, errorNode, diagnosticMessage, args)!);
            _awaitedTypeStack.RemoveAt(_awaitedTypeStack.Count - 1);
            _cachedTypes[key] = mapped;
            return mapped;
        }
        // If `type` is generic and should be wrapped in `Awaited<T>`, return it.
        if (IsAwaitedTypeNeeded(t))
        {
            _cachedTypes[key] = t;
            return t;
        }
        var promisedType = GetPromisedTypeOfPromiseEx(t, null /*errorNode*/, out var thisTypeForError);
        if (promisedType != null)
        {
            if (t == promisedType || _awaitedTypeStack.Contains(promisedType))
            {
                // Verify that we don't have a bad actor in the form of a promise whose
                // promised type is the same as the promise type, or a mutually recursive
                // promise. If so, we return undefined as we cannot guess the shape. If this
                // were the actual case in the JavaScript, this Promise would never resolve.
                //
                // An example of a bad actor with a singly-recursive promise type might
                // be:
                //
                //  interface BadPromise {
                //      then(
                //          onfulfilled: (value: BadPromise) => any,
                //          onrejected: (error: any) => any): BadPromise;
                //  }
                //
                // The above interface will pass the PromiseLike check, and return a
                // promised type of `BadPromise`. Since this is a self reference, we
                // don't want to keep recursing ad infinitum.
                //
                // An example of a bad actor in the form of a mutually-recursive
                // promise type might be:
                //
                //  interface BadPromiseA {
                //      then(
                //          onfulfilled: (value: BadPromiseB) => any,
                //          onrejected: (error: any) => any): BadPromiseB;
                //  }
                //
                //  interface BadPromiseB {
                //      then(
                //          onfulfilled: (value: BadPromiseA) => any,
                //          onrejected: (error: any) => any): BadPromiseA;
                //  }
                //
                if (errorNode != null)
                {
                    Error(errorNode, Diagnostics.Type_is_referenced_directly_or_indirectly_in_the_fulfillment_callback_of_its_own_then_method);
                }
                return null;
            }
            // Keep track of the type we're about to unwrap to avoid bad recursive promise types.
            // See the comments above for more information.
            _awaitedTypeStack.Add(t);
            var awaitedType = GetAwaitedTypeNoAliasEx(promisedType, errorNode, diagnosticMessage, args);
            _awaitedTypeStack.RemoveAt(_awaitedTypeStack.Count - 1);
            if (awaitedType == null)
            {
                return null;
            }
            _cachedTypes[key] = awaitedType;
            return awaitedType;
        }
        // The type was not a promise, so it could not be unwrapped any further.
        // As long as the type does not have a callable "then" property, it is
        // safe to return the type; otherwise, an error is reported and we return
        // undefined.
        //
        // An example of a non-promise "thenable" might be:
        //
        //  await { then(): void {} }
        //
        // The "thenable" does not match the minimal definition for a promise. When
        // a Promise/A+-compatible or ES6 promise tries to adopt this value, the promise
        // will never settle. We treat this as an error to help flag an early indicator
        // of a runtime problem. If the user wants to return this value from an async
        // function, they would need to wrap it in some other value. If they want it to
        // be treated as a promise, they can cast to <any>.
        if (IsThenableType(t))
        {
            if (errorNode != null)
            {
                Diagnostic? diagnostic = null;
                if (thisTypeForError != null)
                {
                    diagnostic = NewDiagnosticForNode(errorNode, Diagnostics.The_this_context_of_type_0_is_not_assignable_to_method_s_this_of_type_1, TypeToString(t), TypeToString(thisTypeForError));
                }
                AddDiagnostic(NewDiagnosticChainForNode(diagnostic, errorNode, diagnosticMessage!, args ?? []));
            }
            return null;
        }
        _cachedTypes[key] = t;
        return t;
    }

    internal bool IsAwaitedTypeInstantiation(Type t)
    {
        if ((t._flags & TypeFlags.Conditional) != 0)
        {
            var awaitedSymbol = _getGlobalAwaitedSymbolOrNil();
            return awaitedSymbol != null && t._alias != null && t._alias._symbol == awaitedSymbol && t._alias._typeArguments?.Length == 1;
        }
        return false;
    }

    internal bool IsAwaitedTypeNeeded(Type t)
    {
        // If this is already an `Awaited<T>`, we shouldn't wrap it. This helps to avoid `Awaited<Awaited<T>>` in higher-order.
        if (IsTypeAny(t) || IsAwaitedTypeInstantiation(t))
        {
            return false;
        }
        // We only need `Awaited<T>` if `T` contains possibly non-primitive types.
        if (IsGenericObjectType(t))
        {
            var baseConstraint = GetBaseConstraintOfTypeWorker(t);
            // We only need `Awaited<T>` if `T` is a type variable that has no base constraint, or the base constraint of `T` is `any`, `unknown`, `{}`, `object`,
            // or is promise-like.
            if (baseConstraint != null)
            {
                return (baseConstraint._flags & TypeFlags.AnyOrUnknown) != 0 || IsEmptyObjectType(baseConstraint) || SomeType(baseConstraint, IsThenableType);
            }
            return MaybeTypeOfKind(t, TypeFlags.TypeVariable);
        }
        return false;
    }

    internal Type CreateAwaitedTypeIfNeeded(Type t)
    {
        // We wrap type `T` in `Awaited<T>` based on the following conditions:
        // - `T` is not already an `Awaited<U>`, and
        // - `T` is generic, and
        // - One of the following applies:
        //   - `T` has no base constraint, or
        //   - The base constraint of `T` is `any`, `unknown`, `object`, or `{}`, or
        //   - The base constraint of `T` is an object type with a callable `then` method.
        if (IsAwaitedTypeNeeded(t))
        {
            var awaitedType = TryCreateAwaitedType(t);
            if (awaitedType != null)
            {
                return awaitedType;
            }
        }
        return t;
    }

    internal Type? TryCreateAwaitedType(Type t)
    {
        // Nothing to do if `Awaited<T>` doesn't exist
        var awaitedSymbol = _getGlobalAwaitedSymbol();
        if (awaitedSymbol != null)
        {
            // Unwrap unions that may contain `Awaited<T>`, otherwise its possible to manufacture an `Awaited<Awaited<T> | U>` where
            // an `Awaited<T | U>` would suffice.
            return GetTypeAliasInstantiation(awaitedSymbol, new Type[] { UnwrapAwaitedType(t) }, null);
        }
        return null;
    }

    // For a generic `Awaited<T>`, gets `T`.
    internal Type UnwrapAwaitedType(Type t)
    {
        switch (t)
        {
            case { } when (t._flags & TypeFlags.Union) != 0:
                return MapType(t, UnwrapAwaitedType);
            case { } when IsAwaitedTypeInstantiation(t):
                return t._alias!._typeArguments![0];
        }
        return t!;
    }

    internal bool IsThenableType(Type t)
    {
        if (AllTypesAssignableToKind(GetBaseConstraintOrType(t), TypeFlags.Primitive | TypeFlags.Never))
        {
            // primitive types cannot be considered "thenable" since they are not objects.
            return false;
        }
        var thenFunction = GetTypeOfPropertyOfTypeWorker(t, "then");
        return thenFunction != null && (GetSignaturesOfTypeWorker(GetTypeWithFacts(thenFunction, TypeFacts.NEUndefinedOrNull), SignatureKind.Call)?.Length ?? 0) != 0;
    }

    internal Type? GetAwaitedTypeOfPromise(Type t)
    {
        return GetAwaitedTypeOfPromiseEx(t, null, null);
    }

    internal Type? GetAwaitedTypeOfPromiseEx(Type t, Node? errorNode, Message? diagnosticMessage, params object?[]? args)
    {
        var promisedType = GetPromisedTypeOfPromiseEx(t, errorNode, out _);
        if (promisedType != null)
        {
            return GetAwaitedTypeEx(promisedType, errorNode, diagnosticMessage, args);
        }
        return null;
    }

    // Check if a parameter or catch variable (or their bindings elements) is assigned anywhere
    internal bool IsSomeSymbolAssigned(Node rootDeclaration)
    {
        return IsSomeSymbolAssignedWorker(rootDeclaration.Name()!);
    }

    internal bool IsSomeSymbolAssignedWorker(Node node)
    {
        if (node.Kind == Kind.Identifier)
        {
            return IsSymbolAssigned(GetSymbolOfDeclaration(node.Parent!)!);
        }
        return Some(node.Elements(), e => e.Name() != null && IsSomeSymbolAssignedWorker(e.Name()!));
    }

    internal Type GetTargetType(Type t)
    {
        if ((t._objectFlags & ObjectFlags.Reference) != 0)
        {
            return t.AsTypeReference()!._target!;
        }
        return t;
    }

    internal Type GetNarrowableTypeForReference(Type t, Node reference, CheckMode checkMode)
    {
        if (IsNoInferType(t))
        {
            t = t.AsSubstitutionType()!._baseType!;
        }
        // When the type of a reference is or contains an instantiable type with a union type constraint, and
        // when the reference is in a constraint position (where it is known we'll obtain the apparent type) or
        // has a contextual type containing no top-level instantiables (meaning constraints will determine
        // assignability), we substitute constraints for all instantiables in the type of the reference to give
        // control flow analysis an opportunity to narrow it further. For example, for a reference of a type
        // parameter type 'T extends string | undefined' with a contextual type 'string', we substitute
        // 'string | undefined' to give control flow analysis the opportunity to narrow to type 'string'.
        var substituteConstraints = (checkMode & CheckMode.Inferential) == 0 && SomeType(t, IsGenericTypeWithUnionConstraint) && (IsConstraintPosition(t, reference) || HasContextualTypeWithNoGenericTypes(reference, checkMode));
        if (substituteConstraints)
        {
            return MapType(t, GetBaseConstraintOrType);
        }
        return t;
    }

    internal bool IsConstraintPosition(Type t, Node node)
    {
        var parent = node.Parent;
        // In an element access obj[x], we consider obj to be in a constraint position, except when obj is of
        // a generic type without a nullable constraint and x is a generic type. This is because when both obj
        // and x are of generic types T and K, we want the resulting type to be T[K].
        return IsPropertyAccessExpression(parent!) || IsQualifiedName(parent!) ||
            (IsCallExpression(parent!) || IsNewExpression(parent!)) && parent!.Expression() == node ||
            IsElementAccessExpression(parent!) && parent!.Expression() == node && !(SomeType(t, IsGenericTypeWithoutNullableConstraint) && IsGenericIndexType(GetTypeOfExpression(parent!.AsElementAccessExpression()!.ArgumentExpression!)));
    }

    internal bool IsGenericTypeWithUnionConstraint(Type t)
    {
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return Some(t.AsIntersectionType()!._types, IsGenericTypeWithUnionConstraint);
        }
        return (t._flags & TypeFlags.Instantiable) != 0 && (GetBaseConstraintOrType(t)._flags & (TypeFlags.Nullable | TypeFlags.Union)) != 0;
    }

    internal bool IsGenericTypeWithoutNullableConstraint(Type t)
    {
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return Some(t.AsIntersectionType()!._types, IsGenericTypeWithoutNullableConstraint);
        }
        return (t._flags & TypeFlags.Instantiable) != 0 && !MaybeTypeOfKind(GetBaseConstraintOrType(t), TypeFlags.Nullable);
    }

    internal bool HasContextualTypeWithNoGenericTypes(Node node, CheckMode checkMode)
    {
        // Computing the contextual type for a child of a JSX element involves resolving the type of the
        // element's tag name, so we exclude that here to avoid circularities.
        // If check mode has `CheckMode.RestBindingElement`, we skip binding pattern contextual types,
        // as we want the type of a rest element to be generic when possible.
        if ((IsIdentifier(node) || IsPropertyAccessExpression(node) || IsElementAccessExpression(node)) &&
            !((IsJsxOpeningElement(node.Parent!) || IsJsxSelfClosingElement(node.Parent!)) && node.Parent!.TagName() == node))
        {
            var contextualType = GetContextualTypeWorker(node, IfElse((checkMode & CheckMode.RestBindingElement) != 0, ContextFlags.SkipBindingPatterns, ContextFlags.None));
            if (contextualType != null)
            {
                return !IsGenericType(contextualType);
            }
        }
        return false;
    }

    internal Type GetNonUndefinedType(Type t)
    {
        var typeOrConstraint = t;
        if (SomeType(t, IsGenericTypeWithUndefinedConstraint))
        {
            typeOrConstraint = MapType(t, x =>
            {
                if ((x._flags & TypeFlags.Instantiable) != 0)
                {
                    return GetBaseConstraintOrType(x);
                }
                return x;
            });
        }
        return GetTypeWithFacts(typeOrConstraint, TypeFacts.NEUndefined);
    }

    internal bool IsGenericTypeWithUndefinedConstraint(Type t)
    {
        if ((t._flags & TypeFlags.Instantiable) != 0)
        {
            var constraint = GetBaseConstraintOfTypeWorker(t);
            if (constraint != null)
            {
                return MaybeTypeOfKind(constraint, TypeFlags.Undefined);
            }
        }
        return false;
    }

    internal Type GetActualTypeVariable(Type t)
    {
        if ((t._flags & TypeFlags.Substitution) != 0)
        {
            return GetActualTypeVariable(t.AsSubstitutionType()!._baseType!);
        }
        if ((t._flags & TypeFlags.IndexedAccess) != 0)
        {
            var objectType = GetActualTypeVariable(t.AsIndexedAccessType()!._objectType!);
            var indexType = GetActualTypeVariable(t.AsIndexedAccessType()!._indexType!);
            if (objectType != t.AsIndexedAccessType()!._objectType || indexType != t.AsIndexedAccessType()!._indexType)
            {
                return GetIndexedAccessType(objectType, indexType);
            }
        }
        return GetNonDistributedTypeParameter(t);
    }
}

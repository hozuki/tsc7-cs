// ported from: internal/checker/flow.go:377-1252（flow 类型收窄：narrowType 分派与 narrowTypeBy* 家族
//   —— optionality/truthiness/call/binary/equality/typeof/literal/typeName/typeFacts/discriminant/constructor/
//   booleanComparison/instanceof + getNarrowedType(Worker)/getInstanceType/privateIdentifier-in/
//   in-keyword/isTypePresencePossible/optionalChainContainment + switch 收窄家族）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 字段 `_camelCase`；Go 的 `(T, bool)` → `bool TryX(..., out T)`（docs/checker-plan.md §5.4）。
// 2) Go 的 `switch { case cond: }` → `if`/`else if` 链；Go 的 `fallthrough` → `goto case`（C# 无 fallthrough）。
// 3) Go 的 lambda 参数与外围局部同名（如 `t`）在 C# 里不允许 shadow（CS0136）→ 改名为 `tt`（语义不变）。
// 4) Go 的 `core.IfElse(cond, a, b)` → C# 三元表达式（避免 `T` 在可空引用类型上推断成 `T?`）。
// 5) Go 的 `[]T` 子切片（`s[i:j]`）→ C# 数组范围切片（复制；只读使用，语义等价）；`append` → `Concatenate`。
// 6) 实例方法作为函数值传给 `Core.Map/Some/Every/MapType/FilterType` 时可直接传方法组；命中
//    artifacts/checker-method-collisions.md 改名的用改后名字。
// 7) `nil` 映射：Go 里"必然非 nil"的节点数据（`data.SwitchStatement`、`flow.Node`、`expr.OperatorToken`）用 `!`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    // Narrow the given type based on the given expression having the assumed boolean value. The returned type
    // will be a subtype or the same type as the argument.
    internal Type NarrowType(FlowState f, Type t, Node expr, bool assumeTrue)
    {
        // for `a?.b`, we emulate a synthetic `a !== null && a !== undefined` condition for `a`
        if (IsExpressionOfOptionalChainRoot(expr) || IsBinaryExpression(expr.Parent!) && (expr.Parent!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.QuestionQuestionToken || expr.Parent!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.QuestionQuestionEqualsToken) && expr.Parent!.AsBinaryExpression()!.Left == expr)
        {
            return NarrowTypeByOptionality(f, t, expr, assumeTrue);
        }
        switch (expr.Kind)
        {
            case Kind.Identifier:
                // When narrowing a reference to a const variable, non-assigned parameter, or readonly property, we inline
                // up to five levels of aliased conditional expressions that are themselves declared as const variables.
                if (!IsMatchingReference(f._reference!, expr) && _inlineLevel < 5)
                {
                    var symbol = GetResolvedSymbolWorker(expr);
                    if (IsConstantVariable(symbol))
                    {
                        var declaration = symbol.ValueDeclaration;
                        if (declaration != null && IsVariableDeclaration(declaration) && declaration.Type() == null && declaration.Initializer() != null && IsConstantReference(f._reference!))
                        {
                            _inlineLevel++;
                            var result = NarrowType(f, t, declaration.Initializer()!, assumeTrue);
                            _inlineLevel--;
                            return result;
                        }
                    }
                }
                goto case Kind.ThisKeyword;
            case Kind.ThisKeyword:
            case Kind.SuperKeyword:
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                return NarrowTypeByTruthiness(f, t, expr, assumeTrue);
            case Kind.CallExpression:
                return NarrowTypeByCallExpression(f, t, expr, assumeTrue);
            case Kind.ParenthesizedExpression:
            case Kind.NonNullExpression:
            case Kind.SatisfiesExpression:
                return NarrowType(f, t, expr.Expression()!, assumeTrue);
            case Kind.BinaryExpression:
                return NarrowTypeByBinaryExpression(f, t, expr.AsBinaryExpression()!, assumeTrue);
            case Kind.PrefixUnaryExpression:
                if (expr.AsPrefixUnaryExpression()!.Operator == Kind.ExclamationToken)
                {
                    return NarrowType(f, t, expr.AsPrefixUnaryExpression()!.Operand!, !assumeTrue);
                }
                break;
        }
        return t;
    }

    internal Type NarrowTypeByOptionality(FlowState f, Type t, Node expr, bool assumePresent)
    {
        if (IsMatchingReference(f._reference!, expr))
        {
            return GetAdjustedTypeWithFacts(t, assumePresent ? TypeFacts.NEUndefinedOrNull : TypeFacts.EQUndefinedOrNull);
        }
        var access = GetDiscriminantPropertyAccess(f, expr, t);
        if (access != null)
        {
            return NarrowTypeByDiscriminant(t, access, tt => GetTypeWithFacts(tt, assumePresent ? TypeFacts.NEUndefinedOrNull : TypeFacts.EQUndefinedOrNull));
        }
        return t;
    }

    internal Type NarrowTypeByTruthiness(FlowState f, Type t, Node expr, bool assumeTrue)
    {
        if (IsMatchingReference(f._reference!, expr))
        {
            return GetAdjustedTypeWithFacts(t, assumeTrue ? TypeFacts.Truthy : TypeFacts.Falsy);
        }
        if (_strictNullChecks && assumeTrue && OptionalChainContainsReference(expr, f._reference!))
        {
            t = GetAdjustedTypeWithFacts(t, TypeFacts.NEUndefinedOrNull);
        }
        var access = GetDiscriminantPropertyAccess(f, expr, t);
        if (access != null)
        {
            return NarrowTypeByDiscriminant(t, access, tt => GetTypeWithFacts(tt, assumeTrue ? TypeFacts.Truthy : TypeFacts.Falsy));
        }
        return t;
    }

    internal Type NarrowTypeByCallExpression(FlowState f, Type t, Node callExpression, bool assumeTrue)
    {
        if (HasMatchingArgument(callExpression, f._reference!))
        {
            TypePredicate? predicate = null;
            if (assumeTrue || !IsCallChain(callExpression))
            {
                var signature = GetEffectsSignature(callExpression);
                if (signature != null)
                {
                    predicate = GetTypePredicateOfSignatureWorker(signature);
                }
            }
            if (predicate != null && (predicate._kind == TypePredicateKind.This || predicate._kind == TypePredicateKind.Identifier))
            {
                return NarrowTypeByTypePredicate(f, t, predicate, callExpression, assumeTrue);
            }
        }
        if (_containsMissingType(t) && IsAccessExpression(f._reference) && IsPropertyAccessExpression(callExpression.Expression()!))
        {
            var callAccess = callExpression.Expression()!;
            if (IsMatchingReference(f._reference!.Expression()!, GetReferenceCandidate(callAccess.Expression()!)) && IsIdentifier(callAccess.Name()!) && callAccess.Name()!.Text() == "hasOwnProperty" && (callExpression.Arguments()?.Length ?? 0) == 1)
            {
                var argument = callExpression.Arguments()![0];
                if (TryGetAccessedPropertyName(f._reference!, out var accessedName) && IsStringLiteralLike(argument) && accessedName == argument.Text())
                {
                    return GetTypeWithFacts(t, assumeTrue ? TypeFacts.NEUndefined : TypeFacts.EQUndefined);
                }
            }
        }
        return t;
    }

    internal Type NarrowTypeByBinaryExpression(FlowState f, Type t, BinaryExpression expr, bool assumeTrue)
    {
        switch (expr.OperatorToken!.Kind)
        {
            case Kind.EqualsToken:
            case Kind.BarBarEqualsToken:
            case Kind.AmpersandAmpersandEqualsToken:
            case Kind.QuestionQuestionEqualsToken:
                return NarrowTypeByTruthiness(f, NarrowType(f, t, expr.Right!, assumeTrue), expr.Left!, assumeTrue);
            case Kind.EqualsEqualsToken:
            case Kind.ExclamationEqualsToken:
            case Kind.EqualsEqualsEqualsToken:
            case Kind.ExclamationEqualsEqualsToken:
                var @operator = expr.OperatorToken!.Kind;
                var left = GetReferenceCandidate(expr.Left!);
                var right = GetReferenceCandidate(expr.Right!);
                if (left.Kind == Kind.TypeOfExpression && IsStringLiteralLike(right))
                {
                    return NarrowTypeByTypeof(f, t, left.AsTypeOfExpression()!, @operator, right, assumeTrue);
                }
                if (right.Kind == Kind.TypeOfExpression && IsStringLiteralLike(left))
                {
                    return NarrowTypeByTypeof(f, t, right.AsTypeOfExpression()!, @operator, left, assumeTrue);
                }
                if (IsMatchingReference(f._reference!, left))
                {
                    return NarrowTypeByEquality(t, @operator, right, assumeTrue);
                }
                if (IsMatchingReference(f._reference!, right))
                {
                    return NarrowTypeByEquality(t, @operator, left, assumeTrue);
                }
                if (_strictNullChecks)
                {
                    if (OptionalChainContainsReference(left, f._reference!))
                    {
                        t = NarrowTypeByOptionalChainContainment(f, t, @operator, right, assumeTrue);
                    }
                    else if (OptionalChainContainsReference(right, f._reference!))
                    {
                        t = NarrowTypeByOptionalChainContainment(f, t, @operator, left, assumeTrue);
                    }
                }
                var leftAccess = GetDiscriminantPropertyAccess(f, left, t);
                if (leftAccess != null)
                {
                    return NarrowTypeByDiscriminantProperty(t, leftAccess, @operator, right, assumeTrue);
                }
                var rightAccess = GetDiscriminantPropertyAccess(f, right, t);
                if (rightAccess != null)
                {
                    return NarrowTypeByDiscriminantProperty(t, rightAccess, @operator, left, assumeTrue);
                }
                if (IsMatchingConstructorReference(f, left))
                {
                    return NarrowTypeByConstructor(t, @operator, right, assumeTrue);
                }
                if (IsMatchingConstructorReference(f, right))
                {
                    return NarrowTypeByConstructor(t, @operator, left, assumeTrue);
                }
                if (IsBooleanLiteral(right) && !IsAccessExpression(left))
                {
                    return NarrowTypeByBooleanComparison(f, t, left, right, @operator, assumeTrue);
                }
                if (IsBooleanLiteral(left) && !IsAccessExpression(right))
                {
                    return NarrowTypeByBooleanComparison(f, t, right, left, @operator, assumeTrue);
                }
                break;
            case Kind.InstanceOfKeyword:
                return NarrowTypeByInstanceof(f, t, expr, assumeTrue);
            case Kind.InKeyword:
                if (IsPrivateIdentifier(expr.Left!))
                {
                    return NarrowTypeByPrivateIdentifierInInExpression(f, t, expr, assumeTrue);
                }
                var target = GetReferenceCandidate(expr.Right!);
                if (_containsMissingType(t) && IsAccessExpression(f._reference) && IsMatchingReference(f._reference!.Expression()!, target))
                {
                    var leftType = GetTypeOfExpression(expr.Left!);
                    if (IsTypeUsableAsPropertyName(leftType))
                    {
                        if (TryGetAccessedPropertyName(f._reference!, out var accessedName) && accessedName == GetPropertyNameFromType(leftType))
                        {
                            return GetTypeWithFacts(t, assumeTrue ? TypeFacts.NEUndefined : TypeFacts.EQUndefined);
                        }
                    }
                }
                if (IsMatchingReference(f._reference!, target))
                {
                    var leftType = GetTypeOfExpression(expr.Left!);
                    if (IsTypeUsableAsPropertyName(leftType))
                    {
                        return NarrowTypeByInKeyword(f, t, leftType, assumeTrue);
                    }
                }
                break;
            case Kind.CommaToken:
                return NarrowType(f, t, expr.Right!, assumeTrue);
            case Kind.AmpersandAmpersandToken:
                // Ordinarily we won't see && and || expressions in control flow analysis because the Binder breaks those
                // expressions down to individual conditional control flows. However, we may encounter them when analyzing
                // aliased conditional expressions.
                if (assumeTrue)
                {
                    return NarrowType(f, NarrowType(f, t, expr.Left!, true /*assumeTrue*/), expr.Right!, true /*assumeTrue*/);
                }
                return GetUnionTypeWorker(new[] { NarrowType(f, t, expr.Left!, false /*assumeTrue*/), NarrowType(f, t, expr.Right!, false /*assumeTrue*/) });
            case Kind.BarBarToken:
                if (assumeTrue)
                {
                    return GetUnionTypeWorker(new[] { NarrowType(f, t, expr.Left!, true /*assumeTrue*/), NarrowType(f, t, expr.Right!, true /*assumeTrue*/) });
                }
                return NarrowType(f, NarrowType(f, t, expr.Left!, false /*assumeTrue*/), expr.Right!, false /*assumeTrue*/);
        }
        return t;
    }

    internal Type NarrowTypeByEquality(Type t, Kind @operator, Node value, bool assumeTrue)
    {
        if ((t._flags & TypeFlags.Any) != 0)
        {
            return t;
        }
        if (@operator == Kind.ExclamationEqualsToken || @operator == Kind.ExclamationEqualsEqualsToken)
        {
            assumeTrue = !assumeTrue;
        }
        var valueType = GetTypeOfExpression(value);
        var doubleEquals = @operator == Kind.EqualsEqualsToken || @operator == Kind.ExclamationEqualsToken;
        if ((valueType._flags & TypeFlags.Nullable) != 0)
        {
            if (!_strictNullChecks)
            {
                return t;
            }
            TypeFacts facts;
            if (doubleEquals)
            {
                facts = assumeTrue ? TypeFacts.EQUndefinedOrNull : TypeFacts.NEUndefinedOrNull;
            }
            else if ((valueType._flags & TypeFlags.Null) != 0)
            {
                facts = assumeTrue ? TypeFacts.EQNull : TypeFacts.NENull;
            }
            else
            {
                facts = assumeTrue ? TypeFacts.EQUndefined : TypeFacts.NEUndefined;
            }
            return GetAdjustedTypeWithFacts(t, facts);
        }
        if (assumeTrue)
        {
            if (!doubleEquals && ((t._flags & TypeFlags.Unknown) != 0 || SomeType(t, IsEmptyAnonymousObjectType)))
            {
                if ((valueType._flags & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0 || IsEmptyAnonymousObjectType(valueType))
                {
                    return valueType;
                }
                if ((valueType._flags & TypeFlags.Object) != 0)
                {
                    return _nonPrimitiveType;
                }
            }
            if (!doubleEquals && (valueType._flags & TypeFlags.Primitive) != 0 && IsUniformUnionType(t))
            {
                var regularType = GetRegularTypeOfLiteralType(valueType);
                if (UnionContainsType(t, regularType, false /*matchSymbol*/))
                {
                    return regularType;
                }
            }
            var filteredType = FilterType(t, tt => AreTypesComparable(tt, valueType) || doubleEquals && IsCoercibleUnderDoubleEquals(tt, valueType));
            return ReplacePrimitivesWithLiterals(filteredType, valueType);
        }
        if (IsUnitType(valueType))
        {
            if (IsUniformUnionType(t))
            {
                var filteredType = RemoveType(t, GetRegularTypeOfLiteralType(valueType));
                if (filteredType != t)
                {
                    return filteredType;
                }
            }
            return FilterType(t, tt => !(IsUnitLikeType(tt) && AreTypesComparable(tt, valueType)));
        }
        return t;
    }

    internal Type NarrowTypeByTypeof(FlowState f, Type t, TypeOfExpression typeOfExpr, Kind @operator, Node literal, bool assumeTrue)
    {
        // We have '==', '!=', '===', or !==' operator with 'typeof xxx' and string literal operands
        if (@operator == Kind.ExclamationEqualsToken || @operator == Kind.ExclamationEqualsEqualsToken)
        {
            assumeTrue = !assumeTrue;
        }
        var target = GetReferenceCandidate(typeOfExpr.Expression!);
        if (!IsMatchingReference(f._reference!, target))
        {
            if (_strictNullChecks && OptionalChainContainsReference(target, f._reference!) && assumeTrue == (literal.Text() != "undefined"))
            {
                t = GetAdjustedTypeWithFacts(t, TypeFacts.NEUndefinedOrNull);
            }
            var propertyAccess = GetDiscriminantPropertyAccess(f, target, t);
            if (propertyAccess != null)
            {
                return NarrowTypeByDiscriminant(t, propertyAccess, tt => NarrowTypeByLiteralExpression(tt, literal, assumeTrue));
            }
            return t;
        }
        return NarrowTypeByLiteralExpression(t, literal, assumeTrue);
    }

    internal static readonly Dictionary<string, TypeFacts> _typeofNEFacts = new()
    {
        ["string"] = TypeFacts.TypeofNEString,
        ["number"] = TypeFacts.TypeofNENumber,
        ["bigint"] = TypeFacts.TypeofNEBigInt,
        ["boolean"] = TypeFacts.TypeofNEBoolean,
        ["symbol"] = TypeFacts.TypeofNESymbol,
        ["undefined"] = TypeFacts.NEUndefined,
        ["object"] = TypeFacts.TypeofNEObject,
        ["function"] = TypeFacts.TypeofNEFunction,
    };

    internal Type NarrowTypeByLiteralExpression(Type t, Node literal, bool assumeTrue)
    {
        if (assumeTrue)
        {
            return NarrowTypeByTypeName(t, literal.Text());
        }
        if (!_typeofNEFacts.TryGetValue(literal.Text(), out var facts))
        {
            facts = TypeFacts.TypeofNEHostObject;
        }
        return GetAdjustedTypeWithFacts(t, facts);
    }

    internal Type NarrowTypeByTypeName(Type t, string typeName)
    {
        switch (typeName)
        {
            case "string":
                return NarrowTypeByTypeFacts(t, _stringType, TypeFacts.TypeofEQString);
            case "number":
                return NarrowTypeByTypeFacts(t, _numberType, TypeFacts.TypeofEQNumber);
            case "bigint":
                return NarrowTypeByTypeFacts(t, _bigintType, TypeFacts.TypeofEQBigInt);
            case "boolean":
                return NarrowTypeByTypeFacts(t, _booleanType, TypeFacts.TypeofEQBoolean);
            case "symbol":
                return NarrowTypeByTypeFacts(t, _esSymbolType, TypeFacts.TypeofEQSymbol);
            case "object":
                if ((t._flags & TypeFlags.Any) != 0)
                {
                    return t;
                }
                return GetUnionTypeWorker(new[] { NarrowTypeByTypeFacts(t, _nonPrimitiveType, TypeFacts.TypeofEQObject), NarrowTypeByTypeFacts(t, _nullType, TypeFacts.EQNull) });
            case "function":
                if ((t._flags & TypeFlags.Any) != 0)
                {
                    return t;
                }
                return NarrowTypeByTypeFacts(t, _globalFunctionType, TypeFacts.TypeofEQFunction);
            case "undefined":
                return NarrowTypeByTypeFacts(t, _undefinedType, TypeFacts.EQUndefined);
        }
        return NarrowTypeByTypeFacts(t, _nonPrimitiveType, TypeFacts.TypeofEQHostObject);
    }

    internal Type NarrowTypeByTypeFacts(Type t, Type impliedType, TypeFacts facts)
    {
        return MapType(t, tt =>
        {
            if (IsTypeRelatedTo(tt, impliedType, _strictSubtypeRelation))
            {
                return HasTypeFacts(tt, facts) ? tt : _neverType;
            }
            if (IsTypeSubtypeOf(impliedType, tt))
            {
                return impliedType;
            }
            if (HasTypeFacts(tt, facts))
            {
                return GetIntersectionType(new[] { tt, impliedType });
            }
            return _neverType;
        });
    }

    internal Type NarrowTypeByDiscriminantProperty(Type t, Node access, Kind @operator, Node value, bool assumeTrue)
    {
        if ((@operator == Kind.EqualsEqualsEqualsToken || @operator == Kind.ExclamationEqualsEqualsToken) && (t._flags & TypeFlags.Union) != 0)
        {
            var keyPropertyName = GetKeyPropertyName(t);
            if (keyPropertyName != "")
            {
                if (TryGetAccessedPropertyName(access, out var accessedName) && keyPropertyName == accessedName)
                {
                    var candidate = GetConstituentTypeForKeyType(t, GetTypeOfExpression(value));
                    if (candidate != null)
                    {
                        if (assumeTrue && @operator == Kind.EqualsEqualsEqualsToken || !assumeTrue && @operator == Kind.ExclamationEqualsEqualsToken)
                        {
                            return candidate;
                        }
                        var propType = GetTypeOfPropertyOfTypeWorker(candidate, keyPropertyName);
                        if (propType != null && IsUnitType(propType))
                        {
                            return RemoveType(t, candidate);
                        }
                        return t;
                    }
                }
            }
        }
        return NarrowTypeByDiscriminant(t, access, tt => NarrowTypeByEquality(tt, @operator, value, assumeTrue));
    }

    internal Type NarrowTypeByDiscriminant(Type t, Node access, Func<Type, Type> narrowType)
    {
        if (!TryGetAccessedPropertyName(access, out var propName))
        {
            return t;
        }
        var optionalChain = IsOptionalChain(access);
        var removeNullable = _strictNullChecks && (optionalChain || IsNonNullAccess(access)) && MaybeTypeOfKind(t, TypeFlags.Nullable);
        var nonNullType = t;
        if (removeNullable)
        {
            nonNullType = GetTypeWithFacts(t, TypeFacts.NEUndefinedOrNull);
        }
        var propType = GetTypeOfPropertyOfTypeWorker(nonNullType, propName);
        if (propType == null)
        {
            return t;
        }
        if (removeNullable && optionalChain)
        {
            propType = GetOptionalType(propType, false);
        }
        var narrowedPropType = narrowType(propType);
        return FilterType(t, tt =>
        {
            var discriminantType = OrElse(GetTypeOfPropertyOrIndexSignatureOfType(tt, propName), _unknownType)!;
            return (discriminantType._flags & TypeFlags.Never) == 0 && (narrowedPropType._flags & TypeFlags.Never) == 0 && AreTypesComparable(narrowedPropType, discriminantType);
        });
    }

    internal bool IsMatchingConstructorReference(FlowState f, Node expr)
    {
        Node? name = null;
        if (IsPropertyAccessExpression(expr))
        {
            name = expr.AsPropertyAccessExpression()!.Name();
        }
        else if (IsElementAccessExpression(expr) && IsStringLiteralLike(expr.AsElementAccessExpression()!.ArgumentExpression))
        {
            name = expr.AsElementAccessExpression()!.ArgumentExpression;
        }
        return name != null && name.Text() == "constructor" && IsMatchingReference(f._reference!, expr.Expression()!);
    }

    internal Type NarrowTypeByConstructor(Type t, Kind @operator, Node identifier, bool assumeTrue)
    {
        // Do not narrow when checking inequality.
        if (assumeTrue && @operator != Kind.EqualsEqualsToken && @operator != Kind.EqualsEqualsEqualsToken || !assumeTrue && @operator != Kind.ExclamationEqualsToken && @operator != Kind.ExclamationEqualsEqualsToken)
        {
            return t;
        }
        // Get the type of the constructor identifier expression, if it is not a function then do not narrow.
        var identifierType = GetTypeOfExpression(identifier);
        if (!IsFunctionType(identifierType) && !IsConstructorType(identifierType))
        {
            return t;
        }
        // Get the prototype property of the type identifier so we can find out its type.
        var prototypeProperty = GetPropertyOfTypeWorker(identifierType, "prototype");
        if (prototypeProperty == null)
        {
            return t;
        }
        // Get the type of the prototype, if it is undefined, or the global `Object` or `Function` types then do not narrow.
        var prototypeType = GetTypeOfSymbolWorker(prototypeProperty);
        Type? candidate = null;
        if (!IsTypeAny(prototypeType))
        {
            candidate = prototypeType;
        }
        if (candidate == null || candidate == _globalObjectType || candidate == _globalFunctionType)
        {
            return t;
        }
        // If the type that is being narrowed is `any` then just return the `candidate` type since every type is a subtype of `any`.
        if (IsTypeAny(t))
        {
            return candidate;
        }
        // Filter out types that are not considered to be "constructed by" the `candidate` type.
        return FilterType(t, tt => IsConstructedBy(tt, candidate));
    }

    internal bool IsConstructedBy(Type source, Type target)
    {
        // If either the source or target type are a class type then we need to check that they are the same exact type.
        // This is because you may have a class `A` that defines some set of properties, and another class `B`
        // that defines the same set of properties as class `A`, in that case they are structurally the same
        // type, but when you do something like `instanceOfA.constructor === B` it will return false.
        if ((source._flags & TypeFlags.Object) != 0 && (source._objectFlags & ObjectFlags.Class) != 0 || (target._flags & TypeFlags.Object) != 0 && (target._objectFlags & ObjectFlags.Class) != 0)
        {
            return source._symbol == target._symbol;
        }
        // For all other types just check that the `source` type is a subtype of the `target` type.
        return IsTypeSubtypeOf(source, target);
    }

    internal Type NarrowTypeByBooleanComparison(FlowState f, Type t, Node expr, Node boolValue, Kind @operator, bool assumeTrue)
    {
        assumeTrue = (assumeTrue != (boolValue.Kind == Kind.TrueKeyword)) != (@operator != Kind.ExclamationEqualsEqualsToken && @operator != Kind.ExclamationEqualsToken);
        return NarrowType(f, t, expr, assumeTrue);
    }

    internal Type NarrowTypeByInstanceof(FlowState f, Type t, BinaryExpression expr, bool assumeTrue)
    {
        var left = GetReferenceCandidate(expr.Left!);
        if (!IsMatchingReference(f._reference!, left))
        {
            if (assumeTrue && _strictNullChecks && OptionalChainContainsReference(left, f._reference!))
            {
                return GetAdjustedTypeWithFacts(t, TypeFacts.NEUndefinedOrNull);
            }
            return t;
        }
        var right = expr.Right!;
        var rightType = GetTypeOfExpression(right);
        if (!IsTypeDerivedFrom(rightType, _globalObjectType))
        {
            return t;
        }
        // if the right-hand side has an object type with a custom `[Symbol.hasInstance]` method, and that method
        // has a type predicate, use the type predicate to perform narrowing. This allows normal `object` types to
        // participate in `instanceof`, as per Step 2 of https://tc39.es/ecma262/#sec-instanceofoperator.
        TypePredicate? predicate = null;
        var signature = GetEffectsSignature(expr.AsNode());
        if (signature != null)
        {
            predicate = GetTypePredicateOfSignatureWorker(signature);
        }
        if (predicate != null && predicate._kind == TypePredicateKind.Identifier && predicate._parameterIndex == 0)
        {
            return GetNarrowedType(t, predicate._t!, assumeTrue, true /*checkDerived*/);
        }
        if (!IsTypeDerivedFrom(rightType, _globalFunctionType))
        {
            return t;
        }
        var instanceType = MapType(rightType, GetInstanceType);
        // Don't narrow from `any` if the target type is exactly `Object` or `Function`, and narrow
        // in the false branch only if the target is a non-empty object type.
        if (IsTypeAny(t) && (instanceType == _globalObjectType || instanceType == _globalFunctionType) || !assumeTrue && !((instanceType._flags & TypeFlags.Object) != 0 && !IsEmptyAnonymousObjectType(instanceType)))
        {
            return t;
        }
        return GetNarrowedType(t, instanceType, assumeTrue, true /*checkDerived*/);
    }

    internal Type GetNarrowedType(Type t, Type candidate, bool assumeTrue, bool checkDerived)
    {
        if ((t._flags & TypeFlags.Union) == 0)
        {
            return GetNarrowedTypeWorker(t, candidate, assumeTrue, checkDerived);
        }
        var key = new NarrowedTypeKey { _t = t, _candidate = candidate, _assumeTrue = assumeTrue, _checkDerived = checkDerived };
        if (_narrowedTypes.TryGetValue(key, out var narrowedType))
        {
            return narrowedType;
        }
        var narrowedTypeWorker = GetNarrowedTypeWorker(t, candidate, assumeTrue, checkDerived);
        _narrowedTypes[key] = narrowedTypeWorker;
        return narrowedTypeWorker;
    }

    internal Type GetNarrowedTypeWorker(Type t, Type candidate, bool assumeTrue, bool checkDerived)
    {
        if (!assumeTrue)
        {
            if (t == candidate)
            {
                return _neverType;
            }
            if (checkDerived)
            {
                return FilterType(t, tt => !IsTypeDerivedFrom(tt, candidate));
            }
            if ((t._flags & TypeFlags.Unknown) != 0)
            {
                t = _unknownUnionType;
            }
            var trueType = GetNarrowedType(t, candidate, true /*assumeTrue*/, false /*checkDerived*/);
            return RecombineUnknownType(FilterType(t, tt => !IsTypeSubsetOf(tt, trueType)));
        }
        if ((t._flags & TypeFlags.AnyOrUnknown) != 0)
        {
            return candidate;
        }
        if (t == candidate)
        {
            return candidate;
        }
        // We first attempt to filter the current type, narrowing constituents as appropriate and removing
        // constituents that are unrelated to the candidate.
        var keyPropertyName = "";
        if ((t._flags & TypeFlags.Union) != 0)
        {
            keyPropertyName = GetKeyPropertyName(t);
        }
        var narrowedType = MapType(candidate, n =>
        {
            // If a discriminant property is available, use that to reduce the type.
            var matching = t;
            if (keyPropertyName != "")
            {
                var discriminant = GetTypeOfPropertyOfTypeWorker(n, keyPropertyName);
                if (discriminant != null)
                {
                    var constituent = GetConstituentTypeForKeyType(t, discriminant);
                    if (constituent != null)
                    {
                        matching = constituent;
                    }
                }
            }
            // For each constituent t in the current type, if t and c are directly related, pick the most
            // specific of the two. When t and c are related in both directions, we prefer c for type predicates
            // because that is the asserted type, but t for `instanceof` because generics aren't reflected in
            // prototype object types.
            Func<Type, Type> mapType;
            if (checkDerived)
            {
                mapType = tt =>
                {
                    if (IsTypeDerivedFrom(tt, n))
                    {
                        return tt;
                    }
                    if (IsTypeDerivedFrom(n, tt))
                    {
                        return n;
                    }
                    return _neverType;
                };
            }
            else
            {
                mapType = tt =>
                {
                    if (IsTypeStrictSubtypeOf(tt, n))
                    {
                        return tt;
                    }
                    if (IsTypeStrictSubtypeOf(n, tt))
                    {
                        return n;
                    }
                    if (IsTypeSubtypeOf(tt, n))
                    {
                        return tt;
                    }
                    if (IsTypeSubtypeOf(n, tt))
                    {
                        return n;
                    }
                    return _neverType;
                };
            }
            var directlyRelated = MapType(matching, mapType);
            if ((directlyRelated._flags & TypeFlags.Never) == 0)
            {
                return directlyRelated;
            }
            // If no constituents are directly related, create intersections for any generic constituents that
            // are related by constraint.
            Func<Type, Type, bool> isRelated;
            if (checkDerived)
            {
                isRelated = IsTypeDerivedFrom;
            }
            else
            {
                isRelated = IsTypeSubtypeOf;
            }
            return MapType(t, tt =>
            {
                if (MaybeTypeOfKind(tt, TypeFlags.Instantiable))
                {
                    var constraint = GetBaseConstraintOfTypeWorker(tt);
                    if (constraint == null || isRelated(n, constraint))
                    {
                        return GetIntersectionType(new[] { tt, n });
                    }
                }
                return _neverType;
            });
        });
        // If filtering produced a non-empty type, return that. Otherwise, pick the most specific of the two
        // based on assignability, or as a last resort produce an intersection.
        if ((narrowedType._flags & TypeFlags.Never) == 0)
        {
            return narrowedType;
        }
        if (IsTypeSubtypeOf(candidate, t))
        {
            return candidate;
        }
        if (IsTypeAssignableToWorker(t, candidate))
        {
            return t;
        }
        if (IsTypeAssignableToWorker(candidate, t))
        {
            return candidate;
        }
        return GetIntersectionType(new[] { t, candidate });
    }

    internal Type GetInstanceType(Type constructorType)
    {
        var prototypePropertyType = GetTypeOfPropertyOfTypeWorker(constructorType, "prototype");
        if (prototypePropertyType != null && !IsTypeAny(prototypePropertyType))
        {
            return prototypePropertyType;
        }
        var constructSignatures = GetSignaturesOfTypeWorker(constructorType, SignatureKind.Construct);
        if ((constructSignatures?.Length ?? 0) != 0)
        {
            return GetUnionTypeWorker(Map(constructSignatures, s => GetReturnTypeOfSignatureWorker(GetErasedSignature(s))));
        }
        // We use the empty object type to indicate we don't know the type of objects created by
        // this constructor function.
        return _emptyObjectType;
    }

    internal Type NarrowTypeByPrivateIdentifierInInExpression(FlowState f, Type t, BinaryExpression expr, bool assumeTrue)
    {
        var target = GetReferenceCandidate(expr.Right!);
        if (!IsMatchingReference(f._reference!, target))
        {
            return t;
        }
        var symbol = GetSymbolForPrivateIdentifierExpression(expr.Left!);
        if (symbol == null)
        {
            return t;
        }
        var classSymbol = symbol.Parent!;
        Type targetType;
        if (HasStaticModifier(symbol.ValueDeclaration!))
        {
            targetType = GetTypeOfSymbolWorker(classSymbol);
        }
        else
        {
            targetType = GetDeclaredTypeOfSymbolWorker(classSymbol);
        }
        return GetNarrowedType(t, targetType, assumeTrue, true /*checkDerived*/);
    }

    internal Type NarrowTypeByInKeyword(FlowState f, Type t, Type nameType, bool assumeTrue)
    {
        var name = GetPropertyNameFromType(nameType);
        var isKnownProperty = SomeType(t, tt => IsTypePresencePossible(tt, name, true /*assumeTrue*/));
        if (isKnownProperty)
        {
            // If the check is for a known property (i.e. a property declared in some constituent of
            // the target type), we filter the target type by presence of absence of the property.
            return FilterType(t, tt => IsTypePresencePossible(tt, name, assumeTrue));
        }
        if (assumeTrue)
        {
            // If the check is for an unknown property, we intersect the target type with `Record<X, unknown>`,
            // where X is the name of the property.
            var recordSymbol = _getGlobalRecordSymbol();
            if (recordSymbol != null)
            {
                return GetIntersectionType(new[] { t, GetTypeAliasInstantiation(recordSymbol, new[] { nameType, _unknownType }, null) });
            }
        }
        return t;
    }

    internal bool IsTypePresencePossible(Type t, string propName, bool assumeTrue)
    {
        var prop = GetPropertyOfTypeWorker(t, propName);
        if (prop != null)
        {
            return (prop.Flags & SymbolFlags.Optional) != 0 || (prop.CheckFlags & CheckFlags.Partial) != 0 || assumeTrue;
        }
        return GetApplicableIndexInfoForName(t, propName) != null || !assumeTrue;
    }

    internal Type NarrowTypeByOptionalChainContainment(FlowState f, Type t, Kind @operator, Node value, bool assumeTrue)
    {
        // We are in a branch of obj?.foo === value (or any one of the other equality operators). We narrow obj as follows:
        // When operator is === and type of value excludes undefined, null and undefined is removed from type of obj in true branch.
        // When operator is !== and type of value excludes undefined, null and undefined is removed from type of obj in false branch.
        // When operator is == and type of value excludes null and undefined, null and undefined is removed from type of obj in true branch.
        // When operator is != and type of value excludes null and undefined, null and undefined is removed from type of obj in false branch.
        // When operator is === and type of value is undefined, null and undefined is removed from type of obj in false branch.
        // When operator is !== and type of value is undefined, null and undefined is removed from type of obj in true branch.
        // When operator is == and type of value is null or undefined, null and undefined is removed from type of obj in false branch.
        // When operator is != and type of value is null or undefined, null and undefined is removed from type of obj in true branch.
        var equalsOperator = @operator == Kind.EqualsEqualsToken || @operator == Kind.EqualsEqualsEqualsToken;
        TypeFlags nullableFlags;
        if (@operator == Kind.EqualsEqualsToken || @operator == Kind.ExclamationEqualsToken)
        {
            nullableFlags = TypeFlags.Nullable;
        }
        else
        {
            nullableFlags = TypeFlags.Undefined;
        }
        var valueType = GetTypeOfExpression(value);
        // Note that we include any and unknown in the exclusion test because their domain includes null and undefined.
        var removeNullable = equalsOperator != assumeTrue && EveryType(valueType, tt => (tt._flags & nullableFlags) != 0) ||
            equalsOperator == assumeTrue && EveryType(valueType, tt => (tt._flags & (TypeFlags.AnyOrUnknown | nullableFlags)) == 0);
        if (removeNullable)
        {
            return GetAdjustedTypeWithFacts(t, TypeFacts.NEUndefinedOrNull);
        }
        return t;
    }

    internal FlowType GetTypeAtSwitchClause(FlowState f, FlowNode flow)
    {
        var data = flow.Node!.AsFlowSwitchClauseData();
        var expr = SkipParentheses(data.SwitchStatement!.Expression()!)!;
        var flowType = GetTypeAtFlowNode(f, flow.Antecedent!);
        var t = flowType._t!;
        if (IsMatchingReference(f._reference!, expr))
        {
            t = NarrowTypeBySwitchOnDiscriminant(t, data);
        }
        else if (expr.Kind == Kind.TypeOfExpression && IsMatchingReference(f._reference!, expr.Expression()!))
        {
            t = NarrowTypeBySwitchOnTypeOf(t, data);
        }
        else if (expr.Kind == Kind.TrueKeyword)
        {
            t = NarrowTypeBySwitchOnTrue(f, t, data);
        }
        else
        {
            if (_strictNullChecks)
            {
                if (OptionalChainContainsReference(expr, f._reference!))
                {
                    t = NarrowTypeBySwitchOptionalChainContainment(t, data, tt => (tt._flags & (TypeFlags.Undefined | TypeFlags.Never)) == 0);
                }
                else if (IsTypeOfExpression(expr) && OptionalChainContainsReference(expr.Expression()!, f._reference!))
                {
                    t = NarrowTypeBySwitchOptionalChainContainment(t, data, tt => !((tt._flags & TypeFlags.Never) != 0 || (tt._flags & TypeFlags.StringLiteral) != 0 && GetStringLiteralValue(tt) == "undefined"));
                }
            }
            var access = GetDiscriminantPropertyAccess(f, expr, t);
            if (access != null)
            {
                t = NarrowTypeBySwitchOnDiscriminantProperty(t, access, data);
            }
        }
        return NewFlowType(t, flowType._incomplete);
    }

    internal Type NarrowTypeBySwitchOnDiscriminant(Type t, FlowSwitchClauseData data)
    {
        // We only narrow if all case expressions specify
        // values with unit types, except for the case where
        // `type` is unknown. In this instance we map object
        // types to the nonPrimitive type and narrow with that.
        var switchTypes = GetSwitchClauseTypes(data.SwitchStatement!);
        if (switchTypes.Length == 0)
        {
            return t;
        }
        var clauseTypes = switchTypes[data.ClauseStart..data.ClauseEnd];
        var hasDefaultClause = data.ClauseStart == data.ClauseEnd || Array.IndexOf(clauseTypes, _neverType) >= 0;
        if ((t._flags & TypeFlags.Unknown) != 0 && !hasDefaultClause)
        {
            Type[]? groundClauseTypes = null;
            for (var i = 0; i < clauseTypes.Length; i++)
            {
                var s = clauseTypes[i];
                if ((s._flags & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0)
                {
                    if (groundClauseTypes != null)
                    {
                        groundClauseTypes = Concatenate(groundClauseTypes, new[] { s });
                    }
                }
                else if ((s._flags & TypeFlags.Object) != 0)
                {
                    if (groundClauseTypes == null)
                    {
                        // Go: clauseTypes[:i:i]（cap = i）——后续 append 会另分配，不别名原数组。
                        groundClauseTypes = clauseTypes[..i];
                    }
                    groundClauseTypes = Concatenate(groundClauseTypes, new[] { _nonPrimitiveType });
                }
                else
                {
                    return t;
                }
            }
            return GetUnionTypeWorker(groundClauseTypes == null ? clauseTypes : groundClauseTypes);
        }
        var discriminantType = GetUnionTypeWorker(clauseTypes);
        Type? caseType = null;
        if ((discriminantType._flags & TypeFlags.Never) != 0)
        {
            caseType = _neverType;
        }
        else
        {
            if ((discriminantType._flags & TypeFlags.Primitive) != 0 && IsUniformUnionType(t))
            {
                var regularType = GetRegularTypeOfLiteralType(discriminantType);
                if (UnionContainsType(t, regularType, false /*matchSymbol*/))
                {
                    caseType = regularType;
                }
            }
            if (caseType == null)
            {
                var filtered = FilterType(t, tt => AreTypesComparable(discriminantType, tt));
                caseType = ReplacePrimitivesWithLiterals(filtered, discriminantType);
            }
        }
        if (!hasDefaultClause)
        {
            return caseType;
        }
        var defaultType = FilterType(t, tt =>
        {
            if (!IsUnitLikeType(tt))
            {
                return true;
            }
            var u = _undefinedType;
            if ((tt._flags & TypeFlags.Undefined) == 0)
            {
                u = GetRegularTypeOfLiteralType(ExtractUnitType(tt));
            }
            return !Some(switchTypes, st => IsUnitType(st) && AreTypesComparable(st, u));
        });
        if ((caseType._flags & TypeFlags.Never) != 0)
        {
            return defaultType;
        }
        return GetUnionTypeWorker(new[] { caseType, defaultType });
    }

    internal Type NarrowTypeBySwitchOnTypeOf(Type t, FlowSwitchClauseData data)
    {
        var witnesses = GetSwitchClauseTypeOfWitnesses(data.SwitchStatement!);
        if (witnesses == null)
        {
            return t;
        }
        var clauses = data.SwitchStatement!.AsSwitchStatement()!.CaseBlock!.AsCaseBlock()!.Clauses!.Nodes!;
        // Equal start and end denotes implicit fallthrough; undefined marks explicit default clause.
        var defaultIndex = FindIndex(clauses, clause => clause.Kind == Kind.DefaultClause);
        var clauseStart = data.ClauseStart;
        var clauseEnd = data.ClauseEnd;
        var hasDefaultClause = clauseStart == clauseEnd || (defaultIndex >= clauseStart && defaultIndex < clauseEnd);
        if (hasDefaultClause)
        {
            // In the default clause we filter constituents down to those that are not-equal to all handled cases.
            var notEqualFacts = GetNotEqualFactsFromTypeofSwitch(clauseStart, clauseEnd, witnesses);
            return FilterType(t, tt => GetTypeFacts(tt, notEqualFacts) == notEqualFacts);
        }
        // In the non-default cause we create a union of the type narrowed by each of the listed cases.
        var clauseWitnesses = witnesses[clauseStart..clauseEnd];
        return GetUnionTypeWorker(Map(clauseWitnesses, text => text != "" ? NarrowTypeByTypeName(t, text) : _neverType));
    }

    internal Type NarrowTypeBySwitchOnTrue(FlowState f, Type t, FlowSwitchClauseData data)
    {
        var clauses = data.SwitchStatement!.AsSwitchStatement()!.CaseBlock!.AsCaseBlock()!.Clauses!.Nodes!;
        var defaultIndex = FindIndex(clauses, clause => clause.Kind == Kind.DefaultClause);
        var clauseStart = data.ClauseStart;
        var clauseEnd = data.ClauseEnd;
        var hasDefaultClause = clauseStart == clauseEnd || (defaultIndex >= clauseStart && defaultIndex < clauseEnd);
        // First, narrow away all of the cases that preceded this set of cases.
        for (var i = 0; i < clauseStart; i++)
        {
            var clause = clauses[i];
            if (clause.Kind == Kind.CaseClause)
            {
                t = NarrowType(f, t, clause.Expression()!, false /*assumeTrue*/);
            }
        }
        // If our current set has a default, then none the other cases were hit either.
        // There's no point in narrowing by the other cases in the set, since we can
        // get here through other paths.
        if (hasDefaultClause)
        {
            for (var i = clauseEnd; i < clauses.Length; i++)
            {
                var clause = clauses[i];
                if (clause.Kind == Kind.CaseClause)
                {
                    t = NarrowType(f, t, clause.Expression()!, false /*assumeTrue*/);
                }
            }
            return t;
        }
        // Now, narrow based on the cases in this set.
        return GetUnionTypeWorker(Map(clauses[clauseStart..clauseEnd], clause => clause.Kind == Kind.CaseClause ? NarrowType(f, t, clause.Expression()!, true /*assumeTrue*/) : _neverType));
    }

    internal Type NarrowTypeBySwitchOptionalChainContainment(Type t, FlowSwitchClauseData data, Func<Type, bool> clauseCheck)
    {
        var everyClauseChecks = data.ClauseStart != data.ClauseEnd && Every(GetSwitchClauseTypes(data.SwitchStatement!)[data.ClauseStart..data.ClauseEnd], clauseCheck);
        if (everyClauseChecks)
        {
            return GetTypeWithFacts(t, TypeFacts.NEUndefinedOrNull);
        }
        return t;
    }

    internal Type NarrowTypeBySwitchOnDiscriminantProperty(Type t, Node access, FlowSwitchClauseData data)
    {
        if (data.ClauseStart < data.ClauseEnd && (t._flags & TypeFlags.Union) != 0)
        {
            TryGetAccessedPropertyName(access, out var accessedName);
            if (accessedName != "" && GetKeyPropertyName(t) == accessedName)
            {
                var clauseTypes = GetSwitchClauseTypes(data.SwitchStatement!)[data.ClauseStart..data.ClauseEnd];
                var candidate = GetUnionTypeWorker(Map(clauseTypes, s =>
                {
                    var result = GetConstituentTypeForKeyType(t, s);
                    return result ?? _unknownType;
                }));
                if (candidate != _unknownType)
                {
                    return candidate;
                }
            }
        }
        return NarrowTypeByDiscriminant(t, access, tt => NarrowTypeBySwitchOnDiscriminant(tt, data));
    }
}

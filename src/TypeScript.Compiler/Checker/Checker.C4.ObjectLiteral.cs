// ported from: internal/checker/checker.go:13349-13904
//   （checkObjectLiteral / checkContextualDeprecations / checkDeprecatedProperty / checkSpreadPropOverrides /
//    getSpreadType / getIndexInfoWithReadonly / isValidSpreadType / getUnionIndexInfos / isNonGenericObjectType /
//    tryMergeUnionOfObjectTypeAndEmptyObject / isSpreadableProperty / getSpreadSymbol /
//    isEmptyObjectTypeOrSpreadsIntoEmptyObject / hasDefaultValue / isConstContext / isInlineImportAttributes /
//    isValidConstAssertionArgument / isConstTypeVariable）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) Go 的闭包 `createObjectLiteralType := func() *Type {…}` → C# **局部函数** `Type CreateObjectLiteralType()`：
//    同样按"捕获变量"语义（`offset`/`propertiesArray`/`propertiesTable`/`hasComputed*`/`objectFlags` 的后续
//    赋值在闭包内可见，与 Go 的闭包捕获一致）。
// 2) Go 的 `propertiesArray` 是 `[]*ast.Symbol`（nil 切片 + append）→ C# `List<Symbol>`（nil 切片 → `[]`）；
//    `propertiesArray[offset:]`（零拷贝视图）→ `CollectionsMarshal.AsSpan(propertiesArray)[offset..].ToArray()`
//    （§3.8：Span 视图 + 一次 ToArray，接收方是 `Symbol[]`）。
// 3) Go 的 `map[*Type]*ast.Node`（`c.patternForType`）读缺失键 → `GetValueOrDefault`（Go 零值是 nil；
//    `Dictionary` 索引器会抛）。同 `props[right.Name]`（`ast.SymbolTable`）→ `GetValueOrDefault`。
// 4) Go 的 `var indexInfos []*IndexInfo` + `append` → C# `List<IndexInfo>`，传参处 `.ToArray()`。
// 5) `debug.Assert(...)` → `Debug.Assert(...)`（TypeScript.Debug，抛 DebugFailureException，不杀进程）。
// 6) `core.Some/Every/Map/Filter/Find/SameMap/Concatenate` → `Core` 的静态方法（`using static` 后裸名）；
//    Go 的实例方法值（`c.isEmptyObjectType`、`c.isValidSpreadType`、`c.getBaseConstraintOrType`）→ C# 方法组
//    （同类型内合法且不多分配）；捕获局部变量的（`indexType`/`readonly`）→ lambda。
// 7) 未导出成员命中 `artifacts/checker-method-collisions.md` 的改名照写：`isContextSensitive` →
//    `IsContextSensitiveWorker`、`getPropertyOfType` → `GetPropertyOfTypeWorker`、`getIndexInfoOfType` →
//    `GetIndexInfoOfTypeWorker`、`getIndexInfosOfType` → `GetIndexInfosOfTypeWorker`、`getIndexTypeOfType` →
//    `GetIndexTypeOfTypeWorker`、`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、`getUnionType` → `GetUnionTypeWorker`、
//    `getUnionTypeEx` → `GetUnionTypeExWorker`、`getReducedType` → `GetReducedTypeWorker`、
//    `removeMissingOrUndefinedType` → `RemoveMissingOrUndefinedTypeWorker`、`getContextualType` →
//    `GetContextualTypeWorker`、`symbolToString` → `SymbolToStringWorker`。`c.TypeToString` 是 Go 的导出方法
//    （printer.go:43）→ 保持 `TypeToString`。
namespace TypeScript.Checker;

using System.Runtime.InteropServices;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) checkObjectLiteral(node *ast.Node, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckObjectLiteral(Node node, CheckMode checkMode)
    {
        // Expando object literals have empty properties but filled exports
        if (node.Properties()!.Length == 0 && node.Symbol is not null && (node.Symbol!.Exports?.Count ?? 0) != 0)
        {
            var expandoResult = NewAnonymousType(node.Symbol!, node.Symbol!.Exports, null, null, null);
            if (IsInJSFile(node) && !IsInJsonFile(node))
            {
                expandoResult._objectFlags |= ObjectFlags.JSLiteral;
            }

            // An expando object literal has no property children (len == 0), so there
            // is nothing to check here.
            return expandoResult;
        }

        CheckNodeDeferred(node);
        var inDestructuringPattern = IsAssignmentTarget(node);

        // Grammar checking
        CheckGrammarObjectLiteralExpression(node.AsObjectLiteralExpression()!, inDestructuringPattern);
        SymbolTable? allPropertiesTable = null;
        if (_strictNullChecks)
        {
            allPropertiesTable = new SymbolTable();
        }

        var propertiesTable = new SymbolTable();
        List<Symbol> propertiesArray = [];
        var spread = _emptyObjectType!;
        PushCachedContextualType(node);
        var contextualType = GetApparentTypeOfContextualType(node, ContextFlags.None);
        var contextualTypeHasPattern = false;
        if (contextualType is not null)
        {
            var pattern = _patternForType.GetValueOrDefault(contextualType);
            if (pattern is not null && (IsObjectBindingPattern(pattern) || IsObjectLiteralExpression(pattern)))
            {
                contextualTypeHasPattern = true;
            }
        }

        var inConstContext = IsConstContext(node);
        var checkFlags = CheckFlags.None;
        if (inConstContext)
        {
            checkFlags = CheckFlags.Readonly;
        }

        var objectFlags = ObjectFlags.FreshLiteral;
        var patternWithComputedProperties = false;
        var hasComputedStringProperty = false;
        var hasComputedNumberProperty = false;
        var hasComputedSymbolProperty = false;

        // Spreads may cause an early bail; ensure computed names are always checked (this is cached)
        // As otherwise they may not be checked until exports for the type at this position are retrieved,
        // which may never occur.
        foreach (var elem in node.Properties()!)
        {
            if (elem.Name() is not null && IsComputedPropertyName(elem.Name()!))
            {
                CheckComputedPropertyName(elem.Name()!);
            }
        }

        var offset = 0;

        Type CreateObjectLiteralType()
        {
            List<IndexInfo> indexInfos = [];
            var isReadonly = IsConstContext(node);
            if (hasComputedStringProperty)
            {
                indexInfos.Add(GetObjectLiteralIndexInfo(isReadonly, CollectionsMarshal.AsSpan(propertiesArray)[offset..].ToArray(), _stringType!));
            }

            if (hasComputedNumberProperty)
            {
                indexInfos.Add(GetObjectLiteralIndexInfo(isReadonly, CollectionsMarshal.AsSpan(propertiesArray)[offset..].ToArray(), _numberType!));
            }

            if (hasComputedSymbolProperty)
            {
                indexInfos.Add(GetObjectLiteralIndexInfo(isReadonly, CollectionsMarshal.AsSpan(propertiesArray)[offset..].ToArray(), _esSymbolType!));
            }

            var result = NewAnonymousType(node.Symbol!, propertiesTable, null, null, indexInfos.ToArray());
            result._objectFlags |= objectFlags | ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
            if (contextualType is null && IsInJSFile(node) && !IsInJsonFile(node))
            {
                result._objectFlags |= ObjectFlags.JSLiteral;
            }

            if (patternWithComputedProperties)
            {
                result._objectFlags |= ObjectFlags.ObjectLiteralPatternWithComputedProperties;
            }

            if (inDestructuringPattern)
            {
                _patternForType[result] = node;
            }

            return result;
        }

        foreach (var memberDecl in node.Properties()!)
        {
            var member = GetSymbolOfDeclaration(memberDecl)!;
            Type? computedNameType = null;
            if (memberDecl.Name() is not null && memberDecl.Name()!.Kind == Kind.ComputedPropertyName)
            {
                computedNameType = CheckComputedPropertyName(memberDecl.Name()!);
            }

            if (IsPropertyAssignment(memberDecl) || IsShorthandPropertyAssignment(memberDecl) || IsObjectLiteralMethod(memberDecl))
            {
                Type t;
                switch (memberDecl.Kind)
                {
                    case Kind.PropertyAssignment:
                        t = CheckPropertyAssignment(memberDecl, checkMode);
                        break;
                    case Kind.ShorthandPropertyAssignment:
                        t = CheckShorthandPropertyAssignment(memberDecl, inDestructuringPattern, checkMode);
                        break;
                    default:
                        t = CheckObjectLiteralMethod(memberDecl, checkMode);
                        break;
                }

                objectFlags |= t._objectFlags & ObjectFlags.PropagatingFlags;
                Type? nameType = null;
                if (computedNameType is not null && IsTypeUsableAsPropertyNameWorker(computedNameType))
                {
                    nameType = computedNameType;
                }

                Symbol prop;
                if (nameType is not null)
                {
                    prop = NewSymbolEx(SymbolFlags.Property | member.Flags, GetPropertyNameFromTypeWorker(nameType), checkFlags | CheckFlags.Late);
                }
                else
                {
                    prop = NewSymbolEx(SymbolFlags.Property | member.Flags, member.Name, checkFlags);
                }

                var links = _valueSymbolLinks.Get(prop);
                if (nameType is not null)
                {
                    links._nameType = nameType;
                }

                if (inDestructuringPattern && HasDefaultValue(memberDecl))
                {
                    // If object literal is an assignment pattern and if the assignment pattern specifies a default value
                    // for the property, make the property optional.
                    prop.Flags |= SymbolFlags.Optional;
                }
                else if (contextualTypeHasPattern && (contextualType!._objectFlags & ObjectFlags.ObjectLiteralPatternWithComputedProperties) == 0)
                {
                    // If object literal is contextually typed by the implied type of a binding pattern, and if the
                    // binding pattern specifies a default value for the property, make the property optional.
                    var impliedProp = GetPropertyOfTypeWorker(contextualType, member.Name);
                    if (impliedProp is not null)
                    {
                        prop.Flags |= impliedProp.Flags & SymbolFlags.Optional;
                    }
                    else if (GetIndexInfoOfTypeWorker(contextualType, _stringType!) is null)
                    {
                        Error(memberDecl.Name(), Object_literal_may_only_specify_known_properties_and_0_does_not_exist_in_type_1, SymbolToStringWorker(member), TypeToString(contextualType));
                    }
                }

                prop.Declarations = member.Declarations;
                prop.Parent = member.Parent;
                prop.ValueDeclaration = member.ValueDeclaration;
                links._resolvedType = t;
                links._target = member;
                member = prop;
                if (allPropertiesTable is not null)
                {
                    allPropertiesTable[prop.Name] = prop;
                }

                if (contextualType is not null && (checkMode & CheckMode.Inferential) != 0 && (checkMode & CheckMode.SkipContextSensitive) == 0
                    && (IsPropertyAssignment(memberDecl) || IsMethodDeclaration(memberDecl)) && IsContextSensitiveWorker(memberDecl))
                {
                    var inferenceContext = GetInferenceContext(node);

                    // In CheckMode.Inferential we should always have an inference context
                    var inferenceNode = memberDecl;
                    if (IsPropertyAssignment(memberDecl))
                    {
                        inferenceNode = memberDecl.Initializer()!;
                    }

                    AddIntraExpressionInferenceSite(inferenceContext!, inferenceNode, t);
                }
            }
            else if (memberDecl.Kind == Kind.SpreadAssignment)
            {
                if (propertiesArray.Count > 0)
                {
                    spread = GetSpreadType(spread, CreateObjectLiteralType(), node.Symbol!, objectFlags, inConstContext);
                    propertiesArray = [];
                    propertiesTable = new SymbolTable();
                    hasComputedStringProperty = false;
                    hasComputedNumberProperty = false;
                    hasComputedSymbolProperty = false;
                }

                var t = GetReducedTypeWorker(CheckExpressionEx(memberDecl.Expression()!, checkMode & CheckMode.Inferential));
                if (IsValidSpreadType(t))
                {
                    var mergedType = TryMergeUnionOfObjectTypeAndEmptyObject(t, inConstContext);
                    if (allPropertiesTable is not null)
                    {
                        CheckSpreadPropOverrides(mergedType, allPropertiesTable, memberDecl);
                    }

                    offset = propertiesArray.Count;
                    if (IsErrorType(spread))
                    {
                        continue;
                    }

                    spread = GetSpreadType(spread, mergedType, node.Symbol!, objectFlags, inConstContext);
                }
                else
                {
                    Error(memberDecl, Spread_types_may_only_be_created_from_object_types);
                    spread = _errorType!;
                }

                continue;
            }
            else
            {
                // TypeScript 1.0 spec (April 2014)
                // A get accessor declaration is processed in the same manner as
                // an ordinary function declaration(section 6.1) with no parameters.
                // A set accessor declaration is processed in the same manner
                // as an ordinary function declaration with a single parameter and a Void return type.
                Debug.Assert(memberDecl.Kind == Kind.GetAccessor || memberDecl.Kind == Kind.SetAccessor);
                CheckNodeDeferred(memberDecl);
            }

            if (computedNameType is not null && (computedNameType._flags & TypeFlags.StringOrNumberLiteralOrUnique) == 0)
            {
                if (IsTypeAssignableToWorker(computedNameType, _stringNumberSymbolType!))
                {
                    if (IsTypeAssignableToWorker(computedNameType, _numberType!))
                    {
                        hasComputedNumberProperty = true;
                    }
                    else if (IsTypeAssignableToWorker(computedNameType, _esSymbolType!))
                    {
                        hasComputedSymbolProperty = true;
                    }
                    else
                    {
                        hasComputedStringProperty = true;
                    }

                    if (inDestructuringPattern)
                    {
                        patternWithComputedProperties = true;
                    }
                }
            }
            else
            {
                propertiesTable![member.Name] = member;
            }

            propertiesArray.Add(member!);
        }

        PopContextualType();
        if (IsErrorType(spread))
        {
            return _errorType!;
        }

        if (spread != _emptyObjectType)
        {
            if (propertiesArray.Count > 0)
            {
                spread = GetSpreadType(spread, CreateObjectLiteralType(), node.Symbol!, objectFlags, inConstContext);
                propertiesArray = [];
                propertiesTable = new SymbolTable();
                hasComputedStringProperty = false;
                hasComputedNumberProperty = false;
            }

            // remap the raw emptyObjectType fed in at the top into a fresh empty object literal type, unique to this use site
            return MapType(spread, t => t == _emptyObjectType ? CreateObjectLiteralType() : t);
        }

        return CreateObjectLiteralType();
    }

    /// <summary>Go: <c>func (c *Checker) checkContextualDeprecations(node *ast.Node)</c>。</summary>
    internal void CheckContextualDeprecations(Node node)
    {
        var contextualType = GetApparentTypeOfContextualType(node, ContextFlags.None);
        foreach (var property in node.Properties()!)
        {
            if (IsCanceled())
            {
                return;
            }

            if (property.Name() is not null && !IsComputedPropertyName(property.Name()!))
            {
                CheckDeprecatedProperty(property.Name()!, contextualType);
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) checkDeprecatedProperty(name *ast.Node, contextualType *Type)</c>。</summary>
    internal void CheckDeprecatedProperty(Node name, Type? contextualType)
    {
        if (contextualType is null)
        {
            return;
        }

        var prop = GetPropertyOfTypeWorker(contextualType, name.Text());
        if (prop is null || prop.Declarations!.Length == 0)
        {
            return;
        }

        if (IsDeprecatedSymbol(prop))
        {
            AddDeprecatedSuggestion(name, prop.Declarations!, name.Text());
        }
    }

    /// <summary>Go: <c>func (c *Checker) checkSpreadPropOverrides(t *Type, props ast.SymbolTable, spread *ast.Node)</c>。</summary>
    internal void CheckSpreadPropOverrides(Type t, SymbolTable props, Node spread)
    {
        // Go: range over a nil slice is a no-op.
        foreach (var right in GetPropertiesOfTypeWorker(t) ?? [])
        {
            if ((right.Flags & SymbolFlags.Optional) == 0 && (right.CheckFlags & CheckFlags.Partial) == 0)
            {
                var left = props.GetValueOrDefault(right.Name);
                if (left is not null)
                {
                    var diagnostic = Error(left.ValueDeclaration, X_0_is_specified_more_than_once_so_this_usage_will_be_overwritten, left.Name);
                    diagnostic.AddRelatedInfo(NewDiagnosticForNode(spread, This_spread_always_overwrites_this_property));
                }
            }
        }
    }

    /// <summary>
    /// Since the source of spread types are object literals, which are not binary,
    /// this function should be called in a left folding style, with left = previous result of getSpreadType
    /// and right = the new element to be spread.
    /// </summary>
    internal Type GetSpreadType(Type left, Type right, Symbol symbol, ObjectFlags objectFlags, bool isReadonly)
    {
        if ((left._flags & TypeFlags.Any) != 0 || (right._flags & TypeFlags.Any) != 0)
        {
            return _anyType!;
        }

        if ((left._flags & TypeFlags.Unknown) != 0 || (right._flags & TypeFlags.Unknown) != 0)
        {
            return _unknownType!;
        }

        if ((left._flags & TypeFlags.Never) != 0)
        {
            return right;
        }

        if ((right._flags & TypeFlags.Never) != 0)
        {
            return left;
        }

        left = TryMergeUnionOfObjectTypeAndEmptyObject(left, isReadonly);
        if ((left._flags & TypeFlags.Union) != 0)
        {
            if (CheckCrossProductUnion([left, right]))
            {
                return MapType(left, t => GetSpreadType(t, right, symbol, objectFlags, isReadonly));
            }

            return _errorType!;
        }

        right = TryMergeUnionOfObjectTypeAndEmptyObject(right, isReadonly);
        if ((right._flags & TypeFlags.Union) != 0)
        {
            if (CheckCrossProductUnion([left, right]))
            {
                return MapType(right, t => GetSpreadType(left, t, symbol, objectFlags, isReadonly));
            }

            return _errorType!;
        }

        if ((right._flags & (TypeFlags.BooleanLike | TypeFlags.NumberLike | TypeFlags.BigIntLike | TypeFlags.StringLike | TypeFlags.EnumLike | TypeFlags.NonPrimitive | TypeFlags.Index)) != 0)
        {
            return left;
        }

        if (IsGenericObjectType(left) || IsGenericObjectType(right))
        {
            if (IsEmptyObjectType(left))
            {
                return right;
            }

            // When the left type is an intersection, we may need to merge the last constituent of the
            // intersection with the right type. For example when the left type is 'T & { a: string }'
            // and the right type is '{ b: string }' we produce 'T & { a: string, b: string }'.
            if ((left._flags & TypeFlags.Intersection) != 0)
            {
                var types = left.Types();
                var lastLeft = types[types.Length - 1];
                if (IsNonGenericObjectType(lastLeft) && IsNonGenericObjectType(right))
                {
                    var newTypes = (Type[])types.Clone();
                    newTypes[newTypes.Length - 1] = GetSpreadType(lastLeft, right, symbol, objectFlags, isReadonly);
                    return GetIntersectionType(newTypes);
                }
            }

            return GetIntersectionType([left, right]);
        }

        var members = new SymbolTable();
        Set<string>? skippedPrivateMembers = null;
        IndexInfo[] indexInfos;
        if (left == _emptyObjectType)
        {
            indexInfos = GetIndexInfosOfTypeWorker(right);
        }
        else
        {
            indexInfos = GetUnionIndexInfos([left, right]);
        }

        foreach (var rightProp in GetPropertiesOfTypeWorker(right) ?? [])
        {
            if ((GetDeclarationModifierFlagsFromSymbolWorker(rightProp) & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
            {
                if (skippedPrivateMembers is null)
                {
                    skippedPrivateMembers = new Set<string>();
                }

                skippedPrivateMembers.Add(rightProp.Name);
            }
            else if (IsSpreadableProperty(rightProp))
            {
                members[rightProp.Name] = GetSpreadSymbol(rightProp, isReadonly);
            }
        }

        foreach (var leftProp in GetPropertiesOfTypeWorker(left) ?? [])
        {
            if ((skippedPrivateMembers?.Has(leftProp.Name) ?? false) || !IsSpreadableProperty(leftProp))
            {
                continue;
            }

            var rightProp = members.GetValueOrDefault(leftProp.Name);
            if (rightProp is not null)
            {
                var rightType = GetTypeOfSymbolWorker(rightProp);
                if ((rightProp.Flags & SymbolFlags.Optional) != 0)
                {
                    var declarations = Concatenate(leftProp.Declarations, rightProp.Declarations);
                    var flags = SymbolFlags.Property | (leftProp.Flags & SymbolFlags.Optional);
                    var result = NewSymbol(flags, leftProp.Name);
                    var links = _valueSymbolLinks.Get(result);

                    // Optimization: avoid calculating the union type if spreading into the exact same type.
                    // This is common, e.g. spreading one options bag into another where the bags have the
                    // same type, or have properties which overlap. If the unions are large, it may turn out
                    // to be expensive to perform subtype reduction.
                    var leftType = GetTypeOfSymbolWorker(leftProp);
                    var leftTypeWithoutUndefined = RemoveMissingOrUndefinedTypeWorker(leftType);
                    var rightTypeWithoutUndefined = RemoveMissingOrUndefinedTypeWorker(rightType);
                    if (leftTypeWithoutUndefined == rightTypeWithoutUndefined)
                    {
                        links._resolvedType = leftType;
                    }
                    else
                    {
                        links._resolvedType = GetUnionTypeExWorker([leftType, rightTypeWithoutUndefined], UnionReduction.Subtype, null, null);
                    }

                    _spreadLinks.Get(result)._leftSpread = leftProp;
                    _spreadLinks.Get(result)._rightSpread = rightProp;
                    result.Declarations = declarations;
                    links._nameType = _valueSymbolLinks.Get(leftProp)._nameType;
                    members[leftProp.Name] = result;
                }
            }
            else
            {
                members[leftProp.Name] = GetSpreadSymbol(leftProp, isReadonly);
            }
        }

        var spreadIndexInfos = SameMap(indexInfos, info => GetIndexInfoWithReadonly(info, isReadonly));
        var spread = NewAnonymousType(symbol, members, null, null, spreadIndexInfos);
        spread._objectFlags |= ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral | ObjectFlags.ContainsSpread | objectFlags;
        return spread;
    }

    /// <summary>Go: <c>func (c *Checker) getIndexInfoWithReadonly(info *IndexInfo, readonly bool) *IndexInfo</c>。</summary>
    internal IndexInfo GetIndexInfoWithReadonly(IndexInfo info, bool isReadonly)
    {
        if (info._isReadonly != isReadonly)
        {
            return NewIndexInfo(info._keyType, info._valueType, isReadonly, info._declaration, info._components);
        }

        return info;
    }

    /// <summary>Go: <c>func (c *Checker) isValidSpreadType(t *Type) bool</c>。</summary>
    internal bool IsValidSpreadType(Type t)
    {
        var s = RemoveDefinitelyFalsyTypes(MapType(t, GetBaseConstraintOrType));
        return (s._flags & (TypeFlags.Any | TypeFlags.NonPrimitive | TypeFlags.Object | TypeFlags.InstantiableNonPrimitive)) != 0
            || ((s._flags & TypeFlags.UnionOrIntersection) != 0 && Every(s.Types(), IsValidSpreadType));
    }

    /// <summary>Go: <c>func (c *Checker) getUnionIndexInfos(types []*Type) []*IndexInfo</c>。</summary>
    internal IndexInfo[] GetUnionIndexInfos(Type[] types)
    {
        var sourceInfos = GetIndexInfosOfTypeWorker(types[0]);
        List<IndexInfo> result = [];
        foreach (var info in sourceInfos)
        {
            var indexType = info._keyType;
            if (Every(types, t => GetIndexInfoOfTypeWorker(t, indexType!) is not null))
            {
                var valueType = GetUnionTypeWorker(Map(types, t => GetIndexTypeOfTypeWorker(t, indexType!)!));
                var isReadonly = Some(types, t => GetIndexInfoOfTypeWorker(t, indexType!)!._isReadonly);
                result.Add(NewIndexInfo(indexType, valueType, isReadonly, null, null));
            }
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func (c *Checker) isNonGenericObjectType(t *Type) bool</c>。</summary>
    internal bool IsNonGenericObjectType(Type t)
    {
        return (t._flags & TypeFlags.Object) != 0 && !IsGenericMappedType(t);
    }

    /// <summary>Go: <c>func (c *Checker) tryMergeUnionOfObjectTypeAndEmptyObject(t *Type, readonly bool) *Type</c>。</summary>
    internal Type TryMergeUnionOfObjectTypeAndEmptyObject(Type t, bool isReadonly)
    {
        if ((t._flags & TypeFlags.Union) == 0)
        {
            return t;
        }

        if (Every(t.Types(), IsEmptyObjectTypeOrSpreadsIntoEmptyObject))
        {
            var empty = Find(t.Types(), IsEmptyObjectType);
            if (empty is not null)
            {
                return empty;
            }

            return _emptyObjectType!;
        }

        var firstType = Find(t.Types(), t2 => !IsEmptyObjectTypeOrSpreadsIntoEmptyObject(t2));
        if (firstType is null)
        {
            return t;
        }

        var secondType = Find(t.Types(), t2 => t2 != firstType && !IsEmptyObjectTypeOrSpreadsIntoEmptyObject(t2));
        if (secondType is not null)
        {
            return t;
        }

        // gets the type as if it had been spread, but where everything in the spread is made optional
        var members = new SymbolTable();
        foreach (var prop in GetPropertiesOfTypeWorker(firstType) ?? [])
        {
            if ((GetDeclarationModifierFlagsFromSymbolWorker(prop) & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
            {
                // do nothing, skip privates
            }
            else if (IsSpreadableProperty(prop))
            {
                var isSetonlyAccessor = (prop.Flags & SymbolFlags.SetAccessor) != 0 && (prop.Flags & SymbolFlags.GetAccessor) == 0;
                var flags = SymbolFlags.Property | SymbolFlags.Optional;
                var result = NewSymbolEx(flags, prop.Name, (prop.CheckFlags & CheckFlags.Late) | IfElse(isReadonly, CheckFlags.Readonly, default));
                var links = _valueSymbolLinks.Get(result);
                if (isSetonlyAccessor)
                {
                    links._resolvedType = _undefinedType;
                }
                else
                {
                    links._resolvedType = AddOptionalityEx(GetTypeOfSymbolWorker(prop), true /*isProperty*/, true /*isOptional*/);
                }

                result.Declarations = prop.Declarations;
                links._nameType = _valueSymbolLinks.Get(prop)._nameType;
                _mappedSymbolLinks.Get(result)._syntheticOrigin = prop;
                members[prop.Name] = result;
            }
        }

        var spread = NewAnonymousType(firstType._symbol, members, null, null, GetIndexInfosOfTypeWorker(firstType));
        spread._objectFlags |= ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        return spread;
    }

    // We approximate own properties as non-methods plus methods that are inside the object literal
    /// <summary>Go: <c>func (c *Checker) isSpreadableProperty(prop *ast.Symbol) bool</c>。</summary>
    internal bool IsSpreadableProperty(Symbol prop)
    {
        return (!Some(prop.Declarations, IsPrivateIdentifierClassElementDeclaration)
                && (prop.Flags & (SymbolFlags.Method | SymbolFlags.GetAccessor | SymbolFlags.SetAccessor)) == 0)
            || !Some(prop.Declarations, d => d.Parent is not null && IsClassLike(d.Parent));
    }

    /// <summary>Go: <c>func (c *Checker) getSpreadSymbol(prop *ast.Symbol, readonly bool) *ast.Symbol</c>。</summary>
    internal Symbol GetSpreadSymbol(Symbol prop, bool isReadonly)
    {
        var isSetonlyAccessor = (prop.Flags & SymbolFlags.SetAccessor) != 0 && (prop.Flags & SymbolFlags.GetAccessor) == 0;
        if (!isSetonlyAccessor && isReadonly == IsReadonlySymbolWorker(prop))
        {
            return prop;
        }

        var flags = SymbolFlags.Property | (prop.Flags & SymbolFlags.Optional);
        var result = NewSymbolEx(flags, prop.Name, (prop.CheckFlags & CheckFlags.Late) | IfElse(isReadonly, CheckFlags.Readonly, default));
        var links = _valueSymbolLinks.Get(result);
        if (isSetonlyAccessor)
        {
            links._resolvedType = _undefinedType;
        }
        else
        {
            links._resolvedType = GetTypeOfSymbolWorker(prop);
        }

        result.Declarations = prop.Declarations;
        links._nameType = _valueSymbolLinks.Get(prop)._nameType;
        _mappedSymbolLinks.Get(result)._syntheticOrigin = prop;
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) isEmptyObjectTypeOrSpreadsIntoEmptyObject(t *Type) bool</c>。</summary>
    internal bool IsEmptyObjectTypeOrSpreadsIntoEmptyObject(Type t)
    {
        return IsEmptyObjectType(t)
            || (t._flags & (TypeFlags.Null | TypeFlags.Undefined | TypeFlags.BooleanLike | TypeFlags.NumberLike | TypeFlags.BigIntLike | TypeFlags.StringLike | TypeFlags.EnumLike | TypeFlags.NonPrimitive | TypeFlags.Index)) != 0;
    }

    /// <summary>Go: <c>func (c *Checker) hasDefaultValue(node *ast.Node) bool</c>。</summary>
    internal bool HasDefaultValue(Node node)
    {
        return (IsBindingElement(node) && node.Initializer() is not null)
            || (IsPropertyAssignment(node) && HasDefaultValue(node.Initializer()!))
            || (IsShorthandPropertyAssignment(node) && node.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer is not null)
            || (IsBinaryExpression(node) && node.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken);
    }

    /// <summary>Go: <c>func (c *Checker) isConstContext(node *ast.Node) bool</c>。</summary>
    internal bool IsConstContext(Node node)
    {
        var parent = node.Parent;
        return IsConstAssertion(parent)
            || IsInlineImportAttributes(node)
            || (IsValidConstAssertionArgument(node) && IsConstTypeVariable(GetContextualTypeWorker(node, ContextFlags.None), 0))
            || ((IsParenthesizedExpression(parent!) || IsArrayLiteralExpression(parent!) || IsSpreadElement(parent!)) && IsConstContext(parent!))
            || ((IsPropertyAssignment(parent!) || IsShorthandPropertyAssignment(parent!) || IsTemplateSpan(parent!)) && IsConstContext(parent!.Parent!));
    }

    /// <summary>Go: <c>func (c *Checker) isInlineImportAttributes(node *ast.Node) bool</c>。</summary>
    internal bool IsInlineImportAttributes(Node node)
    {
        if (!IsObjectLiteralExpression(node) || !IsPropertyAssignment(node.Parent!) || node.Parent!.Initializer() != node)
        {
            return false;
        }

        var property = node.Parent!;
        if ((!IsIdentifier(property.Name()!) && !IsStringLiteralLike(property.Name()!)) || property.Name()!.Text() != "with")
        {
            return false;
        }

        var options = property.Parent!;
        if (!IsObjectLiteralExpression(options))
        {
            return false;
        }

        var importCall = FindAncestor(options, IsImportCall);
        return importCall is not null && importCall.Arguments()!.Length > 1 && SkipParentheses(importCall.Arguments()![1]) == options;
    }

    /// <summary>Go: <c>func (c *Checker) isValidConstAssertionArgument(node *ast.Node) bool</c>。</summary>
    internal bool IsValidConstAssertionArgument(Node node)
    {
        switch (node.Kind)
        {
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.ArrayLiteralExpression:
            case Kind.ObjectLiteralExpression:
            case Kind.TemplateExpression:
                return true;
            case Kind.ParenthesizedExpression:
                return IsValidConstAssertionArgument(node.Expression()!);
            case Kind.PrefixUnaryExpression:
                var op = node.AsPrefixUnaryExpression()!.Operator;
                var arg = node.AsPrefixUnaryExpression()!.Operand!;
                return (op == Kind.MinusToken && (arg.Kind == Kind.NumericLiteral || arg.Kind == Kind.BigIntLiteral))
                    || (op == Kind.PlusToken && arg.Kind == Kind.NumericLiteral);
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                var expr = SkipParentheses(node.Expression()!)!;
                Symbol? symbol = null;
                if (IsEntityNameExpression(expr))
                {
                    symbol = ResolveEntityName(expr, SymbolFlags.Value, true /*ignoreErrors*/, false, null);
                }

                return symbol is not null && (symbol.Flags & SymbolFlags.Enum) != 0;
        }

        return false;
    }

    /// <summary>Go: <c>func (c *Checker) isConstTypeVariable(t *Type, depth int) bool</c>。</summary>
    internal bool IsConstTypeVariable(Type? t, int depth)
    {
        if (depth >= 5 || t is null)
        {
            return false;
        }

        if ((t._flags & TypeFlags.TypeParameter) != 0)
        {
            return t._symbol is not null && Some(t._symbol.Declarations, d => HasSyntacticModifier(d, ModifierFlags.Const));
        }

        if ((t._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            return Some(t.Types(), s => IsConstTypeVariable(s, depth));
        }

        if ((t._flags & TypeFlags.IndexedAccess) != 0)
        {
            return IsConstTypeVariable(t.AsIndexedAccessType()!._objectType, depth + 1);
        }

        if ((t._flags & TypeFlags.Conditional) != 0)
        {
            return IsConstTypeVariable(GetConstraintOfConditionalType(t), depth + 1);
        }

        if ((t._flags & TypeFlags.Substitution) != 0)
        {
            return IsConstTypeVariable(t.AsSubstitutionType()!._baseType, depth);
        }

        if ((t._objectFlags & ObjectFlags.Mapped) != 0)
        {
            var typeVariable = GetHomomorphicTypeVariable(t);
            return typeVariable is not null && IsConstTypeVariable(typeVariable, depth);
        }

        if (IsGenericTupleType(t))
        {
            var elementTypes = GetElementTypes(t);
            for (int i = 0; i < elementTypes.Length; i++)
            {
                if ((t.TargetTupleType()!._elementInfos![i]._flags & ElementFlags.Variadic) != 0 && IsConstTypeVariable(elementTypes[i], depth))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

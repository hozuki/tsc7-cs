// ported from: internal/checker/checker.go:18693-19183
//   （getWidenedType … reportCircularityError：类型加宽、可空/可选包装、访问器与别名的 value 类型解析、
//     节点标志缓存、类型解析栈（循环检测）与循环报错；30 个顶层声明的窗口）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) **Worker 后缀**（artifacts/checker-method-collisions.md：同一 partial class 里 Go 导出 `GetX` 与未导出 `getX`
//    会 CS0111）：`getWidenedType` → `GetWidenedTypeWorker`（导出的包装器 `GetWidenedType` 在 exports.go:390）。
//    本文件调用的其它未导出同名项同样按清单改名：`getUnionType`→`GetUnionTypeWorker`、
//    `getUnionTypeEx`→`GetUnionTypeExWorker`、`getTypeArguments`→`GetTypeArgumentsWorker`、
//    `getTypeOfSymbol`→`GetTypeOfSymbolWorker`、`getSymbolFlags`→`GetSymbolFlagsWorker`、
//    `resolveAlias`→`ResolveAliasWorker`、`getPropertiesOfType`→`GetPropertiesOfTypeWorker`、
//    `getIndexInfosOfType`→`GetIndexInfosOfTypeWorker`、`symbolToString`→`SymbolToStringWorker`。
// 2) Go 的 `(T, bool)` → `bool TryX(..., out T)`（docs/checker-plan.md §5.2/§5.4）：
//    `getEffectivePropertyNameForPropertyNameNode` → `TryGetEffectivePropertyNameForPropertyNameNode(Node, out string)`
//    （调用点 Checker.C2.ClassLike.cs:198、GrammarChecks.Declarations.cs:179 已按此写）；
//    `tryGetNameFromType` → `TryGetNameFromType(Type, out string)`（flow.go:1782 的**包级**同名函数由 Flow 分片命名为
//    `TryGetNameFromTypeWorker`，见 Flow.Keys.cs:281，两者不冲突）。
// 3) `WideningContext` 由 Checker.Keys.cs（C1 分片）声明为 `public sealed partial class`；Go 的
//    `func (w *WideningContext) getChildContext(...)`（checker.go:18752）在本文件末尾以同一 partial 类型的分片落地。
// 4) Go 的 map 读缺失键返回零值（nil）→ `TryGetValue`；写/增用索引器（docs/architecture.md §3.2）。C1 把
//    `WideningContext._siblings`/`_resolvedProperties` 定为 `List<T>?`，故 `GetSiblingsOfContext`/`GetPropertiesOfContext`
//    返回 `List<T>`（Go 返回 `[]*Type`/`[]*ast.Symbol`），`append(xs, ys...)` → `List<T>.AddRange`；`[]*Type{...}` →
//    `new Type[] { ... }`。
// 5) Go 的 `resolution.target == target`（接口值比较 = 动态类型 + 指针值）→ `ReferenceEquals`：本窗口里 target 恒为
//    Symbol/Type/Signature/Node 指针（Go 的 TypeSystemEntity 只放指针）。
// 6) Go 的 `links != nil`（Go 的 LinkStore.Get 必新建槽位、恒非 nil）在 C# 里恒真 → 删；Go 里"若为 nil 就会
//    panic/NRE"的不变量位置按 C# 可空语义写 `!`（仅编译期）。Go 的 `panic(...)` → `Debug.Fail(...)`。
// 7) `c.getAnnotatedAccessorType(getter)` 的 C# 形参（C6 分片）是 `Node`（未标 `Node?`），而 Go 允许传 nil（实现内部
//    判空）→ 调用点写 `getter!`/`setter!`/`accessor!`（只消除 CS8604，运行时行为与 Go 完全一致）。
// 8) 字段命名：Go 未导出字段 → `_camelCase`（`t.flags`→`t._flags`、`links.resolvedType`→`links._resolvedType`、
//    `c.cachedTypes`→`_cachedTypes`、`context.widenedTypes`→`context._widenedTypes`）；Go 导出字段保持原名
//    （`Symbol.Flags`/`Symbol.Declarations`/`IndexInfo.KeyType`…）。
// 9) Go 在 `getWidenedTypeWithContext` 的闭包里用 `func(t *Type)` 遮蔽了外层形参 `t`（C# 不允许遮蔽，CS0136）→ 闭包
//    形参改名 `x`；同理 `getWidenedTypeOfObjectLiteral` 的 `info` 闭包形参保持 Go 名。
// 10) `foreach (var x in arr)` 的 C# 数组元素类型来自 C5 分片的可空返回（`Symbol[]?`）→ 加 `?? []`（Go 的 nil slice 与空
//    slice 在 `range` 下语义相同，docs/architecture.md §3.2）；`t.Target()` 在 C# 声明为 `Type?`，此处 Go 的调用点必非
//    nil（`isArrayOrTupleType` 分支）→ 写 `t.Target()!`。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;

public sealed partial class Checker
{
    internal Type GetWidenedTypeWorker(Type t)
    {
        return GetWidenedTypeWithContext(t, null /*context*/);
    }

    internal Type GetWidenedTypeWithContext(Type t, WideningContext? context)
    {
        if ((t._objectFlags & ObjectFlags.RequiresWidening) != 0)
        {
            if (context == null)
            {
                if (_cachedTypes.TryGetValue(new CachedTypeKey(CachedTypeKind.Widened, t._id), out var cached))
                {
                    return cached;
                }
            }
            Type? result = null;
            if ((t._flags & (TypeFlags.Any | TypeFlags.Nullable)) != 0)
            {
                result = _anyType;
            }
            else if (IsObjectLiteralType(t))
            {
                result = GetWidenedTypeOfObjectLiteral(t, context);
            }
            else if ((t._flags & TypeFlags.Union) != 0)
            {
                var unionContext = context;
                if (unionContext == null)
                {
                    unionContext = new WideningContext { _siblings = [.. t.Types()] };
                }
                var widenedTypes = SameMap(t.Types(), (Type x) =>
                {
                    if ((x._flags & TypeFlags.Nullable) != 0)
                    {
                        return x;
                    }
                    return GetWidenedTypeWithContext(x, unionContext);
                });
                // Widening an empty object literal transitions from a highly restrictive type to
                // a highly inclusive one. For that reason we perform subtype reduction here if the
                // union includes empty object types (e.g. reducing {} | string to just {}).
                result = GetUnionTypeExWorker(widenedTypes, Some(widenedTypes, IsEmptyObjectType) ? UnionReduction.Subtype : UnionReduction.Literal, null, null);
            }
            else if ((t._flags & TypeFlags.Intersection) != 0)
            {
                result = GetIntersectionType(SameMap(t.Types(), GetWidenedTypeWorker));
            }
            else if (IsArrayOrTupleType(t))
            {
                result = CreateTypeReference(t.Target()!, SameMap(GetTypeArgumentsWorker(t), GetWidenedTypeWorker));
            }
            if (result != null && context == null)
            {
                _cachedTypes[new CachedTypeKey(CachedTypeKind.Widened, t._id)] = result;
            }
            return OrElse(result, t)!;
        }
        return t;
    }

    internal Type GetWidenedTypeOfObjectLiteral(Type t, WideningContext? context)
    {
        if (context != null && context._widenedTypes != null && context._widenedTypes.TryGetValue(t, out var cached))
        {
            return cached;
        }
        var members = new SymbolTable();
        // C5 的 `GetPropertiesOfObjectType` 在 C# 里返回 `Symbol[]?` → `?? []`（Go 的 nil slice 与空 slice 在 `range` 下等价）。
        foreach (var prop in GetPropertiesOfObjectType(t) ?? [])
        {
            members[prop.Name] = GetWidenedProperty(prop, context);
        }
        if (context != null)
        {
            foreach (var prop in GetPropertiesOfContext(context))
            {
                if (!members.ContainsKey(prop.Name))
                {
                    members[prop.Name] = GetUndefinedProperty(prop);
                }
            }
        }
        var result = NewAnonymousType(t._symbol!, members, null, null, SameMap(GetIndexInfosOfTypeWorker(t), (IndexInfo info) =>
            NewIndexInfo(info._keyType, GetWidenedTypeWorker(info._valueType!), info._isReadonly, info._declaration, info._components)));
        // Retain js literal flag through widening
        result._objectFlags |= t._objectFlags & (ObjectFlags.JSLiteral | ObjectFlags.NonInferrableType);
        // Only cache in child contexts since the root context never widens a particular object literal type more than once
        if (context != null && context._parent != null)
        {
            if (context._widenedTypes == null)
            {
                context._widenedTypes = new Dictionary<Type, Type>();
            }
            context._widenedTypes[t] = result;
        }
        return result;
    }

    internal Symbol GetWidenedProperty(Symbol prop, WideningContext? context)
    {
        if ((prop.Flags & SymbolFlags.Property) == 0)
        {
            // Since get accessors already widen their return value there is no need to
            // widen accessor based properties here.
            return prop;
        }
        var original = GetTypeOfSymbolWorker(prop);
        WideningContext? propContext = null;
        if (context != null)
        {
            propContext = context.GetChildContext(prop.Name);
        }
        var widened = GetWidenedTypeWithContext(original, propContext);
        if (widened == original)
        {
            return prop;
        }
        return CreateSymbolWithType(prop, widened);
    }

    internal List<Symbol> GetPropertiesOfContext(WideningContext context)
    {
        if (context._resolvedProperties == null)
        {
            var names = new OrderedMap<string, Symbol>();
            foreach (var t in GetSiblingsOfContext(context))
            {
                if (IsObjectLiteralType(t) && (t._objectFlags & ObjectFlags.ContainsSpread) == 0)
                {
                    foreach (var prop in GetPropertiesOfTypeWorker(t) ?? [])
                    {
                        names.Set(prop.Name, prop);
                    }
                }
            }
            var resolvedProperties = new List<Symbol>();
            foreach (var value in names.Values())
            {
                resolvedProperties.Add(value!);
            }
            context._resolvedProperties = resolvedProperties;
        }
        return context._resolvedProperties!;
    }

    internal List<Type> GetSiblingsOfContext(WideningContext context)
    {
        if (context._siblings == null)
        {
            var siblings = new List<Type>();
            // Go: `c.getSiblingsOfContext(context.parent)`；根 context 必带 siblings（见 GetWidenedTypeWithContext），
            // 因此递归只会沿非 nil 的 parent 走，nil parent 在 Go 里同样是解引用崩溃。
            foreach (var t in GetSiblingsOfContext(context._parent!))
            {
                if (IsObjectLiteralType(t))
                {
                    var prop = GetPropertyOfObjectType(t, context._propertyName);
                    if (prop != null)
                    {
                        siblings.AddRange(GetTypeOfSymbolWorker(prop).Distributed());
                    }
                }
            }
            context._siblings = siblings;
        }
        return context._siblings!;
    }

    internal Symbol GetUndefinedProperty(Symbol prop)
    {
        if (_undefinedProperties.TryGetValue(prop.Name, out var cached))
        {
            return cached;
        }
        var result = CreateSymbolWithType(prop, _undefinedOrMissingType);
        result.Flags |= SymbolFlags.Optional;
        _undefinedProperties[prop.Name] = result;
        return result;
    }

    internal Type GetTypeOfEnumMember(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._resolvedType == null)
        {
            links._resolvedType = GetDeclaredTypeOfEnumMember(symbol);
        }
        return links._resolvedType!;
    }

    internal Type GetTypeOfAccessors(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._resolvedType == null)
        {
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.Type))
            {
                return _errorType;
            }
            var getter = GetDeclarationOfKind(symbol, Kind.GetAccessor);
            var setter = GetDeclarationOfKind(symbol, Kind.SetAccessor);
            Node? accessor = Find(symbol.Declarations, IsAutoAccessorPropertyDeclaration);
            // We try to resolve a getter type annotation, a setter type annotation, or a getter function
            // body return type inference, in that order.
            Type? t = GetAnnotatedAccessorType(getter!);
            if (t == null)
            {
                t = GetAnnotatedAccessorType(setter!);
            }
            if (t == null)
            {
                t = GetAnnotatedAccessorType(accessor!);
            }
            if (t == null && getter != null)
            {
                if (getter.Body() != null)
                {
                    t = GetReturnTypeFromBody(getter, CheckMode.Normal);
                }
            }
            if (t == null && accessor != null)
            {
                t = GetWidenedTypeForVariableLikeDeclaration(accessor, true /*reportErrors*/);
            }
            if (t == null)
            {
                if (setter != null && !IsPrivateWithinAmbient(setter))
                {
                    ErrorOrSuggestion(_noImplicitAny, setter, Diagnostics.Property_0_implicitly_has_type_any_because_its_set_accessor_lacks_a_parameter_type_annotation, SymbolToStringWorker(symbol));
                }
                else if (getter != null && !IsPrivateWithinAmbient(getter))
                {
                    ErrorOrSuggestion(_noImplicitAny, getter, Diagnostics.Property_0_implicitly_has_type_any_because_its_get_accessor_lacks_a_return_type_annotation, SymbolToStringWorker(symbol));
                }
                else if (accessor != null && !IsPrivateWithinAmbient(accessor))
                {
                    ErrorOrSuggestion(_noImplicitAny, accessor, Diagnostics.Member_0_implicitly_has_an_1_type, SymbolToStringWorker(symbol), "any");
                }
                t = _anyType;
            }
            if (!PopTypeResolution())
            {
                if (GetAnnotatedAccessorTypeNode(getter) != null)
                {
                    Error(getter, Diagnostics.X_0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToStringWorker(symbol));
                }
                else if (GetAnnotatedAccessorTypeNode(setter) != null)
                {
                    Error(setter, Diagnostics.X_0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToStringWorker(symbol));
                }
                else if (GetAnnotatedAccessorTypeNode(accessor) != null)
                {
                    // Go 此处传的是 setter（不是 accessor），照抄以保持 1:1。
                    Error(setter, Diagnostics.X_0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToStringWorker(symbol));
                }
                else if (getter != null && _noImplicitAny)
                {
                    Error(getter, Diagnostics.X_0_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions, SymbolToStringWorker(symbol));
                }
                t = _anyType;
            }
            if (links._resolvedType == null)
            {
                links._resolvedType = t;
            }
        }
        return links._resolvedType!;
    }

    internal Type GetWriteTypeOfAccessors(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._writeType == null)
        {
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.WriteType))
            {
                return _errorType;
            }
            var setter = GetDeclarationOfKind(symbol, Kind.SetAccessor);
            if (setter == null)
            {
                var propDeclaration = GetDeclarationOfKind(symbol, Kind.PropertyDeclaration);
                if (propDeclaration != null && IsAutoAccessorPropertyDeclaration(propDeclaration))
                {
                    setter = propDeclaration;
                }
            }
            Type? writeType = GetAnnotatedAccessorType(setter!);
            if (!PopTypeResolution())
            {
                if (GetAnnotatedAccessorTypeNode(setter) != null)
                {
                    Error(setter, Diagnostics.X_0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToStringWorker(symbol));
                }
                writeType = _anyType;
            }
            // Absent an explicit setter type annotation we use the read type of the accessor.
            if (links._writeType == null)
            {
                if (writeType != null)
                {
                    links._writeType = writeType;
                }
                else
                {
                    links._writeType = GetTypeOfAccessors(symbol);
                }
            }
        }
        return links._writeType!;
    }

    internal Type GetTypeOfAlias(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._resolvedType == null)
        {
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.Type))
            {
                return _errorType;
            }
            var targetSymbol = ResolveAliasWorker(symbol);
            var exportSymbol = GetTargetOfAliasDeclaration(GetDeclarationOfAliasSymbol(symbol)!);
            // It only makes sense to get the type of a value symbol. If the result of resolving
            // the alias is not a value, then it has no type. To get the type associated with a
            // type symbol, call getDeclaredTypeOfSymbol.
            // This check is important because without it, a call to getTypeOfSymbol could end
            // up recursively calling getTypeOfAlias, causing a stack overflow.
            if (links._resolvedType == null)
            {
                if ((GetSymbolFlagsWorker(targetSymbol) & SymbolFlags.Value) != 0)
                {
                    links._resolvedType = GetTypeOfSymbolWorker(targetSymbol);
                }
                else
                {
                    links._resolvedType = _errorType;
                }
            }
            if (!PopTypeResolution())
            {
                ReportCircularityError(OrElse(exportSymbol, symbol)!);
                if (links._resolvedType == null)
                {
                    links._resolvedType = _errorType;
                }
                return links._resolvedType;
            }
        }
        return links._resolvedType!;
    }

    internal Type AddOptionality(Type t)
    {
        return AddOptionalityEx(t, false /*isProperty*/, true /*isOptional*/);
    }

    internal Type AddOptionalityEx(Type t, bool isProperty, bool isOptional)
    {
        if (_strictNullChecks && isOptional)
        {
            return GetOptionalType(t, isProperty);
        }
        return t;
    }

    internal Type GetOptionalType(Type t, bool isProperty)
    {
        Debug.Assert(_strictNullChecks);
        var missingOrUndefined = IfElse(isProperty, _undefinedOrMissingType, _undefinedType);
        if (t == missingOrUndefined || (t._flags & TypeFlags.Union) != 0 && t.Types()[0] == missingOrUndefined)
        {
            return t;
        }
        return GetUnionTypeWorker(new Type[] { t, missingOrUndefined });
    }

    // Add undefined or null or both to a type if they are missing.
    internal Type GetNullableType(Type t, TypeFlags flags)
    {
        var missing = (flags & ~t._flags) & (TypeFlags.Undefined | TypeFlags.Null);
        if (missing == 0)
        {
            return t;
        }
        else if (missing == TypeFlags.Undefined)
        {
            return GetUnionTypeWorker(new Type[] { t, _undefinedType });
        }
        else if (missing == TypeFlags.Null)
        {
            return GetUnionTypeWorker(new Type[] { t, _nullType });
        }
        return GetUnionTypeWorker(new Type[] { t, _undefinedType, _nullType });
    }

    public Type GetNonNullableType(Type t)
    {
        if (_strictNullChecks)
        {
            return GetAdjustedTypeWithFacts(t, TypeFacts.NEUndefinedOrNull);
        }
        return t;
    }

    public bool IsNullableType(Type t)
    {
        return HasTypeFacts(t, TypeFacts.IsUndefinedOrNull);
    }

    internal Type GetNonNullableTypeIfNeeded(Type t)
    {
        if (IsNullableType(t))
        {
            return GetNonNullableType(t);
        }
        return t;
    }

    internal NodeFlags GetDeclarationNodeFlagsFromSymbol(Symbol s)
    {
        if (s.ValueDeclaration != null)
        {
            return GetCombinedNodeFlagsCached(s.ValueDeclaration);
        }
        return NodeFlags.None;
    }

    internal NodeFlags GetCombinedNodeFlagsCached(Node node)
    {
        // we hold onto the last node and result to speed up repeated lookups against the same node.
        if (_lastGetCombinedNodeFlagsNode == node)
        {
            return _lastGetCombinedNodeFlagsResult;
        }
        _lastGetCombinedNodeFlagsNode = node;
        _lastGetCombinedNodeFlagsResult = GetCombinedNodeFlags(node);
        return _lastGetCombinedNodeFlagsResult;
    }

    internal bool IsVarConstLike(Node node)
    {
        var blockScopeKind = GetCombinedNodeFlagsCached(node) & NodeFlags.BlockScoped;
        return blockScopeKind == NodeFlags.Const || blockScopeKind == NodeFlags.Using || blockScopeKind == NodeFlags.AwaitUsing;
    }

    internal bool TryGetEffectivePropertyNameForPropertyNameNode(Node node, out string name)
    {
        name = GetPropertyNameForPropertyNameNode(node);
        if (name != InternalSymbolNameMissing)
        {
            return true;
        }
        else if (IsComputedPropertyName(node))
        {
            // This is cached so `getTypeOfExpression` isn't constantly reinvoked for every property name lookup
            var links = _computedNameLinks.Get(node);
            if (links._hasName != null)
            {
                name = links._name;
                return links._hasName.Value;
            }
            var exists = TryGetNameFromType(GetTypeOfExpression(node.Expression()!), out name);
            links._name = name;
            links._hasName = exists;
            return exists;
        }
        name = "";
        return false;
    }

    internal bool TryGetNameFromType(Type t, out string name)
    {
        if ((t._flags & TypeFlags.UniqueESSymbol) != 0)
        {
            name = t.AsUniqueESSymbolType()!._name;
            return true;
        }
        else if ((t._flags & TypeFlags.StringLiteral) != 0)
        {
            name = GetStringLiteralValue(t);
            return true;
        }
        else if ((t._flags & TypeFlags.NumberLiteral) != 0)
        {
            name = GetNumberLiteralValue(t).String();
            return true;
        }
        name = "";
        return false;
    }

    internal ModifierFlags GetCombinedModifierFlagsCached(Node node)
    {
        // we hold onto the last node and result to speed up repeated lookups against the same node.
        if (_lastGetCombinedModifierFlagsNode == node)
        {
            return _lastGetCombinedModifierFlagsResult;
        }
        _lastGetCombinedModifierFlagsNode = node;
        _lastGetCombinedModifierFlagsResult = GetCombinedModifierFlags(node);
        return _lastGetCombinedModifierFlagsResult;
    }

    /// <summary>
    /// Push an entry on the type resolution stack. If an entry with the given target and the given property name
    /// is already on the stack, and no entries in between already have a type, then a circularity has occurred.
    /// In this case, the result values of the existing entry and all entries pushed after it are changed to false,
    /// and the value false is returned. Otherwise, the new entry is just pushed onto the stack, and true is returned.
    /// In order to see if the same query has already been done before, the target object and the propertyName both
    /// must match the one passed in.
    /// </summary>
    /// <param name="target">The symbol, type, or signature whose type is being queried</param>
    /// <param name="propertyName">The property name that should be used to query the target for its type</param>
    internal bool PushTypeResolution(object? target, TypeSystemPropertyName propertyName)
    {
        var resolutionCycleStartIndex = FindResolutionCycleStartIndex(target, propertyName);
        if (resolutionCycleStartIndex >= 0)
        {
            // A cycle was found
            for (var i = resolutionCycleStartIndex; i < _typeResolutions.Count; i++)
            {
                _typeResolutions[i]._result = false;
            }
            return false;
        }
        _typeResolutions.Add(new TypeResolution(target, propertyName, true));
        return true;
    }

    /// <summary>
    /// Pop an entry from the type resolution stack and return its associated result value. The result value will
    /// be true if no circularities were detected, or false if a circularity was found.
    /// </summary>
    internal bool PopTypeResolution()
    {
        var lastIndex = _typeResolutions.Count - 1;
        var result = _typeResolutions[lastIndex]._result;
        _typeResolutions[lastIndex] = new TypeResolution(); // Clear the last entry to avoid memory leaks
        _typeResolutions.RemoveAt(lastIndex);
        return result;
    }

    internal int FindResolutionCycleStartIndex(object? target, TypeSystemPropertyName propertyName)
    {
        for (var i = _typeResolutions.Count - 1; i >= _resolutionStart; i--)
        {
            var resolution = _typeResolutions[i];
            if (TypeResolutionHasProperty(resolution))
            {
                return -1;
            }
            if (ReferenceEquals(resolution._target, target) && resolution._propertyName == propertyName)
            {
                return i;
            }
        }
        return -1;
    }

    internal bool TypeResolutionHasProperty(TypeResolution r)
    {
        switch (r._propertyName)
        {
            case TypeSystemPropertyName.Type:
                return _valueSymbolLinks.Get((Symbol)r._target!)._resolvedType != null;
            case TypeSystemPropertyName.DeclaredType:
                return _typeAliasLinks.Get((Symbol)r._target!)._declaredType != null;
            case TypeSystemPropertyName.ResolvedTypeArguments:
                return ((Type)r._target!).AsTypeReference()!._resolvedTypeArguments != null;
            case TypeSystemPropertyName.ResolvedBaseTypes:
                return ((Type)r._target!).AsInterfaceType()!._baseTypesResolved;
            case TypeSystemPropertyName.ResolvedBaseConstructorType:
                return ((Type)r._target!).AsInterfaceType()!._resolvedBaseConstructorType != null;
            case TypeSystemPropertyName.ResolvedReturnType:
                return ((Signature)r._target!)._resolvedReturnType != null;
            case TypeSystemPropertyName.ResolvedBaseConstraint:
                return ((Type)r._target!).AsConstrainedType()!._resolvedBaseConstraint != null;
            case TypeSystemPropertyName.InitializerIsUndefined:
                return (_nodeLinks.Get((Node)r._target!)._flags & NodeCheckFlags.InitializerIsUndefinedComputed) != 0;
            case TypeSystemPropertyName.WriteType:
                return _valueSymbolLinks.Get((Symbol)r._target!)._writeType != null;
            case TypeSystemPropertyName.AliasTarget:
                return _aliasSymbolLinks.Get((Symbol)r._target!)._aliasTarget != null;
        }
        Debug.Fail("Unhandled case in typeResolutionHasProperty");
        return false;
    }

    internal Type ReportCircularityError(Symbol symbol)
    {
        var declaration = symbol.ValueDeclaration;
        // Check if variable has type annotation that circularly references the variable itself
        if (declaration != null)
        {
            if (declaration.Type() != null)
            {
                Error(symbol.ValueDeclaration, Diagnostics.X_0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToStringWorker(symbol));
                return _errorType;
            }
            // Check if variable has initializer that circularly references the variable itself
            if (_noImplicitAny && (!IsParameterDeclaration(declaration) || declaration.Initializer() != null))
            {
                Error(symbol.ValueDeclaration, Diagnostics.X_0_implicitly_has_type_any_because_it_does_not_have_a_type_annotation_and_is_referenced_directly_or_indirectly_in_its_own_initializer, SymbolToStringWorker(symbol));
            }
        }
        else if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            var node = GetDeclarationOfAliasSymbol(symbol);
            if (node != null)
            {
                Error(node, Diagnostics.Circular_definition_of_import_alias_0, SymbolToStringWorker(symbol));
            }
        }
        // Circularities could also result from parameters in function expressions that end up
        // having themselves as contextual types following type argument inference. In those cases
        // we have already reported an implicit any error so we don't report anything here.
        return _anyType;
    }
}

/// <summary>Go: <c>func (w *WideningContext) getChildContext(propertyName string) *WideningContext</c>（checker.go:18752）。</summary>
/// <remarks>
/// Go 里这个方法紧随 `getWidenedProperty`；C# 需要单独的类型声明，故本 partial 分片放在文件末尾（partial 成员与顺序无关）。
/// </remarks>
public sealed partial class WideningContext
{
    internal WideningContext GetChildContext(string propertyName)
    {
        if (_childContexts != null && _childContexts.TryGetValue(propertyName, out var cached))
        {
            return cached;
        }
        var result = new WideningContext { _parent = this, _propertyName = propertyName };
        if (_childContexts == null)
        {
            _childContexts = new Dictionary<string, WideningContext>();
        }
        _childContexts[propertyName] = result;
        return result;
    }
}

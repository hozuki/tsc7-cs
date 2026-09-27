// ported from: internal/checker/checker.go:29617-30519
//
// 与 Go 的差异（逐条，一行一条理由；其余逐行对应，含注释与分支顺序）：
// - 命名（docs/architecture.md §2、docs/checker-plan.md §5.5、Main 终局口径）：Go 未导出**字段** → `_camelCase`
//   （`t.flags`→`t._flags`、`t.symbol`→`t._symbol`、`c.anyType`→`_anyType`、`sig.parameters`→`sig._parameters`、
//   `templ.texts`→`templ._texts`、`info.flags`→`info._flags`）；Go 未导出方法 → `internal` + PascalCase；
//   Go 包级函数 → 本类 `internal static` 成员（`applyStringMapping`→`ApplyStringMapping`、`isSpreadArgument`→`IsSpreadArgument`）。
// - 同名冲突（artifacts/checker-method-collisions.md，未导出者加 `Worker` 后缀）：本文件定义的
//   `getContextualType`→`GetContextualTypeWorker`、`getContextualTypeForArgumentAtIndex`→
//   `GetContextualTypeForArgumentAtIndexWorker`、`getContextualTypeForObjectLiteralElement`→
//   `GetContextualTypeForObjectLiteralElementWorker`（调用点同样用新名）；调用的**外片**成员同理：
//   `GetResolvedSignatureWorker`、`GetResolvedSymbolWorker`、`GetTypeFromTypeNodeWorker`、`GetTypeOfSymbolWorker`、
//   `GetTypeAtPositionWorker`、`GetUnionTypeWorker`、`GetWidenedLiteralTypeWorker`、`GetReturnTypeOfSignatureWorker`、
//   `GetPropertiesOfTypeWorker`、`GetPropertyOfTypeWorker`、`GetTypeOfPropertyOfTypeWorker`、
//   `GetTypeOfPropertyOfContextualTypeWorker`、`GetTypeArgumentsWorker`、`GetBaseConstraintOfTypeWorker`、
//   `IsTypeAssignableToWorker`、`IsArrayLikeTypeWorker`、`IsTupleTypeWorker`、`GetContextualTypeForJsxAttributeWorker`、
//   包级 `getPropertyNameFromType`→`GetPropertyNameFromTypeWorker`、`isTypeUsableAsPropertyName`→`IsTypeUsableAsPropertyNameWorker`。
// - Go 的 `switch { case cond: }`（表达式条件 switch）→ `if` + `return` 链，分支顺序保持；Go 的多值 `case a, b:` →
//   C# 连续 `case a: case b:`；`switch …Kind` 保持 `switch`；C# 的 switch 各 section 共用作用域，故 Go 在不同 case
//   里重用的局部名要改名（`symbol`→`thisSymbol`）或包 `{}`（Capitalize/Uncapitalize 各包一层）。
// - Go 的多返回值 `applyTemplateStringMapping` → `out` 参数（§5.4：纯多值改 out）；`getSpreadIndices` 由 C8Decorators
//   片提供，形状 `GetSpreadIndices(Node, out int, out int)`（ChkC8 确认）。
// - Go 的 map 读（缺键 = 零值）→ `TryGetValue`/`ContainsKey`/`GetValueOrDefault`（`Dictionary` 索引器缺键抛异常）：
//   `_stringMappingTypes.TryGetValue`、`_patternForType.ContainsKey`、`IntrinsicTypeKinds.GetValueOrDefault`
//   （缺键得 `IntrinsicTypeKind.Unknown`(0)，与 Go 的零值一致）。
// - Go 把实例方法当函数值传给 core/切片工具 → C# 统一写 lambda（`x => Xxx(x)`；Main 已澄清方法组亦可，行为一致）。
// - `slices.Index(a, x)` → `Array.IndexOf(a ?? [], x)`（Go 的 nil 切片返回 -1，而 `Array.IndexOf(null, …)` 会抛）；
//   `slices.Clone(s)` → `(T[])s.Clone()`；`strconv.Itoa(n)` → `n.ToString(CultureInfo.InvariantCulture)`；
//   `min(a, b)` → `Math.Min(a, b)`；`core.*` → `using static TypeScript.Core.Core` 后的裸名；`len(nil 切片)` → `?.Length ?? 0`。
// - `stringutil.DecodeJSStringRune` 不移植（docs/architecture.md §3.1）：C# 字符串是 UTF-16，`ApplyStringMapping` 的
//   Capitalize/Uncapitalize 用"首个码点的代码单元长度"（高代理 + 低代理 = 2，否则非空 = 1，空串 = 0）替代 Go 的
//   UTF-8 字节长度 size，语义与 `str[:size] + str[size:]` 一致。
// - Go 的 `t.data.(*X)` → `t.AsX()!`（§5.1）；`arg.AsSyntheticExpression().Type.(*Type)` →
//   `(Type)arg.AsSyntheticExpression()!.Type!`（生成的 `SyntheticExpression.Type` 是 `object?`，对应 Go 的 `Type any`）；
//   Go 的 `binary.AsNode()` 就是 `binary`（`BinaryExpression` 本身是 `Node`）。
// - `args := slices.Clip(args[:spreadIndex])` + `append` → `List<Node>`（§5.4：局部 append 用 List）；Go 的内层
//   `for i, t := range c.getElementTypes(...)` 遮蔽外层 `i`（Go 允许），C# 不允许（CS0136）→ 内层改 `j`。
// - `getEffectiveCallArguments` Go 在 JSX 无属性分支返回 nil 切片 → C# 返回 `[]`（唯一的调用点是 `slices.Index`/`len`，
//   两者对 nil 与空切片等价；`_args` 字段与已落地的 `GetEffectiveCallArguments(iife).Length` 都要求非空 `Node[]`）。
// - C# 的 `GetResolvedSignatureWorker` 第三参是 `ref Signature[]?`（C3.Calls.cs:142）→ Go 的 `nil` 实参写成局部
//   `Signature[]? candidatesOutArray = null;` 再 `ref` 传入（C3.Calls.cs:39 同款）。
// - Go 里可能为 nil 却被直接解引用的指针（Go 侧即 panic）→ C# 在同一点按不变量写 `!`。
// - 跨片依赖（不在本文件实现，按 Go 名 PascalCase 调用；见报告 missingDeps）：`NewStringMappingType`、
//   `GetTypeParameterFromMappedType`、`GetTemplateTypeFromMappedType`、`GetModifiersTypeFromMappedType`、
//   `GetMappedTypeOptionality`、`GetCombinedMappedTypeOptionality`、`InstantiateType`、`IsGenericType`、
//   `AddOptionalityEx`、`GetApplicableIndexInfoForName`、`GetTypeOfPropertyOfTypeWorker`、`GetLiteralTypeFromProperty`、
//   `GetStringLiteralValue`、`GetTemplateLiteralType`、`GetStringLiteralType`、`GetNumberLiteralType`、`GetSpreadIndices`、
//   `GetEffectiveDecoratorArguments`、`GetDecoratorCallSignature`、`CreateSyntheticExpression`、`IsConstTypeVariable`、
//   `CreateArrayType`/`CreateArrayTypeEx`/`CreateTupleTypeEx`、`GetElementTypes`、
//   `GetTypeReferenceArity`、`GetElementTypeOfSliceOfTupleType`、`RemoveMissingType`、`GetIndexedAccessTypeEx`、
//   `CheckExpression`/`CheckExpressionCached`/`CheckExpressionWithContextualType`、`CheckDeclarationInitializer`、
//   `GetContextualSignature`、`GetRestTypeAtPosition`、`TryGetTypeAtPosition`、`CheckTypeAssignableTo`、
//   `GetRegularTypeOfLiteralType`、`MaybeTypeOfKind`、`IsMutableArrayLikeType`、`GetApparentTypeOfContextualType`、
//   `GetTypeOfPropertyOfContextualTypeEx`、`HasBindableName`、`GetLiteralTypeFromPropertyName`、`GetSymbolOfDeclaration`、
//   `GetTypeOfExpression`、`GetThisContainer`、`GetOrCreateTypeFromSignature`、`GetExportSymbolOfValueSymbolIfExported`、
//   `IsResolvingReturnTypeOfSignature`、`GetReturnTypeFromAnnotation`、`IsContextSensitiveFunctionLikeDeclaration`、
//   `GetEffectiveFirstArgumentForJsxSignature`、`GetIteratedTypeOrElementType`、`CheckIteratedTypeOrElementType`、
//   `GetIterationTypeOfGeneratorFunctionReturnType`、`GetIterationTypesOfGeneratorFunctionReturnType`、
//   `CreateGeneratorType`、`GetAwaitedTypeNoAlias`、`GetAwaitedTypeOfPromise`、`CreatePromiseLikeType`、
//   `GetContextualTypeForJsxExpression`、`GetContextualJsxElementAttributesType`、`TryGetTypeFromTypeNode`、
//   字段 `_getGlobalImportCallOptionsType`/`_getGlobalImportAttributesType`/`_getGlobalTemplateStringsArrayType`、
//   `TypeScript.Binder.Binder.GetSymbolNameForPrivateIdentifier`、`EvaluatorFunctions.AnyToString`。
//
// 已按 Main 的自查要点核对：本文件不出现 `x.Symbol()`/`x.SubtreeFacts()`/`x.ModifierFlags()`；
// 所有 `x.Foo()` 都已 grep 过 Ast/Checker 侧是方法还是属性（`Node.Name()`/`Type()`/`Text()`/`Elements()`/`Parameters()`/
// `Arguments()`/`Attributes()`/`Children()`/`Initializer()`/`Expression()`/`PropertyName()`/`PropertyNameOrName()` 是方法；
// `Node.Flags`/`Node.Kind`/`Symbol.Flags`/`Symbol.Name`/`Symbol.ValueDeclaration`/`BinaryExpression.Left|Right|Type|Symbol`/
// `SyntheticExpression.Type|IsSpread|TupleNameSource`/`YieldExpression.AsteriskToken`/`LiteralType._value`/
// `IntrinsicType._intrinsicName`/`TemplateLiteralType._texts|_types`/`Type._flags|_symbol` 是字段或属性）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Evaluator.EvaluatorFunctions;
using static TypeScript.StringUtil.StringUtil;

public sealed partial class Checker
{
    internal string GetTemplateStringForType(Type t)
    {
        if ((t._flags & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral | TypeFlags.BooleanLiteral | TypeFlags.BigIntLiteral)) != 0)
        {
            return AnyToString(t.AsLiteralType()!._value);
        }

        if ((t._flags & TypeFlags.Nullable) != 0)
        {
            return t.AsIntrinsicType()!._intrinsicName;
        }

        return "";
    }

    internal Type GetStringMappingType(Symbol symbol, Type t)
    {
        if ((t._flags & (TypeFlags.Union | TypeFlags.Never)) != 0)
        {
            return MapType(t, x => GetStringMappingType(symbol, x));
        }

        if ((t._flags & TypeFlags.StringLiteral) != 0)
        {
            return GetStringLiteralType(ApplyStringMapping(symbol, GetStringLiteralValue(t)));
        }

        if ((t._flags & TypeFlags.TemplateLiteral) != 0)
        {
            var templateLiteral = t.AsTemplateLiteralType()!;
            ApplyTemplateStringMapping(symbol, templateLiteral._texts!, templateLiteral._types!, out var newTexts, out var newTypes);
            return GetTemplateLiteralType(newTexts, newTypes);
        }

        if ((t._flags & TypeFlags.StringMapping) != 0 && symbol == t._symbol)
        {
            return t;
        }

        if ((t._flags & (TypeFlags.Any | TypeFlags.String | TypeFlags.StringMapping)) != 0 || IsGenericIndexType(t))
        {
            return GetStringMappingTypeForGenericType(symbol, t);
        }

        if (IsPatternLiteralPlaceholderType(t))
        {
            return GetStringMappingTypeForGenericType(symbol, GetTemplateLiteralType(new[] { "", "" }, new[] { t }));
        }

        return t;
    }

    /// <summary>Go: <c>func applyStringMapping(symbol *ast.Symbol, str string) string</c>（包级函数 → 类内 static）。</summary>
    internal static string ApplyStringMapping(Symbol? symbol, string str)
    {
        switch (IntrinsicTypeKinds.GetValueOrDefault(symbol!.Name))
        {
            case IntrinsicTypeKind.Uppercase:
                return ToUpperJS(str);
            case IntrinsicTypeKind.Lowercase:
                return ToLowerJS(str);
            case IntrinsicTypeKind.Capitalize:
            {
                // Go: _, size := stringutil.DecodeJSStringRune(str) —— C# 侧取首个码点的代码单元长度。
                var size = str.Length >= 2 && char.IsHighSurrogate(str[0]) && char.IsLowSurrogate(str[1]) ? 2 : (str.Length != 0 ? 1 : 0);
                return ToUpperJS(str[..size]) + str[size..];
            }
            case IntrinsicTypeKind.Uncapitalize:
            {
                var size = str.Length >= 2 && char.IsHighSurrogate(str[0]) && char.IsLowSurrogate(str[1]) ? 2 : (str.Length != 0 ? 1 : 0);
                return ToLowerJS(str[..size]) + str[size..];
            }
        }

        return str;
    }

    /// <summary>Go: <c>func (c *Checker) applyTemplateStringMapping(...) ([]string, []*Type)</c>（两个返回值 → <c>out</c>）。</summary>
    internal void ApplyTemplateStringMapping(Symbol symbol, string[] texts, Type[] types, out string[] newTexts, out Type[] newTypes)
    {
        switch (IntrinsicTypeKinds.GetValueOrDefault(symbol.Name))
        {
            case IntrinsicTypeKind.Uppercase:
            case IntrinsicTypeKind.Lowercase:
                newTexts = Map(texts, s => ApplyStringMapping(symbol, s));
                newTypes = Map(types, x => GetStringMappingType(symbol, x));
                return;
            case IntrinsicTypeKind.Capitalize:
            case IntrinsicTypeKind.Uncapitalize:
                if (texts[0] != "")
                {
                    var clonedTexts = (string[])texts.Clone();
                    clonedTexts[0] = ApplyStringMapping(symbol, clonedTexts[0]);
                    newTexts = clonedTexts;
                    newTypes = types;
                    return;
                }

                var clonedTypes = (Type[])types.Clone();
                clonedTypes[0] = GetStringMappingType(symbol, clonedTypes[0]);
                newTexts = texts;
                newTypes = clonedTypes;
                return;
        }

        newTexts = texts;
        newTypes = types;
    }

    internal Type GetStringMappingTypeForGenericType(Symbol symbol, Type t)
    {
        var key = new StringMappingKey(symbol, t);
        // Go: result := c.stringMappingTypes[key]（缺键 = nil）
        if (!_stringMappingTypes.TryGetValue(key, out var result))
        {
            result = NewStringMappingType(symbol, t);
            _stringMappingTypes[key] = result;
        }

        return result;
    }

    // Given an indexed access on a mapped type of the form { [P in K]: E }[X], return an instantiation of E where P is
    // replaced with X. Since this simplification doesn't account for mapped type modifiers, add 'undefined' to the
    // resulting type if the mapped type includes a '?' modifier or if the modifiers type indicates that some properties
    // are optional. If the modifiers type is generic, conservatively estimate optionality by recursively looking for
    // mapped types that include '?' modifiers.
    internal Type SubstituteIndexedMappedType(Type objectType, Type index)
    {
        var mapper = new SimpleTypeMapper(GetTypeParameterFromMappedType(objectType), index);
        var templateMapper = CombineTypeMappers(objectType.AsMappedType()!._mapper, mapper);
        var instantiatedTemplateType = InstantiateType(GetTemplateTypeFromMappedType(OrElse(objectType.AsMappedType()!._target, objectType)!), templateMapper);
        var isOptional = GetMappedTypeOptionality(objectType) > 0;
        if (!isOptional)
        {
            if (IsGenericType(objectType))
            {
                isOptional = GetCombinedMappedTypeOptionality(GetModifiersTypeFromMappedType(objectType)) > 0;
            }
            else
            {
                isOptional = CouldAccessOptionalProperty(objectType, index);
            }
        }

        return AddOptionalityEx(instantiatedTemplateType, true /*isProperty*/, isOptional);
    }

    // Return true if an indexed access with the given object and index types could access an optional property.
    internal bool CouldAccessOptionalProperty(Type objectType, Type indexType)
    {
        var indexConstraint = GetBaseConstraintOfTypeWorker(indexType);
        return indexConstraint != null && Some(GetPropertiesOfTypeWorker(objectType), p =>
            (p.Flags & SymbolFlags.Optional) != 0 &&
            IsTypeAssignableToWorker(GetLiteralTypeFromProperty(p, TypeFlags.StringOrNumberLiteralOrUnique, false), indexConstraint));
    }

    internal Type? GetTypeOfPropertyOrIndexSignatureOfType(Type t, string name)
    {
        var propType = GetTypeOfPropertyOfTypeWorker(t, name);
        if (propType != null)
        {
            return propType;
        }

        var indexInfo = GetApplicableIndexInfoForName(t, name);
        if (indexInfo != null)
        {
            return AddOptionalityEx(indexInfo._valueType!, true /*isProperty*/, true /*isOptional*/);
        }

        return null;
    }

    /**
     * Whoa! Do you really want to use this function?
     *
     * Unless you're trying to get the *non-apparent* type for a
     * value-literal type or you're authoring relevant portions of this algorithm,
     * you probably meant to use 'getApparentTypeOfContextualType'.
     * Otherwise this may not be very useful.
     *
     * In cases where you *are* working on this function, you should understand
     * when it is appropriate to use 'getContextualType' and 'getApparentTypeOfContextualType'.
     *
     *   - Use 'getContextualType' when you are simply going to propagate the result to the expression.
     *   - Use 'getApparentTypeOfContextualType' when you're going to need the members of the type.
     *
     * @param node the expression whose contextual type will be returned.
     * @returns the contextual type of an expression.
     */
    internal Type? GetContextualTypeWorker(Node node, ContextFlags contextFlags)
    {
        if ((node.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return null;
        }

        // Cached contextual types are obtained with no ContextFlags, so we can only consult them for
        // requests with no ContextFlags.
        var index = FindContextualNode(node, contextFlags == ContextFlags.None /*includeCaches*/);
        if (index >= 0)
        {
            return _contextualInfos[index]._t;
        }

        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.BindingElement:
                return GetContextualTypeForInitializerExpression(node, contextFlags);
            case Kind.ArrowFunction:
            case Kind.ReturnStatement:
                return GetContextualTypeForReturnExpression(node, contextFlags);
            case Kind.YieldExpression:
                return GetContextualTypeForYieldOperand(parent, contextFlags);
            case Kind.AwaitExpression:
                return GetContextualTypeForAwaitOperand(parent, contextFlags);
            case Kind.CallExpression:
            case Kind.NewExpression:
                return GetContextualTypeForArgument(parent, node);
            case Kind.Decorator:
                return GetContextualTypeForDecorator(parent);
            case Kind.TypeAssertionExpression:
            case Kind.AsExpression:
                if (IsConstAssertion(parent))
                {
                    return GetContextualTypeWorker(parent, contextFlags);
                }

                return GetTypeFromTypeNodeWorker(parent.Type()!);
            case Kind.BinaryExpression:
                return GetContextualTypeForBinaryOperand(node, contextFlags);
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
                return GetContextualTypeForObjectLiteralElementWorker(parent, contextFlags);
            case Kind.SpreadAssignment:
                return GetContextualTypeWorker(parent.Parent!, contextFlags);
            case Kind.ArrayLiteralExpression:
                var t = GetApparentTypeOfContextualType(parent, contextFlags);
                var elementIndex = IndexOfNode(parent.Elements() ?? [], node);
                if (elementIndex < 0)
                {
                    return null;
                }

                GetSpreadIndices(parent, out var firstSpreadIndex, out var lastSpreadIndex);
                return GetContextualTypeForElementExpression(t, elementIndex, parent.Elements()?.Length ?? 0, firstSpreadIndex, lastSpreadIndex);
            case Kind.ConditionalExpression:
                return GetContextualTypeForConditionalOperand(node, contextFlags);
            case Kind.TemplateSpan:
                return GetContextualTypeForSubstitutionExpression(parent.Parent!, node);
            case Kind.ParenthesizedExpression:
                return GetContextualTypeWorker(parent, contextFlags);
            case Kind.NonNullExpression:
                return GetContextualTypeWorker(parent, contextFlags);
            case Kind.SatisfiesExpression:
                return GetTypeFromTypeNodeWorker(parent.Type()!);
            case Kind.ExportAssignment:
                return TryGetTypeFromTypeNode(parent);
            case Kind.JsxExpression:
                return GetContextualTypeForJsxExpression(parent, contextFlags);
            case Kind.JsxAttribute:
            case Kind.JsxSpreadAttribute:
                return GetContextualTypeForJsxAttributeWorker(parent, contextFlags);
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
                return GetContextualJsxElementAttributesType(parent, contextFlags);
            case Kind.ImportAttribute:
                return GetContextualImportAttributeType(parent);
        }

        return null;
    }

    // In a variable, parameter or property declaration with a type annotation,
    // the contextual type of an initializer expression is the type of the variable, parameter or property.
    //
    // Otherwise, in a parameter declaration of a contextually typed function expression,
    // the contextual type of an initializer expression is the contextual type of the parameter.
    //
    // Otherwise, in a variable or parameter declaration with a binding pattern name,
    // the contextual type of an initializer expression is the type implied by the binding pattern.
    //
    // Otherwise, in a binding pattern inside a variable or parameter declaration,
    // the contextual type of an initializer expression is the type annotation of the containing declaration, if present.
    internal Type? GetContextualTypeForInitializerExpression(Node node, ContextFlags contextFlags)
    {
        var declaration = node.Parent!;
        var initializer = declaration.Initializer();
        if (node == initializer)
        {
            var result = GetContextualTypeForVariableLikeDeclaration(declaration, contextFlags);
            if (result != null)
            {
                return result;
            }

            if ((contextFlags & ContextFlags.SkipBindingPatterns) == 0 && IsBindingPattern(declaration.Name()) && (declaration.Name()!.Elements()?.Length ?? 0) > 0)
            {
                return GetTypeFromBindingPattern(declaration.Name()!, true /*includePatternInType*/, false /*reportErrors*/);
            }
        }

        return null;
    }

    internal Type? GetContextualTypeForVariableLikeDeclaration(Node declaration, ContextFlags contextFlags)
    {
        var typeNode = declaration.Type();
        if (typeNode != null)
        {
            return GetTypeFromTypeNodeWorker(typeNode);
        }

        switch (declaration.Kind)
        {
            case Kind.Parameter:
                return GetContextuallyTypedParameterType(declaration);
            case Kind.BindingElement:
                return GetContextualTypeForBindingElement(declaration, contextFlags);
            case Kind.PropertyDeclaration:
                if (IsStatic(declaration))
                {
                    return GetContextualTypeForStaticPropertyDeclaration(declaration, contextFlags);
                }

                break;
        }

        // By default, do nothing and return nil - only the above cases have context implied by a parent
        return null;
    }

    // Return contextual type of parameter or undefined if no contextual type is available
    internal Type? GetContextuallyTypedParameterType(Node parameter)
    {
        var fn = parameter.Parent!;
        if (!IsContextSensitiveFunctionOrObjectLiteralMethod(fn))
        {
            return null;
        }

        var iife = GetImmediatelyInvokedFunctionExpression(fn);
        if (iife != null)
        {
            var args = GetEffectiveCallArguments(iife);
            var indexOfParameter = Array.IndexOf(fn.Parameters() ?? [], parameter);
            if (HasDotDotDotToken(parameter))
            {
                return GetSpreadArgumentType(args, indexOfParameter, args.Length, _anyType, null /*context*/, CheckMode.Normal);
            }

            var links = _signatureLinks.Get(iife);
            var cached = links._resolvedSignature;
            links._resolvedSignature = _anySignature;
            Type? t;
            if (indexOfParameter < args.Length)
            {
                t = GetWidenedLiteralTypeWorker(CheckExpression(args[indexOfParameter]));
            }
            else if (parameter.Initializer() != null)
            {
                t = null;
            }
            else
            {
                t = _undefinedWideningType;
            }

            links._resolvedSignature = cached;
            return t;
        }

        var contextualSignature = GetContextualSignature(fn);
        if (contextualSignature != null)
        {
            var index = Array.IndexOf(fn.Parameters() ?? [], parameter) - IfElse(GetThisParameter(fn) != null, 1, 0);
            if (HasDotDotDotToken(parameter) && LastOrNil(fn.Parameters()) == parameter)
            {
                return GetRestTypeAtPosition(contextualSignature, index, false);
            }

            return TryGetTypeAtPosition(contextualSignature, index);
        }

        return null;
    }

    internal bool IsContextSensitiveFunctionOrObjectLiteralMethod(Node fn)
    {
        return (IsFunctionExpressionOrArrowFunction(fn) || IsObjectLiteralMethod(fn)) && IsContextSensitiveFunctionLikeDeclaration(fn);
    }

    internal Type GetSpreadArgumentType(Node[] args, int index, int argCount, Type restType, InferenceContext? context, CheckMode checkMode)
    {
        var inConstContext = IsConstTypeVariable(restType, 0);
        if (argCount > 0 && index >= argCount - 1)
        {
            var arg = args[argCount - 1];
            if (IsSpreadArgument(arg))
            {
                // We are inferring from a spread expression in the last argument position, i.e. both the parameter
                // and the argument are ...x forms.
                Type? spreadType;
                if (IsSyntheticExpression(arg))
                {
                    spreadType = (Type)arg.AsSyntheticExpression()!.Type!;
                }
                else
                {
                    spreadType = CheckExpressionWithContextualType(arg.Expression()!, restType, context, checkMode);
                }

                if (IsArrayLikeTypeWorker(spreadType!))
                {
                    return GetMutableArrayOrTupleType(spreadType!);
                }

                if (IsSpreadElement(arg))
                {
                    arg = arg.Expression()!;
                }

                return CreateArrayTypeEx(CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType!, _undefinedType, arg), inConstContext);
            }
        }

        var types = new List<Type>();
        var infos = new List<TupleElementInfo>();
        for (var i = index; i < argCount; i++)
        {
            var arg = args[i];
            Type t;
            var info = new TupleElementInfo();
            if (IsSpreadArgument(arg))
            {
                Type? spreadType;
                if (IsSyntheticExpression(arg))
                {
                    spreadType = (Type)arg.AsSyntheticExpression()!.Type!;
                }
                else
                {
                    spreadType = CheckExpression(arg.Expression()!);
                }

                if (IsArrayLikeTypeWorker(spreadType!))
                {
                    t = spreadType!;
                    info._flags = ElementFlags.Variadic;
                }
                else
                {
                    if (IsSpreadElement(arg))
                    {
                        t = CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType!, _undefinedType, arg.Expression()!);
                    }
                    else
                    {
                        t = CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType!, _undefinedType, arg);
                    }

                    info._flags = ElementFlags.Rest;
                }
            }
            else
            {
                Type contextualType;
                if (IsTupleTypeWorker(restType))
                {
                    contextualType = OrElse(GetContextualTypeForElementExpression(restType, i - index, argCount - index, -1, -1), _unknownType)!;
                }
                else
                {
                    contextualType = GetIndexedAccessTypeEx(restType, GetNumberLiteralType(new Number(i - index)), AccessFlags.Contextual, null, null);
                }

                var argType = CheckExpressionWithContextualType(arg, contextualType, context, checkMode);
                var hasPrimitiveContextualType = inConstContext || MaybeTypeOfKind(contextualType, TypeFlags.Primitive | TypeFlags.Index | TypeFlags.TemplateLiteral | TypeFlags.StringMapping);
                if (hasPrimitiveContextualType)
                {
                    t = GetRegularTypeOfLiteralType(argType);
                }
                else
                {
                    t = GetWidenedLiteralTypeWorker(argType);
                }

                info._flags = ElementFlags.Required;
            }

            if (IsSyntheticExpression(arg) && arg.AsSyntheticExpression()!.TupleNameSource != null)
            {
                info._labeledDeclaration = arg.AsSyntheticExpression()!.TupleNameSource;
            }

            types.Add(t);
            infos.Add(info);
        }

        return CreateTupleTypeEx(types.ToArray(), infos.ToArray(), inConstContext && !SomeType(restType, x => IsMutableArrayLikeType(x)));
    }

    internal Type GetMutableArrayOrTupleType(Type t)
    {
        if ((t._flags & TypeFlags.Union) != 0)
        {
            return MapType(t, x => GetMutableArrayOrTupleType(x));
        }

        if ((t._flags & TypeFlags.Any) != 0 || IsMutableArrayOrTuple(GetBaseConstraintOrType(t)))
        {
            return t;
        }

        if (IsTupleTypeWorker(t))
        {
            return CreateTupleTypeEx(GetElementTypes(t), t.TargetTupleType()!._elementInfos!, false /*readonly*/);
        }

        return CreateTupleTypeEx(new[] { t }, new[] { new TupleElementInfo { _flags = ElementFlags.Variadic } }, false);
    }

    internal Type? GetContextualTypeForBindingElement(Node declaration, ContextFlags contextFlags)
    {
        var name = declaration.PropertyNameOrName()!;
        if (IsBindingPattern(name) || IsComputedNonLiteralName(name))
        {
            return null;
        }

        var parent = declaration.Parent!.Parent!;
        var parentType = GetContextualTypeForVariableLikeDeclaration(parent, contextFlags);
        if (parentType == null)
        {
            if (!IsBindingElement(parent) && parent.Initializer() != null)
            {
                parentType = CheckDeclarationInitializer(parent, IfElse(HasDotDotDotToken(declaration), CheckMode.RestBindingElement, CheckMode.Normal), null);
            }
        }

        if (parentType == null)
        {
            return null;
        }

        if (IsArrayBindingPattern(parent.Name()!))
        {
            var index = Array.IndexOf(declaration.Parent!.Elements() ?? [], declaration);
            if (index < 0)
            {
                return null;
            }

            return GetContextualTypeForElementExpression(parentType, index, -1, -1, -1);
        }

        var nameType = GetLiteralTypeFromPropertyName(name);
        if (IsTypeUsableAsPropertyNameWorker(nameType))
        {
            return GetTypeOfPropertyOfTypeWorker(parentType, GetPropertyNameFromTypeWorker(nameType));
        }

        return null;
    }

    internal Type? GetContextualTypeForStaticPropertyDeclaration(Node declaration, ContextFlags contextFlags)
    {
        if (IsExpression(declaration.Parent))
        {
            var parentType = GetContextualTypeWorker(declaration.Parent!, contextFlags);
            if (parentType != null)
            {
                return GetTypeOfPropertyOfContextualTypeWorker(parentType, GetSymbolOfDeclaration(declaration)!.Name);
            }
        }

        return null;
    }

    internal Type? GetContextualTypeForReturnExpression(Node node, ContextFlags contextFlags)
    {
        var fn = GetContainingFunction(node);
        if (fn != null)
        {
            var contextualReturnType = GetContextualReturnType(fn, contextFlags);
            if (contextualReturnType != null)
            {
                var functionFlags = GetFunctionFlags(fn);
                if ((functionFlags & FunctionFlags.Generator) != 0)
                {
                    var isAsyncGenerator = (functionFlags & FunctionFlags.Async) != 0;
                    if ((contextualReturnType._flags & TypeFlags.Union) != 0)
                    {
                        contextualReturnType = FilterType(contextualReturnType, t =>
                            GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, t, isAsyncGenerator) != null);
                    }

                    var iterationReturnType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, contextualReturnType, (functionFlags & FunctionFlags.Async) != 0);
                    if (iterationReturnType == null)
                    {
                        return null;
                    }

                    contextualReturnType = iterationReturnType;
                    // falls through to unwrap Promise for AsyncGenerators
                }

                if ((functionFlags & FunctionFlags.Async) != 0)
                {
                    // Get the awaited type without the `Awaited<T>` alias
                    var contextualAwaitedType = MapType(contextualReturnType, t => GetAwaitedTypeNoAlias(t)!);
                    if (contextualAwaitedType == null)
                    {
                        return null;
                    }

                    return GetUnionTypeWorker(new[] { contextualAwaitedType, CreatePromiseLikeType(contextualAwaitedType) });
                }

                // Regular function or Generator function
                return contextualReturnType;
            }
        }

        return null;
    }

    internal Type? GetContextualIterationType(IterationTypeKind kind, Node functionDecl)
    {
        var isAsync = (GetFunctionFlags(functionDecl) & FunctionFlags.Async) != 0;
        var contextualReturnType = GetContextualReturnType(functionDecl, ContextFlags.None);
        if (contextualReturnType != null)
        {
            return GetIterationTypeOfGeneratorFunctionReturnType(kind, contextualReturnType, isAsync);
        }

        return null;
    }

    internal Type? GetContextualReturnType(Node functionDecl, ContextFlags contextFlags)
    {
        // If the containing function has a return type annotation, is a constructor, or is a get accessor whose
        // corresponding set accessor has a type annotation, return statements in the function are contextually typed
        var returnType = GetReturnTypeFromAnnotation(functionDecl);
        if (returnType != null)
        {
            return returnType;
        }

        // Otherwise, if the containing function is contextually typed by a function type with exactly one call signature
        // and that call signature is non-generic, return statements are contextually typed by the return type of the signature
        var signature = GetContextualSignatureForFunctionLikeDeclaration(functionDecl);
        if (signature != null && !IsResolvingReturnTypeOfSignature(signature))
        {
            // Go 在同名块里重新声明 returnType（Go 允许遮蔽），C# 不允许 → 改名 signatureReturnType。
            var signatureReturnType = GetReturnTypeOfSignatureWorker(signature);
            var functionFlags = GetFunctionFlags(functionDecl);
            if ((functionFlags & FunctionFlags.Generator) != 0)
            {
                return FilterType(signatureReturnType, t =>
                    (t._flags & (TypeFlags.AnyOrUnknown | TypeFlags.Void | TypeFlags.InstantiableNonPrimitive)) != 0 ||
                    CheckGeneratorInstantiationAssignabilityToReturnType(t, functionFlags, null /*errorNode*/));
            }

            if ((functionFlags & FunctionFlags.Async) != 0)
            {
                return FilterType(signatureReturnType, t =>
                    (t._flags & (TypeFlags.AnyOrUnknown | TypeFlags.Void | TypeFlags.InstantiableNonPrimitive)) != 0 ||
                    GetAwaitedTypeOfPromise(t) != null);
            }

            return signatureReturnType;
        }

        var iife = GetImmediatelyInvokedFunctionExpression(functionDecl);
        if (iife != null)
        {
            return GetContextualTypeWorker(iife, contextFlags);
        }

        return null;
    }

    internal bool CheckGeneratorInstantiationAssignabilityToReturnType(Type returnType, FunctionFlags functionFlags, Node? errorNode)
    {
        // Naively, one could check that Generator<any, any, any> is assignable to the return type annotation.
        // However, that would not catch the error in the following case.
        //
        //    interface BadGenerator extends Iterable<number>, Iterator<string> { }
        //    function* g(): BadGenerator { } // Iterable and Iterator have different types!
        //
        var generatorYieldType = OrElse(GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Yield, returnType, (functionFlags & FunctionFlags.Async) != 0), _anyType)!;
        var generatorReturnType = OrElse(GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, returnType, (functionFlags & FunctionFlags.Async) != 0), generatorYieldType)!;
        var generatorNextType = OrElse(GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Next, returnType, (functionFlags & FunctionFlags.Async) != 0), _unknownType)!;
        var generatorInstantiation = CreateGeneratorType(generatorYieldType, generatorReturnType, generatorNextType, (functionFlags & FunctionFlags.Async) != 0);
        return CheckTypeAssignableTo(generatorInstantiation, returnType, errorNode, null);
    }

    internal Signature? GetContextualSignatureForFunctionLikeDeclaration(Node node)
    {
        // Only function expressions, arrow functions, and object literal methods are contextually typed.
        if (IsFunctionExpressionOrArrowFunction(node) || IsObjectLiteralMethod(node))
        {
            return GetContextualSignature(node);
        }

        return null;
    }

    internal Type? GetContextualTypeForYieldOperand(Node node, ContextFlags contextFlags)
    {
        var fn = GetContainingFunction(node);
        if (fn != null)
        {
            var functionFlags = GetFunctionFlags(fn);
            var contextualReturnType = GetContextualReturnType(fn, contextFlags);
            if (contextualReturnType != null)
            {
                var isAsyncGenerator = (functionFlags & FunctionFlags.Async) != 0;
                var isYieldStar = node.AsYieldExpression()!.AsteriskToken != null;
                if (!isYieldStar && (contextualReturnType._flags & TypeFlags.Union) != 0)
                {
                    contextualReturnType = FilterType(contextualReturnType, t =>
                        GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, t, isAsyncGenerator) != null);
                }

                if (isYieldStar)
                {
                    var iterationTypes = GetIterationTypesOfGeneratorFunctionReturnType(contextualReturnType, isAsyncGenerator);
                    var yieldType = OrElse(iterationTypes._yieldType, _silentNeverType)!;
                    var returnType = OrElse(GetContextualTypeWorker(node, contextFlags), _silentNeverType)!;
                    var nextType = OrElse(iterationTypes._nextType, _unknownType)!;
                    var generatorType = CreateGeneratorType(yieldType, returnType, nextType, false /*isAsyncGenerator*/);
                    if (isAsyncGenerator)
                    {
                        var asyncGeneratorType = CreateGeneratorType(yieldType, returnType, nextType, true /*isAsyncGenerator*/);
                        return GetUnionTypeWorker(new[] { generatorType, asyncGeneratorType });
                    }

                    return generatorType;
                }

                return GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Yield, contextualReturnType, isAsyncGenerator);
            }
        }

        return null;
    }

    internal Type? GetContextualTypeForAwaitOperand(Node node, ContextFlags contextFlags)
    {
        var contextualType = GetContextualTypeWorker(node, contextFlags);
        if (contextualType != null)
        {
            var contextualAwaitedType = GetAwaitedTypeNoAlias(contextualType);
            if (contextualAwaitedType != null)
            {
                return GetUnionTypeWorker(new[] { contextualAwaitedType, CreatePromiseLikeType(contextualAwaitedType) });
            }
        }

        return null;
    }

    // In a typed function call, an argument or substitution expression is contextually typed by the type of the corresponding parameter.
    internal Type? GetContextualTypeForArgument(Node callTarget, Node arg)
    {
        var args = GetEffectiveCallArguments(callTarget);
        var argIndex = Array.IndexOf(args, arg);
        // -1 for e.g. the expression of a CallExpression, or the tag of a TaggedTemplateExpression
        if (argIndex == -1)
        {
            return null;
        }

        return GetContextualTypeForArgumentAtIndexWorker(callTarget, argIndex);
    }

    internal Type? GetContextualTypeForArgumentAtIndexWorker(Node callTarget, int argIndex)
    {
        if (IsImportCall(callTarget))
        {
            if (argIndex == 0)
            {
                return _stringType;
            }

            if (argIndex == 1)
            {
                return _getGlobalImportCallOptionsType();
            }

            return _anyType;
        }

        // If we're already in the process of resolving the given signature, don't resolve again as
        // that could cause infinite recursion. Instead, return anySignature.
        Signature signature;
        if (_signatureLinks.Get(callTarget)._resolvedSignature == _resolvingSignature)
        {
            signature = _resolvingSignature;
        }
        else
        {
            Signature[]? candidatesOutArray = null;
            signature = GetResolvedSignatureWorker(callTarget, ref candidatesOutArray, CheckMode.Normal);
        }

        if (IsJsxOpeningLikeElement(callTarget) && argIndex == 0)
        {
            return GetEffectiveFirstArgumentForJsxSignature(signature, callTarget);
        }

        var restIndex = (signature._parameters?.Length ?? 0) - 1;
        if (SignatureHasRestParameter(signature) && argIndex >= restIndex)
        {
            return GetIndexedAccessTypeEx(
                GetTypeOfSymbolWorker(signature._parameters![restIndex]),
                GetNumberLiteralType(new Number(argIndex - restIndex)),
                AccessFlags.Contextual,
                null,
                null);
        }

        return GetTypeAtPositionWorker(signature, argIndex);
    }

    internal Type? GetContextualTypeForDecorator(Node decorator)
    {
        var signature = GetDecoratorCallSignature(decorator);
        if (signature != null)
        {
            return GetOrCreateTypeFromSignature(signature);
        }

        return null;
    }

    internal Type? GetContextualTypeForBinaryOperand(Node node, ContextFlags contextFlags)
    {
        var binary = node.Parent!.AsBinaryExpression()!;
        var typeNode = binary.Type;
        if (typeNode != null)
        {
            return GetTypeFromTypeNodeWorker(typeNode);
        }

        switch (binary.OperatorToken!.Kind)
        {
            case Kind.EqualsToken:
            case Kind.AmpersandAmpersandEqualsToken:
            case Kind.BarBarEqualsToken:
            case Kind.QuestionQuestionEqualsToken:
                // In an assignment expression, the right operand is contextually typed by the type of the left operand
                // unless it's an assignment declaration.
                if (node == binary.Right)
                {
                    var target = GetLeftmostExpression(binary.Left!, false);
                    if (!(IsIdentifier(target) && (GetResolvedSymbolWorker(target!).Flags & SymbolFlags.ModuleExports) != 0))
                    {
                        return GetContextualTypeForAssignmentExpression(binary);
                    }
                }

                break;
            case Kind.BarBarToken:
            case Kind.QuestionQuestionToken:
                // When an || expression has a contextual type, the operands are contextually typed by that type, except
                // when that type originates in a binding pattern, the right operand is contextually typed by the type of
                // the left operand. When an || expression has no contextual type, the right operand is contextually typed
                // by the type of the left operand, except for the special case of Javascript declarations of the form
                // `namespace.prop = namespace.prop || {}`.
                var operatorContextualType = GetContextualTypeWorker(binary, contextFlags);
                if (node == binary.Right && (operatorContextualType == null || _patternForType.ContainsKey(operatorContextualType)))
                {
                    return GetTypeOfExpression(binary.Left!);
                }

                return operatorContextualType;
            case Kind.AmpersandAmpersandToken:
            case Kind.CommaToken:
                if (node == binary.Right)
                {
                    return GetContextualTypeWorker(binary, contextFlags);
                }

                break;
        }

        return null;
    }

    internal Type? GetContextualTypeForAssignmentExpression(BinaryExpression binary)
    {
        var left = binary.Left!;
        if (IsAccessExpression(left))
        {
            var expr = left.Expression()!;
            switch (expr.Kind)
            {
                case Kind.Identifier:
                    var symbol = GetExportSymbolOfValueSymbolIfExported(GetResolvedSymbolWorker(expr))!;
                    if ((symbol.Flags & SymbolFlags.ModuleExports) != 0)
                    {
                        // No contextual type for an expression of the form 'module.exports = expr'.
                        return null;
                    }

                    if (binary.Symbol != null)
                    {
                        // We have an assignment declaration (a binary expression with a symbol assigned by the binder) of the form
                        // 'F.id = expr' or 'F[xxx] = expr'. If 'F' is declared as a variable with a type annotation, we can obtain a
                        // contextual type from the annotated type without triggering a circularity. Otherwise, the assignment
                        // declaration has no contextual type.
                        var valueDeclaration = symbol.ValueDeclaration;
                        if (valueDeclaration != null && IsVariableDeclaration(valueDeclaration))
                        {
                            var annotatedTypeNode = valueDeclaration.Type();
                            if (annotatedTypeNode != null)
                            {
                                if (IsPropertyAccessExpression(left))
                                {
                                    return GetTypeOfPropertyOfContextualTypeWorker(GetTypeFromTypeNodeWorker(annotatedTypeNode), left.Name()!.Text());
                                }

                                var nameType = CheckExpressionCached(left.AsElementAccessExpression()!.ArgumentExpression!);
                                if (IsTypeUsableAsPropertyNameWorker(nameType))
                                {
                                    return GetTypeOfPropertyOfContextualTypeEx(GetTypeFromTypeNodeWorker(annotatedTypeNode), GetPropertyNameFromTypeWorker(nameType), nameType);
                                }

                                return GetTypeOfExpression(left);
                            }
                        }

                        return null;
                    }

                    break;
                case Kind.PropertyAccessExpression:
                case Kind.ElementAccessExpression:
                    if (binary.Symbol != null)
                    {
                        return null;
                    }

                    break;
                case Kind.ThisKeyword:
                    // Go 在此 case 里声明了与上面 case 同名的 symbol（Go 的各 case 作用域独立），C# 的 switch 共用作用域 → 改名 thisSymbol。
                    Symbol? thisSymbol = null;
                    var thisType = GetTypeOfExpression(expr);
                    if (IsPropertyAccessExpression(left))
                    {
                        var name = left.Name()!;
                        if (IsPrivateIdentifier(name))
                        {
                            if (thisType._symbol != null)
                            {
                                thisSymbol = GetPropertyOfTypeWorker(thisType, TypeScript.Binder.Binder.GetSymbolNameForPrivateIdentifier(thisType._symbol, name.Text()));
                            }
                        }
                        else
                        {
                            thisSymbol = GetPropertyOfTypeWorker(thisType, name.Text());
                        }
                    }
                    else
                    {
                        var propType = CheckExpressionCached(left.AsElementAccessExpression()!.ArgumentExpression!);
                        if (IsTypeUsableAsPropertyNameWorker(propType))
                        {
                            thisSymbol = GetPropertyOfTypeWorker(thisType, GetPropertyNameFromTypeWorker(propType));
                        }
                    }

                    if (thisSymbol != null)
                    {
                        var d = thisSymbol.ValueDeclaration;
                        if (d != null && (IsPropertyDeclaration(d) || IsPropertySignatureDeclaration(d)) && d.Type() == null && d.Initializer() == null)
                        {
                            // No contextual type for 'this.xxx = expr', where xxx is declared as a property with no type annotation or initializer.
                            return null;
                        }
                    }

                    if (binary.Symbol != null && binary.Symbol.ValueDeclaration != null && binary.Symbol.ValueDeclaration.Type() == null)
                    {
                        // We have an assignment declaration 'this.xxx = expr' with no (synthetic) type annotation
                        if (!IsObjectLiteralMethod(GetThisContainer(expr, false, false)))
                        {
                            return null;
                        }

                        // and now for one single case of object literal methods
                        var thisName = GetElementOrPropertyAccessName(left);
                        if (thisName == null)
                        {
                            return null;
                        }
                        else
                        {
                            // !!! contextual typing for `this` in object literals
                            return null;
                        }
                    }

                    break;
            }
        }

        return GetTypeOfExpression(left);
    }

    internal Type? GetContextualTypeForObjectLiteralElementWorker(Node element, ContextFlags contextFlags)
    {
        var elementTypeNode = element.Type();
        if (elementTypeNode != null && !IsObjectLiteralMethod(element))
        {
            return GetTypeFromTypeNodeWorker(elementTypeNode);
        }

        var objectLiteral = element.Parent!;
        var t = GetApparentTypeOfContextualType(objectLiteral, contextFlags);
        if (t != null)
        {
            if (HasBindableName(element))
            {
                // For a (non-symbol) computed property, there is no reason to look up the name
                // in the type. It will just be "__computed", which does not appear in any
                // SymbolTable.
                var symbol = GetSymbolOfDeclaration(element)!;
                return GetTypeOfPropertyOfContextualTypeEx(t, symbol.Name, _valueSymbolLinks.Get(symbol)._nameType);
            }

            if (HasDynamicName(element))
            {
                var name = GetNameOfDeclaration(element);
                if (name != null && IsComputedPropertyName(name))
                {
                    var exprType = CheckExpression(name.Expression()!);
                    if (IsTypeUsableAsPropertyNameWorker(exprType))
                    {
                        var propType = GetTypeOfPropertyOfContextualTypeWorker(t, GetPropertyNameFromTypeWorker(exprType));
                        if (propType != null)
                        {
                            return propType;
                        }
                    }
                }
            }

            if (element.Name() != null)
            {
                var nameType = GetLiteralTypeFromPropertyName(element.Name()!);
                // We avoid calling getApplicableIndexInfo here because it performs potentially expensive intersection reduction.
                return MapTypeEx(t, x =>
                {
                    var indexInfo = FindApplicableIndexInfo(GetIndexInfosOfStructuredType(x), nameType);
                    if (indexInfo == null)
                    {
                        return null!;
                    }

                    return indexInfo._valueType!;
                }, true /*noReductions*/);
            }
        }

        return null;
    }

    // In an object literal contextually typed by a type T, the contextual type of a property assignment is the type of
    // the matching property in T, if one exists. Otherwise, it is the type of the numeric index signature in T, if one
    // exists. Otherwise, it is the type of the string index signature in T, if one exists.
    internal Type? GetContextualTypeForObjectLiteralMethod(Node node, ContextFlags contextFlags)
    {
        if ((node.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return null;
        }

        return GetContextualTypeForObjectLiteralElementWorker(node, contextFlags);
    }

    internal Type? GetContextualTypeForElementExpression(Type? t, int index, int length, int firstSpreadIndex, int lastSpreadIndex)
    {
        if (t == null)
        {
            return null;
        }

        return MapTypeEx(t, x =>
        {
            if (IsTupleTypeWorker(x))
            {
                // If index is before any spread element and within the fixed part of the contextual tuple type, return
                // the type of the contextual tuple element.
                if ((firstSpreadIndex < 0 || index < firstSpreadIndex) && index < x.TargetTupleType()!._fixedLength)
                {
                    return RemoveMissingType(GetTypeArgumentsWorker(x)[index], (x.TargetTupleType()!._elementInfos![index]._flags & ElementFlags.Optional) != 0);
                }

                // When the length is known and the index is after all spread elements we compute the offset from the element
                // to the end and the number of ending fixed elements in the contextual tuple type.
                var offset = 0;
                if (length >= 0 && (lastSpreadIndex < 0 || index > lastSpreadIndex))
                {
                    offset = length - index;
                }

                var fixedEndLength = 0;
                if (offset > 0 && (x.TargetTupleType()!._combinedFlags & ElementFlags.Variable) != 0)
                {
                    fixedEndLength = GetEndElementCount(x.TargetTupleType()!, ElementFlags.Fixed);
                }

                // If the offset is within the ending fixed part of the contextual tuple type, return the type of the contextual
                // tuple element.
                if (offset > 0 && offset <= fixedEndLength)
                {
                    return GetTypeArgumentsWorker(x)[GetTypeReferenceArity(x) - offset];
                }

                // Return a union of the possible contextual element types with no subtype reduction.
                var tupleIndex = x.TargetTupleType()!._fixedLength;
                if (firstSpreadIndex >= 0)
                {
                    tupleIndex = Math.Min(tupleIndex, firstSpreadIndex);
                }

                var endSkipCount = fixedEndLength;
                if (length >= 0 && lastSpreadIndex >= 0)
                {
                    endSkipCount = Math.Min(fixedEndLength, length - lastSpreadIndex);
                }

                return GetElementTypeOfSliceOfTupleType(x, tupleIndex, endSkipCount, false /*writing*/, true /*noReductions*/)!;
            }

            // If element index is known and a contextual property with that name exists, return it. Otherwise return the
            // iterated or element type of the contextual type.
            if (firstSpreadIndex < 0 || index < firstSpreadIndex)
            {
                var propType = GetTypeOfPropertyOfContextualTypeWorker(x, index.ToString(CultureInfo.InvariantCulture));
                if (propType != null)
                {
                    return propType;
                }
            }

            return GetIteratedTypeOrElementType(IterationUse.Element, x, _undefinedType, null /*errorNode*/, false /*checkAssignability*/)!;
        }, true /*noReductions*/);
    }

    // In a contextually typed conditional expression, the true/false expressions are contextually typed by the same type.
    internal Type? GetContextualTypeForConditionalOperand(Node node, ContextFlags contextFlags)
    {
        var conditional = node.Parent!.AsConditionalExpression()!;
        if (node == conditional.WhenTrue || node == conditional.WhenFalse)
        {
            return GetContextualTypeWorker(node.Parent!, contextFlags);
        }

        return null;
    }

    internal Type? GetContextualTypeForSubstitutionExpression(Node template, Node substitutionExpression)
    {
        if (IsTaggedTemplateExpression(template.Parent!))
        {
            return GetContextualTypeForArgument(template.Parent!, substitutionExpression);
        }

        return null;
    }

    internal Type? GetContextualImportAttributeType(Node node)
    {
        return GetTypeOfPropertyOfContextualTypeWorker(_getGlobalImportAttributesType(), node.Name()!.Text());
    }

    // Returns the effective arguments for an expression that works like a function invocation.
    internal Node[] GetEffectiveCallArguments(Node node)
    {
        if (IsJsxOpeningFragment(node))
        {
            // This attributes Type does not include a children property yet, the same way a fragment created with <React.Fragment> does not at this stage
            return new[] { CreateSyntheticExpression(node, _emptyFreshJsxObjectType, false, null)! };
        }

        if (IsTaggedTemplateExpression(node))
        {
            var template = node.AsTaggedTemplateExpression()!.Template!;
            var firstArg = CreateSyntheticExpression(template, _getGlobalTemplateStringsArrayType(), false, null)!;
            if (!IsTemplateExpression(template))
            {
                return new[] { firstArg };
            }

            var spans = template.AsTemplateExpression()!.TemplateSpans!.Nodes ?? [];
            var args = new Node[spans.Length + 1];
            args[0] = firstArg;
            // Go: for i, span := range spans —— C# 的方法体作用域不允许与下面 nodeArgs 循环的 i 重名（CS0136），故改名 spanIndex。
            for (var spanIndex = 0; spanIndex < spans.Length; spanIndex++)
            {
                var span = spans[spanIndex];
                args[spanIndex + 1] = span.Expression()!;
            }

            return args;
        }

        if (IsDecorator(node))
        {
            return GetEffectiveDecoratorArguments(node);
        }

        if (IsBinaryExpression(node))
        {
            // Handles instanceof operator
            return new[] { node.AsBinaryExpression()!.Left! };
        }

        if (IsJsxOpeningLikeElement(node))
        {
            if ((node.Attributes()!.Properties()?.Length ?? 0) != 0 || (IsJsxOpeningElement(node) && (node.Parent!.Children()?.Nodes?.Length ?? 0) != 0))
            {
                return new[] { node.Attributes()! };
            }

            // Go 返回 nil 切片；C# 统一用空数组（调用点只用 len/索引/遍历，nil 与空等价）。
            return [];
        }

        // Go 的 switch default 分支；局部名 nodeArgs 避开上面 tagged template 分支的 args（C# 方法体同一作用域）。
        var nodeArgs = node.Arguments() ?? [];
        var spreadIndex = GetSpreadArgumentIndex(nodeArgs);
        if (spreadIndex >= 0)
        {
            // Create synthetic arguments from spreads of tuple types.
            var effectiveArgs = new List<Node>(nodeArgs[..spreadIndex]);
            for (var i = spreadIndex; i < nodeArgs.Length; i++)
            {
                var arg = nodeArgs[i];
                Type? spreadType = null;
                // We can call checkExpressionCached because spread expressions never have a contextual type.
                if (IsSpreadElement(arg))
                {
                    if (_flowLoopStack.Count != 0)
                    {
                        spreadType = CheckExpression(arg.Expression()!);
                    }
                    else
                    {
                        spreadType = CheckExpressionCached(arg.Expression()!);
                    }
                }

                if (spreadType != null && IsTupleTypeWorker(spreadType))
                {
                    var elementTypes = GetElementTypes(spreadType);
                    var elementInfos = spreadType.TargetTupleType()!._elementInfos!;
                    for (var j = 0; j < elementTypes.Length; j++)
                    {
                        var t = elementTypes[j];
                        var flags = elementInfos[j]._flags;
                        var syntheticType = t;
                        if ((flags & ElementFlags.Rest) != 0)
                        {
                            syntheticType = CreateArrayType(t);
                        }

                        var syntheticArg = CreateSyntheticExpression(arg, syntheticType, (flags & ElementFlags.Variable) != 0, elementInfos[j]._labeledDeclaration)!;
                        effectiveArgs.Add(syntheticArg);
                    }
                }
                else
                {
                    effectiveArgs.Add(arg);
                }
            }

            return effectiveArgs.ToArray();
        }

        return nodeArgs;
    }

    internal int GetSpreadArgumentIndex(Node[]? args)
    {
        return FindIndex(args, a => IsSpreadArgument(a));
    }

    /// <summary>Go: <c>func isSpreadArgument(arg *ast.Node) bool</c>（包级函数 → 类内 static）。</summary>
    internal static bool IsSpreadArgument(Node arg)
    {
        return IsSpreadElement(arg) || (IsSyntheticExpression(arg) && arg.AsSyntheticExpression()!.IsSpread);
    }
}

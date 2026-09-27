// ported from: internal/checker/checker.go:30520-31300
//
// 与 Go 的差异（逐条，一行一条理由；其余逐行对应，含注释与分支顺序）：
// - Go 的多值返回 `getSpreadIndices(node) (int, int)` → C# `out` 参数：
//   `internal void GetSpreadIndices(Node node, out int firstSpreadIndex, out int lastSpreadIndex)`（C8 各分片统一）。
// - Go 的 `panic("...")` → `Debug.Fail("...")` + `throw new System.Diagnostics.UnreachableException()`（Binder 先例）。
// - 未导出字段带 `_`（Main 2026-09-28 终局裁决）：`c.arrayLiteralLinks`→`_arrayLiteralLinks`、`t.flags`→`t._flags`、
//   `t.objectFlags`→`t._objectFlags`、`t.id`→`t._id`、`signature.parameters`→`signature._parameters`、
//   `links.decoratorSignature`→`links._decoratorSignature`、`links.indicesComputed`→`links._indicesComputed`、
//   `indexInfo.valueType`→`indexInfo._valueType`、`inferenceContext.inferences`→`inferenceContext._inferences`。
// - Go 的包级类型 `ObjectLiteralDiscriminator`（+ 3 个方法）移到文件末尾的 namespace 级 `class`：C# 不允许在
//   `partial class Checker` 内声明另一个类型；成员集合与声明顺序无关。它实现 relater.go:1199 的 `Discriminator` 接口（跨片，见报告）。
// - Go 的包级函数（无接收者）→ 同一个 `partial class Checker` 的 static 成员（`ContainsType`/`IsTupleType`/
//   `IsNumericLiteralName`/`HasInferenceCandidatesOrDefault` 按裸名调用；均在其它分片落地，见报告）。
// - 跨片成员按 `artifacts/checker-method-collisions.md` 使用 `…Worker` 改名后的名字（`GetTypeOfSymbolWorker`、
//   `GetPropertiesOfTypeWorker`、`GetPropertyOfTypeWorker`、`GetDeclaredTypeOfSymbolWorker`、`GetApparentTypeWorker`、
//   `GetReducedTypeWorker`、`GetContextualTypeWorker`、`GetUnionTypeWorker`、`GetUnionTypeExWorker`、
//   `GetSignatureFromDeclarationWorker`、`IsTypeAssignableToWorker`）；本窗口内 `getTypeOfPropertyOfContextualType`
//   同样落成 `GetTypeOfPropertyOfContextualTypeWorker`（同片其它文件/其它分片按此名调用）。
// - Go 的无标签 `switch { case <cond>: ... }`（条件 case）→ C# `if / else if / else` 链（分支顺序与语义一致）；
//   Go 的多值 `case a, b:` → C# 连续 `case a: case b:`。
// - C# 不允许 lambda 形参遮蔽外层局部/参数（CS0136），故 Go 里 `func(t *Type) ...` 闭包的形参改名为 `s`，体内引用同步改名。
// - Go 的 map 读（缺键返回零值）→ `dict.GetValueOrDefault(k)`（`Dictionary` 索引器缺键会抛异常）；
//   Go 的 `if x := m[k]; x != nil` → C# `if (m.GetValueOrDefault(k) is { } x)`（`MapTypeEx` 的映射函数可能返回 nil）。
// - Go 的局部/参数 `append` → `Array.Resize` + 写末位（返回值语义与 Go 的 `append` 一致；局部候选列表用 `List<T>`）。
// - Go 的 nil 切片：可能为 nil 的参数声明为 `T[]?` 并按 Go 原样传 nil（`NewSignature` 的 `Type[]?/Symbol[]?`
//   直接存引用、不解引用，故 nil 语义与 Go 一致）；`minArgumentCount` 用 `parameters?.Length ?? 0`（= Go 的 `len(nil)`）。
// - Go 里可能为 nil 的参数/字段（`node.Parent`、`t.AsMappedType()`、Checker 的标量类型字段等）按不变量用 `!`。
// - `memberwise`/类型断言：`t.data.(*X)` → `t.AsX()`（§5.1），`AsX()` 返回可空，断言点上按不变量用 `!`。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;

public sealed partial class Checker
{
    internal Node CreateSyntheticExpression(Node parent, Type t, bool isSpread, Node? tupleNameSource)
    {
        var result = _factory.NewSyntheticExpression(t, isSpread, tupleNameSource);
        result.Loc = parent.Loc;
        result.Parent = parent;
        return result;
    }

    // ported from: internal/checker/checker.go:30527-30545（Go: `getSpreadIndices(node) (int, int)` → out 参数）
    internal void GetSpreadIndices(Node node, out int firstSpreadIndex, out int lastSpreadIndex)
    {
        var links = _arrayLiteralLinks.Get(node);
        if (!links._indicesComputed)
        {
            var first = -1;
            var last = -1;
            var elements = node.Elements()!;
            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                if (IsSpreadElement(element))
                {
                    if (first < 0)
                    {
                        first = i;
                    }
                    last = i;
                }
            }
            links._firstSpreadIndex = first;
            links._lastSpreadIndex = last;
            links._indicesComputed = true;
        }
        firstSpreadIndex = links._firstSpreadIndex;
        lastSpreadIndex = links._lastSpreadIndex;
    }

    // Returns the synthetic argument list for a decorator invocation.
    internal Node[] GetEffectiveDecoratorArguments(Node node)
    {
        var expr = node.Expression()!;
        var signature = GetDecoratorCallSignature(node);
        if (signature != null)
        {
            var parameters = signature._parameters!;
            var args = new Node[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                args[i] = CreateSyntheticExpression(expr, GetTypeOfSymbolWorker(param), false, null);
            }
            return args;
        }
        Debug.Fail("Decorator signature not found");
        throw new System.Diagnostics.UnreachableException();
    }

    internal Signature? GetDecoratorCallSignature(Node decorator)
    {
        if (_legacyDecorators)
        {
            return GetLegacyDecoratorCallSignature(decorator);
        }
        return GetESDecoratorCallSignature(decorator);
    }

    internal Signature? GetLegacyDecoratorCallSignature(Node decorator)
    {
        var node = decorator.Parent!;
        var links = _signatureLinks.Get(node);
        if (links._decoratorSignature == null)
        {
            links._decoratorSignature = _anySignature;
            switch (node.Kind)
            {
                case Kind.ClassDeclaration:
                case Kind.ClassExpression:
                    {
                        // For a class decorator, the `target` is the type of the class (e.g. the
                        // "static" or "constructor" side of the class).
                        var targetType = GetTypeOfSymbolWorker(GetSymbolOfDeclaration(node)!);
                        var targetParam = NewParameter("target", targetType);
                        links._decoratorSignature = NewCallSignature(null, null, new Symbol[] { targetParam }, GetUnionTypeWorker(new Type[] { targetType, _voidType! }));
                        break;
                    }
                case Kind.Parameter:
                    {
                        if (!IsConstructorDeclaration(node.Parent!) && !(IsMethodDeclaration(node.Parent!) || (IsSetAccessorDeclaration(node.Parent!) && IsClassLike(node.Parent!.Parent))))
                        {
                            break;
                        }
                        if (GetThisParameter(node.Parent) == node)
                        {
                            break;
                        }
                        var index = Array.IndexOf(node.Parent!.Parameters()!, node) - IfElse(GetThisParameter(node.Parent) != null, 1, 0);
                        Debug.Assert(index >= 0);
                        // A parameter declaration decorator will have three arguments (see `ParameterDecorator` in
                        // core.d.ts).
                        Type targetType;
                        Type keyType;
                        if (IsConstructorDeclaration(node.Parent!))
                        {
                            targetType = GetTypeOfSymbolWorker(GetSymbolOfDeclaration(node.Parent!.Parent!)!);
                            keyType = _undefinedType!;
                        }
                        else
                        {
                            targetType = GetParentTypeOfClassElement(node.Parent!);
                            keyType = GetClassElementPropertyKeyType(node.Parent!);
                        }
                        var indexType = GetNumberLiteralType((Number)index);
                        var targetParam = NewParameter("target", targetType);
                        var keyParam = NewParameter("propertyKey", keyType);
                        var indexParam = NewParameter("parameterIndex", indexType);
                        links._decoratorSignature = NewCallSignature(null, null, new Symbol[] { targetParam, keyParam, indexParam }, _voidType!);
                        break;
                    }
                case Kind.MethodDeclaration:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                case Kind.PropertyDeclaration:
                    {
                        if (!IsClassLike(node.Parent))
                        {
                            break;
                        }
                        // A method or accessor declaration decorator will have either two or three arguments (see
                        // `PropertyDecorator` and `MethodDecorator` in core.d.ts).
                        var targetType = GetParentTypeOfClassElement(node);
                        var targetParam = NewParameter("target", targetType);
                        var keyType = GetClassElementPropertyKeyType(node);
                        var keyParam = NewParameter("propertyKey", keyType);
                        var returnType = _voidType!;
                        if (!IsPropertyDeclaration(node))
                        {
                            returnType = NewTypedPropertyDescriptorType(GetTypeOfNode(node));
                        }
                        var hasPropDesc = !IsPropertyDeclaration(node) || HasAccessorModifier(node);
                        if (hasPropDesc)
                        {
                            var descriptorType = NewTypedPropertyDescriptorType(GetTypeOfNode(node));
                            var descriptorParam = NewParameter("descriptor", descriptorType);
                            links._decoratorSignature = NewCallSignature(null, null, new Symbol[] { targetParam, keyParam, descriptorParam }, GetUnionTypeWorker(new Type[] { returnType, _voidType! }));
                        }
                        else
                        {
                            links._decoratorSignature = NewCallSignature(null, null, new Symbol[] { targetParam, keyParam }, GetUnionTypeWorker(new Type[] { returnType, _voidType! }));
                        }
                        break;
                    }
            }
        }
        if (links._decoratorSignature == _anySignature)
        {
            return null;
        }
        return links._decoratorSignature;
    }

    internal Signature? GetESDecoratorCallSignature(Node decorator)
    {
        // We are considering a future change that would allow the type of a decorator to affect the type of the
        // class and its members, such as a `@Stringify` decorator changing the type of a `number` field to `string`, or
        // a `@Callable` decorator adding a call signature to a `class`. The type arguments for the various context
        // types may eventually change to reflect such mutations.
        //
        // In some cases we describe such potential mutations as coming from a "prior decorator application". It is
        // important to note that, while decorators are *evaluated* left to right, they are *applied* right to left
        // to preserve f ৹ g -> f(g(x)) application order. In these cases, a "prior" decorator usually means the
        // next decorator following this one in document order.
        //
        // The "original type" of a class or member is the type it was declared as, or the type we infer from
        // initializers, before _any_ decorators are applied.
        //
        // The type of a class or member that is a result of a prior decorator application represents the
        // "current type", i.e., the type for the declaration at the time the decorator is _applied_.
        //
        // The type of a class or member that is the result of the application of *all* relevant decorators is the
        // "final type".
        //
        // Any decorator that allows mutation or replacement will also refer to an "input type" and an
        // "output type". The "input type" corresponds to the "current type" of the declaration, while the
        // "output type" will become either the "input type/current type" for a subsequent decorator application,
        // or the "final type" for the decorated declaration.
        //
        // It is important to understand decorator application order as it relates to how the "current", "input",
        // "output", and "final" types will be determined:
        //
        //  @E2 @E1 class SomeClass {
        //      @A2 @A1 static f() {}
        //      @B2 @B1 g() {}
        //      @C2 @C1 static x;
        //      @D2 @D1 y;
        //  }
        //
        // Per [the specification][1], decorators are applied in the following order:
        //
        // 1. For each static method (incl. get/set methods and `accessor` fields), in document order:
        //    a. Apply each decorator for that method, in reverse order (`A1`, `A2`).
        // 2. For each instance method (incl. get/set methods and `accessor` fields), in document order:
        //    a. Apply each decorator for that method, in reverse order (`B1`, `B2`).
        // 3. For each static field (excl. auto-accessors), in document order:
        //    a. Apply each decorator for that field, in reverse order (`C1`, `C2`).
        // 4. For each instance field (excl. auto-accessors), in document order:
        //    a. Apply each decorator for that field, in reverse order (`D1`, `D2`).
        // 5. Apply each decorator for the class, in reverse order (`E1`, `E2`).
        //
        // As a result, "current" types at each decorator application are as follows:
        // - For `A1`, the "current" types of the class and method are their "original" types.
        // - For `A2`, the "current type" of the method is the "output type" of `A1`, and the "current type" of the
        //   class is the type of `SomeClass` where `f` is the "output type" of `A1`. This becomes the "final type"
        //   of `f`.
        // - For `B1`, the "current type" of the method is its "original type", and the "current type" of the class
        //   is the type of `SomeClass` where `f` now has its "final type".
        // - etc.
        //
        // [1]: https://arai-a.github.io/ecma262-compare/?pr=2417&id=sec-runtime-semantics-classdefinitionevaluation
        //
        // This seems complicated at first glance, but is not unlike our existing inference for functions:
        //
        //  declare function pipe<Original, A1, A2, B1, B2, C1, C2, D1, D2, E1, E2>(
        //      original: Original,
        //      a1: (input: Original, context: Context<E2>) => A1,
        //      a2: (input: A1, context: Context<E2>) => A2,
        //      b1: (input: A2, context: Context<E2>) => B1,
        //      b2: (input: B1, context: Context<E2>) => B2,
        //      c1: (input: B2, context: Context<E2>) => C1,
        //      c2: (input: C1, context: Context<E2>) => C2,
        //      d1: (input: C2, context: Context<E2>) => D1,
        //      d2: (input: D1, context: Context<E2>) => D2,
        //      e1: (input: D2, context: Context<E2>) => E1,
        //      e2: (input: E1, context: Context<E2>) => E2,
        //  ): E2;

        // When a decorator is applied, it is passed two arguments: "target", which is a value representing the
        // thing being decorated (constructors for classes, functions for methods/accessors, `undefined` for fields,
        // and a `{ get, set }` object for auto-accessors), and "context", which is an object that provides
        // reflection information about the decorated element, as well as the ability to add additional "extra"
        // initializers. In most cases, the "target" argument corresponds to the "input type" in some way, and the
        // return value similarly corresponds to the "output type" (though if the "output type" is `void` or
        // `undefined` then the "output type" is the "input type").
        var node = decorator.Parent!;
        var links = _signatureLinks.Get(node);
        if (links._decoratorSignature == null)
        {
            links._decoratorSignature = _anySignature;
            switch (node.Kind)
            {
                case Kind.ClassDeclaration:
                case Kind.ClassExpression:
                    {
                        // Class decorators have a `context` of `ClassDecoratorContext<Class>`, where the `Class` type
                        // argument will be the "final type" of the class after all decorators are applied.
                        var targetType = GetTypeOfSymbolWorker(GetSymbolOfDeclaration(node)!);
                        var contextType = NewClassDecoratorContextType(targetType);
                        links._decoratorSignature = NewESDecoratorCallSignature(targetType, contextType!, targetType);
                        break;
                    }
                case Kind.MethodDeclaration:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                    {
                        if (!IsClassLike(node.Parent))
                        {
                            break;
                        }
                        // Method decorators have a `context` of `ClassMethodDecoratorContext<This, Value>`, where the
                        // `Value` type argument corresponds to the "final type" of the method.
                        //
                        // Getter decorators have a `context` of `ClassGetterDecoratorContext<This, Value>`, where the
                        // `Value` type argument corresponds to the "final type" of the value returned by the getter.
                        //
                        // Setter decorators have a `context` of `ClassSetterDecoratorContext<This, Value>`, where the
                        // `Value` type argument corresponds to the "final type" of the parameter of the setter.
                        //
                        // In all three cases, the `This` type argument is the "final type" of either the class or
                        // instance, depending on whether the member was `static`.
                        Type valueType;
                        if (IsMethodDeclaration(node))
                        {
                            valueType = GetOrCreateTypeFromSignature(GetSignatureFromDeclarationWorker(node));
                        }
                        else
                        {
                            valueType = GetTypeOfNode(node);
                        }
                        Type thisType;
                        if (HasStaticModifier(node))
                        {
                            thisType = GetTypeOfSymbolWorker(GetSymbolOfDeclaration(node.Parent!)!);
                        }
                        else
                        {
                            thisType = GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(node.Parent!)!);
                        }
                        // We wrap the "input type", if necessary, to match the decoration target. For getters this is
                        // something like `() => inputType`, for setters it's `(value: inputType) => void` and for
                        // methods it is just the input type.
                        Type targetType;
                        if (IsGetAccessorDeclaration(node))
                        {
                            targetType = NewGetterFunctionType(valueType);
                        }
                        else if (IsSetAccessorDeclaration(node))
                        {
                            targetType = NewSetterFunctionType(valueType);
                        }
                        else
                        {
                            targetType = valueType;
                        }
                        var contextType = NewClassMemberDecoratorContextTypeForNode(node, thisType, valueType);
                        links._decoratorSignature = NewESDecoratorCallSignature(targetType, contextType, targetType);
                        break;
                    }
                case Kind.PropertyDeclaration:
                    {
                        if (!IsClassLike(node.Parent))
                        {
                            break;
                        }
                        // Field decorators have a `context` of `ClassFieldDecoratorContext<This, Value>` and
                        // auto-accessor decorators have a `context` of `ClassAccessorDecoratorContext<This, Value>. In
                        // both cases, the `This` type argument is the "final type" of either the class or instance,
                        // depending on whether the member was `static`, and the `Value` type argument corresponds to
                        // the "final type" of the value stored in the field.
                        var valueType = GetTypeOfNode(node);
                        Type thisType;
                        if (HasStaticModifier(node))
                        {
                            thisType = GetTypeOfSymbolWorker(GetSymbolOfDeclaration(node.Parent!)!);
                        }
                        else
                        {
                            thisType = GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(node.Parent!)!);
                        }
                        // The `target` of an auto-accessor decorator is a `{ get, set }` object, representing the
                        // runtime-generated getter and setter that are added to the class/prototype. The `target` of a
                        // regular field decorator is always `undefined` as it isn't installed until it is initialized.
                        Type targetType;
                        if (HasAccessorModifier(node))
                        {
                            targetType = NewClassAccessorDecoratorTargetType(thisType, valueType)!;
                        }
                        else
                        {
                            targetType = _undefinedType!;
                        }
                        // We wrap the "output type" depending on the declaration. For auto-accessors, we wrap the
                        // "output type" in a `ClassAccessorDecoratorResult<This, In, Out>` type, which allows for
                        // mutation of the runtime-generated getter and setter, as well as the injection of an
                        // initializer mutator. For regular fields, we wrap the "output type" in an initializer mutator.
                        Type returnType;
                        if (HasAccessorModifier(node))
                        {
                            returnType = NewClassAccessorDecoratorResultType(thisType, valueType)!;
                        }
                        else
                        {
                            returnType = NewClassFieldDecoratorInitializerMutatorType(thisType, valueType);
                        }
                        var contextType = NewClassMemberDecoratorContextTypeForNode(node, thisType, valueType);
                        links._decoratorSignature = NewESDecoratorCallSignature(targetType, contextType, returnType);
                        break;
                    }
            }
        }
        if (links._decoratorSignature == _anySignature)
        {
            return null;
        }
        return links._decoratorSignature;
    }

    internal Type? NewClassDecoratorContextType(Type classType)
    {
        return TryCreateTypeReference(_getGlobalClassDecoratorContextType(), new Type[] { classType });
    }

    internal Type? NewClassMethodDecoratorContextType(Type classType, Type valueType)
    {
        return TryCreateTypeReference(_getGlobalClassMethodDecoratorContextType(), new Type[] { classType, valueType });
    }

    internal Type? NewClassGetterDecoratorContextType(Type classType, Type valueType)
    {
        return TryCreateTypeReference(_getGlobalClassGetterDecoratorContextType(), new Type[] { classType, valueType });
    }

    internal Type? NewClassSetterDecoratorContextType(Type classType, Type valueType)
    {
        return TryCreateTypeReference(_getGlobalClassSetterDecoratorContextType(), new Type[] { classType, valueType });
    }

    internal Type? NewClassAccessorDecoratorContextType(Type thisType, Type valueType)
    {
        return TryCreateTypeReference(_getGlobalClassAccessorDecoratorContextType(), new Type[] { thisType, valueType });
    }

    internal Type? NewClassFieldDecoratorContextType(Type thisType, Type valueType)
    {
        return TryCreateTypeReference(_getGlobalClassFieldDecoratorContextType(), new Type[] { thisType, valueType });
    }

    // Gets a type like `{ name: "foo", private: false, static: true }` that is used to provided member-specific
    // details that will be intersected with a decorator context type.
    internal Type GetClassMemberDecoratorContextOverrideType(Type nameType, bool isPrivate, bool isStatic)
    {
        var kind = IfElse(
            isPrivate,
            IfElse(isStatic, CachedTypeKind.DecoratorContextPrivateStatic, CachedTypeKind.DecoratorContextPrivate),
            IfElse(isStatic, CachedTypeKind.DecoratorContextStatic, CachedTypeKind.DecoratorContext));
        var key = new CachedTypeKey(kind, nameType._id);
        if (_cachedTypes.GetValueOrDefault(key) is { } cachedOverrideType)
        {
            return cachedOverrideType;
        }
        var members = new SymbolTable();
        members["name"] = NewProperty("name", nameType);
        members["private"] = NewProperty("private", IfElse(isPrivate, _trueType!, _falseType!));
        members["static"] = NewProperty("static", IfElse(isStatic, _trueType!, _falseType!));
        var overrideType = NewAnonymousType(null!, members, null, null, null);
        _cachedTypes[key] = overrideType;
        return overrideType;
    }

    internal Type NewClassMemberDecoratorContextTypeForNode(Node node, Type thisType, Type valueType)
    {
        var isStatic = HasStaticModifier(node);
        var isPrivate = IsPrivateIdentifier(node.Name()!);
        Type nameType;
        if (isPrivate)
        {
            nameType = GetStringLiteralType(node.Name()!.Text());
        }
        else
        {
            nameType = GetLiteralTypeFromPropertyName(node.Name()!);
        }
        Type? contextType;
        if (IsMethodDeclaration(node))
        {
            contextType = NewClassMethodDecoratorContextType(thisType, valueType);
        }
        else if (IsGetAccessorDeclaration(node))
        {
            contextType = NewClassGetterDecoratorContextType(thisType, valueType);
        }
        else if (IsSetAccessorDeclaration(node))
        {
            contextType = NewClassSetterDecoratorContextType(thisType, valueType);
        }
        else if (IsAutoAccessorPropertyDeclaration(node))
        {
            contextType = NewClassAccessorDecoratorContextType(thisType, valueType);
        }
        else if (IsPropertyDeclaration(node))
        {
            contextType = NewClassFieldDecoratorContextType(thisType, valueType);
        }
        else
        {
            Debug.Fail("Unhandled case in createClassMemberDecoratorContextTypeForNode");
            throw new System.Diagnostics.UnreachableException();
        }
        var overrideType = GetClassMemberDecoratorContextOverrideType(nameType, isPrivate, isStatic);
        return GetIntersectionType(new Type[] { contextType!, overrideType });
    }

    internal Type? NewClassAccessorDecoratorTargetType(Type thisType, Type valueType)
    {
        return TryCreateTypeReference(_getGlobalClassAccessorDecoratorTargetType(), new Type[] { thisType, valueType });
    }

    internal Type? NewClassAccessorDecoratorResultType(Type thisType, Type valueType)
    {
        return TryCreateTypeReference(_getGlobalClassAccessorDecoratorResultType(), new Type[] { thisType, valueType });
    }

    internal Type NewClassFieldDecoratorInitializerMutatorType(Type thisType, Type valueType)
    {
        var thisParam = NewParameter("this", thisType);
        var valueParam = NewParameter("value", valueType);
        return NewFunctionType(null, thisParam, new Symbol[] { valueParam }, valueType);
    }

    // Creates a call signature for an ES Decorator. This method is used by the semantics of
    // `getESDecoratorCallSignature`, which you should probably be using instead.
    internal Signature NewESDecoratorCallSignature(Type targetType, Type contextType, Type nonOptionalReturnType)
    {
        var targetParam = NewParameter("target", targetType);
        var contextParam = NewParameter("context", contextType);
        var returnType = GetUnionTypeWorker(new Type[] { nonOptionalReturnType, _voidType! });
        return NewCallSignature(null, null /*thisParameter*/, new Symbol[] { targetParam, contextParam }, returnType);
    }

    // Creates a synthetic `FunctionType`
    internal Type NewFunctionType(Type[]? typeParameters, Symbol? thisParameter, Symbol[]? parameters, Type returnType)
    {
        var signature = NewCallSignature(typeParameters, thisParameter, parameters, returnType);
        return GetOrCreateTypeFromSignature(signature);
    }

    internal Type NewGetterFunctionType(Type t)
    {
        return NewFunctionType(null, null /*thisParameter*/, null, t);
    }

    internal Type NewSetterFunctionType(Type t)
    {
        var valueParam = NewParameter("value", t);
        return NewFunctionType(null, null /*thisParameter*/, new Symbol[] { valueParam }, _voidType!);
    }

    // Creates a synthetic `Signature` corresponding to a call signature.
    internal Signature NewCallSignature(Type[]? typeParameters, Symbol? thisParameter, Symbol[]? parameters, Type returnType)
    {
        var decl = _factory.NewFunctionTypeNode(null, null, _factory.NewKeywordTypeNode(Kind.AnyKeyword));
        return NewSignature(SignatureFlags.None, decl, typeParameters, thisParameter, parameters, returnType, null, parameters?.Length ?? 0);
    }

    internal Type NewTypedPropertyDescriptorType(Type propertyType)
    {
        return CreateTypeFromGenericGlobalType(_getGlobalTypedPropertyDescriptorType(), new Type[] { propertyType });
    }

    internal Type GetParentTypeOfClassElement(Node node)
    {
        var classSymbol = GetSymbolOfNode(node.Parent!)!;
        if (IsStatic(node))
        {
            return GetTypeOfSymbolWorker(classSymbol);
        }
        return GetDeclaredTypeOfSymbolWorker(classSymbol);
    }

    internal Type GetClassElementPropertyKeyType(Node element)
    {
        var name = element.Name()!;
        switch (name.Kind)
        {
            case Kind.Identifier:
            case Kind.NumericLiteral:
            case Kind.StringLiteral:
                return GetStringLiteralType(name.Text());
            case Kind.ComputedPropertyName:
                {
                    var nameType = CheckComputedPropertyName(name);
                    if (IsTypeAssignableToKind(nameType, TypeFlags.ESSymbolLike))
                    {
                        return nameType;
                    }
                    return _stringType!;
                }
        }
        return _errorType!;
    }

    internal Type? GetTypeOfPropertyOfContextualTypeWorker(Type t, string name)
    {
        return GetTypeOfPropertyOfContextualTypeEx(t, name, null);
    }

    internal Type? GetTypeOfPropertyOfContextualTypeEx(Type t, string name, Type? nameType)
    {
        return MapTypeEx(t, s =>
        {
            if ((s._flags & TypeFlags.Intersection) != 0)
            {
                Type[]? types = null;
                var indexInfoCandidates = new List<Type>();
                var ignoreIndexInfos = false;
                foreach (var constituentType in s.Types())
                {
                    if ((constituentType._flags & TypeFlags.Object) == 0)
                    {
                        continue;
                    }
                    if (IsGenericMappedType(constituentType) && GetMappedTypeNameTypeKind(constituentType) != MappedTypeNameTypeKind.Remapping)
                    {
                        var substitutedType = GetIndexedMappedTypeSubstitutedTypeOfContextualType(constituentType, name, nameType);
                        types = AppendContextualPropertyTypeConstituent(types, substitutedType);
                        continue;
                    }
                    var propertyType = GetTypeOfConcretePropertyOfContextualType(constituentType, name);
                    if (propertyType == null)
                    {
                        if (!ignoreIndexInfos)
                        {
                            indexInfoCandidates.Add(constituentType);
                        }
                        continue;
                    }
                    ignoreIndexInfos = true;
                    indexInfoCandidates.Clear();
                    types = AppendContextualPropertyTypeConstituent(types, propertyType);
                }
                foreach (var candidate in indexInfoCandidates)
                {
                    var indexInfoType = GetTypeFromIndexInfosOfContextualType(candidate, name, nameType);
                    types = AppendContextualPropertyTypeConstituent(types, indexInfoType);
                }
                if (types == null || types.Length == 0)
                {
                    return null!;
                }
                if (types.Length == 1)
                {
                    return types[0];
                }
                return GetIntersectionType(types);
            }
            if ((s._flags & TypeFlags.Object) == 0)
            {
                return null!;
            }
            if (IsGenericMappedType(s) && GetMappedTypeNameTypeKind(s) != MappedTypeNameTypeKind.Remapping)
            {
                return GetIndexedMappedTypeSubstitutedTypeOfContextualType(s, name, nameType)!;
            }
            var result = GetTypeOfConcretePropertyOfContextualType(s, name);
            if (result != null)
            {
                return result;
            }
            return GetTypeFromIndexInfosOfContextualType(s, name, nameType)!;
        }, true /*noReductions*/);
    }

    internal Type? GetIndexedMappedTypeSubstitutedTypeOfContextualType(Type t, string name, Type? nameType)
    {
        var propertyNameType = nameType;
        if (propertyNameType == null)
        {
            propertyNameType = GetStringLiteralType(name);
        }
        var constraint = GetConstraintTypeFromMappedType(t)!;
        // special case for conditional types pretending to be negated types
        if (t.AsMappedType()!._nameType != null && IsExcludedMappedPropertyName(t.AsMappedType()!._nameType!, propertyNameType) || IsExcludedMappedPropertyName(constraint, propertyNameType))
        {
            return null;
        }
        var constraintOfConstraint = GetBaseConstraintOrType(constraint);
        if (!IsTypeAssignableToWorker(propertyNameType, constraintOfConstraint))
        {
            return null;
        }
        return SubstituteIndexedMappedType(t, propertyNameType);
    }

    internal bool IsExcludedMappedPropertyName(Type t, Type propertyNameType)
    {
        if ((t._flags & TypeFlags.Conditional) != 0)
        {
            return (GetReducedTypeWorker(GetTrueTypeFromConditionalType(t))._flags & TypeFlags.Never) != 0 &&
                GetActualTypeVariable(GetFalseTypeFromConditionalType(t)) == GetActualTypeVariable(t.AsConditionalType()!._checkType!) &&
                IsTypeAssignableToWorker(propertyNameType, t.AsConditionalType()!._extendsType!);
        }
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return Some(t.Types(), s => IsExcludedMappedPropertyName(s, propertyNameType));
        }
        return false;
    }

    internal Type? GetTypeOfConcretePropertyOfContextualType(Type t, string name)
    {
        var prop = GetPropertyOfTypeWorker(t, name);
        if (prop == null || IsCircularMappedProperty(prop))
        {
            return null;
        }
        return RemoveMissingType(GetTypeOfSymbolWorker(prop), (prop.Flags & SymbolFlags.Optional) != 0);
    }

    internal Type? GetTypeFromIndexInfosOfContextualType(Type t, string name, Type? nameType)
    {
        if (IsTupleTypeWorker(t) && IsNumericLiteralName(name) && FromString(name) >= 0)
        {
            var restType = GetElementTypeOfSliceOfTupleType(t, t.TargetTupleType()!.FixedLength(), 0 /*endSkipCount*/, false /*writing*/, true /*noReductions*/);
            if (restType != null)
            {
                return restType;
            }
        }
        if (nameType == null)
        {
            nameType = GetStringLiteralType(name);
        }
        var indexInfo = FindApplicableIndexInfo(GetIndexInfosOfStructuredType(t), nameType);
        if (indexInfo == null)
        {
            return null;
        }
        return indexInfo._valueType;
    }

    internal bool IsCircularMappedProperty(Symbol symbol)
    {
        if ((symbol.CheckFlags & CheckFlags.Mapped) != 0)
        {
            var links = _valueSymbolLinks.Get(symbol);
            return links._resolvedType == null && FindResolutionCycleStartIndex(symbol, TypeSystemPropertyName.Type) >= 0;
        }
        return false;
    }

    internal Type[]? AppendContextualPropertyTypeConstituent(Type[]? types, Type? t)
    {
        // any doesn't provide any contextual information but could spoil the overall result by nullifying contextual information
        // provided by other intersection constituents so it gets replaced with `unknown` as `T & unknown` is just `T` and all
        // types computed based on the contextual information provided by other constituens are still assignable to any
        if (t == null)
        {
            return types;
        }
        if ((t._flags & TypeFlags.Any) != 0)
        {
            Array.Resize(ref types, (types?.Length ?? 0) + 1);
            types![^1] = _unknownType!;
            return types;
        }
        Array.Resize(ref types, (types?.Length ?? 0) + 1);
        types![^1] = t;
        return types;
    }

    // Return the contextual type for a given expression node. During overload resolution, a contextual type may temporarily
    // be "pushed" onto a node using the contextualType property.
    internal Type? GetApparentTypeOfContextualType(Node node, ContextFlags contextFlags)
    {
        Type? contextualType;
        if (IsObjectLiteralMethod(node))
        {
            contextualType = GetContextualTypeForObjectLiteralMethod(node, contextFlags);
        }
        else
        {
            contextualType = GetContextualTypeWorker(node, contextFlags);
        }
        var instantiatedType = InstantiateContextualType(contextualType, node, contextFlags);
        if (instantiatedType != null && !((contextFlags & ContextFlags.NoConstraints) != 0 && (instantiatedType._flags & TypeFlags.TypeVariable) != 0))
        {
            var apparentType = MapTypeEx(instantiatedType, t =>
            {
                if ((t._objectFlags & ObjectFlags.Mapped) != 0)
                {
                    return t;
                }
                return GetApparentTypeWorker(t);
            }, true)!;
            if ((apparentType._flags & TypeFlags.Union) != 0 && IsObjectLiteralExpression(node))
            {
                return DiscriminateContextualTypeByObjectMembers(node, apparentType);
            }
            if ((apparentType._flags & TypeFlags.Union) != 0 && IsJsxAttributes(node))
            {
                return DiscriminateContextualTypeByJSXAttributes(node, apparentType);
            }
            return apparentType;
        }
        return null;
    }

    internal Type? DiscriminateContextualTypeByObjectMembers(Node node, Type contextualType)
    {
        var key = new DiscriminatedContextualTypeKey(GetNodeId(node), contextualType._id);
        if (_discriminatedContextualTypes.GetValueOrDefault(key) is { } cachedDiscriminated)
        {
            return cachedDiscriminated;
        }
        var discriminated = GetMatchingUnionConstituentForObjectLiteral(contextualType, node);
        if (discriminated == null)
        {
            var discriminantProperties = Filter(node.Properties()!, p =>
            {
                var symbol = p.Symbol;
                if (symbol == null)
                {
                    return false;
                }
                if (IsPropertyAssignment(p))
                {
                    return IsPossiblyDiscriminantValue(p.Initializer()!) && IsDiscriminantProperty(contextualType, symbol.Name);
                }
                if (IsShorthandPropertyAssignment(p))
                {
                    return IsDiscriminantProperty(contextualType, symbol.Name);
                }
                return false;
            });
            var discriminantMembers = Filter(GetPropertiesOfTypeWorker(contextualType), s =>
            {
                return (s.Flags & SymbolFlags.Optional) != 0 && node.Symbol!.Members?.GetValueOrDefault(s.Name) == null && IsDiscriminantProperty(contextualType, s.Name);
            });
            var discriminator = new ObjectLiteralDiscriminator { _c = this, _props = discriminantProperties, _members = discriminantMembers };
            discriminated = DiscriminateTypeByDiscriminableItems(contextualType, discriminator);
        }
        _discriminatedContextualTypes[key] = discriminated!;
        return discriminated;
    }

    internal Type? GetMatchingUnionConstituentForObjectLiteral(Type unionType, Node node)
    {
        var keyPropertyName = GetKeyPropertyName(unionType);
        if (keyPropertyName != "")
        {
            Node? propNode = Find(node.Properties()!, p => p.Symbol != null && IsPropertyAssignment(p) && p.Symbol!.Name == keyPropertyName && IsPossiblyDiscriminantValue(p.Initializer()!));
            if (propNode != null)
            {
                var propType = GetContextFreeTypeOfExpression(propNode.Initializer()!)!;
                return GetConstituentTypeForKeyType(unionType, propType);
            }
        }
        return null;
    }

    // Return true if the given expression is possibly a discriminant value. We limit the kinds of
    // expressions we check to those that don't depend on their contextual type in order not to cause
    // recursive (and possibly infinite) invocations of getContextualType.
    internal bool IsPossiblyDiscriminantValue(Node node)
    {
        switch (node.Kind)
        {
            case Kind.StringLiteral:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateExpression:
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.NullKeyword:
            case Kind.Identifier:
            case Kind.UndefinedKeyword:
                return true;
            case Kind.PropertyAccessExpression:
            case Kind.ParenthesizedExpression:
                return IsPossiblyDiscriminantValue(node.Expression()!);
            case Kind.JsxExpression:
                return node.Expression() == null || IsPossiblyDiscriminantValue(node.Expression()!);
        }
        return false;
    }

    // If the given contextual type contains instantiable types and if a mapper representing
    // return type inferences is available, instantiate those types using that mapper.
    internal Type? InstantiateContextualType(Type? contextualType, Node node, ContextFlags contextFlags)
    {
        if (contextualType != null && MaybeTypeOfKind(contextualType, TypeFlags.Instantiable))
        {
            var inferenceContext = GetInferenceContext(node);
            // If no inferences have been made, and none of the type parameters for which we are inferring
            // specify default types, nothing is gained from instantiating as type parameters would just be
            // replaced with their constraints similar to the apparent type.
            if (inferenceContext != null)
            {
                if ((contextFlags & ContextFlags.Signature) != 0 && Some<InferenceInfo>([.. inferenceContext._inferences], HasInferenceCandidatesOrDefault))
                {
                    // For contextual signatures we incorporate all inferences made so far, e.g. from return
                    // types as well as arguments to the left in a function call.
                    var t = InstantiateInstantiableTypes(contextualType, inferenceContext._nonFixingMapper!);
                    if ((t._flags & TypeFlags.AnyOrUnknown) == 0)
                    {
                        return t;
                    }
                }
                if (inferenceContext._returnMapper != null)
                {
                    // For other purposes (e.g. determining whether to produce literal types) we only
                    // incorporate inferences made from the return type in a function call. We remove
                    // the 'boolean' type from the contextual type such that contextually typed boolean
                    // literals actually end up widening to 'boolean' (see #48363).
                    var t = InstantiateInstantiableTypes(contextualType, inferenceContext._returnMapper);
                    if ((t._flags & TypeFlags.AnyOrUnknown) == 0)
                    {
                        if ((t._flags & TypeFlags.Union) != 0 && ContainsType(t.Types(), _regularFalseType!) && ContainsType(t.Types(), _regularTrueType!))
                        {
                            return FilterType(t, s => s != _regularFalseType && s != _regularTrueType);
                        }
                        return t;
                    }
                }
            }
        }
        return contextualType;
    }

    // This function is similar to instantiateType, except that (a) it only instantiates types that
    // are classified as instantiable (i.e. it doesn't instantiate object types), and (b) it performs
    // no reductions on instantiated union types.
    internal Type InstantiateInstantiableTypes(Type t, TypeMapper mapper)
    {
        if ((t._flags & TypeFlags.Instantiable) != 0)
        {
            return InstantiateType(t, mapper);
        }
        if ((t._flags & TypeFlags.Union) != 0)
        {
            return GetUnionTypeExWorker(Map(t.Types(), s => InstantiateInstantiableTypes(s, mapper)), UnionReduction.None, null, null);
        }
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return GetIntersectionType(Map(t.Types(), s => InstantiateInstantiableTypes(s, mapper)));
        }
        return t;
    }

    internal void PushCachedContextualType(Node node)
    {
        PushContextualType(node, GetContextualTypeWorker(node, ContextFlags.None), true /*isCache*/);
    }

    internal void PushContextualType(Node node, Type? t, bool isCache)
    {
        _contextualInfos.Add(new ContextualInfo(node, t, isCache));
    }

    internal void PopContextualType()
    {
        var lastIndex = _contextualInfos.Count - 1;
        _contextualInfos[lastIndex] = new ContextualInfo();
        _contextualInfos.RemoveAt(lastIndex);
    }

    internal int FindContextualNode(Node node, bool includeCaches)
    {
        for (var i = 0; i < _contextualInfos.Count; i++)
        {
            var info = _contextualInfos[i];
            if (node == info._node && (includeCaches || !info._isCache))
            {
                return i;
            }
        }
        return -1;
    }
}

// ported from: internal/checker/checker.go:31117-31158（Go 的包级类型 `ObjectLiteralDiscriminator`）
// 实现 relater.go:1199 的 `Discriminator` 接口（跨片：由 relater 分片落地为 `Discriminator`，成员 `Len()/Name(int)/Matches(int, Type)`）。
// Go 用结构体字面量 `&ObjectLiteralDiscriminator{c: c, props: ..., members: ...}` 构造 → C# 对象初始化器
// （jsx.go:290 的构造点同款写法，见报告）；未导出字段按 `_camelCase`。
internal sealed class ObjectLiteralDiscriminator : Discriminator
{
    internal Checker _c = null!;
    internal Node[] _props = [];
    internal Symbol[] _members = [];

    public int Len()
    {
        return _props.Length + _members.Length;
    }

    public string Name(int index)
    {
        if (index < _props.Length)
        {
            return _props[index].Symbol!.Name;
        }
        return _members[index - _props.Length].Name;
    }

    public bool Matches(int index, Type t)
    {
        Type propType;
        if (index < _props.Length)
        {
            var prop = _props[index];
            if (IsPropertyAssignment(prop) || IsJsxAttribute(prop))
            {
                var initializer = prop.Initializer();
                if (initializer != null)
                {
                    propType = _c.GetContextFreeTypeOfExpression(prop.Initializer()!)!;
                }
                else
                {
                    propType = _c._trueType!; // JsxAttribute without initializer is always true
                }
            }
            else
            {
                propType = _c.GetContextFreeTypeOfExpression(prop.Name()!)!;
            }
        }
        else
        {
            propType = _c._undefinedType!;
        }
        foreach (var s in propType.Distributed())
        {
            if (_c.IsTypeAssignableToWorker(s, t))
            {
                return true;
            }
        }
        return false;
    }
}
// ported from: internal/checker/nodebuilderimpl.go:2719-3298（结构化类型成员→类型字面量、匿名类型节点、类型引用/条件类型转换）
//
// 与 Go 的差异（逐条）：
// 1) 包级函数（`getTypeAliasForTypeLiteral`）→ `partial class Checker` 的 static 成员（§5.5）。
// 2) Go 的方法值（`(*NodeBuilderImpl).createTypeNodeFromObjectType`/`typeToTypeNode`/`conditionalTypeToTypeNode`）
//    → C# lambda（`Func<NodeBuilderImpl, Type, Node?>`：Go 的方法值带接收者，C# 的方法组不带，无法直接转委托）。
// 3) Go 未导出字段 → `_camelCase`（§5.5）；同名方法冲突用 Worker 名（`getTypeFromTypeNode`→`GetTypeFromTypeNodeWorker` 等）。
// 4) `slices.Concat(a, b)` → 展开表达式 `[.. a, .. b]`；`slices.Equal(a, b)` → `SequenceEqual`；
//    `min(a, b)` → `Math.Min`；`core.Specialize`/`core.IfElse` 等同名 static。
// 5) Go 的函数值字段 `c.getGlobalIterableType` 等 → C# 委托字段 `_getGlobalIterableType()`（checker.go 的 func 字段）。
// 6) Go 的 `for ok := true; ok; ok = <cond>`（do-while）→ C# `do { } while (<cond>)`。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using System.Linq;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Checker.Checker;
using static TypeScript.Scanner.Scanner;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;

public sealed partial class NodeBuilderImpl
{
    internal NodeList? CreateTypeNodesFromResolvedType(StructuredType resolvedType)
    {
        if (CheckTruncationLength())
        {
            if ((_ctx!._flags & NodeBuilderFlags.NoTruncation) != 0)
            {
                var elem = _f.NewNotEmittedTypeElement();
                return _f.NewNodeList([_e.AddSyntheticTrailingComment(elem, Kind.MultiLineCommentTrivia, "elided", false /*hasTrailingNewLine*/)]);
            }

            return _f.NewNodeList([_f.NewPropertySignatureDeclaration(null, _f.NewIdentifier("..."), null, null, null)]);
        }

        var typeElements = new List<Node>();
        foreach (var signature in resolvedType.CallSignatures())
        {
            typeElements.Add(SignatureToSignatureDeclarationHelper(signature, Kind.CallSignature, null)!);
        }

        foreach (var signature in resolvedType.ConstructSignatures())
        {
            if ((signature._flags & SignatureFlags.Abstract) != 0)
            {
                continue;
            }

            typeElements.Add(SignatureToSignatureDeclarationHelper(signature, Kind.ConstructSignature, null)!);
        }

        // Go: `for _, info := range resolvedType.indexInfos`（nodebuilderimpl.go:2737）—— 切片可为 nil，
        // `range nil` 合法（0 次迭代）；C# 的 `IndexInfo[]` 为 null 时 `foreach` 会抛 NRE（setStructuredTypeMembers
        // 在无 index signature 时把 `slices.Clip(nil)` 存回，即 nil），故按 Go 语义判空。
        foreach (var info in resolvedType._indexInfos ?? [])
        {
            typeElements.AddRange(IndexInfoToObjectComputedNamesOrSignatureDeclaration(info, IfElse((resolvedType._objectFlags & ObjectFlags.ReverseMapped) != 0, CreateElidedInformationPlaceholder(), null)));
        }

        var properties = resolvedType._properties ?? [];
        if (properties.Length == 0)
        {
            return _f.NewNodeList(typeElements.ToArray());
        }

        var i = 0;
        foreach (var propertySymbol in properties)
        {
            if (IsExpanding(_ctx!) && (propertySymbol.Flags & SymbolFlags.Prototype) != 0)
            {
                continue;
            }

            i++;
            if ((_ctx!._flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0)
            {
                if ((propertySymbol.Flags & SymbolFlags.Prototype) != 0)
                {
                    continue;
                }

                if ((GetDeclarationModifierFlagsFromSymbol(propertySymbol) & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
                {
                    _ctx!._tracker.ReportPrivateInBaseOfClassExpression(propertySymbol.Name);
                }

                if (IsPrivateIdentifierSymbol(propertySymbol))
                {
                    _ctx!._tracker.ReportPrivateInBaseOfClassExpression(SymbolName(propertySymbol));
                }
            }

            if (CheckTruncationLength() && (i + 2 < properties.Length - 1))
            {
                if ((_ctx!._flags & NodeBuilderFlags.NoTruncation) != 0)
                {
                    typeElements[^1] = _e.AddSyntheticTrailingComment(typeElements[^1], Kind.MultiLineCommentTrivia, $"... {properties.Length - i} more elided ...", false /*hasTrailingNewLine*/);
                }
                else
                {
                    var text = $"... {properties.Length - i} more ...";
                    typeElements.Add(_f.NewPropertySignatureDeclaration(null, _f.NewIdentifier(text), null, null, null));
                }

                typeElements = new List<Node>(AddPropertyToElementList(properties[^1], typeElements.ToArray()));
                break;
            }

            typeElements = new List<Node>(AddPropertyToElementList(propertySymbol, typeElements.ToArray()));
        }

        if (typeElements.Count != 0)
        {
            return _f.NewNodeList(typeElements.ToArray());
        }
        else
        {
            return null;
        }
    }

    internal Node? CreateTypeNodeFromObjectType(Type t)
    {
        if (_ch.IsGenericMappedType(t) || ((t._objectFlags & ObjectFlags.Mapped) != 0 && t.AsMappedType()!._containsError))
        {
            return CreateMappedTypeNodeFromType(t);
        }

        var resolved = _ch.ResolveStructuredTypeMembers(t);
        var callSigs = resolved.CallSignatures();
        var ctorSigs = resolved.ConstructSignatures();
        // Go: `len(resolved.properties)` / `len(resolved.indexInfos)` —— 二者都是切片，nil 与空等价
        // （setStructuredTypeMembers 置 nil 的两处：getNamedMembers 对空成员表返回 nil、slices.Clip(nil)）。
        var resolvedProperties = resolved._properties ?? [];
        var resolvedIndexInfos = resolved._indexInfos ?? [];
        if (resolvedProperties.Length == 0 && resolvedIndexInfos.Length == 0)
        {
            if (callSigs.Length == 0 && ctorSigs.Length == 0)
            {
                _ctx!._approximateLength += 2;
                var result = _f.NewTypeLiteralNode(_f.NewNodeList([]));
                _e.SetEmitFlags(result, EmitFlags.SingleLine);
                return result;
            }

            if (callSigs.Length == 1 && ctorSigs.Length == 0)
            {
                var signature = callSigs[0];
                return SignatureToSignatureDeclarationHelper(signature, Kind.FunctionType, null);
            }

            if (ctorSigs.Length == 1 && callSigs.Length == 0)
            {
                var signature = ctorSigs[0];
                return SignatureToSignatureDeclarationHelper(signature, Kind.ConstructorType, null);
            }
        }

        var abstractSignatures = Filter(ctorSigs, signature => (signature._flags & SignatureFlags.Abstract) != 0);
        if (abstractSignatures.Length > 0)
        {
            var types = new List<Type>();
            foreach (var s in abstractSignatures)
            {
                types.Add(_ch.GetOrCreateTypeFromSignature(s));
            }

            // count the number of type elements excluding abstract constructors
            var typeElementCount = callSigs.Length + (ctorSigs.Length - abstractSignatures.Length) + resolvedIndexInfos.Length + IfElse((_ctx!._flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0, CountWhere(resolvedProperties, p => (p.Flags & SymbolFlags.Prototype) == 0), resolvedProperties.Length);

            // don't include an empty object literal if there were no other static-side
            // properties to write, i.e. `abstract class C { }` becomes `abstract new () => {}`
            // and not `(abstract new () => {}) & {}`
            if (typeElementCount != 0)
            {
                // create a copy of the object type without any abstract construct signatures.
                types.Add(GetResolvedTypeWithoutAbstractConstructSignatures(resolved));
            }

            return TypeToTypeNode(_ch.GetIntersectionType(types.ToArray()));
        }

        var restoreFlags = SaveRestoreFlags();
        _ctx!._flags |= NodeBuilderFlags.InObjectTypeLiteral;
        var members = CreateTypeNodesFromResolvedType(resolved);
        restoreFlags();
        var typeLiteralNode = _f.NewTypeLiteralNode(members);
        _ctx!._approximateLength += 2;
        _e.SetEmitFlags(typeLiteralNode, IfElse((_ctx!._flags & NodeBuilderFlags.MultilineObjectLiterals) != 0, EmitFlags.None, EmitFlags.SingleLine));
        return typeLiteralNode;
    }

    internal (bool ShouldWrite, Symbol? Symbol) ShouldWriteTypeOfFunctionSymbol(Symbol symbol, TypeId typeId)
    {
        // `typeof C.name` can only be written when the member name is a valid identifier
        var isStaticMethodSymbol = (symbol.Flags & SymbolFlags.Method) != 0 && IsIdentifierText(symbol.Name, LanguageVariant.Standard) && Some(symbol.Declarations, declaration =>
            IsStatic(declaration) && !_ch.IsLateBindableIndexSignature(GetNameOfDeclaration(declaration)!));
        var isNonLocalFunctionSymbol = false;
        var isFunctionExpressionSymbol = false;
        if ((symbol.Flags & SymbolFlags.Function) != 0)
        {
            if (symbol.Parent is not null)
            {
                isNonLocalFunctionSymbol = true;
            }
            else
            {
                foreach (var declaration in symbol.Declarations ?? [])
                {
                    if (declaration.Parent!.Kind == Kind.SourceFile || declaration.Parent!.Kind == Kind.ModuleBlock)
                    {
                        isNonLocalFunctionSymbol = true;
                        break;
                    }

                    if (IsFunctionExpressionOrArrowFunction(declaration) && IsVariableDeclaration(declaration.Parent!) &&
                        IsVariableDeclarationList(declaration.Parent!.Parent!) && IsVariableStatement(declaration.Parent!.Parent!.Parent!) &&
                        declaration.Parent!.Parent!.Parent!.Parent is not null &&
                        (declaration.Parent!.Parent!.Parent!.Parent!.Kind == Kind.SourceFile || declaration.Parent!.Parent!.Parent!.Parent!.Kind == Kind.ModuleBlock))
                    {
                        isNonLocalFunctionSymbol = true;
                        isFunctionExpressionSymbol = true;
                        break;
                    }
                }
            }
        }

        if (isStaticMethodSymbol || isNonLocalFunctionSymbol)
        {
            if (isFunctionExpressionSymbol && symbol.ValueDeclaration is not null && symbol.ValueDeclaration.Parent is not null && symbol.ValueDeclaration.Parent != _ctx!._enclosingDeclaration)
            {
                symbol = _ch.GetMergedSymbolWorker(symbol.ValueDeclaration.Parent.Symbol!)!;
            }

            // typeof is allowed only for static/non local functions
            return (((_ctx!._flags & NodeBuilderFlags.UseTypeOfFunction) != 0 || _ctx!._visitedTypes.Has(typeId)) && // it is type of the symbol uses itself recursively
                ((_ctx!._flags & NodeBuilderFlags.UseStructuralFallback) == 0 || _ch.IsValueSymbolAccessible(symbol, _ctx!._enclosingDeclaration)), symbol); // And the build is going to succeed without visibility error or there is no structural fallback allowed
        }

        return (false, symbol);
    }

    internal Node? CreateAnonymousTypeNode(Type t)
    {
        return CreateAnonymousTypeNodeEx(t, false, false);
    }

    internal (bool ShouldEmit, Symbol? Symbol) ShouldEmitTypeOfSymbol(bool forceExpansion, bool forceClassExpansion, SymbolFlags isInstanceType, Symbol symbol, TypeId typeId)
    {
        if (forceExpansion)
        {
            return (false, symbol);
        }

        var nonFunctionResult = (symbol.Flags & SymbolFlags.Class) != 0 && !forceClassExpansion && _ch.GetBaseTypeVariableOfClass(symbol) is null && !(symbol.ValueDeclaration is not null && IsClassLike(symbol.ValueDeclaration) && (_ctx!._flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0 && (!IsClassDeclaration(symbol.ValueDeclaration) || _ch.IsSymbolAccessible(symbol, _ctx!._enclosingDeclaration, isInstanceType, false /*shouldComputeAliasesToMakeVisible*/).Accessibility != SymbolAccessibility.Accessible)) || (symbol.Flags & (SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0;
        if (nonFunctionResult)
        {
            return (true, symbol);
        }

        return ShouldWriteTypeOfFunctionSymbol(symbol, typeId);
    }

    internal Node? CreateAnonymousTypeNodeEx(Type t, bool forceClassExpansion, bool forceExpansion)
    {
        var typeId = t._id;
        var symbol = t._symbol;
        if (symbol is not null)
        {
            var isInstantiationExpressionType = (t._objectFlags & ObjectFlags.InstantiationExpressionType) != 0;
            if (isInstantiationExpressionType)
            {
                var instantiationExpressionType = t.AsInstantiationExpressionType()!;
                var existing = instantiationExpressionType._node;
                //  instantiationExpressionType.node is unreliable for constituents of unions and intersections.
                // declare const Err: typeof ErrImpl & (<T>() => T);
                // type ErrAlias<U> = typeof Err<U>;
                // declare const e: ErrAlias<number>;
                // ErrAlias<number> = typeof Err<number> = typeof ErrImpl & (<number>() => number)
                // The problem is each constituent of the intersection will be associated with typeof Err<number>
                // And when extracting a type for typeof ErrImpl from typeof Err<number> does not make sense.
                if (existing is not null && IsTypeQueryNode(existing) && GetTypeFromTypeNode(existing, false) == t)
                {
                    // Guard against unbounded recursion when the existing typeof node fails to be reused
                    // (e.g. its entity name isn't accessible from this scope) and the recovery boundary's
                    // fallback re-enters typeToTypeNode with the very same instantiation type, which would
                    // in turn try to reuse the same node again. Mark the type as visited around the reuse
                    // attempt so the inner recursion bottoms out via the visitedTypes guard below.
                    if (_ctx!._visitedTypes.Has(typeId))
                    {
                        return CreateElidedInformationPlaceholder();
                    }

                    _ctx!._visitedTypes.Add(typeId);
                    var typeNode = TryReuseExistingNonParameterTypeNode(existing, t, null, null);
                    _ctx!._visitedTypes.Delete(typeId);
                    if (typeNode is not null)
                    {
                        return typeNode;
                    }
                }

                if (_ctx!._visitedTypes.Has(typeId))
                {
                    return CreateElidedInformationPlaceholder();
                }

                return VisitAndTransformType(t, static (b, t2) => b.CreateTypeNodeFromObjectType(t2));
            }

            SymbolFlags isInstanceType;
            if (IsClassInstanceSide(_ch, t))
            {
                isInstanceType = SymbolFlags.Type;
            }
            else
            {
                isInstanceType = SymbolFlags.Value;
            }

            // !!! JS support
            // if c.isJSConstructor(symbol.ValueDeclaration) { return b.symbolToTypeNode(symbol, isInstanceType, nil) } else
            var (shouldEmit, symbol2) = ShouldEmitTypeOfSymbol(forceExpansion, forceClassExpansion, isInstanceType, symbol, typeId);
            if (shouldEmit)
            {
                if (ShouldExpandType(t, false /*isAlias*/))
                {
                    _ctx!._depth++;
                }
                else
                {
                    return SymbolToTypeNode(symbol2!, isInstanceType, null);
                }
            }

            if (_ctx!._visitedTypes.Has(typeId))
            {
                // If type is an anonymous type literal in a type alias declaration, use type alias name
                var typeAlias = GetTypeAliasForTypeLiteral(_ch, t);
                if (typeAlias is not null)
                {
                    // The specified symbol flags need to be reinterpreted as type flags
                    return SymbolToTypeNode(typeAlias, SymbolFlags.Type, null);
                }
                else
                {
                    return CreateElidedInformationPlaceholder();
                }
            }
            else
            {
                return VisitAndTransformType(t, static (b, t2) => b.CreateTypeNodeFromObjectType(t2));
            }
        }
        else
        {
            // Anonymous types without a symbol are never circular.
            return CreateTypeNodeFromObjectType(t);
        }
    }

    internal Type? GetTypeFromTypeNode(Node node, bool noMappedTypes)
    {
        // !!! noMappedTypes optional param support
        if (node.Parent is null)
        {
            return _ch._errorType;
        }

        var t = _ch.GetTypeFromTypeNodeWorker(node);
        if (_ctx!._mapper is null)
        {
            return t;
        }

        var instantiated = _ch.InstantiateType(t, _ctx!._mapper);
        if (noMappedTypes && instantiated != t)
        {
            return null;
        }

        return instantiated;
    }

    internal Node? TypeToTypeNodeOrCircularityElision(Type t)
    {
        if ((t._flags & TypeFlags.Union) != 0)
        {
            if (_ctx!._visitedTypes.Has(t._id))
            {
                if ((_ctx!._flags & NodeBuilderFlags.AllowAnonymousIdentifier) == 0)
                {
                    _ctx!._encounteredError = true;
                    _ctx!._tracker.ReportCyclicStructureError();
                }

                return CreateElidedInformationPlaceholder();
            }

            return VisitAndTransformType(t, static (b, t2) => b.TypeToTypeNode(t2));
        }

        return TypeToTypeNode(t);
    }

    internal Node? ConditionalTypeToTypeNode(Type type_)
    {
        if (CheckTruncationLength())
        {
            return CreateElidedInformationPlaceholder();
        }

        var t = type_.AsConditionalType()!;
        var checkTypeNode = TypeToTypeNode(t._checkType);
        _ctx!._approximateLength += 15;
        if ((_ctx!._flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && t._root!._isDistributive && (t._checkType!._flags & TypeFlags.TypeParameter) == 0)
        {
            var newParam = _ch.NewTypeParameter(_ch.NewSymbol(SymbolFlags.TypeParameter, "T" /* as __String */));
            var name = TypeParameterToName(newParam);
            var newTypeVariable = _f.NewTypeReferenceNode(name.AsNode(), null);
            _ctx!._approximateLength += 37;
            // 15 each for two added conditionals, 7 for an added infer type
            var newMapper = PrependTypeMapping(t._root._checkType!, newParam, t._mapper);
            var saveInferTypeParameters = _ctx!._inferTypeParameters;
            _ctx!._inferTypeParameters = t._root._inferTypeParameters;
            var extendsTypeNode = TypeToTypeNode(_ch.InstantiateType(t._root._extendsType!, newMapper));
            _ctx!._inferTypeParameters = saveInferTypeParameters;
            var trueTypeNode = TypeToTypeNodeOrCircularityElision(_ch.InstantiateType(GetTypeFromTypeNode(t._root._node!.TrueType!, false)!, newMapper));
            var falseTypeNode = TypeToTypeNodeOrCircularityElision(_ch.InstantiateType(GetTypeFromTypeNode(t._root._node!.FalseType!, false)!, newMapper));

            // outermost conditional makes `T` a type parameter, allowing the inner conditionals to be distributive
            // second conditional makes `T` have `T & checkType` substitution, so it is correctly usable as the checkType
            // inner conditional runs the check the user provided on the check type (distributively) and returns the result
            // checkType extends infer T ? T extends checkType ? T extends extendsType<T> ? trueType<T> : falseType<T> : never : never;
            // this is potentially simplifiable to
            // checkType extends infer T ? T extends checkType & extendsType<T> ? trueType<T> : falseType<T> : never;
            // but that may confuse users who read the output more.
            // On the other hand,
            // checkType extends infer T extends checkType ? T extends extendsType<T> ? trueType<T> : falseType<T> : never;
            // may also work with `infer ... extends ...` in, but would produce declarations only compatible with the latest TS.
            var newId = newTypeVariable.AsTypeReferenceNode()!.TypeName!.AsIdentifier()!.Clone(_f)!;
            var syntheticExtendsNode = _f.NewInferTypeNode(_f.NewTypeParameterDeclaration(null, newId, null, null, null));
            var innerCheckConditionalNode = _f.NewConditionalTypeNode(newTypeVariable, extendsTypeNode, trueTypeNode, falseTypeNode);
            var syntheticTrueNode = _f.NewConditionalTypeNode(_f.NewTypeReferenceNode(name.Clone(_f), null), _f.DeepCloneNode(checkTypeNode!), innerCheckConditionalNode, _f.NewKeywordTypeNode(Kind.NeverKeyword));
            return _f.NewConditionalTypeNode(checkTypeNode, syntheticExtendsNode, syntheticTrueNode, _f.NewKeywordTypeNode(Kind.NeverKeyword));
        }

        var saveInferTypeParameters2 = _ctx!._inferTypeParameters;
        _ctx!._inferTypeParameters = t._root!._inferTypeParameters;
        var extendsTypeNode2 = TypeToTypeNode(t._extendsType);
        _ctx!._inferTypeParameters = saveInferTypeParameters2;
        var trueTypeNode2 = TypeToTypeNodeOrCircularityElision(_ch.GetTrueTypeFromConditionalType(type_));
        var falseTypeNode2 = TypeToTypeNodeOrCircularityElision(_ch.GetFalseTypeFromConditionalType(type_));
        return _f.NewConditionalTypeNode(checkTypeNode, extendsTypeNode2, trueTypeNode2, falseTypeNode2);
    }

    internal Symbol? GetParentSymbolOfTypeParameter(TypeParameter typeParameter)
    {
        var tp = GetDeclarationOfKind(typeParameter._symbol!, Kind.TypeParameter);
        // !!! JSDoc support
        // if ast.IsJSDocTemplateTag(tp.Parent) { host = getEffectiveContainerForJSDocTemplateTag(tp.Parent) } else {
        var host = tp!.Parent;
        // }
        if (host is null)
        {
            return null;
        }

        return _ch.GetSymbolOfNode(host);
    }

    internal Node? TypeReferenceToTypeNode(Type t)
    {
        var typeArguments = _ch.GetTypeArgumentsWorker(t);
        if (t.Target() == _ch._globalArrayType || t.Target() == _ch._globalReadonlyArrayType)
        {
            if ((_ctx!._flags & NodeBuilderFlags.WriteArrayAsGenericType) != 0)
            {
                var typeArgumentNode = TypeToTypeNode(typeArguments[0]);
                return _f.NewTypeReferenceNode(
                    NewIdentifier(t.Target() == _ch._globalArrayType ? "Array" : "ReadonlyArray", t.Target()!._symbol),
                    _f.NewNodeList([typeArgumentNode!]));
            }

            var elementType = TypeToTypeNode(typeArguments[0]);
            Node arrayType = _f.NewArrayTypeNode(elementType);
            if (t.Target() == _ch._globalArrayType)
            {
                return arrayType;
            }
            else
            {
                return _f.NewTypeOperatorNode(Kind.ReadonlyKeyword, arrayType);
            }
        }
        else if ((t.Target()!._objectFlags & ObjectFlags.Tuple) != 0)
        {
            typeArguments = SameMapIndex(typeArguments, (arg, i) =>
            {
                var isOptional = false;
                if (i < t.Target()!.AsTupleType()!._elementInfos!.Length)
                {
                    isOptional = (t.Target()!.AsTupleType()!._elementInfos![i]._flags & ElementFlags.Optional) != 0;
                }

                return _ch.RemoveMissingType(arg, isOptional);
            });
            if (typeArguments.Length > 0)
            {
                var arity = _ch.GetTypeReferenceArity(t);
                var tupleConstituentNodes = MapToTypeNodes(typeArguments[0..arity], false /*isBareList*/);
                if (tupleConstituentNodes is not null)
                {
                    for (var i = 0; i < (tupleConstituentNodes.Nodes?.Length ?? 0); i++)
                    {
                        var flags = t.Target()!.AsTupleType()!._elementInfos![i]._flags;
                        var labeledElementDeclaration = t.Target()!.AsTupleType()!._elementInfos![i]._labeledDeclaration;

                        if (labeledElementDeclaration is not null)
                        {
                            tupleConstituentNodes.Nodes![i] = _f.NewNamedTupleMember(
                                IfElse((flags & ElementFlags.Variable) != 0, _f.NewToken(Kind.DotDotDotToken), null)!,
                                NewIdentifier(_ch.GetTupleElementLabel(t.Target()!.AsTupleType()!._elementInfos![i], null, i), null /*symbol*/),
                                IfElse((flags & ElementFlags.Optional) != 0, _f.NewToken(Kind.QuestionToken), null)!,
                                IfElse((flags & ElementFlags.Rest) != 0, _f.NewArrayTypeNode(tupleConstituentNodes.Nodes![i]), tupleConstituentNodes.Nodes![i]));
                        }
                        else
                        {
                            if ((flags & ElementFlags.Variable) != 0)
                            {
                                tupleConstituentNodes.Nodes![i] = _f.NewRestTypeNode(IfElse((flags & ElementFlags.Rest) != 0, _f.NewArrayTypeNode(tupleConstituentNodes.Nodes![i]), tupleConstituentNodes.Nodes![i]));
                            }
                            else if ((flags & ElementFlags.Optional) != 0)
                            {
                                tupleConstituentNodes.Nodes![i] = _f.NewOptionalTypeNode(tupleConstituentNodes.Nodes![i]);
                            }
                        }
                    }

                    var tupleTypeNode = _f.NewTupleTypeNode(tupleConstituentNodes);
                    _e.SetEmitFlags(tupleTypeNode, EmitFlags.SingleLine);
                    if (t.Target()!.AsTupleType()!._readonly)
                    {
                        return _f.NewTypeOperatorNode(Kind.ReadonlyKeyword, tupleTypeNode);
                    }
                    else
                    {
                        return tupleTypeNode;
                    }
                }
            }

            if (_ctx!._encounteredError || (_ctx!._flags & NodeBuilderFlags.AllowEmptyTuple) != 0)
            {
                var tupleTypeNode = _f.NewTupleTypeNode(_f.NewNodeList([]));
                _e.SetEmitFlags(tupleTypeNode, EmitFlags.SingleLine);
                if (t.Target()!.AsTupleType()!._readonly)
                {
                    return _f.NewTypeOperatorNode(Kind.ReadonlyKeyword, tupleTypeNode);
                }
                else
                {
                    return tupleTypeNode;
                }
            }

            _ctx!._encounteredError = true;
            return null;
            // TODO: GH#18217
        }
        else if ((_ctx!._flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0 && t._symbol!.ValueDeclaration is not null && IsClassLike(t._symbol.ValueDeclaration) && !_ch.IsValueSymbolAccessible(t._symbol, _ctx!._enclosingDeclaration))
        {
            return CreateAnonymousTypeNode(t);
        }
        else
        {
            var outerTypeParameters = t.Target()!.AsInterfaceType()!.OuterTypeParameters();
            var i = 0;
            Node? resultType = null;
            if (outerTypeParameters is not null)
            {
                var length = outerTypeParameters.Length;
                while (i < length)
                {
                    // Find group of type arguments for type parameters with the same declaring container.
                    var start = i;
                    var parent = GetParentSymbolOfTypeParameter(outerTypeParameters[i].AsTypeParameter()!);
                    do
                    {
                        i++;
                    }
                    while (i < length && GetParentSymbolOfTypeParameter(outerTypeParameters[i].AsTypeParameter()!) == parent);

                    // When type parameters are their own type arguments for the whole group (i.e. we have
                    // the default outer type arguments), we don't show the group.
                    if (!outerTypeParameters[start..i].SequenceEqual(typeArguments[start..i]))
                    {
                        var typeArgumentSlice = MapToTypeNodes(typeArguments[start..i], false /*isBareList*/);
                        var restoreFlags = SaveRestoreFlags();
                        _ctx!._flags |= NodeBuilderFlags.ForbidIndexedAccessSymbolReferences;
                        var ref2 = SymbolToTypeNode(parent!, SymbolFlags.Type, typeArgumentSlice);
                        restoreFlags();
                        if (resultType is null)
                        {
                            resultType = ref2;
                        }
                        else
                        {
                            resultType = AppendReferenceToType(resultType, ref2!);
                        }
                    }
                }
            }

            NodeList? typeArgumentNodes = null;
            if (typeArguments.Length > 0)
            {
                var typeParameterCount = 0;
                var typeParams = t.Target()!.AsInterfaceType()!.TypeParameters();
                if (typeParams is not null)
                {
                    typeParameterCount = Math.Min(typeParams.Length, typeArguments.Length);

                    // Maybe we should do this for more types, but for now we only elide type arguments that are
                    // identical to their associated type parameters' defaults for `Iterable`, `IterableIterator`,
                    // `AsyncIterable`, and `AsyncIterableIterator` to provide backwards-compatible .d.ts emit due
                    // to each now having three type parameters instead of only one.
                    if (_ch.IsReferenceToType(t, _ch._getGlobalIterableType()) || _ch.IsReferenceToType(t, _ch._getGlobalIterableIteratorType()) || _ch.IsReferenceToType(t, _ch._getGlobalAsyncIterableType()) || _ch.IsReferenceToType(t, _ch._getGlobalAsyncIterableIteratorType()))
                    {
                        if (t.AsTypeReference()!._node is null || !IsTypeReferenceNode(t.AsTypeReference()!._node!) || t.AsTypeReference()!._node!.TypeArguments() is null || t.AsTypeReference()!._node!.TypeArguments()!.Length < typeParameterCount)
                        {
                            while (typeParameterCount > 0)
                            {
                                var typeArgument = typeArguments[typeParameterCount - 1];
                                var typeParameter = t.Target()!.AsInterfaceType()!.TypeParameters()[typeParameterCount - 1];
                                var defaultType = _ch.GetDefaultFromTypeParameterWorker(typeParameter);
                                if (defaultType is null || !_ch.IsTypeIdenticalTo(typeArgument, defaultType))
                                {
                                    break;
                                }

                                typeParameterCount--;
                            }
                        }
                    }
                }

                typeArgumentNodes = MapToTypeNodes(typeArguments[i..typeParameterCount], false /*isBareList*/);
            }

            var restoreFlags2 = SaveRestoreFlags();
            _ctx!._flags |= NodeBuilderFlags.ForbidIndexedAccessSymbolReferences;
            var finalRef = SymbolToTypeNode(t._symbol!, SymbolFlags.Type, typeArgumentNodes);
            restoreFlags2();
            if (resultType is null)
            {
                return finalRef;
            }
            else
            {
                return AppendReferenceToType(resultType, finalRef!);
            }
        }
    }

    internal Node? VisitAndTransformType(Type t, Func<NodeBuilderImpl, Type, Node?> transform)
    {
        var typeId = t._id;
        var isConstructorObject = (t._objectFlags & ObjectFlags.Anonymous) != 0 && t._symbol is not null && (t._symbol.Flags & SymbolFlags.Class) != 0;
        CompositeSymbolIdentity? id;
        if ((t._objectFlags & ObjectFlags.Reference) != 0 && t.AsTypeReference()!._node is not null)
        {
            id = new CompositeSymbolIdentity(false, default, GetNodeId(t.AsTypeReference()!._node!));
        }
        else if ((t._flags & TypeFlags.Conditional) != 0)
        {
            id = new CompositeSymbolIdentity(false, default, GetNodeId(t.AsConditionalType()!._root!._node!.AsNode()));
        }
        else if (t._symbol is not null)
        {
            id = new CompositeSymbolIdentity(isConstructorObject, GetSymbolId(t._symbol), default);
        }
        else
        {
            id = null;
        }

        // Since instantiations of the same anonymous type have the same symbol, tracking symbols instead
        // of types allows us to catch circular references to instantiations of the same anonymous type

        var key = new CompositeTypeCacheIdentity(typeId, _ctx!._flags, _ctx!._internalFlags);
        // Don't rely on type cache if we're expanding a type, because we need to compute `canIncreaseExpansionDepth`.
        var canUseCache = _ctx!._maxExpansionDepth < 0;
        if (canUseCache && _ctx!._enclosingDeclaration is not null && _links.Has(_ctx!._enclosingDeclaration))
        {
            var links = _links.Get(_ctx!._enclosingDeclaration);
            if (links._serializedTypes is not null && links._serializedTypes.TryGetValue(key, out var cachedResult))
            {
                // TODO:: check if we instead store late painted statements associated with this?
                if (cachedResult._trackedSymbols is not null)
                {
                    foreach (var arg in cachedResult._trackedSymbols)
                    {
                        _ctx!._tracker.TrackSymbol(arg._symbol, arg._enclosingDeclaration!, arg._meaning);
                    }
                }

                if (cachedResult._truncating)
                {
                    _ctx!._truncating = true;
                }

                _ctx!._approximateLength += cachedResult._addedLength;
                return _f.DeepCloneNode(cachedResult._node!);
            }
        }

        var depth = 0;
        if (id is not null)
        {
            _ctx!._symbolDepth.TryGetValue(id.Value, out depth);
            if (depth > 10)
            {
                return CreateElidedInformationPlaceholder();
            }

            _ctx!._symbolDepth[id.Value] = depth + 1;
        }

        _ctx!._visitedTypes.Add(typeId);
        var prevTrackedSymbols = _ctx!._trackedSymbols;
        _ctx!._trackedSymbols = [];
        var startLength = _ctx!._approximateLength;
        var result = transform(this, t);
        var addedLength = _ctx!._approximateLength - startLength;
        if (canUseCache && !_ctx!._reportedDiagnostic && !_ctx!._encounteredError && _ctx!._enclosingDeclaration is not null)
        {
            var links = _links.Get(_ctx!._enclosingDeclaration);
            links._serializedTypes ??= [];
            links._serializedTypes[key] = new SerializedTypeEntry
            {
                _node = result,
                _truncating = _ctx!._truncating,
                _addedLength = addedLength,
                _trackedSymbols = _ctx!._trackedSymbols.ToArray(),
            };
        }

        _ctx!._visitedTypes.Delete(typeId);
        if (id is not null)
        {
            _ctx!._symbolDepth[id.Value] = depth;
        }

        _ctx!._trackedSymbols = prevTrackedSymbols;
        return result;

        // !!! TODO: Attempt node reuse or parse nodes to minimize copying once text range setting is set up
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func getTypeAliasForTypeLiteral(c *Checker, t *Type) *ast.Symbol</c>（nodebuilderimpl.go:2842）。</summary>
    internal static Symbol? GetTypeAliasForTypeLiteral(Checker c, Type t)
    {
        if (t._symbol is not null && (t._symbol.Flags & SymbolFlags.TypeLiteral) != 0 && t._symbol.Declarations is not null)
        {
            var node = WalkUpParenthesizedTypes(t._symbol.Declarations[0].Parent);
            if (IsTypeAliasDeclaration(node!))
            {
                return c.GetSymbolOfDeclaration(node!);
            }
        }

        return null;
    }
}

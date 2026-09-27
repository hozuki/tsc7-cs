// ported from: internal/checker/nodebuilderimpl.go:1401-2150（类型参数/参数/签名的节点构造、绑定名克隆、返回值序列化）
//
// 与 Go 的差异（逐条）：
// 1) 包级函数（`getEffectiveParameterDeclaration`）→ `partial class Checker` 的 static 成员；
//    `func (c *Checker) getExpandedParameters`（nodebuilderimpl.go:1984）→ 同类实例方法 `GetExpandedParametersWorker`
//    （同名方法冲突：Go 导出的 `GetExpandedParameters` 在 exports.go:255）。
// 2) Go 未导出字段 → `_camelCase`（§5.5）；同名方法冲突一律用 Worker 名（artifacts/checker-method-collisions.md）。
// 3) `printer.EFSingleLine`/`EFNoAsciiEscaping`/`EFNoIndentation` → `EmitFlags.SingleLine`/`NoAsciiEscaping`/`NoIndentation`。
// 4) `(T, bool)` 的 COW 读取 → C# 元组解构；`b.ctx.enclosingSymbolTypes[id]`（读缺失键）→ `TryGetValue`。
// 5) Go 的函数内局部函数（`getUniqAssociatedNamesFromTupleType`/`expandSignatureParametersWithTupleMembers`/`bindElement`/`bindPattern`）
//    → C# 局部函数（保持 Go 的递归闭包语义）。
// 6) `SignatureToSignatureDeclarationOptions`（Go 未导出结构体）→ `internal sealed class`（可空字段 + 对象初始化器）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Checker.Checker;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;

public sealed class SignatureToSignatureDeclarationOptions
{
    internal Node[]? _modifiers;
    internal Node? _name;
    internal Node? _questionToken;
}

public sealed partial class NodeBuilderImpl
{
    internal Node TypeParameterToDeclarationWithConstraint(Type typeParameter, Node? constraintNode)
    {
        var restoreFlags = SaveRestoreFlags();
        _ctx!._flags &= ~NodeBuilderFlags.WriteTypeParametersInQualifiedName; // Avoids potential infinite loop when building for a claimspace with a generic
        var modifiers = CreateModifiersFromModifierFlags(_ch.GetTypeParameterModifiers(typeParameter), _f.NewModifier);
        ModifierList? modifiersList = null;
        if (modifiers.Length > 0)
        {
            modifiersList = _f.NewModifierList(modifiers);
        }

        var name = TypeParameterToName(typeParameter);
        var defaultParameter = _ch.GetDefaultFromTypeParameterWorker(typeParameter);
        Node? defaultParameterDeclarationNode = null;
        if (defaultParameter is not null)
        {
            defaultParameterDeclarationNode = TypeToTypeNode(defaultParameter);
        }

        restoreFlags();
        return _f.NewTypeParameterDeclaration(
            modifiersList,
            name.AsNode(),
            constraintNode,
            null, // expression
            defaultParameterDeclarationNode);
    }

    /// <summary>
    /// Unlike the utilities `setTextRange`, this checks if the `location` we're trying to set on `range` is within the
    /// same file as the active context. If not, the range is not applied. This prevents us from copying ranges across files,
    /// which will confuse the node printer (as it assumes all node ranges are within the current file).
    /// Additionally, if `range` _isn't synthetic_, or isn't in the current file, it will _copy_ it to _remove_ its' position
    /// information.
    ///
    /// It also calls `setOriginalNode` to setup a `.original` pointer, since you basically *always* want these in the node builder.
    /// </summary>
    internal Node? SetTextRange(Node? range_, Node? location)
    {
        if (range_ is null)
        {
            return range_;
        }

        if (!NodeIsSynthesized(range_) || (range_.Flags & NodeFlags.Synthesized) == 0 || _ctx!._enclosingFile is null || _ctx!._enclosingFile != GetSourceFileOfNode(_e.MostOriginal(range_)))
        {
            var original = range_;
            range_ = range_.Clone(_f)!; // if `range` is synthesized or originates in another file, copy it so it definitely has synthetic positions
            range_.Loc = NewTextRange(-1, -1);
            if (_idToSymbol.TryGetValue(original, out var symbol))
            {
                _idToSymbol[range_] = symbol;
            }
        }

        if (range_ == location || location is null)
        {
            return range_;
        }

        // Don't overwrite the original node if `range` has an `original` node that points either directly or indirectly to `location`
        var original2 = _e.Original(range_);
        while (original2 is not null && original2 != location)
        {
            original2 = _e.Original(original2);
        }

        if (original2 is null)
        {
            _e.SetOriginalEx(range_, location, true);
        }

        // only set positions if range comes from the same file since copying text across files isn't supported by the emitter
        if (_ctx!._enclosingFile is not null && _ctx!._enclosingFile == GetSourceFileOfNode(_e.MostOriginal(location)))
        {
            range_.Loc = location.Loc;
            return range_;
        }
        else
        {
            range_.Loc = NewTextRange(-1, -1);
        }

        return range_;
    }

    internal bool TypeParameterShadowsOtherTypeParameterInScope(string name, Type typeParameter)
    {
        var result = _ch._resolveName(_ctx!._enclosingDeclaration, name, SymbolFlags.Type, null!, false /*isUse*/, false);
        if (result is not null && (result.Flags & SymbolFlags.TypeParameter) != 0)
        {
            return result != typeParameter._symbol;
        }

        return false;
    }

    internal Identifier TypeParameterToName(Type typeParameter)
    {
        if ((_ctx!._flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0)
        {
            var (cached, ok) = _ctx!._typeParameterNames.Get(typeParameter._id);
            if (ok)
            {
                return cached!;
            }
        }

        var result = SymbolToName(typeParameter._symbol!, SymbolFlags.Type /*expectsIdentifier*/, true)!;
        if (!IsIdentifier(result))
        {
            return _f.NewIdentifier("(Missing type parameter)").AsIdentifier()!;
        }

        if (typeParameter._symbol is not null && (typeParameter._symbol.Declarations?.Length ?? 0) > 0)
        {
            var decl = typeParameter._symbol.Declarations![0];
            if (decl is not null && IsTypeParameterDeclaration(decl))
            {
                result = SetTextRange(result, decl.Name())!;
            }
        }

        if ((_ctx!._flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0)
        {
            var rawText = result.Text();
            var (i, _) = _ctx!._typeParameterNamesByTextNextNameCount.Get(rawText);
            var text = rawText;

            while (true)
            {
                if (!_ctx!._typeParameterNamesByText.Has(text) && !TypeParameterShadowsOtherTypeParameterInScope(text, typeParameter))
                {
                    break;
                }

                i++;
                text = $"{rawText}_{i}";
            }

            if (text != rawText)
            {
                // !!! TODO: smuggle type arguments out
                // const typeArguments = getIdentifierTypeArguments(result);
                result = NewIdentifier(text, typeParameter._symbol);
                // setIdentifierTypeArguments(result, typeArguments);
            }

            // avoiding iterations of the above loop turns out to be worth it when `i` starts to get large, so we cache the max
            // `i` we've used thus far, to save work later
            _ctx!._typeParameterNamesByTextNextNameCount.Set(rawText, i);
            _ctx!._typeParameterNames.Set(typeParameter._id, result.AsIdentifier()!);
            _ctx!._typeParameterNamesByText.Add(text);
        }

        return result.AsIdentifier()!;
    }

    internal bool IsMappedTypeHomomorphic(Type mapped)
    {
        return _ch.GetHomomorphicTypeVariable(mapped) is not null;
    }

    internal bool IsHomomorphicMappedTypeWithNonHomomorphicInstantiation(MappedType mapped)
    {
        return mapped._target is not null && !IsMappedTypeHomomorphic(mapped) && IsMappedTypeHomomorphic(mapped._target);
    }

    internal Node? CreateMappedTypeNodeFromType(Type t)
    {
        Debug.Assert((t.Flags() & TypeFlags.Object) != 0);
        var mapped = t.AsMappedType()!;
        Node? readonlyToken = null;
        if (mapped._declaration!.ReadonlyToken is not null)
        {
            readonlyToken = _f.NewToken(mapped._declaration.ReadonlyToken.Kind);
        }

        Node? questionToken = null;
        if (mapped._declaration.QuestionToken is not null)
        {
            questionToken = _f.NewToken(mapped._declaration.QuestionToken.Kind);
        }

        Node? appropriateConstraintTypeNode = null;
        Node? newTypeVariable = null;
        var templateType = _ch.GetTemplateTypeFromMappedType(t);
        var typeParameter = _ch.GetTypeParameterFromMappedType(t);

        // If the mapped type isn't `keyof` constraint-declared, _but_ still has modifiers preserved, and its naive instantiation won't preserve modifiers because its constraint isn't `keyof` constrained, we have work to do
        var needsModifierPreservingWrapper = !_ch.IsMappedTypeWithKeyofConstraintDeclaration(t) &&
            (_ch.GetModifiersTypeFromMappedType(t)._flags & TypeFlags.Unknown) == 0 &&
            (_ctx!._flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 &&
            !((_ch.GetConstraintTypeFromMappedType(t)._flags & TypeFlags.TypeParameter) != 0 && (_ch.GetConstraintOfTypeParameterWorker(_ch.GetConstraintTypeFromMappedType(t))!._flags & TypeFlags.Index) != 0);

        if (_ch.IsMappedTypeWithKeyofConstraintDeclaration(t))
        {
            // We have a { [P in keyof T]: X }
            // We do this to ensure we retain the toplevel keyof-ness of the type which may be lost due to keyof distribution during `getConstraintTypeFromMappedType`
            if ((_ctx!._flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && IsHomomorphicMappedTypeWithNonHomomorphicInstantiation(mapped))
            {
                var newConstraintParam = _ch.NewTypeParameter(
                    _ch.NewSymbol(SymbolFlags.TypeParameter, "T"));
                var name = TypeParameterToName(newConstraintParam);
                var target = t.Target()!;
                newTypeVariable = _f.NewTypeReferenceNode(name.AsNode(), null);
                templateType = _ch.InstantiateType(_ch.GetTemplateTypeFromMappedType(target), NewTypeMapper(
                    [_ch.GetTypeParameterFromMappedType(target), _ch.GetModifiersTypeFromMappedType(target)],
                    [typeParameter, newConstraintParam]));
            }

            var indexTarget = newTypeVariable;
            indexTarget ??= TypeToTypeNode(_ch.GetModifiersTypeFromMappedType(t));
            appropriateConstraintTypeNode = _f.NewTypeOperatorNode(Kind.KeyOfKeyword, indexTarget);
        }
        else if (needsModifierPreservingWrapper)
        {
            // So, step 1: new type variable
            var newParam = _ch.NewTypeParameter(
                _ch.NewSymbol(SymbolFlags.TypeParameter, "T"));
            var name = TypeParameterToName(newParam);
            newTypeVariable = _f.NewTypeReferenceNode(name.AsNode(), null);
            // step 2: make that new type variable itself the constraint node, making the mapped type `{[K in T_1]: Template}`
            appropriateConstraintTypeNode = newTypeVariable;
        }
        else
        {
            appropriateConstraintTypeNode = TypeToTypeNode(_ch.GetConstraintTypeFromMappedType(t));
        }

        // nameType and templateType nodes have to be in the new scope
        var cleanup = EnterNewScope(mapped._declaration.AsNode(), null, [_ch.GetTypeParameterFromMappedType(t)], null, null);
        var typeParameterDeclarationNode = TypeParameterToDeclarationWithConstraint(typeParameter, appropriateConstraintTypeNode);
        Node? nameTypeNode = null;
        if (mapped._declaration.NameType is not null)
        {
            nameTypeNode = TypeToTypeNode(_ch.GetNameTypeFromMappedType(t));
        }

        var templateTypeNode = TypeToTypeNode(_ch.RemoveMissingType(
            templateType,
            (GetMappedTypeModifiers(t) & MappedTypeModifiers.IncludeOptional) != 0));
        cleanup();
        var result = _f.NewMappedTypeNode(
            readonlyToken,
            typeParameterDeclarationNode,
            nameTypeNode,
            questionToken,
            templateTypeNode,
            null);
        _ctx!._approximateLength += 10;
        _e.AddEmitFlags(result, EmitFlags.SingleLine);

        if ((_ctx!._flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && IsHomomorphicMappedTypeWithNonHomomorphicInstantiation(mapped))
        {
            // homomorphic mapped type with a non-homomorphic naive inlining
            // wrap it with a conditional like `SomeModifiersType extends infer U ? {..the mapped type...} : never` to ensure the resulting
            // type stays homomorphic

            var rawConstraintTypeFromDeclaration = GetTypeFromTypeNode(mapped._declaration.TypeParameter!.AsTypeParameterDeclaration()!.Constraint!.Type()!, false);
            if (rawConstraintTypeFromDeclaration is not null)
            {
                rawConstraintTypeFromDeclaration = _ch.GetConstraintOfTypeParameterWorker(rawConstraintTypeFromDeclaration);
            }

            rawConstraintTypeFromDeclaration ??= _ch._unknownType;
            var originalConstraint = _ch.InstantiateType(rawConstraintTypeFromDeclaration, mapped._mapper);

            Node? originalConstraintNode = null;
            if ((originalConstraint._flags & TypeFlags.Unknown) == 0)
            {
                originalConstraintNode = TypeToTypeNode(originalConstraint);
            }

            return _f.NewConditionalTypeNode(
                TypeToTypeNode(_ch.GetModifiersTypeFromMappedType(t)),
                _f.NewInferTypeNode(_f.NewTypeParameterDeclaration(null, newTypeVariable!.AsTypeReferenceNode()!.TypeName!.Clone(_f), originalConstraintNode, null, null)),
                result,
                _f.NewKeywordTypeNode(Kind.NeverKeyword));
        }
        else if (needsModifierPreservingWrapper)
        {
            // and step 3: once the mapped type is reconstructed, create a `ConstraintType extends infer T_1 extends keyof ModifiersType ? {[K in T_1]: Template} : never`
            // subtly different from the `keyof` constraint case, by including the `keyof` constraint on the `infer` type parameter, it doesn't rely on the constraint type being itself
            // constrained to a `keyof` type to preserve its modifier-preserving behavior. This is all basically because we preserve modifiers for a wider set of mapped types than
            // just homomorphic ones.
            return _f.NewConditionalTypeNode(
                TypeToTypeNode(_ch.GetConstraintTypeFromMappedType(t)),
                _f.NewInferTypeNode(_f.NewTypeParameterDeclaration(null, newTypeVariable!.AsTypeReferenceNode()!.TypeName!.Clone(_f), _f.NewTypeOperatorNode(Kind.KeyOfKeyword, TypeToTypeNode(_ch.GetModifiersTypeFromMappedType(t))), null, null)),
                result,
                _f.NewKeywordTypeNode(Kind.NeverKeyword));
        }

        return result;
    }

    internal Node? TypePredicateToTypePredicateNode(TypePredicate predicate)
    {
        Node? assertsModifier = null;
        if (predicate._kind == TypePredicateKind.AssertsIdentifier || predicate._kind == TypePredicateKind.AssertsThis)
        {
            assertsModifier = _f.NewToken(Kind.AssertsKeyword);
        }

        Node? parameterName;
        if (predicate._kind == TypePredicateKind.Identifier || predicate._kind == TypePredicateKind.AssertsIdentifier)
        {
            parameterName = _f.NewIdentifier(predicate._parameterName);
            _e.AddEmitFlags(parameterName, EmitFlags.NoAsciiEscaping);
        }
        else
        {
            parameterName = _f.NewThisTypeNode();
        }

        Node? typeNode = null;
        if (predicate._t is not null)
        {
            typeNode = TypeToTypeNode(predicate._t);
        }

        return _f.NewTypePredicateNode(
            assertsModifier,
            parameterName,
            typeNode);
    }

    internal Node? TypeToTypeNodeHelperWithPossibleReusableTypeNode(Type? t, Node? typeNode)
    {
        if (t is null)
        {
            return _f.NewKeywordTypeNode(Kind.AnyKeyword);
        }

        if (!IsActivelyExpanding() && typeNode is not null && GetTypeFromTypeNode(typeNode, false) == t)
        {
            var reused = TryReuseExistingNodeHelper(typeNode);
            if (reused is not null)
            {
                CheckTypeExpandability(t);
                return reused;
            }
        }

        return TypeToTypeNode(t);
    }

    internal Node? TypeParameterToDeclaration(Type parameter)
    {
        var constraint = _ch.GetConstraintOfTypeParameterWorker(parameter);
        Node? constraintNode = null;
        if (constraint is not null)
        {
            constraintNode = TypeToTypeNodeHelperWithPossibleReusableTypeNode(constraint, _ch.GetConstraintDeclaration(parameter));
        }

        return TypeParameterToDeclarationWithConstraint(parameter, constraintNode);
    }

    internal Node[] SymbolToTypeParameterDeclarations(Symbol symbol)
    {
        return TypeParametersToTypeParameterDeclarations(symbol);
    }

    internal Node[] TypeParametersToTypeParameterDeclarations(Symbol symbol)
    {
        var targetSymbol = _ch.GetTargetSymbolWorker(symbol);
        if ((targetSymbol.Flags & (SymbolFlags.Class | SymbolFlags.Interface | SymbolFlags.Alias)) != 0)
        {
            var results = new List<Node>();
            var @params = _ch.GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(symbol);
            foreach (var param in @params)
            {
                results.Add(TypeParameterToDeclaration(param)!);
            }

            return results.ToArray();
        }
        else if ((targetSymbol.Flags & SymbolFlags.Function) != 0)
        {
            var results = new List<Node>();
            foreach (var param in _ch.GetTypeParametersFromDeclaration(symbol.ValueDeclaration!))
            {
                results.Add(TypeParameterToDeclaration(param)!);
            }

            return results.ToArray();
        }

        return [];
    }

    internal Node? SymbolToParameterDeclaration(Symbol parameterSymbol, bool preserveModifierFlags)
    {
        var parameterDeclaration = GetEffectiveParameterDeclaration(parameterSymbol);

        var parameterType = _ch.GetTypeOfSymbolWorker(parameterSymbol);
        var parameterTypeNode = SerializeTypeForDeclaration(parameterDeclaration, parameterType, parameterSymbol, true);
        ModifierList? modifiers = null;
        if ((_ctx!._flags & NodeBuilderFlags.OmitParameterModifiers) == 0 && preserveModifierFlags && parameterDeclaration is not null && CanHaveModifiers(parameterDeclaration))
        {
            var originals = Filter(parameterDeclaration.ModifierNodes(), IsModifier);
            var clones = Map(originals, node => node.Clone(_f)!);
            if (clones.Length > 0)
            {
                modifiers = _f.NewModifierList(clones);
            }
        }

        var isRest = parameterDeclaration is not null && IsRestParameter(parameterDeclaration) || (parameterSymbol.CheckFlags & CheckFlags.RestParameter) != 0;
        Node? dotDotDotToken = null;
        if (isRest)
        {
            dotDotDotToken = _f.NewToken(Kind.DotDotDotToken);
        }

        var name = ParameterToParameterDeclarationName(parameterSymbol, parameterDeclaration);
        var isOptional = parameterDeclaration is not null && _ch.IsOptionalParameter(parameterDeclaration) || (parameterSymbol.CheckFlags & CheckFlags.OptionalParameter) != 0;
        Node? questionToken = null;
        if (isOptional)
        {
            questionToken = _f.NewToken(Kind.QuestionToken);
        }

        var parameterNode = _f.NewParameterDeclaration(
            modifiers,
            dotDotDotToken,
            name,
            questionToken,
            parameterTypeNode,
            /*initializer*/ null);
        _ctx!._approximateLength += parameterSymbol.Name.Length + 3;
        return parameterNode;
    }

    internal Node? ParameterToParameterDeclarationName(Symbol parameterSymbol, Node? parameterDeclaration)
    {
        if (parameterDeclaration is null || parameterDeclaration.Name() is null)
        {
            return NewIdentifier(parameterSymbol.Name, parameterSymbol);
        }

        var name = parameterDeclaration.Name()!;
        switch (name.Kind)
        {
            case Kind.Identifier:
            {
                var cloned = _f.DeepCloneNode(name)!;
                _e.SetEmitFlags(cloned, EmitFlags.NoAsciiEscaping);
                _idToSymbol[cloned] = parameterSymbol;
                return cloned;
            }

            case Kind.QualifiedName:
            {
                var cloned = _f.DeepCloneNode(name.AsQualifiedName()!.Right!)!;
                _e.SetEmitFlags(cloned, EmitFlags.NoAsciiEscaping);
                _idToSymbol[cloned] = parameterSymbol;
                return cloned;
            }

            default:
                return CloneBindingName(name);
        }
    }

    internal Node? CloneBindingName(Node node)
    {
        if (IsComputedPropertyName(node) && _ch.IsLateBindableName(node))
        {
            TrackComputedName(node.Expression()!, _ctx!._enclosingDeclaration);
        }

        var visited = _cloneBindingNameVisitor.VisitEachChild(node);

        if (IsBindingElement(visited!))
        {
            var bindingElement = visited!.AsBindingElement()!;
            visited = _f.UpdateBindingElement(
                bindingElement,
                bindingElement.DotDotDotToken,
                bindingElement.PropertyName,
                bindingElement.Name(),
                null); // remove initializer
        }

        if (!NodeIsSynthesized(visited!))
        {
            visited = _f.DeepCloneNode(visited!);
        }

        _e.SetEmitFlags(visited!, EmitFlags.SingleLine | EmitFlags.NoAsciiEscaping);
        return visited;
    }

    internal Node? SerializeTypeForExpression(Node expr)
    {
        // !!! TODO: shim, add node reuse
        var t = _ch.InstantiateType(_ch.GetWidenedTypeWorker(_ch.GetRegularTypeOfExpression(expr)), _ctx!._mapper);
        return TypeToTypeNode(t);
    }

    internal Node? SerializeInferredReturnTypeForSignature(Signature signature, Type returnType)
    {
        var oldSuppressReportInferenceFallback = _ctx!._suppressReportInferenceFallback;
        _ctx!._suppressReportInferenceFallback = true;
        var typePredicate = _ch.GetTypePredicateOfSignatureWorker(signature);
        Node? returnTypeNode;
        if (typePredicate is not null)
        {
            TypePredicate predicate;
            if (_ctx!._mapper is not null)
            {
                predicate = _ch.InstantiateTypePredicate(typePredicate, _ctx!._mapper);
            }
            else
            {
                predicate = typePredicate;
            }

            returnTypeNode = TypePredicateToTypePredicateNodeHelper(predicate);
        }
        else
        {
            returnTypeNode = TypeToTypeNode(returnType);
        }

        _ctx!._suppressReportInferenceFallback = oldSuppressReportInferenceFallback;
        return returnTypeNode;
    }

    internal Node? TypePredicateToTypePredicateNodeHelper(TypePredicate typePredicate)
    {
        Node? assertsModifier;
        if (typePredicate._kind == TypePredicateKind.AssertsThis || typePredicate._kind == TypePredicateKind.AssertsIdentifier)
        {
            assertsModifier = _f.NewToken(Kind.AssertsKeyword);
        }
        else
        {
            assertsModifier = null;
        }

        Node? parameterName;
        if (typePredicate._kind == TypePredicateKind.Identifier || typePredicate._kind == TypePredicateKind.AssertsIdentifier)
        {
            parameterName = NewIdentifier(typePredicate._parameterName, null /*symbol*/);
            _e.SetEmitFlags(parameterName, EmitFlags.NoAsciiEscaping);
        }
        else
        {
            parameterName = _f.NewThisTypeNode();
        }

        Node? typeNode = null;
        if (typePredicate._t is not null)
        {
            typeNode = TypeToTypeNode(typePredicate._t);
        }

        return _f.NewTypePredicateNode(assertsModifier, parameterName, typeNode);
    }

    internal Node? SignatureToSignatureDeclarationHelper(Signature signature, Kind kind, SignatureToSignatureDeclarationOptions? options)
    {
        var typeParameters = new List<Node>();

        var cleanup = EnterSignatureScope(signature, out var expandedParams);
        _ctx!._approximateLength += 3;
        // Usually a signature contributes a few more characters than this, but 3 is the minimum

        if ((_ctx!._flags & NodeBuilderFlags.WriteTypeArgumentsOfSignature) != 0 && signature._target is not null && signature._mapper is not null && (signature._target._typeParameters?.Length ?? 0) != 0)
        {
            // Go: range over a nil slice is a no-op.
            foreach (var parameter in signature._target._typeParameters ?? [])
            {
                typeParameters.Add(TypeToTypeNode(_ch.InstantiateType(parameter, signature._mapper))!);
            }
        }
        else
        {
            foreach (var parameter in signature._typeParameters ?? [])
            {
                typeParameters.Add(TypeParameterToDeclaration(parameter)!);
            }
        }

        var restoreFlags = SaveRestoreFlags();
        _ctx!._flags &= ~NodeBuilderFlags.SuppressAnyReturnType;
        // If the expanded parameter list had a variadic in a non-trailing position, don't expand it
        var parameters = Map(IfElse(Some(expandedParams, p => p != expandedParams[expandedParams.Length - 1] && (p.CheckFlags & CheckFlags.RestParameter) != 0), signature._parameters, expandedParams), parameter => SymbolToParameterDeclaration(parameter, kind == Kind.Constructor)!);
        Node? thisParameter;
        if ((_ctx!._flags & NodeBuilderFlags.OmitThisParameter) != 0)
        {
            thisParameter = null;
        }
        else
        {
            thisParameter = TryGetThisParameterDeclaration(signature);
        }

        if (thisParameter is not null)
        {
            var withThis = new List<Node>(parameters.Length + 1) { thisParameter };
            withThis.AddRange(parameters);
            parameters = withThis.ToArray();
        }

        restoreFlags();

        var returnTypeNode = SerializeReturnTypeForSignature(signature, true);

        var modifiers = options?._modifiers;
        if (kind == Kind.ConstructorType && (signature._flags & SignatureFlags.Abstract) != 0)
        {
            var flags = ModifiersToFlags(modifiers);
            modifiers = CreateModifiersFromModifierFlags(flags | ModifierFlags.Abstract, _f.NewModifier);
        }

        var paramList = _f.NewNodeList(parameters);
        NodeList? typeParamList = null;
        if (typeParameters.Count != 0)
        {
            typeParamList = _f.NewNodeList(typeParameters.ToArray());
        }

        ModifierList? modifierList = null;
        if (modifiers is not null && modifiers.Length > 0)
        {
            modifierList = _f.NewModifierList(modifiers);
        }

        var name = options?._name;
        name ??= _f.NewIdentifier("");

        Node node;
        if (kind == Kind.CallSignature)
        {
            node = _f.NewCallSignatureDeclaration(typeParamList, paramList, returnTypeNode);
        }
        else if (kind == Kind.ConstructSignature)
        {
            node = _f.NewConstructSignatureDeclaration(typeParamList, paramList, returnTypeNode);
        }
        else if (kind == Kind.MethodSignature)
        {
            Node? questionToken = null;
            if (options is not null)
            {
                questionToken = options._questionToken;
            }

            node = _f.NewMethodSignatureDeclaration(modifierList, name, questionToken, typeParamList, paramList, returnTypeNode);
        }
        else if (kind == Kind.MethodDeclaration)
        {
            node = _f.NewMethodDeclaration(modifierList, null /*asteriskToken*/, name, null /*questionToken*/, typeParamList, paramList, returnTypeNode, null /*fullSignature*/, null /*body*/);
        }
        else if (kind == Kind.Constructor)
        {
            node = _f.NewConstructorDeclaration(modifierList, null /*typeParamList*/, paramList, null /*returnTypeNode*/, null /*fullSignature*/, null /*body*/);
        }
        else if (kind == Kind.GetAccessor)
        {
            node = _f.NewGetAccessorDeclaration(modifierList, name, null /*typeParamList*/, paramList, returnTypeNode, null /*fullSignature*/, null /*body*/);
        }
        else if (kind == Kind.SetAccessor)
        {
            node = _f.NewSetAccessorDeclaration(modifierList, name, null /*typeParamList*/, paramList, null /*returnTypeNode*/, null /*fullSignature*/, null /*body*/);
        }
        else if (kind == Kind.IndexSignature)
        {
            node = _f.NewIndexSignatureDeclaration(modifierList, paramList, returnTypeNode);
        }
        // !!! JSDoc Support
        // else if (kind == ast.KindJSDocFunctionType) node = b.f.NewJSDocFunctionType(parameters, returnTypeNode);
        else if (kind == Kind.FunctionType)
        {
            returnTypeNode ??= _f.NewTypeReferenceNode(_f.NewIdentifier(""), null);
            node = _f.NewFunctionTypeNode(typeParamList, paramList, returnTypeNode);
        }
        else if (kind == Kind.ConstructorType)
        {
            returnTypeNode ??= _f.NewTypeReferenceNode(_f.NewIdentifier(""), null);
            node = _f.NewConstructorTypeNode(modifierList, typeParamList, paramList, returnTypeNode);
        }
        else if (kind == Kind.FunctionDeclaration)
        {
            // TODO: assert name is Identifier
            node = _f.NewFunctionDeclaration(modifierList, null /*asteriskToken*/, name, typeParamList, paramList, returnTypeNode, null /*fullSignature*/, null /*body*/);
        }
        else if (kind == Kind.FunctionExpression)
        {
            // TODO: assert name is Identifier
            node = _f.NewFunctionExpression(modifierList, null /*asteriskToken*/, name, typeParamList, paramList, returnTypeNode, null /*fullSignature*/, _f.NewBlock(_f.NewNodeList([]), false));
        }
        else if (kind == Kind.ArrowFunction)
        {
            node = _f.NewArrowFunction(modifierList, typeParamList, paramList, returnTypeNode, null /*fullSignature*/, null /*equalsGreaterThanToken*/, _f.NewBlock(_f.NewNodeList([]), false));
        }
        else
        {
            Debug.Fail("Unhandled kind in signatureToSignatureDeclarationHelper");
            return null;
        }

        // !!! TODO: Smuggle type arguments of signatures out for quickinfo
        // if typeArguments != nil { node.TypeArguments = b.f.NewNodeList(typeArguments) }

        cleanup();
        return node;
    }

    internal Node? TryGetThisParameterDeclaration(Signature signature)
    {
        if (signature._thisParameter is not null)
        {
            return SymbolToParameterDeclaration(signature._thisParameter, false);
        }

        if (signature._declaration is not null && IsInJSFile(signature._declaration))
        {
            // !!! JSDoc Support
            // thisTag := getJSDocThisTag(signature.declaration)
            // if (thisTag && thisTag.typeExpression) { ... }
        }

        return null;
    }

    /// <summary>
    /// Serializes the return type of the signature by first trying to use the syntactic printer if possible and falling back to the checker type if not.
    /// </summary>
    internal Node? SerializeReturnTypeForSignature(Signature signature, bool tryReuse)
    {
        var suppressAny = (_ctx!._flags & NodeBuilderFlags.SuppressAnyReturnType) != 0;
        var restoreFlags = SaveRestoreFlags();
        if (suppressAny)
        {
            _ctx!._flags &= ~NodeBuilderFlags.SuppressAnyReturnType; // suppress only toplevel `any`s
        }

        Node? returnTypeNode = null;

        Type returnType;
        if (signature._declaration is not null && !NodeIsSynthesized(signature._declaration))
        {
            var symbol = _ch.GetSymbolOfDeclaration(signature._declaration);
            if (!_ctx!._enclosingSymbolTypes.TryGetValue(GetSymbolId(symbol!), out var symbolType) || symbolType is null)
            {
                returnType = _ch.InstantiateType(_ch.GetReturnTypeOfSignatureWorker(signature), _ctx!._mapper);
            }
            else
            {
                returnType = symbolType;
            }
        }
        else
        {
            returnType = _ch.GetReturnTypeOfSignatureWorker(signature);
        }

        if (!(suppressAny && IsTypeAny(returnType)))
        {
            if (!IsActivelyExpanding() && tryReuse && _ctx!._enclosingDeclaration is not null && signature._declaration is not null && !NodeIsSynthesized(signature._declaration))
            {
                var declarationSymbol = _ch.GetSymbolOfDeclaration(signature._declaration);
                var restore = AddSymbolTypeToContext(declarationSymbol!, returnType);
                var pt = _pc.GetReturnTypeOfSignature(signature._declaration);
                if (PseudoTypeEquivalentToType(pt, returnType, false, !_ctx!._suppressReportInferenceFallback))
                {
                    // Also verify the pseudo type captures any inferred type predicate, not just the boolean return type.
                    // The pseudochecker is unaware of inferred type predicates, so it produces boolean where
                    // the checker infers e.g. `x is string`.
                    var typePredicate = _ch.GetTypePredicateOfSignatureWorker(signature);
                    if (typePredicate is not null && !PseudoReturnTypeMatchesPredicate(pt, typePredicate))
                    {
                        if (!_ctx!._suppressReportInferenceFallback)
                        {
                            _ctx!._tracker.ReportInferenceFallback(signature._declaration);
                        }

                        pt = null!;
                    }

                    if (pt is not null)
                    {
                        // !!! TODO: If annotated type node is a reference with insufficient type arguments, we should still fall back to type serialization
                        // see: canReuseTypeNodeAnnotation in strada for context
                        returnTypeNode = PseudoTypeToNodeWithCheckerFallback(pt, returnType);
                    }
                }

                restore();
            }

            returnTypeNode ??= SerializeInferredReturnTypeForSignature(signature, returnType);
        }

        if (returnTypeNode is null && !suppressAny)
        {
            returnTypeNode = _f.NewKeywordTypeNode(Kind.AnyKeyword);
        }

        restoreFlags();
        return returnTypeNode;
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func getEffectiveParameterDeclaration(symbol *ast.Symbol) *ast.Node</c>（nodebuilderimpl.go:1715）。</summary>
    internal static Node? GetEffectiveParameterDeclaration(Symbol symbol)
    {
        var parameterDeclaration = GetDeclarationOfKind(symbol, Kind.Parameter);
        if (parameterDeclaration is not null)
        {
            return parameterDeclaration;
        }

        if ((symbol.Flags & SymbolFlags.Transient) == 0)
        {
            return GetDeclarationOfKind(symbol, Kind.JSDocParameterTag);
        }

        return null;
    }

    /// <summary>
    /// Go: <c>func (c *Checker) getExpandedParameters(sig *Signature, skipUnionExpanding bool) [][]*ast.Symbol</c>
    /// （nodebuilderimpl.go:1984）。同名冲突：Go 导出的 `GetExpandedParameters`（exports.go:255）保留原名。
    /// </summary>
    internal Symbol[][] GetExpandedParametersWorker(Signature sig, bool skipUnionExpanding)
    {
        if (SignatureHasRestParameter(sig))
        {
            var restIndex = sig._parameters!.Length - 1;
            var restSymbol = sig._parameters![restIndex];
            var restType = GetTypeOfSymbolWorker(restSymbol);

            string[] GetUniqAssociatedNamesFromTupleType(Type t, Symbol restSymbol2)
            {
                var names = MapIndex(t.Target()!.AsTupleType()!._elementInfos, (info, i) => GetTupleElementLabel(info, restSymbol2, i));
                if (names.Length > 0)
                {
                    var duplicates = new List<int>();
                    var uniqueNames = new HashSet<string>();
                    for (var i = 0; i < names.Length; i++)
                    {
                        var name = names[i];
                        if (!uniqueNames.Add(name))
                        {
                            duplicates.Add(i);
                        }
                    }

                    var counters = new Dictionary<string, int>();
                    foreach (var i in duplicates)
                    {
                        if (!counters.TryGetValue(names[i], out var counter))
                        {
                            counter = 1;
                        }

                        string name2;
                        while (true)
                        {
                            name2 = $"{names[i]}_{counter}";
                            if (uniqueNames.Contains(name2))
                            {
                                counter++;
                                continue;
                            }
                            else
                            {
                                uniqueNames.Add(name2);
                                break;
                            }
                        }

                        names[i] = name2;
                        counters[names[i]] = counter + 1;
                    }
                }

                return names;
            }

            Symbol[] ExpandSignatureParametersWithTupleMembers(Type restType2, int restIndex2, Symbol restSymbol2)
            {
                var elementTypes = GetTypeArgumentsWorker(restType2);
                var associatedNames = GetUniqAssociatedNamesFromTupleType(restType2, restSymbol2);
                var restParams = MapIndex(elementTypes, (t, i) =>
                {
                    // Lookup the label from the individual tuple passed in before falling back to the signature `rest` parameter name
                    // TODO: getTupleElementLabel can no longer fail, investigate if this lack of falliability meaningfully changes output
                    var name = associatedNames[i];
                    var flags = restType2.Target()!.AsTupleType()!._elementInfos![i]._flags;
                    var checkFlags = CheckFlags.None;
                    if ((flags & ElementFlags.Variable) != 0)
                    {
                        checkFlags = CheckFlags.RestParameter;
                    }
                    else if ((flags & ElementFlags.Optional) != 0)
                    {
                        checkFlags = CheckFlags.OptionalParameter;
                    }

                    var symbol = NewSymbolEx(SymbolFlags.FunctionScopedVariable, name, checkFlags);
                    var links = _valueSymbolLinks.Get(symbol);
                    if ((flags & ElementFlags.Rest) != 0)
                    {
                        links._resolvedType = CreateArrayType(t);
                    }
                    else
                    {
                        links._resolvedType = t;
                    }

                    return symbol;
                });
                return Concatenate(sig._parameters![..restIndex2], restParams);
            }

            if (IsTupleType(restType))
            {
                return [ExpandSignatureParametersWithTupleMembers(restType, restIndex, restSymbol)];
            }
            else if (!skipUnionExpanding && (restType._flags & TypeFlags.Union) != 0 && Every(restType.AsUnionType()!._types!, IsTupleType))
            {
                return Map(restType.AsUnionType()!._types!, t => ExpandSignatureParametersWithTupleMembers(t, restIndex, restSymbol));
            }
        }

        return [sig._parameters!];
    }
}

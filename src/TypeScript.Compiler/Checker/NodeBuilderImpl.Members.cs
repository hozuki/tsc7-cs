// ported from: internal/checker/nodebuilderimpl.go:2150-2718（声明序列化、属性名节点、元素列表构造）
//
// 与 Go 的差异（逐条）：
// 1) Go 的函数内局部类型 `propertyNameNodeKind`（int + iota 常量）→ 文件级 `internal enum PropertyNameNodeKind`
//    （成员名 = Go 常量名去 `propertyNameNodeKind` 前缀，数值一致）。
// 2) Go 未导出字段 → `_camelCase`（§5.5）；同名方法冲突用 Worker 名（`symbolToString`→`SymbolToStringWorker`、
//    `getTypeOfSymbol`→`GetTypeOfSymbolWorker`、`isReadonlySymbol`→`IsReadonlySymbolWorker`、
//    `getTypeFromTypeNode`→`GetTypeFromTypeNodeWorker`、`getSignatureFromDeclaration`→`GetSignatureFromDeclarationWorker`）。
// 3) `pseudochecker.PseudoTypeKindNoResult` → `PseudoTypeKind.NoResult`；`pseudochecker.NewPseudoTypeUnion` → `PseudoType.NewPseudoTypeUnion`；
//    `pseudochecker.PseudoTypeUndefined` → `global::TypeScript.PseudoChecker.PseudoChecker.PseudoTypeUndefined`（静态字段）。
// 4) `printer.SymbolAccessibilityAccessible` → `SymbolAccessibility.Accessible`；
//    `core.IfElse`/`core.Every`/`core.Coalesce`/`core.Find` → 同名 static。
// 5) `b.ctx.reverseMappedStack = append(...)` → `Add`；`[:len-1]` → `RemoveAt(count-1)`。
namespace TypeScript.Checker;

using System;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.JsNum;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.PseudoChecker;
using TypeScript.Scanner;
using TypeScript.StringUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Checker.Checker;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;

internal enum PropertyNameNodeKind
{
    Identifier = 0,
    NumericLiteral = 1,
    StringLiteral = 2,
}

public sealed partial class NodeBuilderImpl
{
    internal bool IsTriviallySerializableComputedName(Node? e)
    {
        var shapeGood = e is not null && e.Name() is not null && IsComputedPropertyName(e.Name()!) && IsEntityNameExpression(e.Name()!.Expression()!);
        if (!shapeGood)
        {
            return false;
        }

        // TODO: going through emit resolver here is weird. Relayer these APIs.
        return _ch.GetEmitResolver()!.IsEntityNameVisibleWorker(e!.Name()!.Expression()!, _ctx!._enclosingDeclaration!, false).Accessibility == SymbolAccessibility.Accessible;
    }

    internal Node[] IndexInfoToObjectComputedNamesOrSignatureDeclaration(IndexInfo indexInfo, Node? typeNode)
    {
        if (indexInfo._components is not null && indexInfo._components.Length > 0)
        {
            // Index info is derived from object or class computed property names (plus explicit named members) - we can clone those instead of writing out the result computed index signature
            var allComponentComputedNamesSerializable = _ctx!._enclosingDeclaration is not null && Every(indexInfo._components, IsTriviallySerializableComputedName);
            if (allComponentComputedNamesSerializable)
            {
                // Only use computed name serialization form if all components are visible and take the `a.b.c` form
                var newComponents = Filter(indexInfo._components, c =>
                    // skip late bound props that contribute to the index signature - they'll be created by property creation anyway
                    !_ch.HasLateBindableName(c));
                var bailed = false;
                var results = Map(newComponents, e =>
                {
                    var name = ReuseNode(e.Name()!);
                    if (name is not null)
                    {
                        // Still need to track visibility even if we've already checked it to paint references as used
                        TrackComputedName(e.Name()!.Expression()!, _ctx!._enclosingDeclaration);
                        ModifierList? mods = null;
                        if (indexInfo._isReadonly)
                        {
                            mods = _f.NewModifierList([_f.NewModifier(Kind.ReadonlyKeyword)]);
                        }

                        Node? postfixToken = null;
                        if (e.PostfixToken() is not null)
                        {
                            postfixToken = e.PostfixToken()!.Clone(_f);
                        }

                        Node? currentTypeNode;
                        if (typeNode is not null)
                        {
                            currentTypeNode = _f.DeepCloneNode(typeNode);
                        }
                        else
                        {
                            currentTypeNode = TypeToTypeNode(_ch.GetTypeOfSymbolWorker(e.Symbol!));
                        }

                        var sig = _f.NewPropertySignatureDeclaration(
                            mods,
                            name,
                            postfixToken,
                            currentTypeNode,
                            null);
                        sig.Loc = e.Loc;
                        return sig;
                    }

                    bailed = true;
                    return null!;
                });
                if (!bailed)
                {
                    return results;
                }
            }
        }

        return [IndexInfoToIndexSignatureDeclarationHelper(indexInfo, typeNode)];
    }

    internal Node IndexInfoToIndexSignatureDeclarationHelper(IndexInfo indexInfo, Node? typeNode)
    {
        var name = GetNameFromIndexInfo(indexInfo);
        var indexerTypeNode = TypeToTypeNode(indexInfo._keyType);

        var indexingParameter = _f.NewParameterDeclaration(null, null, NewIdentifier(name, null /*symbol*/), null, indexerTypeNode, null);
        if (typeNode is null)
        {
            if (indexInfo._valueType is null)
            {
                typeNode = _f.NewKeywordTypeNode(Kind.AnyKeyword);
            }
            else
            {
                typeNode = TypeToTypeNode(indexInfo._valueType);
            }
        }

        if (indexInfo._valueType is null && (_ctx!._flags & NodeBuilderFlags.AllowEmptyIndexInfoType) == 0)
        {
            _ctx!._encounteredError = true;
        }

        _ctx!._approximateLength += name.Length + 4;
        ModifierList? modifiers = null;
        if (indexInfo._isReadonly)
        {
            _ctx!._approximateLength += 9;
            modifiers = _f.NewModifierList([_f.NewModifier(Kind.ReadonlyKeyword)]);
        }

        return _f.NewIndexSignatureDeclaration(modifiers, _f.NewNodeList([indexingParameter]), typeNode);
    }

    /// <summary>
    /// Unlike `typeToTypeNodeHelper`, this handles setting up the `AllowUniqueESSymbolType` flag
    /// so a `unique symbol` is returned when appropriate for the input symbol, rather than `typeof sym`
    /// </summary>
    /// <param name="declaration">The preferred declaration to pull existing type nodes from (the symbol will be used as a fallback to find any annotated declaration)</param>
    /// <param name="t">The type to write; an existing annotation must match this type if it's used, otherwise this is the type serialized as a new type node</param>
    /// <param name="symbol">The symbol is used both to find an existing annotation if declaration is not provided, and to determine if `unique symbol` should be printed</param>
    internal Node? SerializeTypeForDeclaration(Node? declaration, Type? t, Symbol? symbol, bool tryReuse)
    {
        if (declaration is null)
        {
            if (symbol is not null)
            {
                declaration = symbol.ValueDeclaration;
                if (declaration is null)
                {
                    // TODO: prefer annotated declarations like in strada (but does this ever even matter in practice? All callers should supply a declaration!)
                    declaration = FirstOrNil(symbol.Declarations);
                }
            }
        }

        symbol ??= _ch.GetSymbolOfDeclaration(declaration!);
        if (t is null)
        {
            if (symbol is null)
            {
                if (IsVariableLike(declaration))
                {
                    t = _ch.GetTypeForVariableLikeDeclaration(declaration!, false, CheckMode.Normal);
                }
                else
                {
                    t = _ch._errorType;
                }
            }
            else
            {
                _ctx!._enclosingSymbolTypes.TryGetValue(GetSymbolId(symbol), out t);
                if (t is null)
                {
                    if ((symbol.Flags & SymbolFlags.Accessor) != 0 && declaration!.Kind == Kind.SetAccessor)
                    {
                        t = _ch.InstantiateType(_ch.GetWriteTypeOfSymbol(symbol), _ctx!._mapper);
                    }
                    else if (symbol is not null && (symbol.Flags & (SymbolFlags.TypeLiteral | SymbolFlags.Signature)) == 0)
                    {
                        t = _ch.InstantiateType(_ch.GetWidenedLiteralTypeWorker(_ch.GetTypeOfSymbolWorker(symbol)), _ctx!._mapper);
                    }
                    else
                    {
                        t = _ch._errorType;
                    }
                }
            }
        }

        // !!! TODO: JSDoc, getEmitResolver call is unfortunate layering for the helper - hoist it into checker
        var requiresAddingUndefined = declaration is not null && (IsParameterDeclaration(declaration) || IsPropertySignatureDeclaration(declaration) || IsPropertyDeclaration(declaration)) && _ch.GetEmitResolver().RequiresAddingImplicitUndefined(declaration, symbol!, _ctx!._enclosingDeclaration);
        var addUndefinedForParameter = requiresAddingUndefined && IsParameterDeclaration(declaration!) /*|| ast.IsJSDocParameterTag(declaration)*/;
        if (addUndefinedForParameter)
        {
            t = _ch.GetOptionalType(t!, false);
        }

        var restoreFlags = SaveRestoreFlags();
        if ((t!._flags & TypeFlags.UniqueESSymbol) != 0 && t._symbol == symbol && (_ctx!._enclosingDeclaration is null || Some(symbol!.Declarations, d => GetSourceFileOfNode(d) == _ctx!._enclosingFile)))
        {
            _ctx!._flags |= NodeBuilderFlags.AllowUniqueESSymbolType;
        }

        Node? result = null;
        var reportedInferenceFallback = false;

        // !!! expandable hover support
        if (!IsActivelyExpanding() && tryReuse && _ctx!._enclosingDeclaration is not null && declaration is not null && (IsAccessor(declaration) || (HasInferredType(declaration) && !NodeIsSynthesized(declaration) && (t!.ObjectFlags() & ObjectFlags.RequiresWidening) == 0)))
        {
            Action? remove = null;
            if (symbol is not null)
            {
                remove = AddSymbolTypeToContext(symbol, t!);
            }

            PseudoType? pt;
            if (IsAccessor(declaration))
            {
                pt = _pc.GetTypeOfAccessor(declaration);
            }
            else
            {
                pt = _pc.GetTypeOfDeclaration(declaration);
            }

            if ((pt is null || pt.Kind == PseudoTypeKind.NoResult) && IsBinaryExpression(declaration) && symbol is not null)
            {
                var decl = Find(symbol.Declarations, HasTypeAnnotation);
                if (decl is not null)
                {
                    // Binary expressions have a first-in-wins type annotation system. The first one with an annotation supplies the type for the rest.
                    pt = _pc.GetTypeOfDeclaration(decl);
                }
            }

            var reportErrors = !_ctx!._suppressReportInferenceFallback;
            if (PseudoTypeEquivalentToType(pt!, t, !requiresAddingUndefined && (IsParameterDeclaration(declaration) || IsPropertySignatureDeclaration(declaration) || IsPropertyDeclaration(declaration)) && IsOptionalDeclaration(declaration), reportErrors))
            {
                // !!! TODO: If annotated type node is a reference with insufficient type arguments, we should still fall back to type serialization
                // see: canReuseTypeNodeAnnotation in strada for context
                var ptt = PseudoTypeToType(pt!);
                if (ptt is not null && requiresAddingUndefined && ContainsNonMissingUndefinedType(_ch, t!) && !ContainsNonMissingUndefinedType(_ch, ptt))
                {
                    pt = PseudoType.NewPseudoTypeUnion([pt!, global::TypeScript.PseudoChecker.PseudoChecker.PseudoTypeUndefined]);
                }

                result = PseudoTypeToNodeWithCheckerFallback(pt!, t!);
            }
            else
            {
                // Equivalence failed; if errors from inferred-with-errors pseudo types were
                // reported, note it so we can suppress nested errors during the fallback
                // typeToTypeNode serialization (mirroring the suppression that
                // pseudoTypeToNodeWithCheckerFallback provides).
                reportedInferenceFallback = reportErrors && pt!.Kind == PseudoTypeKind.Inferred && pt.AsPseudoTypeInferred().ErrorNodes!.Length > 0;
                var shouldAddUndefined = false;
                if (requiresAddingUndefined)
                {
                    if (PseudoTypeToType(pt!) is { } ptt2)
                    {
                        shouldAddUndefined = !ContainsNonMissingUndefinedType(_ch, ptt2);
                    }
                    else
                    {
                        shouldAddUndefined = !global::TypeScript.PseudoChecker.PseudoChecker.CouldAlreadyReferToUndefinedType(pt!);
                    }
                }

                if (shouldAddUndefined)
                {
                    pt = PseudoType.NewPseudoTypeUnion([pt!, global::TypeScript.PseudoChecker.PseudoChecker.PseudoTypeUndefined]);
                    if (PseudoTypeEquivalentToType(pt, t, false, reportErrors))
                    {
                        result = PseudoTypeToNodeWithCheckerFallback(pt, t!);
                        reportedInferenceFallback = false;
                    }
                }
            }

            remove?.Invoke();
        }

        if (result is null)
        {
            if (reportedInferenceFallback)
            {
                var oldSuppress = _ctx!._suppressReportInferenceFallback;
                _ctx!._suppressReportInferenceFallback = true;
                result = TypeToTypeNode(t!);
                _ctx!._suppressReportInferenceFallback = oldSuppress;
            }
            else
            {
                result = TypeToTypeNode(t!);
            }
        }

        restoreFlags();
        if (result is null)
        {
            return _f.NewKeywordTypeNode(Kind.AnyKeyword);
        }

        return result;
    }

    internal bool ShouldUsePlaceholderForProperty(Symbol propertySymbol)
    {
        // Use placeholders for reverse mapped types we've either
        // (1) already descended into, or
        // (2) are nested reverse mappings within a mapping over a non-anonymous type, or
        // (3) are deeply nested properties that originate from the same mapped type.
        // Condition (2) is a restriction mostly just to
        // reduce the blowup in printback size from doing, eg, a deep reverse mapping over `Window`.
        // Since anonymous types usually come from expressions, this allows us to preserve the output
        // for deep mappings which likely come from expressions, while truncating those parts which
        // come from mappings over library functions.
        // Condition (3) limits printing of possibly infinitely deep reverse mapped types.
        if ((propertySymbol.CheckFlags & CheckFlags.ReverseMapped) == 0)
        {
            return false;
        }

        // (1)
        if (_ctx!._reverseMappedStack.Contains(propertySymbol))
        {
            return true;
        }

        // (2)
        if (_ctx!._reverseMappedStack.Count > 0)
        {
            var last = _ctx!._reverseMappedStack[^1];
            if (_ch.ReverseMappedSymbolLinks.Has(last))
            {
                var links = _ch.ReverseMappedSymbolLinks.TryGet(last)!;
                var propertyType = links._propertyType;
                if (propertyType is not null && (propertyType._objectFlags & ObjectFlags.Anonymous) == 0)
                {
                    return true;
                }
            }
        }

        // (3) - we only inspect the last MAX_REVERSE_MAPPED_NESTING_INSPECTION_DEPTH elements of the
        // stack for approximate matches to catch tight infinite loops
        // TODO: Why? Reasoning lost to time. this could probably stand to be improved?
        if (_ctx!._reverseMappedStack.Count < MaxReverseMappedNestingInspectionDepth)
        {
            return false;
        }

        if (!_ch.ReverseMappedSymbolLinks.Has(propertySymbol))
        {
            return false;
        }

        var propertyLinks = _ch.ReverseMappedSymbolLinks.TryGet(propertySymbol)!;
        var propMappedType = propertyLinks._mappedType;
        if (propMappedType is null || propMappedType._symbol is null)
        {
            return false;
        }

        for (var i = 0; i < _ctx!._reverseMappedStack.Count; i++)
        {
            if (i > MaxReverseMappedNestingInspectionDepth)
            {
                break;
            }

            var prop = _ctx!._reverseMappedStack[_ctx!._reverseMappedStack.Count - 1 - i];
            if (_ch.ReverseMappedSymbolLinks.Has(prop))
            {
                var links = _ch.ReverseMappedSymbolLinks.TryGet(prop)!;
                var mappedType = links._mappedType;
                if (mappedType is not null && mappedType._symbol == propMappedType._symbol)
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal void TrackComputedName(Node accessExpression, Node? enclosingDeclaration)
    {
        // get symbol of the first identifier of the entityName
        var firstIdentifier = GetFirstIdentifier(accessExpression);
        var name = _ch._resolveName(enclosingDeclaration, firstIdentifier!.Text(), SymbolFlags.Value | SymbolFlags.ExportValue, null! /*nameNotFoundMessage*/, true /*isUse*/, false);
        if (name is not null)
        {
            _ctx!._tracker.TrackSymbol(name, enclosingDeclaration!, SymbolFlags.Value);
        }
        else
        {
            // Name does not resolve at target location, track symbol at dest location (should be inaccessible)
            var fallback = _ch._resolveName(firstIdentifier, firstIdentifier.Text(), SymbolFlags.Value | SymbolFlags.ExportValue, null! /*nameNotFoundMessage*/, true /*isUse*/, false);
            if (fallback is not null)
            {
                _ctx!._tracker.TrackSymbol(fallback, enclosingDeclaration!, SymbolFlags.Value);
            }
        }
    }

    internal Node? CreatePropertyNameNodeForIdentifierOrLiteral(string name, bool singleQuote, bool stringNamed, bool isMethod, Symbol? symbol)
    {
        switch (ClassifyPropertyName(name, stringNamed, isMethod))
        {
            case PropertyNameNodeKind.Identifier:
                return NewIdentifier(name, symbol);
            case PropertyNameNodeKind.NumericLiteral:
                return _f.NewNumericLiteral(name, TokenFlags.None);
            default:
                return _f.NewStringLiteral(name, IfElse(singleQuote, TokenFlags.SingleQuote, TokenFlags.None));
        }
    }

    internal bool IsStringNamed(Node d)
    {
        var name = GetNameOfDeclaration(d);
        if (name is null)
        {
            return false;
        }

        if (IsComputedPropertyName(name))
        {
            var t = _ch.CheckExpression(name.Expression()!);
            return (t._flags & TypeFlags.StringLike) != 0;
        }

        if (IsElementAccessExpression(name))
        {
            var t = _ch.CheckExpression(name.AsElementAccessExpression()!.ArgumentExpression!);
            return (t._flags & TypeFlags.StringLike) != 0;
        }

        return IsStringLiteral(name);
    }

    internal bool IsSingleQuotedStringNamed(Node d)
    {
        var name = GetNameOfDeclaration(d);
        return name is not null && IsStringLiteral(name) && (name.AsStringLiteral()!.TokenFlags & TokenFlags.SingleQuote) != 0;
    }

    internal Node? GetPropertyNameNodeForSymbol(Symbol symbol, Node? enclosingDeclaration)
    {
        // For hash-private names, clone the original private identifier from the declaration
        if (symbol.ValueDeclaration is not null)
        {
            var declName = symbol.ValueDeclaration.Name();
            if (declName is not null && IsPrivateIdentifier(declName))
            {
                return _f.DeepCloneNode(declName);
            }
        }

        var stringNamed = (symbol.Declarations?.Length ?? 0) != 0 && Every(symbol.Declarations, IsStringNamed);
        var singleQuote = (symbol.Declarations?.Length ?? 0) != 0 && Every(symbol.Declarations, IsSingleQuotedStringNamed);
        var isMethod = (symbol.Flags & SymbolFlags.Method) != 0;
        var fromNameType = GetPropertyNameNodeForSymbolFromNameType(symbol, enclosingDeclaration, singleQuote, stringNamed, isMethod);
        if (fromNameType is not null)
        {
            return fromNameType;
        }

        var name = symbol.Name;
        const string privateNamePrefix = InternalSymbolNamePrefix + "#";
        if (name.StartsWith(privateNamePrefix))
        {
            // symbol IDs are unstable - replace #nnn# with #private#
            name = name[privateNamePrefix.Length..];
            var digitEnd = 0;
            while (digitEnd < name.Length && IsDigit(name[digitEnd]))
            {
                digitEnd++;
            }

            name = "__#private" + name[digitEnd..];
        }

        return CreatePropertyNameNodeForIdentifierOrLiteral(name, singleQuote, stringNamed, isMethod, symbol);
    }

    // See getNameForSymbolFromNameType for a stringy equivalent
    internal Node? GetPropertyNameNodeForSymbolFromNameType(Symbol symbol, Node? enclosingDeclaration, bool singleQuote, bool stringNamed, bool isMethod)
    {
        if (!_ch._valueSymbolLinks.Has(symbol))
        {
            return null;
        }

        var nameType = _ch._valueSymbolLinks.TryGet(symbol)!._nameType;
        if (nameType is null)
        {
            return null;
        }

        var enumEnclosingDeclaration = enclosingDeclaration;
        if (enumEnclosingDeclaration is null && _ctx!._enclosingFile is not null)
        {
            enumEnclosingDeclaration = _ctx!._enclosingFile.AsNode();
        }

        if ((nameType._flags & TypeFlags.EnumLiteral) != 0)
        {
            var enumSymbol = nameType._symbol!.Parent;
            enumSymbol ??= nameType._symbol;
            if (enumEnclosingDeclaration is not null &&
                _ch.IsSymbolAccessibleByFlags(enumSymbol!, enumEnclosingDeclaration, SymbolFlags.Value))
            {
                var saveEnclosingDeclaration = _ctx!._enclosingDeclaration;
                _ctx!._enclosingDeclaration = enumEnclosingDeclaration;
                var result = _f.NewComputedPropertyName(SymbolToExpression(nameType._symbol!, SymbolFlags.Value)!);
                _ctx!._enclosingDeclaration = saveEnclosingDeclaration;
                return result;
            }
        }

        if ((nameType._flags & TypeFlags.StringOrNumberLiteral) != 0)
        {
            var name = "";
            var value = nameType.AsLiteralType()!._value;
            if (value is global::TypeScript.JsNum.Number n)
            {
                name = n.String();
            }
            else if (value is string s)
            {
                name = s;
            }

            if (!IsIdentifierText(name, LanguageVariant.Standard) && (stringNamed || !IsNumericLiteralName(name)))
            {
                return _f.NewStringLiteral(name, IfElse(singleQuote, TokenFlags.SingleQuote, TokenFlags.None));
            }

            if (IsNumericLiteralName(name) && name[0] == '-')
            {
                return _f.NewComputedPropertyName(_f.NewPrefixUnaryExpression(Kind.MinusToken, _f.NewNumericLiteral(name[1..], TokenFlags.None)));
            }

            return CreatePropertyNameNodeForIdentifierOrLiteral(name, singleQuote, stringNamed, isMethod, symbol);
        }

        if ((nameType._flags & TypeFlags.UniqueESSymbol) != 0)
        {
            // The reference was tracked in the destination scope by trackComputedName.
            // Reconstructing its spelling in the source scope must not paint that scope's declarations visible.
            return _f.NewComputedPropertyName(SymbolToExpressionWorker(nameType.AsUniqueESSymbolType()!._symbol!, SymbolFlags.Value)!);
        }

        return null;
    }

    internal Node[] AddPropertyToElementList(Symbol propertySymbol, Node[] typeElements)
    {
        var propertyIsReverseMapped = (propertySymbol.CheckFlags & CheckFlags.ReverseMapped) != 0;
        Type? propertyType;
        if (ShouldUsePlaceholderForProperty(propertySymbol))
        {
            propertyType = _ch._anyType;
        }
        else
        {
            propertyType = _ch.GetNonMissingTypeOfSymbolWorker(propertySymbol);
        }

        var saveEnclosingDeclaration = _ctx!._enclosingDeclaration;
        _ctx!._enclosingDeclaration = null;
        if (IsLateBoundName(propertySymbol.Name))
        {
            if ((propertySymbol.Declarations?.Length ?? 0) > 0)
            {
                var decl = propertySymbol.Declarations![0];
                if (_ch.HasLateBindableName(decl))
                {
                    if (IsBinaryExpression(decl))
                    {
                        var name = GetNameOfDeclaration(decl);
                        if (name is not null && IsElementAccessExpression(name) && IsPropertyAccessEntityNameExpression(name.AsElementAccessExpression()!.ArgumentExpression!, false /*allowJs*/))
                        {
                            TrackComputedName(name.AsElementAccessExpression()!.ArgumentExpression!, saveEnclosingDeclaration);
                        }
                    }
                    else
                    {
                        TrackComputedName(decl.Name()!.Expression()!, saveEnclosingDeclaration);
                    }
                }
            }
            else
            {
                _ctx!._tracker.ReportNonSerializableProperty(_ch.SymbolToStringWorker(propertySymbol));
            }
        }

        if (propertySymbol.ValueDeclaration is not null)
        {
            _ctx!._enclosingDeclaration = propertySymbol.ValueDeclaration;
        }
        else if ((propertySymbol.Declarations?.Length ?? 0) > 0 && propertySymbol.Declarations![0] is not null)
        {
            _ctx!._enclosingDeclaration = propertySymbol.Declarations[0];
        }
        else
        {
            _ctx!._enclosingDeclaration = saveEnclosingDeclaration;
        }

        var propertyName = GetPropertyNameNodeForSymbol(propertySymbol, saveEnclosingDeclaration);
        _ctx!._enclosingDeclaration = saveEnclosingDeclaration;
        _ctx!._approximateLength += SymbolName(propertySymbol).Length + 1;

        if ((propertySymbol.Flags & SymbolFlags.Accessor) != 0)
        {
            var writeType = _ch.GetWriteTypeOfSymbol(propertySymbol);
            if (!_ch.IsErrorType(propertyType!) && !_ch.IsErrorType(writeType))
            {
                var propDeclaration = GetDeclarationOfKind(propertySymbol, Kind.PropertyDeclaration);
                if (propertyType != writeType || propertySymbol.Parent is not null && (propertySymbol.Parent.Flags & SymbolFlags.Class) != 0 && propDeclaration is null)
                {
                    var symbolMapper = _ch._valueSymbolLinks.Get(propertySymbol)._mapper;
                    var getterDeclaration = GetDeclarationOfKind(propertySymbol, Kind.GetAccessor);
                    if (getterDeclaration is not null)
                    {
                        var getterSignature = _ch.GetSignatureFromDeclarationWorker(getterDeclaration);
                        if (symbolMapper is not null)
                        {
                            getterSignature = _ch.InstantiateSignature(getterSignature, symbolMapper);
                        }

                        var getter = SignatureToSignatureDeclarationHelper(getterSignature, Kind.GetAccessor, new SignatureToSignatureDeclarationOptions
                        {
                            _name = propertyName,
                        });
                        SetCommentRange(getter!, getterDeclaration);
                        typeElements = Append(typeElements, getter!);
                    }

                    var setterDeclaration = GetDeclarationOfKind(propertySymbol, Kind.SetAccessor);
                    if (setterDeclaration is not null)
                    {
                        var setterSignature = _ch.GetSignatureFromDeclarationWorker(setterDeclaration);
                        if (symbolMapper is not null)
                        {
                            setterSignature = _ch.InstantiateSignature(setterSignature, symbolMapper);
                        }

                        var setter = SignatureToSignatureDeclarationHelper(setterSignature, Kind.SetAccessor, new SignatureToSignatureDeclarationOptions
                        {
                            _name = propertyName,
                        });
                        SetCommentRange(setter!, setterDeclaration);
                        typeElements = Append(typeElements, setter!);
                    }

                    return typeElements;
                }
                else if (propertySymbol.Parent is not null && (propertySymbol.Parent.Flags & SymbolFlags.Class) != 0 && propDeclaration is not null && Find(propDeclaration.ModifierNodes(), m => m.Kind == Kind.AccessorKeyword) is not null)
                {
                    var fakeGetterSignature = _ch.NewSignature(SignatureFlags.None, null!, null!, null!, null!, propertyType, null, 0);
                    var fakeGetterDeclaration = SignatureToSignatureDeclarationHelper(fakeGetterSignature, Kind.GetAccessor, new SignatureToSignatureDeclarationOptions
                    {
                        _name = propertyName,
                    });
                    SetCommentRange(fakeGetterDeclaration!, propDeclaration);
                    typeElements = Append(typeElements, fakeGetterDeclaration!);

                    var setterParam = _ch.NewSymbol(SymbolFlags.FunctionScopedVariable, "arg");
                    _ch._valueSymbolLinks.Get(setterParam)._resolvedType = writeType;
                    var fakeSetterSignature = _ch.NewSignature(SignatureFlags.None, null!, null!, null!, [setterParam], _ch._voidType, null, 0);
                    var fakeSetterDeclaration = SignatureToSignatureDeclarationHelper(fakeSetterSignature, Kind.SetAccessor, new SignatureToSignatureDeclarationOptions
                    {
                        _name = propertyName,
                    });
                    typeElements = Append(typeElements, fakeSetterDeclaration!);
                    return typeElements;
                }
            }
        }

        Node? optionalToken;
        if ((propertySymbol.Flags & SymbolFlags.Optional) != 0)
        {
            optionalToken = _f.NewToken(Kind.QuestionToken);
        }
        else
        {
            optionalToken = null;
        }

        if ((propertySymbol.Flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0 && (_ch.GetPropertiesOfObjectType(propertyType!)?.Length ?? 0) == 0 && !_ch.IsReadonlySymbolWorker(propertySymbol))
        {
            var signatures = _ch.GetSignaturesOfTypeWorker(_ch.FilterType(propertyType!, t => (t._flags & TypeFlags.Undefined) == 0), SignatureKind.Call) ?? [];
            foreach (var signature in signatures)
            {
                var methodDeclaration = SignatureToSignatureDeclarationHelper(signature, Kind.MethodSignature, new SignatureToSignatureDeclarationOptions
                {
                    _name = propertyName,
                    _questionToken = optionalToken,
                });
                SetCommentRange(methodDeclaration!, Coalesce(signature._declaration, propertySymbol.ValueDeclaration)!);
                typeElements = Append(typeElements, methodDeclaration!);
            }

            if (signatures.Length != 0 || optionalToken is null)
            {
                return typeElements;
            }
        }

        Node? propertyTypeNode;
        if (ShouldUsePlaceholderForProperty(propertySymbol))
        {
            propertyTypeNode = CreateElidedInformationPlaceholder();
        }
        else
        {
            if (propertyIsReverseMapped)
            {
                _ctx!._reverseMappedStack.Add(propertySymbol);
            }

            if (propertyType is not null)
            {
                propertyTypeNode = SerializeTypeForDeclaration(null /*declaration*/, propertyType, propertySymbol, true);
            }
            else
            {
                propertyTypeNode = _f.NewKeywordTypeNode(Kind.AnyKeyword);
            }

            if (propertyIsReverseMapped)
            {
                _ctx!._reverseMappedStack.RemoveAt(_ctx!._reverseMappedStack.Count - 1);
            }
        }

        ModifierList? modifiers = null;
        if (_ch.IsReadonlySymbolWorker(propertySymbol))
        {
            modifiers = _f.NewModifierList([_f.NewModifier(Kind.ReadonlyKeyword)]);
            _ctx!._approximateLength += 9;
        }

        var propertySignature = _f.NewPropertySignatureDeclaration(modifiers, propertyName, optionalToken, propertyTypeNode, null);

        SetCommentRange(propertySignature, propertySymbol.ValueDeclaration);
        return Append(typeElements, propertySignature);
    }

    /// <summary>C# 数组没有 Go 的 append 语义（无 len &lt; cap 视图）；用展开表达式分配新数组，与 Go 逐次 append 的结果一致。</summary>
    private static Node[] Append(Node[] array, Node node)
    {
        return [.. array, node];
    }
}

public sealed partial class Checker
{
    internal const int MaxReverseMappedNestingInspectionDepth = 3;

    /// <summary>Go: <c>func hasTypeAnnotation(declaration *ast.Declaration) bool</c>（nodebuilderimpl.go:2234）。</summary>
    internal static bool HasTypeAnnotation(Node? declaration)
    {
        if (declaration is null || declaration.Type() is null)
        {
            return false;
        }

        // Type alias declarations have a .Type() that is their type definition, not a type annotation on a value.
        // Exclude them so callers don't mistake them for annotated value declarations.
        if (IsTypeAliasDeclaration(declaration) || IsJSTypeAliasDeclaration(declaration))
        {
            return false;
        }

        return true;
    }

    /// <summary>Go: <c>func classifyPropertyName(name string, stringNamed bool, isMethod bool) propertyNameNodeKind</c>（nodebuilderimpl.go:2456）。</summary>
    internal static PropertyNameNodeKind ClassifyPropertyName(string name, bool stringNamed, bool isMethod)
    {
        if (isMethod && name == "new")
        {
            return PropertyNameNodeKind.StringLiteral;
        }

        if (IsIdentifierText(name, LanguageVariant.Standard))
        {
            return PropertyNameNodeKind.Identifier;
        }

        return IfElse(!stringNamed && IsNumericLiteralName(name) && global::TypeScript.JsNum.JsNum.FromString(name) >= 0, PropertyNameNodeKind.NumericLiteral, PropertyNameNodeKind.StringLiteral);
    }
}

// ported from: internal/checker/nodebuilderimpl.go:584-1400（符号→名字/表达式/类型节点的链式构造、模块说明符）
//
// 与 Go 的差异（逐条）：
// 1) 包级函数（`canUsePropertyAccess`/`startsWithSingleOrDoubleQuote`/`startsWithSquareBracket`/`isDefaultBindingContext`/
//    `canHaveModuleSpecifier`/`TryGetModuleSpecifierFromDeclaration(Worker)`/`getTopmostIndexedAccessType`）
//    → `partial class Checker` 的 static 成员（docs/checker-plan.md §5.5）。
// 2) Go 未导出字段 → `_camelCase`（§5.5）：`b._ch`/`b._e`/`b._f`/`b._ctx`、`t._flags`/`t._symbol`/`t._alias`。
// 3) 同名方法冲突（artifacts/checker-method-collisions.md）：`c.resolveAlias`→`ResolveAliasWorker`、
//    `c.getAccessibleSymbolChain`→`GetAccessibleSymbolChainWorker`、`c.getTypeOfSymbol`→`GetTypeOfSymbolWorker`、
//    `c.getMergedSymbol`→`GetMergedSymbolWorker`、`c.getTargetSymbol`→`GetTargetSymbolWorker`、
//    `c.getSymbolFlags`→`GetSymbolFlagsWorker`、`c.getExportsOfModule`→`GetExportsOfModuleWorker`。
// 4) Go 的 `strings.Contains(s, sub)` → `s.Contains(sub)`；`core.IfElse` → `IfElse`；`core.Some` → `Some`；
//    `core.FirstNonNil` → `FirstNonNil`；`slices.SortStableFunc` → `OrderBy` + `Comparer.Create`（OrderBy 是稳定排序，
//    与 Go 的稳定排序语义一致；`Array.Sort`/`List.Sort` 不稳定，输出会随排序不稳定而漂移）。
// 5) `module.ModeAwareCacheKey{Name, Mode}` → `new ModeAwareCacheKey(...)`；读缺失键用 `TryGetValue`。
// 6) Go 的 `*string`/指针语义字段无对应 → 直接读写对象字段（`links._specifierCache`）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using System.Linq;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.ModuleSpecifiers;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.Scanner;
using TypeScript.StringUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;
using static TypeScript.Checker.Checker;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;

public sealed partial class NodeBuilderImpl
{
    internal Node? SymbolToNode(Symbol symbol, SymbolFlags meaning)
    {
        if ((_ctx!._internalFlags & NodeBuilderInternalFlags.WriteComputedProps) != 0)
        {
            if (symbol.ValueDeclaration is not null)
            {
                var name = GetNameOfDeclaration(symbol.ValueDeclaration);
                if (name is not null && IsComputedPropertyName(name))
                {
                    return name;
                }
            }

            if (_ch._valueSymbolLinks.Has(symbol))
            {
                var nameType = _ch._valueSymbolLinks.Get(symbol)._nameType;
                if (nameType is not null && (nameType._flags & (TypeFlags.EnumLiteral | TypeFlags.UniqueESSymbol)) != 0)
                {
                    var oldEnclosing = _ctx!._enclosingDeclaration;
                    _ctx!._enclosingDeclaration = nameType._symbol!.ValueDeclaration;
                    var result = _f.NewComputedPropertyName(SymbolToExpressionWorker(nameType._symbol, meaning));
                    _ctx!._enclosingDeclaration = oldEnclosing;
                    return result;
                }
            }
        }

        return SymbolToExpression(symbol, meaning);
    }

    internal Node? SymbolToName(Symbol symbol, SymbolFlags meaning, bool expectsIdentifier)
    {
        var chain = LookupSymbolChain(symbol, meaning, false);
        if (expectsIdentifier && chain.Length != 1 && !_ctx!._encounteredError && (_ctx!._flags & NodeBuilderFlags.AllowQualifiedNameInPlaceOfIdentifier) != 0)
        {
            _ctx!._encounteredError = true;
        }

        return CreateEntityNameFromSymbolChain(chain, chain.Length - 1);
    }

    internal Node? CreateEntityNameFromSymbolChain(Symbol[] chain, int index)
    {
        // typeParameterNodes := b.lookupTypeParameterNodes(chain, index)
        var symbol = chain[index];

        if (index == 0)
        {
            _ctx!._flags |= NodeBuilderFlags.InInitialEntityName;
        }

        var symbolName = GetNameOfSymbolAsWritten(symbol);
        if (index == 0)
        {
            _ctx!._flags ^= NodeBuilderFlags.InInitialEntityName;
        }

        var identifier = NewIdentifier(symbolName, symbol);
        _e.AddEmitFlags(identifier, EmitFlags.NoAsciiEscaping);
        // !!! TODO: smuggle type arguments out
        // if (typeParameterNodes) setIdentifierTypeArguments(identifier, factory.createNodeArray<TypeNode | TypeParameterDeclaration>(typeParameterNodes));
        // identifier.symbol = symbol;
        // expression = identifier;
        if (index > 0)
        {
            return _f.NewQualifiedName(
                CreateEntityNameFromSymbolChain(chain, index - 1)!,
                identifier);
        }

        return identifier;
    }

    // TODO: Audit usages of symbolToEntityNameNode - they should probably all be symbolToName
    internal Node SymbolToEntityNameNode(Symbol symbol)
    {
        var identifier = NewIdentifier(symbol.Name, symbol);
        if (symbol.Parent is not null)
        {
            return _f.NewQualifiedName(SymbolToEntityNameNode(symbol.Parent), identifier);
        }

        return identifier;
    }

    internal Node? SymbolToTypeNode(Symbol symbol, SymbolFlags mask, NodeList? typeArguments)
    {
        var chain = LookupSymbolChain(symbol, mask, (_ctx!._flags & NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope) == 0); // If we're using aliases outside the current scope, dont bother with the module
        if (chain.Length == 0)
        {
            return null; // TODO: shouldn't be possible, `lookupSymbolChain` should always at least return the input symbol and issue an error
        }

        var isTypeOf = mask == SymbolFlags.Value;
        if (Some(chain[0].Declarations, HasNonGlobalAugmentationExternalModuleSymbol))
        {
            // module is root, must use `ImportTypeNode`
            Node? nonRootParts = null;
            if (chain.Length > 1)
            {
                nonRootParts = CreateAccessFromSymbolChain(chain, chain.Length - 1, 1, typeArguments);
            }

            var typeParameterNodes = typeArguments;
            typeParameterNodes ??= LookupTypeParameterNodes(chain, 0);
            var contextFile = GetSourceFileOfNode(_e.MostOriginal(_ctx!._enclosingDeclaration)); // TODO: Just use b._ctx._enclosingFile ? Or is the delayed lookup important for context moves?
            var targetFile = GetSourceFileOfModule(chain[0]);
            var specifierResult = new ModuleSpecifierResult();
            var importModeOverride = ModuleKind.None;
            if (_ch._compilerOptions.GetModuleResolutionKind() == ModuleResolutionKind.Node16 || _ch._compilerOptions.GetModuleResolutionKind() == ModuleResolutionKind.NodeNext)
            {
                // An `import` type directed at an esm format file is only going to resolve in esm mode - set the esm mode assertion
                if (targetFile is not null && contextFile is not null && _ch._program!.GetEmitModuleFormatOfFile(targetFile) == ModuleKind.ESNext && _ch._program.GetEmitModuleFormatOfFile(targetFile) != _ch._program.GetEmitModuleFormatOfFile(contextFile))
                {
                    specifierResult = GetSpecifierForModuleSymbol(chain[0], ModuleKind.ESNext);
                    importModeOverride = ModuleKind.ESNext;
                }
            }

            if (specifierResult._specifier.Length == 0)
            {
                specifierResult = GetSpecifierForModuleSymbol(chain[0], ModuleKind.None);
            }

            if ((_ctx!._flags & NodeBuilderFlags.AllowNodeModulesRelativePaths) == 0 /* && b.ch.compilerOptions.GetModuleResolutionKind() != core.ModuleResolutionKindClassic */ && specifierResult._specifier.Contains("/node_modules/"))
            {
                var oldSpecifierResult = specifierResult;

                if (_ch._compilerOptions.GetModuleResolutionKind() == ModuleResolutionKind.Node16 || _ch._compilerOptions.GetModuleResolutionKind() == ModuleResolutionKind.NodeNext)
                {
                    // We might be able to write a portable import type using a mode override; try specifier generation again, but with a different mode set
                    var swappedMode = ModuleKind.ESNext;
                    if (_ch._program!.GetEmitModuleFormatOfFile(contextFile!) == ModuleKind.ESNext)
                    {
                        swappedMode = ModuleKind.CommonJS;
                    }

                    specifierResult = GetSpecifierForModuleSymbol(chain[0], swappedMode);

                    if (specifierResult._specifier.Contains("/node_modules/"))
                    {
                        // Still unreachable :(
                        specifierResult = oldSpecifierResult;
                    }
                    else
                    {
                        importModeOverride = swappedMode;
                    }
                }

                if (importModeOverride == ModuleKind.None)
                {
                    // If ultimately we can only name the symbol with a reference that dives into a `node_modules` folder, we should error
                    // since declaration files with these kinds of references are liable to fail when published :(
                    _ctx!._encounteredError = true;
                    _ctx!._tracker.ReportLikelyUnsafeImportRequiredError(oldSpecifierResult._specifier, symbol.Name);
                }
            }

            var attributes = CreateImportAttributesForModuleSpecifier(specifierResult, importModeOverride);
            var lit = _f.NewLiteralTypeNode(NewStringLiteral(specifierResult._specifier));
            _ctx!._approximateLength += specifierResult._specifier.Length + 10; // specifier + import("")
            if (nonRootParts is null || IsEntityName(nonRootParts))
            {
                if (nonRootParts is not null)
                {
                    // !!! TODO: smuggle type arguments out
                    // const lastId = isIdentifier(nonRootParts) ? nonRootParts : nonRootParts.right;
                    // setIdentifierTypeArguments(lastId, /*typeArguments*/ undefined);
                }

                return _f.NewImportTypeNode(isTypeOf, lit, attributes, nonRootParts, typeParameterNodes);
            }

            var splitNode = GetTopmostIndexedAccessType(nonRootParts.AsIndexedAccessTypeNode()!);
            var qualifier = splitNode.ObjectType!.AsTypeReferenceNode()!.TypeName;
            return _f.NewIndexedAccessTypeNode(
                _f.NewImportTypeNode(isTypeOf, lit, attributes, qualifier, typeParameterNodes),
                splitNode.IndexType!);
        }

        var entityName = CreateAccessFromSymbolChain(chain, chain.Length - 1, 0, typeArguments);
        if (IsIndexedAccessTypeNode(entityName!))
        {
            return entityName; // Indexed accesses can never be `typeof`
        }

        if (IsEntityName(entityName))
        {
            if (isTypeOf)
            {
                return _f.NewTypeQueryNode(entityName, null);
            }

            return _f.NewTypeReferenceNode(entityName, typeArguments);
        }

        if (isTypeOf && IsExpressionWithTypeArguments(entityName!))
        {
            var expr = entityName!.AsExpressionWithTypeArguments()!;
            return _f.NewTypeQueryNode(_f.DeepCloneNode(expr.Expression!), expr.TypeArguments);
        }

        return entityName;
    }

    internal Node? CreateAccessFromSymbolChain(Symbol[] chain, int index, int stopper, NodeList? overrideTypeArguments)
    {
        var typeParameterNodes = overrideTypeArguments;
        if (index != chain.Length - 1)
        {
            typeParameterNodes = LookupTypeParameterNodes(chain, index);
        }

        var symbol = chain[index];
        Symbol? parent = null;
        if (index > 0)
        {
            parent = chain[index - 1];
        }

        var symbolName = "";
        if (index == 0)
        {
            _ctx!._flags |= NodeBuilderFlags.InInitialEntityName;
            symbolName = GetNameOfSymbolAsWritten(symbol);
            _ctx!._approximateLength += symbolName.Length + 1;
            _ctx!._flags ^= NodeBuilderFlags.InInitialEntityName;
        }
        else
        {
            // lookup a ref to symbol within parent to handle export aliases
            if (parent is not null)
            {
                var exports = _ch.GetExportsOfSymbol(parent);
                if (exports is not null)
                {
                    // avoid exhaustive iteration in the common case
                    var ok = exports.TryGetValue(symbol.Name, out var res);
                    if (symbol.Name != InternalSymbolNameExportEquals && !IsLateBoundName(symbol.Name) && ok && res is not null && _ch.GetSymbolIfSameReference(res, symbol) is not null)
                    {
                        symbolName = symbol.Name;
                    }
                    else
                    {
                        var results = new Dictionary<Symbol, string>(1);
                        foreach (var (name, ex) in exports)
                        {
                            if (_ch.GetSymbolIfSameReference(ex, symbol) is not null && !IsLateBoundName(name) && name != InternalSymbolNameExportEquals)
                            {
                                results[ex] = name;
                                // break // must collect all results and sort them - exports are randomly iterated
                            }
                        }

                        var resultSymbols = results.Keys.ToArray();
                        if (resultSymbols.Length > 0)
                        {
                            _ch.SortSymbols(resultSymbols);
                            symbolName = results[resultSymbols[0]];
                        }
                    }
                }
            }
        }

        if (symbolName.Length == 0)
        {
            Node? name = null;
            foreach (var d in symbol.Declarations!)
            {
                name = GetNameOfDeclaration(d);
                if (name is not null)
                {
                    break;
                }
            }

            if (name is not null && IsComputedPropertyName(name) && IsEntityName(name.Expression()!))
            {
                var lhs2 = CreateAccessFromSymbolChain(chain, index - 1, stopper, overrideTypeArguments);
                if (IsEntityName(lhs2))
                {
                    return _f.NewIndexedAccessTypeNode(
                        _f.NewParenthesizedTypeNode(_f.NewTypeQueryNode(lhs2, null)),
                        _f.NewTypeQueryNode(name.Expression(), null));
                }

                return lhs2;
            }

            symbolName = GetNameOfSymbolAsWritten(symbol);
        }

        _ctx!._approximateLength += symbolName.Length + 1;

        if ((_ctx!._flags & NodeBuilderFlags.ForbidIndexedAccessSymbolReferences) == 0 && parent is not null &&
            _ch.GetMembersOfSymbol(parent) is not null && _ch.GetMembersOfSymbol(parent).TryGetValue(symbol.Name, out var member) &&
            _ch.GetSymbolIfSameReference(member, symbol) is not null)
        {
            // Should use an indexed access
            var lhs = CreateAccessFromSymbolChain(chain, index - 1, stopper, overrideTypeArguments);
            if (IsIndexedAccessTypeNode(lhs!))
            {
                return _f.NewIndexedAccessTypeNode(
                    lhs,
                    _f.NewLiteralTypeNode(NewStringLiteral(symbolName)));
            }

            return _f.NewIndexedAccessTypeNode(
                _f.NewTypeReferenceNode(lhs, typeParameterNodes),
                _f.NewLiteralTypeNode(NewStringLiteral(symbolName)));
        }

        var identifier = NewIdentifier(symbolName, symbol);
        _e.AddEmitFlags(identifier, EmitFlags.NoAsciiEscaping);

        if (index > stopper)
        {
            var lhs = CreateAccessFromSymbolChain(chain, index - 1, stopper, overrideTypeArguments);
            if ((_ctx!._flags & NodeBuilderFlags.UseInstantiationExpressions) == 0 || IsEntityName(lhs) && (typeParameterNodes is null || typeParameterNodes.Nodes!.Length == 0))
            {
                return _f.NewQualifiedName(lhs, identifier);
            }

            return CreateExpressionWithTypeArguments(_f.NewPropertyAccessExpression(CreateAccessExpression(lhs!)!, null, identifier, NodeFlags.None), typeParameterNodes);
        }

        return identifier;
    }

    internal Node? SymbolToExpression(Symbol symbol, SymbolFlags mask)
    {
        _ctx!._tracker.TrackSymbol(symbol, _ctx!._enclosingDeclaration!, mask);
        return SymbolToExpressionWorker(symbol, mask);
    }

    internal Node? SymbolToExpressionWorker(Symbol symbol, SymbolFlags mask)
    {
        var chain = LookupSymbolChainWorker(symbol, mask, false);
        return CreateExpressionFromSymbolChain(chain, chain.Length - 1);
    }

    internal Node? CreateExpressionFromSymbolChain(Symbol[] chain, int index)
    {
        var typeParameterNodes = LookupExpressionChainTypeArgumentNodes(chain, index);
        var symbol = chain[index];

        if (index == 0)
        {
            _ctx!._flags |= NodeBuilderFlags.InInitialEntityName;
        }

        var symbolName = GetNameOfSymbolAsWritten(symbol);
        if (index == 0)
        {
            _ctx!._flags ^= NodeBuilderFlags.InInitialEntityName;
        }

        if (StartsWithSingleOrDoubleQuote(symbolName) && Some(symbol.Declarations, HasNonGlobalAugmentationExternalModuleSymbol))
        {
            var specifierResult = GetSpecifierForModuleSymbol(symbol, ModuleKind.None);
            _ctx!._approximateLength += 2 + specifierResult._specifier.Length;
            return NewStringLiteral(specifierResult._specifier);
        }

        if (index == 0 || CanUsePropertyAccess(symbolName))
        {
            var identifier = NewIdentifier(symbolName, symbol);
            _e.AddEmitFlags(identifier, EmitFlags.NoAsciiEscaping);
            _ctx!._approximateLength += 1 + symbolName.Length;
            if (index > 0)
            {
                var result = _f.NewPropertyAccessExpression(CreateExpressionFromSymbolChain(chain, index - 1)!, null, identifier, NodeFlags.None);
                _e.AddEmitFlags(result, EmitFlags.NoIndentation);
                return CreateExpressionWithTypeArguments(result, typeParameterNodes);
            }

            return CreateExpressionWithTypeArguments(identifier, typeParameterNodes);
        }

        if (StartsWithSquareBracket(symbolName))
        {
            symbolName = symbolName[1..^1];
        }

        Node? expression = null;
        if (StartsWithSingleOrDoubleQuote(symbolName) && (symbol.Flags & SymbolFlags.EnumMember) == 0)
        {
            var literalText = UnquoteString(symbolName);
            _ctx!._approximateLength += literalText.Length + 2;
            expression = NewStringLiteralEx(literalText, symbolName[0] == '\'');
        }
        else if (global::TypeScript.JsNum.JsNum.FromString(symbolName).String() == symbolName)
        {
            // TODO: the follwing in strada would assert if the number is negative, but no such assertion exists here
            // Moreover, what's even guaranteeing the name *isn't* -1 here anyway? Needs double-checking.
            _ctx!._approximateLength += symbolName.Length;
            expression = _f.NewNumericLiteral(symbolName, TokenFlags.None);
        }

        if (expression is null)
        {
            _ctx!._approximateLength += symbolName.Length;
            expression = NewIdentifier(symbolName, symbol);
            _e.AddEmitFlags(expression, EmitFlags.NoAsciiEscaping);
        }

        _ctx!._approximateLength += 2; // []
        return CreateExpressionWithTypeArguments(_f.NewElementAccessExpression(CreateExpressionFromSymbolChain(chain, index - 1)!, null, expression, NodeFlags.None), typeParameterNodes);
    }

    internal string GetNameOfSymbolFromNameType(Symbol symbol)
    {
        if (_ch._valueSymbolLinks.Has(symbol))
        {
            var nameType = _ch._valueSymbolLinks.Get(symbol)._nameType;
            if (nameType is null)
            {
                return "";
            }

            if ((nameType._flags & TypeFlags.StringOrNumberLiteral) != 0)
            {
                var name = "";
                var value = nameType.AsLiteralType()!._value;
                if (value is string s)
                {
                    name = s;
                }
                else if (value is global::TypeScript.JsNum.Number n)
                {
                    name = n.String();
                }

                if (!IsIdentifierText(name, LanguageVariant.Standard) && !IsNumericLiteralName(name))
                {
                    return ValueToString(value);
                }

                if (IsNumericLiteralName(name) && name.StartsWith('-'))
                {
                    return $"[{name}]";
                }

                return name;
            }

            if ((nameType._flags & TypeFlags.UniqueESSymbol) != 0)
            {
                var text = GetNameOfSymbolAsWritten(nameType.AsUniqueESSymbolType()!._symbol!);
                return $"[{text}]";
            }
        }

        return "";
    }

    /// <summary>
    /// Gets a human-readable name for a symbol.
    /// Should *not* be used for the right-hand side of a `.` -- use `symbolName(symbol)` for that instead.
    ///
    /// Unlike `symbolName(symbol)`, this will include quotes if the name is from a string literal.
    /// It will also use a representation of a number as written instead of a decimal form, e.g. `0o11` instead of `9`.
    /// </summary>
    internal string GetNameOfSymbolAsWritten(Symbol symbol)
    {
        if (_ctx!._remappedSymbolReferences.TryGetValue(GetSymbolId(symbol), out var result))
        {
            symbol = result;
        }

        if (symbol.Name == InternalSymbolNameDefault && (_ctx!._flags & NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope) == 0 &&
            // If it's not the first part of an entity name, it must print as `default`
            ((_ctx!._flags & NodeBuilderFlags.InInitialEntityName) == 0 ||
             // if the symbol is synthesized, it will only be referenced externally it must print as `default`
             (symbol.Declarations?.Length ?? 0) == 0 ||
             // if not in the same binding context (source file, module declaration), it must print as `default`
             (_ctx!._enclosingDeclaration is not null && FindAncestor(symbol.Declarations![0], IsDefaultBindingContext) != FindAncestor(_ctx!._enclosingDeclaration, IsDefaultBindingContext))))
        {
            return "default";
        }

        if ((symbol.Declarations?.Length ?? 0) > 0)
        {
            var name = FirstNonNil(symbol.Declarations, GetNameOfDeclaration); // Try using a declaration with a name, first
            if (name is not null)
            {
                // !!! TODO: JS Object.defineProperty declarations
                // if ast.IsCallExpression(declaration) && ast.IsBindableObjectDefinePropertyCall(declaration) {
                // 	return symbol.Name
                // }
                if (IsComputedPropertyName(name) && (symbol.CheckFlags & CheckFlags.Late) == 0)
                {
                    if (_ch._valueSymbolLinks.Has(symbol) && _ch._valueSymbolLinks.Get(symbol)._nameType is not null && (_ch._valueSymbolLinks.Get(symbol)._nameType!._flags & TypeFlags.StringOrNumberLiteral) != 0)
                    {
                        var fromNameType = GetNameOfSymbolFromNameType(symbol);
                        if (fromNameType.Length > 0)
                        {
                            return fromNameType;
                        }
                    }
                }

                return DeclarationNameToString(name);
            }

            var declaration = symbol.Declarations![0]; // Declaration may be nameless, but we'll try anyway
            if (declaration.Parent is not null && declaration.Parent.Kind == Kind.VariableDeclaration)
            {
                return DeclarationNameToString(declaration.Parent.AsVariableDeclaration()!.Name());
            }

            if (IsClassExpression(declaration) || IsFunctionExpression(declaration) || IsArrowFunction(declaration))
            {
                if (_ctx is not null && !_ctx!._encounteredError && (_ctx!._flags & NodeBuilderFlags.AllowAnonymousIdentifier) == 0)
                {
                    _ctx!._encounteredError = true;
                }

                switch (declaration.Kind)
                {
                    case Kind.ClassExpression:
                        return "(Anonymous class)";
                    case Kind.FunctionExpression:
                    case Kind.ArrowFunction:
                        return "(Anonymous function)";
                }
            }
        }

        var name2 = GetNameOfSymbolFromNameType(symbol);
        if (name2.Length > 0)
        {
            return name2;
        }

        return EscapeInternalSymbolName(symbol.Name);
    }

    /// <summary>
    /// The full set of type parameters for a generic class or interface type consists of its outer type parameters plus
    /// its locally declared type parameters.
    /// </summary>
    internal Type[] GetTypeParametersOfClassOrInterface(Symbol symbol)
    {
        var result = new List<Type>();
        result.AddRange(_ch.GetOuterTypeParametersOfClassOrInterface(symbol) ?? []);
        result.AddRange(_ch.GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(symbol));
        return result.ToArray();
    }

    internal NodeList? LookupTypeParameterNodes(Symbol[] chain, int index)
    {
        Debug.Assert(chain is not null && 0 <= index && index < chain.Length);
        var symbol = chain![index];
        var symbolId = GetSymbolId(symbol);
        if (_ctx!._typeParameterSymbolList.Has(symbolId))
        {
            return null;
        }

        _ctx!._typeParameterSymbolList.Add(symbolId);

        if ((_ctx!._flags & NodeBuilderFlags.WriteTypeParametersInQualifiedName) != 0 && index < chain.Length - 1)
        {
            var typeArgumentNodes = LookupInstantiatedTypeArgumentNodes(chain, index);
            if (typeArgumentNodes is not null)
            {
                return typeArgumentNodes;
            }
            else
            {
                var typeParameterNodes = TypeParametersToTypeParameterDeclarations(symbol);
                if (typeParameterNodes.Length > 0)
                {
                    return _f.NewNodeList(typeParameterNodes);
                }

                return null;
            }
        }

        return null;
    }

    // TODO: move `lookupSymbolChain` and co to `symbolaccessibility.go` (but getSpecifierForModuleSymbol uses much context which makes that hard?)
    internal Symbol[] LookupSymbolChain(Symbol symbol, SymbolFlags meaning, bool yieldModuleSymbol)
    {
        _ctx!._tracker.TrackSymbol(symbol, _ctx!._enclosingDeclaration!, meaning);
        return LookupSymbolChainWorker(symbol, meaning, yieldModuleSymbol);
    }

    internal Symbol[] LookupSymbolChainWorker(Symbol symbol, SymbolFlags meaning, bool yieldModuleSymbol)
    {
        // Try to get qualified name if the symbol is not a type parameter and there is an enclosing declaration.
        Symbol[] chain;
        var isTypeParameter = (symbol.Flags & SymbolFlags.TypeParameter) != 0;
        if (!isTypeParameter && (_ctx!._enclosingDeclaration is not null || (_ctx!._flags & NodeBuilderFlags.UseFullyQualifiedType) != 0) && (_ctx!._internalFlags & NodeBuilderInternalFlags.DoNotIncludeSymbolChain) == 0)
        {
            var res = GetSymbolChain(symbol, meaning /*endOfChain*/, true, yieldModuleSymbol);
            chain = res!;
            Debug.Assert(chain is not null);
            Debug.Assert(chain!.Length > 0);
        }
        else
        {
            chain = [symbol];
        }

        return chain;
    }

    /// <summary>@param endOfChain Set to false for recursive calls; non-recursive calls should always output something.</summary>
    internal Symbol[]? GetSymbolChain(Symbol symbol, SymbolFlags meaning, bool endOfChain, bool yieldModuleSymbol)
    {
        var accessibleSymbolChain = _ch.GetAccessibleSymbolChainWorker(symbol, _ctx!._enclosingDeclaration, meaning, (_ctx!._flags & NodeBuilderFlags.UseOnlyExternalAliasing) != 0) ?? [];
        var qualifierMeaning = meaning;
        if (accessibleSymbolChain.Length > 1)
        {
            qualifierMeaning = GetQualifiedLeftMeaning(meaning);
        }

        if (accessibleSymbolChain.Length == 0 ||
            _ch.NeedsQualification(accessibleSymbolChain[0], _ctx!._enclosingDeclaration, qualifierMeaning))
        {
            // Go up and add our parent.
            var root = symbol;
            if (accessibleSymbolChain.Length > 0)
            {
                root = accessibleSymbolChain[0];
            }

            var parents = _ch.GetContainersOfSymbol(root, _ctx!._enclosingDeclaration, meaning) ?? [];
            if (parents.Length > 0)
            {
                var parentSpecifiers = Map(parents, symbol2 =>
                {
                    if (Some(symbol2.Declarations, HasNonGlobalAugmentationExternalModuleSymbol))
                    {
                        return new SortedSymbolNamePair(symbol2, GetSpecifierForModuleSymbol(symbol2, ModuleKind.None)._specifier);
                    }

                    return new SortedSymbolNamePair(symbol2, "");
                });
                // Go 的 slices.SortStableFunc → OrderBy（稳定）；Array.Sort/List.Sort 不稳定，会破坏输出确定性。
                parentSpecifiers = parentSpecifiers.OrderBy(x => x, Comparer<SortedSymbolNamePair>.Create(SortByBestName)).ToArray();
                foreach (var pair in parentSpecifiers)
                {
                    var parent = pair._sym;
                    var parentChain = GetSymbolChain(parent, GetQualifiedLeftMeaning(meaning), false, yieldModuleSymbol);
                    if (parentChain is not null && parentChain.Length > 0)
                    {
                        if (parent.Exports is not null)
                        {
                            if (parent.Exports.TryGetValue(InternalSymbolNameExportEquals, out var exported) && _ch.GetSymbolIfSameReference(exported, symbol) is not null)
                            {
                                // parentChain root _is_ symbol - symbol is a module export=, so it kinda looks like it's own parent
                                // No need to lookup an alias for the symbol in itself
                                accessibleSymbolChain = parentChain;
                                break;
                            }
                        }

                        var nextSyms = accessibleSymbolChain;
                        if (nextSyms.Length == 0)
                        {
                            var fallback = _ch.GetAliasForSymbolInContainer(parent, symbol);
                            fallback ??= symbol;
                            nextSyms = [fallback];
                        }

                        accessibleSymbolChain = parentChain.Concat(nextSyms).ToArray();
                        break;
                    }
                }
            }
        }

        if (accessibleSymbolChain.Length > 0)
        {
            return accessibleSymbolChain;
        }

        if (
            // If this is the last part of outputting the symbol, always output. The cases apply only to parent symbols.
            endOfChain ||
            // If a parent symbol is an anonymous type, don't write it.
            (symbol.Flags & (SymbolFlags.TypeLiteral | SymbolFlags.ObjectLiteral)) == 0)
        {
            // If a parent symbol is an external module, don't write it. (We prefer just `x` vs `"foo/bar".x`.)
            if (!endOfChain && !yieldModuleSymbol && Some(symbol.Declarations, HasNonGlobalAugmentationExternalModuleSymbol))
            {
                return null;
            }

            return [symbol];
        }

        return null;
    }

    internal int SortByBestName(SortedSymbolNamePair a, SortedSymbolNamePair b)
    {
        var specifierA = a._name;
        var specifierB = b._name;
        if (specifierA.Length > 0 && specifierB.Length > 0)
        {
            var isBRelative = global::TypeScript.TSPath.TSPath.PathIsRelative(specifierB);
            if (global::TypeScript.TSPath.TSPath.PathIsRelative(specifierA) == isBRelative)
            {
                // Both relative or both non-relative, sort by number of parts
                return global::TypeScript.ModuleSpecifiers.ModuleSpecifiers.CountPathComponents(specifierA) - global::TypeScript.ModuleSpecifiers.ModuleSpecifiers.CountPathComponents(specifierB);
            }

            if (isBRelative)
            {
                // A is non-relative, B is relative: prefer A
                return -1;
            }

            // A is relative, B is non-relative: prefer B
            return 1;
        }

        return _ch._compareSymbols(a._sym, b._sym); // must sort symbols for stable ordering
    }

    internal ModuleSpecifierResult GetSpecifierForModuleSymbol(Symbol symbol, ModuleKind overrideImportMode)
    {
        var enclosingDeclaration = _e.MostOriginal(_ctx!._enclosingDeclaration);
        Node? originalModuleSpecifier = null;
        if (CanHaveModuleSpecifier(enclosingDeclaration))
        {
            originalModuleSpecifier = TryGetModuleSpecifierFromDeclaration(enclosingDeclaration);
        }

        Type? originalImportAttributesType = null;
        if (originalModuleSpecifier is not null)
        {
            originalImportAttributesType = _ch.GetImportAttributesTypeForModuleSpecifier(originalModuleSpecifier);
        }

        var file = GetDeclarationOfKind(symbol, Kind.SourceFile);
        if (file is null)
        {
            var equivalentSymbol = FirstNonNil(symbol.Declarations, d => _ch.GetFileSymbolIfFileSymbolExportEqualsContainer(d, symbol));
            if (equivalentSymbol is not null)
            {
                file = GetDeclarationOfKind(equivalentSymbol, Kind.SourceFile);
            }
        }

        if (file is null)
        {
            var declaration = Find(symbol.Declarations, IsModuleWithStringLiteralName);
            if (declaration is not null)
            {
                var specifier0 = declaration.Name()!.Text();
                if (originalImportAttributesType is not null && ModuleSpecifierResolvesToSymbol(specifier0, originalImportAttributesType, symbol))
                {
                    return new ModuleSpecifierResult { _specifier = specifier0, _importAttributesType = originalImportAttributesType };
                }

                return new ModuleSpecifierResult { _specifier = specifier0, _importAttributesType = _ch.GetTypeOfModuleImportAttributes(symbol) };
            }

            if (TryGetAmbientModuleNameFromSymbolName(symbol.Name, out var ambientSpecifier))
            {
                return new ModuleSpecifierResult { _specifier = ambientSpecifier };
            }
        }

        if (_ctx!._enclosingFile is null)
        {
            if (TryGetAmbientModuleNameFromSymbolName(symbol.Name, out var ambientSpecifier2))
            {
                return new ModuleSpecifierResult { _specifier = ambientSpecifier2 };
            }

            return new ModuleSpecifierResult { _specifier = GetSourceFileOfModule(symbol)!.FileName() };
        }

        var contextFile = _ctx!._enclosingFile!;
        var resolutionMode = overrideImportMode;
        if (resolutionMode == ModuleKind.None && originalModuleSpecifier is not null)
        {
            resolutionMode = _ch._program!.GetModeForUsageLocation(contextFile, originalModuleSpecifier);
        }
        else if (resolutionMode == ModuleKind.None && contextFile is not null)
        {
            resolutionMode = _ch._program!.GetDefaultResolutionModeForFile(contextFile);
        }

        var cacheKey = new ModeAwareCacheKey((string)contextFile!.Path(), resolutionMode);
        var links = _symbolLinks.Get(symbol);
        links._specifierCache ??= new ModeAwareCache<ModuleSpecifierResult>();
        if (links._specifierCache.TryGetValue(cacheKey, out var cached))
        {
            return ModuleSpecifierResultForSymbol(cached, originalImportAttributesType, symbol);
        }

        // For declaration bundles, we need to generate absolute paths relative to the common source dir for imports,
        // just like how the declaration emitter does for the ambient module declarations - we can easily accomplish this
        // using the `baseUrl` compiler option (which we would otherwise never use in declaration emit) and a non-relative
        // specifier preference
        var host = _ctx!._host;
        var specifierCompilerOptions = _ch._compilerOptions;
        var specifierPref = (ImportModuleSpecifierPreference)global::TypeScript.ModuleSpecifiers.ModuleSpecifiers.ImportModuleSpecifierPreferenceProjectRelative;
        var endingPref = (ImportModuleSpecifierEndingPreference)global::TypeScript.ModuleSpecifiers.ModuleSpecifiers.ImportModuleSpecifierEndingPreferenceNone;
        if (resolutionMode == ModuleKind.ESNext)
        {
            endingPref = (ImportModuleSpecifierEndingPreference)global::TypeScript.ModuleSpecifiers.ModuleSpecifiers.ImportModuleSpecifierEndingPreferenceJs;
        }

        var moduleSpecifiersResult = global::TypeScript.ModuleSpecifiers.ModuleSpecifiers.GetModuleSpecifiers(
            symbol,
            _ch,
            specifierCompilerOptions,
            contextFile,
            host!,
            new UserPreferences
            {
                ImportModuleSpecifierPreference = specifierPref,
                ImportModuleSpecifierEnding = endingPref,
            },
            new ModuleSpecifierOptions
            {
                OverrideImportMode = overrideImportMode,
            },
            false /*forAutoImports*/);
        Debug.Assert(moduleSpecifiersResult.Specifiers.Length > 0);
        var result = new ModuleSpecifierResult { _specifier = moduleSpecifiersResult.Specifiers[0] };
        if (moduleSpecifiersResult.AmbientModuleSymbol is not null)
        {
            result._importAttributesType = _ch.GetTypeOfModuleImportAttributes(moduleSpecifiersResult.AmbientModuleSymbol);
        }

        links._specifierCache[cacheKey] = result;
        return ModuleSpecifierResultForSymbol(result, originalImportAttributesType, symbol);
    }

    internal ModuleSpecifierResult ModuleSpecifierResultForSymbol(ModuleSpecifierResult result, Type? importAttributesType, Symbol symbol)
    {
        if (importAttributesType is not null && ModuleSpecifierResolvesToSymbol(result._specifier, importAttributesType, symbol))
        {
            result._importAttributesType = importAttributesType;
        }

        return result;
    }

    internal bool ModuleSpecifierResolvesToSymbol(string specifier, Type importAttributesType, Symbol symbol)
    {
        var location = _ctx!._enclosingDeclaration;
        if (location is null && _ctx!._enclosingFile is not null)
        {
            location = _ctx!._enclosingFile.AsNode();
        }

        if (location is null)
        {
            return false;
        }

        var resolved = _ch.ResolveExternalModule(location, specifier, null, null, false /*isForAugmentation*/, importAttributesType);
        return resolved is not null && _ch.GetMergedSymbolWorker(resolved) == _ch.GetMergedSymbolWorker(symbol);
    }

    internal Node? CreateImportAttributesForModuleSpecifier(ModuleSpecifierResult result, ModuleKind importModeOverride)
    {
        var isEmptyAttributesType = result._importAttributesType is null || _ch.IsEmptyObjectType(result._importAttributesType);
        if (isEmptyAttributesType && importModeOverride == ModuleKind.None)
        {
            return null;
        }

        Symbol[] properties = [];
        if (!isEmptyAttributesType)
        {
            properties = (Symbol[])(_ch.GetPropertiesOfTypeWorker(result._importAttributesType!) ?? []).Clone();
        }

        Array.Sort(properties, (a, b) => string.CompareOrdinal(a.Name, b.Name));
        var attributes = new List<Node>(properties.Length + 1);
        if (importModeOverride != ModuleKind.None)
        {
            var resolutionMode = importModeOverride == ModuleKind.ESNext ? "import" : "require";
            attributes.Add(_f.NewImportAttribute(NewStringLiteral("resolution-mode"), NewStringLiteral(resolutionMode)));
            _ctx!._approximateLength += "resolution-mode".Length + resolutionMode.Length + 6; // `"resolution-mode": "value"`
        }

        foreach (var property in properties)
        {
            var name = property.Name;
            var propertyType = _ch.GetTypeOfSymbolWorker(property);
            if ((propertyType._flags & TypeFlags.StringLiteral) == 0)
            {
                continue;
            }

            var value = GetStringLiteralValue(propertyType);
            Node nameNode;
            if (IsIdentifierText(name, LanguageVariant.Standard))
            {
                nameNode = _f.NewIdentifier(name);
            }
            else
            {
                nameNode = NewStringLiteral(name);
                _ctx!._approximateLength += 2;
            }

            attributes.Add(_f.NewImportAttribute(nameNode, NewStringLiteral(value)));
            _ctx!._approximateLength += name.Length + value.Length + 4; // `name: "value"`
        }

        if (attributes.Count == 0)
        {
            return null;
        }

        _ctx!._approximateLength += 16 + 2 * (attributes.Count - 1); // `, { with: { , ... , } }`
        return _f.NewImportAttributes(Kind.WithKeyword, _f.NewNodeList(attributes.ToArray()), false);
    }
}

internal sealed class SortedSymbolNamePair
{
    internal Symbol _sym;
    internal string _name;

    internal SortedSymbolNamePair(Symbol sym, string name)
    {
        _sym = sym;
        _name = name;
    }
}

// Go 的 `modulespecifiers.CheckerShape` 由 `*Checker` **结构上**满足（Go 隐式接口）；C# 没有结构性满足，
// 必须在**一个** partial 声明上补出接口 —— 见 `NodeBuilderImpl.cs` 的
// `public sealed partial class Checker : global::TypeScript.ModuleSpecifiers.CheckerShape`（那里同时给出两个显式实现）。
// 这里不再重复列出接口（同一类型多处列同一接口容易与集成时的类声明核对冲突）。
public sealed partial class Checker
{
    // Go: type sortedSymbolNamePair struct { sym *ast.Symbol; name string }（nodebuilderimpl.go:1081，已按 C# 习惯命名 SortedSymbolNamePair）

    /// <summary>Go: <c>func canUsePropertyAccess(name string) bool</c>。</summary>
    internal static bool CanUsePropertyAccess(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        // TODO: in strada, this only used `isIdentifierStart` on the first character, while this checks the whole string for validity
        // - possible strada bug?
        if (name.StartsWith('#'))
        {
            return name.Length > 1 && IsIdentifierText(name[1..], LanguageVariant.Standard);
        }

        return IsIdentifierText(name, LanguageVariant.Standard);
    }

    /// <summary>Go: <c>func startsWithSingleOrDoubleQuote(str string) bool</c>。</summary>
    internal static bool StartsWithSingleOrDoubleQuote(string str)
    {
        return str.StartsWith('\'') || str.StartsWith('"');
    }

    /// <summary>Go: <c>func startsWithSquareBracket(str string) bool</c>。</summary>
    internal static bool StartsWithSquareBracket(string str)
    {
        return str.StartsWith('[');
    }

    /// <summary>Go: <c>func isDefaultBindingContext(location *ast.Node) bool</c>。</summary>
    internal static bool IsDefaultBindingContext(Node location)
    {
        return location.Kind == Kind.SourceFile || IsAmbientModule(location);
    }

    /// <summary>Go: <c>func canHaveModuleSpecifier(node *ast.Node) bool</c>。</summary>
    internal static bool CanHaveModuleSpecifier(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        switch (node.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.BindingElement:
            case Kind.ImportDeclaration:
            case Kind.ExportDeclaration:
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportClause:
            case Kind.NamespaceExport:
            case Kind.NamespaceImport:
            case Kind.ExportSpecifier:
            case Kind.ImportSpecifier:
            case Kind.ImportType:
                return true;
        }

        return false;
    }

    /// <summary>Go: <c>func TryGetModuleSpecifierFromDeclaration(node *ast.Node) *ast.Node</c>。</summary>
    public static Node? TryGetModuleSpecifierFromDeclaration(Node? node)
    {
        var res = TryGetModuleSpecifierFromDeclarationWorker(node);
        if (res is null || !IsStringLiteral(res))
        {
            return null;
        }

        return res;
    }

    /// <summary>Go: <c>func tryGetModuleSpecifierFromDeclarationWorker(node *ast.Node) *ast.Node</c>。</summary>
    internal static Node? TryGetModuleSpecifierFromDeclarationWorker(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.BindingElement:
            {
                var moduleCall = FindAncestor(node.Initializer()!, n => IsRequireCall(n, true /*requireStringLiteralLikeArgument*/) || IsImportCall(n));
                if (moduleCall is null)
                {
                    return null;
                }

                return moduleCall.Arguments()![0];
            }

            case Kind.ImportDeclaration:
            case Kind.ExportDeclaration:
            case Kind.JSDocImportTag:
                return node.ModuleSpecifier();
            case Kind.ImportEqualsDeclaration:
            {
                var refExpr = node.AsImportEqualsDeclaration()!.ModuleReference!;
                if (refExpr.Kind != Kind.ExternalModuleReference)
                {
                    return null;
                }

                return refExpr.Expression();
            }

            case Kind.ImportClause:
                if (IsImportDeclaration(node.Parent!))
                {
                    return node.Parent!.ModuleSpecifier();
                }

                return node.Parent!.ModuleSpecifier();
            case Kind.NamespaceExport:
                return node.Parent!.ModuleSpecifier();
            case Kind.NamespaceImport:
                if (IsImportDeclaration(node.Parent!.Parent!))
                {
                    return node.Parent!.Parent!.ModuleSpecifier();
                }

                return node.Parent!.Parent!.ModuleSpecifier();
            case Kind.ExportSpecifier:
                return node.Parent!.Parent!.ModuleSpecifier();
            case Kind.ImportSpecifier:
                if (IsImportDeclaration(node.Parent!.Parent!.Parent!))
                {
                    return node.Parent!.Parent!.Parent!.ModuleSpecifier();
                }

                return node.Parent!.Parent!.Parent!.ModuleSpecifier();
            case Kind.ImportType:
                if (IsLiteralImportTypeNode(node))
                {
                    return node.AsImportTypeNode()!.Argument!.AsLiteralTypeNode()!.Literal;
                }

                return null;
            default:
                Debug.AssertNever(node);
                return null;
        }
    }

    /// <summary>Go: <c>func getTopmostIndexedAccessType(node *ast.IndexedAccessTypeNode) *ast.IndexedAccessTypeNode</c>。</summary>
    internal static IndexedAccessTypeNode GetTopmostIndexedAccessType(IndexedAccessTypeNode node)
    {
        if (IsIndexedAccessTypeNode(node.ObjectType!))
        {
            return GetTopmostIndexedAccessType(node.ObjectType!.AsIndexedAccessTypeNode()!);
        }

        return node;
    }
}

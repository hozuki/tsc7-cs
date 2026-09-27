// ported from: internal/checker/checker.go:1307-1555
//   （initializeChecker、mergeGlobalSymbol、mergePatternAmbientModules、mergeModuleAugmentation、
//     addUndefinedToGlobalsOrErrorOnRedeclaration、createNameResolver、createNameResolverForSuggestion、
//     symbolReferenced、getRequiresScopeChangeCache、setRequiresScopeChangeCache）
//
// 与 Go 的偏离（逐条）：
// 1) `c.patternAmbientModules = append(...)` → `List<T>.AddRange/Add`（Go 的 nil slice ↔ C# 空 List；
//    调 core.Some 的那一处改成显式 foreach，因为 C# 的 Core.Some 只收 `T[]?`）。
// 2) `ast.GetSymbolTable(&c.patternAmbientModuleAugmentations)` → `GetSymbolTable(ref _patternAmbientModuleAugmentations)`
//    （C# 的 SymbolTable 是 class，`ref` 复刻"按需新建"语义）。
// 3) `for name, symbol := range file.GlobalExports`（nil map 安全）→ `file.GlobalExports is not null` + foreach。
// 4) `c.valueSymbolLinks.Get(sym).resolvedType` → `_valueSymbolLinks.Get(sym)._resolvedType`（links 字段 `_camelCase`，§5.5）。
// 5) `onFailedToResolveSymbol`/`onSuccessfullyResolvedSymbol`/`checkAndReportErrorForInvalidInitializer` 的委托形状
//    按 Binder/NameResolver.cs 的字段类型（`Action<Node?, string, SymbolFlags, Message>` 等）。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;

public sealed partial class Checker
{
    /// <summary>Go: checker.go:1307 <c>initializeChecker</c>。</summary>
    internal void InitializeChecker()
    {
        // Initialize global symbol table
        var ambientModuleSymbols = new List<Symbol>();
        var augmentations = new List<Node[]>(_files.Length);
        foreach (var file in _files)
        {
            if (!IsExternalOrCommonJSModule(file))
            {
                // It is an error for a non-external-module (i.e. script) to declare its own `globalThis`.
                if (file.Locals is not null && file.Locals.TryGetValue("globalThis", out var fileGlobalThisSymbol))
                {
                    foreach (var d in fileGlobalThisSymbol.Declarations ?? [])
                    {
                        AddDiagnostic(NewDiagnosticForNode(d, Diagnostics.Declaration_name_conflicts_with_built_in_global_identifier_0, "globalThis"));
                    }
                }
                foreach (var (_, symbol) in file.Locals ?? [])
                {
                    // We defer merging of global ambient module declarations since they may require other global symbols
                    // and types to be resolved. See https://github.com/microsoft/TypeScript/tsc/issues/2953.
                    if ((symbol.Flags & SymbolFlags.Module) != 0 && IsAmbientModuleSymbolName(symbol.Name))
                    {
                        ambientModuleSymbols.Add(symbol);
                    }
                    else
                    {
                        MergeGlobalSymbol(symbol);
                    }
                }
            }
            if (file.PatternAmbientModules is not null)
            {
                _patternAmbientModules.AddRange(file.PatternAmbientModules);
            }
            augmentations.Add(file.ModuleAugmentations ?? []);
            if (file.Symbol is not null)
            {
                // Merge in UMD exports with first-in-wins semantics (see #9771)
                if (file.GlobalExports is not null)
                {
                    foreach (var (name, symbol) in file.GlobalExports)
                    {
                        if (!_globals.ContainsKey(name))
                        {
                            _globals[name] = symbol;
                        }
                    }
                }
            }
        }
        // We do global augmentations separately from module augmentations (and before creating global types) because they
        //  1. Affect global types. We won't have the correct global types until global augmentations are merged. Also,
        //  2. Module augmentation instantiation requires creating the type of a module, which, in turn, can require
        //       checking for an export or property on the module (if export=) which, in turn, can fall back to the
        //       apparent type of the module - either globalObjectType or globalFunctionType - which wouldn't exist if we
        //       did module augmentations prior to finalizing the global types.
        foreach (var list in augmentations)
        {
            foreach (var augmentation in list)
            {
                // Merge 'global' module augmentations. This needs to be done after global symbol table is initialized to
                // make sure that all ambient modules are indexed
                if (IsGlobalScopeAugmentation(augmentation.Parent!))
                {
                    MergeModuleAugmentation(augmentation);
                }
            }
        }
        AddUndefinedToGlobalsOrErrorOnRedeclaration();
        _valueSymbolLinks.Get(_undefinedSymbol)._resolvedType = _undefinedWideningType;
        _valueSymbolLinks.Get(_argumentsSymbol)._resolvedType = GetGlobalType("IArguments", 0 /*arity*/, true /*reportErrors*/);
        _valueSymbolLinks.Get(_unknownSymbol)._resolvedType = _errorType;
        _valueSymbolLinks.Get(_globalThisSymbol)._resolvedType = NewObjectType(ObjectFlags.Anonymous, _globalThisSymbol);
        // Initialize special types
        _globalArrayType = GetGlobalType("Array", 1 /*arity*/, true /*reportErrors*/);
        _globalObjectType = GetGlobalType("Object", 0 /*arity*/, true /*reportErrors*/);
        _globalFunctionType = GetGlobalType("Function", 0 /*arity*/, true /*reportErrors*/);
        _globalCallableFunctionType = GetGlobalStrictFunctionType("CallableFunction");
        _globalNewableFunctionType = GetGlobalStrictFunctionType("NewableFunction");
        _globalStringType = GetGlobalType("String", 0 /*arity*/, true /*reportErrors*/);
        _globalNumberType = GetGlobalType("Number", 0 /*arity*/, true /*reportErrors*/);
        _globalBooleanType = GetGlobalType("Boolean", 0 /*arity*/, true /*reportErrors*/);
        _globalRegExpType = GetGlobalType("RegExp", 0 /*arity*/, true /*reportErrors*/);
        _anyArrayType = CreateArrayType(_anyType);
        _autoArrayType = CreateArrayType(_autoType);
        if (ReferenceEquals(_autoArrayType, _emptyObjectType))
        {
            // autoArrayType is used as a marker, so even if global Array type is not defined, it needs to be a unique type
            _autoArrayType = NewAnonymousType(null!, null, null, null, null);
        }
        _globalReadonlyArrayType = GetGlobalType("ReadonlyArray", 1 /*arity*/, false /*reportErrors*/);
        if (ReferenceEquals(_globalReadonlyArrayType, _emptyGenericType))
        {
            _globalReadonlyArrayType = _globalArrayType;
        }
        _anyReadonlyArrayType = CreateTypeFromGenericGlobalType(_globalReadonlyArrayType, [_anyType]);
        _globalThisType = GetGlobalType("ThisType", 1 /*arity*/, false /*reportErrors*/);
        // Now merge global ambient module declarations
        foreach (var symbol in ambientModuleSymbols)
        {
            MergeGlobalSymbol(symbol);
        }
        MergePatternAmbientModules();
        // merge _nonglobal_ module augmentations.
        // this needs to be done after global symbol table is initialized to make sure that all ambient modules are indexed
        foreach (var list in augmentations)
        {
            foreach (var augmentation in list)
            {
                if (!IsGlobalScopeAugmentation(augmentation.Parent!))
                {
                    MergeModuleAugmentation(augmentation);
                }
            }
        }
    }

    /// <summary>Go: checker.go:1398 <c>mergeGlobalSymbol</c>。</summary>
    internal void MergeGlobalSymbol(Symbol symbol)
    {
        var globalSymbol = _globals.GetValueOrDefault(symbol.Name);
        Symbol merged;
        if (globalSymbol is not null)
        {
            merged = MergeSymbol(globalSymbol, symbol, false /*unidirectional*/);
        }
        else
        {
            merged = GetMergedSymbolWorker(symbol)!;
        }
        _globals[symbol.Name] = merged;
    }

    // Pattern ambient modules are merged together if they have the same pattern and identical import attributes type.
    /// <summary>Go: checker.go:1410 <c>mergePatternAmbientModules</c>。</summary>
    internal void MergePatternAmbientModules()
    {
        var groupsByPattern = new Dictionary<string, List<int>>();
        var grouped = new List<PatternAmbientModule>(_patternAmbientModules.Count);
        foreach (var module in _patternAmbientModules)
        {
            var attributesType = GetTypeOfModuleImportAttributes(module.Symbol!);
            var groupIndex = -1;
            if (groupsByPattern.TryGetValue(module.Pattern.Text, out var groupIndices))
            {
                foreach (var index in groupIndices)
                {
                    if (IsTypeIdenticalTo(attributesType, GetTypeOfModuleImportAttributes(grouped[index].Symbol!)))
                    {
                        groupIndex = index;
                        break;
                    }
                }
            }
            if (groupIndex == -1)
            {
                if (!groupsByPattern.TryGetValue(module.Pattern.Text, out var indices))
                {
                    indices = [];
                    groupsByPattern[module.Pattern.Text] = indices;
                }
                indices.Add(grouped.Count);
                grouped.Add(new PatternAmbientModule { Pattern = module.Pattern, Symbol = module.Symbol });
            }
            else
            {
                grouped[groupIndex].Symbol = MergeSymbol(grouped[groupIndex].Symbol!, module.Symbol!, false /*unidirectional*/);
            }
        }
        foreach (var module in _patternAmbientModules)
        {
            if (module.Symbol is not null && _globals.ContainsKey(module.Symbol.Name))
            {
                _globals[module.Symbol.Name] = GetMergedSymbolWorker(module.Symbol)!;
            }
        }
        _patternAmbientModules = grouped;
    }

    /// <summary>Go: checker.go:1437 <c>mergeModuleAugmentation</c>。</summary>
    internal void MergeModuleAugmentation(Node moduleName)
    {
        var moduleNode = moduleName.Parent!;
        var moduleAugmentation = moduleNode.AsModuleDeclaration()!;
        if (!ReferenceEquals(moduleAugmentation.Symbol!.Declarations![0], moduleNode))
        {
            // this is a combined symbol for multiple augmentations within the same file.
            // its symbol already has accumulated information for all declarations
            // so we need to add it just once - do the work only for first declaration
            return;
        }
        if (IsGlobalScopeAugmentation(moduleNode))
        {
            MergeSymbolTable(_globals, moduleAugmentation.Symbol.Exports!, false /*unidirectional*/, null /*parent*/);
        }
        else
        {
            // find a module that about to be augmented
            // do not validate names of augmentations that are defined in ambient context
            Message? moduleNotFoundError = null;
            if ((moduleName.Parent!.Parent!.Flags & NodeFlags.Ambient) == 0)
            {
                moduleNotFoundError = Diagnostics.Invalid_module_name_in_augmentation_module_0_cannot_be_found;
            }
            // We ban import attributes on module augmentation declarations.
            var mainModule = ResolveExternalModuleNameWorkerImpl(moduleName, moduleName, moduleNotFoundError, false /*ignoreErrors*/, true /*isForAugmentation*/, null /*importAttributesType*/);
            if (mainModule is null)
            {
                return;
            }
            // obtain item referenced by 'export='
            mainModule = ResolveExternalModuleSymbolWorker(mainModule, false /*dontResolveAlias*/)!;
            if ((mainModule.Flags & SymbolFlags.Namespace) != 0)
            {
                // If we're merging an augmentation to a pattern ambient module, we want to
                // perform the merge unidirectionally from the augmentation ('a.foo') to
                // the pattern ('*.foo'), so that 'getMergedSymbol()' on a.foo gives you
                // all the exports both from the pattern and from the augmentation, but
                // 'getMergedSymbol()' on *.foo only gives you exports from *.foo.
                // Go: core.Some(c.patternAmbientModules, func(module) bool { return mainModule == c.getMergedSymbol(module.Symbol) })
                var isPatternAmbientModule = false;
                foreach (var module in _patternAmbientModules)
                {
                    if (module.Symbol is not null && ReferenceEquals(mainModule, GetMergedSymbolWorker(module.Symbol)))
                    {
                        isPatternAmbientModule = true;
                        break;
                    }
                }
                if (isPatternAmbientModule)
                {
                    var merged = MergeSymbol(moduleAugmentation.Symbol, mainModule, true /*unidirectional*/);
                    // moduleName will be a StringLiteral since this is not `declare global`.
                    GetSymbolTable(ref _patternAmbientModuleAugmentations)[moduleName.Text()] = merged;
                    GetSymbolTable(ref _patternAmbientModuleAugmentationTargets)[moduleName.Text()] = mainModule;
                }
                else
                {
                    if (mainModule.Exports is not null &&
                        mainModule.Exports.GetValueOrDefault(InternalSymbolNameExportStar) is not null &&
                        moduleAugmentation.Symbol.Exports is { Count: > 0 })
                    {
                        // We may need to merge the module augmentation's exports into the target symbols of the resolved exports
                        var resolvedExports = GetResolvedMembersOrExportsOfSymbol(mainModule, MembersOrExportsResolutionKind.ResolvedExports);
                        foreach (var (key, value) in moduleAugmentation.Symbol.Exports)
                        {
                            if (resolvedExports.GetValueOrDefault(key) is not null && mainModule.Exports.GetValueOrDefault(key) is null)
                            {
                                MergeSymbol(resolvedExports[key], value, false /*unidirectional*/);
                            }
                        }
                    }
                    MergeSymbol(mainModule, moduleAugmentation.Symbol, false /*unidirectional*/);
                }
            }
            else
            {
                // moduleName will be a StringLiteral since this is not `declare global`.
                Error(moduleName, Diagnostics.Cannot_augment_module_0_because_it_resolves_to_a_non_module_entity, moduleName.Text());
            }
        }
    }

    /// <summary>Go: checker.go:1494 <c>addUndefinedToGlobalsOrErrorOnRedeclaration</c>。</summary>
    internal void AddUndefinedToGlobalsOrErrorOnRedeclaration()
    {
        var name = _undefinedSymbol.Name;
        var targetSymbol = _globals.GetValueOrDefault(name);
        if (targetSymbol is not null)
        {
            foreach (var declaration in targetSymbol.Declarations ?? [])
            {
                if (!IsTypeDeclaration(declaration))
                {
                    AddDiagnostic(CreateDiagnosticForNode(declaration, Diagnostics.Declaration_name_conflicts_with_built_in_global_identifier_0, name));
                }
            }
        }
        else
        {
            _globals[name] = _undefinedSymbol;
        }
    }

    /// <summary>Go: checker.go:1508 <c>createNameResolver</c>。</summary>
    internal NameResolver CreateNameResolver()
    {
        return new NameResolver
        {
            CompilerOptions = _compilerOptions,
            GetSymbolOfDeclaration = GetSymbolOfDeclaration,
            Error = Error,
            Globals = _globals,
            ArgumentsSymbol = _argumentsSymbol,
            RequireSymbol = _requireSymbol,
            Lookup = GetSymbol,
            SymbolReferenced = SymbolReferenced,
            SetRequiresScopeChangeCache = SetRequiresScopeChangeCache,
            GetRequiresScopeChangeCache = GetRequiresScopeChangeCache,
            OnPropertyWithInvalidInitializer = CheckAndReportErrorForInvalidInitializer,
            OnFailedToResolveSymbol = OnFailedToResolveSymbol,
            OnSuccessfullyResolvedSymbol = OnSuccessfullyResolvedSymbol,
        };
    }

    /// <summary>Go: checker.go:1526 <c>createNameResolverForSuggestion</c>。</summary>
    internal NameResolver CreateNameResolverForSuggestion()
    {
        return new NameResolver
        {
            CompilerOptions = _compilerOptions,
            GetSymbolOfDeclaration = GetSymbolOfDeclaration,
            Error = Error,
            Globals = _globals,
            ArgumentsSymbol = _argumentsSymbol,
            RequireSymbol = _requireSymbol,
            Lookup = GetSuggestionForSymbolNameLookup,
            SymbolReferenced = SymbolReferenced,
            SetRequiresScopeChangeCache = SetRequiresScopeChangeCache,
            GetRequiresScopeChangeCache = GetRequiresScopeChangeCache,
        };
    }

    /// <summary>Go: checker.go:1541 <c>symbolReferenced</c>。</summary>
    internal void SymbolReferenced(Symbol symbol, SymbolFlags meaning)
    {
        _symbolReferenceLinks.Get(symbol)._referenceKinds |= meaning;
    }

    /// <summary>Go: checker.go:1545 <c>getRequiresScopeChangeCache</c>。</summary>
    internal Tristate GetRequiresScopeChangeCache(Node node)
    {
        return _nodeLinks.Get(node)._declarationRequiresScopeChange;
    }

    /// <summary>Go: checker.go:1549 <c>setRequiresScopeChangeCache</c>。</summary>
    internal void SetRequiresScopeChangeCache(Node node, Tristate value)
    {
        _nodeLinks.Get(node)._declarationRequiresScopeChange = value;
    }
}

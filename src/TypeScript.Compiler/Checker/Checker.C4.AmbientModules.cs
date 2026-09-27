// ported from: internal/checker/checker.go:15690-16052
//   （tryResolvePatternAmbientModule / resolutionExtensionIsTSOrJson / getSuggestedImportSource /
//    getSuggestedImportExtension / errorOnImplicitAnyModule / createModuleNotFoundChain / createModeMismatchDetails /
//    tryFindAmbientModule / GetAmbientModules / resolveExternalModuleSymbol / resolveESModuleSymbol / hasSignatures /
//    isESMFormatImportImportingCommonjsFormatFile / getTypeWithSyntheticDefaultOnly /
//    getTypeWithSyntheticDefaultImportType / isCommonJSRequire / createDefaultPropertyWrapperForModule /
//    cloneTypeAsModuleType）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) `sync.Once`（`c.ambientModulesOnce.Do(...)`）→ C1 已落地的"标志 + `System.Threading.Lock`"双重检查
//    （`_ambientModulesOnce` / `_ambientModulesOnceLock`；语义与 Go 的 Once 一致：零次或一次、并发安全）。
// 2) `seen := make(map[*ast.Symbol]struct{})` → `HashSet<Symbol>`（Go 的 map-set → C# 集合）。
// 3) `c.patternAmbientModuleAugmentations[moduleReference]` 等 Go map 读 → `GetValueOrDefault`。
// 4) `maps.Clone` / `slices.Clone` → 拷贝构造 / `(Node[])x.Clone()`（nil 保持 nil，与 Go 一致）。
// 5) 包级函数 → 同一类的 static 成员：`resolutionExtensionIsTSOrJson` → `ResolutionExtensionIsTSOrJson`、
//    `isESMFormatImportImportingCommonjsFormatFile` → `IsESMFormatImportImportingCommonjsFormatFile`。
// 6) `CreateModuleNotFoundChain` / `CreateModeMismatchDetails`（utilities.go 的包级函数，ChkLinksUtils 已落在
//    `Checker/Utilities.Program.cs`）与本类的同名实例方法重载共存：按实参类型即可区分（`Program` vs `ResolvedModule` /
//    `SourceFile`）。这两个 Go 包级函数名与 Go 方法名同名正是 docs/checker-plan.md §5.2 的情形，这里不额外改名——
//    C# 允许静态/实例重载并存，且调用点实参类型唯一确定重载。
// 7) `c.tryFindAmbientModule` → `TryFindAmbientModuleWorker`、`c.resolveExternalModuleSymbol` →
//    `ResolveExternalModuleSymbolWorker`、`c.getMergedSymbol` → `GetMergedSymbolWorker`、`c.resolveSymbolEx` →
//    `ResolveSymbolEx`（`artifacts/checker-method-collisions.md`）。
// 8) `CachedTypeKey{kind: …, typeId: t.id}` → `new CachedTypeKey(CachedTypeKind.…, t._id)`（ChkC5 的
//    `CachedTypeKey(CachedTypeKind, TypeId)` 构造器 + `_cachedTypes.GetValueOrDefault`）。
// 9) `panic("Expected identifier for require call")` → `Debug.Fail(...)` + `throw new UnreachableException()`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Module;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) tryResolvePatternAmbientModule(resolvedSymbol *ast.Symbol, moduleReference string, importAttributesType *Type) *ast.Symbol</c>。</summary>
    internal Symbol? TryResolvePatternAmbientModule(Symbol? resolvedSymbol, string moduleReference, Type importAttributesType)
    {
        if (IsEmptyObjectType(importAttributesType) && resolvedSymbol is not null)
        {
            return resolvedSymbol;
        }

        if (_patternAmbientModules.Count != 0)
        {
            var candidates = Filter(_patternAmbientModules.ToArray(), v =>
            {
                var moduleAttributesType = GetTypeOfModuleImportAttributes(v.Symbol!);
                return v.Pattern.Matches(moduleReference) && IsTypeAssignableToWorker(importAttributesType, moduleAttributesType);
            });

            if (candidates.Length > 0)
            {
                var augmentation = _patternAmbientModuleAugmentations?.GetValueOrDefault(moduleReference);
                var augmentationTarget = _patternAmbientModuleAugmentationTargets?.GetValueOrDefault(moduleReference);

                if (candidates.Length == 1)
                {
                    var mergedCandidate = GetMergedSymbolWorker(candidates[0].Symbol)!;
                    if (augmentation is not null && augmentationTarget == mergedCandidate)
                    {
                        return GetMergedSymbolWorker(augmentation);
                    }

                    return mergedCandidate;
                }

                List<PatternAmbientModule> bestTypeCandidates = [];
                for (int i = 0; i < candidates.Length; i++)
                {
                    var candidate = candidates[i];
                    var candidateType = GetTypeOfModuleImportAttributes(candidate.Symbol!);
                    var skip = false;
                    for (int j = 0; j < candidates.Length; j++)
                    {
                        var other = candidates[j];
                        var otherType = GetTypeOfModuleImportAttributes(other.Symbol!);
                        if (i != j && IsTypeStrictSubtypeOf(otherType, candidateType) && !IsTypeIdenticalTo(otherType, candidateType))
                        {
                            // Go: `continue outer`
                            skip = true;
                            break;
                        }
                    }

                    if (skip)
                    {
                        continue;
                    }

                    bestTypeCandidates.Add(candidate);
                }

                if (bestTypeCandidates.Count == 1)
                {
                    var mergedCandidate = GetMergedSymbolWorker(bestTypeCandidates[0].Symbol)!;
                    if (augmentation is not null && augmentationTarget == mergedCandidate)
                    {
                        return GetMergedSymbolWorker(augmentation);
                    }

                    return mergedCandidate;
                }

                var pattern = FindBestPatternMatch(bestTypeCandidates.ToArray(), v => v.Pattern, moduleReference)!;
                var mergedPatternCandidate = GetMergedSymbolWorker(pattern.Symbol)!;
                if (augmentation is not null && augmentationTarget == mergedPatternCandidate)
                {
                    return GetMergedSymbolWorker(augmentation);
                }

                return mergedPatternCandidate;
            }
        }

        return resolvedSymbol;
    }

    /// <summary>Go（包级）: <c>func resolutionExtensionIsTSOrJson(ext string) bool</c>。</summary>
    internal static bool ResolutionExtensionIsTSOrJson(string ext)
    {
        return ExtensionIsTs(ext) || ext == ExtensionJson;
    }

    /// <summary>Go: <c>func (c *Checker) getSuggestedImportSource(moduleReference string, tsExtension string, mode core.ResolutionMode) string</c>。</summary>
    internal string GetSuggestedImportSource(string moduleReference, string tsExtension, ModuleKind mode)
    {
        var importSourceWithoutExtension = RemoveExtension(moduleReference, tsExtension);

        // Direct users to import source with .js extension if outputting an ES module.
        // @see https://github.com/microsoft/TypeScript/issues/42151
        if (_moduleKind.IsNonNodeESM() || mode == ModuleKind.ESNext)
        {
            var preferTs = IsDeclarationFileName(moduleReference) && _compilerOptions.GetAllowImportingTsExtensions();
            string ext;
            if (tsExtension == ExtensionMts || tsExtension == ExtensionDmts)
            {
                ext = IfElse(preferTs, ".mts", ".mjs");
            }
            else if (tsExtension == ExtensionCts || tsExtension == ExtensionDcts)
            {
                ext = IfElse(preferTs, ".cts", ".cjs");
            }
            else
            {
                ext = IfElse(preferTs, ".ts", ".js");
            }

            return importSourceWithoutExtension + ext;
        }

        return importSourceWithoutExtension;
    }

    /// <summary>Go: <c>func (c *Checker) getSuggestedImportExtension(extensionlessImportPath string) string</c>。</summary>
    internal string GetSuggestedImportExtension(string extensionlessImportPath)
    {
        if (_program.FileExists(extensionlessImportPath + ".mts"))
        {
            return ".mjs";
        }

        if (_program.FileExists(extensionlessImportPath + ".ts"))
        {
            return ".js";
        }

        if (_program.FileExists(extensionlessImportPath + ".cts"))
        {
            return ".cjs";
        }

        if (_program.FileExists(extensionlessImportPath + ".mjs"))
        {
            return ".mjs";
        }

        if (_program.FileExists(extensionlessImportPath + ".js"))
        {
            return ".js";
        }

        if (_program.FileExists(extensionlessImportPath + ".cjs"))
        {
            return ".cjs";
        }

        if (_program.FileExists(extensionlessImportPath + ".tsx"))
        {
            return IfElse(_compilerOptions.Jsx == JsxEmit.Preserve, ".jsx", ".js");
        }

        if (_program.FileExists(extensionlessImportPath + ".jsx"))
        {
            return ".jsx";
        }

        if (_program.FileExists(extensionlessImportPath + ".json"))
        {
            return ".json";
        }

        return "";
    }

    /// <summary>Go: <c>func (c *Checker) errorOnImplicitAnyModule(isError bool, errorNode *ast.Node, mode core.ResolutionMode, resolvedModule *module.ResolvedModule, moduleReference string)</c>。</summary>
    internal void ErrorOnImplicitAnyModule(bool isError, Node errorNode, ModuleKind mode, ResolvedModule resolvedModule, string moduleReference)
    {
        if (IsSideEffectImport(errorNode))
        {
            return;
        }

        Diagnostic? errorInfo = null;
        if (!IsExternalModuleNameRelative(moduleReference) && resolvedModule.PackageId.Name != "")
        {
            errorInfo = CreateModuleNotFoundChain(resolvedModule, errorNode, moduleReference, mode, resolvedModule.PackageId.Name);
        }

        AddErrorOrSuggestion(
            isError,
            NewDiagnosticChainForNode(
                errorInfo,
                errorNode,
                Could_not_find_a_declaration_file_for_module_0_1_implicitly_has_an_any_type,
                moduleReference,
                resolvedModule.ResolvedFileName));
    }

    /// <summary>Go: <c>func (c *Checker) createModuleNotFoundChain(resolvedModule *module.ResolvedModule, errorNode *ast.Node, moduleReference string, mode core.ResolutionMode, packageName string) *ast.Diagnostic</c>。</summary>
    internal Diagnostic CreateModuleNotFoundChain(ResolvedModule resolvedModule, Node errorNode, string moduleReference, ModuleKind mode, string packageName)
    {
        // Store the original packageName for repopulateInfo before any modifications
        var storedPackageName = packageName;
        if (storedPackageName == moduleReference)
        {
            storedPackageName = "";
        }

        var details = CreateModuleNotFoundChain(_program, GetSourceFileOfNode(errorNode)!, moduleReference, mode, packageName);
        var result = NewDiagnosticForNode(errorNode, details.Message, details.Args ?? []);
        result.SetRepopulateInfo(new RepopulateDiagnosticInfo
        {
            Kind = RepopulateDiagnosticKind.ModuleNotFound,
            ModuleReference = moduleReference,
            Mode = mode,
            PackageName = storedPackageName,
        });
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) createModeMismatchDetails(sourceFile *ast.SourceFile, errorNode *ast.Node) *ast.Diagnostic</c>。</summary>
    internal Diagnostic CreateModeMismatchDetails(SourceFile sourceFile, Node errorNode)
    {
        var details = CreateModeMismatchDetails(_program, sourceFile);
        var result = NewDiagnosticForNode(errorNode, details.Message, details.Args ?? []);
        result.SetRepopulateInfo(new RepopulateDiagnosticInfo
        {
            Kind = RepopulateDiagnosticKind.ModeMismatch,
        });
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) tryFindAmbientModule(moduleName string, withAugmentations bool) *ast.Symbol</c>（C# 名加 Worker 后缀，见文件头 7）。</summary>
    internal Symbol? TryFindAmbientModuleWorker(string moduleName, bool withAugmentations)
    {
        if (IsExternalModuleNameRelative(moduleName))
        {
            return null;
        }

        var symbol = GetSymbol(_globals!, "\"" + moduleName + "\"", SymbolFlags.ValueModule);

        // merged symbol is module declaration symbol combined with all augmentations
        if (withAugmentations)
        {
            return GetMergedSymbolWorker(symbol);
        }

        return symbol;
    }

    /// <summary>Go: <c>func (c *Checker) GetAmbientModules() []*ast.Symbol</c>。</summary>
    public Symbol[] GetAmbientModules()
    {
        if (!_ambientModulesOnce)
        {
            lock (_ambientModulesOnceLock)
            {
                if (!_ambientModulesOnce)
                {
                    var seen = new HashSet<Symbol>();
                    foreach (var (sym, global) in _globals!)
                    {
                        if (IsAmbientModuleSymbolName(sym))
                        {
                            _ambientModules.Add(global);
                            seen.Add(global);
                        }
                    }

                    foreach (var module in _patternAmbientModules)
                    {
                        var symbol = GetMergedSymbolWorker(module.Symbol)!;
                        if (!seen.Contains(symbol))
                        {
                            _ambientModules.Add(symbol);
                            seen.Add(symbol);
                        }
                    }

                    _ambientModulesOnce = true;
                }
            }
        }

        return _ambientModules.ToArray();
    }

    /// <summary>Go: <c>func (c *Checker) resolveExternalModuleSymbol(moduleSymbol *ast.Symbol, dontResolveAlias bool) *ast.Symbol</c>（C# 名加 Worker 后缀，见文件头 7）。</summary>
    internal Symbol? ResolveExternalModuleSymbolWorker(Symbol? moduleSymbol, bool dontResolveAlias)
    {
        if (moduleSymbol is not null)
        {
            var exportEquals = ResolveSymbolEx(moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolNameExportEquals), dontResolveAlias);
            if (exportEquals is not null)
            {
                return GetMergedSymbolWorker(exportEquals);
            }
        }

        return moduleSymbol;
    }

    // Resolves the given external module symbol, possibly removing call and construct signatures or creating a
    // wrapper module with a synthetic default.
    /// <summary>Go: <c>func (c *Checker) resolveESModuleSymbol(moduleSymbol *ast.Symbol, node *ast.Node, moduleSpecifier *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? ResolveESModuleSymbol(Symbol? moduleSymbol, Node node, Node? moduleSpecifier)
    {
        var symbol = ResolveExternalModuleSymbolWorker(moduleSymbol, true /*dontResolveAlias*/);
        if (IsNonLocalAlias(symbol, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace))
        {
            // When the module has an export= with a pure alias, we transitively resolve and propagate any typeOnlyDeclaration
            symbol = GetMergedSymbolWorker(ResolveIndirectionAlias(GetSymbolOfDeclaration(node)!, symbol!));
        }

        if (symbol is not null)
        {
            var referenceParent = moduleSpecifier!.Parent!;
            Node? namespaceImport = null;
            if (IsImportDeclaration(referenceParent))
            {
                namespaceImport = GetNamespaceDeclarationNode(referenceParent);
            }

            if (namespaceImport is not null || IsImportCall(referenceParent))
            {
                Node reference;
                if (IsImportCall(referenceParent))
                {
                    reference = referenceParent.Arguments()![0];
                }
                else
                {
                    reference = referenceParent.ModuleSpecifier()!;
                }

                var typ = GetTypeOfSymbolWorker(symbol);
                var defaultOnlyType = GetTypeWithSyntheticDefaultOnly(typ, symbol, moduleSymbol, reference, GetImportAttributesTypeForModuleSpecifier(reference));
                if (defaultOnlyType is not null)
                {
                    return CloneTypeAsModuleType(symbol, defaultOnlyType, referenceParent);
                }

                // Go: moduleSymbol.Declarations —— 此处 `symbol != nil` 蕴含 `moduleSymbol != nil`
                //（resolveExternalModuleSymbol 是恒等或被 moduleSymbol.Exports 派生的别名），故与 Go 同为非 nil。
                var targetFile = Find(moduleSymbol!.Declarations, IsSourceFile);
                var usageMode = GetEmitSyntaxForModuleSpecifierExpression(reference);
                Symbol? exportModuleDotExportsSymbol = null;
                if (namespaceImport is not null && targetFile is not null
                    && ModuleKind.Node20 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext
                    && usageMode == ModuleKind.CommonJS
                    && _program.GetImpliedNodeFormatForEmit(targetFile.AsSourceFile()!) == ModuleKind.ESNext)
                {
                    exportModuleDotExportsSymbol = GetExportOfModule(symbol, InternalSymbolNameModuleExports, namespaceImport, true /*dontResolveAlias*/);
                }

                if (exportModuleDotExportsSymbol is not null)
                {
                    if (HasSignatures(typ))
                    {
                        return CloneTypeAsModuleType(exportModuleDotExportsSymbol, typ, referenceParent);
                    }

                    return exportModuleDotExportsSymbol;
                }

                var isEsmCjsRef = targetFile is not null
                    && IsESMFormatImportImportingCommonjsFormatFile(usageMode, _program.GetImpliedNodeFormatForEmit(targetFile.AsSourceFile()!));
                if (HasSignatures(typ)
                    || GetPropertyOfTypeEx(typ, InternalSymbolNameDefault, true /*skipObjectFunctionPropertyAugment*/, false /*includeTypeOnlyMembers*/) is not null
                    || isEsmCjsRef)
                {
                    Type moduleType;
                    if ((typ.Flags() & TypeFlags.StructuredType) != 0)
                    {
                        moduleType = GetTypeWithSyntheticDefaultImportType(typ, symbol, moduleSymbol, reference)!;
                    }
                    else
                    {
                        moduleType = CreateDefaultPropertyWrapperForModule(symbol, symbol.Parent, null);
                    }

                    return CloneTypeAsModuleType(symbol, moduleType!, referenceParent);
                }
            }
        }

        return symbol;
    }

    /// <summary>Go: <c>func (c *Checker) hasSignatures(t *Type) bool</c>。</summary>
    internal bool HasSignatures(Type t)
    {
        // Go: len(nil) == 0
        return (GetSignaturesOfStructuredType(t, SignatureKind.Call) ?? []).Length > 0
            || (GetSignaturesOfStructuredType(t, SignatureKind.Construct) ?? []).Length > 0;
    }

    /// <summary>Go（包级）: <c>func isESMFormatImportImportingCommonjsFormatFile(usageMode, targetMode core.ResolutionMode) bool</c>。</summary>
    internal static bool IsESMFormatImportImportingCommonjsFormatFile(ModuleKind usageMode, ModuleKind targetMode)
    {
        return usageMode == ModuleKind.ESNext && targetMode == ModuleKind.CommonJS;
    }

    /// <summary>Go: <c>func (c *Checker) getTypeWithSyntheticDefaultOnly(t *Type, symbol *ast.Symbol, originalSymbol *ast.Symbol, moduleSpecifier *ast.Node, importAttributesType *Type) *Type</c>。</summary>
    internal Type? GetTypeWithSyntheticDefaultOnly(Type t, Symbol symbol, Symbol? originalSymbol, Node moduleSpecifier, Type? importAttributesType)
    {
        var hasDefaultOnly = IsOnlyImportableAsDefault(moduleSpecifier, null, importAttributesType);
        if (hasDefaultOnly && t is not null && !IsErrorType(t))
        {
            var key = new CachedTypeKey(CachedTypeKind.DefaultOnlyType, t._id);
            var cached = _cachedTypes.GetValueOrDefault(key);
            if (cached is not null)
            {
                return cached;
            }

            var result = CreateDefaultPropertyWrapperForModule(symbol, originalSymbol, null);
            _cachedTypes[key] = result;
            return result;
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getTypeWithSyntheticDefaultImportType(t *Type, symbol *ast.Symbol, originalSymbol *ast.Symbol, moduleSpecifier *ast.Node) *Type</c>。</summary>
    internal Type? GetTypeWithSyntheticDefaultImportType(Type t, Symbol symbol, Symbol originalSymbol, Node moduleSpecifier)
    {
        if (t is not null && !IsErrorType(t))
        {
            var key = new CachedTypeKey(CachedTypeKind.SyntheticType, t._id);
            var cached = _cachedTypes.GetValueOrDefault(key);
            if (cached is not null)
            {
                return cached;
            }

            var file = Find(originalSymbol.Declarations, IsSourceFile);
            var hasSyntheticDefault = CanHaveSyntheticDefault(file, originalSymbol, false /*dontResolveAlias*/, moduleSpecifier);
            Type syntheticType;
            if (hasSyntheticDefault)
            {
                var anonymousSymbol = NewSymbol(SymbolFlags.TypeLiteral, InternalSymbolNameType);
                anonymousSymbol.Declarations = originalSymbol.Declarations;
                var defaultContainingObject = CreateDefaultPropertyWrapperForModule(symbol, originalSymbol, anonymousSymbol);
                _valueSymbolLinks.Get(anonymousSymbol)._resolvedType = defaultContainingObject;
                if (IsValidSpreadType(t))
                {
                    syntheticType = GetSpreadType(t, defaultContainingObject, anonymousSymbol, default /*objectFlags*/, false /*readonly*/);
                }
                else
                {
                    syntheticType = defaultContainingObject;
                }
            }
            else
            {
                syntheticType = t;
            }

            _cachedTypes[key] = syntheticType;
            return syntheticType;
        }

        return t;
    }

    /// <summary>Go: <c>func (c *Checker) isCommonJSRequire(node *ast.Node) bool</c>。</summary>
    internal bool IsCommonJSRequire(Node node)
    {
        if (!IsRequireCall(node, true /*requireStringLiteralLikeArgument*/))
        {
            return false;
        }

        if (!IsIdentifier(node.Expression()!))
        {
            Debug.Fail("Expected identifier for require call");
            throw new System.Diagnostics.UnreachableException();
        }

        // Make sure require is not a local function
        var resolvedRequire = _resolveName(node.Expression()!, node.Expression()!.Text(), SymbolFlags.Value, null /*nameNotFoundMessage*/, true /*isUse*/, false /*excludeGlobals*/);
        if (resolvedRequire == _requireSymbol)
        {
            return true;
        }

        // project includes symbol named 'require' - make sure that it is ambient and local non-alias
        if (resolvedRequire is null || (resolvedRequire.Flags & SymbolFlags.Alias) != 0)
        {
            return false;
        }

        Kind targetDeclarationKind;
        if ((resolvedRequire.Flags & SymbolFlags.Function) != 0)
        {
            targetDeclarationKind = Kind.FunctionDeclaration;
        }
        else if ((resolvedRequire.Flags & SymbolFlags.Variable) != 0)
        {
            targetDeclarationKind = Kind.VariableDeclaration;
        }
        else
        {
            targetDeclarationKind = Kind.Unknown;
        }

        if (targetDeclarationKind != Kind.Unknown)
        {
            var decl = GetDeclarationOfKind(resolvedRequire, targetDeclarationKind);

            // function/variable declaration should be ambient
            return decl is not null && (decl.Flags & NodeFlags.Ambient) != 0;
        }

        return false;
    }

    /// <summary>Go: <c>func (c *Checker) createDefaultPropertyWrapperForModule(symbol *ast.Symbol, originalSymbol *ast.Symbol, anonymousSymbol *ast.Symbol) *Type</c>。</summary>
    internal Type CreateDefaultPropertyWrapperForModule(Symbol symbol, Symbol? originalSymbol, Symbol? anonymousSymbol)
    {
        var memberTable = new SymbolTable();
        var newSymbol = NewSymbol(SymbolFlags.Alias, InternalSymbolNameDefault);
        newSymbol.Parent = originalSymbol;
        _valueSymbolLinks.Get(newSymbol)._nameType = GetStringLiteralType("default");
        _aliasSymbolLinks.Get(newSymbol)._aliasTarget = ResolveSymbol(symbol);
        memberTable[InternalSymbolNameDefault] = newSymbol;
        if (anonymousSymbol is null && originalSymbol is not null)
        {
            anonymousSymbol = NewSymbol(SymbolFlags.ObjectLiteral, InternalSymbolNameObject);
            anonymousSymbol.Declarations = originalSymbol.Declarations;
        }

        return NewAnonymousType(anonymousSymbol!, memberTable, null, null, null);
    }

    /// <summary>Go: <c>func (c *Checker) cloneTypeAsModuleType(symbol *ast.Symbol, moduleType *Type, referenceParent *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol CloneTypeAsModuleType(Symbol symbol, Type moduleType, Node referenceParent)
    {
        var result = NewSymbol(symbol.Flags, symbol.Name);
        result.Declarations = symbol.Declarations is null ? null : (Node[])symbol.Declarations.Clone();
        result.ValueDeclaration = symbol.ValueDeclaration;
        result.Members = symbol.Members is null ? null : CloneSymbolTable(symbol.Members);
        result.Exports = symbol.Exports is null ? null : CloneSymbolTable(symbol.Exports);
        result.Parent = symbol.Parent;
        var links = _exportTypeLinks.Get(result);
        links._target = symbol;
        links._originatingImport = referenceParent;
        var resolvedModuleType = ResolveStructuredTypeMembers(moduleType);
        _valueSymbolLinks.Get(result)._resolvedType = NewAnonymousType(result, resolvedModuleType._members, null, null, resolvedModuleType._indexInfos);
        return result;
    }
}

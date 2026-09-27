// ported from: internal/checker/checker.go:15393-15689
//   （resolveExternalModule：模块解析 + 诊断的主函数，~300 行）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) `strings.HasPrefix(moduleReference, "@types/")` → `HasPrefix(moduleReference, "@types/")`
//    （`using static TypeScript.StringUtil.StringUtil;`）；`strings.Contains(a, b)` → `Contains(a, b)`。
// 2) `moduleReference[len("@types/"):]` → `moduleReference["@types/".Length..]`（Go 的字节切片 → C# 的
//    字符串切片视图；这里是 UTF-16 代码单元下标，`@types/` 全 ASCII，两者等价）。
// 3) `panic("should be able to extract TS extension …")` → `Debug.Fail(...)` + `throw new UnreachableException()`。
// 4) `tspath.ComparePathsOptions{Field: v, …}` → `new ComparePathsOptions { Field = v, … }`（C# 对象初始化器）。
// 5) `core.ResolutionModeESM` → `ResolutionModeESM`（`Core` 里的 `ModuleKind` 常量，`using static` 后裸名）。
// 6) `module.GetResolutionDiagnostic(...)`（Go 的 `internal/module` 包级函数）→ 别名 `CheckerModule.GetResolutionDiagnostic(...)`
//    （裸写 `Module.GetResolutionDiagnostic` 会解析到命名空间 `TypeScript.Module`）。
// 7) `c.program.*` → `_program.*`：Program（compiler 包，M7）尚未落地，这些成员是**预期缺失的跨片符号**（见报告）。
//    这里一律按 Go 的成员名照写（`GetModeForUsageLocation`/`GetResolvedModule`/…），等 Program 落地后即可对上。
// 8) `c.tryFindAmbientModule` → `TryFindAmbientModuleWorker`、`c.getMergedSymbol` → `GetMergedSymbolWorker`
//    （`artifacts/checker-method-collisions.md`）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Module;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using CheckerModule = TypeScript.Module.Module;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) resolveExternalModule(location, moduleReference string, moduleNotFoundError *diagnostics.Message, errorNode *ast.Node, isForAugmentation bool, importAttributesType *Type) *ast.Symbol</c>。</summary>
    internal Symbol? ResolveExternalModule(
        Node location,
        string moduleReference,
        Message? moduleNotFoundError,
        Node? errorNode,
        bool isForAugmentation,
        Type? importAttributesType)
    {
        if (errorNode is not null && HasPrefix(moduleReference, "@types/", true /*caseSensitive*/))
        {
            var withoutAtTypePrefix = moduleReference["@types/".Length..];
            Error(errorNode, Cannot_import_type_declaration_files_Consider_importing_0_instead_of_1, withoutAtTypePrefix, moduleReference);
        }

        importAttributesType ??= _emptyObjectType;

        var ambientModule = TryFindAmbientModuleWorker(moduleReference, true /*withAugmentations*/);
        if (ambientModule is not null)
        {
            return TryResolvePatternAmbientModule(ambientModule, moduleReference, importAttributesType);
        }

        var importingSourceFile = GetSourceFileOfNode(location)!;
        Node? contextSpecifier = null;
        ModuleKind mode;

        if (IsStringLiteralLike(location)
            || (location.Parent is not null && IsModuleDeclaration(location.Parent) && location.Parent.AsModuleDeclaration()!.Name() == location))
        {
            contextSpecifier = location;
        }
        else if (IsModuleDeclaration(location))
        {
            contextSpecifier = location.AsModuleDeclaration()!.Name();
        }
        else if (IsLiteralImportTypeNode(location))
        {
            contextSpecifier = location.AsImportTypeNode()!.Argument!.AsLiteralTypeNode()!.Literal;
        }
        else if (IsVariableDeclarationInitializedToBareOrAccessedRequire(location))
        {
            contextSpecifier = GetModuleSpecifierOfBareOrAccessedRequire(location);
        }
        else
        {
            var ancestor = FindAncestor(location, IsImportCall);
            if (ancestor is not null)
            {
                contextSpecifier = ancestor.Arguments()![0];
            }

            if (ancestor is null)
            {
                ancestor = FindAncestor(location, IsImportDeclarationOrJSImportDeclaration);
                if (ancestor is not null)
                {
                    contextSpecifier = ancestor.ModuleSpecifier();
                }
            }

            if (ancestor is null)
            {
                ancestor = FindAncestor(location, IsExportDeclaration);
                if (ancestor is not null)
                {
                    contextSpecifier = ancestor.ModuleSpecifier();
                }
            }

            if (ancestor is null)
            {
                ancestor = FindAncestor(location, IsImportEqualsDeclaration);
                if (ancestor is not null)
                {
                    var moduleRefrence = ancestor.AsImportEqualsDeclaration()!.ModuleReference!;
                    if (moduleRefrence.Kind == Kind.ExternalModuleReference)
                    {
                        contextSpecifier = moduleRefrence.Expression();
                    }
                }
            }
        }

        if (contextSpecifier is not null && IsStringLiteralLike(contextSpecifier))
        {
            mode = _program.GetModeForUsageLocation(importingSourceFile, contextSpecifier);
        }
        else
        {
            mode = _program.GetDefaultResolutionModeForFile(importingSourceFile);
        }

        // Go: `resolvedModule := c.program.GetResolvedModule(...)` 是**可空指针**，而 C# 的 `IsResolved()` 是实例方法
        // （不能像 Go 那样在 nil 接收者上分派，Module/Types.cs 文件头 §3.2）。此处一次性取"非空局部"：
        // 解析失败时用空的 `ResolvedModule` 作哨兵——它的 `ResolvedFileName == ""` ⇒ `IsResolved() == false`，
        // 与 Go 的 nil 在所有使用点（全部以 `IsResolved()` 为闸门）语义一致。
        var resolvedModule = _program.GetResolvedModule(importingSourceFile, moduleReference, mode) ?? new ResolvedModule();

        Message? resolutionDiagnostic = null;
        if (errorNode is not null && resolvedModule.IsResolved())
        {
            resolutionDiagnostic = CheckerModule.GetResolutionDiagnostic(_compilerOptions, resolvedModule, importingSourceFile);
        }

        SourceFile? sourceFile = null;
        if (resolvedModule.IsResolved() && (resolutionDiagnostic is null || resolutionDiagnostic == Module_0_was_resolved_to_1_but_jsx_is_not_set))
        {
            sourceFile = _program.GetSourceFileForResolvedModule(resolvedModule.ResolvedFileName);
        }

        if (sourceFile is not null)
        {
            // If there's a resolutionDiagnostic we need to report it even if a sourceFile is found.
            if (resolutionDiagnostic is not null)
            {
                Error(errorNode, resolutionDiagnostic, moduleReference, resolvedModule.ResolvedFileName);
            }

            if (errorNode is not null)
            {
                if (resolvedModule.ResolvedUsingTsExtension && IsDeclarationFileName(moduleReference))
                {
                    if (FindAncestor(location, IsEmittableImport) is not null)
                    {
                        var tsExtension = TryExtractTSExtension(moduleReference);
                        if (tsExtension == "")
                        {
                            Debug.Fail("should be able to extract TS extension from string that passes IsDeclarationFileName");
                            throw new System.Diagnostics.UnreachableException();
                        }

                        Error(
                            errorNode,
                            A_declaration_file_cannot_be_imported_without_import_type_Did_you_mean_to_import_an_implementation_file_0_instead,
                            GetSuggestedImportSource(moduleReference, tsExtension, mode));
                    }
                }
                else if (resolvedModule.ResolvedUsingTsExtension && !_compilerOptions.AllowImportingTsExtensionsFrom(importingSourceFile.FileName()))
                {
                    if (FindAncestor(location, IsEmittableImport) is not null)
                    {
                        var tsExtension = TryExtractTSExtension(moduleReference);
                        if (tsExtension == "")
                        {
                            // Fallback: do a best-effort extraction using strings.Contains.
                            // This handles cases where a wildcard pattern matches a TS extension that's
                            // not at the end of the module specifier, e.g., "#/foo.ts.omg" through "#/*.omg": "./src/*"
                            foreach (var ext in SupportedTSExtensionsFlat)
                            {
                                if (moduleReference.Contains(ext))
                                {
                                    tsExtension = ext;
                                    break;
                                }
                            }
                        }

                        if (tsExtension == "")
                        {
                            Debug.Fail("should be able to extract TS extension from string when resolvedUsingTsExtension is true");
                            throw new System.Diagnostics.UnreachableException();
                        }

                        Error(
                            errorNode,
                            An_import_path_can_only_end_with_a_0_extension_when_allowImportingTsExtensions_is_enabled,
                            tsExtension);
                    }
                }
                else if (_compilerOptions.RewriteRelativeImportExtensions.IsTrue()
                    && (location.Flags & NodeFlags.Ambient) == 0
                    && !IsDeclarationFileName(moduleReference)
                    && !IsLiteralImportTypeNode(location)
                    && !IsPartOfTypeOnlyImportOrExportDeclaration(location))
                {
                    var shouldRewrite = ShouldRewriteModuleSpecifier(moduleReference, _compilerOptions);
                    if (!resolvedModule.ResolvedUsingTsExtension && shouldRewrite)
                    {
                        var relativeToSourceFile = GetRelativePathFromFile(
                            GetNormalizedAbsolutePath(importingSourceFile.FileName(), _program.GetCurrentDirectory()),
                            resolvedModule.ResolvedFileName,
                            new ComparePathsOptions(_program.UseCaseSensitiveFileNames(), _program.GetCurrentDirectory()));
                        Error(
                            errorNode,
                            This_relative_import_path_is_unsafe_to_rewrite_because_it_looks_like_a_file_name_but_actually_resolves_to_0,
                            relativeToSourceFile);
                    }
                    else if (resolvedModule.ResolvedUsingTsExtension && !shouldRewrite && _program.SourceFileMayBeEmitted(sourceFile, false))
                    {
                        Error(
                            errorNode,
                            This_import_uses_a_0_extension_to_resolve_to_an_input_TypeScript_file_but_will_not_be_rewritten_during_emit_because_it_is_not_a_relative_path,
                            GetAnyExtensionFromPath(moduleReference, null!, false));
                    }
                    else if (resolvedModule.ResolvedUsingTsExtension && shouldRewrite)
                    {
                        var redirect = _program.GetRedirectForResolution(sourceFile);
                        if (redirect is not null)
                        {
                            var ownRootDir = _program.CommonSourceDirectory();
                            var otherRootDir = redirect.CommonSourceDirectory();

                            var compareOptions = new ComparePathsOptions(_program.UseCaseSensitiveFileNames(), _program.GetCurrentDirectory());

                            var rootDirPath = GetRelativePathFromDirectory(ownRootDir, otherRootDir, compareOptions);

                            // Get outDir paths, defaulting to root directories if not specified
                            var ownOutDir = _compilerOptions.OutDir;
                            if (ownOutDir == "")
                            {
                                ownOutDir = ownRootDir;
                            }

                            var otherOutDir = redirect.CompilerOptions()!.OutDir;
                            if (otherOutDir == "")
                            {
                                otherOutDir = otherRootDir;
                            }

                            var outDirPath = GetRelativePathFromDirectory(ownOutDir, otherOutDir, compareOptions);

                            if (rootDirPath != outDirPath)
                            {
                                Error(
                                    errorNode,
                                    This_import_path_is_unsafe_to_rewrite_because_it_resolves_to_another_project_and_the_relative_path_between_the_projects_output_files_is_not_the_same_as_the_relative_path_between_its_input_files);
                            }
                        }
                    }
                }
            }

            if (sourceFile.Symbol is not null)
            {
                if (errorNode is not null)
                {
                    if (resolvedModule.IsExternalLibraryImport && !ResolutionExtensionIsTSOrJson(resolvedModule.Extension))
                    {
                        ErrorOnImplicitAnyModule(false /*isError*/, errorNode, mode, resolvedModule, moduleReference);
                    }

                    if (_moduleKind == ModuleKind.Node16 || _moduleKind == ModuleKind.Node18)
                    {
                        var isSyncImport = (_program.GetDefaultResolutionModeForFile(importingSourceFile) == ModuleKind.CommonJS && FindAncestor(location, IsImportCall) is null)
                            || FindAncestor(location, IsImportEqualsDeclaration) is not null;
                        var overrideHost = FindAncestor(location, IsResolutionModeOverrideHost);
                        if (isSyncImport && _program.GetDefaultResolutionModeForFile(sourceFile) == ModuleKind.ESNext && !HasResolutionModeOverride(overrideHost))
                        {
                            if (FindAncestorKind(location, Kind.ImportEqualsDeclaration) is not null)
                            {
                                // ImportEquals in an ESM file resolving to another ESM file
                                Error(errorNode, Module_0_cannot_be_imported_using_this_construct_The_specifier_only_resolves_to_an_ES_module_which_cannot_be_imported_with_require_Use_an_ECMAScript_import_instead, moduleReference);
                            }
                            else
                            {
                                // CJS file resolving to an ESM file
                                Diagnostic? diagnosticDetails = null;
                                var ext = TryGetExtensionFromPath(importingSourceFile.FileName());
                                if (ext == ExtensionTs || ext == ExtensionJs || ext == ExtensionTsx || ext == ExtensionJsx)
                                {
                                    diagnosticDetails = CreateModeMismatchDetails(importingSourceFile, errorNode);
                                }

                                Message message;
                                if (overrideHost is not null && overrideHost.Kind == Kind.ImportDeclaration && overrideHost.ImportClause() is not null && overrideHost.ImportClause()!.IsTypeOnly())
                                {
                                    message = Type_only_import_of_an_ECMAScript_module_from_a_CommonJS_module_must_have_a_resolution_mode_attribute;
                                }
                                else if (overrideHost is not null && overrideHost.Kind == Kind.ImportType)
                                {
                                    message = Type_import_of_an_ECMAScript_module_from_a_CommonJS_module_must_have_a_resolution_mode_attribute;
                                }
                                else
                                {
                                    message = The_current_file_is_a_CommonJS_module_whose_imports_will_produce_require_calls_however_the_referenced_file_is_an_ECMAScript_module_and_cannot_be_imported_with_require_Consider_writing_a_dynamic_import_0_call_instead;
                                }

                                AddDiagnostic(NewDiagnosticChainForNode(diagnosticDetails, errorNode, message, moduleReference));
                            }
                        }
                    }
                }

                return TryResolvePatternAmbientModule(GetMergedSymbolWorker(sourceFile.Symbol)!, moduleReference, importAttributesType);
            }

            var patternAmbientModule = TryResolvePatternAmbientModule(null /*resolvedSymbol*/, moduleReference, importAttributesType);
            if (patternAmbientModule is not null)
            {
                return patternAmbientModule;
            }

            if (errorNode is not null && moduleNotFoundError is not null && !IsSideEffectImport(errorNode))
            {
                Error(errorNode, File_0_is_not_a_module, resolvedModule.ResolvedFileName);
            }

            return null;
        }

        var outerPatternAmbientModule = TryResolvePatternAmbientModule(null /*resolvedSymbol*/, moduleReference, importAttributesType);
        if (outerPatternAmbientModule is not null)
        {
            return outerPatternAmbientModule;
        }

        if (errorNode is null)
        {
            return null;
        }

        if ((resolvedModule.IsResolved() && !ResolutionExtensionIsTSOrJson(resolvedModule.Extension) && resolutionDiagnostic is null)
            || resolutionDiagnostic == Could_not_find_a_declaration_file_for_module_0_1_implicitly_has_an_any_type)
        {
            if (isForAugmentation)
            {
                Error(
                    errorNode,
                    Invalid_module_name_in_augmentation_Module_0_resolves_to_an_untyped_module_at_1_which_cannot_be_augmented,
                    moduleReference,
                    resolvedModule.ResolvedFileName);
            }
            else
            {
                ErrorOnImplicitAnyModule(_noImplicitAny && moduleNotFoundError is not null, errorNode, mode, resolvedModule, moduleReference);
            }

            return null;
        }

        if (moduleNotFoundError is not null)
        {
            // See if this was possibly a projectReference redirect
            if (resolvedModule.IsResolved())
            {
                var redirect = _program.GetProjectReferenceFromSource(ToPath(resolvedModule.ResolvedFileName, _program.GetCurrentDirectory(), _program.UseCaseSensitiveFileNames()));
                if (redirect is not null && redirect.OutputDts != "")
                {
                    Error(
                        errorNode,
                        Output_file_0_has_not_been_built_from_source_file_1,
                        redirect.OutputDts,
                        resolvedModule.ResolvedFileName);
                    return null;
                }
            }

            if (resolutionDiagnostic is not null)
            {
                Error(errorNode, resolutionDiagnostic, moduleReference, resolvedModule.ResolvedFileName);
            }
            else
            {
                var isExtensionlessRelativePathImport = PathIsRelative(moduleReference) && !HasExtension(moduleReference);
                var resolutionIsNode16OrNext = _moduleResolutionKind == ModuleResolutionKind.Node16 || _moduleResolutionKind == ModuleResolutionKind.NodeNext;
                if (!_compilerOptions.GetResolveJsonModule() && FileExtensionIs(moduleReference, ExtensionJson))
                {
                    Error(errorNode, Cannot_find_module_0_Consider_using_resolveJsonModule_to_import_module_with_json_extension, moduleReference);
                }
                else if (mode == ResolutionModeESM && resolutionIsNode16OrNext && isExtensionlessRelativePathImport)
                {
                    var absoluteRef = GetNormalizedAbsolutePath(moduleReference, GetDirectoryPath(importingSourceFile.FileName()));
                    var suggestedExt = GetSuggestedImportExtension(absoluteRef);
                    if (suggestedExt != "")
                    {
                        Error(errorNode, Relative_import_paths_need_explicit_file_extensions_in_ECMAScript_imports_when_moduleResolution_is_node16_or_nodenext_Did_you_mean_0, moduleReference + suggestedExt);
                    }
                    else
                    {
                        Error(errorNode, Relative_import_paths_need_explicit_file_extensions_in_ECMAScript_imports_when_moduleResolution_is_node16_or_nodenext_Consider_adding_an_extension_to_the_import_path);
                    }
                }
                else if (resolvedModule is not null && resolvedModule.AlternateResult != "")
                {
                    var errorInfo = CreateModuleNotFoundChain(resolvedModule, errorNode, moduleReference, mode, moduleReference);
                    AddDiagnostic(NewDiagnosticChainForNode(errorInfo, errorNode, moduleNotFoundError, moduleReference));
                }
                else
                {
                    Error(errorNode, moduleNotFoundError, moduleReference);
                }
            }
        }

        return null;
    }
}

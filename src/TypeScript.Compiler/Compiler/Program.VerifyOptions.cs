// ported from: internal/compiler/program.go:885-1454（verifyCompilerOptions / blockEmittingOfFile / IsEmitBlocked /
// verifyProjectReferences / hasZeroOrOneAsteriskCharacter / moduleResolutionSupportsPackageJsonExportsAndImports /
// emitModuleKindIsNonESM）
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) `core.Memoize(func() T)` → `Memoize<T>(...)`（返回 `Func<T>`），语义相同（只算一次）。
// 2) `core.Must(json.Marshal(x))` → `Encoding.UTF8.GetString(Json.Marshal(x))`（Go 的 Must 在 C# 侧没有对应物，
//    `Json.Marshal` 直接返回 `byte[]`，其文本内容即 Go 的 `string(bytes)`）。
// 3) Go 的 `AsObjectLiteralExpression()` 是断言（错类型 panic），C# 侧是安全转换（返回 null）→ 在已用
//    `IsObjectLiteralExpression` 判定过的位置写 `AsObjectLiteralExpression()!`。
// 4) `diagnostics.*` 常量用 `using static TypeScript.Diagnostics.Diagnostics;` 引入（§2）。
// 5) 包级函数（hasZeroOrOneAsteriskCharacter 等）落 `Compiler` 静态类，实例方法留在 `Program`。
namespace TypeScript.Compiler;

using System.Text;
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Parser;
using TypeScript.Scanner;
using TypeScript.TsOptions;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using Path = TypeScript.TSPath.Path;

public sealed partial class Program
{
    /// <summary>Go: <c>func (p *Program) verifyCompilerOptions()</c>（program.go:885）。</summary>
    internal void VerifyCompilerOptions()
    {
        var options = Options();

        var sourceFile = Memoize<SourceFile?>(() =>
        {
            var configFile = _opts.Config.ConfigFile;
            if (configFile is null)
            {
                return null;
            }
            return configFile.SourceFile;
        });

        var configFilePath = Memoize(() =>
        {
            var file = sourceFile();
            if (file is not null)
            {
                return file.FileName();
            }
            return "";
        });

        var getCompilerOptionsPropertySyntax = Memoize(() =>
            ForEachTsConfigPropArray<PropertyAssignment>(sourceFile(), "compilerOptions", static property => property));

        var getCompilerOptionsObjectLiteralSyntax = Memoize<ObjectLiteralExpression?>(() =>
        {
            var compilerOptionsProperty = getCompilerOptionsPropertySyntax();
            if (compilerOptionsProperty is not null &&
                compilerOptionsProperty.Initializer is not null &&
                IsObjectLiteralExpression(compilerOptionsProperty.Initializer))
            {
                return compilerOptionsProperty.Initializer.AsObjectLiteralExpression()!;
            }
            return null;
        });

        Diagnostic? CreateOptionDiagnosticInObjectLiteralSyntax(ObjectLiteralExpression? objectLiteral, bool onKey, string key1, string key2, Message message, params object?[] args)
        {
            var diag = ForEachPropertyAssignment<Diagnostic>(objectLiteral, key1, property =>
                CreateDiagnosticForNodeInSourceFile(sourceFile()!, IfElse(onKey, property.Name()!, property.Initializer!), message, args), key2);
            if (diag is not null)
            {
                _programDiagnostics = [.. _programDiagnostics, diag];
            }
            return diag;
        }

        Diagnostic CreateCompilerOptionsDiagnostic(Message message, params object?[] args)
        {
            var compilerOptionsProperty = getCompilerOptionsPropertySyntax();
            Diagnostic diag;
            if (compilerOptionsProperty is not null)
            {
                diag = CreateDiagnosticForNodeInSourceFile(sourceFile()!, compilerOptionsProperty.Name()!, message, args);
            }
            else
            {
                diag = NewCompilerDiagnostic(message, args);
            }
            _programDiagnostics = [.. _programDiagnostics, diag];
            return diag;
        }

        Diagnostic CreateDiagnosticForOption(bool onKey, string option1, string option2, Message message, params object?[] args)
        {
            var diag = CreateOptionDiagnosticInObjectLiteralSyntax(getCompilerOptionsObjectLiteralSyntax(), onKey, option1, option2, message, args);
            diag ??= CreateCompilerOptionsDiagnostic(message, args);
            return diag;
        }

        void CreateDiagnosticForOptionName(Message message, string option1, string option2, params object?[] args)
        {
            var newArgs = new object?[args.Length + 2];
            newArgs[0] = option1;
            newArgs[1] = option2;
            Array.Copy(args, 0, newArgs, 2, args.Length);
            CreateDiagnosticForOption(true /*onKey*/, option1, option2, message, newArgs);
        }

        void CreateOptionValueDiagnostic(string option1, Message message, params object?[] args)
        {
            CreateDiagnosticForOption(false /*onKey*/, option1, "", message, args);
        }

        void CreateRemovedOptionDiagnostic(string name, string value, string useInstead)
        {
            Message message;
            object?[] args;
            if (value == "")
            {
                message = Option_0_has_been_removed_Please_remove_it_from_your_configuration;
                args = [name];
            }
            else
            {
                message = Option_0_1_has_been_removed_Please_remove_it_from_your_configuration;
                args = [name, value];
            }

            var diag = CreateDiagnosticForOption(value == "", name, "", message, args);
            if (useInstead != "")
            {
                diag.AddMessageChain(NewCompilerDiagnostic(Use_0_instead, useInstead));
            }
        }

        // Removed in TS7

        if (options.BaseUrl != "")
        {
            // 到这里 BaseUrl 应该已经被转成绝对路径了
            var useInstead = "";
            if (configFilePath() != "")
            {
                var relative = GetRelativePathFromFile(configFilePath(), options.BaseUrl, _comparePathsOptions);
                if (!(relative.StartsWith("./", StringComparison.Ordinal) || relative.StartsWith("../", StringComparison.Ordinal)))
                {
                    relative = "./" + relative;
                }
                var suggestion = CombinePaths(relative, "*");
                useInstead = $"\"paths\": {{\"*\": [{Encoding.UTF8.GetString(Json.Json.Marshal(suggestion))}]}}";
            }
            CreateRemovedOptionDiagnostic("baseUrl", "", useInstead);
        }

        if (options.OutFile != "")
        {
            CreateRemovedOptionDiagnostic("outFile", "", "");
        }

        if (options.Target == ScriptTarget.ES5)
        {
            CreateRemovedOptionDiagnostic("target", "ES5", "");
        }

        if (options.Module == ModuleKind.AMD)
        {
            CreateRemovedOptionDiagnostic("module", "AMD", "");
        }
        if (options.Module == ModuleKind.System)
        {
            CreateRemovedOptionDiagnostic("module", "System", "");
        }
        if (options.Module == ModuleKind.UMD)
        {
            CreateRemovedOptionDiagnostic("module", "UMD", "");
        }

        if (options.ModuleResolution == ModuleResolutionKind.Classic)
        {
            CreateRemovedOptionDiagnostic("moduleResolution", "Classic", "");
        }

        if (options.AlwaysStrict.IsFalse())
        {
            CreateRemovedOptionDiagnostic("alwaysStrict", "false", "");
        }

        if (options.ESModuleInterop.IsFalse())
        {
            CreateRemovedOptionDiagnostic("esModuleInterop", "false", "");
        }

        if (options.AllowSyntheticDefaultImports.IsFalse())
        {
            CreateRemovedOptionDiagnostic("allowSyntheticDefaultImports", "false", "");
        }

        if (options.ModuleResolution == ModuleResolutionKind.Node10)
        {
            CreateRemovedOptionDiagnostic("moduleResolution", "node10", "");
        }

        if (!options.DownlevelIteration.IsUnknown())
        {
            CreateRemovedOptionDiagnostic("downlevelIteration", "", "");
        }

        if (options.StrictPropertyInitialization.IsTrue() && !options.GetStrictOptionValue(options.StrictNullChecks))
        {
            CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1, "strictPropertyInitialization", "strictNullChecks");
        }
        if (options.ExactOptionalPropertyTypes.IsTrue() && !options.GetStrictOptionValue(options.StrictNullChecks))
        {
            CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1, "exactOptionalPropertyTypes", "strictNullChecks");
        }

        if (options.IsolatedDeclarations.IsTrue())
        {
            if (options.GetAllowJS())
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_with_option_1, "allowJs", "isolatedDeclarations");
            }
            if (!options.GetEmitDeclarations())
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "isolatedDeclarations", "declaration", "composite");
            }
        }

        if (options.InlineSourceMap.IsTrue())
        {
            if (options.SourceMap.IsTrue())
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_with_option_1, "sourceMap", "inlineSourceMap");
            }
            if (options.MapRoot != "")
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_with_option_1, "mapRoot", "inlineSourceMap");
            }
        }

        if (options.Composite.IsTrue())
        {
            if (options.Declaration.IsFalse())
            {
                CreateDiagnosticForOptionName(Composite_projects_may_not_disable_declaration_emit, "declaration", "");
            }
            if (options.Incremental.IsFalse())
            {
                CreateDiagnosticForOptionName(Composite_projects_may_not_disable_incremental_compilation, "declaration", "");
            }
        }

        if (options.TsBuildInfoFile == "" && options.Incremental.IsTrue() && options.ConfigFilePath == "")
        {
            CreateCompilerOptionsDiagnostic(Option_incremental_is_only_valid_with_a_known_configuration_file_like_tsconfig_json_or_when_tsBuildInfoFile_is_explicitly_provided);
        }

        VerifyProjectReferences();

        if (options.Composite.IsTrue())
        {
            var rootPaths = new Set<Path>();
            foreach (var fileName in _opts.Config.FileNames())
            {
                rootPaths.Add(ToPath(fileName));
            }

            foreach (var file in _files)
            {
                var rootPath = file.Path();
                var canonical = file.CanonicalSourceFile();
                if (canonical is not null)
                {
                    rootPath = canonical.Path();
                }
                if (Compiler.SourceFileMayBeEmitted(file, this, false, false) && !rootPaths.Has(rootPath))
                {
                    _includeProcessor.AddProcessingDiagnostic(new ProcessingDiagnostic
                    {
                        Kind = ProcessingDiagnosticKind.ExplainingFileInclude,
                        Data = new IncludeExplainingDiagnostic
                        {
                            File = file.Path(),
                            Message = File_0_is_not_listed_within_the_file_list_of_project_1_Projects_must_list_all_files_or_use_an_include_pattern,
                            Args = [file.FileName(), configFilePath()],
                        },
                    });
                }
            }
        }

        Diagnostic? ForEachOptionPathsSyntax(Func<PropertyAssignment, Diagnostic?> callback) =>
            ForEachPropertyAssignment(getCompilerOptionsObjectLiteralSyntax(), "paths", callback);

        Diagnostic CreateDiagnosticForOptionPaths(bool onKey, string key, Message message, params object?[] args)
        {
            var diag = ForEachOptionPathsSyntax(pathProperty =>
            {
                if (IsObjectLiteralExpression(pathProperty.Initializer!))
                {
                    return CreateOptionDiagnosticInObjectLiteralSyntax(pathProperty.Initializer!.AsObjectLiteralExpression()!, onKey, key, "", message, args);
                }
                return null;
            });
            diag ??= CreateCompilerOptionsDiagnostic(message, args);
            return diag;
        }

        Diagnostic CreateDiagnosticForOptionPathKeyValue(string key, int valueIndex, Message message, params object?[] args)
        {
            var diag = ForEachOptionPathsSyntax(pathProperty =>
            {
                if (IsObjectLiteralExpression(pathProperty.Initializer!))
                {
                    return ForEachPropertyAssignment<Diagnostic>(pathProperty.Initializer!.AsObjectLiteralExpression(), key, keyProperty =>
                    {
                        var initializer = keyProperty.Initializer;
                        if (initializer is not null && IsArrayLiteralExpression(initializer))
                        {
                            var elements = initializer.ElementList();
                            if (elements?.Nodes is not null && elements.Nodes.Length > valueIndex)
                            {
                                var d = CreateDiagnosticForNodeInSourceFile(sourceFile()!, elements.Nodes[valueIndex], message, args);
                                _programDiagnostics = [.. _programDiagnostics, d];
                                return d;
                            }
                        }
                        return null;
                    });
                }
                return null;
            });
            diag ??= CreateCompilerOptionsDiagnostic(message, args);
            return diag;
        }

        var pathEntries = options.Paths?.Entries() ?? [];
        foreach (var (key, value) in pathEntries)
        {
            // !!! 这段代码不处理 path mappings 类型写错的情形，因为解析过程中大多已经丢失了该信息。
            if (!Compiler.HasZeroOrOneAsteriskCharacter(key))
            {
                CreateDiagnosticForOptionPaths(true /*onKey*/, key, Pattern_0_can_have_at_most_one_Asterisk_character, key);
            }
            if (value is null)
            {
                CreateDiagnosticForOptionPaths(false /*onKey*/, key, Substitutions_for_pattern_0_should_be_an_array, key);
            }
            else if (value.Length == 0)
            {
                CreateDiagnosticForOptionPaths(false /*onKey*/, key, Substitutions_for_pattern_0_shouldn_t_be_an_empty_array, key);
            }
            if (value is not null)
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var subst = value[i];
                    if (!Compiler.HasZeroOrOneAsteriskCharacter(subst))
                    {
                        CreateDiagnosticForOptionPathKeyValue(key, i, Substitution_0_in_pattern_1_can_have_at_most_one_Asterisk_character, subst, key);
                    }
                    if (!PathIsRelative(subst) && !PathIsAbsolute(subst))
                    {
                        CreateDiagnosticForOptionPathKeyValue(key, i, Non_relative_paths_are_not_allowed_Did_you_forget_a_leading_Slash);
                    }
                }
            }
        }

        if (options.SourceMap.IsFalseOrUnknown() && options.InlineSourceMap.IsFalseOrUnknown())
        {
            if (options.InlineSources.IsTrue())
            {
                CreateDiagnosticForOptionName(Option_0_can_only_be_used_when_either_option_inlineSourceMap_or_option_sourceMap_is_provided, "inlineSources", "");
            }
            if (options.SourceRoot != "")
            {
                CreateDiagnosticForOptionName(Option_0_can_only_be_used_when_either_option_inlineSourceMap_or_option_sourceMap_is_provided, "sourceRoot", "");
            }
        }

        if (options.MapRoot != "" && !(options.SourceMap.IsTrue() || options.DeclarationMap.IsTrue()))
        {
            // 没有 --sourcemap 却指定 --mapRoot 是错误
            CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "mapRoot", "sourceMap", "declarationMap");
        }

        if (options.DeclarationDir != "")
        {
            if (!options.GetEmitDeclarations())
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "declarationDir", "declaration", "composite");
            }
        }

        if (options.DeclarationMap.IsTrue() && !options.GetEmitDeclarations())
        {
            CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "declarationMap", "declaration", "composite");
        }

        if (options.Lib is not null && options.NoLib.IsTrue())
        {
            CreateDiagnosticForOptionName(Option_0_cannot_be_specified_with_option_1, "lib", "noLib");
        }

        if (options.IsolatedModules.IsTrue() || options.VerbatimModuleSyntax.IsTrue())
        {
            if (options.PreserveConstEnums.IsFalse())
            {
                CreateDiagnosticForOptionName(Option_preserveConstEnums_cannot_be_disabled_when_0_is_enabled, IfElse(options.VerbatimModuleSyntax.IsTrue(), "verbatimModuleSyntax", "isolatedModules"), "preserveConstEnums");
            }
        }

        if (options.OutDir != "" ||
            options.RootDir != "" ||
            options.SourceRoot != "" ||
            options.MapRoot != "" ||
            (options.GetEmitDeclarations() && options.DeclarationDir != ""))
        {
            // !!! sheetal checkSourceFilesBelongToPath —— 对 rootDir 和 configFile 说明文件为何在程序里
            var dir = CommonSourceDirectory();
            if (options.OutDir != "" && dir == "" && Some(_files, static f => GetRootLength(f.FileName()) > 1))
            {
                CreateDiagnosticForOptionName(Cannot_find_the_common_subdirectory_path_for_the_input_files, "outDir", "");
            }
        }

        if (!options.NoEmit.IsTrue() &&
            !options.Composite.IsTrue() &&
            options.RootDir == "" &&
            options.ConfigFilePath != "" &&
            (options.OutDir != "" ||
                (options.GetEmitDeclarations() && options.DeclarationDir != "") ||
                options.OutFile != ""))
        {
            // 检查推断出来的 rootDir 是否变了，是就报诊断
            var dir = CommonSourceDirectory();
            var emittedFiles = new List<string>();
            foreach (var file in _files)
            {
                if (!file.IsDeclarationFile && Compiler.SourceFileMayBeEmitted(file, this, false, false))
                {
                    emittedFiles.Add(file.FileName());
                }
            }
            var dir59 = TypeScript.OutputPaths.OutputPaths.GetComputedCommonSourceDirectory([.. emittedFiles], GetCurrentDirectory(), UseCaseSensitiveFileNames());
            if (dir59 != "" && GetCanonicalFileName(dir, UseCaseSensitiveFileNames()) != GetCanonicalFileName(dir59, UseCaseSensitiveFileNames()))
            {
                // 布局变了
                string option1;
                if (options.OutFile != "")
                {
                    option1 = "outFile";
                }
                else if (options.OutDir != "")
                {
                    option1 = "outDir";
                }
                else
                {
                    option1 = "declarationDir";
                }
                var option2 = options.OutFile == "" && options.OutDir != "" ? "declarationDir" : "";
                var diag = CreateDiagnosticForOption(
                    true, /*onKey*/
                    option1,
                    option2,
                    The_common_source_directory_of_0_is_1_The_rootDir_setting_must_be_explicitly_set_to_this_or_another_path_to_adjust_your_output_s_file_layout,
                    GetBaseFileName(options.ConfigFilePath),
                    GetRelativePathFromFile(options.ConfigFilePath, dir59, _comparePathsOptions));
                diag.AddMessageChain(NewCompilerDiagnostic(Visit_https_Colon_Slash_Slashaka_ms_Slashts6_for_migration_information));
            }
        }

        if (options.CheckJs.IsTrue() && !options.GetAllowJS())
        {
            CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1, "checkJs", "allowJs");
        }

        if (options.EmitDeclarationOnly.IsTrue())
        {
            if (!options.GetEmitDeclarations())
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "emitDeclarationOnly", "declaration", "composite");
            }
        }

        if (options.EmitDecoratorMetadata.IsTrue() && options.ExperimentalDecorators.IsFalseOrUnknown())
        {
            CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1, "emitDecoratorMetadata", "experimentalDecorators");
        }

        if (options.JsxFactory != "")
        {
            if (options.ReactNamespace != "")
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_with_option_1, "reactNamespace", "jsxFactory");
            }
            if (options.Jsx == JsxEmit.ReactJSX || options.Jsx == JsxEmit.ReactJSXDev)
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_when_option_jsx_is_1, "jsxFactory", options.Jsx.String());
            }
            if (Parser.ParseIsolatedEntityName(options.JsxFactory) is null)
            {
                CreateOptionValueDiagnostic("jsxFactory", Invalid_value_for_jsxFactory_0_is_not_a_valid_identifier_or_qualified_name, options.JsxFactory);
            }
        }
        else if (options.ReactNamespace != "" && !Scanner.IsIdentifierText(options.ReactNamespace, LanguageVariant.Standard))
        {
            CreateOptionValueDiagnostic("reactNamespace", Invalid_value_for_reactNamespace_0_is_not_a_valid_identifier, options.ReactNamespace);
        }

        if (options.JsxFragmentFactory != "")
        {
            if (options.JsxFactory == "")
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_without_specifying_option_1, "jsxFragmentFactory", "jsxFactory");
            }
            if (options.Jsx == JsxEmit.ReactJSX || options.Jsx == JsxEmit.ReactJSXDev)
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_when_option_jsx_is_1, "jsxFragmentFactory", options.Jsx.String());
            }
            if (Parser.ParseIsolatedEntityName(options.JsxFragmentFactory) is null)
            {
                CreateOptionValueDiagnostic("jsxFragmentFactory", Invalid_value_for_jsxFragmentFactory_0_is_not_a_valid_identifier_or_qualified_name, options.JsxFragmentFactory);
            }
        }

        if (options.ReactNamespace != "")
        {
            if (options.Jsx == JsxEmit.ReactJSX || options.Jsx == JsxEmit.ReactJSXDev)
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_when_option_jsx_is_1, "reactNamespace", options.Jsx.String());
            }
        }

        if (options.JsxImportSource != "")
        {
            if (options.Jsx == JsxEmit.React)
            {
                CreateDiagnosticForOptionName(Option_0_cannot_be_specified_when_option_jsx_is_1, "jsxImportSource", options.Jsx.String());
            }
        }

        var moduleKind = options.GetEmitModuleKind();

        if (options.AllowImportingTsExtensions.IsTrue() && !(options.NoEmit.IsTrue() || options.EmitDeclarationOnly.IsTrue() || options.RewriteRelativeImportExtensions.IsTrue()))
        {
            CreateOptionValueDiagnostic("allowImportingTsExtensions", Option_allowImportingTsExtensions_can_only_be_used_when_one_of_noEmit_emitDeclarationOnly_or_rewriteRelativeImportExtensions_is_set);
        }

        var moduleResolution = options.GetModuleResolutionKind();
        if (options.ResolvePackageJsonExports.IsTrue() && !Compiler.ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            CreateDiagnosticForOptionName(Option_0_can_only_be_used_when_moduleResolution_is_set_to_node16_nodenext_or_bundler, "resolvePackageJsonExports", "");
        }
        if (options.ResolvePackageJsonImports.IsTrue() && !Compiler.ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            CreateDiagnosticForOptionName(Option_0_can_only_be_used_when_moduleResolution_is_set_to_node16_nodenext_or_bundler, "resolvePackageJsonImports", "");
        }
        if (options.CustomConditions is not null && !Compiler.ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            CreateDiagnosticForOptionName(Option_0_can_only_be_used_when_moduleResolution_is_set_to_node16_nodenext_or_bundler, "customConditions", "");
        }

        if (moduleResolution == ModuleResolutionKind.Bundler && !Compiler.EmitModuleKindIsNonNodeESM(moduleKind) && moduleKind != ModuleKind.Preserve && moduleKind != ModuleKind.CommonJS)
        {
            CreateOptionValueDiagnostic("moduleResolution", Option_0_can_only_be_used_when_module_is_set_to_preserve_commonjs_or_es2015_or_later, "bundler");
        }

        if (ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext &&
            !(ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext))
        {
            var moduleKindName = moduleKind.String();
            var moduleResolutionName = ModuleKindToModuleResolutionKind.TryGetValue(moduleKind, out var v) ? v.String() : "Node16";
            CreateOptionValueDiagnostic("moduleResolution", Option_moduleResolution_must_be_set_to_0_or_left_unspecified_when_option_module_is_set_to_1, moduleResolutionName, moduleKindName);
        }
        else if (ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext &&
            !(ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext))
        {
            var moduleResolutionName = moduleResolution.String();
            CreateOptionValueDiagnostic("module", Option_module_must_be_set_to_0_when_option_moduleResolution_is_set_to_1, moduleResolutionName, moduleResolutionName);
        }

        // !!! 下面需要 filesByName，它与 p.filesByPath 不等价。

        // 如果开了 emit，要确保每个输出文件唯一、且不覆盖任何输入文件
        if (!options.NoEmit.IsTrue() && !options.SuppressOutputPathCheck.IsTrue())
        {
            var emitFilesSeen = new Set<string>();

            // 校验所有 emit 文件唯一、且不覆盖输入文件
            void VerifyEmitFilePath(string emitFileName)
            {
                if (emitFileName != "")
                {
                    var emitFilePath = ToPath(emitFileName);
                    // 输出覆盖输入文件就报错
                    if (_filesByPath.ContainsKey(emitFilePath))
                    {
                        var diag = NewCompilerDiagnostic(Cannot_write_file_0_because_it_would_overwrite_input_file, emitFileName);
                        if (configFilePath() == "")
                        {
                            // 该程序来自推断项目或外部项目
                            diag.AddMessageChain(NewCompilerDiagnostic(Adding_a_tsconfig_json_file_will_help_organize_projects_that_contain_both_TypeScript_and_JavaScript_files_Learn_more_at_https_Colon_Slash_Slashaka_ms_Slashtsconfig));
                        }
                        BlockEmittingOfFile(emitFileName, diag);
                    }

                    var emitFileKey = Host().FS().UseCaseSensitiveFileNames() ? emitFilePath.Value : ToFileNameLowerCase(emitFilePath.Value);

                    // 多个文件写同一个输出文件就报错
                    if (emitFilesSeen.Has(emitFileKey))
                    {
                        // 已经见过同一个 emit 文件 → 报错
                        BlockEmittingOfFile(emitFileName, NewCompilerDiagnostic(Cannot_write_file_0_because_it_would_be_overwritten_by_multiple_input_files, emitFileName));
                    }
                    else
                    {
                        emitFilesSeen.Add(emitFileKey);
                    }
                }
            }

            TypeScript.OutputPaths.OutputPaths.ForEachEmittedFile(this, options, (emitFileNames, _) =>
            {
                VerifyEmitFilePath(emitFileNames.JsFilePath());
                VerifyEmitFilePath(emitFileNames.SourceMapFilePath());
                VerifyEmitFilePath(emitFileNames.DeclarationFilePath());
                VerifyEmitFilePath(emitFileNames.DeclarationMapPath());
                return false;
            }, GetSourceFilesToEmit(null, false, false), false);
            VerifyEmitFilePath(_opts.Config.GetBuildInfoFileName());
        }
    }

    /// <summary>Go: <c>func (p *Program) blockEmittingOfFile(emitFileName string, diag *ast.Diagnostic)</c>（program.go:1385）。</summary>
    private void BlockEmittingOfFile(string emitFileName, Diagnostic diag)
    {
        _hasEmitBlockingDiagnostics.Add(ToPath(emitFileName));
        _programDiagnostics = [.. _programDiagnostics, diag];
    }

    /// <summary>Go: <c>func (p *Program) IsEmitBlocked(emitFileName string) bool</c>（program.go:1390）。</summary>
    public bool IsEmitBlocked(string emitFileName) => _hasEmitBlockingDiagnostics.Has(ToPath(emitFileName));

    /// <summary>Go: <c>func (p *Program) verifyProjectReferences()</c>（program.go:1394）。</summary>
    private void VerifyProjectReferences()
    {
        var buildInfoFileName = IfElse(!Options().SuppressOutputPathCheck.IsTrue(), _opts.Config.GetBuildInfoFileName(), "");
        Diagnostic CreateDiagnosticForReference(ParsedCommandLine config, int index, Message message, params object?[] args)
        {
            var diag = CreateDiagnosticAtReferenceSyntax(config, index, message, args);
            diag ??= NewCompilerDiagnostic(message, args);
            _programDiagnostics = [.. _programDiagnostics, diag];
            return diag;
        }

        RangeResolvedProjectReference((path, config, parent, index) =>
        {
            var reference = parent!.ProjectReferences()![index];
            // !!! 5.0 起弃用，5.5 起移除
            // verifyRemovedProjectReference(ref, parent, index);
            if (config is null)
            {
                CreateDiagnosticForReference(parent!, index, File_0_not_found, reference.Path);
                return true;
            }
            var refOptions = config.CompilerOptions()!;
            if (!refOptions.Composite.IsTrue() || refOptions.NoEmit.IsTrue())
            {
                if (parent.FileNames().Length > 0)
                {
                    if (!refOptions.Composite.IsTrue())
                    {
                        CreateDiagnosticForReference(parent, index, Referenced_project_0_must_have_setting_composite_Colon_true, reference.Path);
                    }
                    if (refOptions.NoEmit.IsTrue())
                    {
                        CreateDiagnosticForReference(parent, index, Referenced_project_0_may_not_disable_emit, reference.Path);
                    }
                }
            }
            if (buildInfoFileName != "" && buildInfoFileName == config.GetBuildInfoFileName())
            {
                CreateDiagnosticForReference(parent, index, Cannot_write_file_0_because_it_will_overwrite_tsbuildinfo_file_generated_by_referenced_project_1, buildInfoFileName, reference.Path);
                _hasEmitBlockingDiagnostics.Add(ToPath(buildInfoFileName));
            }
            return true;
        });
    }
}

/// <summary>program.go 里三个包级 helper（`Compiler` 静态类的 partial，见 Pkg.cs）。</summary>
public static partial class Compiler
{
    /// <summary>Go: <c>func hasZeroOrOneAsteriskCharacter(str string) bool</c>（program.go:1431）。</summary>
    internal static bool HasZeroOrOneAsteriskCharacter(string str)
    {
        var seenAsterisk = false;
        foreach (var ch in str)
        {
            if (ch == '*')
            {
                if (!seenAsterisk)
                {
                    seenAsterisk = true;
                }
                else
                {
                    // 已经见过一个星号了
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>Go: <c>func moduleResolutionSupportsPackageJsonExportsAndImports(moduleResolution) bool</c>（program.go:1446）。</summary>
    internal static bool ModuleResolutionSupportsPackageJsonExportsAndImports(ModuleResolutionKind moduleResolution) =>
        moduleResolution >= ModuleResolutionKind.Node16 && moduleResolution <= ModuleResolutionKind.NodeNext ||
        moduleResolution == ModuleResolutionKind.Bundler;

    /// <summary>Go: <c>func emitModuleKindIsNonNodeESM(moduleKind core.ModuleKind) bool</c>（program.go:1451）。</summary>
    internal static bool EmitModuleKindIsNonNodeESM(ModuleKind moduleKind) =>
        moduleKind >= ModuleKind.ES2015 && moduleKind <= ModuleKind.ESNext;
}

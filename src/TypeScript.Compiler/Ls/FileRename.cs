// ported from: internal/ls/file_rename.go:1-403
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级类型 `pathUpdater`（Go 的 `func(string) (string, bool)`）→ 本文件的 `internal delegate bool PathUpdater(string path, out string updated)`
//    （§3.2 的 `(T, bool)` → `TryX(out T)` 约定；lambda 可以带 `out` 形参）。
//    `toImport` / `movedFile` → `internal sealed class ToImport` / `MovedFile`（Go 未导出字段 → `_camelCase`）。
//
// 2) 包级函数 → 静态类 `Ls` 的 `internal static` 成员（PascalCase、不加 `_`）：`UpdatePathsProperty` /
//    `TryUpdateConfigString` / `CreateStringTextRange` / `GetTsConfigObjectLiteralExpression` /
//    `ForEachObjectProperty` / `RelativePathFromDirectory` / `RelativeImportPathFromDirectory` /
//    `GetSourceFileToImport` / `GetUpdatedImportSpecifierFromMovedSourceFiles`（后两者要 `MovedFile`/`ToImport`
//    ⇒ 留 `internal`）。本文件**不** `using static TypeScript.Ls.Ls;` ⇒ 调用点显式写 `Ls.Xxx(...)`。
//
// 3) `change.NewTracker(...)` → `Tracker.NewTracker(...)`；`changeTracker.SetEmitFlags` 之类的提升方法 →
//    `changeTracker.EmitContext.Xxx(...)`（先例 `Ls/OrganizeImports.cs`）。
//
// 4) `tspath.ComparePathsOptions{UseCaseSensitiveFileNames: x}` → `new ComparePathsOptions(useCaseSensitiveFileNames: x)`
//    （C# 是带默认值的 readonly struct，见 `TSPath/Path.cs:1620-1630`）。
//    `tspath.TrimFilePathPrefix(...) (string, bool)` → `TryTrimFilePathPrefix(..., out string suffix)`（既有落地）。
//
// 5) `program.CommandLine()` 在 C# 返回**非空** `ParsedCommandLine`（Go 的 `*tsoptions.ParsedCommandLine` 可为 nil，
//    但那只是"Program 由空配置构造"的病态情形）⇒ 保留 `ConfigFile` 的判空（C# 侧 `ConfigFile` 是可空的
//    `TsConfigSourceFile?`），并保留 `ConfigFile.SourceFile` 的判空（Go 显式判过）。
//
// 6) `commandLine.GetMatchedIncludeSpec(x) (string, bool)` → `TryGetMatchedIncludeSpec(x, out string)`，**返回值是
//    Go 的 `isDefaultIncludeSpec`**（`TsOptions/ParsedCommandLine.cs:13,502`）。
//
// 7) `option.Kind == tsoptions.CommandLineOptionTypeList` → `option.Kind == CommandLineOptionKind.List`（C# 的
//    `CommandLineOptionKind` 枚举成员去掉了 Go 常量的 `CommandLineOptionType` 前缀）。
//
// 8) `debug.Assert(fidelity.IsExact(), "…")` → `TypeScript.Debug.Debug.Assert(fidelity.IsExact(), "…")`
//    （**禁** `using System.Diagnostics;`；`StatusBar` 之外全仓都用 `TypeScript.Debug.Debug`）。
//
// 9) `context.Background()`（`updateImportsForFileRename` 里显式构造）→ `Context.Background`（`Core/Context.cs:44`）。
//
// 10) `modulespecifiers.UpdateModuleSpecifier(...)` → 裸名（`using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;`）；
//    `program` 直接当 `ModuleSpecifierGenerationHost` 传（`Compiler.Program : TypeScript.Checker.Program : Checker.Host`，
//    而 `Checker.Host : ModuleSpecifierGenerationHost`）。
//
// 11) `importStringLiteral`/`ref` 的 Go 类型 `*ast.StringLiteralLike` 是 `= Node` 的**类型别名** ⇒ C# 用 `Node`。
//    `ref.TextRange` → C# `FileReference.Range`（`Ast/AstTypes.cs:106-112`）。
//
// 12) 深递归：本文件不递归（只有列表遍历）⇒ 无需大栈线程。
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.ModuleSpecifiers;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Debug;
using static TypeScript.LsConv.LsConv;
using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Tracker = TypeScript.LsChange.Tracker;

namespace TypeScript.Ls;

/// <summary>Go: <c>type pathUpdater func(path string) (string, bool)</c>（file_rename.go:20）。</summary>
internal delegate bool PathUpdater(string path, out string updated);

/// <summary>Go: <c>type toImport struct</c>（file_rename.go:22-25）。</summary>
internal sealed class ToImport
{
    internal string _newFileName = "";
    internal bool _updated;
}

/// <summary>Go: <c>type movedFile struct</c>（file_rename.go:27-30）。</summary>
internal sealed class MovedFile
{
    internal SourceFile _sourceFile = null!;
    internal string _newFileName = "";
}

public static partial class Ls
{
    /// <summary>Go: <c>func updatePathsProperty(...)</c>（file_rename.go:203-215）。</summary>
    internal static bool UpdatePathsProperty(
        SourceFile configFile,
        string configDir,
        PropertyAssignment property,
        Tracker changeTracker,
        PathUpdater oldToNew,
        LsConv.Converters converters,
        bool useCaseSensitiveFileNames)
    {
        var elements = new List<Node> { property.Initializer! };
        if (property.Initializer is { } initializer && IsArrayLiteralExpression(initializer))
        {
            elements = [.. initializer.AsArrayLiteralExpression()!.Elements!.Nodes ?? []];
        }

        var foundExactMatch = false;
        foreach (var element in elements)
        {
            foundExactMatch = TryUpdateConfigString(configFile, configDir, element, changeTracker, oldToNew, converters, useCaseSensitiveFileNames) || foundExactMatch;
        }

        return foundExactMatch;
    }

    /// <summary>Go: <c>func tryUpdateConfigString(...)</c>（file_rename.go:217-235）。</summary>
    internal static bool TryUpdateConfigString(
        SourceFile configFile,
        string configDir,
        Node element,
        Tracker changeTracker,
        PathUpdater oldToNew,
        LsConv.Converters converters,
        bool useCaseSensitiveFileNames)
    {
        if (!IsStringLiteral(element))
        {
            return false;
        }

        var elementFileName = NormalizePath(CombinePaths(configDir, element.Text()));
        if (!oldToNew(elementFileName, out var updated))
        {
            return false;
        }

        var textRange = new TextRange(GetTokenPosOfNode(element, configFile, false) + 1, element.End() - 1);
        var (lspRange, fidelity) = converters.ToLSPRange(configFile, textRange);
        Assert(fidelity.IsExact(), "config files are not content-mapped");
        changeTracker.ReplaceRangeWithText(configFile, lspRange, RelativePathFromDirectory(configDir, updated, useCaseSensitiveFileNames));
        return true;
    }

    /// <summary>Go: <c>func createStringTextRange(sourceFile *ast.SourceFile, node *ast.LiteralLikeNode) core.TextRange</c>（file_rename.go:373-375）。</summary>
    internal static TextRange CreateStringTextRange(SourceFile sourceFile, Node node)
    {
        return new TextRange(GetTokenPosOfNode(node, sourceFile, false) + 1, node.End() - 1);
    }

    /// <summary>Go: <c>func getTsConfigObjectLiteralExpression(tsConfigSourceFile *ast.SourceFile) *ast.ObjectLiteralExpression</c>（file_rename.go:377-386）。</summary>
    internal static ObjectLiteralExpression? GetTsConfigObjectLiteralExpression(SourceFile? tsConfigSourceFile)
    {
        if (tsConfigSourceFile is not null && tsConfigSourceFile.Statements is not null && (tsConfigSourceFile.Statements.Nodes ?? []).Length > 0)
        {
            var expression = tsConfigSourceFile.Statements.Nodes![0].Expression();
            if (expression is { } objectExpression && IsObjectLiteralExpression(objectExpression))
            {
                return objectExpression.AsObjectLiteralExpression()!;
            }
        }

        return null;
    }

    /// <summary>Go: <c>func forEachObjectProperty(objectLiteral *ast.ObjectLiteralExpression, cb func(property *ast.PropertyAssignment, propertyName string))</c>（file_rename.go:388-399）。</summary>
    internal static void ForEachObjectProperty(ObjectLiteralExpression? objectLiteral, Action<PropertyAssignment, string> cb)
    {
        if (objectLiteral is null)
        {
            return;
        }

        foreach (var property in objectLiteral.Properties!.Nodes ?? [])
        {
            if (!IsPropertyAssignment(property))
            {
                continue;
            }

            if (TryGetTextOfPropertyName(property.Name(), out var name))
            {
                cb(property.AsPropertyAssignment()!, name);
            }
        }
    }

    /// <summary>Go: <c>func relativePathFromDirectory(fromDirectory string, to string, useCaseSensitiveFileNames bool) string</c>（file_rename.go:401-403）。</summary>
    internal static string RelativePathFromDirectory(string fromDirectory, string to, bool useCaseSensitiveFileNames)
    {
        return GetRelativePathFromDirectory(fromDirectory, to, new ComparePathsOptions(useCaseSensitiveFileNames: useCaseSensitiveFileNames));
    }

    /// <summary>Go: <c>func relativeImportPathFromDirectory(fromDirectory string, to string, useCaseSensitiveFileNames bool) string</c>（file_rename.go:405-407）。</summary>
    internal static string RelativeImportPathFromDirectory(string fromDirectory, string to, bool useCaseSensitiveFileNames)
    {
        return EnsurePathIsNonModuleName(RelativePathFromDirectory(fromDirectory, to, useCaseSensitiveFileNames));
    }

    /// <summary>Go: <c>func getSourceFileToImport(program *compiler.Program, sourceFile *ast.SourceFile, importLiteral *ast.StringLiteralLike, oldToNew pathUpdater) *toImport</c>（file_rename.go:325-338）。</summary>
    internal static ToImport? GetSourceFileToImport(
        Program program,
        SourceFile sourceFile,
        Node importLiteral,
        PathUpdater oldToNew)
    {
        var resolved = program.GetResolvedModuleFromModuleSpecifier(sourceFile, importLiteral);
        if (resolved is not null && resolved.ResolvedFileName != "")
        {
            var oldFileName = resolved.ResolvedFileName;
            if (oldToNew(oldFileName, out var newFileName))
            {
                return new ToImport { _newFileName = newFileName, _updated = true };
            }

            return new ToImport { _newFileName = oldFileName, _updated = false };
        }

        return null;
    }

    /// <summary>
    /// Go: <c>func getUpdatedImportSpecifierFromMovedSourceFiles(program *compiler.Program, sourceFile *ast.SourceFile, importLiteral *ast.StringLiteralLike, movedFiles []movedFile, importingSourceFileName string, userPreferences modulespecifiers.UserPreferences) string</c>
    /// （file_rename.go:340-371）。As a fall back for unresolved modules, we'll check every file affected by the rename to see if any of them would match
    /// the import specifier, and if so, we'll obtain the updated specifier for that file.
    /// </summary>
    internal static string GetUpdatedImportSpecifierFromMovedSourceFiles(
        Program program,
        SourceFile sourceFile,
        Node importLiteral,
        List<MovedFile> movedFiles,
        string importingSourceFileName,
        TypeScript.ModuleSpecifiers.UserPreferences userPreferences)
    {
        var resolutionMode = program.GetModeForUsageLocation(sourceFile, importLiteral);
        foreach (var candidate in movedFiles)
        {
            var oldSpecifier = UpdateModuleSpecifier(
                program.Options(),
                program,
                sourceFile,
                importingSourceFileName,
                importLiteral.Text(),
                candidate._sourceFile.FileName(),
                userPreferences,
                new ModuleSpecifierOptions
                {
                    OverrideImportMode = resolutionMode,
                });
            if (oldSpecifier != importLiteral.Text())
            {
                continue;
            }

            return UpdateModuleSpecifier(
                program.Options(),
                program,
                sourceFile,
                importingSourceFileName,
                importLiteral.Text(),
                candidate._newFileName,
                userPreferences,
                new ModuleSpecifierOptions
                {
                    OverrideImportMode = resolutionMode,
                });
        }

        return "";
    }

    /// <summary>Go: <c>func isAmbientModuleSymbol(symbol *ast.Symbol) bool</c>（file_rename.go:409-414）。</summary>
    internal static bool IsAmbientModuleSymbol(Symbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        return (symbol.Declarations ?? []).Any(IsModuleWithStringLiteralName);
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) GetEditsForFileRename(ctx context.Context, oldURI lsproto.DocumentUri, newURI lsproto.DocumentUri) []lsproto.TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile</c>（file_rename.go:32-96）。</summary>
    public TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile[] GetEditsForFileRename(Context ctx, DocumentUri oldURI, DocumentUri newURI)
    {
        var program = GetProgram()!;
        var oldPath = oldURI.FileName();
        var newPath = newURI.FileName();

        var oldToNew = CreatePathUpdater(oldPath, newPath);

        var changeTracker = Tracker.NewTracker(ctx, program.Options(), FormatOptions(), _converters!);
        UpdateTsconfigFiles(program, changeTracker, oldToNew, oldPath, newPath);
        UpdateImportsForFileRename(program, changeTracker, oldToNew);

        var documentChanges = new List<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>();

        // When renaming e.g. `foo.d.css.ts` -> `bar.d.css.ts`, also rename `foo.css` -> `bar.css` if it exists.
        if (IsDeclarationFileName(oldPath) && IsDeclarationFileName(newPath))
        {
            var dtsExt = GetDeclarationFileExtension(oldPath);
            var originalExtensions = GetPossibleOriginalInputExtensionForExtension(dtsExt);
            foreach (var ext in originalExtensions)
            {
                var oldOriginalPath = ChangeFullExtension(oldPath, ext);
                if (_host.FileExists(oldOriginalPath))
                {
                    var newDtsExt = GetDeclarationFileExtension(oldPath);
                    var newOriginalExtensions = GetPossibleOriginalInputExtensionForExtension(newDtsExt);
                    if (newOriginalExtensions.Contains(ext))
                    {
                        var newOriginalPath = ChangeFullExtension(newPath, ext);
                        documentChanges.Add(new TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile
                        {
                            RenameFile = new RenameFile
                            {
                                OldUri = FileNameToDocumentURI(oldOriginalPath),
                                NewUri = FileNameToDocumentURI(newOriginalPath),
                            },
                        });
                    }
                }
            }
        }

        var (changes, _) = changeTracker.GetChanges();
        foreach (var (fileName, edits) in changes)
        {
            var uri = FileNameToDocumentURI(fileName);
            var lspEdits = new TextEditOrAnnotatedTextEditOrSnippetTextEdit[edits.Length];
            for (var i = 0; i < edits.Length; i++)
            {
                lspEdits[i] = new TextEditOrAnnotatedTextEditOrSnippetTextEdit
                {
                    TextEdit = edits[i],
                };
            }

            documentChanges.Add(new TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile
            {
                TextDocumentEdit = new TextDocumentEdit
                {
                    TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = uri },
                    Edits = lspEdits,
                },
            });
        }

        return documentChanges.ToArray();
    }

    /// <summary>Go: <c>func (l *LanguageService) createPathUpdater(oldPath string, newPath string) pathUpdater</c>（file_rename.go:98-120）。</summary>
    private PathUpdater CreatePathUpdater(string oldPath, string newPath)
    {
        var compareOptions = new ComparePathsOptions(useCaseSensitiveFileNames: UseCaseSensitiveFileNames());
        var trimmedOldPath = RemoveTrailingDirectorySeparator(oldPath);
        return (string path, out string updated) =>
        {
            if (ComparePaths(path, oldPath, compareOptions) == 0)
            {
                updated = newPath;
                return true;
            }

            // Trim the directory prefix ourselves (rather than using
            // tspath.StartsWithDirectory followed by a separate slice on
            // len(oldPath)) so the containment check and the suffix we return can
            // never disagree, and so we don't slice path by a byte count derived
            // from a canonicalized/differently-cased string: case-folding can
            // change a path's UTF-8 byte length without changing its rune count
            // (e.g. the Kelvin sign '\u212A' folds to the single-byte 'k'), which
            // could otherwise put len(oldPath) out of range of path.
            if (TryTrimFilePathPrefix(path, trimmedOldPath, UseCaseSensitiveFileNames(), out var suffix) &&
                (suffix.StartsWith('/') || suffix.StartsWith('\\')))
            {
                updated = newPath + suffix;
                return true;
            }

            updated = "";
            return false;
        };
    }

    /// <summary>Go: <c>func (l *LanguageService) updateTsconfigFiles(program *compiler.Program, changeTracker *change.Tracker, oldToNew pathUpdater, oldPath string, newPath string)</c>（file_rename.go:122-201）。</summary>
    private void UpdateTsconfigFiles(Program program, Tracker changeTracker, PathUpdater oldToNew, string oldPath, string newPath)
    {
        var commandLine = program.CommandLine();
        if (commandLine.ConfigFile is null)
        {
            return;
        }

        var configFile = commandLine.ConfigFile.SourceFile;
        if (configFile is null)
        {
            return;
        }

        var configDir = GetDirectoryPath(configFile.FileName());
        var jsonObjectLiteral = Ls.GetTsConfigObjectLiteralExpression(configFile);
        if (jsonObjectLiteral is null)
        {
            return;
        }

        Ls.ForEachObjectProperty(jsonObjectLiteral, (property, propertyName) =>
        {
            switch (propertyName)
            {
                case "files":
                case "include":
                case "exclude":
                {
                    var foundExactMatch = Ls.UpdatePathsProperty(configFile, configDir, property, changeTracker, oldToNew, _converters!, UseCaseSensitiveFileNames());
                    if (foundExactMatch || propertyName != "include" || property.Initializer is not { } includeInitializer || !IsArrayLiteralExpression(includeInitializer))
                    {
                        return;
                    }

                    var isDefault = commandLine.TryGetMatchedIncludeSpec(oldPath, out var oldSpec);
                    if (oldSpec != "" && !isDefault)
                    {
                        commandLine.TryGetMatchedIncludeSpec(newPath, out var newSpec);
                        if (newSpec == "")
                        {
                            var elements = includeInitializer.AsArrayLiteralExpression()!.Elements!.Nodes ?? [];
                            if (elements.Length > 0)
                            {
                                changeTracker.InsertNodeAfter(
                                    configFile,
                                    elements[^1],
                                    changeTracker.NodeFactory.NewStringLiteral(Ls.RelativePathFromDirectory(configDir, newPath, UseCaseSensitiveFileNames()), TokenFlags.None));
                            }
                        }
                    }

                    break;
                }

                case "compilerOptions":
                {
                    if (property.Initializer is not { } compilerOptionsInitializer || !IsObjectLiteralExpression(compilerOptionsInitializer))
                    {
                        return;
                    }

                    Ls.ForEachObjectProperty(compilerOptionsInitializer.AsObjectLiteralExpression()!, (property2, propertyName2) =>
                    {
                        var option = TypeScript.TsOptions.TsOptions.CommandLineCompilerOptionsMap.Get(propertyName2);
                        if (option is not null)
                        {
                            var elementOption = option.Elements();
                            if (option.IsFilePath || (option.Kind == CommandLineOptionKind.List && elementOption is not null && elementOption.IsFilePath))
                            {
                                Ls.UpdatePathsProperty(configFile, configDir, property2, changeTracker, oldToNew, _converters!, UseCaseSensitiveFileNames());
                                return;
                            }
                        }

                        if (propertyName2 != "paths" || property2.Initializer is not { } pathsInitializer || !IsObjectLiteralExpression(pathsInitializer))
                        {
                            return;
                        }

                        Ls.ForEachObjectProperty(pathsInitializer.AsObjectLiteralExpression()!, (pathsProperty, _) =>
                        {
                            if (pathsProperty.Initializer is not { } arrayInitializer || !IsArrayLiteralExpression(arrayInitializer))
                            {
                                return;
                            }

                            foreach (var element in arrayInitializer.AsArrayLiteralExpression()!.Elements!.Nodes ?? [])
                            {
                                Ls.TryUpdateConfigString(configFile, configDir, element, changeTracker, oldToNew, _converters!, UseCaseSensitiveFileNames());
                            }
                        });
                    });
                    break;
                }
            }
        });
    }

    /// <summary>Go: <c>func (l *LanguageService) updateRelativePath(oldToNew pathUpdater, oldImportFromPath, newImportFromPath, relativeSpecifier string) string</c>（file_rename.go:237-244）。</summary>
    private string UpdateRelativePath(PathUpdater oldToNew, string oldImportFromPath, string newImportFromPath, string relativeSpecifier)
    {
        var oldAbsolute = NormalizePath(CombinePaths(GetDirectoryPath(oldImportFromPath), relativeSpecifier));
        if (!oldToNew(oldAbsolute, out var newAbsolute))
        {
            newAbsolute = oldAbsolute;
        }

        return Ls.RelativeImportPathFromDirectory(GetDirectoryPath(newImportFromPath), newAbsolute, UseCaseSensitiveFileNames());
    }

    /// <summary>Go: <c>func (l *LanguageService) updateImportsForFileRename(program *compiler.Program, changeTracker *change.Tracker, oldToNew pathUpdater)</c>（file_rename.go:246-276）。</summary>
    private void UpdateImportsForFileRename(Program program, Tracker changeTracker, PathUpdater oldToNew)
    {
        var allFiles = program.SourceFiles();
        var (checker, done) = program.GetTypeChecker(Context.Background);
        try
        {
            var moduleSpecifierPreferences = UserPreferences().ModuleSpecifierPreferences();

            var movedFiles = new List<MovedFile>();
            foreach (var sourceFile in allFiles)
            {
                if (oldToNew(sourceFile.OriginalFileName(), out var newFileName))
                {
                    movedFiles.Add(new MovedFile { _sourceFile = sourceFile, _newFileName = newFileName });
                }
            }

            foreach (var sourceFile in allFiles)
            {
                var oldFileName = sourceFile.OriginalFileName();
                var fileMoved = oldToNew(oldFileName, out var newFromOld);
                var newImportFromPath = oldFileName;
                if (fileMoved)
                {
                    newImportFromPath = newFromOld;
                }

                foreach (var @ref in sourceFile.ReferencedFiles ?? [])
                {
                    if (!IsExternalModuleNameRelative(@ref.FileName))
                    {
                        continue;
                    }

                    var updated = UpdateRelativePath(oldToNew, oldFileName, newImportFromPath, @ref.FileName);
                    if (updated != @ref.FileName)
                    {
                        changeTracker.ReplaceTextRangeWithText(sourceFile, @ref.Range, updated);
                    }
                }

                foreach (var importStringLiteral in sourceFile.Imports())
                {
                    var updated = GetUpdatedImportSpecifier(program, checker, sourceFile, importStringLiteral, oldToNew, movedFiles, newImportFromPath, fileMoved, moduleSpecifierPreferences);
                    if (updated != "" && updated != importStringLiteral.Text())
                    {
                        changeTracker.ReplaceTextRangeWithText(sourceFile, Ls.CreateStringTextRange(sourceFile, importStringLiteral), updated);
                    }
                }
            }
        }
        finally
        {
            done();
        }
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) getUpdatedImportSpecifier(program *compiler.Program, checker *checker.Checker, sourceFile *ast.SourceFile, importLiteral *ast.StringLiteralLike, oldToNew pathUpdater, movedFiles []movedFile, newImportFromPath string, importingSourceFileMoved bool, userPreferences modulespecifiers.UserPreferences) string</c>
    /// （file_rename.go:279-323）。We assume the source file did not move to a different program.
    /// </summary>
    private string GetUpdatedImportSpecifier(
        Program program,
        TypeScript.Checker.Checker checker,
        SourceFile sourceFile, // old importing source file
        Node importLiteral,
        PathUpdater oldToNew,
        List<MovedFile> movedFiles,
        string newImportFromPath,
        bool importingSourceFileMoved,
        TypeScript.ModuleSpecifiers.UserPreferences userPreferences)
    {
        var importedModuleSymbol = checker.GetSymbolAtLocation(importLiteral);
        if (Ls.IsAmbientModuleSymbol(importedModuleSymbol))
        {
            return "";
        }

        var target = Ls.GetSourceFileToImport(program, sourceFile, importLiteral, oldToNew);

        if (target is null)
        {
            // First fall back: try every file affected by the rename to see if any of them would match the import specifier, and if so, obtain the updated specifier for that file.
            var updated = Ls.GetUpdatedImportSpecifierFromMovedSourceFiles(program, sourceFile, importLiteral, movedFiles, newImportFromPath, userPreferences);
            if (updated != "" && updated != importLiteral.Text())
            {
                return updated;
            }

            // Fall back to a regular path update for unresolved module.
            if (IsExternalModuleNameRelative(importLiteral.Text()))
            {
                return UpdateRelativePath(oldToNew, sourceFile.FileName(), newImportFromPath, importLiteral.Text());
            }

            return "";
        }

        // Optimization: neither the importing or imported file changed.
        if (!target._updated && !(importingSourceFileMoved && IsExternalModuleNameRelative(importLiteral.Text())))
        {
            return "";
        }

        return UpdateModuleSpecifier(
            program.Options(),
            program,
            sourceFile,
            newImportFromPath,
            importLiteral.Text(),
            target._newFileName,
            userPreferences,
            new ModuleSpecifierOptions
            {
                OverrideImportMode = program.GetModeForUsageLocation(sourceFile, importLiteral),
            });
    }
}

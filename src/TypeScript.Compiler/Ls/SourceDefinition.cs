// ported from: internal/ls/sourcedefinition.go:1-735
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **Go 的 `sourceDefResolver` 的 error 面**：`provideSourceDefinitionAtPosition` 在 Go 里声明了 `error` 返回值，
//    但方法体里每一处 `return …` 的第二值都是 `nil`（唯一的错误来自内层 `provideSourceDefinitionAtPosition`，
//    而它自己也不产生错误）⇒ C# 去掉这层恒 nil 的 error，`ProvideSourceDefinition` 直接返回 `(Response, null)`。
//    对外 API 的形状（`(Response, Exception? Error)`）与其它 `Provide*` 保持一致。
// 2) Go 的未导出 struct `sourceDefResolver` → `internal sealed class SourceDefResolver`（字段一律 `_camelCase`，
//    Go 未导出字段；`ls`/`fs`/`options`/`getSourceFile`/`resolveFrom`/`resolver`/`parsedFiles` 逐一对应）。
//    Go 的结构字面量 + 单构造点 ⇒ C# 显式构造函数（`LanguageService.NewSourceDefResolver` 里 new）。
// 3) **`ast.FileReference` 在 C# 里不是 Node**（Go 里它内嵌 `core.TextRange`，`ref.Pos()/ref.End()` 来自内嵌字段）
//    ⇒ `ref.Pos()` 写 `reference.Range.Pos()`（见 Ast/AstTypes.cs:106）。
// 4) Go 的 `[]*ast.Node` 可 nil ⇒ C# `Node[]?`；**凡是 Go 里判 `!= nil` / `len(...) == 0` 的地方都保持可空**：
//    `FindDeclarationsInFile` / `SearchImplementationFile` / `MapDeclarationToSource` / `ResolveFromCheckerInfo`
//    返回 `Node[]?`；其它（`GetSourceDefinitionEntryDeclarations` / `UniqueDeclarationNodes` /
//    `GetCandidateSourceDeclarationNames`）Go 里恒非 nil ⇒ C# 也非 nil。
// 5) `r.ls.ReadFile(fileName)` → `_ls.TryReadFile(fileName, out var text)`（见 Ls/Host.cs 文件头第 1 条）。
// 6) `strings.LastIndex(s, "/node_modules/")`（字节偏移，输入是 ASCII 路径）→ `string.LastIndexOf(..., StringComparison.Ordinal)`；
//    Go 的切片 `s[a:b]` → C# 的范围索引 `s[a..b]`。
// 7) `core.ResolutionMode` 在 C# 就是 `ModuleKind`（Ast/AstTypes.cs:110 的注释同款）⇒ `core.ModuleKindESNext` →
//    `ModuleKind.ESNext`、`core.ModuleKindCommonJS` → `ModuleKind.CommonJS`。
// 8) Go 的 `collections.Set[string]{}` 零值 → `new Set<string>()`；`&collections.Set[string]{}` 的指针语义不需要。
// 9) **同名命名空间/静态类遮蔽**（G1 契约 §4.1）：`module.TryGetJSExtensionForFile` / `module.NewResolver` /
//    `module.GetPackageNameFromTypesPackageName` / `module.UnmangleScopedPackageName` 用 `using static TypeScript.Module.Module;`
//    化解；`modulespecifiers.GetNodeModulePathParts` 用 `using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;`；
//    `parser.ParseSourceFile` / `binder.BindSourceFile` 分别用 `using static TypeScript.Parser.Parser;` /
//    `using static TypeScript.Binder.Binder;`（`Parser`/`Binder`/`Module`/`ModuleSpecifiers` 都既是命名空间又是静态类）。
//    `Ls` 静态类的成员用 `using static TypeScript.Ls.Ls;` 后的裸名（同名遮蔽见上一条）。
// 10) `findDeclarationNodesByName` 的局部 `type candidate struct { node; depth }` → C# 的 `List<(Node Node, int Depth)>`
//     （Go 的 `math.MaxInt` → `int.MaxValue`）；`uniqueDeclarationNodes` 的局部 `type declarationKey struct` →
//     `internal readonly record struct DeclarationKey(string FileName, TextRange Loc)`（Go 的结构相等语义）。
// 11) `sourceFile.AsNode()` → C# 的 `Node.AsNode()`（Ast/Node.cs:42，返回 this）；`sourceFile.Statements.Nodes` →
//     `sourceFile.Statements()?.Nodes`（C# 侧是方法 + 可空 NodeList）。
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Module;
using TypeScript.PackageJson;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Binder.Binder;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using static TypeScript.Module.Module;
using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;
using static TypeScript.Parser.Parser;
using static TypeScript.Ls.Ls;
using static TypeScript.TSPath.TSPath;
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Type = TypeScript.Checker.Type;
using Program = TypeScript.Compiler.Program;

namespace TypeScript.Ls;

/// <summary>Go: <c>type declarationKey struct { fileName string; loc core.TextRange }</c>（sourcedefinition.go:688-691）。</summary>
internal readonly record struct DeclarationKey(string FileName, TextRange Loc);

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideSourceDefinition(ctx, documentURI, position) (lsproto.DefinitionResponse, error)</c>（sourcedefinition.go:24-42）。</summary>
    public (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) ProvideSourceDefinition(
        Context ctx,
        DocumentUri documentURI,
        Position position)
    {
        var (program, file) = GetProgramAndFile(documentURI);
        var positions = _converters!.FromLSPPositionForSourceFile(file, position, Feature.Definition);
        var results = new List<LocationOrLocationsOrDefinitionLinksOrNull>(positions.Length);
        foreach (var mapped in positions)
        {
            if (mapped.Fidelity.IsSingleSegment())
            {
                results.Add(ProvideSourceDefinitionAtPosition(ctx, program, mapped.Script, mapped.Position));
            }
        }

        return (CombineDefinitionResponses(results, GetClientCapabilities().TextDocument.Definition.LinkSupport), null);
    }

    /// <summary>Go: <c>func (l *LanguageService) provideSourceDefinitionAtPosition(ctx, program, file, textPos) (lsproto.DefinitionResponse, error)</c>（sourcedefinition.go:44-126，见文件头第 1 条）。</summary>
    internal LocationOrLocationsOrDefinitionLinksOrNull ProvideSourceDefinitionAtPosition(
        Context ctx,
        Program program,
        SourceFile file,
        TextPos textPos)
    {
        var clientSupportsLink = GetClientCapabilities().TextDocument.Definition.LinkSupport;

        var pos = (int)textPos;
        var resolver = NewSourceDefResolver(program, file.FileName());
        var node = GetTouchingPropertyName(file, pos);

        if (node.Kind == Kind.SourceFile)
        {
            // Triple-slash directives are comments, not AST nodes, so
            // GetTouchingPropertyName returns the SourceFile node.
            var (tripleSlashDeclarations, reference) = resolver.ResolveTripleSlashReference(file, pos, program);
            if (tripleSlashDeclarations.Length != 0)
            {
                var (originSelectionRangeForReference, _) = CreateLspRangeFromBounds(reference!.Range.Pos(), reference.Range.End(), file);
                return CreateDefinitionLocations(originSelectionRangeForReference, clientSupportsLink, tripleSlashDeclarations, null /*reference*/, Feature.Definition);
            }

            return new LocationOrLocationsOrDefinitionLinksOrNull();
        }

        var (originSelectionRange, _) = CreateLspRangeFromNode(node, file);

        // If the cursor is directly on a module specifier string, resolve to the
        // implementation file's entry point.
        var containingModuleSpecifier = FindContainingModuleSpecifier(node);
        if (node == containingModuleSpecifier)
        {
            var specifierMode = program.GetModeForUsageLocation(file, containingModuleSpecifier);
            var implementationFile = resolver.ResolveImplementation(containingModuleSpecifier.Text(), specifierMode);
            if (implementationFile != "")
            {
                if (resolver.GetOrParseSourceFile(implementationFile) is { } sourceFile)
                {
                    return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, GetSourceDefinitionEntryDeclarations(sourceFile), null, Feature.Definition);
                }
            }

            return ProvideDefinitionAtPosition(ctx, program, file, textPos, clientSupportsLink);
        }

        // Phase 1: Syntactic fast path — when the cursor is inside an
        // import/require/export, forward-resolve the module specifier to an
        // implementation file and search it directly. This avoids acquiring
        // the type checker entirely when the fast path succeeds.
        var resolvedImplFile = "";
        if (containingModuleSpecifier is not null)
        {
            var specifierMode = program.GetModeForUsageLocation(file, containingModuleSpecifier);
            resolvedImplFile = resolver.ResolveImplementation(containingModuleSpecifier.Text(), specifierMode);
        }

        if (resolvedImplFile != "")
        {
            var names = GetCandidateSourceDeclarationNames(node, null);
            var moduleResults = resolver.SearchImplementationFile(node, resolvedImplFile, names);
            if ((moduleResults?.Length ?? 0) != 0)
            {
                if (!IsPartOfTypeNode(node) && !IsPartOfTypeOnlyImportOrExportDeclaration(node) || HasConcreteSourceDeclarations(moduleResults!))
                {
                    return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, UniqueDeclarationNodes(moduleResults!), null, Feature.Definition);
                }
            }
        }

        // Phase 2: Type checker path — acquire the checker for the original file
        // and use its declarations and module specifier to map to source
        // implementations. This is the only point where the checker is used;
        // after this, only the NoDts module resolver and file parsing are needed.
        var (checkerDeclarations, moduleSpecifier) = GetSourceDefCheckerInfo(ctx, program, file, node);

        // Phase 3: Map checker results to source definitions.
        var declarations = resolver.ResolveFromCheckerInfo(node, resolvedImplFile, checkerDeclarations, moduleSpecifier);
        if ((declarations?.Length ?? 0) == 0)
        {
            // If we resolved an implementation file from an import/export but
            // couldn't find specific declarations, fall back to the file entry
            // point rather than the standard definition provider — unless the
            // checker found declarations that are all type-only (e.g. interfaces),
            // in which case the .d.ts definition is more appropriate.
            if (containingModuleSpecifier is not null && resolvedImplFile != "" && !HasConcreteSourceDeclarations(checkerDeclarations))
            {
                if (resolver.GetOrParseSourceFile(resolvedImplFile) is { } sourceFile)
                {
                    return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, GetSourceDefinitionEntryDeclarations(sourceFile), null, Feature.Definition);
                }
            }

            return ProvideDefinitionAtPosition(ctx, program, file, textPos, clientSupportsLink);
        }

        return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, declarations!, null /*reference*/, Feature.Definition);
    }

    /// <summary>Go: <c>func (l *LanguageService) newSourceDefResolver(program, resolveFrom) *sourceDefResolver</c>（sourcedefinition.go:150-172）。</summary>
    internal SourceDefResolver NewSourceDefResolver(Program program, string resolveFrom)
    {
        var options = program.Options();
        var noDtsOptions = options.Clone();
        noDtsOptions.NoDtsResolution = Tristate.TSTrue;
        return new SourceDefResolver(
            this,
            program.Host().FS(),
            options,
            program.GetSourceFile,
            resolveFrom,
            NewResolver(new ResolverOptions
            {
                Host = program.Host(),
                CompilerOptions = noDtsOptions,
                TypingsLocation = program.GetGlobalTypingsCacheLocation(),
                ExtraExtensions = program.CommandLine().ContentMapperExtensions(),
            }));
    }

    /// <summary>Go: <c>func getSourceDefCheckerInfo(ctx, program, file, node) ([]*ast.Node, string)</c>（sourcedefinition.go:205-269）。</summary>
    internal static (Node[] Declarations, string ModuleSpecifier) GetSourceDefCheckerInfo(
        Context ctx,
        Program program,
        SourceFile file,
        Node node)
    {
        var (c, done) = program.GetTypeCheckerForFile(ctx, file);
        try
        {
            var declarations = GetDeclarationsFromLocation(c, node);
            var isPropertyName = node.Parent is not null && IsAccessExpression(node.Parent) && node.Parent.Name() == node;
            if (declarations.Length == 0 && isPropertyName)
            {
                if (node.Parent!.Expression() is { } left)
                {
                    if (c.GetPropertyOfType(c.GetTypeAtLocation(left), node.Text()) is { } prop)
                    {
                        declarations = prop.Declarations ?? [];
                    }
                }
            }

            if (TryGetSignatureDeclaration(c, node) is { } calledDeclaration)
            {
                var nonFunctionDeclarations = Filter(declarations, node1 => !IsFunctionLike(node1));
                declarations = Concatenate(nonFunctionDeclarations, [calledDeclaration]);
            }

            // Extract module specifier from the import that brought this symbol into
            // scope. For property access (obj.prop), walk up the access chain to the
            // root expression's symbol.
            var moduleSpecifier = "";
            var resolveNode = node;
            if (isPropertyName)
            {
                var expr = node.Parent!.Expression();
                while (expr is not null && IsAccessExpression(expr))
                {
                    expr = expr.Expression();
                }

                if (expr is not null)
                {
                    resolveNode = expr;
                }
            }

            if (c.GetSymbolAtLocation(resolveNode) is { } sym)
            {
                foreach (var d in sym.Declarations ?? [])
                {
                    if (!IsImportSpecifier(d) && !IsImportClause(d) && !IsNamespaceImport(d) && !IsImportEqualsDeclaration(d))
                    {
                        continue;
                    }

                    if (TypeScript.Checker.Checker.TryGetModuleSpecifierFromDeclaration(d) is { } spec)
                    {
                        moduleSpecifier = spec.Text();
                        break;
                    }
                }
            }

            return (declarations, moduleSpecifier);
        }
        finally
        {
            done();
        }
    }
}

/// <summary>
/// Go: <c>type sourceDefResolver struct</c>（sourcedefinition.go:128-148）。
/// sourceDefResolver resolves source definitions by mapping .d.ts declarations
/// to their implementation files (.js/.ts). It uses the NoDts module resolver
/// and file parsing for resolution, but never acquires the type checker or
/// the original program; all checker-dependent work is done before results
/// are passed in.
/// </summary>
internal sealed class SourceDefResolver
{
    internal readonly LanguageService _ls;
    internal readonly FS _fs;
    internal readonly CompilerOptions _options;
    internal readonly Func<string, SourceFile?> _getSourceFile;
    internal readonly string _resolveFrom;
    internal readonly DefaultResolver _resolver;
    internal Dictionary<string, SourceFile?>? _parsedFiles;

    internal SourceDefResolver(
        LanguageService ls,
        FS fs,
        CompilerOptions options,
        Func<string, SourceFile?> getSourceFile,
        string resolveFrom,
        DefaultResolver resolver)
    {
        _ls = ls;
        _fs = fs;
        _options = options;
        _getSourceFile = getSourceFile;
        _resolveFrom = resolveFrom;
        _resolver = resolver;
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) resolveFromCheckerInfo(node, resolvedImplFile, checkerDeclarations, moduleSpecifier) []*ast.Node</c>（sourcedefinition.go:174-203）。</summary>
    internal Node[]? ResolveFromCheckerInfo(
        Node node,
        string resolvedImplFile,
        Node[] checkerDeclarations,
        string moduleSpecifier)
    {
        // If we don't yet have a forward-resolved implementation file, try to
        // recover a module specifier from the checker (e.g. from the import that
        // brought the symbol into scope, or from the root of an access expression).
        if (resolvedImplFile == "" && moduleSpecifier != "")
        {
            resolvedImplFile = ResolveImplementation(moduleSpecifier, InferImpliedNodeFormat(_resolveFrom));
        }

        // For property access where the checker found no declarations (e.g.
        // mapped types), search the implementation file for the property name.
        if (checkerDeclarations.Length == 0 && resolvedImplFile != "")
        {
            var names = GetCandidateSourceDeclarationNames(node, null);
            if (SearchImplementationFile(node, resolvedImplFile, names) is { } results)
            {
                return UniqueDeclarationNodes(results);
            }
        }

        var declarations = Array.Empty<Node>();
        foreach (var declaration in checkerDeclarations)
        {
            declarations = Concatenate(declarations, MapDeclarationToSource(node, declaration, resolvedImplFile));
        }

        declarations = UniqueDeclarationNodes(declarations);
        if (HasConcreteSourceDeclarations(declarations))
        {
            return declarations;
        }

        return null;
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) resolveTripleSlashReference(file, pos, program) ([]*ast.Node, *ast.FileReference)</c>（sourcedefinition.go:271-303，见文件头第 3 条）。</summary>
    internal (Node[] Declarations, FileReference? Reference) ResolveTripleSlashReference(
        SourceFile file,
        int pos,
        Program program)
    {
        var reference = GetReferenceAtPosition(file, pos, program);
        if (reference is null || reference._file is null)
        {
            return ([], null);
        }

        // If the referenced file is already an implementation file, return it directly.
        if (!reference._file.IsDeclarationFile)
        {
            return (GetSourceDefinitionEntryDeclarations(reference._file), reference._reference);
        }

        // The referenced file is a .d.ts. Try to find the implementation file
        // using the NoDts module resolver via findImplementationFileFromDtsFileName.
        var dtsFileName = reference._file.FileName();
        var preferredMode = InferImpliedNodeFormat(dtsFileName);
        var implementationFile = FindImplementationFileFromDtsFileName(dtsFileName, preferredMode);
        if (implementationFile == "")
        {
            return ([], null);
        }

        var sourceFile = GetOrParseSourceFile(implementationFile);
        if (sourceFile is null)
        {
            return ([], null);
        }

        return (GetSourceDefinitionEntryDeclarations(sourceFile), reference._reference);
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) searchImplementationFile(originalNode, implementationFile, names) []*ast.Node</c>（sourcedefinition.go:305-333）。</summary>
    internal Node[]? SearchImplementationFile(
        Node originalNode,
        string implementationFile,
        string[] names)
    {
        if (implementationFile == "")
        {
            return null;
        }

        var sourceFile = GetOrParseSourceFile(implementationFile);
        if (sourceFile is null)
        {
            return null;
        }

        if (IsDefaultImportName(originalNode))
        {
            // For default imports, only search for "default" declarations to avoid
            // matching unrelated declarations with the same identifier name.
            var defaultDeclarations = FindDeclarationsInFile(implementationFile, ["default"], new Set<string>());
            if ((defaultDeclarations?.Length ?? 0) != 0)
            {
                return FilterPreferredSourceDeclarations(originalNode, defaultDeclarations!);
            }

            return GetSourceDefinitionEntryDeclarations(sourceFile);
        }

        var declarations = FindDeclarationsInFile(implementationFile, names, new Set<string>());
        if ((declarations?.Length ?? 0) != 0)
        {
            return FilterPreferredSourceDeclarations(originalNode, declarations!);
        }

        return null;
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) mapDeclarationToSource(originalNode, declaration, resolvedImplFile) []*ast.Node</c>（sourcedefinition.go:343-373）。</summary>
    internal Node[]? MapDeclarationToSource(
        Node originalNode,
        Node declaration,
        string resolvedImplFile)
    {
        var (file, startPos) = GetFileAndStartPosFromDeclaration(declaration);
        var fileName = file.FileName();

        if (_ls.TryGetSourcePosition(fileName, startPos) is { } mapped)
        {
            if (GetOrParseSourceFile(mapped.FileName) is { } sourceFile)
            {
                return [FindClosestDeclarationNode(sourceFile, mapped.Pos)];
            }
        }

        if (!IsDeclarationFileName(fileName))
        {
            return [declaration];
        }

        var implementationFile = resolvedImplFile;
        if (implementationFile == "")
        {
            // Reverse-resolve .d.ts path to implementation file. This path is only
            // reached for declarations with no associated module specifier (e.g.
            // globals, ambient declarations, or when forward resolution failed).
            var dtsFileName = GetSourceFileOfNode(declaration)!.FileName();
            var preferredMode = InferImpliedNodeFormat(dtsFileName);
            implementationFile = FindImplementationFileFromDtsFileName(dtsFileName, preferredMode);
        }

        return SearchImplementationFile(originalNode, implementationFile, GetCandidateSourceDeclarationNames(originalNode, declaration));
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) findImplementationFileFromDtsFileName(dtsFileName, preferredMode) string</c>（sourcedefinition.go:375-416）。</summary>
    internal string FindImplementationFileFromDtsFileName(string dtsFileName, ModuleKind preferredMode)
    {
        var jsExt = TryGetJSExtensionForFile(dtsFileName, _options);
        if (jsExt != "")
        {
            var candidate = ChangeExtension(dtsFileName, jsExt);
            if (_fs.FileExists(candidate))
            {
                return candidate;
            }
        }

        var parts = GetNodeModulePathParts(dtsFileName);
        if (parts is null)
        {
            return "";
        }

        // Ensure the file only contains one /node_modules/ segment. If there's more
        // than one, the package name extraction may be incorrect, so bail out.
        if (dtsFileName.LastIndexOf("/node_modules/", StringComparison.Ordinal) != parts.TopLevelNodeModulesIndex)
        {
            return "";
        }

        var packageNamePathPart = dtsFileName[(parts.TopLevelPackageNameIndex + 1)..parts.PackageRootIndex];
        var packageName = GetPackageNameFromTypesPackageName(UnmangleScopedPackageName(packageNamePathPart));
        if (packageName == "")
        {
            return "";
        }

        var pathToFileInPackage = dtsFileName[(parts.PackageRootIndex + 1)..];

        // Try resolving as a package subpath first (e.g. "pkg/dist/utils"), then
        // fall back to the bare package name (e.g. "pkg"). This covers both main
        // entrypoints and deep imports without needing to inspect package.json
        // entrypoints.
        if (pathToFileInPackage != "")
        {
            var specifier = packageName + "/" + RemoveFileExtension(pathToFileInPackage);
            if (ResolveImplementation(specifier, preferredMode) is { Length: > 0 } implementationFile)
            {
                return implementationFile;
            }
        }

        return ResolveImplementation(packageName, preferredMode);
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) resolveImplementation(moduleName, preferredMode) string</c>（sourcedefinition.go:418-423）。</summary>
    internal string ResolveImplementation(string moduleName, ModuleKind preferredMode)
    {
        return ResolveImplementationFrom(moduleName, _resolveFrom, preferredMode);
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) resolveImplementationFrom(moduleName, resolveFromFile, preferredMode) string</c>（sourcedefinition.go:425-444）。</summary>
    internal string ResolveImplementationFrom(string moduleName, string resolveFromFile, ModuleKind preferredMode)
    {
        var modes = new List<ModuleKind> { preferredMode };
        if (preferredMode != ModuleKind.ESNext)
        {
            modes.Add(ModuleKind.ESNext);
        }

        if (preferredMode != ModuleKind.CommonJS)
        {
            modes.Add(ModuleKind.CommonJS);
        }

        foreach (var mode in modes)
        {
            var (resolved, _) = _resolver.ResolveModuleName(moduleName, resolveFromFile, mode, null);
            if (resolved is not null && resolved.IsResolved() && !IsDeclarationFileName(resolved.ResolvedFileName))
            {
                return resolved.ResolvedFileName;
            }
        }

        return "";
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) getOrParseSourceFile(fileName) *ast.SourceFile</c>（sourcedefinition.go:446-469）。</summary>
    internal SourceFile? GetOrParseSourceFile(string fileName)
    {
        if (_getSourceFile(fileName) is { } fromProgram)
        {
            return fromProgram;
        }

        if (_parsedFiles is not null && _parsedFiles.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        SourceFile? sourceFile = null;
        if (_ls.TryReadFile(fileName, out var text))
        {
            sourceFile = ParseSourceFile(
                new SourceFileParseOptions
                {
                    FileName = fileName,
                    Path = _ls.ToPath(fileName),
                },
                text,
                // A declaration map's `sources` entries are arbitrary strings, so the
                // file name here may not have a recognized extension.
                EnsureScriptKindFromFileName(fileName));
            BindSourceFile(sourceFile);
        }

        _parsedFiles ??= [];
        _parsedFiles[fileName] = sourceFile;
        return sourceFile;
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) inferImpliedNodeFormat(fileName) core.ResolutionMode</c>（sourcedefinition.go:471-480）。</summary>
    internal ModuleKind InferImpliedNodeFormat(string fileName)
    {
        var packageJsonType = "";
        var scope = _resolver.GetPackageScopeForPath(GetDirectoryPath(fileName));
        if (scope is not null && scope.Exists() && scope.Contents!.HeaderFields.Type.TryGetValue(out var value))
        {
            packageJsonType = value;
        }

        return GetImpliedNodeFormatForFile(fileName, packageJsonType);
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) findDeclarationsInFile(fileName, names, seen) []*ast.Node</c>（sourcedefinition.go:493-527）。</summary>
    internal Node[]? FindDeclarationsInFile(string fileName, string[] names, Set<string> seen)
    {
        if (fileName == "" || names.Length == 0)
        {
            return null;
        }

        if (!seen.AddIfAbsent(fileName))
        {
            return null;
        }

        var sourceFile = GetOrParseSourceFile(fileName);
        if (sourceFile is null)
        {
            return null;
        }

        var declarations = FindDeclarationNodesByName(sourceFile, names);
        if (declarations.Length != 0 && HasConcreteSourceDeclarations(declarations))
        {
            return declarations;
        }

        var forwarded = Array.Empty<Node>();
        foreach (var forwardedFile in GetForwardedImplementationFiles(sourceFile))
        {
            forwarded = Concatenate(forwarded, FindDeclarationsInFile(forwardedFile, names, seen));
        }

        if (forwarded.Length != 0)
        {
            if (HasConcreteSourceDeclarations(forwarded))
            {
                return UniqueDeclarationNodes(forwarded);
            }

            return UniqueDeclarationNodes(Concatenate(declarations, forwarded));
        }

        return declarations;
    }

    /// <summary>Go: <c>func (r *sourceDefResolver) getForwardedImplementationFiles(sourceFile) []string</c>（sourcedefinition.go:529-541）。</summary>
    internal string[] GetForwardedImplementationFiles(SourceFile sourceFile)
    {
        var preferredMode = InferImpliedNodeFormat(sourceFile.FileName());

        var files = new List<string>();
        foreach (var imp in sourceFile.Imports())
        {
            var moduleName = imp.Text();
            var implementationFile = ResolveImplementationFrom(moduleName, sourceFile.FileName(), preferredMode);
            if (implementationFile != "")
            {
                files.Add(implementationFile);
            }
        }

        return Deduplicate(files.ToArray());
    }
}

public static partial class Ls
{
    /// <summary>Go: <c>func isDefaultImportName(node *ast.Node) bool</c>（sourcedefinition.go:335-341）。</summary>
    internal static bool IsDefaultImportName(Node? node)
    {
        if (node is null || node.Parent is null || !IsImportClause(node.Parent) || node.Parent.Name() != node || node.Parent.Parent is null)
        {
            return false;
        }

        return IsDefaultImport(node.Parent.Parent);
    }

    /// <summary>Go: <c>func getSourceDefinitionEntryNode(sourceFile *ast.SourceFile) *ast.Node</c>（sourcedefinition.go:343-... 见文件头第 3 条）。</summary>
    internal static Node GetSourceDefinitionEntryNode(SourceFile sourceFile)
    {
        var statements = sourceFile.Statements();
        if (statements is not null && statements.Length != 0)
        {
            return statements[0].AsNode();
        }

        return sourceFile.AsNode();
    }

    /// <summary>Go: <c>func getSourceDefinitionEntryDeclarations(sourceFile *ast.SourceFile) []*ast.Node</c>（sourcedefinition.go:348-350）。</summary>
    internal static Node[] GetSourceDefinitionEntryDeclarations(SourceFile sourceFile)
    {
        return [GetSourceDefinitionEntryNode(sourceFile)];
    }

    /// <summary>Go: <c>func findContainingModuleSpecifier(node *ast.Node) *ast.Node</c>（sourcedefinition.go:482-491）。</summary>
    internal static Node? FindContainingModuleSpecifier(Node? node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (IsAnyImportOrReExport(current) || IsRequireCall(current, true /*requireStringLiteralLikeArgument*/) || IsImportCall(current))
            {
                if (GetExternalModuleName(current) is { } moduleSpecifier && IsStringLiteralLike(moduleSpecifier))
                {
                    return moduleSpecifier;
                }
            }
        }

        return null;
    }

    /// <summary>Go: <c>func getCandidateSourceDeclarationNames(originalNode, declaration *ast.Node) []string</c>（sourcedefinition.go:543-578）。</summary>
    internal static string[] GetCandidateSourceDeclarationNames(Node? originalNode, Node? declaration)
    {
        var names = new List<string>();
        if (declaration is not null)
        {
            if (GetNameOfDeclaration(declaration) is { } name)
            {
                var text = GetTextOfPropertyName(name);
                if (text != "")
                {
                    names.Add(text);
                }
            }

            if (declaration.Kind == Kind.ExportAssignment)
            {
                names.Add("default");
            }

            if ((IsFunctionDeclaration(declaration) || IsClassDeclaration(declaration))
                && (declaration.ModifierFlags & ModifierFlags.ExportDefault) == ModifierFlags.ExportDefault)
            {
                names.Add("default");
            }

            if (IsImportSpecifier(declaration) || IsExportSpecifier(declaration))
            {
                if (declaration.PropertyName() is { } propName)
                {
                    names.Add(propName.Text());
                }
            }
        }

        if (originalNode is not null)
        {
            if (IsIdentifier(originalNode) || IsPrivateIdentifier(originalNode))
            {
                names.Add(originalNode.Text());
            }

            if (IsDefaultImportName(originalNode))
            {
                names.Add("default");
            }

            if (originalNode.Parent is not null)
            {
                if (IsImportSpecifier(originalNode.Parent) || IsExportSpecifier(originalNode.Parent))
                {
                    if (originalNode.Parent.PropertyName() is { } propName)
                    {
                        names.Add(propName.Text());
                    }
                }
            }
        }

        return names.ToArray();
    }

    /// <summary>Go: <c>func findDeclarationNodesByName(sourceFile *ast.SourceFile, names []string) []*ast.Node</c>（sourcedefinition.go:580-638）。</summary>
    internal static Node[] FindDeclarationNodesByName(SourceFile sourceFile, string[] names)
    {
        names = Deduplicate(Filter(names, name => name != ""));
        if (names.Length == 0)
        {
            return [];
        }

        var wanted = new Set<string>();
        var wantDefault = false;
        foreach (var name in names)
        {
            if (name == "default")
            {
                wantDefault = true;
                continue;
            }

            wanted.Add(name);
        }

        var candidates = new List<(Node Node, int Depth)>();
        var minDepth = int.MaxValue;

        Visitor visit = null!;
        visit = node =>
        {
            var matched = false;
            if (GetNameOfDeclaration(node) is { } name)
            {
                var text = GetTextOfPropertyName(name);
                if (text != "" && wanted.Has(text))
                {
                    matched = true;
                }
            }

            if (wantDefault && node.Kind == Kind.ExportAssignment)
            {
                matched = true;
            }

            if (wantDefault && (IsFunctionDeclaration(node) || IsClassDeclaration(node))
                && (node.ModifierFlags & ModifierFlags.ExportDefault) == ModifierFlags.ExportDefault)
            {
                matched = true;
            }

            if (matched)
            {
                var depth = GetContainerDepth(node);
                candidates.Add((node, depth));
                if (depth < minDepth)
                {
                    minDepth = depth;
                }
            }

            return node.ForEachChild(visit);
        };
        sourceFile.AsNode().ForEachChild(visit);

        // Only keep declarations at the shallowest depth, like getTopMostDeclarationNamesInFile.
        var declarations = new List<Node>();
        foreach (var candidate in candidates)
        {
            if (candidate.Depth == minDepth)
            {
                declarations.Add(candidate.Node);
            }
        }

        return UniqueDeclarationNodes(declarations.ToArray());
    }

    /// <summary>Go: <c>func getContainerDepth(node *ast.Node) int</c>（sourcedefinition.go:640-648）。</summary>
    internal static int GetContainerDepth(Node node)
    {
        var depth = 0;
        Node? current = node;
        while (current is not null)
        {
            current = GetContainerNode(current);
            depth++;
        }

        return depth;
    }

    /// <summary>Go: <c>func filterPreferredSourceDeclarations(originalNode, declarations *ast.Node) []*ast.Node</c>（sourcedefinition.go:650-663）。</summary>
    internal static Node[] FilterPreferredSourceDeclarations(Node? originalNode, Node[] declarations)
    {
        if (declarations.Length <= 1 || originalNode is null)
        {
            return declarations;
        }

        if (GetPropertyLikeSourceDeclarations(originalNode, declarations) is { Length: > 0 } preferred)
        {
            return preferred;
        }

        if (Filter(declarations, IsConcreteSourceDeclaration) is { Length: > 0 } concretePreferred)
        {
            return concretePreferred;
        }

        return declarations;
    }

    /// <summary>Go: <c>func getPropertyLikeSourceDeclarations(originalNode, declarations) []*ast.Node</c>（sourcedefinition.go:665-686）。</summary>
    internal static Node[] GetPropertyLikeSourceDeclarations(Node originalNode, Node[] declarations)
    {
        if (originalNode.Parent is null || !IsAccessExpression(originalNode.Parent) || originalNode.Parent.Name() != originalNode)
        {
            return [];
        }

        return Filter(declarations, node => node.Kind switch
        {
            Kind.PropertyAssignment
                or Kind.ShorthandPropertyAssignment
                or Kind.PropertyDeclaration
                or Kind.PropertySignature
                or Kind.MethodDeclaration
                or Kind.MethodSignature
                or Kind.GetAccessor
                or Kind.SetAccessor
                or Kind.EnumMember => true,
            _ => false,
        });
    }

    /// <summary>Go: <c>func hasConcreteSourceDeclarations(declarations []*ast.Node) bool</c>（sourcedefinition.go:688-690）。</summary>
    internal static bool HasConcreteSourceDeclarations(Node[] declarations)
    {
        return Some(declarations, IsConcreteSourceDeclaration);
    }

    /// <summary>Go: <c>func isConcreteSourceDeclaration(node *ast.Node) bool</c>（sourcedefinition.go:692-716）。</summary>
    internal static bool IsConcreteSourceDeclaration(Node node)
    {
        if (!IsDeclaration(node) || node.Kind == Kind.ExportAssignment)
        {
            return false;
        }

        if ((IsBinaryExpression(node) || IsCallExpression(node)) && GetAssignmentDeclarationKind(node) != JSDeclarationKind.None)
        {
            return false;
        }

        return node.Kind switch
        {
            Kind.Parameter
                or Kind.TypeParameter
                or Kind.BindingElement
                or Kind.ImportClause
                or Kind.ImportSpecifier
                or Kind.NamespaceImport
                or Kind.ExportSpecifier
                or Kind.PropertyAccessExpression
                or Kind.ElementAccessExpression => false,
            _ => true,
        };
    }

    /// <summary>Go: <c>func uniqueDeclarationNodes(nodes []*ast.Node) []*ast.Node</c>（sourcedefinition.go:718-734，见文件头第 10 条）。</summary>
    internal static Node[] UniqueDeclarationNodes(Node[] nodes)
    {
        var seen = new Set<DeclarationKey>();
        var result = new List<Node>(nodes.Length);
        foreach (var node in nodes)
        {
            if (node is null)
            {
                continue;
            }

            var fileName = GetSourceFileOfNode(node)!.FileName();
            if (!seen.AddIfAbsent(new DeclarationKey(fileName, node.Loc)))
            {
                continue;
            }

            result.Add(node);
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func findClosestDeclarationNode(sourceFile *ast.SourceFile, pos int) *ast.Node</c>（sourcedefinition.go:736-...）。</summary>
    internal static Node FindClosestDeclarationNode(SourceFile sourceFile, int pos)
    {
        var node = GetTouchingPropertyName(sourceFile, pos);
        for (var current = node; current is not null; current = current.Parent)
        {
            if (IsDeclaration(current) || current.Kind == Kind.ExportAssignment)
            {
                return current;
            }
        }

        return GetSourceDefinitionEntryNode(sourceFile);
    }
}

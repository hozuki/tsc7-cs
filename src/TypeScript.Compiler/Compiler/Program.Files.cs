// ported from: internal/compiler/program.go:1690-1836,2067-2460（计数 API / source file 元数据 / CommonSourceDirectory /
// 文件查找 / ExplainFiles / lib 与 type reference 查询 / 包名收集 / symlink cache / resolved module 遍历）
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `Program.toPath` 与 `tspath.ToPath` 同名：C# 里实例方法（Go 未导出 → internal PascalCase）会遮蔽
//    `using static` 引入的静态方法，所以静态调用点写全名 `TypeScript.TSPath.TSPath.ToPath(...)`。
// 2) `(T, bool)` → `bool TryX(..., out T)`（TryGetResolvedProjectReferenceFor / TryGetLibFileName）。
// 3) `atomic.Uint32` 累加 → `int[1]` + `Interlocked.Add`（Go 的计数也是无符号 32 位；这里保持 int 语义，
//    因为 SourceFile.SymbolCount 本身就是 int）。
// 4) `p.Program()`（返回自身）在 C# 里不能与类同名 → 落成 `ProgramSelf()`（并把 `ProgramLike.Program()` 换成
//    `ContentMapperDiagnostics()`，见 Program.Emit.cs）。
// 5) Go 的 map 缺失键读零值 → C# `TryGetValue`/`GetValueOrDefault`（`ModeAwareCache` 是 Dictionary 派生，缺键会抛）。
namespace TypeScript.Compiler;

using System.IO;
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Module;
using TypeScript.ModuleSpecifiers;
using TypeScript.PackageJson;
using TypeScript.Symlinks;
using TypeScript.TsOptions;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Module.Module;
using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using Loc = TypeScript.Locale.Locale;
using Path = TypeScript.TSPath.Path;

public sealed partial class Program
{
    /// <summary>Go: <c>func (p *Program) toPath(filename string) tspath.Path</c>（program.go:2067）。</summary>
    internal Path ToPath(string filename) =>
        TypeScript.TSPath.TSPath.ToPath(filename, GetCurrentDirectory(), UseCaseSensitiveFileNames());

    /// <summary>Go: <c>func (p *Program) GetSourceFile(filename string) *ast.SourceFile</c>（program.go:2071）。</summary>
    public SourceFile? GetSourceFile(string filename)
    {
        var path = ToPath(filename);
        return GetSourceFileByPath(path);
    }

    /// <summary>Go: <c>func (p *Program) GetSourceFileForResolvedModule(fileName string) *ast.SourceFile</c>（program.go:2076）。</summary>
    public SourceFile? GetSourceFileForResolvedModule(string fileName)
    {
        var file = GetSourceFile(fileName);
        if (file is null)
        {
            var filename = GetParseFileRedirect(fileName);
            if (filename != "")
            {
                return GetSourceFile(filename);
            }
        }
        return file;
    }

    /// <summary>Go: <c>func (p *Program) FilesByPath() map[tspath.Path]*ast.SourceFile</c>（program.go:2086）。</summary>
    public Dictionary<Path, SourceFile> FilesByPath() => _filesByPath;

    /// <summary>Go: <c>func (p *Program) GetSourceFileByPath(path tspath.Path) *ast.SourceFile</c>（program.go:2091）。</summary>
    public SourceFile? GetSourceFileByPath(Path path) => _filesByPath.GetValueOrDefault(path);

    /// <summary>Go: <c>func (p *Program) HasSameFileNames(other *Program) bool</c>（program.go:2095）。</summary>
    public bool HasSameFileNames(Program other)
    {
        // 检查大小写不敏感文件系统上的大小写差异
        if (_filesByPath.Count != other._filesByPath.Count)
        {
            return false;
        }
        foreach (var (path, file) in _filesByPath)
        {
            if (!other._filesByPath.TryGetValue(path, out var otherFile) || file.FileName() != otherFile.FileName())
            {
                return false;
            }
        }
        var otherRedirects = other._redirectFilesByPath;
        if ((_redirectFilesByPath?.Count ?? 0) != (otherRedirects?.Count ?? 0))
        {
            return false;
        }
        if (_redirectFilesByPath is not null && otherRedirects is not null)
        {
            foreach (var (path, redirect) in _redirectFilesByPath)
            {
                if (!otherRedirects.TryGetValue(path, out var otherRedirect) || redirect.FileName() != otherRedirect.FileName())
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>Go: <c>func (p *Program) GetSourceFiles() []*ast.SourceFile</c>（program.go:2104）。</summary>
    public SourceFile[] GetSourceFiles() => _files;

    /// <summary>Go: <c>func (p *Program) GetIncludeReasons() map[tspath.Path][]*FileIncludeReason</c>（program.go:2109，仅供测试）。</summary>
    public Dictionary<Path, List<FileIncludeReason>> GetIncludeReasons() => _includeProcessor._fileIncludeReasons;

    /// <summary>Go: <c>func (p *Program) IsMissingPath(path tspath.Path) bool</c>（program.go:2114，仅供测试）。</summary>
    public bool IsMissingPath(Path path) => _missingFiles.Any(missingPath => ToPath(missingPath) == path);

    /// <summary>Go: <c>func (p *Program) ExplainFiles(w io.Writer, locale locale.Locale)</c>（program.go:2119）。</summary>
    public void ExplainFiles(TextWriter w, Loc locale)
    {
        string ToRelativeFileName(string fileName) => GetRelativePathFromDirectory(GetCurrentDirectory(), fileName, _comparePathsOptions);
        var filesExplained = 0;
        void ExplainFile(HasFileName file)
        {
            // Go: fmt.Fprintln(w, x) 与 fmt.Fprintln(w, "  ", x)（Fprintln 会在操作数之间插空格 → 前缀是三个空格）
            w.WriteLine(ToRelativeFileName(file.FileName()));
            if (_includeProcessor._fileIncludeReasons.TryGetValue(file.Path(), out var reasons))
            {
                foreach (var reason in reasons)
                {
                    w.WriteLine("   " + reason.ToDiagnostic(this, true).Localize(locale));
                }
            }
            foreach (var diag in _includeProcessor.ExplainRedirectAndImpliedFormat(this, file.Path(), ToRelativeFileName) ?? [])
            {
                w.WriteLine("   " + diag.Localize(locale));
            }
            filesExplained++;
        }

        var redirectFiles = _redirectFilesByPath is null ? new List<RedirectsFile>() : [.. _redirectFilesByPath.Values];
        redirectFiles.Sort(static (a, b) => a._index - b._index);

        var files = GetSourceFiles();
        var sourceFileIndex = 0;
        void ExplainSourceFiles(int endIndex)
        {
            while (filesExplained < endIndex)
            {
                ExplainFile(files[sourceFileIndex]);
                sourceFileIndex++;
            }
        }

        foreach (var redirectFile in redirectFiles)
        {
            // 先把到达该 redirectFile 索引之前的 sourceFiles 全部解释掉
            ExplainSourceFiles(redirectFile._index);
            ExplainFile(redirectFile);
        }

        // 解释剩余的 sourceFiles
        ExplainSourceFiles(files.Length + redirectFiles.Count);
    }

    /// <summary>Go: <c>func (p *Program) GetLibFileFromReference(ref *ast.FileReference) *ast.SourceFile</c>（program.go:2145）。</summary>
    public SourceFile? GetLibFileFromReference(FileReference reference)
    {
        if (!TryGetLibFileName(reference.FileName, out var libFileName))
        {
            return null;
        }
        return _filesByPath.GetValueOrDefault((Path)libFileName);
    }

    /// <summary>Go: <c>func (p *Program) GetResolvedTypeReferenceDirectiveFromTypeReferenceDirective(...)</c>（program.go:2155）。</summary>
    public TypeScript.Module.ResolvedTypeReferenceDirective? GetResolvedTypeReferenceDirectiveFromTypeReferenceDirective(FileReference typeReference, SourceFile sourceFile) =>
        GetResolvedTypeReferenceDirective(sourceFile, typeReference.FileName, GetModeForTypeReferenceDirectiveInFile(typeReference, sourceFile));

    /// <summary>Go: <c>func (p *Program) GetResolvedTypeReferenceDirective(file, typeDirectiveName, mode)</c>（program.go:2159）。</summary>
    public TypeScript.Module.ResolvedTypeReferenceDirective? GetResolvedTypeReferenceDirective(HasFileName file, string typeDirectiveName, ModuleKind mode)
    {
        if (_typeResolutionsInFile.TryGetValue(file.Path(), out var resolutions) &&
            resolutions.TryGetValue(new ModeAwareCacheKey(typeDirectiveName, mode), out var resolved))
        {
            return resolved;
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Program) GetResolvedTypeReferenceDirectives() map[tspath.Path]module.ModeAwareCache[*module.ResolvedTypeReferenceDirective]</c>（program.go:2169）。</summary>
    public Dictionary<Path, ModeAwareCache<TypeScript.Module.ResolvedTypeReferenceDirective>> GetResolvedTypeReferenceDirectives() => _typeResolutionsInFile;

    /// <summary>Go: <c>func (p *Program) getModeForTypeReferenceDirectiveInFile(ref, sourceFile) core.ResolutionMode</c>（program.go:2173）。</summary>
    private ModuleKind GetModeForTypeReferenceDirectiveInFile(FileReference reference, SourceFile sourceFile)
    {
        if (reference.ResolutionMode != ModuleKind.None)
        {
            return reference.ResolutionMode;
        }
        return GetDefaultResolutionModeForFile(sourceFile);
    }

    /// <summary>Go: <c>func (p *Program) IsSourceFileFromExternalLibrary(file *ast.SourceFile) bool</c>（program.go:2180）。</summary>
    public bool IsSourceFileFromExternalLibrary(SourceFile file) => _sourceFilesFoundSearchingNodeModules.Has(file.Path());

    /// <summary>Go: <c>func (p *Program) GetJSXRuntimeImportSpecifier(path tspath.Path) (moduleReference string, specifier *ast.Node)</c>（program.go:2184）。</summary>
    public (string moduleReference, Node? specifier) GetJSXRuntimeImportSpecifier(Path path)
    {
        var result = _jsxRuntimeImportSpecifiers?.GetValueOrDefault(path);
        if (result is not null)
        {
            return (result._moduleReference, result._specifier);
        }
        return ("", null);
    }

    /// <summary>Go: <c>func (p *Program) GetImportHelpersImportSpecifier(path tspath.Path) *ast.Node</c>（program.go:2191）。</summary>
    public Node? GetImportHelpersImportSpecifier(Path path) => _importHelpersImportSpecifiers?.GetValueOrDefault(path);

    /// <summary>Go: <c>func (p *Program) SourceFileMayBeEmitted(sourceFile *ast.SourceFile, forceDtsEmit bool) bool</c>（program.go:2195）。</summary>
    public bool SourceFileMayBeEmitted(SourceFile sourceFile, bool forceDtsEmit) =>
        Compiler.SourceFileMayBeEmitted(sourceFile, this, forceDtsEmit, false);

    /// <summary>Go: <c>func (p *Program) ResolvedPackageNames() *collections.Set[string]</c>（program.go:2199）。</summary>
    public Set<string>? ResolvedPackageNames() => CollectPackageNames()?.Resolved;

    /// <summary>Go: <c>func (p *Program) UnresolvedPackageNames() *collections.Set[string]</c>（program.go:2203）。</summary>
    public Set<string>? UnresolvedPackageNames() => CollectPackageNames()?.Unresolved;

    /// <summary>Go: <c>func (p *Program) DeepImportPackageNames() *collections.Set[string]</c>（program.go:2207）。</summary>
    public Set<string>? DeepImportPackageNames() => CollectPackageNames()?.DeepImportPackages;

    /// <summary>Go: <c>func (p *Program) collectPackageNames() *packageNamesInfo</c>（program.go:2211）。</summary>
    private PackageNamesInfo? CollectPackageNames() =>
        _packageNames.GetValue(() =>
        {
            var packageNames = new PackageNamesInfo(new Set<string>(), new Set<string>(), new Set<string>());
            foreach (var file in _files)
            {
                if (IsSourceFileDefaultLibrary(file.Path()) || IsSourceFileFromExternalLibrary(file) || file.FileName().Contains("/node_modules/", StringComparison.Ordinal))
                {
                    // 检查 /node_modules/ 有点不精确，但 ATA 会把本地安装的 typings 当根文件，
                    // 那样就不会通过 IsSourceFileFromExternalLibrary。
                    continue;
                }
                foreach (var import in file.Imports())
                {
                    if (IsExternalModuleNameRelative(import.Text()))
                    {
                        continue;
                    }
                    if (_resolvedModules.TryGetValue(file.Path(), out var resolvedModules))
                    {
                        var key = new ModeAwareCacheKey(import.Text(), GetModeForUsageLocation(file, import));
                        if (resolvedModules.TryGetValue(key, out var resolvedModule) && resolvedModule.IsResolved())
                        {
                            if (!resolvedModule.IsExternalLibraryImport)
                            {
                                continue;
                            }
                            // 取包名的优先级：
                            // 1. PackageId.Name（package.json 里同时有 name 和 version 才有）
                            var name = resolvedModule.PackageId.Name;
                            if (name == "")
                            {
                                // 2. GetPackageScopeForPath —— 从包目录里的 package.json 取 name
                                var packageScope = _resolver.GetPackageScopeForPath(resolvedModule.ResolvedFileName);
                                if (packageScope is not null && packageScope.Exists())
                                {
                                    if (packageScope.Contents!.HeaderFields.Name.TryGetValue(out var scopeName))
                                    {
                                        name = scopeName;
                                    }
                                }
                            }
                            if (name == "")
                            {
                                // 3. GetPackageNameFromDirectory —— 从 node_modules 路径里提取
                                name = GetPackageNameFromDirectory(resolvedModule.ResolvedFileName);
                            }
                            // 4. 全都失败就不加空串
                            if (name != "")
                            {
                                packageNames.Resolved.Add(name);
                                // 识别 deep import：没有 exports 的包里的子路径导入（如 "lodash/fp"），
                                // 这类只能靠递归目录搜索找到。
                                var packageName = ParsePackageName(import.Text(), out var rest);
                                _ = packageName;
                                if (rest != "")
                                {
                                    var scope = _resolver.GetPackageScopeForPath(resolvedModule.ResolvedFileName);
                                    if (scope is not null && scope.Exists() && !scope.Contents!.PathFields.Exports.IsPresent())
                                    {
                                        packageNames.DeepImportPackages.Add(GetPackageNameFromTypesPackageName(name));
                                    }
                                }
                            }
                            continue;
                        }
                    }
                    packageNames.Unresolved.Add(import.Text());
                }
            }
            return packageNames;
        });

    /// <summary>Go: <c>func (p *Program) IsLibFile(sourceFile *ast.SourceFile) bool</c>（program.go:2271）。</summary>
    public bool IsLibFile(SourceFile sourceFile) => _libFiles.ContainsKey(sourceFile.Path());

    /// <summary>Go: <c>func (p *Program) HasTSFile() bool</c>（program.go:2276）。</summary>
    public bool HasTSFile()
    {
        lock (_hasTSFileOnceLock)
        {
            if (!_hasTSFileComputed)
            {
                foreach (var file in _files)
                {
                    if (HasImplementationTSFileExtension(file.FileName()))
                    {
                        _hasTSFile = true;
                        break;
                    }
                }
                _hasTSFileComputed = true;
            }
        }
        return _hasTSFile;
    }

    /// <summary>Go: <c>func (p *Program) GetSymlinkCache() *symlinks.KnownSymlinks</c>（program.go:2290）。</summary>
    public KnownSymlinks? GetSymlinkCache() =>
        _knownSymlinks.GetValue(() =>
        {
            var knownSymlinks = Symlinks.NewKnownSymlink(GetCurrentDirectory(), UseCaseSensitiveFileNames());

            // 解析出来的模块在 node_modules 里解析时会带上 realpath 信息
            if (_resolvedModules.Count > 0 || _typeResolutionsInFile.Count > 0)
            {
                knownSymlinks.SetSymlinksFromResolutions(ForEachResolvedModule, ForEachResolvedTypeReferenceDirective);
            }

            // 检查其它依赖里的软链
            var seenPackageJsons = new Set<Path>();
            foreach (var (filePath, meta) in _sourceFileMetaDatas)
            {
                if (meta.PackageJsonDirectory == "" ||
                    !SourceFileMayBeEmitted(GetSourceFileByPath(filePath)!, false) ||
                    !seenPackageJsons.AddIfAbsent(ToPath(meta.PackageJsonDirectory)))
                {
                    continue;
                }
                var packageJsonName = CombinePaths(meta.PackageJsonDirectory, "package.json");
                var info = GetPackageJsonInfo(packageJsonName);
                var contents = info?.GetContents();
                if (contents is null)
                {
                    continue;
                }

                foreach (var dep in contents.DependencyFields.GetRuntimeDependencyNames().Keys())
                {
                    // 常见情况下先跳过：我们已经为该 package.json 相邻的 node_modules 存过软链了
                    var possibleDirectoryPath = ToPath(CombinePaths(meta.PackageJsonDirectory, "node_modules", dep));
                    if (knownSymlinks.HasDirectory(possibleDirectoryPath))
                    {
                        continue;
                    }
                    if (!dep.StartsWith("@types", StringComparison.Ordinal))
                    {
                        var possibleTypesDirectoryPath = ToPath(CombinePaths(meta.PackageJsonDirectory, "node_modules", GetTypesPackageName(dep)));
                        if (knownSymlinks.HasDirectory(possibleTypesDirectoryPath))
                        {
                            continue;
                        }
                    }

                    var packageResolution = _resolver.ResolvePackageDirectory(dep, packageJsonName, ModuleKind.CommonJS, null);
                    if (packageResolution is not null && packageResolution.IsResolved() && packageResolution.OriginalPath != "")
                    {
                        knownSymlinks.ProcessResolution(
                            CombinePaths(packageResolution.OriginalPath, "package.json"),
                            CombinePaths(packageResolution.ResolvedFileName, "package.json"));
                    }
                }
            }
            return knownSymlinks;
        });

    /// <summary>Go: <c>func (p *Program) ForEachResolvedModule(callback, file)</c>（program.go:2352）。</summary>
    public void ForEachResolvedModule(Action<ResolvedModule, string, ModuleKind, Path> callback, SourceFile? file) =>
        ForEachResolution(_resolvedModules, callback, file);

    /// <summary>Go: <c>func (p *Program) ForEachResolvedTypeReferenceDirective(callback, file)</c>（program.go:2356）。</summary>
    public void ForEachResolvedTypeReferenceDirective(Action<TypeScript.Module.ResolvedTypeReferenceDirective, string, ModuleKind, Path> callback, SourceFile? file) =>
        ForEachResolution(_typeResolutionsInFile, callback, file);

    /// <summary>Go: <c>func forEachResolution[T any](resolutionCache, callback, file)</c>（program.go:2360）——包级函数。</summary>
    private static void ForEachResolution<T>(Dictionary<Path, ModeAwareCache<T>> resolutionCache, Action<T, string, ModuleKind, Path> callback, SourceFile? file)
    {
        if (file is not null)
        {
            if (resolutionCache.TryGetValue(file.Path(), out var resolutions))
            {
                foreach (var (key, resolution) in resolutions)
                {
                    callback(resolution, key.Name, key.Mode, file.Path());
                }
            }
        }
        else
        {
            foreach (var (filePath, resolutions) in resolutionCache)
            {
                foreach (var (key, resolution) in resolutions)
                {
                    callback(resolution, key.Name, key.Mode, filePath);
                }
            }
        }
    }

    /// <summary>Go: <c>func (p *Program) LineCount() int</c>（program.go:1690）。</summary>
    public int LineCount()
    {
        var count = 0;
        foreach (var file in _files)
        {
            count += file.ECMALineMap().Length;
        }
        return count;
    }

    /// <summary>Go: <c>func (p *Program) IdentifierCount() int</c>（program.go:1698）。</summary>
    public int IdentifierCount()
    {
        var count = 0;
        foreach (var file in _files)
        {
            count += file.IdentifierCount;
        }
        return count;
    }

    /// <summary>Go: <c>func (p *Program) SymbolCount() int</c>（program.go:1706）。</summary>
    public int SymbolCount()
    {
        var count = 0;
        foreach (var file in _files)
        {
            count += file.SymbolCount;
        }
        // Go: var val atomic.Uint32; val.Store(uint32(count)) —— C# 用单元素数组 + Interlocked
        var total = new int[1];
        total[0] = count;
        ForEachCheckerParallel((_, c) => Interlocked.Add(ref total[0], (int)c.SymbolCount));
        return Volatile.Read(ref total[0]);
    }

    /// <summary>Go: <c>func (p *Program) TypeCount() int</c>（program.go:1719）。</summary>
    public int TypeCount()
    {
        var total = new int[1];
        ForEachCheckerParallel((_, c) => Interlocked.Add(ref total[0], (int)c.TypeCount));
        return Volatile.Read(ref total[0]);
    }

    /// <summary>Go: <c>func (p *Program) InstantiationCount() int</c>（program.go:1727）。</summary>
    public int InstantiationCount()
    {
        var total = new int[1];
        ForEachCheckerParallel((_, c) => Interlocked.Add(ref total[0], (int)c.TotalInstantiationCount));
        return Volatile.Read(ref total[0]);
    }

    /// <summary>
    /// Go: <c>func (p *Program) Program() *Program</c>（program.go:1735，返回自身）。
    /// C# 不允许成员与所在类同名（CS0542）→ 落成 <c>ProgramSelf</c>；唯一的 Go 调用点（GetDiagnosticsOfAnyProgram）
    /// 改为走 <c>ProgramLike.ContentMapperDiagnostics()</c>。
    /// </summary>
    public Program ProgramSelf() => this;

    /// <summary>Go: <c>func (p *Program) GetSourceFileMetaData(path tspath.Path) ast.SourceFileMetaData</c>（program.go:1739）。</summary>
    public SourceFileMetaData GetSourceFileMetaData(Path path) => _sourceFileMetaDatas.GetValueOrDefault(path)!;

    /// <summary>Go: <c>func (p *Program) GetEmitModuleFormatOfFile(sourceFile ast.HasFileName) core.ModuleKind</c>（program.go:1743）。</summary>
    public ModuleKind GetEmitModuleFormatOfFile(HasFileName sourceFile) =>
        GetEmitModuleFormatOfFileWorker(sourceFile.FileName(), _projectReferenceFileMapper.GetCompilerOptionsForFile(sourceFile), GetSourceFileMetaData(sourceFile.Path()));

    /// <summary>Go: <c>func (p *Program) GetEmitSyntaxForUsageLocation(sourceFile, location *ast.StringLiteralLike) core.ResolutionMode</c>（program.go:1747）。</summary>
    public ModuleKind GetEmitSyntaxForUsageLocation(HasFileName sourceFile, Node location) =>
        Compiler.GetEmitSyntaxForUsageLocationWorker(sourceFile.FileName(), _sourceFileMetaDatas.GetValueOrDefault(sourceFile.Path())!, location, _projectReferenceFileMapper.GetCompilerOptionsForFile(sourceFile));

    /// <summary>Go: <c>func (p *Program) GetImpliedNodeFormatForEmit(sourceFile ast.HasFileName) core.ResolutionMode</c>（program.go:1751）。</summary>
    public ModuleKind GetImpliedNodeFormatForEmit(HasFileName sourceFile) =>
        GetImpliedNodeFormatForEmitWorker(sourceFile.FileName(), _projectReferenceFileMapper.GetCompilerOptionsForFile(sourceFile).GetEmitModuleKind(), GetSourceFileMetaData(sourceFile.Path()));

    /// <summary>Go: <c>func (p *Program) GetModeForUsageLocation(sourceFile, location *ast.StringLiteralLike) core.ResolutionMode</c>（program.go:1755）。</summary>
    public ModuleKind GetModeForUsageLocation(HasFileName sourceFile, Node location) =>
        Compiler.GetModeForUsageLocation(sourceFile.FileName(), _sourceFileMetaDatas.GetValueOrDefault(sourceFile.Path())!, location, _projectReferenceFileMapper.GetCompilerOptionsForFile(sourceFile));

    /// <summary>Go: <c>func (p *Program) GetModeForResolutionAtIndex(sourceFile *ast.SourceFile, index int) core.ResolutionMode</c>（program.go:1759）。</summary>
    public ModuleKind GetModeForResolutionAtIndex(SourceFile sourceFile, int index)
    {
        var imports = sourceFile.Imports();
        if (index < imports.Length)
        {
            return GetModeForUsageLocation(sourceFile, imports[index]);
        }
        index -= imports.Length;
        foreach (var augmentation in sourceFile.ModuleAugmentations ?? [])
        {
            if (augmentation.Kind == Kind.StringLiteral)
            {
                if (index == 0)
                {
                    return GetModeForUsageLocation(sourceFile, augmentation);
                }
                index--;
            }
        }
        Debug.Fail("resolution index out of range");
        return ModuleKind.None;
    }

    /// <summary>Go: <c>func (p *Program) GetDefaultResolutionModeForFile(sourceFile ast.HasFileName) core.ResolutionMode</c>（program.go:1776）。</summary>
    public ModuleKind GetDefaultResolutionModeForFile(HasFileName sourceFile) =>
        Compiler.GetDefaultResolutionModeForFile(sourceFile.FileName(), _sourceFileMetaDatas.GetValueOrDefault(sourceFile.Path())!, _projectReferenceFileMapper.GetCompilerOptionsForFile(sourceFile));

    /// <summary>Go: <c>func (p *Program) IsSourceFileDefaultLibrary(path tspath.Path) bool</c>（program.go:1780）。</summary>
    public bool IsSourceFileDefaultLibrary(Path path) => _libFiles.ContainsKey(path);

    /// <summary>Go: <c>func (p *Program) IsGlobalTypingsFile(fileName string) bool</c>（program.go:1785）。</summary>
    public bool IsGlobalTypingsFile(string fileName)
    {
        if (!IsDeclarationFileName(fileName))
        {
            return false;
        }
        return TypeScript.TSPath.TSPath.ContainsPath(GetGlobalTypingsCacheLocation(), fileName, _comparePathsOptions);
    }

    /// <summary>Go: <c>func (p *Program) GetDefaultLibFile(path tspath.Path) *LibFile</c>（program.go:1792）。</summary>
    public LibFile? GetDefaultLibFile(Path path) => _libFiles.GetValueOrDefault(path);

    /// <summary>Go: <c>func (p *Program) CommonSourceDirectory() string</c>（program.go:1799）。</summary>
    public string CommonSourceDirectory()
    {
        lock (_commonSourceDirectoryOnceLock)
        {
            if (!_commonSourceDirectoryComputed)
            {
                string[] Files() => MapFiltered(_files, file =>
                    (file.FileName(), Compiler.SourceFileMayBeEmitted(file, this, false /*forceDtsEmit*/, false /*forceJsEmit*/) && !file.IsDeclarationFile));
                _commonSourceDirectory = TypeScript.OutputPaths.OutputPaths.GetCommonSourceDirectory(
                    Options(),
                    Files,
                    GetCurrentDirectory(),
                    UseCaseSensitiveFileNames(),
                    CheckSourceFilesBelongToPath);
                _commonSourceDirectoryComputed = true;
            }
        }
        return _commonSourceDirectory;
    }

    /// <summary>Go: <c>func (p *Program) checkSourceFilesBelongToPath(sourceFiles []string, rootDirectory string) bool</c>（program.go:1817）。</summary>
    private bool CheckSourceFilesBelongToPath(string[] sourceFiles, string rootDirectory)
    {
        var allFilesBelongToPath = true;
        foreach (var file in sourceFiles)
        {
            var absoluteSourceFilePath = GetCanonicalFileName(GetNormalizedAbsolutePath(file, GetCurrentDirectory()), UseCaseSensitiveFileNames());
            if (!TypeScript.TSPath.TSPath.ContainsPath(rootDirectory, file, _comparePathsOptions))
            {
                _includeProcessor.AddProcessingDiagnostic(new ProcessingDiagnostic
                {
                    Kind = ProcessingDiagnosticKind.ExplainingFileInclude,
                    Data = new IncludeExplainingDiagnostic
                    {
                        File = (Path)absoluteSourceFilePath,
                        Message = File_0_is_not_under_rootDir_1_rootDir_is_expected_to_contain_all_source_files,
                        Args = [file, rootDirectory],
                    },
                });
                allFilesBelongToPath = false;
            }
        }

        return allFilesBelongToPath;
    }
}

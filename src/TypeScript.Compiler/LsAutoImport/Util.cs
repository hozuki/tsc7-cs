// ported from: internal/ls/autoimport/util.go
//
// 与 Go 的偏离（逐条）：
// 1) `context.Context` → `TypeScript.Core.Context`（§3.2 的既定映射，见 Core/Context.cs）。
// 2) `(T, bool)` → `bool TryX(..., out T)`；`panic` → `Debug.Fail`（其后补 `throw new UnreachableException()`，
//    因为 `Debug.Fail` 没有 `[DoesNotReturn]`）。
// 3) `unicode.ToUpper/ToLower/IsUpper/IsLower`（单码点 simple 映射）→ `Rune.ToUpperInvariant/...`（§3.4），
//    但 Go 用的是 **int 码点**（含代理项值），而 `new Rune(0xD800)` 会抛异常，所以统一走本文件的 `RuneUtil`
//    小工具（它先用 `Rune.IsValid` 挡住非标量值，语义与 Go 的"代理项不参与大小写映射"一致）。
// 4) Go 的 `utf8.DecodeRuneInString`/`DecodeLastRuneInString`（按**字节**解码 UTF-8）→ `RuneUtil` 里按
//    **UTF-16 代码单元**解码（§3.1）。`wordIndices` 返回的下标因此是代码单元下标（Go 是字节下标）：
//    两者的"单词切分"结果相同（分隔符/大小写判定都只看 ASCII 与码点），只有非 BMP 字符的下标数值不同，
//    而该下标只在本包内部用于 `name[start:]` 取后缀，不对外泄漏（不是 LSP 位置）。
// 5) `sync/atomic.Int32` + channel 的 checker 池 → `SemaphoreSlim` + `ConcurrentQueue<Checker>`
//    （§3.2：channel → Channel/同步原语）；`close(pool)` 无对应物，`ClosePool` 改成释放两个 semaphore
//    （Go 里 `close` 之后再用池会 panic，C# 里对应 `ObjectDisposedException`，语义一致）。
// 6) `GOMAXPROCS(0)` → `Environment.ProcessorCount`。
// 7) Go 的 `strings.CutPrefix` → `TryCutPrefix`（本文件的 C#-only 小工具，C# 没有直接等价物）。
// 8) `SymbolTable`/`Set`/`SyncMap` 等类型用 `using TypeScript.Collections;` 后的裸名：`Collections.Set`
//    这种限定写法在 `TypeScript.*` 命名空间里会先命中命名空间 `TypeScript.Collections`（§2 的坑）。
// 9) `Module.X`/`ModuleSpecifiers.X` 的包级函数用 `using static` 导入后按裸名调用（§2），因为裸 `Module.X`
//    会命中命名空间 `TypeScript.Module`。
using System.Collections.Concurrent;
using System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.Vfs;
using TypeScript.Vfs.WrapVfs;
using Path = TypeScript.TSPath.Path;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Module.Module;
using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.WrapVfs.WrapVfs;

namespace TypeScript.LsAutoImport;

/// <summary>C#-only 小工具：Go 的 <c>utf8.DecodeRuneInString</c>/<c>unicode.*</c> 在 **int 码点** 上的对应物。
/// （Go 的 rune 在 §3.1 里映射成 int；`Rune` 无法表示孤立代理项，所以这里显式挡住非标量值。）</summary>
internal static class RuneUtil
{
    /// <summary>Go 的 <c>utf8.RuneError</c>（U+FFFD）。</summary>
    internal const int RuneError = 0xFFFD;

    /// <summary>Go 的 <c>utf8.DecodeRuneInString(s)</c> 的第一个返回值（UTF-16 输入，§3.1）。</summary>
    internal static int DecodeRuneAt(string s, int index, out int size)
    {
        var rune = Rune.DecodeFromUtf16(s.AsSpan(index), out var value, out size);
        _ = rune;
        return value.Value;
    }

    /// <summary>Go 的 <c>utf8.DecodeLastRuneInString(s[:index])</c>：紧邻 <paramref name="index"/> 之前的那个码点。</summary>
    internal static int DecodeLastRuneAt(string s, int index)
    {
        if (index <= 0)
        {
            return RuneError;
        }
        if (index >= 2 && char.IsLowSurrogate(s[index - 1]) && char.IsHighSurrogate(s[index - 2]))
        {
            return char.ConvertToUtf32(s[index - 2], s[index - 1]);
        }
        return s[index - 1];
    }

    /// <summary>Go 的 <c>unicode.ToUpper(r)</c>（simple 映射；非标量值原样返回）。</summary>
    internal static int ToUpperRune(int ch) => Rune.IsValid(ch) ? Rune.ToUpperInvariant(new Rune(ch)).Value : ch;

    /// <summary>Go 的 <c>unicode.ToLower(r)</c>（simple 映射；非标量值原样返回）。</summary>
    internal static int ToLowerRune(int ch) => Rune.IsValid(ch) ? Rune.ToLowerInvariant(new Rune(ch)).Value : ch;

    /// <summary>Go 的 <c>unicode.IsUpper(r)</c>。</summary>
    internal static bool IsUpperRune(int ch) => Rune.IsValid(ch) && Rune.IsUpper(new Rune(ch));

    /// <summary>Go 的 <c>unicode.IsLower(r)</c>。</summary>
    internal static bool IsLowerRune(int ch) => Rune.IsValid(ch) && Rune.IsLower(new Rune(ch));

    /// <summary>Go 的 <c>strings.CutPrefix(s, prefix)</c>。</summary>
    internal static bool TryCutPrefix(string s, string prefix, out string after)
    {
        if (s.StartsWith(prefix, StringComparison.Ordinal))
        {
            after = s[prefix.Length..];
            return true;
        }
        after = s;
        return false;
    }
}

/// <summary>Go 的 <c>autoimport</c> 包（包级函数/常量放这里，见 docs/architecture.md §2）。</summary>
public static partial class AutoImport
{
    /// <summary>Go: <c>func tryGetModuleIDAndFileNameOfModuleSymbol(symbol *ast.Symbol) (ModuleID, string, bool)</c>（util.go:22-34）。</summary>
    internal static bool TryGetModuleIDAndFileNameOfModuleSymbol(Symbol symbol, out ModuleID moduleID, out string fileName)
    {
        moduleID = default;
        fileName = "";
        if (!symbol.IsExternalModule())
        {
            return false;
        }
        var decl = GetNonAugmentationDeclaration(symbol);
        if (decl is null)
        {
            return false;
        }
        if (decl.Kind == Kind.SourceFile)
        {
            var file = decl.AsSourceFile()!;
            moduleID = (ModuleID)(string)file.Path();
            fileName = file.FileName();
            return true;
        }
        if (IsModuleWithStringLiteralName(decl))
        {
            moduleID = (ModuleID)decl.Name()!.Text();
            fileName = "";
            return true;
        }
        return false;
    }

    /// <summary>Go: <c>func getModuleIDAndFileNameOfModuleSymbol(symbol *ast.Symbol) (ModuleID, string)</c>（util.go:36-49）。</summary>
    internal static (ModuleID ModuleID, string FileName) GetModuleIDAndFileNameOfModuleSymbol(Symbol symbol)
    {
        if (!symbol.IsExternalModule())
        {
            Debug.Fail("symbol is not an external module");
            throw new System.Diagnostics.UnreachableException();
        }
        var decl = GetNonAugmentationDeclaration(symbol);
        if (decl is null)
        {
            Debug.Fail("module symbol has no non-augmentation declaration");
            throw new System.Diagnostics.UnreachableException();
        }
        if (decl.Kind == Kind.SourceFile)
        {
            var file = decl.AsSourceFile()!;
            return ((ModuleID)(string)file.Path(), file.FileName());
        }
        if (IsModuleWithStringLiteralName(decl))
        {
            return ((ModuleID)decl.Name()!.Text(), "");
        }
        Debug.Fail("could not determine module ID of module symbol");
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Go: <c>func wordIndices(s string) []int</c>（util.go:51-79）：按 camelCase/snake_case 把标识符切成单词，
    /// 返回每个单词的起始下标（首项恒为 0）。</summary>
    internal static int[] WordIndices(string s)
    {
        List<int>? indices = null;
        for (var i = 0; i < s.Length;)
        {
            var index = i;
            var runeValue = RuneUtil.DecodeRuneAt(s, i, out var size);
            i += size;
            if (index == 0)
            {
                (indices ??= []).Add(index);
                continue;
            }
            if (runeValue == '_')
            {
                if (index + 1 < s.Length && s[index + 1] != '_')
                {
                    (indices ??= []).Add(index + 1);
                }
                continue;
            }
            if (RuneUtil.IsUpperRune(runeValue)
                && (RuneUtil.IsLowerRune(RuneUtil.DecodeLastRuneAt(s, index))
                    || (index + 1 < s.Length && RuneUtil.IsLowerRune(RuneUtil.DecodeRuneAt(s, index + 1, out _)))))
            {
                (indices ??= []).Add(index);
            }
        }
        return indices?.ToArray() ?? [];
    }

    /// <summary>Go: <c>func getPackageNamesInNodeModules(nodeModulesDir string, fs vfs.FS) *collections.Set[string]</c>（util.go:81-109）。</summary>
    internal static Set<string> GetPackageNamesInNodeModules(string nodeModulesDir, FS fs)
    {
        var packageNames = new Set<string>();
        if (GetBaseFileName(nodeModulesDir) != "node_modules")
        {
            Debug.Fail("nodeModulesDir is not a node_modules directory");
            throw new System.Diagnostics.UnreachableException();
        }
        // A missing node_modules directory yields no entries (GetAccessibleEntries returns
        // empty), so there's no need to check existence first: a deleted node_modules is
        // handled upstream in updateBucketAndDirectoryExistence, which drops the bucket.
        var entries = fs.GetAccessibleEntries(nodeModulesDir);
        foreach (var baseName in entries.Directories)
        {
            if (baseName[0] == '.')
            {
                continue;
            }
            if (baseName[0] == '@')
            {
                var scopedDirPath = CombinePaths(nodeModulesDir, baseName);
                foreach (var scopedPackageDirName in fs.GetAccessibleEntries(scopedDirPath).Directories)
                {
                    var scopedBaseName = GetBaseFileName(scopedPackageDirName);
                    if (baseName == "@types")
                    {
                        packageNames.Add(GetPackageNameFromTypesPackageName(CombinePaths("@types", scopedBaseName)));
                    }
                    else
                    {
                        packageNames.Add(CombinePaths(baseName, scopedBaseName));
                    }
                }
                continue;
            }
            packageNames.Add(baseName);
        }
        return packageNames;
    }

    /// <summary>Go: <c>func getDefaultLikeExportNameFromDeclaration(symbol *ast.Symbol) string</c>（util.go:111-136）。</summary>
    internal static string GetDefaultLikeExportNameFromDeclaration(Symbol symbol)
    {
        foreach (var d in symbol.Declarations ?? [])
        {
            // "export default" in this case. See `ExportAssignment`for more details.
            if (IsExportAssignment(d))
            {
                if (SkipOuterExpressions(d.Expression(), OuterExpressionKinds.All) is { } innerExpression && IsIdentifier(innerExpression))
                {
                    return innerExpression.Text();
                }
                continue;
            }
            // "export { ~ as default }"
            if (IsExportSpecifier(d) && d.Symbol!.Flags == SymbolFlags.Alias && d.PropertyName() is not null)
            {
                if (d.PropertyName()!.Kind == Kind.Identifier)
                {
                    return d.PropertyName()!.Text();
                }
                continue;
            }
            // GH#52694
            if (GetNameOfDeclaration(d) is { } name && name.Kind == Kind.Identifier)
            {
                return name.Text();
            }
            if (symbol.Parent is not null && !TypeScript.Checker.Checker.IsExternalModuleSymbol(symbol.Parent))
            {
                return symbol.Parent.Name;
            }
        }
        return "";
    }

    /// <summary>Go: <c>func getResolvedPackageNames(ctx context.Context, program *compiler.Program) *collections.Set[string]</c>（util.go:138-167）。</summary>
    internal static Set<string> GetResolvedPackageNames(Context ctx, TypeScript.Compiler.Program program)
    {
        var rawNames = program.ResolvedPackageNames() ?? new Set<string>();
        var unresolvedPackageNames = program.UnresolvedPackageNames() ?? new Set<string>();

        // Normalize @types/ package names to their actual package names
        // (e.g., "@types/react" → "react"). ResolvedPackageNames can contain
        // @types names when the program resolves an import like "react" to
        // "@types/react/index.d.ts" via the PackageId.Name field.
        var resolvedPackageNames = NewSetWithSizeHint<string>(rawNames.Len());
        foreach (var name in rawNames.Keys())
        {
            resolvedPackageNames.Add(GetPackageNameFromTypesPackageName(name));
        }

        foreach (var name in program.Options().Types ?? [])
        {
            if (name != "*")
            {
                resolvedPackageNames.Add(GetPackageNameFromTypesPackageName(name));
            }
        }

        if (unresolvedPackageNames.Len() > 0)
        {
            var (checker, done) = program.GetTypeChecker(ctx);
            try
            {
                foreach (var name in unresolvedPackageNames.Keys())
                {
                    if (checker.TryFindAmbientModule(name) is { } symbol)
                    {
                        var declaringFile = GetSourceFileOfModule(symbol);
                        if (declaringFile is not null && GetPackageNameFromDirectory(declaringFile.FileName()) is { Length: > 0 } packageName)
                        {
                            resolvedPackageNames.Add(GetPackageNameFromTypesPackageName(packageName));
                        }
                    }
                }
            }
            finally
            {
                done();
            }
        }
        return resolvedPackageNames;
    }

    /// <summary>Go: <c>func addProjectReferenceOutputMappings(program *compiler.Program, result map[tspath.Path]string)</c>（util.go:169-184）。</summary>
    internal static void AddProjectReferenceOutputMappings(TypeScript.Compiler.Program program, Dictionary<Path, string> result)
    {
        var refs = program.GetResolvedProjectReferences();
        foreach (var reference in refs)
        {
            if (reference is null)
            {
                continue;
            }
            reference.ParseInputOutputNames();
            var mappings = reference.OutputDtsToProjectReference();
            if (mappings is null)
            {
                continue;
            }
            foreach (var (outputDtsPath, mapping) in mappings)
            {
                // Only add if not already present (first program wins)
                if (!result.ContainsKey(outputDtsPath))
                {
                    result[outputDtsPath] = mapping.Source;
                }
            }
        }
    }

    /// <summary>Go: <c>func createCheckerPool(program checker.Program) (getChecker, closePool, getCreatedCount)</c>（util.go:186-216）。</summary>
    internal static (Func<(TypeScript.Checker.Checker Checker, Action Done)> GetChecker, Action ClosePool, Func<int> GetCreatedCount) CreateCheckerPool(TypeScript.Checker.Program program)
    {
        var maxSize = Environment.ProcessorCount;
        var pool = new ConcurrentQueue<TypeScript.Checker.Checker>();
        var created = 0;
        var available = new SemaphoreSlim(0);

        (TypeScript.Checker.Checker Checker, Action Done) GetChecker()
        {
            void Return(TypeScript.Checker.Checker checker)
            {
                pool.Enqueue(checker);
                available.Release();
            }

            // Try to get an existing checker
            if (available.Wait(0) && pool.TryDequeue(out var existing))
            {
                return (existing, () => Return(existing));
            }
            // Try to create a new one if under limit
            while (true)
            {
                var current = Volatile.Read(ref created);
                if (current >= maxSize)
                {
                    // At limit, wait for one to become available
                    available.Wait();
                    pool.TryDequeue(out var waiting);
                    var ready = waiting!;
                    return (ready, () => Return(ready));
                }
                if (Interlocked.CompareExchange(ref created, current + 1, current) == current)
                {
                    var checker = TypeScript.Checker.Checker.NewChecker(program, null).Checker;
                    return (checker, () => Return(checker));
                }
            }
        }

        // Go 的 close(pool) 只是关掉 channel，没有需要显式释放的资源；这里对应释放 semaphore
        // （之后再用池对应 Go 里"向已关闭 channel 发送"的 panic，C# 落成 ObjectDisposedException）。
        void ClosePool() => available.Dispose();

        int GetCreatedCount() => Volatile.Read(ref created);

        return (GetChecker, ClosePool, GetCreatedCount);
    }

    /// <summary>Go: <c>func addPackageJsonDependencies(contents *packagejson.PackageJson, deps *collections.Set[string])</c>（util.go:218-233）。</summary>
    internal static void AddPackageJsonDependencies(PackageJson.PackageJson contents, Set<string> deps)
    {
        contents.DependencyFields.RangeDependencies((name, _, field) =>
        {
            if (name == "" || name == "@types/" || name[0] == '.')
            {
                // Edge cases that could make us blow up probably
                return true;
            }
            if (field == "dependencies" || field == "peerDependencies")
            {
                deps.Add(GetPackageNameFromTypesPackageName(name));
            }
            return true;
        });
    }

    /// <summary>Go: <c>func getPackageRealpathFuncs(fs vfs.FS, packageDir string) (toRealpath, toSymlink func(string) string)</c>（util.go:235-290）。</summary>
    internal static (Func<string, string> ToRealpath, Func<string, string> ToSymlink) GetPackageRealpathFuncs(FS fs, string packageDir)
    {
        var realPackageDir = fs.Realpath(packageDir);
        var isSymlinked = realPackageDir != packageDir;
        // Cache of package-directory-level symlink→realpath prefix mappings for
        // external packages encountered via re-exports. Keyed by the node_modules
        // package directory (e.g. "/app/node_modules/dep"), so all files under
        // that package reuse a single realpath lookup.
        var dirCache = new Dictionary<string, string>();
        string ToRealpath(string fileName)
        {
            // Fast path: files within the package use prefix substitution.
            if (isSymlinked && RuneUtil.TryCutPrefix(fileName, packageDir, out var after))
            {
                return realPackageDir + after;
            }
            // Files outside the package (e.g. re-exports into symlinked deps):
            // find the node_modules package directory, resolve it once, and cache.
            var pkgDir = ParseNodeModuleFromPath(fileName, false /*isFolder*/);
            if (pkgDir == "")
            {
                return fileName;
            }
            if (dirCache.TryGetValue(pkgDir, out var cachedRealDir))
            {
                if (cachedRealDir == pkgDir)
                {
                    return fileName;
                }
                return cachedRealDir + fileName[pkgDir.Length..];
            }
            var realDir = fs.Realpath(pkgDir);
            dirCache[pkgDir] = realDir;
            if (realDir == pkgDir)
            {
                return fileName;
            }
            return realDir + fileName[pkgDir.Length..];
        }

        if (!isSymlinked)
        {
            return (ToRealpath, Identity);
        }
        // toSymlink only handles files within the package directory (reversing the
        // packageDir→realPackageDir substitution). It does not handle arbitrary external
        // paths; callers should only use it for files known to be within the package.
        string ToSymlink(string fileName)
        {
            if (RuneUtil.TryCutPrefix(fileName, realPackageDir, out var after))
            {
                return packageDir + after;
            }
            return fileName;
        }
        return (ToRealpath, ToSymlink);
    }

    /// <summary>Go: <c>func getModuleResolver(host RegistryCloneHost, realpath func(string) string, opts module.ResolverOptions) *module.DefaultResolver</c>（util.go:300-311）。</summary>
    internal static DefaultResolver GetModuleResolver(RegistryCloneHost host, Func<string, string> realpath, ResolverOptions opts)
    {
        var rh = new ResolutionHost
        {
            _fs = Wrap(host.FS(), new Replacements { Realpath = realpath }),
            _currentDirectory = host.GetCurrentDirectory(),
        };
        opts.Host = rh;
        opts.CompilerOptions = EmptyCompilerOptions;
        return NewResolver(opts);
    }
}

/// <summary>Go: <c>type resolutionHost struct { fs vfs.FS; currentDirectory string }</c>（util.go:293-298）。</summary>
internal sealed class ResolutionHost : TypeScript.Module.ResolutionHost
{
    internal FS _fs = null!;

    internal string _currentDirectory = "";

    /// <summary>Go: <c>func (rh *resolutionHost) GetCurrentDirectory() string</c>（util.go:300）。</summary>
    public string GetCurrentDirectory() => _currentDirectory;

    /// <summary>Go: <c>func (rh *resolutionHost) FS() vfs.FS</c>（util.go:304）。</summary>
    public FS FS() => _fs;
}

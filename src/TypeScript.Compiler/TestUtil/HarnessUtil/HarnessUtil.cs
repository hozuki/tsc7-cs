// ported from: internal/testutil/harnessutil/harnessutil.go
//
// 一个 Go package = 同名文件夹 + 同名命名空间；Go 的包级函数/包级 var 落在与包同名的静态类 `HarnessUtil`
// 上（docs/architecture.md §2）。本文件按 Go 的声明顺序 1:1 对应 harnessutil.go（1,273 行）；
// `recorderfs.go`/`sourcemap_recorder.go` 是对应的 RecorderFS.cs / SourceMapRecorder.cs。
//
// 逐条语义改写（Go → C#），与 docs/architecture.md §2/§3 一致：
// 1) `*testing.T` → `IHarnessTest`（TypeScript.TestUtil.HarnessContext.cs）：`t.Fatalf(...)` → `t.Fatal(...)`
//    （抛 TestFailureException，等价 Go 的 FailNow+Goexit），`t.Skipf(...)` → `t.Skip(...)`。**没有格式化重载**：
//    Go 的 `%s/%d/%v` 在调用点先插值成字符串，文本逐字对齐 Go。
// 2) `(T, error)` 没有出现（harnessutil 全靠 t.Fatalf 报错）；`panic(fmt.Sprintf(...))` → `Debug.Fail(...)`
//    （TypeScript.Debug.Debug，抛 DebugFailureException；见 Debug/Debug.cs，它在**根命名空间** TypeScript 里，
//    所以裸写 `Debug.Fail` 不会被 `TypeScript.Debug` 命名空间吃掉）。
// 3) `type TestConfiguration = map[string]string` 是**别名**（Go 里它就等于 map）：C# 无别名，本文件用
//    file-scoped 的 `using TestConfiguration = System.Collections.Generic.Dictionary<string, string>;` 保名，
//    编译出来的签名就是 `Dictionary<string, string>`。
// 4) Go 的 nil map / nil slice / nil 接口全部显式表达（本仓踩过最多的坑）：
//    * `map[string]any` 的 `testfs` 用 `Dictionary<string, object>`（非 nil）；
//    * `[]*TestFile` 的入参/出参用 `TestFile[]`，Go 的 nil slice → 空数组（Core.cs 文件头的约定）；
//    * `harnessOptions.LibFiles` 是 nil slice → `string[]`，判空用 `.Length > 0`；
//    * `tsconfig`/`testConfig`/`symlinks` 的 Go nil → C# `?`（可空引用）；
//    * `option.EnumMap()` 返回 Go 的 nil map → C# `?`，取值前必须判空（Go 的 `m[k]` 对 nil map 是安全的，
//      C# 的索引器不是）。
// 5) `strings.Builder` → `System.Text.StringBuilder`（TracerForBaselining 的构造器形参按 Go 的 `*strings.Builder`
//    保留为 `StringBuilder`，见 CreateCompilerHost）；`io.Writer` → `System.IO.TextWriter`
//    （TraceWithWriter；Go 的 io.Writer 是字节语义，但这里只被用来写文本行）。
// 6) `slices.SortedFunc(js.Values(), compareTestFiles)` → `List<T>.Sort`（同样的比较器）；`maps.Clone` →
//    `new Dictionary<...>(other)`；`maps.Copy(a, b)` → 逐项赋值；`slices.Collect(m.Keys())` → `[.. m.Keys()]`。
// 7) **C# 侧必需的一处形状补充**：Go 的 `c.Program.Program()` 在 `getOutputPath` 里被当作
//    `outputpaths.OutputPathsHost`（Go 是结构满足）。C# 的 `ProgramLike` 没有那 4 个成员，所以加了
//    `ProgramOutputPathsHost` 适配器：`ContentMapperExtensions()` 取同一份 config（Go 的
//    `Program.ContentMapperExtensions()` 就是 `p.opts.Config.ContentMapperExtensions()`），其余三项直接委托给
//    传入的 host/ProgramLike（Go 的 `Program.GetCurrentDirectory()/UseCaseSensitiveFileNames()` 也正是这样委托）。
// 8) 依赖（本波由并行 agent 落地，按 Go 名字调用；若尚未合入，构建期会报"缺失符号"）：
//    * `internal/repo` → `TypeScript.Repo.Repo`（`TestDataPath()`；RootPath 用 TSGO_ROOT）。
//    * `internal/testutil` 的 `TestProgramIsSingleThreaded()` → `TypeScript.TestUtil.TestUtil`。
//    * `internal/execute/incremental` → `TypeScript.Execute.Incremental.Incremental`（createProgram 的
//      incremental 分支 + testBuildInfoReader）。
//    * `internal/testutil/contentmappertest` → `TypeScript.TestUtil.ContentMapperTest.ContentMapperTest`
//      （CompileFilesEx 的 runExternalCode 分支）。
using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.OutputPaths;
using TypeScript.SourceMap;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using TypeScript.Vfs.VfsTest;
using Loc = TypeScript.Locale.Locale;
using IncrementalProgram = TypeScript.Execute.Incremental.Program;
using Diagnostic = TypeScript.Ast.Diagnostic;
using FS = TypeScript.Vfs.FS;
using Path = TypeScript.TSPath.Path;
using TestConfiguration = System.Collections.Generic.Dictionary<string, string>;
using static TypeScript.Ast.Ast;
using static TypeScript.Bundled.Bundled;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.OutputPaths.OutputPaths;
using static TypeScript.Repo.Repo;
using static TypeScript.SourceMap.SourceMap;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.TestUtil.HarnessUtil;

/// <summary>Go: <c>type TestFile struct { UnitName, Content string }</c>（harnessutil.go:45）。</summary>
public sealed class TestFile
{
    /// <summary>Go: <c>UnitName</c>。</summary>
    public string UnitName = "";

    /// <summary>Go: <c>Content</c>。</summary>
    public string Content = "";
}

/// <summary>Go: <c>type NamedTestConfiguration struct { Name string; Config TestConfiguration }</c>（harnessutil.go:59）。</summary>
public sealed class NamedTestConfiguration
{
    /// <summary>Go: <c>Name</c>。</summary>
    public string Name = "";

    /// <summary>Go: <c>Config</c>。Go 里可能是 nil map，但 GetFileBasedTestConfigurations 的返回永远非 nil。</summary>
    public TestConfiguration Config = new();
}

/// <summary>Go: <c>type HarnessOptions struct</c>（harnessutil.go:64）。字段顺序与 Go 一致。</summary>
public sealed class HarnessOptions
{
    /// <summary>Go: <c>UseCaseSensitiveFileNames</c>。</summary>
    public bool UseCaseSensitiveFileNames;

    /// <summary>Go: <c>BaselineFile</c>。</summary>
    public string BaselineFile = "";

    /// <summary>Go: <c>IncludeBuiltFile</c>。</summary>
    public string IncludeBuiltFile = "";

    /// <summary>Go: <c>FileName</c>。</summary>
    public string FileName = "";

    /// <summary>Go: <c>LibFiles []string</c>（nil slice → 空数组）。</summary>
    public string[] LibFiles = [];

    /// <summary>Go: <c>NoImplicitReferences</c>。</summary>
    public bool NoImplicitReferences;

    /// <summary>Go: <c>CurrentDirectory</c>。</summary>
    public string CurrentDirectory = "";

    /// <summary>Go: <c>Symlink</c>。</summary>
    public string Symlink = "";

    /// <summary>Go: <c>Link</c>。</summary>
    public string Link = "";

    /// <summary>Go: <c>NoTypesAndSymbols</c>。</summary>
    public bool NoTypesAndSymbols;

    /// <summary>Go: <c>FullEmitPaths</c>。</summary>
    public bool FullEmitPaths;

    /// <summary>Go: <c>ReportDiagnostics</c>。</summary>
    public bool ReportDiagnostics;

    /// <summary>Go: <c>CaptureSuggestions</c>。</summary>
    public bool CaptureSuggestions;

    /// <summary>Go: <c>TypescriptVersion</c>。</summary>
    public string TypescriptVersion = "";

    /// <summary>Go 里 <c>newHarnessOptions := *harnessOptions</c> 是结构体值拷贝（CompileFilesEx 的 Repeat 闭包用它，
    /// 免得 SetOptionsFromTestConfig 改到外层）。C# 的类是引用语义，这里给出等价的浅拷贝（数组字段共享，与 Go 的
    /// slice 头拷贝一致）。</summary>
    internal HarnessOptions Clone() => (HarnessOptions)MemberwiseClone();
}

public static partial class HarnessUtil
{
    /// <summary>Go: <c>const testLibFolder = "/.lib"</c>（harnessutil.go:41，Posix 风格路径，指向额外测试库）。</summary>
    internal const string TestLibFolder = "/.lib";

    /// <summary>Go: <c>const FakeTSVersion = "FakeTSVersion"</c>（harnessutil.go:43）。</summary>
    public const string FakeTSVersion = "FakeTSVersion";

    /// <summary>Go: <c>var sourceFileCache collections.SyncMap[SourceFileCacheKey, *ast.SourceFile]</c>（harnessutil.go:492）。</summary>
    internal static readonly SyncMap<SourceFileCacheKey, SourceFile> _sourceFileCache = new();

    /// <summary>Go: <c>func GetSourceFileCacheKey(opts ast.SourceFileParseOptions, text string, scriptKind core.ScriptKind) SourceFileCacheKey</c>（harnessutil.go:500）。</summary>
    public static SourceFileCacheKey GetSourceFileCacheKey(SourceFileParseOptions opts, string text, ScriptKind scriptKind) =>
        new(opts, text, scriptKind);

    /// <summary>Go: <c>func CompileFiles(...)</c>（harnessutil.go:81）。</summary>
    public static CompilationResult CompileFiles(
        IHarnessTest t,
        TestFile[] inputFiles,
        TestFile[] otherFiles,
        TestConfiguration? testConfig,
        ParsedCommandLine? tsconfig,
        string currentDirectory,
        Dictionary<string, string>? symlinks)
    {
        CompilerOptions? compilerOptions = null;
        if (tsconfig is not null)
        {
            compilerOptions = tsconfig.ParsedConfig.CompilerOptions?.Clone();
        }

        if (compilerOptions is null)
        {
            compilerOptions = new CompilerOptions();
        }

        // Set default options for tests
        if (compilerOptions.NewLine == NewLineKind.None)
        {
            compilerOptions.NewLine = NewLineKind.CRLF;
        }

        if (compilerOptions.SkipDefaultLibCheck == Tristate.TSUnknown)
        {
            compilerOptions.SkipDefaultLibCheck = Tristate.TSTrue;
        }

        compilerOptions.NoErrorTruncation = Tristate.TSTrue;
        var harnessOptions = new HarnessOptions { UseCaseSensitiveFileNames = true, CurrentDirectory = currentDirectory };

        // Parse harness and compiler options from the test configuration
        if (testConfig is not null)
        {
            SetOptionsFromTestConfig(t, testConfig, compilerOptions, harnessOptions, currentDirectory, false /*allowUnknownOptions*/);
        }

        return CompileFilesEx(t, inputFiles, otherFiles, harnessOptions, compilerOptions, currentDirectory, symlinks, tsconfig);
    }

    /// <summary>Go: <c>func CompileFilesEx(...) *CompilationResult</c>（harnessutil.go:115）。</summary>
    public static CompilationResult CompileFilesEx(
        IHarnessTest t,
        TestFile[] inputFiles,
        TestFile[] otherFiles,
        HarnessOptions harnessOptions,
        CompilerOptions compilerOptions,
        string currentDirectory,
        Dictionary<string, string>? symlinks,
        ParsedCommandLine? tsconfig)
    {
        var programFileNames = new List<string>();
        foreach (var file in inputFiles)
        {
            var fileName = GetNormalizedAbsolutePath(file.UnitName, currentDirectory);

            if (!FileExtensionIs(fileName, ExtensionJson) &&
                !FileExtensionIs(fileName, ExtensionTsBuildInfo))
            {
                programFileNames.Add(fileName);
            }
        }

        // !!! Note: lib files are not going to be in `built/local`.
        // In addition, not all files that used to be in `built/local` are going to exist.
        // Files from built\local that are requested by test "@includeBuiltFiles" to be in the context.
        // Treat them as library files, so include them in build, but not in baselines.
        // if harnessOptions.includeBuiltFile != "" {
        // 	programFileNames = append(programFileNames, tspath.CombinePaths(builtFolder, harnessOptions.includeBuiltFile))
        // }

        // Performance optimization; avoid copying in the /.lib folder if the test doesn't need it.
        var includeLibDir = Some(inputFiles, file => file.Content.Contains(TestLibFolder + "/", StringComparison.Ordinal));

        // Files from testdata\lib that are requested by "@libFiles"
        if (harnessOptions.LibFiles.Length > 0)
        {
            foreach (var libFile in harnessOptions.LibFiles)
            {
                if (libFile == "lib.d.ts" && compilerOptions.NoLib != Tristate.TSTrue)
                {
                    // We used to override lib with a custom lib.d.ts for some reason. Skip this unless it becomes necessary.
                    continue;
                }

                programFileNames.Add(CombinePaths(TestLibFolder, libFile));
                includeLibDir = true;
            }
        }

        // !!!
        // ts.assign(options, ts.convertToOptionsWithAbsolutePaths(options, path => ts.getNormalizedAbsolutePath(path, currentDirectory)));
        if (compilerOptions.OutDir != "")
        {
            compilerOptions.OutDir = GetNormalizedAbsolutePath(compilerOptions.OutDir, currentDirectory);
        }

        if (compilerOptions.Project != "")
        {
            compilerOptions.Project = GetNormalizedAbsolutePath(compilerOptions.Project, currentDirectory);
        }

        if (compilerOptions.RootDir != "")
        {
            compilerOptions.RootDir = GetNormalizedAbsolutePath(compilerOptions.RootDir, currentDirectory);
        }

        if (compilerOptions.TsBuildInfoFile != "")
        {
            compilerOptions.TsBuildInfoFile = GetNormalizedAbsolutePath(compilerOptions.TsBuildInfoFile, currentDirectory);
        }

        if (compilerOptions.BaseUrl != "")
        {
            compilerOptions.BaseUrl = GetNormalizedAbsolutePath(compilerOptions.BaseUrl, currentDirectory);
        }

        if (compilerOptions.DeclarationDir != "")
        {
            compilerOptions.DeclarationDir = GetNormalizedAbsolutePath(compilerOptions.DeclarationDir, currentDirectory);
        }

        // Go 的 `for i, rootDir := range compilerOptions.RootDirs` 对 nil slice 是空循环；C# 的 RootDirs 是 `string[]?`
        // （null = Go 的 nil），所以先判空再按下标改写。
        if (compilerOptions.RootDirs is { } rootDirs)
        {
            for (var i = 0; i < rootDirs.Length; i++)
            {
                rootDirs[i] = GetNormalizedAbsolutePath(rootDirs[i], currentDirectory);
            }
        }

        if (compilerOptions.TypeRoots is { } typeRoots)
        {
            for (var i = 0; i < typeRoots.Length; i++)
            {
                typeRoots[i] = GetNormalizedAbsolutePath(typeRoots[i], currentDirectory);
            }
        }

        Mapper[] contentMappers = [];
        if (tsconfig is not null && tsconfig.ParsedConfig is not null)
        {
            contentMappers = tsconfig.ParsedConfig.ContentMappers ?? [];
        }

        // Create fake FS for testing
        var testfs = new Dictionary<string, object>();
        foreach (var file in inputFiles)
        {
            var fileName = GetNormalizedAbsolutePath(file.UnitName, currentDirectory);
            testfs[fileName] = new MapFile { Data = file.Content };
        }

        foreach (var file in otherFiles)
        {
            var fileName = GetNormalizedAbsolutePath(file.UnitName, currentDirectory);
            testfs[fileName] = new MapFile { Data = file.Content };
        }

        if (symlinks is not null)
        {
            foreach (var (src, target) in symlinks)
            {
                var srcFileName = GetNormalizedAbsolutePath(src, currentDirectory);
                var targetFileName = GetNormalizedAbsolutePath(target, currentDirectory);
                testfs[srcFileName] = Symlink(targetFileName);
            }
        }

        if (includeLibDir)
        {
            foreach (var (path, file) in _testLibFolderMap.Value)
            {
                testfs[path] = file;
            }
        }

        var fs = FromMap(testfs, harnessOptions.UseCaseSensitiveFileNames);
        fs = WrapFS(fs);
        fs = NewOutputRecorderFS(fs);

        // Go 的两个 `defer …Close()`（LIFO：先 project 后 host）在这里落成 try/finally。
        ContentMapper.Host? contentMapperHost = null;
        ContentMapper.Project? contentMapperProject = null;
        try
        {
            // Content mappers, when trusted, are served in-process by the test mapper (see contentmappertest).
            // The host is shared by the pre- and post-emit programs and torn down when this compilation finishes.
            if (compilerOptions.RunExternalCode.IsTrue() && contentMappers.Length > 0)
            {
                contentMapperHost = NewHost(Context.Background, TypeScript.TestUtil.ContentMapperTest.ContentMapperTest.NewSpawner(), Loc.Default);
            }

            TsConfigSourceFile? configFile = null;
            Diagnostic[] errors = [];
            if (tsconfig is not null)
            {
                configFile = tsconfig.ConfigFile;
                errors = tsconfig.Errors;
            }

            var config = new ParsedCommandLine
            {
                ParsedConfig = new ParsedOptions
                {
                    CompilerOptions = compilerOptions,
                    FileNames = [.. programFileNames],
                    ContentMappers = contentMappers,
                },
                ConfigFile = configFile,
                Errors = errors,
            };

            if (contentMapperHost is not null)
            {
                contentMapperProject = contentMapperHost.Project(new ProjectSpec
                {
                    ConfigFileName = config.ConfigName(),
                    Mappers = config.ContentMappers() ?? [],
                    CompilerOptions = config.CompilerOptions(),
                });
            }

            var host = CreateCompilerHost(fs, LibPath(), currentDirectory, contentMapperProject);
            var result = CompileFilesWithHost(host, config, harnessOptions);
            result.Symlinks = symlinks ?? new Dictionary<string, string>();
            result.Trace = host.Tracer.String();
            result.Repeat = repeatTestConfig =>
            {
                var newHarnessOptions = harnessOptions.Clone();
                var newCompilerOptions = compilerOptions.Clone();
                SetOptionsFromTestConfig(t, repeatTestConfig, newCompilerOptions, newHarnessOptions, currentDirectory, false /*allowUnknownOptions*/);
                return CompileFilesEx(t, inputFiles, otherFiles, newHarnessOptions, newCompilerOptions, currentDirectory, symlinks, tsconfig);
            };
            return result;
        }
        finally
        {
            contentMapperProject?.Close();
            contentMapperHost?.Close();
        }
    }

    /// <summary>Go: <c>var testLibFolderMap = sync.OnceValue(func() map[string]any {...})</c>（harnessutil.go:266）。
    /// Go 把 OnceValue 的结果当"可调用值"用（<c>testLibFolderMap()</c>），C# 里就是 <c>_testLibFolderMap.Value</c>。</summary>
    private static readonly Lazy<Dictionary<string, object>> _testLibFolderMap = new(() =>
    {
        var testfs = new Dictionary<string, object>();
        var libRoot = System.IO.Path.Combine(TestDataPath(), "tests", "lib");
        try
        {
            WalkLibDir(libRoot, libRoot, testfs);
        }
        catch (Exception ex)
        {
            // Go: panic(fmt.Sprintf("Failed to read lib dir: %v", err))
            Debug.Fail($"Failed to read lib dir: {ex}");
        }

        return testfs;
    });

    /// <summary>Go 的 <c>fs.WalkDir(libfs, ".", …)</c>：不跟随符号链接的目录树遍历；key 与 Go 逐字一致
    /// （<c>testLibFolder + "/" + path</c>，path 是相对 lib 根的 slash 分隔路径）。</summary>
    private static void WalkLibDir(string root, string folder, Dictionary<string, object> testfs)
    {
        foreach (var entry in ReadDirEntries(folder))
        {
            var path = System.IO.Path.Combine(folder, entry.Name);
            if (entry.IsDirectory)
            {
                WalkLibDir(root, path, testfs);
                continue;
            }

            var relative = NormalizeSlashes(System.IO.Path.GetRelativePath(root, path));
            testfs[TestLibFolder + "/" + relative] = new MapFile
            {
                Data = TypeScript.Vfs.Internal.Internal.DecodeBytes(File.ReadAllBytes(path)),
            };
        }
    }

    /// <summary>Go 的 <c>os.ReadDir</c>：按文件名排序的目录项，<c>IsDir()</c> 为 false 的符号链接不当作目录
    /// （os.ReadDir 走 readdirent 的 d_type，WalkDir 也明确不跟随符号链接）。</summary>
    private static List<(string Name, bool IsDirectory)> ReadDirEntries(string folder)
    {
        var infos = new DirectoryInfo(folder).GetFileSystemInfos();
        var entries = new List<(string Name, bool IsDirectory)>(infos.Length);
        foreach (var info in infos)
        {
            var isDirectory = info is DirectoryInfo && (info.Attributes & FileAttributes.ReparsePoint) == 0;
            entries.Add((info.Name, isDirectory));
        }

        entries.Sort((a, b) => CompareStringsCaseSensitive(a.Name, b.Name));
        return entries;
    }

    /// <summary>Go: <c>func SetOptionsFromTestConfig(t, testConfig, compilerOptions, harnessOptions, currentDirectory, allowUnknownOptions)</c>（harnessutil.go:291）。</summary>
    public static void SetOptionsFromTestConfig(
        IHarnessTest t,
        TestConfiguration testConfig,
        CompilerOptions compilerOptions,
        HarnessOptions harnessOptions,
        string currentDirectory,
        bool allowUnknownOptions)
    {
        foreach (var (name, value) in testConfig)
        {
            if (name == "typescriptversion")
            {
                continue;
            }

            var commandLineOption = GetCommandLineOption(name);
            if (commandLineOption is not null)
            {
                var parsedValue = GetOptionValue(t, commandLineOption, value, currentDirectory);
                var errors = ParseCompilerOptions(commandLineOption.Name, parsedValue, compilerOptions);
                if (errors.Length > 0)
                {
                    t.Fatal($"Error parsing value '{value}' for compiler option '{commandLineOption.Name}'.");
                }

                continue;
            }

            var harnessOption = GetHarnessOption(name);
            if (harnessOption is not null)
            {
                var parsedValue = GetOptionValue(t, harnessOption, value, currentDirectory);
                ParseHarnessOption(t, harnessOption.Name, parsedValue, harnessOptions);
                continue;
            }

            if (!allowUnknownOptions)
            {
                t.Fatal($"Unknown compiler option '{name}'.");
            }
        }
    }

    /// <summary>Go: <c>var compilerOptions = core.Concatenate(tsoptions.OptionsDeclarations, []*CommandLineOption{…})</c>（harnessutil.go:318）。</summary>
    private static readonly CommandLineOption[] _compilerOptions = Concatenate(
        OptionsDeclarations,
        new CommandLineOption[]
        {
            new()
            {
                Name = "allowNonTsExtensions",
                Kind = CommandLineOptionKind.Boolean,
            },
            new()
            {
                Name = "noErrorTruncation",
                Kind = CommandLineOptionKind.Boolean,
            },
            new()
            {
                Name = "suppressOutputPathCheck",
                Kind = CommandLineOptionKind.Boolean,
            },
            new()
            {
                Name = "noCheck",
                Kind = CommandLineOptionKind.Boolean,
            },
        });

    /// <summary>Go: <c>var harnessCommandLineOptions = []*tsoptions.CommandLineOption{…}</c>（harnessutil.go:340）。</summary>
    private static readonly CommandLineOption[] _harnessCommandLineOptions =
    [
        new()
        {
            Name = "useCaseSensitiveFileNames",
            Kind = CommandLineOptionKind.Boolean,
        },
        new()
        {
            Name = "baselineFile",
            Kind = CommandLineOptionKind.String,
        },
        new()
        {
            Name = "includeBuiltFile",
            Kind = CommandLineOptionKind.String,
        },
        new()
        {
            Name = "fileName",
            Kind = CommandLineOptionKind.String,
        },
        new()
        {
            Name = "libFiles",
            Kind = CommandLineOptionKind.List,
        },
        new()
        {
            Name = "noImplicitReferences",
            Kind = CommandLineOptionKind.Boolean,
        },
        new()
        {
            Name = "currentDirectory",
            Kind = CommandLineOptionKind.String,
        },
        new()
        {
            Name = "symlink",
            Kind = CommandLineOptionKind.String,
        },
        new()
        {
            Name = "link",
            Kind = CommandLineOptionKind.String,
        },
        new()
        {
            Name = "noTypesAndSymbols",
            Kind = CommandLineOptionKind.Boolean,
        },
        // Emitted js baseline will print full paths for every output file
        new()
        {
            Name = "fullEmitPaths",
            Kind = CommandLineOptionKind.Boolean,
        },
        // used to enable error collection in `transpile` baselines
        new()
        {
            Name = "reportDiagnostics",
            Kind = CommandLineOptionKind.Boolean,
        },
        // Adds suggestion diagnostics to error baselines
        new()
        {
            Name = "captureSuggestions",
            Kind = CommandLineOptionKind.Boolean,
        },
    ];

    /// <summary>Go: <c>func getHarnessOption(name string) *tsoptions.CommandLineOption</c>（harnessutil.go:398）。</summary>
    private static CommandLineOption? GetHarnessOption(string name)
    {
        return Find(_harnessCommandLineOptions, option => EquateStringCaseInsensitive(option.Name, name));
    }

    /// <summary>Go: <c>func parseHarnessOption(t, key string, value any, harnessOptions *HarnessOptions)</c>（harnessutil.go:404）。
    /// Go 的 `value.(bool)`/`value.(string)`/`value.([]any)` 是**类型断言**（失败即 panic）：C# 用显式强转（失败抛
    /// InvalidCastException），语义同为"这里不可能失败"。</summary>
    private static void ParseHarnessOption(IHarnessTest t, string key, object? value, HarnessOptions harnessOptions)
    {
        switch (key)
        {
            case "useCaseSensitiveFileNames":
                harnessOptions.UseCaseSensitiveFileNames = (bool)value!;
                break;
            case "baselineFile":
                harnessOptions.BaselineFile = (string)value!;
                break;
            case "includeBuiltFile":
                harnessOptions.IncludeBuiltFile = (string)value!;
                break;
            case "fileName":
                harnessOptions.FileName = (string)value!;
                break;
            case "libFiles":
                {
                    var items = (object?[])value!;
                    var libFiles = new string[items.Length];
                    for (var i = 0; i < items.Length; i++)
                    {
                        libFiles[i] = (string)items[i]!;
                    }

                    harnessOptions.LibFiles = libFiles;
                    break;
                }

            case "noImplicitReferences":
                harnessOptions.NoImplicitReferences = (bool)value!;
                break;
            case "currentDirectory":
                harnessOptions.CurrentDirectory = (string)value!;
                break;
            case "symlink":
                harnessOptions.Symlink = (string)value!;
                break;
            case "link":
                harnessOptions.Link = (string)value!;
                break;
            case "noTypesAndSymbols":
                harnessOptions.NoTypesAndSymbols = (bool)value!;
                break;
            case "fullEmitPaths":
                harnessOptions.FullEmitPaths = (bool)value!;
                break;
            case "reportDiagnostics":
                harnessOptions.ReportDiagnostics = (bool)value!;
                break;
            case "captureSuggestions":
                harnessOptions.CaptureSuggestions = (bool)value!;
                break;
            case "typescriptVersion":
                harnessOptions.TypescriptVersion = (string)value!;
                break;
            default:
                t.Fatal($"Unknown harness option '{key}'.");
                break;
        }
    }

    /// <summary>Go: <c>func getOptionValue(t, option *tsoptions.CommandLineOption, value string, cwd string) tsoptions.CompilerOptionsValue</c>（harnessutil.go:442）。</summary>
    private static object? GetOptionValue(IHarnessTest t, CommandLineOption option, string value, string cwd)
    {
        switch (option.Kind)
        {
            case CommandLineOptionKind.String:
                if (option.IsFilePath)
                {
                    return GetNormalizedAbsolutePath(value, cwd);
                }

                return value;
            case CommandLineOptionKind.Number:
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var numVal))
                {
                    // Go: t.Fatalf("Value for option '%s' must be a number, got: %v", option.Name, value)
                    t.Fatal($"Value for option '{option.Name}' must be a number, got: {value}");
                }

                return numVal;
            case CommandLineOptionKind.Boolean:
                switch (ToLowerJS(value))
                {
                    case "true":
                        return true;
                    case "false":
                        return false;
                    default:
                        t.Fatal($"Value for option '{option.Name}' must be a boolean, got: {value}");
                        break;
                }

                break;
            case CommandLineOptionKind.Enum:
                {
                    var (enumVal, ok) = option.EnumMap()!.Get(ToLowerJS(value));
                    if (!ok)
                    {
                        // Go: t.Fatalf("Value for option '%s' must be one of %s, got: %v", option.Name, strings.Join(slices.Collect(option.EnumMap().Keys()), ","), value)
                        t.Fatal($"Value for option '{option.Name}' must be one of {string.Join(",", option.EnumMap()!.Keys())}, got: {value}");
                    }

                    return enumVal;
                }

            case CommandLineOptionKind.List:
            case CommandLineOptionKind.ListOrElement:
                {
                    var (listVal, errors) = ParseListTypeOption(option, value);
                    if (option.Elements()!.IsFilePath)
                    {
                        return Map(listVal, item => (object?)GetNormalizedAbsolutePath((string)item!, cwd));
                    }

                    if (errors.Length > 0)
                    {
                        t.Fatal($"Unknown value '{value}' for compiler option '{option.Name}'");
                    }

                    return listVal;
                }

            case CommandLineOptionKind.Object:
                t.Fatal($"Object type options like '{option.Name}' are not supported");
                break;
        }

        return null;
    }

    /// <summary>Go: <c>func EnumerateFiles(folder string, testRegex *regexp.Regexp, recursive bool) ([]string, error)</c>（harnessutil.go:990）。
    /// Go 的 error 在 C# 里抛异常（IO 失败）；路径基准是 <c>repo.TestDataPath()</c>。</summary>
    public static string[] EnumerateFiles(string folder, Regex? testRegex, bool recursive)
    {
        var files = ListFiles(folder, testRegex, recursive);
        return Map(files, NormalizeSlashes);
    }

    /// <summary>Go: <c>func listFiles(path string, spec *regexp.Regexp, recursive bool) ([]string, error)</c>（harnessutil.go:998）。</summary>
    private static string[] ListFiles(string path, Regex? spec, bool recursive) => ListFilesWorker(spec, recursive, path);

    /// <summary>Go: <c>func listFilesWorker(spec *regexp.Regexp, recursive bool, folder string) ([]string, error)</c>（harnessutil.go:1002）。</summary>
    private static string[] ListFilesWorker(Regex? spec, bool recursive, string folder)
    {
        folder = GetNormalizedAbsolutePath(folder, TestDataPath());
        var entries = ReadDirEntries(folder);
        var paths = new List<string>();
        foreach (var entry in entries)
        {
            var path = NormalizePath(System.IO.Path.Combine(folder, entry.Name));
            if (!entry.IsDirectory)
            {
                if (spec is null || spec.IsMatch(path))
                {
                    paths.Add(path);
                }
            }
            else if (recursive)
            {
                paths.AddRange(ListFilesWorker(spec, recursive, path));
            }
        }

        return [.. paths];
    }

    /// <summary>Go: <c>func getFileBasedTestConfigurationDescription(config TestConfiguration) string</c>（harnessutil.go:1026）。</summary>
    private static string GetFileBasedTestConfigurationDescription(TestConfiguration config)
    {
        var output = new StringBuilder();
        var keys = config.Keys.ToArray();
        Array.Sort(keys, CompareStringsCaseSensitive);
        for (var i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                output.Append(',');
            }

            output.Append($"{keys[i]}={ToLowerJS(config[keys[i]])}");
        }

        return output.ToString();
    }

    /// <summary>Go: <c>func GetFileBasedTestConfigurations(t, settings map[string]string, varyByOptions map[string]struct{}) []*NamedTestConfiguration</c>（harnessutil.go:1038）。</summary>
    public static List<NamedTestConfiguration> GetFileBasedTestConfigurations(
        IHarnessTest t,
        Dictionary<string, string> settings,
        HashSet<string> varyByOptions)
    {
        var optionEntries = new List<string[]>(); // Each element slice has the option name as the first element, and the values as the rest
        var variationCount = 1;
        var nonVaryingOptions = new Dictionary<string, string>();
        foreach (var (option, value) in settings)
        {
            if (varyByOptions.Contains(option))
            {
                var entries = SplitOptionValues(t, value, option);
                if (entries.Length > 1)
                {
                    variationCount *= entries.Length;
                    if (variationCount > 25)
                    {
                        t.Fatal("Provided test options exceeded the maximum number of variations");
                    }

                    optionEntries.Add([option, .. entries]);
                }
                else if (entries.Length == 1)
                {
                    nonVaryingOptions[option] = entries[0];
                }
            }
            else
            {
                // Variation is not supported for the option
                nonVaryingOptions[option] = value;
            }
        }

        var configurations = new List<NamedTestConfiguration>();
        if (optionEntries.Count > 0)
        {
            // Merge varying and non-varying options
            var varyingConfigurations = ComputeFileBasedTestConfigurationVariations(variationCount, [.. optionEntries]);
            foreach (var varyingConfig in varyingConfigurations)
            {
                var description = GetFileBasedTestConfigurationDescription(varyingConfig);
                foreach (var (key, value) in nonVaryingOptions)
                {
                    varyingConfig[key] = value;
                }

                configurations.Add(new NamedTestConfiguration { Name = description, Config = varyingConfig });
            }
        }
        else if (nonVaryingOptions.Count > 0)
        {
            // Only non-varying options
            configurations.Add(new NamedTestConfiguration { Name = "", Config = nonVaryingOptions });
        }

        return configurations;
    }

    /// <summary>
    /// Splits a string value into an array of strings, each corresponding to a unique value for the given option.
    /// Also handles the `*` value, which includes all possible values for the option, and exclusions using `-` or `!`.
    /// <code>
    /// splitOptionValues("esnext, es2015, es6", "target") =&gt; ["esnext", "es2015"]
    /// splitOptionValues("*", "strict") =&gt; ["true", "false"]
    /// splitOptionValues("*, -true", "strict") =&gt; ["false"]
    /// </code>
    /// Go: <c>func splitOptionValues(t, value string, option string) []string</c>（harnessutil.go:1085）。
    /// Go 的 nil slice → C# 空数组。
    /// </summary>
    private static string[] SplitOptionValues(IHarnessTest t, string value, string option)
    {
        if (value.Length == 0)
        {
            return [];
        }

        var star = false;
        var includes = new List<string>();
        var excludes = new List<string>();
        foreach (var raw in value.Split(','))
        {
            var s = TrimSpace(raw);
            if (s.Length == 0)
            {
                continue;
            }

            if (s == "*")
            {
                star = true;
            }
            else if (s.StartsWith('-') || s.StartsWith('!'))
            {
                excludes.Add(s[1..]);
            }
            else
            {
                includes.Add(s);
            }
        }

        if (includes.Count == 0 && !star && excludes.Count == 0)
        {
            return [];
        }

        // Dedupe the variations by their normalized values
        // Go: map[tsoptions.CompilerOptionsValue]string（键是 any，按动态类型+值比较；C# 用装箱的 object 键同语义）。
        var variations = new Dictionary<object, string>();

        // add (and deduplicate) all included entries
        foreach (var include in includes)
        {
            var includeValue = GetValueOfOptionString(t, option, include);
            if (!variations.ContainsKey(includeValue!))
            {
                variations[includeValue!] = include;
            }
        }

        var allValues = GetAllValuesForOption(option);
        if (star && allValues.Length > 0)
        {
            // add all entries
            foreach (var include in allValues)
            {
                var includeValue = GetValueOfOptionString(t, option, include);
                if (!variations.ContainsKey(includeValue!))
                {
                    variations[includeValue!] = include;
                }
            }
        }

        // remove all excluded entries
        foreach (var exclude in excludes)
        {
            var (excludeValue, ok) = TryGetValueOfOptionString(option, exclude);
            if (!ok)
            {
                // The excluded value is not recognized (e.g., a removed option like "es3").
                // Just skip it since there's nothing to remove.
                continue;
            }

            variations.Remove(excludeValue!);
        }

        if (variations.Count == 0)
        {
            // Go: panic(fmt.Sprintf("Variations in test option '@%s' resulted in an empty set.", option))
            Debug.Fail($"Variations in test option '@{option}' resulted in an empty set.");
        }

        return [.. variations.Values];
    }

    /// <summary>Go: <c>func getValueOfOptionString(t, option string, value string) tsoptions.CompilerOptionsValue</c>（harnessutil.go:1150）。</summary>
    private static object? GetValueOfOptionString(IHarnessTest t, string option, string value)
    {
        var (result, ok) = TryGetValueOfOptionString(option, value);
        if (!ok)
        {
            t.Fatal($"Unknown value '{value}' for option '{option}'");
        }

        return result;
    }

    /// <summary>Go: <c>func tryGetValueOfOptionString(option string, value string) (tsoptions.CompilerOptionsValue, bool)</c>（harnessutil.go:1158）。</summary>
    private static (object? Value, bool Ok) TryGetValueOfOptionString(string option, string value)
    {
        var optionDecl = GetCommandLineOption(option);
        if (optionDecl is null)
        {
            return (null, false);
        }

        switch (optionDecl.Kind)
        {
            case CommandLineOptionKind.Enum:
                {
                    var (enumVal, ok) = optionDecl.EnumMap()!.Get(ToLowerJS(value));
                    if (!ok)
                    {
                        return (null, false);
                    }

                    return (enumVal, true);
                }

            case CommandLineOptionKind.Boolean:
                switch (ToLowerJS(value))
                {
                    case "true":
                        return (true, true);
                    case "false":
                        return (false, true);
                }

                return (null, false);
        }

        return (value, true);
    }

    /// <summary>Go: <c>func getCommandLineOption(option string) *tsoptions.CommandLineOption</c>（harnessutil.go:1182）。</summary>
    private static CommandLineOption? GetCommandLineOption(string option)
    {
        return Find(_compilerOptions, optionDecl => EquateStringCaseInsensitive(optionDecl.Name, option));
    }

    /// <summary>Go: <c>func getAllValuesForOption(option string) []string</c>（harnessutil.go:1188）。</summary>
    private static string[] GetAllValuesForOption(string option)
    {
        var optionDecl = GetCommandLineOption(option);
        if (optionDecl is null)
        {
            return [];
        }

        switch (optionDecl.Kind)
        {
            case CommandLineOptionKind.Enum:
                return [.. optionDecl.EnumMap()!.Keys()];
            case CommandLineOptionKind.Boolean:
                return ["true", "false"];
        }

        return [];
    }

    /// <summary>Go: <c>func computeFileBasedTestConfigurationVariations(variationCount int, optionEntries [][]string) []TestConfiguration</c>（harnessutil.go:1202）。</summary>
    private static List<TestConfiguration> ComputeFileBasedTestConfigurationVariations(int variationCount, string[][] optionEntries)
    {
        var configurations = new List<TestConfiguration>(variationCount);
        ComputeFileBasedTestConfigurationVariationsWorker(configurations, optionEntries, 0, new Dictionary<string, string>());
        return configurations;
    }

    /// <summary>Go: <c>func computeFileBasedTestConfigurationVariationsWorker(configurations *[]TestConfiguration, optionEntries [][]string, index int, variationState TestConfiguration)</c>（harnessutil.go:1208）。</summary>
    private static void ComputeFileBasedTestConfigurationVariationsWorker(
        List<TestConfiguration> configurations,
        string[][] optionEntries,
        int index,
        TestConfiguration variationState)
    {
        if (index >= optionEntries.Length)
        {
            // Go: maps.Clone(variationState)
            configurations.Add(new Dictionary<string, string>(variationState));
            return;
        }

        var optionKey = optionEntries[index][0];
        var entries = optionEntries[index][1..];
        foreach (var entry in entries)
        {
            // set or overwrite the variation, then compute the next variation
            variationState[optionKey] = entry;
            ComputeFileBasedTestConfigurationVariationsWorker(configurations, optionEntries, index + 1, variationState);
        }
    }

    /// <summary>
    /// Go: <c>func GetConfigNameFromFileName(filename string) string</c>（harnessutil.go:1228）。
    /// 只把 `tsconfig.json` / `jsconfig.json`（比较时小写化）识别为配置文件，其余返回空串。
    /// </summary>
    public static string GetConfigNameFromFileName(string filename)
    {
        // Go: basenameLower := strings.ToLower(tspath.GetBaseFileName(filename))
        var basenameLower = ToLowerJS(GetBaseFileName(filename));
        if (basenameLower == "tsconfig.json" || basenameLower == "jsconfig.json")
        {
            return basenameLower;
        }

        return "";
    }

    /// <summary>Go: <c>func SkipUnsupportedCompilerOptions(t, options *core.CompilerOptions)</c>（harnessutil.go:1236）。</summary>
    public static void SkipUnsupportedCompilerOptions(IHarnessTest t, CompilerOptions options)
    {
        // Go: t.Helper()
        FailOnUnsupportedCompilerOptions(t, options);
        switch (options.Module)
        {
            case ModuleKind.UMD or ModuleKind.System:
                t.Skip($"unsupported module kind {options.Module.String()}");
                break;
        }

        switch (options.ModuleResolution)
        {
            case ModuleResolutionKind.Node10 or ModuleResolutionKind.Classic:
                t.Skip($"unsupported module resolution kind {(int)options.ModuleResolution}");
                break;
        }

        if (options.ESModuleInterop.IsFalse())
        {
            t.Skip("esModuleInterop=false is unsupported");
        }

        if (options.AllowSyntheticDefaultImports.IsFalse())
        {
            t.Skip("allowSyntheticDefaultImports=false is unsupported");
        }

        if (options.BaseUrl != "")
        {
            t.Skip($"unsupported baseUrl {options.BaseUrl}");
        }

        switch (options.Target)
        {
            case ScriptTarget.ES5:
                t.Skip($"unsupported target {options.Target.String()}");
                break;
        }

        if (options.AlwaysStrict.IsFalse())
        {
            t.Skip("alwaysStrict=false is unsupported");
        }
    }

    /// <summary>Go: <c>func failOnUnsupportedCompilerOptions(t, options *core.CompilerOptions)</c>（harnessutil.go:1265）。</summary>
    private static void FailOnUnsupportedCompilerOptions(IHarnessTest t, CompilerOptions options)
    {
        // Go: t.Helper()
        if (options.Module == ModuleKind.AMD)
        {
            t.Fatal($"unsupported module kind {options.Module.String()}");
        }

        if (options.OutFile != "")
        {
            t.Fatal($"unsupported outFile {options.OutFile}");
        }
    }

    /// <summary>Go: <c>func createCompilerHost(fs vfs.FS, defaultLibraryPath, currentDirectory string, contentMapperProject contentmapper.Project) *cachedCompilerHost</c>（harnessutil.go:615）。
    /// Go 的 `&amp;strings.Builder{}` 用 <see cref="StringWriter"/> 表达（同为"可 ToString 的文本接收器"）。</summary>
    private static CachedCompilerHost CreateCompilerHost(
        FS fs,
        string defaultLibraryPath,
        string currentDirectory,
        ContentMapper.Project? contentMapperProject)
    {
        var tracer = new TracerForBaselining(
            new ComparePathsOptions(fs.UseCaseSensitiveFileNames(), currentDirectory),
            new StringWriter());
        return new CachedCompilerHost(
            TypeScript.Compiler.Compiler.NewCompilerHost(currentDirectory, fs, defaultLibraryPath, null, tracer.Trace, contentMapperProject),
            tracer);
    }

    /// <summary>Go: <c>func compileFilesWithHost(host compiler.CompilerHost, config *tsoptions.ParsedCommandLine, harnessOptions *HarnessOptions) *CompilationResult</c>（harnessutil.go:626）。</summary>
    private static CompilationResult CompileFilesWithHost(CompilerHost host, ParsedCommandLine config, HarnessOptions harnessOptions)
    {
        // !!!
        // if (compilerOptions.project || !rootFiles || rootFiles.length === 0) {
        // 	const project = readProject(host.parseConfigHost, compilerOptions.project, compilerOptions);
        // 	if (project) {
        // 		if (project.errors && project.errors.length > 0) {
        // 			return new CompilationResult(host, compilerOptions, /*program*/ undefined, /*result*/ undefined, project.errors);
        // 		}
        // 		if (project.config) {
        // 			rootFiles = project.config.fileNames;
        // 			compilerOptions = project.config.options;
        // 		}
        // 	}
        // 	delete compilerOptions.project;
        // }

        var ctx = Context.Background;

        var preErrors = new List<Diagnostic>();
        var preCompilerOptions = config.CompilerOptions()!.Clone();
        preCompilerOptions.TraceResolution = Tristate.TSFalse;
        var preConfig = new ParsedCommandLine
        {
            ParsedConfig = new ParsedOptions
            {
                CompilerOptions = preCompilerOptions,
                FileNames = config.FileNames(),
                ContentMappers = config.ContentMappers(),
            },
            ConfigFile = config.ConfigFile,
            Errors = config.Errors,
        };
        var preProgram = CreateProgram(host, preConfig);
        preErrors.AddRange(preProgram.GetConfigFileParsingDiagnostics());
        preErrors.AddRange(preProgram.GetProgramDiagnostics());
        preErrors.AddRange(preProgram.GetSyntacticDiagnostics(ctx, null));
        preErrors.AddRange(preProgram.GetSemanticDiagnostics(ctx, null));
        preErrors.AddRange(preProgram.GetGlobalDiagnostics(ctx));
        if (preProgram.Options().GetEmitDeclarations())
        {
            preErrors.AddRange(preProgram.GetDeclarationDiagnostics(ctx, null));
        }

        if (harnessOptions.CaptureSuggestions)
        {
            preErrors.AddRange(preProgram.GetSuggestionDiagnostics(ctx, null));
        }

        var preErrorsSorted = TypeScript.Compiler.Compiler.SortAndDeduplicateDiagnostics([.. preErrors]);

        var postProgram = CreateProgram(host, config);
        var emitResult = postProgram.Emit(ctx, new EmitOptions());
        var postErrors = new List<Diagnostic>();
        postErrors.AddRange(postProgram.GetConfigFileParsingDiagnostics());
        postErrors.AddRange(postProgram.GetProgramDiagnostics());
        postErrors.AddRange(postProgram.GetSyntacticDiagnostics(ctx, null));
        postErrors.AddRange(postProgram.GetSemanticDiagnostics(ctx, null));
        postErrors.AddRange(postProgram.GetGlobalDiagnostics(ctx));
        if (postProgram.Options().GetEmitDeclarations())
        {
            postErrors.AddRange(postProgram.GetDeclarationDiagnostics(ctx, null));
        }

        if (harnessOptions.CaptureSuggestions)
        {
            postErrors.AddRange(postProgram.GetSuggestionDiagnostics(ctx, null));
        }

        var postErrorsSorted = TypeScript.Compiler.Compiler.SortAndDeduplicateDiagnostics([.. postErrors]);

        var errors = postErrorsSorted;
        if (postErrorsSorted.Length != preErrorsSorted.Length)
        {
            var longerErrors = postErrorsSorted;
            var shorterErrors = preErrorsSorted;
            if (preErrorsSorted.Length > postErrorsSorted.Length)
            {
                longerErrors = preErrorsSorted;
                shorterErrors = postErrorsSorted;
            }

            var diag = NewCompilerDiagnostic(
                NewAdHocMessage($"Pre-emit ({preErrorsSorted.Length}) and post-emit ({postErrorsSorted.Length}) diagnostic counts do not match! This can indicate that a semantic _error_ was added by the emit resolver - such an error may not be reflected on the command line or in the editor, but may be captured in a baseline here!"));
            diag = diag.AddRelatedInfo(NewCompilerDiagnostic(NewAdHocMessage("The excess diagnostics are:")));
            foreach (var d in longerErrors)
            {
                var matched = false;
                foreach (var d2 in shorterErrors)
                {
                    var comparison = CompareDiagnostics(d, d2);
                    if (comparison == 0)
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    diag = diag.AddRelatedInfo(d);
                }
            }

            errors = shorterErrors;
            errors = [.. errors, diag];
        }

        return NewCompilationResult(host, config.CompilerOptions()!, postProgram, emitResult, errors, harnessOptions, config);
    }

    /// <summary>Go: <c>func compareTestFiles(a, b *TestFile) int</c>（harnessutil.go:832）。</summary>
    private static int CompareTestFiles(TestFile a, TestFile b) => CompareStringsCaseSensitive(a.UnitName, b.UnitName);

    /// <summary>Go: <c>func newCompilationResult(...) *CompilationResult</c>（harnessutil.go:746）。
    /// C# 侧多收一个 <paramref name="config"/>：Go 的 <c>c.Program.Program()</c> 在 getOutputPath 里被当作
    /// OutputPathsHost 用（见文件头第 7 条）。</summary>
    private static CompilationResult NewCompilationResult(
        CompilerHost host,
        CompilerOptions options,
        ProgramLike? program,
        EmitResult? result,
        Diagnostic[] diagnostics,
        HarnessOptions harnessOptions,
        ParsedCommandLine config)
    {
        if (program is not null)
        {
            options = program.Options();
        }

        var c = new CompilationResult
        {
            Diagnostics = diagnostics,
            Result = result,
            Program = program!,
            Options = options,
            HarnessOptions = harnessOptions,
            Host = host,
            _outputPathsHost = new ProgramOutputPathsHost(program!, config, host),
        };

        var fs = (OutputRecorderFS)host.FS();
        if (fs is not null && program is not null)
        {
            // Corsa, unlike Strada, can use multiple threads for emit. As a result, the order of outputs is non-deterministic.
            // To make the order deterministic, we sort the outputs by the order of the inputs.
            var js = new OrderedMap<string, TestFile>();
            var dts = new OrderedMap<string, TestFile>();
            var maps = new OrderedMap<string, TestFile>();
            foreach (var document in fs.Outputs())
            {
                if (HasJSFileExtension(document.UnitName) ||
                    HasJSONFileExtension(document.UnitName))
                {
                    js.Set(document.UnitName, document);
                }
                else if (IsDeclarationFileName(document.UnitName))
                {
                    dts.Set(document.UnitName, document);
                }
                else if (FileExtensionIs(document.UnitName, ".map"))
                {
                    maps.Set(document.UnitName, document);
                }
            }

            // using the order from the inputs, populate the outputs
            foreach (var sourceFile in program.GetSourceFiles())
            {
                var input = new TestFile { UnitName = sourceFile.FileName(), Content = sourceFile.Text() };
                c._inputs = [.. c._inputs, input];
                if (!IsDeclarationFileName(sourceFile.FileName()))
                {
                    var extname = GetOutputExtension(sourceFile.FileName(), options.Jsx);
                    var outputs = new CompilationOutput
                    {
                        Inputs = [input],
                        JS = js.GetOrZero(c.GetOutputPath(sourceFile.FileName(), extname)),
                        DTS = dts.GetOrZero(c.GetOutputPath(sourceFile.FileName(), GetDeclarationEmitExtensionForPath(sourceFile.FileName()))),
                        Map = maps.GetOrZero(c.GetOutputPath(sourceFile.FileName(), extname + ".map")),
                    };
                    c._inputsAndOutputs.Set(sourceFile.FileName(), outputs);
                    if (outputs.JS is not null)
                    {
                        c._inputsAndOutputs.Set(outputs.JS.UnitName, outputs);
                        c.JS.Set(outputs.JS.UnitName, outputs.JS);
                        js.Delete(outputs.JS.UnitName);
                        c._outputs = [.. c._outputs, outputs.JS];
                    }

                    if (outputs.DTS is not null)
                    {
                        c._inputsAndOutputs.Set(outputs.DTS.UnitName, outputs);
                        c.DTS.Set(outputs.DTS.UnitName, outputs.DTS);
                        dts.Delete(outputs.DTS.UnitName);
                        c._outputs = [.. c._outputs, outputs.DTS];
                    }

                    if (outputs.Map is not null)
                    {
                        c._inputsAndOutputs.Set(outputs.Map.UnitName, outputs);
                        c.Maps.Set(outputs.Map.UnitName, outputs.Map);
                        maps.Delete(outputs.Map.UnitName);
                        c._outputs = [.. c._outputs, outputs.Map];
                    }
                }
            }

            // add any unhandled outputs, ordered by unit name
            foreach (var document in SortedByUnitName(js.Values()))
            {
                c.JS.Set(document.UnitName, document);
            }

            foreach (var document in SortedByUnitName(dts.Values()))
            {
                c.DTS.Set(document.UnitName, document);
            }

            foreach (var document in SortedByUnitName(maps.Values()))
            {
                c.Maps.Set(document.UnitName, document);
            }
        }

        return c;
    }

    /// <summary>Go 的 <c>slices.SortedFunc(m.Values(), compareTestFiles)</c>。</summary>
    private static List<TestFile> SortedByUnitName(IEnumerable<TestFile?> values)
    {
        var list = new List<TestFile>();
        foreach (var value in values)
        {
            if (value is not null)
            {
                list.Add(value);
            }
        }

        list.Sort(CompareTestFiles);
        return list;
    }

    /// <summary>Go: <c>func getTestBuildInfoReader(host compiler.CompilerHost) *testBuildInfoReader</c>（harnessutil.go:966）。</summary>
    private static TestBuildInfoReader GetTestBuildInfoReader(CompilerHost host)
    {
        return new TestBuildInfoReader(NewBuildInfoReader(host));
    }

    /// <summary>Go: <c>func createProgram(host compiler.CompilerHost, config *tsoptions.ParsedCommandLine) compiler.ProgramLike</c>（harnessutil.go:970）。</summary>
    private static ProgramLike CreateProgram(CompilerHost host, ParsedCommandLine config)
    {
        var singleThreaded = Tristate.TSUnknown;
        if (TypeScript.TestUtil.TestUtil.TestProgramIsSingleThreaded())
        {
            singleThreaded = Tristate.TSTrue;
        }

        var programOptions = new ProgramOptions
        {
            Config = config,
            Host = host,
            SingleThreaded = singleThreaded,
        };
        var program = TypeScript.Compiler.Compiler.NewProgram(programOptions);
        if (config.CompilerOptions()!.Incremental.IsTrue())
        {
            var oldProgram = ReadBuildInfoProgram(config, GetTestBuildInfoReader(host), host);
            var incrementalProgram = IncrementalProgram.NewProgram(program, oldProgram, CreateHost(host), null, false);
            return incrementalProgram;
        }

        return program;
    }
}

/// <summary>Go: <c>type testBuildInfoReader struct { inner incremental.BuildInfoReader }</c>（harnessutil.go:953）。</summary>
internal sealed class TestBuildInfoReader(TypeScript.Execute.Incremental.BuildInfoReader inner) : TypeScript.Execute.Incremental.BuildInfoReader
{
    internal readonly TypeScript.Execute.Incremental.BuildInfoReader _inner = inner;

    /// <summary>Go: <c>func (t *testBuildInfoReader) ReadBuildInfo(config *tsoptions.ParsedCommandLine) *incremental.BuildInfo</c>（harnessutil.go:957）。</summary>
    public TypeScript.Execute.Incremental.BuildInfo? ReadBuildInfo(ParsedCommandLine config)
    {
        var r = _inner.ReadBuildInfo(config);
        if (r is null)
        {
            return null;
        }

        r.Version = Version();
        return r;
    }
}

/// <summary>Go: <c>type cachedCompilerHost struct { compiler.CompilerHost; tracer *TracerForBaselining }</c>（harnessutil.go:487）。
/// Go 靠嵌入接口自动转发其余方法，C# 没有嵌入，逐个显式委托（<see cref="GetSourceFile"/> 是唯一改写的方法）。</summary>
internal sealed class CachedCompilerHost : CompilerHost
{
    private readonly CompilerHost _host;

    internal readonly TracerForBaselining Tracer;

    internal CachedCompilerHost(CompilerHost host, TracerForBaselining tracer)
    {
        _host = host;
        Tracer = tracer;
    }

    public FS FS() => _host.FS();

    public string DefaultLibraryPath() => _host.DefaultLibraryPath();

    public string GetCurrentDirectory() => _host.GetCurrentDirectory();

    public void Trace(Message msg, params object?[] args) => _host.Trace(msg, args);

    /// <summary>Go: <c>func (h *cachedCompilerHost) GetSourceFile(opts ast.SourceFileParseOptions) *ast.SourceFile</c>（harnessutil.go:508）。</summary>
    public SourceFile? GetSourceFile(SourceFileParseOptions opts)
    {
        if (!FS().TryReadFile(opts.FileName, out var text))
        {
            return null;
        }

        var scriptKind = GetScriptKindFromFileName(opts.FileName);
        if (scriptKind == ScriptKind.Unknown)
        {
            // Go: panic("Unknown script kind for file  " + opts.FileName)
            Debug.Fail("Unknown script kind for file  " + opts.FileName);
        }

        var key = HarnessUtil.GetSourceFileCacheKey(opts, text, scriptKind);

        var (cached, ok) = HarnessUtil._sourceFileCache.Load(key);
        if (ok)
        {
            return cached;
        }

        var sourceFile = TypeScript.Parser.Parser.ParseSourceFile(opts, text, scriptKind);
        var (result, _) = HarnessUtil._sourceFileCache.LoadOrStore(key, sourceFile);
        return result;
    }

    public SourceFiles GetContentMappedSourceFiles(SourceFileParseOptions parseOptions, Mapper mapper) =>
        _host.GetContentMappedSourceFiles(parseOptions, mapper);

    public ContentMapper.Project? ContentMapperProject() => _host.ContentMapperProject();

    public ParsedCommandLine? GetResolvedProjectReference(string fileName, Path path) =>
        _host.GetResolvedProjectReference(fileName, path);
}

/// <summary>
/// Go: <c>type SourceFileCacheKey struct</c>（harnessutil.go:494）；它的构造由
/// Go: <c>func GetSourceFileCacheKey(opts ast.SourceFileParseOptions, text string, scriptKind core.ScriptKind) SourceFileCacheKey</c>
/// （harnessutil.go:500）完成，见 <see cref="HarnessUtil.GetSourceFileCacheKey"/>。
/// </summary>
public readonly struct SourceFileCacheKey : IEquatable<SourceFileCacheKey>
{
    private readonly string _fileName;
    private readonly Path _path;
    private readonly bool _jsx;
    private readonly bool _force;
    private readonly string _text;
    private readonly ScriptKind _scriptKind;

    /// <summary>Go 的 <c>SourceFileCacheKey{opts, text, scriptKind}</c>。
    /// **偏离**：Go 的 <c>ast.SourceFileParseOptions</c> 是值结构体（含值结构体 ExternalModuleIndicatorOptions），
    /// map 键比较逐字段做值比较；C# 里两个都是 class（引用比较），所以这里把字段**拆开存**并做逐字段比较，
    /// 保证键等价性与 Go 一致（否则同一文件会被解析成多个 SourceFile 实例，program 里的身份比较就会变）。</summary>
    internal SourceFileCacheKey(SourceFileParseOptions opts, string text, ScriptKind scriptKind)
    {
        _fileName = opts.FileName;
        _path = opts.Path;
        _jsx = opts.ExternalModuleIndicatorOptions.JSX;
        _force = opts.ExternalModuleIndicatorOptions.Force;
        _text = text;
        _scriptKind = scriptKind;
    }

    public bool Equals(SourceFileCacheKey other) =>
        _fileName == other._fileName &&
        _path == other._path &&
        _jsx == other._jsx &&
        _force == other._force &&
        _text == other._text &&
        _scriptKind == other._scriptKind;

    public override bool Equals(object? obj) => obj is SourceFileCacheKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_fileName, _path, _jsx, _force, _text, _scriptKind);
}

/// <summary>Go: <c>type TracerForBaselining struct</c>（harnessutil.go:530）。</summary>
public sealed class TracerForBaselining
{
    private readonly ComparePathsOptions _opts;
    private Dictionary<Path, bool> _packageJsonCache;
    private readonly TextWriter _builder;

    internal TracerForBaselining(ComparePathsOptions opts, TextWriter builder)
    {
        _opts = opts;
        _packageJsonCache = new Dictionary<Path, bool>();
        _builder = builder;
    }

    /// <summary>Go: <c>func NewTracerForBaselining(opts tspath.ComparePathsOptions, builder *strings.Builder) *TracerForBaselining</c>（harnessutil.go:536）。
    /// C# 取 Go 的 <c>*strings.Builder</c>（<see cref="TextWriter"/>：Go 侧本来就是当 io.Writer 用的）。</summary>
    public static TracerForBaselining NewTracerForBaselining(ComparePathsOptions opts, TextWriter builder) => new(opts, builder);

    /// <summary>Go: <c>func (t *TracerForBaselining) Trace(msg *diagnostics.Message, args ...any)</c>（harnessutil.go:544）。</summary>
    public void Trace(Message msg, params object?[] args)
    {
        TraceWithWriter(_builder, msg.Localize(Loc.Default, args), true);
    }

    /// <summary>Go: <c>func (t *TracerForBaselining) TraceWithWriter(w io.Writer, msg string, usePackageJsonCache bool)</c>（harnessutil.go:548）。
    /// Go 的 <c>fmt.Fprintln</c> 写的是 "\n"（不是 CRLF），这里照样写两个 Write。</summary>
    public void TraceWithWriter(TextWriter w, string msg, bool usePackageJsonCache)
    {
        w.Write(SanitizeTrace(msg, usePackageJsonCache));
        w.Write('\n');
    }

    /// <summary>Go: <c>func (t *TracerForBaselining) sanitizeTrace(msg string, usePackageJsonCache bool) string</c>（harnessutil.go:552）。</summary>
    internal string SanitizeTrace(string msg, bool usePackageJsonCache)
    {
        // Version
        var replaced = ReplaceFirst(msg, "'" + Version() + "'", "'" + HarnessUtil.FakeTSVersion + "'");
        if (replaced != msg)
        {
            return replaced;
        }

        // caching of fs in trace to be replaces with non caching version
        if (CutSuffix(msg, "' does not exist according to earlier cached lookups.", out var str))
        {
            var file = CutPrefix(str, "File '");
            if (usePackageJsonCache)
            {
                var filePath = ToPath(file, _opts.CurrentDirectory, _opts.UseCaseSensitiveFileNames);
                if (_packageJsonCache.ContainsKey(filePath))
                {
                    return msg;
                }

                _packageJsonCache[filePath] = false;
            }

            return $"File '{file}' does not exist.";
        }

        if (CutSuffix(msg, "' exists according to earlier cached lookups.", out str))
        {
            var file = CutPrefix(str, "File '");
            if (usePackageJsonCache)
            {
                var filePath = ToPath(file, _opts.CurrentDirectory, _opts.UseCaseSensitiveFileNames);
                if (_packageJsonCache.ContainsKey(filePath))
                {
                    return msg;
                }

                _packageJsonCache[filePath] = true;
            }

            return $"Found 'package.json' at '{file}'.";
        }

        if (usePackageJsonCache)
        {
            if (CutSuffix(msg, "' does not exist.", out str))
            {
                var file = CutPrefix(str, "File '");
                var filePath = ToPath(file, _opts.CurrentDirectory, _opts.UseCaseSensitiveFileNames);
                if (!_packageJsonCache.ContainsKey(filePath))
                {
                    _packageJsonCache[filePath] = false;
                    return msg;
                }

                return $"File '{file}' does not exist according to earlier cached lookups.";
            }

            if (CutPrefix(msg, "Found 'package.json' at '", out str))
            {
                var file = CutSuffix(str, "'.");
                var filePath = ToPath(file, _opts.CurrentDirectory, _opts.UseCaseSensitiveFileNames);
                if (!_packageJsonCache.ContainsKey(filePath))
                {
                    _packageJsonCache[filePath] = true;
                    return msg;
                }

                return $"File '{file}' exists according to earlier cached lookups.";
            }
        }

        return msg;
    }

    /// <summary>Go: <c>func (t *TracerForBaselining) String() string</c>（harnessutil.go:607）。</summary>
    public string String() => _builder.ToString() ?? "";

    /// <inheritdoc/>
    public override string ToString() => String();

    /// <summary>Go: <c>func (t *TracerForBaselining) Reset()</c>（harnessutil.go:611）。</summary>
    public void Reset()
    {
        _packageJsonCache = new Dictionary<Path, bool>();
    }

    /// <summary>Go: <c>strings.Replace(s, old, new, 1)</c>（只替换第一处，.NET 的 string.Replace 会全替换）。</summary>
    private static string ReplaceFirst(string s, string oldValue, string newValue)
    {
        var index = s.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0 ? s : s[..index] + newValue + s[(index + oldValue.Length)..];
    }

    /// <summary>Go: <c>strings.CutSuffix</c>。</summary>
    internal static bool CutSuffix(string s, string suffix, out string rest)
    {
        if (s.EndsWith(suffix, StringComparison.Ordinal))
        {
            rest = s[..^suffix.Length];
            return true;
        }

        rest = "";
        return false;
    }

    /// <summary>Go: <c>strings.CutSuffix</c>（丢弃 suffix）。</summary>
    private static string CutSuffix(string s, string suffix) => CutSuffix(s, suffix, out var rest) ? rest : s;

    /// <summary>Go: <c>strings.CutPrefix</c>。</summary>
    private static bool CutPrefix(string s, string prefix, out string rest)
    {
        if (s.StartsWith(prefix, StringComparison.Ordinal))
        {
            rest = s[prefix.Length..];
            return true;
        }

        rest = "";
        return false;
    }

    /// <summary>Go: <c>strings.CutPrefix</c>（丢弃 prefix）。</summary>
    private static string CutPrefix(string s, string prefix) => CutPrefix(s, prefix, out var rest) ? rest : s;
}

/// <summary>Go: <c>type CompilationResult struct</c>（harnessutil.go:721）。</summary>
public sealed class CompilationResult
{
    /// <summary>Go: <c>Diagnostics []*ast.Diagnostic</c>。</summary>
    public Diagnostic[] Diagnostics = [];

    /// <summary>Go: <c>Result *compiler.EmitResult</c>（Go 里可为 nil）。</summary>
    public EmitResult? Result;

    /// <summary>Go: <c>Program compiler.ProgramLike</c>。</summary>
    public ProgramLike Program = null!;

    /// <summary>Go: <c>Options *core.CompilerOptions</c>。</summary>
    public CompilerOptions Options = null!;

    /// <summary>Go: <c>HarnessOptions *HarnessOptions</c>。</summary>
    public HarnessOptions HarnessOptions = null!;

    /// <summary>Go: <c>JS collections.OrderedMap[string, *TestFile]</c>（迭代顺序 = 插入顺序）。</summary>
    public OrderedMap<string, TestFile> JS = new();

    /// <summary>Go: <c>DTS collections.OrderedMap[string, *TestFile]</c>。</summary>
    public OrderedMap<string, TestFile> DTS = new();

    /// <summary>Go: <c>Maps collections.OrderedMap[string, *TestFile]</c>。</summary>
    public OrderedMap<string, TestFile> Maps = new();

    /// <summary>Go: <c>Symlinks map[string]string</c>（Go 可能是 nil map，这里统一非 null 空字典）。</summary>
    public Dictionary<string, string> Symlinks = new();

    /// <summary>Go: <c>Repeat func(TestConfiguration) *CompilationResult</c>（函数字段 → 委托字段）。</summary>
    public Func<TestConfiguration, CompilationResult>? Repeat;

    /// <summary>Go: <c>outputs []*TestFile</c>（未导出字段，经 <see cref="Outputs"/> 读）。</summary>
    internal TestFile[] _outputs = [];

    /// <summary>Go: <c>inputs []*TestFile</c>（未导出字段，经 <see cref="Inputs"/> 读）。</summary>
    internal TestFile[] _inputs = [];

    /// <summary>Go: <c>inputsAndOutputs collections.OrderedMap[string, *CompilationOutput]</c>。</summary>
    internal readonly OrderedMap<string, CompilationOutput> _inputsAndOutputs = new();

    /// <summary>Go 的 <c>c.Program.Program()</c>（在 getOutputPath 里被当作 outputpaths.OutputPathsHost 用）。</summary>
    internal OutputPathsHost? _outputPathsHost;

    /// <summary>Go: <c>Trace string</c>。</summary>
    public string Trace = "";

    /// <summary>Go: <c>Host compiler.CompilerHost</c>。</summary>
    public CompilerHost Host = null!;

    /// <summary>Go: <c>func (c *CompilationResult) getOutputPath(path string, ext string) string</c>（harnessutil.go:836）。</summary>
    internal string GetOutputPath(string path, string ext)
    {
        path = ResolvePath(Host.GetCurrentDirectory(), path);
        string outDir;
        if (ext == ".d.ts" || ext == ".d.mts" || ext == ".d.cts" || (ext.EndsWith(".ts", StringComparison.Ordinal) && ext.Contains(".d.", StringComparison.Ordinal)))
        {
            outDir = Options.DeclarationDir;
            if (outDir == "")
            {
                outDir = Options.OutDir;
            }
        }
        else
        {
            outDir = Options.OutDir;
        }

        if (outDir != "")
        {
            var common = Program.CommonSourceDirectory();
            if (common != "")
            {
                path = GetRelativePathFromDirectory(common, path, new ComparePathsOptions(
                    Host.FS().UseCaseSensitiveFileNames(),
                    Host.GetCurrentDirectory()));
                path = CombinePaths(ResolvePath(Host.GetCurrentDirectory(), Options.OutDir), path);
            }
        }

        if (ext == GetDeclarationEmitExtensionForPath(path))
        {
            return ChangeToDeclarationExtension(path, _outputPathsHost!);
        }

        return ChangeExtension(path, ext);
    }

    /// <summary>Go: <c>func (r *CompilationResult) FS() vfs.FS</c>（harnessutil.go:863）。</summary>
    public FS FS() => Host.FS();

    /// <summary>Go: <c>func (r *CompilationResult) GetNumberOfJSFiles(includeJson bool) int</c>（harnessutil.go:867）。</summary>
    public int GetNumberOfJSFiles(bool includeJson)
    {
        if (includeJson)
        {
            return JS.Size();
        }

        var count = 0;
        foreach (var file in JS.Values())
        {
            if (!FileExtensionIs(file!.UnitName, ExtensionJson))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Go: <c>func (c *CompilationResult) Inputs() []*TestFile</c>（harnessutil.go:880）。</summary>
    public TestFile[] Inputs() => _inputs;

    /// <summary>Go: <c>func (c *CompilationResult) Outputs() []*TestFile</c>（harnessutil.go:884）。</summary>
    public TestFile[] Outputs() => _outputs;

    /// <summary>Go: <c>func (c *CompilationResult) GetInputsAndOutputsForFile(path string) *CompilationOutput</c>（harnessutil.go:888）。</summary>
    public CompilationOutput? GetInputsAndOutputsForFile(string path) =>
        _inputsAndOutputs.GetOrZero(ResolvePath(Host.GetCurrentDirectory(), path));

    /// <summary>Go: <c>func (c *CompilationResult) GetInputsForFile(path string) []*TestFile</c>（harnessutil.go:892）。</summary>
    public TestFile[]? GetInputsForFile(string path)
    {
        var outputs = GetInputsAndOutputsForFile(path);
        if (outputs is not null)
        {
            return outputs.Inputs;
        }

        return null;
    }

    /// <summary>Go: <c>func (c *CompilationResult) GetOutput(path string, kind string /*"js" | "dts" | "map"*/) *TestFile</c>（harnessutil.go:900）。</summary>
    public TestFile? GetOutput(string path, string kind)
    {
        var outputs = GetInputsAndOutputsForFile(path);
        if (outputs is not null)
        {
            switch (kind)
            {
                case "js":
                    return outputs.JS;
                case "dts":
                    return outputs.DTS;
                case "map":
                    return outputs.Map;
            }
        }

        return null;
    }

    /// <summary>Go: <c>func (c *CompilationResult) GetSourceMapRecord() string</c>（harnessutil.go:915）。</summary>
    public string GetSourceMapRecord()
    {
        if (Result is null || Result.SourceMaps.Length == 0)
        {
            return "";
        }

        var sourceMapRecorder = new WriterAggregator();
        foreach (var sourceMapData in Result.SourceMaps)
        {
            SourceFile? prevSourceFile = null;
            TestFile? currentFile;

            if (IsDeclarationFileName(sourceMapData.GeneratedFile))
            {
                currentFile = DTS.GetOrZero(sourceMapData.GeneratedFile);
            }
            else
            {
                currentFile = JS.GetOrZero(sourceMapData.GeneratedFile);
            }

            var sourceMapSpanWriter = new SourceMapSpanWriter(sourceMapRecorder, sourceMapData.SourceMap!, currentFile);
            var mapper = TypeScript.SourceMap.SourceMap.DecodeMappings(sourceMapData.SourceMap!.Mappings);
            foreach (var decodedSourceMapping in mapper.Values())
            {
                if (!decodedSourceMapping.IsSourceMapping())
                {
                    sourceMapSpanWriter.RecordSourceMapSpan(decodedSourceMapping);
                    continue;
                }

                var currentSourceFile = Program.GetSourceFile(sourceMapData.InputSourceFileNames[(int)decodedSourceMapping.SourceIndex]);
                if (!ReferenceEquals(currentSourceFile, prevSourceFile))
                {
                    if (currentSourceFile is not null)
                    {
                        sourceMapSpanWriter.RecordNewSourceFileSpan(decodedSourceMapping, currentSourceFile.OriginalText());
                    }

                    prevSourceFile = currentSourceFile;
                }
                else
                {
                    sourceMapSpanWriter.RecordSourceMapSpan(decodedSourceMapping);
                }
            }

            sourceMapSpanWriter.Close();
        }

        return sourceMapRecorder.ToString();
    }
}

/// <summary>Go: <c>type CompilationOutput struct</c>（harnessutil.go:739）。</summary>
public sealed class CompilationOutput
{
    /// <summary>Go: <c>Inputs []*TestFile</c>。</summary>
    public TestFile[] Inputs = [];

    /// <summary>Go: <c>JS *TestFile</c>。</summary>
    public TestFile? JS;

    /// <summary>Go: <c>DTS *TestFile</c>。</summary>
    public TestFile? DTS;

    /// <summary>Go: <c>Map *TestFile</c>。</summary>
    public TestFile? Map;
}

/// <summary>
/// Go 的 <c>c.Program.Program()</c> 在 <c>outputpaths.ChangeToDeclarationExtension(path, host)</c> 里被当作
/// <c>OutputPathsHost</c>（Go 靠结构满足）。C# 的 <see cref="ProgramLike"/> 没声明这 4 个成员，用本适配器表达
/// 同一事实：<c>ContentMapperExtensions()</c> 取同一份 config（Go 的
/// <c>Program.ContentMapperExtensions()</c> 就是 <c>p.opts.Config.ContentMapperExtensions()</c>），其余三项与
/// Go 的 <c>Program.GetCurrentDirectory()/UseCaseSensitiveFileNames()/CommonSourceDirectory()</c> 逐字一致。
/// </summary>
internal sealed class ProgramOutputPathsHost(ProgramLike program, ParsedCommandLine config, CompilerHost host) : OutputPathsHost
{
    public string CommonSourceDirectory() => program.CommonSourceDirectory();

    public string[] ContentMapperExtensions() => config.ContentMapperExtensions();

    public string GetCurrentDirectory() => host.GetCurrentDirectory();

    public bool UseCaseSensitiveFileNames() => host.FS().UseCaseSensitiveFileNames();
}

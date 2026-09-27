// ported from: internal/testutil/projecttestutil/projecttestutil.go:1-320
//
// 这是所有 project 测试共用的脚手架（Go 的 `projecttestutil.Setup(files)` → `(*project.Session, *SessionUtils)`）。
//
// 与 Go 的差异（逐条）：
//
// 1) 包级函数与常量按 docs/architecture.md §2 收进与包同名的静态类 `ProjectTestUtil`（`TestTypingsLocation`、
//    `Setup*`、`WithRequestID`、`GetSessionInitOptions`、`TypesRegistryConfig*`）。
// 2) **文件树的表达**：Go 的 `files map[string]any` 是**扁平**的 map（键 = 规范化绝对路径，值 = 文件内容：
//    `string` / `[]byte` / `*fstest.MapFile`，见 vfs/vfstest.FromMap），C# 对应
//    `Dictionary<string, object?>`（值 = `string` / `byte[]` / `VfsTest.MapFile`），直接喂给
//    `VfsTest.FromMap(Dictionary<string, object>, bool)`。**本移植新增**一个超集：值为
//    `Dictionary<string, object?>` 时按**子目录**展开（键前面拼上父路径 + "/"；键以 '/' 开头则是绝对路径，
//    不再拼父路径）。展开在 `Flatten` 里做，`Setup*` 的全部重载都接受这种写法。Go 侧没有这个能力，
//    写扁平与嵌套两种写法都能 1:1 对应到 Go（嵌套写法展开后就是 Go 的扁平 map）。
// 3) Go 的双返回值 `(*project.Session, *SessionUtils)` → C# 命名元组 `(Session Session, SessionUtils Utils)`。
// 4) `error` → 异常（§3.2）：`FS().WriteFile(...)` 失败直接抛（C# 的 `vfs.FS.WriteFile` 是 void）；
//    `SetupNpmExecutorForTypingsInstaller` 里的错误路径抛 `Exception`，错误文本与 Go 的 `fmt.Errorf` 一致
//    （`%v` 打 `[]string` 的 `[a b c]` 形状见 `FormatStringSlice`）。
// 5) `context.Context` → `TypeScript.Core.Context`（`Context.Background`）；`core.WithRequestID(ctx, "0")` →
//    `Context.WithRequestID`（Core/Context.cs 文件头已把 Go 的 `context.WithValue(ctx, requestIDKey, …)`
//    映射到它，签名与 Go 一致）。
// 6) `tspath.Path` → `TypeScript.TSPath.Path`；`tspath.ToPath`/`EnsureTrailingDirectorySeparator` →
//    `using static TypeScript.TSPath.TSPath;` 后的裸名（与 Go 一致）。
// 7) `testing.T` → `TypeScript.TestUtil.IHarnessTest`（Baseline/Baseline.cs 文件头第 1 条）；`baseline.Run` →
//    `Baseline.Run`，`baseline.Options` → `BaselineOptions`。
// 8) 包级 `sync.Once` 单例（`typesRegistryConfig`/`typesRegistryConfigText`）→ `Lazy<T>`（默认
//    ExecutionAndPublication 与 `sync.Once` 同为"只算一次 + 并发安全"）。
// 9) **`TypesRegistryConfig()` 的顺序**：Go 的 map 迭代顺序是随机的（`TypesRegistryConfigText()` 与
//    `createTypesRegistryFileContent` 里的 `PackageToFile` 也一样），C# 的 `Dictionary` 迭代保持插入顺序
//    ⇒ 这里生成的 JSON 文本是**确定的**（Go 的每次都不一样）。这是本移植的收紧，不改变语义。
// 10) `os.Getwd()` → `Directory.GetCurrentDirectory()`（失败时 .NET 自己抛，Go 的 panic 分支不移植）。
// 11) 未导出成员按 C# 习惯命名（字段 `_camelCase`、方法 PascalCase）；Go 导出成员原名保留。
// 12) `iovfs.FsWithSys` → `TypeScript.Vfs.Iovfs.FsWithSys`（Go 的目录是 `vfs/iovfs` ⇒ 命名空间是
//     **`Iovfs`**、不是 `IoVfs`）；`vfs.FS` → `TypeScript.Vfs.FS`。`VfsTest.FromMap` 的实际返回对象实现了
//     `FsWithSys`（IoFs.cs:59 的 `Iovfs.From`），与 Go 的 `vfs.FS` 断言成 `iovfs.FsWithSys` 一致。
namespace TypeScript.Tests.ProjectTestUtil;

using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.TestUtil;
using TypeScript.TestUtil.Baseline;
using TypeScript.Vfs;
using TypeScript.Vfs.Iovfs;
using TypeScript.Vfs.VfsTest;

using static TypeScript.Bundled.Bundled;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.OsVfs.Osvfs;
using static TypeScript.Vfs.VfsTest.VfsTest;

using Logging = TypeScript.Project.Logging.Logging;
using TSPathType = TypeScript.TSPath.Path;

/// <summary>Go 的 <c>projecttestutil</c> 包级常量/函数所在的静态类（见文件头第 1 条）。</summary>
public static class ProjectTestUtil
{
    /// <summary>Go: <c>const TestTypingsLocation = "/home/src/Library/Caches/typescript"</c>。</summary>
    public const string TestTypingsLocation = "/home/src/Library/Caches/typescript";

    /// <summary>Go: <c>func Setup(files map[string]any) (*project.Session, *SessionUtils)</c>。</summary>
    public static (Session Session, SessionUtils Utils) Setup(Dictionary<string, object?> files) =>
        SetupWithTypingsInstaller(files, new TypingsInstallerOptions());

    /// <summary>Go: <c>func SetupWithOptions(files map[string]any, options *project.SessionOptions)</c>。</summary>
    public static (Session Session, SessionUtils Utils) SetupWithOptions(Dictionary<string, object?> files, SessionOptions options) =>
        SetupWithOptionsAndTypingsInstaller(files, options, new TypingsInstallerOptions());

    /// <summary>Go: <c>func SetupWithTypingsInstaller(files map[string]any, tiOptions *TypingsInstallerOptions)</c>。</summary>
    public static (Session Session, SessionUtils Utils) SetupWithTypingsInstaller(Dictionary<string, object?> files, TypingsInstallerOptions tiOptions) =>
        SetupWithOptionsAndTypingsInstaller(files, null, tiOptions);

    /// <summary>Go: <c>func SetupWithOptionsAndTypingsInstaller(files, options, tiOptions)</c>。</summary>
    public static (Session Session, SessionUtils Utils) SetupWithOptionsAndTypingsInstaller(
        Dictionary<string, object?> files,
        SessionOptions? options,
        TypingsInstallerOptions tiOptions)
    {
        var (init, sessionUtils) = GetSessionInitOptions(files, options, tiOptions);
        var session = new Session(init);
        return (session, sessionUtils);
    }

    /// <summary>Go: <c>func SetupWithRealFS() (*project.Session, *SessionUtils)</c>（用真实的 os FS 与工作目录）。</summary>
    public static (Session Session, SessionUtils Utils) SetupWithRealFS()
    {
        var fs = WrapFS(FS());
        var clientMock = new ClientMock();
        var npmExecutorMock = new NpmExecutorMock();
        var wd = Directory.GetCurrentDirectory();

        var sessionUtils = new SessionUtils(
            currentDirectory: wd,
            fsFromFileMap: null,
            fs: fs,
            client: clientMock,
            npmExecutor: npmExecutorMock,
            tiOptions: null,
            logger: Logging.NewTestLogger());

        var session = new Session(new SessionInit
        {
            BackgroundCtx = Context.Background,
            FS = fs,
            Client = clientMock,
            NpmExecutor = npmExecutorMock,
            Logger = sessionUtils._logger,
            Options = new SessionOptions
            {
                CurrentDirectory = wd,
                DefaultLibraryPath = LibPath(),
                PositionEncoding = PositionEncodingKind.UTF8,
                WatchEnabled = true,
                LoggingEnabled = true,
                PushDiagnosticsEnabled = true,
            },
        });
        return (session, sessionUtils);
    }

    /// <summary>
    /// Go: <c>func WithRequestID(ctx context.Context) context.Context</c>。C# 的 `Core.WithRequestID` 是静态类
    /// `TypeScript.Core.Core` 的成员 ⇒ 只能用全限定名或 `using static`（裸写 `Core.WithRequestID` 会绑到
    /// 命名空间 `TypeScript.Core`，CS0117/CS0234，见 docs/architecture.md §2）。
    /// </summary>
    public static Context WithRequestID(Context ctx) => TypeScript.Core.Core.WithRequestID(ctx, "0");

    /// <summary>Go: <c>func GetSessionInitOptions(files map[string]any, options *project.SessionOptions, tiOptions *TypingsInstallerOptions) (*project.SessionInit, *SessionUtils)</c>。</summary>
    public static (SessionInit Init, SessionUtils Utils) GetSessionInitOptions(
        Dictionary<string, object?> files,
        SessionOptions? options,
        TypingsInstallerOptions? tiOptions)
    {
        var fsFromFileMap = (FsWithSys)FromMap(Flatten(files), false /*useCaseSensitiveFileNames*/);
        var fs = WrapFS(fsFromFileMap);
        var clientMock = new ClientMock();
        var npmExecutorMock = new NpmExecutorMock();
        var sessionUtils = new SessionUtils(
            currentDirectory: "/",
            fsFromFileMap: fsFromFileMap,
            fs: fs,
            client: clientMock,
            npmExecutor: npmExecutorMock,
            tiOptions: tiOptions,
            logger: Logging.NewTestLogger());

        // Configure the npm executor mock to handle typings installation
        sessionUtils.SetupNpmExecutorForTypingsInstaller();

        // Use provided options or create default ones
        options ??= new SessionOptions
        {
            CurrentDirectory = "/",
            DefaultLibraryPath = LibPath(),
            TypingsLocation = TestTypingsLocation,
            PositionEncoding = PositionEncodingKind.UTF8,
            WatchEnabled = true,
            LoggingEnabled = true,
            PushDiagnosticsEnabled = true,
        };

        return (new SessionInit
        {
            BackgroundCtx = Context.Background,
            Options = options,
            FS = fs,
            Client = clientMock,
            NpmExecutor = npmExecutorMock,
            Logger = sessionUtils._logger,
        }, sessionUtils);
    }

    /// <summary>Go: <c>func TypesRegistryConfigText() string</c>。</summary>
    public static string TypesRegistryConfigText() => _typesRegistryConfigText.Value;

    /// <summary>Go: <c>var typesRegistryConfig map[string]string</c>（见文件头第 8、9 条）。</summary>
    private static readonly Lazy<string> _typesRegistryConfigText = new(() =>
    {
        var result = new System.Text.StringBuilder();
        foreach (var (key, value) in TypesRegistryConfig())
        {
            if (result.Length != 0)
            {
                result.Append(',');
            }

            result.Append($"\n      \"{key}\": \"{value}\"");
        }

        return result.ToString();
    });

    /// <summary>Go: <c>func TypesRegistryConfig() map[string]string</c>。</summary>
    public static Dictionary<string, string> TypesRegistryConfig() => _typesRegistryConfig.Value;

    private static readonly Lazy<Dictionary<string, string>> _typesRegistryConfig = new(() => new Dictionary<string, string>
    {
        ["latest"] = "1.3.0",
        ["ts2.0"] = "1.0.0",
        ["ts2.1"] = "1.0.0",
        ["ts2.2"] = "1.2.0",
        ["ts2.3"] = "1.3.0",
        ["ts2.4"] = "1.3.0",
        ["ts2.5"] = "1.3.0",
        ["ts2.6"] = "1.3.0",
        ["ts2.7"] = "1.3.0",
    });

    /// <summary>
    /// 把（可能嵌套的）文件树展开成 Go 的扁平 `map[string]any` 形状（见文件头第 2 条）。
    /// 值只能是 `string` / `byte[]` / <see cref="MapFile"/> / 嵌套的 `Dictionary&lt;string, object?&gt;`。
    /// </summary>
    internal static Dictionary<string, object> Flatten(Dictionary<string, object?> tree)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        FlattenInto("", tree, result);
        return result;
    }

    private static void FlattenInto(string prefix, Dictionary<string, object?> tree, Dictionary<string, object> result)
    {
        foreach (var (key, value) in tree)
        {
            var path = key.StartsWith('/') || prefix.Length == 0
                ? key
                : prefix + "/" + key;
            if (value is Dictionary<string, object?> nested)
            {
                FlattenInto(path, nested, result);
                continue;
            }

            result[path] = value ?? throw new ArgumentNullException(nameof(tree), $"file tree value for \"{path}\" is null");
        }
    }
}

/// <summary>Go 的 <c>projecttestutil.TypingsInstallerOptions</c>。</summary>
public sealed class TypingsInstallerOptions
{
    public string[] TypesRegistry = [];

    public Dictionary<string, string> PackageToFile = new(StringComparer.Ordinal);
}

/// <summary>Go 的 <c>projecttestutil.SessionUtils</c>：Setup 返回的第二半，测试断言/驱动用。</summary>
public sealed class SessionUtils
{
    private readonly string _currentDirectory;
    private readonly FsWithSys? _fsFromFileMap;
    private readonly FS _fs;
    private readonly ClientMock _client;
    private readonly NpmExecutorMock _npmExecutor;
    private readonly TypingsInstallerOptions? _tiOptions;

    /// <summary>Go 的 <c>logger logging.LogCollector</c>（`Logs()` 读它；Session 也用同一个实例）。</summary>
    internal readonly TypeScript.Project.Logging.LogCollector _logger;

    internal SessionUtils(
        string currentDirectory,
        FsWithSys? fsFromFileMap,
        FS fs,
        ClientMock client,
        NpmExecutorMock npmExecutor,
        TypingsInstallerOptions? tiOptions,
        TypeScript.Project.Logging.LogCollector logger)
    {
        _currentDirectory = currentDirectory;
        _fsFromFileMap = fsFromFileMap;
        _fs = fs;
        _client = client;
        _npmExecutor = npmExecutor;
        _tiOptions = tiOptions;
        _logger = logger;
    }

    /// <summary>Go: <c>func (h *SessionUtils) FsFromFileMap() iovfs.FsWithSys</c>（<c>SetupWithRealFS</c> 下是 nil）。</summary>
    public FsWithSys? FsFromFileMap() => _fsFromFileMap;

    /// <summary>Go: <c>func (h *SessionUtils) Client() *ClientMock</c>。</summary>
    public ClientMock Client() => _client;

    /// <summary>Go: <c>func (h *SessionUtils) NpmExecutor() *NpmExecutorMock</c>。</summary>
    public NpmExecutorMock NpmExecutor() => _npmExecutor;

    /// <summary>Go: <c>func (h *SessionUtils) SetupNpmExecutorForTypingsInstaller()</c>。</summary>
    public void SetupNpmExecutorForTypingsInstaller()
    {
        if (_tiOptions is null)
        {
            return;
        }

        _npmExecutor.NpmInstallFunc = (cwd, packageNames) =>
        {
            // packageNames is actually npmInstallArgs due to interface misnaming
            var npmInstallArgs = packageNames;
            var lenNpmInstallArgs = npmInstallArgs.Length;
            if (lenNpmInstallArgs < 3)
            {
                throw new Exception($"unexpected npm install: {cwd} {FormatStringSlice(npmInstallArgs)}");
            }

            if (lenNpmInstallArgs == 3 && npmInstallArgs[2] == "types-registry@latest")
            {
                // Write typings file
                _fs.WriteFile(cwd + "/node_modules/types-registry/index.json", CreateTypesRegistryFileContent());
                return null;
            }

            // Find the packages: they start at index 2 and continue until we hit a flag starting with --
            var packageEnd = lenNpmInstallArgs;
            for (var i = 2; i < lenNpmInstallArgs; i++)
            {
                if (npmInstallArgs[i].StartsWith("--", StringComparison.Ordinal))
                {
                    packageEnd = i;
                    break;
                }
            }

            for (var i = 2; i < packageEnd; i++)
            {
                // @types/packageName@TsVersionToUse
                var atTypesPackage = npmInstallArgs[i];
                // Remove version suffix
                var versionIndex = atTypesPackage.LastIndexOf('@');
                if (versionIndex > 6) // "@types/".length is 7, so version @ must be after
                {
                    atTypesPackage = atTypesPackage[..versionIndex];
                }

                // Extract package name from @types/packageName
                var packageBaseName = atTypesPackage[7..]; // Remove "@types/" prefix
                if (!_tiOptions.PackageToFile.TryGetValue(packageBaseName, out var content))
                {
                    throw new Exception($"content not provided for {packageBaseName}");
                }

                _fs.WriteFile(cwd + "/node_modules/@types/" + packageBaseName + "/index.d.ts", content);
            }

            return null;
        };
    }

    /// <summary>
    /// Go: <c>func (h *SessionUtils) ToPath(fileName string) tspath.Path</c>。
    /// ⚠ 必须全限定 `TypeScript.TSPath.TSPath.ToPath`：本类自己有个同名方法 `ToPath(string)`，而**同一类型里的成员
    /// 会遮蔽 `using static` 导入的静态方法**（简单名解析先找 enclosing type 的成员）⇒ 裸写 `ToPath(fileName,
    /// basePath, useCaseSensitiveFileNames)` 会报 CS1501（找不到三参重载）。
    /// </summary>
    public TSPathType ToPath(string fileName) =>
        TypeScript.TSPath.TSPath.ToPath(fileName, _currentDirectory, _fs.UseCaseSensitiveFileNames());

    /// <summary>Go: <c>func (h *SessionUtils) FS() vfs.FS</c>。</summary>
    public FS FS() => _fs;

    /// <summary>
    /// Go: <c>func (h *SessionUtils) WatchesFile(filePath string) bool</c>。任何已注册的 watcher 匹配该路径就返回 true；
    /// 同时处理绝对 glob 与带 base URI 的相对 pattern。大小写不敏感的文件系统上 glob 里的路径是小写的，
    /// 调用方应传小写路径。
    /// </summary>
    public bool WatchesFile(string filePath)
    {
        foreach (var call in _client.WatchFilesCalls())
        {
            foreach (var watcher in call.Watchers)
            {
                if (watcher.GlobPattern.Pattern is not null)
                {
                    if (Glob.Glob.TryParse(watcher.GlobPattern.Pattern, out var g) && g!.Match(filePath))
                    {
                        return true;
                    }
                }
                else if (watcher.GlobPattern.RelativePattern is not null)
                {
                    var rp = watcher.GlobPattern.RelativePattern;
                    // Go: baseUri := string(*rp.BaseUri.URI)
                    var baseUri = rp.BaseUri.URI!.Value.Value;
                    // Convert base URI (e.g. "file:///home/projects") to a directory path
                    // with trailing separator for proper prefix matching on path boundaries.
                    var baseDir = new DocumentUri(baseUri).FileName();
                    baseDir = EnsureTrailingDirectorySeparator(baseDir);
                    if (filePath.StartsWith(baseDir, StringComparison.Ordinal))
                    {
                        var relativePath = filePath[baseDir.Length..];
                        if (Glob.Glob.TryParse(rp.Pattern, out var g) && g!.Match(relativePath))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Go: <c>func (h *SessionUtils) Logs() string</c>。</summary>
    public string Logs() => _logger.String();

    /// <summary>Go: <c>func (h *SessionUtils) BaselineLogs(t *testing.T)</c>。</summary>
    public void BaselineLogs(IHarnessTest t)
    {
        Baseline.Run(t, t.Name + ".log", Logs(), new BaselineOptions { Subfolder = "project" });
    }

    /// <summary>Go: <c>func (h *SessionUtils) createTypesRegistryFileContent() string</c>（未导出）。</summary>
    internal string CreateTypesRegistryFileContent()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("{\n  \"entries\": {");
        var tiOptions = _tiOptions!;
        for (var index = 0; index < tiOptions.TypesRegistry.Length; index++)
        {
            AppendTypesRegistryConfig(builder, index, tiOptions.TypesRegistry[index]);
        }

        var nextIndex = tiOptions.TypesRegistry.Length;
        foreach (var key in tiOptions.PackageToFile.Keys)
        {
            if (!tiOptions.TypesRegistry.Contains(key))
            {
                AppendTypesRegistryConfig(builder, nextIndex, key);
                nextIndex++;
            }
        }

        builder.Append("\n  }\n}");
        return builder.ToString();
    }

    /// <summary>Go: <c>func (h *SessionUtils) appendTypesRegistryConfig(builder *strings.Builder, index int, entry string)</c>（未导出）。</summary>
    private static void AppendTypesRegistryConfig(System.Text.StringBuilder builder, int index, string entry)
    {
        if (index > 0)
        {
            builder.Append(',');
        }

        builder.Append($"\n    \"{entry}\": {{{ProjectTestUtil.TypesRegistryConfigText()}\n    }}");
    }

    /// <summary>
    /// `fmt.Sprintf("…%v…", []string{…})` 的 `[a b c]` 形状（Go 的 `fmt` 对切片就是这个文本；
    /// 仓内同款：`Ata.TypingsInfo.FormatStringSlice`）。错误文本与 Go 逐字一致。
    /// </summary>
    private static string FormatStringSlice(string[] values) => "[" + string.Join(' ', values) + "]";
}

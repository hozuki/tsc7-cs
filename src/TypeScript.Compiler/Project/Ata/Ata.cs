// ported from: internal/project/ata/ata.go:1-506
//
// 与 Go 的偏离（逐条）：
// 1) **错误路径**：Go 的 `(T, error)` 按 docs/architecture.md §3.2 落成"正常路径返回 T，失败抛异常"。影响：
//    · `NpmExecutor.NpmInstall(cwd, args) ([]byte, error)` → `byte[] NpmInstall(string cwd, string[] args)`（失败抛异常；
//      byte[] 只在成功路径存在）。
//    · `TypingsInstaller.InstallTypings(request) (*TypingsInstallResult, error)` → 抛异常。Go 的
//      `discoverAndInstallTypings`/`installTypings` 同理（后两者是 private，C# 侧也抛）。
//    · 失败时 Go 会 `logger.Log(fmt.Sprintf("ATA:: Output is: %s", output))`（output 是 npm 的 stdout/stderr 字节）；
//      C# 的错误路径没有 output，改成打印异常消息（`e.Message`）。
//    · `installNpmPackages(...) error` → `InstallNpmPackages(...) void`，`ThrottleGroup.Wait()` 重抛第一个异常。
// 2) **`sync.Once` → 锁 + 标志**（`_initLock` + `_initialized`）：Go 的 `once.Do` 在函数执行期间阻塞其它调用者，
//    这里整段持锁，语义一致（`Core.PackageJson` 的 Cache.cs 有同款先例）。
// 3) **`sync/atomic.Int32` → `Interlocked.Increment`**：Go 的 `ti.installRunCount.Add(1)` → `Interlocked.Increment(ref _installRunCount)`。
// 4) **`chan struct{}` 当信号量 → `SemaphoreSlim`**（Go 的 `make(chan struct{}, N)` → `new SemaphoreSlim(N, N)`），
//    与 `Core.NewThrottleGroup` 的既定形状一致（见 Core/Workgroup.cs）。`InstallNpmPackages` 用 `Task.Run` 起的并发
//    由 `ThrottleGroup.Wait()` join，整体仍是同步的（Go 的 `tg.Wait()`）。
// 5) **`context.Context` → `TypeScript.Core.Context`**；`ti.installWorker` 里 Go 建的是 `context.Background()` →
//    `Context.Background`。
// 6) **包级函数/常量落点**：`ata.NewTypingsInstaller` → `TypingsInstaller.NewTypingsInstaller`（静态工厂留在它构造的
//    类型上；C# 没有包作用域）。未导出的包级函数 `installNpmPackages` 只有 `installnpmpackages_test.go`（同包测试）
//    调用 → `internal static TypingsInstaller.InstallNpmPackages`。
// 7) **`ProjectID interface{ fmt.Stringer }`** → `public interface ProjectID { string String(); }`（保留 Go 的方法名，
//    仓内先例：`Locale.String()`、`EmitTextWriter.String()`）。`TypingsInstallRequest.ProjectID` 字段名与类型同名 ⇒
//    字段声明的类型用全限定名。
// 8) **`%v` 打切片**：Go 的 `fmt.Sprintf("…%v…", []string{…})` 产出 `[a b c]` → `TypingsInfo.FormatStringSlice`（ata.go
//    与 discovertypings.go 的日志文本因此与 Go 逐字一致）。
// 9) **未使用的公开字段**：`TypingsInstallRequest.CompilerOptions`/`CurrentDirectory`/`GetScriptKind` 在 Go 的 ata.go
//    里**声明但从未被读取**，为保持公开 API 一致仍完整保留（`GetScriptKind` 是 `Func<string, Core.ScriptKind>`）。
// 10) **JSON DTO（npmConfig/npmDependecyEntry/npmLock）**：Go 用 struct tag 走 `json.Unmarshal`；AOT 下 C# 禁反射映射
//    （docs §3.6），所以按 packagejson 的手写风格实现 `IJsonUnmarshaler`（字段名/类型与 Go 的 tag 逐条对应）。
//    类型名按 C# 习惯：`npmConfig`→`NpmConfig`、`npmDependecyEntry`→`NpmDependencyEntry`（Go 的拼写笔误不再保留）、
//    `npmLock`→`NpmLock`。
// 11) **types-registry 的三层 map**：Go 是 `map[string]map[string]map[string]string`；Json 子包的字典解码只支持
//    "值走 shape 的一层"（嵌套 Dictionary 会 NeedsInstance），所以 `LoadTypesRegistryFile` 里手写三层读取。
// 12) **`fs.WriteFile` 的失败**：Go 返回 error → C# 抛 IOException（本仓 FS 契约），故 `catch (IOException)`。
// 13) **日志换行/格式**：`Logger.Logf` 是 C# 复合格式，Go 的 `%v/%s` 在调用点改成插值（见第 8 条的切片特例）。
// 14) **nil 切片**：Go 的 `append(currentlyCachedTypings, installedTypingFiles...)` 与返回的 nil 切片 → C# 用非 null
//    数组（空 = `Length == 0`）。
using System.Text;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.Module;
using TypeScript.Project.Logging;
using TypeScript.Vfs;
using SemVerVersion = TypeScript.SemVer.Version;
// `Decoder` 同时被 `System.Text`（ImplicitUsings 之外的 `using System.Text;`，为了 Encoding）与 `TypeScript.Json`
// 提供 → 用别名消歧（仓内既有做法：`using Path = TypeScript.TSPath.Path;`）。
using Decoder = TypeScript.Json.Decoder;
using static TypeScript.Core.Core;
using static TypeScript.SemVer.SemVer;
using static TypeScript.Json.Json;
using static TypeScript.Module.Module;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Project.Ata;

/// <summary>Go: <c>type TypingsInfo struct</c>（discovertypings.go 的 `DiscoverTypings` 在本类型的另一个 partial 里）。</summary>
public sealed partial class TypingsInfo
{
    public TypeAcquisition? TypeAcquisition;

    public CompilerOptions? CompilerOptions;

    public Set<string>? UnresolvedImports;

    // Go: func (ti TypingsInfo) Equals(other TypingsInfo) bool（值接收者 → C# 实例方法 + 可空参数）
    public bool Equals(TypingsInfo? other)
    {
        // Go 的 `ti.TypeAcquisition.Equals(other.TypeAcquisition)` 是 nil 接收者安全的（见 TypeAcquisition 文件头）：
        // C# 实例方法不能在 null 上分派，故用 `?.` + `??` 复刻"两者同为 nil → true"。
        var typeAcquisitionEquals = TypeAcquisition?.Equals(other?.TypeAcquisition)
            ?? (other?.TypeAcquisition is null);
        var unresolvedImportsEquals = UnresolvedImports?.Equals(other?.UnresolvedImports)
            ?? (other?.UnresolvedImports is null);
        return typeAcquisitionEquals
            && (CompilerOptions?.GetAllowJS() ?? false) == (other?.CompilerOptions?.GetAllowJS() ?? false)
            && unresolvedImportsEquals;
    }
}

/// <summary>Go: <c>type CachedTyping struct</c>。</summary>
public sealed class CachedTyping
{
    public string TypingsLocation = "";
    public SemVerVersion? Version;
}

/// <summary>Go: <c>type TypingsInstallerOptions struct</c>。</summary>
public sealed class TypingsInstallerOptions
{
    public string TypingsLocation = "";
    public int ThrottleLimit;
}

/// <summary>Go: <c>type NpmExecutor interface { NpmInstall(cwd string, args []string) ([]byte, error) }</c>（见文件头第 1 条）。</summary>
public interface NpmExecutor
{
    byte[] NpmInstall(string cwd, string[] args);
}

/// <summary>Go: <c>type TypingsInstallerHost interface { NpmExecutor; module.ResolutionHost }</c>。</summary>
public interface TypingsInstallerHost : NpmExecutor, ResolutionHost;

/// <summary>Go: <c>type ProjectID interface { fmt.Stringer }</c>（见文件头第 7 条）。</summary>
public interface ProjectID
{
    string String();
}

/// <summary>Go: <c>type TypingsInstallRequest struct</c>。</summary>
public sealed class TypingsInstallRequest
{
    // 字段名与类型同名：字段声明的类型用全限定名（裸 `ProjectID` 在这里会解析到字段）。
    public TypeScript.Project.Ata.ProjectID ProjectID = null!;
    public TypingsInfo TypingsInfo = null!;
    public string[] FileNames = [];
    public string ProjectRootPath = "";
    public CompilerOptions CompilerOptions = null!;
    public string CurrentDirectory = "";
    public Func<string, ScriptKind> GetScriptKind = null!;
    public FS FS = null!;
    public Logger? Logger;
}

/// <summary>Go: <c>type TypingsInstallResult struct</c>。</summary>
public sealed class TypingsInstallResult
{
    public string[] TypingsFiles = [];
    public string[] FilesToWatch = [];
}

/// <summary>Go: <c>type TypingsInstaller struct</c> + 包级函数 <c>NewTypingsInstaller</c>（见文件头第 6 条）。</summary>
public sealed class TypingsInstaller
{
    private readonly string _typingsLocation;
    private readonly TypingsInstallerHost _host;

    // Go: initOnce sync.Once → 锁 + 标志（见文件头第 2 条）。
    private readonly Lock _initLock = new();
    private bool _initialized;

    private readonly SyncMap<string, CachedTyping> _packageNameToTypingLocation = new();
    private readonly SyncMap<string, bool> _missingTypingsSet = new();

    private Dictionary<string, Dictionary<string, string>> _typesRegistry = new(StringComparer.Ordinal);

    // Go: installRunCount atomic.Int32 → Interlocked（见文件头第 3 条）。
    private int _installRunCount;

    // Go: concurrencySemaphore chan struct{} → SemaphoreSlim（见文件头第 4 条）。
    private readonly SemaphoreSlim _concurrencySemaphore;

    private TypingsInstaller(TypingsInstallerOptions options, TypingsInstallerHost host)
    {
        _typingsLocation = options.TypingsLocation;
        _host = host;
        _concurrencySemaphore = new SemaphoreSlim(options.ThrottleLimit, options.ThrottleLimit);
    }

    /// <summary>Go: <c>func NewTypingsInstaller(options *TypingsInstallerOptions, host TypingsInstallerHost) *TypingsInstaller</c>。</summary>
    public static TypingsInstaller NewTypingsInstaller(TypingsInstallerOptions options, TypingsInstallerHost host)
    {
        return new TypingsInstaller(options, host);
    }

    /// <summary>Go: <c>func (ti *TypingsInstaller) IsKnownTypesPackageName(projectID ProjectID, name string, fs vfs.FS, logger logging.Logger) bool</c>。</summary>
    public bool IsKnownTypesPackageName(ProjectID projectID, string name, FS fs, Logger? logger)
    {
        // We want to avoid looking this up in the registry as that is expensive. So first check that it's actually an NPM package.
        var (validationResult, _, _) = PackageNameValidation.ValidatePackageName(name);
        if (validationResult != NameValidationResult.NameOk)
        {
            return false;
        }

        // Strada did this lazily - is that needed here to not waiting on and returning false on first request
        Init(projectID.String(), fs, logger);
        return _typesRegistry.ContainsKey(name);
    }

    // !!! sheetal currently we use latest instead of core.VersionMajorMinor()
    private const string _tsVersionToUse = "latest";

    /// <summary>Go: <c>func (ti *TypingsInstaller) InstallTypings(request *TypingsInstallRequest) (*TypingsInstallResult, error)</c>。</summary>
    public TypingsInstallResult InstallTypings(TypingsInstallRequest request)
    {
        var result = DiscoverAndInstallTypings(request);
        Array.Sort(result.TypingsFiles);
        Array.Sort(result.FilesToWatch);
        request.Logger?.Log("ATA:: Got install request for: " + request.ProjectID.String());
        return result;
    }

    // Go: func (ti *TypingsInstaller) discoverAndInstallTypings(request *TypingsInstallRequest) (*TypingsInstallResult, error)
    private TypingsInstallResult DiscoverAndInstallTypings(TypingsInstallRequest request)
    {
        Init(request.ProjectID.String(), request.FS, request.Logger);

        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            request.FS,
            request.Logger,
            request.TypingsInfo,
            request.FileNames,
            request.ProjectRootPath,
            _packageNameToTypingLocation,
            _typesRegistry);

        var requestId = Interlocked.Increment(ref _installRunCount);
        // install typings
        if (newTypingNames.Length > 0)
        {
            var filteredTypings = FilterTypings(request.Logger, newTypingNames);
            if (filteredTypings.Length != 0)
            {
                var typingsFiles = InstallTypings(requestId, cachedTypingPaths, filteredTypings, request.Logger);
                return new TypingsInstallResult
                {
                    TypingsFiles = typingsFiles,
                    FilesToWatch = filesToWatch,
                };
            }

            request.Logger?.Log("ATA:: All typings are known to be missing or invalid - no need to install more typings");
        }
        else
        {
            request.Logger?.Log("ATA:: No new typings were requested as a result of typings discovery");
        }

        return new TypingsInstallResult
        {
            TypingsFiles = cachedTypingPaths,
            FilesToWatch = filesToWatch,
        };
        // !!! sheetal events to send
        // this.event(response, "setTypings");
    }

    // Go: func (ti *TypingsInstaller) installTypings(requestID int32, currentlyCachedTypings []string, filteredTypings []string, logger logging.Logger) ([]string, error)
    // （与上面的公开重载同名，靠参数表区分；Go 靠首字母大小写区分，见文件头第 1 条。）
    private string[] InstallTypings(
        int requestID,
        string[] currentlyCachedTypings,
        string[] filteredTypings,
        Logger? logger)
    {
        // !!! sheetal events to send（Go 侧只是一大段注释，未移植事件协议，见 ata.go:150-215）
        var scopedTypings = new string[filteredTypings.Length];
        for (var i = 0; i < filteredTypings.Length; i++)
        {
            var packageName = filteredTypings[i];
            scopedTypings[i] = $"@types/{packageName}@{_tsVersionToUse}"; // @tscore.VersionMajorMinor) // This is normally @tsVersionMajorMinor but for now lets use latest
        }

        var (packageNames, ok) = InstallWorker(requestID, scopedTypings, logger);
        if (ok)
        {
            logger?.Log($"ATA:: Installed typings {TypingsInfo.FormatStringSlice(packageNames)}");
            var installedTypingFiles = new List<string>();
            var resolver = NewResolver(new ResolverOptions
            {
                Host = _host,
                CompilerOptions = new CompilerOptions { ModuleResolution = ModuleResolutionKind.NodeNext },
            });
            foreach (var packageName in filteredTypings)
            {
                var typingFile = TypingToFileName(resolver, packageName);
                if (typingFile.Length == 0)
                {
                    logger?.Log($"ATA:: Failed to find typing file for package '{packageName}'");
                    _missingTypingsSet.Store(packageName, true);
                    continue;
                }

                // packageName is guaranteed to exist in typesRegistry by filterTypings
                var distTags = _typesRegistry[packageName];
                if (!distTags.TryGetValue("ts" + VersionMajorMinor(), out var useVersion))
                {
                    useVersion = distTags["latest"];
                }

                var newVersion = MustParse(useVersion);
                var newTyping = new CachedTyping { TypingsLocation = typingFile, Version = newVersion };
                _packageNameToTypingLocation.Store(packageName, newTyping);
                installedTypingFiles.Add(typingFile);
            }

            logger?.Log($"ATA:: Installed typing files {TypingsInfo.FormatStringSlice(installedTypingFiles)}");

            var combined = new string[currentlyCachedTypings.Length + installedTypingFiles.Count];
            currentlyCachedTypings.CopyTo(combined, 0);
            installedTypingFiles.CopyTo(combined, currentlyCachedTypings.Length);
            return combined;
        }

        // DO we really need these events
        // this.event(response, "setTypings");
        logger?.Log($"ATA:: install request failed, marking packages as missing to prevent repeated requests: {TypingsInfo.FormatStringSlice(filteredTypings)}");
        foreach (var typing in filteredTypings)
        {
            _missingTypingsSet.Store(typing, true);
        }

        throw new InvalidOperationException("npm install failed");
    }

    // Go: func (ti *TypingsInstaller) installWorker(requestId int32, packageNames []string, logger logging.Logger) ([]string, bool)
    private (string[] PackageNames, bool Ok) InstallWorker(int requestId, string[] packageNames, Logger? logger)
    {
        logger?.Log($"ATA:: #{requestId} with cwd: {_typingsLocation} arguments: {TypingsInfo.FormatStringSlice(packageNames)}");
        var ctx = Context.Background;
        var ok = true;
        try
        {
            InstallNpmPackages(ctx, packageNames, _concurrencySemaphore, packageNamesToInstall =>
            {
                var npmArgs = new List<string> { "install", "--ignore-scripts" };
                npmArgs.AddRange(packageNamesToInstall);
                npmArgs.Add("--save-dev");
                npmArgs.Add("--user-agent=\"typesInstaller/" + Version() + "\"");
                try
                {
                    _host.NpmInstall(_typingsLocation, npmArgs.ToArray());
                }
                catch (Exception e)
                {
                    // Go: output, err := ti.host.NpmInstall(...); if err != nil { logger.Log(fmt.Sprintf("ATA:: Output is: %s", output)); return err }
                    // C# 的错误路径没有 output 字节（见文件头第 1 条）→ 打印异常消息。
                    logger?.Log("ATA:: Output is: " + e.Message);
                    throw;
                }
            });
        }
        catch (Exception)
        {
            ok = false;
        }

        logger?.Log($"TI:: npm install #{requestId} completed");
        return (packageNames, ok);
    }

    /// <summary>
    /// Go: <c>func installNpmPackages(ctx context.Context, packageNames []string, concurrencySemaphore chan struct{},
    /// installPackages func(packages []string) error) error</c>。
    /// </summary>
    internal static void InstallNpmPackages(
        Context ctx,
        string[] packageNames,
        SemaphoreSlim concurrencySemaphore,
        Action<string[]> installPackages)
    {
        var tg = NewThrottleGroup(ctx, concurrencySemaphore);

        var currentCommandStart = 0;
        var currentCommandEnd = 0;
        var currentCommandSize = 100;

        foreach (var packageName in packageNames)
        {
            // Go: len(packageName) 是**字节**长度（npm 命令行长度限制按字节）→ 用 UTF-8 字节数保持一致。
            currentCommandSize = currentCommandSize + Encoding.UTF8.GetByteCount(packageName) + 1;
            if (currentCommandSize < 8000)
            {
                currentCommandEnd++;
            }
            else
            {
                var packages = packageNames[currentCommandStart..currentCommandEnd];
                tg.Go(() => installPackages(packages));
                currentCommandStart = currentCommandEnd;
                currentCommandSize = 100 + Encoding.UTF8.GetByteCount(packageName) + 1;
                currentCommandEnd++;
            }
        }

        // Handle the final batch
        if (currentCommandStart < packageNames.Length)
        {
            var packages = packageNames[currentCommandStart..currentCommandEnd];
            tg.Go(() => installPackages(packages));
        }

        tg.Wait();
    }

    // Go: func (ti *TypingsInstaller) filterTypings(logger logging.Logger, typingsToInstall []string) []string
    private string[] FilterTypings(Logger? logger, string[] typingsToInstall)
    {
        var result = new List<string>();
        foreach (var typing in typingsToInstall)
        {
            var typingKey = MangleScopedPackageName(typing);
            var (_, isMissing) = _missingTypingsSet.Load(typingKey);
            if (isMissing)
            {
                logger?.Log($"ATA:: '{typing}':: '{typingKey}' is in missingTypingsSet - skipping...");
                continue;
            }

            var (validationResult, name, isScopeName) = PackageNameValidation.ValidatePackageName(typing);
            if (validationResult != NameValidationResult.NameOk)
            {
                // add typing name to missing set so we won't process it again
                _missingTypingsSet.Store(typingKey, true);
                logger?.Log("ATA:: " + PackageNameValidation.RenderPackageNameValidationFailure(typing, validationResult, name, isScopeName));
                continue;
            }

            if (!_typesRegistry.TryGetValue(typingKey, out var typesRegistryEntry))
            {
                logger?.Log($"ATA:: '{typing}':: Entry for package '{typingKey}' does not exist in local types registry - skipping...");
                continue;
            }

            var (typingLocation, hasTypingLocation) = _packageNameToTypingLocation.Load(typingKey);
            if (hasTypingLocation && typingLocation is not null && TypingsInfo.IsTypingUpToDate(typingLocation, typesRegistryEntry))
            {
                logger?.Log($"ATA:: '{typing}':: '{typingKey}' already has an up-to-date typing - skipping...");
                continue;
            }

            result.Add(typingKey);
        }

        return result.ToArray();
    }

    // Go: func (ti *TypingsInstaller) init(projectID string, fs vfs.FS, logger logging.Logger)
    private void Init(string projectID, FS fs, Logger? logger)
    {
        // Go: ti.initOnce.Do(func() { … })（见文件头第 2 条）
        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            logger?.Log("ATA:: Global cache location '" + _typingsLocation + "'"); // , safe file path '" + safeListPath + "', types map path '" + typesMapLocation + "`)

            ProcessCacheLocation(projectID, fs, logger);

            // !!! sheetal handle npm path here if we would support it（Go 侧是注释块，未移植）

            EnsureTypingsLocationExists(fs, logger);
            logger?.Log("ATA:: Updating types-registry@latest npm package...");
            try
            {
                _host.NpmInstall(_typingsLocation, ["install", "--ignore-scripts", "types-registry@latest"]);
                logger?.Log("ATA:: Updated types-registry npm package");
            }
            catch (Exception e)
            {
                logger?.Log($"ATA:: Error updating types-registry package: {e.Message}");
                // !!! sheetal events to send（Go 侧是注释块，未移植）
            }

            _typesRegistry = LoadTypesRegistryFile(fs, logger);
            _initialized = true;
        }
    }

    // Go: func (ti *TypingsInstaller) processCacheLocation(projectID string, fs vfs.FS, logger logging.Logger)
    private void ProcessCacheLocation(string projectID, FS fs, Logger? logger)
    {
        logger?.Log("ATA:: Processing cache location " + _typingsLocation);
        var packageJson = CombinePaths(_typingsLocation, "package.json");
        var packageLockJson = CombinePaths(_typingsLocation, "package-lock.json");
        logger?.Log("ATA:: Trying to find '" + packageJson + "'...");
        if (fs.FileExists(packageJson) && fs.FileExists(packageLockJson))
        {
            var npmConfigContents = ParseNpmConfigOrLock(fs, logger, packageJson, out NpmConfig? npmConfig);
            var npmLockContents = ParseNpmConfigOrLock(fs, logger, packageLockJson, out NpmLock? npmLock);

            logger?.Log("ATA:: Loaded content of " + packageJson + ": " + npmConfigContents);
            logger?.Log("ATA:: Loaded content of " + packageLockJson + ": " + npmLockContents);

            // !!! sheetal strada uses Node10
            var resolver = NewResolver(new ResolverOptions
            {
                Host = _host,
                CompilerOptions = new CompilerOptions { ModuleResolution = ModuleResolutionKind.NodeNext },
            });
            if (npmConfig?.DevDependencies is not null && (npmLock?.Packages is not null || npmLock?.Dependencies is not null))
            {
                foreach (var key in npmConfig.DevDependencies.Keys)
                {
                    NpmDependencyEntry? npmLockValue = null;
                    var npmLockValueExists = false;
                    if (npmLock?.Packages is not null && npmLock.Packages.TryGetValue("node_modules/" + key, out var packageValue))
                    {
                        npmLockValue = packageValue;
                        npmLockValueExists = true;
                    }

                    if (!npmLockValueExists && npmLock?.Dependencies is not null && npmLock.Dependencies.TryGetValue(key, out var dependencyValue))
                    {
                        npmLockValue = dependencyValue;
                        npmLockValueExists = true;
                    }

                    if (!npmLockValueExists)
                    {
                        // if package in package.json but not package-lock.json, skip adding to cache so it is reinstalled on next use
                        continue;
                    }

                    // key is @types/<package name>
                    var packageName = GetBaseFileName(key);
                    if (packageName.Length == 0)
                    {
                        continue;
                    }

                    var typingFile = TypingToFileName(resolver, packageName);
                    if (typingFile.Length == 0)
                    {
                        _missingTypingsSet.Store(packageName, true);
                        continue;
                    }

                    var (existingTypingFile, existingTypingsFilePresent) = _packageNameToTypingLocation.Load(packageName);
                    if (existingTypingsFilePresent && existingTypingFile is not null)
                    {
                        if (existingTypingFile.TypingsLocation == typingFile)
                        {
                            continue;
                        }

                        logger?.Log("ATA:: New typing for package " + packageName + " from " + typingFile + " conflicts with existing typing file " + existingTypingFile.TypingsLocation);
                    }

                    logger?.Log("ATA:: Adding entry into typings cache: " + packageName + " => " + typingFile);
                    var version = npmLockValue!.Version;
                    if (version.Length == 0)
                    {
                        continue;
                    }

                    var newVersion = MustParse(version);
                    var newTyping = new CachedTyping { TypingsLocation = typingFile, Version = newVersion };
                    _packageNameToTypingLocation.Store(packageName, newTyping);
                }
            }
        }

        logger?.Log("ATA:: Finished processing cache location " + _typingsLocation);
    }

    // Go: func parseNpmConfigOrLock[T npmConfig | npmLock](fs vfs.FS, logger logging.Logger, location string, config *T) string
    private static string ParseNpmConfigOrLock<T>(FS fs, Logger? logger, string location, out T? config)
        where T : class, new()
    {
        // Go: contents, _ := fs.ReadFile(location)（`ok` 被忽略；读不到时 contents 是空串）
        fs.TryReadFile(location, out var contents);
        contents ??= "";
        config = new T();
        try
        {
            // Go: _ = json.Unmarshal([]byte(contents), config)（错误被丢弃，config 保持零值）
            config = UnmarshalNew<T>(Encoding.UTF8.GetBytes(contents));
        }
        catch (Exception e) when (e is JsonException or System.Text.Json.JsonException)
        {
            // 见上：Go 丢弃解码错误（此时 config 是全零值 → C# 保持 new T() 的空实例）。
        }

        return contents;
    }

    // Go: func (ti *TypingsInstaller) ensureTypingsLocationExists(fs vfs.FS, logger logging.Logger)
    private void EnsureTypingsLocationExists(FS fs, Logger? logger)
    {
        var npmConfigPath = CombinePaths(_typingsLocation, "package.json");
        logger?.Log("ATA:: Npm config file: " + npmConfigPath);

        if (!fs.FileExists(npmConfigPath))
        {
            logger?.Log($"ATA:: Npm config file: '{npmConfigPath}' is missing, creating new one...");
            try
            {
                fs.WriteFile(npmConfigPath, "{ \"private\": true }");
            }
            catch (IOException e)
            {
                // Go: err := fs.WriteFile(...); if err != nil { logger.Log(...) }（见文件头第 12 条）
                logger?.Log($"ATA:: Npm config file write failed: {e.Message}");
            }
        }
    }

    // Go: func (ti *TypingsInstaller) typingToFileName(resolver *module.DefaultResolver, packageName string) string
    private string TypingToFileName(DefaultResolver resolver, string packageName)
    {
        var (result, _) = resolver.ResolveModuleName(
            packageName,
            CombinePaths(_typingsLocation, "index.d.ts"),
            ModuleKind.None,
            null);
        return result?.ResolvedFileName ?? "";
    }

    // Go: func (ti *TypingsInstaller) loadTypesRegistryFile(fs vfs.FS, logger logging.Logger) map[string]map[string]string
    private Dictionary<string, Dictionary<string, string>> LoadTypesRegistryFile(FS fs, Logger? logger)
    {
        var typesRegistryFile = CombinePaths(_typingsLocation, "node_modules/types-registry/index.json");
        if (fs.TryReadFile(typesRegistryFile, out var typesRegistryFileContents))
        {
            try
            {
                var entries = ReadTypesRegistryEntries(typesRegistryFileContents);
                if (entries.TryGetValue("entries", out var typesRegistry))
                {
                    return typesRegistry;
                }

                // Go: logger.Log(fmt.Sprintf("…: %v", err)) —— 解出来了但缺 "entries" 时 err 是 nil，`%v` 打 "<nil>"。
                logger?.Log($"ATA:: Error when loading types registry file '{typesRegistryFile}': <nil>");
            }
            catch (Exception e) when (e is JsonException or System.Text.Json.JsonException)
            {
                logger?.Log($"ATA:: Error when loading types registry file '{typesRegistryFile}': {e.Message}");
            }
        }
        else
        {
            logger?.Log($"ATA:: Error reading types registry file '{typesRegistryFile}'");
        }

        return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
    }

    // Go: json.Unmarshal(contents, &entries)，entries 是 map[string]map[string]map[string]string
    // （见文件头第 11 条：三层手写，Json 子包的字典解码不支持嵌套 Dictionary 值）。
    private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> ReadTypesRegistryEntries(string contents)
    {
        var decoder = new Decoder(Encoding.UTF8.GetBytes(contents));
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("types-registry: expected object");
        }

        var entries = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.Ordinal);
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadName();
            entries[name] = ReadTypesRegistryDistTags(decoder);
        }

        decoder.ReadToken();
        return entries;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadTypesRegistryDistTags(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("types-registry: expected object");
        }

        var distTags = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadName();
            distTags[name] = ReadTypesRegistryVersions(decoder);
        }

        decoder.ReadToken();
        return distTags;
    }

    private static Dictionary<string, string> ReadTypesRegistryVersions(Decoder decoder)
    {
        // Go: 解成 map[string]string；JSON null 在 Go 里会留空串（不报错）→ C# 用 `?? ""` 保持一致。
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("types-registry: expected object");
        }

        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadName();
            versions[name] = decoder.Decode<string>() ?? "";
        }

        decoder.ReadToken();
        return versions;
    }
}

/// <summary>Go: <c>type npmConfig struct { DevDependencies map[string]any \`json:"devDependencies"\` }</c>（见文件头第 10 条）。</summary>
internal sealed class NpmConfig : IJsonUnmarshaler
{
    public Dictionary<string, object?>? DevDependencies;

    public void UnmarshalJSONFrom(Decoder dec)
    {
        if (dec.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("npm config: expected object");
        }

        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            if (name == "devDependencies")
            {
                var target = DevDependencies ??= new Dictionary<string, object?>(StringComparer.Ordinal);
                dec.DecodeInto(target);
                continue;
            }

            dec.SkipValue();
        }

        dec.ReadToken();
    }
}

/// <summary>Go: <c>type npmDependecyEntry struct { Version string \`json:"version"\` }</c>（Go 的拼写笔误不再保留）。</summary>
internal sealed class NpmDependencyEntry : IJsonUnmarshaler
{
    public string Version = "";

    public void UnmarshalJSONFrom(Decoder dec)
    {
        if (dec.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("npm lock: expected object");
        }

        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            if (name == "version")
            {
                Version = dec.Decode<string>() ?? "";
                continue;
            }

            dec.SkipValue();
        }

        dec.ReadToken();
    }
}

/// <summary>Go: <c>type npmLock struct { Dependencies map[string]npmDependecyEntry \`json:"dependencies"\`; Packages map[string]npmDependecyEntry \`json:"packages"\` }</c>。</summary>
internal sealed class NpmLock : IJsonUnmarshaler
{
    public Dictionary<string, NpmDependencyEntry>? Dependencies;
    public Dictionary<string, NpmDependencyEntry>? Packages;

    public void UnmarshalJSONFrom(Decoder dec)
    {
        if (dec.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("npm lock: expected object");
        }

        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            if (name == "dependencies")
            {
                Dependencies = ReadEntries(dec);
                continue;
            }

            if (name == "packages")
            {
                Packages = ReadEntries(dec);
                continue;
            }

            dec.SkipValue();
        }

        dec.ReadToken();
    }

    private static Dictionary<string, NpmDependencyEntry> ReadEntries(Decoder dec)
    {
        if (dec.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("npm lock: expected object");
        }

        var entries = new Dictionary<string, NpmDependencyEntry>(StringComparer.Ordinal);
        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            var entry = new NpmDependencyEntry();
            dec.DecodeInto(entry);
            entries[name] = entry;
        }

        dec.ReadToken();
        return entries;
    }
}

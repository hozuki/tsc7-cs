// ported from: internal/project/ata/ata_test.go（**安装器级**的等价用例）+ 本切片测试共享的小工具
//
// 与 Go 测试的差异（逐条）：
// 1) **范围**：ata_test.go 全部 20+ 个用例都通过 `projecttestutil.SetupWithTypingsInstaller` 起一个完整的
//    `project.Session`（DidOpenFile / WaitForBackgroundTasks / GetLanguageService / Program），那是 **Main 的
//    集成切片**（`internal/testutil/projecttestutil` 与 `project.Session` 都在飞）——本切片只负责 ata 包，
//    故这里移植的是**同一批场景在安装器层**的等价形式：直接构造 `TypingsInstallRequest`（字段逐条对应
//    session.go 的构造点）并断言 `TypingsInstallResult` + npm 调用序列 + FS 内容。这样既覆盖 ata.go 的全部
//    路径（init/processCacheLocation/discover/filter/install/typingToFileName），又不依赖还没落地的 Session。
//    未移植清单（原因见上）：session 层的 `local module should not be picked up`、`configured projects`、
//    `inferred projects`、`disableFilenameBasedTypeAcquisition`、`discover from node_modules*`、
//    `discover from bower*`、`Malformed package.json should be watched`、`should redo resolution…`、
//    `deduplicate from local @types packages`（Go 里 `t.Skip`）、`lockfile3` 两例（与 A/B 同形，只换
//    package-lock 结构，本文件的 B/C 已覆盖 packages/dependencies 两条路径）、`should install typings for
//    unresolved imports`（A 覆盖）、`ATA with WatchEnabled false should not panic`、`ATA disabled via …`、
//    `ATA re-enabled after being disabled triggers diagnostics refresh`（都是 session/诊断刷新语义）。
// 2) `projecttestutil.TypesRegistryConfig()`/`TestTypingsLocation`/`SetupNpmExecutorForTypingsInstaller`
//    （Go 在 `internal/testutil/projecttestutil`，属别的切片）→ 按任务要求在本文件里写**最小**替身：
//    `AtaTestHost`（NpmExecutor + ResolutionHost，逐条照抄 npm mock 的写入行为）与 `TypesRegistryConfig()`。
//    现状说明（2026-09-28）：仓里已有 `tests/TypeScript.Compiler.Tests/Project/ProjectTestUtil/`
//    （namespace `TypeScript.Tests.ProjectTestUtil`，M8Project.ProjTestHarness 落的 `ProjectTestUtil` /
//    `NpmExecutorMock`），但它依赖尚未落地的 `project.Session`/`SessionInit`/`SessionOptions` ⇒ 用它会让本切片
//    的探针跟着那些文件一起编不过、无法自证 0/0。所以本切片保留自带的极小替身（行为与 Go 的 mock 逐条一致），
//    待 Session 落地后由集成方决定是否合并（两处 `TypesRegistryConfig` 的取值完全相同）。
//    registry 文件的 JSON 文本用固定顺序（Go 用 map 遍历，顺序随机但 JSON 对象无序、解析结果一致）。
// 3) Go 的 `time`/`context` 无对应物；`t.Parallel()` 省略（xunit 的并行是集合级）。
using TypeScript.Core;
using TypeScript.Project.Ata;
using TypeScript.Project.Logging;
using TypeScript.Vfs;
using Xunit;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Project.Logging.Logging;
using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

/// <summary>Go: <c>projecttestutil.TestTypingsLocation</c>。</summary>
internal static class AtaTestConstants
{
    public const string TestTypingsLocation = "/home/src/Library/Caches/typescript";
}

/// <summary>Go: <c>projecttestutil.TypesRegistryConfig()</c>（types-registry 里每个包固定的 dist-tag 表）。</summary>
internal static class AtaTestTypesRegistry
{
    public static Dictionary<string, string> Config()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
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
        };
    }

    /// <summary>Go: <c>projecttestutil.TypesRegistryConfigText()</c>（dist-tag 表的 JSON 片段）。</summary>
    public static string ConfigText()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var pair in Config())
        {
            if (builder.Length != 0)
            {
                builder.Append(',');
            }

            builder.Append($"\n      \"{pair.Key}\": \"{pair.Value}\"");
        }

        return builder.ToString();
    }
}

/// <summary>Go: <c>projecttestutil.SessionUtils</c> 在 ATA 相关部分的最小替身（NpmExecutor + ResolutionHost）。</summary>
internal sealed class AtaTestHost : TypingsInstallerHost
{
    private readonly FS _fs;
    private readonly Dictionary<string, string> _packageToFile;
    private readonly List<string> _typesRegistry;

    internal AtaTestHost(FS fs, List<string> typesRegistry, Dictionary<string, string> packageToFile)
    {
        _fs = fs;
        _typesRegistry = typesRegistry;
        _packageToFile = packageToFile;
    }

    internal readonly List<(string Cwd, string[] Args)> NpmInstallCalls = [];

    public FS FS()
    {
        return _fs;
    }

    public string GetCurrentDirectory()
    {
        return "/";
    }

    public byte[] NpmInstall(string cwd, string[] args)
    {
        // Go: projecttestutil.SessionUtils.SetupNpmExecutorForTypingsInstaller 里的 mock（逐条照抄）。
        NpmInstallCalls.Add((cwd, args));
        if (args.Length < 3)
        {
            throw new InvalidOperationException($"unexpected npm install: {cwd} {string.Join(" ", args)}");
        }

        if (args.Length == 3 && args[2] == "types-registry@latest")
        {
            // Write typings file
            _fs.WriteFile(cwd + "/node_modules/types-registry/index.json", CreateTypesRegistryFileContent());
            return [];
        }

        // Find the packages: they start at index 2 and continue until we hit a flag starting with --
        var packageEnd = args.Length;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                packageEnd = i;
                break;
            }
        }

        for (var i = 2; i < packageEnd; i++)
        {
            var atTypesPackage = args[i];
            // Remove version suffix（"@types/".length is 7, so version @ must be after）
            var versionIndex = atTypesPackage.LastIndexOf('@');
            if (versionIndex > 6)
            {
                atTypesPackage = atTypesPackage[..versionIndex];
            }

            // Extract package name from @types/packageName
            var packageBaseName = atTypesPackage[7..];
            if (!_packageToFile.TryGetValue(packageBaseName, out var content))
            {
                throw new InvalidOperationException($"content not provided for {packageBaseName}");
            }

            _fs.WriteFile(cwd + "/node_modules/@types/" + packageBaseName + "/index.d.ts", content);
        }

        return [];
    }

    // Go: projecttestutil.SessionUtils.createTypesRegistryFileContent()/appendTypesRegistryConfig
    private string CreateTypesRegistryFileContent()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("{\n  \"entries\": {");
        var index = 0;
        foreach (var entry in _typesRegistry)
        {
            AppendTypesRegistryConfig(builder, index, entry);
            index++;
        }

        foreach (var key in _packageToFile.Keys)
        {
            if (!_typesRegistry.Contains(key))
            {
                AppendTypesRegistryConfig(builder, index, key);
                index++;
            }
        }

        builder.Append("\n  }\n}");
        return builder.ToString();
    }

    private static void AppendTypesRegistryConfig(System.Text.StringBuilder builder, int index, string entry)
    {
        if (index > 0)
        {
            builder.Append(',');
        }

        builder.Append($"\n    \"{entry}\": {{{AtaTestTypesRegistry.ConfigText()}\n    }}");
    }
}

/// <summary>Go: 测试里的 `project.ID()` 实现（session 传的是 `project.ID`，它满足 `ata.ProjectID`）。</summary>
internal sealed class AtaTestProjectID(string value) : ProjectID
{
    public string String()
    {
        return value;
    }
}

public class AtaTests
{
    /// <summary>
    /// 安装器级等价于 ata_test.go 的 "should install typings for unresolved imports"：
    /// init 装 types-registry + 建 package.json → discover 出 "jquery" → 装 `@types/jquery@latest` →
    /// 缓存并返回 typing 文件。
    /// </summary>
    [Fact]
    public void InstallTypingsInstallsTypingsForUnresolvedImports()
    {
        var fs = FromMap(new Dictionary<string, string>
        {
            ["/user/username/projects/project/app.js"] = "",
        }, false);

        var host = new AtaTestHost(
            fs,
            ["jquery"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["jquery"] = "declare const jquery: { x: number }",
            });

        var installer = TypingsInstaller.NewTypingsInstaller(
            new TypingsInstallerOptions
            {
                TypingsLocation = AtaTestConstants.TestTypingsLocation,
                ThrottleLimit = 5,
            },
            host);

        var logger = NewLogTree("AtaTests");
        var result = installer.InstallTypings(new TypingsInstallRequest
        {
            ProjectID = new AtaTestProjectID("/user/username/projects/project"),
            TypingsInfo = new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
                UnresolvedImports = NewSetFromItems("jquery"),
            },
            FileNames = ["/user/username/projects/project/app.js"],
            ProjectRootPath = "/user/username/projects/project",
            CompilerOptions = new CompilerOptions(),
            CurrentDirectory = "/",
            GetScriptKind = GetScriptKindFromFileName,
            FS = fs,
            Logger = logger,
        });

        // npm install 序列：先 types-registry，再 @types/jquery@latest
        Assert.Equal(2, host.NpmInstallCalls.Count);
        Assert.Equal(AtaTestConstants.TestTypingsLocation, host.NpmInstallCalls[0].Cwd);
        Assert.Equal(new[] {"install", "--ignore-scripts", "types-registry@latest"}, host.NpmInstallCalls[0].Args);
        Assert.Equal(AtaTestConstants.TestTypingsLocation, host.NpmInstallCalls[1].Cwd);
        Assert.Contains("@types/jquery@latest", host.NpmInstallCalls[1].Args);

        Assert.Equal(new[] {(AtaTestConstants.TestTypingsLocation + "/node_modules/@types/jquery/index.d.ts")},
            result.TypingsFiles);
        Assert.Equal(new[] {
                "/user/username/projects/project/bower_components",
                "/user/username/projects/project/node_modules",
            },
            result.FilesToWatch);

        // init 里 ensureTypingsLocationExists 写的 package.json
        Assert.True(fs.TryReadFile(AtaTestConstants.TestTypingsLocation + "/package.json", out var packageJson));
        Assert.Equal("{ \"private\": true }", packageJson);
    }

    /// <summary>
    /// 安装器级等价于 ata_test.go 的 "non-expired cache entry (inferred project, should not install typings)"：
    /// 缓存里的 @types/jquery@1.3.0 与 registry 的 latest(1.3.0) 相同 → 不重新安装，直接返回缓存的 typing 文件。
    /// </summary>
    [Fact]
    public void InstallTypingsDoesNotReinstallUpToDateCachedTypings()
    {
        const string typingFile = AtaTestConstants.TestTypingsLocation + "/node_modules/@types/jquery/index.d.ts";
        var fs = FromMap(new Dictionary<string, string>
        {
            ["/user/username/projects/project/app.js"] = "",
            ["/user/username/projects/project/package.json"] = """{"name":"test","dependencies":{"jquery":"^3.1.0"}}""",
            [typingFile] = "export const x = 10;",
            [AtaTestConstants.TestTypingsLocation + "/package.json"] = """{"dependencies":{"types-registry":"^0.1.317"},"devDependencies":{"@types/jquery":"^1.3.0"}}""",
            [AtaTestConstants.TestTypingsLocation + "/package-lock.json"] = """{"dependencies":{"@types/jquery":{"version":"1.3.0"}}}""",
        }, false);

        var host = new AtaTestHost(fs, ["jquery"], new Dictionary<string, string>(StringComparer.Ordinal));

        var installer = TypingsInstaller.NewTypingsInstaller(
            new TypingsInstallerOptions
            {
                TypingsLocation = AtaTestConstants.TestTypingsLocation,
                ThrottleLimit = 5,
            },
            host);

        var result = installer.InstallTypings(NewRequest(fs, "/user/username/projects/project"));

        // 只有 init 的 types-registry 一次安装，没有 @types/jquery
        Assert.Single(host.NpmInstallCalls);
        Assert.Equal(new[] {"install", "--ignore-scripts", "types-registry@latest"}, host.NpmInstallCalls[0].Args);
        Assert.Equal(new[] {typingFile}, result.TypingsFiles);

        // 已有内容不变
        Assert.True(fs.TryReadFile(typingFile, out var contents));
        Assert.Equal("export const x = 10;", contents);
    }

    /// <summary>
    /// 安装器级等价于 ata_test.go 的 "expired cache entry (inferred project, should install typings)"：
    /// 缓存里的 @types/jquery@1.0.0 落后于 registry 的 latest(1.3.0) → 重新安装并写回新内容。
    /// </summary>
    [Fact]
    public void InstallTypingsReinstallsExpiredCachedTypings()
    {
        const string typingFile = AtaTestConstants.TestTypingsLocation + "/node_modules/@types/jquery/index.d.ts";
        var fs = FromMap(new Dictionary<string, string>
        {
            ["/user/username/projects/project/app.js"] = "",
            ["/user/username/projects/project/package.json"] = """{"name":"test","dependencies":{"jquery":"^3.1.0"}}""",
            [typingFile] = "export const x = 10;",
            [AtaTestConstants.TestTypingsLocation + "/package.json"] = """{"dependencies":{"types-registry":"^0.1.317"},"devDependencies":{"@types/jquery":"^1.0.0"}}""",
            [AtaTestConstants.TestTypingsLocation + "/package-lock.json"] = """{"dependencies":{"@types/jquery":{"version":"1.0.0"}}}""",
        }, false);

        var host = new AtaTestHost(
            fs,
            ["jquery"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["jquery"] = "export const y = 10",
            });

        var installer = TypingsInstaller.NewTypingsInstaller(
            new TypingsInstallerOptions
            {
                TypingsLocation = AtaTestConstants.TestTypingsLocation,
                ThrottleLimit = 5,
            },
            host);

        var result = installer.InstallTypings(NewRequest(fs, "/user/username/projects/project"));

        Assert.Equal(2, host.NpmInstallCalls.Count);
        Assert.Contains("@types/jquery@latest", host.NpmInstallCalls[1].Args);
        Assert.Equal(new[] {typingFile}, result.TypingsFiles);

        // 内容被新装的 typings 覆盖
        Assert.True(fs.TryReadFile(typingFile, out var contents));
        Assert.Equal("export const y = 10", contents);
    }

    /// <summary>Go: ata_test.go 里 npmCalls[0].Cwd 的断言 + `IsKnownTypesPackageName` 的初始化语义。</summary>
    [Fact]
    public void IsKnownTypesPackageNameInitsRegistryAndValidatesName()
    {
        var fs = FromMap(new Dictionary<string, string>
        {
            ["/user/username/projects/project/app.js"] = "",
        }, false);

        var host = new AtaTestHost(fs, ["jquery"], new Dictionary<string, string>(StringComparer.Ordinal));
        var installer = TypingsInstaller.NewTypingsInstaller(
            new TypingsInstallerOptions
            {
                TypingsLocation = AtaTestConstants.TestTypingsLocation,
                ThrottleLimit = 5,
            },
            host);

        var logger = NewLogTree("IsKnownTypesPackageName");
        var projectId = new AtaTestProjectID("/user/username/projects/project");

        // 名字不合法（以 '.' 开头）→ 直接 false，且**不**触发 init（Go：先做 npm 包名校验再看 registry）
        Assert.False(installer.IsKnownTypesPackageName(projectId, ".foo", fs, logger));
        Assert.Empty(host.NpmInstallCalls);

        Assert.True(installer.IsKnownTypesPackageName(projectId, "jquery", fs, logger));
        Assert.Single(host.NpmInstallCalls);
        Assert.Equal(AtaTestConstants.TestTypingsLocation, host.NpmInstallCalls[0].Cwd);

        // init 只跑一次（sync.Once 语义）
        Assert.True(installer.IsKnownTypesPackageName(projectId, "jquery", fs, logger));
        Assert.Single(host.NpmInstallCalls);
    }

    // session.go 里构造 ata.TypingsInstallRequest 的字段取值：编译器选项/CWD/GetScriptKind 在 ata.go 里**未被读取**
    // （见 Ata.cs 文件头第 9 条），这里按 Go 的形状填上。
    private static TypingsInstallRequest NewRequest(FS fs, string projectRootPath)
    {
        return new TypingsInstallRequest
        {
            ProjectID = new AtaTestProjectID(projectRootPath),
            TypingsInfo = new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
            },
            FileNames = [projectRootPath + "/app.js"],
            ProjectRootPath = projectRootPath,
            CompilerOptions = new CompilerOptions(),
            CurrentDirectory = "/",
            GetScriptKind = GetScriptKindFromFileName,
            FS = fs,
            Logger = null,
        };
    }
}

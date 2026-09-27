// ported from: internal/project/configfileregistrybuilder.go（行为覆盖：配置发现、acquire/release 生命周期、
// didChangeFiles 的待重载状态机、Cleanup）+ 对 configfilechanges_test.go / customconfigfilename_test.go 中
// **不依赖 Session/projecttestutil** 的那部分确定性断言的等价覆盖。
//
// 与 Go 的偏离（测试层面）：
// - Go 的这些行为由 `projecttestutil.Setup(files)` + `session.DidOpenFile` 端到端驱动；本文件直接驱动
//   `ConfigFileRegistryBuilder`（配置注册层），因此不需要 Session/LanguageService/ATA 这些别的切片。
//   端到端用例见 tests/Project/ProjectCollectionBuilderTests.cs 与（待 M8Project 落地 harness 后的）项目级测试。
// - Go 的 `assert.Equal(t, want, got)` → xunit 的 `Assert.Equal(expected, actual)`。
// - 文件系统用本仓既有的 `TypeScript.Vfs.VfsTest.FromMap`（Go 用 `vfs/vfsmock` / projecttestutil 的 MapFS）。
using System.Collections.Generic;
using System.Linq;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.TsOptions;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.VfsTest;
using Xunit;

using Path = TypeScript.TSPath.Path;

using static TypeScript.TSPath.TSPath;

namespace TypeScript.Tests;

/// <summary>本项目切片测试用的最小 fixture（Go: projecttestutil 里与"只建 builder"等价的那一小部分）。</summary>
internal static class ProjectBuilderTestFixture
{
    internal static Func<string, Path> ToPathOf(string currentDirectory)
    {
        return fileName => TSPath.TSPath.ToPath(fileName, currentDirectory, useCaseSensitiveFileNames: true);
    }

    internal static (ConfigFileRegistryBuilder Builder, SnapshotFSBuilder Fs) NewConfigFileRegistryBuilder(
        Dictionary<string, string> files,
        ConfigFileRegistry? baseRegistry = null,
        string customConfigFileName = "",
        Func<Path, bool>? isOpenFile = null)
    {
        var toPath = ProjectBuilderTestFixture.ToPathOf("/");
        var hostFs = VfsTest.FromMap(files, useCaseSensitiveFileNames: true);
        var layered = OverlayFS.LayerOverlayFileSystem(hostFs, [], PositionEncodingKind.UTF16, toPath);
        var snapshotFs = SnapshotFSBuilder.NewSnapshotFSBuilderFromSource(layered, null, null, null, toPath);
        var sessionOptions = new SessionOptions { CurrentDirectory = "/" };
        var builder = ConfigFileRegistryBuilder.NewConfigFileRegistryBuilder(
            hasRelativePatternCapability: false,
            snapshotFs,
            isOpenFile ?? (_ => false),
            baseRegistry ?? new ConfigFileRegistry(),
            extendedConfigCache: ExtendedConfigCache.NewExtendedConfigCache(),
            snapshotID: 1,
            sessionOptions,
            customConfigFileName,
            logger: null);
        return (builder, snapshotFs);
    }
}

public class ConfigFileRegistryBuilderTests
{
    private static readonly Path _mainPath = new("/a/main.ts");
    private static readonly Path _tsconfigPath = new("/a/tsconfig.json");

    [Fact]
    public void FinalizeWithoutChangesReturnsBaseInstance()
    {
        var registry = new ConfigFileRegistry();
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder([], registry);

        // Go 的注释：“If no changes were made, it returns the original base registry.”
        Assert.Same(registry, builder.Finalize());
    }

    [Fact]
    public void DidChangeCustomConfigFileNameInvalidatesCacheAndFinalizesClone()
    {
        var registry = new ConfigFileRegistry { _customConfigFileName = "tsconfig.custom.json" };
        registry._configFileNames[_mainPath] = new ConfigFileNames { _nearestConfigFileName = "/a/tsconfig.custom.json" };
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(
            [],
            registry,
            customConfigFileName: "tsconfig.other.json");

        Assert.True(builder.DidChangeCustomConfigFileName(null));

        var finalized = builder.Finalize();
        Assert.NotSame(registry, finalized);
        Assert.Equal("tsconfig.other.json", finalized._customConfigFileName);
    }

    [Fact]
    public void DidChangeCustomConfigFileNameReturnsFalseWhenUnchanged()
    {
        var registry = new ConfigFileRegistry { _customConfigFileName = "tsconfig.custom.json" };
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(
            [],
            registry,
            customConfigFileName: "tsconfig.custom.json");

        Assert.False(builder.DidChangeCustomConfigFileName(null));
        Assert.Same(registry, builder.Finalize());
    }

    [Fact]
    public void ComputeConfigFileNamePrefersNearestAncestor()
    {
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = "{}",
            ["/a/b/jsconfig.json"] = "{}",
            ["/a/b/c/main.ts"] = "",
        });

        // /a/b/c 里没有配置；/a/b 里只有 jsconfig.json → 最近祖先就是它（tsconfig.json 优先只在**同目录**内比较）
        Assert.Equal("/a/b/jsconfig.json", builder.ComputeConfigFileName("/a/b/c/main.ts", skipSearchInDirectoryOfFile: false, null));
    }

    [Fact]
    public void ComputeConfigFileNamePrefersTsconfigInSameDirectory()
    {
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = "{}",
            ["/a/jsconfig.json"] = "{}",
            ["/a/main.ts"] = "",
        });

        Assert.Equal("/a/tsconfig.json", builder.ComputeConfigFileName("/a/main.ts", skipSearchInDirectoryOfFile: false, null));
    }

    [Fact]
    public void ComputeConfigFileNameUsesCustomConfigFileName()
    {
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(
            new Dictionary<string, string>
            {
                ["/a/tsconfig.json"] = "{}",
                ["/a/custom.json"] = "{}",
                ["/a/main.ts"] = "",
            },
            customConfigFileName: "custom.json");

        // Go: 提供 customConfigFileName 时优先用自定义配置名（即便目录里也有 tsconfig.json）
        Assert.Equal("/a/custom.json", builder.ComputeConfigFileName("/a/main.ts", skipSearchInDirectoryOfFile: false, null));
    }

    [Fact]
    public void ComputeConfigFileNameStopsAtNodeModules()
    {
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = "{}",
            ["/a/node_modules/pkg/main.ts"] = "",
        });

        // Go: 搜索到 "/node_modules" 目录即停止（不会用 workspace 根的 tsconfig.json 覆盖依赖里的文件）
        Assert.Equal("", builder.ComputeConfigFileName("/a/node_modules/pkg/main.ts", skipSearchInDirectoryOfFile: false, null));
    }

    [Fact]
    public void GetAncestorConfigFileNameSkipsConfigInSearchDirectory()
    {
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(
            new Dictionary<string, string>
            {
                ["/a/tsconfig.json"] = "{}",
                ["/a/b/tsconfig.json"] = "{}",
                ["/a/b/jsconfig.json"] = "{}",
                ["/a/b/main.ts"] = "",
            },
            isOpenFile: path => path == new Path("/a/b/main.ts"));

        Assert.Equal("/a/b/tsconfig.json", builder.GetConfigFileNameForFile("/a/b/main.ts", new Path("/a/b/main.ts"), null));

        // 祖先搜索：tsconfig.json 的祖先**跳过同目录的 tsconfig.json**，但仍检查同目录的 jsconfig.json
        Assert.Equal("/a/b/jsconfig.json", builder.GetAncestorConfigFileName("/a/b/main.ts", new Path("/a/b/main.ts"), "/a/b/tsconfig.json", null));

        // jsconfig.json 的祖先**同时跳过**同目录的 tsconfig.json 与 jsconfig.json
        Assert.Equal("/a/tsconfig.json", builder.GetAncestorConfigFileName("/a/b/main.ts", new Path("/a/b/main.ts"), "/a/b/jsconfig.json", null));
    }

    [Fact]
    public void AcquireConfigForFileParsesRetainsAndClones()
    {
        var files = new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = """{"compilerOptions":{"strict":true}}""",
            ["/a/main.ts"] = "const x = 1;",
        };
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(files, isOpenFile: path => path == _mainPath);

        var commandLine = builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null);
        Assert.NotNull(commandLine);
        Assert.Equal(["/a/main.ts"], commandLine.FileNames());

        var entry = builder._configs.Load(_tsconfigPath).Entry!.Value();
        Assert.Equal(PendingReload.PendingReloadNone, entry._pendingReload);
        Assert.True(entry._retainingOpenFiles!.Has(_mainPath));

        // 第二次 acquire：无待重载、无新增保留 ⇒ 不重新解析（同一实例）
        Assert.Same(commandLine, builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null));

        // Finalize 后的注册表能看到解析结果与配置名缓存
        var finalized = builder.Finalize();
        Assert.Same(commandLine, finalized.GetConfig(_tsconfigPath));
        Assert.True(finalized.IsTracked(_tsconfigPath));
    }

    [Fact]
    public void AcquireConfigForFileRecordsExtendsAsRetainingConfig()
    {
        var files = new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = """{"extends":"./base.json"}""",
            ["/a/base.json"] = """{"compilerOptions":{"strict":true}}""",
            ["/a/main.ts"] = "const x = 1;",
        };
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(files, isOpenFile: path => path == _mainPath);

        Assert.NotNull(builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null));

        var extendedEntries = new List<ConfigFileEntry>();
        builder._configs.Range(entry =>
        {
            if (entry.Key() != _tsconfigPath)
            {
                extendedEntries.Add(entry.Value());
            }

            return true;
        });
        var extended = Assert.Single(extendedEntries);
        Assert.True(extended._retainingConfigs!.Has(_tsconfigPath));

        // Cleanup：extended 条目被 retainingConfigs 保留（Go: len(retainingConfigs) > 0 时不删除）
        builder.Cleanup();
        Assert.True(builder.Finalize().IsTracked(new Path(extended._fileName)));
    }

    [Fact]
    public void CleanupRemovesUnretainedConfigAndKeepsRetainedProject()
    {
        var files = new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = "{}",
            ["/a/main.ts"] = "const x = 1;",
        };

        // 场景 1：只有 retainingProjects（用 test 辅助 API 模拟 acquireConfigForProject 的保留）
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(
            files,
            isOpenFile: path => path == _mainPath);
        builder.GetConfigFileNameForFile("/a/main.ts", _mainPath, null);
        builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null);
        builder.DidCloseFile(_mainPath);
        builder.Cleanup();
        Assert.False(builder.Finalize().IsTracked(_tsconfigPath));

        // 场景 2：项目保留后 Release（isOpenFile 为 false ⇒ 只有 retainingProjects 参与保留判定）
        var (builder2, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(files);
        builder2.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null);
        builder2.RetainConfigForProject(_tsconfigPath, "p1");
        builder2.Cleanup();
        Assert.True(builder2.Finalize().IsTracked(_tsconfigPath));

        builder2.ReleaseConfigForProject(_tsconfigPath, "p1");
        builder2.Cleanup();
        Assert.False(builder2.Finalize().IsTracked(_tsconfigPath));
    }

    [Fact]
    public void DidCloseFileDoesNotDeleteRegistryEntryWhileRetained()
    {
        var files = new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = "{}",
            ["/a/main.ts"] = "const x = 1;",
        };
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(files, isOpenFile: path => path == _mainPath);

        builder.GetConfigFileNameForFile("/a/main.ts", _mainPath, null);
        builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null);
        builder.DidCloseFile(_mainPath);

        var entry = builder._configs.Load(_tsconfigPath).Entry!.Value();
        Assert.Equal(0, entry._retainingOpenFiles!.Len());
        Assert.True(entry._retainingProjects is null || entry._retainingProjects.Len() == 0);

        // DidCloseFile 只释放保留；条目要等 Cleanup 才被删除
        Assert.True(builder.Finalize().IsTracked(_tsconfigPath));
    }

    [Fact]
    public void DidChangeFilesMarksConfigForFullReloadAndReloads()
    {
        var files = new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = """{"include":["*.ts"]}""",
            ["/a/main.ts"] = "const x = 1;",
        };
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(files, isOpenFile: path => path == _mainPath);

        var first = builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null);
        Assert.NotNull(first);

        var summary = new FileChangeSummary();
        summary.Changed.Add("file:///a/tsconfig.json");
        builder.DidChangeFiles(summary, null);

        // Go: handleConfigChange → pendingReload = PendingReloadFull
        Assert.Equal(PendingReload.PendingReloadFull, builder._configs.Load(_tsconfigPath).Entry!.Value()._pendingReload);

        // 再次 acquire 触发重新解析
        var reloaded = builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null);
        Assert.NotNull(reloaded);
        Assert.NotSame(first, reloaded);
        Assert.Equal(PendingReload.PendingReloadNone, builder._configs.Load(_tsconfigPath).Entry!.Value()._pendingReload);
        Assert.Equal(["/a/main.ts"], reloaded.FileNames());
    }

    [Fact]
    public void DidChangeFilesWithInvalidateAllChecksConfigTextForReloadKind()
    {
        var files = new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = """{"include":["*.ts"]}""",
            ["/a/main.ts"] = "const x = 1;",
        };
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(files, isOpenFile: path => path == _mainPath);
        builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null);

        var summary = new FileChangeSummary { InvalidateAll = true };
        builder.DidChangeFiles(summary, null);

        // Go 的 invalidateCache：文件文本与已解析的 ConfigFile.SourceFile.Text() 相同 ⇒ 只重载文件名
        Assert.Equal(PendingReload.PendingReloadFileNames, builder._configs.Load(_tsconfigPath).Entry!.Value()._pendingReload);
    }

    [Fact]
    public void DidChangeFilesCreatedConfigInvalidatesConfigFileNameCache()
    {
        var files = new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = "{}",
            ["/a/main.ts"] = "const x = 1;",
        };

        // 基线注册表里缓存了"没有配置"的陈旧结果（文件当时还没被 open 过 ⇒ nearestConfigFileName = ""）
        var staleRegistry = new ConfigFileRegistry();
        staleRegistry._configFileNames[_mainPath] = new ConfigFileNames { _nearestConfigFileName = "" };
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(
            files,
            staleRegistry,
            isOpenFile: path => path == _mainPath);

        Assert.Equal("", builder.GetConfigFileNameForFile("/a/main.ts", _mainPath, null));

        // Go: 创建 tsconfig.json/jsconfig.json 时按目录反查并删除受影响的 configFileNames 缓存条目
        var summary = new FileChangeSummary();
        summary.Created.Add("file:///a/tsconfig.json");
        var result = builder.DidChangeFiles(summary, null);

        Assert.NotNull(result._affectedFiles);
        Assert.True(result._affectedFiles!.Has(_mainPath));
        Assert.Equal("/a/tsconfig.json", builder.GetConfigFileNameForFile("/a/main.ts", _mainPath, null));
    }

    [Fact]
    public void ContentMapperManifestPathMatchesOnlyContributedPackages()
    {
        var commandLine = ParsedCommandLine.NewParsedCommandLine(
            new CompilerOptions(),
            [],
            null,
            new ComparePathsOptions(useCaseSensitiveFileNames: true, currentDirectory: "/"));
        commandLine.ParsedConfig.ContentMappers =
        [
            new ContentMapper.Mapper(new ContentMapper.Definition { Package = "vue", Extensions = ["vue"] }, new ContentMapper.Manifest())
            {
                PackageDirectory = "/node_modules/vue",
            },
        ];

        var toPath = ProjectBuilderTestFixture.ToPathOf("/");
        Assert.True(ConfigFileRegistryBuilder.ContentMapperManifestPath(commandLine, toPath, toPath("/node_modules/vue/package.json")));
        Assert.False(ConfigFileRegistryBuilder.ContentMapperManifestPath(commandLine, toPath, toPath("/node_modules/other/package.json")));
        Assert.False(ConfigFileRegistryBuilder.ContentMapperManifestPath(null, toPath, toPath("/node_modules/vue/package.json")));
    }

    [Fact]
    public void FSAndCurrentDirectoryComeFromSessionOptions()
    {
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder([]);

        Assert.Equal("/", builder.GetCurrentDirectory());
        Assert.NotNull(builder.FS());
        Assert.Equal("/", builder._sessionOptions.CurrentDirectory);
    }

    [Fact]
    public void ContentMappersReflectsLoadedConfigsAndInvalidation()
    {
        var (builder, _) = ProjectBuilderTestFixture.NewConfigFileRegistryBuilder(new Dictionary<string, string>
        {
            ["/a/tsconfig.json"] = """{"compilerOptions":{}}""",
            ["/a/main.ts"] = "const x = 1;",
        });
        builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null);

        Assert.Empty(builder.ContentMappers()._extensions);

        // Go 的 invalidateContentMappers：重新解析时会清缓存
        var summary = new FileChangeSummary();
        summary.Changed.Add("file:///a/tsconfig.json");
        builder.DidChangeFiles(summary, null);
        Assert.NotNull(builder.AcquireConfigForFile("/a/tsconfig.json", _tsconfigPath, _mainPath, null));
        Assert.Empty(builder.ContentMappers()._extensions);
    }
}

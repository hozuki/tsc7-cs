// ported from: internal/project/configfileregistry.go（**For testing** 部分）+ internal/project/extendedconfigcache_test.go 之外的
// 纯逻辑覆盖：Go 侧的 configfileregistry 没有独立 test 文件，它由 projecttestutil/session 级测试覆盖
// （见 tests/Project/ProjectTestUtil 的落地计划）。本文件覆盖 ConfigFileRegistry 本身的确定性行为：
// 查找、浅拷贝、Test* 辅助函数、以及 contentMapper 扩展名的收集（collectConfiguredContentMappers）。
//
// 与 Go 的偏离（测试层面）：
// - Go 的 `assert.Equal(t, want, got)` → xunit 的 `Assert.Equal(expected, actual)`（参数顺序相反）。
// - Go 直接读未导出字段 → C# 靠 InternalsVisibleTo("TypeScript.Compiler.Tests") 读 `_camelCase` 字段。
// - Go 的 `slices.Sort`/`maps.Keys` 的确定性在 C# 测试里用**排序后的投影**断言（集合顺序与 Go 一样不保证）。
using System.Collections.Generic;
using System.Linq;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Project;
using TypeScript.TsOptions;
using TypeScript.TSPath;
using Xunit;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public class ConfigFileRegistryTests
{
    private static ParsedCommandLine CommandLineWithMappers(params string[][] extensionSets)
    {
        var commandLine = ParsedCommandLine.NewParsedCommandLine(
            new CompilerOptions(),
            [],
            null,
            new ComparePathsOptions(useCaseSensitiveFileNames: true, currentDirectory: "/"));
        var mappers = new List<Mapper>();
        foreach (var extensions in extensionSets)
        {
            mappers.Add(new Mapper(new Definition { Extensions = extensions }, new Manifest()));
        }

        commandLine.ParsedConfig.ContentMappers = mappers.ToArray();
        return commandLine;
    }

    [Fact]
    public void CollectConfiguredContentMappersDedupesAndSorts()
    {
        var result = ConfigFileRegistry.CollectConfiguredContentMappers(
            [CommandLineWithMappers(["b", "a"]), CommandLineWithMappers(["c", "a"])]);

        Assert.Equal(["a", "b", "c"], result._extensions);
    }

    [Fact]
    public void CollectConfiguredContentMappersWithNoMappers()
    {
        var result = ConfigFileRegistry.CollectConfiguredContentMappers([CommandLineWithMappers()]);

        Assert.Empty(result._extensions);
    }

    [Fact]
    public void ContentMappersCollectsFromConfigsAndCaches()
    {
        var registry = new ConfigFileRegistry();
        registry._configs[new Path("/a/tsconfig.json")] = new ConfigFileEntry
        {
            _fileName = "/a/tsconfig.json",
            _commandLine = CommandLineWithMappers(["x"]),
        };

        var mappers = registry.ContentMappers();
        Assert.Equal(["x"], mappers._extensions);

        // Go: `contentMappers()` 只在 `allConfiguredContentMappers` 已设置时才复用（注册表自身不缓存计算结果，
        // 缓存发生在 builder 的 Finalize 里）⇒ 这里断言值相等，不断言同一实例。
        Assert.Equal(["x"], registry.ContentMappers()._extensions);
    }

    [Fact]
    public void ContentMappersUsesPrecomputedInstance()
    {
        var precomputed = new ConfiguredContentMappers { _extensions = ["pre"] };
        var registry = new ConfigFileRegistry { _allConfiguredContentMappers = precomputed };

        Assert.Same(precomputed, registry.ContentMappers());
    }

    [Fact]
    public void ConfigFileEntryFactory()
    {
        var entry = ConfigFileEntry.NewConfigFileEntry(hasRelativePatternCapability: true, "/a/tsconfig.json");

        Assert.Equal("/a/tsconfig.json", entry._fileName);
        Assert.Equal(PendingReload.PendingReloadFull, entry._pendingReload);
        Assert.Null(entry._commandLine);
        Assert.Null(entry._retainingProjects);
        Assert.NotNull(entry._rootFilesWatch);
    }

    [Fact]
    public void ExtendedConfigFileEntryFactory()
    {
        var extending = new Path("/a/tsconfig.json");
        var entry = ConfigFileEntry.NewExtendedConfigFileEntry("/a/base.json", extending);

        Assert.Equal("/a/base.json", entry._fileName);
        Assert.Equal(PendingReload.PendingReloadFull, entry._pendingReload);
        Assert.Null(entry._rootFilesWatch);
        Assert.NotNull(entry._retainingConfigs);
        Assert.True(entry._retainingConfigs.Has(extending));
    }

    [Fact]
    public void ConfigFileEntryCloneIsShallowButSetsAreIndependent()
    {
        var entry = ConfigFileEntry.NewConfigFileEntry(hasRelativePatternCapability: false, "/a/tsconfig.json");
        var retainingProjects = new Collections.Set<ID>();
        retainingProjects.Add("p1");
        entry._retainingProjects = retainingProjects;
        var openFile = new Path("/a/main.ts");
        var retainingOpenFiles = new Collections.Set<Path>();
        retainingOpenFiles.Add(openFile);
        entry._retainingOpenFiles = retainingOpenFiles;

        var clone = entry.Clone();

        Assert.Equal(entry._fileName, clone._fileName);
        Assert.Equal(entry._pendingReload, clone._pendingReload);
        Assert.Same(entry._commandLine, clone._commandLine);

        // !!! Go 的注释：maps.Clone 是**急切**克隆（避免共享可变 map）
        Assert.Same(entry._rootFilesWatch, clone._rootFilesWatch);
        Assert.True(clone._retainingProjects!.Has("p1"));
        Assert.True(clone._retainingOpenFiles!.Has(openFile));
        clone._retainingProjects.Add("p2");
        Assert.False(entry._retainingProjects.Has("p2"));
    }

    [Fact]
    public void RegistryLookups()
    {
        var configPath = new Path("/a/tsconfig.json");
        var commandLine = CommandLineWithMappers(["x"]);
        var filePath = new Path("/a/main.ts");
        var registry = new ConfigFileRegistry();
        registry._configs[configPath] = new ConfigFileEntry { _fileName = "/a/tsconfig.json", _commandLine = commandLine };
        registry._configFileNames[filePath] = new ConfigFileNames
        {
            _nearestConfigFileName = "/a/tsconfig.json",
            _ancestors = new Dictionary<string, string> { ["/a/tsconfig.json"] = "/tsconfig.json" },
        };

        Assert.Same(commandLine, registry.GetConfig(configPath));
        Assert.Null(registry.GetConfig(new Path("/b/tsconfig.json")));

        Assert.True(registry.IsTracked(configPath));
        Assert.False(registry.IsTracked(new Path("/b/tsconfig.json")));

        Assert.Equal("/a/tsconfig.json", registry.GetConfigFileName(filePath));
        Assert.Equal("", registry.GetConfigFileName(new Path("/b/main.ts")));

        Assert.Equal("/tsconfig.json", registry.GetAncestorConfigFileName(filePath, "/a/tsconfig.json"));
        Assert.Equal("", registry.GetAncestorConfigFileName(filePath, "/other/tsconfig.json"));
        Assert.Equal("", registry.GetAncestorConfigFileName(new Path("/b/main.ts"), "/a/tsconfig.json"));
    }

    [Fact]
    public void RegistryCloneCopiesDictionariesButSharesEntries()
    {
        var configPath = new Path("/a/tsconfig.json");
        var filePath = new Path("/a/main.ts");
        var entry = new ConfigFileEntry { _fileName = "/a/tsconfig.json" };
        var registry = new ConfigFileRegistry { _customConfigFileName = "tsconfig.custom.json" };
        registry._configs[configPath] = entry;
        registry._configFileNames[filePath] = new ConfigFileNames { _nearestConfigFileName = "/a/tsconfig.json" };

        var clone = registry.Clone();

        Assert.Equal(registry._customConfigFileName, clone._customConfigFileName);
        Assert.Same(entry, clone._configs[configPath]);

        // 浅拷贝：加删条目不互相影响（Go 的 maps.Clone）
        clone._configs.Remove(configPath);
        Assert.True(registry._configs.ContainsKey(configPath));
        clone._configs[new Path("/b/tsconfig.json")] = entry;
        Assert.False(registry._configs.ContainsKey(new Path("/b/tsconfig.json")));

        // configFileNames 的 Clone（configFileNames.Clone）也是急切克隆
        var namesClone = registry._configFileNames[filePath].Clone();
        Assert.Equal("/a/tsconfig.json", namesClone._nearestConfigFileName);
        namesClone._ancestors = new Dictionary<string, string> { ["/a/tsconfig.json"] = "/tsconfig.json" };
        Assert.Null(registry._configFileNames[filePath]._ancestors);
    }

    [Fact]
    public void TestHelpersExposeEntries()
    {
        var configPath = new Path("/a/tsconfig.json");
        var filePath = new Path("/a/main.ts");
        var retainingProjects = new Collections.Set<ID>();
        retainingProjects.Add("p1");
        var retainingOpenFiles = new Collections.Set<Path>();
        retainingOpenFiles.Add(filePath);
        var retainingConfigs = new Collections.Set<Path>();
        retainingConfigs.Add(new Path("/c/tsconfig.json"));

        var registry = new ConfigFileRegistry();
        registry._configs[configPath] = new ConfigFileEntry
        {
            _fileName = "/a/tsconfig.json",
            _retainingProjects = retainingProjects,
            _retainingOpenFiles = retainingOpenFiles,
            _retainingConfigs = retainingConfigs,
        };
        registry._configFileNames[filePath] = new ConfigFileNames
        {
            _nearestConfigFileName = "/a/tsconfig.json",
            _ancestors = new Dictionary<string, string> { ["/a/tsconfig.json"] = "/tsconfig.json" },
        };

        var entry = registry.GetTestConfigEntry(configPath);
        Assert.NotNull(entry);
        Assert.Equal("/a/tsconfig.json", entry.FileName);
        Assert.Equal(["p1"], entry.RetainingProjects.Select(id => id.Value).ToArray());
        Assert.Equal([filePath], entry.RetainingOpenFiles.ToArray());
        Assert.Equal([new Path("/c/tsconfig.json")], entry.RetainingConfigs.ToArray());
        Assert.Null(registry.GetTestConfigEntry(new Path("/b/tsconfig.json")));

        var visited = new List<(Path Path, string FileName)>();
        registry.ForEachTestConfigEntry((path, e) => visited.Add((path, e.FileName)));
        Assert.Equal([(configPath, "/a/tsconfig.json")], visited.ToArray());

        var namesEntry = registry.GetTestConfigFileNamesEntry(filePath);
        Assert.NotNull(namesEntry);
        Assert.Equal("/a/tsconfig.json", namesEntry.NearestConfigFileName);
        Assert.Equal("/tsconfig.json", namesEntry.Ancestors!["/a/tsconfig.json"]);
        Assert.Null(registry.GetTestConfigFileNamesEntry(new Path("/b/main.ts")));

        var namesVisited = new List<Path>();
        registry.ForEachTestConfigFileNamesEntry((path, _) => namesVisited.Add(path));
        Assert.Equal([filePath], namesVisited.ToArray());
    }
}

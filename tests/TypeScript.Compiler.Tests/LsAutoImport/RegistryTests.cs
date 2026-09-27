// ported from: internal/ls/autoimport/registry_test.go **的非 Session 部分**
//
// 与 Go 测试的差异（逐条）：
// - Go 的 `registry_test.go`（1,278 行）是**集成测试**：它经由 `internal/ls` 的 Session
//   （`session.GetCurrentLanguageServiceWithAutoImports`）+ `internal/project` + `testutil/{autoimporttestutil,
//   projecttestutil,contentmappertest}` 驱动真实语言服务。那些包（尤其 `internal/ls`）**尚未移植**，因此
//   本文件只覆盖不依赖 Session 的部分：注册表的桶/目录账本、BucketState、recursiveSearchSubset、
//   node_modules 包名枚举。**未移植的集成场景**（等价覆盖见括号）：
//     * "builds project and node_modules buckets"（= 本文件 CloneTracks… + BucketState 断言）
//     * "bucket does not rebuild on same-file change" / "…when new files added"（需要真实 program，
//       由 `BucketState.PossiblyNeedsRebuildForFile` 的单测 + 后续 ls 切片补）
//     * "package.json dependency changes invalidate node_modules buckets"（需要真实 program/resolver）
//     * "node_modules buckets get deleted when no open files can reference them"（= 本文件 CloneRemoves…）
//     * "deleting node_modules leaves the registry prepared for importing"（= 本文件 CloneRemoves…）
//     * "deleting a package directory inside node_modules invalidates the bucket"（需要 markBucketsDirty 的真实路径）
// - Go 的 `autoimporttestutil` 造出的 session/FS → 这里用 `vfstest.FromMap` + 一个最小的 `RegistryCloneHost`。
// - Go 的 `assert.Check`/`assert.Equal` → xunit 的 `Assert.*`；`Set` 无序 → 断言前显式按序数排序。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.LsUtil;
using TypeScript.PackageJson;
using TypeScript.Project.Logging;
using TypeScript.Vfs;
using Xunit;
using Path = TypeScript.TSPath.Path;
using static TypeScript.Collections.Collections;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class LsAutoImportRegistryTests
{
    /// <summary>测试用 ProjectID：值语义（Go 里 `project.ID` 是 string 类型，`map[ProjectID]` 按值比较）。</summary>
    internal sealed record FakeProjectID(string Id) : ProjectID
    {
        public string String() => Id;
    }

    private sealed class FakeCloneHost : RegistryCloneHost
    {
        private readonly FS _fs;

        private readonly InfoCache _packageJsonCache;

        private readonly ProjectID _projectId;

        public FakeCloneHost(FS fs, ProjectID projectId)
        {
            _fs = fs;
            _packageJsonCache = TypeScript.PackageJson.PackageJson.NewInfoCache("/", fs.UseCaseSensitiveFileNames());
            _projectId = projectId;
        }

        public FS FS() => _fs;

        public string GetCurrentDirectory() => "/";

        public (ProjectID Id, TypeScript.Compiler.Program? Program) GetDefaultProject(Path path) => (_projectId, null);

        public TypeScript.Compiler.Program? GetProgramForProject(ProjectID projectID) => null;

        public InfoCacheEntry? GetPackageJson(string fileName)
        {
            if (_packageJsonCache.Get(fileName) is { } existing)
            {
                return existing;
            }
            var directory = GetDirectoryPath(fileName);
            return _packageJsonCache.Set(fileName, new InfoCacheEntry
            {
                PackageDirectory = directory,
                DirectoryExists = _fs.DirectoryExists(directory),
                Contents = null,
            });
        }

        public SourceFile? GetSourceFile(string fileName, Path path) => null;

        public void Dispose()
        {
        }
    }

    private static RegistryChange OpenFileChange(params string[] fileNames)
    {
        var openFiles = new Dictionary<Path, string>();
        foreach (var fileName in fileNames)
        {
            openFiles[(Path)fileName] = fileName;
        }
        return new RegistryChange { OpenFiles = openFiles };
    }

    [Fact]
    public void CloneTracksNodeModulesDirectoriesAndBuckets()
    {
        var fs = FromMap(
            new Dictionary<string, object>
            {
                ["/proj/index.ts"] = "",
                ["/proj/node_modules/pkg/package.json"] = "{\"name\":\"pkg\"}",
                ["/proj/node_modules/pkg/index.d.ts"] = "export declare const x: number;",
            },
            true /*useCaseSensitiveFileNames*/);
        var host = new FakeCloneHost(fs, new FakeProjectID("proj"));
        var registry = AutoImport.NewRegistry(f => (Path)f, NewDefaultUserPreferences());

        var cloned = registry.Clone(Context.Background, OpenFileChange("/proj/index.ts"), host, null);

        Assert.NotNull(cloned);
        var stats = cloned!.GetCacheStats();
        var nodeModulesBucket = Assert.Single(stats.NodeModulesBuckets);
        Assert.Equal("/proj", nodeModulesBucket.Name);
        // 只创建了桶、还没建索引（`RequestedFile` 为空 ⇒ 不走 updateIndexes），所以桶是脏的且没有导出。
        Assert.True(nodeModulesBucket.State.Dirty());
        Assert.Equal(0, nodeModulesBucket.ExportCount);
        var projectBucket = Assert.Single(stats.ProjectBuckets);
        Assert.Equal("proj", projectBucket.Name);
        Assert.True(projectBucket.State.Dirty());

        // `BucketStats.Name` 是**桶的键**（= 含 node_modules 的那个目录，Go: `Name: string(path)`），
        // 不是 "…/node_modules"；后者只在 `NodeModulesDirectories()` 的键里出现。
        var dirs = cloned.NodeModulesDirectories();
        Assert.Equal(new[] { (Path)"/proj/node_modules" }, dirs.Keys.ToArray());
        Assert.Equal("/proj/node_modules", dirs[(Path)"/proj/node_modules"]);

        // 桶还没建好 ⇒ 不能算"已为自动导入准备好"。
        Assert.False(cloned.IsPreparedForImportingFile("/proj/index.ts", new FakeProjectID("proj"), NewDefaultUserPreferences()));
    }

    [Fact]
    public void CloneDropsNodeModulesBucketWhenDirectoryDisappears()
    {
        var withNodeModules = FromMap(
            new Dictionary<string, object>
            {
                ["/proj/index.ts"] = "",
                ["/proj/node_modules/pkg/package.json"] = "{\"name\":\"pkg\"}",
            },
            true /*useCaseSensitiveFileNames*/);
        var registry = AutoImport.NewRegistry(f => (Path)f, NewDefaultUserPreferences());
        var first = registry.Clone(
            Context.Background,
            OpenFileChange("/proj/index.ts"),
            new FakeCloneHost(withNodeModules, new FakeProjectID("proj")),
            null);
        Assert.Single(first.GetCacheStats().NodeModulesBuckets);

        // Simulate the user deleting node_modules: the same changeset, but the host FS no longer has it.
        var withoutNodeModules = FromMap(
            new Dictionary<string, object>
            {
                ["/proj/index.ts"] = "",
            },
            true /*useCaseSensitiveFileNames*/);
        var second = first.Clone(
            Context.Background,
            OpenFileChange("/proj/index.ts"),
            new FakeCloneHost(withoutNodeModules, new FakeProjectID("proj")),
            null);

        // The node_modules bucket should be removed entirely, not left behind as an empty bucket.
        Assert.Empty(second.GetCacheStats().NodeModulesBuckets);
        Assert.Empty(second.NodeModulesDirectories());
    }

    [Fact]
    public void CloneDropsProjectBucketWhenNoOpenFilesNeedIt()
    {
        var fs = FromMap(
            new Dictionary<string, object>
            {
                ["/proj/index.ts"] = "",
            },
            true /*useCaseSensitiveFileNames*/);
        var registry = AutoImport.NewRegistry(f => (Path)f, NewDefaultUserPreferences());
        var first = registry.Clone(
            Context.Background,
            OpenFileChange("/proj/index.ts"),
            new FakeCloneHost(fs, new FakeProjectID("proj")),
            null);
        Assert.Single(first.GetCacheStats().ProjectBuckets);

        // 没有任何打开的文件（也没有 RequestedFile）⇒ 项目桶被丢掉。
        var second = first.Clone(
            Context.Background,
            new RegistryChange(),
            new FakeCloneHost(fs, new FakeProjectID("proj")),
            null);
        Assert.Empty(second.GetCacheStats().ProjectBuckets);
    }

    [Fact]
    public void CacheStatsAreSortedByName()
    {
        var fs = FromMap(
            new Dictionary<string, object>
            {
                ["/b/index.ts"] = "",
                ["/a/index.ts"] = "",
                ["/a/node_modules/pkg/package.json"] = "{}",
                ["/b/node_modules/pkg/package.json"] = "{}",
            },
            true /*useCaseSensitiveFileNames*/);
        var host = new FakeCloneHost(fs, new FakeProjectID("proj"));
        var registry = AutoImport.NewRegistry(f => (Path)f, NewDefaultUserPreferences());
        var cloned = registry.Clone(
            Context.Background,
            OpenFileChange("/a/index.ts", "/b/index.ts"),
            host,
            null);

        var stats = cloned.GetCacheStats();
        // Go 的 GetCacheStats 用 slices.SortFunc(cmp.Compare(Name, …)) 排序 ⇒ 顺序确定；
        // Name 是 nodeModules 桶的键（含 node_modules 的目录）。
        Assert.Equal(new[] { "/a", "/b" }, stats.NodeModulesBuckets.Select(b => b.Name).ToArray());
        // `DependencyNames`/`PackageNames` 只在真正建桶时才有内容；这里只断言没有崩溃且集合为空。
        Assert.All(stats.NodeModulesBuckets, b => Assert.Empty((b.PackageNames ?? new Set<string>()).M));
    }

    [Fact]
    public void RecursiveSearchSubsetTreatsNullAsAllPackages()
    {
        // nil 代表"所有包"（任何具体集合的超集）。
        Assert.True(AutoImport.RecursiveSearchSubset(null, null));
        Assert.False(AutoImport.RecursiveSearchSubset(null, NewSetFromItems("a")));
        Assert.True(AutoImport.RecursiveSearchSubset(NewSetFromItems("a"), null));
        Assert.True(AutoImport.RecursiveSearchSubset(NewSetFromItems("a", "b"), NewSetFromItems("a", "b", "c")));
        Assert.True(AutoImport.RecursiveSearchSubset(NewSetFromItems("a"), NewSetFromItems("a")));
        Assert.False(AutoImport.RecursiveSearchSubset(NewSetFromItems("a", "d"), NewSetFromItems("a", "b")));
    }

    [Fact]
    public void BucketStateTracksDirtyFileAndPackages()
    {
        var clean = new BucketState();
        Assert.False(clean.Dirty());
        Assert.Equal(default, clean.DirtyFile());
        Assert.Null(clean.DirtyPackages());

        var singleFile = new BucketState { _dirtyFile = (Path)"/proj/a.ts" };
        Assert.True(singleFile.Dirty());
        Assert.Equal((Path)"/proj/a.ts", singleFile.DirtyFile());

        // multipleFilesDirty 让 DirtyFile/DirtyPackages 退回"整体重建"语义（Go: "" / nil）。
        var manyFiles = new BucketState { _dirtyFile = (Path)"/proj/a.ts", _multipleFilesDirty = true };
        Assert.True(manyFiles.Dirty());
        Assert.Equal(default, manyFiles.DirtyFile());
        Assert.Null(manyFiles.DirtyPackages());

        var dirtyPackages = new Set<string>();
        dirtyPackages.Add("pkg");
        var granular = new BucketState { _dirtyPackages = dirtyPackages };
        Assert.True(granular.Dirty());
        Assert.Same(dirtyPackages, granular.DirtyPackages());

        // newProgramStructure != false 也算脏（文件名集合变了）。
        var programStructure = new BucketState { _newProgramStructure = NewProgramStructure.DifferentFileNames };
        Assert.True(programStructure.Dirty());
        Assert.Equal(default, programStructure.DirtyFile());
    }

    [Fact]
    public void GetPackageNamesInNodeModulesSkipsDotDirectoriesAndUnscopesTypes()
    {
        var fs = FromMap(
            new Dictionary<string, object>
            {
                ["/proj/node_modules/foo/package.json"] = "{}",
                ["/proj/node_modules/@scope/bar/package.json"] = "{}",
                ["/proj/node_modules/@types/node/package.json"] = "{}",
                ["/proj/node_modules/.bin/foo"] = "",
            },
            true /*useCaseSensitiveFileNames*/);

        var names = AutoImport.GetPackageNamesInNodeModules("/proj/node_modules", fs)
            .Keys()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // `.bin` 被跳过；`@types/node` 归一成 "node"；`@scope/bar` 直接组合。
        Assert.Equal(new[] { "@scope/bar", "foo", "node" }, names);
    }

    [Fact]
    public void GetPackageNamesInNodeModulesOfMissingDirectoryIsEmpty()
    {
        var fs = FromMap(
            new Dictionary<string, object> { ["/proj/index.ts"] = "" },
            true /*useCaseSensitiveFileNames*/);

        var names = AutoImport.GetPackageNamesInNodeModules("/proj/node_modules", fs);
        Assert.Equal(0, names.Len());
    }
}

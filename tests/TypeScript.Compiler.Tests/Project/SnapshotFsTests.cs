// ported from: tsc/internal/project/snapshotfs_test.go:1-1558（确定性用例）
//
// 说明（与 Go 测试的差异，逐条）：
//
// - Go 的 `assert.Assert(t, x, "…")` / `assert.Equal(t, want, got)` → xunit 的 `Assert.True(x, "…")` /
//   `Assert.Equal(want, got)`（xunit 参数顺序是 (expected, actual)）；`assert.NilError` 无对应物
//   （`FS.Remove`/`FS.WriteFile` 失败抛异常，成功路径不需要断言）。
// - Go 的 `t.Run("…")` 子测试 → 每个子测试一个 `[Fact]`，方法名 = 父测试名 + 子测试名 PascalCase。
// - Go 的 `t.Parallel()` 不需要移植（xunit 的测试并行由 runner 配置决定，逐测试加 `t.Parallel` 不是行为）。
// - Go 的 `sync.WaitGroup` + `make(chan struct{})` 起跑栅栏 → `ManualResetEventSlim` + `Task[]` + `Task.WaitAll`。
// - Go 的 `map[tspath.Path]…` 索引（缺失时返回零值）→ C# 用 `TryGetValue`/辅助函数
//   `HasDirectory`/`HasChild`（缺失时 Go 的 `ok` 为 false、C# 会抛 KeyNotFoundException，故必须显式查）。
// - Go 的 `snapshot.cacheFiles[path]` / `builder.cacheFiles` 等未导出字段 → C# 的 internal 字段 `_cacheFiles` 等
//   （测试靠 Properties/AssemblyInfo.cs 的 InternalsVisibleTo("TypeScript.Compiler.Tests")）。
// - Go 的 `NewSetWithSizeHint`/`NewSetFromItems`/`Set` → `TypeScript.Collections.Collections` 的静态成员
//   （`using static`）与 `TypeScript.Collections.Set<T>`。
// - Go 的 `vfstest.FromMap(map[string]any{…}, false)`（值为 string 或 `vfstest.Symlink`）→
//   `VfsTest.FromMap(Dictionary<string, object>, bool)`；`vfstest.Symlink(target)` → `VfsTest.Symlink(target)`。
// - Go 测试里的 `snapshotFSBuilder.fs`（`fs.getAccessibleEntries` 等）→ C# 的 `builder._fs`。
//
// **未移植的用例（1 条）**：`TestAutoImportBuilderFS` 的 "symlink cache mismatch …" 原本构造的是
// `autoimport.go` 的 `autoImportBuilderFS`（它自己的 `untrackedFiles` 缓存才是被测对象）——该类型属于
// autoimport 切片。这里用**同一个 FS 场景**（同一 testFS、同一 symlink/realpath 路径）通过
// `SnapshotFSBuilder` 直接验证同一机制（缓存按符号链接路径记账 ⇒ 删除后按 realpath 读不到），
// 名字改为 `TestAutoImportBuilderFSSymlinkCacheMismatch`，并保留 Go 的分步注释。
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.Project.Dirty;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.VfsTest;
using Xunit;
using static TypeScript.Collections.Collections;
using Path = TypeScript.TSPath.Path;
using VfsFileInfo = TypeScript.Vfs.FileInfo;

namespace TypeScript.Tests;

public class ProjectSnapshotFsTests
{
    private static readonly Func<string, Path> _toPath = fileName => (Path)fileName;

    private static SnapshotFSBuilder NewSnapshotFSBuilder(
        FS fs,
        Dictionary<Path, Overlay> overlays,
        Dictionary<Path, CachedFile>? cacheFiles,
        Dictionary<Path, CloneableMap<Path, string>>? cacheDirectories,
        Dictionary<Path, RealpathAliasSet>? nodeModulesRealpathAliases)
    {
        var layeredFS = OverlayFS.LayerOverlayFileSystem(fs, overlays, PositionEncodingKind.UTF16, _toPath);
        return SnapshotFSBuilder.NewSnapshotFSBuilderFromSource(
            layeredFS,
            cacheFiles,
            cacheDirectories,
            nodeModulesRealpathAliases,
            _toPath);
    }

    /// <summary>Go 的 <c>newSnapshotFSBuilder(testFS, make(prevOverlays), make(overlays), make(cacheFiles), make(cacheDirectories), nil, …)</c>
    /// 里最常用的形态（Go 的 prevOverlays 参数在实现里被丢弃，这里同样不设）。</summary>
    private static SnapshotFSBuilder NewBuilder(FS fs)
    {
        return NewSnapshotFSBuilder(fs, [], [], [], null);
    }

    private static LayeredFileSystem NewTestLayeredFileSystem(FS fs)
    {
        return OverlayFS.NewOverlayFS(fs, [], PositionEncodingKind.UTF16, _toPath);
    }

    private static SnapshotFS NewSnapshot(FS fs, Dictionary<Path, Overlay>? overlays = null)
    {
        return new SnapshotFS
        {
            _toPath = _toPath,
            _fs = overlays is null ? NewTestLayeredFileSystem(fs) : OverlayFS.NewOverlayFS(fs, overlays, PositionEncodingKind.UTF16, _toPath),
            _cacheFiles = [],
            _cacheDirectories = [],
        };
    }

    private static bool HasDirectory(SnapshotFS snapshot, string directory)
    {
        return snapshot._cacheDirectories is not null && snapshot._cacheDirectories.ContainsKey((Path)directory);
    }

    private static bool HasChild(SnapshotFS snapshot, string directory, string child)
    {
        return snapshot._cacheDirectories is not null
            && snapshot._cacheDirectories.TryGetValue((Path)directory, out var dir)
            && dir.ContainsKey((Path)child);
    }

    private static bool HasCachedFile(SnapshotFS snapshot, string path)
    {
        return snapshot._cacheFiles is not null && snapshot._cacheFiles.ContainsKey((Path)path);
    }

    private static bool HasAlias(SnapshotFS snapshot, string realpath)
    {
        return snapshot._nodeModulesRealpathAliases is not null
            && snapshot._nodeModulesRealpathAliases.ContainsKey((Path)realpath);
    }

    private static RealpathAliasSet GetAlias(SnapshotFS snapshot, string realpath)
    {
        Assert.True(snapshot._nodeModulesRealpathAliases!.TryGetValue((Path)realpath, out var aliases));
        return aliases;
    }

    /// <summary>Go 测试里的 <c>countingHandleFileSystem</c>：内嵌 LayeredFileSystem 并覆盖
    /// `GetFile`/`GetFileByPath`/`ReadFile` 计数（C# 没有接口内嵌，改为持有被包装对象并逐个转发）。</summary>
    private sealed class CountingHandleFileSystem(LayeredFileSystem wrapped, string content) : LayeredFileSystem
    {
        internal string Content = content;

        internal int GetFileByPathCalls;

        internal int ReadFileCalls;

        public FileHandle? GetFile(string fileName)
        {
            return GetFileByPath(fileName, (Path)fileName);
        }

        public FileHandle? GetFileByPath(string fileName, Path path)
        {
            GetFileByPathCalls++;
            return CachedFile.NewCachedFileHandle(fileName, Content);
        }

        public bool TryReadFile(string path, out string contents)
        {
            ReadFileCalls++;
            contents = Content;
            return true;
        }

        public Dictionary<Path, Overlay> Overlays()
        {
            return wrapped.Overlays();
        }

        public bool UseCaseSensitiveFileNames()
        {
            return wrapped.UseCaseSensitiveFileNames();
        }

        public bool FileExists(string path)
        {
            return wrapped.FileExists(path);
        }

        public void WriteFile(string path, string data)
        {
            wrapped.WriteFile(path, data);
        }

        public void AppendFile(string path, string data)
        {
            wrapped.AppendFile(path, data);
        }

        public void Remove(string path)
        {
            wrapped.Remove(path);
        }

        public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
        {
            wrapped.Chtimes(path, accessTime, modificationTime);
        }

        public bool DirectoryExists(string path)
        {
            return wrapped.DirectoryExists(path);
        }

        public Entries GetAccessibleEntries(string path)
        {
            return wrapped.GetAccessibleEntries(path);
        }

        public VfsFileInfo? Stat(string path)
        {
            return wrapped.Stat(path);
        }

        public string Realpath(string path)
        {
            return wrapped.Realpath(path);
        }
    }

    [Fact]
    public void TestSnapshotFSBuilderCachesReturnedSourceHandle()
    {
        var fileSystem = new CountingHandleFileSystem(
            NewTestLayeredFileSystem(VfsTest.FromMap(new Dictionary<string, string>(), true)),
            "export const value = 1;");
        var builder = SnapshotFSBuilder.NewSnapshotFSBuilderFromSource(fileSystem, null, null, null, _toPath);

        var file = builder.GetFile("/src/index.ts");
        Assert.NotNull(file);
        Assert.Equal(fileSystem.Content, file.Content());
        Assert.Same(file, builder.GetFile("/src/index.ts"));
        Assert.Equal(1, fileSystem.GetFileByPathCalls);
        Assert.Equal(0, fileSystem.ReadFileCalls);
    }

    [Fact]
    public void TestSnapshotFSBuilderBuildsDirectoryTreeOnFileAdd()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "const foo = 1;" },
            false /* useCaseSensitiveFileNames */);

        var builder = NewBuilder(testFS);

        // Read the file to add it to the cacheFiles
        var fh = builder.GetFile("/src/foo.ts");
        Assert.True(fh is not null, "file should exist");
        Assert.Equal("const foo = 1;", fh.Content());

        // Finalize and check directories
        var (snapshot, changed) = builder.Finalize();
        Assert.True(changed, "should have changed");

        // Check that directory structure was built
        // /src should contain /src/foo.ts
        Assert.True(HasDirectory(snapshot, "/src"), "/src directory should exist");
        Assert.True(HasChild(snapshot, "/src", "/src/foo.ts"), "/src should contain /src/foo.ts");

        // / should contain /src
        Assert.True(HasDirectory(snapshot, "/"), "/ directory should exist");
        Assert.True(HasChild(snapshot, "/", "/src"), "/ should contain /src");
    }

    [Fact]
    public void TestSnapshotFSBuilderBuildsNestedDirectoryTree()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/nested/deep/file.ts"] = "export const x = 1;" },
            false /* useCaseSensitiveFileNames */);

        var builder = NewBuilder(testFS);

        // Read the file to add it to the cacheFiles
        var fh = builder.GetFile("/src/nested/deep/file.ts");
        Assert.True(fh is not null, "file should exist");

        var (snapshot, changed) = builder.Finalize();
        Assert.True(changed, "should have changed");

        // Check the complete directory tree
        Assert.True(HasChild(snapshot, "/src/nested/deep", "/src/nested/deep/file.ts"));
        Assert.True(HasChild(snapshot, "/src/nested", "/src/nested/deep"));
        Assert.True(HasChild(snapshot, "/src", "/src/nested"));
        Assert.True(HasChild(snapshot, "/", "/src"));
    }

    [Fact]
    public void TestSnapshotFSBuilderRemovesDirectoryEntriesOnFileDelete()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "const foo = 1;" },
            false /* useCaseSensitiveFileNames */);

        // Start with existing cacheFiles and directories
        var existingCacheFiles = new Dictionary<Path, CachedFile>
        {
            [(Path)"/src/foo.ts"] = CachedFile.NewCachedFile("/src/foo.ts", "const foo = 1;"),
        };
        var existingDirs = new Dictionary<Path, CloneableMap<Path, string>>
        {
            [(Path)"/"] = new CloneableMap<Path, string> { [(Path)"/src"] = "src" },
            [(Path)"/src"] = new CloneableMap<Path, string> { [(Path)"/src/foo.ts"] = "foo.ts" },
        };

        var builder = NewSnapshotFSBuilder(testFS, [], existingCacheFiles, existingDirs, null);

        // Mark the file for deletion by loading and deleting
        var (entry, ok) = builder._cacheFiles.Load((Path)"/src/foo.ts");
        if (ok)
        {
            entry!.Delete();
        }

        var (snapshot, changed) = builder.Finalize();
        Assert.True(changed, "should have changed");

        // File should be deleted
        Assert.False(HasCachedFile(snapshot, "/src/foo.ts"), "file should be deleted");

        // Directory tree should be cleaned up
        Assert.False(HasDirectory(snapshot, "/src"), "/src directory should be removed");
        Assert.False(HasDirectory(snapshot, "/"), "root directory should be removed");
    }

    [Fact]
    public void TestSnapshotFSBuilderRemovesOnlyEmptyDirectoriesOnFileDelete()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string>
            {
                ["/src/foo.ts"] = "const foo = 1;",
                ["/src/bar.ts"] = "const bar = 2;",
            },
            false /* useCaseSensitiveFileNames */);

        // Start with existing cacheFiles and directories
        var existingCacheFiles = new Dictionary<Path, CachedFile>
        {
            [(Path)"/src/foo.ts"] = CachedFile.NewCachedFile("/src/foo.ts", "const foo = 1;"),
            [(Path)"/src/bar.ts"] = CachedFile.NewCachedFile("/src/bar.ts", "const bar = 2;"),
        };
        var existingDirs = new Dictionary<Path, CloneableMap<Path, string>>
        {
            [(Path)"/"] = new CloneableMap<Path, string> { [(Path)"/src"] = "src" },
            [(Path)"/src"] = new CloneableMap<Path, string>
            {
                [(Path)"/src/foo.ts"] = "foo.ts",
                [(Path)"/src/bar.ts"] = "bar.ts",
            },
        };

        var builder = NewSnapshotFSBuilder(testFS, [], existingCacheFiles, existingDirs, null);

        // Delete only foo.ts
        var (entry, ok) = builder._cacheFiles.Load((Path)"/src/foo.ts");
        if (ok)
        {
            entry!.Delete();
        }

        var (snapshot, changed) = builder.Finalize();
        Assert.True(changed, "should have changed");

        // foo.ts should be deleted
        Assert.False(HasCachedFile(snapshot, "/src/foo.ts"), "foo.ts should be deleted");

        // bar.ts should still exist
        Assert.True(HasCachedFile(snapshot, "/src/bar.ts"), "bar.ts should still exist");

        // /src directory should still exist with bar.ts
        Assert.True(HasDirectory(snapshot, "/src"), "/src directory should still exist");
        Assert.False(HasChild(snapshot, "/src", "/src/foo.ts"), "/src should not contain foo.ts");
        Assert.True(HasChild(snapshot, "/src", "/src/bar.ts"), "/src should contain bar.ts");

        // root should still contain /src
        Assert.True(HasDirectory(snapshot, "/"), "root directory should still exist");
        Assert.True(HasChild(snapshot, "/", "/src"), "root should contain /src");
    }

    [Fact]
    public void TestSnapshotFSBuilderAddsFileToExistingDirectory()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string>
            {
                ["/src/foo.ts"] = "const foo = 1;",
                ["/src/bar.ts"] = "const bar = 2;",
            },
            false /* useCaseSensitiveFileNames */);

        // Start with existing file and directories
        var existingCacheFiles = new Dictionary<Path, CachedFile>
        {
            [(Path)"/src/foo.ts"] = CachedFile.NewCachedFile("/src/foo.ts", "const foo = 1;"),
        };
        var existingDirs = new Dictionary<Path, CloneableMap<Path, string>>
        {
            [(Path)"/"] = new CloneableMap<Path, string> { [(Path)"/src"] = "src" },
            [(Path)"/src"] = new CloneableMap<Path, string> { [(Path)"/src/foo.ts"] = "foo.ts" },
        };

        var builder = NewSnapshotFSBuilder(testFS, [], existingCacheFiles, existingDirs, null);

        // Read bar.ts to add it
        var fh = builder.GetFile("/src/bar.ts");
        Assert.True(fh is not null, "bar.ts should exist");

        var (snapshot, changed) = builder.Finalize();
        Assert.True(changed, "should have changed");

        // /src should contain both files
        Assert.True(HasChild(snapshot, "/src", "/src/foo.ts"), "/src should contain foo.ts");
        Assert.True(HasChild(snapshot, "/src", "/src/bar.ts"), "/src should contain bar.ts");
    }

    [Fact]
    public void TestSnapshotFSBuilderNoChangeWhenNoFilesAddedOrDeleted()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "const foo = 1;" },
            false /* useCaseSensitiveFileNames */);

        var existingCacheFiles = new Dictionary<Path, CachedFile>
        {
            [(Path)"/src/foo.ts"] = CachedFile.NewCachedFile("/src/foo.ts", "const foo = 1;"),
        };
        var existingDirs = new Dictionary<Path, CloneableMap<Path, string>>
        {
            [(Path)"/"] = new CloneableMap<Path, string> { [(Path)"/src"] = "src" },
            [(Path)"/src"] = new CloneableMap<Path, string> { [(Path)"/src/foo.ts"] = "foo.ts" },
        };

        var builder = NewSnapshotFSBuilder(testFS, [], existingCacheFiles, existingDirs, null);

        // Don't add or delete any files
        var (snapshot, changed) = builder.Finalize();
        Assert.False(changed, "should not have changed");

        // Directories should remain the same
        Assert.True(HasChild(snapshot, "/src", "/src/foo.ts"));
    }

    [Fact]
    public void TestSnapshotFSBuilderOverlayFilesAreReturnedOverDiskFiles()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "const foo = 1;" },
            false /* useCaseSensitiveFileNames */);

        var overlays = new Dictionary<Path, Overlay>
        {
            [(Path)"/src/foo.ts"] = new Overlay { _fileName = "/src/foo.ts", _content = "const foo = 999;" },
        };

        var builder = NewSnapshotFSBuilder(testFS, overlays, [], [], null);

        // Should return overlay content
        var fh = builder.GetFile("/src/foo.ts");
        Assert.NotNull(fh);
        Assert.Equal("const foo = 999;", fh.Content());
    }

    [Fact]
    public void TestSnapshotFSBuilderMultipleFilesAddedAndDeletedInSingleCycle()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string>
            {
                ["/src/a.ts"] = "const a = 1;",
                ["/src/b.ts"] = "const b = 2;",
                ["/lib/utils.ts"] = "export const util = 1;",
                ["/lib/helpers.ts"] = "export const helper = 1;",
                ["/other/single.ts"] = "const single = 1;",
            },
            false /* useCaseSensitiveFileNames */);

        // Start with some existing files
        var existingCacheFiles = new Dictionary<Path, CachedFile>
        {
            [(Path)"/src/a.ts"] = CachedFile.NewCachedFile("/src/a.ts", "const a = 1;"),
            [(Path)"/other/single.ts"] = CachedFile.NewCachedFile("/other/single.ts", "const single = 1;"),
        };
        var existingDirs = new Dictionary<Path, CloneableMap<Path, string>>
        {
            [(Path)"/"] = new CloneableMap<Path, string>
            {
                [(Path)"/src"] = "src",
                [(Path)"/other"] = "other",
            },
            [(Path)"/src"] = new CloneableMap<Path, string> { [(Path)"/src/a.ts"] = "a.ts" },
            [(Path)"/other"] = new CloneableMap<Path, string> { [(Path)"/other/single.ts"] = "single.ts" },
        };

        var builder = NewSnapshotFSBuilder(testFS, [], existingCacheFiles, existingDirs, null);

        // Add new files
        var fh = builder.GetFile("/src/b.ts");
        Assert.NotNull(fh);
        fh = builder.GetFile("/lib/utils.ts");
        Assert.NotNull(fh);
        fh = builder.GetFile("/lib/helpers.ts");
        Assert.NotNull(fh);

        // Delete existing files
        var (aEntry, hasA) = builder._cacheFiles.Load((Path)"/src/a.ts");
        if (hasA)
        {
            aEntry!.Delete();
        }

        var (singleEntry, hasSingle) = builder._cacheFiles.Load((Path)"/other/single.ts");
        if (hasSingle)
        {
            singleEntry!.Delete();
        }

        var (snapshot, changed) = builder.Finalize();
        Assert.True(changed, "should have changed");

        // Verify deleted files are gone
        Assert.False(HasCachedFile(snapshot, "/src/a.ts"), "/src/a.ts should be deleted");
        Assert.False(HasCachedFile(snapshot, "/other/single.ts"), "/other/single.ts should be deleted");

        // Verify added files exist
        Assert.True(HasCachedFile(snapshot, "/src/b.ts"), "/src/b.ts should exist");
        Assert.True(HasCachedFile(snapshot, "/lib/utils.ts"), "/lib/utils.ts should exist");
        Assert.True(HasCachedFile(snapshot, "/lib/helpers.ts"), "/lib/helpers.ts should exist");

        // Verify /other directory is cleaned up (was only entry deleted)
        Assert.False(HasDirectory(snapshot, "/other"), "/other directory should be removed");

        // Verify /src still exists with b.ts (a.ts deleted, b.ts added)
        Assert.True(HasDirectory(snapshot, "/src"), "/src directory should exist");
        Assert.False(HasChild(snapshot, "/src", "/src/a.ts"), "/src should not contain a.ts");
        Assert.True(HasChild(snapshot, "/src", "/src/b.ts"), "/src should contain b.ts");

        // Verify /lib was created with both files
        Assert.True(HasDirectory(snapshot, "/lib"), "/lib directory should exist");
        Assert.True(HasChild(snapshot, "/lib", "/lib/utils.ts"), "/lib should contain utils.ts");
        Assert.True(HasChild(snapshot, "/lib", "/lib/helpers.ts"), "/lib should contain helpers.ts");

        // Verify root contains /src and /lib but not /other
        Assert.True(HasChild(snapshot, "/", "/src"), "root should contain /src");
        Assert.True(HasChild(snapshot, "/", "/lib"), "root should contain /lib");
        Assert.False(HasChild(snapshot, "/", "/other"), "root should not contain /other");
    }

    [Fact]
    public void TestSnapshotFSBuilderOverlayDirectoriesAreComputedFromOverlays()
    {
        var testFS = VfsTest.FromMap(new Dictionary<string, string>(), false /* useCaseSensitiveFileNames */);

        var overlays = new Dictionary<Path, Overlay>
        {
            [(Path)"/src/overlay.ts"] = new Overlay { _fileName = "/src/overlay.ts", _content = "const x = 1;" },
            [(Path)"/src/nested/deep.ts"] = new Overlay { _fileName = "/src/nested/deep.ts", _content = "const y = 2;" },
        };

        var builder = NewSnapshotFSBuilder(testFS, overlays, [], [], null);

        var srcEntries = builder.GetAccessibleEntries("/src");
        Assert.Contains("overlay.ts", srcEntries.Files);
        Assert.Contains("nested", srcEntries.Directories);

        var nestedEntries = builder.GetAccessibleEntries("/src/nested");
        Assert.Contains("deep.ts", nestedEntries.Files);

        var rootEntries = builder.GetAccessibleEntries("/");
        Assert.Contains("src", rootEntries.Directories);
    }

    [Fact]
    public void TestSnapshotFSBuilderGetAccessibleEntriesCombinesDiskAndOverlay()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/disk.ts"] = "const disk = 1;" },
            false /* useCaseSensitiveFileNames */);

        var overlays = new Dictionary<Path, Overlay>
        {
            [(Path)"/src/overlay.ts"] = new Overlay { _fileName = "/src/overlay.ts", _content = "const overlay = 1;" },
        };

        var builder = NewSnapshotFSBuilder(testFS, overlays, [], [], null);

        var entries = builder.GetAccessibleEntries("/src");

        // Should contain both disk file and overlay file (both as basenames)
        Assert.Contains("disk.ts", entries.Files);
        Assert.Contains("overlay.ts", entries.Files);
    }

    [Fact]
    public async Task TestSnapshotFSBuilderGetAccessibleEntriesIsSafeUnderConcurrentCalls()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string>
            {
                ["/src/a.ts"] = "",
                ["/src/b.ts"] = "",
                ["/src/c.ts"] = "",
                ["/src/d.ts"] = "",
                ["/src/e.ts"] = "",
            },
            false /* useCaseSensitiveFileNames */);

        var overlays = new Dictionary<Path, Overlay>
        {
            [(Path)"/src/overlay.ts"] = new Overlay { _fileName = "/src/overlay.ts", _content = "" },
        };

        var builder = NewSnapshotFSBuilder(testFS, overlays, [], [], null);

        _ = builder._fs.GetAccessibleEntries("/src");

        using var start = new ManualResetEventSlim(false);
        var tasks = new Task[50];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                start.Wait();
                builder.GetAccessibleEntries("/src");
            });
        }

        start.Set();
        await Task.WhenAll(tasks);

        // Sanity: the overlay is still reported after all the concurrent calls.
        var entries = builder.GetAccessibleEntries("/src");
        Assert.Contains("overlay.ts", entries.Files);
    }

    [Fact]
    public void TestSnapshotFSGetFileReturnsOverlayFile()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "disk content" },
            false /* useCaseSensitiveFileNames */);

        var overlays = new Dictionary<Path, Overlay>
        {
            [(Path)"/src/foo.ts"] = new Overlay { _fileName = "/src/foo.ts", _content = "overlay content" },
        };

        var snapshot = NewSnapshot(testFS, overlays);

        var fh = snapshot.GetFile("/src/foo.ts");
        Assert.NotNull(fh);
        Assert.Equal("overlay content", fh.Content());
    }

    [Fact]
    public void TestSnapshotFSGetFileReturnsDiskFileWhenNotInOverlay()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "disk content" },
            false /* useCaseSensitiveFileNames */);

        var snapshot = new SnapshotFS
        {
            _toPath = _toPath,
            _fs = NewTestLayeredFileSystem(testFS),
            _cacheFiles = new Dictionary<Path, CachedFile>
            {
                [(Path)"/src/foo.ts"] = CachedFile.NewCachedFile("/src/foo.ts", "disk content"),
            },
            _cacheDirectories = [],
        };

        var fh = snapshot.GetFile("/src/foo.ts");
        Assert.NotNull(fh);
        Assert.Equal("disk content", fh.Content());
    }

    [Fact]
    public void TestSnapshotFSGetFileReadsFromFsWhenNotCached()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "fs content" },
            false /* useCaseSensitiveFileNames */);

        var snapshot = NewSnapshot(testFS);

        var fh = snapshot.GetFile("/src/foo.ts");
        Assert.NotNull(fh);
        Assert.Equal("fs content", fh.Content());
    }

    [Fact]
    public void TestSnapshotFSGetFileReturnsNilForNonExistentFile()
    {
        var testFS = VfsTest.FromMap(new Dictionary<string, string>(), false /* useCaseSensitiveFileNames */);

        var snapshot = NewSnapshot(testFS);

        var fh = snapshot.GetFile("/src/nonexistent.ts");
        Assert.True(fh is null, "should return nil for non-existent file");
    }

    [Fact]
    public void TestSnapshotFSIsOpenFileReturnsTrueForOverlays()
    {
        var testFS = VfsTest.FromMap(new Dictionary<string, string>(), false /* useCaseSensitiveFileNames */);

        var overlays = new Dictionary<Path, Overlay>
        {
            [(Path)"/src/foo.ts"] = new Overlay { _fileName = "/src/foo.ts", _content = "overlay content" },
        };

        var overlayFS = OverlayFS.NewOverlayFS(testFS, overlays, PositionEncodingKind.UTF16, _toPath);
        Assert.True(overlayFS.GetFile("/src/foo.ts")!.IsOverlay(), "overlay file should be open");
        Assert.True(overlayFS.GetFile("/src/bar.ts") is null, "non-overlay file should not be open");
    }

    [Fact]
    public void TestSnapshotFSGetFileByPathUsesProvidedPath()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "disk content" },
            false /* useCaseSensitiveFileNames */);

        var overlays = new Dictionary<Path, Overlay>
        {
            [(Path)"/src/foo.ts"] = new Overlay { _fileName = "/src/foo.ts", _content = "overlay content" },
        };
        var overlayFS = OverlayFS.NewOverlayFS(testFS, overlays, PositionEncodingKind.UTF16, _toPath);

        var snapshot = new SnapshotFS
        {
            _toPath = _toPath,
            _fs = overlayFS,
            _cacheFiles = [],
            _cacheDirectories = [],
        };

        // GetFileByPath should use the provided path directly
        var fh = snapshot.GetFileByPath("/src/foo.ts", (Path)"/src/foo.ts");
        Assert.NotNull(fh);
        Assert.Equal("overlay content", fh.Content());
    }

    [Fact]
    public void TestSnapshotFSGetAccessibleEntriesCombinesDiskAndOverlayDirectories()
    {
        var testFS = VfsTest.FromMap(new Dictionary<string, string>(), false /* useCaseSensitiveFileNames */);

        var overlays = new Dictionary<Path, Overlay>
        {
            [(Path)"/src/overlay.ts"] = new Overlay { _fileName = "/src/overlay.ts", _content = "overlay content" },
        };
        var overlayFS = OverlayFS.NewOverlayFS(testFS, overlays, PositionEncodingKind.UTF16, _toPath);
        var snapshot = new SnapshotFS
        {
            _toPath = _toPath,
            _fs = overlayFS,
            _cacheFiles = new Dictionary<Path, CachedFile>
            {
                [(Path)"/src/disk.ts"] = CachedFile.NewCachedFile("/src/disk.ts", "disk content"),
            },
            _cacheDirectories = new Dictionary<Path, CloneableMap<Path, string>>
            {
                [(Path)"/"] = new CloneableMap<Path, string> { [(Path)"/src"] = "src" },
                [(Path)"/src"] = new CloneableMap<Path, string> { [(Path)"/src/disk.ts"] = "disk.ts" },
            },
        };

        var entries = snapshot.GetAccessibleEntries("/src");

        // Should contain both disk file and overlay file (both as basenames)
        Assert.Contains("disk.ts", entries.Files);
        Assert.Contains("overlay.ts", entries.Files);
    }

    [Fact]
    public void TestSourceFSTracksFilesWhenTrackingEnabled()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "content" },
            false /* useCaseSensitiveFileNames */);

        var snapshot = NewSnapshot(testFS);

        var sourceFS = SourceFS.NewSourceFS(true /* tracking */, snapshot, _toPath);

        // File should not be seen yet
        Assert.False(sourceFS.SeenFile((Path)"/src/foo.ts"));

        // Read the file
        var fh = sourceFS.GetFile("/src/foo.ts");
        Assert.NotNull(fh);

        // Now it should be seen
        Assert.True(sourceFS.SeenFile((Path)"/src/foo.ts"));
    }

    [Fact]
    public void TestSourceFSDoesNotTrackFilesWhenTrackingDisabled()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "content" },
            false /* useCaseSensitiveFileNames */);

        var snapshot = NewSnapshot(testFS);

        var sourceFS = SourceFS.NewSourceFS(false /* tracking */, snapshot, _toPath);

        // Read the file
        var fh = sourceFS.GetFile("/src/foo.ts");
        Assert.NotNull(fh);

        // Should not be seen since tracking is disabled
        Assert.False(sourceFS.SeenFile((Path)"/src/foo.ts"));
    }

    [Fact]
    public void TestSourceFSDisableTrackingStopsTracking()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string>
            {
                ["/src/foo.ts"] = "content",
                ["/src/bar.ts"] = "content",
            },
            false /* useCaseSensitiveFileNames */);

        var snapshot = NewSnapshot(testFS);

        var sourceFS = SourceFS.NewSourceFS(true /* tracking */, snapshot, _toPath);

        // Read foo while tracking
        sourceFS.GetFile("/src/foo.ts");
        Assert.True(sourceFS.SeenFile((Path)"/src/foo.ts"));

        // Disable tracking
        sourceFS.DisableTracking();

        // Read bar after tracking disabled
        sourceFS.GetFile("/src/bar.ts");
        Assert.False(sourceFS.SeenFile((Path)"/src/bar.ts"));
    }

    [Fact]
    public void TestSourceFSFileExistsReturnsTrueForFilesInSource()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "content" },
            false /* useCaseSensitiveFileNames */);

        var snapshot = NewSnapshot(testFS);

        var sourceFS = SourceFS.NewSourceFS(false /* tracking */, snapshot, _toPath);

        Assert.True(sourceFS.FileExists("/src/foo.ts"));
        Assert.False(sourceFS.FileExists("/src/nonexistent.ts"));
    }

    [Fact]
    public void TestSourceFSReadFileReturnsContentForFilesInSource()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string> { ["/src/foo.ts"] = "file content" },
            false /* useCaseSensitiveFileNames */);

        var snapshot = NewSnapshot(testFS);

        var sourceFS = SourceFS.NewSourceFS(false /* tracking */, snapshot, _toPath);

        Assert.True(sourceFS.TryReadFile("/src/foo.ts", out var content));
        Assert.Equal("file content", content);

        Assert.False(sourceFS.TryReadFile("/src/nonexistent.ts", out _));
    }

    [Fact]
    public void TestAutoImportBuilderFSSymlinkCacheMismatch()
    {
        // Create a VFS with a real file and a symlinked directory pointing to it.
        // /real/pkg/index.d.ts is the real file.
        // /project/node_modules/pkg is a symlink to /real/pkg.
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/real/pkg/index.d.ts"] = "export declare const x: number;",
                ["/project/node_modules/pkg"] = VfsTest.Symlink("/real/pkg"),
            },
            true /* useCaseSensitiveFileNames */);

        // Verify symlink works as expected
        var symlinkPath = "/project/node_modules/pkg/index.d.ts";
        var realpathPath = testFS.Realpath(symlinkPath);
        Assert.True(realpathPath == "/real/pkg/index.d.ts", "Realpath should resolve the symlink to the real path");

        var builder = NewBuilder(testFS);

        // Step 1: Read the file via its symlink path (simulating what module resolution does
        // during FileExists). This caches the file in the builder's cache at the symlink path key.
        var fh = builder.GetFile(symlinkPath);
        Assert.True(fh is not null, "File should be readable via symlink path");
        Assert.Equal("export declare const x: number;", fh.Content());

        // Step 2: Simulate a file deletion from disk (e.g., npm install running concurrently).
        // This deletes both the real file and effectively breaks the symlink.
        testFS.Remove("/real/pkg/index.d.ts");

        // Step 3: Request the file by its realpath (simulating what GetSourceFile does after
        // the checker resolves the module). This bypasses the symlink-path cache entry because
        // the realpath has a different key.
        var fh2 = builder.GetFile(realpathPath);
        // The file was cached at the symlink path, but the realpath lookup misses the cache
        // and goes to disk where the file is now deleted. This returns nil.
        Assert.True(fh2 is null, "File should be nil when accessed by realpath after deletion from disk");
    }

    [Fact]
    public void TestRealpathAliasLifecycleAliasRecordedWhenReadingSymlinkedNodeModulesFile()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/packages/mylib/package.json"] = """{"name": "mylib", "main": "index.js"}""",
                ["/packages/mylib/index.d.ts"] = "export declare const x: number;",
                ["/project/node_modules/nolink/package.json"] = """{"name": "nolink"}""",
            },
            false);

        var builder = NewBuilder(testFS);

        // Read a file through the symlink — should record an alias.
        var fh = builder.GetFile("/project/node_modules/mylib/package.json");
        Assert.NotNull(fh);
        Assert.Equal("""{"name": "mylib", "main": "index.js"}""", fh.Content());

        // Read a non-symlinked node_modules file — should NOT record an alias.
        var fh2 = builder.GetFile("/project/node_modules/nolink/package.json");
        Assert.NotNull(fh2);

        var (snapshot, _) = builder.Finalize();

        // Alias exists for the symlinked file.
        var aliases = GetAlias(snapshot, "/packages/mylib/package.json");
        Assert.True(aliases._paths.Has((Path)"/project/node_modules/mylib/package.json"));

        // No alias for the non-symlinked file.
        Assert.False(HasAlias(snapshot, "/project/node_modules/nolink/package.json"), "no alias should exist for non-symlinked file");
    }

    [Fact]
    public void TestRealpathAliasLifecycleNoAliasRecordedForFilesOutsideNodeModules()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/link"] = VfsTest.Symlink("/elsewhere"),
                ["/elsewhere/index.ts"] = "export const x = 1;",
            },
            false);

        var builder = NewBuilder(testFS);

        var fh = builder.GetFile("/project/link/index.ts");
        Assert.NotNull(fh);

        var (snapshot, _) = builder.Finalize();
        Assert.True((snapshot._nodeModulesRealpathAliases?.Count ?? 0) == 0, "no aliases for non-node_modules symlinks");
    }

    [Fact]
    public void TestRealpathAliasLifecycleAliasesCarriedOverAcrossSnapshots()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/packages/mylib/package.json"] = """{"name": "mylib"}""",
            },
            false);

        // Build first snapshot.
        var builder1 = NewBuilder(testFS);
        builder1.GetFile("/project/node_modules/mylib/package.json");
        var (snapshot1, _) = builder1.Finalize();

        // Build second snapshot from the first, without reading the file again.
        var builder2 = NewSnapshotFSBuilder(
            testFS, [], snapshot1._cacheFiles, snapshot1._cacheDirectories, snapshot1._nodeModulesRealpathAliases);
        var (snapshot2, _) = builder2.Finalize();

        // Alias should still be present.
        var aliases = GetAlias(snapshot2, "/packages/mylib/package.json");
        Assert.True(aliases._paths.Has((Path)"/project/node_modules/mylib/package.json"), "alias should survive across snapshots");
    }

    [Fact]
    public void TestRealpathAliasLifecycleAliasPrunedWhenSymlinkedFileIsDeleted()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/packages/mylib/package.json"] = """{"name": "mylib"}""",
                ["/packages/mylib/index.d.ts"] = "export declare const x: number;",
            },
            false);

        // Build first snapshot — read both files.
        var builder1 = NewBuilder(testFS);
        builder1.GetFile("/project/node_modules/mylib/package.json");
        builder1.GetFile("/project/node_modules/mylib/index.d.ts");
        var (snapshot1, _) = builder1.Finalize();

        // Both should be aliased under the same realpath directory but separate files.
        Assert.True(HasAlias(snapshot1, "/packages/mylib/package.json"));
        Assert.True(HasAlias(snapshot1, "/packages/mylib/index.d.ts"));

        // Build second snapshot — delete one file via markDirtyFiles.
        var builder2 = NewSnapshotFSBuilder(
            testFS, [], snapshot1._cacheFiles, snapshot1._cacheDirectories, snapshot1._nodeModulesRealpathAliases);

        // Simulate deletion of index.d.ts from the disk file cache.
        var (entry, ok) = builder2._cacheFiles.Load((Path)"/project/node_modules/mylib/index.d.ts");
        if (ok)
        {
            entry!.Delete();
        }

        var (snapshot2, _) = builder2.Finalize();

        // package.json alias should remain.
        var aliases = GetAlias(snapshot2, "/packages/mylib/package.json");
        Assert.True(aliases._paths.Has((Path)"/project/node_modules/mylib/package.json"), "package.json alias should survive");

        // index.d.ts alias should be fully pruned (empty set → removed from map).
        Assert.False(HasAlias(snapshot2, "/packages/mylib/index.d.ts"), "index.d.ts alias should be pruned after deletion");
    }

    [Fact]
    public void TestRealpathAliasLifecycleMultipleSymlinksToSameRealpath()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/project/node_modules/alias"] = VfsTest.Symlink("/packages/mylib"),
                ["/packages/mylib/package.json"] = """{"name": "mylib"}""",
            },
            false);

        var builder = NewBuilder(testFS);

        // Read via both symlinks.
        var fh1 = builder.GetFile("/project/node_modules/mylib/package.json");
        Assert.NotNull(fh1);
        var fh2 = builder.GetFile("/project/node_modules/alias/package.json");
        Assert.NotNull(fh2);

        var (snapshot, _) = builder.Finalize();

        var aliases = GetAlias(snapshot, "/packages/mylib/package.json");
        Assert.True(aliases._paths.Has((Path)"/project/node_modules/mylib/package.json"));
        Assert.True(aliases._paths.Has((Path)"/project/node_modules/alias/package.json"));
    }

    [Fact]
    public void TestRealpathAliasLifecycleMultipleSymlinksPrunedIndividually()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/project/node_modules/alias"] = VfsTest.Symlink("/packages/mylib"),
                ["/packages/mylib/package.json"] = """{"name": "mylib"}""",
            },
            false);

        // Build first snapshot – read via both symlinks.
        var builder1 = NewBuilder(testFS);
        builder1.GetFile("/project/node_modules/mylib/package.json");
        builder1.GetFile("/project/node_modules/alias/package.json");
        var (snapshot1, _) = builder1.Finalize();

        // Build second snapshot – delete ONE of the symlink disk entries.
        var builder2 = NewSnapshotFSBuilder(
            testFS, [], snapshot1._cacheFiles, snapshot1._cacheDirectories, snapshot1._nodeModulesRealpathAliases);
        var (entry, ok) = builder2._cacheFiles.Load((Path)"/project/node_modules/alias/package.json");
        if (ok)
        {
            entry!.Delete();
        }

        var (snapshot2, _) = builder2.Finalize();

        // The realpath alias set should still exist, but only contain the surviving symlink.
        var aliases = GetAlias(snapshot2, "/packages/mylib/package.json");
        Assert.True(aliases._paths.Has((Path)"/project/node_modules/mylib/package.json"), "surviving symlink should remain");
        Assert.False(aliases._paths.Has((Path)"/project/node_modules/alias/package.json"), "deleted symlink should be pruned");
    }

    [Fact]
    public void TestRealpathAliasLifecycleExpandRealpathAliasesExpandsChangeEvents()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/packages/mylib/package.json"] = """{"name": "mylib"}""",
            },
            false);

        var builder = NewBuilder(testFS);
        builder.GetFile("/project/node_modules/mylib/package.json");
        var (snapshot, _) = builder.Finalize();

        // Simulate a watch event on the REALPATH.
        var change = new FileChangeSummary();
        change.Changed.Add("file:///packages/mylib/package.json");

        var expanded = snapshot.ExpandRealpathAliases(change);

        // Should now also contain the symlink path.
        Assert.True(expanded.Changed.Has("file:///packages/mylib/package.json"), "original event should remain");
        Assert.True(expanded.Changed.Has("file:///project/node_modules/mylib/package.json"), "symlink event should be added");
    }

    [Fact]
    public void TestRealpathAliasLifecycleExpandRealpathAliasesExpandsDeleteEvents()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/packages/mylib/package.json"] = """{"name": "mylib"}""",
            },
            false);

        var builder = NewBuilder(testFS);
        builder.GetFile("/project/node_modules/mylib/package.json");
        var (snapshot, _) = builder.Finalize();

        // Simulate a delete watch event on the REALPATH.
        var change = new FileChangeSummary();
        change.Deleted.Add("file:///packages/mylib/package.json");

        var expanded = snapshot.ExpandRealpathAliases(change);

        Assert.True(expanded.Deleted.Has("file:///project/node_modules/mylib/package.json"), "symlink deletion should be added");
    }

    [Fact]
    public void TestRealpathAliasLifecycleExpandRealpathAliasesIsNoOpWithNoAliases()
    {
        var snapshot = new SnapshotFS
        {
            _toPath = _toPath,
            _nodeModulesRealpathAliases = null,
        };

        var change = new FileChangeSummary();
        change.Changed.Add("file:///some/file.ts");

        var expanded = snapshot.ExpandRealpathAliases(change);
        Assert.Equal(1, expanded.Changed.Len());
        Assert.True(expanded.Changed.Has("file:///some/file.ts"));
    }

    [Fact]
    public void TestRealpathAliasLifecycleMarkDirtyFilesInvalidatesSymlinkedFileViaRealpathEvent()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/packages/mylib/package.json"] = """{"name": "mylib", "main": "index.js"}""",
            },
            false);

        // Build first snapshot — read the symlinked file.
        var builder1 = NewBuilder(testFS);
        var fh = builder1.GetFile("/project/node_modules/mylib/package.json");
        Assert.NotNull(fh);
        Assert.Equal("""{"name": "mylib", "main": "index.js"}""", fh.Content());
        var (snapshot1, _) = builder1.Finalize();

        // Modify the real file on disk.
        testFS.WriteFile("/packages/mylib/package.json", """{"name": "mylib"}""");

        // Build second snapshot — simulate realpath change event, expanded via aliases.
        var builder2 = NewSnapshotFSBuilder(
            testFS, [], snapshot1._cacheFiles, snapshot1._cacheDirectories, snapshot1._nodeModulesRealpathAliases);

        var change = new FileChangeSummary();
        change.Changed.Add("file:///packages/mylib/package.json");

        // Expand the realpath event to include the symlink path.
        change = snapshot1.ExpandRealpathAliases(change);
        // Now mark dirty — should find the file under the symlink key.
        builder2.MarkDirtyFiles(change);

        // Trigger reload by reading the file (simulates program construction).
        fh = builder2.GetFile("/project/node_modules/mylib/package.json");
        Assert.NotNull(fh);
        Assert.True(fh.Content() == """{"name": "mylib"}""", "builder should serve updated content after dirty marking");

        var (snapshot2, _) = builder2.Finalize();

        // The file should have been reloaded with new content.
        Assert.True(snapshot2._cacheFiles!.TryGetValue((Path)"/project/node_modules/mylib/package.json", out var file), "file should still be in cacheFiles");
        Assert.True(file.Content() == """{"name": "mylib"}""", "content should be updated");
    }

    [Fact]
    public void TestRealpathAliasLifecycleAliasCloneIsolationBetweenSnapshots()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/project/node_modules/other"] = VfsTest.Symlink("/packages/other"),
                ["/packages/mylib/package.json"] = """{"name": "mylib"}""",
                ["/packages/other/package.json"] = """{"name": "other"}""",
            },
            false);

        // Build first snapshot — read only mylib.
        var builder1 = NewBuilder(testFS);
        builder1.GetFile("/project/node_modules/mylib/package.json");
        var (snapshot1, _) = builder1.Finalize();

        // Build second snapshot — also read other.
        var builder2 = NewSnapshotFSBuilder(
            testFS, [], snapshot1._cacheFiles, snapshot1._cacheDirectories, snapshot1._nodeModulesRealpathAliases);
        builder2.GetFile("/project/node_modules/other/package.json");
        var (snapshot2, _) = builder2.Finalize();

        // snapshot1 should only have mylib alias.
        Assert.True(HasAlias(snapshot1, "/packages/mylib/package.json"), "snapshot1 should have mylib alias");
        Assert.False(HasAlias(snapshot1, "/packages/other/package.json"), "snapshot1 should NOT have other alias — it was added in a later snapshot");

        // snapshot2 should have both.
        Assert.True(HasAlias(snapshot2, "/packages/mylib/package.json"), "snapshot2 should have mylib alias");
        Assert.True(HasAlias(snapshot2, "/packages/other/package.json"), "snapshot2 should have other alias");
    }

    [Fact]
    public void TestRealpathAliasLifecycleAddingSymlinkToInheritedRealpathKeyDoesNotMutatePreviousSnapshot()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, object>
            {
                ["/project/node_modules/mylib"] = VfsTest.Symlink("/packages/mylib"),
                ["/project/node_modules/alias"] = VfsTest.Symlink("/packages/mylib"),
                ["/packages/mylib/package.json"] = """{"name": "mylib"}""",
            },
            false);

        // Snapshot 1: read via one symlink only.
        var builder1 = NewBuilder(testFS);
        builder1.GetFile("/project/node_modules/mylib/package.json");
        var (snapshot1, _) = builder1.Finalize();

        // Verify snapshot1 has exactly one alias for the realpath.
        var aliases1 = GetAlias(snapshot1, "/packages/mylib/package.json");
        Assert.Equal(1, aliases1._paths.Len());
        Assert.True(aliases1._paths.Has((Path)"/project/node_modules/mylib/package.json"));

        // Snapshot 2: read via the SECOND symlink, which maps to the same realpath.
        // This exercises the case where LoadOrStore finds the key in the base map
        // and must clone-on-write rather than mutating the shared set.
        var builder2 = NewSnapshotFSBuilder(
            testFS, [], snapshot1._cacheFiles, snapshot1._cacheDirectories, snapshot1._nodeModulesRealpathAliases);
        builder2.GetFile("/project/node_modules/alias/package.json");
        var (snapshot2, _) = builder2.Finalize();

        // Snapshot 2 should have both symlinks.
        var aliases2 = GetAlias(snapshot2, "/packages/mylib/package.json");
        Assert.Equal(2, aliases2._paths.Len());
        Assert.True(aliases2._paths.Has((Path)"/project/node_modules/mylib/package.json"));
        Assert.True(aliases2._paths.Has((Path)"/project/node_modules/alias/package.json"));

        // Snapshot 1 must NOT have been mutated — it should still have only one alias.
        Assert.Equal(1, aliases1._paths.Len());
        Assert.False(aliases1._paths.Has((Path)"/project/node_modules/alias/package.json"), "snapshot1 must not contain alias added in snapshot2");
    }

    [Fact]
    public void TestExpandAndFilterWatchEventsPreservesNodeModulesDirectoryDeletionEvenWhenUntracked()
    {
        // node_modules package files are read transiently and never tracked in
        // cacheDirectories, so a node_modules directory deletion can neither be
        // expanded nor matched by extension. It must still be preserved.
        var builder = NewBuilder(VfsTest.FromMap(
            new Dictionary<string, string> { ["/project/index.ts"] = "export const x = 1;" },
            false));

        var change = new FileChangeSummary();
        change.Deleted.Add("file:///project/node_modules");

        var expanded = builder.ExpandAndFilterWatchEvents(change, null, null, null, null);
        Assert.True(
            expanded.Deleted.Has("file:///project/node_modules"),
            "bare node_modules directory deletion should be preserved");
    }

    [Fact]
    public void TestExpandAndFilterWatchEventsPreservesDeletionOfPackageDirectoryInsideNodeModules()
    {
        var builder = NewBuilder(VfsTest.FromMap(
            new Dictionary<string, string> { ["/project/index.ts"] = "export const x = 1;" },
            false));

        var change = new FileChangeSummary();
        change.Deleted.Add("file:///project/node_modules/@scope/pkg");

        var expanded = builder.ExpandAndFilterWatchEvents(change, null, null, null, null);
        Assert.True(
            expanded.Deleted.Has("file:///project/node_modules/@scope/pkg"),
            "package directory deletion inside node_modules should be preserved");
    }

    [Fact]
    public void TestExpandAndFilterWatchEventsDropsIrrelevantUntrackedDeletionOutsideNodeModules()
    {
        var builder = NewBuilder(VfsTest.FromMap(
            new Dictionary<string, string> { ["/project/index.ts"] = "export const x = 1;" },
            false));

        var change = new FileChangeSummary();
        change.Deleted.Add("file:///project/build");

        var expanded = builder.ExpandAndFilterWatchEvents(change, null, null, null, null);
        Assert.Equal(0, expanded.Deleted.Len());
    }

    [Fact]
    public void TestExpandAndFilterWatchEventsPreservesExactContentMapperDependencies()
    {
        var builder = NewBuilder(VfsTest.FromMap(
            new Dictionary<string, string> { ["/project/index.ts"] = "export const x = 1;" },
            false));
        var watched = NewSetFromItems((Path)"/project/mapper.config");
        var change = new FileChangeSummary();
        change.Changed.Add("file:///project/mapper.config");
        change.Deleted.Add("file:///project/mapper.config");

        var expanded = builder.ExpandAndFilterWatchEvents(change, null, watched, null, null);
        Assert.True(expanded.Changed.Has("file:///project/mapper.config"));
        Assert.True(expanded.Deleted.Has("file:///project/mapper.config"));
    }

    [Fact]
    public void TestExpandAndFilterWatchEventsExpandsTrackedDirectoryDeletionIntoFileDeletions()
    {
        var existingCacheFiles = new Dictionary<Path, CachedFile>
        {
            [(Path)"/src/foo.ts"] = CachedFile.NewCachedFile("/src/foo.ts", "const foo = 1;"),
        };
        var existingDirs = new Dictionary<Path, CloneableMap<Path, string>>
        {
            [(Path)"/"] = new CloneableMap<Path, string> { [(Path)"/src"] = "src" },
            [(Path)"/src"] = new CloneableMap<Path, string> { [(Path)"/src/foo.ts"] = "foo.ts" },
        };
        var builder = NewSnapshotFSBuilder(
            VfsTest.FromMap(new Dictionary<string, string> { ["/src/foo.ts"] = "const foo = 1;" }, false),
            [],
            existingCacheFiles,
            existingDirs,
            null);

        var change = new FileChangeSummary();
        change.Deleted.Add("file:///src");

        var expanded = builder.ExpandAndFilterWatchEvents(change, null, null, null, null);
        Assert.True(
            expanded.Deleted.Has("file:///src/foo.ts"),
            "tracked directory deletion should expand to contained file deletions");
        Assert.False(
            expanded.Deleted.Has("file:///src"),
            "the directory URI itself should be replaced by its files");
    }
}

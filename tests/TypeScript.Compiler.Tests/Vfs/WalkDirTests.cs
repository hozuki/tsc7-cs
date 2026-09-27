// ported from: internal/vfs/walkdir_test.go
//
// 说明：
// - Go 的 `entry.Type() fs.FileMode`（ModeDir=1<<31 / 0 / ModeSymlink）在 C# 里没有等价物（vfs 与文本位置无关，
//   filesystem 侧只区分目录/普通文件/符号链接），断言改成 `entry.IsDirectory` / `entry.IsSymlink` 的组合，
//   逐条对应 Go 期望的 mode 值（ModeDir → (IsDirectory:true, IsSymlink:false)，0 → (false,false)，
//   ModeSymlink → (false,true)）。
// - `entry.Info()` 对符号链接返回只带 name+IsSymlink 的 FileInfo（Go 的 walkDirFileInfo），所以
//   `info.Mode()==ModeSymlink && !info.IsDir()` 可原样断言为 `info.IsSymlink && !info.IsDirectory`。
// - Go 的 `assert.DeepEqual(t, paths, []string{...})` → `Assert.Equal(new[]{...}, paths)`。
using TypeScript.Vfs;
using TypeScript.Vfs.WrapVfs;
using Xunit;
using static TypeScript.Vfs.Vfs;
using static TypeScript.Vfs.VfsTest.VfsTest;
using static TypeScript.Vfs.WrapVfs.WrapVfs;

namespace TypeScript.Tests;

public class VfsWalkDirTests
{
    [Fact]
    public void TestWalkDir()
    {
        var baseFs = FromMap(new Dictionary<string, string>
        {
            ["/root/a.ts"] = "",
            ["/root/dir/b.ts"] = "",
            ["/root/link/hidden.ts"] = "",
            ["/target/hidden.ts"] = "",
        }, true);

        var fileSystem = Wrap(baseFs, new Replacements
        {
            GetAccessibleEntries = path =>
            {
                var entries = baseFs.GetAccessibleEntries(path);
                entries.Symlinks = null;
                if (path == "/root")
                {
                    entries.Files = [.. entries.Files, "C:/foreign.ts"];
                }

                return entries;
            },
            Realpath = path => path switch
            {
                "/root/link" => "/target",
                "/root/link/hidden.ts" => "/target/hidden.ts",
                _ => path,
            },
        });

        var paths = new List<string>();
        var modes = new List<(bool IsDir, bool IsSymlink)>();
        WalkDir(fileSystem, "/root", (path, entry, err) =>
        {
            Assert.Null(err);
            paths.Add(path);
            modes.Add((entry!.IsDirectory, entry.IsSymlink));
            if (entry.IsSymlink)
            {
                var info = entry.Info();
                Assert.True(info.IsSymlink);
                Assert.False(info.IsDirectory);
            }

            return WalkDirResult.Ok;
        });

        Assert.Equal(["/root", "/root/a.ts", "/root/dir", "/root/dir/b.ts", "/root/link"], paths);
        Assert.Equal<(bool, bool)>(
            [(true, false), (false, false), (true, false), (false, false), (false, true)],
            modes);
    }

    [Fact]
    public void TestWalkDirDoesNotFollowRootSymlink()
    {
        var baseFs = FromMap(new Dictionary<string, string>
        {
            ["/root/link/hidden.ts"] = "",
            ["/target/hidden.ts"] = "",
        }, true);

        var fileSystem = Wrap(baseFs, new Replacements
        {
            GetAccessibleEntries = path =>
            {
                var entries = baseFs.GetAccessibleEntries(path);
                entries.Symlinks = null;
                return entries;
            },
            Realpath = path => path == "/root/link" ? "/target" : path,
        });

        var paths = new List<string>();
        WalkDir(fileSystem, "/root/link", (path, entry, err) =>
        {
            Assert.Null(err);
            paths.Add(path);
            Assert.True(entry!.IsSymlink);
            return WalkDirResult.Ok;
        });

        Assert.Equal(["/root/link"], paths);
    }

    [Fact]
    public void TestWalkDirReportsRootFileSymlink()
    {
        var baseFs = FromMap(new Dictionary<string, string> { ["/target/file.ts"] = "" }, true);

        var fileSystem = Wrap(baseFs, new Replacements
        {
            Stat = path => baseFs.Stat(path == "/root/link.ts" ? "/target/file.ts" : path),
            Realpath = path => path == "/root/link.ts" ? "/target/file.ts" : path,
        });

        WalkDir(fileSystem, "/root/link.ts", (path, entry, err) =>
        {
            Assert.Null(err);
            Assert.Equal("/root/link.ts", path);
            Assert.Equal("link.ts", entry!.Name);
            Assert.True(entry.IsSymlink);
            return WalkDirResult.Ok;
        });
    }

    [Fact]
    public void TestWalkDirSkipDir()
    {
        var fileSystem = FromMap(new Dictionary<string, string>
        {
            ["/root/a/hidden.ts"] = "",
            ["/root/b.ts"] = "",
        }, true);

        var paths = new List<string>();
        WalkDir(fileSystem, "/root", (path, entry, err) =>
        {
            Assert.Null(err);
            paths.Add(path);
            return path == "/root/a" ? WalkDirResult.SkipDir : WalkDirResult.Ok;
        });

        Assert.Equal(["/root", "/root/a", "/root/b.ts"], paths);
    }

    [Fact]
    public void TestWalkDirSkipAll()
    {
        var fileSystem = FromMap(new Dictionary<string, string>
        {
            ["/root/a.ts"] = "",
            ["/root/b.ts"] = "",
        }, true);

        var paths = new List<string>();
        WalkDir(fileSystem, "/root", (path, entry, err) =>
        {
            Assert.Null(err);
            paths.Add(path);
            return path == "/root/a.ts" ? WalkDirResult.SkipAll : WalkDirResult.Ok;
        });

        Assert.Equal(["/root", "/root/a.ts"], paths);
    }

    [Fact]
    public void TestWalkDirConsumesSkipDirForRootFile()
    {
        var fileSystem = FromMap(new Dictionary<string, string> { ["/root.ts"] = "" }, true);

        // 不抛异常即通过（SkipDir 会被 WalkDir 吞掉）。
        WalkDir(fileSystem, "/root.ts", (path, entry, err) =>
        {
            Assert.Null(err);
            return WalkDirResult.SkipDir;
        });
    }

    [Fact]
    public void TestWalkDirConsumesSkipForMissingRoot()
    {
        var fileSystem = FromMap(new Dictionary<string, string>(), true);

        foreach (var sentinel in new[] { WalkDirResult.SkipDir, WalkDirResult.SkipAll })
        {
            WalkDir(fileSystem, "/missing", (path, entry, err) =>
            {
                Assert.IsType<FileNotFoundException>(err);
                Assert.Null(entry);
                return sentinel;
            });
        }
    }

    [Fact]
    public void TestWalkDirUsesSymlinkMetadataWithoutRealpathCalls()
    {
        var baseFs = FromMap(new Dictionary<string, string> { ["/root/dir/file.ts"] = "" }, true);
        var realpathCalls = new List<string>();
        var fileSystem = Wrap(baseFs, new Replacements
        {
            Realpath = path =>
            {
                realpathCalls.Add(path);
                return path;
            },
        });

        WalkDir(fileSystem, "/root", (path, entry, err) => err != null ? WalkDirResult.FromError(err) : WalkDirResult.Ok);

        Assert.DoesNotContain("/root/dir", realpathCalls);
    }
}

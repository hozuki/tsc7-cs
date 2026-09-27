// ported from: internal/vfs/vfs_test.go
//
// Go 的这个文件只有 `BenchmarkReadFile`（两种 FS 读小文件/checker.ts 的吞吐对比），没有断言语义；
// 基准测试不移植，改编成等价的**行为**冒烟：同一份内容经 Osvfs（真实文件系统）与 VfsTest（内存 FS）
// 读出来必须一致（Go 的 bench 用例正是这两个 FS 读同一份数据）。
// 另外补了 TrackingVfs 的观测冒烟：Go 的 trackingvfs 没有测试文件，但它是 watch 模式依赖记录的唯一入口。
using TypeScript.Collections;
using TypeScript.Vfs.OsVfs;
using TypeScript.Vfs.TrackingVfs;
using Xunit;

using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class VfsSmokeTests
{
    [Fact]
    public void VfsAndOsvfsReadSameSmallData()
    {
        const string smallData = "hello, world";

        var dir = Directory.CreateTempSubdirectory("tsc-cs-vfs-smoke").FullName;
        try
        {
            var osPath = NormalizeSlashes(Path.Combine(dir, "foo.ts"));
            var osFs = Osvfs.FS();
            osFs.WriteFile(osPath, smallData);

            var mapFs = FromMap(new Dictionary<string, string> { ["/foo.ts"] = smallData }, true);

            Assert.True(osFs.TryReadFile(osPath, out var fromOs));
            Assert.True(mapFs.TryReadFile("/foo.ts", out var fromMap));
            Assert.Equal(smallData, fromOs);
            Assert.Equal(smallData, fromMap);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VfsAndOsvfsReadSameLargeFile()
    {
        // Go 的 bench 用 repo testdata 里的 checker.ts（~50k 行）；这里用生成的等价大文本，
        // 语义相同（两种 FS 必须读出逐字符一致的内容）。
        var contents = string.Join("\n", Enumerable.Range(0, 20_000).Select(i => $"export const x{i} = {i};"));

        var dir = Directory.CreateTempSubdirectory("tsc-cs-vfs-large").FullName;
        try
        {
            var osPath = NormalizeSlashes(Path.Combine(dir, "checker.ts"));
            var osFs = Osvfs.FS();
            osFs.WriteFile(osPath, contents);

            var mapFs = FromMap(new Dictionary<string, string> { ["/checker.ts"] = contents }, true);

            Assert.True(osFs.TryReadFile(osPath, out var fromOs));
            Assert.True(mapFs.TryReadFile("/checker.ts", out var fromMap));
            Assert.Equal(contents, fromOs);
            Assert.Equal(contents, fromMap);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TrackingVfsRecordsReadLikeOperationsOnly()
    {
        var inner = FromMap(new Dictionary<string, string> { ["/a/b.ts"] = "a" }, true);
        var fs = new TypeScript.Vfs.TrackingVfs.FS(inner, new SyncSet<string>());

        Assert.True(fs.TryReadFile("/a/b.ts", out var contents));
        Assert.Equal("a", contents);
        Assert.True(fs.FileExists("/a/b.ts"));
        Assert.True(fs.DirectoryExists("/a"));
        Assert.NotNull(fs.Stat("/a/b.ts"));
        _ = fs.GetAccessibleEntries("/");
        _ = fs.Realpath("/a/b.ts");

        // 写操作不是依赖，不记录（Go 的文件头注释）。
        fs.WriteFile("/c.ts", "c");

        var seen = fs.SeenFiles.ToSlice();
        Assert.Contains("/a/b.ts", seen);
        Assert.Contains("/a", seen);
        Assert.Contains("/", seen);
        Assert.DoesNotContain("/c.ts", seen);
    }
}

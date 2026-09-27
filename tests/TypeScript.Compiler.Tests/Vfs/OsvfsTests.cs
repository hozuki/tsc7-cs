// ported from: internal/vfs/osvfs/os_test.go, realpath_test.go, helpers_test.go
//
// 说明/偏离：
// - `repo.RootPath()/go.mod` 在本仓库不存在 → 读仓库根下真实存在的 `README.md`（用向上找 `TypeScript.slnx`
//   的辅助函数定位仓库根）；断言仍是"osvfs 读出的内容 == 直接用 System.IO 读出的内容"。
// - Go 的 `tb.Skip`（Windows 上无权限建文件符号链接时）→ xunit 2.9 的 SkipException 会被报成 FAIL，
//   本仓库既有惯例是 `Assert.Fail`，这里对"环境不支持建链接"用 `Assert.Fail` 并带上 Go 的原文消息。
// - `BenchmarkRealpath` 不移植（基准测试）。
// - Go 的 `assert.Equal(t, realpath, expected)` → `Assert.Equal(expected, realpath)`。
// - Go 的 mklink 在 Windows 上用 `cmd /c mklink /J`（junction，不需要管理员权限），Unix 用 os.Symlink。
using TypeScript.Vfs;
using TypeScript.Vfs.OsVfs;
using Xunit;

// 项目约定：不得 `using System.Diagnostics;`（会让 Debug.Fail 绑到 System.Diagnostics.Debug，见 Main 的公告）；
// 这里只需要这两个类型，用别名引入。
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

using static TypeScript.TSPath.TSPath;

namespace TypeScript.Tests;

public class OsvfsTests
{
    [Fact]
    public void TestOS_ReadFile()
    {
        var fs = Osvfs.FS();
        var readme = Path.Combine(RepoRoot(), "README.md");
        var readmePath = NormalizeSlashes(readme);

        var expected = File.ReadAllText(readme);
        Assert.True(fs.TryReadFile(readmePath, out var contents));
        Assert.Equal(expected, contents);
    }

    [Fact]
    public void TestOS_Realpath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(home));
        home = NormalizeSlashes(home);

        var expected = home;
        if (OperatingSystem.IsWindows())
        {
            // Windows 的盘符可以小写，但 realpath 总返回大写。
            expected = char.ToUpperInvariant(expected[0]) + expected[1..];
        }

        Assert.Equal(expected, Osvfs.FS().Realpath(home));
    }

    [Fact]
    public void TestOS_UseCaseSensitiveFileNames()
    {
        var fs = Osvfs.FS();

        // 只要求能调用（Go 的注释：just check that it works）。
        _ = fs.UseCaseSensitiveFileNames();

        if (OperatingSystem.IsWindows())
        {
            Assert.False(fs.UseCaseSensitiveFileNames());
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.True(fs.UseCaseSensitiveFileNames());
        }
    }

    [Fact]
    public void TestOS_ReadWriteRoundTrip()
    {
        var fs = Osvfs.FS();
        var dir = Directory.CreateTempSubdirectory("tsc-cs-osvfs").FullName;
        try
        {
            var file = NormalizeSlashes(Path.Combine(dir, "sub", "file.ts"));

            // WriteFile 会自动建父目录（Go 的 writeFileEnsuringDir）。
            fs.WriteFile(file, "hello, world");
            Assert.True(fs.FileExists(file));
            Assert.False(fs.DirectoryExists(file));
            Assert.True(fs.DirectoryExists(NormalizeSlashes(Path.Combine(dir, "sub"))));
            Assert.True(fs.TryReadFile(file, out var contents));
            Assert.Equal("hello, world", contents);

            fs.AppendFile(file, "!!");
            Assert.True(fs.TryReadFile(file, out contents));
            Assert.Equal("hello, world!!", contents);

            var info = fs.Stat(file);
            Assert.NotNull(info);
            Assert.False(info.IsDirectory);
            Assert.Equal("file.ts", info.Name);
            Assert.Equal(14, info.Size);

            var entries = fs.GetAccessibleEntries(NormalizeSlashes(Path.Combine(dir, "sub")));
            Assert.Equal(["file.ts"], entries.Files);
            Assert.Empty(entries.Directories);

            var mTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            fs.Chtimes(file, mTime, mTime);
            Assert.Equal(mTime, fs.Stat(file)!.ModifiedTimeUtc);

            fs.Remove(NormalizeSlashes(Path.Combine(dir, "sub")));
            Assert.False(fs.FileExists(file));
            Assert.False(fs.DirectoryExists(NormalizeSlashes(Path.Combine(dir, "sub"))));

            // 删除不存在的路径不报错（Go 的 os.RemoveAll）。
            fs.Remove(NormalizeSlashes(Path.Combine(dir, "sub")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("a.ts")]
    [InlineData("b.ts")]
    public void TestOS_GetAccessibleEntriesSortsAndClassifies(string extraFile)
    {
        var fs = Osvfs.FS();
        var dir = Directory.CreateTempSubdirectory("tsc-cs-osvfs-entries").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.ts"), "");
            File.WriteAllText(Path.Combine(dir, extraFile), "");
            Directory.CreateDirectory(Path.Combine(dir, "zdir"));

            var entries = fs.GetAccessibleEntries(NormalizeSlashes(dir));
            var files = new List<string>(entries.Files);
            files.Sort(StringComparer.Ordinal);

            Assert.Contains("a.ts", files);
            Assert.Contains(extraFile, files);
            Assert.Equal(["zdir"], entries.Directories);
            // Common.GetAccessibleEntries 总是给出（可能为空的）Symlinks 集合。
            Assert.NotNull(entries.Symlinks);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TestSymlinkRealpath()
    {
        var tmp = Directory.CreateTempSubdirectory("tsc-cs-osvfs-link").FullName;
        try
        {
            var target = Path.Combine(tmp, "target");
            var targetFile = Path.Combine(target, "file");
            var link = Path.Combine(tmp, "link");
            var linkFile = Path.Combine(link, "file");

            Directory.CreateDirectory(target);
            File.WriteAllText(targetFile, "hello");
            MkLink(target, link, isDir: true);

            Assert.Equal("hello", File.ReadAllText(linkFile));

            var fs = Osvfs.FS();
            var targetRealpath = fs.Realpath(NormalizeSlashes(targetFile));
            var linkRealpath = fs.Realpath(NormalizeSlashes(linkFile));
            Assert.Equal(targetRealpath, linkRealpath);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void TestOS_RemoveSymlinkRemovesLinkOnly()
    {
        // Go 的 os.RemoveAll 不跟随符号链接：删链接不会动目标（osvfs.Remove 的注释引用了 Go 的 #701 待办）。
        var fs = Osvfs.FS();
        var tmp = Directory.CreateTempSubdirectory("tsc-cs-osvfs-remove").FullName;
        try
        {
            var target = Path.Combine(tmp, "target");
            var link = Path.Combine(tmp, "link");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "file.ts"), "hello");
            MkLink(target, link, isDir: true);

            var linkPath = NormalizeSlashes(link);
            Assert.True(fs.DirectoryExists(linkPath));

            fs.Remove(linkPath);

            Assert.False(fs.DirectoryExists(linkPath));
            // 目标还在，内容没被删。
            Assert.True(fs.FileExists(NormalizeSlashes(Path.Combine(target, "file.ts"))));
            Assert.Equal("hello", File.ReadAllText(Path.Combine(target, "file.ts")));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void TestOS_GetAccessibleEntriesWithSymlinks()
    {
        var fs = Osvfs.FS();
        var tmp = Directory.CreateTempSubdirectory("tsc-cs-osvfs-entries-links").FullName;
        try
        {
            var target = Path.Combine(tmp, "target");
            var link = Path.Combine(tmp, "link");
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(link);

            var targetFile1 = Path.Combine(target, "file1");
            var targetFile2 = Path.Combine(target, "file2");
            File.WriteAllText(targetFile1, "hello");
            File.WriteAllText(targetFile2, "world");

            var targetDir1 = Path.Combine(target, "dir1");
            var targetDir2 = Path.Combine(target, "dir2");
            Directory.CreateDirectory(targetDir1);
            Directory.CreateDirectory(targetDir2);

            // Go 的 mklink(..., isDir=false) 在 Windows 上没有符号链接权限时是 tb.Skip；
            // xunit 2.9 的 SkipException 会被报成 FAIL（见 JsNum/JsTest.cs 的说明），这里同样直接返回。
            if (!TryMkLink(targetFile1, Path.Combine(link, "file1"), isDir: false))
            {
                return;
            }

            TryMkLink(targetFile2, Path.Combine(link, "file2"), isDir: false);
            MkLink(targetDir1, Path.Combine(link, "dir1"), isDir: true);
            MkLink(targetDir2, Path.Combine(link, "dir2"), isDir: true);

            var entries = fs.GetAccessibleEntries(NormalizeSlashes(link));

            // 符号链接会被跟随归类，但仍然记录在 Symlinks 里（Go 的 Common.GetAccessibleEntries）。
            Assert.Equal(["dir1", "dir2"], entries.Directories);
            Assert.Equal(["file1", "file2"], entries.Files);
            Assert.NotNull(entries.Symlinks);
            Assert.Equal(4, entries.Symlinks.Count);
            foreach (var name in new[] { "file1", "file2", "dir1", "dir2" })
            {
                Assert.Contains(name, entries.Symlinks);
            }

            // 没有符号链接的目录：Symlinks 非 null 但为空。
            var targetEntries = fs.GetAccessibleEntries(NormalizeSlashes(target));
            Assert.Equal(["dir1", "dir2"], targetEntries.Directories);
            Assert.Equal(["file1", "file2"], targetEntries.Files);
            Assert.NotNull(targetEntries.Symlinks);
            Assert.Empty(targetEntries.Symlinks);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    /// <summary>Go 的 helpers_test.go: mklink；建不出来（Windows 上缺符号链接权限）返回 false（= Go 的 tb.Skip）。</summary>
    private static bool TryMkLink(string target, string link, bool isDir)
    {
        try
        {
            MkLink(target, link, isDir);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void MkLink(string target, string link, bool isDir)
    {
        if (OperatingSystem.IsWindows() && isDir)
        {
            // Windows 上用 junction（mklink /J），不要用 os.Symlink：那会建"真"符号链接。
            using var process = Process.Start(new ProcessStartInfo("cmd", ["/c", "mklink", "/J", link, target])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            Assert.NotNull(process);
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            return;
        }

        try
        {
            if (isDir)
            {
                Directory.CreateSymbolicLink(link, target);
            }
            else
            {
                File.CreateSymbolicLink(link, target);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Go: !isDir && windows && "A required privilege is not held by the client" → tb.Skip
            Assert.Fail($"cannot create symlink (需要开发者模式/管理员权限，Go 侧此处是 tb.Skip): {e.Message}");
        }
    }

    /// <summary>Go 的 <c>repo.RootPath()</c> 的等价：从测试产物目录向上找 <c>TypeScript.slnx</c>。</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TypeScript.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail("failed to locate repo root (TypeScript.slnx)");
        return "";
    }
}

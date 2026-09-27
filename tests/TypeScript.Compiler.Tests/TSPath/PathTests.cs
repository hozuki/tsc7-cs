// ported from: internal/tspath/path_test.go
//
// 说明：
// - Go 的 Benchmark* 不移植。Go 的 Fuzz* 只取了它的种子语料（seed corpus），作为 [Theory] 用例逐一保留，
//   用来对照 Go 里那套"参考实现"（正则 / 旧算法）——这两条不变量检查是 Go 测试文件的真实内容。
// - 测试方法名与 Go 的 Test/Fuzz 名逐字对应（保留 Test 前缀，避免与 using static 引入的被测函数同名）。
// - 数据表里只服务 Benchmark 的 bench/benchmark 列没有测试意义，未搬运（其余列与期望值原样保留）。
// - path_test.go 里的 normalizePath_old 在 Go 侧也没有被任何用例引用（死代码），未移植。
using System.Text.RegularExpressions;
using TypeScript.TSPath;
using Xunit;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Tests;

public class TspathPathTests
{
    [Fact]
    public void TestNormalizeSlashes()
    {
        Assert.Equal("a", NormalizeSlashes("a"));
        Assert.Equal("a/b", NormalizeSlashes("a/b"));
        Assert.Equal("a/b", NormalizeSlashes("a\\b"));
        Assert.Equal("//server/path", NormalizeSlashes("\\\\server\\path"));
    }

    [Fact]
    public void TestGetRootLength()
    {
        Assert.Equal(0, GetRootLength("a"));
        Assert.Equal(1, GetRootLength("/"));
        Assert.Equal(1, GetRootLength("/path"));
        Assert.Equal(2, GetRootLength("c:"));
        Assert.Equal(0, GetRootLength("c:d"));
        Assert.Equal(3, GetRootLength("c:/"));
        Assert.Equal(3, GetRootLength("c:\\"));
        Assert.Equal(8, GetRootLength("//server"));
        Assert.Equal(9, GetRootLength("//server/share"));
        Assert.Equal(8, GetRootLength("\\\\server"));
        Assert.Equal(9, GetRootLength("\\\\server\\share"));
        Assert.Equal(8, GetRootLength("file:///"));
        Assert.Equal(8, GetRootLength("file:///path"));
        Assert.Equal(10, GetRootLength("file:///c:"));
        Assert.Equal(8, GetRootLength("file:///c:d"));
        Assert.Equal(11, GetRootLength("file:///c:/path"));
        Assert.Equal(12, GetRootLength("file:///c%3a"));
        Assert.Equal(8, GetRootLength("file:///c%3ad"));
        Assert.Equal(13, GetRootLength("file:///c%3a/path"));
        Assert.Equal(12, GetRootLength("file:///c%3A"));
        Assert.Equal(8, GetRootLength("file:///c%3Ad"));
        Assert.Equal(13, GetRootLength("file:///c%3A/path"));
        Assert.Equal(16, GetRootLength("file://localhost"));
        Assert.Equal(17, GetRootLength("file://localhost/"));
        Assert.Equal(17, GetRootLength("file://localhost/path"));
        Assert.Equal(19, GetRootLength("file://localhost/c:"));
        Assert.Equal(17, GetRootLength("file://localhost/c:d"));
        Assert.Equal(20, GetRootLength("file://localhost/c:/path"));
        Assert.Equal(21, GetRootLength("file://localhost/c%3a"));
        Assert.Equal(17, GetRootLength("file://localhost/c%3ad"));
        Assert.Equal(22, GetRootLength("file://localhost/c%3a/path"));
        Assert.Equal(21, GetRootLength("file://localhost/c%3A"));
        Assert.Equal(17, GetRootLength("file://localhost/c%3Ad"));
        Assert.Equal(22, GetRootLength("file://localhost/c%3A/path"));
        Assert.Equal(13, GetRootLength("file://server"));
        Assert.Equal(14, GetRootLength("file://server/"));
        Assert.Equal(14, GetRootLength("file://server/path"));
        Assert.Equal(14, GetRootLength("file://server/c:"));
        Assert.Equal(14, GetRootLength("file://server/c:d"));
        Assert.Equal(14, GetRootLength("file://server/c:/d"));
        Assert.Equal(14, GetRootLength("file://server/c%3a"));
        Assert.Equal(14, GetRootLength("file://server/c%3ad"));
        Assert.Equal(14, GetRootLength("file://server/c%3a/d"));
        Assert.Equal(14, GetRootLength("file://server/c%3A"));
        Assert.Equal(14, GetRootLength("file://server/c%3Ad"));
        Assert.Equal(14, GetRootLength("file://server/c%3A/d"));
        Assert.Equal(13, GetRootLength("http://server"));
        Assert.Equal(14, GetRootLength("http://server/path"));
    }

    [Fact]
    public void TestPathIsAbsolute()
    {
        // POSIX
        Assert.True(PathIsAbsolute("/path/to/file.ext"));
        // DOS
        Assert.True(PathIsAbsolute("c:/path/to/file.ext"));
        // URL
        Assert.True(PathIsAbsolute("file:///path/to/file.ext"));
        // Non-absolute
        Assert.False(PathIsAbsolute("path/to/file.ext"));
        Assert.False(PathIsAbsolute("./path/to/file.ext"));
    }

    [Fact]
    public void TestIsUrl()
    {
        Assert.False(IsUrl("a"));
        Assert.False(IsUrl("/"));
        Assert.False(IsUrl("c:"));
        Assert.False(IsUrl("c:d"));
        Assert.False(IsUrl("c:/"));
        Assert.False(IsUrl("c:\\"));
        Assert.False(IsUrl("//server"));
        Assert.False(IsUrl("//server/share"));
        Assert.False(IsUrl("\\\\server"));
        Assert.False(IsUrl("\\\\server\\share"));

        Assert.True(IsUrl("file:///path"));
        Assert.True(IsUrl("file:///c:"));
        Assert.True(IsUrl("file:///c:d"));
        Assert.True(IsUrl("file:///c:/path"));
        Assert.True(IsUrl("file://server"));
        Assert.True(IsUrl("file://server/path"));
        Assert.True(IsUrl("http://server"));
        Assert.True(IsUrl("http://server/path"));
    }

    [Fact]
    public void TestIsRootedDiskPath()
    {
        Assert.False(IsRootedDiskPath("a"));
        Assert.True(IsRootedDiskPath("/"));
        Assert.True(IsRootedDiskPath("c:"));
        Assert.False(IsRootedDiskPath("c:d"));
        Assert.True(IsRootedDiskPath("c:/"));
        Assert.True(IsRootedDiskPath("c:\\"));
        Assert.True(IsRootedDiskPath("//server"));
        Assert.True(IsRootedDiskPath("//server/share"));
        Assert.True(IsRootedDiskPath("\\\\server"));
        Assert.True(IsRootedDiskPath("\\\\server\\share"));
        Assert.False(IsRootedDiskPath("file:///path"));
        Assert.False(IsRootedDiskPath("file:///c:"));
        Assert.False(IsRootedDiskPath("file:///c:d"));
        Assert.False(IsRootedDiskPath("file:///c:/path"));
        Assert.False(IsRootedDiskPath("file://server"));
        Assert.False(IsRootedDiskPath("file://server/path"));
        Assert.False(IsRootedDiskPath("http://server"));
        Assert.False(IsRootedDiskPath("http://server/path"));
    }

    [Fact]
    public void TestGetDirectoryPath()
    {
        Assert.Equal("", GetDirectoryPath(""));
        Assert.Equal("", GetDirectoryPath("a"));
        Assert.Equal("a", GetDirectoryPath("a/b"));
        Assert.Equal("/", GetDirectoryPath("/"));
        Assert.Equal("/", GetDirectoryPath("/a"));
        Assert.Equal("/", GetDirectoryPath("/a/"));
        Assert.Equal("/a", GetDirectoryPath("/a/b"));
        Assert.Equal("/a", GetDirectoryPath("/a/b/"));
        Assert.Equal("c:", GetDirectoryPath("c:"));
        Assert.Equal("", GetDirectoryPath("c:d"));
        Assert.Equal("c:/", GetDirectoryPath("c:/"));
        Assert.Equal("c:/", GetDirectoryPath("c:/path"));
        Assert.Equal("c:/", GetDirectoryPath("c:/path/"));
        Assert.Equal("//server", GetDirectoryPath("//server"));
        Assert.Equal("//server/", GetDirectoryPath("//server/"));
        Assert.Equal("//server/", GetDirectoryPath("//server/share"));
        Assert.Equal("//server/", GetDirectoryPath("//server/share/"));
        Assert.Equal("//server", GetDirectoryPath("\\\\server"));
        Assert.Equal("//server/", GetDirectoryPath("\\\\server\\"));
        Assert.Equal("//server/", GetDirectoryPath("\\\\server\\share"));
        Assert.Equal("//server/", GetDirectoryPath("\\\\server\\share\\"));
        Assert.Equal("file:///", GetDirectoryPath("file:///"));
        Assert.Equal("file:///", GetDirectoryPath("file:///path"));
        Assert.Equal("file:///", GetDirectoryPath("file:///path/"));
        Assert.Equal("file:///c:", GetDirectoryPath("file:///c:"));
        Assert.Equal("file:///", GetDirectoryPath("file:///c:d"));
        Assert.Equal("file:///c:/", GetDirectoryPath("file:///c:/"));
        Assert.Equal("file:///c:/", GetDirectoryPath("file:///c:/path"));
        Assert.Equal("file:///c:/", GetDirectoryPath("file:///c:/path/"));
        Assert.Equal("file://server", GetDirectoryPath("file://server"));
        Assert.Equal("file://server/", GetDirectoryPath("file://server/"));
        Assert.Equal("file://server/", GetDirectoryPath("file://server/path"));
        Assert.Equal("file://server/", GetDirectoryPath("file://server/path/"));
        Assert.Equal("http://server", GetDirectoryPath("http://server"));
        Assert.Equal("http://server/", GetDirectoryPath("http://server/"));
        Assert.Equal("http://server/", GetDirectoryPath("http://server/path"));
        Assert.Equal("http://server/", GetDirectoryPath("http://server/path/"));
    }

    [Fact]
    public void TestGetLongestExtensionFromPath()
    {
        var extensions = new[] { ".z", ".y.z", ".other" };
        Assert.Equal(".y.z", GetLongestExtensionFromPath("/src/Component.y.z", extensions, false));
        Assert.Equal(".z", GetLongestExtensionFromPath("/src/Component.z", extensions, false));
        Assert.Equal("", GetLongestExtensionFromPath("/src/Component.y.Z", extensions, false));
        Assert.Equal(".y.Z", GetLongestExtensionFromPath("/src/Component.y.Z", extensions, true));
    }

    [Fact]
    public void TestRemoveAnyFileExtension()
    {
        Assert.Equal("/src/Component", RemoveAnyFileExtension("/src/Component.vue"));
        Assert.Equal("/src/Component", RemoveAnyFileExtension("/src/Component.d.ts"));
        Assert.Equal("/src/Component", RemoveAnyFileExtension("/src/Component"));
    }

    // !!!
    // getBaseFileName
    // getAnyExtensionFromPath

    [Fact]
    public void TestGetPathComponents()
    {
        Assert.Equal(new[] { "" }, GetPathComponents("", ""));
        Assert.Equal(new[] { "", "a" }, GetPathComponents("a", ""));
        Assert.Equal(new[] { "", ".", "a" }, GetPathComponents("./a", ""));
        Assert.Equal(new[] { "/" }, GetPathComponents("/", ""));
        Assert.Equal(new[] { "/", "a" }, GetPathComponents("/a", ""));
        Assert.Equal(new[] { "/", "a" }, GetPathComponents("/a/", ""));
        Assert.Equal(new[] { "c:" }, GetPathComponents("c:", ""));
        Assert.Equal(new[] { "", "c:d" }, GetPathComponents("c:d", ""));
        Assert.Equal(new[] { "c:/" }, GetPathComponents("c:/", ""));
        Assert.Equal(new[] { "c:/", "path" }, GetPathComponents("c:/path", ""));
        Assert.Equal(new[] { "//server" }, GetPathComponents("//server", ""));
        Assert.Equal(new[] { "//server/" }, GetPathComponents("//server/", ""));
        Assert.Equal(new[] { "//server/", "share" }, GetPathComponents("//server/share", ""));
        Assert.Equal(new[] { "file:///" }, GetPathComponents("file:///", ""));
        Assert.Equal(new[] { "file:///", "path" }, GetPathComponents("file:///path", ""));
        Assert.Equal(new[] { "file:///c:" }, GetPathComponents("file:///c:", ""));
        Assert.Equal(new[] { "file:///", "c:d" }, GetPathComponents("file:///c:d", ""));
        Assert.Equal(new[] { "file:///c:/" }, GetPathComponents("file:///c:/", ""));
        Assert.Equal(new[] { "file:///c:/", "path" }, GetPathComponents("file:///c:/path", ""));
        Assert.Equal(new[] { "file://server" }, GetPathComponents("file://server", ""));
        Assert.Equal(new[] { "file://server/" }, GetPathComponents("file://server/", ""));
        Assert.Equal(new[] { "file://server/", "path" }, GetPathComponents("file://server/path", ""));
        Assert.Equal(new[] { "http://server" }, GetPathComponents("http://server", ""));
        Assert.Equal(new[] { "http://server/" }, GetPathComponents("http://server/", ""));
        Assert.Equal(new[] { "http://server/", "path" }, GetPathComponents("http://server/path", ""));
    }

    [Fact]
    public void TestReducePathComponents()
    {
        Assert.Equal(new[] { "" }, ReducePathComponents(new[] { "" }));
        Assert.Equal(new[] { "" }, ReducePathComponents(new[] { "", "." }));
        Assert.Equal(new[] { "", "a" }, ReducePathComponents(new[] { "", ".", "a" }));
        Assert.Equal(new[] { "", "a" }, ReducePathComponents(new[] { "", "a", "." }));
        Assert.Equal(new[] { "", ".." }, ReducePathComponents(new[] { "", ".." }));
        Assert.Equal(new[] { "", "..", ".." }, ReducePathComponents(new[] { "", "..", ".." }));
        Assert.Equal(new[] { "", "..", ".." }, ReducePathComponents(new[] { "", "..", ".", ".." }));
        Assert.Equal(new[] { "" }, ReducePathComponents(new[] { "", "a", ".." }));
        Assert.Equal(new[] { "", "..", "a" }, ReducePathComponents(new[] { "", "..", "a" }));
        Assert.Equal(new[] { "/" }, ReducePathComponents(new[] { "/" }));
        Assert.Equal(new[] { "/" }, ReducePathComponents(new[] { "/", "." }));
        Assert.Equal(new[] { "/" }, ReducePathComponents(new[] { "/", ".." }));
        Assert.Equal(new[] { "/" }, ReducePathComponents(new[] { "/", "a", ".." }));
    }

    [Fact]
    public void TestCombinePaths()
    {
        // Non-rooted
        Assert.Equal("path/to/file.ext", CombinePaths("path", "to", "file.ext"));
        Assert.Equal("path/dir/../to/file.ext", CombinePaths("path", "dir", "..", "to", "file.ext"));
        // POSIX
        Assert.Equal("/path/to/file.ext", CombinePaths("/path", "to", "file.ext"));
        Assert.Equal("/to/file.ext", CombinePaths("/path", "/to", "file.ext"));
        // DOS
        Assert.Equal("c:/path/to/file.ext", CombinePaths("c:/path", "to", "file.ext"));
        Assert.Equal("c:/to/file.ext", CombinePaths("c:/path", "c:/to", "file.ext"));
        // URL
        Assert.Equal("file:///path/to/file.ext", CombinePaths("file:///path", "to", "file.ext"));
        Assert.Equal("file:///to/file.ext", CombinePaths("file:///path", "file:///to", "file.ext"));

        Assert.Equal("/node_modules/@types", CombinePaths("/", "/node_modules/@types"));
        Assert.Equal("/a/..", CombinePaths("/a/..", ""));
        Assert.Equal("/a/../b", CombinePaths("/a/..", "b"));
        Assert.Equal("/a/../b/", CombinePaths("/a/..", "b/"));
        Assert.Equal("/", CombinePaths("/a/..", "/"));
        Assert.Equal("/b", CombinePaths("/a/..", "/b"));
    }

    [Fact]
    public void TestResolvePath()
    {
        Assert.Equal("", ResolvePath(""));
        Assert.Equal("", ResolvePath("."));
        Assert.Equal("", ResolvePath("./"));
        Assert.Equal("..", ResolvePath(".."));
        Assert.Equal("../", ResolvePath("../"));
        Assert.Equal("/", ResolvePath("/"));
        Assert.Equal("/", ResolvePath("/."));
        Assert.Equal("/", ResolvePath("/./"));
        Assert.Equal("/", ResolvePath("/../"));
        Assert.Equal("/a", ResolvePath("/a"));
        Assert.Equal("/a/", ResolvePath("/a/"));
        Assert.Equal("/a", ResolvePath("/a/."));
        Assert.Equal("/a/", ResolvePath("/a/./"));
        Assert.Equal("/a/b", ResolvePath("/a/./b"));
        Assert.Equal("/a/b/", ResolvePath("/a/./b/"));
        Assert.Equal("/", ResolvePath("/a/.."));
        Assert.Equal("/", ResolvePath("/a/../"));
        Assert.Equal("/b", ResolvePath("/a/../b"));
        Assert.Equal("/b/", ResolvePath("/a/../b/"));
        Assert.Equal("/b", ResolvePath("/a/..", "b"));
        Assert.Equal("/", ResolvePath("/a/..", "/"));
        Assert.Equal("/b/", ResolvePath("/a/..", "b/"));
        Assert.Equal("/b", ResolvePath("/a/..", "/b"));
        Assert.Equal("/a/b", ResolvePath("/a/.", "b"));
        Assert.Equal("/a", ResolvePath("/a/.", "."));
        Assert.Equal("a/b/c", ResolvePath("a", "b", "c"));
        Assert.Equal("/c", ResolvePath("a", "b", "/c"));
        Assert.Equal("a/c", ResolvePath("a", "b", "../c"));
    }

    [Fact]
    public void TestGetNormalizedAbsolutePath()
    {
        Assert.Equal("/", GetNormalizedAbsolutePath("/", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("/.", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("/./", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("/../", ""));
        Assert.Equal("/a", GetNormalizedAbsolutePath("/a", ""));
        Assert.Equal("/a", GetNormalizedAbsolutePath("/a/", ""));
        Assert.Equal("/a", GetNormalizedAbsolutePath("/a/.", ""));
        Assert.Equal("/a/foo.", GetNormalizedAbsolutePath("/a/foo.", ""));
        Assert.Equal("/a", GetNormalizedAbsolutePath("/a/./", ""));
        Assert.Equal("/a/b", GetNormalizedAbsolutePath("/a/./b", ""));
        Assert.Equal("/a/b", GetNormalizedAbsolutePath("/a/./b/", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("/a/..", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("/a/../", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("/a/../", ""));
        Assert.Equal("/b", GetNormalizedAbsolutePath("/a/../b", ""));
        Assert.Equal("/b", GetNormalizedAbsolutePath("/a/../b/", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("/a/..", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("/a/..", "/"));
        Assert.Equal("/", GetNormalizedAbsolutePath("/a/..", "b/"));
        Assert.Equal("/", GetNormalizedAbsolutePath("/a/..", "/b"));
        Assert.Equal("/a", GetNormalizedAbsolutePath("/a/.", "b"));
        Assert.Equal("/a", GetNormalizedAbsolutePath("/a/.", "."));

        // Tests as above, but with backslashes.
        Assert.Equal("/", GetNormalizedAbsolutePath("\\", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\.", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\.\\", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\..\\", ""));
        Assert.Equal("/a", GetNormalizedAbsolutePath("\\a\\.\\", ""));
        Assert.Equal("/a/b", GetNormalizedAbsolutePath("\\a\\.\\b", ""));
        Assert.Equal("/a/b", GetNormalizedAbsolutePath("\\a\\.\\b\\", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\a\\..", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\a\\..\\", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\a\\..\\", ""));
        Assert.Equal("/b", GetNormalizedAbsolutePath("\\a\\..\\b", ""));
        Assert.Equal("/b", GetNormalizedAbsolutePath("\\a\\..\\b\\", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\a\\..", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\a\\..", "\\"));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\a\\..", "b\\"));
        Assert.Equal("/", GetNormalizedAbsolutePath("\\a\\..", "\\b"));
        Assert.Equal("/a", GetNormalizedAbsolutePath("\\a\\.", "b"));
        Assert.Equal("/a", GetNormalizedAbsolutePath("\\a\\.", "."));

        // Relative paths on an empty currentDirectory.
        Assert.Equal("", GetNormalizedAbsolutePath("", ""));
        Assert.Equal("", GetNormalizedAbsolutePath(".", ""));
        Assert.Equal("", GetNormalizedAbsolutePath("./", ""));
        // Strangely, these do not normalize to the empty string.
        Assert.Equal("..", GetNormalizedAbsolutePath("..", ""));
        Assert.Equal("..", GetNormalizedAbsolutePath("../", ""));

        // Interaction between relative paths and currentDirectory.
        Assert.Equal("/home", GetNormalizedAbsolutePath("", "/home"));
        Assert.Equal("/home", GetNormalizedAbsolutePath(".", "/home"));
        Assert.Equal("/home", GetNormalizedAbsolutePath("./", "/home"));
        Assert.Equal("/", GetNormalizedAbsolutePath("..", "/home"));
        Assert.Equal("/", GetNormalizedAbsolutePath("../", "/home"));
        Assert.Equal("b/a", GetNormalizedAbsolutePath("a", "b"));
        Assert.Equal("b/c/a", GetNormalizedAbsolutePath("a", "b/c"));

        // Base names starting or ending with a dot do not affect normalization.
        Assert.Equal(".a", GetNormalizedAbsolutePath(".a", ""));
        Assert.Equal("..a", GetNormalizedAbsolutePath("..a", ""));
        Assert.Equal("a.", GetNormalizedAbsolutePath("a.", ""));
        Assert.Equal("a..", GetNormalizedAbsolutePath("a..", ""));

        Assert.Equal("/base/.a", GetNormalizedAbsolutePath("/base/./.a", ""));
        Assert.Equal("/.a", GetNormalizedAbsolutePath("/base/../.a", ""));
        Assert.Equal("/base/..a", GetNormalizedAbsolutePath("/base/./..a", ""));
        Assert.Equal("/..a", GetNormalizedAbsolutePath("/base/../..a", ""));
        Assert.Equal("/base/..a/b", GetNormalizedAbsolutePath("/base/./..a/b", ""));
        Assert.Equal("/..a/b", GetNormalizedAbsolutePath("/base/../..a/b", ""));

        Assert.Equal("/base/a.", GetNormalizedAbsolutePath("/base/./a.", ""));
        Assert.Equal("/a.", GetNormalizedAbsolutePath("/base/../a.", ""));
        Assert.Equal("/base/a..", GetNormalizedAbsolutePath("/base/./a..", ""));
        Assert.Equal("/a..", GetNormalizedAbsolutePath("/base/../a..", ""));
        Assert.Equal("/base/a../b", GetNormalizedAbsolutePath("/base/./a../b", ""));
        Assert.Equal("/a../b", GetNormalizedAbsolutePath("/base/../a../b", ""));

        Assert.Equal("", GetNormalizedAbsolutePath("a/..", ""));
        Assert.Equal("/a", GetNormalizedAbsolutePath("/a//", ""));
        Assert.Equal("//a/", GetNormalizedAbsolutePath("//a", "a"));
        Assert.Equal("//", GetNormalizedAbsolutePath("/\\", ""));
        Assert.Equal("a/a", GetNormalizedAbsolutePath("a///", "a"));
        Assert.Equal("/", GetNormalizedAbsolutePath("/.//", ""));
        Assert.Equal("///", GetNormalizedAbsolutePath("//\\\\", ""));
        Assert.Equal("a", GetNormalizedAbsolutePath(".//a", "."));
        Assert.Equal("..", GetNormalizedAbsolutePath("a/../..", ""));
        Assert.Equal("/", GetNormalizedAbsolutePath("../..", "\\a"));
        Assert.Equal("a:/", GetNormalizedAbsolutePath("a:", "b"));
        Assert.Equal("../..", GetNormalizedAbsolutePath("a/../..", ".."));
        Assert.Equal("", GetNormalizedAbsolutePath("a/../..", "b"));
        Assert.Equal("../..", GetNormalizedAbsolutePath("a//../..", ".."));

        // Consecutive intermediate slashes are normalized to a single slash.
        Assert.Equal("a/b", GetNormalizedAbsolutePath("a//b", ""));
        Assert.Equal("a/b", GetNormalizedAbsolutePath("a///b", ""));
        Assert.Equal("a/b/c", GetNormalizedAbsolutePath("a/b//c", ""));
        Assert.Equal("/a/b/c", GetNormalizedAbsolutePath("/a/b//c", ""));
        Assert.Equal("//a/b/c", GetNormalizedAbsolutePath("//a/b//c", ""));

        // Backslashes are converted to slashes,
        // and then consecutive intermediate slashes are normalized to a single slash
        Assert.Equal("a/b", GetNormalizedAbsolutePath("a\\\\b", ""));
        Assert.Equal("a/b", GetNormalizedAbsolutePath("a\\\\\\b", ""));
        Assert.Equal("a/b/c", GetNormalizedAbsolutePath("a\\b\\\\c", ""));
        Assert.Equal("/a/b/c", GetNormalizedAbsolutePath("\\a\\b\\\\c", ""));
        Assert.Equal("//a/b/c", GetNormalizedAbsolutePath("\\\\a\\b\\\\c", ""));

        // The same occurs for mixed slashes.
        Assert.Equal("a/b", GetNormalizedAbsolutePath("a/\\b", ""));
        Assert.Equal("a/b", GetNormalizedAbsolutePath("a\\/b", ""));
        Assert.Equal("a/b", GetNormalizedAbsolutePath("a\\/\\b", ""));
        Assert.Equal("a/b/c", GetNormalizedAbsolutePath("a\\b//c", ""));
        Assert.Equal("/a/b/c", GetNormalizedAbsolutePath("\\a\\b\\\\c", ""));
        Assert.Equal("//a/b/c", GetNormalizedAbsolutePath("\\\\a\\b\\\\c", ""));
    }

    [Fact]
    public void TestGetNormalizedAbsolutePathWithoutRoot()
    {
        Assert.Equal("a/b/c.txt", GetNormalizedAbsolutePathWithoutRoot("/a/b/c.txt", "/a/b"));
        Assert.Equal("work/hello.txt", GetNormalizedAbsolutePathWithoutRoot("c:/work/hello.txt", "c:/work"));
        Assert.Equal("work/hello.txt", GetNormalizedAbsolutePathWithoutRoot("c:/work/hello.txt", "d:/worspaces"));
    }

    // path_test.go 的 getNormalizedAbsolutePathTests：Go 只把它用在 Benchmark/Fuzz 上，
    // 这里作为 FuzzGetNormalizedAbsolutePath 的种子语料。
    private static readonly (string Group, string Path, string CurrentDirectory)[] _getNormalizedAbsolutePathTests =
    [
        ("non-normalized inputs", "/.", ""),
        ("non-normalized inputs", "/./", ""),
        ("non-normalized inputs", "/../", ""),
        ("non-normalized inputs", "/a/", ""),
        ("non-normalized inputs", "/a/.", ""),
        ("non-normalized inputs", "/a/foo.", ""),
        ("non-normalized inputs", "/a/./", ""),
        ("non-normalized inputs", "/a/./b", ""),
        ("non-normalized inputs", "/a/./b/", ""),
        ("non-normalized inputs", "/a/..", ""),
        ("non-normalized inputs", "/a/../", ""),
        ("non-normalized inputs", "/a/../", ""),
        ("non-normalized inputs", "/a/../b", ""),
        ("non-normalized inputs", "/a/../b/", ""),
        ("non-normalized inputs", "/a/..", ""),
        ("non-normalized inputs", "/a/..", "/"),
        ("non-normalized inputs", "/a/..", "b/"),
        ("non-normalized inputs", "/a/..", "/b"),
        ("non-normalized inputs", "/a/.", "b"),
        ("non-normalized inputs", "/a/.", "."),
        ("normalized inputs", "/a/b", ""),
        ("normalized inputs", "/one/two/three", ""),
        ("normalized inputs", "/users/root/project/src/foo.ts", ""),
        ("normalized inputs (long)", "/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/q/r/s/t/u/v/w/x/y/z", ""),
        ("normalized inputs (long)", "/one/two/three/four/five/six/seven/eight/nine/ten/eleven/twelve/thirteen/fourteen/fifteen/sixteen/seventeen/eighteen/nineteen/twenty", ""),
        ("normalized inputs (long)", "/users/root/project/src/foo/bar/baz/qux/quux/corge/grault/garply/waldo/fred/plugh/xyzzy/thud", ""),
        ("normalized inputs (long)", "/lorem/ipsum/dolor/sit/amet/consectetur/adipiscing/elit/sed/do/eiusmod/tempor/incididunt/ut/labore/et/dolore/magna/aliqua/ut/enim/ad/minim/veniam", ""),
    ];

    public static IEnumerable<object[]> GetNormalizedAbsolutePathSeeds()
    {
        foreach (var (_, path, currentDirectory) in _getNormalizedAbsolutePathTests)
        {
            yield return new object[] { path, currentDirectory };
        }
    }

    // path_test.go 的 FuzzGetNormalizedAbsolutePath：拿旧算法
    // GetPathFromPathComponents(GetNormalizedPathComponents(...)) 做参照。这里只跑种子语料。
    [Theory]
    [MemberData(nameof(GetNormalizedAbsolutePathSeeds))]
    public void FuzzGetNormalizedAbsolutePath(string p, string dir)
    {
        Assert.Equal(GetNormalizedAbsolutePathOld(p, dir), GetNormalizedAbsolutePath(p, dir));
    }

    // Go 测试文件里的 getNormalizedAbsolutePath_old。
    private static string GetNormalizedAbsolutePathOld(string fileName, string currentDirectory)
        => GetPathFromPathComponents(GetNormalizedPathComponents(fileName, currentDirectory));

    [Fact]
    public void TestGetRelativePathToDirectoryOrUrl()
    {
        // !!!
        // Based on tests for `getRelativePathFromDirectory`.

        Assert.Equal("", GetRelativePathToDirectoryOrUrl("/", "/", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("", GetRelativePathToDirectoryOrUrl("/a", "/a", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("", GetRelativePathToDirectoryOrUrl("/a/", "/a", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("..", GetRelativePathToDirectoryOrUrl("/a", "/", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("../b", GetRelativePathToDirectoryOrUrl("/a", "/b", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("../../b", GetRelativePathToDirectoryOrUrl("/a/b", "/b", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("../../../b", GetRelativePathToDirectoryOrUrl("/a/b/c", "/b", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("../../../b/c", GetRelativePathToDirectoryOrUrl("/a/b/c", "/b/c", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("..", GetRelativePathToDirectoryOrUrl("/a/b/c", "/a/b", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("d:/", GetRelativePathToDirectoryOrUrl("c:", "d:", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("", GetRelativePathToDirectoryOrUrl("file:///", "file:///", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("", GetRelativePathToDirectoryOrUrl("file:///a", "file:///a", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("", GetRelativePathToDirectoryOrUrl("file:///a/", "file:///a", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("..", GetRelativePathToDirectoryOrUrl("file:///a", "file:///", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("../b", GetRelativePathToDirectoryOrUrl("file:///a", "file:///b", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("../../b", GetRelativePathToDirectoryOrUrl("file:///a/b", "file:///b", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("../../../b", GetRelativePathToDirectoryOrUrl("file:///a/b/c", "file:///b", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("../../../b/c", GetRelativePathToDirectoryOrUrl("file:///a/b/c", "file:///b/c", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("..", GetRelativePathToDirectoryOrUrl("file:///a/b/c", "file:///a/b", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
        Assert.Equal("file:///d:/", GetRelativePathToDirectoryOrUrl("file:///c:", "file:///d:", false /*isAbsolutePathAnUrl*/, new ComparePathsOptions()));
    }

    [Fact]
    public void TestToFileNameLowerCase()
    {
        Assert.Equal("/user/username/projects/project/file.ts", ToFileNameLowerCase("/user/UserName/projects/Project/file.ts"));
        Assert.Equal("/user/username/projects/projectß/file.ts", ToFileNameLowerCase("/user/UserName/projects/projectß/file.ts"));
        Assert.Equal("/user/username/projects/İproject/file.ts", ToFileNameLowerCase("/user/UserName/projects/İproject/file.ts"));
        Assert.Equal("/user/username/projects/ı/file.ts", ToFileNameLowerCase("/user/UserName/projects/ı/file.ts"));
    }

    // See [toFileNameLowerCase] for more info.
    //
    // To avoid having to do string building for most common cases, also ignore
    // a-z, 0-9, \u0131, \u00DF, \, /, ., : and space
    private static readonly Regex _fileNameLowerCaseRegExp = new(@"[^\u0130\u0131\u00DFa-z0-9\\/:\-_. ]+");

    // path_test.go 的 toFileNameLowerCaseTests：Go 用作 Benchmark/Fuzz 的种子语料。
    private static readonly string[] _toFileNameLowerCaseTests =
    [
        "/path/to/file.ext",
        "/PATH/TO/FILE.EXT",
        "/path/to/FILE.EXT",
        "/user/UserName/projects/Project/file.ts",
        "/user/UserName/projects/projectß/file.ts",
        "/user/UserName/projects/İproject/file.ts",
        "/user/UserName/projects/ı/file.ts",
        Repeat("FoO/", 100),
    ];

    // Go 测试文件里的 oldToFileNameLowerCase（正则版参考实现）。
    private static string OldToFileNameLowerCase(string fileName)
        => _fileNameLowerCaseRegExp.Replace(fileName, static m => m.Value.ToLowerInvariant());

    public static IEnumerable<object[]> ToFileNameLowerCaseSeeds()
    {
        foreach (var test in _toFileNameLowerCaseTests)
        {
            yield return new object[] { test };
        }
    }

    // path_test.go 的 FuzzToFileNameLowerCase：与正则版参考实现对比。这里只跑种子语料。
    [Theory]
    [MemberData(nameof(ToFileNameLowerCaseSeeds))]
    public void FuzzToFileNameLowerCase(string p)
    {
        Assert.Equal(OldToFileNameLowerCase(p), ToFileNameLowerCase(p));
    }

    // Go 测试文件里的 strings.Repeat（只用于构造测试数据）。
    private static string Repeat(string value, int count)
    {
        var builder = new System.Text.StringBuilder(value.Length * count);
        for (var i = 0; i < count; i++)
        {
            builder.Append(value);
        }

        return builder.ToString();
    }

    [Fact]
    public void TestTrimFilePathPrefix_CaseSensitiveExactMatch()
    {
        var ok = TryTrimFilePathPrefix("/project/src/file.ts", "/project/src", true /*useCaseSensitiveFileNames*/, out var suffix);
        Assert.True(ok);
        Assert.Equal("/file.ts", suffix);
    }

    [Fact]
    public void TestTrimFilePathPrefix_CaseSensitiveMismatch()
    {
        var ok = TryTrimFilePathPrefix("/project/SRC/file.ts", "/project/src", true /*useCaseSensitiveFileNames*/, out var suffix);
        Assert.False(ok);
        Assert.Equal("/project/SRC/file.ts", suffix);
    }

    [Fact]
    public void TestTrimFilePathPrefix_CaseInsensitiveMatch()
    {
        var ok = TryTrimFilePathPrefix("/project/SRC/file.ts", "/project/src", false /*useCaseSensitiveFileNames*/, out var suffix);
        Assert.True(ok);
        Assert.Equal("/file.ts", suffix);
    }

    [Fact]
    public void TestTrimFilePathPrefix_NoMatch()
    {
        var ok = TryTrimFilePathPrefix("/other/file.ts", "/project/src", false /*useCaseSensitiveFileNames*/, out var suffix);
        Assert.False(ok);
        Assert.Equal("/other/file.ts", suffix);
    }

    [Fact]
    public void TestTrimFilePathPrefix_CaseFoldingShrinksPrefixByteLengthWithoutChangingRuneCount()
    {
        // Each Kelvin sign '\u212A' case-folds to the single-byte 'k', so the raw
        // (non-canonicalized) prefix is longer, in bytes, than the path it's a
        // case-insensitive prefix of, even though the path itself is longer overall
        // once its own (already-lowercase) suffix is included. Slicing path by
        // len(prefix) bytes would panic here ([10:9]); TrimFilePathPrefix must
        // clamp per-rune instead, like the reference implementation's substring
        // does.
        var ok = TryTrimFilePathPrefix("/kkk/a.ts", "/\u212A\u212A\u212A", false /*useCaseSensitiveFileNames*/, out var suffix);
        Assert.True(ok);
        Assert.Equal("/a.ts", suffix);
    }

    [Fact]
    public void TestTrimFilePathPrefix_PathEqualToPrefix()
    {
        var ok = TryTrimFilePathPrefix("/project/src", "/project/src", true /*useCaseSensitiveFileNames*/, out var suffix);
        Assert.True(ok);
        Assert.Equal("", suffix);
    }

    [Fact]
    public void TestToPath()
    {
        Assert.Equal("path/to/file.ext", (string)ToPath("file.ext", "path/to", false /*useCaseSensitiveFileNames*/));
        Assert.Equal("/path/to/file.ext", (string)ToPath("file.ext", "/path/to", true /*useCaseSensitiveFileNames*/));
        Assert.Equal("/path/file.ext", (string)ToPath("/path/to/../file.ext", "path/to", true /*useCaseSensitiveFileNames*/));
    }

    private static readonly Regex _relativePathSegmentRegExp = new(@"//|(?:^|/)\.\.?(?:$|/)");

    // Go 测试文件里的 oldHasRelativePathSegment（正则版参考实现）。
    private static bool OldHasRelativePathSegment(string p) => _relativePathSegmentRegExp.IsMatch(p);

    // path_test.go 的 hasRelativePathSegmentTests（去掉了只用于 Benchmark 的 bench 列）。
    // 用 record 承载 Go 的子测试名（ToString 即 name），避免 xUnit1026（Theory 未使用参数）。
    public sealed record HasRelativePathSegmentCase(string Name, string P)
    {
        public override string ToString() => Name;
    }

    private static readonly string[] _hasRelativePathSegmentTests =
    [
        "//",
        "foo/bar/baz",
        "foo/./baz",
        "foo/../baz",
        "foo/bar/baz/.",
        "./some/path",
        "/foo//bar/",
        "/foo/./bar/../../.",
        Repeat("foo/", 100) + "..",
    ];

    public static IEnumerable<object[]> HasRelativePathSegmentSeeds()
    {
        foreach (var test in _hasRelativePathSegmentTests)
        {
            yield return new object[] { new HasRelativePathSegmentCase(ShortenName(test), test) };
        }
    }

    // path_test.go 的 FuzzHasRelativePathSegment：与正则版参考实现对比。这里只跑种子语料。
    [Theory]
    [MemberData(nameof(HasRelativePathSegmentSeeds))]
    public void FuzzHasRelativePathSegment(HasRelativePathSegmentCase tt)
    {
        Assert.Equal(OldHasRelativePathSegment(tt.P), HasRelativePathSegment(tt.P));
    }

    // Go 测试文件里的 shortenName。
    private static string ShortenName(string name) => name.Length > 20 ? name[..20] + "...etc" : name;

    // path_test.go 的 pathIsRelativeTests；Go 的 init() 会把每一条的 "/" 换成 "\" 再追加一份。
    // （benchmark 列只服务 BenchmarkPathIsRelative，未搬运。）
    private static readonly (string P, bool IsRelative)[] _pathIsRelativeTests = BuildPathIsRelativeTests();

    private static (string P, bool IsRelative)[] BuildPathIsRelativeTests()
    {
        (string P, bool IsRelative)[] initial =
        [
            // relative
            (".", true),
            ("..", true),
            ("./", true),
            ("../", true),
            ("./foo/bar", true),
            ("../foo/bar", true),
            ("../" + Repeat("foo/", 100), true),
            // non-relative
            ("", false),
            ("foo", false),
            ("foo/bar", false),
            ("/foo/bar", false),
            ("c:/foo/bar", false),
        ];

        var all = new List<(string P, bool IsRelative)>(initial);
        foreach (var (p, isRelative) in initial)
        {
            all.Add((p.Replace('/', '\\'), isRelative));
        }

        return [.. all];
    }

    // path_test.go 的 pathIsRelativeTests 用的行（Name 是 Go 的 shortenName(p) 子测试名）。
    public sealed record PathIsRelativeCase(string Name, string P, bool IsRelative)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<object[]> PathIsRelativeSeeds()
    {
        foreach (var (p, isRelative) in _pathIsRelativeTests)
        {
            yield return new object[] { new PathIsRelativeCase(ShortenName(p), p, isRelative) };
        }
    }

    [Theory]
    [MemberData(nameof(PathIsRelativeSeeds))]
    public void TestPathIsRelative(PathIsRelativeCase tt)
    {
        Assert.Equal(tt.IsRelative, PathIsRelative(tt.P));
    }

    [Fact]
    public void TestGetCommonParents_EmptyInput()
    {
        var opts = new ComparePathsOptions();
        string[] paths = [];
        var (got, ignored) = GetCommonParents(paths, 1, GetPathComponents, opts);
        Assert.Empty(ignored);
        // Go 断言的是 got == nil；nil slice 在 C# 里映射为处理成空数组。
        Assert.Empty(got);
    }

    [Fact]
    public void TestGetCommonParents_SinglePathReturnsItself()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "/a/b/c/d" };
        var (got, ignored) = GetCommonParents(paths, 1, GetPathComponents, opts);
        Assert.Empty(ignored);
        Assert.Equal(new[] { paths[0] }, got);
    }

    [Fact]
    public void TestGetCommonParents_PathsShorterThanMinComponentsAreIgnored()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "/a/b/c/d", "/a/b/c/e", "/a/b/f/g", "/x/y" };
        var (got, ignored) = GetCommonParents(paths, 4, GetPathComponents, opts);
        Assert.Single(ignored);
        Assert.Contains("/x/y", ignored);
        Assert.Equal(new[] { "/a/b/c", "/a/b/f/g" }, got);
    }

    [Fact]
    public void TestGetCommonParents_ThreePathsShareAParent()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "/a/b/c/d", "/a/b/c/e", "/a/b/f/g" };
        var (got, ignored) = GetCommonParents(paths, 1, GetPathComponents, opts);
        Assert.Empty(ignored);
        Assert.Equal(new[] { "/a/b" }, got);
    }

    [Fact]
    public void TestGetCommonParents_MixedWithShortPathCollapsesToRootWhenMinComponentsIsOne()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "/a/b/c/d", "/a/b/c/e", "/a/b/f/g", "/x/y/z" };
        var (got, ignored) = GetCommonParents(paths, 1, GetPathComponents, opts);
        Assert.Empty(ignored);
        Assert.Equal(new[] { "/" }, got);
    }

    [Fact]
    public void TestGetCommonParents_MixedWithShortPathPreservesBothWhenMinComponentsIsThree()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "/a/b/c/d", "/a/b/c/e", "/a/b/f/g", "/x/y/z" };
        var (got, ignored) = GetCommonParents(paths, 3, GetPathComponents, opts);
        Assert.Empty(ignored);
        Assert.Equal(new[] { "/a/b", "/x/y/z" }, got);
    }

    [Fact]
    public void TestGetCommonParents_DifferentVolumesAreReturnedIndividually()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "c:/a/b/c/d", "d:/a/b/c/d" };
        var (got, ignored) = GetCommonParents(paths, 1, GetPathComponents, opts);
        Assert.Empty(ignored);
        Assert.Equal(new[] { paths[0], paths[1] }, got);
    }

    [Fact]
    public void TestGetCommonParents_DuplicatePathsDeduplicateResult()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "/a/b/c/d", "/a/b/c/d" };
        var (got, ignored) = GetCommonParents(paths, 1, GetPathComponents, opts);
        Assert.Empty(ignored);
        Assert.Equal(new[] { paths[0] }, got);
    }

    [Fact]
    public void TestGetCommonParents_PathsWithFewComponentsAreReturnedAsIsWhenMinComponentsMet()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "/a/b/c/d", "/x/y" };
        var (got, ignored) = GetCommonParents(paths, 2, GetPathComponents, opts);
        Assert.Empty(ignored);
        Assert.Equal(new[] { "/a/b/c/d", "/x/y" }, got);
    }

    [Fact]
    public void TestGetCommonParents_MinComponentsTwo()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "/a/b/c/d", "/a/z/c/e", "/a/aaa/f/g", "/x/y/z" };
        var (got, ignored) = GetCommonParents(paths, 2, GetPathComponents, opts);
        Assert.Empty(ignored);
        Assert.Equal(new[] { "/a", "/x/y/z" }, got);
    }

    [Fact]
    public void TestGetCommonParents_TrailingSeparatorsAreHandled()
    {
        var opts = new ComparePathsOptions();
        var paths = new[] { "/a/b/", "/a/b/c" };
        var (got, ignored) = GetCommonParents(paths, 1, GetPathComponents, opts);
        Assert.Empty(ignored);
        Assert.Equal(new[] { "/a/b" }, got);
    }
}



// ported from: internal/vfs/vfsmatch/vfsmatch_test.go
//
// 说明：
// - Go 的测试用 `vfstest.FromMap`；C# 侧用同一个移植（`TypeScript.Vfs.VfsTest.VfsTest.FromMap`，由 Vfs 切片提供），
//   不再自建内存 FS。
// - Go 的 `runReadDirectoryCase` 里 currentDir/path/depth 的空值默认（"/"、"/dev"、UnlimitedDepth）原样保留。
// - Go 的断言助手 `contains`/`hasSuffix` 用 BCL 的 `string.Contains`/`EndsWith`；
//   `gotest.tools/assert.Assert(t, slices.Contains(...))` → `Assert.Contains(item, got)`；
//   `assert.DeepEqual(t, got, []string{...})` → `Assert.Equal(string[], string[])`（xunit 逐元素比较）。
// - 本文件只覆盖能在这个内存 FS 上跑起来的用例；`vfs_test.go` 只有 Benchmark，不移植（见报告）。
using TypeScript.Vfs;
using TypeScript.Vfs.VfsMatch;
using Xunit;
using static TypeScript.Vfs.VfsMatch.VfsMatch;
using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class VfsMatchTests
{
    // --- Go 里的测试宿主（caseInsensitiveHost / caseSensitiveHost / commonFoldersHost / …） ---

    // Go: caseInsensitiveHost，模拟 Windows 式大小写不敏感文件系统
    private static FS CaseInsensitiveHost() => FromMap(new Dictionary<string, string>
    {
        ["/dev/a.ts"] = "",
        ["/dev/a.d.ts"] = "",
        ["/dev/a.js"] = "",
        ["/dev/b.ts"] = "",
        ["/dev/b.js"] = "",
        ["/dev/c.d.ts"] = "",
        ["/dev/z/a.ts"] = "",
        ["/dev/z/abz.ts"] = "",
        ["/dev/z/aba.ts"] = "",
        ["/dev/z/b.ts"] = "",
        ["/dev/z/bbz.ts"] = "",
        ["/dev/z/bba.ts"] = "",
        ["/dev/x/a.ts"] = "",
        ["/dev/x/aa.ts"] = "",
        ["/dev/x/b.ts"] = "",
        ["/dev/x/y/a.ts"] = "",
        ["/dev/x/y/b.ts"] = "",
        ["/dev/js/a.js"] = "",
        ["/dev/js/b.js"] = "",
        ["/dev/js/d.min.js"] = "",
        ["/dev/js/ab.min.js"] = "",
        ["/ext/ext.ts"] = "",
        ["/ext/b/a..b.ts"] = "",
    }, false);

    // Go: caseSensitiveHost，模拟 Unix 式大小写敏感文件系统
    private static FS CaseSensitiveHost() => FromMap(new Dictionary<string, string>
    {
        ["/dev/a.ts"] = "",
        ["/dev/a.d.ts"] = "",
        ["/dev/a.js"] = "",
        ["/dev/b.ts"] = "",
        ["/dev/b.js"] = "",
        ["/dev/A.ts"] = "",
        ["/dev/B.ts"] = "",
        ["/dev/c.d.ts"] = "",
        ["/dev/z/a.ts"] = "",
        ["/dev/z/abz.ts"] = "",
        ["/dev/z/aba.ts"] = "",
        ["/dev/z/b.ts"] = "",
        ["/dev/z/bbz.ts"] = "",
        ["/dev/z/bba.ts"] = "",
        ["/dev/x/a.ts"] = "",
        ["/dev/x/b.ts"] = "",
        ["/dev/x/y/a.ts"] = "",
        ["/dev/x/y/b.ts"] = "",
        ["/dev/q/a/c/b/d.ts"] = "",
        ["/dev/js/a.js"] = "",
        ["/dev/js/b.js"] = "",
        ["/dev/js/d.MIN.js"] = "",
    }, true);

    // Go: commonFoldersHost，包含 node_modules / bower_components / jspm_packages
    private static FS CommonFoldersHost() => FromMap(new Dictionary<string, string>
    {
        ["/dev/a.ts"] = "",
        ["/dev/a.d.ts"] = "",
        ["/dev/a.js"] = "",
        ["/dev/b.ts"] = "",
        ["/dev/x/a.ts"] = "",
        ["/dev/node_modules/a.ts"] = "",
        ["/dev/bower_components/a.ts"] = "",
        ["/dev/jspm_packages/a.ts"] = "",
    }, false);

    // Go: dottedFoldersHost，包含以点开头的文件与目录
    private static FS DottedFoldersHost() => FromMap(new Dictionary<string, string>
    {
        ["/dev/x/d.ts"] = "",
        ["/dev/x/y/d.ts"] = "",
        ["/dev/x/y/.e.ts"] = "",
        ["/dev/x/.y/a.ts"] = "",
        ["/dev/.z/.b.ts"] = "",
        ["/dev/.z/c.ts"] = "",
        ["/dev/w/.u/e.ts"] = "",
        ["/dev/g.min.js/.g/g.ts"] = "",
    }, false);

    // Go: mixedExtensionHost
    private static FS MixedExtensionHost() => FromMap(new Dictionary<string, string>
    {
        ["/dev/a.ts"] = "",
        ["/dev/a.d.ts"] = "",
        ["/dev/a.js"] = "",
        ["/dev/b.tsx"] = "",
        ["/dev/b.d.ts"] = "",
        ["/dev/b.jsx"] = "",
        ["/dev/c.tsx"] = "",
        ["/dev/c.js"] = "",
        ["/dev/d.js"] = "",
        ["/dev/e.jsx"] = "",
        ["/dev/f.other"] = "",
    }, false);

    // Go: sameNamedDeclarationsHost，同名不同扩展名
    private static FS SameNamedDeclarationsHost() => FromMap(new Dictionary<string, string>
    {
        ["/dev/a.tsx"] = "",
        ["/dev/a.d.ts"] = "",
        ["/dev/b.tsx"] = "",
        ["/dev/b.ts"] = "",
        ["/dev/c.tsx"] = "",
        ["/dev/m.ts"] = "",
        ["/dev/m.d.ts"] = "",
        ["/dev/n.tsx"] = "",
        ["/dev/n.ts"] = "",
        ["/dev/n.d.ts"] = "",
        ["/dev/o.ts"] = "",
        ["/dev/x.d.ts"] = "",
    }, false);

    // --- Go 的 readDirTestCase + runReadDirectoryCase ---

    public sealed record ReadDirCase(
        string Name,
        Func<FS> Host,
        string CurrentDir,
        string Path,
        string[]? Extensions,
        string[]? Excludes,
        string[]? Includes,
        int Depth,
        Action<string[]> Expect)
    {
        public override string ToString() => Name;
    }

    private static int DepthOrUnlimited(int depth) => depth == 0 ? UnlimitedDepth : depth;

    private static void RunReadDirectoryCase(ReadDirCase tc)
    {
        var currentDir = tc.CurrentDir == "" ? "/" : tc.CurrentDir;
        var path = tc.Path == "" ? "/dev" : tc.Path;
        var host = tc.Host();
        var got = MatchFiles(path, tc.Extensions, tc.Excludes, tc.Includes, host.UseCaseSensitiveFileNames(), currentDir, DepthOrUnlimited(tc.Depth), host);
        tc.Expect(got);
    }

    private static IEnumerable<object[]> AsRows<T>(T[] cases)
    {
        foreach (var testCase in cases)
        {
            yield return new object[] { testCase! };
        }
    }

    private static void ContainsAll(string[] got, params string[] expected)
    {
        foreach (var item in expected)
        {
            Assert.Contains(item, got);
        }
    }

    private static void ContainsNone(string[] got, params string[] unexpected)
    {
        foreach (var item in unexpected)
        {
            Assert.DoesNotContain(item, got);
        }
    }

    private static readonly ReadDirCase[] _readDirectoryCases =
    [
        new("defaults include common package folders", CommonFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null, null, 0,
            got => ContainsAll(got, "/dev/a.ts", "/dev/b.ts", "/dev/x/a.ts", "/dev/node_modules/a.ts", "/dev/bower_components/a.ts", "/dev/jspm_packages/a.ts")),
        new("literal includes without exclusions", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["a.ts", "b.ts"], 0,
            got => Assert.Equal(["/dev/a.ts", "/dev/b.ts"], got)),
        new("literal includes with non ts extensions excluded", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["a.js", "b.js"], 0,
            got => Assert.Empty(got)),
        new("literal includes missing files excluded", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["z.ts", "x.ts"], 0,
            got => Assert.Empty(got)),
        new("literal includes with literal excludes", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["b.ts"], ["a.ts", "b.ts"], 0,
            got => Assert.Equal(["/dev/a.ts"], got)),
        new("literal includes with wildcard excludes", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["*.ts", "z/??z.ts", "*/b.ts"],
            ["a.ts", "b.ts", "z/a.ts", "z/abz.ts", "z/aba.ts", "x/b.ts"], 0,
            got => Assert.Equal(["/dev/z/a.ts", "/dev/z/aba.ts"], got)),
        new("literal includes with recursive excludes", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**/b.ts"],
            ["a.ts", "b.ts", "x/a.ts", "x/b.ts", "x/y/a.ts", "x/y/b.ts"], 0,
            got => Assert.Equal(["/dev/a.ts", "/dev/x/a.ts", "/dev/x/y/a.ts"], got)),
        new("case sensitive exclude is respected", CaseSensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**/b.ts"], ["B.ts"], 0,
            got => Assert.Equal(["/dev/B.ts"], got)),
        new("explicit includes keep common package folders", CommonFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null,
            ["a.ts", "b.ts", "node_modules/a.ts", "bower_components/a.ts", "jspm_packages/a.ts"], 0,
            got => ContainsAll(got, "/dev/a.ts", "/dev/b.ts", "/dev/node_modules/a.ts", "/dev/bower_components/a.ts", "/dev/jspm_packages/a.ts")),
        new("wildcard include sorted order", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["z/*.ts", "x/*.ts"], 0,
            got => Assert.Equal(
                ["/dev/z/a.ts", "/dev/z/aba.ts", "/dev/z/abz.ts", "/dev/z/b.ts", "/dev/z/bba.ts", "/dev/z/bbz.ts",
                 "/dev/x/a.ts", "/dev/x/aa.ts", "/dev/x/b.ts"], got)),
        new("wildcard include same named declarations excluded", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["*.ts"], 0,
            got => ContainsAll(got, "/dev/a.ts", "/dev/b.ts", "/dev/a.d.ts", "/dev/c.d.ts")),
        new("wildcard star matches only ts files", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["*"], 0,
            got =>
            {
                foreach (var f in got)
                {
                    Assert.True(f.Contains(".ts"), "unexpected file: " + f);
                }

                ContainsNone(got, "/dev/a.js", "/dev/b.js");
            }),
        new("wildcard question mark single character", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["x/?.ts"], 0,
            got => Assert.Equal(["/dev/x/a.ts", "/dev/x/b.ts"], got)),
        new("wildcard recursive directory", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/a.ts"], 0,
            got => ContainsAll(got, "/dev/a.ts", "/dev/z/a.ts", "/dev/x/a.ts", "/dev/x/y/a.ts")),
        new("double asterisk matches zero-or-more directories", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["x/**/a.ts"], 0,
            got =>
            {
                Assert.Equal(2, got.Length);
                ContainsAll(got, "/dev/x/a.ts", "/dev/x/y/a.ts");
            }),
        new("wildcard multiple recursive directories", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null,
            ["x/y/**/a.ts", "x/**/a.ts", "z/**/a.ts"], 0,
            got => Assert.NotEmpty(got)),
        new("wildcard case sensitive matching", CaseSensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/A.ts"], 0,
            got => Assert.Equal(["/dev/A.ts"], got)),
        new("wildcard missing files excluded", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["*/z.ts"], 0,
            got => Assert.Empty(got)),
        new("exclude folders with wildcards", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["z", "x"], ["**/*"], 0,
            got =>
            {
                foreach (var f in got)
                {
                    Assert.True(!f.Contains("/z/") && !f.Contains("/x/"), "should not contain z or x: " + f);
                }

                ContainsAll(got, "/dev/a.ts", "/dev/b.ts");
            }),
        new("include paths outside project absolute", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["*", "/ext/*"], 0,
            got => ContainsAll(got, "/dev/a.ts", "/ext/ext.ts")),
        new("include paths outside project relative", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**"], ["*", "../ext/*"], 0,
            got => ContainsAll(got, "/ext/ext.ts")),
        new("include files containing double dots", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**"], ["/ext/b/a..b.ts"], 0,
            got => ContainsAll(got, "/ext/b/a..b.ts")),
        new("exclude files containing double dots", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["/ext/b/a..b.ts"], ["/ext/**/*"], 0,
            got =>
            {
                ContainsAll(got, "/ext/ext.ts");
                ContainsNone(got, "/ext/b/a..b.ts");
            }),
        new("common package folders implicitly excluded", CommonFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/a.ts"], 0,
            got =>
            {
                ContainsAll(got, "/dev/a.ts", "/dev/x/a.ts");
                ContainsNone(got, "/dev/node_modules/a.ts", "/dev/bower_components/a.ts", "/dev/jspm_packages/a.ts");
            }),
        new("common package folders explicit recursive include", CommonFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null,
            ["**/a.ts", "**/node_modules/a.ts"], 0,
            got => ContainsAll(got, "/dev/a.ts", "/dev/node_modules/a.ts")),
        new("common package folders wildcard include", CommonFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["*/a.ts"], 0,
            got =>
            {
                ContainsAll(got, "/dev/x/a.ts");
                ContainsNone(got, "/dev/node_modules/a.ts");
            }),
        new("common package folders explicit wildcard include", CommonFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null,
            ["*/a.ts", "node_modules/a.ts"], 0,
            got => ContainsAll(got, "/dev/x/a.ts", "/dev/node_modules/a.ts")),
        new("dotted folders not implicitly included", DottedFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["x/**/*", "w/*/*"], 0,
            got =>
            {
                ContainsAll(got, "/dev/x/d.ts", "/dev/x/y/d.ts");
                ContainsNone(got, "/dev/x/.y/a.ts", "/dev/x/y/.e.ts", "/dev/w/.u/e.ts");
            }),
        new("dotted folders explicitly included", DottedFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["x/.y/a.ts", "/dev/.z/.b.ts"], 0,
            got => ContainsAll(got, "/dev/x/.y/a.ts", "/dev/.z/.b.ts")),
        new("dotted folders recursive wildcard matches directories", DottedFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/.*/*"], 0,
            got => ContainsAll(got, "/dev/x/.y/a.ts", "/dev/.z/c.ts", "/dev/w/.u/e.ts")),
        new("trailing recursive include returns empty", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**"], 0,
            got => Assert.Empty(got)),
        new("trailing recursive exclude removes everything", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**"], ["**/*"], 0,
            got => Assert.Empty(got)),
        new("multiple recursive directory patterns in includes", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/x/**/*"], 0,
            got => ContainsAll(got, "/dev/x/a.ts", "/dev/x/y/a.ts")),
        new("multiple recursive directory patterns in excludes", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**/x/**"], ["**/a.ts"], 0,
            got =>
            {
                ContainsAll(got, "/dev/a.ts", "/dev/z/a.ts");
                ContainsNone(got, "/dev/x/a.ts", "/dev/x/y/a.ts");
            }),
        new("implicit globbification expands directory", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["z"], 0,
            got => ContainsAll(got, "/dev/z/a.ts", "/dev/z/aba.ts", "/dev/z/b.ts")),
        new("exclude patterns starting with starstar", CaseSensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**/x"], null, 0,
            got =>
            {
                foreach (var f in got)
                {
                    Assert.True(!f.Contains("/x/"), "should not contain /x/: " + f);
                }
            }),
        new("include patterns starting with starstar", CaseSensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/x", "**/a/**/b"], 0,
            got => ContainsAll(got, "/dev/x/a.ts", "/dev/q/a/c/b/d.ts")),
        new("depth limit one", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, null, 1,
            got =>
            {
                foreach (var f in got)
                {
                    var suffix = f["/dev/".Length..];
                    Assert.True(!suffix.Contains('/'), "depth 1 should not include nested files: " + f);
                }
            }),
        new("depth limit two", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, null, 2,
            got =>
            {
                ContainsAll(got, "/dev/a.ts", "/dev/z/a.ts");
                ContainsNone(got, "/dev/x/y/a.ts");
            }),
        new("mixed extensions only ts", MixedExtensionHost, "", "", [".ts"], null, null, 0,
            got =>
            {
                foreach (var f in got)
                {
                    Assert.True(f.EndsWith(".ts"), "should only have .ts files: " + f);
                }
            }),
        new("mixed extensions ts and tsx", MixedExtensionHost, "", "", [".ts", ".tsx"], null, null, 0,
            got =>
            {
                foreach (var f in got)
                {
                    Assert.True(f.EndsWith(".ts") || f.EndsWith(".tsx"), "should only have .ts or .tsx files: " + f);
                }
            }),
        new("mixed extensions js and jsx", MixedExtensionHost, "", "", [".js", ".jsx"], null, null, 0,
            got =>
            {
                foreach (var f in got)
                {
                    Assert.True(f.EndsWith(".js") || f.EndsWith(".jsx"), "should only have .js or .jsx files: " + f);
                }
            }),
        new("min js files excluded by wildcard", CaseInsensitiveHost, "", "", [".js"], null, ["js/*"], 0,
            got =>
            {
                ContainsAll(got, "/dev/js/a.js", "/dev/js/b.js");
                ContainsNone(got, "/dev/js/d.min.js", "/dev/js/ab.min.js");
            }),
        new("min js exclusion is case-sensitive on case-sensitive FS", CaseSensitiveHost, "", "", [".js"], null, ["js/*"], 0,
            got => ContainsAll(got, "/dev/js/a.js", "/dev/js/b.js", "/dev/js/d.MIN.js")),
        new("min js files explicitly included", CaseInsensitiveHost, "", "", [".js"], null, ["js/*.min.js"], 0,
            got => ContainsAll(got, "/dev/js/d.min.js", "/dev/js/ab.min.js")),
        new("min js files included when pattern mentions .min.", CaseInsensitiveHost, "", "", [".js"], null, ["js/*.min.*"], 0,
            got =>
            {
                Assert.Equal(2, got.Length);
                ContainsAll(got, "/dev/js/d.min.js", "/dev/js/ab.min.js");
            }),
        new("exclude literal node_modules folder", CommonFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], ["node_modules"], ["**/*"], 0,
            got =>
            {
                ContainsAll(got, "/dev/a.ts");
                ContainsNone(got, "/dev/node_modules/a.ts");
            }),
        new("same named declarations include ts", SameNamedDeclarationsHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["*.ts"], 0,
            got => Assert.NotEmpty(got)),
        new("same named declarations include tsx", SameNamedDeclarationsHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["*.tsx"], 0,
            got =>
            {
                foreach (var f in got)
                {
                    Assert.True(f.EndsWith(".tsx"), "should only have .tsx files: " + f);
                }
            }),
        new("empty includes returns all matching files", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, null, 0,
            got =>
            {
                Assert.NotEmpty(got);
                ContainsAll(got, "/dev/a.ts");
            }),
        new("nil extensions returns all files", CaseInsensitiveHost, "", "", null, null, null, 0,
            got => ContainsAll(got, "/dev/a.ts", "/dev/a.js")),
        new("empty extensions slice returns all files", CaseInsensitiveHost, "", "", [], null, null, 0,
            got => Assert.NotEmpty(got)),
    ];

    public static IEnumerable<object[]> ReadDirectoryData() => AsRows(_readDirectoryCases);

    [Theory]
    [MemberData(nameof(ReadDirectoryData))]
    public void TestReadDirectory(ReadDirCase tc) => RunReadDirectoryCase(tc);

    // --- TestReadDirectoryEdgeCases ---

    private static readonly ReadDirCase[] _readDirectoryEdgeCases =
    [
        new("rooted include path", CaseInsensitiveHost, "", "", [".ts"], null, ["/dev/a.ts"], 0,
            got => ContainsAll(got, "/dev/a.ts")),
        new("include with extension in path", CaseInsensitiveHost, "", "", [".ts"], null, ["a.ts"], 0,
            got => ContainsAll(got, "/dev/a.ts")),
        new("special regex characters in path", () => FromMap(new Dictionary<string, string>
        {
            ["/dev/file+test.ts"] = "",
            ["/dev/file[0].ts"] = "",
            ["/dev/file(1).ts"] = "",
            ["/dev/file$money.ts"] = "",
            ["/dev/file^start.ts"] = "",
            ["/dev/file|pipe.ts"] = "",
            ["/dev/file#hash.ts"] = "",
        }, false), "", "", [".ts"], null, ["file+test.ts"], 0,
            got => ContainsAll(got, "/dev/file+test.ts")),
        new("include pattern starting with question mark", CaseInsensitiveHost, "", "", [".ts"], null, ["?.ts"], 0,
            got => ContainsAll(got, "/dev/a.ts", "/dev/b.ts")),
        new("include pattern starting with star", CaseInsensitiveHost, "", "", [".ts"], null, ["*b.ts"], 0,
            got => ContainsAll(got, "/dev/b.ts")),
        new("case insensitive file matching", () => FromMap(new Dictionary<string, string>
        {
            ["/dev/File.ts"] = "",
            ["/dev/FILE.ts"] = "",
        }, true), "", "", [".ts"], null, ["*.ts"], 0,
            got => Assert.Equal(2, got.Length)),
        new("nested subdirectory base path", CaseSensitiveHost, "", "", [".ts"], null, ["q/a/c/b/d.ts"], 0,
            got => ContainsAll(got, "/dev/q/a/c/b/d.ts")),
        new("current directory differs from path", CaseInsensitiveHost, "", "", [".ts"], null, ["z/*.ts"], 0,
            got => Assert.NotEmpty(got)),
    ];

    public static IEnumerable<object[]> ReadDirectoryEdgeCaseData() => AsRows(_readDirectoryEdgeCases);

    [Theory]
    [MemberData(nameof(ReadDirectoryEdgeCaseData))]
    public void TestReadDirectoryEdgeCases(ReadDirCase tc) => RunReadDirectoryCase(tc);

    // --- TestReadDirectoryEmptyIncludes ---

    private static readonly ReadDirCase[] _readDirectoryEmptyIncludes =
    [
        new("empty includes slice behavior", () => FromMap(new Dictionary<string, string> { ["/root/a.ts"] = "" }, true),
            "/", "/root", [".ts"], null, [], 0,
            got =>
            {
                if (got.Length == 0)
                {
                    return;
                }

                ContainsAll(got, "/root/a.ts");
            }),
    ];

    public static IEnumerable<object[]> ReadDirectoryEmptyIncludesData() => AsRows(_readDirectoryEmptyIncludes);

    [Theory]
    [MemberData(nameof(ReadDirectoryEmptyIncludesData))]
    public void TestReadDirectoryEmptyIncludes(ReadDirCase tc) => RunReadDirectoryCase(tc);

    // --- TestReadDirectorySymlinkCycle ---

    // 循环符号链接由 vfs 层用 Realpath 检测；带循环的目录在遍历时被跳过。
    private static FS SymlinkCycleHost() => FromMap(new Dictionary<string, object>
    {
        ["/root/file.ts"] = "",
        ["/root/a/file.ts"] = "",
        ["/root/a/b"] = Symlink("/root/a"),
    }, true);

    private static readonly ReadDirCase[] _readDirectorySymlinkCycle =
    [
        new("detects and skips symlink cycles", SymlinkCycleHost, "/", "/root", [".ts"], null, ["**/*"], 0,
            got => Assert.Equal(["/root/file.ts", "/root/a/file.ts"], got)),
    ];

    public static IEnumerable<object[]> ReadDirectorySymlinkCycleData() => AsRows(_readDirectorySymlinkCycle);

    [Theory]
    [MemberData(nameof(ReadDirectorySymlinkCycleData))]
    public void TestReadDirectorySymlinkCycle(ReadDirCase tc) => RunReadDirectoryCase(tc);

    // --- TestReadDirectoryMatchesTypeScriptBaselines（与已 promotion 的 TS baseline 对齐） ---

    private static readonly ReadDirCase[] _readDirectoryBaselineCases =
    [
        new("sorted in include order then alphabetical", () => FromMap(new Dictionary<string, string>
        {
            ["/dev/z/a.ts"] = "",
            ["/dev/z/aba.ts"] = "",
            ["/dev/z/abz.ts"] = "",
            ["/dev/z/b.ts"] = "",
            ["/dev/z/bba.ts"] = "",
            ["/dev/z/bbz.ts"] = "",
            ["/dev/x/a.ts"] = "",
            ["/dev/x/aa.ts"] = "",
            ["/dev/x/b.ts"] = "",
        }, false), "", "", [".ts", ".tsx", ".d.ts"], null, ["z/*.ts", "x/*.ts"], 0,
            got => Assert.Equal(
                ["/dev/z/a.ts", "/dev/z/aba.ts", "/dev/z/abz.ts", "/dev/z/b.ts", "/dev/z/bba.ts", "/dev/z/bbz.ts",
                 "/dev/x/a.ts", "/dev/x/aa.ts", "/dev/x/b.ts"], got)),
        new("recursive wildcards match dotted directories", DottedFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/.*/*"], 0,
            got =>
            {
                string[] expected = ["/dev/.z/c.ts", "/dev/g.min.js/.g/g.ts", "/dev/w/.u/e.ts", "/dev/x/.y/a.ts"];
                Assert.Equal(expected.Length, got.Length);
                ContainsAll(got, expected);
            }),
        new("common package folders implicitly excluded with wildcard", CommonFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/a.ts"], 0,
            got => Assert.Equal(["/dev/a.ts", "/dev/x/a.ts"], got)),
        new("js wildcard excludes min js files", CaseInsensitiveHost, "", "", [".js"], null, ["js/*"], 0,
            got => Assert.Equal(["/dev/js/a.js", "/dev/js/b.js"], got)),
        new("explicit min js pattern includes min files", CaseInsensitiveHost, "", "", [".js"], null, ["js/*.min.js"], 0,
            got =>
            {
                Assert.Equal(2, got.Length);
                ContainsAll(got, "/dev/js/ab.min.js", "/dev/js/d.min.js");
            }),
        new("literal excludes baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["b.ts"], ["a.ts", "b.ts"], 0,
            got => Assert.Equal(["/dev/a.ts"], got)),
        new("wildcard excludes baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["*.ts", "z/??z.ts", "*/b.ts"],
            ["a.ts", "b.ts", "z/a.ts", "z/abz.ts", "z/aba.ts", "x/b.ts"], 0,
            got => Assert.Equal(["/dev/z/a.ts", "/dev/z/aba.ts"], got)),
        new("recursive excludes baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**/b.ts"],
            ["a.ts", "b.ts", "x/a.ts", "x/b.ts", "x/y/a.ts", "x/y/b.ts"], 0,
            got => Assert.Equal(["/dev/a.ts", "/dev/x/a.ts", "/dev/x/y/a.ts"], got)),
        new("question mark baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["x/?.ts"], 0,
            got => Assert.Equal(["/dev/x/a.ts", "/dev/x/b.ts"], got)),
        new("recursive directory pattern baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/a.ts"], 0,
            got => Assert.Equal(["/dev/a.ts", "/dev/x/a.ts", "/dev/x/y/a.ts", "/dev/z/a.ts"], got)),
        new("case sensitive baseline", CaseSensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/A.ts"], 0,
            got => Assert.Equal(["/dev/A.ts"], got)),
        new("exclude folders baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["z", "x"], ["**/*"], 0,
            got =>
            {
                foreach (var f in got)
                {
                    Assert.True(!f.Contains("/z/") && !f.Contains("/x/"), "should not contain z or x: " + f);
                }

                ContainsAll(got, "/dev/a.ts", "/dev/b.ts");
            }),
        new("implicit glob expansion baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["z"], 0,
            got => Assert.Equal(
                ["/dev/z/a.ts", "/dev/z/aba.ts", "/dev/z/abz.ts", "/dev/z/b.ts", "/dev/z/bba.ts", "/dev/z/bbz.ts"], got)),
        new("trailing recursive directory baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**"], 0,
            got => Assert.Empty(got)),
        new("exclude trailing recursive directory baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**"], ["**/*"], 0,
            got => Assert.Empty(got)),
        new("multiple recursive directory patterns baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/x/**/*"], 0,
            got => ContainsAll(got, "/dev/x/a.ts", "/dev/x/aa.ts", "/dev/x/b.ts", "/dev/x/y/a.ts", "/dev/x/y/b.ts")),
        new("include dirs with starstar prefix baseline", CaseSensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["**/x", "**/a/**/b"], 0,
            got => ContainsAll(got, "/dev/x/a.ts", "/dev/x/b.ts", "/dev/q/a/c/b/d.ts")),
        new("dotted folders not implicitly included baseline", DottedFoldersHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["x/**/*", "w/*/*"], 0,
            got =>
            {
                ContainsAll(got, "/dev/x/d.ts", "/dev/x/y/d.ts");
                ContainsNone(got, "/dev/x/.y/a.ts", "/dev/x/y/.e.ts", "/dev/w/.u/e.ts");
            }),
        new("include paths outside project baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], null, ["*", "/ext/*"], 0,
            got => ContainsAll(got, "/dev/a.ts", "/ext/ext.ts")),
        new("include files with double dots baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["**"], ["/ext/b/a..b.ts"], 0,
            got => ContainsAll(got, "/ext/b/a..b.ts")),
        new("exclude files with double dots baseline", CaseInsensitiveHost, "", "", [".ts", ".tsx", ".d.ts"], ["/ext/b/a..b.ts"], ["/ext/**/*"], 0,
            got =>
            {
                ContainsAll(got, "/ext/ext.ts");
                ContainsNone(got, "/ext/b/a..b.ts");
            }),
    ];

    public static IEnumerable<object[]> ReadDirectoryBaselineData() => AsRows(_readDirectoryBaselineCases);

    [Theory]
    [MemberData(nameof(ReadDirectoryBaselineData))]
    public void TestReadDirectoryMatchesTypeScriptBaselines(ReadDirCase tc) => RunReadDirectoryCase(tc);
}

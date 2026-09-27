// ported from: internal/vfs/vfsmatch/vfsmatch_test.go（内部函数/边界/包目录部分）
//
// 这些用例直接调用 Go 包内未导出函数；C# 侧对应 internal：`MatchFiles`（= matchFiles）、
// `TryCompileGlobPattern`（= compileGlobPattern，`(T,bool)` → TryX+out）、`GetBasePaths`、`NextPathPartParts`、
// `EnsureTrailingSlash`，类型 `GlobPattern`（= globPattern）的 `Matches`。
using TypeScript.Vfs;
using TypeScript.Vfs.VfsMatch;
using Xunit;
using static TypeScript.Vfs.VfsMatch.VfsMatch;
using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class VfsMatchInternalsTests
{
    // --- TestGlobPatternInternals ---

    [Fact]
    public void TestGlobPatternInternals_NextPathPartHandlesConsecutiveSlashes()
    {
        // Test path with consecutive slashes
        const string path = "/dev//foo///bar";

        // First call - returns empty for root
        var (part, offset, ok) = NextPathPartParts(path, "", 0);
        Assert.True(ok);
        Assert.Equal("", part);
        Assert.Equal(1, offset);

        // Second call - should skip consecutive slashes after /dev
        (part, offset, ok) = NextPathPartParts(path, "", 1);
        Assert.True(ok);
        Assert.Equal("dev", part);

        // Third call - should skip the double slashes before foo
        (part, offset, ok) = NextPathPartParts(path, "", offset);
        Assert.True(ok);
        Assert.Equal("foo", part);

        // Fourth call - should skip the triple slashes before bar
        (part, _, ok) = NextPathPartParts(path, "", offset);
        Assert.True(ok);
        Assert.Equal("bar", part);
    }

    [Fact]
    public void TestGlobPatternInternals_NextPathPartHandlesPathEndingWithSlashes()
    {
        const string path = "/dev/";

        // Skip to after "dev"
        var (_, offset, ok) = NextPathPartParts(path, "", 0); // root
        Assert.True(ok);
        (_, offset, ok) = NextPathPartParts(path, "", offset); // dev
        Assert.True(ok);

        // Now at trailing slash, should return not ok
        (_, _, ok) = NextPathPartParts(path, "", offset);
        Assert.False(ok);
    }

    [Fact]
    public void TestGlobPatternInternals_NextPathPartPartsHandlesEmptyPrefix()
    {
        const string path = "/dev//foo";

        var (part, offset, ok) = NextPathPartParts("", path, 0);
        Assert.True(ok);
        Assert.Equal("", part);
        Assert.Equal(1, offset);

        (part, offset, ok) = NextPathPartParts("", path, offset);
        Assert.True(ok);
        Assert.Equal("dev", part);

        (part, _, ok) = NextPathPartParts("", path, offset);
        Assert.True(ok);
        Assert.Equal("foo", part);
    }

    [Fact]
    public void TestGlobPatternInternals_NextPathPartPartsReturnsNotOkWhenOnlySlashesRemain()
    {
        const string prefix = "/dev/";
        const string suffix = "foo";

        var (_, offset, ok) = NextPathPartParts(prefix, suffix, 0); // root
        Assert.True(ok);

        var (part, offset2, ok2) = NextPathPartParts(prefix, suffix, offset); // dev
        Assert.True(ok2);
        Assert.Equal("dev", part);

        (part, offset, ok) = NextPathPartParts(prefix, suffix, offset2); // foo
        Assert.True(ok);
        Assert.Equal("foo", part);
        Assert.Equal(prefix.Length + suffix.Length, offset);

        (_, _, ok) = NextPathPartParts(prefix, suffix, offset);
        Assert.False(ok);
    }

    [Fact]
    public void TestGlobPatternInternals_NextPathPartPartsParsesFromSuffixRegion()
    {
        const string prefix = "/";
        const string suffix = "a";

        var (part, offset, ok) = NextPathPartParts(prefix, suffix, 0); // root
        Assert.True(ok);
        Assert.Equal("", part);
        Assert.Equal(1, offset);

        (part, _, ok) = NextPathPartParts(prefix, suffix, offset);
        Assert.True(ok);
        Assert.Equal("a", part);
    }

    [Fact]
    public void TestGlobPatternInternals_QuestionMarkSegmentAtEndOfString()
    {
        // Create pattern with question mark that should fail when string is exhausted
        Assert.True(TryCompileGlobPattern("a?", "/", Usage.Files, true, out var p));

        // Should match "ab"
        Assert.True(p!.Matches("/ab"));

        // Should NOT match "a" (question mark requires a character)
        Assert.False(p!.Matches("/a"));
    }

    [Fact]
    public void TestGlobPatternInternals_StarSegmentWithComplexPattern()
    {
        // Pattern like "a*b*c" requires backtracking in star matching
        Assert.True(TryCompileGlobPattern("a*b*c", "/", Usage.Files, true, out var p));

        Assert.True(p!.Matches("/abc"));
        Assert.True(p!.Matches("/aXbYc"));
        Assert.True(p!.Matches("/aXXXbYYYc"));
        Assert.False(p!.Matches("/aXbY"));
    }

    [Fact]
    public void TestGlobPatternInternals_EnsureTrailingSlashWithExistingSlash()
    {
        // Test that ensureTrailingSlash doesn't double-add slashes
        Assert.Equal("/dev/", EnsureTrailingSlash("/dev/"));
        Assert.Equal("/", EnsureTrailingSlash("/"));
    }

    [Fact]
    public void TestGlobPatternInternals_EnsureTrailingSlashWithEmptyString()
    {
        Assert.Equal("", EnsureTrailingSlash(""));
    }

    [Fact]
    public void TestGlobPatternInternals_LiteralComponentWithPackageFolderInInclude()
    {
        // When a literal include path goes through a package folder,
        // the skipPackageFolders flag on literal components should not block it
        // because literal components in includes don't have skipPackageFolders=true
        var host = FromMap(new Dictionary<string, string> { ["/dev/node_modules/pkg/index.ts"] = "" }, false);

        // Explicit literal path should work
        var got = MatchFiles("/dev", [".ts"], null, ["node_modules/pkg/index.ts"], false, "/", UnlimitedDepth, host);
        Assert.Contains("/dev/node_modules/pkg/index.ts", got);
    }

    // --- TestMatchSegmentsEdgeCases ---

    [Fact]
    public void TestMatchSegmentsEdgeCases_QuestionMarkBeforeSlashInString()
    {
        Assert.True(TryCompileGlobPattern("a?b", "/", Usage.Files, true, out var p));

        // "a/b" should not match "a?b" pattern since ? shouldn't match /, but this is a single
        // component pattern, so / wouldn't be in the component.
        Assert.True(p!.Matches("/aXb"));   // X matches ?
        Assert.False(p!.Matches("/ab"));   // nothing to match ?
        Assert.False(p!.Matches("/aXYb")); // XY is too many chars for ?
    }

    [Fact]
    public void TestMatchSegmentsEdgeCases_StarWithNoTrailingContent()
    {
        Assert.True(TryCompileGlobPattern("a*", "/", Usage.Files, true, out var p));

        Assert.True(p!.Matches("/a"));
        Assert.True(p!.Matches("/abc"));
        Assert.True(p!.Matches("/aXYZ"));
    }

    [Fact]
    public void TestMatchSegmentsEdgeCases_MultipleStarsInPattern()
    {
        Assert.True(TryCompileGlobPattern("*a*", "/", Usage.Files, true, out var p));

        Assert.True(p!.Matches("/a"));
        Assert.True(p!.Matches("/Xa"));
        Assert.True(p!.Matches("/aX"));
        Assert.True(p!.Matches("/XaY"));
        Assert.False(p!.Matches("/XYZ")); // no 'a'
    }

    [Fact]
    public void TestMatchSegmentsEdgeCases_MultipleStarsRequiringBacktracking()
    {
        // These patterns require proper backtracking to match correctly.
        // A naive greedy algorithm would fail on these.

        // Pattern: *a*a - must find two 'a' characters
        Assert.True(TryCompileGlobPattern("*a*a", "/", Usage.Files, true, out var p1));
        Assert.True(p1!.Matches("/aa"));     // minimal: first * matches "", second * matches ""
        Assert.True(p1!.Matches("/Xaa"));    // first * matches "X"
        Assert.True(p1!.Matches("/aXa"));    // second * matches "X"
        Assert.True(p1!.Matches("/XaYa"));   // both * match chars
        Assert.True(p1!.Matches("/aaaa"));   // multiple a's
        Assert.False(p1!.Matches("/a"));     // only one 'a'
        Assert.False(p1!.Matches("/Xa"));    // only one 'a'
        Assert.False(p1!.Matches("/aX"));    // only one 'a', doesn't end with 'a'
        Assert.False(p1!.Matches("/XaYaZ")); // doesn't end with 'a'

        // Pattern: *a*b*c - must find a, then b, then c in order
        Assert.True(TryCompileGlobPattern("*a*b*c", "/", Usage.Files, true, out var p2));
        Assert.True(p2!.Matches("/abc"));       // minimal
        Assert.True(p2!.Matches("/XaYbZc"));    // chars between
        Assert.True(p2!.Matches("/aXbYc"));     // chars between
        Assert.True(p2!.Matches("/aaabbbccc")); // repeated chars
        Assert.False(p2!.Matches("/ab"));       // missing c
        Assert.False(p2!.Matches("/ac"));       // missing b
        Assert.False(p2!.Matches("/cba"));      // wrong order
        Assert.False(p2!.Matches("/abcX"));     // doesn't end with c

        // Pattern: *a*a*a - must find three 'a' characters
        Assert.True(TryCompileGlobPattern("*a*a*a", "/", Usage.Files, true, out var p3));
        Assert.True(p3!.Matches("/aaa"));
        Assert.True(p3!.Matches("/aXaYa"));
        Assert.True(p3!.Matches("/XaYaZa"));
        Assert.False(p3!.Matches("/aa"));  // only two 'a's
        Assert.False(p3!.Matches("/aaX")); // doesn't end with 'a'

        // Pattern: a*b*a - starts with a, ends with a, has b in middle
        Assert.True(TryCompileGlobPattern("a*b*a", "/", Usage.Files, true, out var p4));
        Assert.True(p4!.Matches("/aba"));
        Assert.True(p4!.Matches("/aXbYa"));
        Assert.True(p4!.Matches("/abba"));  // b appears, ends with a
        Assert.False(p4!.Matches("/ab"));   // doesn't end with a
        Assert.False(p4!.Matches("/aba ")); // trailing space
        Assert.False(p4!.Matches("/Xaba")); // doesn't start with a (hidden file rule may affect)
    }

    [Fact]
    public void TestMatchSegmentsEdgeCases_PathologicalPatternPerformance()
    {
        // This pattern could cause exponential backtracking in naive implementations.
        // Pattern: *a*a*a*a*b against "aaaaaaaaaaaaaaaa" (no b) - should return false quickly, not hang.
        Assert.True(TryCompileGlobPattern("*a*a*a*a*b", "/", Usage.Files, true, out var p));

        Assert.False(p!.Matches("/aaaaaaaaaaaaaaaa"));  // no 'b' at end
        Assert.False(p!.Matches("/aaaaaaaaaaaaaaaaX")); // ends with X not b
        Assert.True(p!.Matches("/aaaab"));              // minimal match
        Assert.True(p!.Matches("/XaYaZaWab"));          // complex match
    }

    [Fact]
    public void TestMatchSegmentsEdgeCases_LiteralSegmentNotMatching()
    {
        Assert.True(TryCompileGlobPattern("abcdefgh.ts", "/", Usage.Files, true, out var p));

        Assert.False(p!.Matches("/abc.ts"));     // different literal
        Assert.True(p!.Matches("/abcdefgh.ts")); // exact match
    }

    [Fact]
    public void TestMatchSegmentsEdgeCases_QuestionMarkMatchesMultiByteUnicodeRune()
    {
        // ? should match one full Unicode codepoint, not one byte/one UTF-16 unit.
        // 'é' 是 2 字节 / U+00E9（1 个 UTF-16 单元），'中' 3 字节（1 单元），'🎉' 4 字节（代理对）。
        Assert.True(TryCompileGlobPattern("?.ts", "/", Usage.Files, true, out var p1));

        Assert.True(p1!.Matches("/a.ts"));   // single ASCII char
        Assert.True(p1!.Matches("/é.ts"));   // 2-byte rune
        Assert.True(p1!.Matches("/中.ts"));  // 3-byte rune
        Assert.True(p1!.Matches("/🎉.ts"));  // 4-byte rune (surrogate pair in UTF-16)
        Assert.False(p1!.Matches("/.ts"));   // empty - no char for ? to match
        Assert.False(p1!.Matches("/ab.ts")); // two chars

        // Two question marks should match exactly two runes
        Assert.True(TryCompileGlobPattern("??.ts", "/", Usage.Files, true, out var p2));

        Assert.True(p2!.Matches("/ab.ts"));  // two ASCII chars
        Assert.True(p2!.Matches("/é中.ts")); // two multi-byte runes
        Assert.True(p2!.Matches("/🎉é.ts")); // 4-byte + 2-byte runes
        Assert.False(p2!.Matches("/a.ts"));  // only one char
        Assert.False(p2!.Matches("/abc.ts")); // three chars
    }

    [Fact]
    public void TestMatchSegmentsEdgeCases_StarMatchesMultiByteUnicodeRunesCorrectly()
    {
        // * should advance by full runes during backtracking.

        // Pattern: *é.ts - anything ending in é.ts
        Assert.True(TryCompileGlobPattern("*é.ts", "/", Usage.Files, true, out var p));
        Assert.True(p!.Matches("/é.ts"));
        Assert.True(p!.Matches("/café.ts"));
        Assert.False(p!.Matches("/cafe.ts")); // 'e' != 'é'

        // Pattern: *🎉* - contains 🎉 somewhere
        Assert.True(TryCompileGlobPattern("*🎉*", "/", Usage.Files, true, out var p2));
        Assert.True(p2!.Matches("/🎉"));
        Assert.True(p2!.Matches("/a🎉b"));
        Assert.False(p2!.Matches("/abc"));
    }

    // --- TestReadDirectoryConsecutiveSlashes ---

    [Fact]
    public void TestReadDirectoryConsecutiveSlashes()
    {
        var host = FromMap(new Dictionary<string, string>
        {
            ["/dev/a.ts"] = "",
            ["/dev/x/b.ts"] = "",
        }, false);

        // matchFiles 会先规范化路径，这里验证内部处理连续斜杠时不会漏文件
        var got = MatchFiles("/dev", [".ts"], null, ["**/*.ts"], false, "/", UnlimitedDepth, host);
        Assert.True(got.Length >= 2, "should find files");
        Assert.Contains("/dev/a.ts", got);
        Assert.Contains("/dev/x/b.ts", got);
    }

    // --- TestGlobPatternLiteralWithPackageFolders ---

    [Fact]
    public void TestGlobPatternLiteralWithPackageFolders_WildcardSkipsPackageFolders()
    {
        // Wildcard patterns should skip node_modules
        var host = FromMap(new Dictionary<string, string>
        {
            ["/dev/a.ts"] = "",
            ["/dev/node_modules/b.ts"] = "",
        }, false);

        var got = MatchFiles("/dev", [".ts"], null, ["*/*.ts"], false, "/", UnlimitedDepth, host);
        Assert.DoesNotContain("/dev/node_modules/b.ts", got);
    }

    [Fact]
    public void TestGlobPatternLiteralWithPackageFolders_ExplicitLiteralIncludesPackageFolder()
    {
        // Explicit literal paths should include package folders
        var host = FromMap(new Dictionary<string, string>
        {
            ["/dev/node_modules/b.ts"] = "",
        }, false);

        var got = MatchFiles("/dev", [".ts"], null, ["node_modules/b.ts"], false, "/", UnlimitedDepth, host);
        Assert.Contains("/dev/node_modules/b.ts", got);
    }
}

// ported from: internal/tspath/untitled_test.go
//
// Go 的 assert.Equal(t, x, y, msg) 的消息参数在 xunit 2.x 里没有对应物，未移植（只影响失败信息）。
using Xunit;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Tests;

public class TspathUntitledTests
{
    [Fact]
    public void TestUntitledPathHandling()
    {
        // Test that untitled paths are treated as rooted
        var untitledPath = "^/untitled/ts-nul-authority/Untitled-2";

        // GetEncodedRootLength should return 2 for "^/"
        var rootLength = GetEncodedRootLength(untitledPath);
        Assert.Equal(2, rootLength);

        // IsRootedDiskPath should return true
        var isRooted = IsRootedDiskPath(untitledPath);
        Assert.True(isRooted);

        // ToPath should not resolve untitled paths against current directory
        var currentDir = "/home/user/project";
        var path = ToPath(untitledPath, currentDir, true);
        // The path should be the original untitled path
        Assert.Equal("^/untitled/ts-nul-authority/Untitled-2", (string)path);

        // Test GetNormalizedAbsolutePath doesn't resolve untitled paths
        var normalized = GetNormalizedAbsolutePath(untitledPath, currentDir);
        Assert.Equal("^/untitled/ts-nul-authority/Untitled-2", normalized);
    }

    public sealed record EdgeCase(string Path, int Expected, bool IsRooted)
    {
        public override string ToString() => Path;
    }

    private static readonly EdgeCase[] _testCases =
    [
        new("^/", 2, true), // Minimal untitled path
        new("^/untitled/ts-nul-authority/test", 2, true), // Normal untitled path
        new("^", 0, false),   // Just ^ is not rooted
        new("^x", 0, false),  // ^x is not untitled
        new("^^/", 0, false), // ^^/ is not untitled
        new("x^/", 0, false), // x^/ is not untitled (doesn't start with ^)
        new("^/untitled/ts-nul-authority/path/with/deeper/structure", 2, true), // Deeper path
    ];

    public static IEnumerable<object[]> UntitledPathEdgeCaseData()
    {
        foreach (var testCase in _testCases)
        {
            yield return new object[] { testCase };
        }
    }

    [Theory]
    [MemberData(nameof(UntitledPathEdgeCaseData))]
    public void TestUntitledPathEdgeCases(EdgeCase tc)
    {
        // Test edge cases
        var rootLength = GetEncodedRootLength(tc.Path);
        Assert.Equal(tc.Expected, rootLength);

        var isRooted = IsRootedDiskPath(tc.Path);
        Assert.Equal(tc.IsRooted, isRooted);
    }
}

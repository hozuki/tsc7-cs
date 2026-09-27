// ported from: internal/tspath/startsWithDirectory_test.go
using Xunit;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Tests;

public class TspathStartsWithDirectoryTests
{
    // Go 的表驱动数据表用匿名 struct；C# 用一个 record 保留同样的字段与子测试名（ToString 即 name）。
    public sealed record Case(string Name, string FileName, string DirectoryName, bool UseCaseSensitiveFileNames, bool Expected)
    {
        public override string ToString() => Name;
    }

    private static readonly Case[] startsWithDirectoryTests =
    [
        new("exact match case sensitive", "/project/src/file.ts", "/project/src", true, true),
        new("exact match case insensitive", "/project/src/file.ts", "/PROJECT/SRC", false, true),
        new("case sensitive mismatch", "/project/src/file.ts", "/PROJECT/SRC", true, false),
        new("file not in directory", "/project/lib/file.ts", "/project/src", true, false),
        new("file in subdirectory", "/project/src/components/Button.tsx", "/project/src", true, true),
        new("file in parent directory", "/project/file.ts", "/project/src", true, false),
        new("windows style separators", "C:\\project\\src\\file.ts", "C:\\project\\src", true, true),
        new("mixed separators", "/project/src/file.ts", "\\project\\src", true, false),
        new("empty directory name", "/project/src/file.ts", "", true, false),
        new("empty file name", "", "/project/src", true, false),
        // File name doesn't start with directory + separator
        new("identical paths", "/project/src", "/project/src", true, false),
        new("directory with trailing separator", "/project/src/file.ts", "/project/src/", true, true),
        new("unicode characters", "/project/测试/file.ts", "/project/测试", true, true),
        new("unicode case insensitive", "/project/测试/file.ts", "/PROJECT/测试", false, true),
    ];

    private static readonly Case[] startsWithDirectoryEdgeCaseTests =
    [
        new("file name shorter than directory", "/proj", "/project", true, false),
        new("file name starts with directory but no separator", "/projectsrc/file.ts", "/project", true, false),
        new("relative paths", "src/file.ts", "src", true, true),
        new("absolute vs relative", "/project/src/file.ts", "project/src", true, false),
    ];

    public static IEnumerable<object[]> StartsWithDirectoryData() => AsRows(startsWithDirectoryTests);

    public static IEnumerable<object[]> StartsWithDirectoryEdgeCaseData() => AsRows(startsWithDirectoryEdgeCaseTests);

    private static IEnumerable<object[]> AsRows(Case[] cases)
    {
        foreach (var testCase in cases)
        {
            yield return new object[] { testCase };
        }
    }

    [Theory]
    [MemberData(nameof(StartsWithDirectoryData))]
    public void TestStartsWithDirectory(Case tt)
        => Assert.Equal(tt.Expected, StartsWithDirectory(tt.FileName, tt.DirectoryName, tt.UseCaseSensitiveFileNames));

    [Theory]
    [MemberData(nameof(StartsWithDirectoryEdgeCaseData))]
    public void TestStartsWithDirectoryEdgeCases(Case tt)
        => Assert.Equal(tt.Expected, StartsWithDirectory(tt.FileName, tt.DirectoryName, tt.UseCaseSensitiveFileNames));
}

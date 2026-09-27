// ported from: internal/testutil/tsbaseline/util.go
//
// 与 Go 的差异（逐条，都不改变字节语义）：
// 1) Go 的 `strings.NewReplacer(...)`（trie，逐**字节**扫描、同位置取最长匹配）→ 这里的 ReplaceAll：
//    逐 char 扫描，同一位置按**参数顺序**取首个匹配。两套替换表里没有任何一个 pattern 是另一个的前缀
//    （"/.ts/"、"/.lib/"、"/.src/"、 "bundled:///libs/"、"file:///./ts/"、"file:///./lib/"、"file:///./src/"），
//    所以"参数顺序"与"最长匹配"在本表上结果完全一致；替换文本（""、"/"、"file:///"）里也不含任何 pattern，
//    不存在 Go 单遍扫描与顺序 Replace 的差别。
// 2) Go 的 string 是 UTF-8 字节序列、本文件里都是按 ASCII 分隔符切分/替换 → C# 按 UTF-16 代码单元扫描，
//    结果字符串完全相同（docs/architecture.md §3.1）。
// 3) `regexp.MustCompile` → `Regex`（静态构造，RegexOptions.Compiled 与 Go 的预编译等价）。
// 4) Go 的 `string(tspath.ToPath(...))` → Path.Value（Path 是 record struct，见 TSPath/Path.cs）。

namespace TypeScript.TestUtil.TsBaseline;

using System.Text.RegularExpressions;
using static TypeScript.TSPath.TSPath;

public static partial class TsBaseline
{
    private static readonly Regex _lineDelimiter = new("\r?\n", RegexOptions.Compiled);

    private static readonly Regex _nonWhitespace = new(@"\S", RegexOptions.Compiled);

    private static readonly Regex _tsExtension = new(@"\.tsx?$", RegexOptions.Compiled);

    private static readonly Regex _testPathCharacters = new("""[\^<>:"|?*%]""", RegexOptions.Compiled);

    private static readonly Regex _testPathDotDot = new(@"\.\./", RegexOptions.Compiled);

    private const string _libFolder = "built/local/";

    private const string _builtFolder = "/.ts";

    /// <summary>Go: <c>testPathPrefixReplacer</c>（删掉前缀）。</summary>
    private static readonly (string Old, string New)[] _testPathPrefixReplacer =
    [
        ("/.ts/", ""),
        ("/.lib/", ""),
        ("/.src/", ""),
        ("bundled:///libs/", ""),
        ("file:///./ts/", "file:///"),
        ("file:///./lib/", "file:///"),
        ("file:///./src/", "file:///"),
    ];

    /// <summary>Go: <c>testPathTrailingReplacerTrailingSeparator</c>（保留尾分隔符）。</summary>
    private static readonly (string Old, string New)[] _testPathTrailingReplacerTrailingSeparator =
    [
        ("/.ts/", "/"),
        ("/.lib/", "/"),
        ("/.src/", "/"),
        ("bundled:///libs/", "/"),
        ("file:///./ts/", "file:///"),
        ("file:///./lib/", "file:///"),
        ("file:///./src/", "file:///"),
    ];

    /// <summary>Go: <c>func removeTestPathPrefixes(text string, retainTrailingDirectorySeparator bool) string</c>。</summary>
    private static string RemoveTestPathPrefixes(string text, bool retainTrailingDirectorySeparator)
    {
        if (retainTrailingDirectorySeparator)
        {
            return ReplaceAll(text, _testPathTrailingReplacerTrailingSeparator);
        }

        return ReplaceAll(text, _testPathPrefixReplacer);
    }

    /// <summary>Go: <c>func isDefaultLibraryFile(filePath string) bool</c>。</summary>
    private static bool IsDefaultLibraryFile(string filePath)
    {
        var fileName = GetBaseFileName(filePath);
        return fileName.StartsWith("lib.", StringComparison.Ordinal) && fileName.EndsWith(ExtensionDts, StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func isBuiltFile(filePath string) bool</c>。</summary>
    private static bool IsBuiltFile(string filePath)
    {
        return filePath.StartsWith(_libFolder, StringComparison.Ordinal)
            || filePath.StartsWith(EnsureTrailingDirectorySeparator(_builtFolder), StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func isTsConfigFile(path string) bool</c>。</summary>
    private static bool IsTsConfigFile(string path)
    {
        // !!! fix to check for just prefixes/suffixes
        return path.Contains("tsconfig", StringComparison.Ordinal) && path.Contains("json", StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func sanitizeTestFilePath(name string) string</c>。</summary>
    private static string SanitizeTestFilePath(string name)
    {
        var path = _testPathCharacters.Replace(name, "_");
        path = NormalizeSlashes(path);
        path = _testPathDotDot.Replace(path, "__dotdot/");
        path = ToPath(path, "", false /*useCaseSensitiveFileNames*/).Value;
        return path.StartsWith('/') ? path[1..] : path;
    }

    /// <summary>
    /// Go: <c>strings.NewReplacer(pairs...).Replace(text)</c>（见文件头第 1 条）。
    /// </summary>
    private static string ReplaceAll(string text, (string Old, string New)[] pairs)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var replaced = false;
            for (var pairIndex = 0; pairIndex < pairs.Length; pairIndex++)
            {
                var (oldValue, newValue) = pairs[pairIndex];
                if (oldValue.Length <= text.Length - index && string.CompareOrdinal(text, index, oldValue, 0, oldValue.Length) == 0)
                {
                    builder.Append(newValue);
                    index += oldValue.Length;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                builder.Append(text[index]);
                index++;
            }
        }

        return builder.ToString();
    }
}

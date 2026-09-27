// ported from: internal/tsoptions/wildcarddirectories.go
//
// 与 Go 的差异：
// - Go 的 `strings.ToLower`（完整多字符映射）→ `StringUtil.ToLowerJS`（docs §3.4：不能用 string.ToLower）。
// - Go 的 `strings.CutLast`/`IndexAny`/`LastIndexByte` → C# 的 span/IndexOf 等价写法（UTF-16 代码单元，
//   docs §3.1；路径分隔符 `*?` 都是 BMP 单单元字符，语义不变）。
// - `getWildcardDirectories` 在 Go 里边 range 边 delete 同一个 map（Go 允许），C# 的 Dictionary 不允许
//   边遍历边改 → 先取键快照，遍历时跳过已删除的键（与 Go 的 range 语义一致：被删掉的键不会再产生）。
using TypeScript.Collections;
using TypeScript.TSPath;
using TypeScript.Vfs.VfsMatch;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.VfsMatch.VfsMatch;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    // Go: func getWildcardDirectories(include []string, exclude []string, comparePathsOptions tspath.ComparePathsOptions) map[string]bool
    internal static Dictionary<string, bool>? GetWildcardDirectories(string[]? include, string[]? exclude, ComparePathsOptions comparePathsOptions)
    {
        // We watch a directory recursively if it contains a wildcard anywhere in a directory segment
        // of the pattern:
        //
        //  /a/b/**/d   - Watch /a/b recursively to catch changes to any d in any subfolder recursively
        //  /a/b/*/d    - Watch /a/b recursively to catch any d in any immediate subfolder, even if a new subfolder is added
        //  /a/b        - Watch /a/b recursively to catch changes to anything in any recursive subfoler
        //
        // We watch a directory without recursion if it contains a wildcard in the file segment of
        // the pattern:
        //
        //  /a/b/*      - Watch /a/b directly to catch any new file
        //  /a/b/a?z    - Watch /a/b directly to catch any new file matching a?z
        if (include is null || include.Length == 0)
        {
            return null;
        }

        var excludeMatcher = NewSpecMatcher(exclude ?? [], comparePathsOptions.CurrentDirectory, Usage.Exclude, comparePathsOptions.UseCaseSensitiveFileNames);

        var wildcardDirectories = new Dictionary<string, bool>();
        var wildCardKeyToPath = new Dictionary<string, string>();

        var recursiveKeys = new List<string>();

        foreach (var file in include)
        {
            var spec = NormalizePath(CombinePaths(comparePathsOptions.CurrentDirectory, file));
            if (excludeMatcher is not null && excludeMatcher.MatchString(spec))
            {
                continue;
            }

            var match = GetWildcardDirectoryFromSpec(spec, comparePathsOptions.UseCaseSensitiveFileNames);
            if (match is not null)
            {
                var key = match.Key;
                var path = match.Path;
                var recursive = match.Recursive;

                var existsPath = wildCardKeyToPath.TryGetValue(key, out var existingPath);
                var existingRecursive = false;

                if (existsPath)
                {
                    existingRecursive = wildcardDirectories[existingPath!];
                }

                if (!existsPath || (!existingRecursive && recursive))
                {
                    var pathToUse = path;
                    if (existsPath)
                    {
                        pathToUse = existingPath!;
                    }

                    wildcardDirectories[pathToUse] = recursive;

                    if (!existsPath)
                    {
                        wildCardKeyToPath[key] = path;
                    }

                    if (recursive)
                    {
                        recursiveKeys.Add(key);
                    }
                }
            }

            // Remove any subpaths under an existing recursively watched directory
            var paths = new List<string>(wildcardDirectories.Keys);
            foreach (var path in paths)
            {
                if (!wildcardDirectories.ContainsKey(path))
                {
                    continue; // Go 的 range 不会产生已被 delete 的键
                }

                foreach (var recursiveKey in recursiveKeys)
                {
                    var key = ToCanonicalKey(path, comparePathsOptions.UseCaseSensitiveFileNames);
                    if (key != recursiveKey && ContainsPath(recursiveKey, key, comparePathsOptions))
                    {
                        wildcardDirectories.Remove(path);
                    }
                }
            }
        }

        return wildcardDirectories;
    }

    // Go: func toCanonicalKey(path string, useCaseSensitiveFileNames bool) string
    internal static string ToCanonicalKey(string path, bool useCaseSensitiveFileNames) =>
        useCaseSensitiveFileNames ? path : ToLowerJS(path);

    // Go: type wildcardDirectoryMatch struct { Key, Path string; Recursive bool }
    internal sealed class WildcardDirectoryMatch
    {
        public string Key = "";
        public string Path = "";
        public bool Recursive;
    }

    // Go: func getWildcardDirectoryFromSpec(spec string, useCaseSensitiveFileNames bool) *wildcardDirectoryMatch
    internal static WildcardDirectoryMatch? GetWildcardDirectoryFromSpec(string spec, bool useCaseSensitiveFileNames)
    {
        // Find the first occurrence of a wildcard character
        var firstWildcard = spec.AsSpan().IndexOfAny('*', '?');
        if (firstWildcard != -1)
        {
            // Find the last directory separator before the wildcard
            var lastSepBeforeWildcard = spec.AsSpan(0, firstWildcard).LastIndexOf(DirectorySeparator);
            if (lastSepBeforeWildcard != -1)
            {
                var path = spec[..lastSepBeforeWildcard];
                var lastDirectorySeparatorIndex = spec.LastIndexOf(DirectorySeparator);

                // Determine if this should be watched recursively:
                // recursive if the wildcard appears in a directory segment (not just the final file segment)
                var recursive = firstWildcard < lastDirectorySeparatorIndex;

                return new WildcardDirectoryMatch
                {
                    Key = ToCanonicalKey(path, useCaseSensitiveFileNames),
                    Path = path,
                    Recursive = recursive,
                };
            }
        }

        // Go: strings.CutLast(spec, string(tspath.DirectorySeparator))
        var lastSeparatorIndex = spec.LastIndexOf(DirectorySeparator);
        if (lastSeparatorIndex >= 0)
        {
            var lastSegment = spec[(lastSeparatorIndex + 1)..];
            if (IsImplicitGlob(lastSegment))
            {
                var path = RemoveTrailingDirectorySeparator(spec);
                return new WildcardDirectoryMatch
                {
                    Key = ToCanonicalKey(path, useCaseSensitiveFileNames),
                    Path = path,
                    Recursive = true,
                };
            }
        }

        return null;
    }
}

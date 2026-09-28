// ported from: internal/vfs/vfsmatch/vfsmatch.go
//
// 本文件实现 MATCHING_ALGORITHM.md 里的 glob 匹配算法（与 Go 逐函数对应）。
//
// 偏离 Go 的地方：
//
// 1) docs §3.1：Go 按字节下标扫（`s[i]`、`utf8.DecodeRuneInString`），C# 的 `string` 是 UTF-16 代码单元
//    序列。本文件一律用 `char` 下标；"前进一个码点"（Go 的 `utf8.DecodeRuneInString` 的 size）用
//    `char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1`（`CodePointSize`）。
//    大小写比较里的码点解码沿用 BCL 的 `Rune.DecodeFromUtf16`（与 StringUtil 的私有 `DecodeRuneAt` 同口径：
//    孤立代理项 → U+FFFD、消耗 1 个单元），`Rune.ToLowerInvariant` 对应 Go 的单码点 simple 映射。
//    `strings.EqualFold` → `StringUtil.EquateStringCaseInsensitive`；`strings.ToLower` → `StringUtil.ToLowerJS`。
// 2) 未导出类型按 C# 习惯改名：`globPattern` → `GlobPattern`、`component` → `PatternComponent`、
//    `componentKind` → `ComponentKind`（`kindLiteral/kindWildcard/kindDoubleAsterisk` → `Literal/Wildcard/DoubleAsterisk`）、
//    `segment` → `PatternSegment`、`segmentKind` → `SegmentKind`（`segLiteral/segStar/segQuestion` → `Literal/Star/Question`）。
//    未导出方法一律 PascalCase（`matches` → `Matches`…），未导出字段 `_camelCase`；包级函数/常量进静态类 `VfsMatch`。
// 3) `compileGlobPattern` 的 `(globPattern, bool)` → `TryCompileGlobPattern(..., out GlobPattern? pattern)`（§3.2）。
// 4) `panic("unreachable: …")` → `Debug.Fail(...)`（抛 `DebugFailureException`）。
// 5) `[]string` 参数保留为 `string[]?`：Go 里 nil 与空切片在本文件里语义相同（都只判 `len(...) > 0`）。
using System.Diagnostics.CodeAnalysis;
using System.Text;
using TypeScript.Collections;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Vfs.VfsMatch;

/// <summary>Go: <c>type Usage int8</c>。枚举成员名按 docs §3.3 去掉类型前缀（UsageFiles → Files）。</summary>
public enum Usage
{
    Files = 0,
    Directories = 1,
    Exclude = 2,
}

/// <summary>Go 的 <c>globPattern</c>：编译好的 glob 模式，不用正则匹配文件路径。</summary>
internal sealed class GlobPattern
{
    private readonly PatternComponent[] _components; // path segments to match (e.g., ["src", "**", "*.ts"])
    private readonly bool _isExclude;                // exclude patterns have different matching rules
    private readonly bool _caseSensitive;
    private readonly bool _excludeMinJs;             // for "files" patterns, exclude .min.js by default

    internal GlobPattern(PatternComponent[] components, bool isExclude, bool caseSensitive, bool excludeMinJs)
    {
        _components = components;
        _isExclude = isExclude;
        _caseSensitive = caseSensitive;
        _excludeMinJs = excludeMinJs;
    }

    /// <summary>Go: <c>func (p *globPattern) matches(path string) bool</c>。</summary>
    internal bool Matches(string path) => MatchPathParts(path, "", 0, 0, false);

    /// <summary>
    /// Go: <c>func (p *globPattern) matchesParts(prefix, suffix string) bool</c>。
    /// 避免为 prefix+suffix 拼接出一个字符串（常见调用点是 prefix 以 '/' 结尾）。
    /// </summary>
    internal bool MatchesParts(string prefix, string suffix) => MatchPathParts(prefix, suffix, 0, 0, false);

    /// <summary>Go: <c>func (p *globPattern) matchesPrefixParts(prefix, suffix string) bool</c>。</summary>
    internal bool MatchesPrefixParts(string prefix, string suffix) => MatchPathParts(prefix, suffix, 0, 0, true);

    /// <summary>
    /// Go: <c>func (p *globPattern) matchPathParts(prefix, suffix string, pathOffset, compIdx int, prefixOnly bool) bool</c>。
    /// 操作由 prefix+suffix 组成的虚拟路径，偏移量是拼接后字符串上的下标。
    /// </summary>
    private bool MatchPathParts(string prefix, string suffix, int pathOffset, int compIdx, bool prefixOnly)
    {
        while (true)
        {
            var (pathPart, nextOffset, ok) = VfsMatch.NextPathPartParts(prefix, suffix, pathOffset);
            if (!ok)
            {
                if (prefixOnly)
                {
                    return true;
                }

                return PatternSatisfied(compIdx);
            }

            if (compIdx >= _components.Length)
            {
                return _isExclude && !prefixOnly;
            }

            var comp = _components[compIdx];
            switch (comp._kind)
            {
                case ComponentKind.DoubleAsterisk:
                    if (MatchPathParts(prefix, suffix, pathOffset, compIdx + 1, prefixOnly))
                    {
                        return true;
                    }

                    if (!_isExclude && (VfsMatch.IsHiddenPath(pathPart) || VfsMatch.IsPackageFolder(pathPart)))
                    {
                        return false;
                    }

                    pathOffset = nextOffset;
                    continue;
                case ComponentKind.Literal:
                    if (comp._skipPackageFolders && VfsMatch.IsPackageFolder(pathPart))
                    {
                        Debug.Fail("unreachable: literal components never have skipPackageFolders");
                    }

                    if (!StringsEqual(comp._literal!, pathPart))
                    {
                        return false;
                    }

                    break;
                case ComponentKind.Wildcard:
                    if (comp._skipPackageFolders && VfsMatch.IsPackageFolder(pathPart))
                    {
                        return false;
                    }

                    if (!MatchWildcard(comp._segments!, pathPart))
                    {
                        return false;
                    }

                    break;
            }

            pathOffset = nextOffset;
            compIdx++;
        }
    }

    /// <summary>Go: <c>func (p *globPattern) patternSatisfied(compIdx int) bool</c>。</summary>
    private bool PatternSatisfied(int compIdx)
    {
        // A pattern is satisfied when remaining components can match empty input.
        // For both include and exclude patterns, only trailing "**" components may match nothing.
        for (var i = compIdx; i < _components.Length; i++)
        {
            if (_components[i]._kind != ComponentKind.DoubleAsterisk)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>func (p *globPattern) matchWildcard(segs []segment, s string) bool</c>。</summary>
    private bool MatchWildcard(PatternSegment[] segs, string s)
    {
        // Include patterns: wildcards at start cannot match hidden files
        if (!_isExclude && segs.Length > 0 && VfsMatch.IsHiddenPath(s) && (segs[0]._kind == SegmentKind.Star || segs[0]._kind == SegmentKind.Question))
        {
            return false;
        }

        // Fast path: single * followed by literal suffix (e.g., "*.ts")
        if (segs.Length == 2 && segs[0]._kind == SegmentKind.Star && segs[1]._kind == SegmentKind.Literal)
        {
            // Go: len(s) < len(suffix) || !stringsEqual(suffix, s[len(s)-len(suffix):])
            var suffix = segs[1]._literal!;
            if (!HasSuffix(s, suffix, _caseSensitive))
            {
                return false;
            }

            return ShouldIncludeMinJs(s, segs);
        }

        return MatchSegments(segs, s) && ShouldIncludeMinJs(s, segs);
    }

    /// <summary>
    /// Go: <c>func (p *globPattern) matchSegments(segs []segment, s string) bool</c>。
    /// 迭代实现，只记录最后一个 star 的位置，避免指数级回溯；复杂度 O(n*m)。
    /// </summary>
    private bool MatchSegments(PatternSegment[] segs, string s)
    {
        int segIdx = 0, sIdx = 0, starSegIdx = -1, starSIdx = 0;

        while (sIdx < s.Length)
        {
            if (segIdx < segs.Length)
            {
                var seg = segs[segIdx];
                switch (seg._kind)
                {
                    case SegmentKind.Literal:
                        var end = sIdx + seg._literal!.Length;
                        if (end <= s.Length && StringsEqual(seg._literal!, s.AsSpan(sIdx, seg._literal!.Length)))
                        {
                            sIdx = end;
                            segIdx++;
                            continue;
                        }

                        break;
                    case SegmentKind.Question:
                        if (s[sIdx] != '/')
                        {
                            sIdx += VfsMatch.CodePointSize(s, sIdx);
                            segIdx++;
                            continue;
                        }

                        break;
                    case SegmentKind.Star:
                        // Record star position for backtracking, then try matching zero chars.
                        starSegIdx = segIdx;
                        starSIdx = sIdx;
                        segIdx++;
                        continue;
                }
            }

            // Current segment didn't match. Backtrack to last star if possible.
            if (starSegIdx >= 0 && starSIdx < s.Length && s[starSIdx] != '/')
            {
                // Star consumes one more character (rune), retry from segment after star.
                starSIdx += VfsMatch.CodePointSize(s, starSIdx);
                sIdx = starSIdx;
                segIdx = starSegIdx + 1;
                continue;
            }

            return false;
        }

        // Consume any trailing stars.
        while (segIdx < segs.Length && segs[segIdx]._kind == SegmentKind.Star)
        {
            segIdx++;
        }

        return segIdx >= segs.Length;
    }

    /// <summary>Go: <c>func (p *globPattern) shouldIncludeMinJs(filename string, segs []segment) bool</c>。</summary>
    private bool ShouldIncludeMinJs(string filename, PatternSegment[] segs)
    {
        if (!_excludeMinJs)
        {
            return true;
        }

        // Preserve legacy behavior:
        // - When matching is case-sensitive, only the exact ".min.js" suffix is excluded by default.
        // - When matching is case-insensitive, any casing variant is excluded by default.
        if (!HasMinJsSuffix(filename))
        {
            return true;
        }

        // Allow when the user's pattern explicitly references the .min. suffix.
        if (PatternMentionsMinSuffix(segs))
        {
            return true;
        }

        return false;
    }

    /// <summary>Go: <c>func (p *globPattern) hasMinJsSuffix(filename string) bool</c>。</summary>
    private bool HasMinJsSuffix(string filename)
    {
        // Go: strings.HasSuffix(filename, ".min.js") / strings.EqualFold(filename[len-len(minJs):], ".min.js")
        // 两个分支合起来就是 StringUtil.HasSuffix 的两种口径（长度不足时返回 false）。
        return HasSuffix(filename, ".min.js", _caseSensitive);
    }

    /// <summary>Go: <c>func (p *globPattern) patternMentionsMinSuffix(segs []segment) bool</c>。</summary>
    private bool PatternMentionsMinSuffix(PatternSegment[] segs)
    {
        foreach (var seg in segs)
        {
            if (seg._kind != SegmentKind.Literal)
            {
                continue;
            }

            var lit = seg._literal!;
            if (!_caseSensitive)
            {
                // Go: strings.ToLower（完整小写映射，JS 语义）→ ToLowerJS，见 docs §3.4。
                lit = ToLowerJS(lit);
            }

            if (lit.Contains(".min.js") || lit.Contains(".min."))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func (p *globPattern) stringsEqual(a, b string) bool</c>。按大小写敏感度比较。</summary>
    private bool StringsEqual(string a, string b) => _caseSensitive ? a == b : EquateStringCaseInsensitive(a, b);

    /// <summary>同上，但第二边是切片（避免为了比较而分配子串，docs §3.8）。</summary>
    private bool StringsEqual(string a, ReadOnlySpan<char> b)
    {
        if (_caseSensitive)
        {
            return a.AsSpan().SequenceEqual(b);
        }

        return VfsMatch.EquateSpanCaseInsensitive(a.AsSpan(), b);
    }
}

/// <summary>
/// Go 的 <c>component</c>：glob 模式里的一个路径段。
/// 例："src"（literal）、"*"（wildcard）、"*.ts"（wildcard）、"**"（recursive）。
/// </summary>
internal readonly struct PatternComponent
{
    internal readonly ComponentKind _kind;
    internal readonly string? _literal;              // for Literal: the exact string to match
    internal readonly PatternSegment[]? _segments;   // for Wildcard: parsed wildcard pattern

    // Include patterns with wildcards skip common package folders (node_modules, etc.)
    internal readonly bool _skipPackageFolders;

    private PatternComponent(ComponentKind kind, string? literal, PatternSegment[]? segments, bool skipPackageFolders)
    {
        _kind = kind;
        _literal = literal;
        _segments = segments;
        _skipPackageFolders = skipPackageFolders;
    }

    // Go: component{kind: kindLiteral, literal: s}
    internal static PatternComponent Literal(string literal) => new(ComponentKind.Literal, literal, null, false);

    // Go: component{kind: kindDoubleAsterisk}
    internal static PatternComponent DoubleAsterisk() => new(ComponentKind.DoubleAsterisk, null, null, false);

    // Go: component{kind: kindWildcard, segments: …, skipPackageFolders: isInclude}
    internal static PatternComponent Wildcard(PatternSegment[] segments, bool skipPackageFolders) => new(ComponentKind.Wildcard, null, segments, skipPackageFolders);
}

internal enum ComponentKind
{
    Literal = 0,        // exact match (e.g., "src")
    Wildcard = 1,       // contains * or ? (e.g., "*.ts")
    DoubleAsterisk = 2, // ** matches zero or more directories
}

/// <summary>Go 的 <c>segment</c>：通配符组件里的一小段。例："*.ts" → [Star, Literal(".ts")]。</summary>
internal readonly struct PatternSegment
{
    internal readonly SegmentKind _kind;
    internal readonly string? _literal; // only for Literal

    private PatternSegment(SegmentKind kind, string? literal)
    {
        _kind = kind;
        _literal = literal;
    }

    internal static PatternSegment Literal(string literal) => new(SegmentKind.Literal, literal);

    internal static PatternSegment Star() => new(SegmentKind.Star, null);

    internal static PatternSegment Question() => new(SegmentKind.Question, null);
}

internal enum SegmentKind
{
    Literal = 0,  // exact text
    Star = 1,     // * matches any chars except /
    Question = 2, // ? matches single char except /
}

/// <summary>Go 的 <c>globMatcher</c>：把 include/exclude 模式合起来做文件匹配。</summary>
internal sealed class GlobMatcher
{
    internal readonly GlobPattern[] Includes;
    internal readonly GlobPattern[] Excludes;

    // true if include specs were provided (even if none compiled)
    private readonly bool _hadIncludes;

    internal GlobMatcher(string[]? includeSpecs, string[]? excludeSpecs, string basePath, bool caseSensitive, Usage usage)
    {
        _hadIncludes = includeSpecs is { Length: > 0 };
        var includes = new List<GlobPattern>();
        var excludes = new List<GlobPattern>();

        if (includeSpecs != null)
        {
            foreach (var spec in includeSpecs)
            {
                if (VfsMatch.TryCompileGlobPattern(spec, basePath, usage, caseSensitive, out var p))
                {
                    includes.Add(p);
                }
            }
        }

        if (excludeSpecs != null)
        {
            foreach (var spec in excludeSpecs)
            {
                if (VfsMatch.TryCompileGlobPattern(spec, basePath, Usage.Exclude, caseSensitive, out var p))
                {
                    excludes.Add(p);
                }
            }
        }

        Includes = includes.ToArray();
        Excludes = excludes.ToArray();
    }

    /// <summary>
    /// Go: <c>func (m *globMatcher) matchesFileParts(prefix, suffix string) (int, bool)</c>。
    /// 返回命中的 include 模式下标与 true；未命中返回 (0, false)。
    /// </summary>
    internal (int Index, bool Ok) MatchesFileParts(string prefix, string suffix)
    {
        for (var i = 0; i < Excludes.Length; i++)
        {
            if (Excludes[i].MatchesParts(prefix, suffix))
            {
                return (0, false);
            }
        }

        if (Includes.Length == 0)
        {
            if (_hadIncludes)
            {
                return (0, false);
            }

            return (0, true);
        }

        for (var i = 0; i < Includes.Length; i++)
        {
            if (Includes[i].MatchesParts(prefix, suffix))
            {
                return (i, true);
            }
        }

        return (0, false);
    }

    /// <summary>Go: <c>func (m *globMatcher) matchesDirectoryParts(prefix, suffix string) bool</c>。</summary>
    internal bool MatchesDirectoryParts(string prefix, string suffix)
    {
        for (var i = 0; i < Excludes.Length; i++)
        {
            if (Excludes[i].MatchesParts(prefix, suffix))
            {
                return false;
            }
        }

        if (Includes.Length == 0)
        {
            return !_hadIncludes;
        }

        for (var i = 0; i < Includes.Length; i++)
        {
            if (Includes[i].MatchesPrefixParts(prefix, suffix))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Go 的 <c>globVisitor</c>：遍历目录树，收集命中 glob 模式的文件。</summary>
internal sealed class GlobVisitor
{
    private readonly FS _host;
    private readonly GlobMatcher _fileMatcher;
    private readonly GlobMatcher _directoryMatcher;
    private readonly string[]? _extensions;
    private readonly bool _useCaseSensitiveFileNames;
    private readonly Set<string> _visited = new();
    private readonly List<string>[] _results;

    internal GlobVisitor(
        FS host,
        GlobMatcher fileMatcher,
        GlobMatcher directoryMatcher,
        string[]? extensions,
        bool useCaseSensitiveFileNames,
        List<string>[] results)
    {
        _host = host;
        _fileMatcher = fileMatcher;
        _directoryMatcher = directoryMatcher;
        _extensions = extensions;
        _useCaseSensitiveFileNames = useCaseSensitiveFileNames;
        _results = results;
    }

    /// <summary>
    /// Go: <c>func (v *globVisitor) visit(path, absolutePath string, depth int, resolvedRealPath string)</c>。
    /// resolvedRealPath 非空时是本目录已解析的 realpath（从父目录增量算得）；为空时调用 Realpath 解析 symlink。
    /// </summary>
    internal void Visit(string path, string absolutePath, int depth, string resolvedRealPath)
    {
        // Detect symlink cycles
        string realPath;
        if (resolvedRealPath != "")
        {
            realPath = resolvedRealPath;
        }
        else
        {
            realPath = _host.Realpath(absolutePath);
        }

        var canonicalPath = GetCanonicalFileName(realPath, _useCaseSensitiveFileNames);
        if (_visited.Has(canonicalPath))
        {
            return;
        }

        _visited.Add(canonicalPath);

        var entries = _host.GetAccessibleEntries(absolutePath);

        var pathPrefix = VfsMatch.EnsureTrailingSlash(path);
        var absPrefix = VfsMatch.EnsureTrailingSlash(absolutePath);

        // Go: range 一个 nil slice 是合法的（读不到目录时 internal.Common 会返回 nil Files/Directories）；
        // C# 的 foreach 会 NRE，所以这里按 nil → 空数组兜底（`[]` 是缓存的空数组，不分配）。
        foreach (var file in entries.Files ?? [])
        {
            if (_extensions is { Length: > 0 } && !FileExtensionIsOneOf(file, _extensions))
            {
                continue;
            }

            var (idx, ok) = _fileMatcher.MatchesFileParts(absPrefix, file);
            if (ok)
            {
                _results[idx].Add(pathPrefix + file);
            }
        }

        if (depth != VfsMatch.UnlimitedDepth)
        {
            depth--;
            if (depth == 0)
            {
                return;
            }
        }

        foreach (var dir in entries.Directories ?? [])
        {
            if (!_directoryMatcher.MatchesDirectoryParts(absPrefix, dir))
            {
                continue;
            }

            var absDir = absPrefix + dir;
            var childRealPath = "";
            if (entries.Symlinks != null)
            {
                if (!entries.Symlinks.Contains(dir))
                {
                    // Non-symlink directory: compute realpath incrementally.
                    childRealPath = CombinePaths(realPath, dir);
                }

                // else: symlink directory; leave childRealPath empty to force Realpath call.
            }

            // If Symlinks is null, the FS doesn't track symlinks;
            // leave childRealPath empty to call Realpath (preserving old behavior).
            Visit(pathPrefix + dir, absDir, depth, childRealPath);
        }
    }
}

/// <summary>Go 的 <c>vfsmatch</c> 包（包级函数/常量）。</summary>
public static partial class VfsMatch
{
    /// <summary>Go: <c>const UnlimitedDepth = math.MaxInt</c>。传给 depth 表示不限深度。</summary>
    public const int UnlimitedDepth = int.MaxValue;

    // Go: var wildcardCharCodes = []rune{'*', '?'}（只被 GetIncludeBasePath 用到）。
    private static readonly char[] _wildcardCharCodes = ['*', '?'];

    /// <summary>
    /// Go: <c>func ReadDirectory(host vfs.FS, currentDir string, path string, extensions []string, excludes []string, includes []string, depth int) []string</c>。
    /// </summary>
    public static string[] ReadDirectory(FS host, string currentDir, string path, string[]? extensions, string[]? excludes, string[]? includes, int depth)
    {
        return MatchFiles(path, extensions, excludes, includes, host.UseCaseSensitiveFileNames(), currentDir, depth, host);
    }

    /// <summary>
    /// IsImplicitGlob checks if a path component is implicitly a glob.
    /// An "includes" path "foo" is implicitly a glob "foo/**&#47;*" if its last component has no extension,
    /// and does not contain any glob characters itself.
    /// </summary>
    public static bool IsImplicitGlob(string lastPathComponent)
    {
        return lastPathComponent.AsSpan().IndexOfAny('.', '*', '?') < 0;
    }

    /// <summary>Go: <c>func getIncludeBasePath(absolute string) string</c>。</summary>
    internal static string GetIncludeBasePath(string absolute)
    {
        var wildcardOffset = absolute.AsSpan().IndexOfAny(_wildcardCharCodes);
        if (wildcardOffset < 0)
        {
            // No "*" or "?" in the path
            if (!HasExtension(absolute))
            {
                return absolute;
            }

            return RemoveTrailingDirectorySeparator(GetDirectoryPath(absolute));
        }

        // Go: absolute[:max(strings.LastIndex(absolute[:wildcardOffset], "/"), 0)]
        var lastSeparator = wildcardOffset == 0 ? -1 : absolute.LastIndexOf('/', wildcardOffset - 1);
        return absolute[..Math.Max(lastSeparator, 0)];
    }

    /// <summary>Go: <c>func getBasePaths(path string, includes []string, useCaseSensitiveFileNames bool) []string</c>。</summary>
    internal static string[] GetBasePaths(string path, string[]? includes, bool useCaseSensitiveFileNames)
    {
        // Storage for our results in the form of literal paths (e.g. the paths as written by the user).
        var basePaths = new List<string> { path };

        if (includes is { Length: > 0 })
        {
            var comparePathsOptions = new ComparePathsOptions(useCaseSensitiveFileNames, path);
            var stringComparer = comparePathsOptions.GetComparer();

            // Storage for literal base paths amongst the include patterns.
            var includeBasePaths = new List<string>();
            foreach (var include in includes)
            {
                // We also need to check the relative paths by converting them to absolute and normalizing
                // in case they escape the base path (e.g "..\somedirectory")
                string absolute;
                if (IsRootedDiskPath(include))
                {
                    absolute = include;
                }
                else
                {
                    absolute = NormalizePath(CombinePaths(path, include));
                }

                // Append the literal and canonical candidate base paths.
                includeBasePaths.Add(GetIncludeBasePath(absolute));
            }

            // Sort the offsets array using either the literal or canonical path representations.
            SortStable(includeBasePaths, stringComparer);

            // Iterate over each include base path and include unique base paths that are not a
            // subpath of an existing base path
            foreach (var includeBasePath in includeBasePaths)
            {
                if (Every(basePaths.ToArray(), basepath => !ContainsPath(basepath, includeBasePath, comparePathsOptions)))
                {
                    basePaths.Add(includeBasePath);
                }
            }
        }

        return basePaths.ToArray();
    }

    /// <summary>Go: <c>func compileGlobPattern(spec string, basePath string, usage Usage, caseSensitive bool) (globPattern, bool)</c>。</summary>
    internal static bool TryCompileGlobPattern(string spec, string basePath, Usage usage, bool caseSensitive, [NotNullWhen(true)] out GlobPattern? pattern)
    {
        var parts = GetNormalizedPathComponents(spec, basePath);

        // "src/**" without a filename matches nothing (for include patterns)
        if (usage != Usage.Exclude && LastOrNil(parts) == "**")
        {
            pattern = null;
            return false;
        }

        // Normalize root: "/home/" -> "/home"
        parts[0] = RemoveTrailingDirectorySeparator(parts[0]);

        // Directories implicitly match all files: "src" -> "src/**/*"
        if (IsImplicitGlob(LastOrNil(parts)))
        {
            parts = [.. parts, "**", "*"];
        }

        var components = new List<PatternComponent>(parts.Length);
        foreach (var part in parts)
        {
            components.Add(ParseComponent(part, usage != Usage.Exclude));
        }

        pattern = new GlobPattern(components.ToArray(), usage == Usage.Exclude, caseSensitive, usage == Usage.Files);
        return true;
    }

    /// <summary>Go: <c>func parseComponent(s string, isInclude bool) component</c>。</summary>
    private static PatternComponent ParseComponent(string s, bool isInclude)
    {
        if (s == "**")
        {
            return PatternComponent.DoubleAsterisk();
        }

        if (s.AsSpan().IndexOfAny('*', '?') < 0)
        {
            return PatternComponent.Literal(s);
        }

        return PatternComponent.Wildcard(ParseSegments(s), isInclude);
    }

    /// <summary>Go: <c>func parseSegments(s string) []segment</c>。把 "*.ts" 拆成 [Star, Literal(".ts")]。</summary>
    private static PatternSegment[] ParseSegments(string s)
    {
        // Preallocate based on wildcard count: each wildcard contributes 1 segment,
        // and each wildcard can split literals into at most one extra literal segment.
        var wildcards = s.AsSpan().Count('*') + s.AsSpan().Count('?');

        var result = new List<PatternSegment>(2 * wildcards + 1);
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '*':
                case '?':
                    if (i > start)
                    {
                        result.Add(PatternSegment.Literal(s[start..i]));
                    }

                    if (s[i] == '*')
                    {
                        result.Add(PatternSegment.Star());
                    }
                    else
                    {
                        result.Add(PatternSegment.Question());
                    }

                    start = i + 1;
                    break;
            }
        }

        if (start < s.Length)
        {
            result.Add(PatternSegment.Literal(s[start..]));
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func nextPathPartSingle(s string, offset int) (part string, nextOffset int, ok bool)</c>。</summary>
    private static (string Part, int NextOffset, bool Ok) NextPathPartSingle(string s, int offset)
    {
        if (offset >= s.Length)
        {
            return ("", offset, false);
        }

        if (offset == 0 && s.Length > 0 && s[0] == '/')
        {
            return ("", 1, true);
        }

        while (offset < s.Length && s[offset] == '/')
        {
            offset++;
        }

        if (offset >= s.Length)
        {
            return ("", offset, false);
        }

        var idx = s.IndexOf('/', offset);
        if (idx >= 0)
        {
            return (s[offset..idx], idx, true);
        }

        return (s[offset..], s.Length, true);
    }

    /// <summary>Go: <c>func nextPathPartParts(prefix, suffix string, offset int) (part string, nextOffset int, ok bool)</c>。</summary>
    internal static (string Part, int NextOffset, bool Ok) NextPathPartParts(string prefix, string suffix, int offset)
    {
        // Fast paths: keep the hot single-string scan tight.
        if (suffix.Length == 0)
        {
            return NextPathPartSingle(prefix, offset);
        }

        if (prefix.Length == 0)
        {
            return NextPathPartSingle(suffix, offset);
        }

        // For matchFilesNoRegex call sites, prefix is a directory path ending in '/',
        // and suffix is a single entry name (no '/'). That makes this significantly
        // simpler than a general-purpose "virtual concatenation" scanner.

        var totalLen = prefix.Length + suffix.Length;
        if (offset >= totalLen)
        {
            return ("", offset, false);
        }

        // Handle leading slash (root of absolute path)
        if (offset == 0 && prefix[0] == '/')
        {
            return ("", 1, true);
        }

        // Scan within prefix.
        if (offset < prefix.Length)
        {
            while (offset < prefix.Length && prefix[offset] == '/')
            {
                offset++;
            }

            if (offset < prefix.Length)
            {
                var idx = prefix.IndexOf('/', offset);

                // idx is guaranteed >= 0 for the call sites we care about because prefix ends in '/'
                // (Go 在这里用 rest[:idx]，idx < 0 会 panic；C# 的 Range 越界同样抛异常)。
                return (prefix[offset..idx], idx, true);
            }

            // Fall through into suffix region.
        }

        // Scan suffix: it's a single component.
        var sOff = offset - prefix.Length;
        if (sOff >= suffix.Length)
        {
            return ("", offset, false);
        }

        return (suffix[sOff..], totalLen, true);
    }

    /// <summary>Go: <c>func isHiddenPath(name string) bool</c>。路径段以点开头视为隐藏。</summary>
    internal static bool IsHiddenPath(string name)
    {
        return name.Length > 0 && name[0] == '.';
    }

    /// <summary>Go: <c>func isPackageFolder(name string) bool</c>。node_modules 等常见包目录。</summary>
    internal static bool IsPackageFolder(string name)
    {
        // Go 是 `switch len(name) { case len("node_modules"): … }`（Go 的 len 是常量表达式）；
        // C# 的 string.Length 不是常量，switch 标签只能写字面量长度：12/13/16。
        return name.Length switch
        {
            12 => EquateStringCaseInsensitive(name, "node_modules"),
            13 => EquateStringCaseInsensitive(name, "jspm_packages"),
            16 => EquateStringCaseInsensitive(name, "bower_components"),
            _ => false,
        };
    }

    /// <summary>Go: <c>func ensureTrailingSlash(s string) string</c>。</summary>
    internal static string EnsureTrailingSlash(string s)
    {
        if (s.Length > 0 && s[^1] != '/')
        {
            return s + "/";
        }

        return s;
    }

    /// <summary>Go: <c>func matchFiles(path string, extensions, excludes, includes []string, useCaseSensitiveFileNames bool, currentDirectory string, depth int, host vfs.FS) []string</c>。</summary>
    internal static string[] MatchFiles(string path, string[]? extensions, string[]? excludes, string[]? includes, bool useCaseSensitiveFileNames, string currentDirectory, int depth, FS host)
    {
        path = NormalizePath(path);
        currentDirectory = NormalizePath(currentDirectory);
        var absolutePath = CombinePaths(currentDirectory, path);

        var fileMatcher = new GlobMatcher(includes, excludes, absolutePath, useCaseSensitiveFileNames, Usage.Files);
        var directoryMatcher = new GlobMatcher(includes, excludes, absolutePath, useCaseSensitiveFileNames, Usage.Directories);

        var results = new List<string>[Math.Max(fileMatcher.Includes.Length, 1)];
        for (var i = 0; i < results.Length; i++)
        {
            results[i] = [];
        }

        var v = new GlobVisitor(host, fileMatcher, directoryMatcher, extensions, useCaseSensitiveFileNames, results);

        foreach (var basePath in GetBasePaths(path, includes, useCaseSensitiveFileNames))
        {
            v.Visit(basePath, CombinePaths(currentDirectory, basePath), depth, "");
        }

        // Fast path: a single include bucket (or no includes) doesn't need flattening.
        if (results.Length == 1)
        {
            return results[0].ToArray();
        }

        // Go: core.Flatten(v.results)
        var flattened = new List<string>();
        foreach (var bucket in results)
        {
            flattened.AddRange(bucket);
        }

        return flattened.ToArray();
    }

    /// <summary>Go: <c>func NewSpecMatcher(specs []string, basePath string, usage Usage, useCaseSensitiveFileNames bool) *SpecMatcher</c>。</summary>
    public static SpecMatcher? NewSpecMatcher(string[]? specs, string basePath, Usage usage, bool useCaseSensitiveFileNames)
    {
        if (specs is not { Length: > 0 })
        {
            return null;
        }

        var patterns = new List<GlobPattern>(specs.Length);
        foreach (var spec in specs)
        {
            if (TryCompileGlobPattern(spec, basePath, usage, useCaseSensitiveFileNames, out var p))
            {
                patterns.Add(p);
            }
        }

        if (patterns.Count == 0)
        {
            return null;
        }

        return new SpecMatcher(patterns.ToArray());
    }

    /// <summary>Go: <c>slices.SortStableFunc</c>（C# 的 Array/List.Sort 都不稳定，这里用下标兜底保持稳定性）。</summary>
    private static void SortStable(List<string> list, Comparison<string> comparison)
    {
        var snapshot = list.ToArray();
        var indices = new int[snapshot.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        Array.Sort(indices, (a, b) =>
        {
            var c = comparison(snapshot[a], snapshot[b]);
            return c != 0 ? c : a - b;
        });

        for (var i = 0; i < indices.Length; i++)
        {
            list[i] = snapshot[indices[i]];
        }
    }

    /// <summary>
    /// Go 的 <c>utf8.DecodeRuneInString(s[i:])</c> 的 size —— docs §3.1 的对应写法：
    /// 高位代理 + 紧跟低位代理才占 2 个 UTF-16 代码单元，否则 1 个。
    /// </summary>
    internal static int CodePointSize(string s, int i)
    {
        return char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
    }

    /// <summary>Go: <c>strings.EqualFold</c>，但作用在两段 span 上（避免为切片分配子串，docs §3.8）。</summary>
    internal static bool EquateSpanCaseInsensitive(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var ia = 0;
        var ib = 0;
        while (true)
        {
            if (ia == a.Length)
            {
                return ib == b.Length;
            }

            if (ib == b.Length)
            {
                return false;
            }

            var lca = Rune.ToLowerInvariant(DecodeRuneAt(a, ia, out var sizeA));
            var lcb = Rune.ToLowerInvariant(DecodeRuneAt(b, ib, out var sizeB));
            if (lca.Value != lcb.Value)
            {
                return false;
            }

            ia += sizeA;
            ib += sizeB;
        }
    }

    // 同 StringUtil 的私有 DecodeRuneAt：在 UTF-16 代码单元串上读一个码点，语义与 Go 的
    // utf8.DecodeRuneInString 逐条对应（非法/孤立代理项 → U+FFFD、消耗 1）。
    private static Rune DecodeRuneAt(ReadOnlySpan<char> span, int index, out int size)
    {
        _ = Rune.DecodeFromUtf16(span[index..], out var value, out size);
        return value;
    }
}

/// <summary>Go 的 <c>SpecMatcher</c>：把多个 glob 模式包起来按路径匹配。</summary>
public sealed class SpecMatcher
{
    private readonly GlobPattern[] _patterns;

    internal SpecMatcher(GlobPattern[] patterns)
    {
        _patterns = patterns;
    }

    /// <summary>MatchString returns true if any pattern matches the path.</summary>
    public bool MatchString(string path)
    {
        for (var i = 0; i < _patterns.Length; i++)
        {
            if (_patterns[i].Matches(path))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>MatchIndex returns the index of the first matching pattern, or -1.</summary>
    public int MatchIndex(string path)
    {
        for (var i = 0; i < _patterns.Length; i++)
        {
            if (_patterns[i].Matches(path))
            {
                return i;
            }
        }

        return -1;
    }
}

// ported from: internal/module/resolver.go:1976-2140（通配 pattern 解析与匹配；CJS 相对名规范化；扩展名判定；
// 独立的 ResolveConfig / GetAutomaticTypeDirectiveNames 入口）
//
// 与 Go 的偏离（同 Resolver.cs 文件头）：
// - Go 的 `ParsedPatterns`/`core.Pattern` 都是值/指针混合使用 → C# `ParsedPatterns` 是 class（模块切片内部
//   只在 `parsedPatternsCache` 里按引用同一性缓存），`Pattern` 沿用 `TypeScript.Core.Pattern`（struct）。
// - Go 的 `collections.Set[string]` 是值类型 + nil 接收者安全的 `Clone()` → C# `Set<string>?` + `?.Clone()`。
// - `(T, bool)` → `bool TryX(..., out T)`；`ResolveConfig` 的 `(T, error)` → 返回值 + 异常（§3.2）。
using System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.PackageJson;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Module;

/// <summary>Go 的 <c>ParsedPatterns</c>（`paths` / `typesVersions` 的一套匹配模式）。</summary>
public sealed class ParsedPatterns
{
    internal Set<string> _matchableStringSet = new();
    internal Pattern[] _patterns = [];
}

public static partial class Module
{
    /// <summary>Go 的 <c>moveToNextDirectorySeparatorIfAvailable</c>。</summary>
    internal static int MoveToNextDirectorySeparatorIfAvailable(string path, int prevSeparatorIndex, bool isFolder)
    {
        var offset = prevSeparatorIndex + 1;
        var nextSeparatorIndex = -1;
        if (offset <= path.Length)
        {
            nextSeparatorIndex = path.IndexOf('/', offset);
        }

        if (nextSeparatorIndex == -1)
        {
            if (isFolder)
            {
                return path.Length;
            }

            return prevSeparatorIndex;
        }

        return nextSeparatorIndex;
    }

    /// <summary>Go 的 <c>TryParsePatterns</c>。</summary>
    public static ParsedPatterns TryParsePatterns(OrderedMap<string, string[]> pathMappings)
    {
        var paths = pathMappings.Keys();

        var numPatterns = 0;
        var numMatchables = 0;
        foreach (var path in paths)
        {
            var pattern = TryParsePattern(path);
            if (pattern.IsValid())
            {
                if (pattern.StarIndex == -1)
                {
                    numMatchables++;
                }
                else
                {
                    numPatterns++;
                }
            }
        }

        var patterns = new List<Pattern>(numPatterns);
        var matchableStringSet = TypeScript.Collections.Collections.NewSetWithSizeHint<string>(numMatchables);

        foreach (var path in paths)
        {
            var pattern = TryParsePattern(path);
            if (pattern.IsValid())
            {
                if (pattern.StarIndex == -1)
                {
                    matchableStringSet.Add(path);
                }
                else
                {
                    patterns.Add(pattern);
                }
            }
        }

        return new ParsedPatterns
        {
            _matchableStringSet = matchableStringSet,
            _patterns = [.. patterns],
        };
    }

    /// <summary>Go 的 <c>MatchPatternOrExact</c>。</summary>
    public static Pattern MatchPatternOrExact(ParsedPatterns patterns, string candidate)
    {
        if (patterns._matchableStringSet.Has(candidate))
        {
            return new Pattern
            {
                Text = candidate,
                StarIndex = -1,
            };
        }

        if (patterns._patterns.Length == 0)
        {
            return default;
        }

        return FindBestPatternMatch(patterns._patterns, Identity, candidate);
    }

    /// <summary>
    /// If you import from "." inside a containing directory "/foo", the result of `TSPath.NormalizePath`
    /// would be "/foo", but this loses the information that `foo` is a directory and we intended
    /// to look inside of it. The Node CommonJS resolution algorithm doesn't call this out
    /// (https://nodejs.org/api/modules.html#all-together), but it seems that module paths ending
    /// in `.` are actually normalized to `./` before proceeding with the resolution algorithm.
    /// </summary>
    internal static string NormalizePathForCJSResolution(string containingDirectory, string moduleName)
    {
        var combined = CombinePaths(containingDirectory, moduleName);
        var parts = GetPathComponents(combined, "");
        var lastPart = parts[^1];
        if (lastPart == "." || lastPart == "..")
        {
            return EnsureTrailingDirectorySeparator(NormalizePath(combined));
        }

        return NormalizePath(combined);
    }

    /// <summary>Go 的 <c>matchesPatternWithTrailer</c>。</summary>
    internal static bool MatchesPatternWithTrailer(string target, string name)
    {
        if (target.EndsWith('*'))
        {
            return false;
        }

        var starIndex = target.IndexOf('*');
        if (starIndex == -1)
        {
            return false;
        }

        var before = target[..starIndex];
        var after = target[(starIndex + 1)..];
        return name.StartsWith(before, StringComparison.Ordinal) && name.EndsWith(after, StringComparison.Ordinal);
    }

    /// <summary>True if `extension` is one of the supported `extensions`.</summary>
    internal static bool ExtensionIsOk(Extensions extensions, string extension)
    {
        return ((extensions & Extensions.JavaScript) != 0 && (extension == ExtensionJs || extension == ExtensionJsx || extension == ExtensionMjs || extension == ExtensionCjs) ||
            ((extensions & Extensions.TypeScript) != 0 && (extension == ExtensionTs || extension == ExtensionTsx || extension == ExtensionMts || extension == ExtensionCts)) ||
            ((extensions & Extensions.Declaration) != 0 && (extension == ExtensionDts || extension == ExtensionDmts || extension == ExtensionDcts)) ||
            ((extensions & Extensions.Json) != 0 && extension == ExtensionJson));
    }

    /// <summary>Go 的 <c>strings.Replace(s, old, new, 1)</c>：只替换第一处。</summary>
    internal static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (index == -1)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, index), newValue, text.AsSpan(index + oldValue.Length));
    }

    /// <summary>Go 的 <c>ResolveConfig</c>。</summary>
    public static ResolvedModule ResolveConfig(string moduleName, string containingFile, ResolutionHost host)
    {
        var resolver = NewResolver(new ResolverOptions
        {
            Host = host,
            CompilerOptions = new CompilerOptions { ModuleResolution = ModuleResolutionKind.NodeNext },
        });
        return resolver.ResolveConfig(moduleName, containingFile);
    }

    /// <summary>Go 的 <c>GetAutomaticTypeDirectiveNames</c>。</summary>
    public static string[] GetAutomaticTypeDirectiveNames(CompilerOptions options, ResolutionHost host)
    {
        if (!options.UsesWildcardTypes())
        {
            return options.Types ?? [];
        }

        // Walk the primary type lookup locations
        var wildcardMatches = new List<string>();
        var (typeRoots, _) = options.GetEffectiveTypeRoots(host.GetCurrentDirectory());
        foreach (var root in typeRoots)
        {
            if (host.FS().DirectoryExists(root))
            {
                foreach (var typeDirectivePath in host.FS().GetAccessibleEntries(root).Directories)
                {
                    var normalized = NormalizePath(typeDirectivePath);
                    var packageJsonPath = CombinePaths(root, normalized, "package.json");
                    var isNotNeededPackage = false;
                    if (host.FS().FileExists(packageJsonPath))
                    {
                        host.FS().TryReadFile(packageJsonPath, out var contents);
                        // `types-publisher` sometimes creates packages with `"typings": null` for packages that don't provide their own types.
                        // See `createNotNeededPackageJSON` in the types-publisher` repo.
                        isNotNeededPackage =
                            TryParsePackageJson(Encoding.UTF8.GetBytes(contents ?? ""), out var packageJsonContent) &&
                            packageJsonContent.PathFields.Typings.Null;
                    }

                    if (!isNotNeededPackage)
                    {
                        var baseFileName = GetBaseFileName(normalized);
                        if (!baseFileName.StartsWith('.'))
                        {
                            wildcardMatches.Add(baseFileName);
                        }
                    }
                }
            }
        }

        // Order potentially matters in program construction, so substitute
        // in the wildcard in the position it was specified in the types array
        var result = new List<string>();
        foreach (var t in options.Types ?? [])
        {
            if (t == "*")
            {
                result.AddRange(wildcardMatches);
            }
            else
            {
                result.Add(t);
            }
        }

        return Deduplicate([.. result]);
    }

    /// <summary>Go 的 <c>packagejson.Parse(data) (Fields, error)</c> 的 C# 形状（失败返回 false + 零值 Fields）。</summary>
    private static bool TryParsePackageJson(byte[] data, out Fields fields)
    {
        return TypeScript.PackageJson.PackageJson.TryParse(data, out fields);
    }
}

// ported from: internal/tspath/path.go
//
// 与 Go 的差异（都不改变语义，依据 docs/architecture.md §3.1/§3.2）：
// - Go 的 string 是 UTF-8 字节序列，本文件里一切"下标/长度"都改成 UTF-16 代码单元（= TS 的 Pos 语义）。
//   凡是按 ASCII 分隔符切分/判断的地方，两种下标得到的字符串内容完全相同；只有 TrimRuneCount 与
//   ToFileNameLowerCase 的非 ASCII 分支是按**码点**计数的（Go 用 utf8.RuneCountInString/DecodeRuneInString），
//   这里用 Rune 枚举。
// - Go 的 `func(a, b string) int` / `func(a, b string) bool` 用 stringutil 的 Comparison<string> /
//   Func<string, string, bool> 表示；比较器一律来自 stringutil，绝不换成 StringComparison.OrdinalIgnoreCase
//   （后者与 Go 的 EqualFold 不等价）。
// - Go 的 `(T, bool)` 统一映射成 `bool TryX(..., out T)`：TrimFilePathPrefix → TryTrimFilePathPrefix、
//   SplitVolumePath → TrySplitVolumePath、ForEachAncestorDirectory → bool + out result。
// - Go 的 nil slice 参数在 C# 里用空数组表示：这些参数在本文件里只参与 len()/range，nil 与空无行为差别。
// - Go 的 panic → Debug.Fail（抛 DebugFailureException）。
// - 无约束泛型 T 在"没找到"分支要返回 Go 的零值（引用类型即 null），C# 只能写成 `default!`：
//   这是本文件唯一使用 `!` 的地方，因为编译器无法证明 T 非空。
// - Go 里散落在文件各处的 `(p Path)` 方法在 C# 里必须集中到 Path 类型内部（C# 类型不能分散声明）。
// - CombinePaths 里 Go 的 strings.Builder + 零拷贝切片技巧在 C# 里会复制字符串，改为只记录 start 下标，
//   循环内不再为 result() 分配（语义相同：result() 就是 builder 从 start 起的内容）。
using System.Text;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.TSPath;

/// <summary>Go 的 <c>type Path string</c>。</summary>
/// <remarks>
/// 注意：ImplicitUsings 会把 System.IO 带进来，所以**外部**代码里裸写 <c>Path</c> 会 CS0104
/// （TypeScript.TSPath.Path vs System.IO.Path）；请用 `using Path = TypeScript.TSPath.Path;`
/// 别名，或在 TypeScript.* 命名空间里写 `TSPath.Path`。本命名空间内部不受影响（命名空间成员优先）。
/// </remarks>
public readonly record struct Path(string Value)
{
    // Go 的 Path(x) / string(p) 都是显式转换，这里保留为显式运算符。
    public static explicit operator Path(string value) => new(value);

    public static explicit operator string(Path path) => path.Value;

    // Go 里 Path 就是 string，打印/格式化/拼接得到的都是字符串内容本身；record struct 默认的
    // ToString() 会打印 "Path { Value = ... }"，所以必须覆写。
    public override string ToString() => Value;

    /// <summary>Go 的 <c>func (p Path) GetDirectoryPath() Path</c>。</summary>
    public Path GetDirectoryPath() => (Path)TSPath.GetDirectoryPath(Value);

    /// <summary>Go 的 <c>func (p Path) RemoveTrailingDirectorySeparator() Path</c>。</summary>
    public Path RemoveTrailingDirectorySeparator() => (Path)TSPath.RemoveTrailingDirectorySeparator(Value);

    /// <summary>Go 的 <c>func (p Path) EnsureTrailingDirectorySeparator() Path</c>。</summary>
    public Path EnsureTrailingDirectorySeparator() => (Path)TSPath.EnsureTrailingDirectorySeparator(Value);

    // ContainsPath checks whether child is contained within or equal to p.
    // Since Path values are already rooted, reduced, and case-canonicalized,
    // this is a simple string prefix check.
    public bool ContainsPath(Path child)
    {
        if (Value.Length == 0)
        {
            return false;
        }

        // && 优先级高于 ||，与 Go 一致：长度比较先短路，child.Value[Value.Length] 才安全。
        return Value == child.Value
            || (child.Value.Length > Value.Length
                && child.Value.StartsWith(Value, StringComparison.Ordinal)
                && (Value[Value.Length - 1] == '/' || child.Value[Value.Length] == '/'));
    }

    /// <summary>Go 的 <c>func (p Path) ForEachAncestorDirectory[T any](callback) (result T, ok bool)</c>。</summary>
    public bool ForEachAncestorDirectory<T>(Func<Path, (T Result, bool Stop)> callback, out T result)
        => TSPath.ForEachAncestorDirectory(Value, directory => callback(new Path(directory)), out result);
}

public static partial class TSPath
{
    // Internally, we represent paths as strings with '/' as the directory separator.
    // When we make system calls (eg: LanguageServiceHost.getDirectory()),
    // we expect the host to correctly handle paths in our specified format.
    // Go 的 DirectorySeparator 是无类型 rune 常量；这里用 char（用法只有字符比较与拼接）。
    public const char DirectorySeparator = '/';
    private const string _urlSchemeSeparator = "://";

    //// Path Tests

    // Determines whether a byte corresponds to `/` or `\`.
    private static bool IsAnyDirectorySeparator(char ch) => ch == '/' || ch == '\\';

    // Determines whether a path starts with a URL scheme (e.g. starts with `http://`, `ftp://`, `file://`, etc.).
    public static bool IsUrl(string path) => GetEncodedRootLength(path) < 0;

    // Determines whether a path is an absolute disk path (e.g. starts with `/`, or a dos path
    // like `c:`, `c:\` or `c:/`).
    public static bool IsRootedDiskPath(string path) => GetEncodedRootLength(path) > 0;

    // Determines whether a path consists only of a path root.
    public static bool IsDiskPathRoot(string path)
    {
        var rootLength = GetEncodedRootLength(path);
        return rootLength > 0 && rootLength == path.Length;
    }

    // IsDynamicFileName returns true if the file name represents a dynamic/virtual file
    // that doesn't exist on disk (e.g., untitled files with paths like "^/untitled/...").
    public static bool IsDynamicFileName(string fileName) => fileName.StartsWith("^/", StringComparison.Ordinal);

    // Determines whether a path starts with an absolute path component (i.e. `/`, `c:/`, `file://`, etc.).
    //
    //	```
    //	// POSIX
    //	PathIsAbsolute("/path/to/file.ext") === true
    //	// DOS
    //	PathIsAbsolute("c:/path/to/file.ext") === true
    //	// URL
    //	PathIsAbsolute("file:///path/to/file.ext") === true
    //	// Non-absolute
    //	PathIsAbsolute("path/to/file.ext") === false
    //	PathIsAbsolute("./path/to/file.ext") === false
    //	```
    public static bool PathIsAbsolute(string path) => GetEncodedRootLength(path) != 0;

    public static bool HasTrailingDirectorySeparator(string path)
        => path.Length > 0 && IsAnyDirectorySeparator(path[path.Length - 1]);

    // Combines paths. If a path is absolute, it replaces any previous path. Relative paths are not simplified.
    //
    //	```
    //	// Non-rooted
    //	CombinePaths("path", "to", "file.ext") === "path/to/file.ext"
    //	CombinePaths("path", "dir", "..", "to", "file.ext") === "path/dir/../to/file.ext"
    //	// POSIX
    //	CombinePaths("/path", "to", "file.ext") === "/path/to/file.ext"
    //	CombinePaths("/path", "/to", "file.ext") === "/to/file.ext"
    //	// DOS
    //	CombinePaths("c:/path", "to", "file.ext") === "c:/path/to/file.ext"
    //	CombinePaths("c:/path", "c:/to", "file.ext") === "c:/to/file.ext"
    //	// URL
    //	CombinePaths("file:///path", "to", "file.ext") === "file:///path/to/file.ext"
    //	CombinePaths("file:///path", "file:///to", "file.ext") === "file:///to/file.ext"
    //	```
    /// <remarks>`paths` 是 `params ReadOnlySpan&lt;string&gt;`（不是 `string[]`）：Go 的 `paths ...string` 变参每次调用
    /// 分配一个切片，而 C# 13 的 `params ReadOnlySpan&lt;T&gt;` 在调用点由编译器 `stackalloc`（先例
    /// `Ast/Utilities.cs` 的 `NodeKindIs(bool, params ReadOnlySpan&lt;Kind&gt;)`）。本方法 159 个调用点里绝大多数字面量
    /// 只带 1 个额外 path，省掉的就是每个调用点一个 `string[1]`；传已有数组的调用点写 `CombinePaths(a, b)` 一行不改
    /// （`string[]` 隐式转 `ReadOnlySpan&lt;string&gt;`）。</remarks>
    public static string CombinePaths(string firstPath, params ReadOnlySpan<string> paths)
    {
        // TODO (drosen): There is potential for a fast path here.
        // In the case where we find the last absolute path and just path.Join from there.
        firstPath = NormalizeSlashes(firstPath);

        // To provide a way to "set" the path, keep track of the start and then slice.
        // This will waste some memory each time we do it, but saving memory is more common.
        // C# 的 StringBuilder.ToString(start, len) 会复制，所以把 start 显式记下来，
        // 只在最后取一次字符串：b 从 start 到末尾的内容恒等于 Go 的 result()。
        var size = firstPath.Length + paths.Length;
        foreach (var p in paths)
        {
            size += p.Length;
        }

        var b = new StringBuilder(size);

        b.Append(firstPath);

        var start = 0;
        foreach (var p in paths)
        {
            if (p.Length == 0)
            {
                continue;
            }

            var trailingPath = NormalizeSlashes(p);
            var hasResult = b.Length > start;
            if (!hasResult || GetRootLength(trailingPath) != 0)
            {
                // `trailingPath` is absolute.
                start = b.Length;
                b.Append(trailingPath);
            }
            else
            {
                if (!(b.Length > start && IsAnyDirectorySeparator(b[b.Length - 1])))
                {
                    b.Append(DirectorySeparator);
                }

                b.Append(trailingPath);
            }
        }

        return b.ToString(start, b.Length - start);
    }

    public static string[] GetPathComponents(string path, string currentDirectory)
    {
        path = CombinePaths(currentDirectory, path);
        return PathComponents(path, GetRootLength(path));
    }

    private static string[] PathComponents(string path, int rootLength)
    {
        var root = path[..rootLength];
        var rest = path[rootLength..].Split('/');
        if (rest.Length > 0 && rest[^1].Length == 0)
        {
            rest = rest[..^1];
        }

        var result = new string[rest.Length + 1];
        result[0] = root;
        Array.Copy(rest, 0, result, 1, rest.Length);
        return result;
    }

    public static bool IsVolumeCharacter(char ch) => ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z';

    private static int GetFileUrlVolumeSeparatorEnd(string url, int start)
    {
        if (url.Length <= start)
        {
            return -1;
        }

        var ch0 = url[start];
        if (ch0 == ':')
        {
            return start + 1;
        }

        if (ch0 == '%' && url.Length > start + 2 && url[start + 1] == '3')
        {
            var ch2 = url[start + 2];
            if (ch2 == 'a' || ch2 == 'A')
            {
                return start + 3;
            }
        }

        return -1;
    }

    public static int GetEncodedRootLength(string path)
    {
        var ln = path.Length;
        if (ln == 0)
        {
            return 0;
        }

        var ch0 = path[0];

        // POSIX or UNC
        if (ch0 == '/' || ch0 == '\\')
        {
            if (ln == 1 || path[1] != ch0)
            {
                return 1; // POSIX: "/" (or non-normalized "\")
            }

            const int offset = 2;
            var p1 = path.IndexOf(ch0, offset);
            if (p1 < 0)
            {
                return ln; // UNC: "//server" or "\\server"
            }

            return p1 + 1; // UNC: "//server/" or "\\server\"
        }

        // DOS
        if (IsVolumeCharacter(ch0) && ln > 1 && path[1] == ':')
        {
            if (ln == 2)
            {
                return 2; // DOS: "c:" (but not "c:d")
            }

            var ch2 = path[2];
            if (ch2 == '/' || ch2 == '\\')
            {
                return 3; // DOS: "c:/" or "c:\"
            }
        }

        // Untitled paths (e.g., "^/untitled/ts-nul-authority/Untitled-1")
        if (ch0 == '^' && ln > 1 && path[1] == '/')
        {
            return 2; // Untitled: "^/"
        }

        // URL
        var schemeEnd = path.IndexOf(_urlSchemeSeparator, StringComparison.Ordinal);
        if (schemeEnd != -1)
        {
            var authorityStart = schemeEnd + _urlSchemeSeparator.Length;
            var authorityLength = path.IndexOf('/', authorityStart);
            if (authorityLength != -1) // URL: "file:///", "file://server/", "file://server/path"
            {
                var authorityEnd = authorityLength;

                // For local "file" URLs, include the leading DOS volume (if present).
                // Per https://www.ietf.org/rfc/rfc1738.txt, a host of "" or "localhost" is a
                // special case interpreted as "the machine from which the URL is being interpreted".
                var scheme = path[..schemeEnd];
                var authority = path[authorityStart..authorityEnd];
                if (scheme == "file" && (authority.Length == 0 || authority == "localhost") && path.Length > authorityEnd + 2 && IsVolumeCharacter(path[authorityEnd + 1]))
                {
                    var volumeSeparatorEnd = GetFileUrlVolumeSeparatorEnd(path, authorityEnd + 2);
                    if (volumeSeparatorEnd != -1)
                    {
                        if (volumeSeparatorEnd == ln)
                        {
                            // URL: "file:///c:", "file://localhost/c:", "file:///c$3a", "file://localhost/c%3a"
                            // but not "file:///c:d" or "file:///c%3ad"
                            return ~volumeSeparatorEnd;
                        }

                        if (path[volumeSeparatorEnd] == '/')
                        {
                            // URL: "file:///c:/", "file://localhost/c:/", "file:///c%3a/", "file://localhost/c%3a/"
                            return ~(volumeSeparatorEnd + 1);
                        }
                    }
                }

                return ~(authorityEnd + 1); // URL: "file://server/", "http://server/"
            }

            return ~ln; // URL: "file://server", "http://server"
        }

        // relative
        return 0;
    }

    public static int GetRootLength(string path)
    {
        var rootLength = GetEncodedRootLength(path);
        if (rootLength < 0)
        {
            return ~rootLength;
        }

        return rootLength;
    }

    public static string GetDirectoryPath(string path)
    {
        path = NormalizeSlashes(path);

        // If the path provided is itself a root, then return it.
        var rootLength = GetRootLength(path);
        if (rootLength == path.Length)
        {
            return path;
        }

        // return the leading portion of the path up to the last (non-terminal) directory separator
        // but not including any trailing directory separator.
        path = RemoveTrailingDirectorySeparator(path);
        return path[..Math.Max(rootLength, path.LastIndexOf('/'))];
    }

    public static string GetPathFromPathComponents(string[] pathComponents)
    {
        if (pathComponents.Length == 0)
        {
            return "";
        }

        var root = pathComponents[0];
        if (root.Length != 0)
        {
            root = EnsureTrailingDirectorySeparator(root);
        }

        return root + string.Join("/", pathComponents, 1, pathComponents.Length - 1);
    }

    public static string NormalizeSlashes(string path) => path.Replace('\\', '/');

    internal static string[] ReducePathComponents(string[] components)
    {
        if (components.Length == 0)
        {
            return [];
        }

        var reduced = new string[components.Length];
        reduced[0] = components[0];
        var count = 1;
        for (var i = 1; i < components.Length; i++)
        {
            var component = components[i];
            if (component.Length == 0)
            {
                continue;
            }

            if (component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                if (count > 1)
                {
                    if (reduced[count - 1] != "..")
                    {
                        count--;
                        continue;
                    }
                }
                else if (reduced[0].Length != 0)
                {
                    continue;
                }
            }

            reduced[count] = component;
            count++;
        }

        return count == reduced.Length ? reduced : reduced[..count];
    }

    // Combines and resolves paths. If a path is absolute, it replaces any previous path. Any
    // `.` and `..` path components are resolved. Trailing directory separators are preserved.
    //
    // ```go
    // resolvePath("/path", "to", "file.ext") == "path/to/file.ext"
    // resolvePath("/path", "to", "file.ext/") == "path/to/file.ext/"
    // resolvePath("/path", "dir", "..", "to", "file.ext") == "path/to/file.ext"
    // ```
    public static string ResolvePath(string path, params ReadOnlySpan<string> paths)
    {
        var combinedPath = paths.Length > 0 ? CombinePaths(path, paths) : NormalizeSlashes(path);
        return NormalizePath(combinedPath);
    }

    public static string ResolveTripleslashReference(string moduleName, string containingFile)
    {
        var basePath = GetDirectoryPath(containingFile);
        if (IsRootedDiskPath(moduleName))
        {
            return NormalizePath(moduleName);
        }

        return NormalizePath(CombinePaths(basePath, moduleName));
    }

    public static string[] GetNormalizedPathComponents(string path, string currentDirectory)
    {
        var combined = CombinePaths(currentDirectory, path);
        return GetNormalizedPathComponentsFromCombined(combined);
    }

    private static string[] GetNormalizedPathComponentsFromCombined(string path)
    {
        var rootLength = GetRootLength(path);
        // Always include the root component (empty string for relative paths).
        var components = new List<string>(8) { path[..rootLength] };

        for (var i = rootLength; i < path.Length;)
        {
            // Skip directory separators (handles consecutive separators and trailing '/').
            while (i < path.Length && path[i] == '/')
            {
                i++;
            }

            if (i >= path.Length)
            {
                break;
            }

            var start = i;
            while (i < path.Length && path[i] != '/')
            {
                i++;
            }

            var component = path[start..i];

            if (component.Length == 0 || component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                if (components.Count > 1)
                {
                    if (components[^1] != "..")
                    {
                        components.RemoveAt(components.Count - 1);
                        continue;
                    }
                }
                else if (components[0].Length != 0)
                {
                    // If this is an absolute path, we can't go above the root.
                    continue;
                }
            }

            components.Add(component);
        }

        return [.. components];
    }

    public static string GetNormalizedAbsolutePathWithoutRoot(string fileName, string currentDirectory)
    {
        var absolutePath = GetNormalizedAbsolutePath(fileName, currentDirectory);
        var rootLength = GetRootLength(absolutePath);
        return absolutePath[rootLength..];
    }

    public static string GetNormalizedAbsolutePath(string fileName, string currentDirectory)
    {
        var rootLength = GetRootLength(fileName);
        if (rootLength == 0 && currentDirectory.Length != 0)
        {
            fileName = CombinePaths(currentDirectory, fileName);
        }
        else
        {
            // CombinePaths normalizes slashes, so not necessary in other branch
            fileName = NormalizeSlashes(fileName);
        }

        rootLength = GetRootLength(fileName);

        if (TrySimpleNormalizePath(fileName, out var simpleNormalized))
        {
            var simpleLength = simpleNormalized.Length;
            if (simpleLength > rootLength)
            {
                return RemoveTrailingDirectorySeparator(simpleNormalized);
            }

            if (simpleLength == rootLength && rootLength != 0)
            {
                return EnsureTrailingDirectorySeparator(simpleNormalized);
            }

            return simpleNormalized;
        }

        var length = fileName.Length;
        var root = fileName[..rootLength];
        // `normalized` is only initialized once `fileName` is determined to be non-normalized.
        // `changed` is set at the same time.
        // （Go 的 zero value 是 ""，所以这里初值也是空串，而不是 null。）
        var changed = false;
        var normalized = string.Empty;
        var index = rootLength;
        var normalizedUpTo = index;
        var seenNonDotDotSegment = rootLength != 0;
        while (index < length)
        {
            // At beginning of segment
            var segmentStart = index;
            var ch = fileName[index];
            while (ch == '/')
            {
                index++;
                if (index < length)
                {
                    ch = fileName[index];
                }
                else
                {
                    break;
                }
            }

            if (index > segmentStart)
            {
                // Seen superfluous separator
                if (!changed)
                {
                    normalized = fileName[..Math.Max(rootLength, segmentStart - 1)];
                    changed = true;
                }

                if (index == length)
                {
                    break;
                }

                segmentStart = index;
            }

            // Past any superfluous separators
            var segmentEnd = fileName.IndexOf('/', index + 1);
            if (segmentEnd == -1)
            {
                segmentEnd = length;
            }

            var segmentLength = segmentEnd - segmentStart;
            if (segmentLength == 1 && fileName[index] == '.')
            {
                // "." segment (skip)
                if (!changed)
                {
                    normalized = fileName[..normalizedUpTo];
                    changed = true;
                }
            }
            else if (segmentLength == 2 && fileName[index] == '.' && fileName[index + 1] == '.')
            {
                // ".." segment
                if (!seenNonDotDotSegment)
                {
                    if (changed)
                    {
                        normalized += normalized.Length == rootLength ? ".." : "/..";
                    }
                    else
                    {
                        normalizedUpTo = index + 2;
                    }
                }
                else if (!changed)
                {
                    if (normalizedUpTo - 1 >= 0)
                    {
                        // Go: strings.LastIndexByte(fileName[:normalizedUpTo-1], '/')；span 版本零分配。
                        normalized = fileName[..Math.Max(rootLength, fileName.AsSpan(0, normalizedUpTo - 1).LastIndexOf('/'))];
                    }
                    else
                    {
                        normalized = fileName[..normalizedUpTo];
                    }

                    changed = true;
                    seenNonDotDotSegment = (normalized.Length != rootLength || rootLength != 0) && normalized != ".." && !normalized.EndsWith("/..", StringComparison.Ordinal);
                }
                else
                {
                    var lastSlash = normalized.AsSpan().LastIndexOf('/');
                    if (lastSlash != -1)
                    {
                        normalized = normalized[..Math.Max(rootLength, lastSlash)];
                    }
                    else
                    {
                        normalized = root;
                    }

                    seenNonDotDotSegment = (normalized.Length != rootLength || rootLength != 0) && normalized != ".." && !normalized.EndsWith("/..", StringComparison.Ordinal);
                }
            }
            else if (changed)
            {
                if (normalized.Length != rootLength)
                {
                    normalized += "/";
                }

                seenNonDotDotSegment = true;
                normalized += fileName[segmentStart..segmentEnd];
            }
            else
            {
                seenNonDotDotSegment = true;
                normalizedUpTo = segmentEnd;
            }

            index = segmentEnd + 1;
        }

        if (changed)
        {
            return normalized;
        }

        if (length > rootLength)
        {
            return RemoveTrailingDirectorySeparators(fileName);
        }

        if (length == rootLength)
        {
            return EnsureTrailingDirectorySeparator(fileName);
        }

        return fileName;
    }

    private static bool TrySimpleNormalizePath(string path, out string normalized)
    {
        // Most paths don't require normalization
        if (!HasRelativePathSegment(path))
        {
            normalized = path;
            return true;
        }

        // Some paths only require cleanup of `/./` or leading `./`
        var simplified = path.Replace("/./", "/", StringComparison.Ordinal);
        var trimmed = simplified.StartsWith("./", StringComparison.Ordinal) ? simplified[2..] : simplified;
        if (trimmed != path && !HasRelativePathSegment(trimmed) && !(trimmed != simplified && trimmed.StartsWith("/", StringComparison.Ordinal)))
        {
            // If we trimmed a leading "./" and the path now starts with "/", we changed the meaning
            path = trimmed;
            normalized = path;
            return true;
        }

        normalized = "";
        return false;
    }

    // hasRelativePathSegment reports whether p contains ".", "..", "./", "../", "/.", "/..", "//", "/./", or "/../".
    internal static bool HasRelativePathSegment(string p)
    {
        var n = p.Length;
        if (n == 0)
        {
            return false;
        }

        if (p == "." || p == "..")
        {
            return true;
        }

        // Leading "./" OR "../"
        if (p[0] == '.')
        {
            if (n >= 2 && p[1] == '/')
            {
                return true;
            }

            // Leading "../"
            if (n >= 3 && p[1] == '.' && p[2] == '/')
            {
                return true;
            }
        }

        // Trailing "/." OR "/.."
        if (p[n - 1] == '.')
        {
            if (n >= 2 && p[n - 2] == '/')
            {
                return true;
            }

            if (n >= 3 && p[n - 2] == '.' && p[n - 3] == '/')
            {
                return true;
            }
        }

        // Now look for any `//` or `/./` or `/../`

        var prevSlash = false;
        var segLen = 0;   // length of current segment since last slash
        var dotCount = 0; // consecutive dots at start of the current segment; -1 => not only dots

        for (var i = 0; i < n; i++)
        {
            var c = p[i];
            if (c == '/')
            {
                // "//"
                if (prevSlash)
                {
                    return true;
                }

                // "/./" or "/../"
                if (segLen == 1 && dotCount == 1 || segLen == 2 && dotCount == 2)
                {
                    return true;
                }

                prevSlash = true;
                segLen = 0;
                dotCount = 0;
                continue;
            }

            if (c == '.')
            {
                if (dotCount >= 0)
                {
                    dotCount++;
                }
            }
            else
            {
                dotCount = -1;
            }

            segLen++;
            prevSlash = false;
        }

        // Trailing "/." or "/.."
        return segLen == 1 && dotCount == 1 || segLen == 2 && dotCount == 2;
    }

    public static string NormalizePath(string path)
    {
        path = NormalizeSlashes(path);
        if (TrySimpleNormalizePath(path, out var normalized))
        {
            return normalized;
        }

        normalized = GetNormalizedAbsolutePath(path, "");
        if (normalized.Length != 0 && HasTrailingDirectorySeparator(path))
        {
            normalized = EnsureTrailingDirectorySeparator(normalized);
        }

        return normalized;
    }

    public static string GetCanonicalFileName(string fileName, bool useCaseSensitiveFileNames)
    {
        if (useCaseSensitiveFileNames)
        {
            return fileName;
        }

        return ToFileNameLowerCase(fileName);
    }

    // TrimFilePathPrefix removes prefix from the start of path, honoring
    // useCaseSensitiveFileNames the same way GetCanonicalFileName does. It returns
    // the remainder of path and true if path starts with prefix; otherwise it
    // returns path unchanged and false.
    //
    // This must not slice path using len(prefix): case-folding (as performed by
    // GetCanonicalFileName) can change a string's UTF-8 byte length without
    // changing its rune count (e.g. the Kelvin sign '\u212A' case-folds to the
    // single-byte 'k'), so path and prefix can disagree in byte length even when
    // one is (a case-insensitive match for) a prefix of the other.
    //
    // Go 的 (string, bool) 按架构文档 §3.2 映射成 TryX 形式。
    public static bool TryTrimFilePathPrefix(string path, string prefix, bool useCaseSensitiveFileNames, out string suffix)
    {
        if (useCaseSensitiveFileNames)
        {
            // strings.CutPrefix
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                suffix = path[prefix.Length..];
                return true;
            }

            suffix = path;
            return false;
        }

        var canonicalPrefix = GetCanonicalFileName(prefix, false /*useCaseSensitiveFileNames*/);
        if (!GetCanonicalFileName(path, false /*useCaseSensitiveFileNames*/).StartsWith(canonicalPrefix, StringComparison.Ordinal))
        {
            suffix = path;
            return false;
        }

        // Go: utf8.RuneCountInString(canonicalPrefix)（按码点计数，不能按代码单元，见上面的注释）。
        var runeCount = 0;
        foreach (var _ in canonicalPrefix.EnumerateRunes())
        {
            runeCount++;
        }

        suffix = TrimRuneCount(path, runeCount);
        return true;
    }

    // trimRuneCount returns the suffix of s after skipping up to runeCount runes,
    // clamping to the end of s if it has fewer runes than runeCount.
    private static string TrimRuneCount(string s, int runeCount)
    {
        var i = 0;
        for (var n = 0; n < runeCount; n++)
        {
            if (i >= s.Length)
            {
                break;
            }

            // Go: utf8.DecodeRuneInString；非法序列两边都按"消耗 1 个单元"处理。
            var rune = Rune.GetRuneAt(s, i);
            i += rune.Utf16SequenceLength;
        }

        return s[i..];
    }

    // We convert the file names to lower case as key for file name on case insensitive file system
    // While doing so we need to handle special characters (eg \u0130) to ensure that we dont convert
    // it to lower case, fileName with its lowercase form can exist along side it.
    // Handle special characters and make those case sensitive instead
    //
    // |-#--|-Unicode--|-Char code-|-Desc-------------------------------------------------------------------|
    // | 1. | i        | 105       | Ascii i                                                                |
    // | 2. | I        | 73        | Ascii I                                                                |
    // |-------- Special characters ------------------------------------------------------------------------|
    // | 3. | \u0130   | 304       | Upper case I with dot above                                            |
    // | 4. | i,\u0307 | 105,775   | i, followed by 775: Lower case of (3rd item)                           |
    // | 5. | I,\u0307 | 73,775    | I, followed by 775: Upper case of (4th item), lower case is (4th item) |
    // | 6. | \u0131   | 305       | Lower case i without dot, upper case is I (2nd item)                   |
    // | 7. | \u00DF   | 223       | Lower case sharp s                                                     |
    //
    // Because item 3 is special where in its lowercase character has its own
    // upper case form we cant convert its case.
    // Rest special characters are either already in lower case format or
    // they have corresponding upper case character so they dont need special handling
    public static string ToFileNameLowerCase(string fileName)
    {
        const char iWithDot = '\u0130';

        var ascii = true;
        var needsLower = false;
        var fileNameLen = fileName.Length;
        for (var i = 0; i < fileNameLen; i++)
        {
            var c = fileName[i];
            if (c >= 0x80)
            {
                ascii = false;
                break;
            }

            if (c >= 'A' && c <= 'Z')
            {
                needsLower = true;
            }
        }

        if (ascii)
        {
            if (!needsLower)
            {
                return fileName;
            }

            // Go 在这里用 unsafe.String 把 []byte 零拷贝当字符串返回；C# 的 string 不可变且无法别名
            // 一个数组，普通构造即可（这是 Go 侧的性能技巧，移植不需要）。
            var b = new char[fileNameLen];
            for (var i = 0; i < fileNameLen; i++)
            {
                var c = fileName[i];
                if (c >= 'A' && c <= 'Z')
                {
                    c += (char)('a' - 'A'); // +32
                }

                b[i] = c;
            }

            return new string(b);
        }

        // Go 用 strings.Map + unicode.ToLower；C# 用 Rune.ToLowerInvariant（simple 映射），
        // 并按上面的注释保留 U+0130 不转换。
        var builder = new StringBuilder(fileNameLen);
        Span<char> buffer = stackalloc char[2];
        foreach (var rune in fileName.EnumerateRunes())
        {
            var lower = rune.Value == iWithDot ? rune : Rune.ToLowerInvariant(rune);
            var written = lower.EncodeToUtf16(buffer);
            builder.Append(buffer[..written]);
        }

        return builder.ToString();
    }

    public static Path ToPath(string fileName, string basePath, bool useCaseSensitiveFileNames)
    {
        string nonCanonicalizedPath;
        if (IsRootedDiskPath(fileName))
        {
            nonCanonicalizedPath = NormalizePath(fileName);
        }
        else
        {
            nonCanonicalizedPath = GetNormalizedAbsolutePath(fileName, basePath);
        }

        return (Path)GetCanonicalFileName(nonCanonicalizedPath, useCaseSensitiveFileNames);
    }

    public static string RemoveTrailingDirectorySeparator(string path)
    {
        if (HasTrailingDirectorySeparator(path))
        {
            return path[..^1];
        }

        return path;
    }

    public static string RemoveTrailingDirectorySeparators(string path)
    {
        while (HasTrailingDirectorySeparator(path))
        {
            path = RemoveTrailingDirectorySeparator(path);
        }

        return path;
    }

    public static string EnsureTrailingDirectorySeparator(string path)
    {
        if (!HasTrailingDirectorySeparator(path))
        {
            return path + "/";
        }

        return path;
    }

    //// Relative Paths

    public static string[] GetPathComponentsRelativeTo(string from, string to, ComparePathsOptions options)
    {
        var fromComponents = ReducePathComponents(GetPathComponents(from, options.CurrentDirectory));
        var toComponents = ReducePathComponents(GetPathComponents(to, options.CurrentDirectory));

        var start = 0;
        var maxCommonComponents = Math.Min(fromComponents.Length, toComponents.Length);
        var stringEqualer = options.GetEqualityComparer();
        for (; start < maxCommonComponents; start++)
        {
            var fromComponent = fromComponents[start];
            var toComponent = toComponents[start];
            if (start == 0)
            {
                if (!EquateStringCaseInsensitive(fromComponent, toComponent))
                {
                    break;
                }
            }
            else
            {
                if (!stringEqualer(fromComponent, toComponent))
                {
                    break;
                }
            }
        }

        if (start == 0)
        {
            return toComponents;
        }

        var numDotDotSlashes = fromComponents.Length - start;
        var result = new string[1 + numDotDotSlashes + toComponents.Length - start];

        result[0] = "";
        var i = 1;
        // Add all the relative components until we hit a common directory.
        for (var n = 0; n < numDotDotSlashes; n++)
        {
            result[i] = "..";
            i++;
        }

        // Now add all the remaining components of the "to" path.
        // Go 的 `for j := start; j < len(toComponents); j++ { result[i] = toComponents[j]; i++ }` ⇒ 连续区间，
        // 用内置的 `Array.Copy`（memmove）取代手写循环。
        Array.Copy(toComponents, start, result, i, toComponents.Length - start);

        return result;
    }

    public static string GetRelativePathFromDirectory(string fromDirectory, string to, ComparePathsOptions options)
    {
        if ((GetRootLength(fromDirectory) > 0) != (GetRootLength(to) > 0))
        {
            Debug.Fail("paths must either both be absolute or both be relative");
        }

        var pathComponents = GetPathComponentsRelativeTo(fromDirectory, to, options);
        return GetPathFromPathComponents(pathComponents);
    }

    public static string GetRelativePathFromFile(string from, string to, ComparePathsOptions options)
        => EnsurePathIsNonModuleName(GetRelativePathFromDirectory(GetDirectoryPath(from), to, options));

    public static string ConvertToRelativePath(string absoluteOrRelativePath, ComparePathsOptions options)
    {
        if (!IsRootedDiskPath(absoluteOrRelativePath))
        {
            return absoluteOrRelativePath;
        }

        return GetRelativePathToDirectoryOrUrl(options.CurrentDirectory, absoluteOrRelativePath, false /*isAbsolutePathAnUrl*/, options);
    }

    public static string GetRelativePathToDirectoryOrUrl(string directoryPathOrUrl, string relativeOrAbsolutePath, bool isAbsolutePathAnUrl, ComparePathsOptions options)
    {
        var pathComponents = GetPathComponentsRelativeTo(
            directoryPathOrUrl,
            relativeOrAbsolutePath,
            options);

        var firstComponent = pathComponents[0];
        if (isAbsolutePathAnUrl && IsRootedDiskPath(firstComponent))
        {
            var prefix = firstComponent[0] == DirectorySeparator ? "file://" : "file:///";
            pathComponents[0] = prefix + firstComponent;
        }

        return GetPathFromPathComponents(pathComponents);
    }

    // Gets the portion of a path following the last (non-terminal) separator (`/`).
    // Semantics align with NodeJS's `path.basename` except that we support URL's as well.
    // If the base name has any one of the provided extensions, it is removed.
    //
    //	// POSIX
    //	GetBaseFileName("/path/to/file.ext") == "file.ext"
    //	GetBaseFileName("/path/to/") == "to"
    //	GetBaseFileName("/") == ""
    //	// DOS
    //	GetBaseFileName("c:/path/to/file.ext") == "file.ext"
    //	GetBaseFileName("c:/path/to/") == "to"
    //	GetBaseFileName("c:/") == ""
    //	GetBaseFileName("c:") == ""
    //	// URL
    //	GetBaseFileName("http://typescriptlang.org/path/to/file.ext") == "file.ext"
    //	GetBaseFileName("http://typescriptlang.org/path/to/") == "to"
    //	GetBaseFileName("http://typescriptlang.org/") == ""
    //	GetBaseFileName("http://typescriptlang.org") == ""
    //	GetBaseFileName("file://server/path/to/file.ext") == "file.ext"
    //	GetBaseFileName("file://server/path/to/") == "to"
    //	GetBaseFileName("file://server/") == ""
    //	GetBaseFileName("file://server") == ""
    //	GetBaseFileName("file:///path/to/file.ext") == "file.ext"
    //	GetBaseFileName("file:///path/to/") == "to"
    //	GetBaseFileName("file:///") == ""
    //	GetBaseFileName("file://") == ""
    public static string GetBaseFileName(string path)
    {
        path = NormalizeSlashes(path);

        // if the path provided is itself the root, then it has no file name.
        var rootLength = GetRootLength(path);
        if (rootLength == path.Length)
        {
            return "";
        }

        // return the trailing portion of the path starting after the last (non-terminal) directory
        // separator but not including any trailing directory separator.
        path = RemoveTrailingDirectorySeparator(path);
        return path[Math.Max(GetRootLength(path), path.LastIndexOf('/') + 1)..];
    }

    // Gets the file extension for a path.
    // If extensions are provided, gets the file extension for a path, provided it is one of the provided extensions.
    //
    //	GetAnyExtensionFromPath("/path/to/file.ext", nil, false) == ".ext"
    //	GetAnyExtensionFromPath("/path/to/file.ext/", nil, false) == ".ext"
    //	GetAnyExtensionFromPath("/path/to/file", nil, false) == ""
    //	GetAnyExtensionFromPath("/path/to.ext/file", nil, false) == ""
    //	GetAnyExtensionFromPath("/path/to/file.ext", ".ext", true) === ".ext"
    //	GetAnyExtensionFromPath("/path/to/file.js", ".ext", true) === ""
    //	GetAnyExtensionFromPath("/path/to/file.js", [".ext", ".js"], true) === ".js"
    //	GetAnyExtensionFromPath("/path/to/file.ext", ".EXT", false) === ""
    public static string GetAnyExtensionFromPath(string path, string[] extensions, bool ignoreCase)
    {
        // Retrieves any string from the final "." onwards from a base file name.
        // Unlike extensionFromPath, which throws an exception on unrecognized extensions.
        if (extensions.Length > 0)
        {
            return GetAnyExtensionFromPathWorker(RemoveTrailingDirectorySeparator(path), extensions, GetStringEqualityComparer(ignoreCase));
        }

        var baseFileName = GetBaseFileName(path);
        var extensionIndex = baseFileName.LastIndexOf('.');
        if (extensionIndex >= 0)
        {
            return baseFileName[extensionIndex..];
        }

        return "";
    }

    public static string GetLongestExtensionFromPath(string path, string[] extensions, bool ignoreCase)
    {
        path = RemoveTrailingDirectorySeparator(path);
        var comparer = GetStringEqualityComparer(ignoreCase);
        var longest = "";
        foreach (var extension in extensions)
        {
            if (extension.Length > longest.Length)
            {
                var matched = TryGetExtensionFromPath(path, extension, comparer);
                if (matched.Length != 0)
                {
                    longest = matched;
                }
            }
        }

        return longest;
    }

    private static string GetAnyExtensionFromPathWorker(string path, string[] extensions, Func<string, string, bool> stringEqualityComparer)
    {
        foreach (var extension in extensions)
        {
            var result = TryGetExtensionFromPath(path, extension, stringEqualityComparer);
            if (result.Length != 0)
            {
                return result;
            }
        }

        return "";
    }

    private static string TryGetExtensionFromPath(string path, string extension, Func<string, string, bool> stringEqualityComparer)
    {
        if (!extension.StartsWith(".", StringComparison.Ordinal))
        {
            extension = "." + extension;
        }

        if (path.Length >= extension.Length && path[path.Length - extension.Length] == '.')
        {
            var pathExtension = path[(path.Length - extension.Length)..];
            if (stringEqualityComparer(pathExtension, extension))
            {
                return pathExtension;
            }
        }

        return "";
    }

    public static bool PathIsRelative(string path)
    {
        // True if path is ".", "..", or starts with "./", "../", ".\\", or "..\\".

        if (path == "." || path == "..")
        {
            return true;
        }

        if (path.Length >= 2 && path[0] == '.' && (path[1] == '/' || path[1] == '\\'))
        {
            return true;
        }

        if (path.Length >= 3 && path[0] == '.' && path[1] == '.' && (path[2] == '/' || path[2] == '\\'))
        {
            return true;
        }

        return false;
    }

    // EnsurePathIsNonModuleName ensures a path is either absolute (prefixed with `/` or `c:`) or dot-relative (prefixed
    // with `./` or `../`) so as not to be confused with an unprefixed module name.
    public static string EnsurePathIsNonModuleName(string path)
    {
        if (!PathIsAbsolute(path) && !PathIsRelative(path))
        {
            return "./" + path;
        }

        return path;
    }

    public static bool IsExternalModuleNameRelative(string moduleName)
    {
        // TypeScript 1.0 spec (April 2014): 11.2.1
        // An external module name is "relative" if the first term is "." or "..".
        // Update: We also consider a path like `C:\foo.ts` "relative" because we do not search for it in `node_modules` or treat it as an ambient module.
        return PathIsRelative(moduleName) || IsRootedDiskPath(moduleName);
    }

    public static int ComparePaths(string a, string b, ComparePathsOptions options)
    {
        a = CombinePaths(options.CurrentDirectory, a);
        b = CombinePaths(options.CurrentDirectory, b);

        if (a == b)
        {
            return 0;
        }

        if (a.Length == 0)
        {
            return -1;
        }

        if (b.Length == 0)
        {
            return 1;
        }

        // NOTE: Performance optimization - shortcut if the root segments differ as there would be no
        //       need to perform path reduction.
        var aRoot = a[..GetRootLength(a)];
        var bRoot = b[..GetRootLength(b)];
        var result = CompareStringsCaseInsensitive(aRoot, bRoot);
        if (result != 0)
        {
            return result;
        }

        // NOTE: Performance optimization - shortcut if there are no relative path segments in
        //       the non-root portion of the path
        var aRest = a[aRoot.Length..];
        var bRest = b[bRoot.Length..];
        if (!HasRelativePathSegment(aRest) && !HasRelativePathSegment(bRest))
        {
            return options.GetComparer()(aRest, bRest);
        }

        // The path contains a relative path segment. Normalize the paths and perform a slower component
        // by component comparison.
        var aComponents = ReducePathComponents(GetPathComponents(a, ""));
        var bComponents = ReducePathComponents(GetPathComponents(b, ""));
        var sharedLength = Math.Min(aComponents.Length, bComponents.Length);
        for (var i = 1; i < sharedLength; i++)
        {
            result = options.GetComparer()(aComponents[i], bComponents[i]);
            if (result != 0)
            {
                return result;
            }
        }

        // Go: cmp.Compare(len(aComponents), len(bComponents))（结果同样是 -1/0/1）。
        return aComponents.Length.CompareTo(bComponents.Length);
    }

    public static int ComparePathsCaseSensitive(string a, string b, string currentDirectory)
        => ComparePaths(a, b, new ComparePathsOptions(useCaseSensitiveFileNames: true, currentDirectory: currentDirectory));

    public static int ComparePathsCaseInsensitive(string a, string b, string currentDirectory)
        => ComparePaths(a, b, new ComparePathsOptions(useCaseSensitiveFileNames: false, currentDirectory: currentDirectory));

    public static bool ContainsPath(string parent, string child, ComparePathsOptions options)
    {
        parent = CombinePaths(options.CurrentDirectory, parent);
        child = CombinePaths(options.CurrentDirectory, child);
        if (parent.Length == 0 || child.Length == 0)
        {
            return false;
        }

        if (parent == child)
        {
            return true;
        }

        var parentComponents = ReducePathComponents(GetPathComponents(parent, ""));
        var childComponents = ReducePathComponents(GetPathComponents(child, ""));
        if (childComponents.Length < parentComponents.Length)
        {
            return false;
        }

        var componentComparer = options.GetEqualityComparer();
        for (var i = 0; i < parentComponents.Length; i++)
        {
            var comparer = i == 0 ? (Func<string, string, bool>)EquateStringCaseInsensitive : componentComparer;
            if (!comparer(parentComponents[i], childComponents[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool FileExtensionIs(string path, string extension)
        => path.Length > extension.Length && path.EndsWith(extension, StringComparison.Ordinal);

    // Calls `callback` on `directory` and every ancestor directory it has, returning the first defined result.
    // Stops at global cache location
    //
    // 核对过 Go 侧：internal/tspath 里**没有**任何全局缓存/互斥量（没有 sync.Map/Mutex），
    // `globalCacheLocation` 只是一个"走到它就停"的路径参数，缓存由调用方自己持有，
    // 因此 C# 侧同样不需要线程安全结构（本移植全程无共享可变状态）。
    public static T ForEachAncestorDirectoryStoppingAtGlobalCache<T>(string globalCacheLocation, string directory, Func<string, (T Result, bool Stop)> callback)
    {
        ForEachAncestorDirectory(directory, ancestorDirectory =>
        {
            var (callbackResult, stop) = callback(ancestorDirectory);
            return (callbackResult, stop || ancestorDirectory == globalCacheLocation);
        }, out var result);
        return result;
    }

    public static bool ForEachAncestorDirectory<T>(string directory, Func<string, (T Result, bool Stop)> callback, out T result)
    {
        while (true)
        {
            var (callbackResult, stop) = callback(directory);
            if (stop)
            {
                result = callbackResult;
                return true;
            }

            var parentPath = GetDirectoryPath(directory);
            if (parentPath == directory)
            {
                // Go 在这里返回 T 的零值；无约束泛型的零值对引用类型就是 null，C# 只能这样表达。
                result = default!;
                return false;
            }

            directory = parentPath;
        }
    }

    public static bool HasExtension(string fileName) => GetBaseFileName(fileName).Contains('.');

    // Go 的 (string, string, bool) 按架构文档 §3.2 映射成 TryX 形式。
    public static bool TrySplitVolumePath(string path, out string volume, out string rest)
    {
        if (path.Length >= 2 && IsVolumeCharacter(path[0]) && path[1] == ':')
        {
            // Go 用 strings.ToLower；盘符保证是 ASCII 字母，char.ToLowerInvariant 等价。
            volume = char.ToLowerInvariant(path[0]) + ":";
            rest = path[2..];
            return true;
        }

        volume = "";
        rest = path;
        return false;
    }

    // GetCommonParents returns the smallest set of directories that are parents of all paths with
    // at least `minComponents` directory components. Any path that has fewer than `minComponents` directory components
    // will be returned in the second return value. Examples:
    //
    //	/a/b/c/d, /a/b/c/e, /a/b/f/g  =>  /a/b
    //	/a/b/c/d, /a/b/c/e, /a/b/f/g, /x/y  =>  /
    //	/a/b/c/d, /a/b/c/e, /a/b/f/g, /x/y  (minComponents: 2)	=>  /a/b, /x/y
    //	c:/a/b/c/d, d:/a/b/c/d =>	c:/a/b/c/d, d:/a/b/c/d
    //
    // Go 的第二个返回值是 map[string]struct{}（集合语义），C# 用 HashSet<string>；nil 用空集合表示。
    public static (string[] Parents, HashSet<string> Ignored) GetCommonParents(
        string[] paths,
        int minComponents,
        Func<string, string, string[]> getPathComponents,
        ComparePathsOptions options)
    {
        if (minComponents < 1)
        {
            Debug.Fail("minComponents must be at least 1");
        }

        if (paths.Length == 0)
        {
            return ([], []);
        }

        if (paths.Length == 1)
        {
            if (ReducePathComponents(getPathComponents(paths[0], options.CurrentDirectory)).Length < minComponents)
            {
                return ([], [paths[0]]);
            }

            return (paths, []);
        }

        var ignored = new HashSet<string>();
        var pathComponents = new List<string[]>(paths.Length);
        foreach (var path in paths)
        {
            var components = ReducePathComponents(getPathComponents(path, options.CurrentDirectory));
            if (components.Length < minComponents)
            {
                ignored.Add(path);
            }
            else
            {
                pathComponents.Add(components);
            }
        }

        var results = GetCommonParentsWorker([.. pathComponents], minComponents, options);
        var resultPaths = new string[results.Length];
        for (var i = 0; i < results.Length; i++)
        {
            resultPaths[i] = GetPathFromPathComponents(results[i]);
        }

        return (resultPaths, ignored);
    }

    private static string[][] GetCommonParentsWorker(string[][] componentGroups, int minComponents, ComparePathsOptions options)
    {
        if (componentGroups.Length == 0)
        {
            return [];
        }

        // Determine the maximum depth we can consider
        var maxDepth = componentGroups[0].Length;
        for (var i = 1; i < componentGroups.Length; i++)
        {
            var l = componentGroups[i].Length;
            if (l < maxDepth)
            {
                maxDepth = l;
            }
        }

        var equality = options.GetEqualityComparer();
        for (var lastCommonIndex = 0; lastCommonIndex < maxDepth; lastCommonIndex++)
        {
            var candidate = componentGroups[0][lastCommonIndex];
            // Go: for j, comps := range componentGroups[1:]（j 只用来算 orderedGroups 的容量）。
            for (var j = 0; j < componentGroups.Length - 1; j++)
            {
                var comps = componentGroups[j + 1];
                if (!equality(candidate, comps[lastCommonIndex])) // divergence
                {
                    if (lastCommonIndex < minComponents)
                    {
                        // Not enough components, we need to fan out
                        var orderedGroups = new List<Path>(componentGroups.Length - j);
                        var newGroups = new Dictionary<Path, (string[] Head, List<string[]> Tails)>();
                        foreach (var g in componentGroups)
                        {
                            var key = ToPath(g[lastCommonIndex], options.CurrentDirectory, options.UseCaseSensitiveFileNames);
                            List<string[]> tails;
                            if (newGroups.TryGetValue(key, out var group))
                            {
                                tails = group.Tails;
                            }
                            else
                            {
                                orderedGroups.Add(key);
                                tails = [];
                            }

                            tails.Add(g[(lastCommonIndex + 1)..]);
                            // Go 每次赋值都会重写 head（同一个 key 的最后一组获胜），这里保持一致。
                            newGroups[key] = (g[..(lastCommonIndex + 1)], tails);
                        }

                        // Go: slices.Sort([]Path)（Path 是 string，按字节序）；这里用序数比较得到相同顺序。
                        orderedGroups.Sort((x, y) => string.CompareOrdinal(x.Value, y.Value));
                        var result = new List<string[]>(newGroups.Count);
                        foreach (var key in orderedGroups)
                        {
                            var group = newGroups[key];
                            var subResults = GetCommonParentsWorker([.. group.Tails], minComponents - (lastCommonIndex + 1), options);
                            foreach (var sr in subResults)
                            {
                                // Go 用 append(group.head, sr...)（可能复用底层数组）；这里拼接成新数组，
                                // 逻辑结果相同且不共享存储。
                                var combined = new string[group.Head.Length + sr.Length];
                                Array.Copy(group.Head, 0, combined, 0, group.Head.Length);
                                Array.Copy(sr, 0, combined, group.Head.Length, sr.Length);
                                result.Add(combined);
                            }
                        }

                        return [.. result];
                    }

                    return [componentGroups[0][..lastCommonIndex]];
                }
            }
        }

        return [componentGroups[0][..maxDepth]];
    }

    public static bool StartsWithDirectory(string fileName, string directoryName, bool useCaseSensitiveFileNames)
    {
        if (directoryName.Length == 0)
        {
            return false;
        }

        var canonicalFileName = GetCanonicalFileName(fileName, useCaseSensitiveFileNames);
        var canonicalDirectoryName = GetCanonicalFileName(directoryName, useCaseSensitiveFileNames);
        // Go: strings.TrimSuffix(…, "/") 与 TrimSuffix(…, "\\")，各最多去掉一个后缀字符。
        if (canonicalDirectoryName.EndsWith('/'))
        {
            canonicalDirectoryName = canonicalDirectoryName[..^1];
        }

        if (canonicalDirectoryName.EndsWith('\\'))
        {
            canonicalDirectoryName = canonicalDirectoryName[..^1];
        }

        return canonicalFileName.StartsWith(canonicalDirectoryName + "/", StringComparison.Ordinal)
            || canonicalFileName.StartsWith(canonicalDirectoryName + "\\", StringComparison.Ordinal);
    }

    public static int CompareNumberOfDirectorySeparators(string path1, string path2)
    {
        // Go: cmp.Compare(strings.Count(path, "/"), …)（结果同样是 -1/0/1）。
        return path1.AsSpan().Count('/').CompareTo(path2.AsSpan().Count('/'));
    }
}

// Go 把 ComparePathsOptions 声明在 path.go 中部；C# 的类型不能嵌在别的类型里，所以放在文件末尾
// （同一个命名空间，调用点不受影响）。
public readonly struct ComparePathsOptions
{
    private readonly bool _useCaseSensitiveFileNames;
    private readonly string? _currentDirectory;

    // Go 的零值构造 ComparePathsOptions{} 对应 new ComparePathsOptions()（两个参数都有默认值）。
    public ComparePathsOptions(bool useCaseSensitiveFileNames = false, string? currentDirectory = null)
    {
        this._useCaseSensitiveFileNames = useCaseSensitiveFileNames;
        this._currentDirectory = currentDirectory;
    }

    public bool UseCaseSensitiveFileNames => _useCaseSensitiveFileNames;

    // Go 的 string 零值是 ""，而 C# 的 default(ComparePathsOptions) 会把字段置为 null；
    // 这里回退到空串，使 default(ComparePathsOptions) 与 Go 的 ComparePathsOptions{} 行为一致。
    public string CurrentDirectory => _currentDirectory ?? string.Empty;

    public Comparison<string> GetComparer() => GetStringComparer(!_useCaseSensitiveFileNames);

    // Go 的 getEqualityComparer 是包内可见（unexported），C# 用 internal 对应。（未加 IEqualityComparer
    // 包装：Go 的用法就是把比较器当回调传给 tryGetExtensionFromPath 这类函数。）
    internal Func<string, string, bool> GetEqualityComparer() => GetStringEqualityComparer(!_useCaseSensitiveFileNames);
}

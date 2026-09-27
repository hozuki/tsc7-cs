// ported from: internal/fswatch/pathcompare.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `pathComparer` 是**未导出**类型（`pathkey.go` 里导出的 `PathComparer` 是它的薄包装）→ 按 C# 习惯命名为
//    `VolumePathComparer`（描述"被监听卷的文件名等价关系"）。它是 `readonly record struct`，字段只有 `IgnoreCase`。
//    Go 里 `dw.comparer != comparer` 是逐字段比较 → C# 由 record struct 生成的 `!=`/`==` 一一对应。
// 2) Go 的 `comparisonPath` 带指针接收者（`fold()` 会写回 `folded`/`ready`），且多处按值传递后又在本地折叠 →
//    C# 用**可变 struct**（字段 public/internal），需要写回调用方拷贝的方法用 `ref ComparisonPath`。这是为了让
//    "Go 的 struct 值拷贝语义"逐字保留（若改成 class，`cb.fileComparison` 这类被共享的字段会被就地改写，
//    `triggerCallbacks` 在锁外读取时就不再安全）。
// 3) Go 的 `comparisonCache map[string]string` 在 `fold()` 里靠 `*p.cache` 懒创建（指针到 map 才能赋值）→
//    C# 的引用类型 `ComparisonCache`（内部 `Dictionary` 懒创建）；Go 的 "cache == nil" 对应 `Cache` 为 null。
// 4) Go 的 `pathSuffixASCII(root, path) (string, bool, bool)` 是**包级函数** → `FsWatch.PathSuffixASCII`
//    （返回三元组 `(Suffix, Ok, Unicode)`）；`pathSuffixFoldUnicode` → `FsWatch.PathSuffixFoldUnicode`
//    （Go 的 `(T, bool)` → `TryX` 形状）。两者逐行对照 Go：Go 是按 **UTF-8 字节**走的，C# 按 UTF-16 代码单元走；
//    因为 ASCII 分支上二者一一对应（字节 ≥ 0x80 ⇔ 代码单元 > 0x7F，且只需 ASCII 时下标即字节下标），
//    非 ASCII 一律转交 Unicode 分支，所以语义等价。
// 5) Go 的 `strings.EqualFold`（Unicode simple case folding）**没有** C# 等价物：实测 .NET 的
//    `StringComparison.OrdinalIgnoreCase` 对 `"s" vs "ſ"`、`"k" vs "K"(U+212A)` 判 false，而 Go 判 true →
//    本文件按 Go 的算法实现 `FsWatch.EqualFold`（详细理由与实测见该方法的注释），`PathSuffixFoldUnicode` 用它。
namespace TypeScript.FsWatch;

/// <summary>Go: <c>type pathComparer struct</c>。一个被监听卷的文件名等价关系（不可变、可共享）。</summary>
internal readonly record struct VolumePathComparer(bool IgnoreCase)
{
    /// <summary>Go: <c>pathComparer{}</c>（零值 = 逐字节精确比较）。</summary>
    internal static readonly VolumePathComparer Exact = new(false);

    /// <summary>
    /// Go: <c>func (c pathComparer) prepare(path string) comparisonPath</c>。监听根在发布前准备好、之后不再变化；
    /// 事件路径只在一次路由操作内局部使用，按需折叠。
    /// </summary>
    internal ComparisonPath Prepare(string path)
    {
        var p = new ComparisonPath { Path = path };
        if (IgnoreCase && FsWatch.NativePathFolding)
        {
            p.Fold();
        }

        return p;
    }

    /// <summary>Go: <c>func (c pathComparer) suffix(root, path string) (string, bool)</c>。</summary>
    internal bool Suffix(string root, string path, out string suffix)
    {
        var p = new ComparisonPath { Path = path };
        return SuffixPrepared(new ComparisonPath { Path = root }, ref p, out suffix);
    }

    /// <summary>Go: <c>func (c pathComparer) suffixPrepared(root comparisonPath, path *comparisonPath) (string, bool)</c>。</summary>
    internal bool SuffixPrepared(in ComparisonPath root, ref ComparisonPath path, out string suffix)
    {
        if (FsWatch.IsInDirectoryOrSelf(root.Path, path.Path))
        {
            suffix = path.Path[root.Path.Length..];
            return true;
        }

        if (!IgnoreCase || root.Path.Length == 0)
        {
            suffix = "";
            return false;
        }

        (var asciiSuffix, var ok, var unicode) = FsWatch.PathSuffixASCII(root.Path, path.Path);
        if (!unicode)
        {
            suffix = asciiSuffix;
            return ok;
        }

        return SuffixUnicode(root, ref path, out suffix);
    }

    private bool SuffixUnicode(in ComparisonPath root, ref ComparisonPath path, out string suffix)
    {
        if (!FsWatch.NativePathFolding)
        {
            return FsWatch.PathSuffixFoldUnicode(root.Path, path.Path, out suffix);
        }

        var a = root.Fold();
        var b = path.Fold();
        if (a.Length == 0 || b.Length == 0)
        {
            // CFString 表示不了非法 UTF-8；对畸形路径保留简单的 folded 比较，而不是截断或丢字节。
            return FsWatch.PathSuffixFoldUnicode(root.Path, path.Path, out suffix);
        }

        if (!FsWatch.IsInDirectoryOrSelf(a, b))
        {
            suffix = "";
            return false;
        }

        if (a == b)
        {
            suffix = "";
            return true;
        }

        // 折叠与规范归一化会保留分隔符，但不保留字节长度。要在**原始**事件路径上找匹配边界，而不是折叠后的路径上。
        var offset = 0;
        var separators = root.Path.Count(ch => ch == '/');
        var trailingSeparator = root.Path[^1] == '/';
        if (!trailingSeparator)
        {
            separators++;
        }

        for (var i = 0; i < separators; i++)
        {
            var idx = path.Path.IndexOf('/', offset);
            if (idx < 0)
            {
                Debug.Fail("fswatch: folded path lost a directory boundary");
            }

            offset = idx + 1;
        }

        suffix = trailingSeparator ? path.Path[offset..] : path.Path[(offset - 1)..];
        return true;
    }

    /// <summary>Go: <c>func (c pathComparer) contains(root, path string) bool</c>。</summary>
    internal bool Contains(string root, string path) => Suffix(root, path, out _);

    /// <summary>Go: <c>func (c pathComparer) rebase(path, from, to string) (string, bool)</c>。</summary>
    internal bool Rebase(string path, string from, string to, out string result)
    {
        var p = new ComparisonPath { Path = path };
        return RebasePrepared(ref p, new ComparisonPath { Path = from }, to, out result);
    }

    /// <summary>Go: <c>func (c pathComparer) rebasePrepared(path *comparisonPath, from comparisonPath, to string) (string, bool)</c>。</summary>
    internal bool RebasePrepared(ref ComparisonPath path, in ComparisonPath from, string to, out string result)
    {
        if (FsWatch.IsInDirectoryOrSelf(from.Path, path.Path))
        {
            result = FsWatch.RebasePath(path.Path, from.Path, to);
            return true;
        }

        if (!IgnoreCase || from.Path.Length == 0)
        {
            result = "";
            return false;
        }

        (var suffix, var ok, var unicode) = FsWatch.PathSuffixASCII(from.Path, path.Path);
        if (unicode)
        {
            ok = SuffixUnicode(from, ref path, out suffix);
        }

        if (!ok)
        {
            result = "";
            return false;
        }

        result = FsWatch.JoinPathSuffix(to, suffix);
        return true;
    }
}

/// <summary>
/// Go: <c>type comparisonPath struct</c>。只在一次同步回调/终止扫描内共享，绝不发布给订阅者或存进 watch。
/// </summary>
internal struct ComparisonPath
{
    internal string Path;
    internal string Folded;
    internal bool Ready;
    internal ComparisonCache? Cache;

    /// <summary>Go: <c>func (p *comparisonPath) fold() string</c>。</summary>
    internal string Fold()
    {
        if (!Ready)
        {
            if (Cache is not null && Cache.TryGet(Path, out var cached))
            {
                Folded = cached;
                Ready = true;
                return cached;
            }

            Folded = FsWatch.FoldNativePath(Path);
            Ready = true;
            Cache?.Set(Path, Folded);
        }

        return Folded;
    }
}

/// <summary>Go: <c>type comparisonCache map[string]string</c>（`fold()` 里按需创建）。</summary>
internal sealed class ComparisonCache
{
    private Dictionary<string, string>? _map;

    internal bool TryGet(string path, out string folded)
    {
        if (_map is not null && _map.TryGetValue(path, out var value))
        {
            folded = value;
            return true;
        }

        folded = "";
        return false;
    }

    internal void Set(string path, string folded)
    {
        _map ??= new Dictionary<string, string>();
        _map[path] = folded;
    }
}

public static partial class FsWatch
{
    /// <summary>Go: <c>func pathSuffixASCII(root, path string) (string, bool, bool)</c>。</summary>
    internal static (string Suffix, bool Ok, bool Unicode) PathSuffixASCII(string root, string path)
    {
        var i = 0;

        // 一次跳 8 个字符比较公共前缀（Go 里是 `root[i:i+8] == path[i:i+8]` 的字节切片比较）。
        while (i + 8 <= root.Length && i + 8 <= path.Length
            && root.AsSpan(i, 8).SequenceEqual(path.AsSpan(i, 8)))
        {
            i += 8;
        }

        for (; i < root.Length && i < path.Length; i++)
        {
            var a = root[i];
            var b = path[i];
            if (a > 0x7F || b > 0x7F)
            {
                return ("", false, true);
            }

            if (a == b)
            {
                continue;
            }

            a |= (char)0x20;
            b |= (char)0x20;
            if (a != b || a < 'a' || a > 'z')
            {
                return ("", false, false);
            }
        }

        if (i == root.Length && (i == path.Length || path[i] == '/'))
        {
            return (path[i..], true, false);
        }

        return ("", false, (i < root.Length && root[i] > 0x7F) || (i < path.Length && path[i] > 0x7F));
    }

    /// <summary>
    /// Go: <c>func pathSuffixFoldUnicode(root, path string) (string, bool)</c>。逐段比较，避免假设大小写等价的
    /// UTF-8 串有相同字节长度（例如 s 与长 s）。
    /// </summary>
    internal static bool PathSuffixFoldUnicode(string root, string path, out string suffix)
    {
        while (true)
        {
            var rootCut = root.IndexOf('/');
            var pathCut = path.IndexOf('/');
            var rootPart = rootCut < 0 ? root : root[..rootCut];
            var pathPart = pathCut < 0 ? path : path[..pathCut];
            var rootMore = rootCut >= 0;
            var pathMore = pathCut >= 0;

            if (!EqualFold(rootPart, pathPart))
            {
                suffix = "";
                return false;
            }

            if (!rootMore)
            {
                if (pathMore)
                {
                    suffix = path[pathPart.Length..];
                    return true;
                }

                suffix = "";
                return true;
            }

            if (!pathMore)
            {
                suffix = "";
                return false;
            }

            root = root[(rootCut + 1)..];
            path = path[(pathCut + 1)..];
        }
    }

    /// <summary>
    /// Go: <c>strings.EqualFold</c>（Unicode **simple case folding** 等价）。
    ///
    /// 偏离：C# 的 <c>StringComparison.OrdinalIgnoreCase</c> 只按大写形式比较，**不等价于** Go 的 EqualFold——
    /// 实测 .NET 上 <c>"s" vs "ſ"(U+017F)</c> 与 <c>"k" vs "K"(U+212A)</c> 都是 false，而 Go 都是 true。
    /// 本实现按 Go 的算法逐码点判定：相等、或 ASCII 小写相等、或"大写形式相同 **或** 小写形式相同"
    /// （Unicode simple case folding 的等价类正好由 simple upper/lower 映射张成）。
    /// </summary>
    internal static bool EqualFold(string a, string b)
    {
        var i = 0;
        var j = 0;
        while (i < a.Length && j < b.Length)
        {
            var ca = NextCodePoint(a, ref i);
            var cb = NextCodePoint(b, ref j);
            if (ca == cb)
            {
                continue;
            }

            if (ca < 0x80 && cb < 0x80)
            {
                if (AsciiLower(ca) != AsciiLower(cb))
                {
                    return false;
                }

                continue;
            }

            if (ToUpperCodePoint(ca) != ToUpperCodePoint(cb) && ToLowerCodePoint(ca) != ToLowerCodePoint(cb))
            {
                return false;
            }
        }

        return i == a.Length && j == b.Length;
    }

    private static int AsciiLower(int c) => c is >= 'A' and <= 'Z' ? c + ('a' - 'A') : c;

    private static int NextCodePoint(string s, ref int index)
    {
        var c = s[index++];
        if (char.IsHighSurrogate(c) && index < s.Length && char.IsLowSurrogate(s[index]))
        {
            var cp = char.ConvertToUtf32(c, s[index]);
            index++;
            return cp;
        }

        return c;
    }

    private static int ToUpperCodePoint(int cp) => cp > 0xFFFF
        ? System.Text.Rune.ToUpperInvariant(new System.Text.Rune(cp)).Value
        : char.ToUpperInvariant((char)cp);

    private static int ToLowerCodePoint(int cp) => cp > 0xFFFF
        ? System.Text.Rune.ToLowerInvariant(new System.Text.Rune(cp)).Value
        : char.ToLowerInvariant((char)cp);
}

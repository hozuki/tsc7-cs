// ported from: internal/ls/autoimport/index.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `Index[T Named]` 泛型约束 → C# 的 `Index<T> where T : INamed`；Go 的 `interface{ Name() string }`
//    是结构性满足，C# 里必须显式实现（`Export : INamed`）。
// 2) Go 的 `map[rune][]int` 索引表 → `Dictionary<int, List<int>>`：§3.1 规定"Go 的 rune → 用 int 表示码点"，
//    不用 `Rune` 做键（`Rune` 无法表示孤立代理项，而且它是引用语义之外的 struct 键，行为与 int 码点一致）。
// 3) Go 的 `utf8.DecodeRuneInString`（对**字节**解码）→ §3.1 的 `Rune.DecodeFromUtf16`（对 UTF-16 代码单元解码）：
//    非法/孤立代理项时 Go 返回 U+FFFD，这里也同样只看 U+FFFD。注意 TSGO 会把孤立代理项编码成哨兵 UTF-8 字节
//    （§3.1 不移植该机制），所以"同一个字符串在两边得到同一个键"这一**内部一致性**结论不受影响。
// 4) Go 的 nil 切片返回 → C# 返回 `T[]`（`[]`）：调用点只做 len/range，二者等价（§3.2）。
// 5) `unicode.ToUpper/ToLower`（单码点 simple 映射）→ `Rune.ToUpperInvariant/ToLowerInvariant`（§3.4）；
//    `strings.ToLower`（完整映射）→ `StringUtil.ToLowerJS`。
// 6) `strings.EqualFold` → `StringUtil.EquateStringCaseInsensitive`（.NET 没有 SimpleFold 等价物，§3.4）。
// 7) Go 的 nil 接收者分支 `if idx == nil { return nil }`（Clone）：C# 实例方法不能在 null 上分派，删除该分支，
//    调用点写成 `idx?.Clone(...)`。
// 8) Go 的 unexported 方法 `insertAsWords`/`containsCharsInOrder` → internal `InsertAsWords`/`ContainsCharsInOrder`
//    （同程序集跨文件访问，§2）；`entries`/`index` 字段同上。
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.LsAutoImport;

/// <summary>Go: <c>type Named interface { Name() string }</c>（index.go:12-14）。</summary>
public interface INamed
{
    string Name();
}

/// <summary>Go: <c>type Index[T Named] struct</c>（index.go:16-21）。<br/>
/// 索引把"名字以某字母开头的条目"按大写字母归档、"名字含某单词且该单词以某字母开头"的条目按小写字母归档。
/// </summary>
public sealed class Index<T> where T : INamed
{
    internal List<T> _entries = [];

    internal Dictionary<int, List<int>>? _index;

    /// <summary>Go: <c>func (idx *Index[T]) Find(name string, caseSensitive bool) []T</c>（index.go:23-46）。</summary>
    public T[] Find(string name, bool caseSensitive)
    {
        if (_entries.Count == 0 || name.Length == 0)
        {
            return [];
        }
        var firstRune = RuneUtil.DecodeRuneAt(name, 0, out _);
        if (firstRune == RuneUtil.RuneError)
        {
            return [];
        }
        var firstRuneUpper = RuneUtil.ToUpperRune(firstRune);
        if (_index is null || !_index.TryGetValue(firstRuneUpper, out var candidates))
        {
            return [];
        }

        List<T>? results = null;
        foreach (var entryIndex in candidates)
        {
            var entry = _entries[entryIndex];
            var entryName = entry.Name();
            if ((caseSensitive && entryName == name) || (!caseSensitive && EquateStringCaseInsensitive(entryName, name)))
            {
                (results ??= []).Add(entry);
            }
        }

        return results is null ? [] : results.ToArray();
    }

    /// <summary>Go: <c>func (idx *Index[T]) SearchWordPrefix(prefix string) []T</c>（index.go:49-94）。<br/>
    /// 返回"名字里有一个以 prefix 首字符开头的单词、且名字按顺序（忽略大小写）包含 prefix 的所有字符"的条目。
    /// </summary>
    public T[] SearchWordPrefix(string prefix)
    {
        if (_entries.Count == 0)
        {
            return [];
        }

        if (prefix.Length == 0)
        {
            return _entries.ToArray();
        }

        prefix = ToLowerJS(prefix);
        var firstRune = RuneUtil.DecodeRuneAt(prefix, 0, out _);
        if (firstRune == RuneUtil.RuneError)
        {
            return [];
        }

        var firstRuneUpper = RuneUtil.ToUpperRune(firstRune);
        var firstRuneLower = RuneUtil.ToLowerRune(firstRune);

        // Look up entries that have words starting with this letter
        List<int>? nameStarts = null;
        List<int>? wordStarts = null;
        if (_index is not null)
        {
            _index.TryGetValue(firstRuneUpper, out nameStarts);
            if (firstRuneUpper != firstRuneLower)
            {
                _index.TryGetValue(firstRuneLower, out wordStarts);
            }
        }
        nameStarts ??= [];
        wordStarts ??= [];
        var count = nameStarts.Count + wordStarts.Count;
        if (count == 0)
        {
            return [];
        }

        // Filter entries by checking if they contain all characters in order
        var results = new List<T>(count);
        foreach (var starts in new[] { nameStarts, wordStarts })
        {
            foreach (var i in starts)
            {
                var entry = _entries[i];
                if (ContainsCharsInOrder(entry.Name(), prefix))
                {
                    results.Add(entry);
                }
            }
        }
        return results.ToArray();
    }

    /// <summary>Go: <c>func containsCharsInOrder(str, pattern string) bool</c>（index.go:96-111）。</summary>
    internal static bool ContainsCharsInOrder(string str, string pattern)
    {
        str = ToLowerJS(str);
        pattern = ToLowerJS(pattern);

        var patternIdx = 0;
        for (var i = 0; i < str.Length;)
        {
            var ch = RuneUtil.DecodeRuneAt(str, i, out var size);
            i += size;
            if (patternIdx < pattern.Length)
            {
                var patternRune = RuneUtil.DecodeRuneAt(pattern, patternIdx, out var patternSize);
                if (ch == patternRune)
                {
                    patternIdx += patternSize;
                }
            }
        }
        return patternIdx == pattern.Length;
    }

    /// <summary>Go: <c>func (idx *Index[T]) insertAsWords(value T)</c>（index.go:113-141）。</summary>
    internal void InsertAsWords(T value)
    {
        _index ??= [];

        var name = value.Name();
        if (name.Length == 0)
        {
            Debug.Fail("Cannot index entry with empty name");
            return;
        }
        var entryIndex = _entries.Count;
        _entries.Add(value);

        var indices = AutoImport.WordIndices(name);
        var seenRunes = new HashSet<int>();

        for (var i = 0; i < indices.Length; i++)
        {
            var start = indices[i];
            var firstRune = RuneUtil.DecodeRuneAt(name, start, out _);
            if (firstRune == RuneUtil.RuneError)
            {
                continue;
            }
            if (i == 0)
            {
                // Name start keyed by uppercase
                firstRune = RuneUtil.ToUpperRune(firstRune);
                Append(_index, firstRune, entryIndex);
                seenRunes.Add(firstRune); // (Still set seenRunes in case first character is non-alphabetic)
            }
            else
            {
                // Subsequent word starts keyed by lowercase
                firstRune = RuneUtil.ToLowerRune(firstRune);
                if (seenRunes.Add(firstRune))
                {
                    Append(_index, firstRune, entryIndex);
                }
            }
        }
    }

    /// <summary>Go: <c>func (idx *Index[T]) Clone(filter func(T) bool) *Index[T]</c>（index.go:143-186）。</summary>
    public Index<T> Clone(Func<T, bool> filter)
    {
        var newIdx = new Index<T>
        {
            _entries = new List<T>(_entries.Count),
            _index = new Dictionary<int, List<int>>(_index?.Count ?? 0),
        };

        // Build mapping from old index to new index for filtered entries
        var oldToNew = new Dictionary<int, int>(_entries.Count);
        for (var oldIndex = 0; oldIndex < _entries.Count; oldIndex++)
        {
            if (filter(_entries[oldIndex]))
            {
                oldToNew[oldIndex] = newIdx._entries.Count;
                newIdx._entries.Add(_entries[oldIndex]);
            }
        }

        // Rebuild the index with remapped indices
        if (_index is not null)
        {
            foreach (var (r, oldIndices) in _index)
            {
                var newIndices = new List<int>(oldIndices.Count);
                foreach (var oldIndex in oldIndices)
                {
                    if (oldToNew.TryGetValue(oldIndex, out var newIndex))
                    {
                        newIndices.Add(newIndex);
                    }
                }
                if (newIndices.Count > 0)
                {
                    newIdx._index[r] = newIndices;
                }
            }
        }

        return newIdx;
    }

    private static void Append(Dictionary<int, List<int>> index, int key, int value)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = [];
            index[key] = list;
        }
        list.Add(value);
    }
}

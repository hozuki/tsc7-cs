// ported from: internal/testutil/fsbaselineutil/differ.go
//
// 与 Go 的差异（逐条；C# 的 vfs 与 Go 的 io/fs 形状不同，见 TestUtil/HarnessContext.cs 与 Vfs/VfsTest）：
// 1) `io/fs.FileInfo.Mode()&fs.ModeSymlink != 0` / `Mode().IsRegular()` → 本仓 `VfsTest.MapFile` 直接给了
//    `IsSymlink` / `IsDir` 两个 bool（Vfs/VfsTest/VfsTest.cs:52），所以"符号链接"= `file.IsSymlink`、
//    "普通文件"= `!file.IsDir && !file.IsSymlink`。
// 2) `MapFs().Entries()` → `IEnumerable<(string Path, MapFile File)>`（C# 用元组而不是双返回值）。
// 3) `MapFs().GetTargetOfSymlink(path)` → `TryGetTargetOfSymlink(path, out target)`（C# 版是 Try 形状；失败时 Go panic，
//    这里同样 `Debug.Fail`）。
// 4) `file.Data` 在 Go 是 `[]byte`、在本仓是 `string`（Go 侧处处 `string(file.Data)`），所以 `SanitizeInternalSymbolName`
//    直接吃字符串。
// 5) `maps.Keys` + `slices.Collect` + `slices.Sort` → `Keys.ToList()` + `Sort(StringComparer.Ordinal)`
//    （Go 的字符串排序是字节序，Ordinal 是最接近的对应物；见 TSPath 文件头对 EqualFold 的同类说明）。
// 6) `*d.WrittenFiles = collections.SyncSet[string]{}`（Go 在**原地**替换被指向的值）→ 这里逐键删除，
//    保持"调用方持有的那个 SyncSet 变空"的同一语义（换引用会让外部持有的旧集合仍非空）。
// 7) `fmt.Fprint(w, a, b, c)` / `fmt.Fprintln(w)` → `TextWriter` 的 `Write`（Go 的 Fprintln 只加 `\n`，没有 CRLF）。
// 8) `baseline io.Writer` → `TextWriter`（本仓其它 baseline 输出也是 TextWriter/StringWriter）。
// 9) `d.FS.FSys().(*vfstest.MapFS)`（类型断言）→ C# 的显式转型（失败抛 InvalidCastException，等价于 Go 的 panic）。

namespace TypeScript.TestUtil.FsBaselineUtil;

using System.Text.RegularExpressions;
using TypeScript.Collections;
using TypeScript.Vfs.Iovfs;
using TypeScript.Vfs.VfsTest;

/// <summary>Go: <c>type DiffEntry struct</c>。</summary>
public sealed class DiffEntry
{
    public string Content = "";
    public DateTime MTime;
    public bool IsWritten;
    public string SymlinkTarget = "";
}

/// <summary>Go: <c>type Snapshot struct</c>。</summary>
public sealed class Snapshot
{
    public Dictionary<string, DiffEntry> Snap = new();

    public SyncSet<string>? DefaultLibs;
}

/// <summary>Go: <c>type FSDiffer struct</c>。</summary>
public sealed class FSDiffer
{
    public FsWithSys FS = null!;

    public Func<SyncSet<string>?>? DefaultLibs;

    public SyncSet<string> WrittenFiles = new();

    private Snapshot? _serializedDiff;

    /// <summary>Go: <c>func (d *FSDiffer) MapFs() *vfstest.MapFS</c>。</summary>
    public MapFS MapFs() => (MapFS)FS.FSys();

    /// <summary>Go: <c>func (d *FSDiffer) SerializedDiff() *Snapshot</c>。</summary>
    public Snapshot? SerializedDiff() => _serializedDiff;

    /// <summary>Go: <c>func (d *FSDiffer) BaselineFSwithDiff(baseline io.Writer)</c>。</summary>
    public void BaselineFSWithDiff(TextWriter baseline)
    {
        // todo: baselines the entire fs, possibly doesn't correctly diff all cases of emitted files, since emit isn't fully implemented and doesn't always emit the same way as strada
        var snap = new Dictionary<string, DiffEntry>();

        var diffs = new Dictionary<string, string>();

        foreach (var (path, file) in MapFs().Entries())
        {
            if (file.IsSymlink)
            {
                if (!MapFs().TryGetTargetOfSymlink(path, out var target))
                {
                    // Go: panic("Failed to resolve symlink target: " + path)
                    Debug.Fail("Failed to resolve symlink target: " + path);
                }

                var newEntry = new DiffEntry { SymlinkTarget = target };
                snap[path] = newEntry;
                AddFsEntryDiff(diffs, newEntry, path);
                continue;
            }
            else if (!file.IsDir)
            {
                var content = SanitizeInternalSymbolName(file.Data);
                var newEntry = new DiffEntry { Content = content, MTime = file.ModTime, IsWritten = WrittenFiles.Has(path) };
                snap[path] = newEntry;
                AddFsEntryDiff(diffs, newEntry, path);
            }
        }

        if (_serializedDiff is not null)
        {
            foreach (var path in _serializedDiff.Snap.Keys)
            {
                if (MapFs().GetFileInfo(path) is null)
                {
                    // report deleted
                    AddFsEntryDiff(diffs, null, path);
                }
            }
        }

        var defaultLibs = new SyncSet<string>();
        if (DefaultLibs is not null && DefaultLibs() is not null)
        {
            DefaultLibs()!.Range(libPath =>
            {
                defaultLibs.Add(libPath);
                return true;
            });
        }

        _serializedDiff = new Snapshot
        {
            Snap = snap,
            DefaultLibs = defaultLibs,
        };
        var diffKeys = diffs.Keys.ToList();
        diffKeys.Sort(StringComparer.Ordinal);
        foreach (var path in diffKeys)
        {
            baseline.Write("//// [" + path + "] " + diffs[path] + "\n");
        }

        baseline.Write("\n");

        // Go: *d.WrittenFiles = collections.SyncSet[string]{}（见文件头第 6 条）
        foreach (var key in WrittenFiles.Keys().ToList())
        {
            WrittenFiles.Delete(key);
        }
    }

    private static readonly Regex _internalSymbolRegex = new("\uFFFD@[^@]+@[0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Replaces internal symbol names of shape \uFFFD@symbolName@123 with \uFFFD@symbolName@&lt;symbolId&gt;
    /// to avoid baselining differences in symbol ids, which can change between runs.
    /// </summary>
    public static string SanitizeInternalSymbolName(string s)
    {
        if (!s.Contains("\uFFFD@", StringComparison.Ordinal))
        {
            return s;
        }

        return _internalSymbolRegex.Replace(s, match =>
        {
            var text = match.Value;
            var idStart = text.LastIndexOf('@');
            return text[..idStart] + "@<symbolId>";
        });
    }

    /// <summary>Go: <c>func (d *FSDiffer) addFsEntryDiff(diffs map[string]string, newDirContent *DiffEntry, path string)</c>。</summary>
    private void AddFsEntryDiff(Dictionary<string, string> diffs, DiffEntry? newDirContent, string path)
    {
        DiffEntry? oldDirContent = null;
        SyncSet<string>? defaultLibs = null;
        if (_serializedDiff is not null)
        {
            _serializedDiff.Snap.TryGetValue(path, out oldDirContent);
            defaultLibs = _serializedDiff.DefaultLibs;
        }

        // todo handle more cases of fs changes
        if (oldDirContent is null)
        {
            if (DefaultLibs is null || DefaultLibs() is null || !DefaultLibs()!.Has(path))
            {
                if (newDirContent!.SymlinkTarget != "")
                {
                    diffs[path] = "-> " + newDirContent.SymlinkTarget + " *new*";
                }
                else
                {
                    diffs[path] = "*new* \n" + newDirContent.Content;
                }
            }
        }
        else if (newDirContent is null)
        {
            diffs[path] = "*deleted*";
        }
        else if (newDirContent.Content != oldDirContent.Content)
        {
            diffs[path] = "*modified* \n" + newDirContent.Content;
        }
        else if (newDirContent.IsWritten)
        {
            diffs[path] = "*rewrite with same content*";
        }
        else if (newDirContent.MTime != oldDirContent.MTime)
        {
            diffs[path] = "*mTime changed*";
        }
        else if (defaultLibs is not null && defaultLibs.Has(path) && DefaultLibs is not null && DefaultLibs() is not null && !DefaultLibs()!.Has(path))
        {
            // Lib file that was read
            diffs[path] = "*Lib*\n" + newDirContent.Content;
        }
    }

    /// <summary>Go: <c>type FileChange struct</c>。</summary>
    public sealed class FileChange
    {
        public string Path = "";
        public bool Deleted;
    }

    /// <summary>Go: <c>func (d *FSDiffer) ChangedPaths() []FileChange</c>。</summary>
    public List<FileChange>? ChangedPaths()
    {
        if (_serializedDiff is null)
        {
            return null;
        }

        var changes = new List<FileChange>();
        var oldSnap = _serializedDiff;

        // Check current files against previous snapshot.
        foreach (var (path, file) in MapFs().Entries())
        {
            if (file.IsSymlink || file.IsDir)
            {
                continue;
            }

            if (!oldSnap.Snap.TryGetValue(path, out var old))
            {
                // New file.
                changes.Add(new FileChange { Path = path });
            }
            else if (file.Data != old.Content || file.ModTime != old.MTime)
            {
                // Modified or touched file.
                changes.Add(new FileChange { Path = path });
            }
        }

        // Check for deleted files.
        foreach (var path in oldSnap.Snap.Keys)
        {
            if (MapFs().GetFileInfo(path) is null)
            {
                changes.Add(new FileChange { Path = path, Deleted = true });
            }
        }

        return changes;
    }
}

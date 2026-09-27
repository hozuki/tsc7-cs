// ported from: internal/project/filechange.go:1-98
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `FileChange` / `FileChangeSummary` 是**值类型**（按值传递、拷贝）。C# 侧它们是 class：
//    - `FileChange` 在 Go 里是 `[]FileChange` 的元素、只被读写字段，没有值语义依赖 ⇒ class 即可（迁移
//      成本最低：调用点的 `append(changes, FileChange{...})` → `list.Add(new FileChange{...})`）。
//    - `FileChangeSummary` 在 Go 里被**按值传递并返回**（`expandFileChanges`/`processFileChanges`/`flushChanges`
//      都返回它），语义上是"不可变的值快照"。C# 里同样是 class，但**所有**"复制"的调用点必须显式调用
//      `Clone()`（Go 是隐式的）—— 见 `Clone()` 的实现与调用点注释。`Clone()` 里 Go 是 `f.Closed = *f.Closed.Clone()`
//      （值结构体 + 内部指针 set 的浅拷贝），C# 的 `Set<T>.Clone()` 直接返回新实例。
//
// 2) Go 的 `collections.Set[DocumentUri]` 零值是 **nil map**（读空、写时自动 make，`Len()` 为 0）。
//    C# 的 `Set<T>` 是 class ⇒ 在构造函数里就 `new` 出来（`new FileChangeSummary()` 即 Go 的零值
//    `FileChangeSummary{}`），因此 Go 里所有 "nil map 也能用" 的分支在 C# 里等价于"空集合"。
//
// 3) `Opened`/`Reopened` 判空：Go 是 `f.Opened == ""`；C# 的 `DocumentUri` 是 `readonly record struct`
//    （`LsProto/Lsp.cs:33`），空值 = `default`（`Value` 为 ""），故写成 `Opened == default`。
//
// 4) `func (k FileChangeKind) IsWatchKind() bool` → C# 的扩展方法 `IsWatchKind(this FileChangeKind)`
//    （本仓既有做法：`Core/CompilerOptions.cs` 的 `String(this ModuleResolutionKind)` 等）。枚举名的
//    `FileChangeKind*` 前缀按 Go 的前缀裁剪（`FileChangeKindOpen` → `FileChangeKind.Open`）。
//
// 5) `mergeFileChangeSummary`（未导出）→ `FileChangeSummary.MergeFileChangeSummary` 静态方法：`project` 包
//    与类型 `Project` 同名，无法再建 `static class Project`，故包级函数挂到最相关类型的静态成员上
//    （先例：`Locale.Parse`/`Locale.WithLocale`，见 Locale/Locale.cs）。
//
// 6) `excessiveChangeThreshold`（未导出常量）→ `internal const int ExcessiveChangeThreshold`
//    （C# 的 const 一律 PascalCase，见 architecture.md §3.2）。
namespace TypeScript.Project;

/// <summary>Go 的 <c>FileChangeExpander</c>。</summary>
public interface FileChangeExpander
{
    FileChangeSummary ExpandFileChanges(FileChangeSummary summary);
}

/// <summary>Go 的 <c>FileChangeKind</c>。</summary>
public enum FileChangeKind
{
    Open,
    Close,
    Change,
    Save,
    WatchCreate,
    WatchChange,
    WatchDelete,
}

public static class FileChangeKindExtensions
{
    /// <summary>Go: <c>func (k FileChangeKind) IsWatchKind() bool</c>。</summary>
    public static bool IsWatchKind(this FileChangeKind k)
    {
        return k is FileChangeKind.WatchCreate or FileChangeKind.WatchChange or FileChangeKind.WatchDelete;
    }
}

/// <summary>Go 的 <c>FileChange</c>（见文件头第 1 条）。</summary>
public sealed class FileChange
{
    public FileChangeKind Kind;

    public LsProto.DocumentUri URI;

    /// <summary>Only set for Open/Change（Go: <c>Version int32</c>）。</summary>
    public int Version;

    /// <summary>Only set for Open。</summary>
    public string Content = "";

    /// <summary>Only set for Open。</summary>
    public LsProto.LanguageKind LanguageKind;

    /// <summary>Only set for Change（Go 的 nil 切片 → <c>T[]?</c>，见 architecture.md §3.2）。</summary>
    public LsProto.TextDocumentContentChangePartialOrWholeDocument[]? Changes;
}

/// <summary>Go 的 <c>FileChangeSummary</c>（见文件头第 1、2 条）。</summary>
public sealed class FileChangeSummary
{
    internal const int ExcessiveChangeThreshold = 1000;

    /// <summary>Only one file can be opened at a time per request。</summary>
    public LsProto.DocumentUri Opened;

    /// <summary>Reopened is set if a close and open occurred for the same file in a single batch of changes。</summary>
    public LsProto.DocumentUri Reopened;

    public Collections.Set<LsProto.DocumentUri> Closed = new();

    public Collections.Set<LsProto.DocumentUri> Changed = new();

    /// <summary>Only set when file watching is enabled。</summary>
    public Collections.Set<LsProto.DocumentUri> Created = new();

    /// <summary>Only set when file watching is enabled。</summary>
    public Collections.Set<LsProto.DocumentUri> Deleted = new();

    /// <summary>
    /// IncludesWatchChangeOutsideNodeModules is true if the summary includes a create, change, or delete watch
    /// event of a file outside a node_modules directory.
    /// </summary>
    public bool IncludesWatchChangeOutsideNodeModules;

    /// <summary>InvalidateAll indicates that all cached file state should be discarded。</summary>
    public bool InvalidateAll;

    /// <summary>Go: <c>func (f FileChangeSummary) Clone() FileChangeSummary</c>（Go 是值拷贝，C# 必须显式调用）。</summary>
    public FileChangeSummary Clone()
    {
        var result = MemberwiseClone() as FileChangeSummary ?? throw new System.Diagnostics.UnreachableException();
        result.Closed = Closed.Clone();
        result.Changed = Changed.Clone();
        result.Created = Created.Clone();
        result.Deleted = Deleted.Clone();
        return result;
    }

    public bool IsEmpty()
    {
        return !InvalidateAll
            && Opened == default
            && Reopened == default
            && Closed.Len() == 0
            && Changed.Len() == 0
            && Created.Len() == 0
            && Deleted.Len() == 0;
    }

    public bool HasExcessiveWatchEvents()
    {
        return InvalidateAll || Created.Len() + Deleted.Len() + Changed.Len() > ExcessiveChangeThreshold;
    }

    public bool HasExcessiveNonCreateWatchEvents()
    {
        return InvalidateAll || Deleted.Len() + Changed.Len() > ExcessiveChangeThreshold;
    }

    /// <summary>Go: <c>func mergeFileChangeSummary(dst *FileChangeSummary, src FileChangeSummary)</c>。</summary>
    public static void MergeFileChangeSummary(FileChangeSummary dst, FileChangeSummary src)
    {
        if (src.IsEmpty())
        {
            return;
        }

        if (src.InvalidateAll)
        {
            dst.InvalidateAll = true;
        }

        foreach (var uri in src.Changed.M)
        {
            dst.Changed.Add(uri);
        }

        foreach (var uri in src.Created.M)
        {
            dst.Created.Add(uri);
        }

        foreach (var uri in src.Deleted.M)
        {
            dst.Deleted.Add(uri);
        }

        if (src.IncludesWatchChangeOutsideNodeModules)
        {
            dst.IncludesWatchChangeOutsideNodeModules = true;
        }
    }
}

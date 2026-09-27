// ported from: internal/tsoptions/parsedoptions.go
//
// 映射约定（docs/architecture.md §2/§3.2）：
// 1) Go 的 `[]string`/`[]*T` 字段用 C# 数组；Go 的 nil slice 在本类型里只被 len()/range 消费（"空"与"nil"无行为差别），
//    故 `FileNames` 用非空数组（`= []`）表示，`ProjectReferences`/`ContentMappers` 保留 `?`（Go 侧 `NewParsedCommandLine`
//    会显式传 nil，且 `ContentMappers()` 有 `== nil` 的判断）。
// 2) Go 的 `*core.CompilerOptions` 等指针字段 → `?`（Go 侧确实可能为 nil：`ParsedCommandLine.CompilerOptions()` 返回它）。
// 3) Go 里 `parsedConfig := *p.ParsedConfig`（结构体浅拷贝）→ C# 用 `Clone()`（`MemberwiseClone`，§3.2）；
//    该方法是 C# 侧的补充成员（Go 没有），仅供 ParsedCommandLine 的 WithFileNames/ReloadFileNames 使用。
using TypeScript.ContentMapper;
using TypeScript.Core;

namespace TypeScript.TsOptions;

public sealed class ParsedOptions
{
    public CompilerOptions? CompilerOptions;
    public WatchOptions? WatchOptions;
    public TypeAcquisition? TypeAcquisition;

    public string[] FileNames = [];
    public ProjectReference[]? ProjectReferences;
    public Mapper[]? ContentMappers;

    /// <summary>Go 的 <c>parsedConfig := *p.ParsedConfig</c>（结构体浅拷贝）。</summary>
    internal ParsedOptions Clone() => (ParsedOptions)MemberwiseClone();
}

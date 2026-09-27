// ported from: internal/ls/host.go:1-25
//
// ⚠ **这是 `ls` 大切片的前置引导文件**（Main 裁定，见 M8Project 广播）：`internal/project` 的编译闭包需要
// `ls.Host` / `ls.Project` / `ls.LanguageService` 三个类型，而 `internal/ls` 整包依赖 `project`，
// 所以由 M8Project 按 Go 原样先落地这三个文件；**`ls` 切片请在它们之上继续扩展**（新增文件或用 `partial`），
// 不要改名/改命名空间。
//
// 与 Go 的偏离（逐条说明）：
// 1) `ReadFile(path string) (contents string, ok bool)` → `bool TryReadFile(string path, out string contents)`
//    （§3.2 的 `(T,bool)` → `TryX`；与 `Vfs.FS`、`SourceMap.Host` 的既有落点一致）。
// 2) `lsutil.UserPreferences` / `lsconv.Converters` / `autoimport.Registry` 分别由
//    `TypeScript.LsUtil` / `TypeScript.LsConv` / `TypeScript.LsAutoImport` 提供（各自由对应切片落地）。
// 3) `ReadDirectory(...)` 的 `[]string` 参数在 C# 里允许为 null（Go 里可以是 nil 切片）⇒ `string[]?`。
// 4) `GetPreferences(activeFile string)` / `GetECMALineInfo(fileName string)` 返回可为 null（Go 的指针语义）。
namespace TypeScript.Ls;

/// <summary>
/// Go 的 <c>ls.Host</c>：`project.Snapshot` 实现的"快照时刻"宿主接口。
/// </summary>
public interface Host
{
    bool UseCaseSensitiveFileNames();

    bool TryReadFile(string path, out string contents);

    LsConv.Converters Converters();

    LsUtil.UserPreferences GetPreferences(string activeFile);

    SourceMap.ECMALineInfo? GetECMALineInfo(string fileName);

    LsAutoImport.Registry AutoImportRegistry();

    /// <summary>
    /// Used for module specifier completions.
    /// ! Do not use for anything else, as this violates the principle that
    /// the host is a snapshot-in-time.
    /// </summary>
    string[] ReadDirectory(string currentDir, string path, string[]? extensions, string[]? excludes, string[]? includes, int depth);

    string[] GetDirectories(string path);

    bool DirectoryExists(string path);

    bool FileExists(string path);
}

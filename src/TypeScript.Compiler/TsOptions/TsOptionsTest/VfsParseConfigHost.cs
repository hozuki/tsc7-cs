// ported from: internal/tsoptions/tsoptionstest/vfsparseconfighost.go
//
// 说明（Go → C# 的改写）：
// 1) Go 的 `fixRoot`（vfsparseconfighost.go:12-20）**没有被包内任何文件引用**（全仓 grep 只有定义处），
//    属 Go 侧死代码 → 不移植（AGENTS.md：删掉死重）。
// 2) Go 的 `var _ tsoptions.ParseConfigHost = (*VfsParseConfigHost)(nil)` 是编译期断言；
//    C# 由 `: TsOptions.ParseConfigHost` 直接表达，不需要单独一行。
// 3) 导出字段 `Vfs`/`CurrentDirectory` 保留 PascalCase 原名（docs/architecture.md §2：Go 导出名镜像），
//    Go 的对象字面量 `&VfsParseConfigHost{Vfs: …, CurrentDirectory: …}` → C# 对象初始化器；
//    字段给零值兜底（`null!` / `""`）以免 CS8618（Go 的结构体零值语义）。
// 4) `TsOptions` 是 `TypeScript.TsOptions` 命名空间里的静态类（`ParseConfigHost` 是它的嵌套接口），
//    从嵌套命名空间里写 `TsOptions.ParseConfigHost` 解析到该静态类，不会歧义。
namespace TypeScript.TsOptions.TsOptionsTest;

using TypeScript.Vfs;

/// <summary>
/// Go: <c>type VfsParseConfigHost struct { Vfs vfs.FS; CurrentDirectory string }</c>
/// （tsoptionstest/vfsparseconfighost.go:22-25）。
/// </summary>
public sealed class VfsParseConfigHost : TsOptions.ParseConfigHost
{
    /// <summary>Go: 导出字段 <c>Vfs</c>。</summary>
    public FS Vfs = null!;

    /// <summary>Go: 导出字段 <c>CurrentDirectory</c>。</summary>
    public string CurrentDirectory = "";

    public VfsParseConfigHost()
    {
    }

    public VfsParseConfigHost(FS vfs, string currentDirectory)
    {
        Vfs = vfs;
        CurrentDirectory = currentDirectory;
    }

    /// <summary>Go: <c>func (h *VfsParseConfigHost) FS() vfs.FS</c>（vfsparseconfighost.go:27-29）。</summary>
    public FS FS() => Vfs;

    /// <summary>Go: <c>func (h *VfsParseConfigHost) GetCurrentDirectory() string</c>（vfsparseconfighost.go:31-33）。</summary>
    public string GetCurrentDirectory() => CurrentDirectory;
}

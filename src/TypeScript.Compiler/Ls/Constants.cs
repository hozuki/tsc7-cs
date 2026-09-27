// ported from: internal/ls/constants.go:1-6
//
// Go 的 `internal/ls` 包级常量 → 同名静态类 `Ls` 的常量成员（§2：Go 包 = 同名文件夹 + 同名命名空间，
// 包级函数/常量挂到同名静态类，调用点用 `using static TypeScript.Ls.Ls;` 裸名引用）。
//
// 与 Go 的偏离（逐条说明）：
// 1) `moduleSpecifierResolutionLimit` / `moduleSpecifierResolutionCacheAttemptLimit` 在 Go 里是**未导出**包级常量
//    → C# 里是 `internal const`（跨文件可见 = Go 的包级可见），命名沿用 Go 的 `_camelCase`（§3.2 命名规则）。
// 2) 这两个常量在 Go 全仓（含 `_test.go`）**没有任何引用**（已 grep `internal/`、`packages/`、`tools/` 确认）；
//    本文件仍 1:1 落地以保持"一个 Go 文件 = 一个 C# 文件"的可对照性（`internal const` 不会触发任何"未使用"警告）。
namespace TypeScript.Ls;

public static partial class Ls
{
    /// <summary>Go: <c>moduleSpecifierResolutionLimit = 100</c>（Go 里无引用，见文件头第 2 条）。</summary>
    internal const int _moduleSpecifierResolutionLimit = 100;

    /// <summary>Go: <c>moduleSpecifierResolutionCacheAttemptLimit = 1000</c>（Go 里无引用，见文件头第 2 条）。</summary>
    internal const int _moduleSpecifierResolutionCacheAttemptLimit = 1000;
}

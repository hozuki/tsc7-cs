// ported from: internal/compiler/pkg.go（全文，2 行）
//
// pkg.go 的内容只有 package 声明（`// Package compiler implements the TypeScript compiler.` + `package compiler`），
// 没有任何可移植的声明；本文件承载的是**该 Go package 的 C# 对应物**：与包同名的静态类 `Compiler`
// （docs/architecture.md §2：一个 Go package = 一个同名文件夹 + 同名命名空间，包级函数放**与该 package 同名的
// 静态类**，全名 `TypeScript.Compiler.Compiler`）。
//
// 文件名仍是 Go 文件名的 PascalCase（pkg.go → Pkg.cs），这样任何一个 .cs 都能和 Go 源文件并排对照。
// 类声明集中放在这里（而不是每个分片各写一遍）是为了给「一个 Go package = 一个同名静态类」一个明确出处：
// 其余分片要用包级函数时写 `public static partial class Compiler { … }` 追加即可（partial）。
//
// 命名空间遮蔽（本仓实测的坑，docs/architecture.md §2）：静态类 `Compiler` 与命名空间段 `TypeScript.Compiler`
// **不冲突**——C# 的名字查找在最内层命名空间声明（这里就是 `TypeScript.Compiler`）里先找**类型**，`Compiler`
// 正是声明在该命名空间里的类型，所以本命名空间内裸写 `Compiler.X` 命中类型而不是命名空间段。
// 这与既有先例 `TypeScript.Bundled.Bundled`、`TypeScript.Transformers.Transformers`、`TypeScript.Checker.Checker`
// 同款（已 grep 全仓确认不存在名为 `Compiler` 的其它类型/命名空间冲突）。
namespace TypeScript.Compiler;

/// <summary>
/// Go: <c>// Package compiler implements the TypeScript compiler.</c>（pkg.go 的全部内容）。<br/>
/// 本类收纳 <c>internal/compiler</c> 的**包级函数与包级常量**（Go 里没有自由函数，C# 里放进与包同名的静态类）；
/// 各分片按 Go 文件归属拆成 <c>partial</c>，成员与 Go 的包级声明一一对应。
/// </summary>
public static partial class Compiler
{
}

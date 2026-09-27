// ported from: internal/ls/crossproject.go:17-21（**只含 `Project` 接口这三行**）
//
// ⚠ **这是 `ls` 大切片的前置引导文件**（Main 裁定）：`internal/project` 的编译闭包需要 `ls.Project`，
// 而 `internal/ls` 整包依赖 `project`，故由 M8Project 先落地这个接口。`crossproject.go` 的**其余部分**
// （跨项目 language service 机制，436 行）仍归 `ls` 切片——请新建文件落地，不要改本文件。
//
// 与 Go 的偏离：
// 1) `GetProgram() *compiler.Program` → `Compiler.Program?`（Go 指针可空语义）。
// 2) 实现方：`TypeScript.Project.Project`（Go 的 `var _ ls.Project = (*Project)(nil)` 那个断言由
//    C# 的 `: Ls.Project` 声明本身承担）。
namespace TypeScript.Ls;

/// <summary>Go 的 <c>ls.Project</c>：跨项目操作需要的最小项目视图。</summary>
public interface Project
{
    string Id();

    Compiler.Program? GetProgram();

    bool HasFile(string fileName);
}

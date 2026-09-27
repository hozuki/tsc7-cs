// ported from: internal/sourcemap/source.go
//
// 与 Go 的差异：无（导出的接口名原样保留，见 docs/architecture.md §2）。
// 注意（M6 接线）：Go 的 `*ast.SourceFile` 在结构上就满足 `Source`；C# 必须**显式**声明 `: Source`。
// `TypeScript.Ast.SourceFile` 目前没有列这个接口（改 Ast 不在本切片范围），谁需要把 SourceFile 当 Source 用，
// 就在 Ast 侧补上 `, Source`（一行）。
namespace TypeScript.SourceMap;

using TypeScript.Core;

public interface Source
{
    string Text();

    string FileName();

    TextPos[] ECMALineMap();
}

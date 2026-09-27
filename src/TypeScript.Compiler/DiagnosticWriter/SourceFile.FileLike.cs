// ported from: internal/diagnosticwriter/diagnosticwriter.go
//
// Go 的 `FileLike` 是结构化接口：`*ast.SourceFile`（有 FileName/Text/ECMALineMap）天然满足它，
// `diagnosticwriter.ASTDiagnostic.File()` 直接把 `*ast.SourceFile` 当 `FileLike` 返回、并作为
// `errorsByFile` 的 key（指针同一性）。C# 的接口是名义的，且 `SourceFile` 是 sealed，别的程序集/命名空间
// 无法用 `is FileLike`、`as FileLike` 或隐式转换补上这一层——所以这里把 `FileLike` 追加进它（已是 partial）的
// 基类型表。`SourceFile` 已显式列出 `HasFileName`/`SourceFileLike`，加接口即满足 `FileName()`/`Text()`/
// `ECMALineMap()` 三个成员，不改 `Ast/SourceFile.cs`。
//
// 与 Go 的差异：Go 里不需要这行（结构满足）；C# 必须有。除此之外无其它差异。
namespace TypeScript.Ast;

public sealed partial class SourceFile : TypeScript.DiagnosticWriter.FileLike
{
}

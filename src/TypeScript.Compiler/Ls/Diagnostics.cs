// ported from: internal/ls/diagnostics.go:1-120
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级未导出函数 `getAllDiagnostics` → 静态类 `Ls` 的 `internal static GetAllDiagnostics`
//    （§2：包级函数挂同名静态类；未导出 → `internal`，跨文件可见 = Go 的包级可见；**方法一律 PascalCase 不加 `_`**，
//    .editorconfig 的 `members_should_be_pascal_case` 对 method/property/event 强制，见 docs/architecture.md §2）。
//    未导出函数 `isSynthesizedContentMappedDiagnostic` / `aggregateSynthesizedDiagnostics` / `worstCategory`
//    同理 → `IsSynthesizedContentMappedDiagnostic` / `AggregateSynthesizedDiagnostics` / `WorstCategory`
//    （G1 修正：上一版用了 `_camelCase`，IDE1006 在 TreatWarningsAsErrors 下是编译错误）。
//
// 2) Go 的 `type DocumentDiagnosticResponse = lsproto.RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport`
//    是**类型别名**，LsProto 的 C# 生成物不建别名（见 LsProto/Lsp.Generated.Methods.cs:503 处的注释："类型别名在
//    这里由使用点直接用目标类型"）⇒ 本文件直接用 `LsProto.RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport`。
//
// 3) Go 的 `(lsproto.DocumentDiagnosticResponse, error)` → C# 的 `(Response, Exception? Error)` 双返回值
//    （本仓既有约定，见 Ls/LanguageService.cs 的 `GetPreparedAutoImportView`；Go 这里 error 恒为 nil）。
//
// 4) Go 的 `append(diags, ...)` → `List<T>.AddRange`（§3.2 切片 → C# 数组/List）；`make([]*lsproto.Diagnostic, 0, size)`
//    → `new List<…>(size)`（只影响分配次数，不影响语义）。变参里的 nil 切片（Go 里可以是 nil）在 C# 里按
//    `?? []` 处理（§3.1 nil 切片）——本文件的循环只在 C# 数组上跑，调用方传的是数组。
//
// 5) `collections.OrderedMap[*ast.SourceFile, []*ast.Diagnostic]` → `Collections.OrderedMap<Ast.SourceFile, AstDiag[]?>`
//    （Go 的切片值类型 → C# 数组；Go 的零值 nil 切片 = C# 的 null，故用 `Concatenate(GetOrZero(file), [diag])`
//    复刻 `append(zeroValue, diag)` 的语义，而不是 C# 的 List 原地追加——语义等价但保持 `Map.Set` 的写法）。
//    `Entries()` 返回 `(K Key, V? Value)` 元组序列，与 Go 的 `range … Entries()` 对应。
//
// 6) `diagnostics.Virtual_code_produced_by_..._this_file`（Go 的 `diagnostics` 包级消息变量）→ `using static
//    TypeScript.Diagnostics.Diagnostics;` 后的裸名（消息常量是静态类 `TypeScript.Diagnostics.Diagnostics` 的成员，
//    见 Diagnostics/Messages.Generated.cs:1933）。
//
// 7) **`Diagnostic` 在 C# 里有两个同名类型**（`TypeScript.Ast.Diagnostic` 与 `TypeScript.LsProto.Diagnostic`），
//    裸名会 CS0104 ⇒ 沿用本仓既有别名约定（LsConv/Converters.cs:53,55）：`AstDiag` = AST 诊断、`LspDiag` = LSP 诊断。
//
// 8) `spanmap.FidelityNone` → `Fidelity.None`（C# 枚举成员去掉 Go 的前缀，见 SpanMap/SpanMap.cs:90-105）。
//
// 9) Go 的 `file.SpanMap()` 为 nil 时 Go 代码已显式判空（`file.SpanMap() == nil`）；C# 侧 `SpanMap()` 返回可空，
//    判空后需 `!`（本仓既有的"Go 的 nil 检查 → C# 的 null 检查 + `!`"写法）。
//
// 10) 深递归入口约定：本文件不递归（只是遍历），无需大栈线程。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.LsProto;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Ls.Ls;
using static TypeScript.LsConv.LsConv;
using AstDiag = TypeScript.Ast.Diagnostic;
using LspDiag = TypeScript.LsProto.Diagnostic;

namespace TypeScript.Ls;

public static partial class Ls
{
    /// <summary>
    /// getAllDiagnostics collects all diagnostics for a file: syntactic, semantic,
    /// suggestion, and (when declarations are emitted) declaration diagnostics.
    /// </summary>
    internal static AstDiag[] GetAllDiagnostics(Context ctx, Program program, SourceFile file)
    {
        var diags = new List<AstDiag>();
        var files = Concatenate([file], file.SupplementalSourceFiles());
        foreach (var sourceFile in files)
        {
            diags.AddRange(program.GetSyntacticDiagnostics(ctx, sourceFile));
            diags.AddRange(program.GetSemanticDiagnostics(ctx, sourceFile));
            diags.AddRange(program.GetSuggestionDiagnostics(ctx, sourceFile));
            if (program.Options().GetEmitDeclarations())
            {
                diags.AddRange(program.GetDeclarationDiagnostics(ctx, sourceFile));
            }
        }

        return diags.ToArray();
    }

    /// <summary>
    /// isSynthesizedContentMappedDiagnostic reports whether diag is a compiler diagnostic on a content-mapped
    /// file whose location lies entirely in synthesized virtual code with no counterpart in the original
    /// file, and so has no meaningful position to report against the original file.
    /// </summary>
    internal static bool IsSynthesizedContentMappedDiagnostic(AstDiag diag)
    {
        var file = diag.File();
        if (file is null || file.SpanMap() is null || diag.Source() != "")
        {
            return false;
        }

        var (_, fidelity) = file.SpanMap()!.VirtualToOriginalSpan(diag.Loc());
        return fidelity == Fidelity.None;
    }

    /// <summary>
    /// aggregateSynthesizedDiagnostics builds a single diagnostic at the top of a content-mapped file standing
    /// in for compiler diagnostics located in synthesized code with no original location. The originals are
    /// attached as related information so their messages are surfaced rather than silently dropped. (A later
    /// change will point the related locations at a read-only view of the file's virtual TypeScript.)
    /// </summary>
    internal static AstDiag AggregateSynthesizedDiagnostics(SourceFile file, AstDiag[] diags)
    {
        var aggregate = NewDiagnostic(
            file,
            NewTextRange(0, 0),
            Virtual_code_produced_by_the_content_mapper_0_has_problems_with_no_corresponding_location_in_this_file,
            file.ContentMapper());
        aggregate.SetRelatedInfo(diags);
        aggregate.SetCategory(WorstCategory(diags));
        return aggregate;
    }

    internal static Category WorstCategory(AstDiag[] diags)
    {
        var worst = diags[0].Category();
        foreach (var diag in diags)
        {
            switch (diag.Category())
            {
                case Category.Error:
                    return Category.Error;
                case Category.Warning:
                    worst = Category.Warning;
                    break;
            }
        }

        return worst;
    }
}

public sealed partial class LanguageService
{
    public (RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport Response, Exception? Error) ProvideDiagnostics(Context ctx, DocumentUri uri)
    {
        var (program, file) = GetProgramAndFile(uri);

        if (UserPreferences().EnableValidation.IsFalse())
        {
            var diagnostics = Array.Empty<LspDiag>();
            return (new RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport
            {
                FullDocumentDiagnosticReport = new RelatedFullDocumentDiagnosticReport
                {
                    Items = diagnostics,
                },
            }, null);
        }

        var diags = GetAllDiagnostics(ctx, program, file);

        return (new RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport
        {
            FullDocumentDiagnosticReport = new RelatedFullDocumentDiagnosticReport
            {
                Items = ToLspDiagnostics(ctx, diags),
            },
        }, null);
    }

    internal LspDiag?[] ToLspDiagnostics(Context ctx, params AstDiag[][] diagnostics)
    {
        var reportStyleChecksAsWarnings = UserPreferences().ReportStyleChecksAsWarnings.IsTrue();
        var size = 0;
        foreach (var diagSlice in diagnostics)
        {
            size += diagSlice.Length;
        }

        var lspDiagnostics = new List<LspDiag?>(size);
        // Compiler diagnostics located entirely in a content-mapped file's synthesized code have no location
        // in the original file. Collect them per file and surface them through a single aggregate at the top
        // of the file (with the real messages as related information) rather than dropping them or scattering
        // them at position 0.
        var synthesizedByFile = new OrderedMap<SourceFile, AstDiag[]?>();
        foreach (var diagSlice in diagnostics)
        {
            foreach (var diag in diagSlice)
            {
                if (IsSynthesizedContentMappedDiagnostic(diag))
                {
                    synthesizedByFile.Set(diag.File()!, Concatenate(synthesizedByFile.GetOrZero(diag.File()!), [diag]));
                    continue;
                }

                lspDiagnostics.Add(DiagnosticToLSPPull(ctx, _converters!, diag, reportStyleChecksAsWarnings));
            }
        }

        foreach (var (file, diags) in synthesizedByFile.Entries())
        {
            var aggregate = AggregateSynthesizedDiagnostics(file, diags!);
            lspDiagnostics.Add(DiagnosticToLSPPull(ctx, _converters!, aggregate, reportStyleChecksAsWarnings));
        }

        return lspDiagnostics.ToArray();
    }
}

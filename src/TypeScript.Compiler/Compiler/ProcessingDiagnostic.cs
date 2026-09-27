// ported from: internal/compiler/processingDiagnostic.go（135 行，全部）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出类型 → 顶层 `internal` 类型：`processingDiagnostic` → `ProcessingDiagnostic`、
//    `processingDiagnosticKind` → `ProcessingDiagnosticKind`（成员名去掉冗余前缀，与 `WriteKind`/`FileIncludeKind` 同款，
//    数值照 iota）、`includeExplainingDiagnostic` → `IncludeExplainingDiagnostic`。
// 2) 字段名沿用**兄弟片已落定的写法**（Program.Files.cs / IncludeProcessor.cs 已按这些名字构造它们，不能改）：
//    `ProcessingDiagnostic.Kind`/`Data`、`IncludeExplainingDiagnostic.File`/`DiagnosticReason`/`Message`/`Args`。
// 3) `d.data.(*T)`（Go 的类型断言，失败即 panic）→ C# 强制转换 `(T)Data!`（失败抛 InvalidCastException，同为 panic 语义）。
// 4) `panic(...)` → `Debug.Fail(...)` + `throw new System.Diagnostics.UnreachableException()`（同 FileInclude.cs 的写法）。
// 5) `strings.TrimSuffix(strings.TrimPrefix(libName, "lib."), ".d.ts")` → `StartsWith`/`EndsWith` +
//    切片（仓库既有等价写法：Compiler/FileLoader.cs 的 TrimLibPrefixAndDtsSuffix；C# 没有 strings.TrimPrefix 的移植物）。
// 6) `slices.Values(tsoptions.Libs)` → C# 的 `string[]` 直接当 `IEnumerable<string>` 用（`GetSpellingSuggestionForStrings`
//    的第二个参数）。
// 7) `collections.Set[*FileIncludeReason]` → `TypeScript.Collections.Set<FileIncludeReason>`（`AddIfAbsent`/`Len` 同名）。
//    Go 的零值 `var seenReasons collections.Set[…]` → `new Set<FileIncludeReason>()`。
// 8) 闭包（`processRelatedInfo`/`processInclude`）→ C# **局部函数**（同样捕获并就地修改外层局部变量；Go 里这两个
//    闭包就是这样改 `includeDetails`/`relatedInfo`/`preferredLocation` 的）。
//    `preferredLocation != includeReason` 是 Go 的**指针比较** → `!ReferenceEquals(...)`。
// 9) `append(list, x)` 到可能为 nil 的切片：C# 用 `List<T>?` + `??= []` + `Add`/`AddRange`，并保留 Go 的
//    "非 nil 才用"判定（`includeDetails`/`relatedInfo`/`chain` 的 nil 判定都是**语义**，不是空判断：
//    Go 的 `append(nil_slice)` 零元素时仍是 nil，故 `redirectInfo` 为空数组时 chain 必须保持 null）。
// 10) `make([]*ast.Diagnostic, 0, len(reasons))`（容量提示）→ `new List<Diagnostic>()`（容量与语义无关）。
//     Go 用 `make` 意味着**非 nil 空切片**（`diag.file != ""` 时 includeDetails 立刻不再是 nil）→ 见文件里
//     `includeDetails = []` 的位置与注释。
// 11) `diag.file != ""`（Go 的 tspath.Path 就是 string）→ `diag.File.Value is { Length: > 0 }`（`Path` 的零值是
//     `Value == null`；这里把 null 与 "" 都当"没有 file"，与 Go 的 `!= ""` 一致）。
// 12) `ast.NewCompilerDiagnostic` → `NewCompilerDiagnostic`（`using static TypeScript.Ast.Ast;`）；
//     诊断消息常量 → 裸名（`using static TypeScript.Diagnostics.Diagnostics;`，见 Checker/Jsdoc.cs 文件头的实测坑）。
namespace TypeScript.Compiler;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Diagnostics;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;

internal enum ProcessingDiagnosticKind
{
    UnknownReference = 0,
    ExplainingFileInclude = 1,
}

internal sealed class ProcessingDiagnostic
{
    internal ProcessingDiagnosticKind Kind;
    internal object? Data;

    internal FileIncludeReason AsFileIncludeReason()
    {
        return (FileIncludeReason)Data!;
    }

    internal IncludeExplainingDiagnostic AsIncludeExplainingDiagnostic()
    {
        return (IncludeExplainingDiagnostic)Data!;
    }

    internal Diagnostic ToDiagnostic(Program program)
    {
        switch (Kind)
        {
            case ProcessingDiagnosticKind.UnknownReference:
            {
                var reference = AsFileIncludeReason();
                var location = reference.GetReferencedLocation(program);
                switch (reference._kind)
                {
                    case FileIncludeKind.TypeReferenceDirective:
                        return location.DiagnosticAt(Cannot_find_type_definition_file_for_0, location._ref!.FileName);
                    case FileIncludeKind.LibReferenceDirective:
                        var libName = ToFileNameLowerCase(location._ref!.FileName);
                        // Go: strings.TrimSuffix(strings.TrimPrefix(libName, "lib."), ".d.ts")
                        var unqualifiedLibName = libName.StartsWith("lib.", StringComparison.Ordinal) ? libName["lib.".Length..] : libName;
                        if (unqualifiedLibName.EndsWith(".d.ts", StringComparison.Ordinal))
                        {
                            unqualifiedLibName = unqualifiedLibName[..^".d.ts".Length];
                        }

                        var suggestion = GetSpellingSuggestionForStrings(unqualifiedLibName, TypeScript.TsOptions.TsOptions.Libs);
                        return location.DiagnosticAt(
                            IfElse(
                                suggestion != "",
                                Cannot_find_lib_definition_for_0_Did_you_mean_1,
                                Cannot_find_lib_definition_for_0),
                            libName,
                            suggestion);
                    default:
                        // Go: panic("unknown include kind")
                        Debug.Fail("unknown include kind");
                        throw new System.Diagnostics.UnreachableException();
                }
            }

            case ProcessingDiagnosticKind.ExplainingFileInclude:
                return CreateDiagnosticExplainingFile(program);
            default:
                // Go: panic("unknown processingDiagnosticKind")
                Debug.Fail("unknown processingDiagnosticKind");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    internal Diagnostic CreateDiagnosticExplainingFile(Program program)
    {
        var diag = AsIncludeExplainingDiagnostic();
        List<Diagnostic>? includeDetails = null;
        List<Diagnostic>? relatedInfo = null;
        Diagnostic[]? redirectInfo = null;
        FileIncludeReason? preferredLocation = null;
        var seenReasons = new Set<FileIncludeReason>();
        if (diag.DiagnosticReason is not null && diag.DiagnosticReason.IsReferencedFile() && !program._includeProcessor.GetReferenceLocation(diag.DiagnosticReason, program).IsSynthetic)
        {
            preferredLocation = diag.DiagnosticReason;
        }

        void ProcessRelatedInfo(FileIncludeReason includeReason)
        {
            if (preferredLocation is null && includeReason.IsReferencedFile() && !program._includeProcessor.GetReferenceLocation(includeReason, program).IsSynthetic)
            {
                preferredLocation = includeReason;
            }
            else if (!ReferenceEquals(preferredLocation, includeReason))
            {
                var info = program._includeProcessor.GetRelatedInfo(includeReason, program);
                if (info is not null)
                {
                    relatedInfo ??= [];
                    relatedInfo.Add(info);
                }
            }
        }

        void ProcessInclude(FileIncludeReason includeReason)
        {
            if (!seenReasons.AddIfAbsent(includeReason))
            {
                return;
            }

            includeDetails ??= [];
            includeDetails.Add(includeReason.ToDiagnostic(program, false));
            ProcessRelatedInfo(includeReason);
        }

        // !!! todo sheetal caching

        if (diag.File.Value is { Length: > 0 })
        {
            // Go: reasons := program.includeProcessor.fileIncludeReasons[diag.file]（map 缺失键 → nil 切片）
            var reasons = program._includeProcessor._fileIncludeReasons.GetValueOrDefault(diag.File);
            // Go: includeDetails = make([]*ast.Diagnostic, 0, len(reasons)) —— **非 nil 空切片**（见文件头第 10 条）。
            includeDetails = [];
            if (reasons is not null)
            {
                foreach (var reason in reasons)
                {
                    ProcessInclude(reason);
                }
            }

            redirectInfo = program._includeProcessor.ExplainRedirectAndImpliedFormat(program, diag.File, static fileName => fileName);
        }

        if (diag.DiagnosticReason is not null)
        {
            ProcessInclude(diag.DiagnosticReason);
        }

        List<Diagnostic>? chain = null;
        if (includeDetails is not null && (preferredLocation is null || seenReasons.Len() != 1))
        {
            var fileReason = NewCompilerDiagnostic(The_file_is_in_the_program_because_Colon);
            fileReason.SetMessageChain([.. includeDetails]);
            chain = [fileReason];
        }

        // Go: chain = append(chain, redirectInfo...) —— 空 append 保持 nil（见文件头第 9 条）。
        if (redirectInfo is not null && redirectInfo.Length > 0)
        {
            chain ??= [];
            chain.AddRange(redirectInfo);
        }

        Diagnostic? result = null;
        if (preferredLocation is not null)
        {
            result = program._includeProcessor.GetReferenceLocation(preferredLocation, program).DiagnosticAt(diag.Message, diag.Args);
        }

        result ??= NewCompilerDiagnostic(diag.Message, diag.Args);
        if (chain is not null)
        {
            result.SetMessageChain([.. chain]);
        }

        if (relatedInfo is not null)
        {
            result.SetRelatedInfo([.. relatedInfo]);
        }

        return result;
    }
}

internal sealed class IncludeExplainingDiagnostic
{
    internal Path File;
    internal FileIncludeReason? DiagnosticReason;
    internal Message Message = null!;
    internal object?[] Args = [];
}

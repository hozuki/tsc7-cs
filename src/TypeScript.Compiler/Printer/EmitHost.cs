// ported from: internal/printer/emithost.go
//
// Go 的 `WriteFile(fileName string, text string) error`：C# 按 docs §3.2 落成 void（错误路径抛异常）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Printer;

// NOTE: EmitHost operations must be thread-safe
public interface EmitHost
{
    CompilerOptions Options();

    SourceFile[] SourceFiles();

    bool UseCaseSensitiveFileNames();

    string GetCurrentDirectory();

    string CommonSourceDirectory();

    bool IsEmitBlocked(string file);

    void WriteFile(string fileName, string text);

    ModuleKind GetEmitModuleFormatOfFile(HasFileName file);

    EmitResolver GetEmitResolver();

    SourceOutputAndProjectReference? GetProjectReferenceFromSource(Path path);

    bool IsSourceFileFromExternalLibrary(SourceFile file);
}

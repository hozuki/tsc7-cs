// ported from: internal/transformers/declarations/supplementalreferences.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 未导出字段 → `internal _camelCase`。
// 2) Go 的 `sourceFile.ReferencedFiles = append(..., &ast.FileReference{...})` → C# `Core.Concatenate`
//    （`FileReference[]`，Go 的 nil slice ↔ C# null，见 docs/architecture.md §3.2）。
// 3) `tspath.GetRelativePathFromFile` 的 `tspath.ComparePathsOptions{CurrentDirectory, UseCaseSensitiveFileNames}`
//    命名参数 → C# 构造器 `new ComparePathsOptions(useCaseSensitiveFileNames, currentDirectory)`。
namespace TypeScript.Transformers.Declarations;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Transformers.Declarations.Declarations;

/// <summary>
/// SupplementalReferencesTransformer adds triple-slash path references from a content mapper's
/// canonical declaration output to the declaration files emitted for its supplemental files.
/// This ensures that consumers loading the canonical declaration also include the supplemental types.
/// </summary>
public sealed class SupplementalReferencesTransformer
{
    internal DeclarationEmitHost _host = null!;
    internal SourceFile[] _supplementalFiles = [];
    internal string _declarationFilePath = "";
    internal bool _forceDeclarationPaths;

    public SourceFile TransformSourceFile(SourceFile sourceFile)
    {
        foreach (var supplemental in _supplementalFiles)
        {
            if (!_host.SourceFileMayBeEmitted(supplemental, _forceDeclarationPaths))
            {
                continue;
            }
            var declarationPath = _host.GetOutputPathsFor(supplemental, _forceDeclarationPaths).DeclarationFilePath();
            if (declarationPath == "")
            {
                continue;
            }
            sourceFile.ReferencedFiles = Concatenate(sourceFile.ReferencedFiles, [new FileReference
            {
                Range = NewTextRange(-1, -1),
                FileName = GetRelativePathFromFile(
                    _declarationFilePath,
                    declarationPath,
                    new ComparePathsOptions(_host.UseCaseSensitiveFileNames(), _host.GetCurrentDirectory())
                ),
            }]);
        }
        return sourceFile;
    }

    public Diagnostic[] GetDiagnostics()
    {
        return [];
    }
}

public static partial class Declarations
{
    public static SupplementalReferencesTransformer NewSupplementalReferencesTransformer(DeclarationEmitHost host, SourceFile sourceFile, string declarationFilePath, bool forceDeclarationPaths)
    {
        return new SupplementalReferencesTransformer
        {
            _host = host,
            _supplementalFiles = sourceFile.SupplementalSourceFiles(),
            _declarationFilePath = declarationFilePath,
            _forceDeclarationPaths = forceDeclarationPaths,
        };
    }
}

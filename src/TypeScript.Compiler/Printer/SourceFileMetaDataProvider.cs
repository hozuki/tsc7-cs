// ported from: internal/printer/sourcefilemetadataprovider.go
using TypeScript.Ast;
using TypeScript.TSPath;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Printer;

/// <summary>Go 的 <c>type SourceFileMetaDataProvider interface</c>。</summary>
public interface SourceFileMetaDataProvider
{
    SourceFileMetaData? GetSourceFileMetaData(Path path);
}

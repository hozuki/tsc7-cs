// ported from: internal/ls/source_map.go:1-167
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的未导出 struct `script` → `internal sealed class Script : LsConv.Script`（Go 里它只是**结构上**满足
//    `lsconv.Script`，C# 没有结构满足 ⇒ 显式挂接口，同 LsConv/Script.cs 对 `ast.SourceFile` 的做法）。
//    类型名与 `LsConv.Script`（接口）同名：本文件里裸名 `Script` 是**本文件的实现类**，接口一律写 `LsConv.Script`。
//    `SpanMap() *spanmap.SpanMap` 恒为 nil ⇒ C# 返回 `SpanMapType?`（别名指向 `TypeScript.SpanMap.SpanMap`，
//    避免与命名空间 `TypeScript.SpanMap` 撞名，先例 LsConv/Script.cs 文件头第 3 条）。
//
// 2) `LanguageService` 的成员一律 PascalCase（含 Go 的未导出方法）：与**已落地**的引导文件
//    `Ls/LanguageService.cs` 的偏离清单第 1 条（`toPath` → `ToPath`）保持一致 —— 那是本仓已定型的
//    LanguageService 命名面，后 9 组都在其上追加成员，不再区分 Go 的导出/未导出。
//
// 3) Go 的 `*sourcemap.DocumentPositionMapper` 允许 nil 接收者（`GetSourcePosition`/`GetGeneratedPosition`
//    开头就 `if d == nil { return nil }`，见 sourcemap/source_mapper.go:169-171,204-206），而 C# 的实例方法不能在
//    null 上调用 ⇒ C# 侧 `GetDocumentPositionMapper` 返回可空（SourceMapper.cs:432），调用点写 `positionMapper?.GetXxx(...)`
//    （正是 Go 的 nil 接收者语义）。
//
// 4) `outputpaths.GetOutputDeclarationFileNameWorker(fileName, program.Options(), program)` 的第三个参数在 Go 里是
//    结构满足的 `outputpaths.OutputPathsHost`，C# 需要显式接口声明 ⇒ 见本文件末尾的
//    `Program : TypeScript.OutputPaths.OutputPathsHost`（先例：ModuleSpecifiers/Types.cs:100 的
//    `ModuleSpecifierGenerationHost : OutputPathsHost`、LsConv/Script.cs 的 `partial class SourceFile : Script`）。
//
// 5) Go 的 `l.host.ReadFile(fileName)` → `_host.TryReadFile`（见 Ls/Host.cs 文件头第 1 条）。
//    `l.getScript(fileName)` 在 Go 里可能返回 nil，而调用点（`createLspRangeFromRange`）会把 nil 当 Script 用
//    （Go 会在 nil 接收者上 panic）⇒ C# 侧同样按"调用方保证文件存在"处理，写 `GetScript(...)!`。
//    同理 `program` 在 `tryGetGeneratedPositionWorker` 里 Go 已显式判空（`program == nil || ...`）。
//
// 6) Go 的 `core.TextPos(x)` 显式转换在 C# 里由 `Core.TextPos` 的隐式转换（Core/Text.cs:34,36）承接，
//    所以 `TryGetSourcePosition(fileName, fileRange.Pos())` 与 Go 逐字对应。
//
// 7) **本文件用块作用域命名空间**：文件里要放两个命名空间（`TypeScript.Ls` 的 LanguageService/Script +
//    `TypeScript.Compiler` 的 Program 挂接口），C# 不允许文件作用域与块作用域混用（先例 LsConv/Script.cs）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.SourceMap;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.Ls.Ls;
using static TypeScript.LsConv.LsConv;
using static TypeScript.OutputPaths.OutputPaths;
using static TypeScript.TSPath.TSPath;
using SpanMapType = TypeScript.SpanMap.SpanMap;

namespace TypeScript.Ls
{
    public sealed partial class LanguageService
    {
        /// <summary>
        /// sourceFileRangeToLSPLocation maps a range from an arbitrary program SourceFile to an LSP location,
        /// composing content-mapper span maps and declaration source maps as needed. LS features should use this
        /// for cross-file results instead of calling getMappedLocation or lsconv.ToLSPLocation directly.
        /// This unfiltered form is appropriate for diagnostics and text edits.
        /// </summary>
        internal (Location Location, Fidelity Fidelity) SourceFileRangeToLSPLocation(SourceFile file, TextRange fileRange)
        {
            if (file.ContentMapper() != "")
            {
                return _converters!.ToLSPLocation(file, fileRange);
            }

            return GetMappedLocation(file.FileName(), fileRange);
        }

        /// <summary>
        /// sourceFileRangeToLSPLocationForFeature is the preferred conversion for visible LS results that may
        /// come from another file. It applies content-mapper feature filtering and follows declaration source maps.
        /// Do not use it for diagnostics or text edits.
        /// </summary>
        internal (Location Location, Fidelity Fidelity) SourceFileRangeToLSPLocationForFeature(SourceFile file, TextRange fileRange, Feature feature)
        {
            if (file.ContentMapper() != "")
            {
                return _converters!.ToLSPLocationForFeature(file, fileRange, feature);
            }

            return GetMappedLocation(file.FileName(), fileRange);
        }

        /// <summary>
        /// getMappedLocation follows declaration source maps from a .d.ts range to its source location.
        /// It is an implementation detail of sourceFileRangeToLSPLocation; LS features should not call it directly,
        /// because it does not preserve a content-mapper projection or apply span-map feature filtering.
        /// </summary>
        internal (Location Location, Fidelity Fidelity) GetMappedLocation(string fileName, TextRange fileRange)
        {
            var startPos = TryGetSourcePosition(fileName, fileRange.Pos());
            if (startPos is null)
            {
                var (lspRange, fidelity) = CreateLspRangeFromRange(fileRange, GetScript(fileName)!);
                return (new Location
                {
                    Uri = FileNameToDocumentURI(fileName),
                    Range = lspRange,
                }, fidelity);
            }

            var endPos = TryGetSourcePosition(fileName, fileRange.End());
            if (endPos is null || endPos.FileName != startPos.FileName || endPos.Pos < startPos.Pos)
            {
                // When end doesn't map, maps to a different source file (e.g. in a .d.ts with a
                // multi-source source map from --outFile compilation), or maps to a position before
                // start (non-monotonic source map mappings), approximate the end position.
                endPos = new DocumentPosition
                {
                    FileName = startPos.FileName,
                    Pos = startPos.Pos + fileRange.Len(),
                };
            }

            var newRange = NewTextRange(startPos.Pos, endPos.Pos);
            var (mappedRange, mappedFidelity) = CreateLspRangeFromRange(newRange, GetScript(startPos.FileName)!);
            return (new Location
            {
                Uri = FileNameToDocumentURI(startPos.FileName),
                Range = mappedRange,
            }, mappedFidelity);
        }

        internal Script? GetScript(string fileName)
        {
            if (!_host.TryReadFile(fileName, out var text))
            {
                return null;
            }

            return new Script(fileName, text);
        }

        internal DocumentPosition? TryGetSourcePosition(
            string fileName,
            TextPos position)
        {
            var newPos = TryGetSourcePositionWorker(fileName, position);
            if (newPos is not null)
            {
                if (!TryReadFile(newPos.FileName, out _)) // File doesn't exist
                {
                    return null;
                }
            }

            return newPos;
        }

        internal DocumentPosition? TryGetSourcePositionWorker(
            string fileName,
            TextPos position)
        {
            if (!IsDeclarationFileName(fileName))
            {
                return null;
            }

            var positionMapper = GetDocumentPositionMapper(fileName);
            var documentPos = positionMapper?.GetSourcePosition(new DocumentPosition { FileName = fileName, Pos = position });
            if (documentPos is null)
            {
                return null;
            }

            var newPos = TryGetSourcePositionWorker(documentPos.FileName, documentPos.Pos);
            if (newPos is not null)
            {
                return newPos;
            }

            return documentPos;
        }

        internal DocumentPosition? TryGetGeneratedPosition(
            string fileName,
            TextPos position)
        {
            var newPos = TryGetGeneratedPositionWorker(fileName, position);
            if (newPos is not null)
            {
                if (!TryReadFile(newPos.FileName, out _)) // File doesn't exist
                {
                    return null;
                }
            }

            return newPos;
        }

        internal DocumentPosition? TryGetGeneratedPositionWorker(
            string fileName,
            TextPos position)
        {
            if (IsDeclarationFileName(fileName))
            {
                return null;
            }

            var program = GetProgram();
            if (program is null || program.GetSourceFile(fileName) is null)
            {
                return null;
            }

            var path = ToPath(fileName);
            // If this is source file of project reference source (instead of redirect) there is no generated position
            if (program.IsSourceFromProjectReference(path))
            {
                return null;
            }

            var declarationFileName = GetOutputDeclarationFileNameWorker(fileName, program.Options(), program);
            var positionMapper = GetDocumentPositionMapper(declarationFileName);
            var documentPos = positionMapper?.GetGeneratedPosition(new DocumentPosition { FileName = fileName, Pos = position });
            if (documentPos is null)
            {
                return null;
            }

            var newPos = TryGetGeneratedPositionWorker(documentPos.FileName, documentPos.Pos);
            if (newPos is not null)
            {
                return newPos;
            }

            return documentPos;
        }
    }

    /// <summary>Go 的未导出 struct <c>script</c>（source_map.go:74-90，见文件头第 1 条）。</summary>
    internal sealed class Script(string fileName, string text) : LsConv.Script
    {
        private readonly string _fileName = fileName;
        private readonly string _text = text;

        public string FileName()
        {
            return _fileName;
        }

        public string OriginalFileName() => _fileName;

        public string Text()
        {
            return _text;
        }

        public string OriginalText() => _text;

        public SpanMapType? SpanMap() => null;
    }
}

namespace TypeScript.Compiler
{
    /// <summary>
    /// Go: `*compiler.Program` 结构化满足 `outputpaths.OutputPathsHost`（见 Ls/SourceMap.cs 文件头第 4 条）。
    /// `Program` 已有 `CommonSourceDirectory()`/`ContentMapperExtensions()`/`GetCurrentDirectory()`/`UseCaseSensitiveFileNames()`
    /// 四个 public 成员 ⇒ 这里只需显式挂上接口（隐式实现）。
    /// </summary>
    public sealed partial class Program : TypeScript.OutputPaths.OutputPathsHost
    {
    }
}

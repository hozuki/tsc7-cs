// ported from: internal/ls/selectionranges_test.go:1-58
//
// 与 Go 测试的差异（逐条）：
//
// 1) Go 的 `&LanguageService{converters: …}`（只填一个字段的复合字面量）在 C# 里不可行 —— `LanguageService` 的构造器是
//    私有的，唯一的公开入口是 `LanguageService.NewLanguageService(projectID, program, host, activeFile)`（它会向 host
//    要 converters 与 preferences）⇒ 本测试提供一个最小 `Host`/`ProjectID` 测试替身，**program 传 null**（Go 的测试也
//    只填了 converters，program/host 都是零值）。
// 2) Go 用普通 goroutine（栈可增长）解析 12,000 层嵌套括号；.NET 的线程栈固定且 StackOverflowException 不可捕获
//    （docs/architecture.md §3.9）⇒ 整段包在 `TscTestSystem.RunOnBigStackThread`（64 MiB 大栈线程，仓库既有做法）。
// 3) `result.Range != innerRange` 的 Go 结构体 `==` → C# 用 `LsProto.CompareRanges(a, b) == 0`（`Range` 是 class）。
// 4) `getSmartSelectionRange` / `maxSelectionRangeDepth` → `TypeScript.Ls.Ls.GetSmartSelectionRange` / `TypeScript.Ls.Ls.MaxSelectionRangeDepth`
//    （internal；测试项目有 InternalsVisibleTo，见 src/TypeScript.Compiler/Properties/AssemblyInfo.cs）。
// 5) `json.Marshal(message)`：C# 侧直接marshal `ResponseMessage`（它自带 MarshalJSONTo，与 Go 的 `Message()` 载荷同形）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.JsonRpc;
using static TypeScript.Json.Json;
using TypeScript.Ls;
using TypeScript.LsAutoImport;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.SourceMap;
using Path = TypeScript.TSPath.Path;
using Xunit;
using static TypeScript.LsProto.LsProto;
using static TypeScript.Parser.Parser;

namespace TypeScript.Tests;

public class SelectionRangesTests
{
    private const int _nestingDepth = 12000;

    /// <summary>Go 的 `projectID autoimport.ProjectID` 测试替身（只用于构造 LanguageService）。</summary>
    private sealed class StubProjectID : ProjectID
    {
        public string String() => "selection-ranges-tests";
    }

    /// <summary>Go 测试里的零值 host（只需要 Converter 与 preferences）。</summary>
    private sealed class StubHost(LSPLineMap lineMap) : TypeScript.Ls.Host
    {
        public bool UseCaseSensitiveFileNames() => true;

        public bool TryReadFile(string path, out string contents)
        {
            contents = "";
            return false;
        }

        public Converters Converters() => LsConv.LsConv.NewConverters(PositionEncodingKind.UTF16, _ => lineMap);

        public UserPreferences GetPreferences(string activeFile) => new();

        public ECMALineInfo? GetECMALineInfo(string fileName) => null;

        public Registry AutoImportRegistry() => null!;

        public string[] ReadDirectory(string currentDir, string path, string[]? extensions, string[]? excludes, string[]? includes, int depth) => [];

        public string[] GetDirectories(string path) => [];

        public bool DirectoryExists(string path) => false;

        public bool FileExists(string path) => false;
    }

    [Fact]
    public void SelectionRangeDepthIsLimited()
    {
        var text = "const x = " + new string('(', _nestingDepth) + "1" + new string(')', _nestingDepth) + ";";
        var lineMap = LsConv.LsConv.ComputeLSPLineStarts(text);

        TscTestSystem.RunOnBigStackThread(() =>
        {
            var sourceFile = ParseSourceFile(
                new SourceFileParseOptions { FileName = "/index.ts", Path = new Path("/index.ts") },
                text,
                ScriptKind.TS);
            var host = new StubHost(lineMap);
            var languageService = LanguageService.NewLanguageService(new StubProjectID(), null, host, "/index.ts");

            var position = "const x = ".Length + _nestingDepth;
            var result = TypeScript.Ls.Ls.GetSmartSelectionRange(languageService, sourceFile, position);
            Assert.NotNull(result);

            var depth = 0;
            SelectionRange? outermost = null;
            for (var current = result; current is not null; current = current.Parent)
            {
                depth++;
                outermost = current;
            }

            Assert.Equal(TypeScript.Ls.Ls.MaxSelectionRangeDepth, depth);

            var (innerRange, _) = languageService._converters!.ToLSPRange(sourceFile, new TextRange(position, position + 1));
            Assert.Equal(0, CompareRanges(result!.Range, innerRange));

            var (fullRange, _) = languageService._converters!.ToLSPRange(sourceFile, new TextRange(sourceFile.Pos(), sourceFile.End()));
            Assert.Equal(0, CompareRanges(outermost!.Range, fullRange));

            var response = new SelectionRangesOrNull { SelectionRanges = [result] };
            var message = new TypeScript.LsProto.ResponseMessage { ID = ID.NewIDString("selectionRange"), Result = response };
            var json = Marshal(message);
            Assert.True(json.Length > 0, "the limited selection range serializes to a non-empty JSON payload");
        });
    }

    /// <summary>
    /// **本移植新增的防御性边界用例**（Go 侧没有对应断言）：`Json/Encoder.cs` 的深度上限从 .NET 默认的 1000
    /// 提到 10000（见那里的实测说明）。这里证明放宽不是"无限放宽" —— 远超上限的嵌套响应必须失败。
    /// 一条 <see cref="SelectionRange"/> 在 JSON 里展开成两个嵌套对象（<c>range</c> 与 <c>parent</c>），
    /// 所以取 <c>MaxNestingDepth</c> 层链 ≈ 2×MaxNestingDepth 层对象，必然越界。
    /// </summary>
    [Fact]
    public void SelectionRangeSerializationDepthIsBounded()
    {
        // 编码是按嵌套层递归的 ⇒ 必须在大栈线程上跑（否则超限前就先 StackOverflowException，而它不可捕获）。
        TscTestSystem.RunOnBigStackThread(() =>
        {
            SelectionRange? chain = null;
            for (var i = 0; i < TypeScript.Json.Encoder.MaxNestingDepth + 10; i++)
            {
                chain = new SelectionRange { Range = new TypeScript.LsProto.Range { Start = new Position(), End = new Position() }, Parent = chain };
            }

            var response = new SelectionRangesOrNull { SelectionRanges = [chain] };
            var message = new TypeScript.LsProto.ResponseMessage { ID = ID.NewIDString("tooDeep"), Result = response };
            Assert.ThrowsAny<Exception>(() => Marshal(message));
        });
    }
}

// ported from: internal/testutil/tsbaseline/sourcemap_baseline.go
//
// 与 Go 的差异（逐条）：
// 1) `*testing.T` → `IHarnessTest`；`t.Fatal` 抛 `TestFailureException`。
// 2) `json.Unmarshal([]byte(content), &v)`（v 是零值结构体）→ `Json.UnmarshalNew<RawSourceMap>(UTF8(content))`。
// 3) `base64EncodeChunk` 里 `url.QueryEscape` 紧接着 `url.QueryUnescape` 是**恒等变换**（QueryEscape 只产出
//    `%XX` 与 `+`，QueryUnescape 是它的逆；输入里没有非法转义，所以不会走到 err 分支）。因此这里直接
//    `Convert.ToBase64String(UTF8(s))` —— 与 Go 的 `base64.StdEncoding.EncodeToString([]byte(s))` 逐字节相同，
//    且省掉两次无意义的编解码（Go 侧那两步是历史写法）。注意 `base64.StdEncoding` 与 `Convert.ToBase64String`
//    都是"标准字母表 + `=` 填充、不换行"。
// 4) `slices.Contains(sourceTDs, nil)` → 手写循环判 null（repo 里不用 LINQ）。
// 5) `strings.HasSuffix(x, y)` → `x.EndsWith(y, StringComparison.Ordinal)`。

namespace TypeScript.TestUtil.TsBaseline;

using System.Text;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.SourceMap;
using TypeScript.TestUtil.Baseline;
using TypeScript.TestUtil.HarnessUtil;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

public static partial class TsBaseline
{
    /// <summary>Go: <c>func DoSourcemapBaseline(...)</c>。</summary>
    public static void DoSourcemapBaseline(
        IHarnessTest t,
        string baselinePath,
        string header,
        CompilerOptions options,
        CompilationResult result,
        HarnessOptions harnessSettings,
        BaselineOptions opts)
    {
        var declMaps = options.GetAreDeclarationMapsEnabled();
        if (options.InlineSourceMap.IsTrue())
        {
            if (result.Maps.Size() > 0 && !declMaps)
            {
                t.Fatal("No sourcemap files should be generated if inlineSourceMaps was set.");
            }

            return;
        }
        else if (options.SourceMap.IsTrue() || declMaps)
        {
            var expectedMapCount = 0;
            if (options.SourceMap.IsTrue())
            {
                expectedMapCount += result.GetNumberOfJSFiles( /*includeJSON*/ false);
            }

            if (declMaps)
            {
                expectedMapCount += result.DTS.Size();
            }

            if (result.Maps.Size() != expectedMapCount)
            {
                t.Fatal("Number of sourcemap files should be same as js files.");
            }

            string sourceMapCode;
            if ((options.NoEmitOnError.IsTrue() && result.Diagnostics.Length != 0) || result.Maps.Size() == 0)
            {
                sourceMapCode = Baseline.NoContent;
            }
            else
            {
                var sourceMapCodeBuilder = new StringBuilder();
                foreach (var sourceMapOrNull in result.Maps.Values())
                {
                    var sourceMap = sourceMapOrNull!;
                    if (sourceMapCodeBuilder.Length > 0)
                    {
                        sourceMapCodeBuilder.Append("\r\n");
                    }

                    sourceMapCodeBuilder.Append(FileOutput(sourceMap, harnessSettings));
                    if (!options.InlineSourceMap.IsTrue())
                    {
                        sourceMapCodeBuilder.Append(CreateSourceMapPreviewLink(sourceMap, result));
                    }
                }

                sourceMapCode = sourceMapCodeBuilder.ToString();
            }

            if (FileExtensionIsOneOf(baselinePath, [ExtensionTs, ExtensionTsx]))
            {
                baselinePath = ChangeExtension(baselinePath, ExtensionJs + ".map");
            }

            Baseline.Run(t, baselinePath, sourceMapCode, opts);
        }
    }

    /// <summary>Go: <c>func createSourceMapPreviewLink(sourceMap *harnessutil.TestFile, result *harnessutil.CompilationResult) string</c>。</summary>
    private static string CreateSourceMapPreviewLink(TestFile sourceMap, CompilationResult result)
    {
        // Go: var sourcemapJSON sourcemap.RawSourceMap; json.Unmarshal([]byte(sourceMap.Content), &sourcemapJSON)
        var sourcemapJSON = Json.UnmarshalNew<RawSourceMap>(Encoding.UTF8.GetBytes(sourceMap.Content));

        var outputJSFile = Find(result.Outputs(), td => td.UnitName.EndsWith(sourcemapJSON.File, StringComparison.Ordinal));

        // !!! Strada uses a fallible approach to associating inputs and outputs derived from a source map output. The
        // !!! commented logic below should be used after the Strada migration is complete:

        ////inputsAndOutputs := result.GetInputsAndOutputsForFile(sourceMap.UnitName)
        ////outputJSFile := inputsAndOutputs.Js

        if (outputJSFile is null)
        {
            return "";
        }

        ////if len(sourcemapJSON.Sources) == len(inputsAndOutputs.Inputs) {
        ////	sourceTDs = inputsAndOutputs.Inputs
        ////} else {
        var sourceTDs = Map<string, TestFile?>(sourcemapJSON.Sources ?? [], s =>
        {
            var sourceFile = Find(result.Inputs(), td => td.UnitName.EndsWith(s, StringComparison.Ordinal));
            if (sourceFile is not null)
            {
                if (result.Program.GetSourceFile(sourceFile.UnitName) is { } programSource)
                {
                    return new TestFile { UnitName = sourceFile.UnitName, Content = programSource.OriginalText() };
                }
            }

            return sourceFile;
        });
        foreach (var td in sourceTDs)
        {
            if (td is null)
            {
                return "";
            }
        }

        ////}

        var hash = new StringBuilder();
        hash.Append("\n//// https://sokra.github.io/source-map-visualization#base64,");
        hash.Append(Base64EncodeChunk(outputJSFile.Content));
        hash.Append(',');
        hash.Append(Base64EncodeChunk(sourceMap.Content));
        foreach (var td in sourceTDs)
        {
            hash.Append(',');
            hash.Append(Base64EncodeChunk(td!.Content));
        }

        hash.Append('\n');
        return hash.ToString();
    }

    /// <summary>Go: <c>func base64EncodeChunk(s string) string</c>（见文件头第 3 条）。</summary>
    private static string Base64EncodeChunk(string s)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    }
}

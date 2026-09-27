// ported from: internal/testutil/tsbaseline/js_emit_baseline.go
//
// 与 Go 的差异（逐条；行尾/字节规则见 docs/harness.md §4）：
// 1) `*testing.T` → `IHarnessTest`。`t.Fatal` 抛 `TestFailureException`。
// 2) **行尾是混合的**：harness 自己拼的分隔行一律 `\r\n`（`tsCode`/`jsCode` 里所有字面量换行都保留为 `\r\n`），
//    而 `file.Content`（源码照抄的行）原样写入、不加不改 —— 这正是 reference baseline 的现状。
// 3) `result.Program.Program()`（Go 的 ProgramLike → *Program）在 C# 里是显式向下转型（见 `ProgramOf`）。
// 4) `outputpaths.ChangeToDeclarationExtension(path, program)` 在 Go 靠**结构满足**把 *Program 当 OutputPathsHost 用；
//    C# 没有结构满足，所以这里包一层 `ProgramOutputPathsHost`（四个方法逐一转发到 Program，行为与 Go 一致）。
// 5) `strings.Replace(s, old, new, 1)` → `ReplaceFirst`（只替换第一处）。
// 6) `append(a, b...)` / `slices.Concat` → 集合表达式 `[.. a, .. b]`。
// 7) Go 的 `for x := range map.Values()`（`OrderedMap.Values()` 在 C# 返回 `IEnumerable<V?>`）→ `x!`。

namespace TypeScript.TestUtil.TsBaseline;

using System.Text;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.OutputPaths;
using TypeScript.TestUtil.Baseline;
using TypeScript.TestUtil.HarnessUtil;
using static TypeScript.DiagnosticWriter.DiagnosticWriter;
using TestConfiguration = System.Collections.Generic.Dictionary<string, string>;
using TypeScript.TsOptions;
using static TypeScript.Core.Core;
using static TypeScript.Parser.Parser;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;
using Program = TypeScript.Compiler.Program;

public static partial class TsBaseline
{
    /// <summary>Go: <c>func DoJSEmitBaseline(...)</c>。</summary>
    public static void DoJSEmitBaseline(
        IHarnessTest t,
        string baselinePath,
        string header,
        CompilerOptions options,
        CompilationResult result,
        TestFile[] tsConfigFiles,
        TestFile[] toBeCompiled,
        TestFile[] otherFiles,
        HarnessOptions harnessSettings,
        BaselineOptions opts)
    {
        if (!options.NoEmit.IsTrue() && !options.EmitDeclarationOnly.IsTrue() && result.JS.Size() == 0 && result.Diagnostics.Length == 0)
        {
            t.Fatal("Expected at least one js file to be emitted or at least one error to be created.");
        }

        // check js output
        var tsCode = new StringBuilder();
        var tsSources = Concatenate(otherFiles, toBeCompiled);
        tsCode.Append("//// [");
        tsCode.Append(header);
        tsCode.Append("] ////\r\n\r\n");

        for (var i = 0; i < tsSources.Length; i++)
        {
            var file = tsSources[i];
            tsCode.Append("//// [");
            tsCode.Append(GetBaseFileName(file.UnitName));
            tsCode.Append("]\r\n");
            tsCode.Append(file.Content);
            if (i < tsSources.Length - 1)
            {
                tsCode.Append("\r\n");
            }
        }

        var jsCode = new StringBuilder();
        foreach (var fileOrNull in result.JS.Values())
        {
            var file = fileOrNull!;
            if (jsCode.Length > 0 && !jsCode.ToString().EndsWith('\n'))
            {
                jsCode.Append("\r\n");
            }

            if (result.Diagnostics.Length == 0 && file.UnitName.EndsWith(ExtensionJson, StringComparison.Ordinal))
            {
                var fileParseResult = ParseSourceFile(
                    new SourceFileParseOptions
                    {
                        FileName = file.UnitName,
                        Path = (Path)file.UnitName,
                    },
                    file.Content,
                    ScriptKind.JSON);
                if (fileParseResult.Diagnostics().Length > 0)
                {
                    jsCode.Append(GetErrorBaseline(
                        t,
                        [file],
                        WrapASTDiagnostics(fileParseResult.Diagnostics()),
                        CompareASTDiagnostics,
                        false /*pretty*/));
                    continue;
                }
            }

            jsCode.Append(FileOutput(file, harnessSettings));
        }

        if (result.DTS.Size() > 0)
        {
            jsCode.Append("\r\n\r\n");
            foreach (var declFileOrNull in result.DTS.Values())
            {
                jsCode.Append(FileOutput(declFileOrNull!, harnessSettings));
            }
        }

        var declFileContext = PrepareDeclarationCompilationContext(
            toBeCompiled,
            otherFiles,
            result,
            harnessSettings,
            options,
            "" /*currentDirectory*/);
        var declFileCompilationResult = CompileDeclarationFiles(t, declFileContext, result.Symlinks);

        if (declFileCompilationResult is not null && declFileCompilationResult.DeclResult.Diagnostics.Length > 0)
        {
            jsCode.Append("\r\n\r\n//// [DtsFileErrors]\r\n");
            jsCode.Append("\r\n\r\n");
            jsCode.Append(GetErrorBaseline(
                t,
                [.. tsConfigFiles, .. declFileCompilationResult.DeclInputFiles, .. declFileCompilationResult.DeclOtherFiles],
                WrapASTDiagnostics(declFileCompilationResult.DeclResult.Diagnostics),
                CompareASTDiagnostics,
                false /*pretty*/));
        }

        if (!options.NoCheck.IsTrue() && !options.NoEmit.IsTrue())
        {
            var testConfig = new TestConfiguration { ["noCheck"] = "true" };
            var withoutChecking = result.Repeat!(testConfig);
            CompareResultFileSets(withoutChecking.DTS, result.DTS);
            CompareResultFileSets(withoutChecking.JS, result.JS);

            void CompareResultFileSets(Collections.OrderedMap<string, TestFile> a, Collections.OrderedMap<string, TestFile> b)
            {
                foreach (var (key, docOrNull) in a.Entries())
                {
                    var doc = docOrNull!;
                    var original = b.GetOrZero(key);
                    if (original is null)
                    {
                        jsCode.Append("\r\n\r\n!!!! File ");
                        jsCode.Append(RemoveTestPathPrefixes(doc.UnitName, false /*retainTrailingDirectorySeparator*/));
                        jsCode.Append(" missing from original emit, but present in noCheck emit\r\n");
                        jsCode.Append(FileOutput(doc, harnessSettings));
                    }
                    else if (original.Content != doc.Content)
                    {
                        jsCode.Append("\r\n\r\n!!!! File ");
                        jsCode.Append(RemoveTestPathPrefixes(doc.UnitName, false /*retainTrailingDirectorySeparator*/));
                        jsCode.Append(" differs from original emit in noCheck emit\r\n");
                        string fileName;
                        if (harnessSettings.FullEmitPaths)
                        {
                            fileName = RemoveTestPathPrefixes(doc.UnitName, false /*retainTrailingDirectorySeparator*/);
                        }
                        else
                        {
                            fileName = GetBaseFileName(doc.UnitName);
                        }

                        jsCode.Append("//// [");
                        jsCode.Append(fileName);
                        jsCode.Append("]\r\n");
                        var expected = original.Content;
                        var actual = doc.Content;
                        jsCode.Append(Baseline.DiffText("Expected\tThe full check baseline", "Actual\twith noCheck set", expected, actual));
                    }
                }
            }
        }

        if (FileExtensionIsOneOf(baselinePath, [ExtensionTs, ExtensionTsx]))
        {
            baselinePath = ChangeExtension(baselinePath, ExtensionJs);
        }

        string actualContent;
        if (jsCode.Length > 0)
        {
            actualContent = tsCode.ToString() + "\r\n\r\n" + jsCode.ToString();
        }
        else
        {
            actualContent = Baseline.NoContent;
        }

        Baseline.Run(t, baselinePath, actualContent, opts);
    }

    /// <summary>Go: <c>func fileOutput(file *harnessutil.TestFile, settings *harnessutil.HarnessOptions) string</c>。</summary>
    private static string FileOutput(TestFile file, HarnessOptions settings)
    {
        string fileName;
        if (settings.FullEmitPaths)
        {
            fileName = RemoveTestPathPrefixes(file.UnitName, false /*retainTrailingDirectorySeparator*/);
        }
        else
        {
            fileName = GetBaseFileName(file.UnitName);
        }

        return "//// [" + fileName + "]\r\n" + file.Content;
    }

    /// <summary>Go: <c>type declarationCompilationContext struct</c>。</summary>
    private sealed class DeclarationCompilationContext
    {
        public TestFile[] DeclInputFiles = [];
        public TestFile[] DeclOtherFiles = [];
        public HarnessOptions HarnessSettings = null!;
        public CompilerOptions Options = null!;
        public string CurrentDirectory = "";
        public ParsedCommandLine? Config;
    }

    /// <summary>Go: <c>func prepareDeclarationCompilationContext(...) *declarationCompilationContext</c>。</summary>
    private static DeclarationCompilationContext? PrepareDeclarationCompilationContext(
        TestFile[] inputFiles,
        TestFile[] otherFiles,
        CompilationResult result,
        HarnessOptions harnessSettings,
        CompilerOptions options,
        // Current directory is needed for rwcRunner to be able to use currentDirectory defined in json file
        string currentDirectory)
    {
        if (options.Declaration.IsTrue() && result.Diagnostics.Length == 0)
        {
            if (options.EmitDeclarationOnly.IsTrue())
            {
                if (result.JS.Size() > 0)
                {
                    Debug.Fail("Only declaration files should be generated when emitDeclarationOnly:true");
                }

                if (result.DTS.Size() == 0 && !options.NoEmit.IsTrue())
                {
                    Debug.Fail("Expected at least one declaration file to be emitted when emitDeclarationOnly:true and no errors were generated");
                }
            }
            else if (!Some(result.Program.GetSourceFiles(), file => file.ContentMapper() != "")
                && result.DTS.Size() != result.GetNumberOfJSFiles(false /*includeJson*/))
            {
                Debug.Fail("There were no errors and declFiles generated did not match number of js files generated");
            }
        }

        var declInputFiles = new List<TestFile>();
        var declOtherFiles = new List<TestFile>();

        TestFile? FindUnit(string fileName, List<TestFile> units)
        {
            foreach (var unit in units)
            {
                if (unit.UnitName == fileName)
                {
                    return unit;
                }
            }

            return null;
        }

        TestFile? FindResultCodeFile(string fileName)
        {
            var sourceFile = result.Program.GetSourceFile(fileName);
            if (sourceFile is null)
            {
                Debug.Fail("Program has no source file with name '" + fileName + "'");
            }

            // Is this file going to be emitted separately
            string sourceFileName;

            if (options.OutDir.Length != 0)
            {
                var sourceFilePath = GetNormalizedAbsolutePath(sourceFile!.FileName(), result.Host.GetCurrentDirectory());
                sourceFilePath = ReplaceFirst(sourceFilePath, result.Program.CommonSourceDirectory(), "");
                sourceFileName = CombinePaths(options.OutDir, sourceFilePath);
            }
            else
            {
                sourceFileName = sourceFile!.FileName();
            }

            var dTsFileName = TypeScript.OutputPaths.OutputPaths.ChangeToDeclarationExtension(sourceFileName, new ProgramOutputPathsHost(ProgramOf(result.Program)));
            return result.DTS.GetOrZero(dTsFileName);
        }

        List<TestFile> AddDtsFile(TestFile file, List<TestFile> dtsFiles)
        {
            if (IsDeclarationFileName(file.UnitName) || HasJSONFileExtension(file.UnitName))
            {
                dtsFiles.Add(file);
            }
            else if (result.Program.GetSourceFile(file.UnitName) is { } sourceFile
                && (HasTSFileExtension(file.UnitName)
                    || (HasJSFileExtension(file.UnitName) && options.GetAllowJS())
                    || sourceFile.ContentMapper() != ""))
            {
                var declFile = FindResultCodeFile(file.UnitName);
                if (declFile is not null
                    && FindUnit(declFile.UnitName, declInputFiles) is null
                    && FindUnit(declFile.UnitName, declOtherFiles) is null)
                {
                    dtsFiles.Add(new TestFile
                    {
                        UnitName = declFile.UnitName,
                        Content = declFile.Content.StartsWith('\uFEFF') ? declFile.Content[1..] : declFile.Content,
                    });
                }
            }

            return dtsFiles;
        }

        // if the .d.ts is non-empty, confirm it compiles correctly as well
        if (options.Declaration.IsTrue() && result.Diagnostics.Length == 0 && result.DTS.Size() > 0)
        {
            foreach (var file in inputFiles)
            {
                declInputFiles = AddDtsFile(file, declInputFiles);
            }

            foreach (var file in otherFiles)
            {
                declOtherFiles = AddDtsFile(file, declOtherFiles);
            }

            return new DeclarationCompilationContext
            {
                DeclInputFiles = [.. declInputFiles],
                DeclOtherFiles = [.. declOtherFiles],
                HarnessSettings = harnessSettings,
                Options = options,
                CurrentDirectory = IfElse(currentDirectory.Length > 0, currentDirectory, harnessSettings.CurrentDirectory),
                Config = ProgramOf(result.Program).CommandLine(),
            };
        }

        return null;
    }

    /// <summary>Go: <c>type declarationCompilationResult struct</c>。</summary>
    private sealed class DeclarationCompilationResult
    {
        public TestFile[] DeclInputFiles = [];
        public TestFile[] DeclOtherFiles = [];
        public CompilationResult DeclResult = null!;
    }

    /// <summary>Go: <c>func compileDeclarationFiles(t *testing.T, context *declarationCompilationContext, symlinks map[string]string) *declarationCompilationResult</c>。</summary>
    private static DeclarationCompilationResult? CompileDeclarationFiles(
        IHarnessTest t,
        DeclarationCompilationContext? context,
        Dictionary<string, string> symlinks)
    {
        if (context is null)
        {
            return null;
        }

        ParsedCommandLine? tsconfig = null;
        if (context.Config is not null && context.Config.ConfigFile is not null)
        {
            tsconfig = new ParsedCommandLine
            {
                ParsedConfig = new ParsedOptions
                {
                    ContentMappers = context.Config.ContentMappers(),
                },
                ConfigFile = context.Config.ConfigFile,
            };
        }

        var declFileCompilationResult = HarnessUtil.CompileFilesEx(
            t,
            context.DeclInputFiles,
            context.DeclOtherFiles,
            context.HarnessSettings,
            context.Options,
            context.CurrentDirectory,
            symlinks,
            tsconfig);
        return new DeclarationCompilationResult
        {
            DeclInputFiles = context.DeclInputFiles,
            DeclOtherFiles = context.DeclOtherFiles,
            DeclResult = declFileCompilationResult,
        };
    }

    /// <summary>
    /// Go: <c>result.Program.Program()</c>。C# 的 `ProgramLike` 没有 `Program()` 成员（Program.Emit.cs 文件头第 4 条：
    /// 接口成员与类同名会 CS0542），所以显式转回具体类型 —— <c>CompilationResult.Program</c> 的运行期类型恒为 Program。
    /// </summary>
    private static Program ProgramOf(ProgramLike program) => (Program)program;

    /// <summary>
    /// Go: <c>outputpaths.ChangeToDeclarationExtension(path, result.Program.Program())</c> 的 OutputPathsHost 适配器
    /// （Go 是结构满足，C# 要显式实现；四个成员逐一转发到 Program，行为完全一致）。
    /// </summary>
    private sealed class ProgramOutputPathsHost(Program program) : OutputPathsHost
    {
        public string CommonSourceDirectory() => program.CommonSourceDirectory();

        public string[] ContentMapperExtensions() => program.ContentMapperExtensions();

        public string GetCurrentDirectory() => program.GetCurrentDirectory();

        public bool UseCaseSensitiveFileNames() => program.UseCaseSensitiveFileNames();
    }

    /// <summary>Go: <c>strings.Replace(s, old, new, 1)</c>（只替换第一处）。</summary>
    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0)
        {
            return text;
        }

        return text[..index] + newValue + text[(index + oldValue.Length)..];
    }
}

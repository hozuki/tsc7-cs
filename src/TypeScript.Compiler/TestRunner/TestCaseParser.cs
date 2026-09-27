// ported from: internal/testrunner/test_case_parser.go
//
// 声明顺序：Go 的包级 var 在 C# 里必须落在同一个静态类（`TestRunner`）内，所以
// `lineDelimiter`/`optionRegex`/`linkRegex`/`fourslashDirectives` 从 Go 的「分散在类型之间」改成集中在类首
// （C# 的成员集合与声明顺序无关，§2 的 partial 约定）；其余声明次序与 Go 逐一对应。
//
// 与 Go 的差异（均在 docs/architecture.md §2/§3 允许范围内）：
// 1) `rawCompilerSettings`（`type rawCompilerSettings map[string]string`，命名 map 类型、无方法）→
//    `Dictionary<string, string>`（§3.2）；Go 的零值语义（读缺失键得 ""）在调用点改成 `TryGetValue`。
// 2) `(T, error)` → 抛异常（§3.2）：`parseFile` 回调失败时 Go 会"break + 返回已解析的部分单元 + 错误"，
//    C# 里回调抛出的异常直接穿过（调用方本就把该错误视为致命：fourslash 的 ParseTestData 用 t.Fatalf）。
// 3) `testUnit`/`testCaseContent` 是未导出类型 → 按 C# 习惯命名 `TestUnit`/`TestCaseContent`；未导出字段
//    `content`/`name`/`testUnitData`/`tsConfig`/`tsConfigFileUnitData`/`symlinks` → `_camelCase`（同程序集可见，
//    用 `internal`，§2）。`ParseTestFilesOptions` 是导出类型/导出字段 → 名字照抄。
// 4) `regexp` → `System.Text.RegularExpressions.Regex`（AOT 安全，不用 `RegexOptions.Compiled`，SemVer 同款）：
//    * Go 的 `\s` 只匹配 ASCII `[\t\n\f\r ]`、`\w` 只匹配 `[0-9A-Za-z_]`，而 .NET 默认是 Unicode 类别，
//      所以 pattern 里显式写成这两个集合（`SemVer/Version.cs` 的同款约定）；`\s` 含 `\n`/`\r` 这点必须保留
//      （`^//\s*@…` 允许跨行匹配，见 extractCompilerSettings）。
//    * `(?m)` → `RegexOptions.Multiline`，另加 `RegexOptions.CultureInvariant`（Go 的匹配与区域无关）。
//    * `\/` → `/`（.NET 里无需转义斜杠，语义相同）。
// 5) 标准库映射：`strings.Builder` → `StringBuilder`；`strings.ToLower` → `ToLowerJS`（§3.4 的完整 JS 映射）；
//    `strings.TrimSpace` → `TsOptions.TrimSpace`（Go 的 unicode.IsSpace 集合，见 TsOptions/ParsingHelpers.cs）；
//    `strings.TrimSuffix(s, ";")` → `EndsWith(';')` + `s[..^1]`；`strings.SplitSeq` → `ReadOnlySpan<char>.Split`
//    （§3.8 零分配）；`slices.Delete` → `List.RemoveAt`；`slices.Contains` → `Array.IndexOf(...) >= 0`。
// 6) `panic` → `Debug.Fail`（§3.2）。
// 7) **依赖缺失（尚未移植，本文件因此不可单独编译；调用点按 §2 的目录/命名规则写出期望名）**：
//    * `tsoptionstest.NewVFSParseConfigHostWithSymlinks`（internal/tsoptions/tsoptionstest/vfsparseconfighost.go:36）
//      → `TypeScript.TsOptions.TsOptionsTest.TsOptionsTest`；
//    * `harnessutil.GetConfigNameFromFileName`（internal/testutil/harnessutil/harnessutil.go:1228）
//      → `TypeScript.TestUtil.HarnessUtil.HarnessUtil`；
//    * `srcFolder`（internal/testrunner/compiler_runner.go:34，同包另一文件）→ `TestRunner.srcFolder`。
using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using static TypeScript.Parser.Parser;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;

namespace TypeScript.TestRunner;

// This maps a compiler setting to its value as written in the test file. For example, if a test file contains:
//
//	// @target: esnext, es2015
//
// Then the map will map "target" to "esnext, es2015"
//
// Go: type rawCompilerSettings map[string]string（命名 map 类型、无方法 → 直接落成 Dictionary<string, string>，§3.2）

/// <summary>All the necessary information to turn a multi file test into useful units for later compilation.</summary>
internal sealed class TestUnit
{
    internal string _content = "";
    internal string _name = "";
}

internal sealed class TestCaseContent
{
    internal TestUnit[] _testUnitData = [];
    internal ParsedCommandLine? _tsConfig;
    internal TestUnit? _tsConfigFileUnitData;
    internal Dictionary<string, string> _symlinks = [];
}

/// <summary>Go: <c>type ParseTestFilesOptions struct</c>。</summary>
public sealed class ParseTestFilesOptions
{
    /// <summary>If true, allows test content to appear before the first @Filename directive.
    /// In this case, an implicit first file is created using the fileName parameter.
    /// This matches the behavior of the TypeScript fourslash test harness.</summary>
    public bool AllowImplicitFirstFile;
}

/// <summary>Go: <c>package testrunner</c> 的包级 var 与包级函数。</summary>
public static partial class TestRunner
{
    /// <summary>Go: <c>var lineDelimiter = regexp.MustCompile("\r?\n")</c>。</summary>
    internal static readonly Regex _lineDelimiter = new(@"\r?\n", RegexOptions.CultureInvariant);

    /// <summary>Regex for parsing options in the format "@Alpha: Value of any sort"。</summary>
    internal static readonly Regex _optionRegex = new(
        @"^//[ \t\n\f\r]*@([0-9A-Za-z_]+)[ \t\n\f\r]*:[ \t\n\f\r]*([^\r\n]*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Regex for parsing @link option。</summary>
    internal static readonly Regex _linkRegex = new(
        @"^//[ \t\n\f\r]*@link[ \t\n\f\r]*:[ \t\n\f\r]*([^\r\n]*)[ \t\n\f\r]*->[ \t\n\f\r]*([^\r\n]*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>File-specific directives used by fourslash tests。</summary>
    internal static readonly string[] _fourslashDirectives = ["emitthisfile", "noopen"];

    /// <summary>Given a test file containing // @FileName directives,
    /// return an array of named units of code to be added to an existing compiler instance.</summary>
    internal static TestCaseContent MakeUnitsFromTest(string code, string fileName)
    {
        var (testUnits, symlinks, currentDirectory, globalOptions) = ParseTestFilesAndSymlinks(
            code,
            fileName,
            (filename, content, _) => new TestUnit { _content = content, _name = filename });

        if (currentDirectory == "")
        {
            // Go: currentDirectory = srcFolder（compiler_runner.go:34 的包级 var；见本目录 CompilerRunner.cs）
            currentDirectory = SrcFolder;
        }

        // unit tests always list files explicitly
        var allFiles = new Dictionary<string, string>();
        foreach (var data in testUnits)
        {
            allFiles[GetNormalizedAbsolutePath(data._name, currentDirectory)] = data._content;
        }
        var parseConfigHost = TypeScript.TsOptions.TsOptionsTest.TsOptionsTest.NewVFSParseConfigHostWithSymlinks(allFiles, symlinks, currentDirectory, true /*useCaseSensitiveFileNames*/);

        // Content mappers are gated behind --runExternalCode, a command-line-only option. A test
        // opts in with a top-level `// @runExternalCode: true`, which we surface to the config
        // parse as an existing option so the gate passes and the mappers register.
        CompilerOptions? existingOptions = null;
        if (globalOptions.TryGetValue("runexternalcode", out var runExternalCode) && runExternalCode == "true")
        {
            existingOptions = new CompilerOptions { RunExternalCode = Tristate.TSTrue };
        }

        // check if project has tsconfig.json in the list of files
        ParsedCommandLine? tsConfig = null;
        TestUnit? tsConfigFileUnitData = null;
        var units = new List<TestUnit>(testUnits);
        for (var i = 0; i < units.Count; i++)
        {
            var data = units[i];
            // Go: tsoptionstest/harnessutil 的 GetConfigNameFromFileName 已落地（TestUtil/HarnessUtil/HarnessUtil.cs）
            if (TypeScript.TestUtil.HarnessUtil.HarnessUtil.GetConfigNameFromFileName(data._name) != "")
            {
                var configFileName = GetNormalizedAbsolutePath(data._name, currentDirectory);
                var path = ToPath(data._name, parseConfigHost.GetCurrentDirectory(), parseConfigHost.Vfs.UseCaseSensitiveFileNames());
                var configJson = ParseSourceFile(
                    new SourceFileParseOptions
                    {
                        FileName = configFileName,
                        Path = path,
                    },
                    data._content,
                    ScriptKind.JSON);
                var tsConfigSourceFile = new TsConfigSourceFile(configJson);
                var configDir = GetDirectoryPath(configFileName);
                tsConfig = ParseJsonSourceFileConfigFileContent(
                    tsConfigSourceFile,
                    parseConfigHost,
                    configDir,
                    existingOptions,
                    null, /*existingOptionsRaw*/
                    configFileName,
                    null, /*resolutionStack*/
                    null); /*extendedConfigCache*/
                tsConfigFileUnitData = data;

                // delete tsconfig file entry from the list
                units.RemoveAt(i);
                break;
            }
        }

        return new TestCaseContent
        {
            _testUnitData = [.. units],
            _tsConfig = tsConfig,
            _tsConfigFileUnitData = tsConfigFileUnitData,
            _symlinks = symlinks,
        };
    }

    /// <summary>Given a test file containing // @FileName and // @symlink directives,
    /// return an array of named units of code to be added to an existing compiler instance,
    /// along with a map of symlinks and the current directory.</summary>
    public static (T[] Units, Dictionary<string, string> Symlinks, string CurrentDir, Dictionary<string, string> GlobalOptions) ParseTestFilesAndSymlinks<T>(
        string code,
        string fileName,
        Func<string, string, Dictionary<string, string>, T> parseFile)
    {
        return ParseTestFilesAndSymlinksWithOptions(code, fileName, parseFile, new ParseTestFilesOptions());
    }

    public static (T[] Units, Dictionary<string, string> Symlinks, string CurrentDir, Dictionary<string, string> GlobalOptions) ParseTestFilesAndSymlinksWithOptions<T>(
        string code,
        string fileName,
        Func<string, string, Dictionary<string, string>, T> parseFile,
        ParseTestFilesOptions options)
    {
        // List of all the subfiles we've parsed out
        List<T> testUnits = [];

        var lines = _lineDelimiter.Split(code);

        // Stuff related to the subfile we're parsing
        var currentFileContent = new StringBuilder();
        var currentFileName = "";
        var seenContentLine = false;
        var hasSeenFile = false;
        if (options.AllowImplicitFirstFile)
        {
            // For fourslash tests, initialize currentFileName to the fileName parameter
            // so content before the first @Filename directive goes into an implicit first file
            currentFileName = fileName;
        }
        var currentDirectory = "";
        var currentFileOptions = new Dictionary<string, string>();
        var symlinks = new Dictionary<string, string>();
        var globalOptions = new Dictionary<string, string>();

        foreach (var line in lines)
        {
            if (ParseSymlinkFromTest(line, symlinks))
            {
                continue;
            }
            var testMetaData = _optionRegex.Match(line);
            if (testMetaData.Success)
            {
                // Comment line, check for global/file @options and record them
                var metaDataName = ToLowerJS(testMetaData.Groups[1].Value);
                var metaDataValue = TrimSpace(testMetaData.Groups[2].Value);
                if (metaDataName == "currentdirectory")
                {
                    currentDirectory = metaDataValue;
                }
                if (metaDataName != "filename")
                {
                    if (metaDataName == "symlink" && currentFileName != "")
                    {
                        var value = metaDataValue.AsSpan();
                        foreach (var linkRange in value.Split(','))
                        {
                            var link = value[linkRange].Trim();
                            if (link.Length != 0)
                            {
                                symlinks[link.ToString()] = currentFileName;
                            }
                        }
                    }
                    else if (Array.IndexOf(_fourslashDirectives, metaDataName) >= 0)
                    {
                        // File-specific option
                        currentFileOptions[metaDataName] = metaDataValue;
                    }
                    else
                    {
                        // Global option
                        if (globalOptions.TryGetValue(metaDataName, out var existingValue) && existingValue != metaDataValue)
                        {
                            // !!! This would break existing baseline tests
                            // Debug.Fail("Duplicate global option: " + metaDataName);
                        }
                        globalOptions[metaDataName] = metaDataValue;
                    }
                    continue;
                }

                // New metadata statement after having collected some code to go with the previous metadata
                if (currentFileName != "")
                {
                    // Store result file - always save for regular tests, but skip empty implicit first file for fourslash
                    var shouldSaveFile = !options.AllowImplicitFirstFile || currentFileContent.Length != 0 || hasSeenFile;
                    if (shouldSaveFile)
                    {
                        hasSeenFile = true;
                        testUnits.Add(parseFile(currentFileName, currentFileContent.ToString(), currentFileOptions));
                    }

                    // Reset local data
                    currentFileContent.Clear();
                    seenContentLine = false;
                    currentFileName = metaDataValue;
                    currentFileOptions = new Dictionary<string, string>();
                }
                else
                {
                    // First metadata marker in the file
                    var hasContentBeforeFirstFilename = currentFileContent.Length != 0 && SkipTrivia(currentFileContent.ToString(), 0) != currentFileContent.Length;
                    if (hasContentBeforeFirstFilename && !options.AllowImplicitFirstFile)
                    {
                        Debug.Fail("Non-comment test content appears before the first '// @Filename' directive");
                    }

                    // If we have content before the first @Filename and AllowImplicitFirstFile is true,
                    // we need to save it as an implicit first file before starting the new file
                    if (hasContentBeforeFirstFilename && options.AllowImplicitFirstFile && currentFileName != "")
                    {
                        // Store the implicit first file
                        hasSeenFile = true;
                        testUnits.Add(parseFile(currentFileName, currentFileContent.ToString(), currentFileOptions));
                    }

                    // Reset for the new file
                    currentFileContent.Clear();
                    seenContentLine = false;
                    currentFileName = TrimSpace(testMetaData.Groups[2].Value);
                    currentFileOptions = new Dictionary<string, string>();
                }
            }
            else
            {
                // Subfile content line
                // Append to the current subfile content, inserting a newline if needed
                // For fourslash tests, use seenContentLine to preserve leading blank lines
                // (matching TS fourslash's //// content markers). For compiler tests, use
                // Len() != 0 which drops leading blanks (matching TS's harness behavior).
                if (options.AllowImplicitFirstFile)
                {
                    if (seenContentLine)
                    {
                        currentFileContent.Append('\n');
                    }
                    seenContentLine = true;
                }
                else
                {
                    if (currentFileContent.Length != 0)
                    {
                        currentFileContent.Append('\n');
                    }
                }
                currentFileContent.Append(line);
            }
        }

        // normalize the fileName for the single file case
        if (testUnits.Count == 0 && currentFileName.Length == 0)
        {
            currentFileName = GetBaseFileName(fileName);
        }

        // if there are no parse errors so far, parse the rest of the file
        // EOF, push whatever remains
        testUnits.Add(parseFile(currentFileName, currentFileContent.ToString(), currentFileOptions));

        return ([.. testUnits], symlinks, currentDirectory, globalOptions);
    }

    internal static Dictionary<string, string> ExtractCompilerSettings(string content)
    {
        var opts = new Dictionary<string, string>();

        foreach (Match match in _optionRegex.Matches(content))
        {
            var value = TrimSpace(match.Groups[2].Value);
            opts[ToLowerJS(match.Groups[1].Value)] = value.EndsWith(';') ? value[..^1] : value;
        }

        return opts;
    }

    internal static bool ParseSymlinkFromTest(string line, Dictionary<string, string> symlinks)
    {
        var linkMetaData = _linkRegex.Match(line);
        if (!linkMetaData.Success)
        {
            return false;
        }

        symlinks[TrimSpace(linkMetaData.Groups[2].Value)] = TrimSpace(linkMetaData.Groups[1].Value);
        return true;
    }
}

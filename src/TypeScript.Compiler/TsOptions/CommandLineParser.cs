// ported from: internal/tsoptions/commandlineparser.go
//
// 映射约定（docs/architecture.md §2/§3）：
// 1) Go 的 `commandLineParser`（未导出）→ `internal sealed partial class CommandLineParser`（PascalCase 类名；
//    errors.go 那个分片里的同类型方法也在同一个 partial 类型上）。
// 2) Go 的 `vfs.FS` 接口 → `TypeScript.Vfs.FS`（另一个分片提供）；`p.fs == nil` 的判断保留成 `_fs is null`。
// 3) `p.options *collections.OrderedMap[string, any]` → `OrderedMap<string, object?>`；`p.fileNames`/`p.errors`
//    在 Go 里是 append 用的切片 → C# 用 `List<T>`，出口处转成数组（`[.. list]`）。
// 4) `strconv.Atoi` → `int.TryParse` + `NumberStyles.AllowLeadingSign` + InvariantCulture（Go 的 Atoi 只接受
//    可选符号 + 十进制数字，不接受前后空白/千分位；无符号或空白都会走到 `e != nil` 的诊断分支）。
//    文件中保留 Go 的注释 `!!! Make sure this parseInt matches JS parseInt`。
// 5) 响应文件里 Go 用 `[]rune(fileContents)` 按**码点**扫描并切分 token。§3.1：C# 的 string 按 UTF-16 代码单元索引，
//    这里的循环只判断 `' '`/`'"'`/其它非空白，两者切出的 token 文本完全一致（索引只用于切片，不参与语义），
//    故直接用 string 下标 + `AsSpan(...).ToString()`（不产生中间 char[]）。
// 6) `panic(...)` → `throw new DebugFailureException(...)`；`(T, error)` 形态的 `tryReadFile` → C# 元组返回
//    `(string FileContents, Diagnostic[] Errors)`（详见 ParsingHelpers.cs 的 TryReadFile）。
using System.Collections.Generic;
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Collections;
using static TypeScript.Core.Core;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TSPath;
using Path = TypeScript.TSPath.Path;
using TypeScript.Vfs;
using static TypeScript.Ast.Ast;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.TsOptions;

internal sealed partial class CommandLineParser
{
    internal ParseCommandLineWorkerDiagnostics _workerDiagnostics = new();
    internal NameMap? _optionsMap;
    internal FS? _fs;
    internal string _currentDirectory = "";
    internal OrderedMap<string, object?> _options = new();
    internal List<string> _fileNames = [];
    internal List<Diagnostic> _errors = [];
    internal Set<Path> _responseFileStack = new();

    internal AlternateModeDiagnostics? AlternateMode() => _workerDiagnostics._didYouMean._alternateMode;

    internal CommandLineOption[] OptionsDeclarations() => _workerDiagnostics._didYouMean.OptionDeclarations;

    internal Message? UnknownOptionDiagnostic() => _workerDiagnostics._didYouMean.UnknownOptionDiagnostic;

    internal Message? UnknownDidYouMeanDiagnostic() => _workerDiagnostics._didYouMean.UnknownDidYouMeanDiagnostic;

    internal void ParseStrings(string[] args)
    {
        var i = 0;
        while (i < args.Length)
        {
            var s = args[i];
            i++;
            if (s == "")
            {
                continue;
            }
            switch (s[0])
            {
                case '@':
                    ParseResponseFile(s[1..]);
                    break;
                case '-':
                    var inputOptionName = GetInputOptionName(s);
                    var opt = _optionsMap!.GetOptionDeclarationFromName(inputOptionName, true /*allowShort*/);
                    if (opt is not null)
                    {
                        i = ParseOptionValue(args, i, opt, _workerDiagnostics.OptionTypeMismatchDiagnostic!);
                    }
                    else
                    {
                        var watchOpt = TsOptions.WatchNameMap.GetOptionDeclarationFromName(inputOptionName, true /*allowShort*/);
                        if (watchOpt is not null)
                        {
                            i = ParseOptionValue(args, i, watchOpt, TsOptions.WatchOptionsDidYouMeanDiagnostics.OptionTypeMismatchDiagnostic!);
                        }
                        else
                        {
                            _errors.Add(CreateUnknownOptionError(inputOptionName, s, null, null));
                        }
                    }
                    break;
                default:
                    _fileNames.Add(s);
                    break;
            }
        }
    }

    internal static string GetInputOptionName(string input)
    {
        // removes at most two leading '-' from the input string
        if (input.StartsWith('-'))
        {
            input = input[1..];
        }
        if (input.StartsWith('-'))
        {
            input = input[1..];
        }
        return input;
    }

    internal void ParseResponseFile(string fileName)
    {
        fileName = GetNormalizedAbsolutePath(fileName, _currentDirectory);
        var path = ToPath(fileName, _currentDirectory, _fs!.UseCaseSensitiveFileNames());
        if (_responseFileStack.Has(path))
        {
            return;
        }
        _responseFileStack.Add(path);
        try
        {
            var (fileContents, errors) = TsOptions.TryReadFile(fileName, readFileName =>
            {
                if (_fs is null)
                {
                    return ("", false);
                }
                var ok = _fs.TryReadFile(readFileName, out var contents);
                return (contents, ok);
            }, [.. _errors]);
            _errors.Clear();
            _errors.AddRange(errors);

            if (fileContents == "")
            {
                return;
            }

            var args = new List<string>();
            var text = fileContents;
            var textLength = text.Length;
            var pos = 0;
            while (pos < textLength)
            {
                while (pos < textLength && text[pos] <= ' ')
                {
                    pos++;
                }
                if (pos >= textLength)
                {
                    break;
                }
                var start = pos;
                if (text[pos] == '"')
                {
                    pos++;
                    while (pos < textLength && text[pos] != '"')
                    {
                        pos++;
                    }
                    if (pos < textLength)
                    {
                        args.Add(text.AsSpan(start + 1, pos - (start + 1)).ToString());
                        pos++;
                    }
                    else
                    {
                        _errors.Add(NewCompilerDiagnostic(Unterminated_quoted_string_in_response_file_0, fileName));
                    }
                }
                else
                {
                    while (pos < textLength && text[pos] > ' ')
                    {
                        pos++;
                    }
                    args.Add(text.AsSpan(start, pos - start).ToString());
                }
            }
            ParseStrings([.. args]);
        }
        finally
        {
            // Go: defer p.responseFileStack.Delete(path)
            _responseFileStack.Delete(path);
        }
    }

    internal int ParseOptionValue(
        string[] args,
        int i,
        CommandLineOption opt,
        Message diag)
    {
        if (opt.IsTSConfigOnly && i <= args.Length)
        {
            var optValue = "";
            if (i < args.Length)
            {
                optValue = args[i];
            }
            if (optValue == "null")
            {
                _options.Set(opt.Name, null);
                i++;
            }
            else if (opt.Kind == CommandLineOptionKind.Boolean)
            {
                if (optValue == "false")
                {
                    _options.Set(opt.Name, false);
                    i++;
                }
                else
                {
                    if (optValue == "true")
                    {
                        i++;
                    }
                    _errors.Add(NewCompilerDiagnostic(Option_0_can_only_be_specified_in_tsconfig_json_file_or_set_to_false_or_null_on_command_line, opt.Name));
                }
            }
            else
            {
                _errors.Add(NewCompilerDiagnostic(Option_0_can_only_be_specified_in_tsconfig_json_file_or_set_to_null_on_command_line, opt.Name));
                if (optValue.Length != 0 && !optValue.StartsWith('-'))
                {
                    i++;
                }
            }
        }
        else
        {
            // Check to see if no argument was provided (e.g. "--locale" is the last command-line argument).
            if (i >= args.Length)
            {
                if (opt.Kind != CommandLineOptionKind.Boolean)
                {
                    _errors.Add(NewCompilerDiagnostic(diag, opt.Name, TsOptions.GetCompilerOptionValueTypeString(opt)));
                    if (opt.Kind == CommandLineOptionKind.List)
                    {
                        _options.Set(opt.Name, (string[])[]);
                    }
                    else if (opt.Kind == CommandLineOptionKind.Enum)
                    {
                        _errors.Add(TsOptions.CreateDiagnosticForInvalidEnumType(opt, null, null));
                    }
                }
                else
                {
                    _options.Set(opt.Name, true);
                }
                return i;
            }
            if (args[i] != "null")
            {
                switch (opt.Kind)
                {
                    case CommandLineOptionKind.Number:
                    {
                        // !!! Make sure this parseInt matches JS parseInt
                        if (int.TryParse(args[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var num))
                        {
                            if (num >= opt._minValue)
                            {
                                _options.Set(opt.Name, num);
                            }
                            else
                            {
                                _errors.Add(NewCompilerDiagnostic(Option_0_requires_value_to_be_greater_than_1, opt.Name, opt._minValue.ToString(CultureInfo.InvariantCulture)));
                            }
                        }
                        else
                        {
                            _errors.Add(NewCompilerDiagnostic(diag, opt.Name, "number"));
                        }
                        i++;
                        break;
                    }
                    case CommandLineOptionKind.Boolean:
                    {
                        // boolean flag has optional value true, false, others
                        var optValue = args[i];

                        // check next argument as boolean flag value
                        if (optValue == "false")
                        {
                            _options.Set(opt.Name, false);
                        }
                        else
                        {
                            _options.Set(opt.Name, true);
                        }
                        // try to consume next argument as value for boolean flag; do not consume argument if it is not "true" or "false"
                        if (optValue == "false" || optValue == "true")
                        {
                            i++;
                        }
                        break;
                    }
                    case CommandLineOptionKind.String:
                    {
                        var (val, errors) = TsOptions.ValidateJsonOptionValue(opt, args[i], null, null);
                        if (errors.Length == 0)
                        {
                            _options.Set(opt.Name, val);
                        }
                        else
                        {
                            _errors.AddRange(errors);
                        }
                        i++;
                        break;
                    }
                    case CommandLineOptionKind.List:
                    {
                        var (result, errors) = ParseListTypeOption(opt, args[i]);
                        _options.Set(opt.Name, result);
                        _errors.AddRange(errors);
                        if (result.Length > 0 || errors.Length > 0)
                        {
                            i++;
                        }
                        break;
                    }
                    case CommandLineOptionKind.ListOrElement:
                        // If not a primitive, the possible types are specified in what is effectively a map of options.
                        throw new DebugFailureException("listOrElement not supported here");
                    default:
                    {
                        var (val, errors) = TsOptions.ConvertJsonOptionOfEnumType(opt, TsOptions.TrimWhiteSpaceLike(args[i]), null, null);
                        _options.Set(opt.Name, val);
                        _errors.AddRange(errors);
                        i++;
                        break;
                    }
                }
            }
            else
            {
                _options.Set(opt.Name, null);
                i++;
            }
        }
        return i;
    }

    internal (object?[] Values, Diagnostic[] Errors) ParseListTypeOption(CommandLineOption opt, string value)
    {
        return TsOptions.ParseListTypeOption(opt, value);
    }
}

public static partial class TsOptions
{
    public static ParsedCommandLine ParseCommandLine(string[]? commandLine, ParseConfigHost host)
    {
        commandLine ??= [];
        var parser = ParseCommandLineWorker(CompilerOptionsDidYouMeanDiagnostics, commandLine, host.FS(), host.GetCurrentDirectory());
        var options = ConvertToOptionsWithAbsolutePaths(parser._options.Clone(), CommandLineCompilerOptionsMap, host.GetCurrentDirectory());
        // Go 侧 options 由 convertToOptionsWithAbsolutePaths 返回，恒非 nil（nil 即 panic）
        var compilerOptions = ConvertMapToOptions(options!, new CompilerOptionsParser(new CompilerOptions())).CompilerOptions;
        var watchOptions = ConvertMapToOptions(options!, new WatchOptionsParser(new WatchOptions())).WatchOptions;
        var result = ParsedCommandLine.NewParsedCommandLine(compilerOptions, [.. parser._fileNames], null, new ComparePathsOptions(host.FS().UseCaseSensitiveFileNames(), host.GetCurrentDirectory()));
        result.ParsedConfig.WatchOptions = watchOptions;
        result.Errors = [.. parser._errors];
        result.Raw = parser._options;
        return result;
    }

    public static ParsedBuildCommandLine ParseBuildCommandLine(string[]? commandLine, ParseConfigHost host)
    {
        commandLine ??= [];
        var parser = ParseCommandLineWorker(BuildOptionsDidYouMeanDiagnostics, commandLine, host.FS(), host.GetCurrentDirectory());
        var compilerOptions = new CompilerOptions();
        foreach (var (key, value) in parser._options.Entries())
        {
            var buildOption = BuildNameMap.Get(key);
            if (ReferenceEquals(buildOption, TscBuildOption) || ReferenceEquals(buildOption, CompilerNameMap.Get(key)))
            {
                ParseCompilerOptions(key, value, compilerOptions);
            }
        }
        var result = new ParsedBuildCommandLine
        {
            BuildOptions = ConvertMapToOptions(parser._options, new BuildOptionsParser(new BuildOptions())).BuildOptions,
            CompilerOptions = compilerOptions,
            WatchOptions = ConvertMapToOptions(parser._options, new WatchOptionsParser(new WatchOptions())).WatchOptions,
            Projects = [.. parser._fileNames],
            Errors = [.. parser._errors],
            Raw = parser._options,

            _comparePathsOptions = new ComparePathsOptions(host.FS().UseCaseSensitiveFileNames(), host.GetCurrentDirectory()),
        };

        if (result.Projects.Length == 0)
        {
            // tsc -b invoked with no extra arguments; act as if invoked with "tsc -b ."
            result.Projects = ["."];
        }

        // Nonsensical combinations
        if (result.BuildOptions.Clean.IsTrue() && result.BuildOptions.Force.IsTrue())
        {
            result.Errors = [.. result.Errors, NewCompilerDiagnostic(Options_0_and_1_cannot_be_combined, "clean", "force")];
        }
        if (result.BuildOptions.Clean.IsTrue() && result.BuildOptions.Verbose.IsTrue())
        {
            result.Errors = [.. result.Errors, NewCompilerDiagnostic(Options_0_and_1_cannot_be_combined, "clean", "verbose")];
        }
        if (result.BuildOptions.Clean.IsTrue() && result.CompilerOptions.Watch.IsTrue())
        {
            result.Errors = [.. result.Errors, NewCompilerDiagnostic(Options_0_and_1_cannot_be_combined, "clean", "watch")];
        }
        if (result.CompilerOptions.Watch.IsTrue() && result.BuildOptions.Dry.IsTrue())
        {
            result.Errors = [.. result.Errors, NewCompilerDiagnostic(Options_0_and_1_cannot_be_combined, "watch", "dry")];
        }

        return result;
    }

    internal static CommandLineParser ParseCommandLineWorker(
        ParseCommandLineWorkerDiagnostics parseCommandLineWithDiagnostics,
        string[] commandLine,
        FS fs,
        string currentDirectory)
    {
        var parser = new CommandLineParser
        {
            _fs = fs,
            _currentDirectory = currentDirectory,
            _workerDiagnostics = parseCommandLineWithDiagnostics,
            _fileNames = [],
            _options = new OrderedMap<string, object?>(),
            _errors = [],
        };
        parser._optionsMap = GetNameMapFromList(parser.OptionsDeclarations());
        parser.ParseStrings(commandLine);
        return parser;
    }

    internal static (string FileContents, Diagnostic[] Errors) TryReadFile(
        string fileName,
        Func<string, (string Text, bool Ok)> readFile,
        Diagnostic[] errors)
    {
        // this function adds a compiler diagnostic if the file cannot be read
        var (text, e) = readFile(fileName);

        if (!e)
        {
            // !!! Divergence: the returned error will not give a useful message
            // errors = append(errors, ast.NewCompilerDiagnostic(diagnostics.Cannot_read_file_0_Colon_1, *e));
            text = "";
            errors = [.. errors, NewCompilerDiagnostic(Cannot_read_file_0, fileName)];
        }
        return (text, errors);
    }

    public static (object?[] Values, Diagnostic[] Errors) ParseListTypeOption(CommandLineOption opt, string value)
    {
        value = TrimSpace(value);
        var errors = new List<Diagnostic>();
        if (value.StartsWith('-'))
        {
            return ([], [.. errors]);
        }
        if (opt.Kind == CommandLineOptionKind.ListOrElement && !value.Contains(','))
        {
            var (val, err) = ValidateJsonOptionValue(opt, value, null, null);
            if (err.Length != 0)
            {
                return ([], err);
            }
            return ([(string)val!], [.. errors]);
        }
        if (value == "")
        {
            return ([], [.. errors]);
        }
        var values = value.Split(',');
        var elements = opt.Elements()!; // Go: opt.Elements() 直接解引用
        switch (elements.Kind)
        {
            case CommandLineOptionKind.String:
                var stringElements = MapFiltered(values, v =>
                {
                    var (val, err) = ValidateJsonOptionValue(elements, v, null, null);
                    if (val is string s && err.Length == 0 && s != "")
                    {
                        return ((object?)s, true);
                    }
                    errors.AddRange(err);
                    return ("", false);
                });
                return (stringElements, [.. errors]);
            case CommandLineOptionKind.Boolean:
            case CommandLineOptionKind.Object:
            case CommandLineOptionKind.Number:
                // do nothing: only string and enum/object types currently allowed as list entries
                // 				!!! we don't actually have number list options, so I didn't implement number list parsing
                throw new DebugFailureException("List of " + elements.Kind.String() + " is not yet supported.");
            default:
                var result = MapFiltered(values, v =>
                {
                    var (val, err) = ConvertJsonOptionOfEnumType(elements, TrimWhiteSpaceLike(v), null, null);
                    if (val is string s && err.Length == 0 && s != "")
                    {
                        return ((object?)s, true);
                    }
                    errors.AddRange(err);
                    return ("", false);
                });
                return (result, [.. errors]);
        }
    }

    internal static (object? Value, Diagnostic[] Errors) ConvertJsonOptionOfEnumType(
        CommandLineOption opt,
        string value,
        Node? valueExpression,
        SourceFile? sourceFile)
    {
        if (value == "")
        {
            return (null, []);
        }
        var key = ToLowerJS(value);
        var typeMap = opt.EnumMap();
        if (typeMap is null)
        {
            return (null, []);
        }
        var (val, ok) = typeMap.Get(key);
        if (ok)
        {
            return ValidateJsonOptionValue(opt, val, valueExpression, sourceFile);
        }
        return (null, [CreateDiagnosticForInvalidEnumType(opt, sourceFile, valueExpression)]);
    }
}

// ported from: internal/tsoptions/export_test.go
//
// Go 的 export_test.go 属于 package tsoptions（只在测试二进制里编译）。C# 的测试在独立程序集里
// （TypeScript.Compiler.Tests 有 InternalsVisibleTo，可直接用 internal 成员），所以这里把同一份测试辅助放进
// 测试工程 —— 与 Go 一样，它不是产品 API（`TestCommandLineParser`/`ParseCommandLineTestWorker` 只在测试里用）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using static TypeScript.TsOptions.TsOptions;

namespace TypeScript.Tests;

internal static class TsOptionsExportTest
{
    internal static ParseCommandLineWorkerDiagnostics GetTestParseCommandLineWorkerDiagnostics(CommandLineOption[] decls)
    {
        if (decls.Length == 0)
        {
            return CompilerOptionsDidYouMeanDiagnostics;
        }
        return GetParseCommandLineWorkerDiagnostics(decls);
    }

    internal static TestCommandLineParser ParseCommandLineTestWorker(
        CommandLineOption[]? decls,
        string[] commandLine,
        FS fs,
        string currentDirectory)
    {
        var parser = new CommandLineParser
        {
            _fs = fs,
            _currentDirectory = currentDirectory,
            _workerDiagnostics = CompilerOptionsDidYouMeanDiagnostics,
            _fileNames = [],
            _options = new OrderedMap<string, object?>(),
            _errors = [],
        };
        if (decls is not null && decls.Length != 0)
        {
            parser._workerDiagnostics = GetTestParseCommandLineWorkerDiagnostics(decls);
        }

        parser._optionsMap = GetNameMapFromList(parser.OptionsDeclarations());
        parser.ParseStrings(commandLine);
        return new TestCommandLineParser(
            fs,
            parser._workerDiagnostics,
            [.. parser._fileNames],
            parser._options,
            [.. parser._errors]);
    }
}

/// <summary>Go: <c>type TestCommandLineParser struct</c>（export_test.go；测试专用）。</summary>
internal sealed class TestCommandLineParser(
    FS fs,
    ParseCommandLineWorkerDiagnostics workerDiagnostics,
    string[] fileNames,
    OrderedMap<string, object?> options,
    Diagnostic[] errors)
{
    internal readonly FS Fs = fs;
    internal readonly ParseCommandLineWorkerDiagnostics WorkerDiagnostics = workerDiagnostics;
    internal readonly string[] FileNames = fileNames;
    internal readonly OrderedMap<string, object?> Options = options;
    internal readonly Diagnostic[] Errors = errors;
}

// ported from: internal/outputpaths/outputpaths.go + internal/outputpaths/commonsourcedirectory.go
//
// 映射约定（docs/architecture.md §2/§3）：
// 1) Go 的包名 `outputpaths` 与本包唯一的导出类型 `OutputPaths` 同名，无法再建一个静态类来放包级函数
//    （同一命名空间内撞名）；这里把包级函数落成 `OutputPaths` 类型上的 static 成员（与 Glob 包同样的偏离）。
//    调用点仍是 `OutputPaths.GetOutputJSFileName(...)` 的形状。
// 2) Go 的 `OutputPathsHost` 接口（导出）→ 同名 C# 接口（docs/ast.md §2：导出接口保留 Go 名）。
// 3) `OutputPaths` 结构体的未导出字段 → `internal _camelCase`；`ForceEmitPaths` 是导出结构体、字段导出 → class + public 字段
//    （Go 的 `ForceEmitPaths{Dts: x}` 复合字面量 → C# 对象初始化器）。
// 4) `tspath.GetAnyExtensionFromPath(path, nil, false)` 的 nil 扩展名列表 → C# 传 `[]`（C# 的该函数签名是非空数组，
//    内部 `extensions.Length > 0` 与 Go 的 `len(extensions) > 0` 等价）。
// 5) `GetSourceFilePathInNewDir` 只是 `GetSourceFilePathInNewDirWorker` 的转发（Go 保留两层），照抄。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.OutputPaths;

public interface OutputPathsHost
{
    string CommonSourceDirectory();
    string[] ContentMapperExtensions();
    string GetCurrentDirectory();
    bool UseCaseSensitiveFileNames();
}

public sealed class OutputPaths : TypeScript.Transformers.Declarations.OutputPaths
{
    internal string _jsFilePath = "";
    internal string _sourceMapFilePath = "";
    internal string _declarationFilePath = "";
    internal string _declarationMapPath = "";

    // DeclarationFilePath implements declarations.OutputPaths.
    public string DeclarationFilePath() => _declarationFilePath;

    // JsFilePath implements declarations.OutputPaths.
    public string JsFilePath() => _jsFilePath;

    public string SourceMapFilePath() => _sourceMapFilePath;

    public string DeclarationMapPath() => _declarationMapPath;

    public static OutputPaths GetOutputPathsFor(SourceFile sourceFile, CompilerOptions options, OutputPathsHost host, ForceEmitPaths force)
    {
        var ownOutputFilePath = GetOwnEmitOutputFilePath(sourceFile.FileName(), options, host, GetOutputExtension(sourceFile.FileName(), options.Jsx));
        var isJsonFile = IsJsonSourceFile(sourceFile);
        // If json file emits to the same location skip writing it, if emitDeclarationOnly skip writing it
        var isJsonEmittedToSameLocation = isJsonFile &&
            ComparePaths(sourceFile.FileName(), ownOutputFilePath, new ComparePathsOptions(
                host.UseCaseSensitiveFileNames(),
                host.GetCurrentDirectory())) == 0;
        var paths = new OutputPaths();
        if (sourceFile.ContentMapper() == "" && (force.Js || options.EmitDeclarationOnly != Tristate.TSTrue) && !isJsonEmittedToSameLocation)
        {
            paths._jsFilePath = ownOutputFilePath;
            if (!IsJsonSourceFile(sourceFile))
            {
                paths._sourceMapFilePath = GetSourceMapFilePath(paths._jsFilePath, options);
            }
        }
        if (force.Dts || options.GetEmitDeclarations() && !isJsonFile)
        {
            paths._declarationFilePath = GetDeclarationEmitOutputFilePath(sourceFile.FileName(), options, host);
            if (options.GetAreDeclarationMapsEnabled() || force.DeclarationMap && options.DeclarationMap.IsTrue())
            {
                paths._declarationMapPath = paths._declarationFilePath + ".map";
            }
        }
        return paths;
    }

    public static bool ForEachEmittedFile(OutputPathsHost host, CompilerOptions options, Func<OutputPaths, SourceFile, bool> action, SourceFile[] sourceFiles, bool forceDtsEmit)
    {
        foreach (var sourceFile in sourceFiles)
        {
            if (action(GetOutputPathsFor(sourceFile, options, host, new ForceEmitPaths { Dts = forceDtsEmit }), sourceFile))
            {
                return true;
            }
        }
        return false;
    }

    public static string GetOutputJSFileName(string inputFileName, CompilerOptions options, OutputPathsHost host)
    {
        if (options.EmitDeclarationOnly.IsTrue() || IsContentMappedFileName(inputFileName, host))
        {
            return "";
        }
        var outputFileName = GetOutputJSFileNameWorker(inputFileName, options, host);
        if (!FileExtensionIs(outputFileName, ExtensionJson) ||
            ComparePaths(inputFileName, outputFileName, new ComparePathsOptions(
                host.UseCaseSensitiveFileNames(),
                host.GetCurrentDirectory())) != 0)
        {
            return outputFileName;
        }

        return "";
    }

    internal static bool IsContentMappedFileName(string fileName, OutputPathsHost host)
    {
        return GetLongestExtensionFromPath(fileName, host.ContentMapperExtensions(), !host.UseCaseSensitiveFileNames()) != "";
    }

    public static string GetOutputJSFileNameWorker(string inputFileName, CompilerOptions options, OutputPathsHost host)
    {
        return ChangeExtension(
            GetOutputPathWithoutChangingExtension(inputFileName, options.OutDir, host),
            GetOutputExtension(inputFileName, options.Jsx));
    }

    public static string GetOutputDeclarationFileNameWorker(string inputFileName, CompilerOptions options, OutputPathsHost host)
    {
        var dir = options.DeclarationDir;
        if (dir.Length == 0)
        {
            dir = options.OutDir;
        }
        return ChangeToDeclarationExtension(GetOutputPathWithoutChangingExtension(inputFileName, dir, host), host);
    }

    public static string GetOutputExtension(string fileName, JsxEmit jsx)
    {
        if (FileExtensionIs(fileName, ExtensionJson))
        {
            return ExtensionJson;
        }
        if (jsx == JsxEmit.Preserve && FileExtensionIsOneOf(fileName, [ExtensionJsx, ExtensionTsx]))
        {
            return ExtensionJsx;
        }
        if (FileExtensionIsOneOf(fileName, [ExtensionMts, ExtensionMjs]))
        {
            return ExtensionMjs;
        }
        if (FileExtensionIsOneOf(fileName, [ExtensionCts, ExtensionCjs]))
        {
            return ExtensionCjs;
        }
        return ExtensionJs;
    }

    public static string GetDeclarationEmitOutputFilePath(string file, CompilerOptions options, OutputPathsHost host)
    {
        string? outputDir = null;
        if (options.DeclarationDir.Length > 0)
        {
            outputDir = options.DeclarationDir;
        }
        else if (options.OutDir.Length > 0)
        {
            outputDir = options.OutDir;
        }

        string path;
        if (outputDir is not null)
        {
            path = GetSourceFilePathInNewDirWorker(file, outputDir, host.GetCurrentDirectory(), host.CommonSourceDirectory(), host.UseCaseSensitiveFileNames());
        }
        else
        {
            path = file;
        }
        return ChangeToDeclarationExtension(path, host);
    }

    public static string ChangeToDeclarationExtension(string path, OutputPathsHost host)
    {
        var extension = GetLongestExtensionFromPath(path, host.ContentMapperExtensions(), false);
        if (extension != "")
        {
            return RemoveExtension(path, extension) + ".d" + extension + ".ts";
        }
        var pathWithoutExtension = RemoveFileExtension(path);
        if (pathWithoutExtension == path)
        {
            var anyExtension = GetAnyExtensionFromPath(path, [], false);
            if (anyExtension != "")
            {
                pathWithoutExtension = RemoveExtension(path, anyExtension);
            }
        }
        return pathWithoutExtension + GetDeclarationEmitExtensionForPath(path);
    }

    public static string GetSourceFilePathInNewDir(string fileName, string newDirPath, string currentDirectory, string commonSourceDirectory, bool useCaseSensitiveFileNames)
    {
        return GetSourceFilePathInNewDirWorker(fileName, newDirPath, currentDirectory, commonSourceDirectory, useCaseSensitiveFileNames);
    }

    internal static string GetOutputPathWithoutChangingExtension(string inputFileName, string outputDirectory, OutputPathsHost host)
    {
        if (outputDirectory.Length > 0)
        {
            return ResolvePath(outputDirectory, GetRelativePathFromDirectory(host.CommonSourceDirectory(), inputFileName, new ComparePathsOptions(
                host.UseCaseSensitiveFileNames(),
                host.GetCurrentDirectory())));
        }
        return inputFileName;
    }

    public static string GetSourceFilePathInNewDirWorker(string fileName, string newDirPath, string currentDirectory, string commonSourceDirectory, bool useCaseSensitiveFileNames)
    {
        var sourceFilePath = GetNormalizedAbsolutePath(fileName, currentDirectory);
        if (TryTrimFilePathPrefix(sourceFilePath, commonSourceDirectory, useCaseSensitiveFileNames, out var trimmed))
        {
            sourceFilePath = trimmed;
        }
        return CombinePaths(newDirPath, sourceFilePath);
    }

    internal static string GetOwnEmitOutputFilePath(string fileName, CompilerOptions options, OutputPathsHost host, string extension)
    {
        string emitOutputFilePathWithoutExtension;
        if (options.OutDir.Length > 0)
        {
            var currentDirectory = host.GetCurrentDirectory();
            emitOutputFilePathWithoutExtension = RemoveFileExtension(GetSourceFilePathInNewDir(
                fileName,
                options.OutDir,
                currentDirectory,
                host.CommonSourceDirectory(),
                host.UseCaseSensitiveFileNames()));
        }
        else
        {
            emitOutputFilePathWithoutExtension = RemoveFileExtension(fileName);
        }
        return emitOutputFilePathWithoutExtension + extension;
    }

    public static string GetSourceMapFilePath(string jsFilePath, CompilerOptions options)
    {
        if (options.SourceMap.IsTrue() && !options.InlineSourceMap.IsTrue())
        {
            return jsFilePath + ".map";
        }
        return "";
    }

    public static string GetBuildInfoFileName(CompilerOptions options, ComparePathsOptions opts)
    {
        if (!options.IsIncremental() && !options.Build.IsTrue())
        {
            return "";
        }
        if (options.TsBuildInfoFile != "")
        {
            return options.TsBuildInfoFile;
        }
        if (options.ConfigFilePath == "")
        {
            return "";
        }
        var configFileExtensionLess = RemoveFileExtension(options.ConfigFilePath);
        string buildInfoExtensionLess;
        if (options.OutDir != "")
        {
            if (options.RootDir != "")
            {
                buildInfoExtensionLess = ResolvePath(options.OutDir, GetRelativePathFromDirectory(options.RootDir, configFileExtensionLess, opts));
            }
            else
            {
                buildInfoExtensionLess = CombinePaths(options.OutDir, GetBaseFileName(configFileExtensionLess));
            }
        }
        else
        {
            buildInfoExtensionLess = configFileExtensionLess;
        }
        return buildInfoExtensionLess + ExtensionTsBuildInfo;
    }

    // ---- commonsourcedirectory.go ----

    internal static string ComputeCommonSourceDirectoryOfFilenames(string[] fileNames, string currentDirectory, bool useCaseSensitiveFileNames)
    {
        string[]? commonPathComponents = null;
        foreach (var sourceFile in fileNames)
        {
            // Each file contributes into common source file path
            var sourcePathComponents = GetNormalizedPathComponents(sourceFile, currentDirectory);

            // The base file name is not part of the common directory path
            sourcePathComponents = sourcePathComponents[..^1];

            if (commonPathComponents is null)
            {
                // first file
                commonPathComponents = sourcePathComponents;
                continue;
            }

            var n = Math.Min(commonPathComponents.Length, sourcePathComponents.Length);
            for (var i = 0; i < n; i++)
            {
                if (GetCanonicalFileName(commonPathComponents[i], useCaseSensitiveFileNames) != GetCanonicalFileName(sourcePathComponents[i], useCaseSensitiveFileNames))
                {
                    if (i == 0)
                    {
                        // Failed to find any common path component
                        return "";
                    }

                    // New common path found that is 0 -> i-1
                    commonPathComponents = commonPathComponents[..i];
                    break;
                }
            }

            // If the sourcePathComponents was shorter than the commonPathComponents, truncate to the sourcePathComponents
            if (sourcePathComponents.Length < commonPathComponents.Length)
            {
                commonPathComponents = commonPathComponents[..sourcePathComponents.Length];
            }
        }

        if (commonPathComponents is null || commonPathComponents.Length == 0)
        {
            // Can happen when all input files are .d.ts files
            return currentDirectory;
        }

        return GetPathFromPathComponents(commonPathComponents);
    }

    public static string GetComputedCommonSourceDirectory(string[] emittedFiles, string currentDirectory, bool useCaseSensitiveFileNames)
    {
        var commonSourceDirectory = ComputeCommonSourceDirectoryOfFilenames(emittedFiles, currentDirectory, useCaseSensitiveFileNames);
        if (commonSourceDirectory.Length > 0)
        {
            commonSourceDirectory = EnsureTrailingDirectorySeparator(commonSourceDirectory);
        }
        return commonSourceDirectory;
    }

    public static string GetCommonSourceDirectory(
        CompilerOptions options,
        Func<string[]> files,
        string currentDirectory,
        bool useCaseSensitiveFileNames,
        Func<string[], string, bool>? checkSourceFilesBelongToPath)
    {
        string commonSourceDirectory;
        if (options.RootDir != "")
        {
            // If a rootDir is specified use it as the commonSourceDirectory
            commonSourceDirectory = options.RootDir;
            if (checkSourceFilesBelongToPath is not null)
            {
                checkSourceFilesBelongToPath(files(), options.RootDir);
            }
        }
        else if (options.ConfigFilePath != "")
        {
            // If the rootDir is not specified, then the common source directory is the directory of the config file.
            commonSourceDirectory = GetDirectoryPath(options.ConfigFilePath);
            if (checkSourceFilesBelongToPath is not null)
            {
                checkSourceFilesBelongToPath(files(), commonSourceDirectory);
            }
        }
        else
        {
            commonSourceDirectory = ComputeCommonSourceDirectoryOfFilenames(files(), currentDirectory, useCaseSensitiveFileNames);
        }

        if (commonSourceDirectory.Length > 0)
        {
            // Make sure directory path ends with directory separator so this string can directly
            // used to replace with "" to get the relative path of the source file and the relative path doesn't
            // start with / making it rooted path
            commonSourceDirectory = EnsureTrailingDirectorySeparator(commonSourceDirectory);
        }

        return commonSourceDirectory;
    }
}

public sealed class ForceEmitPaths
{
    public bool Dts;
    public bool Js;
    public bool DeclarationMap;
}

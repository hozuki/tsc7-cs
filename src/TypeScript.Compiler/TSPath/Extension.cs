// ported from: internal/tspath/extension.go
//
// 与 Go 的差异（都不改变语义）：
// - Go 的 unexported 包级变量（supportedTSExtensionsForExtractExtension/extensionsToRemove）与
//   unexported 函数映射成 private（Go 的包内可见性在 C# 里是上限，私有即可）。
// - Go 的 `[]string` 参数在调用点可能传 nil；nil 与空 slice 在本文件里只参与 len()/range，行为相同，
//   所以 C# 签名用非空的 `string[]`，调用点传 `[]`。
// - slices.Concat 用 C# 的集合表达式 `[.. a, [x]]` 表达。
// - Go 的 `slices.Contains` 用手写循环（等价，且不为 string 数组引入额外分配）。
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.TSPath;

public static partial class TSPath
{
    public const string ExtensionTs = ".ts";
    public const string ExtensionTsx = ".tsx";
    public const string ExtensionDts = ".d.ts";
    public const string ExtensionJs = ".js";
    public const string ExtensionJsx = ".jsx";
    public const string ExtensionJson = ".json";
    public const string ExtensionTsBuildInfo = ".tsbuildinfo";
    public const string ExtensionMjs = ".mjs";
    public const string ExtensionMts = ".mts";
    public const string ExtensionDmts = ".d.mts";
    public const string ExtensionCjs = ".cjs";
    public const string ExtensionCts = ".cts";
    public const string ExtensionDcts = ".d.cts";

    public static readonly string[] SupportedDeclarationExtensions = [ExtensionDts, ExtensionDcts, ExtensionDmts];
    public static readonly string[] SupportedTSImplementationExtensions = [ExtensionTs, ExtensionTsx, ExtensionMts, ExtensionCts];
    private static readonly string[] _supportedTSExtensionsForExtractExtension = [ExtensionDts, ExtensionDcts, ExtensionDmts, ExtensionTs, ExtensionTsx, ExtensionMts, ExtensionCts];
    public static readonly string[][] AllSupportedExtensions = [[ExtensionTs, ExtensionTsx, ExtensionDts, ExtensionJs, ExtensionJsx], [ExtensionCts, ExtensionDcts, ExtensionCjs], [ExtensionMts, ExtensionDmts, ExtensionMjs]];
    public static readonly string[][] SupportedTSExtensions = [[ExtensionTs, ExtensionTsx, ExtensionDts], [ExtensionCts, ExtensionDcts], [ExtensionMts, ExtensionDmts]];
    public static readonly string[] SupportedTSExtensionsFlat = [ExtensionTs, ExtensionTsx, ExtensionDts, ExtensionCts, ExtensionDcts, ExtensionMts, ExtensionDmts];
    public static readonly string[][] SupportedJSExtensions = [[ExtensionJs, ExtensionJsx], [ExtensionMjs], [ExtensionCjs]];
    public static readonly string[] SupportedJSExtensionsFlat = [ExtensionJs, ExtensionJsx, ExtensionMjs, ExtensionCjs];
    // 静态字段按声明顺序初始化，所以下面几个必须在 AllSupportedExtensions/SupportedTSExtensions… 之后声明。
    public static readonly string[][] AllSupportedExtensionsWithJson = [.. AllSupportedExtensions, [ExtensionJson]];
    public static readonly string[][] SupportedTSExtensionsWithJson = [.. SupportedTSExtensions, [ExtensionJson]];
    public static readonly string[] SupportedTSExtensionsWithJsonFlat = [.. SupportedTSExtensionsFlat, ExtensionJson];
    public static readonly string[] ExtensionsNotSupportingExtensionlessResolution = [ExtensionMts, ExtensionDmts, ExtensionMjs, ExtensionCts, ExtensionDcts, ExtensionCjs];

    public static bool ExtensionIsTs(string ext)
        => ext == ExtensionTs
            || ext == ExtensionTsx
            || ext == ExtensionDts
            || ext == ExtensionMts
            || ext == ExtensionDmts
            || ext == ExtensionCts
            || ext == ExtensionDcts
            || ext.Length >= 7 && ext.StartsWith(".d.", StringComparison.Ordinal) && ext.EndsWith(".ts", StringComparison.Ordinal);

    private static readonly string[] _extensionsToRemove = [ExtensionDts, ExtensionDmts, ExtensionDcts, ExtensionMjs, ExtensionMts, ExtensionCjs, ExtensionCts, ExtensionTs, ExtensionJs, ExtensionTsx, ExtensionJsx, ExtensionJson];

    public static string RemoveFileExtension(string path)
    {
        // Remove any known extension even if it has more than one dot
        foreach (var ext in _extensionsToRemove)
        {
            if (path.EndsWith(ext, StringComparison.Ordinal))
            {
                return path[..^ext.Length];
            }
        }

        return path;
    }

    public static string RemoveAnyFileExtension(string path)
    {
        var withoutExtension = RemoveFileExtension(path);
        if (withoutExtension != path)
        {
            return withoutExtension;
        }

        var extension = GetAnyExtensionFromPath(path, [], false);
        if (extension.Length != 0)
        {
            return RemoveExtension(path, extension);
        }

        return path;
    }

    public static string TryGetExtensionFromPath(string p)
    {
        foreach (var ext in _extensionsToRemove)
        {
            if (FileExtensionIs(p, ext))
            {
                return ext;
            }
        }

        return "";
    }

    public static string RemoveExtension(string path, string extension) => path[..^extension.Length];

    public static bool FileExtensionIsOneOf(string path, string[] extensions)
    {
        foreach (var ext in extensions)
        {
            if (FileExtensionIs(path, ext))
            {
                return true;
            }
        }

        return false;
    }

    public static string TryExtractTSExtension(string fileName)
    {
        foreach (var ext in _supportedTSExtensionsForExtractExtension)
        {
            if (FileExtensionIs(fileName, ext))
            {
                return ext;
            }
        }

        return "";
    }

    public static bool HasTSFileExtension(string path) => FileExtensionIsOneOf(path, SupportedTSExtensionsFlat);

    public static bool HasImplementationTSFileExtension(string path)
        => FileExtensionIsOneOf(path, SupportedTSImplementationExtensions) && !IsDeclarationFileName(path);

    public static bool HasJSFileExtension(string path) => FileExtensionIsOneOf(path, SupportedJSExtensionsFlat);

    public static bool HasJSONFileExtension(string path) => FileExtensionIs(path, ExtensionJson);

    public static bool IsDeclarationFileName(string fileName) => GetDeclarationFileExtension(fileName).Length != 0;

    public static bool ExtensionIsOneOf(string ext, string[] extensions)
    {
        // Go: slices.Contains
        foreach (var candidate in extensions)
        {
            if (candidate == ext)
            {
                return true;
            }
        }

        return false;
    }

    public static string GetDeclarationFileExtension(string fileName)
    {
        var @base = GetBaseFileName(fileName);
        foreach (var ext in SupportedDeclarationExtensions)
        {
            if (@base.EndsWith(ext, StringComparison.Ordinal))
            {
                return ext;
            }
        }

        if (@base.EndsWith(ExtensionTs, StringComparison.Ordinal))
        {
            var index = @base.IndexOf(".d.", StringComparison.Ordinal);
            if (index >= 0)
            {
                return @base[index..];
            }
        }

        return "";
    }

    public static string GetDeclarationEmitExtensionForPath(string path)
    {
        if (FileExtensionIsOneOf(path, [ExtensionMjs, ExtensionMts]))
        {
            return ExtensionDmts;
        }

        if (FileExtensionIsOneOf(path, [ExtensionCjs, ExtensionCts]))
        {
            return ExtensionDcts;
        }

        if (FileExtensionIsOneOf(path, [ExtensionTs, ExtensionTsx, ExtensionJs, ExtensionJsx]))
        {
            return ExtensionDts;
        }

        var ext = GetAnyExtensionFromPath(path, [], false);
        if (ext.Length != 0)
        {
            return ".d" + ext + ".ts";
        }

        return ExtensionDts;
    }

    // ChangeAnyExtension changes the extension of a path to the provided extension if it has one of the provided extensions.
    //
    // ChangeAnyExtension("/path/to/file.ext", ".js", ".ext") === "/path/to/file.js"
    // ChangeAnyExtension("/path/to/file.ext", ".js", ".ts") === "/path/to/file.ext"
    // ChangeAnyExtension("/path/to/file.ext", ".js", [".ext", ".ts"]) === "/path/to/file.js"
    public static string ChangeAnyExtension(string path, string ext, string[] extensions, bool ignoreCase)
    {
        var pathext = GetAnyExtensionFromPath(path, extensions, ignoreCase);
        if (pathext.Length != 0)
        {
            var result = path[..^pathext.Length];
            if (ext.Length == 0)
            {
                return result;
            }

            if (ext.StartsWith(".", StringComparison.Ordinal))
            {
                return result + ext;
            }

            return result + "." + ext;
        }

        return path;
    }

    public static string ChangeExtension(string path, string newExtension)
        => ChangeAnyExtension(path, newExtension, _extensionsToRemove, false /*ignoreCase*/);

    // Like `changeAnyExtension`, but declaration file extensions are recognized
    // and replaced starting from the `.d`.
    //
    //	changeAnyExtension("file.d.ts", ".js") === "file.d.js"
    //	changeFullExtension("file.d.ts", ".js") === "file.js"
    public static string ChangeFullExtension(string path, string newExtension)
    {
        var declarationExtension = GetDeclarationFileExtension(path);
        if (declarationExtension.Length != 0)
        {
            var ext = newExtension;
            if (!ext.StartsWith(".", StringComparison.Ordinal))
            {
                ext = "." + ext;
            }

            return path[..^declarationExtension.Length] + ext;
        }

        return ChangeExtension(path, newExtension);
    }

    public static string[] GetPossibleOriginalInputExtensionForExtension(string path)
    {
        if (FileExtensionIsOneOf(path, [ExtensionDmts, ExtensionMjs, ExtensionMts]))
        {
            return [ExtensionMts, ExtensionMjs];
        }

        if (FileExtensionIsOneOf(path, [ExtensionDcts, ExtensionCjs, ExtensionCts]))
        {
            return [ExtensionCts, ExtensionCjs];
        }

        // Handle any custom .d.x.ts extension (e.g., .d.json.ts -> .json, .d.css.ts -> .css)
        var ext = GetDeclarationFileExtension(path);
        if (ext.Length != 0 && ext != ExtensionDts)
        {
            var inner = ext[".d.".Length..^".ts".Length];
            return ["." + inner];
        }

        return [ExtensionTsx, ExtensionTs, ExtensionJsx, ExtensionJs];
    }
}

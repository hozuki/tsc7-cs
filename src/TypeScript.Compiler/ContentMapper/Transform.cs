// ported from: internal/contentmapper/transform.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `(SourceFiles, error)` → C# 正常返回 `SourceFiles`、失败抛异常（docs/architecture.md §3.2）；
//    这里直接抛 `spanmap.MappingError`（Main 已批准把它的基类改成 Exception，Go 的 `return SourceFiles{}, problem`
//    = C# 的 `throw problem`，调用方 `errors.AsType[*spanmap.MappingError]` = `catch (MappingError)`）。
// 2) Go 的 `ast.SourceFileParseOptions` 是**值类型**（`ParseResult` 收的是副本，内部再派生 canonical/supplemental
//    各自的副本）；C# 的 `SourceFileParseOptions` 是可变类，所以这里用 <see cref="CloneParseOptions"/> 显式复制，
//    复刻 Go 的值语义（否则 supplemental 的文件名后缀会改到 canonical 的选项上——Go 的
//    `TestParseResultDoesNotLeakCanonicalModuleForcingToSupplementals` 就在测这个）。
// 3) `func isModuleVirtualExtension` 是未导出函数 → C# 的 private static（所在类型是包级静态类 ContentMapper）。
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.SpanMap;
using static TypeScript.Core.Core;
// `ParseSourceFile` / `ExtensionMts` 等是包级静态类成员 → 必须 using static 后裸调用（docs/architecture.md §2）。
using static TypeScript.Parser.Parser;
using static TypeScript.TSPath.TSPath;
// ImplicitUsings 会带进 System.IO.Path，裸写 Path 是 CS0104 → 显式别名（同 Ast/ParseOptions.cs）。
using Path = TypeScript.TSPath.Path;

namespace TypeScript.ContentMapper;

/// <summary>Go: <c>type SourceFiles struct</c>（规范输出 + 它的无名补充输入）。</summary>
public sealed class SourceFiles
{
    public SourceFile? Canonical;
    public SourceFile[] Supplemental = [];
}

public static partial class ContentMapper
{
    /// <summary>
    /// Go: <c>func TransformAndParse(parseOptions ast.SourceFileParseOptions, content string, mapper *Mapper,
    /// project Project) (SourceFiles, error)</c>。跑 mapper 的 transform 并解析得到的 TypeScript，保留原文件名、
    /// 在源文件上保留未变换的文本。失败时抛 <see cref="TransformError"/>（或来自 project 的异常）。
    /// </summary>
    public static SourceFiles TransformAndParse(
        SourceFileParseOptions parseOptions,
        string content,
        Mapper mapper,
        Project project)
    {
        var transformIdentity = project.Identity(mapper);
        var result = project.Transform(mapper, new Request { FileName = parseOptions.FileName, Content = content });
        return ParseResult(parseOptions, content, mapper, transformIdentity, result);
    }

    /// <summary>Go: <c>func ParseResult(parseOptions ast.SourceFileParseOptions, content string, mapper *Mapper,
    /// transformIdentity string, result Result) (SourceFiles, error)</c>。校验并解析一个 mapper 结果及其全部补充输出。</summary>
    public static SourceFiles ParseResult(
        SourceFileParseOptions parseOptions,
        string content,
        Mapper mapper,
        string transformIdentity,
        Result result)
    {
        if (result.Mappings is null)
        {
            throw new TransformError(TransformErrorKind.Mappings, null);
        }

        if (result.Mappings.Validate(result.Text, content) is { } problem)
        {
            throw problem;
        }

        var virtualExtension = result.VirtualExtension;
        if (!IsSupportedVirtualExtension(virtualExtension))
        {
            throw new TransformError(TransformErrorKind.Response, null);
        }

        // Go 的 `baseParseOptions := parseOptions` 是结构体复制；C# 必须显式复制（见文件头第 2 条）。
        var baseParseOptions = CloneParseOptions(parseOptions);
        var virtualFileName = baseParseOptions.FileName + virtualExtension;
        var canonicalOptions = CloneParseOptions(baseParseOptions);
        if (IsModuleVirtualExtension(virtualExtension))
        {
            canonicalOptions.ExternalModuleIndicatorOptions.Force = true;
        }

        var sourceFile = ParseSourceFile(canonicalOptions, result.Text, GetScriptKindFromFileName(virtualFileName));
        if (result.Diagnostics.Length > 0)
        {
            // runner 产生诊断时还没有源文件；这里把它们关联到这个文件，以便针对它报告。
            foreach (var diagnostic in result.Diagnostics)
            {
                diagnostic.SetFile(sourceFile);
            }

            var existing = sourceFile.Diagnostics();
            var merged = new Ast.Diagnostic[existing.Length + result.Diagnostics.Length];
            existing.CopyTo(merged, 0);
            result.Diagnostics.CopyTo(merged, existing.Length);
            sourceFile.SetDiagnostics(merged);
        }

        var files = new SourceFiles { Canonical = sourceFile, Supplemental = new SourceFile[result.Supplemental.Length] };
        for (var i = 0; i < result.Supplemental.Length; i++)
        {
            var supplemental = result.Supplemental[i];
            if (supplemental.Mappings is null)
            {
                throw new TransformError(TransformErrorKind.Mappings, null);
            }

            if (supplemental.Mappings.Validate(supplemental.Text, content) is { } supplementalProblem)
            {
                throw supplementalProblem;
            }

            var supplementalOptions = CloneParseOptions(baseParseOptions);
            if (!IsSupportedVirtualExtension(supplemental.VirtualExtension))
            {
                throw new TransformError(TransformErrorKind.Response, null);
            }

            var suffix = "." + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + supplemental.VirtualExtension;
            supplementalOptions.FileName += suffix;
            supplementalOptions.Path = (Path)(canonicalOptions.Path.Value + suffix);
            if (IsModuleVirtualExtension(supplemental.VirtualExtension))
            {
                supplementalOptions.ExternalModuleIndicatorOptions.Force = true;
            }

            var file = ParseSourceFile(supplementalOptions, supplemental.Text, GetScriptKindFromFileName(supplementalOptions.FileName));
            files.Supplemental[i] = file;
        }

        var mapperIdentity = mapper.Identity();
        sourceFile.SetContentMapperInfo(new ContentMapperSourceFileInfo
        {
            ContentMapper = mapperIdentity,
            TransformIdentity = transformIdentity,
            ParseOptions = baseParseOptions,
            VirtualFileName = virtualFileName,
            OriginalText = content,
            SpanMap = result.Mappings,
            DiagnosticDirectives = result.DiagnosticDirectives,
            SupplementalSourceFiles = files.Supplemental,
        });
        for (var i = 0; i < files.Supplemental.Length; i++)
        {
            var supplemental = result.Supplemental[i];
            files.Supplemental[i].SetContentMapperInfo(new ContentMapperSourceFileInfo
            {
                ContentMapper = mapperIdentity,
                TransformIdentity = transformIdentity,
                ParseOptions = baseParseOptions,
                VirtualFileName = files.Supplemental[i].FileName(),
                OriginalText = content,
                SpanMap = supplemental.Mappings,
                DiagnosticDirectives = supplemental.DiagnosticDirectives,
                CanonicalSourceFile = sourceFile,
            });
        }

        return files;
    }

    /// <summary>C# 侧新增：复刻 Go 的 <c>SourceFileParseOptions</c> 值复制（含内嵌的
    /// <c>ExternalModuleIndicatorOptions</c> 结构体复制）。</summary>
    internal static SourceFileParseOptions CloneParseOptions(SourceFileParseOptions parseOptions) => new()
    {
        FileName = parseOptions.FileName,
        Path = parseOptions.Path,
        ExternalModuleIndicatorOptions = new ExternalModuleIndicatorOptions
        {
            JSX = parseOptions.ExternalModuleIndicatorOptions.JSX,
            Force = parseOptions.ExternalModuleIndicatorOptions.Force,
        },
    };

    private static bool IsModuleVirtualExtension(string extension) =>
        extension is ExtensionMts or ExtensionCts or ExtensionMjs or ExtensionCjs;

    /// <summary>Go: <c>func CheckSupplementalFileNameCollisions(files SourceFiles, fileExists func(string) bool) error</c>。
    /// 拒绝命名了真实物理文件的编译器分配虚拟文件名。</summary>
    public static void CheckSupplementalFileNameCollisions(SourceFiles files, Func<string, bool> fileExists)
    {
        foreach (var file in files.Supplemental)
        {
            if (fileExists(file.FileName()))
            {
                throw new SupplementalFileCollisionError(file.FileName());
            }
        }
    }
}

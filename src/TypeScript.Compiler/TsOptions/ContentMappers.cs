// ported from: internal/tsoptions/contentmappers.go
//
// 映射约定（docs/architecture.md §2/§3.2）：
// 1) Go 的 `(contentmapper.Manifest, string, *ast.Diagnostic)` 三返回值 → C# 元组（与 tsconfigparsing.go 分片约定的一致形状）。
// 2) `Expected[T].GetValue() (T, bool)` → `bool TryGetValue(out T)`（§3.2）；`packagejson.Parse(data []byte) (Fields, error)`
//    的第一个错误是"JSON 解析失败"，属于预期内失败 → `bool TryParse(...)`。
// 3) **本文件依赖另外三个分片尚未落地的类型**（module / packagejson / contentmapper）：下面的调用按 Go 名字 + §3 约定写成，
//    若对方落地的形状不同，只需要改这里的几行（见报告"unverified"）。
//    - `TypeScript.Module`：`Resolver`（class）、`ResolverOptions`（Host/CompilerOptions 字段）、
//      `Resolver.ResolvePackageDirectory(packageName, containingFile, resolutionMode, redirectedReference)` →
//      可空的解析结果（`ResolvedFileName` 字段）。
//    - `TypeScript.PackageJson`：`PackageJson.TryParse(string, out Fields)`、`Fields.Name/Version`（`Expected<string>`）、
//      `Fields.ContentMapper`（`Expected<ContentMapperFields>`）、`ContentMapperFields.Exec/CompilerOptions/DynamicConfig`。
//    - `TypeScript.ContentMapper`：`Manifest`（Name/Version/Exec/CompilerOptions/DynamicConfig 字段，Go 的复合字面量 → 对象初始化器）。
// 4) Go 的 `resolved == nil || resolved.ResolvedFileName == ""` → C# 的 `is null` 判断保留（解析结果是可空的 class）。
using System.Text;
using TypeScript.Ast;
using TypeScript.ContentMapper;
using Diagnostic = TypeScript.Ast.Diagnostic; // Ast 与 ContentMapper 各有一个 Diagnostic（Go 里也是两个包）→ 别名消歧
using TypeScript.Core;
using TypeScript.Module;
using static TypeScript.Core.Core;
using static TypeScript.Module.Module;
using static TypeScript.PackageJson.PackageJson;
using static TypeScript.Ast.Ast;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    /// <summary>
    /// resolveContentMapperManifest locates packageName in node_modules (walking up from the directory of
    /// containingFile via node module resolution) and reads its package.json to produce the mapper's manifest
    /// and package directory. It never executes the package. On failure it returns a diagnostic describing why
    /// the mapper could not be resolved; on success the diagnostic is null.
    /// </summary>
    internal static (Manifest Manifest, string PackageDirectory, Diagnostic? Diagnostic) ResolveContentMapperManifest(
        ParseConfigHost host,
        string containingFile,
        string packageName)
    {
        var resolver = NewResolver(new ResolverOptions
        {
            Host = new ResolverHost(host),
            CompilerOptions = new CompilerOptions { ModuleResolution = ModuleResolutionKind.Bundler },
        });
        var resolved = resolver.ResolvePackageDirectory(packageName, containingFile, ResolutionModeNone, null);
        if (resolved is null || resolved.ResolvedFileName == "")
        {
            return (default!, "", NewCompilerDiagnostic(The_content_mapper_package_0_could_not_be_resolved, packageName));
        }
        var packageDirectory = resolved.ResolvedFileName;

        var packageJsonPath = CombinePaths(packageDirectory, "package.json");
        if (!host.FS().TryReadFile(packageJsonPath, out var contents))
        {
            return (default!, packageDirectory, NewCompilerDiagnostic(The_content_mapper_package_0_could_not_be_resolved, packageName));
        }
        if (!TryParse(Encoding.UTF8.GetBytes(contents), out var fields))
        {
            return (default!, packageDirectory, NewCompilerDiagnostic(The_package_json_of_the_content_mapper_package_0_could_not_be_parsed, packageName));
        }
        fields.HeaderFields.Name.TryGetValue(out var name);
        if (name == "")
        {
            return (default!, packageDirectory, NewCompilerDiagnostic(The_package_json_of_the_content_mapper_package_0_does_not_specify_a_name, packageName));
        }
        fields.HeaderFields.Version.TryGetValue(out var version);

        // A content mapper package must declare how to run it: a "typescript.contentMapper" object with a non-empty
        // "exec" array of strings.
        if (!fields.ContentMapper.TryGetValue(out var cm))
        {
            return (default!, packageDirectory, NewCompilerDiagnostic(The_package_json_of_the_content_mapper_package_0_does_not_declare_a_typescript_contentMapper_object, packageName));
        }
        if (!cm.Exec.TryGetValue(out var exec) || exec.Length == 0)
        {
            return (default!, packageDirectory, NewCompilerDiagnostic(The_typescript_contentMapper_exec_of_the_content_mapper_package_0_must_be_a_non_empty_array_of_strings, packageName));
        }
        cm.CompilerOptions.TryGetValue(out var compilerOptions);
        cm.DynamicConfig.TryGetValue(out var dynamicConfig);
        // Go: `compilerOptions, _ := cm.CompilerOptions.GetValue()` 在字段缺失时是 nil 切片，而 Go 的
        // `len(nil) == 0` / `range nil` 都合法。C# 的 `Expected<T>.TryGetValue` 失败时 out 参数是 null
        // （见 PackageJson/Expected.cs 文件头第 6 条），落进 `Manifest.CompilerOptions` 会让后续
        // `CompilerOptions.Length`（内容映射器 transform identity）抛 NRE → 按 Go 语义非空化为 `[]`。
        return (new Manifest
        {
            Name = name,
            Version = version,
            Exec = exec,
            CompilerOptions = compilerOptions ?? [],
            DynamicConfig = dynamicConfig,
        }, packageDirectory, null);
    }
}

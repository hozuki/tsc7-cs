// ported from: internal/checker/checker.go:554-585（Program / Host 接口、nextCheckerID）
//
// 与 Go 的偏离：
// 1) `ast.HasFileName`/`ast.StringLiteralLike` 分别是 Ast 的接口与**类型别名**（Go 侧 `StringLiteralLike = Node`，
//    ast.go:1263），所以第二个参数直接是 `Node`（不生成别名类型；docs §3.2）。
// 2) Go 的 `(moduleReference string, specifier *ast.Node)` 多返回值 → C# 元组。
// 3) `nextCheckerID atomic.Uint32` → `private static int _nextCheckerId` + `Interlocked.Increment`
//    （§3.2：atomic.* → Interlocked）。
// 4) Program/Host 在 Go 里是导出接口 → C# `public interface`；方法名保持 Go 的 PascalCase 原名。
namespace TypeScript.Checker;

using System.Collections.Generic;
using System.Threading;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.ModuleSpecifiers;
using TypeScript.TsOptions;
using TypeScript.TSPath;

/// <summary>Go: <c>type Program interface { Host; ... }</c>（checker.go:554）。</summary>
public interface Program : Host
{
    CompilerOptions Options();

    SourceFile[] SourceFiles();

    void BindSourceFiles();

    new bool FileExists(string fileName);

    SourceFile? GetSourceFile(string fileName);

    SourceFile? GetSourceFileForResolvedModule(string fileName);

    ModuleKind GetEmitModuleFormatOfFile(HasFileName sourceFile);

    ModuleKind GetEmitSyntaxForUsageLocation(HasFileName sourceFile, Node usageLocation);

    ModuleKind GetImpliedNodeFormatForEmit(HasFileName sourceFile);

    ResolvedModule? GetResolvedModule(HasFileName currentSourceFile, string moduleReference, ModuleKind mode);

    Dictionary<Path, ModeAwareCache<ResolvedModule>> GetResolvedModules();

    Dictionary<string, bool> GetPackagesMap();

    SourceFileMetaData GetSourceFileMetaData(Path path);

    (string moduleReference, Node? specifier) GetJSXRuntimeImportSpecifier(Path path);

    Node? GetImportHelpersImportSpecifier(Path path);

    bool SourceFileMayBeEmitted(SourceFile sourceFile, bool forceDtsEmit);

    bool IsSourceFileDefaultLibrary(Path path);

    SourceOutputAndProjectReference? GetProjectReferenceFromOutputDts(Path path);

    ParsedCommandLine? GetRedirectForResolution(HasFileName file);

    new string CommonSourceDirectory();
}

/// <summary>Go: <c>type Host interface { modulespecifiers.ModuleSpecifierGenerationHost }</c>（checker.go:578）。</summary>
public interface Host : ModuleSpecifierGenerationHost
{
}

public sealed partial class Checker
{
    // Go: var nextCheckerID atomic.Uint32（checker.go:584）
    internal static int _nextCheckerId;
}

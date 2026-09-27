// ported from: internal/compiler/emitHost.go（143 行，全部）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `EmitHost` 接口**内嵌** `printer.EmitHost` + `declarations.DeclarationEmitHost`，并**再次列出**
//    6 个成员（Options/SourceFiles/UseCaseSensitiveFileNames/GetCurrentDirectory/CommonSourceDirectory/
//    IsEmitBlocked）。C# 用接口继承表达内嵌，重复列出的成员用 `new` 显式隐藏（与
//    ModuleSpecifiers/Types.cs 的 `ModuleSpecifierGenerationHost` 同款做法，保住 Go 的声明面）。
// 2) Go 未导出 struct `emitHost` 实现导出接口 `EmitHost` → `internal sealed class EmitHostImpl : EmitHost`
//    （仓库先例： Go `referenceResolver` → `ReferenceResolverImpl`）。
// 3) Go 未导出字段 `program`/`emitResolver` → `internal _program`/`internal _emitResolver`（§2：跨类访问用
//    internal 且仍带 `_`）。
// 4) `newEmitHost(ctx, program, file) (*emitHost, func())` → `EmitHostImpl.NewEmitHost(Context ctx, Program program,
//    SourceFile file)` 返回元组 `(EmitHostImpl Host, Action Done)`（Go 的 `func()` → `Action`；命名元组先例：
//    `Checker.NewChecker` 的 `(Checker, Lock)`、`Printer.GetEmitContext` 的 `(Context, Cleanup)`）。
//    Go 的 `newEmitHost` 是**包级构造函数**，C# 落在该类型的静态工厂上（仓库既有做法：Go 包级
//    `checker.NewChecker`/`tracer.NewTracer` → C# `Checker.NewChecker`/`Tracer.NewTracer`）；调用点是
//    T10Program 的 `Program.Emit`/`Program.GetDeclarationDiagnostics`（已是 `EmitHostImpl.NewEmitHost(...)`）。
// 5) `WriteFile(...) error` → `void WriteFile(...)`（docs §3.2：错误路径抛异常，由 FS 实现抛出）。
// 6) `ast.HasFileName` → `Ast.HasFileName`（接口）、`ast.StringLiteralLike` → Go 的类型别名（= Node），C# 没有
//    别名，所以参数类型直接写 `Node`（与 Checker.Program 的既有做法一致）。
// 7) `core.ResolutionMode` = `core.ModuleKind` 的**类型别名**（Go），C# 一律写 `ModuleKind`（CompilerOptions.cs 文件头第 2 条）。
// 8) `GetOutputPathsFor` 的返回类型是 declarations 包的 `OutputPaths` **接口**，实现值是
//    `TypeScript.OutputPaths.OutputPaths`。两个 `OutputPaths` 都不是 `TypeScript.Compiler` 的成员，直接写短名会
//    命中**命名空间** `TypeScript.OutputPaths`（docs §2 的实测坑），所以两者都用全限定名。
// 9) `host.program.resolver.ResolveModuleName(...)` 返回 `(resolved, trace, err)` → C# 返回元组
//    `(ResolvedModule? Module, DiagAndArgs[] Traces)`，只取第一项；`nil` 重定向引用 → `null`。
//    该方法是 Go 里的**死代码**（全仓无调用点，只被接口面覆盖），仍按 1:1 移植。
// 10) `outputpaths.ForceEmitPaths{Dts: forceDtsPaths}` → 对象初始化器 `new ForceEmitPaths { Dts = forceDtsPaths }`。
namespace TypeScript.Compiler;

using System;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.PackageJson;
using TypeScript.Printer;
using TypeScript.Symlinks;
using TypeScript.TsOptions;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using Path = TypeScript.TSPath.Path;

// NOTE: EmitHost operations must be thread-safe
/// <summary>
/// Go: <c>type EmitHost interface { printer.EmitHost; declarations.DeclarationEmitHost; … }</c>。<br/>
/// 额外列出 <see cref="SourceFileMayBeEmittedHost"/>：Go 里 `*emitHost` **结构性**同时满足这两个接口
/// （`sourceFileMayBeEmitted(file, host, …)` 收的就是 `SourceFileMayBeEmittedHost`），C# 没有结构性满足，
/// 故把这条 Go 的隐式子类型关系显式写出来——`EmitHost` 的方法集本来就覆盖
/// `SourceFileMayBeEmittedHost` 的全部成员（Options/SourceFiles/UseCaseSensitiveFileNames/
/// GetCurrentDirectory/GetProjectReferenceFromSource/IsSourceFileFromExternalLibrary）。
/// </summary>
public interface EmitHost : TypeScript.Printer.EmitHost, TypeScript.Transformers.Declarations.DeclarationEmitHost, SourceFileMayBeEmittedHost
{
    new CompilerOptions Options();

    new SourceFile[] SourceFiles();

    new bool UseCaseSensitiveFileNames();

    new string GetCurrentDirectory();

    new string CommonSourceDirectory();

    new bool IsEmitBlocked(string file);
}

// NOTE: emitHost operations must be thread-safe
/// <summary>Go: <c>type emitHost struct { program *Program; emitResolver printer.EmitResolver }</c>。</summary>
internal sealed class EmitHostImpl : EmitHost
{
    /// <summary>Go: 未导出字段 <c>program</c>。</summary>
    internal Program _program = null!;

    /// <summary>Go: 未导出字段 <c>emitResolver</c>。</summary>
    internal EmitResolver _emitResolver = null!;

    /// <summary>Go: <c>func newEmitHost(ctx context.Context, program *Program, file *ast.SourceFile) (*emitHost, func())</c>。</summary>
    public static (EmitHostImpl Host, Action Done) NewEmitHost(Context ctx, Program program, SourceFile file)
    {
        var (checker, done) = program.GetTypeCheckerForFile(ctx, file);
        return (new EmitHostImpl
        {
            _program = program,
            _emitResolver = checker.GetEmitResolver(),
        }, done);
    }

    public ModuleKind GetModeForUsageLocation(HasFileName file, Node moduleSpecifier)
    {
        return _program.GetModeForUsageLocation(file, moduleSpecifier);
    }

    public ResolvedModule? GetResolvedModuleFromModuleSpecifier(HasFileName file, Node moduleSpecifier)
    {
        return _program.GetResolvedModuleFromModuleSpecifier(file, moduleSpecifier);
    }

    public ModuleKind GetDefaultResolutionModeForFile(HasFileName file)
    {
        return _program.GetDefaultResolutionModeForFile(file);
    }

    public ModuleKind GetEmitModuleFormatOfFile(HasFileName file)
    {
        return _program.GetEmitModuleFormatOfFile(file);
    }

    public bool FileExists(string path)
    {
        return _program.FileExists(path);
    }

    public string GetGlobalTypingsCacheLocation()
    {
        return _program.GetGlobalTypingsCacheLocation();
    }

    public string GetNearestAncestorDirectoryWithPackageJson(string dirname)
    {
        return _program.GetNearestAncestorDirectoryWithPackageJson(dirname);
    }

    public InfoCacheEntry? GetPackageJsonInfo(string pkgJsonPath)
    {
        return _program.GetPackageJsonInfo(pkgJsonPath);
    }

    public string GetSourceOfProjectReferenceIfOutputIncluded(HasFileName file)
    {
        return _program.GetSourceOfProjectReferenceIfOutputIncluded(file);
    }

    public SourceOutputAndProjectReference? GetProjectReferenceFromSource(Path path)
    {
        return _program.GetProjectReferenceFromSource(path);
    }

    public string[] GetRedirectTargets(Path path)
    {
        return _program.GetRedirectTargets(path);
    }

    public ModifierFlags GetEffectiveDeclarationFlags(Node node, ModifierFlags flags)
    {
        return GetEmitResolver().GetEffectiveDeclarationFlags(node, flags);
    }

    public TypeScript.Transformers.Declarations.OutputPaths GetOutputPathsFor(SourceFile file, bool forceDtsPaths)
    {
        // TODO: cache
        return TypeScript.OutputPaths.OutputPaths.GetOutputPathsFor(
            file,
            Options(),
            this,
            new TypeScript.OutputPaths.ForceEmitPaths { Dts = forceDtsPaths });
    }

    public bool SourceFileMayBeEmitted(SourceFile file, bool forceDtsEmit)
    {
        return Compiler.SourceFileMayBeEmitted(file, this, forceDtsEmit, false);
    }

    public SourceFile? GetSourceFileFromReference(SourceFile origin, FileReference @ref)
    {
        return _program.GetSourceFileFromReference(origin, @ref);
    }

    public CompilerOptions Options() => _program.Options();

    public SourceFile[] SourceFiles() => _program.SourceFiles();

    public string GetCurrentDirectory() => _program.GetCurrentDirectory();

    public string CommonSourceDirectory() => _program.CommonSourceDirectory();

    public string[] ContentMapperExtensions()
    {
        return _program.ContentMapperExtensions();
    }

    public bool UseCaseSensitiveFileNames()
    {
        return _program.UseCaseSensitiveFileNames();
    }

    public bool IsEmitBlocked(string file)
    {
        return _program.IsEmitBlocked(file);
    }

    public void WriteFile(string fileName, string text)
    {
        _program.Host().FS().WriteFile(fileName, text);
    }

    public EmitResolver GetEmitResolver()
    {
        return _emitResolver;
    }

    public bool IsSourceFileFromExternalLibrary(SourceFile file)
    {
        return _program.IsSourceFileFromExternalLibrary(file);
    }

    public KnownSymlinks? GetSymlinkCache()
    {
        return _program.GetSymlinkCache();
    }

    public ResolvedModule? ResolveModuleName(string moduleName, string containingFile, ModuleKind resolutionMode)
    {
        var (resolved, _) = _program._resolver.ResolveModuleName(moduleName, containingFile, resolutionMode, null);
        return resolved;
    }
}

// ported from: internal/ls/autoimport/aliasresolver_crash_test.go
//
// 与 Go 测试的差异（逐条）：
// - Go 的 `fakeCloneHost`（实现 `RegistryCloneHost`）→ C# 的嵌套 `FakeCloneHost`。Go 里 `GetDefaultProject`
//   返回 `(nil, nil)`，C# 的元组第一项是接口类型，用 `(null!, null)` 表达"两个都是 nil"。
// - `parser.ParseSourceFile`/`binder.BindSourceFile`/`module.NewResolver` 与命名空间同名 ⇒ 用
//   `using static` 的裸名（§2）。
// - `tspath.Path(fileName)` → C# 的显式转换 `(Path)fileName`。
// - 断言只有一条："GetDiagnostics 不 panic"（Go 侧也是"跑通即可"），C# 里等价于"不抛异常"（xunit 天然表达）。
// - `testmain_test.go` **不移植**：它只做两件 Go 侧专属的事 —— `core.ApplyDebugStackLimit()`（Go 的 goroutine
//   栈上限调整，§3.9 在 C# 里由"大栈线程"承担，本用例只解析 2 条语句、不涉及深递归）与 `baseline.Track()`
//   （Go 的 baseline 比对框架，本包测试不产生 baseline）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.PackageJson;
using TypeScript.Vfs;
using Xunit;
using Path = TypeScript.TSPath.Path;
using static TypeScript.Core.Core;
using static TypeScript.Module.Module;
using static TypeScript.Parser.Parser;
using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class LsAutoImportAliasResolverTests
{
    private sealed class FakeCloneHost : RegistryCloneHost
    {
        private readonly FS _fs;

        public FakeCloneHost(FS fs) => _fs = fs;

        public FS FS() => _fs;

        public string GetCurrentDirectory() => "/";

        public (ProjectID Id, TypeScript.Compiler.Program? Program) GetDefaultProject(Path path) => (null!, null);

        public TypeScript.Compiler.Program? GetProgramForProject(ProjectID projectID) => null;

        public InfoCacheEntry? GetPackageJson(string fileName) => null;

        public SourceFile? GetSourceFile(string fileName, Path path) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>Regression test for microsoft/typescript-go#4322：导出提取时 checker 建立在"冒充 Program 的
    /// aliasResolver"之上；即使文件里有类型错误，提取也必须跑完而不是崩溃。</summary>
    [Fact]
    public void AliasResolverGetDiagnosticsDoesNotPanic()
    {
        const string fileName = "/pkg/index.ts";
        const string text = "declare function f(arg: { a: string }): () => void;\nexport const x = f({ a: 1 });\n";

        var fs = FromMap(new Dictionary<string, string> { [fileName] = text }, true /*useCaseSensitiveFileNames*/);
        var host = new FakeCloneHost(fs);

        var sourceFile = ParseSourceFile(
            new SourceFileParseOptions { FileName = fileName, Path = (Path)fileName },
            text,
            ScriptKind.TS);
        TypeScript.Binder.Binder.BindSourceFile(sourceFile);

        var resolver = NewResolver(new TypeScript.Module.ResolverOptions
        {
            Host = host,
            CompilerOptions = EmptyCompilerOptions,
        });
        var r = new AliasResolver(
            [sourceFile],
            null,
            host,
            resolver,
            f => (Path)f,
            (_, _) => { });

        var (ch, _) = TypeScript.Checker.Checker.NewChecker(r, null);

        // Type-checking this file's diagnostics must not panic.
        _ = ch.GetDiagnostics(Context.Background, sourceFile);
    }
}

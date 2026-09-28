// ported from: internal/ls/findallreferences_test.go:1-107（TestImplementationsWorklistDoesNotBlowUp）
//
// 与 Go 测试的差异（逐条）：
//
// 1) Go 的 `&LanguageService{program: program, converters: converters}`（只填两个字段的复合字面量）在 C# 里不可行 ——
//    `LanguageService` 的构造器是私有的，唯一入口是 `LanguageService.NewLanguageService(projectID, program, host, activeFile)`
//    （它会向 host 要 converters 与 preferences）⇒ 本测试提供最小 `Host`/`ProjectID` 测试替身（与
//    `SelectionRangesTests` 同一套写法），host 原样返回测试自己构造的 converters。
// 2) Go 的 `assert.Equal(t, len(errors), 0)` / `assert.Assert(t, cond, msg)` → xunit `Assert.Empty` / `Assert.True`。
// 3) `core.TextPos` → `new TextPos(offset)`；`converters.ToLSPPosition` 返回 `(Position, Fidelity)` 元组。
// 4) `provideSymbolsAndEntries` / `SymbolAndEntries.References()` 是 `internal`（测试项目有 InternalsVisibleTo，
//    见 src/TypeScript.Compiler/Properties/AssemblyInfo.cs）⇒ 直接调用。
// 5) Go 的 `t.Logf` → `ITestOutputHelper.WriteLine`（只用于打印 K/refs/groups 的观测值）。
using TypeScript.Bundled;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Ls;
using TypeScript.LsAutoImport;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.SourceMap;
using TypeScript.TsOptions;
using TypeScript.Vfs.VfsTest;
using Xunit;
using Xunit.Abstractions;
using static TypeScript.LsConv.LsConv;

namespace TypeScript.Tests;

/// <summary>
/// `provideSymbolsAndEntries` 驱动 go-to-implementation 时用广度优先 worklist。当接口成员有 K 个实现时，
/// 每次程序级搜索都会返回全部 K 个实现；若不去重，保留的引用、工作队列与保留的 SymbolsAndEntries 组都会 O(K^2) 增长，
/// 在大型深层类型化程序上可能耗尽内存。
///
/// 最终 LSP 响应按节点去重，所以这种膨胀在响应上看不出来；本测试检查去重前的数据：引用数与组数都应随 K **线性**增长
/// （K 翻倍 ⇒ 约 2 倍；未去重时约 4 倍，故阈值取 3 倍）。
/// </summary>
public class FindAllReferencesTests(ITestOutputHelper output)
{
    private const int _k = 40;

    /// <summary>Go 的 `projectID autoimport.ProjectID` 测试替身（只用于构造 LanguageService）。</summary>
    private sealed class StubProjectID : ProjectID
    {
        public string String() => "find-all-references-tests";
    }

    /// <summary>Go 测试里的零值 host（只需要 Converters 与 preferences）。</summary>
    private sealed class StubHost(Converters converters) : TypeScript.Ls.Host
    {
        public bool UseCaseSensitiveFileNames() => false;

        public bool TryReadFile(string path, out string contents)
        {
            contents = "";
            return false;
        }

        public Converters Converters() => converters;

        public UserPreferences GetPreferences(string activeFile) => new();

        public ECMALineInfo? GetECMALineInfo(string fileName) => null;

        public Registry AutoImportRegistry() => null!;

        public string[] ReadDirectory(string currentDir, string path, string[]? extensions, string[]? excludes, string[]? includes, int depth) => [];

        public string[] GetDirectories(string path) => [];

        public bool DirectoryExists(string path) => false;

        public bool FileExists(string path) => false;
    }

    /// <summary>Go 测试的 `measure(k)`：返回去重前的引用数与 SymbolsAndEntries 组数。</summary>
    private static (int Refs, int Groups) Measure(int k)
    {
        var b = new System.Text.StringBuilder();
        b.Append("interface I { m(): void; }\n");
        for (var i = 0; i < k; i++)
        {
            b.Append($"const a{i}: I = {{ m() {{}} }};\n");
        }

        b.Append("declare const i: I;\n");
        b.Append("i.m();\n");
        var content = b.ToString();

        // 命名空间遮蔽：在本命名空间里裸写 Bundled/Compiler/TsOptions 会解析到同名**命名空间**（CS0234），
        // 必须全限定到同名静态类（仓内既定口径，见 artifacts/ls-g1-helpers.md 的踩坑清单）。
        var fs = TypeScript.Bundled.Bundled.WrapFS(VfsTest.FromMap(new Dictionary<string, string>
        {
            ["/repro.ts"] = content,
            ["/tsconfig.json"] = "{ \"compilerOptions\": {}, \"files\": [\"repro.ts\"] }",
        }, useCaseSensitiveFileNames: false));

        var host = TypeScript.Compiler.Compiler.NewCompilerHost("/", fs, TypeScript.Bundled.Bundled.LibPath(), null, null, null);
        var (parsed, errors) = TypeScript.TsOptions.TsOptions.GetParsedCommandLineOfConfigFile("/tsconfig.json", new CompilerOptions(), null, host, null);
        Assert.Empty(errors);

        var program = TypeScript.Compiler.Compiler.NewProgram(new ProgramOptions { Config = parsed!, Host = host });
        program.BindSourceFiles();
        var sourceFile = program.GetSourceFile("/repro.ts")!;
        program.GetSemanticDiagnostics(Context.Background, sourceFile);

        var lineMap = ComputeLSPLineStarts(content);
        var converters = NewConverters(PositionEncodingKind.UTF8, _ => lineMap);
        var ls = LanguageService.NewLanguageService(new StubProjectID(), program, new StubHost(converters), "");

        // Position of the `m` property in the final `i.m();`.
        var offset = content.LastIndexOf("i.m", StringComparison.Ordinal) + "i.".Length;
        var (pos, _) = converters.ToLSPPosition(sourceFile, new TextPos(offset));

        var (data, ok) = ls.ProvideSymbolsAndEntries(Context.Background, new DocumentUri("file:///repro.ts"), pos, isRename: false, implementations: true);
        Assert.True(ok, "provideSymbolsAndEntries should find the interface member");

        var refs = 0;
        foreach (var se in data.SymbolsAndEntries ?? [])
        {
            refs += se.References()?.Length ?? 0;
        }

        return (refs, data.SymbolsAndEntries?.Length ?? 0);
    }

    [Fact]
    public void ImplementationsWorklistDoesNotBlowUp()
    {
        var (smallRefs, smallGroups) = Measure(_k);
        var (largeRefs, largeGroups) = Measure(2 * _k);
        output.WriteLine(
            $"K={_k} -> {smallRefs} refs / {smallGroups} groups; K={2 * _k} -> {largeRefs} refs / {largeGroups} groups");

        // Retained references (and, since each is enqueued at most once, the work queue) must grow
        // ~linearly (~2x when K doubles). The un-deduplicated worklist grows ~4x. Fail above 3x.
        Assert.True(
            largeRefs <= smallRefs * 3,
            $"implementations worklist references scale superlinearly: K={_k} -> {smallRefs}, K={2 * _k} -> {largeRefs} (expected ~linear); " +
            "provideSymbolsAndEntries accumulates references without deduplicating by node");

        // Retained SymbolsAndEntries groups must also grow ~linearly. Appending one group per search
        // result (K searches, each returning all K implementations) grows ~4x; dropping duplicate
        // empty groups keeps it bounded by the distinct definitions. Fail above 3x.
        Assert.True(
            largeGroups <= smallGroups * 3,
            $"implementations worklist groups scale superlinearly: K={_k} -> {smallGroups}, K={2 * _k} -> {largeGroups} (expected ~linear); " +
            "provideSymbolsAndEntries retains a group per search result without deduplicating by definition");
    }
}

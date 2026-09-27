// ported from: internal/execute/build/graph_test.go、internal/execute/build/clean_test.go
//
// 与 Go 的偏离（都是机械替换，不改变断言强度）：
// 1) Go 的 `t.Run(名称, …)`：graph_test.go 是表驱动（`buildOrderTestCase.run`，9 个 case）→ C# 用
//    `[Theory]`/`[MemberData]` 的同一个表（数据行逐条对应 graph_test.go:15-25）；clean_test.go 的 4 个 `t.Run`
//    → 4 个 `[Fact]`，方法名 = `<GoTest>_<子测试名驼峰>`（同 ExtendedConfigCacheTests 的约定）。
// 2) `tsctests.NewTscSystem(files, true, cwd)` → 测试局部的 `TscTestSystem`（见 TscTestSystem.cs；graph_test.go
//    用 "/home/src/workspaces/project"、clean_test.go 用 "/project"，两者都传 useCaseSensitiveFileNames = true）。
// 3) `tsoptions.ParseBuildCommandLine` → `TsOptions.ParseBuildCommandLine`（`using static
//    TypeScript.TsOptions.TsOptions;`）；`build.NewOrchestrator` → `using static TypeScript.Execute.Build.Build;`
//    的 `NewOrchestrator`；`tsc.ExitStatusXxx` → `TypeScript.Execute.Tsc.ExitStatus.Xxx`。
// 4) `core.Map(xs, f)` → LINQ `xs.Select(f).ToArray()`；`slices.Contains(s, x)` → `s.Contains(x)`（`Span<T>` 的
//    Contains 是序数比较，与 Go 的 `==` 一致）；`slices.Index` → `Array.IndexOf`。
// 5) `gotest.tools/assert` → xunit：`assert.DeepEqual(t, got, want)` → `Assert.Equal(want, got)`（string[] 逐元素
//    比较）；带消息的 `assert.Assert(t, cond, msg)` → `Assert.True(cond, msg)` / `Assert.False(cond, msg)`；
//    `assert.Assert(t, len(x) > 0)` → `Assert.NotEmpty`。
// 6) Go 的 `t.Parallel()` 由 xunit 调度，不逐字搬；`.NET` 侧测试主体跑在 64 MiB 栈的线程上
//    （`TscTestSystem.RunOnBigStackThread`，docs/architecture.md §3.9，同 TscEmitTests）。
// 7) 两处 tsconfig 文本只喂给 JSON 解析器（空白/缩进在 JSON 里无意义）→ C# 用原始字符串字面量书写，键值逐字
//    相同；graph_test.go 里由 `fmt.Sprintf` 拼出的那份用等价的内插字符串拼出同一文本。
// 8) graph_test.go 的 "parent before child" 检查（graph_test.go:126-141）在 Go 里恒不生效：`buildOrder` 已被
//    `projectName` 映射成项目名（"A"…），而 `child`/`parent` 是 `configName` 产出的配置路径，`slices.Index`
//    永远返回 -1 并 `continue`。本移植按 1:1 保留同样的写法与同样的（无效）语义，不擅自"修复"这个断言。
//
// 未移植：无（graph_test.go 的 TestBuildOrderGenerator（1 个表驱动测试、9 个 case）与 clean_test.go 的
// TestClean（4 个子测试）全部覆盖）。
using TypeScript.Execute.Build;
using TypeScript.Vfs;
using Xunit;
using TscNs = TypeScript.Execute.Tsc;

namespace TypeScript.Tests;

using static TypeScript.Execute.Build.Build;
using static TypeScript.TsOptions.TsOptions;

public class BuildGraphAndCleanTests
{
    /// <summary>Go: <c>func (b *buildOrderTestCase) configName(project string) string</c>（graph_test.go:44-46）。</summary>
    private static string ConfigName(string project) => $"/home/src/workspaces/project/{project}/tsconfig.json";

    /// <summary>Go: <c>func (b *buildOrderTestCase) projectName(config string) string</c>（graph_test.go:48-53）。</summary>
    private static string ProjectName(string config)
    {
        const string prefix = "/home/src/workspaces/project/";
        const string suffix = "/tsconfig.json";
        var str = config.StartsWith(prefix, StringComparison.Ordinal) ? config[prefix.Length..] : config;
        return str.EndsWith(suffix, StringComparison.Ordinal) ? str[..^suffix.Length] : str;
    }

    /// <summary>Go: <c>testCases</c>（graph_test.go:15-25）：9 个 case，字段顺序 name/projects/expected/expectedSchedule/circular。</summary>
    public static IEnumerable<object[]> BuildOrderCases() =>
    [
        ["specify two roots", new[] { "A", "G" }, new[] { "D", "E", "C", "B", "A", "G" }, new[] { "D", "E", "G", "C", "B", "A" }, false],
        ["multiple parts of the same graph in various orders", new[] { "A" }, new[] { "D", "E", "C", "B", "A" }, new[] { "D", "E", "C", "B", "A" }, false],
        ["multiple parts of the same graph in various orders", new[] { "A", "C", "D" }, new[] { "D", "E", "C", "B", "A" }, new[] { "D", "E", "C", "B", "A" }, false],
        ["multiple parts of the same graph in various orders", new[] { "D", "C", "A" }, new[] { "D", "E", "C", "B", "A" }, new[] { "D", "E", "C", "B", "A" }, false],
        ["other orderings", new[] { "F" }, new[] { "E", "F" }, new[] { "E", "F" }, false],
        ["other orderings", new[] { "E" }, new[] { "E" }, new[] { "E" }, false],
        ["other orderings", new[] { "F", "C", "A" }, new[] { "E", "F", "D", "C", "B", "A" }, new[] { "E", "D", "F", "C", "B", "A" }, false],
        ["returns circular order", new[] { "H" }, new[] { "E", "J", "I", "H" }, new[] { "E", "J", "I", "H" }, true],
        ["returns circular order", new[] { "A", "H" }, new[] { "D", "E", "C", "B", "A", "J", "I", "H" }, new[] { "D", "E", "C", "J", "B", "I", "A", "H" }, true],
    ];

    // Go: TestBuildOrderGenerator（graph_test.go:13-27）→ buildOrderTestCase.run（graph_test.go:55-156）。
    // 说明：Go 的子测试名是 `name + " - " + strings.Join(projects, ",")`；xunit 的 Theory 显示名由参数自动生成，
    // `name` 保留为数据行的第一个参数正是为了让显示名里带上 Go 的分组名。
    [Theory]
    [MemberData(nameof(BuildOrderCases))]
    public void TestBuildOrderGenerator(string name, string[] projects, string[] expected, string[] expectedSchedule, bool circular)
    {
        _ = name;
        TscTestSystem.RunOnBigStackThread(() =>
        {
            var files = new Dictionary<string, string>();
            var deps = new Dictionary<string, string[]>
            {
                ["A"] = ["B", "C"],
                ["B"] = ["C", "D"],
                ["C"] = ["D", "E"],
                ["F"] = ["E"],
                ["H"] = ["I"],
                ["I"] = ["J"],
                ["J"] = ["H", "E"],
            };
            var reverseDeps = new Dictionary<string, List<string>>();
            foreach (var (project, projectDeps) in deps)
            {
                foreach (var dep in projectDeps)
                {
                    if (!reverseDeps.TryGetValue(dep, out var list))
                    {
                        reverseDeps[dep] = list = [];
                    }
                    list.Add(project);
                }
            }

            void VerifyDeps(Orchestrator orchestrator, string[] buildOrder, bool hasDownStream)
            {
                for (var index = 0; index < buildOrder.Length; index++)
                {
                    var project = buildOrder[index];
                    var upstream = orchestrator.Upstream(ConfigName(project)).Select(ProjectName).ToArray();
                    var expectedUpstream = deps.TryGetValue(project, out var upstreamDeps) ? upstreamDeps : [];
                    Assert.True(
                        upstream.Length <= expectedUpstream.Length,
                        $"Expected upstream for {project} to be at most {expectedUpstream.Length}, got {upstream.Length}");
                    foreach (var expectedDep in expectedUpstream)
                    {
                        if (buildOrder.AsSpan(0, index).Contains(expectedDep))
                        {
                            Assert.True(
                                upstream.Contains(expectedDep),
                                $"Expected upstream for {project} to contain {expectedDep}");
                        }
                        else
                        {
                            Assert.False(
                                upstream.Contains(expectedDep),
                                $"Expected upstream for {project} to not contain {expectedDep}");
                        }
                    }

                    var downstream = orchestrator.Downstream(ConfigName(project)).Select(ProjectName).ToArray();
                    var expectedDownstream = hasDownStream && reverseDeps.TryGetValue(project, out var downstreamDeps)
                        ? downstreamDeps.ToArray()
                        : [];
                    Assert.True(
                        downstream.Length <= expectedDownstream.Length,
                        $"Expected downstream for {project} to be at most {expectedDownstream.Length}, got {downstream.Length}");
                    foreach (var expectedDep in expectedDownstream)
                    {
                        if (buildOrder.AsSpan(index + 1).Contains(expectedDep))
                        {
                            Assert.True(
                                downstream.Contains(expectedDep),
                                $"Expected downstream for {project} to contain {expectedDep}");
                        }
                        else
                        {
                            Assert.False(
                                downstream.Contains(expectedDep),
                                $"Expected downstream for {project} to not contain {expectedDep}");
                        }
                    }
                }
            }

            foreach (var project in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" })
            {
                files[$"/home/src/workspaces/project/{project}/{project}.ts"] = "export {}";
                var referencesStr = "";
                if (deps.TryGetValue(project, out var projectDeps))
                {
                    referencesStr = $""", "references": [{string.Join(",", projectDeps.Select(dep => $"{{ \"path\": \"../{dep}\" }}"))}]""";
                }
                files[ConfigName(project)] = $$"""
                    {
                        "compilerOptions": { "composite": true },
                        "files": ["./{{project}}.ts"],
                        {{referencesStr}}
                    }
                    """;
            }

            var sys = new TscTestSystem(files, true, "/home/src/workspaces/project");
            string[] args = ["--build", "--dry", .. projects];
            var buildCommand = ParseBuildCommandLine(args, sys);
            var orchestrator = NewOrchestrator(new Options
            {
                Sys = sys,
                Command = buildCommand,
            });
            orchestrator.GenerateGraph(null);
            var buildOrder = orchestrator.Order().Select(ProjectName).ToArray();
            Assert.Equal(expected, buildOrder);
            VerifyDeps(orchestrator, buildOrder, false);
            var scheduleOrder = orchestrator.ScheduleOrder().Select(ProjectName).ToArray();
            Assert.Equal(expectedSchedule, scheduleOrder);
            VerifyDeps(orchestrator, scheduleOrder, false);

            if (!circular)
            {
                foreach (var (project, projectDeps) in deps)
                {
                    var child = ConfigName(project);
                    var childIndex = Array.IndexOf(buildOrder, child);
                    if (childIndex == -1)
                    {
                        continue;
                    }
                    foreach (var dep in projectDeps)
                    {
                        var parent = ConfigName(dep);
                        var parentIndex = Array.IndexOf(buildOrder, parent);

                        Assert.True(childIndex > parentIndex, $"Expecting child {project} to be built after parent {dep}");
                    }
                }
            }

            orchestrator.GenerateGraphReusingOldTasks();
            var buildOrder2 = orchestrator.Order().Select(ProjectName).ToArray();
            Assert.Equal(expected, buildOrder2);

            string[] argsWatch = ["--build", "--watch", .. projects];
            var buildCommandWatch = ParseBuildCommandLine(argsWatch, sys);
            orchestrator = NewOrchestrator(new Options
            {
                Sys = sys,
                Command = buildCommandWatch,
            });
            orchestrator.GenerateGraph(null);
            var buildOrder3 = orchestrator.Order().Select(ProjectName).ToArray();
            VerifyDeps(orchestrator, buildOrder3, true);
        });
    }

    /// <summary>Go: <c>type cleanTestSystem struct</c>（clean_test.go:53-70）——把 Writer/ErrorWriter 都指向自己的 buffer。</summary>
    private sealed class CleanTestSystem : TscTestSystem
    {
        private readonly StringWriter _cleanOutput = new();

        public CleanTestSystem(Dictionary<string, string> files) : base(files, true, "/project")
        {
        }

        public override TextWriter Writer() => _cleanOutput;

        public override TextWriter ErrorWriter() => _cleanOutput;
    }

    /// <summary>Go: <c>func newCleanTestSystem() *cleanTestSystem</c>（clean_test.go:68-92）。</summary>
    private static CleanTestSystem NewCleanTestSystem() => new(new Dictionary<string, string>
    {
        ["/project/a/tsconfig.json"] = """
            {
                "compilerOptions": { "composite": true, "noLib": true, "outDir": "dist" },
                "files": ["index.ts"],
                "references": [{ "path": "../b" }]
            }
            """,
        ["/project/a/index.ts"] = "export const a = 1;",
        ["/project/a/dist/index.js"] = "export const a = 1;",
        ["/project/a/dist/index.d.ts"] = "export declare const a = 1;",
        ["/project/b/tsconfig.json"] = """{ "compilerOptions": { "composite": true, "noLib": true, "outDir": "dist" }, "files": ["index.ts"] }""",
        ["/project/b/index.ts"] = "export const b = 1;",
        ["/project/b/dist/index.js"] = "export const b = 1;",
        ["/project/b/dist/index.d.ts"] = "export declare const b = 1;",
        ["/project/c/tsconfig.json"] = """{ "compilerOptions": { "composite": true, "noLib": true, "outDir": "dist" }, "files": ["index.ts"] }""",
        ["/project/c/index.ts"] = "export const c = 1;",
        ["/project/c/dist/index.js"] = "export const c = 1;",
        ["/project/c/dist/index.d.ts"] = "export declare const c = 1;",
        ["/project/cycle1/tsconfig.json"] = """
            {
                "compilerOptions": { "composite": true, "noLib": true, "outDir": "dist" },
                "files": ["index.ts"],
                "references": [{ "path": "../cycle2" }]
            }
            """,
        ["/project/cycle1/index.ts"] = "export const cycle1 = 1;",
        ["/project/cycle1/dist/index.js"] = "export const cycle1 = 1;",
        ["/project/cycle2/tsconfig.json"] = """
            {
                "compilerOptions": { "composite": true, "noLib": true, "outDir": "dist" },
                "files": ["index.ts"],
                "references": [{ "path": "../cycle1" }]
            }
            """,
        ["/project/cycle2/index.ts"] = "export const cycle2 = 1;",
        ["/project/cycle2/dist/index.js"] = "export const cycle2 = 1;",
    });

    /// <summary>Go: <c>func newCleanTestOrchestrator(sys tsc.System, args ...string) *build.Orchestrator</c>（clean_test.go:94-100）。</summary>
    private static Orchestrator NewCleanTestOrchestrator(TscNs.System sys, params string[] args)
    {
        var command = ParseBuildCommandLine(["--build", .. args], sys);
        return NewOrchestrator(new Options
        {
            Sys = sys,
            Command = command,
        });
    }

    // Go: TestClean/"cleans selected project and references"（clean_test.go:18-28）。
    [Fact]
    public void TestClean_CleansSelectedProjectAndReferences()
    {
        var sys = NewCleanTestSystem();
        var orchestrator = NewCleanTestOrchestrator(sys, "a", "c");

        var result = orchestrator.Clean("a");
        Assert.Equal(TscNs.ExitStatus.Success, result.Result.Status);
        Assert.Equal(2, result.Statistics.Projects);
        Assert.False(sys.FS().FileExists("/project/a/dist/index.js"));
        Assert.False(sys.FS().FileExists("/project/b/dist/index.js"));
        Assert.True(sys.FS().FileExists("/project/c/dist/index.js"));
    }

    // Go: TestClean/"dry run preserves outputs"（clean_test.go:30-41）。
    [Fact]
    public void TestClean_DryRunPreservesOutputs()
    {
        var sys = NewCleanTestSystem();
        var orchestrator = NewCleanTestOrchestrator(sys, "--dry", "a");

        var result = orchestrator.Clean("a");
        Assert.Equal(TscNs.ExitStatus.Success, result.Result.Status);
        Assert.Equal(2, result.Statistics.Projects);
        Assert.NotEmpty(result.FilesToDelete);
        Assert.True(sys.FS().FileExists("/project/a/dist/index.js"));
        Assert.True(sys.FS().FileExists("/project/b/dist/index.js"));
    }

    // Go: TestClean/"rejects project outside build"（clean_test.go:43-52）。
    [Fact]
    public void TestClean_RejectsProjectOutsideBuild()
    {
        var sys = NewCleanTestSystem();
        var orchestrator = NewCleanTestOrchestrator(sys, "a");

        var result = orchestrator.Clean("c");
        Assert.Equal(TscNs.ExitStatus.InvalidProject_OutputsSkipped, result.Result.Status);
        Assert.True(sys.FS().FileExists("/project/a/dist/index.js"));
        Assert.True(sys.FS().FileExists("/project/b/dist/index.js"));
        Assert.True(sys.FS().FileExists("/project/c/dist/index.js"));
    }

    // Go: TestClean/"rejects circular build"（clean_test.go:54-63）。
    [Fact]
    public void TestClean_RejectsCircularBuild()
    {
        var sys = NewCleanTestSystem();
        var orchestrator = NewCleanTestOrchestrator(sys, "cycle1");

        var result = orchestrator.Clean("cycle1");
        Assert.Equal(TscNs.ExitStatus.ProjectReferenceCycle_OutputsSkipped, result.Result.Status);
        Assert.NotEmpty(result.Errors);
        Assert.True(sys.FS().FileExists("/project/cycle1/dist/index.js"));
        Assert.True(sys.FS().FileExists("/project/cycle2/dist/index.js"));
    }
}

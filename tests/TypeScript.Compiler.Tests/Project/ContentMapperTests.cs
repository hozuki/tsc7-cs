// ported from: internal/project/contentmapper_test.go:1-1081
//
// 与 Go 测试的差异（逐条）：
//
// 1) `assert.Equal(t, want, got)` → `Assert.Equal(expected, actual)`；`assert.Assert(t, cond, msg, …)` →
//    `Assert.True(cond, msg)`；`assert.DeepEqual(t, a, b)` → `Assert.Equal(expected, actual)`（数组按元素比较）；
//    Go 的 `_, err := session.GetLanguageService(ctx, uri); assert.NilError(t, err)` /
//    `assert.ErrorContains(t, err, "no project found")` → `ProjectTestSupport.Open` / `ProjectTestSupport.AssertNoProjectError`
//    （C# 的 `Session.GetLanguageService` 按 ProjHub 的契约保留双返回 `(Ls.LanguageService?, Exception?)`，**不抛**，
//    见 ProjectTests.cs 的 ProjectTestSupport）。
// 2) `defer session.Close()` → 辅助方法 `WithSession(init, body)`（try/finally 里调 `Close()`）；`defer` 的
//    "函数返回时执行"语义与它一致。
// 3) `context.Background()` → `Context.Background`；`lsproto.WithClientCapabilities(ctx, caps)` 在 C# 里
//    **不接收 ctx**（Lsp.cs 文件头第 … 条：能力挂在静态 AsyncLocal 上）⇒ 调用点直接 `WithClientCapabilities(caps)`，
//    ctx 仍是 `Context.Background`。
// 4) `recordingContentMapperSpawner`（未导出）→ `RecordingContentMapperSpawner`；Go 的 `atomic.Int32` 计数 →
//    `Interlocked.Increment` + 公开 int 字段（Go 的 `.Load()` 在这里就是直接读字段）；Go 用 `sync.Once` 保证
//    Close 只计一次 → `Interlocked.Exchange`。
// 5) `languageService.GetProgram()` 在 C# 返回**可空** `Compiler.Program?` ⇒ 断言点先 `Assert.NotNull(...)`
//    （xunit 的 NotNull 带 [NotNull] 注解，后续解引用不报 CS8602；仓内先例 Bundled/BundledTests.cs）。
// 6) `diagnostics()`/`slices.ContainsFunc` → `System.Linq` 的 `Any`；`slices.Clone` + `slices.Sort` →
//    `(string[])Extensions.Clone()` + `Array.Sort(…, StringComparer.Ordinal)`。
// 7) `files` 的取值 `files["/home/project/main.ts"].(string)` → `(string)files["/home/project/main.ts"]!`。
// 8) 裸写 `Project` 会命中命名空间 `TypeScript.Project`（CS0118）⇒ 本文件里所有 `Project`/`Snapshot` 引用都靠
//    `var` 推断，没有一处需要写类型名（若将来需要，用异名别名，仓内先例 Execute/Tsc.cs 的 `CmProject`：
//    与命名空间同名的 `using Project = …` 无效）；`ContentMapper.Project`（映射器的项目接口）也没有显式引用。
// 9) `SetContentMapperContributions(ctx, contributions, documentURIs)` 的第三参在 C# 里是**非空**数组
//    （ProjHub 契约）⇒ Go 的 `nil` 在这里写 `[]`；`WithLanguageServiceAndSnapshot` 的释放回调是
//    `Func<Exception?>`（返回 null = 成功），Go 的 `func() error` 一一对应。
using System.Linq;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.TestUtil.ContentMapperTest;
using TypeScript.Tests.ProjectTestUtil;
using static TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;
using TypeScript.TSPath;
using TypeScript.Vfs.VfsTest;

using Xunit;

using Path = TypeScript.TSPath.Path;
using Loc = TypeScript.Locale.Locale;

using static TypeScript.LsProto.LsProto;
using static TypeScript.Vfs.VfsTest.VfsTest;
using static TypeScript.Project.Kind;
using static TypeScript.Project.ProgramUpdateKind;
using static TypeScript.TestUtil.ContentMapperTest.ContentMapperTest;
using static TypeScript.Tests.ProjectContentMapperTestSupport;

namespace TypeScript.Tests;

/// <summary>Go: <c>recordingContentMapperSpawner</c> + <c>recordingContentMapperProcess</c>（contentmapper_test.go:24-52）。</summary>
internal sealed class RecordingContentMapperSpawner(Spawner inner) : Spawner
{
    /// <summary>Go: <c>spawns atomic.Int32</c>。</summary>
    public int Spawns;

    /// <summary>Go: <c>closes atomic.Int32</c>。</summary>
    public int Closes;

    /// <summary>Go: <c>func (s *recordingContentMapperSpawner) Spawn(...)</c>。</summary>
    public Stream Spawn(string[] command, string dir, Stream stderr)
    {
        var process = inner.Spawn(command, dir, stderr);
        Interlocked.Increment(ref Spawns);
        return new RecordingContentMapperProcess(process, this);
    }

    /// <summary>Go: <c>recordingContentMapperProcess</c>：Close 只计一次（Go 的 <c>sync.Once</c>）。</summary>
    private sealed class RecordingContentMapperProcess(Stream inner, RecordingContentMapperSpawner owner) : Stream
    {
        private int _closed;

        public override bool CanRead => inner.CanRead;

        public override bool CanWrite => inner.CanWrite;

        public override bool CanSeek => inner.CanSeek;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            // Go: p.once.Do(func() { p.closes.Add(1) }); return p.ReadWriteCloser.Close()
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                Interlocked.Increment(ref owner.Closes);
            }

            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>contentmapper 测试共享的小工具（`WithSession`、默认 SessionOptions）。</summary>
internal static class ProjectContentMapperTestSupport
{
    /// <summary>Go 的 `session := project.NewSession(init); defer session.Close()`。</summary>
    internal static void WithSession(SessionInit init, Action<Session> body)
    {
        var session = new Session(init);
        try
        {
            body(session);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go 的 `&project.SessionOptions{CurrentDirectory: "/home/project", …}`。</summary>
    internal static SessionOptions Options(bool runExternalCode, bool loggingEnabled = false, bool watchEnabled = false) => new()
    {
        CurrentDirectory = "/home/project",
        DefaultLibraryPath = TypeScript.Bundled.Bundled.LibPath(),
        TypingsLocation = TestTypingsLocation,
        PositionEncoding = PositionEncodingKind.UTF8,
        LoggingEnabled = loggingEnabled,
        WatchEnabled = watchEnabled,
        RunExternalCode = runExternalCode,
    };
}

/// <summary>Go: <c>TestContentMapper*</c>（contentmapper_test.go 的全部顶层用例）。</summary>
public class ProjectContentMapperTests
{

    /// <summary>Go: <c>TestContentMapperProjectWithoutMappedFiles</c>（contentmapper_test.go:54-96）。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectWithoutMappedFiles(bool hasMapper)
    {
        var config = """{"compilerOptions": {"noLib": true}}""";
        if (hasMapper)
        {
            config = """
                {
                					"compilerOptions": { "noLib": true },
                					"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
                				}
                """;
        }

        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = config,
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
            ["/home/project/main.ts"] = "export {};",
        };
        var (init, _) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        var spawner = new RecordingContentMapperSpawner(NewSpawner());
        init.Spawner = spawner;

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            session.DidOpenFile(ctx, "file:///home/project/main.ts", 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            var languageService = ProjectTestSupport.Open(session, ctx, "file:///home/project/main.ts");
            // Access after freezing must not try to initialize using the cleared builder.
            var program = languageService.GetProgram();
            Assert.NotNull(program);
            var mapperProject = program.ContentMapperProject();
            Assert.Equal(hasMapper, mapperProject is not null);
            Assert.Same(mapperProject, program.ContentMapperProject());
            Assert.Equal(0, spawner.Spawns);
        });
    }

    /// <summary>Go: <c>TestContentMapperParallelFileLoading</c>（contentmapper_test.go:98-139）。</summary>
    [Fact]
    public void ParallelFileLoading()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """
                {
                			"compilerOptions": { "target": "es2020", "noLib": true },
                			"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
                		}
                """,
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
            ["/home/project/main.ts"] = "export {};",
        };

        // Parallel parsing reads the mapper project identity while another file initializes it.
        const int fileCount = 32;
        for (var i = 0; i < fileCount; i++)
        {
            files[$"/home/project/file{i}.box"] = "export const version = #{target};\n";
        }

        var (init, _) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            session.DidOpenFile(ctx, "file:///home/project/main.ts", 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            var languageService = ProjectTestSupport.Open(session, ctx, "file:///home/project/main.ts");
            var program = languageService.GetProgram();
            Assert.NotNull(program);
            var mapperProject = program.ContentMapperProject();
            Assert.NotNull(mapperProject);
            Assert.Same(mapperProject, languageService.GetProgram()!.ContentMapperProject());
            for (var i = 0; i < fileCount; i++)
            {
                var fileName = $"/home/project/file{i}.box";
                var file = languageService.GetProgram()!.GetSourceFile(fileName);
                Assert.NotNull(file);
                Assert.Equal("const __VERSION = \"1.0.0\";\nexport const version = 7;\n", file.Text());
            }
        });
    }


    /// <summary>Go: <c>TestContentMapperPackageManifestChangeReloadsConfig</c>（contentmapper_test.go:294-357）。</summary>
    [Fact]
    public void PackageManifestChangeReloadsConfig()
    {
        const string packageJsonPath = "/home/mapper/package.json";
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """
                {
                			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler" },
                			"contentMappers": [{ "package": "mapper", "extensions": [".box"] }]
                		}
                """,
            ["/home/project/node_modules/mapper"] = Symlink("/home/mapper"),
            [packageJsonPath] = """
                {
                			"name": "mapper",
                			"version": "1.0.0",
                			"typescript": { "contentMapper": { "exec": ["compiler-test-mapper"] } }
                		}
                """,
            ["/home/project/app.box"] = "export const version = #{target};\n",
            ["/home/project/main.ts"] = "import { version } from \"./app.box\";\n",
        };

        var caps = new ResolvedClientCapabilities();
        caps.Workspace.DidChangeWatchedFiles.RelativePatternSupport = true;
        // Go: ctx := lsproto.WithClientCapabilities(context.Background(), caps)
        WithClientCapabilities(caps);
        var ctx = Context.Background;
        var (init, utils) = GetSessionInitOptions(files, new SessionOptions
        {
            CurrentDirectory = "/home/project",
            DefaultLibraryPath = TypeScript.Bundled.Bundled.LibPath(),
            TypingsLocation = TestTypingsLocation,
            PositionEncoding = PositionEncodingKind.UTF8,
            RunExternalCode = true,
            WatchEnabled = true,
        }, null);
        init.BackgroundCtx = ctx;
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            var mainURI = (DocumentUri)"file:///home/project/main.ts";
            session.DidOpenFile(ctx, mainURI, 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, ctx, mainURI);
            var configuredProject = session.Snapshot().GetDefaultProject(mainURI);
            Assert.NotNull(configuredProject);
            var mappers = configuredProject.CommandLine!.ContentMappers();
            Assert.NotNull(mappers);
            Assert.Single(mappers);
            Assert.Equal("/home/mapper", mappers[0].PackageDirectory);
            session.WaitForBackgroundTasks();
            Assert.True(utils.WatchesFile(packageJsonPath), "expected the invalid mapper package manifest to be watched");
            Assert.True(
                utils.Client().WatchFilesCalls().Any(call => call.Watchers.Any(watcher =>
                {
                    var relative = watcher.GlobPattern.RelativePattern;
                    return relative is not null
                        && relative.BaseUri.URI is not null
                        && relative.BaseUri.URI.Value.Value == "file:///home/mapper"
                        && relative.Pattern == "**/*";
                })),
                "expected an external relative-pattern watcher for the mapper package");

            // Go: strings.Replace(PackageJSON(TransformingMapper), `"version": "1.0.0"`, `"version": "2.0.0"`, 1)
            // （该子串只出现一次，C# 的 string.Replace 等价）
            var fixedManifest = PackageJSON(TransformingMapper).Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"", StringComparison.Ordinal);
            Assert.True(fixedManifest.Contains("\"version\": \"2.0.0\"", StringComparison.Ordinal));
            utils.FS().WriteFile(packageJsonPath, fixedManifest);
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent
                {
                    Uri = (DocumentUri)("file://" + packageJsonPath),
                    Type = FileChangeType.Changed,
                },
            ]);

            var languageService = ProjectTestSupport.Open(session, ctx, mainURI);
            var boxFile = languageService.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.NotNull(boxFile);
            Assert.True(
                boxFile.Text().Contains("export const version = 7;", StringComparison.Ordinal),
                $"expected fixed mapper manifest to be reloaded: \"{boxFile.Text()}\"");
        });
    }

    /// <summary>Go: <c>TestContentMapperSupplementalFileClonedOnEdit</c>（contentmapper_test.go:359-423）。</summary>
    [Fact]
    public void SupplementalFileClonedOnEdit()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """{ "compilerOptions": { "strict": true }, "contentMappers": [{ "package": "mapper", "extensions": [".box"] }] }""",
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(SupplementalMapper),
            ["/home/project/app.box"] = "declare const supplementalValue: number;\n",
            ["/home/project/extra.d.ts"] = "interface Extra {}\n",
            ["/home/project/main.ts"] = "const value: number = supplementalValue;\n",
        };
        var (init, utils) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            var mainURI = (DocumentUri)"file:///home/project/main.ts";
            session.DidOpenFile(ctx, mainURI, 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            var languageService = ProjectTestSupport.Open(session, ctx, mainURI);
            var oldProgram = languageService.GetProgram();
            Assert.NotNull(oldProgram);
            var oldCanonical = oldProgram.GetSourceFile("/home/project/app.box");
            Assert.NotNull(oldCanonical);
            var oldSupplemental = oldCanonical.SupplementalSourceFiles();
            Assert.Single(oldSupplemental);
            Assert.Equal("/home/project/app.box.0.ts", oldSupplemental[0].FileName());
            Assert.Equal((Path)"/home/project/app.box.0.ts", oldSupplemental[0].Path());
            Assert.Equal(oldCanonical.Hash, oldSupplemental[0].Hash);
            Assert.Same(oldSupplemental[0], oldProgram.GetSourceFileByPath(oldSupplemental[0].Path()));

            Assert.Same(oldSupplemental[0], oldProgram.FilesByPath()[oldSupplemental[0].Path()]);

            utils.FS().WriteFile("/home/project/app.box", "declare const supplementalValue: string;\n");
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent
                {
                    Uri = "file:///home/project/app.box",
                    Type = FileChangeType.Changed,
                },
            ]);
            languageService = ProjectTestSupport.Open(session, ctx, mainURI);
            var updatedSnapshot = session.Snapshot();
            var configuredProject = updatedSnapshot.GetDefaultProject(mainURI);
            Assert.NotNull(configuredProject);
            Assert.Equal(ProgramUpdateKindCloned, configuredProject.ProgramUpdateKind);

            var newProgram = languageService.GetProgram();
            Assert.NotNull(newProgram);
            var newCanonical = newProgram.GetSourceFile("/home/project/app.box");
            Assert.NotNull(newCanonical);
            var newSupplemental = newCanonical.SupplementalSourceFiles();
            Assert.Single(newSupplemental);
            Assert.Equal(oldSupplemental[0].Path(), newSupplemental[0].Path());
            Assert.NotSame(oldCanonical, newCanonical);
            Assert.NotSame(oldSupplemental[0], newSupplemental[0]);
            Assert.Equal(newCanonical.Hash, newSupplemental[0].Hash);
            Assert.NotEqual(oldSupplemental[0].Hash, newSupplemental[0].Hash);
            Assert.Same(newSupplemental[0], newProgram.FilesByPath()[newSupplemental[0].Path()]);
            Assert.True(
                newSupplemental[0].Text().Contains("supplementalValue: string", StringComparison.Ordinal),
                $"expected the re-transformed supplemental file, got: \"{newSupplemental[0].Text()}\"");
            var mainFile = newProgram.GetSourceFile("/home/project/main.ts");
            var diagnostics = newProgram.GetSemanticDiagnostics(ctx, mainFile);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code() == 2322);

            // Changing the supplemental file's reference graph must fall back from cloning to a full rebuild.
            utils.FS().WriteFile("/home/project/app.box", "/// <reference path=\"./extra.d.ts\" />\ndeclare const supplementalValue: string;\n");
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent
                {
                    Uri = "file:///home/project/app.box",
                    Type = FileChangeType.Changed,
                },
            ]);
            ProjectTestSupport.Open(session, ctx, mainURI);
            configuredProject = session.Snapshot().GetDefaultProject(mainURI);
            Assert.NotNull(configuredProject);
            Assert.Equal(ProgramUpdateKindSameFileNames, configuredProject.ProgramUpdateKind);
        });
    }

    /// <summary>Go: <c>TestContentMapperModuleExtensionClonedOnUnrelatedEdit</c>（contentmapper_test.go:425-461）。</summary>
    [Fact]
    public void ModuleExtensionClonedOnUnrelatedEdit()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """{ "contentMappers": [{ "package": "mapper", "extensions": [".box"] }] }""",
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(ModuleVerbatimMapper),
            ["/home/project/app.box"] = "export const value = 1;\n",
            ["/home/project/main.ts"] = "import { value } from \"./app.box\"; value;",
        };
        var (init, _) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            var mainURI = (DocumentUri)"file:///home/project/main.ts";
            session.DidOpenFile(ctx, mainURI, 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            var languageService = ProjectTestSupport.Open(session, ctx, mainURI);
            var mappedFile = languageService.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.NotNull(mappedFile);
            Assert.Equal("/home/project/app.box.mts", mappedFile.VirtualFileName());
            Assert.True(mappedFile.ParseOptions().ExternalModuleIndicatorOptions.Force);

            session.DidChangeFile(ctx, mainURI, 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    WholeDocument = new TextDocumentContentChangeWholeDocument
                    {
                        Text = "import { value } from \"./app.box\"; value + 1;",
                    },
                },
            ]);
            languageService = ProjectTestSupport.Open(session, ctx, mainURI);
            var configuredProject = session.Snapshot().GetDefaultProject(mainURI);
            Assert.NotNull(configuredProject);
            Assert.Equal(ProgramUpdateKindCloned, configuredProject.ProgramUpdateKind);
            Assert.Same(mappedFile, languageService.GetProgram()!.GetSourceFile("/home/project/app.box"));
        });
    }

    /// <summary>Go: <c>TestContentMapperLocaleChange</c>（contentmapper_test.go:463-509）。</summary>
    [Fact]
    public void LocaleChange()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """{ "contentMappers": [ { "package": "mapper", "extensions": [".box"] } ] }""",
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(VerbatimMapper),
            ["/home/project/app.box"] = "export const value = 1;\n",
            ["/home/project/main.ts"] = "import { value } from \"./app.box\"; value;",
        };
        var (init, utils) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        var spawner = new RecordingContentMapperSpawner(NewSpawner());
        init.Spawner = spawner;

        var localeLock = new Lock();
        var currentLocale = Loc.Default;
        utils.Client().GetLocaleFunc = () =>
        {
            lock (localeLock)
            {
                return currentLocale;
            }
        };
        utils.Client().SetLocaleFunc = value =>
        {
            Assert.True(Loc.TryParse(value, out var updated));
            lock (localeLock)
            {
                currentLocale = updated;
            }
        };

        WithSession(init, session =>
        {
            var ctx = Loc.WithLocale(Context.Background, Loc.Default);
            var localeReads = utils.Client().GetLocaleCalls();
            session.DidOpenFile(ctx, "file:///home/project/main.ts", 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, ctx, "file:///home/project/main.ts");
            // Snapshot adoption reads the current locale for its background work; project construction should not.
            Assert.Equal(localeReads + 1, utils.Client().GetLocaleCalls());
            Assert.Equal(1, spawner.Spawns);

            var preferences = session.Config();
            preferences.Locale = "fr";
            session.Configure(preferences);
            Assert.Equal(1, spawner.Closes);

            ProjectTestSupport.Open(session, Context.Background, "file:///home/project/main.ts");
            Assert.Equal(2, spawner.Spawns);
        });
    }

    /// <summary>Go: <c>TestDynamicContentMapperInProject</c>（contentmapper_test.go:511-556）。</summary>
    [Fact]
    public void DynamicContentMapperInProject()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """{ "contentMappers": [ { "package": "mapper", "extensions": [".box"], "options": { "mode": "project" } } ] }""",
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(DynamicVerbatimMapper),
            ["/home/project/mapper.config.json"] = """{ "version": 1 }""",
            ["/home/project/app.box"] = "export const value = 1;\n",
            ["/home/project/main.ts"] = "import { value } from \"./app.box\"; value;",
        };
        var (init, utils) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            session.DidOpenFile(Context.Background, "file:///home/project/main.ts", 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            var languageService = ProjectTestSupport.Open(session, Context.Background, "file:///home/project/main.ts");
            var mappedFile = languageService.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.NotNull(mappedFile);
            Assert.True(!mappedFile.IsContentMapperFailureStub());

            var program = languageService.GetProgram();
            utils.FS().WriteFile("/home/project/mapper.config.json", """{ "version": 2 }""");
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent
                {
                    Uri = "file:///home/project/mapper.config.json",
                    Type = FileChangeType.Changed,
                },
            ]);
            languageService = ProjectTestSupport.Open(session, Context.Background, "file:///home/project/main.ts");
            Assert.NotSame(program, languageService.GetProgram());
            mappedFile = languageService.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.NotNull(mappedFile);
            Assert.True(!mappedFile.IsContentMapperFailureStub());
        });
    }

    /// <summary>Go: <c>TestDynamicContentMapperRefreshesForMixedWatchBatches</c>（contentmapper_test.go:558-647）。</summary>
    [Theory]
    [InlineData("excessive events")]
    [InlineData("changed files make project fully dirty before mapper deletion")]
    [InlineData("changed files make project fully dirty before mapper creation")]
    public void DynamicContentMapperRefreshesForMixedWatchBatches(string name)
    {
        FileEvent[] Events() => name switch
        {
            "excessive events" => ExcessiveEvents(),
            "changed files make project fully dirty before mapper deletion" =>
            [
                new FileEvent { Uri = "file:///home/project/main.ts", Type = FileChangeType.Changed },
                new FileEvent { Uri = "file:///home/project/app.box", Type = FileChangeType.Changed },
                new FileEvent { Uri = "file:///home/project/mapper.config.json", Type = FileChangeType.Deleted },
            ],
            _ =>
            [
                new FileEvent { Uri = "file:///home/project/main.ts", Type = FileChangeType.Changed },
                new FileEvent { Uri = "file:///home/project/app.box", Type = FileChangeType.Changed },
                new FileEvent { Uri = "file:///home/project/mapper.config.json", Type = FileChangeType.Created },
            ],
        };

        static FileEvent[] ExcessiveEvents()
        {
            var events = new FileEvent[1001];
            for (var i = 0; i < events.Length; i++)
            {
                events[i] = new FileEvent { Uri = (DocumentUri)$"file:///home/project/noise-{i}.ts", Type = FileChangeType.Changed };
            }

            events[0] = new FileEvent { Uri = "file:///home/project/mapper.config.json", Type = FileChangeType.Changed };
            events[1] = new FileEvent { Uri = "file:///home/project/main.ts", Type = FileChangeType.Changed };
            return events;
        }

        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """{ "contentMappers": [{ "package": "mapper", "extensions": [".box"] }] }""",
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(DynamicVerbatimMapper),
            ["/home/project/mapper.config.json"] = """{ "version": 1 }""",
            ["/home/project/app.box"] = "export const value = 1;\n",
            ["/home/project/main.ts"] = "import { value } from \"./app.box\"; value;",
        };
        var (init, _) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        var lifecycle = new ProjectLifecycle();
        init.Spawner = NewSpawnerWithProjectLifecycle(lifecycle);

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            var mainURI = (DocumentUri)"file:///home/project/main.ts";
            session.DidOpenFile(ctx, mainURI, 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, ctx, mainURI);
            Assert.Equal(1, lifecycle.Opens);
            Assert.Equal(0, lifecycle.Closes);

            session.DidChangeWatchedFiles(ctx, Events());
            session.WaitForBackgroundTasks();
            ProjectTestSupport.Open(session, ctx, mainURI);
            Assert.Equal(1, lifecycle.Closes);
            Assert.Equal(2, lifecycle.Opens);
        });
    }

    /// <summary>Go: <c>TestUnusedDynamicContentMapperIsNotOpened</c>（contentmapper_test.go:649-676）。</summary>
    [Fact]
    public void UnusedDynamicContentMapperIsNotOpened()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """{ "contentMappers": [{ "package": "mapper", "extensions": [".box"] }] }""",
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(DynamicVerbatimMapper),
            ["/home/project/main.ts"] = "export const value = 1;",
        };
        var (init, _) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        var lifecycle = new ProjectLifecycle();
        init.Spawner = NewSpawnerWithProjectLifecycle(lifecycle);

        WithSession(init, session =>
        {
            var uri = (DocumentUri)"file:///home/project/main.ts";
            session.DidOpenFile(Context.Background, uri, 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, Context.Background, uri);
            Assert.Equal(0, lifecycle.Opens);
        });
    }

    /// <summary>Go: <c>TestContentMappersInParallelProjectReferences</c>（contentmapper_test.go:678-721）。</summary>
    [Fact]
    public void MappersInParallelProjectReferences()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """
                {
                			"files": ["src/index.d.ts"],
                			"references": [{ "path": "./a" }, { "path": "./b" }]
                		}
                """,
            ["/home/project/src/index.d.ts"] = "export {};",
            ["/home/project/a/tsconfig.json"] = """
                {
                			"compilerOptions": { "composite": true },
                			"files": ["../src/index.d.ts"],
                			"contentMappers": [{ "package": "mapper", "extensions": [".vue"] }]
                		}
                """,
            ["/home/project/b/tsconfig.json"] = """
                {
                			"compilerOptions": { "composite": true },
                			"files": ["../src/index.d.ts"],
                			"contentMappers": [{ "package": "mapper", "extensions": [".svelte"] }]
                		}
                """,
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
        };
        var (init, utils) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            var uri = (DocumentUri)"file:///home/project/src/index.d.ts";
            session.DidOpenFile(Context.Background, uri, 1, (string)files["/home/project/src/index.d.ts"]!, LanguageKind.TypeScript);
            session.WaitForBackgroundTasks();
            var calls = utils.Client().RegisterContentMapperExtensionsCalls();
            Assert.True(calls.Length > 0);
            var extensions = (string[])calls[^1].Extensions.Clone();
            Array.Sort(extensions, StringComparer.Ordinal);
            Assert.Equal(new[] { ".svelte", ".vue" }, extensions);
        });
    }

    /// <summary>Go: <c>TestContentMapperOpenFileExcludedByConfigChange</c>（contentmapper_test.go:723-785）。</summary>
    [Fact]
    public void OpenFileExcludedByConfigChange()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """
                {
                			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler", "strict": true },
                			"include": ["src"],
                			"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
                		}
                """,
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
            ["/home/project/src/app.box"] = "export const version = #{target};\n",
            ["/home/project/src/main.ts"] = "export const main = true;\n",
        };
        var (init, utils) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            var boxURI = (DocumentUri)"file:///home/project/src/app.box";
            session.SetContentMapperContributions(ctx, new ContentMapperContributions
            {
                Mappers =
                [
                    new Mapper
                    {
                        Package = "test.extension",
                        Extensions = [".box"],
                        Name = "mapper",
                        Version = "1.0.0",
                        Exec = [TransformingMapper],
                        CompilerOptions = DeclaredOptions,
                        PackageDirectory = "/home/project",
                        ContributionID = "test.extension[0]",
                    },
                ],
                Extensions = [".box"],
            }, []);
            session.DidOpenFile(ctx, boxURI, 1, (string)files["/home/project/src/app.box"]!, new LanguageKind("box"));
            var languageService = ProjectTestSupport.Open(session, ctx, boxURI);
            Assert.True(languageService.GetProgram()!.GetSourceFile("/home/project/src/app.box") is not null);

            utils.FS().WriteFile("/home/project/tsconfig.json", """
                {
                		"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler", "strict": true },
                		"include": ["src/**/*.ts"],
                		"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
                	}
                """);
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent
                {
                    Uri = "file:///home/project/tsconfig.json",
                    Type = FileChangeType.Changed,
                },
            ]);
            session.WaitForBackgroundTasks();

            // The background update removes app.box from the configured project, but inferred
            // project cleanup is deferred until the next file open.
            Assert.True(session.Snapshot().GetDefaultProject(boxURI) is null);
            var mainURI = (DocumentUri)"file:///home/project/src/main.ts";
            session.DidOpenFile(ctx, mainURI, 1, (string)files["/home/project/src/main.ts"]!, LanguageKind.TypeScript);

            languageService = ProjectTestSupport.Open(session, ctx, boxURI);
            var defaultProject = session.Snapshot().GetDefaultProject(boxURI);
            Assert.NotNull(defaultProject);
            Assert.Equal(KindInferred, defaultProject.Kind);
            var boxFile = languageService.GetProgram()!.GetSourceFile("/home/project/src/app.box");
            Assert.NotNull(boxFile);
            Assert.True(boxFile.ContentMapper() != "", "expected app.box to retain its content mapper");
            Assert.True(
                !boxFile.Text().Contains("#{target}", StringComparison.Ordinal),
                $"expected app.box to be transformed: \"{boxFile.Text()}\"");
        });
    }

    /// <summary>Go: <c>TestContentMapperRemovalWithOpenFile</c>（contentmapper_test.go:787-861）。</summary>
    [Fact]
    public void RemovalWithOpenFile()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """
                {
                			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler" },
                			"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
                		}
                """,
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
            ["/home/project/app.box"] = "export const version = #{target};\n",
        };
        var options = new SessionOptions
        {
            CurrentDirectory = "/home/project",
            DefaultLibraryPath = TypeScript.Bundled.Bundled.LibPath(),
            TypingsLocation = TestTypingsLocation,
            PositionEncoding = PositionEncodingKind.UTF8,
            RunExternalCode = true,
        };
        var (init, utils) = GetSessionInitOptions(files, options, null);
        var spawner = new RecordingContentMapperSpawner(NewSpawner());
        init.Spawner = spawner;

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            var boxURI = (DocumentUri)"file:///home/project/app.box";
            session.DidOpenFile(ctx, boxURI, 1, (string)files["/home/project/app.box"]!, new LanguageKind("box"));
            var languageService = ProjectTestSupport.Open(session, ctx, boxURI);
            Assert.True(languageService.GetProgram()!.GetSourceFile("/home/project/app.box")!.ContentMapper() != "");
            Assert.Equal(1, spawner.Spawns);
            Assert.Equal(0, spawner.Closes);
            for (var version = 2; version <= 4; version++)
            {
                session.DidChangeFile(ctx, boxURI, version,
                [
                    new TextDocumentContentChangePartialOrWholeDocument
                    {
                        WholeDocument = new TextDocumentContentChangeWholeDocument
                        {
                            Text = $"export const version = {version};\n",
                        },
                    },
                ]);
                _ = ProjectTestSupport.Open(session, ctx, boxURI);
                Assert.Equal(1, spawner.Spawns); // snapshot clone should reuse the mapper process
                Assert.Equal(0, spawner.Closes); // snapshot clone should preserve overlapping ownership
            }

            // Go: `WithLanguageServiceAndSnapshot(ctx, uri, func(ls, snapshot) (func() error, error))` —— C# 侧
            // 释放回调是 `Func<Exception?>`（返回 null = 成功，见 ProjHub 的 Session 契约）。
            var (releaseOldSnapshot, error) = session.WithLanguageServiceAndSnapshot(ctx, boxURI, static (_, _) => (static () => null, null));
            Assert.Null(error);

            utils.FS().WriteFile("/home/project/tsconfig.json", """
                {
                			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler" }
                		}
                """);
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent
                {
                    Uri = "file:///home/project/tsconfig.json",
                    Type = FileChangeType.Changed,
                },
            ]);

            ProjectTestSupport.AssertNoProjectError(session, ctx, boxURI);
            Assert.True(session.Snapshot().GetFile("/home/project/app.box") is not null, "overlay should remain until didClose");
            Assert.True(session.Snapshot().GetDefaultProject(boxURI) is null, "unsupported file should not be in a project");

            session.WaitForBackgroundTasks();
            Assert.Equal(0, spawner.Closes); // live old snapshot should retain the mapper process
            Assert.Null(releaseOldSnapshot!());
            Assert.Equal(1, spawner.Closes); // process should close after the final live snapshot is released
            var calls = utils.Client().RegisterContentMapperExtensionsCalls();
            Assert.True(calls.Length > 0, "expected content mapper registration updates");
            Assert.Empty(calls[^1].Extensions); // expected content mapper extensions to be unregistered

            session.DidCloseFile(ctx, boxURI);
            ProjectTestSupport.AssertNoProjectError(session, ctx, boxURI);
        });
    }

    /// <summary>Go: <c>TestContentMapperProcessSharedAcrossProjects</c>（contentmapper_test.go:863-916）。</summary>
    [Fact]
    public void ProcessSharedAcrossProjects()
    {
        static string Config(string extension) => $$"""
            {
            			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler" },
            			"contentMappers": [ { "package": "mapper", "extensions": [{{extension}}] } ]
            		}
            """;

        var files = new Dictionary<string, object?>
        {
            ["/home/a/tsconfig.json"] = Config("\".box\""),
            ["/home/a/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
            ["/home/a/app.box"] = "export const a = 1;\n",
            ["/home/b/tsconfig.json"] = Config("\".panel\""),
            ["/home/b/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
            ["/home/b/app.panel"] = "export const b = 1;\n",
        };
        var (init, utils) = GetSessionInitOptions(files, new SessionOptions
        {
            CurrentDirectory = "/home",
            DefaultLibraryPath = TypeScript.Bundled.Bundled.LibPath(),
            TypingsLocation = TestTypingsLocation,
            PositionEncoding = PositionEncodingKind.UTF8,
            RunExternalCode = true,
        }, null);
        var spawner = new RecordingContentMapperSpawner(NewSpawner());
        init.Spawner = spawner;

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            var aURI = (DocumentUri)"file:///home/a/app.box";
            var bURI = (DocumentUri)"file:///home/b/app.panel";
            session.DidOpenFile(ctx, aURI, 1, (string)files["/home/a/app.box"]!, new LanguageKind("box"));
            ProjectTestSupport.Open(session, ctx, aURI);
            session.DidOpenFile(ctx, bURI, 1, (string)files["/home/b/app.panel"]!, new LanguageKind("panel"));
            ProjectTestSupport.Open(session, ctx, bURI);
            Assert.Equal(1, spawner.Spawns);

            utils.FS().WriteFile("/home/a/tsconfig.json", "{}");
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent { Uri = "file:///home/a/tsconfig.json", Type = FileChangeType.Changed },
            ]);
            ProjectTestSupport.AssertNoProjectError(session, ctx, aURI);
            Assert.Equal(0, spawner.Closes);

            utils.FS().WriteFile("/home/b/tsconfig.json", "{}");
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent { Uri = "file:///home/b/tsconfig.json", Type = FileChangeType.Changed },
            ]);
            ProjectTestSupport.AssertNoProjectError(session, ctx, bURI);
            Assert.Equal(1, spawner.Closes);
        });
    }

    /// <summary>Go: <c>TestContentMapperInferredProjectUsesExtensionContributions</c>（contentmapper_test.go:918-970）。</summary>
    [Fact]
    public void InferredProjectUsesExtensionContributions()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/configured/tsconfig.json"] = """
                {
                			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler" },
                			"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
                		}
                """,
            ["/home/configured/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
            ["/home/configured/main.ts"] = "export const main = true;\n",
            ["/home/loose/app.box"] = "export const version = #{target};\n",
        };
        var (init, _) = GetSessionInitOptions(files, new SessionOptions
        {
            CurrentDirectory = "/home",
            DefaultLibraryPath = TypeScript.Bundled.Bundled.LibPath(),
            TypingsLocation = TestTypingsLocation,
            PositionEncoding = PositionEncodingKind.UTF8,
            RunExternalCode = true,
        }, null);
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            var configuredURI = (DocumentUri)"file:///home/configured/main.ts";
            session.DidOpenFile(ctx, configuredURI, 1, (string)files["/home/configured/main.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, ctx, configuredURI);

            var boxURI = (DocumentUri)"file:///home/loose/app.box";
            session.DidOpenFile(ctx, boxURI, 1, (string)files["/home/loose/app.box"]!, new LanguageKind("box"));
            // Go: assert.ErrorContains(t, err, "no project found", "configured mapper must not leak into inferred projects")
            ProjectTestSupport.AssertNoProjectError(session, ctx, boxURI);
            session.SetContentMapperContributions(ctx, new ContentMapperContributions
            {
                Mappers =
                [
                    new Mapper
                    {
                        Package = "test.extension",
                        Extensions = [".box"],
                        Name = "mapper",
                        Version = "1.0.0",
                        Exec = [TransformingMapper],
                        CompilerOptions = DeclaredOptions,
                        PackageDirectory = "/home",
                        ContributionID = "test.extension[0]",
                    },
                ],
                Extensions = [".box"],
            }, [boxURI]);
            var languageService = ProjectTestSupport.Open(session, ctx, boxURI);
            var defaultProject = session.Snapshot().GetDefaultProject(boxURI);
            Assert.NotNull(defaultProject);
            Assert.Equal(KindInferred, defaultProject.Kind);
            var boxFile = languageService.GetProgram()!.GetSourceFile("/home/loose/app.box");
            Assert.NotNull(boxFile);
            Assert.True(boxFile.ContentMapper() != "", "expected loose app.box to use the extension contribution");
            Assert.True(
                !boxFile.Text().Contains("#{target}", StringComparison.Ordinal),
                $"expected loose app.box to be transformed: \"{boxFile.Text()}\"");
        });
    }

    /// <summary>Go: <c>TestContentMapperInferredProjectSurvivesTypingsInstall</c>（contentmapper_test.go:972-1040）。</summary>
    [Fact]
    public void InferredProjectSurvivesTypingsInstall()
    {
        // A loose content-mapped file lands in the inferred project with an extension content mapper.
        // When ATA finishes installing typings, the inferred program rebuilds with the
        // typings-augmented command line; if that command line drops the content mappers, the
        // otherwise unsupported root file is parsed as plain TypeScript with an unknown script kind and the
        // server panics.
        var files = new Dictionary<string, object?>
        {
            ["/home/configured/tsconfig.json"] = """
                {
                			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler" },
                			"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
                		}
                """,
            ["/home/configured/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
            ["/home/configured/main.ts"] = "export const main = true;\n",
            ["/home/loose/app.box"] = "export const version = #{target};\n",
            ["/home/package.json"] = """{"name":"loose","dependencies":{"jquery":"^3.1.0"}}""",
        };
        var (init, utils) = GetSessionInitOptions(files, new SessionOptions
        {
            CurrentDirectory = "/home",
            DefaultLibraryPath = TypeScript.Bundled.Bundled.LibPath(),
            TypingsLocation = TestTypingsLocation,
            PositionEncoding = PositionEncodingKind.UTF8,
            LoggingEnabled = true,
            RunExternalCode = true,
        }, new TypingsInstallerOptions
        {
            PackageToFile = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["jquery"] = """declare const $: { x: number }""",
            },
        });
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            session.SetContentMapperContributions(ctx, new ContentMapperContributions
            {
                Mappers =
                [
                    new Mapper
                    {
                        Package = "test.extension",
                        Extensions = [".box"],
                        Name = "mapper",
                        Version = "1.0.0",
                        Exec = [TransformingMapper],
                        CompilerOptions = DeclaredOptions,
                        PackageDirectory = "/home",
                        ContributionID = "test.extension[0]",
                    },
                ],
                Extensions = [".box"],
            }, []);
            var configuredURI = (DocumentUri)"file:///home/configured/main.ts";
            session.DidOpenFile(ctx, configuredURI, 1, (string)files["/home/configured/main.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, ctx, configuredURI);

            var boxURI = (DocumentUri)"file:///home/loose/app.box";
            session.DidOpenFile(ctx, boxURI, 1, (string)files["/home/loose/app.box"]!, new LanguageKind("box"));
            ProjectTestSupport.Open(session, ctx, boxURI);
            var defaultProject = session.Snapshot().GetDefaultProject(boxURI);
            Assert.NotNull(defaultProject);
            Assert.Equal(KindInferred, defaultProject.Kind);

            // Let ATA install the typings in the background.
            session.WaitForBackgroundTasks();
            Assert.True(utils.NpmExecutor().NpmInstallCalls().Length > 0, "expected ATA to install typings");

            // Applying the typings change rebuilds the inferred program with the typings-augmented
            // command line. The content mappers must survive that rebuild.
            var languageService = ProjectTestSupport.Open(session, ctx, boxURI);
            var boxFile = languageService.GetProgram()!.GetSourceFile("/home/loose/app.box");
            Assert.NotNull(boxFile);
            Assert.True(boxFile.ContentMapper() != "", "expected loose app.box to keep its content mapper after typings install");
            Assert.True(
                !boxFile.Text().Contains("#{target}", StringComparison.Ordinal),
                $"expected loose app.box to be transformed after typings install: \"{boxFile.Text()}\"");
            TypeScript.Ast.SourceFile? typingsFile = null;
            foreach (var file in languageService.GetProgram()!.SourceFiles())
            {
                if (file.FileName().EndsWith("@types/jquery/index.d.ts", StringComparison.Ordinal))
                {
                    typingsFile = file;
                    break;
                }
            }

            Assert.True(typingsFile is not null, "expected installed typings in the inferred program (the typings-augmented rebuild did not happen)");
        });
    }

    /// <summary>Go: <c>TestContentMapperCreatedFileAdoptedByConfiguredProject</c>（contentmapper_test.go:1042-1080）。</summary>
    [Fact]
    public void CreatedFileAdoptedByConfiguredProject()
    {
        // A content-mapped file created while the server is running must be adopted by the
        // configured project: the created-file root matching has to account for the content
        // mapper extensions, otherwise the file falls into the inferred project until a full
        // project reload.
        var files = new Dictionary<string, object?>
        {
            ["/home/project/tsconfig.json"] = """
                {
                			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler" },
                			"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
                		}
                """,
            ["/home/project/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
            ["/home/project/main.ts"] = "export const main = true;\n",
        };
        var (init, utils) = GetSessionInitOptions(files, Options(runExternalCode: true), null);
        init.Spawner = NewSpawner();

        WithSession(init, session =>
        {
            var ctx = Context.Background;
            var mainURI = (DocumentUri)"file:///home/project/main.ts";
            session.DidOpenFile(ctx, mainURI, 1, (string)files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, ctx, mainURI);

            utils.FS().WriteFile("/home/project/new.box", "export const version = #{target};\n");
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent { Uri = "file:///home/project/new.box", Type = FileChangeType.Created },
            ]);
            var boxURI = (DocumentUri)"file:///home/project/new.box";
            session.DidOpenFile(ctx, boxURI, 1, "export const version = #{target};\n", new LanguageKind("box"));
            var languageService = ProjectTestSupport.Open(session, ctx, boxURI);
            var defaultProject = session.Snapshot().GetDefaultProject(boxURI);
            Assert.NotNull(defaultProject);
            Assert.Equal(KindConfigured, defaultProject.Kind);
            var boxFile = languageService.GetProgram()!.GetSourceFile("/home/project/new.box");
            Assert.NotNull(boxFile);
            Assert.True(
                !boxFile.Text().Contains("#{target}", StringComparison.Ordinal),
                $"expected new.box to be transformed: \"{boxFile.Text()}\"");
        });
    }
}

/// <summary>Go: <c>TestContentMapperInProject</c>（contentmapper_test.go:141-292）。</summary>
public class ProjectContentMapperInProjectTests
{
    private static readonly Dictionary<string, object?> _files = new()
    {
        ["/home/project/tsconfig.json"] = """
            {
            			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler", "strict": true },
            			"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
            		}
            """,
        ["/home/project/node_modules/mapper/package.json"] = PackageJSON(TransformingMapper),
        ["/home/project/app.box"] = "export const version = #{target};\n",
        ["/home/project/main.ts"] = "import { version } from \"./app.box\";\nexport const twice: number = version * 2;\n",
    };

    private static (SessionInit Init, SessionUtils Utils) NewSession(bool trusted)
    {
        var options = new SessionOptions
        {
            CurrentDirectory = "/home/project",
            DefaultLibraryPath = TypeScript.Bundled.Bundled.LibPath(),
            TypingsLocation = TestTypingsLocation,
            PositionEncoding = PositionEncodingKind.UTF8,
            LoggingEnabled = true,
            RunExternalCode = trusted,
        };
        var (init, utils) = GetSessionInitOptions(_files, options, null);
        init.Spawner = NewSpawner();
        return (init, utils);
    }

    /// <summary>Go: <c>TestContentMapperInProject/trusted workspace transforms the content-mapped file</c>。</summary>
    [Fact]
    public void TrustedWorkspaceTransformsTheContentMappedFile()
    {
        var (init, utils) = NewSession(true);
        WithSession(init, session =>
        {
            session.DidOpenFile(Context.Background, "file:///home/project/main.ts", 1, (string)_files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            var ls = ProjectTestSupport.Open(session, Context.Background, "file:///home/project/main.ts");

            var boxFile = ls.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.NotNull(boxFile);
            // The #{target} token was substituted with the es2020 target value (7) by the content mapper.
            Assert.True(
                boxFile.Text().Contains("export const version = 7;", StringComparison.Ordinal),
                $"app.box was not transformed: \"{boxFile.Text()}\"");

            // The config's .box mapper should have been registered for text document synchronization.
            session.WaitForBackgroundTasks();
            var calls = utils.Client().RegisterContentMapperExtensionsCalls();
            Assert.True(calls.Length > 0, "expected RegisterContentMapperExtensions to be called");
            Assert.Equal(new[] { ".box" }, calls[^1].Extensions);
        });
    }

    /// <summary>Go: <c>TestContentMapperInProject/untrusted workspace does not run the content mapper</c>。</summary>
    [Fact]
    public void UntrustedWorkspaceDoesNotRunTheContentMapper()
    {
        var (init, utils) = NewSession(false);
        WithSession(init, session =>
        {
            session.DidOpenFile(Context.Background, "file:///home/project/main.ts", 1, (string)_files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            var ls = ProjectTestSupport.Open(session, Context.Background, "file:///home/project/main.ts");

            // Without workspace trust, the content mapper gate drops the mappers, so .box is not a recognized
            // extension and app.box never enters the program.
            var boxFile = ls.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.True(boxFile is null, "app.box should not be loaded without trust");

            // No content mapper extensions should be registered without trust.
            session.WaitForBackgroundTasks();
            foreach (var call in utils.Client().RegisterContentMapperExtensionsCalls())
            {
                Assert.Empty(call.Extensions);
            }
        });
    }

    /// <summary>Go: <c>TestContentMapperInProject/editing an open content-mapped file reparses it through the mapper</c>。</summary>
    [Fact]
    public void EditingAnOpenContentMappedFileReparsesItThroughTheMapper()
    {
        var (init, _) = NewSession(true);
        WithSession(init, session =>
        {
            var ctx = Context.Background;
            session.DidOpenFile(ctx, "file:///home/project/main.ts", 1, (string)_files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            // Open the .box with its content-mapped language id so its overlay script kind is Unknown, matching how an
            // editor opens a content-mapped file. This is what made the incremental reparse panic.
            session.DidOpenFile(ctx, "file:///home/project/app.box", 1, (string)_files["/home/project/app.box"]!, new LanguageKind("box"));
            ProjectTestSupport.Open(session, ctx, "file:///home/project/main.ts");

            // Editing the open .box file drives the single-file incremental reparse path
            // (Program.UpdateProgram), which must re-run the content mapper transform rather than parse the
            // raw source text.
            session.DidChangeFile(ctx, "file:///home/project/app.box", 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    WholeDocument = new TextDocumentContentChangeWholeDocument
                    {
                        Text = "export const version = #{target};\nexport const extra = 1;\n",
                    },
                },
            ]);
            var ls = ProjectTestSupport.Open(session, ctx, "file:///home/project/main.ts");

            var boxFile = ls.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.NotNull(boxFile);
            Assert.True(
                boxFile.Text().Contains("export const version = 7;", StringComparison.Ordinal),
                $"reparsed app.box was not transformed: \"{boxFile.Text()}\"");
            Assert.True(
                boxFile.Text().Contains("export const extra = 1;", StringComparison.Ordinal),
                $"reparsed app.box missing the edit: \"{boxFile.Text()}\"");
        });
    }

    /// <summary>Go: <c>TestContentMapperInProject/watch change to a content-mapped file updates the program</c>。</summary>
    [Fact]
    public void WatchChangeToAContentMappedFileUpdatesTheProgram()
    {
        var (init, utils) = NewSession(true);
        WithSession(init, session =>
        {
            var ctx = Context.Background;
            var mainURI = (DocumentUri)"file:///home/project/main.ts";
            session.DidOpenFile(ctx, mainURI, 1, (string)_files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            var languageService = ProjectTestSupport.Open(session, ctx, mainURI);
            var original = languageService.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.NotNull(original);

            // Wait until the configured extension set has been published; watch filtering uses the set captured
            // when the snapshot change is created.
            session.WaitForBackgroundTasks();
            const string updatedContent = "export const version = #{target};\nexport const watched = true;\n";
            utils.FS().WriteFile("/home/project/app.box", updatedContent);
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent
                {
                    Uri = "file:///home/project/app.box",
                    Type = FileChangeType.Changed,
                },
            ]);

            languageService = ProjectTestSupport.Open(session, ctx, mainURI);
            var updatedSnapshot = session.Snapshot();
            var configuredProject = updatedSnapshot.GetDefaultProject(mainURI);
            Assert.NotNull(configuredProject);
            Assert.Equal(ProgramUpdateKindCloned, configuredProject.ProgramUpdateKind);
            Assert.Equal(updatedSnapshot.ID(), configuredProject.ProgramLastUpdate);
            var updated = languageService.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.NotNull(updated);
            Assert.NotSame(original, updated);
            Assert.True(
                updated.Text().Contains("export const version = 7;", StringComparison.Ordinal),
                $"updated app.box was not transformed: \"{updated.Text()}\"");
            Assert.True(
                updated.Text().Contains("export const watched = true;", StringComparison.Ordinal),
                $"updated app.box missing watched change: \"{updated.Text()}\"");
        });
    }

    /// <summary>Go: <c>TestContentMapperInProject/unchanged content-mapped file is reused from the cache across a full rebuild</c>。</summary>
    [Fact]
    public void UnchangedContentMappedFileIsReusedFromTheCacheAcrossAFullRebuild()
    {
        var (init, utils) = NewSession(true);
        WithSession(init, session =>
        {
            var ctx = Context.Background;
            session.DidOpenFile(ctx, "file:///home/project/main.ts", 1, (string)_files["/home/project/main.ts"]!, LanguageKind.TypeScript);
            var ls = ProjectTestSupport.Open(session, ctx, "file:///home/project/main.ts");
            var boxFile = ls.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.NotNull(boxFile);
            Assert.True(
                boxFile.Text().Contains("export const version = 7;", StringComparison.Ordinal),
                $"app.box was not transformed: \"{boxFile.Text()}\"");

            // Changing a compiler option the mapper does not depend on (strict) forces a full program
            // rebuild while leaving app.box's content and the mapper's transform identity unchanged, so the
            // transformed file must be served from the parse cache rather than re-transformed.
            utils.FS().WriteFile("/home/project/tsconfig.json", """
                {
                			"compilerOptions": { "target": "es2020", "module": "esnext", "moduleResolution": "bundler", "strict": false },
                			"contentMappers": [ { "package": "mapper", "extensions": [".box"] } ]
                		}
                """);
            session.DidChangeWatchedFiles(ctx,
            [
                new FileEvent { Uri = "file:///home/project/tsconfig.json", Type = FileChangeType.Changed },
            ]);

            ls = ProjectTestSupport.Open(session, ctx, "file:///home/project/main.ts");
            var rebuiltBox = ls.GetProgram()!.GetSourceFile("/home/project/app.box");
            Assert.Same(boxFile, rebuiltBox);
        });
    }
}
// ported from: tsc/internal/project/overlayfs_test.go:1-377
//
// 说明（与 Go 测试的差异，逐条）：
//
// - Go 的 `assert.Assert(t, x)` / `assert.Equal(t, want, got)` / `assert.NilError(t, err)` →
//   xunit 的 `Assert.True(x)` / `Assert.Equal(want, got)`（xunit 参数顺序是 (expected, actual)）；
//   Go 的 `assert.NilError` 在 C# 里没有对应物 —— `FS.WriteFile/Remove` 失败**抛异常**（见 Vfs.cs 文件头），
//   成功路径什么都不用断言。
// - Go 的 `t.Run("…")` 子测试 → 每个子测试一个 `[Fact]`，方法名 = 父测试名 + 子测试名的 PascalCase 组合。
// - Go 用 `recover()` 断言 panic → C# 用 `Assert.Throws<DebugFailureException>`（`Debug.Fail` 抛的异常，
//   见 Debug/Debug.cs）。
// - Go 的 `&Overlay{fileBase: fileBase{fileName: …, content: …}}` 结构体字面量 → C# 的对象初始化器
//   （`Overlay` 的字段是 internal，测试靠 InternalsVisibleTo("TypeScript.Compiler.Tests") 访问）。
// - Go 的 `map[tspath.Path]*Overlay` / `map[tspath.Path]RealpathAliasSet` → `Dictionary<Path, …>`。
// - Go 的 `newOverlayFS(...)` / `newOverlay(...)` → `OverlayFS.NewOverlayFS(...)` / `Overlay.NewOverlay(...)`。
// - Go 的 `slices.Contains(rootEntries.Directories, "virtual")` → `Assert.Contains("virtual", rootEntries.Directories)`
//   （xunit 的 `Assert.Contains(expected, collection)`；数组是 IEnumerable<string>）。
//
// **本切片新增的用例（1 条，Go 文件里没有）**：`TestProcessChangesPartialChangeAppliesEdit` —— Go 的
// overlayfs_test.go 只构造 WholeDocument 变更，partial 变更（`Converters.FromLSPRange` + `TextChange.ApplyTo`
// 那条分支）从未被执行；该用例补上它，用来**运行时**证明 `Overlay : lsconv.Script` 的集成成立。
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.Vfs.VfsTest;
using Path = TypeScript.TSPath.Path;
using Range = TypeScript.LsProto.Range;
using Xunit;

namespace TypeScript.Tests;

public class ProjectOverlayFsTests
{
    private static readonly DocumentUri _testURI1 = "file:///test1.ts";

    private static readonly DocumentUri _testURI2 = "file:///test2.ts";

    private static readonly Func<string, Path> _toPath = fileName => (Path)fileName;

    private static OverlayFS CreateOverlayFS()
    {
        var testFS = VfsTest.FromMap(
            new Dictionary<string, string>
            {
                ["/test1.ts"] = "// existing content",
                ["/test2.ts"] = "// existing content",
                ["/script"] = "// extensionless content",
            },
            false /* useCaseSensitiveFileNames */);
        return OverlayFS.NewOverlayFS(
            testFS,
            new Dictionary<Path, Overlay>(),
            PositionEncodingKind.UTF16,
            _toPath);
    }

    private static FileChange Open(DocumentUri uri, int version, string content, LanguageKind languageKind)
    {
        return new FileChange
        {
            Kind = FileChangeKind.Open,
            URI = uri,
            Version = version,
            Content = content,
            LanguageKind = languageKind,
        };
    }

    [Fact]
    public void TestProcessChangesMultipleOpensShouldPanic()
    {
        var fs = CreateOverlayFS();

        var changes = new[]
        {
            Open(_testURI1, 1, "const x = 1;", LanguageKind.TypeScript),
            Open(_testURI2, 1, "const y = 2;", LanguageKind.TypeScript),
        };

        Assert.Throws<DebugFailureException>(() => fs.ProcessChanges(changes));
    }

    [Fact]
    public void TestProcessChangesWatchCreateThenDeleteBecomesNothing()
    {
        var fs = CreateOverlayFS();

        var changes = new[]
        {
            new FileChange { Kind = FileChangeKind.WatchCreate, URI = _testURI1 },
            new FileChange { Kind = FileChangeKind.WatchDelete, URI = _testURI1 },
        };

        var (result, _) = fs.ProcessChanges(changes);
        Assert.True(result.IsEmpty());
    }

    [Fact]
    public void TestProcessChangesWatchDeleteThenCreateBecomesChange()
    {
        var fs = CreateOverlayFS();

        var changes = new[]
        {
            new FileChange { Kind = FileChangeKind.WatchDelete, URI = _testURI1 },
            new FileChange { Kind = FileChangeKind.WatchCreate, URI = _testURI1 },
        };

        var (result, _) = fs.ProcessChanges(changes);

        Assert.Equal(0, result.Created.Len());
        Assert.Equal(0, result.Deleted.Len());
        Assert.True(result.Changed.Has(_testURI1));
    }

    [Fact]
    public void TestProcessChangesMultipleWatchChangesDeduplicated()
    {
        var fs = CreateOverlayFS();

        var changes = new[]
        {
            new FileChange { Kind = FileChangeKind.WatchChange, URI = _testURI1 },
            new FileChange { Kind = FileChangeKind.WatchChange, URI = _testURI1 },
            new FileChange { Kind = FileChangeKind.WatchChange, URI = _testURI1 },
        };

        var (result, _) = fs.ProcessChanges(changes);

        Assert.True(result.Changed.Has(_testURI1));
        Assert.Equal(1, result.Changed.Len());
    }

    [Fact]
    public void TestProcessChangesSaveMarksOverlayAsMatchingDisk()
    {
        var fs = CreateOverlayFS();

        // First create an overlay
        fs.ProcessChanges([Open(_testURI1, 1, "const x = 1;", LanguageKind.TypeScript)]);
        // Then save
        var (result, _) = fs.ProcessChanges([new FileChange { Kind = FileChangeKind.Save, URI = _testURI1 }]);
        // We don't observe saves for snapshot changes,
        // so they're not included in the summary
        Assert.True(result.IsEmpty());

        // Check that the overlay is marked as matching disk text
        var fh = fs.GetFile(_testURI1.FileName());
        Assert.NotNull(fh);
        Assert.True(fh.MatchesDiskText());
    }

    [Fact]
    public void TestProcessChangesOpenFallsBackToFileExtensionForUnknownLanguageKind()
    {
        var fs = CreateOverlayFS();
        DocumentUri uri = "file:///test1.mts";

        fs.ProcessChanges([Open(uri, 1, "export const x = 1;", new LanguageKind("mts"))]);

        var fh = fs.GetFile(uri.FileName());
        Assert.NotNull(fh);
        Assert.Equal(ScriptKind.TS, fh.Kind());
    }

    [Fact]
    public void TestProcessChangesOpenExtensionlessFilePreservesUnknownScriptKind()
    {
        var fs = CreateOverlayFS();
        DocumentUri uri = "file:///script";

        fs.ProcessChanges([Open(uri, 1, "const x = 1;", new LanguageKind("plaintext"))]);

        var fh = fs.GetFile(uri.FileName());
        Assert.NotNull(fh);
        Assert.Equal(ScriptKind.Unknown, fh.Kind());
    }

    [Fact]
    public void TestProcessChangesExtensionlessDiskFilePreservesUnknownScriptKind()
    {
        var fs = CreateOverlayFS();

        var fh = fs.GetFile("/script");
        Assert.NotNull(fh);
        Assert.Equal(ScriptKind.Unknown, fh.Kind());
    }

    [Fact]
    public void TestProcessChangesWatchChangeOnOverlayMarksAsNotMatchingDisk()
    {
        var fs = CreateOverlayFS();

        // First create an overlay
        fs.ProcessChanges([Open(_testURI1, 1, "const x = 1;", LanguageKind.TypeScript)]);
        Assert.False(fs.GetFile(_testURI1.FileName())!.MatchesDiskText());

        // Then save
        fs.ProcessChanges([new FileChange { Kind = FileChangeKind.Save, URI = _testURI1 }]);
        Assert.True(fs.GetFile(_testURI1.FileName())!.MatchesDiskText());

        // Now process a watch change
        fs.ProcessChanges([new FileChange { Kind = FileChangeKind.WatchChange, URI = _testURI1 }]);
        Assert.False(fs.GetFile(_testURI1.FileName())!.MatchesDiskText());
    }

    [Fact]
    public void TestProcessChangesSaveWithoutOverlayShouldNotPanic()
    {
        var fs = CreateOverlayFS();

        // Save a file that was never opened (no overlay exists).
        // This can happen when an editor sends didSave for a file
        // that is not managed by the LSP server (e.g., package.json).
        var (result, _) = fs.ProcessChanges([new FileChange { Kind = FileChangeKind.Save, URI = _testURI1 }]);
        // Should be treated as a disk change
        Assert.True(result.Changed.Has(_testURI1));
    }

    [Fact]
    public void TestProcessChangesCloseAndChangeWithoutOverlayShouldNotPanic()
    {
        var fs = CreateOverlayFS();

        fs.ProcessChanges([Open(_testURI1, 1, "const x = 1;", LanguageKind.TypeScript)]);
        fs.ProcessChanges([new FileChange { Kind = FileChangeKind.Close, URI = _testURI1 }]);

        var (result, _) = fs.ProcessChanges([new FileChange { Kind = FileChangeKind.Close, URI = _testURI1 }]);

        Assert.True(result.IsEmpty());

        (result, _) = fs.ProcessChanges(
        [
            new FileChange
            {
                Kind = FileChangeKind.Change,
                URI = _testURI1,
                Version = 2,
                Changes =
                [
                    new TextDocumentContentChangePartialOrWholeDocument
                    {
                        WholeDocument = new TextDocumentContentChangeWholeDocument { Text = "const x = 1;" },
                    },
                ],
            },
        ]);

        Assert.True(result.IsEmpty());
    }

    [Fact]
    public void TestProcessChangesCloseThenOpenInSameBatchMarksAsChanged()
    {
        var fs = CreateOverlayFS();

        // First create an overlay
        fs.ProcessChanges([Open(_testURI1, 1, "const x = 1;", LanguageKind.TypeScript)]);

        // Now close and reopen in the same batch (like Neovim does for file reload)
        var (result, _) = fs.ProcessChanges(
        [
            new FileChange { Kind = FileChangeKind.Close, URI = _testURI1 },
            Open(_testURI1, 0, "const x = 2;", LanguageKind.TypeScript),
        ]);

        // Should not be marked as opened since it was already open
        Assert.True(result.Opened == default, "close then open should not mark as opened");
        // Should also be marked as changed since it was closed and reopened
        Assert.True(result.Changed.Has(_testURI1), "close then open should mark as changed");
        // Should have the new content
        var fh = fs.GetFile(_testURI1.FileName());
        Assert.Equal("const x = 2;", fh!.Content());
    }

    [Fact]
    public void TestProcessChangesPartialChangeAppliesEdit()
    {
        // 本切片**新增**（Go 的 overlayfs_test.go 未覆盖）：Go 的 Change 事件全用 WholeDocument，
        // 这条走 `Converters.FromLSPRange` + `core.TextChange.ApplyTo` 的分支（`TextChange` 分支里的
        // Debug.Assert 只有在 partial 路径才会执行），是 `Overlay` 满足 `lsconv.Script` 的**运行时**证明。
        var fs = CreateOverlayFS();

        fs.ProcessChanges([Open(_testURI1, 1, "const x = 1;", LanguageKind.TypeScript)]);

        var (result, _) = fs.ProcessChanges(
        [
            new FileChange
            {
                Kind = FileChangeKind.Change,
                URI = _testURI1,
                Version = 2,
                Changes =
                [
                    new TextDocumentContentChangePartialOrWholeDocument
                    {
                        Partial = new TextDocumentContentChangePartial
                        {
                            Range = new Range
                            {
                                Start = new Position { Line = 0, Character = 6 },
                                End = new Position { Line = 0, Character = 7 },
                            },
                            Text = "y",
                        },
                    },
                ],
            },
        ]);

        Assert.True(result.Changed.Has(_testURI1));
        Assert.Equal(2, fs.GetFile(_testURI1.FileName())!.Version());
        Assert.Equal("const y = 1;", fs.GetFile(_testURI1.FileName())!.Content());
    }

    [Fact]
    public void TestOverlayFSFileSystem()
    {
        var host = VfsTest.FromMap(new Dictionary<string, string> { ["/virtual"] = "host file" }, false /* useCaseSensitiveFileNames */);
        var overlays = new Dictionary<Path, Overlay>
        {
            [(Path)"/virtual/nested/file.ts"] = Overlay.NewOverlay("/virtual/nested/file.ts", "overlay", 1, ScriptKind.TS),
        };
        var fileSystem = OverlayFS.NewOverlayFS(host, overlays, PositionEncodingKind.UTF16, _toPath);

        Assert.True(fileSystem.DirectoryExists("/virtual"));
        Assert.False(fileSystem.FileExists("/virtual"));
        Assert.True(fileSystem.Stat("/virtual")!.IsDirectory);
        Assert.True(fileSystem.TryReadFile("/virtual/nested/file.ts", out var content));
        Assert.Equal("overlay", content);

        var rootEntries = fileSystem.GetAccessibleEntries("/");
        Assert.Contains("virtual", rootEntries.Directories);
        Assert.DoesNotContain("virtual", rootEntries.Files);
    }
}

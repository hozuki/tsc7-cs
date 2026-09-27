// ported from: internal/vfs/vfstest/vfstest_test.go
//
// 说明：
// - `convertMapFS`（未导出）→ `VfsTest.ConvertMapFS`（internal，靠 Properties/AssemblyInfo.cs 的
//   InternalsVisibleTo("TypeScript.Compiler.Tests") 可见）。输入键与 Go 一致：**不带前导 '/'** 的
//   canonical path；clock 传 null（Go 的 nil）→ 内部回落到 SystemClock。
// - Go 的 `fs.ReadFile(fs, p)` / `fs.Stat(fs, p)` / `fs.ReadDir(fs, p)`（io/fs 的 stdlib 助手）没有
//   C# 等价物：`convertMapFS` 建出来的 `*MapFS` 本身就实现 IIoFs，这些用例直接调 `MapFS` 的
//   `TryReadFile`/`Stat`/`ReadDir`；`FromMap` 建出来的是 iovfs 包装后的 `FS`，调 `FS` 的
//   `TryReadFile`/`Stat`/`FileExists`/`DirectoryExists`。`dirEntriesToNames` 用 `entry.Name`（字段）。
// - Go 的失败语义是 error（"file does not exist"），C# 侧是 null/false（见 Vfs.cs 文件头），
//   所以 `assert.ErrorContains(err, "file does not exist")` 这类断言改写成 `Assert.Null`/`Assert.False`。
// - Go 的 panic → `DebugFailureException`，消息带 "Debug failure. " 前缀（Debug.Fail 加的），
//   故 panic 用例用 `Assert.Contains`；`invalid file type int` → `invalid file type Int32`（.NET 类型名）。
//   `assert.Error(t, err, msg)`（精确比较）→ `Assert.Equal(msg, ex.Message)`；经 iovfs 包装后最终冒出来
//   的是 mkdirAll 那层的异常，消息保持未被 `write "path": ` 前缀包装（见各用例注释）。
// - 跳过（无法移植）的断言：
//     * `assert.Equal(t, xxxInfo.Sys(), 1234)`（vfstest_test.go:43,63,106,118,151）：C# 的 FileInfo
//       没有 Sys()（Go 用它挂 realpath；C# 由 MapFS 内部的 MapEntry 承载，见 Vfs.cs 文件头）。
//     * `fstest.TestFS(vfs, …)`（vfstest_test.go:56,123,153；78,111 在 Go 里本就是注释）：
//       stdlib 的 FS 一致性检查器，C# 没有等价物。
// - Go 的 t.Parallel() 交给 xunit 调度，不逐字搬；每个 t.Run 子测试 → 独立 [Fact]（`<GoTest>_<SubTest>`）。
// - 本文件没有 Benchmark。
using System.Text;
using TypeScript.Vfs;
using TypeScript.Vfs.VfsTest;
using Xunit;

using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class VfsTestTests
{
    // Go: dirEntriesToNames(entries)
    private static string[] DirEntriesToNames(DirEntry[] entries)
    {
        var names = new string[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            names[i] = entries[i].Name;
        }

        return names;
    }

    // Go: FromMap[any](nil, false) —— Go 的 map[string]any（any 即 object）。
    private static FS EmptyMapFs()
    {
        return FromMap(new Dictionary<string, object>(), false);
    }

    [Fact]
    public void TestInsensitive()
    {
        const string contents = "bar";

        var vfs = ConvertMapFS(new Dictionary<string, MapFile>
        {
            ["foo/bar/baz"] = new MapFile { Data = contents },
            ["foo/bar2/baz2"] = new MapFile { Data = contents },
            ["foo/bar3/baz3"] = new MapFile { Data = contents },
        }, false /*useCaseSensitiveFileNames*/, null);

        Assert.True(vfs.TryReadFile("foo/bar/baz", out var sensitive));
        Assert.Equal(contents, sensitive);
        var sensitiveInfo = vfs.Stat("foo/bar/baz");
        Assert.NotNull(sensitiveInfo);
        // Go: assert.Equal(t, sensitiveInfo.Sys(), 1234) —— 跳过，FileInfo 没有 Sys()。
        Assert.Equal("foo/bar/baz", vfs.Realpath("foo/bar/baz"));
        Assert.Equal(new[] { "bar", "bar2", "bar3" }, DirEntriesToNames(vfs.ReadDir("foo")!));

        // Go: vfs.Realpath("does/not/exist") 报 err "file does not exist"；C# 侧 Realpath 失败返回 null。
        Assert.Null(vfs.Realpath("does/not/exist"));
        // Go: fs.Stat(vfs, "does/not/exist") 报 err "file does not exist"；C# 侧 Stat 不存在时返回 null。
        Assert.Null(vfs.Stat("does/not/exist"));

        // Go: assert.NilError(t, fstest.TestFS(vfs, "foo/bar/baz")) —— 跳过，fstest.TestFS 无等价物。

        Assert.True(vfs.TryReadFile("Foo/Bar/Baz", out var insensitive));
        Assert.Equal(contents, insensitive);
        var insensitiveInfo = vfs.Stat("Foo/Bar/Baz");
        Assert.NotNull(insensitiveInfo);
        // Go: assert.Equal(t, insensitiveInfo.Sys(), 1234) —— 跳过，FileInfo 没有 Sys()。
        Assert.Equal("foo/bar/baz", vfs.Realpath("Foo/Bar/Baz"));
        Assert.Equal(new[] { "bar", "bar2", "bar3" }, DirEntriesToNames(vfs.ReadDir("Foo")!));

        Assert.Null(vfs.Realpath("Does/Not/Exist"));
        Assert.Null(vfs.Stat("Does/Not/Exist"));

        // Go 在此有一行注释掉的 fstest.TestFS(vfs, "Foo/Bar/Baz") 与 TODO（TestFS 不懂大小写不敏感 FS）。
    }

    [Fact]
    public void TestInsensitiveUpper()
    {
        const string contents = "bar";

        var vfs = ConvertMapFS(new Dictionary<string, MapFile>
        {
            ["Foo/Bar/Baz"] = new MapFile { Data = contents },
            ["Foo/Bar2/Baz2"] = new MapFile { Data = contents },
            ["Foo/Bar3/Baz3"] = new MapFile { Data = contents },
        }, false /*useCaseSensitiveFileNames*/, null);

        Assert.True(vfs.TryReadFile("foo/bar/baz", out var sensitive));
        Assert.Equal(contents, sensitive);
        var sensitiveInfo = vfs.Stat("foo/bar/baz");
        Assert.NotNull(sensitiveInfo);
        // Go: assert.Equal(t, sensitiveInfo.Sys(), 1234) —— 跳过，FileInfo 没有 Sys()。
        Assert.Equal(new[] { "Bar", "Bar2", "Bar3" }, DirEntriesToNames(vfs.ReadDir("foo")!));

        // Go 在此有一行注释掉的 fstest.TestFS(vfs, "foo/bar/baz") —— 跳过。

        Assert.True(vfs.TryReadFile("Foo/Bar/Baz", out var insensitive));
        Assert.Equal(contents, insensitive);
        var insensitiveInfo = vfs.Stat("Foo/Bar/Baz");
        Assert.NotNull(insensitiveInfo);
        // Go: assert.Equal(t, insensitiveInfo.Sys(), 1234) —— 跳过，FileInfo 没有 Sys()。
        Assert.Equal(new[] { "Bar", "Bar2", "Bar3" }, DirEntriesToNames(vfs.ReadDir("Foo")!));

        // Go: assert.NilError(t, fstest.TestFS(vfs, "Foo/Bar/Baz")) —— 跳过，fstest.TestFS 无等价物。
    }

    [Fact]
    public void TestSensitive()
    {
        const string contents = "bar";

        var vfs = ConvertMapFS(new Dictionary<string, MapFile>
        {
            ["foo/bar/baz"] = new MapFile { Data = contents },
            ["foo/bar2/baz2"] = new MapFile { Data = contents },
            ["foo/bar3/baz3"] = new MapFile { Data = contents },
        }, true /*useCaseSensitiveFileNames*/, null);

        Assert.True(vfs.TryReadFile("foo/bar/baz", out var sensitive));
        Assert.Equal(contents, sensitive);
        var sensitiveInfo = vfs.Stat("foo/bar/baz");
        Assert.NotNull(sensitiveInfo);
        // Go: assert.Equal(t, sensitiveInfo.Sys(), 1234) —— 跳过，FileInfo 没有 Sys()。

        // Go: assert.NilError(t, fstest.TestFS(vfs, "foo/bar/baz")) —— 跳过，fstest.TestFS 无等价物。

        // Go: fs.ReadFile(vfs, "Foo/Bar/Baz") 报 err "file does not exist"；C# 侧 TryReadFile 返回 false。
        Assert.False(vfs.TryReadFile("Foo/Bar/Baz", out var missing));
        Assert.Equal("", missing);
    }

    [Fact]
    public void TestSensitiveDuplicatePath()
    {
        var testFs = new Dictionary<string, MapFile>
        {
            ["foo"] = new MapFile { Data = "bar" },
            ["Foo"] = new MapFile { Data = "baz" },
        };

        // Go: testutil.AssertPanics(t, ..., `duplicate path: "Foo" and "foo" have the same canonical path`)
        //     —— Debug.Fail 会加 "Debug failure. " 前缀，故用 Contains。
        var ex = Assert.Throws<DebugFailureException>(
            () => ConvertMapFS(testFs, false /*useCaseSensitiveFileNames*/, null));
        Assert.Contains("duplicate path: \"Foo\" and \"foo\" have the same canonical path", ex.Message);
    }

    [Fact]
    public void TestInsensitiveDuplicatePath()
    {
        var testFs = new Dictionary<string, MapFile>
        {
            ["foo"] = new MapFile { Data = "bar" },
            ["Foo"] = new MapFile { Data = "baz" },
        };

        // 大小写敏感时两个路径不冲突：不 panic，正常返回。
        Assert.NotNull(ConvertMapFS(testFs, true /*useCaseSensitiveFileNames*/, null));
    }

    [Fact]
    public void TestWritableFS()
    {
        var fs = EmptyMapFs();

        fs.WriteFile("/foo/bar/baz", "hello, world");

        Assert.True(fs.TryReadFile("/foo/bar/baz", out var content));
        Assert.Equal("hello, world", content);

        fs.WriteFile("/foo/bar/baz", "goodbye, world");

        Assert.True(fs.TryReadFile("/foo/bar/baz", out content));
        Assert.Equal("goodbye, world", content);

        // Go: assert.ErrorContains(t, err, `mkdir "foo/bar/baz": path exists but is not a directory`)
        //     —— iovfs 的 WriteFileEnsuringDir 先写失败、再 mkdirAll，最终冒出来的是 mkdirAll 这层的异常。
        var ex = Assert.Throws<IOException>(() => fs.WriteFile("/foo/bar/baz/oops", "goodbye, world"));
        Assert.Contains("mkdir \"foo/bar/baz\": path exists but is not a directory", ex.Message);
    }

    [Fact]
    public void TestWritableFSDelete()
    {
        var fs = EmptyMapFs();

        fs.WriteFile("/foo/bar/file.ts", "remove");
        Assert.True(fs.FileExists("/foo/bar/file.ts"));
        fs.Remove("/foo/bar/file.ts");
        Assert.False(fs.FileExists("/foo/bar/file.ts"));

        fs.WriteFile("/foo/bar/test/remove2.ts", "remove2");
        Assert.True(fs.DirectoryExists("/foo/bar/test"));
        fs.Remove("/foo/bar/test");
        Assert.False(fs.FileExists("/foo/bar/test/remove2.ts"));
        Assert.False(fs.DirectoryExists("/foo/bar/test"));

        // 删除不存在的文件/目录不报错。
        fs.Remove("/foo/bar/test");
        fs.Remove("/foo/bar/file.ts");

        fs.WriteFile("/foo/barbar", "remove2");
        fs.Remove("/foo/bar");
        Assert.True(fs.FileExists("/foo/barbar"));
    }

    [Fact]
    public void TestStress()
    {
        var fs = EmptyMapFs();

        var ops = new Action[]
        {
            () => fs.WriteFile("/foo/bar/baz.txt", "hello, world"),
            () => fs.TryReadFile("/foo/bar/baz.txt", out _),
            () => fs.DirectoryExists("/foo/bar"),
            () => fs.FileExists("/foo/bar"),
            () => fs.FileExists("/foo/bar/baz.txt"),
            () => fs.GetAccessibleEntries("/foo/bar"),
            () => fs.Realpath("/foo/bar/baz.txt"),
            () => fs.Stat("/foo/bar/baz.txt"),
        };

        // Go: runtime.GOMAXPROCS(0) 个 goroutine，每个洗牌后随机跑 10000 次操作；不抛异常即通过。
        Parallel.For(0, Environment.ProcessorCount, _ =>
        {
            var randomOps = (Action[])ops.Clone();
            // Go: rand.Shuffle —— Fisher-Yates + Random.Shared（线程安全）。
            for (var i = randomOps.Length - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (randomOps[i], randomOps[j]) = (randomOps[j], randomOps[i]);
            }

            for (var i = 0; i < 10000; i++)
            {
                randomOps[i % randomOps.Length]();
            }
        });
    }

    [Fact]
    public void TestParentDirFile()
    {
        var testFs = new Dictionary<string, MapFile>
        {
            ["foo"] = new MapFile { Data = "bar" },
            ["foo/oops"] = new MapFile { Data = "baz" },
        };

        // Go: AssertPanics(..., `failed to create intermediate directories for "foo/oops": mkdir "foo": path exists but is not a directory`)
        var ex = Assert.Throws<DebugFailureException>(
            () => ConvertMapFS(testFs, false /*useCaseSensitiveFileNames*/, null));
        Assert.Contains(
            "failed to create intermediate directories for \"foo/oops\": mkdir \"foo\": path exists but is not a directory",
            ex.Message);
    }

    [Fact]
    public void TestFromMap_POSIX()
    {
        var fs = FromMap(new Dictionary<string, object>
        {
            ["/string"] = "hello, world",
            ["/bytes"] = Encoding.UTF8.GetBytes("hello, world"),
            ["/mapfile"] = new MapFile { Data = "hello, world" },
        }, false);

        Assert.True(fs.TryReadFile("/string", out var content));
        Assert.Equal("hello, world", content);

        Assert.True(fs.TryReadFile("/bytes", out content));
        Assert.Equal("hello, world", content);

        Assert.True(fs.TryReadFile("/mapfile", out content));
        Assert.Equal("hello, world", content);
    }

    [Fact]
    public void TestFromMap_Windows()
    {
        var fs = FromMap(new Dictionary<string, object>
        {
            ["c:/string"] = "hello, world",
            ["d:/bytes"] = Encoding.UTF8.GetBytes("hello, world"),
            ["e:/mapfile"] = new MapFile { Data = "hello, world" },
        }, false);

        Assert.True(fs.TryReadFile("c:/string", out var content));
        Assert.Equal("hello, world", content);

        Assert.True(fs.TryReadFile("d:/bytes", out content));
        Assert.Equal("hello, world", content);

        Assert.True(fs.TryReadFile("e:/mapfile", out content));
        Assert.Equal("hello, world", content);
    }

    [Fact]
    public void TestFromMap_Mixed()
    {
        var ex = Assert.Throws<DebugFailureException>(() => FromMap(new Dictionary<string, object>
        {
            ["/string"] = "hello, world",
            ["c:/bytes"] = Encoding.UTF8.GetBytes("hello, world"),
        }, false));
        Assert.Contains("mixed posix and windows paths", ex.Message);
    }

    [Fact]
    public void TestFromMap_NonRooted()
    {
        var ex = Assert.Throws<DebugFailureException>(() => FromMap(new Dictionary<string, object>
        {
            ["string"] = "hello, world",
        }, false));
        Assert.Contains("non-rooted path \"string\"", ex.Message);
    }

    [Fact]
    public void TestFromMap_NonNormalized()
    {
        var ex = Assert.Throws<DebugFailureException>(() => FromMap(new Dictionary<string, object>
        {
            ["/string/"] = "hello, world",
        }, false));
        Assert.Contains("non-normalized path \"/string/\"", ex.Message);
    }

    [Fact]
    public void TestFromMap_NonNormalized2()
    {
        var ex = Assert.Throws<DebugFailureException>(() => FromMap(new Dictionary<string, object>
        {
            ["/string/../foo"] = "hello, world",
        }, false));
        Assert.Contains("non-normalized path \"/string/../foo\"", ex.Message);
    }

    [Fact]
    public void TestFromMap_InvalidFile()
    {
        // Go: "invalid file type int"；C# 用 .NET 类型名，故是 Int32。
        var ex = Assert.Throws<DebugFailureException>(() => FromMap(new Dictionary<string, object>
        {
            ["/string"] = 1234,
        }, false));
        Assert.Contains("invalid file type Int32", ex.Message);
    }

    // Go: TestVFSTestMapFS 里 FromMap 出来的 fs。
    private static FS MapFsForVfsTest()
    {
        return FromMap(new Dictionary<string, string>
        {
            ["/foo.ts"] = "hello, world",
            ["/dir1/file1.ts"] = "export const foo = 42;",
            ["/dir1/file2.ts"] = "export const foo = 42;",
            ["/dir2/file1.ts"] = "export const foo = 42;",
        }, false /*useCaseSensitiveFileNames*/);
    }

    [Fact]
    public void TestVFSTestMapFS_ReadFile()
    {
        var fs = MapFsForVfsTest();

        Assert.True(fs.TryReadFile("/foo.ts", out var content));
        Assert.Equal("hello, world", content);

        Assert.False(fs.TryReadFile("/does/not/exist.ts", out content));
        Assert.Equal("", content);
    }

    [Fact]
    public void TestVFSTestMapFS_Realpath()
    {
        var fs = MapFsForVfsTest();

        Assert.Equal("/foo.ts", fs.Realpath("/foo.ts"));
        Assert.Equal("/foo.ts", fs.Realpath("/Foo.ts"));
        // Go: fs.Realpath 的 C# 签名是 string（失败时返回原路径，见 iovfs 的 realpath ?? path）。
        Assert.Equal("/does/not/exist.ts", fs.Realpath("/does/not/exist.ts"));
    }

    [Fact]
    public void TestVFSTestMapFS_UseCaseSensitiveFileNames()
    {
        var fs = MapFsForVfsTest();

        Assert.False(fs.UseCaseSensitiveFileNames());
    }

    [Fact]
    public void TestVFSTestMapFSWindows_ReadFile()
    {
        var fs = WindowsMapFs();

        Assert.True(fs.TryReadFile("c:/foo.ts", out var content));
        Assert.Equal("hello, world", content);

        Assert.False(fs.TryReadFile("c:/does/not/exist.ts", out content));
        Assert.Equal("", content);
    }

    [Fact]
    public void TestVFSTestMapFSWindows_Realpath()
    {
        var fs = WindowsMapFs();

        Assert.Equal("c:/foo.ts", fs.Realpath("c:/foo.ts"));
        Assert.Equal("c:/foo.ts", fs.Realpath("c:/Foo.ts"));
        Assert.Equal("c:/does/not/exist.ts", fs.Realpath("c:/does/not/exist.ts"));
    }

    private static FS WindowsMapFs()
    {
        return FromMap(new Dictionary<string, string>
        {
            ["c:/foo.ts"] = "hello, world",
            ["c:/dir1/file1.ts"] = "export const foo = 42;",
            ["c:/dir1/file2.ts"] = "export const foo = 42;",
            ["c:/dir2/file1.ts"] = "export const foo = 42;",
        }, false);
    }

    // Go: TestBOM 的 BigEndian / LittleEndian 子测试：BOM + 每个 UTF-16 code unit 按 order 追加。
    private static void AssertUtf16BomRoundTrip(string expected, bool bigEndian)
    {
        var buf = new List<byte>(bigEndian ? [0xFE, 0xFF] : [0xFF, 0xFE]);
        foreach (var ch in expected)
        {
            var unit = (ushort)ch;
            if (bigEndian)
            {
                buf.Add((byte)(unit >> 8));
                buf.Add((byte)unit);
            }
            else
            {
                buf.Add((byte)unit);
                buf.Add((byte)(unit >> 8));
            }
        }

        // Go: FromMap(map[string][]byte{...}, true) → C# 的 map[string]any 重载（值可以是 byte[]）。
        var fs = FromMap(new Dictionary<string, object>
        {
            ["/foo.ts"] = buf.ToArray(),
        }, true);

        Assert.True(fs.TryReadFile("/foo.ts", out var content));
        Assert.Equal(expected, content);
    }

    [Fact]
    public void TestBOM_BigEndian()
    {
        AssertUtf16BomRoundTrip("hello, world", bigEndian: true);
    }

    [Fact]
    public void TestBOM_LittleEndian()
    {
        AssertUtf16BomRoundTrip("hello, world", bigEndian: false);
    }

    [Fact]
    public void TestBOM_UTF8()
    {
        const string expected = "hello, world";

        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(Encoding.UTF8.GetBytes(expected));

        var fs = FromMap(new Dictionary<string, object>
        {
            ["/foo.ts"] = bytes.ToArray(),
        }, true);

        Assert.True(fs.TryReadFile("/foo.ts", out var content));
        Assert.Equal(expected, content);
    }

    // Go: TestSymlink 等用例共用的那份 map。
    private static FS SymlinkMap()
    {
        return FromMap(new Dictionary<string, object>
        {
            ["/foo.ts"] = "hello, world",
            ["/symlink.ts"] = Symlink("/foo.ts"),
            ["/some/dir/file.ts"] = "hello, world",
            ["/some/dirlink"] = Symlink("/some/dir"),
            ["/a"] = Symlink("/b"),
            ["/b"] = Symlink("/c"),
            ["/c"] = Symlink("/d"),
            ["/d/existing.ts"] = "this is existing.ts",
        }, false);
    }

    [Fact]
    public void TestSymlink_ReadFile()
    {
        var fs = SymlinkMap();

        Assert.True(fs.TryReadFile("/symlink.ts", out var content));
        Assert.Equal("hello, world", content);

        Assert.True(fs.TryReadFile("/some/dirlink/file.ts", out content));
        Assert.Equal("hello, world", content);

        Assert.True(fs.TryReadFile("/a/existing.ts", out content));
        Assert.Equal("this is existing.ts", content);
    }

    [Fact]
    public void TestSymlink_Realpath()
    {
        var fs = SymlinkMap();

        Assert.Equal("/foo.ts", fs.Realpath("/symlink.ts"));
        Assert.Equal("/some/dir", fs.Realpath("/some/dirlink"));
        Assert.Equal("/some/dir/file.ts", fs.Realpath("/some/dirlink/file.ts"));
    }

    [Fact]
    public void TestSymlink_FileExists()
    {
        var fs = SymlinkMap();

        Assert.True(fs.FileExists("/symlink.ts"));
        Assert.True(fs.FileExists("/some/dirlink/file.ts"));
        Assert.True(fs.FileExists("/a/existing.ts"));
    }

    [Fact]
    public void TestSymlink_DirectoryExists()
    {
        var fs = SymlinkMap();

        Assert.True(fs.DirectoryExists("/some/dirlink"));
        Assert.True(fs.DirectoryExists("/d"));
        Assert.True(fs.DirectoryExists("/c"));
        Assert.True(fs.DirectoryExists("/b"));
        Assert.True(fs.DirectoryExists("/a"));
    }

    // Go: TestWritableFSSymlink / TestWritableFSSymlinkDelete 共用的那份 map。
    private static FS WritableSymlinkMap()
    {
        return FromMap(new Dictionary<string, object>
        {
            ["/some/dir/other.ts"] = "NOTHING",
            ["/other.ts"] = Symlink("/some/dir/other.ts"),
            ["/some/dirlink"] = Symlink("/some/dir"),
            ["/brokenlink"] = Symlink("/does/not/exist"),
            ["/a"] = Symlink("/b"),
            ["/b"] = Symlink("/c"),
            ["/c"] = Symlink("/d"),
            ["/d/existing.ts"] = "hello, world",
        }, false);
    }

    [Fact]
    public void TestWritableFSSymlink()
    {
        var fs = WritableSymlinkMap();

        fs.WriteFile("/some/dirlink/file.ts", "hello, world");

        Assert.True(fs.TryReadFile("/some/dirlink/file.ts", out var content));
        Assert.Equal("hello, world", content);

        Assert.True(fs.TryReadFile("/some/dir/file.ts", out content));
        Assert.Equal("hello, world", content);

        fs.WriteFile("/some/dirlink/file.ts", "goodbye, world");

        Assert.True(fs.TryReadFile("/some/dirlink/file.ts", out content));
        Assert.Equal("goodbye, world", content);

        fs.WriteFile("/other.ts", "hello, world");

        Assert.True(fs.TryReadFile("/other.ts", out content));
        Assert.Equal("hello, world", content);

        Assert.True(fs.TryReadFile("/some/dir/other.ts", out content));
        Assert.Equal("hello, world", content);

        // Go: assert.Error(t, err, `write "some/dirlink": path exists but is not a regular file`)
        //     —— mkdirAll("/some") 不会失败，所以最终仍是 WriteFile 那层的 IOException（消息与原文件一致）。
        var ex = Assert.Throws<IOException>(() => fs.WriteFile("/some/dirlink", "hello, world"));
        Assert.Equal("write \"some/dirlink\": path exists but is not a regular file", ex.Message);

        // 不能在断掉的目录符号链接里写入：最终冒出来的是 mkdirAll 那层的 brokenSymlinkError
        // （iovfs 先写失败 → mkdirAll 也失败 → 未包装的消息）。
        ex = Assert.Throws<BrokenSymlinkException>(() => fs.WriteFile("/brokenlink/file.ts", "hello, world"));
        Assert.Equal("broken symlink \"brokenlink\" -> \"does/not/exist\"", ex.Message);

        ex = Assert.Throws<BrokenSymlinkException>(() => fs.WriteFile("/brokenlink/also/wrong/file.ts", "hello, world"));
        Assert.Equal("broken symlink \"brokenlink\" -> \"does/not/exist\"", ex.Message);

        // 但断掉的**文件**符号链接可以写（写到解析出来的 realpath 上）。
        fs.WriteFile("/brokenlink", "hello, world");
        Assert.True(fs.TryReadFile("/brokenlink", out content));
        Assert.Equal("hello, world", content);
        Assert.True(fs.TryReadFile("/does/not/exist", out content));
        Assert.Equal("hello, world", content);
    }

    [Fact]
    public void TestWritableFSSymlinkChain()
    {
        var fs = FromMap(new Dictionary<string, object>
        {
            ["/a"] = Symlink("/b"),
            ["/b"] = Symlink("/c"),
            ["/c"] = Symlink("/d"),
            ["/d/existing.ts"] = "hello, world",
        }, false);

        fs.WriteFile("/a/foo/bar/new.ts", "this is new.ts");
        Assert.True(fs.TryReadFile("/a/foo/bar/new.ts", out var content));
        Assert.Equal("this is new.ts", content);
        Assert.True(fs.TryReadFile("/b/foo/bar/new.ts", out content));
        Assert.Equal("this is new.ts", content);
        Assert.True(fs.TryReadFile("/d/foo/bar/new.ts", out content));
        Assert.Equal("this is new.ts", content);
    }

    [Fact]
    public void TestWritableFSSymlinkChainNotDir()
    {
        var fs = FromMap(new Dictionary<string, object>
        {
            ["/a"] = Symlink("/b"),
            ["/b"] = Symlink("/c"),
            ["/c"] = Symlink("/d"),
            ["/d"] = "hello, world",
        }, false);

        // Go: assert.Error(t, err, `mkdir "d": path exists but is not a directory`)
        //     —— iovfs 的 mkdirAll 解析符号链接链后落在 "d" 上，且该异常不被 writeFileEnsuringDir 捕获。
        var ex = Assert.Throws<IOException>(() => fs.WriteFile("/a/foo/bar/new.ts", "this is new.ts"));
        Assert.Equal("mkdir \"d\": path exists but is not a directory", ex.Message);
    }

    [Fact]
    public void TestWritableFSSymlinkDelete()
    {
        var fs = WritableSymlinkMap();

        fs.Remove("/a");
        Assert.False(fs.DirectoryExists("/a"));
        Assert.True(fs.DirectoryExists("/b"));
        Assert.True(fs.DirectoryExists("/c"));
        Assert.True(fs.FileExists("/d/existing.ts"));

        // 底层文件/目录被删掉后，符号链接本身还在。
        fs.Remove("/d");
        Assert.False(fs.DirectoryExists("/b"));
        Assert.False(fs.DirectoryExists("/c"));
        Assert.False(fs.DirectoryExists("/d"));
        Assert.False(fs.FileExists("/d/again.ts"));
        fs.WriteFile("/d/again.ts", "d exists again");
        Assert.True(fs.DirectoryExists("/b"));
        Assert.True(fs.DirectoryExists("/c"));
        Assert.True(fs.TryReadFile("/b/again.ts", out var content));
        Assert.Equal("d exists again", content);

        Assert.False(fs.FileExists("/brokenlink"));
        Assert.False(fs.DirectoryExists("/brokenlink"));
        fs.Remove("/does/not/exist"); // 什么都不做
        Assert.False(fs.FileExists("/brokenlink"));
        Assert.False(fs.DirectoryExists("/brokenlink"));
        fs.WriteFile("/does/not/exist", "hello, world");
        Assert.True(fs.FileExists("/brokenlink"));
    }
}

// ported from: internal/testutil/filefixture/filefixture.go
//
// 与 Go 的差异（逐条）：
// 1) `testing.TB` → `IHarnessTest`（见 TestUtil/HarnessContext.cs）。`tb.Helper()` 在 C# 没有对应物
//    （Go 只用来把失败行号指向调用者），这里直接省掉 —— 不影响任何行为。
// 2) `tb.Skipf("Test fixture %q does not exist", path)` → `Skip($"Test fixture \"{path}\" does not exist")`
//    （`%q` 就是双引号加转义；路径里不会有需要转义的字符）。
// 3) `sync.OnceValues(func() (string, error))` → `Lazy<(string Contents, Exception? Error)>`（只求值一次，缓存成功与失败）。
// 4) `os.ReadFile` + `string(b)` → `File.ReadAllText(path, UTF8 无 BOM)`：Go 的 `string([]byte)` 是"字节原样进字符串"
//    （BOM 会保留），所以这里用**不剥 BOM** 的 UTF-8 解码器，行为与 Go 一致。

namespace TypeScript.TestUtil.FileFixture;

using System.Text;

/// <summary>Go: <c>type Fixture interface</c>。</summary>
public interface Fixture
{
    /// <summary>Go: <c>Name() string</c>。</summary>
    string Name();

    /// <summary>Go: <c>Path() string</c>。</summary>
    string Path();

    /// <summary>Go: <c>SkipIfNotExist(t testing.TB)</c>。</summary>
    void SkipIfNotExist(IHarnessTest tb);

    /// <summary>Go: <c>ReadFile(t testing.TB) string</c>。</summary>
    string ReadFile(IHarnessTest tb);
}

/// <summary>Go: <c>package filefixture</c>。</summary>
public static class FileFixture
{
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Go: <c>func FromFile(name string, path string) Fixture</c>。</summary>
    public static Fixture FromFile(string name, string path)
    {
        // Cache the file contents and errors.
        var contents = new Lazy<(string Contents, Exception? Error)>(() =>
        {
            try
            {
                return (File.ReadAllText(path, _utf8NoBom), null);
            }
            catch (Exception ex)
            {
                return ("", ex);
            }
        });
        return new FromFileFixture(name, path, contents);
    }

    /// <summary>Go: <c>func FromString(name string, path string, contents string) Fixture</c>。</summary>
    public static Fixture FromString(string name, string path, string contents)
    {
        return new FromStringFixture(name, path, contents);
    }

    /// <summary>Go: <c>type fromFile struct</c>（未导出 → C# internal 嵌套类）。</summary>
    private sealed class FromFileFixture(string name, string path, Lazy<(string Contents, Exception? Error)> contents) : Fixture
    {
        public string Name() => name;

        public string Path() => path;

        public void SkipIfNotExist(IHarnessTest tb)
        {
            if (!File.Exists(path))
            {
                tb.Skip($"Test fixture \"{path}\" does not exist");
            }
        }

        public string ReadFile(IHarnessTest tb)
        {
            var (fileContents, error) = contents.Value;
            if (error is not null)
            {
                tb.Fatal($"Failed to read test fixture \"{path}\": {error.Message}");
            }

            return fileContents;
        }
    }

    /// <summary>Go: <c>type fromString struct</c>。</summary>
    private sealed class FromStringFixture(string name, string path, string contents) : Fixture
    {
        public string Name() => name;

        public string Path() => path;

        public void SkipIfNotExist(IHarnessTest tb)
        {
        }

        public string ReadFile(IHarnessTest tb) => contents;
    }
}

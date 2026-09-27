// ported from: internal/testutil/baseline/baseline.go
//
// 与 Go 的差异（逐条，都不改变字节语义）：
// 1) Go 的 `*testing.T` → `IHarnessTest`（M7Runner 提供，命名空间 `TypeScript.TestUtil`）；`t.Error` → `Error`，
//    `t.Fatalf` → `Fatal`。`Run`/`WriteComparison` 只把它当"失败上报"用，不参与路径计算。
// 2) **逐字节比较**：Go 里 `os.ReadFile` + 字符串 `==` 比的是**原始字节**（Go 的 string 就是字节序列），
//    所以这里也全程走字节：参考文件 `File.ReadAllBytes`，生成内容用**无 BOM 的 UTF-8** 编码成字节，
//    避免 `File.ReadAllText` 的编码嗅探/BOM 剥离造成假相等（docs/harness.md §4：baseline 不写 BOM）。
//    写文件同理用 `File.WriteAllBytes`（不做任何换行/编码转换）。
// 3) Go 的 `os.MkdirAll` → `Directory.CreateDirectory`；`os.Remove`+`os.Stat` → `File.Exists`/`File.Delete`。
// 4) `localRoot`/`referenceRoot` 在 Go 里是包级变量（import 时就算好）；C# 用 `Lazy<string>` 推迟到首次使用，
//    但调用点不可见该差异（`Repo.TestDataPath()` 仍只算一次）。
// 5) panic → `Debug.Fail`（`TypeScript.Debug`，见 docs/architecture.md §2）。

namespace TypeScript.TestUtil.Baseline;

using System.Text;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

/// <summary>Go: <c>type Options struct</c>（baseline 的写入/比较选项）。</summary>
public sealed class BaselineOptions
{
    public string Subfolder = "";

    /// <summary>Go: <c>DiffFixupOld func(string) string</c>（baseline 包内未读取，保留以对齐调用点）。</summary>
    public Func<string, string>? DiffFixupOld;

    /// <summary>Go: <c>DiffFixupNew func(string) string</c>（同上）。</summary>
    public Func<string, string>? DiffFixupNew;

    /// <summary>Go: <c>SkipDiffWithOld bool</c>（同上）。</summary>
    public bool SkipDiffWithOld;
}

/// <summary>Go: <c>package baseline</c>（baseline.go + testmain.go 都在这个包里）。</summary>
public static partial class Baseline
{
    /// <summary>Go: <c>const NoContent = "&lt;no content&gt;"</c>。</summary>
    public const string NoContent = "<no content>";

    private static readonly Lazy<string> _localRoot = new(() => CombinePaths(Repo.Repo.TestDataPath(), "baselines", "local"));
    private static readonly Lazy<string> _referenceRoot = new(() => CombinePaths(Repo.Repo.TestDataPath(), "baselines", "reference"));

    /// <summary>Go: <c>var localRoot = filepath.Join(repo.TestDataPath(), "baselines", "local")</c>。</summary>
    private static string LocalRoot => _localRoot.Value;

    /// <summary>Go: <c>var referenceRoot = filepath.Join(repo.TestDataPath(), "baselines", "reference")</c>。</summary>
    private static string ReferenceRoot => _referenceRoot.Value;

    /// <summary>Go: <c>func Run(t *testing.T, fileName string, actual string, opts Options)</c>。</summary>
    public static void Run(IHarnessTest t, string fileName, string actual, BaselineOptions opts)
    {
        var subfolder = opts.Subfolder;
        var localPath = CombinePaths(LocalRoot, subfolder, fileName);
        var referencePath = CombinePaths(ReferenceRoot, subfolder, fileName);
        RecordBaseline(t, CombinePaths(subfolder, fileName));
        WriteComparison(t, actual, localPath, referencePath);
    }

    /// <summary>Go: <c>func DiffText(oldName string, newName string, expected string, actual string) string</c>。</summary>
    public static string DiffText(string oldName, string newName, string expected, string actual)
    {
        var lines = Patience.Patience.Diff(SplitLines(expected), SplitLines(actual));
        return Patience.Patience.UnifiedDiffTextWithOptions(
            lines ?? [],
            new Patience.UnifiedDiffOptions
            {
                Precontext = 3,
                Postcontext = 3,
                SrcHeader = oldName,
                DstHeader = newName,
            });
    }

    /// <summary>Go: <c>func writeComparison(t *testing.T, actualContent string, local, reference string)</c>。</summary>
    private static void WriteComparison(IHarnessTest t, string actualContent, string local, string reference)
    {
        if (actualContent.Length == 0)
        {
            // Go: panic("the generated content was \"\". Return 'baseline.NoContent' if no baselining is required.")
            Debug.Fail("the generated content was \"\". Return 'baseline.NoContent' if no baselining is required.");
        }

        var localDirectory = GetDirectoryPath(local);
        if (localDirectory.Length != 0)
        {
            // Go: os.MkdirAll(filepath.Dir(local), 0o755)
            Directory.CreateDirectory(localDirectory);
        }

        if (File.Exists(local))
        {
            File.Delete(local);
        }

        var actualBytes = _utf8NoBom.GetBytes(actualContent);

        byte[]? expectedBytes = null;
        if (File.Exists(reference))
        {
            // Go: content, err := os.ReadFile(reference)；err == nil 才算 foundExpected。
            expectedBytes = File.ReadAllBytes(reference);
        }

        var foundExpected = expectedBytes is not null;

        // Go:
        //   if expected == actualContent && !(actualContent == NoContent && foundExpected) { return }
        //   （reference 不存在时 expected 是 NoContent；此时 actual 也是 NoContent 就静默通过 —— docs/harness.md §4）
        if (foundExpected && actualContent != NoContent && expectedBytes!.AsSpan().SequenceEqual(actualBytes))
        {
            return;
        }

        if (!foundExpected && actualContent == NoContent)
        {
            return;
        }

        if (actualContent == NoContent)
        {
            // Go: os.WriteFile(local+".delete", []byte{}, 0o644)
            File.WriteAllBytes(local + ".delete", []);
            return;
        }

        File.WriteAllBytes(local, actualBytes);

        if (!foundExpected)
        {
            t.Error($"new baseline created at {local}.");
            return;
        }

        t.Error($"the baseline file {reference} has changed. (Run `hereby baseline-accept` if the new baseline is correct.)");
    }

    /// <summary>Go: 0o644 的写入语义 —— .NET 的 UTF8Encoding(false) 就是不写 BOM 的 UTF-8（文档 §4 要求无 BOM）。</summary>
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
}

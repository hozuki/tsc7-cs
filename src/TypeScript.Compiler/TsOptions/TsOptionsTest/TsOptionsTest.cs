// ported from: internal/tsoptions/tsoptionstest/parsedcommandline.go
//               internal/tsoptions/tsoptionstest/vfsparseconfighost.go（两个 New* 工厂函数）
//
// 说明（Go → C# 的改写）：
// 1) Go 的 `GetParsedCommandLine(t assert.TestingT, …)` 首参是测试断言器（只用于 Go 测试里的 assert）；
//    C# 无对应物 → 去掉该参数，其余参数/顺序/语义一致。
// 2) Go 的这两个文件都是包级函数（无接收者）→ 按 docs/architecture.md §2 放进与包同名的静态类
//    `TsOptionsTest`（先例：`TypeScript.Vfs.VfsTest.VfsTest`）。
namespace TypeScript.TsOptions.TsOptionsTest;

using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.VfsTest.VfsTest;

/// <summary>Go 的 <c>tsoptionstest</c> 包级函数（vfsparseconfighost.go / parsedcommandline.go）。</summary>
public static partial class TsOptionsTest
{
    /// <summary>
    /// Go: <c>func NewVFSParseConfigHost(files map[string]string, currentDirectory string, useCaseSensitiveFileNames bool) *VfsParseConfigHost</c>
    /// （vfsparseconfighost.go:35-42）。
    /// </summary>
    public static VfsParseConfigHost NewVFSParseConfigHost(
        Dictionary<string, string> files,
        string currentDirectory,
        bool useCaseSensitiveFileNames)
    {
        return new VfsParseConfigHost(FromMap(files, useCaseSensitiveFileNames), currentDirectory);
    }

    /// <summary>
    /// Go: <c>func NewVFSParseConfigHostWithSymlinks(files, symlinks map[string]string, currentDirectory string, useCaseSensitiveFileNames bool) *VfsParseConfigHost</c>
    /// （vfsparseconfighost.go:45-59）。symlinks 是 link 路径 → 目标路径，两者都按 currentDirectory 规范化。
    /// </summary>
    public static VfsParseConfigHost NewVFSParseConfigHostWithSymlinks(
        Dictionary<string, string> files,
        Dictionary<string, string> symlinks,
        string currentDirectory,
        bool useCaseSensitiveFileNames)
    {
        if (symlinks.Count == 0)
        {
            return NewVFSParseConfigHost(files, currentDirectory, useCaseSensitiveFileNames);
        }

        // Go: entries := make(map[string]any, len(files)+len(symlinks))（值可以是 string 或 *fstest.MapFile）
        var entries = new Dictionary<string, object>(files.Count + symlinks.Count);
        foreach (var (name, content) in files)
        {
            entries[name] = content;
        }

        foreach (var (link, target) in symlinks)
        {
            entries[GetNormalizedAbsolutePath(link, currentDirectory)] = Symlink(GetNormalizedAbsolutePath(target, currentDirectory));
        }

        return new VfsParseConfigHost(FromMap(entries, useCaseSensitiveFileNames), currentDirectory);
    }

    /// <summary>
    /// Go: <c>func GetParsedCommandLine(t assert.TestingT, jsonText string, files map[string]string, currentDirectory string, useCaseSensitiveFileNames bool) *tsoptions.ParsedCommandLine</c>
    /// （parsedcommandline.go:10-15）。
    /// </summary>
    public static ParsedCommandLine GetParsedCommandLine(
        string jsonText,
        Dictionary<string, string> files,
        string currentDirectory,
        bool useCaseSensitiveFileNames)
    {
        var host = NewVFSParseConfigHost(files, currentDirectory, useCaseSensitiveFileNames);
        var configFileName = CombinePaths(currentDirectory, "tsconfig.json");
        var tsconfigSourceFile = TsOptions.NewTsconfigSourceFileFromFilePath(
            configFileName,
            ToPath(configFileName, currentDirectory, useCaseSensitiveFileNames),
            jsonText);
        return TsOptions.ParseJsonSourceFileConfigFileContent(
            tsconfigSourceFile,
            host,
            currentDirectory,
            null,
            null,
            configFileName,
            null,
            null);
    }
}

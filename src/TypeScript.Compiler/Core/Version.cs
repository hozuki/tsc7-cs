// ported from: internal/core/version.go
//
// 与 Go 的差异：
// - Go 的 `var version = "7.1.0-dev"` 目的是让 `-ldflags -X` 能在构建时覆盖；C# 没有对应机制，
//   这里保留普通静态字段（要覆盖只能在构建时替换这个字面量），字段仍非 readonly 以保持"var"的语义。
// - Go 的 `panic("invalid version string: " + version)` → Debug.Fail（docs §3.2）。
// - Go 在包初始化时求值 versionMajorMinor；C# 的静态字段初始化同样按声明顺序执行（跨 partial 文件的
//   顺序未定义），所以 version 必须声明在 versionMajorMinor 之前。
namespace TypeScript.Core;

public static partial class Core
{
    private static string _version = "7.1.0-dev";

    public static string Version()
    {
        return _version;
    }

    private static readonly string _versionMajorMinor = ComputeVersionMajorMinor();

    private static string ComputeVersionMajorMinor()
    {
        // Go 的 strings.IndexFunc：返回**第二个** '.' 的下标（只见过一个 '.' 时返回 -1）。
        var seenMajor = false;
        var i = -1;
        for (var j = 0; j < _version.Length; j++)
        {
            if (_version[j] != '.')
            {
                continue;
            }

            if (seenMajor)
            {
                i = j;
                break;
            }

            seenMajor = true;
        }

        if (i == -1)
        {
            Debug.Fail("invalid version string: " + _version);
        }

        return _version[..i];
    }

    public static string VersionMajorMinor()
    {
        return _versionMajorMinor;
    }
}

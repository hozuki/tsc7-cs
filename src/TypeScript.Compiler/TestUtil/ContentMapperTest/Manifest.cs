// ported from: internal/testutil/contentmappertest/manifest.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `fmt.Sprintf` → C# 内插字符串（`{`/`}` 需写成 `{{`/`}}`）；`%q` → `"\"…\""`（见
//    ContentMapper/HostImpl.cs 文件头第 9 条）。制表符/换行与 Go 的格式串逐字一致。
// 2) `PackageName` 是 Go 的导出常量 → `ContentMapperTest` 的 `public const string`；`PackageJSON` 是包级函数 →
//    同一个 partial class 的 `public static` 成员。
using TypeScript.ContentMapper;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go 的 <c>contentmappertest</c> 包级函数/常量（manifest.go 部分）。</summary>
public static partial class ContentMapperTest
{
    /// <summary>Go: <c>const PackageName = "mapper"</c>。</summary>
    public const string PackageName = "mapper";

    /// <summary>Go: <c>func PackageJSON(mapper string) string</c>。返回选择指定映射器的 package manifest。</summary>
    public static string PackageJSON(string mapper)
    {
        var compilerOptions = "";
        var dynamicConfig = "";
        if (mapper == TransformingMapper)
        {
            compilerOptions = ", \"compilerOptions\": [\"target\", \"jsx\"]";
        }

        if (mapper == DynamicVerbatimMapper)
        {
            dynamicConfig = ", \"dynamicConfig\": true";
        }

        return $"{{\n\t\"name\": \"{PackageName}\",\n\t\"version\": \"1.0.0\",\n\t\"typescript\": {{ \"contentMapper\": {{ \"exec\": [\"{mapper}\"]{compilerOptions}{dynamicConfig} }} }}\n}}";
    }
}

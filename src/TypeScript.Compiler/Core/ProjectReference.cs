// ported from: internal/core/projectreference.go
//
// 偏离 Go 的地方：
// 1) Go 里 ProjectReference 以 `*core.ProjectReference` 使用（tsoptions 建 `[]*core.ProjectReference`）→ C# class。
// 2) Go 的参数名 `ref` 是 C# 关键字，改名为 `reference`；`ResolveProjectReferencePath` 的读取路径不变。
// 3) JSON tag 见 CompilerOptions.cs 文件头（Path → "path"、OriginalPath → "originalPath,omitempty"、
//    Circular → "circular,omitempty"）：字段级映射由 `artifacts/api-codegen/gen-core-json.mjs` 生成到
//    `Core/BuildOptionsJson.Generated.cs`（本类据此声明为 partial 并实现 IJsonMarshaler/IJsonUnmarshaler）。
using TypeScript.Json;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Core;

public sealed partial class ProjectReference : IJsonMarshaler, IJsonUnmarshaler
{
    // Path is a normalized path on disk.
    public string Path = "";
    // OriginalPath is the path as it was originally written.
    public string OriginalPath = "";
    // Circular indicates that this reference is intended to form a circularity.
    public bool Circular;
}

public static partial class Core
{
    public static string ResolveProjectReferencePath(ProjectReference reference)
    {
        return ResolveConfigFileNameOfProjectReference(reference.Path);
    }

    public static string ResolveConfigFileNameOfProjectReference(string path)
    {
        if (FileExtensionIs(path, ExtensionJson))
        {
            return path;
        }
        return CombinePaths(path, "tsconfig.json");
    }
}

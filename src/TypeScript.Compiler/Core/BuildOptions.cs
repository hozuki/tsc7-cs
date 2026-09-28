// ported from: internal/core/buildoptions.go
//
// 偏离 Go 的地方：
// 1) `_ noCopy` 字段（go vet 的 -copylocks 检查用）跳过：C# 引用类型无对应物。
// 2) Go 里 BuildOptions 一律以 `*BuildOptions` 使用（api/proto.go、execute/build 都有 nil 判断）→ C# class。
// 3) JSON tag 见 CompilerOptions.cs 文件头（Dry → "dry,omitzero"、Builders → "builders,omitzero"…），
//    字段级映射由 `artifacts/api-codegen/gen-core-json.mjs` 生成到 `Core/BuildOptionsJson.Generated.cs`
//    （本类据此声明为 partial 并实现 IJsonMarshaler/IJsonUnmarshaler，与 Go 的 struct tag 反射映射等价）。
using TypeScript.Json;

namespace TypeScript.Core;

public sealed partial class BuildOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public Tristate Dry;
    public Tristate Force;
    public Tristate Verbose;
    public int? Builders;
    public Tristate StopBuildOnErrors;

    // CompilerOptions are not parsed here and will be available on ParsedBuildCommandLine

    // Internal fields
    public Tristate Clean;
}

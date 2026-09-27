// ported from: internal/core/buildoptions.go
//
// 偏离 Go 的地方：
// 1) `_ noCopy` 字段（go vet 的 -copylocks 检查用）跳过：C# 引用类型无对应物。
// 2) Go 里 BuildOptions 一律以 `*BuildOptions` 使用（api/proto.go、execute/build 都有 nil 判断）→ C# class。
// 3) JSON tag 见 CompilerOptions.cs 文件头的说明（Dry → "dry,omitzero"、Builders → "builders,omitzero"…），
//    本轮不实现映射，字段不挂 attribute。
namespace TypeScript.Core;

public sealed class BuildOptions
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

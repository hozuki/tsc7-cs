// ported from: internal/core/typeacquisition.go
//
// 偏离 Go 的地方：
// 1) Go 的 `slices.Equal`（nil slice 与空 slice 相等）→ 私有的 SlicesEqual 辅助（null 视作空数组）。
// 2) Go 的 `Equals(other *TypeAcquisition)` 里有 nil 接收者分支：`ta == other`（两者同为 nil → true）、
//    `ta == nil || other == nil` → false。C# 实例方法不能在 null 上分派，`ta == nil` 的那半不可达，
//    只保留 `ReferenceEquals(this, other)` 与 `other is null`；调用点若 ta 可能为 nil，写
//    `ta?.Equals(other) ?? (other is null)`。
// 3) Go 里 TypeAcquisition 以 `*core.TypeAcquisition` 使用（api/proto.go 有 nil 判断）→ C# class。
// 4) JSON tag 见 CompilerOptions.cs 文件头（Enable → "enable,omitzero"、Include → "include,omitzero"…）；
//    字段级映射由 `artifacts/api-codegen/gen-core-json.mjs` 生成到 `Core/BuildOptionsJson.Generated.cs`
//    （本类据此声明为 partial 并实现 IJsonMarshaler/IJsonUnmarshaler）。
using TypeScript.Json;

namespace TypeScript.Core;

public sealed partial class TypeAcquisition : IJsonMarshaler, IJsonUnmarshaler
{
    public Tristate Enable;
    public string[]? Include;
    public string[]? Exclude;
    public Tristate DisableFilenameBasedTypeAcquisition;

    public bool Equals(TypeAcquisition? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        if (other is null)
        {
            return false;
        }

        return Enable == other.Enable &&
            SlicesEqual(Include, other.Include) &&
            SlicesEqual(Exclude, other.Exclude) &&
            DisableFilenameBasedTypeAcquisition == other.DisableFilenameBasedTypeAcquisition;
    }

    // Go: slices.Equal(m.Include, other.Include)（nil 与空 slice 相等）
    private static bool SlicesEqual(string[]? a, string[]? b)
    {
        a ??= [];
        b ??= [];
        return a.AsSpan().SequenceEqual(b);
    }
}

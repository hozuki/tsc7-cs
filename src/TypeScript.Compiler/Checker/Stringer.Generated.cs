// ported from: internal/checker/stringer_generated.go
//
// Go 侧由 `stringer -type=SignatureKind -output=stringer_generated.go` 生成；C# 的 enum 不能覆写 ToString，
// 所以按 docs/architecture.md §3.3 落成扩展方法 `String(this SignatureKind)`（先例：
// Core/TristateStringer.Generated.cs、Ast/KindStringer.Generated.cs），调用点保留 Go 的 `x.String()` 写法。
//
// 输出与 Go 逐字一致：_SignatureKind_name = "SignatureKindCallSignatureKindConstruct"、
// _SignatureKind_index = {0, 17, 39} → 合法值返回带 `SignatureKind` 前缀的常量名；
// 越界/负值返回 "SignatureKind(" + strconv.FormatInt(int64(i), 10) + ")"。
//
// 与 Go 的差异（C# 语言特性）：C# 的扩展方法必须定义在**非泛型、非嵌套的 static 类**里，而本包的
// `public sealed partial class Checker` 是实例类（CS1106），所以单开一个同命名空间的 `CheckerExtensions`
// 承载扩展方法；调用点只要有 `using TypeScript.Checker;` 就与 Go 一样写 `kind.String()`。
namespace TypeScript.Checker;

/// <summary>Go checker 包里 stringer 生成的扩展方法的宿主类型（见文件头）。</summary>
public static class CheckerExtensions
{
    /// <summary>Go: <c>func (i SignatureKind) String() string</c>（stringer 生成）。</summary>
    public static string String(this SignatureKind i)
    {
        switch (i)
        {
            case SignatureKind.Call:
                return "SignatureKindCall";
            case SignatureKind.Construct:
                return "SignatureKindConstruct";
            default:
                return $"SignatureKind({(int)i})";
        }
    }
}

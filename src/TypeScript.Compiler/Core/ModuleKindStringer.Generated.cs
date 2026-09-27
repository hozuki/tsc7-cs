// ported from: internal/core/modulekind_stringer_generated.go
//
// Go 侧由 `stringer -type=ModuleKind -trimprefix=ModuleKind` 生成；C# 的 enum 不能覆写 ToString，
// 所以 1:1 落成 `Core` 静态类里的扩展方法 `String(this ModuleKind)`，调用点保留 Go 的 `x.String()` 写法。
//
// 输出与 Go 逐字一致（名字已去掉 ModuleKind 前缀）：
//   0..7    → "None" "CommonJS" "AMD" "UMD" "System" "ES2015" "ES2020" "ES2022"
//   99..102 → "ESNext" "Node16" "Node18" "Node20"
//   199..200→ "NodeNext" "Preserve"
//   其余    → "ModuleKind(" + strconv.FormatInt(int64(i), 10) + ")"
// Go 的 stringer 按区间匹配，而这些区间里的整数恰好就是上面这些常量，所以逐个常量 switch 的语义完全相同。
namespace TypeScript.Core;

public static partial class Core
{
    public static string String(this ModuleKind i)
    {
        switch (i)
        {
            case ModuleKind.None:
                return "None";
            case ModuleKind.CommonJS:
                return "CommonJS";
            case ModuleKind.AMD:
                return "AMD";
            case ModuleKind.UMD:
                return "UMD";
            case ModuleKind.System:
                return "System";
            case ModuleKind.ES2015:
                return "ES2015";
            case ModuleKind.ES2020:
                return "ES2020";
            case ModuleKind.ES2022:
                return "ES2022";
            case ModuleKind.ESNext:
                return "ESNext";
            case ModuleKind.Node16:
                return "Node16";
            case ModuleKind.Node18:
                return "Node18";
            case ModuleKind.Node20:
                return "Node20";
            case ModuleKind.NodeNext:
                return "NodeNext";
            case ModuleKind.Preserve:
                return "Preserve";
            default:
                return $"ModuleKind({(int)i})";
        }
    }
}

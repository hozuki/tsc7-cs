// ported from: internal/core/tristate.go
//
// 偏离 Go 的地方（C# 语言特性，语义等价；Main 已批准的跨 agent 约定）：
//
// 1) Go 的 `type Tristate byte` + 包级常量 → C# `enum Tristate : byte`（底层类型一致）。
//    成员名 = Go 常量名去掉类型前缀。
//
// 2) Go 的方法（IsTrue/IsTrueOrUnknown/IsFalse/IsFalseOrUnknown/IsUnknown/DefaultIfUnknown）在 C# 里
//    是 `Core` 静态类的**扩展方法**：C# 的 enum 不能声明实例方法，扩展方法让调用点保持 Go 的
//    `t.IsTrue()` 写法（同一命名空间/`using TypeScript.Core;` 下可见）。
//
// 3) Go 的 `String()`（tristate_stringer_generated.go 生成）→ Core 的扩展方法 `String(this Tristate)`
//    （见 TristateStringer.Generated.cs）。enum 成员名让合法值上的 `ToString()` 也与 Go 一致。
//
// 4) Go 的 `UnmarshalJSON`/`MarshalJSON`（JSON 布尔 true/false，缺省/null → TSUnknown）在 C# 里没有
//    接口可挂（enum 不能实现 IJsonMarshaler），落成同名的 `Core` 扩展方法：`UnmarshalJSON(this ref
//    Tristate, byte[])` / `MarshalJSON(Tristate) → byte[]`。将来给 CompilerOptions 写显式 JSON mapper
//    时必须调用这两个方法，否则 Json 包会把 enum 按底层整数写（Go 的 Tristate 不会）。
//
// 5) Go 的 `UnmarshalJSON` 返回 error（实际恒为 nil）→ C# void；出错走异常（docs §3.2）。
using System.Text;

namespace TypeScript.Core;

/// <summary>Go 的 <c>Tristate</c>：三值逻辑（Unknown/False/True），底层 byte。</summary>
public enum Tristate : byte
{
    TSUnknown = 0,
    TSFalse = 1,
    TSTrue = 2,
}

public static partial class Core
{
    public static bool IsTrue(this Tristate t)
    {
        return t == Tristate.TSTrue;
    }

    public static bool IsTrueOrUnknown(this Tristate t)
    {
        return t == Tristate.TSTrue || t == Tristate.TSUnknown;
    }

    public static bool IsFalse(this Tristate t)
    {
        return t == Tristate.TSFalse;
    }

    public static bool IsFalseOrUnknown(this Tristate t)
    {
        return t == Tristate.TSFalse || t == Tristate.TSUnknown;
    }

    public static bool IsUnknown(this Tristate t)
    {
        return t == Tristate.TSUnknown;
    }

    public static Tristate DefaultIfUnknown(this Tristate t, Tristate value)
    {
        if (t == Tristate.TSUnknown)
        {
            return value;
        }
        return t;
    }

    public static void UnmarshalJSON(this ref Tristate t, byte[] data)
    {
        switch (Encoding.UTF8.GetString(data))
        {
            case "true":
                t = Tristate.TSTrue;
                break;
            case "false":
                t = Tristate.TSFalse;
                break;
            default:
                t = Tristate.TSUnknown;
                break;
        }
    }

    public static byte[] MarshalJSON(this Tristate t)
    {
        switch (t)
        {
            case Tristate.TSTrue:
                return Encoding.UTF8.GetBytes("true");
            case Tristate.TSFalse:
                return Encoding.UTF8.GetBytes("false");
            default:
                return Encoding.UTF8.GetBytes("null");
        }
    }

    public static Tristate BoolToTristate(bool b)
    {
        if (b)
        {
            return Tristate.TSTrue;
        }
        return Tristate.TSFalse;
    }
}

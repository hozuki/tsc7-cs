// C# 侧新增：Number 的泛型数学接口实现（Go 无对应物）
//
// 只实现 System.Numerics 的**运算符接口族**，不实现 INumberBase/INumber：INumberBase 的契约成员
// （Parse + NumberStyles、checked/saturating/truncating 转换、TryFormat 等）与 JS 的
// `Number(string)` / ToInt32 语义相冲突，实现它们会把两种语义混在同一个 API 上；而运算符接口族
// 正好提供"可读的数值运算 + 可被泛型算法消费"，且与 JS/IEEE 语义一致。
//
// 这里的每个运算符都只是 IEEE 754 对 double Value 的直接作用（与 Go 的 `type Number float64`
// 上的运算符完全一致），因此**不会改变任何既有 JS 语义方法的行为**。
using System.Numerics;

namespace TypeScript.JsNum;

public readonly partial struct Number :
    // 算术运算符（对应 Go 里 Number 可用的 `+ - * /`）。
    IAdditionOperators<Number, Number, Number>,
    ISubtractionOperators<Number, Number, Number>,
    IMultiplyOperators<Number, Number, Number>,
    IDivisionOperators<Number, Number, Number>,
    IModulusOperators<Number, Number, Number>,
    IUnaryPlusOperators<Number, Number>,
    IUnaryNegationOperators<Number, Number>,
    IIncrementOperators<Number>,
    IDecrementOperators<Number>,
    // 比较运算符（`== != < <= > >=` 已存在；IComparisonOperators 蕴含 IEqualityOperators）。
    IComparisonOperators<Number, Number, bool>,
    // 单位元：0 与 1（Go 里靠无类型常量 `0`/`1` 表达，C# 的泛型算法需要这个成员）。
    IAdditiveIdentity<Number, Number>,
    IMultiplicativeIdentity<Number, Number>
{
    // Go 的 `Number` 隐式转换：Go 里 `n * 2`、`n < 0`、`n + 0.5` 之所以能编译，是因为这些是
    // **无类型常量**；C# 里同样的写法只有靠由数值类型到 Number 的隐式转换才能编译。
    // 反方向仍是 explicit（对应 Go 的 `float64(n)`，见 JsNum.cs），否则 `double d = n;` 这种
    // 会丢精度的写法就会静默通过。
    public static implicit operator Number(double value) => new(value);

    // int/long/float 各自给一条：int/long 只经这一条用户定义转换（不必绕 double→Number 两步），
    // float 同理。三者最终都只是 `(double)` 后包一层，与 Go 的无类型常量取值一致。
    public static implicit operator Number(int value) => new(value);

    public static implicit operator Number(long value) => new(value);

    public static implicit operator Number(float value) => new(value);

    // JS 的 `a % b`（ECMA-262 Number::remainder）与 IEEE 754 的 fmod 是同一个运算，C# 的 double `%`
    // 就是 fmod。这里直接复用 JS 语义的 Remainder（见 JsNum.cs），保证 `%` 与 `n.Remainder(m)`
    // 永远给出同一个值（NaN/±0/Inf 的边界两边本来就一致）。
    public static Number operator %(Number left, Number right) => left.Remainder(right);

    // JS 的 `++x` / `x++` / `--x` / `x--` 都是 `x = x + 1` / `x = x - 1` 的精确浮点运算
    // （没有溢出概念），所以 checked 与 unchecked 在这里同义：.NET 的默认 checked ++ / --
    // 实现就是 `=> ++value` / `=> --value`，最终走到这两行（不会抛 OverflowException，
    // MaxValue + 1 按 IEEE 舍入回 MaxValue，Infinity + 1 仍是 Infinity）。
    public static Number operator ++(Number value) => new(value.Value + 1.0);

    public static Number operator --(Number value) => new(value.Value - 1.0);

    // IAdditiveIdentity / IMultiplicativeIdentity：0 与 1。
    public static Number AdditiveIdentity => new(0.0);

    public static Number MultiplicativeIdentity => new(1.0);
}

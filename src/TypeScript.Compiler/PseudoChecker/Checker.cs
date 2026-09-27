// ported from: internal/pseudochecker/checker.go
//
// 与 Go 的差异（逐条）：
// - Go 的 `PseudoChecker` 是指针接收者类型 → C# 是普通类（引用语义一致）。未导出字段 `strictNullChecks` /
//   `exactOptionalPropertyTypes` → `_strictNullChecks` / `_exactOptionalPropertyTypes`（docs/architecture.md §2）。
//   `_exactOptionalPropertyTypes` 与 Go 一样**只保存不读取**（Go 侧 checker.go 里赋值后没有任何读取点）。
// - Go 的包级变量 `PseudoTypeUndefined`…`PseudoTypeTrue`（导出）→ 同名的 `public static readonly` 字段放在
//   `PseudoChecker`（一个 Go package = 一个 C# 类型，见 docs/architecture.md §2 的 "包级函数/常量/变量放静态类"）。
//   它们原来由未导出的 `newPseudoType(kind, data)` 构造；`data` 层已按 docs/ast.md §2 折叠（见 Type.cs 文件头），
//   所以直接 `new PseudoType { Kind = … }`。
namespace TypeScript.PseudoChecker;

// `PseudoChecker` 的构建/入口；lookup.go 的方法在 Lookup.cs。

public sealed partial class PseudoChecker
{
    private readonly bool _strictNullChecks;

    // Go 里赋值后没有任何读取点（保留以与 Go 字段形状一致）。
    private readonly bool _exactOptionalPropertyTypes;

    private PseudoChecker(bool strictNullChecks, bool exactOptionalPropertyTypes)
    {
        _strictNullChecks = strictNullChecks;
        _exactOptionalPropertyTypes = exactOptionalPropertyTypes;
    }

    public static PseudoChecker NewPseudoChecker(bool strictNullChecks, bool exactOptionalPropertyTypes) =>
        new(strictNullChecks, exactOptionalPropertyTypes);

    public static readonly PseudoType PseudoTypeUndefined = new() { Kind = PseudoTypeKind.Undefined };
    public static readonly PseudoType PseudoTypeNull = new() { Kind = PseudoTypeKind.Null };
    public static readonly PseudoType PseudoTypeAny = new() { Kind = PseudoTypeKind.Any };
    public static readonly PseudoType PseudoTypeString = new() { Kind = PseudoTypeKind.String };
    public static readonly PseudoType PseudoTypeNumber = new() { Kind = PseudoTypeKind.Number };
    public static readonly PseudoType PseudoTypeBigInt = new() { Kind = PseudoTypeKind.BigInt };
    public static readonly PseudoType PseudoTypeBoolean = new() { Kind = PseudoTypeKind.Boolean };
    public static readonly PseudoType PseudoTypeFalse = new() { Kind = PseudoTypeKind.False };
    public static readonly PseudoType PseudoTypeTrue = new() { Kind = PseudoTypeKind.True };
}

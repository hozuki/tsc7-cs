// ported from: internal/tsoptions/declstypeacquisition.go
//
// 映射约定（docs/architecture.md §2）：未导出的 `typeAcquisitionDeclaration`/`typeAcquisitionDecls` 转 PascalCase；
// **.editorconfig 的非公开字段命名规则（IDE1006）要求 `_camelCase`**，故用只读的 `internal static` 属性
// （属性受 PascalCase 规则约束），名字与 ticket 契约一致、读取方写法不变（tsconfigparsing.go 的两处引用照此写）。
// `ElementOptions: commandLineOptionsToMap(typeAcquisitionDecls)` →
// `ElementOptions = CommandLineOptionsToMap(TypeAcquisitionDecls)`（`CommandLineOptionsToMap` 在 TsConfigParsing.cs）。
// **偏离**：Go 的包级初始化按依赖顺序解析，`typeAcquisitionDeclaration` 文本上在前也能取到值；C# 的静态初始化是
// 文本顺序，所以这里把被依赖的 `TypeAcquisitionDecls` 排在前面（声明表内容与 Go 顺序不变）。
namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    // Go: declstypeacquisition.go:10-29
    internal static CommandLineOption[] TypeAcquisitionDecls { get; } =
    [
        new()
        {
            Name = "enable",
            Kind = CommandLineOptionKind.Boolean,
            DefaultValueDescription = false,
        },
        new()
        {
            Name = "include",
            Kind = CommandLineOptionKind.List,
        },
        new()
        {
            Name = "exclude",
            Kind = CommandLineOptionKind.List,
        },
        new()
        {
            Name = "disableFilenameBasedTypeAcquisition",
            Kind = CommandLineOptionKind.Boolean,
            DefaultValueDescription = false,
        },
    ];

    // Go: declstypeacquisition.go:3-7
    internal static CommandLineOption TypeAcquisitionDeclaration { get; } = new()
    {
        Name = "typeAcquisition",
        Kind = CommandLineOptionKind.Object,
        ElementOptions = CommandLineOptionsToMap(TypeAcquisitionDecls),
    };

}
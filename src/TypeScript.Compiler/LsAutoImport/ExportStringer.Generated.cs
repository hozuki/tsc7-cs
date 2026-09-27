// ported from: internal/ls/autoimport/export_stringer_generated.go
//
// 生成物说明：Go 侧由 `stringer -type=ExportSyntax` 生成 `_ExportSyntax_name`/`_ExportSyntax_index` 与
// `func (i ExportSyntax) String() string`。C# 的 enum 不能声明实例方法（§3.3），因此 String() 落成
// **静态类 `AutoImport` 上的扩展方法**（与 `Core.Core`/`LsProto.LsProto` 的 stringer 表同款写法），
// 调用点照 Go 写 `x.String()`。数值与文本表逐字照抄 `export_stringer_generated.go`。
namespace TypeScript.LsAutoImport;

public static partial class AutoImport
{
    private const string _exportSyntaxName = "ExportSyntaxNoneExportSyntaxModifierExportSyntaxNamedExportSyntaxDefaultModifierExportSyntaxDefaultDeclarationExportSyntaxEqualsExportSyntaxUMDExportSyntaxStarExportSyntaxCommonJSModuleExportsExportSyntaxCommonJSExportsProperty";

    private static readonly ushort[] _exportSyntaxIndex = [0, 16, 36, 53, 80, 110, 128, 143, 159, 192, 227];

    /// <summary>Go: <c>func (i ExportSyntax) String() string</c>（export_stringer_generated.go:29-35）。</summary>
    public static string String(this ExportSyntax i)
    {
        var idx = (int)i - 0;
        if (idx < 0 || idx >= _exportSyntaxIndex.Length - 1)
        {
            return $"ExportSyntax({(int)i})";
        }

        return _exportSyntaxName.Substring(_exportSyntaxIndex[idx], _exportSyntaxIndex[idx + 1] - _exportSyntaxIndex[idx]);
    }
}

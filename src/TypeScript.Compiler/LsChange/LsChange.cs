// ported from: tsc/internal/ls/change/{tracker.go,trackerimpl.go,delete.go} 的**包级函数**汇总
//
// 本文件承载 Go `change` 包的包级函数（导出 + 未导出），因为包级函数要落在与 package 同名的静态类
// `LsChange` 里（docs/architecture.md §2；先例：`LsUtil`/`LsProto`）。按 Go 的文件归属，其余包级函数分散在
// Tracker.cs（tracker.go）、TrackerImpl.cs（trackerimpl.go）、Delete.cs（delete.go）的 `partial class LsChange`
// 里；本文件是主声明，放两个**导出**入口：
//
// - `NewTracker` ← tracker.go:96-110（Go: `change.NewTracker`）。
//   消费方契约要求 `LsChange.NewTracker(...)`。注意：`TypeScript.LsChange`（命名空间）与 `LsChange`（类型）
//   同名 ⇒ 在 `TypeScript.LsChange.*` **之外**的调用点必须先 `using static TypeScript.LsChange.LsChange;` 再裸名
//   调用 `NewTracker(...)`（或直接 `new Tracker(...)`）；写 `LsChange.NewTracker(...)` 会先绑到命名空间。
//   为方便消费方，`Tracker` 上另有一个同义的静态工厂 `Tracker.NewTracker(...)`。
// - `GetFormatCodeSettingsForWriting` ← trackerimpl.go:243-252（Go: `change.GetFormatCodeSettingsForWriting`）。
//   Go 的 `lsutil.ProbablyUsesSemicolons` 由 LsUtil/Utilities.cs 提供（本切片不重复实现，直接调用）。
//
// 与 Go 的偏离（逐条）：
// 1) `ProbablyUsesSemicolons` 已由 LsUtil/Utilities.cs 落地 → 直接调用 `return`（依赖 lsutil 完整移植）。
// 2) 文件头第 6 条的命名空间遮蔽：这里的 `NewEmitContext`/`WithFormatCodeSettings`/`ProbablyUsesSemicolons` 都用
//    `using static` 引入后裸名调用。
// 3) `strings.TrimSuffix` 在 C# 侧没有公共实现（LsUtil/Utilities.cs、ModuleSpecifiers/Util.cs 里都是 private）
//    → 这里补一个同义的 `internal static TrimSuffix`（Ordinal 语义，与那两处同形），供 trackerimpl.go 的调用点用。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsUtil;
using static TypeScript.Format.Format;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Printer.Printer;

namespace TypeScript.LsChange;

/// <summary>
/// Go: `package change` 的包级函数落点（§2；命名空间遮蔽见文件头）。
/// </summary>
public static partial class LsChange
{
    /// <summary>Go: <c>func NewTracker(ctx context.Context, compilerOptions *core.CompilerOptions,
    /// formatOptions lsutil.FormatCodeSettings, converters *lsconv.Converters) *Tracker</c>（tracker.go:96-110）。</summary>
    public static Tracker NewTracker(Context ctx, CompilerOptions compilerOptions, FormatCodeSettings formatOptions, Converters converters)
    {
        var emitContext = NewEmitContext();
        var newLine = compilerOptions.NewLine.GetNewLineCharacter();
        ctx = WithFormatCodeSettings(ctx, formatOptions, newLine); // !!! formatSettings in context?
        return new Tracker(emitContext, ctx, converters, formatOptions, newLine);
    }

    /// <summary>Go: <c>func GetFormatCodeSettingsForWriting(options lsutil.FormatCodeSettings,
    /// sourceFile *ast.SourceFile) lsutil.FormatCodeSettings</c>（trackerimpl.go:243-252）。
    /// `FormatCodeSettings` 是 C# 的 struct ⇒ 传值语义与 Go 的结构体拷贝一致。</summary>
    public static FormatCodeSettings GetFormatCodeSettingsForWriting(FormatCodeSettings options, SourceFile sourceFile)
    {
        var shouldAutoDetectSemicolonPreference = options.Semicolons == SemicolonPreferenceIgnore;
        var shouldRemoveSemicolons = options.Semicolons == SemicolonPreferenceRemove || shouldAutoDetectSemicolonPreference && !ProbablyUsesSemicolons(sourceFile);
        if (shouldRemoveSemicolons)
        {
            options.Semicolons = SemicolonPreferenceRemove;
        }

        return options;
    }

    /// <summary>Go 的 <c>strings.TrimSuffix(s, suffix)</c>（Ordinal 语义，见文件头第 3 条）。</summary>
    internal static string TrimSuffix(string s, string suffix)
    {
        return s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;
    }
}

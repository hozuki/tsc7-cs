// ported from: internal/transformers/estransforms/definitions.go（43 行，全部）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 package 级 `var`（`esDecoratorAndClassFields`、`NewESNextTransformer` … `NewES2016Transformer`）→
//    `public static partial class EsTransforms` 的 `static readonly` 字段（docs/architecture.md §2：Go 包级成员
//    落进与 package 同名的静态类）。Go 未导出名 `esDecoratorAndClassFields` → `_esDecoratorAndClassFields`；
//    Go 导出名（`NewESNextTransformer` …）原样。
// 2) 字段的**声明顺序 = Go 的初始化依赖顺序**（Go 的包级 var 按依赖求值，C# 的静态字段按文本顺序求值）；
//    每个字段只引用 `Chain`（方法）与前面已声明的字段，所以两种顺序在这里等价。
// 3) Go 的 `options.GetEmitScriptTarget()` 在一个无 default 的 switch 上有 default 分支 →
//    C# 的 `switch` 语句保持同样的 case 集合与 default。
using TypeScript.Core;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.EsTransforms;

public static partial class EsTransforms
{
    /// <summary>Go: <c>var esDecoratorAndClassFields = transformers.Chain(newESDecoratorTransformer, newClassFieldsTransformer)</c>。</summary>
    private static readonly TransformerFactory _esDecoratorAndClassFields = Chain(NewESDecoratorTransformer, NewClassFieldsTransformer);

    /// <summary>
    /// Go: <c>var NewESNextTransformer = transformers.Chain(newUsingDeclarationTransformer, esDecoratorAndClassFields)</c>。
    /// <para>2025: only module system syntax (import attributes, json modules), untransformed regex modifiers</para>
    /// <para>2024: no new downlevel syntax</para>
    /// <para>2023: no new downlevel syntax</para>
    /// <para>2022: class static blocks and class fields are handled by newClassFieldsTransformer</para>
    /// </summary>
    public static readonly TransformerFactory NewESNextTransformer = Chain(NewUsingDeclarationTransformer, _esDecoratorAndClassFields);

    /// <summary>Go: <c>var NewES2021Transformer = transformers.Chain(NewESNextTransformer, newLogicalAssignmentTransformer)</c>。</summary>
    public static readonly TransformerFactory NewES2021Transformer = Chain(NewESNextTransformer, NewLogicalAssignmentTransformer);

    /// <summary>Go: <c>var NewES2020Transformer = transformers.Chain(NewES2021Transformer, newNullishCoalescingTransformer, newOptionalChainTransformer)</c>。</summary>
    public static readonly TransformerFactory NewES2020Transformer = Chain(NewES2021Transformer, NewNullishCoalescingTransformer, NewOptionalChainTransformer);

    /// <summary>Go: <c>var NewES2019Transformer = transformers.Chain(NewES2020Transformer, newOptionalCatchTransformer)</c>。</summary>
    public static readonly TransformerFactory NewES2019Transformer = Chain(NewES2020Transformer, NewOptionalCatchTransformer);

    /// <summary>Go: <c>var NewES2018Transformer = transformers.Chain(NewES2019Transformer, newObjectRestSpreadTransformer, newforawaitTransformer, newTaggedTemplateLiftRestrictionTransformer)</c>。</summary>
    public static readonly TransformerFactory NewES2018Transformer = Chain(NewES2019Transformer, NewObjectRestSpreadTransformer, NewForAwaitTransformer, NewTaggedTemplateLiftRestrictionTransformer);

    /// <summary>Go: <c>var NewES2017Transformer = transformers.Chain(NewES2018Transformer, newAsyncTransformer)</c>。</summary>
    public static readonly TransformerFactory NewES2017Transformer = Chain(NewES2018Transformer, NewAsyncTransformer);

    /// <summary>Go: <c>var NewES2016Transformer = transformers.Chain(NewES2017Transformer, newExponentiationTransformer)</c>。</summary>
    public static readonly TransformerFactory NewES2016Transformer = Chain(NewES2017Transformer, NewExponentiationTransformer);

    /// <summary>Go: <c>func GetESTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? GetESTransformer(TransformOptions opts)
    {
        var options = opts.CompilerOptions!;
        switch (options.GetEmitScriptTarget())
        {
            case ScriptTarget.ESNext:
                return _esDecoratorAndClassFields(opts);
            case ScriptTarget.ES2025:
            case ScriptTarget.ES2024:
            case ScriptTarget.ES2023:
            case ScriptTarget.ES2022:
            case ScriptTarget.ES2021:
                return NewESNextTransformer(opts);
            case ScriptTarget.ES2020:
                return NewES2021Transformer(opts);
            case ScriptTarget.ES2019:
                return NewES2020Transformer(opts);
            case ScriptTarget.ES2018:
                return NewES2019Transformer(opts);
            case ScriptTarget.ES2017:
                return NewES2018Transformer(opts);
            case ScriptTarget.ES2016:
                return NewES2017Transformer(opts);
            default: // other, older, option, transform maximally
                return NewES2016Transformer(opts);
        }
    }
}

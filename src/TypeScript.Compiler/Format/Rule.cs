// ported from: internal/format/rule.go
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.2/§3.3）：
// - 包级函数 → 静态类 `Format`（与 package 同名，`partial` 分布在 Format/ 的各个文件里）。
// - Go 未导出类型 `ruleImpl`/`tokenRange`/`ruleSpec` → `RuleImpl`/`TokenRange`/`RuleSpec`
//   （§2：未导出类型按 C# 习惯命名）；未导出字段 → `_camelCase`（internal，同程序集可见）。
// - `rule(debugName string, left any, right any, …)`：Go 用 `any` + 类型开关接受
//   `ast.Kind`/`[]ast.Kind`/`tokenRange`。C# 的 `ToTokenRange` 用模式匹配做同一件事
//   （装箱只发生在规则表构造时一次）。
// - `ruleFlags ...ruleFlags`（可变参数，只取第一个）→ `params RuleFlags[] flags`。
// - `iota` 枚举 → C# enum，数值与 Go 一致（§3.3）。`ruleAction` 是位掩码 → `[Flags]`。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;

namespace TypeScript.Format;

/// <summary>Go 的 <c>type contextPredicate = func(ctx *FormattingContext) bool</c>。</summary>
public delegate bool ContextPredicate(FormattingContext context);

/// <summary>Go 的 <c>type ruleAction int</c>（位掩码）。</summary>
[Flags]
public enum RuleAction
{
    None = 0,
    StopProcessingSpaceActions = 1 << 0,
    StopProcessingTokenActions = 1 << 1,
    InsertSpace = 1 << 2,
    InsertNewLine = 1 << 3,
    DeleteSpace = 1 << 4,
    DeleteToken = 1 << 5,
    InsertTrailingSemicolon = 1 << 6,

    StopAction = StopProcessingSpaceActions | StopProcessingTokenActions,
    ModifySpaceAction = InsertSpace | InsertNewLine | DeleteSpace,
    ModifyTokenAction = DeleteToken | InsertTrailingSemicolon,
}

/// <summary>Go 的 <c>type ruleFlags int</c>（<c>ruleFlagsNone</c>/<c>ruleFlagsCanDeleteNewLines</c>）。</summary>
public enum RuleFlags
{
    None = 0,
    CanDeleteNewLines = 1,
}

/// <summary>Go 的 <c>type ruleImpl struct</c>（未导出类型；方法名照抄 Go 的导出方法）。</summary>
internal sealed class RuleImpl
{
    internal string _debugName = "";
    internal ContextPredicate[] _context = [];
    internal RuleAction _action;
    internal RuleFlags _flags;

    public RuleAction Action() => _action;

    public ContextPredicate[] Context() => _context;

    public RuleFlags Flags() => _flags;

    /// <summary>Go 的 <c>func (r ruleImpl) String() string</c>（fmt.Stringer → ToString 覆写，§3.3）。</summary>
    public override string ToString() => _debugName;
}

/// <summary>Go 的 <c>type tokenRange struct</c>（未导出类型）。</summary>
internal struct TokenRange
{
    internal Kind[] _tokens;
    internal bool _isSpecific;
}

/// <summary>Go 的 <c>type ruleSpec struct</c>（未导出类型）。</summary>
internal struct RuleSpec
{
    internal TokenRange _leftTokenRange;
    internal TokenRange _rightTokenRange;
    internal RuleImpl _rule;
}

public static partial class Format
{
    /// <summary>Go 的 <c>var anyContext = []contextPredicate{}</c>。</summary>
    public static readonly ContextPredicate[] AnyContext = [];

    /// <summary>
    /// A rule takes a two tokens (left/right) and a particular context
    /// for which you're meant to look at them. You then declare what should the
    /// whitespace annotation be between these tokens via the action param.
    /// </summary>
    /// <param name="debugName">Name to print</param>
    /// <param name="left">The left side of the comparison</param>
    /// <param name="right">The right side of the comparison</param>
    /// <param name="context">A set of filters to narrow down the space in which this formatter rule applies</param>
    /// <param name="action">a declaration of the expected whitespace</param>
    /// <param name="flags">whether the rule deletes a line or not, defaults to no-op</param>
    internal static RuleSpec Rule(
        string debugName,
        object left,
        object right,
        ContextPredicate[] context,
        RuleAction action,
        params RuleFlags[] flags)
    {
        var flag = RuleFlags.None;
        if (flags.Length > 0)
        {
            flag = flags[0];
        }

        var leftRange = ToTokenRange(left);
        var rightRange = ToTokenRange(right);
        var rule = new RuleImpl
        {
            _debugName = debugName,
            _context = context,
            _action = action,
            _flags = flag,
        };
        return new RuleSpec
        {
            _leftTokenRange = leftRange,
            _rightTokenRange = rightRange,
            _rule = rule,
        };
    }

    internal static TokenRange ToTokenRange(object e)
    {
        switch (e)
        {
            case Kind kind:
                return new TokenRange { _isSpecific = true, _tokens = [kind] };
            case Kind[] kinds:
                return new TokenRange { _isSpecific = true, _tokens = kinds };
            case TokenRange range:
                return range;
        }

        Debug.Fail("Unknown argument type passed to toTokenRange - only ast.Kind, []ast.Kind, and tokenRange supported");
        return default;
    }
}

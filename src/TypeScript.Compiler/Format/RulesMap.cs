// ported from: internal/format/rulesmap.go
//
// 与 Go 的差异（C# 语言特性）：
// - 包级函数 → 静态类 `Format`；Go 未导出函数 → internal + PascalCase。
// - `var getRulesMap = sync.OnceValue(buildRulesMap)` → `Lazy<RuleImpl[][]>`（与 Ast 的 `SourceFileDataCell` 同款，
//   AOT 安全、不用反射）。
// - Go 的 `[]*ruleImpl` 桶（append/slices.Insert）→ C# 的 `List<RuleImpl>?`（桶按需创建，nil = Go 的 nil 切片）；
//   `slices.Insert(rules, index, rule)` → `List.Insert(index, rule)`。
// - `rules []*ruleImpl` 参数是"复用调用方的缓冲"（span.go 里 `w.currentRules[:0]`）；C# 用 `List<RuleImpl>` +
//   `Clear()` 表达同一件事。
// - 枚举与 int 的位运算：`rule.Action() & acceptRuleActions != 0` → `!= RuleAction.None`（C# 不允许 enum 与 0 比较）。
// - `panic`/`debug.Assert` → `Debug.Fail`/`Debug.Assert`（§3.2）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;

namespace TypeScript.Format;

/// <summary>
/// Go 的 <c>type RulesPosition int</c>（各子桶在插入位图里的位移）。
/// 枚举体里的 <c>Format.MaskBitSize</c> 必须写全名：C# 的 enum 会继承 <c>System.Enum</c> 的静态成员
/// <c>Enum.Format</c>，裸写 <c>Format</c> 会解析到那个方法（CS0119）。
/// </summary>
public enum RulesPosition
{
    StopRulesSpecific = 0,
    StopRulesAny = TypeScript.Format.Format.MaskBitSize * 1,
    ContextRulesSpecific = TypeScript.Format.Format.MaskBitSize * 2,
    ContextRulesAny = TypeScript.Format.Format.MaskBitSize * 3,
    NoContextRulesSpecific = TypeScript.Format.Format.MaskBitSize * 4,
    NoContextRulesAny = TypeScript.Format.Format.MaskBitSize * 5,
}

public static partial class Format
{
    internal const int MaskBitSize = 5;
    internal const int Mask = 0b11111; // MaskBitSize bits

    internal const int MapRowLength = (int)Kind.LastToken + 1;

    private static readonly Lazy<RuleImpl[][]> _getRulesMap = new(BuildRulesMap);

    internal static List<RuleImpl> GetRules(FormattingContext context, List<RuleImpl> rules)
    {
        var bucket = _getRulesMap.Value[GetRuleBucketIndex(context._currentTokenSpan.Kind, context._nextTokenSpan.Kind)];
        if (bucket.Length > 0)
        {
            var ruleActionMask = RuleAction.None;
            foreach (var rule in bucket)
            {
                var acceptRuleActions = ~GetRuleActionExclusion(ruleActionMask);
                if ((rule.Action() & acceptRuleActions) != RuleAction.None)
                {
                    var preds = rule.Context();
                    var accepted = true;
                    foreach (var p in preds)
                    {
                        if (!p(context))
                        {
                            accepted = false;
                            break;
                        }
                    }

                    if (!accepted)
                    {
                        continue;
                    }

                    rules.Add(rule);
                    ruleActionMask |= rule.Action();
                }
            }

            return rules;
        }

        return rules;
    }

    internal static int GetRuleBucketIndex(Kind row, Kind column)
    {
        Debug.Assert(row <= Kind.LastKeyword && column <= Kind.LastKeyword, "Must compute formatting context from tokens");
        return ((int)row * MapRowLength) + (int)column;
    }

    /// <summary>
    /// For a given rule action, gets a mask of other rule actions that
    /// cannot be applied at the same position.
    /// </summary>
    internal static RuleAction GetRuleActionExclusion(RuleAction ruleAction)
    {
        var mask = RuleAction.None;
        if ((ruleAction & RuleAction.StopProcessingSpaceActions) != RuleAction.None)
        {
            mask |= RuleAction.ModifySpaceAction;
        }

        if ((ruleAction & RuleAction.StopProcessingTokenActions) != RuleAction.None)
        {
            mask |= RuleAction.ModifyTokenAction;
        }

        if ((ruleAction & RuleAction.ModifySpaceAction) != RuleAction.None)
        {
            mask |= RuleAction.ModifySpaceAction;
        }

        if ((ruleAction & RuleAction.ModifyTokenAction) != RuleAction.None)
        {
            mask |= RuleAction.ModifyTokenAction;
        }

        return mask;
    }

    internal static RuleImpl[][] BuildRulesMap()
    {
        var rules = GetAllRules();
        // Map from bucket index to array of rules
        var m = new List<RuleImpl>?[MapRowLength * MapRowLength];
        // This array is used only during construction of the rulesbucket in the map
        var rulesBucketConstructionStateList = new int[m.Length];
        foreach (var rule in rules)
        {
            var specificRule = rule._leftTokenRange._isSpecific && rule._rightTokenRange._isSpecific;

            foreach (var left in rule._leftTokenRange._tokens)
            {
                foreach (var right in rule._rightTokenRange._tokens)
                {
                    var index = GetRuleBucketIndex(left, right);
                    m[index] = AddRule(m[index], rule._rule, specificRule, rulesBucketConstructionStateList, index);
                }
            }
        }

        var result = new RuleImpl[m.Length][];
        for (var i = 0; i < m.Length; i++)
        {
            result[i] = m[i] is null ? [] : [.. m[i]!];
        }

        return result;
    }

    // The Rules list contains all the inserted rules into a rulebucket in the following order:
    //
    //  1- Ignore rules with specific token combination
    //  2- Ignore rules with any token combination
    //  3- Context rules with specific token combination
    //  4- Context rules with any token combination
    //  5- Non-context rules with specific token combination
    //  6- Non-context rules with any token combination
    //
    // The member rulesInsertionIndexBitmap is used to describe the number of rules
    // in each sub-bucket (above) hence can be used to know the index of where to insert
    // the next rule. It's a bitmap which contains 6 different sections each is given 5 bits.
    //
    // Example:
    // In order to insert a rule to the end of sub-bucket (3), we get the index by adding
    // the values in the bitmap segments 3rd, 2nd, and 1st.
    private static List<RuleImpl> AddRule(
        List<RuleImpl>? rules,
        RuleImpl rule,
        bool specificTokens,
        int[] constructionState,
        int rulesBucketIndex)
    {
        RulesPosition position;
        if ((rule.Action() & RuleAction.StopAction) != RuleAction.None)
        {
            position = specificTokens ? RulesPosition.StopRulesSpecific : RulesPosition.StopRulesAny;
        }
        else if (rule.Context().Length != 0)
        {
            position = specificTokens ? RulesPosition.ContextRulesSpecific : RulesPosition.ContextRulesAny;
        }
        else
        {
            position = specificTokens ? RulesPosition.NoContextRulesSpecific : RulesPosition.NoContextRulesAny;
        }

        rules ??= [];
        var state = constructionState[rulesBucketIndex];

        rules.Insert(GetRuleInsertionIndex(state, position), rule);
        constructionState[rulesBucketIndex] = IncreaseInsertionIndex(state, position);
        return rules;
    }

    private static int GetRuleInsertionIndex(int indexBitmap, RulesPosition maskPosition)
    {
        var index = 0;
        for (var pos = 0; pos <= (int)maskPosition; pos += MaskBitSize)
        {
            index += indexBitmap & Mask;
            indexBitmap >>= MaskBitSize;
        }

        return index;
    }

    private static int IncreaseInsertionIndex(int indexBitmap, RulesPosition maskPosition)
    {
        var value = ((indexBitmap >> (int)maskPosition) & Mask) + 1;
        Debug.Assert((value & Mask) == value, "Adding more rules into the sub-bucket than allowed. Maximum allowed is 32 rules.");
        return (indexBitmap & ~(Mask << (int)maskPosition)) | (value << (int)maskPosition);
    }
}

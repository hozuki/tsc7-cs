// ported from: internal/transformers/inliners/constenum.go（102 行，全部）
//
// Go 包级函数 → 与包同名的静态类 `Inliners`（partial；本包只有这一个文件）。
//
// 与 Go 的偏离（C# 语言特性）：
// 1) Go 的 `switch v := value.(type)`（`value` 是 `any`，来自 `EmitResolver.GetConstantValue`）→ C# 的 `switch (value)`
//    模式匹配（`case Number v:` / `case string v:` / `case PseudoBigInt v:`）。
//    Go 的 type switch **没有 default**：未命中时 `replacement` 保持 nil 并被原样 `return`（调用方视为"删除该节点"）。
//    C# 侧保留同一形状（`Node? replacement = null;` + 命中后处理），但把"replacement 为 nil"显式短路返回
//    —— 因为 Go 那句 `AddSyntheticTrailingComment(nil, …)` 只是往 map 里插入 nil 键（不 panic），
//    而 C# 的 `Dictionary<Node,…>` 用 nil 键会抛 ArgumentNullException。该分支在 `GetConstantValue`
//    的契约（`string | number | PseudoBigInt | undefined`）下不可达，故选"不崩、返回 nil"这一等价可观测行为。
// 2) `jsnum.PseudoBigInt{}` 零值比较（Go 的结构体相等）→ C# 逐字段判断（`!v.Negative && v.Base10Value.Length == 0`；
//    C# 侧 `_base10Value` 为 null 时 `Base10Value` 归一为 `""`，与 Go 的零值 `""` 一致）。
// 3) `strings.Builder` → `StringBuilder`；`text[i+2:]` 的零拷贝切片 → `ReadOnlySpan<char>` 视图（docs §3.8）。
// 4) `compilerOptions := opt.CompilerOptions` 后立刻解引用（Go 里 nil 会 panic）→ C# `opt.CompilerOptions!`（同契约，docs §3.2）。
// 5) Go 的 `debug.Fail(...)` → `Debug.Fail(...)`（同一个 DebugFailureException）。
// 6) Go 的 `*ast.SourceFile` 字段 → C# `SourceFile?`；`_currentSourceFile` 在 Go 里声明后从未使用（**保持 1:1 保留**）；
//    因 `private` 未使用字段会触发 CS0169（本工程 TreatWarningsAsErrors）→ 用 `internal`（Go 未导出成员在 C# 的可见性对应物）。
using System;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.JsNum;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Scanner.Scanner;
using NodeFactory = TypeScript.Printer.NodeFactory; // Go: printer.NodeFactory（与 Ast.NodeFactory 同名，必须消歧）

namespace TypeScript.Transformers.Inliners;

/// <summary>Go: <c>type ConstEnumInliningTransformer struct { transformers.Transformer; compilerOptions *core.CompilerOptions; currentSourceFile *ast.SourceFile; emitResolver printer.EmitResolver }</c>。</summary>
internal sealed class ConstEnumInliningTransformer : Transformer
{
    private readonly CompilerOptions _compilerOptions;

    /// <summary>Go 字段 <c>currentSourceFile</c>：Go 里声明后从未使用（1:1 保留）。</summary>
    internal SourceFile? _currentSourceFile;

    private readonly EmitResolver? _emitResolver;

    internal ConstEnumInliningTransformer(CompilerOptions compilerOptions, EmitResolver? emitResolver)
    {
        _compilerOptions = compilerOptions;
        _emitResolver = emitResolver;
    }

    /// <summary>Go: <c>func (tx *ConstEnumInliningTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                {
                    var parse = EmitContext().ParseNode(node);
                    if (parse is null)
                    {
                        return Visitor().VisitEachChild(node);
                    }
                    var value = _emitResolver!.GetConstantValue(parse);
                    if (value is not null)
                    {
                        Node? replacement = null;
                        switch (value)
                        {
                            case Number v:
                                if (v.IsInf())
                                {
                                    if (v.Abs() == v)
                                    {
                                        replacement = Factory().NewIdentifier("Infinity");
                                    }
                                    else
                                    {
                                        replacement = Factory().NewPrefixUnaryExpression(Kind.MinusToken, Factory().NewIdentifier("Infinity"));
                                    }
                                }
                                else if (v.IsNaN())
                                {
                                    replacement = Factory().NewIdentifier("NaN");
                                }
                                else if (v.Abs() == v)
                                {
                                    replacement = Factory().NewNumericLiteral(v.String(), TokenFlags.None);
                                }
                                else
                                {
                                    replacement = Factory().NewPrefixUnaryExpression(Kind.MinusToken, Factory().NewNumericLiteral(v.Abs().String(), TokenFlags.None));
                                }
                                break;
                            case string v:
                                replacement = Factory().NewStringLiteral(v, TokenFlags.None);
                                break;
                            case PseudoBigInt v: // technically not supported by strada, and issues a checker error, handled here for completeness
                                if (!v.Negative && v.Base10Value.Length == 0)
                                {
                                    replacement = Factory().NewBigIntLiteral("0", TokenFlags.None);
                                }
                                else if (!v.Negative)
                                {
                                    replacement = Factory().NewBigIntLiteral(v.Base10Value, TokenFlags.None);
                                }
                                else
                                {
                                    replacement = Factory().NewPrefixUnaryExpression(Kind.MinusToken, Factory().NewBigIntLiteral(v.Base10Value, TokenFlags.None));
                                }
                                break;
                        }

                        if (replacement is null)
                        {
                            // Go: type switch 无 default → replacement 仍为 nil → 后面的注释写入（nil 键，不 panic）后 return nil。
                            return null;
                        }

                        if (_compilerOptions.RemoveComments.IsFalseOrUnknown())
                        {
                            var original = EmitContext().MostOriginal(node);
                            if (original is not null && !NodeIsSynthesized(original))
                            {
                                var originalText = GetTextOfNode(original);
                                var escapedText = SafeMultiLineComment(originalText);
                                EmitContext().AddSyntheticTrailingComment(replacement, Kind.MultiLineCommentTrivia, escapedText, false);
                            }
                        }
                        return replacement;
                    }
                    return Visitor().VisitEachChild(node);
                }
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func safeMultiLineComment(text string) string</c>（未导出包级函数 → static class `Inliners` 的 private 成员）。</summary>
    private static string SafeMultiLineComment(string text)
    {
        // Go: var b strings.Builder; b.Grow(len(text)+2)
        var b = new StringBuilder(text.Length + 2);
        b.Append(' ');
        // Go: text[i+2:] 是零拷贝切片 → C# 用 ReadOnlySpan<char> 视图（docs §3.8）
        var remaining = text.AsSpan();
        while (true)
        {
            var i = remaining.IndexOf("*/");
            if (i < 0)
            {
                break;
            }
            b.Append(remaining[..i]);
            b.Append("*_/");
            remaining = remaining[(i + 2)..];
        }
        b.Append(remaining);
        b.Append(' ');
        return b.ToString();
    }
}

/// <summary>Go 包级函数（package inliners）→ 与包同名的静态类。</summary>
public static class Inliners
{
    /// <summary>Go: <c>func NewConstEnumInliningTransformer(opt *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer NewConstEnumInliningTransformer(TransformOptions opt)
    {
        var compilerOptions = opt.CompilerOptions!; // Go: opt.CompilerOptions（nil → 下一行解引用 panic）
        var emitContext = opt.Context;
        if (compilerOptions.GetIsolatedModules())
        {
            Debug.Fail("const enums are not inlined under isolated modules");
        }
        var tx = new ConstEnumInliningTransformer(compilerOptions, opt.EmitResolver);
        return tx.NewTransformer(tx.Visit, emitContext);
    }
}

// ported from: internal/ls/linkedediting.go:1-116
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 包级变量 `jsxTagWordPattern = new("[a-zA-Z0-9:\\-\\._$]*")`：Go 1.26 的 `new(expr)` 取的是
//    **指向新变量的指针**（Go 的 `WordPattern *string`），C# 的 `LinkedEditingRanges.WordPattern` 是 `string?`
//    ⇒ 落成 `const string JsxTagWordPattern`（字面量与 Go 完全一致：`\\-`/`\\.` 都是"反斜杠+字符"两个字符）。
//    Go 未导出 ⇒ `internal`。
// 2) `*LanguageService` 方法 → `public sealed partial class LanguageService` 成员（Go 导出 ⇒ `public`）；
//    多返回值 `(lsproto.LinkedEditingRangeResponse, error)` → 元组 `(LinkedEditingRangesOrNull Response, Exception? Error)`
//    （响应类型别名在 C# 侧展开成底层 union：`LinkedEditingRangeResponse = LinkedEditingRangesOrNull`）。
// 3) `l.converters` → `_converters!`（C# 字段，见 Ls/LanguageService.cs）；`l.getProgramAndFile` → `GetProgramAndFile`。
// 4) `core.TextPos(...)` 由 `TextPos` 的隐式转换承接（Core/Text.cs:34）；`core.NewTextRange(a, b)` → `new TextRange(a, b)`。
// 5) `astnav.GetStartOfNode(...)` / `astnav.FindPrecedingToken(...)` → `using static TypeScript.AstNav.AstNav;` 后裸名
//    （`AstNav` 既是命名空间又是静态类，限定名会解析成命名空间 —— 见 artifacts/ls-g1-helpers.md §4.1）。
// 6) `token.Parent.Kind`（Go 假定非 nil）→ `token.Parent!.Kind`；`fragment.OpeningFragment` 在 C# 是 `Node?` ⇒ 补 `!`；
//    `x.AsJsxFragment()` / `x.AsJsxElement()` 等 C# 返回**可空**（`this as T`）⇒ 按 Go 类型断言成功的语义补 `!`。
// 7) `debug.Assert(cond, msg)` → `Debug.Assert(cond, msg)`（`TypeScript.Debug`；禁 `using System.Diagnostics;`）。
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

public sealed partial class LanguageService
{
    /// <summary>
    /// Go: <c>var jsxTagWordPattern = new("[a-zA-Z0-9:\\-\\._$]*")</c>（linkedediting.go:16）：
    /// 允许客户端匹配比合法标签名更多的内容，以便在输入过程中/标签名不完整时也能做链接编辑。
    /// </summary>
    internal const string JsxTagWordPattern = "[a-zA-Z0-9:\\-\\._$]*";

    /// <summary>
    /// Go: <c>func (l *LanguageService) ProvideLinkedEditingRange(ctx context.Context, params *lsproto.LinkedEditingRangeParams) (lsproto.LinkedEditingRangeResponse, error)</c>
    /// （linkedediting.go:18-116）。
    /// </summary>
    public (LinkedEditingRangesOrNull Response, Exception? Error) ProvideLinkedEditingRange(Context ctx, LinkedEditingRangeParams @params)
    {
        var (_, sourceFile) = GetProgramAndFile(@params.TextDocument.Uri);
        var positions = _converters!.FromLSPPositionForSourceFile(sourceFile, @params.Position, Feature.LinkedEditing);
        if (positions.Length != 1 || !positions[0].Fidelity.IsExact())
        {
            return (new LinkedEditingRangesOrNull(), null);
        }

        sourceFile = positions[0].Script;
        var position = positions[0].Position;
        var token = FindPrecedingToken(sourceFile, position);

        if (token is null || token.Parent!.Kind == Kind.SourceFile)
        {
            return (new LinkedEditingRangesOrNull(), null);
        }

        if (IsJsxFragment(token.Parent.Parent!))
        {
            var fragment = token.Parent.Parent!.AsJsxFragment()!;
            var openFragment = fragment.OpeningFragment!;
            var closeFragment = fragment.ClosingFragment!;
            if ((openFragment.Flags & NodeFlags.ThisNodeOrAnySubNodesHasError) != 0 || (closeFragment.Flags & NodeFlags.ThisNodeOrAnySubNodesHasError) != 0)
            {
                return (new LinkedEditingRangesOrNull(), null);
            }

            var openPos = new TextPos(GetStartOfNode(openFragment, sourceFile, false) + "<".Length);
            var closePos = new TextPos(GetStartOfNode(closeFragment, sourceFile, false) + "</".Length);

            // only allows linked editing right after opening bracket: <| ></| >
            if (position != openPos && position != closePos)
            {
                return (new LinkedEditingRangesOrNull(), null);
            }

            var (openLineChar, openFidelity) = _converters.ToLSPPositionForFeature(sourceFile, openPos, Feature.LinkedEditing);
            var (closeLineChar, closeFidelity) = _converters.ToLSPPositionForFeature(sourceFile, closePos, Feature.LinkedEditing);
            if (!openFidelity.IsExact() || !closeFidelity.IsExact())
            {
                return (new LinkedEditingRangesOrNull(), null);
            }

            return (new LinkedEditingRangesOrNull
            {
                LinkedEditingRanges = new LinkedEditingRanges
                {
                    Ranges =
                    [
                        new Range { Start = openLineChar, End = openLineChar }, // only return start position for opening tag since the length of a fragment is always 3 and it is unlikely user will type in the middle of a fragment tag
                        new Range { Start = closeLineChar, End = closeLineChar },
                    ],
                    WordPattern = JsxTagWordPattern,
                },
            }, null);
        }
        else
        {
            // determines if the cursor is in an element tag
            var tag = FindAncestor(token.Parent, n => IsJsxOpeningElement(n) || IsJsxClosingElement(n));
            if (tag is null)
            {
                return (new LinkedEditingRangesOrNull(), null);
            }

            Debug.Assert(IsJsxOpeningElement(tag) || IsJsxClosingElement(tag), "tag should be opening or closing element");

            var jsxElement = tag.Parent!.AsJsxElement()!;
            var openTag = jsxElement.OpeningElement!;
            var closeTag = jsxElement.ClosingElement!;

            var openTagNameStart = GetStartOfNode(openTag.TagName()!, sourceFile, false);
            var openTagNameEnd = openTag.TagName()!.End();
            var closeTagNameStart = GetStartOfNode(closeTag.TagName()!, sourceFile, false);
            var closeTagNameEnd = closeTag.TagName()!.End();
            // do not return linked cursors if tags are not well-formed
            if (openTagNameStart == GetStartOfNode(openTag, sourceFile, false) || closeTagNameStart == GetStartOfNode(closeTag, sourceFile, false) ||
                openTagNameEnd == openTag.End() || closeTagNameEnd == closeTag.End())
            {
                return (new LinkedEditingRangesOrNull(), null);
            }

            // only return linked cursors if the cursor is within a tag name
            if (!(openTagNameStart <= position && position <= openTagNameEnd || closeTagNameStart <= position && position <= closeTagNameEnd))
            {
                return (new LinkedEditingRangesOrNull(), null);
            }

            // only return linked cursors if text in both tags is identical
            var openingTagText = GetTextOfNode(openTag.TagName()!);
            if (openingTagText != GetTextOfNode(closeTag.TagName()!))
            {
                return (new LinkedEditingRangesOrNull(), null);
            }

            var (openRange, openFidelity) = _converters.ToLSPRangeForFeature(sourceFile, new TextRange(openTagNameStart, openTagNameEnd), Feature.LinkedEditing);
            var (closeRange, closeFidelity) = _converters.ToLSPRangeForFeature(sourceFile, new TextRange(closeTagNameStart, closeTagNameEnd), Feature.LinkedEditing);
            if (!openFidelity.IsExact() || !closeFidelity.IsExact())
            {
                return (new LinkedEditingRangesOrNull(), null);
            }

            return (new LinkedEditingRangesOrNull
            {
                LinkedEditingRanges = new LinkedEditingRanges
                {
                    Ranges =
                    [
                        openRange,
                        closeRange,
                    ],
                    WordPattern = JsxTagWordPattern,
                },
            }, null);
        }
    }
}

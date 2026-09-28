// ported from: internal/ls/autoinsert.go:1-105
//
// 与 Go 的偏离（逐条说明）：
// 1) `([]*X, error)` / `lsproto.VSOnAutoInsertResponse` 的 `(响应, error)` 形态 → 本仓约定
//    `(VSOnAutoInsertResponseItemOrNull Response, Exception? Error)`（Go 的 `type VSOnAutoInsertResponse =
//    VSOnAutoInsertResponseItemOrNull` 是类型别名 ⇒ C# 直接用底层 union 类型，见 Lsp.Generated.Methods.cs:741）。
//    空的 `lsproto.VSOnAutoInsertResponse{}` → `new VSOnAutoInsertResponseItemOrNull()`（先例：`Ls/LinkedEditing.cs`）。
// 2) `ast.IsJsxOpeningElement(token.Parent)` / `ast.IsJsxFragment(parent)`：Go 的生成版走 `node.Kind`，传 nil 会 panic；
//    C# 的生成版形参是非空 `Node` ⇒ 写成 `x is { } p && IsXxx(p)`（对可达输入语义相同，且不会 NRE）。
// 3) `element.AsJsxElement().OpeningElement`：C# 的 `AsJsxElement()` 返回 `JsxElement?`、`OpeningElement` 是 `Node?`
//    （Go 里分别是类型断言与 `*JsxOpeningElement`）⇒ 补 `!`（G1 约定的"类型断言语义"），再 `AsJsxOpeningElement()!`
//    取 `TagName` 字段（C# 里是字段，不是 Go 的 `TagName()` 方法）。
// 4) `node.ClosingElement.TagName()`：Go 的 ClosingElement 是 `*JsxClosingElement`，C# 是 `Node?` ⇒ `!` + `AsJsxClosingElement()!`。
// 5) `escapeSnippetText` 是 `completions.go:3734` 的成员，按计划由 G8 落成 `Ls.EscapeSnippetText`（**本文件不重复声明**）。
// 6) `scanner.GetTextOfNode` 是 `Scanner` 静态类的成员 ⇒ `using static TypeScript.Scanner.Scanner;` 后裸名传方法组。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Scanner.Scanner;
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Ls;

public sealed partial class LanguageService
{
    /// <summary>
    /// Go: <c>func (l *LanguageService) ProvideOnAutoInsert(ctx context.Context, params *lsproto.VSOnAutoInsertParams) (lsproto.VSOnAutoInsertResponse, error)</c>
    /// （autoinsert.go:14-64）。
    /// </summary>
    public (VSOnAutoInsertResponseItemOrNull Response, Exception? Error) ProvideOnAutoInsert(Context ctx, VSOnAutoInsertParams @params)
    {
        if (UserPreferences().EnableAutoClosingTags.IsFalse())
        {
            return (new VSOnAutoInsertResponseItemOrNull(), null);
        }

        if (@params.VSCh != ">")
        {
            return (new VSOnAutoInsertResponseItemOrNull(), null);
        }

        var (_, sourceFile) = GetProgramAndFile(@params.VSTextDocument.Uri);
        var positions = _converters!.FromLSPPositionForSourceFile(sourceFile, @params.VSPosition, Feature.AutoInsert);
        if (positions.Length != 1 || !positions[0].Fidelity.IsExact())
        {
            return (new VSOnAutoInsertResponseItemOrNull(), null);
        }

        sourceFile = positions[0].Script;
        var position = positions[0].Position;

        var token = FindPrecedingToken(sourceFile, position);
        if (token is null)
        {
            return (new VSOnAutoInsertResponseItemOrNull(), null);
        }

        var closingText = "";
        Node? element;
        if (token.Kind == Kind.GreaterThanToken && token.Parent is { } parent && IsJsxOpeningElement(parent))
        {
            element = parent.Parent;
        }
        else if (IsJsxText(token) && token.Parent is { } parent2 && IsJsxElement(parent2))
        {
            element = parent2;
        }
        else
        {
            element = null;
        }

        if (element is not null && IsUnclosedTag(element.AsJsxElement()!))
        {
            var tagNameNode = element.AsJsxElement()!.OpeningElement!.AsJsxOpeningElement()!.TagName;
            // Slight divergence from Strada - we don't use the verbatim text from the opening tag.
            closingText = "</" + EntityNameToString(tagNameNode, GetTextOfNode) + ">";
        }
        else
        {
            Node? fragment;
            if (token.Kind == Kind.GreaterThanToken && token.Parent is { } parent3 && IsJsxOpeningFragment(parent3))
            {
                fragment = parent3.Parent;
            }
            else if (IsJsxText(token) && token.Parent is { } parent4 && IsJsxFragment(parent4))
            {
                fragment = parent4;
            }
            else
            {
                fragment = null;
            }

            if (fragment is not null && IsUnclosedFragment(fragment.AsJsxFragment()!))
            {
                closingText = "</>";
            }
        }

        if (closingText == "")
        {
            return (new VSOnAutoInsertResponseItemOrNull(), null);
        }

        return (new VSOnAutoInsertResponseItemOrNull
        {
            VSOnAutoInsertResponseItem = new VSOnAutoInsertResponseItem
            {
                VSTextEditFormat = InsertTextFormat.Snippet,
                VSTextEdit = new TextEdit
                {
                    Range = new Range { Start = @params.VSPosition, End = @params.VSPosition },
                    // Tag names can contain `$` (valid JSX identifier characters), so
                    // escape the closing text to avoid being interpreted as a snippet
                    // placeholder/variable.
                    NewText = "$0" + Ls.EscapeSnippetText(closingText),
                },
            },
        }, null);
    }

    /// <summary>Go: <c>func isUnclosedTag(node *ast.JsxElement) bool</c>（autoinsert.go:66-82）。</summary>
    private static bool IsUnclosedTag(JsxElement node)
    {
        var openingElement = node.OpeningElement!.AsJsxOpeningElement()!;
        var closingElement = node.ClosingElement!.AsJsxClosingElement()!;
        if (!TagNamesAreEquivalent(openingElement.TagName, closingElement.TagName))
        {
            return true;
        }

        var parent = node.Parent;
        if (parent is { } p && IsJsxElement(p))
        {
            var parentElement = p.AsJsxElement()!;
            return TagNamesAreEquivalent(openingElement.TagName, parentElement.OpeningElement!.AsJsxOpeningElement()!.TagName) && IsUnclosedTag(parentElement);
        }

        return false;
    }

    /// <summary>Go: <c>func isUnclosedFragment(node *ast.JsxFragment) bool</c>（autoinsert.go:84-95）。</summary>
    private static bool IsUnclosedFragment(JsxFragment node)
    {
        var closingFragment = node.ClosingFragment!;
        if ((closingFragment.Flags & NodeFlags.ThisNodeHasError) != 0)
        {
            return true;
        }

        var parent = node.Parent;
        if (parent is { } p && IsJsxFragment(p) && IsUnclosedFragment(p.AsJsxFragment()!))
        {
            return true;
        }

        return false;
    }
}

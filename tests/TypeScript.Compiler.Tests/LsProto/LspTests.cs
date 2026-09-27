// ported from: internal/lsp/lsproto/lsp_test.go
//
// C# 映射（与 docs/architecture.md §3.3 的命名一致）：
//   * Go 的 `new(X)`（*X）→ C# 的 `X?`，本测试里就是 `new StringOrInlayHintLabelParts { … }` 这种对象初始化器；
//   * Go 的 `assert.DeepEqual` → xunit 的 `Assert.Equivalent`（同类型对象的结构比较）；
//   * Go 的 `json.Unmarshal(data, &v)` → `UnmarshalNew<T>(data)`（契约文档 §Json/Decoder 的备注）。
using System.Text;
using Xunit;

using TypeScript.LsProto;
using static TypeScript.Json.Json;
// `TypeScript.LsProto.Range` 与 implicit usings 的 `System.Range` 撞名（见 SemVer 测试的 `using Version = …`）。
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Tests;

public class LspTests
{
    [Fact]
    public void TestUnmarshalCompletionItem()
    {
        const string message = """
            {
                "label": "pageXOffset",
                "insertTextFormat": 1,
                "textEdit": {
                    "newText": "pageXOffset",
                    "insert": {
                        "start": {
                            "line": 4,
                            "character": 0
                        },
                        "end": {
                            "line": 4,
                            "character": 4
                        }
                    },
                    "replace": {
                        "start": {
                            "line": 4,
                            "character": 0
                        },
                        "end": {
                            "line": 4,
                            "character": 4
                        }
                    }
                },
                "kind": 6,
                "sortText": "15",
                "commitCharacters": [
                    ".",
                    ",",
                    ";"
                ]
            }
            """;

        var result = UnmarshalNew<CompletionItem>(Encoding.UTF8.GetBytes(message));

        var expected = new CompletionItem
        {
            Label = "pageXOffset",
            InsertTextFormat = InsertTextFormat.PlainText,
            TextEdit = new TextEditOrInsertReplaceEdit
            {
                InsertReplaceEdit = new InsertReplaceEdit
                {
                    NewText = "pageXOffset",
                    Insert = new Range
                    {
                        Start = new Position { Line = 4, Character = 0 },
                        End = new Position { Line = 4, Character = 4 },
                    },
                    Replace = new Range
                    {
                        Start = new Position { Line = 4, Character = 0 },
                        End = new Position { Line = 4, Character = 4 },
                    },
                },
            },
            Kind = CompletionItemKind.Variable,
            SortText = "15",
            CommitCharacters = [".", ",", ";"],
        };

        Assert.Equivalent(expected, result);

        // Go 的 DeepEqual 是数据级比较；C# 的 Assert.Equivalent 走反射，这里再钉一遍嵌套联合体
        // （TextEditOrInsertReplaceEdit 选了 InsertReplaceEdit 臂）与几个字段的具体值。
        Assert.Equal("pageXOffset", result.Label);
        Assert.Equal((InsertTextFormat?)InsertTextFormat.PlainText, result.InsertTextFormat);
        Assert.Equal((CompletionItemKind?)CompletionItemKind.Variable, result.Kind);
        Assert.Equal("15", result.SortText);
        Assert.Equal(new[] { ".", ",", ";" }, result.CommitCharacters);
        Assert.NotNull(result.TextEdit);
        Assert.Null(result.TextEdit!.TextEdit);
        Assert.NotNull(result.TextEdit.InsertReplaceEdit);
        var edit = result.TextEdit.InsertReplaceEdit!;
        Assert.Equal("pageXOffset", edit.NewText);
        Assert.Equal(4u, edit.Insert.Start.Line);
        Assert.Equal(0u, edit.Insert.Start.Character);
        Assert.Equal(4u, edit.Insert.End.Line);
        Assert.Equal(4u, edit.Insert.End.Character);
        Assert.Equal(4u, edit.Replace.Start.Line);
        Assert.Equal(0u, edit.Replace.Start.Character);
        Assert.Equal(4u, edit.Replace.End.Line);
        Assert.Equal(4u, edit.Replace.End.Character);
    }
}

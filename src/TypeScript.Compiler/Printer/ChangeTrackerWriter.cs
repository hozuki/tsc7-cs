// ported from: internal/printer/changetrackerwriter.go
//
// 偏离 Go 的地方：
// 1) Go 用**值内嵌** `textWriter` 复用实现：C# 里 `TextWriter` 是 internal（若让公开的 `ChangeTrackerWriter`
//    继承它，会违反"基类不能比派生类更不可见"），所以按 Go 自己的写法落成**组合**（Go 的每个方法本来也都委派给
//    `ct.textWriter.X`）。Go 上被提升的 `Grow` 显式补一个转发方法（printer.go:5099 会做结构探测）。
// 2) Go 的 `triviaPositionKey`（结构化接口，`*ast.Node` / `*ast.NodeList` / `*ast.ModifierList` 都满足）在 C#
//    里没有结构类型 → `Dictionary<object, int>` + `ReferenceEqualityComparer`（键都是引用类型，不装箱；Go 的
//    接口键也是按 (动态类型, 指针) 比较，等价）。
// 3) `setLastNonTriviaPosition` 里 Go 用 `utf8.DecodeLastRuneInString` 逐码点回退空白；C# 的 string 是 UTF-16，
//   空白类码点全在 BMP，所以逐 `char` 回退判定等价（docs §3.1）。
// 4) Go 的 `panic("if nodes is nil, visited should not be nil")` → `throw new DebugFailureException(...)`
//    （docs §3.2 允许 panic → Debug.Fail/throw；这里用 throw 是因为其后要用 `nodes` 的非空收窄，
//    TypeScript.Debug.Fail 没标 [DoesNotReturn]，写成 Debug.Fail 会让后续实参报 CS8604）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using AstNodeFactory = TypeScript.Ast.NodeFactory;

namespace TypeScript.Printer;

public sealed class ChangeTrackerWriter : EmitTextWriter, IGrowableTextWriter
{
    private readonly TextWriter _textWriter;
    private int _lastNonTriviaPosition;
    private readonly Dictionary<object, int> _pos = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> _end = new(ReferenceEqualityComparer.Instance);

    public ChangeTrackerWriter(string newline, int indentSize)
    {
        // TODO: Callers passing -1 should pass actual indent options once indent-related formatting is ported.
        if (indentSize < 0)
        {
            indentSize = Printer.GetDefaultIndentSize();
        }
        _textWriter = new TextWriter(newline, indentSize);
        _lastNonTriviaPosition = 0;
        _textWriter.Clear();
    }

    /// <summary>Go: <c>textWriter.Grow</c> 的转发（Go 里由内嵌提升而来）。</summary>
    public void Grow(int n) => _textWriter.Grow(n);

    public PrintHandlers GetPrintHandlers() => new()
    {
        OnBeforeEmitNode = nodeOpt =>
        {
            if (nodeOpt is not null)
            {
                SetPos(nodeOpt);
            }
        },
        OnAfterEmitNode = nodeOpt =>
        {
            if (nodeOpt is not null)
            {
                SetEnd(nodeOpt);
            }
        },
        OnBeforeEmitNodeList = nodesOpt =>
        {
            if (nodesOpt is not null)
            {
                SetPos(nodesOpt);
            }
        },
        OnAfterEmitNodeList = nodesOpt =>
        {
            if (nodesOpt is not null)
            {
                SetEnd(nodesOpt);
            }
        },
        OnBeforeEmitToken = nodeOpt =>
        {
            if (nodeOpt is not null)
            {
                SetPos(nodeOpt);
            }
        },
        OnAfterEmitToken = nodeOpt =>
        {
            if (nodeOpt is not null)
            {
                SetEnd(nodeOpt);
            }
        },
    };

    private void SetPos(object node) => _pos[node] = _lastNonTriviaPosition;

    private void SetEnd(object node) => _end[node] = _lastNonTriviaPosition;

    private int GetPos(object node) => _pos.TryGetValue(node, out var value) ? value : 0;

    private int GetEnd(object node) => _end.TryGetValue(node, out var value) ? value : 0;

    private void SetLastNonTriviaPosition(string s, bool force)
    {
        if (force || SkipTrivia(s, 0) != s.Length)
        {
            _lastNonTriviaPosition = _textWriter.GetTextPos();
            // trim trailing whitespaces
            var pos = s.Length;
            while (pos > 0)
            {
                var ch = s[pos - 1];
                if (IsWhiteSpaceLike(ch))
                {
                    pos--;
                }
                else
                {
                    break;
                }
            }
            _lastNonTriviaPosition -= s.Length - pos;
        }
    }

    public Node AssignPositionsToNode(Node node, AstNodeFactory factory)
    {
        NodeVisitor? visitor = null;
        visitor = new NodeVisitor(
            n => AssignPositionsToNodeWorker(n, visitor!),
            factory,
            new NodeVisitorHooks
            {
                VisitNode = AssignPositionsToNodeWorker,
                VisitNodes = AssignPositionsToNodeArray,
                VisitToken = AssignPositionsToNodeWorker,
                VisitModifiers = (modifiers, v) =>
                {
                    // Go: changeTrackerWriter.go:116-124 的 VisitModifiers 有 nil 守卫：
                    //     `if modifiers != nil { … }; return modifiers`
                    // 钩子收到 nil 时（Visitor.VisitModifiersWithHooks 会把 null 传进来，见 Ast/Visitor.cs:370）
                    // 必须原样返回 nil，否则 assignPositionsToNodeArray(nil) 返回 null → newNodeList.Nodes 抛 NRE。
                    if (modifiers is null)
                    {
                        return modifiers;
                    }

                    // Go: ct.assignPositionsToNodeArray(&modifiers.NodeList, v) —— C# 的 ModifierList 本身就是 NodeList（派生）
                    var newNodeList = AssignPositionsToNodeArray(modifiers, v);
                    // Return a new ModifierList so that VisitEachChild/Update detects the
                    // change and creates a new node with reassigned child positions.
                    return factory.NewModifierList(newNodeList!.Nodes);
                },
            });
        return AssignPositionsToNodeWorker(node, visitor)!;
    }

    private Node? AssignPositionsToNodeWorker(Node node, NodeVisitor v)
    {
        if (node is null)
        {
            return node;
        }
        var visited = node.VisitEachChild(v)!;
        // Assigning positions must not mutate the caller's node: it may be printed again (a change in a
        // content-mapped file is formatted once per virtual projection of its insertion point), and a node
        // that has acquired positions is printed by reading text back out of the source file. VisitEachChild
        // returns a fresh node only when a child changed, so clone whenever it hands back the input.
        var newNode = visited;
        if (ReferenceEquals(visited, node))
        {
            newNode = visited.Clone(v.Factory)!;
        }
        newNode.ForEachChild(child =>
        {
            child.Parent = newNode;
            return true;
        });
        newNode.Loc = NewTextRange(GetPos(node), GetEnd(node));
        return newNode;
    }

    private NodeList? AssignPositionsToNodeArray(NodeList nodes, NodeVisitor v)
    {
        var visited = v.VisitNodes(nodes);
        if (visited is null)
        {
            return visited;
        }
        if (nodes is null)
        {
            // Debug.assert(nodes);
            throw new DebugFailureException("if nodes is nil, visited should not be nil");
        }
        // clone nodearray if necessary
        var nodeArray = visited;
        if (ReferenceEquals(visited, nodes))
        {
            nodeArray = visited.Clone(v.Factory)!;
        }

        nodeArray.Loc = NewTextRange(GetPos(nodes), GetEnd(nodes));
        return nodeArray;
    }

    public void Write(string text)
    {
        _textWriter.Write(text);
        SetLastNonTriviaPosition(text, false);
    }

    public void WriteTrailingSemicolon(string text)
    {
        _textWriter.WriteTrailingSemicolon(text);
        SetLastNonTriviaPosition(text, false);
    }

    public void WriteComment(string text) => _textWriter.WriteComment(text);

    public void WriteKeyword(string text)
    {
        _textWriter.WriteKeyword(text);
        SetLastNonTriviaPosition(text, false);
    }

    public void WriteOperator(string text)
    {
        _textWriter.WriteOperator(text);
        SetLastNonTriviaPosition(text, false);
    }

    public void WritePunctuation(string text)
    {
        _textWriter.WritePunctuation(text);
        SetLastNonTriviaPosition(text, false);
    }

    public void WriteSpace(string text)
    {
        _textWriter.WriteSpace(text);
        SetLastNonTriviaPosition(text, false);
    }

    public void WriteStringLiteral(string text)
    {
        _textWriter.WriteStringLiteral(text);
        SetLastNonTriviaPosition(text, false);
    }

    public void WriteParameter(string text)
    {
        _textWriter.WriteParameter(text);
        SetLastNonTriviaPosition(text, false);
    }

    public void WriteProperty(string text)
    {
        _textWriter.WriteProperty(text);
        SetLastNonTriviaPosition(text, false);
    }

    public void WriteSymbol(string text, Symbol? symbol)
    {
        _textWriter.WriteSymbol(text, symbol);
        SetLastNonTriviaPosition(text, false);
    }

    public void WriteLine() => _textWriter.WriteLine();

    public void WriteLineForce(bool force) => _textWriter.WriteLineForce(force);

    public void IncreaseIndent() => _textWriter.IncreaseIndent();

    public void DecreaseIndent() => _textWriter.DecreaseIndent();

    public void Clear()
    {
        _textWriter.Clear();
        _lastNonTriviaPosition = 0;
    }

    public string String() => _textWriter.String();

    public void RawWrite(string s)
    {
        _textWriter.RawWrite(s);
        SetLastNonTriviaPosition(s, false);
    }

    public void WriteLiteral(string s)
    {
        _textWriter.WriteLiteral(s);
        SetLastNonTriviaPosition(s, true);
    }

    public int GetTextPos() => _textWriter.GetTextPos();

    public int GetLine() => _textWriter.GetLine();

    public UTF16Offset GetColumn() => _textWriter.GetColumn();

    public int GetIndent() => _textWriter.GetIndent();

    public bool IsAtStartOfLine() => _textWriter.IsAtStartOfLine();

    public bool HasTrailingComment() => _textWriter.HasTrailingComment();

    public bool HasTrailingWhitespace() => _textWriter.HasTrailingWhitespace();
}

public sealed partial class Printer
{
    public static ChangeTrackerWriter NewChangeTrackerWriter(string newline, int indentSize) =>
        new(newline, indentSize);
}

// C# 侧新增：Go 的 ast.go:2217-2248（`ImportAttributesNode.GetResolutionModeOverride`）。
//
// 为什么是新文件：Go 里 `ImportAttributesNode` 只是 `type ImportAttributesNode = Node` 的**别名**，所以那句
// `func (node *ImportAttributesNode) …` 实际是声明在 `*Node` 上的方法、依赖 `node == nil` 的可达分支，并与
// 生成的节点数据混在 ast.go 里。C# 侧：
//   * 生成物 Nodes.Generated.cs 不允许手工编辑，手写成员按 SubtreeFactsOverrides.cs 的既有做法放 partial / 独立文件；
//   * `Node` 上的方法集在 Node.cs，但这个方法只服务 ImportAttributes 的调用点，放这里更好找；C# 没有类型别名，
//     所以用**扩展方法**（接收者可空）复刻 Go 的"别名接收者 + nil 接收者安全"：调用点 `attributes.GetResolutionModeOverride(null)`
//     与 Go 逐字一致，`null` 接收者返回 `(ResolutionModeNone, false)`（Go 的 `if node == nil` 分支）。
// 其余差异：
//   - `grammarErrorOnNode func(node *Node, message *diagnostics.Message, args ...any) bool` → C# 具名委托
//     `GrammarErrorOnNode`（C# 的参数类型必须是具名类型；可空表示 Go 的 nil 回调，调用点写 `?.Invoke(...)`）。
//   - `(core.ResolutionMode, bool)` → `(ModuleKind, bool)`（C# 没有 `ResolutionMode` 别名，见 Core/CompilerOptions.cs）。
//   - `core.Find` → `Find`（Core.cs，未命中返回 default = null，与 Go 的 nil 一致）。
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

namespace TypeScript.Ast;

/// <summary>Go: <c>func(node *Node, message *diagnostics.Message, args ...any) bool</c>（语法错误上报回调；nil = 不上报）。</summary>
public delegate bool GrammarErrorOnNode(Node node, Message message, params object?[] args);

public static partial class Ast
{
    /// <summary>
    /// Go: <c>func (node *ImportAttributesNode) GetResolutionModeOverride(grammarErrorOnNode func(node *Node, message *diagnostics.Message, args ...any) bool) (core.ResolutionMode, bool)</c>。
    /// 从 `resolution-mode` 属性取解析模式覆盖；属性缺失、值不是字符串字面量、或值不是 import/require 时返回 false
    /// （最后一种情况会调用 grammarErrorOnNode 报语法错误）。
    /// </summary>
    public static (ModuleKind ResolutionMode, bool Ok) GetResolutionModeOverride(
        this Node? node,
        GrammarErrorOnNode? grammarErrorOnNode)
    {
        if (node is null)
        {
            return (ResolutionModeNone, false);
        }

        var attributes = node.AsImportAttributes()!.Attributes;
        Node? attribute = null;
        if (attributes?.Nodes is not null)
        {
            attribute = Find(attributes.Nodes, candidate => candidate.Name()!.Text() == "resolution-mode");
        }

        if (attribute is null)
        {
            return (ResolutionModeNone, false);
        }

        var elem = attribute.AsImportAttribute()!;
        var value = elem.Value;
        if (!IsStringLiteralLike(value))
        {
            return (ResolutionModeNone, false);
        }

        var text = value!.Text();
        if (text != "import" && text != "require")
        {
            grammarErrorOnNode?.Invoke(value, X_resolution_mode_should_be_either_require_or_import);
            return (ResolutionModeNone, false);
        }

        return text == "import" ? (ResolutionModeESM, true) : (ModuleKind.CommonJS, true);
    }
}

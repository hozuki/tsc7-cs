// ported from: internal/transformers/modifiervisitor.go（28 行，全部）
//
// 与 Go 的偏离（C# 语言特性）：
// - Go 的 `modifierVisitor` 结构体嵌入 `Transformer` → C# 继承（见 transformer.go 文件头）。
// - Go 的 `tx.visitor.VisitModifiers(...)`（**字段**访问）→ C# `tx.Visitor().VisitModifiers(...)`（`_visitor` 是 internal 字段，
//   但按命名规则/可读性走公开访问器；语义相同）。
// - 未导出类型 `modifierVisitor` 按 C# 习惯命名 → `ModifierVisitor`。
using TypeScript.Ast;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;

namespace TypeScript.Transformers;

/// <summary>Go: <c>type modifierVisitor struct { Transformer; AllowedModifiers ast.ModifierFlags }</c>。</summary>
internal sealed class ModifierVisitor : Transformer
{
    /// <summary>Go 导出字段 <c>AllowedModifiers</c> → 原名 PascalCase、不加前缀。</summary>
    internal ModifierFlags AllowedModifiers;

    /// <summary>Go: <c>func (v *modifierVisitor) visit(node *ast.Node) *ast.Node</c>（返回 nil = 丢弃该修饰符）。</summary>
    internal Node? Visit(Node node)
    {
        var flags = ModifierToFlag(node.Kind);
        if (flags != ModifierFlags.None && (flags & AllowedModifiers) == 0)
        {
            return null;
        }
        return node;
    }
}

public static partial class Transformers
{
    /// <summary>Go: <c>func ExtractModifiers(emitContext *printer.EmitContext, modifiers *ast.ModifierList, allowed ast.ModifierFlags) *ast.ModifierList</c>。</summary>
    public static ModifierList? ExtractModifiers(EmitContext emitContext, ModifierList? modifiers, ModifierFlags allowed)
    {
        if (modifiers is null)
        {
            return null;
        }
        var tx = new ModifierVisitor { AllowedModifiers = allowed };
        tx.NewTransformer(tx.Visit, emitContext);
        return tx.Visitor().VisitModifiers(modifiers);
    }
}

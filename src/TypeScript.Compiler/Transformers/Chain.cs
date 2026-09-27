// ported from: internal/transformers/chain.go（62 行，全部）
//
// 与 Go 的偏离（C# 语言特性）：
// - Go 的 `type TransformerFactory = func(opt *TransformOptions) *Transformer`（**类型别名**）→ C# 具名委托
//   `TransformerFactory`（C# 没有函数类型别名，委托是唯一能出现在签名里的具名函数类型）。
// - Go 的 `chainedTransformer` 结构体**嵌入** `Transformer` → C# `ChainedTransformer : Transformer`（继承，见 transformer.go 文件头）。
// - `panic(...)` → `throw new DebugFailureException(...)`（Go 的 panic 终止控制流，C# 侧要保证返回值确定）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Printer;

namespace TypeScript.Transformers;

/// <summary>Go: <c>type chainedTransformer struct { Transformer; components []*Transformer }</c>（未导出类型按 C# 习惯命名）。</summary>
internal sealed class ChainedTransformer : Transformer
{
    /// <summary>Go 未导出字段 <c>components</c> → <c>_components</c>。</summary>
    internal Transformer[] _components = [];

    /// <summary>Go: <c>func (ch *chainedTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node Visit(Node node)
    {
        if (node.Kind != Kind.SourceFile)
        {
            // Go: panic("Chained transform passed non-sourcefile initial node")
            throw new DebugFailureException("Chained transform passed non-sourcefile initial node");
        }
        var result = node.AsSourceFile()!;
        foreach (var t in _components)
        {
            result = t.TransformSourceFile(result);
        }
        return result; // Go: result.AsNode()
    }
}

/// <summary>Go: <c>type TransformOptions struct</c>（导出字段原名 PascalCase）。</summary>
public sealed class TransformOptions
{
    public EmitContext? Context;
    public CompilerOptions? CompilerOptions;
    public ReferenceResolver? Resolver;
    public EmitResolver? EmitResolver;
    public Func<HasFileName, ModuleKind>? GetEmitModuleFormatOfFile;
}

/// <summary>Go: <c>type TransformerFactory = func(opt *TransformOptions) *Transformer</c>（返回值可 nil，表示"该变换器不参与此次 emit"）。</summary>
public delegate Transformer? TransformerFactory(TransformOptions opt);

/// <summary>Go 包级函数（package transformers）→ 与包同名的静态类。</summary>
public static partial class Transformers
{
    /// <summary>
    /// Chains transforms in left-to-right order, running them one at a time in order (as opposed to interleaved at each node)
    /// - the resulting combined transform only operates on SourceFile nodes<br/>
    /// Go: <c>func Chain(transforms ...TransformerFactory) TransformerFactory</c>。
    /// </summary>
    public static TransformerFactory Chain(params TransformerFactory[] transforms)
    {
        if (transforms.Length < 2)
        {
            if (transforms.Length == 0)
            {
                // Go: panic("Expected some number of transforms to chain, but got none")
                throw new DebugFailureException("Expected some number of transforms to chain, but got none");
            }
            return transforms[0];
        }
        return opt =>
        {
            // Go: constructed := make([]*Transformer, 0, len(transforms)) + append
            var constructed = new List<Transformer>(transforms.Length);
            foreach (var t in transforms)
            {
                // TODO: flatten nested chains?
                if (t(opt) is { } result)
                {
                    constructed.Add(result);
                }
            }
            switch (constructed.Count)
            {
                case 0:
                    return null;
                case 1:
                    return constructed[0];
            }
            var ch = new ChainedTransformer { _components = constructed.ToArray() };
            return ch.NewTransformer(ch.Visit, opt.Context);
        };
    }
}

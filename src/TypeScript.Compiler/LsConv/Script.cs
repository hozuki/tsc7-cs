// ported from: tsc/internal/ls/lsconv/converters.go:44-50（Script 接口；本文件只放接口与 SourceFile 的
// 显式实现，转换逻辑在 Converters.cs）。
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `*ast.SourceFile` 是**结构化**满足 `lsconv.Script` 的（方法名/签名对上即可）。C# 的接口实现是
//    声明式的，没有结构化满足，所以在**本切片目录内**追加一个 `partial class SourceFile` 声明把接口挂上去
//    （`SourceFile` 本来就是 `public sealed partial class SourceFile : Node, …`，C# 允许 partial 声明追加基接口，
//    基类仍只在主声明里给）。这样 `Converters.ToLSPRange(sourceFile, …)` 等调用点与 Go 逐字对应。
//    SourceFile 上已有的 `FileName()/OriginalFileName()/Text()/SpanMap()/OriginalText()` 都是 public，
//    签名逐一匹配 → 接口成员是**隐式**实现，不需要额外写转发方法。
//    —— 若日后把 `Script` 上移到 `Ast` 命名空间，这个声明应该并进 `Ast/SourceFile.cs` 的基接口列表。
// 2) Go 的接口名 `Script` 是导出名 → C# 接口名照抄（先例：`LsProto.HasTextDocumentURI` 没有加 `I` 前缀）。
//    Go 未导出接口才用 `I` 前缀（docs/architecture.md §2）。
// 3) `SpanMap() *spanmap.SpanMap`（Go 的 nil 指针）→ `SpanMap()?`（C# 的 `SpanMap` 是引用类型，null 即 Go 的 nil）。
//    返回类型写成别名 `SpanMapType`（= `TypeScript.SpanMap.SpanMap`）：本文件 import 了命名空间 `TypeScript.SpanMap`，
//    裸写 `SpanMap` 会先命中命名空间（本仓多次踩过 CS0118）。
// 4) **本文件用块作用域命名空间**（而不是仓库惯例的文件作用域）：一个文件里要放两个命名空间
//    （`TypeScript.LsConv` 的接口 + `TypeScript.Ast` 的 partial 声明），C# 不允许文件作用域与块作用域混用。

using TypeScript.Ast;
using SpanMapType = TypeScript.SpanMap.SpanMap;

namespace TypeScript.LsConv
{
    /// <summary>
    /// Go: <c>type Script interface</c>（converters.go:44-50）。转换器操作的源文本抽象：内容映射过的文件里
    /// <see cref="Text"/> 是映射器的虚拟输出，<see cref="SpanMap"/> 给出虚拟文本回原文的映射
    /// （<see cref="OriginalText"/>）；虚拟区间因此会自动换算成原文坐标（见 <c>Converters.ToLSPRange</c>）。
    /// 普通文件的 <see cref="SpanMap"/> 是 null 且 <see cref="OriginalText"/> 等于 <see cref="Text"/>。
    /// </summary>
    public interface Script
    {
        string FileName();

        string OriginalFileName();

        string Text();

        SpanMapType? SpanMap();

        string OriginalText();
    }
}

namespace TypeScript.Ast
{
    /// <summary>
    /// Go: `*ast.SourceFile` 结构化满足 `lsconv.Script`（见文件头第 1 条）。五个成员由 SourceFile 上已有的
    /// public 方法隐式实现。
    /// </summary>
    public sealed partial class SourceFile : TypeScript.LsConv.Script
    {
    }
}

// ported from: internal/printer/emitflags.go
//
// Go 的 `type EmitFlags uint32` + 两组包级常量 → C# `enum EmitFlags : uint`：成员名 = Go 常量名去掉 `EF`
// 前缀（docs §3.3 "枚举成员名 = Go 常量名去掉类型前缀"；EmitFlags 的类型前缀在 TS/Strada 里就是 `EF`），
// 数值逐位与 Go 一致。第二组常量（EFNone/EFNoSourceMap/EFNoTokenSourceMaps/EFNoComments）是复合位掩码，
// 同样是 EmitFlags 类型的常量 → 同样落成 enum 成员（C# enum 允许成员取复合值）。
using System;

namespace TypeScript.Printer;

/// <summary>Go 的 <c>type EmitFlags uint32</c>：节点/令牌的 emit 定制标志（位掩码）。</summary>
[Flags]
public enum EmitFlags : uint
{
    /// <summary>Go: <c>EFNone</c>。</summary>
    None = 0,

    /// <summary>The contents of this node should be emitted on a single line.</summary>
    SingleLine = 1u << 0,
    /// <summary>The contents of this node should be emitted on multiple lines.</summary>
    MultiLine = 1u << 1,
    /// <summary>Do not emit a leading source map location for this node.</summary>
    NoLeadingSourceMap = 1u << 2,
    /// <summary>Do not emit a trailing source map location for this node.</summary>
    NoTrailingSourceMap = 1u << 3,
    /// <summary>Do not emit source map locations for children of this node.</summary>
    NoNestedSourceMaps = 1u << 4,
    /// <summary>Do not emit leading source map location for token nodes.</summary>
    NoTokenLeadingSourceMaps = 1u << 5,
    /// <summary>Do not emit trailing source map location for token nodes.</summary>
    NoTokenTrailingSourceMaps = 1u << 6,
    /// <summary>Do not emit leading comments for this node.</summary>
    NoLeadingComments = 1u << 7,
    /// <summary>Do not emit trailing comments for this node.</summary>
    NoTrailingComments = 1u << 8,
    /// <summary>Do not emit nested comments for children of this node.</summary>
    NoNestedComments = 1u << 9,
    /// <summary>The Identifier refers to an *unscoped* emit helper (one that is emitted at the top of the file).</summary>
    HelperName = 1u << 10,
    /// <summary>
    /// Ensure an export prefix is added for an identifier that points to an exported declaration with a local name
    /// (see SymbolFlags.ExportHasLocal).
    /// </summary>
    ExportName = 1u << 11,
    /// <summary>Ensure an export prefix is not added for an identifier that points to an exported declaration.</summary>
    LocalName = 1u << 12,
    /// <summary>
    /// Adds an explicit extra indentation level for class and function bodies when printing (used to match old emitter).
    /// </summary>
    Indented = 1u << 13,
    /// <summary>Do not indent the node.</summary>
    NoIndentation = 1u << 14,
    /// <summary>Reuse the existing temp variable scope during emit.</summary>
    ReuseTempVariableScope = 1u << 15,
    /// <summary>
    /// Treat the statement as if it were a prologue directive (NOTE: Prologue directives are *not* transformed).
    /// </summary>
    CustomPrologue = 1u << 16,
    /// <summary>
    /// When synthesizing nodes that lack an original node or textSourceNode, we want to write the text on the node
    /// with ASCII escaping substitutions.
    /// </summary>
    NoAsciiEscaping = 1u << 17,
    /// <summary>This source file has external helpers.</summary>
    ExternalHelpers = 1u << 18,
    /// <summary>Start this node on a new line.</summary>
    StartOnNewLine = 1u << 19,
    /// <summary>Emit CallExpression as an indirect call: `(0, f)()`.</summary>
    IndirectCall = 1u << 20,
    /// <summary>The node was originally an async function body.</summary>
    AsyncFunctionBody = 1u << 21,
    /// <summary>
    /// Do not capture `arguments` for this arrow function. Set on arrows lowered from class static blocks, where
    /// `arguments` is an error; preserves Strada's emit behavior.
    /// </summary>
    NoLexicalArguments = 1u << 22,
    /// <summary>
    /// Indicates static private elements in a file or class should be transformed regardless of --target
    /// (used by esDecorators transform).
    /// </summary>
    TransformPrivateStaticElements = 1u << 23,
    /// <summary>
    /// Do not capture `this` for this node's subtree. Set on relocated static initializers, where `this` is handled
    /// by the class fields transform.
    /// </summary>
    NoLexicalThis = 1u << 24,

    // 第二组包级常量（复合位掩码）

    /// <summary>Go: <c>EFNoSourceMap</c>：不为此节点发出 source map 位置。</summary>
    NoSourceMap = NoLeadingSourceMap | NoTrailingSourceMap,

    /// <summary>Go: <c>EFNoTokenSourceMaps</c>：不为此节点的令牌发出 source map 位置。</summary>
    NoTokenSourceMaps = NoTokenLeadingSourceMaps | NoTokenTrailingSourceMaps,

    /// <summary>Go: <c>EFNoComments</c>：不为此节点发出注释。</summary>
    NoComments = NoLeadingComments | NoTrailingComments,
}

// ported from: internal/ast/ast.go（SourceFile 周边的小数据结构与 pragma 类型；SourceFile 本体见 SourceFile.cs）
//
// 与 Go 的差异（C# 语言特性）：
// - Go 的结构体 → C# class（引用语义；Go 里这些类型基本都按指针传递，例如 `*CommentRange`）。
// - Go 的 `type PragmaKindFlags = uint8` 是类型别名 + 常量组；C# 没有别名，落成 `[Flags] enum PragmaKind : byte`
//   （成员名去 `PragmaKind` 前缀：`PragmaKindTripleSlashXML` → `TripleSlashXML`，数值与 Go 一致）。
// - Go 的 `pattern map[string]...`? 无；`PatternAmbientModule.Pattern` 是 `core.Pattern`（M2 已有）。
// - `SourceFileDataKey[T]`/`sourceFileDataCell[T]`：Go 用泛型 + `sync.Once` + `map[sourceFileDataKey]any`；
//   C# 落成泛型 class + `Lazy<T>`（AOT 安全：不反射），键是 `ulong`（Go 的 sourceFileDataKey）。
// - `SourceFileDataKey.key` 是 Go 的未导出字段 → C# `internal ulong _key`。
using System.Threading;
using TypeScript.Core;

namespace TypeScript.Ast;

/// <summary>Go: <c>type PatternAmbientModule struct { Pattern core.Pattern; Symbol *Symbol }</c>。</summary>
public sealed class PatternAmbientModule
{
    public Pattern Pattern;
    public Symbol? Symbol;
}

/// <summary>Go: <c>type CommentDirectiveKind int32</c>。</summary>
public enum CommentDirectiveKind
{
    Unknown = 0,
    ExpectError = 1,
    Ignore = 2,
}

/// <summary>Go: <c>type CommentDirective struct { Loc core.TextRange; Kind CommentDirectiveKind }</c>。</summary>
public sealed class CommentDirective
{
    public TextRange Loc;
    public CommentDirectiveKind Kind;
}

/// <summary>Go: <c>type SourceFileMetaData struct</c>（package.json 的 type 与目录、以及推导出的模块格式）。</summary>
public sealed class SourceFileMetaData
{
    public string PackageJsonType = "";
    public string PackageJsonDirectory = "";
    public ModuleKind ImpliedNodeFormat; // Go: core.ResolutionMode = ModuleKind 别名
}

/// <summary>
/// Go: <c>type SourceFileDataKey[T any] struct { key sourceFileDataKey; _ [0]T }</c>：
/// 供其它 package 给 SourceFile 挂懒计算数据的键。
/// </summary>
public sealed class SourceFileDataKey<T>
{
    internal readonly ulong _key;

    internal SourceFileDataKey(ulong key) => _key = key;
}

public static partial class Ast
{
    // Go: var sourceFileDataKeyCounter atomic.Uint64（全进程共享，不随 T 分裂）
    private static ulong _sourceFileDataKeyCounter;

    /// <summary>Go: <c>func NewSourceFileDataKey[T any]() *SourceFileDataKey[T]</c>。</summary>
    public static SourceFileDataKey<T> NewSourceFileDataKey<T>() =>
        new(Interlocked.Increment(ref _sourceFileDataKeyCounter));
}

/// <summary>Go: <c>type CheckJsDirective struct { Enabled bool; Range CommentRange }</c>。</summary>
public sealed class CheckJsDirective
{
    public bool Enabled;
    public CommentRange Range = new();
}

/// <summary>Go: <c>type HasFileName interface { FileName() string; Path() tspath.Path }</c>。</summary>
public interface HasFileName
{
    string FileName();

    TSPath.Path Path();
}

/// <summary>Go: <c>type TokenCacheKey struct { parent *Node; loc core.TextRange }</c>（SourceFile 的 token 缓存键）。</summary>
public sealed class TokenCacheKey
{
    public Node? Parent;
    public TextRange Loc;
}

/// <summary>Go: <c>type SourceFileLike interface { Text() string; ECMALineMap() []core.TextPos }</c>。</summary>
public interface SourceFileLike
{
    string Text();

    TextPos[] ECMALineMap();
}

/// <summary>Go: <c>type CommentRange struct { core.TextRange; Kind Kind; HasTrailingNewLine bool }</c>。</summary>
public class CommentRange
{
    public TextRange Range;
    public Kind Kind;
    public bool HasTrailingNewLine;
}

/// <summary>Go: <c>type FileReference struct { core.TextRange; FileName string; ResolutionMode core.ResolutionMode; Preserve bool }</c>。</summary>
public sealed class FileReference
{
    public TextRange Range;
    public string FileName = "";
    public ModuleKind ResolutionMode; // Go: core.ResolutionMode = ModuleKind 别名
    public bool Preserve;
}

/// <summary>Go: <c>type PragmaArgument struct { core.TextRange; Name string; Value string }</c>。</summary>
public sealed class PragmaArgument
{
    public TextRange Range;
    public string Name = "";
    public string Value = "";
}

/// <summary>Go: <c>type Pragma struct { CommentRange; Name string; Args map[string]PragmaArgument }</c>。</summary>
public sealed class Pragma : CommentRange
{
    public string Name = "";
    public Dictionary<string, PragmaArgument>? Args;
}

/// <summary>Go: <c>type PragmaKindFlags = uint8</c> + 常量组。</summary>
[Flags]
public enum PragmaKind : byte
{
    None = 0,
    TripleSlashXML = 1 << 0,
    SingleLine = 1 << 1,
    MultiLine = 1 << 2,
    All = TripleSlashXML | SingleLine | MultiLine,
    Default = All,
}

/// <summary>Go: <c>type PragmaArgumentSpecification struct</c>。</summary>
public sealed class PragmaArgumentSpecification
{
    public string Name = "";
    public bool Optional;
    public bool CaptureSpan;
}

/// <summary>Go: <c>type PragmaSpecification struct { Args []PragmaArgumentSpecification; Kind PragmaKindFlags }</c>。</summary>
public sealed class PragmaSpecification
{
    public PragmaArgumentSpecification[] Args = [];
    public PragmaKind Kind;

    /// <summary>Go: <c>func (spec *PragmaSpecification) IsTripleSlash() bool</c>。</summary>
    public bool IsTripleSlash() => (Kind & PragmaKind.TripleSlashXML) > 0;
}

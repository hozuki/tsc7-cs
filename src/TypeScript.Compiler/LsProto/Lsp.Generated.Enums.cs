// generated from: internal/lsp/lsproto/lsp_generated.go（Enumerations：行 9510-10792）
// generator: artifacts/lsproto-gen/generate.mjs（确定性转换；改生成器后重跑，不要手改本文件）
//
// 与 Go 的对应关系（偏离见下）：
//   * Go 的 reflect 驱动解码（structcodec.go 的 unmarshalStruct）在这里落成每个类型一张显式字段表
//     （StructSpec/StructFieldSpec，见 StructCodec.cs）：字段名、required、null 严格度、omitzero 逐条照抄 tag。
//   * Go 的类型别名（type X = Y）不生成 C# 类型（C# 没有跨文件类型别名），使用点直接用目标类型。
//   * Go 的包级常量（如 MarkupKindMarkdown）落在 C# 侧对应类型上（MarkupKind.Markdown），见 docs/architecture.md §3.3。
//   * Go 的值结构体在这里是 class（docs/architecture.md §3.2 的 `*T` → `T?`），因此 resolve() 的“拷贝”语义
//     退化成共享引用（Resolved* 是只读视图）。
//   * 字段名与外围类型同名在 C# 里非法（CS0542）。整个 lsp_generated.go 只有 Command.Command（2834 行），
//     按同名常量（constMemberName）的裁决改叫 `Value`。

using System.Globalization;
using System.Text;
using TypeScript.Json;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;

namespace TypeScript.LsProto;

/// <summary>Go: <c>type SemanticTokenType string</c>（lsp_generated.go:9517）。
/// A set of predefined token types. This set is not fixed
/// an clients can specify additional token types via the
/// corresponding client capabilities.
/// 
/// Since: 3.16.0
/// </summary>
public readonly partial struct SemanticTokenType : IEquatable<SemanticTokenType>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>SemanticTokenType(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public SemanticTokenType(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator SemanticTokenType(string value) => new(value);

    public static explicit operator string(SemanticTokenType value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(SemanticTokenType other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SemanticTokenType other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(SemanticTokenType left, SemanticTokenType right) => left.Equals(right);

    public static bool operator !=(SemanticTokenType left, SemanticTokenType right) => !left.Equals(right);

    /// <summary>Go: <c>func (m SemanticTokenType) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>SemanticTokenTypeNamespace SemanticTokenType = "namespace"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Namespace = new("namespace");

    /// <summary>Go: <c>SemanticTokenTypeType SemanticTokenType = "type"</c>（lsp_generated.go:9519）。
    /// Represents a generic type. Acts as a fallback for types which can't be mapped to
    /// a specific type like class or enum.
    /// </summary>
    public static readonly SemanticTokenType Type = new("type");

    /// <summary>Go: <c>SemanticTokenTypeClass SemanticTokenType = "class"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Class = new("class");

    /// <summary>Go: <c>SemanticTokenTypeEnum SemanticTokenType = "enum"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Enum = new("enum");

    /// <summary>Go: <c>SemanticTokenTypeInterface SemanticTokenType = "interface"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Interface = new("interface");

    /// <summary>Go: <c>SemanticTokenTypeStruct SemanticTokenType = "struct"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Struct = new("struct");

    /// <summary>Go: <c>SemanticTokenTypeTypeParameter SemanticTokenType = "typeParameter"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType TypeParameter = new("typeParameter");

    /// <summary>Go: <c>SemanticTokenTypeParameter SemanticTokenType = "parameter"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Parameter = new("parameter");

    /// <summary>Go: <c>SemanticTokenTypeVariable SemanticTokenType = "variable"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Variable = new("variable");

    /// <summary>Go: <c>SemanticTokenTypeProperty SemanticTokenType = "property"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Property = new("property");

    /// <summary>Go: <c>SemanticTokenTypeEnumMember SemanticTokenType = "enumMember"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType EnumMember = new("enumMember");

    /// <summary>Go: <c>SemanticTokenTypeEvent SemanticTokenType = "event"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Event = new("event");

    /// <summary>Go: <c>SemanticTokenTypeFunction SemanticTokenType = "function"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Function = new("function");

    /// <summary>Go: <c>SemanticTokenTypeMethod SemanticTokenType = "method"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Method = new("method");

    /// <summary>Go: <c>SemanticTokenTypeMacro SemanticTokenType = "macro"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Macro = new("macro");

    /// <summary>Go: <c>SemanticTokenTypeKeyword SemanticTokenType = "keyword"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Keyword = new("keyword");

    /// <summary>Go: <c>SemanticTokenTypeModifier SemanticTokenType = "modifier"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Modifier = new("modifier");

    /// <summary>Go: <c>SemanticTokenTypeComment SemanticTokenType = "comment"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Comment = new("comment");

    /// <summary>Go: <c>SemanticTokenTypeString SemanticTokenType = "string"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType String = new("string");

    /// <summary>Go: <c>SemanticTokenTypeNumber SemanticTokenType = "number"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Number = new("number");

    /// <summary>Go: <c>SemanticTokenTypeRegexp SemanticTokenType = "regexp"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Regexp = new("regexp");

    /// <summary>Go: <c>SemanticTokenTypeOperator SemanticTokenType = "operator"</c>（lsp_generated.go:9519）。
    /// </summary>
    public static readonly SemanticTokenType Operator = new("operator");

    /// <summary>Go: <c>SemanticTokenTypeDecorator SemanticTokenType = "decorator"</c>（lsp_generated.go:9519）。
    /// Since: 3.17.0
    /// </summary>
    public static readonly SemanticTokenType Decorator = new("decorator");

    /// <summary>Go: <c>SemanticTokenTypeLabel SemanticTokenType = "label"</c>（lsp_generated.go:9519）。
    /// Since: 3.18.0
    /// </summary>
    public static readonly SemanticTokenType Label = new("label");
}

/// <summary>Go: <c>type SemanticTokenModifier string</c>（lsp_generated.go:9555）。
/// A set of predefined token modifiers. This set is not fixed
/// an clients can specify additional token types via the
/// corresponding client capabilities.
/// 
/// Since: 3.16.0
/// </summary>
public readonly partial struct SemanticTokenModifier : IEquatable<SemanticTokenModifier>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>SemanticTokenModifier(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public SemanticTokenModifier(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator SemanticTokenModifier(string value) => new(value);

    public static explicit operator string(SemanticTokenModifier value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(SemanticTokenModifier other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SemanticTokenModifier other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(SemanticTokenModifier left, SemanticTokenModifier right) => left.Equals(right);

    public static bool operator !=(SemanticTokenModifier left, SemanticTokenModifier right) => !left.Equals(right);

    /// <summary>Go: <c>func (m SemanticTokenModifier) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>SemanticTokenModifierDeclaration SemanticTokenModifier = "declaration"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier Declaration = new("declaration");

    /// <summary>Go: <c>SemanticTokenModifierDefinition SemanticTokenModifier = "definition"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier Definition = new("definition");

    /// <summary>Go: <c>SemanticTokenModifierReadonly SemanticTokenModifier = "readonly"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier Readonly = new("readonly");

    /// <summary>Go: <c>SemanticTokenModifierStatic SemanticTokenModifier = "static"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier Static = new("static");

    /// <summary>Go: <c>SemanticTokenModifierDeprecated SemanticTokenModifier = "deprecated"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier Deprecated = new("deprecated");

    /// <summary>Go: <c>SemanticTokenModifierAbstract SemanticTokenModifier = "abstract"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier Abstract = new("abstract");

    /// <summary>Go: <c>SemanticTokenModifierAsync SemanticTokenModifier = "async"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier Async = new("async");

    /// <summary>Go: <c>SemanticTokenModifierModification SemanticTokenModifier = "modification"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier Modification = new("modification");

    /// <summary>Go: <c>SemanticTokenModifierDocumentation SemanticTokenModifier = "documentation"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier Documentation = new("documentation");

    /// <summary>Go: <c>SemanticTokenModifierDefaultLibrary SemanticTokenModifier = "defaultLibrary"</c>（lsp_generated.go:9557）。
    /// </summary>
    public static readonly SemanticTokenModifier DefaultLibrary = new("defaultLibrary");
}

/// <summary>Go: <c>type DocumentDiagnosticReportKind string</c>（lsp_generated.go:9573）。
/// The document diagnostic report kinds.
/// 
/// Since: 3.17.0
/// </summary>
public readonly partial struct DocumentDiagnosticReportKind : IEquatable<DocumentDiagnosticReportKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>DocumentDiagnosticReportKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public DocumentDiagnosticReportKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator DocumentDiagnosticReportKind(string value) => new(value);

    public static explicit operator string(DocumentDiagnosticReportKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(DocumentDiagnosticReportKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DocumentDiagnosticReportKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(DocumentDiagnosticReportKind left, DocumentDiagnosticReportKind right) => left.Equals(right);

    public static bool operator !=(DocumentDiagnosticReportKind left, DocumentDiagnosticReportKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m DocumentDiagnosticReportKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>DocumentDiagnosticReportKindFull DocumentDiagnosticReportKind = "full"</c>（lsp_generated.go:9575）。
    /// A diagnostic report with a full
    /// set of problems.
    /// </summary>
    public static readonly DocumentDiagnosticReportKind Full = new("full");

    /// <summary>Go: <c>DocumentDiagnosticReportKindUnchanged DocumentDiagnosticReportKind = "unchanged"</c>（lsp_generated.go:9575）。
    /// A report indicating that the last
    /// returned report is still accurate.
    /// </summary>
    public static readonly DocumentDiagnosticReportKind Unchanged = new("unchanged");
}

/// <summary>Go: <c>type ErrorCode int32</c>（lsp_generated.go:9585）。
/// Predefined error codes.
/// </summary>
public enum ErrorCode : int
{
    /// <summary>Go: <c>ErrorCodeParseError ErrorCode = -32700</c>（lsp_generated.go:9587）。</summary>
    ParseError = -32700,
    /// <summary>Go: <c>ErrorCodeInvalidRequest ErrorCode = -32600</c>（lsp_generated.go:9587）。</summary>
    InvalidRequest = -32600,
    /// <summary>Go: <c>ErrorCodeMethodNotFound ErrorCode = -32601</c>（lsp_generated.go:9587）。</summary>
    MethodNotFound = -32601,
    /// <summary>Go: <c>ErrorCodeInvalidParams ErrorCode = -32602</c>（lsp_generated.go:9587）。</summary>
    InvalidParams = -32602,
    /// <summary>Go: <c>ErrorCodeInternalError ErrorCode = -32603</c>（lsp_generated.go:9587）。</summary>
    InternalError = -32603,
    /// Error code indicating that a server received a notification or
    /// request before the server has received the `initialize` request.
    /// <summary>Go: <c>ErrorCodeServerNotInitialized ErrorCode = -32002</c>（lsp_generated.go:9587）。</summary>
    ServerNotInitialized = -32002,
    /// <summary>Go: <c>ErrorCodeUnknownErrorCode ErrorCode = -32001</c>（lsp_generated.go:9587）。</summary>
    UnknownErrorCode = -32001,
    /// A request failed but it was syntactically correct, e.g the
    /// method name was known and the parameters were valid. The error
    /// message should contain human readable information about why
    /// the request failed.
    /// 
    /// Since: 3.17.0
    /// <summary>Go: <c>ErrorCodeRequestFailed ErrorCode = -32803</c>（lsp_generated.go:9587）。</summary>
    RequestFailed = -32803,
    /// The server cancelled the request. This error code should
    /// only be used for requests that explicitly support being
    /// server cancellable.
    /// 
    /// Since: 3.17.0
    /// <summary>Go: <c>ErrorCodeServerCancelled ErrorCode = -32802</c>（lsp_generated.go:9587）。</summary>
    ServerCancelled = -32802,
    /// The server detected that the content of a document got
    /// modified outside normal conditions. A server should
    /// NOT send this error code if it detects a content change
    /// in it unprocessed messages. The result even computed
    /// on an older state might still be useful for the client.
    /// 
    /// If a client decides that a result is not of any use anymore
    /// the client should cancel the request.
    /// <summary>Go: <c>ErrorCodeContentModified ErrorCode = -32801</c>（lsp_generated.go:9587）。</summary>
    ContentModified = -32801,
    /// The client has canceled a request and a server has detected
    /// the cancel.
    /// <summary>Go: <c>ErrorCodeRequestCancelled ErrorCode = -32800</c>（lsp_generated.go:9587）。</summary>
    RequestCancelled = -32800
}

/// <summary>Go: <c>type FoldingRangeKind string</c>（lsp_generated.go:9656）。
/// A set of predefined range kinds.
/// </summary>
public readonly partial struct FoldingRangeKind : IEquatable<FoldingRangeKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>FoldingRangeKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public FoldingRangeKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator FoldingRangeKind(string value) => new(value);

    public static explicit operator string(FoldingRangeKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(FoldingRangeKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FoldingRangeKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(FoldingRangeKind left, FoldingRangeKind right) => left.Equals(right);

    public static bool operator !=(FoldingRangeKind left, FoldingRangeKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m FoldingRangeKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>FoldingRangeKindComment FoldingRangeKind = "comment"</c>（lsp_generated.go:9658）。
    /// Folding range for a comment
    /// </summary>
    public static readonly FoldingRangeKind Comment = new("comment");

    /// <summary>Go: <c>FoldingRangeKindImports FoldingRangeKind = "imports"</c>（lsp_generated.go:9658）。
    /// Folding range for an import or include
    /// </summary>
    public static readonly FoldingRangeKind Imports = new("imports");

    /// <summary>Go: <c>FoldingRangeKindRegion FoldingRangeKind = "region"</c>（lsp_generated.go:9658）。
    /// Folding range for a region (e.g. `#region`)
    /// </summary>
    public static readonly FoldingRangeKind Region = new("region");
}

/// <summary>Go: <c>type SymbolKind uint32</c>（lsp_generated.go:9668）。
/// A symbol kind.
/// </summary>
public enum SymbolKind : uint
{
    /// <summary>Go: <c>SymbolKindFile SymbolKind = 1</c>（lsp_generated.go:9670）。</summary>
    File = 1,
    /// <summary>Go: <c>SymbolKindModule SymbolKind = 2</c>（lsp_generated.go:9670）。</summary>
    Module = 2,
    /// <summary>Go: <c>SymbolKindNamespace SymbolKind = 3</c>（lsp_generated.go:9670）。</summary>
    Namespace = 3,
    /// <summary>Go: <c>SymbolKindPackage SymbolKind = 4</c>（lsp_generated.go:9670）。</summary>
    Package = 4,
    /// <summary>Go: <c>SymbolKindClass SymbolKind = 5</c>（lsp_generated.go:9670）。</summary>
    Class = 5,
    /// <summary>Go: <c>SymbolKindMethod SymbolKind = 6</c>（lsp_generated.go:9670）。</summary>
    Method = 6,
    /// <summary>Go: <c>SymbolKindProperty SymbolKind = 7</c>（lsp_generated.go:9670）。</summary>
    Property = 7,
    /// <summary>Go: <c>SymbolKindField SymbolKind = 8</c>（lsp_generated.go:9670）。</summary>
    Field = 8,
    /// <summary>Go: <c>SymbolKindConstructor SymbolKind = 9</c>（lsp_generated.go:9670）。</summary>
    Constructor = 9,
    /// <summary>Go: <c>SymbolKindEnum SymbolKind = 10</c>（lsp_generated.go:9670）。</summary>
    Enum = 10,
    /// <summary>Go: <c>SymbolKindInterface SymbolKind = 11</c>（lsp_generated.go:9670）。</summary>
    Interface = 11,
    /// <summary>Go: <c>SymbolKindFunction SymbolKind = 12</c>（lsp_generated.go:9670）。</summary>
    Function = 12,
    /// <summary>Go: <c>SymbolKindVariable SymbolKind = 13</c>（lsp_generated.go:9670）。</summary>
    Variable = 13,
    /// <summary>Go: <c>SymbolKindConstant SymbolKind = 14</c>（lsp_generated.go:9670）。</summary>
    Constant = 14,
    /// <summary>Go: <c>SymbolKindString SymbolKind = 15</c>（lsp_generated.go:9670）。</summary>
    String = 15,
    /// <summary>Go: <c>SymbolKindNumber SymbolKind = 16</c>（lsp_generated.go:9670）。</summary>
    Number = 16,
    /// <summary>Go: <c>SymbolKindBoolean SymbolKind = 17</c>（lsp_generated.go:9670）。</summary>
    Boolean = 17,
    /// <summary>Go: <c>SymbolKindArray SymbolKind = 18</c>（lsp_generated.go:9670）。</summary>
    Array = 18,
    /// <summary>Go: <c>SymbolKindObject SymbolKind = 19</c>（lsp_generated.go:9670）。</summary>
    Object = 19,
    /// <summary>Go: <c>SymbolKindKey SymbolKind = 20</c>（lsp_generated.go:9670）。</summary>
    Key = 20,
    /// <summary>Go: <c>SymbolKindNull SymbolKind = 21</c>（lsp_generated.go:9670）。</summary>
    Null = 21,
    /// <summary>Go: <c>SymbolKindEnumMember SymbolKind = 22</c>（lsp_generated.go:9670）。</summary>
    EnumMember = 22,
    /// <summary>Go: <c>SymbolKindStruct SymbolKind = 23</c>（lsp_generated.go:9670）。</summary>
    Struct = 23,
    /// <summary>Go: <c>SymbolKindEvent SymbolKind = 24</c>（lsp_generated.go:9670）。</summary>
    Event = 24,
    /// <summary>Go: <c>SymbolKindOperator SymbolKind = 25</c>（lsp_generated.go:9670）。</summary>
    Operator = 25,
    /// <summary>Go: <c>SymbolKindTypeParameter SymbolKind = 26</c>（lsp_generated.go:9670）。</summary>
    TypeParameter = 26
}

/// <summary>Go: <c>type SymbolTag uint32</c>（lsp_generated.go:9714）。
/// Symbol tags are extra annotations that tweak the rendering of a symbol.
/// 
/// Since: 3.16
/// </summary>
public enum SymbolTag : uint
{
    /// Render a symbol as obsolete, usually using a strike-out.
    /// <summary>Go: <c>SymbolTagDeprecated SymbolTag = 1</c>（lsp_generated.go:9716）。</summary>
    Deprecated = 1
}

/// <summary>Go: <c>type UniquenessLevel string</c>（lsp_generated.go:9736）。
/// Moniker uniqueness level to define scope of the moniker.
/// 
/// Since: 3.16.0
/// </summary>
public readonly partial struct UniquenessLevel : IEquatable<UniquenessLevel>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>UniquenessLevel(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public UniquenessLevel(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator UniquenessLevel(string value) => new(value);

    public static explicit operator string(UniquenessLevel value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(UniquenessLevel other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is UniquenessLevel other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(UniquenessLevel left, UniquenessLevel right) => left.Equals(right);

    public static bool operator !=(UniquenessLevel left, UniquenessLevel right) => !left.Equals(right);

    /// <summary>Go: <c>func (m UniquenessLevel) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>UniquenessLevelDocument UniquenessLevel = "document"</c>（lsp_generated.go:9738）。
    /// The moniker is only unique inside a document
    /// </summary>
    public static readonly UniquenessLevel Document = new("document");

    /// <summary>Go: <c>UniquenessLevelProject UniquenessLevel = "project"</c>（lsp_generated.go:9738）。
    /// The moniker is unique inside a project for which a dump got created
    /// </summary>
    public static readonly UniquenessLevel Project = new("project");

    /// <summary>Go: <c>UniquenessLevelGroup UniquenessLevel = "group"</c>（lsp_generated.go:9738）。
    /// The moniker is unique inside the group to which a project belongs
    /// </summary>
    public static readonly UniquenessLevel Group = new("group");

    /// <summary>Go: <c>UniquenessLevelScheme UniquenessLevel = "scheme"</c>（lsp_generated.go:9738）。
    /// The moniker is unique inside the moniker scheme.
    /// </summary>
    public static readonly UniquenessLevel Scheme = new("scheme");

    /// <summary>Go: <c>UniquenessLevelGlobal UniquenessLevel = "global"</c>（lsp_generated.go:9738）。
    /// The moniker is globally unique
    /// </summary>
    public static readonly UniquenessLevel Global = new("global");
}

/// <summary>Go: <c>type MonikerKind string</c>（lsp_generated.go:9754）。
/// The moniker kind.
/// 
/// Since: 3.16.0
/// </summary>
public readonly partial struct MonikerKind : IEquatable<MonikerKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>MonikerKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public MonikerKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator MonikerKind(string value) => new(value);

    public static explicit operator string(MonikerKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(MonikerKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is MonikerKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(MonikerKind left, MonikerKind right) => left.Equals(right);

    public static bool operator !=(MonikerKind left, MonikerKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m MonikerKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>MonikerKindImport MonikerKind = "import"</c>（lsp_generated.go:9756）。
    /// The moniker represent a symbol that is imported into a project
    /// </summary>
    public static readonly MonikerKind Import = new("import");

    /// <summary>Go: <c>MonikerKindExport MonikerKind = "export"</c>（lsp_generated.go:9756）。
    /// The moniker represents a symbol that is exported from a project
    /// </summary>
    public static readonly MonikerKind Export = new("export");

    /// <summary>Go: <c>MonikerKindLocal MonikerKind = "local"</c>（lsp_generated.go:9756）。
    /// The moniker represents a symbol that is local to a project (e.g. a local
    /// variable of a function, a class not visible outside the project, ...)
    /// </summary>
    public static readonly MonikerKind Local = new("local");
}

/// <summary>Go: <c>type InlayHintKind uint32</c>（lsp_generated.go:9769）。
/// Inlay hint kinds.
/// 
/// Since: 3.17.0
/// </summary>
public enum InlayHintKind : uint
{
    /// An inlay hint that for a type annotation.
    /// <summary>Go: <c>InlayHintKindType InlayHintKind = 1</c>（lsp_generated.go:9771）。</summary>
    Type = 1,
    /// An inlay hint that is for a parameter.
    /// <summary>Go: <c>InlayHintKindParameter InlayHintKind = 2</c>（lsp_generated.go:9771）。</summary>
    Parameter = 2
}

/// <summary>Go: <c>type MessageType uint32</c>（lsp_generated.go:9791）。
/// The message type
/// </summary>
public enum MessageType : uint
{
    /// An error message.
    /// <summary>Go: <c>MessageTypeError MessageType = 1</c>（lsp_generated.go:9793）。</summary>
    Error = 1,
    /// A warning message.
    /// <summary>Go: <c>MessageTypeWarning MessageType = 2</c>（lsp_generated.go:9793）。</summary>
    Warning = 2,
    /// An information message.
    /// <summary>Go: <c>MessageTypeInfo MessageType = 3</c>（lsp_generated.go:9793）。</summary>
    Info = 3,
    /// A log message.
    /// <summary>Go: <c>MessageTypeLog MessageType = 4</c>（lsp_generated.go:9793）。</summary>
    Log = 4,
    /// A debug message.
    /// 
    /// Since: 3.18.0
    /// <summary>Go: <c>MessageTypeDebug MessageType = 5</c>（lsp_generated.go:9793）。</summary>
    Debug = 5
}

/// <summary>Go: <c>type TextDocumentSyncKind uint32</c>（lsp_generated.go:9822）。
/// Defines how the host (editor) should sync
/// document changes to the language server.
/// </summary>
public enum TextDocumentSyncKind : uint
{
    /// Documents should not be synced at all.
    /// <summary>Go: <c>TextDocumentSyncKindNone TextDocumentSyncKind = 0</c>（lsp_generated.go:9824）。</summary>
    None = 0,
    /// Documents are synced by always sending the full content
    /// of the document.
    /// <summary>Go: <c>TextDocumentSyncKindFull TextDocumentSyncKind = 1</c>（lsp_generated.go:9824）。</summary>
    Full = 1,
    /// Documents are synced by sending the full content on open.
    /// After that only incremental updates to the document are
    /// send.
    /// <summary>Go: <c>TextDocumentSyncKindIncremental TextDocumentSyncKind = 2</c>（lsp_generated.go:9824）。</summary>
    Incremental = 2
}

/// <summary>Go: <c>type TextDocumentSaveReason uint32</c>（lsp_generated.go:9849）。
/// Represents reasons why a text document is saved.
/// </summary>
public enum TextDocumentSaveReason : uint
{
    /// Manually triggered, e.g. by the user pressing save, by starting debugging,
    /// or by an API call.
    /// <summary>Go: <c>TextDocumentSaveReasonManual TextDocumentSaveReason = 1</c>（lsp_generated.go:9851）。</summary>
    Manual = 1,
    /// Automatic after a delay.
    /// <summary>Go: <c>TextDocumentSaveReasonAfterDelay TextDocumentSaveReason = 2</c>（lsp_generated.go:9851）。</summary>
    AfterDelay = 2,
    /// When the editor lost focus.
    /// <summary>Go: <c>TextDocumentSaveReasonFocusOut TextDocumentSaveReason = 3</c>（lsp_generated.go:9851）。</summary>
    FocusOut = 3
}

/// <summary>Go: <c>type CompletionItemKind uint32</c>（lsp_generated.go:9874）。
/// The kind of a completion entry.
/// </summary>
public enum CompletionItemKind : uint
{
    /// <summary>Go: <c>CompletionItemKindText CompletionItemKind = 1</c>（lsp_generated.go:9876）。</summary>
    Text = 1,
    /// <summary>Go: <c>CompletionItemKindMethod CompletionItemKind = 2</c>（lsp_generated.go:9876）。</summary>
    Method = 2,
    /// <summary>Go: <c>CompletionItemKindFunction CompletionItemKind = 3</c>（lsp_generated.go:9876）。</summary>
    Function = 3,
    /// <summary>Go: <c>CompletionItemKindConstructor CompletionItemKind = 4</c>（lsp_generated.go:9876）。</summary>
    Constructor = 4,
    /// <summary>Go: <c>CompletionItemKindField CompletionItemKind = 5</c>（lsp_generated.go:9876）。</summary>
    Field = 5,
    /// <summary>Go: <c>CompletionItemKindVariable CompletionItemKind = 6</c>（lsp_generated.go:9876）。</summary>
    Variable = 6,
    /// <summary>Go: <c>CompletionItemKindClass CompletionItemKind = 7</c>（lsp_generated.go:9876）。</summary>
    Class = 7,
    /// <summary>Go: <c>CompletionItemKindInterface CompletionItemKind = 8</c>（lsp_generated.go:9876）。</summary>
    Interface = 8,
    /// <summary>Go: <c>CompletionItemKindModule CompletionItemKind = 9</c>（lsp_generated.go:9876）。</summary>
    Module = 9,
    /// <summary>Go: <c>CompletionItemKindProperty CompletionItemKind = 10</c>（lsp_generated.go:9876）。</summary>
    Property = 10,
    /// <summary>Go: <c>CompletionItemKindUnit CompletionItemKind = 11</c>（lsp_generated.go:9876）。</summary>
    Unit = 11,
    /// <summary>Go: <c>CompletionItemKindValue CompletionItemKind = 12</c>（lsp_generated.go:9876）。</summary>
    Value = 12,
    /// <summary>Go: <c>CompletionItemKindEnum CompletionItemKind = 13</c>（lsp_generated.go:9876）。</summary>
    Enum = 13,
    /// <summary>Go: <c>CompletionItemKindKeyword CompletionItemKind = 14</c>（lsp_generated.go:9876）。</summary>
    Keyword = 14,
    /// <summary>Go: <c>CompletionItemKindSnippet CompletionItemKind = 15</c>（lsp_generated.go:9876）。</summary>
    Snippet = 15,
    /// <summary>Go: <c>CompletionItemKindColor CompletionItemKind = 16</c>（lsp_generated.go:9876）。</summary>
    Color = 16,
    /// <summary>Go: <c>CompletionItemKindFile CompletionItemKind = 17</c>（lsp_generated.go:9876）。</summary>
    File = 17,
    /// <summary>Go: <c>CompletionItemKindReference CompletionItemKind = 18</c>（lsp_generated.go:9876）。</summary>
    Reference = 18,
    /// <summary>Go: <c>CompletionItemKindFolder CompletionItemKind = 19</c>（lsp_generated.go:9876）。</summary>
    Folder = 19,
    /// <summary>Go: <c>CompletionItemKindEnumMember CompletionItemKind = 20</c>（lsp_generated.go:9876）。</summary>
    EnumMember = 20,
    /// <summary>Go: <c>CompletionItemKindConstant CompletionItemKind = 21</c>（lsp_generated.go:9876）。</summary>
    Constant = 21,
    /// <summary>Go: <c>CompletionItemKindStruct CompletionItemKind = 22</c>（lsp_generated.go:9876）。</summary>
    Struct = 22,
    /// <summary>Go: <c>CompletionItemKindEvent CompletionItemKind = 23</c>（lsp_generated.go:9876）。</summary>
    Event = 23,
    /// <summary>Go: <c>CompletionItemKindOperator CompletionItemKind = 24</c>（lsp_generated.go:9876）。</summary>
    Operator = 24,
    /// <summary>Go: <c>CompletionItemKindTypeParameter CompletionItemKind = 25</c>（lsp_generated.go:9876）。</summary>
    TypeParameter = 25
}

/// <summary>Go: <c>type CompletionItemTag uint32</c>（lsp_generated.go:9920）。
/// Completion item tags are extra annotations that tweak the rendering of a completion
/// item.
/// 
/// Since: 3.15.0
/// </summary>
public enum CompletionItemTag : uint
{
    /// Render a completion as obsolete, usually using a strike-out.
    /// <summary>Go: <c>CompletionItemTagDeprecated CompletionItemTag = 1</c>（lsp_generated.go:9922）。</summary>
    Deprecated = 1
}

/// <summary>Go: <c>type InsertTextFormat uint32</c>（lsp_generated.go:9941）。
/// Defines whether the insert text in a completion item should be interpreted as
/// plain text or a snippet.
/// </summary>
public enum InsertTextFormat : uint
{
    /// The primary text to be inserted is treated as a plain string.
    /// <summary>Go: <c>InsertTextFormatPlainText InsertTextFormat = 1</c>（lsp_generated.go:9943）。</summary>
    PlainText = 1,
    /// The primary text to be inserted is treated as a snippet.
    /// 
    /// A snippet can define tab stops and placeholders with `$1`, `$2`
    /// and `${3:foo}`. `$0` defines the final tab stop, it defaults to
    /// the end of the snippet. Placeholders with equal identifiers are linked,
    /// that is typing in one will update others too.
    /// 
    /// See also: https://microsoft.github.io/language-server-protocol/specifications/specification-current/#snippet_syntax
    /// <summary>Go: <c>InsertTextFormatSnippet InsertTextFormat = 2</c>（lsp_generated.go:9943）。</summary>
    Snippet = 2
}

/// <summary>Go: <c>type InsertTextMode uint32</c>（lsp_generated.go:9973）。
/// How whitespace and indentation is handled during completion
/// item insertion.
/// 
/// Since: 3.16.0
/// </summary>
public enum InsertTextMode : uint
{
    /// The insertion or replace strings is taken as it is. If the
    /// value is multi line the lines below the cursor will be
    /// inserted using the indentation defined in the string value.
    /// The client will not apply any kind of adjustments to the
    /// string.
    /// <summary>Go: <c>InsertTextModeAsIs InsertTextMode = 1</c>（lsp_generated.go:9975）。</summary>
    AsIs = 1,
    /// The editor adjusts leading whitespace of new lines so that
    /// they match the indentation up to the cursor of the line for
    /// which the item is accepted.
    /// 
    /// Consider a line like this: &lt;2tabs&gt;&lt;cursor&gt;&lt;3tabs&gt;foo. Accepting a
    /// multi line completion item is indented using 2 tabs and all
    /// following lines inserted will be indented using 2 tabs as well.
    /// <summary>Go: <c>InsertTextModeAdjustIndentation InsertTextMode = 2</c>（lsp_generated.go:9975）。</summary>
    AdjustIndentation = 2
}

/// <summary>Go: <c>type DocumentHighlightKind uint32</c>（lsp_generated.go:10005）。
/// A document highlight kind.
/// </summary>
public enum DocumentHighlightKind : uint
{
    /// A textual occurrence.
    /// <summary>Go: <c>DocumentHighlightKindText DocumentHighlightKind = 1</c>（lsp_generated.go:10007）。</summary>
    Text = 1,
    /// Read-access of a symbol, like reading a variable.
    /// <summary>Go: <c>DocumentHighlightKindRead DocumentHighlightKind = 2</c>（lsp_generated.go:10007）。</summary>
    Read = 2,
    /// Write-access of a symbol, like writing to a variable.
    /// <summary>Go: <c>DocumentHighlightKindWrite DocumentHighlightKind = 3</c>（lsp_generated.go:10007）。</summary>
    Write = 3
}

/// <summary>Go: <c>type CodeActionKind string</c>（lsp_generated.go:10029）。
/// A set of predefined code action kinds
/// </summary>
public readonly partial struct CodeActionKind : IEquatable<CodeActionKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>CodeActionKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public CodeActionKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator CodeActionKind(string value) => new(value);

    public static explicit operator string(CodeActionKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(CodeActionKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CodeActionKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(CodeActionKind left, CodeActionKind right) => left.Equals(right);

    public static bool operator !=(CodeActionKind left, CodeActionKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m CodeActionKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>CodeActionKindEmpty CodeActionKind = ""</c>（lsp_generated.go:10031）。
    /// Empty kind.
    /// </summary>
    public static readonly CodeActionKind Empty = new("");

    /// <summary>Go: <c>CodeActionKindQuickFix CodeActionKind = "quickfix"</c>（lsp_generated.go:10031）。
    /// Base kind for quickfix actions: 'quickfix'
    /// </summary>
    public static readonly CodeActionKind QuickFix = new("quickfix");

    /// <summary>Go: <c>CodeActionKindRefactor CodeActionKind = "refactor"</c>（lsp_generated.go:10031）。
    /// Base kind for refactoring actions: 'refactor'
    /// </summary>
    public static readonly CodeActionKind Refactor = new("refactor");

    /// <summary>Go: <c>CodeActionKindRefactorExtract CodeActionKind = "refactor.extract"</c>（lsp_generated.go:10031）。
    /// Base kind for refactoring extraction actions: 'refactor.extract'
    /// 
    /// Example extract actions:
    /// 
    /// - Extract method
    /// - Extract function
    /// - Extract variable
    /// - Extract interface from class
    /// - ...
    /// </summary>
    public static readonly CodeActionKind RefactorExtract = new("refactor.extract");

    /// <summary>Go: <c>CodeActionKindRefactorInline CodeActionKind = "refactor.inline"</c>（lsp_generated.go:10031）。
    /// Base kind for refactoring inline actions: 'refactor.inline'
    /// 
    /// Example inline actions:
    /// 
    /// - Inline function
    /// - Inline variable
    /// - Inline constant
    /// - ...
    /// </summary>
    public static readonly CodeActionKind RefactorInline = new("refactor.inline");

    /// <summary>Go: <c>CodeActionKindRefactorMove CodeActionKind = "refactor.move"</c>（lsp_generated.go:10031）。
    /// Base kind for refactoring move actions: `refactor.move`
    /// 
    /// Example move actions:
    /// 
    /// - Move a function to a new file
    /// - Move a property between classes
    /// - Move method to base class
    /// - ...
    /// 
    /// Since: 3.18.0
    /// </summary>
    public static readonly CodeActionKind RefactorMove = new("refactor.move");

    /// <summary>Go: <c>CodeActionKindRefactorRewrite CodeActionKind = "refactor.rewrite"</c>（lsp_generated.go:10031）。
    /// Base kind for refactoring rewrite actions: 'refactor.rewrite'
    /// 
    /// Example rewrite actions:
    /// 
    /// - Convert JavaScript function to class
    /// - Add or remove parameter
    /// - Encapsulate field
    /// - Make method static
    /// - Move method to base class
    /// - ...
    /// </summary>
    public static readonly CodeActionKind RefactorRewrite = new("refactor.rewrite");

    /// <summary>Go: <c>CodeActionKindSource CodeActionKind = "source"</c>（lsp_generated.go:10031）。
    /// Base kind for source actions: `source`
    /// 
    /// Source code actions apply to the entire file.
    /// </summary>
    public static readonly CodeActionKind Source = new("source");

    /// <summary>Go: <c>CodeActionKindSourceOrganizeImports CodeActionKind = "source.organizeImports"</c>（lsp_generated.go:10031）。
    /// Base kind for an organize imports source action: `source.organizeImports`
    /// </summary>
    public static readonly CodeActionKind SourceOrganizeImports = new("source.organizeImports");

    /// <summary>Go: <c>CodeActionKindSourceFixAll CodeActionKind = "source.fixAll"</c>（lsp_generated.go:10031）。
    /// Base kind for auto-fix source actions: `source.fixAll`.
    /// 
    /// Fix all actions automatically fix errors that have a clear fix that do not require user input.
    /// They should not suppress errors or perform unsafe fixes such as generating new types or classes.
    /// 
    /// Since: 3.15.0
    /// </summary>
    public static readonly CodeActionKind SourceFixAll = new("source.fixAll");
}

/// <summary>Go: <c>type CodeActionTag uint32</c>（lsp_generated.go:10097）。
/// Code action tags are extra annotations that tweak the behavior of a code action.
/// 
/// Since: 3.18.0
/// </summary>
public enum CodeActionTag : uint
{
    /// Marks the code action as LLM-generated.
    /// <summary>Go: <c>CodeActionTagLLMGenerated CodeActionTag = 1</c>（lsp_generated.go:10099）。</summary>
    LLMGenerated = 1
}

/// <summary>Go: <c>type TraceValue string</c>（lsp_generated.go:10116）。
/// </summary>
public readonly partial struct TraceValue : IEquatable<TraceValue>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>TraceValue(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public TraceValue(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator TraceValue(string value) => new(value);

    public static explicit operator string(TraceValue value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(TraceValue other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TraceValue other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(TraceValue left, TraceValue right) => left.Equals(right);

    public static bool operator !=(TraceValue left, TraceValue right) => !left.Equals(right);

    /// <summary>Go: <c>func (m TraceValue) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>TraceValueOff TraceValue = "off"</c>（lsp_generated.go:10118）。
    /// Turn tracing off.
    /// </summary>
    public static readonly TraceValue Off = new("off");

    /// <summary>Go: <c>TraceValueMessages TraceValue = "messages"</c>（lsp_generated.go:10118）。
    /// Trace messages only.
    /// </summary>
    public static readonly TraceValue Messages = new("messages");

    /// <summary>Go: <c>TraceValueVerbose TraceValue = "verbose"</c>（lsp_generated.go:10118）。
    /// Verbose message tracing.
    /// </summary>
    public static readonly TraceValue Verbose = new("verbose");
}

/// <summary>Go: <c>type MarkupKind string</c>（lsp_generated.go:10132）。
/// Describes the content type that a client supports in various
/// result literals like `Hover`, `ParameterInfo` or `CompletionItem`.
/// 
/// Please note that `MarkupKinds` must not start with a `$`. This kinds
/// are reserved for internal usage.
/// </summary>
public readonly partial struct MarkupKind : IEquatable<MarkupKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>MarkupKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public MarkupKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator MarkupKind(string value) => new(value);

    public static explicit operator string(MarkupKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(MarkupKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is MarkupKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(MarkupKind left, MarkupKind right) => left.Equals(right);

    public static bool operator !=(MarkupKind left, MarkupKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m MarkupKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>MarkupKindPlainText MarkupKind = "plaintext"</c>（lsp_generated.go:10134）。
    /// Plain text is supported as a content format
    /// </summary>
    public static readonly MarkupKind PlainText = new("plaintext");

    /// <summary>Go: <c>MarkupKindMarkdown MarkupKind = "markdown"</c>（lsp_generated.go:10134）。
    /// Markdown is supported as a content format
    /// </summary>
    public static readonly MarkupKind Markdown = new("markdown");
}

/// <summary>Go: <c>type LanguageKind string</c>（lsp_generated.go:10144）。
/// Predefined Language kinds
/// 
/// Since: 3.18.0
/// </summary>
public readonly partial struct LanguageKind : IEquatable<LanguageKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>LanguageKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public LanguageKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator LanguageKind(string value) => new(value);

    public static explicit operator string(LanguageKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(LanguageKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is LanguageKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(LanguageKind left, LanguageKind right) => left.Equals(right);

    public static bool operator !=(LanguageKind left, LanguageKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m LanguageKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>LanguageKindABAP LanguageKind = "abap"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind ABAP = new("abap");

    /// <summary>Go: <c>LanguageKindWindowsBat LanguageKind = "bat"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind WindowsBat = new("bat");

    /// <summary>Go: <c>LanguageKindBibTeX LanguageKind = "bibtex"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind BibTeX = new("bibtex");

    /// <summary>Go: <c>LanguageKindClojure LanguageKind = "clojure"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Clojure = new("clojure");

    /// <summary>Go: <c>LanguageKindCoffeescript LanguageKind = "coffeescript"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Coffeescript = new("coffeescript");

    /// <summary>Go: <c>LanguageKindC LanguageKind = "c"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind C = new("c");

    /// <summary>Go: <c>LanguageKindCPP LanguageKind = "cpp"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind CPP = new("cpp");

    /// <summary>Go: <c>LanguageKindCSharp LanguageKind = "csharp"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind CSharp = new("csharp");

    /// <summary>Go: <c>LanguageKindCSS LanguageKind = "css"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind CSS = new("css");

    /// <summary>Go: <c>LanguageKindD LanguageKind = "d"</c>（lsp_generated.go:10146）。
    /// Since: 3.18.0
    /// </summary>
    public static readonly LanguageKind D = new("d");

    /// <summary>Go: <c>LanguageKindDelphi LanguageKind = "pascal"</c>（lsp_generated.go:10146）。
    /// Since: 3.18.0
    /// </summary>
    public static readonly LanguageKind Delphi = new("pascal");

    /// <summary>Go: <c>LanguageKindDiff LanguageKind = "diff"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Diff = new("diff");

    /// <summary>Go: <c>LanguageKindDart LanguageKind = "dart"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Dart = new("dart");

    /// <summary>Go: <c>LanguageKindDockerfile LanguageKind = "dockerfile"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Dockerfile = new("dockerfile");

    /// <summary>Go: <c>LanguageKindElixir LanguageKind = "elixir"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Elixir = new("elixir");

    /// <summary>Go: <c>LanguageKindErlang LanguageKind = "erlang"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Erlang = new("erlang");

    /// <summary>Go: <c>LanguageKindFSharp LanguageKind = "fsharp"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind FSharp = new("fsharp");

    /// <summary>Go: <c>LanguageKindGitCommit LanguageKind = "git-commit"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind GitCommit = new("git-commit");

    /// <summary>Go: <c>LanguageKindGitRebase LanguageKind = "git-rebase"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind GitRebase = new("git-rebase");

    /// <summary>Go: <c>LanguageKindGo LanguageKind = "go"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Go = new("go");

    /// <summary>Go: <c>LanguageKindGroovy LanguageKind = "groovy"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Groovy = new("groovy");

    /// <summary>Go: <c>LanguageKindHandlebars LanguageKind = "handlebars"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Handlebars = new("handlebars");

    /// <summary>Go: <c>LanguageKindHaskell LanguageKind = "haskell"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Haskell = new("haskell");

    /// <summary>Go: <c>LanguageKindHTML LanguageKind = "html"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind HTML = new("html");

    /// <summary>Go: <c>LanguageKindIni LanguageKind = "ini"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Ini = new("ini");

    /// <summary>Go: <c>LanguageKindJava LanguageKind = "java"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Java = new("java");

    /// <summary>Go: <c>LanguageKindJavaScript LanguageKind = "javascript"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind JavaScript = new("javascript");

    /// <summary>Go: <c>LanguageKindJavaScriptReact LanguageKind = "javascriptreact"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind JavaScriptReact = new("javascriptreact");

    /// <summary>Go: <c>LanguageKindJSON LanguageKind = "json"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind JSON = new("json");

    /// <summary>Go: <c>LanguageKindLaTeX LanguageKind = "latex"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind LaTeX = new("latex");

    /// <summary>Go: <c>LanguageKindLess LanguageKind = "less"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Less = new("less");

    /// <summary>Go: <c>LanguageKindLua LanguageKind = "lua"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Lua = new("lua");

    /// <summary>Go: <c>LanguageKindMakefile LanguageKind = "makefile"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Makefile = new("makefile");

    /// <summary>Go: <c>LanguageKindMarkdown LanguageKind = "markdown"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Markdown = new("markdown");

    /// <summary>Go: <c>LanguageKindObjectiveC LanguageKind = "objective-c"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind ObjectiveC = new("objective-c");

    /// <summary>Go: <c>LanguageKindObjectiveCPP LanguageKind = "objective-cpp"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind ObjectiveCPP = new("objective-cpp");

    /// <summary>Go: <c>LanguageKindPascal LanguageKind = "pascal"</c>（lsp_generated.go:10146）。
    /// Since: 3.18.0
    /// </summary>
    public static readonly LanguageKind Pascal = new("pascal");

    /// <summary>Go: <c>LanguageKindPerl LanguageKind = "perl"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Perl = new("perl");

    /// <summary>Go: <c>LanguageKindPerl6 LanguageKind = "perl6"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Perl6 = new("perl6");

    /// <summary>Go: <c>LanguageKindPHP LanguageKind = "php"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind PHP = new("php");

    /// <summary>Go: <c>LanguageKindPlaintext LanguageKind = "plaintext"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Plaintext = new("plaintext");

    /// <summary>Go: <c>LanguageKindPowershell LanguageKind = "powershell"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Powershell = new("powershell");

    /// <summary>Go: <c>LanguageKindPug LanguageKind = "jade"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Pug = new("jade");

    /// <summary>Go: <c>LanguageKindPython LanguageKind = "python"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Python = new("python");

    /// <summary>Go: <c>LanguageKindR LanguageKind = "r"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind R = new("r");

    /// <summary>Go: <c>LanguageKindRazor LanguageKind = "razor"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Razor = new("razor");

    /// <summary>Go: <c>LanguageKindRuby LanguageKind = "ruby"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Ruby = new("ruby");

    /// <summary>Go: <c>LanguageKindRust LanguageKind = "rust"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Rust = new("rust");

    /// <summary>Go: <c>LanguageKindSCSS LanguageKind = "scss"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind SCSS = new("scss");

    /// <summary>Go: <c>LanguageKindSASS LanguageKind = "sass"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind SASS = new("sass");

    /// <summary>Go: <c>LanguageKindScala LanguageKind = "scala"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Scala = new("scala");

    /// <summary>Go: <c>LanguageKindShaderLab LanguageKind = "shaderlab"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind ShaderLab = new("shaderlab");

    /// <summary>Go: <c>LanguageKindShellScript LanguageKind = "shellscript"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind ShellScript = new("shellscript");

    /// <summary>Go: <c>LanguageKindSQL LanguageKind = "sql"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind SQL = new("sql");

    /// <summary>Go: <c>LanguageKindSwift LanguageKind = "swift"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind Swift = new("swift");

    /// <summary>Go: <c>LanguageKindTypeScript LanguageKind = "typescript"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind TypeScript = new("typescript");

    /// <summary>Go: <c>LanguageKindTypeScriptReact LanguageKind = "typescriptreact"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind TypeScriptReact = new("typescriptreact");

    /// <summary>Go: <c>LanguageKindTeX LanguageKind = "tex"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind TeX = new("tex");

    /// <summary>Go: <c>LanguageKindVisualBasic LanguageKind = "vb"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind VisualBasic = new("vb");

    /// <summary>Go: <c>LanguageKindXML LanguageKind = "xml"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind XML = new("xml");

    /// <summary>Go: <c>LanguageKindXSL LanguageKind = "xsl"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind XSL = new("xsl");

    /// <summary>Go: <c>LanguageKindYAML LanguageKind = "yaml"</c>（lsp_generated.go:10146）。
    /// </summary>
    public static readonly LanguageKind YAML = new("yaml");
}

/// <summary>Go: <c>type InlineCompletionTriggerKind uint32</c>（lsp_generated.go:10217）。
/// Describes how an provider was triggered.
/// 
/// Since: 3.18.0
/// </summary>
public enum InlineCompletionTriggerKind : uint
{
    /// Completion was triggered explicitly by a user gesture.
    /// <summary>Go: <c>InlineCompletionTriggerKindInvoked InlineCompletionTriggerKind = 1</c>（lsp_generated.go:10219）。</summary>
    Invoked = 1,
    /// Completion was triggered automatically while editing.
    /// <summary>Go: <c>InlineCompletionTriggerKindAutomatic InlineCompletionTriggerKind = 2</c>（lsp_generated.go:10219）。</summary>
    Automatic = 2
}

/// <summary>Go: <c>type PositionEncodingKind string</c>（lsp_generated.go:10241）。
/// A set of predefined position encoding kinds.
/// 
/// Since: 3.17.0
/// </summary>
public readonly partial struct PositionEncodingKind : IEquatable<PositionEncodingKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>PositionEncodingKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public PositionEncodingKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator PositionEncodingKind(string value) => new(value);

    public static explicit operator string(PositionEncodingKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(PositionEncodingKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is PositionEncodingKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(PositionEncodingKind left, PositionEncodingKind right) => left.Equals(right);

    public static bool operator !=(PositionEncodingKind left, PositionEncodingKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m PositionEncodingKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>PositionEncodingKindUTF8 PositionEncodingKind = "utf-8"</c>（lsp_generated.go:10243）。
    /// Character offsets count UTF-8 code units (e.g. bytes).
    /// </summary>
    public static readonly PositionEncodingKind UTF8 = new("utf-8");

    /// <summary>Go: <c>PositionEncodingKindUTF16 PositionEncodingKind = "utf-16"</c>（lsp_generated.go:10243）。
    /// Character offsets count UTF-16 code units.
    /// 
    /// This is the default and must always be supported
    /// by servers
    /// </summary>
    public static readonly PositionEncodingKind UTF16 = new("utf-16");

    /// <summary>Go: <c>PositionEncodingKindUTF32 PositionEncodingKind = "utf-32"</c>（lsp_generated.go:10243）。
    /// Character offsets count UTF-32 code units.
    /// 
    /// Implementation note: these are the same as Unicode codepoints,
    /// so this `PositionEncodingKind` may also be used for an
    /// encoding-agnostic representation of character offsets.
    /// </summary>
    public static readonly PositionEncodingKind UTF32 = new("utf-32");
}

/// <summary>Go: <c>type FileChangeType uint32</c>（lsp_generated.go:10260）。
/// The file event type
/// </summary>
public enum FileChangeType : uint
{
    /// The file got created.
    /// <summary>Go: <c>FileChangeTypeCreated FileChangeType = 1</c>（lsp_generated.go:10262）。</summary>
    Created = 1,
    /// The file got changed.
    /// <summary>Go: <c>FileChangeTypeChanged FileChangeType = 2</c>（lsp_generated.go:10262）。</summary>
    Changed = 2,
    /// The file got deleted.
    /// <summary>Go: <c>FileChangeTypeDeleted FileChangeType = 3</c>（lsp_generated.go:10262）。</summary>
    Deleted = 3
}

/// <summary>Go: <c>type WatchKind uint32</c>（lsp_generated.go:10283）。
/// </summary>
public enum WatchKind : uint
{
    /// Interested in create events.
    /// <summary>Go: <c>WatchKindCreate WatchKind = 1</c>（lsp_generated.go:10285）。</summary>
    Create = 1,
    /// Interested in change events
    /// <summary>Go: <c>WatchKindChange WatchKind = 2</c>（lsp_generated.go:10285）。</summary>
    Change = 2,
    /// Interested in delete events
    /// <summary>Go: <c>WatchKindDelete WatchKind = 4</c>（lsp_generated.go:10285）。</summary>
    Delete = 4
}

/// <summary>Go: <c>type DiagnosticSeverity uint32</c>（lsp_generated.go:10319）。
/// The diagnostic's severity.
/// </summary>
public enum DiagnosticSeverity : uint
{
    /// Reports an error.
    /// <summary>Go: <c>DiagnosticSeverityError DiagnosticSeverity = 1</c>（lsp_generated.go:10321）。</summary>
    Error = 1,
    /// Reports a warning.
    /// <summary>Go: <c>DiagnosticSeverityWarning DiagnosticSeverity = 2</c>（lsp_generated.go:10321）。</summary>
    Warning = 2,
    /// Reports an information.
    /// <summary>Go: <c>DiagnosticSeverityInformation DiagnosticSeverity = 3</c>（lsp_generated.go:10321）。</summary>
    Information = 3,
    /// Reports a hint.
    /// <summary>Go: <c>DiagnosticSeverityHint DiagnosticSeverity = 4</c>（lsp_generated.go:10321）。</summary>
    Hint = 4
}

/// <summary>Go: <c>type DiagnosticTag uint32</c>（lsp_generated.go:10347）。
/// The diagnostic tags.
/// 
/// Since: 3.15.0
/// </summary>
public enum DiagnosticTag : uint
{
    /// Unused or unnecessary code.
    /// 
    /// Clients are allowed to render diagnostics with this tag faded out instead of having
    /// an error squiggle.
    /// <summary>Go: <c>DiagnosticTagUnnecessary DiagnosticTag = 1</c>（lsp_generated.go:10349）。</summary>
    Unnecessary = 1,
    /// Deprecated or obsolete code.
    /// 
    /// Clients are allowed to rendered diagnostics with this tag strike through.
    /// <summary>Go: <c>DiagnosticTagDeprecated DiagnosticTag = 2</c>（lsp_generated.go:10349）。</summary>
    Deprecated = 2
}

/// <summary>Go: <c>type CompletionTriggerKind uint32</c>（lsp_generated.go:10374）。
/// How a completion was triggered
/// </summary>
public enum CompletionTriggerKind : uint
{
    /// Completion was triggered by typing an identifier (24x7 code
    /// complete), manual invocation (e.g Ctrl+Space) or via API.
    /// <summary>Go: <c>CompletionTriggerKindInvoked CompletionTriggerKind = 1</c>（lsp_generated.go:10376）。</summary>
    Invoked = 1,
    /// Completion was triggered by a trigger character specified by
    /// the `triggerCharacters` properties of the `CompletionRegistrationOptions`.
    /// <summary>Go: <c>CompletionTriggerKindTriggerCharacter CompletionTriggerKind = 2</c>（lsp_generated.go:10376）。</summary>
    TriggerCharacter = 2,
    /// Completion was re-triggered as current completion list is incomplete
    /// <summary>Go: <c>CompletionTriggerKindTriggerForIncompleteCompletions CompletionTriggerKind = 3</c>（lsp_generated.go:10376）。</summary>
    TriggerForIncompleteCompletions = 3
}

/// <summary>Go: <c>type ApplyKind uint32</c>（lsp_generated.go:10403）。
/// Defines how values from a set of defaults and an individual item will be
/// merged.
/// 
/// Since: 3.18.0
/// </summary>
public enum ApplyKind : uint
{
    /// The value from the individual item (if provided and not `null`) will be
    /// used instead of the default.
    /// <summary>Go: <c>ApplyKindReplace ApplyKind = 1</c>（lsp_generated.go:10405）。</summary>
    Replace = 1,
    /// The value from the item will be merged with the default.
    /// 
    /// The specific rules for mergeing values are defined against each field
    /// that supports merging.
    /// <summary>Go: <c>ApplyKindMerge ApplyKind = 2</c>（lsp_generated.go:10405）。</summary>
    Merge = 2
}

/// <summary>Go: <c>type SignatureHelpTriggerKind uint32</c>（lsp_generated.go:10431）。
/// How a signature help was triggered.
/// 
/// Since: 3.15.0
/// </summary>
public enum SignatureHelpTriggerKind : uint
{
    /// Signature help was invoked manually by the user or by a command.
    /// <summary>Go: <c>SignatureHelpTriggerKindInvoked SignatureHelpTriggerKind = 1</c>（lsp_generated.go:10433）。</summary>
    Invoked = 1,
    /// Signature help was triggered by a trigger character.
    /// <summary>Go: <c>SignatureHelpTriggerKindTriggerCharacter SignatureHelpTriggerKind = 2</c>（lsp_generated.go:10433）。</summary>
    TriggerCharacter = 2,
    /// Signature help was triggered by the cursor moving or by the document content changing.
    /// <summary>Go: <c>SignatureHelpTriggerKindContentChange SignatureHelpTriggerKind = 3</c>（lsp_generated.go:10433）。</summary>
    ContentChange = 3
}

/// <summary>Go: <c>type CodeActionTriggerKind uint32</c>（lsp_generated.go:10457）。
/// The reason why code actions were requested.
/// 
/// Since: 3.17.0
/// </summary>
public enum CodeActionTriggerKind : uint
{
    /// Code actions were explicitly requested by the user or by an extension.
    /// <summary>Go: <c>CodeActionTriggerKindInvoked CodeActionTriggerKind = 1</c>（lsp_generated.go:10459）。</summary>
    Invoked = 1,
    /// Code actions were requested automatically.
    /// 
    /// This typically happens when current selection in a file changes, but can
    /// also be triggered when file content changes.
    /// <summary>Go: <c>CodeActionTriggerKindAutomatic CodeActionTriggerKind = 2</c>（lsp_generated.go:10459）。</summary>
    Automatic = 2
}

/// <summary>Go: <c>type FileOperationPatternKind string</c>（lsp_generated.go:10485）。
/// A pattern kind describing if a glob pattern matches a file a folder or
/// both.
/// 
/// Since: 3.16.0
/// </summary>
public readonly partial struct FileOperationPatternKind : IEquatable<FileOperationPatternKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>FileOperationPatternKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public FileOperationPatternKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator FileOperationPatternKind(string value) => new(value);

    public static explicit operator string(FileOperationPatternKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(FileOperationPatternKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FileOperationPatternKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(FileOperationPatternKind left, FileOperationPatternKind right) => left.Equals(right);

    public static bool operator !=(FileOperationPatternKind left, FileOperationPatternKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m FileOperationPatternKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>FileOperationPatternKindFile FileOperationPatternKind = "file"</c>（lsp_generated.go:10487）。
    /// The pattern matches a file only.
    /// </summary>
    public static readonly FileOperationPatternKind File = new("file");

    /// <summary>Go: <c>FileOperationPatternKindFolder FileOperationPatternKind = "folder"</c>（lsp_generated.go:10487）。
    /// The pattern matches a folder only.
    /// </summary>
    public static readonly FileOperationPatternKind Folder = new("folder");
}

/// <summary>Go: <c>type ResourceOperationKind string</c>（lsp_generated.go:10494）。
/// </summary>
public readonly partial struct ResourceOperationKind : IEquatable<ResourceOperationKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>ResourceOperationKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public ResourceOperationKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator ResourceOperationKind(string value) => new(value);

    public static explicit operator string(ResourceOperationKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(ResourceOperationKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ResourceOperationKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(ResourceOperationKind left, ResourceOperationKind right) => left.Equals(right);

    public static bool operator !=(ResourceOperationKind left, ResourceOperationKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m ResourceOperationKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>ResourceOperationKindCreate ResourceOperationKind = "create"</c>（lsp_generated.go:10496）。
    /// Supports creating new files and folders.
    /// </summary>
    public static readonly ResourceOperationKind Create = new("create");

    /// <summary>Go: <c>ResourceOperationKindRename ResourceOperationKind = "rename"</c>（lsp_generated.go:10496）。
    /// Supports renaming existing files and folders.
    /// </summary>
    public static readonly ResourceOperationKind Rename = new("rename");

    /// <summary>Go: <c>ResourceOperationKindDelete ResourceOperationKind = "delete"</c>（lsp_generated.go:10496）。
    /// Supports deleting existing files and folders.
    /// </summary>
    public static readonly ResourceOperationKind Delete = new("delete");
}

/// <summary>Go: <c>type FailureHandlingKind string</c>（lsp_generated.go:10505）。
/// </summary>
public readonly partial struct FailureHandlingKind : IEquatable<FailureHandlingKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>FailureHandlingKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public FailureHandlingKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator FailureHandlingKind(string value) => new(value);

    public static explicit operator string(FailureHandlingKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(FailureHandlingKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FailureHandlingKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(FailureHandlingKind left, FailureHandlingKind right) => left.Equals(right);

    public static bool operator !=(FailureHandlingKind left, FailureHandlingKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m FailureHandlingKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>FailureHandlingKindAbort FailureHandlingKind = "abort"</c>（lsp_generated.go:10507）。
    /// Applying the workspace change is simply aborted if one of the changes provided
    /// fails. All operations executed before the failing operation stay executed.
    /// </summary>
    public static readonly FailureHandlingKind Abort = new("abort");

    /// <summary>Go: <c>FailureHandlingKindTransactional FailureHandlingKind = "transactional"</c>（lsp_generated.go:10507）。
    /// All operations are executed transactional. That means they either all
    /// succeed or no changes at all are applied to the workspace.
    /// </summary>
    public static readonly FailureHandlingKind Transactional = new("transactional");

    /// <summary>Go: <c>FailureHandlingKindTextOnlyTransactional FailureHandlingKind = "textOnlyTransactional"</c>（lsp_generated.go:10507）。
    /// If the workspace edit contains only textual file changes they are executed transactional.
    /// If resource changes (create, rename or delete file) are part of the change the failure
    /// handling strategy is abort.
    /// </summary>
    public static readonly FailureHandlingKind TextOnlyTransactional = new("textOnlyTransactional");

    /// <summary>Go: <c>FailureHandlingKindUndo FailureHandlingKind = "undo"</c>（lsp_generated.go:10507）。
    /// The client tries to undo the operations already executed. But there is no
    /// guarantee that this is succeeding.
    /// </summary>
    public static readonly FailureHandlingKind Undo = new("undo");
}

/// <summary>Go: <c>type PrepareSupportDefaultBehavior uint32</c>（lsp_generated.go:10523）。
/// </summary>
public enum PrepareSupportDefaultBehavior : uint
{
    /// The client's default behavior is to select the identifier
    /// according the to language's syntax rule.
    /// <summary>Go: <c>PrepareSupportDefaultBehaviorIdentifier PrepareSupportDefaultBehavior = 1</c>（lsp_generated.go:10525）。</summary>
    Identifier = 1
}

/// <summary>Go: <c>type TokenFormat string</c>（lsp_generated.go:10543）。
/// </summary>
public readonly partial struct TokenFormat : IEquatable<TokenFormat>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>TokenFormat(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public TokenFormat(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator TokenFormat(string value) => new(value);

    public static explicit operator string(TokenFormat value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(TokenFormat other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TokenFormat other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(TokenFormat left, TokenFormat right) => left.Equals(right);

    public static bool operator !=(TokenFormat left, TokenFormat right) => !left.Equals(right);

    /// <summary>Go: <c>func (m TokenFormat) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>TokenFormatRelative TokenFormat = "relative"</c>（lsp_generated.go:10545）。
    /// </summary>
    public static readonly TokenFormat Relative = new("relative");
}

/// <summary>Go: <c>type VSContainerElementStyle int32</c>（lsp_generated.go:10550）。
/// Layout style for a VSContainerElement's children, mirroring VS's Microsoft.VisualStudio.Text.Adornments.ContainerElementStyle.
/// </summary>
public enum VSContainerElementStyle : int
{
    /// Child elements are laid out inline, wrapping as needed (e.g. an icon next to a signature line).
    /// <summary>Go: <c>VSContainerElementStyleWrapped VSContainerElementStyle = 0</c>（lsp_generated.go:10552）。</summary>
    Wrapped = 0,
    /// Child elements are stacked vertically, each on its own line (e.g. a signature line followed by documentation).
    /// <summary>Go: <c>VSContainerElementStyleStacked VSContainerElementStyle = 1</c>（lsp_generated.go:10552）。</summary>
    Stacked = 1
}

/// <summary>Go: <c>type LogVerbosity int32</c>（lsp_generated.go:10572）。
/// Log verbosity level, mirroring the VS Code LogLevel enum values.
/// </summary>
public enum LogVerbosity : int
{
    /// All logging disabled.
    /// <summary>Go: <c>LogVerbosityOff LogVerbosity = 0</c>（lsp_generated.go:10574）。</summary>
    Off = 0,
    /// Most verbose; includes LSP request/response traces.
    /// <summary>Go: <c>LogVerbosityTrace LogVerbosity = 1</c>（lsp_generated.go:10574）。</summary>
    Trace = 1,
    /// Verbose server logs.
    /// <summary>Go: <c>LogVerbosityDebug LogVerbosity = 2</c>（lsp_generated.go:10574）。</summary>
    Debug = 2,
    /// Normal server logs.
    /// <summary>Go: <c>LogVerbosityInfo LogVerbosity = 3</c>（lsp_generated.go:10574）。</summary>
    Info = 3,
    /// Warnings only.
    /// <summary>Go: <c>LogVerbosityWarning LogVerbosity = 4</c>（lsp_generated.go:10574）。</summary>
    Warning = 4,
    /// Errors only.
    /// <summary>Go: <c>LogVerbosityError LogVerbosity = 5</c>（lsp_generated.go:10574）。</summary>
    Error = 5
}

/// <summary>Go: <c>type DiagnosticFlakeLogLevel int32</c>（lsp_generated.go:10602）。
/// Behavior for tracking and logging flaky diagnostics.
/// </summary>
public enum DiagnosticFlakeLogLevel : int
{
    /// All flake logging disabled.
    /// <summary>Go: <c>DiagnosticFlakeLogLevelOff DiagnosticFlakeLogLevel = 0</c>（lsp_generated.go:10604）。</summary>
    Off = 0,
    /// Log flaky diagnostics to the error log.
    /// <summary>Go: <c>DiagnosticFlakeLogLevelLog DiagnosticFlakeLogLevel = 1</c>（lsp_generated.go:10604）。</summary>
    Log = 1,
    /// Panic on flaky diagnostics.
    /// <summary>Go: <c>DiagnosticFlakeLogLevelPanic DiagnosticFlakeLogLevel = 2</c>（lsp_generated.go:10604）。</summary>
    Panic = 2
}

/// <summary>Go: <c>type VSReferenceKind int32</c>（lsp_generated.go:10625）。
/// </summary>
public enum VSReferenceKind : int
{
    /// <summary>Go: <c>VSReferenceKindInactive VSReferenceKind = 0</c>（lsp_generated.go:10627）。</summary>
    Inactive = 0,
    /// <summary>Go: <c>VSReferenceKindComment VSReferenceKind = 1</c>（lsp_generated.go:10627）。</summary>
    Comment = 1,
    /// <summary>Go: <c>VSReferenceKindString VSReferenceKind = 2</c>（lsp_generated.go:10627）。</summary>
    String = 2,
    /// <summary>Go: <c>VSReferenceKindRead VSReferenceKind = 3</c>（lsp_generated.go:10627）。</summary>
    Read = 3,
    /// <summary>Go: <c>VSReferenceKindWrite VSReferenceKind = 4</c>（lsp_generated.go:10627）。</summary>
    Write = 4,
    /// <summary>Go: <c>VSReferenceKindReference VSReferenceKind = 5</c>（lsp_generated.go:10627）。</summary>
    Reference = 5,
    /// <summary>Go: <c>VSReferenceKindName VSReferenceKind = 6</c>（lsp_generated.go:10627）。</summary>
    Name = 6,
    /// <summary>Go: <c>VSReferenceKindQualified VSReferenceKind = 7</c>（lsp_generated.go:10627）。</summary>
    Qualified = 7,
    /// <summary>Go: <c>VSReferenceKindTypeArgument VSReferenceKind = 8</c>（lsp_generated.go:10627）。</summary>
    TypeArgument = 8,
    /// <summary>Go: <c>VSReferenceKindTypeConstraint VSReferenceKind = 9</c>（lsp_generated.go:10627）。</summary>
    TypeConstraint = 9,
    /// <summary>Go: <c>VSReferenceKindBaseType VSReferenceKind = 10</c>（lsp_generated.go:10627）。</summary>
    BaseType = 10,
    /// <summary>Go: <c>VSReferenceKindConstructor VSReferenceKind = 11</c>（lsp_generated.go:10627）。</summary>
    Constructor = 11,
    /// <summary>Go: <c>VSReferenceKindDestructor VSReferenceKind = 12</c>（lsp_generated.go:10627）。</summary>
    Destructor = 12,
    /// <summary>Go: <c>VSReferenceKindImport VSReferenceKind = 13</c>（lsp_generated.go:10627）。</summary>
    Import = 13,
    /// <summary>Go: <c>VSReferenceKindDeclaration VSReferenceKind = 14</c>（lsp_generated.go:10627）。</summary>
    Declaration = 14,
    /// <summary>Go: <c>VSReferenceKindAddressOf VSReferenceKind = 15</c>（lsp_generated.go:10627）。</summary>
    AddressOf = 15,
    /// <summary>Go: <c>VSReferenceKindNotReference VSReferenceKind = 16</c>（lsp_generated.go:10627）。</summary>
    NotReference = 16,
    /// <summary>Go: <c>VSReferenceKindUnknown VSReferenceKind = 17</c>（lsp_generated.go:10627）。</summary>
    Unknown = 17
}

/// <summary>Go: <c>type CodeLensKind string</c>（lsp_generated.go:10660）。
/// </summary>
public readonly partial struct CodeLensKind : IEquatable<CodeLensKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>CodeLensKind(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public CodeLensKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator CodeLensKind(string value) => new(value);

    public static explicit operator string(CodeLensKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(CodeLensKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CodeLensKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(CodeLensKind left, CodeLensKind right) => left.Equals(right);

    public static bool operator !=(CodeLensKind left, CodeLensKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (m CodeLensKind) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>CodeLensKindReferences CodeLensKind = "references"</c>（lsp_generated.go:10662）。
    /// </summary>
    public static readonly CodeLensKind References = new("references");

    /// <summary>Go: <c>CodeLensKindImplementations CodeLensKind = "implementations"</c>（lsp_generated.go:10662）。
    /// </summary>
    public static readonly CodeLensKind Implementations = new("implementations");
}

/// <summary>Go: <c>type AutoImportFixKind int32</c>（lsp_generated.go:10667）。
/// </summary>
public enum AutoImportFixKind : int
{
    /// Augment an existing namespace import.
    /// <summary>Go: <c>AutoImportFixKindUseNamespace AutoImportFixKind = 0</c>（lsp_generated.go:10669）。</summary>
    UseNamespace = 0,
    /// Add a JSDoc-only type import.
    /// <summary>Go: <c>AutoImportFixKindJsdocTypeImport AutoImportFixKind = 1</c>（lsp_generated.go:10669）。</summary>
    JsdocTypeImport = 1,
    /// Insert into an existing import declaration.
    /// <summary>Go: <c>AutoImportFixKindAddToExisting AutoImportFixKind = 2</c>（lsp_generated.go:10669）。</summary>
    AddToExisting = 2,
    /// Create a fresh import statement.
    /// <summary>Go: <c>AutoImportFixKindAddNew AutoImportFixKind = 3</c>（lsp_generated.go:10669）。</summary>
    AddNew = 3,
    /// Promote a type-only import when necessary.
    /// <summary>Go: <c>AutoImportFixKindPromoteTypeOnly AutoImportFixKind = 4</c>（lsp_generated.go:10669）。</summary>
    PromoteTypeOnly = 4
}

/// <summary>Go: <c>type ImportKind int32</c>（lsp_generated.go:10694）。
/// </summary>
public enum ImportKind : int
{
    /// Adds a named import.
    /// <summary>Go: <c>ImportKindNamed ImportKind = 0</c>（lsp_generated.go:10696）。</summary>
    Named = 0,
    /// Adds a default import.
    /// <summary>Go: <c>ImportKindDefault ImportKind = 1</c>（lsp_generated.go:10696）。</summary>
    Default = 1,
    /// Adds a namespace import.
    /// <summary>Go: <c>ImportKindNamespace ImportKind = 2</c>（lsp_generated.go:10696）。</summary>
    Namespace = 2,
    /// Adds a CommonJS import assignment.
    /// <summary>Go: <c>ImportKindCommonJS ImportKind = 3</c>（lsp_generated.go:10696）。</summary>
    CommonJS = 3
}

/// <summary>Go: <c>type AddAsTypeOnly int32</c>（lsp_generated.go:10719）。
/// </summary>
public enum AddAsTypeOnly : int
{
    /// Import may be marked type-only if needed.
    /// <summary>Go: <c>AddAsTypeOnlyAllowed AddAsTypeOnly = 1</c>（lsp_generated.go:10721）。</summary>
    Allowed = 1,
    /// Import must be marked type-only.
    /// <summary>Go: <c>AddAsTypeOnlyRequired AddAsTypeOnly = 2</c>（lsp_generated.go:10721）。</summary>
    Required = 2,
    /// Import cannot be marked type-only.
    /// <summary>Go: <c>AddAsTypeOnlyNotAllowed AddAsTypeOnly = 4</c>（lsp_generated.go:10721）。</summary>
    NotAllowed = 4
}

/// <summary>Go: <c>type ClassificationTypeName string</c>（lsp_generated.go:10750）。
/// Roslyn classification type names used by VS for syntax coloring in tooltips and other UI elements.
/// </summary>
public readonly partial struct ClassificationTypeName : IEquatable<ClassificationTypeName>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>ClassificationTypeName(value)</c> 转换（Go 里是显式转换；这里入向 implicit、出向 explicit）。</summary>
    public ClassificationTypeName(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator ClassificationTypeName(string value) => new(value);

    public static explicit operator string(ClassificationTypeName value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(ClassificationTypeName other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ClassificationTypeName other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(ClassificationTypeName left, ClassificationTypeName right) => left.Equals(right);

    public static bool operator !=(ClassificationTypeName left, ClassificationTypeName right) => !left.Equals(right);

    /// <summary>Go: <c>func (m ClassificationTypeName) MarshalJSONTo(enc *json.Encoder) error</c>：Go 的命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>ClassificationTypeNameKeyword ClassificationTypeName = "keyword"</c>（lsp_generated.go:10752）。
    /// Language keyword (e.g., function, const, class).
    /// </summary>
    public static readonly ClassificationTypeName Keyword = new("keyword");

    /// <summary>Go: <c>ClassificationTypeNamePunctuation ClassificationTypeName = "punctuation"</c>（lsp_generated.go:10752）。
    /// Punctuation characters (e.g., parentheses, commas, semicolons).
    /// </summary>
    public static readonly ClassificationTypeName Punctuation = new("punctuation");

    /// <summary>Go: <c>ClassificationTypeNameOperator ClassificationTypeName = "operator"</c>（lsp_generated.go:10752）。
    /// Operators (e.g., =, +, ?).
    /// </summary>
    public static readonly ClassificationTypeName Operator = new("operator");

    /// <summary>Go: <c>ClassificationTypeNameWhiteSpace ClassificationTypeName = "whitespace"</c>（lsp_generated.go:10752）。
    /// Whitespace including spaces and line breaks.
    /// </summary>
    public static readonly ClassificationTypeName WhiteSpace = new("whitespace");

    /// <summary>Go: <c>ClassificationTypeNameText ClassificationTypeName = "text"</c>（lsp_generated.go:10752）。
    /// Plain text with no special classification.
    /// </summary>
    public static readonly ClassificationTypeName Text = new("text");

    /// <summary>Go: <c>ClassificationTypeNameString ClassificationTypeName = "string"</c>（lsp_generated.go:10752）。
    /// String and literal values.
    /// </summary>
    public static readonly ClassificationTypeName String = new("string");

    /// <summary>Go: <c>ClassificationTypeNameNumber ClassificationTypeName = "number"</c>（lsp_generated.go:10752）。
    /// Numeric literal values.
    /// </summary>
    public static readonly ClassificationTypeName Number = new("number");

    /// <summary>Go: <c>ClassificationTypeNameComment ClassificationTypeName = "comment"</c>（lsp_generated.go:10752）。
    /// Comment text.
    /// </summary>
    public static readonly ClassificationTypeName Comment = new("comment");

    /// <summary>Go: <c>ClassificationTypeNameClassName ClassificationTypeName = "class name"</c>（lsp_generated.go:10752）。
    /// Class names.
    /// </summary>
    public static readonly ClassificationTypeName ClassName = new("class name");

    /// <summary>Go: <c>ClassificationTypeNameInterfaceName ClassificationTypeName = "interface name"</c>（lsp_generated.go:10752）。
    /// Interface names.
    /// </summary>
    public static readonly ClassificationTypeName InterfaceName = new("interface name");

    /// <summary>Go: <c>ClassificationTypeNameEnumName ClassificationTypeName = "enum name"</c>（lsp_generated.go:10752）。
    /// Enum names.
    /// </summary>
    public static readonly ClassificationTypeName EnumName = new("enum name");

    /// <summary>Go: <c>ClassificationTypeNameModuleName ClassificationTypeName = "module name"</c>（lsp_generated.go:10752）。
    /// Module/namespace names.
    /// </summary>
    public static readonly ClassificationTypeName ModuleName = new("module name");

    /// <summary>Go: <c>ClassificationTypeNameMethodName ClassificationTypeName = "method name"</c>（lsp_generated.go:10752）。
    /// Method and function names.
    /// </summary>
    public static readonly ClassificationTypeName MethodName = new("method name");

    /// <summary>Go: <c>ClassificationTypeNameParameterName ClassificationTypeName = "parameter name"</c>（lsp_generated.go:10752）。
    /// Parameter names.
    /// </summary>
    public static readonly ClassificationTypeName ParameterName = new("parameter name");

    /// <summary>Go: <c>ClassificationTypeNamePropertyName ClassificationTypeName = "property name"</c>（lsp_generated.go:10752）。
    /// Property and accessor names.
    /// </summary>
    public static readonly ClassificationTypeName PropertyName = new("property name");

    /// <summary>Go: <c>ClassificationTypeNameFieldName ClassificationTypeName = "field name"</c>（lsp_generated.go:10752）。
    /// Field names (e.g., enum members).
    /// </summary>
    public static readonly ClassificationTypeName FieldName = new("field name");

    /// <summary>Go: <c>ClassificationTypeNameLocalName ClassificationTypeName = "local name"</c>（lsp_generated.go:10752）。
    /// Local variable names.
    /// </summary>
    public static readonly ClassificationTypeName LocalName = new("local name");

    /// <summary>Go: <c>ClassificationTypeNameTypeParameterName ClassificationTypeName = "type parameter name"</c>（lsp_generated.go:10752）。
    /// Type parameter names.
    /// </summary>
    public static readonly ClassificationTypeName TypeParameterName = new("type parameter name");

    /// <summary>Go: <c>ClassificationTypeNameIdentifier ClassificationTypeName = "identifier"</c>（lsp_generated.go:10752）。
    /// General identifiers (e.g., type aliases, imports).
    /// </summary>
    public static readonly ClassificationTypeName Identifier = new("identifier");
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _errorCodeName = "RequestFailedServerCancelledContentModifiedRequestCancelledParseErrorInternalErrorInvalidParamsMethodNotFoundInvalidRequestServerNotInitializedUnknownErrorCode";

    private static readonly ushort[] _errorCodeIndex0 = [0, 13, 28, 43, 59];

    private static readonly ushort[] _errorCodeIndex2 = [0, 13, 26, 40, 54];

    private static readonly ushort[] _errorCodeIndex3 = [0, 20, 36];

    /// <summary>Go: <c>func (e ErrorCode) String() string</c>（lsp_generated.go:9633）。</summary>
    public static string String(this ErrorCode value)
    {
        if (-32803 <= (int)value && (int)value <= -32800)
        {
            var i = (int)value - -32803;
            return _errorCodeName.Substring(0 + _errorCodeIndex0[i], (0 + _errorCodeIndex0[i + 1]) - (0 + _errorCodeIndex0[i]));
        }

        if ((int)value == -32700)
        {
            return _errorCodeName.Substring(59, 69 - 59);
        }

        if (-32603 <= (int)value && (int)value <= -32600)
        {
            var i = (int)value - -32603;
            return _errorCodeName.Substring(69 + _errorCodeIndex2[i], (69 + _errorCodeIndex2[i + 1]) - (69 + _errorCodeIndex2[i]));
        }

        if (-32002 <= (int)value && (int)value <= -32001)
        {
            var i = (int)value - -32002;
            return _errorCodeName.Substring(123 + _errorCodeIndex3[i], (123 + _errorCodeIndex3[i + 1]) - (123 + _errorCodeIndex3[i]));
        }

        return $"ErrorCode({(int)value})";
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _symbolKindName = "FileModuleNamespacePackageClassMethodPropertyFieldConstructorEnumInterfaceFunctionVariableConstantStringNumberBooleanArrayObjectKeyNullEnumMemberStructEventOperatorTypeParameter";

    private static readonly ushort[] _symbolKindIndex = [0, 4, 10, 19, 26, 31, 37, 45, 50, 61, 65, 74, 82, 90, 98, 104, 110, 117, 122, 128, 131, 135, 145, 151, 156, 164, 177];

    /// <summary>Go: <c>func (e SymbolKind) String() string</c>（lsp_generated.go:9703）。</summary>
    public static string String(this SymbolKind value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _symbolKindIndex.Length - 1)
        {
            return $"SymbolKind({(uint)value})";
        }

        return _symbolKindName.Substring(_symbolKindIndex[i], _symbolKindIndex[i + 1] - _symbolKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _symbolTagName = "Deprecated";

    private static readonly ushort[] _symbolTagIndex = [0, 10];

    /// <summary>Go: <c>func (e SymbolTag) String() string</c>（lsp_generated.go:9725）。</summary>
    public static string String(this SymbolTag value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _symbolTagIndex.Length - 1)
        {
            return $"SymbolTag({(uint)value})";
        }

        return _symbolTagName.Substring(_symbolTagIndex[i], _symbolTagIndex[i + 1] - _symbolTagIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _inlayHintKindName = "TypeParameter";

    private static readonly ushort[] _inlayHintKindIndex = [0, 4, 13];

    /// <summary>Go: <c>func (e InlayHintKind) String() string</c>（lsp_generated.go:9782）。</summary>
    public static string String(this InlayHintKind value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _inlayHintKindIndex.Length - 1)
        {
            return $"InlayHintKind({(uint)value})";
        }

        return _inlayHintKindName.Substring(_inlayHintKindIndex[i], _inlayHintKindIndex[i + 1] - _inlayHintKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _messageTypeName = "ErrorWarningInfoLogDebug";

    private static readonly ushort[] _messageTypeIndex = [0, 5, 12, 16, 19, 24];

    /// <summary>Go: <c>func (e MessageType) String() string</c>（lsp_generated.go:9812）。</summary>
    public static string String(this MessageType value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _messageTypeIndex.Length - 1)
        {
            return $"MessageType({(uint)value})";
        }

        return _messageTypeName.Substring(_messageTypeIndex[i], _messageTypeIndex[i + 1] - _messageTypeIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _textDocumentSyncKindName = "NoneFullIncremental";

    private static readonly ushort[] _textDocumentSyncKindIndex = [0, 4, 8, 19];

    /// <summary>Go: <c>func (e TextDocumentSyncKind) String() string</c>（lsp_generated.go:9840）。</summary>
    public static string String(this TextDocumentSyncKind value)
    {
        var i = (int)value - 0;
        if (i < 0 || i >= _textDocumentSyncKindIndex.Length - 1)
        {
            return $"TextDocumentSyncKind({(uint)value})";
        }

        return _textDocumentSyncKindName.Substring(_textDocumentSyncKindIndex[i], _textDocumentSyncKindIndex[i + 1] - _textDocumentSyncKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _textDocumentSaveReasonName = "ManualAfterDelayFocusOut";

    private static readonly ushort[] _textDocumentSaveReasonIndex = [0, 6, 16, 24];

    /// <summary>Go: <c>func (e TextDocumentSaveReason) String() string</c>（lsp_generated.go:9865）。</summary>
    public static string String(this TextDocumentSaveReason value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _textDocumentSaveReasonIndex.Length - 1)
        {
            return $"TextDocumentSaveReason({(uint)value})";
        }

        return _textDocumentSaveReasonName.Substring(_textDocumentSaveReasonIndex[i], _textDocumentSaveReasonIndex[i + 1] - _textDocumentSaveReasonIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _completionItemKindName = "TextMethodFunctionConstructorFieldVariableClassInterfaceModulePropertyUnitValueEnumKeywordSnippetColorFileReferenceFolderEnumMemberConstantStructEventOperatorTypeParameter";

    private static readonly ushort[] _completionItemKindIndex = [0, 4, 10, 18, 29, 34, 42, 47, 56, 62, 70, 74, 79, 83, 90, 97, 102, 106, 115, 121, 131, 139, 145, 150, 158, 171];

    /// <summary>Go: <c>func (e CompletionItemKind) String() string</c>（lsp_generated.go:9908）。</summary>
    public static string String(this CompletionItemKind value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _completionItemKindIndex.Length - 1)
        {
            return $"CompletionItemKind({(uint)value})";
        }

        return _completionItemKindName.Substring(_completionItemKindIndex[i], _completionItemKindIndex[i + 1] - _completionItemKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _completionItemTagName = "Deprecated";

    private static readonly ushort[] _completionItemTagIndex = [0, 10];

    /// <summary>Go: <c>func (e CompletionItemTag) String() string</c>（lsp_generated.go:9931）。</summary>
    public static string String(this CompletionItemTag value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _completionItemTagIndex.Length - 1)
        {
            return $"CompletionItemTag({(uint)value})";
        }

        return _completionItemTagName.Substring(_completionItemTagIndex[i], _completionItemTagIndex[i + 1] - _completionItemTagIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _insertTextFormatName = "PlainTextSnippet";

    private static readonly ushort[] _insertTextFormatIndex = [0, 9, 16];

    /// <summary>Go: <c>func (e InsertTextFormat) String() string</c>（lsp_generated.go:9961）。</summary>
    public static string String(this InsertTextFormat value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _insertTextFormatIndex.Length - 1)
        {
            return $"InsertTextFormat({(uint)value})";
        }

        return _insertTextFormatName.Substring(_insertTextFormatIndex[i], _insertTextFormatIndex[i + 1] - _insertTextFormatIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _insertTextModeName = "asIsadjustIndentation";

    private static readonly ushort[] _insertTextModeIndex = [0, 4, 21];

    /// <summary>Go: <c>func (e InsertTextMode) String() string</c>（lsp_generated.go:9996）。</summary>
    public static string String(this InsertTextMode value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _insertTextModeIndex.Length - 1)
        {
            return $"InsertTextMode({(uint)value})";
        }

        return _insertTextModeName.Substring(_insertTextModeIndex[i], _insertTextModeIndex[i + 1] - _insertTextModeIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _documentHighlightKindName = "TextReadWrite";

    private static readonly ushort[] _documentHighlightKindIndex = [0, 4, 8, 13];

    /// <summary>Go: <c>func (e DocumentHighlightKind) String() string</c>（lsp_generated.go:10020）。</summary>
    public static string String(this DocumentHighlightKind value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _documentHighlightKindIndex.Length - 1)
        {
            return $"DocumentHighlightKind({(uint)value})";
        }

        return _documentHighlightKindName.Substring(_documentHighlightKindIndex[i], _documentHighlightKindIndex[i + 1] - _documentHighlightKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _codeActionTagName = "LLMGenerated";

    private static readonly ushort[] _codeActionTagIndex = [0, 12];

    /// <summary>Go: <c>func (e CodeActionTag) String() string</c>（lsp_generated.go:10108）。</summary>
    public static string String(this CodeActionTag value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _codeActionTagIndex.Length - 1)
        {
            return $"CodeActionTag({(uint)value})";
        }

        return _codeActionTagName.Substring(_codeActionTagIndex[i], _codeActionTagIndex[i + 1] - _codeActionTagIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _inlineCompletionTriggerKindName = "InvokedAutomatic";

    private static readonly ushort[] _inlineCompletionTriggerKindIndex = [0, 7, 16];

    /// <summary>Go: <c>func (e InlineCompletionTriggerKind) String() string</c>（lsp_generated.go:10230）。</summary>
    public static string String(this InlineCompletionTriggerKind value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _inlineCompletionTriggerKindIndex.Length - 1)
        {
            return $"InlineCompletionTriggerKind({(uint)value})";
        }

        return _inlineCompletionTriggerKindName.Substring(_inlineCompletionTriggerKindIndex[i], _inlineCompletionTriggerKindIndex[i + 1] - _inlineCompletionTriggerKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _fileChangeTypeName = "CreatedChangedDeleted";

    private static readonly ushort[] _fileChangeTypeIndex = [0, 7, 14, 21];

    /// <summary>Go: <c>func (e FileChangeType) String() string</c>（lsp_generated.go:10275）。</summary>
    public static string String(this FileChangeType value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _fileChangeTypeIndex.Length - 1)
        {
            return $"FileChangeType({(uint)value})";
        }

        return _fileChangeTypeName.Substring(_fileChangeTypeIndex[i], _fileChangeTypeIndex[i + 1] - _fileChangeTypeIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _watchKindName = "CreateChangeDelete";

    private static readonly ushort[] _watchKindIndex = [0, 6, 12, 18];

    /// <summary>Go: <c>func (e WatchKind) String() string</c>（lsp_generated.go:10298）。</summary>
    public static string String(this WatchKind value)
    {
        if (value == 0)
        {
            return "0";
        }

        var parts = new List<string>();
        if (((uint)value & 1U) != 0)
        {
            parts.Add(_watchKindName.Substring(_watchKindIndex[0], _watchKindIndex[1] - _watchKindIndex[0]));
        }

        if (((uint)value & 2U) != 0)
        {
            parts.Add(_watchKindName.Substring(_watchKindIndex[1], _watchKindIndex[2] - _watchKindIndex[1]));
        }

        if (((uint)value & 4U) != 0)
        {
            parts.Add(_watchKindName.Substring(_watchKindIndex[2], _watchKindIndex[3] - _watchKindIndex[2]));
        }

        if (parts.Count == 0)
        {
            return $"WatchKind({(uint)value})";
        }

        return string.Join("|", parts);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _diagnosticSeverityName = "ErrorWarningInformationHint";

    private static readonly ushort[] _diagnosticSeverityIndex = [0, 5, 12, 23, 27];

    /// <summary>Go: <c>func (e DiagnosticSeverity) String() string</c>（lsp_generated.go:10336）。</summary>
    public static string String(this DiagnosticSeverity value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _diagnosticSeverityIndex.Length - 1)
        {
            return $"DiagnosticSeverity({(uint)value})";
        }

        return _diagnosticSeverityName.Substring(_diagnosticSeverityIndex[i], _diagnosticSeverityIndex[i + 1] - _diagnosticSeverityIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _diagnosticTagName = "UnnecessaryDeprecated";

    private static readonly ushort[] _diagnosticTagIndex = [0, 11, 21];

    /// <summary>Go: <c>func (e DiagnosticTag) String() string</c>（lsp_generated.go:10365）。</summary>
    public static string String(this DiagnosticTag value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _diagnosticTagIndex.Length - 1)
        {
            return $"DiagnosticTag({(uint)value})";
        }

        return _diagnosticTagName.Substring(_diagnosticTagIndex[i], _diagnosticTagIndex[i + 1] - _diagnosticTagIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _completionTriggerKindName = "InvokedTriggerCharacterTriggerForIncompleteCompletions";

    private static readonly ushort[] _completionTriggerKindIndex = [0, 7, 23, 54];

    /// <summary>Go: <c>func (e CompletionTriggerKind) String() string</c>（lsp_generated.go:10391）。</summary>
    public static string String(this CompletionTriggerKind value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _completionTriggerKindIndex.Length - 1)
        {
            return $"CompletionTriggerKind({(uint)value})";
        }

        return _completionTriggerKindName.Substring(_completionTriggerKindIndex[i], _completionTriggerKindIndex[i + 1] - _completionTriggerKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _applyKindName = "ReplaceMerge";

    private static readonly ushort[] _applyKindIndex = [0, 7, 12];

    /// <summary>Go: <c>func (e ApplyKind) String() string</c>（lsp_generated.go:10420）。</summary>
    public static string String(this ApplyKind value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _applyKindIndex.Length - 1)
        {
            return $"ApplyKind({(uint)value})";
        }

        return _applyKindName.Substring(_applyKindIndex[i], _applyKindIndex[i + 1] - _applyKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _signatureHelpTriggerKindName = "InvokedTriggerCharacterContentChange";

    private static readonly ushort[] _signatureHelpTriggerKindIndex = [0, 7, 23, 36];

    /// <summary>Go: <c>func (e SignatureHelpTriggerKind) String() string</c>（lsp_generated.go:10446）。</summary>
    public static string String(this SignatureHelpTriggerKind value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _signatureHelpTriggerKindIndex.Length - 1)
        {
            return $"SignatureHelpTriggerKind({(uint)value})";
        }

        return _signatureHelpTriggerKindName.Substring(_signatureHelpTriggerKindIndex[i], _signatureHelpTriggerKindIndex[i + 1] - _signatureHelpTriggerKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _codeActionTriggerKindName = "InvokedAutomatic";

    private static readonly ushort[] _codeActionTriggerKindIndex = [0, 7, 16];

    /// <summary>Go: <c>func (e CodeActionTriggerKind) String() string</c>（lsp_generated.go:10473）。</summary>
    public static string String(this CodeActionTriggerKind value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _codeActionTriggerKindIndex.Length - 1)
        {
            return $"CodeActionTriggerKind({(uint)value})";
        }

        return _codeActionTriggerKindName.Substring(_codeActionTriggerKindIndex[i], _codeActionTriggerKindIndex[i + 1] - _codeActionTriggerKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _prepareSupportDefaultBehaviorName = "Identifier";

    private static readonly ushort[] _prepareSupportDefaultBehaviorIndex = [0, 10];

    /// <summary>Go: <c>func (e PrepareSupportDefaultBehavior) String() string</c>（lsp_generated.go:10535）。</summary>
    public static string String(this PrepareSupportDefaultBehavior value)
    {
        var i = (int)value - 1;
        if (i < 0 || i >= _prepareSupportDefaultBehaviorIndex.Length - 1)
        {
            return $"PrepareSupportDefaultBehavior({(uint)value})";
        }

        return _prepareSupportDefaultBehaviorName.Substring(_prepareSupportDefaultBehaviorIndex[i], _prepareSupportDefaultBehaviorIndex[i + 1] - _prepareSupportDefaultBehaviorIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _vSContainerElementStyleName = "WrappedStacked";

    private static readonly ushort[] _vSContainerElementStyleIndex = [0, 7, 14];

    /// <summary>Go: <c>func (e VSContainerElementStyle) String() string</c>（lsp_generated.go:10563）。</summary>
    public static string String(this VSContainerElementStyle value)
    {
        var i = (int)value - 0;
        if (i < 0 || i >= _vSContainerElementStyleIndex.Length - 1)
        {
            return $"VSContainerElementStyle({(int)value})";
        }

        return _vSContainerElementStyleName.Substring(_vSContainerElementStyleIndex[i], _vSContainerElementStyleIndex[i + 1] - _vSContainerElementStyleIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _logVerbosityName = "OffTraceDebugInfoWarningError";

    private static readonly ushort[] _logVerbosityIndex = [0, 3, 8, 13, 17, 24, 29];

    /// <summary>Go: <c>func (e LogVerbosity) String() string</c>（lsp_generated.go:10593）。</summary>
    public static string String(this LogVerbosity value)
    {
        var i = (int)value - 0;
        if (i < 0 || i >= _logVerbosityIndex.Length - 1)
        {
            return $"LogVerbosity({(int)value})";
        }

        return _logVerbosityName.Substring(_logVerbosityIndex[i], _logVerbosityIndex[i + 1] - _logVerbosityIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _diagnosticFlakeLogLevelName = "OffLogPanic";

    private static readonly ushort[] _diagnosticFlakeLogLevelIndex = [0, 3, 6, 11];

    /// <summary>Go: <c>func (e DiagnosticFlakeLogLevel) String() string</c>（lsp_generated.go:10617）。</summary>
    public static string String(this DiagnosticFlakeLogLevel value)
    {
        var i = (int)value - 0;
        if (i < 0 || i >= _diagnosticFlakeLogLevelIndex.Length - 1)
        {
            return $"DiagnosticFlakeLogLevel({(int)value})";
        }

        return _diagnosticFlakeLogLevelName.Substring(_diagnosticFlakeLogLevelIndex[i], _diagnosticFlakeLogLevelIndex[i + 1] - _diagnosticFlakeLogLevelIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _vSReferenceKindName = "InactiveCommentStringReadWriteReferenceNameQualifiedTypeArgumentTypeConstraintBaseTypeConstructorDestructorImportDeclarationAddressOfNotReferenceUnknown";

    private static readonly ushort[] _vSReferenceKindIndex = [0, 8, 15, 21, 25, 30, 39, 43, 52, 64, 78, 86, 97, 107, 113, 124, 133, 145, 152];

    /// <summary>Go: <c>func (e VSReferenceKind) String() string</c>（lsp_generated.go:10652）。</summary>
    public static string String(this VSReferenceKind value)
    {
        var i = (int)value - 0;
        if (i < 0 || i >= _vSReferenceKindIndex.Length - 1)
        {
            return $"VSReferenceKind({(int)value})";
        }

        return _vSReferenceKindName.Substring(_vSReferenceKindIndex[i], _vSReferenceKindIndex[i + 1] - _vSReferenceKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _autoImportFixKindName = "UseNamespaceJsdocTypeImportAddToExistingAddNewPromoteTypeOnly";

    private static readonly ushort[] _autoImportFixKindIndex = [0, 12, 27, 40, 46, 61];

    /// <summary>Go: <c>func (e AutoImportFixKind) String() string</c>（lsp_generated.go:10686）。</summary>
    public static string String(this AutoImportFixKind value)
    {
        var i = (int)value - 0;
        if (i < 0 || i >= _autoImportFixKindIndex.Length - 1)
        {
            return $"AutoImportFixKind({(int)value})";
        }

        return _autoImportFixKindName.Substring(_autoImportFixKindIndex[i], _autoImportFixKindIndex[i + 1] - _autoImportFixKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _importKindName = "NamedDefaultNamespaceCommonJS";

    private static readonly ushort[] _importKindIndex = [0, 5, 12, 21, 29];

    /// <summary>Go: <c>func (e ImportKind) String() string</c>（lsp_generated.go:10711）。</summary>
    public static string String(this ImportKind value)
    {
        var i = (int)value - 0;
        if (i < 0 || i >= _importKindIndex.Length - 1)
        {
            return $"ImportKind({(int)value})";
        }

        return _importKindName.Substring(_importKindIndex[i], _importKindIndex[i + 1] - _importKindIndex[i]);
    }
}

/// <summary>Go 的 stringer 表（<c>_X_name</c>/<c>_X_index</c>）与扩展方法 <c>String()</c>。</summary>
public static partial class LsProto
{
    private const string _addAsTypeOnlyName = "AllowedRequiredNotAllowed";

    private static readonly ushort[] _addAsTypeOnlyIndex0 = [0, 7, 15];

    /// <summary>Go: <c>func (e AddAsTypeOnly) String() string</c>（lsp_generated.go:10737）。</summary>
    public static string String(this AddAsTypeOnly value)
    {
        if (1 <= (int)value && (int)value <= 2)
        {
            var i = (int)value - 1;
            return _addAsTypeOnlyName.Substring(0 + _addAsTypeOnlyIndex0[i], (0 + _addAsTypeOnlyIndex0[i + 1]) - (0 + _addAsTypeOnlyIndex0[i]));
        }

        if ((int)value == 4)
        {
            return _addAsTypeOnlyName.Substring(15, 25 - 15);
        }

        return $"AddAsTypeOnly({(int)value})";
    }
}

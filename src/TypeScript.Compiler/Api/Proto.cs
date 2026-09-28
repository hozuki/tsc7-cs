// ported from: internal/api/proto.go:284-360（DocumentIdentifier）、398-419（EnsurePrograms）、
//   569-745（unmarshalers 表）、764-786（jsonValueToAny）、983-1009（NewConfigFileResponse）、
//   1011-1034（toProtocolJSONValue）、1036-1105（NewProjectResponse）、1107-1117（symbolHandles）、
//   1196-1281（newTypeResponse）、1283-1292（typeHandles）、1294-1396（literalValueToJSON）、
//   1398-1408（NewPackageId）、1915-1942（diagnosticSourceLines）、1944-2013（NewDiagnosticResponse /
//   newDiagnosticResponse / (*DiagnosticResponse).ToDiagnostic）、2015-2024（NewDiagnosticResponses）、
//   2026-2044（unmarshalPayload / unmarshallerFor / noParams）
//
// 本文件是 api 包"手写的那一半"：proto.go 里**不是** struct 声明、也**不由**生成器产出的部分。
// 其余类型声明在 Api/Proto.Types.cs / Api/Proto.Types2.cs（兄弟分片）；Api/Api.cs 是冻结地基（Method / ID 类型 /
// SymbolHandle / TypeHandle / SignatureHandle / SnapshotHandle）。
//
// 与 Go 的偏离（逐条说明）：
//
//  1) **手写 JSON 两个类型**（`DocumentIdentifier`、`EnsurePrograms`）：Go 靠 `encoding/json/v2` 的 struct tag +
//     自定义 `UnmarshalJSONFrom`；C# AOT 禁反射（契约 §1.1）⇒ 手写 `UnmarshalJSONFrom`/`MarshalJSONTo`（契约 §3 末条）。
//  2) Go 的 `dec.ReadToken() (Token, error)` / `dec.PeekKind() (Kind, error)` 带 error；C# 的 `Decoder` 出错直接抛
//     `TypeScript.Json.JsonException`（Json 包既有形状）⇒ `if err != nil` 分支在 C# 消失。
//  3) Go 的 `tok.String()` / `key.String()` → C# 的 `JsonToken.StringValue`（Json/JsonToken.cs 的既有成员名）。
//  4) Go 的 `fmt.Errorf("… got %v", tok.Kind())` 里 `%v` 打的是 `jsontext.Kind` 的 stringer（null/false/true/string/
//     number/`{`/`}`/`[`/`]`）；本仓同款实现 `LsProto.KindString` 是 private ⇒ 在本文件复刻 `JsonKindString`。
//  5) `DocumentIdentifier.MarshalJSONTo`：Go **没有**手写 marshal，靠两个 `omitempty` tag。C# 手写等价编码：
//     字段顺序 fileName、uri，各自非空才写出（**两个都可能写出**，与 Go 的 omitempty 语义一致；不是 if/else）。
//  6) `EnsurePrograms.MarshalJSONTo`：Go 同样没有手写 marshal，靠无 tag 的默认名 `All`/`Projects`（无 omitempty）
//     ⇒ `All` 总是写出；`Projects` 为 nil 时写 `null`（Go 的 nil 切片 → null）。元素 `project.ID` 在本仓**没有**
//     `IJsonMarshaler`（见 Proto.Types2.cs 文件头第 9 条）⇒ 直接写底层 `Value` 字符串。
//  7) **错误路径**：Go 的 `(T, error)` → C# 异常。`unmarshallerFor` 的 `fmt.Errorf("failed to unmarshal %T: %w", …)`
//     与调用点 `fmt.Errorf("%w: %w", ErrInvalidRequest, err)` 的文本合成为
//     `api: invalid request: failed to unmarshal *T: <json 错误>`，由 <see cref="UnmarshalPayload"/> 一次抛出
//     `InvalidRequestException`（session.go:710-713 的包装因此不需要在调用点重复）。**偏离**：Go 的 `%T` 打
//     `*api.XParams`，C# 用 `typeof(T).Name` 只能给出 `*XParams`（本仓类型没有 Go 的包名限定）。
//  8) `collections.NewOrderedMapWithSizeHint` → `NewOrderedMapWithSizeHint<string, object?>(…)`
//     （`using static TypeScript.Collections.Collections;`，同 PackageJson/Cache.cs 的写法）。
//  9) `jsonValueToAny` 的 `panic(fmt.Sprintf("unexpected JSON value type %v", value.Type))` → `DebugFailureException`；
//     `value.Type` 的 stringer 在本仓是扩展方法 `JSONValueTypeExtensions.String()`（PackageJson/JsonValue.cs）。
// 10) `NewProjectResponse` 的 `panic("NewProjectResponse called with unloaded project")` → `Debug.Fail(...)` +
//     `throw new System.Diagnostics.UnreachableException()`（`Debug` 静态类在**根命名空间** `TypeScript` 里，
//     文件头之外的调用点写 `Debug.Fail`；`Debug.Fail` 没有 `[DoesNotReturn]`，其后必须补 throw 才能让可空流分析
//     认可 `p`/`p.CommandLine` 非空，先例 Checker/Checker.C5.SymbolTypes.cs:254-255；全项目禁止 `using
//     System.Diagnostics;`，故 UnreachableException 写全名）。契约 §1.2：panic → DebugFailureException。
//     `NewConfigFileResponse` 里 `parsedCommandLine.CompilerOptions()` 在本仓返回可空（TsOptions/ParsedCommandLine.cs:345），
//     而 proto 字段是 `nonnil:"true"` 的非空字段 ⇒ 用 `!`：与 Go 一样，"已解析的 command line 一定有 options"
//     这个前提由调用方保证（Go 置 nil 会在 marshal 时 panic）。
// 11) `newTypeResponse` 的 `switch flags := t.Flags(); { case … }`（switch-true）在 C# 里落成 **if / else if 链**，
//     分支顺序与 Go 逐条一致；`t.AsX()` 在本仓是可空虚方法（Go 的类型断言失败会 panic）⇒ 调用点按 Ast 习惯加 `!`。
//     `&fixedLen` / `&isReadonly` → 直接给 `int?` / `bool?` 字段赋值（Proto.Types2.cs 的字段形状）。
// 12) `diagnosticSourceLines` 返回 `[]*DiagnosticSourceLineResponse`（proto.go:1915）⇒ C# 返回数组
//     `DiagnosticSourceLineResponse[]?`（nil = Go 的 nil 切片），内部用 `List<>` 累积后 `[.. ]`。
//     **这是对集成者冻结签名（`List<DiagnosticSourceLineResponse>?`）的有意修正**：兄弟分片 Proto.Types2.cs 的
//     `DiagnosticResponse.SourceLines` 是 `DiagnosticSourceLineResponse[]?`，返回 List 会在调用点多一次拷贝。
// 13) Go 的方法 `(*DiagnosticResponse).ToDiagnostic` → C# 的**扩展方法** `ToDiagnostic(this DiagnosticResponse d)`，
//     声明在 `Api` 静态类上：`DiagnosticResponse` 由兄弟分片声明为 `public sealed class`（非 partial），
//     本文件无法给它加实例成员，而 `Api` 静态类是 `using TypeScript.Api;` 下自动在作用域的宿主（调用点写法不变）。
// 14) `core.UTF16Len` / `core.Map` → `Core.Core.UTF16Len` / `Core.Core.Map`（`using static TypeScript.Core.Core;`
//     会被 `Session.ToPath` 之类的同名成员遮蔽，同 Session.Files.cs 文件头的写法）；`core.NewTextRange` →
//     `Core.TextRange.NewTextRange`；`core.UTF16Offset` → `Core.UTF16Offset`。
// 15) `scanner.GetECMALineAndUTF16CharacterOfPosition` → `Scanner.Scanner.GetECMALineAndUTF16CharacterOfPosition`
//     （返回 `(int Line, UTF16Offset Character)` 元组，Scanner/Scanner.Trivia.cs:622）。
// 16) `locale.Default` → `Loc.Default`（`using Loc = TypeScript.Locale.Locale;`，见 Locale/Locale.cs 文件头的
//     命名解析警告：类型 `Locale` 与命名空间 `TypeScript.Locale` 同名）。
// 17) `diagnosticwriter.FileLike` / `ASTDiagnostic` / `WrapASTDiagnostic` → 命名空间限定
//     `DiagnosticWriter.FileLike` / `DiagnosticWriter.ASTDiagnostic` 与别名 `DW`（`TypeScript.DiagnosticWriter`
//     既是命名空间又有同名静态类，同 `Checker.Checker` 的处理）。
// 18) 线程：本文件不含任何递归入口、也不启动线程 ⇒ 无需 64 MiB 栈线程（契约 §1.3 不适用）。
// 19) 类型检查：`d.MessageChain()`/`d.RelatedInformation()` 在本仓返回 `DiagnosticWriter.Diagnostic[]`（接口数组），
//     Go 的 `c.(*diagnosticwriter.ASTDiagnostic)` → C# 的向下强转 `(DiagnosticWriter.ASTDiagnostic)c`。
using System.Text;
using TypeScript.Collections;
using TypeScript.Json;
using static TypeScript.Collections.Collections;
using static TypeScript.Json.Json;
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;
using JsonStatics = TypeScript.Json.Json;
using DW = TypeScript.DiagnosticWriter.DiagnosticWriter;
using Loc = TypeScript.Locale.Locale;

namespace TypeScript.Api;

/// <summary>
/// Go: <c>type DocumentIdentifier struct { FileName string `json:"fileName,omitempty"`; URI lsproto.DocumentUri `json:"uri,omitempty"` }</c>
/// （proto.go:284-287）。线格式是 <c>string | { uri: string }</c>（proto.go:265-273 的注释）。
/// </summary>
public sealed class DocumentIdentifier : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>FileName string `json:"fileName,omitempty"`</c>（proto.go:285）。</summary>
    public string FileName = "";

    /// <summary>Go: <c>URI lsproto.DocumentUri `json:"uri,omitempty"`</c>（proto.go:286）。</summary>
    public LsProto.DocumentUri URI;

    /// <summary>Go: <c>func (d *DocumentIdentifier) UnmarshalJSONFrom(dec *json.Decoder) error</c>（proto.go:291-325）。
    /// 先试普通字符串；否则必须是对象，只认 <c>uri</c> 成员（其余成员照读不误但无效果），最后吃掉右花括号。
    /// Go 的 <c>dec.ReadToken()</c> 错误 → C# 的 <see cref="JsonException"/>（文件头第 2 条）；
    /// <c>tok.String()</c> → <c>StringValue</c>（第 3 条）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        // Try reading as a plain string first
        var tok = dec.ReadToken();
        switch (tok.Kind)
        {
            case JsonKind.String:
                FileName = tok.StringValue;
                return;
            case JsonKind.BeginObject:
                // Read the object fields
                while (dec.PeekKind() != JsonKind.EndObject)
                {
                    var key = dec.ReadToken();
                    var isURI = key.StringValue == "uri";
                    var val = dec.ReadToken();
                    if (isURI)
                    {
                        URI = new LsProto.DocumentUri(val.StringValue);
                    }
                }
                // Consume the closing brace
                dec.ReadToken();
                return;
            default:
                // Go: fmt.Errorf("DocumentIdentifier: expected string or object, got %v", tok.Kind())
                throw new JsonException($"DocumentIdentifier: expected string or object, got {Api.JsonKindString(tok.Kind)}");
        }
    }

    /// <summary>Go: Go 侧无此方法 —— 靠 <c>json:"fileName,omitempty"</c> / <c>json:"uri,omitempty"</c> 两个 tag 编码
    /// （proto.go:285-286）。C# 手写等价编码：字段顺序 fileName、uri，各自非空才写出（文件头第 5 条）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        enc.WriteToken(BeginObject);
        if (FileName.Length != 0)
        {
            enc.WriteToken(JsonToken.String("fileName"));
            MarshalEncode(enc, FileName);
        }

        if (URI.Value.Length != 0)
        {
            enc.WriteToken(JsonToken.String("uri"));
            MarshalEncode(enc, URI);
        }

        enc.WriteToken(EndObject);
    }

    /// <summary>Go: <c>func (d DocumentIdentifier) ToFileName() string</c>（proto.go:327-332）。</summary>
    public string ToFileName()
    {
        if (URI.Value.Length != 0)
        {
            return URI.FileName();
        }

        return FileName;
    }

    /// <summary>Go: <c>func (d DocumentIdentifier) ToURI(cwd string) lsproto.DocumentUri</c>（proto.go:337-342）：
    /// 显式给出的 URI 原样返回；文件名先按 cwd 归一成绝对路径再转 URI。</summary>
    public LsProto.DocumentUri ToURI(string cwd)
    {
        if (URI.Value.Length != 0)
        {
            return URI;
        }

        return LsConv.LsConv.FileNameToDocumentURI(TSPath.TSPath.GetNormalizedAbsolutePath(FileName, cwd));
    }

    /// <summary>Go: <c>func (d DocumentIdentifier) ToAbsoluteFileName(cwd string) string</c>（proto.go:344-349）。</summary>
    public string ToAbsoluteFileName(string cwd)
    {
        if (URI.Value.Length != 0)
        {
            return URI.FileName();
        }

        return TSPath.TSPath.GetNormalizedAbsolutePath(FileName, cwd);
    }

    /// <summary>Go: <c>func (d DocumentIdentifier) String() string</c>（proto.go:351-356）。
    /// Go 的 stringer → C# 覆写 <c>ToString()</c>（docs/architecture.md §3.3）。</summary>
    public override string ToString()
    {
        if (URI.Value.Length != 0)
        {
            return URI.Value;
        }

        return FileName;
    }
}

/// <summary>
/// Go: <c>type EnsurePrograms struct { All bool; Projects []project.ID }</c>（proto.go:398-401）。
/// 线格式是 <c>true | project.ID[]</c>（见其自定义 <c>UnmarshalJSONFrom</c>）。
/// </summary>
public sealed class EnsurePrograms : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>All bool</c>（proto.go:399）。</summary>
    public bool All;

    /// <summary>Go: <c>Projects []project.ID</c>（proto.go:400）。nil = Go 的 nil 切片（编码为 <c>null</c>）。</summary>
    public Project.ID[]? Projects;

    /// <summary>Go: <c>func (e *EnsurePrograms) UnmarshalJSONFrom(dec *json.Decoder) error</c>（proto.go:405-417）：
    /// <c>true</c> 置 <c>All</c>；否则必须是数组（元素是 project.ID 字符串），直接解进 <c>Projects</c>。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var value = dec.ReadValue();
        if (Encoding.UTF8.GetString(value) == "true")
        {
            All = true;
            return;
        }

        if (value.Length == 0 || value[0] != (byte)'[')
        {
            // Go: errors.New("ensurePrograms must be true or an array of project IDs")
            throw new JsonException("ensurePrograms must be true or an array of project IDs");
        }

        // Go: json.Unmarshal(value, &e.Projects) —— project.ID 在本仓没有 IJsonUnmarshaler（见文件头第 6 条），
        // 所以按 Proto.Types2.cs 的既定做法逐元素 `new Project.ID(dec.Decode<string>())`。
        var decoder = new JsonDecoder(value);
        decoder.ReadToken(); // consume '['
        var projects = new List<Project.ID>();
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            projects.Add(new Project.ID(decoder.Decode<string>() ?? ""));
        }

        decoder.ReadToken(); // consume ']'
        Projects = [.. projects];
    }

    /// <summary>Go: Go 侧无此方法 —— 靠无 tag 的默认字段名编码（`All`/`Projects`，都没有 omitempty）：proto.go:398-401。
    /// <c>Projects</c> 为 nil 时写 <c>null</c>（Go 的 nil 切片 → null）；元素直接写 `project.ID.Value`（文件头第 6 条）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        enc.WriteToken(BeginObject);
        enc.WriteToken(JsonToken.String("All"));
        MarshalEncode(enc, All);
        enc.WriteToken(JsonToken.String("Projects"));
        if (Projects is null)
        {
            enc.WriteToken(Null);
        }
        else
        {
            enc.WriteToken(BeginArray);
            foreach (var project in Projects)
            {
                enc.WriteStringValue(project.Value);
            }

            enc.WriteToken(EndArray);
        }

        enc.WriteToken(EndObject);
    }
}

public static partial class Api
{
    /// <summary>Go: <c>var unmarshalers = map[Method]func([]byte) (any, error)</c>（proto.go:569-745）。
    /// 每个条目是 Go 的 <c>unmarshallerFor[X]</c>（→ <see cref="UnmarshalNew{T}"/>）；<c>MethodInitialize</c> /
    /// <c>MethodStopCPUProfile</c> 用 Go 的 <c>noParams</c>（→ <see cref="NoParams"/>，返回 null）。
    /// 键是 <see cref="Method"/> 的 <c>const string</c>（Api.cs 文件头第 1 条：C# 没有 `type Method string`）。</summary>
    internal static readonly Dictionary<string, Func<byte[], object?>> Unmarshallers = new()
    {
        [Method.BatchRequests] = static d => UnmarshalNew<BatchRequestsParams>(d),
        [Method.Release] = static d => UnmarshalNew<ReleaseParams>(d),
        [Method.ReleaseSourceFile] = static d => UnmarshalNew<ReleaseSourceFileParams>(d),
        [Method.Initialize] = NoParams,
        [Method.CreateSnapshot] = static d => UnmarshalNew<CreateSnapshotParams>(d),
        [Method.UpdateSnapshot] = static d => UnmarshalNew<UpdateSnapshotParams>(d),
        [Method.GetCurrentLanguageServerSnapshot] = static d => UnmarshalNew<GetCurrentLanguageServerSnapshotParams>(d),
        [Method.CreateBuildOrchestrator] = static d => UnmarshalNew<CreateBuildOrchestratorParams>(d),
        [Method.DisposeBuildOrchestrator] = static d => UnmarshalNew<DisposeBuildOrchestratorParams>(d),
        [Method.Build] = static d => UnmarshalNew<BuildParams>(d),
        [Method.BuildReferences] = static d => UnmarshalNew<BuildParams>(d),
        [Method.CleanBuild] = static d => UnmarshalNew<CleanBuildParams>(d),
        [Method.CleanReferences] = static d => UnmarshalNew<CleanBuildParams>(d),
        [Method.CreateModuleResolver] = static d => UnmarshalNew<CreateModuleResolverParams>(d),
        [Method.ReleaseModuleResolver] = static d => UnmarshalNew<ReleaseModuleResolverParams>(d),
        [Method.ResolveModuleName] = static d => UnmarshalNew<ResolveModuleNameParams>(d),
        [Method.ParseCommandLine] = static d => UnmarshalNew<ParseCommandLineParams>(d),
        [Method.ReadConfigFile] = static d => UnmarshalNew<ReadConfigFileParams>(d),
        [Method.ParseJsonConfigFile] = static d => UnmarshalNew<ParseJsonConfigFileContentParams>(d),
        [Method.ParseConfigFile] = static d => UnmarshalNew<ParseConfigFileParams>(d),
        [Method.CreateSourceFile] = static d => UnmarshalNew<CreateSourceFileParams>(d),
        [Method.CreateSourceFileFromFile] = static d => UnmarshalNew<CreateSourceFileFromFileParams>(d),
        [Method.TranspileModule] = static d => UnmarshalNew<TranspileParams>(d),
        [Method.TranspileModuleFromFile] = static d => UnmarshalNew<TranspileFromFileParams>(d),
        [Method.TranspileDeclaration] = static d => UnmarshalNew<TranspileParams>(d),
        [Method.TranspileDeclarationFromFile] = static d => UnmarshalNew<TranspileFromFileParams>(d),
        [Method.GetDefaultProjectForFile] = static d => UnmarshalNew<GetDefaultProjectForFileParams>(d),
        [Method.GetSourceFile] = static d => UnmarshalNew<GetSourceFileParams>(d),
        [Method.GetSourceFileNames] = static d => UnmarshalNew<GetSourceFileNamesParams>(d),
        [Method.GetSourceFileMetadata] = static d => UnmarshalNew<GetSourceFileParams>(d),
        [Method.GetModeForUsageLocation] = static d => UnmarshalNew<GetModeForUsageLocationParams>(d),
        [Method.GetModeForResolutionAtIndex] = static d => UnmarshalNew<GetModeForResolutionAtIndexParams>(d),
        [Method.GetResolvedModule] = static d => UnmarshalNew<GetResolvedModuleParams>(d),
        [Method.GetResolvedModuleFromModuleSpecifier] = static d => UnmarshalNew<GetResolvedModuleFromModuleSpecifierParams>(d),
        [Method.GetResolvedTypeReferenceDirective] = static d => UnmarshalNew<GetResolvedTypeReferenceDirectiveParams>(d),
        [Method.GetResolvedTypeReferenceDirectiveFromReference] = static d => UnmarshalNew<GetResolvedTypeReferenceDirectiveFromReferenceParams>(d),
        [Method.GetConfigFileNames] = static d => UnmarshalNew<GetProjectDiagnosticsParams>(d),
        [Method.GetConfigSourceFile] = static d => UnmarshalNew<GetSourceFileParams>(d),
        [Method.GetSymbolAtPosition] = static d => UnmarshalNew<GetSymbolAtPositionParams>(d),
        [Method.GetSymbolsAtPositions] = static d => UnmarshalNew<GetSymbolsAtPositionsParams>(d),
        [Method.GetSymbolAtLocation] = static d => UnmarshalNew<GetSymbolAtLocationParams>(d),
        [Method.GetSymbolsAtLocations] = static d => UnmarshalNew<GetSymbolsAtLocationsParams>(d),
        [Method.GetSymbolOfSourceFile] = static d => UnmarshalNew<GetSymbolOfSourceFileParams>(d),
        [Method.GetSymbolsOfSourceFiles] = static d => UnmarshalNew<GetSymbolsOfSourceFilesParams>(d),
        [Method.GetTypeOfSymbol] = static d => UnmarshalNew<GetTypeOfSymbolParams>(d),
        [Method.GetTypesOfSymbols] = static d => UnmarshalNew<GetTypesOfSymbolsParams>(d),
        [Method.GetDeclaredTypeOfSymbol] = static d => UnmarshalNew<GetTypeOfSymbolParams>(d),
        [Method.GetNonMissingTypeOfSymbol] = static d => UnmarshalNew<GetTypeOfSymbolParams>(d),
        [Method.ResolveName] = static d => UnmarshalNew<ResolveNameParams>(d),
        [Method.GetSymbolsInScope] = static d => UnmarshalNew<GetSymbolsInScopeParams>(d),
        [Method.GetSignaturesOfType] = static d => UnmarshalNew<GetSignaturesOfTypeParams>(d),
        [Method.GetResolvedSignature] = static d => UnmarshalNew<GetResolvedSignatureParams>(d),
        [Method.GetTypeAtLocation] = static d => UnmarshalNew<GetTypeAtLocationParams>(d),
        [Method.GetTypeAtLocations] = static d => UnmarshalNew<GetTypeAtLocationsParams>(d),
        [Method.GetTypeAtPosition] = static d => UnmarshalNew<GetTypeAtPositionParams>(d),
        [Method.GetTypesAtPositions] = static d => UnmarshalNew<GetTypesAtPositionsParams>(d),

        [Method.GetParentOfSymbol] = static d => UnmarshalNew<GetSymbolPropertyParams>(d),
        [Method.GetMembersOfSymbol] = static d => UnmarshalNew<GetSymbolPropertyParams>(d),
        [Method.GetExportsOfSymbol] = static d => UnmarshalNew<GetSymbolPropertyParams>(d),
        [Method.GetExportSymbolOfSymbol] = static d => UnmarshalNew<GetSymbolPropertyParams>(d),

        [Method.GetSymbolOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetTargetOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetFreshTypeOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetRegularTypeOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetTypesOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetTypeParametersOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetOuterTypeParametersOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetLocalTypeParametersOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetThisTypeOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetAliasTypeArgumentsOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetAliasSymbolOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetObjectTypeOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetIndexTypeOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetCheckTypeOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetExtendsTypeOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetBaseTypeOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetConstraintOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetTypeParameterOfMappedType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetConstraintTypeOfMappedType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetNameTypeOfMappedType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetTemplateTypeOfMappedType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetTrueTypeOfConditionalType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetFalseTypeOfConditionalType] = static d => UnmarshalNew<GetTypePropertyParams>(d),

        [Method.GetTypeParametersOfSignature] = static d => UnmarshalNew<GetSignaturePropertyParams>(d),
        [Method.GetParametersOfSignature] = static d => UnmarshalNew<GetSignaturePropertyParams>(d),
        [Method.GetThisParameterOfSignature] = static d => UnmarshalNew<GetSignaturePropertyParams>(d),
        [Method.GetTargetOfSignature] = static d => UnmarshalNew<GetSignaturePropertyParams>(d),

        [Method.GetContextualType] = static d => UnmarshalNew<GetContextualTypeParams>(d),
        [Method.GetContextualTypeForArgument] = static d => UnmarshalNew<GetContextualTypeForArgumentParams>(d),
        [Method.GetAwaitedType] = static d => UnmarshalNew<CheckerTypeParams>(d),
        [Method.GetBaseTypeOfLiteralType] = static d => UnmarshalNew<GetBaseTypeOfLiteralTypeParams>(d),
        [Method.GetNonNullableType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetTypeFromTypeNode] = static d => UnmarshalNew<GetTypeFromTypeNodeParams>(d),
        [Method.GetWidenedType] = static d => UnmarshalNew<GetWidenedTypeParams>(d),
        [Method.GetParameterType] = static d => UnmarshalNew<GetParameterTypeParams>(d),
        [Method.GetTypeParameterAtPosition] = static d => UnmarshalNew<GetParameterTypeParams>(d),
        [Method.IsArrayLikeType] = static d => UnmarshalNew<IsArrayLikeTypeParams>(d),
        [Method.IsTypeAssignableTo] = static d => UnmarshalNew<IsTypeAssignableToParams>(d),
        [Method.GetShorthandAssignmentValueSymbol] = static d => UnmarshalNew<GetTypeAtLocationParams>(d),
        [Method.GetTypeOfSymbolAtLocation] = static d => UnmarshalNew<GetTypeOfSymbolAtLocationParams>(d),
        [Method.TypeToTypeNode] = static d => UnmarshalNew<TypeToTypeNodeParams>(d),
        [Method.SignatureToSignatureDeclaration] = static d => UnmarshalNew<SignatureToSignatureDeclarationParams>(d),
        [Method.TypeToString] = static d => UnmarshalNew<TypeToTypeNodeParams>(d),
        [Method.IsContextSensitive] = static d => UnmarshalNew<GetContextualTypeParams>(d),
        [Method.GetReturnTypeOfSignature] = static d => UnmarshalNew<GetSignaturePropertyParams>(d),
        [Method.GetRestTypeOfSignature] = static d => UnmarshalNew<CheckerSignatureParams>(d),
        [Method.GetTypePredicateOfSignature] = static d => UnmarshalNew<CheckerSignatureParams>(d),
        [Method.GetBaseTypes] = static d => UnmarshalNew<CheckerTypeParams>(d),
        [Method.GetPropertiesOfType] = static d => UnmarshalNew<CheckerTypeParams>(d),
        [Method.GetApparentPropertiesOfType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetApparentType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetReducedType] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetPropertyOfType] = static d => UnmarshalNew<GetPropertyOfTypeParams>(d),
        [Method.GetTypeOfPropertyOfType] = static d => UnmarshalNew<GetPropertyOfTypeParams>(d),
        [Method.GetIndexInfoOfType] = static d => UnmarshalNew<GetIndexInfoOfTypeParams>(d),
        [Method.GetIndexInfosOfType] = static d => UnmarshalNew<CheckerTypeParams>(d),
        [Method.GetConstraintOfTypeParameter] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetBaseConstraintOfType] = static d => UnmarshalNew<CheckerTypeParams>(d),
        [Method.GetDefaultFromTypeParameter] = static d => UnmarshalNew<GetTypePropertyParams>(d),
        [Method.GetTypeArguments] = static d => UnmarshalNew<CheckerTypeParams>(d),
        [Method.GetImportAdderEdits] = static d => UnmarshalNew<GetImportAdderEditsParams>(d),
        [Method.GetConstantValue] = static d => UnmarshalNew<CheckerNodeParams>(d),
        [Method.GetSignatureFromDeclaration] = static d => UnmarshalNew<CheckerNodeParams>(d),
        [Method.GetExportSpecifierLocalTarget] = static d => UnmarshalNew<CheckerNodeParams>(d),
        [Method.GetAliasedSymbol] = static d => UnmarshalNew<CheckerSymbolParams>(d),
        [Method.GetImmediateAliasedSymbol] = static d => UnmarshalNew<CheckerSymbolParams>(d),
        [Method.GetTargetSymbol] = static d => UnmarshalNew<CheckerSymbolParams>(d),
        [Method.GetExportSymbolOfSymbolForChecker] = static d => UnmarshalNew<CheckerSymbolParams>(d),
        [Method.GetFullyQualifiedName] = static d => UnmarshalNew<CheckerSymbolParams>(d),
        [Method.GetExportsOfModule] = static d => UnmarshalNew<CheckerSymbolParams>(d),
        [Method.GetMemberInModuleExports] = static d => UnmarshalNew<GetMemberInModuleExportsParams>(d),
        [Method.GetJSDocTags] = static d => UnmarshalNew<CheckerSymbolParams>(d),
        [Method.GetDocumentationComment] = static d => UnmarshalNew<CheckerSymbolParams>(d),
        [Method.IsArrayType] = static d => UnmarshalNew<CheckerTypeParams>(d),
        [Method.IsReadonlySymbol] = static d => UnmarshalNew<CheckerSymbolParams>(d),
        [Method.GetReferencesToSymbolInFile] = static d => UnmarshalNew<GetReferencesToSymbolInFileParams>(d),
        [Method.GetReferencedSymbolsForNode] = static d => UnmarshalNew<GetReferencedSymbolsForNodeParams>(d),
        [Method.GetSignatureUsages] = static d => UnmarshalNew<GetSignatureUsagesParams>(d),
        [Method.GetCompletionsAtPosition] = static d => UnmarshalNew<GetCompletionsAtPositionParams>(d),
        [Method.PrintNode] = static d => UnmarshalNew<PrintNodeParams>(d),
        [Method.FormatNodeForInsertion] = static d => UnmarshalNew<FormatNodeForInsertionParams>(d),
        [Method.Emit] = static d => UnmarshalNew<EmitParams>(d),
        [Method.EmitToString] = static d => UnmarshalNew<EmitParams>(d),
        [Method.GetJavaScriptEmit] = static d => UnmarshalNew<SelectedFilesEmitParams>(d),
        [Method.GetDeclarationEmit] = static d => UnmarshalNew<SelectedFilesEmitParams>(d),
        [Method.GetAnyType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetStringType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetNumberType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetBooleanType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetVoidType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetUndefinedType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetNullType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetNeverType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetUnknownType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetBigIntType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetESSymbolType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetNonPrimitiveType] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetWellKnownSymbols] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetWellKnownSignatures] = static d => UnmarshalNew<GetIntrinsicTypeParams>(d),
        [Method.GetSyntacticDiagnostics] = static d => UnmarshalNew<GetDiagnosticsParams>(d),
        [Method.GetBindDiagnostics] = static d => UnmarshalNew<GetDiagnosticsParams>(d),
        [Method.GetSemanticDiagnostics] = static d => UnmarshalNew<GetDiagnosticsParams>(d),
        [Method.GetSuggestionDiagnostics] = static d => UnmarshalNew<GetDiagnosticsParams>(d),
        [Method.GetDeclarationDiagnostics] = static d => UnmarshalNew<GetDiagnosticsParams>(d),
        [Method.GetProgramDiagnostics] = static d => UnmarshalNew<GetProjectDiagnosticsParams>(d),
        [Method.GetGlobalDiagnostics] = static d => UnmarshalNew<GetProjectDiagnosticsParams>(d),
        [Method.GetConfigFileParsingDiagnostics] = static d => UnmarshalNew<GetProjectDiagnosticsParams>(d),
        [Method.StartCPUProfile] = static d => UnmarshalNew<ProfileParams>(d),
        [Method.StopCPUProfile] = NoParams,
        [Method.SaveHeapProfile] = static d => UnmarshalNew<ProfileParams>(d),
    };

    /// <summary>Go: <c>func jsonValueToAny(value packagejson.JSONValue) any</c>（proto.go:764-786）：
    /// 把 package.json 的形状化 JSON 值转成普通 JSON 值（对象 → <c>OrderedMap&lt;string, object?&gt;</c>）。</summary>
    internal static object? JsonValueToAny(PackageJson.JSONValue value)
    {
        switch (value.Type)
        {
            case PackageJson.JSONValueType.NotPresent:
            case PackageJson.JSONValueType.Null:
                return null;
            case PackageJson.JSONValueType.String:
            case PackageJson.JSONValueType.Number:
            case PackageJson.JSONValueType.Boolean:
                return value.Value;
            case PackageJson.JSONValueType.Array:
            {
                var array = value.AsArray();
                var result = new object?[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    result[i] = JsonValueToAny(array[i]);
                }

                return result;
            }

            case PackageJson.JSONValueType.Object:
            {
                var obj = value.AsObject()!;
                var result = NewOrderedMapWithSizeHint<string, object?>(obj.Size());
                foreach (var (key, child) in obj.Entries())
                {
                    result.Set(key, JsonValueToAny(child!));
                }

                return result;
            }

            default:
                // Go: panic(fmt.Sprintf("unexpected JSON value type %v", value.Type))
                throw new DebugFailureException(
                    $"unexpected JSON value type {PackageJson.JSONValueTypeExtensions.String(value.Type)}");
        }
    }

    /// <summary>Go: <c>func toProtocolJSONValue(value any) any</c>（proto.go:1011-1034）：
    /// <c>core.WatchFileKind</c>/<c>WatchDirectoryKind</c>/<c>PollingKind</c> → <c>int(value) - 1</c>（0 基协议值），
    /// <c>*collections.OrderedMap[string, any]</c> 与 <c>[]any</c> 递归，其余原样返回。</summary>
    internal static object? ToProtocolJSONValue(object? value)
    {
        switch (value)
        {
            case Core.WatchFileKind watchFileKind:
                return (int)watchFileKind - 1;
            case Core.WatchDirectoryKind watchDirectoryKind:
                return (int)watchDirectoryKind - 1;
            case Core.PollingKind pollingKind:
                return (int)pollingKind - 1;
            case OrderedMap<string, object?> map:
            {
                var result = NewOrderedMapWithSizeHint<string, object?>(map.Size());
                foreach (var (key, child) in map.Entries())
                {
                    result.Set(key, ToProtocolJSONValue(child));
                }

                return result;
            }

            case object?[] array:
            {
                var result = new object?[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    result[i] = ToProtocolJSONValue(array[i]);
                }

                return result;
            }

            default:
                return value;
        }
    }

    /// <summary>Go: <c>func NewConfigFileResponse(parsedCommandLine *tsoptions.ParsedCommandLine) *ConfigFileResponse</c>
    /// （proto.go:983-1009）：nil 入参 → nil；<c>CompileOnSave</c> 缺省时回退到 raw 配置里的 <c>compileOnSave</c>。</summary>
    public static ConfigFileResponse? NewConfigFileResponse(TsOptions.ParsedCommandLine? parsedCommandLine)
    {
        if (parsedCommandLine is null)
        {
            return null;
        }

        var compileOnSave = parsedCommandLine.CompileOnSave;
        if (compileOnSave is null && parsedCommandLine.Raw is OrderedMap<string, object?> rawConfig)
        {
            if (rawConfig.GetOrZero("compileOnSave") is bool value)
            {
                compileOnSave = value;
            }
        }

        var compilerOptions = parsedCommandLine.CompilerOptions();
        var errors = NewDiagnosticResponses(parsedCommandLine.Errors);
        if (errors is null)
        {
            errors = [];
        }

        return new ConfigFileResponse
        {
            FileNames = parsedCommandLine.FileNames(),
            Options = compilerOptions!, // 非空前提见文件头第 10 条
            ProjectReferences = parsedCommandLine.ProjectReferences(),
            TypeAcquisition = parsedCommandLine.TypeAcquisition(),
            CompileOnSave = compileOnSave,
            Raw = ToProtocolJSONValue(parsedCommandLine.Raw),
            Errors = errors,
        };
    }

    /// <summary>Go: <c>func NewProjectResponse(p *project.Project) *ProjectResponse</c>（proto.go:1036-1052）：
    /// 未加载（CommandLine 为 nil）的项目在 Go 里 panic → C# 走 <c>Debug.Fail</c>（文件头第 10 条）。</summary>
    public static ProjectResponse NewProjectResponse(Project.Project p)
    {
        if (p is null || p.CommandLine is null)
        {
            // Go: panic("NewProjectResponse called with unloaded project")
            Debug.Fail("NewProjectResponse called with unloaded project");
            throw new System.Diagnostics.UnreachableException();
        }

        var configFileName = "";
        if (p.Kind == Project.Kind.KindConfigured)
        {
            configFileName = p.ConfigFileName();
        }

        return new ProjectResponse
        {
            Id = p.ID(),
            ConfigFileName = configFileName,
            CurrentDirectory = p.CurrentDirectory(),
            Dirty = p.IsDirty(),
            ParsedCommandLine = NewConfigFileResponse(p.CommandLine)!,
            RootFiles = p.CommandLine.FileNames(),
            CompilerOptions = p.CommandLine.CompilerOptions()!, // 非空前提见文件头第 10 条
        };
    }

    /// <summary>Go: <c>func symbolHandles(symbols []*ast.Symbol) []SymbolID</c>（proto.go:1107-1117）：空输入 → nil。</summary>
    internal static SymbolID[]? SymbolHandles(Ast.Symbol[] symbols)
    {
        if (symbols.Length == 0)
        {
            return null;
        }

        var handles = new SymbolID[symbols.Length];
        for (var i = 0; i < symbols.Length; i++)
        {
            handles[i] = SymbolHandle(symbols[i]);
        }

        return handles;
    }

    /// <summary>Go: <c>func newTypeResponse(t *checker.Type, id TypeID) *TypeResponse</c>（proto.go:1196-1281）：
    /// 按 <c>t.Flags()</c> 的 switch-true 逐类填充（C# 落成 if/else if 链，顺序一致，见文件头第 11 条）。</summary>
    internal static TypeResponse NewTypeResponse(Checker.Type t, TypeID id)
    {
        var resp = new TypeResponse
        {
            Id = id,
            Flags = (uint)t.Flags(),
        };

        if (t.Symbol() is { } symbol)
        {
            resp.Symbol = SymbolHandle(symbol);
        }

        if (t.Alias() is { } alias)
        {
            resp.AliasTypeArguments = TypeHandles(alias.TypeArguments() ?? []);
            if (alias.Symbol() is { } aliasSymbol)
            {
                resp.AliasSymbol = SymbolHandle(aliasSymbol);
            }
        }

        var flags = t.Flags();
        if ((flags & Checker.TypeFlags.Freshable) != 0)
        {
            var lit = t.AsLiteralType()!;
            if ((flags & Checker.TypeFlags.Literal) != 0)
            {
                resp.Value = LiteralValueToJson(lit.Value());
            }

            if (lit.FreshType() is { } freshType)
            {
                resp.FreshType = TypeHandle(freshType);
            }

            if (lit.RegularType() is { } regularType)
            {
                resp.RegularType = TypeHandle(regularType);
            }
        }
        else if ((flags & Checker.TypeFlags.Object) != 0)
        {
            resp.ObjectFlags = (uint)t.ObjectFlags();
            resp.IsTupleType = Checker.Checker.IsTupleType(t);
            var objectFlags = t.ObjectFlags();
            if ((objectFlags & Checker.ObjectFlags.Reference) != 0)
            {
                var @ref = t.AsTypeReference()!;
                if (Checker.Checker.IsTupleTypeTarget(t))
                {
                    var tuple = t.AsTupleType()!;
                    resp.ElementFlags = tuple.ElementFlags();
                    resp.FixedLength = tuple.FixedLength();
                    resp.TupleReadonly = tuple.IsReadonly();
                }

                if (@ref.Target() is { } target)
                {
                    resp.Target = TypeHandle(target);
                }
            }

            if ((objectFlags & Checker.ObjectFlags.ClassOrInterface) != 0)
            {
                var iface = t.AsInterfaceType()!;
                resp.TypeParameters = TypeHandles(iface.TypeParameters());
                resp.OuterTypeParameters = TypeHandles(iface.OuterTypeParameters());
                resp.LocalTypeParameters = TypeHandles(iface.LocalTypeParameters());
                if (iface.ThisType() is { } thisType)
                {
                    resp.ThisType = TypeHandle(thisType);
                }
            }
        }
        else if ((flags & Checker.TypeFlags.UnionOrIntersection) != 0)
        {
            // types omitted; fetched via separate request
        }
        else if ((flags & Checker.TypeFlags.Index) != 0)
        {
            resp.Target = TypeHandle(t.AsIndexType()!.Target()!);
        }
        else if ((flags & Checker.TypeFlags.IndexedAccess) != 0)
        {
            var data = t.AsIndexedAccessType()!;
            resp.ObjectType = TypeHandle(data.ObjectType()!);
            resp.IndexType = TypeHandle(data.IndexType()!);
        }
        else if ((flags & Checker.TypeFlags.Conditional) != 0)
        {
            var data = t.AsConditionalType()!;
            resp.CheckType = TypeHandle(data.CheckType()!);
            resp.ExtendsType = TypeHandle(data.ExtendsType()!);
        }
        else if ((flags & Checker.TypeFlags.Substitution) != 0)
        {
            var data = t.AsSubstitutionType()!;
            resp.BaseType = TypeHandle(data.BaseType()!);
            resp.SubstConstraint = TypeHandle(data.SubstConstraint()!);
        }
        else if ((flags & Checker.TypeFlags.TemplateLiteral) != 0)
        {
            var tl = t.AsTemplateLiteralType()!;
            resp.Texts = tl.Texts();
            // types omitted; fetched via separate request
        }
        else if ((flags & Checker.TypeFlags.StringMapping) != 0)
        {
            resp.Target = TypeHandle(t.AsStringMappingType()!.Target()!);
        }
        else if ((flags & Checker.TypeFlags.TypeParameter) != 0)
        {
            resp.IsThisType = t.AsTypeParameter()!.IsThisType();
        }
        else if ((flags & Checker.TypeFlags.Intrinsic) != 0)
        {
            resp.IntrinsicName = t.AsIntrinsicType()!.IntrinsicName();
        }

        return resp;
    }

    /// <summary>Go: <c>func typeHandles(types []*checker.Type) []TypeID</c>（proto.go:1283-1292）：空输入 → nil。</summary>
    internal static TypeID[]? TypeHandles(Checker.Type[] types)
    {
        if (types.Length == 0)
        {
            return null;
        }

        var handles = new TypeID[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            handles[i] = TypeHandle(types[i]);
        }

        return handles;
    }

    /// <summary>Go: <c>func literalValueToJSON(value any) any</c>（proto.go:1294-1316）：
    /// <c>string</c> 原样；<c>jsnum.Number</c> 的 ±Inf/NaN 编码成字符串，其余 <c>float64</c>；
    /// <c>bool</c> 原样；<c>jsnum.PseudoBigInt</c> 编成有符号十进制字符串（JSON 没有 bigint）；未知类型 → nil。</summary>
    internal static object? LiteralValueToJson(object? value)
    {
        switch (value)
        {
            case string s:
                return s;
            case JsNum.Number number:
                if (number.IsInf())
                {
                    return number.Value > 0 ? "+Infinity" : "-Infinity";
                }

                if (number.IsNaN())
                {
                    return "NaN";
                }

                return number.Value;
            case bool b:
                return b;
            case JsNum.PseudoBigInt bigInt:
                // Encode bigint literals as a signed decimal string (e.g. "-123"); the API client decodes this
                // back into a real bigint. JSON has no bigint.
                return bigInt.ToString();
            default:
                return null;
        }
    }

    /// <summary>Go: <c>func NewPackageId(packageID module.PackageId) *PackageId</c>（proto.go:1398-1408）：Name 为空 → nil。</summary>
    public static PackageId? NewPackageId(Module.PackageId packageID)
    {
        if (packageID.Name.Length == 0)
        {
            return null;
        }

        return new PackageId
        {
            Name = packageID.Name,
            SubModuleName = packageID.SubModuleName,
            Version = packageID.Version,
            PeerDependencies = packageID.PeerDependencies,
        };
    }

    /// <summary>Go: <c>func diagnosticSourceLines(file diagnosticwriter.FileLike, firstLine int, lastLine int) []*DiagnosticSourceLineResponse</c>
    /// （proto.go:1915-1942）：行表为空 → nil；跨度 ≥ 4 行时只取首两行与末两行。</summary>
    internal static DiagnosticSourceLineResponse[]? DiagnosticSourceLines(DiagnosticWriter.FileLike file, int firstLine, int lastLine)
    {
        var lineMap = file.ECMALineMap();
        if (lineMap.Length == 0)
        {
            return null;
        }

        // Go: make([]int, 0, min(lastLine-firstLine+1, 4))
        var lines = new List<int>(Math.Min(lastLine - firstLine + 1, 4));
        if (lastLine - firstLine >= 4)
        {
            lines.Add(firstLine);
            lines.Add(firstLine + 1);
            lines.Add(lastLine - 1);
            lines.Add(lastLine);
        }
        else
        {
            for (var line = firstLine; line <= lastLine; line++)
            {
                lines.Add(line);
            }
        }

        var text = file.Text();
        var result = new List<DiagnosticSourceLineResponse>(lines.Count);
        foreach (var line in lines)
        {
            var start = (int)lineMap[line];
            var end = text.Length;
            if (line + 1 < lineMap.Length)
            {
                end = (int)lineMap[line + 1];
            }

            result.Add(new DiagnosticSourceLineResponse { Line = line, Text = text[start..end] });
        }

        return [.. result];
    }

    /// <summary>Go: <c>func NewDiagnosticResponse(d *ast.Diagnostic) *DiagnosticResponse</c>（proto.go:1944-1946）。</summary>
    public static DiagnosticResponse NewDiagnosticResponse(Ast.Diagnostic d)
    {
        return NewDiagnosticResponse(DW.WrapASTDiagnostic(d));
    }

    /// <summary>Go: <c>func newDiagnosticResponse(d *diagnosticwriter.ASTDiagnostic) *DiagnosticResponse</c>（proto.go:1948-1998）：
    /// 位置按文件文本裁剪并转成 UTF-16；带文件名时补上行列与上下文源码行；消息链/相关信息递归。</summary>
    internal static DiagnosticResponse NewDiagnosticResponse(DiagnosticWriter.ASTDiagnostic d)
    {
        var file = d.File();
        var pos = d.Pos();
        var end = d.End();
        if (file is not null)
        {
            var length = file.Text().Length;
            pos = Math.Max(0, Math.Min(pos, length));
            end = Math.Max(pos, Math.Min(end, length));
        }

        var resp = new DiagnosticResponse
        {
            Pos = pos,
            End = end,
            Code = d.Code(),
            Category = d.Category(),
            Source = d.Source(),
            Text = d.Localize(Loc.Default),
            ReportsUnnecessary = d.Diagnostic.ReportsUnnecessary(),
            ReportsDeprecated = d.Diagnostic.ReportsDeprecated(),
        };

        if (file is not null)
        {
            resp.FileName = file.FileName();
            if (file is Ast.SourceFile sourceFile)
            {
                var positionMap = sourceFile.GetPositionMap();
                resp.Pos = positionMap.UTF8ToUTF16(pos);
                resp.End = positionMap.UTF8ToUTF16(end);
            }
            else
            {
                resp.Pos = Core.Core.UTF16Len(file.Text()[..pos]);
                resp.End = Core.Core.UTF16Len(file.Text()[..end]);
            }

            var (startLine, startCharacter) = Scanner.Scanner.GetECMALineAndUTF16CharacterOfPosition(file, pos);
            var (endLine, endCharacter) = Scanner.Scanner.GetECMALineAndUTF16CharacterOfPosition(file, end);
            resp.StartPosition = new DiagnosticPositionResponse { Line = startLine, Character = startCharacter };
            resp.EndPosition = new DiagnosticPositionResponse { Line = endLine, Character = endCharacter };
            resp.SourceLines = DiagnosticSourceLines(file, startLine, endLine);
        }

        var chain = d.MessageChain();
        if (chain is { Length: > 0 })
        {
            resp.MessageChain = new DiagnosticResponse[chain.Length];
            for (var i = 0; i < chain.Length; i++)
            {
                // Go: chain[i].(*diagnosticwriter.ASTDiagnostic)（文件头第 19 条）
                resp.MessageChain[i] = NewDiagnosticResponse((DiagnosticWriter.ASTDiagnostic)chain[i]);
            }
        }

        var related = d.RelatedInformation();
        if (related is { Length: > 0 })
        {
            resp.RelatedInformation = new DiagnosticResponse[related.Length];
            for (var i = 0; i < related.Length; i++)
            {
                resp.RelatedInformation[i] = NewDiagnosticResponse((DiagnosticWriter.ASTDiagnostic)related[i]);
            }
        }

        return resp;
    }

    /// <summary>Go: <c>func (d *DiagnosticResponse) ToDiagnostic() *ast.Diagnostic</c>（proto.go:2000-2013）。
    /// Go 的方法 → C# 的扩展方法（<see cref="DiagnosticResponse"/> 是兄弟分片的 sealed class，见文件头第 13 条）。</summary>
    public static Ast.Diagnostic ToDiagnostic(this DiagnosticResponse d)
    {
        return Ast.Ast.NewDiagnosticFromText(
            null,
            Core.Core.NewTextRange(d.Pos, d.End),
            d.Code,
            d.Category,
            d.Text,
            Core.Core.Map(d.MessageChain, static r => ToDiagnostic(r!)),
            Core.Core.Map(d.RelatedInformation, static r => ToDiagnostic(r!)),
            d.ReportsUnnecessary,
            d.ReportsDeprecated);
    }

    /// <summary>Go: <c>func NewDiagnosticResponses(diags []*ast.Diagnostic) []*DiagnosticResponse</c>（proto.go:2015-2024）：空输入 → nil。</summary>
    /// <remarks>Go 的 <c>len(diags) == 0</c> 对 **nil 切片**成立（返回 nil）；C# 的 `nil 切片` = <c>null</c>，
    /// 而调用方确实会传 null（例如 <see cref="ProjectCollectionBuilder"/> 里 `CommandLine.Errors` 可能是
    /// Go 的 nil → 本仓的 null，见 ProjectCollectionBuilder.cs:1761/1796/1816 的 `!`），所以形参必须是可空的
    /// （否则 `diags.Length` 直接 NRE：Go 侧不存在这个分支）。</remarks>
    public static DiagnosticResponse[]? NewDiagnosticResponses(Ast.Diagnostic[]? diags)
    {
        if (diags is null || diags.Length == 0)
        {
            return null;
        }

        var result = new DiagnosticResponse[diags.Length];
        for (var i = 0; i < diags.Length; i++)
        {
            result[i] = NewDiagnosticResponse(diags[i]);
        }

        return result;
    }

    /// <summary>Go: <c>func unmarshalPayload(method string, payload json.Value) (any, error)</c>（proto.go:2026-2032）+
    /// 调用点 session.go:710-713 的 <c>fmt.Errorf("%w: %w", ErrInvalidRequest, err)</c> 包装。
    /// C# 侧一次性抛出带前缀的 <see cref="InvalidRequestException"/>（文件头第 7 条）；
    /// 未知方法用 Go 的文本 <c>unknown API method %q</c>。</summary>
    internal static object? UnmarshalPayload(string method, byte[]? payload)
    {
        if (!Unmarshallers.TryGetValue(method, out var unmarshaler))
        {
            throw new InvalidRequestException($"api: invalid request: unknown API method \"{method}\"");
        }

        try
        {
            // Go 的 json.Value 是 []byte；nil（无 payload）→ 空数组，解码时报 "unexpected end of input"（与 Go 同）。
            return unmarshaler(payload ?? []);
        }
        catch (Exception ex)
        {
            throw new InvalidRequestException($"api: invalid request: {ex.Message}", ex);
        }
    }

    /// <summary>Go: <c>func unmarshallerFor[T any](data []byte) (any, error)</c>（proto.go:2034-2040）：
    /// 解出一个新实例；失败时按 Go 的 <c>failed to unmarshal %T: %w</c> 组装文本（文件头第 7 条）。
    /// 名字沿用集成者冻结的形状：<c>Unmarshallers</c> 的每个值都是 <c>static d =&gt; UnmarshalNew&lt;XParams&gt;(d)</c>。</summary>
    private static object? UnmarshalNew<T>(byte[] data) where T : new()
    {
        try
        {
            return JsonStatics.UnmarshalNew<T>(data);
        }
        catch (Exception ex)
        {
            throw new JsonException($"failed to unmarshal *{typeof(T).Name}: {ex.Message}", ex);
        }
    }

    /// <summary>Go: <c>func noParams(data []byte) (any, error) { return nil, nil }</c>（proto.go:2042-2044）。</summary>
    private static object? NoParams(byte[] data) => null;

    /// <summary>Go: <c>func (k jsontext.Kind) String() string</c>（encoding/json/jsontext）在该路径上的取值：
    /// <c>null</c>/<c>false</c>/<c>true</c>/<c>string</c>/<c>number</c>/<c>{</c>/<c>}</c>/<c>[</c>/<c>]</c>。
    /// 本仓同款实现 <c>LsProto.KindString</c> 是 private ⇒ 在本文件复刻（文件头第 4 条）。</summary>
    internal static string JsonKindString(JsonKind kind) => kind switch
    {
        JsonKind.Null => "null",
        JsonKind.False => "false",
        JsonKind.True => "true",
        JsonKind.String => "string",
        JsonKind.Number => "number",
        JsonKind.BeginObject => "{",
        JsonKind.EndObject => "}",
        JsonKind.BeginArray => "[",
        JsonKind.EndArray => "]",
        _ => "",
    };
}

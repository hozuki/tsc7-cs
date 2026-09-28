// ported from: internal/api/proto.go:1-63（`Method` 类型、ID 类型组、`nextBuildOrchestratorId`、四个句柄函数）
//   + internal/api/proto.go:26-28（ErrInvalidRequest / ErrClientError）
//   + internal/api/protocol_msgpack.go:60-62（RawBinary 标记类型）
//
// 本文件是 api 包的"零依赖"地基：错误、方法名常量、句柄值类型。其它 Api/*.cs 分片都引用这里，
// 所以它必须最先落地（集成者持有）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `type Method string` + 一大批 `Method = "..."` 常量 → C# 没有"具名字符串类型 + 常量"的等价物
//    （`enum` 无法承载任意字符串且要 `String()` 转换）。落成 **静态类 `Method` 的 `const string`**：
//    于是 `unmarshalers` 表的键就是 `string`，Go 的 `Method(method)` 转换在 C# 里消失（调用点直接传 string）。
//
// 2) Go 的 ID 类型（`SnapshotID`/`ModuleResolverID`/…）是底层整数的新类型：`SnapshotID(handle)`、`x != 0`。
//    C# 落成 **readonly struct + `Value` 字段**，先例 `TypeScript.Checker.TypeId`/`Ast.NodeId`（Checker/Types.cs:188）。
//    差异：Go 的 `x != 0` / `SnapshotID(5)` 在 C# 里写成 `x.Value != 0` / `new SnapshotID(5)`；
//    为减少调用点噪音，同时提供与底层整数之间的 `implicit` 转换（两个方向都隐式，与 TypeId 一致）。
//
// 3) **这些 ID 类型需要 IJsonMarshaler**：Go 的 `func (...) HandleXxx(...) (any, error)` 会把裸 ID 当 JSON 数字
//    返回（`protocol_msgpack.go` 的 `json.Marshal(result)`），C# 的 Json 编码器对没有 `IJsonMarshaler` 的类
//    直接抛异常（Json/Encode.cs 的 default 分支）。**不实现 IJsonUnmarshaler**：C# 的 readonly struct 无法在
//    `UnmarshalJSONFrom` 里写回字段（会丢更新），所以解码一律走生成代码里的 `new X(dec.Decode<ulong>())`
//    —— 显式、无装箱、AOT 安全。
//
// 4) `NodeHandle string` → `readonly struct NodeHandle` 包一个 `string`（理由同 2：Go 里它是独立类型，
//    出现在 JSON 里是字符串）。提供与 `string` 的双向 implicit 转换，调用点（拼接、插值、map 键）写法不变。
//
// 5) `RawBinary []byte` → `sealed class RawBinary`（Go 的切片无法在 C# 里"标记"一个类型）：它是
//    ProtocolMsgPack.WriteResponse 的"这段 payload 是原始二进制、不要 JSON 编码"标记，字段 `Data` 即内容。
//    注意：**不能**让它实现 IJsonMarshaler —— 它的存在意义就是绕开 JSON 编码（见 protocol_msgpack.go:222-230）。
using System.Globalization;
using TypeScript.Json;
using JsonEncoder = TypeScript.Json.Encoder;

namespace TypeScript.Api;

/// <summary>Go: <c>var ErrInvalidRequest = errors.New("api: invalid request")</c>（proto.go:27）。
/// Go 的 `fmt.Errorf("%w: …", ErrInvalidRequest)` → C# 的 <see cref="InvalidRequestException"/>（文本 + InnerException）。</summary>
public sealed class InvalidRequestException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Go: <c>var ErrClientError = errors.New("api: client error")</c>（proto.go:28）。</summary>
public sealed class ClientErrorException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Go: <c>type Method string</c>（proto.go:31）与随之的常量块（proto.go:65-260）。
/// C# 用 `const string` 承载（见文件头第 1 条）；成员名与 Go 常量名逐字一致（去掉 `Method` 前缀）。
/// </summary>
public static class Method
{
    public const string Release = "release";
    public const string ReleaseSourceFile = "releaseSourceFile";

    public const string BatchRequests = "batchRequests";
    public const string Initialize = "initialize";
    public const string CreateSnapshot = "createSnapshot";
    public const string UpdateSnapshot = "updateSnapshot";
    public const string GetCurrentLanguageServerSnapshot = "getCurrentLanguageServerSnapshot";
    public const string CreateBuildOrchestrator = "createBuildOrchestrator";
    public const string DisposeBuildOrchestrator = "disposeBuildOrchestrator";
    public const string Build = "build";
    public const string BuildReferences = "buildReferences";
    public const string CleanBuild = "cleanBuild";
    public const string CleanReferences = "cleanReferences";
    public const string CreateModuleResolver = "createModuleResolver";
    public const string ReleaseModuleResolver = "releaseModuleResolver";
    public const string ResolveModuleName = "resolveModuleName";
    public const string ParseCommandLine = "parseCommandLine";
    public const string ReadConfigFile = "readConfigFile";
    public const string ParseJsonConfigFile = "parseJsonConfigFileContent";
    public const string ParseConfigFile = "parseConfigFile";
    public const string CreateSourceFile = "createSourceFile";
    public const string CreateSourceFileFromFile = "createSourceFileFromFile";
    public const string TranspileModule = "transpileModule";
    public const string TranspileModuleFromFile = "transpileModuleFromFile";
    public const string TranspileDeclaration = "transpileDeclaration";
    public const string TranspileDeclarationFromFile = "transpileDeclarationFromFile";
    public const string GetDefaultProjectForFile = "getDefaultProjectForFile";
    public const string GetSymbolAtPosition = "getSymbolAtPosition";
    public const string GetSymbolsAtPositions = "getSymbolsAtPositions";
    public const string GetSymbolAtLocation = "getSymbolAtLocation";
    public const string GetSymbolsAtLocations = "getSymbolsAtLocations";
    public const string GetSymbolOfSourceFile = "getSymbolOfSourceFile";
    public const string GetSymbolsOfSourceFiles = "getSymbolsOfSourceFiles";
    public const string GetTypeOfSymbol = "getTypeOfSymbol";
    public const string GetTypesOfSymbols = "getTypesOfSymbols";
    public const string GetDeclaredTypeOfSymbol = "getDeclaredTypeOfSymbol";
    public const string GetNonMissingTypeOfSymbol = "getNonMissingTypeOfSymbol";
    public const string GetSourceFile = "getSourceFile";
    public const string GetSourceFileNames = "getSourceFileNames";
    public const string GetSourceFileMetadata = "getSourceFileMetadata";
    public const string GetModeForUsageLocation = "getModeForUsageLocation";
    public const string GetModeForResolutionAtIndex = "getModeForResolutionAtIndex";
    public const string GetResolvedModule = "getResolvedModule";
    public const string GetResolvedModuleFromModuleSpecifier = "getResolvedModuleFromModuleSpecifier";
    public const string GetResolvedTypeReferenceDirective = "getResolvedTypeReferenceDirective";
    public const string GetResolvedTypeReferenceDirectiveFromReference = "getResolvedTypeReferenceDirectiveFromTypeReferenceDirective";
    public const string GetConfigFileNames = "getConfigFileNames";
    public const string GetConfigSourceFile = "getConfigSourceFile";
    public const string ResolveName = "resolveName";
    public const string GetSymbolsInScope = "getSymbolsInScope";
    public const string GetSignaturesOfType = "getSignaturesOfType";
    public const string GetResolvedSignature = "getResolvedSignature";
    public const string GetTypeAtLocation = "getTypeAtLocation";
    public const string GetTypeAtLocations = "getTypeAtLocations";
    public const string GetTypeAtPosition = "getTypeAtPosition";
    public const string GetTypesAtPositions = "getTypesAtPositions";

    // Symbol sub-property methods
    public const string GetParentOfSymbol = "getParentOfSymbol";
    public const string GetMembersOfSymbol = "getMembersOfSymbol";
    public const string GetExportsOfSymbol = "getExportsOfSymbol";
    public const string GetExportSymbolOfSymbol = "getExportSymbolOfSymbol";

    // Type sub-property methods
    public const string GetSymbolOfType = "getSymbolOfType";
    public const string GetTargetOfType = "getTargetOfType";
    public const string GetFreshTypeOfType = "getFreshTypeOfType";
    public const string GetRegularTypeOfType = "getRegularTypeOfType";
    public const string GetTypesOfType = "getTypesOfType";
    public const string GetTypeParametersOfType = "getTypeParametersOfType";
    public const string GetOuterTypeParametersOfType = "getOuterTypeParametersOfType";
    public const string GetLocalTypeParametersOfType = "getLocalTypeParametersOfType";
    public const string GetThisTypeOfType = "getThisTypeOfType";
    public const string GetAliasTypeArgumentsOfType = "getAliasTypeArgumentsOfType";
    public const string GetAliasSymbolOfType = "getAliasSymbolOfType";
    public const string GetObjectTypeOfType = "getObjectTypeOfType";
    public const string GetIndexTypeOfType = "getIndexTypeOfType";
    public const string GetCheckTypeOfType = "getCheckTypeOfType";
    public const string GetExtendsTypeOfType = "getExtendsTypeOfType";
    public const string GetBaseTypeOfType = "getBaseTypeOfType";
    public const string GetConstraintOfType = "getConstraintOfType";
    public const string GetTypeParameterOfMappedType = "getTypeParameterOfMappedType";
    public const string GetConstraintTypeOfMappedType = "getConstraintTypeOfMappedType";
    public const string GetNameTypeOfMappedType = "getNameTypeOfMappedType";
    public const string GetTemplateTypeOfMappedType = "getTemplateTypeOfMappedType";

    // Signature sub-property methods
    public const string GetTypeParametersOfSignature = "getTypeParametersOfSignature";
    public const string GetParametersOfSignature = "getParametersOfSignature";
    public const string GetThisParameterOfSignature = "getThisParameterOfSignature";
    public const string GetTargetOfSignature = "getTargetOfSignature";

    // Checker methods
    public const string GetContextualType = "getContextualType";
    public const string GetContextualTypeForArgument = "getContextualTypeForArgument";
    public const string GetAwaitedType = "getAwaitedType";
    public const string GetBaseTypeOfLiteralType = "getBaseTypeOfLiteralType";
    public const string GetNonNullableType = "getNonNullableType";
    public const string GetTypeFromTypeNode = "getTypeFromTypeNode";
    public const string GetWidenedType = "getWidenedType";
    public const string GetParameterType = "getParameterType";
    public const string GetTypeParameterAtPosition = "getTypeParameterAtPosition";
    public const string IsArrayLikeType = "isArrayLikeType";
    public const string IsTypeAssignableTo = "isTypeAssignableTo";
    public const string GetShorthandAssignmentValueSymbol = "getShorthandAssignmentValueSymbol";
    public const string GetTypeOfSymbolAtLocation = "getTypeOfSymbolAtLocation";
    public const string TypeToTypeNode = "typeToTypeNode";
    public const string SignatureToSignatureDeclaration = "signatureToSignatureDeclaration";
    public const string TypeToString = "typeToString";
    public const string IsContextSensitive = "isContextSensitive";
    public const string GetReturnTypeOfSignature = "getReturnTypeOfSignature";
    public const string GetRestTypeOfSignature = "getRestTypeOfSignature";
    public const string GetTypePredicateOfSignature = "getTypePredicateOfSignature";
    public const string GetBaseTypes = "getBaseTypes";
    public const string GetPropertiesOfType = "getPropertiesOfType";
    public const string GetApparentPropertiesOfType = "getApparentPropertiesOfType";
    public const string GetApparentType = "getApparentType";
    public const string GetReducedType = "getReducedType";
    public const string GetPropertyOfType = "getPropertyOfType";
    public const string GetTypeOfPropertyOfType = "getTypeOfPropertyOfType";
    public const string GetIndexInfoOfType = "getIndexInfoOfType";
    public const string GetIndexInfosOfType = "getIndexInfosOfType";
    public const string GetConstraintOfTypeParameter = "getConstraintOfTypeParameter";
    public const string GetDefaultFromTypeParameter = "getDefaultFromTypeParameter";
    public const string GetBaseConstraintOfType = "getBaseConstraintOfType";
    public const string GetTypeArguments = "getTypeArguments";
    public const string GetImportAdderEdits = "getImportAdderEdits";
    public const string GetTrueTypeOfConditionalType = "getTrueTypeOfConditionalType";
    public const string GetFalseTypeOfConditionalType = "getFalseTypeOfConditionalType";
    public const string GetConstantValue = "getConstantValue";
    public const string GetSignatureFromDeclaration = "getSignatureFromDeclaration";
    public const string GetExportSpecifierLocalTarget = "getExportSpecifierLocalTargetSymbol";
    public const string GetAliasedSymbol = "getAliasedSymbol";
    public const string GetImmediateAliasedSymbol = "getImmediateAliasedSymbol";
    public const string GetTargetSymbol = "getTargetSymbol";
    public const string GetExportSymbolOfSymbolForChecker = "getExportSymbolOfSymbolForChecker";
    public const string GetFullyQualifiedName = "getFullyQualifiedName";
    public const string GetExportsOfModule = "getExportsOfModule";
    public const string GetMemberInModuleExports = "getMemberInModuleExports";
    public const string GetJSDocTags = "getJsDocTags";
    public const string GetDocumentationComment = "getDocumentationComment";
    public const string IsArrayType = "isArrayType";
    public const string IsReadonlySymbol = "isReadonlySymbol";

    // Reference methods
    public const string GetReferencesToSymbolInFile = "getReferencesToSymbolInFile";
    public const string GetReferencedSymbolsForNode = "getReferencedSymbolsForNode";
    public const string GetSignatureUsages = "getSignatureUsages";

    // Language service methods
    public const string GetCompletionsAtPosition = "getCompletionsAtPosition";

    // Diagnostic methods
    public const string GetSyntacticDiagnostics = "getSyntacticDiagnostics";
    public const string GetBindDiagnostics = "getBindDiagnostics";
    public const string GetSemanticDiagnostics = "getSemanticDiagnostics";
    public const string GetSuggestionDiagnostics = "getSuggestionDiagnostics";
    public const string GetDeclarationDiagnostics = "getDeclarationDiagnostics";
    public const string GetProgramDiagnostics = "getProgramDiagnostics";
    public const string GetGlobalDiagnostics = "getGlobalDiagnostics";
    public const string GetConfigFileParsingDiagnostics = "getConfigFileParsingDiagnostics";

    // Printer methods
    public const string PrintNode = "printNode";
    public const string FormatNodeForInsertion = "formatNodeForInsertion";
    public const string Emit = "emit";
    public const string EmitToString = "emitToString";
    public const string GetJavaScriptEmit = "getJavaScriptEmit";
    public const string GetDeclarationEmit = "getDeclarationEmit";

    // Intrinsic type getters
    public const string GetAnyType = "getAnyType";
    public const string GetStringType = "getStringType";
    public const string GetNumberType = "getNumberType";
    public const string GetBooleanType = "getBooleanType";
    public const string GetVoidType = "getVoidType";
    public const string GetUndefinedType = "getUndefinedType";
    public const string GetNullType = "getNullType";
    public const string GetNeverType = "getNeverType";
    public const string GetUnknownType = "getUnknownType";
    public const string GetBigIntType = "getBigIntType";
    public const string GetESSymbolType = "getESSymbolType";
    public const string GetNonPrimitiveType = "getNonPrimitiveType";

    // Well-known per-checker symbols
    public const string GetWellKnownSymbols = "getWellKnownSymbols";

    // Well-known per-checker signatures
    public const string GetWellKnownSignatures = "getWellKnownSignatures";

    // Profiling methods
    public const string StartCPUProfile = "startCPUProfile";
    public const string StopCPUProfile = "stopCPUProfile";
    public const string SaveHeapProfile = "saveHeapProfile";
}

/// <summary>Go: <c>type SnapshotID uint64</c>（proto.go:39）。</summary>
public readonly struct SnapshotID(ulong value) : IEquatable<SnapshotID>, IComparable<SnapshotID>, IJsonMarshaler
{
    public readonly ulong Value = value;

    public static implicit operator ulong(SnapshotID id) => id.Value;

    public static implicit operator SnapshotID(ulong value) => new(value);

    public bool Equals(SnapshotID other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SnapshotID other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(SnapshotID other) => Value.CompareTo(other.Value);

    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteRawValue(Value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Go: <c>type ModuleResolverID uint64</c>（proto.go:40）。</summary>
public readonly struct ModuleResolverID(ulong value) : IEquatable<ModuleResolverID>, IComparable<ModuleResolverID>, IJsonMarshaler
{
    public readonly ulong Value = value;

    public static implicit operator ulong(ModuleResolverID id) => id.Value;

    public static implicit operator ModuleResolverID(ulong value) => new(value);

    public bool Equals(ModuleResolverID other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is ModuleResolverID other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(ModuleResolverID other) => Value.CompareTo(other.Value);

    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteRawValue(Value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Go: <c>type SourceFileLeaseID uint64</c>（proto.go:41）。</summary>
public readonly struct SourceFileLeaseID(ulong value) : IEquatable<SourceFileLeaseID>, IComparable<SourceFileLeaseID>, IJsonMarshaler
{
    public readonly ulong Value = value;

    public static implicit operator ulong(SourceFileLeaseID id) => id.Value;

    public static implicit operator SourceFileLeaseID(ulong value) => new(value);

    public bool Equals(SourceFileLeaseID other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SourceFileLeaseID other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(SourceFileLeaseID other) => Value.CompareTo(other.Value);

    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteRawValue(Value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Go: <c>type BuildOrchestratorID uint64</c>（proto.go:42）。</summary>
public readonly struct BuildOrchestratorID(ulong value) : IEquatable<BuildOrchestratorID>, IComparable<BuildOrchestratorID>, IJsonMarshaler
{
    public readonly ulong Value = value;

    public static implicit operator ulong(BuildOrchestratorID id) => id.Value;

    public static implicit operator BuildOrchestratorID(ulong value) => new(value);

    public bool Equals(BuildOrchestratorID other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is BuildOrchestratorID other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(BuildOrchestratorID other) => Value.CompareTo(other.Value);

    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteRawValue(Value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Go: <c>type SymbolID uint64</c>（proto.go:43）。</summary>
public readonly struct SymbolID(ulong value) : IEquatable<SymbolID>, IComparable<SymbolID>, IJsonMarshaler
{
    public readonly ulong Value = value;

    public static implicit operator ulong(SymbolID id) => id.Value;

    public static implicit operator SymbolID(ulong value) => new(value);

    public bool Equals(SymbolID other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SymbolID other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(SymbolID other) => Value.CompareTo(other.Value);

    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteRawValue(Value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Go: <c>type TypeID uint32</c>（proto.go:44）。</summary>
public readonly struct TypeID(uint value) : IEquatable<TypeID>, IComparable<TypeID>, IJsonMarshaler
{
    public readonly uint Value = value;

    public static implicit operator uint(TypeID id) => id.Value;

    public static implicit operator TypeID(uint value) => new(value);

    public bool Equals(TypeID other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is TypeID other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(TypeID other) => Value.CompareTo(other.Value);

    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteRawValue(Value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Go: <c>type SignatureID uint64</c>（proto.go:45）。</summary>
public readonly struct SignatureID(ulong value) : IEquatable<SignatureID>, IComparable<SignatureID>, IJsonMarshaler
{
    public readonly ulong Value = value;

    public static implicit operator ulong(SignatureID id) => id.Value;

    public static implicit operator SignatureID(ulong value) => new(value);

    public bool Equals(SignatureID other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SignatureID other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(SignatureID other) => Value.CompareTo(other.Value);

    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteRawValue(Value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Go: <c>type NodeHandle string</c>（proto.go:46）。
/// Go 里是"文件名:节点下标"形式的字符串（见 session.go 的 <c>nodeHandleFrom</c>/<c>resolveNodeHandle</c>），
/// C# 侧包一层 readonly struct 以保留类型区分（文件头第 4 条）；与 <c>string</c> 双向隐式转换，调用点写法不变。</summary>
public readonly struct NodeHandle : IEquatable<NodeHandle>, IComparable<NodeHandle>, IJsonMarshaler
{
    /// <summary>**故意可空**：`default(NodeHandle)`（proto 解码前的占位、`new NodeHandle[3]` 的零填充）必须
    /// 行为等同于 Go 的零值 `NodeHandle("")`，所以对外只暴露把 null 归一成空串的 <see cref="Value"/>（本仓
    /// 对 `new T[n]` 零填充 null 的既定处理方式，见 docs/architecture.md）。</summary>
    private readonly string? _value;

    public NodeHandle(string value)
    {
        _value = value;
    }

    public string Value => _value ?? "";

    public static implicit operator string(NodeHandle handle) => handle.Value;

    public static implicit operator NodeHandle(string value) => new(value);

    public bool Equals(NodeHandle other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is NodeHandle other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    public int CompareTo(NodeHandle other) => string.CompareOrdinal(Value, other.Value);

    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteStringValue(Value);
}

/// <summary>Go: <c>type RawBinary []byte</c>（protocol_msgpack.go:281）。msgpack 协议下"原样写二进制"的标记类型
/// （见文件头第 5 条）。</summary>
public sealed class RawBinary(byte[] data)
{
    public readonly byte[] Data = data;
}

/// <summary>Go 的 api 包级函数与包级变量（C# 没有自由函数，落成静态类 `Api`，先例 `TypeScript.Ipc.Ipc`）。</summary>
public static partial class Api
{
    /// <summary>Go: <c>var nextBuildOrchestratorId atomic.Uint64</c>（proto.go:50）。</summary>
    private static long _nextBuildOrchestratorId;

    /// <summary>Go: <c>func NewBuildOrchestratorID() BuildOrchestratorID</c>（proto.go:52-54）。
    /// Go 的 `atomic.Uint64.Add(1)` → C# 的 `Interlocked.Increment`（从 1 开始，与 Go 一致）。</summary>
    public static BuildOrchestratorID NewBuildOrchestratorID()
    {
        return new BuildOrchestratorID((ulong)Interlocked.Increment(ref _nextBuildOrchestratorId));
    }

    /// <summary>Go: <c>func snapshotHandle(snapshot *project.Snapshot) SnapshotID</c>（session.go:544-546）：
    /// 由快照 ID 生成句柄。Go 的未导出包级函数 → C# 的 `internal static`（见本文件头）。</summary>
    internal static SnapshotID SnapshotHandle(Project.Snapshot snapshot)
    {
        return new SnapshotID(snapshot.ID());
    }

    /// <summary>Go: <c>func SymbolHandle(symbol *ast.Symbol) SymbolID</c>（proto.go:56-58）。</summary>
    public static SymbolID SymbolHandle(Ast.Symbol symbol)
    {
        return new SymbolID(Ast.Ast.GetSymbolId(symbol).Value);
    }

    /// <summary>Go: <c>func TypeHandle(t *checker.Type) TypeID</c>（proto.go:60-62）。</summary>
    public static TypeID TypeHandle(Checker.Type type)
    {
        return new TypeID(type.Id().Value);
    }

    /// <summary>Go: <c>func SignatureHandle(sig *checker.Signature) SignatureID</c>（proto.go:64-66）。</summary>
    public static SignatureID SignatureHandle(Checker.Signature signature)
    {
        return new SignatureID(signature.Id().Value);
    }
}

// ported from: internal/api/session.go:694-1212（HandleRequest 的巨型 switch、handleBatchRequests、newBatchResponsePage、
//   paginateBatchResponses、handleBatchRequest、isSourceFileResponseMethod、handleStartCPUProfile、handleStopCPUProfile、
//   handleSaveHeapProfile）+ internal/api/session.go:1213-1216（HandleNotification）+ internal/api/session.go:1218-1225（handleInitialize）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `s.useBinaryResponses` → `_useBinaryResponses`（契约 §4 的字段名表）；`RawBinary(params)` → `new RawBinary(@params ?? [])`：
//    Go 的 `json.Value` 是 []byte，nil 表示"空 payload"，而 C# 的 `RawBinary.Data` 是非空 `byte[]` 字段，
//    故 nil 映射为空数组（msgpack 层对二者的处理完全一致）。
// 2) Go 的 `if s != nil && s.withLocale != nil` → C# 无条件 `_withLocale(ctx)`：契约 §4 规定该字段非空（SessionCore
//    的冻结契约：newSession 会把 nil 替换成恒等函数），且 C# 的实例方法不可能在 null 实例上调用、也没有"nil 函数值"。
// 3) `unmarshalPayload` 的 `fmt.Errorf("%w: %w", ErrInvalidRequest, err)` 包装 → `Api.UnmarshalPayload` **内部**已经抛
//    `InvalidRequestException`（集成者冻结签名：`internal static object? Api.UnmarshalPayload(string method, byte[]? payload)`），
//    所以这里没有 try/catch。对客户端可观测的结果与 Go 相同（都是 "api: invalid request: …"，文本由 Api.cs 负责）。
// 4) 巨型 switch：Go 的 `case string(MethodXxx)` → C# 的 `case Method.Xxx`（`Method` 是 `const string` 静态类，可直接作
//    case 标签）；`parsed.(*XxxParams)` → `(XxxParams)parsed!`（`!` = 编译期非空断言，表达 Go"类型断言必定成功"的前提）。
// 5) default 分支：Go 是**裸** error（没有 ErrInvalidRequest 包装）`fmt.Errorf("unknown method: %s", method)`；C# 抛文本
//    完全相同的 `InvalidRequestException` —— C# 用异常表达错误路径，而 api 包最贴近"请求错误"的类型就是它；ipc 层对
//    非 DebugFailureException 的异常只看文本，因此对外行为与 Go 一致。
// 6) `s.batchResponsePages`（sync.Map）→ `_batchResponsePages`（ConcurrentDictionary）；`LoadAndDelete` → `TryRemove`；
//    `Store` → 索引器赋值；`nextBatchResponsePageID.Add(1)` → `Interlocked.Increment(ref _nextBatchResponsePageId)`
//    （Go 的 atomic.Uint64 → C# `long` 字段，见契约 §4 字段表）/ `fmt.Sprintf("%s-%d", …)` → 字符串插值。
// 7) handleBatchRequests 里 Go 的 `if s == nil { … }` 在 C# 无对应物（实例方法不会在 null 实例上被调用）→ 省略。
// 8) `debug.Stack()` → `panic.ToString()`：本仓既有先例 `Ipc/ConnSync.cs:219` / `Ipc/ConnAsync.cs:376` 的
//    `$"panic: {panic.Message}\n{panic}"`（C# 的 panic 文本 = message + Exception.ToString() 里的栈）。
//    Go 的 `recover()` 在 C# 落成 `catch (DebugFailureException)`（契约 §3.2），普通错误落成 `catch (Exception)`
//    —— 正好对应 Go 的 panic 分支与 `err != nil` 分支，且 ipc 层就是用异常类型区分二者。
// 9) 切片 → C# 数组 + Range：`page.encodedResponses[:n]` → `page.EncodedResponses[..n]`，`[n:]` → `[n..]`；
//    `len(x)` → `x.Length`；`for _, v := range xs` → `foreach`（需要下标时用 for）。
// 10) `json.Marshal(&responses[i])` → `TypeScript.Json.Json.Marshal(responses[i])`（Go 的 error 返回值 → C# 抛异常，
//     错误路径一致：直接冒泡给 ipc 层，**不**包 ErrClientError）。
// 11) `pprof.CPUProfiler` 的 `(T, error)` → 本仓 `Execute.CPUProfiler` 的 `TryX(out …)` 形状（见 Execute/Pprof.cs 文件头
//     偏离 3/5）：StartCPUProfile 失败只可能是 "CPU profiling already in progress"，SaveHeapProfile 在 .NET 没有堆
//     profile 导出通道 → 恒失败。错误文本按 Go 的 `%w: …: %w` 包装格式手工构造（内层原因不是 Go 的 runtime/pprof 文本，
//     而是本仓 no-op 实现的固定说明）。
// 12) `s.cpuProfiler`（Go 的非指针字段，零值即可用）→ `_cpuProfiler` 在本仓是**可空字段**（契约 §4）→ 用 `??=` 惰性
//     构造，语义等价于 Go 的零值 `pprof.CPUProfiler{}`。
// 13) `params == nil` 检查（handleStartCPUProfile/handleSaveHeapProfile）被保留：参数声明为 `ProfileParams?`，因为
//     `(ProfileParams)parsed!` 在 parsed 为 null 时会得到 null，Go 的 `params == nil` 分支因此仍然可达。
// 14) `batchResponsePage`（session.go:459-462）与 `DefaultMaxResponseBytesPerPage`（session.go:472-474）不在本切片 →
//     由 `Api/Session.cs` 声明（`internal sealed class BatchResponsePage { public byte[][] EncodedResponses = []; }`、
//     `Session.DefaultMaxResponseBytesPerPage`，SessionCore 冻结形状），本文件直接使用。
// 15) `isSourceFileResponseMethod(method Method)` → C# `IsSourceFileResponseMethod(string method)`：C# 里没有独立的
//     `Method` 字符串类型（契约 §4 第 1 条），字段/参数类型一律是 `string`。
using TypeScript.Core;

namespace TypeScript.Api;

/// <summary>Go: <c>type Session struct</c>（session.go:417-458）的请求分发切片。</summary>
public sealed partial class Session
{
    /// <summary>
    /// Go: <c>func (s *Session) HandleRequest(ctx context.Context, method string, params json.Value) (any, error)</c>
    /// （session.go:694-1060）。
    /// </summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        ctx = _withLocale(ctx);

        // Handle simple methods that don't need param parsing
        switch (method)
        {
            case "echo":
                // Return raw binary for msgpack protocol compatibility
                if (_useBinaryResponses)
                {
                    return new RawBinary(@params ?? []);
                }
                return @params;
            case "ping":
                return "pong";
        }

        var parsed = Api.UnmarshalPayload(method, @params);

        switch (method)
        {
            case Method.BatchRequests:
                return HandleBatchRequests(ctx, (BatchRequestsParams)parsed!);
            case Method.Release:
                return HandleRelease(ctx, (ReleaseParams)parsed!);
            case Method.ReleaseSourceFile:
                return HandleReleaseSourceFile((ReleaseSourceFileParams)parsed!);
            case Method.Initialize:
                return HandleInitialize(ctx);
            case Method.CreateSnapshot:
                return HandleCreateSnapshot(ctx, (CreateSnapshotParams)parsed!);
            case Method.UpdateSnapshot:
                return HandleUpdateSnapshot(ctx, (UpdateSnapshotParams)parsed!);
            case Method.GetCurrentLanguageServerSnapshot:
                return HandleGetCurrentLanguageServerSnapshot(ctx, (GetCurrentLanguageServerSnapshotParams)parsed!);
            case Method.CreateModuleResolver:
                return HandleCreateModuleResolver((CreateModuleResolverParams)parsed!);
            case Method.ReleaseModuleResolver:
                return HandleReleaseModuleResolver((ReleaseModuleResolverParams)parsed!);
            case Method.ResolveModuleName:
                return HandleResolveModuleName(ctx, (ResolveModuleNameParams)parsed!);
            case Method.ParseCommandLine:
                return HandleParseCommandLine(ctx, (ParseCommandLineParams)parsed!);
            case Method.ReadConfigFile:
                return HandleReadConfigFile(ctx, (ReadConfigFileParams)parsed!);
            case Method.ParseJsonConfigFile:
                return HandleParseJsonConfigFileContent(ctx, (ParseJsonConfigFileContentParams)parsed!);
            case Method.ParseConfigFile:
                return HandleParseConfigFile(ctx, (ParseConfigFileParams)parsed!);
            case Method.CreateBuildOrchestrator:
                return HandleCreateBuildOrchestrator(ctx, (CreateBuildOrchestratorParams)parsed!);
            case Method.DisposeBuildOrchestrator:
                return HandleDisposeBuildOrchestrator(ctx, (DisposeBuildOrchestratorParams)parsed!);
            case Method.Build:
                return HandleBuild(ctx, (BuildParams)parsed!);
            case Method.BuildReferences:
                return HandleBuildReferences(ctx, (BuildParams)parsed!);
            case Method.CleanBuild:
                return HandleCleanBuild(ctx, (CleanBuildParams)parsed!);
            case Method.CleanReferences:
                return HandleCleanReferences(ctx, (CleanBuildParams)parsed!);
            case Method.CreateSourceFile:
                return HandleCreateSourceFile(ctx, (CreateSourceFileParams)parsed!);
            case Method.CreateSourceFileFromFile:
                return HandleCreateSourceFileFromFile(ctx, (CreateSourceFileFromFileParams)parsed!);
            case Method.TranspileModule:
                return HandleTranspile(ctx, (TranspileParams)parsed!, false);
            case Method.TranspileModuleFromFile:
                return HandleTranspileFromFile(ctx, (TranspileFromFileParams)parsed!, false);
            case Method.TranspileDeclaration:
                return HandleTranspile(ctx, (TranspileParams)parsed!, true);
            case Method.TranspileDeclarationFromFile:
                return HandleTranspileFromFile(ctx, (TranspileFromFileParams)parsed!, true);
            case Method.GetDefaultProjectForFile:
                return HandleGetDefaultProjectForFile(ctx, (GetDefaultProjectForFileParams)parsed!);
            case Method.GetSourceFile:
                return HandleGetSourceFile(ctx, (GetSourceFileParams)parsed!);
            case Method.GetSourceFileNames:
                return HandleGetSourceFileNames(ctx, (GetSourceFileNamesParams)parsed!);
            case Method.GetSourceFileMetadata:
                return HandleGetSourceFileMetadata(ctx, (GetSourceFileParams)parsed!);
            case Method.GetModeForUsageLocation:
                return HandleGetModeForUsageLocation(ctx, (GetModeForUsageLocationParams)parsed!);
            case Method.GetModeForResolutionAtIndex:
                return HandleGetModeForResolutionAtIndex(ctx, (GetModeForResolutionAtIndexParams)parsed!);
            case Method.GetResolvedModule:
                return HandleGetResolvedModule(ctx, (GetResolvedModuleParams)parsed!);
            case Method.GetResolvedModuleFromModuleSpecifier:
                return HandleGetResolvedModuleFromModuleSpecifier(ctx, (GetResolvedModuleFromModuleSpecifierParams)parsed!);
            case Method.GetResolvedTypeReferenceDirective:
                return HandleGetResolvedTypeReferenceDirective(ctx, (GetResolvedTypeReferenceDirectiveParams)parsed!);
            case Method.GetResolvedTypeReferenceDirectiveFromReference:
                return HandleGetResolvedTypeReferenceDirectiveFromReference(ctx, (GetResolvedTypeReferenceDirectiveFromReferenceParams)parsed!);
            case Method.GetConfigFileNames:
                return HandleGetConfigFileNames(ctx, (GetProjectDiagnosticsParams)parsed!);
            case Method.GetConfigSourceFile:
                return HandleGetConfigSourceFile(ctx, (GetSourceFileParams)parsed!);
            case Method.GetSymbolAtPosition:
                return HandleGetSymbolAtPosition(ctx, (GetSymbolAtPositionParams)parsed!);
            case Method.GetSymbolsAtPositions:
                return HandleGetSymbolsAtPositions(ctx, (GetSymbolsAtPositionsParams)parsed!);
            case Method.GetSymbolAtLocation:
                return HandleGetSymbolAtLocation(ctx, (GetSymbolAtLocationParams)parsed!);
            case Method.GetSymbolsAtLocations:
                return HandleGetSymbolsAtLocations(ctx, (GetSymbolsAtLocationsParams)parsed!);
            case Method.GetSymbolOfSourceFile:
                return HandleGetSymbolOfSourceFile(ctx, (GetSymbolOfSourceFileParams)parsed!);
            case Method.GetSymbolsOfSourceFiles:
                return HandleGetSymbolsOfSourceFiles(ctx, (GetSymbolsOfSourceFilesParams)parsed!);
            case Method.GetTypeOfSymbol:
                return HandleGetTypeOfSymbol(ctx, (GetTypeOfSymbolParams)parsed!);
            case Method.GetTypesOfSymbols:
                return HandleGetTypesOfSymbols(ctx, (GetTypesOfSymbolsParams)parsed!);
            case Method.GetDeclaredTypeOfSymbol:
                return HandleGetDeclaredTypeOfSymbol(ctx, (GetTypeOfSymbolParams)parsed!);
            case Method.GetNonMissingTypeOfSymbol:
                return HandleGetNonMissingTypeOfSymbol(ctx, (GetTypeOfSymbolParams)parsed!);
            case Method.ResolveName:
                return HandleResolveName(ctx, (ResolveNameParams)parsed!);
            case Method.GetSymbolsInScope:
                return HandleGetSymbolsInScope(ctx, (GetSymbolsInScopeParams)parsed!);
            case Method.GetSignaturesOfType:
                return HandleGetSignaturesOfType(ctx, (GetSignaturesOfTypeParams)parsed!);
            case Method.GetResolvedSignature:
                return HandleGetResolvedSignature(ctx, (GetResolvedSignatureParams)parsed!);
            case Method.GetTypeAtLocation:
                return HandleGetTypeAtLocation(ctx, (GetTypeAtLocationParams)parsed!);
            case Method.GetTypeAtLocations:
                return HandleGetTypeAtLocations(ctx, (GetTypeAtLocationsParams)parsed!);
            case Method.GetTypeAtPosition:
                return HandleGetTypeAtPosition(ctx, (GetTypeAtPositionParams)parsed!);
            case Method.GetTypesAtPositions:
                return HandleGetTypesAtPositions(ctx, (GetTypesAtPositionsParams)parsed!);
            case Method.GetParentOfSymbol:
                return HandleGetParentOfSymbol(ctx, (GetSymbolPropertyParams)parsed!);
            case Method.GetMembersOfSymbol:
                return HandleGetMembersOfSymbol(ctx, (GetSymbolPropertyParams)parsed!);
            case Method.GetExportsOfSymbol:
                return HandleGetExportsOfSymbol(ctx, (GetSymbolPropertyParams)parsed!);
            case Method.GetExportSymbolOfSymbol:
                return HandleGetExportSymbolOfSymbol(ctx, (GetSymbolPropertyParams)parsed!);
            case Method.GetSymbolOfType:
                return HandleGetSymbolOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetTargetOfType:
                return HandleGetTargetOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetFreshTypeOfType:
                return HandleGetFreshTypeOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetRegularTypeOfType:
                return HandleGetRegularTypeOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetTypesOfType:
                return HandleGetTypesOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetTypeParametersOfType:
                return HandleGetTypeParametersOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetOuterTypeParametersOfType:
                return HandleGetOuterTypeParametersOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetLocalTypeParametersOfType:
                return HandleGetLocalTypeParametersOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetThisTypeOfType:
                return HandleGetThisTypeOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetAliasTypeArgumentsOfType:
                return HandleGetAliasTypeArgumentsOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetAliasSymbolOfType:
                return HandleGetAliasSymbolOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetObjectTypeOfType:
                return HandleGetObjectTypeOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetIndexTypeOfType:
                return HandleGetIndexTypeOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetCheckTypeOfType:
                return HandleGetCheckTypeOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetExtendsTypeOfType:
                return HandleGetExtendsTypeOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetBaseTypeOfType:
                return HandleGetBaseTypeOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetConstraintOfType:
                return HandleGetConstraintOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetTypeParameterOfMappedType:
                return HandleGetTypeParameterOfMappedType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetConstraintTypeOfMappedType:
                return HandleGetConstraintTypeOfMappedType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetNameTypeOfMappedType:
                return HandleGetNameTypeOfMappedType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetTemplateTypeOfMappedType:
                return HandleGetTemplateTypeOfMappedType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetTrueTypeOfConditionalType:
                return HandleGetTrueTypeOfConditionalType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetFalseTypeOfConditionalType:
                return HandleGetFalseTypeOfConditionalType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetTypeParametersOfSignature:
                return HandleGetTypeParametersOfSignature(ctx, (GetSignaturePropertyParams)parsed!);
            case Method.GetParametersOfSignature:
                return HandleGetParametersOfSignature(ctx, (GetSignaturePropertyParams)parsed!);
            case Method.GetThisParameterOfSignature:
                return HandleGetThisParameterOfSignature(ctx, (GetSignaturePropertyParams)parsed!);
            case Method.GetTargetOfSignature:
                return HandleGetTargetOfSignature(ctx, (GetSignaturePropertyParams)parsed!);
            case Method.GetContextualType:
                return HandleGetContextualType(ctx, (GetContextualTypeParams)parsed!);
            case Method.GetContextualTypeForArgument:
                return HandleGetContextualTypeForArgument(ctx, (GetContextualTypeForArgumentParams)parsed!);
            case Method.GetAwaitedType:
                return HandleGetAwaitedType(ctx, (CheckerTypeParams)parsed!);
            case Method.GetBaseTypeOfLiteralType:
                return HandleGetBaseTypeOfLiteralType(ctx, (GetBaseTypeOfLiteralTypeParams)parsed!);
            case Method.GetNonNullableType:
                return HandleGetNonNullableType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetTypeFromTypeNode:
                return HandleGetTypeFromTypeNode(ctx, (GetTypeFromTypeNodeParams)parsed!);
            case Method.GetWidenedType:
                return HandleGetWidenedType(ctx, (GetWidenedTypeParams)parsed!);
            case Method.GetParameterType:
                return HandleGetParameterType(ctx, (GetParameterTypeParams)parsed!);
            case Method.GetTypeParameterAtPosition:
                return HandleGetTypeParameterAtPosition(ctx, (GetParameterTypeParams)parsed!);
            case Method.IsArrayLikeType:
                return HandleIsArrayLikeType(ctx, (IsArrayLikeTypeParams)parsed!);
            case Method.IsTypeAssignableTo:
                return HandleIsTypeAssignableTo(ctx, (IsTypeAssignableToParams)parsed!);
            case Method.GetShorthandAssignmentValueSymbol:
                return HandleGetShorthandAssignmentValueSymbol(ctx, (GetTypeAtLocationParams)parsed!);
            case Method.GetTypeOfSymbolAtLocation:
                return HandleGetTypeOfSymbolAtLocation(ctx, (GetTypeOfSymbolAtLocationParams)parsed!);
            case Method.TypeToTypeNode:
                return HandleTypeToTypeNode(ctx, (TypeToTypeNodeParams)parsed!);
            case Method.SignatureToSignatureDeclaration:
                return HandleSignatureToSignatureDeclaration(ctx, (SignatureToSignatureDeclarationParams)parsed!);
            case Method.TypeToString:
                return HandleTypeToString(ctx, (TypeToTypeNodeParams)parsed!);
            case Method.PrintNode:
                return HandlePrintNode(ctx, (PrintNodeParams)parsed!);
            case Method.FormatNodeForInsertion:
                return HandleFormatNodeForInsertion(ctx, (FormatNodeForInsertionParams)parsed!);
            case Method.Emit:
                return HandleEmit(ctx, (EmitParams)parsed!);
            case Method.EmitToString:
                return HandleEmitToString(ctx, (EmitParams)parsed!);
            case Method.GetJavaScriptEmit:
                return HandleSelectedFilesEmit(ctx, (SelectedFilesEmitParams)parsed!, TypeScript.Compiler.EmitOnly.EmitOnlyJs);
            case Method.GetDeclarationEmit:
                return HandleSelectedFilesEmit(ctx, (SelectedFilesEmitParams)parsed!, TypeScript.Compiler.EmitOnly.EmitOnlyDts);
            case Method.IsContextSensitive:
                return HandleIsContextSensitive(ctx, (GetContextualTypeParams)parsed!);
            case Method.GetReturnTypeOfSignature:
                return HandleGetReturnTypeOfSignature(ctx, (GetSignaturePropertyParams)parsed!);
            case Method.GetRestTypeOfSignature:
                return HandleGetRestTypeOfSignature(ctx, (CheckerSignatureParams)parsed!);
            case Method.GetTypePredicateOfSignature:
                return HandleGetTypePredicateOfSignature(ctx, (CheckerSignatureParams)parsed!);
            case Method.GetBaseTypes:
                return HandleGetBaseTypes(ctx, (CheckerTypeParams)parsed!);
            case Method.GetPropertiesOfType:
                return HandleGetPropertiesOfType(ctx, (CheckerTypeParams)parsed!);
            case Method.GetApparentPropertiesOfType:
                return HandleGetApparentPropertiesOfType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetApparentType:
                return HandleGetApparentType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetReducedType:
                return HandleGetReducedType(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetPropertyOfType:
                return HandleGetPropertyOfType(ctx, (GetPropertyOfTypeParams)parsed!);
            case Method.GetTypeOfPropertyOfType:
                return HandleGetTypeOfPropertyOfType(ctx, (GetPropertyOfTypeParams)parsed!);
            case Method.GetIndexInfoOfType:
                return HandleGetIndexInfoOfType(ctx, (GetIndexInfoOfTypeParams)parsed!);
            case Method.GetIndexInfosOfType:
                return HandleGetIndexInfosOfType(ctx, (CheckerTypeParams)parsed!);
            case Method.GetConstraintOfTypeParameter:
                return HandleGetConstraintOfTypeParameter(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetBaseConstraintOfType:
                return HandleGetBaseConstraintOfType(ctx, (CheckerTypeParams)parsed!);
            case Method.GetDefaultFromTypeParameter:
                return HandleGetDefaultFromTypeParameter(ctx, (GetTypePropertyParams)parsed!);
            case Method.GetTypeArguments:
                return HandleGetTypeArguments(ctx, (CheckerTypeParams)parsed!);
            case Method.GetImportAdderEdits:
                return HandleGetImportAdderEdits(ctx, (GetImportAdderEditsParams)parsed!);
            case Method.GetConstantValue:
                return HandleGetConstantValue(ctx, (CheckerNodeParams)parsed!);
            case Method.GetSignatureFromDeclaration:
                return HandleGetSignatureFromDeclaration(ctx, (CheckerNodeParams)parsed!);
            case Method.GetExportSpecifierLocalTarget:
                return HandleGetExportSpecifierLocalTargetSymbol(ctx, (CheckerNodeParams)parsed!);
            case Method.GetAliasedSymbol:
                return HandleGetAliasedSymbol(ctx, (CheckerSymbolParams)parsed!);
            case Method.GetImmediateAliasedSymbol:
                return HandleGetImmediateAliasedSymbol(ctx, (CheckerSymbolParams)parsed!);
            case Method.GetTargetSymbol:
                return HandleMethodGetTargetSymbol(ctx, (CheckerSymbolParams)parsed!);
            case Method.GetExportSymbolOfSymbolForChecker:
                return HandleGetExportSymbolOfSymbolForChecker(ctx, (CheckerSymbolParams)parsed!);
            case Method.GetFullyQualifiedName:
                return HandleGetFullyQualifiedName(ctx, (CheckerSymbolParams)parsed!);
            case Method.GetExportsOfModule:
                return HandleGetExportsOfModule(ctx, (CheckerSymbolParams)parsed!);
            case Method.GetMemberInModuleExports:
                return HandleGetMemberInModuleExports(ctx, (GetMemberInModuleExportsParams)parsed!);
            case Method.GetJSDocTags:
                return HandleGetJSDocTags(ctx, (CheckerSymbolParams)parsed!);
            case Method.GetDocumentationComment:
                return HandleGetDocumentationComment(ctx, (CheckerSymbolParams)parsed!);
            case Method.IsArrayType:
                return HandleIsArrayType(ctx, (CheckerTypeParams)parsed!);
            case Method.IsReadonlySymbol:
                return HandleIsReadonlySymbol(ctx, (CheckerSymbolParams)parsed!);
            case Method.GetAnyType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetAnyType());
            case Method.GetStringType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetStringType());
            case Method.GetNumberType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetNumberType());
            case Method.GetBooleanType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetBooleanType());
            case Method.GetVoidType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetVoidType());
            case Method.GetUndefinedType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetUndefinedType());
            case Method.GetNullType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetNullType());
            case Method.GetNeverType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetNeverType());
            case Method.GetUnknownType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetUnknownType());
            case Method.GetBigIntType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetBigIntType());
            case Method.GetESSymbolType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetESSymbolType());
            case Method.GetNonPrimitiveType:
                return HandleGetIntrinsicType(ctx, (GetIntrinsicTypeParams)parsed!, static c => c.GetNonPrimitiveType());
            case Method.GetWellKnownSymbols:
                return HandleGetWellKnownSymbols(ctx, (GetIntrinsicTypeParams)parsed!);
            case Method.GetWellKnownSignatures:
                return HandleGetWellKnownSignatures(ctx, (GetIntrinsicTypeParams)parsed!);
            case Method.GetSyntacticDiagnostics:
                return HandleGetSyntacticDiagnostics(ctx, (GetDiagnosticsParams)parsed!);
            case Method.GetBindDiagnostics:
                return HandleGetBindDiagnostics(ctx, (GetDiagnosticsParams)parsed!);
            case Method.GetSemanticDiagnostics:
                return HandleGetSemanticDiagnostics(ctx, (GetDiagnosticsParams)parsed!);
            case Method.GetSuggestionDiagnostics:
                return HandleGetSuggestionDiagnostics(ctx, (GetDiagnosticsParams)parsed!);
            case Method.GetDeclarationDiagnostics:
                return HandleGetDeclarationDiagnostics(ctx, (GetDiagnosticsParams)parsed!);
            case Method.GetProgramDiagnostics:
                return HandleGetProgramDiagnostics(ctx, (GetProjectDiagnosticsParams)parsed!);
            case Method.GetGlobalDiagnostics:
                return HandleGetGlobalDiagnostics(ctx, (GetProjectDiagnosticsParams)parsed!);
            case Method.GetConfigFileParsingDiagnostics:
                return HandleGetConfigFileParsingDiagnostics(ctx, (GetProjectDiagnosticsParams)parsed!);
            case Method.StartCPUProfile:
                return HandleStartCPUProfile(ctx, (ProfileParams)parsed!);
            case Method.StopCPUProfile:
                return HandleStopCPUProfile(ctx);
            case Method.SaveHeapProfile:
                return HandleSaveHeapProfile(ctx, (ProfileParams)parsed!);
            case Method.GetReferencesToSymbolInFile:
                return HandleGetReferencesToSymbolInFile(ctx, (GetReferencesToSymbolInFileParams)parsed!);
            case Method.GetReferencedSymbolsForNode:
                return HandleGetReferencedSymbolsForNode(ctx, (GetReferencedSymbolsForNodeParams)parsed!);
            case Method.GetSignatureUsages:
                return HandleGetSignatureUsages(ctx, (GetSignatureUsagesParams)parsed!);
            case Method.GetCompletionsAtPosition:
                return HandleGetCompletionsAtPosition(ctx, (GetCompletionsAtPositionParams)parsed!);
            default:
                throw new InvalidRequestException($"unknown method: {method}");
        }
    }

    /// <summary>Go: <c>func (s *Session) handleBatchRequests(ctx context.Context, params *BatchRequestsParams) (*BatchRequestsResponse, error)</c>
    /// （session.go:1061-1084）。</summary>
    /// <remarks>测试可访问性：Go 里 `handleBatchRequests` 是包内（internal/api）可见，C# 用 `internal` +
    /// `InternalsVisibleTo("TypeScript.Compiler.Tests")` 表达同一边界（契约 §4）。</remarks>
    internal BatchRequestsResponse HandleBatchRequests(Context ctx, BatchRequestsParams @params)
    {
        if (@params.ContinuationToken != "")
        {
            if (!_batchResponsePages.TryRemove(@params.ContinuationToken, out var stored))
            {
                throw new ClientErrorException("api: client error: invalid batch continuation token");
            }
            return PaginateBatchResponses(stored, null, @params.MaxResponseBytesPerPage);
        }

        var responses = new BatchResponse[@params.Requests.Length];
        for (var i = 0; i < @params.Requests.Length; i++)
        {
            responses[i] = HandleBatchRequest(ctx, @params.Requests[i]);
        }
        var page = NewBatchResponsePage(responses);
        return PaginateBatchResponses(page, responses, @params.MaxResponseBytesPerPage);
    }

    /// <summary>Go: <c>func newBatchResponsePage(responses []BatchResponse) (batchResponsePage, error)</c>（session.go:1085-1096）。</summary>
    internal static BatchResponsePage NewBatchResponsePage(BatchResponse[] responses)
    {
        var encodedResponses = new byte[responses.Length][];
        for (var i = 0; i < responses.Length; i++)
        {
            encodedResponses[i] = TypeScript.Json.Json.Marshal(responses[i]);
        }
        return new BatchResponsePage { EncodedResponses = encodedResponses };
    }

    /// <summary>Go: <c>func (s *Session) paginateBatchResponses(page batchResponsePage, responses []BatchResponse, maxResponseBytesPerPage int) (*BatchRequestsResponse, error)</c>
    /// （session.go:1097-1141）。</summary>
    internal BatchRequestsResponse PaginateBatchResponses(BatchResponsePage page, BatchResponse[]? responses, int maxResponseBytesPerPage)
    {
        if (maxResponseBytesPerPage <= 0)
        {
            maxResponseBytesPerPage = DefaultMaxResponseBytesPerPage;
        }
        var encodedLength = "{\"responses\":[]}".Length;
        var pageLength = 0;
        foreach (var encoded in page.EncodedResponses)
        {
            var additionalLength = encoded.Length;
            if (pageLength > 0)
            {
                additionalLength++;
            }
            if (pageLength > 0 && encodedLength + additionalLength > maxResponseBytesPerPage)
            {
                break;
            }
            encodedLength += additionalLength;
            pageLength++;
        }

        if (pageLength == page.EncodedResponses.Length)
        {
            return new BatchRequestsResponse
            {
                Responses = responses ?? [],
                EncodedResponses = page.EncodedResponses,
            };
        }
        var continuationToken = $"{_id}-{Interlocked.Increment(ref _nextBatchResponsePageId)}";
        var continuationLength = ",\"continuationToken\":\"\"".Length + continuationToken.Length;
        while (pageLength > 1 && encodedLength + continuationLength > maxResponseBytesPerPage)
        {
            encodedLength -= page.EncodedResponses[pageLength - 1].Length + 1;
            pageLength--;
        }
        var currentResponses = page.EncodedResponses[..pageLength];
        var remainingResponses = page.EncodedResponses[pageLength..];
        var response = new BatchRequestsResponse
        {
            ContinuationToken = continuationToken,
            EncodedResponses = currentResponses,
        };
        if (responses is not null)
        {
            response.Responses = responses[..pageLength];
        }
        _batchResponsePages[continuationToken] = new BatchResponsePage { EncodedResponses = remainingResponses };
        return response;
    }

    /// <summary>Go: <c>func (s *Session) handleBatchRequest(ctx context.Context, request BatchRequest) (response BatchResponse)</c>
    /// （session.go:1142-1168）。</summary>
    internal BatchResponse HandleBatchRequest(Context ctx, BatchRequest request)
    {
        var response = new BatchResponse { Method = request.Method };
        if (request.Method == Method.BatchRequests)
        {
            response.Error = "api: invalid request: batchRequests cannot be nested";
            return response;
        }
        // Go 的 defer/recover 覆盖整个函数体（含后续的 RawBinary 处理）→ try 包住同样范围。
        try
        {
            response.Result = HandleRequest(ctx, request.Method, request.Params);
            if (response.Result is RawBinary data && IsSourceFileResponseMethod(request.Method))
            {
                response.Result = data.Data.Length == 0
                    ? null
                    : new SourceFileResponse { Data = Convert.ToBase64String(data.Data) };
            }
            return response;
        }
        catch (DebugFailureException panic)
        {
            response.Result = null;
            response.Error = $"panic: {panic.Message}\n{panic}";
            return response;
        }
        catch (Exception err)
        {
            response.Error = err.Message;
            return response;
        }
    }

    /// <summary>Go: <c>func isSourceFileResponseMethod(method Method) bool</c>（session.go:1169-1182）。</summary>
    internal static bool IsSourceFileResponseMethod(string method)
    {
        switch (method)
        {
            case Method.CreateSourceFile:
            case Method.CreateSourceFileFromFile:
            case Method.GetSourceFile:
            case Method.GetConfigSourceFile:
            case Method.TypeToTypeNode:
            case Method.SignatureToSignatureDeclaration:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func (s *Session) handleStartCPUProfile(_ context.Context, params *ProfileParams) (any, error)</c>
    /// （session.go:1183-1191）。</summary>
    internal object? HandleStartCPUProfile(Context ctx, ProfileParams? @params)
    {
        if (@params is null || @params.Dir == "")
        {
            throw new ClientErrorException("api: client error: dir is required");
        }
        var profiler = _cpuProfiler ??= new Execute.CPUProfiler();
        if (!profiler.TryStartCPUProfile(@params.Dir))
        {
            throw new ClientErrorException("api: client error: failed to start CPU profile: CPU profiling already in progress");
        }
        return null;
    }

    /// <summary>Go: <c>func (s *Session) handleStopCPUProfile(_ context.Context) (*ProfileResult, error)</c>（session.go:1193-1199）。</summary>
    internal ProfileResult HandleStopCPUProfile(Context ctx)
    {
        var profiler = _cpuProfiler ??= new Execute.CPUProfiler();
        if (!profiler.TryStopCPUProfile(out var filePath))
        {
            throw new ClientErrorException("api: client error: failed to stop CPU profile: CPU profiling not in progress");
        }
        return new ProfileResult { File = filePath };
    }

    /// <summary>Go: <c>func (s *Session) handleSaveHeapProfile(_ context.Context, params *ProfileParams) (*ProfileResult, error)</c>
    /// （session.go:1201-1211）。</summary>
    internal ProfileResult HandleSaveHeapProfile(Context ctx, ProfileParams? @params)
    {
        if (@params is null || @params.Dir == "")
        {
            throw new ClientErrorException("api: client error: dir is required");
        }
        if (!Execute.Pprof.TrySaveHeapProfile(@params.Dir, out var filePath))
        {
            throw new ClientErrorException("api: client error: failed to save heap profile: heap profiling is not supported");
        }
        return new ProfileResult { File = filePath };
    }

    /// <summary>Go: <c>func (s *Session) HandleNotification(ctx context.Context, method string, params json.Value) error</c>
    /// （session.go:1213-1216）。Go 侧是空实现（TODO: Implement notification handling）。</summary>
    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
        // TODO: Implement notification handling
    }

    /// <summary>Go: <c>func (s *Session) handleInitialize(ctx context.Context) (*InitializeResponse, error)</c>（session.go:1218-1225）。</summary>
    internal InitializeResponse HandleInitialize(Context ctx)
        => new()
        {
            UseCaseSensitiveFileNames = UseCaseSensitiveFileNames(),
            CurrentDirectory = GetCurrentDirectory(),
        };
}

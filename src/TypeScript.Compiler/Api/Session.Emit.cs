// ported from: internal/api/session.go:3564-4522
//   （handleEmit/handleEmitToString/handleSelectedFilesEmit + emitToOutput/getEmitOptions/getEmitProgram/
//     getEmitOnly/emitProgram/nonNilDiagnostics + handleFormatNodeForInsertion + 内建类型/内建符号/内建签名 +
//     类型/符号/签名/模块查询 handler 组）
//   注：原分片边界写的是 3564-4558，但 `snapshotData.resolveNodeHandle`（session.go:4523-4558）已由
//   `Api/Session.Snapshot.cs` 落地（同一个 `internal sealed partial class SnapshotData` 分片），
//   两边都声明会撞成员重复，故本文件不再重复实现，末尾停在 session.go:4522。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `defer setup.done()` → try/finally：C# 没有 defer。本文件每个用到 CheckerSetup 的 handler 都写成
//    `try { … } finally { setup.Done(); }`，与 Go 的 defer 时机完全一致（包括提前 return 的路径）。
//
// 2) Go 的 `(T, error)` → C# 正常路径返回 T、错误路径抛异常（契约 §1.2）。因此：
//    - `setupChecker` 失败（Go 的 `return checkerSetup{}, err`）在 C# 里由 SetupChecker 抛异常表达，
//      handler 里没有 `if err != nil { return nil, err }` 分支；
//    - `getEmitOptions` 的 `(program, options, error)` → 元组 `(Program, Options)`（错误抛异常）；
//    - `getEmitOnly` 的 `(EmitOnly, error)` → 返回 EmitOnly（非法值抛 ClientErrorException）；
//    - `resolveIndexInfoRequest` 的 `(checkerSetup, *Type, *Type, error)` → 元组
//      `(CheckerSetup, Checker.Type, Checker.Type)`；出错时它仍须像 Go 一样 `setup.done()`
//      （Go 在错误分支显式调用），C# 用 catch → Done() → rethrow 表达。
//
// 3) 句柄解析的 nil 语义：`resolveTypeHandle`/`resolveSymbolHandle`/`resolveSignatureHandle`/`resolveNodeHandle`
//    在 C# 里**失败即抛** ClientErrorException，绝不会返回 nil。因此 Go 里 `if symbol == nil { return nil, nil }`
//    这类"句柄未注册/为空"分支在 C# 不可达，已删除。
//    与之相对，**checker 查询返回 nil 是正常的"没有这个值"语义**（Go 同样用 nil 表示，最终 JSON 是 null）：
//    `GetConstraintOfTypeParameter`/`GetDefaultFromTypeParameter`/`GetBaseConstraintOfType`/`GetPropertyOfType`/
//    `GetTypeOfPropertyOfType`/`GetImmediateAliasedSymbol`/`GetTargetSymbol`/`TryGetMemberInModuleExports`/
//    `GetTrue|FalseTypeOfConditionalType`/`GetTypePredicateOfSignature` 的 nil 检查一律保留并返回 null。
//
// 4) Go 的 `newSymbolResponse`/`newSignatureResponse`/`newTypeResponse`/`newIndexInfoResponse` 对 nil 入参返回 nil；
//    本仓落地的 `SnapshotData.New*Response` 与 `CheckerSetup.New*Response` 同样是「可空入参 → 可空返回」
//    （`SymbolResponse?`/`TypeResponse?`/`SignatureResponse?`/`IndexInfoResponse?`）。因此本文件里：
//    - **输入可能为 nil** 的位置（`GetSignatureFromDeclaration`、`GetIndexInfoOfType`、`GetTargetSymbol`、
//      `GetTypeOfPropertyOfType`、`GetTrue|FalseTypeOfConditionalType`、`GetPropertyOfType` 等）先判 null 再返回 null，
//      与 Go 的最终 JSON（null）逐点一致；
//    - **输入由 checker 保证非空** 的位置（`GetReturnTypeOfSignature`/`GetRestTypeOfSignature`/`GetApparentType`/
//      `GetReducedType`/`GetAliasedSymbol`/`GetExportSymbolOfSymbol`，以及 `GetBaseTypes`/`GetPropertiesOfType`/
//      `GetApparentProperties`/`GetTypeArguments`/`GetExportsOfModule` 的数组元素）用 `!` 收窄成非可空，
//      以保持 Go 那边"返回值/数组元素必非 nil"的类型语义（运行期与 Go 完全一致，`!` 只消除静态可空告警）。
//
// 5) `emitProgram`：Go 用 `ctx.Err()` 判断取消、用 `errors.New(...)` 报"编译器返回 nil 结果"。
//    C# 没有 error 值 → `ctx.CancellationToken.IsCancellationRequested` 为真时抛 `OperationCanceledException`，
//    否则抛 `InvalidOperationException("compiler emit returned nil result")`（消息逐字保留）。
//
// 6) `handleEmit`/`emitToOutput` 的 WriteFile 闭包：Go 用 `sync.Mutex` 保护切片/map，因为 `Program.Emit`
//    在工作组里并发射射（C# 侧见 `Compiler/Program.Emit.cs` 的 `wg.Queue`）。C# 落成局部 `System.Threading.Lock`
//    + `lock` 语句（契约 §2 的 sync.Mutex 映射；先例 `Ls/CrossProject.cs`）。
//
// 7) `slices.Clone(result.EmittedFiles)` + `if emittedFiles == nil { … = []string{} }` → C# 的
//    `EmitResult.EmittedFiles` 声明为 `= []`（恒非 null，见 Compiler/Program.Emit.cs），故仅保留
//    `(string[])…Clone()`；Go 的 nil 归一化分支在 C# 不可达（语义等价）。
//
// 8) Go map 读缺失键得到零值 `""` → C# `Dictionary.TryGetValue` 失败时显式取 `""`。
//
// 9) `strings.Compare`（Go 按 UTF-8 字节序）→ `string.CompareOrdinal`（按 UTF-16 码元序）。对 ASCII 路径
//    两者一致；含非 ASCII 时仅排序次序可能不同、集合内容不变 —— C# 侧无法零成本复刻 UTF-8 字节序比较。
//
// 10) `getEmitOnly` 里 `uint32(compiler.EmitOnlyDts)` → `(uint)EmitOnly.EmitOnlyDts`（C# 的 EmitOnly 底层是 byte）。
//
// 11) `format.*`/`printer.*`/`ast.*`/`core.*`/`ls.*` 这些 Go 包在 C# 里都落成"与命名空间同名"的静态类，
//     在 namespace 体内写 `Format.Xxx` 会被解析成命名空间（找不到成员），所以本文件用 using 别名
//     （AstApi/CoreApi/FormatApi/PrinterApi）调用，`Encoder`/`RequestFileSystem` 用 using static 调用。
//
// 12) `handleFormatNodeForInsertion` 的逐点差异：
//     - `base64.StdEncoding.DecodeString` → `Convert.FromBase64String` + try/catch（Go 的 error → C# 异常，
//       `%w` 链 → ClientErrorException 的 message + InnerException）；
//     - `encoder.DecodeNodes`（Go 返回 error）→ `Decoder.DecodeNodes`（decoder.go 的包级函数落在静态类
//       `TypeScript.Api.Encoder.Decoder` 上；encoder.go 的 `GetNodeIndexTable` 则在 `TypeScript.Api.Encoder.Encoder`
//       上，两者都用 using static 引入），失败抛异常，本文件同样包成 ClientErrorException；
//     - `printer.PrintAndPositionNode(factory, node, nil, newLine, indentSize, nil)` 的两个 nil：第 3 个参数
//     （sourceFile）C# 声明为可空，照传 `null`；第 6 个参数（emitContext）C# 声明非可空，用 `null!` 传 Go 的 nil；
//     - `format.ShouldIndentChildNode(opts, node, nil, nil)` 的第 4 个参数（sourceFile）Go 传 nil、C# 签名非可空
//       → `null!`；
//     - `ctx = format.WithFormatCodeSettings(ctx, opts, newLine)`：C# 的 `Context` 是不可变类，
//       `WithFormatCodeSettings` 派生新实例（等价 Go 的 context.WithValue）→ 重新赋值给 `ctx` 局部变量；
//     - `ast.NewNodeFactory(ast.NodeFactoryHooks{})` → `AstApi.NewNodeFactory(new Ast.NodeFactoryHooks())`。
//
// 13) `handleSelectedFilesEmit`：Go 的 `params.Files == nil` → C# `@params.Files is null`（proto 的
//     `[]DocumentIdentifier` 落成 `DocumentIdentifier[]?`）。`resolveOptionalSourceFile` 在 C# 里返回
//     `Ast.SourceFile?`，但它为 null 的前提（Go 的 `file == nil`）在 C# 不存在（参数非可空），"文件不在 program 里"
//     则抛异常 —— 所以数组元素用 `!`（Go 侧 `&file` 恒非 nil，实际也不会 append nil），语义等价。
//
// 14) `nonNilDiagnostics`：`Api.NewDiagnosticResponses` 返回可空数组（空输入 → null，同 Go）→ `?? []` 归一。
//
// 15) `handleGetConstantValue`：`value.(jsnum.Number)` 类型断言 → `value is JsNum.Number`（装箱判定，
//     结果同样是 bool）；`literalValueToJSON` → `Api.LiteralValueToJSON`（proto.go 的包级函数，见契约 §4）。
//
// 16) `handleGetJSDocTags`：`ls.GetSymbolJSDocTags` → `Ls.Ls.GetSymbolJSDocTags`（C# 的 ls 包函数在静态类
//     `TypeScript.Ls.Ls` 上），它返回 `TypeScript.Ls.JSDocTagInfo[]`（无标签时是空数组而非 nil，Go 是 nil 切片），
//     再逐项转成本 API 自己的 `JSDocTagInfo`（proto.go 的消息类型）——所以 C# 侧仍用 `Length == 0` 复刻 Go 的 nil 返回。
//
// 17) `handleGetIntrinsicType` 的 `getter func(*checker.Checker) *checker.Type` → C# 委托参数
//     `Func<Checker.Checker, Checker.Type?>`（具体 lambda 由 Session.Requests.cs 按方法映射传入）。
//
// 18) `handleSelectedFilesEmit` 的第三个参数 `emitOnly compiler.EmitOnly` 保留为显式参数
//     （由 Session.Requests.cs 传 `EmitOnly.EmitOnlyJs`/`EmitOnly.EmitOnlyDts`）。
//
// 19) 方法可见性：Go 的未导出方法/函数的唯一调用方是同一 partial 类（Session.Requests.cs 等）与本包其它分片，
//     故一律 C# `internal`（契约 §4 的 `handleXxx → HandleXxx` 命名保留）。
//
// 20) 包级函数 `emitToOutput`/`emitProgram`/`getEmitOnly`/`nonNilDiagnostics` 落在 Session 的 partial 分片上
//     作为 `internal static`（契约允许"与 Go 文件同名的静态类或 Api 静态类"；session.go 的主类型就是 Session，
//     故不新造类型名）。
//
// 21) 本文件不包含 `snapshotData.resolveNodeHandle`（session.go:4523-4558）：它由 `Api/Session.Snapshot.cs`
//     的 `SnapshotData` 分片实现（`internal sealed partial class SnapshotData`），本文件只通过
//     `setup.Sd.ResolveNodeHandle(program, handle)` 调用（失败抛 ClientErrorException）。这里照旧登记该冲突的
//     处理结果，避免集成时出现重复成员。
//
// 22) 排序：`slices.SortFunc` → `List.Sort`/`Array.Sort`（两者都是不稳定排序，与 Go 一致）。

using TypeScript.Core;
using static TypeScript.Api.Encoder.Decoder;
using static TypeScript.Api.RequestFileSystem.RequestFileSystem;
using AstApi = TypeScript.Ast.Ast;
using CoreApi = TypeScript.Core.Core;
using FormatApi = TypeScript.Format.Format;
using PrinterApi = TypeScript.Printer.Printer;

namespace TypeScript.Api;

public sealed partial class Session
{
    /// <summary>Go: <c>func (s *Session) handleEmit(ctx context.Context, params *EmitParams) (*EmitResponse, error)</c>（session.go:3564-3610）。</summary>
    internal EmitResponse HandleEmit(Context ctx, EmitParams @params)
    {
        var (program, options) = GetEmitOptions(@params);
        Dictionary<string, string>? outputFiles = null;
        var sd = GetSnapshotData(@params.Snapshot);
        if (HasFullFileSystem(sd.FileSystem))
        {
            // Go 用同一个 outputFiles 变量；C# 里为了不让闭包捕获可空局部（可空性流分析），
            // 先捕获非空局部再赋给外层变量（见文件头偏离 6）。
            var captured = new Dictionary<string, string>();
            var outputMu = new Lock();
            options.WriteFile = (fileName, text, _) =>
            {
                lock (outputMu)
                {
                    captured[fileName] = text;
                }
            };
            outputFiles = captured;
        }
        else
        {
            options.WriteFile = (fileName, text, _) => _snapshotHost.FS().WriteFile(fileName, text);
        }

        var result = EmitProgram(ctx, program, options);
        var emittedFiles = (string[])result.EmittedFiles.Clone();
        var emittedFilesContents = Array.Empty<string>();
        if (outputFiles is not null)
        {
            emittedFilesContents = new string[emittedFiles.Length];
            for (var i = 0; i < emittedFiles.Length; i++)
            {
                emittedFilesContents[i] = outputFiles.TryGetValue(emittedFiles[i], out var content) ? content : "";
            }
        }

        return new EmitResponse
        {
            EmitSkipped = result.EmitSkipped,
            Diagnostics = NonNilDiagnostics(result.Diagnostics),
            EmittedFiles = emittedFiles,
            EmittedFilesContents = emittedFilesContents,
        };
    }

    /// <summary>Go: <c>func (s *Session) handleEmitToString(ctx context.Context, params *EmitParams) (*EmitOutputResponse, error)</c>（session.go:3611-3618）。</summary>
    internal EmitOutputResponse HandleEmitToString(Context ctx, EmitParams @params)
    {
        var (program, options) = GetEmitOptions(@params);
        return EmitToOutput(ctx, program, options);
    }

    /// <summary>Go: <c>func (s *Session) handleSelectedFilesEmit(ctx context.Context, params *SelectedFilesEmitParams, emitOnly compiler.EmitOnly) (*EmitOutputResponse, error)</c>（session.go:3619-3641）。</summary>
    internal EmitOutputResponse HandleSelectedFilesEmit(Context ctx, SelectedFilesEmitParams @params, Compiler.EmitOnly emitOnly)
    {
        var program = GetEmitProgram(@params.Snapshot, @params.Project);
        var files = @params.Files;
        if (files is null)
        {
            throw new ClientErrorException("api: client error: files is required");
        }

        var targetSourceFiles = new Ast.SourceFile[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            // Go 的 resolveOptionalSourceFile 只在 file == nil 时返回 nil；这里参数非可空，故不可能为 nil。
            targetSourceFiles[i] = ResolveOptionalSourceFile(program, files[i])!;
        }

        return EmitToOutput(ctx, program, new Compiler.EmitOptions
        {
            TargetSourceFiles = targetSourceFiles,
            EmitOnly = emitOnly,
            ForceEmit = true,
        });
    }

    /// <summary>Go: <c>func emitToOutput(ctx context.Context, program *compiler.Program, options compiler.EmitOptions) (*EmitOutputResponse, error)</c>（session.go:3642-3670）。</summary>
    internal static EmitOutputResponse EmitToOutput(Context ctx, Compiler.Program program, Compiler.EmitOptions options)
    {
        var outputFiles = new List<EmitOutputFile>();
        var mu = new Lock();
        options.WriteFile = (fileName, text, data) =>
        {
            string? sourceFileName = null;
            if (data.SourceFile is not null)
            {
                sourceFileName = data.SourceFile.FileName();
            }

            lock (mu)
            {
                outputFiles.Add(new EmitOutputFile { FileName = fileName, Text = text, SourceFileName = sourceFileName });
            }
        };

        var result = EmitProgram(ctx, program, options);
        outputFiles.Sort(static (a, b) => string.CompareOrdinal(a.FileName, b.FileName));
        return new EmitOutputResponse
        {
            EmitSkipped = result.EmitSkipped,
            Diagnostics = NonNilDiagnostics(result.Diagnostics),
            OutputFiles = outputFiles.ToArray(),
        };
    }

    /// <summary>Go: <c>func (s *Session) getEmitOptions(params *EmitParams) (*compiler.Program, compiler.EmitOptions, error)</c>（session.go:3671-3684）。</summary>
    internal (Compiler.Program Program, Compiler.EmitOptions Options) GetEmitOptions(EmitParams @params)
    {
        var program = GetEmitProgram(@params.Snapshot, @params.Project);
        var emitOnly = GetEmitOnly(@params.EmitOnly);
        return (program, new Compiler.EmitOptions
        {
            EmitOnly = emitOnly,
        });
    }

    /// <summary>Go: <c>func (s *Session) getEmitProgram(snapshot SnapshotID, projectID project.ID) (*compiler.Program, error)</c>（session.go:3685-3692）。</summary>
    internal Compiler.Program GetEmitProgram(SnapshotID snapshot, Project.ID projectID)
    {
        var sd = GetSnapshotData(snapshot);
        return sd.GetProgram(projectID);
    }

    /// <summary>Go: <c>func getEmitOnly(value *uint32) (compiler.EmitOnly, error)</c>（session.go:3693-3703）。</summary>
    internal static Compiler.EmitOnly GetEmitOnly(uint? value)
    {
        if (value is null)
        {
            return Compiler.EmitOnly.EmitAll;
        }

        if (value > (uint)Compiler.EmitOnly.EmitOnlyDts)
        {
            throw new ClientErrorException($"api: client error: invalid emitOnly value: {value}");
        }

        return (Compiler.EmitOnly)value.Value;
    }

    /// <summary>Go: <c>func emitProgram(ctx context.Context, program *compiler.Program, options compiler.EmitOptions) (*compiler.EmitResult, error)</c>（session.go:3704-3714）。</summary>
    internal static Compiler.EmitResult EmitProgram(Context ctx, Compiler.Program program, Compiler.EmitOptions options)
    {
        var result = program.Emit(ctx, options);
        if (result is not null)
        {
            return result;
        }

        if (ctx.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ctx.CancellationToken);
        }

        throw new InvalidOperationException("compiler emit returned nil result");
    }

    /// <summary>Go: <c>func nonNilDiagnostics(diags []*ast.Diagnostic) []*DiagnosticResponse</c>（session.go:3715-3724）。</summary>
    internal static DiagnosticResponse[] NonNilDiagnostics(Ast.Diagnostic[] diags)
    {
        var result = Api.NewDiagnosticResponses(diags);
        return result ?? [];
    }

    /// <summary>Go: <c>func (s *Session) handleFormatNodeForInsertion(ctx context.Context, params *FormatNodeForInsertionParams) (string, error)</c>（session.go:3725-3773）。</summary>
    internal string HandleFormatNodeForInsertion(Context ctx, FormatNodeForInsertionParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var program = sd.GetProgram(@params.Project);

        var targetSourceFile = ResolveOptionalSourceFile(program, @params.File)!;

        byte[] data;
        try
        {
            data = Convert.FromBase64String(@params.Data);
        }
        catch (FormatException ex)
        {
            throw new ClientErrorException($"api: client error: invalid base64 data: {ex.Message}", ex);
        }

        Ast.Node node;
        try
        {
            node = DecodeNodes(data);
        }
        catch (Exception ex)
        {
            throw new ClientErrorException($"api: client error: failed to decode AST: {ex.Message}", ex);
        }

        var pos = targetSourceFile.GetPositionMap().UTF16ToUTF8((int)@params.Position);
        var formatOptions = sd.Snapshot.UserPreferences().FormatCodeSettings;
        var newLine = formatOptions.NewLineCharacter;

        var factory = AstApi.NewNodeFactory(new Ast.NodeFactoryHooks());
        var (text, nodeWithPos) = PrinterApi.PrintAndPositionNode(factory, node, null, newLine, formatOptions.IndentSize, null!);
        var syntheticFile = PrinterApi.CreateSyntheticSourceFile(factory, nodeWithPos, text, targetSourceFile.ParseOptions());

        var isAtLineStart = FormatApi.GetLineStartPositionForPosition(pos, targetSourceFile) == pos;
        var initialIndentation = FormatApi.GetIndentation(pos, targetSourceFile, formatOptions, isAtLineStart);

        var delta = 0;
        if (formatOptions.IndentSize != 0 && FormatApi.ShouldIndentChildNode(formatOptions, node, null, null!))
        {
            delta = formatOptions.IndentSize;
        }

        ctx = FormatApi.WithFormatCodeSettings(ctx, formatOptions, newLine);
        var changes = FormatApi.FormatNodeGivenIndentation(ctx, nodeWithPos, syntheticFile, targetSourceFile.LanguageVariant, initialIndentation, delta);

        return CoreApi.ApplyBulkEdits(text, changes);
    }

    /// <summary>Go: <c>func (s *Session) handleGetIntrinsicType(ctx context.Context, params *GetIntrinsicTypeParams, getter func(*checker.Checker) *checker.Type) (*TypeResponse, error)</c>（session.go:3774-3790）。</summary>
    internal TypeResponse? HandleGetIntrinsicType(Context ctx, GetIntrinsicTypeParams @params, Func<Checker.Checker, Checker.Type?> getter)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = getter(setup.Checker);
            if (t is null)
            {
                return null;
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, t, setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetWellKnownSymbols(ctx context.Context, params *GetIntrinsicTypeParams) (*WellKnownSymbolsResponse, error)</c>（session.go:3791-3809）。</summary>
    internal WellKnownSymbolsResponse HandleGetWellKnownSymbols(Context ctx, GetIntrinsicTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var (unknown, _) = setup.Sd.RegisterSymbol(setup.Checker.GetUnknownSymbol()!, setup.ProjectID);
            var (undefined, _) = setup.Sd.RegisterSymbol(setup.Checker.GetUndefinedSymbol()!, setup.ProjectID);
            var (arguments, _) = setup.Sd.RegisterSymbol(setup.Checker.GetArgumentsSymbol()!, setup.ProjectID);
            return new WellKnownSymbolsResponse
            {
                Unknown = unknown,
                Undefined = undefined,
                Arguments = arguments,
            };
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetWellKnownSignatures(ctx context.Context, params *GetIntrinsicTypeParams) (*WellKnownSignaturesResponse, error)</c>（session.go:3810-3822）。</summary>
    internal WellKnownSignaturesResponse HandleGetWellKnownSignatures(Context ctx, GetIntrinsicTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            return new WellKnownSignaturesResponse
            {
                Unknown = setup.Sd.RegisterSignature(setup.ProjectID, setup.Checker.GetUnknownSignature()!),
            };
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleIsContextSensitive(ctx context.Context, params *GetContextualTypeParams) (bool, error)</c>（session.go:3823-3841）。</summary>
    internal bool HandleIsContextSensitive(Context ctx, GetContextualTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);

            return setup.Checker.IsContextSensitive(node);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetReturnTypeOfSignature(ctx context.Context, params *GetSignaturePropertyParams) (*TypeResponse, error)</c>（session.go:3842-3857）。</summary>
    internal TypeResponse HandleGetReturnTypeOfSignature(Context ctx, GetSignaturePropertyParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sig = setup.ResolveSignatureHandle(@params.Signature);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetReturnTypeOfSignature(sig), setup.Checker)!;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetRestTypeOfSignature(ctx context.Context, params *CheckerSignatureParams) (*TypeResponse, error)</c>（session.go:3858-3874）。</summary>
    internal TypeResponse HandleGetRestTypeOfSignature(Context ctx, CheckerSignatureParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sig = setup.ResolveSignatureHandle(@params.Signature);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetRestTypeOfSignature(sig), setup.Checker)!;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypePredicateOfSignature(ctx context.Context, params *CheckerSignatureParams) (*TypePredicateResponse, error)</c>（session.go:3875-3904，@gen-proto-nullable）。</summary>
    internal TypePredicateResponse? HandleGetTypePredicateOfSignature(Context ctx, CheckerSignatureParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sig = setup.ResolveSignatureHandle(@params.Signature);

            var pred = setup.Checker.GetTypePredicateOfSignature(sig);
            if (pred is null)
            {
                return null;
            }

            var resp = new TypePredicateResponse
            {
                Kind = (int)pred.Kind(),
                ParameterIndex = pred.ParameterIndex(),
                ParameterName = pred.ParameterName(),
            };
            var predType = pred.Type();
            if (predType is not null)
            {
                resp.Type = setup.Sd.NewTypeResponse(setup.ProjectID, predType, setup.Checker);
            }

            return resp;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleIsArrayType(ctx context.Context, params *CheckerTypeParams) (bool, error)</c>（session.go:3905-3920）。</summary>
    internal bool HandleIsArrayType(Context ctx, CheckerTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            return setup.Checker.IsArrayType(t);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleIsReadonlySymbol(ctx context.Context, params *CheckerSymbolParams) (bool, error)</c>（session.go:3921-3937）。</summary>
    internal bool HandleIsReadonlySymbol(Context ctx, CheckerSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            return setup.Checker.IsReadonlySymbol(symbol);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetBaseTypes(ctx context.Context, params *CheckerTypeParams) ([]*TypeResponse, error)</c>（session.go:3938-3964，@gen-proto-nullable）。</summary>
    internal TypeResponse[]? HandleGetBaseTypes(Context ctx, CheckerTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var baseTypes = setup.Checker.GetBaseTypes(t);
            if (baseTypes.Length == 0)
            {
                return null;
            }

            var results = new TypeResponse[baseTypes.Length];
            for (var i = 0; i < baseTypes.Length; i++)
            {
                results[i] = setup.Sd.NewTypeResponse(setup.ProjectID, baseTypes[i], setup.Checker)!;
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetPropertiesOfType(ctx context.Context, params *CheckerTypeParams) ([]*SymbolResponse, error)</c>（session.go:3965-3991，@gen-proto-nullable）。</summary>
    internal SymbolResponse[]? HandleGetPropertiesOfType(Context ctx, CheckerTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var props = setup.Checker.GetPropertiesOfType(t);
            if (props.Length == 0)
            {
                return null;
            }

            var results = new SymbolResponse[props.Length];
            for (var i = 0; i < props.Length; i++)
            {
                results[i] = setup.NewSymbolResponse(props[i])!;
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetApparentPropertiesOfType(ctx context.Context, params *GetTypePropertyParams) ([]*SymbolResponse, error)</c>（session.go:3992-4012）。</summary>
    internal SymbolResponse[] HandleGetApparentPropertiesOfType(Context ctx, GetTypePropertyParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var props = setup.Checker.GetApparentProperties(t);
            var results = new SymbolResponse[props.Length];
            for (var i = 0; i < props.Length; i++)
            {
                results[i] = setup.NewSymbolResponse(props[i])!;
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetApparentType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:4013-4028）。</summary>
    internal TypeResponse HandleGetApparentType(Context ctx, GetTypePropertyParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetApparentType(t), setup.Checker)!;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetReducedType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:4029-4045）。</summary>
    internal TypeResponse HandleGetReducedType(Context ctx, GetTypePropertyParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetReducedType(t), setup.Checker)!;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetIndexInfosOfType(ctx context.Context, params *CheckerTypeParams) ([]*IndexInfoResponse, error)</c>（session.go:4046-4070，@gen-proto-nullable）。</summary>
    internal IndexInfoResponse[]? HandleGetIndexInfosOfType(Context ctx, CheckerTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var infos = setup.Checker.GetIndexInfosOfType(t);
            if (infos.Length == 0)
            {
                return null;
            }

            var results = new IndexInfoResponse[infos.Length];
            for (var i = 0; i < infos.Length; i++)
            {
                results[i] = setup.NewIndexInfoResponse(infos[i])!;
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) resolveIndexInfoRequest(ctx context.Context, params *GetIndexInfoOfTypeParams) (checkerSetup, *checker.Type, *checker.Type, error)</c>（session.go:4071-4096）。</summary>
    internal (CheckerSetup Setup, Checker.Type Type, Checker.Type KeyType) ResolveIndexInfoRequest(Context ctx, GetIndexInfoOfTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            Checker.Type keyType = (Checker.IndexKind)@params.Kind switch
            {
                Checker.IndexKind.String => setup.Checker.GetStringType(),
                Checker.IndexKind.Number => setup.Checker.GetNumberType(),
                _ => throw new ClientErrorException($"api: client error: invalid index kind {@params.Kind}"),
            };
            return (setup, t, keyType);
        }
        catch
        {
            // 成功路径由调用方 finally 释放；失败路径在 Go 里也是这里显式 setup.done() 后返回错误。
            setup.Done();
            throw;
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetIndexInfoOfType(ctx context.Context, params *GetIndexInfoOfTypeParams) (*IndexInfoResponse, error)</c>（session.go:4097-4107，@gen-proto-nullable）。</summary>
    internal IndexInfoResponse? HandleGetIndexInfoOfType(Context ctx, GetIndexInfoOfTypeParams @params)
    {
        var (setup, t, keyType) = ResolveIndexInfoRequest(ctx, @params);
        try
        {
            var info = setup.Checker.GetIndexInfoOfType(t, keyType);
            if (info is null)
            {
                return null;
            }

            return setup.NewIndexInfoResponse(info);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetConstraintOfTypeParameter(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:4108-4129，@gen-proto-nullable）。</summary>
    internal TypeResponse? HandleGetConstraintOfTypeParameter(Context ctx, GetTypePropertyParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var constraint = setup.Checker.GetConstraintOfTypeParameter(t);
            if (constraint is null)
            {
                return null;
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, constraint, setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetDefaultFromTypeParameter(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:4130-4146，@gen-proto-nullable）。</summary>
    internal TypeResponse? HandleGetDefaultFromTypeParameter(Context ctx, GetTypePropertyParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var def = setup.Checker.GetDefaultFromTypeParameter(t);
            if (def is null)
            {
                return null;
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, def, setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetBaseConstraintOfType(ctx context.Context, params *CheckerTypeParams) (*TypeResponse, error)</c>（session.go:4147-4168，@gen-proto-nullable）。</summary>
    internal TypeResponse? HandleGetBaseConstraintOfType(Context ctx, CheckerTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var constraint = setup.Checker.GetBaseConstraintOfType(t);
            if (constraint is null)
            {
                return null;
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, constraint, setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetPropertyOfType(ctx context.Context, params *GetPropertyOfTypeParams) (*SymbolResponse, error)</c>（session.go:4169-4189，@gen-proto-nullable）。</summary>
    internal SymbolResponse? HandleGetPropertyOfType(Context ctx, GetPropertyOfTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var prop = setup.Checker.GetPropertyOfType(t, @params.Name);
            if (prop is null)
            {
                return null;
            }

            return setup.NewSymbolResponse(prop);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeOfPropertyOfType(ctx context.Context, params *GetPropertyOfTypeParams) (*TypeResponse, error)</c>（session.go:4190-4206，@gen-proto-nullable）。</summary>
    internal TypeResponse? HandleGetTypeOfPropertyOfType(Context ctx, GetPropertyOfTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var propType = setup.Checker.GetTypeOfPropertyOfType(t, @params.Name);
            if (propType is null)
            {
                return null;
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, propType, setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetConstantValue(ctx context.Context, params *CheckerNodeParams) (*ConstantValueResponse, error)</c>（session.go:4207-4229，@gen-proto-nullable）。</summary>
    internal ConstantValueResponse? HandleGetConstantValue(Context ctx, CheckerNodeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);

            var result = new ConstantValueResponse();
            var value = setup.Checker.GetConstantValue(node);
            result.IsNumber = value is JsNum.Number;
            result.Value = Api.LiteralValueToJson(value);
            return result;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetSignatureFromDeclaration(ctx context.Context, params *CheckerNodeParams) (*SignatureResponse, error)</c>（session.go:4230-4246）。</summary>
    internal SignatureResponse? HandleGetSignatureFromDeclaration(Context ctx, CheckerNodeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);

            var sig = setup.Checker.GetSignatureFromDeclaration(node);
            if (sig is null)
            {
                return null;
            }

            return setup.NewSignatureResponse(sig);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetExportSpecifierLocalTargetSymbol(ctx context.Context, params *CheckerNodeParams) (*SymbolResponse, error)</c>（session.go:4247-4270，@gen-proto-nullable）。</summary>
    internal SymbolResponse? HandleGetExportSpecifierLocalTargetSymbol(Context ctx, CheckerNodeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);

            var symbol = setup.Checker.GetExportSpecifierLocalTargetSymbol(node);
            if (symbol is null)
            {
                return null;
            }

            return setup.NewSymbolResponse(symbol);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetAliasedSymbol(ctx context.Context, params *CheckerSymbolParams) (*SymbolResponse, error)</c>（session.go:4271-4287）。</summary>
    internal SymbolResponse HandleGetAliasedSymbol(Context ctx, CheckerSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            return setup.NewSymbolResponse(setup.Checker.GetAliasedSymbol(symbol))!;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetFullyQualifiedName(ctx context.Context, params *CheckerSymbolParams) (string, error)</c>（session.go:4288-4307）。</summary>
    internal string HandleGetFullyQualifiedName(Context ctx, CheckerSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            return setup.Checker.GetFullyQualifiedName(symbol);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetImmediateAliasedSymbol(ctx context.Context, params *CheckerSymbolParams) (*SymbolResponse, error)</c>（session.go:4308-4332，@gen-proto-nullable）。</summary>
    internal SymbolResponse? HandleGetImmediateAliasedSymbol(Context ctx, CheckerSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            var aliased = setup.Checker.GetImmediateAliasedSymbol(symbol);
            if (aliased is null)
            {
                return null;
            }

            return setup.NewSymbolResponse(aliased);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleMethodGetTargetSymbol(ctx context.Context, params *CheckerSymbolParams) (*SymbolResponse, error)</c>（session.go:4333-4347）。</summary>
    internal SymbolResponse? HandleMethodGetTargetSymbol(Context ctx, CheckerSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            var target = setup.Checker.GetTargetSymbol(symbol);
            if (target is null)
            {
                return null;
            }

            return setup.NewSymbolResponse(target);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetExportSymbolOfSymbolForChecker(ctx context.Context, params *CheckerSymbolParams) (*SymbolResponse, error)</c>（session.go:4348-4364）。</summary>
    internal SymbolResponse HandleGetExportSymbolOfSymbolForChecker(Context ctx, CheckerSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            return setup.NewSymbolResponse(setup.Checker.GetExportSymbolOfSymbol(symbol))!;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetExportsOfModule(ctx context.Context, params *CheckerSymbolParams) ([]*SymbolResponse, error)</c>（session.go:4365-4395，@gen-proto-nullable）。</summary>
    internal SymbolResponse[]? HandleGetExportsOfModule(Context ctx, CheckerSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            var exports = setup.Checker.GetExportsOfModule(symbol);
            if (exports.Length == 0)
            {
                return null;
            }

            Array.Sort(exports, setup.Checker.CompareSymbols);

            var results = new SymbolResponse[exports.Length];
            for (var i = 0; i < exports.Length; i++)
            {
                results[i] = setup.NewSymbolResponse(exports[i])!;
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetMemberInModuleExports(ctx context.Context, params *GetMemberInModuleExportsParams) (*SymbolResponse, error)</c>（session.go:4396-4420，@gen-proto-nullable）。</summary>
    internal SymbolResponse? HandleGetMemberInModuleExports(Context ctx, GetMemberInModuleExportsParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            var member = setup.Checker.TryGetMemberInModuleExports(@params.Name, symbol);
            if (member is null)
            {
                return null;
            }

            return setup.NewSymbolResponse(member);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetJSDocTags(ctx context.Context, params *CheckerSymbolParams) ([]*JSDocTagInfo, error)</c>（session.go:4421-4447，@gen-proto-nullable）。</summary>
    internal JSDocTagInfo[]? HandleGetJSDocTags(Context ctx, CheckerSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            var tags = Ls.Ls.GetSymbolJSDocTags(symbol);
            if (tags.Length == 0)
            {
                return null;
            }

            var results = new JSDocTagInfo[tags.Length];
            for (var i = 0; i < tags.Length; i++)
            {
                results[i] = new JSDocTagInfo { Name = tags[i].Name, Text = tags[i].Text };
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetDocumentationComment(ctx context.Context, params *CheckerSymbolParams) (string, error)</c>（session.go:4448-4467）。</summary>
    internal string HandleGetDocumentationComment(Context ctx, CheckerSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            return Ls.Ls.GetSymbolDocumentationComment(setup.Checker, symbol);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeArguments(ctx context.Context, params *CheckerTypeParams) ([]*TypeResponse, error)</c>（session.go:4468-4492，@gen-proto-nullable）。</summary>
    internal TypeResponse[]? HandleGetTypeArguments(Context ctx, CheckerTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var typeArgs = setup.Checker.GetTypeArguments(t);
            if (typeArgs.Length == 0)
            {
                return null;
            }

            var results = new TypeResponse[typeArgs.Length];
            for (var i = 0; i < typeArgs.Length; i++)
            {
                results[i] = setup.Sd.NewTypeResponse(setup.ProjectID, typeArgs[i], setup.Checker)!;
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTrueTypeOfConditionalType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:4493-4507）。</summary>
    internal TypeResponse? HandleGetTrueTypeOfConditionalType(Context ctx, GetTypePropertyParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            // Go 这里走的是 sd.resolveTypeHandle(params.Project, params.Type)（不是 setup.resolveTypeHandle），逐字保留。
            var t = setup.Sd.ResolveTypeHandle(@params.Project, @params.Type);

            var trueType = setup.Checker.GetTrueTypeOfConditionalType(t);
            if (trueType is null)
            {
                return null;
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, trueType, setup.Checker)!;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetFalseTypeOfConditionalType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:4508-4522）。</summary>
    internal TypeResponse? HandleGetFalseTypeOfConditionalType(Context ctx, GetTypePropertyParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            // 同 handleGetTrueTypeOfConditionalType：Go 用 sd.resolveTypeHandle(params.Project, params.Type)。
            var t = setup.Sd.ResolveTypeHandle(@params.Project, @params.Type);

            var falseType = setup.Checker.GetFalseTypeOfConditionalType(t);
            if (falseType is null)
            {
                return null;
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, falseType, setup.Checker)!;
        }
        finally
        {
            setup.Done();
        }
    }
}

// ported from: internal/api/session.go:1749-2288（parse / transpile / source-file lease / config & resolution 查询族）
//
// 与 Go 的偏离（逐条说明为什么 C# 必须不同）：
//
// 1) Go 的 `(T, error)` → C# 正常路径返回 T、错误路径抛异常。本片里 `fmt.Errorf("%w: …", ErrClientError)` 的分支
//    一律抛 `ClientErrorException`（其 Message 写成 Go `err.Error()` 的完整文本 `"api: client error: …"`，
//    因为 Api.cs 头注释已约定 C# 侧异常文本对应 Go 的 error 文本）；`errors.New("transpilation produced no output")`
//    与 `fmt.Errorf("failed to encode source file: %w", err)` 抛 `InvalidOperationException`（本仓对"非客户端错误的普通
//    error"的既定落法，先例 ContentMapper/HostImpl.cs:2219、Lsp/LspWatcher.cs:129）。
// 2) Go 的 `%q`（`could not read file %q`）→ C# 手工加引号，**不做 Go 风格的转义**（`\` 不会变成 `\\`）；差别只在
//    错误文本的转义形式上，不影响语义。
// 3) Go 的 `ctx context.Context`：本文件保留 `Context ctx` 形参以逐字对齐 Go 签名（多数 handler 不使用它）。
//    `if err := ctx.Err(); err != nil { return nil, err }`（transpileOutput）→
//    `ctx.CancellationToken.IsCancellationRequested` + `throw new OperationCanceledException(ctx.CancellationToken)`
//    （先例 Ipc/ConnAsync.cs:102：Context 只带 CancellationToken，区分不了 Canceled/DeadlineExceeded）。
// 4) Go 的 `FS().ReadFile(...) (string, bool)` → C# `TryReadFile(path, out var contents)`（本仓 §3.2 的 `(T, bool) → TryX`）；
//    因此 Go 的 `if !ok { … }` 变成 `if (!TryReadFile(...)) { … }`，两个分支体逐字对应。
// 5) Go 的 `encoder.EncodeSourceFile` 返回 `([]byte, *NodeIndexTable, error)`；C# 侧冻结为
//    `(byte[] Data, NodeIndexTable Table) EncodeSourceFile(SourceFile)` + 异常，所以 Go 的 `data, _, err := …`
//    落成 `EncodeSourceFile(sf).Data`，`err` 分支用 try/catch 保留 Go 的"包装后返回"语义（第 1 条）。
// 6) Go 的 `RawBinary(nil)` → C# `new RawBinary([])`：`RawBinary.Data` 不可空（Api.cs），空数组与 Go 的 nil 切片
//    在 protocol_msgpack 里都写出 0 字节。
// 7) Go 的 `handleReleaseSourceFile` **没有** ctx 参数（session.go:1893）→ C# 同形：`HandleReleaseSourceFile(ReleaseSourceFileParams?)`。
// 8) Go 的 `params.Options` 是值结构体，`options := params.Options; options.FileName = fileName` 改的是**副本**
//    （handleTranspileFromFile）；C# 的 proto 类型是引用类型，故显式 `new TranspileOptions { … }` 复制 Go 的三个字段，
//    保持"不改动入参对象"的语义。
// 9) Go 的 `map[string]any{}` → `new Dictionary<string, object?>()`（契约 §2/§5：Dictionary 是 Json 可编码的）。
// 10) Go 的包级函数 `transpileOutput`/`isValidCreateSourceFileScriptKind`/`newResolvedModuleResponse`/
//    `newResolvedTypeReferenceDirectiveResponse` → 同一个 partial 类里的 `internal static` 成员（PascalCase），
//    因为 C# 没有自由函数（契约 §2）。
// 11) `newResolvedModuleResponse` 里 Go 靠"nil 接收者安全"的 `resolution.IsResolved()` 处理 nil；本仓
//    `Module.ResolvedModule.IsResolved()` 是实例方法（Module/Types.cs 文件头第 2 条），故写 `resolution is null || !resolution.IsResolved()`。
// 12) `tspath.Xxx` 一律写成 `TypeScript.TSPath.TSPath.Xxx`：在 `TypeScript.Api` 里裸写 `TSPath` 会解析到**命名空间**
//    `TypeScript.TSPath`（契约 §2 的命名空间遮蔽），而 `using static` 又会被 Session 自己的 `ToPath` 方法遮蔽。
// 13) Go 的 `s.nextSourceFileLeaseID.Add(1)` → `(ulong)Interlocked.Increment(ref _nextSourceFileLeaseId)`
//    （契约 §4：`atomic.Uint64` → `long` 字段 + Interlocked）。
// 14) Go 的 `base64.StdEncoding.EncodeToString` → `Convert.ToBase64String`；`diagnostics.Cannot_read_file_0` →
//    `using static TypeScript.Diagnostics.Diagnostics;` + 裸名（本仓既定写法，见 Ast/Diagnostic.cs:30）。
// 15) Go 的 `core.ScriptKind`/`core.ResolutionMode` → C# `ScriptKind`/`ModuleKind`（成员名去前缀，契约 §2）。
namespace TypeScript.Api;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Api.Encoder.Encoder;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TsOptions.TsOptions;

public sealed partial class Session
{
    /// <summary>Go: <c>func (s *Session) handleParseCommandLine(ctx context.Context, params *ParseCommandLineParams) (*ConfigFileResponse, error)</c>（session.go:1749-1752）。</summary>
    internal ConfigFileResponse HandleParseCommandLine(Context ctx, ParseCommandLineParams @params)
    {
        return Api.NewConfigFileResponse(ParseCommandLine(@params.CommandLine, _snapshotHost))!;
    }

    /// <summary>Go: <c>func (s *Session) handleReadConfigFile(ctx context.Context, params *ReadConfigFileParams) (*ReadConfigFileResponse, error)</c>（session.go:1754-1775）。</summary>
    internal ReadConfigFileResponse HandleReadConfigFile(Context ctx, ReadConfigFileParams @params)
    {
        var configFileName = @params.File.ToAbsoluteFileName(GetCurrentDirectory());
        if (!_snapshotHost.FS().TryReadFile(configFileName, out var configFileContent))
        {
            return new ReadConfigFileResponse
            {
                Config = new Dictionary<string, object?>(),
                Error = Api.NewDiagnosticResponse(NewCompilerDiagnostic(Cannot_read_file_0, configFileName)),
            };
        }

        var (config, parseErrors) = ParseConfigFileTextToJson(
            configFileName,
            ToPath(configFileName),
            configFileContent
        );
        var response = new ReadConfigFileResponse { Config = config };
        if (parseErrors.Length > 0)
        {
            response.Error = Api.NewDiagnosticResponse(parseErrors[0]);
        }
        return response;
    }

    /// <summary>Go: <c>func (s *Session) handleParseJsonConfigFileContent(ctx context.Context, params *ParseJsonConfigFileContentParams) (*ConfigFileResponse, error)</c>（session.go:1777-1802）。</summary>
    internal ConfigFileResponse HandleParseJsonConfigFileContent(Context ctx, ParseJsonConfigFileContentParams @params)
    {
        if ((@params.ConfigDirectory is null) == (@params.ConfigFileName is null))
        {
            throw new ClientErrorException("api: client error: exactly one of configDirectory or configFileName is required");
        }

        var basePath = "";
        var configFileName = "";
        if (@params.ConfigDirectory is not null)
        {
            basePath = TypeScript.TSPath.TSPath.GetNormalizedAbsolutePath(@params.ConfigDirectory, GetCurrentDirectory());
        }
        else
        {
            configFileName = @params.ConfigFileName!.ToAbsoluteFileName(GetCurrentDirectory());
            basePath = TypeScript.TSPath.TSPath.GetDirectoryPath(configFileName);
        }

        var parsedCommandLine = ParseJsonConfigFileContent(
            Api.JsonValueToAny(@params.JSON),
            _snapshotHost,
            basePath,
            null, /*existingOptions*/
            configFileName,
            null, /*resolutionStack*/
            null /*extendedConfigCache*/
        );
        return Api.NewConfigFileResponse(parsedCommandLine)!;
    }

    /// <summary>Go: <c>func (s *Session) handleParseConfigFile(ctx context.Context, params *ParseConfigFileParams) (*ConfigFileResponse, error)</c>（session.go:1804-1828）。</summary>
    internal ConfigFileResponse HandleParseConfigFile(Context ctx, ParseConfigFileParams @params)
    {
        var configFileName = @params.File.ToAbsoluteFileName(GetCurrentDirectory());
        if (!_snapshotHost.FS().TryReadFile(configFileName, out var configFileContent))
        {
            throw new ClientErrorException($"api: client error: could not read file \"{configFileName}\"");
        }

        var configDir = TypeScript.TSPath.TSPath.GetDirectoryPath(configFileName);
        var tsConfigSourceFile = NewTsconfigSourceFileFromFilePath(
            configFileName,
            ToPath(configFileName),
            configFileContent
        );
        var parsedCommandLine = ParseJsonSourceFileConfigFileContent(
            tsConfigSourceFile,
            _snapshotHost,
            configDir,
            null, /*existingOptions*/
            null, /*existingOptionsRaw*/
            configFileName,
            null, /*resolutionStack*/
            null /*extendedConfigCache*/
        );
        return Api.NewConfigFileResponse(parsedCommandLine)!;
    }

    /// <summary>Go: <c>func (s *Session) handleTranspile(ctx context.Context, params *TranspileParams, declaration bool) (*TranspileOutputResponse, error)</c>（session.go:1830-1832）。</summary>
    internal TranspileOutputResponse HandleTranspile(Context ctx, TranspileParams @params, bool declaration)
    {
        return TranspileOutput(ctx, @params.Input, @params.Options, declaration);
    }

    /// <summary>Go: <c>func (s *Session) handleCreateSourceFile(ctx context.Context, params *CreateSourceFileParams) (any, error)</c>（session.go:1835-1842，<c>@gen-proto-result: SourceFileResponse</c>）。</summary>
    internal object? HandleCreateSourceFile(Context ctx, CreateSourceFileParams @params)
    {
        var lease = CreateSourceFile(@params.FileName, @params.SourceText, @params.Options);
        return EncodeLeasedSourceFile(lease);
    }

    /// <summary>Go: <c>func (s *Session) handleCreateSourceFileFromFile(ctx context.Context, params *CreateSourceFileFromFileParams) (any, error)</c>（session.go:1844-1855，<c>@gen-proto-result: SourceFileResponse</c>）。</summary>
    internal object? HandleCreateSourceFileFromFile(Context ctx, CreateSourceFileFromFileParams @params)
    {
        var fileName = TypeScript.TSPath.TSPath.GetNormalizedAbsolutePath(@params.FileName, GetCurrentDirectory());
        if (!_snapshotHost.FS().TryReadFile(fileName, out var sourceText))
        {
            throw new ClientErrorException($"api: client error: could not read file \"{fileName}\"");
        }
        var lease = CreateSourceFile(fileName, sourceText, @params.Options);
        return EncodeLeasedSourceFile(lease);
    }

    /// <summary>Go: <c>func (s *Session) createSourceFile(fileName string, sourceText string, options CreateSourceFileOptions) (*project.SourceFileLease, error)</c>（session.go:1857-1870）。</summary>
    internal Project.SourceFileLease CreateSourceFile(string fileName, string sourceText, CreateSourceFileOptions options)
    {
        var scriptKind = options.ScriptKind;
        if (scriptKind == ScriptKind.Unknown)
        {
            scriptKind = EnsureScriptKindFromFileName(fileName);
        }
        if (!IsValidCreateSourceFileScriptKind(scriptKind))
        {
            throw new ClientErrorException($"api: client error: invalid scriptKind {(int)scriptKind}");
        }
        fileName = TypeScript.TSPath.TSPath.GetNormalizedAbsolutePath(fileName, GetCurrentDirectory());
        return AcquireSourceFile(new SourceFileParseOptions
        {
            FileName = fileName,
            Path = ToPath(fileName),
        }, sourceText, scriptKind);
    }

    /// <summary>Go: <c>func (s *Session) acquireSourceFile(options ast.SourceFileParseOptions, sourceText string, scriptKind core.ScriptKind) *project.SourceFileLease</c>（session.go:1872-1874）。</summary>
    internal Project.SourceFileLease AcquireSourceFile(SourceFileParseOptions options, string sourceText, ScriptKind scriptKind)
    {
        return _snapshotHost.AcquireSourceFile(options, sourceText, scriptKind);
    }

    /// <summary>Go: <c>func (s *Session) encodeLeasedSourceFile(lease *project.SourceFileLease) (any, error)</c>（session.go:1876-1891）。</summary>
    internal object? EncodeLeasedSourceFile(Project.SourceFileLease lease)
    {
        byte[] data;
        try
        {
            data = EncodeSourceFile(lease.SourceFile());
        }
        catch (Exception ex)
        {
            lease.Release();
            throw new InvalidOperationException($"failed to encode source file: {ex.Message}", ex);
        }
        var id = new SourceFileLeaseID((ulong)Interlocked.Increment(ref _nextSourceFileLeaseId));
        SetSourceFileLease(data, id.Value);
        lock (_sourceFileLeasesMu)
        {
            _sourceFileLeases[id] = lease;
        }
        if (_useBinaryResponses)
        {
            return new RawBinary(data);
        }
        return new SourceFileResponse { Data = Convert.ToBase64String(data) };
    }

    /// <summary>Go: <c>func (s *Session) handleReleaseSourceFile(params *ReleaseSourceFileParams) (any, error)</c>（session.go:1893-1908；注意 Go 里没有 ctx 形参）。</summary>
    internal object? HandleReleaseSourceFile(ReleaseSourceFileParams? @params)
    {
        if (@params is null || @params.Lease.Value == 0)
        {
            throw new ClientErrorException("api: client error: empty source file lease");
        }
        Project.SourceFileLease? lease;
        lock (_sourceFileLeasesMu)
        {
            _sourceFileLeases.TryGetValue(@params.Lease, out lease);
            if (lease is not null)
            {
                _sourceFileLeases.Remove(@params.Lease);
            }
        }
        if (lease is null)
        {
            throw new ClientErrorException($"api: client error: source file lease {@params.Lease.Value} not found");
        }
        lease.Release();
        return true;
    }

    /// <summary>Go: <c>func (s *Session) releaseSourceFileLeases()</c>（session.go:1910-1921）。由 <c>Session.Close</c>（Session.Diagnostics.cs）调用。</summary>
    internal void ReleaseSourceFileLeases()
    {
        Project.SourceFileLease[] leases;
        lock (_sourceFileLeasesMu)
        {
            leases = new Project.SourceFileLease[_sourceFileLeases.Count];
            var index = 0;
            foreach (var lease in _sourceFileLeases.Values)
            {
                leases[index++] = lease;
            }
            _sourceFileLeases.Clear();
        }
        foreach (var lease in leases)
        {
            lease.Release();
        }
    }

    /// <summary>Go: <c>func isValidCreateSourceFileScriptKind(scriptKind core.ScriptKind) bool</c>（session.go:1923-1930）。</summary>
    internal static bool IsValidCreateSourceFileScriptKind(ScriptKind scriptKind)
    {
        switch (scriptKind)
        {
            case ScriptKind.JS:
            case ScriptKind.JSX:
            case ScriptKind.TS:
            case ScriptKind.TSX:
            case ScriptKind.JSON:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func (s *Session) handleTranspileFromFile(ctx context.Context, params *TranspileFromFileParams, declaration bool) (*TranspileOutputResponse, error)</c>（session.go:1932-1941）。</summary>
    internal TranspileOutputResponse HandleTranspileFromFile(Context ctx, TranspileFromFileParams @params, bool declaration)
    {
        var fileName = TypeScript.TSPath.TSPath.GetNormalizedAbsolutePath(@params.FileName, GetCurrentDirectory());
        if (!_snapshotHost.FS().TryReadFile(fileName, out var input))
        {
            throw new ClientErrorException($"api: client error: could not read file \"{fileName}\"");
        }
        // Go: options := params.Options; options.FileName = fileName（值拷贝 → C# 显式复制，见文件头第 8 条）。
        var options = new TranspileOptions
        {
            CompilerOptions = @params.Options.CompilerOptions,
            FileName = fileName,
            ReportDiagnostics = @params.Options.ReportDiagnostics,
        };
        return TranspileOutput(ctx, input, options, declaration);
    }

    /// <summary>Go: <c>func transpileOutput(ctx context.Context, input string, options TranspileOptions, declaration bool) (*TranspileOutputResponse, error)</c>（session.go:1943-1967）。</summary>
    internal static TranspileOutputResponse TranspileOutput(Context ctx, string input, TranspileOptions options, bool declaration)
    {
        var transpileOptions = new TypeScript.Transpile.Transpile.Options
        {
            CompilerOptions = options.CompilerOptions,
            FileName = options.FileName,
            ReportDiagnostics = options.ReportDiagnostics,
        };
        var output = declaration
            ? TypeScript.Transpile.Transpile.TranspileDeclaration(ctx.CancellationToken, input, transpileOptions)
            : TypeScript.Transpile.Transpile.TranspileModule(ctx.CancellationToken, input, transpileOptions);
        if (output is null)
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(ctx.CancellationToken);
            }
            throw new InvalidOperationException("transpilation produced no output");
        }
        return new TranspileOutputResponse
        {
            OutputText = output.OutputText,
            Diagnostics = Api.NewDiagnosticResponses(output.Diagnostics),
            SourceMapText = output.SourceMapText,
        };
    }

    /// <summary>Go: <c>func (s *Session) handleGetSourceFile(ctx context.Context, params *GetSourceFileParams) (any, error)</c>（session.go:1971-1983，<c>@gen-proto-result: SourceFileResponse</c>）。</summary>
    internal object? HandleGetSourceFile(Context ctx, GetSourceFileParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var program = sd.GetProgram(@params.Project);

        return EncodeSourceFileResponse(program.GetSourceFile(@params.File.ToFileName()));
    }

    /// <summary>Go: <c>func (s *Session) handleGetConfigFileNames(ctx context.Context, params *GetProjectDiagnosticsParams) ([]string, error)</c>（session.go:1987-2008，<c>@gen-proto-nullable</c>）。</summary>
    internal string[]? HandleGetConfigFileNames(Context ctx, GetProjectDiagnosticsParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var program = sd.GetProgram(@params.Project);

        var commandLine = program.CommandLine();
        if (commandLine is null || commandLine.ConfigFile is null || commandLine.ConfigFile.SourceFile is null)
        {
            return null;
        }

        var extendedFiles = commandLine.ExtendedSourceFiles();
        var configFiles = new List<string> { commandLine.ConfigFile.SourceFile.FileName() };
        configFiles.AddRange(extendedFiles ?? []);
        return configFiles.ToArray();
    }

    /// <summary>Go: <c>func (s *Session) handleGetConfigSourceFile(ctx context.Context, params *GetSourceFileParams) (any, error)</c>（session.go:2013-2050）。</summary>
    internal object? HandleGetConfigSourceFile(Context ctx, GetSourceFileParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var program = sd.GetProgram(@params.Project);

        var commandLine = program.CommandLine();
        if (commandLine is null || commandLine.ConfigFile is null || commandLine.ConfigFile.SourceFile is null)
        {
            return EncodeSourceFileResponse(null);
        }

        var requestedPath = TypeScript.TSPath.TSPath.ToPath(@params.File.ToFileName(), program.GetCurrentDirectory(), program.UseCaseSensitiveFileNames());
        var rootConfigSourceFile = commandLine.ConfigFile.SourceFile;
        if (rootConfigSourceFile.Path() == requestedPath)
        {
            return EncodeSourceFileResponse(rootConfigSourceFile);
        }

        foreach (var configFileName in commandLine.ExtendedSourceFiles() ?? [])
        {
            if (TypeScript.TSPath.TSPath.ToPath(configFileName, program.GetCurrentDirectory(), program.UseCaseSensitiveFileNames()) != requestedPath)
            {
                continue;
            }

            if (!sd.Snapshot.TryReadFile(configFileName, out var configFileContent))
            {
                return EncodeSourceFileResponse(null);
            }

            var configSourceFile = NewTsconfigSourceFileFromFilePath(configFileName, requestedPath, configFileContent);
            return EncodeSourceFileResponse(configSourceFile.SourceFile);
        }

        return EncodeSourceFileResponse(null);
    }

    /// <summary>Go: <c>func (s *Session) encodeSourceFileResponse(sourceFile *ast.SourceFile) (any, error)</c>（session.go:2052-2073）。</summary>
    internal object? EncodeSourceFileResponse(SourceFile? sourceFile)
    {
        if (sourceFile is null)
        {
            if (_useBinaryResponses)
            {
                return new RawBinary([]);
            }
            return null;
        }

        byte[] data;
        try
        {
            data = EncodeSourceFile(sourceFile);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"failed to encode source file: {ex.Message}", ex);
        }

        if (_useBinaryResponses)
        {
            return new RawBinary(data);
        }
        return new SourceFileResponse
        {
            Data = Convert.ToBase64String(data),
        };
    }

    /// <summary>Go: <c>func (s *Session) handleGetSourceFileNames(ctx context.Context, params *GetSourceFileNamesParams) ([]string, error)</c>（session.go:2075-2092）。</summary>
    internal string[] HandleGetSourceFileNames(Context ctx, GetSourceFileNamesParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var program = sd.GetProgram(@params.Project);

        var sourceFiles = program.GetSourceFiles();
        var result = new string[sourceFiles.Length];
        for (var i = 0; i < sourceFiles.Length; i++)
        {
            result[i] = sourceFiles[i].FileName();
        }
        return result;
    }

    /// <summary>Go: <c>func (s *Session) handleGetSourceFileMetadata(ctx context.Context, params *GetSourceFileParams) (*SourceFileMetadata, error)</c>（session.go:2097-2121，<c>@gen-proto-nullable</c>）。</summary>
    internal SourceFileMetadata? HandleGetSourceFileMetadata(Context ctx, GetSourceFileParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var program = sd.GetProgram(@params.Project);

        var sourceFile = program.GetSourceFile(@params.File.ToFileName());
        if (sourceFile is null)
        {
            return null;
        }

        var metaData = program.GetSourceFileMetaData(sourceFile.Path());
        return new SourceFileMetadata
        {
            IsDefaultLibrary = program.IsSourceFileDefaultLibrary(sourceFile.Path()),
            IsFromExternalLibrary = program.IsSourceFileFromExternalLibrary(sourceFile),
            PackageJsonType = metaData.PackageJsonType,
            PackageJsonDirectory = metaData.PackageJsonDirectory,
            ImpliedNodeFormat = metaData.ImpliedNodeFormat,
        };
    }

    /// <summary>Go: <c>func newResolvedModuleResponse(resolution *module.ResolvedModule) *ResolvedModule</c>（session.go:2123-2137）。</summary>
    internal static ResolvedModule? NewResolvedModuleResponse(Module.ResolvedModule? resolution)
    {
        if (resolution is null || !resolution.IsResolved())
        {
            return null;
        }
        return new ResolvedModule
        {
            ResolvedFileName = resolution.ResolvedFileName,
            OriginalPath = resolution.OriginalPath,
            Extension = resolution.Extension,
            ResolvedUsingTsExtension = resolution.ResolvedUsingTsExtension,
            ResolvedUsingExtraExtensions = resolution.ResolvedUsingExtraExtensions,
            PackageId = Api.NewPackageId(resolution.PackageId),
            IsExternalLibraryImport = resolution.IsExternalLibraryImport,
            AlternateResult = resolution.AlternateResult,
        };
    }

    /// <summary>Go: <c>func newResolvedTypeReferenceDirectiveResponse(resolution *module.ResolvedTypeReferenceDirective) *ResolvedTypeReferenceDirective</c>（session.go:2139-2151）。</summary>
    internal static ResolvedTypeReferenceDirective? NewResolvedTypeReferenceDirectiveResponse(Module.ResolvedTypeReferenceDirective? resolution)
    {
        if (resolution is null || !resolution.IsResolved())
        {
            return null;
        }
        return new ResolvedTypeReferenceDirective
        {
            Primary = resolution.Primary,
            ResolvedFileName = resolution.ResolvedFileName,
            OriginalPath = resolution.OriginalPath,
            PackageId = Api.NewPackageId(resolution.PackageId),
            IsExternalLibraryImport = resolution.IsExternalLibraryImport,
        };
    }

    /// <summary>Go: <c>func (s *Session) handleGetModeForUsageLocation(ctx context.Context, params *GetModeForUsageLocationParams) (core.ResolutionMode, error)</c>（session.go:2152-2174）。</summary>
    internal ModuleKind HandleGetModeForUsageLocation(Context ctx, GetModeForUsageLocationParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);
        var sourceFile = ResolveOptionalSourceFile(program, @params.File);
        var usage = sd.ResolveNodeHandle(program, @params.Usage);
        if (!IsStringLiteralLike(usage))
        {
            throw new ClientErrorException("api: client error: usage must be a StringLiteralLike node");
        }
        return program.GetModeForUsageLocation(sourceFile!, usage);
    }

    /// <summary>Go: <c>func (s *Session) handleGetModeForResolutionAtIndex(ctx context.Context, params *GetModeForResolutionAtIndexParams) (core.ResolutionMode, error)</c>（session.go:2175-2200）。</summary>
    internal ModuleKind HandleGetModeForResolutionAtIndex(Context ctx, GetModeForResolutionAtIndexParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);
        var sourceFile = ResolveOptionalSourceFile(program, @params.File)!;
        var resolutionCount = sourceFile.Imports().Length;
        foreach (var augmentation in sourceFile.ModuleAugmentations ?? [])
        {
            if (augmentation.Kind == Kind.StringLiteral)
            {
                resolutionCount++;
            }
        }
        if (@params.Index < 0 || @params.Index >= resolutionCount)
        {
            throw new ClientErrorException("api: client error: invalid resolution index");
        }
        return program.GetModeForResolutionAtIndex(sourceFile, @params.Index);
    }

    /// <summary>Go: <c>func (s *Session) handleGetResolvedModule(ctx context.Context, params *GetResolvedModuleParams) (*ResolvedModule, error)</c>（session.go:2201-2216，<c>@gen-proto-nullable</c>）。</summary>
    internal ResolvedModule? HandleGetResolvedModule(Context ctx, GetResolvedModuleParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);
        var sourceFile = ResolveOptionalSourceFile(program, @params.File)!;
        return NewResolvedModuleResponse(program.GetResolvedModule(sourceFile, @params.ModuleName, @params.Mode));
    }

    /// <summary>Go: <c>func (s *Session) handleGetResolvedModuleFromModuleSpecifier(ctx context.Context, params *GetResolvedModuleFromModuleSpecifierParams) (*ResolvedModule, error)</c>（session.go:2218-2247，<c>@gen-proto-nullable</c>）。</summary>
    internal ResolvedModule? HandleGetResolvedModuleFromModuleSpecifier(Context ctx, GetResolvedModuleFromModuleSpecifierParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);
        var node = sd.ResolveNodeHandle(program, @params.ModuleSpecifier);
        if (!IsStringLiteralLike(node))
        {
            throw new ClientErrorException("api: client error: moduleSpecifier must be a StringLiteralLike node");
        }
        var sourceFile = GetSourceFileOfNode(node);
        if (@params.SourceFile is not null)
        {
            sourceFile = ResolveOptionalSourceFile(program, @params.SourceFile);
        }
        if (sourceFile is null)
        {
            throw new ClientErrorException("api: client error: moduleSpecifier must have a SourceFile ancestor or sourceFile must be provided");
        }
        var mode = program.GetModeForUsageLocation(sourceFile, node);
        return NewResolvedModuleResponse(program.GetResolvedModule(sourceFile, node.Text(), mode));
    }

    /// <summary>Go: <c>func (s *Session) handleGetResolvedTypeReferenceDirective(ctx context.Context, params *GetResolvedTypeReferenceDirectiveParams) (*ResolvedTypeReferenceDirective, error)</c>（session.go:2249-2264，<c>@gen-proto-nullable</c>）。</summary>
    internal ResolvedTypeReferenceDirective? HandleGetResolvedTypeReferenceDirective(Context ctx, GetResolvedTypeReferenceDirectiveParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);
        var sourceFile = ResolveOptionalSourceFile(program, @params.File)!;
        return NewResolvedTypeReferenceDirectiveResponse(program.GetResolvedTypeReferenceDirective(sourceFile, @params.TypeDirectiveName, @params.Mode));
    }

    /// <summary>Go: <c>func (s *Session) handleGetResolvedTypeReferenceDirectiveFromReference(ctx context.Context, params *GetResolvedTypeReferenceDirectiveFromReferenceParams) (*ResolvedTypeReferenceDirective, error)</c>（session.go:2266-2287，<c>@gen-proto-nullable</c>）。</summary>
    internal ResolvedTypeReferenceDirective? HandleGetResolvedTypeReferenceDirectiveFromReference(Context ctx, GetResolvedTypeReferenceDirectiveFromReferenceParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);
        var sourceFile = ResolveOptionalSourceFile(program, @params.SourceFile)!;
        var mode = @params.ResolutionMode;
        if (mode == ModuleKind.None)
        {
            mode = program.GetDefaultResolutionModeForFile(sourceFile);
        }
        return NewResolvedTypeReferenceDirectiveResponse(program.GetResolvedTypeReferenceDirective(sourceFile, @params.TypeDirectiveName, mode));
    }
}

// ported from: internal/api/session.go:2840-3563
//   handleGetImportAdderEdits(2840-2934)、toAPITextEdits(2935-2957)、originalTextOffset(2958-2970)、
//   resolveTypePropertyOfType(2971-2991)、resolveTypeArrayPropertyOfType(2992-3016)、
//   resolveSymbolPropertyOfType(3017-3035)、resolveSymbolPropertyOfSymbol(3036-3056)、
//   resolveSymbolTablePropertyOfSymbol(3057-3098)、resolveSymbolArrayPropertyOfSignature(3099-3122)、
//   resolveSymbolPropertyOfSignature(3123-3140)、resolveTypeArrayPropertyOfSignature(3141-3164)、
//   resolveSignaturePropertyOfSignature(3165-3184)、handleGetContextualType(3185-3208)、
//   handleGetContextualTypeForArgument(3209-3223)、handleGetAwaitedType(3224-3238)、
//   handleGetBaseTypeOfLiteralType(3239-3254)、handleGetNonNullableType(3255-3270)、
//   handleGetTypeFromTypeNode(3271-3286)、handleGetWidenedType(3287-3302)、handleGetParameterType(3303-3321)、
//   handleGetTypeParameterAtPosition(3322-3339)、handleIsArrayLikeType(3340-3355)、
//   handleIsTypeAssignableTo(3356-3376)、handleGetShorthandAssignmentValueSymbol(3377-3400)、
//   handleGetTypeOfSymbolAtLocation(3401-3423)、handleTypeToTypeNode(3424-3463)、
//   handleSignatureToSignatureDeclaration(3464-3502)、handleTypeToString(3503-3529)、
//   handlePrintNode(3530-3542)、decodePrintNode(3543-3555)、newPrinter(3556-3563)
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `(T, error)` → 正常路径返回值 + 错误路径抛异常（契约 §1.2）。Go 里 `fmt.Errorf("%w: …", ErrClientError)`
//    的分支（找不到源文件、找不到工程、无法解析句柄、参数下标为负、base64/AST 解码失败）在 C# 里是
//    `ClientErrorException`，异常文本保留 Go 的完整消息（含 `api: client error: ` 前缀 —— Go 的
//    `ErrClientError.Error()` 就是这个词，客户端收到的文本因此逐字一致；同 Session.cs 的既有写法）。
//    找不到源文件的 `%v` 打的是整个 `DocumentIdentifier`；C# 侧改打它的文件名（Session.cs 里
//    `resolveLocation` 的同款处理：同类信息、更可读）。
//    注意 `handleTypeToTypeNode`/`handleSignatureToSignatureDeclaration` 里 `fmt.Errorf("failed to encode …: %w", err)`
//    是**普通**错误（不带 ErrClientError），C# 侧 `Encoder.EncodeNode` 内部失败直接抛 DebugFailureException，
//    本文件不额外包装（保持"错误向上抛"的单一语义，不再造一层异常类型）。
//
// 2) 方法命名/可见性：Go 的 `func (s *Session) handleXxx` → `HandleXxx` 都保留 `Handle` 前缀的 PascalCase
//    （契约 §4）；`resolveXxx` 系列是 Session.Symbols.cs 等兄弟分片的调用点 → `internal`（同一个 partial 类的
//    成员本来也能跨文件调用，`internal` 让跨分片契约显式化）。**全部成员（含 handle* 与字段）都是 `internal`**：
//    Go 的 `_test.go` 与实现同 package、能直接调用未导出成员，C# 的测试在另一个程序集，靠
//    `InternalsVisibleTo("TypeScript.Compiler.Tests")` 保住同样的覆盖（先例：Project/CheckerPoolTests.cs 文件头第 4 条）。
//
// 3) Go 的 nil 语义 → C# 可空性：
//    - `[]*TextEdit`/`*TypeResponse`/`*SymbolResponse`/`*SignatureResponse`（Go 里 `nil, nil` 表示"没有结果"）
//      → `T[]?` / `T?`；空切片（Go 的 `[]*TextEdit{}`，import adder 无 fix 时）→ C# 的**空数组** `[]`，
//      两者在 JSON 里分别是 `null` 与 `[]`，不能混同。
//    - `[]*TypeResponse` / `[]*SymbolResponse` 的元素在 Go 里是**指针**（`newTypeResponse` 对 nil 类型返回 nil），
//      C# 侧照此用可为空元素 `T?[]?`（外层与元素都可空），与消费方 Session.Symbols.cs 的声明
//      （`TypeResponse?[]?`/`SymbolResponse?[]?`）逐字一致。
//    - `params.Project` 是 `Project.ID`（`readonly record struct`，显式实现 `LsAutoImport.ProjectID`），
//      传给 `IsPreparedForImportingFile`/`NewView` 时由 C# 的接口转换隐式完成（Go 侧 `project.ID` 同样隐式满足）。
//
// 4) Go 的 getter 参数（`func(*checker.Type) *checker.Type` 等方法值）→ C# `Func<…>`。返回值声明为**可空**
//    （`Func<Type, Type?>`/`Func<Symbol, SymbolTable?>`…），因为 Go 的切片/map/指针 getter 都可能返回 nil
//    （`len(nil) == 0` 在 Go 里成立）；调用点的 lambda/方法组不需要额外 `!`。
//
// 5) `slices.SortFunc(symbols, setup.checker.CompareSymbols)` → `symbols.Sort(setup.Checker.CompareSymbols)`
//    （`List<T>.Sort(Comparison<T>)`，`CompareSymbols` 是 `(Symbol, Symbol) => int`，排序序与 Go 一致）。
//    Go 注释里"symbol table 的 map 迭代顺序随机，所以排序"的语义在 C# 里保持：仍然照抄这次排序。
//
// 6) `defer` → `try/finally`（仓内既有做法）：`defer setup.done()` 覆盖方法的所有退出路径 ⇒
//    `try { … } finally { setup.Done(); }`；`handleGetImportAdderEdits` 里 `defer preparedSnapshot.Deref()`
//    在 Go 里声明于 if 块内、作用域到函数末尾 ⇒ 外层 `try/finally` 包住 if 之后的全部代码，
//    `preparedSnapshot?.Deref()` 在 finally 里条件调用（未克隆时为 nil，Go 侧 defer 只在克隆后注册）。
//
// 7) `printer.NewPrinter(printer.PrinterOptions{…}, printer.PrintHandlers{}, nil)` → 全限定的
//    `TypeScript.Printer.Printer.NewPrinter(…)`（`Printer` 既是命名空间又是类型，先例 Ls/CallHierarchy.cs），
//    `PrintHandlers{}` → `new PrintHandlers()`，第三个参数（Go 的 nil emitContext）→ `null`。
//
// 8) `nodebuilder.Flags(params.Flags)` → `(NodeBuilderFlags)@params.Flags`，别名
//    `using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;`（`using TypeScript.NodeBuilder;` 会与类型名撞名，
//    先例 Checker/Printer.cs）；`ast.Kind(params.Kind)` → `(Kind)@params.Kind`。
//
// 9) `base64.StdEncoding.DecodeString` / `EncodeToString` → `Convert.FromBase64String` / `Convert.ToBase64String`。
//    非法 base64 在 Go 返回 error、在 C# 抛 FormatException ⇒ 捕获后按 Go 的文本包成 ClientErrorException。
//    `encoder.DecodeNodes` 在 C# 里落在**解码器**静态类 `TypeScript.Api.Encoder.Decoder`（Go 的 encoder 包在
//    C# 里拆成 Encoder/Decoder 两个静态类），失败抛 DebugFailureException ⇒ 同样包成 ClientErrorException。
//
// 10) Go 的 `if node == nil { return nil, nil }`（handleGetContextualType/handleGetShorthandAssignmentValueSymbol）：
//     `snapshotData.resolveNodeHandle` 在 Go 里失败必带 error（永不返回 `nil, nil`），所以这些分支是**不可达的
//     防御代码**；C# 的 `SnapshotData.ResolveNodeHandle` 声明为非空 + 抛异常，这里照抄源码文本保留判断
//     （对非空引用类型写 `is null` 不产生警告），语义与 Go 完全相同。
//
// 11) `any` → `object?`（契约 §5）：handleTypeToTypeNode/handleSignatureToSignatureDeclaration 返回
//     `RawBinary` 或 `SourceFileResponse`（`_useBinaryResponses` 为 true 时是前者）；
//     handleTypeToString 返回 string（Go 也是 `any`，但两条路径都是 string）。
//
// 12) `ast.IsSourceFile`/`node.AsSourceFile()`：C# 的 `AsSourceFile()` 在 kind 不匹配时返回 null（Go 是类型断言
//     panic），这里先 `IsSourceFile(node)` 再取，`SourceFile?` 传给 `Emit(node, sourceFile)`（Go 的 `*ast.SourceFile` 可空）。
using TypeScript.Checker;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using static TypeScript.Ast.Ast;
using static TypeScript.LsConv.LsConv;
using Kind = TypeScript.Ast.Kind;
using LspTextEdit = TypeScript.LsProto.TextEdit;
using Node = TypeScript.Ast.Node;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using Position = TypeScript.LsProto.Position;
using Signature = TypeScript.Checker.Signature;
using SourceFile = TypeScript.Ast.SourceFile;
using Symbol = TypeScript.Ast.Symbol;
using SymbolTable = TypeScript.Ast.SymbolTable;
using Type = TypeScript.Checker.Type;
using TypeFormatFlags = TypeScript.Checker.TypeFormatFlags;

namespace TypeScript.Api;

public sealed partial class Session
{
    /// <summary>Go: <c>func (s *Session) handleGetImportAdderEdits(ctx context.Context, params *GetImportAdderEditsParams) ([]*TextEdit, error)</c>（session.go:2840-2934）。</summary>
    internal TextEdit[]? HandleGetImportAdderEdits(Context ctx, GetImportAdderEditsParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var projectID = @params.Project;
        var workingSnapshot = sd.Snapshot;
        var program = sd.GetProgram(@params.Project);
        var sourceFile = program.GetSourceFile(@params.File.ToFileName());
        if (sourceFile is null)
        {
            throw new ClientErrorException($"api: client error: source file not found: {@params.File.ToFileName()}");
        }

        var userPreferences = workingSnapshot.UserPreferences();
        // Go 的 `defer preparedSnapshot.Deref()`（见文件头偏离 6）：克隆出来的快照在函数退出时释放。
        TypeScript.Project.Snapshot? preparedSnapshot = null;
        try
        {
            var registry = workingSnapshot.AutoImportRegistry();
            if (registry is null || !registry.IsPreparedForImportingFile(sourceFile.FileName(), projectID, userPreferences))
            {
                preparedSnapshot = _snapshotHost.CloneSnapshotWithAutoImports(ctx, workingSnapshot, @params.File.ToURI(GetCurrentDirectory()), null);
                if (_projectSession is not null)
                {
                    _projectSession.TryAdoptSnapshotInBackground(workingSnapshot, preparedSnapshot);
                }

                workingSnapshot = preparedSnapshot;
                var proj = workingSnapshot.ProjectCollection.GetProject(projectID);
                if (proj is null)
                {
                    throw new ClientErrorException($"api: client error: project {projectID.String()} not found");
                }

                program = proj.GetProgram();
                if (program is null)
                {
                    throw new ClientErrorException("api: client error: project has no program");
                }

                sourceFile = program.GetSourceFile(@params.File.ToFileName());
                if (sourceFile is null)
                {
                    throw new ClientErrorException($"api: client error: source file not found: {@params.File.ToFileName()}");
                }

                userPreferences = workingSnapshot.UserPreferences();
                registry = workingSnapshot.AutoImportRegistry();
            }

            if (registry is null)
            {
                return [];
            }

            var (ch, done) = program.GetTypeChecker(ctx);
            try
            {
                var view = TypeScript.LsAutoImport.AutoImport.NewView(
                    registry,
                    sourceFile,
                    projectID,
                    program,
                    ch,
                    userPreferences.ModuleSpecifierPreferences());
                var importAdder = TypeScript.LsAutoImport.AutoImport.NewImportAdder(
                    ctx,
                    program,
                    ch,
                    sourceFile,
                    view,
                    workingSnapshot.GetPreferences(sourceFile.FileName()).FormatCodeSettings,
                    workingSnapshot.Converters(),
                    userPreferences);

                for (var i = 0; i < @params.Actions.Length; i++)
                {
                    var action = @params.Actions[i];
                    // Go 的 `switch action.Kind { case ImportAdderActionKindImportSymbol: …; default: error }`。
                    // `ImportAdderActionKind` 是 Go 的字符串型枚举（proto.go:1685-1689），C# 侧落成
                    // readonly struct + 静态单例 `ImportSymbol`（Proto.Types2.cs:7446-7478，照 LsProto 的
                    // FoldingRangeKind），并定义了 `operator ==`/`IEquatable` ⇒ 这里的 `==` 与 Go 逐字对应且不装箱。
                    if (action.Kind == ImportAdderActionKind.ImportSymbol)
                    {
                        if (action.Symbol.Value == 0)
                        {
                            throw new ClientErrorException($"api: client error: import adder action {i} missing symbol");
                        }

                        var symbol = sd.ResolveSymbolHandle(action.Symbol);
                        var isValidTypeOnlyUseSite = true;
                        if (action.IsValidTypeOnlyUseSite is not null)
                        {
                            isValidTypeOnlyUseSite = action.IsValidTypeOnlyUseSite.Value;
                        }

                        importAdder.AddImportFromExportedSymbol(symbol, isValidTypeOnlyUseSite);
                    }
                    else
                    {
                        throw new ClientErrorException($"api: client error: unknown import adder action kind \"{action.Kind}\"");
                    }
                }

                if (!importAdder.HasFixes())
                {
                    return [];
                }

                return ToAPITextEdits(sourceFile, importAdder.Edits());
            }
            finally
            {
                done();
            }
        }
        finally
        {
            preparedSnapshot?.Deref();
        }
    }

    /// <summary>Go: <c>func toAPITextEdits(sourceFile *ast.SourceFile, edits []*lsproto.TextEdit) []*TextEdit</c>（session.go:2935-2957）。</summary>
    internal static TextEdit[]? ToAPITextEdits(SourceFile sourceFile, LspTextEdit[] edits)
    {
        var originalText = sourceFile.OriginalText();
        var lineMap = ComputeLSPLineStarts(originalText);
        var positionMap = ComputePositionMap(originalText);
        var result = new TextEdit[edits.Length];
        for (var i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            var (start, ok) = OriginalTextOffset(lineMap, edit.Range.Start, originalText.Length);
            if (!ok)
            {
                return null;
            }

            var (end, okEnd) = OriginalTextOffset(lineMap, edit.Range.End, originalText.Length);
            if (!okEnd)
            {
                return null;
            }

            result[i] = new TextEdit
            {
                Pos = positionMap.UTF8ToUTF16(start),
                End = positionMap.UTF8ToUTF16(end),
                NewText = edit.NewText,
            };
        }

        return result;
    }

    /// <summary>Go: <c>func originalTextOffset(lineMap *lsconv.LSPLineMap, position lsproto.Position, textLength int) (int, bool)</c>（session.go:2958-2970）。
    /// Go 的 `(int, bool)` 多返回值 → C# 具名元组；`textLength` 在 Go 里是 `len(originalText)`（UTF-8 字节数），
    /// C# 侧传 `originalText.Length`（UTF-16 代码单元数）—— 本仓内部坐标一律 UTF-16（契约 §1.4）。</summary>
    internal static (int Offset, bool Ok) OriginalTextOffset(LSPLineMap lineMap, Position position, int textLength)
    {
        var line = (int)position.Line;
        if (line < 0 || line >= lineMap.LineStarts.Length)
        {
            return (0, false);
        }

        var offset = lineMap.LineStarts[line] + (int)position.Character;
        if (offset < lineMap.LineStarts[line] || offset > textLength)
        {
            return (0, false);
        }

        return (offset, true);
    }

    /// <summary>Go: <c>func (s *Session) resolveTypePropertyOfType(ctx context.Context, params *GetTypePropertyParams, getter func(*checker.Type) *checker.Type) (*TypeResponse, error)</c>（session.go:2971-2991）。</summary>
    internal TypeResponse? ResolveTypePropertyOfType(Context ctx, GetTypePropertyParams @params, Func<Type, Type?> getter)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.Sd.ResolveTypeHandle(@params.Project, @params.Type);

            var result = getter(t);
            if (result is null)
            {
                return null;
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, result, setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) resolveTypeArrayPropertyOfType(ctx context.Context, params *GetTypePropertyParams, getter func(*checker.Type) []*checker.Type) ([]*TypeResponse, error)</c>（session.go:2992-3016）。</summary>
    internal TypeResponse?[]? ResolveTypeArrayPropertyOfType(Context ctx, GetTypePropertyParams @params, Func<Type, Type[]?> getter)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.Sd.ResolveTypeHandle(@params.Project, @params.Type);

            var types = getter(t);
            if (types is null || types.Length == 0)
            {
                return null;
            }

            var results = new TypeResponse?[types.Length];
            for (var i = 0; i < types.Length; i++)
            {
                results[i] = setup.Sd.NewTypeResponse(setup.ProjectID, types[i], setup.Checker);
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) resolveSymbolPropertyOfType(params *GetTypePropertyParams, getter func(*checker.Type) *ast.Symbol) (*SymbolResponse, error)</c>（session.go:3017-3035）。</summary>
    internal SymbolResponse? ResolveSymbolPropertyOfType(GetTypePropertyParams @params, Func<Type, Symbol?> getter)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var t = sd.ResolveTypeHandle(@params.Project, @params.Type);

        var result = getter(t);
        if (result is null)
        {
            return null;
        }

        return sd.NewSymbolResponse(result, @params.Project);
    }

    /// <summary>Go: <c>func (s *Session) resolveSymbolPropertyOfSymbol(params *GetSymbolPropertyParams, getter func(*ast.Symbol) *ast.Symbol) (*SymbolResponse, error)</c>（session.go:3036-3056）。</summary>
    internal SymbolResponse? ResolveSymbolPropertyOfSymbol(GetSymbolPropertyParams @params, Func<Symbol, Symbol?> getter)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var symbol = sd.ResolveSymbolHandle(@params.Symbol);

        var result = getter(symbol);
        if (result is null)
        {
            return null;
        }

        return sd.NewSymbolResponse(result, @params.Project);
    }

    /// <summary>Go: <c>func (s *Session) resolveSymbolTablePropertyOfSymbol(ctx context.Context, params *GetSymbolPropertyParams, getter func(*ast.Symbol) ast.SymbolTable) ([]*SymbolResponse, error)</c>（session.go:3057-3098）。
    /// Go 注释：结果按 checker 的规范符号顺序排序，让 API 消费者拿到稳定顺序（而不是 Go map 的随机迭代顺序）。</summary>
    internal SymbolResponse?[]? ResolveSymbolTablePropertyOfSymbol(Context ctx, GetSymbolPropertyParams @params, Func<Symbol, SymbolTable?> getter)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var symbol = sd.ResolveSymbolHandle(@params.Symbol);

        var symbolTable = getter(symbol);
        if (symbolTable is null || symbolTable.Count == 0)
        {
            return null;
        }

        if (symbolTable.Count == 1)
        {
            foreach (var sub in symbolTable.Values)
            {
                return [sd.NewSymbolResponse(sub, @params.Project)];
            }
        }

        // More than one symbol, need a checker to sort
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbols = new List<Symbol>(symbolTable.Count);
            foreach (var sub in symbolTable.Values)
            {
                symbols.Add(sub);
            }

            symbols.Sort(setup.Checker.CompareSymbols);

            var results = new SymbolResponse?[symbols.Count];
            for (var i = 0; i < symbols.Count; i++)
            {
                results[i] = setup.NewSymbolResponse(symbols[i]);
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) resolveSymbolArrayPropertyOfSignature(params *GetSignaturePropertyParams, getter func(*checker.Signature) []*ast.Symbol) ([]*SymbolResponse, error)</c>（session.go:3099-3122）。</summary>
    internal SymbolResponse?[]? ResolveSymbolArrayPropertyOfSignature(GetSignaturePropertyParams @params, Func<Signature, Symbol[]?> getter)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var sig = sd.ResolveSignatureHandle(@params.Project, @params.Signature);

        var symbols = getter(sig);
        if (symbols is null || symbols.Length == 0)
        {
            return null;
        }

        var results = new SymbolResponse?[symbols.Length];
        for (var i = 0; i < symbols.Length; i++)
        {
            results[i] = sd.NewSymbolResponse(symbols[i], @params.Project);
        }

        return results;
    }

    /// <summary>Go: <c>func (s *Session) resolveSymbolPropertyOfSignature(params *GetSignaturePropertyParams, getter func(*checker.Signature) *ast.Symbol) (*SymbolResponse, error)</c>（session.go:3123-3140）。</summary>
    internal SymbolResponse? ResolveSymbolPropertyOfSignature(GetSignaturePropertyParams @params, Func<Signature, Symbol?> getter)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var sig = sd.ResolveSignatureHandle(@params.Project, @params.Signature);

        var result = getter(sig);
        if (result is null)
        {
            return null;
        }

        return sd.NewSymbolResponse(result, @params.Project);
    }

    /// <summary>Go: <c>func (s *Session) resolveTypeArrayPropertyOfSignature(ctx context.Context, params *GetSignaturePropertyParams, getter func(signature *checker.Signature) []*checker.Type) ([]*TypeResponse, error)</c>（session.go:3141-3164）。</summary>
    internal TypeResponse?[]? ResolveTypeArrayPropertyOfSignature(Context ctx, GetSignaturePropertyParams @params, Func<Signature, Type[]?> getter)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sig = setup.Sd.ResolveSignatureHandle(@params.Project, @params.Signature);

            var types = getter(sig);
            if (types is null || types.Length == 0)
            {
                return null;
            }

            var results = new TypeResponse?[types.Length];
            for (var i = 0; i < types.Length; i++)
            {
                results[i] = setup.Sd.NewTypeResponse(setup.ProjectID, types[i], setup.Checker);
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) resolveSignaturePropertyOfSignature(params *GetSignaturePropertyParams, getter func(*checker.Signature) *checker.Signature) (*SignatureResponse, error)</c>（session.go:3165-3184）。</summary>
    internal SignatureResponse? ResolveSignaturePropertyOfSignature(GetSignaturePropertyParams @params, Func<Signature, Signature?> getter)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var sig = sd.ResolveSignatureHandle(@params.Project, @params.Signature);

        var result = getter(sig);
        if (result is null)
        {
            return null;
        }

        return sd.NewSignatureResponse(@params.Project, result);
    }

    /// <summary>Go: <c>func (s *Session) handleGetContextualType(ctx context.Context, params *GetContextualTypeParams) (*TypeResponse, error)</c>（session.go:3185-3208）。
    /// Go 的 `// @gen-proto-nullable`（返回可为空）。</summary>
    internal TypeResponse? HandleGetContextualType(Context ctx, GetContextualTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);
            if (node is null)
            {
                return null;
            }

            var t = setup.Checker.GetContextualType(node, TypeScript.Checker.ContextFlags.None);
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

    /// <summary>Go: <c>func (s *Session) handleGetContextualTypeForArgument(ctx context.Context, params *GetContextualTypeForArgumentParams) (*TypeResponse, error)</c>（session.go:3209-3223）。
    /// Go 的 `// @gen-proto-nullable`。</summary>
    internal TypeResponse? HandleGetContextualTypeForArgument(Context ctx, GetContextualTypeForArgumentParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);
            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetContextualTypeForArgumentAtIndex(node, @params.Index), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetAwaitedType(ctx context.Context, params *CheckerTypeParams) (*TypeResponse, error)</c>（session.go:3224-3238）。
    /// Go 的 `// @gen-proto-nullable`。</summary>
    internal TypeResponse? HandleGetAwaitedType(Context ctx, CheckerTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);
            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetAwaitedType(t), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetBaseTypeOfLiteralType(ctx context.Context, params *GetBaseTypeOfLiteralTypeParams) (*TypeResponse, error)</c>（session.go:3239-3254）。返回字面量类型的基类型（如 42 → number）。</summary>
    internal TypeResponse? HandleGetBaseTypeOfLiteralType(Context ctx, GetBaseTypeOfLiteralTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetBaseTypeOfLiteralType(t), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetNonNullableType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:3255-3270）。返回去掉 null 与 undefined 的类型。</summary>
    internal TypeResponse? HandleGetNonNullableType(Context ctx, GetTypePropertyParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetNonNullableType(t), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeFromTypeNode(ctx context.Context, params *GetTypeFromTypeNodeParams) (*TypeResponse, error)</c>（session.go:3271-3286）。返回类型节点对应的类型。</summary>
    internal TypeResponse? HandleGetTypeFromTypeNode(Context ctx, GetTypeFromTypeNodeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetTypeFromTypeNode(node), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetWidenedType(ctx context.Context, params *GetWidenedTypeParams) (*TypeResponse, error)</c>（session.go:3287-3302）。返回拓宽后的类型。</summary>
    internal TypeResponse? HandleGetWidenedType(Context ctx, GetWidenedTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetWidenedType(t), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetParameterType(ctx context.Context, params *GetParameterTypeParams) (*TypeResponse, error)</c>（session.go:3303-3321）。返回签名里给定下标参数的类型。</summary>
    internal TypeResponse? HandleGetParameterType(Context ctx, GetParameterTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sig = setup.ResolveSignatureHandle(@params.Signature);

            if (@params.Index < 0)
            {
                throw new ClientErrorException("api: client error: invalid parameter index");
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetTypeAtPosition(sig, @params.Index), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeParameterAtPosition(ctx context.Context, params *GetParameterTypeParams) (*TypeResponse, error)</c>（session.go:3322-3339）。</summary>
    internal TypeResponse? HandleGetTypeParameterAtPosition(Context ctx, GetParameterTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sig = setup.ResolveSignatureHandle(@params.Signature);
            if (@params.Index < 0)
            {
                throw new ClientErrorException("api: client error: invalid parameter index");
            }

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetTypeParameterAtPosition(sig, @params.Index), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleIsArrayLikeType(ctx context.Context, params *IsArrayLikeTypeParams) (bool, error)</c>（session.go:3340-3355）。返回类型是否 array-like。</summary>
    internal bool HandleIsArrayLikeType(Context ctx, IsArrayLikeTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            return setup.Checker.IsArrayLikeType(t);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleIsTypeAssignableTo(ctx context.Context, params *IsTypeAssignableToParams) (bool, error)</c>（session.go:3356-3376）。返回 source 是否可赋值给 target。</summary>
    internal bool HandleIsTypeAssignableTo(Context ctx, IsTypeAssignableToParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var source = setup.ResolveTypeHandle(@params.Source);
            var target = setup.ResolveTypeHandle(@params.Target);

            return setup.Checker.IsTypeAssignableTo(source, target);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetShorthandAssignmentValueSymbol(ctx context.Context, params *GetTypeAtLocationParams) (*SymbolResponse, error)</c>（session.go:3377-3400）。
    /// 返回简写属性赋值的值符号。Go 的 `// @gen-proto-nullable`。</summary>
    internal SymbolResponse? HandleGetShorthandAssignmentValueSymbol(Context ctx, GetTypeAtLocationParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);
            if (node is null)
            {
                return null;
            }

            var symbol = setup.Checker.GetShorthandAssignmentValueSymbol(node);
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

    /// <summary>Go: <c>func (s *Session) handleGetTypeOfSymbolAtLocation(ctx context.Context, params *GetTypeOfSymbolAtLocationParams) (*TypeResponse, error)</c>（session.go:3401-3423）。
    /// 返回符号在特定位置被窄化后的类型。</summary>
    internal TypeResponse? HandleGetTypeOfSymbolAtLocation(Context ctx, GetTypeOfSymbolAtLocationParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetTypeOfSymbolAtLocation(symbol, node), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleTypeToTypeNode(ctx context.Context, params *TypeToTypeNodeParams) (any, error)</c>（session.go:3424-3463）。
    /// 把 Type 转成 TypeNode AST 并以二进制编码返回。Go 的 `// @gen-proto-result: SourceFileResponse` / `// @gen-proto-nullable`。</summary>
    internal object? HandleTypeToTypeNode(Context ctx, TypeToTypeNodeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            Node? enclosingDeclaration = null;
            if (@params.Location.Value != "")
            {
                enclosingDeclaration = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);
            }

            var typeNode = setup.Checker.TypeToTypeNode(t, enclosingDeclaration, (NodeBuilderFlags)@params.Flags, null);
            if (typeNode is null)
            {
                return null;
            }

            var data = TypeScript.Api.Encoder.Encoder.EncodeNode(typeNode.AsNode(), null);

            if (_useBinaryResponses)
            {
                return new RawBinary(data);
            }

            return new SourceFileResponse
            {
                Data = Convert.ToBase64String(data),
            };
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleSignatureToSignatureDeclaration(ctx context.Context, params *SignatureToSignatureDeclarationParams) (any, error)</c>（session.go:3464-3502）。
    /// Go 的 `// @gen-proto-result: SourceFileResponse` / `// @gen-proto-nullable`。</summary>
    internal object? HandleSignatureToSignatureDeclaration(Context ctx, SignatureToSignatureDeclarationParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sig = setup.ResolveSignatureHandle(@params.Signature);

            Node? enclosingDeclaration = null;
            if (@params.Location.Value != "")
            {
                enclosingDeclaration = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);
            }

            var node = setup.Checker.SignatureToSignatureDeclaration(sig, (Kind)@params.Kind, enclosingDeclaration, (NodeBuilderFlags)@params.Flags);
            if (node is null)
            {
                return null;
            }

            var data = TypeScript.Api.Encoder.Encoder.EncodeNode(node.AsNode(), null);

            if (_useBinaryResponses)
            {
                return new RawBinary(data);
            }

            return new SourceFileResponse
            {
                Data = Convert.ToBase64String(data),
            };
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleTypeToString(ctx context.Context, params *TypeToTypeNodeParams) (any, error)</c>（session.go:3503-3529）。把 Type 转成字符串表示。</summary>
    internal object? HandleTypeToString(Context ctx, TypeToTypeNodeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            Node? enclosingDeclaration = null;
            if (@params.Location.Value != "")
            {
                enclosingDeclaration = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);
            }

            if (@params.Flags != 0)
            {
                return setup.Checker.TypeToStringEx(t, enclosingDeclaration, (TypeFormatFlags)@params.Flags, null);
            }

            return setup.Checker.TypeToStringEx(
                t,
                enclosingDeclaration,
                TypeFormatFlags.AllowUniqueESSymbolType | TypeFormatFlags.UseAliasDefinedOutsideCurrentScope,
                null);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handlePrintNode(_ context.Context, params *PrintNodeParams) (string, error)</c>（session.go:3530-3542）。解码二进制 AST 节点并打印成文本。</summary>
    internal string HandlePrintNode(Context ctx, PrintNodeParams @params)
    {
        var node = DecodePrintNode(@params.Data);

        SourceFile? sourceFile = null;
        if (IsSourceFile(node))
        {
            sourceFile = node.AsSourceFile();
        }

        return NewPrinter(@params).Emit(node, sourceFile);
    }

    /// <summary>Go: <c>func decodePrintNode(encoded string) (*ast.Node, error)</c>（session.go:3543-3555）。</summary>
    internal static Node DecodePrintNode(string encoded)
    {
        byte[] data;
        try
        {
            data = Convert.FromBase64String(encoded);
        }
        catch (FormatException err)
        {
            throw new ClientErrorException($"api: client error: invalid base64 data: {err.Message}", err);
        }

        try
        {
            return TypeScript.Api.Encoder.Decoder.DecodeNodes(data);
        }
        catch (DebugFailureException err)
        {
            throw new ClientErrorException($"api: client error: failed to decode AST: {err.Message}", err);
        }
    }

    /// <summary>Go: <c>func newPrinter(params *PrintNodeParams) *printer.Printer</c>（session.go:3556-3563）。</summary>
    internal static TypeScript.Printer.Printer NewPrinter(PrintNodeParams @params)
    {
        return TypeScript.Printer.Printer.NewPrinter(
            new TypeScript.Printer.PrinterOptions
            {
                PreserveSourceNewlines = @params.PreserveSourceNewlines,
                NeverAsciiEscape = @params.NeverAsciiEscape,
                TerminateUnterminatedLiterals = @params.TerminateUnterminatedLiterals,
            },
            new TypeScript.Printer.PrintHandlers(),
            null);
    }
}

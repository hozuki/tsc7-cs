// ported from: internal/api/session.go:2288-2840
//
// 覆盖：symbol / type / signature 三类"查询 + 属性"端点，共 46 个 handler：
//   handleGetSymbolAtPosition …  handleGetTypesAtPositions（位置/位置集合查询）
//   handleGetParentOfSymbol … handleGetTargetOfSignature（symbol/type/signature 的属性代理）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 错误：Go 的 `(T, error)` → C# 正常路径返回 T，错误路径抛异常。`fmt.Errorf("%w: …", ErrClientError, …)`
//    → `throw new ClientErrorException("api: client error: …")`。**为什么必须自己写前缀**：Go 的 `%w` 展开后
//    错误文本是 `ErrClientError.Error() + ": " + 细节`，而 C# 的 `ClientErrorException(message)`（Api.cs:43-45）
//    原样透传 message、不加任何前缀，所以这里逐字写出以保证错误文本与 Go 一致。
//
// 2) `defer setup.done()`：C# 没有 defer → `try { … } finally { setup.Done(); }`。**必须**用 finally，
//    因为 Go 的 defer 在错误路径（本文件里即异常）也会执行；若只在末尾调用，`source file not found` 之类
//    的抛出会让 checker 租约泄漏。`CheckerSetup` 是 readonly struct，`var setup = SetupChecker(...)` 后
//    `setup.Done()` 即 Go 值接收者的 `setup.done()`。
//
// 3) astnav 的 nil 判定被省略：Go 的 `astnav.GetTouchingPropertyName` 顶层循环只返回非 nil
//    （tokens.go:215-270：`return child` / `return current` / `return sourceFile.GetOrCreateToken(...)`），
//    且本仓 `TypeScript.AstNav.AstNav.GetTouchingPropertyName`（AstNav/Tokens.cs:41）声明为非空返回 →
//    Go 里 `if node == nil { return nil, nil }` 在 C# 不可达，删掉（行为等价，不是"漏移植"）。
//
// 4) `checker.GetTypeAtLocation` 同理：本仓 Checker.C8.SymbolQueries.cs:799 声明
//    `public Type GetTypeAtLocation(Node node)`（非空），故 handleGetTypeAtPosition 里 Go 的 `if t == nil` 分支省略。
//
// 5) `sourceFile.AsNode()` 逐字保留：本仓 `Ast.SourceFile : Ast.Node`（SourceFile.cs:88），`Node.AsNode()` 返回自身
//    （Node.cs:42），所以 `sourceFile.AsNode()` 的静态类型就是 `Ast.Node`，与 Go 的嵌入方法一致。
//
// 6) 类型转换：`ast.SymbolFlags(params.Meaning)`（uint32 → 新类型）→ `(Ast.SymbolFlags)@params.Meaning`；
//    `checker.SignatureKind(params.Kind)` → `(Checker.SignatureKind)@params.Kind`。**为什么不同**：C# 枚举不能
//    隐式/隐式转换自整数以外的表达式，必须显式强制转换。
//
// 7) 多值结果一律 `X?[]`（元素可空）：Go 的 `make([]*SymbolResponse, n)` / `make([]*TypeResponse, n)` 元素是**指针**，
//    未赋值的元素就是 nil（会编码成 JSON `null`）；C# 用 `SymbolResponse?[]` / `TypeResponse?[]` 精确对应，
//    数组本身非空（Go 的 slice 头非 nil，编码成 `[]`）。元素恒非 nil 的端点（getSymbolsInScope、getTypesOfSymbols、
//    getTypeAtLocations、getSignaturesOfType）同样落成 `X?[]`，与 Go 的指针切片类型对齐、也是对兄弟分片
//    `NewXxxResponse` 可空性的解耦。
//    委托给 `resolveXxxArrayPropertyOf…` 的端点（getMembersOfSymbol、getExportsOfSymbol、getTypesOfType、
//    getTypeParametersOfType、getOuterTypeParametersOfType、getLocalTypeParametersOfType、getAliasTypeArgumentsOfType、
//    getTypeParametersOfSignature、getParametersOfSignature）返回类型是 **`X?[]?`**（数组本身也可空）：Go 的 helper
//    在结果为空时 `return nil, nil`（session.go:2995-2997 / 3088-3090 / 3110-3112 等），nil slice 会编码成 JSON `null`；
//    其余端点自己 `make(...)`，slice 头非 nil，故数组非空。
//
// 8) `symbol.Members` / `symbol.Exports` 在本仓是 `Ast.SymbolTable?`（Ast/Symbol.cs:27-28），而"属性"端点的
//    getter 形参被冻结为 `Func<Ast.Symbol, Ast.SymbolTable>`（非空）→ 写成
//    `static symbol => symbol.Members ?? new Ast.SymbolTable()`。**行为等价**：Go 里 nil map 与空 map 在 `len`/`range`
//    上完全一致，`resolveSymbolTablePropertyOfSymbol`（session.go:3057）只判 `len == 0`。
//
// 9) Go 的方法表达式（method expression）`(*checker.Type).Symbol`、`(*checker.Signature).TypeParameters`、`(*checker.Type).Types`
//    → C# 没有方法表达式，写成 lambda `static t => t.Symbol()` / `static sig => sig.TypeParameters()`。参数 `t`/`sig`
//    由 helper 内部保证非 nil（Go 侧亦然：nil 接收者会 panic，本仓实例方法不能在 null 上分派）。
//
// 10) `t.AsLiteralType()` / `t.AsInterfaceType()` / `t.AsIndexedAccessType()` / `t.AsConditionalType()` /
//     `t.AsSubstitutionType()` / `t.AsMappedType()`：Go 在类型不匹配时 panic，本仓（Types.Type.cs:81-105）返回可空
//     → 用 `!` 断言非空，与 Go 的 panic 语义对应（这 6 组端点本来就只对相应类型有意义）。
//
// 11) `setup.newSignatureResponse(setup.checker.GetResolvedSignature(node))`：Go 的 `snapshotData.newSignatureResponse`
//     先判 `sig == nil` 并返回 nil（session.go:350-353），而本仓 `CheckerSetup.NewSignatureResponse(Checker.Signature)`
//     的形参非空 → 在 handleGetResolvedSignature 里显式写成 `signature is null ? null : setup.NewSignatureResponse(signature)`，
//     与 Go 的 nil 短路等价（nil 时不写注册表、响应为 nil）。
//
// 12) `handleResolveName` 的 `resolveLocation` 可能返回 nil（Go 的 location 为 `*ast.Node`，允许 nil），
//     而本仓 `Checker.ResolveName(string, Node, …)`（Checker/Exports.cs:189）把 location 标注为非空
//     → 传参处写 `location!`。**为什么安全**：它直接转调 `_resolveName`，后者的委托类型是
//     `Func<Node?, string, SymbolFlags, Message?, bool, bool, Symbol?>`（Checker/Struct.cs:109），Go 自身也在
//     checker.go:1254 传 nil，故运行时传 null 与 Go 完全一致。
//
// 13) 返回类型用 Go 的具体类型（`SymbolResponse?` / `TypeResponse?[]` …）而不是 `object?`：Go 的 handler 签名就是
//     `(*SymbolResponse, error)`，`object?` 只出现在 HandleRequest 的 `(any, error)` 处；C# 里具体引用类型到
//     `object?` 是隐式转换，调用点（Session.Requests.cs 的调度 switch）无需装箱。
using TypeScript.Core;

namespace TypeScript.Api;

public sealed partial class Session
{
    /// <summary>Go: <c>func (s *Session) handleGetSymbolAtPosition(ctx context.Context, params *GetSymbolAtPositionParams) (*SymbolResponse, error)</c>（session.go:2288-2316）。</summary>
    internal SymbolResponse? HandleGetSymbolAtPosition(Context ctx, GetSymbolAtPositionParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sourceFile = setup.Program.GetSourceFile(@params.File.ToFileName());
            if (sourceFile is null)
            {
                throw new ClientErrorException($"api: client error: source file not found: {@params.File}");
            }

            var positionMap = sourceFile.GetPositionMap();
            var node = TypeScript.AstNav.AstNav.GetTouchingPropertyName(sourceFile, positionMap.UTF16ToUTF8((int)@params.Position));

            var symbol = setup.Checker.GetSymbolAtLocation(node);
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

    /// <summary>Go: <c>func (s *Session) handleGetSymbolOfSourceFile(ctx context.Context, params *GetSymbolOfSourceFileParams) (*SymbolResponse, error)</c>（session.go:2317-2336）。
    /// 返回源文件的模块符号；非模块（脚本）文件返回 null。</summary>
    internal SymbolResponse? HandleGetSymbolOfSourceFile(Context ctx, GetSymbolOfSourceFileParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sourceFile = setup.Program.GetSourceFile(@params.File.ToFileName());
            if (sourceFile is null)
            {
                throw new ClientErrorException($"api: client error: source file not found: {@params.File}");
            }

            var symbol = setup.Checker.GetSymbolAtLocation(sourceFile.AsNode());
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

    /// <summary>Go: <c>func (s *Session) handleGetSymbolsOfSourceFiles(ctx context.Context, params *GetSymbolsOfSourceFilesParams) ([]*SymbolResponse, error)</c>（session.go:2337-2358）。
    /// 返回多个源文件的模块符号（各自可能为 null）。</summary>
    internal SymbolResponse?[] HandleGetSymbolsOfSourceFiles(Context ctx, GetSymbolsOfSourceFilesParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var files = @params.Files ?? [];
            var results = new SymbolResponse?[files.Length];
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                var sourceFile = setup.Program.GetSourceFile(file.ToFileName());
                if (sourceFile is null)
                {
                    throw new ClientErrorException($"api: client error: source file not found: {file}");
                }
                var symbol = setup.Checker.GetSymbolAtLocation(sourceFile.AsNode());
                if (symbol is not null)
                {
                    results[i] = setup.NewSymbolResponse(symbol);
                }
            }
            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetSymbolsAtPositions(ctx context.Context, params *GetSymbolsAtPositionsParams) ([]*SymbolResponse, error)</c>（session.go:2359-2388）。
    /// 返回一个文件里多个位置的符号（无符号处为 null）。</summary>
    internal SymbolResponse?[] HandleGetSymbolsAtPositions(Context ctx, GetSymbolsAtPositionsParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sourceFile = setup.Program.GetSourceFile(@params.File.ToFileName());
            if (sourceFile is null)
            {
                throw new ClientErrorException($"api: client error: source file not found: {@params.File}");
            }

            var positionMap = sourceFile.GetPositionMap();
            var positions = @params.Positions ?? [];
            var results = new SymbolResponse?[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                var pos = positions[i];
                var node = TypeScript.AstNav.AstNav.GetTouchingPropertyName(sourceFile, positionMap.UTF16ToUTF8((int)pos));
                var symbol = setup.Checker.GetSymbolAtLocation(node);
                if (symbol is not null)
                {
                    results[i] = setup.NewSymbolResponse(symbol);
                }
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetSymbolAtLocation(ctx context.Context, params *GetSymbolAtLocationParams) (*SymbolResponse, error)</c>（session.go:2389-2412）。</summary>
    internal SymbolResponse? HandleGetSymbolAtLocation(Context ctx, GetSymbolAtLocationParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);

            var symbol = setup.Checker.GetSymbolAtLocation(node);
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

    /// <summary>Go: <c>func (s *Session) handleGetSymbolsAtLocations(ctx context.Context, params *GetSymbolsAtLocationsParams) ([]*SymbolResponse, error)</c>（session.go:2413-2438）。
    /// 返回多个节点位置的符号（无符号处为 null）。</summary>
    internal SymbolResponse?[] HandleGetSymbolsAtLocations(Context ctx, GetSymbolsAtLocationsParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var locations = @params.Locations ?? [];
            var results = new SymbolResponse?[locations.Length];
            for (var i = 0; i < locations.Length; i++)
            {
                var node = setup.Sd.ResolveNodeHandle(setup.Program, locations[i]);
                var symbol = setup.Checker.GetSymbolAtLocation(node);
                if (symbol is not null)
                {
                    results[i] = setup.NewSymbolResponse(symbol);
                }
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeOfSymbol(ctx context.Context, params *GetTypeOfSymbolParams) (*TypeResponse, error)</c>（session.go:2439-2454）。</summary>
    internal TypeResponse? HandleGetTypeOfSymbol(Context ctx, GetTypeOfSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetTypeOfSymbol(symbol), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypesOfSymbols(ctx context.Context, params *GetTypesOfSymbolsParams) ([]*TypeResponse, error)</c>（session.go:2455-2476）。
    /// resolveSymbolHandle 对不可解析的句柄抛异常、GetTypeOfSymbol 永不返回 nil，故每个元素都解析成类型（最差是 error type）。</summary>
    internal TypeResponse?[] HandleGetTypesOfSymbols(Context ctx, GetTypesOfSymbolsParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbolHandles = @params.Symbols ?? [];
            var results = new TypeResponse?[symbolHandles.Length];
            for (var i = 0; i < symbolHandles.Length; i++)
            {
                var symbol = setup.ResolveSymbolHandle(symbolHandles[i]);
                results[i] = setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetTypeOfSymbol(symbol), setup.Checker);
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetDeclaredTypeOfSymbol(ctx context.Context, params *GetTypeOfSymbolParams) (*TypeResponse, error)</c>（session.go:2477-2492）。
    /// 返回符号的声明类型（例如类型别名符号的别名体）。</summary>
    internal TypeResponse? HandleGetDeclaredTypeOfSymbol(Context ctx, GetTypeOfSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetDeclaredTypeOfSymbol(symbol), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetNonMissingTypeOfSymbol(ctx context.Context, params *GetTypeOfSymbolParams) (*TypeResponse, error)</c>（session.go:2493-2509）。
    /// 返回符号的类型，排除 missing type。</summary>
    internal TypeResponse? HandleGetNonMissingTypeOfSymbol(Context ctx, GetTypeOfSymbolParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetNonMissingTypeOfSymbol(symbol), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleResolveName(ctx context.Context, params *ResolveNameParams) (*SymbolResponse, error)</c>（session.go:2510-2531）。
    /// 在给定位置解析一个名字到符号；location 由 node handle 或 fileName+position 二者之一给出。</summary>
    internal SymbolResponse? HandleResolveName(Context ctx, ResolveNameParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            // 位置既可能来自节点句柄，也可能来自 fileName+position（见文件头第 12 条：location 允许为 nil）。
            var location = setup.ResolveLocation(@params.Location, @params.File, @params.Position);

            var symbol = setup.Checker.ResolveName(@params.Name, location!, (Ast.SymbolFlags)@params.Meaning, @params.ExcludeGlobals);
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

    /// <summary>Go: <c>func (s *Session) handleGetSymbolsInScope(ctx context.Context, params *GetSymbolsInScopeParams) ([]*SymbolResponse, error)</c>（session.go:2532-2556）。
    /// 返回在给定位置可见的、具有指定 meaning 的所有符号。</summary>
    internal SymbolResponse?[] HandleGetSymbolsInScope(Context ctx, GetSymbolsInScopeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var location = setup.ResolveLocation(@params.Location, @params.File, @params.Position);
            if (location is null)
            {
                throw new ClientErrorException("api: client error: getSymbolsInScope requires a location");
            }

            var symbols = setup.Checker.GetSymbolsInScope(location, (Ast.SymbolFlags)@params.Meaning);
            var results = new SymbolResponse?[symbols.Length];
            for (var i = 0; i < symbols.Length; i++)
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

    /// <summary>Go: <c>func (s *Session) handleGetSignaturesOfType(ctx context.Context, params *GetSignaturesOfTypeParams) ([]*SignatureResponse, error)</c>（session.go:2557-2578）。
    /// 返回类型的调用签名或构造签名。</summary>
    internal SignatureResponse?[] HandleGetSignaturesOfType(Context ctx, GetSignaturesOfTypeParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var t = setup.ResolveTypeHandle(@params.Type);

            var sigs = setup.Checker.GetSignaturesOfType(t, (Checker.SignatureKind)@params.Kind);
            var results = new SignatureResponse?[sigs.Length];
            for (var i = 0; i < sigs.Length; i++)
            {
                results[i] = setup.NewSignatureResponse(sigs[i]);
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetResolvedSignature(ctx context.Context, params *GetResolvedSignatureParams) (*SignatureResponse, error)</c>（session.go:2579-2594）。
    /// 返回 call-like 表达式解析出的签名（见文件头第 11 条：GetResolvedSignature 可能为 nil）。</summary>
    internal SignatureResponse? HandleGetResolvedSignature(Context ctx, GetResolvedSignatureParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);

            var signature = setup.Checker.GetResolvedSignature(node);
            return signature is null ? null : setup.NewSignatureResponse(signature);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeAtLocation(ctx context.Context, params *GetTypeAtLocationParams) (*TypeResponse, error)</c>（session.go:2595-2610）。</summary>
    internal TypeResponse? HandleGetTypeAtLocation(Context ctx, GetTypeAtLocationParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var node = setup.Sd.ResolveNodeHandle(setup.Program, @params.Location);

            return setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetTypeAtLocation(node), setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeAtLocations(ctx context.Context, params *GetTypeAtLocationsParams) ([]*TypeResponse, error)</c>（session.go:2611-2633）。
    /// resolveNodeHandle 对不可解析的句柄抛异常、GetTypeAtLocation 永不返回 nil，故每个元素都解析成类型（最差是 error type）。</summary>
    internal TypeResponse?[] HandleGetTypeAtLocations(Context ctx, GetTypeAtLocationsParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var locations = @params.Locations ?? [];
            var results = new TypeResponse?[locations.Length];
            for (var i = 0; i < locations.Length; i++)
            {
                var node = setup.Sd.ResolveNodeHandle(setup.Program, locations[i]);
                results[i] = setup.Sd.NewTypeResponse(setup.ProjectID, setup.Checker.GetTypeAtLocation(node), setup.Checker);
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeAtPosition(ctx context.Context, params *GetTypeAtPositionParams) (*TypeResponse, error)</c>（session.go:2634-2660）。</summary>
    internal TypeResponse? HandleGetTypeAtPosition(Context ctx, GetTypeAtPositionParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sourceFile = setup.Program.GetSourceFile(@params.File.ToFileName());
            if (sourceFile is null)
            {
                throw new ClientErrorException($"api: client error: source file not found: {@params.File}");
            }

            var positionMap = sourceFile.GetPositionMap();
            var node = TypeScript.AstNav.AstNav.GetTouchingPropertyName(sourceFile, positionMap.UTF16ToUTF8((int)@params.Position));

            var t = setup.Checker.GetTypeAtLocation(node);

            return setup.Sd.NewTypeResponse(setup.ProjectID, t, setup.Checker);
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypesAtPositions(ctx context.Context, params *GetTypesAtPositionsParams) ([]*TypeResponse, error)</c>（session.go:2661-2689）。
    /// 返回一个文件里多个位置的类型（无类型处为 null）。</summary>
    internal TypeResponse?[] HandleGetTypesAtPositions(Context ctx, GetTypesAtPositionsParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var sourceFile = setup.Program.GetSourceFile(@params.File.ToFileName());
            if (sourceFile is null)
            {
                throw new ClientErrorException($"api: client error: source file not found: {@params.File}");
            }

            var positionMap = sourceFile.GetPositionMap();
            var positions = @params.Positions ?? [];
            var results = new TypeResponse?[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                var pos = positions[i];
                var node = TypeScript.AstNav.AstNav.GetTouchingPropertyName(sourceFile, positionMap.UTF16ToUTF8((int)pos));
                var t = setup.Checker.GetTypeAtLocation(node);
                results[i] = setup.Sd.NewTypeResponse(setup.ProjectID, t, setup.Checker);
            }

            return results;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetParentOfSymbol(_ context.Context, params *GetSymbolPropertyParams) (*SymbolResponse, error)</c>（session.go:2690-2694）。</summary>
    internal SymbolResponse? HandleGetParentOfSymbol(Context ctx, GetSymbolPropertyParams @params)
    {
        return ResolveSymbolPropertyOfSymbol(@params, static sym => sym.Parent);
    }

    /// <summary>Go: <c>func (s *Session) handleGetMembersOfSymbol(ctx context.Context, params *GetSymbolPropertyParams) ([]*SymbolResponse, error)</c>（session.go:2695-2701）。</summary>
    internal SymbolResponse?[]? HandleGetMembersOfSymbol(Context ctx, GetSymbolPropertyParams @params)
    {
        return ResolveSymbolTablePropertyOfSymbol(ctx, @params, static symbol => symbol.Members ?? new Ast.SymbolTable());
    }

    /// <summary>Go: <c>func (s *Session) handleGetExportsOfSymbol(ctx context.Context, params *GetSymbolPropertyParams) ([]*SymbolResponse, error)</c>（session.go:2702-2708）。</summary>
    internal SymbolResponse?[]? HandleGetExportsOfSymbol(Context ctx, GetSymbolPropertyParams @params)
    {
        return ResolveSymbolTablePropertyOfSymbol(ctx, @params, static symbol => symbol.Exports ?? new Ast.SymbolTable());
    }

    /// <summary>Go: <c>func (s *Session) handleGetExportSymbolOfSymbol(_ context.Context, params *GetSymbolPropertyParams) (*SymbolResponse, error)</c>（session.go:2709-2713）。</summary>
    internal SymbolResponse? HandleGetExportSymbolOfSymbol(Context ctx, GetSymbolPropertyParams @params)
    {
        return ResolveSymbolPropertyOfSymbol(@params, static sym => sym.ExportSymbol);
    }

    /// <summary>Go: <c>func (s *Session) handleGetSymbolOfType(_ context.Context, params *GetTypePropertyParams) (*SymbolResponse, error)</c>（session.go:2714-2717）。</summary>
    internal SymbolResponse? HandleGetSymbolOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveSymbolPropertyOfType(@params, static t => t.Symbol());
    }

    /// <summary>Go: <c>func (s *Session) handleGetTargetOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2718-2722）。</summary>
    internal TypeResponse? HandleGetTargetOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.Target());
    }

    /// <summary>Go: <c>func (s *Session) handleGetFreshTypeOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2723-2727）。</summary>
    internal TypeResponse? HandleGetFreshTypeOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsLiteralType()!.FreshType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetRegularTypeOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2728-2732）。</summary>
    internal TypeResponse? HandleGetRegularTypeOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsLiteralType()!.RegularType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypesOfType(ctx context.Context, params *GetTypePropertyParams) ([]*TypeResponse, error)</c>（session.go:2733-2737）。</summary>
    internal TypeResponse?[]? HandleGetTypesOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypeArrayPropertyOfType(ctx, @params, static t => t.Types());
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeParametersOfType(ctx context.Context, params *GetTypePropertyParams) ([]*TypeResponse, error)</c>（session.go:2738-2742）。</summary>
    internal TypeResponse?[]? HandleGetTypeParametersOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypeArrayPropertyOfType(ctx, @params, static t => t.AsInterfaceType()!.TypeParameters());
    }

    /// <summary>Go: <c>func (s *Session) handleGetOuterTypeParametersOfType(ctx context.Context, params *GetTypePropertyParams) ([]*TypeResponse, error)</c>（session.go:2743-2747）。</summary>
    internal TypeResponse?[]? HandleGetOuterTypeParametersOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypeArrayPropertyOfType(ctx, @params, static t => t.AsInterfaceType()!.OuterTypeParameters());
    }

    /// <summary>Go: <c>func (s *Session) handleGetLocalTypeParametersOfType(ctx context.Context, params *GetTypePropertyParams) ([]*TypeResponse, error)</c>（session.go:2748-2752）。</summary>
    internal TypeResponse?[]? HandleGetLocalTypeParametersOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypeArrayPropertyOfType(ctx, @params, static t => t.AsInterfaceType()!.LocalTypeParameters());
    }

    /// <summary>Go: <c>func (s *Session) handleGetThisTypeOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2753-2757）。</summary>
    internal TypeResponse? HandleGetThisTypeOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsInterfaceType()!.ThisType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetAliasTypeArgumentsOfType(ctx context.Context, params *GetTypePropertyParams) ([]*TypeResponse, error)</c>（session.go:2758-2767）。
    /// 别名不存在时返回 null（Go 的 nil slice）。</summary>
    internal TypeResponse?[]? HandleGetAliasTypeArgumentsOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypeArrayPropertyOfType(ctx, @params, static t =>
        {
            if (t.Alias() is null)
            {
                return null;
            }
            return t.Alias()!.TypeArguments();
        });
    }

    /// <summary>Go: <c>func (s *Session) handleGetAliasSymbolOfType(_ context.Context, params *GetTypePropertyParams) (*SymbolResponse, error)</c>（session.go:2768-2776）。
    /// 别名不存在时返回 null。</summary>
    internal SymbolResponse? HandleGetAliasSymbolOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveSymbolPropertyOfType(@params, static t =>
        {
            if (t.Alias() is null)
            {
                return null;
            }
            return t.Alias()!.Symbol();
        });
    }

    /// <summary>Go: <c>func (s *Session) handleGetObjectTypeOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2777-2780）。</summary>
    internal TypeResponse? HandleGetObjectTypeOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsIndexedAccessType()!.ObjectType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetIndexTypeOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2781-2784）。</summary>
    internal TypeResponse? HandleGetIndexTypeOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsIndexedAccessType()!.IndexType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetCheckTypeOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2785-2788）。</summary>
    internal TypeResponse? HandleGetCheckTypeOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsConditionalType()!.CheckType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetExtendsTypeOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2789-2792）。</summary>
    internal TypeResponse? HandleGetExtendsTypeOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsConditionalType()!.ExtendsType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetBaseTypeOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2793-2798）。</summary>
    internal TypeResponse? HandleGetBaseTypeOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsSubstitutionType()!.BaseType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetConstraintOfType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2799-2802）。
    /// 返回替换类型的约束；类型参数的约束由 HandleGetConstraintOfTypeParameter 处理。</summary>
    internal TypeResponse? HandleGetConstraintOfType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsSubstitutionType()!.SubstConstraint());
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeParameterOfMappedType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2803-2806）。</summary>
    internal TypeResponse? HandleGetTypeParameterOfMappedType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsMappedType()!.TypeParameter());
    }

    /// <summary>Go: <c>func (s *Session) handleGetConstraintTypeOfMappedType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2807-2811）。</summary>
    internal TypeResponse? HandleGetConstraintTypeOfMappedType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsMappedType()!.ConstraintType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetNameTypeOfMappedType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2812-2815）。</summary>
    internal TypeResponse? HandleGetNameTypeOfMappedType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsMappedType()!.NameType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetTemplateTypeOfMappedType(ctx context.Context, params *GetTypePropertyParams) (*TypeResponse, error)</c>（session.go:2816-2820）。</summary>
    internal TypeResponse? HandleGetTemplateTypeOfMappedType(Context ctx, GetTypePropertyParams @params)
    {
        return ResolveTypePropertyOfType(ctx, @params, static t => t.AsMappedType()!.TemplateType());
    }

    /// <summary>Go: <c>func (s *Session) handleGetTypeParametersOfSignature(ctx context.Context, params *GetSignaturePropertyParams) ([]*TypeResponse, error)</c>（session.go:2821-2825）。</summary>
    internal TypeResponse?[]? HandleGetTypeParametersOfSignature(Context ctx, GetSignaturePropertyParams @params)
    {
        return ResolveTypeArrayPropertyOfSignature(ctx, @params, static sig => sig.TypeParameters());
    }

    /// <summary>Go: <c>func (s *Session) handleGetParametersOfSignature(_ context.Context, params *GetSignaturePropertyParams) ([]*SymbolResponse, error)</c>（session.go:2826-2830）。</summary>
    internal SymbolResponse?[]? HandleGetParametersOfSignature(Context ctx, GetSignaturePropertyParams @params)
    {
        return ResolveSymbolArrayPropertyOfSignature(@params, static sig => sig.Parameters());
    }

    /// <summary>Go: <c>func (s *Session) handleGetThisParameterOfSignature(_ context.Context, params *GetSignaturePropertyParams) (*SymbolResponse, error)</c>（session.go:2831-2835）。</summary>
    internal SymbolResponse? HandleGetThisParameterOfSignature(Context ctx, GetSignaturePropertyParams @params)
    {
        return ResolveSymbolPropertyOfSignature(@params, static sig => sig.ThisParameter());
    }

    /// <summary>Go: <c>func (s *Session) handleGetTargetOfSignature(_ context.Context, params *GetSignaturePropertyParams) (*SignatureResponse, error)</c>（session.go:2836-2839）。</summary>
    internal SignatureResponse? HandleGetTargetOfSignature(Context ctx, GetSignaturePropertyParams @params)
    {
        return ResolveSignaturePropertyOfSignature(@params, static sig => sig.Target());
    }
}

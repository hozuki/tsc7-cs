// ported from: internal/compiler/program.go:574-666,667-742,743-875（BindSourceFiles / checker 获取 / 模块解析查询 /
// packagesMap / collect*Diagnostics / Syntactic・Bind・Semantic・Suggestion 诊断入口 / SkipTypeChecking / getSourceFilesToEmit）
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) `(checker, func())` → C# 命名元组 `(Checker Checker, Action Done)`（与 `Checker.NewChecker` 的 `(Checker, Lock)` 同款）。
// 2) Go 的闭包捕获循环变量（Go 1.22 语义：每次迭代一个新变量）→ C# 需要显式局部副本。
// 3) `core.NewWorkGroup(...)` → `NewWorkGroup(...)`（`using static TypeScript.Core.Core`）。
// 4) go 语句级 `defer f()()` → `try/finally`。
namespace TypeScript.Compiler;

using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Checker;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.PackageJson;
using TypeScript.Tracing;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;

public sealed partial class Program
{
    /// <summary>Go: <c>func (p *Program) BindSourceFiles()</c>（program.go:574）。</summary>
    public void BindSourceFiles()
    {
        var wg = NewWorkGroup(SingleThreaded());
        foreach (var file in _files)
        {
            if (!file.IsBound())
            {
                var current = file;
                wg.Queue(() =>
                {
                    Action done = Noop;
                    if (_opts.Tracing is not null)
                    {
                        done = _opts.Tracing.Push(Phase.Bind, "bindSourceFile", new Dictionary<string, object?> { ["path"] = current.Path().Value }, true);
                    }
                    try
                    {
                        Binder.BindSourceFile(current);
                    }
                    finally
                    {
                        done();
                    }
                });
            }
        }
        wg.RunAndWait();
    }

    private static void Noop()
    {
    }

    /// <summary>Go: <c>func (p *Program) GetTypeChecker(ctx context.Context) (*checker.Checker, func())</c>（program.go:590）。</summary>
    public (Checker Checker, Action Done) GetTypeChecker(Context ctx)
    {
        if (_compilerCheckerPool is not null)
        {
            return _compilerCheckerPool.GetCheckerNonExclusive();
        }
        return _checkerPool.GetChecker(ctx, null);
    }

    /// <summary>Go: <c>func (p *Program) ForEachCheckerParallel(cb func(idx int, c *checker.Checker))</c>（program.go:597）。</summary>
    public void ForEachCheckerParallel(Action<int, Checker> cb)
    {
        if (_compilerCheckerPool is not null)
        {
            _compilerCheckerPool.ForEachCheckerParallel(cb);
        }
    }

    /// <summary>
    /// Go: <c>func (p *Program) GetTypeCheckerForFile(ctx context.Context, file *ast.SourceFile) (*checker.Checker, func())</c>（program.go:607）。
    /// 并发场景下可能有多个 checker；这里返回被指派检查该文件的那个。注意不同 checker 产出的类型不能混用。
    /// </summary>
    public (Checker Checker, Action Done) GetTypeCheckerForFile(Context ctx, SourceFile file)
    {
        if (_compilerCheckerPool is not null)
        {
            return _compilerCheckerPool.GetCheckerForFileNonExclusive(file);
        }
        return _checkerPool.GetChecker(ctx, file);
    }

    /// <summary>
    /// Go: <c>func (p *Program) GetTypeCheckerForFileExclusive(ctx context.Context, file *ast.SourceFile) (*checker.Checker, func())</c>（program.go:616）。
    /// 锁到当前线程以防多线程访问同一 checker；调用 <c>Done</c> 时释放。
    /// </summary>
    public (Checker Checker, Action Done) GetTypeCheckerForFileExclusive(Context ctx, SourceFile file)
    {
        if (_compilerCheckerPool is not null)
        {
            return _compilerCheckerPool.GetCheckerForFileExclusive(ctx, file);
        }
        return _checkerPool.GetChecker(ctx, file);
    }

    /// <summary>Go: <c>func (p *Program) GetResolvedModule(file ast.HasFileName, moduleReference string, mode core.ResolutionMode) *module.ResolvedModule</c>（program.go:623）。</summary>
    public ResolvedModule? GetResolvedModule(HasFileName file, string moduleReference, ModuleKind mode)
    {
        if (_resolvedModules.TryGetValue(file.Path(), out var resolutions) &&
            resolutions.TryGetValue(new ModeAwareCacheKey(moduleReference, mode), out var resolved))
        {
            return resolved;
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Program) GetResolvedModuleFromModuleSpecifier(file ast.HasFileName, moduleSpecifier *ast.StringLiteralLike) *module.ResolvedModule</c>（program.go:632）。</summary>
    public ResolvedModule? GetResolvedModuleFromModuleSpecifier(HasFileName file, Node moduleSpecifier)
    {
        if (!IsStringLiteralLike(moduleSpecifier))
        {
            Debug.Fail("moduleSpecifier must be a StringLiteralLike");
        }
        var mode = GetModeForUsageLocation(file, moduleSpecifier);
        return GetResolvedModule(file, moduleSpecifier.Text(), mode);
    }

    /// <summary>Go: <c>func (p *Program) GetResolvedModules() map[tspath.Path]module.ModeAwareCache[*module.ResolvedModule]</c>（program.go:640）。</summary>
    public Dictionary<Path, ModeAwareCache<ResolvedModule>> GetResolvedModules() => _resolvedModules;

    /// <summary>Go: <c>func (p *Program) ModuleResolutionError() error</c>（program.go:644）。</summary>
    public Exception? ModuleResolutionError() => _moduleResolutionError;

    /// <summary>
    /// Go: <c>func (p *Program) GetPackagesMap() map[string]bool</c>（program.go:650）：
    /// 包名 → 是否自带类型（types）的惰性缓存，供增量诊断重填使用。
    /// </summary>
    public Dictionary<string, bool> GetPackagesMap()
    {
        lock (_packagesMapOnceLock)
        {
            if (!_packagesMapComputed)
            {
                var result = new Dictionary<string, bool>();
                foreach (var resolvedModulesInFile in _resolvedModules.Values)
                {
                    foreach (var mod in resolvedModulesInFile.Values)
                    {
                        var name = mod.PackageId.Name;
                        if (name != "")
                        {
                            result[name] = result.GetValueOrDefault(name) || mod.Extension == ExtensionDts;
                        }
                    }
                }
                _packagesMap = result;
                _packagesMapComputed = true;
            }
        }
        return _packagesMap!;
    }

    /// <summary>
    /// Go: <c>func (p *Program) collectDiagnostics(ctx, sourceFile, concurrent, collect) []*ast.Diagnostic</c>（program.go:667）：
    /// sourceFile 非 nil 时只收该文件，否则收整个程序。
    /// </summary>
    private Diagnostic[] CollectDiagnostics(Context ctx, SourceFile? sourceFile, bool concurrent, Func<Context, SourceFile, Diagnostic[]> collect)
    {
        Diagnostic[] result;
        if (sourceFile is not null)
        {
            result = collect(ctx, sourceFile);
        }
        else
        {
            var diagnostics = CollectDiagnosticsFromFiles(ctx, _files, concurrent, collect);
            result = [.. diagnostics.SelectMany(static d => d)];
        }
        return FilterAndSortDiagnostics(result);
    }

    /// <summary>Go: <c>func (p *Program) collectDiagnosticsFromFiles(...) [][]*ast.Diagnostic</c>（program.go:678）。</summary>
    private Diagnostic[][] CollectDiagnosticsFromFiles(Context ctx, SourceFile[] sourceFiles, bool concurrent, Func<Context, SourceFile, Diagnostic[]> collect)
    {
        var diagnostics = new Diagnostic[sourceFiles.Length][];
        var wg = NewWorkGroup(!concurrent || SingleThreaded());
        for (var i = 0; i < sourceFiles.Length; i++)
        {
            var index = i;
            var file = sourceFiles[i];
            wg.Queue(() =>
            {
                diagnostics[index] = collect(ctx, file);
            });
        }
        wg.RunAndWait();
        return diagnostics;
    }

    /// <summary>
    /// Go: <c>func (p *Program) collectCheckerDiagnostics(...)</c>（program.go:695）：
    /// 回调会拿到每个文件的 checker；内置池支持按 checker 分组并行（减少争用、改善缓存局部性），
    /// 否则退化成逐文件并发收集。
    /// </summary>
    private Diagnostic[] CollectCheckerDiagnostics(Context ctx, SourceFile? sourceFile, Func<Context, Checker, SourceFile, Diagnostic[]> collect)
    {
        if (sourceFile is not null)
        {
            if (SkipTypeChecking(sourceFile, false))
            {
                return [];
            }
            var (c, done) = GetTypeCheckerForFileExclusive(ctx, sourceFile);
            var result = collect(ctx, c, sourceFile);
            done();
            return FilterAndSortDiagnostics(result);
        }
        return FilterAndSortDiagnostics([.. CollectCheckerDiagnosticsFromFiles(ctx, _files, collect).SelectMany(static d => d)]);
    }

    /// <summary>Go: <c>func filterAndSortDiagnostics(diags) []*ast.Diagnostic</c>（program.go:708）——包级函数。</summary>
    internal static Diagnostic[] FilterAndSortDiagnostics(Diagnostic[] diags)
    {
        return Compiler.SortAndDeduplicateDiagnostics(Filter(diags, diag =>
        {
            var file = diag.File();
            if (!diag.ReportsUnnecessary() || file is null || file.SpanMap() is null || diag.Source() != "")
            {
                return true;
            }
            var (_, fidelity) = file.SpanMap()!.VirtualToOriginalSpan(diag.Loc());
            return fidelity != TypeScript.SpanMap.Fidelity.None;
        }));
    }

    /// <summary>Go: <c>func (p *Program) collectCheckerDiagnosticsFromFiles(...) [][]*ast.Diagnostic</c>（program.go:720）。</summary>
    private Diagnostic[][] CollectCheckerDiagnosticsFromFiles(Context ctx, SourceFile[] sourceFiles, Func<Context, Checker, SourceFile, Diagnostic[]> collect)
    {
        var diagnostics = new Diagnostic[sourceFiles.Length][];
        if (_compilerCheckerPool is not null)
        {
            _compilerCheckerPool.ForEachCheckerGroupDo(ctx, sourceFiles, SingleThreaded(), (c, fileIndex, file) =>
            {
                diagnostics[fileIndex] = collect(ctx, c, file);
            });
        }
        else
        {
            var wg = NewWorkGroup(SingleThreaded());
            for (var i = 0; i < sourceFiles.Length; i++)
            {
                var index = i;
                var file = sourceFiles[i];
                if (SkipTypeChecking(file, false))
                {
                    continue;
                }
                wg.Queue(() =>
                {
                    var (c, done) = _checkerPool.GetChecker(ctx, file);
                    diagnostics[index] = collect(ctx, c, file);
                    done();
                });
            }
            wg.RunAndWait();
        }
        return diagnostics;
    }

    /// <summary>Go: <c>func (p *Program) GetSyntacticDiagnostics(ctx, sourceFile) []*ast.Diagnostic</c>（program.go:743）。</summary>
    public Diagnostic[] GetSyntacticDiagnostics(Context ctx, SourceFile? sourceFile)
    {
        return CollectDiagnostics(ctx, sourceFile, false /*concurrent*/, (_, file) =>
        {
            var diags = Concatenate(file.Diagnostics(), file.JSDiagnostics());
            // 对于不会被 checker 检查的 JS 文件（没有 checkJs/ts-check），需要依赖编译选项的
            // 程序级语法检查（对应 Strada 的 getJSSyntacticDiagnosticsForFile）。
            if (IsSourceFileJS(file) && !IsCheckJSEnabledForFile(file, Options()))
            {
                diags = [.. diags, .. GetAdditionalJSSyntacticDiagnostics(file, Options())];
            }
            return diags;
        });
    }

    /// <summary>
    /// Go: <c>func getAdditionalJSSyntacticDiagnostics(file *ast.SourceFile, options *core.CompilerOptions) []*ast.Diagnostic</c>（program.go:762）
    /// ——包级函数。
    /// </summary>
    internal static Diagnostic[] GetAdditionalJSSyntacticDiagnostics(SourceFile file, CompilerOptions options)
    {
        if (options.ExperimentalDecorators.IsTrue())
        {
            return [];
        }
        var diags = new List<Diagnostic>();
        // 参数装饰器只在 experimentalDecorators 下合法；没有它时该由 checker 报，
        // 但 checker 不跑未勾选的 JS 文件。
        Visitor walk = null!;
        walk = node =>
        {
            if ((node.SubtreeFacts & SubtreeFacts.ContainsDecorators) == 0)
            {
                return false;
            }
            if (node.Kind == Kind.Parameter && HasDecorators(node))
            {
                var decorator = Find(node.ModifierNodes(), IsDecorator);
                if (decorator is not null)
                {
                    diags.Add(NewDiagnostic(file, decorator.Loc, Decorators_are_not_valid_here));
                }
            }
            node.ForEachChild(walk);
            return false;
        };
        file.AsNode().ForEachChild(walk);
        return [.. diags];
    }

    /// <summary>Go: <c>func (p *Program) GetBindDiagnostics(ctx, sourceFile) []*ast.Diagnostic</c>（program.go:787）。</summary>
    public Diagnostic[] GetBindDiagnostics(Context ctx, SourceFile? sourceFile)
    {
        if (sourceFile is not null)
        {
            Binder.BindSourceFile(sourceFile);
        }
        else
        {
            BindSourceFiles();
        }
        return CollectDiagnostics(ctx, sourceFile, false /*concurrent*/, (_, file) => file.BindDiagnostics());
    }

    /// <summary>Go: <c>func (p *Program) GetSemanticDiagnostics(ctx, sourceFile) []*ast.Diagnostic</c>（program.go:798）。</summary>
    public Diagnostic[] GetSemanticDiagnostics(Context ctx, SourceFile? sourceFile) =>
        CollectCheckerDiagnostics(ctx, sourceFile, GetSemanticDiagnosticsWithChecker);

    /// <summary>
    /// Go: <c>func (p *Program) GetSemanticDiagnosticsForIncremental(ctx, sourceFiles) map[*ast.SourceFile][]*ast.Diagnostic</c>（program.go:804）：
    /// 每个文件的缓存诊断里补进新发现的 globals；noEmit 过滤交给 builder。
    /// </summary>
    public Dictionary<SourceFile, Diagnostic[]> GetSemanticDiagnosticsForIncremental(Context ctx, SourceFile[] sourceFiles)
    {
        var allDiags = CollectCheckerDiagnosticsFromFiles(ctx, sourceFiles, (cctx, c, file) =>
            GetBindAndCheckDiagnosticsWithChecker(cctx, c, file, true /*includeDeferredGlobals*/));
        var result = new Dictionary<SourceFile, Diagnostic[]>(sourceFiles.Length);
        for (var i = 0; i < allDiags.Length; i++)
        {
            result[sourceFiles[i]] = FilterAndSortDiagnostics(allDiags[i]);
        }
        return result;
    }

    /// <summary>Go: <c>func (p *Program) GetSuggestionDiagnostics(ctx, sourceFile) []*ast.Diagnostic</c>（program.go:815）。</summary>
    public Diagnostic[] GetSuggestionDiagnostics(Context ctx, SourceFile? sourceFile) =>
        CollectCheckerDiagnostics(ctx, sourceFile, GetSuggestionDiagnosticsWithChecker);

    /// <summary>Go: <c>func (p *Program) SkipTypeChecking(sourceFile *ast.SourceFile, ignoreNoCheck bool) bool</c>（program.go:848）。</summary>
    public bool SkipTypeChecking(SourceFile sourceFile, bool ignoreNoCheck) =>
        (!ignoreNoCheck && Options().NoCheck.IsTrue()) ||
        Options().SkipLibCheck.IsTrue() && sourceFile.IsDeclarationFile ||
        Options().SkipDefaultLibCheck.IsTrue() && IsSourceFileDefaultLibrary(sourceFile.Path()) ||
        IsSourceFromProjectReference(sourceFile.Path()) ||
        !CanIncludeBindAndCheckDiagnostics(sourceFile);

    /// <summary>Go: <c>func (p *Program) canIncludeBindAndCheckDiagnostics(sourceFile *ast.SourceFile) bool</c>（program.go:856）。</summary>
    private bool CanIncludeBindAndCheckDiagnostics(SourceFile sourceFile)
    {
        if (sourceFile.CheckJsDirective is not null && !sourceFile.CheckJsDirective.Enabled)
        {
            return false;
        }

        if (sourceFile.ScriptKind == ScriptKind.TS || sourceFile.ScriptKind == ScriptKind.TSX)
        {
            return true;
        }

        var isJS = sourceFile.ScriptKind == ScriptKind.JS || sourceFile.ScriptKind == ScriptKind.JSX;
        var isCheckJS = isJS && IsCheckJSEnabledForFile(sourceFile, Options());
        var isPlainJS = IsPlainJSFile(sourceFile, Options().CheckJs);

        // 默认只检查 .ts/.tsx/plain JS/checked JS：
        // - plain JS：没有 // ts-check 且 checkJs 未设置的 .js
        // - check JS：有 // ts-check 或 checkJs: true 的 .js
        return isPlainJS || isCheckJS;
    }

    /// <summary>Go: <c>func (p *Program) getSourceFilesToEmit(targetSourceFiles, forceDtsEmit, forceJsEmit) []*ast.SourceFile</c>（program.go:875）。</summary>
    internal SourceFile[] GetSourceFilesToEmit(SourceFile[]? targetSourceFiles, bool forceDtsEmit, bool forceJsEmit)
    {
        if (targetSourceFiles is null && !forceDtsEmit && !forceJsEmit)
        {
            lock (_sourceFilesToEmitOnceLock)
            {
                if (!_sourceFilesToEmitComputed)
                {
                    _sourceFilesToEmit = Compiler.GetSourceFilesToEmit(this, null, false, false);
                    _sourceFilesToEmitComputed = true;
                }
            }
            return _sourceFilesToEmit!;
        }
        return Compiler.GetSourceFilesToEmit(this, targetSourceFiles, forceDtsEmit, forceJsEmit);
    }
}

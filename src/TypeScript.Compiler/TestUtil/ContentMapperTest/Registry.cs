// ported from: internal/testutil/contentmappertest/registry.go
//
// C# 映射与偏离（逐条）：
// 1) `handlerConstructor`（未导出类型别名）→ `Func<ProjectLifecycle, Ipc.Handler>`（Go 的函数类型 → C# 的
//    Func）。Go 的形参是 `*ProjectLifecycle`（可以为 nil），C# 这边字典里的形参类型不可空，调用点用
//    `lifecycle!` 抑制可空警告（Go 里传 nil 是合法的：只有 dynamicVerbatimHandler 会用这个指针）。
// 2) `mapperHandlers`（未导出 map）→ 静态只读
//    `Dictionary<string, Func<ProjectLifecycle, Ipc.Handler>>`，在静态字段初始化器里建表（Go 的 map 无序，
//    因此顺序无关紧要）。
// 3) `handlerForMapper`（未导出包级函数）→ 同名静态类 `ContentMapperTest` 上的 `HandlerForMapper`
//    （PascalCase）；Go 的 `(ipc.Handler, error)` → 正常返回、失败抛异常（architecture.md §3.2）。
//    Go 的 `fmt.Errorf("… unknown mapper command %v", command)` 打在 `[]string` 上输出 `[a b c]` 形状，
//    这里照抄该形状。
using TypeScript.ContentMapper;

namespace TypeScript.TestUtil.ContentMapperTest;

public static partial class ContentMapperTest
{
    // Go 的导出常量：mapper 命令名（tsconfig 的 contentMapper.exec[0]）。值必须与 Go 逐字一致。
    public const string TransformingMapper = "compiler-test-mapper";
    public const string VerbatimMapper = "verbatim-mapper";
    public const string ModuleVerbatimMapper = "module-verbatim-mapper";
    public const string DynamicVerbatimMapper = "dynamic-verbatim-mapper";
    public const string DiagnosticCodeCollisionMapper = "diagnostic-code-collision-mapper";
    public const string FailingMapper = "failing-mapper";
    public const string SynthesizingMapper = "synthesizing-mapper";
    public const string ComponentMapper = "component-mapper";
    public const string DuplicateMapper = "duplicate-mapper";
    public const string LispMapper = "lisp-mapper";
    public const string SupplementalMapper = "supplemental-mapper";
    public const string SupplementalDiagnosticsMapper = "supplemental-diagnostics-mapper";
    public const string SupplementalGlobalsMapper = "supplemental-globals-mapper";
    public const string SupplementalModuleMapper = "supplemental-module-mapper";
    public const string PrefixedSupplementalMapper = "prefixed-supplemental-mapper";
    public const string UnmappedFoldingMapper = "unmapped-folding-mapper";
    public const string HoistingMapper = "hoisting-mapper";
    public const string DuplicateProjectionMapper = "duplicate-projection-mapper";

    /// <summary>Go: <c>var mapperHandlers = map[string]handlerConstructor{…}</c>。</summary>
    private static readonly Dictionary<string, Func<ProjectLifecycle, Ipc.Handler>> _mapperHandlers = new(StringComparer.Ordinal)
    {
        [TransformingMapper] = static _ => new Handler(),
        [VerbatimMapper] = static _ => new VerbatimHandler(),
        [ModuleVerbatimMapper] = static _ => new ModuleVerbatimHandler(),
        [DynamicVerbatimMapper] = static lifecycle => new DynamicVerbatimHandler(lifecycle),
        [DiagnosticCodeCollisionMapper] = static _ => new DiagnosticCodeCollisionHandler(),
        [FailingMapper] = static _ => new FailingHandler(),
        [SynthesizingMapper] = static _ => new SynthesizingHandler(),
        [ComponentMapper] = static _ => new ComponentHandler(),
        [DuplicateMapper] = static _ => new DuplicateHandler(),
        [LispMapper] = static _ => new LispHandler(),
        [SupplementalMapper] = static _ => new SupplementalHandler(),
        [SupplementalDiagnosticsMapper] = static _ => new SupplementalDiagnosticsHandler(),
        [SupplementalGlobalsMapper] = static _ => new SupplementalGlobalsHandler(),
        [SupplementalModuleMapper] = static _ => new SupplementalModuleHandler(),
        [PrefixedSupplementalMapper] = static _ => new PrefixedSupplementalHandler(),
        [UnmappedFoldingMapper] = static _ => new UnmappedFoldingHandler(),
        [HoistingMapper] = static _ => new HoistingHandler(),
        [DuplicateProjectionMapper] = static _ => new DuplicateProjectionHandler(),
    };

    /// <summary>
    /// Go: <c>func handlerForMapper(command []string, lifecycle *ProjectLifecycle) (ipc.Handler, error)</c>。
    /// 按命令名造出对应的测试 mapper handler（未知命令抛异常）。
    /// </summary>
    public static Ipc.Handler HandlerForMapper(string[] command, ProjectLifecycle? lifecycle)
    {
        if (command.Length == 0)
        {
            throw new Exception("contentmappertest: empty mapper command");
        }

        if (!_mapperHandlers.TryGetValue(command[0], out var constructor))
        {
            throw new Exception($"contentmappertest: unknown mapper command [{string.Join(' ', command)}]");
        }

        return constructor(lifecycle!);
    }
}

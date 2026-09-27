// ported from: internal/project/programcounter.go:1-48
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `sync.Mutex` → **`System.Threading.Lock`**（仓内先例 Compiler/CheckerPool.cs、Checker/Checker.New.cs、Main 仲裁），
//    `mu.Lock()/Unlock()` 落成 `lock (_mu) { … }`：`lock` 语句保证异常路径也退出（与 `defer Unlock` 等价）。
// 2) Go 未导出结构体 `programCounter` → C# `internal sealed class ProgramCounter`（类型名按 C# 习惯命名，
//    见任务规则 2）。Go 未导出字段 → `internal _camelCase`：`_refs`（`mu` 是私有的，只本类用 → `_mu`）。
// 3) `map[*compiler.Program]int32` 是**指针键**（身份相等）→ `Dictionary<Program, int>(ReferenceEqualityComparer.Instance)`
//    （仓库先例：Compiler/CheckerPool.cs 的 `_fileAssociations`）。Go 的 map 零值是 nil（惰性初始化）→ C# 用
//    `Dictionary<…>? _refs` + `??=` 复刻"首次 Ref 时才建表"。
// 4) `panic("program reference count went below zero")` → `Debug.Fail(...)`（抛 DebugFailureException，
//    docs/architecture.md §3.2）；其后不再有代码。
// 5) `len(c.refs)` → `_refs?.Count ?? 0`（Go 里 nil map 的 len 是 0）。
// 6) Go 的 `int32` → C# `int`（Go int32/int 在本仓一律落 `int`；引用计数不可能接近上限，溢出语义无需复刻）。
// 7) `c.refs[program]++` 落成 `_refs[program] = _refs.GetValueOrDefault(program) + 1`：Go 的 map 自增对缺失键
//    先取零值再加，与这里逐分支等价（`GetValueOrDefault` 就是"缺失键 → 零值"）。
namespace TypeScript.Project;

using System.Collections.Generic;
using TypeScript.Compiler;

/// <summary>Go: <c>type programCounter struct</c>（programcounter.go:9-12）：按 Program 身份计数的引用计数器。</summary>
internal sealed class ProgramCounter
{
    // Go: `mu sync.Mutex`。见文件头第 1 条（只本类使用 → private）。
    private readonly Lock _mu = new();

    // Go: `refs map[*compiler.Program]int32`（nil = 空表，见文件头第 3 条）。
    internal Dictionary<Program, int>? _refs;

    /// <summary>Go: <c>func (c *programCounter) Ref(program *compiler.Program)</c>（programcounter.go:16-23）。
    /// 已跟踪的程序自增，未跟踪的程序以 1 加入。</summary>
    internal void Ref(Program program)
    {
        lock (_mu)
        {
            _refs ??= new Dictionary<Program, int>(ReferenceEqualityComparer.Instance);
            _refs[program] = _refs.GetValueOrDefault(program) + 1;
        }
    }

    /// <summary>Go: <c>func (c *programCounter) Deref(program *compiler.Program) bool</c>（programcounter.go:25-40）。
    /// 减到 0 时移除并返回 true；未跟踪时返回 false。</summary>
    internal bool Deref(Program program)
    {
        lock (_mu)
        {
            if (_refs is null || !_refs.TryGetValue(program, out var count))
            {
                return false;
            }

            count--;
            if (count < 0)
            {
                // Go: panic("program reference count went below zero")
                Debug.Fail("program reference count went below zero");
            }

            if (count == 0)
            {
                _refs.Remove(program);
                return true;
            }

            _refs[program] = count;
            return false;
        }
    }

    /// <summary>Go: <c>func (c *programCounter) Len() int</c>（programcounter.go:42-46）。</summary>
    internal int Len()
    {
        lock (_mu)
        {
            return _refs?.Count ?? 0;
        }
    }
}

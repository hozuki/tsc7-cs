// ported from: internal/compiler/checkerpool.go（493 行，全部）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出 struct `checkerPool` 实现导出接口 `CheckerPool` → `internal sealed class CheckerPoolImpl :
//    CheckerPool`（仓库先例：Go `referenceResolver` → `ReferenceResolverImpl`）。
// 2) Go 未导出字段 → `internal _camelCase`：`_program`/`_tracing`/`_checkers`/`_locks`/`_fileAssociations`。
// 3) `createCheckersOnce sync.Once` → "标志 + `System.Threading.Lock` 双重检查"（仓库既有做法，见
//    Checker.C4.AmbientModules.cs 文件头第 1 条 / Checker.C8.SymbolQueries.cs；AOT 下不用
//    `Lazy<T>` 的 `Once` 语义更直观）：`_createCheckersOnceLock` + `_createCheckersOnceDone`。
//    **整个构造体都在锁内**执行，于是语义与 `sync.Once.Do` 一致：并发的第二个调用者会阻塞到第一个跑完，
//    而不是"看到标志就返回"（这正是 sync.Once 的保证）。
// 4) `sync.Mutex` → **`System.Threading.Lock`**（与 `Checker.NewChecker` 返回的 `(Checker, Lock)` 同一类型，
//    Main 仲裁）。Go 的 `mu.Lock()/Unlock()` 落成 `lock (_locks[i]) { … }`：`Lock` 可重入，`lock` 语句
//    保证异常路径也退出（与 `defer Unlock` 等价）——`GetChecker*` 返回的 `func()` 因为要跨调用释放，
//    仍显式用 `Enter()`/`Exit()`。
// 5) `sync.OnceFunc(f)` → `Compiler.OnceFunc(f)`（Interlocked 标志 + 只跑一次），语义同 Go：重复调用只执行
//    第一次（Go 的 `sync.OnceFunc` 是"第一次调用执行，之后什么都不做"，不是"只允许调用一次否则 panic"）。
// 6) `core.NewWorkGroup(singleThreaded)` → `Core.NewWorkGroup(bool)`（Core/Workgroup.cs，接口 `WorkGroup`；
//    Go 的 `wg.Queue(func(){…})` + `wg.RunAndWait()` 逐行对应）。
// 7) Go 的 `map[*ast.SourceFile]*checker.Checker` / `map[*ast.SourceFile]int` 是**指针键**（身份相等）→
//    C# 的 `Dictionary<…>` 走 `ReferenceEqualityComparer.Instance`（仓库先例：ContentMapper/HostImpl.cs、
//    Printer/ChangeTrackerWriter.cs）。**缺失键**：Go 返回零值（nil / 0,false），C# 的索引器会抛，故一律
//    `GetValueOrDefault` / `TryGetValue`。
// 8) `for i := range checkerCount { wg.Queue(func(){ … p.checkers[i] … }) }`：Go 1.22+ 的循环变量每轮新建，
//    C# 的 `for` 变量**被闭包共享** → 每轮先 `var index = i;` 再入队（语义与 Go 一致）。
// 9) `slices.Concat(globalDiagnostics...)` → 逐段 `List.AddRange`；`SortAndDeduplicateDiagnostics` 是
//    program.go 的**包级函数** → `Compiler.SortAndDeduplicateDiagnostics`（与包同名的静态类）。
// 10) `math.Inf(-1)` → `double.NegativeInfinity`；`max/min(a, b, …)` → `Math.Max`/`Math.Min` 嵌套；
//     `clear(slice)` → `Array.Clear`；`sort.Slice` → `Array.Sort(array, comparison)`。
// 11) `float64(x)`/`int(p)` 的显式转换照抄成 C# 强制转换；`edgeCount/2` 是**整数除法**（Go 与 C# 同）。
// 12) `for _, resolvedModules := range …` 里 `resolved == nil`：`ModeAwareCache` 的值可能为 nil（Go 的
//     `*ResolvedModule`），C# 侧同样判空；`GetSourceFileForResolvedModule` 可能返回 nil，Go 的 map 查
//     nil 键得 (0,false)，C# 的 `Dictionary.TryGetValue(null)` 会抛 → 先判 `is null` 再查。
// 13) Go 的 `checker.NewTracer`/`checker.NewChecker`：C# 里 `Checker` 既是命名空间又是类型（`TypeScript.Checker.Checker`），
//     本命名空间内裸写 `Checker` 会命中命名空间段 → 一律全限定 `TypeScript.Checker.Checker` /
//     `TypeScript.Checker.Tracer`（docs/architecture.md §2 的实测坑）。
namespace TypeScript.Compiler;

using System;
using System.Collections.Generic;
using System.Threading;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Tracing;
using static TypeScript.Core.Core;

// CheckerPool is implemented by the project system to provide checkers with
// request-scoped lifetime and reclamation. It returns a checker and a release
// function that must be called when the caller is done with the checker.
// The returned checker must not be accessed concurrently; each acquisition is exclusive.
// If file is non-nil, the pool may use it as an affinity hint to return the same
// checker for the same file across calls.
public interface CheckerPool
{
    (TypeScript.Checker.Checker Checker, Action Done) GetChecker(Context ctx, SourceFile? file);
}

internal sealed class CheckerPoolImpl : CheckerPool
{
    internal Program _program = null!;
    internal Tracing? _tracing;

    // Go: createCheckersOnce sync.Once（见文件头第 3 条）。
    private readonly Lock _createCheckersOnceLock = new();
    private bool _createCheckersOnceDone;

    internal TypeScript.Checker.Checker[] _checkers = [];
    internal Lock[] _locks = [];
    internal Dictionary<SourceFile, TypeScript.Checker.Checker> _fileAssociations = [];

    public (TypeScript.Checker.Checker Checker, Action Done) GetChecker(Context ctx, SourceFile? file)
    {
        if (file is not null)
        {
            return GetCheckerForFileExclusive(ctx, file);
        }

        CreateCheckers();
        var c = _checkers[0];
        _locks[0].Enter();
        return (c, Compiler.OnceFunc(_locks[0].Exit));
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) getCheckerForFileNonExclusive(file *ast.SourceFile) (*checker.Checker, func())</c>。
    /// Returns the checker for the given file without locking. This is only safe when the caller guarantees no
    /// concurrent access to the same checker, e.g. for read-only operations like obtaining an emit resolver.
    /// </summary>
    internal (TypeScript.Checker.Checker Checker, Action Done) GetCheckerForFileNonExclusive(SourceFile file)
    {
        CreateCheckers();
        // Go 的 map 缺失键返回 nil；这里同理返回 nil（Go 侧调用点直接把它当"没有 checker"用）。
        return (_fileAssociations.GetValueOrDefault(file)!, Compiler.Noop);
    }

    internal (TypeScript.Checker.Checker Checker, Action Done) GetCheckerForFileExclusive(Context ctx, SourceFile file)
    {
        CreateCheckers();
        var c = _fileAssociations.GetValueOrDefault(file)!;
        var idx = Array.IndexOf(_checkers, c);
        _locks[idx].Enter();
        return (c, Compiler.OnceFunc(_locks[idx].Exit));
    }

    /// <summary>Go: <c>func (p *checkerPool) getCheckerNonExclusive() (*checker.Checker, func())</c>。</summary>
    internal (TypeScript.Checker.Checker Checker, Action Done) GetCheckerNonExclusive()
    {
        CreateCheckers();
        return (_checkers[0], Compiler.Noop);
    }

    internal void CreateCheckers()
    {
        // Go: p.createCheckersOnce.Do(func() { … })。见文件头第 3 条：整体在锁内 → 与 sync.Once 等效。
        lock (_createCheckersOnceLock)
        {
            if (_createCheckersOnceDone)
            {
                return;
            }

            var checkerCount = _checkers.Length;
            var wg = NewWorkGroup(_program.SingleThreaded());
            for (var i = 0; i < checkerCount; i++)
            {
                // Go 1.22+ 的 `for i := range checkerCount` 每轮一个新变量；C# 的 for 变量被闭包共享 → 复制。
                var index = i;
                wg.Queue(() =>
                {
                    TypeScript.Checker.Tracer? tracer = null;
                    if (_tracing is not null)
                    {
                        tracer = TypeScript.Checker.Tracer.NewTracer(_tracing, index);
                    }

                    var (checker, mu) = TypeScript.Checker.Checker.NewChecker(_program, tracer);
                    _checkers[index] = checker;
                    _locks[index] = mu;
                });
            }

            wg.RunAndWait();

            var files = _program.SourceFiles();
            var associations = new int[files.Length];
            if (checkerCount > 1)
            {
                var baseWeights = new int[files.Length];
                var importCounts = new int[files.Length];
                var isDeclarationFile = new bool[files.Length];
                var totalBaseWeight = 0;
                var declarationBaseWeight = 0;
                for (var i = 0; i < files.Length; i++)
                {
                    var file = files[i];
                    var baseWeight = Compiler.GetCheckerAssociationBaseWeight(file.NodeCount, file.Text().Length);
                    totalBaseWeight += baseWeight;
                    if (file.IsDeclarationFile)
                    {
                        declarationBaseWeight += baseWeight;
                    }

                    baseWeights[i] = baseWeight;
                    importCounts[i] = file.Imports().Length;
                    isDeclarationFile[i] = file.IsDeclarationFile;
                }

                var policy = Compiler.GetCheckerAssociationPolicy(totalBaseWeight, declarationBaseWeight, checkerCount);
                if (policy._sourceFileWeightMultiplier != 1)
                {
                    // Apply this before import normalization. The policy intentionally
                    // increases both source-file work and the normalized import unit.
                    for (var i = 0; i < isDeclarationFile.Length; i++)
                    {
                        if (!isDeclarationFile[i])
                        {
                            baseWeights[i] *= policy._sourceFileWeightMultiplier;
                        }
                    }
                }

                var fileWeights = Compiler.GetCheckerAssociationWeights(baseWeights, importCounts);
                var adjacentFiles = GetImportAdjacency();
                var fileOrder = Compiler.GetCheckerAssociationOrder(fileWeights, isDeclarationFile, policy._prioritizeSourceFiles);
                associations = Compiler.GetCheckerAssociationsInOrder(fileWeights, adjacentFiles, fileOrder, checkerCount, policy._balancePenaltyMultiplier);
            }

            _fileAssociations = new Dictionary<SourceFile, TypeScript.Checker.Checker>(files.Length, ReferenceEqualityComparer.Instance);
            for (var i = 0; i < files.Length; i++)
            {
                _fileAssociations[files[i]] = _checkers[associations[i]];
            }

            _createCheckersOnceDone = true;
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) getImportAdjacency() [][]int</c>。
    /// Returns an undirected import graph represented by file index. A directed import from A to B makes both
    /// files adjacent because either file can benefit from sharing checker caches with the other.
    /// </summary>
    internal int[][] GetImportAdjacency()
    {
        var files = _program.SourceFiles();
        var fileIndices = new Dictionary<SourceFile, int>(files.Length, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < files.Length; i++)
        {
            fileIndices[files[i]] = i;
        }

        var adjacent = new List<int>[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            adjacent[i] = [];
        }

        var resolvedModulesByPath = _program.GetResolvedModules();
        for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
        {
            var file = files[fileIndex];
            // Go: resolvedModules := p.program.resolvedModules[file.Path()]（缺失键 → nil map，零次迭代）
            if (!resolvedModulesByPath.TryGetValue(file.Path(), out var resolvedModules))
            {
                continue;
            }

            foreach (var resolved in resolvedModules.Values)
            {
                if (resolved is null || !resolved.IsResolved())
                {
                    continue;
                }

                var importedFile = _program.GetSourceFileForResolvedModule(resolved.ResolvedFileName);
                if (importedFile is null || !fileIndices.TryGetValue(importedFile, out var importedIndex) || importedIndex == fileIndex)
                {
                    continue;
                }

                adjacent[fileIndex].Add(importedIndex);
                adjacent[importedIndex].Add(fileIndex);
            }
        }

        var adjacentFiles = new int[files.Length][];
        for (var i = 0; i < files.Length; i++)
        {
            adjacentFiles[i] = [.. adjacent[i]];
        }

        return adjacentFiles;
    }

    /// <summary>
    /// Runs <paramref name="cb"/> for each checker in the pool concurrently, locking and unlocking checker
    /// mutexes as it goes, making it safe to call <c>ForEachCheckerParallel</c> from many threads simultaneously.
    /// </summary>
    internal void ForEachCheckerParallel(Action<int, TypeScript.Checker.Checker> cb)
    {
        CreateCheckers();
        var wg = NewWorkGroup(_program.SingleThreaded());
        for (var i = 0; i < _checkers.Length; i++)
        {
            var index = i;
            var checker = _checkers[i];
            wg.Queue(() =>
            {
                lock (_locks[index])
                {
                    cb(index, checker);
                }
            });
        }

        wg.RunAndWait();
    }

    public Diagnostic[] GetGlobalDiagnostics()
    {
        CreateCheckers();
        var globalDiagnostics = new Diagnostic[_checkers.Length][];
        ForEachCheckerParallel((idx, checker) =>
        {
            globalDiagnostics[idx] = checker.GetGlobalDiagnostics();
        });

        // Go: SortAndDeduplicateDiagnostics(slices.Concat(globalDiagnostics...))
        var all = new List<Diagnostic>();
        foreach (var part in globalDiagnostics)
        {
            all.AddRange(part);
        }

        return Compiler.SortAndDeduplicateDiagnostics([.. all]);
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) forEachCheckerGroupDo(ctx context.Context, files []*ast.SourceFile,
    /// singleThreaded bool, cb func(c *checker.Checker, fileIndex int, file *ast.SourceFile))</c>。
    /// Runs one task per checker in parallel. Each task iterates the provided files, processing only those
    /// assigned to its checker. Within each checker's set, files are visited in their original order.
    /// </summary>
    internal void ForEachCheckerGroupDo(Context ctx, SourceFile[] files, bool singleThreaded, Action<TypeScript.Checker.Checker, int, SourceFile> cb)
    {
        CreateCheckers();

        var checkerCount = _checkers.Length;
        var wg = NewWorkGroup(singleThreaded);
        for (var i = 0; i < checkerCount; i++)
        {
            var checkerIdx = i;
            wg.Queue(() =>
            {
                lock (_locks[checkerIdx])
                {
                    for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
                    {
                        var file = files[fileIndex];
                        // Go: p.fileAssociations[file]（缺失键 → nil，比较恒为 false）
                        var checker = _checkers[checkerIdx];
                        if (checker == _fileAssociations.GetValueOrDefault(file))
                        {
                            cb(checker, fileIndex, file);
                        }
                    }
                }
            });
        }

        wg.RunAndWait();
    }
}

/*
Checker association is a balanced graph-partitioning problem:

  - A vertex is a source file.
  - An undirected edge connects two files for each resolved, in-program import
    entry between them. Multiple entries may connect the same pair and therefore
    strengthen their affinity. Self-imports and unresolved or external targets do
    not create edges.
  - A partition is a checker with its own symbol, type, and instantiation caches.

Putting related files on the same checker reduces duplicated cache construction,
but concentrating too many roots on one checker increases the parallel critical
path. We use weighted FENNEL to trade off those objectives:

  affinity(partition) - alpha * incrementalLoadPenalty(partition)

See Tsourakakis et al., "FENNEL: Streaming Graph Partitioning for Massive Scale
Graphs", WSDM 2014: https://doi.org/10.1145/2556195.2556213.

FENNEL is sensitive to stream order. This is both established in the partitioning
literature (for example, Awadelkarim and Ugander, "Prioritized Restreaming
Algorithms for Balanced Graph Partitioning", KDD 2020:
https://arxiv.org/abs/2007.03131) and pronounced in checker workloads because
semantic work is demand-driven. During calibration with four checkers,
degree-first streams produced nearly equal estimated loads but highly unequal
per-checker completion times:

  - MUI docs: approximately 0.6s, 2.8s, 15.3s, and 19.0s.
  - XState: approximately 0.04s, 0.35s, 0.67s, and 1.18s.

Seeded random-order testing also found repeatable slow orders with identical
diagnostics, including about +37% MUI docs and +59% XState compiler Check time
relative to normal order, again with four checkers. Therefore stream order is part
of the policy below, rather than an incidental implementation detail.
*/

/// <summary>Go: <c>type checkerAssociationPolicy struct</c>（包级类型 → 顶层 internal 类）。</summary>
internal sealed class CheckerAssociationPolicy
{
    internal bool _prioritizeSourceFiles;
    internal int _sourceFileWeightMultiplier;
    internal int _balancePenaltyMultiplier;
}

/// <summary>Go 包级声明（package compiler）→ 与包同名的静态类（docs/architecture.md §2）。</summary>
public static partial class Compiler
{
    /*
    The constants below are empirical safety factors for a work proxy that cannot
    observe future semantic cache construction. They were swept across representative
    projects including VS Code, TypeScript, MUI docs, XState, and Bluesky, with 2, 4,
    and 8 checkers:

      - 100-byte text weight divisor: among 25, 50, 75, 80, 90, 100, 110, 125, 150,
        200, and 400, this kept large literals, comments, and generated files from
        appearing artificially cheap without allowing raw byte length to dominate.
        Nearby values occasionally improved one project, but 100 was the most robust
        setting, particularly on VS Code and MUI docs.
      - 4x source-file weight: among 2x, 3x, 4x, 5x, and 8x, this best balanced
        declaration-heavy projects without losing the locality benefit.
      - 16x FENNEL penalty: 1x, 2x, 4x, 8x, 12x, 16x, 20x, 24x, 32x, and 64x were
        sampled across the experiments; 16x was the most robust balance/locality
        compromise for declaration-heavy projects.
      - 12x prioritized-source penalty: 8x, 12x, 16x, 20x, and 24x were compared;
        source-first ordering already spreads expensive roots, and 12x retained more
        locality than the stronger settings.
      - 4-checker cutoff: at 2-3 checkers the tight load cap provides enough balance;
        extra source weighting and penalty pressure regressed some projects.

    These are project-independent operating points, not formulas derived by FENNEL.
    Rebenchmark the vscode, self-compiler, mui-docs, and xstate-main scenarios in the
    TypeScript-benchmarking repository at 2, 4, and 8 checkers before changing them.
    */
    internal const int CheckerAssociationTextWeightDivisor = 100;
    internal const int CheckerAssociationSourceFileWeightMultiplier = 4;
    internal const int CheckerAssociationBalancePenaltyMultiplier = 16;
    internal const int CheckerAssociationPrioritizedSourcePenalty = 12;
    internal const int CheckerAssociationStrongBalanceMinCheckerCount = 4;

    /*
    GetCheckerAssociationPolicy selects one of three calibrated regimes:

    1. Source-dominated, any checker count:
       source files first by descending weight; unmodified source-file weight;
       CheckerAssociationPrioritizedSourcePenalty.
    2. Declaration-heavy, at least CheckerAssociationStrongBalanceMinCheckerCount:
       program order; CheckerAssociationSourceFileWeightMultiplier;
       CheckerAssociationBalancePenaltyMultiplier.
    3. Declaration-heavy, fewer checkers:
       program order; unmodified source-file weight; unscaled adapted FENNEL
       penalty.

    The source-dominated test is evaluated first intentionally: projects with very
    little declaration work benefit from balancing source-file roots directly even
    with a small checker pool.
    */
    internal static CheckerAssociationPolicy GetCheckerAssociationPolicy(int totalWeight, int declarationWeight, int checkerCount)
    {
        if (ShouldPrioritizeSourceFiles(totalWeight, declarationWeight, checkerCount))
        {
            return new CheckerAssociationPolicy
            {
                _prioritizeSourceFiles = true,
                _sourceFileWeightMultiplier = 1,
                _balancePenaltyMultiplier = CheckerAssociationPrioritizedSourcePenalty,
            };
        }

        if (checkerCount >= CheckerAssociationStrongBalanceMinCheckerCount)
        {
            return new CheckerAssociationPolicy
            {
                _sourceFileWeightMultiplier = CheckerAssociationSourceFileWeightMultiplier,
                _balancePenaltyMultiplier = CheckerAssociationBalancePenaltyMultiplier,
            };
        }

        return new CheckerAssociationPolicy
        {
            _sourceFileWeightMultiplier = 1,
            _balancePenaltyMultiplier = 1,
        };
    }

    /// <summary>
    /// Go: <c>func getCheckerAssociationsInOrder(fileWeights []int, adjacentFiles [][]int, fileOrder []int,
    /// checkerCount int, penaltyMultiplier int) []int</c>。
    /// Partitions the import graph using a weighted adaptation of FENNEL's streaming graph-partitioning objective
    /// with gamma = 3/2. Each file is placed where it has the most already-placed neighbors, minus the incremental
    /// convex load penalty. The published alpha = m*sqrt(k)/n^(3/2) becomes m*sqrt(k)/W^(3/2), where W is total
    /// estimated checker work. penaltyMultiplier applies the empirical safety factor selected by
    /// GetCheckerAssociationPolicy.<br/>
    /// A null order means stable program order. The preferred maximum checker weight is the larger of the largest
    /// file and roughly 101% of average. If no checker can accept a file under that bound, the file is assigned to
    /// the least-loaded checker. The 1% slack permits discrete files to pack near the average while preventing
    /// affinity from deliberately creating meaningful estimated imbalance. Ties are deterministic.
    /// </summary>
    internal static int[] GetCheckerAssociationsInOrder(int[] fileWeights, int[][] adjacentFiles, int[]? fileOrder, int checkerCount, int penaltyMultiplier)
    {
        if (fileWeights.Length == 0)
        {
            return [];
        }

        var totalWeight = 0;
        var maxFileWeight = 0;
        var edgeCount = 0;
        for (var i = 0; i < fileWeights.Length; i++)
        {
            var weight = fileWeights[i];
            totalWeight += weight;
            maxFileWeight = Math.Max(maxFileWeight, weight);
            edgeCount += adjacentFiles[i].Length;
        }

        var associations = new int[fileWeights.Length];
        for (var i = 0; i < associations.Length; i++)
        {
            associations[i] = -1;
        }

        var checkerWeights = new int[checkerCount];
        var averageCheckerWeight = (totalWeight + checkerCount - 1) / checkerCount;
        var maxCheckerWeight = Math.Max(maxFileWeight, averageCheckerWeight + averageCheckerWeight / 100);
        var totalWeightFloat = (double)totalWeight;
        var alpha = (double)penaltyMultiplier * (double)(edgeCount / 2) * Math.Sqrt(checkerCount) / (totalWeightFloat * Math.Sqrt(totalWeightFloat));
        var neighborCounts = new int[checkerCount];

        for (var position = 0; position < fileWeights.Length; position++)
        {
            var fileIndex = position;
            if (fileOrder is not null)
            {
                fileIndex = fileOrder[position];
            }

            Array.Clear(neighborCounts);
            foreach (var adjacentFile in adjacentFiles[fileIndex])
            {
                var checkerIndex = associations[adjacentFile];
                if (checkerIndex >= 0)
                {
                    neighborCounts[checkerIndex]++;
                }
            }

            var bestChecker = -1;
            var bestScore = double.NegativeInfinity;
            for (var checkerIndex = 0; checkerIndex < checkerWeights.Length; checkerIndex++)
            {
                var checkerWeight = checkerWeights[checkerIndex];
                if (checkerWeight + fileWeights[fileIndex] > maxCheckerWeight)
                {
                    continue;
                }

                var oldWeight = (double)checkerWeight;
                var newWeight = (double)(checkerWeight + fileWeights[fileIndex]);
                var newPenalty = newWeight * Math.Sqrt(newWeight);
                var oldPenalty = oldWeight * Math.Sqrt(oldWeight);
                var penalty = alpha * (newPenalty - oldPenalty);
                var score = (double)neighborCounts[checkerIndex] - penalty;
                if (score > bestScore || score == bestScore && (bestChecker < 0 || checkerWeight < checkerWeights[bestChecker]))
                {
                    bestChecker = checkerIndex;
                    bestScore = score;
                }
            }

            if (bestChecker < 0)
            {
                bestChecker = 0;
                // Go: for checkerIndex, checkerWeight := range checkerWeights[1:]（Go 的 [1:] 视图 → C# 用下标偏移）
                for (var checkerIndex = 0; checkerIndex < checkerWeights.Length - 1; checkerIndex++)
                {
                    var checkerWeight = checkerWeights[checkerIndex + 1];
                    if (checkerWeight < checkerWeights[bestChecker])
                    {
                        bestChecker = checkerIndex + 1;
                    }
                }
            }

            associations[fileIndex] = bestChecker;
            checkerWeights[bestChecker] += fileWeights[fileIndex];
        }

        return associations;
    }

    /// <summary>
    /// Go: <c>func getCheckerAssociationOrder(fileWeights []int, isDeclarationFile []bool, prioritizeSourceFiles bool) []int</c>。
    /// Places source files before declarations and orders each group by descending estimated work. This exposes
    /// expensive semantic roots early, when all checker loads are still available. Returning null preserves
    /// program order without allocating an index array. Program order is itself a locality choice: it preserves
    /// deterministic groups produced during program construction and was consistently safer for
    /// declaration-heavy projects.
    /// </summary>
    internal static int[]? GetCheckerAssociationOrder(int[] fileWeights, bool[] isDeclarationFile, bool prioritizeSourceFiles)
    {
        if (!prioritizeSourceFiles)
        {
            return null;
        }

        var fileOrder = new int[fileWeights.Length];
        for (var i = 0; i < fileOrder.Length; i++)
        {
            fileOrder[i] = i;
        }

        Array.Sort(fileOrder, (left, right) =>
        {
            if (isDeclarationFile[left] != isDeclarationFile[right])
            {
                return !isDeclarationFile[left] ? -1 : 1;
            }

            if (fileWeights[left] != fileWeights[right])
            {
                return fileWeights[left] > fileWeights[right] ? -1 : 1;
            }

            return left.CompareTo(right);
        });
        return fileOrder;
    }

    internal static int GetCheckerAssociationBaseWeight(int nodeCount, int textLength)
    {
        return Math.Max(nodeCount + textLength / CheckerAssociationTextWeightDivisor, 1);
    }

    /// <summary>
    /// Go: <c>func shouldPrioritizeSourceFiles(totalWeight int, declarationWeight int, checkerCount int) bool</c>。
    /// Reports whether all declaration-file base work is at most half of one average checker load:<br/>
    /// <c>declarationWeight &lt;= totalWeight / (2 * checkerCount)</c><br/>
    /// This threshold separated source-dominated projects such as VS Code from projects where declaration
    /// locality remained important, such as MUI docs, TypeScript, and XState. Delaying at most half a
    /// checker-load of declarations was the stable boundary in the cross-project sweeps.
    /// </summary>
    internal static bool ShouldPrioritizeSourceFiles(int totalWeight, int declarationWeight, int checkerCount)
    {
        return declarationWeight * checkerCount * 2 <= totalWeight;
    }

    /// <summary>
    /// Go: <c>func getCheckerAssociationWeights(baseWeights []int, importCounts []int) []int</c>。
    /// Combines local syntax work with syntactic import fanout. One import unit is totalBaseWeight / totalImports,
    /// so imports collectively contribute approximately the same vertex weight as syntax. Syntactic imports are
    /// deliberately broader than getImportAdjacency's resolved, in-program edges: this term estimates the work of
    /// processing module references, while adjacency controls checker affinity. Normalizing the term avoids a
    /// project-specific vertex-weight constant.
    /// </summary>
    internal static int[] GetCheckerAssociationWeights(int[] baseWeights, int[] importCounts)
    {
        var totalBaseWeight = 0;
        var totalImports = 0;
        for (var i = 0; i < baseWeights.Length; i++)
        {
            totalBaseWeight += baseWeights[i];
            totalImports += importCounts[i];
        }

        var importWeight = 0;
        if (totalImports > 0)
        {
            importWeight = Math.Max(totalBaseWeight / totalImports, 1);
        }

        var fileWeights = new int[baseWeights.Length];
        for (var i = 0; i < baseWeights.Length; i++)
        {
            fileWeights[i] = baseWeights[i] + importCounts[i] * importWeight;
        }

        return fileWeights;
    }

    internal static CheckerPoolImpl NewCheckerPool(Program program)
    {
        return NewCheckerPoolWithTracing(program, null);
    }

    internal static CheckerPoolImpl NewCheckerPoolWithTracing(Program program, Tracing? tr)
    {
        var checkerCount = 4;
        if (program.SingleThreaded())
        {
            checkerCount = 1;
        }
        else if (program.Options().Checkers is { } c)
        {
            checkerCount = c;
        }

        checkerCount = Math.Max(Math.Min(checkerCount, Math.Min(program.SourceFiles().Length, 256)), 1);

        return new CheckerPoolImpl
        {
            _program = program,
            _checkers = new TypeScript.Checker.Checker[checkerCount],
            _locks = new Lock[checkerCount],
            _tracing = tr,
        };
    }

    /// <summary>Go: <c>func noop() {}</c> —— Go 的"什么都不做"的 <c>func()</c> 值。</summary>
    internal static void Noop()
    {
    }

    /// <summary>
    /// Go: <c>sync.OnceFunc(f func()) func()</c>：返回一个函数，**第一次**调用执行 <paramref name="f"/>，
    /// 之后什么都不做（并发安全；这正是 Go 的 OnceFunc 语义，不是"第二次调用 panic"）。
    /// </summary>
    internal static Action OnceFunc(Action f)
    {
        var called = 0;
        return () =>
        {
            if (Interlocked.Exchange(ref called, 1) == 0)
            {
                f();
            }
        };
    }
}

// ported from: internal/execute/tsc/statistics.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) 本命名空间里有名为 `System` 的接口，会遮蔽根命名空间 `System` → `using global::System;` + 禁止 `System.` 前缀。
// 2) `io.Writer` → `System.IO.TextWriter`；`fmt.Fprintf` 的 `%-*s`/`%*s` → `string.Format` 的对齐格式（宽度按
//    `string.Length`，即 UTF-16 代码单元；Go 的 `len()` 是字节 —— 表头/表项都是 ASCII 时两者相同）。
// 3) `runtime.MemStats` 的 `Alloc`/`Mallocs` 在 C# 里没有等价物 → 本文件定义只含这两个字段的 `MemStats`，
//    EmitAndReportStatistics 用 `GC.GetTotalMemory(false)`/`GC.GetTotalAllocatedBytes()` 填充（**语义偏离**：
//    Go 的 `Mallocs` 是累计**分配次数**，C# 只能拿到累计**分配字节数**；这行数字会与 Go 输出不同）。
// 4) `map[K]V` 的有序遍历用 `OrderBy(StringComparer.Ordinal)`（Go 的 `slices.Sorted(maps.Keys(...))` 也是序数序）。
// 5) 未导出的 `table`/`tableRow` → `Table`/`TableRow`；未导出方法 → internal + PascalCase。
// 6) `fmt.Sprint(value)` → `Convert.ToString(value, CultureInfo.InvariantCulture)`（避免跟随当前区域的数字形状）。
using global::System;
using global::System.Collections.Generic;
using global::System.Globalization;
using global::System.IO;
using global::System.Linq;
using TypeScript.Compiler;

namespace TypeScript.Execute.Tsc;

/// <summary>Go: <c>runtime.MemStats</c> 里本包用到的两个字段（见文件头偏离 3）。</summary>
internal struct MemStats
{
    /// <summary>Go: <c>MemStats.Alloc</c>（最近一次 GC 后的存活字节数）→ <c>GC.GetTotalMemory(false)</c>。</summary>
    public ulong Alloc;

    /// <summary>Go: <c>MemStats.Mallocs</c>（累计分配次数）→ <c>GC.GetTotalAllocatedBytes()</c>（累计分配字节数）。</summary>
    public ulong Mallocs;
}

/// <summary>Go: <c>type tableRow struct</c>（statistics.go:15-18）。</summary>
internal readonly struct TableRow(string name, string value)
{
    internal readonly string Name = name;
    internal readonly string Value = value;
}

/// <summary>Go: <c>type table struct</c>（statistics.go:20-42）。</summary>
internal sealed class Table
{
    private readonly List<TableRow> _rows = [];

    /// <summary>Go: <c>func (t *table) add(name string, value any)</c>。</summary>
    internal void Add(string name, object? value)
    {
        if (value is TimeSpan d)
        {
            value = Tsc.FormatDuration(d);
        }
        _rows.Add(new TableRow(name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
    }

    /// <summary>Go: <c>func (t *table) print(w io.Writer)</c>。</summary>
    internal void Print(TextWriter w)
    {
        var nameWidth = 0;
        var valueWidth = 0;
        foreach (var r in _rows)
        {
            nameWidth = Math.Max(nameWidth, r.Name.Length);
            valueWidth = Math.Max(valueWidth, r.Value.Length);
        }

        foreach (var r in _rows)
        {
            w.Write(string.Format(
                CultureInfo.InvariantCulture,
                $"{{0,-{nameWidth + 1}}} {{1,{valueWidth}}}\n",
                r.Name + ":",
                r.Value));
        }
    }
}

public static partial class Tsc
{
    /// <summary>Go: <c>func formatDuration(d time.Duration) string</c>（statistics.go:44-46）。</summary>
    internal static string FormatDuration(TimeSpan d) => d.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "s";

    /// <summary>Go: <c>func identifierCount(p *compiler.Program) int</c>（statistics.go:48-54）。</summary>
    internal static int IdentifierCount(Program p)
    {
        var count = 0;
        foreach (var file in p.SourceFiles())
        {
            count += file.IdentifierCount;
        }
        return count;
    }

    /// <summary>Go: <c>func statisticsFromProgram(input EmitInput, memStats *runtime.MemStats) *Statistics</c>（statistics.go:72-84）。</summary>
    internal static Statistics StatisticsFromProgram(EmitInput input, MemStats memStats) => new()
    {
        _files = input.Program.SourceFiles().Length,
        _lines = input.Program.LineCount(),
        _identifiers = input.Program.IdentifierCount(),
        _symbols = input.Program.SymbolCount(),
        _types = input.Program.TypeCount(),
        _instantiations = input.Program.InstantiationCount(),
        _memoryUsed = memStats.Alloc,
        _memoryAllocs = memStats.Mallocs,
        _compileTimes = input.CompileTimes,
    };
}

/// <summary>Go: <c>type Statistics struct</c>（statistics.go:56-70）。</summary>
public sealed class Statistics
{
    internal bool _isAggregate;

    /// <summary>Go: <c>Projects int</c>。</summary>
    public int Projects;

    /// <summary>Go: <c>ProjectsBuilt int</c>。</summary>
    public int ProjectsBuilt;

    /// <summary>Go: <c>TimestampUpdates int</c>。</summary>
    public int TimestampUpdates;

    internal int _files;
    internal int _lines;
    internal int _identifiers;
    internal int _symbols;
    internal int _types;
    internal int _instantiations;
    internal ulong _memoryUsed;
    internal ulong _memoryAllocs;
    internal CompileTimes? _compileTimes;

    /// <summary>Go: <c>func (s *Statistics) Report(w io.Writer, testing CommandLineTesting)</c>（statistics.go:86-130）。</summary>
    public void Report(TextWriter w, CommandLineTesting? testing)
    {
        if (testing is not null)
        {
            testing.OnStatisticsStart(w);
        }
        try
        {
            var table = new Table();
            var prefix = "";

            if (_isAggregate)
            {
                prefix = "Aggregate ";
                table.Add("Projects in scope", Projects);
                table.Add("Projects built", ProjectsBuilt);
                table.Add("Timestamps only updates", TimestampUpdates);
            }
            table.Add(prefix + "Files", _files);
            table.Add(prefix + "Lines", _lines);
            table.Add(prefix + "Identifiers", _identifiers);
            table.Add(prefix + "Symbols", _symbols);
            table.Add(prefix + "Types", _types);
            table.Add(prefix + "Instantiations", _instantiations);
            table.Add(prefix + "Memory used", $"{_memoryUsed / 1024}K");
            table.Add(prefix + "Memory allocs", _memoryAllocs.ToString(CultureInfo.InvariantCulture));
            if (_compileTimes!.ConfigTime != default)
            {
                table.Add(prefix + "Config time", _compileTimes.ConfigTime);
            }
            if (_compileTimes.BuildInfoReadTime != default)
            {
                table.Add(prefix + "BuildInfo read time", _compileTimes.BuildInfoReadTime);
            }
            table.Add(prefix + "Parse time", _compileTimes.ParseTime);
            if (_compileTimes._bindTime != default)
            {
                table.Add(prefix + "Bind time", _compileTimes._bindTime);
            }
            if (_compileTimes._checkTime != default)
            {
                table.Add(prefix + "Check time", _compileTimes._checkTime);
            }
            if (_compileTimes._emitTime != default)
            {
                table.Add(prefix + "Emit time", _compileTimes._emitTime);
            }
            if (_compileTimes.ChangesComputeTime != default)
            {
                table.Add(prefix + "Changes compute time", _compileTimes.ChangesComputeTime);
            }
            AddContentMapperStatistics(table, prefix);
            table.Add(prefix + "Total time", _compileTimes._totalTime);
            table.Print(w);
        }
        finally
        {
            testing?.OnStatisticsEnd(w);
        }
    }

    /// <summary>Go: <c>func (s *Statistics) addContentMapperStatistics(table *table, prefix string)</c>（statistics.go:132-153）。</summary>
    internal void AddContentMapperStatistics(Table table, string prefix)
    {
        var timings = _compileTimes!.ContentMapperTimes;
        if (timings.RequestWait != default)
        {
            table.Add(prefix + "Content mapper request wait time", timings.RequestWait);
        }
        foreach (var identity in timings.Mappers.Keys.OrderBy(static k => k, StringComparer.Ordinal))
        {
            var mapper = timings.Mappers[identity];
            var initializationCount = mapper.Spawn.Count;
            if (initializationCount != 0)
            {
                table.Add(prefix + identity + " initialization time", mapper.Spawn.Duration + mapper.Initialize.Duration);
            }
            if (mapper.Transform.Count != 0)
            {
                table.Add(prefix + identity + " transform time", mapper.Transform.Duration);
            }
            if (mapper.OpenProject.Count != 0)
            {
                table.Add(prefix + identity + " openProject time", mapper.OpenProject.Duration);
            }
            if (mapper.CloseProject.Count != 0)
            {
                table.Add(prefix + identity + " closeProject time", mapper.CloseProject.Duration);
            }
        }
    }

    /// <summary>Go: <c>func (s *Statistics) Aggregate(stat *Statistics)</c>（statistics.go:155-176）。</summary>
    public void Aggregate(Statistics stat)
    {
        _isAggregate = true;
        _compileTimes ??= new CompileTimes();
        // Aggregate statistics
        _files += stat._files;
        _lines += stat._lines;
        _identifiers += stat._identifiers;
        _symbols += stat._symbols;
        _types += stat._types;
        _instantiations += stat._instantiations;
        _memoryUsed += stat._memoryUsed;
        _memoryAllocs += stat._memoryAllocs;
        _compileTimes.ConfigTime += stat._compileTimes!.ConfigTime;
        _compileTimes.BuildInfoReadTime += stat._compileTimes.BuildInfoReadTime;
        _compileTimes.ParseTime += stat._compileTimes.ParseTime;
        _compileTimes._bindTime += stat._compileTimes._bindTime;
        _compileTimes._checkTime += stat._compileTimes._checkTime;
        _compileTimes._emitTime += stat._compileTimes._emitTime;
        _compileTimes.ChangesComputeTime += stat._compileTimes.ChangesComputeTime;
    }

    /// <summary>Go: <c>func (s *Statistics) SetTotalTime(totalTime time.Duration)</c>（statistics.go:178-183）。</summary>
    public void SetTotalTime(TimeSpan totalTime)
    {
        _compileTimes ??= new CompileTimes();
        _compileTimes._totalTime = totalTime;
    }
}

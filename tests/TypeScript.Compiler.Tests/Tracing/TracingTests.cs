// ported from: internal/tracing/tracing_test.go
//
// 与 Go 测试的差异（逐条）：
// - Go 用 `vfstest.FromMap(fstest.MapFS{...})` 造内存 FS；vfs 切片还没有 `vfstest`，所以这里写一个最小的
//   内存 FS（`MemoryFs`），只实现 tracing 需要的 WriteFile/AppendFile/TryReadFile，其余成员按"未使用"实现。
// - `traceEvent` 在 C# 里是 internal 类（Go 里未导出）→ 测试程序集通过 InternalsVisibleTo 访问（项目级设置）。
// - trace.json 的解析用 `Decoder` + `DecodeInto`（TraceEvent 实现 IJsonUnmarshaler），逐事件读，不用反射。
// - `assert.Assert(t, cond, msg)` → xunit 的 `Assert.True(cond, msg)`；`assert.NilError` → 直接调用（C# 抛异常即失败）。
// - 另加 XXH3 的向量测试（Go 侧是外部依赖 zeebo/xxh3，没有对应 *_test.go；期望值取自 TSGO 已提交的
//   generateTrace baseline，见 src/TypeScript.Compiler/Xxh3/Xxh3.cs 文件头）。
using System.Text;
using JsonDecoder = TypeScript.Json.Decoder;
using TypeScript.Json;
using TypeScript.Tracing;
using static TypeScript.Tracing.Tracing;
using TypeScript.Vfs;
using Xunit;

namespace TypeScript.Tests;

public class TracingTests
{
    [Fact]
    public void ConcurrentDurationEventsUseSeparateThreadIDs()
    {
        var fs = new MemoryFs();
        var tr = StartTracing(fs, "/trace", "", true /*deterministic*/);

        var endA = tr.Push(Phase.Parse, "createSourceFile", new Dictionary<string, object?> { ["path"] = "/a.ts" }, true);
        var endB = tr.Push(Phase.Parse, "createSourceFile", new Dictionary<string, object?> { ["path"] = "/b.ts" }, true);
        endA();
        endB();

        var endCheck = tr.Push(Phase.Check, "checkSourceFile", new Dictionary<string, object?> { ["checkerId"] = 0, ["path"] = "/a.ts" }, true);
        var endVariance = tr.Push(Phase.CheckTypes, "getVariancesWorker", new Dictionary<string, object?> { ["checkerId"] = 0, ["id"] = 1 }, true);
        endVariance();
        endCheck();

        tr.StopTracing();

        var events = ParseTraceEvents(fs.ReadAllText("/trace/trace.json"));

        var aBegin = FindEvent(events, "B", "createSourceFile", "path", "/a.ts");
        var aEnd = FindEvent(events, "E", "createSourceFile", "path", "/a.ts");
        var bBegin = FindEvent(events, "B", "createSourceFile", "path", "/b.ts");
        var bEnd = FindEvent(events, "E", "createSourceFile", "path", "/b.ts");
        Assert.Equal(aBegin.TID, aEnd.TID);
        Assert.Equal(bBegin.TID, bEnd.TID);
        Assert.True(aBegin.TID != bBegin.TID, "different files must use different thread ids");
        AssertThreadName(events, aBegin.TID, "file:/a.ts");
        AssertThreadName(events, bBegin.TID, "file:/b.ts");

        var checkBegin = FindEvent(events, "B", "checkSourceFile", "path", "/a.ts");
        var varianceBegin = FindEvent(events, "B", "getVariancesWorker", "id", 1d);
        Assert.Equal(checkBegin.TID, varianceBegin.TID);
        AssertThreadName(events, checkBegin.TID, "checker:0");

        AssertDurationEventsAreWellNestedByThread(events);
    }

    [Fact]
    public void ThreadIDsAreStableAcrossFirstSeenOrder()
    {
        var first = TraceThreadIDsForPaths(["/a.ts", "/b.ts"]);
        var second = TraceThreadIDsForPaths(["/b.ts", "/a.ts"]);

        Assert.Equal(first.Count, second.Count);
        foreach (var (path, tid) in first)
        {
            Assert.Equal(tid, second[path]);
        }
    }

    [Theory]
    // 期望值 = TSGO baseline 的 `"tid"` 减 firstFileThreadID(1_000_000) = XXH3-64 的 `% 1_000_000_000`。
    [InlineData("file:/home/src/tslibs/TS/Lib/lib.es2025.full.d.ts", 2208031706851581467UL)]
    [InlineData("file:/home/src/workspaces/project/a.ts", 9197916062353130385UL)]
    [InlineData("file:/home/src/workspaces/project/main.ts", 7876163279099209852UL)]
    [InlineData("file:/home/src/workspaces/project/types.ts", 128579712552096334UL)]
    public void Xxh3MatchesCommittedBaselineThreadIds(string key, ulong expectedHash)
    {
        Assert.Equal(expectedHash, Xxh3.Xxh3.Hash64(Encoding.UTF8.GetBytes(key)));
    }

    private static Dictionary<string, int> TraceThreadIDsForPaths(string[] paths)
    {
        var fs = new MemoryFs();
        var tr = StartTracing(fs, "/trace", "", true /*deterministic*/);

        foreach (var path in paths)
        {
            var end = tr.Push(Phase.Parse, "createSourceFile", new Dictionary<string, object?> { ["path"] = path }, true);
            end();
        }

        tr.StopTracing();

        var events = ParseTraceEvents(fs.ReadAllText("/trace/trace.json"));

        var threadIDs = new Dictionary<string, int>();
        foreach (var path in paths)
        {
            threadIDs[path] = FindEvent(events, "B", "createSourceFile", "path", path).TID;
        }

        return threadIDs;
    }

    private static List<TraceEvent> ParseTraceEvents(string traceText)
    {
        var events = new List<TraceEvent>();
        var decoder = new JsonDecoder(Encoding.UTF8.GetBytes(traceText));
        Assert.Equal(JsonKind.BeginArray, decoder.ReadToken().Kind);
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            var @event = new TraceEvent();
            decoder.DecodeInto(@event);
            events.Add(@event);
        }

        return events;
    }

    private static TraceEvent FindEvent(List<TraceEvent> events, string phase, string name, string argName, object argValue)
    {
        foreach (var @event in events)
        {
            if (@event.PH == phase && @event.Name == name &&
                @event.Args is not null && @event.Args.TryGetValue(argName, out var value) &&
                Equals(value, argValue))
            {
                return @event;
            }
        }

        throw new Xunit.Sdk.XunitException($"failed to find {phase} event {name} with {argName}={argValue}");
    }

    private static void AssertThreadName(List<TraceEvent> events, int tid, string name)
    {
        foreach (var @event in events)
        {
            if (@event.PH == "M" && @event.Name == "thread_name" && @event.TID == tid &&
                @event.Args is not null && @event.Args.TryGetValue("name", out var value) && Equals(value, name))
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException($"failed to find thread_name metadata for thread {tid} named {name}");
    }

    private static void AssertDurationEventsAreWellNestedByThread(List<TraceEvent> events)
    {
        var stacks = new Dictionary<int, List<TraceEvent>>();
        foreach (var @event in events)
        {
            switch (@event.PH)
            {
                case "B":
                    if (!stacks.TryGetValue(@event.TID, out var beginStack))
                    {
                        beginStack = [];
                        stacks[@event.TID] = beginStack;
                    }

                    beginStack.Add(@event);
                    break;
                case "E":
                    Assert.True(stacks.TryGetValue(@event.TID, out var endStack) && endStack.Count > 0,
                        $"unmatched end event { @event.Name } on thread { @event.TID }");
                    var begin = endStack[^1];
                    Assert.Equal(begin.Cat, @event.Cat);
                    Assert.Equal(begin.Name, @event.Name);
                    endStack.RemoveAt(endStack.Count - 1);
                    break;
            }
        }

        foreach (var (tid, stack) in stacks)
        {
            Assert.True(stack.Count == 0, $"thread {tid} has {stack.Count} unterminated events");
        }
    }

    /// <summary>
    /// Go 测试里的 `vfstest.FromMap`（内存 FS）的最小替代：tracing 只用 WriteFile/AppendFile/TryReadFile。
    /// </summary>
    private sealed class MemoryFs : FS
    {
        private readonly Dictionary<string, string> _files = [];

        public string ReadAllText(string path) => _files.TryGetValue(path, out var text)
            ? text
            : throw new Xunit.Sdk.XunitException($"no such file: {path}");

        public bool UseCaseSensitiveFileNames() => true;

        public bool FileExists(string path) => _files.ContainsKey(path);

        public bool TryReadFile(string path, out string contents) => _files.TryGetValue(path, out contents!);

        public void WriteFile(string path, string data) => _files[path] = data;

        public void AppendFile(string path, string data) =>
            _files[path] = _files.TryGetValue(path, out var existing) ? existing + data : data;

        public void Remove(string path) => _files.Remove(path);

        public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
        {
        }

        public bool DirectoryExists(string path) => _files.Keys.Any(f => f.StartsWith(path, StringComparison.Ordinal));

        public Entries GetAccessibleEntries(string path) => new();

        public TypeScript.Vfs.FileInfo? Stat(string path) => _files.ContainsKey(path)
            ? new TypeScript.Vfs.FileInfo { Name = System.IO.Path.GetFileName(path), FullName = path, Size = _files[path].Length }
            : null;

        public string Realpath(string path) => path;
    }
}

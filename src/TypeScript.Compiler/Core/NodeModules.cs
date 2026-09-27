// ported from: internal/core/nodemodules.go
//
// 偏离 Go 的地方：
// 1) Go 的 `var NodeCoreModules = sync.OnceValue(func() map[string]bool {…})`（首次调用时求值一次）→ C# 的
//    static readonly 字段 + 静态初始化函数：CLR 保证静态字段初始化只执行一次且线程安全。差异只在求值时机
//    （首次触碰 Core 类型 vs 首次调用 NodeCoreModules()），可观察结果相同（内容只读、不依赖外部状态）。
// 2) Go 的 `map[string]bool` → `Dictionary<string, bool>`（保留 Go 的形状；查值处也照抄
//    `NodeCoreModules()[moduleName]` 的“取值为真”语义）。
// 3) Go 的 `NodeCoreModules()` 是函数调用，C# 是字段：调用点写 `Core.NodeCoreModules[key]`。
namespace TypeScript.Core;

public static partial class Core
{
    // require('module').builtinModules.filter(x => !x.match(/^(?:_|node:)/))
    public static readonly Dictionary<string, bool> UnprefixedNodeCoreModules = new()
    {
        { "assert", true },
        { "assert/strict", true },
        { "async_hooks", true },
        { "buffer", true },
        { "child_process", true },
        { "cluster", true },
        { "console", true },
        { "constants", true },
        { "crypto", true },
        { "dgram", true },
        { "diagnostics_channel", true },
        { "dns", true },
        { "dns/promises", true },
        { "domain", true },
        { "events", true },
        { "fs", true },
        { "fs/promises", true },
        { "http", true },
        { "http2", true },
        { "https", true },
        { "inspector", true },
        { "inspector/promises", true },
        { "module", true },
        { "net", true },
        { "os", true },
        { "path", true },
        { "path/posix", true },
        { "path/win32", true },
        { "perf_hooks", true },
        { "process", true },
        { "punycode", true },
        { "querystring", true },
        { "readline", true },
        { "readline/promises", true },
        { "repl", true },
        { "stream", true },
        { "stream/consumers", true },
        { "stream/promises", true },
        { "stream/web", true },
        { "string_decoder", true },
        { "sys", true },
        { "timers", true },
        { "timers/promises", true },
        { "tls", true },
        { "trace_events", true },
        { "tty", true },
        { "url", true },
        { "util", true },
        { "util/types", true },
        { "v8", true },
        { "vm", true },
        { "wasi", true },
        { "worker_threads", true },
        { "zlib", true },
    };

    // require('module').builtinModules.filter(x => x.startsWith('node:'))
    public static readonly Dictionary<string, bool> ExclusivelyPrefixedNodeCoreModules = new()
    {
        { "node:quic", true },
        { "node:sea", true },
        { "node:sqlite", true },
        { "node:test", true },
        { "node:test/reporters", true },
    };

    // Go: var NodeCoreModules = sync.OnceValue(func() map[string]bool { … })
    public static readonly Dictionary<string, bool> NodeCoreModules = BuildNodeCoreModules();

    private static Dictionary<string, bool> BuildNodeCoreModules()
    {
        var nodeCoreModules = new Dictionary<string, bool>(UnprefixedNodeCoreModules.Count * 2 + ExclusivelyPrefixedNodeCoreModules.Count);
        foreach (var unprefixed in UnprefixedNodeCoreModules.Keys)
        {
            nodeCoreModules[unprefixed] = true;
            nodeCoreModules["node:" + unprefixed] = true;
        }
        // Go: maps.Copy(nodeCoreModules, ExclusivelyPrefixedNodeCoreModules)
        foreach (var entry in ExclusivelyPrefixedNodeCoreModules)
        {
            nodeCoreModules[entry.Key] = entry.Value;
        }
        return nodeCoreModules;
    }

    public static string NonRelativeModuleNameForTypingCache(string moduleName)
    {
        // Go: if NodeCoreModules()[moduleName] { return "node" }
        if (NodeCoreModules.TryGetValue(moduleName, out var isNodeCoreModule) && isNodeCoreModule)
        {
            return "node";
        }
        return moduleName;
    }
}

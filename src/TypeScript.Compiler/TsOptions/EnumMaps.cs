// ported from: internal/tsoptions/enummaps.go
//
// 映射约定（docs/architecture.md §2/§3）：
// 1) Go 的 `collections.NewOrderedMapFromList([]MapEntry[string, any]{...})` → `NewOrderedMapFromList<string, object?>(...)`；
//    Go 的 `map[core.ScriptTarget]string` → `Dictionary<ScriptTarget, string>`（顺序不敏感，Go 侧同样从未依赖顺序）。
// 2) Go 的无类型常量字符串 key 原样保留（`"es5"`、`"react-native"` …）；值是 **装箱后的枚举**（与 Go 的 `any` 持有
//    `core.ScriptTarget` 等一致），调用点（`validateJsonOptionValue` / `convertJsonOptionOfEnumType`）按类型断言取回。
// 3) 包级 var 一律落在 `public static partial class TsOptions`（§2），非导出的 var 是 `internal static readonly`
//    的 `_camelCase` 字段（.editorconfig 的命名规则）。凡是初始化依赖**别的 partial 文件**里的成员的，都用
//    `Lazy<T>` 包一层：C# 的 partial 类型在不同文件里的字段初始化顺序是**未定义**的（`LibMap` 之类跨文件引用
//    会在 `CommandLineOption.cs` 的 `_commandLineOptionEnumMap` 里出现，见那里的注释）。
// 4) `GetLibFileName(libName) (string, bool)` → `TryGetLibFileName(string, out string)`（§3.2）。
// 5) `slices.Collect(OrderedMap.Keys())` → 数组（`[.. LibMap.Keys()]`，C# 无 Go 的迭代器惰性语义需求）。
using TypeScript.Collections;
using TypeScript.Core;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    public static readonly OrderedMap<string, object?> LibMap = NewOrderedMapFromList<string, object?>(
    [
        // JavaScript only
        new("es5", "lib.es5.d.ts"),
        new("es6", "lib.es2015.d.ts"),
        new("es2015", "lib.es2015.d.ts"),
        new("es7", "lib.es2016.d.ts"),
        new("es2016", "lib.es2016.d.ts"),
        new("es2017", "lib.es2017.d.ts"),
        new("es2018", "lib.es2018.d.ts"),
        new("es2019", "lib.es2019.d.ts"),
        new("es2020", "lib.es2020.d.ts"),
        new("es2021", "lib.es2021.d.ts"),
        new("es2022", "lib.es2022.d.ts"),
        new("es2023", "lib.es2023.d.ts"),
        new("es2024", "lib.es2024.d.ts"),
        new("es2025", "lib.es2025.d.ts"),
        new("esnext", "lib.esnext.d.ts"),
        // Host only
        new("dom", "lib.dom.d.ts"),
        new("dom.iterable", "lib.dom.iterable.d.ts"),
        new("dom.asynciterable", "lib.dom.asynciterable.d.ts"),
        new("webworker", "lib.webworker.d.ts"),
        new("webworker.importscripts", "lib.webworker.importscripts.d.ts"),
        new("webworker.iterable", "lib.webworker.iterable.d.ts"),
        new("webworker.asynciterable", "lib.webworker.asynciterable.d.ts"),
        new("scripthost", "lib.scripthost.d.ts"),
        // ES2015 and later By-feature options
        new("es2015.core", "lib.es2015.core.d.ts"),
        new("es2015.collection", "lib.es2015.collection.d.ts"),
        new("es2015.generator", "lib.es2015.generator.d.ts"),
        new("es2015.iterable", "lib.es2015.iterable.d.ts"),
        new("es2015.promise", "lib.es2015.promise.d.ts"),
        new("es2015.proxy", "lib.es2015.proxy.d.ts"),
        new("es2015.reflect", "lib.es2015.reflect.d.ts"),
        new("es2015.symbol", "lib.es2015.symbol.d.ts"),
        new("es2015.symbol.wellknown", "lib.es2015.symbol.wellknown.d.ts"),
        new("es2016.array.include", "lib.es2016.array.include.d.ts"),
        new("es2016.intl", "lib.es2016.intl.d.ts"),
        new("es2017.arraybuffer", "lib.es2017.arraybuffer.d.ts"),
        new("es2017.date", "lib.es2017.date.d.ts"),
        new("es2017.object", "lib.es2017.object.d.ts"),
        new("es2017.sharedmemory", "lib.es2017.sharedmemory.d.ts"),
        new("es2017.string", "lib.es2017.string.d.ts"),
        new("es2017.intl", "lib.es2017.intl.d.ts"),
        new("es2017.typedarrays", "lib.es2017.typedarrays.d.ts"),
        new("es2018.asyncgenerator", "lib.es2018.asyncgenerator.d.ts"),
        new("es2018.asynciterable", "lib.es2018.asynciterable.d.ts"),
        new("es2018.intl", "lib.es2018.intl.d.ts"),
        new("es2018.promise", "lib.es2018.promise.d.ts"),
        new("es2018.regexp", "lib.es2018.regexp.d.ts"),
        new("es2019.array", "lib.es2019.array.d.ts"),
        new("es2019.object", "lib.es2019.object.d.ts"),
        new("es2019.string", "lib.es2019.string.d.ts"),
        new("es2019.symbol", "lib.es2019.symbol.d.ts"),
        new("es2019.intl", "lib.es2019.intl.d.ts"),
        new("es2020.bigint", "lib.es2020.bigint.d.ts"),
        new("es2020.date", "lib.es2020.date.d.ts"),
        new("es2020.promise", "lib.es2020.promise.d.ts"),
        new("es2020.sharedmemory", "lib.es2020.sharedmemory.d.ts"),
        new("es2020.string", "lib.es2020.string.d.ts"),
        new("es2020.symbol.wellknown", "lib.es2020.symbol.wellknown.d.ts"),
        new("es2020.intl", "lib.es2020.intl.d.ts"),
        new("es2020.number", "lib.es2020.number.d.ts"),
        new("es2021.promise", "lib.es2021.promise.d.ts"),
        new("es2021.string", "lib.es2021.string.d.ts"),
        new("es2021.weakref", "lib.es2021.weakref.d.ts"),
        new("es2021.intl", "lib.es2021.intl.d.ts"),
        new("es2022.array", "lib.es2022.array.d.ts"),
        new("es2022.error", "lib.es2022.error.d.ts"),
        new("es2022.intl", "lib.es2022.intl.d.ts"),
        new("es2022.object", "lib.es2022.object.d.ts"),
        new("es2022.string", "lib.es2022.string.d.ts"),
        new("es2022.regexp", "lib.es2022.regexp.d.ts"),
        new("es2023.array", "lib.es2023.array.d.ts"),
        new("es2023.collection", "lib.es2023.collection.d.ts"),
        new("es2023.intl", "lib.es2023.intl.d.ts"),
        new("es2024.arraybuffer", "lib.es2024.arraybuffer.d.ts"),
        new("es2024.collection", "lib.es2024.collection.d.ts"),
        new("es2024.object", "lib.es2024.object.d.ts"),
        new("es2024.promise", "lib.es2024.promise.d.ts"),
        new("es2024.regexp", "lib.es2024.regexp.d.ts"),
        new("es2024.sharedmemory", "lib.es2024.sharedmemory.d.ts"),
        new("es2024.string", "lib.es2024.string.d.ts"),
        new("es2025.collection", "lib.es2025.collection.d.ts"),
        new("es2025.float16", "lib.es2025.float16.d.ts"),
        new("es2025.intl", "lib.es2025.intl.d.ts"),
        new("es2025.iterator", "lib.es2025.iterator.d.ts"),
        new("es2025.promise", "lib.es2025.promise.d.ts"),
        new("es2025.regexp", "lib.es2025.regexp.d.ts"),
        // Fallback for backward compatibility
        new("esnext.asynciterable", "lib.es2018.asynciterable.d.ts"),
        new("esnext.symbol", "lib.es2019.symbol.d.ts"),
        new("esnext.bigint", "lib.es2020.bigint.d.ts"),
        new("esnext.weakref", "lib.es2021.weakref.d.ts"),
        new("esnext.object", "lib.es2024.object.d.ts"),
        new("esnext.regexp", "lib.es2024.regexp.d.ts"),
        new("esnext.string", "lib.es2024.string.d.ts"),
        new("esnext.float16", "lib.es2025.float16.d.ts"),
        new("esnext.iterator", "lib.es2025.iterator.d.ts"),
        new("esnext.promise", "lib.es2025.promise.d.ts"),
        // ESNext By-feature options
        new("esnext.array", "lib.esnext.array.d.ts"),
        new("esnext.collection", "lib.esnext.collection.d.ts"),
        new("esnext.date", "lib.esnext.date.d.ts"),
        new("esnext.decorators", "lib.esnext.decorators.d.ts"),
        new("esnext.disposable", "lib.esnext.disposable.d.ts"),
        new("esnext.error", "lib.esnext.error.d.ts"),
        new("esnext.intl", "lib.esnext.intl.d.ts"),
        new("esnext.sharedmemory", "lib.esnext.sharedmemory.d.ts"),
        new("esnext.temporal", "lib.esnext.temporal.d.ts"),
        new("esnext.typedarrays", "lib.esnext.typedarrays.d.ts"),
        // Decorators
        new("decorators", "lib.decorators.d.ts"),
        new("decorators.legacy", "lib.decorators.legacy.d.ts"),
    ]);

    // Go: Libs = slices.Collect(LibMap.Keys())（与 LibMap 同文件、故按声明顺序初始化，无需 Lazy）
    public static readonly string[] Libs = [.. LibMap.Keys()];

    private static readonly object?[] _libMapValues = [.. LibMap.Values()];

    // Go: LibFilesSet = collections.NewSetFromItems(core.Map(LibMap.Values(), func(s any) string { return s.(string) })...)
    public static readonly Set<string> LibFilesSet = NewSetFromItems(Map(_libMapValues, s => (string)s!));

    public static bool TryGetLibFileName(string libName, out string fileName)
    {
        // checks if the libName is a valid lib name or file name and converts the lib name to the filename if needed
        libName = ToFileNameLowerCase(libName);
        if (LibFilesSet.Has(libName))
        {
            fileName = libName;
            return true;
        }
        var (lib, ok) = LibMap.Get(libName);
        if (!ok)
        {
            fileName = "";
            return false;
        }
        fileName = (string)lib!;
        return true;
    }

    internal static readonly OrderedMap<string, object?> _moduleResolutionOptionMap = NewOrderedMapFromList<string, object?>(
    [
        new("node16", ModuleResolutionKind.Node16),
        new("nodenext", ModuleResolutionKind.NodeNext),
        new("bundler", ModuleResolutionKind.Bundler),
        new("classic", ModuleResolutionKind.Classic),
        new("node", ModuleResolutionKind.Node10),
        new("node10", ModuleResolutionKind.Node10),
    ]);

    internal static readonly OrderedMap<string, object?> _targetOptionMap = NewOrderedMapFromList<string, object?>(
    [
        new("es5", ScriptTarget.ES5),
        new("es6", ScriptTarget.ES2015),
        new("es2015", ScriptTarget.ES2015),
        new("es2016", ScriptTarget.ES2016),
        new("es2017", ScriptTarget.ES2017),
        new("es2018", ScriptTarget.ES2018),
        new("es2019", ScriptTarget.ES2019),
        new("es2020", ScriptTarget.ES2020),
        new("es2021", ScriptTarget.ES2021),
        new("es2022", ScriptTarget.ES2022),
        new("es2023", ScriptTarget.ES2023),
        new("es2024", ScriptTarget.ES2024),
        new("es2025", ScriptTarget.ES2025),
        new("esnext", ScriptTarget.ESNext),
    ]);

    internal static readonly OrderedMap<string, object?> _moduleOptionMap = NewOrderedMapFromList<string, object?>(
    [
        new("commonjs", ModuleKind.CommonJS),
        new("amd", ModuleKind.AMD),
        new("system", ModuleKind.System),
        new("umd", ModuleKind.UMD),
        new("es6", ModuleKind.ES2015),
        new("es2015", ModuleKind.ES2015),
        new("es2020", ModuleKind.ES2020),
        new("es2022", ModuleKind.ES2022),
        new("esnext", ModuleKind.ESNext),
        new("node16", ModuleKind.Node16),
        new("node18", ModuleKind.Node18),
        new("node20", ModuleKind.Node20),
        new("nodenext", ModuleKind.NodeNext),
        new("preserve", ModuleKind.Preserve),
    ]);

    internal static readonly OrderedMap<string, object?> _moduleDetectionOptionMap = NewOrderedMapFromList<string, object?>(
    [
        new("auto", ModuleDetectionKind.Auto),
        new("legacy", ModuleDetectionKind.Legacy),
        new("force", ModuleDetectionKind.Force),
    ]);

    internal static readonly OrderedMap<string, object?> _jsxOptionMap = NewOrderedMapFromList<string, object?>(
    [
        new("preserve", JsxEmit.Preserve),
        new("react-native", JsxEmit.ReactNative),
        new("react-jsx", JsxEmit.ReactJSX),
        new("react-jsxdev", JsxEmit.ReactJSXDev),
        new("react", JsxEmit.React),
    ]);

    internal static readonly OrderedMap<string, object?> _newLineOptionMap = NewOrderedMapFromList<string, object?>(
    [
        new("crlf", NewLineKind.CRLF),
        new("lf", NewLineKind.LF),
    ]);

    private static readonly Dictionary<ScriptTarget, string> _targetToLibMap = new()
    {
        [ScriptTarget.ESNext] = "lib.esnext.full.d.ts",
        [ScriptTarget.ES2025] = "lib.es2025.full.d.ts",
        [ScriptTarget.ES2024] = "lib.es2024.full.d.ts",
        [ScriptTarget.ES2023] = "lib.es2023.full.d.ts",
        [ScriptTarget.ES2022] = "lib.es2022.full.d.ts",
        [ScriptTarget.ES2021] = "lib.es2021.full.d.ts",
        [ScriptTarget.ES2020] = "lib.es2020.full.d.ts",
        [ScriptTarget.ES2019] = "lib.es2019.full.d.ts",
        [ScriptTarget.ES2018] = "lib.es2018.full.d.ts",
        [ScriptTarget.ES2017] = "lib.es2017.full.d.ts",
        [ScriptTarget.ES2016] = "lib.es2016.full.d.ts",
        [ScriptTarget.ES2015] = "lib.es6.d.ts", // We don't use lib.es2015.full.d.ts due to breaking change.
    };

    public static Dictionary<ScriptTarget, string> TargetToLibMap() => _targetToLibMap;

    public static string GetDefaultLibFileName(CompilerOptions options)
    {
        return _targetToLibMap.TryGetValue(options.GetEmitScriptTarget(), out var name) ? name : "lib.d.ts";
    }

    internal static readonly OrderedMap<string, object?> _watchFileEnumMap = NewOrderedMapFromList<string, object?>(
    [
        new("fixedpollinginterval", WatchFileKind.FixedPollingInterval),
        new("prioritypollinginterval", WatchFileKind.PriorityPollingInterval),
        new("dynamicprioritypolling", WatchFileKind.DynamicPriorityPolling),
        new("fixedchunksizepolling", WatchFileKind.FixedChunkSizePolling),
        new("usefsevents", WatchFileKind.UseFsEvents),
        new("usefseventsonparentdirectory", WatchFileKind.UseFsEventsOnParentDirectory),
    ]);

    internal static readonly OrderedMap<string, object?> _watchDirectoryEnumMap = NewOrderedMapFromList<string, object?>(
    [
        new("usefsevents", WatchDirectoryKind.UseFsEvents),
        new("fixedpollinginterval", WatchDirectoryKind.FixedPollingInterval),
        new("dynamicprioritypolling", WatchDirectoryKind.DynamicPriorityPolling),
        new("fixedchunksizepolling", WatchDirectoryKind.FixedChunkSizePolling),
    ]);

    internal static readonly OrderedMap<string, object?> _fallbackEnumMap = NewOrderedMapFromList<string, object?>(
    [
        new("fixedinterval", PollingKind.FixedInterval),
        new("priorityinterval", PollingKind.PriorityInterval),
        new("dynamicpriority", PollingKind.DynamicPriority),
        new("fixedchunksize", PollingKind.FixedChunkSize),
    ]);
}

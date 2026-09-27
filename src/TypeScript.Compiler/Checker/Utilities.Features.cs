// ported from: internal/checker/utilities.go:1331-1596（FeatureMapEntry + getFeatureMap）
//
// 与 Go 的机械改写（通用规则见 Utilities.cs 文件头）：
// 1) Go 的 `type FeatureMapEntry struct { lib string; props []string }`（未导出字段）→ `internal` + `_camelCase`：
//    `_lib` / `_props`。Go 的复合字面量 `FeatureMapEntry{lib: "es2015", props: []string{...}}` → 本文件加了
//    一个内部构造器 `FeatureMapEntry(string lib, params string[] props)`（等价写法，省掉逐条 object initializer；
//    该类型是 Go 未导出类型，C# 侧无对外契约）。
// 2) Go 的 `var getFeatureMap = sync.OnceValue(func() map[string][]FeatureMapEntry { ... })` → C# 的
//    `private static readonly Lazy<Dictionary<string, FeatureMapEntry[]>> _featureMap = new(CreateFeatureMap);`
//    + `GetFeatureMap() => _featureMap.Value`（`Lazy<T>` 默认 ExecutionAndPublication = `sync.Once` 的
//    至多执行一次语义，且 AOT 安全）。调用点与 Go 一样写 `GetFeatureMap()`（Go 的 `getFeatureMap()`）。
// 3) `map[string][]FeatureMapEntry` → `Dictionary<string, FeatureMapEntry[]>`；Go 的 map 字面量 →
//    C# 的索引器初始化 `["Array"] = [...]`（集合表达式）。
namespace TypeScript.Checker;

/// <summary>Go: <c>type FeatureMapEntry struct { lib string; props []string }</c>（utilities.go:1331-1334）。Go 导出类型 → public；字段未导出 → internal `_camelCase`。</summary>
public sealed class FeatureMapEntry
{
    internal string _lib = "";
    internal string[] _props = [];

    internal FeatureMapEntry(string lib, params string[] props)
    {
        _lib = lib;
        _props = props;
    }
}

public sealed partial class Checker
{
    private static readonly Lazy<Dictionary<string, FeatureMapEntry[]>> _featureMap = new(CreateFeatureMap);

    /// <summary>Go: <c>var getFeatureMap = sync.OnceValue(func() map[string][]FeatureMapEntry)</c>（utilities.go:1336-1596）。</summary>
    internal static Dictionary<string, FeatureMapEntry[]> GetFeatureMap()
    {
        return _featureMap.Value;
    }

    private static Dictionary<string, FeatureMapEntry[]> CreateFeatureMap()
    {
        return new Dictionary<string, FeatureMapEntry[]>
        {
            ["Array"] =
            [
                new FeatureMapEntry("es2015", "find", "findIndex", "fill", "copyWithin", "entries", "keys", "values"),
                new FeatureMapEntry("es2016", "includes"),
                new FeatureMapEntry("es2019", "flat", "flatMap"),
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Iterator"] =
            [
                new FeatureMapEntry("es2015"),
            ],
            ["AsyncIterator"] =
            [
                new FeatureMapEntry("es2015"),
            ],
            ["ArrayBuffer"] =
            [
                new FeatureMapEntry("es2024",
                    "maxByteLength",
                    "resizable",
                    "resize",
                    "detached",
                    "transfer",
                    "transferToFixedLength"),
            ],
            ["Atomics"] =
            [
                new FeatureMapEntry("es2017",
                    "add",
                    "and",
                    "compareExchange",
                    "exchange",
                    "isLockFree",
                    "load",
                    "or",
                    "store",
                    "sub",
                    "wait",
                    "notify",
                    "xor"),
                new FeatureMapEntry("es2024",
                    "waitAsync"),
            ],
            ["SharedArrayBuffer"] =
            [
                new FeatureMapEntry("es2017",
                    "byteLength",
                    "slice"),
                new FeatureMapEntry("es2024",
                    "growable",
                    "maxByteLength",
                    "grow"),
            ],
            ["AsyncIterable"] =
            [
                new FeatureMapEntry("es2018"),
            ],
            ["AsyncIterableIterator"] =
            [
                new FeatureMapEntry("es2018"),
            ],
            ["AsyncGenerator"] =
            [
                new FeatureMapEntry("es2018"),
            ],
            ["AsyncGeneratorFunction"] =
            [
                new FeatureMapEntry("es2018"),
            ],
            ["RegExp"] =
            [
                new FeatureMapEntry("es2015", "flags", "sticky", "unicode"),
                new FeatureMapEntry("es2018", "dotAll"),
                new FeatureMapEntry("es2024", "unicodeSets"),
            ],
            ["RegExpConstructor"] =
            [
                new FeatureMapEntry("es2025", "escape"),
            ],
            ["Reflect"] =
            [
                new FeatureMapEntry("es2015", "apply", "construct", "defineProperty", "deleteProperty", "get", "getOwnPropertyDescriptor", "getPrototypeOf", "has", "isExtensible", "ownKeys", "preventExtensions", "set", "setPrototypeOf"),
            ],
            ["ArrayConstructor"] =
            [
                new FeatureMapEntry("es2015", "from", "of"),
                new FeatureMapEntry("esnext", "fromAsync"),
            ],
            ["ObjectConstructor"] =
            [
                new FeatureMapEntry("es2015", "assign", "getOwnPropertySymbols", "keys", "is", "setPrototypeOf"),
                new FeatureMapEntry("es2017", "values", "entries", "getOwnPropertyDescriptors"),
                new FeatureMapEntry("es2019", "fromEntries"),
                new FeatureMapEntry("es2022", "hasOwn"),
                new FeatureMapEntry("es2024", "groupBy"),
            ],
            ["NumberConstructor"] =
            [
                new FeatureMapEntry("es2015", "isFinite", "isInteger", "isNaN", "isSafeInteger", "parseFloat", "parseInt"),
            ],
            ["Math"] =
            [
                new FeatureMapEntry("es2015", "clz32", "imul", "sign", "log10", "log2", "log1p", "expm1", "cosh", "sinh", "tanh", "acosh", "asinh", "atanh", "hypot", "trunc", "fround", "cbrt"),
                new FeatureMapEntry("es2025", "f16round"),
            ],
            ["Map"] =
            [
                new FeatureMapEntry("es2015", "entries", "keys", "values"),
                new FeatureMapEntry("esnext",
                    "getOrInsert",
                    "getOrInsertComputed"),
            ],
            ["MapConstructor"] =
            [
                new FeatureMapEntry("es2024", "groupBy"),
            ],
            ["Set"] =
            [
                new FeatureMapEntry("es2015", "entries", "keys", "values"),
                new FeatureMapEntry("es2025",
                    "union",
                    "intersection",
                    "difference",
                    "symmetricDifference",
                    "isSubsetOf",
                    "isSupersetOf",
                    "isDisjointFrom"),
            ],
            ["PromiseConstructor"] =
            [
                new FeatureMapEntry("es2015", "all", "race", "reject", "resolve"),
                new FeatureMapEntry("es2020", "allSettled"),
                new FeatureMapEntry("es2021", "any"),
                new FeatureMapEntry("es2024", "withResolvers"),
                new FeatureMapEntry("es2025", "try"),
            ],
            ["Symbol"] =
            [
                new FeatureMapEntry("es2015", "for", "keyFor"),
                new FeatureMapEntry("es2019", "description"),
            ],
            ["WeakMap"] =
            [
                new FeatureMapEntry("es2015"),
                new FeatureMapEntry("esnext",
                    "getOrInsert",
                    "getOrInsertComputed"),
            ],
            ["WeakSet"] =
            [
                new FeatureMapEntry("es2015"),
            ],
            ["String"] =
            [
                new FeatureMapEntry("es2015", "codePointAt", "includes", "endsWith", "normalize", "repeat", "startsWith", "anchor", "big", "blink", "bold", "fixed", "fontcolor", "fontsize", "italics", "link", "small", "strike", "sub", "sup"),
                new FeatureMapEntry("es2017", "padStart", "padEnd"),
                new FeatureMapEntry("es2019", "trimStart", "trimEnd", "trimLeft", "trimRight"),
                new FeatureMapEntry("es2020", "matchAll"),
                new FeatureMapEntry("es2021", "replaceAll"),
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2024", "isWellFormed", "toWellFormed"),
            ],
            ["StringConstructor"] =
            [
                new FeatureMapEntry("es2015", "fromCodePoint", "raw"),
            ],
            ["DateTimeFormat"] =
            [
                new FeatureMapEntry("es2017", "formatToParts"),
            ],
            ["Promise"] =
            [
                new FeatureMapEntry("es2015"),
                new FeatureMapEntry("es2018", "finally"),
            ],
            ["RegExpMatchArray"] =
            [
                new FeatureMapEntry("es2018", "groups"),
            ],
            ["RegExpExecArray"] =
            [
                new FeatureMapEntry("es2018", "groups"),
            ],
            ["Intl"] =
            [
                new FeatureMapEntry("es2018", "PluralRules"),
                new FeatureMapEntry("es2020", "RelativeTimeFormat", "Locale", "DisplayNames"),
                new FeatureMapEntry("es2021", "ListFormat", "DateTimeFormat"),
                new FeatureMapEntry("es2022", "Segmenter"),
                new FeatureMapEntry("es2025", "DurationFormat"),
            ],
            ["NumberFormat"] =
            [
                new FeatureMapEntry("es2018", "formatToParts"),
            ],
            ["SymbolConstructor"] =
            [
                new FeatureMapEntry("es2020", "matchAll"),
                new FeatureMapEntry("esnext",
                    "metadata",
                    "dispose",
                    "asyncDispose"),
            ],
            ["DataView"] =
            [
                new FeatureMapEntry("es2020", "setBigInt64", "setBigUint64", "getBigInt64", "getBigUint64"),
                new FeatureMapEntry("es2025", "setFloat16", "getFloat16"),
            ],
            ["BigInt"] =
            [
                new FeatureMapEntry("es2020"),
            ],
            ["RelativeTimeFormat"] =
            [
                new FeatureMapEntry("es2020", "format", "formatToParts", "resolvedOptions"),
            ],
            ["Int8Array"] =
            [
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Uint8Array"] =
            [
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Uint8ClampedArray"] =
            [
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Int16Array"] =
            [
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Uint16Array"] =
            [
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Int32Array"] =
            [
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Uint32Array"] =
            [
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Float16Array"] =
            [
                new FeatureMapEntry("es2025"),
            ],
            ["Float32Array"] =
            [
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Float64Array"] =
            [
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["BigInt64Array"] =
            [
                new FeatureMapEntry("es2020"),
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["BigUint64Array"] =
            [
                new FeatureMapEntry("es2020"),
                new FeatureMapEntry("es2022", "at"),
                new FeatureMapEntry("es2023", "findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"),
            ],
            ["Error"] =
            [
                new FeatureMapEntry("es2022", "cause"),
            ],
            ["ErrorConstructor"] =
            [
                new FeatureMapEntry("esnext", "isError"),
            ],
            ["Uint8ArrayConstructor"] =
            [
                new FeatureMapEntry("esnext", "fromBase64", "fromHex"),
            ],
            ["DisposableStack"] =
            [
                new FeatureMapEntry("esnext"),
            ],
            ["AsyncDisposableStack"] =
            [
                new FeatureMapEntry("esnext"),
            ],
            ["Date"] =
            [
                new FeatureMapEntry("esnext", "toTemporalInstant"),
            ],
        };
    }
}

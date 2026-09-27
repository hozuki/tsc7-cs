// ported from: internal/project/dirty/util.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `dirty *T, original *T, getMap func(*T) map[K]V`：三个指针/函数类型里 `*T` → `T?`（Go 的指针
//    可空语义，architecture.md §3.2），并加 `where T : class`（C# 只有引用类型能表达 Go 的 `*T` 可空）。
//    `map[K]V` → `Dictionary<K,V>`（返回值可能为 null → `Dictionary<K,V>`，与 Go 返回 nil map 一致：
//    本函数保证**不**返回 null，所以返回类型标注为非 null）。
//
// 2) 函数体逐分支照抄：dirty 侧的 map 为 nil 时克隆 original 的（original 也为 nil 则新建空表）。
//
// 3) 包级函数落在同名静态类 `Dirty` 里（architecture.md §2），调用点 `using static TypeScript.Project.Dirty.Dirty;`。
namespace TypeScript.Project.Dirty;

public static partial class Dirty
{
    /// <summary>Go: <c>CloneMapIfNil[K,V,T](dirty, original *T, getMap func(*T) map[K]V) map[K]V</c>。</summary>
    public static Dictionary<K, V> CloneMapIfNil<K, V, T>(T? dirty, T? original, Func<T, Dictionary<K, V>?> getMap)
        where K : notnull
        where T : class
    {
        var dirtyMap = getMap(dirty!);
        if (dirtyMap == null)
        {
            if (original == null)
            {
                return new Dictionary<K, V>();
            }

            var originalMap = getMap(original);
            if (originalMap == null)
            {
                return new Dictionary<K, V>();
            }

            return new Dictionary<K, V>(originalMap);
        }

        return dirtyMap;
    }
}

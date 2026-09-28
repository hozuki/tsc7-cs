// ported from: internal/api/jsonvalue_test.go:1-35
//
// 与 Go 测试的差异（逐条）：
//
// 1) Go 用 `json.Unmarshal([]byte(`…`), &value)` 把 JSON 解进 `packagejson.JSONValue`；C# 的同款入口是
//    `Json.UnmarshalNew<JSONValue>(byte[])`（`JSONValue` 实现 `IJsonUnmarshaler`，`new JSONValue()` 即 Go 的零值）。
// 2) Go 的运行时类型断言 `jsonValueToAny(value).(*collections.OrderedMap[string, any])` → C# 的强转
//    `(OrderedMap<string, object?>)JsonValueToAny(value)!`（断言语义一致：Go 失败是 panic，C# 失败是 InvalidCastException）。
// 3) `slices.Collect(root.Keys())` → `root.Keys().ToArray()`（C# 的 `Keys()` 返回 `IEnumerable<string>`）；
//    `assert.DeepEqual` 比切片 → `Assert.Equal(string[], string[])`。
// 4) Go 里 `any` 形状的数字是 `float64`，C# 里是 `double`（docs/json.md §2）⇒ `assert.Equal(t, root.GetOrZero("z"), float64(1))`
//    落成 `Assert.Equal(1.0, (double)root.GetOrZero("z")!)`。
// 5) Go 的 `[]any` → C# 的 `object?[]`（契约 §2：切片 → 数组），元素断言 `array[1] == nil` → `Assert.Null(array[1])`。
// 6) 类名加 `Api` 前缀：`tests/…/PackageJson/JsonValueTests.cs` 已经占用了 `JsonValueTests`（同命名空间）。
namespace TypeScript.Tests;

using System.Text;
using TypeScript.Collections;
using TypeScript.PackageJson;
using Xunit;
using static TypeScript.Api.Api;
using static TypeScript.Json.Json;

/// <summary>Go: <c>internal/api/jsonvalue_test.go</c> 的 <c>TestJSONValueToAny</c>（jsonvalue_test.go:13）。</summary>
public class ApiJsonValueTests
{
    /// <summary>Go: <c>TestJSONValueToAny</c>（jsonvalue_test.go:13-35）。</summary>
    [Fact]
    public void TestJSONValueToAny()
    {
        var json = """{"z":1,"a":{"y":2,"x":3},"m":[{"b":4,"a":5},null],"e":[]}"""u8.ToArray();
        var value = UnmarshalNew<JSONValue>(json);
        Assert.Equal(JSONValueType.Object, value.Type);

        var root = (OrderedMap<string, object?>)JsonValueToAny(value)!;
        Assert.Equal(new[] { "z", "a", "m", "e" }, root.Keys().ToArray());
        Assert.Equal(1.0, (double)root.GetOrZero("z")!);

        var nested = (OrderedMap<string, object?>)root.GetOrZero("a")!;
        Assert.Equal(new[] { "y", "x" }, nested.Keys().ToArray());

        var array = (object?[])root.GetOrZero("m")!;
        var arrayObject = (OrderedMap<string, object?>)array[0]!;
        Assert.Equal(new[] { "b", "a" }, arrayObject.Keys().ToArray());
        Assert.Null(array[1]);

        var empty = (object?[])root.GetOrZero("e")!;
        Assert.NotNull(empty);
        Assert.Empty(empty);
    }
}

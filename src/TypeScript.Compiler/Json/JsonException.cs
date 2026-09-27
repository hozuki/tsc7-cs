// C# 侧新增：Go 的 `(T, error)` 错误路径在 C# 侧统一抛这个异常（docs/architecture.md §3.2）。
// 对应 Go 的 json/jsontext 返回的 error：语法错误、类型不匹配、重复属性名、NaN/Inf、unsupported type 等。
namespace TypeScript.Json;

public sealed class JsonException : Exception
{
    public JsonException(string message)
        : base(message)
    {
    }

    public JsonException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

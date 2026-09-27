// ported from: internal/packagejson/validated.go
//
// 与 Go 一致：`TypeValidatedField` 是**导出**类型，名字照抄（不按未导出接口的 `I` 前缀规则改名）——
// docs/architecture.md §2「公开 API 与 TSGO 一致」，验收也按 Go 的导出名核对。
//
// Go 里这个接口由 `*Expected[T]`（指针接收者方法）满足；C# 里 `Expected<T>` 是 class（见 Expected.cs 文件头），
// 直接列出该接口即可。
namespace TypeScript.PackageJson;

/// <summary>Go: <c>type TypeValidatedField interface</c>（一个"字段是否出现过/能不能按期望类型解出来"的探询接口）。</summary>
public interface TypeValidatedField
{
    bool IsPresent();

    bool IsValid();

    string ExpectedJSONType();

    string ActualJSONType();
}

// ported from: internal/lsp/lsproto/lsp_generated.go 的 codec 覆盖（Go 靠 encoding/json 反射，本移植靠生成器发字段表）
//
// 这组用例防的是同一类复发：**Go 侧有反射就天然可编解码的类型，C# 侧必须有显式 codec**。
// 触发过的事故：`InitializedParams`（空 struct、无方法、只作为 notification 的 params 出现）既不在
// "有 UnmarshalJSONFrom/MarshalJSONTo" 集合、也不在"被别的 struct 字段引用"的可达闭包里，于是生成物只发了
// 一个裸 class ⇒ `Json.UnmarshalNew<InitializedParams>("{}")` 与 `Json.Marshal(new InitializedParams())`
// 双双抛 JsonException ⇒ `initialized` 通知在客户端与服务端两侧都失败（fourslash 起不来、真 server 收不了）。
// 修法是让生成器对**所有** struct 发 codec（见 artifacts/lsproto-contract.md §4.6）。
//
// Go 语义对照（对 C# 侧的三条断言依据）：
//   * 空 struct：marshal → `{}`；unmarshal 接受任意对象并忽略全部成员（`{"a":1}` 也不报错）；
//   * 非对象（数组/字符串/数字/null）解进 struct → 报错（本移植是 JsonException，Go 是 error）；
//   * 无 json tag 的导出字段：Go 的反射用**字段名**当 JSON 名，本移植同样（见 gen-emit.mjs 的 emitStruct 注释）。
using System.Text;
using Xunit;

using TypeScript.Json;
using TypeScript.LsProto;
using static TypeScript.Json.Json;

namespace TypeScript.Tests;

public class CodecCoverageTests
{
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(byte[] data) => Encoding.UTF8.GetString(data);

    /// <summary>Go: <c>encoding/json</c> 对空 struct 的输出是 <c>{}</c>。</summary>
    private static void AssertEmptyObjectRoundTrip<T>()
        where T : class, new()
    {
        Assert.Equal("{}", Text(Marshal(new T())));

        // Go: json.Unmarshal([]byte("{}"), new(T)) 不报错，且解出来仍是零值。
        var value = UnmarshalNew<T>(Utf8("{}"));
        Assert.NotNull(value);
        Assert.Equal("{}", Text(Marshal(value)));

        // Go: 未知成员被忽略（反射解码只看字段）。
        var withUnknown = UnmarshalNew<T>(Utf8("""{"unknownField":{"nested":[1,2,3]}}"""));
        Assert.Equal("{}", Text(Marshal(withUnknown)));
    }

    [Fact]
    public void InitializedParamsHasCodec()
    {
        // `initialized` 通知的 params：Go 是 `type InitializedParams struct{}`。
        AssertEmptyObjectRoundTrip<InitializedParams>();
    }

    /// <summary>空 struct 也必须拒绝非对象（Go: json.Unmarshal 进 struct 要求对象）。</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("\"oops\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void InitializedParamsRejectsNonObject(string input)
    {
        Assert.Throws<JsonException>(() => UnmarshalNew<InitializedParams>(Utf8(input)));
    }

    /// <summary>
    /// <c>RegisterOptions</c>：Go 里是"外部标记联合的成员容器"，本身没有手写 codec（Go 靠反射）；
    /// C# 侧同样必须能编解码——其字段没有 json tag，所以 JSON 名是**字段名**（与 Go 的反射一致）。
    /// </summary>
    [Fact]
    public void RegisterOptionsHasCodec()
    {
        var options = new RegisterOptions();
        var text = Text(Marshal(options));
        Assert.StartsWith("{", text, StringComparison.Ordinal);
        Assert.Contains("\"TextDocumentImplementation\":null", text, StringComparison.Ordinal);
        Assert.Contains("\"WorkspaceDidChangeWatchedFiles\":null", text, StringComparison.Ordinal);

        var back = UnmarshalNew<RegisterOptions>(Utf8(text));
        Assert.Null(back.TextDocumentImplementation);
        Assert.Null(back.WorkspaceDidChangeWatchedFiles);

        // 字段名即 JSON 名：能把对象解出来（不是"必须对象"之外的额外严格度）。
        var one = UnmarshalNew<RegisterOptions>(Utf8("""{"TextDocumentImplementation":{"documentSelector":null}}"""));
        Assert.NotNull(one.TextDocumentImplementation);
    }

    /// <summary><c>WorkspaceFoldersInitializeParams</c>：同 RegisterOptions，Go 侧无手写 codec（靠反射）。</summary>
    [Fact]
    public void WorkspaceFoldersInitializeParamsHasCodec()
    {
        // workspaceFolders 是 `json:"workspaceFolders,omitzero" lsp:"nullable"`，零值 → 整个对象为空。
        Assert.Equal("{}", Text(Marshal(new WorkspaceFoldersInitializeParams())));
        var parsed = UnmarshalNew<WorkspaceFoldersInitializeParams>(Utf8("""{"workspaceFolders":null}"""));
        Assert.Null(parsed.WorkspaceFolders);
    }

    /// <summary>
    /// 覆盖回归的直接体检：这些类型分别代表三种"过去没有 codec"的来源——
    /// ① 只作为 method/notification 的 params 或 result 出现；② 空 struct；③ Resolved*（只有 resolve() 构造）。
    /// </summary>
    [Fact]
    public void TypesReachableFromDispatchTablesAndResolversHaveCodecs()
    {
        // ① params/result：全部来自 lsp_generated.go 的 ...Info 表（InitializedParams 是其中的空 struct）。
        Assert.Equal("{}", Text(Marshal(new InitializedParams())));

        // ② 空 struct 占位类型（自定义 data 的容器）。
        AssertEmptyObjectRoundTrip<InlayHintData>();
        AssertEmptyObjectRoundTrip<CodeActionData>();
        AssertEmptyObjectRoundTrip<DiagnosticData>();

        // ③ Resolved*（Go 侧靠反射，本移植发 tag 派生的字段表）。
        var resolved = new ResolvedChangeAnnotationsSupportOptions();
        Assert.Equal("{}", Text(Marshal(resolved)));
        var parsed = UnmarshalNew<ResolvedChangeAnnotationsSupportOptions>(Utf8("""{"groupsOnLabel":true}"""));
        Assert.True(parsed.GroupsOnLabel);
    }
}

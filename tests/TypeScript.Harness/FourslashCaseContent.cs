// fourslash 用例正文的解码入口（非 Go 源：C# 侧的基础设施）。
//
// 为什么需要：4.3k 个 fourslash 用例的正文合计远超单个程序集 #US（user-string）堆的 16 MB 上限
// （CS8103: The combined length of user strings used by the program exceeds the limit）。Go 侧不存在
// 这个问题（每个用例是独立的包级常量，没有"单程序集字符串堆"的概念）。
//
// 做法（用户指定）：正文改写成 **UTF-8 字面量** `"…"u8`（类型 `ReadOnlySpan<byte>`）。`u8` 字面量的字节
// 进 **#Blob 堆**，不占 #US 堆，因此不受 16 MB 上限约束；且仍是单程序集、无外置资源、字节可精确控制。
// 生成物里出现的是 `var content = FourslashCaseContent.U8("…"u8);`。
//
// 逐字节保证（生成器 artifacts/fourslash-gen/gen.mjs 侧）：
//   * 转义规则固定：`"`/`\` 转义、`\n`/`\r`/`\t`/`\0` 用简写、其余 ASCII 可打印原样、非 ASCII 一律
//     `\uXXXX`、补充平面用**单个** `\UXXXXXXXX`（若写成代理对，`u8` 会把每个 `\uXXXX` 各自编码 → 错误字节）；
//   * 生成器对**每个**正文做一次 round-trip 自检：把生成的 `"…"u8` 按键位解析回字节，必须与正文的
//     UTF-8 字节逐字节相同（不一致直接终止生成）；实测 4,466 个正文全部通过；
//   * 这里用 `Encoding.UTF8.GetString(ReadOnlySpan<byte>)` 还原 ⇒ 与原 `const content = \`…\`` 字符串逐字节相同
//     （CRLF/LF、尾随空白、BOM 都不做任何归一化）。
namespace TypeScript.Harness.FourslashCases;

using System.Text;

/// <summary>Go 侧不存在对应物（Go 的用例正文是编译期字符串常量）。</summary>
internal static class FourslashCaseContent
{
    /// <summary>UTF-8 字节 → string（与 Go 的 <c>string</c> 语义一致：逐字节、不归一化）。</summary>
    public static string U8(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);
}

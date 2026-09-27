// ported from: internal/json/json.go（`UnmarshalerFrom = json.UnmarshalerFrom` 的别名）
// + Go encoding/json/v2 的 UnmarshalerFrom 接口（`UnmarshalJSONFrom(*jsontext.Decoder) error`）。
//
// 与 Go 一致：null 也会调用这个方法，由实现自己决定怎么处理（TSGO 的 OrderedMap 就是在里面
// 把 null 当 no-op，见 internal/collections/ordered_map.go）。
namespace TypeScript.Json;

public interface IJsonUnmarshaler
{
    // 从 decoder 当前位置读出一个值（Go 的 `UnmarshalJSONFrom(dec *jsontext.Decoder) error`）。
    void UnmarshalJSONFrom(Decoder decoder);
}

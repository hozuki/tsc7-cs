// ported from: internal/json/json.go（`MarshalerTo = json.MarshalerTo` 的别名）
// + Go encoding/json/v2 的 MarshalerTo 接口（`MarshalJSONTo(*jsontext.Encoder) error`）。
//
// 映射优先级：实现了这个接口的类型**优先**于结构体/字典/数组的字段映射（与 Go json/v2 一样）。
namespace TypeScript.Json;

public interface IJsonMarshaler
{
    // 把 this 写进 encoder（Go 的 `MarshalJSONTo(enc *jsontext.Encoder) error`）。
    void MarshalJSONTo(Encoder encoder);
}

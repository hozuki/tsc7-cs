// ported from: internal/testutil/contentmappertest/dynamic_verbatim.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `dynamicVerbatimHandler struct { verbatimHandler; lifecycle *ProjectLifecycle }`（内嵌 verbatimHandler
//    以提升它的 HandleRequest，最后由 `h.verbatimHandler.HandleRequest(...)` 转调）→ C# 用继承：
//    `DynamicVerbatimHandler : VerbatimHandler`（Verbatim.cs 提供，HandleRequest 是 virtual）+ `base.HandleRequest`。
// 2) 未导出类型改名：`dynamicVerbatimHandler` → `DynamicVerbatimHandler`；未导出字段 `lifecycle` → `_lifecycle`。
// 3) `atomic.Int32` 的 `Add(1)`/`Load()` → C# 的 `Interlocked.Increment(ref field)` / 直接读字段（见
//    ContentMapper/HostImpl.cs 文件头第 4 条）。故 `ProjectLifecycle` 的两个字段是普通 public int。
// 4) `string(p.Options)`（Go 的 []byte → string 转换）→ `Encoding.UTF8.GetString(p.Options ?? [])`；
//    `json.Value` → `byte[]?`（见 ContentMapper/HostImpl.cs 文件头第 1 条）。
// 5) `(T, error)` → 正常返回 T、失败抛异常；`json.Unmarshal` 失败 → JsonException；params 为空 → `?? throw`。
// 6) Go 的 switch 各 case 是独立作用域（两个 case 的形参都叫 `p`），C# 的 switch 段共享作用域 → Transform 分支的
//    形参改名 `transformParams`（`{ }` 不能隔离，仍会 CS0136）。
// 7) Go 的 `tspath.GetDirectoryPath`/`tspath.CombinePaths` 是包级函数 → `using static TypeScript.TSPath.TSPath;`
//    后裸名调用（与仓库既有写法一致；注意 `TypeScript.TSPath.Path` 是路径值类型，不是这些函数的家）。
using System.Text;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.Json;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Ipc.Ipc;
// Go 的 `tspath.GetDirectoryPath` / `tspath.CombinePaths` 是包级函数 → `TypeScript.TSPath.TSPath` 的静态成员
// （`using static` 后裸名调用，与仓库既有写法一致；`Path` 在 TypeScript.TSPath 里是路径值类型，不是函数家）。
using static TypeScript.TSPath.TSPath;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type ProjectLifecycle struct { Opens atomic.Int32; Closes atomic.Int32 }</c>（记录映射器的项目协议调用次数）。</summary>
public class ProjectLifecycle
{
    /// <summary>Go: <c>Opens atomic.Int32</c>（`Add(1)` → `Interlocked.Increment(ref lifecycle.Opens)`，`Load()` → 直接读）。</summary>
    public int Opens;

    /// <summary>Go: <c>Closes atomic.Int32</c>（同上）。</summary>
    public int Closes;
}

/// <summary>Go: <c>type dynamicVerbatimHandler struct { verbatimHandler; lifecycle *ProjectLifecycle }</c>。</summary>
public class DynamicVerbatimHandler : VerbatimHandler
{
    /// <summary>Go: <c>lifecycle *ProjectLifecycle</c>（未导出字段 → `_camelCase`）。</summary>
    private readonly ProjectLifecycle? _lifecycle;

    /// <summary>Go: `dynamicVerbatimHandler{lifecycle: lifecycle}`。</summary>
    public DynamicVerbatimHandler(ProjectLifecycle? lifecycle)
    {
        _lifecycle = lifecycle;
    }

    /// <summary>Go: <c>func (h dynamicVerbatimHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public override object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodOpenProject:
                if (_lifecycle is not null)
                {
                    Interlocked.Increment(ref _lifecycle.Opens);
                }

                var p = UnmarshalParams<OpenProjectParams>(@params)
                    ?? throw new JsonException("contentmappertest: missing openProject parameters");
                var optionsText = Encoding.UTF8.GetString(p.Options ?? []);
                var identity = p.ConfigFileName + ":" + optionsText;
                OptionDiagnosticResult[]? diagnostics = null;
                if (optionsText == "{\"plugins\":[{\"name\":1}]}")
                {
                    diagnostics =
                    [
                        new OptionDiagnosticResult
                        {
                            Path = ["\"plugins\""u8.ToArray(), "0"u8.ToArray(), "\"name\""u8.ToArray()],
                            MessageText = "Option 'name' requires a string.",
                            Code = 123,
                        },
                    ];
                }

                var watchDirectory = GetDirectoryPath(p.ConfigFileName);
                if (watchDirectory.Length == 0)
                {
                    watchDirectory = "/";
                }

                return new OpenProjectResult
                {
                    ConfigIdentity = identity,
                    WatchedFiles = [CombinePaths(watchDirectory, "mapper.config.json")],
                    OptionDiagnostics = diagnostics,
                };
            case MethodCloseProject:
                if (_lifecycle is not null)
                {
                    Interlocked.Increment(ref _lifecycle.Closes);
                }

                return null;
            case MethodTransform:
            {
                // Go 的 switch 各 case 是独立作用域（两个 case 都叫 `p`）；C# 的 switch 段共享作用域，
                // 所以这里的参数改名 `transformParams`。
                var transformParams = UnmarshalParams<TransformParams>(@params)
                    ?? throw new JsonException("contentmappertest: missing transform parameters");
                if (transformParams.ProjectHandle.Length == 0)
                {
                    throw new Exception("content mapper transform requires a project handle");
                }

                break;
            }
        }

        // Go: return h.verbatimHandler.HandleRequest(ctx, method, params)。
        return base.HandleRequest(ctx, method, @params);
    }
}

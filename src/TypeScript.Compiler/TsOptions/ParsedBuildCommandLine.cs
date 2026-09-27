// ported from: internal/tsoptions/parsedbuildcommandline.go
//
// 映射约定（docs/architecture.md §2/§3.2/§3.3）：
// 1) `sync.Once` + 缓存字段（Go 里 `resolvedProjectPaths`/`locale` 既是缓存又是结果）→ C# 用 `Lazy<T>` 实例字段；
//    缓存值本身不再单独保留（它在 C# 里没有别的读取点，Go 侧的字段只被本文件的这两个方法读）。
// 2) `locale.Locale`（Go 的 `string` 别名语义）→ `TypeScript.Locale.Locale`；`(?T, bool)` 形态的 `locale.Parse`
//    在 C# 是 `TryParse`（§3.2），这里丢弃返回值（与 Go 的 `p.locale, _ = locale.Parse(...)` 一致）。
// 3) Go 的 `Locale() locale.Locale` 与类型名同名的**方法**在 C# 会遮蔽类型名（CS0119）→ 落成同名属性（"Color Color"
//    规则，见 docs/ast.md §2 对 SubtreeFacts 的处理），调用点由 `p.Locale()` 变成 `p.Locale`。
// 4) `*core.BuildOptions`/`*core.CompilerOptions`/`*core.WatchOptions` 字段 → `?`（Go 侧是 nil 可空的指针；本结构由
//    ParseBuildCommandLine 填充后才使用）。
using System;
using TypeScript.Ast;
using TypeScript.Core;

using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.TsOptions;

public sealed class ParsedBuildCommandLine
{
    public BuildOptions? BuildOptions;
    public CompilerOptions? CompilerOptions;
    public WatchOptions? WatchOptions;
    public string[] Projects = [];
    public Diagnostic[] Errors = [];
    public object? Raw;

    internal ComparePathsOptions _comparePathsOptions;

    private Lazy<string[]>? _resolvedProjectPathsOnce;

    private Lazy<TypeScript.Locale.Locale>? _localeOnce;

    public string[] ResolvedProjectPaths()
    {
        _resolvedProjectPathsOnce ??= new Lazy<string[]>(() => Map(Projects, project =>
            ResolveConfigFileNameOfProjectReference(ResolvePath(_comparePathsOptions.CurrentDirectory, project))));
        return _resolvedProjectPathsOnce.Value;
    }

    public TypeScript.Locale.Locale Locale
    {
        get
        {
            // Go: p.locale, _ = locale.Parse(p.CompilerOptions.Locale)（CompilerOptions 由 ParseBuildCommandLine 赋值，
            // nil 会在 Go 侧 panic，这里断言非空以保留同样的前提）
            _localeOnce ??= new Lazy<TypeScript.Locale.Locale>(() =>
            {
                TypeScript.Locale.Locale.TryParse(CompilerOptions!.Locale, out var locale);
                return locale;
            });
            return _localeOnce.Value;
        }
    }
}

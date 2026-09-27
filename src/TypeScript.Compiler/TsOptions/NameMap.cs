// ported from: internal/tsoptions/namemap.go
//
// 映射约定（docs/architecture.md §3.4）：Go 的 `strings.ToLower`（JS 语义、完整多字符映射）→ `StringUtil.ToLowerJS`，
// **不是** .NET 的 `string.ToLower`。
// 包级 var → `public static …`，但三个 NameMap 都用 `Lazy<T>`：它们的初始化依赖别的 partial 文件（decls*.go）里的
// `OptionsDeclarations`/`BuildOpts`/`OptionsForWatch`，而 C# 的 partial 类型跨文件的字段初始化顺序未定义。
// 未导出字段 `optionsNames`/`shortOptionNames` → `internal readonly _camelCase`。
using System.Collections.Generic;
using TypeScript.Collections;
using static TypeScript.Collections.Collections;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    private static readonly Lazy<NameMap> _compilerNameMap = new(() => GetNameMapFromList(OptionsDeclarations));
    private static readonly Lazy<NameMap> _buildNameMap = new(() => GetNameMapFromList(BuildOpts));
    private static readonly Lazy<NameMap> _watchNameMap = new(() => GetNameMapFromList(OptionsForWatch));

    public static NameMap CompilerNameMap => _compilerNameMap.Value;
    public static NameMap BuildNameMap => _buildNameMap.Value;
    public static NameMap WatchNameMap => _watchNameMap.Value;

    public static NameMap GetNameMapFromList(CommandLineOption[] optDecls)
    {
        var optionsNames = NewOrderedMapWithSizeHint<string, CommandLineOption>(optDecls.Length);
        var shortOptionNames = new Dictionary<string, string>();
        foreach (var option in optDecls)
        {
            optionsNames.Set(ToLowerJS(option.Name), option);
            if (option.ShortName != "")
            {
                shortOptionNames[option.ShortName] = option.Name;
            }
        }
        return new NameMap(optionsNames, shortOptionNames);
    }
}

public sealed class NameMap
{
    internal readonly OrderedMap<string, CommandLineOption> _optionsNames;
    internal readonly Dictionary<string, string> _shortOptionNames;

    internal NameMap(OrderedMap<string, CommandLineOption> optionsNames, Dictionary<string, string> shortOptionNames)
    {
        _optionsNames = optionsNames;
        _shortOptionNames = shortOptionNames;
    }

    public CommandLineOption? Get(string name) => _optionsNames.GetOrZero(ToLowerJS(name));

    public CommandLineOption? GetFromShort(string shortName)
    {
        // returns option only if shortName is a valid short option
        if (!_shortOptionNames.TryGetValue(shortName, out var name))
        {
            return null;
        }
        return Get(name);
    }

    public CommandLineOption? GetOptionDeclarationFromName(string optionName, bool allowShort)
    {
        optionName = ToLowerJS(optionName);
        // Try to translate short option names to their full equivalents.
        if (allowShort && _shortOptionNames.TryGetValue(optionName, out var @short) && @short != "")
        {
            optionName = @short;
        }
        return Get(optionName);
    }
}

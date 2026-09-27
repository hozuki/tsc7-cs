// ported from: internal/execute/tsc/help.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) 本命名空间里有名为 `System` 的接口（Go 的 `tsc.System`），会遮蔽根命名空间 `System` →
//    本文件一律 `using global::System;` 且禁止 `System.` 前缀（同 Compile.cs 偏离 1）。
// 2) `io.Writer` → `System.IO.TextWriter`；`fmt.Fprint(w, s)` → `w.Write(s)`。
// 3) Go 的 `colors` 类型与 `createColors`（diagnostics.go:68-134）已由 Diagnostics.cs 移植为本命名空间的
//    internal `Colors` 与 `CreateColors`，本文件**不重复定义**；`colors.blue(bold/blueBackground/brightWhite)` →
//    `Blue/Bold/BlueBackground/BrightWhite`（§2：未导出方法 PascalCase）。
// 4) Go 的 `fmt.Sprintf("%-*s", w, s)` / `"%*s"`（空格填充、超宽不截断）→ `PadRight`/`PadLeft`。
//    `PadLeft`/`PadRight` 没有 culture 参数，也**不存在**区域相关性（.NET 里只有数字/日期格式化吃 culture，
//    而 Go 的 `%*s` 也只做空格填充）。
// 5) Go 的 `len(s)` 是 UTF-8 **字节**数 → `string.Length`（UTF-16 代码单元数 = TS/Strada 的 UTF-16 假设）。
//    `getPrettyOutput` 因此按代码单元切分（Go 侧按字节切片会截断多字节字符——Go 源码里的 `!!!` 注释也承认
//    Strada 假设的是 UTF-16，见 help.go:339）。
// 6) Go 的 `option.Kind == tsoptions.CommandLineOptionTypeX` → `CommandLineOptionKind.X`（§3.3）；
//    `string(option.Kind)` → `option.Kind.String()`（同 §3.3 的 Stringer 扩展方法，字面量与 Go 一致）。
// 7) Go 的 `any` 相等比较（`value == v`、`DefaultValueDescription == "false"`、`defaultValue == core.TSUnknown`）→
//    `object.Equals(a, b)`：对装箱值做"动态类型 + 值"比较，与 Go 的接口比较一致（Go 对不可比较的动态类型会 panic，
//    C# 返回 false；这两处都只可能是标量，不可达）。
// 8) `panic` → `Debug.Fail`（§3.2，抛 DebugFailureException）。
// 8.1) `formatDefaultValue` 末尾的 `fmt.Sprintf("%v", defaultValue)` → 显式分支：bool 走 `"true"/"false"`
//      （C# 的 `bool.ToString()` 是 "True"/"False"，实测 baseline 逐字节对拍会挂）、数字走
//      `IFormattable.ToString(null, CultureInfo.InvariantCulture)`（Go 的 %v 与区域无关）、
//      其余走 `ToString()`（§3.3 保证 enum 合法值上 `ToString()` 与 Go 的 stringer 逐字相同）。
// 9) 未导出类型 `valueCandidate` → internal `ValueCandidate`（§2：未导出类型按 C# 习惯命名）；
//    Go 的包级未导出函数 → internal static 方法（PascalCase）。
// 10) `slices.Clone` + `append` → 集合表达式 `[.. OptionsDeclarations, TscBuildOption]`；
//     `slices.SortFunc` → `Array.Sort`（两者都是不稳定排序，语义一致）。
// 11) `strings.ToLower` → `TypeScript.StringUtil.StringUtil.ToLowerJS`（§3.4：JS 语义的完整小写映射）；
//     `strings.Compare` → `string.CompareOrdinal`（序数比较，只取符号，等价）；
//     `strings.Repeat(" ", n)` → `new string(' ', n)`；`strings.Join(xs, sep)` → `string.Join(sep, xs)`。
// 12) Go 的 `commandLine.CompilerOptions()` 返回 `*core.CompilerOptions`（解析后恒非 nil，为 nil 时 Go 会 panic）→
//     C# 的可空返回 + `!`（与 TsOptions/ParsedCommandLine.cs 既有写法一致）。
// 13) `getValueCandidate` 的 `sys` 参数在 Go 里也没被使用（Go 允许未使用参数）→ 保留同样签名。
using global::System;
using global::System.Collections.Generic;
using global::System.Globalization;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
using Loc = TypeScript.Locale.Locale;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TsOptions.TsOptions;

namespace TypeScript.Execute.Tsc;

public static partial class Tsc
{
    /// <summary>Go: <c>func PrintVersion(sys System, locale locale.Locale)</c>（help.go:14-16）。</summary>
    public static void PrintVersion(System sys, Loc locale)
    {
        sys.Writer().WriteLine(Version_0.Localize(locale, Version()));
    }

    /// <summary>Go: <c>func PrintHelp(sys System, locale locale.Locale, commandLine *tsoptions.ParsedCommandLine)</c>（help.go:18-24）。</summary>
    public static void PrintHelp(System sys, Loc locale, ParsedCommandLine commandLine)
    {
        if (commandLine.CompilerOptions()!.All.IsFalseOrUnknown())
        {
            PrintEasyHelp(sys, locale, GetOptionsForHelp(commandLine));
        }
        else
        {
            PrintAllHelp(sys, locale, GetOptionsForHelp(commandLine));
        }
    }

    /// <summary>Go: <c>func getOptionsForHelp(commandLine *tsoptions.ParsedCommandLine) []*tsoptions.CommandLineOption</c>（help.go:26-41）。</summary>
    internal static CommandLineOption[] GetOptionsForHelp(ParsedCommandLine commandLine)
    {
        // Sort our options by their names, (e.g. "--noImplicitAny" comes before "--watch")
        CommandLineOption[] opts = [.. OptionsDeclarations, TscBuildOption];

        if (commandLine.CompilerOptions()!.All.IsTrue())
        {
            Array.Sort(opts, (a, b) => string.CompareOrdinal(
                TypeScript.StringUtil.StringUtil.ToLowerJS(a.Name),
                TypeScript.StringUtil.StringUtil.ToLowerJS(b.Name)));
            return opts;
        }

        return Filter(opts, opt => opt.ShowInSimplifiedHelpView);
    }

    /// <summary>Go: <c>func getHeader(sys System, message string) []string</c>（help.go:43-64）。</summary>
    internal static string[] GetHeader(System sys, string message)
    {
        var colors = CreateColors(sys);
        var header = new List<string>(3);
        var terminalWidth = sys.GetWidthOfTerminal();
        const string tsIcon = "     ";
        const string tsIconTS = "  TS ";
        // Go: const tsIconLength = len(tsIcon)（ASCII，字节数 = UTF-16 代码单元数）
        var tsIconLength = tsIcon.Length;

        var tsIconFirstLine = colors.BlueBackground(tsIcon);
        var tsIconSecondLine = colors.BlueBackground(colors.BrightWhite(tsIconTS));
        // If we have enough space, print TS icon.
        if (terminalWidth >= message.Length + tsIconLength)
        {
            // right align of the icon is 120 at most.
            var rightAlign = IfElse(terminalWidth > 120, 120, terminalWidth);
            var leftAlign = rightAlign - tsIconLength;
            header.Add(message.PadRight(leftAlign));
            header.Add(tsIconFirstLine);
            header.Add("\n");
            header.Add(new string(' ', leftAlign));
            header.Add(tsIconSecondLine);
            header.Add("\n");
        }
        else
        {
            header.Add(message);
            header.Add("\n");
            header.Add("\n");
        }
        return header.ToArray();
    }

    /// <summary>Go: <c>func printEasyHelp(sys System, locale locale.Locale, simpleOptions []*tsoptions.CommandLineOption)</c>（help.go:66-111）。</summary>
    internal static void PrintEasyHelp(System sys, Loc locale, CommandLineOption[] simpleOptions)
    {
        var colors = CreateColors(sys);
        var output = new List<string>();

        void Example(string[] examples, Message desc)
        {
            foreach (var example in examples)
            {
                output.Add("  ");
                output.Add(colors.Blue(example));
                output.Add("\n");
            }
            output.Add("  ");
            output.Add(desc.Localize(locale));
            output.Add("\n");
            output.Add("\n");
        }

        var msg = X_tsc_Colon_The_TypeScript_Compiler.Localize(locale) + " - " + Version_0.Localize(locale, Version());
        output.AddRange(GetHeader(sys, msg));

        output.Add(colors.Bold(COMMON_COMMANDS.Localize(locale)));
        output.Add("\n");
        output.Add("\n");

        Example(["tsc"], Compiles_the_current_project_tsconfig_json_in_the_working_directory);
        Example(["tsc app.ts util.ts"], Ignoring_tsconfig_json_compiles_the_specified_files_with_default_compiler_options);
        Example(["tsc -b"], Build_a_composite_project_in_the_working_directory);
        Example(["tsc --init"], Creates_a_tsconfig_json_with_the_recommended_settings_in_the_working_directory);
        Example(["tsc -p ./path/to/tsconfig.json"], Compiles_the_TypeScript_project_located_at_the_specified_path);
        Example(["tsc --help --all"], An_expanded_version_of_this_information_showing_all_possible_compiler_options);
        Example(["tsc --noEmit", "tsc --target esnext"], Compiles_the_current_project_with_additional_settings);

        var cliCommands = new List<CommandLineOption>();
        var configOpts = new List<CommandLineOption>();
        foreach (var opt in simpleOptions)
        {
            if (opt.IsCommandLineOnly || opt.Category == Command_line_Options)
            {
                cliCommands.Add(opt);
            }
            else
            {
                configOpts.Add(opt);
            }
        }

        output.AddRange(GenerateSectionOptionsOutput(sys, locale, COMMAND_LINE_FLAGS.Localize(locale), cliCommands.ToArray(), false /*subCategory*/, null /*beforeOptionsDescription*/, null /*afterOptionsDescription*/));

        var after = You_can_learn_about_all_of_the_compiler_options_at_0.Localize(locale, "https://aka.ms/tsc");
        output.AddRange(GenerateSectionOptionsOutput(sys, locale, COMMON_COMPILER_OPTIONS.Localize(locale), configOpts.ToArray(), false /*subCategory*/, null /*beforeOptionsDescription*/, after /*afterOptionsDescription*/));

        foreach (var chunk in output)
        {
            sys.Writer().Write(chunk);
        }
    }

    /// <summary>Go: <c>func printAllHelp(sys System, locale locale.Locale, options []*tsoptions.CommandLineOption)</c>（help.go:113-139）。</summary>
    internal static void PrintAllHelp(System sys, Loc locale, CommandLineOption[] options)
    {
        var output = new List<string>();
        var msg = X_tsc_Colon_The_TypeScript_Compiler.Localize(locale) + " - " + Version_0.Localize(locale, Version());
        output.AddRange(GetHeader(sys, msg));

        // ALL COMPILER OPTIONS section
        var afterCompilerOptions = You_can_learn_about_all_of_the_compiler_options_at_0.Localize(locale, "https://aka.ms/tsc");
        output.AddRange(GenerateSectionOptionsOutput(sys, locale, ALL_COMPILER_OPTIONS.Localize(locale), options, true, null, afterCompilerOptions));

        // WATCH OPTIONS section
        var beforeWatchOptions = Including_watch_w_will_start_watching_the_current_project_for_the_file_changes_Once_set_you_can_config_watch_mode_with_Colon.Localize(locale);
        output.AddRange(GenerateSectionOptionsOutput(sys, locale, WATCH_OPTIONS.Localize(locale), OptionsForWatch, false, beforeWatchOptions, null));

        // BUILD OPTIONS section
        var beforeBuildOptions = Using_build_b_will_make_tsc_behave_more_like_a_build_orchestrator_than_a_compiler_This_is_used_to_trigger_building_composite_projects_which_you_can_learn_more_about_at_0.Localize(locale, "https://aka.ms/tsc-composite-builds");
        var buildOptions = Filter(OptionsForBuild, option => option != TscBuildOption);
        output.AddRange(GenerateSectionOptionsOutput(sys, locale, BUILD_OPTIONS.Localize(locale), buildOptions, false, beforeBuildOptions, null));

        foreach (var chunk in output)
        {
            sys.Writer().Write(chunk);
        }
    }

    /// <summary>Go: <c>func PrintBuildHelp(sys System, locale locale.Locale, buildOptions []*tsoptions.CommandLineOption)</c>（help.go:141-152）。</summary>
    public static void PrintBuildHelp(System sys, Loc locale, CommandLineOption[] buildOptions)
    {
        var output = new List<string>();
        output.AddRange(GetHeader(sys, X_tsc_Colon_The_TypeScript_Compiler.Localize(locale) + " - " + Version_0.Localize(locale, Version())));
        var before = Using_build_b_will_make_tsc_behave_more_like_a_build_orchestrator_than_a_compiler_This_is_used_to_trigger_building_composite_projects_which_you_can_learn_more_about_at_0.Localize(locale, "https://aka.ms/tsc-composite-builds");
        var options = Filter(buildOptions, option => option != TscBuildOption);
        output.AddRange(GenerateSectionOptionsOutput(sys, locale, BUILD_OPTIONS.Localize(locale), options, false, before, null));

        foreach (var chunk in output)
        {
            sys.Writer().Write(chunk);
        }
    }
}

public static partial class Tsc
{
    /// <summary>Go: <c>func generateSectionOptionsOutput(...) (output []string)</c>（help.go:154-196）。</summary>
    internal static string[] GenerateSectionOptionsOutput(
        System sys,
        Loc locale,
        string sectionName,
        CommandLineOption[] options,
        bool subCategory,
        string? beforeOptionsDescription,
        string? afterOptionsDescription)
    {
        var output = new List<string>();
        output.Add(CreateColors(sys).Bold(sectionName));
        output.Add("\n");
        output.Add("\n");

        if (beforeOptionsDescription is not null)
        {
            output.Add(beforeOptionsDescription);
            output.Add("\n");
            output.Add("\n");
        }
        if (!subCategory)
        {
            output.AddRange(GenerateGroupOptionOutput(sys, locale, options));
            if (afterOptionsDescription is not null)
            {
                output.Add(afterOptionsDescription);
                output.Add("\n");
                output.Add("\n");
            }
            return output.ToArray();
        }
        var categoryMap = new Dictionary<string, List<CommandLineOption>>();
        var categoryOrder = new List<string>();
        foreach (var option in options)
        {
            if (option.Category is null)
            {
                continue;
            }
            var curCategory = option.Category.Localize(locale);
            if (!categoryMap.TryGetValue(curCategory, out var value))
            {
                categoryOrder.Add(curCategory);
                value = [];
                categoryMap[curCategory] = value;
            }
            value.Add(option);
        }
        foreach (var key in categoryOrder)
        {
            var value = categoryMap[key];
            output.Add("### ");
            output.Add(key);
            output.Add("\n");
            output.Add("\n");
            output.AddRange(GenerateGroupOptionOutput(sys, locale, value.ToArray()));
        }
        if (afterOptionsDescription is not null)
        {
            output.Add(afterOptionsDescription);
            output.Add("\n");
            output.Add("\n");
        }

        return output.ToArray();
    }

    /// <summary>Go: <c>func generateGroupOptionOutput(sys System, locale locale.Locale, optionsList []*tsoptions.CommandLineOption) []string</c>（help.go:198-219）。</summary>
    internal static string[] GenerateGroupOptionOutput(System sys, Loc locale, CommandLineOption[] optionsList)
    {
        var maxLength = 0;
        foreach (var option in optionsList)
        {
            var curLenght = GetDisplayNameTextOfOption(option).Length;
            maxLength = Math.Max(curLenght, maxLength);
        }

        // left part should be right align, right part should be left align

        // assume 2 space between left margin and left part.
        var rightAlignOfLeftPart = maxLength + 2;
        // assume 2 space between left and right part
        var leftAlignOfRightPart = rightAlignOfLeftPart + 2;

        var lines = new List<string>();
        foreach (var option in optionsList)
        {
            var tmp = GenerateOptionOutput(sys, locale, option, rightAlignOfLeftPart, leftAlignOfRightPart);
            lines.AddRange(tmp);
        }

        // make sure always a blank line in the end.
        if (lines.Count < 2 || lines[^2] != "\n")
        {
            lines.Add("\n");
        }

        return lines.ToArray();
    }

    /// <summary>Go: <c>func generateOptionOutput(...) []string</c>（help.go:221-288）。</summary>
    internal static string[] GenerateOptionOutput(
        System sys,
        Loc locale,
        CommandLineOption option,
        int rightAlignOfLeft,
        int leftAlignOfRight)
    {
        var text = new List<string>();
        var colors = CreateColors(sys);

        // name and description
        var name = GetDisplayNameTextOfOption(option);

        // value type and possible value
        var valueCandidates = GetValueCandidate(sys, locale, option);

        string defaultValueDescription;
        if (option.DefaultValueDescription is Message msg)
        {
            defaultValueDescription = msg.Localize(locale);
        }
        else
        {
            // Go 的 `core.IfElse(cond, option.Elements(), option)`：`Elements()` 在 Kind 为 list/listOrElement
            // 时由 decls 的 element 表返回（非 nil；为 nil 时 Go 会 panic，见 CommandLineOption.cs 的既有实现）。
            defaultValueDescription = FormatDefaultValue(
                option.DefaultValueDescription,
                option.Kind is CommandLineOptionKind.List or CommandLineOptionKind.ListOrElement ? option.Elements()! : option);
        }

        var terminalWidth = sys.GetWidthOfTerminal();

        if (terminalWidth >= 80)
        {
            var description = "";
            if (option.Description is not null)
            {
                description = option.Description.Localize(locale);
            }
            text.AddRange(GetPrettyOutput(colors, name, description, rightAlignOfLeft, leftAlignOfRight, terminalWidth, true /*colorLeft*/));
            text.Add("\n");
            if (ShowAdditionalInfoOutput(valueCandidates, option))
            {
                if (valueCandidates is not null)
                {
                    text.AddRange(GetPrettyOutput(colors, valueCandidates._valueType, valueCandidates._possibleValues, rightAlignOfLeft, leftAlignOfRight, terminalWidth, false /*colorLeft*/));
                    text.Add("\n");
                }
                if (defaultValueDescription != "")
                {
                    text.AddRange(GetPrettyOutput(colors, X_default_Colon.Localize(locale), defaultValueDescription, rightAlignOfLeft, leftAlignOfRight, terminalWidth, false /*colorLeft*/));
                    text.Add("\n");
                }
            }
            text.Add("\n");
        }
        else
        {
            text.Add(colors.Blue(name));
            text.Add("\n");
            if (option.Description is not null)
            {
                text.Add(option.Description.Localize(locale));
            }
            text.Add("\n");
            if (ShowAdditionalInfoOutput(valueCandidates, option))
            {
                if (valueCandidates is not null)
                {
                    text.Add(valueCandidates._valueType);
                    text.Add(" ");
                    text.Add(valueCandidates._possibleValues);
                }
                if (defaultValueDescription != "")
                {
                    if (valueCandidates is not null)
                    {
                        text.Add("\n");
                    }
                    text.Add(X_default_Colon.Localize(locale));
                    text.Add(" ");
                    text.Add(defaultValueDescription);
                }

                text.Add("\n");
            }
            text.Add("\n");
        }

        return text.ToArray();
    }
}

public static partial class Tsc
{
    /// <summary>Go: <c>func formatDefaultValue(defaultValue any, option *tsoptions.CommandLineOption) string</c>（help.go:290-303）。</summary>
    internal static string FormatDefaultValue(object? defaultValue, CommandLineOption option)
    {
        if (defaultValue is null || Equals(defaultValue, Tristate.TSUnknown))
        {
            return "undefined";
        }

        if (option.Kind == CommandLineOptionKind.Enum)
        {
            // e.g. ScriptTarget.ES2015 -> "es6/es2015"
            var names = new List<string>();
            foreach (var (name, value) in option.EnumMap()!.Entries())
            {
                if (Equals(value, defaultValue))
                {
                    names.Add(name);
                }
            }
            return string.Join("/", names);
        }
        // Go 的 `fmt.Sprintf("%v", defaultValue)`：bool 走 Go 的 %v → "true"/"false"（不是 C# 的 "True"/"False"）；
        // 数字用不变文化（Go 的 %v 与区域无关）；枚举走 Go 的 stringer，而 §3.3 保证合法值上 ToString() 与之逐字相同。
        return defaultValue switch
        {
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => defaultValue.ToString() ?? "",
        };
    }

    /// <summary>Go: <c>func showAdditionalInfoOutput(valueCandidates *valueCandidate, option *tsoptions.CommandLineOption) bool</c>（help.go:311-324）。</summary>
    internal static bool ShowAdditionalInfoOutput(ValueCandidate? valueCandidates, CommandLineOption option)
    {
        if (option.Category == Command_line_Options)
        {
            return false;
        }
        if (valueCandidates is not null && valueCandidates._possibleValues == "string" &&
            (option.DefaultValueDescription is null ||
                Equals(option.DefaultValueDescription, "false") ||
                Equals(option.DefaultValueDescription, "n/a")))
        {
            return false;
        }
        return true;
    }

    /// <summary>Go: <c>func getValueCandidate(sys System, locale locale.Locale, option *tsoptions.CommandLineOption) *valueCandidate</c>（help.go:326-357）。</summary>
    internal static ValueCandidate? GetValueCandidate(System sys, Loc locale, CommandLineOption option)
    {
        // option.type might be "string" | "number" | "boolean" | "object" | "list" | Map<string, number | string>
        // string -- any of: string
        // number -- any of: number
        // boolean -- any of: boolean
        // object -- null
        // list -- one or more: , content depends on `option.element.type`, the same as others
        // Map<string, number | string> -- any of: key1, key2, ....
        if (option.Kind == CommandLineOptionKind.Object)
        {
            return null;
        }

        var res = new ValueCandidate();
        if (option.Kind == CommandLineOptionKind.ListOrElement)
        {
            // assert(option.type !== "listOrElement")
            Debug.Fail("no value candidate for list or element");
        }

        switch (option.Kind)
        {
            case CommandLineOptionKind.String:
            case CommandLineOptionKind.Number:
            case CommandLineOptionKind.Boolean:
                res._valueType = X_type_Colon.Localize(locale);
                break;
            case CommandLineOptionKind.List:
                res._valueType = X_one_or_more_Colon.Localize(locale);
                break;
            default:
                res._valueType = X_one_of_Colon.Localize(locale);
                break;
        }

        res._possibleValues = GetPossibleValues(option);

        return res;
    }

    /// <summary>Go: <c>func getPossibleValues(option *tsoptions.CommandLineOption) string</c>（help.go:359-390）。</summary>
    internal static string GetPossibleValues(CommandLineOption option)
    {
        switch (option.Kind)
        {
            case CommandLineOptionKind.String:
            case CommandLineOptionKind.Number:
            case CommandLineOptionKind.Boolean:
                return option.Kind.String();
            case CommandLineOptionKind.List:
            case CommandLineOptionKind.ListOrElement:
                return GetPossibleValues(option.Elements()!);
            case CommandLineOptionKind.Object:
                return "";
            default:
            {
                // Map<string, number | string>
                // Group synonyms: es6/es2015
                var enumMap = option.EnumMap()!;
                var inverted = NewOrderedMapWithSizeHint<object, string[]>(enumMap.Size());
                var deprecatedKeys = option.DeprecatedKeys();

                foreach (var (name, value) in enumMap.Entries())
                {
                    if (deprecatedKeys is null || !deprecatedKeys.Has(name))
                    {
                        inverted.Set(value!, [.. inverted.GetOrZero(value!) ?? [], name]);
                    }
                }
                var syns = new List<string>();
                foreach (var synonyms in inverted.Values())
                {
                    syns.Add(string.Join("/", synonyms ?? []));
                }
                return string.Join(", ", syns);
            }
        }
    }

    /// <summary>Go: <c>func getPrettyOutput(colors *colors, left string, right string, ...) []string</c>（help.go:392-421）。</summary>
    internal static string[] GetPrettyOutput(Colors colors, string left, string right, int rightAlignOfLeft, int leftAlignOfRight, int terminalWidth, bool colorLeft)
    {
        // !!! How does terminalWidth interact with UTF-8 encoding? Strada just assumed UTF-16.
        var res = new List<string>(4);
        var isFirstLine = true;
        var remainRight = right;
        var rightCharacterNumber = terminalWidth - leftAlignOfRight;
        while (remainRight.Length > 0)
        {
            string curLeft;
            if (isFirstLine)
            {
                curLeft = left.PadLeft(rightAlignOfLeft);
                curLeft = curLeft.PadRight(leftAlignOfRight);
                if (colorLeft)
                {
                    curLeft = colors.Blue(curLeft);
                }
            }
            else
            {
                curLeft = new string(' ', leftAlignOfRight);
            }

            var idx = Math.Min(rightCharacterNumber, remainRight.Length);
            var curRight = remainRight[..idx];
            remainRight = remainRight[idx..];
            res.Add(curLeft);
            res.Add(curRight);
            res.Add("\n");
            isFirstLine = false;
        }
        return res.ToArray();
    }

    /// <summary>Go: <c>func getDisplayNameTextOfOption(option *tsoptions.CommandLineOption) string</c>（help.go:423-425）。</summary>
    internal static string GetDisplayNameTextOfOption(CommandLineOption option) =>
        "--" + option.Name + IfElse(option.ShortName != "", ", -" + option.ShortName, "");
}

/// <summary>Go: <c>type valueCandidate struct</c>（help.go:305-309）。</summary>
internal sealed class ValueCandidate
{
    // "one or more" or "any of"
    internal string _valueType = "";

    internal string _possibleValues = "";
}

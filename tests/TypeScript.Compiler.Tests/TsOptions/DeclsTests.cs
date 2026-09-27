// ported from: internal/tsoptions/decls_test.go
//
// 映射约定（docs/architecture.md §3.4/§3.6/§5）：
// 1) Go 的 `strings.ToLower`（JS 语义、完整多字符映射）→ `StringUtil.ToLowerJS`（§3.4），不用 .NET 的 ToLower。
// 2) Go 用 `reflect.TypeFor[core.CompilerOptions]()` 遍历**导出字段**、读 struct tag `json:"<name>,omitzero"`；
//    C# 禁反射（§3.6）→ 改成遍历 TsOptionsConfig 生成的显式字段表 `TsOptions.CompilerOptionFields`
//    （与 reflect 版等价：顺序 = Go struct 字段顺序、只收导出字段）。
//    **偏离**：C# 的 CompilerOptions 不挂 json tag（docs/json.md §4：本轮不实现 JSON 映射），所以"json tag 名"
//    按 Core/CompilerOptions.cs 文件头写明、并逐条照 Go tag 移植的规则推算（字段名首字母小写）。
//    这条推算本身在 artifacts/DeclsTables/check_decls.mjs 里用 Go 源的真实 tag 交叉核对过。
// 3) Go 用 t.Errorf 累积错误（非致命）；xunit 的 Assert.Fail 立即失败 → 这里把不一致收进 list，最后一次性断言，
//    保留"一次报告全部不一致"的行为。
// 4) Go 的 `field.IsExported()` 判断：字段表只收导出字段，故不需要对应分支。
using System.Collections.Generic;
using TypeScript.Core;
using TypeScript.TsOptions;
using Xunit;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TsOptions.TsOptions;

namespace TypeScript.Tests;

public class DeclsTests
{
    [Fact]
    public void CompilerOptionsDeclaration()
    {
        // Go: decls := make(map[string]*tsoptions.CommandLineOption)
        //     for _, decl := range tsoptions.OptionsDeclarations { decls[strings.ToLower(decl.Name)] = decl }
        var decls = new Dictionary<string, CommandLineOption>();
        foreach (var decl in OptionsDeclarations)
        {
            decls[ToLowerJS(decl.Name)] = decl;
        }

        string[] internalOptions =
        [
            "allowNonTsExtensions",
            "build",
            "configFilePath",
            "noDtsResolution",
            "noEmitForJsFiles",
            "pathsBasePath",
            "suppressOutputPathCheck",
            "build",
        ];

        var internalOptionsMap = new Dictionary<string, string>();
        foreach (var opt in internalOptions)
        {
            internalOptionsMap[ToLowerJS(opt)] = opt;
        }

        var failures = new List<string>();
        foreach (var field in CompilerOptionFields)
        {
            var lowerName = ToLowerJS(field.Name);

            if (!decls.TryGetValue(lowerName, out var decl))
            {
                if (internalOptionsMap.TryGetValue(lowerName, out var internalName))
                {
                    CheckCompilerOptionJsonTagName(failures, field, internalName);
                    continue;
                }
                failures.Add($"CompilerOptions.{field.Name} has no options declaration");
                continue;
            }
            decls.Remove(lowerName);

            CheckCompilerOptionJsonTagName(failures, field, decl.Name);
        }

        string[] skippedOptions = ["plugins"];

        foreach (var opt in skippedOptions)
        {
            decls.Remove(ToLowerJS(opt));
        }

        foreach (var decl in decls.Values)
        {
            failures.Add($"Option declaration {decl.Name} is not present in CompilerOptions");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // Go: checkCompilerOptionJsonTagName（decls_test.go:63-72）：want := name + ",omitzero"；got := field.Tag.Get("json")
    private static void CheckCompilerOptionJsonTagName(List<string> failures, CompilerOptionField field, string name)
    {
        var want = name + ",omitzero";
        var got = JsonTagName(field.Name) + ",omitzero";
        if (got != want)
        {
            failures.Add($"Field {field.Name} has json tag {got}, but the option declaration has name {want}");
        }
    }

    // Go 的 struct tag 名 = 字段名的 camelCase（Core/CompilerOptions.cs 文件头，逐条照 Go 的 tag 移植）。
    // 注意**开头连续的大写缩写**要整体小写：ESModuleInterop → "esModuleInterop"、TsBuildInfoFile → "tsBuildInfoFile"，
    // 而 AllowJs → "allowJs"。这条规则用 Go 源里 132 个真实 tag 逐一核对过
    // （artifacts/DeclsTables/check_decls.mjs 的 "每个 CompilerOptions 导出字段都能对上同名声明" 一节）。
    private static string JsonTagName(string goFieldName)
    {
        var acronym = LeadingAcronym(goFieldName);
        var keepLastUpper = acronym.Length > 1
            && goFieldName.Length > acronym.Length
            && char.IsLower(goFieldName[acronym.Length]);
        var head = keepLastUpper ? acronym[..^1] : acronym;
        return head.ToLowerInvariant() + goFieldName[(keepLastUpper ? acronym.Length - 1 : acronym.Length)..];
    }

    private static string LeadingAcronym(string goFieldName)
    {
        var end = 0;
        while (end < goFieldName.Length && char.IsUpper(goFieldName[end]))
        {
            end++;
        }
        return goFieldName[..end];
    }
}

// ported from: internal/execute/tsc/init.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) 本命名空间里有名为 `System` 的接口（Go 的 `tsc.System`），会遮蔽根命名空间 `System` →
//    本文件一律 `using global::System;` 且禁止 `System.` 前缀（同 Compile.cs 偏离 1）。
// 2) `io.Writer` → `System.IO.TextWriter`；`fmt.Fprint(w, s)` → `w.Write(s)`；
//    `strings.Join(result, "\n")` → `string.Join("\n", result)`。
// 3) Go 的 `reflect.ValueOf(value).Kind() == reflect.Slice` → `value is Array`（§3.6 禁止反射；
//    tsoptions 解析出的 list 值在 C# 里就是 `object?[]`（非空 list）或 `string[]`（空 list，
//    见 CommandLineParser.cs:241），`Array` 一次覆盖全部数组，`foreach` 取出的即 Go 的 `rval.Index(i).Interface()`）。
// 4) `json.MarshalIndent(value, "", "")` 在 C# 里返回 `byte[]`（json 包是 UTF-8 字节语义），
//    `string(b)` → `Encoding.UTF8.GetString(b)`。
// 5) Go 的 `b, err := json.MarshalIndent(...); if err != nil { panic(...) }` → C# 的 json 包直接抛
//    `JsonException`（§3.2：(T, error) 正常路径返回值、错误路径抛异常），本函数不额外捕获、也不再 Debug.Fail。
// 6) Go 的 `value == v`（enum map 反查）与 `options.Get(...)` 的 `(V, bool)` → `object.Equals(a, b)`（装箱值按
//    "动态类型 + 值"比较，与 Go 的接口比较一致）与 `(V? Value, bool Ok)`（§3.2）。Go 对不可比较的动态类型会
//    panic，C# 返回 false；这里的两侧都只可能是标量。
// 7) `panic` → `Debug.Fail`（§3.2）。
// 8) Go 的 `_ = sys.FS().WriteFile(file, ...)`（忽略 error）→ C# 的 `void WriteFile`（失败抛 IOException 家族，
//    见 Vfs/FS.cs）。
// 9) `slices.Index`/`slices.Delete`（就地删一个元素）→ `List<string>.IndexOf`/`RemoveAt`；
//    `strings.Repeat` 无使用。
// 10) Go 函数内的局部类型 `type commented int` + iota 常量 → 文件级 internal `enum Commented`（C# 没有函数内类型），
//     成员名 Never/Always/Optional 对应 commentedNever/commentedAlways/commentedOptional，数值 0/1/2 不变。
// 11) Go 的包级未导出函数 → internal static 方法（PascalCase）；`option == nil` 判断下的 Go 会 nil panic 的点，
//     C# 用 `!`（与 CommandLineOption.cs / ParsedCommandLine.cs 既有写法一致）。
using global::System;
using global::System.Collections.Generic;
using global::System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
using Loc = TypeScript.Locale.Locale;
using static TypeScript.Ast.Ast;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Json.Json;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;

namespace TypeScript.Execute.Tsc;

public static partial class Tsc
{
    /// <summary>Go: <c>func WriteConfigFile(sys System, locale locale.Locale, reportDiagnostic DiagnosticReporter, options *collections.OrderedMap[string, any])</c>（init.go:20-31）。</summary>
    public static void WriteConfigFile(System sys, Loc locale, DiagnosticReporter reportDiagnostic, OrderedMap<string, object?> options)
    {
        var getCurrentDirectory = sys.GetCurrentDirectory();
        var file = NormalizePath(CombinePaths(getCurrentDirectory, "tsconfig.json"));
        if (sys.FS().FileExists(file))
        {
            reportDiagnostic(NewCompilerDiagnostic(A_tsconfig_json_file_is_already_defined_at_Colon_0, file));
        }
        else
        {
            sys.FS().WriteFile(file, GenerateTSConfig(options, locale));
            var output = new List<string> { "\n" };
            output.AddRange(GetHeader(sys, "Created a new tsconfig.json"));
            output.Add("You can learn more at https://aka.ms/tsconfig");
            output.Add("\n");
            sys.Writer().Write(string.Join("", output));
        }
    }

    /// <summary>Go: <c>func generateTSConfig(options *collections.OrderedMap[string, any], locale locale.Locale) string</c>（init.go:33-215）。</summary>
    internal static string GenerateTSConfig(OrderedMap<string, object?> options, Loc locale)
    {
        const string tab = "  ";
        var result = new List<string>();

        var allSetOptions = new List<string>(options.Size());
        foreach (var k in options.Keys())
        {
            if (k != "init" && k != "help" && k != "watch")
            {
                allSetOptions.Add(k);
            }
        }

        void EmitHeader(Message header)
        {
            result.Add(tab + tab + "// " + header.Localize(locale));
        }
        void Newline()
        {
            result.Add("");
        }
        void Push(params string[] args)
        {
            result.AddRange(args);
        }

        string FormatSingleValue(object? value, OrderedMap<string, object?>? enumMap)
        {
            if (enumMap is not null)
            {
                var found = false;
                foreach (var (k, v) in enumMap.Entries())
                {
                    if (Equals(value, v))
                    {
                        value = k;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Debug.Fail($"No matching value of {value}");
                }
            }

            var b = MarshalIndent(value, "", "");
            return Encoding.UTF8.GetString(b);
        }

        string FormatValueOrArray(string settingName, object? value)
        {
            CommandLineOption? option = null;
            foreach (var decl in OptionsDeclarations)
            {
                if (decl.Name == settingName)
                {
                    option = decl;
                }
            }
            if (option is null)
            {
                Debug.Fail("No option named " + settingName);
            }

            if (value is Array array)
            {
                OrderedMap<string, object?>? enumMap = null;
                if (option!.Elements() is { } elemOption)
                {
                    enumMap = elemOption.EnumMap();
                }

                var elems = new List<string>();
                foreach (var elem in array)
                {
                    elems.Add(FormatSingleValue(elem, enumMap));
                }
                return "[" + string.Join(", ", elems) + "]";
            }
            else
            {
                return FormatSingleValue(value, option!.EnumMap());
            }
        }

        // commentedNever': Never comment this out
        // commentedAlways': Always comment this out, even if it's on commandline
        // commentedOptional': Comment out unless it's on commandline
        void EmitOption(string setting, object? defaultValue, Commented commented)
        {
            if ((int)commented > 2)
            {
                Debug.Fail("should not happen: invalid `commented`, must be a bug.");
            }

            var existingOptionIndex = allSetOptions.IndexOf(setting);
            if (existingOptionIndex >= 0)
            {
                allSetOptions.RemoveAt(existingOptionIndex);
            }

            bool comment;
            switch (commented)
            {
                case Commented.Always:
                    comment = true;
                    break;
                case Commented.Never:
                    comment = false;
                    break;
                default:
                    comment = !options.Has(setting);
                    break;
            }

            var (value, ok) = options.Get(setting);
            if (!ok)
            {
                value = defaultValue;
            }

            if (comment)
            {
                Push(tab + tab + "// \"" + setting + "\": " + FormatValueOrArray(setting, value) + ",");
            }
            else
            {
                Push(tab + tab + "\"" + setting + "\": " + FormatValueOrArray(setting, value) + ",");
            }
        }

        Push("{");
        Push(tab + "// " + Visit_https_Colon_Slash_Slashaka_ms_Slashtsconfig_to_read_more_about_this_file.Localize(locale));
        Push(tab + "\"compilerOptions\": {");

        EmitHeader(File_Layout);
        EmitOption("rootDir", "./src", Commented.Optional);
        EmitOption("outDir", "./dist", Commented.Optional);

        Newline();

        EmitHeader(Environment_Settings);
        EmitHeader(See_also_https_Colon_Slash_Slashaka_ms_Slashtsconfig_Slashmodule);
        EmitOption("module", ModuleKind.NodeNext, Commented.Never);
        EmitOption("target", ScriptTarget.ESNext, Commented.Never);
        EmitOption("types", Array.Empty<object?>() /* []any{} */, Commented.Never);
        var (lib, hasLib) = options.Get("lib");
        if (hasLib)
        {
            EmitOption("lib", lib, Commented.Never);
        }
        EmitHeader(For_nodejs_Colon);
        Push(tab + tab + "// \"lib\": [\"esnext\"],");
        Push(tab + tab + "// \"types\": [\"node\"],");
        EmitHeader(X_and_npm_install_D_types_Slashnode);

        Newline();

        EmitHeader(Other_Outputs);
        EmitOption("sourceMap" /*defaultValue*/, true, Commented.Never);
        EmitOption("declaration" /*defaultValue*/, true, Commented.Never);
        EmitOption("declarationMap" /*defaultValue*/, true, Commented.Never);

        Newline();

        EmitHeader(Stricter_Typechecking_Options);
        EmitOption("noUncheckedIndexedAccess" /*defaultValue*/, true, Commented.Never);
        EmitOption("exactOptionalPropertyTypes" /*defaultValue*/, true, Commented.Never);

        Newline();

        EmitHeader(Style_Options);
        EmitOption("noImplicitReturns" /*defaultValue*/, true, Commented.Optional);
        EmitOption("noImplicitOverride" /*defaultValue*/, true, Commented.Optional);
        EmitOption("noUnusedLocals" /*defaultValue*/, true, Commented.Optional);
        EmitOption("noUnusedParameters" /*defaultValue*/, true, Commented.Optional);
        EmitOption("noFallthroughCasesInSwitch" /*defaultValue*/, true, Commented.Optional);
        EmitOption("noPropertyAccessFromIndexSignature" /*defaultValue*/, true, Commented.Optional);

        Newline();

        EmitHeader(Recommended_Options);
        EmitOption("strict" /*defaultValue*/, true, Commented.Never);
        EmitOption("jsx", JsxEmit.ReactJSX, Commented.Never);
        EmitOption("verbatimModuleSyntax" /*defaultValue*/, true, Commented.Never);
        EmitOption("isolatedModules" /*defaultValue*/, true, Commented.Never);
        EmitOption("noUncheckedSideEffectImports" /*defaultValue*/, true, Commented.Never);
        EmitOption("moduleDetection", ModuleDetectionKind.Force, Commented.Never);
        EmitOption("skipLibCheck" /*defaultValue*/, true, Commented.Never);

        // Write any user-provided options we haven't already
        if (allSetOptions.Count > 0)
        {
            Newline();
            while (allSetOptions.Count > 0)
            {
                EmitOption(allSetOptions[0], options.GetOrZero(allSetOptions[0]), Commented.Never);
            }
        }

        Push(tab + "}");
        Push("}");
        Push("");

        return string.Join("\n", result);
    }
}

/// <summary>Go: <c>type commented int</c>（init.go:93-99 的函数内类型）与常量 <c>commentedNever</c>/<c>commentedAlways</c>/<c>commentedOptional</c>。</summary>
internal enum Commented
{
    Never = 0,
    Always = 1,
    Optional = 2,
}

// ported from: internal/tsoptions/tsconfigparsing.go:466-780（CommandLineOptionNameMap、对象/数组字面量 →
// JSON 值、ParseConfigFileTextToJson / ParseJsonSourceFileConfigFileContent / ParseJsonConfigFileContent 入口）
//
// 与 Go 的差异：
// - Go 的 `type Expression = Node`（类型别名）→ C# 里就是 `Node`（Ast 没有单独的 Expression 类）。
// - Go 的 `struct{}{}`（空 JSON 根）→ C# `new object()`（`ConvertToJson` 在 rootExpression 为空时返回它，
//   与 Go 一样只是"非 nil 的空原始值"，后续 `ParseJsonToStringKey` 会把它当空 map）。
// - `map[string]any` → `Dictionary<string, object?>`；键排序用 `string.CompareOrdinal`（Go 的 `slices.Sorted`）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Module;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.JsNum.JsNum;
using static TypeScript.TSPath.TSPath;
using Diagnostic = TypeScript.Ast.Diagnostic;
using TPath = TypeScript.TSPath.Path;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    // Go: type tsConfigOptions struct
    internal sealed class TsConfigOptions
    {
        public Dictionary<string, string[]>? Prop;
        public ProjectReference[]? References;
        public string NotDefined = "";
    }

    // Go: type CommandLineOptionNameMap map[string]*CommandLineOption（命名 map 类型 + 方法）
    public sealed class CommandLineOptionNameMap
    {
        private readonly Dictionary<string, CommandLineOption> _map;

        internal CommandLineOptionNameMap(Dictionary<string, CommandLineOption> map)
        {
            _map = map;
        }

        // Go: func (m CommandLineOptionNameMap) Get(name string) *CommandLineOption
        public CommandLineOption? Get(string name)
        {
            if (!_map.TryGetValue(name, out var opt))
            {
                _map.TryGetValue(TypeScript.StringUtil.StringUtil.ToLowerJS(name), out opt);
            }

            return opt;
        }

        // Go: func (m CommandLineOptionNameMap) GetSpellingSuggestion(name string) *CommandLineOption
        // 裸名会被本类的同名方法（1 参数）遮蔽，`Core.` 前缀又会解析成命名空间 TypeScript.Core
        // （docs/architecture.md §2），故用完整的类型名限定。
        public CommandLineOption? GetSpellingSuggestion(string name) =>
            TypeScript.Core.Core.GetSpellingSuggestion(
                name,
                _map.Values,
                option => option.Name,
                (a, b) => string.CompareOrdinal(a.Name, b.Name));

        internal Dictionary<string, CommandLineOption> Entries() => _map;
    }

    // Go: func commandLineOptionsToMap(compilerOptions []*CommandLineOption) CommandLineOptionNameMap
    internal static CommandLineOptionNameMap CommandLineOptionsToMap(CommandLineOption[] compilerOptions)
    {
        var result = new Dictionary<string, CommandLineOption>(compilerOptions.Length * 2);
        foreach (var option in compilerOptions)
        {
            result[option.Name] = option;
            result[TypeScript.StringUtil.StringUtil.ToLowerJS(option.Name)] = option;
        }

        return new CommandLineOptionNameMap(result);
    }

    // Go: var CommandLineCompilerOptionsMap CommandLineOptionNameMap = commandLineOptionsToMap(OptionsDeclarations)
    // （依赖 TsOptionsModel 的 OptionsDeclarations，后者又在另一 partial 文件里 → 用 Lazy 保证初始化顺序无关）
    private static readonly Lazy<CommandLineOptionNameMap> _commandLineCompilerOptionsMap =
        new(() => CommandLineOptionsToMap(OptionsDeclarations));

    public static CommandLineOptionNameMap CommandLineCompilerOptionsMap => _commandLineCompilerOptionsMap.Value;

    // Go: func convertMapToOptions[O optionParser](compilerOptions *collections.OrderedMap[string, any], result O) O
    // this assumes any `key`, `value` pair in `options` will have `value` already be the correct type. this function should no error handling
    internal static T ConvertMapToOptions<T>(OrderedMap<string, object?> compilerOptions, T result)
        where T : IOptionParser
    {
        foreach (var (key, value) in compilerOptions.Entries())
        {
            result.ParseOption(key, value);
        }

        return result;
    }

    // Go: func convertOptionsFromJson[O optionParser](optionsNameMap CommandLineOptionNameMap, jsonOptions any, basePath string, result O) (O, []*ast.Diagnostic)
    internal static (T Result, Diagnostic[] Errors) ConvertOptionsFromJson<T>(
        CommandLineOptionNameMap optionsNameMap,
        object? jsonOptions,
        string basePath,
        T result)
        where T : IOptionParser
    {
        if (jsonOptions is null)
        {
            return (result, []);
        }

        if (jsonOptions is not OrderedMap<string, object?> jsonMap)
        {
            // !!! probably should be an error
            return (result, []);
        }

        var errors = new List<Diagnostic>();
        foreach (var (key, value) in jsonMap.Entries())
        {
            var opt = optionsNameMap.Get(key);
            if (opt is not null && opt.Name != key)
            {
                // Case-insensitive match found but exact case doesn't match - provide "did you mean" suggestion
                errors.Add(CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(null, null, result.UnknownDidYouMeanDiagnostic()!, key, opt.Name));
                continue;
            }

            if (opt is null)
            {
                errors.Add(CreateUnknownOptionError(key, result.UnknownOptionDiagnostic()!, "", null, null, null, result.UnknownDidYouMeanDiagnostic(), optionsNameMap));
                continue;
            }

            var (convertJson, err) = ConvertJsonOption(opt, value, basePath, null, null, null);
            errors.AddRange(err ?? []);
            var compilerOptionsErr = result.ParseOption(key, convertJson);
            errors.AddRange(compilerOptionsErr);
        }

        return (result, [.. errors]);
    }

    // Go: func convertArrayLiteralExpressionToJson(...)
    internal static (object? Value, Diagnostic[] Errors) ConvertArrayLiteralExpressionToJson(
        SourceFile sourceFile,
        Node[] elements,
        CommandLineOption? elementOption,
        bool returnValue)
    {
        if (!returnValue)
        {
            foreach (var element in elements)
            {
                ConvertPropertyValueToJson(sourceFile, element, elementOption, returnValue, null);
            }

            return (null, []);
        }

        // Filter out invalid values
        if (elements.Length == 0)
        {
            // Always return an empty array, even if elements is nil.
            // The parser will produce nil slices instead of allocating empty ones.
            return (new object?[0], []);
        }

        var errors = new List<Diagnostic>();
        var value = new List<object?>();
        foreach (var element in elements)
        {
            var (convertedValue, err) = ConvertPropertyValueToJson(sourceFile, element, elementOption, returnValue, null);
            errors.AddRange(err);
            if (convertedValue is not null)
            {
                value.Add(convertedValue);
            }
        }

        // Go 的 `value []any` 在元组第一个元素（any/object?）位置上推断不出集合表达式的目标类型，
        // 故先落成显式数组类型。
        object?[] valueArray = [.. value];
        return (valueArray, [.. errors]);
    }

    // Go: func directoryOfCombinedPath(fileName string, basePath string) string
    internal static string DirectoryOfCombinedPath(string fileName, string basePath) =>
        // Use the `getNormalizedAbsolutePath` function to avoid canonicalizing the path, as it must remain noncanonical
        // until consistent casing errors are reported
        GetDirectoryPath(GetNormalizedAbsolutePath(fileName, basePath));

    // ParseConfigFileTextToJson parses the text of the tsconfig.json file
    // fileName is the path to the config file
    // jsonText is the text of the config file
    // Go: func ParseConfigFileTextToJson(fileName string, path tspath.Path, jsonText string) (any, []*ast.Diagnostic)
    public static (object? Config, Diagnostic[] Errors) ParseConfigFileTextToJson(string fileName, TPath path, string jsonText)
    {
        var jsonSourceFile = TypeScript.Parser.Parser.ParseSourceFile(
            new SourceFileParseOptions
            {
                FileName = fileName,
                Path = path,
            },
            jsonText,
            ScriptKind.JSON);
        var (config, errors) = ConvertConfigFileToObject(jsonSourceFile /*jsonConversionNotifier*/, null);
        if (jsonSourceFile.Diagnostics().Length > 0)
        {
            errors = [jsonSourceFile.Diagnostics()[0]];
        }

        return (config, errors);
    }

    // Go: type ParseConfigHost interface { FS() vfs.FS; GetCurrentDirectory() string }
    public interface ParseConfigHost
    {
        FS FS();

        string GetCurrentDirectory();
    }

    // Go: type resolverHost struct { ParseConfigHost }（+ Trace），同时满足 module.ResolutionHost
    internal sealed class ResolverHost : ParseConfigHost, ResolutionHost
    {
        private readonly ParseConfigHost _host;

        public ResolverHost(ParseConfigHost host)
        {
            _host = host;
        }

        public FS FS() => _host.FS();

        public string GetCurrentDirectory() => _host.GetCurrentDirectory();

        // Go: func (r *resolverHost) Trace(msg string) {}
        public void Trace(string msg)
        {
        }
    }

    // Go: func ParseJsonSourceFileConfigFileContent(...)
    public static ParsedCommandLine ParseJsonSourceFileConfigFileContent(
        TsConfigSourceFile sourceFile,
        ParseConfigHost host,
        string basePath,
        CompilerOptions? existingOptions,
        OrderedMap<string, object?>? existingOptionsRaw,
        string configFileName,
        TPath[]? resolutionStack,
        ExtendedConfigCache? extendedConfigCache)
    {
        // tracing?.push(tracing.Phase.Parse, "parseJsonSourceFileConfigFileContent", { path: sourceFile.fileName });
        var result = ParseJsonConfigFileContentWorker(
            null /*json*/,
            sourceFile,
            host,
            basePath,
            existingOptions,
            existingOptionsRaw,
            configFileName,
            resolutionStack,
            extendedConfigCache);

        // tracing?.pop();
        return result;
    }

    // Go: func convertObjectLiteralExpressionToJson(...)
    internal static (OrderedMap<string, object?>? Result, Diagnostic[] Errors) ConvertObjectLiteralExpressionToJson(
        SourceFile sourceFile,
        bool returnValue,
        ObjectLiteralExpression node,
        CommandLineOption? objectOption,
        JsonConversionNotifier? jsonConversionNotifier)
    {
        OrderedMap<string, object?>? result = null;
        if (returnValue)
        {
            result = new OrderedMap<string, object?>();
        }

        var errors = new List<Diagnostic>();
        // Go 的 Properties 是值字段、Nodes 是 nil 时 range 零次迭代 → `Properties!` + `?? []`。
        foreach (var element in node.Properties!.Nodes ?? [])
        {
            if (element.Kind != Kind.PropertyAssignment)
            {
                errors.Add(NewDiagnostic(sourceFile, element.Loc, Property_assignment_expected));
                continue;
            }

            if (element.QuestionToken() is { } token)
            {
                errors.Add(NewDiagnostic(sourceFile, token.Loc, The_0_modifier_can_only_be_used_in_TypeScript_files, "?"));
            }

            var textOfKey = "";
            if (!IsComputedNonLiteralName(element.Name()) && TryGetTextOfPropertyName(element.Name(), out var propertyName))
            {
                textOfKey = propertyName;
            }

            var keyText = textOfKey;
            CommandLineOption? option = null;
            if (keyText != "" && objectOption?.ElementOptions is not null)
            {
                option = objectOption.ElementOptions.Get(keyText);
                if (option is not null && option.Name != keyText)
                {
                    option = null;
                }
            }

            var (value, err) = ConvertPropertyValueToJson(sourceFile, element.AsPropertyAssignment()!.Initializer!, option, returnValue, jsonConversionNotifier);
            errors.AddRange(err);
            if (keyText != "")
            {
                if (returnValue)
                {
                    result!.Set(keyText, value);
                }

                // Notify key value set, if user asked for it
                if (jsonConversionNotifier is not null)
                {
                    var (_, notifierErrors) = jsonConversionNotifier.OnPropertySet(keyText, value, element.AsPropertyAssignment()!, objectOption, option);
                    errors.AddRange(notifierErrors);
                }
            }
        }

        return (result, [.. errors]);
    }

    // convertToJson converts the json syntax tree into the json value and report errors
    // This returns the json value (apart from checking errors) only if returnValue provided is true.
    // Otherwise it just checks the errors and returns undefined
    // Go: func convertToJson(...)
    internal static (object? Json, Diagnostic[] Errors) ConvertToJson(
        SourceFile sourceFile,
        Node? rootExpression,
        bool returnValue,
        JsonConversionNotifier? jsonConversionNotifier)
    {
        if (rootExpression is null)
        {
            // Go 返回 struct{}{}（一个非 nil 的空值）；C# 里对应 new object()。
            return returnValue ? (new object(), []) : (null, []);
        }

        CommandLineOption? rootOptions = null;
        if (jsonConversionNotifier is not null)
        {
            rootOptions = jsonConversionNotifier.RootOptions;
        }

        return ConvertPropertyValueToJson(sourceFile, rootExpression, rootOptions, returnValue, jsonConversionNotifier);
    }

    // Go: func isDoubleQuotedString(node *ast.Node) bool
    private static bool IsDoubleQuotedString(Node node) => IsStringLiteral(node);

    // Go: func convertPropertyValueToJson(...)
    internal static (object? Value, Diagnostic[] Errors) ConvertPropertyValueToJson(
        SourceFile sourceFile,
        Node valueExpression,
        CommandLineOption? option,
        bool returnValue,
        JsonConversionNotifier? jsonConversionNotifier)
    {
        switch (valueExpression.Kind)
        {
            case Kind.TrueKeyword:
                return (true, []);
            case Kind.FalseKeyword:
                return (false, []);
            case Kind.NullKeyword: // todo: how to manage null
                return (null, []);

            case Kind.StringLiteral:
                if (!IsDoubleQuotedString(valueExpression))
                {
                    return (valueExpression.Text(), [NewDiagnostic(sourceFile, valueExpression.Loc, String_literal_with_double_quotes_expected)]);
                }

                return (valueExpression.Text(), []);

            case Kind.NumericLiteral:
                return ((double)FromString(valueExpression.Text()), []);
            case Kind.PrefixUnaryExpression:
                if (valueExpression.AsPrefixUnaryExpression()!.Operator != Kind.MinusToken ||
                    valueExpression.AsPrefixUnaryExpression()!.Operand!.Kind != Kind.NumericLiteral)
                {
                    break; // not valid JSON syntax
                }

                return (-(double)FromString(valueExpression.AsPrefixUnaryExpression()!.Operand!.Text()), []);
            case Kind.ObjectLiteralExpression:
                var objectLiteralExpression = valueExpression.AsObjectLiteralExpression()!;

                // Currently having element option declaration in the tsconfig with type "object"
                // determines if it needs onSetValidOptionKeyValueInParent callback or not
                // At moment there are only "compilerOptions", "typeAcquisition" and "typingOptions"
                // that satisfies it and need it to modify options set in them (for normalizing file paths)
                // vs what we set in the json
                // If need arises, we can modify this interface and callbacks as needed
                var (literalResult, literalErrors) = ConvertObjectLiteralExpressionToJson(sourceFile, returnValue, objectLiteralExpression, option, jsonConversionNotifier);
                return (literalResult, literalErrors);
            case Kind.ArrayLiteralExpression:
                var (arrayResult, arrayErrors) = ConvertArrayLiteralExpressionToJson(
                    sourceFile,
                    // Kind == ArrayLiteralExpression 时 ElementList() 必定非 nil（Go 同样直接解引用）。
                    valueExpression.Elements()!,
                    option,
                    returnValue);
                return (arrayResult, arrayErrors);
        }

        // Not in expected format
        Diagnostic[] errors;
        if (option is not null)
        {
            errors = [NewDiagnostic(sourceFile, valueExpression.Loc, Compiler_option_0_requires_a_value_of_type_1, option.Name, GetCompilerOptionValueTypeString(option))];
        }
        else
        {
            errors = [NewDiagnostic(sourceFile, valueExpression.Loc, Property_value_can_only_be_string_literal_numeric_literal_true_false_null_object_literal_or_array_literal)];
        }

        return (null, errors);
    }

    // ParseJsonConfigFileContent parses the contents of a config file (tsconfig.json).
    // jsonNode: The contents of the config file to parse
    // host: Instance of ParseConfigHost used to enumerate files in folder.
    // basePath: A root directory to resolve relative path entries in the config file to. e.g. outDir
    // Go: func ParseJsonConfigFileContent(json any, host ParseConfigHost, basePath string, existingOptions *core.CompilerOptions, configFileName string, resolutionStack []tspath.Path, extendedConfigCache ExtendedConfigCache) *ParsedCommandLine
    public static ParsedCommandLine ParseJsonConfigFileContent(
        object? json,
        ParseConfigHost host,
        string basePath,
        CompilerOptions? existingOptions,
        string configFileName,
        TPath[]? resolutionStack,
        ExtendedConfigCache? extendedConfigCache)
    {
        var normalized = NormalizeJsonValue(json);
        var jsonObject = normalized as OrderedMap<string, object?> ?? new OrderedMap<string, object?>();
        var result = ParseJsonConfigFileContentWorker(
            jsonObject,
            null /*sourceFile*/,
            host,
            basePath,
            existingOptions,
            null /*existingOptionsRaw*/,
            configFileName,
            resolutionStack,
            extendedConfigCache);
        return result;
    }

    // Go: func normalizeJsonValue(value any) any
    internal static object? NormalizeJsonValue(object? value)
    {
        switch (value)
        {
            case OrderedMap<string, object?> map:
                foreach (var (key, child) in map.Entries())
                {
                    map.Set(key, NormalizeJsonValue(child));
                }

                return map;
            case Dictionary<string, object?> dictionary:
                var result = NewOrderedMapWithSizeHint<string, object?>(dictionary.Count);
                var keys = new List<string>(dictionary.Keys);
                keys.Sort(string.CompareOrdinal); // Go: slices.Sorted(maps.Keys(value))
                foreach (var key in keys)
                {
                    result.Set(key, NormalizeJsonValue(dictionary[key]));
                }

                return result;
            case object?[] array:
                var items = new object?[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    items[i] = NormalizeJsonValue(array[i]);
                }

                return items;
            default:
                // Go 的 default 分支用 reflect 处理"其它 slice/array"（例如 []string）；C# 里对应非泛型 Array。
                if (value is Array otherArray)
                {
                    // 名字与上面 case 的 `items` 区分（C# 的 switch 各 section 共享块作用域，重名是 CS0136）。
                    var arrayItems = new object?[otherArray.Length];
                    for (var i = 0; i < otherArray.Length; i++)
                    {
                        arrayItems[i] = NormalizeJsonValue(otherArray.GetValue(i));
                    }

                    return arrayItems;
                }

                return value;
        }
    }
}

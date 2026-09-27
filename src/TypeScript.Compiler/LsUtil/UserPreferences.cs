// ported from: internal/ls/lsutil/userpreferences.go
//
// **本文件补齐 `internal/ls/lsutil` 的缺口**（此前只移植了 Asi/Children/CompletedNode/FormatCodeOptions）。
//
// 与 Go 的偏离（C# 语言特性；依据 docs/architecture.md §2/§3）：
// 1) **Go 的 `reflect` 字段扫表 + struct tag 换成了静态描述表**。Go 里 `fieldInfoCache`/`unstableNameIndex`
//    用 `reflect` 遍历 `UserPreferences`、读 `raw:`/`config:`/`fallbackConfig:` tag，`typeParsers`/
//    `typeSerializers` 用 `reflect.Type` 做键；docs/architecture.md §3.6 明令**禁止运行时反射驱动的映射**
//    （AOT/裁剪下会静默丢成员，且 IL2026/IL3050 因 TreatWarningsAsErrors 直接报错）。所以这里把每个字段的
//    (rawName, rawInvert, configPath, configInvert, fallbackConfigPaths, getter, setter, parser, serializer)
//    写成**一行显式登记**，顺序与 Go 的 `collectFieldInfos` 的遍历顺序**逐字一致**（`FormatCodeSettings`
//    是首个字段且无 tag ⇒ 递归进去，其 27 个字段排在最前；`InlayHints`/`CodeLens` 同理就地展开）。
//    这样 `UnstableNameIndex`/配置路径回填/`MarshalJSONTo` 的遍历顺序与 Go 完全相同。
//    Go 的 `typeParsers`/`typeSerializers`/`configPathParsers` 三张映射表等价于 `AddXxx` 辅助方法里内联的
//    parse/serialize 函数，逐条语义见各 `TryParse*`/`Serialize*` 方法。
// 2) Go 的 `type X string` + 一堆常量（`QuotePreference`/`WorkspaceSymbolsScope`/`JsxAttributeCompletionStyle`/
//    `IncludeInlayParameterNameHints`）→ C# 的 `readonly struct × string`（同文件夹里 `SemicolonPreference`
//    的既有做法：隐式入 + 显式出，`default` 规范成 ""）。常量照 Go 的名字放在静态类 `LsUtil` 里
//    （`public static readonly`，struct 不能是 const），与 `SemicolonPreferenceIgnore` 一致。
// 3) Go 的 `type OrganizeImportsCollation bool` → C# `enum OrganizeImportsCollation : byte`
//    （C# 的 enum 不能以 bool 为底层类型；成员名去类型前缀 `Ordinal`/`Unicode`，数值 0/1 与 Go 的 false/true 对齐）。
//    `OrganizeImportsSort`/`OrganizeImportsCaseFirst`/`OrganizeImportsTypeOrder` 是 int 枚举，数值照抄、成员名去前缀。
// 4) Go 的 `map[string]any` → `Dictionary<string, object?>`（JSON 形状解码产出的就是这个类型，
//    见 Json/Decode.cs 的 `DecodeShapeObject`/`DecodeShapeArray`——数组是 `List<object?>`，对应 Go 的 `[]any`）。
// 5) Go 的 `MarshalJSONTo`/`UnmarshalJSONFrom` 是**指针接收者**，所以 Go 里 `json.Marshal(&prefs)` 走自定义编码、
//    `json.Marshal(prefs)`（值）走反射编码；C# 的 struct 实例方法即 `IJsonMarshaler`/`IJsonUnmarshaler`
//    的实现（`Json.Marshal(prefs)` 装箱后同样命中），差异随之消失。
// 6) `strings.ToLower`（Go 的完整 Unicode 小写）→ `StringUtil.ToLowerJS`（§3.4：不能用 `string.ToLower`）。
// 7) `norm.NFD`（golang.org/x/text）→ `string.Normalize(NormalizationForm.FormD)`；`unicode.Mn` 判定 →
//    `CharUnicodeInfo.GetUnicodeCategory`（两者都是规范分解 + Mn 过滤，极少数边缘码点可能因 ICU/x-text 版本
//    不同而有差异，见 docs/architecture.md §3.4 的同类说明）。
// 8) Go 的 `sync.OnceValue` → `static readonly` 字段 + 静态初始化函数（同 Core/NodeModules.cs 的既有做法）。
// 9) Go 的 `panic("raw or config tag required for field " + field.Name)`（字段漏标 tag 时的运行时检查）在 C#
//    里没有对应位置（没有反射遍历），由"每个字段都必须在表里登记"这一静态事实取代。
// 10) **Go 的 `string` 字段零值是 ""，C# 的字段默认是 null** ⇒ 本结构体的三个真实 string 字段
//    （`OrganizeImportsLocale`/`Locale`/`CustomConfigFileName`，Go: userpreferences.go 的 `UserPreferences`
//    字段，`NewDefaultUserPreferences` 并不设置它们，因此 Go 侧默认就是 ""）必须显式 `= ""` 初始化，
//    并因此需要显式无参构造函数（CS8983）。漏掉会让下游把"未设置"当成"设置了"：
//      `ConfigFileRegistryBuilder.computeConfigFileName` 的 `c.customConfigFileName != ""`
//      （configfileregistrybuilder.go:672 → C# 的 `_customConfigFileName.Length != 0`）在 C# 里恒真并解引用 null
//      → `ProjectPushDiagnosticsTests.DoesNotPublishWhenValidationIsDisabled` 等 NRE；
//      `Session.Configure` 的 `config.Locale != ""`（session.go:296）同理恒真 → 每次 Configure 都会
//      `SetLocale(null)`。`new UserPreferences{…}`/`new UserPreferences()` 都走这个构造函数（Go 的
//      `UserPreferences{}` 零值语义）；只有 `default(UserPreferences)` 仍留 null（C# 限制，调用点不要用）。
using System.Text;
using TypeScript.Core;
using TypeScript.ModuleSpecifiers;
using TypeScript.Vfs.VfsMatch;
using static TypeScript.Core.Core;
using static TypeScript.Json.Json;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.Vfs.VfsMatch.VfsMatch;
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;

namespace TypeScript.LsUtil;

/// <summary>Go 的 <c>type QuotePreference string</c>（常量见静态类 <see cref="LsUtil"/>）。</summary>
public readonly struct QuotePreference : IEquatable<QuotePreference>
{
    private readonly string? _value;

    public QuotePreference(string value) => _value = value ?? string.Empty;

    public string Value => _value ?? string.Empty;

    public static implicit operator QuotePreference(string value) => new(value);

    public static explicit operator string(QuotePreference preference) => preference.Value;

    public override string ToString() => Value;

    public bool Equals(QuotePreference other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is QuotePreference other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(QuotePreference left, QuotePreference right) => left.Equals(right);

    public static bool operator !=(QuotePreference left, QuotePreference right) => !left.Equals(right);
}

/// <summary>Go 的 <c>type WorkspaceSymbolsScope string</c>（常量见静态类 <see cref="LsUtil"/>）。</summary>
public readonly struct WorkspaceSymbolsScope : IEquatable<WorkspaceSymbolsScope>
{
    private readonly string? _value;

    public WorkspaceSymbolsScope(string value) => _value = value ?? string.Empty;

    public string Value => _value ?? string.Empty;

    public static implicit operator WorkspaceSymbolsScope(string value) => new(value);

    public static explicit operator string(WorkspaceSymbolsScope scope) => scope.Value;

    public override string ToString() => Value;

    public bool Equals(WorkspaceSymbolsScope other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is WorkspaceSymbolsScope other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(WorkspaceSymbolsScope left, WorkspaceSymbolsScope right) => left.Equals(right);

    public static bool operator !=(WorkspaceSymbolsScope left, WorkspaceSymbolsScope right) => !left.Equals(right);
}

/// <summary>Go 的 <c>type JsxAttributeCompletionStyle string</c>（常量见静态类 <see cref="LsUtil"/>）。</summary>
public readonly struct JsxAttributeCompletionStyle : IEquatable<JsxAttributeCompletionStyle>
{
    private readonly string? _value;

    public JsxAttributeCompletionStyle(string value) => _value = value ?? string.Empty;

    public string Value => _value ?? string.Empty;

    public static implicit operator JsxAttributeCompletionStyle(string value) => new(value);

    public static explicit operator string(JsxAttributeCompletionStyle style) => style.Value;

    public override string ToString() => Value;

    public bool Equals(JsxAttributeCompletionStyle other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is JsxAttributeCompletionStyle other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(JsxAttributeCompletionStyle left, JsxAttributeCompletionStyle right) => left.Equals(right);

    public static bool operator !=(JsxAttributeCompletionStyle left, JsxAttributeCompletionStyle right) => !left.Equals(right);
}

/// <summary>Go 的 <c>type IncludeInlayParameterNameHints string</c>（常量见静态类 <see cref="LsUtil"/>）。</summary>
public readonly struct IncludeInlayParameterNameHints : IEquatable<IncludeInlayParameterNameHints>
{
    private readonly string? _value;

    public IncludeInlayParameterNameHints(string value) => _value = value ?? string.Empty;

    public string Value => _value ?? string.Empty;

    public static implicit operator IncludeInlayParameterNameHints(string value) => new(value);

    public static explicit operator string(IncludeInlayParameterNameHints hints) => hints.Value;

    public override string ToString() => Value;

    public bool Equals(IncludeInlayParameterNameHints other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is IncludeInlayParameterNameHints other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(IncludeInlayParameterNameHints left, IncludeInlayParameterNameHints right) => left.Equals(right);

    public static bool operator !=(IncludeInlayParameterNameHints left, IncludeInlayParameterNameHints right) => !left.Equals(right);
}

/// <summary>Go 的 <c>type OrganizeImportsSort int</c>。</summary>
public enum OrganizeImportsSort : int
{
    Auto = 0,
    Ordinal = 1,
    OrdinalIgnoreCase = 2,
    Natural = 3,
    NaturalIgnoreCase = 4,
}

/// <summary>Go 的 <c>type OrganizeImportsCollation bool</c>（C# 的 enum 不能以 bool 为底层类型 ⇒ 用 byte，0/1 对齐 false/true）。</summary>
public enum OrganizeImportsCollation : byte
{
    Ordinal = 0,
    Unicode = 1,
}

/// <summary>Go 的 <c>type OrganizeImportsCaseFirst int</c>。</summary>
public enum OrganizeImportsCaseFirst : int
{
    False = 0,
    Lower = 1,
    Upper = 2,
}

/// <summary>Go 的 <c>type OrganizeImportsTypeOrder int</c>。</summary>
public enum OrganizeImportsTypeOrder : int
{
    Auto = 0,
    Last = 1,
    Inline = 2,
    First = 3,
}

/// <summary>Go 的 <c>type InlayHintsPreferences struct</c>。</summary>
/// <remarks>
/// Go 里 `oldPrefs.InlayHints != newPrefs.InlayHints`（project/session.go:1832）依赖 struct 的**值相等**；
/// C# 的 struct 默认只有 <c>ValueType.Equals</c>（且没有 <c>==</c> 运算符），所以这里显式实现
/// <c>IEquatable</c> 与 <c>==</c>/<c>!=</c>（逐字段比较，与 Go 的可比较 struct 语义一致）。
/// </remarks>
public struct InlayHintsPreferences : IEquatable<InlayHintsPreferences>
{
    public IncludeInlayParameterNameHints IncludeInlayParameterNameHints;
    public Tristate IncludeInlayParameterNameHintsWhenArgumentMatchesName;
    public Tristate IncludeInlayFunctionParameterTypeHints;
    public Tristate IncludeInlayVariableTypeHints;
    public Tristate IncludeInlayVariableTypeHintsWhenTypeMatchesName;
    public Tristate IncludeInlayPropertyDeclarationTypeHints;
    public Tristate IncludeInlayFunctionLikeReturnTypeHints;
    public Tristate IncludeInlayEnumMemberValueHints;

    public readonly bool Equals(InlayHintsPreferences other) =>
        IncludeInlayParameterNameHints == other.IncludeInlayParameterNameHints &&
        IncludeInlayParameterNameHintsWhenArgumentMatchesName == other.IncludeInlayParameterNameHintsWhenArgumentMatchesName &&
        IncludeInlayFunctionParameterTypeHints == other.IncludeInlayFunctionParameterTypeHints &&
        IncludeInlayVariableTypeHints == other.IncludeInlayVariableTypeHints &&
        IncludeInlayVariableTypeHintsWhenTypeMatchesName == other.IncludeInlayVariableTypeHintsWhenTypeMatchesName &&
        IncludeInlayPropertyDeclarationTypeHints == other.IncludeInlayPropertyDeclarationTypeHints &&
        IncludeInlayFunctionLikeReturnTypeHints == other.IncludeInlayFunctionLikeReturnTypeHints &&
        IncludeInlayEnumMemberValueHints == other.IncludeInlayEnumMemberValueHints;

    public readonly override bool Equals(object? obj) => obj is InlayHintsPreferences other && Equals(other);

    public readonly override int GetHashCode() => HashCode.Combine(
        IncludeInlayParameterNameHints,
        IncludeInlayParameterNameHintsWhenArgumentMatchesName,
        IncludeInlayFunctionParameterTypeHints,
        IncludeInlayVariableTypeHints,
        IncludeInlayVariableTypeHintsWhenTypeMatchesName,
        IncludeInlayPropertyDeclarationTypeHints,
        IncludeInlayFunctionLikeReturnTypeHints,
        IncludeInlayEnumMemberValueHints);

    public static bool operator ==(InlayHintsPreferences left, InlayHintsPreferences right) => left.Equals(right);

    public static bool operator !=(InlayHintsPreferences left, InlayHintsPreferences right) => !left.Equals(right);
}

/// <summary>Go 的 <c>type CodeLensUserPreferences struct</c>（值相等理由同 <see cref="InlayHintsPreferences"/>）。</summary>
public struct CodeLensUserPreferences : IEquatable<CodeLensUserPreferences>
{
    public Tristate ReferencesCodeLensEnabled;
    public Tristate ImplementationsCodeLensEnabled;
    public Tristate ReferencesCodeLensShowOnAllFunctions;
    public Tristate ImplementationsCodeLensShowOnInterfaceMethods;
    public Tristate ImplementationsCodeLensShowOnAllClassMethods;

    public readonly bool Equals(CodeLensUserPreferences other) =>
        ReferencesCodeLensEnabled == other.ReferencesCodeLensEnabled &&
        ImplementationsCodeLensEnabled == other.ImplementationsCodeLensEnabled &&
        ReferencesCodeLensShowOnAllFunctions == other.ReferencesCodeLensShowOnAllFunctions &&
        ImplementationsCodeLensShowOnInterfaceMethods == other.ImplementationsCodeLensShowOnInterfaceMethods &&
        ImplementationsCodeLensShowOnAllClassMethods == other.ImplementationsCodeLensShowOnAllClassMethods;

    public readonly override bool Equals(object? obj) => obj is CodeLensUserPreferences other && Equals(other);

    public readonly override int GetHashCode() => HashCode.Combine(
        ReferencesCodeLensEnabled,
        ImplementationsCodeLensEnabled,
        ReferencesCodeLensShowOnAllFunctions,
        ImplementationsCodeLensShowOnInterfaceMethods,
        ImplementationsCodeLensShowOnAllClassMethods);

    public static bool operator ==(CodeLensUserPreferences left, CodeLensUserPreferences right) => left.Equals(right);

    public static bool operator !=(CodeLensUserPreferences left, CodeLensUserPreferences right) => !left.Equals(right);
}

/// <summary>
/// Go 的 <c>type UserPreferences struct</c>：字段顺序与 Go 逐字一致（`FormatCodeSettings` 在最前）。
/// 备注（Go 的 `!!!` 待办）不移植，同本仓既定做法。
/// </summary>
public struct UserPreferences : TypeScript.Json.IJsonMarshaler, TypeScript.Json.IJsonUnmarshaler
{
    /// <summary>
    /// C# 要求：结构体带字段初始化器时必须显式声明构造函数（CS8983）。这里的构造函数正是 Go 的
    /// <c>UserPreferences{}</c> 零值语义：<c>string</c> 字段为 <c>""</c>（不是 null）——Go 里所有
    /// <c>string</c> 字段的零值都是 <c>""</c>，`c.customConfigFileName != ""`（configfileregistrybuilder.go:672）
    /// 这类判断在 C# 侧只有初始化后才等价。
    /// </summary>
    public UserPreferences()
    {
    }

    public FormatCodeSettings FormatCodeSettings;

    public QuotePreference QuotePreference;
    public Tristate LazyConfiguredProjectsFromExternalProject;

    // A positive integer indicating the maximum length of a hover text before it is truncated.
    //
    // Default: `500`
    public int MaximumHoverLength;

    // ------- Completions -------

    // If enabled, TypeScript will search through all external modules' exports and add them to the completions list.
    // This affects lone identifier completions but not completions on the right hand side of `obj.`.
    public Tristate IncludeCompletionsForModuleExports;

    // Enables auto-import-style completions on partially-typed import statements. E.g., allows
    // `import write|` to be completed to `import { writeFile } from "fs"`.
    public Tristate IncludeCompletionsForImportStatements;

    // Unless this option is `false`,  member completion lists triggered with `.` will include entries
    // on potentially-null and potentially-undefined values, with insertion text to replace
    // preceding `.` tokens with `?.`.
    public Tristate IncludeAutomaticOptionalChainCompletions;

    // If enabled, completions for class members (e.g. methods and properties) will include
    // a whole declaration for the member.
    // E.g., `class A { f| }` could be completed to `class A { foo(): number {} }`, instead of
    // `class A { foo }`.
    public Tristate IncludeCompletionsWithClassMemberSnippets;

    // If enabled, object literal methods will have a method declaration completion entry in addition
    // to the regular completion entry containing just the method name.
    // E.g., `const objectLiteral: T = { f| }` could be completed to `const objectLiteral: T = { foo(): void {} }`,
    // in addition to `const objectLiteral: T = { foo }`.
    public Tristate IncludeCompletionsWithObjectLiteralMethodSnippets;

    public JsxAttributeCompletionStyle JsxAttributeCompletionStyle;
    public Tristate EnableAutoClosingTags;
    public Tristate EnableJSDocCompletions;
    public Tristate GenerateReturnInDocTemplate;

    // ------- AutoImports --------

    public ImportModuleSpecifierPreference ImportModuleSpecifierPreference;

    // Determines whether we import `foo/index.ts` as "foo", "foo/index", or "foo/index.js"
    public ImportModuleSpecifierEndingPreference ImportModuleSpecifierEnding;

    public string[]? AutoImportSpecifierExcludeRegexes;
    public string[]? AutoImportFileExcludePatterns;
    public Tristate AutoImportEntrypointDirectorySearch;
    public Tristate PreferTypeOnlyAutoImports;

    // ------- OrganizeImports -------

    // Indicates which deterministic preset should be used to sort imports.
    // "auto" detects the existing ordinal case sensitivity where possible.
    public OrganizeImportsSort OrganizeImportsSort;

    // Indicates whether imports should be organized in a case-insensitive manner.
    //
    // Default: TSUnknown ("auto" in strada), will perform detection
    public Tristate OrganizeImportsIgnoreCase;

    // Indicates whether imports should be organized via an "ordinal" (binary) comparison using the numeric value of their
    // code points, or via "unicode" natural sorting. This implementation is locale-agnostic and approximates the practical
    // import-sorting behavior rather than the full Unicode Collation Algorithm.
    //
    // Default: Ordinal
    public OrganizeImportsCollation OrganizeImportsCollation;

    // Indicates the locale to use for "unicode" collation in legacy clients. This is accepted for compatibility, but
    // currently ignored because organize-import sorting is deterministic and locale-agnostic.
    //
    // This preference is ignored if organizeImportsCollation is not `unicode`.
    //
    // Default: `"en"`
    // Go 的 `string` 字段零值是 ""（userpreferences.go:47 的 struct 字段；NewDefaultUserPreferences 不设它）
    // ⇒ C# 必须显式初始化，否则 `new UserPreferences{…}` 留 null、"未设置"会被当成"设置了"。
    public string OrganizeImportsLocale = "";

    // Indicates whether numeric collation should be used for digit sequences in strings. When `true`, will collate
    // strings such that `a1z < a2z < a100z`. When `false`, will collate strings such that `a1z < a100z < a2z`.
    //
    // This preference is ignored if organizeImportsCollation is not `unicode`.
    //
    // Default: `false`
    public Tristate OrganizeImportsNumericCollation;

    // Indicates whether accents and other diacritic marks are considered unequal for the purpose of sorting.
    //
    // This preference is ignored if organizeImportsCollation is not `unicode`.
    //
    // Default: `true`
    public Tristate OrganizeImportsAccentCollation;

    // Indicates whether upper case or lower case should sort first.
    //
    // This permission is ignored if:
    //	- organizeImportsCollation is not `unicode`
    //	- organizeImportsIgnoreCase is `true`
    //	- organizeImportsIgnoreCase is `auto` and the auto-detected case sensitivity is case-insensitive.
    //
    // Default: `false`
    public OrganizeImportsCaseFirst OrganizeImportsCaseFirst;

    // Indicates where named type-only imports should sort. "inline" sorts named imports without regard to if the import is type-only.
    //
    // Default: `auto`, which defaults to `last`
    public OrganizeImportsTypeOrder OrganizeImportsTypeOrder;

    // ------- MoveToFile -------

    public Tristate AllowTextChangesInNewFiles;

    // ------- Rename -------

    public Tristate UseAliasesForRename;
    public Tristate AllowRenameOfImportPath;

    // ------- CodeFixes/Refactors -------

    public Tristate ProvideRefactorNotApplicableReason;

    // ------- InlayHints -------

    public InlayHintsPreferences InlayHints;

    // ------- CodeLens -------

    public CodeLensUserPreferences CodeLens;

    // ------- Definition -------

    public bool PreferGoToSourceDefinition;

    // ------- Symbols -------

    public Tristate ExcludeLibrarySymbolsInNavTo;
    public WorkspaceSymbolsScope WorkspaceSymbolsScope;

    // ------- Misc -------

    public Tristate EnableFormatting;
    public Tristate EnableValidation;
    public Tristate DisableSuggestions;
    public Tristate DisableLineTextInReferences;
    public Tristate DisplayPartsForJSDoc;
    public Tristate ReportStyleChecksAsWarnings;
    public string Locale = "";

    // ------- ATA -------

    // DisableAutomaticTypeAcquisition is the deprecated setting from typescript.disableAutomaticTypeAcquisition.
    public Tristate DisableAutomaticTypeAcquisition;

    // AutomaticTypeAcquisitionEnabled is the unified setting from tsserver.automaticTypeAcquisition.enabled under the js/ts section.
    // When set, it takes precedence over DisableAutomaticTypeAcquisition.
    public Tristate AutomaticTypeAcquisitionEnabled;

    // ------- Project Configuration -------

    // CustomConfigFileName specifies a custom config file name to use before defaulting to tsconfig.json/jsconfig.json.
    // Go 的零值是 ""（userpreferences.go 的 `CustomConfigFileName string`）；C# 不初始化会让本字段为 null，
    // 于是 ConfigFileRegistryBuilder.computeConfigFileName 的 `c.customConfigFileName != ""`（configfileregistrybuilder.go:672）
    // 在 C# 里恒真并解引用 null（.Length）→ NRE。
    public string CustomConfigFileName = "";

    /// <summary>
    /// IsATADisabled returns whether Automatic Type Acquisition is disabled based on user preferences.
    /// It checks the unified setting (tsserver.automaticTypeAcquisition.enabled) first,
    /// then falls back to the deprecated setting (disableAutomaticTypeAcquisition).
    /// </summary>
    public readonly bool IsATADisabled()
    {
        if (!AutomaticTypeAcquisitionEnabled.IsUnknown())
        {
            return !AutomaticTypeAcquisitionEnabled.IsTrue();
        }

        return DisableAutomaticTypeAcquisition.IsTrue();
    }

    // --- Helper methods ---

    /// <summary>Go 的 <c>func (p UserPreferences) ModuleSpecifierPreferences() modulespecifiers.UserPreferences</c>。</summary>
    /// <remarks>
    /// **返回类型必须全限定**：本类型的名字也叫 `UserPreferences`，裸写 `UserPreferences` 会解析成**自己**
    /// （C# 的名字查找先命中封闭类型/LsUtil 里的同名类型），于是返回了一个 LsUtil.UserPreferences 值 ——
    /// 编译器不报错（两者都有那三个字段），但在调用点（`ls.LanguageService` 把结果传给
    /// `LsAutoImport.NewView(..., modulespecifiers.UserPreferences, ...)`）会 CS1503。
    /// </remarks>
    public readonly TypeScript.ModuleSpecifiers.UserPreferences ModuleSpecifierPreferences()
    {
        return new TypeScript.ModuleSpecifiers.UserPreferences
        {
            ImportModuleSpecifierPreference = ImportModuleSpecifierPreference,
            ImportModuleSpecifierEnding = ImportModuleSpecifierEnding,
            AutoImportSpecifierExcludeRegexes = AutoImportSpecifierExcludeRegexes,
        };
    }

    /// <summary>Go 的 <c>func (p UserPreferences) ParsedAutoImportFileExcludePatterns(useCaseSensitiveFileNames bool) *vfsmatch.SpecMatcher</c>。</summary>
    public readonly SpecMatcher? ParsedAutoImportFileExcludePatterns(bool useCaseSensitiveFileNames)
    {
        return NewSpecMatcher(AutoImportFileExcludePatterns, "", Usage.Exclude, useCaseSensitiveFileNames);
    }

    /// <summary>Go 的 <c>func (p UserPreferences) IsModuleSpecifierExcluded(moduleSpecifier string) bool</c>。</summary>
    public readonly bool IsModuleSpecifierExcluded(string moduleSpecifier)
    {
        return IsExcludedByRegex(moduleSpecifier, AutoImportSpecifierExcludeRegexes);
    }

    /// <summary>Go 的 <c>func (p *UserPreferences) MarshalJSONTo(enc *json.Encoder) error</c>。</summary>
    public readonly void MarshalJSONTo(JsonEncoder enc)
    {
        var config = new Dictionary<string, object?>(StringComparer.Ordinal);
        var p = this;

        foreach (var info in _fieldInfoCache)
        {
            var val = info._get(p);
            if (val is null)
            {
                continue;
            }

            // Prefer config path if available, otherwise use unstable section
            if (info._configPath.Length != 0)
            {
                if (info._configInvert && val is bool invertBool)
                {
                    val = !invertBool;
                }

                SetNestedValue(config, info._configPath, val);
            }
            else if (info._rawName.Length != 0)
            {
                if (info._rawInvert && val is bool invertRawBool)
                {
                    val = !invertRawBool;
                }

                SetNestedValue(config, "unstable." + info._rawName, val);
            }
        }

        MarshalEncode(enc, config, Deterministic(true));
    }

    /// <summary>Go 的 <c>func (p *UserPreferences) UnmarshalJSONFrom(dec *json.Decoder) error</c>。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var config = UnmarshalDecode<Dictionary<string, object?>>(dec) ?? new Dictionary<string, object?>(StringComparer.Ordinal);

        // Start with defaults, then overlay parsed values
        this = NewDefaultUserPreferences().WithConfig(config);
    }

    /// <summary>Go 的 <c>func (p UserPreferences) withConfig(config map[string]any) UserPreferences</c>（未导出 → internal）。</summary>
    internal readonly UserPreferences WithConfig(Dictionary<string, object?> config)
    {
        var p = this;
        var infos = _fieldInfoCache;

        // Raw UserPreferences can be provided directly, notably via LSP initializationOptions.
        SetRawFieldsFromConfig(ref p, infos, config);

        // Process "unstable" section first - allows any field to be set by raw name.
        // This mirrors VS Code's behavior: { ...config.get('unstable'), ...stableOptions }
        // where stable options are spread after and take precedence.
        if (config.TryGetValue("unstable", out var unstable) && unstable is Dictionary<string, object?> unstableMap)
        {
            SetRawFieldsFromConfig(ref p, infos, unstableMap);
        }

        // Process path-based config (VS Code style nested paths).
        // These run after unstable, so stable config values take precedence.
        foreach (var info in infos)
        {
            if (info._configPath.Length == 0)
            {
                continue;
            }

            var configPath = new ConfigPathInfo { _path = info._configPath, _invert = info._configInvert };
            var ok = TryGetNestedValue(config, configPath._path, out var val);
            if (!ok)
            {
                foreach (var fallbackConfigPath in info._fallbackConfigPaths)
                {
                    if (TryGetNestedValue(config, fallbackConfigPath._path, out val))
                    {
                        ok = true;
                        configPath = fallbackConfigPath;
                        break;
                    }
                }
            }

            if (!ok)
            {
                continue;
            }

            if (configPath._invert && val is bool invertBool)
            {
                val = !invertBool;
            }

            if (_configPathParsers.TryGetValue(configPath._path, out var parser))
            {
                info._setDirect(ref p, parser(val));
                continue;
            }

            info._set(ref p, val);
        }

        // Validate CustomConfigFileName for path traversal
        if (!string.IsNullOrEmpty(p.CustomConfigFileName))
        {
            var name = p.CustomConfigFileName.Trim();
            if (name.IndexOfAny(['/', '\\']) >= 0 || name == ".." || name == ".")
            {
                p.CustomConfigFileName = "";
            }
            else
            {
                p.CustomConfigFileName = name;
            }
        }

        return p;
    }
}

public static partial class LsUtil
{
    // --- Enum-ish constants（Go 的包级常量；struct/枚举不能是 const，用 static readonly）---

    /// <summary>Go 的 <c>WorkspaceSymbolsScopeAllOpenProjects</c>。</summary>
    public static readonly WorkspaceSymbolsScope WorkspaceSymbolsScopeAllOpenProjects = new("allOpenProjects");

    /// <summary>Go 的 <c>WorkspaceSymbolsScopeCurrentProject</c>。</summary>
    public static readonly WorkspaceSymbolsScope WorkspaceSymbolsScopeCurrentProject = new("currentProject");

    /// <summary>Go 的 <c>QuotePreferenceUnknown</c>。</summary>
    public static readonly QuotePreference QuotePreferenceUnknown = new("");

    /// <summary>Go 的 <c>QuotePreferenceAuto</c>。</summary>
    public static readonly QuotePreference QuotePreferenceAuto = new("auto");

    /// <summary>Go 的 <c>QuotePreferenceDouble</c>。</summary>
    public static readonly QuotePreference QuotePreferenceDouble = new("double");

    /// <summary>Go 的 <c>QuotePreferenceSingle</c>。</summary>
    public static readonly QuotePreference QuotePreferenceSingle = new("single");

    /// <summary>Go 的 <c>JsxAttributeCompletionStyleUnknown</c>。</summary>
    public static readonly JsxAttributeCompletionStyle JsxAttributeCompletionStyleUnknown = new("");

    /// <summary>Go 的 <c>JsxAttributeCompletionStyleAuto</c>。</summary>
    public static readonly JsxAttributeCompletionStyle JsxAttributeCompletionStyleAuto = new("auto");

    /// <summary>Go 的 <c>JsxAttributeCompletionStyleBraces</c>。</summary>
    public static readonly JsxAttributeCompletionStyle JsxAttributeCompletionStyleBraces = new("braces");

    /// <summary>Go 的 <c>JsxAttributeCompletionStyleNone</c>。</summary>
    public static readonly JsxAttributeCompletionStyle JsxAttributeCompletionStyleNone = new("none");

    /// <summary>Go 的 <c>IncludeInlayParameterNameHintsNone</c>。</summary>
    public static readonly IncludeInlayParameterNameHints IncludeInlayParameterNameHintsNone = new("");

    /// <summary>Go 的 <c>IncludeInlayParameterNameHintsAll</c>。</summary>
    public static readonly IncludeInlayParameterNameHints IncludeInlayParameterNameHintsAll = new("all");

    /// <summary>Go 的 <c>IncludeInlayParameterNameHintsLiterals</c>。</summary>
    public static readonly IncludeInlayParameterNameHints IncludeInlayParameterNameHintsLiterals = new("literals");

    /// <summary>Go 的 <c>func NewDefaultUserPreferences() UserPreferences</c>。</summary>
    public static UserPreferences NewDefaultUserPreferences()
    {
        var prefs = new UserPreferences
        {
            FormatCodeSettings = GetDefaultFormatCodeSettings(),

            IncludeCompletionsForModuleExports = Tristate.TSTrue,
            IncludeCompletionsForImportStatements = Tristate.TSTrue,
            EnableAutoClosingTags = Tristate.TSTrue,
            EnableJSDocCompletions = Tristate.TSTrue,
            GenerateReturnInDocTemplate = Tristate.TSTrue,

            AllowRenameOfImportPath = Tristate.TSTrue,
            ProvideRefactorNotApplicableReason = Tristate.TSTrue,
            EnableFormatting = Tristate.TSTrue,
            EnableValidation = Tristate.TSTrue,
            DisplayPartsForJSDoc = Tristate.TSTrue,
            DisableLineTextInReferences = Tristate.TSTrue,
            ReportStyleChecksAsWarnings = Tristate.TSTrue,

            ExcludeLibrarySymbolsInNavTo = Tristate.TSTrue,
            WorkspaceSymbolsScope = WorkspaceSymbolsScopeAllOpenProjects,
        };
        return prefs;
    }

    /// <summary>Go 的 <c>func ParseUserPreferences(items map[string]any) UserPreferences</c>。</summary>
    public static UserPreferences ParseUserPreferences(Dictionary<string, object?> items)
    {
        var prefs = NewDefaultUserPreferences();

        // Apply editor settings first (tabSize, indentSize, etc.) as raw-name defaults,
        // then overlay language-specific settings with increasing precedence:
        // editor < javascript < typescript < js/ts
        if (items.TryGetValue("editor", out var editorItem) && editorItem is not null)
        {
            if (editorItem is Dictionary<string, object?> editorSettings)
            {
                var normalizedSettings = new Dictionary<string, object?>(editorSettings, StringComparer.Ordinal);
                if (normalizedSettings.TryGetValue("tabSize", out var tabSize) && !normalizedSettings.ContainsKey("indentSize"))
                {
                    normalizedSettings["indentSize"] = tabSize;
                }

                if (normalizedSettings.TryGetValue("insertSpaces", out var insertSpaces) && !normalizedSettings.ContainsKey("convertTabsToSpaces"))
                {
                    normalizedSettings["convertTabsToSpaces"] = insertSpaces;
                }

                prefs = prefs.WithConfig(new Dictionary<string, object?>(StringComparer.Ordinal) { ["unstable"] = normalizedSettings });
            }
        }

        // Apply javascript, then typescript, then js/ts (highest precedence).
        foreach (var section in new[] { "javascript", "typescript", "js/ts" })
        {
            if (items.TryGetValue(section, out var item) && item is not null)
            {
                if (item is Dictionary<string, object?> settings)
                {
                    prefs = prefs.WithConfig(settings);
                }
            }
        }

        return prefs;
    }

    // ── 配置字段描述表（替换 Go 的 reflect 扫表；见文件头第 1 条）──

    internal delegate void PreferenceFieldSetter<T>(ref UserPreferences prefs, T value);

    internal delegate void PreferenceFieldValueSetter(ref UserPreferences prefs, object? value);

    internal delegate bool PreferenceFieldParser<T>(object? value, out T result);

    internal delegate object? PreferenceFieldSerializer<T>(T value);

    /// <summary>Go 的未导出 <c>configPathInfo</c>（`UserPreferences.WithConfig` 要用 ⇒ internal）。</summary>
    internal sealed class ConfigPathInfo
    {
        internal string _path = "";
        internal bool _invert;
    }

    /// <summary>Go 的未导出 <c>fieldInfo</c>（`fieldPath []int` 换成一对 getter/setter 委托）。</summary>
    internal sealed class PreferenceFieldInfo
    {
        internal string _rawName = "";   // raw name for unstable section lookup (e.g., "quotePreference")
        internal bool _rawInvert;        // whether to invert boolean values for raw name
        internal string _configPath = ""; // dotted path for config (e.g., "preferences.quoteStyle")
        internal bool _configInvert;     // whether to invert boolean values for config path
        internal ConfigPathInfo[] _fallbackConfigPaths = [];
        internal Func<UserPreferences, object?> _get = static _ => null;

        // Go 的 setFieldFromValue（`val == nil` 时不动字段）。
        internal PreferenceFieldValueSetter _set = static (ref UserPreferences _, object? _) => { };

        // Go 的 `field.Set(reflect.ValueOf(parser(val)))`（配置路径特化解析器专用：不做 nil 短路）。
        internal PreferenceFieldValueSetter _setDirect = static (ref UserPreferences _, object? _) => { };
    }

    /// <summary>Go 的 <c>sync.OnceValue(func() []fieldInfo { return collectFieldInfos(...) })</c>。</summary>
    internal static readonly PreferenceFieldInfo[] _fieldInfoCache = CollectFieldInfos();

    /// <summary>Go 的 <c>sync.OnceValue(unstableNameIndex)</c>：raw 名 → fieldInfo 下标（同名时后者覆盖，与 Go 一致）。</summary>
    private static readonly Dictionary<string, int> _unstableNameIndex = BuildUnstableNameIndex();

    private static Dictionary<string, int> BuildUnstableNameIndex()
    {
        var infos = _fieldInfoCache;
        var index = new Dictionary<string, int>(infos.Length, StringComparer.Ordinal);
        for (var i = 0; i < infos.Length; i++)
        {
            if (infos[i]._rawName.Length != 0)
            {
                index[infos[i]._rawName] = i;
            }
        }

        return index;
    }

    /// <summary>
    /// Go 的 <c>configPathParsers</c>：当 VS Code 给的取值形状与 Go 字段类型不同时按**路径**特化解析。
    /// </summary>
    internal static readonly Dictionary<string, Func<object?, object?>> _configPathParsers = new(StringComparer.Ordinal)
    {
        // VS Code sends caseSensitivity as a string ("auto"/"caseSensitive"/"caseInsensitive"),
        // but OrganizeImportsIgnoreCase is a core.Tristate.
        ["preferences.organizeImports.caseSensitivity"] = static val =>
        {
            if (val is string s)
            {
                switch (ToLowerJS(s))
                {
                    case "caseinsensitive":
                        return Tristate.TSTrue;
                    case "casesensitive":
                        return Tristate.TSFalse;
                }
            }

            if (val is bool b)
            {
                return BoolToTristate(b);
            }

            return Tristate.TSUnknown;
        },
    };

    /// <summary>Go 的 <c>setRawFieldsFromConfig</c>。</summary>
    internal static void SetRawFieldsFromConfig(ref UserPreferences prefs, PreferenceFieldInfo[] infos, Dictionary<string, object?> settings)
    {
        var index = _unstableNameIndex;
        foreach (var (name, value) in settings)
        {
            if (index.TryGetValue(name, out var idx))
            {
                var info = infos[idx];
                var val = value;
                if (info._rawInvert && val is bool b)
                {
                    val = !b;
                }

                info._set(ref prefs, val);
            }
        }
    }

    /// <summary>Go 的未导出 <c>getNestedValue</c>（第二返回值 = 是否命中；<c>WithConfig</c> 要用 ⇒ internal）。</summary>
    internal static bool TryGetNestedValue(Dictionary<string, object?> config, string path, out object? value)
    {
        var parts = path.Split('.');
        object? current = config;
        foreach (var part in parts)
        {
            if (current is not Dictionary<string, object?> m)
            {
                value = null;
                return false;
            }

            if (!m.TryGetValue(part, out current))
            {
                value = null;
                return false;
            }
        }

        value = current;
        return true;
    }

    /// <summary>Go 的未导出 <c>setNestedValue</c>（中间层缺失或不是对象时替换成新对象；<c>MarshalJSONTo</c> 要用 ⇒ internal）。</summary>
    internal static void SetNestedValue(Dictionary<string, object?> config, string path, object? value)
    {
        var parts = path.Split('.');
        var current = config;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> nextMap)
            {
                nextMap = new Dictionary<string, object?>(StringComparer.Ordinal);
                current[parts[i]] = nextMap;
            }

            current = nextMap;
        }

        current[parts[^1]] = value;
    }

    /// <summary>Go 的未导出 <c>parseConfigPathTag</c>（<c>"path.to.setting"</c> 或 <c>"path.to.setting,invert"</c>）。</summary>
    private static ConfigPathInfo ParseConfigPathTag(string tag)
    {
        var parts = tag.Split(',');
        var info = new ConfigPathInfo { _path = parts[0] };
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i] == "invert")
            {
                info._invert = true;
            }
        }

        return info;
    }

    /// <summary>登记一个字段（Go 的 <c>collectFieldInfos</c> 里"读 tag + 记 fieldPath"那一段的等价物）。</summary>
    private static void Add<T>(
        List<PreferenceFieldInfo> infos,
        string rawName,
        string configPath,
        string[] fallbackConfigPaths,
        Func<UserPreferences, T> get,
        PreferenceFieldSetter<T> set,
        PreferenceFieldParser<T> parse,
        PreferenceFieldSerializer<T> serialize,
        bool rawInvert = false,
        bool configInvert = false)
    {
        var fallbacks = new ConfigPathInfo[fallbackConfigPaths.Length];
        for (var i = 0; i < fallbackConfigPaths.Length; i++)
        {
            fallbacks[i] = ParseConfigPathTag(fallbackConfigPaths[i]);
        }

        infos.Add(new PreferenceFieldInfo
        {
            _rawName = rawName,
            _rawInvert = rawInvert,
            _configPath = configPath,
            _configInvert = configInvert,
            _fallbackConfigPaths = fallbacks,
            _get = p => serialize(get(p)),
            _set = (ref UserPreferences p, object? value) =>
            {
                if (value is null)
                {
                    return;
                }

                if (parse(value, out var parsed))
                {
                    set(ref p, parsed);
                }
            },
            _setDirect = (ref UserPreferences p, object? value) => set(ref p, (T)value!),
        });
    }

    private static void AddInt(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, int> get, PreferenceFieldSetter<int> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseIntValue, SerializeInt, rawInvert, configInvert);

    private static void AddString(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, string> get, PreferenceFieldSetter<string> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseStringValue, SerializeString, rawInvert, configInvert);

    private static void AddBool(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, bool> get, PreferenceFieldSetter<bool> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseBoolValue, SerializeBool, rawInvert, configInvert);

    private static void AddStringSlice(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, string[]?> get, PreferenceFieldSetter<string[]?> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseStringSliceValue, SerializeStringSlice, rawInvert, configInvert);

    private static void AddTristate(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, Tristate> get, PreferenceFieldSetter<Tristate> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseTristateValue, SerializeTristate, rawInvert, configInvert);

    private static void AddIndentStyle(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, IndentStyle> get, PreferenceFieldSetter<IndentStyle> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseIndentStyleValue, SerializeIndentStyle, rawInvert, configInvert);

    private static void AddSemicolonPreference(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, SemicolonPreference> get, PreferenceFieldSetter<SemicolonPreference> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseSemicolonPreferenceValue, SerializeSemicolonPreference, rawInvert, configInvert);

    private static void AddQuotePreference(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, QuotePreference> get, PreferenceFieldSetter<QuotePreference> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseQuotePreferenceValue, SerializeQuotePreference, rawInvert, configInvert);

    private static void AddJsxAttributeCompletionStyle(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, JsxAttributeCompletionStyle> get, PreferenceFieldSetter<JsxAttributeCompletionStyle> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseJsxAttributeCompletionStyleValue, SerializeJsxAttributeCompletionStyle, rawInvert, configInvert);

    private static void AddIncludeInlayParameterNameHints(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, IncludeInlayParameterNameHints> get, PreferenceFieldSetter<IncludeInlayParameterNameHints> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseIncludeInlayParameterNameHintsValue, SerializeIncludeInlayParameterNameHints, rawInvert, configInvert);

    private static void AddOrganizeImportsSort(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, OrganizeImportsSort> get, PreferenceFieldSetter<OrganizeImportsSort> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseOrganizeImportsSortValue, SerializeOrganizeImportsSort, rawInvert, configInvert);

    private static void AddOrganizeImportsCollation(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, OrganizeImportsCollation> get, PreferenceFieldSetter<OrganizeImportsCollation> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseOrganizeImportsCollationValue, SerializeOrganizeImportsCollation, rawInvert, configInvert);

    private static void AddOrganizeImportsCaseFirst(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, OrganizeImportsCaseFirst> get, PreferenceFieldSetter<OrganizeImportsCaseFirst> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseOrganizeImportsCaseFirstValue, SerializeOrganizeImportsCaseFirst, rawInvert, configInvert);

    private static void AddOrganizeImportsTypeOrder(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, OrganizeImportsTypeOrder> get, PreferenceFieldSetter<OrganizeImportsTypeOrder> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseOrganizeImportsTypeOrderValue, SerializeOrganizeImportsTypeOrder, rawInvert, configInvert);

    private static void AddImportModuleSpecifierPreference(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, ImportModuleSpecifierPreference> get, PreferenceFieldSetter<ImportModuleSpecifierPreference> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseImportModuleSpecifierPreferenceValue, SerializeImportModuleSpecifierPreference, rawInvert, configInvert);

    private static void AddImportModuleSpecifierEnding(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, ImportModuleSpecifierEndingPreference> get, PreferenceFieldSetter<ImportModuleSpecifierEndingPreference> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseImportModuleSpecifierEndingPreferenceValue, SerializeImportModuleSpecifierEndingPreference, rawInvert, configInvert);

    private static void AddWorkspaceSymbolsScope(List<PreferenceFieldInfo> infos, string rawName, string configPath, string[] fallbackConfigPaths, Func<UserPreferences, WorkspaceSymbolsScope> get, PreferenceFieldSetter<WorkspaceSymbolsScope> set, bool rawInvert = false, bool configInvert = false) =>
        Add(infos, rawName, configPath, fallbackConfigPaths, get, set, TryParseWorkspaceSymbolsScopeValue, SerializeWorkspaceSymbolsScope, rawInvert, configInvert);

    // --- Go 的 typeParsers / typeSerializers（逐条对应，见文件头第 1 条）---

    /// <summary>Go 的 <c>setFieldFromValue</c> 的 <c>reflect.Int</c> 分支（int / float64 两种来源）。</summary>
    private static bool TryParseIntValue(object? value, out int result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case double d:
                result = (int)d;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>Go 的 <c>serializeField</c> 的 <c>reflect.Int</c> 分支（零值 = 未设置，省略）。</summary>
    private static object? SerializeInt(int value) => value == 0 ? null : value;

    /// <summary>Go 的 <c>setFieldFromValue</c> 的 <c>reflect.String</c> 分支。</summary>
    private static bool TryParseStringValue(object? value, out string result)
    {
        if (value is string s)
        {
            result = s;
            return true;
        }

        result = "";
        return false;
    }

    /// <summary>Go 的 <c>serializeField</c> 的 <c>reflect.String</c> 分支（<c>""</c> 省略；C# 里 struct 字段的"未初始化"是 null，同样省略）。</summary>
    private static object? SerializeString(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Go 的 <c>setFieldFromValue</c> 的 <c>reflect.Bool</c> 分支。</summary>
    private static bool TryParseBoolValue(object? value, out bool result)
    {
        if (value is bool b)
        {
            result = b;
            return true;
        }

        result = false;
        return false;
    }

    /// <summary>Go 的 <c>serializeField</c> 的 <c>reflect.Bool</c> 分支。</summary>
    private static object? SerializeBool(bool value) => value;

    /// <summary>Go 的 <c>setFieldFromValue</c> 的 <c>reflect.Slice</c> 分支（只接受 <c>[]any</c>，且只收其中的字符串）。</summary>
    private static bool TryParseStringSliceValue(object? value, out string[]? result)
    {
        if (value is List<object?> list)
        {
            var items = new List<string>(list.Count);
            foreach (var item in list)
            {
                if (item is string s)
                {
                    items.Add(s);
                }
            }

            result = items.ToArray();
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>Go 的 <c>serializeField</c> 的 <c>reflect.Slice</c> 分支（nil → nil）。</summary>
    private static object? SerializeStringSlice(string[]? value) => value is null ? null : value;

    /// <summary>Go 的 <c>typeParsers[core.Tristate]</c>：非 bool 一律回落 TSUnknown（与 Go 一样"总是成功"）。</summary>
    private static bool TryParseTristateValue(object? value, out Tristate result)
    {
        result = value is bool b ? BoolToTristate(b) : Tristate.TSUnknown;
        return true;
    }

    /// <summary>Go 的 <c>typeSerializers[core.Tristate]</c>（unknown → nil）。</summary>
    private static object? SerializeTristate(Tristate value)
    {
        return value switch
        {
            Tristate.TSTrue => true,
            Tristate.TSFalse => false,
            _ => null,
        };
    }

    /// <summary>Go 的 <c>typeParsers[IndentStyle]</c>（复用 FormatCodeOptions.cs 的 <c>parseIndentStyle</c>）。</summary>
    private static bool TryParseIndentStyleValue(object? value, out IndentStyle result)
    {
        result = ParseIndentStyle(value);
        return true;
    }

    /// <summary>Go 的 <c>serializeField</c> 的 <c>reflect.Int</c> 分支（IndentStyle 没有专门的 serializer）。</summary>
    private static object? SerializeIndentStyle(IndentStyle value) => (int)value == 0 ? null : (int)value;

    /// <summary>Go 的 <c>typeParsers[SemicolonPreference]</c>（复用 FormatCodeOptions.cs 的 <c>parseSemicolonPreference</c>）。</summary>
    private static bool TryParseSemicolonPreferenceValue(object? value, out SemicolonPreference result)
    {
        result = ParseSemicolonPreference(value);
        return true;
    }

    /// <summary>Go 的 <c>serializeField</c> 的 <c>reflect.String</c> 分支（SemicolonPreference 没有专门的 serializer）。</summary>
    private static object? SerializeSemicolonPreference(SemicolonPreference value) => string.IsNullOrEmpty(value.Value) ? null : value.Value;

    /// <summary>Go 的 <c>typeParsers[QuotePreference]</c>。</summary>
    private static bool TryParseQuotePreferenceValue(object? value, out QuotePreference result)
    {
        if (value is string s)
        {
            switch (ToLowerJS(s))
            {
                case "auto":
                    result = QuotePreferenceAuto;
                    return true;
                case "double":
                    result = QuotePreferenceDouble;
                    return true;
                case "single":
                    result = QuotePreferenceSingle;
                    return true;
            }
        }

        result = QuotePreferenceUnknown;
        return true;
    }

    /// <summary>Go 的 <c>serializeField</c> 的 <c>reflect.String</c> 分支（QuotePreference 没有专门的 serializer）。</summary>
    private static object? SerializeQuotePreference(QuotePreference value) => string.IsNullOrEmpty(value.Value) ? null : value.Value;

    /// <summary>Go 的 <c>typeParsers[JsxAttributeCompletionStyle]</c>。</summary>
    private static bool TryParseJsxAttributeCompletionStyleValue(object? value, out JsxAttributeCompletionStyle result)
    {
        if (value is string s)
        {
            switch (ToLowerJS(s))
            {
                case "braces":
                    result = JsxAttributeCompletionStyleBraces;
                    return true;
                case "none":
                    result = JsxAttributeCompletionStyleNone;
                    return true;
            }
        }

        result = JsxAttributeCompletionStyleAuto;
        return true;
    }

    /// <summary>Go 的 <c>typeSerializers[JsxAttributeCompletionStyle]</c>（unknown → nil）。</summary>
    private static object? SerializeJsxAttributeCompletionStyle(JsxAttributeCompletionStyle value) =>
        value == JsxAttributeCompletionStyleUnknown ? null : value.Value;

    /// <summary>Go 的 <c>typeParsers[IncludeInlayParameterNameHints]</c>。</summary>
    private static bool TryParseIncludeInlayParameterNameHintsValue(object? value, out IncludeInlayParameterNameHints result)
    {
        if (value is string s)
        {
            switch (s)
            {
                case "all":
                    result = IncludeInlayParameterNameHintsAll;
                    return true;
                case "literals":
                    result = IncludeInlayParameterNameHintsLiterals;
                    return true;
            }
        }

        result = IncludeInlayParameterNameHintsNone;
        return true;
    }

    /// <summary>Go 的 <c>serializeField</c> 的 <c>reflect.String</c> 分支。</summary>
    private static object? SerializeIncludeInlayParameterNameHints(IncludeInlayParameterNameHints value) =>
        string.IsNullOrEmpty(value.Value) ? null : value.Value;

    /// <summary>Go 的 <c>typeParsers[OrganizeImportsSort]</c>。</summary>
    private static bool TryParseOrganizeImportsSortValue(object? value, out OrganizeImportsSort result)
    {
        if (value is string s)
        {
            switch (ToLowerJS(s))
            {
                case "ordinal":
                    result = OrganizeImportsSort.Ordinal;
                    return true;
                case "ordinalignorecase":
                    result = OrganizeImportsSort.OrdinalIgnoreCase;
                    return true;
                case "natural":
                    result = OrganizeImportsSort.Natural;
                    return true;
                case "naturalignorecase":
                    result = OrganizeImportsSort.NaturalIgnoreCase;
                    return true;
            }
        }

        result = OrganizeImportsSort.Auto;
        return true;
    }

    /// <summary>Go 的 <c>typeSerializers[OrganizeImportsSort]</c>。</summary>
    private static object? SerializeOrganizeImportsSort(OrganizeImportsSort value)
    {
        return value switch
        {
            OrganizeImportsSort.Ordinal => "ordinal",
            OrganizeImportsSort.OrdinalIgnoreCase => "ordinalIgnoreCase",
            OrganizeImportsSort.Natural => "natural",
            OrganizeImportsSort.NaturalIgnoreCase => "naturalIgnoreCase",
            _ => "auto",
        };
    }

    /// <summary>Go 的 <c>typeParsers[OrganizeImportsCollation]</c>。</summary>
    private static bool TryParseOrganizeImportsCollationValue(object? value, out OrganizeImportsCollation result)
    {
        result = value is string s && ToLowerJS(s) == "unicode" ? OrganizeImportsCollation.Unicode : OrganizeImportsCollation.Ordinal;
        return true;
    }

    /// <summary>Go 的 <c>typeSerializers[OrganizeImportsCollation]</c>。</summary>
    private static object? SerializeOrganizeImportsCollation(OrganizeImportsCollation value) =>
        value == OrganizeImportsCollation.Unicode ? "unicode" : "ordinal";

    /// <summary>Go 的 <c>typeParsers[OrganizeImportsCaseFirst]</c>。</summary>
    private static bool TryParseOrganizeImportsCaseFirstValue(object? value, out OrganizeImportsCaseFirst result)
    {
        if (value is string s)
        {
            switch (s)
            {
                case "lower":
                    result = OrganizeImportsCaseFirst.Lower;
                    return true;
                case "upper":
                    result = OrganizeImportsCaseFirst.Upper;
                    return true;
            }
        }

        result = OrganizeImportsCaseFirst.False;
        return true;
    }

    /// <summary>Go 的 <c>typeSerializers[OrganizeImportsCaseFirst]</c>。</summary>
    private static object? SerializeOrganizeImportsCaseFirst(OrganizeImportsCaseFirst value)
    {
        return value switch
        {
            OrganizeImportsCaseFirst.Lower => "lower",
            OrganizeImportsCaseFirst.Upper => "upper",
            _ => "default",
        };
    }

    /// <summary>Go 的 <c>typeParsers[OrganizeImportsTypeOrder]</c>。</summary>
    private static bool TryParseOrganizeImportsTypeOrderValue(object? value, out OrganizeImportsTypeOrder result)
    {
        if (value is string s)
        {
            switch (s)
            {
                case "last":
                    result = OrganizeImportsTypeOrder.Last;
                    return true;
                case "inline":
                    result = OrganizeImportsTypeOrder.Inline;
                    return true;
                case "first":
                    result = OrganizeImportsTypeOrder.First;
                    return true;
            }
        }

        result = OrganizeImportsTypeOrder.Auto;
        return true;
    }

    /// <summary>Go 的 <c>typeSerializers[OrganizeImportsTypeOrder]</c>。</summary>
    private static object? SerializeOrganizeImportsTypeOrder(OrganizeImportsTypeOrder value)
    {
        return value switch
        {
            OrganizeImportsTypeOrder.Last => "last",
            OrganizeImportsTypeOrder.Inline => "inline",
            OrganizeImportsTypeOrder.First => "first",
            _ => "auto",
        };
    }

    /// <summary>Go 的 <c>typeParsers[modulespecifiers.ImportModuleSpecifierPreference]</c>。</summary>
    private static bool TryParseImportModuleSpecifierPreferenceValue(object? value, out ImportModuleSpecifierPreference result)
    {
        if (value is string s)
        {
            switch (ToLowerJS(s))
            {
                case "project-relative":
                    result = (ImportModuleSpecifierPreference)ImportModuleSpecifierPreferenceProjectRelative;
                    return true;
                case "relative":
                    result = (ImportModuleSpecifierPreference)ImportModuleSpecifierPreferenceRelative;
                    return true;
                case "non-relative":
                    result = (ImportModuleSpecifierPreference)ImportModuleSpecifierPreferenceNonRelative;
                    return true;
            }
        }

        result = (ImportModuleSpecifierPreference)ImportModuleSpecifierPreferenceShortest;
        return true;
    }

    /// <summary>Go 的 <c>typeSerializers[modulespecifiers.ImportModuleSpecifierPreference]</c>（零值 → nil）。</summary>
    private static object? SerializeImportModuleSpecifierPreference(ImportModuleSpecifierPreference value) =>
        string.IsNullOrEmpty(value.Value) ? null : value.Value;

    /// <summary>Go 的 <c>typeParsers[modulespecifiers.ImportModuleSpecifierEndingPreference]</c>。</summary>
    private static bool TryParseImportModuleSpecifierEndingPreferenceValue(object? value, out ImportModuleSpecifierEndingPreference result)
    {
        if (value is string s)
        {
            switch (ToLowerJS(s))
            {
                case "minimal":
                    result = (ImportModuleSpecifierEndingPreference)ImportModuleSpecifierEndingPreferenceMinimal;
                    return true;
                case "index":
                    result = (ImportModuleSpecifierEndingPreference)ImportModuleSpecifierEndingPreferenceIndex;
                    return true;
                case "js":
                    result = (ImportModuleSpecifierEndingPreference)ImportModuleSpecifierEndingPreferenceJs;
                    return true;
            }
        }

        result = (ImportModuleSpecifierEndingPreference)ImportModuleSpecifierEndingPreferenceAuto;
        return true;
    }

    /// <summary>Go 的 <c>typeSerializers[modulespecifiers.ImportModuleSpecifierEndingPreference]</c>（零值 → nil）。</summary>
    private static object? SerializeImportModuleSpecifierEndingPreference(ImportModuleSpecifierEndingPreference value) =>
        string.IsNullOrEmpty(value.Value) ? null : value.Value;

    /// <summary>Go 的 <c>setFieldFromValue</c> 的 <c>reflect.String</c> 分支（WorkspaceSymbolsScope 没有专门的 parser）。</summary>
    private static bool TryParseWorkspaceSymbolsScopeValue(object? value, out WorkspaceSymbolsScope result)
    {
        if (value is string s)
        {
            result = new WorkspaceSymbolsScope(s);
            return true;
        }

        result = new WorkspaceSymbolsScope("");
        return false;
    }

    /// <summary>Go 的 <c>serializeField</c> 的 <c>reflect.String</c> 分支。</summary>
    private static object? SerializeWorkspaceSymbolsScope(WorkspaceSymbolsScope value) =>
        string.IsNullOrEmpty(value.Value) ? null : value.Value;

    // ── 字段登记表（顺序 = Go 的 collectFieldInfos 遍历顺序）──

    /// <summary>Go 的未导出 <c>collectFieldInfos(reflect.TypeFor[UserPreferences](), nil)</c>。</summary>
    private static PreferenceFieldInfo[] CollectFieldInfos()
    {
        var infos = new List<PreferenceFieldInfo>(83);

        // ── FormatCodeSettings（Go 里 UserPreferences 的首个字段、本身无 tag ⇒ collectFieldInfos 递归进来）──

        // EditorSettings（Go 的内嵌结构，同样无 tag ⇒ 再递归一层；C# 侧这 7 个字段平铺在 FormatCodeSettings 上）
        AddInt(infos, "baseIndentSize", "format.baseIndentSize", [],
            static p => p.FormatCodeSettings.BaseIndentSize,
            static (ref UserPreferences p, int v) => p.FormatCodeSettings.BaseIndentSize = v);
        AddInt(infos, "indentSize", "format.indentSize", [],
            static p => p.FormatCodeSettings.IndentSize,
            static (ref UserPreferences p, int v) => p.FormatCodeSettings.IndentSize = v);
        AddInt(infos, "tabSize", "format.tabSize", [],
            static p => p.FormatCodeSettings.TabSize,
            static (ref UserPreferences p, int v) => p.FormatCodeSettings.TabSize = v);
        AddString(infos, "newLineCharacter", "format.newLineCharacter", [],
            static p => p.FormatCodeSettings.NewLineCharacter,
            static (ref UserPreferences p, string v) => p.FormatCodeSettings.NewLineCharacter = v);
        AddTristate(infos, "convertTabsToSpaces", "format.convertTabsToSpaces", [],
            static p => p.FormatCodeSettings.ConvertTabsToSpaces,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.ConvertTabsToSpaces = v);
        AddIndentStyle(infos, "indentStyle", "format.indentStyle", [],
            static p => p.FormatCodeSettings.IndentStyle,
            static (ref UserPreferences p, IndentStyle v) => p.FormatCodeSettings.IndentStyle = v);
        AddTristate(infos, "trimTrailingWhitespace", "format.trimTrailingWhitespace", [],
            static p => p.FormatCodeSettings.TrimTrailingWhitespace,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.TrimTrailingWhitespace = v);

        AddTristate(infos, "insertSpaceAfterCommaDelimiter", "format.insertSpaceAfterCommaDelimiter", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterCommaDelimiter,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterCommaDelimiter = v);
        AddTristate(infos, "insertSpaceAfterSemicolonInForStatements", "format.insertSpaceAfterSemicolonInForStatements", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterSemicolonInForStatements,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterSemicolonInForStatements = v);
        AddTristate(infos, "insertSpaceBeforeAndAfterBinaryOperators", "format.insertSpaceBeforeAndAfterBinaryOperators", [],
            static p => p.FormatCodeSettings.InsertSpaceBeforeAndAfterBinaryOperators,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceBeforeAndAfterBinaryOperators = v);
        AddTristate(infos, "insertSpaceAfterConstructor", "format.insertSpaceAfterConstructor", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterConstructor,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterConstructor = v);
        AddTristate(infos, "insertSpaceAfterKeywordsInControlFlowStatements", "format.insertSpaceAfterKeywordsInControlFlowStatements", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterKeywordsInControlFlowStatements,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterKeywordsInControlFlowStatements = v);
        AddTristate(infos, "insertSpaceAfterFunctionKeywordForAnonymousFunctions", "format.insertSpaceAfterFunctionKeywordForAnonymousFunctions", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterFunctionKeywordForAnonymousFunctions,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterFunctionKeywordForAnonymousFunctions = v);
        AddTristate(infos, "insertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesis", "format.insertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesis", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesis,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesis = v);
        AddTristate(infos, "insertSpaceAfterOpeningAndBeforeClosingNonemptyBrackets", "format.insertSpaceAfterOpeningAndBeforeClosingNonemptyBrackets", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingNonemptyBrackets,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingNonemptyBrackets = v);
        AddTristate(infos, "insertSpaceAfterOpeningAndBeforeClosingNonemptyBraces", "format.insertSpaceAfterOpeningAndBeforeClosingNonemptyBraces", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingNonemptyBraces,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingNonemptyBraces = v);
        AddTristate(infos, "insertSpaceAfterOpeningAndBeforeClosingEmptyBraces", "format.insertSpaceAfterOpeningAndBeforeClosingEmptyBraces", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingEmptyBraces,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingEmptyBraces = v);
        AddTristate(infos, "insertSpaceAfterOpeningAndBeforeClosingTemplateStringBraces", "format.insertSpaceAfterOpeningAndBeforeClosingTemplateStringBraces", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBraces,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBraces = v);
        AddTristate(infos, "insertSpaceAfterOpeningAndBeforeClosingJsxExpressionBraces", "format.insertSpaceAfterOpeningAndBeforeClosingJsxExpressionBraces", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBraces,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBraces = v);
        AddTristate(infos, "insertSpaceAfterTypeAssertion", "format.insertSpaceAfterTypeAssertion", [],
            static p => p.FormatCodeSettings.InsertSpaceAfterTypeAssertion,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceAfterTypeAssertion = v);
        AddTristate(infos, "insertSpaceBeforeFunctionParenthesis", "format.insertSpaceBeforeFunctionParenthesis", [],
            static p => p.FormatCodeSettings.InsertSpaceBeforeFunctionParenthesis,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceBeforeFunctionParenthesis = v);
        AddTristate(infos, "placeOpenBraceOnNewLineForFunctions", "format.placeOpenBraceOnNewLineForFunctions", [],
            static p => p.FormatCodeSettings.PlaceOpenBraceOnNewLineForFunctions,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.PlaceOpenBraceOnNewLineForFunctions = v);
        AddTristate(infos, "placeOpenBraceOnNewLineForControlBlocks", "format.placeOpenBraceOnNewLineForControlBlocks", [],
            static p => p.FormatCodeSettings.PlaceOpenBraceOnNewLineForControlBlocks,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.PlaceOpenBraceOnNewLineForControlBlocks = v);
        AddTristate(infos, "insertSpaceBeforeTypeAnnotation", "format.insertSpaceBeforeTypeAnnotation", [],
            static p => p.FormatCodeSettings.InsertSpaceBeforeTypeAnnotation,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.InsertSpaceBeforeTypeAnnotation = v);
        AddTristate(infos, "indentMultiLineObjectLiteralBeginningOnBlankLine", "format.indentMultiLineObjectLiteralBeginningOnBlankLine", [],
            static p => p.FormatCodeSettings.IndentMultiLineObjectLiteralBeginningOnBlankLine,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.IndentMultiLineObjectLiteralBeginningOnBlankLine = v);
        AddSemicolonPreference(infos, "semicolons", "format.semicolons", [],
            static p => p.FormatCodeSettings.Semicolons,
            static (ref UserPreferences p, SemicolonPreference v) => p.FormatCodeSettings.Semicolons = v);
        AddTristate(infos, "indentSwitchCase", "format.indentSwitchCase", [],
            static p => p.FormatCodeSettings.IndentSwitchCase,
            static (ref UserPreferences p, Tristate v) => p.FormatCodeSettings.IndentSwitchCase = v);

        // ── UserPreferences 自己的字段 ──
        AddQuotePreference(infos, "quotePreference", "preferences.quoteStyle", [],
            static p => p.QuotePreference,
            static (ref UserPreferences p, QuotePreference v) => p.QuotePreference = v);
        AddTristate(infos, "lazyConfiguredProjectsFromExternalProject", "", [],
            static p => p.LazyConfiguredProjectsFromExternalProject,
            static (ref UserPreferences p, Tristate v) => p.LazyConfiguredProjectsFromExternalProject = v);
        AddInt(infos, "maximumHoverLength", "", [],
            static p => p.MaximumHoverLength,
            static (ref UserPreferences p, int v) => p.MaximumHoverLength = v);

        // ------- Completions -------
        AddTristate(infos, "includeCompletionsForModuleExports", "suggest.autoImports", [],
            static p => p.IncludeCompletionsForModuleExports,
            static (ref UserPreferences p, Tristate v) => p.IncludeCompletionsForModuleExports = v);
        AddTristate(infos, "includeCompletionsForImportStatements", "suggest.includeCompletionsForImportStatements", [],
            static p => p.IncludeCompletionsForImportStatements,
            static (ref UserPreferences p, Tristate v) => p.IncludeCompletionsForImportStatements = v);
        AddTristate(infos, "includeAutomaticOptionalChainCompletions", "suggest.includeAutomaticOptionalChainCompletions", [],
            static p => p.IncludeAutomaticOptionalChainCompletions,
            static (ref UserPreferences p, Tristate v) => p.IncludeAutomaticOptionalChainCompletions = v);
        AddTristate(infos, "includeCompletionsWithClassMemberSnippets", "suggest.classMemberSnippets.enabled", [],
            static p => p.IncludeCompletionsWithClassMemberSnippets,
            static (ref UserPreferences p, Tristate v) => p.IncludeCompletionsWithClassMemberSnippets = v);
        AddTristate(infos, "includeCompletionsWithObjectLiteralMethodSnippets", "suggest.objectLiteralMethodSnippets.enabled", [],
            static p => p.IncludeCompletionsWithObjectLiteralMethodSnippets,
            static (ref UserPreferences p, Tristate v) => p.IncludeCompletionsWithObjectLiteralMethodSnippets = v);
        AddJsxAttributeCompletionStyle(infos, "jsxAttributeCompletionStyle", "preferences.jsxAttributeCompletionStyle", [],
            static p => p.JsxAttributeCompletionStyle,
            static (ref UserPreferences p, JsxAttributeCompletionStyle v) => p.JsxAttributeCompletionStyle = v);
        AddTristate(infos, "autoClosingTags", "autoClosingTags.enabled", ["autoClosingTags"],
            static p => p.EnableAutoClosingTags,
            static (ref UserPreferences p, Tristate v) => p.EnableAutoClosingTags = v);
        AddTristate(infos, "completeJSDocs", "suggest.jsdoc.enabled", ["suggest.completeJSDocs"],
            static p => p.EnableJSDocCompletions,
            static (ref UserPreferences p, Tristate v) => p.EnableJSDocCompletions = v);
        AddTristate(infos, "generateReturnInDocTemplate", "suggest.jsdoc.generateReturns", [],
            static p => p.GenerateReturnInDocTemplate,
            static (ref UserPreferences p, Tristate v) => p.GenerateReturnInDocTemplate = v);

        // ------- AutoImports --------
        AddImportModuleSpecifierPreference(infos, "importModuleSpecifierPreference", "preferences.importModuleSpecifier", [],
            static p => p.ImportModuleSpecifierPreference,
            static (ref UserPreferences p, ImportModuleSpecifierPreference v) => p.ImportModuleSpecifierPreference = v);
        AddImportModuleSpecifierEnding(infos, "importModuleSpecifierEnding", "preferences.importModuleSpecifierEnding", [],
            static p => p.ImportModuleSpecifierEnding,
            static (ref UserPreferences p, ImportModuleSpecifierEndingPreference v) => p.ImportModuleSpecifierEnding = v);
        AddStringSlice(infos, "autoImportSpecifierExcludeRegexes", "preferences.autoImportSpecifierExcludeRegexes", [],
            static p => p.AutoImportSpecifierExcludeRegexes,
            static (ref UserPreferences p, string[]? v) => p.AutoImportSpecifierExcludeRegexes = v);
        AddStringSlice(infos, "autoImportFileExcludePatterns", "preferences.autoImportFileExcludePatterns", [],
            static p => p.AutoImportFileExcludePatterns,
            static (ref UserPreferences p, string[]? v) => p.AutoImportFileExcludePatterns = v);
        AddTristate(infos, "autoImportEntrypointDirectorySearch", "preferences.autoImportEntrypointDirectorySearch", [],
            static p => p.AutoImportEntrypointDirectorySearch,
            static (ref UserPreferences p, Tristate v) => p.AutoImportEntrypointDirectorySearch = v);
        AddTristate(infos, "preferTypeOnlyAutoImports", "preferences.preferTypeOnlyAutoImports", [],
            static p => p.PreferTypeOnlyAutoImports,
            static (ref UserPreferences p, Tristate v) => p.PreferTypeOnlyAutoImports = v);

        // ------- OrganizeImports -------
        AddOrganizeImportsSort(infos, "organizeImportsSort", "preferences.organizeImports.sort", [],
            static p => p.OrganizeImportsSort,
            static (ref UserPreferences p, OrganizeImportsSort v) => p.OrganizeImportsSort = v);
        AddTristate(infos, "organizeImportsIgnoreCase", "preferences.organizeImports.caseSensitivity", [],
            static p => p.OrganizeImportsIgnoreCase,
            static (ref UserPreferences p, Tristate v) => p.OrganizeImportsIgnoreCase = v);
        AddOrganizeImportsCollation(infos, "organizeImportsCollation", "preferences.organizeImports.unicodeCollation", [],
            static p => p.OrganizeImportsCollation,
            static (ref UserPreferences p, OrganizeImportsCollation v) => p.OrganizeImportsCollation = v);
        AddString(infos, "organizeImportsLocale", "preferences.organizeImports.locale", [],
            static p => p.OrganizeImportsLocale,
            static (ref UserPreferences p, string v) => p.OrganizeImportsLocale = v);
        AddTristate(infos, "organizeImportsNumericCollation", "preferences.organizeImports.numericCollation", [],
            static p => p.OrganizeImportsNumericCollation,
            static (ref UserPreferences p, Tristate v) => p.OrganizeImportsNumericCollation = v);
        AddTristate(infos, "organizeImportsAccentCollation", "preferences.organizeImports.accentCollation", [],
            static p => p.OrganizeImportsAccentCollation,
            static (ref UserPreferences p, Tristate v) => p.OrganizeImportsAccentCollation = v);
        AddOrganizeImportsCaseFirst(infos, "organizeImportsCaseFirst", "preferences.organizeImports.caseFirst", [],
            static p => p.OrganizeImportsCaseFirst,
            static (ref UserPreferences p, OrganizeImportsCaseFirst v) => p.OrganizeImportsCaseFirst = v);
        AddOrganizeImportsTypeOrder(infos, "organizeImportsTypeOrder", "preferences.organizeImports.typeOrder", [],
            static p => p.OrganizeImportsTypeOrder,
            static (ref UserPreferences p, OrganizeImportsTypeOrder v) => p.OrganizeImportsTypeOrder = v);

        // ------- MoveToFile -------
        AddTristate(infos, "allowTextChangesInNewFiles", "", [],
            static p => p.AllowTextChangesInNewFiles,
            static (ref UserPreferences p, Tristate v) => p.AllowTextChangesInNewFiles = v);

        // ------- Rename -------
        AddTristate(infos, "providePrefixAndSuffixTextForRename", "preferences.useAliasesForRenames", [],
            static p => p.UseAliasesForRename,
            static (ref UserPreferences p, Tristate v) => p.UseAliasesForRename = v);
        AddTristate(infos, "allowRenameOfImportPath", "", [],
            static p => p.AllowRenameOfImportPath,
            static (ref UserPreferences p, Tristate v) => p.AllowRenameOfImportPath = v);

        // ------- CodeFixes/Refactors -------
        AddTristate(infos, "provideRefactorNotApplicableReason", "", [],
            static p => p.ProvideRefactorNotApplicableReason,
            static (ref UserPreferences p, Tristate v) => p.ProvideRefactorNotApplicableReason = v);

        // ------- InlayHints -------
        AddIncludeInlayParameterNameHints(infos, "includeInlayParameterNameHints", "inlayHints.parameterNames.enabled", [],
            static p => p.InlayHints.IncludeInlayParameterNameHints,
            static (ref UserPreferences p, IncludeInlayParameterNameHints v) => p.InlayHints.IncludeInlayParameterNameHints = v);
        AddTristate(infos, "includeInlayParameterNameHintsWhenArgumentMatchesName", "inlayHints.parameterNames.suppressWhenArgumentMatchesName", [],
            static p => p.InlayHints.IncludeInlayParameterNameHintsWhenArgumentMatchesName,
            static (ref UserPreferences p, Tristate v) => p.InlayHints.IncludeInlayParameterNameHintsWhenArgumentMatchesName = v,
            configInvert: true);
        AddTristate(infos, "includeInlayFunctionParameterTypeHints", "inlayHints.parameterTypes.enabled", [],
            static p => p.InlayHints.IncludeInlayFunctionParameterTypeHints,
            static (ref UserPreferences p, Tristate v) => p.InlayHints.IncludeInlayFunctionParameterTypeHints = v);
        AddTristate(infos, "includeInlayVariableTypeHints", "inlayHints.variableTypes.enabled", [],
            static p => p.InlayHints.IncludeInlayVariableTypeHints,
            static (ref UserPreferences p, Tristate v) => p.InlayHints.IncludeInlayVariableTypeHints = v);
        AddTristate(infos, "includeInlayVariableTypeHintsWhenTypeMatchesName", "inlayHints.variableTypes.suppressWhenTypeMatchesName", [],
            static p => p.InlayHints.IncludeInlayVariableTypeHintsWhenTypeMatchesName,
            static (ref UserPreferences p, Tristate v) => p.InlayHints.IncludeInlayVariableTypeHintsWhenTypeMatchesName = v,
            configInvert: true);
        AddTristate(infos, "includeInlayPropertyDeclarationTypeHints", "inlayHints.propertyDeclarationTypes.enabled", [],
            static p => p.InlayHints.IncludeInlayPropertyDeclarationTypeHints,
            static (ref UserPreferences p, Tristate v) => p.InlayHints.IncludeInlayPropertyDeclarationTypeHints = v);
        AddTristate(infos, "includeInlayFunctionLikeReturnTypeHints", "inlayHints.functionLikeReturnTypes.enabled", [],
            static p => p.InlayHints.IncludeInlayFunctionLikeReturnTypeHints,
            static (ref UserPreferences p, Tristate v) => p.InlayHints.IncludeInlayFunctionLikeReturnTypeHints = v);
        AddTristate(infos, "includeInlayEnumMemberValueHints", "inlayHints.enumMemberValues.enabled", [],
            static p => p.InlayHints.IncludeInlayEnumMemberValueHints,
            static (ref UserPreferences p, Tristate v) => p.InlayHints.IncludeInlayEnumMemberValueHints = v);

        // ------- CodeLens -------
        AddTristate(infos, "referencesCodeLensEnabled", "referencesCodeLens.enabled", [],
            static p => p.CodeLens.ReferencesCodeLensEnabled,
            static (ref UserPreferences p, Tristate v) => p.CodeLens.ReferencesCodeLensEnabled = v);
        AddTristate(infos, "implementationsCodeLensEnabled", "implementationsCodeLens.enabled", [],
            static p => p.CodeLens.ImplementationsCodeLensEnabled,
            static (ref UserPreferences p, Tristate v) => p.CodeLens.ImplementationsCodeLensEnabled = v);
        AddTristate(infos, "referencesCodeLensShowOnAllFunctions", "referencesCodeLens.showOnAllFunctions", [],
            static p => p.CodeLens.ReferencesCodeLensShowOnAllFunctions,
            static (ref UserPreferences p, Tristate v) => p.CodeLens.ReferencesCodeLensShowOnAllFunctions = v);
        AddTristate(infos, "implementationsCodeLensShowOnInterfaceMethods", "implementationsCodeLens.showOnInterfaceMethods", [],
            static p => p.CodeLens.ImplementationsCodeLensShowOnInterfaceMethods,
            static (ref UserPreferences p, Tristate v) => p.CodeLens.ImplementationsCodeLensShowOnInterfaceMethods = v);
        AddTristate(infos, "implementationsCodeLensShowOnAllClassMethods", "implementationsCodeLens.showOnAllClassMethods", [],
            static p => p.CodeLens.ImplementationsCodeLensShowOnAllClassMethods,
            static (ref UserPreferences p, Tristate v) => p.CodeLens.ImplementationsCodeLensShowOnAllClassMethods = v);

        // ------- Definition -------
        AddBool(infos, "preferGoToSourceDefinition", "", [],
            static p => p.PreferGoToSourceDefinition,
            static (ref UserPreferences p, bool v) => p.PreferGoToSourceDefinition = v);

        // ------- Symbols -------
        AddTristate(infos, "excludeLibrarySymbolsInNavTo", "workspaceSymbols.excludeLibrarySymbols", [],
            static p => p.ExcludeLibrarySymbolsInNavTo,
            static (ref UserPreferences p, Tristate v) => p.ExcludeLibrarySymbolsInNavTo = v);
        AddWorkspaceSymbolsScope(infos, "", "workspaceSymbols.scope", [],
            static p => p.WorkspaceSymbolsScope,
            static (ref UserPreferences p, WorkspaceSymbolsScope v) => p.WorkspaceSymbolsScope = v);

        // ------- Misc -------
        AddTristate(infos, "formatEnabled", "format.enabled", ["format.enable"],
            static p => p.EnableFormatting,
            static (ref UserPreferences p, Tristate v) => p.EnableFormatting = v);
        AddTristate(infos, "validateEnabled", "validate.enabled", ["validate.enable"],
            static p => p.EnableValidation,
            static (ref UserPreferences p, Tristate v) => p.EnableValidation = v);
        AddTristate(infos, "disableSuggestions", "", [],
            static p => p.DisableSuggestions,
            static (ref UserPreferences p, Tristate v) => p.DisableSuggestions = v);
        AddTristate(infos, "disableLineTextInReferences", "", [],
            static p => p.DisableLineTextInReferences,
            static (ref UserPreferences p, Tristate v) => p.DisableLineTextInReferences = v);
        AddTristate(infos, "displayPartsForJSDoc", "", [],
            static p => p.DisplayPartsForJSDoc,
            static (ref UserPreferences p, Tristate v) => p.DisplayPartsForJSDoc = v);
        AddTristate(infos, "reportStyleChecksAsWarnings", "reportStyleChecksAsWarnings", [],
            static p => p.ReportStyleChecksAsWarnings,
            static (ref UserPreferences p, Tristate v) => p.ReportStyleChecksAsWarnings = v);
        AddString(infos, "", "locale", [],
            static p => p.Locale,
            static (ref UserPreferences p, string v) => p.Locale = v);

        // ------- ATA -------
        AddTristate(infos, "disableAutomaticTypeAcquisition", "disableAutomaticTypeAcquisition", [],
            static p => p.DisableAutomaticTypeAcquisition,
            static (ref UserPreferences p, Tristate v) => p.DisableAutomaticTypeAcquisition = v);
        AddTristate(infos, "automaticTypeAcquisitionEnabled", "tsserver.automaticTypeAcquisition.enabled", [],
            static p => p.AutomaticTypeAcquisitionEnabled,
            static (ref UserPreferences p, Tristate v) => p.AutomaticTypeAcquisitionEnabled = v);

        // ------- Project Configuration -------
        AddString(infos, "customConfigFileName", "customConfigFileName", [],
            static p => p.CustomConfigFileName,
            static (ref UserPreferences p, string v) => p.CustomConfigFileName = v);

        return infos.ToArray();
    }
}

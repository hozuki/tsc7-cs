// ported from: internal/ast/diagnostic.go
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2、§3.2、§3.3、§3.8）：
// - 包级函数（NewDiagnostic*/NewCompilerDiagnostic/NewExternalDiagnostic/EqualDiagnostics*/CompareDiagnostics/
//   EqualDiagnosticsNoRelatedInfo）落在静态类 `Ast`（与 Go 的 package ast 同名，§2），调用点写
//   `Ast.NewDiagnostic(...)`，或测试/调用方 `using static TypeScript.Ast.Ast;` 后按 Go 的裸名调用。
// - Go 的未导出字段 → `_camelCase`；未导出函数 → PascalCase（`displayMessageArgs` → `DisplayMessageArgs`、
//   `getDiagnosticLocationKey` → `GetDiagnosticLocationKey`）。
// - `diagnostics.Message` 在 Go 是 `*Message`，C# 是 class → 判空写 `_message is null`；Go 的 `Message.String()`
//   stringer → C# 的 `ToString()`（§3.3）。
// - `diagnostics.Key`（Go: `type Key string`）在字符串上下文里的 `string(key)` → `key.ToString()`（Key 是
//   包装 struct，`ToString()` 返回 `Value`）。
// - `locale.Locale` / 包级 var `locale.Default` → `using TSLocale = TypeScript.Locale.Locale;` + `TSLocale.Default`
//   （别名**不能**叫 `Locale`：类型名与命名空间 `TypeScript.Locale` 同名，外层命名空间成员优先于 using 导入的
//   类型，裸写会 CS0118，与 Json 的坑同源）。
// - Go 的 `(T, error)`/map 的 nil 语义：`map[K]V` → `Dictionary<K,List<V>>`，Go 的 nil map 分支保留
//   （C# 的 null 字典 + 惰性创建），"取不存在的键得到 nil 切片" → `GetValueOrDefault` + `is null`。
// - `diagnosticLocationKey`（Go 结构体 map 键）→ `internal readonly record struct`（值语义 = Go 的 `==`）。
// - `sync.Mutex` → `lock`（`DiagnosticsCollection` 的并发契约与 Go 相同）。
// - `slices.SortStableFunc`/`slices.BinarySearchFunc`/`slices.Equal`/`slices.EqualFunc`/`slices.Compare` 在 C# 里
//   没有等价物 → 本文件的私有助手（`SortStable`/`BinarySearchByDiagnostic`/`EqualStrings`/`CompareStrings`/
//   `EqualDiagnosticLists`），按 Go 的语义逐条实现（含 nil 切片 == 空切片）。
// - `displayMessageArgs` 里 Go 的字符串切片视图 → `AsSpan`（§3.8）；只有真的命中别名时才把原文切片
//   `ToString()` 成字符串（Go 的切片视图零拷贝，C# 的 Substring 会分配）。
// - Go 的 `Clone()`（结构体浅拷贝 `result := *d`）→ `MemberwiseClone()`（§3.2：与 Go 的 reflect 浅拷贝同义）。
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using Diag = TypeScript.Diagnostics.Diagnostics;
using Path = TypeScript.TSPath.Path;
using TSLocale = TypeScript.Locale.Locale;

namespace TypeScript.Ast;

/// <summary>
/// Go: <c>type RepopulateDiagnosticKind int</c>（成员名去 Go 的 <c>Repopulate</c> 前缀，§3.3）。
/// </summary>
public enum RepopulateDiagnosticKind
{
    /// <summary>Go: <c>RepopulateModeMismatch</c>。</summary>
    ModeMismatch = 1,

    /// <summary>Go: <c>RepopulateModuleNotFound</c>。</summary>
    ModuleNotFound = 2,
}

/// <summary>
/// Go: <c>type RepopulateDiagnosticInfo struct</c>。增量构建时重算诊断链条目所需的信息。
/// </summary>
public sealed class RepopulateDiagnosticInfo
{
    public RepopulateDiagnosticKind Kind;
    public string ModuleReference = "";

    /// <summary>Go: <c>Mode core.ResolutionMode</c>（= ModuleKind 别名，C# 没有别名，见 Core/CompilerOptions.cs）。</summary>
    public ModuleKind Mode;

    public string PackageName = "";
}

/// <summary>Go: <c>type Diagnostic struct</c>。一个诊断（可带消息链与相关信息）。</summary>
public sealed class Diagnostic
{
    private SourceFile? _file;
    private TextRange _loc;
    private readonly int _code;
    private Category _category;

    /// <summary>
    /// Go: <c>source</c>。非空时是自定义前缀（例如内容映射器的名字），显示在 code 前而不是 "TS"；它把
    /// 诊断标记为来自外部源，其区间指向文件的**原始**（未变换）文本。
    /// </summary>
    private string _source = "";

    /// <summary>Go: <c>message *diagnostics.Message</c>（原始消息，可能为 nil）。</summary>
    internal readonly Message? _message;

    /// <summary>
    /// Go: <c>messageText</c>。message 为 nil 时使用的已本地化文本（例如从外部进程反序列化、自带本地化的诊断）。
    /// </summary>
    private string _messageText = "";

    private readonly Key _messageKey;
    private string[]? _messageArgs;
    private Diagnostic[]? _messageChain;
    private Diagnostic[]? _relatedInformation;
    private readonly bool _reportsUnnecessary;
    private readonly bool _reportsDeprecated;
    private bool _skippedOnNoEmit;
    private RepopulateDiagnosticInfo? _repopulateInfo;

    internal Diagnostic(
        SourceFile? file,
        TextRange loc,
        int code,
        Category category,
        string source,
        Message? message,
        string messageText,
        Key messageKey,
        string[]? messageArgs,
        Diagnostic[]? messageChain,
        Diagnostic[]? relatedInformation,
        bool reportsUnnecessary,
        bool reportsDeprecated,
        bool skippedOnNoEmit)
    {
        _file = file;
        _loc = loc;
        _code = code;
        _category = category;
        _source = source;
        _message = message;
        _messageText = messageText;
        _messageKey = messageKey;
        _messageArgs = messageArgs;
        _messageChain = messageChain;
        _relatedInformation = relatedInformation;
        _reportsUnnecessary = reportsUnnecessary;
        _reportsDeprecated = reportsDeprecated;
        _skippedOnNoEmit = skippedOnNoEmit;
    }

    public SourceFile? File() => _file;

    public int Pos() => _loc.Pos();

    public int End() => _loc.End();

    public int Len() => _loc.Len();

    public TextRange Loc() => _loc;

    public int Code() => _code;

    public Category Category() => _category;

    public string Source() => _source;

    public string MessageText() => _messageText;

    public Key MessageKey() => _messageKey;

    public string[]? MessageArgs() => _messageArgs;

    public Diagnostic[]? MessageChain() => _messageChain;

    public Diagnostic[]? RelatedInformation() => _relatedInformation;

    public bool ReportsUnnecessary() => _reportsUnnecessary;

    public bool ReportsDeprecated() => _reportsDeprecated;

    public bool SkippedOnNoEmit() => _skippedOnNoEmit;

    public RepopulateDiagnosticInfo? RepopulateInfo() => _repopulateInfo;

    public void SetFile(SourceFile? file) => _file = file;

    public void SetLocation(TextRange loc) => _loc = loc;

    public void SetCategory(Category category) => _category = category;

    public void SetSkippedOnNoEmit() => _skippedOnNoEmit = true;

    public void SetRepopulateInfo(RepopulateDiagnosticInfo? info) => _repopulateInfo = info;

    public Diagnostic SetExternalData(string source, string messageText)
    {
        _source = source;
        _messageText = messageText;
        return this;
    }

    public Diagnostic SetMessageChain(Diagnostic[]? messageChain)
    {
        _messageChain = messageChain;
        return this;
    }

    public Diagnostic AddMessageChain(Diagnostic? messageChain)
    {
        if (messageChain is not null)
        {
            _messageChain = [.. _messageChain ?? [], messageChain];
        }

        return this;
    }

    public Diagnostic SetRelatedInfo(Diagnostic[]? relatedInformation)
    {
        _relatedInformation = relatedInformation;
        return this;
    }

    public Diagnostic AddRelatedInfo(Diagnostic? relatedInformation)
    {
        if (relatedInformation is not null)
        {
            _relatedInformation = [.. _relatedInformation ?? [], relatedInformation];
        }

        return this;
    }

    /// <summary>Go: <c>func (d *Diagnostic) Clone() *Diagnostic</c>（结构体浅拷贝 → MemberwiseClone）。</summary>
    public Diagnostic Clone() => (Diagnostic)MemberwiseClone();

    /// <summary>Go: <c>func (d *Diagnostic) Localize(locale locale.Locale) string</c>。</summary>
    public string Localize(TSLocale locale)
    {
        if (_message is null && _messageText != "")
        {
            return _messageText;
        }

        return Diag.Localize(locale, _message, _messageKey, DisplayMessageArgs());
    }

    /// <summary>For debugging only.（Go: <c>func (d *Diagnostic) String() string</c> → <c>ToString()</c>，§3.3。）</summary>
    public override string ToString()
    {
        if (_message is null && _messageText != "")
        {
            return _messageText;
        }

        return Diag.Localize(TSLocale.Default, _message, _messageKey, DisplayMessageArgs());
    }

    /// <summary>
    /// Go: <c>func (d *Diagnostic) displayMessageArgs() []string</c>。当诊断参数与完整的 alias 段完全一致时，
    /// 用原文文本替换它；存储的参数保持不变（供 code fix 与序列化使用）。
    /// </summary>
    private string[]? DisplayMessageArgs()
    {
        if (_file is null || _source != "")
        {
            return _messageArgs;
        }

        var map = _file.SpanMap();
        if (map?.TryAliasForVirtualSpan(_loc, out var segment) != true)
        {
            return _messageArgs;
        }

        var virtualText = _file.Text();
        var originalText = _file.OriginalText();
        if (segment.VirtualStart.Value < 0 || segment.VirtualEnd.Value > virtualText.Length ||
            segment.OriginalStart.Value < 0 || segment.OriginalEnd.Value > originalText.Length)
        {
            return _messageArgs;
        }

        var virtualName = virtualText.AsSpan(segment.VirtualStart.Value, segment.VirtualEnd.Value - segment.VirtualStart.Value);
        var originalName = originalText.AsSpan(segment.OriginalStart.Value, segment.OriginalEnd.Value - segment.OriginalStart.Value);
        string[]? result = null;
        if (_messageArgs is not null)
        {
            for (var i = 0; i < _messageArgs.Length; i++)
            {
                if (!virtualName.SequenceEqual(_messageArgs[i].AsSpan()))
                {
                    continue;
                }

                result ??= (string[])_messageArgs.Clone();
                result[i] = originalName.ToString();
            }
        }

        return result ?? _messageArgs;
    }
}

public static partial class Ast
{
    /// <summary>Go: <c>func NewDiagnosticFromSerialized(...) *Diagnostic</c>。</summary>
    public static Diagnostic NewDiagnosticFromSerialized(
        SourceFile? file,
        TextRange loc,
        int code,
        Category category,
        Key messageKey,
        string[]? messageArgs,
        Diagnostic[]? messageChain,
        Diagnostic[]? relatedInformation,
        bool reportsUnnecessary,
        bool reportsDeprecated,
        bool skippedOnNoEmit) =>
        new(
            file,
            loc,
            code,
            category,
            "",
            null,
            "",
            messageKey,
            messageArgs,
            messageChain,
            relatedInformation,
            reportsUnnecessary,
            reportsDeprecated,
            skippedOnNoEmit);

    /// <summary>Go: <c>func NewDiagnosticFromText(...) *Diagnostic</c>。</summary>
    public static Diagnostic NewDiagnosticFromText(
        SourceFile? file,
        TextRange loc,
        int code,
        Category category,
        string text,
        Diagnostic[]? messageChain,
        Diagnostic[]? relatedInformation,
        bool reportsUnnecessary,
        bool reportsDeprecated) =>
        new(
            file,
            loc,
            code,
            category,
            "",
            NewAdHocMessage(text),
            "",
            default,
            null,
            messageChain,
            relatedInformation,
            reportsUnnecessary,
            reportsDeprecated,
            false);

    /// <summary>Go: <c>func NewDiagnostic(file *SourceFile, loc core.TextRange, message *diagnostics.Message, args ...any) *Diagnostic</c>。</summary>
    public static Diagnostic NewDiagnostic(SourceFile? file, TextRange loc, Message message, params object?[] args) =>
        new(
            file,
            loc,
            message.Code(),
            message.Category(),
            "",
            message,
            "",
            message.Key(),
            StringifyArgs(args),
            null,
            null,
            message.ReportsUnnecessary(),
            message.ReportsDeprecated(),
            false);

    /// <summary>Go: <c>func NewDiagnosticChain(chain *Diagnostic, message *diagnostics.Message, args ...any) *Diagnostic</c>。</summary>
    public static Diagnostic NewDiagnosticChain(Diagnostic? chain, Message message, params object?[] args)
    {
        if (chain is not null)
        {
            return NewDiagnostic(chain.File(), chain.Loc(), message, args)
                .AddMessageChain(chain)
                .SetRelatedInfo(chain.RelatedInformation());
        }

        return NewDiagnostic(null, default, message, args);
    }

    /// <summary>Go: <c>func NewCompilerDiagnostic(message *diagnostics.Message, args ...any) *Diagnostic</c>。</summary>
    public static Diagnostic NewCompilerDiagnostic(Message message, params object?[] args) =>
        NewDiagnostic(null, UndefinedTextRange(), message, args);

    /// <summary>
    /// Go: <c>func NewExternalDiagnostic(file *SourceFile, loc core.TextRange, source string, category diagnostics.Category, code int32, messageText string) *Diagnostic</c>。
    /// 由外部源（如内容映射器）上报的诊断：消息文本已本地化、code 用给定前缀（例如 "vue"）而不是 "TS"。
    /// </summary>
    public static Diagnostic NewExternalDiagnostic(
        SourceFile? file,
        TextRange loc,
        string source,
        Category category,
        int code,
        string messageText) =>
        new(
            file,
            loc,
            code,
            category,
            source,
            null,
            messageText,
            default,
            null,
            null,
            null,
            false,
            false,
            false);
}

/// <summary>Go: <c>type DiagnosticsCollection struct</c>：按位置去重、按文件归类的诊断集合（锁与 Go 相同）。</summary>
public sealed class DiagnosticsCollection
{
    private readonly object _mu = new();
    private int _count;
    private Dictionary<Path, List<Diagnostic>>? _fileDiagnostics;

    /// <summary>Go: <c>fileDiagnosticsSorted collections.Set[tspath.Path]</c>（值字段，零值 = 空集合）。</summary>
    private readonly Set<Path> _fileDiagnosticsSorted = new();

    private List<Diagnostic>? _nonFileDiagnostics;
    private bool _nonFileDiagnosticsSorted;
    private Dictionary<DiagnosticLocationKey, Diagnostic>? _diagnosticIndex;
    private Dictionary<DiagnosticLocationKey, List<Diagnostic>>? _diagnosticCollisions;

    /// <summary>Go: <c>func (c *DiagnosticsCollection) Add(diagnostic *Diagnostic) *Diagnostic</c>。</summary>
    public Diagnostic Add(Diagnostic diagnostic)
    {
        lock (_mu)
        {
            var key = Ast.GetDiagnosticLocationKey(diagnostic);
            var existing = _diagnosticIndex?.GetValueOrDefault(key);
            if (existing is not null)
            {
                if (Ast.EqualDiagnostics(existing, diagnostic))
                {
                    return existing;
                }

                if (_diagnosticCollisions is not null && _diagnosticCollisions.TryGetValue(key, out var collisions))
                {
                    foreach (var collision in collisions)
                    {
                        if (Ast.EqualDiagnostics(collision, diagnostic))
                        {
                            return collision;
                        }
                    }
                }
            }

            _diagnosticIndex ??= [];
            if (!_diagnosticIndex.ContainsKey(key))
            {
                _diagnosticIndex[key] = diagnostic;
            }
            else
            {
                _diagnosticCollisions ??= [];
                if (!_diagnosticCollisions.TryGetValue(key, out var list))
                {
                    list = [];
                    _diagnosticCollisions[key] = list;
                }

                list.Add(diagnostic);
            }

            _count++;

            var file = diagnostic.File();
            if (file is not null)
            {
                var path = file.Path();
                _fileDiagnostics ??= [];
                if (!_fileDiagnostics.TryGetValue(path, out var list))
                {
                    list = [];
                    _fileDiagnostics[path] = list;
                }

                list.Add(diagnostic);
                _fileDiagnosticsSorted.Delete(path);
            }
            else
            {
                _nonFileDiagnostics ??= [];
                _nonFileDiagnostics.Add(diagnostic);
                _nonFileDiagnosticsSorted = false;
            }

            return diagnostic;
        }
    }

    /// <summary>Go: <c>func (c *DiagnosticsCollection) Lookup(diagnostic *Diagnostic) *Diagnostic</c>。</summary>
    public Diagnostic? Lookup(Diagnostic diagnostic)
    {
        lock (_mu)
        {
            var file = diagnostic.File();
            var diagnostics = file is not null ? GetDiagnosticsForFileLocked(file) : GetGlobalDiagnosticsLocked();
            var i = BinarySearchByDiagnostic(diagnostics, diagnostic, out var found);
            return found ? diagnostics[i] : null;
        }
    }

    /// <summary>Go: <c>func (c *DiagnosticsCollection) GetGlobalDiagnostics() []*Diagnostic</c>。</summary>
    public Diagnostic[] GetGlobalDiagnostics()
    {
        lock (_mu)
        {
            return GetGlobalDiagnosticsLocked();
        }
    }

    private Diagnostic[] GetGlobalDiagnosticsLocked()
    {
        if (!_nonFileDiagnosticsSorted)
        {
            if (_nonFileDiagnostics is not null)
            {
                SortStable(_nonFileDiagnostics, Ast.CompareDiagnostics);
            }

            _nonFileDiagnosticsSorted = true;
        }

        return _nonFileDiagnostics is null ? [] : _nonFileDiagnostics.ToArray();
    }

    /// <summary>Go: <c>func (c *DiagnosticsCollection) GetDiagnosticsForFile(file *SourceFile) []*Diagnostic</c>。</summary>
    public Diagnostic[] GetDiagnosticsForFile(SourceFile file)
    {
        lock (_mu)
        {
            return GetDiagnosticsForFileLocked(file);
        }
    }

    private Diagnostic[] GetDiagnosticsForFileLocked(SourceFile file)
    {
        var path = file.Path();
        var diagnostics = _fileDiagnostics?.GetValueOrDefault(path);
        if (!_fileDiagnosticsSorted.Has(path))
        {
            if (diagnostics is not null)
            {
                SortStable(diagnostics, Ast.CompareDiagnostics);
            }

            _fileDiagnosticsSorted.Add(path);
        }

        return diagnostics is null ? [] : diagnostics.ToArray();
    }

    /// <summary>Go: <c>func (c *DiagnosticsCollection) GetDiagnostics() []*Diagnostic</c>。</summary>
    public Diagnostic[] GetDiagnostics()
    {
        lock (_mu)
        {
            var diagnostics = new List<Diagnostic>(_count);
            if (_nonFileDiagnostics is not null)
            {
                diagnostics.AddRange(_nonFileDiagnostics);
            }

            if (_fileDiagnostics is not null)
            {
                foreach (var diags in _fileDiagnostics.Values)
                {
                    diagnostics.AddRange(diags);
                }
            }

            // Go: slices.SortFunc（不稳定）；C# 的 List.Sort 同样不稳定。
            diagnostics.Sort(Ast.CompareDiagnostics);
            return diagnostics.ToArray();
        }
    }

    /// <summary>Go: <c>slices.SortStableFunc</c>（C# 的 Array/List.Sort 都不稳定，这里用下标兜底保持稳定性）。</summary>
    private static void SortStable(List<Diagnostic> list, Comparison<Diagnostic> comparison)
    {
        var snapshot = list.ToArray();
        var indices = new int[snapshot.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        Array.Sort(indices, (a, b) =>
        {
            var c = comparison(snapshot[a], snapshot[b]);
            return c != 0 ? c : a - b;
        });
        for (var i = 0; i < indices.Length; i++)
        {
            list[i] = snapshot[indices[i]];
        }
    }

    /// <summary>Go: <c>slices.BinarySearchFunc(diagnostics, diagnostic, CompareDiagnostics)</c>：插入点 + 是否命中。</summary>
    private static int BinarySearchByDiagnostic(Diagnostic[] diagnostics, Diagnostic target, out bool found)
    {
        var lo = 0;
        var hi = diagnostics.Length;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (Ast.CompareDiagnostics(diagnostics[mid], target) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        found = lo < diagnostics.Length && Ast.CompareDiagnostics(diagnostics[lo], target) == 0;
        return lo;
    }
}

/// <summary>
/// Go: <c>type diagnosticLocationKey struct { path tspath.Path; loc core.TextRange; code int32 }</c>（诊断去重键；
/// Go 的结构体 map 键 → C# 的 record struct 值语义）。
/// </summary>
internal readonly record struct DiagnosticLocationKey(Path Path, TextRange Loc, int Code);

public static partial class Ast
{
    internal static DiagnosticLocationKey GetDiagnosticLocationKey(Diagnostic diagnostic)
    {
        var file = diagnostic.File();
        var path = file is not null ? file.Path() : new Path("");
        return new DiagnosticLocationKey(path, diagnostic.Loc(), diagnostic.Code());
    }

    /// <summary>Go: <c>func getDiagnosticPath(d *Diagnostic) string</c>。</summary>
    private static string GetDiagnosticPath(Diagnostic d)
    {
        var file = d.File();
        return file is not null ? file.FileName() : "";
    }

    /// <summary>Go: <c>func EqualDiagnostics(d1, d2 *Diagnostic) bool</c>（指针相等 → 引用相等）。</summary>
    public static bool EqualDiagnostics(Diagnostic d1, Diagnostic d2)
    {
        if (ReferenceEquals(d1, d2))
        {
            return true;
        }

        return EqualDiagnosticsNoRelatedInfo(d1, d2) &&
            EqualDiagnosticLists(d1.RelatedInformation(), d2.RelatedInformation());
    }

    /// <summary>Go: <c>func EqualDiagnosticsNoRelatedInfo(d1, d2 *Diagnostic) bool</c>。</summary>
    public static bool EqualDiagnosticsNoRelatedInfo(Diagnostic d1, Diagnostic d2)
    {
        if (ReferenceEquals(d1, d2))
        {
            return true;
        }

        return GetDiagnosticPath(d1) == GetDiagnosticPath(d2) &&
            d1.Loc() == d2.Loc() &&
            d1.Code() == d2.Code() &&
            d1.Category() == d2.Category() &&
            d1.Source() == d2.Source() &&
            GetDiagnosticMessageIdentity(d1) == GetDiagnosticMessageIdentity(d2) &&
            EqualStrings(d1.MessageArgs(), d2.MessageArgs()) &&
            EqualMessageChains(d1.MessageChain(), d2.MessageChain());
    }

    private static string GetDiagnosticMessageIdentity(Diagnostic diagnostic)
    {
        if (diagnostic.MessageText() != "")
        {
            return diagnostic.MessageText();
        }

        var message = diagnostic._message;
        if (message is not null && diagnostic.Code() == -1)
        {
            return message.ToString();
        }

        return diagnostic.MessageKey().ToString();
    }

    private static bool EqualMessageChain(Diagnostic c1, Diagnostic c2)
    {
        if (ReferenceEquals(c1, c2))
        {
            return true;
        }

        return c1.Code() == c2.Code() &&
            EqualStrings(c1.MessageArgs(), c2.MessageArgs()) &&
            EqualMessageChains(c1.MessageChain(), c2.MessageChain());
    }

    private static bool EqualMessageChains(Diagnostic[]? c1, Diagnostic[]? c2) =>
        EqualLists(c1, c2, EqualMessageChain);

    private static bool EqualDiagnosticLists(Diagnostic[]? c1, Diagnostic[]? c2) =>
        EqualLists(c1, c2, EqualDiagnostics);

    private static int CompareMessageChainSize(Diagnostic[]? c1, Diagnostic[]? c2)
    {
        var c = (c2?.Length ?? 0) - (c1?.Length ?? 0);
        if (c != 0)
        {
            return c;
        }

        for (var i = 0; i < (c1?.Length ?? 0); i++)
        {
            c = CompareMessageChainSize(c1![i].MessageChain(), c2![i].MessageChain());
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }

    private static int CompareMessageChainContent(Diagnostic[]? c1, Diagnostic[]? c2)
    {
        for (var i = 0; i < (c1?.Length ?? 0); i++)
        {
            var c = CompareStrings(c1![i].MessageArgs(), c2![i].MessageArgs());
            if (c != 0)
            {
                return c;
            }

            if (c1[i].MessageChain() is not null)
            {
                c = CompareMessageChainContent(c1[i].MessageChain(), c2[i].MessageChain());
                if (c != 0)
                {
                    return c;
                }
            }
        }

        return 0;
    }

    private static int CompareRelatedInfo(Diagnostic[]? r1, Diagnostic[]? r2)
    {
        var c = (r2?.Length ?? 0) - (r1?.Length ?? 0);
        if (c != 0)
        {
            return c;
        }

        for (var i = 0; i < (r1?.Length ?? 0); i++)
        {
            c = CompareDiagnostics(r1![i], r2![i]);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }

    /// <summary>Go: <c>func CompareDiagnostics(d1, d2 *Diagnostic) int</c>。</summary>
    public static int CompareDiagnostics(Diagnostic d1, Diagnostic d2)
    {
        if (ReferenceEquals(d1, d2))
        {
            return 0;
        }

        var c = string.CompareOrdinal(GetDiagnosticPath(d1), GetDiagnosticPath(d2));
        if (c != 0)
        {
            return c;
        }

        c = d1.Loc().Pos() - d2.Loc().Pos();
        if (c != 0)
        {
            return c;
        }

        c = d1.Loc().End() - d2.Loc().End();
        if (c != 0)
        {
            return c;
        }

        c = d1.Code() - d2.Code();
        if (c != 0)
        {
            return c;
        }

        c = (int)d1.Category() - (int)d2.Category();
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(d1.Source(), d2.Source());
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(GetDiagnosticMessageIdentity(d1), GetDiagnosticMessageIdentity(d2));
        if (c != 0)
        {
            return c;
        }

        c = CompareStrings(d1.MessageArgs(), d2.MessageArgs());
        if (c != 0)
        {
            return c;
        }

        c = CompareMessageChainSize(d1.MessageChain(), d2.MessageChain());
        if (c != 0)
        {
            return c;
        }

        c = CompareMessageChainContent(d1.MessageChain(), d2.MessageChain());
        if (c != 0)
        {
            return c;
        }

        return CompareRelatedInfo(d1.RelatedInformation(), d2.RelatedInformation());
    }

    /// <summary>Go: <c>slices.Equal</c>（nil 切片与空切片相等）。</summary>
    private static bool EqualStrings(string[]? a, string[]? b)
    {
        var aLength = a?.Length ?? 0;
        if (aLength != (b?.Length ?? 0))
        {
            return false;
        }

        for (var i = 0; i < aLength; i++)
        {
            if (a![i] != b![i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>slices.EqualFunc</c>。</summary>
    private static bool EqualLists<T>(T[]? a, T[]? b, Func<T, T, bool> equals)
    {
        var aLength = a?.Length ?? 0;
        if (aLength != (b?.Length ?? 0))
        {
            return false;
        }

        for (var i = 0; i < aLength; i++)
        {
            if (!equals(a![i], b![i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>slices.Compare</c>（按序数比较字符串，nil 切片从空开始）。</summary>
    private static int CompareStrings(string[]? a, string[]? b)
    {
        var aLength = a?.Length ?? 0;
        var bLength = b?.Length ?? 0;
        var n = Math.Min(aLength, bLength);
        for (var i = 0; i < n; i++)
        {
            var c = string.CompareOrdinal(a![i], b![i]);
            if (c != 0)
            {
                return c;
            }
        }

        return aLength - bLength;
    }
}

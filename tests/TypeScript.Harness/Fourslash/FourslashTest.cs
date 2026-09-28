// ported from: internal/fourslash/fourslash.go
//   * 1-160      FourslashTest 字段 / scriptInfo / testConverters / textEditSpan / newScriptInfo / editScript 辅助 / rootDir / parseCache
//   * 160-395    NewFourslash / FourslashOptions / NewFourslashWithOptions / newFourslash / handleServerRequest / getBaseFileNameFromTest / initialize
//   * 397-441    defaultSemanticTokenTypes / defaultSemanticTokenModifiers
//   * 739-1110   sendRequest … getLanguageKind
//   * 3873-3885  roundtripThroughJson
//   * 3985-4180  applyTextEdits / editScriptAndUpdateMarkers(Worker) / updatePosition / fromLSPRange / editScript / getScriptInfo / getOrLoadScriptInfo
//                （Go 里这些是 fourslash.go 的包级/方法辅助函数；FsCore 契约要求由本文件提供，见偏离 11）
//   * 5235-5261  GetRangesByText / getRangeText / verifyBaselines
//
// 偏离清单（相对 Go 原文逐条）：
// 1) `bundled.Embedded` 在 C# 里恒为 true（Bundled.Embedded 是 const true）⇒ 省略 Go 的
//    `if !bundled.Embedded { t.Skip("bundled files are not embedded") }` 分支。
// 2) Go 的 `testData.GlobalOptions["tsc"]` 段（调 `tsctests.GetFileMapWithBuild`）**未移植**：
//    `internal/execute/tsctests` 在 C# 侧没有对应物 ⇒ 整块省略，即 `@tsc:` 全局选项不被支持。
// 3) `fsFromMap.(iovfs.FsWithSys)`（类型断言，失败 panic）→ C# 的强制转换 `(FsWithSys)fsFromMap`
//    （失败抛 InvalidCastException）。
// 4) go-cmp 退化为 JSON 编解码比较：`FsCmp.Diff`/`FsCmp.AssertDeepEqual` 用 `Json.Marshal` 把两边编成
//    UTF-8 字节逐字节比较（`null` → 字面 `null`）。编码器会省略零值 / omitzero 字段，因此"两边都是默认值"
//    的差异可能漏检（Go 的按字段深比较能发现）。`assert.DeepEqual` 的调用点全部改用本类型。
// 5) `runtime.Caller(1)` 取测试文件路径 → `[CallerFilePath] string testPath = ""`。
// 6) Go 未导出函数 `newFourslash` 与导出函数 `NewFourslash` 的 PascalCase 名相同 ⇒ 前者加 `Core` 后缀落成
//    `Fourslash.NewFourslashCore`。
// 7) Go 的 `goToMarker`/`goToPosition` 与同名的 `GoToMarker`/`GoToPosition` 参数类型不同 ⇒ C# 用重载
//    （`GoToMarker(MarkerOrRange)` / `GoToPosition(Position)`）。
// 8) `t.Fatalf(prefix+"...", args...)` → `t.Fatal($"{prefix}...")`，消息文本形状保持一致；`t.Helper()` 删除。
// 9) `sendRequestAndBaselineWorker` 错误路径里的 `%T`（Go 类型名，如 `*lsproto.CompletionList`）→
//    `GoTypeName(value)`（`value.GetType().Name`，null → `<nil>`）。只出现在失败消息里，不进 baseline。
// 10) gotest 的 `assert.Equal` → 手写字符串比较 + `t.Fatal`（消息文本与 gotest 不同；不进 baseline）。
// 11) `getScriptInfo`/`getOrLoadScriptInfo`/`editScript`/`applyTextEdits`/`editScriptAndUpdateMarkers(Worker)`/
//     `updatePosition`/`fromLSPRange` 在 Go 里是 fourslash.go:3985-4178 的辅助函数（不在本切片原始行范围内），
//     但共享契约把它们列为 FsCore 成员且本文件的方法（FormatDocument 等）直接依赖 ⇒ 一并移植到这里。
// 12) `editScript` 里 `f.vfs.WriteFile(...) != nil` 的错误分支省略：C# 的 `FS.WriteFile` 失败直接抛异常
//     （对应 Go 的 err）。
// 13) `scriptInfo`（未导出类型）→ `public sealed class ScriptInfo`（PascalCase 类名；因为它在 public 方法
//     签名里出现，故为 public）。未导出字段落成 `internal` + `_camelCase`：`_fileName`/`_content`/`_lineMap`/
//     `_version`。`SpanMap()` 恒返回 null（Go 也返回 nil）。
// 14) `strings.TrimRight(content[start:end], "\r\n")` → `TrimEnd('\r', '\n')`。
// 15) `slices.Backward(edits)` → 倒序 for 循环；`slices.SortFunc` → `Array.Sort`（两者都是不稳定排序）。
// 16) `VerifyIndentation` 在 Go 里整段被注释掉（no-op）⇒ 照抄为空实现，保留参数。
// 17) 包级 `rootDir` → `Fourslash.RootDir`；包级 `parseCache` → `Fourslash.ParseCache`（private static readonly，
//     `DisableDeletion: true`）。
// 18) 包级 `defaultSemanticTokenTypes`/`defaultSemanticTokenModifiers` → `FourslashTest.DefaultSemanticTokenTypes`/
//     `DefaultSemanticTokenModifiers`（`internal static`，FsCaps 的 GetCapabilitiesWithDefaults 也调用它们）。
// 19) `updateState` 的 `params.(*lsproto.X)` 类型断言 → C# 强制转换 `(X)parameters!`（失败抛 InvalidCastException）。
// 20) `configureWithReset` 返回的 `func()` → `Action`。
// 21) `MarkerOrRange`/`Marker`/`RangeMarker`/`TestData`/`TestFileInfo`（test_parser.go）由 TestParser.cs 提供；
//     本文件只使用，不重复定义。`ParseTestData` 是 `Fourslash` 静态类的成员（TestParser.cs 声明为 partial）。
// 22) `verifyBaselines` 依赖 BaselineUtil.cs 的 `GetBaselineFileName`/`GetBaselineOptions` 与 StateBaseline.cs 的
//     `_stateBaseline._baseline`；`sendRequestAndBaselineWorker` 依赖 StateBaseline.cs 的 `BaselineState`/
//     `BaselineRequestOrNotification`，`openFile` 依赖 `BaselineProjectsAfterNotification`，
//     `initialize` 依赖 Capabilities.cs 的 `GetCapabilitiesWithDefaults`，`sendRequestAndBaselineWorker` 依赖
//     QuickInfo.cs 的 `GetCurrentPositionPrefix`。均为 partial 内的同名成员。
// 23) Go 的 `goToEachMarker` 里 `var markers []*Marker` 后紧跟 `if len(markers) == 0 { markers = f.Markers() }`，
//     该条件对 nil 切片恒为真 ⇒ `markerNames` 分支永不执行。C# 保留参数（签名契约）但直接取 `Markers()`，
//     并在方法里注明该行为与 Go 一致。
// 24) `array(edits)` 的元素类型：Go `[]*lsproto.TextEdit` → C# `TextEdit?[]`（生成的 `TextEditsOrNull.TextEdits`
//     就是 `TextEdit?[]?`）。
// 25) `TestConverters` 用**组合**表达 Go 的内嵌 `*lsconv.Converters`（字段 `Converters`）；Go 里被提升的
//     `ToLSPRange`/`FromLSPRange` 等调用点写 `_converters.Converters.X(...)`。
using System.Runtime.CompilerServices;
using System.Text;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.TestUtil;
using TypeScript.TestUtil.Baseline;
using TypeScript.TestUtil.HarnessUtil;
using TypeScript.TestUtil.LspTestUtil;
using TypeScript.Vfs;
using TypeScript.Vfs.Iovfs;
using TypeScript.Vfs.VfsTest;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using Range = TypeScript.LsProto.Range;
using SpanMapType = TypeScript.SpanMap.SpanMap;

namespace TypeScript.Harness.Fourslash;

public sealed partial class FourslashTest
{
    internal LSPClient _client = null!;
    internal FS _vfs = null!;

    internal TestData? _testData; // !!! consolidate test files from test data and script info
    internal Dictionary<BaselineCommand, StringBuilder> _baselines = [];
    internal Collections.MultiMap<string, RangeMarker>? _rangesByText;
    internal HashSet<string> _openFiles = [];
    internal StateBaseline? _stateBaseline;

    internal Dictionary<string, ScriptInfo> _scriptInfos = [];
    internal TestConverters _converters = null!;

    internal bool _stateEnableFormatting;
    internal bool _reportFormatOnTypeCrash;
    internal TypeScript.LsUtil.UserPreferences _userPreferences;
    internal Position _currentCaretPosition = new();
    internal string? _lastKnownMarkerName;
    internal string _activeFilename = "";
    internal Position? _selectionEnd;

    internal ClientCapabilities? _capabilities;
    internal bool _isStradaServer; // Whether this is a fourslash server test in Strada. !!! Remove once we don't need to diff baselines.

    // Semantic token configuration
    internal string[] _semanticTokenTypes = [];
    internal string[] _semanticTokenModifiers = [];

    internal const string ShowCodeLensLocationsCommandName = "typescript.showCodeLensLocations";

    // ---- fourslash.go:343-365 getBaseFileNameFromTest ------------------------------------------------
    internal static string GetBaseFileNameFromTest(IHarnessTest t)
    {
        var name = t.Name;
        name = LastOrNil(name.Split('/'));
        name = name.StartsWith("Test", StringComparison.Ordinal) ? name["Test".Length..] : name;
        name = TypeScript.StringUtil.StringUtil.LowerFirstChar(name);

        // Special case: TypeScript has "callHierarchyFunctionAmbiguity.N" with periods
        switch (name)
        {
            case "callHierarchyFunctionAmbiguity1":
                name = "callHierarchyFunctionAmbiguity.1";
                break;
            case "callHierarchyFunctionAmbiguity2":
                name = "callHierarchyFunctionAmbiguity.2";
                break;
            case "callHierarchyFunctionAmbiguity3":
                name = "callHierarchyFunctionAmbiguity.3";
                break;
            case "callHierarchyFunctionAmbiguity4":
                name = "callHierarchyFunctionAmbiguity.4";
                break;
            case "callHierarchyFunctionAmbiguity5":
                name = "callHierarchyFunctionAmbiguity.5";
                break;
        }

        return name;
    }

    // ---- fourslash.go:5681-5687 isLibFile -----------------------------------------------------------
    internal static bool IsLibFile(string fileName)
    {
        var baseName = GetBaseFileName(fileName);
        return baseName.StartsWith("lib.", StringComparison.Ordinal) && baseName.EndsWith(".d.ts", StringComparison.Ordinal);
    }

    // ---- fourslash.go:1086-1109 getLanguageKind -----------------------------------------------------
    internal static LanguageKind GetLanguageKind(string filename)
    {
        if (FileExtensionIsOneOf(
            filename,
            [ExtensionTs, ExtensionMts, ExtensionCts, ExtensionDmts, ExtensionDcts, ExtensionDts]))
        {
            return LanguageKind.TypeScript;
        }

        if (FileExtensionIsOneOf(filename, [ExtensionJs, ExtensionMjs, ExtensionCjs]))
        {
            return LanguageKind.JavaScript;
        }

        if (FileExtensionIs(filename, ExtensionJsx))
        {
            return LanguageKind.JavaScriptReact;
        }

        if (FileExtensionIs(filename, ExtensionTsx))
        {
            return LanguageKind.TypeScriptReact;
        }

        if (FileExtensionIs(filename, ExtensionJson))
        {
            return LanguageKind.JSON;
        }

        return LanguageKind.TypeScript; // !!! should we error in this case?
    }

    // ---- fourslash.go:397-441 defaultSemanticTokenTypes / defaultSemanticTokenModifiers --------------
    internal static string[] DefaultSemanticTokenTypes() =>
    [
        SemanticTokenType.Namespace.Value,
        SemanticTokenType.Class.Value,
        SemanticTokenType.Enum.Value,
        SemanticTokenType.Interface.Value,
        SemanticTokenType.Struct.Value,
        SemanticTokenType.TypeParameter.Value,
        SemanticTokenType.Type.Value,
        SemanticTokenType.Parameter.Value,
        SemanticTokenType.Variable.Value,
        SemanticTokenType.Property.Value,
        SemanticTokenType.EnumMember.Value,
        SemanticTokenType.Decorator.Value,
        SemanticTokenType.Event.Value,
        SemanticTokenType.Function.Value,
        SemanticTokenType.Method.Value,
        SemanticTokenType.Macro.Value,
        SemanticTokenType.Label.Value,
        SemanticTokenType.Comment.Value,
        SemanticTokenType.String.Value,
        SemanticTokenType.Keyword.Value,
        SemanticTokenType.Number.Value,
        SemanticTokenType.Regexp.Value,
        SemanticTokenType.Operator.Value,
    ];

    internal static string[] DefaultSemanticTokenModifiers() =>
    [
        SemanticTokenModifier.Declaration.Value,
        SemanticTokenModifier.Definition.Value,
        SemanticTokenModifier.Readonly.Value,
        SemanticTokenModifier.Static.Value,
        SemanticTokenModifier.Deprecated.Value,
        SemanticTokenModifier.Abstract.Value,
        SemanticTokenModifier.Async.Value,
        SemanticTokenModifier.Modification.Value,
        SemanticTokenModifier.Documentation.Value,
        SemanticTokenModifier.DefaultLibrary.Value,
        "local",
    ];

    // ---- fourslash.go:107-150 newScriptInfo / scriptInfo 构造 ---------------------------------------
    internal static ScriptInfo NewScriptInfo(string fileName, string content) => new(fileName, content);

    // ---- fourslash.go:86-88 newTestConverters -------------------------------------------------------
    internal static TestConverters NewTestConverters(Converters converters) => new(converters);

    // ---- fourslash.go:739-742 sendRequest -----------------------------------------------------------
    public Resp SendRequest<Params, Resp>(IHarnessTest t, RequestInfo<Params, Resp> info, Params parameters) =>
        SendRequestAndBaselineWorker(t, info, parameters, true);

    // ---- fourslash.go:744-774 sendRequestAndBaselineWorker ------------------------------------------
    public Resp SendRequestAndBaselineWorker<Params, Resp>(IHarnessTest t, RequestInfo<Params, Resp> info, Params parameters, bool baselineProjects)
    {
        var prefix = GetCurrentPositionPrefix();
        if (baselineProjects)
        {
            BaselineState(t);
        }

        BaselineRequestOrNotification(t, info.Method, parameters);
        var (responseMessage, result, resultOk) = _client.SendRequest(t, info, parameters);
        if (baselineProjects)
        {
            BaselineState(t);
        }

        // Go 的 `switch info.Method { case …OnTypeFormatting: if !f.reportFormatOnTypeCrash { break }; fallthrough; default: … }`
        if (info.Method == Method.TextDocumentOnTypeFormatting && !_reportFormatOnTypeCrash)
        {
            return result;
        }

        if (responseMessage is null)
        {
            t.Fatal($"{prefix}Nil response received for {info.Method} request");
        }

        var response = responseMessage.AsResponse();
        if (response.Error is not null)
        {
            t.Fatal($"{prefix}{info.Method} request returned error: {response.Error.String()}");
        }

        if (!resultOk)
        {
            t.Fatal($"{prefix}Unexpected {info.Method} response type: {GoTypeName(response.Result)}, error: {response.Error}");
        }

        return result;
    }

    private static string GoTypeName(object? value) => value is null ? "<nil>" : value.GetType().Name;

    // ---- fourslash.go:776-788 sendNotification ------------------------------------------------------
    public void SendNotification<Params>(IHarnessTest t, NotificationInfo<Params> info, Params parameters)
    {
        if (info.Method != Method.TextDocumentDidChange)
        {
            // This is called eg when doing typeText = which is series of edits and formatting - which becomes non deterministic "after state"
            // The notification can only guarantee before state and thats what it baselines, but in case of type it creates
            // multiple edits which results in getting different state -based on if the snapshot was updated or not at the time of formatting requests
            // So this is used for all the incremental edits - to baseline only request data but not project state between those edits
            BaselineState(t);
            UpdateState(info.Method, parameters);
        }

        BaselineRequestOrNotification(t, info.Method, parameters);
        _client.SendNotification(t, info, parameters);
    }

    // ---- fourslash.go:790-797 updateState -----------------------------------------------------------
    internal void UpdateState(Method method, object? parameters)
    {
        if (method == Method.TextDocumentDidOpen)
        {
            _openFiles.Add(((DidOpenTextDocumentParams)parameters!).TextDocument!.Uri.FileName());
        }
        else if (method == Method.TextDocumentDidClose)
        {
            _openFiles.Remove(((DidCloseTextDocumentParams)parameters!).TextDocument.Uri.FileName());
        }
    }

    // ---- fourslash.go:799-801 GetOptions ------------------------------------------------------------
    public TypeScript.LsUtil.UserPreferences GetOptions() => _userPreferences;

    // ---- fourslash.go:803-813 Configure -------------------------------------------------------------
    public void Configure(IHarnessTest t, TypeScript.LsUtil.UserPreferences config)
    {
        // We send 'js/ts' by default because that is what we expect the primary config to be in vscode and VS (one
        // set of preferences for both languages). This should be fine in fourslash since tests that need
        // multiple options usually send reconfiguration commands for each `verify` anyways
        _userPreferences = config;
        SendNotification(t, LsProto.LsProto.WorkspaceDidChangeConfigurationInfo, new DidChangeConfigurationParams
        {
            Settings = new Dictionary<string, object?>
            {
                ["js/ts"] = config,
            },
        });
    }

    // ---- fourslash.go:815-821 ConfigureWithReset ----------------------------------------------------
    public Action ConfigureWithReset(IHarnessTest t, TypeScript.LsUtil.UserPreferences config)
    {
        var originalConfig = _userPreferences;
        Configure(t, config);
        return () => Configure(t, originalConfig);
    }

    // ---- fourslash.go:823-825 GoToMarkerOrRange -----------------------------------------------------
    public void GoToMarkerOrRange(IHarnessTest t, MarkerOrRange markerOrRange) => GoToMarker(t, markerOrRange);

    // ---- fourslash.go:827-833 GoToMarker ------------------------------------------------------------
    public void GoToMarker(IHarnessTest t, string markerName)
    {
        if (!_testData!.MarkerPositions.TryGetValue(markerName, out var marker) || marker is null)
        {
            t.Fatal($"Marker '{markerName}' not found");
        }

        GoToMarker(t, marker);
    }

    // ---- fourslash.go:835-839 goToMarker ------------------------------------------------------------
    public void GoToMarker(IHarnessTest t, MarkerOrRange markerOrRange)
    {
        EnsureActiveFile(t, markerOrRange.FileName());
        GoToPosition(t, markerOrRange.LSPos());
        _lastKnownMarkerName = markerOrRange.GetName();
    }

    // ---- fourslash.go:841-846 GoToEOF ---------------------------------------------------------------
    public void GoToEOF(IHarnessTest t)
    {
        var script = GetScriptInfo(_activeFilename)!;
        var pos = script.Text().Length;
        var lspPos = _converters.PositionToLineAndCharacter(script, new TextPos(pos));
        GoToPosition(t, lspPos);
    }

    // ---- fourslash.go:848-850 GoToBOF ---------------------------------------------------------------
    public void GoToBOF(IHarnessTest t) => GoToPosition(t, new Position { Line = 0, Character = 0 });

    // ---- fourslash.go:852-856 GoToPosition ----------------------------------------------------------
    public void GoToPosition(IHarnessTest t, int position)
    {
        var script = GetScriptInfo(_activeFilename)!;
        var lspPos = _converters.PositionToLineAndCharacter(script, new TextPos(position));
        GoToPosition(t, lspPos);
    }

    // ---- fourslash.go:858-861 goToPosition ----------------------------------------------------------
    public void GoToPosition(IHarnessTest t, Position position)
    {
        _currentCaretPosition = position;
        _selectionEnd = null;
    }

    // ---- fourslash.go:863-881 GoToEachMarker --------------------------------------------------------
    public void GoToEachMarker(IHarnessTest t, string[] markerNames, Action<Marker, int> action)
    {
        // Go: `var markers []*Marker; if len(markers) == 0 { markers = f.Markers() } else { …markerNames… }`
        // —— 对 nil 切片 `len(markers) == 0` 恒为真，故 markerNames 分支永不执行（照抄该可观察行为）。
        var markers = Markers();
        for (var i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            GoToMarker(t, marker);
            action(marker, i);
        }
    }

    // ---- fourslash.go:883-889 GoToEachRange ---------------------------------------------------------
    public void GoToEachRange(IHarnessTest t, Action<IHarnessTest, RangeMarker> action)
    {
        var ranges = Ranges();
        foreach (var rangeMarker in ranges)
        {
            GoToPosition(t, rangeMarker.LSRange.Start);
            action(t, rangeMarker);
        }
    }

    // ---- fourslash.go:891-894 GoToRangeStart --------------------------------------------------------
    public void GoToRangeStart(IHarnessTest t, RangeMarker rangeMarker)
    {
        OpenFile(t, rangeMarker.FileName());
        GoToPosition(t, rangeMarker.LSRange.Start);
    }

    // ---- fourslash.go:896-911 GoToSelect ------------------------------------------------------------
    public void GoToSelect(IHarnessTest t, string startMarkerName, string endMarkerName)
    {
        _testData!.MarkerPositions.TryGetValue(startMarkerName, out var startMarker);
        if (startMarker is null)
        {
            t.Fatal($"Start marker '{startMarkerName}' not found");
        }

        _testData.MarkerPositions.TryGetValue(endMarkerName, out var endMarker);
        if (endMarker is null)
        {
            t.Fatal($"End marker '{endMarkerName}' not found");
        }

        if (startMarker.FileName() != endMarker.FileName())
        {
            t.Fatal($"Markers '{startMarkerName}' and '{endMarkerName}' are in different files");
        }

        EnsureActiveFile(t, startMarker.FileName());
        GoToPosition(t, startMarker.LSPosition);
        _selectionEnd = endMarker.LSPosition;
    }

    // ---- fourslash.go:913-916 GoToSelectRange -------------------------------------------------------
    public void GoToSelectRange(IHarnessTest t, RangeMarker rangeMarker)
    {
        GoToRangeStart(t, rangeMarker);
        _selectionEnd = rangeMarker.LSRange.End;
    }

    // ---- fourslash.go:918-921 GoToFile --------------------------------------------------------------
    public void GoToFile(IHarnessTest t, string filename)
    {
        filename = GetNormalizedAbsolutePath(filename, Fourslash.RootDir);
        OpenFile(t, filename);
    }

    // ---- fourslash.go:923-929 GoToFileNumber --------------------------------------------------------
    public void GoToFileNumber(IHarnessTest t, int index)
    {
        var files = _testData!.Files ?? [];
        if (index < 0 || index >= files.Length)
        {
            t.Fatal($"File index {index} out of range (0-{files.Length - 1})");
        }

        var filename = files[index]._fileName;
        OpenFile(t, filename);
    }

    // ---- fourslash.go:931-933 Markers ---------------------------------------------------------------
    public Marker[] Markers() => _testData!.Markers ?? [];

    // ---- fourslash.go:935-942 MarkerNames -----------------------------------------------------------
    public string[] MarkerNames() =>
        MapFiltered<Marker, string>(_testData!.Markers ?? [], static marker =>
            marker.Name is null ? ("", false) : (marker.Name, true));

    // ---- fourslash.go:944-946 MarkerByName ----------------------------------------------------------
    public Marker? MarkerByName(IHarnessTest t, string name) =>
        _testData!.MarkerPositions.TryGetValue(name, out var marker) ? marker : null;

    // ---- fourslash.go:948-950 Ranges ----------------------------------------------------------------
    public RangeMarker[] Ranges() => _testData!.Ranges ?? [];

    // ---- fourslash.go:952-960 getRangesInFile -------------------------------------------------------
    public RangeMarker[] GetRangesInFile(string fileName)
    {
        List<RangeMarker> rangesInFile = [];
        foreach (var rangeMarker in _testData!.Ranges ?? [])
        {
            if (rangeMarker.FileName() == fileName)
            {
                rangesInFile.Add(rangeMarker);
            }
        }

        return rangesInFile.ToArray();
    }

    // ---- fourslash.go:962-970 ensureActiveFile ------------------------------------------------------
    public void EnsureActiveFile(IHarnessTest t, string filename)
    {
        if (_activeFilename != filename)
        {
            if (!_openFiles.Contains(filename))
            {
                OpenFile(t, filename);
            }
            else
            {
                _activeFilename = filename;
            }
        }
    }

    // ---- fourslash.go:972-991 CloseFileOfMarker -----------------------------------------------------
    public void CloseFileOfMarker(IHarnessTest t, string markerName)
    {
        if (!_testData!.MarkerPositions.TryGetValue(markerName, out var marker) || marker is null)
        {
            t.Fatal($"Marker '{markerName}' not found");
        }

        if (_activeFilename == marker.FileName())
        {
            _activeFilename = "";
        }

        var files = _testData.Files ?? [];
        var index = Array.FindIndex(files, f => f._fileName == marker.FileName());
        if (index >= 0)
        {
            var testFile = files[index];
            _scriptInfos[testFile._fileName] = NewScriptInfo(testFile._fileName, testFile.Content);
        }
        else
        {
            _scriptInfos.Remove(marker.FileName());
        }

        SendNotification(t, LsProto.LsProto.TextDocumentDidCloseInfo, new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(marker.FileName()),
            },
        });
    }

    // ---- fourslash.go:993-1012 openFile -------------------------------------------------------------
    public void OpenFile(IHarnessTest t, string filename)
    {
        var script = GetScriptInfo(filename);
        if (script is null)
        {
            if (_vfs.TryReadFile(filename, out var content))
            {
                script = NewScriptInfo(filename, content);
                _scriptInfos[filename] = script;
            }
            else
            {
                t.Fatal($"File {filename} not found in test data");
            }
        }

        _activeFilename = filename;
        SendNotification(t, LsProto.LsProto.TextDocumentDidOpenInfo, new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(filename),
                LanguageId = GetLanguageKind(filename),
                Text = script.Text(),
            },
        });
        BaselineProjectsAfterNotification(t, filename);
    }

    // ---- fourslash.go:1014-1028 FormatDocument ------------------------------------------------------
    public void FormatDocument(IHarnessTest t, string filename)
    {
        if (filename == "")
        {
            filename = _activeFilename;
        }

        var result = SendRequest(t, LsProto.LsProto.TextDocumentFormattingInfo, new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(filename),
            },
            Options = _userPreferences.FormatCodeSettings.ToLSFormatOptions(),
        });
        if (result.TextEdits is null)
        {
            return;
        }

        ApplyTextEdits(t, result.TextEdits);
    }

    // ---- fourslash.go:1030-1058 FormatSelection -----------------------------------------------------
    public void FormatSelection(IHarnessTest t, string startMarkerName, string endMarkerName)
    {
        if (!_testData!.MarkerPositions.TryGetValue(startMarkerName, out var startMarker) || startMarker is null)
        {
            t.Fatal($"Marker '{startMarkerName}' not found");
        }

        if (!_testData.MarkerPositions.TryGetValue(endMarkerName, out var endMarker) || endMarker is null)
        {
            t.Fatal($"Marker '{endMarkerName}' not found");
        }

        if (startMarker.FileName() != endMarker.FileName())
        {
            t.Fatal($"Markers '{startMarkerName}' and '{endMarkerName}' are in different files");
        }

        var filename = startMarker.FileName();
        var result = SendRequest(t, LsProto.LsProto.TextDocumentRangeFormattingInfo, new DocumentRangeFormattingParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(filename),
            },
            Range = new Range
            {
                Start = startMarker.LSPosition,
                End = endMarker.LSPosition,
            },
            Options = _userPreferences.FormatCodeSettings.ToLSFormatOptions(),
        });
        if (result.TextEdits is null)
        {
            return;
        }

        ApplyTextEdits(t, result.TextEdits);
    }

    // ---- fourslash.go:1060-1064 VerifyCurrentFileContent --------------------------------------------
    public void VerifyCurrentFileContent(IHarnessTest t, string expectedContent)
    {
        var actualContent = GetScriptInfo(_activeFilename)!.Text();
        // Go: assert.Equal(t, actualContent, expectedContent)
        if (actualContent != expectedContent)
        {
            t.Fatal($"expected content to equal\n  actual:   {actualContent}\nexpected: {expectedContent}");
        }
    }

    // ---- fourslash.go:1066-1077 VerifyCurrentLineContent --------------------------------------------
    public void VerifyCurrentLineContent(IHarnessTest t, string expectedContent)
    {
        var actualContent = GetScriptInfo(_activeFilename)!.GetLineContent((int)_currentCaretPosition.Line);
        // Go: assert.Equal(t, actualContent, expectedContent, fmt.Sprintf(…))
        if (actualContent != expectedContent)
        {
            t.Fatal($"""

  actual line: "{actualContent}"
expected line: "{expectedContent}"
""");
        }
    }

    // ---- fourslash.go:1079-1084 VerifyIndentation ---------------------------------------------------
    public void VerifyIndentation(IHarnessTest t, int numSpaces)
    {
        // not implemented
        // actualContent := f.getScriptInfo(f.activeFilename).GetLineContent(int(f.currentCaretPosition.Line))
        // assert.Equal(t, actualContent, expectedContent, fmt.Sprintf("Actual line content %s does not match expected content.", actualContent))
    }

    // ---- fourslash.go:3873-3885 roundtripThroughJson ------------------------------------------------
    public T? RoundtripThroughJson<T>(object? value)
    {
        byte[] bytes;
        try
        {
            bytes = global::TypeScript.Json.Json.Marshal(value);
        }
        catch (JsonException e)
        {
            throw new JsonException($"failed to marshal value to JSON: {e.Message}", e);
        }

        try
        {
            return global::TypeScript.Json.Json.Unmarshal<T>(bytes);
        }
        catch (JsonException e)
        {
            throw new JsonException($"failed to unmarshal value from JSON: {e.Message}", e);
        }
    }

    // ---- fourslash.go:3995-4026 applyTextEdits ------------------------------------------------------
    public int ApplyTextEdits(IHarnessTest t, TextEdit?[] edits)
    {
        var script = GetScriptInfo(_activeFilename)!;
        Array.Sort(edits, (a, b) =>
            (int)_converters.LineAndCharacterToPosition(script, a!.Range.Start) -
            (int)_converters.LineAndCharacterToPosition(script, b!.Range.Start));

        var totalOffset = 0;
        var currentCaretPosition = (int)_converters.LineAndCharacterToPosition(script, _currentCaretPosition);
        // Apply edits in reverse order to avoid affecting the positions of earlier edits.
        for (var i = edits.Length - 1; i >= 0; i--)
        {
            var edit = edits[i]!;
            var start = (int)_converters.LineAndCharacterToPosition(script, edit.Range.Start);
            var end = (int)_converters.LineAndCharacterToPosition(script, edit.Range.End);
            EditScriptAndUpdateMarkers(t, _activeFilename, start, end, edit.NewText);

            var delta = edit.NewText.Length - (end - start);
            if (start <= currentCaretPosition)
            {
                if (end <= currentCaretPosition)
                {
                    // The entirety of the edit span falls before the caret position, shift the caret accordingly
                    currentCaretPosition += delta;
                }
                else
                {
                    // The span being replaced includes the caret position, place the caret at the beginning of the span
                    currentCaretPosition = start;
                }
            }

            totalOffset += delta;
        }

        _currentCaretPosition = _converters.PositionToLineAndCharacter(script, new TextPos(currentCaretPosition));
        return totalOffset;
    }

    // ---- fourslash.go:4083-4100 editScriptAndUpdateMarkers ------------------------------------------
    public void EditScriptAndUpdateMarkers(IHarnessTest t, string fileName, int editStart, int editEnd, string newText)
    {
        EditScriptAndUpdateMarkersWorker(t, fileName, [new TextChange(NewTextRange(editStart, editEnd), newText)]);
    }

    // ---- fourslash.go:4087-4116 editScriptAndUpdateMarkersWorker ------------------------------------
    public void EditScriptAndUpdateMarkersWorker(IHarnessTest t, string fileName, TextChange[] changes)
    {
        // Sort changes by position (ascending) so we can apply in reverse
        var sortedChanges = (TextChange[])changes.Clone();
        Array.Sort(sortedChanges, static (a, b) => a.Range.Pos() - b.Range.Pos());

        // Apply changes in reverse order to preserve positions of earlier changes
        for (var i = sortedChanges.Length - 1; i >= 0; i--)
        {
            var change = sortedChanges[i];
            var editStart = change.Range.Pos();
            var editEnd = change.Range.End();
            var script = EditScript(t, fileName, change)!;
            foreach (var marker in _testData!.Markers ?? [])
            {
                if (marker.FileName() == fileName)
                {
                    marker.Position = UpdatePosition(marker.Position, editStart, editEnd, change.NewText);
                    marker.LSPosition = _converters.PositionToLineAndCharacter(script, new TextPos(marker.Position));
                }
            }

            foreach (var rangeMarker in _testData.Ranges ?? [])
            {
                if (rangeMarker.FileName() == fileName)
                {
                    var start = UpdatePosition(rangeMarker.Range.Pos(), editStart, editEnd, change.NewText);
                    var end = UpdatePosition(rangeMarker.Range.End(), editStart, editEnd, change.NewText);
                    rangeMarker.Range = NewTextRange(start, end);
                    rangeMarker.LSRange = _converters.Converters.ToLSPRange(script, rangeMarker.Range).Range;
                }
            }
        }

        _rangesByText = null;
    }

    // ---- fourslash.go:4118-4127 updatePosition ------------------------------------------------------
    internal static int UpdatePosition(int pos, int editStart, int editEnd, string newText)
    {
        if (pos <= editStart)
        {
            return pos;
        }

        // If inside the edit, return -1 to mark as invalid
        if (pos < editEnd)
        {
            return -1;
        }

        return pos + newText.Length - (editEnd - editStart);
    }

    // ---- fourslash.go:4129-4135 fromLSPRange --------------------------------------------------------
    public TextRange FromLSPRange(ScriptInfo script, Range r)
    {
        var ranges = _converters.Converters.FromLSPRange(script, r, TypeScript.SpanMap.Feature.All);
        if (ranges.Length != 1)
        {
            return default; // Go: core.TextRange{}
        }

        return ranges[0].Span;
    }

    // ---- fourslash.go:4137-4160 editScript ----------------------------------------------------------
    public ScriptInfo? EditScript(IHarnessTest t, string fileName, TextChange change)
    {
        var script = GetOrLoadScriptInfo(fileName);
        if (script is null)
        {
            Debug.Fail($"Script info for file {fileName} not found");
            throw new System.Diagnostics.UnreachableException();
        }

        var (changeRange, _) = _converters.Converters.ToLSPRange(script, NewTextRange(change.Range.Pos(), change.Range.End()));
        script.EditContent(change);
        _vfs.WriteFile(fileName, script.Text());
        SendNotification(t, LsProto.LsProto.TextDocumentDidChangeInfo, new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(fileName),
                Version = script._version,
            },
            ContentChanges =
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    Partial = new TextDocumentContentChangePartial
                    {
                        Range = changeRange,
                        Text = change.NewText,
                    },
                },
            ],
        });
        return script;
    }

    // ---- fourslash.go:4162-4164 getScriptInfo -------------------------------------------------------
    public ScriptInfo? GetScriptInfo(string fileName) =>
        _scriptInfos.TryGetValue(fileName, out var script) ? script : null;

    // ---- fourslash.go:4166-4177 getOrLoadScriptInfo -------------------------------------------------
    public ScriptInfo? GetOrLoadScriptInfo(string fileName)
    {
        var script = GetScriptInfo(fileName);
        if (script is not null)
        {
            return script;
        }

        if (_vfs.TryReadFile(fileName, out var content))
        {
            var newScript = NewScriptInfo(fileName, content);
            _scriptInfos[fileName] = newScript;
            return newScript;
        }

        return null;
    }

    // ---- fourslash.go:5235-5246 GetRangesByText -----------------------------------------------------
    public Collections.MultiMap<string, RangeMarker> GetRangesByText()
    {
        if (_rangesByText is not null)
        {
            return _rangesByText;
        }

        var rangesByText = new Collections.MultiMap<string, RangeMarker>();
        foreach (var r in _testData!.Ranges ?? [])
        {
            var rangeText = GetRangeText(r);
            rangesByText.Add(rangeText, r);
        }

        _rangesByText = rangesByText;
        return rangesByText;
    }

    // ---- fourslash.go:5248-5251 getRangeText --------------------------------------------------------
    public string GetRangeText(RangeMarker r)
    {
        var script = GetScriptInfo(r.FileName())!;
        return script._content[r.Range.Pos()..r.Range.End()];
    }

    // ---- fourslash.go:5253-5261 verifyBaselines -----------------------------------------------------
    public void VerifyBaselines(IHarnessTest t, string testPath)
    {
        if (!_testData!.IsStateBaseliningEnabled())
        {
            foreach (var (command, content) in _baselines)
            {
                Baseline.Run(t, GetBaselineFileName(t, command), content.ToString(), GetBaselineOptions(command, testPath));
            }
        }
        else
        {
            Baseline.Run(
                t,
                GetBaseFileNameFromTest(t) + ".baseline",
                _stateBaseline!._baseline.ToString(),
                new BaselineOptions { Subfolder = "fourslash/state" });
        }
    }

    // ---- fourslash.go:288-341 handleServerRequest ---------------------------------------------------
    // handleServerRequest handles requests initiated by the server (e.g., workspace/configuration).
    public ResponseMessage? HandleServerRequest(CancellationToken ctx, RequestMessage req)
    {
        if (req.Method == Method.WorkspaceConfiguration)
        {
            // Return current user preferences for each requested section.
            // The server requests multiple sections (js/ts, typescript, javascript, editor);
            // we return user preferences for "js/ts" and nil for others.
            // Go: `params, err := req.UnmarshalParams[*lsproto.ConfigurationParams]()`（失败 → 走下面的 fallback 分支）
            ConfigurationParams? parameters;
            try
            {
                parameters = req.UnmarshalParams<ConfigurationParams>();
            }
            catch (Exception e) when (e is JsonException or ErrorCodeException)
            {
                parameters = null;
            }

            if (parameters is null || parameters.Items is null)
            {
                return new ResponseMessage
                {
                    ID = req.ID,
                    JSONRPC = req.JSONRPC,
                    Result = new object?[] { _userPreferences },
                };
            }

            var results = new object?[parameters.Items.Length];
            for (var i = 0; i < parameters.Items.Length; i++)
            {
                var item = parameters.Items[i];
                if (item is not null && item.Section is not null && item.Section == "js/ts")
                {
                    results[i] = _userPreferences;
                }
            }

            return new ResponseMessage
            {
                ID = req.ID,
                JSONRPC = req.JSONRPC,
                Result = results,
            };
        }

        if (req.Method == Method.ClientRegisterCapability)
        {
            // Accept all capability registrations
            return new ResponseMessage
            {
                ID = req.ID,
                JSONRPC = req.JSONRPC,
                Result = new Null(),
            };
        }

        if (req.Method == Method.ClientUnregisterCapability)
        {
            // Accept all capability unregistrations
            return new ResponseMessage
            {
                ID = req.ID,
                JSONRPC = req.JSONRPC,
                Result = new Null(),
            };
        }

        // Unknown server request
        return new ResponseMessage
        {
            ID = req.ID,
            JSONRPC = req.JSONRPC,
            Error = new TypeScript.JsonRpc.ResponseError
            {
                Code = (int)ErrorCode.MethodNotFound,
                Message = $"Unknown method: {req.Method}",
            },
        };
    }

    // ---- fourslash.go:368-393 initialize ------------------------------------------------------------
    public void Initialize(IHarnessTest t, ClientCapabilities? capabilities, bool runExternalCode)
    {
        var initializationOptions = new InitializationOptions
        {
            CodeLensShowLocationsCommandName = ShowCodeLensLocationsCommandName,
        };
        if (runExternalCode)
        {
            initializationOptions.RunExternalCode = true;
        }

        var parameters = new InitializeParams
        {
            Locale = "en-US",
            InitializationOptions = new InitializationOptionsOrNull
            {
                InitializationOptions = initializationOptions,
            },
        };
        parameters.Capabilities = GetCapabilitiesWithDefaults(capabilities);
        _capabilities = parameters.Capabilities;
        var (message, _, ok) = _client.SendRequest(t, LsProto.LsProto.InitializeInfo, parameters);
        if (!ok)
        {
            t.Fatal("Initialize request failed");
        }

        if (message!.AsResponse().Error is not null)
        {
            t.Fatal($"Initialize request returned error: {message.AsResponse().Error!.String()}");
        }

        _client.SendNotification(t, LsProto.LsProto.InitializedInfo, new InitializedParams());

        // Wait for the initial configuration exchange to complete
        // The server will send workspace/configuration as part of handleInitialized
        //
        // 非 Go 源（偏离 Go）：Go 侧这里是 `<-f.client.Server.InitComplete()`，**无上限**地等一个 channel；
        // 服务端若在 handleInitialized 之前退出/被取消，这个信号永远不来，Go 由 `go test -timeout` 的
        // goroutine dump 兜底（10 分钟后报告"测试超时"）。C# 没有等价兜底 ⇒ 整个 harness 会永久挂死。
        // 因此这里带一个上限，并在超时时打印**两侧的状态**（客户端是否已取消/失败、服务端的退出原因），
        // 让"偶发死锁"变成一条可定位的用例失败。
        using (TypeScript.TestRunner.HarnessProgress.Wait("test thread waiting for the server's InitComplete signal"))
        {
            if (!_client.Server.InitComplete.Wait(_initCompleteTimeout))
            {
                t.Fatal(
                    $"the LSP server never signalled initComplete within {_initCompleteTimeout.TotalSeconds}s "
                    + $"(client context cancelled={_client.Context.IsCancellationRequested}, "
                    + $"client failure={Describe(_client.Failure)}, "
                    + $"server exit error={Describe(_client.Server.ExitError)})");
            }
        }
    }

    /// <summary>非 Go 源：<c>InitComplete</c> 等待的上限（见 <see cref="Initialize"/>）。单个用例正常的初始化
    /// 只需几十毫秒，30 秒足够宽松；一旦超时说明是死锁，不是慢。</summary>
    private static readonly TimeSpan _initCompleteTimeout = TimeSpan.FromSeconds(30);

    /// <summary>非 Go 源：把诊断用异常渲染成一行（异常消息里可能带换行）。</summary>
    private static string Describe(Exception? error) =>
        error is null ? "<none>" : $"{error.GetType().Name}: {error.Message.ReplaceLineEndings(" | ")}";
}

/// <summary>Go: <c>type scriptInfo struct</c>（fourslash.go:75-80）。LSP 侧的脚本内容 + 行表 + 版本。</summary>
public sealed class ScriptInfo(string fileName, string content) : Script
{
    internal string _fileName = fileName;
    internal string _content = content;
    internal LSPLineMap _lineMap = LsConv.LsConv.ComputeLSPLineStarts(content);
    internal int _version = 1;

    // ---- fourslash.go:116-120 editContent -----------------------------------------------------------
    public void EditContent(TextChange change)
    {
        _content = change.ApplyTo(_content);
        _lineMap = LsConv.LsConv.ComputeLSPLineStarts(_content);
        _version++;
    }

    // ---- fourslash.go:122-124 Text ----------------------------------------------------------------
    public string Text() => _content;

    // ---- fourslash.go:126 OriginalText ------------------------------------------------------------
    public string OriginalText() => _content;

    // ---- fourslash.go:128 SpanMap ----------------------------------------------------------------
    public SpanMapType? SpanMap() => null;

    // ---- fourslash.go:130-132 FileName ------------------------------------------------------------
    public string FileName() => _fileName;

    // ---- fourslash.go:134 OriginalFileName --------------------------------------------------------
    public string OriginalFileName() => _fileName;

    // ---- fourslash.go:136-150 GetLineContent ------------------------------------------------------
    public string GetLineContent(int line)
    {
        var numLines = _lineMap.LineStarts.Length;
        if (line < 0 || line >= numLines)
        {
            return "";
        }

        var start = (int)_lineMap.LineStarts[line];
        int end;
        if (line + 1 < numLines)
        {
            end = (int)_lineMap.LineStarts[line + 1];
        }
        else
        {
            end = _content.Length;
        }

        return _content[start..end].TrimEnd('\r', '\n');
    }
}

/// <summary>Go: <c>type testConverters struct</c>（fourslash.go:82-99）：内嵌 <c>*lsconv.Converters</c> 的替身。</summary>
public sealed class TestConverters(Converters converters)
{
    public readonly Converters Converters = converters;

    // ---- fourslash.go:90-93 PositionToLineAndCharacter ----------------------------------------------
    public Position PositionToLineAndCharacter(Script script, TextPos position)
    {
        var (lspPosition, _) = Converters.ToLSPPosition(script, position);
        return lspPosition;
    }

    // ---- fourslash.go:95-99 LineAndCharacterToPosition ---------------------------------------------
    public TextPos LineAndCharacterToPosition(Script script, Position position)
    {
        var positions = Converters.FromLSPPosition(script, position, TypeScript.SpanMap.Feature.All);
        Debug.Assert(positions.Length == 1, "fourslash script must have exactly one position projection");
        return positions[0].Position;
    }
}

/// <summary>Go: <c>type textEditSpan struct</c>（fourslash.go:101-105）。</summary>
public sealed class TextEditSpan
{
    public int Start;
    public int End;
    public int Length;
}

/// <summary>Go: <c>type FourslashOptions struct</c>（fourslash.go:165-169）。</summary>
public sealed class FourslashOptions
{
    public ClientCapabilities? Capabilities;
    public TypeScript.ContentMapper.Spawner? ContentMapperSpawner;
    public bool RunExternalCode;
}

/// <summary>
/// C# 侧新增（替代 go-cmp）：把两边用 <see cref="Json.Marshal"/> 编成 UTF-8 字节逐字节比较。
/// <c>null</c> 视作字面 <c>null</c>。注意编码器会省略零值 / omitzero 字段，故"两边都是默认值"的差异可能漏检。
/// </summary>
public static class FsCmp
{
    /// <summary>Go: <c>cmp.Diff(actual, expected, opts…)</c>。返回 <c>""</c> 表示相等。</summary>
    public static string Diff(object? actual, object? expected)
    {
        var expectedJson = Encoding.UTF8.GetString(global::TypeScript.Json.Json.Marshal(expected));
        var actualJson = Encoding.UTF8.GetString(global::TypeScript.Json.Json.Marshal(actual));
        return expectedJson == actualJson
            ? ""
            : $"- expected: {expectedJson}\n+ actual:   {actualJson}";
    }

    /// <summary>Go: <c>assert.DeepEqual(t, actual, expected)</c>：不相等即 <c>t.Fatal</c>。</summary>
    public static void AssertDeepEqual(IHarnessTest t, object? actual, object? expected)
    {
        var diff = Diff(actual, expected);
        if (diff.Length != 0)
        {
            t.Fatal(diff);
        }
    }
}

/// <summary>Go 的 fourslash 包级成员（fourslash.go 的 <c>NewFourslash*</c> 与包级常量/变量）。
/// 本类型是 partial：<c>ParseTestData</c> 等由 TestParser.cs 提供。</summary>
public static partial class Fourslash
{
    /// <summary>Go: <c>const rootDir = "/"</c>（fourslash.go:152）。</summary>
    public const string RootDir = "/";

    /// <summary>Go: 包级 <c>var parseCache</c>（fourslash.go:154）。</summary>
    internal static readonly TypeScript.Project.ParseCache ParseCache =
        TypeScript.Project.ParseCache.NewParseCache(new TypeScript.Project.RefCountCacheOptions
        {
            DisableDeletion = true,
        });

    // ---- fourslash.go:160-163 NewFourslash ----------------------------------------------------------
    public static (FourslashTest Test, Action Close) NewFourslash(
        IHarnessTest t,
        ClientCapabilities? capabilities,
        string content,
        [CallerFilePath] string testPath = "") =>
        NewFourslashCore(t, content, new FourslashOptions { Capabilities = capabilities }, testPath);

    // ---- fourslash.go:171-174 NewFourslashWithOptions ----------------------------------------------
    public static (FourslashTest Test, Action Close) NewFourslashWithOptions(
        IHarnessTest t,
        string content,
        FourslashOptions options,
        [CallerFilePath] string testPath = "") =>
        NewFourslashCore(t, content, options, testPath);

    // ---- fourslash.go:176-286 newFourslash ---------------------------------------------------------
    internal static (FourslashTest Test, Action Close) NewFourslashCore(
        IHarnessTest t,
        string content,
        FourslashOptions? options,
        string testPath)
    {
        options ??= new FourslashOptions();

        var fileName = FourslashTest.GetBaseFileNameFromTest(t) + ExtensionTs;
        var testfs = new Dictionary<string, object>();
        var scriptInfos = new Dictionary<string, ScriptInfo>();
        var testData = FourslashTest.ParseTestData(t, content, fileName);
        foreach (var file in testData.Files ?? [])
        {
            var filePath = GetNormalizedAbsolutePath(file._fileName, RootDir);
            // Dynamic files (e.g., untitled:) shouldn't be added to the VFS
            if (!IsDynamicFileName(filePath))
            {
                testfs[filePath] = file.Content;
            }

            scriptInfos[filePath] = FourslashTest.NewScriptInfo(filePath, file.Content);
        }

        foreach (var (link, target) in testData.Symlinks ?? [])
        {
            var filePath = GetNormalizedAbsolutePath(link, RootDir);
            testfs[filePath] = VfsTest.Symlink(GetNormalizedAbsolutePath(target, RootDir));
        }

        // !!! use default compiler options for inferred project as base
        var compilerOptions = new CompilerOptions
        {
            SkipDefaultLibCheck = Tristate.TSTrue,
            Target = ScriptTargetLatestStandard,
            Jsx = JsxEmit.Preserve,
        };
        var harnessOptions = new HarnessOptions { UseCaseSensitiveFileNames = true, CurrentDirectory = RootDir };
        HarnessUtil.SetOptionsFromTestConfig(t, testData.GlobalOptions ?? new Dictionary<string, string>(), compilerOptions, harnessOptions, RootDir, true /*allowUnknownOptions*/);
        // Go 的 `testData.GlobalOptions["tsc"]`（调 tsctests.GetFileMapWithBuild）未移植，见文件头偏离 2。

        HarnessUtil.SkipUnsupportedCompilerOptions(t, compilerOptions);

        var fsFromMap = VfsTest.FromMap(testfs, harnessOptions.UseCaseSensitiveFileNames);
        var fs = Bundled.Bundled.WrapFS(fsFromMap);

        var serverOpts = new Lsp.ServerOptions
        {
            Err = TextWriter.Null,

            Cwd = "/",
            FS = fs,
            DefaultLibraryPath = Bundled.Bundled.LibPath(),

            ParseCache = ParseCache,
        };
        if (options.ContentMapperSpawner is not null)
        {
            serverOpts.Spawn = options.ContentMapperSpawner;
        }

        var converters = FourslashTest.NewTestConverters(LsConv.LsConv.NewConverters(
            PositionEncodingKind.UTF8,
            name => scriptInfos.TryGetValue(name, out var scriptInfo) ? scriptInfo._lineMap : null));

        var f = new FourslashTest
        {
            _testData = testData,
            _stateEnableFormatting = true,
            _reportFormatOnTypeCrash = true,
            _userPreferences = TypeScript.LsUtil.LsUtil.NewDefaultUserPreferences(),
            _vfs = fs,
            _scriptInfos = scriptInfos,
            _converters = converters,
            _baselines = [],
            _openFiles = [],
            _semanticTokenTypes = FourslashTest.DefaultSemanticTokenTypes(),
            _semanticTokenModifiers = FourslashTest.DefaultSemanticTokenModifiers(),
        };
        var (client, closeClient) = LSPClient.NewLSPClient(t, serverOpts, f.HandleServerRequest);
        f._client = client;

        // !!! temporary; remove when we have `handleDidChangeConfiguration`/implicit project config support
        // !!! replace with a proper request *after initialize*
        client.SetCompilerOptionsForInferredProjects(compilerOptions);
        f.Initialize(t, options.Capabilities, options.RunExternalCode);

        if (testData.IsStateBaseliningEnabled())
        {
            // Single baseline, so initialize project state baseline too
            f._stateBaseline = StateBaseline.NewStateBaseline((FsWithSys)fsFromMap);
        }
        else
        {
            foreach (var file in testData.Files ?? [])
            {
                if (file._open)
                {
                    f.OpenFile(t, file._fileName);
                }
            }

            f._activeFilename = (testData.Files ?? [])[0]._fileName;
        }

        return (f, () =>
        {
            var err = closeClient();
            if (err is not null)
            {
                t.Error($"goroutine error: {err}");
            }

            f.VerifyBaselines(t, testPath);
        });
    }
}

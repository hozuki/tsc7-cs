// ported from: internal/testutil/harnessutil/sourcemap_recorder.go
//
// 逐条映射（Go → C#）：
// 1) `type writerAggregator struct { strings.Builder }` → 组合一个 `StringBuilder` 的小类：Go 的 `WriteString` 由嵌入的
//    Builder 提供，C# 里显式给同名方法（`WriteString`/`WriteStringf`/`WriteLine`/`WriteLinef`/`ToString`）。
//    **注意** `fmt.Fprintln` 写 "\n"，而 `WriteLine`（Go 自己的实现）写 "\r\n"——两者都逐字保留。
// 2) Go 的 `%s/%d` 格式化 → `string.Format` 的 `{0}/{1}`（调用点逐个改写，文本与 Go 逐字一致）。
// 3) `sourcemap.Mapping` 是指针字段语义：Go 里 `*Mapping` 可能为 nil（`writeSourceMapMarkerEx(nil, …)` 只用到
//    index/endColumn，不碰 currentSpan），所以那个形参在 C# 里是 `SourceMapSpanWithDecodeErrors?`。
// 4) `errors.New("No encoded entry found")` → `new Exception("No encoded entry found")`（Message 逐字一致）。
// 5) `panic(...)` → `Debug.Fail(...)`（DebugFailureException）。
// 6) `json.Marshal(sourceMap.SourcesContent)` → `Encoding.UTF8.GetString(TypeScript.Json.Json.Marshal(...))`
//    （Go 的 `%s` 把 []byte 当文本打印，C# 的 Marshal 返回 byte[]，见 Program.VerifyOptions.cs 的同款写法）。
// 7) `sourcemap.DecodeMappings(...).Next()` → C# 的 `TryNext(out var value)`（(T,bool) 语义反转为 bool+out）。
namespace TypeScript.TestUtil.HarnessUtil;

using System.Globalization;
using System.Text;
using TypeScript.Core;
using TypeScript.SourceMap;
using static TypeScript.Core.Core;
using static TypeScript.SourceMap.SourceMap;
using static TypeScript.StringUtil.StringUtil;

/// <summary>Go: <c>type writerAggregator struct { strings.Builder }</c>（sourcemap_recorder.go:16）。</summary>
internal sealed class WriterAggregator
{
    private readonly StringBuilder _builder = new();

    public void WriteString(string s) => _builder.Append(s);

    /// <summary>Go: <c>func (w *writerAggregator) WriteStringf(format string, args ...any)</c>（sourcemap_recorder.go:20）。</summary>
    public void WriteStringf(string format, params object?[] args) =>
        _builder.Append(string.Format(CultureInfo.InvariantCulture, format, args));

    /// <summary>Go: <c>func (w *writerAggregator) WriteLine(s string)</c>（sourcemap_recorder.go:24）。</summary>
    public void WriteLine(string s)
    {
        _builder.Append(s);
        _builder.Append("\r\n");
    }

    /// <summary>Go: <c>func (w *writerAggregator) WriteLinef(format string, args ...any)</c>（sourcemap_recorder.go:29）。</summary>
    public void WriteLinef(string format, params object?[] args) => WriteStringf(format + "\r\n", args);

    public override string ToString() => _builder.ToString();
}

/// <summary>Go: <c>type sourceMapSpanWithDecodeErrors struct { sourceMapSpan *sourcemap.Mapping; decodeErrors []string }</c>（sourcemap_recorder.go:33）。</summary>
internal sealed class SourceMapSpanWithDecodeErrors
{
    public Mapping SourceMapSpan = null!;

    public string[] DecodeErrors = [];
}

/// <summary>Go: <c>type decodedMapping struct { sourceMapSpan *sourcemap.Mapping; error error }</c>（sourcemap_recorder.go:38）。</summary>
internal sealed class DecodedMapping
{
    public Mapping SourceMapSpan = null!;

    public Exception? Error;
}

/// <summary>Go: <c>type sourceMapDecoder struct { sourceMapMappings string; mappings *sourcemap.MappingsDecoder }</c>（sourcemap_recorder.go:43）。</summary>
internal sealed class SourceMapDecoder
{
    private readonly string _sourceMapMappings;
    private readonly MappingsDecoder _mappings;

    /// <summary>Go: <c>func newSourceMapDecoder(sourceMap *sourcemap.RawSourceMap) *sourceMapDecoder</c>（sourcemap_recorder.go:48）。</summary>
    public SourceMapDecoder(RawSourceMap sourceMap)
    {
        _sourceMapMappings = sourceMap.Mappings;
        _mappings = DecodeMappings(sourceMap.Mappings);
    }

    /// <summary>Go: <c>func (d *sourceMapDecoder) decodeNextEncodedSourceMapSpan() *decodedMapping</c>（sourcemap_recorder.go:55）。</summary>
    public DecodedMapping DecodeNextEncodedSourceMapSpan()
    {
        if (!_mappings.TryNext(out var value))
        {
            var mapping = new DecodedMapping
            {
                Error = _mappings.Error(),
                SourceMapSpan = _mappings.State(),
            };
            if (mapping.Error is null)
            {
                mapping.Error = new Exception("No encoded entry found");
            }

            return mapping;
        }

        return new DecodedMapping { SourceMapSpan = value! };
    }

    /// <summary>Go: <c>func (d *sourceMapDecoder) hasCompletedDecoding() bool</c>（sourcemap_recorder.go:70）。</summary>
    public bool HasCompletedDecoding() => _mappings.Pos() == _sourceMapMappings.Length;

    /// <summary>Go: <c>func (d *sourceMapDecoder) getRemainingDecodeString() string</c>（sourcemap_recorder.go:74）。</summary>
    public string GetRemainingDecodeString() => _sourceMapMappings[_mappings.Pos()..];
}

/// <summary>Go: <c>type sourceMapSpanWriter struct</c>（sourcemap_recorder.go:78）。</summary>
internal sealed class SourceMapSpanWriter
{
    internal readonly WriterAggregator _sourceMapRecorder;
    internal readonly string[] _sourceMapSources;
    internal readonly string[] _sourceMapNames;
    internal readonly TestFile? _jsFile;
    internal ECMALineStarts _jsLineMap;
    internal string _tsCode = "";
    internal ECMALineStarts _tsLineMap;
    internal List<SourceMapSpanWithDecodeErrors> _spansOnSingleLine;
    internal int _prevWrittenSourcePos;
    internal int _nextJsLineToWrite;
    internal bool _spanMarkerContinues;
    private readonly SourceMapDecoder _sourceMapDecoder;

    /// <summary>Go: <c>func newSourceMapSpanWriter(sourceMapRecorder *writerAggregator, sourceMap *sourcemap.RawSourceMap, jsFile *TestFile) *sourceMapSpanWriter</c>（sourcemap_recorder.go:93）。</summary>
    internal SourceMapSpanWriter(WriterAggregator sourceMapRecorder, RawSourceMap sourceMap, TestFile? jsFile)
    {
        _sourceMapRecorder = sourceMapRecorder;
        _sourceMapSources = sourceMap.Sources ?? [];
        _sourceMapNames = sourceMap.Names ?? [];
        _jsFile = jsFile;
        _jsLineMap = ComputeECMALineStarts(jsFile!.Content);
        _spansOnSingleLine = [];
        _prevWrittenSourcePos = 0;
        _nextJsLineToWrite = 0;
        _spanMarkerContinues = false;
        _sourceMapDecoder = new SourceMapDecoder(sourceMap);

        sourceMapRecorder.WriteLine("===================================================================");
        sourceMapRecorder.WriteLinef("JsFile: {0}", sourceMap.File);
        sourceMapRecorder.WriteLinef("mapUrl: {0}", TryGetSourceMappingURL(CreateECMALineInfo(jsFile.Content, _jsLineMap)));
        sourceMapRecorder.WriteLinef("sourceRoot: {0}", sourceMap.SourceRoot);
        sourceMapRecorder.WriteLinef("sources: {0}", string.Join(",", _sourceMapSources));
        if (sourceMap.SourcesContent is { Length: > 0 } sourcesContent)
        {
            // Go: content, err := json.Marshal(sourceMap.SourcesContent); if err != nil { panic(err) }
            sourceMapRecorder.WriteLinef("sourcesContent: {0}", Encoding.UTF8.GetString(TypeScript.Json.Json.Marshal(sourcesContent)));
        }

        sourceMapRecorder.WriteLine("===================================================================");
    }

    /// <summary>Go: <c>func (w *sourceMapSpanWriter) getSourceMapSpanString(mapEntry *sourcemap.Mapping, getAbsentNameIndex bool) string</c>（sourcemap_recorder.go:123）。</summary>
    internal string GetSourceMapSpanString(Mapping mapEntry, bool getAbsentNameIndex)
    {
        var mapString = new WriterAggregator();
        mapString.WriteStringf("Emitted({0}, {1})", mapEntry.GeneratedLine + 1, (int)mapEntry.GeneratedCharacter + 1);
        if (mapEntry.IsSourceMapping())
        {
            mapString.WriteStringf(" Source({0}, {1}) + SourceIndex({2})", mapEntry.SourceLine + 1, (int)mapEntry.SourceCharacter + 1, (int)mapEntry.SourceIndex);
            var nameIndex = (int)mapEntry.NameIndex;
            if (nameIndex >= 0 && nameIndex < _sourceMapNames.Length)
            {
                mapString.WriteStringf(" name ({0})", _sourceMapNames[nameIndex]);
            }
            else if (nameIndex != (int)MissingName || getAbsentNameIndex)
            {
                mapString.WriteStringf(" nameIndex ({0})", nameIndex);
            }
        }

        return mapString.ToString();
    }

    /// <summary>Go: <c>func (w *sourceMapSpanWriter) recordSourceMapSpan(sourceMapSpan *sourcemap.Mapping)</c>（sourcemap_recorder.go:139）。</summary>
    internal void RecordSourceMapSpan(Mapping sourceMapSpan)
    {
        // verify the decoded span is same as the new span
        var decodeResult = _sourceMapDecoder.DecodeNextEncodedSourceMapSpan();
        var decodeErrors = new List<string>();
        if (decodeResult.Error is not null || !decodeResult.SourceMapSpan.Equals(sourceMapSpan))
        {
            if (decodeResult.Error is not null)
            {
                decodeErrors.Add("!!^^ !!^^ There was decoding error in the sourcemap at this location: " + decodeResult.Error.Message);
            }
            else
            {
                decodeErrors.Add("!!^^ !!^^ The decoded span from sourcemap's mapping entry does not match what was encoded for this span:");
            }

            decodeErrors.Add(
                "!!^^ !!^^ Decoded span from sourcemap's mappings entry: " +
                    GetSourceMapSpanString(decodeResult.SourceMapSpan, true /*getAbsentNameIndex*/) +
                    " Span encoded by the emitter:" +
                    GetSourceMapSpanString(sourceMapSpan, true /*getAbsentNameIndex*/));
        }

        if (_spansOnSingleLine.Count > 0 && _spansOnSingleLine[0].SourceMapSpan.GeneratedLine != sourceMapSpan.GeneratedLine)
        {
            // On different line from the one that we have been recording till now,
            WriteRecordedSpans();
            _spansOnSingleLine = [];
        }

        _spansOnSingleLine.Add(new SourceMapSpanWithDecodeErrors
        {
            SourceMapSpan = sourceMapSpan,
            DecodeErrors = [.. decodeErrors],
        });
    }

    /// <summary>Go: <c>func (w *sourceMapSpanWriter) recordNewSourceFileSpan(sourceMapSpan *sourcemap.Mapping, newSourceFileCode string)</c>（sourcemap_recorder.go:169）。</summary>
    internal void RecordNewSourceFileSpan(Mapping sourceMapSpan, string newSourceFileCode)
    {
        var continuesLine = false;
        if (_spansOnSingleLine.Count > 0 && (int)_spansOnSingleLine[0].SourceMapSpan.GeneratedCharacter == sourceMapSpan.GeneratedLine) // !!! char == line seems like a bug in Strada?
        {
            WriteRecordedSpans();
            _spansOnSingleLine = [];
            _nextJsLineToWrite--; // walk back one line to reprint the line
            continuesLine = true;
        }

        RecordSourceMapSpan(sourceMapSpan);

        if (_spansOnSingleLine.Count != 1)
        {
            // Go: panic("expected a single span")
            Debug.Fail("expected a single span");
        }

        _sourceMapRecorder.WriteLine("-------------------------------------------------------------------");
        if (continuesLine)
        {
            _sourceMapRecorder.WriteLinef("emittedFile:{0} ({1}, {2})", _jsFile!.UnitName, sourceMapSpan.GeneratedLine + 1, (int)sourceMapSpan.GeneratedCharacter + 1);
        }
        else
        {
            _sourceMapRecorder.WriteLinef("emittedFile:{0}", _jsFile!.UnitName);
        }

        _sourceMapRecorder.WriteLinef("sourceFile:{0}", _sourceMapSources[(int)_spansOnSingleLine[0].SourceMapSpan.SourceIndex]);
        _sourceMapRecorder.WriteLine("-------------------------------------------------------------------");

        _tsLineMap = ComputeECMALineStarts(newSourceFileCode);
        _tsCode = newSourceFileCode;
        _prevWrittenSourcePos = 0;
    }

    /// <summary>Go: <c>func (w *sourceMapSpanWriter) close()</c>（sourcemap_recorder.go:198）。</summary>
    internal void Close()
    {
        // Write the lines pending on the single line
        WriteRecordedSpans();

        if (!_sourceMapDecoder.HasCompletedDecoding())
        {
            _sourceMapRecorder.WriteLine("!!!! **** There are more source map entries in the sourceMap's mapping than what was encoded");
            _sourceMapRecorder.WriteLinef("!!!! **** Remaining decoded string: {0}", _sourceMapDecoder.GetRemainingDecodeString());
        }

        // write remaining js lines
        WriteJsFileLines(_jsLineMap.Length);
    }

    /// <summary>Go: <c>func (w *sourceMapSpanWriter) getTextOfLine(line int, lineMap []core.TextPos, code string) string</c>（sourcemap_recorder.go:211）。</summary>
    internal string GetTextOfLine(int line, ECMALineStarts lineMap, string code)
    {
        var startPos = (int)lineMap[line];
        int endPos;
        if (line + 1 < lineMap.Length)
        {
            endPos = (int)lineMap[line + 1];
        }
        else
        {
            endPos = code.Length;
        }

        var text = code[startPos..endPos];
        if (line == 0)
        {
            return RemoveByteOrderMark(text);
        }

        // return line == 0 ? Utils.removeByteOrderMark(text) : text;
        return text;
    }

    /// <summary>Go: <c>func (w *sourceMapSpanWriter) writeJsFileLines(endJsLine int)</c>（sourcemap_recorder.go:227）。</summary>
    internal void WriteJsFileLines(int endJsLine)
    {
        for (; _nextJsLineToWrite < endJsLine; _nextJsLineToWrite++)
        {
            _sourceMapRecorder.WriteStringf(">>>{0}", GetTextOfLine(_nextJsLineToWrite, _jsLineMap, _jsFile!.Content));
        }
    }

    /// <summary>Go: <c>func (w *sourceMapSpanWriter) writeRecordedSpans()</c>（sourcemap_recorder.go:233）。</summary>
    internal void WriteRecordedSpans()
    {
        var recordedSpanWriter = new RecordedSpanWriter(this);
        recordedSpanWriter.WriteRecordedSpans();
    }
}

/// <summary>Go: <c>type recordedSpanWriter struct { markerIds []string; prevEmittedCol int; w *sourceMapSpanWriter }</c>（sourcemap_recorder.go:238）。</summary>
internal sealed class RecordedSpanWriter(SourceMapSpanWriter w)
{
    internal readonly List<string> _markerIds = [];
    internal int _prevEmittedCol;
    internal readonly SourceMapSpanWriter _w = w;

    /// <summary>Go: <c>func (sw *recordedSpanWriter) getMarkerId(markerIndex int) string</c>（sourcemap_recorder.go:244）。</summary>
    internal string GetMarkerId(int markerIndex)
    {
        string markerId;
        if (_w._spanMarkerContinues)
        {
            if (markerIndex != 0)
            {
                // Go: panic("expected markerIndex to be 0")
                Debug.Fail("expected markerIndex to be 0");
            }

            markerId = "1->";
        }
        else
        {
            markerId = (markerIndex + 1).ToString(CultureInfo.InvariantCulture);
            if (markerId.Length < 2)
            {
                markerId += " ";
            }

            markerId += ">";
        }

        return markerId;
    }

    /// <summary>Go: <c>func (sw *recordedSpanWriter) iterateSpans(fn func(currentSpan *sourceMapSpanWithDecodeErrors, index int))</c>（sourcemap_recorder.go:261）。</summary>
    internal void IterateSpans(Action<SourceMapSpanWithDecodeErrors, int> fn)
    {
        _prevEmittedCol = 0;
        for (var i = 0; i < _w._spansOnSingleLine.Count; i++)
        {
            fn(_w._spansOnSingleLine[i], i);
            _prevEmittedCol = (int)_w._spansOnSingleLine[i].SourceMapSpan.GeneratedCharacter;
        }
    }

    /// <summary>Go: <c>func (sw *recordedSpanWriter) writeSourceMapIndent(indentLength int, indentPrefix string)</c>（sourcemap_recorder.go:269）。</summary>
    internal void WriteSourceMapIndent(int indentLength, string indentPrefix)
    {
        _w._sourceMapRecorder.WriteString(indentPrefix);
        for (var i = 0; i < indentLength; i++)
        {
            _w._sourceMapRecorder.WriteString(" ");
        }
    }

    /// <summary>Go: <c>func (sw *recordedSpanWriter) writeSourceMapMarker(currentSpan *sourceMapSpanWithDecodeErrors, index int)</c>（sourcemap_recorder.go:276）。</summary>
    internal void WriteSourceMapMarker(SourceMapSpanWithDecodeErrors currentSpan, int index)
    {
        WriteSourceMapMarkerEx(currentSpan, index, (int)currentSpan.SourceMapSpan.GeneratedCharacter, false /*endContinues*/);
    }

    /// <summary>Go: <c>func (sw *recordedSpanWriter) writeSourceMapMarkerEx(currentSpan *sourceMapSpanWithDecodeErrors, index int, endColumn int, endContinues bool)</c>（sourcemap_recorder.go:280）。</summary>
    internal void WriteSourceMapMarkerEx(SourceMapSpanWithDecodeErrors? currentSpan, int index, int endColumn, bool endContinues)
    {
        var markerId = GetMarkerId(index);
        _markerIds.Add(markerId);
        WriteSourceMapIndent(_prevEmittedCol, markerId);
        for (var i = _prevEmittedCol; i < endColumn; i++)
        {
            _w._sourceMapRecorder.WriteString("^");
        }

        if (endContinues)
        {
            _w._sourceMapRecorder.WriteString("->");
        }

        _w._sourceMapRecorder.WriteLine("");
        _w._spanMarkerContinues = endContinues;
    }

    /// <summary>Go: <c>func (sw *recordedSpanWriter) writeSourceMapSourceText(currentSpan *sourceMapSpanWithDecodeErrors, index int)</c>（sourcemap_recorder.go:294）。</summary>
    internal void WriteSourceMapSourceText(SourceMapSpanWithDecodeErrors currentSpan, int index)
    {
        // Convert UTF-16 character offset from the source map to a byte position.
        var sourcePos = TypeScript.Scanner.Scanner.ComputePositionOfLineAndUTF16Character(
            _w._tsLineMap,
            currentSpan.SourceMapSpan.SourceLine,
            currentSpan.SourceMapSpan.SourceCharacter,
            _w._tsCode,
            true /*allowEdits*/);
        var sourceText = "";
        if (_w._prevWrittenSourcePos < sourcePos)
        {
            // Position that goes forward, get text
            sourceText = _w._tsCode[_w._prevWrittenSourcePos..sourcePos];
        }

        // If there are decode errors, write
        foreach (var decodeError in currentSpan.DecodeErrors)
        {
            WriteSourceMapIndent(_prevEmittedCol, _markerIds[index]);
            _w._sourceMapRecorder.WriteLine(decodeError);
        }

        var tsCodeLineMap = ComputeECMALineStarts(sourceText);
        for (var i = 0; i < tsCodeLineMap.Length; i++)
        {
            if (i == 0)
            {
                WriteSourceMapIndent(_prevEmittedCol, _markerIds[index]);
            }
            else
            {
                WriteSourceMapIndent(_prevEmittedCol, "  >");
            }

            _w._sourceMapRecorder.WriteString(_w.GetTextOfLine(i, tsCodeLineMap, sourceText));
            if (i == tsCodeLineMap.Length - 1)
            {
                _w._sourceMapRecorder.WriteLine("");
            }
        }

        _w._prevWrittenSourcePos = sourcePos;
    }

    /// <summary>Go: <c>func (sw *recordedSpanWriter) writeSpanDetails(currentSpan *sourceMapSpanWithDecodeErrors, index int)</c>（sourcemap_recorder.go:331）。</summary>
    internal void WriteSpanDetails(SourceMapSpanWithDecodeErrors currentSpan, int index)
    {
        _w._sourceMapRecorder.WriteLinef("{0}{1}", _markerIds[index], _w.GetSourceMapSpanString(currentSpan.SourceMapSpan, false /*getAbsentNameIndex*/));
    }

    /// <summary>Go: <c>func (sw *recordedSpanWriter) writeRecordedSpans()</c>（sourcemap_recorder.go:335）。</summary>
    internal void WriteRecordedSpans()
    {
        var w = _w;

        if (w._spansOnSingleLine.Count > 0)
        {
            var currentJsLine = w._spansOnSingleLine[0].SourceMapSpan.GeneratedLine;

            // Write js line
            w.WriteJsFileLines(currentJsLine + 1);

            // Emit markers
            IterateSpans(WriteSourceMapMarker);

            var jsFileText = w.GetTextOfLine(currentJsLine + 1, w._jsLineMap, w._jsFile!.Content); // TODO: Strada is wrong here, we should be looking at `currentJsLine`, not `currentJsLine+1`
            if (_prevEmittedCol < jsFileText.Length - 1)
            {
                // There is remaining text on this line that will be part of next source span so write marker that continues
                WriteSourceMapMarkerEx(null /*currentSpan*/, w._spansOnSingleLine.Count, jsFileText.Length - 1 /*endColumn*/, true /*endContinues*/);
            }

            // Emit Source text
            IterateSpans(WriteSourceMapSourceText);

            // Emit column number etc
            IterateSpans(WriteSpanDetails);

            w._sourceMapRecorder.WriteLine("---");
        }
    }
}

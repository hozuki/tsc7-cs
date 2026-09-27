// ported from: internal/printer/printer.go:5812-6154（source map + 名字生成作用域）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `sourcemap.Generator` 在 C# 契约里把 Go 的 `error` 返回值去掉（出错即抛异常），所以 Go 的
//    `if err := …; err != nil { panic(err) }` 包裹整体删除，只留调用本身（`SetSourceContent` /
//    `AddGeneratedMapping` / `AddSourceMapping`）。
// 2) Go 的 `*sourceMapState`（arena 指针，nil = "没有"）→ 可空结构体 `SourceMapState?`（Printer.cs 文件头
//    第 9 条）；arena 槽位仍按 Go "写一次、之后只读" 的用法（`ref var s = ref _sourceMapStateArena.New(); s = …`）。
// 3) `tspath.FileExtensionIs(source.FileName(), tspath.ExtensionJson)` → 全限定
//    `TSPath.TSPath.FileExtensionIs(…, TSPath.TSPath.ExtensionJson)`（与 Ast/Utilities.cs:3877 同款，无需 using）。
// 4) `lineCharacterCache.getLineAndCharacter(pos) (line int, character core.UTF16Offset)` →
//    `LineCharacterCache.GetLineAndCharacter(int pos)` 返回元组 `(int Line, UTF16Offset Character)`（与
//    printer utilities 分片钉死的契约；本文件只按此调用）。
// 5) Go 直读字段 `p.sourceMapGenerator`/`p.sourceMapLineCharCache`/`p.MapSourcePosition`；C# 的可空流分析
//    在委托调用之后可能不再把字段当作非空，故用局部变量承接（取值时机与读写顺序与 Go 完全一致）。
// 6) `for range nodes.Nodes` → 显式判空 + `foreach`（Go 的 nil 切片与空切片同义，docs §3.2）。
// 7) `emitSourcePos` 与 Go 一样**不**保存/恢复 `sourceMapSourceIsJson`（与 `emitPos` 里的保存/恢复不对称，
//    这是 Go 的既有行为，逐行照抄，不"顺手修正"）。
// 8) 本段落里没有 `panic`/`fmt.Sprintf`/`debug.Assert`，也没有枚举 `%v` 格式化。
// 9) 末尾两个被 Go 注释掉的 `emitPosName`/`emitSourcePosName` 保持注释（连同 Go 的 TODO 一起保留）。
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    //
    // Source Maps
    //

    internal void SetSourceMapSource(SourceMap.Source source)
    {
        if (_sourceMapsDisabled)
        {
            return;
        }

        _sourceMapSource = source;
        _sourceMapLineCharCache = NewLineCharacterCache(source);
        if (_mostRecentSourceMapSource == source)
        {
            _sourceMapSourceIndex = _mostRecentSourceMapSourceIndex;
            return;
        }

        _sourceMapSourceIsJson = TSPath.TSPath.FileExtensionIs(source.FileName(), TSPath.TSPath.ExtensionJson);
        if (_sourceMapSourceIsJson)
        {
            return;
        }

        _sourceMapSourceIndex = _sourceMapGenerator!.AddSource(source.FileName());
        if (Options.InlineSources)
        {
            // Go: `if err := …; err != nil { panic(err) }`（文件头第 1 条）。
            _sourceMapGenerator!.SetSourceContent(_sourceMapSourceIndex, source.Text());
        }

        _mostRecentSourceMapSource = source;
        _mostRecentSourceMapSourceIndex = _sourceMapSourceIndex;
    }

    internal void EmitPos(int pos)
    {
        if (_sourceMapsDisabled || _sourceMapSource is null || _sourceMapGenerator is null || _sourceMapSourceIsJson || PositionIsSynthesized(pos))
        {
            return;
        }

        // Go 直读字段；C# 用局部承接（文件头第 5 条）。
        var generator = _sourceMapGenerator!;
        var source = _sourceMapSource!;
        var sourceIndex = _sourceMapSourceIndex;
        var lineCharCache = _sourceMapLineCharCache!;
        var mapSourcePosition = MapSourcePosition;
        if (mapSourcePosition is not null)
        {
            var ok = mapSourcePosition(source, pos, out var mappedSource, out var mappedPos);
            if (!ok)
            {
                generator.AddGeneratedMapping(_writer.GetLine(), _writer.GetColumn());
                return;
            }

            pos = mappedPos;
            if (mappedSource != source)
            {
                var savedSource = _sourceMapSource;
                var savedSourceIndex = _sourceMapSourceIndex;
                var savedSourceIsJson = _sourceMapSourceIsJson;
                var savedLineCharCache = _sourceMapLineCharCache;
                SetSourceMapSource(mappedSource);
                sourceIndex = _sourceMapSourceIndex;
                lineCharCache = _sourceMapLineCharCache!;
                _sourceMapSource = savedSource;
                _sourceMapSourceIndex = savedSourceIndex;
                _sourceMapSourceIsJson = savedSourceIsJson;
                _sourceMapLineCharCache = savedLineCharCache;
            }
        }

        var (sourceLine, sourceCharacter) = lineCharCache.GetLineAndCharacter(pos);
        generator.AddSourceMapping(
            _writer.GetLine(),
            _writer.GetColumn(),
            sourceIndex,
            sourceLine,
            sourceCharacter);
    }

    // TODO: Support emitting nameIndex for source maps
    ////func (p *Printer) emitPosName(pos int, name string) {
    ////	if p.sourceMapsDisabled || p.sourceMapSource == nil || p.sourceMapGenerator == nil || p.sourceMapSourceIsJson || ast.PositionIsSynthesized(pos) {
    ////		return
    ////	}
    ////
    ////	sourceLine, sourceCharacter := scanner.GetLineAndCharacterOfPosition(p.sourceMapSource, pos)
    ////	nameIndex := p.sourceMapGenerator.AddName(name)
    ////	if err := p.sourceMapGenerator.AddNamedSourceMapping(
    ////		p.writer.GetLine(),
    ////		p.writer.GetColumn(),
    ////		p.sourceMapSourceIndex,
    ////		sourceLine,
    ////		sourceCharacter,
    ////		nameIndex,
    ////	); err != nil {
    ////		panic(err)
    ////	}
    ////}

    internal void EmitSourcePos(SourceMap.Source source, int pos)
    {
        if (source != _sourceMapSource)
        {
            var savedSourceMapSource = _sourceMapSource;
            var savedSourceMapSourceIndex = _sourceMapSourceIndex;
            var savedSourceMapLineCharCache = _sourceMapLineCharCache;
            SetSourceMapSource(source);
            EmitPos(pos);
            _sourceMapSource = savedSourceMapSource;
            _sourceMapSourceIndex = savedSourceMapSourceIndex;
            _sourceMapLineCharCache = savedSourceMapLineCharCache;
        }
        else
        {
            EmitPos(pos);
        }
    }

    // TODO: Support emitting nameIndex for source maps
    ////func (p *Printer) emitSourcePosName(source sourcemap.Source, pos int, name string) {
    ////	if source != p.sourceMapSource {
    ////		savedSourceMapSource := p.sourceMapSource
    ////		savedSourceMapSourceIndex := p.sourceMapSourceIndex
    ////		p.setSourceMapSource(source)
    ////		p.emitPosName(pos, name)
    ////		p.sourceMapSource = savedSourceMapSource
    ////		p.sourceMapSourceIndex = savedSourceMapSourceIndex
    ////	} else {
    ////		p.emitPosName(pos, name)
    ////	}
    ////}

    internal SourceMapState? EmitSourceMapsBeforeNode(Node node)
    {
        if (!ShouldEmitSourceMaps(node))
        {
            return null;
        }

        var emitFlags = _emitContext.EmitFlags(node);
        var loc = _emitContext.SourceMapRange(node);

        if (!IsNotEmittedStatement(node) &&
            (emitFlags & EmitFlags.NoLeadingSourceMap) == 0 &&
            _currentSourceFile is not null &&
            !PositionIsSynthesized(loc.Pos()))
        {
            EmitSourcePos(_sourceMapSource!, SkipTrivia(_currentSourceFile.Text(), loc.Pos()));
        }

        if ((emitFlags & EmitFlags.NoNestedSourceMaps) != 0)
        {
            _sourceMapsDisabled = true;
        }

        ref var state = ref _sourceMapStateArena.New();
        state = new SourceMapState { EmitFlags = emitFlags, SourceMapRange = loc, HasTokenSourceMapRange = false };
        return state;
    }

    internal void EmitSourceMapsAfterNode(Node node, SourceMapState? previousState)
    {
        if (previousState is null)
        {
            return;
        }

        var emitFlags = previousState.Value.EmitFlags;
        var loc = previousState.Value.SourceMapRange;

        if ((emitFlags & EmitFlags.NoNestedSourceMaps) != 0)
        {
            _sourceMapsDisabled = false;
        }

        if (!IsNotEmittedStatement(node) &&
            (emitFlags & EmitFlags.NoTrailingSourceMap) == 0 &&
            !PositionIsSynthesized(loc.End()))
        {
            EmitSourcePos(_sourceMapSource!, loc.End());
        }
    }

    internal SourceMapState? EmitSourceMapsBeforeToken(Kind token, int pos, Node contextNode, TokenEmitFlags flags)
    {
        if (!ShouldEmitTokenSourceMaps(token, pos, contextNode, flags))
        {
            return null;
        }

        var emitFlags = _emitContext.EmitFlags(contextNode);
        var hasLoc = _emitContext.TryTokenSourceMapRange(contextNode, token, out var loc);
        if (hasLoc)
        {
            pos = loc.Pos();
        }

        if (pos >= 0 && _currentSourceFile is not null)
        {
            pos = SkipTrivia(_currentSourceFile.Text(), pos);
        }

        if ((emitFlags & EmitFlags.NoTokenLeadingSourceMaps) == 0 && pos >= 0)
        {
            EmitSourcePos(_sourceMapSource!, pos);
        }

        ref var state = ref _sourceMapStateArena.New();
        state = new SourceMapState { EmitFlags = emitFlags, SourceMapRange = loc, HasTokenSourceMapRange = hasLoc };
        return state;
    }

    internal void EmitSourceMapsAfterToken(Kind token, int pos, Node contextNode, SourceMapState? previousState)
    {
        if (previousState is null)
        {
            return;
        }

        var emitFlags = previousState.Value.EmitFlags;
        var loc = previousState.Value.SourceMapRange;
        var hasLoc = previousState.Value.HasTokenSourceMapRange;
        if ((emitFlags & EmitFlags.NoTokenTrailingSourceMaps) == 0)
        {
            if (hasLoc)
            {
                pos = loc.End();
            }

            if (pos >= 0)
            {
                EmitSourcePos(_sourceMapSource!, pos);
            }
        }
    }

    //
    // Name Generation
    //

    internal bool ShouldReuseTempVariableScope(Node? node)
    {
        return node is not null && (_emitContext.EmitFlags(node) & EmitFlags.ReuseTempVariableScope) != 0;
    }

    internal void PushNameGenerationScope(Node node)
    {
        _nameGenerator.PushScope(ShouldReuseTempVariableScope(node));
    }

    internal void PopNameGenerationScope(Node node)
    {
        _nameGenerator.PopScope(ShouldReuseTempVariableScope(node));
    }

    internal void GenerateAllNames(NodeList? nodes)
    {
        if (nodes is null)
        {
            return;
        }

        // Go: `for _, node := range nodes.Nodes`（nil 切片与空切片同义，文件头第 6 条）。
        var list = nodes.Nodes;
        if (list is null)
        {
            return;
        }

        foreach (var node in list)
        {
            GenerateNames(node);
        }
    }

    internal void GenerateNames(Node? node)
    {
        if (node is null)
        {
            return;
        }

        switch (node.Kind)
        {
            case Kind.Block or Kind.CaseClause or Kind.DefaultClause:
                GenerateAllNames(node.StatementList());
                break;
            case Kind.LabeledStatement or Kind.WithStatement or Kind.DoStatement or Kind.WhileStatement:
                GenerateNames(node.Statement());
                break;
            case Kind.IfStatement:
                GenerateNames(node.AsIfStatement()!.ThenStatement);
                GenerateNames(node.AsIfStatement()!.ElseStatement);
                break;
            case Kind.ForStatement or Kind.ForOfStatement or Kind.ForInStatement:
                GenerateNames(node.Initializer());
                GenerateNames(node.Statement());
                break;
            case Kind.SwitchStatement:
                GenerateNames(node.AsSwitchStatement()!.CaseBlock);
                break;
            case Kind.CaseBlock:
                GenerateAllNames(node.AsCaseBlock()!.Clauses);
                break;
            case Kind.TryStatement:
                GenerateNames(node.AsTryStatement()!.TryBlock);
                GenerateNames(node.AsTryStatement()!.CatchClause);
                GenerateNames(node.AsTryStatement()!.FinallyBlock);
                break;
            case Kind.CatchClause:
                GenerateNames(node.AsCatchClause()!.VariableDeclaration);
                GenerateNames(node.AsCatchClause()!.Block);
                break;
            case Kind.VariableStatement:
                GenerateNames(node.AsVariableStatement()!.DeclarationList);
                break;
            case Kind.VariableDeclarationList:
                GenerateAllNames(node.AsVariableDeclarationList()!.Declarations);
                break;
            case Kind.VariableDeclaration or Kind.Parameter or Kind.BindingElement or Kind.ClassDeclaration:
                GenerateNameIfNeeded(node.Name());
                break;
            case Kind.FunctionDeclaration:
                GenerateNameIfNeeded(node.Name());
                if (ShouldReuseTempVariableScope(node))
                {
                    GenerateAllNames(node.AsFunctionDeclaration()!.Parameters);
                    GenerateNames(node.AsFunctionDeclaration()!.Body);
                }

                break;
            case Kind.ObjectBindingPattern or Kind.ArrayBindingPattern:
                GenerateAllNames(node.ElementList());
                break;
            case Kind.ImportDeclaration or Kind.JSImportDeclaration:
                GenerateNames(node.AsImportDeclaration()!.ImportClause);
                break;
            case Kind.ImportClause:
                GenerateNameIfNeeded(node.AsImportClause()!.Name());
                GenerateNames(node.AsImportClause()!.NamedBindings);
                break;
            case Kind.NamespaceImport or Kind.NamespaceExport:
                GenerateNameIfNeeded(node.Name());
                break;
            case Kind.NamedImports:
                GenerateAllNames(node.ElementList());
                break;
            case Kind.ImportSpecifier:
                var n = node.AsImportSpecifier()!;
                if (n.PropertyName is not null)
                {
                    GenerateNameIfNeeded(n.PropertyName);
                }
                else
                {
                    GenerateNameIfNeeded(n.Name());
                }

                break;
        }
    }

    internal void GenerateAllMemberNames(NodeList? nodes)
    {
        if (nodes is null)
        {
            return;
        }

        // Go: `for _, node := range nodes.Nodes`（nil 切片与空切片同义，文件头第 6 条）。
        var list = nodes.Nodes;
        if (list is null)
        {
            return;
        }

        foreach (var node in list)
        {
            GenerateMemberNames(node);
        }
    }

    internal void GenerateMemberNames(Node? node)
    {
        if (node is null)
        {
            return;
        }

        switch (node.Kind)
        {
            case Kind.PropertyAssignment or
                Kind.ShorthandPropertyAssignment or
                Kind.PropertyDeclaration or
                Kind.PropertySignature or
                Kind.MethodDeclaration or
                Kind.MethodSignature or
                Kind.GetAccessor or
                Kind.SetAccessor:
                GenerateNameIfNeeded(node.Name());
                break;
        }
    }

    internal void GenerateNameIfNeeded(Node? name)
    {
        if (name is not null)
        {
            if (IsMemberName(name))
            {
                GenerateName(name);
            }
            else if (IsBindingPattern(name))
            {
                GenerateNames(name);
            }
        }
    }

    /// <summary>Generate the text for a generated identifier or private identifier.</summary>
    internal void GenerateName(Node name)
    {
        _ = _nameGenerator.GenerateName(name);
    }

    /// <summary>Returns a value indicating whether a name is unique globally or within the current file.</summary>
    internal bool IsFileLevelUniqueNameInCurrentFile(string name, bool _)
    {
        if (_currentSourceFile is not null)
        {
            return _emitContext.IsFileLevelUniqueName(_currentSourceFile, name, HasGlobalName);
        }

        return true;
    }
}

// ported from: internal/printer/printer.go:5044-5290（Emit / EmitSourceFile / setSourceFile / Write 总派发）
//   + internal/printer/printer.go:6201-6222（tokenEmitFlags / enterToken / exitToken）
//   + internal/printer/printer.go:6155-6200（enterNode / exitNode / enterTokenNode / exitTokenNode）
//   + internal/printer/printer.go:6223-6342（ListFormat + getOpeningBracket / getClosingBracket）
//
// 与 Go 的偏离（逐条）：
// 1) `printerState`（Go 值类型，含两个 arena 指针）→ C# 结构体 `PrinterState`，两个字段是
//    `CommentState?`/`SourceMapState?`（见 Printer.cs 文件头第 9 条）。
// 2) `enterToken` 的 `(printerState, int)` 返回 → C# 元组 `(PrinterState State, int Pos)`；调用点照 Go 解构。
// 3) Go 的 `p.writer.(interface{ Grow(n int) })` 结构化断言 → C# 显式接口 `IGrowableTextWriter`
//    （TextWriter.cs 文件头第 4 条已登记；C# 没有结构化类型）。
// 4) `Write` 的 switch 里 `node.AsXxx()` 全部加 `!`：C# 的 `AsXxx()` 在 kind 不匹配时返回 null，而 Go 的
//    类型断言在同样情形下 panic —— 分支已由 `case ast.KindXxx` 保证，`!` 与 Go 的语义一一对应。
// 5) `LFNone`/`LFSingleLine`/`LFNotDelimited` 都是 0，C# enum 允许多个成员取同一值，照 Go 保留全部名字。
using System;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    public string Emit(Node node, SourceFile? sourceFile)
    {
        // ensure a reusable writer
        _ownWriter ??= NewTextWriter(Options.NewLine.GetNewLineCharacter(), 0);

        Write(node, sourceFile, _ownWriter, null /*sourceMapGenerator*/);
        var text = _ownWriter.String();

        _ownWriter.Clear();
        return text;
    }

    public string EmitSourceFile(SourceFile sourceFile)
    {
        return Emit(sourceFile.AsNode(), sourceFile);
    }

    internal void SetSourceFile(SourceFile? sourceFile)
    {
        _currentSourceFile = sourceFile;
        _uniqueHelperNames = null;
        _externalHelpersModuleName = null;
        if (sourceFile is not null)
        {
            if ((_emitContext.EmitFlags(_emitContext.MostOriginal(sourceFile.AsNode())!) & EmitFlags.ExternalHelpers) != 0)
            {
                _uniqueHelperNames = new Dictionary<string, Identifier>();
            }
            _externalHelpersModuleName = _emitContext.GetExternalHelpersModuleName(sourceFile);
            SetSourceMapSource(sourceFile);
        }

        // !!!
    }

    public void Write(Node node, SourceFile? sourceFile, EmitTextWriter writer, SourceMap.Generator? sourceMapGenerator)
    {
        var savedCurrentSourceFile = _currentSourceFile;
        var savedWriter = _writer;
        var savedUniqueHelperNames = _uniqueHelperNames;
        var savedSourceMapsDisabled = _sourceMapsDisabled;
        var savedSourceMapGenerator = _sourceMapGenerator;
        var savedSourceMapSource = _sourceMapSource;
        var savedSourceMapSourceIndex = _sourceMapSourceIndex;
        var savedSourceMapLineCharCache = _sourceMapLineCharCache;

        _sourceMapsDisabled = sourceMapGenerator is null;
        _sourceMapGenerator = sourceMapGenerator;
        _sourceMapSource = null;
        _sourceMapSourceIndex = -1;
        _sourceMapLineCharCache = null;

        SetSourceFile(sourceFile);
        if (Options.OmitTrailingSemicolon)
        {
            writer = GetTrailingSemicolonDeferringWriter(writer);
        }
        _writer = writer;
        _writer.Clear();
        if (sourceFile is not null && _writer is IGrowableTextWriter grower)
        {
            grower.Grow(sourceFile.Text().Length);
        }

        switch (node.Kind)
        {
            // Pseudo-literals
            case Kind.TemplateHead:
                EmitTemplateHead(node.AsTemplateHead()!);
                break;
            case Kind.TemplateMiddle:
                EmitTemplateMiddle(node.AsTemplateMiddle()!);
                break;
            case Kind.TemplateTail:
                EmitTemplateTail(node.AsTemplateTail()!);
                break;

            // Identifiers
            case Kind.Identifier:
                EmitIdentifierName(node.AsIdentifier()!);
                break;

            // PrivateIdentifiers
            case Kind.PrivateIdentifier:
                EmitPrivateIdentifier(node.AsPrivateIdentifier()!);
                break;

            // Parse tree nodes
            // Names
            case Kind.QualifiedName:
                EmitQualifiedName(node.AsQualifiedName()!);
                break;
            case Kind.ComputedPropertyName:
                EmitComputedPropertyName(node.AsComputedPropertyName()!);
                break;

            // Signature elements
            case Kind.TypeParameter:
                EmitTypeParameter(node.AsTypeParameterDeclaration()!);
                break;
            case Kind.Parameter:
                EmitParameter(node.AsParameterDeclaration()!);
                break;
            case Kind.Decorator:
                EmitDecorator(node.AsDecorator()!);
                break;

            // Type members
            case Kind.PropertySignature:
                EmitPropertySignature(node.AsPropertySignatureDeclaration()!);
                break;
            case Kind.PropertyDeclaration:
                EmitPropertyDeclaration(node.AsPropertyDeclaration()!);
                break;
            case Kind.MethodSignature:
                EmitMethodSignature(node.AsMethodSignatureDeclaration()!);
                break;
            case Kind.MethodDeclaration:
                EmitMethodDeclaration(node.AsMethodDeclaration()!);
                break;
            case Kind.ClassStaticBlockDeclaration:
                EmitClassStaticBlockDeclaration(node.AsClassStaticBlockDeclaration()!);
                break;
            case Kind.Constructor:
                EmitConstructor(node.AsConstructorDeclaration()!);
                break;
            case Kind.GetAccessor:
                EmitGetAccessorDeclaration(node.AsGetAccessorDeclaration()!);
                break;
            case Kind.SetAccessor:
                EmitSetAccessorDeclaration(node.AsSetAccessorDeclaration()!);
                break;
            case Kind.CallSignature:
                EmitCallSignature(node.AsCallSignatureDeclaration()!);
                break;
            case Kind.ConstructSignature:
                EmitConstructSignature(node.AsConstructSignatureDeclaration()!);
                break;
            case Kind.IndexSignature:
                EmitIndexSignature(node.AsIndexSignatureDeclaration()!);
                break;

            // Binding patterns
            case Kind.ObjectBindingPattern:
                EmitObjectBindingPattern(node.AsBindingPattern()!);
                break;
            case Kind.ArrayBindingPattern:
                EmitArrayBindingPattern(node.AsBindingPattern()!);
                break;
            case Kind.BindingElement:
                EmitBindingElement(node.AsBindingElement()!);
                break;

            // Misc
            case Kind.TemplateSpan:
                EmitTemplateSpan(node.AsTemplateSpan()!);
                break;
            case Kind.SemicolonClassElement:
                EmitSemicolonClassElement(node.AsSemicolonClassElement()!);
                break;

            // Declarations (non-statement)
            case Kind.VariableDeclaration:
                EmitVariableDeclaration(node.AsVariableDeclaration()!);
                break;
            case Kind.VariableDeclarationList:
                EmitVariableDeclarationList(node.AsVariableDeclarationList()!);
                break;
            case Kind.ModuleBlock:
                EmitModuleBlock(node.AsModuleBlock()!);
                break;
            case Kind.CaseBlock:
                EmitCaseBlock(node.AsCaseBlock()!);
                break;
            case Kind.ImportClause:
                EmitImportClause(node.AsImportClause()!);
                break;
            case Kind.NamespaceImport:
                EmitNamespaceImport(node.AsNamespaceImport()!);
                break;
            case Kind.NamespaceExport:
                EmitNamespaceExport(node.AsNamespaceExport()!);
                break;
            case Kind.NamedImports:
                EmitNamedImports(node.AsNamedImports()!);
                break;
            case Kind.ImportSpecifier:
                EmitImportSpecifier(node.AsImportSpecifier()!);
                break;
            case Kind.NamedExports:
                EmitNamedExports(node.AsNamedExports()!);
                break;
            case Kind.ExportSpecifier:
                EmitExportSpecifier(node.AsExportSpecifier()!);
                break;
            case Kind.ImportAttributes:
                EmitImportAttributes(node.AsImportAttributes()!);
                break;
            case Kind.ImportAttribute:
                EmitImportAttribute(node.AsImportAttribute()!);
                break;

            // Module references
            case Kind.ExternalModuleReference:
                EmitExternalModuleReference(node.AsExternalModuleReference()!);
                break;

            // JSX (non-expression)
            case Kind.JsxText:
                EmitJsxText(node.AsJsxText()!);
                break;
            case Kind.JsxOpeningElement:
                EmitJsxOpeningElement(node.AsJsxOpeningElement()!);
                break;
            case Kind.JsxOpeningFragment:
                EmitJsxOpeningFragment(node.AsJsxOpeningFragment()!);
                break;
            case Kind.JsxClosingElement:
                EmitJsxClosingElement(node.AsJsxClosingElement()!);
                break;
            case Kind.JsxClosingFragment:
                EmitJsxClosingFragment(node.AsJsxClosingFragment()!);
                break;
            case Kind.JsxAttribute:
                EmitJsxAttribute(node.AsJsxAttribute()!);
                break;
            case Kind.JsxAttributes:
                EmitJsxAttributes(node.AsJsxAttributes()!);
                break;
            case Kind.JsxSpreadAttribute:
                EmitJsxSpreadAttribute(node.AsJsxSpreadAttribute()!);
                break;
            case Kind.JsxExpression:
                EmitJsxExpression(node.AsJsxExpression()!);
                break;
            case Kind.JsxNamespacedName:
                EmitJsxNamespacedName(node.AsJsxNamespacedName()!);
                break;

            // Clauses
            case Kind.CaseClause:
                EmitCaseClause(node.AsCaseOrDefaultClause()!);
                break;
            case Kind.DefaultClause:
                EmitDefaultClause(node.AsCaseOrDefaultClause()!);
                break;
            case Kind.HeritageClause:
                EmitHeritageClause(node.AsHeritageClause()!);
                break;
            case Kind.CatchClause:
                EmitCatchClause(node.AsCatchClause()!);
                break;

            // Property assignments
            case Kind.PropertyAssignment:
                EmitPropertyAssignment(node.AsPropertyAssignment()!);
                break;
            case Kind.ShorthandPropertyAssignment:
                EmitShorthandPropertyAssignment(node.AsShorthandPropertyAssignment()!);
                break;
            case Kind.SpreadAssignment:
                EmitSpreadAssignment(node.AsSpreadAssignment()!);
                break;

            // Enum
            case Kind.EnumMember:
                EmitEnumMember(node.AsEnumMember()!);
                break;

            // Top-level nodes
            case Kind.SourceFile:
                // Go: emitSourceFile（未导出，写进 writer）；C# 因「返回类型不参与重载」而改名 EmitSourceFileWorker
                EmitSourceFileWorker(node.AsSourceFile()!);
                break;

            // Transformation nodes
            case Kind.NotEmittedTypeElement:
                EmitNotEmittedTypeElement(node.AsNotEmittedTypeElement()!);
                break;

            default:
                if (IsTypeNode(node))
                {
                    EmitTypeNodeOutsideExtends(node);
                }
                else if (IsStatement(node))
                {
                    EmitStatement(node);
                }
                else if (IsExpression(node))
                {
                    EmitExpression(node, OperatorPrecedenceLowest);
                }
                else if (IsKeywordKind(node.Kind))
                {
                    EmitKeywordNode(node);
                }
                else if (IsPunctuationKind(node.Kind))
                {
                    EmitPunctuationNode(node);
                }
                else if (IsJSDocKind(node.Kind))
                {
                    EmitJSDocNode(node);
                }
                else
                {
                    Debug.Fail($"unhandled Node: {node.Kind.String()}");
                }
                break;
        }

        _currentSourceFile = savedCurrentSourceFile;
        _writer = savedWriter;
        _uniqueHelperNames = savedUniqueHelperNames;
        _sourceMapsDisabled = savedSourceMapsDisabled;
        _sourceMapGenerator = savedSourceMapGenerator;
        _sourceMapSource = savedSourceMapSource;
        _sourceMapSourceIndex = savedSourceMapSourceIndex;
        _sourceMapLineCharCache = savedSourceMapLineCharCache;
    }

    //
    // Scoped operations
    //

    internal PrinterState EnterNode(Node node)
    {
        var state = new PrinterState();

        OnBeforeEmitNode?.Invoke(node);

        state.CommentState = EmitCommentsBeforeNode(node);
        state.SourceMapState = EmitSourceMapsBeforeNode(node);
        return state;
    }

    internal void ExitNode(Node node, PrinterState previousState)
    {
        EmitSourceMapsAfterNode(node, previousState.SourceMapState);
        EmitCommentsAfterNode(node, previousState.CommentState);

        OnAfterEmitNode?.Invoke(node);
    }

    internal PrinterState EnterTokenNode(Node node, TokenEmitFlags flags)
    {
        var state = new PrinterState();

        OnBeforeEmitToken?.Invoke(node);

        if ((flags & TokenEmitFlags.NoComments) == 0)
        {
            state.CommentState = EmitCommentsBeforeNode(node);
        }
        if ((flags & TokenEmitFlags.NoSourceMaps) == 0)
        {
            state.SourceMapState = EmitSourceMapsBeforeNode(node);
        }
        return state;
    }

    internal void ExitTokenNode(Node node, PrinterState previousState)
    {
        EmitSourceMapsAfterNode(node, previousState.SourceMapState);
        EmitCommentsAfterNode(node, previousState.CommentState);

        OnAfterEmitToken?.Invoke(node);
    }

    [Flags]
    internal enum TokenEmitFlags : uint
    {
        NoComments = 1u << 0,
        IndentLeadingComments = 1u << 1,
        NoSourceMaps = 1u << 2,

        None = 0,
    }

    internal (PrinterState State, int Pos) EnterToken(Kind token, int pos, Node contextNode, TokenEmitFlags flags)
    {
        var state = new PrinterState();
        var newPos = pos;
        state.CommentState = EmitCommentsBeforeToken(token, newPos, contextNode, flags, out newPos);
        state.SourceMapState = EmitSourceMapsBeforeToken(token, newPos, contextNode, flags);
        return (state, newPos);
    }

    internal void ExitToken(Kind token, int pos, Node contextNode, PrinterState previousState)
    {
        EmitSourceMapsAfterToken(token, pos, contextNode, previousState.SourceMapState);
        EmitCommentsAfterToken(token, pos, contextNode, previousState.CommentState);
    }
}

[Flags]
public enum ListFormat
{
    None = 0,

    // Line separators
    SingleLine = 0,    // Prints the list on a single line (default).
    MultiLine = 1 << 0, // Prints the list on multiple lines.
    PreserveLines = 1 << 1, // Prints the list using line preservation if possible.
    LinesMask = SingleLine | MultiLine | PreserveLines,

    // Delimiters
    NotDelimited = 0,    // There is no delimiter between list items (default).
    BarDelimited = 1 << 2, // Each list item is space-and-bar (" |") delimited.
    AmpersandDelimited = 1 << 3, // Each list item is space-and-ampersand (" &") delimited.
    CommaDelimited = 1 << 4, // Each list item is comma (",") delimited.
    AsteriskDelimited = 1 << 5, // Each list item is asterisk ("\n *") delimited, used with JSDoc.
    DelimitersMask = BarDelimited | AmpersandDelimited | CommaDelimited | AsteriskDelimited,

    AllowTrailingComma = 1 << 6, // Write a trailing comma (",") if present.

    // Whitespace
    Indented = 1 << 7, // The list should be indented.
    SpaceBetweenBraces = 1 << 8, // Inserts a space after the opening brace and before the closing brace.
    SpaceBetweenSiblings = 1 << 9, // Inserts a space between each sibling node.

    // Brackets/Braces
    Braces = 1 << 10, // The list is surrounded by "{" and "}".
    Parenthesis = 1 << 11, // The list is surrounded by "(" and ")".
    AngleBrackets = 1 << 12, // The list is surrounded by "<" and ">".
    SquareBrackets = 1 << 13, // The list is surrounded by "[" and "]".
    BracketsMask = Braces | Parenthesis | AngleBrackets | SquareBrackets,

    OptionalIfNil = 1 << 14, // Do not emit brackets if the list is nil.
    OptionalIfEmpty = 1 << 15, // Do not emit brackets if the list is empty.
    Optional = OptionalIfNil | OptionalIfEmpty,

    // Other
    PreferNewLine = 1 << 16, // Prefer adding a LineTerminator between synthesized nodes.
    NoTrailingNewLine = 1 << 17, // Do not emit a trailing NewLine for a MultiLine list.
    NoInterveningComments = 1 << 18, // Do not emit comments between each node
    NoSpaceIfEmpty = 1 << 19, // If the literal is empty, do not add spaces between braces.
    SingleElement = 1 << 20,
    SpaceAfterList = 1 << 21, // Add space after list

    // Precomputed Formats
    Modifiers = SingleLine | SpaceBetweenSiblings | NoInterveningComments | SpaceAfterList,
    HeritageClauses = SingleLine | SpaceBetweenSiblings,
    SingleLineTypeLiteralMembers = SingleLine | SpaceBetweenBraces | SpaceBetweenSiblings,
    MultiLineTypeLiteralMembers = MultiLine | Indented | OptionalIfEmpty,

    SingleLineTupleTypeElements = CommaDelimited | SpaceBetweenSiblings | SingleLine,
    MultiLineTupleTypeElements = CommaDelimited | Indented | SpaceBetweenSiblings | MultiLine,
    UnionTypeConstituents = BarDelimited | SpaceBetweenSiblings | SingleLine,
    IntersectionTypeConstituents = AmpersandDelimited | SpaceBetweenSiblings | SingleLine,
    ObjectBindingPatternElements = SingleLine | AllowTrailingComma | SpaceBetweenBraces | CommaDelimited | SpaceBetweenSiblings | NoSpaceIfEmpty,
    ArrayBindingPatternElements = SingleLine | AllowTrailingComma | CommaDelimited | SpaceBetweenSiblings | NoSpaceIfEmpty,
    ObjectLiteralExpressionProperties = PreserveLines | CommaDelimited | SpaceBetweenSiblings | SpaceBetweenBraces | Indented | Braces | NoSpaceIfEmpty,
    ImportAttributes = PreserveLines | CommaDelimited | SpaceBetweenSiblings | SpaceBetweenBraces | Indented | Braces | NoSpaceIfEmpty,
    ArrayLiteralExpressionElements = PreserveLines | CommaDelimited | SpaceBetweenSiblings | AllowTrailingComma | Indented | SquareBrackets,
    CommaListElements = CommaDelimited | SpaceBetweenSiblings | SingleLine,
    CallExpressionArguments = CommaDelimited | SpaceBetweenSiblings | SingleLine | Parenthesis,
    NewExpressionArguments = CommaDelimited | SpaceBetweenSiblings | SingleLine | Parenthesis | OptionalIfNil,
    TemplateExpressionSpans = SingleLine | NoInterveningComments,
    SingleLineBlockStatements = SpaceBetweenBraces | SpaceBetweenSiblings | SingleLine,
    MultiLineBlockStatements = Indented | MultiLine,
    VariableDeclarationList = CommaDelimited | SpaceBetweenSiblings | SingleLine,
    SingleLineFunctionBodyStatements = SingleLine | SpaceBetweenSiblings | SpaceBetweenBraces,
    MultiLineFunctionBodyStatements = MultiLine,
    ClassHeritageClauses = SingleLine,
    ClassMembers = Indented | MultiLine,
    InterfaceMembers = Indented | MultiLine,
    EnumMembers = CommaDelimited | Indented | MultiLine,
    CaseBlockClauses = Indented | MultiLine,
    NamedImportsOrExportsElements = CommaDelimited | SpaceBetweenSiblings | AllowTrailingComma | SingleLine | SpaceBetweenBraces | NoSpaceIfEmpty,
    JsxElementOrFragmentChildren = SingleLine | NoInterveningComments,
    JsxElementAttributes = SingleLine | SpaceBetweenSiblings | NoInterveningComments,
    CaseOrDefaultClauseStatements = Indented | MultiLine | NoTrailingNewLine | OptionalIfEmpty,
    HeritageClauseTypes = CommaDelimited | SpaceBetweenSiblings | SingleLine,
    SourceFileStatements = MultiLine | NoTrailingNewLine,
    Decorators = MultiLine | Optional | SpaceAfterList,
    TypeArguments = CommaDelimited | SpaceBetweenSiblings | SingleLine | AngleBrackets | Optional,
    TypeParameters = CommaDelimited | SpaceBetweenSiblings | SingleLine | AngleBrackets | Optional,
    Parameters = CommaDelimited | SpaceBetweenSiblings | SingleLine | Parenthesis,
    SingleArrowParameter = CommaDelimited | SpaceBetweenSiblings | SingleLine,
    IndexSignatureParameters = CommaDelimited | SpaceBetweenSiblings | SingleLine | Indented | SquareBrackets,
    JSDocComment = MultiLine | AsteriskDelimited,
    ImportClauseEntries = ImportAttributes, // Deprecated: Use LFImportAttributes
}

public sealed partial class Printer
{
    internal static string GetOpeningBracket(ListFormat format)
    {
        switch (format & ListFormat.BracketsMask)
        {
            case ListFormat.Braces:
                return "{";
            case ListFormat.Parenthesis:
                return "(";
            case ListFormat.AngleBrackets:
                return "<";
            case ListFormat.SquareBrackets:
                return "[";
            default:
                Debug.Fail($"Unexpected bracket: {(int)(format & ListFormat.BracketsMask)}");
                return "";
        }
    }

    internal static string GetClosingBracket(ListFormat format)
    {
        switch (format & ListFormat.BracketsMask)
        {
            case ListFormat.Braces:
                return "}";
            case ListFormat.Parenthesis:
                return ")";
            case ListFormat.AngleBrackets:
                return ">";
            case ListFormat.SquareBrackets:
                return "]";
            default:
                Debug.Fail($"Unexpected bracket: {(int)(format & ListFormat.BracketsMask)}");
                return "";
        }
    }
}

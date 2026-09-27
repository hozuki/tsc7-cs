// ported from: internal/parser/parser.go:2142-3533（类型别名/枚举/模块/import-export 声明/类型/类型参数/参数/访问器/属性名）
//
// 与 Go 的偏离（逐条；其余地方与 Go 逐行对应）：
// 1) 位置一律是 UTF-16 代码单元下标（docs/architecture.md §3.1）：本文件所有 pos/end/Loc 都直接来自
//    scanner/parser 的 pos，与 Go 的"字节下标"写法在结构上完全一致（scanner 侧已按代码单元给出位置）。
// 2) 回调/函数值：Go 的 `func(p *Parser) bool` → `Func<Parser, bool>`；`func(p *Parser) *ast.Node` 按"能否返回 nil"
//    分两种落法——`ParseList` 的 `Func<Parser, Node>`（元素解析器永不返回 nil）与 `ParseDelimitedList` /
//    `ParseBracketedList` / `ParseFunctionOrConstructorTypeToError` 的 **`Func<Parser, Node?>`**（Go 里元素解析器
//    返回 nil 表示"失败 → 整个列表返回 nil"，见 parser.go:650 的注释；本文件 parseParametersWorker 的闭包就走这条）。
//    **调用点一律写 lambda**（`LookAhead(p => p.NextIsNotDot())`、`DoInContext(..., p => p.ParseType())`）：C# 的
//    实例方法组会绑定到 `this`，参数个数对不上 `Func<Parser, T>`，方法组转换会报 CS1503（Go 的 `(*Parser).nextIsNotDot`
//    这种"接收者显式"的方法表达式在 C# 里没有对应物）。
// 3) Go 的多返回值：`parseModuleExportName(disallowKeywords bool) (node *ast.Node, nameOk bool)` 落成
//    `bool TryParseModuleExportName(bool disallowKeywords, out Node node)`（docs §3.2 的"查找失败用 TryX"约定）；
//    `parseImportOrExportSpecifier` 的三返回值落成命名元组 `(bool IsTypeOnly, Node? PropertyName, Node Name)`
//    （与 Scanner.CharAndSize / Core.Semaphore.TryAcquire 的元组写法一致）。
// 4) Go 的 `fallthrough` → C# 的 `goto case`（parseNonArrayType 的 `*=` 与 `??` 两处）。
// 5) `ast.IsXxx(...)` 的调用点写裸名（`using static TypeScript.Ast.Ast;`，理由见 Scanner/Utilities.cs 文件头第 9 条：
//    在 `TypeScript.Parser` 下裸名 `Ast` 会先解析成外层命名空间 `TypeScript.Ast`，实测 CS0234）。
//    `scanner.TokenToString` 同理走 `using static TypeScript.Scanner.Scanner;`；诊断消息走
//    `using static TypeScript.Diagnostics.Diagnostics;`，`core.NewTextRange` 走 `using static TypeScript.Core.Core;`。
// 6) Go 的 `p.diagnostics[len(p.diagnostics)-1]` → `_diagnostics[^1]`；`nodeSliceArena.Clone(types)` 的
//    `types` 在 Go 里是 `make([]*ast.Node, 1, 8)` + append → C# `new List<Node>(8)`（C# 数组没有 len<cap 视图，§3.2），
//    克隆时用 `CollectionsMarshal.AsSpan` 保持零拷贝（§3.8）。
// 7) Go 的 `p.scanner.Xxx()` / `p.factory.Xxx()` / `p.token` / `p.contextFlags`… → 字段 `_camelCase`
//    （Parser 的字段声明在 W1 的 Parser.cs，见 docs/architecture.md §2）；`p.jsdocScannerInfo()` 按 W1 契约落成
//    **属性** `JsdocScannerInfo`（同名方法会遮蔽类型名 CS0119，先例见 docs/ast.md 的 SubtreeFacts）。
// 8) `*ast.TypeNode` / `*ast.Expression` / `*ast.Statement` 等都是 `type X = Node` 别名 → C# 直接用 `Node`
//    （docs/ast.md §2：kind 别名不生成类型）。返回类型一律写 Go 里"永不返回 nil"的那些（`Node`），只有确实可空的
//    写 `Node?`（如 `TryParseConstraintOfInferType`/`ParseReturnType`/`ParseTypeArguments`/`ParseParameterEx`）。
// 9) `parseUnionOrIntersectionType` 的 `typeNode` 在 Go 里是 `*ast.TypeNode`（nil 会进 NodeList），C# 用 `Node?` +
//    三处 `!`：唯一可能为 nil 的来源是 `parseFunctionOrConstructorTypeToError` 直接返回 `parseConstituentType(p)`，
//    而两个调用点传的 constituent 解析器（parseIntersectionTypeOrHigher / parseTypeOperatorOrHigher）永不返回 nil
//    （与 Parser.Expressions.cs 里 `ParseLeftHandSideExpressionOrHigher()!` 同一性质）。

using System.Runtime.InteropServices;
using TypeScript.Ast;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Parser;

public sealed partial class Parser
{
    /// <summary>Go: <c>func (p *Parser) parseTypeAliasDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList) *ast.Node</c>（Go:2142-2161）。</summary>
    private Node ParseTypeAliasDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        ParseExpected(Kind.TypeKeyword);
        if (HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Line_break_not_permitted_here);
        }
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        ParseExpected(Kind.EqualsToken);
        Node? typeNode;
        if (_token == Kind.IntrinsicKeyword && LookAhead(p => p.NextIsNotDot()))
        {
            typeNode = ParseKeywordTypeNode();
        }
        else
        {
            typeNode = ParseType();
        }
        ParseSemicolon();
        var result = FinishNode(_factory.NewTypeAliasDeclaration(modifiers, name, typeParameters, typeNode), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) nextIsNotDot() bool</c>（Go:2163-2165）。</summary>
    private bool NextIsNotDot() => NextToken() != Kind.DotToken;

    // In an ambient declaration, the grammar only allows integer literals as initializers.
    // In a non-ambient declaration, the grammar allows uninitialized members only in a
    // ConstantEnumMemberSection, which starts at the beginning of an enum declaration
    // or any time an integer literal initializer is encountered.
    /// <summary>Go: <c>func (p *Parser) parseEnumMember() *ast.Node</c>（Go:2171-2179）。</summary>
    private Node ParseEnumMember()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var name = ParsePropertyName();
        var initializer = DoInContext(NodeFlags.DisallowInContext, false, p => p.ParseInitializer());
        var result = FinishNode(_factory.NewEnumMember(name, initializer), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseEnumDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList) *ast.Node</c>（Go:2181-2200）。</summary>
    private Node ParseEnumDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        ParseExpected(Kind.EnumKeyword);
        var name = ParseIdentifier();
        NodeList? members;
        if (ParseExpected(Kind.OpenBraceToken))
        {
            var saveContextFlags = _contextFlags;
            SetContextFlags(NodeFlags.YieldContext | NodeFlags.AwaitContext, false);
            members = ParseDelimitedList(ParsingContext.EnumMembers, p => p.ParseEnumMember());
            _contextFlags = saveContextFlags;
            ParseExpected(Kind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList();
        }
        var result = FinishNode(_factory.NewEnumDeclaration(modifiers, name, members), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseModuleDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList) *ast.Statement</c>（Go:2202-2216）。</summary>
    private Node ParseModuleDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        var keyword = Kind.ModuleKeyword;
        if (_token == Kind.GlobalKeyword)
        {
            // global augmentation
            return ParseAmbientExternalModuleDeclaration(pos, jsdoc, modifiers);
        }
        else if (ParseOptional(Kind.NamespaceKeyword))
        {
            keyword = Kind.NamespaceKeyword;
        }
        else
        {
            ParseExpected(Kind.ModuleKeyword);
            if (_token == Kind.StringLiteral)
            {
                return ParseAmbientExternalModuleDeclaration(pos, jsdoc, modifiers);
            }
        }
        return ParseModuleOrNamespaceDeclaration(pos, jsdoc, modifiers, false /*nested*/, keyword);
    }

    /// <summary>Go: <c>func (p *Parser) parseAmbientExternalModuleDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList) *ast.Node</c>（Go:2218-2244）。</summary>
    private Node ParseAmbientExternalModuleDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        Node? name;
        var keyword = Kind.ModuleKeyword;
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        if (_token == Kind.GlobalKeyword)
        {
            // parse 'global' as name of global scope augmentation
            name = ParseIdentifier();
            keyword = Kind.GlobalKeyword;
        }
        else
        {
            // parse string literal
            name = ParseLiteralExpression();
        }
        Node? attributes = null;
        if (keyword == Kind.ModuleKeyword && ParseOptional(Kind.WithKeyword))
        {
            attributes = ParseTypeLiteral();
        }
        Node? body = null;
        if (_token == Kind.OpenBraceToken)
        {
            body = ParseModuleBlock();
        }
        else
        {
            ParseSemicolon();
        }
        var result = FinishNode(_factory.NewModuleDeclaration(modifiers, keyword, name, attributes, body), pos);
        WithJSDoc(result, jsdoc);
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseModuleBlock() *ast.Node</c>（Go:2246-2256）。</summary>
    private Node ParseModuleBlock()
    {
        var pos = NodePos();
        NodeList? statements;
        if (ParseExpected(Kind.OpenBraceToken))
        {
            statements = ParseList(ParsingContext.BlockStatements, p => p.ParseStatement());
            ParseExpected(Kind.CloseBraceToken);
        }
        else
        {
            statements = CreateMissingList();
        }
        return FinishNode(_factory.NewModuleBlock(statements), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseModuleOrNamespaceDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList, nested bool, keyword ast.Kind) *ast.Node</c>（Go:2258-2281）。</summary>
    private Node ParseModuleOrNamespaceDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers, bool nested, Kind keyword)
    {
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        Node? name;
        if (nested)
        {
            name = ParseIdentifierName();
        }
        else
        {
            name = ParseIdentifier();
        }
        Node? body;
        if (ParseOptional(Kind.DotToken))
        {
            var implicitExport = _factory.NewModifier(Kind.ExportKeyword);
            implicitExport.Loc = NewTextRange(NodePos(), NodePos());
            implicitExport.Flags = NodeFlags.Reparsed;
            var implicitModifiers = NewModifierList(implicitExport.Loc, _nodeSliceArena.NewSlice1(implicitExport));
            body = ParseModuleOrNamespaceDeclaration(NodePos(), default /*jsdoc*/, implicitModifiers, true /*nested*/, keyword);
        }
        else
        {
            body = ParseModuleBlock();
        }
        var result = FinishNode(_factory.NewModuleDeclaration(modifiers, keyword, name, null, body), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseImportDeclarationOrImportEqualsDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList) *ast.Statement</c>（Go:2283-2330）。</summary>
    private Node ParseImportDeclarationOrImportEqualsDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        ParseExpected(Kind.ImportKeyword);
        var afterImportPos = NodePos();
        // We don't parse the identifier here in await context, instead we will report a grammar error in the checker.
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        Node? identifier = null;
        if (IsIdentifier())
        {
            identifier = ParseIdentifier();
        }
        var phaseModifier = Kind.Unknown;
        if (identifier is not null && identifier.Text() == "type" &&
            (_token != Kind.FromKeyword || IsIdentifier() && LookAhead(p => p.NextTokenIsFromKeywordOrEqualsToken())) &&
            (IsIdentifier() || TokenAfterImportDefinitelyProducesImportDeclaration()))
        {
            phaseModifier = Kind.TypeKeyword;
            identifier = null;
            if (IsIdentifier())
            {
                identifier = ParseIdentifier();
            }
        }
        else if (identifier is not null && identifier.Text() == "defer")
        {
            bool shouldParseAsDeferModifier;
            if (_token == Kind.FromKeyword)
            {
                shouldParseAsDeferModifier = !LookAhead(p => p.NextTokenIsTokenStringLiteral());
            }
            else
            {
                shouldParseAsDeferModifier = _token != Kind.CommaToken && _token != Kind.EqualsToken;
            }
            if (shouldParseAsDeferModifier)
            {
                phaseModifier = Kind.DeferKeyword;
                identifier = null;
                if (IsIdentifier())
                {
                    identifier = ParseIdentifier();
                }
            }
        }
        if (identifier is not null && !TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration() && phaseModifier != Kind.DeferKeyword)
        {
            var importEquals = CheckJSSyntax(ParseImportEqualsDeclaration(pos, jsdoc, modifiers, identifier, phaseModifier == Kind.TypeKeyword));
            _statementHasAwaitIdentifier = saveHasAwaitIdentifier; // Import= declaration is always parsed in an Await context, no need to reparse
            return importEquals;
        }
        var importClause = TryParseImportClause(identifier, afterImportPos, phaseModifier, false /*skipJSDocLeadingAsterisks*/);
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier; // import clause is always parsed in an Await context
        var moduleSpecifier = ParseModuleSpecifier();
        var attributes = TryParseImportAttributes();
        ParseSemicolon();
        var result = FinishNode(_factory.NewImportDeclaration(modifiers, importClause, moduleSpecifier, attributes), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) nextTokenIsFromKeywordOrEqualsToken() bool</c>（Go:2332-2335）。</summary>
    private bool NextTokenIsFromKeywordOrEqualsToken()
    {
        NextToken();
        return _token == Kind.FromKeyword || _token == Kind.EqualsToken;
    }

    /// <summary>Go: <c>func (p *Parser) tokenAfterImportDefinitelyProducesImportDeclaration() bool</c>（Go:2337-2339）。</summary>
    private bool TokenAfterImportDefinitelyProducesImportDeclaration() =>
        _token == Kind.AsteriskToken || _token == Kind.OpenBraceToken;

    /// <summary>Go: <c>func (p *Parser) tokenAfterImportedIdentifierDefinitelyProducesImportDeclaration() bool</c>（Go:2341-2345）。</summary>
    private bool TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration() =>
        // In `import id ___`, the current token decides whether to produce
        // an ImportDeclaration or ImportEqualsDeclaration.
        _token == Kind.CommaToken || _token == Kind.FromKeyword;

    /// <summary>Go: <c>func (p *Parser) parseImportEqualsDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList, identifier *ast.Node, isTypeOnly bool) *ast.Node</c>（Go:2347-2354）。</summary>
    private Node ParseImportEqualsDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers, Node identifier, bool isTypeOnly)
    {
        ParseExpected(Kind.EqualsToken);
        var moduleReference = ParseModuleReference();
        ParseSemicolon();
        var result = FinishNode(_factory.NewImportEqualsDeclaration(modifiers, isTypeOnly, identifier, moduleReference), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseModuleReference() *ast.Node</c>（Go:2356-2361）。</summary>
    private Node ParseModuleReference()
    {
        if (_token == Kind.RequireKeyword && LookAhead(p => p.NextTokenIsOpenParen()))
        {
            return ParseExternalModuleReference();
        }
        return ParseEntityName(false /*allowReservedWords*/, false /*allowPrivateName*/, null /*diagnosticMessage*/);
    }

    /// <summary>Go: <c>func (p *Parser) parseExternalModuleReference() *ast.Node</c>（Go:2363-2373）。</summary>
    private Node ParseExternalModuleReference()
    {
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        var pos = NodePos();
        ParseExpected(Kind.RequireKeyword);
        ParseExpected(Kind.OpenParenToken);
        var expression = ParseModuleSpecifier();
        ParseExpected(Kind.CloseParenToken);
        var result = FinishNode(_factory.NewExternalModuleReference(expression), pos);
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseModuleSpecifier() *ast.Expression</c>（Go:2375-2383）。</summary>
    private Node ParseModuleSpecifier()
    {
        if (_token == Kind.StringLiteral)
        {
            return ParseLiteralExpression();
        }
        // We allow arbitrary expressions here, even though the grammar only allows string
        // literals.  We check to ensure that it is only a string literal later in the grammar
        // check pass.
        return ParseExpression();
    }

    /// <summary>Go: <c>func (p *Parser) tryParseImportClause(identifier *ast.Node, pos int, phaseModifier ast.Kind, skipJSDocLeadingAsterisks bool) *ast.Node</c>（Go:2385-2395）。</summary>
    private Node? TryParseImportClause(Node? identifier, int pos, Kind phaseModifier, bool skipJSDocLeadingAsterisks)
    {
        // ImportDeclaration:
        //  import ImportClause from ModuleSpecifier ;
        //  import ModuleSpecifier;
        if (identifier is not null || _token == Kind.AsteriskToken || _token == Kind.OpenBraceToken)
        {
            var importClause = ParseImportClause(identifier, pos, phaseModifier, skipJSDocLeadingAsterisks);
            ParseExpected(Kind.FromKeyword);
            return importClause;
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Parser) parseImportClause(identifier *ast.Node, pos int, phaseModifier ast.Kind, skipJSDocLeadingAsterisks bool) *ast.Node</c>（Go:2397-2424）。</summary>
    private Node ParseImportClause(Node? identifier, int pos, Kind phaseModifier, bool skipJSDocLeadingAsterisks)
    {
        // ImportClause:
        //  ImportedDefaultBinding
        //  NameSpaceImport
        //  NamedImports
        //  ImportedDefaultBinding, NameSpaceImport
        //  ImportedDefaultBinding, NamedImports
        // If there was no default import or if there is comma token after default import
        // parse namespace or named imports
        Node? namedBindings = null;
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        if (identifier is null || ParseOptional(Kind.CommaToken))
        {
            if (skipJSDocLeadingAsterisks)
            {
                _scanner.SetSkipJSDocLeadingAsterisks(true);
            }
            if (_token == Kind.AsteriskToken)
            {
                namedBindings = ParseNamespaceImport();
            }
            else
            {
                namedBindings = ParseNamedImports();
            }
            if (skipJSDocLeadingAsterisks)
            {
                _scanner.SetSkipJSDocLeadingAsterisks(false);
            }
        }
        var result = FinishNode(_factory.NewImportClause(phaseModifier, identifier, namedBindings), pos);
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseNamespaceImport() *ast.Node</c>（Go:2426-2434）。</summary>
    private Node ParseNamespaceImport()
    {
        // NameSpaceImport:
        //  * as ImportedBinding
        var pos = NodePos();
        ParseExpected(Kind.AsteriskToken);
        ParseExpected(Kind.AsKeyword);
        var name = ParseIdentifier();
        return FinishNode(_factory.NewNamespaceImport(name), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseNamedImports() *ast.Node</c>（Go:2436-2444）。</summary>
    private Node ParseNamedImports()
    {
        var pos = NodePos();
        // NamedImports:
        //  { }
        //  { ImportsList }
        //  { ImportsList, }
        var imports = ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, p => p.ParseImportSpecifier(), Kind.OpenBraceToken, Kind.CloseBraceToken);
        return FinishNode(_factory.NewNamedImports(imports), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseImportSpecifier() *ast.Node</c>（Go:2446-2459）。</summary>
    private Node ParseImportSpecifier()
    {
        var pos = NodePos();
        var (isTypeOnly, propertyName, name) = ParseImportOrExportSpecifier(Kind.ImportSpecifier);
        Node identifierName;
        if (name.Kind == Kind.Identifier)
        {
            identifierName = name;
        }
        else
        {
            ParseErrorAtRange(SkipRangeTrivia(name.Loc), Identifier_expected);
            identifierName = NewIdentifier("");
            FinishNode(identifierName, name.Pos());
        }
        return CheckJSSyntax(FinishNode(_factory.NewImportSpecifier(isTypeOnly, propertyName, identifierName), pos));
    }

    /// <summary>Go: <c>func (p *Parser) parseImportOrExportSpecifier(kind ast.Kind) (isTypeOnly bool, propertyName *ast.Node, name *ast.Node)</c>（Go:2461-2531）。</summary>
    private (bool IsTypeOnly, Node? PropertyName, Node Name) ParseImportOrExportSpecifier(Kind kind)
    {
        // ImportSpecifier:
        //   BindingIdentifier
        //   ModuleExportName as BindingIdentifier
        // ExportSpecifier:
        //   ModuleExportName
        //   ModuleExportName as ModuleExportName
        var canParseAsKeyword = true;
        var disallowKeywords = kind == Kind.ImportSpecifier;
        var isTypeOnly = false;
        Node? propertyName = null;
        var nameOk = TryParseModuleExportName(disallowKeywords, out var name);
        if (name.Kind == Kind.Identifier && name.Text() == "type")
        {
            // If the first token of an import specifier is 'type', there are a lot of possibilities,
            // especially if we see 'as' afterwards:
            //
            // import { type } from "mod";          - isTypeOnly: false,   name: type
            // import { type as } from "mod";       - isTypeOnly: true,    name: as
            // import { type as as } from "mod";    - isTypeOnly: false,   name: as,    propertyName: type
            // import { type as as as } from "mod"; - isTypeOnly: true,    name: as,    propertyName: as
            if (_token == Kind.AsKeyword)
            {
                // { type as ...? }
                var firstAs = ParseIdentifierName();
                if (_token == Kind.AsKeyword)
                {
                    // { type as as ...? }
                    var secondAs = ParseIdentifierName();
                    if (CanParseModuleExportName())
                    {
                        // { type as as something }
                        // { type as as "something" }
                        isTypeOnly = true;
                        propertyName = firstAs;
                        nameOk = TryParseModuleExportName(disallowKeywords, out name);
                        canParseAsKeyword = false;
                    }
                    else
                    {
                        // { type as as }
                        propertyName = name;
                        name = secondAs;
                        canParseAsKeyword = false;
                    }
                }
                else if (CanParseModuleExportName())
                {
                    // { type as something }
                    // { type as "something" }
                    propertyName = name;
                    canParseAsKeyword = false;
                    nameOk = TryParseModuleExportName(disallowKeywords, out name);
                }
                else
                {
                    // { type as }
                    isTypeOnly = true;
                    name = firstAs;
                }
            }
            else if (CanParseModuleExportName())
            {
                // { type something ...? }
                // { type "something" ...? }
                isTypeOnly = true;
                nameOk = TryParseModuleExportName(disallowKeywords, out name);
            }
        }
        if (canParseAsKeyword && _token == Kind.AsKeyword)
        {
            propertyName = name;
            ParseExpected(Kind.AsKeyword);
            nameOk = TryParseModuleExportName(disallowKeywords, out name);
        }

        if (!nameOk)
        {
            ParseErrorAtRange(SkipRangeTrivia(name.Loc), Identifier_expected);
        }

        return (isTypeOnly, propertyName, name);
    }

    /// <summary>Go: <c>func (p *Parser) canParseModuleExportName() bool</c>（Go:2533-2535）。</summary>
    private bool CanParseModuleExportName() => TokenIsIdentifierOrKeyword(_token) || _token == Kind.StringLiteral;

    /// <summary>Go: <c>func (p *Parser) parseModuleExportName(disallowKeywords bool) (node *ast.Node, nameOk bool)</c>（Go:2537-2547）。</summary>
    private bool TryParseModuleExportName(bool disallowKeywords, out Node node)
    {
        var nameOk = true;

        if (_token == Kind.StringLiteral)
        {
            node = ParseLiteralExpression();
            return nameOk;
        }
        if (disallowKeywords && IsKeyword(_token) && !IsIdentifier())
        {
            nameOk = false;
        }
        node = ParseIdentifierName();
        return nameOk;
    }

    /// <summary>Go: <c>func (p *Parser) tryParseImportAttributes() *ast.Node</c>（Go:2549-2557）。</summary>
    private Node? TryParseImportAttributes()
    {
        if (_token == Kind.WithKeyword || (_token == Kind.AssertKeyword && !HasPrecedingLineBreak()))
        {
            if (_token == Kind.AssertKeyword)
            {
                ParseErrorAtCurrentToken(Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
            }
            return ParseImportAttributes(_token, false /*skipKeyword*/);
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Parser) parseExportAssignment(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList) *ast.Node</c>（Go:2559-2577）。</summary>
    private Node ParseExportAssignment(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        var saveContextFlags = _contextFlags;
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        SetContextFlags(NodeFlags.AwaitContext, true);
        var isExportEquals = false;
        if (ParseOptional(Kind.EqualsToken))
        {
            isExportEquals = true;
        }
        else
        {
            ParseExpected(Kind.DefaultKeyword);
        }
        var expression = ParseAssignmentExpressionOrHigher();
        ParseSemicolon();
        _contextFlags = saveContextFlags;
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        var result = FinishNode(_factory.NewExportAssignment(modifiers, isExportEquals, null /*typeNode*/, expression), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseNamespaceExportDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList) *ast.Node</c>（Go:2579-2590）。</summary>
    private Node ParseNamespaceExportDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        ParseExpected(Kind.AsKeyword);
        ParseExpected(Kind.NamespaceKeyword);
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        var name = ParseIdentifier();
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        ParseSemicolon();
        // NamespaceExportDeclaration nodes cannot have decorators or modifiers, we attach them here so we can report them in the grammar checker
        var result = FinishNode(_factory.NewNamespaceExportDeclaration(modifiers, name), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseExportDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList) *ast.Node</c>（Go:2592-2630）。</summary>
    private Node ParseExportDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        var saveContextFlags = _contextFlags;
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        SetContextFlags(NodeFlags.AwaitContext, true);
        Node? exportClause = null;
        Node? moduleSpecifier = null;
        Node? attributes = null;
        var isTypeOnly = ParseOptional(Kind.TypeKeyword);
        var namespaceExportPos = NodePos();
        if (ParseOptional(Kind.AsteriskToken))
        {
            if (ParseOptional(Kind.AsKeyword))
            {
                exportClause = ParseNamespaceExport(namespaceExportPos);
            }
            ParseExpected(Kind.FromKeyword);
            moduleSpecifier = ParseModuleSpecifier();
        }
        else
        {
            exportClause = ParseNamedExports();
            // It is not uncommon to accidentally omit the 'from' keyword. Additionally, in editing scenarios,
            // the 'from' keyword can be parsed as a named export when the export clause is unterminated (i.e. `export { from "moduleName";`)
            // If we don't have a 'from' keyword, see if we have a string literal such that ASI won't take effect.
            if (_token == Kind.FromKeyword || (_token == Kind.StringLiteral && !HasPrecedingLineBreak()))
            {
                ParseExpected(Kind.FromKeyword);
                moduleSpecifier = ParseModuleSpecifier();
            }
        }
        if (moduleSpecifier is not null && (_token == Kind.WithKeyword || _token == Kind.AssertKeyword) && !HasPrecedingLineBreak())
        {
            if (_token == Kind.AssertKeyword)
            {
                ParseErrorAtCurrentToken(Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
            }
            attributes = ParseImportAttributes(_token, false /*skipKeyword*/);
        }
        ParseSemicolon();
        _contextFlags = saveContextFlags;
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        var result = FinishNode(_factory.NewExportDeclaration(modifiers, isTypeOnly, exportClause, moduleSpecifier, attributes), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseNamespaceExport(pos int) *ast.Node</c>（Go:2632-2635）。</summary>
    private Node ParseNamespaceExport(int pos)
    {
        TryParseModuleExportName(false /*disallowKeywords*/, out var exportName);
        return FinishNode(_factory.NewNamespaceExport(exportName), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseNamedExports() *ast.Node</c>（Go:2637-2645）。</summary>
    private Node ParseNamedExports()
    {
        var pos = NodePos();
        // NamedImports:
        //  { }
        //  { ImportsList }
        //  { ImportsList, }
        var exports = ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, p => p.ParseExportSpecifier(), Kind.OpenBraceToken, Kind.CloseBraceToken);
        return FinishNode(_factory.NewNamedExports(exports), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseExportSpecifier() *ast.Node</c>（Go:2647-2657）。</summary>
    private Node ParseExportSpecifier()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var (isTypeOnly, propertyName, name) = ParseImportOrExportSpecifier(Kind.ExportSpecifier);
        var result = FinishNode(_factory.NewExportSpecifier(isTypeOnly, propertyName, name), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    // TYPES

    /// <summary>Go: <c>func (p *Parser) parseType() *ast.TypeNode</c>（Go:2659-2682）。</summary>
    private Node ParseType()
    {
        var saveContextFlags = _contextFlags;
        SetContextFlags(NodeFlags.TypeExcludesFlags, false);
        Node typeNode;
        if (IsStartOfFunctionTypeOrConstructorType())
        {
            typeNode = ParseFunctionOrConstructorType();
        }
        else
        {
            var pos = NodePos();
            typeNode = ParseUnionTypeOrHigher();
            if (!InDisallowConditionalTypesContext() && !HasPrecedingLineBreak() && ParseOptional(Kind.ExtendsKeyword))
            {
                // The type following 'extends' is not permitted to be another conditional type
                var extendsType = DoInContext(NodeFlags.DisallowConditionalTypesContext, true, p => p.ParseType());
                ParseExpected(Kind.QuestionToken);
                var trueType = DoInContext(NodeFlags.DisallowConditionalTypesContext, false, p => p.ParseType());
                ParseExpected(Kind.ColonToken);
                var falseType = DoInContext(NodeFlags.DisallowConditionalTypesContext, false, p => p.ParseType());
                var conditionalType = _factory.NewConditionalTypeNode(typeNode, extendsType, trueType, falseType);
                FinishNode(conditionalType, pos);
                typeNode = conditionalType;
            }
        }
        _contextFlags = saveContextFlags;
        return typeNode;
    }

    /// <summary>Go: <c>func (p *Parser) parseUnionTypeOrHigher() *ast.TypeNode</c>（Go:2684-2686）。</summary>
    private Node ParseUnionTypeOrHigher() => ParseUnionOrIntersectionType(Kind.BarToken, p => p.ParseIntersectionTypeOrHigher());

    /// <summary>Go: <c>func (p *Parser) parseIntersectionTypeOrHigher() *ast.TypeNode</c>（Go:2688-2690）。</summary>
    private Node ParseIntersectionTypeOrHigher() => ParseUnionOrIntersectionType(Kind.AmpersandToken, p => p.ParseTypeOperatorOrHigher());

    /// <summary>Go: <c>func (p *Parser) parseUnionOrIntersectionType(operator ast.Kind, parseConstituentType func(p *Parser) *ast.TypeNode) *ast.TypeNode</c>（Go:2692-2712）。</summary>
    // Go 的 `typeNode`/`parseConstituentType` 结果都是 `*ast.TypeNode`（可空指针，nil 会进 NodeList）。
    // C# 侧如实把结果标成 `Node?`，只在"两个调用点传的 constituent 解析器（parseIntersectionTypeOrHigher /
    // parseTypeOperatorOrHigher）永不返回 nil"这一跨调用不变量处用 `!`（与 Parser.Expressions.cs 里
    // `ParseLeftHandSideExpressionOrHigher()!` 的写法同一性质）。
    private Node ParseUnionOrIntersectionType(Kind @operator, Func<Parser, Node?> parseConstituentType)
    {
        var pos = NodePos();
        var isUnionType = @operator == Kind.BarToken;
        var hasLeadingOperator = ParseOptional(@operator);
        Node? typeNode;
        if (hasLeadingOperator)
        {
            typeNode = ParseFunctionOrConstructorTypeToError(isUnionType, parseConstituentType);
        }
        else
        {
            typeNode = parseConstituentType(this);
        }
        if (_token == @operator || hasLeadingOperator)
        {
            // Go: `types := make([]*ast.Node, 1, 8); types[0] = typeNode`（C# 数组没有 len<cap 视图 → List）
            var types = new List<Node>(8) { typeNode! };
            while (ParseOptional(@operator))
            {
                types.Add(ParseFunctionOrConstructorTypeToError(isUnionType, parseConstituentType)!);
            }
            typeNode = CreateUnionOrIntersectionTypeNode(@operator, NewNodeList(NewTextRange(pos, NodePos()), _nodeSliceArena.Clone(CollectionsMarshal.AsSpan(types))));
            FinishNode(typeNode, pos);
        }
        return typeNode!;
    }

    /// <summary>Go: <c>func (p *Parser) createUnionOrIntersectionTypeNode(operator ast.Kind, types *ast.NodeList) *ast.Node</c>（Go:2714-2723）。</summary>
    private Node CreateUnionOrIntersectionTypeNode(Kind @operator, NodeList types)
    {
        switch (@operator)
        {
            case Kind.BarToken:
                return _factory.NewUnionTypeNode(types);
            case Kind.AmpersandToken:
                return _factory.NewIntersectionTypeNode(types);
            default:
                throw new DebugFailureException("Unhandled case in createUnionOrIntersectionType");
        }
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeOperatorOrHigher() *ast.TypeNode</c>（Go:2725-2734）。</summary>
    private Node ParseTypeOperatorOrHigher()
    {
        var @operator = _token;
        switch (@operator)
        {
            case Kind.KeyOfKeyword:
            case Kind.UniqueKeyword:
            case Kind.ReadonlyKeyword:
                return ParseTypeOperator(@operator);
            case Kind.InferKeyword:
                return ParseInferType();
        }
        return DoInContext(NodeFlags.DisallowConditionalTypesContext, false, p => p.ParsePostfixTypeOrHigher());
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeOperator(operator ast.Kind) *ast.Node</c>（Go:2736-2740）。</summary>
    private Node ParseTypeOperator(Kind @operator)
    {
        var pos = NodePos();
        ParseExpected(@operator);
        return FinishNode(_factory.NewTypeOperatorNode(@operator, ParseTypeOperatorOrHigher()), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseInferType() *ast.Node</c>（Go:2742-2746）。</summary>
    private Node ParseInferType()
    {
        var pos = NodePos();
        ParseExpected(Kind.InferKeyword);
        return FinishNode(_factory.NewInferTypeNode(ParseTypeParameterOfInferType()), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeParameterOfInferType() *ast.Node</c>（Go:2748-2753）。</summary>
    private Node ParseTypeParameterOfInferType()
    {
        var pos = NodePos();
        var name = ParseIdentifier();
        var constraint = TryParseConstraintOfInferType();
        return FinishNode(_factory.NewTypeParameterDeclaration(null /*modifiers*/, name, constraint, null /*expression*/, null /*defaultType*/), pos);
    }

    /// <summary>Go: <c>func (p *Parser) tryParseConstraintOfInferType() *ast.Node</c>（Go:2755-2765）。</summary>
    private Node? TryParseConstraintOfInferType()
    {
        var state = Mark();
        if (ParseOptional(Kind.ExtendsKeyword))
        {
            var constraint = DoInContext(NodeFlags.DisallowConditionalTypesContext, true, p => p.ParseType());
            if (InDisallowConditionalTypesContext() || _token != Kind.QuestionToken)
            {
                return constraint;
            }
        }
        Rewind(state);
        return null;
    }

    /// <summary>Go: <c>func (p *Parser) parsePostfixTypeOrHigher() *ast.Node</c>（Go:2767-2797）。</summary>
    private Node ParsePostfixTypeOrHigher()
    {
        var pos = NodePos();
        var typeNode = ParseNonArrayType();
        while (!HasPrecedingLineBreak())
        {
            switch (_token)
            {
                case Kind.ExclamationToken:
                    NextToken();
                    typeNode = FinishNode(_factory.NewJSDocNonNullableType(typeNode), pos);
                    break;
                case Kind.QuestionToken:
                    // If next token is start of a type we have a conditional type
                    if (LookAhead(p => p.NextIsStartOfType()))
                    {
                        return typeNode;
                    }
                    NextToken();
                    typeNode = FinishNode(_factory.NewJSDocNullableType(typeNode), pos);
                    break;
                case Kind.OpenBracketToken:
                    ParseExpected(Kind.OpenBracketToken);
                    if (IsStartOfType(false /*isStartOfParameter*/))
                    {
                        var indexType = ParseType();
                        ParseExpected(Kind.CloseBracketToken);
                        typeNode = FinishNode(_factory.NewIndexedAccessTypeNode(typeNode, indexType), pos);
                    }
                    else
                    {
                        ParseExpected(Kind.CloseBracketToken);
                        typeNode = FinishNode(_factory.NewArrayTypeNode(typeNode), pos);
                    }
                    break;
                default:
                    return typeNode;
            }
        }
        return typeNode;
    }

    /// <summary>Go: <c>func (p *Parser) nextIsStartOfType() bool</c>（Go:2799-2802）。</summary>
    private bool NextIsStartOfType()
    {
        NextToken();
        return IsStartOfType(false /*inStartOfParameter*/);
    }

    /// <summary>Go: <c>func (p *Parser) parseNonArrayType() *ast.Node</c>（Go:2804-2872）。</summary>
    private Node ParseNonArrayType()
    {
        switch (_token)
        {
            case Kind.AnyKeyword:
            case Kind.UnknownKeyword:
            case Kind.StringKeyword:
            case Kind.NumberKeyword:
            case Kind.BigIntKeyword:
            case Kind.SymbolKeyword:
            case Kind.BooleanKeyword:
            case Kind.UndefinedKeyword:
            case Kind.NeverKeyword:
            case Kind.ObjectKeyword:
            {
                var state = Mark();
                var keywordTypeNode = ParseKeywordTypeNode();
                // If these are followed by a dot then parse these out as a dotted type reference instead
                if (_token != Kind.DotToken)
                {
                    return keywordTypeNode;
                }
                Rewind(state);
                return ParseTypeReference();
            }
            case Kind.AsteriskEqualsToken:
                // If there is '*=', treat it as * followed by postfix =
                _scanner.ReScanAsteriskEqualsToken();
                goto case Kind.AsteriskToken;
            case Kind.AsteriskToken:
                return ParseJSDocAllType();
            case Kind.QuestionQuestionToken:
                // If there is '??', treat it as prefix-'?' in JSDoc type.
                _scanner.ReScanQuestionToken();
                goto case Kind.QuestionToken;
            case Kind.QuestionToken:
                return ParseJSDocNullableType();
            case Kind.ExclamationToken:
                return ParseJSDocNonNullableType();
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.StringLiteral:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.NullKeyword:
                return ParseLiteralTypeNode(false /*negative*/);
            case Kind.MinusToken:
                if (LookAhead(p => p.NextTokenIsNumericOrBigIntLiteral()))
                {
                    return ParseLiteralTypeNode(true /*negative*/);
                }
                return ParseTypeReference();
            case Kind.VoidKeyword:
                return ParseKeywordTypeNode();
            case Kind.ThisKeyword:
            {
                var thisKeyword = ParseThisTypeNode();
                if (_token == Kind.IsKeyword && !HasPrecedingLineBreak())
                {
                    return ParseThisTypePredicate(thisKeyword);
                }
                return thisKeyword;
            }
            case Kind.TypeOfKeyword:
                if (LookAhead(p => p.NextIsStartOfTypeOfImportType()))
                {
                    return ParseImportType();
                }
                return ParseTypeQuery();
            case Kind.OpenBraceToken:
                if (LookAhead(p => p.NextIsStartOfMappedType()))
                {
                    return ParseMappedType();
                }
                return ParseTypeLiteral();
            case Kind.OpenBracketToken:
                return ParseTupleType();
            case Kind.OpenParenToken:
                return ParseParenthesizedType();
            case Kind.ImportKeyword:
                return ParseImportType();
            case Kind.AssertsKeyword:
                if (LookAhead(p => p.NextTokenIsIdentifierOrKeywordOnSameLine()))
                {
                    return ParseAssertsTypePredicate();
                }
                return ParseTypeReference();
            case Kind.TemplateHead:
                return ParseTemplateType();
            default:
                return ParseTypeReference();
        }
    }

    /// <summary>Go: <c>func (p *Parser) parseKeywordTypeNode() *ast.Node</c>（Go:2874-2879）。</summary>
    private Node ParseKeywordTypeNode()
    {
        var pos = NodePos();
        var result = _factory.NewKeywordTypeNode(_token);
        NextToken();
        return FinishNode(result, pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseThisTypeNode() *ast.Node</c>（Go:2881-2885）。</summary>
    private Node ParseThisTypeNode()
    {
        var pos = NodePos();
        NextToken();
        return FinishNode(_factory.NewThisTypeNode(), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseThisTypePredicate(lhs *ast.Node) *ast.Node</c>（Go:2887-2890）。</summary>
    private Node ParseThisTypePredicate(Node lhs)
    {
        NextToken();
        return FinishNode(_factory.NewTypePredicateNode(null /*assertsModifier*/, lhs, ParseType()), lhs.Pos());
    }

    /// <summary>Go: <c>func (p *Parser) parseJSDocAllType() *ast.Node</c>（Go:2892-2896）。</summary>
    private Node ParseJSDocAllType()
    {
        var pos = NodePos();
        NextToken();
        return FinishNode(_factory.NewJSDocAllType(), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseJSDocNonNullableType() *ast.TypeNode</c>（Go:2898-2902）。</summary>
    private Node ParseJSDocNonNullableType()
    {
        var pos = NodePos();
        NextToken();
        return FinishNode(_factory.NewJSDocNonNullableType(ParseTypeOperatorOrHigher()), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseJSDocNullableType() *ast.Node</c>（Go:2904-2909）。</summary>
    private Node ParseJSDocNullableType()
    {
        var pos = NodePos();
        // skip the ?
        NextToken();
        return FinishNode(_factory.NewJSDocNullableType(ParseTypeOperatorOrHigher()), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseJSDocType() *ast.TypeNode</c>（Go:2911-2926）。</summary>
    private Node ParseJSDocType()
    {
        _scanner.SetSkipJSDocLeadingAsterisks(true);
        var pos = NodePos();

        var hasDotDotDot = ParseOptional(Kind.DotDotDotToken);
        var t = ParseTypeOrTypePredicate();
        _scanner.SetSkipJSDocLeadingAsterisks(false);
        if (hasDotDotDot)
        {
            t = FinishNode(_factory.NewJSDocVariadicType(t), pos);
        }
        if (_token == Kind.EqualsToken)
        {
            NextToken();
            return FinishNode(_factory.NewJSDocOptionalType(t), pos);
        }
        return t;
    }

    /// <summary>Go: <c>func (p *Parser) parseLiteralTypeNode(negative bool) *ast.Node</c>（Go:2928-2943）。</summary>
    private Node ParseLiteralTypeNode(bool negative)
    {
        var pos = NodePos();
        if (negative)
        {
            NextToken();
        }
        Node expression;
        if (_token == Kind.TrueKeyword || _token == Kind.FalseKeyword || _token == Kind.NullKeyword)
        {
            expression = ParseKeywordExpression();
        }
        else
        {
            expression = ParseLiteralExpression();
        }
        if (negative)
        {
            expression = FinishNode(_factory.NewPrefixUnaryExpression(Kind.MinusToken, expression), pos);
        }
        return FinishNode(_factory.NewLiteralTypeNode(expression), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeReference() *ast.Node</c>（Go:2945-2948）。</summary>
    private Node ParseTypeReference()
    {
        var pos = NodePos();
        return FinishNode(_factory.NewTypeReferenceNode(ParseEntityNameOfTypeReference(), ParseTypeArgumentsOfTypeReference()), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseEntityNameOfTypeReference() *ast.Node</c>（Go:2950-2952）。</summary>
    private Node ParseEntityNameOfTypeReference() => ParseEntityName(true /*allowReservedWords*/, false /*allowPrivateName*/, Type_expected);

    /// <summary>Go: <c>func (p *Parser) parseEntityName(allowReservedWords bool, allowPrivateName bool, diagnosticMessage *diagnostics.Message) *ast.Node</c>（Go:2954-2971）。</summary>
    private Node ParseEntityName(bool allowReservedWords, bool allowPrivateName, Message? diagnosticMessage)
    {
        var pos = NodePos();
        Node entity;
        if (allowReservedWords)
        {
            entity = ParseIdentifierNameWithDiagnostic(diagnosticMessage);
        }
        else
        {
            entity = ParseIdentifierWithDiagnostic(diagnosticMessage, null);
        }
        while (ParseOptional(Kind.DotToken))
        {
            if (_token == Kind.LessThanToken)
            {
                // The entity is part of a JSDoc-style generic. We will use the gap between `typeName` and
                // `typeArguments` to report it as a grammar error in the checker.
                break;
            }
            entity = FinishNode(_factory.NewQualifiedName(entity, ParseRightSideOfDot(allowReservedWords, allowPrivateName, true /*allowUnicodeEscapeSequenceInIdentifierName*/)), pos);
        }
        return entity;
    }

    /// <summary>Go: <c>func (p *Parser) parseRightSideOfDot(allowIdentifierNames bool, allowPrivateIdentifiers bool, allowUnicodeEscapeSequenceInIdentifierName bool) *ast.Node</c>（Go:2973-3018）。</summary>
    private Node ParseRightSideOfDot(bool allowIdentifierNames, bool allowPrivateIdentifiers, bool allowUnicodeEscapeSequenceInIdentifierName)
    {
        // Technically a keyword is valid here as all identifiers and keywords are identifier names.
        // However, often we'll encounter this in error situations when the identifier or keyword
        // is actually starting another valid construct.
        //
        // So, we check for the following specific case:
        //
        //      name.
        //      identifierOrKeyword identifierNameOrKeyword
        //
        // Note: the newlines are important here.  For example, if that above code
        // were rewritten into:
        //
        //      name.identifierOrKeyword
        //      identifierNameOrKeyword
        //
        // Then we would consider it valid.  That's because ASI would take effect and
        // the code would be implicitly: "name.identifierOrKeyword; identifierNameOrKeyword".
        // In the first case though, ASI will not take effect because there is not a
        // line terminator after the identifier or keyword.
        if (HasPrecedingLineBreak() && TokenIsIdentifierOrKeyword(_token) && LookAhead(p => p.NextTokenIsIdentifierOrKeywordOnSameLine()))
        {
            // Report that we need an identifier.  However, report it right after the dot,
            // and not on the next token.  This is because the next token might actually
            // be an identifier and the error would be quite confusing.
            ParseErrorAt(NodePos(), NodePos(), Identifier_expected);
            return CreateMissingIdentifier();
        }
        if (_token == Kind.PrivateIdentifier)
        {
            var node = ParsePrivateIdentifier();
            if (allowPrivateIdentifiers)
            {
                return node;
            }
            ParseErrorAt(NodePos(), NodePos(), Identifier_expected);
            return CreateMissingIdentifier();
        }
        if (allowIdentifierNames)
        {
            if (allowUnicodeEscapeSequenceInIdentifierName)
            {
                return ParseIdentifierName();
            }
            return ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        }
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        var id = ParseIdentifier();
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        return id;
    }

    /// <summary>Go: <c>func (p *Parser) newIdentifier(text string) *ast.Node</c>（Go:3020-3027）。</summary>
    private Node NewIdentifier(string text)
    {
        _identifierCount++;
        var id = _factory.NewIdentifier(text);
        if (text == "await")
        {
            _statementHasAwaitIdentifier = true;
        }
        return id;
    }

    /// <summary>Go: <c>func (p *Parser) createMissingIdentifier() *ast.Node</c>（Go:3029-3031）。</summary>
    private Node CreateMissingIdentifier() => FinishNode(NewIdentifier(""), NodePos());

    /// <summary>Go: <c>func (p *Parser) parsePrivateIdentifier() *ast.Node</c>（Go:3033-3038）。</summary>
    private Node ParsePrivateIdentifier()
    {
        var pos = NodePos();
        var text = _scanner.TokenValue();
        NextToken();
        return FinishNode(_factory.NewPrivateIdentifier(text), pos);
    }

    /// <summary>Go: <c>func (p *Parser) reScanLessThanToken() ast.Kind</c>（Go:3040-3043）。</summary>
    private Kind ReScanLessThanToken()
    {
        _token = _scanner.ReScanLessThanToken();
        return _token;
    }

    /// <summary>Go: <c>func (p *Parser) reScanGreaterThanToken() ast.Kind</c>（Go:3045-3048）。</summary>
    private Kind ReScanGreaterThanToken()
    {
        _token = _scanner.ReScanGreaterThanToken();
        return _token;
    }

    /// <summary>Go: <c>func (p *Parser) reScanSlashToken() ast.Kind</c>（Go:3050-3053）。</summary>
    private Kind ReScanSlashToken()
    {
        _token = _scanner.ReScanSlashToken();
        return _token;
    }

    /// <summary>Go: <c>func (p *Parser) reScanTemplateToken(isTaggedTemplate bool) ast.Kind</c>（Go:3055-3058）。</summary>
    private Kind ReScanTemplateToken(bool isTaggedTemplate)
    {
        _token = _scanner.ReScanTemplateToken(isTaggedTemplate);
        return _token;
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeArgumentsOfTypeReference() *ast.NodeList</c>（Go:3060-3065）。</summary>
    private NodeList? ParseTypeArgumentsOfTypeReference()
    {
        if (!HasPrecedingLineBreak() && ReScanLessThanToken() == Kind.LessThanToken)
        {
            return ParseTypeArguments();
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeArguments() *ast.NodeList</c>（Go:3067-3072）。</summary>
    private NodeList? ParseTypeArguments()
    {
        if (_token == Kind.LessThanToken)
        {
            return ParseBracketedList(ParsingContext.TypeArguments, p => p.ParseType(), Kind.LessThanToken, Kind.GreaterThanToken);
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Parser) nextIsStartOfTypeOfImportType() bool</c>（Go:3074-3077）。</summary>
    private bool NextIsStartOfTypeOfImportType()
    {
        NextToken();
        return _token == Kind.ImportKeyword;
    }

    /// <summary>Go: <c>func (p *Parser) parseImportType() *ast.Node</c>（Go:3079-3119）。</summary>
    private Node ParseImportType()
    {
        _sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
        var pos = NodePos();
        var isTypeOf = ParseOptional(Kind.TypeOfKeyword);
        ParseExpected(Kind.ImportKeyword);
        ParseExpected(Kind.OpenParenToken);
        var typeNode = ParseType();
        Node? attributes = null;
        if (ParseOptional(Kind.CommaToken))
        {
            var openBracePosition = _scanner.TokenStart();
            ParseExpected(Kind.OpenBraceToken);
            var currentToken = _token;
            if (currentToken == Kind.WithKeyword || currentToken == Kind.AssertKeyword)
            {
                if (currentToken == Kind.AssertKeyword)
                {
                    ParseErrorAtCurrentToken(Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
                }
                NextToken();
            }
            else
            {
                ParseErrorAtCurrentToken(X_0_expected, TokenToString(Kind.WithKeyword));
            }
            ParseExpected(Kind.ColonToken);
            attributes = ParseImportAttributes(currentToken, true /*skipKeyword*/);
            ParseOptional(Kind.CommaToken);
            if (!ParseExpected(Kind.CloseBraceToken))
            {
                if (_diagnostics.Count != 0)
                {
                    var lastDiagnostic = _diagnostics[^1];
                    if (lastDiagnostic.Code() == X_0_expected.Code())
                    {
                        var related = NewDiagnostic(null, NewTextRange(openBracePosition, openBracePosition), The_parser_expected_to_find_a_1_to_match_the_0_token_here, "{", "}");
                        lastDiagnostic.AddRelatedInfo(related);
                    }
                }
            }
        }
        ParseExpected(Kind.CloseParenToken);
        Node? qualifier = null;
        if (ParseOptional(Kind.DotToken))
        {
            qualifier = ParseEntityNameOfTypeReference();
        }
        var typeArguments = ParseTypeArgumentsOfTypeReference();
        return FinishNode(_factory.NewImportTypeNode(isTypeOf, typeNode, attributes, qualifier, typeArguments), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseImportAttribute() *ast.Node</c>（Go:3121-3136）。</summary>
    private Node ParseImportAttribute()
    {
        var pos = NodePos();
        Node? name = null;
        if (TokenIsIdentifierOrKeyword(_token))
        {
            name = ParseIdentifierName();
        }
        else if (_token == Kind.StringLiteral)
        {
            name = ParseLiteralExpression();
        }
        if (name is not null)
        {
            ParseExpected(Kind.ColonToken);
        }
        else
        {
            ParseErrorAtCurrentToken(Identifier_or_string_literal_expected);
        }
        var value = ParseAssignmentExpressionOrHigher();
        return FinishNode(_factory.NewImportAttribute(name, value), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseImportAttributes(token ast.Kind, skipKeyword bool) *ast.Node</c>（Go:3138-3162）。</summary>
    private Node ParseImportAttributes(Kind token, bool skipKeyword)
    {
        var pos = NodePos();
        if (!skipKeyword)
        {
            ParseExpected(token);
        }
        NodeList? elements;
        var multiLine = false;
        var openBracePosition = _scanner.TokenStart();
        if (ParseExpected(Kind.OpenBraceToken))
        {
            multiLine = HasPrecedingLineBreak();
            elements = ParseDelimitedList(ParsingContext.ImportAttributes, p => p.ParseImportAttribute());
            if (!ParseExpected(Kind.CloseBraceToken))
            {
                if (_diagnostics.Count != 0)
                {
                    var lastDiagnostic = _diagnostics[^1];
                    if (lastDiagnostic.Code() == X_0_expected.Code())
                    {
                        var related = NewDiagnostic(null, NewTextRange(openBracePosition, openBracePosition), The_parser_expected_to_find_a_1_to_match_the_0_token_here, "{", "}");
                        lastDiagnostic.AddRelatedInfo(related);
                    }
                }
            }
        }
        else
        {
            elements = ParseEmptyNodeList();
        }
        return FinishNode(_factory.NewImportAttributes(token, elements, multiLine), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeQuery() *ast.Node</c>（Go:3164-3174）。</summary>
    private Node ParseTypeQuery()
    {
        var pos = NodePos();
        ParseExpected(Kind.TypeOfKeyword);
        var entityName = ParseEntityName(true /*allowReservedWords*/, true /*allowPrivateName*/, null);
        // Make sure we perform ASI to prevent parsing the next line's type arguments as part of an instantiation expression
        NodeList? typeArguments = null;
        if (!HasPrecedingLineBreak())
        {
            typeArguments = ParseTypeArguments();
        }
        return FinishNode(_factory.NewTypeQueryNode(entityName, typeArguments), pos);
    }

    /// <summary>Go: <c>func (p *Parser) nextIsStartOfMappedType() bool</c>（Go:3176-3185）。</summary>
    private bool NextIsStartOfMappedType()
    {
        NextToken();
        if (_token == Kind.PlusToken || _token == Kind.MinusToken)
        {
            return NextToken() == Kind.ReadonlyKeyword;
        }
        if (_token == Kind.ReadonlyKeyword)
        {
            NextToken();
        }
        return _token == Kind.OpenBracketToken && NextTokenIsIdentifier() && NextToken() == Kind.InKeyword;
    }

    /// <summary>Go: <c>func (p *Parser) parseMappedType() *ast.Node</c>（Go:3187-3216）。</summary>
    private Node ParseMappedType()
    {
        var pos = NodePos();
        ParseExpected(Kind.OpenBraceToken);
        Node? readonlyToken = null; // ReadonlyKeyword | PlusToken | MinusToken
        if (_token == Kind.ReadonlyKeyword || _token == Kind.PlusToken || _token == Kind.MinusToken)
        {
            readonlyToken = ParseTokenNode();
            if (readonlyToken.Kind != Kind.ReadonlyKeyword)
            {
                ParseExpected(Kind.ReadonlyKeyword);
            }
        }
        ParseExpected(Kind.OpenBracketToken);
        var typeParameter = ParseMappedTypeParameter();
        Node? nameType = null;
        if (ParseOptional(Kind.AsKeyword))
        {
            nameType = ParseType();
        }
        ParseExpected(Kind.CloseBracketToken);
        Node? questionToken = null; // QuestionToken | PlusToken | MinusToken
        if (_token == Kind.QuestionToken || _token == Kind.PlusToken || _token == Kind.MinusToken)
        {
            questionToken = ParseTokenNode();
            if (questionToken.Kind != Kind.QuestionToken)
            {
                ParseExpected(Kind.QuestionToken);
            }
        }
        var typeNode = ParseTypeAnnotation();
        ParseSemicolon();
        var members = ParseList(ParsingContext.TypeMembers, p => p.ParseTypeMember());
        ParseExpected(Kind.CloseBraceToken);
        return FinishNode(_factory.NewMappedTypeNode(readonlyToken, typeParameter, nameType, questionToken, typeNode, members), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseMappedTypeParameter() *ast.Node</c>（Go:3218-3224）。</summary>
    private Node ParseMappedTypeParameter()
    {
        var pos = NodePos();
        var name = ParseIdentifierName();
        ParseExpected(Kind.InKeyword);
        var typeNode = ParseType();
        return FinishNode(_factory.NewTypeParameterDeclaration(null /*modifiers*/, name, typeNode, null /*expression*/, null /*defaultType*/), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeMember() *ast.Node</c>（Go:3226-3246）。</summary>
    private Node ParseTypeMember()
    {
        if (_token == Kind.OpenParenToken || _token == Kind.LessThanToken)
        {
            return ParseSignatureMember(Kind.CallSignature);
        }
        if (_token == Kind.NewKeyword && LookAhead(p => p.NextTokenIsOpenParenOrLessThan()))
        {
            return ParseSignatureMember(Kind.ConstructSignature);
        }
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var modifiers = ParseModifiers();
        if (ParseContextualModifier(Kind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, jsdoc, modifiers, Kind.GetAccessor, ParseFlags.Type);
        }
        if (ParseContextualModifier(Kind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, jsdoc, modifiers, Kind.SetAccessor, ParseFlags.Type);
        }
        if (IsIndexSignature())
        {
            return ParseIndexSignatureDeclaration(pos, jsdoc, modifiers);
        }
        return ParsePropertyOrMethodSignature(pos, jsdoc, modifiers);
    }

    /// <summary>Go: <c>func (p *Parser) nextTokenIsOpenParenOrLessThan() bool</c>（Go:3248-3251）。</summary>
    private bool NextTokenIsOpenParenOrLessThan()
    {
        NextToken();
        return _token == Kind.OpenParenToken || _token == Kind.LessThanToken;
    }

    /// <summary>Go: <c>func (p *Parser) parseSignatureMember(kind ast.Kind) *ast.Node</c>（Go:3253-3272）。</summary>
    private Node ParseSignatureMember(Kind kind)
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        if (kind == Kind.ConstructSignature)
        {
            ParseExpected(Kind.NewKeyword);
        }
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(ParseFlags.Type);
        var typeNode = ParseReturnType(Kind.ColonToken /*isType*/, true);
        ParseTypeMemberSemicolon();
        Node result;
        if (kind == Kind.CallSignature)
        {
            result = _factory.NewCallSignatureDeclaration(typeParameters, parameters, typeNode);
        }
        else
        {
            result = _factory.NewConstructSignatureDeclaration(typeParameters, parameters, typeNode);
        }
        FinishNode(result, pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeParameters() *ast.NodeList</c>（Go:3274-3279）。</summary>
    private NodeList? ParseTypeParameters()
    {
        if (_token == Kind.LessThanToken)
        {
            return ParseBracketedList(ParsingContext.TypeParameters, p => p.ParseTypeParameter(), Kind.LessThanToken, Kind.GreaterThanToken);
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeParameter() *ast.Node</c>（Go:3281-3311）。</summary>
    private Node ParseTypeParameter()
    {
        var pos = NodePos();
        var modifiers = ParseModifiersEx(false /*allowDecorators*/, true /*permitConstAsModifier*/, false /*stopOnStartOfClassStaticBlock*/);
        var name = ParseIdentifier();
        Node? constraint = null;
        Node? expression = null;
        if (ParseOptional(Kind.ExtendsKeyword))
        {
            // It's not uncommon for people to write improper constraints to a generic.  If the
            // user writes a constraint that is an expression and not an actual type, then parse
            // it out as an expression (so we can recover well), but report that a type is needed
            // instead.
            if (IsStartOfType(false /*inStartOfParameter*/) || !IsStartOfExpression())
            {
                constraint = ParseType();
            }
            else
            {
                // It was not a type, and it looked like an expression.  Parse out an expression
                // here so we recover well.  Note: it is important that we call parseUnaryExpression
                // and not parseExpression here.  If the user has:
                //
                //      <T extends "">
                //
                // We do *not* want to consume the `>` as we're consuming the expression for "".
                expression = ParseUnaryExpressionOrHigher();
            }
        }
        Node? defaultType = null;
        if (ParseOptional(Kind.EqualsToken))
        {
            defaultType = ParseType();
        }
        var result = _factory.NewTypeParameterDeclaration(modifiers, name, constraint, expression, defaultType);
        return FinishNode(result, pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseParameters(flags ParseFlags) *ast.NodeList</c>（Go:3313-3333）。</summary>
    private NodeList? ParseParameters(ParseFlags flags)
    {
        // FormalParameters [Yield,Await]: (modified)
        //      [empty]
        //      FormalParameterList[?Yield,Await]
        //
        // FormalParameter[Yield,Await]: (modified)
        //      BindingElement[?Yield,Await]
        //
        // BindingElement [Yield,Await]: (modified)
        //      SingleNameBinding[?Yield,?Await]
        //      BindingPattern[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        //
        // SingleNameBinding [Yield,Await]:
        //      BindingIdentifier[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        if (ParseExpected(Kind.OpenParenToken))
        {
            var parameters = ParseParametersWorker(flags, true /*allowAmbiguity*/);
            ParseExpected(Kind.CloseParenToken);
            return parameters;
        }
        return CreateMissingList();
    }

    /// <summary>Go: <c>func (p *Parser) parseParametersWorker(flags ParseFlags, allowAmbiguity bool) *ast.NodeList</c>（Go:3335-3362）。</summary>
    private NodeList? ParseParametersWorker(ParseFlags flags, bool allowAmbiguity)
    {
        // FormalParameters [Yield,Await]: (modified)
        //      [empty]
        //      FormalParameterList[?Yield,Await]
        //
        // FormalParameter[Yield,Await]: (modified)
        //      BindingElement[?Yield,Await]
        //
        // BindingElement [Yield,Await]: (modified)
        //      SingleNameBinding[?Yield,?Await]
        //      BindingPattern[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        //
        // SingleNameBinding [Yield,Await]:
        //      BindingIdentifier[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        var inAwaitContext = (_contextFlags & NodeFlags.AwaitContext) != 0;
        var saveContextFlags = _contextFlags;
        SetContextFlags(NodeFlags.YieldContext, (flags & ParseFlags.Yield) != 0);
        SetContextFlags(NodeFlags.AwaitContext, (flags & ParseFlags.Await) != 0);
        var parameters = ParseDelimitedList(ParsingContext.Parameters, p =>
        {
            var parameter = p.ParseParameterEx(inAwaitContext, allowAmbiguity);
            if (parameter is not null && (flags & ParseFlags.Type) == 0)
            {
                p.CheckJSSyntax(parameter);
            }
            return parameter;
        });
        _contextFlags = saveContextFlags;
        return parameters;
    }

    /// <summary>Go: <c>func (p *Parser) parseParameter() *ast.Node</c>（Go:3364-3366）。</summary>
    private Node? ParseParameter() => ParseParameterEx(false /*inOuterAwaitContext*/, true /*allowAmbiguity*/);

    /// <summary>Go: <c>func (p *Parser) parseParameterEx(inOuterAwaitContext bool, allowAmbiguity bool) *ast.Node</c>（Go:3368-3407）。</summary>
    private Node? ParseParameterEx(bool inOuterAwaitContext, bool allowAmbiguity)
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]
        // Decorators are parsed in the outer [Await] context, the rest of the parameter is parsed in the function's [Await] context.
        var saveContextFlags = _contextFlags;
        SetContextFlags(NodeFlags.AwaitContext, inOuterAwaitContext);
        var modifiers = ParseModifiersEx(true /*allowDecorators*/, false /*permitConstAsModifier*/, false /*stopOnStartOfClassStaticBlock*/);
        _contextFlags = saveContextFlags;
        if (_token == Kind.ThisKeyword)
        {
            var result = _factory.NewParameterDeclaration(
                modifiers,
                null, /*dotDotDotToken*/
                CreateIdentifier(true /*isIdentifier*/),
                null, /*questionToken*/
                ParseTypeAnnotation(),
                null /*initializer*/
            );
            if (modifiers is not null)
            {
                ParseErrorAtRange(modifiers.Nodes![0].Loc, Neither_decorators_nor_modifiers_may_be_applied_to_this_parameters);
            }
            WithJSDoc(FinishNode(result, pos), jsdoc);
            return result;
        }
        var dotDotDotToken = ParseOptionalToken(Kind.DotDotDotToken);
        if (!allowAmbiguity && !IsParameterNameStart())
        {
            return null;
        }
        var parameter = _factory.NewParameterDeclaration(
            modifiers,
            dotDotDotToken,
            ParseNameOfParameter(modifiers),
            ParseOptionalToken(Kind.QuestionToken),
            ParseTypeAnnotation(),
            ParseInitializer()
        );
        WithJSDoc(FinishNode(parameter, pos), jsdoc);
        return parameter;
    }

    /// <summary>Go: <c>func (p *Parser) isParameterNameStart() bool</c>（Go:3409-3414）。</summary>
    private bool IsParameterNameStart() =>
        // Be permissive about await and yield by calling isBindingIdentifier instead of isIdentifier; disallowing
        // them during a speculative parse leads to many more follow-on errors than allowing the function to parse then later
        // complaining about the use of the keywords.
        IsBindingIdentifier() || _token == Kind.OpenBracketToken || _token == Kind.OpenBraceToken;

    /// <summary>Go: <c>func (p *Parser) parseNameOfParameter(modifiers *ast.ModifierList) *ast.Node</c>（Go:3416-3432）。</summary>
    private Node ParseNameOfParameter(ModifierList? modifiers)
    {
        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]
        var name = ParseIdentifierOrPatternWithDiagnostic(Private_identifiers_cannot_be_used_as_parameters);
        if (name.Loc.Len() == 0 && modifiers is null && IsModifierKind(_token))
        {
            // in cases like
            // 'use strict'
            // function foo(static)
            // isParameter('static') == true, because of isModifier('static')
            // however 'static' is not a legal identifier in a strict mode.
            // so result of this function will be Parameter (flags = 0, name = missing, type = undefined, initializer = undefined)
            // and current token will not change => parsing of the enclosing parameter list will last till the end of time (or OOM)
            // to avoid this we'll advance cursor to the next token.
            NextToken();
        }
        return name;
    }

    /// <summary>Go: <c>func (p *Parser) parseReturnType(returnToken ast.Kind, isType bool) *ast.TypeNode</c>（Go:3434-3439）。</summary>
    private Node? ParseReturnType(Kind returnToken, bool isType)
    {
        if (ShouldParseReturnType(returnToken, isType))
        {
            return DoInContext(NodeFlags.DisallowConditionalTypesContext, false, p => p.ParseTypeOrTypePredicate());
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Parser) shouldParseReturnType(returnToken ast.Kind, isType bool) bool</c>（Go:3441-3454）。</summary>
    private bool ShouldParseReturnType(Kind returnToken, bool isType)
    {
        if (returnToken == Kind.EqualsGreaterThanToken)
        {
            ParseExpected(returnToken);
            return true;
        }
        else if (ParseOptional(Kind.ColonToken))
        {
            return true;
        }
        else if (isType && _token == Kind.EqualsGreaterThanToken)
        {
            // This is easy to get backward, especially in type contexts, so parse the type anyway
            ParseErrorAtCurrentToken(X_0_expected, TokenToString(Kind.ColonToken));
            NextToken();
            return true;
        }
        return false;
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeOrTypePredicate() *ast.TypeNode</c>（Go:3456-3468）。</summary>
    private Node ParseTypeOrTypePredicate()
    {
        if (IsIdentifier())
        {
            var state = Mark();
            var pos = NodePos();
            var id = ParseIdentifier();
            if (_token == Kind.IsKeyword && !HasPrecedingLineBreak())
            {
                NextToken();
                return FinishNode(_factory.NewTypePredicateNode(null /*assertsModifier*/, id, ParseType()), pos);
            }
            Rewind(state);
        }
        return ParseType();
    }

    /// <summary>Go: <c>func (p *Parser) parseTypeMemberSemicolon()</c>（Go:3470-3478）。</summary>
    private void ParseTypeMemberSemicolon()
    {
        // We allow type members to be separated by commas or (possibly ASI) semicolons.
        // First check if it was a comma.  If so, we're done with the member.
        if (ParseOptional(Kind.CommaToken))
        {
            return;
        }
        // Didn't have a comma.  We must have a (possible ASI) semicolon.
        ParseSemicolon();
    }

    /// <summary>Go: <c>func (p *Parser) parseAccessorDeclaration(pos int, jsdoc jsdocScannerInfo, modifiers *ast.ModifierList, kind ast.Kind, flags ParseFlags) *ast.Node</c>（Go:3480-3498）。</summary>
    private Node ParseAccessorDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers, Kind kind, ParseFlags flags)
    {
        var name = ParsePropertyName();
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(ParseFlags.None);
        var returnType = ParseReturnType(Kind.ColonToken, false /*isType*/);
        var body = ParseFunctionBlockOrSemicolon(flags, null /*diagnosticMessage*/);
        Node result;
        // Keep track of `typeParameters` (for both) and `type` (for setters) if they were parsed those indicate grammar errors
        if (kind == Kind.GetAccessor)
        {
            result = _factory.NewGetAccessorDeclaration(modifiers, name, typeParameters, parameters, returnType, null /*fullSignature*/, body);
        }
        else
        {
            result = _factory.NewSetAccessorDeclaration(modifiers, name, typeParameters, parameters, returnType, null /*fullSignature*/, body);
        }
        WithJSDoc(FinishNode(result, pos), jsdoc);
        if ((flags & ParseFlags.Type) == 0)
        {
            CheckJSSyntax(result);
        }
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parsePropertyName() *ast.Node</c>（Go:3500-3505）。</summary>
    private Node ParsePropertyName()
    {
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        var prop = ParsePropertyNameWorker(true /*allowComputedPropertyNames*/);
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        return prop;
    }

    /// <summary>Go: <c>func (p *Parser) parsePropertyNameWorker(allowComputedPropertyNames bool) *ast.Node</c>（Go:3507-3518）。</summary>
    private Node ParsePropertyNameWorker(bool allowComputedPropertyNames)
    {
        if (_token == Kind.StringLiteral || _token == Kind.NumericLiteral || _token == Kind.BigIntLiteral)
        {
            return ParseLiteralExpression();
        }
        if (allowComputedPropertyNames && _token == Kind.OpenBracketToken)
        {
            return ParseComputedPropertyName();
        }
        if (_token == Kind.PrivateIdentifier)
        {
            return ParsePrivateIdentifier();
        }
        return ParseIdentifierName();
    }

    /// <summary>Go: <c>func (p *Parser) parseComputedPropertyName() *ast.Node</c>（Go:3520-3533）。</summary>
    private Node ParseComputedPropertyName()
    {
        // PropertyName [Yield]:
        //      LiteralPropertyName
        //      ComputedPropertyName[?Yield]
        var pos = NodePos();
        ParseExpected(Kind.OpenBracketToken);
        // We parse any expression (including a comma expression). But the grammar
        // says that only an assignment expression is allowed, so the grammar checker
        // will error if it sees a comma expression.
        var expression = ParseExpressionAllowIn();
        ParseExpected(Kind.CloseBracketToken);
        return FinishNode(_factory.NewComputedPropertyName(expression), pos);
    }
}

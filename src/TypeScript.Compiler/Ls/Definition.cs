// ported from: internal/ls/definition.go:1-517
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的双返回值 `(lsproto.DefinitionResponse, error)` → C# `(LocationOrLocationsOrDefinitionLinksOrNull Response,
//    Exception? Error)`（同 Ls/Diagnostics.cs 的 `ProvideDiagnostics`）。Go 的类型别名
//    `DefinitionResponse = LocationOrLocationsOrDefinitionLinksOrNull`、`TypeDefinitionResponse = 同上`
//    在 C# 侧不生成（见 LsProto/Lsp.Generated.Methods.cs:348,583 的说明），直接用底层 union 类型。
// 2) `combineDefinitionResponses(results []lsproto.DefinitionResponse, links bool)` → 形参用
//    `List<LocationOrLocationsOrDefinitionLinksOrNull>`（调用点本来就在建列表）。
// 3) **`lsproto.Location` 在 Go 里是可比较的 struct（`collections.Set[lsproto.Location]` 按结构相等去重），
//    C# 的 `Location` 是 class（引用相等）** ⇒ 去重集合的元素换成 5 元组
//    `(DocumentUri Uri, uint StartLine, uint StartCharacter, uint EndLine, uint EndCharacter)`，
//    与 Go 的字段逐一对应。`DefinitionLinks` 在 Go 里是 `[]*lsproto.LocationLink`（元素非 nil）⇒ C# `LocationLink?[]?`。
// 4) Go 的 `locationRanges := collections.Set[fileRange]{}` 依赖 `core.TextRange` 的值相等 + `*ast.SourceFile` 的指针
//    相等 ⇒ C# 用 `readonly record struct FileRange(SourceFile File, TextRange Range)`（record struct 生成的结构相等
//    正是该语义）。
// 5) `slices.Clip(s)` 只是"零拷贝改 len/cap"（Go 的惯用法），`core.Filter` 本身已分配新切片 ⇒ C# 直接 `Filter(s, …)`。
// 6) `node.AsVariableDeclarationList().Declarations.Nodes` → C# 的 `AsXxx()` 返回可空 ⇒
//    `node.Parent!.AsVariableDeclarationList()!.Declarations!.Nodes`。
// 7) Go 里 `file := ast.GetSourceFileOfNode(stmt)` 遮蔽了外层 `file` ⇒ C# 改名 `stmtFile`（CS0136）。
// 8) `spanmap.FeatureDefinition` → `Feature.Definition`；`typ := c.GetTypeOfSymbolAtLocation(...)` 等名字不变。
// 9) **借用 helper 备案（G1 契约 §3.2 的同款做法）**：Go 的 `getContextNode` 定义在 **findallreferences.go:286-317**
//    （G6 的票面文件），但 definition.go 的 `createDefinitionLocations` 依赖它，G1 又没落地 ⇒ 本文件就地落地为
//    `Ls.GetContextNode` 并在此备案：**G6 的 FindAllReferences.cs 请勿重复声明**（会 CS0111），直接调用即可。
//    同文件内同样只声明一次的还有 `Ls.GetDeclarationsFromLocation` / `Ls.GetDeclarationsFromObjectLiteralElement` /
//    `Ls.TryGetSignatureDeclaration` / `Ls.GetAncestorCallLikeExpression`（LsG3Hover 已确认不重复声明前两者）。
// 10) **本文件对 `Ls` 静态类的成员一律经类型别名 `LsStatics` 调用**：在 `namespace TypeScript.Ls` 里裸写 `Ls.Xxx(...)`
//     会被解析成"命名空间 `TypeScript.Ls` 里的类型 `Xxx`"（CS0234，见 G1 契约 §4.1），而 `Ast`/`Core`/`Scanner` 等
//     同名遮蔽用 `using static` 化解。Go 里写作 `ast.Xxx(...)` 的调用点用 `using static TypeScript.Ast.Ast;` 后的裸名
//     （= Ast 的版本，与 Go 逐字一致；`Ls` 有自己的同名版本，那 8 个成员里本文件只用到 `IsJumpStatementTarget` /
//     `IsRightSideOfPropertyAccess`，走的都是 Ast）。
// 11) Go 的 `l.converters` 字段在 C# 是 `_converters!`（见 Ls/LanguageService.cs 的偏离清单）。
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using static TypeScript.Scanner.Scanner;
// `Ast`/`Core`/`Scanner` 等"命名空间 + 同名静态类"用 `using static` 化解；`Ls` 静态类的成员直接用 `Ls.` 前缀
// （`TypeScript.Ls` 命名空间里**没有**叫 `Ast` 的成员 ⇒ `Ast.Xxx` 会被解析成命名空间 `TypeScript.Ast`，CS0234；
//  而 `TypeScript.Ls` 里有一张叫 `Ls` 的类型 ⇒ `Ls.Xxx` 能正确解析成静态类，先例 Ls/Hover.cs:1452 的 `Ls.GetRangeOfNode`）。
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;
using Program = TypeScript.Compiler.Program;

namespace TypeScript.Ls;

/// <summary>Go: <c>type fileRange struct { file *ast.SourceFile; fileRange core.TextRange }</c>（definition.go:227-230）。</summary>
internal readonly record struct FileRange(SourceFile File, TextRange Range);

/// <summary>
/// Go 的 <c>collections.Set[lsproto.Location]</c> 去重依赖 Go 的 <c>Location</c> 是可比较 struct（字段逐一比较），
/// 而 C# 的 <c>LsProto.Location</c> 是 class（引用相等）⇒ 用与 Go 字段一一对应的值键（见文件头第 3 条）。
/// </summary>
internal readonly record struct LocationKey(
    DocumentUri Uri,
    uint StartLine,
    uint StartCharacter,
    uint EndLine,
    uint EndCharacter)
{
    public LocationKey(Location location)
        : this(
            location.Uri,
            location.Range.Start.Line,
            location.Range.Start.Character,
            location.Range.End.Line,
            location.Range.End.Character)
    {
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideDefinition(ctx, documentURI, position) (lsproto.DefinitionResponse, error)</c>（definition.go:23-31）。</summary>
    public (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) ProvideDefinition(
        Context ctx,
        DocumentUri documentURI,
        Position position)
    {
        if (UserPreferences().PreferGoToSourceDefinition)
        {
            return ProvideSourceDefinition(ctx, documentURI, position);
        }

        return ProvideDefinitionWorker(ctx, documentURI, position);
    }

    /// <summary>Go: <c>func (l *LanguageService) provideDefinitionWorker(ctx, documentURI, position) (lsproto.DefinitionResponse, error)</c>（definition.go:33-48）。</summary>
    internal (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) ProvideDefinitionWorker(
        Context ctx,
        DocumentUri documentURI,
        Position position)
    {
        var clientSupportsLink = GetClientCapabilities().TextDocument.Definition.LinkSupport;

        var (program, file) = GetProgramAndFile(documentURI);
        var positions = _converters!.FromLSPPositionForSourceFile(file, position, Feature.Definition);
        var results = new List<LocationOrLocationsOrDefinitionLinksOrNull>(positions.Length);
        foreach (var mapped in positions)
        {
            if (mapped.Fidelity.IsSingleSegment())
            {
                results.Add(ProvideDefinitionAtPosition(ctx, program, mapped.Script, mapped.Position, clientSupportsLink));
            }
        }

        return (Ls.CombineDefinitionResponses(results, clientSupportsLink), null);
    }

    /// <summary>Go: <c>func (l *LanguageService) provideDefinitionAtPosition(ctx, program, file, textPos, clientSupportsLink) lsproto.DefinitionResponse</c>（definition.go:50-110）。</summary>
    internal LocationOrLocationsOrDefinitionLinksOrNull ProvideDefinitionAtPosition(
        Context ctx,
        Program program,
        SourceFile file,
        TextPos textPos,
        bool clientSupportsLink)
    {
        var pos = (int)textPos;
        var node = GetTouchingPropertyName(file, pos);
        var reference = Ls.GetReferenceAtPosition(file, pos, program);

        if (node.Kind == Kind.SourceFile)
        {
            return new LocationOrLocationsOrDefinitionLinksOrNull();
        }

        var (originSelectionRange, _) = CreateLspRangeFromNode(node, file);
        if (reference is not null && reference._file is not null)
        {
            return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, [], reference, Feature.Definition);
        }

        var (c, done) = program.GetTypeCheckerForFile(ctx, file);
        try
        {
            if (node.Kind == Kind.OverrideKeyword)
            {
                if (Ls.GetSymbolForOverriddenMember(c, node) is { } sym)
                {
                    return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, sym.Declarations ?? [], null /*reference*/, Feature.Definition);
                }
            }

            if (IsJumpStatementTarget(node))
            {
                if (Ls.GetTargetLabel(node.Parent, node.Text()) is { } label)
                {
                    return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, [label], null /*reference*/, Feature.Definition);
                }
            }

            if (node.Kind == Kind.CaseKeyword || node.Kind == Kind.DefaultKeyword && IsDefaultClause(node.Parent!))
            {
                if (FindAncestor(node.Parent, IsSwitchStatement) is { } stmt)
                {
                    var stmtFile = GetSourceFileOfNode(stmt)!;
                    return CreateLocationFromFileAndRange(stmtFile, GetRangeOfTokenAtPosition(stmtFile, stmt.Pos()), Feature.Definition);
                }
            }

            if (node.Kind == Kind.ReturnKeyword || node.Kind == Kind.YieldKeyword || node.Kind == Kind.AwaitKeyword)
            {
                if (FindAncestor(node, IsFunctionLikeDeclaration) is { } fn)
                {
                    return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, [fn], null /*reference*/, Feature.Definition);
                }
            }

            var declarations = Ls.GetDeclarationsFromLocation(c, node);
            var calledDeclaration = Ls.TryGetSignatureDeclaration(c, node);
            if (calledDeclaration is not null && !(IsJsxOpeningLikeElement(node.Parent!) && Ls.IsJsxConstructorLike(calledDeclaration)))
            {
                var symbol = c.GetSymbolAtLocation(Ls.GetDeclarationNameForKeyword(node));
                if (symbol is not null && Some(c.GetRootSymbols(symbol), rootSymbol => Ls.SymbolMatchesSignature(rootSymbol, calledDeclaration)))
                {
                    if (!IsConstructorDeclaration(calledDeclaration))
                    {
                        declarations = null!;
                    }
                    else
                    {
                        declarations = Filter(declarations, node1 => node1 != calledDeclaration && (IsClassDeclaration(node1) || IsClassExpression(node1)));
                    }
                }
                else
                {
                    declarations = Filter(declarations, node1 => node1 != calledDeclaration);
                }

                declarations = Concatenate(declarations, [calledDeclaration]);
            }

            return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, declarations, reference, Feature.Definition);
        }
        finally
        {
            done();
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideTypeDefinition(ctx, documentURI, position) (lsproto.TypeDefinitionResponse, error)</c>（definition.go:112-127）。</summary>
    public (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) ProvideTypeDefinition(
        Context ctx,
        DocumentUri documentURI,
        Position position)
    {
        var clientSupportsLink = GetClientCapabilities().TextDocument.TypeDefinition.LinkSupport;

        var (program, file) = GetProgramAndFile(documentURI);
        var positions = _converters!.FromLSPPositionForSourceFile(file, position, Feature.TypeDefinition);
        var results = new List<LocationOrLocationsOrDefinitionLinksOrNull>(positions.Length);
        foreach (var mapped in positions)
        {
            if (mapped.Fidelity.IsSingleSegment())
            {
                results.Add(ProvideTypeDefinitionAtPosition(ctx, program, mapped.Script, mapped.Position, clientSupportsLink));
            }
        }

        return (Ls.CombineDefinitionResponses(results, clientSupportsLink), null);
    }

    /// <summary>Go: <c>func (l *LanguageService) provideTypeDefinitionAtPosition(ctx, program, file, textPos, clientSupportsLink) lsproto.TypeDefinitionResponse</c>（definition.go:129-155）。</summary>
    internal LocationOrLocationsOrDefinitionLinksOrNull ProvideTypeDefinitionAtPosition(
        Context ctx,
        Program program,
        SourceFile file,
        TextPos textPos,
        bool clientSupportsLink)
    {
        var pos = (int)textPos;
        var node = GetTouchingPropertyName(file, pos);
        if (node.Kind == Kind.SourceFile)
        {
            return new LocationOrLocationsOrDefinitionLinksOrNull();
        }

        var (originSelectionRange, _) = CreateLspRangeFromNode(node, file);

        var (c, done) = program.GetTypeCheckerForFile(ctx, file);
        try
        {
            node = Ls.GetDeclarationNameForKeyword(node);

            if (c.GetSymbolAtLocation(node) is { } symbol)
            {
                var symbolType = Ls.GetTypeOfSymbolAtLocation(c, symbol, node);
                var declarations = Ls.GetDeclarationsFromType(symbolType);
                if (c.GetFirstTypeArgumentFromKnownType(symbolType) is { } typeArgument)
                {
                    declarations = Concatenate(Ls.GetDeclarationsFromType(typeArgument), declarations);
                }

                if (declarations.Length != 0)
                {
                    return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, declarations, null /*reference*/, Feature.TypeDefinition);
                }

                if ((symbol.Flags & SymbolFlags.Value) == 0 && (symbol.Flags & SymbolFlags.Type) != 0)
                {
                    return CreateDefinitionLocations(originSelectionRange, clientSupportsLink, symbol.Declarations ?? [], null /*reference*/, Feature.TypeDefinition);
                }
            }
        }
        finally
        {
            done();
        }

        return new LocationOrLocationsOrDefinitionLinksOrNull();
    }

    /// <summary>Go: <c>func (l *LanguageService) createDefinitionLocations(originSelectionRange, clientSupportsLink, declarations, reference, feature) lsproto.DefinitionResponse</c>（definition.go:232-292）。</summary>
    internal LocationOrLocationsOrDefinitionLinksOrNull CreateDefinitionLocations(
        Range originSelectionRange,
        bool clientSupportsLink,
        Node[] declarations,
        RefInfo? reference,
        Feature feature)
    {
        var locations = new List<LocationLink>();
        var locationRanges = new Set<FileRange>();

        if (reference is not null)
        {
            var targetRange = new Range
            {
                Start = new Position { Line = 0, Character = 0 },
                End = new Position { Line = 0, Character = 0 },
            };
            locations.Add(new LocationLink
            {
                OriginSelectionRange = originSelectionRange,
                TargetUri = FileNameToDocumentURI(reference._fileName),
                TargetRange = targetRange,
                TargetSelectionRange = targetRange,
            });
        }

        foreach (var decl in declarations)
        {
            var file = GetSourceFileOfNode(decl)!;
            var name = OrElse(GetNameOfDeclaration(decl), decl)!;
            TextRange nameRange;
            if (name.Kind == Kind.EmptyStatement)
            {
                nameRange = NewTextRange(name.Pos(), name.Pos());
            }
            else
            {
                nameRange = Ls.CreateRangeFromNode(name, file);
            }

            if (locationRanges.AddIfAbsent(new FileRange(file, nameRange)))
            {
                var contextNode = OrElse(Ls.GetContextNode(decl), decl);
                var contextRange = OrElse(Ls.ToContextRange(nameRange, file, contextNode), nameRange) ?? nameRange;
                if (!nameRange.ContainedBy(contextRange))
                {
                    contextRange = NewTextRange(
                        Math.Min(nameRange.Pos(), contextRange.Pos()),
                        Math.Max(nameRange.End(), contextRange.End()));
                }

                var (targetSelectionLoc, selectionFidelity) = SourceFileRangeToLSPLocationForFeature(file, nameRange, feature);
                if (!selectionFidelity.IsSingleSegment())
                {
                    continue;
                }

                var (targetLoc, contextFidelity) = SourceFileRangeToLSPLocation(file, contextRange);
                if (contextFidelity.IsNone() || targetLoc.Uri != targetSelectionLoc.Uri || !Ls.LspRangeContains(targetLoc.Range, targetSelectionLoc.Range))
                {
                    targetLoc = targetSelectionLoc;
                }

                locations.Add(new LocationLink
                {
                    OriginSelectionRange = originSelectionRange,
                    TargetSelectionRange = targetSelectionLoc.Range,
                    TargetUri = targetLoc.Uri,
                    TargetRange = targetLoc.Range,
                });
            }
        }

        if (clientSupportsLink)
        {
            return new LocationOrLocationsOrDefinitionLinksOrNull { DefinitionLinks = locations.ToArray() };
        }

        return Ls.CreateLocationsFromLinks(locations);
    }

    /// <summary>Go: <c>func (l *LanguageService) createLocationFromFileAndRange(file, textRange, feature) lsproto.DefinitionResponse</c>（definition.go:300-311）。</summary>
    internal LocationOrLocationsOrDefinitionLinksOrNull CreateLocationFromFileAndRange(
        SourceFile file,
        TextRange textRange,
        Feature feature)
    {
        var (mappedLocation, fidelity) = SourceFileRangeToLSPLocationForFeature(file, textRange, feature);
        if (fidelity.IsNone())
        {
            mappedLocation.Range = new Range();
        }

        return new LocationOrLocationsOrDefinitionLinksOrNull
        {
            Location = mappedLocation,
        };
    }
}

public static partial class Ls
{
    /// <summary>Go: <c>func combineDefinitionResponses(results []lsproto.DefinitionResponse, links bool) lsproto.DefinitionResponse</c>（definition.go:157-179）。</summary>
    internal static LocationOrLocationsOrDefinitionLinksOrNull CombineDefinitionResponses(
        List<LocationOrLocationsOrDefinitionLinksOrNull> results,
        bool links)
    {
        var locations = new List<Location>();
        var definitionLinks = new List<LocationLink>();
        var seen = new Set<LocationKey>();
        foreach (var result in results)
        {
            if (result.DefinitionLinks is not null)
            {
                foreach (var link in result.DefinitionLinks)
                {
                    if (link is null)
                    {
                        continue;
                    }

                    var location = new Location { Uri = link.TargetUri, Range = link.TargetSelectionRange };
                    if (seen.AddIfAbsent(new LocationKey(location)))
                    {
                        definitionLinks.Add(link);
                        locations.Add(location);
                    }
                }
            }

            if (result.Location is not null && seen.AddIfAbsent(new LocationKey(result.Location)))
            {
                locations.Add(result.Location);
                definitionLinks.Add(new LocationLink
                {
                    TargetUri = result.Location.Uri,
                    TargetRange = result.Location.Range,
                    TargetSelectionRange = result.Location.Range,
                });
            }

            if (result.Locations is not null)
            {
                foreach (var location in result.Locations)
                {
                    if (location is null)
                    {
                        continue;
                    }

                    if (seen.AddIfAbsent(new LocationKey(location)))
                    {
                        locations.Add(location);
                        definitionLinks.Add(new LocationLink
                        {
                            TargetUri = location.Uri,
                            TargetRange = location.Range,
                            TargetSelectionRange = location.Range,
                        });
                    }
                }
            }
        }

        // Go 的 `&locations`（指向 nil 切片的非 nil 指针）在 JSON 上就是 `null`，所以空列表要落成 null（见文件头第 3 条）。
        if (links)
        {
            return new LocationOrLocationsOrDefinitionLinksOrNull
            {
                DefinitionLinks = definitionLinks.Count == 0 ? null : definitionLinks.ToArray(),
            };
        }

        return new LocationOrLocationsOrDefinitionLinksOrNull
        {
            Locations = locations.Count == 0 ? null : locations.ToArray(),
        };
    }

    /// <summary>Go: <c>func getDeclarationNameForKeyword(node *ast.Node) *ast.Node</c>（definition.go:181-192）。</summary>
    internal static Node GetDeclarationNameForKeyword(Node node)
    {
        if (node.Kind >= Kind.FirstKeyword && node.Kind <= Kind.LastKeyword)
        {
            if (IsVariableDeclarationList(node.Parent!))
            {
                var decl = FirstOrNil(node.Parent!.AsVariableDeclarationList()!.Declarations!.Nodes);
                if (decl is not null && decl.Name() is not null)
                {
                    return decl.Name()!;
                }
            }
            else if (node.Parent!.DeclarationData() is not null && node.Parent.Name() is not null && node.Pos() < node.Parent.Name()!.Pos())
            {
                return node.Parent.Name()!;
            }
        }

        return node;
    }

    /// <summary>Go: <c>func lspRangeContains(outer, inner lsproto.Range) bool</c>（definition.go:294-297）。</summary>
    internal static bool LspRangeContains(Range outer, Range inner)
    {
        return ComparePositions(outer.Start, inner.Start) <= 0 &&
            ComparePositions(inner.End, outer.End) <= 0;
    }

    /// <summary>Go: <c>func createLocationsFromLinks(links []*lsproto.LocationLink) lsproto.DefinitionResponse</c>（definition.go:299-... 见文件头第 3 条：Go 的 nil 切片在 JSON 上即 null）。</summary>
    internal static LocationOrLocationsOrDefinitionLinksOrNull CreateLocationsFromLinks(List<LocationLink> links)
    {
        // Go: core.Map(links, …)。调用点（createDefinitionLocations）传进来的 links 恒为**非 nil** 切片（`make(…, 0)`），
        // 所以空的 links 在 Go 里会得到非 nil 的空 slice（JSON `[]`），这里逐字保留（用 ToArray 而非三元判空）。
        var locations = new Location[links.Count];
        for (var i = 0; i < links.Count; i++)
        {
            locations[i] = new Location
            {
                Uri = links[i].TargetUri,
                Range = links[i].TargetSelectionRange,
            };
        }

        return new LocationOrLocationsOrDefinitionLinksOrNull { Locations = locations };
    }

    /// <summary>Go: <c>func getDeclarationsFromLocation(c *checker.Checker, node *ast.Node) []*ast.Node</c>（definition.go:313-367）。</summary>
    internal static Node[] GetDeclarationsFromLocation(Checker.Checker c, Node node)
    {
        if (IsIdentifier(node) && IsShorthandPropertyAssignment(node.Parent!))
        {
            // Because name in short-hand property assignment has two different meanings: property name and property value,
            // using go-to-definition at such position should go to the variable declaration of the property value rather than
            // go to the declaration of the property name (in this case stay at the same position). However, if go-to-definition
            // is performed at the location of property access, we would like to go to definition of the property in the short-hand
            // assignment. This case and others are handled by the following code.
            // and the contextual type's property declarations
            var shorthandSymbol = c.GetResolvedSymbol(node);
            var declarations = shorthandSymbol?.Declarations ?? [];
            var contextualDeclarations = GetDeclarationsFromObjectLiteralElement(c, node);
            return Concatenate(declarations, contextualDeclarations);
        }

        if (IsPropertyName(node) && IsBindingElement(node.Parent!) && IsObjectBindingPattern(node.Parent!.Parent!))
        {
            // If the node is the name of a BindingElement within an ObjectBindingPattern instead of just returning the
            // declaration of the symbol (which is itself), we should try to get to the original type of the
            // ObjectBindingPattern and return the property declaration for the referenced property.
            // For example:
            //      import('./foo').then(({ bar }) => undefined); => should navigate to the declaration in file "./foo"
            //
            //      function bar<T>(onfulfilled: (value: T) => void) { }
            //      interface Test { prop1: number }
            //      bar<Test>(({ prop1 }) => {});  => should navigate to prop1 in Test
            var bindingEl = node.Parent.AsBindingElement()!;
            if (bindingEl.DotDotDotToken is null && node == OrElse(bindingEl.PropertyName, node.Parent.Name()))
            {
                if (TryGetTextOfPropertyName(node, out var name))
                {
                    var t = c.GetTypeAtLocation(node.Parent.Parent!);
                    var types = t.IsUnion() ? t.Types() : [t];
                    var result = new List<Node>();
                    foreach (var unionType in types)
                    {
                        if (c.GetPropertyOfType(unionType, name) is { } prop)
                        {
                            result.AddRange(prop.Declarations ?? []);
                        }
                    }

                    return result.ToArray();
                }
            }
        }

        node = GetDeclarationNameForKeyword(node);
        if (c.GetSymbolAtLocation(node) is { } symbol)
        {
            if ((symbol.Flags & SymbolFlags.Class) != 0
                && (symbol.Flags & (SymbolFlags.Function | SymbolFlags.Variable)) == 0
                && node.Kind == Kind.ConstructorKeyword)
            {
                if (symbol.Members is not null && symbol.Members.TryGetValue(InternalSymbolNameConstructor, out var constructor))
                {
                    symbol = constructor;
                }
            }

            if ((symbol.Flags & SymbolFlags.Alias) != 0)
            {
                if (c.ResolveAlias(symbol, out var resolved))
                {
                    symbol = resolved!;
                }
            }

            var objectLiteralElementDeclarations = GetDeclarationsFromObjectLiteralElement(c, node);
            if (objectLiteralElementDeclarations.Length > 0)
            {
                return objectLiteralElementDeclarations;
            }

            if ((symbol.Declarations?.Length ?? 0) > 0)
            {
                return symbol.Declarations!;
            }
        }

        if (c.GetIndexSignaturesAtLocation(node) is { Length: > 0 } indexInfos)
        {
            return indexInfos;
        }

        return [];
    }

    /// <summary>Go: <c>func getDeclarationsFromObjectLiteralElement(c *checker.Checker, node *ast.Node) []*ast.Node</c>（definition.go:369-397）。</summary>
    internal static Node[] GetDeclarationsFromObjectLiteralElement(Checker.Checker c, Node node)
    {
        var element = GetContainingObjectLiteralElement(node);
        if (element is null)
        {
            return [];
        }

        var contextualType = c.GetContextualType(element.Parent!, ContextFlags.None);
        if (contextualType is null)
        {
            return [];
        }

        var properties = c.GetPropertySymbolsFromContextualType(element, contextualType, false /*unionSymbolOk*/);
        if (Some(properties, p =>
            p.ValueDeclaration is not null
            && IsObjectLiteralExpression(p.ValueDeclaration.Parent!)
            && IsObjectLiteralElement(p.ValueDeclaration)
            && p.ValueDeclaration.Name() == node))
        {
            if (c.GetContextualType(element.Parent!, ContextFlags.IgnoreNodeInferences) is { } withoutNodeInferencesType)
            {
                var withoutNodeInferencesProperties = c.GetPropertySymbolsFromContextualType(element, withoutNodeInferencesType, false /*unionSymbolOk*/);
                if (withoutNodeInferencesProperties.Length > 0)
                {
                    properties = withoutNodeInferencesProperties;
                }
            }
        }

        var result = new List<Node>();
        foreach (var prop in properties)
        {
            result.AddRange(prop.Declarations ?? []);
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func getAncestorCallLikeExpression(node *ast.Node) *ast.Node</c>（definition.go:400-408）。</summary>
    internal static Node? GetAncestorCallLikeExpression(Node node)
    {
        var target = FindAncestor(node, n => !IsRightSideOfPropertyAccess(n))!;
        var callLike = target.Parent;
        if (callLike is not null && IsCallLikeExpression(callLike) && GetInvokedExpression(callLike) == target)
        {
            return callLike;
        }

        return null;
    }

    /// <summary>Go: <c>func tryGetSignatureDeclaration(typeChecker *checker.Checker, node *ast.Node) *ast.Node</c>（definition.go:410-425）。</summary>
    internal static Node? TryGetSignatureDeclaration(Checker.Checker typeChecker, Node node)
    {
        Signature? signature = null;
        if (GetAncestorCallLikeExpression(node) is { } callLike)
        {
            signature = typeChecker.GetResolvedSignature(callLike);
        }

        // Don't go to a function type, go to the value having that type.
        if (signature is not null && signature.Declaration() is { } declaration
            && IsFunctionLike(declaration) && !IsFunctionTypeNode(declaration))
        {
            return declaration;
        }

        return null;
    }

    /// <summary>Go: <c>func isJsxConstructorLike(node *ast.Node) bool</c>（definition.go:427-437）。</summary>
    internal static bool IsJsxConstructorLike(Node node)
    {
        return IsConstructorDeclaration(node)
            || IsConstructorTypeNode(node)
            || IsCallSignatureDeclaration(node)
            || IsConstructSignatureDeclaration(node);
    }

    /// <summary>Go: <c>func symbolMatchesSignature(symbol *ast.Symbol, calledDeclaration *ast.Node) bool</c>（definition.go:439-450）。</summary>
    internal static bool SymbolMatchesSignature(Symbol? symbol, Node? calledDeclaration)
    {
        if (symbol is null || calledDeclaration is null)
        {
            return false;
        }

        var calledSymbol = calledDeclaration.Symbol;
        if (symbol == calledSymbol || calledSymbol is not null && symbol == calledSymbol.Parent)
        {
            return true;
        }

        var parent = calledDeclaration.Parent;
        return parent is not null
            && (IsAssignmentExpression(parent, false /*excludeCompoundAssignment*/)
                || !IsCallLikeExpression(parent) && CanHaveSymbol(parent) && symbol == parent.Symbol);
    }

    /// <summary>Go: <c>func getSymbolForOverriddenMember(typeChecker *checker.Checker, node *ast.Node) *ast.Symbol</c>（definition.go:452-479）。</summary>
    internal static Symbol? GetSymbolForOverriddenMember(Checker.Checker typeChecker, Node node)
    {
        var classElement = FindAncestor(node, IsClassElement);
        if (classElement is null || classElement.Name() is null)
        {
            return null;
        }

        var baseDeclaration = FindAncestor(classElement, IsClassLike);
        if (baseDeclaration is null)
        {
            return null;
        }

        var baseTypeNode = GetClassExtendsHeritageElement(baseDeclaration);
        if (baseTypeNode is null)
        {
            return null;
        }

        var expression = SkipParentheses(baseTypeNode.Expression());
        Symbol? @base;
        if (expression is not null && IsClassExpression(expression))
        {
            @base = expression.Symbol;
        }
        else
        {
            @base = typeChecker.GetSymbolAtLocation(expression!);
        }

        if (@base is null)
        {
            return null;
        }

        var name = GetTextOfPropertyName(classElement.Name());
        if (HasStaticModifier(classElement))
        {
            return typeChecker.GetPropertyOfType(typeChecker.GetTypeOfSymbol(@base), name);
        }

        return typeChecker.GetPropertyOfType(typeChecker.GetDeclaredTypeOfSymbol(@base), name);
    }

    /// <summary>Go: <c>func getTypeOfSymbolAtLocation(c *checker.Checker, symbol *ast.Symbol, node *ast.Node) *checker.Type</c>（definition.go:481-492）。</summary>
    internal static Type GetTypeOfSymbolAtLocation(Checker.Checker c, Symbol symbol, Node node)
    {
        var t = c.GetTypeOfSymbolAtLocation(symbol, node);
        // If the type is just a function's inferred type, go-to-type should go to the return type instead since
        // go-to-definition takes you to the function anyway.
        if (t.Symbol() == symbol
            || t.Symbol() is not null
                && symbol.ValueDeclaration is not null
                && IsVariableDeclaration(symbol.ValueDeclaration)
                && symbol.ValueDeclaration.Initializer() == t.Symbol()!.ValueDeclaration)
        {
            var sigs = c.GetCallSignatures(t);
            if (sigs.Length == 1)
            {
                return c.GetReturnTypeOfSignature(sigs[0]);
            }
        }

        return t;
    }

    /// <summary>Go: <c>func getDeclarationsFromType(t *checker.Type) []*ast.Node</c>（definition.go:494-504）。</summary>
    internal static Node[] GetDeclarationsFromType(Type t)
    {
        var result = Array.Empty<Node>();
        foreach (var distributed in t.Distributed())
        {
            if (distributed.Symbol() is not null)
            {
                foreach (var decl in distributed.Symbol()!.Declarations ?? [])
                {
                    result = AppendIfUnique(result, decl);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Go: <c>func getContextNode(node *ast.Node) *ast.Node</c>（**findallreferences.go:286-317**，见文件头第 9 条的借用备案）。
    /// </summary>
    internal static Node? GetContextNode(Node? node)
    {
        if (node is null)
        {
            return null;
        }

        switch (node.Kind)
        {
            case Kind.VariableDeclaration:
                if (!IsVariableDeclarationList(node.Parent!) || node.Parent!.AsVariableDeclarationList()!.Declarations!.Nodes!.Length != 1)
                {
                    return node;
                }
                else if (IsVariableStatement(node.Parent.Parent!))
                {
                    return node.Parent.Parent;
                }
                else if (IsForInOrOfStatement(node.Parent.Parent))
                {
                    return GetContextNode(node.Parent.Parent);
                }

                return node.Parent;

            case Kind.BindingElement:
                return GetContextNode(node.Parent!.Parent!);

            case Kind.ImportSpecifier:
                return node.Parent!.Parent!.Parent;

            case Kind.ExportSpecifier:
            case Kind.NamespaceImport:
                return node.Parent!.Parent;

            case Kind.ImportClause:
            case Kind.NamespaceExport:
                return node.Parent;

            case Kind.BinaryExpression:
                return IfElse(node.Parent!.Kind == Kind.ExpressionStatement, node.Parent, node);

            case Kind.ForOfStatement:
            case Kind.ForInStatement:
                // !!! not implemented
                return null;

            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
                if (IsArrayLiteralOrObjectLiteralDestructuringPattern(node.Parent!))
                {
                    return GetContextNode(FindAncestor(node.Parent, n => n.Kind == Kind.BinaryExpression || IsForInOrOfStatement(n)));
                }

                return node;
            case Kind.SwitchStatement:
                // !!! not implemented
                return null;
            default:
                return node;
        }
    }
}

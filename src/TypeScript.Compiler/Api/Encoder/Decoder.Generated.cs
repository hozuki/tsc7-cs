// ported from: internal/api/encoder/decoder_generated.go:1-1141
// （Go 侧是 `tools/scripts/tsc/generate-encoder.ts` 的生成物；C# 侧按 encoder_generated.go 的同样处理方式机械转写，不做手工重排。）
//
// 与 Go 的偏离（逐条说明）：
// 1. 方法归属：Go 的 `func (d *astDecoder) createStringNode / createExtendedNode / createChildrenNode` 继续落在
//    Decoder.cs 声明的 `internal sealed partial class AstDecoder` 上（本文件是它的另一半）。
// 2. 错误处理：Go 的 `(T, error)` + `fmt.Errorf` → 抛 `DebugFailureException`，方法不再有 error 返回值。
// 3. `d.factory.X(...)` → `_factory.X(...)`；`d.nodeAt / d.nodeListAt / d.modifierListAt / d.singleChild /
//    d.singleNodeListChild / d.allocNodeSlice` → `NodeAt / NodeListAt / ModifierListAt / SingleChild /
//    SingleNodeListChild / AllocNodeSlice`（同一类的实例方法，Go 里的 `d.` 前缀无需保留）。
// 4. `it := newChildIter(childIndices)` → `var it = ChildIterator.NewChildIter(childIndices);`；`childIndices` 的类型由 `[]int`
//    变为 `List<int>`（见 Decoder.cs 偏离 4），`len(childIndices) > 0` → `childIndices.Count > 0`。
// 5. Kind 常量：`ast.KindXxx` → `Kind.Xxx`（本仓枚举去前缀约定，见 Ast/Kind.Generated.cs）。
// 6. C# 的 switch 各 section 共享作用域，Go 的每个 case 各自有作用域；因此凡是声明了局部变量的 case 体
//    一律用 `{ }` 包住（纯 `return` 的 case 不包），保证与 Go 同名的局部变量（it/modifiers/name/…）互不冲突。
// 7. Go 的 `case A, B:` 多值标签 → C# 的堆叠 `case Kind.A:` `case Kind.B:`。
// 8. `operator` 在 C# 是关键字 → 用逐字标识符 `@operator` 保留 Go 的变量名；`var operator ast.Kind` 的 Go 零值
//    （KindUnknown = 0）在 C# 用 `= default` 显式写出（否则 C# 的确定性赋值规则会因 switch 覆盖不全而报 CS0165）。
// 9. `decodeNodeCommonData_SyntheticExpression` 在 Go 是包级函数 → `Decoder.DecodeNodeCommonDataSyntheticExpression`。
// 10. `KindSyntaxList` / `KindJSDocTypeLiteral` 分支的 `nodes[i] = d.nodes[ci]`：Go 里 allocNodeSlice 返回的切片
//     len=0（对非空 childIndices 会 panic），C# 按该分支的明显意图实现为按下标写入（见 Decoder.cs 偏离 6）。
//     `d.nodes[ci]` 是 `Node?`，Go 直接解引用，故此处用 `!`。
#nullable enable
using TypeScript.Ast;

namespace TypeScript.Api.Encoder;

internal sealed partial class AstDecoder
{
    /// <summary>Go: <c>func (d *astDecoder) createStringNode(kind ast.Kind, data uint32, commonData uint8) (*ast.Node, error)</c>（decoder_generated.go:11-34）。</summary>
    internal Node CreateStringNode(Kind kind, uint data, byte commonData)
    {
        var strIdx = data & Encoder.NodeDataStringIndexMask;
        var text = GetString(strIdx);

        switch (kind)
        {
            case Kind.Identifier:
                return _factory.NewIdentifier(text);
            case Kind.PrivateIdentifier:
                return _factory.NewPrivateIdentifier(text);
            case Kind.JsxText:
            {
                var containsOnlyTriviaWhiteSpaces = (commonData & 1) != 0;
                return _factory.NewJsxText(text, containsOnlyTriviaWhiteSpaces);
            }
            case Kind.JSDocText:
                return _factory.NewJSDocText(new[] { text });
            case Kind.JSDocLink:
                return _factory.NewJSDocLink(null, new[] { text });
            case Kind.JSDocLinkPlain:
                return _factory.NewJSDocLinkPlain(null, new[] { text });
            case Kind.JSDocLinkCode:
                return _factory.NewJSDocLinkCode(null, new[] { text });
            default:
                throw new DebugFailureException($"unknown string node kind {kind}");
        }
    }

    /// <summary>Go: <c>func (d *astDecoder) createExtendedNode(kind ast.Kind, data uint32, childIndices []int, commonData uint8) (*ast.Node, error)</c>（decoder_generated.go:36-59）。</summary>
    internal Node CreateExtendedNode(Kind kind, uint data, List<int> childIndices, byte commonData)
    {
        switch (kind)
        {
            case Kind.StringLiteral:
                return DecodeExtendedDataStringLiteral(data, childIndices, commonData);
            case Kind.NumericLiteral:
                return DecodeExtendedDataNumericLiteral(data, childIndices, commonData);
            case Kind.BigIntLiteral:
                return DecodeExtendedDataBigIntLiteral(data, childIndices, commonData);
            case Kind.RegularExpressionLiteral:
                return DecodeExtendedDataRegularExpressionLiteral(data, childIndices, commonData);
            case Kind.NoSubstitutionTemplateLiteral:
                return DecodeExtendedDataNoSubstitutionTemplateLiteral(data, childIndices, commonData);
            case Kind.TemplateHead:
                return DecodeExtendedDataTemplateHead(data, childIndices, commonData);
            case Kind.TemplateMiddle:
                return DecodeExtendedDataTemplateMiddle(data, childIndices, commonData);
            case Kind.TemplateTail:
                return DecodeExtendedDataTemplateTail(data, childIndices, commonData);
            case Kind.SourceFile:
                return DecodeExtendedDataSourceFile(data, childIndices, commonData);
            default:
                throw new DebugFailureException($"unknown extended data node kind {kind}");
        }
    }

    /// <summary>Go: <c>func (d *astDecoder) createChildrenNode(kind ast.Kind, data uint32, childIndices []int, commonData uint8) (*ast.Node, error)</c>（decoder_generated.go:61-1141）。</summary>
    internal Node CreateChildrenNode(Kind kind, uint data, List<int> childIndices, byte commonData)
    {
        var mask = (byte)(data & Encoder.NodeDataChildMask);

        switch (kind)
        {
            case Kind.Unknown:
            case Kind.EndOfFile:
            case Kind.SingleLineCommentTrivia:
            case Kind.MultiLineCommentTrivia:
            case Kind.NewLineTrivia:
            case Kind.WhitespaceTrivia:
            case Kind.ConflictMarkerTrivia:
            case Kind.NonTextFileMarkerTrivia:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.StringLiteral:
            case Kind.JsxText:
            case Kind.JsxTextAllWhiteSpaces:
            case Kind.RegularExpressionLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateHead:
            case Kind.TemplateMiddle:
            case Kind.TemplateTail:
            case Kind.OpenBraceToken:
            case Kind.CloseBraceToken:
            case Kind.OpenParenToken:
            case Kind.CloseParenToken:
            case Kind.OpenBracketToken:
            case Kind.CloseBracketToken:
            case Kind.DotToken:
            case Kind.DotDotDotToken:
            case Kind.SemicolonToken:
            case Kind.CommaToken:
            case Kind.QuestionDotToken:
            case Kind.LessThanToken:
            case Kind.LessThanSlashToken:
            case Kind.GreaterThanToken:
            case Kind.LessThanEqualsToken:
            case Kind.GreaterThanEqualsToken:
            case Kind.EqualsEqualsToken:
            case Kind.ExclamationEqualsToken:
            case Kind.EqualsEqualsEqualsToken:
            case Kind.ExclamationEqualsEqualsToken:
            case Kind.EqualsGreaterThanToken:
            case Kind.PlusToken:
            case Kind.MinusToken:
            case Kind.AsteriskToken:
            case Kind.AsteriskAsteriskToken:
            case Kind.SlashToken:
            case Kind.PercentToken:
            case Kind.PlusPlusToken:
            case Kind.MinusMinusToken:
            case Kind.LessThanLessThanToken:
            case Kind.GreaterThanGreaterThanToken:
            case Kind.GreaterThanGreaterThanGreaterThanToken:
            case Kind.AmpersandToken:
            case Kind.BarToken:
            case Kind.CaretToken:
            case Kind.ExclamationToken:
            case Kind.TildeToken:
            case Kind.AmpersandAmpersandToken:
            case Kind.BarBarToken:
            case Kind.QuestionToken:
            case Kind.ColonToken:
            case Kind.AtToken:
            case Kind.QuestionQuestionToken:
            case Kind.BacktickToken:
            case Kind.HashToken:
            case Kind.EqualsToken:
            case Kind.PlusEqualsToken:
            case Kind.MinusEqualsToken:
            case Kind.AsteriskEqualsToken:
            case Kind.AsteriskAsteriskEqualsToken:
            case Kind.SlashEqualsToken:
            case Kind.PercentEqualsToken:
            case Kind.LessThanLessThanEqualsToken:
            case Kind.GreaterThanGreaterThanEqualsToken:
            case Kind.GreaterThanGreaterThanGreaterThanEqualsToken:
            case Kind.AmpersandEqualsToken:
            case Kind.BarEqualsToken:
            case Kind.BarBarEqualsToken:
            case Kind.AmpersandAmpersandEqualsToken:
            case Kind.QuestionQuestionEqualsToken:
            case Kind.CaretEqualsToken:
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
            case Kind.JSDocCommentTextToken:
            case Kind.BreakKeyword:
            case Kind.CaseKeyword:
            case Kind.CatchKeyword:
            case Kind.ClassKeyword:
            case Kind.ConstKeyword:
            case Kind.ContinueKeyword:
            case Kind.DebuggerKeyword:
            case Kind.DefaultKeyword:
            case Kind.DeleteKeyword:
            case Kind.DoKeyword:
            case Kind.ElseKeyword:
            case Kind.EnumKeyword:
            case Kind.ExportKeyword:
            case Kind.ExtendsKeyword:
            case Kind.FinallyKeyword:
            case Kind.ForKeyword:
            case Kind.FunctionKeyword:
            case Kind.IfKeyword:
            case Kind.InKeyword:
            case Kind.InstanceOfKeyword:
            case Kind.NewKeyword:
            case Kind.ReturnKeyword:
            case Kind.SwitchKeyword:
            case Kind.ThrowKeyword:
            case Kind.TryKeyword:
            case Kind.TypeOfKeyword:
            case Kind.VarKeyword:
            case Kind.WhileKeyword:
            case Kind.WithKeyword:
            case Kind.ImplementsKeyword:
            case Kind.InterfaceKeyword:
            case Kind.LetKeyword:
            case Kind.PackageKeyword:
            case Kind.PrivateKeyword:
            case Kind.ProtectedKeyword:
            case Kind.PublicKeyword:
            case Kind.StaticKeyword:
            case Kind.YieldKeyword:
            case Kind.AbstractKeyword:
            case Kind.AccessorKeyword:
            case Kind.AsKeyword:
            case Kind.AssertsKeyword:
            case Kind.AssertKeyword:
            case Kind.AsyncKeyword:
            case Kind.AwaitKeyword:
            case Kind.ConstructorKeyword:
            case Kind.DeclareKeyword:
            case Kind.GetKeyword:
            case Kind.ImmediateKeyword:
            case Kind.InferKeyword:
            case Kind.IsKeyword:
            case Kind.KeyOfKeyword:
            case Kind.ModuleKeyword:
            case Kind.NamespaceKeyword:
            case Kind.OutKeyword:
            case Kind.ReadonlyKeyword:
            case Kind.RequireKeyword:
            case Kind.SatisfiesKeyword:
            case Kind.SetKeyword:
            case Kind.TypeKeyword:
            case Kind.UniqueKeyword:
            case Kind.UsingKeyword:
            case Kind.FromKeyword:
            case Kind.GlobalKeyword:
            case Kind.OverrideKeyword:
            case Kind.OfKeyword:
            case Kind.DeferKeyword:
                return _factory.NewToken(kind);
            case Kind.QualifiedName:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var left = NodeAt(it.NextIf(mask, 0));
                var right = NodeAt(it.NextIf(mask, 1));
                return _factory.NewQualifiedName(left, right);
            }
            case Kind.ComputedPropertyName:
                return _factory.NewComputedPropertyName(SingleChild(childIndices));
            case Kind.Decorator:
                return _factory.NewDecorator(SingleChild(childIndices));
            case Kind.EmptyStatement:
                return _factory.NewEmptyStatement();
            case Kind.IfStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var thenStatement = NodeAt(it.NextIf(mask, 1));
                var elseStatement = NodeAt(it.NextIf(mask, 2));
                return _factory.NewIfStatement(expression, thenStatement, elseStatement);
            }
            case Kind.DoStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var statement = NodeAt(it.NextIf(mask, 0));
                var expression = NodeAt(it.NextIf(mask, 1));
                return _factory.NewDoStatement(statement, expression);
            }
            case Kind.WhileStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var statement = NodeAt(it.NextIf(mask, 1));
                return _factory.NewWhileStatement(expression, statement);
            }
            case Kind.ForStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var initializer = NodeAt(it.NextIf(mask, 0));
                var condition = NodeAt(it.NextIf(mask, 1));
                var incrementor = NodeAt(it.NextIf(mask, 2));
                var statement = NodeAt(it.NextIf(mask, 3));
                return _factory.NewForStatement(initializer, condition, incrementor, statement);
            }
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var awaitModifier = NodeAt(it.NextIf(mask, 0));
                var initializer = NodeAt(it.NextIf(mask, 1));
                var expression = NodeAt(it.NextIf(mask, 2));
                var statement = NodeAt(it.NextIf(mask, 3));
                return _factory.NewForInOrOfStatement(kind, awaitModifier, initializer, expression, statement);
            }
            case Kind.BreakStatement:
                return _factory.NewBreakStatement(SingleChild(childIndices));
            case Kind.ContinueStatement:
                return _factory.NewContinueStatement(SingleChild(childIndices));
            case Kind.ReturnStatement:
                return _factory.NewReturnStatement(SingleChild(childIndices));
            case Kind.WithStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var statement = NodeAt(it.NextIf(mask, 1));
                return _factory.NewWithStatement(expression, statement);
            }
            case Kind.SwitchStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var caseBlock = NodeAt(it.NextIf(mask, 1));
                return _factory.NewSwitchStatement(expression, caseBlock);
            }
            case Kind.CaseBlock:
                return _factory.NewCaseBlock(SingleNodeListChild(childIndices));
            case Kind.CaseClause:
            case Kind.DefaultClause:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var statements = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewCaseOrDefaultClause(kind, expression, statements);
            }
            case Kind.ThrowStatement:
                return _factory.NewThrowStatement(SingleChild(childIndices));
            case Kind.TryStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tryBlock = NodeAt(it.NextIf(mask, 0));
                var catchClause = NodeAt(it.NextIf(mask, 1));
                var finallyBlock = NodeAt(it.NextIf(mask, 2));
                return _factory.NewTryStatement(tryBlock, catchClause, finallyBlock);
            }
            case Kind.CatchClause:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var variableDeclaration = NodeAt(it.NextIf(mask, 0));
                var block = NodeAt(it.NextIf(mask, 1));
                return _factory.NewCatchClause(variableDeclaration, block);
            }
            case Kind.DebuggerStatement:
                return _factory.NewDebuggerStatement();
            case Kind.LabeledStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var label = NodeAt(it.NextIf(mask, 0));
                var statement = NodeAt(it.NextIf(mask, 1));
                return _factory.NewLabeledStatement(label, statement);
            }
            case Kind.ExpressionStatement:
                return _factory.NewExpressionStatement(SingleChild(childIndices));
            case Kind.Block:
            {
                var multiLine = (commonData & 1) != 0;
                NodeList? list = null;
                if (childIndices.Count > 0)
                {
                    list = NodeListAt(childIndices[0]);
                }

                return _factory.NewBlock(list, multiLine);
            }
            case Kind.VariableStatement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var declarationList = NodeAt(it.NextIf(mask, 1));
                return _factory.NewVariableStatement(modifiers, declarationList);
            }
            case Kind.VariableDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var name = NodeAt(it.NextIf(mask, 0));
                var exclamationToken = NodeAt(it.NextIf(mask, 1));
                var typeNode = NodeAt(it.NextIf(mask, 2));
                var initializer = NodeAt(it.NextIf(mask, 3));
                return _factory.NewVariableDeclaration(name, exclamationToken, typeNode, initializer);
            }
            case Kind.VariableDeclarationList:
                return _factory.NewVariableDeclarationList(SingleNodeListChild(childIndices), 0);
            case Kind.ObjectBindingPattern:
            case Kind.ArrayBindingPattern:
                return _factory.NewBindingPattern(kind, SingleNodeListChild(childIndices));
            case Kind.Parameter:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var dotDotDotToken = NodeAt(it.NextIf(mask, 1));
                var name = NodeAt(it.NextIf(mask, 2));
                var questionToken = NodeAt(it.NextIf(mask, 3));
                var typeNode = NodeAt(it.NextIf(mask, 4));
                var initializer = NodeAt(it.NextIf(mask, 5));
                return _factory.NewParameterDeclaration(modifiers, dotDotDotToken, name, questionToken, typeNode, initializer);
            }
            case Kind.BindingElement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var dotDotDotToken = NodeAt(it.NextIf(mask, 0));
                var propertyName = NodeAt(it.NextIf(mask, 1));
                var name = NodeAt(it.NextIf(mask, 2));
                var initializer = NodeAt(it.NextIf(mask, 3));
                return _factory.NewBindingElement(dotDotDotToken, propertyName, name, initializer);
            }
            case Kind.MissingDeclaration:
            {
                ModifierList? mods = null;
                if (childIndices.Count > 0)
                {
                    mods = ModifierListAt(childIndices[0]);
                }

                return _factory.NewMissingDeclaration(mods);
            }
            case Kind.FunctionDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var asteriskToken = NodeAt(it.NextIf(mask, 1));
                var name = NodeAt(it.NextIf(mask, 2));
                var typeParameters = NodeListAt(it.NextIf(mask, 3));
                var parameters = NodeListAt(it.NextIf(mask, 4));
                var typeNode = NodeAt(it.NextIf(mask, 5));
                var body = NodeAt(it.NextIf(mask, 6));
                return _factory.NewFunctionDeclaration(modifiers, asteriskToken, name, typeParameters, parameters, typeNode, null, body);
            }
            case Kind.ClassDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var typeParameters = NodeListAt(it.NextIf(mask, 2));
                var heritageClauses = NodeListAt(it.NextIf(mask, 3));
                var members = NodeListAt(it.NextIf(mask, 4));
                return _factory.NewClassDeclaration(modifiers, name, typeParameters, heritageClauses, members);
            }
            case Kind.ClassExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var typeParameters = NodeListAt(it.NextIf(mask, 2));
                var heritageClauses = NodeListAt(it.NextIf(mask, 3));
                var members = NodeListAt(it.NextIf(mask, 4));
                return _factory.NewClassExpression(modifiers, name, typeParameters, heritageClauses, members);
            }
            case Kind.HeritageClause:
            {
                var token = Kind.ExtendsKeyword;
                if ((commonData & 1) != 0)
                {
                    token = Kind.ImplementsKeyword;
                }

                return _factory.NewHeritageClause(token, SingleNodeListChild(childIndices));
            }
            case Kind.InterfaceDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var typeParameters = NodeListAt(it.NextIf(mask, 2));
                var heritageClauses = NodeListAt(it.NextIf(mask, 3));
                var members = NodeListAt(it.NextIf(mask, 4));
                return _factory.NewInterfaceDeclaration(modifiers, name, typeParameters, heritageClauses, members);
            }
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var typeParameters = NodeListAt(it.NextIf(mask, 2));
                var typeNode = NodeAt(it.NextIf(mask, 3));
                if (kind == Kind.JSTypeAliasDeclaration)
                {
                    return _factory.NewJSTypeAliasDeclaration(modifiers, name, typeParameters, typeNode);
                }

                return _factory.NewTypeAliasDeclaration(modifiers, name, typeParameters, typeNode);
            }
            case Kind.EnumMember:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var name = NodeAt(it.NextIf(mask, 0));
                var initializer = NodeAt(it.NextIf(mask, 1));
                return _factory.NewEnumMember(name, initializer);
            }
            case Kind.EnumDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var members = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewEnumDeclaration(modifiers, name, members);
            }
            case Kind.ModuleBlock:
                return _factory.NewModuleBlock(SingleNodeListChild(childIndices));
            case Kind.NotEmittedStatement:
                return _factory.NewNotEmittedStatement();
            case Kind.NotEmittedTypeElement:
                return _factory.NewNotEmittedTypeElement();
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var importClause = NodeAt(it.NextIf(mask, 1));
                var moduleSpecifier = NodeAt(it.NextIf(mask, 2));
                var attributes = NodeAt(it.NextIf(mask, 3));
                if (kind == Kind.JSImportDeclaration)
                {
                    return _factory.NewJSImportDeclaration(modifiers, importClause, moduleSpecifier, attributes);
                }

                return _factory.NewImportDeclaration(modifiers, importClause, moduleSpecifier, attributes);
            }
            case Kind.ExternalModuleReference:
                return _factory.NewExternalModuleReference(SingleChild(childIndices));
            case Kind.NamespaceImport:
                return _factory.NewNamespaceImport(SingleChild(childIndices));
            case Kind.NamedImports:
                return _factory.NewNamedImports(SingleNodeListChild(childIndices));
            case Kind.ExportAssignment:
            {
                var isExportEquals = (commonData & 1) != 0;
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var typeNode = NodeAt(it.NextIf(mask, 1));
                var expression = NodeAt(it.NextIf(mask, 2));
                return _factory.NewExportAssignment(modifiers, isExportEquals, typeNode, expression);
            }
            case Kind.NamespaceExportDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                return _factory.NewNamespaceExportDeclaration(modifiers, name);
            }
            case Kind.NamespaceExport:
                return _factory.NewNamespaceExport(SingleChild(childIndices));
            case Kind.NamedExports:
                return _factory.NewNamedExports(SingleNodeListChild(childIndices));
            case Kind.ExportSpecifier:
            {
                var isTypeOnly = (commonData & 1) != 0;
                var it = ChildIterator.NewChildIter(childIndices);
                var propertyName = NodeAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                return _factory.NewExportSpecifier(isTypeOnly, propertyName, name);
            }
            case Kind.CallSignature:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var typeParameters = NodeListAt(it.NextIf(mask, 0));
                var parameters = NodeListAt(it.NextIf(mask, 1));
                var typeNode = NodeAt(it.NextIf(mask, 2));
                return _factory.NewCallSignatureDeclaration(typeParameters, parameters, typeNode);
            }
            case Kind.ConstructSignature:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var typeParameters = NodeListAt(it.NextIf(mask, 0));
                var parameters = NodeListAt(it.NextIf(mask, 1));
                var typeNode = NodeAt(it.NextIf(mask, 2));
                return _factory.NewConstructSignatureDeclaration(typeParameters, parameters, typeNode);
            }
            case Kind.Constructor:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var typeParameters = NodeListAt(it.NextIf(mask, 1));
                var parameters = NodeListAt(it.NextIf(mask, 2));
                var typeNode = NodeAt(it.NextIf(mask, 3));
                var body = NodeAt(it.NextIf(mask, 4));
                return _factory.NewConstructorDeclaration(modifiers, typeParameters, parameters, typeNode, null, body);
            }
            case Kind.GetAccessor:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var typeParameters = NodeListAt(it.NextIf(mask, 2));
                var parameters = NodeListAt(it.NextIf(mask, 3));
                var typeNode = NodeAt(it.NextIf(mask, 4));
                var body = NodeAt(it.NextIf(mask, 5));
                return _factory.NewGetAccessorDeclaration(modifiers, name, typeParameters, parameters, typeNode, null, body);
            }
            case Kind.SetAccessor:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var typeParameters = NodeListAt(it.NextIf(mask, 2));
                var parameters = NodeListAt(it.NextIf(mask, 3));
                var typeNode = NodeAt(it.NextIf(mask, 4));
                var body = NodeAt(it.NextIf(mask, 5));
                return _factory.NewSetAccessorDeclaration(modifiers, name, typeParameters, parameters, typeNode, null, body);
            }
            case Kind.IndexSignature:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var parameters = NodeListAt(it.NextIf(mask, 1));
                var typeNode = NodeAt(it.NextIf(mask, 2));
                return _factory.NewIndexSignatureDeclaration(modifiers, parameters, typeNode);
            }
            case Kind.MethodSignature:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var postfixToken = NodeAt(it.NextIf(mask, 2));
                var typeParameters = NodeListAt(it.NextIf(mask, 3));
                var parameters = NodeListAt(it.NextIf(mask, 4));
                var typeNode = NodeAt(it.NextIf(mask, 5));
                return _factory.NewMethodSignatureDeclaration(modifiers, name, postfixToken, typeParameters, parameters, typeNode);
            }
            case Kind.MethodDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var asteriskToken = NodeAt(it.NextIf(mask, 1));
                var name = NodeAt(it.NextIf(mask, 2));
                var postfixToken = NodeAt(it.NextIf(mask, 3));
                var typeParameters = NodeListAt(it.NextIf(mask, 4));
                var parameters = NodeListAt(it.NextIf(mask, 5));
                var typeNode = NodeAt(it.NextIf(mask, 6));
                var body = NodeAt(it.NextIf(mask, 7));
                return _factory.NewMethodDeclaration(modifiers, asteriskToken, name, postfixToken, typeParameters, parameters, typeNode, null, body);
            }
            case Kind.PropertySignature:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var postfixToken = NodeAt(it.NextIf(mask, 2));
                var typeNode = NodeAt(it.NextIf(mask, 3));
                var initializer = NodeAt(it.NextIf(mask, 4));
                return _factory.NewPropertySignatureDeclaration(modifiers, name, postfixToken, typeNode, initializer);
            }
            case Kind.PropertyDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var postfixToken = NodeAt(it.NextIf(mask, 2));
                var typeNode = NodeAt(it.NextIf(mask, 3));
                var initializer = NodeAt(it.NextIf(mask, 4));
                return _factory.NewPropertyDeclaration(modifiers, name, postfixToken, typeNode, initializer);
            }
            case Kind.SemicolonClassElement:
                return _factory.NewSemicolonClassElement();
            case Kind.ClassStaticBlockDeclaration:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var body = NodeAt(it.NextIf(mask, 1));
                return _factory.NewClassStaticBlockDeclaration(modifiers, body);
            }
            case Kind.OmittedExpression:
                return _factory.NewOmittedExpression();
            case Kind.FalseKeyword:
            case Kind.ImportKeyword:
            case Kind.NullKeyword:
            case Kind.SuperKeyword:
            case Kind.ThisKeyword:
            case Kind.TrueKeyword:
                return _factory.NewKeywordExpression(kind);
            case Kind.BinaryExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var left = NodeAt(it.NextIf(mask, 1));
                var typeNode = NodeAt(it.NextIf(mask, 2));
                var operatorToken = NodeAt(it.NextIf(mask, 3));
                var right = NodeAt(it.NextIf(mask, 4));
                return _factory.NewBinaryExpression(modifiers, left, typeNode, operatorToken, right);
            }
            case Kind.PrefixUnaryExpression:
            {
                Kind @operator = default;
                switch (commonData & 7)
                {
                    case 0:
                        @operator = Kind.PlusToken;
                        break;
                    case 1:
                        @operator = Kind.MinusToken;
                        break;
                    case 2:
                        @operator = Kind.TildeToken;
                        break;
                    case 3:
                        @operator = Kind.ExclamationToken;
                        break;
                    case 4:
                        @operator = Kind.PlusPlusToken;
                        break;
                    case 5:
                        @operator = Kind.MinusMinusToken;
                        break;
                }

                return _factory.NewPrefixUnaryExpression(@operator, SingleChild(childIndices));
            }
            case Kind.PostfixUnaryExpression:
            {
                var @operator = Kind.PlusPlusToken;
                if ((commonData & 1) != 0)
                {
                    @operator = Kind.MinusMinusToken;
                }

                return _factory.NewPostfixUnaryExpression(SingleChild(childIndices), @operator);
            }
            case Kind.YieldExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var asteriskToken = NodeAt(it.NextIf(mask, 0));
                var expression = NodeAt(it.NextIf(mask, 1));
                return _factory.NewYieldExpression(asteriskToken, expression);
            }
            case Kind.ArrowFunction:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var typeParameters = NodeListAt(it.NextIf(mask, 1));
                var parameters = NodeListAt(it.NextIf(mask, 2));
                var typeNode = NodeAt(it.NextIf(mask, 3));
                var equalsGreaterThanToken = NodeAt(it.NextIf(mask, 4));
                var body = NodeAt(it.NextIf(mask, 5));
                return _factory.NewArrowFunction(modifiers, typeParameters, parameters, typeNode, null, equalsGreaterThanToken, body);
            }
            case Kind.FunctionExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var asteriskToken = NodeAt(it.NextIf(mask, 1));
                var name = NodeAt(it.NextIf(mask, 2));
                var typeParameters = NodeListAt(it.NextIf(mask, 3));
                var parameters = NodeListAt(it.NextIf(mask, 4));
                var typeNode = NodeAt(it.NextIf(mask, 5));
                var body = NodeAt(it.NextIf(mask, 6));
                return _factory.NewFunctionExpression(modifiers, asteriskToken, name, typeParameters, parameters, typeNode, null, body);
            }
            case Kind.AsExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var typeNode = NodeAt(it.NextIf(mask, 1));
                return _factory.NewAsExpression(expression, typeNode);
            }
            case Kind.SatisfiesExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var typeNode = NodeAt(it.NextIf(mask, 1));
                return _factory.NewSatisfiesExpression(expression, typeNode);
            }
            case Kind.ConditionalExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var condition = NodeAt(it.NextIf(mask, 0));
                var questionToken = NodeAt(it.NextIf(mask, 1));
                var whenTrue = NodeAt(it.NextIf(mask, 2));
                var colonToken = NodeAt(it.NextIf(mask, 3));
                var whenFalse = NodeAt(it.NextIf(mask, 4));
                return _factory.NewConditionalExpression(condition, questionToken, whenTrue, colonToken, whenFalse);
            }
            case Kind.PropertyAccessExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var questionDotToken = NodeAt(it.NextIf(mask, 1));
                var name = NodeAt(it.NextIf(mask, 2));
                return _factory.NewPropertyAccessExpression(expression, questionDotToken, name, 0);
            }
            case Kind.ElementAccessExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var questionDotToken = NodeAt(it.NextIf(mask, 1));
                var argumentExpression = NodeAt(it.NextIf(mask, 2));
                return _factory.NewElementAccessExpression(expression, questionDotToken, argumentExpression, 0);
            }
            case Kind.CallExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var questionDotToken = NodeAt(it.NextIf(mask, 1));
                var typeArguments = NodeListAt(it.NextIf(mask, 2));
                var arguments = NodeListAt(it.NextIf(mask, 3));
                return _factory.NewCallExpression(expression, questionDotToken, typeArguments, arguments, 0);
            }
            case Kind.NewExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var typeArguments = NodeListAt(it.NextIf(mask, 1));
                var arguments = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewNewExpression(expression, typeArguments, arguments);
            }
            case Kind.MetaProperty:
            {
                var keywordToken = Kind.ImportKeyword;
                if ((commonData & 1) != 0)
                {
                    keywordToken = Kind.NewKeyword;
                }

                return _factory.NewMetaProperty(keywordToken, SingleChild(childIndices));
            }
            case Kind.NonNullExpression:
                return _factory.NewNonNullExpression(SingleChild(childIndices), 0);
            case Kind.SpreadElement:
                return _factory.NewSpreadElement(SingleChild(childIndices));
            case Kind.TemplateExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var head = NodeAt(it.NextIf(mask, 0));
                var templateSpans = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewTemplateExpression(head, templateSpans);
            }
            case Kind.TemplateSpan:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var literal = NodeAt(it.NextIf(mask, 1));
                return _factory.NewTemplateSpan(expression, literal);
            }
            case Kind.TaggedTemplateExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tag = NodeAt(it.NextIf(mask, 0));
                var questionDotToken = NodeAt(it.NextIf(mask, 1));
                var typeArguments = NodeListAt(it.NextIf(mask, 2));
                var template = NodeAt(it.NextIf(mask, 3));
                return _factory.NewTaggedTemplateExpression(tag, questionDotToken, typeArguments, template, 0);
            }
            case Kind.ParenthesizedExpression:
                return _factory.NewParenthesizedExpression(SingleChild(childIndices));
            case Kind.ArrayLiteralExpression:
            {
                var multiLine = (commonData & 1) != 0;
                NodeList? list = null;
                if (childIndices.Count > 0)
                {
                    list = NodeListAt(childIndices[0]);
                }

                return _factory.NewArrayLiteralExpression(list, multiLine);
            }
            case Kind.ObjectLiteralExpression:
            {
                var multiLine = (commonData & 1) != 0;
                NodeList? list = null;
                if (childIndices.Count > 0)
                {
                    list = NodeListAt(childIndices[0]);
                }

                return _factory.NewObjectLiteralExpression(list, multiLine);
            }
            case Kind.SpreadAssignment:
                return _factory.NewSpreadAssignment(SingleChild(childIndices));
            case Kind.PropertyAssignment:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var postfixToken = NodeAt(it.NextIf(mask, 2));
                var typeNode = NodeAt(it.NextIf(mask, 3));
                var initializer = NodeAt(it.NextIf(mask, 4));
                return _factory.NewPropertyAssignment(modifiers, name, postfixToken, typeNode, initializer);
            }
            case Kind.ShorthandPropertyAssignment:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var postfixToken = NodeAt(it.NextIf(mask, 2));
                var typeNode = NodeAt(it.NextIf(mask, 3));
                var equalsToken = NodeAt(it.NextIf(mask, 4));
                var objectAssignmentInitializer = NodeAt(it.NextIf(mask, 5));
                return _factory.NewShorthandPropertyAssignment(modifiers, name, postfixToken, typeNode, equalsToken, objectAssignmentInitializer);
            }
            case Kind.DeleteExpression:
                return _factory.NewDeleteExpression(SingleChild(childIndices));
            case Kind.TypeOfExpression:
                return _factory.NewTypeOfExpression(SingleChild(childIndices));
            case Kind.VoidExpression:
                return _factory.NewVoidExpression(SingleChild(childIndices));
            case Kind.AwaitExpression:
                return _factory.NewAwaitExpression(SingleChild(childIndices));
            case Kind.TypeAssertionExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var typeNode = NodeAt(it.NextIf(mask, 0));
                var expression = NodeAt(it.NextIf(mask, 1));
                return _factory.NewTypeAssertion(typeNode, expression);
            }
            case Kind.VoidKeyword:
            case Kind.AnyKeyword:
            case Kind.BooleanKeyword:
            case Kind.IntrinsicKeyword:
            case Kind.NeverKeyword:
            case Kind.NumberKeyword:
            case Kind.ObjectKeyword:
            case Kind.StringKeyword:
            case Kind.SymbolKeyword:
            case Kind.UndefinedKeyword:
            case Kind.UnknownKeyword:
            case Kind.BigIntKeyword:
                return _factory.NewKeywordTypeNode(kind);
            case Kind.UnionType:
                return _factory.NewUnionTypeNode(SingleNodeListChild(childIndices));
            case Kind.IntersectionType:
                return _factory.NewIntersectionTypeNode(SingleNodeListChild(childIndices));
            case Kind.ConditionalType:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var checkType = NodeAt(it.NextIf(mask, 0));
                var extendsType = NodeAt(it.NextIf(mask, 1));
                var trueType = NodeAt(it.NextIf(mask, 2));
                var falseType = NodeAt(it.NextIf(mask, 3));
                return _factory.NewConditionalTypeNode(checkType, extendsType, trueType, falseType);
            }
            case Kind.TypeOperator:
            {
                Kind @operator = default;
                switch (commonData & 3)
                {
                    case 0:
                        @operator = Kind.KeyOfKeyword;
                        break;
                    case 1:
                        @operator = Kind.ReadonlyKeyword;
                        break;
                    case 2:
                        @operator = Kind.UniqueKeyword;
                        break;
                }

                return _factory.NewTypeOperatorNode(@operator, SingleChild(childIndices));
            }
            case Kind.InferType:
                return _factory.NewInferTypeNode(SingleChild(childIndices));
            case Kind.ArrayType:
                return _factory.NewArrayTypeNode(SingleChild(childIndices));
            case Kind.IndexedAccessType:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var objectType = NodeAt(it.NextIf(mask, 0));
                var indexType = NodeAt(it.NextIf(mask, 1));
                return _factory.NewIndexedAccessTypeNode(objectType, indexType);
            }
            case Kind.TypeReference:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var typeName = NodeAt(it.NextIf(mask, 0));
                var typeArguments = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewTypeReferenceNode(typeName, typeArguments);
            }
            case Kind.ExpressionWithTypeArguments:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var typeArguments = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewExpressionWithTypeArguments(expression, typeArguments);
            }
            case Kind.LiteralType:
                return _factory.NewLiteralTypeNode(SingleChild(childIndices));
            case Kind.ThisType:
                return _factory.NewThisTypeNode();
            case Kind.TypePredicate:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var assertsModifier = NodeAt(it.NextIf(mask, 0));
                var parameterName = NodeAt(it.NextIf(mask, 1));
                var typeNode = NodeAt(it.NextIf(mask, 2));
                return _factory.NewTypePredicateNode(assertsModifier, parameterName, typeNode);
            }
            case Kind.ImportAttribute:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var name = NodeAt(it.NextIf(mask, 0));
                var value = NodeAt(it.NextIf(mask, 1));
                return _factory.NewImportAttribute(name, value);
            }
            case Kind.ImportAttributes:
            {
                var multiLine = (commonData & 1) != 0;
                var token = Kind.WithKeyword;
                if (((commonData >> 1) & 1) != 0)
                {
                    token = Kind.AssertKeyword;
                }

                NodeList? list = null;
                if (childIndices.Count > 0)
                {
                    list = NodeListAt(childIndices[0]);
                }

                return _factory.NewImportAttributes(token, list, multiLine);
            }
            case Kind.TypeQuery:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var exprName = NodeAt(it.NextIf(mask, 0));
                var typeArguments = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewTypeQueryNode(exprName, typeArguments);
            }
            case Kind.MappedType:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var readonlyToken = NodeAt(it.NextIf(mask, 0));
                var typeParameter = NodeAt(it.NextIf(mask, 1));
                var nameType = NodeAt(it.NextIf(mask, 2));
                var questionToken = NodeAt(it.NextIf(mask, 3));
                var typeNode = NodeAt(it.NextIf(mask, 4));
                var members = NodeListAt(it.NextIf(mask, 5));
                return _factory.NewMappedTypeNode(readonlyToken, typeParameter, nameType, questionToken, typeNode, members);
            }
            case Kind.TypeLiteral:
                return _factory.NewTypeLiteralNode(SingleNodeListChild(childIndices));
            case Kind.TupleType:
                return _factory.NewTupleTypeNode(SingleNodeListChild(childIndices));
            case Kind.NamedTupleMember:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var dotDotDotToken = NodeAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var questionToken = NodeAt(it.NextIf(mask, 2));
                var typeNode = NodeAt(it.NextIf(mask, 3));
                return _factory.NewNamedTupleMember(dotDotDotToken, name, questionToken, typeNode);
            }
            case Kind.OptionalType:
                return _factory.NewOptionalTypeNode(SingleChild(childIndices));
            case Kind.RestType:
                return _factory.NewRestTypeNode(SingleChild(childIndices));
            case Kind.ParenthesizedType:
                return _factory.NewParenthesizedTypeNode(SingleChild(childIndices));
            case Kind.FunctionType:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var typeParameters = NodeListAt(it.NextIf(mask, 0));
                var parameters = NodeListAt(it.NextIf(mask, 1));
                var typeNode = NodeAt(it.NextIf(mask, 2));
                return _factory.NewFunctionTypeNode(typeParameters, parameters, typeNode);
            }
            case Kind.ConstructorType:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var typeParameters = NodeListAt(it.NextIf(mask, 1));
                var parameters = NodeListAt(it.NextIf(mask, 2));
                var typeNode = NodeAt(it.NextIf(mask, 3));
                return _factory.NewConstructorTypeNode(modifiers, typeParameters, parameters, typeNode);
            }
            case Kind.TemplateLiteralType:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var head = NodeAt(it.NextIf(mask, 0));
                var templateSpans = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewTemplateLiteralTypeNode(head, templateSpans);
            }
            case Kind.TemplateLiteralTypeSpan:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var typeNode = NodeAt(it.NextIf(mask, 0));
                var literal = NodeAt(it.NextIf(mask, 1));
                return _factory.NewTemplateLiteralTypeSpan(typeNode, literal);
            }
            case Kind.SyntheticExpression:
            {
                var (typeNode, isSpread) = Decoder.DecodeNodeCommonDataSyntheticExpression(commonData);
                return _factory.NewSyntheticExpression(typeNode, isSpread, SingleChild(childIndices));
            }
            case Kind.PartiallyEmittedExpression:
                return _factory.NewPartiallyEmittedExpression(SingleChild(childIndices));
            case Kind.JsxElement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var openingElement = NodeAt(it.NextIf(mask, 0));
                var children = NodeListAt(it.NextIf(mask, 1));
                var closingElement = NodeAt(it.NextIf(mask, 2));
                return _factory.NewJsxElement(openingElement, children, closingElement);
            }
            case Kind.JsxAttributes:
                return _factory.NewJsxAttributes(SingleNodeListChild(childIndices));
            case Kind.JsxNamespacedName:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var @namespace = NodeAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                return _factory.NewJsxNamespacedName(@namespace, name);
            }
            case Kind.JsxOpeningElement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeArguments = NodeListAt(it.NextIf(mask, 1));
                var attributes = NodeAt(it.NextIf(mask, 2));
                return _factory.NewJsxOpeningElement(tagName, typeArguments, attributes);
            }
            case Kind.JsxSelfClosingElement:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeArguments = NodeListAt(it.NextIf(mask, 1));
                var attributes = NodeAt(it.NextIf(mask, 2));
                return _factory.NewJsxSelfClosingElement(tagName, typeArguments, attributes);
            }
            case Kind.JsxFragment:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var openingFragment = NodeAt(it.NextIf(mask, 0));
                var children = NodeListAt(it.NextIf(mask, 1));
                var closingFragment = NodeAt(it.NextIf(mask, 2));
                return _factory.NewJsxFragment(openingFragment, children, closingFragment);
            }
            case Kind.JsxOpeningFragment:
                return _factory.NewJsxOpeningFragment();
            case Kind.JsxClosingFragment:
                return _factory.NewJsxClosingFragment();
            case Kind.JsxAttribute:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var name = NodeAt(it.NextIf(mask, 0));
                var initializer = NodeAt(it.NextIf(mask, 1));
                return _factory.NewJsxAttribute(name, initializer);
            }
            case Kind.JsxSpreadAttribute:
                return _factory.NewJsxSpreadAttribute(SingleChild(childIndices));
            case Kind.JsxClosingElement:
                return _factory.NewJsxClosingElement(SingleChild(childIndices));
            case Kind.JsxExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var dotDotDotToken = NodeAt(it.NextIf(mask, 0));
                var expression = NodeAt(it.NextIf(mask, 1));
                return _factory.NewJsxExpression(dotDotDotToken, expression);
            }
            case Kind.SyntaxList:
            {
                var nodes = AllocNodeSlice(childIndices.Count);
                for (var i = 0; i < childIndices.Count; i++)
                {
                    nodes[i] = _nodes[childIndices[i]]!;
                }

                return _factory.NewSyntaxList(nodes);
            }
            case Kind.JSDoc:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var comment = NodeListAt(it.NextIf(mask, 0));
                var tags = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewJSDoc(comment, tags);
            }
            case Kind.JSDocTypeExpression:
                return _factory.NewJSDocTypeExpression(SingleChild(childIndices));
            case Kind.JSDocNonNullableType:
                return _factory.NewJSDocNonNullableType(SingleChild(childIndices));
            case Kind.JSDocNullableType:
                return _factory.NewJSDocNullableType(SingleChild(childIndices));
            case Kind.JSDocAllType:
                return _factory.NewJSDocAllType();
            case Kind.JSDocVariadicType:
                return _factory.NewJSDocVariadicType(SingleChild(childIndices));
            case Kind.JSDocOptionalType:
                return _factory.NewJSDocOptionalType(SingleChild(childIndices));
            case Kind.JSDocTypeTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeExpression = NodeAt(it.NextIf(mask, 1));
                var comment = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewJSDocTypeTag(tagName, typeExpression, comment);
            }
            case Kind.JSDocUnknownTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var comment = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewJSDocUnknownTag(tagName, comment);
            }
            case Kind.JSDocTemplateTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var constraint = NodeAt(it.NextIf(mask, 1));
                var typeParameters = NodeListAt(it.NextIf(mask, 2));
                var comment = NodeListAt(it.NextIf(mask, 3));
                return _factory.NewJSDocTemplateTag(tagName, constraint, typeParameters, comment);
            }
            case Kind.JSDocReturnTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeExpression = NodeAt(it.NextIf(mask, 1));
                var comment = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewJSDocReturnTag(tagName, typeExpression, comment);
            }
            case Kind.JSDocPublicTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var comment = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewJSDocPublicTag(tagName, comment);
            }
            case Kind.JSDocPrivateTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var comment = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewJSDocPrivateTag(tagName, comment);
            }
            case Kind.JSDocProtectedTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var comment = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewJSDocProtectedTag(tagName, comment);
            }
            case Kind.JSDocReadonlyTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var comment = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewJSDocReadonlyTag(tagName, comment);
            }
            case Kind.JSDocOverrideTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var comment = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewJSDocOverrideTag(tagName, comment);
            }
            case Kind.JSDocDeprecatedTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var comment = NodeListAt(it.NextIf(mask, 1));
                return _factory.NewJSDocDeprecatedTag(tagName, comment);
            }
            case Kind.JSDocSeeTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var nameExpression = NodeAt(it.NextIf(mask, 1));
                var comment = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewJSDocSeeTag(tagName, nameExpression, comment);
            }
            case Kind.JSDocImplementsTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var className = NodeAt(it.NextIf(mask, 1));
                var comment = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewJSDocImplementsTag(tagName, className, comment);
            }
            case Kind.JSDocAugmentsTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var className = NodeAt(it.NextIf(mask, 1));
                var comment = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewJSDocAugmentsTag(tagName, className, comment);
            }
            case Kind.JSDocSatisfiesTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeExpression = NodeAt(it.NextIf(mask, 1));
                var comment = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewJSDocSatisfiesTag(tagName, typeExpression, comment);
            }
            case Kind.JSDocThrowsTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeExpression = NodeAt(it.NextIf(mask, 1));
                var comment = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewJSDocThrowsTag(tagName, typeExpression, comment);
            }
            case Kind.JSDocThisTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeExpression = NodeAt(it.NextIf(mask, 1));
                var comment = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewJSDocThisTag(tagName, typeExpression, comment);
            }
            case Kind.JSDocImportTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var importClause = NodeAt(it.NextIf(mask, 1));
                var moduleSpecifier = NodeAt(it.NextIf(mask, 2));
                var attributes = NodeAt(it.NextIf(mask, 3));
                var comment = NodeListAt(it.NextIf(mask, 4));
                return _factory.NewJSDocImportTag(tagName, importClause, moduleSpecifier, attributes, comment);
            }
            case Kind.JSDocCallbackTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeExpression = NodeAt(it.NextIf(mask, 1));
                var name = NodeAt(it.NextIf(mask, 2));
                var comment = NodeListAt(it.NextIf(mask, 3));
                return _factory.NewJSDocCallbackTag(tagName, typeExpression, name, comment);
            }
            case Kind.JSDocOverloadTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeExpression = NodeAt(it.NextIf(mask, 1));
                var comment = NodeListAt(it.NextIf(mask, 2));
                return _factory.NewJSDocOverloadTag(tagName, typeExpression, comment);
            }
            case Kind.JSDocTypedefTag:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var typeExpression = NodeAt(it.NextIf(mask, 1));
                var name = NodeAt(it.NextIf(mask, 2));
                var comment = NodeListAt(it.NextIf(mask, 3));
                return _factory.NewJSDocTypedefTag(tagName, typeExpression, name, comment);
            }
            case Kind.JSDocSignature:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var typeParameters = NodeListAt(it.NextIf(mask, 0));
                var parameters = NodeListAt(it.NextIf(mask, 1));
                var typeNode = NodeAt(it.NextIf(mask, 2));
                return _factory.NewJSDocSignature(typeParameters, parameters, typeNode);
            }
            case Kind.JSDocNameReference:
                return _factory.NewJSDocNameReference(SingleChild(childIndices));
            case Kind.ModuleDeclaration:
            {
                var keyword = Kind.ModuleKeyword;
                if ((commonData & 1) != 0)
                {
                    keyword = Kind.NamespaceKeyword;
                }

                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var attributes = NodeAt(it.NextIf(mask, 2));
                var body = NodeAt(it.NextIf(mask, 3));
                return _factory.NewModuleDeclaration(modifiers, keyword, name, attributes, body);
            }
            case Kind.ImportEqualsDeclaration:
            {
                var isTypeOnly = (commonData & 1) != 0;
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var moduleReference = NodeAt(it.NextIf(mask, 2));
                return _factory.NewImportEqualsDeclaration(modifiers, isTypeOnly, name, moduleReference);
            }
            case Kind.ExportDeclaration:
            {
                var isTypeOnly = (commonData & 1) != 0;
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var exportClause = NodeAt(it.NextIf(mask, 1));
                var moduleSpecifier = NodeAt(it.NextIf(mask, 2));
                var attributes = NodeAt(it.NextIf(mask, 3));
                return _factory.NewExportDeclaration(modifiers, isTypeOnly, exportClause, moduleSpecifier, attributes);
            }
            case Kind.ImportType:
            {
                var isTypeOf = (commonData & 1) != 0;
                var it = ChildIterator.NewChildIter(childIndices);
                var argument = NodeAt(it.NextIf(mask, 0));
                var attributes = NodeAt(it.NextIf(mask, 1));
                var qualifier = NodeAt(it.NextIf(mask, 2));
                var typeArguments = NodeListAt(it.NextIf(mask, 3));
                return _factory.NewImportTypeNode(isTypeOf, argument, attributes, qualifier, typeArguments);
            }
            case Kind.ImportClause:
            {
                Kind phaseModifier = default;
                switch (commonData & 3)
                {
                    case 1:
                        phaseModifier = Kind.TypeKeyword;
                        break;
                    case 2:
                        phaseModifier = Kind.DeferKeyword;
                        break;
                }

                var it = ChildIterator.NewChildIter(childIndices);
                var name = NodeAt(it.NextIf(mask, 0));
                var namedBindings = NodeAt(it.NextIf(mask, 1));
                return _factory.NewImportClause(phaseModifier, name, namedBindings);
            }
            case Kind.ImportSpecifier:
            {
                var isTypeOnly = (commonData & 1) != 0;
                var it = ChildIterator.NewChildIter(childIndices);
                var propertyName = NodeAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                return _factory.NewImportSpecifier(isTypeOnly, propertyName, name);
            }
            case Kind.TypeParameter:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var modifiers = ModifierListAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var constraint = NodeAt(it.NextIf(mask, 2));
                var expression = NodeAt(it.NextIf(mask, 3));
                var defaultType = NodeAt(it.NextIf(mask, 4));
                return _factory.NewTypeParameterDeclaration(modifiers, name, constraint, expression, defaultType);
            }
            case Kind.SyntheticReferenceExpression:
            {
                var it = ChildIterator.NewChildIter(childIndices);
                var expression = NodeAt(it.NextIf(mask, 0));
                var thisArg = NodeAt(it.NextIf(mask, 1));
                return _factory.NewSyntheticReferenceExpression(expression, thisArg);
            }
            case Kind.JSDocTypeLiteral:
            {
                var isArrayType = (commonData & 1) != 0;
                var nodes = AllocNodeSlice(childIndices.Count);
                for (var i = 0; i < childIndices.Count; i++)
                {
                    nodes[i] = _nodes[childIndices[i]]!;
                }

                return _factory.NewJSDocTypeLiteral(nodes, isArrayType);
            }
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
            {
                var isBracketed = (commonData & 1) != 0;
                var isNameFirst = (commonData & 2) != 0;
                var it = ChildIterator.NewChildIter(childIndices);
                var tagName = NodeAt(it.NextIf(mask, 0));
                var name = NodeAt(it.NextIf(mask, 1));
                var typeExpression = NodeAt(it.NextIf(mask, 2));
                var comment = NodeListAt(it.NextIf(mask, 3));
                return _factory.NewJSDocParameterOrPropertyTag(kind, tagName, name, isBracketed, typeExpression, isNameFirst, comment);
            }
            default:
                throw new DebugFailureException($"unhandled node kind {kind} with {childIndices.Count} children");
        }
    }
}

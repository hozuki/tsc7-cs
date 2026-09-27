// ported from: internal/transformers/estransforms/esdecorator.go:13-123（Class/Decorator 求值顺序注释、lexicalEntryKind、
//               lexicalEntry、memberInfo、classInfo、esDecoratorTransformer 的声明）
//              + :124-576（newESDecoratorTransformer、updateState、enterClass/exitClass、enterClassElement/exitClassElement、
//                enterName/exitName、enterOther/exitOther、visitSourceFile、outerThisVisit、shouldVisitNode、visit、
//                modifierVisitorVisit/classElementVisitorVisit/discardedValueVisit/nonConstructorClassElementVisit/
//                constructorClassElementVisit/exportStrippingModifierVisit、getHelperVariableName、createHelperVariable、
//                createLet、createClassInfo）
//              + :1014-1017（isDecoratedClassLike）
//              + :1232-1242（partialResult / createDescriptorFunc 的声明；Go 把它们放在文件中部，这里按"类型集中到本分片"处理）
//
// 与 Go 的差异（逐条，其余地方与 Go 逐行对应）：
// 1) 包级函数（Go 的 `func X(...)`）按 docs/architecture.md §2 放进与本 package 同名的静态类 `EsTransforms`
//    （partial，跨本目录的多个分片/文件）：`newESDecoratorTransformer` → `NewESDecoratorTransformer`、
//    `getHelperVariableName` → `GetHelperVariableName`、`isDecoratedClassLike` → `IsDecoratedClassLike`。
//    它们被**独立类** `EsDecoratorTransformer` 调用，所以是 `internal static`（C# 的 private 是类型级可见性）。
// 2) Go 的 `type lexicalEntryKind int` + iota 常量 → `enum LexicalEntryKind`（数值与 Go 一致：Class=0…Other=3）。
// 3) Go 用命名字段的结构体字面量（`&lexicalEntry{kind: …, next: …}`）→ C# 对象初始化器。
// 4) Go 的 `[]*ast.Expression`（保存/恢复语义：`saved = tx.pendingExpressions; tx.pendingExpressions = nil`）
//    → `List<Node>?`，**只做引用替换、绝不 Clear**（Go 的切片头赋值等价于"换一个列表"，Clear 会连带清掉已保存的那份）。
// 5) Go 的 `*ast.IdentifierNode`/`*ast.Expression`/`*ast.Statement` 在 C# 里都是 `Node`（无类型别名，docs/ast.md §2）。
// 6) Go 里 `tx.top.next.next.next` 只在 `tx.top.next.next` 非 nil 时执行（enterName/enterClassElement 的断言保证），
//    链尾可能是 nil → C# 用 `!` 表达前两跳的非空不变量、最后一跳照 Go 的 nil 检查。
// 7) `opts.CompilerOptions` 在 Go 里永不为 nil（TransformOptions 的调用约定）→ C# 用 `!`。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Scanner.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using static TypeScript.Transformers.Transformers;
using NodeFactory = TypeScript.Printer.NodeFactory;

namespace TypeScript.Transformers.EsTransforms;

// Class/Decorator evaluation order, as it pertains to this transformer:
//
// 1. Class decorators are evaluated outside of the private name scope of the class.
//    - 15.8.20 RS: BindingClassDeclarationEvaluation
//    - 15.8.21 RS: Evaluation
//    - 8.3.5 RS: NamedEvaluation
// 2. ClassHeritage clause is evaluated outside of the private name scope of the class.
//    - 15.8.19 RS: ClassDefinitionEvaluation, Step 8.c.
// 3. The name of the class is assigned.
// 4. For each member:
//    a. Member Decorators are evaluated.
//       - 15.8.19 RS: ClassDefinitionEvaluation, Step 23.
//       - Probably 15.7.13 RS: ClassElementEvaluation, but it's missing from spec text.
//    b. Computed Property name is evaluated
//       - 15.8.19 RS: ClassDefinitionEvaluation, Step 23.
//       - 15.8.15 RS: ClassFieldDefinitionEvaluation, Step 1.
//       - 15.4.5 RS: MethodDefinitionEvaluation, Step 1.
// 5. Static non-field (method/getter/setter/auto-accessor) element decorators are applied
// 6. Non-static non-field (method/getter/setter/auto-accessor) element decorators are applied
// 7. Static field (excl. auto-accessor) element decorators are applied
// 8. Non-static field (excl. auto-accessor) element decorators are applied
// 9. Class decorators are applied
// 10. Class binding is initialized
// 11. Static method extra initializers are evaluated
// 12. Static fields are initialized (incl. extra initializers) and static blocks are evaluated
// 13. Class extra initializers are evaluated
//
// Class constructor evaluation order, as it pertains to this transformer:
//
// 1. Instance method extra initializers are evaluated
// 2. For each instance field/auto-accessor:
//    a. The field is initialized and defined on the instance.
//    b. Extra initializers for the field are evaluated.

/// <summary>Go: <c>type lexicalEntryKind int</c>（esdecorator.go:48，iota 顺序：class, class-element, name, other）。</summary>
internal enum LexicalEntryKind
{
    Class = 0,
    ClassElement = 1,
    Name = 2,
    Other = 3,
}

/// <summary>
/// Go: <c>type lexicalEntry struct</c>（esdecorator.go:59）。lexicalEntry represents a single entry in the lexical
/// scope stack used to track nested class declarations and their state during transformation.
/// </summary>
internal sealed class LexicalEntry
{
    internal LexicalEntryKind Kind;
    internal LexicalEntry? Next;
    internal ClassInfo? ClassInfoData;
    internal List<Node>? SavedPendingExpressions;
    internal Node? ClassThisData;
    internal Node? ClassSuperData;
    internal int Depth;
}

/// <summary>Go: <c>type memberInfo struct</c>（esdecorator.go:70）。存储单个类元素的装饰相关数据。</summary>
internal sealed class MemberInfo
{
    /// <summary>used in class definition step 4.a</summary>
    internal Node? MemberDecoratorsName;

    /// <summary>used in class definition step 12 and constructor evaluation step 2.a</summary>
    internal Node? MemberInitializersName;

    /// <summary>used in class definition step 12 and constructor evaluation step 2.b</summary>
    internal Node? MemberExtraInitializersName;

    internal Node? MemberDescriptorName;
}

/// <summary>Go: <c>type classInfo struct</c>（esdecorator.go:78）。存储单个被装饰类的所有变换数据。</summary>
internal sealed class ClassInfo
{
    internal Node? Class;

    /// <summary>used in class definition step 2</summary>
    internal Node? ClassDecoratorsName;

    /// <summary>used in class definition step 10</summary>
    internal Node? ClassDescriptorName;

    /// <summary>used in class definition step 13</summary>
    internal Node? ClassExtraInitializersName;

    /// <summary>`_classThis`, if needed.</summary>
    internal Node? ClassThis;

    /// <summary>`_classSuper`, if needed.</summary>
    internal Node? ClassSuper;

    internal Node? MetadataReference;

    /// <summary>used in class definition step 4.a, 12, and constructor evaluation</summary>
    internal OrderedMap<Node, MemberInfo> MemberInfos = new();

    /// <summary>used in constructor evaluation step 1</summary>
    internal Node? InstanceMethodExtraInitializersName;

    /// <summary>used in class definition step 11</summary>
    internal Node? StaticMethodExtraInitializersName;

    internal List<Node>? StaticNonFieldDecorationStatements;
    internal List<Node>? NonStaticNonFieldDecorationStatements;
    internal List<Node>? StaticFieldDecorationStatements;
    internal List<Node>? NonStaticFieldDecorationStatements;
    internal bool HasStaticInitializers;
    internal bool HasNonAmbientInstanceFields;
    internal bool HasStaticPrivateClassElements;
    internal List<Node>? PendingStaticInitializers;
    internal List<Node>? PendingInstanceInitializers;
}

/// <summary>Go: <c>type partialResult struct</c>（esdecorator.go:1232）。</summary>
internal sealed class PartialResult
{
    internal ModifierList? Modifiers;
    internal Node? ReferencedName;
    internal Node? Name;
    internal Node? InitializersName;
    internal Node? ExtraInitializersName;
    internal Node? DescriptorName;
    internal Node? ThisArg;
}

/// <summary>Go: <c>type createDescriptorFunc func(member *ast.Node, modifiers *ast.ModifierList) *ast.Expression</c>（esdecorator.go:1242）。</summary>
internal delegate Node? CreateDescriptorFunc(Node member, ModifierList? modifiers);

internal sealed partial class EsDecoratorTransformer : Transformer
{
    private readonly CompilerOptions? _compilerOptions;
    private LexicalEntry? _top;
    private ClassInfo? _classInfoStack;
    private Node? _classThis;
    private Node? _classSuper;
    private List<Node>? _pendingExpressions;
    private Node? _outerThis;
    private bool _shouldTransformPrivateStaticElementsInFile;

    // Go 的 embedded 结构体字段 `outerThisVisitor` 等在 C# 里由 `EsTransforms.NewESDecoratorTransformer`
    // （**独立类**）赋值，所以是 internal（C# 的 private 是类型级可见性，独立类读不到）。
    internal NodeVisitor? _outerThisVisitor;
    internal NodeVisitor? _discardedVisitor;
    internal NodeVisitor? _modifierVisitor;
    internal NodeVisitor? _exportStrippingModifierVisitor;
    internal NodeVisitor? _classElementVisitor;
    internal NodeVisitor? _nonConstructorClassElementVisitor;
    internal NodeVisitor? _constructorClassElementVisitor;
    internal NodeVisitor? _arrayAssignmentVisitor;
    internal NodeVisitor? _objectAssignmentVisitor;
    internal NodeVisitor? _staticOnlyModifierVisitor;
    internal NodeVisitor? _asyncOnlyModifierVisitor;
    internal NodeVisitor? _accessorStrippingModifierVisitor;

    internal EsDecoratorTransformer(CompilerOptions? compilerOptions)
    {
        _compilerOptions = compilerOptions;
    }

    /// <summary>
    /// Go: <c>newESDecoratorTransformer</c> 里创建各 <c>NodeVisitor</c> 的那一段（esdecorator.go:132-158）。
    /// C# 的 private 是**类型级**可见性，独立类 <c>EsTransforms</c> 读不到这些 private 方法/字段，
    /// 所以这段放在本类内（工厂只调用 <c>InitVisitors()</c>）。
    /// </summary>
    internal void InitVisitors()
    {
        var ec = EmitContext();
        _outerThisVisitor = ec.NewNodeVisitor(OuterThisVisit);
        _discardedVisitor = ec.NewNodeVisitor(DiscardedValueVisit);
        _modifierVisitor = ec.NewNodeVisitor(ModifierVisitorVisit);
        _exportStrippingModifierVisitor = ec.NewNodeVisitor(ExportStrippingModifierVisit);
        _classElementVisitor = ec.NewNodeVisitor(ClassElementVisitorVisit);
        _nonConstructorClassElementVisitor = ec.NewNodeVisitor(NonConstructorClassElementVisit);
        _constructorClassElementVisitor = ec.NewNodeVisitor(ConstructorClassElementVisit);
        _arrayAssignmentVisitor = ec.NewNodeVisitor(VisitArrayAssignmentElement);
        _objectAssignmentVisitor = ec.NewNodeVisitor(VisitObjectAssignmentElement);
        _staticOnlyModifierVisitor = ec.NewNodeVisitor(node => node.Kind == Kind.StaticKeyword ? node : null);
        _asyncOnlyModifierVisitor = ec.NewNodeVisitor(node => node.Kind == Kind.AsyncKeyword ? node : null);
        _accessorStrippingModifierVisitor = ec.NewNodeVisitor(node => node.Kind == Kind.AccessorKeyword ? null : node);
    }

    private void UpdateState()
    {
        _classInfoStack = null;
        _classThis = null;
        _classSuper = null;
        if (_top is null)
        {
            return;
        }

        switch (_top.Kind)
        {
            case LexicalEntryKind.Class:
                _classInfoStack = _top.ClassInfoData;
                break;
            case LexicalEntryKind.ClassElement:
                _classInfoStack = _top.Next!.ClassInfoData;
                _classThis = _top.ClassThisData;
                _classSuper = _top.ClassSuperData;
                break;
            case LexicalEntryKind.Name:
            {
                // Go: grandparent := tx.top.next.next.next（前两跳由 enterClassElement/enterName 的不变量保证非 nil）
                var grandparent = _top.Next!.Next!.Next;
                if (grandparent is not null && grandparent.Kind == LexicalEntryKind.ClassElement)
                {
                    _classInfoStack = grandparent.Next!.ClassInfoData;
                    _classThis = grandparent.ClassThisData;
                    _classSuper = grandparent.ClassSuperData;
                }

                break;
            }
        }
    }

    private void EnterClass(ClassInfo? ci)
    {
        _top = new LexicalEntry
        {
            Kind = LexicalEntryKind.Class,
            Next = _top,
            ClassInfoData = ci,
            SavedPendingExpressions = _pendingExpressions,
        };
        _pendingExpressions = null;
        UpdateState();
    }

    private void ExitClass()
    {
        Debug.Assert(
            _top is not null && _top.Kind == LexicalEntryKind.Class,
            "Incorrect value for top.kind. Expected top.kind to be 'class' but got '",
            (int)_top!.Kind,
            "' instead.");
        _pendingExpressions = _top.SavedPendingExpressions;
        _top = _top.Next;
        UpdateState();
    }

    private void EnterClassElement(Node node)
    {
        Debug.Assert(
            _top is not null && _top.Kind == LexicalEntryKind.Class,
            "Incorrect value for top.kind. Expected top.kind to be 'class' but got '",
            (int)_top!.Kind,
            "' instead.");
        _top = new LexicalEntry
        {
            Kind = LexicalEntryKind.ClassElement,
            Next = _top,
        };
        if (IsClassStaticBlockDeclaration(node) || (IsPropertyDeclaration(node) && HasStaticModifier(node)))
        {
            if (_top.Next!.ClassInfoData is not null)
            {
                _top.ClassThisData = _top.Next.ClassInfoData.ClassThis;
                _top.ClassSuperData = _top.Next.ClassInfoData.ClassSuper;
            }
        }

        UpdateState();
    }

    private void ExitClassElement()
    {
        Debug.Assert(
            _top is not null && _top.Kind == LexicalEntryKind.ClassElement,
            "Incorrect value for top.kind. Expected top.kind to be 'class-element' but got '",
            (int)_top!.Kind,
            "' instead.");
        Debug.Assert(
            _top.Next is not null && _top.Next.Kind == LexicalEntryKind.Class,
            "Incorrect value for top.next.kind. Expected top.next.kind to be 'class' but got '",
            (int)_top.Next!.Kind,
            "' instead.");
        _top = _top.Next;
        UpdateState();
    }

    private void EnterName()
    {
        Debug.Assert(
            _top is not null && _top.Kind == LexicalEntryKind.ClassElement,
            "Incorrect value for top.kind. Expected top.kind to be 'class-element' but got '",
            (int)_top!.Kind,
            "' instead.");
        _top = new LexicalEntry
        {
            Kind = LexicalEntryKind.Name,
            Next = _top,
        };
        UpdateState();
    }

    private void ExitName()
    {
        Debug.Assert(
            _top is not null && _top.Kind == LexicalEntryKind.Name,
            "Incorrect value for top.kind. Expected top.kind to be 'name' but got '",
            (int)_top!.Kind,
            "' instead.");
        _top = _top.Next;
        UpdateState();
    }

    private void EnterOther()
    {
        if (_top is not null && _top.Kind == LexicalEntryKind.Other)
        {
            Debug.Assert(_pendingExpressions is null || _pendingExpressions.Count == 0);
            _top.Depth++;
        }
        else
        {
            _top = new LexicalEntry
            {
                Kind = LexicalEntryKind.Other,
                Next = _top,
                SavedPendingExpressions = _pendingExpressions,
            };
            _pendingExpressions = null;
            UpdateState();
        }
    }

    private void ExitOther()
    {
        Debug.Assert(
            _top is not null && _top.Kind == LexicalEntryKind.Other,
            "Incorrect value for top.kind. Expected top.kind to be 'other' but got '",
            (int)_top!.Kind,
            "' instead.");
        if (_top.Depth > 0)
        {
            Debug.Assert(_pendingExpressions is null || _pendingExpressions.Count == 0);
            _top.Depth--;
        }
        else
        {
            _pendingExpressions = _top.SavedPendingExpressions;
            _top = _top.Next;
            UpdateState();
        }
    }

    private Node? VisitSourceFile(SourceFile node)
    {
        _top = null;
        _shouldTransformPrivateStaticElementsInFile = false;
        var visited = Visitor().VisitEachChild(node);
        EmitContext().AddEmitHelper(visited!, EmitContext().ReadEmitHelpers());
        if (_shouldTransformPrivateStaticElementsInFile)
        {
            EmitContext().AddEmitFlags(visited!, EmitFlags.TransformPrivateStaticElements);
            _shouldTransformPrivateStaticElementsInFile = false;
        }

        return visited;
    }

    private Node? OuterThisVisit(Node n)
    {
        if ((n.SubtreeFacts & SubtreeFacts.ContainsLexicalThis) == 0 && n.Kind != Kind.ThisKeyword)
        {
            return n;
        }

        if (n.Kind == Kind.ThisKeyword)
        {
            _outerThis ??= Factory().NewUniqueNameEx("_outerThis", new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic,
            });
            return _outerThis;
        }

        return _outerThisVisitor!.VisitEachChild(n);
    }

    private bool ShouldVisitNode(Node node)
    {
        return (node.SubtreeFacts & SubtreeFacts.ContainsDecorators) != 0 ||
            (_classThis is not null && (node.SubtreeFacts & SubtreeFacts.ContainsLexicalThis) != 0) ||
            (_classThis is not null && _classSuper is not null && (node.SubtreeFacts & SubtreeFacts.ContainsLexicalSuper) != 0);
    }

    /// <summary>Go: <c>func (tx *esDecoratorTransformer) visit(node *ast.Node) *ast.Node</c>（即 <c>NewTransformer</c> 的 visit 回调）。</summary>
    internal Node? Visit(Node node)
    {
        if (node.Kind == Kind.SourceFile)
        {
            return VisitSourceFile(node.AsSourceFile()!);
        }

        if (!ShouldVisitNode(node))
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.Decorator:
                // Decorators are elided. In Strada, a separate `modifierVisitor` drops decorators
                // before they reach `visitor` via visitEachChild. Here, `visit` serves as both
                // visitors, so decorators from modifier lists reach it directly.
                return null;
            case Kind.ClassDeclaration:
                return VisitClassDeclaration(node.AsClassDeclaration()!);
            case Kind.ClassExpression:
                return VisitClassExpression(node.AsClassExpression()!);
            case Kind.Constructor:
            case Kind.PropertyDeclaration:
            case Kind.ClassStaticBlockDeclaration:
                Debug.Fail("Not supported outside of a class. Use 'classElementVisitor' instead.");
                return null;
            case Kind.Parameter:
                return VisitParameterDeclaration(node.AsParameterDeclaration()!);
            // Support NamedEvaluation to ensure the correct class name for class expressions.
            case Kind.BinaryExpression:
                return VisitBinaryExpression(node, false /*discarded*/);
            case Kind.PropertyAssignment:
            case Kind.VariableDeclaration:
            case Kind.BindingElement:
                return VisitNamedEvaluationSite(node, node.Initializer());
            case Kind.ExportAssignment:
                return VisitExportAssignment(node);
            case Kind.ThisKeyword:
                return VisitThisExpression(node);
            case Kind.ForStatement:
                return VisitForStatement(node);
            case Kind.ExpressionStatement:
                return VisitExpressionStatement(node);
            case Kind.ParenthesizedExpression:
                return VisitParenthesizedExpression(node, false /*discarded*/);
            case Kind.PartiallyEmittedExpression:
                return VisitPartiallyEmittedExpression(node, false /*discarded*/);
            case Kind.CallExpression:
                return VisitCallExpression(node);
            case Kind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression(node);
            case Kind.PrefixUnaryExpression:
            case Kind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, false /*discarded*/);
            case Kind.PropertyAccessExpression:
                return VisitPropertyAccessExpression(node);
            case Kind.ElementAccessExpression:
                return VisitElementAccessExpression(node);
            case Kind.ComputedPropertyName:
                return VisitComputedPropertyName(node);
            case Kind.MethodDeclaration:
            case Kind.SetAccessor:
            case Kind.GetAccessor:
            case Kind.FunctionExpression:
            case Kind.FunctionDeclaration:
            {
                EnterOther();
                var result = Visitor().VisitEachChild(node);
                ExitOther();
                return result;
            }

            default:
                return Visitor().VisitEachChild(node);
        }
    }

    private Node? ModifierVisitorVisit(Node node)
    {
        if (node.Kind == Kind.Decorator)
        {
            return null;
        }

        return node;
    }

    private Node? ClassElementVisitorVisit(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Constructor:
                return VisitConstructorDeclaration(node);
            case Kind.MethodDeclaration:
                return VisitMethodDeclaration(node);
            case Kind.GetAccessor:
                return VisitGetAccessorDeclaration(node);
            case Kind.SetAccessor:
                return VisitSetAccessorDeclaration(node);
            case Kind.PropertyDeclaration:
                return VisitPropertyDeclaration(node);
            case Kind.ClassStaticBlockDeclaration:
                return VisitClassStaticBlockDeclaration(node);
            default:
                return Visit(node);
        }
    }

    private Node? DiscardedValueVisit(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PrefixUnaryExpression:
            case Kind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, true /*discarded*/);
            case Kind.BinaryExpression:
                return VisitBinaryExpression(node, true /*discarded*/);
            case Kind.ParenthesizedExpression:
                return VisitParenthesizedExpression(node, true /*discarded*/);
            case Kind.PartiallyEmittedExpression:
                return VisitPartiallyEmittedExpression(node, true /*discarded*/);
            default:
                return Visit(node);
        }
    }

    private Node? NonConstructorClassElementVisit(Node node)
    {
        if (IsConstructorDeclaration(node))
        {
            return node; // skip constructors in pass 1
        }

        return ClassElementVisitorVisit(node);
    }

    private Node? ConstructorClassElementVisit(Node node)
    {
        if (IsConstructorDeclaration(node))
        {
            return ClassElementVisitorVisit(node);
        }

        return node;
    }

    private Node? ExportStrippingModifierVisit(Node node)
    {
        if (node.Kind == Kind.ExportKeyword)
        {
            return null;
        }

        return ModifierVisitorVisit(node);
    }

    private Node CreateHelperVariable(Node node, string suffix)
    {
        return Factory().NewUniqueNameEx(
            GetHelperVariableName(EmitContext(), node) + "_" + suffix,
            new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes,
            });
    }

    private Node CreateLet(Node name, Node? initializer)
    {
        return Factory().NewVariableStatement(
            null,
            Factory().NewVariableDeclarationList(
                Factory().NewNodeList([
                    Factory().NewVariableDeclaration(name, null, null, initializer),
                ]),
                NodeFlags.Let));
    }

    private ClassInfo CreateClassInfo(Node node)
    {
        var f = Factory();
        var ci = new ClassInfo
        {
            Class = node,
            MetadataReference = f.NewUniqueNameEx("_metadata", new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel,
            }),
        };

        // Before visiting we perform a first pass to collect information we'll need
        // as we descend.

        // If the class itself is decorated, create a _classThis binding
        if (NodeIsDecorated(false, node, null, null))
        {
            var needsUniqueClassThis = Some(node.Members(), member =>
                (IsPrivateIdentifierClassElementDeclaration(member) || IsAutoAccessorPropertyDeclaration(member)) && HasStaticModifier(member));
            // We do not mark _classThis as FileLevel if it may be reused by class private fields, which requires the
            // ability access the captured `_classThis` of outer scopes.
            var flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel;
            if (needsUniqueClassThis)
            {
                flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes;
            }

            ci.ClassThis = f.NewUniqueNameEx("_classThis", new AutoGenerateOptions { Flags = flags });
        }

        foreach (var member in node.Members()!)
        {
            if (IsMethodOrAccessor(member) && NodeOrChildIsDecorated(false, member, node, null))
            {
                if (HasStaticModifier(member))
                {
                    if (ci.StaticMethodExtraInitializersName is null)
                    {
                        ci.StaticMethodExtraInitializersName = f.NewUniqueNameEx("_staticExtraInitializers", new AutoGenerateOptions
                        {
                            Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel,
                        });
                        Node renamedClassThis = ci.ClassThis ?? f.NewThisExpression();
                        var initializer = f.NewRunInitializersHelper(renamedClassThis, ci.StaticMethodExtraInitializersName, null);
                        var nameRange = node.Name();
                        if (nameRange is not null)
                        {
                            EmitContext().SetSourceMapRange(initializer, nameRange.Loc);
                        }
                        else
                        {
                            EmitContext().SetSourceMapRange(initializer, MoveRangePastDecorators(node));
                        }

                        (ci.PendingStaticInitializers ??= []).Add(initializer);
                    }
                }
                else
                {
                    if (ci.InstanceMethodExtraInitializersName is null)
                    {
                        ci.InstanceMethodExtraInitializersName = f.NewUniqueNameEx("_instanceExtraInitializers", new AutoGenerateOptions
                        {
                            Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel,
                        });
                        var initializer = f.NewRunInitializersHelper(f.NewThisExpression(), ci.InstanceMethodExtraInitializersName, null);
                        var nameRange = node.Name();
                        if (nameRange is not null)
                        {
                            EmitContext().SetSourceMapRange(initializer, nameRange.Loc);
                        }
                        else
                        {
                            EmitContext().SetSourceMapRange(initializer, MoveRangePastDecorators(node));
                        }

                        (ci.PendingInstanceInitializers ??= []).Add(initializer);
                    }
                }
            }

            if (IsClassStaticBlockDeclaration(member))
            {
                if (!IsClassNamedEvaluationHelperBlock(EmitContext(), member))
                {
                    ci.HasStaticInitializers = true;
                }
            }
            else if (IsPropertyDeclaration(member))
            {
                if (HasStaticModifier(member))
                {
                    ci.HasStaticInitializers = ci.HasStaticInitializers || member.Initializer() is not null || HasDecorators(member);
                }
                else
                {
                    ci.HasNonAmbientInstanceFields = ci.HasNonAmbientInstanceFields || !HasSyntacticModifier(member, ModifierFlags.Ambient);
                }
            }

            if ((IsPrivateIdentifierClassElementDeclaration(member) || IsAutoAccessorPropertyDeclaration(member)) && HasStaticModifier(member))
            {
                ci.HasStaticPrivateClassElements = true;
            }

            // exit early if possible
            if (ci.StaticMethodExtraInitializersName is not null &&
                ci.InstanceMethodExtraInitializersName is not null &&
                ci.HasStaticInitializers &&
                ci.HasNonAmbientInstanceFields &&
                ci.HasStaticPrivateClassElements)
            {
                break;
            }
        }

        return ci;
    }
}

public static partial class EsTransforms
{
    /// <summary>Go: <c>func newESDecoratorTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>（esdecorator.go:124）。</summary>
    internal static Transformer? NewESDecoratorTransformer(TransformOptions opts)
    {
        // When experimentalDecorators is set, the legacy decorator transformer handles all
        // decorators. When targeting ESNext with useDefineForClassFields, there's nothing to
        // transform. In either case every node would be returned unchanged, so skip entirely.
        if (opts.CompilerOptions!.ExperimentalDecorators.IsTrue() ||
            (opts.CompilerOptions.GetEmitScriptTarget() >= ScriptTarget.ESNext && opts.CompilerOptions.GetUseDefineForClassFields()))
        {
            return null;
        }

        var tx = new EsDecoratorTransformer(opts.CompilerOptions);
        var result = tx.NewTransformer(tx.Visit, opts.Context);
        tx.InitVisitors();
        return result;
    }

    /// <summary>Go: <c>func getHelperVariableName(ec *printer.EmitContext, node *ast.Node) string</c>（esdecorator.go:431）。</summary>
    internal static string GetHelperVariableName(EmitContext ec, Node node)
    {
        var name = node.Name();
        string declarationName;
        if (name is not null && IsIdentifier(name) && !IsGeneratedIdentifier(ec, name))
        {
            declarationName = name.Text();
        }
        else if (name is not null && IsPrivateIdentifier(name) && !ec.HasAutoGenerateInfo(name))
        {
            var text = name.Text();
            declarationName = text.Length > 1 ? text[1..] : "";
        }
        else if (name is not null && IsStringLiteral(name) && IsIdentifierText(name.Text(), LanguageVariant.Standard))
        {
            declarationName = name.Text();
        }
        else if (IsClassLike(node))
        {
            declarationName = "class";
        }
        else
        {
            declarationName = "member";
        }

        if (IsGetAccessorDeclaration(node))
        {
            declarationName = "get_" + declarationName;
        }

        if (IsSetAccessorDeclaration(node))
        {
            declarationName = "set_" + declarationName;
        }

        if (name is not null && IsPrivateIdentifier(name))
        {
            declarationName = "private_" + declarationName;
        }

        if (IsStatic(node))
        {
            declarationName = "static_" + declarationName;
        }

        return "_" + declarationName;
    }

    /// <summary>Go: <c>func isDecoratedClassLike(node *ast.Node) bool</c>（esdecorator.go:1014）。</summary>
    internal static bool IsDecoratedClassLike(Node node)
    {
        return ClassOrConstructorParameterIsDecorated(false, node) ||
            ChildIsDecorated(false, node, null);
    }
}

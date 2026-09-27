// ported from: internal/transformers/destructuring.go（510 行，全部）
//
// Go 包级函数 → 静态类 `Transformers`（partial）；未导出类型 `flattener`/`pendingDecl`/`restIdElemPair`
// 按 C# 习惯命名 → `Flattener`/`PendingDecl`/`RestIdElemPair`（docs §2；未导出字段仍 `_camelCase`）。
//
// 与 Go 的偏离（C# 语言特性）：
// 1) Go 的 `[]*ast.Node` + `append`/`nil`（下游只观察 `len(x)` 与下标）→ C# `List<Node>`；Go 的 `x = nil` → `Clear()`。
// 2) Go 的**函数类型字段**（模式回调：Go 里用方法值 `(*flattener).emitAssignment` 赋值、调用时额外传 `f`）
//    → C# 具名委托 + 实例方法组（不再需要显式 `f` 参数，接收者就是 `this`）：
//      emitBindingOrAssignment                → EmitBindingOrAssignmentFunc（字段 `_emitBindingOrAssignment`）
//      createArrayBindingOrAssignmentPattern  → CreateBindingOrAssignmentPatternFunc
//      createObjectBindingOrAssignmentPattern → CreateBindingOrAssignmentPatternFunc
//      createArrayBindingOrAssignmentElement  → CreateBindingOrAssignmentElementFunc
//    调用点由 `f.emitBindingOrAssignment(f, …)` 变为 `_emitBindingOrAssignment(…)`。
// 3) Go 的 `newFlattener(tx, level)`（包级构造函数）→ C# `new Flattener(tx, level)` 构造函数。
// 4) Go 的 `&location`（`CreateAssignmentCallback` 的第三参是 `*core.TextRange`）→ C# **按值**传 `TextRange`（struct）。
// 5) `strconv.Itoa(i)` → `i.ToString(CultureInfo.InvariantCulture)`；`slices.ContainsFunc` → `Array.Exists`。
// 6) 节点类型：别名（`ast.Expression`/`ast.Statement`/`ast.BindingName`… = `Node`）→ `Node`；具体结构体
//    （`*ast.BindingElement`/`*ast.VariableDeclaration`/`*ast.BindingPattern`）→ 对应生成类；`element.AsNode()` 退化为 `element`。
// 7) Go 里 `x.Name()`/`x.Loc` 的 nil 接收者会 panic → C# 用 `!` 表达同一契约（docs §3.2）；`TextRange{}` 零值 → `default`。
// 8) 回调方/入口方法从静态类 `Transformers` 调用 → 必须是 `internal`（Go 未导出方法在同一 package 内可见的对应物）。
using System.Collections.Generic;
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Transformers; // Go 的包级函数（本文件里 Flattener 的调用点按 Go 裸名写）

namespace TypeScript.Transformers;

/// <summary>FlattenLevel controls how deeply binding/assignment patterns are decomposed.</summary>
public enum FlattenLevel
{
    /// <summary>Go: <c>FlattenLevelAll</c> — Fully decompose all patterns into individual assignments/bindings.</summary>
    All = 0,

    /// <summary>Go: <c>FlattenLevelObjectRest</c> — Only decompose patterns containing object rest elements.</summary>
    ObjectRest = 1,
}

/// <summary>
/// CreateAssignmentCallback is a callback used to create custom assignment expressions during destructuring flattening.
/// When provided, the target will always be an Identifier, and the callback can wrap the assignment with additional logic
/// (e.g., export expressions in CJS modules or namespace member assignments).<br/>
/// Go: <c>type CreateAssignmentCallback func(name *ast.IdentifierNode, value *ast.Expression, location *core.TextRange) *ast.Expression</c>。
/// </summary>
public delegate Node CreateAssignmentCallback(Node name, Node value, TextRange location);

/// <summary>Go: <c>type pendingDecl struct</c>（未导出类型 → C# 习惯命名）。</summary>
internal sealed class PendingDecl
{
    internal List<Node>? _pendingExpressions;
    internal Node _name = null!;
    internal Node? _value;
    internal TextRange _location;
    internal Node? _original;
}

/// <summary>Go: <c>type restIdElemPair struct { id *ast.Node; element *ast.Node }</c>。</summary>
internal readonly struct RestIdElemPair(Node id, Node element)
{
    internal readonly Node _id = id;
    internal readonly Node _element = element;
}

/// <summary>模式回调：Go 的 <c>emitBindingOrAssignment func(f *flattener, target, value *ast.Node, location core.TextRange, original *ast.Node)</c>。</summary>
internal delegate void EmitBindingOrAssignmentFunc(Node target, Node? value, TextRange location, Node? original);

/// <summary>模式回调：Go 的 <c>createArray/ObjectBindingOrAssignmentPattern func(f *flattener, elements []*ast.Node) *ast.Node</c>。</summary>
internal delegate Node CreateBindingOrAssignmentPatternFunc(Node[] elements);

/// <summary>模式回调：Go 的 <c>createArrayBindingOrAssignmentElement func(f *flattener, expr *ast.Node) *ast.Node</c>。</summary>
internal delegate Node CreateBindingOrAssignmentElementFunc(Node expr);

/// <summary>
/// flattener encapsulates the state and logic for flattening destructuring patterns.
/// It is equivalent to TypeScript's FlattenContext in destructuring.ts.<br/>
/// Go: <c>type flattener struct</c>。
/// </summary>
internal sealed class Flattener
{
    private readonly Transformer _tx;
    private readonly FlattenLevel _level;

    internal CreateAssignmentCallback? _createAssignmentCallback;

    // State
    private readonly List<Node> _expressions = [];
    private readonly List<PendingDecl> _declarations = [];
    private bool _hasTransformedPriorElement;
    internal bool _hoistTempVariables;

    // Mode callbacks (set by FlattenDestructuringAssignment or FlattenDestructuringBinding)
    internal EmitBindingOrAssignmentFunc _emitBindingOrAssignment = null!;
    internal CreateBindingOrAssignmentPatternFunc _createArrayBindingOrAssignmentPattern = null!;
    internal CreateBindingOrAssignmentPatternFunc _createObjectBindingOrAssignmentPattern = null!;
    internal CreateBindingOrAssignmentElementFunc _createArrayBindingOrAssignmentElement = null!;

    /// <summary>Go: <c>func newFlattener(tx *Transformer, level FlattenLevel) *flattener</c>。</summary>
    internal Flattener(Transformer tx, FlattenLevel level)
    {
        _tx = tx;
        _level = level;
    }

    // --- Assignment mode callbacks ---

    internal Node CreateArrayAssignmentPattern(Node[] elements) =>
        _tx.Factory().NewArrayLiteralExpression(_tx.Factory().NewNodeList(elements), false);

    internal Node CreateObjectAssignmentPattern(Node[] elements) =>
        _tx.Factory().NewObjectLiteralExpression(_tx.Factory().NewNodeList(elements), false);

    internal Node CreateArrayAssignmentElement(Node expr) => expr;

    internal void EmitAssignment(Node target, Node? value, TextRange location, Node? original)
    {
        Node expression;
        if (_createAssignmentCallback is not null && IsIdentifier(target))
        {
            expression = _createAssignmentCallback(target, value!, location);
        }
        else
        {
            expression = _tx.Factory().NewAssignmentExpression(_tx.Visitor().VisitNode(target)!, value!);
            expression.Loc = location;
        }
        _tx.EmitContext().SetOriginal(expression, original);
        EmitExpression(expression);
    }

    // --- Binding mode callbacks ---

    internal Node CreateArrayBindingPattern(Node[] elements) =>
        _tx.Factory().NewBindingPattern(Kind.ArrayBindingPattern, _tx.Factory().NewNodeList(elements));

    internal Node CreateObjectBindingPattern(Node[] elements) =>
        _tx.Factory().NewBindingPattern(Kind.ObjectBindingPattern, _tx.Factory().NewNodeList(elements));

    internal Node CreateArrayBindingElement(Node expr) => _tx.Factory().NewBindingElement(null, null, expr, null);

    internal void EmitBinding(Node target, Node? value, TextRange location, Node? original)
    {
        if (_expressions.Count > 0)
        {
            // Go: value = f.tx.Factory().InlineExpressions(append(f.expressions, value)); f.expressions = nil
            _expressions.Add(value!);
            value = _tx.Factory().InlineExpressions(_expressions.ToArray());
            _expressions.Clear();
        }
        _declarations.Add(new PendingDecl
        {
            _name = target,
            _value = value,
            _location = location,
            _original = original,
        });
    }

    // --- Shared helpers ---

    private void EmitExpression(Node expr) => _expressions.Add(expr);

    private Node EnsureIdentifier(Node value, bool reuseIdentifierExpressions, TextRange location)
    {
        if (reuseIdentifierExpressions && IsIdentifier(value))
        {
            return value;
        }
        var temp = _tx.Factory().NewTempVariable();
        if (_hoistTempVariables)
        {
            _tx.EmitContext().AddVariableDeclaration(temp);
            var assign = _tx.Factory().NewAssignmentExpression(temp, value);
            assign.Loc = location;
            EmitExpression(assign);
        }
        else
        {
            _emitBindingOrAssignment(temp, value, location, null);
        }
        return temp;
    }

    private Node CreateDefaultValueCheck(Node value, Node defaultValue, TextRange location)
    {
        value = EnsureIdentifier(value, true, location);
        return _tx.Factory().NewConditionalExpression(
            _tx.Factory().NewTypeCheck(value, "undefined"),
            _tx.Factory().NewToken(Kind.QuestionToken),
            defaultValue,
            _tx.Factory().NewToken(Kind.ColonToken),
            value);
    }

    private Node CreateDestructuringPropertyAccess(Node value, Node propertyName)
    {
        if (IsComputedPropertyName(propertyName))
        {
            var argumentExpression = EnsureIdentifier(_tx.Visitor().VisitNode(propertyName.Expression())!, false, propertyName.Loc);
            return _tx.Factory().NewElementAccessExpression(value, null, argumentExpression, NodeFlags.None);
        }
        else if (IsStringOrNumericLiteralLike(propertyName) || IsBigIntLiteral(propertyName))
        {
            var argumentExpression = propertyName.Clone(_tx.Factory())!;
            return _tx.Factory().NewElementAccessExpression(value, null, argumentExpression, NodeFlags.None);
        }
        else
        {
            var name = _tx.Factory().NewIdentifier(propertyName.Text()!);
            return _tx.Factory().NewPropertyAccessExpression(value, null, name, NodeFlags.None);
        }
    }

    // --- Entry points ---

    internal Node? FlattenDestructuringAssignment(Node node, bool needsValue)
    {
        var location = node.Loc;
        Node? value = null;
        if (IsDestructuringAssignment(node))
        {
            value = node.AsBinaryExpression()!.Right;
            while (IsEmptyArrayLiteral(node.AsBinaryExpression()!.Left!) || IsEmptyObjectLiteral(node.AsBinaryExpression()!.Left!))
            {
                if (IsDestructuringAssignment(value!))
                {
                    node = value!;
                    location = node.Loc;
                    value = node.AsBinaryExpression()!.Right;
                }
                else
                {
                    return _tx.Visitor().VisitNode(value!);
                }
            }
        }

        if (value is not null)
        {
            value = _tx.Visitor().VisitNode(value);
            if ((IsIdentifier(value!) && BindingOrAssignmentElementAssignsToName(node, value!.Text()!))
                || BindingOrAssignmentElementContainsNonLiteralComputedName(node))
            {
                value = EnsureIdentifier(value!, false, location);
            }
            else if (needsValue)
            {
                value = EnsureIdentifier(value!, true, location);
            }
            else if (NodeIsSynthesized(node))
            {
                location = value!.Loc;
            }
        }

        FlattenBindingOrAssignmentElement(node, value, location, IsDestructuringAssignment(node));

        if (value is not null && needsValue)
        {
            if (_expressions.Count == 0)
            {
                return value;
            }
            _expressions.Add(value);
        }

        var res = _tx.Factory().InlineExpressions(_expressions.ToArray());
        if (res is not null)
        {
            return res;
        }
        return _tx.Factory().NewOmittedExpression();
    }

    internal Node? FlattenDestructuringBinding(Node node, Node? rval, bool skipInitializer)
    {
        if (IsVariableDeclaration(node))
        {
            var initializer = GetInitializerOfBindingOrAssignmentElement(node);
            if (initializer is not null
                && ((IsIdentifier(initializer) && BindingOrAssignmentElementAssignsToName(node, initializer.Text()!))
                    || BindingOrAssignmentElementContainsNonLiteralComputedName(node)))
            {
                initializer = EnsureIdentifier(_tx.Visitor().VisitNode(initializer)!, false, initializer.Loc);
                node = _tx.Factory().UpdateVariableDeclaration(node.AsVariableDeclaration()!, node.Name(), null, null, initializer);
            }
        }

        FlattenBindingOrAssignmentElement(node, rval, node.Loc, skipInitializer);

        if (_expressions.Count > 0)
        {
            var temp = _tx.Factory().NewTempVariable();
            if (_hoistTempVariables)
            {
                var value = _tx.Factory().InlineExpressions(_expressions.ToArray())!;
                _expressions.Clear();
                _emitBindingOrAssignment(temp, value, default, null);
            }
            else
            {
                _tx.EmitContext().AddVariableDeclaration(temp);
                var last = _declarations[^1];
                (last._pendingExpressions ??= []).Add(_tx.Factory().NewAssignmentExpression(temp, last._value!));
                last._pendingExpressions!.AddRange(_expressions);
                last._value = temp;
            }
        }

        var decls = new List<Node>(_declarations.Count);
        foreach (var pending in _declarations)
        {
            Node? expr = pending._value;
            if (pending._pendingExpressions is { Count: > 0 } pendingExpressions)
            {
                // Go: InlineExpressions(append(pending.pendingExpressions, pending.value)) —— append 不改动 f.declarations 里的原切片。
                var all = new Node[pendingExpressions.Count + 1];
                pendingExpressions.CopyTo(all);
                all[^1] = pending._value!;
                expr = _tx.Factory().InlineExpressions(all);
            }
            var decl = _tx.Factory().NewVariableDeclaration(pending._name, null, null, expr);
            decl.Loc = pending._location;
            if (pending._original is not null)
            {
                _tx.EmitContext().SetOriginal(decl, pending._original);
            }
            decls.Add(decl);
        }

        if (decls.Count == 1)
        {
            return decls[0];
        }
        if (decls.Count == 0)
        {
            return null;
        }
        return _tx.Factory().NewSyntaxList(decls.ToArray());
    }

    // --- Core flattening ---

    private void FlattenBindingOrAssignmentElement(Node element, Node? value, TextRange location, bool skipInitializer)
    {
        var bindingTarget = GetTargetOfBindingOrAssignmentElement(element);
        if (bindingTarget is null)
        {
            return;
        }
        if (!skipInitializer)
        {
            var initializer = _tx.Visitor().VisitNode(GetInitializerOfBindingOrAssignmentElement(element));
            if (initializer is not null)
            {
                if (value is not null)
                {
                    value = CreateDefaultValueCheck(value, initializer, location);
                    if (!IsSimpleCopiableExpression(initializer) && (IsBindingPattern(bindingTarget) || IsAssignmentPattern(bindingTarget)))
                    {
                        value = EnsureIdentifier(value, true, location);
                    }
                }
                else
                {
                    value = initializer;
                }
            }
            else if (value is null)
            {
                value = _tx.Factory().NewVoidZeroExpression();
            }
        }

        if (IsObjectBindingOrAssignmentPattern(bindingTarget))
        {
            FlattenObjectBindingOrAssignmentPattern(element, bindingTarget, value, location);
        }
        else if (IsArrayBindingOrAssignmentPattern(bindingTarget))
        {
            FlattenArrayBindingOrAssignmentPattern(element, bindingTarget, value, location);
        }
        else
        {
            _emitBindingOrAssignment(bindingTarget, value, location, element);
        }
    }

    private void FlattenObjectBindingOrAssignmentPattern(Node parent, Node pattern, Node? value, TextRange location)
    {
        var elements = GetElementsOfBindingOrAssignmentPattern(pattern);
        var numElements = elements.Length;
        if (numElements != 1)
        {
            var reuseIdentifierExpressions = !IsDeclarationBindingElement(parent) || numElements != 0;
            value = EnsureIdentifier(value!, reuseIdentifierExpressions, location);
        }
        List<Node>? bindingElements = null;
        List<Node>? computedTempVariables = null;
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            if (GetRestIndicatorOfBindingOrAssignmentElement(element) is null)
            {
                var propertyName = TryGetPropertyNameOfBindingOrAssignmentElement(element);
                if (_level >= FlattenLevel.ObjectRest
                    && (element.SubtreeFacts & (SubtreeFacts.ContainsRestOrSpread | SubtreeFacts.ContainsObjectRestOrSpread)) == 0
                    && (GetTargetOfBindingOrAssignmentElement(element)!.SubtreeFacts & (SubtreeFacts.ContainsRestOrSpread | SubtreeFacts.ContainsObjectRestOrSpread)) == 0
                    && !IsComputedPropertyName(propertyName!))
                {
                    (bindingElements ??= []).Add(_tx.Visitor().VisitNode(element)!);
                }
                else
                {
                    if (bindingElements is { Count: > 0 })
                    {
                        _emitBindingOrAssignment(_createObjectBindingOrAssignmentPattern(bindingElements.ToArray()), value!, location, pattern);
                        bindingElements.Clear();
                    }
                    var rhsValue = CreateDestructuringPropertyAccess(value!, propertyName!);
                    if (IsComputedPropertyName(propertyName!))
                    {
                        (computedTempVariables ??= []).Add(rhsValue.AsElementAccessExpression()!.ArgumentExpression!);
                    }
                    FlattenBindingOrAssignmentElement(element, rhsValue, element.Loc, false);
                }
            }
            else if (i == numElements - 1)
            {
                if (bindingElements is { Count: > 0 })
                {
                    _emitBindingOrAssignment(_createObjectBindingOrAssignmentPattern(bindingElements.ToArray()), value!, location, pattern);
                    bindingElements.Clear();
                }
                var rhsValue = _tx.Factory().NewRestHelper(
                    value!,
                    elements,
                    computedTempVariables is { Count: > 0 } ? computedTempVariables.ToArray() : null,
                    pattern.Loc);
                FlattenBindingOrAssignmentElement(element, rhsValue, element.Loc, false);
            }
        }
        if (bindingElements is { Count: > 0 })
        {
            _emitBindingOrAssignment(_createObjectBindingOrAssignmentPattern(bindingElements.ToArray()), value!, location, pattern);
        }
    }

    private void FlattenArrayBindingOrAssignmentPattern(Node parent, Node pattern, Node? value, TextRange location)
    {
        var elements = GetElementsOfBindingOrAssignmentPattern(pattern);
        var numElements = elements.Length;
        if ((numElements != 1 && (_level < FlattenLevel.ObjectRest || numElements == 0)) || Every(elements, IsOmittedExpression))
        {
            var reuseIdentifierExpressions = !IsDeclarationBindingElement(parent) || numElements != 0;
            value = EnsureIdentifier(value!, reuseIdentifierExpressions, location);
        }
        List<Node>? bindingElements = null;
        List<RestIdElemPair>? restContainingElements = null;
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            if (_level >= FlattenLevel.ObjectRest)
            {
                if ((element.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) != 0
                    || (_hasTransformedPriorElement && !IsSimpleBindingOrAssignmentElement(element)))
                {
                    _hasTransformedPriorElement = true;
                    var temp = _tx.Factory().NewTempVariable();
                    if (_hoistTempVariables)
                    {
                        _tx.EmitContext().AddVariableDeclaration(temp);
                    }
                    (restContainingElements ??= []).Add(new RestIdElemPair(temp, element));
                    (bindingElements ??= []).Add(_createArrayBindingOrAssignmentElement(temp));
                }
                else
                {
                    (bindingElements ??= []).Add(element);
                }
            }
            else if (IsOmittedExpression(element))
            {
                continue;
            }
            else if (GetRestIndicatorOfBindingOrAssignmentElement(element) is null)
            {
                var rhsValue = _tx.Factory().NewElementAccessExpression(
                    value,
                    null,
                    _tx.Factory().NewNumericLiteral(i.ToString(CultureInfo.InvariantCulture), TokenFlags.None),
                    NodeFlags.None);
                FlattenBindingOrAssignmentElement(element, rhsValue, element.Loc, false);
            }
            else if (i == numElements - 1)
            {
                var rhsValue = _tx.Factory().NewArraySliceCall(value!, i);
                FlattenBindingOrAssignmentElement(element, rhsValue, element.Loc, false);
            }
        }
        if (bindingElements is { Count: > 0 })
        {
            _emitBindingOrAssignment(_createArrayBindingOrAssignmentPattern(bindingElements.ToArray()), value!, location, pattern);
        }
        if (restContainingElements is { Count: > 0 })
        {
            foreach (var pair in restContainingElements)
            {
                FlattenBindingOrAssignmentElement(pair._element, pair._id, pair._element.Loc, false);
            }
        }
    }
}

// --- Exported helper functions ---

public static partial class Transformers
{
    /// <summary>
    /// FlattenDestructuringAssignment flattens a destructuring assignment expression into a sequence of
    /// individual property/element access assignments. Supports custom assignment callbacks for module
    /// export or namespace member expressions.<br/>
    /// Go: <c>func FlattenDestructuringAssignment(tx *Transformer, node *ast.Node, needsValue bool, level FlattenLevel, createAssignmentCallback CreateAssignmentCallback) *ast.Expression</c>。
    /// </summary>
    public static Node? FlattenDestructuringAssignment(
        Transformer tx,
        Node node, // VariableDeclaration | DestructuringAssignment
        bool needsValue,
        FlattenLevel level,
        CreateAssignmentCallback? createAssignmentCallback)
    {
        var f = new Flattener(tx, level);
        f._createAssignmentCallback = createAssignmentCallback;
        f._hoistTempVariables = true;
        // Assignment mode callbacks
        // Go: f.emitBindingOrAssignment = (*flattener).emitAssignment 等（C# 用实例方法组，接收者即 f）
        f._emitBindingOrAssignment = f.EmitAssignment;
        f._createArrayBindingOrAssignmentPattern = f.CreateArrayAssignmentPattern;
        f._createObjectBindingOrAssignmentPattern = f.CreateObjectAssignmentPattern;
        f._createArrayBindingOrAssignmentElement = f.CreateArrayAssignmentElement;
        return f.FlattenDestructuringAssignment(node, needsValue);
    }

    /// <summary>
    /// FlattenDestructuringBinding flattens a binding pattern in a variable declaration or parameter
    /// into individual variable declarations. Returns a single VariableDeclaration, a SyntaxList of
    /// declarations, or null.<br/>
    /// Go: <c>func FlattenDestructuringBinding(tx *Transformer, node *ast.Node, rval *ast.Node, level FlattenLevel, hoistTempVariables bool, skipInitializer bool) *ast.Node</c>。
    /// </summary>
    public static Node? FlattenDestructuringBinding(
        Transformer tx,
        Node node, // VariableDeclaration | ParameterDeclaration | BindingElement
        Node? rval,
        FlattenLevel level,
        bool hoistTempVariables,
        bool skipInitializer)
    {
        var f = new Flattener(tx, level);
        f._hoistTempVariables = hoistTempVariables;
        // Binding mode callbacks
        f._emitBindingOrAssignment = f.EmitBinding;
        f._createArrayBindingOrAssignmentPattern = f.CreateArrayBindingPattern;
        f._createObjectBindingOrAssignmentPattern = f.CreateObjectBindingPattern;
        f._createArrayBindingOrAssignmentElement = f.CreateArrayBindingElement;
        return f.FlattenDestructuringBinding(node, rval, skipInitializer);
    }

    /// <summary>BindingOrAssignmentElementAssignsToName checks if any target in a binding/assignment pattern assigns to the given name.</summary>
    public static bool BindingOrAssignmentElementAssignsToName(Node element, string name)
    {
        var target = GetTargetOfBindingOrAssignmentElement(element);
        if (target is null)
        {
            return false;
        }
        if (IsBindingPattern(target) || IsAssignmentPattern(target))
        {
            return BindingOrAssignmentPatternAssignsToName(target, name);
        }
        else if (IsIdentifier(target))
        {
            return target.Text() == name;
        }
        return false;
    }

    private static bool BindingOrAssignmentPatternAssignsToName(Node pattern, string name)
    {
        var elements = GetElementsOfBindingOrAssignmentPattern(pattern);
        foreach (var element in elements)
        {
            if (BindingOrAssignmentElementAssignsToName(element, name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>BindingOrAssignmentElementContainsNonLiteralComputedName checks if any element has a non-literal computed property name.</summary>
    public static bool BindingOrAssignmentElementContainsNonLiteralComputedName(Node element)
    {
        var propertyName = TryGetPropertyNameOfBindingOrAssignmentElement(element);
        if (propertyName is not null && IsComputedPropertyName(propertyName) && !IsLiteralExpression(propertyName.Expression()!))
        {
            return true;
        }
        var target = GetTargetOfBindingOrAssignmentElement(element);
        return target is not null
            && (IsBindingPattern(target) || IsAssignmentPattern(target))
            && BindingOrAssignmentPatternContainsNonLiteralComputedName(target);
    }

    private static bool BindingOrAssignmentPatternContainsNonLiteralComputedName(Node pattern)
    {
        var elements = GetElementsOfBindingOrAssignmentPattern(pattern);
        // Go: slices.ContainsFunc(elements, BindingOrAssignmentElementContainsNonLiteralComputedName)
        return System.Array.Exists(elements, BindingOrAssignmentElementContainsNonLiteralComputedName);
    }

    /// <summary>GetInitializerOfBindingOrAssignmentElement returns the initializer/default value of a binding or assignment element.</summary>
    public static Node? GetInitializerOfBindingOrAssignmentElement(Node? bindingElement)
    {
        if (bindingElement is null)
        {
            return null;
        }
        if (IsDeclarationBindingElement(bindingElement))
        {
            return bindingElement.Initializer();
        }
        if (IsPropertyAssignment(bindingElement))
        {
            var initializer = bindingElement.Initializer();
            if (IsAssignmentExpression(initializer, true))
            {
                return initializer!.AsBinaryExpression()!.Right;
            }
            return null;
        }
        if (IsShorthandPropertyAssignment(bindingElement))
        {
            return bindingElement.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer;
        }
        if (IsAssignmentExpression(bindingElement, true))
        {
            return bindingElement.AsBinaryExpression()!.Right;
        }
        if (IsSpreadElement(bindingElement))
        {
            return GetInitializerOfBindingOrAssignmentElement(bindingElement.Expression());
        }
        return null;
    }

    internal static bool IsObjectBindingOrAssignmentPattern(Node? node) =>
        node is not null && (node.Kind == Kind.ObjectBindingPattern || node.Kind == Kind.ObjectLiteralExpression);

    internal static bool IsArrayBindingOrAssignmentPattern(Node? node) =>
        node is not null && (node.Kind == Kind.ArrayBindingPattern || node.Kind == Kind.ArrayLiteralExpression);

    internal static bool IsSimpleBindingOrAssignmentElement(Node element)
    {
        var target = GetTargetOfBindingOrAssignmentElement(element);
        if (target is null || IsOmittedExpression(target))
        {
            return true;
        }
        var propertyName = TryGetPropertyNameOfBindingOrAssignmentElement(element);
        if (propertyName is not null && !IsPropertyNameLiteral(propertyName))
        {
            return false;
        }
        var initializer = GetInitializerOfBindingOrAssignmentElement(element);
        if (initializer is not null && !IsSimpleInlineableExpression(initializer))
        {
            return false;
        }
        if (IsBindingPattern(target) || IsAssignmentPattern(target))
        {
            return Every(GetElementsOfBindingOrAssignmentPattern(target), IsSimpleBindingOrAssignmentElement);
        }
        return IsIdentifier(target);
    }
}

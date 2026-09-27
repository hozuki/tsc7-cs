// ported from: internal/binder/binder.go:1-751（ContainerFlags / ExpandoAssignmentInfo / Binder / ActiveLabel；
//               binderPool + getBinder/putBinder + BindSourceFile/bindSourceFile；newSymbol、declareSymbol、
//               declareSymbolEx、getDeclarationName、getDisplayName、GetSymbolNameForPrivateIdentifier、
//               declareModuleMember/declareClassMember/declareSourceFileMember/declareSymbolAndAddToSymbolTable、
//               流图工厂（newFlowNode* / create*Label / createFlow* / newFlowList / combineFlowLists /
//               newSingleDeclaration / setFlowNodeReferenced / addAntecedent / finishFlowLabel）、
//               bind 调度器（binder.go 的 `func (b *Binder) bind`））
//               + internal/binder/binder.go:2728-2802（errorOnNode/errorOnFirstToken/createDiagnosticForNode/
//               addDiagnostic、isSignedNumericLiteral/getOptionalSymbolFlagForNode/isFunctionSymbol/
//               isStatementCondition/isTopLevelLogicalExpression/isAssignmentDeclaration/isEffectiveModuleDeclaration）
//
// 包级函数（Go 的 `func X(...)`）按 docs/architecture.md §2 放进与本 package 同名的类。Go 的包 `binder` 同时有
// 类型 `Binder` 和包级函数，C# 里两者合并成同一个 `Binder` 类（先例：`Parser` 类的 `Parser.ParseSourceFile` 是
// Go 的包级 `ParseSourceFile`）：导出的包级函数 → `public static`，未导出 → `private static`。
//
// 与 Go 的偏离（逐条；其余地方与 Go 逐行对应）：
//
// 1) `binderPool`（Go 的 `sync.Pool`）→ `[ThreadStatic]` 单槽缓存（理由与 `Parser` 的文件头完全相同）：
//    AOT 安全、无锁、容量有界。差异：(a) Go 的池是进程级的，本实现只在同线程复用；(b) Go 的池在 GC 时清空，
//    本实现留到线程退役（只会更省分配）。`putBinder` 的 `*b = Binder{bindFunc: b.bindFunc}` → `Reset()` 逐字段
//    复位（见 `Reset` 的注释：`_file` 与 `_bindFunc` 保留/由 `GetBinder` 重设）。
// 2) `core.Arena[T]` 全部删除（`symbolArena`/`flowNodeArena`/`flowListArena`/`singleDeclarationsArena`）：
//    Go 用 arena 是为了让 `Symbol`/`FlowNode`/`FlowList`（结构体）紧凑存放 + 返回稳定指针；这些类型在 C# 里
//    是 class（引用类型），arena 给不出 slab 语义（docs/architecture.md §3.2、docs/ast.md 里 NodeFactory 的
//    先例）→ 直接 `new`。`singleDeclarationsArena.NewSlice1(x)` → `new[] { x }`。
// 3) `BindSourceFile`（导出）与 `bindSourceFile`（未导出）同名同参，C# 不允许这种重载 → 未导出者按 C# 习惯
//    命名 `BindSourceFileWorker`（照 `Parser.ParseSourceFileWorker`）。
// 4) `b.bindFunc` 的类型是 `Visitor`（`func(*ast.Node) bool` 在 Ast 侧就是 `delegate bool Visitor(Node node)`）；
//    构造器里用 lambda `node => Bind(node)` 初始化（Go 的 `b.bind` 方法值也是闭包；直接写 `_bindFunc = Bind`
//    会因为 `Bind(Node?)` 与 `Visitor(Node)` 的可空性不匹配触发 CS8622，lambda 的形参推断为 `Node` 即可）。
// 5) `ContainerFlags`（Go 的 `type ContainerFlags int32` + 常量）→ `[Flags] enum ContainerFlags : int`，
//    成员名去 `ContainerFlags` 前缀（docs/architecture.md §3.3），数值与 Go 一致。
// 6) `b.file.SetBindDiagnostics(append(b.file.BindDiagnostics(), d))` → `Array.Resize` + 写末位
//    （C# 数组没有 len<cap 视图，Go 的 append 语义只能等价重写；每次调用一次分配，与 Go 摊销扩容同阶）。
// 7) `panic("...")` → `Debug.Fail("...")` + `throw new System.Diagnostics.UnreachableException()`（Debug.Fail 没有
//    `[DoesNotReturn]`，其后必须补 throw 才能让控制流满足返回值要求）。
// 8) Go 的 `map[string]*Symbol` 读（`symbolTable[name]`）→ `GetValueOrDefault(name)`（缺键返回 null，与 Go 的
//    零值一致；`Dictionary` 的索引器会抛 KeyNotFoundException，不能用）。写仍是 `symbolTable[name] = symbol`。
// 9) 可空性：Go 的 nil 指针 → `T?`；`As*()` 之后用 `!`（前面的 kind 分支已证明类型，与 `Parser` 一致）。
//    Go 里 `b.container`/`b.file`/`b.currentFlow` 等在调用点有非 nil 不变量的，C# 侧用 `!` 表达同一不变量。
// 10) `isSignedNumericLiteral`（binder.go:2748）在 Go 里是**死代码**（该文件中无调用点；binder.go:337 调的是
//    `ast.IsSignedNumericLiteral`）。本文件照样保留（1:1 移植），但第 337 行的调用点写全名
//    `TypeScript.Ast.Ast.IsSignedNumericLiteral(...)`：本类的同名私有静态方法会赢得简单名查找，而裸写 `Ast.X`
//    又会先解析到命名空间 `TypeScript.Ast`（docs/architecture.md §2 的 Debug 坑，`Parser` 里同款写法）。
// 11) 几处 Go 参数被解引用多次的地方（`createFlowCondition`/`createFlowMutation`/`createFlowSwitchClause`/
//    `createFlowCall`/`addAntecedent`）改用局部变量（`antecedentValue`）：Go 的 `antecedent.Flags` 解引用在 C# 里
//    是"可能为 null 的引用"（参数声明为 `FlowNode?`，与 Go 允许传 nil 一致），取一次局部变量后流分析才稳定。
namespace TypeScript.Binder;

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

/// <summary>Go: <c>type ContainerFlags int32</c>（成员名去 Go 常量名的 <c>ContainerFlags</c> 前缀，数值一致）。</summary>
[Flags]
public enum ContainerFlags : int
{
    /// <summary>The current node is not a container, and no container manipulation should happen before recursing into it.</summary>
    None = 0,

    /// <summary>
    /// The current node is a container. It should be set as the current container (and block-container) before
    /// recursing into it. The current node does not have locals. Examples: Classes, ObjectLiterals, TypeLiterals, Interfaces...
    /// </summary>
    IsContainer = 1 << 0,

    /// <summary>
    /// The current node is a block-scoped-container. It should be set as the current block-container before recursing
    /// into it. Examples: Blocks (when not parented by functions), Catch clauses, For/For-in/For-of statements...
    /// </summary>
    IsBlockScopedContainer = 1 << 1,

    /// <summary>
    /// The current node is the container of a control flow path. The current control flow should be saved and restored,
    /// and a new control flow initialized within the container.
    /// </summary>
    IsControlFlowContainer = 1 << 2,

    IsFunctionLike = 1 << 3,
    IsFunctionExpression = 1 << 4,
    HasLocals = 1 << 5,
    IsInterface = 1 << 6,
    IsObjectLiteralOrClassExpressionMethodOrAccessor = 1 << 7,
    IsThisContainer = 1 << 8,
    PropagatesThisKeyword = 1 << 9,
}

/// <summary>Go: <c>type ExpandoAssignmentInfo struct</c>（未导出字段按 C# 习惯命名）。</summary>
public sealed class ExpandoAssignmentInfo
{
    internal Node? _node;
    internal Node? _container;
    internal Node? _blockScopeContainer;
}

/// <summary>Go: <c>type ActiveLabel struct</c>（未导出字段 + 两个导出方法）。</summary>
public sealed class ActiveLabel
{
    internal ActiveLabel? _next;
    internal FlowNode? _breakTarget;
    internal FlowNode? _continueTarget;
    internal string _name = "";
    internal bool _referenced;

    public FlowNode? BreakTarget() => _breakTarget;

    public FlowNode? ContinueTarget() => _continueTarget;
}

/// <summary>Go: <c>type Binder struct</c>（同时也是本 package 的包级函数宿主，见文件头）。</summary>
public sealed partial class Binder
{
    private SourceFile _file;
    private Visitor _bindFunc;
    private FlowNode? _unreachableFlow;

    private Node? _container;
    private Node? _thisContainer;
    private Node? _blockScopeContainer;
    private Node? _lastContainer;
    private FlowNode? _currentFlow;
    private FlowNode? _currentBreakTarget;
    private FlowNode? _currentContinueTarget;
    private FlowNode? _currentReturnTarget;
    private FlowNode? _currentTrueTarget;
    private FlowNode? _currentFalseTarget;
    private FlowNode? _currentExceptionTarget;
    private FlowNode? _preSwitchCaseFlow;
    private ActiveLabel? _activeLabelList;
    private NodeFlags _emitFlags;
    private bool _seenThisKeyword;
    private bool _hasExplicitReturn;
    private bool _hasFlowEffects;
    private bool _inAssignmentPattern;
    private bool _seenParseError;
    private int _symbolCount;
    private readonly Set<Symbol> _notConstEnumOnlyModules = new();
    private readonly List<ExpandoAssignmentInfo> _expandoAssignments = [];

    /// <summary>
    /// Go: <c>var binderPool = sync.Pool{New: func() any { b := &amp;Binder{}; b.bindFunc = b.bind; return b }}</c>。
    /// 池化方案与差异见文件头第 1 条。构造器同时完成 Go 里 "New" 回调的两件事（建立 Binder、分配一次闭包）。
    /// </summary>
    private Binder(SourceFile file)
    {
        _file = file;
        _bindFunc = node => Bind(node);
    }

    /// <summary>Go: <c>func BindSourceFile(file *ast.SourceFile)</c>。</summary>
    public static void BindSourceFile(SourceFile file)
    {
        // This is constructed this way to make the compiler "out-line" the function,
        // avoiding most work in the common case where the file has already been bound.
        if (!file.IsBound())
        {
            BindSourceFileWorker(file);
        }
    }

    /// <summary>Go: <c>var binderPool</c> 的 C# 载体（见文件头第 1 条；与 <c>Parser._pooledParser</c> 同款）。</summary>
    [ThreadStatic]
    private static Binder? _pooledBinder;

    /// <summary>Go: <c>func getBinder() *Binder</c>（C# 版本必须同时给出本次要绑定的 file，见构造器注释）。</summary>
    private static Binder GetBinder(SourceFile file)
    {
        var binder = _pooledBinder;
        if (binder is not null)
        {
            _pooledBinder = null;
            binder._file = file;
            return binder;
        }

        return new Binder(file);
    }

    /// <summary>Go: <c>func putBinder(b *Binder)</c>（归还前按 Go 的 <c>*b = Binder{bindFunc: b.bindFunc}</c> 复位）。</summary>
    private static void PutBinder(Binder b)
    {
        b.Reset();
        _pooledBinder = b;
    }

    /// <summary>
    /// Go: putBinder 里的 <c>*b = Binder{bindFunc: b.bindFunc}</c>。
    /// 与 Go 的两点差异（都由 C# 的非空字段断言导致，语义不变）：
    /// - `_file` 在 Go 里被置 nil（下次 <c>bindSourceFile</c> 再赋值），C# 的 `_file` 是非空字段，
    ///   所以保留旧值、由 `GetBinder` 在取用时覆盖；
    /// - `_bindFunc` 在 Go 里被显式保留，这里同样保留（构造器已赋值）。
    /// </summary>
    private void Reset()
    {
        _unreachableFlow = null;
        _container = null;
        _thisContainer = null;
        _blockScopeContainer = null;
        _lastContainer = null;
        _currentFlow = null;
        _currentBreakTarget = null;
        _currentContinueTarget = null;
        _currentReturnTarget = null;
        _currentTrueTarget = null;
        _currentFalseTarget = null;
        _currentExceptionTarget = null;
        _preSwitchCaseFlow = null;
        _activeLabelList = null;
        _emitFlags = NodeFlags.None;
        _seenThisKeyword = false;
        _hasExplicitReturn = false;
        _hasFlowEffects = false;
        _inAssignmentPattern = false;
        _seenParseError = false;
        _symbolCount = 0;
        _notConstEnumOnlyModules.Clear();
        _expandoAssignments.Clear();
    }

    /// <summary>Go: <c>func bindSourceFile(file *ast.SourceFile)</c>（命名偏离见文件头第 3 条）。</summary>
    private static void BindSourceFileWorker(SourceFile file)
    {
        file.BindOnce(() =>
        {
            var b = GetBinder(file);
            try
            {
                b._unreachableFlow = b.NewFlowNode(FlowFlags.Unreachable);
                b.Bind(file.AsNode());
                b.BindDeferredExpandoAssignments();
                file.SymbolCount = b._symbolCount;
            }
            finally
            {
                PutBinder(b);
            }
        });
    }

    private Symbol NewSymbol(SymbolFlags flags, string name)
    {
        _symbolCount++;
        var result = new Symbol();
        result.Flags = flags;
        result.Name = name;
        return result;
    }

    /**
     * Declares a Symbol for the node and adds it to symbols. Reports errors for conflicting identifier names.
     *
     * @param symbolTable - The symbol table which node will be added to.
     * @param parent - node's parent declaration.
     * @param node - The declaration to be added to the symbol table
     * @param includes - The SymbolFlags that node has in addition to its declaration type (eg: export, ambient, etc.)
     * @param excludes - The flags which node cannot be declared alongside in a symbol table. Used to report forbidden declarations.
     */
    private Symbol DeclareSymbol(SymbolTable symbolTable, Symbol? parent, Node node, SymbolFlags includes, SymbolFlags excludes)
    {
        return DeclareSymbolEx(symbolTable, parent, node, includes, excludes, false /*isReplaceableByMethod*/, false /*isComputedName*/);
    }

    private Symbol DeclareSymbolEx(SymbolTable symbolTable, Symbol? parent, Node node, SymbolFlags includes, SymbolFlags excludes, bool isReplaceableByMethod, bool isComputedName)
    {
        Debug.Assert(isComputedName || !HasDynamicName(node));
        var isDefaultExport = HasSyntacticModifier(node, ModifierFlags.Default) || (IsExportSpecifier(node) && ModuleExportNameIsDefault(node.AsExportSpecifier()!.Name()));
        // The exported symbol for an export default function/class node is always named "default"
        string name;
        if (isComputedName)
        {
            name = InternalSymbolNameComputed;
        }
        else if (isDefaultExport && parent is not null)
        {
            name = InternalSymbolNameDefault;
        }
        else
        {
            name = GetDeclarationName(node);
        }

        Symbol? symbol;
        if (name == InternalSymbolNameMissing)
        {
            symbol = NewSymbol(SymbolFlags.None, InternalSymbolNameMissing);
        }
        else
        {
            // Check and see if the symbol table already has a symbol with this name.  If not,
            // create a new symbol with this name and add it to the table.  Note that we don't
            // give the new symbol any flags *yet*.  This ensures that it will not conflict
            // with the 'excludes' flags we pass in.
            //
            // If we do get an existing symbol, see if it conflicts with the new symbol we're
            // creating.  For example, a 'var' symbol and a 'class' symbol will conflict within
            // the same symbol table.  If we have a conflict, report the issue on each
            // declaration we have for this symbol, and then create a new symbol for this
            // declaration.
            //
            // Note that when properties declared in Javascript constructors
            // (marked by isReplaceableByMethod) conflict with another symbol, the property loses.
            // Always. This allows the common Javascript pattern of overwriting a prototype method
            // with an bound instance method of the same type: `this.method = this.method.bind(this)`
            //
            // If we created a new symbol, either because we didn't have a symbol with this name
            // in the symbol table, or we conflicted with an existing symbol, then just add this
            // node as the sole declaration of the new symbol.
            //
            // Otherwise, we'll be merging into a compatible existing symbol (for example when
            // you have multiple 'vars' with the same name in the same container).  In this case
            // just add this node into the declarations list of the symbol.
            symbol = symbolTable.GetValueOrDefault(name);
            if (symbol is null)
            {
                symbol = NewSymbol(SymbolFlags.None, name);
                symbolTable[name] = symbol;
                if (isReplaceableByMethod)
                {
                    symbol.Flags |= SymbolFlags.ReplaceableByMethod;
                }
            }
            else if (isReplaceableByMethod && (symbol.Flags & SymbolFlags.ReplaceableByMethod) == 0)
            {
                // A symbol already exists, so don't add this as a declaration.
                return symbol;
            }
            else if ((symbol.Flags & excludes) != 0)
            {
                if ((symbol.Flags & SymbolFlags.ReplaceableByMethod) != 0)
                {
                    // Javascript constructor-declared symbols can be discarded in favor of
                    // prototype symbols like methods.
                    symbol = NewSymbol(SymbolFlags.None, name);
                    symbolTable[name] = symbol;
                }
                else if (!((includes & SymbolFlags.Variable) != 0 && (symbol.Flags & SymbolFlags.Assignment) != 0 ||
                    (includes & SymbolFlags.Assignment) != 0 && (symbol.Flags & SymbolFlags.Variable) != 0))
                {
                    // Assignment declarations are allowed to merge with variables, no matter what other flags they have.
                    // Report errors every position with duplicate declaration
                    // Report errors on previous encountered declarations
                    Message message;
                    if ((symbol.Flags & SymbolFlags.BlockScopedVariable) != 0)
                    {
                        message = Diagnostics.Cannot_redeclare_block_scoped_variable_0;
                    }
                    else
                    {
                        message = Diagnostics.Duplicate_identifier_0;
                    }

                    var messageNeedsName = true;
                    if ((symbol.Flags & SymbolFlags.Enum) != 0 || (includes & SymbolFlags.Enum) != 0)
                    {
                        message = Diagnostics.Enum_declarations_can_only_merge_with_namespace_or_other_enum_declarations;
                        messageNeedsName = false;
                    }

                    var multipleDefaultExports = false;
                    // Go: `len(symbol.Declarations) != 0` / `range symbol.Declarations` —— nil 切片 ≡ 空切片（§3.2），
                    // 取到局部变量后 C# 的流分析才稳定（跨调用访问 `symbol.Declarations` 的可空状态会被重置）。
                    var declarations = symbol.Declarations ?? [];
                    if (declarations.Length != 0)
                    {
                        // If the current node is a default export of some sort, then check if
                        // there are any other default exports that we need to error on.
                        // We'll know whether we have other default exports depending on if `symbol` already has a declaration list set.
                        if (isDefaultExport)
                        {
                            message = Diagnostics.A_module_cannot_have_multiple_default_exports;
                            messageNeedsName = false;
                            multipleDefaultExports = true;
                        }
                        else
                        {
                            // This is to properly report an error in the case "export default { }" is after export default of class declaration or function declaration.
                            // Error on multiple export default in the following case:
                            // 1. multiple export default of class declaration or function declaration by checking NodeFlags.Default
                            // 2. multiple export default of export assignment. This one doesn't have NodeFlags.Default on (as export default doesn't considered as modifiers)
                            if (declarations.Length != 0 && IsExportAssignment(node) && !node.AsExportAssignment()!.IsExportEquals)
                            {
                                message = Diagnostics.A_module_cannot_have_multiple_default_exports;
                                messageNeedsName = false;
                                multipleDefaultExports = true;
                            }
                        }
                    }

                    var declarationName = GetNameOfDeclaration(node);
                    if (declarationName is null)
                    {
                        declarationName = node;
                    }

                    Diagnostic diag;
                    if (messageNeedsName)
                    {
                        diag = CreateDiagnosticForNode(declarationName, message, GetDisplayName(node));
                    }
                    else
                    {
                        diag = CreateDiagnosticForNode(declarationName, message);
                    }

                    if (IsTypeAliasDeclaration(node) && NodeIsMissing(node.Type()) && HasSyntacticModifier(node, ModifierFlags.Export) && (symbol.Flags & (SymbolFlags.Alias | SymbolFlags.Type | SymbolFlags.Namespace)) != 0)
                    {
                        // export type T; - may have meant export type { T }?
                        diag.AddRelatedInfo(CreateDiagnosticForNode(node, Diagnostics.Did_you_mean_0, "export type { " + node.AsTypeAliasDeclaration()!.Name()!.Text() + " }"));
                    }

                    for (var index = 0; index < declarations.Length; index++)
                    {
                        var declaration = declarations[index];
                        var decl = GetNameOfDeclaration(declaration);
                        if (decl is null)
                        {
                            decl = declaration;
                        }

                        Diagnostic d;
                        if (messageNeedsName)
                        {
                            d = CreateDiagnosticForNode(decl, message, GetDisplayName(declaration));
                        }
                        else
                        {
                            d = CreateDiagnosticForNode(decl, message);
                        }

                        if (multipleDefaultExports)
                        {
                            d.AddRelatedInfo(CreateDiagnosticForNode(declarationName, IfElse(index == 0, Diagnostics.Another_export_default_is_here, Diagnostics.X_and_here)));
                        }

                        AddDiagnostic(d);
                        if (multipleDefaultExports)
                        {
                            diag.AddRelatedInfo(CreateDiagnosticForNode(decl, Diagnostics.The_first_export_default_is_here));
                        }
                    }

                    AddDiagnostic(diag);
                    // When get or set accessor conflicts with a non-accessor or an accessor of a different kind, we mark
                    // the symbol as a full accessor such that all subsequent declarations are considered conflicting. This
                    // for example ensures that a get accessor followed by a non-accessor followed by a set accessor with the
                    // same name are all marked as duplicates.
                    if ((symbol.Flags & SymbolFlags.Accessor) != 0 && (symbol.Flags & SymbolFlags.Accessor) != (includes & SymbolFlags.Accessor))
                    {
                        symbol.Flags |= SymbolFlags.Accessor;
                    }

                    symbol = NewSymbol(SymbolFlags.None, name);
                }
            }
        }

        AddDeclarationToSymbol(symbol, node, includes);
        if (symbol.Parent is null)
        {
            symbol.Parent = parent;
        }
        else if (symbol.Parent != parent)
        {
            Debug.Fail("Existing symbol parent should match new one");
            throw new System.Diagnostics.UnreachableException();
        }

        return symbol;
    }

    // Should not be called on a declaration with a computed property name,
    // unless it is a well known Symbol.
    private string GetDeclarationName(Node node)
    {
        if (IsExportAssignment(node))
        {
            return node.AsExportAssignment()!.IsExportEquals ? InternalSymbolNameExportEquals : InternalSymbolNameDefault;
        }

        var name = GetNameOfDeclaration(node);
        if (name is not null)
        {
            if (IsAmbientModule(node))
            {
                var moduleName = name.Text();
                if (IsGlobalScopeAugmentation(node))
                {
                    return InternalSymbolNameGlobal;
                }

                var pattern = TryParsePattern(moduleName);
                if (pattern.IsValid() && pattern.StarIndex >= 0)
                {
                    var attributes = node.AsModuleDeclaration()!.Attributes;
                    if (attributes is not null)
                    {
                        return InternalSymbolNamePrefix + "\"" + moduleName + "\"pattern@" + GetNodeId(attributes);
                    }
                }

                return "\"" + moduleName + "\"";
            }

            if (IsPrivateIdentifier(name))
            {
                // containingClass exists because private names only allowed inside classes
                var containingClass = GetContainingClass(node);
                if (containingClass is null)
                {
                    // we can get here in cases where there is already a parse error.
                    return InternalSymbolNameMissing;
                }

                return GetSymbolNameForPrivateIdentifier(containingClass.Symbol!, name.Text());
            }

            if (IsPropertyNameLiteral(name) || IsJsxNamespacedName(name))
            {
                return name.Text();
            }

            if (IsComputedPropertyName(name))
            {
                var nameExpression = name.Expression()!;
                // treat computed property names where expression is string/numeric literal as just string/numeric literal
                if (IsStringOrNumericLiteralLike(nameExpression))
                {
                    return nameExpression.Text();
                }

                if (TypeScript.Ast.Ast.IsSignedNumericLiteral(nameExpression))
                {
                    var unaryExpression = nameExpression.AsPrefixUnaryExpression()!;
                    return TokenToString(unaryExpression.Operator) + unaryExpression.Operand!.Text();
                }

                Debug.Fail("Only computed properties with literal names have declaration names");
                throw new System.Diagnostics.UnreachableException();
            }

            return InternalSymbolNameMissing;
        }

        switch (node.Kind)
        {
            case Kind.Constructor:
                return InternalSymbolNameConstructor;
            case Kind.FunctionType:
            case Kind.CallSignature:
                return InternalSymbolNameCall;
            case Kind.ConstructorType:
            case Kind.ConstructSignature:
                return InternalSymbolNameNew;
            case Kind.IndexSignature:
                return InternalSymbolNameIndex;
            case Kind.ExportDeclaration:
                return InternalSymbolNameExportStar;
            case Kind.SourceFile:
            case Kind.BinaryExpression:
                return InternalSymbolNameExportEquals;
        }

        return InternalSymbolNameMissing;
    }

    private string GetDisplayName(Node node)
    {
        var nameNode = node.Name();
        if (nameNode is not null)
        {
            return DeclarationNameToString(nameNode);
        }

        var name = GetDeclarationName(node);
        if (name != InternalSymbolNameMissing)
        {
            return name;
        }

        return "(Missing)";
    }

    /// <summary>Go: <c>func GetSymbolNameForPrivateIdentifier(containingClassSymbol *ast.Symbol, description string) string</c>。</summary>
    public static string GetSymbolNameForPrivateIdentifier(Symbol containingClassSymbol, string description)
    {
        return InternalSymbolNamePrefix + "#" + GetSymbolId(containingClassSymbol) + "@" + description;
    }

    private Symbol DeclareModuleMember(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        var container = _container!;
        var hasExportModifier = (GetCombinedModifierFlags(node) & ModifierFlags.Export) != 0 || IsImplicitlyExportedJSDocDeclaration(node);
        if ((symbolFlags & SymbolFlags.Alias) != 0)
        {
            if (node.Kind == Kind.ExportSpecifier || (node.Kind == Kind.ImportEqualsDeclaration && hasExportModifier))
            {
                return DeclareSymbol(GetExports(container.Symbol), container.Symbol, node, symbolFlags, symbolExcludes);
            }

            return DeclareSymbol(GetLocals(container), null /*parent*/, node, symbolFlags, symbolExcludes);
        }

        // Exported module members are given 2 symbols: A local symbol that is classified with an ExportValue flag,
        // and an associated export symbol with all the correct flags set on it. There are 2 main reasons:
        //
        //   1. We treat locals and exports of the same name as mutually exclusive within a container.
        //      That means the binder will issue a Duplicate Identifier error if you mix locals and exports
        //      with the same name in the same container.
        //      TODO: Make this a more specific error and decouple it from the exclusion logic.
        //   2. When we checkIdentifier in the checker, we set its resolved symbol to the local symbol,
        //      but return the export symbol (by calling getExportSymbolOfValueSymbolIfExported). That way
        //      when the emitter comes back to it, it knows not to qualify the name if it was found in a containing scope.
        //
        // NOTE: Nested ambient modules always should go to to 'locals' table to prevent their automatic merge
        //       during global merging in the checker. Why? The only case when ambient module is permitted inside another module is module augmentation
        //       and this case is specially handled. Module augmentations should only be merged with original module definition
        //       and should never be merged directly with other augmentation, and the latter case would be possible if automatic merge is allowed.
        if (!IsAmbientModule(node) && (hasExportModifier || (_container!.Flags & NodeFlags.ExportContext) != 0))
        {
            if (!IsLocalsContainer(container) || (HasSyntacticModifier(node, ModifierFlags.Default) && GetDeclarationName(node) == InternalSymbolNameMissing))
            {
                return DeclareSymbol(GetExports(container.Symbol), container.Symbol, node, symbolFlags, symbolExcludes);
                // No local symbol for an unnamed default!
            }

            var exportKind = SymbolFlags.None;
            if ((symbolFlags & SymbolFlags.Value) != 0)
            {
                exportKind = SymbolFlags.ExportValue;
            }

            var local = DeclareSymbol(GetLocals(container), null /*parent*/, node, exportKind, symbolExcludes);
            local.ExportSymbol = DeclareSymbol(GetExports(container.Symbol), container.Symbol, node, symbolFlags, symbolExcludes);
            node.ExportableData()!.LocalSymbol = local;
            return local;
        }

        return DeclareSymbol(GetLocals(container), null /*parent*/, node, symbolFlags, symbolExcludes);
    }

    private Symbol DeclareClassMember(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        if (IsStatic(node))
        {
            return DeclareSymbol(GetExports(_container!.Symbol), _container.Symbol, node, symbolFlags, symbolExcludes);
        }

        return DeclareSymbol(GetMembers(_container!.Symbol), _container.Symbol, node, symbolFlags, symbolExcludes);
    }

    private Symbol DeclareSourceFileMember(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        if (IsExternalModule(_file))
        {
            return DeclareModuleMember(node, symbolFlags, symbolExcludes);
        }

        return DeclareSymbol(GetLocals(_file.AsNode()), null /*parent*/, node, symbolFlags, symbolExcludes);
    }

    private Symbol DeclareSymbolAndAddToSymbolTable(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        switch (_container!.Kind)
        {
            case Kind.ModuleDeclaration:
                return DeclareModuleMember(node, symbolFlags, symbolExcludes);
            case Kind.SourceFile:
                return DeclareSourceFileMember(node, symbolFlags, symbolExcludes);
            case Kind.ClassExpression:
            case Kind.ClassDeclaration:
                return DeclareClassMember(node, symbolFlags, symbolExcludes);
            case Kind.EnumDeclaration:
                return DeclareSymbol(GetExports(_container.Symbol), _container.Symbol, node, symbolFlags, symbolExcludes);
            case Kind.TypeLiteral:
            case Kind.ObjectLiteralExpression:
            case Kind.InterfaceDeclaration:
            case Kind.JsxAttributes:
                return DeclareSymbol(GetMembers(_container.Symbol), _container.Symbol, node, symbolFlags, symbolExcludes);
            case Kind.FunctionType:
            case Kind.ConstructorType:
            case Kind.CallSignature:
            case Kind.ConstructSignature:
            case Kind.IndexSignature:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.ClassStaticBlockDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.MappedType:
                return DeclareSymbol(GetLocals(_container), null /*parent*/, node, symbolFlags, symbolExcludes);
        }

        Debug.Fail("Unhandled case in declareSymbolAndAddToSymbolTable");
        throw new System.Diagnostics.UnreachableException();
    }

    private FlowNode NewFlowNode(FlowFlags flags)
    {
        var result = new FlowNode();
        result.Flags = flags;
        return result;
    }

    private FlowNode NewFlowNodeEx(FlowFlags flags, Node? node, FlowNode? antecedent)
    {
        var result = NewFlowNode(flags);
        result.Node = node;
        result.Antecedent = antecedent;
        return result;
    }

    private FlowNode CreateLoopLabel()
    {
        return NewFlowNode(FlowFlags.LoopLabel);
    }

    private FlowNode CreateBranchLabel()
    {
        return NewFlowNode(FlowFlags.BranchLabel);
    }

    private FlowNode CreateReduceLabel(FlowNode target, FlowList? antecedents, FlowNode? antecedent)
    {
        return NewFlowNodeEx(FlowFlags.ReduceLabel, NewFlowReduceLabelData(target, antecedents), antecedent);
    }

    private FlowNode CreateFlowCondition(FlowFlags flags, FlowNode? antecedent, Node? expression)
    {
        var antecedentValue = antecedent!;
        if ((antecedentValue.Flags & FlowFlags.Unreachable) != 0)
        {
            return antecedentValue;
        }

        if (expression is null)
        {
            if ((flags & FlowFlags.TrueCondition) != 0)
            {
                return antecedentValue;
            }

            return _unreachableFlow!;
        }

        if ((expression.Kind == Kind.TrueKeyword && (flags & FlowFlags.FalseCondition) != 0 || expression.Kind == Kind.FalseKeyword && (flags & FlowFlags.TrueCondition) != 0) && !IsExpressionOfOptionalChainRoot(expression) && !IsNullishCoalesce(expression.Parent))
        {
            return _unreachableFlow!;
        }

        if (!IsNarrowingExpression(expression))
        {
            return antecedentValue;
        }

        SetFlowNodeReferenced(antecedentValue);
        return NewFlowNodeEx(flags, expression, antecedentValue);
    }

    private FlowNode CreateFlowMutation(FlowFlags flags, FlowNode? antecedent, Node node)
    {
        var antecedentValue = antecedent!;
        SetFlowNodeReferenced(antecedentValue);
        _hasFlowEffects = true;
        var result = NewFlowNodeEx(flags, node, antecedentValue);
        if (_currentExceptionTarget is not null)
        {
            AddAntecedent(_currentExceptionTarget, result);
        }

        return result;
    }

    private FlowNode CreateFlowSwitchClause(FlowNode? antecedent, Node switchStatement, int clauseStart, int clauseEnd)
    {
        var antecedentValue = antecedent!;
        SetFlowNodeReferenced(antecedentValue);
        return NewFlowNodeEx(FlowFlags.SwitchClause, NewFlowSwitchClauseData(switchStatement, clauseStart, clauseEnd), antecedentValue);
    }

    private FlowNode CreateFlowCall(FlowNode? antecedent, Node node)
    {
        var antecedentValue = antecedent!;
        SetFlowNodeReferenced(antecedentValue);
        _hasFlowEffects = true;
        return NewFlowNodeEx(FlowFlags.Call, node, antecedentValue);
    }

    private FlowList NewFlowList(FlowNode? head, FlowList? tail)
    {
        var result = new FlowList();
        result.Flow = head;
        result.Next = tail;
        return result;
    }

    private FlowList? CombineFlowLists(FlowList? head, FlowList? tail)
    {
        if (head is null)
        {
            return tail;
        }

        return NewFlowList(head.Flow, CombineFlowLists(head.Next, tail));
    }

    private Node[] NewSingleDeclaration(Node declaration)
    {
        return [declaration];
    }

    private static void SetFlowNodeReferenced(FlowNode flow)
    {
        // On first reference we set the Referenced flag, thereafter we set the Shared flag
        if ((flow.Flags & FlowFlags.Referenced) == 0)
        {
            flow.Flags |= FlowFlags.Referenced;
        }
        else
        {
            flow.Flags |= FlowFlags.Shared;
        }
    }

    private void AddAntecedent(FlowNode label, FlowNode? antecedent)
    {
        var antecedentValue = antecedent!;
        if ((antecedentValue.Flags & FlowFlags.Unreachable) != 0)
        {
            return;
        }

        // If antecedent isn't already on the Antecedents list, add it to the end of the list
        FlowList? last = null;
        for (var list = label.Antecedents; list is not null; list = list.Next)
        {
            if (list.Flow == antecedentValue)
            {
                return;
            }

            last = list;
        }

        if (last is null)
        {
            label.Antecedents = NewFlowList(antecedentValue, null);
        }
        else
        {
            last.Next = NewFlowList(antecedentValue, null);
        }

        SetFlowNodeReferenced(antecedentValue);
    }

    private FlowNode FinishFlowLabel(FlowNode label)
    {
        if (label.Antecedents is null)
        {
            return _unreachableFlow!;
        }

        if (label.Antecedents.Next is null)
        {
            // Go: `return label.Antecedents.Flow`；FlowNode 的 Antecedent 字段是可空的，但凡是进了 Antecedents
            // 链表的节点都非空（addAntecedent 只放非空值），这里照 Go 的非空不变量断言。
            return label.Antecedents.Flow!;
        }

        return label;
    }

    /// <summary>
    /// Go: <c>func (b *Binder) bind(node *ast.Node) bool</c>。参数可空：Go 的实现显式容忍 nil
    /// （<c>if node == nil { return false }</c>），调用点会传入合法的 nil 字段（如 BinaryExpression.Type）。
    /// </summary>
    private bool Bind(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        // Even though in the AST the jsdoc @typedef node belongs to the current node,
        // its symbol might be in the same scope with the current node's symbol. Consider:
        //
        //     /** @typedef {string | number} MyType */
        //     function foo();
        //
        // Here the current node is "foo", which is a container, but the scope of "MyType" should
        // not be inside "foo". Therefore we always bind @typedef before bind the parent node,
        // and skip binding this tag later when binding all the other jsdoc tags.

        // First we bind declaration nodes to a symbol if possible. We'll both create a symbol
        // and then potentially add the symbol to an appropriate symbol table. Possible
        // destination symbol tables are:
        //
        //  1) The 'exports' table of the current container's symbol.
        //  2) The 'members' table of the current container's symbol.
        //  3) The 'locals' table of the current container.
        //
        // However, not all symbols will end up in any of these tables. 'Anonymous' symbols
        // (like TypeLiterals for example) will not be put in any table.
        switch (node.Kind)
        {
            case Kind.Identifier:
                node.AsIdentifier()!.FlowNode = _currentFlow;
                CheckContextualIdentifier(node);
                break;
            case Kind.ThisKeyword:
            case Kind.SuperKeyword:
                if (node.Kind == Kind.ThisKeyword)
                {
                    _seenThisKeyword = true;
                }

                node.AsKeywordExpression()!.FlowNode = _currentFlow;
                break;
            case Kind.QualifiedName:
                if (_currentFlow is not null && IsPartOfTypeQuery(node))
                {
                    node.AsQualifiedName()!.FlowNode = _currentFlow;
                }

                break;
            case Kind.MetaProperty:
                node.AsMetaProperty()!.FlowNode = _currentFlow;
                break;
            case Kind.PrivateIdentifier:
                CheckPrivateIdentifier(node);
                break;
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                if (_currentFlow is not null && IsNarrowableReference(node))
                {
                    SetFlowNode(node, _currentFlow);
                }

                break;
            case Kind.BinaryExpression:
                switch (GetAssignmentDeclarationKind(node))
                {
                    case JSDeclarationKind.ModuleExports:
                        BindModuleExportsAssignment(node);
                        break;
                    case JSDeclarationKind.ExportsProperty:
                        BindExportsOrObjectDefineProperty(node);
                        break;
                    case JSDeclarationKind.Property:
                        BindExpandoPropertyAssignment(node);
                        break;
                    case JSDeclarationKind.ThisProperty:
                        BindThisPropertyAssignment(node);
                        break;
                }

                CheckStrictModeBinaryExpression(node);
                break;
            case Kind.CatchClause:
                CheckStrictModeCatchClause(node);
                break;
            case Kind.DeleteExpression:
                CheckStrictModeDeleteExpression(node);
                break;
            case Kind.PostfixUnaryExpression:
                CheckStrictModePostfixUnaryExpression(node);
                break;
            case Kind.PrefixUnaryExpression:
                CheckStrictModePrefixUnaryExpression(node);
                break;
            case Kind.WithStatement:
                CheckStrictModeWithStatement(node);
                break;
            case Kind.LabeledStatement:
                CheckStrictModeLabeledStatement(node);
                break;
            case Kind.ThisType:
                _seenThisKeyword = true;
                break;
            case Kind.TypeParameter:
                BindTypeParameter(node);
                break;
            case Kind.Parameter:
                BindParameter(node);
                break;
            case Kind.VariableDeclaration:
                BindVariableDeclarationOrBindingElement(node);
                break;
            case Kind.BindingElement:
                node.AsBindingElement()!.FlowNode = _currentFlow;
                BindVariableDeclarationOrBindingElement(node);
                break;
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
                BindPropertyWorker(node);
                break;
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.Property, SymbolFlags.PropertyExcludes);
                break;
            case Kind.EnumMember:
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.EnumMember, SymbolFlags.EnumMemberExcludes);
                break;
            case Kind.CallSignature:
            case Kind.ConstructSignature:
            case Kind.IndexSignature:
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Signature, SymbolFlags.None);
                break;
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.Method | GetOptionalSymbolFlagForNode(node), IfElse(IsObjectLiteralMethod(node), SymbolFlags.Value, SymbolFlags.MethodExcludes));
                break;
            case Kind.FunctionDeclaration:
                BindFunctionDeclaration(node);
                break;
            case Kind.Constructor:
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Constructor, SymbolFlags.None);
                break;
            case Kind.GetAccessor:
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.GetAccessor, SymbolFlags.GetAccessorExcludes);
                break;
            case Kind.SetAccessor:
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.SetAccessor, SymbolFlags.SetAccessorExcludes);
                break;
            case Kind.FunctionType:
            case Kind.ConstructorType:
                BindFunctionOrConstructorType(node);
                break;
            case Kind.TypeLiteral:
            case Kind.MappedType:
                BindAnonymousDeclaration(node, SymbolFlags.TypeLiteral, InternalSymbolNameType);
                break;
            case Kind.ObjectLiteralExpression:
                BindAnonymousDeclaration(node, SymbolFlags.ObjectLiteral, InternalSymbolNameObject);
                break;
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
                BindFunctionExpression(node);
                break;
            case Kind.ClassExpression:
            case Kind.ClassDeclaration:
                BindClassLikeDeclaration(node);
                break;
            case Kind.InterfaceDeclaration:
                BindBlockScopedDeclaration(node, SymbolFlags.Interface, SymbolFlags.InterfaceExcludes);
                break;
            case Kind.CallExpression:
                switch (GetAssignmentDeclarationKind(node))
                {
                    case JSDeclarationKind.ObjectDefinePropertyValue:
                        BindExpandoPropertyAssignment(node);
                        break;
                    case JSDeclarationKind.ObjectDefinePropertyExports:
                        BindExportsOrObjectDefineProperty(node);
                        break;
                }

                if (IsInJSFile(node))
                {
                    BindCallExpression(node);
                }

                break;
            case Kind.TypeAliasDeclaration:
                BindBlockScopedDeclaration(node, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
                break;
            case Kind.JSTypeAliasDeclaration:
                // Top-level JSTypeAliasDeclaration nodes are processed in bindContainer
                if (!IsSourceFile(_blockScopeContainer!))
                {
                    BindBlockScopedDeclaration(node, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
                }

                break;
            case Kind.EnumDeclaration:
                BindEnumDeclaration(node);
                break;
            case Kind.ModuleDeclaration:
                BindModuleDeclaration(node);
                break;
            case Kind.ImportEqualsDeclaration:
            case Kind.NamespaceImport:
            case Kind.ImportSpecifier:
            case Kind.ExportSpecifier:
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
                break;
            case Kind.NamespaceExportDeclaration:
                BindNamespaceExportDeclaration(node);
                break;
            case Kind.ImportClause:
                BindImportClause(node);
                break;
            case Kind.ExportDeclaration:
                BindExportDeclaration(node);
                break;
            case Kind.ExportAssignment:
                BindExportAssignment(node);
                break;
            case Kind.SourceFile:
                BindSourceFileIfExternalModule();
                break;
            case Kind.JsxAttributes:
                BindJsxAttributes(node);
                break;
            case Kind.JsxAttribute:
                BindJsxAttribute(node, SymbolFlags.Property, SymbolFlags.PropertyExcludes);
                break;
        }

        // Then we recurse into the children of the node to bind them as well. For certain
        // symbols we do specialized work when we recurse. For example, we'll keep track of
        // the current 'container' node when it changes. This helps us know which symbol table
        // a local should go into for example. Since terminal nodes are known not to have
        // children, as an optimization we don't process those.
        var thisNodeOrAnySubnodesHasError = (node.Flags & NodeFlags.ThisNodeHasError) != 0;
        if (node.Kind > Kind.LastToken)
        {
            var saveSeenParseError = _seenParseError;
            _seenParseError = false;
            var containerFlags = GetContainerFlags(node);
            if (containerFlags == ContainerFlags.None)
            {
                BindChildren(node);
            }
            else
            {
                BindContainer(node, containerFlags);
            }

            if (_seenParseError)
            {
                thisNodeOrAnySubnodesHasError = true;
            }

            _seenParseError = saveSeenParseError;
        }

        if (thisNodeOrAnySubnodesHasError)
        {
            node.Flags |= NodeFlags.ThisNodeOrAnySubNodesHasError;
            _seenParseError = true;
        }

        return false;
    }

    private void ErrorOnNode(Node node, Message message, params object?[] args)
    {
        AddDiagnostic(CreateDiagnosticForNode(node, message, args));
    }

    private void ErrorOnFirstToken(Node node, Message message, params object?[] args)
    {
        var span = GetRangeOfTokenAtPosition(_file, node.Pos());
        AddDiagnostic(NewDiagnostic(_file, span, message, args));
    }

    // Inside the binder, we may create a diagnostic for an as-yet unbound node (with potentially no parent pointers, implying no accessible source file)
    // If so, the node _must_ be in the current file (as that's the only way anything could have traversed to it to yield it as the error node)
    // This version of `createDiagnosticForNode` uses the binder's context to account for this, and always yields correct diagnostics even in these situations.
    private Diagnostic CreateDiagnosticForNode(Node node, Message message, params object?[] args)
    {
        return NewDiagnostic(_file, GetErrorRangeForNode(_file, node), message, args);
    }

    private void AddDiagnostic(Diagnostic diagnostic)
    {
        // Go: b.file.SetBindDiagnostics(append(b.file.BindDiagnostics(), diagnostic))
        var diagnostics = _file.BindDiagnostics();
        Array.Resize(ref diagnostics, diagnostics.Length + 1);
        diagnostics[^1] = diagnostic;
        _file.SetBindDiagnostics(diagnostics);
    }

    private static bool IsSignedNumericLiteral(Node node)
    {
        if (node.Kind == Kind.PrefixUnaryExpression)
        {
            var prefixUnaryExpression = node.AsPrefixUnaryExpression()!;
            return (prefixUnaryExpression.Operator == Kind.PlusToken || prefixUnaryExpression.Operator == Kind.MinusToken) && IsNumericLiteral(prefixUnaryExpression.Operand!);
        }

        return false;
    }

    private static SymbolFlags GetOptionalSymbolFlagForNode(Node node)
    {
        var postfixToken = node.PostfixToken();
        return postfixToken is not null && postfixToken.Kind == Kind.QuestionToken ? SymbolFlags.Optional : SymbolFlags.None;
    }

    private static bool IsFunctionSymbol(Symbol symbol)
    {
        var d = symbol.ValueDeclaration;
        if (d is not null)
        {
            if (IsFunctionDeclaration(d))
            {
                return true;
            }

            if (IsVariableDeclaration(d))
            {
                var varDecl = d.AsVariableDeclaration()!;
                if (varDecl.Initializer is not null)
                {
                    return IsFunctionLike(varDecl.Initializer);
                }
            }
        }

        return false;
    }

    private static bool IsStatementCondition(Node node)
    {
        switch (node.Parent!.Kind)
        {
            case Kind.IfStatement:
            case Kind.WhileStatement:
            case Kind.DoStatement:
                return node.Parent.Expression() == node;
            case Kind.ForStatement:
                return node.Parent.AsForStatement()!.Condition == node;
            case Kind.ConditionalExpression:
                return node.Parent.AsConditionalExpression()!.Condition == node;
        }

        return false;
    }

    private static bool IsTopLevelLogicalExpression(Node node)
    {
        while (IsParenthesizedExpression(node.Parent!) || (IsPrefixUnaryExpression(node.Parent!) && node.Parent!.AsPrefixUnaryExpression()!.Operator == Kind.ExclamationToken))
        {
            node = node.Parent!;
        }

        return !IsStatementCondition(node) && !IsLogicalExpression(node.Parent) && !(IsOptionalChain(node.Parent) && node.Parent!.Expression() == node);
    }

    private static bool IsAssignmentDeclaration(Node decl)
    {
        return IsBinaryExpression(decl) || IsAccessExpression(decl) || IsIdentifier(decl) || IsCallExpression(decl);
    }

    private static bool IsEffectiveModuleDeclaration(Node node)
    {
        return IsModuleDeclaration(node) || IsIdentifier(node);
    }
}

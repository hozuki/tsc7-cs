// ported from: internal/binder/nameresolver.go:1-506（NameResolver 类型 + Resolve 及其未导出辅助方法）。
//   同文件的 5 个包级函数（GetLocalSymbolForExportDefault/isExportDefaultSymbol/getIsDeferredContext/
//   isTypeParameterSymbolDeclaredInContainer/isSelfReferenceLocation）按契约 §1 落在 `partial class Binder`（静态成员），
//   本切片把它们放在 ReferenceResolver.cs 末尾的 Binder 分片里，所以本文件只有 NameResolver 一个类型；
//   调用点按 `Binder.Xxx(...)` 写（见下 7）。
//
// 与 Go 的差异（语言特性导致、且必须等价）：
//  1) Go 的 `loop:` 标签 + `break loop` → C# 的 `goto resolved;`，标签 `resolved:` 挂在循环之后。原因：C# 没有带标签的
//     break，而 `break` 在 switch 里只跳出 switch（Go 的 `break loop` 要跳出整个 for）。跳出 switch 的裸 `break`
//     照抄。`fallthrough`（KindSourceFile 掉进 KindModuleDeclaration）→ `goto case Kind.ModuleDeclaration;`（C# 的
//     `goto case` 就是 C# 表达 fallthrough 的方式），前置条件不满足时照 Go 用 `break` 跳出 switch。
//  2) 名字冲突：Go 里 `NameResolver` 同时有**导出字段** `Error`/`Lookup`/`GetSymbolOfDeclaration`/`ArgumentsSymbol` 和
//     同义的**未导出方法** `error`/`lookup`/`getSymbolOfDeclaration`/`argumentsSymbol`；C# 的同类型里字段与方法不能
//     同名（CS0102），所以这四个方法加 `Worker` 后缀（项目惯例：导出/未导出只差名字时的未导出那份加 `Worker`，
//     见 printer 的 `EmitSourceFileWorker` / `GetEmitStateWorker`）：`error` → `ErrorWorker`、`lookup` → `LookupWorker`、
//     `getSymbolOfDeclaration` → `GetSymbolOfDeclarationWorker`、`argumentsSymbol` → `ArgumentsSymbolWorker`（字段名保持
//     照抄 Go）。其余未导出方法照 Go 名转 PascalCase：`useOuterVariableScopeInParameter` →
//     `UseOuterVariableScopeInParameter`、`requiresScopeChange` → `RequiresScopeChange`、
//     `requiresScopeChangeWorker` → `RequiresScopeChangeWorker`（后者签名 = `Ast.Visitor`，可直接传给 `ForEachChild`）。
//  3) Go 的变参回调 `Error func(location *ast.Node, message *diagnostics.Message, args ...any) *ast.Diagnostic` 在 C# 里
//     没有 `Func<>` 对应物 → 命名委托 `ErrorFunc`（`params object?[] args`）。其余回调照 Go 形状映射到
//     `Func<>`/`Action<>`；Go 里可能为 nil 的函数值 → 可空委托字段，调用点 `f?.Invoke(...)` 对应 Go 的
//     `if f != nil { f(...) }`。Go 里可能为 nil 的指针参数/返回值照契约 §3.5 写成 `T?`（`Symbol?`/`Node?`/`Message?`）。
//  4) map 读取：Go 的 `moduleExports[k]`/`symbols[name]`（缺键与 nil map 都得 nil）→ `?.GetValueOrDefault(k)`。
//     `SymbolTable` 是 class，Go 的 nil map（零值）就是 null，因此 `Globals` 字段与 `LookupWorker` 的表参数写
//     `SymbolTable?`：referenceresolver.go 的兜底路径只填 `CompilerOptions`，之后 `lookup(r.Globals, …)` 依赖
//     nil map 读返回 nil（Go 的 map 是引用类型，nil 读合法）。赋非空表的调用点不受影响。
//     唯一例外是 `Lookup` **hook** 的表参数：写非空 `SymbolTable`（调用点用 `symbols!`）。因为 C# 只禁止"可空实参 →
//     非空形参"的委托转换（实测 CS8622），写非空后 checker 侧无论 `GetSymbol(SymbolTable, …)` 还是
//     `GetSymbol(SymbolTable?, …)` 都能直接赋给 `Lookup`；实际传进去的 null 与 Go 传入 nil map 完全一致。
//  5) `CompilerOptions` 字段是 `CompilerOptions?` 在 C# 里会逼着 `r.CompilerOptions.GetIsolatedModules()` 写成 `!`，而
//     Go 的调用点都是非 nil（checker/emitter 传的是真实的 compilerOptions）→ 声明为非空 + `= null!`（与本仓库
//     Module/Resolver.cs 的既有写法一致）；Go 里对 nil 的 CompilerOptions 调 `GetIsolatedModules()` 同样会 panic。
//  6) Go 里对可能为 nil 的指针直接解引用（nil 会 panic）的位置，C# 里流分析证明不了非空时写 `!`：`location.Parent!`、
//     标有 kind 判定保证的 `As*()!`（既定例外）、`GetSymbolOfDeclarationWorker(x)!.Members`、`node.Name()!` 等。
//     语义等价：Go 是 panic，C# 是 NullReferenceException/DebugFailureException。
//  7) 包级函数调用写 `Binder.Xxx(...)`：这 4 个函数按契约是 `partial class Binder` 的静态成员（不在 NameResolver 上），
//     不能照 Go 写裸名。
//  8) `core.IfElse(a, x, y)` → 三元（与位运算混用时加括号）；`core.Some(parameters, f)` → `Core.Some(...)`。
//     `node.Symbol()` → `node.Symbol`（属性）；`ast.InternalSymbolNameDefault` → 裸名 `InternalSymbolNameDefault`
//     （C# 里它是静态类 `Ast` 的常量，`using static TypeScript.Ast.Ast` 已导入；不能写 `Symbol.Xxx`，那个常量不在 `Symbol` 上，
//     写 `Ast.Xxx` 也会被命名空间 `TypeScript.Ast` 抢先解析）。其余访问器（`Locals()`/`Body()`/`Type()`/`Name()`/`Parameters()`/`Text()`）仍是方法。
namespace TypeScript.Binder;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

/// <summary>
/// Go: <c>NameResolver.Error</c> 字段的类型（<c>func(location *ast.Node, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>）。
/// 变参在 C# 里没有 <c>Func&lt;&gt;</c> 对应物，所以用命名委托（见文件头 3）。
/// </summary>
public delegate Diagnostic ErrorFunc(Node location, Message message, params object?[] args);

/// <summary>Go: <c>type NameResolver struct</c>（binder/nameresolver.go:9）。</summary>
public sealed class NameResolver
{
    public CompilerOptions CompilerOptions = null!;
    public Func<Node, Symbol?>? GetSymbolOfDeclaration;
    public ErrorFunc? Error;
    public SymbolTable? Globals;
    public Symbol? ArgumentsSymbol;
    public Symbol? RequireSymbol;
    public Func<SymbolTable, string, SymbolFlags, Symbol?>? Lookup;
    public Action<Symbol, SymbolFlags>? SymbolReferenced;
    public Action<Node, Tristate>? SetRequiresScopeChangeCache;
    public Func<Node, Tristate>? GetRequiresScopeChangeCache;
    public Func<Node?, string, Node, Symbol?, bool>? OnPropertyWithInvalidInitializer;
    public Action<Node?, string, SymbolFlags, Message>? OnFailedToResolveSymbol;
    public Action<Node?, Symbol, SymbolFlags, Node?, Node?, bool>? OnSuccessfullyResolvedSymbol;

    public Symbol? Resolve(Node? location, string name, SymbolFlags meaning, Message? nameNotFoundMessage, bool isUse, bool excludeGlobals)
    {
        Symbol? result = null;
        Node? lastLocation = null;
        Node? lastSelfReferenceLocation = null;
        Node? propertyWithInvalidInitializer = null;
        Node? associatedDeclarationForContainingInitializerOrBindingName = null;
        var withinDeferredContext = false;
        Node? grandparent = null;
        var originalLocation = location; // needed for did-you-mean error reporting, which gathers candidates starting from the original location
        var nameIsConst = name == "const";
        while (location is not null)
        {
            if (nameIsConst && IsConstAssertion(location))
            {
                // `const` in an `as const` has no symbol, but issues no error because there is no *actual* lookup of the type
                // (it refers to the constant type of the expression instead)
                return null;
            }
            if (IsModuleOrEnumDeclaration(location) && lastLocation is not null && location.Name() == lastLocation)
            {
                // If lastLocation is the name of a namespace or enum, skip the parent since it will have own locals that could
                // conflict.
                lastLocation = location;
                location = location.Parent!;
            }
            var isModuleAttributes = IsModuleDeclaration(location) &&
                location.AsModuleDeclaration()!.Attributes is not null &&
                lastLocation == location.AsModuleDeclaration()!.Attributes;
            var locals = location.Locals();
            // Locals of a source file are not in scope (because they get merged into the global symbol table)
            if (locals is not null && !IsGlobalSourceFile(location))
            {
                result = LookupWorker(locals, name, meaning);
                if (result is not null)
                {
                    var useResult = true;
                    if (isModuleAttributes)
                    {
                        useResult = false;
                    }
                    else if (IsFunctionLike(location) && lastLocation is not null && lastLocation != location.Body())
                    {
                        // symbol lookup restrictions for function-like declarations
                        // - Type parameters of a function are in scope in the entire function declaration, including the parameter
                        //   list and return type. However, local types are only in scope in the function body.
                        // - parameters are only in the scope of function body
                        // This restriction does not apply to JSDoc comment types because they are parented
                        // at a higher level than type parameters would normally be
                        if ((meaning & result.Flags & SymbolFlags.Type) != 0 && lastLocation.Kind != Kind.JSDoc)
                        {
                            // type parameters are visible in parameter list, return type and type parameter list.
                            // Synthetic fake scopes are added for signatures so type parameters are accessible from them.
                            useResult = (result.Flags & SymbolFlags.TypeParameter) != 0 &&
                                ((lastLocation.Flags & NodeFlags.Synthesized) != 0 ||
                                    lastLocation == location.Type() ||
                                    lastLocation.Kind == Kind.Parameter ||
                                    lastLocation.Kind == Kind.JSDocParameterTag ||
                                    lastLocation.Kind == Kind.JSDocReturnTag ||
                                    lastLocation.Kind == Kind.TypeParameter);
                        }
                        if ((meaning & result.Flags & SymbolFlags.Variable) != 0)
                        {
                            // expression inside parameter will lookup as normal variable scope when targeting es2015+
                            if (UseOuterVariableScopeInParameter(result, location, lastLocation))
                            {
                                useResult = false;
                            }
                            else if ((result.Flags & SymbolFlags.FunctionScopedVariable) != 0)
                            {
                                // parameters are visible only inside function body, parameter list and return type
                                // technically for parameter list case here we might mix parameters and variables declared in function,
                                // however it is detected separately when checking initializers of parameters
                                // to make sure that they reference no variables declared after them.
                                useResult = lastLocation.Kind == Kind.Parameter ||
                                    (lastLocation.Flags & NodeFlags.Synthesized) != 0 ||
                                    lastLocation == location.Type() && FindAncestor(result.ValueDeclaration, IsParameterDeclaration) is not null;
                            }
                        }
                    }
                    else if (location.Kind == Kind.ConditionalType)
                    {
                        // A type parameter declared using 'infer T' in a conditional type is visible only in
                        // the true branch of the conditional type.
                        useResult = lastLocation == location.AsConditionalTypeNode()!.TrueType;
                    }
                    if (useResult)
                    {
                        goto resolved; // Go: break loop
                    }
                    result = null;
                }
            }
            withinDeferredContext = withinDeferredContext || Binder.GetIsDeferredContext(location, lastLocation);
            switch (location.Kind)
            {
                case Kind.SourceFile:
                    if (!IsExternalOrCommonJSModule(location.AsSourceFile()!))
                    {
                        break;
                    }
                    goto case Kind.ModuleDeclaration;
                case Kind.ModuleDeclaration:
                    if (isModuleAttributes)
                    {
                        break;
                    }
                    var moduleSymbol = GetSymbolOfDeclarationWorker(location);
                    if (moduleSymbol is null)
                    {
                        break;
                    }
                    var moduleExports = moduleSymbol.Exports;
                    if (IsSourceFile(location) || (IsModuleDeclaration(location) && (location.Flags & NodeFlags.Ambient) != 0 && !IsGlobalScopeAugmentation(location)))
                    {
                        // It's an external module. First see if the module has an export default and if the local
                        // name of that export default matches.
                        result = moduleExports?.GetValueOrDefault(InternalSymbolNameDefault);
                        if (result is not null)
                        {
                            var localSymbol = Binder.GetLocalSymbolForExportDefault(result);
                            if (localSymbol is not null && (result.Flags & meaning) != 0 && localSymbol.Name == name)
                            {
                                goto resolved; // Go: break loop
                            }
                            result = null;
                        }
                        // Because of module/namespace merging, a module's exports are in scope,
                        // yet we never want to treat an export specifier as putting a member in scope.
                        // Therefore, if the name we find is purely an export specifier, it is not actually considered in scope.
                        // Two things to note about this:
                        //     1. We have to check this without calling getSymbol. The problem with calling getSymbol
                        //        on an export specifier is that it might find the export specifier itself, and try to
                        //        resolve it as an alias. This will cause the checker to consider the export specifier
                        //        a circular alias reference when it might not be.
                        //     2. We check === SymbolFlags.Alias in order to check that the symbol is *purely*
                        //        an alias. If we used &, we'd be throwing out symbols that have non alias aspects,
                        //        which is not the desired behavior.
                        var moduleExport = moduleExports?.GetValueOrDefault(name);
                        if (moduleExport is not null && moduleExport.Flags == SymbolFlags.Alias && (GetDeclarationOfKind(moduleExport, Kind.ExportSpecifier) is not null || GetDeclarationOfKind(moduleExport, Kind.NamespaceExport) is not null))
                        {
                            break;
                        }
                    }
                    if (name != InternalSymbolNameDefault)
                    {
                        if ((result = LookupWorker(moduleExports, name, meaning & SymbolFlags.ModuleMember)) is not null)
                        {
                            if (IsSourceFile(location) && location.AsSourceFile()!.CommonJSModuleIndicator is not null && (result.Flags & SymbolFlags.Type) == 0)
                            {
                                result = null;
                            }
                            else
                            {
                                goto resolved; // Go: break loop
                            }
                        }
                    }
                    break;
                case Kind.EnumDeclaration:
                    var enumSymbol = GetSymbolOfDeclarationWorker(location);
                    if (enumSymbol is null)
                    {
                        break;
                    }
                    result = LookupWorker(enumSymbol.Exports, name, meaning & SymbolFlags.EnumMember);
                    if (result is not null)
                    {
                        if (nameNotFoundMessage is not null && CompilerOptions.GetIsolatedModules() && (location.Flags & NodeFlags.Ambient) == 0 && GetSourceFileOfNode(location) != GetSourceFileOfNode(result.ValueDeclaration))
                        {
                            var isolatedModulesLikeFlagName = IfElse(CompilerOptions.VerbatimModuleSyntax == Tristate.TSTrue, "verbatimModuleSyntax", "isolatedModules");
                            ErrorWorker(originalLocation!, Cannot_access_0_from_another_file_without_qualification_when_1_is_enabled_Use_2_instead,
                                name, isolatedModulesLikeFlagName, enumSymbol.Name + "." + name);
                        }
                        goto resolved; // Go: break loop
                    }
                    break;
                case Kind.PropertyDeclaration:
                    if (!IsStatic(location))
                    {
                        var ctor = FindConstructorDeclaration(location.Parent);
                        if (ctor is not null && ctor.Locals() is not null)
                        {
                            if (LookupWorker(ctor.Locals(), name, meaning & SymbolFlags.Value) is not null)
                            {
                                // Remember the property node, it will be used later to report appropriate error
                                propertyWithInvalidInitializer = location;
                            }
                        }
                    }
                    break;
                case Kind.ClassDeclaration:
                case Kind.ClassExpression:
                case Kind.InterfaceDeclaration:
                    result = LookupWorker(GetSymbolOfDeclarationWorker(location)!.Members, name, meaning & SymbolFlags.Type);
                    if (result is not null)
                    {
                        if (!Binder.IsTypeParameterSymbolDeclaredInContainer(result, location))
                        {
                            // ignore type parameters not declared in this container
                            result = null;
                            break;
                        }
                        if (lastLocation is not null && IsStatic(lastLocation))
                        {
                            // TypeScript 1.0 spec (April 2014): 3.4.1
                            // The scope of a type parameter extends over the entire declaration with which the type
                            // parameter list is associated, with the exception of static member declarations in classes.
                            if (nameNotFoundMessage is not null)
                            {
                                ErrorWorker(originalLocation!, Static_members_cannot_reference_class_type_parameters);
                            }
                            return null;
                        }
                        goto resolved; // Go: break loop
                    }
                    if (IsClassExpression(location) && (meaning & SymbolFlags.Class) != 0)
                    {
                        var className = location.Name();
                        if (className is not null && name == className.Text())
                        {
                            result = location.Symbol;
                            goto resolved; // Go: break loop
                        }
                    }
                    break;
                case Kind.ExpressionWithTypeArguments:
                    if (lastLocation == location.Expression() && IsHeritageClause(location.Parent!) && location.Parent!.AsHeritageClause()!.Token == Kind.ExtendsKeyword)
                    {
                        var container = location.Parent!.Parent;
                        if (IsClassLike(container))
                        {
                            result = LookupWorker(GetSymbolOfDeclarationWorker(container!)!.Members, name, meaning & SymbolFlags.Type);
                            if (result is not null)
                            {
                                if (nameNotFoundMessage is not null)
                                {
                                    ErrorWorker(originalLocation!, Base_class_expressions_cannot_reference_class_type_parameters);
                                }
                                return null;
                            }
                        }
                    }
                    break;
                // It is not legal to reference a class's own type parameters from a computed property name that
                // belongs to the class. For example:
                //
                //   function foo<T>() { return '' }
                //   class C<T> { // <-- Class's own type parameter T
                //       [foo<T>()]() { } // <-- Reference to T from class's own computed property
                //   }
                case Kind.ComputedPropertyName:
                    grandparent = location.Parent!.Parent;
                    if (IsClassLike(grandparent) || IsInterfaceDeclaration(grandparent!))
                    {
                        // A reference to this grandparent's type parameters would be an error
                        result = LookupWorker(GetSymbolOfDeclarationWorker(grandparent!)!.Members, name, meaning & SymbolFlags.Type);
                        if (result is not null)
                        {
                            if (nameNotFoundMessage is not null)
                            {
                                ErrorWorker(originalLocation!, A_computed_property_name_cannot_reference_a_type_parameter_from_its_containing_type);
                            }
                            return null;
                        }
                    }
                    break;
                case Kind.MethodDeclaration:
                case Kind.Constructor:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                case Kind.FunctionDeclaration:
                    if ((meaning & SymbolFlags.Variable) != 0 && name == "arguments")
                    {
                        result = ArgumentsSymbolWorker();
                        goto resolved; // Go: break loop
                    }
                    break;
                case Kind.FunctionExpression:
                    if ((meaning & SymbolFlags.Variable) != 0 && name == "arguments")
                    {
                        result = ArgumentsSymbolWorker();
                        goto resolved; // Go: break loop
                    }
                    if ((meaning & SymbolFlags.Function) != 0)
                    {
                        var functionName = location.AsFunctionExpression()!.Name();
                        if (functionName is not null && name == functionName.Text())
                        {
                            result = location.Symbol;
                            goto resolved; // Go: break loop
                        }
                    }
                    break;
                case Kind.Decorator:
                    // Decorators are resolved at the class declaration. Resolving at the parameter
                    // or member would result in looking up locals in the method.
                    //
                    //   function y() {}
                    //   class C {
                    //       method(@y x, y) {} // <-- decorator y should be resolved at the class declaration, not the parameter.
                    //   }
                    //
                    if (location.Parent is not null && location.Parent.Kind == Kind.Parameter)
                    {
                        location = location.Parent;
                    }
                    //   function y() {}
                    //   class C {
                    //       @y method(x, y) {} // <-- decorator y should be resolved at the class declaration, not the method.
                    //   }
                    //
                    // class Decorators are resolved outside of the class to avoid referencing type parameters of that class.
                    //
                    //   type T = number;
                    //   declare function y(x: T): any;
                    //   @param(1 as T) // <-- T should resolve to the type alias outside of class C
                    //   class C<T> {}
                    if (location.Parent is not null && (IsClassElement(location.Parent) || location.Parent.Kind == Kind.ClassDeclaration))
                    {
                        location = location.Parent;
                    }
                    break;
                case Kind.Parameter:
                    var parameterDeclaration = location.AsParameterDeclaration()!;
                    if (lastLocation is not null && (lastLocation == parameterDeclaration.Initializer ||
                        lastLocation == parameterDeclaration.Name() && IsBindingPattern(lastLocation)))
                    {
                        if (associatedDeclarationForContainingInitializerOrBindingName is null)
                        {
                            associatedDeclarationForContainingInitializerOrBindingName = location;
                        }
                    }
                    break;
                case Kind.BindingElement:
                    var bindingElement = location.AsBindingElement()!;
                    if (lastLocation is not null && (lastLocation == bindingElement.Initializer ||
                        lastLocation == bindingElement.Name() && IsBindingPattern(lastLocation)))
                    {
                        if (IsPartOfParameterDeclaration(location) && associatedDeclarationForContainingInitializerOrBindingName is null)
                        {
                            associatedDeclarationForContainingInitializerOrBindingName = location;
                        }
                    }
                    break;
                case Kind.InferType:
                    if ((meaning & SymbolFlags.TypeParameter) != 0)
                    {
                        var parameterName = location.AsInferTypeNode()!.TypeParameter!.AsTypeParameterDeclaration()!.Name();
                        if (parameterName is not null && name == parameterName.Text())
                        {
                            result = location.AsInferTypeNode()!.TypeParameter!.Symbol;
                            goto resolved; // Go: break loop
                        }
                    }
                    break;
                case Kind.ExportSpecifier:
                    var exportSpecifier = location.AsExportSpecifier()!;
                    if (lastLocation is not null && lastLocation == exportSpecifier.PropertyName && location.Parent!.Parent!.ModuleSpecifier() is not null)
                    {
                        location = location.Parent!.Parent!.Parent!;
                    }
                    break;
            }
            if (Binder.IsSelfReferenceLocation(location, lastLocation))
            {
                lastSelfReferenceLocation = location;
            }
            lastLocation = location;
            // !!! In Strada, JSDocTemplateTag/JSDocParameterTag/JSDocReturnTag locations skip to
            // getEffectiveContainerForJSDocTemplateTag/getHostSignatureFromJSDoc instead of location.parent.
            // This is a no-op currently because JSDoc nodes have no locals and getEffectiveJSDocHost is not
            // fully ported for JS assignment patterns.
            location = location.Parent;
        }
    // Go 的 `loop:` + `break loop` → 这里的标签 + `goto resolved;`（C# 的 `break` 在 switch 里只跳出 switch，见文件头 1）。
    resolved:
        // We just climbed up parents looking for the name, meaning that we started in a descendant node of `lastLocation`.
        // If `result === lastSelfReferenceLocation.symbol`, that means that we are somewhere inside `lastSelfReferenceLocation` looking up a name, and resolving to `lastLocation` itself.
        // That means that this is a self-reference of `lastLocation`, and shouldn't count this when considering whether `lastLocation` is used.
        if (isUse && result is not null && (lastSelfReferenceLocation is null || result != lastSelfReferenceLocation.Symbol))
        {
            SymbolReferenced?.Invoke(result, meaning);
        }
        if (result is null && !excludeGlobals)
        {
            result = LookupWorker(Globals, name, meaning | SymbolFlags.GlobalLookup);
        }
        if (result is null)
        {
            if (originalLocation is not null && IsInJSFile(originalLocation) && originalLocation.Parent is not null)
            {
                if (IsRequireCall(originalLocation.Parent, false /*requireStringLiteralLikeArgument*/))
                {
                    return RequireSymbol;
                }
            }
        }
        if (nameNotFoundMessage is not null)
        {
            if (propertyWithInvalidInitializer is not null && OnPropertyWithInvalidInitializer is not null && OnPropertyWithInvalidInitializer(originalLocation, name, propertyWithInvalidInitializer, result))
            {
                return null;
            }
            if (result is null)
            {
                OnFailedToResolveSymbol?.Invoke(originalLocation, name, meaning, nameNotFoundMessage);
            }
            else
            {
                OnSuccessfullyResolvedSymbol?.Invoke(originalLocation, result, meaning, lastLocation, associatedDeclarationForContainingInitializerOrBindingName, withinDeferredContext);
            }
        }
        return result;
    }

    private bool UseOuterVariableScopeInParameter(Symbol result, Node location, Node lastLocation)
    {
        if (IsParameterDeclaration(lastLocation))
        {
            var body = location.Body();
            if (body is not null && result.ValueDeclaration is not null && result.ValueDeclaration.Pos() >= body.Pos() && result.ValueDeclaration.End() <= body.End())
            {
                // check for several cases where we introduce temporaries that require moving the name/initializer of the parameter to the body
                // - static field in a class expression
                // - optional chaining pre-es2020
                // - nullish coalesce pre-es2020
                // - spread assignment in binding pattern pre-es2017
                var functionLocation = location;
                var declarationRequiresScopeChange = Tristate.TSUnknown;
                if (GetRequiresScopeChangeCache is not null)
                {
                    declarationRequiresScopeChange = GetRequiresScopeChangeCache(functionLocation);
                }
                if (declarationRequiresScopeChange == Tristate.TSUnknown)
                {
                    declarationRequiresScopeChange = IfElse(Core.Some(functionLocation.Parameters(), RequiresScopeChange), Tristate.TSTrue, Tristate.TSFalse);
                    if (SetRequiresScopeChangeCache is not null)
                    {
                        SetRequiresScopeChangeCache(functionLocation, declarationRequiresScopeChange);
                    }
                }
                return declarationRequiresScopeChange != Tristate.TSTrue;
            }
        }
        return false;
    }

    private bool RequiresScopeChange(Node node)
    {
        var d = node.AsParameterDeclaration()!;
        return RequiresScopeChangeWorker(d.Name()!) || d.Initializer is not null && RequiresScopeChangeWorker(d.Initializer);
    }

    private bool RequiresScopeChangeWorker(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ArrowFunction:
            case Kind.FunctionExpression:
            case Kind.FunctionDeclaration:
            case Kind.Constructor:
                return false;
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.PropertyAssignment:
                return RequiresScopeChangeWorker(node.Name()!);
            case Kind.PropertyDeclaration:
                if (HasStaticModifier(node))
                {
                    return !CompilerOptions.GetEmitStandardClassFields();
                }
                return RequiresScopeChangeWorker(node.AsPropertyDeclaration()!.Name()!);
            default:
                if (IsNullishCoalesce(node) || IsOptionalChain(node))
                {
                    return CompilerOptions.GetEmitScriptTarget() < ScriptTarget.ES2020;
                }
                if (IsBindingElement(node) && node.AsBindingElement()!.DotDotDotToken is not null && IsObjectBindingPattern(node.Parent!))
                {
                    return CompilerOptions.GetEmitScriptTarget() < ScriptTarget.ES2017;
                }
                if (IsTypeNode(node))
                {
                    return false;
                }
                return node.ForEachChild(RequiresScopeChangeWorker);
        }
    }

    private void ErrorWorker(Node location, Message message, params object?[] args)
    {
        Error?.Invoke(location, message, args);
        // Default implementation does not report errors
    }

    private Symbol? GetSymbolOfDeclarationWorker(Node node)
    {
        if (GetSymbolOfDeclaration is not null)
        {
            return GetSymbolOfDeclaration(node);
        }

        // Default implementation does not support merged symbols
        return node.Symbol;
    }

    private Symbol? LookupWorker(SymbolTable? symbols, string name, SymbolFlags meaning)
    {
        if (Lookup is not null)
        {
            // Go 里表参数是 nilable 的 map（零值 nil，读 nil map 得零值），C# 的 `Globals` 因此是 `SymbolTable?`；
            // hook 的参数类型写非空（见文件头 4）：这样 checker 侧无论写 `SymbolTable` 还是 `SymbolTable?` 都能直接赋给
            // `Lookup`（C# 只禁止"实参可空 → 形参非空"的委托转换）。传进来的 nil 与 Go 的 nil map 一致。
            return Lookup(symbols!, name, meaning);
        }
        // Default implementation does not support following aliases or merged symbols
        if (meaning != 0)
        {
            var symbol = symbols?.GetValueOrDefault(name);
            if (symbol is not null)
            {
                if ((symbol.Flags & meaning) != 0)
                {
                    return symbol;
                }
            }
        }
        return null;
    }

    private Symbol ArgumentsSymbolWorker()
    {
        if (ArgumentsSymbol is null)
        {
            // Default implementation synthesizes a transient symbol for `arguments`
            ArgumentsSymbol = new Symbol { Name = "arguments", Flags = SymbolFlags.Property | SymbolFlags.Transient };
        }
        return ArgumentsSymbol;
    }
}

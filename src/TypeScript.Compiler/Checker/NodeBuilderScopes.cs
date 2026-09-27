// ported from: internal/checker/nodebuilderscopes.go（整文件，1-251）
//
// 与 Go 的差异（逐条）：
// 1) 包级函数 `cloneNodeBuilderContext` → `partial class Checker` 的 static 成员（§5.5）；
//    Go 的函数内局部类型 `localsRecord` → 文件级 `internal sealed class LocalsRecord`。
// 2) Go 未导出字段 → `_camelCase`（§5.5）：`context.typeParameterNames`→`context._typeParameterNames`、
//    `links.fakeScopeForSignatureDeclaration`→`links._fakeScopeForSignatureDeclaration`、
//    `b.ctx.enclosingDeclaration`→`b._ctx._enclosingDeclaration`。
// 3) Go 的 `func() ([]*ast.Symbol, func())` → C# `Action EnterSignatureScope(Signature signature, out Symbol[] expandedParams)`
//    （C# 没有多返回值的闭包，`out` 参数最贴近 Go 的形状）。
// 4) Go 的相互递归闭包（`bindElement`/`bindPattern` 先声明后赋值）→ C# 局部函数。
// 5) `collections.Set`/`CopyOnWrite*` 的零值语义 → 显式 new（见 Collections/Cow.cs 文件头）。
// 6) `debug.Assert(cond)` → `Debug.Assert(cond)`；`core.Some` → `Some`。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Checker.Checker;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;

internal sealed class LocalsRecord
{
    internal string _name = "";
    internal Symbol? _oldSymbol;
}

public sealed partial class NodeBuilderImpl
{
    internal Action AddSymbolTypeToContext(Symbol symbol, Type t)
    {
        var id = GetSymbolId(symbol);
        var oldTypeExists = _ctx!._enclosingSymbolTypes.TryGetValue(id, out var oldType);
        _ctx!._enclosingSymbolTypes[id] = t;
        return () =>
        {
            if (oldTypeExists)
            {
                _ctx!._enclosingSymbolTypes[id] = oldType!;
            }
            else
            {
                _ctx!._enclosingSymbolTypes.Remove(id);
            }
        };
    }

    internal Action EnterSignatureScope(Signature signature, out Symbol[] expandedParams)
    {
        expandedParams = _ch.GetExpandedParametersWorker(signature, true /*skipUnionExpanding*/)[0];
        return EnterNewScope(signature._declaration, expandedParams, signature._typeParameters, signature._parameters, signature._mapper);
    }

    internal Action EnterNewScope(Node? declaration, Symbol[]? expandedParams, Type[]? typeParameters, Symbol[]? originalParameters, TypeMapper? mapper)
    {
        var cleanupContext = CloneNodeBuilderContext(_ctx!);
        // For regular function/method declarations, the enclosing declaration will already be signature.declaration,
        // so this is a no-op, but for arrow functions and function expressions, the enclosing declaration will be
        // the declaration that the arrow function / function expression is assigned to.
        //
        // If the parameters or return type include "typeof globalThis.paramName", using the wrong scope will lead
        // us to believe that we can emit "typeof paramName" instead, even though that would refer to the parameter,
        // not the global. Make sure we are in the right scope by changing the enclosingDeclaration to the function.
        //
        // We can't use the declaration directly; it may be in another file and so we may lose access to symbols
        // accessible to the current enclosing declaration, or gain access to symbols not accessible to the current
        // enclosing declaration. To keep this chain accurate, insert a fake scope into the chain which makes the
        // function's parameters visible.
        Action? cleanupParams = null;
        Action? cleanupTypeParams = null;
        var oldEnclosingDecl = _ctx!._enclosingDeclaration;
        var oldMapper = _ctx!._mapper;
        if (mapper is not null)
        {
            _ctx!._mapper = mapper;
        }

        if (_ctx!._enclosingDeclaration is not null && declaration is not null)
        {
            // As a performance optimization, reuse the same fake scope within this chain.
            // This is especially needed when we are working on an excessively deep type;
            // if we don't do this, then we spend all of our time adding more and more
            // scopes that need to be searched in isSymbolAccessible later. Since all we
            // really want to do is to mark certain names as unavailable, we can just keep
            // all of the names we're introducing in one large table and push/pop from it as
            // needed; isSymbolAccessible will walk upward and find the closest "fake" scope,
            // which will conveniently report on any and all faked scopes in the chain.
            //
            // It'd likely be better to store this somewhere else for isSymbolAccessible, but
            // since that API _only_ uses the enclosing declaration (and its parents), this is
            // seems like the best way to inject names into that search process.
            //
            // Note that we only check the most immediate enclosingDeclaration; the only place we
            // could potentially add another fake scope into the chain is right here, so we don't
            // traverse all ancestors.
            Action? PushFakeScope(string kind, Action<Action<string, Symbol>> addAll)
            {
                // We only ever need to look two declarations upward.
                Debug.Assert(_ctx!._enclosingDeclaration is not null);
                Node? existingFakeScope = null;
                if (_links.Has(_ctx!._enclosingDeclaration!))
                {
                    var links = _links.Get(_ctx!._enclosingDeclaration!);
                    if (links._fakeScopeForSignatureDeclaration is not null && links._fakeScopeForSignatureDeclaration == kind)
                    {
                        existingFakeScope = _ctx!._enclosingDeclaration;
                    }
                }

                if (existingFakeScope is null && _ctx!._enclosingDeclaration!.Parent is not null)
                {
                    if (_links.Has(_ctx!._enclosingDeclaration.Parent))
                    {
                        var links = _links.Get(_ctx!._enclosingDeclaration.Parent);
                        if (links._fakeScopeForSignatureDeclaration is not null && links._fakeScopeForSignatureDeclaration == kind)
                        {
                            existingFakeScope = _ctx!._enclosingDeclaration.Parent;
                        }
                    }
                }

                Debug.Assert(existingFakeScope is null || IsBlock(existingFakeScope));

                SymbolTable? locals = null;
                if (existingFakeScope is not null)
                {
                    locals = existingFakeScope.Locals();
                }

                locals ??= new SymbolTable();
                var newLocals = new List<string>();
                var oldLocals = new List<LocalsRecord>();
                addAll((name, symbol) =>
                {
                    // Add cleanup information only if we don't own the fake scope
                    if (existingFakeScope is not null)
                    {
                        if (!locals.TryGetValue(name, out var oldSymbol) || oldSymbol is null)
                        {
                            newLocals.Add(name);
                        }
                        else
                        {
                            oldLocals.Add(new LocalsRecord { _name = name, _oldSymbol = oldSymbol });
                        }
                    }

                    locals[name] = symbol;
                });

                if (existingFakeScope is null)
                {
                    // Use a Block for this; the type of the node doesn't matter so long as it
                    // has locals, and this is cheaper/easier than using a function-ish Node.
                    var fakeScope = _f.NewBlock(_f.NewNodeList([]), false);
                    _links.Get(fakeScope)._fakeScopeForSignatureDeclaration = kind;
                    var data = fakeScope.LocalsContainerData()!;
                    data.Locals = locals;
                    fakeScope.Parent = _ctx!._enclosingDeclaration;
                    _ctx!._enclosingDeclaration = fakeScope;
                    return null;
                }
                else
                {
                    // We did not create the current scope, so we have to clean it up
                    void Undo()
                    {
                        foreach (var s in newLocals)
                        {
                            locals.Remove(s);
                        }

                        foreach (var s in oldLocals)
                        {
                            locals[s._name] = s._oldSymbol!;
                        }
                    }

                    return Undo;
                }
            }

            if (expandedParams is null || !Some(expandedParams, p => p is not null))
            {
                cleanupParams = null;
            }
            else
            {
                cleanupParams = PushFakeScope("params", add =>
                {
                    if (expandedParams is null)
                    {
                        return;
                    }

                    for (var pIndex = 0; pIndex < expandedParams.Length; pIndex++)
                    {
                        var param = expandedParams[pIndex];
                        Symbol? originalParam = null;
                        if (pIndex < (originalParameters?.Length ?? 0))
                        {
                            originalParam = originalParameters![pIndex];
                        }

                        if (originalParameters is not null && originalParam != param)
                        {
                            // Can't reference the expanded parameter name, just the original, unless we've expanded the param list for some reason
                            if (originalParam is not null)
                            {
                                add(originalParam.Name, originalParam);
                            }
                        }
                        else if (!Some(param.Declarations, d =>
                        {
                            void BindElement(BindingElement e)
                            {
                                if (e.Name() is not null && IsBindingPattern(e.Name()!))
                                {
                                    BindPattern(e.Name()!.AsBindingPattern()!);
                                    return;
                                }

                                var symbol = _ch.GetSymbolOfDeclaration(e.AsNode());
                                if (symbol is not null) // omitted expressions are now parsed as nameless binding patterns and also have no symbol
                                {
                                    add(symbol.Name, symbol);
                                }
                            }

                            void BindPattern(BindingPattern p)
                            {
                                foreach (var e in p.Elements!.Nodes!)
                                {
                                    switch (e.Kind)
                                    {
                                        case Kind.OmittedExpression:
                                            return;
                                        case Kind.BindingElement:
                                            BindElement(e.AsBindingElement()!);
                                            return;
                                        default:
                                            Debug.Fail("Unhandled binding element kind");
                                            return;
                                    }
                                }
                            }

                            if (IsParameterDeclaration(d) && d.Name() is not null && IsBindingPattern(d.Name()!))
                            {
                                BindPattern(d.Name()!.AsBindingPattern()!);
                                return true;
                            }

                            return false;
                        }))
                        {
                            add(param.Name, param);
                        }
                    }
                });
            }

            if ((_ctx!._flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && typeParameters is not null && Some(typeParameters, p => p is not null))
            {
                cleanupTypeParams = PushFakeScope("typeParams", add =>
                {
                    if (typeParameters is null)
                    {
                        return;
                    }

                    foreach (var typeParam in typeParameters)
                    {
                        if (typeParam is null)
                        {
                            continue;
                        }

                        var typeParamName = TypeParameterToName(typeParam).Text;
                        add(typeParamName, typeParam._symbol!);
                    }
                });
            }
        }

        return () =>
        {
            cleanupParams?.Invoke();
            cleanupTypeParams?.Invoke();
            cleanupContext();
            _ctx!._enclosingDeclaration = oldEnclosingDecl;
            _ctx!._mapper = oldMapper;
        };
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func cloneNodeBuilderContext(context *NodeBuilderContext) func()</c>（nodebuilderscopes.go:10）。</summary>
    internal static Action CloneNodeBuilderContext(NodeBuilderContext context)
    {
        // Make type parameters created within this context not consume the name outside this context
        // The symbol serializer ends up creating many sibling scopes that all need "separate" contexts when
        // it comes to naming things - within a normal `typeToTypeNode` call, the node builder only ever descends
        // through the type tree, so the only cases where we could have used distinct sibling scopes was when there
        // were multiple generic overloads with similar generated type parameter names
        // The effect:
        // When we write out
        // export const x: <T>(x: T) => T
        // export const y: <T>(x: T) => T
        // we write it out like that, rather than as
        // export const x: <T>(x: T) => T
        // export const y: <T_1>(x: T_1) => T_1
        var restoreNames = context._typeParameterNames.EnterScope();
        var restoreNamesByText = context._typeParameterNamesByText.EnterScope();
        var restoreNamesByTextNextNameCount = context._typeParameterNamesByTextNextNameCount.EnterScope();
        var restoreSymbolList = context._typeParameterSymbolList.EnterScope();
        return () =>
        {
            restoreNames();
            restoreNamesByText();
            restoreNamesByTextNextNameCount();
            restoreSymbolList();
        };
    }
}

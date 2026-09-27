// ported from: internal/checker/printer.go
//
// 与 Go 的差异（逐条）：
// 1) 同名方法冲突（artifacts/checker-method-collisions.md）：Go 未导出的门面 vs 导出成员 → 未导出者加 `Worker`
//    后缀。本文件的对应关系：
//      typeToString       → TypeToStringWorker        （导出 TypeToString 保留）
//      typeToStringEx     → TypeToStringExWorker      （导出 TypeToStringEx 保留）
//      symbolToString     → SymbolToStringWorker      （导出 SymbolToString 保留）
//      symbolToStringEx   → SymbolToStringExWorker    （导出 SymbolToStringEx 保留）
//      signatureToStringEx→ SignatureToStringExWorker （导出 SignatureToStringEx 保留）
//      typePredicateToString → TypePredicateToStringWorker（导出 TypePredicateToString 在 exports.go）
//      valueToString      → ValueToStringWorker       （包级导出 ValueToString 在 utilities.go，两者同签名会 CS0111）
//    其余（`SignatureToString`/`TypePredicateToStringEx`/`FormatUnionTypes`/`TypeToTypeNode`/`TypeToTypeNodeEx`/
//    `SignatureToSignatureDeclaration`/`TypePredicateToTypePredicateNode`/`ExpandSymbolForHover`/
//    `TypeParameterToStringEx`）无冲突，保持 Go 名。
// 2) Go 的包级函数（`createPrinterWith*`/`toNodeBuilderFlags`）→ 同一个 `partial class Checker` 的 `private static`
//    成员（只有本文件用）。
// 3) `printer.NewPrinter/NewTextWriter/GetSingleLineStringWriter` → `Printer`（TypeScript.Printer）的静态成员；
//    `printer.PrinterOptions{...}` → 对象初始化器；`printer.PrintHandlers{}` → `new PrintHandlers()`。
// 4) Go 的可空指针参数（`tracker`/`sourceMapGenerator`/`vc`）→ `?`；`c.unresolvedType` → `_unresolvedType`；
//    `c.compilerOptions` → `_compilerOptions`；`t == c.unresolvedType` → `ReferenceEquals`。
// 5) `result[0:maxLength-len("...")] + "..."` → `result[..(maxLength - 3)] + "..."`（Go 的 `len("...")` 是 3）。
// 6) `nodeBuilder.verbosity` → `nodeBuilder._verbosity`；`strconv`/`strings.Builder` → `StringBuilder`。
// 7) Go 的 `builder = nodeBuilder.SymbolToNode`（方法值）→ C# 用 if/else 赋值具名委托局部变量
//    （`?:` 里的方法组转换推断不出委托类型）。
// 8) `nodebuilder.Flags`/`InternalFlags` 用别名 `NodeBuilderFlags`/`NodeBuilderInternalFlags`
//    （`using TypeScript.NodeBuilder;` 会让 `NodeBuilder` 与本包的 `NodeBuilder` 类型撞名）。
namespace TypeScript.Checker;

using System;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Checker.Checker;
using static TypeScript.Core.Core;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;
using NodeBuilderSymbolTracker = TypeScript.NodeBuilder.SymbolTracker;

public sealed partial class Checker
{
    // TODO: Memoize once per checker to retain threadsafety
    internal static Printer CreatePrinterWithDefaults(EmitContext emitContext) =>
        Printer.NewPrinter(new PrinterOptions(), new PrintHandlers(), emitContext);

    internal static Printer CreatePrinterWithRemoveComments(EmitContext emitContext) =>
        Printer.NewPrinter(new PrinterOptions { RemoveComments = true }, new PrintHandlers(), emitContext);

    internal static Printer CreatePrinterWithRemoveCommentsOmitTrailingSemicolon(EmitContext emitContext) =>
        Printer.NewPrinter(new PrinterOptions
        {
            RemoveComments = true,
            OmitTrailingSemicolon = true,
        }, new PrintHandlers(), emitContext);

    internal static Printer CreatePrinterWithRemoveCommentsOmitTrailingSemicolonNeverAsciiEscape(EmitContext emitContext) =>
        Printer.NewPrinter(new PrinterOptions
        {
            RemoveComments = true,
            OmitTrailingSemicolon = true,
            NeverAsciiEscape = true,
        }, new PrintHandlers(), emitContext);

    internal static Printer CreatePrinterWithRemoveCommentsNeverAsciiEscape(EmitContext emitContext) =>
        Printer.NewPrinter(new PrinterOptions
        {
            RemoveComments = true,
            NeverAsciiEscape = true,
        }, new PrintHandlers(), emitContext);

    public string TypeToString(Type t)
    {
        return TypeToStringWorker(t, null);
    }

    internal string TypeToStringWorker(Type t, Node? enclosingDeclaration)
    {
        return TypeToStringExWorker(t, enclosingDeclaration, TypeFormatFlags.AllowUniqueESSymbolType | TypeFormatFlags.UseAliasDefinedOutsideCurrentScope, null);
    }

    internal static NodeBuilderFlags ToNodeBuilderFlags(TypeFormatFlags flags)
    {
        return (NodeBuilderFlags)((uint)flags & (uint)TypeFormatFlags.NodeBuilderFlagsMask);
    }

    public string TypeToStringEx(Type t, Node? enclosingDeclaration, TypeFormatFlags flags, VerbosityContext? vc)
    {
        return TypeToStringExWorker(t, enclosingDeclaration, flags, vc);
    }

    internal string TypeToStringExWorker(Type t, Node? enclosingDeclaration, TypeFormatFlags flags, VerbosityContext? vc)
    {
        // Serialization of types can lead to (lazy) resolution of members, which can cause diagnostics that again require
        // serialization of types. This can potentially result in infinite recursion and stack overflows. To prevent that,
        // after a certain number of recursive invocations the function simply returns "?".
        if (_serializationLevel >= MaxSerializationLevel)
        {
            return "?";
        }

        var newLine = "";
        if ((flags & TypeFormatFlags.MultilineObjectLiterals) != 0)
        {
            newLine = "\n";
        }

        var writer = Printer.NewTextWriter(newLine, 0);
        var noTruncation = ((vc is null || vc.MaxTruncationLength == 0) && _compilerOptions.NoErrorTruncation == Tristate.TSTrue) || (flags & TypeFormatFlags.NoTruncation) != 0;
        var combinedFlags = ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors;
        if (noTruncation)
        {
            combinedFlags = combinedFlags | NodeBuilderFlags.NoTruncation;
        }

        var (nodeBuilder, release) = GetNodeBuilder();
        try
        {
            var oldVerbosity = nodeBuilder._verbosity;
            nodeBuilder._verbosity = vc;
            try
            {
                _serializationLevel++;
                var typeNode = nodeBuilder.TypeToTypeNode(t, enclosingDeclaration, combinedFlags, NodeBuilderInternalFlags.None, null);
                _serializationLevel--;
                if (typeNode is null)
                {
                    throw new DebugFailureException("should always get typenode");
                }

                // The unresolved type gets a synthesized comment on `any` to hint to users that it's not a plain `any`.
                // Otherwise, we always strip comments out.
                Printer p;
                if (ReferenceEquals(t, _unresolvedType))
                {
                    p = CreatePrinterWithDefaults(nodeBuilder.EmitContext());
                }
                else
                {
                    p = CreatePrinterWithRemoveComments(nodeBuilder.EmitContext());
                }

                SourceFile? sourceFile = null;
                if (enclosingDeclaration is not null)
                {
                    sourceFile = GetSourceFileOfNode(enclosingDeclaration);
                }

                p.Write(typeNode, sourceFile, writer, null);
                var result = writer.String();

                var maxLength = DefaultMaximumTruncationLength * 2;
                if (vc is not null && vc.MaxTruncationLength > 0)
                {
                    maxLength = vc.MaxTruncationLength * 10; // hard cutoff matching Strada's absoluteMaximumLength
                }

                if (noTruncation)
                {
                    maxLength = NoTruncationMaximumTruncationLength * 2;
                }

                if (maxLength > 0 && result.Length != 0 && result.Length >= maxLength)
                {
                    vc?.Truncated = true;
                    return result[..(maxLength - 3)] + "...";
                }

                return result;
            }
            finally
            {
                nodeBuilder._verbosity = oldVerbosity;
            }
        }
        finally
        {
            release();
        }
    }

    public string SymbolToString(Symbol s)
    {
        return SymbolToStringWorker(s);
    }

    internal string SymbolToStringWorker(Symbol symbol)
    {
        return SymbolToStringExWorker(symbol, null, SymbolFlags.All, SymbolFormatFlags.AllowAnyNodeKind);
    }

    public string SymbolToStringEx(Symbol symbol, Node? enclosingDeclaration, SymbolFlags meaning, SymbolFormatFlags flags)
    {
        return SymbolToStringExWorker(symbol, enclosingDeclaration, meaning, flags);
    }

    internal string SymbolToStringExWorker(Symbol symbol, Node? enclosingDeclaration, SymbolFlags meaning, SymbolFormatFlags flags)
    {
        var (writer, putWriter) = Printer.GetSingleLineStringWriter();
        try
        {
            var nodeFlags = NodeBuilderFlags.IgnoreErrors;
            var internalNodeFlags = NodeBuilderInternalFlags.None;
            if ((flags & SymbolFormatFlags.UseOnlyExternalAliasing) != 0)
            {
                nodeFlags |= NodeBuilderFlags.UseOnlyExternalAliasing;
            }

            if ((flags & SymbolFormatFlags.WriteTypeParametersOrArguments) != 0)
            {
                nodeFlags |= NodeBuilderFlags.WriteTypeParametersInQualifiedName;
            }

            if ((flags & SymbolFormatFlags.UseAliasDefinedOutsideCurrentScope) != 0)
            {
                nodeFlags |= NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope;
            }

            if ((flags & SymbolFormatFlags.DoNotIncludeSymbolChain) != 0)
            {
                internalNodeFlags |= NodeBuilderInternalFlags.DoNotIncludeSymbolChain;
            }

            if ((flags & SymbolFormatFlags.WriteComputedProps) != 0)
            {
                internalNodeFlags |= NodeBuilderInternalFlags.WriteComputedProps;
            }

            var (nodeBuilder, release) = GetNodeBuilder();
            try
            {
                SourceFile? sourceFile = null;
                if (enclosingDeclaration is not null)
                {
                    sourceFile = GetSourceFileOfNode(enclosingDeclaration);
                }

                Printer printer;
                // add neverAsciiEscape for GH#39027
                if (enclosingDeclaration is not null && enclosingDeclaration.Kind == Kind.SourceFile)
                {
                    printer = CreatePrinterWithRemoveCommentsOmitTrailingSemicolonNeverAsciiEscape(nodeBuilder.EmitContext());
                }
                else
                {
                    printer = CreatePrinterWithRemoveCommentsOmitTrailingSemicolon(nodeBuilder.EmitContext());
                }

                Func<Symbol, SymbolFlags, Node?, NodeBuilderFlags, NodeBuilderInternalFlags, NodeBuilderSymbolTracker?, Node?> builder;
                if ((flags & SymbolFormatFlags.AllowAnyNodeKind) != 0)
                {
                    builder = nodeBuilder.SymbolToNode;
                }
                else
                {
                    builder = nodeBuilder.SymbolToEntityName;
                }

                var entity = builder(symbol, meaning, enclosingDeclaration, nodeFlags, internalNodeFlags, null); // TODO: GH#18217
                printer.Write(entity! /*sourceFile*/, sourceFile, writer, null);                                // TODO: GH#18217
                return writer.String();
            }
            finally
            {
                release();
            }
        }
        finally
        {
            putWriter();
        }
    }

    internal string SignatureToString(Signature signature)
    {
        return SignatureToStringExWorker(signature, null, TypeFormatFlags.None, null);
    }

    public string SignatureToStringEx(Signature signature, Node? enclosingDeclaration, TypeFormatFlags flags, VerbosityContext? vc)
    {
        return SignatureToStringExWorker(signature, enclosingDeclaration, flags, vc);
    }

    internal string SignatureToStringExWorker(Signature signature, Node? enclosingDeclaration, TypeFormatFlags flags, VerbosityContext? vc)
    {
        var isConstructor = (signature._flags & SignatureFlags.Construct) != 0 && (flags & TypeFormatFlags.WriteCallStyleSignature) == 0;
        Kind sigOutput;
        if ((flags & TypeFormatFlags.WriteArrowStyleSignature) != 0)
        {
            if (isConstructor)
            {
                sigOutput = Kind.ConstructorType;
            }
            else
            {
                sigOutput = Kind.FunctionType;
            }
        }
        else
        {
            if (isConstructor)
            {
                sigOutput = Kind.ConstructSignature;
            }
            else
            {
                sigOutput = Kind.CallSignature;
            }
        }

        var (nodeBuilder, release) = GetNodeBuilder();
        try
        {
            var oldVerbosity = nodeBuilder._verbosity;
            nodeBuilder._verbosity = vc;
            try
            {
                var combinedFlags = ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.WriteTypeParametersInQualifiedName;
                var sig = nodeBuilder.SignatureToSignatureDeclaration(signature, sigOutput, enclosingDeclaration, combinedFlags, NodeBuilderInternalFlags.None, null);
                var p = CreatePrinterWithRemoveCommentsOmitTrailingSemicolonNeverAsciiEscape(nodeBuilder.EmitContext());
                SourceFile? sourceFile = null;
                if (enclosingDeclaration is not null)
                {
                    sourceFile = GetSourceFileOfNode(enclosingDeclaration);
                }

                if ((flags & TypeFormatFlags.MultilineObjectLiterals) != 0)
                {
                    var writer = Printer.NewTextWriter("\n", 0);
                    p.Write(sig!, sourceFile, writer, null);
                    return writer.String();
                }

                var (writer2, putWriter) = Printer.GetSingleLineStringWriter();
                try
                {
                    p.Write(sig!, sourceFile, writer2, null);
                    return writer2.String();
                }
                finally
                {
                    putWriter();
                }
            }
            finally
            {
                nodeBuilder._verbosity = oldVerbosity;
            }
        }
        finally
        {
            release();
        }
    }

    internal string TypePredicateToStringWorker(TypePredicate typePredicate)
    {
        return TypePredicateToStringEx(typePredicate, null, TypeFormatFlags.UseAliasDefinedOutsideCurrentScope);
    }

    internal string TypePredicateToStringEx(TypePredicate typePredicate, Node? enclosingDeclaration, TypeFormatFlags flags)
    {
        var (writer, putWriter) = Printer.GetSingleLineStringWriter();
        try
        {
            var (nodeBuilder, release) = GetNodeBuilder();
            try
            {
                var combinedFlags = ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.WriteTypeParametersInQualifiedName;
                var predicate = nodeBuilder.TypePredicateToTypePredicateNode(typePredicate, enclosingDeclaration, combinedFlags, NodeBuilderInternalFlags.None, null); // TODO: GH#18217
                var printer = CreatePrinterWithRemoveComments(nodeBuilder.EmitContext());
                SourceFile? sourceFile = null;
                if (enclosingDeclaration is not null)
                {
                    sourceFile = GetSourceFileOfNode(enclosingDeclaration);
                }

                printer.Write(predicate! /*sourceFile*/, sourceFile, writer, null);
                return writer.String();
            }
            finally
            {
                release();
            }
        }
        finally
        {
            putWriter();
        }
    }

    internal string ValueToStringWorker(object? value)
    {
        return ValueToString(value);
    }

    internal Type[] FormatUnionTypes(Type[] types, bool expandingEnum)
    {
        List<Type>? result = null;
        var flags = TypeFlags.None;
        for (var i = 0; i < types.Length; i++)
        {
            var t = types[i];
            flags |= t._flags;
            if ((t._flags & TypeFlags.Nullable) == 0)
            {
                if ((t._flags & TypeFlags.BooleanLiteral) != 0 || (!expandingEnum && (t._flags & TypeFlags.EnumLike) != 0))
                {
                    Type baseType;
                    if ((t._flags & TypeFlags.BooleanLiteral) != 0)
                    {
                        baseType = _booleanType;
                    }
                    else
                    {
                        baseType = GetBaseTypeOfEnumLikeType(t);
                    }

                    if ((baseType._flags & TypeFlags.Union) != 0)
                    {
                        var count = baseType.AsUnionType()!._types!.Length;
                        if (i + count <= types.Length && ReferenceEquals(GetRegularTypeOfLiteralType(types[i + count - 1]), GetRegularTypeOfLiteralType(baseType.AsUnionType()!._types![count - 1])))
                        {
                            (result ??= new List<Type>()).Add(baseType);
                            i += count - 1;
                            continue;
                        }
                    }
                }

                (result ??= new List<Type>()).Add(t);
            }
        }

        if ((flags & TypeFlags.Null) != 0)
        {
            (result ??= new List<Type>()).Add(_nullType);
        }

        if ((flags & TypeFlags.Undefined) != 0)
        {
            (result ??= new List<Type>()).Add(_undefinedType);
        }

        return result?.ToArray() ?? Array.Empty<Type>();
    }

    public Node? TypeToTypeNode(Type t, Node? enclosingDeclaration, NodeBuilderFlags flags, Dictionary<Node, Symbol>? idToSymbol)
    {
        var nodeBuilder = GetNodeBuilderEx(idToSymbol);
        return nodeBuilder.TypeToTypeNode(t, enclosingDeclaration, flags, NodeBuilderInternalFlags.None, null);
    }

    public Node? SignatureToSignatureDeclaration(Signature signature, Kind kind, Node? enclosingDeclaration, NodeBuilderFlags flags)
    {
        var (nodeBuilder, release) = GetNodeBuilder();
        try
        {
            return nodeBuilder.SignatureToSignatureDeclaration(signature, kind, enclosingDeclaration, flags, NodeBuilderInternalFlags.None, null);
        }
        finally
        {
            release();
        }
    }

    /// <summary>ExpandSymbolForHover produces declaration strings for a symbol with verbosity support for expandable hover.</summary>
    public string ExpandSymbolForHover(Symbol symbol, SymbolFlags meaning, VerbosityContext? vc)
    {
        var (nodeBuilder, release) = GetNodeBuilder();
        try
        {
            var oldVerbosity = nodeBuilder._verbosity;
            nodeBuilder._verbosity = vc;
            try
            {
                var nodes = nodeBuilder.ExpandSymbolForHover(symbol, meaning);
                if (nodes is null || nodes.Length == 0)
                {
                    return "";
                }

                var p = CreatePrinterWithRemoveComments(nodeBuilder.EmitContext());
                SourceFile? sourceFile = null;
                if (symbol.ValueDeclaration is not null)
                {
                    sourceFile = GetSourceFileOfNode(symbol.ValueDeclaration);
                }

                var b = new StringBuilder();
                for (var i = 0; i < nodes.Length; i++)
                {
                    if (i > 0)
                    {
                        b.Append('\n');
                    }

                    b.Append(p.Emit(nodes[i], sourceFile));
                }

                return b.ToString();
            }
            finally
            {
                nodeBuilder._verbosity = oldVerbosity;
            }
        }
        finally
        {
            release();
        }
    }

    /// <summary>TypeParameterToStringEx renders a type parameter declaration (e.g. "T extends Foo") with optional verbosity support.</summary>
    public string TypeParameterToStringEx(Type t, Node? enclosingDeclaration, VerbosityContext? vc)
    {
        var (nodeBuilder, release) = GetNodeBuilder();
        try
        {
            var oldVerbosity = nodeBuilder._verbosity;
            nodeBuilder._verbosity = vc;
            try
            {
                var typeParamNode = nodeBuilder.TypeParameterToDeclaration(t, enclosingDeclaration, NodeBuilderFlags.IgnoreErrors, NodeBuilderInternalFlags.None, null);
                if (typeParamNode is null)
                {
                    return TypeToString(t);
                }

                var p = CreatePrinterWithRemoveComments(nodeBuilder.EmitContext());
                SourceFile? sourceFile = null;
                if (enclosingDeclaration is not null)
                {
                    sourceFile = GetSourceFileOfNode(enclosingDeclaration);
                }

                return p.Emit(typeParamNode, sourceFile);
            }
            finally
            {
                nodeBuilder._verbosity = oldVerbosity;
            }
        }
        finally
        {
            release();
        }
    }

    public Node? TypeToTypeNodeEx(Type t, Node? enclosingDeclaration, NodeBuilderFlags flags, NodeBuilderInternalFlags internalFlags, Dictionary<Node, Symbol>? idToSymbol)
    {
        var nodeBuilder = GetNodeBuilderEx(idToSymbol);
        return nodeBuilder.TypeToTypeNode(t, enclosingDeclaration, flags, internalFlags, null);
    }

    public Node TypePredicateToTypePredicateNode(TypePredicate t, Node? enclosingDeclaration, NodeBuilderFlags flags, Dictionary<Node, Symbol>? idToSymbol)
    {
        var nodeBuilder = GetNodeBuilderEx(idToSymbol);
        return nodeBuilder.TypePredicateToTypePredicateNode(t, enclosingDeclaration, flags, NodeBuilderInternalFlags.None, null)!;
    }
}

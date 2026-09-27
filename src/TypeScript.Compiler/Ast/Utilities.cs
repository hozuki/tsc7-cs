// ported from: internal/ast/utilities.go（只收录本轮 Ast 所需的几个小函数）
//
// 说明：utilities.go 有 4631 行，绝大部分属于后续里程碑（binder/checker/printer 的调用点）。本轮只移植
// 手写 Ast 核心**引用到**的函数，其余留给下一轮：
//   * ModifierToFlag / ModifiersToFlags / HasSyntacticModifier / HasAccessorModifier / HasStaticModifier
//     → 见 ModifierFlags.cs（modifierflags.go 那一步一起移植，Go 里实为 utilities.go:990-1046）
//   * IsAmbientModuleSymbolName → 见 Symbol.cs（Go utilities.go:1662）
//   * GetNodeId / GetSymbolId → 见本文件（Go utilities.go:22-45；Node/_id 与 Symbol/_id 的分配点）
//   * GetImpliedNodeFormatForEmitWorker → 见本文件（Go utilities.go:2622；parseoptions.go 依赖它）
//   * IsClassElement / IsMethodOrAccessor / IsPrivateIdentifierClassElementDeclaration / IsThisIdentifier /
//     IsJsxOpeningLikeElement → 见本文件（Go utilities.go:539/554/562/1367/3760）
// 未移植（下一轮）：FindAncestor/GetRootDeclaration/ContainsObjectRestOrSpread/GetElementsOfBindingOrAssignmentPattern
// 等——它们牵出成片的 utilities.go。
//
// 与 Go 的差异：Go 的未导出包级函数在 C# 里是 `Ast` 上的 internal static（PascalCase）。
using System.Threading;
using TypeScript.Core;

namespace TypeScript.Ast;

public static partial class Ast
{
    // Go: var nextNodeId / nextSymbolId atomic.Uint64
    private static ulong _nextNodeId;
    private static ulong _nextSymbolId;

    /// <summary>Go: <c>func GetNodeId(node *Node) NodeId</c>（懒分配：0 表示未分配）。</summary>
    public static NodeId GetNodeId(Node node)
    {
        var id = Volatile.Read(ref node._id);
        if (id == 0)
        {
            // Worst case, we burn a few ids if we have to CAS.
            id = Interlocked.Increment(ref _nextNodeId);
            if (Interlocked.CompareExchange(ref node._id, id, 0) != 0)
            {
                id = Volatile.Read(ref node._id);
            }
        }

        return new NodeId(id);
    }

    /// <summary>Go: <c>func GetSymbolId(symbol *Symbol) SymbolId</c>。</summary>
    public static SymbolId GetSymbolId(Symbol symbol)
    {
        var id = Volatile.Read(ref symbol._id);
        if (id == 0)
        {
            id = Interlocked.Increment(ref _nextSymbolId);
            if (Interlocked.CompareExchange(ref symbol._id, id, 0) != 0)
            {
                id = Volatile.Read(ref symbol._id);
            }
        }

        return new SymbolId(id);
    }

    /// <summary>Go: <c>func GetImpliedNodeFormatForEmitWorker(fileName string, emitModuleKind core.ModuleKind, sourceFileMetaData SourceFileMetaData) core.ResolutionMode</c>。</summary>
    internal static ModuleKind GetImpliedNodeFormatForEmitWorker(
        string fileName,
        ModuleKind emitModuleKind,
        SourceFileMetaData sourceFileMetaData)
    {
        if (ModuleKind.Node16 <= emitModuleKind && emitModuleKind <= ModuleKind.NodeNext)
        {
            return sourceFileMetaData.ImpliedNodeFormat;
        }

        if (sourceFileMetaData.ImpliedNodeFormat == ModuleKind.CommonJS &&
            (sourceFileMetaData.PackageJsonType == "commonjs" ||
                TSPath.TSPath.FileExtensionIsOneOf(fileName, [TSPath.TSPath.ExtensionCjs, TSPath.TSPath.ExtensionCts])))
        {
            return ModuleKind.CommonJS;
        }

        if (sourceFileMetaData.ImpliedNodeFormat == ModuleKind.ESNext &&
            (sourceFileMetaData.PackageJsonType == "module" ||
                TSPath.TSPath.FileExtensionIsOneOf(fileName, [TSPath.TSPath.ExtensionMjs, TSPath.TSPath.ExtensionMts])))
        {
            return ModuleKind.ESNext;
        }

        return ModuleKind.None;
    }

    /// <summary>Go: <c>func IsClassElement(node *Node) bool</c>。</summary>
    public static bool IsClassElement(Node node) => node.Kind is
        Kind.Constructor or
        Kind.PropertyDeclaration or
        Kind.MethodDeclaration or
        Kind.GetAccessor or
        Kind.SetAccessor or
        Kind.IndexSignature or
        Kind.ClassStaticBlockDeclaration or
        Kind.SemicolonClassElement;

    /// <summary>Go: <c>func IsMethodOrAccessor(node *Node) bool</c>。</summary>
    public static bool IsMethodOrAccessor(Node node) =>
        node.Kind is Kind.MethodDeclaration or Kind.GetAccessor or Kind.SetAccessor;

    /// <summary>Go: <c>func IsPrivateIdentifierClassElementDeclaration(node *Node) bool</c>。</summary>
    public static bool IsPrivateIdentifierClassElementDeclaration(Node node) =>
        (IsPropertyDeclaration(node) || IsMethodOrAccessor(node)) && IsPrivateIdentifier(node.Name()!);

    /// <summary>Go: <c>func IsThisIdentifier(node *Node) bool</c>。</summary>
    public static bool IsThisIdentifier(Node node) => IsIdentifier(node) && node.Text() == "this";

    /// <summary>Go: <c>func IsJsxOpeningLikeElement(node *Node) bool</c>。</summary>
    public static bool IsJsxOpeningLikeElement(Node node) => IsJsxOpeningElement(node) || IsJsxSelfClosingElement(node);
}

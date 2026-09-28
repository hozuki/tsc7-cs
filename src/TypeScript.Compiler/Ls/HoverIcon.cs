// ported from: internal/ls/hovericon.go:1-156
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级未导出常量 `vsImageCatalogGuid` / `imageIdXxx` → 静态类 `Ls` 的 `internal const` 字段（Go 未导出的
//    **字段/常量**带 `_` 前缀，方法不加）：`_vsImageCatalogGuid` / `_imageIdWarning` … `_imageIdConstantPublic`。
// 2) 包级未导出函数 `newVSImageId` / `getVSHoverImageId` / `buildVSHoverRawContent` → `Ls` 的 `internal static`
//    `NewVSImageId` / `GetVSHoverImageId` / `BuildVSHoverRawContent`（PascalCase）。
// 3) `len(quickInfoRuns) == 0` → `quickInfoRuns is null || quickInfoRuns.Length == 0`（Go 遍历 nil 切片不迭代；
//    `documentationRuns` 参数保留可空语义 ⇒ `[]` / nil 都当"空"）。
// 4) Go 的复合字面量 `[]lsproto.VSImageElementOrClassifiedTextElementOrContainerElement{{ImageElement: …}, …}`
//    → C# 集合表达式（目标类型是 union 元素数组）。
// 5) 局部闭包 `pick(private, protected, public int32)` → 局部函数 `Pick(int privateId, int protectedId, int publicId)`
//    （`public` 是 C# 关键字，形参改名；调用点一一对应）。
using TypeScript.LsProto;
using TypeScript.LsUtil;

namespace TypeScript.Ls;

public static partial class Ls
{
    /// <summary>
    /// vsImageCatalogGuid is the GUID of the shared VS image catalog (see
    /// Microsoft.VisualStudio.Imaging.KnownImageIds), mirroring the constant duplicated in
    /// TypeScript-VS's ImageIdMapping.cs (which avoids taking an assembly reference just for this GUID).
    /// </summary>
    internal const string _vsImageCatalogGuid = "ae27a6b0-e345-4288-96df-5eaf394ee369";

    // Known image IDs from Microsoft.VisualStudio.Imaging.KnownImageIds, restricted to the subset
    // consumed by TypeScript-VS's ImageIdMapping.cs for hover tooltips. Corsa (this LSP server) has no
    // TSServer/Roslyn dependency to reuse that mapping from, so the values are duplicated here.
    internal const int _imageIdWarning = 0x00000637;
    internal const int _imageIdKeyword = 0x00000635;
    internal const int _imageIdModulePrivate = 0x0000077D;
    internal const int _imageIdModuleProtected = 0x0000077E;
    internal const int _imageIdModulePublic = 0x0000077F;
    internal const int _imageIdType = 0x00000CA1;
    internal const int _imageIdNamespace = 0x0000079F;
    internal const int _imageIdClassPrivate = 0x000001D7;
    internal const int _imageIdClassProtected = 0x000001D8;
    internal const int _imageIdClassPublic = 0x000001D9;
    internal const int _imageIdInterfacePrivate = 0x00000646;
    internal const int _imageIdInterfaceProtected = 0x00000647;
    internal const int _imageIdInterfacePublic = 0x00000648;
    internal const int _imageIdEnumPrivate = 0x00000469;
    internal const int _imageIdEnumProtected = 0x0000046A;
    internal const int _imageIdEnumPublic = 0x0000046B;
    internal const int _imageIdEnumMember = 0x00000465;
    internal const int _imageIdLocalVariable = 0x000006D3;
    internal const int _imageIdPropertyPrivate = 0x00000982;
    internal const int _imageIdPropertyProtected = 0x00000983;
    internal const int _imageIdPropertyPublic = 0x00000984;
    internal const int _imageIdMethodPrivate = 0x00000756;
    internal const int _imageIdMethodProtected = 0x00000757;
    internal const int _imageIdMethodPublic = 0x00000758;
    internal const int _imageIdLabel = 0x0000067D;
    internal const int _imageIdAssembly = 0x000000C4;
    internal const int _imageIdConstantPrivate = 0x0000026A;
    internal const int _imageIdConstantProtected = 0x0000026B;
    internal const int _imageIdConstantPublic = 0x0000026C;

    internal static VSImageId NewVSImageId(int id)
    {
        return new VSImageId { Guid = _vsImageCatalogGuid, Id = id };
    }

    /// <summary>
    /// getVSHoverImageId maps a symbol's ScriptElementKind/modifiers to the VS image shown next to the
    /// symbol name in hover tooltips. This mirrors TypeScript-VS's ImageIdMapping.GetImageId, which the
    /// legacy (TSServer-backed) hover path uses; Corsa has no TSServer to source that mapping from, so
    /// the LSP hover response must carry the equivalent icon directly.
    /// </summary>
    internal static VSImageId GetVSHoverImageId(ScriptElementKind kind, ScriptElementKindModifier modifiers)
    {
        var isPrivate = (modifiers & ScriptElementKindModifier.Private) != 0;
        var isProtected = (modifiers & ScriptElementKindModifier.Protected) != 0;

        // No internal/exported arm: the *Internal VS icons carry a chevron overlay that conveys
        // C# assembly-scoped visibility, a concept that doesn't apply to TypeScript.
        VSImageId Pick(int privateId, int protectedId, int publicId)
        {
            if (isPrivate)
            {
                return NewVSImageId(privateId);
            }
            if (isProtected)
            {
                return NewVSImageId(protectedId);
            }
            return NewVSImageId(publicId);
        }

        switch (kind)
        {
            case ScriptElementKind.Warning:
                return NewVSImageId(_imageIdWarning);
            case ScriptElementKind.Keyword:
                return NewVSImageId(_imageIdKeyword);
            case ScriptElementKind.ScriptElement:
                return Pick(_imageIdModulePrivate, _imageIdModuleProtected, _imageIdModulePublic);
            case ScriptElementKind.PrimitiveType:
                return NewVSImageId(_imageIdType);
            case ScriptElementKind.ModuleElement:
                return NewVSImageId(_imageIdNamespace);
            case ScriptElementKind.ConstructorImplementationElement:
            case ScriptElementKind.ClassElement:
            case ScriptElementKind.LocalClassElement:
            case ScriptElementKind.TypeElement:
                return Pick(_imageIdClassPrivate, _imageIdClassProtected, _imageIdClassPublic);
            case ScriptElementKind.InterfaceElement:
                return Pick(_imageIdInterfacePrivate, _imageIdInterfaceProtected, _imageIdInterfacePublic);
            case ScriptElementKind.EnumElement:
                return Pick(_imageIdEnumPrivate, _imageIdEnumProtected, _imageIdEnumPublic);
            case ScriptElementKind.EnumMemberElement:
                return NewVSImageId(_imageIdEnumMember);
            case ScriptElementKind.ParameterElement:
            case ScriptElementKind.VariableElement:
            case ScriptElementKind.LocalVariableElement:
            case ScriptElementKind.VariableUsingElement:
            case ScriptElementKind.VariableAwaitUsingElement:
            case ScriptElementKind.LetElement:
            case ScriptElementKind.String:
                return NewVSImageId(_imageIdLocalVariable);
            case ScriptElementKind.ConstElement:
                return Pick(_imageIdConstantPrivate, _imageIdConstantProtected, _imageIdConstantPublic);
            case ScriptElementKind.MemberGetAccessorElement:
            case ScriptElementKind.MemberSetAccessorElement:
            case ScriptElementKind.MemberVariableElement:
            case ScriptElementKind.MemberAccessorVariableElement:
                return Pick(_imageIdPropertyPrivate, _imageIdPropertyProtected, _imageIdPropertyPublic);
            case ScriptElementKind.FunctionElement:
            case ScriptElementKind.LocalFunctionElement:
            case ScriptElementKind.MemberFunctionElement:
            case ScriptElementKind.CallSignatureElement:
            case ScriptElementKind.IndexSignatureElement:
            case ScriptElementKind.ConstructSignatureElement:
                return Pick(_imageIdMethodPrivate, _imageIdMethodProtected, _imageIdMethodPublic);
            case ScriptElementKind.TypeParameterElement:
                return NewVSImageId(_imageIdType);
            case ScriptElementKind.Label:
                return NewVSImageId(_imageIdLabel);
            case ScriptElementKind.Alias:
                return NewVSImageId(_imageIdModulePublic);
            default:
                return NewVSImageId(_imageIdAssembly);
        }
    }

    /// <summary>
    /// buildVSHoverRawContent assembles the VS-specific rich hover content (symbol icon + colorized
    /// declaration line, plus an optional colorized documentation block) matching the shape that
    /// TypeScript-VS's legacy HoverService.cs builds from TSServer's quickinfo-full response
    /// (ImageElement + ClassifiedTextElement wrapped in a ContainerElement).
    /// </summary>
    internal static VSContainerElement? BuildVSHoverRawContent(
        VSImageId imageId,
        VSClassifiedTextRun[]? quickInfoRuns,
        VSClassifiedTextRun[]? documentationRuns)
    {
        if (quickInfoRuns is null || quickInfoRuns.Length == 0)
        {
            return null;
        }

        var displayLine = new VSContainerElement
        {
            Style = VSContainerElementStyle.Wrapped,
            Elements =
            [
                new VSImageElementOrClassifiedTextElementOrContainerElement { ImageElement = new VSImageElement { ImageId = imageId } },
                new VSImageElementOrClassifiedTextElementOrContainerElement { ClassifiedTextElement = new VSClassifiedTextElement { Runs = quickInfoRuns } },
            ],
        };

        if (documentationRuns is null || documentationRuns.Length == 0)
        {
            return displayLine;
        }

        return new VSContainerElement
        {
            Style = VSContainerElementStyle.Stacked,
            Elements =
            [
                new VSImageElementOrClassifiedTextElementOrContainerElement { ContainerElement = displayLine },
                new VSImageElementOrClassifiedTextElementOrContainerElement { ClassifiedTextElement = new VSClassifiedTextElement { Runs = documentationRuns } },
            ],
        };
    }
}

// ported from: internal/api/encoder/encoder.go:1-951
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 错误通道：Go 的 `(T, error)` 在本仓一律走异常，`encodeTree` 的 error 返回值因此消失。
//    api 的调用点全部丢弃 `NodeIndexTable`（Go 写 `data, _, err := …`），所以对外的
//    `EncodeSourceFile`/`EncodeNode` 只返回 `byte[]`；需要表时用 `GetNodeIndexTable`——
//    `EncodeSourceFile` 会把表写进 SourceFile 的 data cell（这一点与 Go 完全一致）。
//    内部保留 Go 的二元形状：`internal static (byte[] Data, NodeIndexTable Table) EncodeTree(…)`。
// 2) Go 的 `[]byte` + `appendUint32s`/下标回填 → `ByteBuffer`（可增长字节缓冲，定义在本文件底部）。
//    Go 的 `buf *[]byte`（原地 append）→ 直接传 `ByteBuffer`（C# 的 class 引用语义 = Go 的指针语义）。
//    `appendUint32s(buf, values...)` → `AppendUint32s(buf, ReadOnlySpan<uint>)`：C# 的 `params uint[]`
//    每次调用都要分配数组，而这里是每个节点都要走的路径。
// 3) `binary.LittleEndian.PutUint32/PutUint64/AppendUint32` → `BinaryPrimitives.WriteUInt32/64LittleEndian`。
// 4) `slices.Concat(headerBytes, strsBytes, extendedData, structuredData, nodes)` → 按总长度分配一次后逐段拷贝
//    （分配次数与 Go 相同；只是不再单独分配 Go 那个 headerBytes 中间数组）。
// 5) `xxh3.Uint128{Lo, Hi}` → `System.UInt128`（低 64 位 = Lo、高 64 位 = Hi，见 Xxh3/Xxh3.cs 文件头）：
//    `uint32(hash.Lo)` → `(uint)hash`、`uint32(hash.Lo>>32)` → `(uint)(hash >> 32)`、`uint32(hash.Hi)` → `(uint)(hash >> 64)`、
//    `uint32(hash.Hi>>32)` → `(uint)(hash >> 96)`。
// 6) `internal/spanmap` → `TypeScript.SpanMap`（用 `SpanMapType`/`Feature` 别名，因为 `SpanMap` 同时是命名空间名）。
// 7) `ast.KindXxx` → `Kind.Xxx`（C# 枚举成员去掉 `Kind` 前缀）；`uint32(node.Kind)` → `(uint)node.Kind`（C# 的 Kind : short）。
// 8) `ast.NewSourceFileDataKey[*NodeIndexTable]()` → `Ast.NewSourceFileDataKey<NodeIndexTable>()`；
//    包级变量 `nodeIndexTableKey` → private static readonly 字段 `NodeIndexTableKey`。
// 9) `sync.Once`（NodeIndexTable.sortedOnce）→ 标志 + Lock 双检（本仓约定，先例 Project/SnapshotHost.cs）。
// 10) `SyntaxKindNodeList uint32 = 1<<32 - 1` → C# 必须写字面量 `0xFFFF_FFFFu`：C# 的移位计数按类型宽度取模，
//     `1u << 32` 会得到 1（Go 的无类型常量 `1<<32` 不会）。
// 11) 位置语义：C# 的 `Node.Pos()/End()` 已经是 UTF-16 代码单元下标（本仓 PositionMap 是恒等映射），
//     Go 的 `positionMap.UTF8ToUTF16(pos)` 在 C# 是恒等调用——保留调用点形状，不做额外换算。
// 12) `string(sf.Path())` → `sf.Path().Value`（Go 的 Path 是 string 的新类型；C# 是 record struct，见 TSPath/Path.cs）。
// 13) Go 的 ~150 行「Source File Binary Format」规范注释按"语义完整、排版压缩"移植到 <see cref="Encoder"/> 的类注释里
//     （逐节点类型的 6 位 commonData 定义见 Encoder.Generated.cs）；这只是注释压缩，不改变任何行为。
// 14) `panic(…)` → `throw new DebugFailureException(…)`；`func init()` 的 6 位容量检查 → 静态构造函数。
// 15) nil 兜底：Go 里 `var positionMap *ast.PositionMap` 之后统一 `if positionMap == nil { … }`，
//     C# 写成 `sourceFile?.GetPositionMap() ?? ComputePositionMap("")`（形状不同、语义相同）。
// 16) Go 的 `map[*ast.Node]uint32` → `Dictionary<Node, uint>`（Node 不覆写 Equals/GetHashCode = 引用相等，
//     与 Go 的指针键一致）；Go 从 nil map 读键返回零值，C# 用 `GetValueOrDefault` 保持同一语义。
using System.Buffers.Binary;
using System.Globalization;
using System.Threading;
using TypeScript.Ast;
using TypeScript.Core;
using Feature = TypeScript.SpanMap.Feature;
using SpanMapType = TypeScript.SpanMap.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

namespace TypeScript.Api.Encoder;

/// <summary>
/// Go: package <c>encoder</c> 的编码侧（<c>internal/api/encoder/encoder.go</c> + <c>encoder_generated.go</c>）。
/// <para>
/// Source File Binary Format（Go encoder.go:72-295 的规范注释，压缩排版；所有整数为小端）：
/// </para>
/// <para>
/// 七个段：Header（64 字节）、String offsets（每串 8 字节的 start/end 字节偏移对）、String data（UTF-8，
/// JS 字符串里的孤立代理项用 WTF-8）、Extended node data、Structured data（msgpack 元数据）、Nodes（每节点 28 字节）。
/// </para>
/// <para>
/// Header：0 协议版本 u8、1-3 保留、4-19 内容哈希 u128（xxh3, LE）、20-23 parse options（bit0 JSX、bit1 Force）、
/// 24-27 string offsets 偏移、28-31 string data 偏移、32-35 extended data 偏移、36-39 structured data 偏移、
/// 40-43 nodes 偏移、44-51 source file id（0 = none）、52-59 source file lease id（0 = none）、60-63 binder data 偏移（0 = none）。
/// </para>
/// <para>
/// Extended node data：TemplateHead/TemplateMiddle/TemplateTail 为 `text 索引 u32, rawText 索引 u32, templateFlags u32`；
/// SourceFile 为 19 个 u32：text 索引、fileName 索引、path 索引、languageVariant、scriptKind、referencedFiles 偏移、
/// typeReferenceDirectives 偏移、libReferenceDirectives 偏移、imports 节点索引数组偏移、moduleAugmentations 偏移、
/// ambientModuleNames 字符串数组偏移、externalModuleIndicator 节点索引（0 = nil）、originalText 索引、spanMap 偏移、
/// supplementalSourceFileNames 偏移、canonicalSourceFileName 索引或 noStructuredData、contentMapper 或 noStructuredData、
/// virtualFileName 或 noStructuredData、diagnosticDirectives 偏移。
/// </para>
/// <para>
/// Structured data：msgpack。文件引用数组的元组为
/// `[pos: uint, end: uint, fileName: string, resolutionMode: uint, preserve: bool]`；节点索引数组（imports、
/// moduleAugmentations）是 uint 数组；字符串数组是 string 数组；span map 是
/// `[virtualStart, virtualLength, originalStart, originalLength, kind, features?]`；diagnostic directives 是
/// `[originalStart, originalLength, virtualStart, virtualLength, policy, unusedCode]`。偏移 0xFFFFFFFF 表示"无数据"。
/// </para>
/// <para>
/// Nodes：按源码顺序的扁平数组，每节点 `kind u32, pos u32, end u32, next 兄弟索引 u32, parent 索引 u32, data, flags u32`；
/// 前 28 字节全零代表 nil 节点。NodeList 用 `kind = 0xFFFF_FFFF` 表示，其 `data` 是列表长度、`flags` 的 bit0 是
/// `HasTrailingComma`。其余节点的 `data` 低 2 位是数据类型（0b00 children：低 8 位是子属性掩码；
/// 0b01 string：低 24 位是字符串偏移表索引；0b10 extended：低 24 位是 extended data 段字节偏移），
/// 高 6 位（bit 24-29）是节点特有的小值（布尔标志，一元表达式则是操作符 SyntaxKind）。
/// </para>
/// </summary>
public static partial class Encoder
{
    /// <summary>Go: <c>func init()</c>（encoder.go:16-20）：断言 <c>KindLastUnaryOperator</c> 能塞进 6 位 commonData。</summary>
    static Encoder()
    {
        if ((short)Kind.LastUnaryOperator > 0x3f)
        {
            throw new DebugFailureException(
                $"KindLastUnaryOperator ({(short)Kind.LastUnaryOperator}) exceeds the 6-bit commonData capacity (max 63)");
        }
    }

    // ── 节点段布局（encoder.go:22-32）──

    /// <summary>Go: <c>NodeOffsetKind</c>（encoder.go:23）。</summary>
    public const uint NodeOffsetKind = 0;

    /// <summary>Go: <c>NodeOffsetPos</c>（encoder.go:24）。</summary>
    public const uint NodeOffsetPos = 4;

    /// <summary>Go: <c>NodeOffsetEnd</c>（encoder.go:25）。</summary>
    public const uint NodeOffsetEnd = 8;

    /// <summary>Go: <c>NodeOffsetNext</c>（encoder.go:26）。</summary>
    public const uint NodeOffsetNext = 12;

    /// <summary>Go: <c>NodeOffsetParent</c>（encoder.go:27）。</summary>
    public const uint NodeOffsetParent = 16;

    /// <summary>Go: <c>NodeOffsetData</c>（encoder.go:28）。</summary>
    public const uint NodeOffsetData = 20;

    /// <summary>Go: <c>NodeOffsetFlags</c>（encoder.go:29）。</summary>
    public const uint NodeOffsetFlags = 24;

    /// <summary>Go: <c>NodeSize</c>（encoder.go:30-31）：单个编码节点的字节数。</summary>
    public const uint NodeSize = 28;

    // ── 节点 data 字段的数据类型（encoder.go:34-38）──

    /// <summary>Go: <c>NodeDataTypeChildren</c>（encoder.go:35）：data 的 bit 0-5 是子属性掩码。</summary>
    public const uint NodeDataTypeChildren = 0u << 30;

    /// <summary>Go: <c>NodeDataTypeString</c>（encoder.go:36）：data 的低 24 位是字符串偏移表索引。</summary>
    public const uint NodeDataTypeString = 1u << 30;

    /// <summary>Go: <c>NodeDataTypeExtendedData</c>（encoder.go:37）。</summary>
    public const uint NodeDataTypeExtendedData = 2u << 30;

    /// <summary>Go: <c>NodeDataTypeMask</c>（encoder.go:41）。</summary>
    public const uint NodeDataTypeMask = 0xc000_0000;

    /// <summary>Go: <c>NodeDataChildMask</c>（encoder.go:42）。</summary>
    public const uint NodeDataChildMask = 0x0000_00ff;

    /// <summary>Go: <c>NodeDataStringIndexMask</c>（encoder.go:43）。</summary>
    public const uint NodeDataStringIndexMask = 0x00ff_ffff;

    /// <summary>Go: <c>SyntaxKindNodeList</c>（encoder.go:46-48）：NodeList 节点的 kind 值。
    /// 偏离：Go 的无类型常量 <c>1&lt;&lt;32 - 1</c> 在 C# 里必须写字面量（见文件头 10）。</summary>
    public const uint SyntaxKindNodeList = 0xffff_ffff;

    // ── 头部布局（encoder.go:50-68）──

    /// <summary>Go: <c>HeaderOffsetMetadata</c>（encoder.go:51）。</summary>
    public const uint HeaderOffsetMetadata = 0;

    /// <summary>Go: <c>HeaderOffsetHashLo0</c>（encoder.go:52）。</summary>
    public const uint HeaderOffsetHashLo0 = 4;

    /// <summary>Go: <c>HeaderOffsetHashLo1</c>（encoder.go:53）。</summary>
    public const uint HeaderOffsetHashLo1 = 8;

    /// <summary>Go: <c>HeaderOffsetHashHi0</c>（encoder.go:54）。</summary>
    public const uint HeaderOffsetHashHi0 = 12;

    /// <summary>Go: <c>HeaderOffsetHashHi1</c>（encoder.go:55）。</summary>
    public const uint HeaderOffsetHashHi1 = 16;

    /// <summary>Go: <c>HeaderOffsetParseOptions</c>（encoder.go:56）。</summary>
    public const uint HeaderOffsetParseOptions = 20;

    /// <summary>Go: <c>HeaderOffsetStringOffsets</c>（encoder.go:57）。</summary>
    public const uint HeaderOffsetStringOffsets = 24;

    /// <summary>Go: <c>HeaderOffsetStringData</c>（encoder.go:58）。</summary>
    public const uint HeaderOffsetStringData = 28;

    /// <summary>Go: <c>HeaderOffsetExtendedData</c>（encoder.go:59）。</summary>
    public const uint HeaderOffsetExtendedData = 32;

    /// <summary>Go: <c>HeaderOffsetStructuredData</c>（encoder.go:60）。</summary>
    public const uint HeaderOffsetStructuredData = 36;

    /// <summary>Go: <c>HeaderOffsetNodes</c>（encoder.go:61）。</summary>
    public const uint HeaderOffsetNodes = 40;

    /// <summary>Go: <c>HeaderOffsetSourceFileID</c>（encoder.go:62）。</summary>
    public const uint HeaderOffsetSourceFileID = 44;

    /// <summary>Go: <c>HeaderOffsetSourceFileLease</c>（encoder.go:64）。</summary>
    public const uint HeaderOffsetSourceFileLease = 52;

    /// <summary>Go: <c>HeaderOffsetBinderData</c>（encoder.go:66）。</summary>
    public const uint HeaderOffsetBinderData = 60;

    /// <summary>Go: <c>HeaderSize</c>（encoder.go:67）。</summary>
    public const uint HeaderSize = 64;

    /// <summary>Go: <c>ProtocolVersion uint8 = 9</c>（encoder.go:70）。</summary>
    public const byte ProtocolVersion = 9;

    /// <summary>Go: <c>const noStructuredData = 0xFFFFFFFF</c>（encoder.go:676）：结构化数据段里"无数据"的哨兵偏移。</summary>
    internal const uint NoStructuredData = 0xffff_ffff;

    /// <summary>Go: <c>func SourceFileHash(sourceFile *ast.SourceFile) string</c>（encoder.go:312-315）：内容的 128 位哈希（十六进制）。</summary>
    public static string SourceFileHash(SourceFile sourceFile)
    {
        var h = sourceFile.Hash;
        return ((ulong)(h >> 64)).ToString("x16", CultureInfo.InvariantCulture)
            + ((ulong)h).ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>Go: <c>func encodeParseOptions(opts ast.ExternalModuleIndicatorOptions) uint32</c>（encoder.go:318-327）。</summary>
    internal static uint EncodeParseOptions(ExternalModuleIndicatorOptions opts)
    {
        var bits = 0u;
        if (opts.JSX)
        {
            bits |= 1;
        }

        if (opts.Force)
        {
            bits |= 2;
        }

        return bits;
    }

    // Go 的 NodeIndexTable 是**包级**类型 → C# 落成同命名空间的顶层类（见本文件末尾的 NodeIndexTable）。

    /// <summary>Go: <c>var nodeIndexTableKey = ast.NewSourceFileDataKey[*NodeIndexTable]()</c>（encoder.go:336）。</summary>
    internal static readonly SourceFileDataKey<NodeIndexTable> NodeIndexTableKey = NewSourceFileDataKey<NodeIndexTable>();

    /// <summary>Go: <c>func BuildNodeIndexTable(sourceFile *ast.SourceFile) *NodeIndexTable</c>（encoder.go:370-414）。</summary>
    internal static NodeIndexTable BuildNodeIndexTable(SourceFile sourceFile)
    {
        var nodeCount = 0u;
        var nodeTable = new List<Node?>(sourceFile.NodeCount + 1) { null }; // index 0 = nil sentinel

        NodeVisitor visitor = null!;
        visitor = new NodeVisitor(
            visit: node =>
            {
                nodeCount++;
                nodeTable.Add(node);
                visitor.VisitEachChild(node);
                foreach (var jsdoc in node.JSDoc(sourceFile) ?? [])
                {
                    visitor.Visit!(jsdoc);
                }

                return node;
            },
            hooks: new NodeVisitorHooks
            {
                VisitNodes = (nodeList, v) =>
                {
                    if (nodeList is null)
                    {
                        return nodeList;
                    }

                    nodeCount++;
                    nodeTable.Add(null); // NodeLists are not *ast.Node
                    v.VisitSlice(nodeList.Nodes);
                    return nodeList;
                },
                VisitModifiers = (modifiers, v) =>
                {
                    if (modifiers is not null && modifiers.Nodes is { Length: > 0 })
                    {
                        v.Hooks.VisitNodes!(modifiers, v);
                    }

                    return modifiers;
                },
            });

        var rootNode = sourceFile.AsNode();
        // Index 1 = root node (matches encodeTree)
        nodeCount++;
        nodeTable.Add(rootNode);

        visitor.VisitEachChild(rootNode);
        foreach (var jsdoc in rootNode.JSDoc(sourceFile) ?? [])
        {
            visitor.Visit!(jsdoc);
        }

        return new NodeIndexTable { Nodes = nodeTable.ToArray() };
    }

    /// <summary>Go: <c>func GetNodeIndexTable(sourceFile *ast.SourceFile) *NodeIndexTable</c>（encoder.go:416-418）。</summary>
    public static NodeIndexTable GetNodeIndexTable(SourceFile sourceFile) =>
        sourceFile.GetOrComputeData(NodeIndexTableKey, BuildNodeIndexTable);

    /// <summary>
    /// Go: <c>func EncodeSourceFile(sourceFile *ast.SourceFile) ([]byte, *NodeIndexTable, error)</c>（encoder.go:422-431）。
    /// 偏离：返回值收成 <c>byte[]</c>（见文件头 1）；表仍写进 SourceFile 的 data cell，后续 <see cref="GetNodeIndexTable"/> 拿到同一个实例。
    /// </summary>
    public static byte[] EncodeSourceFile(SourceFile sourceFile)
    {
        var (data, nodeTable) = EncodeTree(sourceFile.AsNode(), sourceFile);
        _ = sourceFile.GetOrComputeData(NodeIndexTableKey, _ => nodeTable);
        return data;
    }

    /// <summary>Go: <c>func SetSourceFileLease(data []byte, lease uint64)</c>（encoder.go:434-436）：就地写入会话级 lease ID。</summary>
    public static void SetSourceFileLease(byte[] data, ulong lease) =>
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan((int)HeaderOffsetSourceFileLease), lease);

    /// <summary>
    /// Go: <c>func EncodeNode(node *ast.Node, sourceFile *ast.SourceFile) ([]byte, *NodeIndexTable, error)</c>（encoder.go:442-444）。
    /// <paramref name="sourceFile"/> 为 null 时（Go 的 nil）只用于字符串表/位置映射的兜底，见 <see cref="EncodeTree"/>。
    /// </summary>
    public static byte[] EncodeNode(Node node, SourceFile? sourceFile) => EncodeTree(node, sourceFile).Data;

    /// <summary>Go: <c>func encodeTree(rootNode *ast.Node, sourceFile *ast.SourceFile) ([]byte, *NodeIndexTable, error)</c>（encoder.go:446-653）。</summary>
    internal static (byte[] Data, NodeIndexTable Table) EncodeTree(Node rootNode, SourceFile? sourceFile)
    {
        var parentIndex = 0u;
        var nodeCount = 0u;
        var prevIndex = 0u;
        var extendedData = new ByteBuffer(0);
        var structuredData = new ByteBuffer(0);

        StringTable strs;
        PositionMap positionMap;
        if (rootNode.Kind == Kind.SourceFile)
        {
            // Go 在这个分支里直接解引用 sourceFile（nil 会 panic）；C# 用一次 `!` 把"该分支下必有值"
            // 告诉可空分析器，运行期仍是同一个引用。
            var rootSourceFile = sourceFile!;
            strs = StringTable.NewStringTable(rootSourceFile.Text(), rootSourceFile.TextCount);
            positionMap = rootSourceFile.GetPositionMap();
        }
        else
        {
            strs = StringTable.NewStringTable("", 0);
            // Go: 只有 sourceFile != nil 时才取 GetPositionMap()，随后统一兜底为 ComputePositionMap("")（见文件头 15）。
            positionMap = sourceFile?.GetPositionMap() ?? ComputePositionMap("");
        }

        uint utf16(int pos) => (uint)positionMap.UTF8ToUTF16(pos);

        var initialNodeCount = sourceFile?.NodeCount ?? 0;
        var nodes = new ByteBuffer((initialNodeCount + 1) * (int)NodeSize);

        // Build node index table for O(1) handle resolution.
        // Index 0 is a nil sentinel; real nodes start at index 1.
        var nodeTable = new List<Node?>(initialNodeCount + 1) { null };

        // Build a small map of nodes we need to track indices for (imports + moduleAugmentations).
        // Values start at 0 and are filled in during the walk.
        Dictionary<Node, uint>? nodeIndexMap = null;
        if (rootNode.Kind == Kind.SourceFile)
        {
            var sf = rootNode.AsSourceFile()!;
            var imports = sf.Imports();
            var total = imports.Length + (sf.ModuleAugmentations?.Length ?? 0);
            if (sf.ExternalModuleIndicator is not null && !ReferenceEquals(sf.ExternalModuleIndicator, rootNode))
            {
                total++;
            }

            if (total > 0)
            {
                nodeIndexMap = new Dictionary<Node, uint>(total);
                foreach (var imp in imports)
                {
                    nodeIndexMap[imp] = 0;
                }

                foreach (var aug in sf.ModuleAugmentations ?? [])
                {
                    nodeIndexMap[aug] = 0;
                }

                if (sf.ExternalModuleIndicator is not null && !ReferenceEquals(sf.ExternalModuleIndicator, rootNode))
                {
                    nodeIndexMap[sf.ExternalModuleIndicator] = 0;
                }
            }
        }

        // byte offset in extendedData where SourceFile fields start
        var sfExtendedDataOffset = 0;

        NodeVisitor visitor = null!;
        visitor = new NodeVisitor(
            visit: node =>
            {
                nodeCount++;
                nodeTable.Add(node);
                if (prevIndex != 0)
                {
                    // this is the next sibling of `prevNode`
                    nodes.PatchUInt32((int)(prevIndex * NodeSize + NodeOffsetNext), nodeCount);
                }

                nodes.WriteUInt32((uint)node.Kind);
                nodes.WriteUInt32(utf16(node.Pos()));
                nodes.WriteUInt32(utf16(node.End()));
                nodes.WriteUInt32(0);
                nodes.WriteUInt32(parentIndex);
                nodes.WriteUInt32(GetNodeData(node, strs, positionMap, extendedData, structuredData));
                nodes.WriteUInt32((uint)node.Flags);

                if (nodeIndexMap is not null && nodeIndexMap.ContainsKey(node))
                {
                    nodeIndexMap[node] = nodeCount;
                }

                var saveParentIndex = parentIndex;
                var currentIndex = nodeCount;
                prevIndex = 0;
                parentIndex = currentIndex;
                visitor.VisitEachChild(node);
                if (sourceFile is not null)
                {
                    foreach (var jsdoc in node.JSDoc(sourceFile) ?? [])
                    {
                        visitor.Visit!(jsdoc);
                    }
                }

                prevIndex = currentIndex;
                parentIndex = saveParentIndex;
                return node;
            },
            hooks: new NodeVisitorHooks
            {
                VisitNodes = (nodeList, v) =>
                {
                    if (nodeList is null)
                    {
                        return nodeList;
                    }

                    nodeCount++;
                    nodeTable.Add(null); // NodeLists are not *ast.Node
                    if (prevIndex != 0)
                    {
                        // this is the next sibling of `prevNode`
                        nodes.PatchUInt32((int)(prevIndex * NodeSize + NodeOffsetNext), nodeCount);
                    }

                    nodes.WriteUInt32(SyntaxKindNodeList);
                    nodes.WriteUInt32(utf16(nodeList.Pos()));
                    nodes.WriteUInt32(utf16(nodeList.End()));
                    nodes.WriteUInt32(0);
                    nodes.WriteUInt32(parentIndex);
                    nodes.WriteUInt32((uint)(nodeList.Nodes?.Length ?? 0));
                    nodes.WriteUInt32(BoolToByte(nodeList.HasTrailingComma()));

                    var saveParentIndex = parentIndex;
                    var currentIndex = nodeCount;
                    prevIndex = 0;
                    parentIndex = currentIndex;
                    v.VisitSlice(nodeList.Nodes);
                    prevIndex = currentIndex;
                    parentIndex = saveParentIndex;
                    return nodeList;
                },
                VisitModifiers = (modifiers, v) =>
                {
                    if (modifiers is not null && modifiers.Nodes is { Length: > 0 })
                    {
                        v.Hooks.VisitNodes!(modifiers, v);
                    }

                    return modifiers;
                },
            });

        // 28 字节的 nil 节点（下标 0）。
        nodes.WriteUInt32(0);
        nodes.WriteUInt32(0);
        nodes.WriteUInt32(0);
        nodes.WriteUInt32(0);
        nodes.WriteUInt32(0);
        nodes.WriteUInt32(0);
        nodes.WriteUInt32(0);

        nodeCount++;
        parentIndex++;
        nodeTable.Add(rootNode); // index 1 = root node

        sfExtendedDataOffset = extendedData.Length;
        nodes.WriteUInt32((uint)rootNode.Kind);
        nodes.WriteUInt32(utf16(rootNode.Pos()));
        nodes.WriteUInt32(utf16(rootNode.End()));
        nodes.WriteUInt32(0);
        nodes.WriteUInt32(0);
        nodes.WriteUInt32(GetNodeData(rootNode, strs, positionMap, extendedData, structuredData));
        nodes.WriteUInt32((uint)rootNode.Flags);

        visitor.VisitEachChild(rootNode);
        if (sourceFile is not null)
        {
            foreach (var jsdoc in rootNode.JSDoc(sourceFile) ?? [])
            {
                visitor.Visit!(jsdoc);
            }
        }

        var hash = UInt128.Zero;
        var parseOpts = 0u;
        if (rootNode.Kind == Kind.SourceFile)
        {
            var rootSourceFile = sourceFile!;
            hash = rootSourceFile.Hash;
            parseOpts = EncodeParseOptions(rootSourceFile.ParseOptions().ExternalModuleIndicatorOptions);

            // Encode imports, moduleAugmentations, and ambientModuleNames into structured data,
            // and patch the placeholder offsets in the SourceFile extended data.
            var sf = rootNode.AsSourceFile()!;
            var importsOffset = EncodeNodeIndexArray(sf.Imports(), nodeIndexMap, structuredData);
            var moduleAugmentationsOffset = EncodeModuleAugmentations(sf.ModuleAugmentations, nodeIndexMap, structuredData);
            var ambientModuleNamesOffset = EncodeStringArray(sf.AmbientModuleNames, structuredData);
            // Patch the 3 placeholder uint32s at sfExtendedDataOffset + 32, 36, 40
            extendedData.PatchUInt32(sfExtendedDataOffset + 32, importsOffset);
            extendedData.PatchUInt32(sfExtendedDataOffset + 36, moduleAugmentationsOffset);
            extendedData.PatchUInt32(sfExtendedDataOffset + 40, ambientModuleNamesOffset);

            // Patch externalModuleIndicator node index at offset 44
            var externalModuleIndicatorIndex = 0u;
            if (sf.ExternalModuleIndicator is not null)
            {
                externalModuleIndicatorIndex = ReferenceEquals(sf.ExternalModuleIndicator, rootNode)
                    ? 1u // root node index
                    : nodeIndexMap is null ? 0u : nodeIndexMap.GetValueOrDefault(sf.ExternalModuleIndicator);
            }

            extendedData.PatchUInt32(sfExtendedDataOffset + 44, externalModuleIndicatorIndex);
        }

        var metadata = (uint)ProtocolVersion << 24;
        var offsetStringTableOffsets = (int)HeaderSize;
        var offsetStringTableData = offsetStringTableOffsets + strs.Offsets.Count * 4;
        var offsetExtendedData = offsetStringTableData + strs.StringLength();
        var offsetStructuredData = offsetExtendedData + extendedData.Length;
        var offsetNodes = offsetStructuredData + structuredData.Length;

        var header = new ByteBuffer((int)HeaderSize);
        AppendUint32s(header, [
            metadata,
            (uint)hash, (uint)(hash >> 32),
            (uint)(hash >> 64), (uint)(hash >> 96),
            parseOpts,
            (uint)offsetStringTableOffsets,
            (uint)offsetStringTableData,
            (uint)offsetExtendedData,
            (uint)offsetStructuredData,
            (uint)offsetNodes,
            0, 0, // source file ID
            0, 0, // source file lease ID
            0, // binder data offset
        ]);

        var strsBytes = strs.Encode();

        var data = new byte[header.Length + strsBytes.Length + extendedData.Length + structuredData.Length + nodes.Length];
        var pos = 0;
        header.AsSpan().CopyTo(data.AsSpan(pos));
        pos += header.Length;
        strsBytes.CopyTo(data.AsSpan(pos));
        pos += strsBytes.Length;
        extendedData.AsSpan().CopyTo(data.AsSpan(pos));
        pos += extendedData.Length;
        structuredData.AsSpan().CopyTo(data.AsSpan(pos));
        pos += structuredData.Length;
        nodes.AsSpan().CopyTo(data.AsSpan(pos));

        return (data, new NodeIndexTable { Nodes = nodeTable.ToArray() });
    }

    /// <summary>
    /// Go: <c>func appendUint32s(buf []byte, values ...uint32) []byte</c>（encoder.go:655-660）。
    /// 偏离：Go 的可变参数在 C# 里每次调用都要分配数组，而这是热路径 → 收成 `ReadOnlySpan&lt;uint&gt;`
    /// （调用点写集合表达式，栈上分配）；返回值也收成 void（ByteBuffer 是引用类型，等价于 Go 的 `*[]byte`）。
    /// </summary>
    internal static void AppendUint32s(ByteBuffer buf, ReadOnlySpan<uint> values)
    {
        foreach (var value in values)
        {
            buf.WriteUInt32(value);
        }
    }

    /// <summary>Go: <c>func getNodeData(node, strs, positionMap, extendedData *[]byte, structuredData *[]byte) uint32</c>（encoder.go:662-674）。</summary>
    internal static uint GetNodeData(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var t = GetNodeDataType(node);
        switch (t)
        {
            case NodeDataTypeChildren:
                return t | GetNodeCommonData(node) | GetChildrenPropertyMask(node);
            case NodeDataTypeString:
                return t | GetNodeCommonData(node) | RecordNodeStrings(node, strs);
            case NodeDataTypeExtendedData:
                return t | GetNodeCommonData(node) | RecordExtendedData(node, strs, positionMap, extendedData, structuredData);
            default:
                throw new DebugFailureException("unreachable");
        }
    }

    /// <summary>Go: <c>func recordExtendedData_SourceFile(…)</c>（encoder.go:678-712）。</summary>
    internal static void RecordExtendedData_SourceFile(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var sf = node.AsSourceFile()!;
        var textIndex = strs.Add(sf.Text(), sf.Kind, sf.Pos(), sf.End());
        var originalTextIndex = textIndex;
        if (sf.OriginalText() != sf.Text())
        {
            originalTextIndex = strs.Add(sf.OriginalText(), 0, 0, 0);
        }

        var fileNameIndex = strs.Add(sf.FileName(), 0, 0, 0);
        var pathIndex = strs.Add(sf.Path().Value, 0, 0, 0);
        var referencedFilesOffset = EncodeFileReferences(sf.ReferencedFiles, positionMap, structuredData);
        var typeRefDirectivesOffset = EncodeFileReferences(sf.TypeReferenceDirectives, positionMap, structuredData);
        var libRefDirectivesOffset = EncodeFileReferences(sf.LibReferenceDirectives, positionMap, structuredData);
        var spanMapOffset = NoStructuredData;
        if (sf.SpanMap() is { } spanMap)
        {
            spanMapOffset = EncodeSpanMap(spanMap, positionMap, ComputePositionMap(sf.OriginalText()), structuredData);
        }

        var supplementalFileNames = Map(sf.SupplementalSourceFiles(), static file => file.FileName());
        var supplementalFileNamesOffset = EncodeStringArray(supplementalFileNames, structuredData);
        var canonicalFileNameIndex = NoStructuredData;
        if (sf.CanonicalSourceFile() is { } canonical)
        {
            canonicalFileNameIndex = strs.Add(canonical.FileName(), 0, 0, 0);
        }

        var contentMapperIndex = NoStructuredData;
        if (sf.ContentMapper() is { Length: > 0 } contentMapper)
        {
            contentMapperIndex = strs.Add(contentMapper, 0, 0, 0);
        }

        var virtualFileNameIndex = NoStructuredData;
        if (sf.VirtualFileName() is { Length: > 0 } virtualFileName)
        {
            virtualFileNameIndex = strs.Add(virtualFileName, 0, 0, 0);
        }

        var diagnosticDirectivesOffset = EncodeDiagnosticDirectives(
            sf.DiagnosticDirectives(), positionMap, ComputePositionMap(sf.OriginalText()), structuredData);

        // imports, moduleAugmentations, ambientModuleNames offsets are placeholders;
        // they will be patched after the tree walk when node indices are known.
        AppendUint32s(extendedData, [
            textIndex, fileNameIndex, pathIndex,
            (uint)sf.LanguageVariant, (uint)sf.ScriptKind,
            referencedFilesOffset, typeRefDirectivesOffset, libRefDirectivesOffset,
            NoStructuredData, NoStructuredData, NoStructuredData,
            0,
            originalTextIndex, spanMapOffset, supplementalFileNamesOffset,
            canonicalFileNameIndex, contentMapperIndex, virtualFileNameIndex, diagnosticDirectivesOffset,
        ]);
    }

    /// <summary>Go: <c>func recordExtendedData_TemplateHead(…)</c>（encoder.go:714-719）。</summary>
    internal static void RecordExtendedData_TemplateHead(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var n = node.AsTemplateHead()!;
        var textIndex = strs.Add(n.Text, node.Kind, node.Pos(), node.End());
        var rawTextIndex = strs.Add(n.RawText, node.Kind, node.Pos(), node.End());
        AppendUint32s(extendedData, [textIndex, rawTextIndex, (uint)n.TemplateFlags]);
    }

    /// <summary>Go: <c>func recordExtendedData_TemplateMiddle(…)</c>（encoder.go:721-726）。</summary>
    internal static void RecordExtendedData_TemplateMiddle(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var n = node.AsTemplateMiddle()!;
        var textIndex = strs.Add(n.Text, node.Kind, node.Pos(), node.End());
        var rawTextIndex = strs.Add(n.RawText, node.Kind, node.Pos(), node.End());
        AppendUint32s(extendedData, [textIndex, rawTextIndex, (uint)n.TemplateFlags]);
    }

    /// <summary>Go: <c>func recordExtendedData_TemplateTail(…)</c>（encoder.go:728-733）。</summary>
    internal static void RecordExtendedData_TemplateTail(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var n = node.AsTemplateTail()!;
        var textIndex = strs.Add(n.Text, node.Kind, node.Pos(), node.End());
        var rawTextIndex = strs.Add(n.RawText, node.Kind, node.Pos(), node.End());
        AppendUint32s(extendedData, [textIndex, rawTextIndex, (uint)n.TemplateFlags]);
    }

    /// <summary>Go: <c>func boolToByte(b bool) byte</c>（encoder.go:735-741）。
    /// 偏离：返回类型 <c>byte</c> → <c>uint</c>：Go 的 <c>uint32(boolToByte(x))</c> 在 C# 里若是 byte，
    /// 每个 `a | b` 表达式都会提升成 int 而需要逐处强转。</summary>
    internal static uint BoolToByte(bool b) => b ? 1u : 0u;

    /// <summary>Go: <c>func hasModifiers(modifiers *ast.ModifierList) bool</c>（encoder.go:743-746）。</summary>
    internal static bool HasModifiers(ModifierList? modifiers) => modifiers is not null && modifiers.Nodes is { Length: > 0 };

    /// <summary>Go: <c>func encodeFileReferences(refs []*ast.FileReference, positionMap *ast.PositionMap, buf *[]byte) uint32</c>（encoder.go:750-769）。</summary>
    internal static uint EncodeFileReferences(FileReference[]? refs, PositionMap positionMap, ByteBuffer buf)
    {
        if (refs is null || refs.Length == 0)
        {
            return NoStructuredData;
        }

        var offset = (uint)buf.Length;
        MsgpackWriteArrayHeader(buf, refs.Length);
        foreach (var reference in refs)
        {
            // Each entry is a 5-element tuple: [pos, end, fileName, resolutionMode, preserve]
            MsgpackWriteArrayHeader(buf, 5);
            MsgpackWriteUint(buf, (uint)positionMap.UTF8ToUTF16(reference.Range.Pos()));
            MsgpackWriteUint(buf, (uint)positionMap.UTF8ToUTF16(reference.Range.End()));
            MsgpackWriteString(buf, reference.FileName);
            MsgpackWriteUint(buf, (uint)reference.ResolutionMode);
            MsgpackWriteBool(buf, reference.Preserve);
        }

        return offset;
    }

    /// <summary>
    /// Go: <c>func encodeNodeIndexArray(nodes []*ast.LiteralLikeNode, indexMap map[*ast.Node]uint32, buf *[]byte) uint32</c>（encoder.go:771-784）。
    /// 偏离：参数类型 <c>[]*ast.LiteralLikeNode</c> → <c>Node[]</c>（C# 的 <c>SourceFile.Imports()</c> 返回 <c>Node[]</c>，
    /// 见 Ast/SourceFile.cs 的 <c>_imports</c> 注释）；缺失键的语义与 Go 一致（nil map / 缺键都得到 0）。
    /// </summary>
    internal static uint EncodeNodeIndexArray(Node[]? nodes, Dictionary<Node, uint>? indexMap, ByteBuffer buf)
    {
        if (nodes is null || nodes.Length == 0)
        {
            return NoStructuredData;
        }

        var offset = (uint)buf.Length;
        MsgpackWriteArrayHeader(buf, nodes.Length);
        foreach (var node in nodes)
        {
            MsgpackWriteUint(buf, indexMap is null ? 0u : indexMap.GetValueOrDefault(node));
        }

        return offset;
    }

    /// <summary>Go: <c>func encodeModuleAugmentations(nodes []*ast.ModuleName, indexMap map[*ast.Node]uint32, buf *[]byte) uint32</c>（encoder.go:786-798）。
    /// 偏离：参数类型 <c>[]*ast.ModuleName</c> → <c>Node[]</c>（理由同 <see cref="EncodeNodeIndexArray"/>）。</summary>
    internal static uint EncodeModuleAugmentations(Node[]? nodes, Dictionary<Node, uint>? indexMap, ByteBuffer buf)
    {
        if (nodes is null || nodes.Length == 0)
        {
            return NoStructuredData;
        }

        var offset = (uint)buf.Length;
        MsgpackWriteArrayHeader(buf, nodes.Length);
        foreach (var node in nodes)
        {
            MsgpackWriteUint(buf, indexMap is null ? 0u : indexMap.GetValueOrDefault(node));
        }

        return offset;
    }

    /// <summary>Go: <c>func encodeStringArray(strs []string, buf *[]byte) uint32</c>（encoder.go:800-810）。</summary>
    internal static uint EncodeStringArray(string[]? strs, ByteBuffer buf)
    {
        if (strs is null || strs.Length == 0)
        {
            return NoStructuredData;
        }

        var offset = (uint)buf.Length;
        MsgpackWriteArrayHeader(buf, strs.Length);
        foreach (var s in strs)
        {
            MsgpackWriteString(buf, s);
        }

        return offset;
    }

    /// <summary>Go: <c>func encodeSpanMap(m *spanmap.SpanMap, virtualPositions, originalPositions *ast.PositionMap, buf *[]byte) uint32</c>（encoder.go:812-839）。</summary>
    internal static uint EncodeSpanMap(SpanMapType m, PositionMap virtualPositions, PositionMap originalPositions, ByteBuffer buf)
    {
        if (m is null)
        {
            return NoStructuredData;
        }

        var segments = m.Segments();
        var offset = (uint)buf.Length;
        MsgpackWriteArrayHeader(buf, segments.Length);
        foreach (var segment in segments)
        {
            var tupleLength = 5;
            if (segment.Features != Feature.All)
            {
                tupleLength = 6;
            }

            MsgpackWriteArrayHeader(buf, tupleLength);
            var virtualStart = virtualPositions.UTF8ToUTF16(segment.VirtualStart);
            var virtualEnd = virtualPositions.UTF8ToUTF16(segment.VirtualEnd);
            var originalStart = originalPositions.UTF8ToUTF16(segment.OriginalStart);
            var originalEnd = originalPositions.UTF8ToUTF16(segment.OriginalEnd);
            MsgpackWriteUint(buf, (uint)virtualStart);
            MsgpackWriteUint(buf, (uint)(virtualEnd - virtualStart));
            MsgpackWriteUint(buf, (uint)originalStart);
            MsgpackWriteUint(buf, (uint)(originalEnd - originalStart));
            MsgpackWriteUint(buf, (uint)segment.Kind);
            if (tupleLength == 6)
            {
                MsgpackWriteUint(buf, (uint)segment.Features);
            }
        }

        return offset;
    }

    /// <summary>Go: <c>func encodeDiagnosticDirectives(directives []ast.MappedDiagnosticDirective, virtualPositions, originalPositions *ast.PositionMap, buf *[]byte) uint32</c>（encoder.go:841-863）。</summary>
    internal static uint EncodeDiagnosticDirectives(MappedDiagnosticDirective[] directives, PositionMap virtualPositions, PositionMap originalPositions, ByteBuffer buf)
    {
        if (directives.Length == 0)
        {
            return NoStructuredData;
        }

        var offset = (uint)buf.Length;
        MsgpackWriteArrayHeader(buf, directives.Length);
        foreach (var directive in directives)
        {
            MsgpackWriteArrayHeader(buf, 6);
            var originalStart = originalPositions.UTF8ToUTF16(directive.OriginalRange.Pos());
            var originalEnd = originalPositions.UTF8ToUTF16(directive.OriginalRange.End());
            var virtualStart = virtualPositions.UTF8ToUTF16(directive.VirtualRange.Pos());
            var virtualEnd = virtualPositions.UTF8ToUTF16(directive.VirtualRange.End());
            MsgpackWriteUint(buf, (uint)originalStart);
            MsgpackWriteUint(buf, (uint)(originalEnd - originalStart));
            MsgpackWriteUint(buf, (uint)virtualStart);
            MsgpackWriteUint(buf, (uint)(virtualEnd - virtualStart));
            MsgpackWriteUint(buf, (uint)directive.Policy);
            MsgpackWriteUint(buf, (uint)directive.UnusedCode);
        }

        return offset;
    }

    // Minimal msgpack writers for the structured data section.

    /// <summary>Go: <c>func msgpackWriteArrayHeader(buf []byte, length int) []byte</c>（encoder.go:865-873）。</summary>
    internal static void MsgpackWriteArrayHeader(ByteBuffer buf, int length)
    {
        if (length <= 0x0f)
        {
            buf.GetWriteSpan(1)[0] = (byte)(0x90 | length);
            return;
        }

        if (length <= 0xffff)
        {
            var span = buf.GetWriteSpan(3);
            span[0] = 0xdc;
            span[1] = (byte)(length >> 8);
            span[2] = (byte)length;
            return;
        }

        var tail = buf.GetWriteSpan(5);
        tail[0] = 0xdd;
        tail[1] = (byte)(length >> 24);
        tail[2] = (byte)(length >> 16);
        tail[3] = (byte)(length >> 8);
        tail[4] = (byte)length;
    }

    /// <summary>Go: <c>func msgpackWriteUint(buf []byte, value uint32) []byte</c>（encoder.go:875-886）。</summary>
    internal static void MsgpackWriteUint(ByteBuffer buf, uint value)
    {
        if (value <= 0x7f)
        {
            buf.GetWriteSpan(1)[0] = (byte)value;
            return;
        }

        if (value <= 0xff)
        {
            var span = buf.GetWriteSpan(2);
            span[0] = 0xcc;
            span[1] = (byte)value;
            return;
        }

        if (value <= 0xffff)
        {
            var span = buf.GetWriteSpan(3);
            span[0] = 0xcd;
            span[1] = (byte)(value >> 8);
            span[2] = (byte)value;
            return;
        }

        var tail = buf.GetWriteSpan(5);
        tail[0] = 0xce;
        tail[1] = (byte)(value >> 24);
        tail[2] = (byte)(value >> 16);
        tail[3] = (byte)(value >> 8);
        tail[4] = (byte)value;
    }

    /// <summary>Go: <c>func msgpackWriteString(buf []byte, s string) []byte</c>（encoder.go:888-900）。
    /// Go 的 <c>len(s)</c>/<c>append(buf, s...)</c> 是字节语义 → C# 用 <see cref="Wtf8"/> 保持逐字节一致。</summary>
    internal static void MsgpackWriteString(ByteBuffer buf, string s)
    {
        var n = Wtf8.ByteCount(s);
        if (n <= 0x1f)
        {
            buf.GetWriteSpan(1)[0] = (byte)(0xa0 | n);
        }
        else if (n <= 0xff)
        {
            var span = buf.GetWriteSpan(2);
            span[0] = 0xd9;
            span[1] = (byte)n;
        }
        else if (n <= 0xffff)
        {
            var span = buf.GetWriteSpan(3);
            span[0] = 0xda;
            span[1] = (byte)(n >> 8);
            span[2] = (byte)n;
        }
        else
        {
            var span = buf.GetWriteSpan(5);
            span[0] = 0xdb;
            span[1] = (byte)(n >> 24);
            span[2] = (byte)(n >> 16);
            span[3] = (byte)(n >> 8);
            span[4] = (byte)n;
        }

        Wtf8.Write(buf, s, n);
    }

    /// <summary>Go: <c>func msgpackWriteBool(buf []byte, value bool) []byte</c>（encoder.go:902-907）。</summary>
    internal static void MsgpackWriteBool(ByteBuffer buf, bool value) =>
        buf.GetWriteSpan(1)[0] = value ? (byte)0xc3 : (byte)0xc2;

    /// <summary>Go: <c>func getNodeCommonData_SyntheticExpression(_ *ast.Node) uint32</c>（encoder.go:914-918）：
    /// SyntheticExpression 是内部编译器节点，永远不该被编码。</summary>
    internal static uint GetNodeCommonData_SyntheticExpression(Node node) =>
        throw new DebugFailureException("SyntheticExpression should never be encoded");

    /// <summary>Go: <c>func recordExtendedData_StringLiteral(…)</c>（encoder.go:923-927）。</summary>
    internal static void RecordExtendedData_StringLiteral(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var n = node.AsStringLiteral()!;
        var textIndex = strs.Add(n.Text, node.Kind, node.Pos(), node.End());
        AppendUint32s(extendedData, [textIndex, (uint)n.TokenFlags]);
    }

    /// <summary>Go: <c>func recordExtendedData_NumericLiteral(…)</c>（encoder.go:929-933）。</summary>
    internal static void RecordExtendedData_NumericLiteral(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var n = node.AsNumericLiteral()!;
        var textIndex = strs.Add(n.Text, node.Kind, node.Pos(), node.End());
        AppendUint32s(extendedData, [textIndex, (uint)n.TokenFlags]);
    }

    /// <summary>Go: <c>func recordExtendedData_BigIntLiteral(…)</c>（encoder.go:935-939）。</summary>
    internal static void RecordExtendedData_BigIntLiteral(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var n = node.AsBigIntLiteral()!;
        var textIndex = strs.Add(n.Text, node.Kind, node.Pos(), node.End());
        AppendUint32s(extendedData, [textIndex, (uint)n.TokenFlags]);
    }

    /// <summary>Go: <c>func recordExtendedData_RegularExpressionLiteral(…)</c>（encoder.go:941-945）。</summary>
    internal static void RecordExtendedData_RegularExpressionLiteral(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var n = node.AsRegularExpressionLiteral()!;
        var textIndex = strs.Add(n.Text, node.Kind, node.Pos(), node.End());
        AppendUint32s(extendedData, [textIndex, (uint)n.TokenFlags]);
    }

    /// <summary>Go: <c>func recordExtendedData_NoSubstitutionTemplateLiteral(…)</c>（encoder.go:947-951）。</summary>
    internal static void RecordExtendedData_NoSubstitutionTemplateLiteral(Node node, StringTable strs, PositionMap positionMap, ByteBuffer extendedData, ByteBuffer structuredData)
    {
        var n = node.AsNoSubstitutionTemplateLiteral()!;
        var textIndex = strs.Add(n.Text, node.Kind, node.Pos(), node.End());
        AppendUint32s(extendedData, [textIndex, (uint)n.TemplateFlags]);
    }
}

/// <summary>
/// Go: <c>type NodeIndexTable struct { Nodes []*ast.Node; sortedOnce sync.Once; sortedIdx []uint32 }</c>
/// （encoder.go:330-334）：节点与其编码下标之间的映射，供 O(1) 句柄解析使用。
/// 偏离：Go 里它是**包级**类型 → C# 顶层类（不是 <see cref="Encoder"/> 的嵌套类，这样
/// <c>TypeScript.Api.Encoder.NodeIndexTable</c> 就是它的全限定名）；Go 的 `sync.Once` → 标志 + Lock 双检
/// （见文件头 9），所以是 sealed class 而不是 readonly struct（懒排序要写状态，值语义会在每次拷贝时丢掉缓存）。
/// </summary>
public sealed class NodeIndexTable
{
    /// <summary>Go: <c>Nodes []*ast.Node</c>（下标 → 节点，用于解析）。下标 0 是 nil 哨兵；NodeList 占位是 null。</summary>
    public Node?[] Nodes = [];

    /// <summary>Go: <c>sortedIdx []uint32</c>：Nodes 的下标，按节点 ID 排序，懒惰构建。</summary>
    private uint[]? _sortedIdx;

    /// <summary>Go: <c>sortedOnce sync.Once</c>。</summary>
    private readonly Lock _sortedMu = new();

    /// <summary>Go: <c>func (t *NodeIndexTable) GetIndex(node *ast.Node) uint32</c>（encoder.go:342-368）。</summary>
    public uint GetIndex(Node node)
    {
        var sortedIdx = EnsureSorted();
        var target = GetNodeId(node);
        var found = TryBinarySearchUniqueFunc(sortedIdx, (_, el) => GetNodeId(Nodes[el]!).CompareTo(target), out var i);
        return found ? sortedIdx[i] : 0;
    }

    /// <summary>Go 的 <c>t.sortedOnce.Do(func() { … })</c> 主体：构建按节点 ID 排序的下标数组。</summary>
    private uint[] EnsureSorted()
    {
        var sorted = Volatile.Read(ref _sortedIdx);
        if (sorted is not null)
        {
            return sorted;
        }

        lock (_sortedMu)
        {
            var existing = _sortedIdx;
            if (existing is not null)
            {
                return existing;
            }

            var nodes = Nodes;
            var idx = new List<uint>(nodes.Length);
            for (var i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] is not null)
                {
                    idx.Add((uint)i);
                }
            }

            var arr = idx.ToArray();
            // Go 用 slices.SortFunc（不稳定排序）；节点 ID 建表后互不相同，所以稳定性无关紧要。
            Array.Sort(arr, (a, b) => GetNodeId(nodes[a]!).CompareTo(GetNodeId(nodes[b]!)));
            Volatile.Write(ref _sortedIdx, arr);
            return arr;
        }
    }
}

/// <summary>
/// C# 专用基础设施（Go 无对应物，见文件头 2）：可增长的字节缓冲。
/// Go 里到处是 <c>append(buf, …)</c> + 回填 <c>buf[i] = b</c>（`nodes`/`extendedData`/`structuredData`/`otherStrings`），
/// C# 的 <c>byte[]</c> 不可增长、<c>List&lt;byte&gt;</c> 又不能原地写索引，所以统一用这个类：
/// <see cref="GetWriteSpan"/>（增长 + 返回可写区间）、<see cref="PatchUInt32"/>（回填小端 u32）、
/// <see cref="AsSpan"/>（读回已写内容）。分配策略与 Go 的 append 增长一致（翻倍）。
/// </summary>
internal sealed class ByteBuffer
{
    private byte[] _buf;
    private int _len;

    internal ByteBuffer(int capacity) => _buf = capacity <= 0 ? [] : new byte[capacity];

    /// <summary>Go 的 <c>len(buf)</c>。</summary>
    internal int Length => _len;

    /// <summary>Go 的 <c>buf[i]</c>（读写）。</summary>
    internal byte this[int index]
    {
        get => _buf[index];
        set => _buf[index] = value;
    }

    /// <summary>预留 <paramref name="count"/> 字节并返回可写区间（写完即计入 <see cref="Length"/>）。</summary>
    internal Span<byte> GetWriteSpan(int count)
    {
        EnsureCapacity(count);
        var span = _buf.AsSpan(_len, count);
        _len += count;
        return span;
    }

    internal void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(GetWriteSpan(4), value);

    /// <summary>Go 的 <c>binary.LittleEndian.PutUint32(buf[offset:], value)</c>（回填已写区域）。</summary>
    internal void PatchUInt32(int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(offset, 4), value);

    /// <summary>Go 的 <c>buf[:len(buf)]</c>。</summary>
    internal ReadOnlySpan<byte> AsSpan() => _buf.AsSpan(0, _len);

    /// <summary>Go 的 <c>slices.Clone(buf)</c> / <c>make([]byte, len(buf))</c> + copy。</summary>
    internal byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();

    private void EnsureCapacity(int count)
    {
        var required = _len + count;
        if (required <= _buf.Length)
        {
            return;
        }

        Array.Resize(ref _buf, Math.Max(required, Math.Max(16, _buf.Length * 2)));
    }
}

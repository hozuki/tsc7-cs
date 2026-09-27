// ported from: github.com/peter-evans/patience@v0.3.0/unified.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `Hunk` 是 struct（字段导出）→ C# `sealed class` + `public` 字段：makeHunks 里到处是
//    `hunks[curHunk].SrcLines += ...` 这类**通过切片元素改字段**的写法，Go 的切片元素可寻址、C# 需要
//    引用类型（值类型会改到副本上，且 `hunks[curHunk] = ...` 的写回语义也不是逐处的）。
// 2) `block.Diffs = append(block.Diffs, x...)` → `block.Diffs = [.. block.Diffs, .. x]`（新数组）。
//    Go 的 append 可能复用底层数组（别名共享），但这里的 block 每次都是新构造/被丢弃的临时值，
//    没有任何后续写回，故选"复制成新数组"这一确定性语义（unified_test.go 的期望值同时校验了这点）。
// 3) Go 的闭包 `updateHunks := func(block Hunk, lastBlock bool)` 捕获 `hunks` 切片变量（只有 append，
//    没有截断）→ C# 局部函数捕获 `List<Hunk> hunks`（只有 Add）。
// 4) `diffs []DiffLine` 可能是 Go 的 nil → `DiffLine[]?`，入口 `??= []`（`len(diffs) == 0` 语义）。
// 5) 返回 `[]Hunk`：Go 在 `len(diffs) == 0` 与"只有相等行"两种情况下返回 nil → C# 返回 `null`。
// 6) `min(a, b)` 是 unified.go 里的包级函数 → private `Min`。
namespace TypeScript.Patience;

/// <summary>Go 的 <c>type Hunk struct</c>：diff 的一个片段。</summary>
public sealed class Hunk
{
    public DiffLine[] Diffs = [];

    public int SrcStart;

    public int SrcLines;

    public int DstStart;

    public int DstLines;
}

public static partial class Patience
{
    /// <summary>
    /// Go: <c>func makeHunks(diffs []DiffLine, precontext, postcontext int) []Hunk</c>。
    /// 纯相等行（或空 diff）返回 null（Go 的 nil）。公开是为了照搬 Go 的同 package 单测。
    /// </summary>
    public static Hunk[]? MakeHunks(DiffLine[]? diffs, int precontext, int postcontext)
    {
        diffs ??= [];
        if (diffs.Length == 0)
        {
            return null;
        }

        var hunks = new List<Hunk>();

        // Update hunks with a diff block.
        void UpdateHunks(Hunk block, bool lastBlock)
        {
            var curHunk = hunks.Count - 1;
            if (block.Diffs[0].Type == DiffType.Equal)
            {
                // Unmodified block.
                if (hunks.Count == 0)
                {
                    // Start a new hunk with the tail of the block.
                    var ctxLen = Min(precontext, block.Diffs.Length);
                    hunks.Add(new Hunk
                    {
                        Diffs = block.Diffs[(block.Diffs.Length - ctxLen)..],
                        SrcStart = block.Diffs.Length - ctxLen + block.SrcStart,
                        SrcLines = ctxLen,
                        DstStart = block.Diffs.Length - ctxLen + block.DstStart,
                        DstLines = ctxLen,
                    });
                }
                else
                {
                    // Update the current hunk.
                    var maxNonContext = precontext + postcontext;
                    if (lastBlock)
                    {
                        maxNonContext = postcontext;
                    }

                    if (block.Diffs.Length <= maxNonContext)
                    {
                        // Block is small enough to be appended to the current hunk.
                        hunks[curHunk].Diffs = [.. hunks[curHunk].Diffs, .. block.Diffs];
                        hunks[curHunk].SrcLines += block.Diffs.Length;
                        hunks[curHunk].DstLines += block.Diffs.Length;
                    }
                    else
                    {
                        // Append the head of the block to the current hunk.
                        hunks[curHunk].Diffs = [.. hunks[curHunk].Diffs, .. block.Diffs[..postcontext]];
                        hunks[curHunk].SrcLines += postcontext;
                        hunks[curHunk].DstLines += postcontext;
                        if (!lastBlock)
                        {
                            // Start a new hunk with the tail of the block.
                            hunks.Add(new Hunk
                            {
                                Diffs = block.Diffs[(block.Diffs.Length - precontext)..],
                                SrcStart = block.Diffs.Length - precontext + block.SrcStart,
                                SrcLines = precontext,
                                DstStart = block.Diffs.Length - precontext + block.DstStart,
                                DstLines = precontext,
                            });
                        }
                    }

                    // Update starting line numbers if the current hunk had no source or destination diff.
                    if (hunks[curHunk].SrcStart == 0)
                    {
                        hunks[curHunk].SrcStart = block.SrcStart;
                    }

                    if (hunks[curHunk].DstStart == 0)
                    {
                        hunks[curHunk].DstStart = block.DstStart;
                    }
                }
            }
            else if (hunks.Count > 0)
            {
                // Modified block, appended to the current hunk.
                hunks[curHunk].Diffs = [.. hunks[curHunk].Diffs, .. block.Diffs];
                hunks[curHunk].SrcLines += block.SrcLines;
                hunks[curHunk].DstLines += block.DstLines;
            }
            else
            {
                // Modified block, first hunk.
                hunks.Add(new Hunk
                {
                    Diffs = block.Diffs,
                    SrcStart = block.SrcStart,
                    SrcLines = block.SrcLines,
                    DstStart = block.DstStart,
                    DstLines = block.DstLines,
                });
            }
        }

        // Aggregate blocks of modified and unmodified diff lines, creating
        // or updating hunks after each block.
        var block = new Hunk();
        var modifiedLines = 0;
        var srcLineNum = 0;
        var dstLineNum = 0;
        foreach (var l in diffs)
        {
            if (block.Diffs.Length == 0 ||
                block.Diffs[0].Type == l.Type ||
                (block.Diffs[0].Type != l.Type && block.Diffs[0].Type != DiffType.Equal && l.Type != DiffType.Equal))
            {
                block.Diffs = [.. block.Diffs, l];
            }
            else
            {
                UpdateHunks(block, false);
                block = new Hunk { Diffs = [l] };
            }

            switch (l.Type)
            {
                case DiffType.Delete:
                    srcLineNum++;
                    block.SrcLines++;
                    modifiedLines++;
                    break;
                case DiffType.Insert:
                    dstLineNum++;
                    block.DstLines++;
                    modifiedLines++;
                    break;
                case DiffType.Equal:
                    srcLineNum++;
                    dstLineNum++;
                    block.SrcLines++;
                    block.DstLines++;
                    break;
            }

            if (block.SrcStart == 0 && (l.Type == DiffType.Equal || l.Type == DiffType.Delete))
            {
                block.SrcStart = srcLineNum;
            }

            if (block.DstStart == 0 && (l.Type == DiffType.Equal || l.Type == DiffType.Insert))
            {
                block.DstStart = dstLineNum;
            }
        }

        UpdateHunks(block, true);

        // Return no hunks if the diffs contain only equal lines.
        return modifiedLines == 0 ? null : hunks.ToArray();
    }

    /// <summary>Go: <c>func min(a, b int) int</c>。</summary>
    private static int Min(int a, int b)
    {
        return a < b ? a : b;
    }
}

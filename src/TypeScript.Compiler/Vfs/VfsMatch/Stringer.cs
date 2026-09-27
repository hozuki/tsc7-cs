// ported from: internal/vfs/vfsmatch/stringer_generated.go
//
// Go 侧由 `stringer -type=Usage -trimprefix=Usage` 生成；C# 的 enum 不能覆写 ToString，
// 所以按 docs §3.3 落成静态类 `VfsMatch` 里的扩展方法 `String(this Usage)`。
//
// 输出与 Go 逐字一致：_Usage_name = "FilesDirectoriesExclude"、_Usage_index = {0, 5, 16, 23}；
// 越界/负值 → "Usage(" + strconv.FormatInt(int64(i), 10) + ")"。
namespace TypeScript.Vfs.VfsMatch;

public static partial class VfsMatch
{
    private const string _usageName = "FilesDirectoriesExclude";

    private static readonly byte[] _usageIndex = [0, 5, 16, 23];

    /// <summary>Go: <c>func (i Usage) String() string</c>。</summary>
    public static string String(this Usage i)
    {
        var value = (int)i;
        var idx = value - 0;
        if (value < 0 || idx >= _usageIndex.Length - 1)
        {
            // Go: strconv.FormatInt —— 不随区域变化，用 invariant。
            return "Usage(" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        return _usageName[_usageIndex[idx].._usageIndex[idx + 1]];
    }
}

// ported from: internal/nativepath/symlink_windows.go, symlink_other.go
//
// Go 分平台实现：Windows 用 GetFileAttributesEx + FILE_ATTRIBUTE_REPARSE_POINT；其它平台用
// os.Lstat(...).Mode()&ModeSymlink。.NET 的 File.GetAttributes 在 Windows 上就是 GetFileAttributes（不跟随链接）、
// 在 Unix 上是 lstat 语义并把符号链接标成 FileAttributes.ReparsePoint，所以两边可以共用一条实现
// （Go 的两个分支在这里合并，行为一致）。失败（文件不存在等）→ false，与 Go 一致。
namespace TypeScript.NativePath;

public static partial class NativePath
{
    /// <summary>Go: <c>IsSymlinkOrReparsePoint(path) bool</c>。</summary>
    public static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

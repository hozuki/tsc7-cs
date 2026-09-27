// ported from: internal/vfs/vfsmock/wrapper.go
namespace TypeScript.Vfs.VfsMock;

using TypeScript.Vfs;

/// <summary>Go 的 <c>vfsmock</c> 包级函数。</summary>
public static partial class VfsMock
{
    /// <summary>Go: <c>Wrap(fs vfs.FS) *FSMock</c>：用真实 FS 实现一个 FS mock 的所有方法。</summary>
    public static FSMock Wrap(FS fs)
    {
        return new FSMock
        {
            DirectoryExistsFunc = fs.DirectoryExists,
            FileExistsFunc = fs.FileExists,
            GetAccessibleEntriesFunc = fs.GetAccessibleEntries,
            ReadFileFunc = fs.TryReadFile,
            RealpathFunc = fs.Realpath,
            RemoveFunc = fs.Remove,
            ChtimesFunc = fs.Chtimes,
            StatFunc = fs.Stat,
            UseCaseSensitiveFileNamesFunc = fs.UseCaseSensitiveFileNames,
            WriteFileFunc = fs.WriteFile,
            AppendFileFunc = fs.AppendFile,
        };
    }
}

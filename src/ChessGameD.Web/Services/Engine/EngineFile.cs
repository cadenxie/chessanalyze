namespace ChessGameD.Web.Services.Engine;

/// <summary>
/// 引擎可执行文件在磁盘上的辅助操作。
///
/// Windows 下 <c>.exe</c> 不需要执行位,Linux / macOS 下解压出来的引擎文件
/// 必须带 <c>x</c> 权限才能被 <see cref="System.Diagnostics.Process"/> 拉起。
/// </summary>
public static class EngineFile
{
    /// <summary>给文件补上执行权限(Windows 上是空操作)。返回是否成功。</summary>
    public static bool EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var mode = File.GetUnixFileMode(path);
            mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>文件是否带执行权限(Windows 上只看文件是否存在)。</summary>
    public static bool HasExecutablePermission(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return File.Exists(path);
        }

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
        }
        catch
        {
            return false;
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Threading;

internal static class Compatibility
{

    internal static string GetFileVersion(string path)
    {
        throw new NotImplementedException();
        //var obj = FileVersionInfo.GetVersionInfo(path);
        //return (string)PortableShim.FileVersionInfo.FileVersion.GetValue(obj);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetCommandLine();
    public const int WRN_FileAlreadyIncluded = 2002;
    public const int ERR_CantReadConfigFile = 7093;
    public static void SetCurrentUICulture(CultureInfo c)
    {
        //Thread.CurrentThread.CurrentUICulture = c;        
    }
}

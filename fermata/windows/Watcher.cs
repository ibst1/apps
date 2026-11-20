using System.Runtime.InteropServices;
using System.Text;

namespace Fermata;

/// <summary>Finds PowerPoint's slideshow window ("PowerPoint-bildspel", class screenClass).</summary>
static class SlideshowWindow
{
    /// <summary>The visible, not minimised slideshow window, or IntPtr.Zero. Keeps <paramref name="current"/> if it still qualifies.</summary>
    public static IntPtr Find(IntPtr current, out string title)
    {
        title = null;
        if (current != IntPtr.Zero && Qualifies(current)) { title = Title(current); return current; }
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (Qualifies(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        if (found != IntPtr.Zero) title = Title(found);
        return found;
    }

    static bool Qualifies(IntPtr h)
    {
        if (!IsWindow(h) || !IsWindowVisible(h) || IsIconic(h)) return false;
        var c = new StringBuilder(64); GetClassName(h, c, 64);
        if (c.ToString() != "screenClass") return false;
        GetWindowRect(h, out var r);
        return r.R - r.L > 200 && r.B - r.T > 150;
    }

    static string Title(IntPtr h) { var t = new StringBuilder(256); GetWindowText(h, t, 256); return t.ToString(); }

    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
}

static class Log
{
    static readonly string PathName = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fermata", "log.txt");
    static readonly object Gate = new();
    public static void Write(string s)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName));
                var fi = new FileInfo(PathName);
                if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
                File.AppendAllText(PathName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}\r\n");
            }
        }
        catch { }
    }
    public static string File_ => PathName;
}

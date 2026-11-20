using System.Runtime.InteropServices;
using System.Text;

namespace Fermata;

/// <summary>Finds PowerPoint's slideshow window ("PowerPoint-bildspel", class screenClass).</summary>
static class SlideshowWindow
{
    static readonly Dictionary<IntPtr, DateTime> FirstSeen = new();

    /// <summary>
    /// The visible, not minimised slideshow window, or IntPtr.Zero. With several slideshows running,
    /// the most recently started one wins (that is the one just started with F5).
    /// </summary>
    public static IntPtr Find(IntPtr current, out string title)
    {
        title = null;
        var now = DateTime.UtcNow;
        var all = new List<IntPtr>();
        EnumWindows((h, _) => { if (Qualifies(h)) all.Add(h); return true; }, IntPtr.Zero);
        foreach (var h in all) if (!FirstSeen.ContainsKey(h)) FirstSeen[h] = now;
        foreach (var h in FirstSeen.Keys.Where(k => !all.Contains(k)).ToList()) FirstSeen.Remove(h);
        if (all.Count == 0) return IntPtr.Zero;
        var newest = all.OrderByDescending(h => FirstSeen[h]).First();
        // keep the current one unless a newer slideshow has appeared
        var pick = current != IntPtr.Zero && all.Contains(current) && FirstSeen[current] >= FirstSeen[newest] ? current : newest;
        title = Title(pick);
        return pick;
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

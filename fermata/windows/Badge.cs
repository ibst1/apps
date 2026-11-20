using System.Drawing;
using System.Runtime.InteropServices;

namespace Fermata;

/// <summary>
/// "● Spelar in 0:12:34": a small red badge at the top of the speaker's screen while a recording
/// runs. Always on top, never takes focus and lets clicks through, so PowerPoint (presenter view,
/// clicker) is not disturbed. It is its own window, so it never ends up in the captured slideshow.
/// With two screens it goes on the screen WITHOUT the slideshow (the laptop).
/// </summary>
sealed class RecordingBadge : Form
{
    string _text = "";

    public RecordingBadge()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0xC6, 0x28, 0x28);
        Opacity = 0.92;                     // also makes WinForms set up the layered (click-through) window
        DoubleBuffered = true;
        Size = new Size(140, 22);            // small: just enough to be noticed
    }

    // No activation, click-through, not in Alt+Tab
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x00000080 /*TOOLWINDOW*/ | 0x00000020 /*TRANSPARENT*/ | 0x00080000 /*LAYERED*/ | 0x00000008 /*TOPMOST*/;
            return cp;
        }
    }

    /// <summary>Show with the elapsed time, on the screen without the slideshow (or the slideshow's screen if there is only one).</summary>
    public void ShowRecording(TimeSpan elapsed, IntPtr slideshow)
    {
        var text = $"● Spelar in {(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        var showScreen = slideshow != IntPtr.Zero ? Screen.FromHandle(slideshow) : Screen.PrimaryScreen;
        var target = Screen.AllScreens.FirstOrDefault(s => !s.Equals(showScreen)) ?? showScreen;
        var wa = target.WorkingArea;
        var loc = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + 2);
        if (Location != loc) Location = loc;
        if (text != _text) { _text = text; Invalidate(); }
        if (!Visible) { Show(); }
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // keep above PowerPoint's full-screen windows
    }

    public void HideBadge() { if (Visible) Hide(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        using var f = new Font("Segoe UI Semibold", 8.5f);
        TextRenderer.DrawText(g, _text, f, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace Fermata;

/// <summary>
/// Fermata for Windows: a small tray helper for the Fermata web page.
/// When the page is in mode "Föreläsare" or "Båda" and PowerPoint starts a slideshow, the
/// slideshow window is captured WITHOUT the mouse pointer (also behind the presenter view)
/// and handed to the page, which sends it to the technician or records it.
/// Portable: one exe, no install, no admin.
/// </summary>
static class Program
{
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0";
    const string PageUrl = "https://apps.ibstedt.net/fermata/";

    static PageServer _server;
    static WindowCapture _capture;
    static IntPtr _hwnd;
    static NotifyIcon _tray;
    static ToolStripMenuItem _statusItem;
    static string _status = "";
    static StatusWindow _window;
    static RecordingBadge _badge;
    static Icon _iconIdle, _iconRec;
    static byte[] _last;            // newest JPEG of the slideshow
    static string Dump;
    static DateTime _lastAt;

    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Fermata.Helper.SingleInstance", out bool first);
        if (!first) { ShowRunning(); OpenPage(); return; }   // already running: bring its window forward, show the page

        ApplicationConfiguration.Initialize();
        _server = new PageServer();
        try { _server.Start(); }
        catch (Exception e)
        {
            MessageBox.Show($"Fermata kunde inte starta (port {PageServer.Port} är upptagen?).\n\n{e.Message}", "Fermata");
            return;
        }
        Log.Write($"Fermata {Version} started");

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Startar …") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Öppna Fermata-sidan", null, (_, _) => OpenPage());
        menu.Items.Add("Visa logg", null, (_, _) => { try { Process.Start(new ProcessStartInfo(Log.File_) { UseShellExecute = true }); } catch { } });
        var icon = MakeIcon(Color.FromArgb(0x2F, 0x6F, 0x5E));
        _iconIdle = icon; _iconRec = MakeIcon(Color.FromArgb(0xC6, 0x28, 0x28));
        _badge = new RecordingBadge();
        void Quit() { _tray.Visible = false; _badge.HideBadge(); StopCapture(); Application.Exit(); }
        menu.Items.Add("Avsluta", null, (_, _) => _window.Close());
        _tray = new NotifyIcon { Icon = icon, Text = "Fermata", ContextMenuStrip = menu, Visible = true };
        _window = new StatusWindow(icon, OpenPage, Quit) { IsCapturing = () => _capture != null, IdleIcon = _iconIdle, RecIcon = _iconRec };
        _tray.DoubleClick += (_, _) => { _window.WindowState = FormWindowState.Normal; _window.Activate(); };

        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _server.Changed += () => ui.Post(_ => { Tick(); if (_capture != null) AnnounceStart(); }, null);
        var timer = new System.Windows.Forms.Timer { Interval = 300 };
        timer.Tick += (_, _) => Tick();
        timer.Start();

        int di = Array.IndexOf(args, "--dump");
        if (di >= 0 && di + 1 < args.Length) Dump = args[di + 1];
        if (!args.Contains("--no-open")) OpenPage();
        Tick();
        Application.Run(_window);
        StopCapture();
    }

    static bool Wants(PageServer.Client c) => c.Role is "lecturer" or "both";

    /// <summary>Every 300 ms: start or stop capturing as the slideshow and the page's mode change.</summary>
    static void Tick()
    {
        var clients = _server.Clients;
        bool active = clients.Any(Wants);
        var h = SlideshowWindow.Find(_hwnd, out var title);

        if (_capture != null && (h != _hwnd || !active)) StopCapture();
        if (_capture == null && active && h != IntPtr.Zero)
        {
            try
            {
                _hwnd = h;
                _capture = new WindowCapture(h);
                _capture.FrameReady += (jpeg, w, hh) =>
                {
                    _last = jpeg; _lastAt = DateTime.UtcNow; _server.SendFrame(jpeg, Wants);
                    if (Dump != null) try { File.WriteAllBytes(Dump, jpeg); } catch { }   // --dump <file>: for testing
                };
                Log.Write($"Slideshow found: {title}");
                AnnounceStart();
            }
            catch (Exception e) { Log.Write("capture start failed: " + e); _capture = null; _hwnd = IntPtr.Zero; }
        }

        // Windows only delivers a frame when the slide changes. Repeat the last one every second, so a
        // page that connects during a still slide gets a picture and the recording never stalls.
        var last = _last;
        if (_capture != null && last != null && DateTime.UtcNow - _lastAt > TimeSpan.FromSeconds(1))
        { _lastAt = DateTime.UtcNow; _server.SendFrame(last, Wants); }

        // The mode of the page that uses the slideshow (another open Fermata tab may be "Tekniker")
        string role = (clients.LastOrDefault(Wants) ?? clients.LastOrDefault())?.Role ?? "";
        string mode = role switch { "lecturer" => "Föreläsare", "both" => "Båda", "tech" => "Tekniker", _ => "" };
        string state, detail; bool ok = true;
        if (clients.Count == 0) { state = "Väntar på Fermata-sidan"; detail = "Klicka Öppna Fermata-sidan och välj Föreläsare eller Båda."; ok = false; }
        else if (!active) { state = $"Sidan är ansluten – läge: {(mode == "" ? "inget valt" : mode)}"; detail = "Välj Föreläsare eller Båda på sidan."; ok = false; }
        else if (_capture != null) { state = "Bildspelet tas utan muspekare" + (_capture.Fps > 0 ? $" ({_capture.Fps} bilder/s)" : " (bilden står still)"); detail = $"Läge på sidan: {mode}. " + (mode == "Föreläsare" ? "Det skickas till teknikern när koden är ifylld." : "Sidan spelar in."); }
        else { state = $"Redo – läge: {mode}"; detail = "Starta bildspelet i PowerPoint (F5), gärna med talarvy. Det tas automatiskt, utan muspekare."; }

        // Recording indicator: the page reports whether a recording runs (Båda: this computer,
        // Föreläsare: the technician's). Red badge on the speaker's screen, red tray icon and window text.
        var recClient = clients.Where(c => Wants(c) && c.Rec && DateTime.UtcNow - c.RecAt < TimeSpan.FromSeconds(4)).LastOrDefault();
        bool recording = recClient != null && _capture != null;
        if (recording)
        {
            var el = TimeSpan.FromMilliseconds(recClient.RecMs) + (DateTime.UtcNow - recClient.RecAt);
            _badge.ShowRecording(el, _hwnd);
            state = $"●  Spelar in  {(int)el.TotalHours}:{el.Minutes:00}:{el.Seconds:00}";
            detail = (mode == "Föreläsare" ? "Teknikern spelar in bildspelet" : "Bildspelet spelas in") + ", utan muspekare."
                     + (_capture.Fps > 0 ? $" {_capture.Fps} bilder/s." : "");
        }
        else _badge.HideBadge();
        if (_tray.Icon != (recording ? _iconRec : _iconIdle)) _tray.Icon = recording ? _iconRec : _iconIdle;
        SetStatus(state);
        _window?.Show(state, detail, ok, recording);
    }

    /// <summary>A second start: bring the running program's window to the front.</summary>
    static void ShowRunning()
    {
        try
        {
            var me = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(me.ProcessName))
                if (p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero) { ShowWindow(p.MainWindowHandle, 9); SetForegroundWindow(p.MainWindowHandle); }
        }
        catch { }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    static void AnnounceStart()
    {
        _server.Broadcast(new { type = "show-start", title = "PowerPoint-bildspel" });
        if (_last != null) _lastAt = DateTime.MinValue;    // resend the last frame on the next tick
    }

    static void StopCapture()
    {
        if (_capture == null) return;
        try { _capture.Dispose(); } catch { }
        _capture = null; _hwnd = IntPtr.Zero; _last = null;
        _server?.Broadcast(new { type = "show-end" });
    }

    static void SetStatus(string s)
    {
        if (s == _status) return;
        _status = s;
        _statusItem.Text = s;
        _tray.Text = ("Fermata – " + s).Length > 63 ? ("Fermata – " + s)[..63] : "Fermata – " + s;
    }

    static void OpenPage()
    {
        try { Process.Start(new ProcessStartInfo(PageUrl) { UseShellExecute = true }); } catch { }
    }

    static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(color);
            g.FillEllipse(bg, 1, 1, 30, 30);
            // a fermata: an arc over a dot
            using var pen = new Pen(Color.White, 3f);
            g.DrawArc(pen, 7, 9, 18, 16, 180, 180);
            g.FillEllipse(Brushes.White, 13, 17, 6, 6);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

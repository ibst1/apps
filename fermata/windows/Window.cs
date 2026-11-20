using System.Drawing;

namespace Fermata;

/// <summary>The small visible window: what Fermata is doing right now, plus two buttons.</summary>
sealed class StatusWindow : Form
{
    readonly Label _state, _detail;
    public Func<bool> IsCapturing = () => false;

    public StatusWindow(Icon icon, Action openPage, Action quit)
    {
        Text = "Fermata";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        // Compact: one status line, one help line, two small buttons (the window title says "Fermata").
        // Sizes come from the content, so it also fits on scaled screens (125 %, 150 %).
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.FromArgb(0x1F, 0x1F, 0x1D);
        ForeColor = Color.FromArgb(0xEC, 0xEB, 0xE7);
        Font = new Font("Segoe UI", 8.5f);
        int w = LogicalToDeviceUnits(280);

        _state = new Label { Font = new Font("Segoe UI Semibold", 9.5f), AutoSize = true, MaximumSize = new Size(w, 0), ForeColor = Color.FromArgb(0x5F, 0xAE, 0x97), Margin = new Padding(0, 0, 0, 2) };
        _detail = new Label { AutoSize = true, MaximumSize = new Size(w, 0), MinimumSize = new Size(w, 0), ForeColor = Color.FromArgb(0xA0, 0x9E, 0x98), Margin = new Padding(0, 0, 0, 6) };
        var open = new Button { Text = "Öppna Fermata-sidan", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0x5F, 0xAE, 0x97), ForeColor = Color.FromArgb(0x0E, 0x1A, 0x16), Margin = new Padding(0, 0, 8, 0) };
        var exit = new Button { Text = "Avsluta", AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0) };
        open.FlatAppearance.BorderSize = 0;
        open.Click += (_, _) => openPage();
        exit.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.AddRange(new Control[] { open, exit });
        var stack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10, 8, 10, 10), Margin = new Padding(0) };
        stack.Controls.AddRange(new Control[] { _state, _detail, buttons });
        Controls.Add(stack);

        // Bottom right of the screen, out of the way of the slides
        var wa = Screen.PrimaryScreen.WorkingArea;
        Load += (_, _) => Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
        SizeChanged += (_, _) => { if (Visible) Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16); };
        FormClosing += (_, e) =>
        {
            if (IsCapturing() && MessageBox.Show(this, "Bildspelet tas just nu. Avsluta Fermata ändå? Då slutar sändningen eller inspelningen.",
                    "Fermata", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
            quit();
        };
    }

    public void Show(string state, string detail, bool ok, bool recording = false)
    {
        if (_state.Text != state) _state.Text = state;
        _state.ForeColor = recording ? Color.FromArgb(0xEF, 0x53, 0x50) : ok ? Color.FromArgb(0x5F, 0xAE, 0x97) : Color.FromArgb(0xF2, 0xC4, 0x6B);
        if (_detail.Text != detail) _detail.Text = detail;
        var want = recording ? RecIcon ?? Icon : IdleIcon ?? Icon;
        if (!ReferenceEquals(Icon, want)) Icon = want;
    }
    public Icon IdleIcon, RecIcon;
}

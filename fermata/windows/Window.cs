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
        ClientSize = new Size(440, 200);
        BackColor = Color.FromArgb(0x1F, 0x1F, 0x1D);
        ForeColor = Color.FromArgb(0xEC, 0xEB, 0xE7);
        Font = new Font("Segoe UI", 10f);

        var title = new Label { Text = "Fermata för Windows", Font = new Font("Segoe UI Semibold", 13f), AutoSize = true, Location = new Point(16, 12) };
        _state = new Label { Font = new Font("Segoe UI Semibold", 11f), Location = new Point(16, 46), Size = new Size(408, 52), ForeColor = Color.FromArgb(0x5F, 0xAE, 0x97) };
        _detail = new Label { Location = new Point(16, 100), Size = new Size(408, 44), ForeColor = Color.FromArgb(0xA0, 0x9E, 0x98) };
        var open = new Button { Text = "Öppna Fermata-sidan", Location = new Point(16, 150), Size = new Size(200, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0x5F, 0xAE, 0x97), ForeColor = Color.FromArgb(0x0E, 0x1A, 0x16) };
        var exit = new Button { Text = "Avsluta", Location = new Point(324, 150), Size = new Size(100, 34), FlatStyle = FlatStyle.Flat };
        open.FlatAppearance.BorderSize = 0;
        open.Click += (_, _) => openPage();
        exit.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { title, _state, _detail, open, exit });

        // Bottom right of the screen, out of the way of the slides
        var wa = Screen.PrimaryScreen.WorkingArea;
        Load += (_, _) => Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);
        FormClosing += (_, e) =>
        {
            if (IsCapturing() && MessageBox.Show(this, "Bildspelet tas just nu. Avsluta Fermata ändå? Då slutar sändningen eller inspelningen.",
                    "Fermata", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
            quit();
        };
    }

    public void Show(string state, string detail, bool ok)
    {
        _state.Text = state;
        _state.ForeColor = ok ? Color.FromArgb(0x5F, 0xAE, 0x97) : Color.FromArgb(0xF2, 0xC4, 0x6B);
        _detail.Text = detail;
    }
}

// SettingsForm.cs - the settings window (tray icon click). Changes apply live.
// C# 5 / .NET Framework 4.x only (see build.bat).

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeymapOverlay
{
    class SettingsForm : Form
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "KeymapOverlay";

        readonly OverlayApp app;
        bool loading;

        RadioButton popupRadio, alwaysRadio;
        NumericUpDown hideAfter, linger, keySize;
        TrackBar opacity;
        Label opacityValue, popupHint;
        Button moveButton;
        TextBox keymapBox, layoutBox;
        CheckBox startup, swallow;

        public SettingsForm(OverlayApp app)
        {
            this.app = app;
            Text = "Keymap Overlay - Settings";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(10);
            ShowInTaskbar = true;
            TopMost = true;

            var root = Column();
            Controls.Add(root);

            // --- When to show
            popupRadio = new RadioButton { Text = "Pop up when I leave the base layer", AutoSize = true };
            alwaysRadio = new RadioButton { Text = "Always show", AutoSize = true };
            popupRadio.CheckedChanged += Changed;
            alwaysRadio.CheckedChanged += Changed;
            hideAfter = Num(0, 120, 0.5m, 1);
            linger = Num(0, 10, 0.25m, 2);
            popupHint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 6),
                Text = "Hide after 0 seconds = stay until you're back on the base layer." };
            var show = Group("When to show",
                popupRadio,
                alwaysRadio,
                Row(Lbl("Hide after"), hideAfter, Lbl("seconds (popup mode)")),
                popupHint,
                Row(Lbl("Keep showing for"), linger, Lbl("seconds after returning to base")));
            root.Controls.Add(show);

            // --- Appearance
            opacity = new TrackBar { Minimum = 15, Maximum = 100, TickFrequency = 5, SmallChange = 5, LargeChange = 10, Width = 200, AutoSize = false, Height = 32 };
            opacity.ValueChanged += Changed;
            opacityValue = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            keySize = Num(20, 100, 2, 0);
            root.Controls.Add(Group("Appearance",
                Row(Lbl("Opacity"), opacity, opacityValue),
                Row(Lbl("Key size"), keySize, Lbl("px"))));

            // --- Position
            moveButton = new Button { AutoSize = true };
            moveButton.Click += (s, e) => app.SetUnlocked(!app.Unlocked);
            var reset = new Button { Text = "Reset positions", AutoSize = true };
            reset.Click += (s, e) => app.ResetPositions();
            root.Controls.Add(Group("Position",
                Row(moveButton, reset),
                new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0),
                    Text = "Click \"Move windows\", then drag each half wherever you like (any monitor). " +
                           "Click \"Done moving\" when finished. Positions are remembered; if a monitor is " +
                           "disconnected, that half moves back to the main screen." }));
            SyncMoveButton();

            // --- Keymap source
            keymapBox = new TextBox { Width = 420 };
            layoutBox = new TextBox { Width = 420 };
            var reload = new Button { Text = "Reload keymap", AutoSize = true };
            reload.Click += (s, e) => { Push(); app.ReloadKeymap(); };
            root.Controls.Add(Group("Keymap (URL or file path)",
                Lbl("Keymap file (.keymap)"), keymapBox,
                Lbl("Layout file (.json)"), layoutBox,
                Row(reload, new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 3),
                    Text = "Re-downloads after you push keymap changes." })));

            // --- Other
            startup = new CheckBox { Text = "Start with Windows", AutoSize = true };
            startup.CheckedChanged += (s, e) => { if (!loading) SetStartup(startup.Checked); };
            swallow = new CheckBox { Text = "Hide the F13-F24 layer keys from other apps", AutoSize = true };
            swallow.CheckedChanged += Changed;
            root.Controls.Add(Group("Other", startup, swallow));

            // --- Buttons
            var openIni = new Button { Text = "Open settings file", AutoSize = true };
            openIni.Click += (s, e) => { try { Process.Start("notepad.exe", "\"" + Settings.FilePath + "\""); } catch { } };
            var close = new Button { Text = "Done", AutoSize = true, DialogResult = DialogResult.OK };
            close.Click += (s, e) => Close();
            AcceptButton = close;
            CancelButton = close;
            var buttons = Row(openIni, close);
            buttons.Anchor = AnchorStyles.Right;
            root.Controls.Add(buttons);

            LoadValues();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Apply edited keymap/layout paths that weren't reloaded explicitly.
            Push();
            base.OnFormClosing(e);
        }

        public void LoadValues()
        {
            loading = true;
            var s = app.CurrentSettings;
            alwaysRadio.Checked = s.AlwaysShow;
            popupRadio.Checked = !s.AlwaysShow;
            hideAfter.Value = Clamp((decimal)s.PopupSeconds, hideAfter);
            linger.Value = Clamp((decimal)s.LingerSeconds, linger);
            opacity.Value = Math.Max(opacity.Minimum, Math.Min(opacity.Maximum, (int)Math.Round(s.Opacity * 100)));
            opacityValue.Text = opacity.Value + "%";
            keySize.Value = Clamp(s.KeySize, keySize);
            keymapBox.Text = s.KeymapSource;
            layoutBox.Text = s.LayoutSource;
            swallow.Checked = s.SwallowLayerKeys;
            startup.Checked = IsStartup();
            UpdateEnabled();
            loading = false;
        }

        public void SyncMoveButton()
        {
            moveButton.Text = app.Unlocked ? "Done moving" : "Move windows";
        }

        void Changed(object sender, EventArgs e)
        {
            if (loading) return;
            // Ignore the "unchecked" half of a radio switch.
            var rb = sender as RadioButton;
            if (rb != null && !rb.Checked) return;
            opacityValue.Text = opacity.Value + "%";
            UpdateEnabled();
            Push();
        }

        void UpdateEnabled()
        {
            hideAfter.Enabled = linger.Enabled = popupRadio.Checked;
        }

        // Builds settings from the form and hands them to the app (applied live).
        void Push()
        {
            if (loading) return;
            var s = app.CurrentSettings.Clone();
            s.AlwaysShow = alwaysRadio.Checked;
            s.PopupSeconds = (double)hideAfter.Value;
            s.LingerSeconds = (double)linger.Value;
            s.Opacity = opacity.Value / 100.0;
            s.KeySize = (int)keySize.Value;
            s.SwallowLayerKeys = swallow.Checked;
            if (keymapBox.Text.Trim().Length > 0) s.KeymapSource = keymapBox.Text.Trim();
            if (layoutBox.Text.Trim().Length > 0) s.LayoutSource = layoutBox.Text.Trim();
            app.ApplySettings(s);
        }

        // ---- start with Windows (per-user Run key, no admin needed)

        static bool IsStartup()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't change the startup setting (maybe blocked by IT policy):\n" + ex.Message +
                    "\n\nAlternative: put a shortcut to KeymapOverlay.exe in the folder that opens with Win+R > shell:startup",
                    "Keymap Overlay");
                loading = true;
                startup.Checked = IsStartup();
                loading = false;
            }
        }

        // ---- layout helpers

        static FlowLayoutPanel Column()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        }

        static FlowLayoutPanel Row(params Control[] controls)
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
            p.Controls.AddRange(controls);
            return p;
        }

        static GroupBox Group(string title, params Control[] controls)
        {
            var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 4, 8, 6), Margin = new Padding(0, 0, 0, 8), MinimumSize = new Size(450, 0) };
            var col = Column();
            col.Controls.AddRange(controls);
            g.Controls.Add(col);
            return g;
        }

        static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        }

        NumericUpDown Num(decimal min, decimal max, decimal step, int decimals)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, Width = 70 };
            n.ValueChanged += Changed;
            return n;
        }

        static decimal Clamp(decimal v, NumericUpDown n)
        {
            return Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }
    }
}

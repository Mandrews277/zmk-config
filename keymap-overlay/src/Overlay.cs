// Overlay.cs - Windows UI: two semi-transparent, click-through, always-on-top
// windows (left + right half) that show the active ZMK layer.
//
// The keyboard firmware (src/layer_keys.c in this repo) taps F13 + <layer>
// whenever the layer changes; a per-user low-level keyboard hook (no admin
// needed) picks those up and swallows them so no other app sees them.
//
// C# 5 / .NET Framework 4.x only, so build.bat can compile it with the csc.exe
// that ships with Windows.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KeymapOverlay
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            bool isNew;
            using (var mutex = new Mutex(true, "KeymapOverlay-single-instance", out isNew))
            {
                if (!isNew)
                {
                    MessageBox.Show("Keymap Overlay is already running (see the tray icon).", "Keymap Overlay");
                    return;
                }
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new OverlayApp());
            }
        }
    }

    // ------------------------------------------------------------------ settings

    class Settings
    {
        public string KeymapSource = "https://raw.githubusercontent.com/Mandrews277/zmk-config/main/config/comet_right.keymap";
        public string LayoutSource = "https://raw.githubusercontent.com/Mandrews277/zmk-config/main/config/comet.json";
        public bool AlwaysShow = false;
        public double PopupSeconds = 0;
        public double LingerSeconds = 0.5;
        public double Opacity = 0.85;
        public int KeySize = 36;
        public int SplitGap = 160;
        public int BottomMargin = 60;
        public string FirstKey = "F13";
        public bool SwallowLayerKeys = true;
        public string LeftPos = "";   // "x,y" or blank = automatic
        public string RightPos = "";

        public static string Dir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static string FilePath { get { return Path.Combine(Dir, "keymap-overlay.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) { s.Save(); return s; }
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                try
                {
                    switch (k)
                    {
                        case "keymap": s.KeymapSource = v; break;
                        case "layout": s.LayoutSource = v; break;
                        case "mode": s.AlwaysShow = v.ToLowerInvariant() == "always"; break;
                        case "popupseconds": s.PopupSeconds = D(v); break;
                        case "lingerseconds": s.LingerSeconds = Math.Max(0, D(v)); break;
                        case "showbaseinpopup": if (B(v)) s.LingerSeconds = 1.5; break; // old name
                        case "opacity": s.Opacity = Math.Max(0.15, Math.Min(1.0, D(v))); break;
                        case "keysize": s.KeySize = Math.Max(16, int.Parse(v)); break;
                        case "splitgap": s.SplitGap = int.Parse(v); break;
                        case "bottommargin": s.BottomMargin = int.Parse(v); break;
                        case "firstkey": s.FirstKey = v.ToUpperInvariant(); break;
                        case "swallowlayerkeys": s.SwallowLayerKeys = B(v); break;
                        case "leftpos": s.LeftPos = v; break;
                        case "rightpos": s.RightPos = v; break;
                    }
                }
                catch { /* ignore bad values, keep default */ }
            }
            return s;
        }

        static double D(string v) { return double.Parse(v, CultureInfo.InvariantCulture); }
        static bool B(string v) { v = v.ToLowerInvariant(); return v == "true" || v == "1" || v == "yes" || v == "on"; }
        static string Bs(bool b) { return b ? "true" : "false"; }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("; Keymap Overlay settings. Easier: click the tray icon to open the Settings window.");
            sb.AppendLine("; keymap / layout: a URL or a file path (relative paths are next to the .exe).");
            sb.AppendLine("; URLs are cached in .\\cache so it still works offline.");
            sb.AppendLine("keymap=" + KeymapSource);
            sb.AppendLine("layout=" + LayoutSource);
            sb.AppendLine();
            sb.AppendLine("; popup  = show when you leave the base layer, hide when you return");
            sb.AppendLine("; always = always visible, showing the current layer");
            sb.AppendLine("mode=" + (AlwaysShow ? "always" : "popup"));
            sb.AppendLine("; popup mode: also hide after this many seconds (0 = stay while the layer is active)");
            sb.AppendLine("popupSeconds=" + PopupSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("; popup mode: keep showing for this many seconds after you return to the base layer");
            sb.AppendLine("lingerSeconds=" + LingerSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("opacity=" + Opacity.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("; key size in pixels at 100% scaling");
            sb.AppendLine("keySize=" + KeySize);
            sb.AppendLine("; automatic placement: space between the halves, distance from the bottom of the screen");
            sb.AppendLine("splitGap=" + SplitGap);
            sb.AppendLine("bottomMargin=" + BottomMargin);
            sb.AppendLine("; window positions (x,y) - set by tray > Unlock to move. Blank = automatic.");
            sb.AppendLine("leftPos=" + LeftPos);
            sb.AppendLine("rightPos=" + RightPos);
            sb.AppendLine();
            sb.AppendLine("; key the firmware sends for layer 0 (layer N = this + N). Must match CONFIG_ZMK_LAYER_KEYS_BASE_USAGE.");
            sb.AppendLine("firstKey=" + FirstKey);
            sb.AppendLine("; hide the layer keys from other apps");
            sb.AppendLine("swallowLayerKeys=" + Bs(SwallowLayerKeys));
            try { File.WriteAllText(FilePath, sb.ToString()); }
            catch { /* read-only folder: run with defaults */ }
        }

        public Settings Clone() { return (Settings)MemberwiseClone(); }

        public static Point? ParsePos(string s)
        {
            var p = s.Split(',');
            int x, y;
            if (p.Length == 2 && int.TryParse(p[0].Trim(), out x) && int.TryParse(p[1].Trim(), out y))
                return new Point(x, y);
            return null;
        }
    }

    // ------------------------------------------------------------------ app

    class OverlayApp : ApplicationContext
    {
        Settings settings;
        Keymap keymap;
        List<KeyPos> layout;
        HalfWindow left, right;
        NotifyIcon tray;
        KeyboardHook hook;
        System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
        ToolStripMenuItem alwaysItem, unlockItem, previewMenu;
        SettingsForm settingsForm;
        int currentLayer = 0;
        bool shown;
        bool unlocked;
        bool previewing;   // settings window open: keep the overlay visible

        public OverlayApp()
        {
            settings = Settings.Load();

            tray = new NotifyIcon { Icon = MakeIcon(), Text = "Keymap Overlay (click for settings)", Visible = true };
            tray.ContextMenuStrip = BuildMenu();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };

            hideTimer.Tick += (s, e) => { hideTimer.Stop(); if (!settings.AlwaysShow && !unlocked && !previewing) SetShown(false); };

            left = new HalfWindow(true);
            right = new HalfWindow(false);
            left.Moved += SavePositions;
            right.Moved += SavePositions;

            if (!LoadKeymap(true))
            {
                tray.ShowBalloonTip(5000, "Keymap Overlay", "Could not load the keymap - click the tray icon to check settings.", ToolTipIcon.Warning);
            }

            hook = new KeyboardHook(FirstVk(), OnLayerKey, settings.SwallowLayerKeys);

            // Docking/undocking the laptop: pull windows back onto a visible screen.
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            if (settings.AlwaysShow) SetShown(true);
            else tray.ShowBalloonTip(3000, "Keymap Overlay", "Running. Change layers on your keyboard to see your keymap. Click the tray icon for settings.", ToolTipIcon.Info);
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            // Raised on another thread.
            if (left.IsHandleCreated) left.BeginInvoke((Action)PlaceWindows);
        }

        int FirstVk()
        {
            int n;
            if (settings.FirstKey.StartsWith("F") && int.TryParse(settings.FirstKey.Substring(1), out n) && n >= 1 && n <= 24)
                return 0x70 + n - 1; // VK_F1 = 0x70
            return 0x7C; // VK_F13
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            var settingsItem = new ToolStripMenuItem("Settings...", null, (s, e) => OpenSettings());
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
            alwaysItem = new ToolStripMenuItem("Always show", null, (s, e) => ToggleAlways());
            alwaysItem.Checked = settings.AlwaysShow;
            previewMenu = new ToolStripMenuItem("Show layer");
            unlockItem = new ToolStripMenuItem("Move windows", null, (s, e) => SetUnlocked(!unlocked));
            m.Items.Add(settingsItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(alwaysItem);
            m.Items.Add(previewMenu);
            m.Items.Add(unlockItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Reload keymap", null, (s, e) => ReloadKeymap()));
            m.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitThread()));
            return m;
        }

        void RebuildPreviewMenu()
        {
            previewMenu.DropDownItems.Clear();
            if (keymap == null) return;
            for (int i = 0; i < keymap.Layers.Count; i++)
            {
                int idx = i;
                previewMenu.DropDownItems.Add(new ToolStripMenuItem(i + ": " + keymap.Layers[i].Name, null, (s, e) =>
                {
                    ShowLayer(idx, true);
                }));
            }
        }

        // ---- used by the settings window

        public Settings CurrentSettings { get { return settings; } }
        public bool Unlocked { get { return unlocked; } }

        void OpenSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(this);
            settingsForm.FormClosed += (s, e) =>
            {
                settingsForm = null;
                if (unlocked) SetUnlocked(false);
                previewing = false;
                ShowLayer(currentLayer, false);
            };
            previewing = true;
            hideTimer.Stop();
            SetShown(true);
            settingsForm.Show();
            settingsForm.Activate();
        }

        // Applies settings live. Window positions always come from the current
        // settings (they're changed by dragging, not by the form).
        public void ApplySettings(Settings s)
        {
            bool sizeChanged = s.KeySize != settings.KeySize;
            bool sourceChanged = s.KeymapSource != settings.KeymapSource || s.LayoutSource != settings.LayoutSource;
            s.LeftPos = settings.LeftPos;
            s.RightPos = settings.RightPos;
            settings = s;
            settings.Save();
            hook.FirstVk = FirstVk();
            hook.Swallow = settings.SwallowLayerKeys;
            alwaysItem.Checked = settings.AlwaysShow;
            if (sourceChanged) LoadKeymap(true);
            else if (sizeChanged && keymap != null)
            {
                left.Setup(keymap, layout, settings);
                right.Setup(keymap, layout, settings);
                PlaceWindows();
            }
            ShowLayer(currentLayer, false);
        }

        public bool ReloadKeymap()
        {
            bool ok = LoadKeymap(true);
            if (ok) tray.ShowBalloonTip(2000, "Keymap Overlay", "Loaded " + keymap.Layers.Count + " layers.", ToolTipIcon.Info);
            return ok;
        }

        public void ResetPositions()
        {
            settings.LeftPos = settings.RightPos = "";
            settings.Save();
            PlaceWindows();
        }

        public void SetUnlocked(bool on)
        {
            if (on == unlocked) return;
            unlocked = on;
            unlockItem.Checked = unlocked;
            left.SetClickThrough(!unlocked);
            right.SetClickThrough(!unlocked);
            if (unlocked) { hideTimer.Stop(); SetShown(true); }
            else { SavePositions(); ShowLayer(currentLayer, false); }
            if (settingsForm != null) settingsForm.SyncMoveButton();
        }

        // ----

        bool LoadKeymap(bool allowNetwork)
        {
            try
            {
                string kmText = Fetch(settings.KeymapSource, "keymap.keymap", allowNetwork);
                string loText = Fetch(settings.LayoutSource, "layout.json", allowNetwork);
                var km = Keymap.Parse(kmText);
                var lo = Keymap.ParseLayoutJson(loText);
                if (lo.Count < km.Layers[0].Bindings.Count)
                    throw new FormatException("Layout has " + lo.Count + " keys but the keymap has " + km.Layers[0].Bindings.Count + ".");
                keymap = km;
                layout = lo;
                left.Setup(keymap, layout, settings);
                right.Setup(keymap, layout, settings);
                RebuildPreviewMenu();
                PlaceWindows();
                ShowLayer(Math.Min(currentLayer, keymap.Layers.Count - 1), false);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not load keymap/layout:\n\n" + ex.Message + "\n\nKeymap: " + settings.KeymapSource + "\nLayout: " + settings.LayoutSource,
                    "Keymap Overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        // Reads a URL (caching it) or a local file.
        static string Fetch(string source, string cacheName, bool allowNetwork)
        {
            bool isUrl = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (!isUrl)
            {
                var path = Path.IsPathRooted(source) ? source : Path.Combine(Settings.Dir, source);
                return File.ReadAllText(path);
            }
            string cacheDir = Path.Combine(Settings.Dir, "cache");
            string cachePath = Path.Combine(cacheDir, cacheName);
            if (allowNetwork)
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var req = (HttpWebRequest)WebRequest.Create(source + (source.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks);
                    req.Timeout = 6000;
                    req.UserAgent = "keymap-overlay";
                    if (req.Proxy != null) req.Proxy.Credentials = CredentialCache.DefaultCredentials;
                    using (var resp = req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                    {
                        string text = sr.ReadToEnd();
                        try { Directory.CreateDirectory(cacheDir); File.WriteAllText(cachePath, text); } catch { }
                        return text;
                    }
                }
                catch (Exception)
                {
                    if (!File.Exists(cachePath)) throw;
                }
            }
            return File.ReadAllText(cachePath);
        }

        void PlaceWindows()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int gap = Scale(settings.SplitGap);
            int bottom = wa.Bottom - Scale(settings.BottomMargin);
            var lp = OnScreen(Settings.ParsePos(settings.LeftPos), left.Size);
            var rp = OnScreen(Settings.ParsePos(settings.RightPos), right.Size);
            int cx = wa.Left + wa.Width / 2;
            left.Location = lp ?? new Point(cx - gap / 2 - left.Width, bottom - left.Height);
            right.Location = rp ?? new Point(cx + gap / 2, bottom - right.Height);
        }

        // A saved position is only used if most of the window is on a connected screen.
        static Point? OnScreen(Point? p, Size size)
        {
            if (!p.HasValue) return null;
            var r = new Rectangle(p.Value, size);
            foreach (var sc in Screen.AllScreens)
            {
                var i = Rectangle.Intersect(sc.WorkingArea, r);
                if (i.Width >= size.Width / 2 && i.Height >= size.Height / 2) return p;
            }
            return null;
        }

        static int Scale(int px) { return (int)Math.Round(px * HalfWindow.DpiScale); }

        void SavePositions()
        {
            settings.LeftPos = left.Left + "," + left.Top;
            settings.RightPos = right.Left + "," + right.Top;
            settings.Save();
        }

        // Called from the keyboard hook (UI thread).
        void OnLayerKey(int layer)
        {
            if (keymap == null || layer >= keymap.Layers.Count) return;
            ShowLayer(layer, false);
        }

        void ShowLayer(int layer, bool preview)
        {
            int previous = currentLayer;
            currentLayer = layer;
            hideTimer.Stop();

            // Popup mode, back on base: keep showing the layer you just left for a moment.
            bool linger = layer == 0 && !preview && shown && previous != 0 && settings.LingerSeconds > 0 &&
                          !(settings.AlwaysShow || unlocked || previewing);
            if (!linger)
            {
                left.SetLayer(layer);
                right.SetLayer(layer);
            }

            if (settings.AlwaysShow || unlocked || previewing) { SetShown(true); return; }

            if (preview)
            {
                SetShown(true);
                StartHide(settings.PopupSeconds > 0 ? settings.PopupSeconds : 4);
            }
            else if (layer != 0)
            {
                SetShown(true);
                if (settings.PopupSeconds > 0) StartHide(settings.PopupSeconds);
            }
            else if (linger)
            {
                StartHide(settings.LingerSeconds);
            }
            else
            {
                SetShown(false);
            }
        }

        void StartHide(double seconds)
        {
            hideTimer.Interval = Math.Max(100, (int)(seconds * 1000));
            hideTimer.Start();
        }

        void SetShown(bool show)
        {
            shown = show;
            left.FadeTo(show ? settings.Opacity : 0);
            right.FadeTo(show ? settings.Opacity : 0);
        }

        void ToggleAlways()
        {
            var s = settings.Clone();
            s.AlwaysShow = !s.AlwaysShow;
            ApplySettings(s);
            if (settingsForm != null) settingsForm.LoadValues();
        }

        protected override void ExitThreadCore()
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            if (settingsForm != null) settingsForm.Close();
            hook.Dispose();
            tray.Visible = false;
            tray.Dispose();
            left.Close();
            right.Close();
            base.ExitThreadCore();
        }

        static Icon MakeIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = HalfWindow.Rounded(new RectangleF(1, 4, 30, 24), 6))
                using (var b = new SolidBrush(Color.FromArgb(137, 180, 250)))
                    g.FillPath(b, path);
                using (var dark = new SolidBrush(Color.FromArgb(30, 30, 46)))
                {
                    for (int r = 0; r < 2; r++)
                        for (int c = 0; c < 4; c++)
                            g.FillRectangle(dark, 5 + c * 6, 9 + r * 6, 4, 4);
                    g.FillRectangle(dark, 9, 21, 14, 3);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    // ------------------------------------------------------------------ window

    class HalfWindow : Form
    {
        public static double DpiScale = 1.0;
        public event Action Moved;

        static readonly Color Bg = Color.FromArgb(24, 24, 37);
        static readonly Color KeyFill = Color.FromArgb(49, 50, 68);
        static readonly Color KeyBorder = Color.FromArgb(69, 71, 90);
        static readonly Color LayerFill = Color.FromArgb(58, 73, 112);
        static readonly Color Text1 = Color.FromArgb(205, 214, 244);
        static readonly Color Text2 = Color.FromArgb(166, 173, 200);
        static readonly Color Accent = Color.FromArgb(137, 180, 250);

        readonly bool isLeft;
        Keymap keymap;
        List<KeyPos> layout;
        List<int> keys = new List<int>();   // key indices on this half
        double unit;                        // px per key unit
        PointF offset;                      // layout units -> window px
        int header;
        int layer;
        bool clickThrough = true;
        double targetOpacity;
        System.Windows.Forms.Timer fade = new System.Windows.Forms.Timer { Interval = 15 };

        static HalfWindow()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) DpiScale = g.DpiX / 96.0;
        }

        public HalfWindow(bool isLeft)
        {
            this.isLeft = isLeft;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Bg;
            DoubleBuffered = true;
            Opacity = 0;
            fade.Tick += (s, e) => StepFade();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                  WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
                if (clickThrough) cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        public void SetClickThrough(bool on)
        {
            clickThrough = on;
            if (!IsHandleCreated) return;
            int ex = GetWindowLong(Handle, -20);
            ex = on ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
            SetWindowLong(Handle, -20, ex);
            Cursor = on ? Cursors.Default : Cursors.SizeAll;
            Invalidate();
        }

        // Unlocked: drag anywhere on the window to move it.
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTCAPTION = 2, WM_EXITSIZEMOVE = 0x232, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && !clickThrough) m.Result = (IntPtr)HTCAPTION;
            if (m.Msg == WM_EXITSIZEMOVE && Moved != null) Moved();
        }

        public void Setup(Keymap km, List<KeyPos> lo, Settings s)
        {
            keymap = km;
            layout = lo;
            unit = s.KeySize * DpiScale;
            header = (int)Math.Round(22 * DpiScale);
            int n = km.Layers[0].Bindings.Count;

            // Split halves at the middle of the layout.
            double minX = double.MaxValue, maxX = double.MinValue;
            for (int i = 0; i < n; i++) { var c = Center(lo[i]); minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X); }
            double mid = (minX + maxX) / 2;
            keys.Clear();
            for (int i = 0; i < n; i++) if ((Center(lo[i]).X < mid) == isLeft) keys.Add(i);

            // Bounding box of the rotated keys (in px).
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (int i in keys)
                foreach (var p in Corners(lo[i]))
                {
                    x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y);
                    x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
                }
            float pad = (float)(8 * DpiScale);
            offset = new PointF(pad - x0, header + pad - y0);
            ClientSize = new Size((int)Math.Ceiling(x1 - x0 + 2 * pad), (int)Math.Ceiling(y1 - y0 + 2 * pad + header));
            using (var path = Rounded(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), (float)(12 * DpiScale)))
                Region = new Region(path);
            if (!Visible) Show();
            Invalidate();
        }

        static PointF Center(KeyPos k)
        {
            var pts = CornersU(k);
            return new PointF((pts[0].X + pts[2].X) / 2, (pts[0].Y + pts[2].Y) / 2);
        }

        // Corners in layout units after rotation.
        static PointF[] CornersU(KeyPos k)
        {
            var pts = new[] {
                new PointF((float)k.X, (float)k.Y), new PointF((float)(k.X + k.W), (float)k.Y),
                new PointF((float)(k.X + k.W), (float)(k.Y + k.H)), new PointF((float)k.X, (float)(k.Y + k.H)) };
            if (k.R != 0)
            {
                using (var m = RotationU(k)) m.TransformPoints(pts);
            }
            return pts;
        }

        static Matrix RotationU(KeyPos k)
        {
            var m = new Matrix();
            var origin = k.HasOrigin ? new PointF((float)k.Rx, (float)k.Ry)
                                     : new PointF((float)(k.X + k.W / 2), (float)(k.Y + k.H / 2));
            m.RotateAt((float)k.R, origin);
            return m;
        }

        PointF[] Corners(KeyPos k)
        {
            var pts = CornersU(k);
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF((float)(pts[i].X * unit), (float)(pts[i].Y * unit));
            return pts;
        }

        public void SetLayer(int l)
        {
            layer = l;
            Invalidate();
        }

        public void FadeTo(double target)
        {
            targetOpacity = target;
            if (target > 0 && !Visible) Show();
            fade.Start();
        }

        void StepFade()
        {
            double step = 0.08;
            double o = Opacity;
            if (Math.Abs(o - targetOpacity) <= step) { Opacity = targetOpacity; fade.Stop(); }
            else Opacity = o + (targetOpacity > o ? step : -step);
            if (Opacity > 0 && !TopMost) TopMost = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Bg);
            if (keymap == null || layer >= keymap.Layers.Count) return;
            var L = keymap.Layers[layer];

            // Header: layer name (+ number), and a move hint when unlocked.
            using (var hf = new Font("Segoe UI Semibold", 10f, FontStyle.Regular, GraphicsUnit.Point))
            using (var hb = new SolidBrush(Accent))
            {
                string title;
                if (isLeft) title = L.Name;
                else if (!clickThrough) title = "drag to move";
                else title = "layer " + layer + " / " + (keymap.Layers.Count - 1);
                var sf = new StringFormat { Alignment = isLeft ? StringAlignment.Near : StringAlignment.Far, LineAlignment = StringAlignment.Center };
                float pad = (float)(10 * DpiScale);
                g.DrawString(title, hf, hb, new RectangleF(pad, 2, ClientSize.Width - 2 * pad, header), sf);
            }

            float gap = (float)(2.5 * DpiScale);
            float radius = (float)(5 * DpiScale);
            foreach (int i in keys)
            {
                var k = layout[i];
                var label = i < L.Labels.Count ? L.Labels[i] : new KeyLabel { Empty = true };
                bool dim = false;
                if (label.Transparent)
                {
                    dim = true;
                    label = i < keymap.Layers[0].Labels.Count ? keymap.Layers[0].Labels[i] : new KeyLabel { Empty = true };
                    if (label.Transparent) label = new KeyLabel { Empty = true };
                }

                var state = g.Save();
                g.TranslateTransform(offset.X, offset.Y);
                if (k.R != 0)
                {
                    var origin = k.HasOrigin ? new PointF((float)(k.Rx * unit), (float)(k.Ry * unit))
                                             : new PointF((float)((k.X + k.W / 2) * unit), (float)((k.Y + k.H / 2) * unit));
                    g.TranslateTransform(origin.X, origin.Y);
                    g.RotateTransform((float)k.R);
                    g.TranslateTransform(-origin.X, -origin.Y);
                }
                var rect = new RectangleF((float)(k.X * unit) + gap, (float)(k.Y * unit) + gap,
                                          (float)(k.W * unit) - 2 * gap, (float)(k.H * unit) - 2 * gap);
                DrawKey(g, rect, radius, label, dim);
                g.Restore(state);
            }

            if (!clickThrough)
            {
                float w = (float)(2 * DpiScale);
                using (var pen = new Pen(Accent, w) { DashStyle = DashStyle.Dash })
                using (var path = Rounded(new RectangleF(w / 2, w / 2, ClientSize.Width - w, ClientSize.Height - w), (float)(12 * DpiScale)))
                    g.DrawPath(pen, path);
            }
        }

        void DrawKey(Graphics g, RectangleF r, float radius, KeyLabel label, bool dim)
        {
            Color fill = label.LayerKey && !dim ? LayerFill : KeyFill;
            int alpha = dim || label.Empty ? 110 : 255;
            using (var path = Rounded(r, radius))
            using (var b = new SolidBrush(Color.FromArgb(alpha, fill)))
            using (var p = new Pen(Color.FromArgb(alpha, label.LayerKey && !dim ? Accent : KeyBorder), (float)DpiScale))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            if (label.Empty) return;

            int ta = dim ? 95 : 255;
            bool hasHold = !string.IsNullOrEmpty(label.Hold);
            float inner = r.Width - 4;
            var tapRect = hasHold ? new RectangleF(r.X, r.Y, r.Width, r.Height * 0.66f) : r;
            using (var tb = new SolidBrush(Color.FromArgb(ta, Text1)))
                DrawFitted(g, label.Tap, tapRect, inner, label.Tap.Length <= 2 ? 11.5f : 9f, tb, FontStyle.Bold);
            if (hasHold)
            {
                var holdRect = new RectangleF(r.X, r.Y + r.Height * 0.58f, r.Width, r.Height * 0.40f);
                using (var hb = new SolidBrush(Color.FromArgb(dim ? 80 : 230, label.LayerKey && !dim ? Accent : Text2)))
                    DrawFitted(g, label.Hold, holdRect, inner, 6.5f, hb, FontStyle.Regular);
            }
        }

        static void DrawFitted(Graphics g, string text, RectangleF rect, float maxWidth, float pt, Brush brush, FontStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None };
            // Size relative to key size so it scales with keySize.
            float size = pt * (float)(rect.Width / (36 * DpiScale)) ;
            size = Math.Max(5.5f, size);
            for (int tries = 0; tries < 8; tries++)
            {
                using (var f = new Font("Segoe UI", size, style, GraphicsUnit.Point))
                {
                    var w = g.MeasureString(text, f, int.MaxValue, sf).Width;
                    if (w <= maxWidth || size <= 5.5f)
                    {
                        g.DrawString(text, f, brush, rect, sf);
                        return;
                    }
                }
                size = Math.Max(5.5f, size * 0.87f);
            }
        }

        public static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ------------------------------------------------------------------ hook

    // Per-user low-level keyboard hook (no admin rights needed).
    class KeyboardHook : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

        const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
        const uint LLKHF_INJECTED = 0x10;

        readonly HookProc proc;   // keep a reference so the GC doesn't collect it
        readonly IntPtr handle;
        readonly Action<int> onLayer;
        public int FirstVk;
        public bool Swallow;

        public KeyboardHook(int firstVk, Action<int> onLayer, bool swallow)
        {
            FirstVk = firstVk;
            Swallow = swallow;
            this.onLayer = onLayer;
            proc = Callback;
            handle = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
            if (handle == IntPtr.Zero)
                MessageBox.Show("Could not install the keyboard hook (error " + Marshal.GetLastWin32Error() + ").\nLayer changes won't be detected.", "Keymap Overlay");
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                int layer = (int)info.vkCode - FirstVk;
                if (layer >= 0 && layer < 12 && FirstVk + layer <= 0x87 && (info.flags & LLKHF_INJECTED) == 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) onLayer(layer);
                    if (Swallow) return (IntPtr)1;
                }
            }
            return CallNextHookEx(handle, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) UnhookWindowsHookEx(handle);
        }
    }
}

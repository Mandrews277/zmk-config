// Keymap.cs - parses a ZMK .keymap and a QMK-style layout .json into
// something the overlay can draw. No WinForms dependencies (testable anywhere).
//
// Written in C# 5 so it compiles with the csc.exe that ships with Windows
// (.NET Framework 4.x) - no SDK or install needed.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KeymapOverlay
{
    public class KeyLabel
    {
        public string Tap = "";      // big centre label
        public string Hold = "";     // small label under it (hold-tap / mod-tap)
        public bool Transparent;     // &trans - drawn dimmed, resolved from base layer
        public bool Empty;           // &none / nothing
        public bool LayerKey;        // switches layers - drawn highlighted
    }

    public class Layer
    {
        public string Id = "";
        public string Name = "";
        public List<string> Bindings = new List<string>();
        public List<KeyLabel> Labels = new List<KeyLabel>();
    }

    public class KeyPos
    {
        public double X, Y, W = 1, H = 1, R, Rx, Ry;
        public bool HasOrigin;
    }

    public class HoldTapDef
    {
        public string Hold = "kp";
        public string Tap = "kp";
    }

    public class Keymap
    {
        public List<Layer> Layers = new List<Layer>();
        public Dictionary<string, HoldTapDef> HoldTaps = new Dictionary<string, HoldTapDef>();

        // ------------------------------------------------------------ parsing

        public static Keymap Parse(string text)
        {
            var km = new Keymap();
            text = StripComments(text);

            // Custom hold-taps:  name: label { compatible = "zmk,behavior-hold-tap"; bindings = <&mo>, <&kp>; ... };
            foreach (Match m in Regex.Matches(text, @"(?:[\w-]+\s*:\s*)?([\w-]+)\s*\{([^{}]*zmk,behavior-hold-tap[^{}]*)\}"))
            {
                var body = m.Groups[2].Value;
                var bm = Regex.Match(body, @"\bbindings\s*=\s*<\s*&([\w-]+)\s*>\s*,\s*<\s*&([\w-]+)\s*>");
                if (!bm.Success) continue;
                var def = new HoldTapDef { Hold = bm.Groups[1].Value, Tap = bm.Groups[2].Value };
                km.HoldTaps[m.Groups[1].Value] = def;
                // also register the node label (the "Layer_tap:" part) - that's what &refs use
                var lm = Regex.Match(m.Value, @"^([\w-]+)\s*:");
                if (lm.Success) km.HoldTaps[lm.Groups[1].Value] = def;
            }

            // keymap { compatible = "zmk,keymap"; layer { bindings = < ... >; }; ... };
            int ki = IndexOfNode(text, "zmk,keymap");
            if (ki < 0) throw new FormatException("No node with compatible = \"zmk,keymap\" found.");
            string block = ExtractBraceBlock(text, ki);

            foreach (Match m in Regex.Matches(block, @"([\w-]+)\s*\{([^{}]*)\}"))
            {
                var body = m.Groups[2].Value;
                var bm = Regex.Match(body, @"\bbindings\s*=\s*<([^>]*)>", RegexOptions.Singleline);
                if (!bm.Success) continue;
                var layer = new Layer { Id = m.Groups[1].Value };
                var dn = Regex.Match(body, "\\b(?:display-name|label)\\s*=\\s*\"([^\"]*)\"");
                layer.Name = dn.Success ? dn.Groups[1].Value : PrettyLayerName(layer.Id);
                layer.Bindings = SplitBindings(bm.Groups[1].Value);
                km.Layers.Add(layer);
            }
            if (km.Layers.Count == 0) throw new FormatException("Keymap node has no layers with bindings.");

            foreach (var l in km.Layers)
                foreach (var b in l.Bindings)
                    l.Labels.Add(km.LabelFor(b));
            return km;
        }

        static string StripComments(string s)
        {
            s = Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            s = Regex.Replace(s, @"//[^\n]*", "");
            return s;
        }

        static int IndexOfNode(string text, string compatible)
        {
            int c = text.IndexOf("\"" + compatible + "\"", StringComparison.Ordinal);
            if (c < 0) return -1;
            return text.LastIndexOf('{', c);
        }

        // Returns the inside of the {...} starting at text[open] == '{'.
        static string ExtractBraceBlock(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return text.Substring(open + 1, i - open - 1);
            }
            return text.Substring(open + 1);
        }

        static List<string> SplitBindings(string s)
        {
            var list = new List<string>();
            foreach (var part in s.Split('&'))
            {
                var t = Regex.Replace(part, @"\s+", " ").Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        public static string PrettyLayerName(string id)
        {
            var s = Regex.Replace(id, @"(?i)[_-]?layer$", "");
            s = Regex.Replace(s, @"(?i)^layer[_-]?", "");
            s = s.Replace('_', ' ').Replace('-', ' ').Trim();
            if (s.Length == 0) s = id;
            if (s.ToLowerInvariant() == "default") s = "Base";
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        }

        public string LayerName(string param)
        {
            int n;
            if (int.TryParse(param, out n) && n >= 0 && n < Layers.Count) return Layers[n].Name;
            return param;
        }

        // ------------------------------------------------------------ labels

        public KeyLabel LabelFor(string binding)
        {
            var parts = binding.Split(' ');
            string beh = parts[0];
            string p1 = parts.Length > 1 ? parts[1] : "";
            string p2 = parts.Length > 2 ? parts[2] : "";
            var k = new KeyLabel();

            switch (beh)
            {
                case "trans": k.Transparent = true; return k;
                case "none": k.Empty = true; return k;
                case "kp": k.Tap = Key(p1); return k;
                case "mt": k.Tap = Key(p2); k.Hold = Key(p1); return k;
                case "lt": k.Tap = Key(p2); k.Hold = LayerName(p1); k.LayerKey = true; return k;
                case "mo": k.Tap = LayerName(p1); k.Hold = "hold"; k.LayerKey = true; return k;
                case "to": k.Tap = "\u2192 " + LayerName(p1); k.LayerKey = true; return k;
                case "tog": k.Tap = "\u21C4 " + LayerName(p1); k.LayerKey = true; return k;
                case "sl": k.Tap = LayerName(p1); k.Hold = "one-shot"; k.LayerKey = true; return k;
                case "sk": k.Tap = Key(p1); k.Hold = "sticky"; return k;
                case "caps_word": k.Tap = "Caps"; k.Hold = "word"; return k;
                case "key_repeat": k.Tap = "Repeat"; return k;
                case "sys_reset": k.Tap = "Reset"; return k;
                case "bootloader": k.Tap = "Boot"; return k;
                case "soft_off": k.Tap = "Off"; return k;
                case "studio_unlock": k.Tap = "Studio"; return k;
                case "gresc": k.Tap = "Esc"; k.Hold = "` ~"; return k;
                case "bt": k.Tap = Bt(p1, p2); return k;
                case "out": k.Tap = Out(p1); return k;
                case "mkp": k.Tap = Mouse(p1); return k;
                case "mmv": k.Tap = "Mouse"; k.Hold = Dir(p1); return k;
                case "msc": k.Tap = "Scroll"; k.Hold = Dir(p1); return k;
                case "ext_power": k.Tap = "ExtPwr"; k.Hold = Tidy(p1.Replace("EP_", "")); return k;
                case "rgb_ug": k.Tap = "RGB"; k.Hold = Tidy(p1.Replace("RGB_", "")); return k;
                case "bl": k.Tap = "BL"; k.Hold = Tidy(p1.Replace("BL_", "")); return k;
            }

            HoldTapDef ht;
            if (HoldTaps.TryGetValue(beh, out ht))
            {
                var hold = LabelFor(ht.Hold + " " + p1);
                var tap = LabelFor(ht.Tap + " " + p2);
                k.Tap = tap.Tap;
                k.Hold = hold.Tap;
                if (ht.Tap == "tog") k.Tap = "\u21C4 " + LayerName(p2);
                k.LayerKey = hold.LayerKey || tap.LayerKey;
                return k;
            }

            // Unknown behavior (macro, custom, ...): show its name and first param.
            k.Tap = Tidy(beh);
            if (p1.Length > 0) k.Hold = Key(p1);
            return k;
        }

        static string Bt(string p1, string p2)
        {
            switch (p1)
            {
                case "BT_CLR": return "BT Clr";
                case "BT_CLR_ALL": return "BT Clr All";
                case "BT_NXT": return "BT \u25B6";
                case "BT_PRV": return "BT \u25C0";
                case "BT_SEL": return "BT " + p2;
                case "BT_DISC": return "BT Disc " + p2;
            }
            return Tidy(p1);
        }

        static string Out(string p1)
        {
            switch (p1)
            {
                case "OUT_TOG": return "USB/BT";
                case "OUT_USB": return "USB";
                case "OUT_BLE": return "BT";
            }
            return Tidy(p1);
        }

        static string Mouse(string p)
        {
            switch (p)
            {
                case "LCLK": case "MB1": return "L Click";
                case "RCLK": case "MB2": return "R Click";
                case "MCLK": case "MB3": return "M Click";
                case "MB4": return "Back";
                case "MB5": return "Fwd";
            }
            return Tidy(p);
        }

        static string Dir(string p)
        {
            if (p.Contains("UP")) return "\u2191";
            if (p.Contains("DOWN")) return "\u2193";
            if (p.Contains("LEFT")) return "\u2190";
            if (p.Contains("RIGHT")) return "\u2192";
            return Tidy(p);
        }

        static readonly Dictionary<string, string> Mods = new Dictionary<string, string>
        {
            {"LS","\u21E7"},{"RS","\u21E7"},{"LC","Ctl+"},{"RC","Ctl+"},
            {"LA","Alt+"},{"RA","AltGr+"},{"LG","Win+"},{"RG","Win+"},
        };

        // Turns a ZMK keycode (e.g. LEFT_SHIFT, N1, LS(A), C_VOL_UP) into a short label.
        public static string Key(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            var m = Regex.Match(code, @"^(LS|RS|LC|RC|LA|RA|LG|RG)\((.*)\)$");
            if (m.Success) return Mods[m.Groups[1].Value] + Key(m.Groups[2].Value);

            string v;
            if (Names.TryGetValue(code, out v)) return v;

            var n = Regex.Match(code, @"^(?:N|NUMBER_|KP_N|KP_NUMBER_)(\d)$");
            if (n.Success) return n.Groups[1].Value;
            var f = Regex.Match(code, @"^F(\d+)$");
            if (f.Success) return code;
            if (Regex.IsMatch(code, @"^[A-Z]$")) return code;
            return Tidy(code);
        }

        static string Tidy(string s)
        {
            s = Regex.Replace(s, @"^(C_|K_|KP_)", "");
            s = s.Replace('_', ' ').Trim();
            s = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
            return s.Length > 10 ? s.Substring(0, 10) : s;
        }

        static readonly Dictionary<string, string> Names = BuildNames();

        static Dictionary<string, string> BuildNames()
        {
            var d = new Dictionary<string, string>();
            Action<string, string[]> add = (label, keys) => { foreach (var k in keys) d[k] = label; };
            add("Esc", new[] { "ESCAPE", "ESC" });
            add("Enter", new[] { "ENTER", "RETURN", "RET", "RETURN2", "RET2" });
            add("Space", new[] { "SPACE", "SPC" });
            add("Tab", new[] { "TAB" });
            add("Bksp", new[] { "BACKSPACE", "BSPC" });
            add("Del", new[] { "DELETE", "DEL" });
            add("Ins", new[] { "INSERT", "INS" });
            add("Home", new[] { "HOME" });
            add("End", new[] { "END" });
            add("PgUp", new[] { "PAGE_UP", "PG_UP" });
            add("PgDn", new[] { "PAGE_DOWN", "PG_DN" });
            add("\u2191", new[] { "UP_ARROW", "UP" });
            add("\u2193", new[] { "DOWN_ARROW", "DOWN" });
            add("\u2190", new[] { "LEFT_ARROW", "LEFT" });
            add("\u2192", new[] { "RIGHT_ARROW", "RIGHT" });
            add("Shift", new[] { "LEFT_SHIFT", "LSHIFT", "LSHFT", "RIGHT_SHIFT", "RSHIFT", "RSHFT" });
            add("Ctrl", new[] { "LEFT_CONTROL", "LCTRL", "LCTL", "RIGHT_CONTROL", "RCTRL", "RCTL" });
            add("Alt", new[] { "LEFT_ALT", "LALT" });
            add("AltGr", new[] { "RIGHT_ALT", "RALT" });
            add("Win", new[] { "LEFT_WIN", "LWIN", "LEFT_GUI", "LGUI", "LEFT_META", "LMETA", "LEFT_COMMAND", "LCMD",
                               "RIGHT_WIN", "RWIN", "RIGHT_GUI", "RGUI", "RIGHT_META", "RMETA", "RIGHT_COMMAND", "RCMD" });
            add("Caps", new[] { "CAPSLOCK", "CAPS", "CLCK" });
            add("Menu", new[] { "K_APPLICATION", "K_APP", "K_CONTEXT_MENU", "K_CMENU" });
            add("PrtSc", new[] { "PRINTSCREEN", "PSCRN" });
            add("ScrLk", new[] { "SCROLLLOCK", "SLCK" });
            add("Pause", new[] { "PAUSE_BREAK", "PAUSE" });
            add("NumLk", new[] { "KP_NUMLOCK", "KP_NUM", "KP_NLCK" });

            add("-", new[] { "MINUS", "KP_MINUS", "KP_SUBTRACT" });
            add("=", new[] { "EQUAL", "KP_EQUAL" });
            add("+", new[] { "PLUS", "KP_PLUS" });
            add("*", new[] { "ASTERISK", "ASTRK", "STAR", "KP_MULTIPLY", "KP_ASTERISK" });
            add("/", new[] { "SLASH", "FSLH", "KP_DIVIDE", "KP_SLASH" });
            add("\\", new[] { "BACKSLASH", "BSLH", "NON_US_BACKSLASH", "NUBS" });
            add("|", new[] { "PIPE", "PIPE2" });
            add("[", new[] { "LEFT_BRACKET", "LBKT" });
            add("]", new[] { "RIGHT_BRACKET", "RBKT" });
            add("{", new[] { "LEFT_BRACE", "LBRC" });
            add("}", new[] { "RIGHT_BRACE", "RBRC" });
            add("(", new[] { "LEFT_PARENTHESIS", "LPAR", "KP_LEFT_PARENTHESIS", "KP_LPAR" });
            add(")", new[] { "RIGHT_PARENTHESIS", "RPAR", "KP_RIGHT_PARENTHESIS", "KP_RPAR" });
            add("<", new[] { "LESS_THAN", "LT" });
            add(">", new[] { "GREATER_THAN", "GT" });
            add(";", new[] { "SEMICOLON", "SEMI" });
            add(":", new[] { "COLON" });
            add("'", new[] { "SINGLE_QUOTE", "SQT", "APOSTROPHE", "APOS" });
            add("\"", new[] { "DOUBLE_QUOTES", "DQT" });
            add(",", new[] { "COMMA", "KP_COMMA" });
            add(".", new[] { "PERIOD", "DOT", "KP_DOT" });
            add("`", new[] { "GRAVE" });
            add("~", new[] { "TILDE", "TILDE2" });
            add("!", new[] { "EXCLAMATION", "EXCL" });
            add("@", new[] { "AT_SIGN", "AT" });
            add("#", new[] { "HASH", "POUND" });
            add("$", new[] { "DOLLAR", "DLLR" });
            add("%", new[] { "PERCENT", "PRCNT" });
            add("^", new[] { "CARET" });
            add("&", new[] { "AMPERSAND", "AMPS" });
            add("_", new[] { "UNDERSCORE", "UNDER" });
            add("?", new[] { "QUESTION", "QMARK" });
            add("Enter", new[] { "KP_ENTER" });

            add("Vol+", new[] { "C_VOLUME_UP", "C_VOL_UP", "K_VOLUME_UP", "K_VOL_UP" });
            add("Vol\u2212", new[] { "C_VOLUME_DOWN", "C_VOL_DN", "K_VOLUME_DOWN", "K_VOL_DN" });
            add("Mute", new[] { "C_MUTE", "K_MUTE" });
            add("Play", new[] { "C_PLAY_PAUSE", "C_PP", "K_PLAY_PAUSE", "K_PP" });
            add("Next", new[] { "C_NEXT", "K_NEXT" });
            add("Prev", new[] { "C_PREVIOUS", "C_PREV", "K_PREVIOUS", "K_PREV" });
            add("Stop", new[] { "C_STOP", "K_STOP" });
            add("Bri+", new[] { "C_BRIGHTNESS_INC", "C_BRI_UP", "C_BRI_INC" });
            add("Bri\u2212", new[] { "C_BRIGHTNESS_DEC", "C_BRI_DN", "C_BRI_DEC" });
            return d;
        }

        // ------------------------------------------------------------ layout

        // Parses "layout": [ { "x": .., "y": .., "r": .., "rx": .., "ry": .. }, ... ]
        // from a QMK/ZMK info json (e.g. config/comet.json).
        public static List<KeyPos> ParseLayoutJson(string json)
        {
            var list = new List<KeyPos>();
            foreach (Match m in Regex.Matches(json, @"\{[^{}\[\]]*""x""[^{}\[\]]*\}"))
            {
                var o = m.Value;
                var k = new KeyPos();
                k.X = Num(o, "x", 0); k.Y = Num(o, "y", 0);
                k.W = Num(o, "w", 1); k.H = Num(o, "h", 1);
                k.R = Num(o, "r", 0);
                k.HasOrigin = Regex.IsMatch(o, @"""rx""") || Regex.IsMatch(o, @"""ry""");
                k.Rx = Num(o, "rx", 0); k.Ry = Num(o, "ry", 0);
                list.Add(k);
            }
            return list;
        }

        static double Num(string obj, string name, double def)
        {
            var m = Regex.Match(obj, "\"" + name + "\"\\s*:\\s*(-?[0-9.]+)");
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : def;
        }
    }
}

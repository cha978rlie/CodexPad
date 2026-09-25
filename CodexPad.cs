using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CodexPad
{
    public sealed class Binding
    {
        public string Input { get; set; } = "none";
        public string Action { get; set; } = "none";
        public string Target { get; set; } = "";
        public string Destination { get; set; } = "codex";
    }

    public sealed class Config
    {
        public int Version { get; set; } = 1;
        public bool ShowTaskNotice { get; set; } = false;
        public List<Binding> Bindings { get; set; } = new List<Binding>();
        public bool SendWithControlEnter { get; set; } = false;
        public bool AutoStartWithWindows { get; set; } = false;
        public bool ActivateOnLaunch { get; set; } = false;
        public bool CompletionLed { get; set; } = false;
        public int NotificationLedMode { get; set; } = 1;
    }

    internal sealed class Choice
    {
        public string Id { get; }
        public string Label { get; }
        public uint VirtualKey { get; }
        public Choice(string id, string label, uint key = 0) { Id = id; Label = label; VirtualKey = key; }
        public override string ToString() { return Label; }
    }

    internal static class Native
    {
        public const int HotkeyMessage = 0x312;
        public const uint NoRepeat = 0x4000;
        public const uint KeyUp = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct WindowPlacement { public uint Length, Flags, ShowCommand; public Point MinPosition, MaxPosition; public Rect NormalPosition; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        public static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        public delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    }

    public sealed partial class PadForm : Form
    {
        private readonly string configPath;
        private readonly ComboBox[] inputBoxes = new ComboBox[6];
        private readonly ComboBox[] actionBoxes = new ComboBox[6];
        private readonly TextBox[] targetBoxes = new TextBox[6];
        private readonly Button[] testButtons = new Button[6];
        private readonly ListBox log = new ListBox();
        private readonly Label state = new Label();
        private readonly CheckBox controlEnter = new CheckBox();
        private readonly CheckBox autoStart = new CheckBox();
        private readonly CheckBox activateOnLaunch = new CheckBox();
        private readonly CheckBox completionLed = new CheckBox();
        private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
        private bool statusBusy;
        private string lastStatusError;
        private readonly Button startButton = new Button();
        private readonly Button stopButton = new Button();
        private readonly Label deviceState = new Label();
        private bool deviceBusy;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly List<int> registered = new List<int>();
        private IntPtr lastChatWindow = IntPtr.Zero;
        private readonly System.Windows.Forms.Timer windowTimer = new System.Windows.Forms.Timer();
        private Config config = Defaults(true);
        private bool exitRequested;

        private static readonly List<Choice> Inputs = MakeInputs();
        private static readonly List<Choice> Actions = MakeActions();

        public PadForm(string path) : this(path, false) { }

        public PadForm(string path, bool startedByWindows, bool preview = false)
        {
            previewMode = preview;
            configPath = path;
            try { config = File.Exists(path) ? SettingsStore.Read(path) : Defaults(true); ValidateConfig(config); }
            catch (Exception error) { config = Defaults(true); config.ActivateOnLaunch = false; initialConfigError = error.Message; }
            BuildUi();
            ApplyConfig();
            if (!preview && initialConfigError == null && File.Exists(path)) {
                using var old = JsonDocument.Parse(File.ReadAllText(path));
                if (!old.RootElement.TryGetProperty("Version", out var version) || version.GetInt32() < 2)
                    SettingsStore.Write(path, config);
            }
            windowTimer.Interval = 400;
            windowTimer.Tick += (sender, args) => {
                IntPtr front = Native.GetForegroundWindow();
                if (IsUsableChatWindow(front)) lastChatWindow = front;
            };
            windowTimer.Start();
            Shown += (sender, args) => {
                if (previewMode) return;
                BeginInvoke(new Action(PreparePages));
                if (initialConfigError != null) Note("Einstellungen nicht geladen; Original unverändert: " + initialConfigError);
                if (config.ActivateOnLaunch) StartListening(false);
                statusTimer.Interval = 3000;
                statusTimer.Tick += async (s, e) => await RefreshCompletionLed();
                statusTimer.Start();
                if (startedByWindows) WindowState = FormWindowState.Minimized;
                _ = RefreshCompletionLed();
            };
        }

        public static string SelfTest(string path)
        {
            Config c = LoadConfig(path);
            if (c.Bindings.Count != 6) throw new InvalidDataException("Sechs Belegungsplaetze erwartet.");
            return "OK: Programmcode und Konfiguration geladen; sechs Belegungsplaetze vorhanden.";
        }

        public string SelfTestHotkeys()
        {
            int expected = config.Bindings.Count(binding => binding.Input != "none");
            StartListening(false);
            int count = registered.Count;
            StopListening();
            if (count != expected) throw new InvalidOperationException("Erwartet wurden " + expected + " angemeldete Tasten, gefunden: " + count);
            return "OK: " + count + " systemweite Tasten im Programm angemeldet und wieder freigegeben.";
        }

        private static Config Defaults(bool device)
        {
            string[] keys = device
                ? new[] { "F13", "F14", "F15", "F16", "F17", "F18" }
                : new[] { "7", "8", "9", "none", "none", "none" };
            string[] actions = device
                ? new[] { "cycle", "dictation", "enter", "chatScrollUp", "chatScrollDown", "effortMenu" }
                : new[] { "recent1", "dictation", "send", "none", "none", "none" };
            var result = new Config { Version = 2 };
            for (int i = 0; i < 6; i++) result.Bindings.Add(new Binding { Input = keys[i], Action = actions[i] });
            return result;
        }

        private static Config LoadConfig(string path)
        {
            try {
                if (!File.Exists(path)) return Defaults(true);
                Config c = JsonSerializer.Deserialize<Config>(File.ReadAllText(path, Encoding.UTF8));
                if (c == null || c.Bindings == null) return Defaults(true);
                while (c.Bindings.Count < 6) c.Bindings.Add(new Binding());
                if (c.Bindings.Count > 6) c.Bindings = c.Bindings.Take(6).ToList();
                return c;
            } catch { return Defaults(true); }
        }

        private static List<Choice> MakeInputs()
        {
            var result = new List<Choice> { new Choice("none", "— keine —") };
            for (int i = 0; i <= 9; i++) result.Add(new Choice(i.ToString(), i.ToString() + " (Ziffernreihe)", (uint)(0x30 + i)));
            for (int i = 0; i <= 9; i++) result.Add(new Choice("Num" + i, "NumPad " + i, (uint)(0x60 + i)));
            for (int i = 13; i <= 24; i++) result.Add(new Choice("F" + i, "F" + i, (uint)(0x7C + i - 13)));
            return result;
        }

        private static List<Choice> MakeActions()
        {
            var result = new List<Choice> {
                new Choice("none", "— keine Aktion —"),
                new Choice("shortcut", "Eigenes Tastenkürzel"),
                new Choice("scrollUp", "Mausrad: nach oben scrollen"),
                new Choice("scrollDown", "Mausrad: nach unten scrollen"),
                new Choice("chatScrollUp", "Codex-Chat: nach oben scrollen"),
                new Choice("chatScrollDown", "Codex-Chat: nach unten scrollen"),
                new Choice("cycle", "Aufgaben durchschalten"),
                new Choice("focus", "Codex nach vorn holen"),
                new Choice("latest", "Neueste Aufgabe öffnen"),
                new Choice("thread", "Bestimmte Aufgabe öffnen"),
                new Choice("dictation", "Diktat ein-/ausschalten"),
                new Choice("voice", "Sprachchat starten"),
                new Choice("send", "Nachricht sicher absenden"),
                new Choice("enter", "Enter senden"),
                new Choice("effortDown", "F19 senden"),
                new Choice("effortUp", "F20 senden"),
                new Choice("effortMenu", "F21 senden"),
                new Choice("attention", "Nächster Chat mit Handlungsbedarf"),
                new Choice("previous", "Vorheriger Chat oder Tab"),
                new Choice("next", "Nächster Chat oder Tab"),
                new Choice("sidebar", "Seitenleiste umschalten"),
                new Choice("new", "Neuen Chat öffnen"),
                new Choice("settings", "Codex-Einstellungen öffnen"),
                new Choice("skills", "Skills öffnen")
            };
            for (int i = 1; i <= 6; i++) result.Add(new Choice("recent" + i, "Letzte Chats: Platz " + i));
            return result;
        }

        private Button NewButton(string text, int x, int y, int width)
        {
            var button = new Button { Text = text };
            button.SetBounds(x, y, width, 34);
            Controls.Add(button);
            return button;
        }

        private static ComboBox NewCombo(List<Choice> choices)
        {
            var box = new PadCombo {
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(4, 6, 4, 5), BackColor = Color.White, ForeColor = Color.Black
            };
            foreach (Choice choice in choices) box.Items.Add(choice);
            return box;
        }

        private static void Select(ComboBox box, string id)
        {
            for (int i = 0; i < box.Items.Count; i++) {
                if (((Choice)box.Items[i]).Id == id) { box.SelectedIndex = i; return; }
            }
            box.SelectedIndex = 0;
        }

        private void UpdateWindowsAutoStart()
        {
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if (run == null) throw new InvalidOperationException("Windows-Autostart-Einstellungen sind nicht erreichbar.");
                string program = Process.GetCurrentProcess().MainModule.FileName;
                if (config.AutoStartWithWindows)
                    run.SetValue("CodexPad", "\"" + program + "\" --autostart", RegistryValueKind.String);
                else run.DeleteValue("CodexPad", false);
            }
            AutoStart.Update(config.AutoStartWithWindows, Process.GetCurrentProcess().MainModule.FileName);
        }

        private void StopListening()
        {
            foreach (int id in registered) Native.UnregisterHotKey(Handle, id);
            registered.Clear();
            state.Text = "Pausiert. Tasten sind wieder frei.";
            activeState.Text = "Tasten pausiert · LED-Automatik bleibt unabhängig aktiv";
            actionQueue.Clear();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == ShowAppMessage) RestoreWindow();
            if (message.Msg == 0x219 || message.Msg == 0x218) {
                deviceGeneration++; appliedLed = null;
            }
            if (message.Msg == Native.HotkeyMessage && !recording) {
                int row = (int)message.WParam - 100;
                if (row >= 0 && row < 6 && registered.Contains(100 + row)) {
                    var binding = config.Bindings[row];
                    IntPtr target = Native.GetForegroundWindow();
                    BeginInvoke(new Action(() => QueueAction(binding, target)));
                }
            }
            base.WndProc(ref message);
        }

        private static void WaitForRelease(uint key)
        {
            if (key == 0) return;
            for (int i = 0; i < 50; i++) {
                if ((((int)Native.GetAsyncKeyState((int)key)) & 0x8000) == 0) return;
                Thread.Sleep(20);
            }
        }

        private void ExecuteLegacy(Binding binding)
        {
            try {
                if (binding.Action == "none") return;
                if (binding.Action == "thread") {
                    OpenLink(ThreadLink(binding.Target));
                    Thread.Sleep(250);
                    if (!IsChatWindow(Native.GetForegroundWindow()) && FocusChatGpt() == IntPtr.Zero)
                        Note("Chat-Link geöffnet; Codex konnte nicht nach vorn geholt werden.");
                    else Note("Chat-Link geöffnet und Codex nach vorn geholt.");
                    return;
                }
                if (binding.Action == "new" || binding.Action == "settings" || binding.Action == "skills") {
                    string link = binding.Action == "new" ? "codex://threads/new" :
                                  binding.Action == "settings" ? "codex://settings" : "codex://skills";
                    OpenLink(link);
                    Note("Codex-Link geöffnet: " + binding.Action);
                    return;
                }
                IntPtr window = FocusChatGpt();
                if (window == IntPtr.Zero) {
                    Note("Codex-Fenster nicht gefunden oder Windows hat den Fokuswechsel verhindert.");
                    return;
                }
                switch (binding.Action) {
                    case "focus": Note("Codex ist im Vordergrund."); return;
                    case "effortDown": SendComboTo(window, 0x82); Note("Regler links: F19 gesendet."); return;
                    case "effortUp": SendComboTo(window, 0x83); Note("Regler rechts: F20 gesendet."); return;
                    case "effortMenu": SendComboTo(window, 0x84); Note("Regler drücken: F21 gesendet."); return;
                    case "dictation": StartDictation(window); return;
                    case "voice": SendComboTo(window, 0x11, 0x10, 0x56); Note("Sprachchat-Kürzel gesendet."); return;
                    case "sidebar": SendComboTo(window, 0x11, 0x42); Note("Seitenleiste umgeschaltet."); return;
                    case "attention": SendComboTo(window, 0x11, 0x12, 0x41); Note("Chat mit Handlungsbedarf aufgerufen."); return;
                    case "previous": SendComboTo(window, 0x11, 0x10, 0x09); Note("Vorheriger Chat oder Tab aufgerufen."); return;
                    case "next": SendComboTo(window, 0x11, 0x09); Note("Nächster Chat oder Tab aufgerufen."); return;
                    case "send": SendMessageSafely(window); return;
                    case "enter": SendComboTo(window, 0x0D); Note("Enter an Codex gesendet."); return;
                    default:
                        if (binding.Action.StartsWith("recent", StringComparison.Ordinal)) {
                            int n = int.Parse(binding.Action.Substring(6));
                            SendComboTo(window, 0x11, 0x12, (byte)(0x30 + n));
                            Note("Letzte Chats: Platz " + n + " aufgerufen.");
                        }
                        return;
                }
            } catch (Exception error) { Note("Aktion fehlgeschlagen: " + error.Message); }
        }

        private static string ThreadLink(string raw)
        {
            string value = (raw ?? "").Trim();
            if (value.StartsWith("codex://threads/", StringComparison.OrdinalIgnoreCase)) value = value.Substring("codex://threads/".Length);
            Guid id;
            if (!Guid.TryParse(value, out id)) throw new ArgumentException("Bitte eine gültige Chat-ID oder einen codex://threads/-Link eintragen.");
            return "codex://threads/" + id.ToString();
        }

        private static void OpenLink(string link)
        {
            var start = new ProcessStartInfo(link) { UseShellExecute = true };
            using (Process process = Process.Start(start)) { }
        }

        private static bool IsChatWindow(IntPtr window)
        {
            if (window == IntPtr.Zero || !Native.IsWindow(window) || !Native.IsWindowVisible(window)) return false;
            uint processId;
            Native.GetWindowThreadProcessId(window, out processId);
            try {
                using (Process process = Process.GetProcessById((int)processId))
                    return process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase);
            } catch { return false; }
        }

        private static bool IsUsableChatWindow(IntPtr window)
        {
            if (!IsChatWindow(window)) return false;
            if (Native.IsIconic(window)) {
                var placement = new Native.WindowPlacement { Length = (uint)Marshal.SizeOf<Native.WindowPlacement>() };
                if (!Native.GetWindowPlacement(window, ref placement)) return false;
                return placement.NormalPosition.Right - placement.NormalPosition.Left > 350 &&
                       placement.NormalPosition.Bottom - placement.NormalPosition.Top > 250;
            }
            Native.Rect rect;
            return Native.GetWindowRect(window, out rect) &&
                   rect.Right - rect.Left > 350 && rect.Bottom - rect.Top > 250;
        }

        private IntPtr FindChatWindow()
        {
            if (IsUsableChatWindow(lastChatWindow)) return lastChatWindow;
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((window, unused) => {
                if (IsUsableChatWindow(window)) {
                    found = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private IntPtr FocusChatGpt()
        {
            IntPtr target = FindChatWindow();
            if (target == IntPtr.Zero) return IntPtr.Zero;
            // Restore only minimized windows; preserve maximized/fullscreen state otherwise.
            if (Native.IsIconic(target)) {
                var placement = new Native.WindowPlacement { Length = (uint)Marshal.SizeOf<Native.WindowPlacement>() };
                bool wasMaximized = Native.GetWindowPlacement(target, ref placement) && (placement.Flags & 2) != 0;
                Native.ShowWindowAsync(target, wasMaximized ? 3 : 9);
            }
            Native.SetForegroundWindow(target);
            for (int i = 0; i < 12; i++) {
                Thread.Sleep(40);
                IntPtr foreground = Native.GetForegroundWindow();
                if (foreground == target && IsUsableChatWindow(foreground)) {
                    lastChatWindow = foreground;
                    return foreground;
                }
            }
            return IntPtr.Zero;
        }

        private static void SendComboTo(IntPtr window, params byte[] keys)
        {
            if (Native.GetForegroundWindow() != window || !IsUsableChatWindow(window))
                throw new InvalidOperationException("Aktion abgebrochen: Codex hat den Fokus verloren.");
            KeyboardOutput.Send(window, keys);
        }

        private void StartDictation(IntPtr window)
        {
            AutomationElement root = AutomationElement.FromHandle(window);
            if (!Native.GetWindowRect(window, out var app)) return;
            var diagnosticButtons = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            var relevant = new List<object>();
            foreach (AutomationElement element in diagnosticButtons) {
                var info = element.Current;
                string name = info.Name ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, "dikt|dict|aufnah|record|stop|abbrech|cancel|fertig|done|transk", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                var box = info.BoundingRectangle;
                relevant.Add(new { name, x = box.X, y = box.Y, width = box.Width, height = box.Height, offscreen = info.IsOffscreen, enabled = info.IsEnabled });
            }
            var found = root.FindAll(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "Diktieren"),
                    new PropertyCondition(AutomationElement.NameProperty, "Diktat beenden"))));
            var candidates = new List<AutomationElement>();
            var geometry = new List<object>();
            var locations = new HashSet<string>();
            foreach (AutomationElement element in found) {
                var info = element.Current;
                var bounds = info.BoundingRectangle;
                geometry.Add(new { name = info.Name, x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height,
                    enabled = info.IsEnabled, offscreen = info.IsOffscreen });
                if (!DictationSelection.InComposerArea(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    app.Left, app.Top, app.Right, app.Bottom, info.IsEnabled, info.IsOffscreen)) continue;
                string location = $"{bounds.X:F0}:{bounds.Y:F0}:{bounds.Width:F0}:{bounds.Height:F0}";
                if (locations.Add(location)) candidates.Add(element);
            }
            // Store only button geometry, never editor text or message contents.
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(configPath), "Diktat-Diagnose.json"),
                JsonSerializer.Serialize(new { buttons = geometry, related_controls = relevant, candidates = candidates.Count,
                    window = new { left = app.Left, top = app.Top, right = app.Right, bottom = app.Bottom } })); } catch { }
            if (candidates.Count == 0) { Note("Diktieren-Knopf am Nachrichtenfeld nicht gefunden. Bitte die gewünschte Aufgabe öffnen."); return; }
            var stopButtons = candidates.Where(e => e.Current.Name == "Diktat beenden").ToList();
            if (stopButtons.Count > 0) candidates = stopButtons;
            // On split views, use the composer containing keyboard focus if it identifies one button.
            AutomationElement focused = AutomationElement.FocusedElement;
            var parent = focused;
            for (int depth = 0; parent != null && depth < 8 && candidates.Count > 1; depth++) {
                var container = parent.Current.BoundingRectangle;
                if (parent.Current.ClassName?.Split(' ').Contains("ProseMirror") == true ||
                    parent.Current.ControlType == ControlType.Edit) {
                    // Buttons sit just below the text editor; exclude other side-by-side composers.
                    var nearby = candidates.Where(b => {
                        var box = b.Current.BoundingRectangle;
                        return box.Left >= container.Left - 24 && box.Right <= container.Right + 24 &&
                            box.Top >= container.Top && box.Top <= container.Bottom + 130;
                    }).ToList();
                    if (nearby.Count == 1) candidates = nearby;
                }
                parent = TreeWalker.ControlViewWalker.GetParent(parent);
            }
            // Duplicated accessibility trees may expose older composers above the active bottom one.
            if (candidates.Count > 1) {
                double lowest = candidates.Max(e => e.Current.BoundingRectangle.Bottom);
                var bottom = candidates.Where(e => Math.Abs(e.Current.BoundingRectangle.Bottom - lowest) <= 3).ToList();
                if (bottom.Count == 1) candidates = bottom;
            }
            if (candidates.Count != 1) {
                Note("Mehrere Nachrichtenfelder sichtbar. Bitte das gewünschte Nachrichtenfeld einmal auswählen; Strg+D wird bewusst nicht als Ersatz gesendet."); return;
            }
            if (Native.GetForegroundWindow() != window) return;
            if (!candidates[0].TryGetCurrentPattern(InvokePattern.Pattern, out object pattern)) {
                Note("Der Diktieren-Knopf lässt sich derzeit nicht direkt auslösen."); return;
            }
            ((InvokePattern)pattern).Invoke();
            Note("Diktieren am Nachrichtenfeld direkt umgeschaltet.");
        }

        private bool FocusComposer(IntPtr window)
        {
            // Find and focus Codex's editor. Enter must never answer an approval dialog.
            Native.Rect app;
            if (!Native.GetWindowRect(window, out app)) { Note("Fensterposition unbekannt."); return false; }
            AutomationElement root = AutomationElement.FromHandle(window);
            Condition editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
            AutomationElementCollection edits = root.FindAll(TreeScope.Descendants, editCondition);
            AutomationElement composer = null;
            foreach (AutomationElement element in edits) {
                System.Windows.Rect bounds = element.Current.BoundingRectangle;
                string className = element.Current.ClassName ?? "";
                string name = element.Current.Name ?? "";
                bool atBottom = bounds.Top > app.Top + (app.Bottom - app.Top) * 0.45 &&
                                bounds.Bottom <= app.Bottom + 15 && bounds.Width > 180;
                bool identified = className.Split(' ').Contains("ProseMirror") ||
                                  name.Equals("Mit ChatGPT arbeiten", StringComparison.Ordinal);
                if (atBottom && identified && !element.Current.IsOffscreen && element.Current.IsEnabled) {
                    if (composer != null) { Note("Mehrere mögliche Nachrichtenfelder gefunden; abgebrochen."); return false; }
                    composer = element;
                }
            }
            if (composer == null) {
                Note("Nicht gesendet: Codex-Nachrichtenfeld wurde nicht erkannt.");
                return false;
            }
            composer.SetFocus();
            Thread.Sleep(110);
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null || !Automation.Compare(focused, composer) || Native.GetForegroundWindow() != window) {
                Note("Nicht gesendet: Nachrichtenfeld konnte nicht sicher fokussiert werden.");
                return false;
            }
            return true;
        }

        private void SendMessageSafely(IntPtr window)
        {
            if (!FocusComposer(window)) return;
            if (config.SendWithControlEnter) SendComboTo(window, 0x11, 0x0D);
            else SendComboTo(window, 0x0D);
            Note("Senden-Taste im Codex-Nachrichtenfeld ausgelöst.");
        }

        private void Note(string text)
        {
            log.Items.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            while (log.Items.Count > 80) log.Items.RemoveAt(log.Items.Count - 1);
            state.Text = text;
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(configPath), "CodexPad.log"), DateTime.Now.ToString("s") + "  " + text + Environment.NewLine, Encoding.UTF8); } catch { }
        }

        private string RunPython(string script, params string[] arguments)
        {
            var start = new ProcessStartInfo(PythonPath(false)) {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(configPath), script));
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using (Process process = Process.Start(start)) {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000)) { process.Kill(true); throw new TimeoutException("Status/LED antwortet nicht."); }
                if (process.ExitCode != 0) throw new InvalidOperationException(error.GetAwaiter().GetResult().Trim());
                return output.GetAwaiter().GetResult();
            }
        }

        private string PythonPath(bool windowed)
        {
            string name = windowed ? "pythonw.exe" : "python.exe";
            string path = Path.Combine(Path.GetDirectoryName(configPath), "runtime", "python", name);
            if (!File.Exists(path)) throw new FileNotFoundException("Die mitgelieferte Python-Laufzeit fehlt. Bitte CodexPad vollständig wiederherstellen.", path);
            return path;
        }

        private async Task DeviceCommand(bool configure)
        {
            if (deviceBusy) { Note("Bitte warten: Die Geräteprüfung läuft bereits."); return; }
            deviceBusy = true;
            if (configure) StopListening();
            deviceState.Text = configure ? "Die sechs Pad-Eingaben werden auf F13–F18 eingestellt …" : "Pad wird gesucht …";
            try {
                string executable = PythonPath(false);
                string script = Path.Combine(Path.GetDirectoryName(configPath), "pad_device.py");
                string result = await Task.Run(() => {
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                    start.Environment["PYTHONIOENCODING"] = "utf-8";
                    start.ArgumentList.Add(script);
                    start.ArgumentList.Add(configure ? "--configure" : "--inspect");
                    using (Process process = Process.Start(start)) {
                        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                        Task<string> stderr = process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(12000)) { process.Kill(true); throw new TimeoutException("Gerätezugriff dauert zu lange. Eine Einrichtung kann teilweise erfolgt sein; bitte Eingaben testen."); }
                        string output = stdout.GetAwaiter().GetResult().Trim();
                        string error = stderr.GetAwaiter().GetResult().Trim();
                        if (process.ExitCode != 0) throw new InvalidOperationException(error.Length == 0 ? output : error);
                        return output;
                    }
                });
                if (IsDisposed) return;
                if (configure) {
                    // Preserve the user's chosen actions and target chats.
                    for (int i = 0; i < 6; i++) Select(inputBoxes[i], "F" + (13 + i));
                    SaveConfig(false);
                }
                deviceState.Text = result;
                Note(result);
            } catch (Exception error) {
                if (!IsDisposed) { deviceState.Text = "Gerätezugriff fehlgeschlagen. Details im Protokoll."; Note(error.Message); }
            } finally { deviceBusy = false; }
        }

        private void LaunchHelper(string file, bool python)
        {
            try {
                string executable = python ? PythonPath(true) : Process.GetCurrentProcess().MainModule.FileName;
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                bool nativePad = !python && Path.GetFileName(executable).Equals("CodexPad.exe", StringComparison.OrdinalIgnoreCase);
                if (nativePad) start.ArgumentList.Add("--pad-test");
                else if (!python) {
                    foreach (string argument in new[] { "-NoProfile", "-STA", "-WindowStyle", "Hidden", "-File" }) start.ArgumentList.Add(argument);
                }
                if (!nativePad) start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(configPath), file));
                using (Process process = Process.Start(start)) { }
                Note("Testfenster gestartet.");
            } catch (Exception error) { Note("Testfenster konnte nicht starten: " + error.Message); }
        }

        internal void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            tray.Visible = true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexPad
{
    public sealed partial class PadForm
    {
        private readonly ComboBox[] destinations = new ComboBox[6];
        private readonly Label[] activeLabels = new Label[6];
        private readonly Label ledState = new Label(), appState = new Label(), activeState = new Label();
        private readonly ListBox unreadTasks = new ListBox();
        private readonly CheckBox showTaskNotice = new CheckBox();
        private readonly TaskCycle cycle = new TaskCycle();
        private readonly LedPolicy ledPolicy = new LedPolicy();
        private readonly Queue<(Binding binding, IntPtr target)> actionQueue = new Queue<(Binding, IntPtr)>();
        private bool executing, previewMode;
        private BackendClient backend;
        private PadSnapshot snapshot;
        private DateTime snapshotAt;
        private int? appliedLed;
        private bool connected;
        private int deviceGeneration;
        private string deviceKey = "";
        private DateTime ledTestUntil;
        private bool recording;
        private string initialConfigError;
        private const int ShowAppMessage = 0x8000 + 73;

        internal void SelectBindingsForPreview() => SelectPage(1);
        internal void ShowDiagnostics() { RestoreWindow(); SelectPage(2); }

        private static void AddButton(Control parent, string text, Action click)
        {
            string symbol = text switch {
                "Aktivieren" => "play", "Pausieren" => "pause", "Übernehmen" => "check", "Verwerfen" => "undo",
                "Exportieren" or "Importieren" or "Anleitung" => "folder", "LED aus" or "LED-Effekt" => "led",
                "Belegung ändern" => "key", _ => "settings"
            };
            var buttonFont = PadTheme.UiFont(9.5f);
            var button = new PadButton { Text = text, Symbol = symbol, Height = 38, Font = buttonFont,
                Width = Math.Max(116, TextRenderer.MeasureText(text, buttonFont).Width + 60),
                Primary = text == "Übernehmen" || text == "Aktivieren", Margin = new Padding(0, 5, 10, 5) };
            button.Click += (s, e) => click(); parent.Controls.Add(button);
        }
        private void ApplyConfig() => ShowConfig(config);
        private void ShowConfig(Config value)
        {
            for (int i = 0; i < 6; i++) {
                Select(inputBoxes[i], value.Bindings[i].Input); Select(actionBoxes[i], value.Bindings[i].Action);
                targetBoxes[i].Text = value.Bindings[i].Target ?? ""; Select(destinations[i], value.Bindings[i].Destination);
            }
            controlEnter.Checked = value.SendWithControlEnter; autoStart.Checked = value.AutoStartWithWindows;
            completionLed.Checked = value.CompletionLed; activateOnLaunch.Checked = value.ActivateOnLaunch; showTaskNotice.Checked = value.ShowTaskNotice;
            notificationLedMode.SelectedIndex = value.NotificationLedMode - 1;
            UpdateActiveLabels();
        }
        private void UpdateActiveLabels()
        {
            for (int i = 0; i < 6; i++) {
                var b = config.Bindings[i];
                string label = b.Action == "shortcut" ? b.Target + (b.Destination == "active" ? " · Aktuelles Programm" : " · Codex") : Actions.FirstOrDefault(a => a.Id == b.Action)?.Label ?? b.Action;
                activeLabels[i].Text = activeLabels[i].Tag + "\n" + label;
                var oldImage = actionIcons[i].Image;
                actionIcons[i].Image = PadTheme.Glyph(PadTheme.ActionIcon(b.Action), PadTheme.Accent, 24);
                oldImage?.Dispose();
            }
            padPicture.Actions = config.Bindings.Take(3).Select(b => b.Action).ToArray();
            padPicture.Invalidate();
        }
        private Config Draft()
        {
            var result = SettingsStore.Copy(config);
            for (int i = 0; i < 6; i++) result.Bindings[i] = new Binding {
                Input = ((Choice)inputBoxes[i].SelectedItem).Id, Action = ((Choice)actionBoxes[i].SelectedItem).Id,
                Target = targetBoxes[i].Text.Trim(), Destination = ((Choice)destinations[i].SelectedItem).Id
            };
            result.Version = 2; result.SendWithControlEnter = controlEnter.Checked; result.AutoStartWithWindows = autoStart.Checked;
            result.CompletionLed = completionLed.Checked; result.ActivateOnLaunch = activateOnLaunch.Checked; result.ShowTaskNotice = showTaskNotice.Checked;
            result.NotificationLedMode = notificationLedMode.SelectedIndex + 1;
            return result;
        }
        private static void ValidateConfig(Config value)
        {
            if (value.NotificationLedMode != 1 && value.NotificationLedMode != 2) throw new ArgumentException("LED-Modus muss 1 oder 2 sein.");
            if (value.Bindings == null || value.Bindings.Count != 6) throw new ArgumentException("Genau sechs Belegungen sind erforderlich.");
            var used = new HashSet<string>();
            foreach (var b in value.Bindings) {
                if (b == null || !Inputs.Any(i => i.Id == b.Input) || !Actions.Any(a => a.Id == b.Action)) throw new ArgumentException("Unbekanntes Pad-Signal oder unbekannte Aktion.");
                if (b.Input != "none" && !used.Add(b.Input)) throw new ArgumentException("Pad-Signal doppelt belegt: " + b.Input);
                if (b.Destination != "codex" && b.Destination != "active") throw new ArgumentException("Ungültiges Zielfenster.");
                if (b.Action == "thread") ThreadLink(b.Target);
                if (b.Action == "shortcut") {
                    byte[] keys = Shortcut.Parse(b.Target);
                    if (keys.Length == 1 && value.Bindings.Any(other => other != null && Inputs.Any(i => i.Id == other.Input && i.VirtualKey == keys[0])))
                        throw new ArgumentException("Das ausgegebene Kürzel ist zugleich ein Pad-Signal. Das würde eine Endlosschleife auslösen.");
                }
            }
        }
        private void SaveConfig(bool applyAutoStart = true)
        {
            Config old = config; bool active = registered.Count > 0;
            try {
                var next = Draft(); ValidateConfig(next);
                if (active) { StopListening(); RegisterBindings(next); }
                SettingsStore.Write(configPath, next); config = next;
                UpdateActiveLabels(); Note("Einstellungen übernommen und vorherige Belegung gesichert.");
                if (applyAutoStart) {
                    try { UpdateWindowsAutoStart(); }
                    catch (Exception e) { Note("Belegung übernommen; Windows-Autostart konnte nicht aktualisiert werden: " + e.Message); }
                }
            } catch (Exception error) {
                config = old;
                if (active) { StopListening(); try { RegisterBindings(old); } catch { } }
                Note("Übernehmen fehlgeschlagen: " + error.Message);
            }
        }
        private void ExportSettings()
        {
            using var dialog = new SaveFileDialog { Filter = "CodexPad-Einstellungen (*.json)|*.json", FileName = "CodexPad-Belegung.json" };
            if (dialog.ShowDialog(this) == DialogResult.OK) {
                try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true })); Note("Aktive Einstellungen exportiert."); }
                catch (Exception e) { Note("Export fehlgeschlagen: " + e.Message); }
            }
        }
        private void ImportSettings()
        {
            using var dialog = new OpenFileDialog { Filter = "CodexPad-Einstellungen (*.json)|*.json" };
            if (dialog.ShowDialog(this) == DialogResult.OK) {
                try { var imported = SettingsStore.Read(dialog.FileName); ValidateConfig(imported); ShowConfig(imported); Note("Import als Entwurf geladen. Mit Übernehmen aktivieren; vorherige Einstellungen werden gesichert."); }
                catch (Exception e) { Note("Import fehlgeschlagen: " + e.Message); }
            }
        }
        private void RegisterBindings(Config value)
        {
            ValidateConfig(value);
            for (int i = 0; i < 6; i++) {
                var choice = Inputs.First(c => c.Id == value.Bindings[i].Input);
                if (choice.VirtualKey == 0 || value.Bindings[i].Action == "none") continue;
                int id = 100 + i;
                if (!Native.RegisterHotKey(Handle, id, Native.NoRepeat, choice.VirtualKey)) {
                    StopListening(); throw new InvalidOperationException("Taste " + choice.Label + " wird schon von einem anderen Programm verwendet (Windows " + Marshal.GetLastWin32Error() + ").");
                }
                registered.Add(id);
            }
            activeState.Text = "Aktiv · " + registered.Count + " Bedienelemente · auch aus anderen Fenstern";
        }
        private void StartListening(bool persist = true)
        {
            StopListening();
            try { RegisterBindings(config); Note("Gespeicherte Belegung aktiv."); }
            catch (Exception error) { StopListening(); Note("Aktivierung fehlgeschlagen: " + error.Message); }
        }
        private void RecordShortcut(int row)
        {
            recording = true;
            try {
                using var recorder = new ShortcutRecorder();
                if (recorder.ShowDialog(this) == DialogResult.OK) {
                    Select(actionBoxes[row], "shortcut"); targetBoxes[row].Text = recorder.Value;
                    Note("Kürzel aufgenommen. Mit Übernehmen aktivieren.");
                }
            } finally { recording = false; }
        }
        private async Task TestBinding(int row)
        {
            try {
                var draft = Draft(); ValidateConfig(draft); var b = draft.Bindings[row];
                if (b.Action == "shortcut" && b.Destination == "active") {
                    Note("Test in 3 Sekunden: Jetzt zum gewünschten Programm wechseln.");
                    await Task.Delay(3000);
                }
                var target = Native.GetForegroundWindow();
                if (b.Action == "shortcut" && b.Destination == "active" && target == Handle) {
                    Note("Test abgebrochen: Bitte zu einem anderen Programm wechseln."); return;
                }
                QueueAction(b, target);
            } catch (Exception e) { Note("Test nicht möglich: " + e.Message); }
        }
        private async void QueueAction(Binding binding, IntPtr target)
        {
            if (actionQueue.Count >= 12) { Note("Bitte kurz warten: Es sind noch Tastendrücke in Bearbeitung."); return; }
            actionQueue.Enqueue((new Binding { Input = binding.Input, Action = binding.Action, Target = binding.Target, Destination = binding.Destination }, target));
            if (executing) return;
            executing = true;
            try {
                while (actionQueue.Count > 0 && !IsDisposed) {
                    var item = actionQueue.Dequeue();
                    uint trigger = Inputs.First(c => c.Id == item.binding.Input).VirtualKey;
                    // Asynchronous release wait keeps status/UI responsive and prevents held-pad repeats.
                    int attempts = 0;
                    while (trigger != 0 && (Native.GetAsyncKeyState((int)trigger) & 0x8000) != 0 && attempts++ < 50) await Task.Delay(20);
                    if (trigger != 0 && (Native.GetAsyncKeyState((int)trigger) & 0x8000) != 0) { Note("Taste bitte loslassen und erneut drücken."); continue; }
                    await ExecuteBinding(item.binding, item.target);
                }
            } catch (Exception error) { Note("Aktion fehlgeschlagen: " + error.Message); }
            finally { executing = false; }
        }
        private async Task ExecuteBinding(Binding binding, IntPtr target)
        {
            try {
                if (binding.Action == "chatScrollUp" || binding.Action == "chatScrollDown") {
                    var window = FocusChatGpt();
                    if (window == IntPtr.Zero) throw new InvalidOperationException("Codex konnte nicht nach vorne geholt werden.");
                    string route = ChatScroller.Scroll(window, binding.Action == "chatScrollUp");
                    Note("Scrollbefehl an den Codex-Nachrichtenbereich gesendet (" + route + ").");
                    return;
                }
                if (binding.Action == "scrollUp" || binding.Action == "scrollDown") {
                    KeyboardOutput.Scroll(binding.Action == "scrollUp");
                    Note(binding.Action == "scrollUp" ? "Mausrad nach oben ausgelöst." : "Mausrad nach unten ausgelöst.");
                    return;
                }
                if (binding.Action == "shortcut") {
                    if (binding.Destination == "codex") target = FocusChatGpt();
                    KeyboardOutput.Send(target, Shortcut.Parse(binding.Target));
                    Note("Tastenkürzel ausgeführt: " + binding.Target); return;
                }
                if (binding.Action == "cycle" || binding.Action == "latest") {
                    if (snapshot == null || (DateTime.UtcNow - snapshotAt).TotalSeconds > 9) {
                        await RefreshCompletionLed();
                        if (snapshot == null || (DateTime.UtcNow - snapshotAt).TotalSeconds > 9) throw new InvalidOperationException("Aufgabenliste noch nicht verfügbar. Bitte gleich erneut versuchen.");
                    }
                    var task = binding.Action == "cycle" ? cycle.Next(snapshot, DateTime.UtcNow) : snapshot.tasks.FirstOrDefault();
                    if (task == null) { Note("Keine lokale Aufgabe verfügbar."); return; }
                    OpenLink(ThreadLink(task.id)); await Task.Delay(180);
                    bool focused = FocusChatGpt() != IntPtr.Zero;
                    Note(focused ? "Aufgabe geöffnet." : "Aufgabe geöffnet; Windows hat den Fokuswechsel nicht bestätigt.");
                    if (config.ShowTaskNotice) ShowTaskToast(TaskTitle.ForDisplay(task.title));
                    return;
                }
                ExecuteLegacy(binding);
            } catch (Exception error) { Note("Aktion fehlgeschlagen: " + error.Message); }
        }
        private void ShowTaskToast(string title)
        {
            var toast = new TaskToast(title);
            toast.Show();
        }

        private async Task RefreshCompletionLed()
        {
            if (statusBusy || IsDisposed || previewMode) return;
            statusBusy = true;
            try {
                backend ??= new BackendClient(Path.GetDirectoryName(configPath), PythonPath(false));
                var current = await backend.Read();
                if (IsDisposed || Disposing) return;
                snapshot = current.error == null ? current : null;
                if (snapshot != null) snapshotAt = DateTime.UtcNow;
                if (current.connected != connected || current.device_key != deviceKey) appliedLed = null;
                connected = current.connected; deviceKey = current.device_key;
                deviceState.Text = connected ? "Pad verbunden · SinLoon · automatische Wiederverbindung aktiv" : "Pad nicht erreichbar · bitte USB-Verbindung prüfen";
                appState.Text = FindChatWindow() != IntPtr.Zero ? "Codex/Work erreichbar · lokale Aufgaben" : "Codex/Work-Fenster nicht gefunden";
                var pending = current.tasks.Where(t => t.unread && t.status == "completed").ToList();
                unreadTasks.BeginUpdate(); unreadTasks.Items.Clear();
                foreach (var t in pending) unreadTasks.Items.Add("Ungelesenes Ergebnis · " + TaskTitle.ForDisplay(t.title));
                if (pending.Count == 0) unreadTasks.Items.Add(current.led_known ? "Keine fertigen ungelesenen Ergebnisse." : "Lesestatus oder Aufgabenstatus derzeit nicht vollständig verfügbar.");
                unreadTasks.EndUpdate();
                int desired = ledPolicy.Desired(config.CompletionLed, current.led_known, pending.Count > 0, DateTime.UtcNow, config.NotificationLedMode);
                ledState.Text = !config.CompletionLed ? "LED-Automatik ausgeschaltet" : current.led_known ?
                    "LED: " + (desired != 0 ? "Modus " + config.NotificationLedMode + " · Ergebnis bereit · " + pending.Count + " ungelesen" : "aus · alles gelesen oder noch in Arbeit") : "LED: Status unbekannt · nach 15 Sekunden ohne gültige Daten aus";
                if (current.error != null && lastStatusError != current.error) { Note(current.error); lastStatusError = current.error; }
                if (current.error == null) lastStatusError = null;
                if (connected && DateTime.UtcNow >= ledTestUntil) {
                    try { await ApplyLed(desired); }
                    catch { deviceState.Text = "Pad erkannt, aber LED-Übertragung fehlgeschlagen · erneuter Versuch folgt"; }
                }
            } catch (Exception error) {
                if (IsDisposed) return;
                snapshot = null;
                ledState.Text = "LED: Status unbekannt · Statusdienst wird erneut geprüft";
                int desired = ledPolicy.Desired(config.CompletionLed, false, false, DateTime.UtcNow, config.NotificationLedMode);
                if (lastStatusError != error.Message) { Note(error.Message); lastStatusError = error.Message; }
                if (DateTime.UtcNow >= ledTestUntil) { try { await ApplyLed(desired); } catch { appliedLed = null; } }
            } finally { statusBusy = false; }
        }
        private async Task ApplyLed(int mode)
        {
            if (appliedLed == mode) return;
            int generation = deviceGeneration;
            try {
                await Task.Run(() => RunPython("pad_led.py", "--mode-volatile", mode.ToString()));
                appliedLed = generation == deviceGeneration ? mode : null;
            } catch { appliedLed = null; throw; }
        }
        private async Task TestLed(int mode)
        {
            if (statusBusy) { Note("Statusabfrage läuft. Bitte gleich erneut testen."); return; }
            statusBusy = true;
            try {
                ledTestUntil = DateTime.UtcNow.AddSeconds(10); appliedLed = null;
                await ApplyLed(mode);
                Note("LED-Test für zehn Sekunden gestartet; danach übernimmt die Automatik.");
            } catch (Exception e) { Note("LED-Test fehlgeschlagen: " + e.Message); }
            finally { statusBusy = false; }
        }
    }

    internal sealed class TaskToast : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x80; return value; } }
        public TaskToast(string title)
        {
            ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; TopMost = true;
            Size = new Size(430, 74); BackColor = Color.FromArgb(30, 40, 52); ForeColor = Color.White;
            StartPosition = FormStartPosition.Manual;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea; Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);
            Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Padding = new Padding(15), Font = PadTheme.UiFont(11), AutoEllipsis = true });
            var timer = new System.Windows.Forms.Timer { Interval = 1800 }; timer.Tick += (s, e) => { timer.Stop(); timer.Dispose(); Close(); }; timer.Start();
        }
    }

    internal sealed class ShortcutRecorder : Form
    {
        private delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, Hook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr handle);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        private readonly HashSet<int> pressed = new HashSet<int>();
        private readonly Label status = new Label();
        private Hook callback;
        private IntPtr hook;
        public string Value { get; private set; }
        public ShortcutRecorder()
        {
            Text = "Tastenkürzel aufnehmen"; Size = new Size(560, 230);
            BackColor = PadTheme.Background; ForeColor = PadTheme.Text;
            HandleCreated += (s, e) => PadTheme.DarkTitle(Handle); StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            status.Text = "Jetzt die gewünschte Kombination drücken und loslassen.\nBeispiel: Strg+D, Enter oder F19.\nAbbrechen ist unten mit der Maus möglich.";
            status.Dock = DockStyle.Fill; status.Padding = new Padding(18); status.Font = PadTheme.UiFont(11);
            Controls.Add(status);
            var cancel = new PadButton { Text = "Abbrechen", Dock = DockStyle.Bottom, Height = 42 };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); }; Controls.Add(cancel);
            Shown += (s, e) => {
                callback = CaptureKey; hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) { status.Text = "Aufnahme nicht verfügbar. Kürzel bitte direkt im Textfeld eintragen."; }
            };
            FormClosed += (s, e) => { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); };
        }
        private IntPtr CaptureKey(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || Native.GetForegroundWindow() != Handle) return CallNextHookEx(hook, code, message, data);
            int key = Marshal.ReadInt32(data); int msg = message.ToInt32();
            bool down = msg == 0x100 || msg == 0x104;
            if (down) {
                pressed.Add(key);
                bool modifier = key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
                if (!modifier && Value == null) {
                    Keys mods = Keys.None;
                    if (pressed.Overlaps(new[] { 0x11, 0xA2, 0xA3 })) mods |= Keys.Control;
                    if (pressed.Overlaps(new[] { 0x10, 0xA0, 0xA1 })) mods |= Keys.Shift;
                    if (pressed.Overlaps(new[] { 0x12, 0xA4, 0xA5 })) mods |= Keys.Alt;
                    Value = Shortcut.Format((Keys)key, mods, pressed.Contains(0x5B) || pressed.Contains(0x5C));
                    status.Text = Value + "\nBitte alle Tasten loslassen.";
                }
            } else {
                pressed.Remove(key);
                if (Value != null && pressed.Count == 0) BeginInvoke(new Action(() => { DialogResult = DialogResult.OK; Close(); }));
            }
            return new IntPtr(1);
        }
    }
}

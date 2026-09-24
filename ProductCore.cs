using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexPad
{
    internal static class TaskTitle
    {
        public static string ForDisplay(string title)
        {
            string value = (title ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (value.Length == 0 || value.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("https://", StringComparison.OrdinalIgnoreCase)) return "Aufgabe geöffnet";
            return value.Length > 90 ? value.Substring(0, 89) + "…" : value;
        }
    }
    internal static class DictationSelection
    {
        public static bool InComposerArea(double x, double y, double width, double height,
            int left, int top, int right, int bottom, bool enabled, bool offscreen)
        {
            return enabled && !offscreen && width > 0 && height > 0 &&
                x >= left && x + width <= right + 2 && y >= top + (bottom - top) * 0.45 &&
                y + height <= bottom + 2;
        }
    }
    internal sealed class TaskInfo
    {
        public string id { get; set; }
        public string title { get; set; }
        public string status { get; set; }
        public long recency { get; set; }
        public bool unread { get; set; }
    }
    internal sealed class PadSnapshot
    {
        public List<TaskInfo> tasks { get; set; } = new List<TaskInfo>();
        public bool unread_known { get; set; }
        public bool led_known { get; set; }
        public bool connected { get; set; }
        public string device_key { get; set; } = "";
        public string error { get; set; }
        public string device_error { get; set; }
    }

    internal sealed class TaskCycle
    {
        private List<TaskInfo> round = new List<TaskInfo>();
        private DateTime lastPress = DateTime.MinValue;
        private int position;
        public TaskInfo Next(PadSnapshot snapshot, DateTime now)
        {
            if (round.Count == 0 || (now - lastPress).TotalSeconds >= 10) {
                var sorted = snapshot.tasks.OrderByDescending(t => t.recency).ThenBy(t => t.id).ToList();
                // No public, verified approval state is available in the local adapter.
                round = sorted.Where(t => snapshot.unread_known && t.unread && t.status == "completed")
                    .Concat(sorted.Where(t => t.status == "running")).ToList();
                if (round.Count == 0) round = sorted.Take(6).ToList();
                position = 0;
            }
            lastPress = now;
            if (round.Count == 0) return null;
            var result = round[position];
            position = (position + 1) % round.Count;
            return result;
        }
    }

    internal sealed class LedPolicy
    {
        private DateTime? unknownSince;
        private int desired;
        public int Desired(bool enabled, bool known, bool unreadCompleted, DateTime now, int mode = 1)
        {
            if (mode != 1 && mode != 2) throw new ArgumentException("LED-Modus muss 1 oder 2 sein.");
            if (!enabled) { desired = 0; unknownSince = null; return 0; }
            if (known) { unknownSince = null; desired = unreadCompleted ? 1 : 0; }
            else {
                unknownSince ??= now;
                if ((now - unknownSince.Value).TotalSeconds >= 15) desired = 0;
            }
            return desired == 0 ? 0 : mode;
        }
    }

    internal static class SettingsStore
    {
        public static Config Copy(Config value) => JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(value));
        public static Config Read(string path)
        {
            var value = JsonSerializer.Deserialize<Config>(File.ReadAllText(path));
            if (value == null || value.Bindings == null || value.Bindings.Count != 6)
                throw new InvalidDataException("Die Datei muss genau sechs Belegungen enthalten.");
            if (value.NotificationLedMode != 1 && value.NotificationLedMode != 2) throw new InvalidDataException("LED-Modus muss 1 oder 2 sein.");
            if (value.Version > 2) throw new InvalidDataException("Diese Einstellungen stammen aus einer neueren Programmversion.");
            if (value.Version < 2) {
                // Agreed upgrade: only replace the old left-button 'latest' action.
                if (value.Bindings[0].Action == "latest") value.Bindings[0].Action = "cycle";
                value.Version = 2;
            }
            return value;
        }
        public static void Write(string path, Config config)
        {
            if (File.Exists(path)) {
                string backups = Path.Combine(Path.GetDirectoryName(path), "Einstellungen-Sicherungen");
                Directory.CreateDirectory(backups);
                File.Copy(path, Path.Combine(backups, "Belegung-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"));
            }
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
    }

    internal static class Shortcut
    {
        public static byte[] Parse(string text)
        {
            var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var modifiers = new HashSet<byte>();
            byte? main = null;
            foreach (string part in parts) {
                string token = part.ToUpperInvariant();
                byte mod = token == "CTRL" || token == "STRG" || token == "CONTROL" ? (byte)0x11 :
                    token == "SHIFT" || token == "UMSCHALT" ? (byte)0x10 :
                    token == "ALT" ? (byte)0x12 : token == "WIN" || token == "WINDOWS" ? (byte)0x5B : (byte)0;
                if (mod != 0) { modifiers.Add(mod); continue; }
                Keys key;
                if (token.Length == 1 && char.IsDigit(token[0])) key = (Keys)token[0];
                else if (token == "ENTER") key = Keys.Enter;
                else if (!Enum.TryParse<Keys>(part, true, out key)) throw new ArgumentException("Unbekannte Taste: " + part);
                if ((int)key <= 0 || (int)key > 254 || key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                    throw new ArgumentException("Bitte eine normale Taste mit optionalen Zusatztasten aufnehmen.");
                if (main != null) throw new ArgumentException("Pro Belegung ist eine Taste mit Strg, Alt, Umschalt und/oder Windows möglich; keine Tastenfolge.");
                main = (byte)key;
            }
            if (main == null) throw new ArgumentException("Bitte zuerst ein Tastenkürzel aufnehmen oder eintragen.");
            if (modifiers.Contains(0x11) && modifiers.Contains(0x12) && main == 0x2E)
                throw new ArgumentException("Strg+Alt+Entf kann Windows nicht auf diesem Weg auslösen.");
            if (modifiers.Contains(0x5B) && (main == 0x4C || main == 0x55))
                throw new ArgumentException("Diese Windows-Systemkombination ist für die Belegung gesperrt.");
            var result = new List<byte>();
            foreach (byte key in new byte[] { 0x11, 0x10, 0x12, 0x5B }) if (modifiers.Contains(key)) result.Add(key);
            result.Add(main.Value);
            return result.ToArray();
        }
        public static string Format(Keys key, Keys modifiers, bool win)
        {
            var parts = new List<string>();
            if ((modifiers & Keys.Control) != 0) parts.Add("Strg");
            if ((modifiers & Keys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & Keys.Alt) != 0) parts.Add("Alt");
            if (win) parts.Add("Win");
            parts.Add(key >= Keys.D0 && key <= Keys.D9 ? ((int)key - (int)Keys.D0).ToString() : key == Keys.Return ? "Enter" : key.ToString());
            return string.Join("+", parts);
        }
    }

    internal static class KeyboardOutput
    {
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint type; public InputUnion data; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion {
            [FieldOffset(0)] public Keyboard keyboard;
            [FieldOffset(0)] public Mouse mouse;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int x, y; public uint data, flags, time; public UIntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        public static void Scroll(bool up)
        {
            // One ordinary wheel notch. Windows applies the user's wheel settings
            // and routes the event normally; do not move the pointer or activate Codex.
            var inputs = new[] { new Input { type = 0, data = new InputUnion {
                mouse = new Mouse { data = unchecked((uint)(up ? 120 : -120)), flags = 0x0800 }
            } } };
            if (SendInput(1, inputs, Marshal.SizeOf<Input>()) != 1)
                throw new InvalidOperationException("Windows hat das Mausradsignal blockiert.");
        }
        public static void Send(IntPtr target, byte[] keys)
        {
            if (target == IntPtr.Zero || Native.GetForegroundWindow() != target)
                throw new InvalidOperationException("Nicht gesendet: Das Zielfenster hat den Fokus verloren.");
            foreach (int mod in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if ((Native.GetAsyncKeyState(mod) & 0x8000) != 0)
                    throw new InvalidOperationException("Bitte Strg, Alt, Umschalt und Windows loslassen und erneut drücken.");
            Input Make(byte key, bool up) => new Input { type = 1, data = new InputUnion { keyboard = new Keyboard {
                key = key, flags = (up ? 2u : 0u) | (key is >= 0x21 and <= 0x2E || key == 0x5B || key == 0x5C || key == 0x6F ? 1u : 0u)
            } } };
            var inputs = keys.Select(k => Make(k, false)).Concat(keys.Reverse().Select(k => Make(k, true))).ToArray();
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) {
                var release = keys.Reverse().Select(k => Make(k, true)).ToArray();
                SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
                throw new InvalidOperationException("Windows hat die Tastenausgabe blockiert. Programme mit Administratorrechten benötigen dieselbe Berechtigungsstufe.");
            }
        }
    }

    internal sealed class BackendClient : IDisposable
    {
        private Process process;
        private readonly string folder, python;
        public BackendClient(string folder, string python) { this.folder = folder; this.python = python; }
        public async Task<PadSnapshot> Read()
        {
            try {
                if (process == null || process.HasExited) {
                    Dispose();
                    var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    start.ArgumentList.Add(Path.Combine(folder, "pad_backend.py"));
                    start.ArgumentList.Add("--serve");
                    start.Environment["PYTHONIOENCODING"] = "utf-8";
                    process = Process.Start(start);
                    process.BeginErrorReadLine(); // Drain stderr; never persist raw data from local task files.
                }
                await process.StandardInput.WriteLineAsync("{\"command\":\"snapshot\"}");
                await process.StandardInput.FlushAsync();
                string line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(12));
                if (line == null) throw new IOException();
                return JsonSerializer.Deserialize<PadSnapshot>(line) ?? throw new IOException();
            } catch {
                Dispose();
                throw new InvalidOperationException("Statusdienst nicht erreichbar; wird automatisch neu gestartet.");
            }
        }
        public void Dispose()
        {
            try { if (process != null && !process.HasExited) process.Kill(true); } catch { }
            process?.Dispose(); process = null;
        }
    }
}

using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace CodexPadDiagnostic
{
    public sealed class TestWindow : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)]
        struct RawHeader { public uint Type, Size; public IntPtr Device, WParam; }
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);

        readonly string[] steps = { "Taste links einmal drücken", "Taste in der Mitte einmal drücken", "Taste rechts einmal drücken", "Regler einen Schritt nach links drehen", "Regler einen Schritt nach rechts drehen", "Regler einmal drücken" };
        readonly Label instruction = new Label();
        readonly TextBox output = new TextBox();
        readonly Button next = new Button();
        readonly bool[] observed = new bool[6];
        readonly bool[] matched = new bool[6];
        readonly string[] expected = { "F13", "F14", "F15", "F16", "F17", "F18" };
        readonly string folder;
        string setupId;
        readonly string logPath;
        int step;

        public TestWindow(string folder)
        {
            this.folder = folder;
            try {
                using (JsonDocument setup = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "Pad-Geraetestatus.json")))) {
                    setupId = setup.RootElement.GetProperty("setup_id").GetString();
                    JsonElement configured = setup.RootElement.GetProperty("expected");
                    if (configured.GetArrayLength() == 6)
                        for (int i = 0; i < 6; i++) expected[i] = configured[i].GetString();
                }
            } catch { }
            logPath = Path.Combine(folder, "Pad-Test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            Text = "CodexPad – Gerät testen";
            ClientSize = new Size(820, 570);
            MinimumSize = new Size(700, 500);
            Font = new Font("Segoe UI", 11);
            StartPosition = FormStartPosition.CenterScreen;
            var explanation = new Label { Text = "Dieses Fenster geöffnet und im Vordergrund lassen.\nNach jedem Versuch mit der Maus auf Weiter klicken. Es wird nichts auf das Pad geschrieben.", AutoSize = false, Bounds = new Rectangle(20, 16, 780, 64), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            instruction.SetBounds(20, 90, 780, 48);
            instruction.Font = new Font(Font, FontStyle.Bold);
            instruction.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            output.Multiline = true;
            output.ReadOnly = true;
            output.ScrollBars = ScrollBars.Vertical;
            output.SetBounds(20, 145, 780, 300);
            output.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            var back = new Button { Text = "Zurück", Bounds = new Rectangle(20, 460, 120, 38), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            back.Click += delegate { if (step > 0) step--; UpdateStep(); };
            next.SetBounds(150, 460, 210, 38);
            next.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            next.Click += delegate {
                if (step < 5) { step++; UpdateStep(); }
                else {
                    int count = 0; foreach (bool value in observed) if (value) count++;
                    int correct = 0; foreach (bool value in matched) if (value) correct++;
                    Write("Auswertung: " + count + "/6 Eingaben empfangen; " + correct + "/6 mit dem erwarteten Tastensignal.");
                    File.WriteAllText(Path.Combine(folder, "Pad-Pruefergebnis.json"), JsonSerializer.Serialize(new {
                        setup_id = setupId, time = DateTime.Now.ToString("s"), matched = correct, received = count,
                        expected = expected, log = Path.GetFileName(logPath)
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    instruction.Text = correct == 6 ? "Alle sechs Eingaben stimmen. Du kannst CodexPad aktivieren." : "Test beendet: Noch nicht alle Eingaben stimmen. Sag Codex Bescheid.";
                    next.Enabled = false;
                }
            };
            var footer = new Label { Text = "Es werden nur Signale des Pads (1189:8890) gespeichert. Andere Tastaturen werden ignoriert.\nProtokoll: " + Path.GetFileName(logPath), Bounds = new Rectangle(20, 510, 780, 52), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Segoe UI", 9) };
            Controls.AddRange(new Control[] { explanation, instruction, output, back, next, footer });
            UpdateStep();
            Write("Test gestartet. Noch keine Taste erkannt.");
        }

        void UpdateStep()
        {
            instruction.Text = "Schritt " + (step + 1) + " von 6: " + steps[step] + " (erwartet: " + expected[step] + ")";
            next.Text = step == 5 ? "Test abschließen" : "Weiter";
            next.Enabled = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Foreground-only listeners. No background capture and no device writes.
            var devices = new[] {
                new RawDevice { Page = 1, Usage = 6, Target = Handle },
                new RawDevice { Page = 1, Usage = 2, Target = Handle },
                new RawDevice { Page = 12, Usage = 1, Target = Handle }
            };
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawDevice>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        void Write(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine;
            output.AppendText(line);
            File.AppendAllText(logPath, line, Encoding.UTF8);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x00FF)
            {
                try { ReadInput(message.LParam); }
                catch (Exception ex) { Write("Lesefehler: " + ex.Message); }
            }
            base.WndProc(ref message);
        }

        void ReadInput(IntPtr raw)
        {
            uint size = 0, headerSize = (uint)Marshal.SizeOf<RawHeader>();
            if (GetRawInputData(raw, 0x10000003, IntPtr.Zero, ref size, headerSize) == uint.MaxValue || size < headerSize || size > 65536) return;
            IntPtr data = Marshal.AllocHGlobal((int)size);
            try {
                if (GetRawInputData(raw, 0x10000003, data, ref size, headerSize) == uint.MaxValue) return;
                var header = Marshal.PtrToStructure<RawHeader>(data);
                uint length = 0;
                GetRawInputDeviceInfo(header.Device, 0x20000007, null, ref length);
                if (length == 0 || length > 8192) return;
                var name = new StringBuilder((int)length + 1);
                if (GetRawInputDeviceInfo(header.Device, 0x20000007, name, ref length) == uint.MaxValue) return;
                string device = name.ToString();
                if (device.IndexOf("VID_1189&PID_8890", StringComparison.OrdinalIgnoreCase) < 0) return;
                IntPtr body = IntPtr.Add(data, (int)headerSize);
                string detail;
                if (header.Type == 1 && size >= headerSize + 16) {
                    ushort key = (ushort)Marshal.ReadInt16(body, 6);
                    ushort flags = (ushort)Marshal.ReadInt16(body, 2);
                    if ((flags & 1) == 0 && string.Equals(((Keys)key).ToString(), expected[step], StringComparison.Ordinal)) matched[step] = true;
                    detail = "Tastatur: " + ((Keys)key).ToString() + ((flags & 1) == 0 ? " gedrückt" : " losgelassen") + " (VK=" + key + ", Scan=" + (ushort)Marshal.ReadInt16(body) + ", Flags=" + flags + ")";
                } else if (header.Type == 0 && size >= headerSize + 24) {
                    ushort buttons = (ushort)Marshal.ReadInt16(body, 4);
                    if (buttons == 0) return;
                    detail = "Maus/Scrollen: Flags=0x" + buttons.ToString("X4") + ", Wert=" + Marshal.ReadInt16(body, 6);
                } else if (header.Type == 2 && size >= headerSize + 8) {
                    uint reportSize = (uint)Marshal.ReadInt32(body), count = (uint)Marshal.ReadInt32(body, 4);
                    ulong total = (ulong)reportSize * count;
                    if (total > size - headerSize - 8) return;
                    byte[] bytes = new byte[(int)total];
                    Marshal.Copy(IntPtr.Add(body, 8), bytes, 0, bytes.Length);
                    detail = "Medien-/HID-Signal: " + BitConverter.ToString(bytes);
                } else return;
                observed[step] = true;
                Write("[" + (step + 1) + ": " + steps[step] + "] " + detail);
            } finally { Marshal.FreeHGlobal(data); }
        }
    }
}

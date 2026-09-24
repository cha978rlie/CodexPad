using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CodexPad
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string folder = AppContext.BaseDirectory;
            bool check = args.Contains("--self-test");
            try {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string config = Path.Combine(folder, "codexpad-config.json");
                if (args.Contains("--logic-test")) {
                    File.WriteAllText(Path.Combine(folder, "Product-Testresultat.txt"), ProductTests.Run());
                    return 0;
                }
                if (check) {
                    using (var form = new PadForm(config, false, true)) {
                        string result = PadForm.SelfTest(config) + Environment.NewLine + form.SelfTestHotkeys();
                        File.WriteAllText(Path.Combine(folder, "Build-Testresultat.txt"), DateTime.Now.ToString("s") + Environment.NewLine + result);
                    }
                    return 0;
                }
                if (args.Contains("--pad-test")) {
                    Application.Run(new CodexPadDiagnostic.TestWindow(folder));
                    return 0;
                }
                if (args.Contains("--screenshot")) {
                    using (var form = new PadForm(config, false, true)) {
                        form.Show();
                        Application.DoEvents();
                        form.PreparePages();
                        if (args.Contains("--bindings")) form.SelectBindingsForPreview();
                        Application.DoEvents();
                        using (var image = new Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            image.Save(Path.Combine(folder, "CodexPad-Vorschau.png"));
                        }
                    }
                    return 0;
                }
                bool created;
                using (var single = new Mutex(true, "Local\\CodexPad", out created)) {
                    if (!created) {
                        for (int attempt = 0; attempt < 10; attempt++) {
                            try { using var signal = EventWaitHandle.OpenExisting(args.Contains("--diagnostics") ? "Local\\CodexPad.Diagnostics" : "Local\\CodexPad.Show"); signal.Set(); return 0; }
                            catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
                        }
                        return 0;
                    }
                    try {
                        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexPad.Show");
                        using var diagnostics = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexPad.Diagnostics");
                        using var form = new PadForm(config, args.Contains("--autostart"));
                        if (args.Contains("--diagnostics")) form.Shown += (s, e) => form.ShowDiagnostics();
                        using var reveal = new System.Windows.Forms.Timer { Interval = 200 };
                        reveal.Tick += (s, e) => { if (signal.WaitOne(0)) form.RestoreWindow(); if (diagnostics.WaitOne(0)) form.ShowDiagnostics(); };
                        reveal.Start();
                        Application.Run(form);
                    }
                    finally { single.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception error) {
                File.WriteAllText(Path.Combine(folder, check ? "Build-Testresultat.txt" : "CodexPad-Startfehler.txt"), error.ToString());
                if (!check && !args.Contains("--logic-test")) MessageBox.Show(error.Message, "CodexPad – Startfehler");
                return 1;
            }
        }
    }
}

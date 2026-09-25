using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace CodexPad
{
    internal static class AutoStart
    {
        private const string TaskName = "CodexPad";

        internal static string TaskXml(string program, string userSid, string userName)
        {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var task = new XElement(ns + "Task", new XAttribute("version", "1.3"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Startet CodexPad nach der Windows-Anmeldung im Hintergrund.")),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "RestartOnFailure",
                        new XElement(ns + "Count", "3"), new XElement(ns + "Interval", "PT1M")),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "Enabled", "true")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Delay", "PT20S"),
                        new XElement(ns + "UserId", userName))),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", program),
                        new XElement(ns + "Arguments", "--autostart"),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(program)))));
            return new XDocument(new XDeclaration("1.0", "utf-16", null), task).ToString();
        }

        private static int Scheduler(params string[] args)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe")) {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in args) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows-Aufgabenplanung konnte nicht gestartet werden.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000)) {
                process.Kill();
                throw new TimeoutException("Windows-Aufgabenplanung antwortet nicht.");
            }
            if (process.ExitCode != 0 && args[0] != "/Query")
                throw new InvalidOperationException("Windows-Autostart konnte nicht eingerichtet werden: " + (error.Length > 0 ? error : output).Trim());
            return process.ExitCode;
        }

        internal static void Update(bool enabled, string program)
        {
            if (!enabled) {
                if (Scheduler("/Query", "/TN", TaskName) == 0)
                    Scheduler("/Delete", "/TN", TaskName, "/F");
                return;
            }
            using var identity = WindowsIdentity.GetCurrent();
            string path = Path.Combine(Path.GetTempPath(), "codexpad-autostart-" + Guid.NewGuid().ToString("N") + ".xml");
            try {
                File.WriteAllText(path, TaskXml(program, identity.User.Value, identity.Name), Encoding.Unicode);
                Scheduler("/Create", "/TN", TaskName, "/XML", path, "/F");
            } finally {
                try { File.Delete(path); } catch { }
            }
        }
    }
}

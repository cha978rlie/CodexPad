using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodexPad
{
    internal static class ProductTests
    {
        public static string Run()
        {
            int count = 0;
            void Check(bool condition, string name) { if (!condition) throw new Exception("Test fehlgeschlagen: " + name); count++; }
            long point = ChatScroller.PackPoint(-1700, -200).ToInt64();
            Check(unchecked((short)(point & 0xffff)) == -1700 && unchecked((short)((point >> 16) & 0xffff)) == -200,
                "Gezieltes Scrollen: negative Bildschirmkoordinaten bleiben erhalten");
            Check(!DictationSelection.InComposerArea(0, 0, 0, 0, 0, 0, 1200, 900, true, false), "Leere UIA-Duplikate ignoriert");
            Check(!DictationSelection.InComposerArea(400, -800, 30, 30, 0, 0, 1200, 900, true, false), "Außerhalb liegende Knöpfe ignoriert");
            Check(DictationSelection.InComposerArea(900, 800, 30, 30, 0, 0, 1200, 900, true, false), "Knopf am sichtbaren Nachrichtenfeld erkannt");
            var now = DateTime.UtcNow;
            var a = new TaskInfo { id = "a", title = "A", status = "completed", unread = true, recency = 10 };
            var b = new TaskInfo { id = "b", title = "B", status = "completed", unread = true, recency = 20 };
            var c = new TaskInfo { id = "c", title = "C", status = "running", recency = 30 };
            var state = new PadSnapshot { unread_known = true, tasks = new List<TaskInfo> { a, b, c } };
            var cycle = new TaskCycle();
            Check(cycle.Next(state, now).id == "b", "Ungelesen vor laufend, neuestes zuerst");
            b.unread = false; c.recency = 40;
            Check(cycle.Next(state, now.AddSeconds(1)).id == "a", "Lesen ändert laufende Runde nicht");
            Check(cycle.Next(state, now.AddSeconds(2)).id == "c", "Laufende Aufgaben folgen");
            Check(cycle.Next(state, now.AddSeconds(3)).id == "b", "Runde beginnt erneut");
            Check(cycle.Next(state, now.AddSeconds(14)).id == "a", "Nach zehn Sekunden neue Liste");
            a.unread = false; c.status = "completed";
            Check(new TaskCycle().Next(state, now).id == "c", "Letzte Aufgaben als Ersatz");
            Check(new TaskCycle().Next(new PadSnapshot(), now) == null, "Leere Liste");
            var led = new LedPolicy();
            Check(led.Desired(true, true, true, now) == 1, "Ungelesene Ergebnisse leuchten");
            Check(led.Desired(true, true, true, now.AddSeconds(1)) == 1, "Ein weiteres ungelesenes Ergebnis hält LED an");
            Check(led.Desired(true, true, false, now.AddSeconds(2)) == 0, "Alle gelesen schaltet aus");
            led.Desired(true, true, true, now);
            Check(led.Desired(true, false, false, now.AddSeconds(1)) == 1, "Kurzer Statusausfall hält Anzeige");
            Check(led.Desired(true, false, false, now.AddSeconds(15)) == 1, "Gnadenfrist 14 Sekunden");
            Check(led.Desired(true, false, false, now.AddSeconds(16)) == 0, "Nach 15 Sekunden unbekannt aus");
            Check(led.Desired(true, true, true, now.AddSeconds(17)) == 1, "Erholung nach Ausfall");
            Check(led.Desired(false, true, true, now) == 0, "Deaktivierung schaltet aus");
            Check(led.Desired(true, true, true, now, 2) == 2, "Modus 2 bei ungelesenem Ergebnis");
            Check(led.Desired(true, false, false, now.AddSeconds(1), 1) == 1, "Effektwechsel während kurzer Statuslücke");
            Check(led.Desired(true, true, false, now.AddSeconds(2), 2) == 0, "Modus 2 nach Lesen aus");
            Check(Shortcut.Parse("Strg+D").SequenceEqual(new byte[] { 0x11, 0x44 }), "Strg D");
            Check(Shortcut.Parse("Enter").SequenceEqual(new byte[] { 13 }), "Einfaches Enter");
            Check(Shortcut.Parse("F19").SequenceEqual(new byte[] { 0x82 }), "Drehrad-Ausgabe F19");
            Check(Shortcut.Parse("Win+Shift+7").SequenceEqual(new byte[] { 0x10, 0x5B, 0x37 }), "Mehrere Zusatztasten");
            foreach (string invalid in new[] { "", "Strg", "Strg+Alt+Delete", "Win+L", "A+B" }) {
                bool rejected = false; try { Shortcut.Parse(invalid); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Ungeeignetes Kürzel abgewiesen: " + invalid);
            }
            string dir = Path.Combine(Path.GetTempPath(), "CodexPad-Test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
            try {
                string file = Path.Combine(dir, "config.json");
                File.WriteAllText(file, "{\"Bindings\":[{\"Input\":\"F13\",\"Action\":\"latest\"},{\"Input\":\"F14\",\"Action\":\"dictation\"},{\"Input\":\"F15\",\"Action\":\"enter\"},{\"Input\":\"F16\",\"Action\":\"effortDown\"},{\"Input\":\"F17\",\"Action\":\"effortUp\"},{\"Input\":\"F18\",\"Action\":\"effortMenu\"}],\"CompletionLed\":true}");
                var config = SettingsStore.Read(file);
                Check(config.Version == 2 && config.Bindings[0].Action == "cycle", "Gezielte Migration links");
                Check(config.Bindings[2].Action == "enter" && config.Bindings[5].Action == "effortMenu" && config.CompletionLed, "Bestehende Belegung erhalten");
                Check(config.NotificationLedMode == 1, "Alte Einstellungen behalten Modus 1");
                config.NotificationLedMode = 2;
                var draft = SettingsStore.Copy(config); draft.Bindings[0].Action = "shortcut";
                Check(config.Bindings[0].Action == "cycle", "Entwurf verändert aktive Einstellungen nicht");
                SettingsStore.Write(file, config);
                Check(Directory.GetFiles(Path.Combine(dir, "Einstellungen-Sicherungen")).Length == 1, "Sicherung vor Übernahme");
                Check(SettingsStore.Read(file).NotificationLedMode == 2, "LED-Auswahl bleibt beim Speichern erhalten");
                Check(SettingsStore.Read(file).Bindings[2].Action == "enter", "Export und Import erhalten einfaches Enter");
            } finally { Directory.Delete(dir, true); }
            return "OK: " + count + " Prüfungen (Aufgabenrunde, LED, Tastenkürzel, Migration und Sicherung).";
        }
    }
}

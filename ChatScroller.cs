using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace CodexPad
{
    internal static class ChatScroller
    {
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref Native.Point point);
        [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr parent, Native.Point point, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(
            IntPtr window, uint message, UIntPtr wparam, IntPtr lparam, uint flags, uint timeout, out UIntPtr result);

        internal static IntPtr PackPoint(int x, int y)
        {
            if (x < short.MinValue || x > short.MaxValue || y < short.MinValue || y > short.MaxValue)
                throw new InvalidOperationException("Diese Bildschirmposition wird vom Windows-Mausradprotokoll nicht unterstützt.");
            return new IntPtr(unchecked((int)((uint)(ushort)x | ((uint)(ushort)y << 16))));
        }

        public static string Scroll(IntPtr window, bool up)
        {
            if (!Native.GetWindowRect(window, out var app)) throw new InvalidOperationException("Fensterposition unbekannt.");
            var root = AutomationElement.FromHandle(window);
            var edits = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            var composers = new List<AutomationElement>();
            var seen = new HashSet<Rect>();
            foreach (AutomationElement edit in edits) {
                var info = edit.Current;
                var box = info.BoundingRectangle;
                if (!(info.ClassName ?? "").Split(' ').Contains("ProseMirror") && info.Name != "Mit ChatGPT arbeiten") continue;
                if (info.IsOffscreen || !info.IsEnabled || box.Width < 180 || box.Height <= 0 ||
                    box.Left < app.Left || box.Right > app.Right + 2 || box.Top < app.Top + (app.Bottom - app.Top) * .45 || box.Bottom > app.Bottom + 2) continue;
                if (seen.Add(box)) composers.Add(edit);
            }
            // Use a focused composer in split views, otherwise require a unique visible one.
            var focused = AutomationElement.FocusedElement;
            var selected = composers.Where(e => focused != null && Automation.Compare(e, focused)).ToList();
            if (selected.Count == 1) composers = selected;
            if (composers.Count != 1) throw new InvalidOperationException(
                composers.Count == 0 ? "Nachrichtenfeld nicht erkannt; bitte eine Codex-Aufgabe öffnen." :
                "Mehrere Nachrichtenfelder sichtbar; bitte das gewünschte Feld einmal auswählen.");
            var composer = composers[0].Current.BoundingRectangle;
            double x = composer.Left + composer.Width / 2;
            double y = app.Top + (composer.Top - app.Top) * .5;
            if (y < app.Top + 80 || y >= composer.Top - 20)
                throw new InvalidOperationException("Der Nachrichtenbereich ist zu klein zum gezielten Scrollen.");
            var point = new Point(x, y);
            var regions = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
            var choices = new List<(Rect bounds, ScrollPattern pattern)>();
            foreach (AutomationElement region in regions) {
                var info = region.Current;
                var box = info.BoundingRectangle;
                if (info.IsOffscreen || !info.IsEnabled || box.Height < 160 || !box.Contains(point) ||
                    box.Top < app.Top || box.Bottom > composer.Top + 20 || box.Width < composer.Width * .7) continue;
                if (region.TryGetCurrentPattern(ScrollPattern.Pattern, out var raw) && ((ScrollPattern)raw).Current.VerticallyScrollable)
                    choices.Add((box, (ScrollPattern)raw));
            }
            if (Native.GetForegroundWindow() != window) throw new InvalidOperationException("Codex hat den Fokus verloren; nicht gescrollt.");
            if (choices.Count > 0) {
                // Prefer the transcript viewport over nested code blocks or result cards.
                var chosen = choices.OrderByDescending(c => c.bounds.Width * c.bounds.Height).First();
                chosen.pattern.Scroll(ScrollAmount.NoAmount, up ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement);
                return "direkt";
            }
            // Chromium does not always expose a ScrollPattern. Address a wheel message
            // directly to its child window at a virtual point in the transcript. Unlike
            // SendInput, this is independent of the real cursor. No cursor move or click.
            IntPtr recipient = window;
            for (int depth = 0; depth < 12; depth++) {
                var local = new Native.Point { X = (int)x, Y = (int)y };
                if (!ScreenToClient(recipient, ref local)) break;
                IntPtr child = ChildWindowFromPointEx(recipient, local, 1 | 2 | 4);
                if (child == IntPtr.Zero || child == recipient) break;
                recipient = child;
            }
            uint wheel = unchecked((uint)(up ? 120 : -120)) << 16;
            if (Native.GetForegroundWindow() != window) throw new InvalidOperationException("Codex hat den Fokus verloren; nicht gescrollt.");
            if (SendMessageTimeout(recipient, 0x020A, new UIntPtr(wheel), PackPoint((int)x, (int)y), 2, 300, out _) == IntPtr.Zero)
                throw new InvalidOperationException("Codex hat den gezielten Scrollbefehl nicht angenommen.");
            return "Fensternachricht";
        }
    }
}

using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CodexPad
{
    public sealed partial class PadForm
    {
        private readonly ComboBox notificationLedMode = new PadCombo();
        private readonly Panel[] pages = new Panel[3];
        private readonly PadButton[] navigation = new PadButton[3];
        private readonly PictureBox[] actionIcons = new PictureBox[6];
        private readonly PadIllustration padPicture = new PadIllustration();
        private readonly ToolTip help = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400 };
        private readonly string[] controlNames = { "Taste links", "Taste Mitte", "Taste rechts", "Drehen links", "Drehen rechts", "Drehrad drücken" };
        private void SelectPage(int index)
        {
            var parent = pages[index].Parent;
            parent.SuspendLayout();
            for (int i = 0; i < 3; i++) {
                pages[i].Visible = i == index;
                navigation[i].Selected = i == index;
                navigation[i].Invalidate();
            }
            pages[index].BringToFront();
            parent.ResumeLayout(true);
        }
        private static void PrepareControls(Control parent)
        {
            foreach (Control child in parent.Controls) {
                _ = child.Handle;
                PrepareControls(child);
            }
        }
        internal void PreparePages()
        {
            PrepareControls(pages[1]);
            PrepareControls(pages[2]);
        }
        private Label TextLabel(string text, float size = 10, bool bold = false, bool muted = false)
            => new Label { Text = text, AutoSize = false, Dock = DockStyle.Fill, ForeColor = muted ? PadTheme.Muted : PadTheme.Text,
                Font = PadTheme.UiFont(size, bold ? FontStyle.Bold : FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft };
        private Control Heading(string eyebrow, string title, string description)
        {
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = new Padding(0) };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); header.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var tag = TextLabel(eyebrow, 8, true); tag.ForeColor = PadTheme.Accent;
            header.Controls.Add(tag); header.Controls.Add(TextLabel(title, 22, true)); header.Controls.Add(TextLabel(description, 9.5f, muted: true));
            return header;
        }
        private TableLayoutPanel Stack(params int[] heights)
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length, Padding = new Padding(0), Margin = new Padding(0) };
            foreach (int height in heights) table.RowStyles.Add(new RowStyle(height < 0 ? SizeType.Percent : SizeType.Absolute, height < 0 ? 100 : height));
            return table;
        }
        private void BuildUi()
        {
            Text = "CodexPad"; Size = new Size(1220, 900); MinimumSize = new Size(1120, 820);
            StartPosition = FormStartPosition.CenterScreen; Font = PadTheme.UiFont(10);
            AutoScaleMode = AutoScaleMode.None; BackColor = PadTheme.Background; ForeColor = PadTheme.Text;
            string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "codexpad.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);
            HandleCreated += (s, e) => PadTheme.DarkTitle(Handle);
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0), Margin = new Padding(0) };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var rail = new Panel { Dock = DockStyle.Fill, BackColor = PadTheme.Surface, Margin = new Padding(0), Padding = new Padding(14, 24, 14, 18) };
            var brand = new TableLayoutPanel { Dock = DockStyle.Top, Height = 85, ColumnCount = 2, RowCount = 2 };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42)); brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var logo = new PictureBox { Image = PadTheme.Glyph("pad", PadTheme.Accent, 34), Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.CenterImage };
            brand.Controls.Add(logo); brand.Controls.Add(TextLabel("CodexPad", 14, true));
            var sub = TextLabel("DEIN PAD. DEIN WORKFLOW.", 7.8f, muted: true); brand.Controls.Add(sub, 0, 1); brand.SetColumnSpan(sub, 2);
            var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 182, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
            string[] names = { "Übersicht", "Belegung", "Gerät & Hilfe" }, icons = { "pad", "key", "settings" };
            for (int i = 0; i < 3; i++) {
                int page = i; navigation[i] = new PadButton { Text = names[i], Symbol = icons[i], Width = 175, Height = 44, Margin = new Padding(0, 0, 0, 10) };
                navigation[i].Click += (s, e) => SelectPage(page); nav.Controls.Add(navigation[i]);
            }
            var footer = TextLabel("WINDOWS · LOKAL\n0.3.1 Preview\n\nCommunity-Projekt\nKeine offizielle OpenAI-App", 8.5f, muted: true); footer.Dock = DockStyle.Bottom; footer.Height = 100;
            rail.Controls.Add(nav); rail.Controls.Add(brand); rail.Controls.Add(footer); shell.Controls.Add(rail);
            var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(24, 22, 24, 14), Margin = new Padding(0) };
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            var content = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            for (int i = 0; i < 3; i++) { pages[i] = new Panel { Dock = DockStyle.Fill, BackColor = PadTheme.Background }; content.Controls.Add(pages[i]); }
            state.Dock = DockStyle.Fill; state.ForeColor = PadTheme.Muted; state.Font = PadTheme.UiFont(9); state.TextAlign = ContentAlignment.MiddleLeft;
            main.Controls.Add(content); main.Controls.Add(state); shell.Controls.Add(main); Controls.Add(shell);
            BuildOverview(); BuildEditor(); BuildDevicePage();
            tray.Icon = Icon; tray.Text = "CodexPad"; tray.Visible = !previewMode;
            var menu = new ContextMenuStrip { BackColor = PadTheme.Surface, ForeColor = PadTheme.Text };
            menu.Items.Add("Fenster öffnen", null, (s, e) => RestoreWindow());
            menu.Items.Add("Aktivieren", null, (s, e) => StartListening(false));
            menu.Items.Add("Pausieren", null, (s, e) => StopListening());
            menu.Items.Add("Beenden", null, (s, e) => { exitRequested = true; Close(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += (s, e) => RestoreWindow();
            Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) Hide(); };
            FormClosing += (s, e) => {
                if (!exitRequested && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
                statusTimer.Stop(); windowTimer.Stop(); StopListening(); backend?.Dispose(); tray.Visible = false; tray.Dispose(); help.Dispose();
            };
            SelectPage(0);
        }
        private void BuildOverview()
        {
            var home = Stack(90, 88, 167, 148, 32, -1, 48);
            home.Controls.Add(Heading("DEINE STEUERZENTRALE", "Weniger klicken. Mehr machen.", "Deine Belegung, dein Gerät und offene Ergebnisse auf einen Blick."));
            var badges = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
            for (int i = 0; i < 3; i++) badges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            string[] labels = { "BEDIENUNG", "VERBINDUNG", "ZIEL" }; Label[] states = { activeState, deviceState, appState };
            for (int i = 0; i < 3; i++) {
                var card = new Panel { Dock = DockStyle.Fill, BackColor = PadTheme.Surface, Padding = new Padding(12, 8, 10, 6), Margin = new Padding(i == 0 ? 0 : 6, 0, i == 2 ? 0 : 6, 0) };
                var caption = TextLabel(labels[i], 8, true, true); caption.Dock = DockStyle.Top; caption.Height = 22;
                states[i].Dock = DockStyle.Fill; states[i].Font = PadTheme.UiFont(9); states[i].ForeColor = PadTheme.Text;
                card.Controls.Add(states[i]); card.Controls.Add(caption); badges.Controls.Add(card);
            }
            activeState.Text = "Tasten pausiert"; deviceState.Text = "Pad wird gesucht …"; appState.Text = "Codex wird gesucht …";
            home.Controls.Add(badges); padPicture.Dock = DockStyle.Fill; padPicture.Margin = new Padding(0, 4, 0, 8); home.Controls.Add(padPicture);
            var mapping = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 0, 0, 8) };
            for (int i = 0; i < 3; i++) mapping.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int i = 0; i < 2; i++) mapping.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            for (int i = 0; i < 6; i++) {
                var card = new Panel { Dock = DockStyle.Fill, BackColor = PadTheme.Surface, Margin = new Padding(i % 3 == 0 ? 0 : 5, 4, i % 3 == 2 ? 0 : 5, 4), Padding = new Padding(8) };
                actionIcons[i] = new PictureBox { Width = 37, Dock = DockStyle.Left, SizeMode = PictureBoxSizeMode.CenterImage };
                activeLabels[i] = new Label { Dock = DockStyle.Fill, ForeColor = PadTheme.Text, TextAlign = ContentAlignment.MiddleLeft,
                    Font = PadTheme.UiFont(9), Tag = controlNames[i], Padding = new Padding(5, 0, 0, 0) };
                card.Controls.Add(activeLabels[i]); card.Controls.Add(actionIcons[i]); mapping.Controls.Add(card);
            }
            home.Controls.Add(mapping);
            ledState.Dock = DockStyle.Fill; ledState.ForeColor = PadTheme.Accent; ledState.TextAlign = ContentAlignment.MiddleLeft;
            ledState.Font = PadTheme.UiFont(9.5f, FontStyle.Bold); ledState.Text = "LED · Status wird geprüft …"; home.Controls.Add(ledState);
            unreadTasks.Dock = DockStyle.Fill; unreadTasks.BackColor = PadTheme.Surface; unreadTasks.ForeColor = PadTheme.Muted;
            unreadTasks.BorderStyle = BorderStyle.None; unreadTasks.Font = PadTheme.UiFont(9.5f); unreadTasks.HorizontalScrollbar = true;
            home.Controls.Add(unreadTasks);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            AddButton(actions, "Aktivieren", () => StartListening(false)); AddButton(actions, "Pausieren", StopListening);
            AddButton(actions, "Belegung ändern", () => SelectPage(1)); home.Controls.Add(actions); pages[0].Controls.Add(home);
        }
        private void BuildEditor()
        {
            pages[1].AutoScroll = true;
            var editor = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0), Margin = new Padding(0) };
            var header = Heading("DEINE BELEGUNG", "Jede Taste hat einen Job.", "Aktion wählen, bei Bedarf ein Kürzel aufnehmen und Änderungen übernehmen."); header.Height = 90; header.Dock = DockStyle.Top; editor.Controls.Add(header);
            var table = new TableLayoutPanel { Dock = DockStyle.Top, Height = 340, ColumnCount = 6, RowCount = 7, BackColor = PadTheme.Surface, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 14) };
            foreach (int width in new[] { 120, 260, 160, 148, 100, 68 }) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); for (int i = 0; i < 6; i++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6));
            foreach (string title in new[] { "BEDIENELEMENT", "AKTION", "KÜRZEL / AUFGABE", "ZIEL", "", "" }) table.Controls.Add(TextLabel(title, 7.7f, true, true));
            for (int i = 0; i < 6; i++) {
                int row = i; table.Controls.Add(TextLabel(controlNames[i], 9));
                inputBoxes[i] = NewCombo(Inputs); PadTheme.StyleCombo(inputBoxes[i]);
                actionBoxes[i] = NewCombo(Actions); PadTheme.StyleCombo(actionBoxes[i]); actionBoxes[i].Font = PadTheme.UiFont(9);
                targetBoxes[i] = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(5, 11, 5, 5), BackColor = PadTheme.Raised, ForeColor = PadTheme.Text, BorderStyle = BorderStyle.FixedSingle, Font = PadTheme.UiFont(10) };
                destinations[i] = NewCombo(new System.Collections.Generic.List<Choice> { new Choice("codex", "Codex / Work"), new Choice("active", "Aktuelles Programm") }); PadTheme.StyleCombo(destinations[i]); destinations[i].Font = PadTheme.UiFont(9);
                var record = new PadButton { Text = "Kürzel", Symbol = "record", Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5), Font = PadTheme.UiFont(9) };
                record.Click += (s, e) => RecordShortcut(row); help.SetToolTip(record, "Tastenkürzel aufnehmen: drücken und loslassen. Ändert die Aktion dieser Zeile.");
                testButtons[i] = new PadButton { Text = "Test", Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5), Font = PadTheme.UiFont(9) };
                testButtons[i].Click += async (s, e) => await TestBinding(row);
                actionBoxes[i].SelectedIndexChanged += (s, e) => {
                    string action = (actionBoxes[row].SelectedItem as Choice)?.Id;
                    targetBoxes[row].Enabled = action == "shortcut" || action == "thread"; destinations[row].Enabled = action == "shortcut";
                    help.SetToolTip(actionBoxes[row], actionBoxes[row].Text);
                };
                table.Controls.Add(actionBoxes[i]); table.Controls.Add(targetBoxes[i]); table.Controls.Add(destinations[i]); table.Controls.Add(record); table.Controls.Add(testButtons[i]);
            }
            editor.Controls.Add(table);
            var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
            autoStart.Text = "Mit Windows im Hintergrund starten"; activateOnLaunch.Text = "Tasten beim Programmstart aktivieren";
            completionLed.Text = "LED für fertige, ungelesene Ergebnisse"; showTaskNotice.Text = "Aufgabennamen beim Wechseln kurz anzeigen";
            controlEnter.Text = "Bei „Sicher senden“ Strg+Enter statt Enter verwenden";
            foreach (var box in new[] { autoStart, activateOnLaunch, completionLed, showTaskNotice, controlEnter }) { box.AutoSize = true; box.ForeColor = PadTheme.Text; box.Font = PadTheme.UiFont(9.5f); box.Margin = new Padding(2, 3, 2, 3); options.Controls.Add(box); }
            var ledOptions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
            ledOptions.Controls.Add(new Label { Text = "Benachrichtigungseffekt", AutoSize = true, ForeColor = PadTheme.Text, Font = PadTheme.UiFont(9.5f), Margin = new Padding(2, 7, 12, 0) });
            notificationLedMode.DropDownStyle = ComboBoxStyle.DropDownList;
            notificationLedMode.Items.AddRange(new object[] { "Modus 1 · Ruhiger Farbeffekt", "Modus 2 · LEDs nacheinander" });
            notificationLedMode.Width = 300; notificationLedMode.Font = PadTheme.UiFont(9.5f); PadTheme.StyleCombo(notificationLedMode);
            ledOptions.Controls.Add(notificationLedMode); options.Controls.Add(ledOptions);
            editor.Controls.Add(options);
            var expert = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Visible = false, Margin = new Padding(0, 0, 0, 8), BackColor = PadTheme.Surface, Padding = new Padding(8) };
            var explanation = TextLabel("Nur für andere Hardware: Diese Signale empfängt die App vom Pad. Normalerweise F13–F18 unverändert lassen.", 9, muted: true); explanation.Height = 40; explanation.Width = 750; explanation.Dock = DockStyle.None; expert.Controls.Add(explanation); expert.SetFlowBreak(explanation, true);
            for (int i = 0; i < 6; i++) {
                var column = new Panel { Width = 125, Height = 68 };
                var caption = TextLabel(controlNames[i], 8.5f, muted: true); caption.Dock = DockStyle.Top; caption.Height = 24;
                inputBoxes[i].Dock = DockStyle.Bottom; column.Controls.Add(caption); column.Controls.Add(inputBoxes[i]); expert.Controls.Add(column);
            }
            var toggle = new PadButton { Text = "Empfangene Pad-Tasten · Erweitert", Symbol = "settings", Height = 34, Width = 310, Margin = new Padding(0, 2, 0, 8) };
            toggle.Click += (s, e) => { expert.Visible = !expert.Visible; toggle.Text = expert.Visible ? "Erweiterte Einstellungen schließen" : "Empfangene Pad-Tasten · Erweitert"; };
            editor.Controls.Add(toggle); editor.Controls.Add(expert);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, AutoSize = true, Margin = new Padding(0) };
            AddButton(buttons, "Übernehmen", () => SaveConfig()); AddButton(buttons, "Verwerfen", () => { ApplyConfig(); Note("Nicht übernommene Änderungen verworfen."); });
            AddButton(buttons, "Exportieren", ExportSettings); AddButton(buttons, "Importieren", ImportSettings); editor.Controls.Add(buttons); pages[1].Controls.Add(editor);
        }
        private void BuildDevicePage()
        {
            var layout = Stack(90, 112, 54, 76, 46, -1);
            layout.Controls.Add(Heading("GERÄT & HILFE", "Alles verbunden?", "Einrichtung, Funktionstests und verständliche Diagnose an einem Ort."));
            var guide = TextLabel("ERSTE SCHRITTE\n1  Pad direkt per USB anschließen.\n2  Bei einem neuen, passenden Pad: „Pad einrichten“, danach alle Eingaben testen.\n3  Belegung auswählen, übernehmen und auf der Übersicht aktivieren.", 10); guide.BackColor = PadTheme.Surface; guide.Padding = new Padding(12); layout.Controls.Add(guide);
            var deviceButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            AddButton(deviceButtons, "Pad prüfen", async () => await DeviceCommand(false));
            AddButton(deviceButtons, "Pad einrichten", async () => {
                using var dialog = new SetupConsent();
                if (dialog.ShowDialog(this) == DialogResult.OK) await DeviceCommand(true);
            });
            AddButton(deviceButtons, "Eingaben testen", () => { StopListening(); LaunchHelper("PadTest.ps1", false); }); layout.Controls.Add(deviceButtons);
            layout.Controls.Add(TextLabel("LED-TEST\nDer Effekt läuft zehn Sekunden. Danach übernimmt wieder die Automatik.\nDas Pad unterstützt eingebaute Effekte, keine frei wählbare Farbe pro Taste.", 9.5f, muted: true));
            var tests = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            AddButton(tests, "LED aus", async () => await TestLed(0)); AddButton(tests, "Modus 1 testen", async () => await TestLed(1)); AddButton(tests, "Modus 2 testen", async () => await TestLed(2));
            AddButton(tests, "Anleitung", () => {
                string doc = Path.Combine(AppContext.BaseDirectory, "README.md");
                try { Process.Start(new ProcessStartInfo(doc) { UseShellExecute = true }); } catch (Exception e) { Note("Anleitung nicht geöffnet: " + e.Message); }
            }); layout.Controls.Add(tests);
            var diagnostics = Stack(66, -1);
            diagnostics.Controls.Add(TextLabel("DIAGNOSE\nLokale Codex-Dateien können sich ändern. Cloud-Aufgaben und Eingabefreigaben werden noch nicht zuverlässig erfasst.", 9, muted: true));
            log.Dock = DockStyle.Fill; log.BackColor = PadTheme.Surface; log.ForeColor = PadTheme.Muted; log.BorderStyle = BorderStyle.None; log.HorizontalScrollbar = true;
            diagnostics.Controls.Add(log); layout.Controls.Add(diagnostics); pages[2].Controls.Add(layout);
        }
    }

    internal sealed class SetupConsent : Form
    {
        public SetupConsent()
        {
            Text = "Pad einrichten"; ClientSize = new Size(530, 220); StartPosition = FormStartPosition.CenterParent;
            BackColor = PadTheme.Background; ForeColor = PadTheme.Text; Font = PadTheme.UiFont(10);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Controls.Add(new Label { Text = "Die sechs Tastenbelegungen im angeschlossenen Pad werden auf F13–F18 geändert.\n\nUnterstützt wird das geprüfte 3-Tasten-Pad mit einem Drehregler (1189:8890). Bestehende Hardwarebelegungen werden überschrieben; sie können nicht ausgelesen oder gesichert werden.", Dock = DockStyle.Fill, Padding = new Padding(20) });
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(14, 8, 14, 8), FlowDirection = FlowDirection.RightToLeft };
            var accept = new PadButton { Text = "Pad einrichten", Primary = true, Width = 160, Height = 38 }; accept.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            var cancel = new PadButton { Text = "Abbrechen", Width = 130, Height = 38 }; cancel.Click += (s, e) => Close(); buttons.Controls.Add(accept); buttons.Controls.Add(cancel); Controls.Add(buttons);
            HandleCreated += (s, e) => PadTheme.DarkTitle(Handle);
        }
    }
}

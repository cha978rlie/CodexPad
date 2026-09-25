# CodexPad auf deinem PC einrichten

Diese Anleitung ist für die fertige Windows-App. Du musst nicht programmieren und nichts selbst kompilieren.

## 1. Das brauchst du

- Einen Windows-PC mit x64-Prozessor. Getestet wurde Windows 11 x64; ARM64 ist nicht getestet.
- Für die Codex-Funktionen: die bereits eingerichtete Codex/Work-Desktop-App. CodexPad installiert diese nicht mit.
- Ein unterstütztes USB-Pad: getestet wurde das SinLoon-Modell mit drei Tasten und einem drückbaren Drehregler (1189:8890). Ähnlich aussehende Modelle können anders funktionieren.
- Für Diktat: ein funktionierendes Mikrofon und eine deutschsprachige Codex-Oberfläche.

Python, .NET und die benötigten Programmdateien sind im Windows-ZIP enthalten. Eine zusätzliche Python-Installation, ein API-Schlüssel oder ein eigener Treiber sind nicht nötig.

## 2. Die richtige Datei herunterladen

Öffne die [Release-Seite](https://github.com/cha978rlie/CodexPad/releases/tag/v0.3.2) und lade unter **Assets** die Datei **CodexPad-0.3.2-win-x64.zip** herunter.

**Source code** und **CodexPad-0.3.2-source.zip** enthalten den Quellcode für Entwickler. Zum normalen Starten brauchst du das **win-x64.zip**.

**Versionshinweis:** Der Windows-Download 0.3.2 enthält die LED-Modus-Auswahl und den verbesserten Windows-Autostart. Bei bestehenden Installationen bleiben die Einstellungen erhalten.

## 3. Entpacken und starten

1. Rechtsklick auf die heruntergeladene ZIP-Datei → **Alle extrahieren**.
2. Einen dauerhaften, beschreibbaren Ordner wählen, beispielsweise **Dokumente → CodexPad**.
3. Den entpackten Ordner öffnen und **CodexPad.exe** doppelt anklicken. Alternativ funktioniert **CodexPad starten.cmd**.
4. Alle Dateien und Unterordner zusammenlassen. Insbesondere `runtime`, die DLL-Dateien und die Python-Hilfsdateien werden benötigt. Die EXE allein reicht nicht.

Es gibt keinen Installationsassistenten. Starte die App nicht direkt aus dem ZIP und verschiebe sie später nicht, solange ihr Windows-Autostart eingeschaltet ist.

Die App ist noch nicht digital signiert. Windows kann deshalb eine Warnung anzeigen. Prüfe die Downloadquelle und beachte die Sicherheitsvorgaben deines PCs; Schutzfunktionen müssen für CodexPad nicht abgeschaltet werden.

## 4. Pad anschließen und erkennen

1. Codex/Work öffnen.
2. Das Pad mit einem USB-Datenkabel anschließen, zunächst möglichst direkt am PC.
3. In CodexPad unter **Gerät & Hilfe → Pad prüfen** nachsehen, ob das Gerät erkannt wird.
4. Bei einem neuen, passenden Pad **Pad einrichten** wählen und den Hinweis lesen.

**Was „Pad einrichten“ verändert:** Die sechs Hardwarebelegungen werden auf F13 bis F18 gesetzt. Bestehende Hardwarebelegungen werden überschrieben; CodexPad kann sie vorher nicht auslesen oder sichern. Bei einem bereits so eingerichteten Pad ist dieser Schritt nicht erneut nötig. Richte damit kein unbekanntes, nur ähnlich aussehendes Gerät ein.

5. **Eingaben testen** öffnen und die drei Tasten drücken, in beide Richtungen drehen und den Drehregler drücken. Alle sechs Eingaben sollten erkannt werden.

Das Öffnen des Eingabetests pausiert die normale Tastensteuerung. Danach auf der Übersicht wieder **Aktivieren** wählen.

## 5. Belegung einschalten

Unter **Belegung** kannst du die Standardaktionen behalten oder ändern. Die kurze Anzeige des geöffneten Aufgabentitels ist optional und bei neuen Installationen zunächst ausgeschaltet:

| Eingabe | Standardaktion |
|---|---|
| Links | Wichtige lokale Aufgaben durchschalten |
| Mitte | Diktat starten oder beenden |
| Rechts | Enter an Codex senden |
| Drehen links / rechts | Im Codex-Aufgabenfenster nach oben / unten scrollen |
| Drehregler drücken | F21 an Codex senden; ohne passende Belegung in Codex hat das möglicherweise keine Wirkung |

Für ein eigenes Tastenkürzel **Kürzel** anklicken und die gewünschte Kombination drücken und loslassen. Als Ziel **Codex / Work** oder **Aktuelles Programm** wählen. Die eingehenden Pad-Signale im erweiterten Bereich bleiben normalerweise unverändert.

Anschließend **Übernehmen** und auf der Übersicht **Aktivieren** anklicken. Die Übersicht sollte eine aktive Bedienung und das angeschlossene Pad anzeigen. Probiere die Tasten zunächst mit einer unverfänglichen Aufgabe aus: Die rechte Taste kann eine eingegebene Nachricht wirklich absenden.

## 6. LED und Hintergrundbetrieb

Unter **Belegung** sind diese Optionen bei einer neuen Installation zunächst ausgeschaltet:

- **LED für fertige, ungelesene Ergebnisse:** Leuchten, sobald mindestens eine erfasste lokale Aufgabe fertig und ungelesen ist; aus, nachdem alle betreffenden Ergebnisse gelesen wurden.
- **Tasten beim Programmstart aktivieren:** Die Tastensteuerung beim Öffnen automatisch einschalten.
- **Mit Windows im Hintergrund starten:** CodexPad bei der Windows-Anmeldung starten.

Den gewünschten LED-Modus unter **Benachrichtigungseffekt** wählen. Beide Modi lassen sich unter **Gerät & Hilfe** für zehn Sekunden testen. Die gewünschten Häkchen setzen und **Übernehmen** anklicken. Für den täglichen Betrieb ohne manuelles Einschalten sind die beiden Startoptionen gemeinsam sinnvoll. CodexPad startet etwa 20 Sekunden nach der Anmeldung im Hintergrund. Prüfe das Symbol neben der Uhr; das Programmfenster erscheint dabei nicht automatisch. Auch auf Akku darf die Anmeldeaufgabe starten.

**X** versteckt das Fenster. CodexPad läuft im Hintergrund weiter. Zum Öffnen das Symbol im Infobereich neben der Uhr doppelt anklicken (gegebenenfalls unter dem kleinen Pfeil für ausgeblendete Symbole) oder CodexPad.exe nochmals starten. Vollständig beenden: Rechtsklick auf das Symbol → **Beenden**.

## 7. Einstellungen sichern oder auf einen anderen PC übertragen

Unter **Belegung → Exportieren** eine Einstellungsdatei speichern. Auf dem anderen PC die App frisch entpacken und die Datei über **Importieren** laden. Die angezeigten Einstellungen prüfen und mit **Übernehmen** aktivieren. Aufgabenspezifische Belegungen passen möglicherweise nicht zum anderen PC.

Zum Weitergeben das unveränderte Release-ZIP verwenden. Der bereits benutzte Programmordner enthält persönliche Einstellungen und Betriebsprotokolle.

## Wenn etwas nicht klappt

| Problem | Das kannst du prüfen |
|---|---|
| App nach der Anmeldung nicht sichtbar | Sie startet im Hintergrund: Symbol neben der Uhr prüfen oder CodexPad.exe erneut öffnen. Wenn sie nicht läuft, den Autostart-Schalter in **Belegung** einmal aus- und wieder einschalten und jeweils **Übernehmen** wählen. |
| Pad nicht erkannt | Direkt am PC anschließen, anderes Datenkabel oder anderen USB-Port testen; **Pad prüfen** öffnen. |
| Tasten reagieren nicht | Auf der Übersicht **Aktivieren** wählen; danach bei Bedarf **Eingaben testen**. |
| Tasten schreiben nur denselben Buchstaben | Hardwarebelegung eines neuen kompatiblen Pads über **Pad einrichten** setzen. |
| Diktat funktioniert nicht | Diktat zuerst direkt in Codex ausprobieren; Mikrofon und deutsche Oberfläche prüfen. |
| LED bleibt aus | LED-Option einschalten und übernehmen; Status auf der Übersicht ansehen; LED-Test unter **Gerät & Hilfe** nutzen. |
| „Status unbekannt“ | Die lokale Codex-Statusquelle ist nicht zuverlässig verfügbar. Nach 15 Sekunden wird die LED ausgeschaltet. |
| Nur eine EXE kopiert / Dateien fehlen | Das vollständige Windows-ZIP erneut in einen eigenen Ordner entpacken. |
| Die LED-Modus-Auswahl fehlt | Prüfen, ob tatsächlich der Windows-Download 0.3.2 entpackt wurde. |

Weitere Grenzen stehen im [README](../README.md). Für einen Fehlerbericht genügen zunächst App-Version, Windows-Version, Pad-Modell und eine kurze Beschreibung. Keine privaten Aufgabenprotokolle oder vollständigen Codex-Datenbanken öffentlich hochladen.

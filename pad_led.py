"""LED-only test for 1189:8890, MI_01, report 3, 65-byte reports.

Protocol references (checked 2026-09-23):
https://github.com/fleximus/macro_keyboard/blob/main/protocol.v
https://github.com/x0f5c3/ch57x-keyboard-tool/blob/master/src/keyboard/k8890.rs
Only B0 18 mode and AA A1 are emitted. No binding/firmware commands.
"""
import sys
import time
import json
import subprocess
from datetime import datetime
from pathlib import Path
from pad_hid import c, w, kernel, inventory, device_lock

ROOT = Path(__file__).resolve().parent
LOG = ROOT / 'LED-Test.jsonl'
MODE_LABELS = ('0 – Aus', '1 – Links rot', '2 – Farblauf')
kernel.WriteFile.argtypes = [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), c.c_void_p]
kernel.WriteFile.restype = w.BOOL

def record(**event):
    with LOG.open('a', encoding='utf-8') as file:
        file.write(json.dumps({'time': datetime.now().isoformat(), **event}, ensure_ascii=False) + '\n')

def target():
    devices = [d for d in inventory() if d.get('usage_page') == 0xff00
               and d.get('usage') == 1 and d.get('input_bytes') == 65
               and d.get('output_bytes') == 65 and d.get('output_report_ids') == [3]
               and '&mi_01#' in d['path'].lower()]
    if len(devices) != 1:
        raise RuntimeError('Es muss genau ein passendes Pad angeschlossen sein. Gefunden: ' + str(len(devices)))
    return devices[0]

def packets(mode, persist=True):
    if type(mode) is not int or mode not in (0, 1, 2):
        raise ValueError('Nur LED-Modi 0, 1 und 2 sind erlaubt.')
    reports = [bytes([3, 0xb0, 0x18, mode]).ljust(65, b'\0')]
    if persist:
        reports.append(bytes([3, 0xaa, 0xa1]).ljust(65, b'\0'))
    return reports

def send(mode=None, persist=True):
    with device_lock():
        return _send(mode, persist)

def _send(mode, persist=True):
    device = target()
    handle = kernel.CreateFileW(device['path'], 0x40000000, 3, None, 3, 0, None)
    if handle == c.c_void_p(-1).value:
        raise c.WinError(c.get_last_error())
    try:
        if mode is None:
            record(event='access_check', device=device, message='Zugriff möglich; nichts gesendet')
            return
        for packet in packets(mode, persist):
            record(event='write_attempt', mode=mode, persist=persist, report=packet.hex())
            data = c.create_string_buffer(packet, len(packet))
            count = w.DWORD()
            if not kernel.WriteFile(handle, data, len(packet), c.byref(count), None):
                raise c.WinError(c.get_last_error())
            if count.value != len(packet):
                raise RuntimeError('Unvollständige Übertragung: ' + str(count.value))
            record(event='write_completed', mode=mode, bytes=count.value)
            time.sleep(0.1)
        record(event='mode_sent', mode=mode, persist=persist,
               message='USB-Übertragung erfolgreich; aktuelle sichtbare Wirkung nicht automatisch geprüft')
    finally:
        kernel.CloseHandle(handle)

def window():
    import tkinter as tk
    from tkinter import ttk
    root = tk.Tk()
    root.title('CodexPad – LED testen')
    root.geometry('720x430')
    root.minsize(650, 400)
    panel = ttk.Frame(root, padding=22)
    panel.pack(fill='both', expand=True)
    ttk.Label(panel, text='Beleuchtung am Pad testen', font=('Segoe UI', 17, 'bold')).pack(anchor='w')
    ttk.Label(panel, text='Am Gerät beobachtet: 0 = aus; 1 = links rotes „Tuckern“;\n2 = von links nach rechts mit wechselnden Farben. Die Einstellung kann gespeichert bleiben.',
              font=('Segoe UI', 10), wraplength=660).pack(anchor='w', pady=(12, 16))
    buttons = ttk.Frame(panel)
    buttons.pack(anchor='w')
    status = tk.StringVar(value='Bereit. Noch kein LED-Befehl gesendet.')
    last_mode = [None]
    def apply(mode):
        status.set('Modus ' + str(mode) + ' wird übertragen …')
        root.update_idletasks()
        try:
            python = Path(sys.executable).with_name('python.exe')
            result = subprocess.run([str(python), str(Path(__file__).resolve()), '--mode', str(mode)],
                                    capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=5,
                                    creationflags=subprocess.CREATE_NO_WINDOW)
            if result.returncode:
                raise RuntimeError((result.stderr or result.stdout).strip())
            last_mode[0] = mode
            status.set('Modus ' + str(mode) + ' gesendet. Was ist am Pad zu sehen?')
        except Exception as error:
            record(event='error', message=str(error))
            status.set('Übertragung nicht bestätigt: ' + str(error)[-300:])
    for mode in range(3):
        ttk.Button(buttons, text=MODE_LABELS[mode], command=lambda m=mode: apply(m)).pack(side='left', padx=(0, 12), ipadx=12, ipady=8)
    ttk.Label(panel, textvariable=status, wraplength=650).pack(anchor='w', pady=18)
    ttk.Label(panel, text='Beobachtung zum zuletzt getesteten Modus (Farbe, Anzahl LEDs, Effekt):').pack(anchor='w')
    note = tk.Text(panel, height=3, font=('Segoe UI', 10))
    note.pack(fill='x', pady=8)
    def save():
        value = note.get('1.0', 'end').strip()
        if value:
            record(event='observation', mode=last_mode[0], text=value)
            status.set('Beobachtung gespeichert. Du kannst den nächsten Modus testen.')
            note.delete('1.0', 'end')
    ttk.Button(panel, text='Beobachtung speichern', command=save).pack(anchor='w')
    ttk.Label(panel, text='Nach den drei Versuchen: Fenster schließen und Codex „fertig“ schreiben.', wraplength=650).pack(anchor='w', pady=14)
    record(event='window_opened')
    root.mainloop()

if __name__ == '__main__':
    if sys.argv[1:] == ['--check']:
        for mode in range(3):
            assert all(len(packet) == 65 for packet in packets(mode))
        send()
        print('OK: Passendes Gerät gefunden, Schreibzugriff möglich; keine Daten gesendet.')
    elif len(sys.argv) == 3 and sys.argv[1] in ('--mode', '--mode-volatile'):
        mode = int(sys.argv[2])
        persist = sys.argv[1] == '--mode'
        packets(mode, persist)
        send(mode, persist)
    elif len(sys.argv) == 1:
        window()
    else:
        raise SystemExit('Aufruf: pad_led.py [--check | --mode 0|1|2 | --mode-volatile 0|1|2]')

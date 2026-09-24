"""Read-only status from one local Codex rollout. Not an official live API.

No message bodies are returned or copied. 'Completed' is not 'unread'.
Quiet/incomplete logs produce unknown status, never a guessed success.
"""
import json
import os
import sqlite3
import subprocess
import sys
import time
import uuid
from contextlib import closing
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SETTINGS = ROOT / 'status-config.json'
MAX_TAIL = 4 * 1024 * 1024
LED_MODES = {'running': 0, 'completed': 1, 'aborted': 0, 'stale': 0, 'unknown': 0}
LED_NAMES = {0: 'aus', 1: 'tastenabhängiger Farbeffekt mit anschließendem Abdunkeln', 2: 'Farblauf'}

def latest_thread_id():
    home = Path(os.environ.get('CODEX_HOME', str(Path.home() / '.codex')))
    databases = list(home.glob('state_*.sqlite'))
    if not databases:
        raise RuntimeError('Keine lokale Codex-Datenbank gefunden.')
    database = max(databases, key=lambda file: file.stat().st_mtime)
    with closing(sqlite3.connect(database.as_uri() + '?mode=ro', uri=True, timeout=0.5)) as connection:
        row = connection.execute("""select id from threads
            where archived=0 and thread_source='user' and originator='codex_work_desktop'
            order by recency_at_ms desc, created_at_ms desc, id desc limit 1""").fetchone()
    if row is None:
        raise RuntimeError('Keine lokale Codex/Work-Aufgabe gefunden.')
    return normalize_id(row[0])

def normalize_id(raw):
    return str(uuid.UUID(raw.strip().removeprefix('codex://threads/')))

def local_unread(state, thread_id):
    """Read only an unambiguous single-account, single-local-host state."""
    section = state.get('electron-thread-read-state-v1', {})
    if not isinstance(section, dict) or section.get('version') != 1:
        return None
    identities = section.get('unreadByIdentity')
    if not isinstance(identities, dict) or len(identities) != 1:
        return None
    hosts = next(iter(identities.values()))
    if not isinstance(hosts, dict):
        return None
    local = [value for key, value in hosts.items() if key.startswith('local:')]
    if len(local) != 1 or not isinstance(local[0], list):
        return None
    if not all(isinstance(value, str) for value in local[0]):
        return None
    return thread_id in local[0]

def summarize(lines, now=None):
    now = now or datetime.now(timezone.utc)
    status, event_time, latest, turn = 'unknown', None, None, None
    for line in lines:
        try:
            item = json.loads(line)
            stamp = datetime.fromisoformat(item['timestamp'].replace('Z', '+00:00'))
            if stamp.tzinfo is None:
                continue
            latest = max(latest, stamp) if latest else stamp
            if item.get('type') != 'event_msg':
                continue
            event = item.get('payload', {})
            kind = event.get('type')
            if kind == 'task_started':
                status, event_time, turn = 'running', stamp, event.get('turn_id')
            elif kind == 'task_complete':
                if turn and event.get('turn_id') != turn:
                    continue
                status, event_time = 'completed', stamp
            elif kind == 'turn_aborted':
                if turn and event.get('turn_id') and event.get('turn_id') != turn:
                    continue
                status, event_time = 'aborted', stamp
        except (ValueError, KeyError, TypeError, AttributeError):
            continue
    if status == 'running' and (latest is None or (now - latest).total_seconds() > 300):
        status = 'stale'
    return {'status': status, 'event_time': event_time.isoformat() if event_time else None,
            'latest_time': latest.isoformat() if latest else None,
            'source': 'local_rollout', 'live_verified': False, 'unread_known': False}

def read_status(thread_id):
    thread_id = normalize_id(thread_id)
    home = Path(os.environ.get('CODEX_HOME', str(Path.home() / '.codex')))
    databases = list(home.glob('state_*.sqlite'))
    if not databases:
        raise RuntimeError('Keine lokale Codex-Datenbank gefunden.')
    database = max(databases, key=lambda file: file.stat().st_mtime)
    with closing(sqlite3.connect(database.as_uri() + '?mode=ro', uri=True, timeout=0.5)) as connection:
        row = connection.execute('select rollout_path from threads where id=?', (thread_id,)).fetchone()
    if row is None:
        raise RuntimeError('Dieser Chat wurde in der lokalen Datenbank nicht gefunden.')
    path = Path(row[0])
    with path.open('rb') as file:
        size = os.fstat(file.fileno()).st_size
        offset = max(0, size - MAX_TAIL)
        file.seek(offset)
        if offset:
            file.readline()
        data = file.read(MAX_TAIL)
    # Do not accept an unterminated tail while the writer is appending.
    lines = data.split(b'\n')[:-1]
    result = summarize(lines)
    result['thread_id'] = thread_id
    try:
        state = json.loads((home / '.codex-global-state.json').read_text(encoding='utf-8-sig'))
        unread = local_unread(state, thread_id) if isinstance(state, dict) else None
    except (OSError, ValueError):
        unread = None
    result['unread'] = unread
    result['unread_known'] = unread is not None
    return result

def save_settings(thread_id, led_signal):
    data = {'thread_id': thread_id, 'led_signal': bool(led_signal)}
    temporary = SETTINGS.with_suffix('.tmp')
    temporary.write_text(json.dumps(data, indent=2), encoding='utf-8')
    temporary.replace(SETTINGS)

def send_led_mode(mode):
    """Apply one tested firmware mode without the separate flash-save report."""
    if type(mode) is not int or mode not in LED_NAMES:
        raise ValueError('Nur LED-Modi 0, 1 und 2 sind erlaubt.')
    python = Path(sys.executable).with_name('python.exe')
    if not python.exists():
        raise RuntimeError('Die lokale Python-Laufzeit wurde nicht gefunden.')
    result = subprocess.run(
        [str(python), str(ROOT / 'pad_led.py'), '--mode-volatile', str(mode)],
        capture_output=True, text=True, encoding='utf-8', errors='replace',
        timeout=4, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError((result.stderr or result.stdout).strip() or 'LED-Befehl fehlgeschlagen.')

def window():
    import tkinter as tk
    from tkinter import ttk
    root = tk.Tk()
    root.title('CodexPad – Chatstatus')
    root.geometry('700x430')
    root.minsize(680, 410)
    frame = ttk.Frame(root, padding=22)
    frame.pack(fill='both', expand=True)
    ttk.Label(frame, text='Status eines lokalen Chats', font=('Segoe UI', 17, 'bold')).pack(anchor='w')
    ttk.Label(frame, text='Chat-ID oder codex://threads/…-Link:').pack(anchor='w', pady=(14, 4))
    settings = {}
    try:
        settings = json.loads(SETTINGS.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        pass
    value = tk.StringVar()
    value.set(settings.get('thread_id', ''))
    entry = ttk.Entry(frame, textvariable=value, width=72)
    entry.pack(fill='x')
    label = tk.Label(frame, text='Noch kein Chat ausgewählt', font=('Segoe UI', 15, 'bold'), anchor='w', padx=12, pady=14, bg='#dadde1')
    label.pack(fill='x', pady=16)
    detail = tk.StringVar(value='')
    ttk.Label(frame, textvariable=detail, wraplength=620).pack(anchor='w')
    led_signal = tk.BooleanVar(value=bool(settings.get('led_signal', False)))
    led_status = tk.StringVar(value='Pad: LED-Steuerung aus.')
    ttk.Checkbutton(frame, text='Pad-LED an den Chatstatus koppeln', variable=led_signal,
                    command=lambda: led_changed()).pack(anchor='w', pady=(10, 2))
    ttk.Label(frame, text='Antwort beendet: Modus 1 (tastenabhängiger Farbeffekt) · läuft, abgebrochen oder unbekannt: aus. Für den neuesten Chat die LED-Option im Hauptprogramm verwenden.',
              wraplength=650).pack(anchor='w')
    ttk.Label(frame, textvariable=led_status, wraplength=650).pack(anchor='w', pady=(3, 0))
    selected = [None]
    last_led_mode = [None]
    led_retry_after = [0.0]
    led_controlled = [False]
    colours = {'running': ('Antwort läuft laut Protokoll', '#b7d5ff'),
               'completed': ('Antwort beendet', '#bde9c8'), 'aborted': ('Antwort abgebrochen', '#f2d09c'),
               'stale': ('Keine aktuelle Bestätigung', '#dadde1'), 'unknown': ('Status unbekannt', '#dadde1')}

    def apply_led(mode, force=False):
        if not force and mode == last_led_mode[0]:
            return True
        now = time.monotonic()
        if not force and now < led_retry_after[0]:
            return False
        try:
            send_led_mode(mode)
            last_led_mode[0] = mode
            led_controlled[0] = led_signal.get()
            led_retry_after[0] = 0.0
            led_status.set('Pad: ' + LED_NAMES[mode] + ' (ohne separaten Speicherbefehl).')
            return True
        except Exception as error:
            led_retry_after[0] = now + 30
            led_status.set('Pad-LED konnte nicht aktualisiert werden: ' + str(error)[-180:])
            return False

    def stop_led_control():
        ok = True
        if led_controlled[0]:
            ok = apply_led(0, force=True)
        led_controlled[0] = False
        if ok:
            led_status.set('Pad: LED-Steuerung aus.')

    def refresh(schedule=True):
        status_name = 'unknown'
        if selected[0]:
            try:
                result = read_status(selected[0])
                status_name = result['status']
                text, colour = colours[status_name]
                label.configure(text=text, bg=colour)
                detail.set('Letztes Statusereignis: ' + str(result['event_time'] or 'keines'))
            except Exception as error:
                label.configure(text='Status nicht verfügbar', bg='#dadde1')
                detail.set(str(error))
        if led_signal.get() and selected[0]:
            apply_led(LED_MODES.get(status_name, 2))
        if schedule:
            root.after(3000, refresh)

    def watch():
        try:
            selected[0] = normalize_id(value.get())
            save_settings(selected[0], led_signal.get())
            if led_signal.get():
                last_led_mode[0] = None
                refresh(schedule=False)
        except ValueError:
            selected[0] = None
            label.configure(text='Bitte einen gültigen lokalen Chat-Link eintragen', bg='#dadde1')
            if led_controlled[0]:
                stop_led_control()

    def led_changed():
        try:
            save_settings(selected[0] or settings.get('thread_id', ''), led_signal.get())
        except OSError as error:
            led_status.set('Einstellung konnte nicht gespeichert werden: ' + str(error))
            led_signal.set(False)
            return
        if not led_signal.get():
            stop_led_control()
        elif selected[0]:
            last_led_mode[0] = None
            refresh(schedule=False)
        else:
            led_status.set('Pad: erst einen Chat auswählen.')

    def close_window():
        if led_controlled[0]:
            apply_led(0, force=True)
        root.destroy()

    ttk.Button(frame, text='Diesen Chat beobachten', command=watch).pack(anchor='w', pady=12)
    ttk.Label(frame, text='Experimentell: liest lokale Protokolle. Erkennt weder „ungelesen“ noch Freigaben. Die LED-Kopplung nutzt nur die drei getesteten Effekte und aktualisiert sie bei Statuswechseln, ohne in den Gerätespeicher zu schreiben.', wraplength=650).pack(anchor='w')
    root.protocol('WM_DELETE_WINDOW', close_window)
    if value.get():
        watch()
    refresh()
    root.mainloop()

if __name__ == '__main__':
    if sys.argv[1:] == ['--latest']:
        print(json.dumps(read_status(latest_thread_id())))
    elif len(sys.argv) == 3 and sys.argv[1] == '--read':
        print(json.dumps(read_status(sys.argv[2])))
    elif len(sys.argv) == 1:
        window()
    else:
        raise SystemExit('Aufruf: pad_status.py [--read CHAT-ID]')

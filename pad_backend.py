"""Local, read-only task inventory. JSON pipe; never returns message bodies.

This adapter deliberately does not infer approval requests from inactivity.
Only the desktop's local user tasks and its unambiguous read state are supported.
"""
import json
import os
import sqlite3
import sys
from contextlib import closing
from datetime import datetime, timezone
from pathlib import Path

from pad_status import MAX_TAIL, local_unread, normalize_id, summarize


class TaskReader:
    def __init__(self, home=None):
        self.home = Path(home or os.environ.get('CODEX_HOME', str(Path.home() / '.codex')))
        self.cache = {}

    def status(self, path):
        try:
            path = Path(path)
            stat = path.stat()
            signature = (stat.st_mtime_ns, stat.st_size)
            cached = self.cache.get(str(path))
            if not cached or cached[0] != signature:
                with path.open('rb') as stream:
                    offset = max(0, stat.st_size - MAX_TAIL)
                    stream.seek(offset)
                    if offset:
                        stream.readline()
                    lines = stream.read(MAX_TAIL).split(b'\n')[:-1]
                result = summarize(lines)
                self.cache[str(path)] = (signature, result)
            result = dict(self.cache[str(path)][1])
            if result['status'] == 'running':
                last = result.get('latest_time') or result.get('event_time')
                if not last or (datetime.now(timezone.utc) - datetime.fromisoformat(last)).total_seconds() > 300:
                    result['status'] = 'stale'
            return result
        except (OSError, ValueError, TypeError):
            return {'status': 'unknown', 'event_time': None}

    def snapshot(self):
        databases = list(self.home.glob('state_*.sqlite'))
        if not databases:
            raise RuntimeError('Keine lokale Codex-Datenbank gefunden.')
        database = max(databases, key=lambda file: file.stat().st_mtime)
        with closing(sqlite3.connect(database.as_uri() + '?mode=ro', uri=True, timeout=0.5)) as db:
            rows = db.execute("""select id, title, rollout_path, recency_at_ms from threads
                where archived=0 and thread_source='user' and originator='codex_work_desktop'
                order by recency_at_ms desc, created_at_ms desc, id desc""").fetchall()
        try:
            state = json.loads((self.home / '.codex-global-state.json').read_text(encoding='utf-8-sig'))
            known = isinstance(state, dict) and local_unread(state, '') is not None
        except (OSError, ValueError):
            state, known = {}, False
        tasks = []
        for ident, title, path, recency in rows:
            try:
                ident = normalize_id(ident)
            except (ValueError, AttributeError):
                continue
            status = self.status(path)
            tasks.append({'id': ident, 'title': title or 'Unbenannte Aufgabe',
                          'status': status['status'], 'recency': recency or 0,
                          'unread': local_unread(state, ident) if known else False})
        live_paths = {str(path) for _, _, path, _ in rows}
        self.cache = {key: value for key, value in self.cache.items() if key in live_paths}
        # An unread task without a conclusive event must not be silently counted as read.
        led_known = known and all(t['status'] != 'unknown' for t in tasks if t['unread'])
        return {'tasks': tasks, 'unread_known': known, 'led_known': led_known,
                'attention_supported': False}


def device_state():
    from pad_hid import inventory
    devices = [d for d in inventory() if d.get('usage_page') == 0xff00
               and d.get('usage') == 1 and d.get('output_bytes') == 65
               and d.get('output_report_ids') == [3] and '&mi_01#' in d['path'].lower()]
    return {'connected': len(devices) == 1, 'device_count': len(devices),
            'device_key': devices[0]['path'] if len(devices) == 1 else ''}


def response(reader):
    result = {'tasks': [], 'unread_known': False, 'led_known': False}
    try:
        result.update(reader.snapshot())
    except (OSError, ValueError, sqlite3.Error, RuntimeError):
        result['error'] = 'Lokale Aufgabenstatus-Daten nicht verfügbar oder inkompatibel.'
    try:
        result.update(device_state())
    except Exception:
        result.update(connected=False, device_key='', device_error='Pad-Verbindung nicht prüfbar.')
    return result


if __name__ == '__main__':
    reader = TaskReader()
    if sys.argv[1:] == ['--serve']:
        for line in sys.stdin:
            try:
                request = json.loads(line)
                result = response(reader) if request.get('command') == 'snapshot' else {'error': 'Unbekannter Befehl.'}
            except (ValueError, AttributeError):
                result = {'error': 'Ungültige Anfrage.'}
            print(json.dumps(result, ensure_ascii=True), flush=True)
    elif sys.argv[1:] == ['--snapshot']:
        print(json.dumps(response(reader)))
    else:
        raise SystemExit('Aufruf: pad_backend.py --serve | --snapshot')

"""Restricted six-input setup for the inspected SinLoon 1189:8890.

Wire format: x0f5c3/ch57x-keyboard-tool src/keyboard/k8890.rs.
Device checks and exact reports are kept in an append-only local audit log.
Successful writes do NOT prove the physical controls work; PadTest verifies that.
"""
import sys
import json
import time
import uuid
from datetime import datetime
from pathlib import Path
from pad_led import target, kernel, c, w
from pad_hid import device_lock

ROOT = Path(__file__).resolve().parent
STATE = ROOT / 'Pad-Geraetestatus.json'
LAYOUT = [('Taste links', 1, 'F13', 0x68), ('Taste Mitte', 2, 'F14', 0x69),
          ('Taste rechts', 3, 'F15', 0x6a), ('Regler links', 13, 'F16', 0x6b),
          ('Regler rechts', 15, 'F17', 0x6c), ('Regler Druck', 14, 'F18', 0x6d)]

def reports(restore=False):
    result = []
    for name, slot, key, usage in LAYOUT:
        code = 0x06 if restore else usage  # USB HID C, observed on all six controls.
        for header in ([3, 0xfe, 1, 1, 1], [3, slot, 0x11, 1, 0, 0, 0],
                       [3, slot, 0x11, 1, 1, 0, code], [3, 0xaa, 0xaa]):
            result.append(bytes(header).ljust(65, b'\0'))
    return result

def audit(**event):
    with (ROOT / 'Pad-Einrichtung.jsonl').open('a', encoding='utf-8') as file:
        file.write(json.dumps({'time': datetime.now().isoformat(), **event}, ensure_ascii=False) + '\n')

def save_state(state):
    temporary = STATE.with_suffix('.tmp')
    temporary.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(STATE)

def configure(restore=False):
    with device_lock():
        return _configure(restore)

def _configure(restore):
    device = target()
    state = {'setup_id': str(uuid.uuid4()), 'time': datetime.now().isoformat(),
             'status': 'programming', 'expected': ['C'] * 6 if restore else [v[2] for v in LAYOUT]}
    audit(event='setup_start', device=device, restore_observed_c=restore,
          baseline='Observed factory mapping: all six controls sent C; not a firmware backup')
    handle = kernel.CreateFileW(device['path'], 0x40000000, 3, None, 3, 0, None)
    if handle == c.c_void_p(-1).value:
        raise c.WinError(c.get_last_error())
    completed = 0
    try:
        save_state(state)
        for report in reports(restore):
            audit(event='write_attempt', index=completed, report=report.hex())
            buffer = c.create_string_buffer(report, len(report))
            count = w.DWORD()
            if not kernel.WriteFile(handle, buffer, len(report), c.byref(count), None):
                raise c.WinError(c.get_last_error())
            if count.value != len(report):
                raise RuntimeError('Unvollständige Übertragung.')
            completed += 1
            audit(event='write_completed', index=completed, bytes=count.value)
            time.sleep(0.08)
        audit(event='setup_sent', reports=completed, physical_verification=False)
        state.update(status='awaiting_physical_test', reports=completed)
        save_state(state)
        print('Übertragung abgeschlossen. Jetzt Eingaben testen: ' + ('alle C' if restore else 'F13 bis F18') + ' erwartet.')
    except Exception as error:
        state.update(status='incomplete', completed_reports=completed, error=str(error))
        save_state(state)
        audit(event='setup_failed', completed_reports=completed, error=str(error))
        raise RuntimeError('Einrichtung unvollständig nach ' + str(completed) + ' Berichten: ' + str(error)) from error
    finally:
        kernel.CloseHandle(handle)

if __name__ == '__main__':
    args = sys.argv[1:]
    if args == ['--inspect']:
        device = target()
        status = 'Eingabetest noch offen.'
        try:
            state = json.loads(STATE.read_text(encoding='utf-8-sig'))
            proof = json.loads((ROOT / 'Pad-Pruefergebnis.json').read_text(encoding='utf-8-sig'))
            if (state['status'] == 'awaiting_physical_test' and proof.get('setup_id') == state['setup_id']
                    and proof.get('matched') == 6 and proof.get('expected') == state['expected']):
                status = 'Alle sechs Eingaben für diese Belegung physisch geprüft.'
        except (OSError, ValueError, KeyError):
            pass
        print('SinLoon erkannt (1189:8890). ' + status)
    elif args == ['--plan']:
        print(json.dumps({'controls': LAYOUT, 'reports': [p.hex() for p in reports()], 'writes': False}, indent=2))
    elif args == ['--configure']:
        configure()
    elif args == ['--restore-observed-c']:
        configure(True)
    else:
        raise SystemExit('Aufruf: --inspect, --plan, --configure oder --restore-observed-c')

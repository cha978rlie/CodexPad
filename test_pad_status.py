import json
import unittest
import tempfile
import sqlite3
from pathlib import Path
from datetime import datetime, timezone
from unittest.mock import patch
from pad_status import summarize, normalize_id, LED_MODES, send_led_mode, latest_thread_id, local_unread

def event(kind, time='2026-09-23T19:00:00Z', turn='a'):
    return json.dumps({'timestamp': time, 'type': 'event_msg', 'payload': {'type': kind, 'turn_id': turn}})

class StatusTests(unittest.TestCase):
    def test_unread_tracks_exact_thread_and_read_transition(self):
        state = {'electron-thread-read-state-v1': {'version': 1, 'unreadByIdentity': {'account': {'local:host': ['a', 'b']}}}}
        self.assertTrue(local_unread(state, 'a'))
        self.assertFalse(local_unread(state, 'c'))
        state['electron-thread-read-state-v1']['unreadByIdentity']['account']['local:host'].remove('a')
        self.assertFalse(local_unread(state, 'a'))
        self.assertTrue(local_unread(state, 'b'))

    def test_ambiguous_unread_state_is_unknown_not_read(self):
        self.assertIsNone(local_unread({}, 'a'))
        state = {'electron-thread-read-state-v1': {'version': 1, 'unreadByIdentity': {'a': {'local:x': []}, 'b': {'local:y': ['a']}}}}
        self.assertIsNone(local_unread(state, 'a'))

    now = datetime(2026, 9, 23, 19, 1, tzinfo=timezone.utc)

    def test_started_then_completed(self):
        self.assertEqual(summarize([event('task_started')], self.now)['status'], 'running')
        result = summarize([event('task_started'), event('task_complete')], self.now)
        self.assertEqual(result['status'], 'completed')
        self.assertFalse(result['unread_known'])

    def test_old_start_is_not_claimed_live(self):
        self.assertEqual(summarize([event('task_started', '2026-09-23T18:00:00Z')], self.now)['status'], 'stale')

    def test_old_completion_cannot_finish_new_turn(self):
        self.assertEqual(summarize([event('task_started', turn='b'), event('task_complete', turn='a')], self.now)['status'], 'running')

    def test_partial_or_unknown_data_cannot_mean_success(self):
        self.assertEqual(summarize(['{', event('tool_complete')], self.now)['status'], 'unknown')
        self.assertEqual(summarize([event('turn_aborted')], self.now)['status'], 'aborted')

    def test_link_is_id_only(self):
        self.assertEqual(normalize_id('codex://threads/11111111-2222-4333-8444-555555555555'), '11111111-2222-4333-8444-555555555555')
        with self.assertRaises(ValueError): normalize_id('../state_5.sqlite')

    def test_pad_status_uses_only_the_three_observed_effects(self):
        self.assertEqual(LED_MODES, {'running': 0, 'completed': 1, 'aborted': 0, 'stale': 0, 'unknown': 0})

    def test_latest_uses_user_recency_and_excludes_archives_and_agents(self):
        with tempfile.TemporaryDirectory() as folder:
            with sqlite3.connect(Path(folder) / 'state_5.sqlite') as db:
                db.execute('create table threads(id, archived, thread_source, originator, recency_at_ms, created_at_ms)')
                ids = ['00000000-0000-0000-0000-00000000000' + str(i) for i in range(4)]
                db.executemany('insert into threads values(?,?,?,?,?,?)', [
                    (ids[0], 0, 'user', 'codex_work_desktop', 10, 1),
                    (ids[1], 0, 'user', 'codex_work_desktop', 20, 1),
                    (ids[2], 1, 'user', 'codex_work_desktop', 30, 1),
                    (ids[3], 0, 'agent', 'codex_work_desktop', 40, 1)])
            db.close()
            with patch.dict('os.environ', {'CODEX_HOME': folder}):
                self.assertEqual(latest_thread_id(), ids[1])

    def test_chat_status_calls_only_the_non_persistent_led_command(self):
        with patch('pad_status.subprocess.run') as run:
            run.return_value.returncode = 0
            send_led_mode(1)
        args = run.call_args.args[0]
        self.assertEqual(args[-2:], ['--mode-volatile', '1'])
        self.assertTrue(run.call_args.kwargs['capture_output'])
        self.assertEqual(run.call_args.kwargs['timeout'], 4)
        with self.assertRaises(ValueError): send_led_mode(9)

if __name__ == '__main__':
    unittest.main()

import json
import sqlite3
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

from pad_backend import TaskReader


class BackendTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.home = Path(self.temp.name)
        self.ids = [f'00000000-0000-0000-0000-{n:012d}' for n in range(1, 6)]
        with sqlite3.connect(self.home / 'state_5.sqlite') as db:
            db.execute('create table threads(id, title, rollout_path, recency_at_ms, created_at_ms, archived, thread_source, originator)')
            for index, ident in enumerate(self.ids):
                path = self.home / (ident + '.jsonl')
                path.write_text(self.event('task_started') + self.event('task_complete'), encoding='utf-8')
                db.execute('insert into threads values(?,?,?,?,?,?,?,?)',
                           (ident, 'Test', str(path), index, index, int(index == 3),
                            'agent' if index == 4 else 'user', 'codex_work_desktop'))
        db.close()
        self.read_state(self.ids[:2])
        self.reader = TaskReader(self.home)

    def tearDown(self):
        self.temp.cleanup()

    def event(self, kind):
        return json.dumps({'timestamp': datetime.now(timezone.utc).isoformat(),
                           'type': 'event_msg', 'payload': {'type': kind, 'turn_id': 'a'}}) + '\n'

    def read_state(self, unread):
        (self.home / '.codex-global-state.json').write_text(json.dumps({
            'electron-thread-read-state-v1': {'version': 1, 'unreadByIdentity': {'account': {'local:pc': unread}}}}))

    def test_two_unread_results_and_mouse_read_transitions(self):
        result = self.reader.snapshot()
        self.assertTrue(result['led_known'])
        self.assertEqual(sum(t['unread'] and t['status'] == 'completed' for t in result['tasks']), 2)
        self.read_state(self.ids[1:2])
        self.assertEqual(sum(t['unread'] for t in self.reader.snapshot()['tasks']), 1)
        self.read_state([])
        self.assertFalse(any(t['unread'] for t in self.reader.snapshot()['tasks']))

    def test_archive_and_agent_excluded_recency_preserved(self):
        result = self.reader.snapshot()
        self.assertEqual([t['id'] for t in result['tasks']], list(reversed(self.ids[:3])))

    def test_partial_global_file_is_unknown_and_recovers(self):
        (self.home / '.codex-global-state.json').write_text('{')
        self.assertFalse(self.reader.snapshot()['led_known'])
        self.read_state(self.ids[:1])
        self.assertTrue(self.reader.snapshot()['led_known'])

    def test_missing_unread_rollout_does_not_claim_all_read(self):
        (self.home / (self.ids[0] + '.jsonl')).unlink()
        result = self.reader.snapshot()
        self.assertFalse(result['led_known'])
        self.assertTrue(result['unread_known'])

    def test_cache_invalidates_new_turn_and_ignores_partial_completion(self):
        self.reader.snapshot()
        path = self.home / (self.ids[0] + '.jsonl')
        with path.open('a') as stream:
            stream.write(self.event('task_started'))
            stream.write(self.event('task_complete').rstrip('\n'))
        task = next(t for t in self.reader.snapshot()['tasks'] if t['id'] == self.ids[0])
        self.assertEqual(task['status'], 'running')
        with path.open('a') as stream:
            stream.write('\n')
        task = next(t for t in self.reader.snapshot()['tasks'] if t['id'] == self.ids[0])
        self.assertEqual(task['status'], 'completed')

    def test_no_message_bodies_in_response(self):
        path = self.home / (self.ids[0] + '.jsonl')
        with path.open('a') as stream:
            stream.write(json.dumps({'timestamp': datetime.now(timezone.utc).isoformat(),
                                     'type': 'response_item', 'payload': {'text': 'PRIVATE-MESSAGE-CONTENT'}}) + '\n')
        result = json.dumps(self.reader.snapshot())
        self.assertNotIn('PRIVATE-MESSAGE-CONTENT', result)


if __name__ == '__main__':
    unittest.main()

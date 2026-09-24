import unittest
import tempfile
import json
from pathlib import Path
from unittest.mock import patch
import pad_device
import pad_led

class ProtocolTests(unittest.TestCase):
    def test_six_controls_have_distinct_hid_function_keys(self):
        messages = pad_device.reports()
        self.assertEqual(len(messages), 24)
        self.assertTrue(all(len(m) == 65 and m[0] == 3 for m in messages))
        # Slot order differs from the UI: the right turn is slot 15, press is 14.
        expected = [(1, 0x68), (2, 0x69), (3, 0x6a), (13, 0x6b), (15, 0x6c), (14, 0x6d)]
        for index, (slot, usage) in enumerate(expected):
            block = messages[index*4:index*4+4]
            self.assertEqual(block[0][:5], bytes.fromhex('03 fe 01 01 01'))
            self.assertEqual(block[1][:7], bytes([3, slot, 0x11, 1, 0, 0, 0]))
            self.assertEqual(block[2][:7], bytes([3, slot, 0x11, 1, 1, 0, usage]))
            self.assertEqual(block[3][:3], bytes.fromhex('03 aa aa'))
            self.assertTrue(all(not any(m[7:]) for m in block))

    def test_restore_only_the_observed_c_mapping(self):
        self.assertEqual([p[6] for p in pad_device.reports(True)[2::4]], [6]*6)

    def test_led_cannot_send_arbitrary_modes(self):
        for mode in (-1, 3, 255, True, '1'):
            with self.assertRaises(ValueError): pad_led.packets(mode)
        for mode in range(3):
            messages = pad_led.packets(mode)
            self.assertEqual(messages[0][:4], bytes([3, 0xb0, 0x18, mode]))
            self.assertEqual(messages[1][:3], bytes.fromhex('03 aa a1'))
            self.assertTrue(all(len(m) == 65 for m in messages))

    def test_volatile_led_mode_does_not_write_flash(self):
        for mode in range(3):
            messages = pad_led.packets(mode, persist=False)
            self.assertEqual(len(messages), 1)
            self.assertEqual(messages[0][:4], bytes([3, 0xb0, 0x18, mode]))
            self.assertEqual(len(messages[0]), 65)

    def test_wrong_or_ambiguous_device_is_rejected(self):
        for devices in ([], [{'usage_page': 1}], [{'usage_page': 0xff00, 'usage': 1, 'input_bytes': 65, 'output_bytes': 9}]):
            with patch('pad_led.inventory', return_value=devices):
                with self.assertRaises(RuntimeError): pad_led.target()
        match = dict(usage_page=0xff00, usage=1, input_bytes=65, output_bytes=65,
                     output_report_ids=[3], path='HID#VID_1189&PID_8890&MI_01#test')
        with patch('pad_led.inventory', return_value=[match, match]):
            with self.assertRaises(RuntimeError): pad_led.target()
        with patch('pad_led.inventory', return_value=[match]):
            self.assertEqual(pad_led.target(), match)

    def test_partial_write_is_not_marked_successful(self):
        calls = []
        def write(handle, buffer, length, count, overlapped):
            calls.append(length)
            if len(calls) == 2:
                return False
            pad_device.c.cast(count, pad_device.c.POINTER(pad_device.w.DWORD))[0] = length
            return True
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with patch('pad_device.ROOT', root), patch('pad_device.STATE', root / 'state.json'), \
                 patch('pad_device.target', return_value={'path': 'test'}), \
                 patch.object(pad_device.kernel, 'CreateFileW', return_value=123), \
                 patch.object(pad_device.kernel, 'CloseHandle'), \
                 patch.object(pad_device.kernel, 'WriteFile', side_effect=write):
                with self.assertRaises(RuntimeError):
                    pad_device._configure(False)
            state = json.loads((root / 'state.json').read_text())
            self.assertEqual(state['status'], 'incomplete')
            self.assertEqual(state['completed_reports'], 1)
            self.assertEqual(len(calls), 2)
            events = [json.loads(line)['event'] for line in (root / 'Pad-Einrichtung.jsonl').read_text().splitlines()]
            self.assertNotIn('setup_sent', events)

if __name__ == '__main__':
    unittest.main()

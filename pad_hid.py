"""Read-only Windows HID inventory for the attached 1189:8890 pad."""
import ctypes as c
from ctypes import wintypes as w
import json
from contextlib import contextmanager

user = c.WinDLL('user32', use_last_error=True)
kernel = c.WinDLL('kernel32', use_last_error=True)
hid = c.WinDLL('hid', use_last_error=True)

class Device(c.Structure):
    _fields_ = [('handle', w.HANDLE), ('kind', w.DWORD)]

user.GetRawInputDeviceList.argtypes = [c.POINTER(Device), c.POINTER(w.UINT), w.UINT]
user.GetRawInputDeviceList.restype = w.UINT
user.GetRawInputDeviceInfoW.argtypes = [w.HANDLE, w.UINT, c.c_void_p, c.POINTER(w.UINT)]
user.GetRawInputDeviceInfoW.restype = w.UINT
kernel.CreateFileW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, c.c_void_p, w.DWORD, w.DWORD, w.HANDLE]
kernel.CreateFileW.restype = w.HANDLE
kernel.CloseHandle.argtypes = [w.HANDLE]
kernel.CreateMutexW.argtypes = [c.c_void_p, w.BOOL, w.LPCWSTR]
kernel.CreateMutexW.restype = w.HANDLE
kernel.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
kernel.WaitForSingleObject.restype = w.DWORD
kernel.ReleaseMutex.argtypes = [w.HANDLE]
hid.HidD_GetPreparsedData.argtypes = [w.HANDLE, c.POINTER(c.c_void_p)]
hid.HidD_GetPreparsedData.restype = c.c_ubyte
hid.HidD_FreePreparsedData.argtypes = [c.c_void_p]
hid.HidP_GetCaps.argtypes = [c.c_void_p, c.c_void_p]
hid.HidP_GetCaps.restype = c.c_long
hid.HidP_GetValueCaps.argtypes = [c.c_int, c.c_void_p, c.POINTER(c.c_ushort), c.c_void_p]
hid.HidP_GetValueCaps.restype = c.c_long
for func in ('HidD_GetManufacturerString', 'HidD_GetProductString'):
    getattr(hid, func).argtypes = [w.HANDLE, c.c_void_p, w.ULONG]
    getattr(hid, func).restype = c.c_ubyte

@contextmanager
def device_lock():
    handle = kernel.CreateMutexW(None, False, 'Local\\CodexPad-Device-1189-8890')
    if not handle:
        raise c.WinError(c.get_last_error())
    acquired = False
    try:
        acquired = kernel.WaitForSingleObject(handle, 0) in (0, 0x80)
        if not acquired:
            raise RuntimeError('Ein anderer Gerätezugriff läuft. Bitte kurz warten.')
        yield
    finally:
        if acquired:
            kernel.ReleaseMutex(handle)
        kernel.CloseHandle(handle)

def inventory():
    count = w.UINT()
    if user.GetRawInputDeviceList(None, c.byref(count), c.sizeof(Device)) == 0xffffffff:
        raise c.WinError(c.get_last_error())
    devices = (Device * count.value)()
    actual = user.GetRawInputDeviceList(devices, c.byref(count), c.sizeof(Device))
    if actual == 0xffffffff:
        raise c.WinError(c.get_last_error())
    result = []
    for dev in devices[:actual]:
        size = w.UINT()
        user.GetRawInputDeviceInfoW(dev.handle, 0x20000007, None, c.byref(size))
        name = c.create_unicode_buffer(size.value + 1)
        user.GetRawInputDeviceInfoW(dev.handle, 0x20000007, name, c.byref(size))
        if 'vid_1189&pid_8890' not in name.value.lower():
            continue
        record = {'path': name.value, 'kind': dev.kind}
        handle = kernel.CreateFileW(name.value, 0, 3, None, 3, 0, None)
        if handle == c.c_void_p(-1).value:
            record['error'] = str(c.WinError(c.get_last_error()))
        else:
            try:
                for field, func in [('manufacturer', hid.HidD_GetManufacturerString), ('product', hid.HidD_GetProductString)]:
                    text = c.create_unicode_buffer(256)
                    if func(handle, text, c.sizeof(text)):
                        record[field] = text.value
                preparsed = c.c_void_p()
                if hid.HidD_GetPreparsedData(handle, c.byref(preparsed)):
                    try:
                        caps = (c.c_ushort * 32)()
                        if hid.HidP_GetCaps(preparsed, caps) == 0x110000:
                            record.update(dict(zip(['usage', 'usage_page', 'input_bytes', 'output_bytes', 'feature_bytes'], list(caps)[:5])))
                            count_values = c.c_ushort(caps[27])
                            if count_values.value:
                                values = c.create_string_buffer(72 * count_values.value)
                                if hid.HidP_GetValueCaps(1, values, c.byref(count_values), preparsed) == 0x110000:
                                    record['output_report_ids'] = [values.raw[i * 72 + 2] for i in range(count_values.value)]
                    finally:
                        hid.HidD_FreePreparsedData(preparsed)
            finally:
                kernel.CloseHandle(handle)
        result.append(record)
    return result

if __name__ == '__main__':
    print(json.dumps(inventory(), indent=2))

from __future__ import annotations

import ctypes
from ctypes import wintypes
from dataclasses import replace
from typing import Callable

from .devices import HidDeviceInfo, parse_vid_pid, require_windows


LRESULT = getattr(wintypes, "LRESULT", wintypes.LPARAM)
HRAWINPUT = getattr(wintypes, "HRAWINPUT", wintypes.HANDLE)
HCURSOR = getattr(wintypes, "HCURSOR", wintypes.HANDLE)

RIM_TYPEMOUSE = 0
RIM_TYPEKEYBOARD = 1
RIM_TYPEHID = 2

RID_INPUT = 0x10000003
RIDI_DEVICENAME = 0x20000007
RIDI_DEVICEINFO = 0x2000000B

RIDEV_INPUTSINK = 0x00000100
RIDEV_DEVNOTIFY = 0x00002000

WM_INPUT = 0x00FF
WM_INPUT_DEVICE_CHANGE = 0x00FE
WM_DESTROY = 0x0002

PM_REMOVE = 0x0001

USAGE_PAGE_GENERIC_DESKTOP = 0x01
USAGE_MOUSE = 0x02
USAGE_PAGE_DIGITIZER = 0x0D
USAGE_TOUCH_PAD = 0x05
USAGE_PAGE_APPLE_VENDOR = 0xFF00
USAGE_APPLE_MAGIC = 0x0B
USAGE_APPLE_MAGIC_USBC = 0x14


RawReportCallback = Callable[[HidDeviceInfo, bytes], None]
DeviceChangeCallback = Callable[[list[HidDeviceInfo]], None]


class RawInputBridge:
    def __init__(
        self,
        on_report: RawReportCallback,
        on_devices_changed: DeviceChangeCallback | None = None,
    ) -> None:
        require_windows()
        self.on_report = on_report
        self.on_devices_changed = on_devices_changed
        self.user32 = ctypes.WinDLL("user32", use_last_error=True)
        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self.hwnd: int | None = None
        self.devices_by_handle: dict[int, HidDeviceInfo] = {}
        self._wndproc_ref = WNDPROC(self._wndproc)
        self._configure_functions()

    def run(self) -> None:
        self.hwnd = self._create_window()
        self._refresh_devices()
        self._register_raw_input()

        message = MSG()
        while self.user32.GetMessageW(ctypes.byref(message), None, 0, 0) != 0:
            self.user32.TranslateMessage(ctypes.byref(message))
            self.user32.DispatchMessageW(ctypes.byref(message))

    def stop(self) -> None:
        if self.hwnd:
            self.user32.PostMessageW(self.hwnd, WM_DESTROY, 0, 0)

    def enumerate_devices(self) -> list[HidDeviceInfo]:
        return enumerate_raw_input_devices()

    def _configure_functions(self) -> None:
        self.user32.RegisterClassExW.argtypes = [ctypes.POINTER(WNDCLASSEXW)]
        self.user32.RegisterClassExW.restype = wintypes.ATOM

        self.user32.CreateWindowExW.argtypes = [
            wintypes.DWORD,
            wintypes.LPCWSTR,
            wintypes.LPCWSTR,
            wintypes.DWORD,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            wintypes.HWND,
            wintypes.HMENU,
            wintypes.HINSTANCE,
            wintypes.LPVOID,
        ]
        self.user32.CreateWindowExW.restype = wintypes.HWND

        self.user32.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
        self.user32.DefWindowProcW.restype = LRESULT

        self.user32.RegisterRawInputDevices.argtypes = [
            ctypes.POINTER(RAWINPUTDEVICE),
            wintypes.UINT,
            wintypes.UINT,
        ]
        self.user32.RegisterRawInputDevices.restype = wintypes.BOOL

        self.user32.GetRawInputData.argtypes = [
            HRAWINPUT,
            wintypes.UINT,
            wintypes.LPVOID,
            ctypes.POINTER(wintypes.UINT),
            wintypes.UINT,
        ]
        self.user32.GetRawInputData.restype = wintypes.UINT

    def _create_window(self) -> int:
        hinstance = self.kernel32.GetModuleHandleW(None)
        class_name = "MagicTrackpadBridgeRawInput"
        wndclass = WNDCLASSEXW()
        wndclass.cbSize = ctypes.sizeof(WNDCLASSEXW)
        wndclass.lpfnWndProc = self._wndproc_ref
        wndclass.hInstance = hinstance
        wndclass.lpszClassName = class_name

        atom = self.user32.RegisterClassExW(ctypes.byref(wndclass))
        if not atom and ctypes.get_last_error() != 1410:
            raise ctypes.WinError(ctypes.get_last_error())

        hwnd = self.user32.CreateWindowExW(
            0,
            class_name,
            "Magic Trackpad Bridge",
            0,
            0,
            0,
            0,
            0,
            None,
            None,
            hinstance,
            None,
        )
        if not hwnd:
            raise ctypes.WinError(ctypes.get_last_error())
        return hwnd

    def _register_raw_input(self) -> None:
        usages = [
            (USAGE_PAGE_GENERIC_DESKTOP, USAGE_MOUSE),
            (USAGE_PAGE_DIGITIZER, USAGE_TOUCH_PAD),
            (USAGE_PAGE_APPLE_VENDOR, USAGE_APPLE_MAGIC),
            (USAGE_PAGE_APPLE_VENDOR, USAGE_APPLE_MAGIC_USBC),
        ]
        records = (RAWINPUTDEVICE * len(usages))()
        for index, (usage_page, usage) in enumerate(usages):
            records[index].usUsagePage = usage_page
            records[index].usUsage = usage
            records[index].dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY
            records[index].hwndTarget = self.hwnd

        if not self.user32.RegisterRawInputDevices(records, len(usages), ctypes.sizeof(RAWINPUTDEVICE)):
            raise ctypes.WinError(ctypes.get_last_error())

    def _refresh_devices(self) -> None:
        devices = self.enumerate_devices()
        self.devices_by_handle = {device.handle: device for device in devices}
        if self.on_devices_changed:
            self.on_devices_changed(devices)

    def _read_raw_input(self, lparam: int) -> tuple[int, bytes] | None:
        size = wintypes.UINT(0)
        result = self.user32.GetRawInputData(
            lparam,
            RID_INPUT,
            None,
            ctypes.byref(size),
            ctypes.sizeof(RAWINPUTHEADER),
        )
        if result == 0xFFFFFFFF:
            return None

        buffer = ctypes.create_string_buffer(size.value)
        result = self.user32.GetRawInputData(
            lparam,
            RID_INPUT,
            buffer,
            ctypes.byref(size),
            ctypes.sizeof(RAWINPUTHEADER),
        )
        if result == 0xFFFFFFFF:
            return None

        raw = buffer.raw[: size.value]
        header = RAWINPUTHEADER.from_buffer_copy(raw)
        if header.dwType != RIM_TYPEHID:
            return None

        offset = ctypes.sizeof(RAWINPUTHEADER)
        hid_size = int.from_bytes(raw[offset : offset + 4], "little")
        hid_count = int.from_bytes(raw[offset + 4 : offset + 8], "little")
        payload = raw[offset + 8 : offset + 8 + hid_size * hid_count]
        return int(header.hDevice), payload

    def _wndproc(self, hwnd: int, msg: int, wparam: int, lparam: int) -> int:
        if msg == WM_INPUT:
            read = self._read_raw_input(lparam)
            if read:
                handle, payload = read
                device = self.devices_by_handle.get(handle)
                if device:
                    self._dispatch_reports(device, payload)
            return 0

        if msg == WM_INPUT_DEVICE_CHANGE:
            self._refresh_devices()
            return 0

        if msg == WM_DESTROY:
            self.user32.PostQuitMessage(0)
            return 0

        return self.user32.DefWindowProcW(hwnd, msg, wparam, lparam)

    def _dispatch_reports(self, device: HidDeviceInfo, payload: bytes) -> None:
        if not device.is_apple_magic_trackpad:
            return
        size = _guess_hid_report_size(payload)
        if size is None:
            self.on_report(device, payload)
            return
        for offset in range(0, len(payload), size):
            report = payload[offset : offset + size]
            if report:
                self.on_report(device, report)


def enumerate_raw_input_devices() -> list[HidDeviceInfo]:
    require_windows()
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    GetRawInputDeviceList = user32.GetRawInputDeviceList
    GetRawInputDeviceList.argtypes = [
        ctypes.POINTER(RAWINPUTDEVICELIST),
        ctypes.POINTER(wintypes.UINT),
        wintypes.UINT,
    ]
    GetRawInputDeviceList.restype = wintypes.UINT

    device_count = wintypes.UINT(0)
    result = GetRawInputDeviceList(None, ctypes.byref(device_count), ctypes.sizeof(RAWINPUTDEVICELIST))
    if result == 0xFFFFFFFF:
        raise ctypes.WinError(ctypes.get_last_error())

    records = (RAWINPUTDEVICELIST * device_count.value)()
    result = GetRawInputDeviceList(records, ctypes.byref(device_count), ctypes.sizeof(RAWINPUTDEVICELIST))
    if result == 0xFFFFFFFF:
        raise ctypes.WinError(ctypes.get_last_error())

    devices = []
    for record in records[: device_count.value]:
        info = _device_info_from_handle(int(record.hDevice))
        if info:
            devices.append(info)
    return devices


def _device_info_from_handle(handle: int) -> HidDeviceInfo | None:
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    GetRawInputDeviceInfoW = user32.GetRawInputDeviceInfoW
    GetRawInputDeviceInfoW.argtypes = [
        wintypes.HANDLE,
        wintypes.UINT,
        wintypes.LPVOID,
        ctypes.POINTER(wintypes.UINT),
    ]
    GetRawInputDeviceInfoW.restype = wintypes.UINT

    name_size = wintypes.UINT(0)
    GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, None, ctypes.byref(name_size))
    if not name_size.value:
        return None

    name_buffer = ctypes.create_unicode_buffer(name_size.value + 1)
    if GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, name_buffer, ctypes.byref(name_size)) == 0xFFFFFFFF:
        return None
    name = name_buffer.value

    info = RID_DEVICE_INFO()
    info.cbSize = ctypes.sizeof(RID_DEVICE_INFO)
    info_size = wintypes.UINT(ctypes.sizeof(RID_DEVICE_INFO))
    if GetRawInputDeviceInfoW(handle, RIDI_DEVICEINFO, ctypes.byref(info), ctypes.byref(info_size)) == 0xFFFFFFFF:
        vendor_id, product_id = parse_vid_pid(name)
        return HidDeviceInfo(handle=handle, name=name, vendor_id=vendor_id, product_id=product_id)

    vendor_id = None
    product_id = None
    version_number = None
    usage_page = None
    usage = None
    if info.dwType == RIM_TYPEHID:
        vendor_id = info.union.hid.dwVendorId
        product_id = info.union.hid.dwProductId
        version_number = info.union.hid.dwVersionNumber
        usage_page = info.union.hid.usUsagePage
        usage = info.union.hid.usUsage

    path_vendor, path_product = parse_vid_pid(name)
    return HidDeviceInfo(
        handle=handle,
        name=name,
        vendor_id=vendor_id or path_vendor,
        product_id=product_id or path_product,
        version_number=version_number,
        usage_page=usage_page,
        usage=usage,
    )


def _guess_hid_report_size(payload: bytes) -> int | None:
    if not payload:
        return None
    report_id = payload[0]
    if report_id in {0x28, 0x31} and len(payload) >= 13 and (len(payload) - 4) % 9 == 0:
        return len(payload)
    if report_id == 0x02 and len(payload) >= 21 and (len(payload) - 12) % 9 == 0:
        return len(payload)
    if report_id == 0xF7:
        return len(payload)
    return None


class RAWINPUTDEVICELIST(ctypes.Structure):
    _fields_ = [
        ("hDevice", wintypes.HANDLE),
        ("dwType", wintypes.DWORD),
    ]


class RAWINPUTDEVICE(ctypes.Structure):
    _fields_ = [
        ("usUsagePage", wintypes.USHORT),
        ("usUsage", wintypes.USHORT),
        ("dwFlags", wintypes.DWORD),
        ("hwndTarget", wintypes.HWND),
    ]


class RAWINPUTHEADER(ctypes.Structure):
    _fields_ = [
        ("dwType", wintypes.DWORD),
        ("dwSize", wintypes.DWORD),
        ("hDevice", wintypes.HANDLE),
        ("wParam", wintypes.WPARAM),
    ]


class RID_DEVICE_INFO_MOUSE(ctypes.Structure):
    _fields_ = [
        ("dwId", wintypes.DWORD),
        ("dwNumberOfButtons", wintypes.DWORD),
        ("dwSampleRate", wintypes.DWORD),
        ("fHasHorizontalWheel", wintypes.BOOL),
    ]


class RID_DEVICE_INFO_KEYBOARD(ctypes.Structure):
    _fields_ = [
        ("dwType", wintypes.DWORD),
        ("dwSubType", wintypes.DWORD),
        ("dwKeyboardMode", wintypes.DWORD),
        ("dwNumberOfFunctionKeys", wintypes.DWORD),
        ("dwNumberOfIndicators", wintypes.DWORD),
        ("dwNumberOfKeysTotal", wintypes.DWORD),
    ]


class RID_DEVICE_INFO_HID(ctypes.Structure):
    _fields_ = [
        ("dwVendorId", wintypes.DWORD),
        ("dwProductId", wintypes.DWORD),
        ("dwVersionNumber", wintypes.DWORD),
        ("usUsagePage", wintypes.USHORT),
        ("usUsage", wintypes.USHORT),
    ]


class RID_DEVICE_INFO_UNION(ctypes.Union):
    _fields_ = [
        ("mouse", RID_DEVICE_INFO_MOUSE),
        ("keyboard", RID_DEVICE_INFO_KEYBOARD),
        ("hid", RID_DEVICE_INFO_HID),
    ]


class RID_DEVICE_INFO(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("dwType", wintypes.DWORD),
        ("union", RID_DEVICE_INFO_UNION),
    ]


WNDPROC = ctypes.WINFUNCTYPE(LRESULT, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)


class WNDCLASSEXW(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.UINT),
        ("style", wintypes.UINT),
        ("lpfnWndProc", WNDPROC),
        ("cbClsExtra", ctypes.c_int),
        ("cbWndExtra", ctypes.c_int),
        ("hInstance", wintypes.HINSTANCE),
        ("hIcon", wintypes.HICON),
        ("hCursor", HCURSOR),
        ("hbrBackground", wintypes.HBRUSH),
        ("lpszMenuName", wintypes.LPCWSTR),
        ("lpszClassName", wintypes.LPCWSTR),
        ("hIconSm", wintypes.HICON),
    ]


class POINT(ctypes.Structure):
    _fields_ = [
        ("x", ctypes.c_long),
        ("y", ctypes.c_long),
    ]


class MSG(ctypes.Structure):
    _fields_ = [
        ("hwnd", wintypes.HWND),
        ("message", wintypes.UINT),
        ("wParam", wintypes.WPARAM),
        ("lParam", wintypes.LPARAM),
        ("time", wintypes.DWORD),
        ("pt", POINT),
    ]

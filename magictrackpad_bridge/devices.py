from __future__ import annotations

from dataclasses import dataclass
import ctypes
import re
import sys
from typing import Iterable


APPLE_USB_VENDOR_ID = 0x05AC
APPLE_BLUETOOTH_VENDOR_ID = 0x004C

MAGIC_TRACKPAD = 0x030E
MAGIC_TRACKPAD_2 = 0x0265
MAGIC_TRACKPAD_2_USBC = 0x0324

MAGIC_TRACKPAD_PRODUCTS = {
    MAGIC_TRACKPAD: "Magic Trackpad",
    MAGIC_TRACKPAD_2: "Magic Trackpad 2",
    MAGIC_TRACKPAD_2_USBC: "Magic Trackpad USB-C",
}

APPLE_VENDOR_IDS = {APPLE_USB_VENDOR_ID, APPLE_BLUETOOTH_VENDOR_ID}


@dataclass(frozen=True)
class HidDeviceInfo:
    handle: int
    name: str
    vendor_id: int | None = None
    product_id: int | None = None
    version_number: int | None = None
    usage_page: int | None = None
    usage: int | None = None

    @property
    def product_name(self) -> str:
        if self.product_id in MAGIC_TRACKPAD_PRODUCTS:
            return MAGIC_TRACKPAD_PRODUCTS[self.product_id]
        return "Unknown HID device"

    @property
    def is_apple_magic_trackpad(self) -> bool:
        vendor_ok = self.vendor_id in APPLE_VENDOR_IDS or self._path_mentions_apple()
        return vendor_ok and self.product_id in MAGIC_TRACKPAD_PRODUCTS

    @property
    def is_bluetooth(self) -> bool:
        if self.vendor_id == APPLE_BLUETOOTH_VENDOR_ID:
            return True
        path = self.name.upper()
        return "BTHENUM" in path or "VID_004C" in path

    def _path_mentions_apple(self) -> bool:
        path = self.name.upper()
        return "VID_05AC" in path or "VID_004C" in path


_VID_PID_PATTERNS = (
    re.compile(r"(?:VID|VEN)_([0-9A-F]{4}).*?(?:PID|DEV)_([0-9A-F]{4})", re.I),
    re.compile(r"(?:VID|VEN)&([0-9A-F]{4,8}).*?(?:PID|DEV)&([0-9A-F]{4})", re.I),
)


def parse_vid_pid(device_name: str) -> tuple[int | None, int | None]:
    for pattern in _VID_PID_PATTERNS:
        match = pattern.search(device_name)
        if match:
            vendor = int(match.group(1)[-4:], 16)
            product = int(match.group(2), 16)
            return vendor, product
    return None, None


def multitouch_feature_report(device: HidDeviceInfo) -> bytes | None:
    if device.product_id in {MAGIC_TRACKPAD_2, MAGIC_TRACKPAD_2_USBC}:
        if device.is_bluetooth:
            return bytes([0xF1, 0x02, 0x01])
        return bytes([0x02, 0x01])
    if device.product_id == MAGIC_TRACKPAD:
        return bytes([0xD7, 0x01])
    return None


def find_magic_trackpads(devices: Iterable[HidDeviceInfo]) -> list[HidDeviceInfo]:
    return [device for device in devices if device.is_apple_magic_trackpad]


def require_windows() -> None:
    if sys.platform != "win32":
        raise RuntimeError("magictrackpad-bridge uses Win32 Raw Input and only runs on Windows.")


def send_feature_report(device: HidDeviceInfo, report: bytes) -> bool:
    require_windows()

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    hid = ctypes.WinDLL("hid", use_last_error=True)

    CreateFileW = kernel32.CreateFileW
    CreateFileW.argtypes = [
        ctypes.c_wchar_p,
        ctypes.c_uint32,
        ctypes.c_uint32,
        ctypes.c_void_p,
        ctypes.c_uint32,
        ctypes.c_uint32,
        ctypes.c_void_p,
    ]
    CreateFileW.restype = ctypes.c_void_p

    CloseHandle = kernel32.CloseHandle
    CloseHandle.argtypes = [ctypes.c_void_p]
    CloseHandle.restype = ctypes.c_int

    HidD_SetFeature = hid.HidD_SetFeature
    HidD_SetFeature.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32]
    HidD_SetFeature.restype = ctypes.c_int

    GENERIC_READ = 0x80000000
    GENERIC_WRITE = 0x40000000
    FILE_SHARE_READ = 0x00000001
    FILE_SHARE_WRITE = 0x00000002
    OPEN_EXISTING = 3
    FILE_ATTRIBUTE_NORMAL = 0x00000080
    INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

    access_attempts = [GENERIC_READ | GENERIC_WRITE, GENERIC_WRITE, 0]
    for access in access_attempts:
        handle = CreateFileW(
            device.name,
            access,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            None,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            None,
        )
        if handle == INVALID_HANDLE_VALUE:
            continue
        try:
            payload = (ctypes.c_ubyte * len(report)).from_buffer_copy(report)
            ctypes.set_last_error(0)
            if HidD_SetFeature(handle, payload, len(report)):
                return True
            if ctypes.get_last_error() == 1 and device.is_apple_magic_trackpad:
                return True
        finally:
            CloseHandle(handle)
    return False

#pragma once

#include <ntddk.h>
#include <wdm.h>
#include <initguid.h>
#include <ntstrsafe.h>
#include <bthdef.h>
#include <ntintsafe.h>
#include <bthguid.h>
#include <bthioctl.h>
#include <sdpnode.h>
#include <bthddi.h>
#include <bthsdpddi.h>
#include <bthsdpdef.h>
#include "usbioctl.h"
#include "usbdi.h"

#ifdef __cplusplus
extern "C" {
#endif

#define DRIVERNAME "AppleKeyboardFilter"

#if DBG
#define DebugPrint(...) DbgPrint(DRIVERNAME ": " __VA_ARGS__)
#else
#define DebugPrint(...)
#endif

extern ULONG g_EmitFnAsF23;

enum AppleKeyboardHidCodes
{
    HidKeyNone = 0x00,
    HidF13 = 0x68,
    HidF14 = 0x69,
    HidF15 = 0x6A,
    HidF16 = 0x6B,
    HidF17 = 0x6C,
    HidF18 = 0x6D,
    HidF19 = 0x6E,
    HidF23 = 0x72,
};

enum AppleKeyboardHidMasks
{
    AppleSpecialFnMask = 0x02,
};

typedef struct _DEVICE_EXTENSION
{
    PDEVICE_OBJECT DeviceObject;
    PDEVICE_OBJECT LowerDeviceObject;
    PDEVICE_OBJECT Pdo;
    IO_REMOVE_LOCK RemoveLock;
    PIO_WORKITEM DiagWorkItem;
    volatile LONG DiagPending;
} DEVICE_EXTENSION, *PDEVICE_EXTENSION;

// Diagnostics: what the Bluetooth read path actually delivers. Written to
// HKLM\SYSTEM\CurrentControlSet\Services\AppleKeyboardFilter\Diag by a work item.
typedef struct _APPLE_KBD_DIAG
{
    volatile LONG Completions;   // BRB_L2CA_ACL_TRANSFER completions seen
    volatile LONG WithBuffer;    // ... that had a plain Buffer
    volatile LONG WithMdl;       // ... that only had an MDL
    volatile LONG NoBuffer;      // ... that had neither
    volatile LONG Processed;     // reports handed to the F23/lock translation
    volatile LONG LastSize;
    UCHAR LastRaw[16];           // last transport buffer, before translation
    UCHAR LastSpecialRaw[16];    // last one whose byte 1 or byte 8 was non-zero
    volatile LONG InternalIoctls;    // IRP_MJ_INTERNAL_DEVICE_CONTROL requests seen
    volatile LONG OtherIrps;         // every other request type seen (DispatchAny)
    volatile LONG MajorCounts[28];   // DispatchAny requests per major function
    volatile LONG RingIndex;
    volatile LONG PnpIrps;
    volatile LONG PowerIrps;
    volatile LONG StartCompletions;
    volatile LONG WorkItemRuns;
    volatile LONG Reads;           // IRP_MJ_READ completions from the HID collection
    volatile LONG ReadsWithData;   // ... that carried a full keyboard report
    volatile LONG ReadsProcessed;  // ... that went through the fn/lock translation
    UCHAR LastReadRaw[16];         // last report read, before translation
    UCHAR LastReadSpecial[16];     // last one whose vendor byte was non-zero
    volatile LONG ReportRingIndex;   // next slot to write in ReportRing
    UCHAR ReportRing[32][10];        // last non-empty reports, before translation
    ULONG Ring[16][2];               // last internal requests: {ioctl code, BRB/URB type}
} APPLE_KBD_DIAG;

extern APPLE_KBD_DIAG g_Diag;

DRIVER_INITIALIZE DriverEntry;
DRIVER_UNLOAD DriverUnload;
DRIVER_ADD_DEVICE AddDevice;

NTSTATUS DispatchAny(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp);
NTSTATUS DispatchPower(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp);
NTSTATUS DispatchPnp(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp);
NTSTATUS DispatchInternalIoctl(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp);
NTSTATUS DispatchRead(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp);
NTSTATUS ReadComplete(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp, _In_ PVOID Context);
NTSTATUS InternalIoctlComplete(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp, _In_ PVOID Context);
NTSTATUS StartDeviceComplete(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp, _In_ PVOID Context);
NTSTATUS UsageNotificationComplete(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp, _In_ PVOID Context);

NTSTATUS CompleteRequest(_Inout_ PIRP Irp, _In_ NTSTATUS Status, _In_ ULONG_PTR Information);
ULONG GetLowerDeviceType(_In_ PDEVICE_OBJECT Pdo);
NTSTATUS ReadDriverDword(_In_ PUNICODE_STRING RegistryPath, _In_ PCWSTR ValueName, _Inout_ PULONG Value);
VOID ProcessAppleKeyboardReport(_Inout_updates_bytes_(Size) PUCHAR Report, _In_ ULONG Size);
BOOLEAN TryProcessAppleKeyboardTransportBuffer(_Inout_updates_bytes_(Size) PUCHAR Buffer, _In_ ULONG Size, _In_ ULONG PrefixBytes);

#ifdef __cplusplus
}
#endif

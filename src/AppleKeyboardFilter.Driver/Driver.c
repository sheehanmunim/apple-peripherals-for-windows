#include "Driver.h"

static VOID RemoveDevice(_In_ PDEVICE_OBJECT DeviceObject);

#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT, DriverEntry)
#pragma alloc_text(PAGE, DriverUnload)
#pragma alloc_text(PAGE, AddDevice)
#pragma alloc_text(PAGE, RemoveDevice)
#pragma alloc_text(PAGE, GetLowerDeviceType)
#pragma alloc_text(PAGE, ReadDriverDword)
#endif

ULONG g_EmitFnAsF23 = 1;
APPLE_KBD_DIAG g_Diag;

static VOID SetDiagDword(_In_ HANDLE key, _In_ PCWSTR name, _In_ ULONG value)
{
    UNICODE_STRING valueName;
    RtlInitUnicodeString(&valueName, name);
    ZwSetValueKey(key, &valueName, 0, REG_DWORD, &value, sizeof(value));
}

static VOID SetDiagBinary(_In_ HANDLE key, _In_ PCWSTR name, _In_reads_bytes_(length) PVOID data, _In_ ULONG length)
{
    UNICODE_STRING valueName;
    RtlInitUnicodeString(&valueName, name);
    ZwSetValueKey(key, &valueName, 0, REG_BINARY, data, length);
}

static VOID DiagWorkItemRoutine(_In_ PDEVICE_OBJECT DeviceObject, _In_opt_ PVOID Context);

static VOID QueueDiag(_In_ PDEVICE_EXTENSION extension)
{
    if (extension->DiagWorkItem != NULL &&
        InterlockedCompareExchange(&extension->DiagPending, 1, 0) == 0)
    {
        if (NT_SUCCESS(IoAcquireRemoveLock(&extension->RemoveLock, &extension->DiagPending)))
        {
            IoQueueWorkItem(extension->DiagWorkItem, DiagWorkItemRoutine, DelayedWorkQueue, NULL);
        }
        else
        {
            InterlockedExchange(&extension->DiagPending, 0);
        }
    }
}

static VOID DiagWorkItemRoutine(_In_ PDEVICE_OBJECT DeviceObject, _In_opt_ PVOID Context)
{
    UNREFERENCED_PARAMETER(Context);
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)DeviceObject->DeviceExtension;

    UNICODE_STRING path;
    RtlInitUnicodeString(&path, L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Services\\AppleKeyboardFilter");
    OBJECT_ATTRIBUTES attributes;
    InitializeObjectAttributes(&attributes, &path, OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);

    HANDLE key = NULL;
    InterlockedIncrement(&g_Diag.WorkItemRuns);
    NTSTATUS openStatus = ZwOpenKey(&key, KEY_SET_VALUE, &attributes);
    if (NT_SUCCESS(openStatus))
    {
        SetDiagDword(key, L"DiagWorkItemRuns", (ULONG)g_Diag.WorkItemRuns);
        SetDiagDword(key, L"DiagPnpIrps", (ULONG)g_Diag.PnpIrps);
        SetDiagDword(key, L"DiagPowerIrps", (ULONG)g_Diag.PowerIrps);
        SetDiagDword(key, L"DiagStartCompletions", (ULONG)g_Diag.StartCompletions);
        SetDiagDword(key, L"Reads", (ULONG)g_Diag.Reads);
        SetDiagDword(key, L"ReadsWithData", (ULONG)g_Diag.ReadsWithData);
        SetDiagDword(key, L"ReadsProcessed", (ULONG)g_Diag.ReadsProcessed);
        SetDiagBinary(key, L"LastReadRaw", (PVOID)g_Diag.LastReadRaw, sizeof(g_Diag.LastReadRaw));
        SetDiagBinary(key, L"LastReadSpecial", (PVOID)g_Diag.LastReadSpecial, sizeof(g_Diag.LastReadSpecial));
        SetDiagDword(key, L"ReportRingIndex", (ULONG)g_Diag.ReportRingIndex);
        SetDiagBinary(key, L"ReportRing", (PVOID)g_Diag.ReportRing, sizeof(g_Diag.ReportRing));
        UCHAR raw[16];
        UCHAR special[16];
        RtlCopyMemory(raw, g_Diag.LastRaw, sizeof(raw));
        RtlCopyMemory(special, g_Diag.LastSpecialRaw, sizeof(special));

        SetDiagDword(key, L"Completions", (ULONG)g_Diag.Completions);
        SetDiagDword(key, L"WithBuffer", (ULONG)g_Diag.WithBuffer);
        SetDiagDword(key, L"WithMdl", (ULONG)g_Diag.WithMdl);
        SetDiagDword(key, L"NoBuffer", (ULONG)g_Diag.NoBuffer);
        SetDiagDword(key, L"Processed", (ULONG)g_Diag.Processed);
        SetDiagDword(key, L"LastSize", (ULONG)g_Diag.LastSize);
        SetDiagDword(key, L"InternalIoctls", (ULONG)g_Diag.InternalIoctls);
        SetDiagDword(key, L"OtherIrps", (ULONG)g_Diag.OtherIrps);
        SetDiagBinary(key, L"MajorCounts", (PVOID)g_Diag.MajorCounts, sizeof(g_Diag.MajorCounts));
        SetDiagBinary(key, L"Ring", (PVOID)g_Diag.Ring, sizeof(g_Diag.Ring));
        SetDiagBinary(key, L"LastRaw", raw, sizeof(raw));
        SetDiagBinary(key, L"LastSpecialRaw", special, sizeof(special));
        ZwClose(key);
    }

    InterlockedExchange(&extension->DiagPending, 0);
    IoReleaseRemoveLock(&extension->RemoveLock, &extension->DiagPending);
}

static VOID RemoveDevice(_In_ PDEVICE_OBJECT DeviceObject)
{
    PAGED_CODE();

    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)DeviceObject->DeviceExtension;
    if (extension->LowerDeviceObject != NULL)
    {
        IoDetachDevice(extension->LowerDeviceObject);
    }

    if (extension->DiagWorkItem != NULL)
    {
        IoFreeWorkItem(extension->DiagWorkItem);
        extension->DiagWorkItem = NULL;
    }

    IoDeleteDevice(DeviceObject);
}

NTSTATUS DriverEntry(_In_ PDRIVER_OBJECT DriverObject, _In_ PUNICODE_STRING RegistryPath)
{
    DebugPrint("DriverEntry\n");

    DriverObject->DriverUnload = DriverUnload;
    DriverObject->DriverExtension->AddDevice = AddDevice;

    for (ULONG index = 0; index <= IRP_MJ_MAXIMUM_FUNCTION; index++)
    {
        DriverObject->MajorFunction[index] = DispatchAny;
    }

    DriverObject->MajorFunction[IRP_MJ_POWER] = DispatchPower;
    DriverObject->MajorFunction[IRP_MJ_PNP] = DispatchPnp;
    DriverObject->MajorFunction[IRP_MJ_INTERNAL_DEVICE_CONTROL] = DispatchInternalIoctl;

    // When the filter is attached to the HID keyboard collection instead of the
    // Bluetooth transport, the reports arrive as ordinary reads from kbdhid.
    DriverObject->MajorFunction[IRP_MJ_READ] = DispatchRead;

    // The tunable lives under the service's Parameters subkey (InfVerif 1323).
    WCHAR pathBuffer[512];
    UNICODE_STRING parameters;
    parameters.Buffer = pathBuffer;
    parameters.Length = 0;
    parameters.MaximumLength = sizeof(pathBuffer);
    if (NT_SUCCESS(RtlAppendUnicodeStringToString(&parameters, RegistryPath)) &&
        NT_SUCCESS(RtlAppendUnicodeToString(&parameters, L"\Parameters")))
    {
        ReadDriverDword(&parameters, L"EmitFnAsF23", &g_EmitFnAsF23);
    }
    return STATUS_SUCCESS;
}

VOID DriverUnload(_In_ PDRIVER_OBJECT DriverObject)
{
    PAGED_CODE();
    UNREFERENCED_PARAMETER(DriverObject);
    DebugPrint("DriverUnload\n");
}

NTSTATUS AddDevice(_In_ PDRIVER_OBJECT DriverObject, _In_ PDEVICE_OBJECT Pdo)
{
    PAGED_CODE();

    PDEVICE_OBJECT filterDevice = NULL;
    NTSTATUS status = IoCreateDevice(
        DriverObject,
        sizeof(DEVICE_EXTENSION),
        NULL,
        GetLowerDeviceType(Pdo),
        0,
        FALSE,
        &filterDevice);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)filterDevice->DeviceExtension;
    RtlZeroMemory(extension, sizeof(DEVICE_EXTENSION));
    IoInitializeRemoveLock(&extension->RemoveLock, 0, 0, 0);
    extension->DeviceObject = filterDevice;
    extension->Pdo = Pdo;
    extension->DiagWorkItem = IoAllocateWorkItem(filterDevice);

    PDEVICE_OBJECT lowerDevice = IoAttachDeviceToDeviceStack(filterDevice, Pdo);
    if (lowerDevice == NULL)
    {
        IoDeleteDevice(filterDevice);
        return STATUS_DEVICE_REMOVED;
    }

    extension->LowerDeviceObject = lowerDevice;
    filterDevice->Flags |= lowerDevice->Flags & (DO_DIRECT_IO | DO_BUFFERED_IO | DO_POWER_PAGABLE);
    filterDevice->Flags &= ~DO_DEVICE_INITIALIZING;
    return STATUS_SUCCESS;
}

ULONG GetLowerDeviceType(_In_ PDEVICE_OBJECT Pdo)
{
    PAGED_CODE();

    PDEVICE_OBJECT lowerDevice = IoGetAttachedDeviceReference(Pdo);
    if (lowerDevice == NULL)
    {
        return FILE_DEVICE_UNKNOWN;
    }

    ULONG deviceType = lowerDevice->DeviceType;
    ObDereferenceObject(lowerDevice);
    return deviceType;
}

NTSTATUS CompleteRequest(_Inout_ PIRP Irp, _In_ NTSTATUS Status, _In_ ULONG_PTR Information)
{
    Irp->IoStatus.Status = Status;
    Irp->IoStatus.Information = Information;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return Status;
}

NTSTATUS DispatchAny(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp)
{
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)DeviceObject->DeviceExtension;
    NTSTATUS status = IoAcquireRemoveLock(&extension->RemoveLock, Irp);
    if (!NT_SUCCESS(status))
    {
        return CompleteRequest(Irp, status, 0);
    }

    UCHAR major = IoGetCurrentIrpStackLocation(Irp)->MajorFunction;
    if (major < RTL_NUMBER_OF(g_Diag.MajorCounts))
    {
        InterlockedIncrement(&g_Diag.MajorCounts[major]);
    }
    if ((InterlockedIncrement(&g_Diag.OtherIrps) % 64) == 1)
    {
        QueueDiag(extension);
    }

    IoSkipCurrentIrpStackLocation(Irp);
    status = IoCallDriver(extension->LowerDeviceObject, Irp);
    IoReleaseRemoveLock(&extension->RemoveLock, Irp);
    return status;
}

NTSTATUS DispatchPower(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp)
{
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)DeviceObject->DeviceExtension;

#pragma warning(suppress: 28175)
    PoStartNextPowerIrp(Irp);

    NTSTATUS status = IoAcquireRemoveLock(&extension->RemoveLock, Irp);
    if (!NT_SUCCESS(status))
    {
        return CompleteRequest(Irp, status, 0);
    }

    IoSkipCurrentIrpStackLocation(Irp);
    status = PoCallDriver(extension->LowerDeviceObject, Irp);
    IoReleaseRemoveLock(&extension->RemoveLock, Irp);
    return status;
}

NTSTATUS DispatchPnp(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp)
{
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)DeviceObject->DeviceExtension;
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(Irp);

    NTSTATUS status = IoAcquireRemoveLock(&extension->RemoveLock, Irp);
    if (!NT_SUCCESS(status))
    {
        return CompleteRequest(Irp, status, 0);
    }

    InterlockedIncrement(&g_Diag.PnpIrps);

    if (stack->MinorFunction == IRP_MN_REMOVE_DEVICE)
    {
        IoSkipCurrentIrpStackLocation(Irp);
        status = IoCallDriver(extension->LowerDeviceObject, Irp);
        IoReleaseRemoveLockAndWait(&extension->RemoveLock, Irp);
        RemoveDevice(DeviceObject);
        return status;
    }

    if (stack->MinorFunction == IRP_MN_DEVICE_USAGE_NOTIFICATION)
    {
        if (DeviceObject->AttachedDevice == NULL ||
            (DeviceObject->AttachedDevice->Flags & DO_POWER_PAGABLE) != 0)
        {
            DeviceObject->Flags |= DO_POWER_PAGABLE;
        }

        IoCopyCurrentIrpStackLocationToNext(Irp);
        IoSetCompletionRoutine(Irp, UsageNotificationComplete, extension, TRUE, TRUE, TRUE);
        return IoCallDriver(extension->LowerDeviceObject, Irp);
    }

    if (stack->MinorFunction == IRP_MN_START_DEVICE)
    {
        IoCopyCurrentIrpStackLocationToNext(Irp);
        IoSetCompletionRoutine(Irp, StartDeviceComplete, extension, TRUE, TRUE, TRUE);
        return IoCallDriver(extension->LowerDeviceObject, Irp);
    }

    IoSkipCurrentIrpStackLocation(Irp);
    status = IoCallDriver(extension->LowerDeviceObject, Irp);
    IoReleaseRemoveLock(&extension->RemoveLock, Irp);
    return status;
}

NTSTATUS StartDeviceComplete(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp, _In_ PVOID Context)
{
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)Context;
    UNREFERENCED_PARAMETER(DeviceObject);

    if (Irp->PendingReturned)
    {
        IoMarkIrpPending(Irp);
    }

    if ((extension->LowerDeviceObject->Characteristics & FILE_REMOVABLE_MEDIA) != 0)
    {
        extension->DeviceObject->Characteristics |= FILE_REMOVABLE_MEDIA;
    }

    InterlockedIncrement(&g_Diag.StartCompletions);
    QueueDiag(extension);

    IoReleaseRemoveLock(&extension->RemoveLock, Irp);
    return STATUS_SUCCESS;
}

NTSTATUS UsageNotificationComplete(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp, _In_ PVOID Context)
{
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)Context;
    UNREFERENCED_PARAMETER(DeviceObject);

    if (Irp->PendingReturned)
    {
        IoMarkIrpPending(Irp);
    }

    if ((extension->LowerDeviceObject->Flags & DO_POWER_PAGABLE) == 0)
    {
        extension->DeviceObject->Flags &= ~DO_POWER_PAGABLE;
    }

    IoReleaseRemoveLock(&extension->RemoveLock, Irp);
    return STATUS_SUCCESS;
}

NTSTATUS InternalIoctlComplete(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp, _In_ PVOID Context)
{
    UNREFERENCED_PARAMETER(DeviceObject);

    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)Context;
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(Irp);

    if (NT_SUCCESS(Irp->IoStatus.Status))
    {
        ULONG controlCode = stack->Parameters.DeviceIoControl.IoControlCode;
        if (controlCode == IOCTL_INTERNAL_BTH_SUBMIT_BRB)
        {
            PBRB brb = (PBRB)stack->Parameters.Others.Argument1;
            if (brb != NULL && brb->BrbHeader.Type == BRB_L2CA_ACL_TRANSFER)
            {
                PUCHAR buffer = (PUCHAR)brb->BrbL2caAclTransfer.Buffer;
                ULONG size = brb->BrbL2caAclTransfer.BufferSize;
                LONG completions = InterlockedIncrement(&g_Diag.Completions);
                if (buffer != NULL)
                {
                    InterlockedIncrement(&g_Diag.WithBuffer);
                }
                else if (brb->BrbL2caAclTransfer.BufferMDL != NULL)
                {
                    InterlockedIncrement(&g_Diag.WithMdl);
                    buffer = (PUCHAR)MmGetSystemAddressForMdlSafe(
                        brb->BrbL2caAclTransfer.BufferMDL,
                        NormalPagePriority | MdlMappingNoExecute);
                }
                else
                {
                    InterlockedIncrement(&g_Diag.NoBuffer);
                }

                BOOLEAN interesting = FALSE;
                if (buffer != NULL && size >= 4)
                {
                    ULONG copy = size < sizeof(g_Diag.LastRaw) ? size : sizeof(g_Diag.LastRaw);
                    g_Diag.LastSize = (LONG)size;
                    RtlCopyMemory(g_Diag.LastRaw, buffer, copy);
                    if (buffer[3] != 0 || (size > 10 && buffer[10] != 0))
                    {
                        RtlZeroMemory(g_Diag.LastSpecialRaw, sizeof(g_Diag.LastSpecialRaw));
                        RtlCopyMemory(g_Diag.LastSpecialRaw, buffer, copy);
                        interesting = TRUE;
                    }
                }

                if (TryProcessAppleKeyboardTransportBuffer(buffer, size, 2))
                {
                    InterlockedIncrement(&g_Diag.Processed);
                }

                if (interesting || completions == 1 || (completions % 32) == 0)
                {
                    QueueDiag(extension);
                }
            }
        }
        else if (controlCode == IOCTL_INTERNAL_USB_SUBMIT_URB)
        {
            PURB urb = (PURB)stack->Parameters.Others.Argument1;
            if (urb != NULL && urb->UrbHeader.Function == URB_FUNCTION_BULK_OR_INTERRUPT_TRANSFER)
            {
                PUCHAR buffer = (PUCHAR)urb->UrbBulkOrInterruptTransfer.TransferBuffer;
                if (buffer == NULL && urb->UrbBulkOrInterruptTransfer.TransferBufferMDL != NULL)
                {
                    buffer = (PUCHAR)MmGetSystemAddressForMdlSafe(
                        urb->UrbBulkOrInterruptTransfer.TransferBufferMDL,
                        NormalPagePriority | MdlMappingNoExecute);
                }

                TryProcessAppleKeyboardTransportBuffer(
                    buffer,
                    urb->UrbBulkOrInterruptTransfer.TransferBufferLength,
                    1);
            }
        }
    }

    if (Irp->PendingReturned)
    {
        IoMarkIrpPending(Irp);
    }

    IoReleaseRemoveLock(&extension->RemoveLock, Irp);
    return STATUS_SUCCESS;
}

// The report buffer of a read, whichever way this stack passes it along.
static PUCHAR GetReadBuffer(_In_ PIRP Irp)
{
    if (Irp->MdlAddress != NULL)
    {
        return (PUCHAR)MmGetSystemAddressForMdlSafe(Irp->MdlAddress, NormalPagePriority | MdlMappingNoExecute);
    }

    if (Irp->AssociatedIrp.SystemBuffer != NULL)
    {
        return (PUCHAR)Irp->AssociatedIrp.SystemBuffer;
    }

    // Only trust a raw pointer when it is a kernel address: a user-mode one
    // would belong to some other process by the time the read completes.
    if ((ULONG_PTR)Irp->UserBuffer >= (ULONG_PTR)MM_SYSTEM_RANGE_START)
    {
        return (PUCHAR)Irp->UserBuffer;
    }

    return NULL;
}

NTSTATUS ReadComplete(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp, _In_ PVOID Context)
{
    UNREFERENCED_PARAMETER(DeviceObject);
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)Context;

    if (NT_SUCCESS(Irp->IoStatus.Status) && Irp->IoStatus.Information >= 9)
    {
        ULONG size = (ULONG)Irp->IoStatus.Information;
        PUCHAR buffer = GetReadBuffer(Irp);
        if (buffer != NULL)
        {
            ULONG copy = size < sizeof(g_Diag.LastReadRaw) ? size : sizeof(g_Diag.LastReadRaw);
            RtlZeroMemory(g_Diag.LastReadRaw, sizeof(g_Diag.LastReadRaw));
            RtlCopyMemory(g_Diag.LastReadRaw, buffer, copy);
            InterlockedIncrement(&g_Diag.ReadsWithData);

            // A 10-byte report is report ID 1 plus the 9-byte boot-keyboard
            // body; a 9-byte one is the body on its own.
            ULONG prefix = (size >= 10 && buffer[0] == 1) ? 1 : 0;
            // Keep the last few non-empty reports exactly as they arrived, so
            // this keyboard's real vendor-bit layout can be read off later.
            BOOLEAN nonEmpty = FALSE;
            for (ULONG i = prefix; i < size && i < prefix + 9; i++)
            {
                if (buffer[i] != 0) { nonEmpty = TRUE; break; }
            }
            if (nonEmpty)
            {
                LONG slot = (InterlockedIncrement(&g_Diag.ReportRingIndex) - 1) & 31;
                RtlZeroMemory(g_Diag.ReportRing[slot], 10);
                RtlCopyMemory(g_Diag.ReportRing[slot], buffer, copy < 10 ? copy : 10);
            }
            BOOLEAN special = (buffer[prefix + 8] != 0) || (buffer[prefix + 1] != 0);
            if (special || nonEmpty)
            {
                RtlZeroMemory(g_Diag.LastReadSpecial, sizeof(g_Diag.LastReadSpecial));
                RtlCopyMemory(g_Diag.LastReadSpecial, buffer, copy);
            }

            if (TryProcessAppleKeyboardTransportBuffer(buffer, size, prefix))
            {
                InterlockedIncrement(&g_Diag.ReadsProcessed);
            }

            if (special || nonEmpty)
            {
                QueueDiag(extension);
            }
        }
    }

    if (Irp->PendingReturned)
    {
        IoMarkIrpPending(Irp);
    }

    IoReleaseRemoveLock(&extension->RemoveLock, Irp);
    return STATUS_SUCCESS;
}

NTSTATUS DispatchRead(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp)
{
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)DeviceObject->DeviceExtension;

    NTSTATUS status = IoAcquireRemoveLock(&extension->RemoveLock, Irp);
    if (!NT_SUCCESS(status))
    {
        return CompleteRequest(Irp, status, 0);
    }

    if ((InterlockedIncrement(&g_Diag.Reads) % 32) == 1)
    {
        QueueDiag(extension);
    }

    IoCopyCurrentIrpStackLocationToNext(Irp);
    IoSetCompletionRoutine(Irp, ReadComplete, extension, TRUE, TRUE, TRUE);
    return IoCallDriver(extension->LowerDeviceObject, Irp);
}

NTSTATUS DispatchInternalIoctl(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp)
{
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)DeviceObject->DeviceExtension;
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(Irp);
    ULONG controlCode = stack->Parameters.DeviceIoControl.IoControlCode;
    BOOLEAN inspectOnComplete = FALSE;

    NTSTATUS status = IoAcquireRemoveLock(&extension->RemoveLock, Irp);
    if (!NT_SUCCESS(status))
    {
        return CompleteRequest(Irp, status, 0);
    }

    {
        LONG slot = (InterlockedIncrement(&g_Diag.RingIndex) - 1) & 15;
        ULONG type = 0;
        if (controlCode == IOCTL_INTERNAL_BTH_SUBMIT_BRB && stack->Parameters.Others.Argument1 != NULL)
        {
            type = ((PBRB)stack->Parameters.Others.Argument1)->BrbHeader.Type;
        }
        g_Diag.Ring[slot][0] = controlCode;
        g_Diag.Ring[slot][1] = type;
        if ((InterlockedIncrement(&g_Diag.InternalIoctls) % 64) == 1)
        {
            QueueDiag(extension);
        }
    }

    if (controlCode == IOCTL_INTERNAL_BTH_SUBMIT_BRB)
    {
        PBRB brb = (PBRB)stack->Parameters.Others.Argument1;
        inspectOnComplete = brb != NULL && brb->BrbHeader.Type == BRB_L2CA_ACL_TRANSFER;
    }
    else if (controlCode == IOCTL_INTERNAL_USB_SUBMIT_URB)
    {
        inspectOnComplete = TRUE;
    }

    if (inspectOnComplete)
    {
        IoCopyCurrentIrpStackLocationToNext(Irp);
        IoSetCompletionRoutine(Irp, InternalIoctlComplete, extension, TRUE, TRUE, TRUE);
        return IoCallDriver(extension->LowerDeviceObject, Irp);
    }

    IoSkipCurrentIrpStackLocation(Irp);
    status = IoCallDriver(extension->LowerDeviceObject, Irp);
    IoReleaseRemoveLock(&extension->RemoveLock, Irp);
    return status;
}

NTSTATUS ReadDriverDword(_In_ PUNICODE_STRING RegistryPath, _In_ PCWSTR ValueName, _Inout_ PULONG Value)
{
    PAGED_CODE();

    HANDLE key = NULL;
    OBJECT_ATTRIBUTES attributes;
    InitializeObjectAttributes(&attributes, RegistryPath, OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);

    NTSTATUS status = ZwOpenKey(&key, KEY_READ, &attributes);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    UNICODE_STRING valueName;
    RtlInitUnicodeString(&valueName, ValueName);

    UCHAR buffer[sizeof(KEY_VALUE_PARTIAL_INFORMATION) + sizeof(ULONG)] = { 0 };
    ULONG resultLength = 0;
    status = ZwQueryValueKey(
        key,
        &valueName,
        KeyValuePartialInformation,
        buffer,
        sizeof(buffer),
        &resultLength);

    if (NT_SUCCESS(status))
    {
        PKEY_VALUE_PARTIAL_INFORMATION value = (PKEY_VALUE_PARTIAL_INFORMATION)buffer;
        if (value->Type == REG_DWORD && value->DataLength == sizeof(ULONG))
        {
            RtlCopyMemory(Value, value->Data, sizeof(ULONG));
        }
    }

    ZwClose(key);
    return status;
}

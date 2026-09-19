#include "Driver.h"

// Puts `hidUsage` into the first free key slot unless it is already there.
static VOID AddKeySlot(_Inout_updates_(6) PUCHAR keySlots, _In_ UCHAR hidUsage)
{
    for (ULONG index = 0; index < 6; index++)
    {
        if (keySlots[index] == hidUsage)
        {
            return;
        }
    }

    for (ULONG index = 0; index < 6; index++)
    {
        if (keySlots[index] == HidKeyNone)
        {
            keySlots[index] = hidUsage;
            return;
        }
    }
}

VOID ProcessAppleKeyboardReport(_Inout_updates_bytes_(Size) PUCHAR Report, _In_ ULONG Size)
{
    if (Report == NULL || Size < 9 || !g_EmitFnAsF23)
    {
        return;
    }

    PUCHAR modifiers = &Report[0];
    PUCHAR keySlots = &Report[2];
    PUCHAR specialKey = &Report[8];

    // fn becomes a real Left Control: a modifier bit, not a key slot, so
    // fn+key combinations arrive as Ctrl+key.
    if ((*specialKey & AppleSpecialFnMask) != 0)
    {
        *modifiers |= HidLeftCtrlMask;
    }

    // The lock key becomes forward Delete (Supr).
    if ((*specialKey & AppleSpecialLockMask) != 0)
    {
        AddKeySlot(keySlots, HidDeleteForward);
    }

    // Windows discards the vendor byte anyway; clear it so nothing downstream
    // sees a partially handled report.
    *specialKey = 0;
}

BOOLEAN TryProcessAppleKeyboardTransportBuffer(_Inout_updates_bytes_(Size) PUCHAR Buffer, _In_ ULONG Size, _In_ ULONG PrefixBytes)
{
    const ULONG keyboardReportBodyLength = 9;
    if (Buffer == NULL || Size < PrefixBytes + keyboardReportBodyLength)
    {
        return FALSE;
    }

    ProcessAppleKeyboardReport(Buffer + PrefixBytes, keyboardReportBodyLength);
    return TRUE;
}

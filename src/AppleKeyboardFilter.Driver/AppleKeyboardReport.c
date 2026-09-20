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
    if (Report == NULL || Size < 9)
    {
        return;
    }

    PUCHAR modifiers = &Report[0];
    PUCHAR keySlots = &Report[2];
    PUCHAR specialKey = &Report[8];

    // fn and the lock key are vendor bits Windows discards. Turn each one into
    // a modifier bit, a key slot, or both, according to the configuration.
    if ((*specialKey & AppleSpecialFnMask) != 0)
    {
        *modifiers |= (UCHAR)g_FnModifier;
        if (g_FnUsage != 0)
        {
            AddKeySlot(keySlots, (UCHAR)g_FnUsage);
        }
    }

    if ((*specialKey & AppleSpecialLockMask) != 0)
    {
        *modifiers |= (UCHAR)g_LockModifier;
        if (g_LockUsage != 0)
        {
            AddKeySlot(keySlots, (UCHAR)g_LockUsage);
        }
    }

    // Eject, on the keyboards that have one instead of a lock key.
    if ((*specialKey & AppleSpecialEjectMask) != 0)
    {
        *modifiers |= (UCHAR)g_EjectModifier;
        if (g_EjectUsage != 0)
        {
            AddKeySlot(keySlots, (UCHAR)g_EjectUsage);
        }
    }

    // Nothing downstream understands the vendor byte, so clear it.
    *specialKey = 0;

    // Apple's ISO layout sends the key below Esc and the key beside left Shift
    // the other way round from what Windows expects, which is what puts | and <
    // on the wrong keys.
    if (g_SwapIsoKeys)
    {
        for (ULONG index = 0; index < 6; index++)
        {
            if (keySlots[index] == HidGraveAccent)
            {
                keySlots[index] = HidNonUsBackslash;
            }
            else if (keySlots[index] == HidNonUsBackslash)
            {
                keySlots[index] = HidGraveAccent;
            }
        }
    }
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

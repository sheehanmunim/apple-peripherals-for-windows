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

    ApplyRemapTables(modifiers, keySlots);
}

// The general key remapping. Modifiers are read from a snapshot so a mapping
// cannot cascade into one made earlier in the same report, and new key slots
// are collected separately for the same reason.
VOID ApplyRemapTables(_Inout_ PUCHAR Modifiers, _Inout_updates_(6) PUCHAR KeySlots)
{
    const UCHAR originalModifiers = *Modifiers;
    UCHAR newModifiers = 0;
    UCHAR added[6] = { 0 };
    ULONG addedCount = 0;

    for (ULONG bit = 0; bit < 8; bit++)
    {
        const UCHAR mask = (UCHAR)(1u << bit);
        if ((originalModifiers & mask) == 0)
        {
            continue;
        }

        const UCHAR toModifier = g_ModifierToModifier[bit];
        const UCHAR toUsage = g_ModifierToUsage[bit];

        if (toUsage != 0 && addedCount < RTL_NUMBER_OF(added))
        {
            added[addedCount++] = toUsage;
        }

        if (toModifier == WB_DISABLED)
        {
            continue;               // swallowed
        }
        newModifiers |= (toModifier != 0) ? toModifier : mask;
    }

    *Modifiers = newModifiers;

    for (ULONG index = 0; index < 6; index++)
    {
        const UCHAR usage = KeySlots[index];
        if (usage == HidKeyNone)
        {
            continue;
        }

        const UCHAR toModifier = g_UsageToModifier[usage];
        if (toModifier != 0)
        {
            *Modifiers |= toModifier;
            KeySlots[index] = HidKeyNone;
            continue;
        }

        const UCHAR toUsage = g_UsageToUsage[usage];
        if (toUsage == WB_DISABLED)
        {
            KeySlots[index] = HidKeyNone;
        }
        else if (toUsage != 0)
        {
            KeySlots[index] = toUsage;
        }
    }

    for (ULONG i = 0; i < addedCount; i++)
    {
        AddKeySlot(KeySlots, added[i]);
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

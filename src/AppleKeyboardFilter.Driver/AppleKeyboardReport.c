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

    PUCHAR keySlots = &Report[2];
    PUCHAR specialKey = &Report[8];

    if ((*specialKey & AppleSpecialFnMask) != 0)
    {
        AddKeySlot(keySlots, HidF23);
        *specialKey &= ~AppleSpecialFnMask;
    }

    // Any other bit in the vendor byte (the Touch ID / lock key sends one of
    // these, Windows drops it) is reported as F13..F19 so the app can map it.
    // bit0 -> F13, bit2 -> F14, bit3 -> F15, bit4 -> F16, bit5 -> F17,
    // bit6 -> F18, bit7 -> F19.
    static const UCHAR extraBitToHid[8] = { HidF13, 0, HidF14, HidF15, HidF16, HidF17, HidF18, HidF19 };
    for (ULONG bit = 0; bit < 8; bit++)
    {
        const UCHAR mask = (UCHAR)(1u << bit);
        if (extraBitToHid[bit] != 0 && (*specialKey & mask) != 0)
        {
            AddKeySlot(keySlots, extraBitToHid[bit]);
            *specialKey &= (UCHAR)~mask;
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

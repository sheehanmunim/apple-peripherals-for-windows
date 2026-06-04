#include "Driver.h"

VOID ProcessAppleKeyboardReport(_Inout_updates_bytes_(Size) PUCHAR Report, _In_ ULONG Size)
{
    if (Report == NULL || Size < 9 || !g_EmitFnAsF23)
    {
        return;
    }

    PUCHAR keySlots = &Report[2];
    PUCHAR specialKey = &Report[8];
    if ((*specialKey & AppleSpecialFnMask) == 0)
    {
        return;
    }

    BOOLEAN alreadyPresent = FALSE;
    for (ULONG index = 0; index < 6; index++)
    {
        if (keySlots[index] == HidF23)
        {
            alreadyPresent = TRUE;
            break;
        }
    }

    if (!alreadyPresent)
    {
        for (ULONG index = 0; index < 6; index++)
        {
            if (keySlots[index] == HidKeyNone)
            {
                keySlots[index] = HidF23;
                break;
            }
        }
    }

    *specialKey &= ~AppleSpecialFnMask;
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

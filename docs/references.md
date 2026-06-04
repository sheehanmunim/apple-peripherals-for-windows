# References

This project intentionally starts as a user-mode bridge instead of a kernel driver.

Relevant upstream behavior:

- Linux `hid-magicmouse.c` identifies the Magic Trackpad report IDs, parses 9-byte touch records, and enables multitouch with an Apple feature report. Source: <https://kernel.googlesource.com/pub/scm/linux/kernel/git/rdma/rdma/+/refs/tags/v6.18-rc2/drivers/hid/hid-magicmouse.c>
- Linux `hid-ids.h` lists Apple product IDs, including Magic Trackpad `0x030e`, Magic Trackpad 2 `0x0265`, and Magic Trackpad USB-C `0x0324`. Source: <https://codebrowser.dev/linux/linux/drivers/hid/hid-ids.h.html>
- Microsoft `SendInput` inserts synthetic keyboard and mouse events into the Windows input stream. Source: <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput>
- Microsoft driver attestation signing is required for distributing a real Windows kernel driver to normal users. Source: <https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-attestation>
- Microsoft digitizer guidance describes HID multitouch digitizer report expectations for Windows. Source: <https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/supporting-usages-in-multitouch-digitizer-drivers-win8>


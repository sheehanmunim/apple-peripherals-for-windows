# References

Relevant upstream behavior:

- Linux `hid-magicmouse.c` identifies the Magic Trackpad report IDs, parses 9-byte touch records, and enables multitouch with an Apple feature report. Source: <https://kernel.googlesource.com/pub/scm/linux/kernel/git/rdma/rdma/+/refs/tags/v6.18-rc2/drivers/hid/hid-magicmouse.c>
- Linux `hid-ids.h` lists Apple product IDs, including Magic Trackpad `0x030e`, Magic Trackpad 2 `0x0265`, and Magic Trackpad USB-C `0x0324`. Source: <https://codebrowser.dev/linux/linux/drivers/hid/hid-ids.h.html>
- Microsoft `SendInput` inserts synthetic keyboard and mouse events into the Windows input stream. Source: <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput>
- WinAppleKey demonstrates a HID lower-filter driver that rewrites Apple Magic Keyboard reports before Windows consumes them. The Apple Keyboard Filter driver adapts that MIT-licensed approach and emits F23 for Globe/Fn so the app can map it through settings. Source: <https://github.com/samartzidis/WinAppleKey>

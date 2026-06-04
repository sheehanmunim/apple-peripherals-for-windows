# Driver Roadmap

The current project is a user-mode bridge. It discovers the Apple Magic Trackpad, sends the Apple multitouch feature report when possible, parses raw multitouch packets, and injects Windows mouse, wheel, and shortcut gestures.

A native Precision Touchpad-quality solution needs a signed Windows driver stack:

1. HID lower-filter or virtual HID miniport
   - Claim or observe the Apple HID collection.
   - Send the Apple feature report reliably after Bluetooth reconnect.
   - Normalize Apple reports into a Windows-compatible multitouch descriptor.

2. Virtual Precision Touchpad presentation
   - Expose a HID descriptor that Windows treats as a touchpad.
   - Preserve contact IDs, contact confidence, button pad state, pressure, and contact count.
   - Let Windows own native gesture policy where possible.

3. Installer and signing
   - Build with the Windows Driver Kit.
   - Sign test builds locally for development.
   - Submit release builds through Microsoft driver attestation or HLK certification.

4. Safety and recovery
   - Do not replace keyboard or pointer class drivers directly.
   - Include a command-line uninstall path.
   - Keep Bluetooth reconnect handling resilient.

This repo keeps the parser, product IDs, feature-report selection, and gesture policy separate so a kernel or virtual-HID backend can reuse the same behavioral model later.


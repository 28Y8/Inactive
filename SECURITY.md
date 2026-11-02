# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through
[GitHub private vulnerability reporting](https://github.com/28Y8/Inactive/security/advisories/new), not
in a public issue. Include the Inactive version, your Windows version and steps to reproduce it.

This is a volunteer project, so there is no guaranteed response time, but reports are taken seriously
and credit is given unless you would rather stay anonymous.

Only the latest release receives fixes.

## Threat model

**Inactive is a privacy deterrent, not a security boundary.** For real protection, press Win + L.

### What Inactive is designed to stop

- Accidental input from a pet, a child, a cleaner or a bumped desk.
- A casual passer-by who tries the mouse and keyboard on a PC left unattended.
- Input sent by remote control tools while the PC is locked, apart from the unlock key.

### What Inactive does not stop

- **A person who knows Ctrl+Alt+Del.** Windows reserves this sequence. It can lead to signing out and,
  in some setups, such as when Task Manager runs as administrator, to closing Inactive in Task Manager.
  It cannot be blocked and Inactive does not try. This is also the guaranteed way out for the owner.
- **Anyone who knows or can see the unlock key.** It is one key, and without a PIN that key is the
  whole lock.
- **Anyone who can sign in to Windows as you.** Signing back in ends the lock, even with a PIN set. On
  an account with no password, Win + L and then Sign in is enough.
- **Physical access.** Restarting the PC, booting another system or removing the disk are all out of
  scope. Inactive does not encrypt anything.
- **Reading the screen.** The Visible and Dimmed styles leave content on display, by design.
- **Software on the PC.** Malware, other apps running under your account and anything running as
  administrator can end Inactive, read its files or act around it.
- **Input to elevated windows.** Low level hooks do not see input while an elevated window has focus,
  so that input is not blocked and the unlock key may not work. Inactive tries to move focus to its
  overlay. This is best effort and can fail. If focus stays there, the overlay asks the owner to press
  Ctrl+Alt+Del, choose Lock and sign in, which still ends the lock.
- **System touch gestures.** Touch is absorbed by the overlay, not by the hooks, and gestures handled
  by Windows itself may still work.
- **Precision touchpad gestures.** Windows may handle these itself, so they may show window thumbnails
  or switch desktops, even in the Covered style.
- **Apps that read raw input.** The hooks may not stop this, so moving the mouse can still turn the
  camera in some games, and some may see key presses too.

### The PIN

- A PIN only guards the lock. It adds nothing against the attacks listed above.
- Inactive stores a PBKDF2-SHA256 hash with a 16 byte salt and 200,000 iterations in
  `%APPDATA%\Inactive\settings.json`, compared in constant time.
- That file is readable by your Windows account. A PIN has 4 to 8 digits, so anyone who gets a copy of
  the file can find the PIN by trying every combination offline. Do not reuse a PIN that protects
  something else.
- Anyone who can edit that file can remove the PIN.
- Online guessing is slowed by a cooldown: 30 seconds after 5 wrong tries, doubling up to 5 minutes.
  Restarting the lock, for example by ending the process, resets it.

### Handling of input

- Keyboard and mouse hooks exist only during a lock. Inactive observes no input while unlocked.
- While locked, events are inspected in memory and dropped. No keystroke is logged or stored. The
  exceptions are releases: a key held since before the lock must be released normally or it would stay
  stuck, and so must a mouse button. They show only that a key held earlier was let go. The release of
  a key pressed during the lock, such as a PIN digit, is dropped like the press.
- PIN digits are never logged. While locked, they are held in fixed buffers that are cleared after
  use, and only a count of digits reaches the lock screen. The dialog that sets, changes or removes the
  PIN uses normal .NET strings, which cannot be cleared.
- Logs in `%APPDATA%\Inactive\logs` hold lock events and errors, never key values or PINs.
- Inactive has no telemetry.

### Updates

Inactive downloads updates over HTTPS from the GitHub Releases of this repository. With automatic
updates on, a new version downloads by itself and installs the next time you exit or start Inactive,
without asking first. It never installs during a lock.

The updater compares each download with a checksum from the same release. That catches a damaged
download, not a tampered release, and the updater checks no publisher signature. So trust in an update
rests on the GitHub account and workflow that publish it, and anyone who takes over either one can
reach every copy with automatic updates on. Releases are not code signed yet, and signing them would
not change this, because the updater would not check the signature.

Turning off automatic updates leaves update checks to you. An update that has already downloaded still
installs.

## What counts as a vulnerability

Please report anything like the following:

- A way to get trapped: a state where the unlock key, Ctrl+Alt+Del or a crash does not free the PC.
- A key, PIN digit or PIN hash reaching a log or any other place it should not be.
- Input reaching the foreground app while locked, in a way that is not listed above.
- A problem in the update or release process.

A bypass through Ctrl+Alt+Del, Win + L, a Windows sign-in, an elevated window or physical access is a
documented limit and not a vulnerability.

# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- If an administrator window keeps focus, so that the unlock key may not work, the lock screen shows
  the Ctrl + Alt + Del route.
- A warning before locking a device in tablet mode, because unlocking needs a physical keyboard.
- A notice on the home page when your settings could not be read.
- Screen readers now announce notices, recorded keys, PIN errors and PIN status.

### Changed

- The first lock dialog explains the Ctrl + Alt + Del route step by step and says that a physical
  keyboard is needed.
- Keys pressed during a lock, PIN digits included, no longer reach other apps when they are released.
- The Settings page shows the lock hotkey in its description, so that long hotkeys fit.
- Shift alone can no longer be paired with a key that types a character in a new lock hotkey.
- The honest limits now cover Windows sign-in, raw input, touchpad gestures and cancelled restarts.
- The Automatic updates setting now says that updates install on exit or at the next launch.
- The Let the display turn off setting now gives advice for long locks on OLED screens.
- The docs no longer promise that Task Manager can close Inactive during a lock.
- SECURITY.md now spells out what the updater checks and how PIN digits are handled in the PIN dialog.
- The README now lists the updater's own log, which stays behind after you uninstall.

### Fixed

- A second launch could apply a waiting update and end a running lock with no key or PIN.
- The Exit command in the tray menu could end a lock with no key or PIN.
- Signing back in to Windows now ends the lock even if the Inactive window has hung.
- A hung Inactive no longer leaves the screen covered after unlocking.
- A lock that loses its hooks and cannot get them back now ends instead of staying half on.
- A crash no longer leaves the cursor pinned, and a slow cursor guard can no longer pin it after an
  unlock.
- A held last PIN digit or a following Enter no longer lands in another app while a key is still held.
- A reading error at startup can no longer overwrite your settings and PIN with defaults.
- A hotkey on a key with no name now survives a restart.
- Keys are named as your keyboard layout prints them, including media keys.
- A failed lock now shows its message even when Settings was open.
- A lock failure notice clears once the unlock key or PIN changes.
- The screen stays covered while the overlays are rebuilt after a display change.
- The Start with Windows toggle shows what Windows will really do, and a development build no longer
  takes over the entry of an installed copy.
- A failed download no longer leaves a Restart banner that does nothing.
- Settings no longer says you are up to date while an update is ready.
- A hotkey press just after choosing Restart or Exit can no longer start a lock that the exit ends.
- Left clicking the tray icon restores a minimized window instead of hiding it.
- A failed publish no longer blocks the next release run.
- A null message in a hand-edited settings file no longer ends Inactive when you lock.
- Inactive now opens its window on a start with Windows if your settings could not be read, so that
  the warning is not missed.
- A release with signing turned on now fails if the installer is not signed.

## [1.0.0] - 2026-11-01

First public release.

### Added

- Lock from the main window, the tray menu or a global hotkey (Ctrl + Alt + L by default). The PC
  stays awake and everything keeps running while mouse, keyboard and touch input do nothing.
- Unlock with a single key (Right Shift by default), which you can change.
- Optional PIN of 4 to 8 digits, with a growing cooldown after 5 wrong tries.
- Three screen styles (Visible, Dimmed and Covered), with an optional clock and custom message.
- Automatic release when you sign back in to Windows or when Windows shuts down.
- Confirmation before the first lock, showing the unlock key.
- Tray icon with lock state, single instance behaviour and optional start with Windows.
- Automatic updates from GitHub Releases, never applied during a lock.
- Per-user installer that needs no administrator rights.

[Unreleased]: https://github.com/28Y8/Inactive/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/28Y8/Inactive/releases/tag/v1.0.0

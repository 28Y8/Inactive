# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.1] - 2026-11-02

### Changed

- The file version in Windows Properties no longer includes a commit hash.

### Fixed

- The changelog now lists everything that shipped in 1.0.0.

## [1.0.0] - 2026-11-02

First public release.

### Added

- Lock from the main window, the tray menu or a global hotkey (Ctrl + Alt + L by default). The PC
  stays awake and everything keeps running while mouse, keyboard and touch input do nothing.
- Unlock with a single key (Right Shift by default), which you can change.
- Optional PIN of 4 to 8 digits, with a growing cooldown after 5 wrong tries.
- Three screen styles (Visible, Dimmed and Covered), with an optional clock and custom message.
- Automatic release when you sign back in to Windows or when Windows shuts down.
- Confirmation before the first lock, showing the unlock key and the Ctrl + Alt + Del route.
- A warning before locking a device in tablet mode, because unlocking needs a physical keyboard.
- A line on the lock screen with the Ctrl + Alt + Del route if an administrator window keeps focus,
  so that the unlock key may not work.
- A notice on the home page when your settings could not be read.
- Screen reader announcements for notices, recorded keys, PIN errors and PIN status.
- Tray icon with lock state, single instance behaviour and optional start with Windows.
- Automatic updates from GitHub Releases, never applied during a lock.
- Per-user installer that needs no administrator rights.

[Unreleased]: https://github.com/28Y8/Inactive/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/28Y8/Inactive/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/28Y8/Inactive/releases/tag/v1.0.0

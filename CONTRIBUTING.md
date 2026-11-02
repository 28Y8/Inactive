# Contributing to Inactive

Thanks for helping out. Bug reports, fixes and clear ideas are all welcome. For a change that is
large or alters how the lock behaves, please open an issue first so that we can agree on the approach.

Found a security problem? Do not open a public issue. Follow [SECURITY.md](SECURITY.md).

## Getting started

You need Windows and the .NET 10 SDK. `global.json` sets the minimum SDK version.

```powershell
dotnet build Inactive.slnx
dotnet test --solution Inactive.slnx
```

Use `--solution` as shown. The positional form fails because the repository uses
Microsoft.Testing.Platform.

Warnings are errors, so a change has to build cleanly in Debug and Release.

## Project layout

| Path | Contents |
| --- | --- |
| `src/Inactive.Core` | The lock engine, settings, PIN hashing and logging. No WPF. |
| `src/Inactive` | The WPF app: windows, overlay, tray, updates and startup. |
| `tests/Inactive.Tests` | Unit tests for everything that can run without real input hooks. |
| `build` | Scripts for the installer and the icons. |
| `docs/ARCHITECTURE.md` | Threads, failure modes and why each guard exists. Read it before you touch the engine. |

## Rules for the lock engine

The lock takes over a person's PC, so a bug here can trap them. These rules are not negotiable:

- **Fail open.** If something goes wrong, the PC must end up free. Never add a code path that keeps
  input blocked without a working way out.
- **Keep the hook path fast and allocation free.** Windows silently removes a hook that takes too
  long. Nothing that runs inside a hook callback may block, allocate, log or wait for the UI.
- **The UI never decides the unlock.** The hook thread decides and releases by itself, so that the
  unlock key still works when the UI hangs.
- **Do not fight Windows.** Ctrl+Alt+Del and the secure desktop stay out of reach by design. Please do
  not send workarounds.

## Testing

- Automated tests must never call `SetWindowsHookEx` or engage a real lock. Tests run on developer
  PCs and CI machines. Put new behaviour behind the existing seams, such as `ILockPlatform`, and test
  it with the fakes in `tests/Inactive.Tests/TestSupport`.
- Inject time through the clock function instead of sleeping, which keeps tests deterministic.
- Add a test with every behaviour change to `LockStateMachine` or `InputFilter`.

Debug builds can run a short real lock that ends by itself, for testing the hook path by hand:

```powershell
.\src\Inactive\bin\Debug\net10.0-windows\Inactive.exe --demo-lock 10 --demo-log demo.txt
```

Your own mouse and keyboard stop working for those 10 seconds. If anything goes wrong, Ctrl+Alt+Del
still works. This code path does not exist in Release builds.

## Privacy rules

- No telemetry of any kind.
- Never log or store a keystroke.
- Never write a PIN or key value to a log, not even in a debug message.

## Writing style

The same rules apply to all text: UI copy, comments, documentation and commit messages.

- Do not use the em dash character. Use a colon, a comma, parentheses or a new sentence.
- Do not put a comma before the final "and" or "or" in a list: "a, b and c".
- UI copy is short, calm and plain, in sentence case. Buttons name the action.
- Comments explain why. Add one only where the reason is not obvious.

## Pull requests

- Keep each pull request to one change.
- Run the build and tests in Debug and Release before you open it.
- Update `CHANGELOG.md` under `[Unreleased]` for anything a user would notice.
- Update `README.md` or `docs/ARCHITECTURE.md` if behaviour or limits change.

## Regenerating the icons

The icons are generated from code. Edit `build/generate-icons.ps1`, not the output files, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\generate-icons.ps1
```

Check the result at 16 px on both a light and a dark taskbar.

## Releasing

Maintainers bump `<Version>` in `Directory.Build.props`, move the `[Unreleased]` notes in
`CHANGELOG.md` to a new version and push a tag such as `v1.2.3` on a commit on `main`. The release
workflow does the rest: the `build` job tests and packs with a read-only token, then the `publish` job
uploads the result. If publishing fails part way, re-run the failed job: it removes the leftover draft
release first and uses the packages that are already built. Those last 7 days.

### If a release is bad

1. Delete the release on GitHub, so that no more copies download it.
2. Publish a fix with a higher version. Copies that already downloaded the bad release still install
   it, and only a higher version replaces it.

### Names that must not change

Installed copies look for updates at `github.com/28Y8/Inactive` and trust what they find there. If the
account or repository name ever became free, someone else could register it and publish to those
copies. If a move is ever needed, first ship an update with the new `RepositoryUrl`, then keep the old
name registered with a placeholder repository.

### Repository settings

These live on GitHub and not in this repository, so check them whenever the repository is set up
again.

- Security: private vulnerability reporting (SECURITY.md depends on it), secret scanning, push
  protection and Dependabot alerts.
- Actions: read-only default workflow permissions, no Actions approval of pull requests, approval
  required for all outside contributors, only actions created by GitHub and, if the account offers it,
  actions pinned to a full commit SHA.
- Rulesets on `main` and on `refs/tags/v*`, plus immutable releases.
- An environment named `release`, limited to `v*` tags and with a maintainer as required reviewer.
- No auto-merge for the Velopack group or for actions.
- The maintainer account: a passkey or hardware key for 2FA, no long-lived classic tokens and no
  `workflow` scope on a local `gh` login that does not need it. A token with `repo` scope on an admin
  account can change everything above, and whoever holds it can publish to every installed copy.

By contributing, you agree that your work is licensed under the [MIT License](LICENSE).

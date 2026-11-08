# Contributing to NOXEL

Thanks for helping. Bug reports, hardware test results and focused pull requests are all welcome.

## Reporting problems

Open an issue with:

* NOXEL version (Settings › About), Windows version and build, GPU model and driver version;
* monitor(s), resolution and refresh rate involved;
* what you did, what you expected and what happened;
* optionally the diagnostic log (Settings › Diagnostics › Write a diagnostic log, then Open log folder). Check it
  before attaching; it holds display and color operations, not personal data.

Results from AMD, Intel, laptop and multi-GPU systems are especially useful: see the checklist in
[docs/GPU-COMPATIBILITY-AUDIT.md](docs/GPU-COMPATIBILITY-AUDIT.md).

Security issues: please follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Pull requests

1. Discuss larger changes in an issue first.
2. Keep each pull request to one change. Match the existing style; new C# files start with
   `// SPDX-License-Identifier: GPL-3.0-only`.
3. `dotnet build` must succeed without new warnings and `dotnet test tests/NOXEL.Tests` must pass.
4. Anything that changes display modes must keep the Keep / Revert confirmation and the crash-recovery journal.
   Don't add code that reads or injects into game processes, installs drivers or hooks, or captures the screen.
5. Don't add dependencies without discussing them. New dependencies must be GPL-3.0-compatible and listed in
   `scripts/make_notices.ps1` / `THIRD-PARTY-NOTICES.txt`.
6. Describe how you tested, including hardware.

## License of contributions

NOXEL is licensed under GPL-3.0-only. By submitting a contribution you agree that it is licensed under the same
terms and that you have the right to submit it.

# Security policy

## Supported versions

Only the latest release on [NOXEL-Releases](https://github.com/reprinted/NOXEL-Releases/releases/latest) receives
fixes.

## Reporting a vulnerability

Please report privately through GitHub: **Security › Report a vulnerability** on
[github.com/reprinted/NOXEL](https://github.com/reprinted/NOXEL/security/advisories/new). Don't open a public
issue for security problems.

Include the NOXEL version, Windows version, steps to reproduce and the impact you see. You should get a reply
within a reasonable time; please allow time for a fix before disclosing publicly.

## Scope

Especially relevant:

* the updater (release lookup, download, SHA-256 and version verification, installer and portable handoff);
* anything that could make NOXEL run a file it didn't verify, or write outside its own folders;
* settings, preset and look-code import (untrusted JSON / text);
* display changes that could leave a monitor in an unusable mode without the automatic revert.

## How updates are protected

NOXEL downloads updates only from `github.com/reprinted/NOXEL-Releases` over HTTPS and installs a file only if its
size, SHA-256 checksum (GitHub's asset digest and `SHA256SUMS.txt`) and embedded version match the release.
Releases are not code-signed yet, so the checksum proves integrity, not publisher identity. See
[docs/KNOWN-LIMITATIONS.md](docs/KNOWN-LIMITATIONS.md#updates).

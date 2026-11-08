# How to Publish a NOXEL Update

NOXEL's source code stays private. Only the finished files go to the public repository
[reprinted/NOXEL-Releases](https://github.com/reprinted/NOXEL-Releases). That repository also has its own checklist
in `docs/RELEASING.md`; this guide is the source-side half.

Nothing here runs automatically. You publish every release by hand.

## What installed copies look for

Every copy of NOXEL asks `https://api.github.com/repos/reprinted/NOXEL-Releases/releases/latest` shortly after it
starts and then once a day. It offers an update when **all** of these are true:

* the release is published (not a draft) and not marked as a pre-release;
* its tag is a version newer than the running one: `v1.0.1` is newer than `v1.0.0`, `v1.1.0` is newer than
  `v1.0.9`;
* it has the file it needs: `NOXEL-Setup.exe` for installed copies, `NOXEL.exe` for portable ones;
* that file has a SHA-256 checksum NOXEL can check. GitHub adds one to every uploaded file (the asset's `digest`),
  and `SHA256SUMS.txt` from the release script is a second copy. If the two disagree, NOXEL refuses the update;
* the downloaded program reports exactly the version in the tag.

Files must be named exactly `NOXEL.exe`, `NOXEL-Setup.exe` and `SHA256SUMS.txt`.

## Publishing v1.0.1 (example)

1. **Set the version.** In `Directory.Build.props` change `<Version>1.0.0</Version>` to `<Version>1.0.1</Version>`.
   This is the only place the version lives. The program, the About page, the installer and the update check all
   read it.
2. **Write the release notes.** Add a `## 1.0.1` section at the top of `docs/RELEASE-NOTES.md`.
3. **Commit** the changes.
4. **Build the release.** From the repository root:
   ```powershell
   ./scripts/release.ps1 -Version 1.0.1
   ```
   The script checks the version, runs the tests, builds `NOXEL.exe` and `NOXEL-Setup.exe`, checks that both report
   1.0.1, and writes `SHA256SUMS.txt` and `RELEASE-NOTES-v1.0.1.md` into `artifacts\release-v1.0.1\`. It uploads
   nothing.
5. **Test the files in `artifacts\release-v1.0.1\`** on your PC: install over 1.0.0, run the portable exe, check the
   things you changed.
6. **Tag the source:** `git tag v1.0.1` (keep it in the private repository; don't push source to NOXEL-Releases).
7. **Create the release** at https://github.com/reprinted/NOXEL-Releases/releases/new:
   * Tag: `v1.0.1` (create it on publish). Title: `NOXEL v1.0.1`.
   * Description: paste `RELEASE-NOTES-v1.0.1.md`.
   * Attach `NOXEL.exe`, `NOXEL-Setup.exe` and `SHA256SUMS.txt`. `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` are
     optional extras; both are already inside the installer and the program.
   * Leave **Set as a pre-release** unticked and **Set as the latest release** ticked. Click **Publish release**.

   Or, with the GitHub CLI signed in on your own PC (the command is printed by the release script):
   ```powershell
   gh release create v1.0.1 artifacts\release-v1.0.1\NOXEL.exe artifacts\release-v1.0.1\NOXEL-Setup.exe artifacts\release-v1.0.1\SHA256SUMS.txt --repo reprinted/NOXEL-Releases --title "NOXEL v1.0.1" --notes-file artifacts\release-v1.0.1\RELEASE-NOTES-v1.0.1.md
   ```
8. **Check it:** open https://api.github.com/repos/reprinted/NOXEL-Releases/releases/latest. It should show
   `"tag_name": "v1.0.1"`, and each file should have a `"digest": "sha256:..."` value matching `SHA256SUMS.txt`.
9. **Check detection:** on a PC with 1.0.0, open Settings › Updates › **Check for updates**. It should say
   **NOXEL v1.0.1 available**.

## If something went wrong

* **Wrong file uploaded:** delete the file from the release (or the whole release) right away and publish again
  with the correct files. NOXEL rejects files whose version or checksum doesn't match, so a mistake shows up as
  "Update not installed", not as a broken install.
* **Pulling a release:** delete it, or edit it and tick "Set as a pre-release". NOXEL ignores pre-releases and
  drafts, so it will fall back to the newest remaining stable release.
* **Never reuse a version number.** If 1.0.1 was public even briefly, fix it in 1.0.2.

## Trust and signing

A checksum published next to the file proves the download is complete and matches what's on the release page. It
does not prove who uploaded it; anyone who controls the GitHub account could publish a matching pair. What protects
users today is the GitHub account itself, so keep two-factor authentication on and publish only from your own PC.
Never put tokens or passwords in the NOXEL source or scripts.

When you get a code-signing certificate, add a signing step to `release.ps1` after the build and before the
checksums are written (signing changes the files, so the checksums must come after it), then set `UpdateService.RequiredSigner` to the certificate's subject name. From then
on NOXEL only installs updates with a valid signature from that publisher. Keep the certificate and its password
outside the repository.

## Optional: GitHub Actions

Building on GitHub would need the private source on GitHub runners and a token that can write to
NOXEL-Releases. It is not set up and nothing publishes automatically. Don't add a workflow that publishes without
deciding that on purpose.

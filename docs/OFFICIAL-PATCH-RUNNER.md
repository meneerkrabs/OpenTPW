# Private official patch job

`official-patch.yml` is manually dispatched on an ephemeral Windows 2022 runner.
It applies the original patch engine to a disposable installation copy. It never
starts the game. The source installation and original executable must come from
the owner's legal copy; neither game bytes nor patch bytes belong in this repo.

Required repository secrets:

- `DOWNLOAD_URL`: HTTPS endpoint returning the input ZIP without redirects.
- `DOWNLOAD_TOKEN`: bearer token for that endpoint.
- `DOWNLOAD_SHA256`: SHA256 of the complete input ZIP.
- `PATCH_RESULT_KEY`: random passphrase of at least 32 characters, retained locally.

ZIP layout:

```
installation/                 complete original installation, including Data/
original-binary/tp.exe        pristine original executable; replaces installation/tp.exe
patch.exe                    TPPatchTwoEUROAMER20000324a.exe
patch-files/patchw32.dll      original DLL extracted from _user1.cab
patch-files/PATCH.RTP
patch-files/EuroAmer/PATCH.RTP
patch-files/2057/PATCH.RTP    English (default); or 1033/PATCH.RTP for American
```

The executable, DLL and selected patch files are pinned by SHA256. They are
extracted locally with 7-Zip (self-extracting archive) and unshield (`_user1.cab`),
then transferred inside the authenticated ZIP. Extraction tools are not new
repository dependencies and are not installed on the runner.

The helper is compiled with the runner's built-in .NET Framework compiler as
x86. The bundled DLL exports `RTPatchApply32@12`; the original `isrtp32.dll`
wrapper calls it with command line, stdcall callback, and wait=1. Its callback
jump table identifies ordinary log/progress/end events and interactive requests.
The helper suppresses native text, rejects warning/error events and aborts
requests requiring user input. The language/common/EuroAmer sequence follows
the original `setup.ins`. This bypasses the installer registry lookup only; the
original engine performs the actual patching and validates old file contents.

InstallShield's silent route requires a recorded, installer-specific response
file; `/s` alone is insufficient. No such file has been validated here. Official
references: [silent InstallScript installations](https://docs.revenera.com/installshield/helplibrary/InstallShieldSilent.htm),
[legacy command-line options](https://community.revenera.com/s/article/what-command-line-parameters-are-available-for-setup-exe),
and [RTPatch vendor overview](https://www.pocketsoft.com/rpatch_technology.pdf).

Success requires every native stage to return zero without diagnostic/prompt
callbacks, changed file hashes, and four expected added files. This establishes
patch application only; it does not prove gameplay or compatibility. A mismatch
fails the job and only metadata is uploaded. The runner does not fix timestamps,
ignore missing files, or publish a partial result.

The one-day `official-patch-result` artifact contains `metadata.json` (hashes,
file names, stage status) and, only on success, `patched.zip.enc`. AES-256-CBC uses
a random salt and PBKDF2-SHA256 (200,000 iterations). A decrypt-and-hash round trip
must pass before publication. The ciphertext SHA256 is recorded in metadata;
verify it before decrypting. CBC does not independently authenticate ciphertext.

Decrypt locally with the same secret in `PATCH_RESULT_KEY`:

```
openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -md sha256 \
  -pass env:PATCH_RESULT_KEY -in patched.zip.enc -out patched.zip
```

No credentials, URL, raw assets, native diagnostic strings, or proprietary bytes
are uploaded in plaintext. Credentials are removed from the patch helper's child
environment. Input/output plaintext is removed after the script; GitHub destroys
the ephemeral runner after the job. Disable the download endpoint and remove
transfer secrets after retrieving the result.

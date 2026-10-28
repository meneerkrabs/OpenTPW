# Path oracle final-assembly audit

This package-free .NET 11 console tool examines the **actual final published**
`wwwroot/_framework/**/*.dll` as PE/ECMA-335 metadata. It does not load or execute
assemblies. Its one output is a JSON report; success is exit code 0 **and**
`passed: true`. Argument errors use exit code 2; audit/report failures use 1.
An exception or partial scan cannot produce a passing report.

```sh
dotnet run --project tools/PathOracleAudit -c Release -- \
  --web-root "$PUBLISH/wwwroot" \
  --repository-root "$PWD" \
  --allowlist tools/web-path-render/artifact-allowlist.json \
  --output "$REPORTS/assembly-resources.json"
```

Build/run this only as part of a separately authorized diagnostic build. The
isolated web publish must use `WasmEnableWebcil=false`, so the inspected PE files
are the files to be staged, not an ambiguous pre-trim/pre-WebCIL copy from `obj`.
Native runtime WebAssembly remains WebAssembly; this scanner does not inspect
native runtime binaries. Do not upload the audit tool's own build output.

## Allowlist contract

The shared JSON allowlist can have other top-level properties for the staging
checker. It must have exactly one `shaders` property: an array of exactly four
objects, each containing only `path`, `logicalName`, and lowercase `sha256`.
The source paths and logical names must match exactly:

- `content/shaders/blit.shader`
- `content/shaders/web/blit.json`
- `content/shaders/test.shader`
- `content/shaders/web/test.json`

Each repository source must match its pinned hash. Resource payload bytes in
the final Web assembly must match that same hash and length. This list cannot
be broadened just by editing the JSON allowlist. The source files are text
shaders; the diagnostic generates its textures and map in code.

## Checks and output

- Require precisely one each of metadata assembly names `OpenTPW.Web`,
  `OpenTPW.Web.Veldrid`, and `OpenTPW.Online`, with neutral culture. Filename
  fingerprinting is fine; assembly identity comes from metadata.
- Reject every other assembly whose name starts with `OpenTPW`, ignoring case.
  Reject duplicate assembly name/culture identities, including duplicate app DLLs.
  Reject MSTest/TestPlatform/Microsoft.Testing, xUnit, NUnit, testhost, and
  assemblies ending in `.Test`/`.Tests` or containing `.Tests.`.
- Web must have exactly the four pinned resources. Online and Veldrid must
  have zero manifest resources.
- Inspect **all** final managed DLLs, not only the project assemblies. Reject
  malformed/nonmanaged DLLs, linked/external resources or assembly files/modules, duplicate resource
  names, invalid embedded-resource bounds, and symbolic links/reparse points.
  Dependency embedded resources are recorded, not claimed to be shader-only;
  dependency provenance and runtime/static file allowlists are separate checks.
- Record each DLL's web-root-relative path, whole-file SHA-256, byte length,
  `assemblyName`/version/culture, and each resource's name, flags, offset,
  implementation kind, payload length, and SHA-256. Resource bytes are never
  included in the report.
- Require at least one final DLL and all three expected app assemblies.
  Missing directories, oversized input, malformed allowlist, and source hash
  mismatch fail closed. The report must be outside published `wwwroot` and
  must not overwrite the source allowlist or pinned shaders.

The caller must stop on nonzero exit and stage only the exact validated bytes,
checking this report's file hashes again before upload. Do not mutate build
outputs between this audit and staging. A standalone audit report is **not**
authorization to upload the normal web publish, other `bin`/`obj` files,
original media, save corpora, extension sprites, or browser captures.

## Verification status and required build checks

This is an **uncompiled source-only candidate**: no SDK/compiler is available
in the preparation environment. No C# execution, final artifact validation,
or browser/GPU verification has been performed. Static review is not a passing
build or a media-free artifact attestation.

Before enabling artifact upload, compile this tool with the pinned SDK and
exercise disposable **synthetic-only** fixture assemblies:

1. Positive: three exact app assembly names, four pinned Web resource payloads,
   no resources in the other two; verify report hashes against actual files.
   Renaming a DLL filename while retaining its expected metadata name must pass.
2. Fail: absent, duplicated, differently named, or unexpected test app DLL.
3. Fail: extra/missing/changed Web resource, source hash mismatch, resources in
   Online/Veldrid, and linked resources in either app or dependency assembly.
4. Fail: malformed/truncated PE, empty `_framework`, invalid resource offset or
   payload length, nonmanaged `.dll`, and symlinked file/directory/ancestor.
5. Fail: duplicate/unknown shader entries, duplicate JSON keys, wrong-case or
   nonhex SHA-256, traversal path, report inside publish root, unreadable input.
6. Confirm the actual isolated release publish passes, then mutate one staged
   DLL and prove the staging hash comparison refuses to upload it.

The separate runtime/static allowlist and evaluated MSBuild item audit must
also pass. This tool verifies manifest resources, not arbitrary string literals,
native binaries, or the visual correctness of path rendering.

## Mandatory generated-fixture qualification

The optional workflow runs `selftest.py` with the compiled inspector before auditing the
real published diagnostic. It builds only tiny BCL-only assemblies and embeds the four
public textual shaders or deliberately wrong synthetic text. Nine cases cover exact
resources, missing/duplicate identity, malformed DLL, symlink, extra/wrong resource,
extra project and test framework. It never executes those fixture assemblies or starts a
browser. The SDK-dependent self-test has not run in source-only preparation.

# Bounded original save-container repair

Scope: SaveReader.cs, focused save-container tests, read-only CLI diagnostics and
evidence docs. Preserve prior unrelated edits. No new dependencies or original
payload semantic importer; no online support or copy-protection work.

1. Lock the existing valid version-133 offline synthetic decoding, repeated reads
   and FileToString behavior with tests before changing the reader.
2. Replace StreamReader/one-shot reads and unbounded inflation with bounded binary
   intake, exact header validation and checked zlib completion/checksum/size.
   Keep caller-owned streams open; implement IDisposable explicitly.
3. Accept the observed TPWI magic 0x190 alongside the legacy 0x1f4 layout, without
   claiming either magic universally defines a playable park. In the local TPWI,
   the BILZ size includes its 28-byte header and its first size is decoded length.
4. Add synthetic truncation/limits/checksum/version/online/trailing-byte tests and
   an optional hashed local TPWI fixture test. No proprietary fixture committed.
5. Expose read-only inspection, report payload identity, and mark payload semantics
   and real TPWS compatibility unverified. Run focused/full tests and scoped format,
   build and diff checks. Do not promote full gameplay or cross-platform runtime.

Review limitation: explore subagent failed on unsupported account model. Continue
locally; no independent review signoff is claimed.

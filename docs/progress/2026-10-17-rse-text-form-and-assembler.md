## RSE ride scripts get a text form, a disassembler and an assembler (October 17, 2026)

Phase 0 of the content pipeline: original ride scripts can now be written as text and compiled to `.RSE`. The game ships no readable script source, so the syntax is OpenTPW's own (header directives, `.var` names, `label:` / `@label`, quoted strings, one instruction per line; docs/RSE-SCRIPTS.md, "Text form and assembler").

- **Shared opcode table**: the `Opcode` enum moved from `OpenTPW` to `OpenTPW.Files` (same namespace), so `RideScriptOpcodeNames` and the VM use one table; a test checks that the enum, the name table and every VM handler agree.
- **`RideScriptDisassembler` / `RideScriptAssembler`**: lossless text for any file `RideScriptFile` accepts, including an explicit `.string` table for blobs that are not in first-reference order. Errors carry line and column.
- **Proof**: all 308 corpus members disassemble and reassemble to the original bytes (string blob, variable table, header, padding), 100 %. Authoring tests run a hand-written `ADDHEAD`/`WAIT`/`DELHEAD` loop in `RideVM` without game data.
- **Unverified**: the defaults of a fresh header besides the time slice of 50, and that an authored script behaves like an original in the game.

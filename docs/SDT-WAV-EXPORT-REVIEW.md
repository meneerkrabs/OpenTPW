# Selected SDT WAV export review

Reviewed author source `3a72998ec156c55452dfb43415c402650527d695` against
`cc62f752f77c3721aa8c6bc07941530d26c5d98c`. No production correction was needed.
The helper exports one stable, explicitly selected 1-based bank entry through the
existing MPEG decoder and signed 16-bit NAudio WAV writer. It does not interpret
container rate/sample hints as decoded PCM properties or perform event mixing,
loop flattening, resampling or gain changes.

Fresh focused verification passed 84 tests, skipped seven original-data cases,
and had no failures. Fidelity validation reports 389 IDs with all 11 checker
tests passing. The restored Web Release build passed with zero errors and 240
existing warnings. The incremental desktop solution build passed with zero
warnings and zero errors; the initial no-restore attempt was blocked by absent local
Web restore metadata, not a source failure.

Independent CPU controls extend the author's coverage with actual CLI selection
of entry 3 past two damaged earlier offsets, complete silent PCM acceptance,
midstream format-change refusal with unchanged input/foreign temporary files,
missing CLI input refusal, and limited-reader invalid-index/zero-bound checks
that count stream opens. Existing author controls compare decoded PCM with
NAudio header/sample readback, distinguish selected nonzero audio from an earlier
silent entry, and cover CRC/unsupported/truncated data and allocation bounds.
Silent complete PCM is valid; zero complete frames remain invalid.

The late-move directory refusal assertion permits exactly `IOException` or
`UnauthorizedAccessException`, since filesystem platforms differ. It still
requires refusal and retains the directory's contents and unrelated temporary;
no arbitrary exception or platform skip is accepted. This review ran on macOS,
not Windows.

An additional macOS process-level race used a synthetic 3,000-frame MPEG-2 LSF
Layer II bank (939,000 payload bytes). After observing the actual owned WAV
`.tmp` file, the reviewer created the destination before the final move. The
real `Program.Main` CLI exited 1 with `IOException` at the non-overwriting move;
the racing destination bytes, foreign temporary and source bytes remained
unchanged, no owned temporary remained, and no profile directory was created.
This is an observed scheduling race control, not a cross-platform transaction
or crash-durability guarantee. No production test hook was added.

Desktop and Web compilation verify the single desktop-helper exclusion while
retaining the shared bank reader and decoder. No original assets, device output,
GPU/browser runtime, native audio/LIP timing or full-suite parity is claimed.
The existing complete decoded PCM buffer still limits valid export memory use;
the early bound prevents a selected compressed payload above the existing
16 MiB decoder limit from being allocated first.

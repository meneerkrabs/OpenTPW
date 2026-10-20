## Load and online park rows follow their proved sound routes (October 18, 2026)

AUDIO-041 is narrowed to its remaining paths. Load, Find Parks and Other Parks no longer add a generic UI click
on row activation. Their type-7/base/owner routes send row notification 0x400 without synthesizing button 0x100
or a dedicated row sound: Load owner `0x49e880`, Find Parks `0x4bf9f0`, Other Parks `0x4c1a10`, type-7 procedure
`0x665c35`, base `0x65f6d1`, hook `0x485780`. The immediate load/details paths are covered by that static audit;
[the audio guide](../AUDIO.md#list-owners-october-18-and-19-2026) records the bounded evidence.

The Load ownership guard and approved select-then-load UX remain intact. Its separate EXT Load Park button
keeps its own 0x1F and no longer receives a second click from the row callback. The two online lists retain
selection/activation details callbacks. Downstream loader/advisor sounds, ordinary buttons, headers and scroll
arrows retain their sound policies. The reusable list default and untraced/extension callers are unchanged.

`ListOwnerAudioTests` adds 12 test methods / 18 expanded cases with generated 0x1F/0xBD samples, exact event
sequences and real mixer voices. Coverage includes concrete Load mouse/direct/Enter activation, first-click
selection, combined/reentrant ownership, stale/invalid/empty/cancelled/refused cases, the separate Load button,
both online details owners, downstream sounds, wheel/track/arrow behavior and default/concrete extension lists.
The earlier generic-row test is relabelled as a default-policy regression, not evidence about every front-end row.

Validation here: fidelity and progress checks passed; the root Python suite passed 108 tests after generating
its required ignored fidelity-register artifact; the evidence runner completed 21 suites / 1,537 cases with 595
skips and seven declared expected failures (overall success). The first root-suite run lacked that generated artifact and reported one failure plus one error;
generating it and rerunning resolved both. The .NET build and targeted C# suites were attempted, but both exited
127 because `dotnet` is unavailable. The 18 new C# cases are unexecuted locally and require CI, together with
`LoadChooserScrollTests`, `LoadChooserOwnershipReviewTests`, `NativeWidgetTests`, `SelectClickTests`,
`AudioRegisterRound2Tests`, `AudioTriggerTests` and `ModsScreenTests`.

AUDIO-041 stays open: other original list owners/custom row classes, TPI/mod/debug UI fidelity, still-thumb
activation and general keyboard/shortcut self-0x100 routing remain separate. Host Enter tests do not establish
original keyboard parity, and no whole-program or original runtime-silence claim is made. No original media,
byte dumps, copied strings or private input metadata are included. AUDIO-040 mixer behavior is unchanged.

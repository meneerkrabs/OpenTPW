# AUDIO-041: Resolve list-row click sounds

**Superseded (October 19, 2026):** every list owner, the scroll thumb and the keyboard's 0x100 paths are traced and `APPROX:AUDIO-041` is retired (docs/AUDIO.md, "List owners"). The rest of this proposal is kept as history.

## Implemented bounded scope
This draft now contains a runtime implementation for three proved owners: Load, Find Parks and Other Parks. Their original type-7 row notifications do not synthesize button message 0x100 or a dedicated row-click sound through the traced owner/base/override paths. The complete static route and source anchors are recorded in docs/AUDIO.md.

Load retains its approved select-then-load flow and TryBeginActivation guard. Its row callback no longer adds 0x1F; the separate EXT Load Park button keeps its own single click. Find Parks and Other Parks explicitly disable the reusable list's generic activation click. Their selection/details callbacks remain unchanged.

The reusable UiScrollList default, other owners and extension callers remain unchanged. Separate buttons, arrows and headers keep receiver-style 0x1F/0xBD behavior; downstream loading/advisor/domain sounds are not muted.

## Evidence resolved
The previous missing procedure/vtable gate is resolved for these three list instances. Constructor 0x662562 installs procedure 0x665C35, dispatch 0x65F697 calls it, base 0x65F6D1 does not remap the row input to button activation, and concrete owners 0x49E880/0x4BF9F0/0x4C1A10 consume their row notifications without injecting a click. The global hook's click branch requires message 0x100.

Self-0x100 sites near these owners must not be conflated with row handling: 0x49EC53 belongs to Save, and 0x4BFC29 is search submit. Highlight callbacks are visual operations. This static analysis does not assert that all later loading or network/gameplay activity is silent.

## Tests and remaining gates
ListOwnerAudioTests supplies twelve test methods / eighteen expanded synthetic cases using distinct samples and real mixer/event assertions. It covers concrete Load and online lists, invalid/empty/reentrant/refused input, separate Load button exactly-once, preserved downstream sounds and default/extension policies. Existing Load/navigation and receiver-style button suites remain applicable.

Repository fidelity/progress/whitespace checks, 108 tooling tests and the evidence aggregate passed locally. C# execution is unavailable locally because dotnet is absent; exact-head CI must run before merge. The new implementation progress fragment records actual validation results. No runtime audio capture or rendered original comparison is claimed.

AUDIO-041 remains open for other original owners/custom row classes, still-thumb activation, and general keyboard/shortcut self-0x100 routing. Retained host Enter/selection behavior is not original keyboard-parity evidence. Do not globally silence lists or retire the whole ID based on this slice.

## Publication boundary
Independent runtime/source/test review is required. Keep this draft until full validation and review are complete. No original assets, strings, binaries, instruction dumps or private input metadata are included.

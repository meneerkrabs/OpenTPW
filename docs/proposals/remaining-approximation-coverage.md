# Remaining approximation coverage index

## Status and baseline

Plan-only draft coverage, audited 2026-10-18 against independently verified main snapshot `5ccddcab56ffb9bda4ded1821988a8492bee8943`. This index records proposals, evidence work and scope decisions, not implemented gameplay or original-fidelity completion. No approximation is retired by this index.

The audited baseline contains **162 distinct registered IDs**. **AUDIO-041**, introduced by existing implementation PR #448, adds one explicitly identified follow-on residual. The index therefore has **163 distinct rows**, with no omissions, extra baseline IDs or duplicate IDs: UI 51, simulation/economy 57, world/audio 55.

Disposition: **149 new plan-only draft PRs**, **six addenda to existing drafts**, **six existing-PR coverage pointers**, **one stale-only record**, and **one alias**. These are coverage dispositions, not 163 missing features. All new links are populated from confirmed publication responses; runtime acceptance remains pending.

## Category meanings

- **Missing behavior:** a bounded behavior absent from the inspected source; implementation still requires the stated evidence and review gates.
- **Approximation:** behavior exists but has a specified residual difference, incomplete state or unresolved lifecycle. Preserve the portions already implemented.
- **Evidence-only:** no new runtime mismatch is established; gather an oracle or adjudicate compatibility before proposing a behavioral change.
- **Out-of-scope / scope-gated:** intentional extension, unshipped case or TPI-specific compatibility question. A proposal documents the decision needed; it does not silently make this mandatory TPW parity work.
- **Stale:** source already implements the formerly described gap. Keep as audit evidence rather than opening a duplicate feature PR.

## Existing work and precise boundaries

- [#440](https://github.com/meneerkrabs/OpenTPW/pull/440): PATH-015 queue torch animation. Its proposed addendum identifies queue-model residuals; terrain deformation belongs to #446 and the `quedead` flag remains evidence-gated.
- [#441](https://github.com/meneerkrabs/OpenTPW/pull/441): TRACK-018 existing-track undo and qualifying reopen; marker work is coordinated with #445. Addendum records that boundary.
- [#442](https://github.com/meneerkrabs/OpenTPW/pull/442): ECON-058 live challenge progress messages. Existing coverage pointer; no duplicate proposal.
- [#443](https://github.com/meneerkrabs/OpenTPW/pull/443): RIDES-019 Instant Action ending. Proposed addendum separately scopes Full Simulation's cross-park research prerequisite.
- [#444](https://github.com/meneerkrabs/OpenTPW/pull/444): ADVISOR-017 map/options speech stop. Proposed addendum covers remaining queue-service and pause-clock questions, excluding the already planned stop hook.
- [#445](https://github.com/meneerkrabs/OpenTPW/pull/445): BLUEPRINT-003 per-cell feedback and marker textures. Fence ghosts are a proposed scope extension, not a claim about existing coverage. Preserve original-loader rejection of `garrow`/`rarrow`; arrow support requires new evidence. Auto-queue `m_inout` remains separately gated.
- [#446](https://github.com/meneerkrabs/OpenTPW/pull/446): RIDES-014 terrain-blend research, shared with PATH-015. Addendum preserves the evidence gate before terrain mutation.
- [#447](https://github.com/meneerkrabs/OpenTPW/pull/447): grouped SAVE-920, SAVE-921, SAVE-922 and SAVE-923 coaster import evidence/restore contract. Existing coverage pointers; retain safe refusal until validated restoration exists.
- [#448](https://github.com/meneerkrabs/OpenTPW/pull/448): AUDIO-004 activation sound routing implementation, unlike the plan-only PRs in this batch. AUDIO-041 is its distinct list-row notification/sound follow-on and must be revalidated against that PR's latest head.

**AUDIO-003:** stale-only audit record. Parameter-bound pitch and ordered delay draws already exist; reverb is AUDIO-039. No new feature PR.

**COAST-014:** evidence-only alias to COAST-032. Already-matched PC cost/rating/mode behavior is excluded; remaining sampler/knot arithmetic has one owner and one proposal, COAST-032.

## Ownership update

PRs #433 (park information/lobby UI) and #423 (languages) were verified merged. The UI owner confirms no remaining HUD ownership blocker for #442 or #444. Evidence and implementation gates remain. #444 carries this update directly; #442 is tracked here. Remaining umbrella U2–U4 work owns guest/snapshot/resume and shared simulation interfaces; coordinate before runtime edits, including U4’s rain-mood option.

Smarter-guests #434 is verified merged (selectable 1.6.0, disabled by default). Its owner retains follow-up Mac CPU-gate repeat validation, heavy-litter scenarios and unknown-variant rejection. These proposals do not duplicate that implementation work.

## Ownership and acceptance gates

1. Before any implementation, recheck current main and active PR heads. These proposals are based on the pinned snapshot and do not reserve runtime files.
2. Assign an implementation owner and agree shared interfaces before edits. Cross-ID dependencies are coordination edges, not authorization for duplicate implementations. Coordinate guest work with the active guest follow-up, particles/tour with their active owners, and UI/save consumers with their state owners.
3. Preserve implemented behavior and deliberate extensions. Resolve evidence and scope questions explicitly; annotations and asset availability are not a newly observed original-game oracle.
4. Author the proposal's concrete regression cases, run the relevant suites and compare deterministic state/RNG or visual traces where applicable. Record fixture provenance, revision, inputs and expected results. Synthetic success alone does not establish original fidelity.
5. Keep original media and private saves out of published artifacts. Obtain necessary legitimate fixtures separately and record unsupported cases honestly.
6. Perform independent review of later implementation and exact acceptance results. This index and these plan-only drafts make no readiness-to-merge, runtime-test or completed-feature claim.

## Per-ID coverage

New plan links identify their reviewed draft PRs. Existing links and alias/stale dispositions remain explicit. Category spellings below are normalized from the three reviewed domain manifests.

| ID | Domain | Category | Coverage destination | Bounded residual / decision |
| --- | --- | --- | --- | --- |
| ADVISOR-005 | world | evidence-only | [#542](https://github.com/meneerkrabs/OpenTPW/pull/542) | Map frontend states to ambient and fog settings, preserving ordinary-park lighting. |
| ADVISOR-009 | world | approximation | [#558](https://github.com/meneerkrabs/OpenTPW/pull/558) | Inventory the 96 non-stub unported score callbacks and unbound help/game posts; stage implementations by callback family. |
| ADVISOR-012 | world | evidence-only | [#559](https://github.com/meneerkrabs/OpenTPW/pull/559) | Resolve flat speech coordinate frame and channel-mode selection before changing mono duplication. |
| ADVISOR-017 | world | approximation | [#444](https://github.com/meneerkrabs/OpenTPW/pull/444) — proposed addendum | Addendum to #444: queue service per park turn, focus/modal clock pause; exclude map/options speech-stop already covered. |
| ADVISOR-025 | world | missing | [#562](https://github.com/meneerkrabs/OpenTPW/pull/562) | Add missing island-lobby key display and timer-triggered refresh; retain park behavior. |
| ARRIVAL-001 | simulation | approximation | [#499](https://github.com/meneerkrabs/OpenTPW/pull/499) | Specify a scheduler restore contract and diagnostic for absent vehicle bindings; share cell ordering with WALK-003 rather than adding a second list. |
| ARRIVAL-003 | simulation | approximation | [#500](https://github.com/meneerkrabs/OpenTPW/pull/500) | Separate safe runtime sample availability from original economic eligibility; define import dependency without pretending coaster reconstruction exists. |
| ARRIVAL-006 | simulation | out-of-scope | [#502](https://github.com/meneerkrabs/OpenTPW/pull/502) | Research whether fidelity needs an optional clock policy; preserve the fixed-60Hz extension until explicit scope decision. |
| ARRIVAL-008 | simulation | out-of-scope | [#503](https://github.com/meneerkrabs/OpenTPW/pull/503) | Prepare a TPI-only evidence matrix and scope decision; do not impose TPI weather on TPW. |
| AUDIO-003 | world | stale | Stale-only audit; no new PR | Audit-only: pitch, parameter-bound pitch and ordered delay draws are already implemented; reverb belongs to AUDIO-039. |
| AUDIO-004 | world | approximation | [#448](https://github.com/meneerkrabs/OpenTPW/pull/448) — existing coverage | Coverage pointer to implementation #448; revalidate receiver-style dispatch there and route row-message uncertainty to AUDIO-041. |
| AUDIO-018 | world | approximation | [#561](https://github.com/meneerkrabs/OpenTPW/pull/561) | Finish camera-axis swap and flat-placeholder source transforms without altering proven mono pan law. |
| AUDIO-038 | world | approximation | [#553](https://github.com/meneerkrabs/OpenTPW/pull/553) | Replace silent loop advance and sentence-next-segment assumptions only after determining stop/restart lifecycle; include already-playing chain children. |
| AUDIO-039 | world | approximation | [#556](https://github.com/meneerkrabs/OpenTPW/pull/556) | Document and evaluate substitute DSP against device/backend behavior; do not label Freeverb constants original. |
| AUDIO-040 | world | missing | [#555](https://github.com/meneerkrabs/OpenTPW/pull/555) | Add explicit SoundQuality-dependent FADEOBJ behavior: zero stops, positive quality retains voice at fixed 60 percent. |
| AUDIO-041 | world | approximation | [#596](https://github.com/meneerkrabs/OpenTPW/pull/596) | New residual from #448: distinguish row messages 0x400-0x406 from activation 0x100; remove unconditional row click only if trace proves silence. |
| AUDIO-900 | world | evidence-only | [#560](https://github.com/meneerkrabs/OpenTPW/pull/560) | Resolve which original camera-state bits select rain/snow parallax factor 0.3. |
| AUDIO-901 | world | evidence-only | [#554](https://github.com/meneerkrabs/OpenTPW/pull/554) | Establish flake aspect correction and coordinate space for depth offsets before modifying geometry. |
| AUDIO-902 | world | approximation | [#557](https://github.com/meneerkrabs/OpenTPW/pull/557) | Replace world-facing unjoined quads with traced projected segment joins once projection/winding are established. |
| AUDIO-904 | world | approximation | [#565](https://github.com/meneerkrabs/OpenTPW/pull/565) | Specify original weather near plane and clipping versus intentional scene-camera near-plane extension. |
| BLUEPRINT-001 | world | approximation | [#566](https://github.com/meneerkrabs/OpenTPW/pull/566) | Provide typed cell/object-height lookup so occupied footprint markers use original quantized raised height. |
| BLUEPRINT-003 | world | approximation | [#445](https://github.com/meneerkrabs/OpenTPW/pull/445) — proposed addendum | PROPOSED addendum extending #445: its existing scope covers per-cell feedback and marker textures only. Add evidence-backed Buy Land fence ghost work; it is not already covered by that PR. Preserve the original loader rejection of garrow/rarrow models: arrows remain unsupported and evidence-gated, not an assumed implementation target. Record auto-queue m_inout dependency separately. |
| COAST-004 | world | approximation | [#567](https://github.com/meneerkrabs/OpenTPW/pull/567) | Implement descriptor skips and cut-open sampling; evaluate one-sweep versus repeated sweeps separately from numeric precision. |
| COAST-014 | world | evidence-only | Alias: COAST-032, [#571](https://github.com/meneerkrabs/OpenTPW/pull/571) | Evidence-only alias: PC cost/rating/mode differences resolved; sampler and knot arithmetic remaining belong to COAST-032. |
| COAST-015 | world | evidence-only | [#568](https://github.com/meneerkrabs/OpenTPW/pull/568) | Classify model-free one-knot/min-max fallback separately from shipped-data fidelity; improve only if concrete original behavior with absent assets is proven. |
| COAST-026 | world | approximation | [#569](https://github.com/meneerkrabs/OpenTPW/pull/569) | Build sample-coordinate section runs and flume wet/dry blend; separately cover emissive marker flags and Add-mode UVs within renderer scope. |
| COAST-028 | world | approximation | [#570](https://github.com/meneerkrabs/OpenTPW/pull/570) | Map model-based pylon picking, pointer units, drawer transition and live rebuild semantics; keep Backspace extension explicit. |
| COAST-032 | world | approximation | [#571](https://github.com/meneerkrabs/OpenTPW/pull/571) | Port remaining knot/chord/tangent and collision arithmetic to proven 24-bit step sequence; own COAST-014 residual. |
| COAST-033 | world | approximation | [#572](https://github.com/meneerkrabs/OpenTPW/pull/572) | Add ground shadow, determine hierarchical hiding and store suspended half-turn at edit-step rebuild; table-sine interpolation separate evidence gate. |
| COAST-034 | world | approximation | [#563](https://github.com/meneerkrabs/OpenTPW/pull/563) | Start/reset wheel clips at original state/rating initialization instead of first visibility; investigate bend endpoints and seated sockets. |
| COAST-035 | world | missing | [#564](https://github.com/meneerkrabs/OpenTPW/pull/564) | Implement selected-pylon handlers that enter loop mode/counter states, preserving existing hover preview. |
| COMPAT-002 | ui | evidence-only | [#449](https://github.com/meneerkrabs/OpenTPW/pull/449) | LogFontHorizontalScale uses average-width scaling and retains fractional advances; Mac GDI width rounding is not verified. |
| COMPAT-003 | ui | approximation | [#450](https://github.com/meneerkrabs/OpenTPW/pull/450) | GdiGray4 rasterization uses 4x4 samples and a 17-level byte map without hinting; the compositor already implements the documented blur/relief arithmetic. |
| COMPAT-004 | ui | evidence-only | [#452](https://github.com/meneerkrabs/OpenTPW/pull/452) | OriginalGateSign composes decoded board and two park-name lines; board placement and relief strength have only low-resolution capture comparison. |
| COMPAT-007 | ui | missing | [#451](https://github.com/meneerkrabs/OpenTPW/pull/451) | RenamePark updates ParkName and redraws GateSign only; original online-record updates, secret-name event effects and original-save name import are not implemented here. |
| COMPAT-010 | ui | approximation | [#453](https://github.com/meneerkrabs/OpenTPW/pull/453) | Material selects preset samplers; clamped slots retain preset MINFILTER while traced PC state would use point, and option-to-state mapping remains unresolved. |
| COMPAT-018 | ui | evidence-only | [#454](https://github.com/meneerkrabs/OpenTPW/pull/454) | SignWaveletDecoder and AlphaBlit exist; uploaded GPU row orientation and independent original board pixels remain unverified. |
| COMPAT-032 | ui | approximation | [#455](https://github.com/meneerkrabs/OpenTPW/pull/455) | Sign faces use the explicit engine alpha-blend extension instead of the original texture-alpha-type state selection. |
| COMPAT-033 | ui | approximation | [#456](https://github.com/meneerkrabs/OpenTPW/pull/456) | Hangul follows generic placement using scaled hmtx advances rather than GDI integer gmCellIncX metrics. |
| COMPAT-034 | ui | missing | [#457](https://github.com/meneerkrabs/OpenTPW/pull/457) | Place iterates logical code-point order; original Uniscribe resolves bidi visual order, even though shipped fonts lack Hebrew glyphs. |
| DET-013 | simulation | approximation | [#504](https://github.com/meneerkrabs/OpenTPW/pull/504) | Define state ownership and replay checkpoints; distinguish approved explicit-seed and 60Hz extensions from missing restore state. |
| DET-014 | simulation | approximation | [#509](https://github.com/meneerkrabs/OpenTPW/pull/509) | Document existing sideshow and balloon recreation draws as excluded; specify one ordered guest RNG transcript and scheduler ownership boundary. |
| DET-015 | simulation | approximation | [#512](https://github.com/meneerkrabs/OpenTPW/pull/512) | Define constructor draw contract with thing allocation order; retain weather-stream unless separately adjudicated. |
| ECON-022 | simulation | approximation | [#516](https://github.com/meneerkrabs/OpenTPW/pull/516) | Bound staff-state parity tests and shared navigation interfaces; exclude implemented StaffSprites performances and TurnToShowOrPerformer. |
| ECON-023 | simulation | approximation | [#506](https://github.com/meneerkrabs/OpenTPW/pull/506) | Produce invariant and transition table before proposing one authoritative hosted state; explicitly preserve headless extension. |
| ECON-026 | simulation | approximation | [#507](https://github.com/meneerkrabs/OpenTPW/pull/507) | Define structural layer valuation inputs independently of cell terrain type; compare saved net worth before implementation. |
| ECON-030 | simulation | missing | [#505](https://github.com/meneerkrabs/OpenTPW/pull/505) | Plan loss panel/tool closure and evidence-led interaction policy; delegate warning producer ownership to ADVISOR-009. |
| ECON-049 | simulation | approximation | [#508](https://github.com/meneerkrabs/OpenTPW/pull/508) | Define typed entrance link requirements with cell-map owner; remove unconditional connectivity only after representation supports original test. |
| ECON-050 | simulation | approximation | [#513](https://github.com/meneerkrabs/OpenTPW/pull/513) | Specify a cell-mutation transcript for place/remove with preserved unrelated layers; no speculative destructor rewrite. |
| ECON-058 | simulation | approximation | [#442](https://github.com/meneerkrabs/OpenTPW/pull/442) — existing coverage | Use existing proposal; no duplicate file. Challenge message progress in #442. |
| ECON-059 | simulation | evidence-only | [#515](https://github.com/meneerkrabs/OpenTPW/pull/515) | Evidence-only precision study and boundary vectors; no numeric rewrite until execution context is known. |
| ECON-060 | simulation | out-of-scope | [#511](https://github.com/meneerkrabs/OpenTPW/pull/511) | TPI-only ledger/ring design and scope adjudication, without replacing TPW daily accounting. |
| ECON-061 | simulation | out-of-scope | [#514](https://github.com/meneerkrabs/OpenTPW/pull/514) | Write TPI-specific branch evidence and proposed contract; await scope decision before runtime work. |
| ECON-062 | simulation | out-of-scope | [#510](https://github.com/meneerkrabs/OpenTPW/pull/510) | Prepare TPI-only payment lifecycle proposal and adjudication, preserving free TPW rides. |
| ECON-063 | simulation | approximation | [#522](https://github.com/meneerkrabs/OpenTPW/pull/522) | Plan explicit initialization/import cache contract, keeping first day dispatch distinct from later day changes. |
| GATE-006 | ui | evidence-only | [#458](https://github.com/meneerkrabs/OpenTPW/pull/458) | M3Gate uses enumerated QueueCellWalkTurns=27 and move-up terms, not a proof for all separation/neighbor configurations; the looser bound was already accepted. |
| GUEST-002 | simulation | approximation | [#523](https://github.com/meneerkrabs/OpenTPW/pull/523) | Define scoring independent from endpoint reachability, coordinated with QUEUE-003 topology. |
| GUEST-004 | simulation | approximation | [#518](https://github.com/meneerkrabs/OpenTPW/pull/518) | Specify target lifecycle separately from queue membership; distinguish full-room and excitement refusals. |
| GUEST-005 | simulation | approximation | [#527](https://github.com/meneerkrabs/OpenTPW/pull/527) | Define access versus join versus occupancy cell contracts; coordinate with WALK-005 before changing goal cells. |
| GUEST-006 | simulation | approximation | [#528](https://github.com/meneerkrabs/OpenTPW/pull/528) | Plan remaining bubble suppression and environment transitions; explicitly exclude voice reimplementation. |
| GUEST-007 | simulation | evidence-only | [#521](https://github.com/meneerkrabs/OpenTPW/pull/521) | Evidence-led decision on retaining advice context; share mixed cell-chain model with WALK-003. |
| GUEST-010 | simulation | approximation | [#517](https://github.com/meneerkrabs/OpenTPW/pull/517) | Describe generalized link traversal once cell links are available; preserve stranded caching already implemented for guests. |
| GUEST-011 | simulation | approximation | [#519](https://github.com/meneerkrabs/OpenTPW/pull/519) | Define close-time cancellation and gate waiting transitions without mass-teleporting queues. |
| GUEST-013 | simulation | approximation | [#520](https://github.com/meneerkrabs/OpenTPW/pull/520) | Plan a shared structure query for drop and walking; isolate visit/TPI policy from baseline TPW. |
| GUEST-014 | simulation | approximation | [#525](https://github.com/meneerkrabs/OpenTPW/pull/525) | Define evidence-backed original-load variant rule separately from backward-compatible own-save migration. |
| GUEST-015 | simulation | out-of-scope | [#524](https://github.com/meneerkrabs/OpenTPW/pull/524) | Research/adjudication-only support boundary for unshipped banks; do not claim missing shipped animation. |
| GUEST-016 | simulation | approximation | [#526](https://github.com/meneerkrabs/OpenTPW/pull/526) | Plan explicit simulation displacement input and presentation interpolation contract. |
| GUEST-017 | simulation | evidence-only | [#532](https://github.com/meneerkrabs/OpenTPW/pull/532) | Write timing trace and proposed activation boundary; leave existing bob order and fade formula intact. |
| GUEST-019 | simulation | approximation | [#534](https://github.com/meneerkrabs/OpenTPW/pull/534) | Separate scoring window position, route metric endpoint and final destination fields. |
| GUEST-020 | simulation | approximation | [#535](https://github.com/meneerkrabs/OpenTPW/pull/535) | Define load-time bank policy and supported setting mapping before implementation. |
| GUEST-021 | simulation | approximation | [#539](https://github.com/meneerkrabs/OpenTPW/pull/539) | Plan nearest terrain-or-model ray hit and independently verified screen anchor offset. |
| GUEST-920 | simulation | approximation | [#538](https://github.com/meneerkrabs/OpenTPW/pull/538) | Specify integer-turn display and roster population contract; exclude implemented arrival-date restore. |
| HEADS-004 | ui | approximation | [#461](https://github.com/meneerkrabs/OpenTPW/pull/461) | HeadPresentation computes roll from world-matrix M21/M22; PC composition includes selected frame reference basis, not yet compared instruction by instruction. |
| HEADS-007 | ui | approximation | [#460](https://github.com/meneerkrabs/OpenTPW/pull/460) | BounceRiders.RunStep divides elapsed milliseconds by 100, a guessed interval for the run-template stepper. |
| HEADS-008 | ui | out-of-scope | [#459](https://github.com/meneerkrabs/OpenTPW/pull/459) | CollectBouncing derives node from current bounceNodeBase plus index; original snapshots node at insertion, but no shipped bounce script changes base mid-bounce. |
| MD2-900 | ui | approximation | [#462](https://github.com/meneerkrabs/OpenTPW/pull/462) | Frame-key writer behavior is ported; unaddressed slots reset to zero and fixed-item pose hold does not extend to texture frames without consumer evidence. |
| MD2-901 | ui | evidence-only | [#463](https://github.com/meneerkrabs/OpenTPW/pull/463) | Invalid slot/frame tracks are ignored defensively; PC writer is unchecked and its consumer is unread, so undefined behavior must not be reproduced blindly. |
| PATH-007 | world | approximation | [#576](https://github.com/meneerkrabs/OpenTPW/pull/576) | Separate safe purchase rejection from original acceptance quirks; decode six-click fence mode and 2x2 fill. |
| PATH-015 | world | approximation | [#440](https://github.com/meneerkrabs/OpenTPW/pull/440) — proposed addendum | Addendum mapping: #440 owns torch animation, #446 owns ground deformation; unresolved quedead load flag 0x100000 remains evidence-gated. |
| PATH-017 | world | evidence-only | [#575](https://github.com/meneerkrabs/OpenTPW/pull/575) | Determine full-screen lolight layer hit testing before changing tool-live speed-pause behavior. |
| PTCL-900 | world | approximation | [#577](https://github.com/meneerkrabs/OpenTPW/pull/577) | Model original cached key refresh cadence only after coordination; distinguish event-time pose from last draw-time pose. |
| PTCL-904 | world | approximation | [#579](https://github.com/meneerkrabs/OpenTPW/pull/579) | Resolve textured aspect behavior and separate world-scale extension; coordinate with current particle owner. |
| PTCL-905 | world | approximation | [#580](https://github.com/meneerkrabs/OpenTPW/pull/580) | Plan original state words, alpha thresholds, depth writes and point filtering after reconciling #425/#435. |
| PTCL-908 | world | approximation | [#581](https://github.com/meneerkrabs/OpenTPW/pull/581) | Reconcile merged beam fixes then plan reachable missing producers; undefined stack-direction cases require evidence, not invented values. |
| PTCL-909 | world | evidence-only | [#582](https://github.com/meneerkrabs/OpenTPW/pull/582) | Establish original callback/view draw ordering before moving screen particles relative to HUD/advisor. |
| QUEUE-003 | simulation | approximation | [#540](https://github.com/meneerkrabs/OpenTPW/pull/540) | Design typed entrance/back links and admission precondition shared with walking; preserve fourth-place path exception. |
| QUEUE-010 | simulation | approximation | [#537](https://github.com/meneerkrabs/OpenTPW/pull/537) | Plan entrance link support then remove fallback only for evidenced walkable original cells. |
| QUEUE-012 | simulation | approximation | [#529](https://github.com/meneerkrabs/OpenTPW/pull/529) | Define queue validation from live cell/owner state while retaining explicit pool/slide extensions. |
| QUEUE-014 | simulation | approximation | [#531](https://github.com/meneerkrabs/OpenTPW/pull/531) | Plan queue-tool gesture state and cell transaction semantics; keep preview side-effect free. |
| QUEUE-015 | simulation | approximation | [#533](https://github.com/meneerkrabs/OpenTPW/pull/533) | Specify segment accounting and occupancy reevaluation separately; retain labeled one-click/backtrack extensions. |
| QUEUE-023 | simulation | approximation | [#530](https://github.com/meneerkrabs/OpenTPW/pull/530) | Define event-source matrix and idempotent reevaluation contract before adding hooks. |
| RIDES-011 | world | missing | [#574](https://github.com/meneerkrabs/OpenTPW/pull/574) | Model type-0x15 owner markers for track-end placement and integrate coaster readers. |
| RIDES-014 | world | approximation | [#446](https://github.com/meneerkrabs/OpenTPW/pull/446) — proposed addendum | Existing #446 owns footprint ground flatten/blend; coordinate queue deformation dependency PATH-015. |
| RIDES-018 | world | approximation | [#578](https://github.com/meneerkrabs/OpenTPW/pull/578) | Add second-layer/access-rule tests and resolve guest displacement semantics before changing it. |
| RIDES-019 | world | missing | [#443](https://github.com/meneerkrabs/OpenTPW/pull/443) — proposed addendum | Addendum to #443: Full Simulation End creation needs all-online-park research ledger; exclude covered Instant Action. |
| RIDES-021 | world | approximation | [#573](https://github.com/meneerkrabs/OpenTPW/pull/573) | Add nearest object-triangle ray hit and retain previous hit when none found; keep sandbox plane explicit. |
| RIDES-031 | world | out-of-scope | [#584](https://github.com/meneerkrabs/OpenTPW/pull/584) | Out-of-scope for TPW original-fidelity closure pending explicit TPI compatibility scope; do not copy TPW stamping as evidence. |
| RIDES-032 | world | approximation | [#583](https://github.com/meneerkrabs/OpenTPW/pull/583) | Distinguish no-pose list update path from callback route and determine whether +0x140 callback is invoked externally. |
| RIDES-040 | world | out-of-scope | [#585](https://github.com/meneerkrabs/OpenTPW/pull/585) | Out-of-scope for TPW parity unless TPI fixed-object compatibility is selected; maintain separate evidence proposal. |
| RIDES-045 | world | out-of-scope | [#586](https://github.com/meneerkrabs/OpenTPW/pull/586) | Out-of-scope TPI-to-TPW mod extension; enumerate unsupported variables without implying original TPW missing feature. |
| SAVE-002 | simulation | missing | [#536](https://github.com/meneerkrabs/OpenTPW/pull/536) | Plan transactional two-pass guest/attraction/guard restore; distinguish debug-only event ring from functional state. |
| SAVE-006 | simulation | approximation | [#544](https://github.com/meneerkrabs/OpenTPW/pull/544) | Specify safe persistent fields versus rebuilt handles and delayed reference binding; never restore raw engine pointers. |
| SAVE-007 | simulation | evidence-only | [#541](https://github.com/meneerkrabs/OpenTPW/pull/541) | Evidence-only compatibility matrix and scope decision for modified saves; preserve strict unknown-asset diagnostics. |
| SAVE-010 | simulation | approximation | [#543](https://github.com/meneerkrabs/OpenTPW/pull/543) | Create field-by-field import/derive/unsupported contract with owners; exclude raw sound-engine dumps and coaster work owned by #447. |
| SAVE-011 | simulation | approximation | [#545](https://github.com/meneerkrabs/OpenTPW/pull/545) | Plan structural boundary selection using decoded type-dependent train/car dimensions; preserve size/count hardening. |
| SAVE-012 | simulation | out-of-scope | [#546](https://github.com/meneerkrabs/OpenTPW/pull/546) | Research compatibility option and scope decision without replacing approved snapshot policy; separate restart source from camera restoration. |
| SAVE-013 | simulation | approximation | [#548](https://github.com/meneerkrabs/OpenTPW/pull/548) | Define restoreable presentation state and unsupported capability diagnostics; do not infer fidelity from empty corpus cases. |
| SAVE-920 | simulation | approximation | [#447](https://github.com/meneerkrabs/OpenTPW/pull/447) — existing coverage | Use existing proposal; no duplicate file. Coaster decode/restore family grouped in #447 because flags, controller fields, trains and runtime reconstruction share one import contract. |
| SAVE-921 | simulation | approximation | [#447](https://github.com/meneerkrabs/OpenTPW/pull/447) — existing coverage | Use existing proposal; no duplicate file. Coaster decode/restore family grouped in #447 because flags, controller fields, trains and runtime reconstruction share one import contract. |
| SAVE-922 | simulation | approximation | [#447](https://github.com/meneerkrabs/OpenTPW/pull/447) — existing coverage | Use existing proposal; no duplicate file. Coaster decode/restore family grouped in #447 because flags, controller fields, trains and runtime reconstruction share one import contract. |
| SAVE-923 | simulation | approximation | [#447](https://github.com/meneerkrabs/OpenTPW/pull/447) — existing coverage | Use existing proposal; no duplicate file. Coaster decode/restore family grouped in #447 because flags, controller fields, trains and runtime reconstruction share one import contract. |
| SAVE-940 | simulation | out-of-scope | [#549](https://github.com/meneerkrabs/OpenTPW/pull/549) | TPI-only research/adjudication proposal with provisional names; no mandatory TPW runtime changes. |
| SAVE-944 | simulation | out-of-scope | [#550](https://github.com/meneerkrabs/OpenTPW/pull/550) | TPI-only research and parser validation plan; keep bounds checks rather than emulate memory corruption. |
| TOUR-005 | world | approximation | [#587](https://github.com/meneerkrabs/OpenTPW/pull/587) | Add hoarding post/station instance height lookup only after owner coordination; track-size rebuild evidence separately. |
| TOUR-011 | world | approximation | [#589](https://github.com/meneerkrabs/OpenTPW/pull/589) | Decode template steppers, render-frame state transitions, terrain mode and node-orientation flag; coordinate current tour owner. |
| TRACK-001 | world | approximation | [#590](https://github.com/meneerkrabs/OpenTPW/pull/590) | Resolve instance-bit selector, one-sided faces, translation-only transforms and owner/station fallback. |
| TRACK-002 | world | evidence-only | [#591](https://github.com/meneerkrabs/OpenTPW/pull/591) | Evidence-only consumer audit: vehicles correctly do not read .hmp; attribute residual to tour/ghost/bounds/preview owners. |
| TRACK-004 | world | approximation | [#592](https://github.com/meneerkrabs/OpenTPW/pull/592) | Implement direction-specific and cut-axis clear semantics with overlap counts/extension recursion; coordinate TRACK-007 replay. |
| TRACK-005 | world | approximation | [#588](https://github.com/meneerkrabs/OpenTPW/pull/588) | Replace premature dual-arm station initialization with original exit/entry tile placement and cost timing. |
| TRACK-007 | world | approximation | [#593](https://github.com/meneerkrabs/OpenTPW/pull/593) | Use line-segment clear steps rather than cut (-1,-1) rule per tile; keep already-traced refunds and reachability. |
| TRACK-010 | world | approximation | [#594](https://github.com/meneerkrabs/OpenTPW/pull/594) | Model under-track path/queue retention and refresh on later path placement; type-25 remains separately gated. |
| TRACK-018 | world | approximation | [#441](https://github.com/meneerkrabs/OpenTPW/pull/441) — proposed addendum | Existing #441 owns undo/reopen and #445 owns marker remainder; no duplicate runtime proposal. |
| TRACK-026 | world | evidence-only | [#595](https://github.com/meneerkrabs/OpenTPW/pull/595) | Investigate guest presentation interval between BUMP 1 and vehicle creation; preserve existing seat-order head sprites. |
| TRACK-032 | world | approximation | [#597](https://github.com/meneerkrabs/OpenTPW/pull/597) | Decode section fields/reference selection and preserve original loaded controller rather than blindly re-walk where different. |
| UI-003 | ui | approximation | [#464](https://github.com/meneerkrabs/OpenTPW/pull/464) | UiModel strips translation from non-root nodes before multiplying root.Transform. |
| UI-004 | ui | approximation | [#466](https://github.com/meneerkrabs/OpenTPW/pull/466) | UiModel groups triangles by material and sorts by depth; device depth initialization alone does not establish per-UI draw state. |
| UI-006 | ui | approximation | [#468](https://github.com/meneerkrabs/OpenTPW/pull/468) | Several traced colors are exact, but other UiColors and screen assignments still use capture-derived values. |
| UI-007 | ui | approximation | [#467](https://github.com/meneerkrabs/OpenTPW/pull/467) | DrawText adds a second offset pass when shadow is true, while traced original glyph/control/sprite routines have one pass; capture appearance remains unexplained. |
| UI-008 | ui | evidence-only | [#469](https://github.com/meneerkrabs/OpenTPW/pull/469) | UiWidgets draws mirrored halves of Mac purple_button, using lower half for focused/pressed/selected state; PC art identity is unread. |
| UI-013 | ui | approximation | [#470](https://github.com/meneerkrabs/OpenTPW/pull/470) | Dialogs and load timestamps exist; pause menu height follows font-tier extension and online zero-spacing variant lacks reply-state linkage. |
| UI-018 | ui | missing | [#471](https://github.com/meneerkrabs/OpenTPW/pull/471) | Globe mode steps flyers/clips but not ordinary island effects; lobby-specific fog, early leave gate close and unlock sequence remain incomplete or untraced. |
| UI-024 | ui | approximation | [#472](https://github.com/meneerkrabs/OpenTPW/pull/472) | LocalHappiness already counts a radius-one neighborhood, but its input filters IsOnParkCell and uses placement anchor instead of traced thing-cell lists; previews are an explicit CPU extension. |
| UI-025 | ui | missing | [#465](https://github.com/meneerkrabs/OpenTPW/pull/465) | MessageTubeCapacity is five with oldest-drop policy; original has eight slots, per-type caps and priority replacement; most action kinds lack events. |
| UI-026 | ui | missing | [#477](https://github.com/meneerkrabs/OpenTPW/pull/477) | PreviewIcon CPU triangles are present; original 3D pipeline animation 5 is absent, object-window tilt/speed differ, grid edge parity is untraced. |
| UI-027 | ui | missing | [#479](https://github.com/meneerkrabs/OpenTPW/pull/479) | ParkHud and ThemeParkIncHud use Objects.FindAt on terrain-hit cell instead of original pick-sprite selection. |
| UI-031 | ui | approximation | [#478](https://github.com/meneerkrabs/OpenTPW/pull/478) | Ctrl repeat and angle behavior partly exist; coaster/water tools end regardless of modifier and original queue-tail mapping remains incomplete. |
| UI-034 | ui | approximation | [#473](https://github.com/meneerkrabs/OpenTPW/pull/473) | Opaque transparent-slot textures use guessed black-key thresholds 6 and 48 with color un-premultiplication. |
| UI-037 | ui | approximation | [#476](https://github.com/meneerkrabs/OpenTPW/pull/476) | OptionText remains (16,16,48) although original labels pass black; unresolved bitmap/blend state prevents direct final-pixel inference. |
| UI-041 | ui | evidence-only | [#475](https://github.com/meneerkrabs/OpenTPW/pull/475) | Shipped park loading deliberately uses reference-start Full Simulation under no-player-profiles extension; original game mode comes from front-end flags and PC file carriage remains unknown. |
| UI-042 | ui | missing | [#480](https://github.com/meneerkrabs/OpenTPW/pull/480) | Panel animates but ends at table geometry; camcorder arms a host walk; postcard photo is empty and preview/address-book controls disabled. |
| UI-047 | ui | missing | [#474](https://github.com/meneerkrabs/OpenTPW/pull/474) | BuildActionCost shows coaster cost in every submode rather than only original placement-in-progress bit state. |
| UI-050 | ui | missing | [#482](https://github.com/meneerkrabs/OpenTPW/pull/482) | BuyWindow displays i_boxtick frames 0/1 only; recent-unowned state 2 is absent and its ring writer is unknown. |
| UI-051 | ui | missing | [#485](https://github.com/meneerkrabs/OpenTPW/pull/485) | HUD ticket count and spending use park state instead of global player earned-minus-spent ledger; key count is already matched. |
| UI-060 | ui | approximation | [#483](https://github.com/meneerkrabs/OpenTPW/pull/483) | Finance and information graphs scale pixels as two canvas units; grid-line color/placement remains host-specific despite implemented series/masks/ranges. |
| UI-064 | ui | missing | [#487](https://github.com/meneerkrabs/OpenTPW/pull/487) | Ball dragging and workload arithmetic exist, but the exact saved press/release rectangle is not settled; two child model states are not drawn and track press recenters rather than original stepped movement. |
| UI-082 | ui | approximation | [#488](https://github.com/meneerkrabs/OpenTPW/pull/488) | Hire portrait uses sprite-to-canvas scale 3.2; original screen-sprite scale 8 consumer is unknown, and employed-view portrait differs from applicant-only source behavior. |
| UI-086 | ui | missing | [#481](https://github.com/meneerkrabs/OpenTPW/pull/481) | Map draws placed objects with plain color rather than original WhichUIType classes, statistic-dependent ramp and neighbor outlines. |
| UI-091 | ui | missing | [#484](https://github.com/meneerkrabs/OpenTPW/pull/484) | Locator order uses IDs, follow snaps, patrol mark/slide/opener sounds are absent, and picked-up frame 9 falls back. |
| UI-092 | ui | missing | [#486](https://github.com/meneerkrabs/OpenTPW/pull/486) | Portrait phase lacks original record field; cycling uses hire order and locate is disabled while held; title-click target is unidentified, not proven rename. |
| UI-094 | ui | approximation | [#492](https://github.com/meneerkrabs/OpenTPW/pull/492) | Ride camera exists and shares original projection; static duration is measured 0.33s, seat-forward axis/look signs remain inferred, prototype heading drops Z, the invisible original window is absent, and crowd counting keeps the pre-ride park target without a traced original target source. |
| UI-095 | ui | missing | [#494](https://github.com/meneerkrabs/OpenTPW/pull/494) | Exit restores prior camera and recenters, but placed object link bytes are absent so original yaw rule never fires; host pose/smoothing also differs. |
| UI-096 | ui | missing | [#490](https://github.com/meneerkrabs/OpenTPW/pull/490) | TimeScaleFor handles named pause/map/postcard/delete/dismiss screens but not remaining generic messages or application deactivation. |
| UI-100 | ui | missing | [#489](https://github.com/meneerkrabs/OpenTPW/pull/489) | Link/end pointers and one cash transition are unselected; CCro crosshair art is absent from available game data. |
| UI-107 | ui | missing | [#496](https://github.com/meneerkrabs/OpenTPW/pull/496) | Cloud layers/cylinder exist; moon billboards and sky flag variants are absent, while fog state/range and primitive gate remain uncertain. RenderFog comment still cites retired UI-101 camera mismatch, so that rationale needs revalidation. |
| UI-901 | ui | approximation | [#493](https://github.com/meneerkrabs/OpenTPW/pull/493) | Happiness value/update are implemented; custom red-yellow-green dish fill substitutes for original listener model response to message 8. |
| UI-907 | ui | evidence-only | [#495](https://github.com/meneerkrabs/OpenTPW/pull/495) | All Visitors rides column is centered, but original cell constructor does not initialize alignment byte and later value is unknown. |
| UI-910 | ui | missing | [#491](https://github.com/meneerkrabs/OpenTPW/pull/491) | Visitor information values and controls exist, but portrait frame remains empty and cycle order is newest-id rather than proven thing-list order. |
| UI-914 | ui | missing | [#498](https://github.com/meneerkrabs/OpenTPW/pull/498) | Original-save history rings are not imported; stay history derives simulated seconds; count alignment/icon re-show and census semantics remain unverified despite complete main window. |
| UI-930 | ui | missing | [#501](https://github.com/meneerkrabs/OpenTPW/pull/501) | GuestWorldPicker hits a projected rectangle with lowest-id tie; original uses anchor circle, nearest clip w and first-drawn tie with one-frame lag. |
| UI-941 | ui | missing | [#497](https://github.com/meneerkrabs/OpenTPW/pull/497) | Lobby effects and flyers share original generator, but camera wander consumes no corresponding draws, changing strike/one-shot rhythm. |
| WALK-003 | simulation | approximation | [#551](https://github.com/meneerkrabs/OpenTPW/pull/551) | Design one persistent typed cell-order contract for supported persons; explicitly adjudicate online-avatar scope. |
| WALK-005 | simulation | missing | [#552](https://github.com/meneerkrabs/OpenTPW/pull/552) | Define edge-test input interface shared by drop/routing; no synthetic assumptions replacing unknown flags. |
| WALK-010 | simulation | approximation | [#547](https://github.com/meneerkrabs/OpenTPW/pull/547) | Plan explicit identity mapping and unsupported-thing lifecycle policy, not fabricated simulation of missing objects. |

## Documentation validation

All 163 IDs were independently reviewed against the pinned source snapshot. The combined 149-proposal documentation checkout passes progress consistency (372 fragments), fidelity-register consistency (162 baseline IDs), site generation (295 documentation pages plus index, zero source-link warnings), website checking (303 HTML pages), and 23 site-docs/site-check unit tests. These are documentation checks, not gameplay acceptance or original-fidelity tests. Original assets and executables were not published.

Final remote verification: all 149 newly published proposal heads were fetched and match the reviewed proposal/progress contents exactly, with no runtime-file changes. Main was rechecked at `a21bf922e25751e2f9f6c4b2ce2d6565f3cf7bad`; intervening #434/#438 changes add no new register IDs. DET-013’s saved-child wording correction is consistent with its evidence-gated plan.

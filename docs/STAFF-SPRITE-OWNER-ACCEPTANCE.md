# Staff owner acceptance map

This maps all 65 mandatory owner vectors to authored C# tests. Optional snapshot
vectors SS01–SS09 are excluded: this slice has no staff-history snapshot format.
See [architecture and qualifications](STAFF-SPRITE-OWNERS.md) and the
[core VM contract](STAFF-SPRITE-VM.md).

## Reading the map

Every row has execution status **A**: authored, not compiled or executed locally;
exact-final-head cross-platform CI is pending. **A/P** additionally identifies
an uncovered clause or narrower route below. A method name is a source reference,
not evidence that the method passed. Existing regression tests also need execution
at this head. Python guards are structural checks, not C# test execution.

Routes describe the code invoked by synthetic fixtures:

- **CPU**: detached VM, registry, owner, metadata or mathematical fixtures.
- **Events**: actual roster, placement, economy and role-handler methods with
  synthetic navigation/metadata.
- **Level**: actual Level scheduler, presentation, load or disposal methods on
  synthetic scenes. This is not a full application/GPU smoke run.
- **HUD**: actual employee controls, stack removal, callbacks or presentation
  coordinator with synthetic host objects.
- **Draw**: actual resource resolution, world quad preparation or HUD Draw into
  a CPU batch, using synthetic sprites and managed texture identity sentinels.
  No GPU texture creation, raster output or native comparison is implied.

Method references use the following class aliases. All files are under
`source/OpenTPW.Tests`; methods after the dot are their exact source names.

| Alias | Test class | File |
|---|---|---|
| EO | `EmployeePortraitOwnerTests` | [StaffSpriteOwnerTests.cs](../source/OpenTPW.Tests/StaffSpriteOwnerTests.cs) |
| RG | `StaffSpriteRegistryTests` | [StaffSpriteOwnerTests.cs](../source/OpenTPW.Tests/StaffSpriteOwnerTests.cs) |
| PH | `StaffSpritePresentationPhaseTests` | [StaffSpriteOwnerTests.cs](../source/OpenTPW.Tests/StaffSpriteOwnerTests.cs) |
| SE | `StaffSourceEventIntegrationTests` | [StaffSourceEventIntegrationTests.cs](../source/OpenTPW.Tests/StaffSourceEventIntegrationTests.cs) |
| HU | `StaffSpriteHudOwnerTests` | [StaffSpriteHudOwnerTests.cs](../source/OpenTPW.Tests/StaffSpriteHudOwnerTests.cs) |
| AE | `StaffSpriteAcceptanceEdgeTests` | [StaffSpriteAcceptanceEdgeTests.cs](../source/OpenTPW.Tests/StaffSpriteAcceptanceEdgeTests.cs) |
| RC | `StaffSpriteReconstructionTests` | [StaffSpriteReconstructionTests.cs](../source/OpenTPW.Tests/StaffSpriteReconstructionTests.cs) |
| LI | `StaffSpriteLevelIntegrationTests` | [StaffSpriteLevelIntegrationTests.cs](../source/OpenTPW.Tests/StaffSpriteLevelIntegrationTests.cs) |
| DR | `StaffSpriteDrawRecordTests` | [StaffSpriteDrawRecordTests.cs](../source/OpenTPW.Tests/StaffSpriteDrawRecordTests.cs) |
| MT | `StaffSpriteMetadataTests` | [StaffSpriteMetadataTests.cs](../source/OpenTPW.Tests/StaffSpriteMetadataTests.cs) |
| MM | `StaffSpriteMotionMathTests` | [StaffSpriteMotionMathTests.cs](../source/OpenTPW.Tests/StaffSpriteMotionMathTests.cs) |
| LP | `EmployeeLivePortraitTests` | [EmployeeLivePortraitTests.cs](../source/OpenTPW.Tests/EmployeeLivePortraitTests.cs) |
| EG | `EmployeePortraitGeometryTests` | [EmployeePortraitGeometryTests.cs](../source/OpenTPW.Tests/EmployeePortraitGeometryTests.cs) |
| HG | `HirePortraitGeometryTests` | [HirePortraitGeometryTests.cs](../source/OpenTPW.Tests/HirePortraitGeometryTests.cs) |
| VM | `StaffSpriteVmTests` | [StaffSpriteVmTests.cs](../source/OpenTPW.Tests/StaffSpriteVmTests.cs) |
| EC | `EmployeeWindowControlsTests` | [EmployeeWindowControlsTests.cs](../source/OpenTPW.Tests/EmployeeWindowControlsTests.cs) |
| SW | `StaffWindowTests` | [StaffWindowTests.cs](../source/OpenTPW.Tests/StaffWindowTests.cs) |
| SP | `StaffPlacementTests` | [StaffPlacementTests.cs](../source/OpenTPW.Tests/StaffPlacementTests.cs) |
| VL | `VisitorLivePortraitTests` | [VisitorLivePortraitTests.cs](../source/OpenTPW.Tests/VisitorLivePortraitTests.cs) |
| PS | `ParkResumeSaveFileTests` | [ParkResumeSaveFileTests.cs](../source/OpenTPW.Tests/ParkResumeSaveFileTests.cs) |

## Mandatory vectors

| ID | Contract and actual method(s) | Route / boundary | Status |
|---|---|---|---|
| O01 | Full-copy 70-word override/alias manifest: `EO.FullOwnerEventCopiesAllSeventyWordsWithOnlyAuthoredOverridesAndNoAliases` | CPU owner full event | A |
| O02 | Exact partial allowlist and retained destination flags/control: `EO.PartialOwnerEventHasExactAllowlistAndRetainsOwnFlagsClockControlAndAuthoredPose` | CPU owner partial event | A |
| O03 | Source-absent open never late-binds: `EO.AbsentOpenCannotLateBindAndFullWhileAbsentDestroysPreviousClone`; `LP.OpeningWithoutSourceStaysBlankAfterSourceReturnUntilRealFullRefresh` | CPU + actual HUD/Draw | A |
| O04 | Loss keeps clone; return preserves ticket/epoch: `EO.ActualSourceDeletionKeepsIndependentCloneAndReturnCopiesIntoSameEpochAndTicket`; `LP.SourceLossWorldRouteSuppressesActualHudQuadAndReturnRestoresSameClone` | CPU lifecycle + HUD/Draw | A |
| O05 | Full event during absence destroys binding and prevents late creation: `EO.AbsentOpenCannotLateBindAndFullWhileAbsentDestroysPreviousClone` | CPU full event; actual rename route separately O08 | A |
| O06 | Reopen/self-cycle/resize/repaint preserve owner: `HU.ReopenSelfCycleResizeAndRepaintDoNotReplaceOrTickButDifferentSelectionsFullyReclone`; `AE.ActualCanvasResizesAndFarCameraRepaintsDoNotReplaceScheduleOrMutateOwners` | Actual HUD and CPU Draw at four canvas sizes | A |
| O07 | Different selection fully reclones: `HU.ReopenSelfCycleResizeAndRepaintDoNotReplaceOrTickButDifferentSelectionsFullyReclone`; `RG.FirstFreeAscendingSlotsGrowByFiftyAndStaleTicketsNeverResolve` | HUD selection + CPU allocation | A |
| O08 | Rename open/cancel retain epoch; unchanged confirmation refreshes once: `HU.RenameCancelPreservesEpochAndSuccessfulUnchangedConfirmationFullyRefreshesExactlyOnce` | Actual HUD rename controls and stale callback | A |
| O09 | All removal routes release clone: `HU.ActualRemovalPathsReleaseCloneAndStaleRenameCannotResurrectIt`; `LI.ActualLevelDisposeImmediatelyRetiresSourcesClonesAndRejectsReusedIdentity` | Actual stack Pop/RemoveWhere/Clear/Replace; session and Level disposal | A |
| O10 | Reused numbers cannot retarget old selection: `HU.DirectFireImmediatelyRejectsUiIdentityAndCapturedRenameDespiteNumericReplacement`; `SE.SuccessfulRosterReplacementRejectsOldObjectEvenWhenNumericIdentityIsReused` | Actual HUD/roster events | A |
| O11 | Synthetic END at 63/125/126; retirement and reused-slot rejection: `EO.SyntheticEndRetiresOnlyAfterStrictDeadlineAndPartialCannotUseReusedSlot` | CPU synthetic END, not a supported staff program | A |
| O12 | Invalid execution remains diagnostic after partial: `EO.InvalidCloneExecutionIsDiagnosticAndPartialCopyDoesNotRepairRetainedControl` | CPU invalid retained stack pointer | A |
| R01 | First-free slot 2, 50-slot growth and stale tickets: `RG.FirstFreeAscendingSlotsGrowByFiftyAndStaleTicketsNeverResolve` | CPU registry | A |
| R02 | Ascending traversal and independent instance order: `RG.StaffSubsetSchedulingOrderHasNoCrossInstanceReadsAndCopiesOnlyAfterTraversal`; `RG.FirstFreeAscendingSlotsGrowByFiftyAndStaleTicketsNeverResolve` | CPU; reversed source/clone slots 1/2, not literal 2/7 | A |
| R03 | Visibility, disposal and far-camera repaint cannot own CPU history: `LP.HiddenWorldAndSkippedPassesKeepPortraitAndCloneClockAlive`; `LP.RendererDeletionClearsHandlesAndDoesNotRetireCpuSourceOrClone`; `AE.ActualCanvasResizesAndFarCameraRepaintsDoNotReplaceScheduleOrMutateOwners` | Actual Draw/PrepareWorld with extreme camera; no GPU cull claim | A |
| L01 | Pending hire allocates nothing; one fresh placed Stand: `SE.PendingHireAllocatesNothingAndValidPlacementCreatesExactlyOneFreshStand`; `SE.CommonPreUpdateAppliesLastTemplateThenIntervalWithoutRebasingAndDefersHandlerRequests` | Actual placement + common pre-update | A |
| L02 | Pickup returns prior member first; failed pickup untouched: `SE.PickupReturnsPreviousMemberFirstAndFailedPickupDoesNotTouchEitherSource`; `SE.PickedUpPreUpdateConsumesPendingFieldsWithoutReplayOrDuplicateRetirement` | Actual placement and picked-up normalization | A |
| L03 | Drop/cancel/save-carry create fresh clock once, no WALK interval: `SE.DropCancelAndSaveWhileCarryingEachCreateOneNewClockAndNoMovementInterval` | Actual placement Click/Cancel/PrepareForSave | A |
| L04 | Rest walk keeps source; actual rest deletes; end recreates first: `SE.RestWalkKeepsSourceActualRestDeletesAndRestEndConstructsBeforeSearch`; `EO.ActualSourceDeletionKeepsIndependentCloneAndReturnCopiesIntoSameEpochAndTicket` | Actual role/rest events + CPU clone loss/return | A |
| L05 | Rest-building removal cannot double-create: `SE.RemovingRestBuildingDistinguishesWalkingAndActualRestWithoutDoubleCreation`; `SE.EconomyRestFallbackCreatesOnlyForActualRestEvenWithoutNavigation` | Actual economy sale/ObjectRemoved/ReleaseJobsOn | A |
| L06 | Fire invalidates immediately: `SE.DirectRosterAndEconomyFireImmediatelyInvalidateIdentityAndSource`; `HU.DirectFireImmediatelyRejectsUiIdentityAndCapturedRenameDespiteNumericReplacement`; `SW.DismissClosesTheWindowAndAsksFirstWithConfirmations` | Roster/economy fire + HUD dismissal/callbacks | A |
| Q01 | Last template wins; template before interval, deadline retained: `SE.CommonPreUpdateAppliesLastTemplateThenIntervalWithoutRebasingAndDefersHandlerRequests`; `VM.TemplateReplacementRetainsEveryWordExceptTheSixControlFields` | Actual common pre-update + CPU raw manifest | A |
| Q02 | Absent pre-update consumes without replay; creation/deletion preserve order: `SE.PickedUpPreUpdateConsumesPendingFieldsWithoutReplayOrDuplicateRetirement`; `SE.UnpositionedPickedUpPreUpdateAlsoConsumesRequests`; `PH.AbsentMovingQueuesDesiredEntryAndConsumptionVersusReturnPreservesExactOrder` | Actual pre-update + CPU lifecycle ordering | A |
| Q03 | Same-job requests repeat, with bounded template reset: `SE.RepeatedSameIdleSetterRequestsStandAgain`; `VM.TemplateReplacementRetainsEveryWordExceptTheSixControlFields` | Actual repeated setter + CPU replacement contract | A |
| Q04 | Compare RestartPc, not CurrentPc/pending request: `SE.MovingComparesRestartEntryNotCurrentPcOrAnOlderPendingRequest`; `SE.MovingEscortRequestsHoldAndReturningGuardRequestsWalk` | Actual moving route + explicit post-pre-update observation | A |
| E01 | Patrol request precedes later moving WALK; early returns do not WALK: `SE.CommonPreUpdateAppliesLastTemplateThenIntervalWithoutRebasingAndDefersHandlerRequests`; `SE.ZeroDeltaContinuationWritesAngleZeroButArrivedAndStuckDoNotWriteAngleOrInterval` | Actual role/navigation return branches | A |
| E02 | Fast guard Run, other roles Walk, escort Hold, return Walk: `SE.ActualMovingBranchUsesGuardOnlyUnsignedBonusAndActualDisplacement`; `SE.MovingEscortRequestsHoldAndReturningGuardRequestsWalk` | Actual movement handlers | A |
| E03 | Repairing/BusyTicks-zero selects Repair; countdown does not restart: `SE.MechanicArrivalRequestsRepairWithoutBusyTicksAndCountdownDoesNotRestart`; `LP.EveryRoleDrawsItsActualProgramWithoutAnyWorldRender` | Actual mechanic events + Draw | A |
| E04 | Existing choice draw and counts 1/2/4: `SE.RepeatedPerformingEntriesUseExactlyTheExistingChoiceDrawEvenForCountOne`; `AE.PerformanceChooserExtremeWordsUseUnsignedCountMinusOneWithoutDrawingRandom`; `PH.PerformanceRequestsPreserveIntroHoldButDirectConstructionInitializesThem` | Actual role RNG + CPU 0/MaxValue API boundaries and all four explicit entries; MaxValue is not claimed reachable from WorldRng | A |
| E05 | Invalid/hole choice consumes without VM change or replay: `SE.InvalidPerformanceChoiceConsumesDrawAndRequestWithoutChangingOrReplayingVm`; `AE.PerformanceHoleIsConsumedAndMatchingResourceObservationCannotReplayOrRepairIt`; `MT.MissingMetadataStillConstructsAndSchedulesBothOwnersWithoutReplayOrHotFill`; `LP.MatchingTextureRecoveryRequiresNoWorldPassAndNeverMutatesVmHistory` | Actual events + CPU frozen-hole/resource observation; managed texture replacement separately, live GPU gate D06 | A |
| E06 | Ordinary performance retains intro/hold; direct construction initializes: `PH.PerformanceRequestsPreserveIntroHoldButDirectConstructionInitializesThem`; `VM.PerformanceReplacementChangesOnlyResolvedSlotAndFrameCountBeyondTemplateControls` | CPU owners and complete replacement manifest | A |
| E07 | Failed research wander only changes stamp: `SE.ResearchEntryRequestsResearchButFailedWanderOnlyRestamps` | Actual researcher handler, whole source comparison | A |
| E08 | Catch before routes; failed routes overwritten by Idle: `SE.CatchRequestPrecedesRouteAttemptsAndFailedCatchReturnIsOverwrittenByIdle`; `SE.MovingEscortRequestsHoldAndReturningGuardRequestsWalk` | Actual guard catch + later moving request routes | A |
| E09 | Release before set-down; failed return overwrites with Idle: `SE.EscortCompletionRequestsReleaseBeforeSetDownAndReturnFailureCanOverwriteIt`; `SE.MovingEscortRequestsHoldAndReturningGuardRequestsWalk` | Actual escort/return handlers | A |
| E10 | Strike clears angle; existing word adjusts/clamps without new RNG: `SE.LegacyStrikingNormalizationQueuesStrikeAndCapturesOnlyTheExistingRandomDraw`; `SE.StrikeWalkAndArrivalRequestPatrolThenStrikeWithZeroAngle`; `MM.StrikeUsesOnlyItsExistingWordAndUnsignedClampRatherThanModulo` | Actual strike entry/RNG + CPU exact word cases | A |
| N01 | All 12 wrapped interval vectors and double quotient: `MM.N01AllTwelveIntervalVectorsPreserveWrappedSumsAndDoubleQuotients`; `MM.N01MinimumIntAxisFailuresStillHaveSafeZeroIntervals` | CPU mathematical expectations; nearest-p53/p64 policy | A |
| N02 | 49 movement and 2048 publication oracles, delayed source heading: `MM.N02AllFortyNineMovementOraclesIncludeReflectedQuantizationBoundariesAndInvalidAxes`; `MM.N02All2048PublishedAnglesMatchIndependentSectorRanges`; `SE.ZeroDeltaContinuationWritesAngleZeroButArrivedAndStuckDoNotWriteAngleOrInterval`; `PH.PartialPrecedesHeadingPublicationAndNewSourceHeadingRemainsZeroUntilThatPhase` | CPU math/owners + actual moving/early-return events | A |
| N03 | Source/clone independent deadlines through interval change/loss: `PH.SourceAndCloneKeepIndependentDeadlinesAcrossIntervalChangeAndSourceLoss` | CPU samples 100/101/122/164 | A |
| N04 | Signed deadline wrap, late sample, one bounded run: `PH.RegistryUsesDirectSignedDeadlineWrapAndOneBoundedRunPerOpportunity`; `VM.FiftyInstructionCapPreservesRunningStateAndOutputWithoutInventingAYield`; `VM.EqualityLargeLateSamplesAndZeroIntervalsNeverCatchUp` | CPU registry and core | A |
| N05 | All 257 generated LUT entries versus independent decimal/Machin-pi oracle: `MM.N05All257GeneratedAnglesMatchIndependentDecimalSeriesAndMachinPi` | CPU mathematical generation; platform execution pending | A |
| N06 | Wrapped shift/subtraction and named invalid axes: `MM.N06CoordinateSubtractionAndRatioShiftRemainWrappedInt32Operations`; `MM.N06OnlyMinimumIntAxisPairsFailAndMovingZeroIsValid`; `SE.InvalidAngleEventPreservesPriorAngleAndContinuesTemplateAndSafeIntervalLogic` | CPU math + body moving event; invalid-axis host qualification | A |
| P01 | Partial copies previous heading, publication changes source, next partial catches up: `PH.PartialPrecedesHeadingPublicationAndNewSourceHeadingRemainsZeroUntilThatPhase` | CPU presentation coordinator | A |
| P02 | Immediate new-source full copy sees heading zero: `PH.PartialPrecedesHeadingPublicationAndNewSourceHeadingRemainsZeroUntilThatPhase` | CPU constructor/full/publication boundary | A |
| P03 | Pause creates no scheduler opportunities; UI publication/getters separate: `HU.PausedOuterCoordinatorCopiesUnderRenameWithoutSchedulingAndRepaintMutatesNothing`; `LI.RealPausedLevelNeverSchedulesWhileLostLevelContinuesBothOwnersWithoutApplyingPending`; `HU.RenameCancelPreservesEpochAndSuccessfulUnchangedConfirmationFullyRefreshesExactlyOnce` | Actual Level pause + HUD coordinator/rename | A |
| P04 | Open root under rename receives partial; closed root excluded: `HU.PausedOuterCoordinatorCopiesUnderRenameWithoutSchedulingAndRepaintMutatesNothing` | Actual HUD stack/Level coordinator; host eligibility only | A |
| P05 | Same samples and boundaries preserve complete state under batching: `LI.SameRealSamplesAndOuterBoundariesPreserveAllWordsAcrossSimulationBatchSizes`; `PH.EqualEventBoundariesGiveCompleteEqualityRegardlessOfBatchGroupingAndRepeatedReads` | Actual Level batch sizes + CPU state comparison | A |
| P06 | Different partial boundary leaves distinct LastPc despite equal final visible output: `PH.DifferentPartialBoundariesCanRetainDifferentLastPcDespiteSameSamplesAndFinalVisibleOutput` | CPU exact 1/12/102 sample sequence | A |
| P07 | Full-before-schedule World versus schedule-before-full Screen: `PH.FullEventsDoNotCommuteWithSchedulerAndAreNeverCoalescedToFinalIdentity` | CPU comparison before partial can hide difference | A |
| P08 | Immediate open/delete/close/reopen/rename preserve each sample barrier: `AE.ActualHudOpenDeleteCloseReopenRenameTapeRetainsEveryExplicitSampleBarrierAndRawState`; `PH.FullEventsDoNotCommuteWithSchedulerAndAreNeverCoalescedToFinalIdentity` | Actual HUD callbacks and Level presentation; CPU registry samples 126/189/252, ticket/epoch/70-word tape | A |
| D01 | Screen resolution uses yaw zero, getters do not mutate: `DR.ScreenDrawIsCameraIndependentAndCopiesEveryRawRegister`; `LP.EveryRoleDrawsItsActualProgramWithoutAnyWorldRender` | CPU resolver + actual HUD/Draw under changed camera | A |
| D02 | Flag instruction emits raw-pose World record and no HUD quad: `DR.WorldRouteKeepsAuthoredClonePoseAndReturnsToScreenOnlyThroughPartialCopy`; `LP.SourceLossWorldRouteSuppressesActualHudQuadAndReturnRestoresSameClone` | CPU + actual Draw; `WorldRouteNotImplemented` is expected | A |
| D03 | Absent-source performance loop can remain Screen and draw: `EO.SourceLossInsidePerformanceLoopStaysScreenUntilAnActualFlagInstruction`; `LP.AbsentSourcePerformanceLoopKeepsDrawingItsIndependentScreenClone` | CPU owner + actual HUD Draw | A |
| D04 | Return partial restores Screen on same clone: `EO.ActualSourceDeletionKeepsIndependentCloneAndReturnCopiesIntoSameEpochAndTicket`; `LP.SourceLossWorldRouteSuppressesActualHudQuadAndReturnRestoresSameClone` | CPU + actual HUD/Draw | A |
| D05 | Strict slot/direction/frame/range rejection including Performance1 frameCount: `DR.InvalidOutputNeverFallsBackClampsOrWraps`; `DR.MirroredDirectionOutsideStoredRangeIsRejected`; `DR.PerformanceOneActuallyYieldsFrameCountAndStrictResolverRejectsIt` | CPU strict resolver, no fallback/modulo/clamp | A |
| D06 | Current texture handle and immutable bank identity: `LP.MatchingTextureRecoveryRequiresNoWorldPassAndNeverMutatesVmHistory`; `LP.ResourceIdentityAndLayoutMismatchAreRejectedWithoutHotFillingCatalog`; `LP.RendererDeletionClearsHandlesAndDoesNotRetireCpuSourceOrClone` | Actual resolver/replacement/delete with managed sentinels; live GPU reload pending | A/P |
| D07 | Named unsupported transforms/resources leave words untouched: `DR.NondefaultAppearanceIsExplicitlyUnsupportedWithoutChangingWords`; `DR.UnsupportedMultipartAndNonfinitePoseRemainExplicit`; `LP.UnsupportedTransformProducesNoActualHudQuadAndKeepsRawWords` | CPU resolver + actual HUD no-quad assertion | A |
| D08 | Employee feet/2.4 scale, asymmetric origin, mirror and pixel factors: `EG.ActualCallbackDrawsIndependentNonWalkCloneBeforeWorldDrawAndAfterSourceLoss`; `EG.ActualThreeWidgetsAndDefaultDrawKeepIndependentBoundsFeetAndScale`; `HU.HudAuthoredRawCloneCoordinatesArePreservedByPeriodicCopy` | Actual CPU quad geometry; authored depth slot 1 (0.975), not native raster/depth proof | A |
| M01 | Missing bank still constructs/schedules ordinary source and clone: `MT.MissingMetadataStillConstructsAndSchedulesBothOwnersWithoutReplayOrHotFill` | CPU metadata/owners, output may become valid while draw stays unavailable | A |
| M02 | Later files/resources cannot hot-fill frozen catalog or replay requests: `MT.IdentifiedInvalidBankKeepsItsOrdinalInsteadOfSelectingTheNextBank`; `MT.MissingMetadataStillConstructsAndSchedulesBothOwnersWithoutReplayOrHotFill`; `LP.ResourceIdentityAndLayoutMismatchAreRejectedWithoutHotFillingCatalog` | Synthetic parser + CPU ownership + actual resolver | A |
| M03 | Same-catalog return stays unavailable; new catalog may create valid fresh source: `MT.MissingMetadataStillConstructsAndSchedulesBothOwnersWithoutReplayOrHotFill` | CPU source replacement/new session | A |
| M04 | Catalog setup independent of renderer; expected failures freeze identity, unrelated failures propagate: `RC.ActualSetupWithoutOriginalParkOrFileSystemDefersAllFiveSourcesUntilFinalClockAndPosition`; `MT.IdentifiedInvalidBankKeepsItsOrdinalInsteadOfSelectingTheNextBank`; `MT.ExpectedListingAndReadFailuresFreezeUnavailableButProgrammingFailuresPropagate`; `MT.SuccessfulDiscoveryAndLegacyHireVariantOrderRemainSortedAndStable` | Actual Level.SetupStaff with OriginalPark/filesystem absent + staff loader with synthetic in-memory files | A |
| S01 | Fresh final-clock reconstruction without old VM/window history or extra RNG: `RC.ActualLegacyEconomyLoadReplacesAllRoleTicketsAndDiscardsOldVmAndWindowHistory`; `RC.ActualWholeWorldRestoreFinishesClockAndStaffPositionsBeforeFreshReconstruction`; `RC.ActualSyntheticOriginalImportReconstructsEveryRoleWithImportedIdentityAndNoElapsedHistory`; `RC.UnknownRestoredPerformanceQueuesEntryThirteenOnlyWithoutAnExtraRandomDraw`; `SE.ImportedPickedUpReconciliationCreatesOnceAfterFinalPosition` | Actual Level load/restore/initialize and synthetic original-format parser/import, all roles; full-world GPU/menu shell excluded | A |
| S02 | Failure preserves economy/streams/pool/owners; success retires old session: `LI.ActualLevelLoadThingConflictPreservesEconomyStreamsPoolSourcesAndClone`; `LI.ActualLegacyLoadFailureKeepsOwnersAndSuccessfulLoadStartsFreshFinalClockSession`; `SE.FailedReplacementEnumerationDoesNotInvalidateTheLiveSource`; `SE.InvalidRestoredThingIdentitiesLeaveOldRosterPoolAndSourceUntouched` | Actual Level.LoadPark invalid ThingId/candidate paths and successful legacy replacement; roster/pool failures | A |
| G01 | Presentation cannot change gameplay RNG/serialization/hash: `SE.PresentationAndRepeatedDrawingDoNotChangeGameplaySerializationOrWorldRandomness`; `LI.CanonicalGameplayHashExcludesSourceClonePublicationAndWindowHistory`; `EG.RepeatedActualCallbackReadsAndDrawsLeaveEconomyRngSourceCloneAndNextWorldOutputUnchanged` | Actual role controls, Level canonical hash and actual Draw/no-read control | A |
| G02 | Separate hire/carry/guest, controls and save behavior: `HG.HireLoopRemainsSeparateFromTheSourceAbsentEmployeeWindow`; `SP.ACarriedMemberIsSavedDroppedOnItsOldCellAndAPendingHireIsNotSaved`; `VL.ObservedAndNoReadControlsHaveTheSameNextWorldQuadAndSimulationState`; `EC.ExplicitNamesSurviveSaveAndResume`; `PS.ACompleteEnvelopeRoundTripsThroughTheOwnFormatCodec` | Existing actual/synthetic regression routes; run complete test project, not only these representatives | A |

## Outstanding execution and fidelity gates

D06 remains narrower than a live graphics-device reload: its tests invoke the
actual resolver, managed-handle replacement and deletion routes, without a GPU
upload or native texture disposal. The CPU edge and reconstruction routes are
now mapped to existing methods. No existing method name stands in for an
unimplemented route, and no row is marked accepted merely because it is mapped.
Full-world reconstruction calls the actual restore/initialize stages after a
real host-envelope round trip, excluding the GPU/menu construction shell. The
original-import fixture generates its own synthetic original-format records;
it neither executes original software nor restores native VM history.

All 65 vectors still require exact-head C# execution. The following are separate
from synthetic coverage and cannot be closed by passing a structural guard:

- Live GPU texture reload and lawful all-role visual smoke, including source
  loss/return, rename and pause. Lawful host visual/GPU smoke remains pending.
- Host allocation/per-frame layout-validation cost measurement.
- Native full-event ordering and modal/control eligibility. Current behavior is
  immediate host full refresh plus post-simulation partial-before-heading, with
  every open employee root eligible under rename.
- Native control depth/raster parity, clone-world projection/submission/culling,
  nondefault transforms and multipart resources. The expected first-slice result
  for unsupported cases is explicit rejection, not their implementation.
- Native floating-environment provenance beyond the chosen nearest-p53/p64
  interval policy, and original saved sprite/window history.

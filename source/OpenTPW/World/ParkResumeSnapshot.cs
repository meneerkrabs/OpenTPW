namespace OpenTPW;

/// <summary>
/// One placed object's runtime state and its link into the script world (park-resume envelope): the runtime
/// snapshots appear exactly in layout-objects order, so the list index is the same stable reference the guest,
/// VM and economy restores join by, and <see cref="ParkResumeRuntime.State"/>'s entry identity must be the
/// layout record's. <see cref="RootScriptId"/> is the object's root script (a script whose parent is 0) while
/// its runtime is live: 0 for an object without a script or one whose runtime was stopped — a stopped runtime's
/// VM has already unregistered itself, so the script world does not carry it.
/// </summary>
/// <param name="RootScriptId">
/// Id of the world snapshot's root script this object's runtime runs, or 0 when the runtime has no live script.
/// </param>
/// <param name="State">The runtime's own explicit state (animator, head slots, counters and identity).</param>
internal sealed record ParkResumeRuntime( int RootScriptId, OriginalObjectRuntimeSnapshot State );

/// <summary>
/// One placed object's association with its economy instance: the direct exact mapping the live park holds
/// (the guest economy bridge's link), saved because re-deriving it by first <c>Info.Id</c> match would swap
/// duplicate objects — the Easymode park alone has three small toilets and two bins.
/// </summary>
/// <param name="AttractionId">The placed object's attraction id (its layout record's).</param>
/// <param name="InstanceId">The economy object's instance id (<c>ParkObjectState.Id</c>), not its Info.Id.</param>
internal sealed record ParkResumeEconomyBinding( int AttractionId, int InstanceId );

/// <summary>
/// The whole park in one versioned own-format envelope (save/load milestone): the legacy economy snapshot
/// (<see cref="ParkSaveFile.Data"/>, embedded by value — never re-implemented), the layout, the guests, the
/// script world, one runtime per placed object, the attraction-to-economy associations, the fixed-step clock and
/// the sound seed, plus the optional weather, gate, staff-world and advisor snapshots the capturing park had.
/// The parts are joined by the rules <see cref="ParkResumeSaveFile.ValidateShape"/> checks (identity, order and
/// allocator agreement), so a snapshot that was valid when written either restores or fails with a reason —
/// never half-applied. The original TPWS/TPWI payload is never written; asset content identity is the
/// <see cref="AssetHashes"/> manifest, not an archive path.
/// </summary>
/// <remarks>
/// The DTO is a deep copy of typed state: no live object graphs, no renderer, VM or guest references. Capture is
/// root's job (one Capture call per module, then this record); restoration runs module by module into a fresh
/// candidate after <see cref="ParkResumeSaveFile.ValidateSnapshot"/> accepted the envelope against the current
/// catalog, balance and asset manifest.
/// </remarks>
internal sealed record ParkResumeSnapshot
{
	/// <summary>Bound for <see cref="DisplayName"/> (a load menu line, not a document).</summary>
	public const int MaximumDisplayNameLength = 128;

	public required string Format { get; init; }
	public required int Version { get; init; }
	/// <summary>The park's theme (a <see cref="ObjectCatalog.Themes"/> name); the layout's catalog must be this theme's.</summary>
	public required string Theme { get; init; }
	/// <summary>How the park was started; pins the economy mode and balance layer it must restore under.</summary>
	public required ParkStartKind StartKind { get; init; }
	/// <summary>The park's world seed (<see cref="WorldSeed"/>); the layout's object-script key derives from it.</summary>
	public required ulong Seed { get; init; }
	/// <summary>The park's name for the load menu; bounded, no control characters.</summary>
	public required string DisplayName { get; init; }
	/// <summary>When the park was saved (metadata; not part of any simulation).</summary>
	public required DateTimeOffset SavedUtc { get; init; }
	/// <summary>The full economy (<see cref="ParkSaveFile.Capture"/>), with its world streams when the park had them.</summary>
	public required ParkSaveFile.Data Economy { get; init; }
	public required ParkLayoutSnapshot.Data Layout { get; init; }
	public required GuestSimulationSnapshot Guests { get; init; }
	public required RideScriptWorldSnapshot Scripts { get; init; }
	/// <summary>One entry per layout object, in layout-objects order.</summary>
	public required IReadOnlyList<ParkResumeRuntime> Runtimes { get; init; }
	/// <summary>The attraction-to-economy-instance associations of the placed objects.</summary>
	public required IReadOnlyList<ParkResumeEconomyBinding> EconomyBindings { get; init; }
	public required FixedStepClockSnapshot Clock { get; init; }
	/// <summary>The park's sound-event seed (<see cref="WorldSeed.SoundStream"/> state at capture).</summary>
	public required uint SoundSeed { get; init; }
	/// <summary>The weather's state, or null when the park had none (lobby/sandbox starts).</summary>
	public ParkWeather.ParkWeatherSnapshot? Weather { get; init; }
	/// <summary>The park gate's turn observation, or null when the park had no gate.</summary>
	public ParkGate.ParkGateSnapshot? Gate { get; init; }
	/// <summary>The staff world's actors, or null when the park had no staff world (the actors are the world-side authority).</summary>
	public StaffWorldSimulation.StaffWorldStateSnapshot? Staff { get; init; }
	/// <summary>The automatic advisor, or null when the park had none; never captured while a response is being spoken.</summary>
	public AutomaticAdvisor.AutomaticAdvisorSnapshot? Advisor { get; init; }
	/// <summary>
	/// The park's coasters, or null when it held none: every live coaster station's track, controller state and trains
	/// with their riders, joined by attraction id (the coaster part joined the envelope with the coaster slice; saves
	/// from before it carry no part and hold no coaster stations).
	/// </summary>
	public CoasterWorldSaveData? Coasters { get; init; }
	/// <summary>
	/// The park's kart and water tracks, or null when it held no track tile and no controller: the cell layer, every water ride's controller with its
	/// boats and guests, joined by attraction id (the part joined the envelope with the track slice, docs/TRACKS.md; earlier saves carry none).
	/// </summary>
	public TrackWorldSaveData? Tracks { get; init; }
	/// <summary>
	/// The park's tour rides, or null when no ride held a tour controller: every live tour ride's flying-car controller with its cars and riders, joined by attraction
	/// id (the part joined the envelope with the tour slice, docs/TOURS.md; earlier saves carry none and their tour rides get fresh controllers).
	/// </summary>
	public TourWorldSaveData? Tours { get; init; }
	/// <summary>
	/// The developer prototype Totem, or null when the park had none: where it stands, its runtime and script state
	/// and its economy association, joined by attraction id (the part joined the envelope with the prototype slice;
	/// saves from before it carry no part and hold no prototype).
	/// </summary>
	public PrototypeRideSaveData? Prototype { get; init; }
	/// <summary>
	/// SHA-256 manifest of the assets the park depends on: canonical expected-asset identifier → 64 lowercase hex
	/// characters. Computed from verified catalog/game-data paths at load; a saved path is never read back.
	/// </summary>
	public required IReadOnlyDictionary<string, string> AssetHashes { get; init; }
}

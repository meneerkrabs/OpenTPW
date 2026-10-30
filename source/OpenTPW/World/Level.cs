using OpenTPW.UI;

namespace OpenTPW;

public partial class Level : IDisposable
{
	internal static Level Current { get; set; }

	public RootPanel Hud { get; set; }
	public Sun SunLight { get; set; }

	/// <summary>[EXT:point-lights] The level's point lights (docs/LIGHTS.md): placed objects with light dummies register here; the original engine supports them but never creates one.</summary>
	public PointLightSet PointLights { get; } = new();

	public SettingsFile Global { get; private init; }
	private ParkLayout parkLayout = null!;
	internal SandboxTextOverlay TextOverlay { get; private set; } = null!;
	public PrototypeRide? PlacedRide { get; private set; }
	private bool isPlacing;
	/// <summary>The prototype ride is being placed; starting it ends a staff tool (one park tool at a time).</summary>
	public bool IsPlacing
	{
		get => isPlacing;
		set
		{
			if ( value )
			{
				StaffPlacement?.Cancel();
				// [EXT:online-coop-commands] another tool: a guest's queue edit or Move pick-up that waits for the host starts no tool once it is back (C3b)
				DropToolStartWait();
				PatrolTool?.Cancel();
				BuyLandTool?.Cancel();
				AreaClearTool?.Cancel();
				PoolTool?.Cancel();
			}
			isPlacing = value;
		}
	}
	public string LastActionMessage
	{
		get;
		private set
		{
			field = value;
			// [EXT:online-coop-commands] whose command wrote it (null outside one): the HUD says its own player's only (C3a-2)
			LastActionMessagePlayer = Commands?.RunningPlayer;
		}
	} = "";

	/// <summary>[EXT:online-coop-commands] The player whose command wrote <see cref="LastActionMessage"/>, or null when no command did (C3a-2).</summary>
	internal int? LastActionMessagePlayer { get; private set; }

	/// <summary>
	/// [EXT:online-coop-commands] Whether the HUD says <see cref="LastActionMessage"/> (C3a-2): written outside any command, or by a command of this machine's player in a park that
	/// does not follow a stream. Another player's command is not this machine's to speak of, and a guest hears its own commands' outcomes from its requests.
	/// </summary>
	internal bool SaysLastActionMessage => LastActionMessagePlayer is not { } player || (player == Commands.Player && !Commands.FollowsStream);
	/// <summary>Imported original level data; null for the generic sandbox.</summary>
	public OriginalPark? OriginalPark { get; private set; }
	public OriginalTerrain? OriginalTerrain { get; private set; }
	/// <summary>The build tools' blueprint markers (original levels only).</summary>
	public BlueprintMarkers? Blueprint { get; private set; }
	/// <summary>The harbour's waves and flags (<see cref="HarbourDynamics"/>), or null for a park without them (Theme Park Inc, a sandbox, a level without <c>WaterStartPos</c>).</summary>
	public HarbourDynamics? Harbour { get; private set; }
	/// <summary>Park management simulation (money, clock, staff, research); original levels only for now.</summary>
	public ParkEconomyRuntime? Park { get; private set; }
	private ParkLitterEffects? litterEffects;
	/// <summary>The park's weather, created on the first simulation step of a park; null without a park economy.</summary>
	public ParkWeather? Weather { get; private set; }

	/// <summary>
	/// The park door of an original level: the entrance gate's script and the open/close walk over the
	/// attractions. Null for the sandbox and read-only visits.
	/// </summary>
	public ParkGate? Gate { get; private set; }

	/// <summary>Park name on the gate sign of an imported original level (TrueType sign text demo).</summary>
	public OriginalGateSign? GateSign { get; private set; }

	/// <summary>The info id of the level's park gate (<c>global.sam</c> <c>ParkName.GateObjectId</c>), or null when the level names none; known whether or not its sign could be drawn.</summary>
	// [DATA:levels/<level>/global.sam:ParkName.GateObjectId]
	public int? GateInfoId => int.TryParse( Global?["ParkName.GateObjectId"], out var id ) ? id : null;

	/// <summary>The park's name: the gate's two sign lines (saved with the park), or null for a level without a gate sign.</summary>
	public ParkNameLines? ParkName { get; private set; }

	/// <summary>
	/// An original save's camera block puts the camera where the save left it (zoom, yaw and the point it looks at); the Home key still returns to the level's home pose. A resumed park, a
	/// visit and a sandbox keep their own start. A saved pose that is no finite point of the map leaves the home pose.
	/// </summary>
	private void ApplySavedCamera()
	{
		if ( resumeState != null || visitLayout != null || OriginalPark?.Save?.RuntimeBlocks is not { } blocks || OriginalPark.Heightfield is not { } field || Camera.Mode is not ParkCameraMode mode )
			return;
		if ( ParkCameraMode.PoseOfSave( blocks.Camera ) is not var (x, z, height, yaw) )
			return;
		var target = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( x, 0, z ) );
		mode.SetPose( new Vector3( target.X, target.Y, 0 ), height, yaw );
	}

	/// <summary>
	/// The failures that leave a park without its gate sign (the gate then shows its plain board) instead of stopping it: data that cannot be read or decoded in this
	/// file system. A disposed object or a null argument is a bug and is not hidden, although its type derives from one of these.
	/// </summary>
	internal static bool IsGateSignFailure( Exception exception ) =>
		exception is not (ObjectDisposedException or ArgumentNullException)
		&& exception is IOException or InvalidDataException or KeyNotFoundException or ArgumentException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException;

	/// <summary>
	/// The park name and the gate sign of an original level: a resumed park keeps its saved lines, which the sign draws as the
	/// original's rebuild after loading does (0x468500); a new park takes the gate's default lines.
	/// </summary>
	// kept beside its steps for the tests that call it by reflection (ParkNameTests)
	private void SetupParkName()
	{
		foreach ( var _ in SetupParkNameSteps() ) { }
	}

	/// <summary><see cref="SetupParkName"/> in steps: the gate sign is composed over several (<see cref="OriginalGateSign.CreateSteps"/>).</summary>
	private IEnumerable<bool> SetupParkNameSteps()
	{
		// [EXT:online-coop-snapshot-join] a join keeps the host's name as its snapshot holds it, none included: the sign's default lines are not its name
		var savedName = resumeState?.ParkName is { } saved ? new ParkNameLines( saved.Line1, saved.Line2 ) : joinRestore != null ? null : SavedGateName();
		if ( OriginalPark != null )
		{
			var created = new OriginalGateSign.Creation();
			using var steps = OriginalGateSign.CreateSteps( OriginalPark, Global, created, savedName, reloaded: savedName != null ).GetEnumerator();
			while ( true )
			{
				bool more;
				try { more = steps.MoveNext(); }
				// the sign is decoration: a font or board that cannot be read in this file system must not stop the park, and the gate then shows its plain board (SignSlotPlan)
				catch ( Exception exception ) when ( IsGateSignFailure( exception ) )
				{
					Log.Warning( $"Gate sign: {exception.GetType().Name}: {exception.Message}" );
					break;
				}
				if ( !more )
				{
					GateSign = created.Sign;
					break;
				}
				yield return true;
			}
		}
		ParkName = joinRestore != null ? savedName : savedName ?? GateSign?.Lines;
	}

	/// <summary>
	/// The park name a player's own original save holds: the two sign lines of its park gate's attraction thing (<c>mParkGates</c>, name lines +0x3C8 and +0x40A), which the load's sign rebuild redraws (0x468500).
	/// Null for any other start (the shipped Easymode seed keeps the gate's default lines in the player's language) and for lines OpenTPW's sign cannot hold.
	/// </summary>
	// [BIN:TPW-EXE:0x004DB7D0 attraction serializer] the placed object's two sign lines (NameA/NameB) are part of its record; a park's name is its gate's pair (SAVE-006)
	private ParkNameLines? SavedGateName()
	{
		if ( playerSavePayload == null || OriginalPark?.Save is not { } save )
			return null;
		var gate = save.Attractions.FirstOrDefault( attraction => attraction.Id == save.World.Variables.ParkGates );
		if ( gate == null )
			return null;
		return ParkNameLines.FromSavedGate( gate.NameLine1, gate.NameLine2 );
	}

	/// <summary>
	/// Renames the park (the ticket price window's rename dialog): stores the two lines and redraws the gate sign. False for
	/// a read-only visit, a level without a park name, or lines a save could not hold (<see cref="ParkNameLines.IsValid"/>).
	/// </summary>
	// [APPROX:COMPAT-007] the rename stores the lines and redraws the gate (0x4993E0), but the handler's other effects are not built: the setters 0x4E2950/0x4E2A50 also copy the lines to the online park record (0x409410, 0x5B0940/0x5B0970: wide characters into +0x34 and +0x76 of the 0x50-byte record DAT_00F7C58C with its dirty byte +0xB8, unless the GameType word is 1), and the secret name's 0x4053C0(1) (built below: the cheat mode, docs/CHEATS.md) also posts a message of type 0x15 (0x40FB10), which is not; gate lines of a user-supplied original save are imported by SavedGateName from SaveAttraction.NameLine1/2 when valid (the shipped Easymode seed keeps the default lines); exact original-save import equivalence still needs corpus evidence — evidence needed: what the listeners of message type 0x15 do (runtime subscriptions of the message centre 0x788D3C)
	public bool RenamePark( string line1, string line2 )
	{
		if ( IsReadOnlyVisit || ParkName == null || line1 == null || line2 == null )
			return false;
		var lines = new ParkNameLines( line1, line2 );
		if ( !lines.IsValid )
			return false;
		ParkName = lines;
		GateSign?.Rename( lines );
		// [BIN:TPW-EXE:0x4993E0 secret park name] "Kevin the" / "Cosmic Cow" turns the cheat mode on (0x4053C0(1)) after the lines are stored, so the sign keeps the name; it works with or without the cheats extension (the user's decision, docs/CHEATS.md), and at the next park turn ([EXT:cheats-turn-boundary])
		if ( SecretParkName.Matches( line1, line2 ) )
			Park?.Economy.QueueCheat( new CheatAction( CheatKind.Mode ) );
		return true;
	}
	private bool wasMouseDown;
	private int renderedCellVersion;
	private int renderedOtherVersion;

	/// <summary>Notes that the path layer, the queues and the pool holes are drawn for the cell map as it is now.</summary>
	private void MarkCellsRendered( ParkCellMap cells )
	{
		renderedCellVersion = cells.Version;
		renderedOtherVersion = cells.Version - cells.PoolWrites;
	}

	/// <summary>Rebuilds the path layer (path tiles and queue models) when the cell map changed since it was last drawn; every frame's update runs it.</summary>
	internal void RefreshRenderedCells()
	{
		if ( OriginalTerrain == null || Guests == null || Guests.Grid.Cells.Version == renderedCellVersion )
			return;
		var cells = Guests.Grid.Cells;
		var other = cells.Version - cells.PoolWrites;
		var pathsChanged = other != renderedOtherVersion;
		MarkCellsRendered( cells );
		// [EXT:pools-render] a stroke of pool cells changes the ground mesh's holes only: the path layer and the queue models are not rebuilt for it
		if ( pathsChanged )
			OriginalTerrain.RefreshPaths();
		else
			OriginalTerrain.RefreshHoles();
	}
	/// <summary>Set each frame by the original HUD when the pointer is over it, so HUD clicks do not build.</summary>
	public bool UiCapturesMouse { get; set; }
	/// <summary>Simulation speed from the HUD speed control (0 = paused).</summary>
	public float SimulationTimeScale { get; set; } = 1f;

	/// <summary>The scene's rate-scaled clock (<see cref="OpenTPW.SceneClock"/>): the sky and the coasters' scrolling textures read it, so they follow the game speed and the pause.</summary>
	internal SceneClock SceneClock { get; } = new();
	/// <summary>Shows the ImGui developer panel and the sandbox BF4 text panel (off when the original HUD runs alone).</summary>
	public bool ShowDeveloperPanels { get; set; } = true;
	private readonly FixedStepClock simulationClock = new();
	/// <summary>Park visitors (imported original levels only; docs/GUESTS.md).</summary>
	public GuestSimulation? Guests { get; private set; }
	public GuestRenderer? GuestRenderer { get; private set; }
	/// <summary>Heads of riders on rides that have ADDHEAD (docs/GUESTS.md, "Riders' heads").</summary>
	public RideHeadRenderer? RideHeadRenderer { get; private set; }
	/// <summary>Draws the guests in costume with the theme's costume set (null when the theme ships none: they keep their kid sprite).</summary>
	public GuestRenderer? CostumeRenderer { get; private set; }
	/// <summary>[EXT:pools-swim] Draws the swimmers with the swim sprites, on the water; created beside the pool renderer, after it, so it draws after the water.</summary>
	public GuestRenderer? SwimmerRenderer { get; private set; }
	/// <summary>Draws the heads of riders in costume with the theme's costume heads (Jungle and Space only).</summary>
	public RideHeadRenderer? CostumeHeadRenderer { get; private set; }
	/// <summary>Draws the guests' balloons.</summary>
	public BalloonRenderer? BalloonRenderer { get; private set; }
	/// <summary>The hoarding boards and textures the level's objects share (docs/OBJECTS.md, "Hoarding"); cleared in <see cref="Dispose"/>.</summary>
	internal ObjectHoardingCache HoardingCache { get; } = new();
	/// <summary>[EXT:umbrellas-draw] The guests' open umbrellas (<see cref="UmbrellaRenderer"/>); null without the extension, its content or guests.</summary>
	public UmbrellaRenderer? UmbrellaRenderer { get; private set; }
	/// <summary>[EXT:winter-draw] The warm items the guests wear (<see cref="WarmItemRenderer"/>); null without the extension, its content or guests.</summary>
	public WarmItemRenderer? WarmItemRenderer { get; private set; }
	/// <summary>Draws the guests' and the staff's thought bubbles.</summary>
	public ThoughtRenderer? ThoughtRenderer { get; private set; }
	/// <summary>The litter sprites lying on the park's cells (null without a park economy).</summary>
	public LitterRenderer? LitterRenderer { get; private set; }
	/// <summary>The balloons over rides whose upgrade has just been installed (null without a park economy).</summary>
	public UpgradeBalloonRenderer? UpgradeBalloons { get; private set; }
	/// <summary>The default world seed (<see cref="WorldSeed.DefaultValue"/>); with it the guest stream keeps its old seed.</summary>
	public const ulong GuestSeed = WorldSeed.DefaultValue;
	/// <summary>The one seed every simulation random stream of this level derives from (docs/DETERMINISM.md).</summary>
	public WorldSeed Seed { get; }

	/// <summary>Level directory name, e.g. <c>jungle</c>.</summary>
	public string LevelName { get; }
	/// <summary>
	/// Set when this level shows a shared park read-only (docs/ONLINE.md): building, sandbox saves and
	/// any economy writes must be refused while it is set.
	/// </summary>
	public ParkVisitInfo? Visit { get; }
	public bool IsReadOnlyVisit => Visit != null;

	/// <summary>
	/// The Sound block of the original save the park was started from: a player's own save (<c>--original-save</c>, the TPWS import) or the level's shipped
	/// <c>Easymode.TPWI</c> (Instant Action, the reference start); null for a new Full Simulation park, a resumed OpenTPW park and a read-only visit.
	/// </summary>
	// [BIN:TPW-EXE:0x5ACCF0 park entry load] park entry (state 9 of 0x54E360) loads the newest file in the player's folder of the park through 0x414D40; an Instant Action player's folder holds the copy of the level's easymode.TPWI (0x5C8190 copies it when the player is created), so its Sound block plays as a loaded save's does
	public SaveSoundData? OriginalSaveSound => !IsReadOnlyVisit && incLayout == null ? OriginalPark?.Save?.Sound : null;
	/// <summary>True while this level runs the experimental Theme Park Inc slice: only the money economy (no staff, research or challenges), no HUD or advisor of the reference game.</summary>
	public bool IsThemeParkIncSlice => GameEditions.IsThemeParkInc && OriginalPark != null;
	/// <summary>The version 2 layout a visit applies instead of the shipped level import; null for version 1 packages and own parks.</summary>
	private readonly OpenTPW.Online.Packages.MinimalParkPayload? visitLayout;
	/// <summary>The visit's placed objects, one entry per payload record (null where content was missing); coaster stations are looked up here.</summary>
	private IReadOnlyList<OriginalObject?>? visitedObjects;
	private ParkResumeSnapshot? resumeState;
	// Developer tools only (--sandbox); players use the original-style online screens (OnlineScreens).
	private OnlinePanel? onlinePanel;
	/// <summary>The developer online panel's folder; null while the online extension is off (no panel then).</summary>
	private OnlineFolders? onlineFolders;

	/// <param name="start">How a writable original level starts; ignored for the sandbox and for read-only visits (which load the shipped save and run no economy).</param>
	private static readonly List<WeakReference<Level>> created = new();

	/// <summary>Levels still reachable (the current one included): after a full collection, more than one means an old park is kept alive.</summary>
	public static int AliveCount
	{
		get
		{
			lock ( created )
			{
				created.RemoveAll( reference => !reference.TryGetTarget( out _ ) );
				return created.Count;
			}
		}
	}

	/// <param name="playerSave">The decoded payload of a player's own original save of this level (<c>--original-save</c>): the park starts from it instead of the shipped Easymode save, with Full Simulation rules and the balance the save was made with.</param>
	public Level( string levelName, bool loadOriginalLevel = false, ParkVisitInfo? visit = null, OnlineFolders? onlineFolders = null, ParkStartKind start = ParkStartKind.OriginalSaveReference, WorldSeed? seed = null, ReadOnlyMemory<byte>? playerSave = null )
		: this( levelName, loadOriginalLevel, visit, onlineFolders, start, seed, null, null, overFrames: false, playerSave: playerSave ) { }

	internal Level( ParkResumeSnapshot snapshot, ParkEconomy economy, OnlineFolders? onlineFolders = null )
		: this( snapshot.Theme, true, null, onlineFolders, snapshot.StartKind, new WorldSeed( snapshot.Seed ), snapshot, economy, overFrames: false ) { }

	/// <summary>
	/// A level whose construction runs step by step through <see cref="Step"/>, so a game frame can build part of it and draw
	/// (docs/BROWSER-PERFORMANCE.md, "Building a park over frames"). The steps and their order are those of the constructor:
	/// the finished level is the same. Each step expects the level's scene to be the current one (<see cref="EntityConstructionScope"/>).
	/// </summary>
	internal static Level BeginOverFrames( string levelName, bool loadOriginalLevel, ParkVisitInfo? visit, OnlineFolders? onlineFolders, ParkStartKind start ) =>
		new( levelName, loadOriginalLevel, visit, onlineFolders, start, null, null, null, overFrames: true );

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A guest's park built over frames from a join snapshot (docs/COOP.md, "The game's join"): the resume's steps in the join restore mode, which keeps
	/// the host's live park exactly as its snapshot holds it, with <paramref name="join"/>'s session state applied after the simulation's restore, before the park's HUD and first frame.
	/// A guest's park from its first step.
	/// </summary>
	internal static Level BeginJoinOverFrames( ParkResumeSnapshot snapshot, ParkEconomy economy, OnlineFolders? onlineFolders, CoopJoinState join ) =>
		new( snapshot.Theme, true, null, onlineFolders, snapshot.StartKind, new WorldSeed( snapshot.Seed ), snapshot, economy, overFrames: true,
			join: join ?? throw new ArgumentNullException( nameof( join ) ) );

	private Level( string levelName, bool loadOriginalLevel, ParkVisitInfo? visit, OnlineFolders? onlineFolders,
		ParkStartKind start, WorldSeed? seed, ParkResumeSnapshot? resume, ParkEconomy? resumedEconomy, bool overFrames, ThemeParkIncLayoutCheckpoint? checkpoint = null, ReadOnlyMemory<byte>? playerSave = null,
		CoopJoinState? join = null )
	{
		playerSavePayload = playerSave;
		if (checkpoint != null && !GameEditions.IsThemeParkInc)
			throw new NotSupportedException("TPI layout checkpoints require Theme Park Inc data.");
		incLayout = checkpoint;
		if ( visit != null && visit.Level != levelName )
			throw new ArgumentException( "The visit level must match the loaded level.", nameof( visit ) );
		LevelName = levelName;
		lock ( created )
		{
			created.RemoveAll( reference => !reference.TryGetTarget( out _ ) );
			created.Add( new WeakReference<Level>( this ) );
		}
		Seed = seed ?? WorldSeed.Default;
		Visit = visit;
		resumeState = resume;
		// [EXT:online-coop-snapshot-join] a join's build is a guest's park from its first step (the HUD made for it reads that)
		joinRestore = resume != null ? join : null;
		if ( joinRestore != null )
			IsCoopGuestPark = true;
		visitLayout = visit != null && visit.Payload.HasBuiltLayout && !visit.IsSandbox ? visit.Payload : null;
		Global = new SettingsFile( $"/levels/{levelName}/global.sam" );
		Current = this;
		construction = Construct( visit != null ? !visit.IsSandbox : loadOriginalLevel, start, onlineFolders, resume, resumedEconomy ).GetEnumerator();
		if ( !overFrames )
			while ( Step() ) { }
	}

	private IEnumerator<float>? construction;
	private readonly ReadOnlyMemory<byte>? playerSavePayload;

	/// <summary>How far the construction has come, from 0 to 1 (a rough share of the work, for a progress bar).</summary>
	internal float ConstructionProgress { get; private set; }

	/// <summary>
	/// Set by a construction step whose next step is a long one (docs/BROWSER-PERFORMANCE.md, "Build steps"): a park built over frames then starts it in a
	/// frame of its own instead of after other steps. It changes only where a frame ends, never the work.
	/// </summary>
	internal bool NextStepIsLong { get; private set; }

	/// <summary>Runs the next construction step; false once the level is complete. A failed step leaves the level to be disposed.</summary>
	internal bool Step()
	{
		NextStepIsLong = false;
		if ( construction == null )
			return false;
		if ( construction.MoveNext() )
		{
			ConstructionProgress = construction.Current;
			return true;
		}
		construction.Dispose();
		construction = null;
		ConstructionProgress = 1;
		return false;
	}

	/// <summary>The constructor's work; each value is a point where a frame may end, with the share of the work done.</summary>
	private IEnumerable<float> Construct( bool original, ParkStartKind start, OnlineFolders? onlineFolders, ParkResumeSnapshot? resume, ParkEconomy? resumedEconomy )
	{
		if ( original )
		{
			using ( LoadProfile.Measure( "original-park" ) )
			{
				var parts = 0;
				foreach ( var loaded in OriginalPark.LoadSteps( LevelName, readShippedSave: incLayout == null && resume == null && (IsReadOnlyVisit || ParkStart.ReadsShippedSave( start )),
					savePayload: incLayout == null && resume == null && !IsReadOnlyVisit ? playerSavePayload : null ) )
				{
					if ( loaded == null )
						yield return Math.Min( 0.14f, 0.004f * ++parts );
					else
						OriginalPark = loaded;
				}
			}
			if ( resume != null && (resume.Layout.Cells.Width != OriginalPark.Map.CellCountX || resume.Layout.Cells.Height != OriginalPark.Map.CellCountY) )
				throw new InvalidDataException( "The saved cell map dimensions do not match the original terrain." );
			yield return 0.15f;
			// [EXT:tpi-viewer] the edition capability policy decides the economy: Theme Park Inc's Standard.sam lacks 86 of the
			// balance keys the Theme Park World economy reads, so its worlds run the money only (Theme Park Inc's own keys: cash, loans, costs,
			// the calendar, running costs) and not the staff, research or challenges (docs/THEME-PARK-INC.md, "Economy").
			using ( LoadProfile.Measure( "economy" ) )
			{
				if ( !IsReadOnlyVisit && GameEditions.Capabilities.Economy && resume == null )
				{
					// the settings and catalogues in steps of their own (docs/BROWSER-PERFORMANCE.md, "Build steps")
					foreach ( var created in ParkEconomyRuntime.ForOriginalLevelSteps( OriginalPark, start, Seed, playerSave: playerSavePayload != null ) )
					{
						if ( created == null )
							yield return 0.15f;
						else
							Park = created;
					}
				}
				else if ( !IsReadOnlyVisit && GameEditions.Capabilities.Economy )
					Park = ParkEconomyRuntime.ForResume( resumedEconomy ?? throw new InvalidDataException( "The resumed economy is missing." ), start );
				else if ( !IsReadOnlyVisit && GameEditions.IsThemeParkInc && GameEditions.Capabilities.Money && resume == null )
					Park = ParkEconomyRuntime.ForThemeParkIncLevel( OriginalPark, Seed );
			}
			yield return 0.2f;
		}

		using ( LoadProfile.Measure( "entities" ) )
		{
			foreach ( var progress in SetupEntities() )
				yield return progress;
		}
		if ( resume != null )
		{
			// the saved simulation's restore a part a step, its section taken a step at a time, so a measured frame names only its own part of it
			using var restore = RestoreResumeSimulationSteps( resume ).GetEnumerator();
			while ( true )
			{
				bool more;
				using ( LoadProfile.Measure( "resume" ) )
					more = restore.MoveNext();
				if ( !more )
					break;
				yield return 0.95f;
			}
		}
		// [EXT:online-coop-snapshot-join] a join's session state, before the park's first frame
		if ( joinRestore is { } joined )
		{
			using ( LoadProfile.Measure( "join-state" ) )
				ApplyJoinState( joined );
		}
		using ( LoadProfile.Measure( "level-hud" ) )
			SetupHud( onlineFolders );
		resumeState = null;
		joinRestore = null;
		incLayout = null;
		incLayoutCells = null;
		if ( visitLayout != null )
			ApplyVisitedLayout();
		if ( Visit?.Payload.PrototypeRide is { } ride )
			RestoreVisitedRide( ride );
	}

	/// <summary>
	/// Applies a version 2 shared layout: coaster tracks onto their stations, then a digest check that what this
	/// visit built is the park the owner published (docs/ONLINE.md "Local files and visits"). Cells and objects are
	/// applied inside <see cref="SetupEntities"/> so guests and bridges see them from the start. A package that
	/// publishes no digest cannot be checked and says so instead of reporting a mismatch.
	/// </summary>
	private void ApplyVisitedLayout()
	{
		var skipped = new List<string>();
		if ( visitLayout!.Coasters != null && visitedObjects != null )
		{
			foreach ( var coaster in visitLayout.Coasters )
			{
				var station = coaster.Station < visitedObjects.Count ? visitedObjects[coaster.Station] : null;
				if ( station == null || !SharedParkLayout.RestoreCoaster( this, station, coaster.Nodes, coaster.Closed, coaster.Complete ) )
					skipped.Add( $"coaster at object {coaster.Station}" );
			}
		}
		if ( visitLayout.LayoutDigest == null )
			Log.Warning( "This shared park publishes no layout digest; the built layout cannot be verified against the owner's park." );
		else
		{
			var digest = SharedParkLayout.DigestOf( this );
			if ( digest != visitLayout.LayoutDigest )
			{
				// One first difference of each kind, so a mismatch names a likely cause and not only a hash.
				var builtCells = SharedParkLayout.CaptureCells( Guests?.Grid.Cells ?? new ParkCellMap( 1, 1 ) );
				var publishedCells = visitLayout.Cells ?? Array.Empty<OpenTPW.Online.Packages.ParkCellRecord>();
				var cellDifference = builtCells.Zip( publishedCells ).FirstOrDefault( pair => pair.First != pair.Second );
				var objectDifference = SharedParkLayout.CaptureObjects( this ).Zip( visitLayout.Objects ).FirstOrDefault( pair => pair.First != pair.Second );
				Log.Warning( $"Shared park layout check failed: built {digest}, published {visitLayout.LayoutDigest}; the visit may differ from the owner's park."
					+ (cellDifference.Equals( default ) ? "" : $" First cell difference: built {cellDifference.First}, published {cellDifference.Second}.")
					+ (objectDifference.Equals( default ) ? "" : $" First object difference: built {objectDifference.First}, published {objectDifference.Second}.") );
			}
			else
				Log.Trace( $"Shared park layout applied and verified (digest {digest}: {visitLayout.Cells?.Count ?? 0} cells, {visitLayout.Objects.Count} objects, {visitLayout.Coasters?.Count ?? 0} coasters)." );
		}
		foreach ( var item in skipped )
			Log.Warning( $"Shared park {item} could not be rebuilt and is not shown." );
	}

	/// <summary>Shows the shared park's prototype ride; the visitor cannot move or remove it.</summary>
	private void RestoreVisitedRide( OpenTPW.Online.Packages.PrototypeRideState ride )
	{
		var position = new Vector3( ride.X, ride.Y, 0 );
		var allowed = OriginalPark != null ? CheckOriginalPlacement( position ) == OriginalPlacementResult.Allowed : ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius );
		if ( !allowed )
		{
			LastActionMessage = "The shared park's ride position is not buildable here; the ride is not shown.";
			return;
		}
		PlacedRide = new PrototypeRide( position, Objects.ScriptWorld );
		RegisterRideWithGuests( PlacedRide );
		if ( ride.Open )
			PlacedRide.Start();
	}

	/// <summary>The centre of the park's paths, where the native smoke tests aim the camera (a pose inside the original's zoom range, which a player can reach by scrolling).</summary>
	internal Vector3 PathFocus { get; private set; }

	private IEnumerable<float> SetupEntities()
	{
		SunLight = new Sun() { Position = new( 0, 100, 100 ) };

		if ( OriginalPark == null )
		{
			_ = new Terrain();
			ParkCameraMode.TargetExtent = ParkCameraMode.SandboxTargetExtent;
			ParkCameraMode.InitialTarget = Vector3.Zero;
		}
		else
		{
			using ( LoadProfile.Measure( "terrain" ) )
			{
				foreach ( var built in OriginalTerrain.BuildSteps( OriginalPark ) )
				{
					if ( built == null )
						yield return 0.3f;
					else
					{
						OriginalTerrain = built;
						built.SceneMilliseconds = () => SceneClock.Milliseconds;
					}
				}
			}
			yield return 0.35f;
			Blueprint = new BlueprintMarkers( OriginalPark.Heightfield, BlueprintHeightOffset );
			Blueprint.WarmUp();
			// [BIN:TPW-EXE:0x0053BFE0 dynamic meshes] the park's start creates the BlueprintMesh, the WaterMesh and the FlagsMesh together; Theme Park Inc's own build is not read here
			if ( GameEditions.Current == GameEdition.ThemeParkWorld && HarbourDynamics.LoadWaterStart( OriginalPark.LevelName ) is { } waterStart )
				Harbour = new HarbourDynamics( OriginalPark.Heightfield, waterStart, OriginalPark.LevelName );
			yield return 0.35f;
			foreach ( var _ in SetupParkNameSteps() )
				yield return 0.35f;
			var origin = OriginalParkPlacement.GetOrigin( OriginalPark.Heightfield );
			ParkCameraMode.TargetExtent = Math.Max( origin.X, origin.Y );
			var save = OriginalPark.Save;
			var field = OriginalPark.Heightfield;
			// [BIN:TPW-EXE:0x42a990 home target] the camera starts, and the Home key returns, at the target (475, 0, 175) in original units, the middle of the entrance cells (47, 17) and (48, 17) that every level's Standard.sam names (FixedItemInfo.EntranceA/BPos); a saved park's own camera (the KAME block: 0x42cdc0 writes the zoom, yaw, flags and target) is not imported (docs/reverse/SAVE-plan.md), so every park starts at Home
			var home = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( ParkCameraMode.HomeTargetX, 0, ParkCameraMode.HomeTargetZ ) );
			ParkCameraMode.InitialTarget = new Vector3( home.X, home.Y, 0 );
			// the native smoke tests look for free owned sites in the middle of the screen, which the original's Home view (the entrance forecourt) rarely has; they scroll to the imported paths' centre (or the initial path's, or the terrain centre) with this
			var paths = save?.PathCells.ToList() ?? Enumerable.Range( 0, field.CellCountX * field.CellCountZ )
				.Select( index => (X: index % field.CellCountX, Y: index / field.CellCountX) )
				.Where( cell => OriginalPark.Map.GetFlagsAt( cell.X, cell.Y ).HasFlag( MapCellFlags.InitialPath ) ).ToList();
			if ( visitLayout?.PathCells.Count > 0 )
				paths = visitLayout.PathCells.Select( cell => (X: cell.X, Y: cell.Y) ).ToList();
			var focus = paths.Count > 0
				? (X: (int)paths.Average( cell => cell.X ), Y: (int)paths.Average( cell => cell.Y ))
				: (X: field.CellCountX / 2, Y: field.CellCountZ / 2);
			var center = OriginalParkPlacement.GetCellCenter( field, focus.X, focus.Y );
			PathFocus = new Vector3( center.X, center.Y, 0 );
			Log.Trace( $"Original {OriginalPark.LevelName} level: {OriginalTerrain.SurfaceCellCount} heightfield cells, {OriginalTerrain.TerrainMeshCount} terrain meshes"
				+ (save == null ? "; no original save." : $"; Easymode import: {save.PathCells.Count} path cells, {save.PlacedObjects.Count} placed objects, {save.FixedItems.Count} fixed items.")
				+ (visitLayout != null ? $"; shared layout: {visitLayout.Cells?.Count ?? 0} cells, {visitLayout.Objects.Count} objects, {visitLayout.Coasters?.Count ?? 0} coasters." : "") );
		}
		yield return 0.4f;
		// the ride script handlers are compiled in steps of their own before the first object's script needs them (docs/BROWSER-PERFORMANCE.md, "Build steps")
		using ( LoadProfile.Measure( "ride-opcodes" ) )
		{
			while ( RideOpcodes.PrepareStep() )
				yield return 0.4f;
		}
		using ( LoadProfile.Measure( "objects" ) )
		{
			foreach ( var share in SetupObjects() )
				yield return 0.4f + 0.35f * share;
			// [BIN:TPW-EXE:0x00415140 load refresh] the cells are refreshed once the park's instances exist
			Objects?.CommitFootprintRefresh();
		}
		using ( LoadProfile.Measure( "coasters" ) )
			SetupCoasters();
		Camera.SetCameraMode<ParkCameraMode>();
		ApplySavedCamera();
		yield return 0.8f;
		if ( OriginalPark != null )
		{
			using ( LoadProfile.Measure( "guests" ) )
			{
				foreach ( var _ in SetupGuestsSteps( OriginalPark ) )
					yield return 0.85f;
			}
			LinkWorldGenerator();
			yield return 0.9f;
			using ( LoadProfile.Measure( "paths" ) )
			{
				SetupPaths();
				SetupTracks();
				SetupTours();
			}
			if ( OriginalTerrain != null && Guests != null )
			{
				var cells = Guests.Grid.Cells;
				OriginalTerrain.PathSource = ( x, y ) => cells.TypeAt( x, y ) == ParkCellType.Path;
				OriginalTerrain.QueueSource = ( x, y ) => cells.TypeAt( x, y ) == ParkCellType.Queue;
				OriginalTerrain.QueueModelSource = ( x, y ) => cells.TypeAt( x, y ) == ParkCellType.Queue ? QueueModels.Choose( cells, x, y ) : null;
				OriginalTerrain.PathVariantSource = PathVariants.IsSecond;
				// [BIN:TPW-EXE:0x00450D10 land refresh] the land outside the park is drawn darker
				OriginalTerrain.LandFlagsSource = ( x, y ) => cells.InBounds( x, y ) ? cells.FlagsAt( x, y ) : ParkCellFlags.None;
				// [EXT:pools-render] the pool cells are render-only holes of the ground mesh while the extension is on (nothing else reads this)
				// [BIN:TPW-EXE:0x00535DD0 hole cell types] the ground is not drawn under an object, entrance or exit cell (the cell refresh writes the hole word), so the floor stands on nothing
				OriginalTerrain.RenderHoleSource = ( x, y ) => ObjectCellHoles.IsHole( cells, x, y ) || PoolRenderHoles.IsHole( cells, x, y, FeatureGates.Pools );
				// [BIN:TPW-EXE:0x00536490 map init refresh] the world's set-up refreshes every cell in order, so each path cell's chooser rolls once at the start
				for ( var y = 0; y < cells.Height; y++ )
					for ( var x = 0; x < cells.Width; x++ )
						RefreshTile( x, y );
				OriginalTerrain.RefreshPaths();
				MarkCellsRendered( cells );
			}
			if ( Park != null && Guests != null )
				Park.AttachGuests( Guests, openParkOnAttach: resumeState == null );
			ConnectObjectsToGuests();
			if ( Guests != null )
				Guests.WeatherDropsFalling = () => Weather is { CurrentDrops: > 0 };
			// [EXT:umbrellas-weather-map] rain without lightning is Theme Park Inc's rain, lightning a storm, snow snow (the flags of the park's weather, not its drops: the umbrella rules ask what the weather is, not how hard it falls)
			if ( Guests != null )
				Guests.UmbrellaWeatherState = () => Weather is { } weather ? UmbrellaRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow ) : UmbrellaRules.WeatherNone;
			// [BIN:TPI-EXE:0x613ea0 track-cell stand-down] the scheduling and the carrying out of an umbrella change stand down on a kart or water track cell of the map's second layer (UMB-002)
			if ( Guests != null )
				Guests.UmbrellaOnTrackCell = ( x, y ) => UmbrellaRules.OnTrackCell( Guests.Grid.Cells.Tracks, x, y );
			// [BIN:TPI-EXE:0x5BCDDE arrival weather] Theme Park Inc's arrival group reads the weather thing's state word (+0x70), the running spell's kind (ThemeParkIncWeatherSpells); Theme Park World's park weather has no such state
			if ( Guests != null && GameEditions.IsThemeParkInc )
				Guests.ArrivalWeatherState = () => Park?.Economy.TpiWeatherState ?? 0;
			// [EXT:winter-season-terms] the winter shops read the park's calendar month (the economy's clock, which the weather steps on too) and their own weather signal, which reads snow as snow even with lightning (Halloween's snow band is its lightning band)
			if ( Guests != null )
				Guests.WinterWeatherState = () => Weather is { } weather ? WinterRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow ) : UmbrellaRules.WeatherNone;
			if ( Guests != null )
				Guests.WinterMonth = () => Park?.Economy is { } economy ? ParkCalendar.DateTimeAt( economy.Tick ).Month : 0;
			// [EXT:winter-always] with the always winter option on the winter shops sell at their winter demand all year, in the parks the option holds on December's row (not space, whose seasons it leaves alone)
			if ( Guests != null )
				Guests.WinterAlways = FeatureGates.WinterAlways && Park?.Economy?.Settings?.Standard is { } standard && ParkWeather.CanSnowFromTheWinterRow( standard );
			// [EXT:rain-mood-extension] the rain mood rule is on while the extension is: the guests read the same weather state as the umbrella rules
			if ( Guests != null )
				Guests.RainMood = RainMoodFor( FeatureGates.RainMood );
			// [EXT:winter-cold] the cold rule is on while its extension is: the same weather state, and the park's calendar day for a glühwein's warmth window
			if ( Guests != null )
			{
				Guests.WinterCold = WinterColdFor( FeatureGates.WinterCold );
				Guests.WinterDay = () => Park?.Economy is { } economy ? ParkCalendar.DayIndex( economy.Tick ) : 0;
			}
			using var connectProfile = LoadProfile.Measure( "staff-gate-economy" );
			ConnectObjectsToEconomy();
			yield return 0.95f;
			if ( Park != null )
			{
				litterEffects = new ParkLitterEffects( Park.Economy.Litter, ParticleRendererModule.AdvisorPool );
				if ( OriginalPark != null )
				{
					// the economy that owns the litter is replaced by a load, so the renderer asks for it again every frame
					LitterRenderer = new LitterRenderer( OriginalPark.Heightfield, () => Park?.Economy.Litter, GuestSpriteAtlas.LoadLitter() );
					UpgradeBalloons = new UpgradeBalloonRenderer( OriginalPark.Heightfield, GuestSpriteAtlas.LoadSpecialEffects() );
				}
			}
			yield return 0.95f;
			ImportOriginalTracks();
			ImportOriginalRideSettings();
			// the scripts come before the guests: a guest in state 16 is a rider the ride's saved script state holds (its walk, bounce and head tables), so the guest import needs to know which scripts were restored
			ImportOriginalScripts();
			ImportOriginalGuests();
			ReconcileOriginalAdmissions();
			// the vehicles of the kart and water rides carry guests the guest import restored as riders
			ImportOriginalTrackRiders();
			yield return 0.95f;
			ImportOriginalCoasters();
			ImportOriginalRetainedRideStates();
			ImportOriginalArrivals();
			yield return 0.95f;
			StartPlacedParticles();
			ImportOriginalParticles();
			yield return 0.95f;
			if ( resumeState == null && !GameEditions.IsThemeParkInc && OriginalPark.Save != null && visitLayout == null )
				ReleaseUnimportedThings();
			if ( GameEditions.Capabilities.Staff )
				foreach ( var _ in SetupStaffSteps() )
					yield return 0.95f;
			yield return 0.95f;
			// [EXT:tpi-park-open] the Theme Park Inc park runs no gate commands: its gate script is not traced, so the economy neither drives nor closes the park door
			if ( !GameEditions.IsThemeParkInc )
				SetupParkGate();
		}
	}

	/// <summary>
	/// The original has one world generator (tp.exe 0x00516330): the park's economy holds it (it drew the staff pool already) and the guests, the staff and the
	/// scripts draw from the same object. A level without an economy owns one started from the world seed.
	/// </summary>
	private void LinkWorldGenerator()
	{
		var world = Park?.Economy.World ?? new WorldRng( Seed.GeneratorState );
		Guests?.AttachWorld( world );
		Objects.ScriptWorld.AttachWorld( world );
	}

	/// <summary>The guests and their renderers; each value is a point where a frame may end (docs/BROWSER-PERFORMANCE.md, "Build steps").</summary>
	private IEnumerable<bool> SetupGuestsSteps( OriginalPark park )
	{
		var settings = GuestSettings.Load( park.LevelName );
		var land = LandSetup( park, settings );
		// A version 2 visit walks the shared park's own cell map (the owner's built paths and queues), not the shipped import.
		// A resumed save from before land existed gets the level's land as a new park (or the imported save's) would.
		var grid = incLayoutCells != null ? new GuestPathGrid(incLayoutCells)
			// [EXT:online-coop-snapshot-join] a join's map is the host's live one, land-less too (an imported save whose land walk failed): no land is laid on it
			: resumeState != null ? new GuestPathGrid( ParkLayoutSnapshot.RestoreCellMap( resumeState.Layout, land == null || joinRestore != null ? null : cells => cells.SetUpLand( land, park.Save ) ) )
			: visitLayout?.Cells != null
			? new GuestPathGrid( SharedParkLayout.BuildCellMap( park.Map.CellCountX, park.Map.CellCountY, visitLayout.Cells ) )
			: new GuestPathGrid( ParkCellMap.FromOriginal( park.Map, park.Save, land, park.Heightfield ) );
		LandBarrier = ( x, y ) => ParkLand.IsBarrier( park.Map, park.Heightfield, x, y );
		// the demolition keeps the NoModify flag of a MAP initial path cell in front of an entrance (tp.exe's cell byte +0x26 bit 8, 0x527EE0)
		grid.Cells.InitialPathSource = ( x, y ) => park.Map.GetFlagsAt( x, y ).HasFlag( MapCellFlags.InitialPath );
		BuyLandTool = grid.Cells.Land != null && !IsReadOnlyVisit
			? new BuyLandTool( () => Guests?.Grid.Cells, () => Park?.Economy, LandBarrier, command => Commands.Issue( command ) )
			: null;
		Guests = new GuestSimulation( grid, settings, Seed.GuestStream ) { OnlineVisit = Visit != null };
		BindGuestThoughtPresentation( Guests );
		// a visit has no economy, whose turn an own park's arrivals read (ParkEconomyRuntime.AttachGuests): its arrivals read the turn of the save it is built on
		if ( Visit != null )
			Guests.ArrivalClock = VisitArrivalClock( Guests, park.Save?.World.Variables.GameTick );
		Guests.AttachThings( Things );
		RegisterPoolActivities();
		NextStepIsLong = true;
		yield return true;
		List<GuestSpriteAtlas> sprites;
		using ( LoadProfile.Measure( "guest-sprites" ) )
			sprites = GuestSpriteAtlas.LoadKids();
		yield return true;
		// [EXT:pools-guest-art] with the pools extension on, the kid and head sets are the extension's 2x ones (together, and only where the game's kid sets are the Theme Park World ones)
		var poolGuestSprites = PoolGuestSprites.LoadReplacement( sprites );
		yield return true;
		var gameKidCount = sprites.Count;
		var headSprites = poolGuestSprites?.Heads ?? GuestSpriteAtlas.LoadKidHeads();
		if ( poolGuestSprites != null )
			sprites = poolGuestSprites.Kids;
		// [BIN:TPW-EXE:0x0041A9D0 kid set limit] the kid and head folders load the sets in the original's order (BI, KI, TA and SU first) up to the preset's limit (2, 4, 6 or 8), and a guest's draw picks among those (KidSetLoad); the pools sets take the same places, as the game's own do
		if ( !GameEditions.IsThemeParkInc )
		{
			var kidLimit = KidSetLoad.LimitOf( IGraphicsSettings.Instance );
			sprites = KidSetLoad.Order( sprites, atlas => atlas.Name, kidLimit );
			headSprites = KidSetLoad.Order( headSprites, atlas => atlas.Name, kidLimit );
			Guests!.KidSetCount = sprites.Count;
		}
		yield return true;
		// [EXT:pools-swim] the swim sets (64 MiB decoded) are decoded on a worker only for a park that has pool water already; any other park starts that when its first pool cell is painted (PreloadSwimSprites)
		swimSpriteSets = null;
		swimSpritesPreloaded = false;
		if ( grid.Cells.CountOf( ParkCellType.PoolWater ) > 0 )
			PreloadSwimSprites();
		// A guest in costume is drawn with the theme's costume set (sprite category 2, tp.exe 0x004FE65E: +0x20 from 0x00541F70(2)), its head on a ride with the costume heads where the theme has them
		var costumes = GuestSpriteAtlas.LoadCostumes( park.LevelName );
		yield return true;
		var costumeHeads = GuestSpriteAtlas.LoadCostumeHeads( park.LevelName );
		// [BIN:TPW-EXE:0x004FCAC0 head pick] a theme without costume heads (Fantasy, Hallow) reseeds the world generator with the rider and draws once on every head hung on a ride (RideScriptWorld.HeadHung)
		Objects.ScriptWorld.HeadHangReseeds = !GameEditions.IsThemeParkInc && costumeHeads.Count == 0;
		NextStepIsLong = true;
		yield return true;
		// [EXT:pools-swim] swimmers are drawn by the pools' own renderer with the swim sprites (RefreshPools), whatever they wear
		// Reserve opaque bridge draw slots before every guest and staff sprite, including bridges built after swimmers appear.
		PoolBridgeRenderer = new PoolBridgeRenderer( () => PoolBridges, Objects.Grid.Origin, PoolStyles.ForTheme( park.LevelName ) );
		GuestRenderer = new GuestRenderer( Guests, park.Heightfield, sprites ) { FeetChooser = BridgeGuestFeet, Draws = guest => (!(guest.IsSwimming || guest.InPoolActivity) || SwimmerRenderer == null) && (costumes.Count == 0 || !guest.InCostume), DrawsRideBody = guest => costumes.Count == 0 || !guest.BodySpriteInCostume };
		NextStepIsLong = true;
		yield return true;
		if ( costumes.Count > 0 )
			CostumeRenderer = new GuestRenderer( Guests, park.Heightfield, costumes ) { FeetChooser = BridgeGuestFeet, Draws = guest => guest.InCostume && (!(guest.IsSwimming || guest.InPoolActivity) || SwimmerRenderer == null), DrawsRideBody = guest => guest.BodySpriteInCostume };
		NextStepIsLong = true;
		yield return true;
		// the riders of the bounce rides are drawn as bodies on the ride (tp.exe 0x00557AB0, BounceRiders), by the set of the guest's own kind
		var bounceRiders = rideBodyPoses = new FlatRideBounceRiders( Objects );
		GuestRenderer.BounceRiders = bounceRiders;
		if ( CostumeRenderer != null )
			CostumeRenderer.BounceRiders = bounceRiders;
		RideHeadRenderer = new RideHeadRenderer( Guests, headSprites, Objects ) { Draws = costumeHeads.Count == 0 ? null : guest => !guest.InCostume, PickByThingId = costumeHeads.Count == 0 };
		RideHeadRenderer.AddSource( new TrackHeadSources( () => trackRenderers.Values ) );
		RideHeadRenderer.AddSource( new TourHeadSources( () => tourRenderer ) );
		AddCoasterHeads( RideHeadRenderer );
		yield return true;
		if ( costumeHeads.Count > 0 )
		{
			CostumeHeadRenderer = new RideHeadRenderer( Guests, costumeHeads, Objects ) { Draws = guest => guest.InCostume };
			CostumeHeadRenderer.AddSource( new TrackHeadSources( () => trackRenderers.Values ) );
			CostumeHeadRenderer.AddSource( new TourHeadSources( () => tourRenderer ) );
			AddCoasterHeads( CostumeHeadRenderer );
		}
		yield return true;
		BalloonRenderer = new BalloonRenderer( Guests, park.Heightfield, GuestSpriteAtlas.LoadBalloons() );
		yield return true;
		// [EXT:umbrellas-draw] the open umbrellas, drawn with the umbrellas extension's own sprites at the guests' hands (the kids' renderer, then the costumes'); parks without the extension or its content have none
		UmbrellaRenderer = null;
		if ( UmbrellaSprites.Load() is { Count: > 0 } umbrellaSets && UmbrellaContent.Directory is { } umbrellaFolder )
			UmbrellaRenderer = new UmbrellaRenderer( Guests, park.Heightfield, umbrellaSets, new[] { GuestRenderer, CostumeRenderer }.OfType<GuestRenderer>().ToArray(), UmbrellaAnchors.Load( umbrellaFolder ) );
		yield return true;
		// [EXT:winter-draw] the warm items (hat, earmuffs, mittens) at the guests' heads and hands; parks without the extension or its content have none
		WarmItemRenderer = null;
		if ( WinterSprites.Load() is { Count: > 0 } winterSets && WinterContent.Directory is { } winterFolder )
			WarmItemRenderer = new WarmItemRenderer( Guests, winterSets, new[] { GuestRenderer, CostumeRenderer }.OfType<GuestRenderer>().ToArray(), WinterAnchorTable.Load( winterFolder ) );
		yield return true;
		ThoughtRenderer = new ThoughtRenderer( Guests, park.Heightfield, GuestSpriteAtlas.LoadThoughts(),
			() => Park?.Economy.Staff.Members ?? (IReadOnlyList<StaffMember>)Array.Empty<StaffMember>(),
			member => member.State != StaffState.PickedUp && StaffRenderer != null && StaffRenderer.TryGetDrawCell( member, out var x, out var y ) ? (x, y) : null )
		{ UmbrellaShown = UmbrellaRenderer == null ? null : UmbrellaRenderer.ShowsBubble };
		Log.Trace( $"Guests: {grid.WalkableCount} walkable path cells, {gameKidCount} original kid sprite sets{(poolGuestSprites != null ? $" (replaced by the pools extension's {sprites.Count} 2x sets)" : "")}, {costumes.Count} costume sets, {Guests.Settings.Types.Count} .sam peep types." );
	}

	/// <summary>
	/// The park turn the arrivals of a read-only visit read: the turn of the save the visit is built on (the level's shipped save) plus the turns its guests have run since; null without
	/// a save (the guests' own turns, as in a park started without one). An own park started from that save (Instant Action, or the save loaded directly) reads its economy's turn, which
	/// starts at the save's turn (755 in the shipped Jungle), so its first group is due at once (<see cref="GuestSettings.ArrivalTimeBetween"/>, 150 units of four turns); counted from
	/// turn 0, a visit's first group would come at turn 604, about 150 s in, with no guest before it to ride rides such as the Belly Bounce, which bounces only with riders aboard.
	/// </summary>
	// [EXT:online-visit-arrivals] the original's visit loaded the owner's uploaded save, a running park (docs/reverse/ONLINE-VISIT.md); a package carries the layout without the guests and the arrival timer, so a visit's arrivals start as those of an own park started from the save the visit is built on
	/// <summary>[EXT:rain-mood-extension] The rain mood settings the level hands its guests: the shipped rows while the extension's gate is on, none (the default: the rule never runs) otherwise.</summary>
	internal static RainMoodSettings? RainMoodFor( bool gateOn ) => gateOn ? RainMoodSettings.Default : null;

	/// <summary>[EXT:winter-cold] The cold rule's settings for a run: the shipped numbers while the <c>opentpw.winter-cold</c> gate is on, otherwise none (the rule never runs).</summary>
	internal static WinterColdSettings? WinterColdFor( bool gateOn ) => gateOn ? WinterColdSettings.Default : null;

	internal static Func<long>? VisitArrivalClock( GuestSimulation guests, long? savedTurn ) => savedTurn is { } start ? () => start + guests.ParkTurn : null;

	/// <summary>The Buy Land tool (tp.exe mouse mode 0x39); null for a level without land (no Hoardings.sam, Theme Park Inc, a visit).</summary>
	public BuyLandTool? BuyLandTool { get; private set; }

	/// <summary>Whether a cell is of the original types 7 or 0x1e, which no fence may cross (<see cref="ParkLand.CheckPurchase"/>); null for a level without an original map.</summary>
	internal Func<int, int, bool>? LandBarrier { get; private set; }

	/// <summary>The level's park fence: its gate posts beside the entrance cells and its <c>Hoardings.sam</c>; null without them.</summary>
	private static ParkLandSetup? LandSetup( OriginalPark park, GuestSettings settings )
	{
		if ( GameEditions.IsThemeParkInc )
			return null;
		var path = $"/levels/{park.LevelName}/Hoardings.sam";
		var setup = ParkLandSetup.FromLevel( settings.EntranceA, settings.EntranceB, FileSystem.FileExists( path ) ? FileSystem.ReadAllText( path ) : null );
		if ( setup == null )
			Log.Warning( $"Level {park.LevelName} has no park fence (entrance cells or {path}); every cell counts as owned." );
		return setup;
	}

	/// <summary>The visitor under the mouse, in or out of costume: the nearer of the two renderers' picks.</summary>
	// [BIN:TPW-EXE:0x532bd0 sprite to thing] the sprite pass picks the nearest pickable sprite, and the visitor (a thing of type 1) that owns it is then looked up only in the hovered cell and its eight neighbours
	// (offset table 0x81ed40, filled by 0x532b30: the cell itself first, then the others), so a visitor that is none of those cells is not picked, and the next best sprite is not tried; the hovered cell is the cell of the cursor ray's nearest hit
	// (0x45d560: the terrain or an object's model, ObjectRayPick), which a hill or a tall object between the camera and the visitor moves away. The gate runs on original levels, where a heightfield gives the ray a hit
	internal Guest? PickVisitor( System.Numerics.Vector2 mouse, System.Numerics.Vector2 viewport, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection )
	{
		float kidDepth = float.PositiveInfinity, costumeDepth = float.PositiveInfinity;
		var kid = GuestRenderer?.PickVisitor( mouse, viewport, view, projection, out kidDepth );
		var costumed = CostumeRenderer?.PickVisitor( mouse, viewport, view, projection, out costumeDepth );
		var picked = kid == null || costumed == null ? kid ?? costumed
			: costumeDepth < kidDepth || (costumeDepth == kidDepth && costumed.Id < kid.Id) ? costumed : kid;
		// [EXT:pools-swim] a swimmer in the water is picked like any visitor
		float swimDepth = float.PositiveInfinity;
		if ( SwimmerRenderer?.PickVisitor( mouse, viewport, view, projection, out swimDepth ) is { } swimmer && ( picked == null || swimDepth < Math.Min( kidDepth, costumeDepth ) ) )
			picked = swimmer;
		if ( picked == null || OriginalPark is not { } park )
			return picked;
		return TryGetGroundHit( mouse, viewport, view, projection, out var ground ) && OriginalParkPlacement.TryGetCell( park.Heightfield, ground.X, ground.Y, out var x, out var y ) && IsInHoveredNeighbourhood( (x, y), ((int)MathF.Floor( picked.X ), (int)MathF.Floor( picked.Y )) ) ? picked : null;
	}

	/// <summary>Whether a thing's cell is the hovered cell or one of its eight neighbours (the offsets of tp.exe's table 0x81ed40).</summary>
	internal static bool IsInHoveredNeighbourhood( (int X, int Y) hovered, (int X, int Y) thing ) =>
		Math.Abs( thing.X - hovered.X ) <= 1 && Math.Abs( thing.Y - hovered.Y ) <= 1;

	private List<ParkEvent>? cheatNotes;

	/// <summary>The cheats applied since the last call (docs/CHEATS.md), for the HUD's short on-screen note; emptied by the call.</summary>
	internal IReadOnlyList<ParkEvent> TakeCheatNotes()
	{
		if ( cheatNotes is not { Count: > 0 } )
			return Array.Empty<ParkEvent>();
		var notes = cheatNotes.ToArray();
		cheatNotes.Clear();
		return notes;
	}

	/// <summary>
	/// Wires the park door (docs/GATES.md): the economy's open/close events command the entrance gate's script
	/// and close or open the attractions, and the gate's deferred-close retry runs with the economy tick.
	/// </summary>
	private void SetupParkGate()
	{
		if ( Park == null )
			return;
		BuildParkGate();
		// The runtime forwards the events of whichever economy it holds, so the wiring survives an in-place load (LoadPark replaces the economy).
		Park.EventRaised += item =>
		{
			if ( item.Kind is ParkEventKind.ParkOpened or ParkEventKind.ParkClosed )
				Gate?.DoorMoved( item.Kind == ParkEventKind.ParkClosed );
			else if ( item.Kind == ParkEventKind.Bankrupt )
				Gate?.ParkLost();
			else if ( item.Kind == ParkEventKind.ResearchAllDone )
				StartResearchFinale();
			else if ( item.Kind == ParkEventKind.RideScriptVariable && Enum.IsDefined( (RideVariables)item.InfoId ) )
				AttractionRuntimeOf( item.InstanceId )?.SetVariable( ((RideVariables)item.InfoId).ToString(), (int)item.Amount );
			else if ( item.Kind == ParkEventKind.RideRepaired )
				OpenAfterRepair( item.InstanceId );
			else if ( item.Kind == ParkEventKind.CheatApplied && (cheatNotes ??= new()).Count < 64 )
				cheatNotes.Add( item );
			else if ( item.Kind is ParkEventKind.UpgradeBought or ParkEventKind.MaintenanceRequested )
				CloseForUpgrade( item.InstanceId, item.Kind == ParkEventKind.UpgradeBought );
			else if ( item.Kind == ParkEventKind.MaintenanceCancelled )
				OpenAfterCancelledCall( item.InstanceId );
			else if ( item.Kind is ParkEventKind.ToiletCleaned or ParkEventKind.UpgradeCancelled )
				OpenAfterClean( item.InstanceId );
			else if ( item.Kind == ParkEventKind.UpgradeCompleted )
			{
				ApplyCompletedCatalogueTier( item.InstanceId );
				// [BIN:TPW-EXE:0x55A100 upgrade effect] the type's UpgradeParticleEffect starts when the upgrade is installed (0x4df8f0)
				AttractionRuntimeOf( item.InstanceId )?.StartObjectEffect( ObjectParticleEffect.Upgrade );
				// [BIN:TPW-EXE:0x55A2E5 upgrade sound] the same routine then plays the global cat_rides event 0xB8 at the footprint middle
				AttractionRuntimeOf( item.InstanceId )?.PlayUpgradeSound();
				HangUpgradeBalloon( item.InstanceId, (int)item.Amount );
				OpenAfterRepair( item.InstanceId );
			}
		};
	}

	/// <summary>
	/// An installed upgrade replaces the ride's balloon sprite: level 1 or 2 hangs a new one over the ride's front cell, level 3 none (tp.exe 0x4DF8F0, <see cref="UpgradeBalloonRenderer"/>).
	/// </summary>
	private void HangUpgradeBalloon( int instance, int level )
	{
		if ( UpgradeBalloons == null )
			return;
		OriginalObject? placed = null;
		foreach ( var (item, linked) in economyInstances )
			if ( linked == instance )
				placed = item;
		var visitors = placed?.Runtime.Visitors;
		if ( placed == null || visitors is not { HasCells: true } )
		{
			UpgradeBalloons.Completed( instance, 0, (0, 0), () => false );
			return;
		}
		UpgradeBalloons.Completed( instance, level, visitors.QueueFrontCell ?? visitors.EntranceCell, () => !placed.IsDeleted );
	}

	/// <summary>The park door over the current economy; rebuilt by <see cref="LoadPark"/>, which replaces the economy.</summary>
	private void BuildParkGate()
	{
		BindResearchFinale();
		Gate = ParkGate.ForLevel( Park!.Economy, Objects, Guests, () => Park.Economy.Staff.Members, () => PlacedRide?.Runtime, EconomyStateOf );
		// A park that starts open commands its gate open once at setup, the way the original's gate lookup
		// writes the command from mParkClosed (Mac 0x104500 writes the gate handle and the closed flag together);
		// the initial OpenPark of AttachGuests (an imported park saved open) raises its event before this wiring, so it commands nothing.
		Gate.InitializeFromParkState();
		if ( Guests != null )
		{
			var gate = Gate;
			Guests.EntranceGateOpen = () => gate.GateStatusIsOpen;
		}
	}

	/// <summary>
	/// A purchased upgrade puts the ride in state 2 (<c>0xdf928</c> → <c>SetState(2)</c>): it closes until the upgrade is installed, which the reopening guard
	/// enforces too (<see cref="RideOpening.MayOpen"/>). Without this the ride kept taking guests while its wear was stopped for the upgrade.
	/// </summary>
	// [BIN:STP-PPC:0x100DF928 upgrade start] the upgrade start writes state 2, which clears the open word +100 and writes VAR_RIDECLOSED = 1 (0xe0c3c, ORACLE.md F27)
	// [BIN:TPW-EXE:0x004E0050 upgrade request's model call] the upgrade request's SetState(2) closes with 0x00454550(1) (0x004E101F) and then calls 0x00454550(8) (0x004E0098): the hoarding shows the upgrade set; the maintenance request 0x004DFE30(1) only closes, with mode 1 (0x004DFEED)
	private void CloseForUpgrade( int instance, bool upgrade )
	{
		if ( AttractionRuntimeOf( instance ) is not { } runtime )
			return;
		runtime.Close();
		if ( upgrade )
			runtime.Hoarding.SetState( ObjectHoardingState.UpgradeMode );
		SyncOpenState( runtime );
	}

	/// <summary>The economy record takes the script's open state at once, so a park save before the next tick keeps it (SyncObjectEconomy does it each tick).</summary>
	private void SyncOpenState( OriginalObjectRuntime runtime )
	{
		if ( EconomyStateOf( runtime ) is { } state )
			state.IsOpen = runtime.IsOpen;
	}

	/// <summary>The attraction runtime linked to an economy instance (a placed object or the prototype ride), or null.</summary>
	private OriginalObjectRuntime? AttractionRuntimeOf( int instance )
	{
		OriginalObjectRuntime? runtime = null;
		foreach ( var (item, linked) in economyInstances )
			if ( linked == instance )
				runtime = item.Runtime;
		runtime ??= prototypeInstance == instance ? PlacedRide?.Runtime : null;
		return runtime is { IsAttraction: true } ? runtime : null;
	}

	/// <summary>The economy record behind a placed object's runtime (or the prototype ride's), or null.</summary>
	private ParkObjectState? EconomyStateOf( OriginalObjectRuntime runtime )
	{
		if ( Park == null )
			return null;
		foreach ( var (item, instance) in economyInstances )
			if ( item.Runtime == runtime )
				return Park.Economy.TryGetObject( instance, out var state ) ? state : null;
		return prototypeInstance is int id && PlacedRide?.Runtime == runtime && Park.Economy.TryGetObject( id, out var prototype ) ? prototype : null;
	}

	/// <summary>
	/// A finished repair opens the ride whatever the reopening guard says (<c>0xdef2c</c>: the open word 1, VAR_RIDECLOSED 0, SetState(0); it only
	/// logs a warning when <c>0xde654</c> refuses), except that a coaster without a complete circuit is closed again (ORACLE.md F27).
	/// </summary>
	// [BIN:STP-PPC:0x100DF4D4 repair opens the ride] 0xdf4a0 open word = 1, 0xdf4c8 VAR_RIDECLOSED = 0, SetState(0); 0xdf4dc closes an incomplete coaster again
	// [BIN:STP-PPC:0x100DF928 upgrade end] the upgrade's end leaves state 2 through SetState(0) like the repair (ORACLE.md F27, static): the same reopening, an incomplete coaster closed again
	/// <summary>A cancelled call for a mechanic reopens the ride unless its state keeps it closed (tp.exe <c>0x004DFE30</c> off, the reopen guard).</summary>
	private void OpenAfterCancelledCall( int instance )
	{
		if ( AttractionRuntimeOf( instance ) is { } runtime && Gate?.MayOpen( runtime ) == true )
		{
			runtime.Open();
			SyncOpenState( runtime );
		}
	}

	/// <summary>
	/// A handyman's finished clean opens the toilet whatever the reopening guard says, called or not, the park open or closed (tp.exe <c>0x004D73C0</c>
	/// state 11: the clean <c>0x004DFD80</c>, then <c>0x004DF390</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x004DF390 open after clean] the guard's refusal (states 1, 4, 2, the call +0x64, the queue link 0x004DE4A0, a broken track) only logs "Opening non-openable" five times; it then writes the open word +0x68 = 1, script variable 6 (VAR_RIDECLOSED) = 0 and SetState(0) (0x004E0E60)
	private void OpenAfterClean( int instance )
	{
		if ( AttractionRuntimeOf( instance ) is not { } runtime )
			return;
		runtime.Open();
		SyncOpenState( runtime );
	}

	// [BIN:TPW-EXE:0x4df8f0 reopen tail] after a repair or an upgrade the ride opens (+0x68 = 1, script variable 6 = 0) whether or not its guard holds (it then logs "Opening non openable ride" five times), and a coaster (track type 3) failing 0x441970 closes again (+0x68 = 0, variable 6 = 1)
	private void OpenAfterRepair( int instance )
	{
		if ( AttractionRuntimeOf( instance ) is not { } runtime )
			return;
		var coaster = runtime.Coaster != null;
		if ( RideRepair.OpensAfterRepair( coaster ? RideOpening.CoasterTrackType : 0, !coaster || runtime.Coaster!.AcceptsGuests ) )
			runtime.Open();
		else
		{
			// [BIN:TPW-EXE:0x004DFC95 repair tail's model calls] the tail opens first (0x004547C0: the hoarding sinks) and its re-close then calls 0x00454550(1) (0x004DFD65), which shows the closed set over a sinking one
			runtime.Hoarding.StartOpenFade();
			runtime.Close();
		}
	}

	/// <summary>Walkable path cell next to (or nearest to) a footprint; the prototype Totem has no catalog entrance cell yet.</summary>
	internal static (int X, int Y)? FindRideEntrance( GuestPathGrid grid, int minX, int minY, int maxX, int maxY )
	{
		(int X, int Y)? best = null;
		var bestDistance = int.MaxValue;
		for ( var y = 0; y < grid.CountY; y++ )
		{
			for ( var x = 0; x < grid.CountX; x++ )
			{
				if ( !grid.IsWalkable( x, y ) )
					continue;
				var distance = Math.Max( 0, Math.Max( minX - x, x - maxX ) ) + Math.Max( 0, Math.Max( minY - y, y - maxY ) );
				if ( distance < bestDistance )
				{
					bestDistance = distance;
					best = (x, y);
				}
			}
		}
		return best;
	}

	private void RegisterRideWithGuests( PrototypeRide ride )
	{
		if ( Guests == null || OriginalPark == null || !OriginalParkPlacement.TryGetCell( OriginalPark.Heightfield, ride.Position.X, ride.Position.Y, out var x, out var y ) )
			return;
		const int radius = OriginalRideFootprintRadiusCells;
		var entrance = FindRideEntrance( Guests.Grid, x - radius, y - radius, x + radius, y + radius );
		if ( entrance == null )
			return;
		ride.Visitors.EntranceCell = entrance.Value;
		ride.Visitors.ExitCell = entrance.Value;
		ride.Visitors.HasCells = true;
		Guests.Register( ride.Visitors );
		// [EXT:developer-prototype] the prototype has no placement that lays access cells, so the cell a called guest's search walks into (state 13) is laid here the way a build lays an entrance
		var (standX, standY) = GuestSimulation.StandCellOf( ride.Visitors );
		ObjectAccessCells.LayStandCell( Guests.Grid.Cells, standX, standY );
		LinkPrototypeToEconomy( ride );
		Log.Trace( $"{ride.Name}: guests queue and exit at path cell {entrance.Value} (nearest path cell; the prototype has no catalog entrance)." );
	}

	/// <summary>Footprint of the prototype ride in original cells: a square around the clicked cell.</summary>
	public const int OriginalRideFootprintRadiusCells = 2;

	/// <summary>
	/// The published record of a placed object during a version 2 visit (open state, price, state of repair as
	/// the owner left them), matched by Info.Id, anchor and rotation; null outside visits or for unknown objects.
	/// </summary>
	public OpenTPW.Online.Packages.ParkObjectRecord? PublishedObjectState( OriginalObject item )
	{
		if ( Visit?.Payload.Objects == null || item.Entry.IsFixedItem )
			return null;
		return Visit.Payload.Objects.FirstOrDefault( record =>
			record.InfoId == item.Entry.InfoId && record.X == item.Placement.X && record.Y == item.Placement.Y && record.Rotation == item.Placement.Rotation );
	}

	/// <summary>The park's published statistics of a version 2 package, for read-only display; null otherwise.</summary>
	public OpenTPW.Online.Packages.ParkStatsRecord? PublishedParkStats => Visit?.Payload.Stats;

	public bool TryGetPlacementPosition( Vector2 mousePosition, Vector2 viewportSize, out Vector3 position )
	{
		position = Vector3.Zero;
		if ( OriginalPark == null )
		{
			var found = ParkPlacement.TryGetPosition( mousePosition, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out var sandboxPosition );
			position = sandboxPosition;
			return found;
		}
		if ( !TryGetGroundHit( mousePosition, viewportSize, out var ground ) )
			return false;
		if ( !OriginalParkPlacement.TryGetCell( OriginalPark.Heightfield, ground.X, ground.Y, out var x, out var y ) )
			return false;
		position = OriginalParkPlacement.GetCellCenter( OriginalPark.Heightfield, x, y );
		return true;
	}

	public OriginalPlacementResult CheckOriginalPlacement( Vector3 position )
	{
		if ( OriginalPark == null || !OriginalParkPlacement.TryGetCell( OriginalPark.Heightfield, position.X, position.Y, out var x, out var y ) )
			return OriginalPlacementResult.OutsideTerrain;
		const int radius = OriginalRideFootprintRadiusCells;
		return OriginalParkPlacement.Check( OriginalPark, x - radius, y - radius, x + radius, y + radius );
	}

	private void SetupHud( OnlineFolders? folders )
	{
		Hud = new();

		parkLayout = new ParkLayout( this );
		// [EXT:extension-online-gate] the online folder and the developer online panel exist only while the online extension is on
		onlineFolders = FeatureGates.Online ? folders ?? OnlineFolders.FromEnvironment() : null;
		TextOverlay = new SandboxTextOverlay();
		// BF4 UI draws at output size after the world blit, not through the world upscaler.
		global::Global.Render.OnOverlayRender += DrawTextOverlay;
	}

	private void DrawTextOverlay()
	{
		// the ride view hides the whole HUD, the developer panels included (the camcorder shows only the viewfinder, the eject button and the advisor)
		if ( ShowDeveloperPanels && !HidesInterface )
			TextOverlay.Draw();
	}

	/// <summary>Stops drawing this level's overlay (the front end replaces the level).</summary>
	internal void DetachOverlay() => global::Global.Render.OnOverlayRender -= DrawTextOverlay;

	public void Update()
	{
		// the ride ends when it is deleted or loses what the view rides on (a coaster's train); the winning camera rides End and the loss camera the gate, which are no rides, until they go
		if ( RideView is { } riding && (riding.Ride.IsDeleted || !RideCameraMode.CanRide( riding.Ride ) && !ParkFinale.IsFinaleObject( riding.Ride ) && !IsGateObject( riding.Ride )) )
			ExitRideView();
		Camera.Update();
		if ( ShowDeveloperPanels && !HidesInterface )
			parkLayout.Draw();
		if ( ShowDeveloperPanels && !HidesInterface && onlineFolders != null )
		{
			onlinePanel ??= new OnlinePanel( this, onlineFolders );
			onlinePanel.Draw();
		}
		UpdateWorldInput( Input.Mouse.Left, UiCapturesMouse || ImGuiNET.ImGui.GetIO().WantCaptureMouse,
			Input.Mouse.Position, new Vector2( Screen.Size.X, Screen.Size.Y ), Input.ModifierMaskDown );
		AdvanceFrame( Time.Delta );
		// Riding follows the frame just simulated; park-camera input is consumed only by the earlier update.
		if ( RideView != null && ReferenceEquals( Camera.Mode, RideView ) )
			Camera.Update();
	}

	public bool PlaceRide( Vector3 position )
	{
		if ( IsReadOnlyVisit )
		{
			LastActionMessage = "Read-only visit: building is disabled.";
			return false;
		}
		if ( PlacedRide != null )
			return false;
		if ( OriginalPark != null )
		{
			var result = CheckOriginalPlacement( position );
			if ( result != OriginalPlacementResult.Allowed )
			{
				LastActionMessage = $"Cannot build here: {result}.";
				return false;
			}
		}
		else if ( !ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius ) )
			return false;
		if ( PrototypeOverlapsObjects( position ) )
		{
			LastActionMessage = "Cannot build here: Occupied.";
			return false;
		}
		PlacedRide = new PrototypeRide( position, Objects.ScriptWorld );
		RegisterRideWithGuests( PlacedRide );
		IsPlacing = false;
		Log.Trace( $"Placed prototype ride at {position}; it runs its original Totem.RSE script." );
		return true;
	}

	public void RemoveRide()
	{
		if ( IsReadOnlyVisit )
			return;
		if ( ReferenceEquals( RideView?.Ride, PlacedRide ) && RideView != null )
			ExitRideView();
		PlacedRide?.Delete();
		if ( PlacedRide != null )
		{
			// the cell its registration laid for the called guests' walk goes with it
			if ( Guests != null && PlacedRide.Visitors.HasCells )
			{
				var (standX, standY) = GuestSimulation.StandCellOf( PlacedRide.Visitors );
				ObjectAccessCells.ClearStandCell( Guests.Grid.Cells, standX, standY );
			}
			Guests?.Unregister( PlacedRide.Visitors );
			RemovePrototypeFromEconomy( PlacedRide );
		}
		PlacedRide = null;
		IsPlacing = false;
	}

	/// <summary>
	/// The park's live attractions for the All Attractions window: every placed attraction plus the developer
	/// prototype ride, which lives beside <see cref="Objects"/> rather than inside it. The two sources share the
	/// script world's attraction ids, so this is where they merge into one row each without duplicates.
	/// </summary>
	// [EXT:developer-prototype] the original window had no developer prototype to list; the Totem joins the placed rides as one more row
	public IReadOnlyList<OriginalObject> ListAttractions() =>
		Objects.Objects.Where( item => !item.IsDeleted && item.Runtime.IsAttraction )
			.Append( PlacedRide )
			.Where( item => item is { IsDeleted: false, Runtime.IsAttraction: true } )
			.DistinctBy( item => item.Runtime.AttractionId )
			.ToArray();

	/// <summary>
	/// Removes one listed attraction the way it was placed: a catalogue object through the object world (which
	/// sells its economy record and unlinks guest payments), the prototype ride through <see cref="RemoveRide"/>
	/// (ride view, guests, script and economy stay consistent — <see cref="ParkObjects.Remove"/> does not own it).
	/// Read-only visits refuse here and change nothing. Returns true when the park no longer holds the item.
	/// </summary>
	public bool RemoveAttraction( OriginalObject item )
	{
		if ( IsReadOnlyVisit )
			return false;
		if ( PlacedRide != null && ReferenceEquals( item, PlacedRide ) )
		{
			RemoveRide();
			return true;
		}
		return !item.IsDeleted && Objects.Remove( item );
	}

	public void SaveSandbox()
	{
		RequireSandbox();
		var state = PlacedRide == null ? SandboxSave.CreateEmpty() : new SandboxSave(
			SandboxSave.CurrentVersion, PlacedRide.Position.X, PlacedRide.Position.Y, true, PlacedRide.IsOpen );
		SandboxSave.Save( SaveFileSystem.GetAbsolutePath( "opentpw-sandbox.json" ), state );
		LastActionMessage = "Sandbox saved. This is not an original TPWS save.";
	}

	public void LoadSandbox()
	{
		RequireSandbox();
		var state = SandboxSave.Load( SaveFileSystem.GetAbsolutePath( "opentpw-sandbox.json" ) );
		var position = new Vector3( state.RideX, state.RideY, 0 );
		if ( state.HasRide && !ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius ) )
			throw new InvalidDataException( "Saved ride footprint is outside the park." );
		var replacement = state.HasRide ? new PrototypeRide( position, Objects.ScriptWorld ) : null;
		if ( state.IsRunning )
			replacement!.Start();
		RemoveRide();
		PlacedRide = replacement;
		simulationClock.Reset();
		LastActionMessage = "Sandbox loaded; the ride script restarts from its first instruction.";
	}

	/// <summary>What the park save, load and canonical hash read from this level.</summary>
	public ParkWorldStreams Streams => new( Seed, Park, Guests, Objects.ScriptWorld, GameAudio.Events );

	/// <summary>The random streams besides the economy's, for the park save.</summary>
	public WorldRandomState CaptureRandomState() => Streams.CaptureRandomState();

	/// <summary>Continues the streams of a park save.</summary>
	public void RestoreRandomState( WorldRandomState state ) => Streams.RestoreRandomState( state );

	/// <summary>Canonical state hash of this level (<see cref="WorldStateHash"/>; the sound seed is not part of it).</summary>
	public ulong ComputeStateHash() => Streams.ComputeStateHash();

	/// <summary>Writes the park economy and every random stream to an OpenTPW park save.</summary>
	public void SavePark( string path )
	{
		StaffPlacement?.PrepareForSave();
		Streams.SavePark( path );
	}

	/// <summary>Loads an OpenTPW park save: the economy and, when the save has them, the random streams. Guests and object scripts keep running (the legacy economy-only save, [EXT:legacy-economy-save]).</summary>
	public void LoadPark( string path )
	{
		// a load that fails leaves the economy, and so the staff tool and its pending flag, as they were
		var replacedStaff = Park?.Economy.Staff.Members.Select( member => member.ThingId ).Where( id => id != 0 ).ToList() ?? new List<int>();
		Streams.LoadPark( path );
		// the loaded economy's staff take their things (a member the save names keeps its slot); the replaced economy's staff are destroyed at the turn's end
		if ( Park != null )
		{
			Park.Economy.Staff.AttachThings( Things );
			var kept = Park.Economy.Staff.Members.Select( member => member.ThingId ).ToHashSet();
			foreach ( var id in replacedStaff.Where( id => !kept.Contains( id ) ) )
				Things.QueueDestroy( id );
		}
		StaffPlacement?.Abandon();
		// the employees in the players' hands belong to the replaced economy
		StaffHands?.Clear();
		// the coaster edits hold the replaced park's builders
		coasterEdits?.Clear();
		UpgradeBalloons?.Clear();
		// the patrol tool's employee id belongs to the replaced economy
		PatrolTool?.Cancel();
		BuyLandTool?.Cancel();
		AreaClearTool?.Cancel();
		PoolTool?.Cancel();
		AttachStaffWorld();
		AttachAdvisorProbes();
		AttachCoasterChallenges();
		// The gate holds the economy it was built over; the load replaced it.
		if ( Park != null && Gate != null )
			BuildParkGate();
		// The saved open state goes back into the running scripts; SyncObjectEconomy would otherwise overwrite it with the live one on the next tick (park points count only open rides, ARRIVAL-003).
		foreach ( var (item, instance) in economyInstances )
		{
			// the replaced economy's environment grid holds the litter and the staff rows only: the standing objects put theirs back
			Park?.Economy.PlaceObjectEffects( instance, (item.Placement.X, item.Placement.Y) );
			if ( Park == null || !Park.Economy.TryGetObject( instance, out var state ) || state.IsOpen == item.IsOpen )
				continue;
			if ( state.IsOpen )
				item.Open();
			else
				item.Close();
		}
	}

	private void RequireSandbox()
	{
		if ( IsReadOnlyVisit )
			throw new InvalidOperationException( "Saving and loading are disabled while visiting a shared park." );
		if ( OriginalPark != null )
			throw new InvalidOperationException( "Sandbox saves are disabled for imported original levels; original saves are read-only." );
	}

	public void Dispose()
	{
		// [EXT:online-coop-lockstep] a park that closes (a load, a restart, a replaced park, leaving it) ends its co-op session for this game
		CoopSession?.Dispose();
		Guests?.ClearBodySprites();
		rideBodyPoses = null;
		ExitRideView();
		ExitFreeWalk();
		onlinePanel?.Dispose();
		foreach ( var renderer in trackRenderers.Values )
			renderer.Dispose();
		trackRenderers.Clear();
		tourRenderer?.Dispose();
		tourRenderer = null;
		DisposeSky();
		DetachOverlay();
		TextOverlay?.Dispose();
		// a scene built without its constructor (the park-build tests make one) has no cache
		HoardingCache?.Clear();
	}

	/// <summary>Invalidates per-world-pass observations even when an entity is absent from the render list.</summary>
	internal long RenderPass { get; private set; }

	public void Render()
	{
		RenderPass++;
		// [EXT:point-lights] the lights of the objects that have any, for every draw of this frame
		var viewProjection = Camera.ViewMatrix * Camera.ProjMatrix;
		PointLights?.Gather( viewProjection );
		var snapshot = WeatherAfterParticles( Entity.All.ToArray() );
		var before = lateBefore ??= new List<Entity>();
		var after = lateAfter ??= new List<Entity>();
		SplitLate( snapshot, before, after );
		// the translucent pass: the entities hand the translucent faces of their models over while they draw (ModelEntity), and the pass draws them after all of these, far to near
		WorldPass.Begin( viewProjection );
		try
		{
			foreach ( var entity in before )
				RenderEntity( entity );
		}
		catch
		{
			WorldPass.Abort();
			throw;
		}
		WorldPass.Flush();
		foreach ( var entity in after )
			RenderEntity( entity );
	}

	private static void RenderEntity( Entity entity )
	{
		if ( !FrameMarks.Enabled )
		{
			entity.Render();
			return;
		}
		using ( FrameMarks.Measure( RenderMarkName( entity.GetType() ) ) )
			entity.Render();
	}

	private static readonly Dictionary<Type, string> renderMarkNames = new();

	/// <summary>The frame mark of an entity type's draws ("draw:GuestRenderer"), for the browser event-hitch harness.</summary>
	private static string RenderMarkName( Type type )
	{
		if ( !renderMarkNames.TryGetValue( type, out var name ) )
			renderMarkNames[type] = name = "draw:" + type.Name;
		return name;
	}
}

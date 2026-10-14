using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OpenTPW.FrontEnd;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// The native park-resume smoke test: one real GPU process builds a fixed-ride Jungle park through the user paths
/// (the HUD build menu for one ride, the authoritative builders the tools commit through for the rest), seeds real
/// guests into queues and onto rides, saves through the actual HUD save handler, and records the SHA-256 of every
/// snapshot section per continuation tick. A second, fresh process loads the same file through the actual menu load
/// entry and must reproduce the boundary and every tick exactly, reject each corrupt file without disturbing the live
/// park, and pass the post-load gameplay checks (open/close, admissions, sale, staff wages, payment bindings).
/// The opt-in prototype variant (<see cref="PrototypeVariant"/>, <c>OPENTPW_SMOKE_PROTOTYPE=1</c> in both phases)
/// adds the developer Totem to the fixture and its own probes and post-load checks; the default pair is unchanged.
/// Root invokes <see cref="Run"/> from the <c>--park-resume-smoke</c> CLI hook after the flow is created and before
/// the normal update/render hooks and the intro branch; the harness then owns the loop: it registers its own update
/// (the only simulation driver — controlled 1/60 advances through <see cref="Level.AdvanceSimulation"/>) and the real
/// <see cref="GameFlow.Render"/> for draw verification, runs <see cref="Renderer.Run"/> itself and closes the window
/// when done. Run with <c>--mute --no-advisor</c> for a controlled clock; active screams and spoken advice cannot be
/// saved and fail the capture with the production reason.
/// </summary>
internal sealed partial class ParkResumeSmokeTest : IDisposable
{
	/// <summary>Continuation ticks compared between the uninterrupted and the restarted park (10 simulated seconds).</summary>
	private const int ContinuationTickCount = 600;
	/// <summary>Budget for queueing, boarding and riding to appear naturally before the save (90 simulated seconds).</summary>
	private const int MaximumPopulateTicks = 90 * FixedStepClock.TicksPerSecond;
	private const int PopulateAdvancesPerFrame = 8;
	private const int EvidenceAdvancesPerFrame = 4;
	/// <summary>Render warm-up before the first GPU readback: streamed meshes and textures settle over the first frames.</summary>
	private const int WarmUpFrames = 45;
	/// <summary>One-shot watchdog (the sandbox smoke test's pattern): a run that stops getting frames fails, not hangs.</summary>
	private const double MaximumSeconds = 900;

	/// <summary>
	/// The explicit test-fixture option (both phases must set it): <c>OPENTPW_SMOKE_PROTOTYPE=1</c> adds the developer
	/// Totem to the fixture park — the native prototype-resume variant. The default fixture is unchanged.
	/// </summary>
	internal static bool PrototypeVariant => Environment.GetEnvironmentVariable( "OPENTPW_SMOKE_PROTOTYPE" ) is "1" or "true";

	private readonly GameFlow flow;
	private readonly string savePath;
	private readonly string evidenceDirectory;
	private readonly bool restoring;
	private readonly BaseFileSystem originalSaveFileSystem;
	private readonly System.Threading.Timer watchdog;
	private readonly List<SmokeSample> tickSamples = new();
	private readonly List<SmokeProbe> probeResults = new();
	private readonly List<string> capturedPngs = new();

	private Level level = null!;
	/// <summary>The capture phase's evidence — the restore phase's oracle.</summary>
	private SmokeEvidence? capture;
	private SmokeSample boundarySample = new() { Tick = -1 };
	private IReadOnlyDictionary<string, string>? verifiedAssets;
	/// <summary>The Totem's identity as this phase's save holds it (set at save time; the restore phase reads it before its post-load removal).</summary>
	private SmokePrototype? savedPrototype;
	private string savedUtc = "";
	private string themeText = "jungle";
	private string startKindText = "";
	private SmokePostLoad? postLoad;
	private bool completed;

	private enum Stage { WarmUp, Build, Populate, Save, Load, Boundary, Probes, Continuation, PostLoad, Done }

	private Stage stage;
	private int frame;
	private int populateTicks;
	private int continuationTick;
	/// <summary>A readback scheduled for a later frame: a freshly (re)loaded world needs frames before its detail draws.</summary>
	private string? pendingCapture;
	private int captureAtFrame;

	private ParkResumeSmokeTest( GameFlow flow, string phase, string savePath, string evidenceDirectory )
	{
		this.flow = flow;
		this.savePath = Path.GetFullPath( savePath );
		this.evidenceDirectory = evidenceDirectory;
		restoring = string.Equals( phase, "restore", StringComparison.Ordinal );
		// Park saves stay inside the run's evidence directory: the actual handlers write and read, but a smoke run
		// never touches the player's save location (the sandbox smoke test's isolation pattern).
		originalSaveFileSystem = SaveFileSystem;
		SaveFileSystem = new BaseFileSystem( Path.Combine( evidenceDirectory, restoring ? "saves-restore" : "saves" ) );
		watchdog = new System.Threading.Timer( _ =>
		{
			Console.Error.WriteLine( $"Native park-resume smoke test failed: timed out after {MaximumSeconds} s in stage {stage} at frame {frame}; display {Render.Metrics}, {Render.Scaling.Describe()}" );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( MaximumSeconds ), System.Threading.Timeout.InfiniteTimeSpan );
		if ( restoring )
		{
			capture = ReadEvidence( Path.Combine( evidenceDirectory, "capture-evidence.json" ) );
			Require( capture.Phase == "capture" && capture.Completed && capture.Failure == null, "read the completed capture phase's evidence as the oracle" );
			themeText = capture.Theme;
			startKindText = capture.StartKind;
			stage = Stage.WarmUp;
		}
		else
		{
			// A full-simulation park starts empty: the shipped Easymode save (which includes coasters) is not the
			// fixture — every object, path and queue is built by this run, so all of it is saveable fixed-ride state.
			level = flow.StartLevel( "jungle", original: true, developerPanels: false, start: ParkStartKind.FullSimulation );
			Require( level.Park != null && level.Guests != null && level.Paths != null && level.OriginalPark != null, "start a writable original Jungle park in full-simulation mode" );
			Require( level.PlacedRide == null && level.Coasters.Count == 0, "the full-simulation park starts without prototype rides or coasters" );
			Require( level.Guests.Grid.WalkableCount > 0, "the fresh park starts with its initial walkable entrance path" );
			themeText = level.LevelName;
			startKindText = ParkStartKind.FullSimulation.ToString();
			stage = Stage.WarmUp;
		}
	}

	/// <summary>Root's CLI hook: <c>--park-resume-smoke capture|restore</c> with <c>--park-resume-file</c> and <c>--park-resume-evidence</c>.</summary>
	internal static void Run( GameFlow flow, string phase, string savePath, string evidenceDirectory )
	{
		ArgumentNullException.ThrowIfNull( flow );
		ArgumentException.ThrowIfNullOrWhiteSpace( savePath );
		ArgumentException.ThrowIfNullOrWhiteSpace( evidenceDirectory );
		if ( !string.Equals( phase, "capture", StringComparison.Ordinal ) && !string.Equals( phase, "restore", StringComparison.Ordinal ) )
			throw new ArgumentException( $"--park-resume-smoke expects 'capture' or 'restore', not '{phase}'." );
		var extension = Path.GetExtension( savePath );
		if ( !extension.Equals( ".json", StringComparison.OrdinalIgnoreCase ) && !extension.Equals( ".otpw", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( $"--park-resume-file expects a .json or .otpw world save, not '{extension}'." );
		Directory.CreateDirectory( evidenceDirectory );
		using var test = new ParkResumeSmokeTest( flow, phase, savePath, evidenceDirectory );
		Render.CaptureOutput = true;
		Render.OnUpdate += test.Update;
		Render.OnRender += flow.Render;
		Render.Run();
		Render.OnUpdate -= test.Update;
		Render.OnRender -= flow.Render;
		test.VerifyCompleted();
	}

	/// <summary>One frame: exactly one stage step, so every mutation happens between frames, never mid-render.</summary>
	private void Update()
	{
		++frame;
		try
		{
			// The harness owns the update path flow.Update normally drives; the camera still needs its per-frame
			// update for the view/projection the world pass draws with (the visit smoke test drives it the same way).
			Camera.Update();
			if ( pendingCapture is { } capture && frame >= captureAtFrame )
			{
				pendingCapture = null;
				CaptureFrame( capture );
			}
			switch ( stage )
			{
				case Stage.WarmUp: WarmUp(); break;
				case Stage.Build: BuildFixture(); break;
				case Stage.Populate: PopulateStep(); break;
				case Stage.Save: SavePark(); break;
				case Stage.Load: LoadSavedPark(); break;
				case Stage.Boundary: VerifyBoundary(); break;
				case Stage.Probes: RunCorruptProbes(); break;
				case Stage.Continuation: ContinuationStep(); break;
				case Stage.PostLoad: VerifyPostLoad(); break;
				case Stage.Done: break;
			}
		}
		catch ( Exception error )
		{
			Fail( error );
		}
	}

	private void WarmUp()
	{
		if ( frame < WarmUpFrames )
			return;
		if ( restoring )
		{
			var saved = File.ReadAllBytes( savePath );
			Require( saved.Length > 0 && Sha256Hex( saved ) == capture!.Save.Sha256,
				$"the save file's bytes survive the restart ({saved.Length} bytes, sha {Sha256Hex( saved )[^12..]}, expected {capture.Save.Sha256.Substring( capture.Save.Sha256.Length - 12 )})" );
			stage = Stage.Load;
		}
		else
		{
			CaptureFrame( "park-resume-capture-empty.png" );
			stage = Stage.Build;
		}
	}

	// ---- Capture phase ---------------------------------------------------------------------------

	private void SavePark()
	{
		boundarySample = CaptureSample( 0 );
		savedWater = WaterCounts();
		// The actual HUD save handler (GameFlow.Resume.cs's SaveCurrentPark), writing the own-format world save.
		flow.SaveCurrentPark();
		var written = SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" );
		Require( File.Exists( written ), "the HUD save handler wrote opentpw-jungle-world.json" );
		if ( !string.Equals( Path.GetFullPath( written ), savePath, StringComparison.Ordinal ) )
			File.Copy( written, savePath, overwrite: true );
		var bytes = File.ReadAllBytes( savePath );
		savedUtc = ParkResumeSaveFile.Load( savePath ).SavedUtc.UtcDateTime.ToString( "O", System.Globalization.CultureInfo.InvariantCulture );
		Require( bytes.SequenceEqual( ParkResumeSaveFile.Serialize( ParkResumeSaveFile.Load( savePath ) ) ),
			"the saved file round-trips byte-identically through the own codec" );
		savedPrototype = SmokePrototypeOf( ParkResumeSaveFile.Load( savePath ).Prototype, level.PlacedRide );
		Log.Trace( $"Park-resume capture: saved {bytes.Length} bytes ({boundarySample.Counters.Objects} objects, {boundarySample.Counters.Guests} guests, "
			+ $"{boundarySample.Counters.QueuedGuests} queued, {boundarySample.Counters.Riders} riding, balance ${boundarySample.Counters.Balance})." );
		CaptureFrame( "park-resume-capture-saved.png" );
		stage = Stage.Continuation;
	}

	private void ContinuationStep()
	{
		for ( var step = 0; step < EvidenceAdvancesPerFrame && continuationTick < ContinuationTickCount; step++ )
		{
			level.AdvanceSimulation( FixedStepClock.TickDuration );
			continuationTick++;
			var sample = CaptureSample( continuationTick );
			tickSamples.Add( sample );
			if ( restoring )
				RequireSampleMatches( capture!.Ticks[continuationTick - 1], sample, $"continuation tick {continuationTick}" );
			if ( !restoring && continuationTick == ContinuationTickCount / 2 )
				CaptureFrame( "park-resume-capture-mid.png" );
		}
		if ( continuationTick < ContinuationTickCount )
			return;
		if ( restoring )
		{
			stage = Stage.PostLoad;
			return;
		}
		CaptureFrame( "park-resume-capture-end.png" );
		Complete( "Native park-resume smoke capture passed: built park saved through the HUD handler; "
			+ $"{ContinuationTickCount} continuation ticks hashed ({tickSamples.Count} samples, {boundarySample.Counters.Guests} guests at save)." );
	}

	// ---- Restore phase ---------------------------------------------------------------------------

	private void LoadSavedPark()
	{
		// The actual menu load entry: the same ParkLoadEntry the load screen builds for an own-format save.
		flow.LoadPark( new ParkLoadEntry( themeText, -1, false, savePath, "park-resume smoke" ) );
		level = flow.Level ?? throw new InvalidOperationException( "Native park-resume smoke test failed: the menu load left no level." );
		Require( level.Park != null && level.Guests != null && level.Paths != null && level.OriginalPark != null, "the resumed park has its economy, guests, paths and original park" );
		Require( ReferenceEquals( Level.Current, level ), "the resumed level is the current level" );
		Require( level.Coasters.Count == 0, "the resumed park carries no coaster state" );
		VerifyWaterRestored();
		VerifyBumperRestored();
		if ( PrototypeVariant )
		{
			var totem = level.PlacedRide;
			Require( totem != null && totem.Visitors.HasCells, "the resumed park rebuilds the developer Totem with its saved guest cells" );
			Require( capture!.Prototype is { } saved && totem!.Visitors.AttractionId == saved.AttractionId,
				"the resumed Totem adopts the saved attraction id" );
			Require( level.Park.Guests!.TryGetInstance( totem.Visitors.AttractionId, out var instance ) && instance == capture.Prototype!.EconomyInstanceId,
				"the resumed Totem is re-linked to its saved economy instance, never bought again" );
		}
		else
			Require( level.PlacedRide == null, "the resumed park carries no prototype ride" );
		Require( flow.StartKind?.ToString() == startKindText, $"the flow's start kind is the saved one ({flow.StartKind} vs {startKindText})" );
		savedPrototype = SmokePrototypeOf( ParkResumeSaveFile.Load( savePath ).Prototype, level.PlacedRide );
		Log.Trace( $"Park-resume restore: loaded '{Path.GetFileName( savePath )}' through the menu entry ({level.Objects.Objects.Count} objects, {level.Guests.Guests.Count} guests)." );
		pendingCapture = "park-resume-restored.png";
		captureAtFrame = frame + WarmUpFrames;
		stage = Stage.Boundary;
	}

	/// <summary>First recapture proves the manifest recomputes identically; the later ones carry it as the verified manifest.</summary>
	private void VerifyBoundary()
	{
		var manifestProbe = level.CaptureResumeSnapshot( Advisor, "park-resume smoke" );
		Require( CanonicalManifestSha256( manifestProbe.AssetHashes ) == capture!.ManifestSha256,
			"the trusted asset manifest recomputes to the capture's (sorted-key hash)" );
		boundarySample = CaptureSample( 0 );
		savedUtc = capture.SavedUtc;
		RequireSampleMatches( capture.Boundary, boundarySample, "boundary" );
		Log.Trace( $"Park-resume restore: boundary matches ({boundarySample.Counters.Objects} objects, {boundarySample.Counters.Guests} guests, "
			+ $"{boundarySample.Counters.QueuedGuests} queued, {boundarySample.Counters.Riders} riding, balance ${boundarySample.Counters.Balance})." );
		stage = Stage.Probes;
	}

	/// <summary>
	/// Corrupt-save candidates, each written from a mutated copy of the real snapshot and loaded through the real
	/// handlers. Rejection alone is not enough: the live resumed park must be provably untouched afterwards.
	/// </summary>
	private void RunCorruptProbes()
	{
		var good = ParkResumeSaveFile.Load( savePath );
		var baseline = CaptureSample( 0 );
		// The rich save's own populated queues and laid queue cells are the probe targets; the rich-state gate
		// guarantees they exist (a named failure is more honest than silently skipping).
		var withQueue = Array.FindIndex( good.Guests.Attractions, attraction => attraction.Queue.Length > 0 );
		Require( withQueue >= 0, "the saved park holds a queued guest to corrupt" );
		var withCells = Array.FindIndex( good.Guests.Attractions, attraction => attraction.QueueCells.Length > 0 );
		Require( withCells >= 0, "the saved park holds a queue cell to corrupt" );
		// A late guest reference inside a queue: passes parsing, dies on the guest cross-checks.
		var attractions = (RideVisitorBridgeSnapshot[]) good.Guests.Attractions.Clone();
		var queue = (int[]) attractions[withQueue].Queue.Clone();
		queue[0] = 9_999_999;
		attractions[withQueue] = attractions[withQueue] with { Queue = queue };
		Probe( baseline, "late-guest-reference", good with { Guests = good.Guests with { Attractions = attractions } }, viaMenu: false );
		// A queue cell outside the saved cell map.
		attractions = (RideVisitorBridgeSnapshot[]) good.Guests.Attractions.Clone();
		var cells = ((int X, int Y)[]) attractions[withCells].QueueCells.Clone();
		cells[0] = (good.Layout.Cells.Width * 4, good.Layout.Cells.Height * 4);
		attractions[withCells] = attractions[withCells] with { QueueCells = cells };
		Probe( baseline, "late-queue-cell", good with { Guests = good.Guests with { Attractions = attractions } }, viaMenu: false );
		// An attraction the park does not place.
		attractions = (RideVisitorBridgeSnapshot[]) good.Guests.Attractions.Clone();
		attractions[0] = attractions[0] with { AttractionId = 1_000_000 };
		Probe( baseline, "late-attraction-reference", good with { Guests = good.Guests with { Attractions = attractions } }, viaMenu: false );
		// An asset fingerprint that no longer matches the trusted data.
		var manifest = new Dictionary<string, string>( good.AssetHashes );
		var key = manifest.Keys.First();
		manifest[key] = manifest[key][..^1] + (manifest[key][^1] == '0' ? '1' : '0');
		Probe( baseline, "asset-fingerprint", good with { AssetHashes = manifest }, viaMenu: false );
		// The shape and manifest still agree; failure occurs after a candidate's real GPU objects exist.
		var wrongScriptIdentities = good with
		{
			Scripts = good.Scripts with
			{
				Scripts = good.Scripts.Scripts.Select( script => script with { ScriptIdentityHash = 1 } ).ToArray()
			},
			Runtimes = good.Runtimes.Select( runtime => runtime.State.HasScript
				? runtime with { State = runtime.State with { ScriptIdentityHash = 1 } } : runtime ).ToArray()
		};
		Probe( baseline, "runtime-script-identity", wrongScriptIdentities, viaMenu: false );
		// The prototype part (variant runs only): dropping it leaves the Totem's root script and guest record
		// unclaimed, and its saved guest cells cannot be swapped for cells outside the map.
		if ( good.Prototype is { } prototype )
		{
			Probe( baseline, "prototype-part-dropped", good with { Prototype = null }, viaMenu: false );
			Probe( baseline, "prototype-cell-off-map", good with { Prototype = prototype with
			{
				EntranceCell = (good.Layout.Cells.Width * 4, good.Layout.Cells.Height * 4),
				ExitCell = (good.Layout.Cells.Width * 4, good.Layout.Cells.Height * 4)
			} }, viaMenu: false );
		}
		// An unsupported future version, through the actual menu entry: a failure dialog, never a crash or a swap.
		Probe( baseline, "unsupported-version", good with { Version = ParkResumeSaveFile.CurrentVersion + 5 }, viaMenu: true );
		// The load menu's own listing: a malformed save file is skipped with a warning, a good one is listed.
		ProbeLoadMenuListing( baseline, good );
		// Active advisor speech is explicitly unsavable state.
		if ( good.Advisor is { } advisor )
			Probe( baseline, "active-advisor-speech", good with { Advisor = advisor with { SpeechWasActive = true } }, viaMenu: false );
		else
			Log.Trace( "Park-resume probes: no advisor in the save (run with --no-advisor), skipping the active-speech probe." );
		// The probes must not have moved the park either: the boundary still recaptures identically.
		RequireSampleMatches( baseline, CaptureSample( 0 ), "after-probes boundary" );
		probeResults.Sort( ( left, right ) => string.CompareOrdinal( left.Name, right.Name ) );
		Log.Trace( $"Park-resume probes: {probeResults.Count} corrupt candidates rejected ({string.Join( ", ", probeResults.Select( item => item.Name ) )}); live park intact." );
		stage = Stage.Continuation;
	}

	private void Probe( SmokeSample baseline, string name, ParkResumeSnapshot corrupted, bool viaMenu )
	{
		var path = Path.Combine( evidenceDirectory, $"corrupt-{name}.json" );
		File.WriteAllBytes( path, ParkResumeJson.Serialize( corrupted ) );
		var before = LiveParkIdentity();
		var cameraBefore = Camera.Mode;
		var stackDepth = flow.Hud?.Stack.Screens.Count ?? 0;
		var rejected = false;
		var message = "";
		if ( viaMenu )
		{
			Exception? escaped = null;
			try
			{
				flow.LoadPark( new ParkLoadEntry( themeText, -1, false, path, "corrupt" ) );
			}
			catch ( Exception error )
			{
				escaped = error;
			}
			Require( escaped == null, $"the menu load reports corrupt '{name}' through its failure dialog, not an escaping {escaped?.GetType().Name}" );
			rejected = ReferenceEquals( flow.Level, level );
			var depth = flow.Hud?.Stack.Screens.Count ?? 0;
			Require( depth > stackDepth, $"the menu load of corrupt '{name}' shows a failure dialog" );
			flow.Hud?.Stack.Pop();
		}
		else
		{
			try
			{
				flow.ResumePark( ParkResumeSaveFile.Load( path ) );
			}
			catch ( Exception error ) when ( error is InvalidDataException or InvalidOperationException or NotSupportedException )
			{
				rejected = true;
				message = error.Message.Split( '\n' )[0];
			}
		}
		Require( rejected, $"corrupt save '{name}' is rejected (via {( viaMenu ? "the menu entry" : "ResumePark" )})" );
		Require( message.Length > 0 || viaMenu, $"corrupt save '{name}' names its rule" );
		var recaptured = CaptureSample( 0 );
		var intact = ReferenceEquals( flow.Level, level ) && ReferenceEquals( Camera.Mode, cameraBefore )
			&& recaptured.FullTypedSha256 == baseline.FullTypedSha256 && LiveParkIdentity() == before;
		Require( intact, $"the rejected '{name}' candidate left the live park untouched (level, state hash, entities, camera, listener, GPU targets)" );
		probeResults.Add( new SmokeProbe { Name = name, Rejected = rejected, Message = message, LiveParkIntact = intact } );
	}

	/// <summary>
	/// The load menu's listing: a malformed world save is skipped with a warning (never listed, never thrown), the
	/// good own-format save is listed under its display name, and the live park is untouched throughout.
	/// </summary>
	private void ProbeLoadMenuListing( SmokeSample baseline, ParkResumeSnapshot good )
	{
		var before = LiveParkIdentity();
		var cameraBefore = Camera.Mode;
		var otherTheme = ObjectCatalog.Themes.First( theme => !string.Equals( theme, themeText, StringComparison.OrdinalIgnoreCase ) );
		var goodName = $"opentpw-{themeText}-world.json";
		var malformedName = $"opentpw-{otherTheme}-world.json";
		var goodSlot = SaveFileSystem.GetAbsolutePath( goodName );
		var malformedSlot = SaveFileSystem.GetAbsolutePath( malformedName );
		File.WriteAllBytes( goodSlot, ParkResumeSaveFile.Serialize( good ) );
		File.WriteAllBytes( malformedSlot, "this is not a world save"u8.ToArray() );
		var entries = flow.FindLoadEntries();
		Require( entries.Any( entry => entry.OwnSavePath != null && string.Equals( Path.GetFileName( entry.OwnSavePath ), goodName, StringComparison.Ordinal ) ),
			"the load menu lists the good own-format save" );
		Require( entries.All( entry => entry.OwnSavePath == null || !string.Equals( Path.GetFileName( entry.OwnSavePath ), malformedName, StringComparison.Ordinal ) ),
			"the load menu skips the malformed world save" );
		var recaptured = CaptureSample( 0 );
		var intact = ReferenceEquals( flow.Level, level ) && ReferenceEquals( Camera.Mode, cameraBefore )
			&& recaptured.FullTypedSha256 == baseline.FullTypedSha256 && LiveParkIdentity() == before;
		Require( intact, "listing the load menu left the live park untouched" );
		probeResults.Add( new SmokeProbe { Name = "load-menu-skips-malformed", Rejected = true,
			Message = $"FindLoadEntries lists {goodName} and skips {malformedName}.", LiveParkIntact = intact } );
		File.Delete( goodSlot );
		File.Delete( malformedSlot );
	}

	/// <summary>The identity a failed candidate must preserve: entity count, camera mode type, audio listener pose, owned GPU targets.</summary>
	private readonly record struct LiveIdentity( int Entities, string CameraMode, float Lx, float Ly, float Lz, float Fx, float Fy, float Fz,
		int OwnedTargets, int Materials, int Shaders );

	private LiveIdentity LiveParkIdentity() => new( Entity.All.Count, Camera.Mode.GetType().Name,
		AudioListener.Position.X, AudioListener.Position.Y, AudioListener.Position.Z,
		AudioListener.Forward.X, AudioListener.Forward.Y, AudioListener.Forward.Z, Render.OwnedTargetResourceCount,
		Asset.All.OfType<Material>().Count(), Asset.All.OfType<Shader>().Count() );

	private void VerifyPostLoad()
	{
		var economy = level.Park!.Economy;
		var payments = level.Park.Guests!;
		// Open/close through the economy; the gate script must follow the command variable.
		economy.OpenPark();
		Require( level.Gate?.Gate?.GetVariableByName( ParkGate.CommandVariable ) == 1, "reopening the resumed park commands the gate open" );
		long Booked( LedgerCategory category ) => economy.Ledger.CurrentTotals.GetValueOrDefault( category ) + economy.Ledger.History.Sum( month => month[category] );
		var gateBefore = Booked( LedgerCategory.GateTakings );
		var admissionsBefore = economy.Counters[ParkCounters.Admissions];
		economy.ClosePark();
		var refused = !economy.TryAdmitVisitor( long.MaxValue, out _ );
		Require( refused && economy.Counters[ParkCounters.Admissions] == admissionsBefore && Booked( LedgerCategory.GateTakings ) == gateBefore,
			"the closed resumed park refuses an admission without booking anything" );
		economy.OpenPark();
		Require( economy.TryAdmitVisitor( economy.EntranceFee, out var feePaid ) && feePaid == economy.EntranceFee
			&& economy.Counters[ParkCounters.Admissions] == admissionsBefore + 1 && Booked( LedgerCategory.GateTakings ) == gateBefore + economy.EntranceFee,
			"one admitted visitor pays exactly one entrance fee into the new economy" );
		// Sell one of the two same-catalogue shops: scrap credited once, links removed exactly.
		var shop = level.Objects.Objects.First( item => item.Entry.Category == ObjectCategory.Shop && !item.IsDeleted
			&& level.GetEconomyInstance( item ) != null && level.Objects.Objects.Count( other => !other.IsDeleted && other.Entry.InfoId == item.Entry.InfoId ) > 1 );
		var instance = level.GetEconomyInstance( shop )!.Value;
		Require( economy.TryGetObject( instance, out var sold ) && payments.TryGetInstance( shop.Visitors.AttractionId, out var paymentInstance ) && paymentInstance == instance,
			"the shop to sell carries its distinct economy and payment instance" );
		var balance = economy.Balance;
		var scrap = economy.ScrapValue( sold! );
		var cell = shop.Cells.First();
		Require( level.RemoveObjectAt( cell.X, cell.Y ), "sell the shop through the removal path" );
		Require( economy.Balance == balance + scrap && !economy.TryGetObject( instance, out _ ),
			$"the sale credits the scrap value exactly once (${balance} + ${scrap} = {economy.Balance})" );
		Require( !payments.TryGetInstance( shop.Visitors.AttractionId, out _ ) && level.GetEconomyInstance( shop ) == null,
			"the sold shop's payment and economy links are removed" );
		// Place a new object after the sale: the allocator still works and the new object binds.
		var feature = level.Objects.Catalog.Buildable.First( entry => entry.Category == ObjectCategory.Feature
			&& economy.Research.IsAvailable( entry.InfoId ) && economy.Balance >= entry.BuildCost );
		var featureSite = FindSite( feature, 0, near: FirstWalkable() );
		var placed = level.PlaceObject( feature, featureSite.X, featureSite.Y, 0 );
		Require( placed != null && level.GetEconomyInstance( placed! ) != null, "a new object placed after the sale binds to the economy" );
		// Hire and pay staff exactly once at month end (the economy-only advance the sandbox smoke test uses).
		var candidate = economy.Staff.Candidates.FirstOrDefault( item => item.Type == StaffType.Mechanic );
		Require( candidate != null, "the staff pool offers a mechanic" );
		economy.Hire( candidate!.Id );
		var wages = economy.Staff.TotalMonthlyWages;
		var afterHire = economy.Balance;
		var monthEnd = ParkCalendar.TickAtMonth( ParkCalendar.MonthIndex( economy.Tick ) + 1 );
		economy.Advance( monthEnd - economy.Tick );
		Require( economy.Tick == monthEnd && economy.Date.Day == 1 && economy.Balance == afterHire - wages,
			$"month-end wages leave the bank account exactly once (${afterHire} - ${wages} = {economy.Balance})" );
		// Every surviving placed attraction binds one attraction to one economy instance.
		var boundInstances = new HashSet<int>();
		foreach ( var item in level.Objects.Objects.Where( item => !item.IsDeleted && item.Runtime.IsAttraction ) )
		{
			var binding = level.GetEconomyInstance( item );
			Require( binding != null && payments.TryGetInstance( item.Visitors.AttractionId, out var payee ) && payee == binding,
				$"{item.Entry.DisplayName} binds its attraction to its own economy instance" );
			Require( boundInstances.Add( binding!.Value ), $"economy instance {binding} is bound to one attraction only" );
		}
		// The prototype variant's own proof: the resumed Totem carries its saved economy link, and removing it sells
		// (or, when the live park never paid, deregisters) exactly once — the saved association, never a re-buy.
		bool? prototypeRemoved = null;
		if ( PrototypeVariant && level.PlacedRide is { } totem )
		{
			var savedTotem = capture!.Prototype;
			var linked = payments.TryGetInstance( totem.Visitors.AttractionId, out var totemInstance );
			Require( savedTotem != null && linked && totemInstance == savedTotem.EconomyInstanceId,
				"the resumed Totem still binds its saved economy instance" );
			var balanceBefore = economy.Balance;
			var scrapValue = economy.TryGetObject( totemInstance, out var totemObject ) ? economy.ScrapValue( totemObject! ) : 0;
			level.RemoveRide();
			Require( level.PlacedRide == null && !payments.TryGetInstance( totem.Visitors.AttractionId, out _ ),
				"removing the resumed Totem unregisters its bridge and unlinks its payments" );
			Require( !economy.TryGetObject( totemInstance, out _ ),
				savedTotem.EconomyBought ? "removing the resumed Totem sells its instance exactly once" : "removing the never-bought resumed Totem deregisters its instance" );
			Require( !savedTotem.EconomyBought || economy.Balance == balanceBefore + scrapValue,
				$"the Totem's sale credits its scrap exactly once (${balanceBefore} + ${scrapValue} = ${economy.Balance})" );
			prototypeRemoved = true;
		}
		postLoad = new SmokePostLoad
		{
			ParkReopenedAndClosed = true,
			ClosedParkRefusedAdmission = refused,
			AdmissionBookedExactlyOnce = true,
			SoldObjectCreditedScrapOnce = true,
			SoldObjectLinksRemoved = true,
			NewObjectPlacedAfterSale = true,
			StaffHiredAndPaidOnce = true,
			PaymentBindingsOneToOne = true,
			PrototypeRemoved = prototypeRemoved,
			BalanceAfter = economy.Balance,
			ScrapCredited = scrap,
			WagesCharged = wages,
			StaffActors = StaffActorCount(),
		};
		CaptureFrame( "park-resume-post-load.png" );
		Complete( "Native park-resume smoke restore passed: the menu load of the capture's save reproduced the boundary and all "
			+ $"{ContinuationTickCount} continuation ticks; {probeResults.Count} corrupt candidates rejected with the live park intact; "
			+ "post-load open/close, admission, sale, placement and wages verified." );
	}

	// ---- Shared evidence helpers -----------------------------------------------------------------

	private AutomaticAdvisor? Advisor => flow.Advisor?.IsAttached == true ? flow.Advisor : null;

	/// <summary>Captures the whole park and hashes every section plus the composite; one sample per recorded tick.</summary>
	private SmokeSample CaptureSample( long tick )
	{
		if ( verifiedAssets == null )
		{
			// The first capture computes the trusted manifest itself; every later one reuses it verbatim, so a
			// thousand ticks never rehash the data files.
			var first = level.CaptureResumeSnapshot( Advisor, "park-resume smoke" );
			verifiedAssets = first.AssetHashes;
		}
		var snapshot = level.CaptureResumeSnapshot( Advisor, "park-resume smoke", verifiedAssets );
		var sections = new Dictionary<string, string>
		{
			["economy"] = SectionSha256( snapshot.Economy ),
			["layout"] = SectionSha256( snapshot.Layout ),
			["guests"] = SectionSha256( snapshot.Guests ),
			["scripts"] = SectionSha256( snapshot.Scripts ),
			["runtimes"] = SectionSha256( snapshot.Runtimes ),
			["bindings"] = SectionSha256( snapshot.EconomyBindings ),
			["clock"] = SectionSha256( snapshot.Clock ),
			["weather"] = SectionSha256( snapshot.Weather ),
			["gate"] = SectionSha256( snapshot.Gate ),
			["staff"] = SectionSha256( snapshot.Staff ),
			["advisor"] = SectionSha256( snapshot.Advisor ),
			// Null (a park without the Totem) hashes to the codec's own 'null' bytes — one stable sentinel.
			["prototype"] = SectionSha256( snapshot.Prototype ),
		};
		var composite = new StringBuilder( "opentpw-park-resume/1|" ).Append( snapshot.Theme ).Append( '|' )
			.Append( snapshot.StartKind ).Append( '|' ).Append( snapshot.Seed ).Append( '|' ).Append( snapshot.SoundSeed );
		foreach ( var (name, hash) in sections.OrderBy( pair => pair.Key, StringComparer.Ordinal ) )
			composite.Append( '|' ).Append( name ).Append( '=' ).Append( hash );
		return new SmokeSample
		{
			Tick = tick,
			FullTypedSha256 = Convert.ToHexString( SHA256.HashData( Encoding.ASCII.GetBytes( composite.ToString() ) ) ).ToLowerInvariant(),
			Sections = sections,
			ComputeStateHash = level.ComputeStateHash().ToString( System.Globalization.CultureInfo.InvariantCulture ),
			Counters = CountersOf( snapshot ),
		};
	}

	private static SmokeCounters CountersOf( ParkResumeSnapshot snapshot )
	{
		int InState( params GuestState[] states ) => snapshot.Guests.Guests.Count( guest => states.Contains( guest.State ) );
		return new SmokeCounters
		{
			Guests = snapshot.Guests.Guests.Length,
			InPark = InState( GuestState.DoingNothing, GuestState.WalkingAround, GuestState.GoingToRide, GuestState.Queueing, GuestState.MovingUpQueue,
				GuestState.Boarding, GuestState.WaitingToBoard, GuestState.Using, GuestState.ExitingRide ),
			WalkingAround = InState( GuestState.WalkingAround ),
			GoingToRide = InState( GuestState.GoingToRide ),
			InQueue = InState( GuestState.Queueing, GuestState.MovingUpQueue ),
			BoardingOrWaiting = InState( GuestState.Boarding, GuestState.WaitingToBoard ),
			Riding = InState( GuestState.Using ),
			ExitingRide = InState( GuestState.ExitingRide ),
			LeavingOrArriving = InState( GuestState.CrossingRoad, GuestState.CrossingRoadToPark, GuestState.GoingToTicketBooth, GuestState.AtTicketBooth,
				GuestState.EnteringPark, GuestState.CrossingRoadHome, GuestState.WaitingToGoHome, GuestState.LeavingPark ),
			Admissions = snapshot.Guests.Admissions,
			Departed = snapshot.Guests.Departed,
			NextGuestId = snapshot.Guests.NextGuestId,
			Attractions = snapshot.Guests.Attractions.Length,
			QueuedGuests = snapshot.Guests.Attractions.Sum( attraction => attraction.Queue.Length ),
			Riders = snapshot.Guests.Attractions.Sum( attraction => attraction.OnRide.Length ),
			WalkingOff = snapshot.Guests.Attractions.Sum( attraction => attraction.WalkingOff.Length ),
			GuestTick = snapshot.Guests.TickCount,
			Objects = snapshot.Layout.Objects.Count,
			NextAttractionId = snapshot.Layout.NextAttractionId,
			CellMapVersion = snapshot.Layout.Cells.Version,
			Balance = snapshot.Economy.Ledger.Balance,
			EconomyTick = snapshot.Economy.Tick,
			EconomyObjects = snapshot.Economy.Objects.Count,
			EconomyBindings = snapshot.EconomyBindings.Count,
			StaffMembers = snapshot.Economy.Staff.Members.Count,
			StaffActors = snapshot.Staff?.Actors.Count ?? 0,
			WeatherQuality = snapshot.Weather?.Quality ?? 0,
			Entities = Entity.All.Count,
		};
	}

	/// <summary>Fail-fast comparison against the oracle sample, naming the first divergence (full hash, then section, then counter).</summary>
	private static void RequireSampleMatches( SmokeSample expected, SmokeSample actual, string what )
	{
		Require( expected.FullTypedSha256 == actual.FullTypedSha256,
			$"{what}: full typed hash {actual.FullTypedSha256[^12..]} is not the capture's {expected.FullTypedSha256[^12..]} "
			+ $"({actual.Counters.Guests} guests, {actual.Counters.Riders} riding, balance ${actual.Counters.Balance})" );
		foreach ( var (name, hash) in expected.Sections )
		{
			var other = actual.Sections.GetValueOrDefault( name );
			Require( other != null && other == hash,
				$"{what}: section {name} {( other == null ? "is missing" : other[^12..] + " is" )} not the capture's {hash[^12..]}" );
		}
		var difference = expected.Counters == actual.Counters ? null : string.Join( "; ", DifferenceDetails( expected.Counters, actual.Counters ).Take( 3 ) );
		Require( difference == null, $"{what}: counter {difference}" );
	}

	private static IEnumerable<string> DifferenceDetails( SmokeCounters expected, SmokeCounters actual )
	{
		if ( expected.Guests != actual.Guests ) yield return $"Guests ({expected.Guests} vs {actual.Guests})";
		if ( expected.InPark != actual.InPark ) yield return $"InPark ({expected.InPark} vs {actual.InPark})";
		if ( expected.WalkingAround != actual.WalkingAround ) yield return $"WalkingAround ({expected.WalkingAround} vs {actual.WalkingAround})";
		if ( expected.GoingToRide != actual.GoingToRide ) yield return $"GoingToRide ({expected.GoingToRide} vs {actual.GoingToRide})";
		if ( expected.InQueue != actual.InQueue ) yield return $"InQueue ({expected.InQueue} vs {actual.InQueue})";
		if ( expected.BoardingOrWaiting != actual.BoardingOrWaiting ) yield return $"BoardingOrWaiting ({expected.BoardingOrWaiting} vs {actual.BoardingOrWaiting})";
		if ( expected.Riding != actual.Riding ) yield return $"Riding ({expected.Riding} vs {actual.Riding})";
		if ( expected.ExitingRide != actual.ExitingRide ) yield return $"ExitingRide ({expected.ExitingRide} vs {actual.ExitingRide})";
		if ( expected.LeavingOrArriving != actual.LeavingOrArriving ) yield return $"LeavingOrArriving ({expected.LeavingOrArriving} vs {actual.LeavingOrArriving})";
		if ( expected.Admissions != actual.Admissions ) yield return $"Admissions ({expected.Admissions} vs {actual.Admissions})";
		if ( expected.Departed != actual.Departed ) yield return $"Departed ({expected.Departed} vs {actual.Departed})";
		if ( expected.NextGuestId != actual.NextGuestId ) yield return $"NextGuestId ({expected.NextGuestId} vs {actual.NextGuestId})";
		if ( expected.Attractions != actual.Attractions ) yield return $"Attractions ({expected.Attractions} vs {actual.Attractions})";
		if ( expected.QueuedGuests != actual.QueuedGuests ) yield return $"QueuedGuests ({expected.QueuedGuests} vs {actual.QueuedGuests})";
		if ( expected.Riders != actual.Riders ) yield return $"Riders ({expected.Riders} vs {actual.Riders})";
		if ( expected.WalkingOff != actual.WalkingOff ) yield return $"WalkingOff ({expected.WalkingOff} vs {actual.WalkingOff})";
		if ( expected.GuestTick != actual.GuestTick ) yield return $"GuestTick ({expected.GuestTick} vs {actual.GuestTick})";
		if ( expected.Objects != actual.Objects ) yield return $"Objects ({expected.Objects} vs {actual.Objects})";
		if ( expected.NextAttractionId != actual.NextAttractionId ) yield return $"NextAttractionId ({expected.NextAttractionId} vs {actual.NextAttractionId})";
		if ( expected.CellMapVersion != actual.CellMapVersion ) yield return $"CellMapVersion ({expected.CellMapVersion} vs {actual.CellMapVersion})";
		if ( expected.Balance != actual.Balance ) yield return $"Balance ({expected.Balance} vs {actual.Balance})";
		if ( expected.EconomyTick != actual.EconomyTick ) yield return $"EconomyTick ({expected.EconomyTick} vs {actual.EconomyTick})";
		if ( expected.EconomyObjects != actual.EconomyObjects ) yield return $"EconomyObjects ({expected.EconomyObjects} vs {actual.EconomyObjects})";
		if ( expected.EconomyBindings != actual.EconomyBindings ) yield return $"EconomyBindings ({expected.EconomyBindings} vs {actual.EconomyBindings})";
		if ( expected.StaffMembers != actual.StaffMembers ) yield return $"StaffMembers ({expected.StaffMembers} vs {actual.StaffMembers})";
		if ( expected.StaffActors != actual.StaffActors ) yield return $"StaffActors ({expected.StaffActors} vs {actual.StaffActors})";
		if ( expected.WeatherQuality != actual.WeatherQuality ) yield return $"WeatherQuality ({expected.WeatherQuality} vs {actual.WeatherQuality})";
		if ( expected.Entities != actual.Entities ) yield return $"Entities ({expected.Entities} vs {actual.Entities})";
	}

	private SmokeEvidence Evidence( bool completed, string? failure )
	{
		var bytes = File.Exists( savePath ) ? File.ReadAllBytes( savePath ) : Array.Empty<byte>();
		return new SmokeEvidence
		{
			Phase = restoring ? "restore" : "capture",
			Theme = themeText,
			StartKind = startKindText,
			Seed = level.Seed.ToString(),
			SavedUtc = savedUtc,
			ManifestSha256 = CanonicalManifestSha256( verifiedAssets ),
			PrototypeVariant = PrototypeVariant,
			WaterVariant = WaterVariant,
			WaterAtSave = new SmokeWater { Tiles = savedWater.Tiles, Boats = savedWater.Boats },
			Prototype = savedPrototype,
			Save = new SmokeSaveFile { Name = Path.GetFileName( savePath ), ByteCount = bytes.Length, Sha256 = Sha256Hex( bytes ) },
			Boundary = boundarySample,
			Ticks = tickSamples,
			Probes = probeResults,
			PostLoad = postLoad,
			Pngs = capturedPngs,
			Completed = completed,
			Failure = failure,
		};
	}

	/// <summary>The saved Totem's evidence facts, cross-checked against the live ride this phase holds (null when the save holds no part).</summary>
	private SmokePrototype? SmokePrototypeOf( PrototypeRideSaveData? part, PrototypeRide? live )
	{
		if ( part == null )
		{
			Require( live == null, "the save holds no prototype part, but the park places a Totem" );
			return null;
		}
		Require( live != null, "the save holds a prototype part, but the park places no Totem" );
		Require( part.AttractionId == live!.Visitors.AttractionId && part.EconomyInstanceId != null,
			"the saved Totem joins the live park by attraction id with its economy instance" );
		return new SmokePrototype { AttractionId = part.AttractionId, Open = part.Open, EconomyInstanceId = part.EconomyInstanceId!.Value, EconomyBought = part.EconomyBought };
	}

	/// <summary>Post-load staff actors are the world-side authority: one recapture, after the hire, is enough.</summary>
	private int StaffActorCount()
	{
		var snapshot = level.CaptureResumeSnapshot( Advisor, "park-resume smoke", verifiedAssets );
		return snapshot.Staff?.Actors.Count ?? 0;
	}

	private void Complete( string summary )
	{
		completed = true;
		var path = Path.Combine( evidenceDirectory, $"{( restoring ? "restore" : "capture" )}-evidence.json" );
		WriteEvidence( path, Evidence( completed: true, failure: null ) );
		if ( restoring )
		{
			var problems = VerifyEvidencePair( capture!, Evidence( completed: true, failure: null ) );
			Require( problems.Count == 0, $"the evidence pair verifies ({problems.Count} problems; first: {problems.FirstOrDefault() ?? "none"})" );
		}
		Log.Trace( summary );
		stage = Stage.Done;
		Render.Window.SdlWindow.Close();
	}

	private void Fail( Exception error )
	{
		stage = Stage.Done;
		var message = $"{error.GetType().Name}: {error.Message}";
		try
		{
			WriteEvidence( Path.Combine( evidenceDirectory, $"{( restoring ? "restore" : "capture" )}-evidence.json" ),
				Evidence( completed: false, failure: message ) );
		}
		catch ( Exception writeError )
		{
			Log.Warning( $"Park-resume smoke: writing the failure evidence failed too: {writeError.Message}" );
		}
		Console.Error.WriteLine( $"Native park-resume smoke test failed: {message}" );
		Log.Error( $"Native park-resume smoke test failed: {message}" );
		Environment.Exit( 1 );
	}

	internal void VerifyCompleted() => Require( completed, "complete all native park-resume smoke frames" );

	internal static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Native park-resume smoke test failed: {step}." );
	}

	/// <summary>World-only GPU readback (the sandbox smoke test's capture pattern), saved beside the evidence.</summary>
	private void CaptureFrame( string name )
	{
		var source = Render.ResolveColorTexture;
		using var staging = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
			source.Width, source.Height, 1, 1, source.Format, TextureUsage.Staging ) );
		using var commands = Device.ResourceFactory.CreateCommandList();
		commands.Begin();
		commands.CopyTexture( source, staging );
		commands.End();
		Device.SubmitCommands( commands );
		Device.WaitForIdle();
		var mapped = Device.Map( staging, MapMode.Read );
		try
		{
			var rowBytes = checked((int)source.Width * 4);
			var pixels = new byte[checked(rowBytes * (int)source.Height)];
			for ( var row = 0; row < source.Height; ++row )
				Marshal.Copy( IntPtr.Add( mapped.Data, checked((int)(row * mapped.RowPitch)) ), pixels, row * rowBytes, rowBytes );
			if ( source.Format is PixelFormat.R8_G8_B8_A8_UNorm )
				for ( var pixel = 0; pixel < pixels.Length; pixel += 4 )
					(pixels[pixel], pixels[pixel + 2]) = (pixels[pixel + 2], pixels[pixel]);
			Require( source.Format is PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.R8_G8_B8_A8_UNorm, "readback format is 8-bit RGBA" );
			Require( pixels.Where( ( value, index ) => index % 4 != 3 ).Any( value => value != 0 ), $"the world framebuffer is not black ({name})" );
			var colors = new HashSet<uint>();
			for ( var pixel = 0; pixel < pixels.Length && colors.Count <= 32; pixel += 4 )
				colors.Add( BitConverter.ToUInt32( pixels, pixel ) );
			Require( colors.Count > 32, $"the world framebuffer retains texture detail ({name})" );
			PngWriter.SaveBgra( Path.Combine( evidenceDirectory, name ), pixels, (int)source.Width, (int)source.Height );
			capturedPngs.Add( name );
			Log.Trace( $"Park-resume draw: {name} ({source.Width}x{source.Height}, 32+ distinct colors)." );
		}
		finally { Device.Unmap( staging ); }
	}

	internal static string Sha256Hex( byte[] bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();

	public void Dispose()
	{
		watchdog.Dispose();
		SaveFileSystem = originalSaveFileSystem;
	}
}

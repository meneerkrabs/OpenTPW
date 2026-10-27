using System.Diagnostics;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.Online.Coop;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-snapshot-join] The game's side of a guest's join (docs/COOP.md, "The game's join"): the join host that builds the host's park from its join snapshot over
/// frames, in the join restore mode, in a scene set aside beside the live one, and swaps it in at the session's step; and the lifecycle of a join that has no park on this machine yet.
/// The screens that start a join are C2d's: <see cref="JoinSharedPark(CoopLink)"/> and <see cref="FollowSharedPark(CoopLink)"/> are their entries.
/// </summary>
internal sealed partial class GameFlow
{
	/// <summary>The reason a session ends with when the player goes elsewhere while it is joined or built.</summary>
	internal const string LeftSharedPark = "The player left the shared park.";

	/// <summary>A shared park's session that has no park on this machine yet (a join under way); null otherwise. Its park, once swapped in, holds it as <see cref="Level.CoopSession"/>.</summary>
	private CoopSession? coopJoin;

	/// <summary>Set while this game ends a join itself (the player went elsewhere): its end shows no failure then.</summary>
	private bool endingJoin;

	/// <summary>This game's asset manifest as the last join hashed it, kept as <see cref="Level.AutosaveAssets"/> keeps a park's (the catalogue and its entry count).</summary>
	private (ObjectCatalog Catalog, int Entries, IReadOnlyDictionary<string, string> Manifest)? joinAssets;

	/// <summary>The join under way without a park (tests and the smoke).</summary>
	internal CoopSession? HeldJoin => coopJoin;

	/// <summary>Test seam: builds a join's candidate instead of the game's restore (the asset-free tests' replay twin, staged in a scene of its own).</summary>
	internal Func<CoopJoinPayload, IGameJoinBuild>? CoopJoinBuildOverride { get; set; }

	/// <summary>Test seam: the clock of the sessions this game joins (the session's own otherwise).</summary>
	internal Func<double>? CoopSessionClock { get; set; }

	/// <summary>
	/// Joins the running session <paramref name="link"/> is in: the guest asks for the host's park, builds it over frames and enters it, leaving the front end. Refused while a park is
	/// open, a join is held, a park is being built or a transition waits.
	/// </summary>
	internal CoopSession JoinSharedPark( CoopLink link ) => JoinSharedPark( link, () => ModsMatchHost( link ) );

	/// <summary><see cref="JoinSharedPark(CoopLink)"/> over any transport, with the check that this game's simulation mods are the host's.</summary>
	internal CoopSession JoinSharedPark( ICoopStreamTransport link, Func<bool> modsMatch )
	{
		ArgumentNullException.ThrowIfNull( modsMatch );
		if ( Level != null )
			throw new InvalidOperationException( "Leave the park before joining a shared park." );
		RequireNoTransition();
		var session = CoopSession.JoinRunning( link, new CoopJoinHost( this, modsMatch ), CoopSessionClock );
		coopJoin = session;
		session.Ended += reason => OnJoinEnded( session, reason );
		return session;
	}

	/// <summary>
	/// Follows the session <paramref name="link"/> is in with the open park, which must be the host's park at the session's identical start (C2b): with the game's join host, a park that
	/// loses the stream gets the host's park again and swaps it in.
	/// </summary>
	internal CoopSession FollowSharedPark( CoopLink link ) => FollowSharedPark( link, () => ModsMatchHost( link ) );

	/// <summary><see cref="FollowSharedPark(CoopLink)"/> over any transport.</summary>
	internal CoopSession FollowSharedPark( ICoopStreamTransport link, Func<bool> modsMatch )
	{
		ArgumentNullException.ThrowIfNull( modsMatch );
		var level = Level ?? throw new InvalidOperationException( "Open the shared park's start before following it." );
		RequireNoTransition();
		var session = CoopSession.Join( level, link, CoopSessionClock, new CoopJoinHost( this, modsMatch ) );
		session.Ended += reason => OnJoinEnded( session, reason );
		return session;
	}

	private void RequireNoTransition()
	{
		if ( coopJoin != null )
			throw new InvalidOperationException( "A shared park is being joined." );
		if ( parkBuild != null || pending != null )
			throw new InvalidOperationException( "A park is being started." );
	}

	/// <summary>The link's join already required the host's simulation mods to be this game's (decision Q16); the build checks it again at the capture it builds.</summary>
	private static bool ModsMatchHost( CoopLink link ) => link.HostMods is { } host && host.SequenceEqual( link.Identity().Mods );

	/// <summary>
	/// The co-op session's step, once a frame right after the online pump and before the HUD's and the level's update. Not while a park is built over frames or a transition waits: one
	/// construction scene at a time, and a queued transition would tear the guest's park down after the swap (the transition itself ends a join, <see cref="LeaveJoinBuild"/>).
	/// </summary>
	internal void StepCoopSession()
	{
		if ( parkBuild != null || pending != null )
			return;
		(Level?.CoopSession ?? coopJoin)?.Step();
	}

	/// <summary>
	/// A transition to another park (a park start, a load, a visit) ends the join it leaves before it builds anything: a held join that has no park yet, and a session whose build holds
	/// the one construction scene (a resyncing guest's).
	/// </summary>
	private void LeaveJoinBuild()
	{
		if ( coopJoin is { } joining )
			EndJoin( joining, LeftSharedPark );
		else if ( Level?.CoopSession is { IsBuilding: true } building )
			EndJoin( building, LeftSharedPark );
	}

	/// <summary>Ends a session this game leaves itself: its end shows no failure.</summary>
	private void EndJoin( CoopSession session, string reason )
	{
		endingJoin = true;
		try { session.End( reason ); }
		finally { endingJoin = false; }
	}

	/// <summary>
	/// A session this game joined ended, said in words by what ended it (<see cref="JoinEndText"/>; nothing when this game left it). One that never had a park on this machine leaves
	/// the player in the front end: on the menu's stack, or with the front end queued when a failed swap already retired it. [EXT:online-coop-session] A guest whose joined park's
	/// session ends (the host left, a removal, a lost connection) hears why over that park, whose OK takes the player to the lobby (C2d-3): the park is the host's and is never saved.
	/// </summary>
	private void OnJoinEnded( CoopSession session, string reason )
	{
		if ( ReferenceEquals( coopJoin, session ) )
			coopJoin = null;
		if ( endingJoin || JoinEndText( session.EndCause, session.ServerReason ) is not { } words )
			return;
		// [EXT:online-coop-commands] the guest's management actions that may not have been made add their line (C4b-2)
		var unfinished = session.ManagementUnfinished;
		if ( session.Level != null )
		{
			if ( ReferenceEquals( session.Level, Level ) && Hud is { } hud )
				ShowGuestEnd( session.Level, hud, words, reason, unfinished );
			return;
		}
		// [EXT:online-coop-snapshot-join] a resync whose install failed before its teardown left the session's old park the game's park, detached from the session: the end shows over it,
		// as over a park the session ended, instead of leaving the guest in a park that issues its own commands (C4b-2)
		if ( Level is { IsCoopGuestPark: true } old && Hud is { } oldHud )
		{
			ShowGuestEnd( old, oldHud, words, reason, unfinished );
			return;
		}
		if ( Level != null )
			return;
		Log.Warning( $"Joining the shared park ended: {reason}" );
		if ( Menu != null )
			ShowJoinFailure( Menu.Stack, words, unfinished );
		else
			Queue( () =>
			{
				ShowFrontEnd();
				if ( Menu != null )
					ShowJoinFailure( Menu.Stack, words, unfinished );
			} );
	}

	/// <summary>
	/// [EXT:online-coop-commands] An end dialog's words, followed by the sentence that some of the last changes may not have been made when <paramref name="unfinished"/> (C4b-2), in the
	/// dialog's body with them (a line break would make the words its caption).
	/// </summary>
	private string JoinEndWords( OpenTpwText words, bool unfinished ) =>
		unfinished ? $"{Strings.Extra( words )} {Strings.Extra( OpenTpwText.CoopChangesNotMade )}" : Strings.Extra( words );

	/// <summary>The name of the dialog over a guest's park whose session ended.</summary>
	internal const string GuestEndedScreenName = "coopGuestEnded";

	/// <summary>[EXT:online-coop-session] The guest's park lost its session: the reason over the park, and its OK leaves for the lobby (C2d-3).</summary>
	private void ShowGuestEnd( Level level, ParkHud hud, OpenTpwText words, string reason, bool unfinished = false )
	{
		Log.Info( $"The shared park ended for this guest: {reason}" );
		UiScreen? screen = null;
		screen = UiDialogs.Message( GuestEndedScreenName, () => JoinEndWords( words, unfinished ), (() => Strings.Extra( OpenTpwText.Back ), () =>
		{
			if ( screen!.IsRemoved || !ReferenceEquals( hud.Stack.Top, screen ) )
				return;
			hud.Stack.Pop();
			RequestAutomaticExit( level, hud );
		}) );
		hud.Stack.Push( screen );
	}

	private void ShowJoinFailure( UiScreenStack stack, OpenTpwText words, bool unfinished = false )
	{
		UiScreen? screen = null;
		screen = UiDialogs.Message( "coopJoinEnded", () => JoinEndWords( words, unfinished ), (() => Strings.Extra( OpenTpwText.Back ), () =>
		{
			if ( !screen!.IsRemoved && ReferenceEquals( stack.Top, screen ) )
				stack.Pop();
		}) );
		stack.Push( screen );
	}

	/// <summary>The live park's camera wherever it is kept now: the camera itself, or the one set aside while the globe is shown over the park, the ride view is open or the player walks.</summary>
	private ParkCameraMode? LiveParkCamera( Level level ) =>
		Camera.Mode as ParkCameraMode ?? parkCamera as ParkCameraMode ?? level.ParkCameraWhileRiding ?? level.ParkCameraWhileWalking;

	/// <summary>
	/// [EXT:park-entry-preparation] Set by a swap that handed its build's advisor preparation to the shared store: kept past the swap's frame until the joined park's advisor
	/// has said its first line, which can come in a later frame (a resync's did, the frame after its swap), or will not say one (no advisor, or the player's advice off).
	/// </summary>
	private bool keepJoinAdvisorPreparation;

	/// <summary>Whether the shared advisor preparation is still kept for a joined park's advisor (<see cref="keepJoinAdvisorPreparation"/>), which ends once it is not.</summary>
	private bool KeepingJoinAdvisorPreparation()
	{
		if ( keepJoinAdvisorPreparation && !AdvisorMayStillTake( Advisor, GameOptions.Current.Advisor ) )
			keepJoinAdvisorPreparation = false;
		return keepJoinAdvisorPreparation;
	}

	/// <summary>
	/// Whether a joined park's advisor may still take what its build prepared for its first line: there is one, it has made no presentation yet, making one has not failed, and the
	/// player hears advice.
	/// </summary>
	internal static bool AdvisorMayStillTake( AutomaticAdvisor? advisor, bool adviceOn ) =>
		advisor != null && advisor.Presentation == null && !advisor.PresentationFailed && adviceOn;

	/// <summary>
	/// The swap (docs/COOP.md, "The game's join"): the live scene (the front end, or a resyncing guest's old park, which the session detached from first) is torn down as a park
	/// entry tears it down, without ending the joining session, and the candidate's scene is activated and entered. Only work that cannot throw follows the activation: a throw before it
	/// leaves the candidate the session's to abandon. Not run: the progress (a guest's park is the host's), the restart snapshot and a saved sound (a guest's park has neither).
	/// </summary>
	private void InstallJoin( Level? old, IGameJoinBuild build )
	{
		// a resync keeps the player's view: read before the teardown leaves the ride view and the free walk and drops the camera the globe set aside
		var pose = old != null && LiveParkCamera( old ) is { } camera ? (camera.Target, camera.Height, camera.Yaw) : default( (Vector3 Target, float Height, float Yaw)? );
		// a first join's session has no park yet: the teardown below must not end it
		coopJoin = null;
		var entry = build.Stage();
		var retired = Advisor;
		TearDown( applyPendingLanguage: false );
		Advisor = null;
		if ( retired != null && !ReferenceEquals( retired, entry.Advisor ) )
		{
			try { retired.Dispose(); }
			catch ( Exception error ) when ( error is not OutOfMemoryException ) { Log.Warning( $"The advisor of the park left could not be released: {error.Message}" ); }
		}
		entry.Scene.Activate();
		build.MarkInstalled();
		// [EXT:park-entry-preparation] what the build read for the advisor's first line, for that line, as a park start keeps its own (the teardown dropped the shared store): handed
		// over once the candidate is the game's park, so a failed activation leaves it the build's to drop
		AdvisorEntryPreparation.Adopt( entry.AdvisorPrepared );
		keepJoinAdvisorPreparation = entry.Advisor != null && AdvisorEntryPreparation.Count > 0;
		var level = entry.Level;
		Level = level;
		StartKind = entry.StartKind;
		// what it took from the input of the frame it was made in is taken from this one
		entry.Hud.RefreshEntryInput();
		Hud = entry.Hud;
		Advisor = entry.Advisor;
		// a resync greets as an entry does: a fresh advisor, as an entry decides it (the host's advice is its own player's)
		if ( entry.AdvisorTried )
			advisorUnavailable = entry.Advisor == null;
		AttachGuestSounds( level );
		try
		{
			GameAudio.EnterPark( entry.Theme, entry.SoundSeed );
			GameAudio.Events?.Reseed( entry.SoundSeed );
			// the restored scream handles had no chain in the candidate: they scream again now that the park's audio runs
			level.ResumeHeldScreams();
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Log.Warning( $"Shared park entered; audio could not start: {error.Message}" );
		}
		if ( pose is { } kept && Camera.Mode is ParkCameraMode fresh )
			fresh.SetPose( kept.Target, kept.Height, kept.Yaw );
		Log.Trace( $"Entered the shared park ({entry.Theme}) at tick {level.TickCount}: {level.Objects.Objects.Count} objects, {level.Guests?.Guests.Count ?? 0} guests." );
	}

	/// <summary>[EXT:online-coop-snapshot-join] The game's join host of one session: its builds and its swap.</summary>
	private sealed class CoopJoinHost( GameFlow flow, Func<bool> modsMatch ) : ICoopJoinHost
	{
		public Func<bool> ModsMatch { get; } = modsMatch;

		public ICoopJoinBuild Build( CoopJoinPayload payload ) => flow.CoopJoinBuildOverride?.Invoke( payload ) ?? new CoopJoinBuild( flow, this, payload );

		public void Install( Level? old, ICoopJoinBuild build ) => flow.InstallJoin( old, (IGameJoinBuild)build );
	}

	/// <summary>
	/// The game's build of a join snapshot over frames, within the park build's frame budget, in <see cref="ResumeStrict"/>'s order: the save parsed; the mods compared; the level's files
	/// waited for; this game's files hashed (once a catalogue) and compared; the snapshot validated; the economy restored; then, in a scene set aside, the level's construction in the
	/// join restore mode, the park HUD and a fresh advisor. Between slices the scene waits, paused, and the live one runs its frames; it waits so until the swap.
	/// </summary>
	private sealed class CoopJoinBuild : IGameJoinBuild
	{
		private readonly GameFlow flow;
		private readonly CoopJoinHost host;
		private readonly CoopJoinPayload payload;
		private readonly IEnumerator<bool> steps;
		private ParkResumeSnapshot? snapshot;
		private EntityConstructionScope? scene;
		private Level? candidate;
		private ParkHud? hud;
		private int hudLanguage;
		private AutomaticAdvisor? advisor;
		private bool advisorTried;
		/// <summary>What this build read for its advisor's first line (<see cref="AdvisorSteps"/>), its own until the swap hands it over.</summary>
		private readonly AdvisorEntryPreparation.Store advisorPrepared = new();
		private bool complete;
		private bool abandoned;
		private bool installed;

		public CoopJoinBuild( GameFlow flow, CoopJoinHost host, CoopJoinPayload payload )
		{
			this.flow = flow;
			this.host = host;
			this.payload = payload;
			steps = Steps().GetEnumerator();
			Timings.ProcessBuild = Interlocked.Increment( ref processBuilds );
		}

		public Level Candidate => complete && candidate != null ? candidate : throw new InvalidOperationException( "The shared park is not built yet." );

		public int LevelSteps { get; private set; }

		public int StagedEntities => scene?.StagedEntities ?? 0;

		public bool RestoringObjects => candidate?.RestoringObjects ?? false;

		public int PlacedObjects => candidate?.Objects?.Objects.Count ?? 0;

		public CoopBuildTimings Timings { get; } = CoopBuildTimings.ForStages( "codec", "save", "files", "catalogue", "manifest", "validation", "economy", "level", "hud", "advisor",
			"prepare", "digest" );

		/// <summary>
		/// The stage the build's work runs in now, and when its current part began (<see cref="Timings"/>): a stage is charged its part when the next begins or the step ends, so a step that
		/// runs past a stage's end charges each stage its own part, and a step that throws is charged too.
		/// </summary>
		private CoopWorkTiming? stage;
		private long partStarted;
		/// <summary>The garbage collections, generation 0's and the oldest generation's, when the current part began (<see cref="CoopWorkTiming.Collections"/>).</summary>
		private int partCollections, partFullCollections;
		/// <summary>The build's slices so far: the frame a stage last ran in.</summary>
		private int slice;
		/// <summary>The timings' own bookkeeping in the current slice, in <see cref="Stopwatch"/> ticks, which the slice's budget leaves out.</summary>
		private long bookkeeping;
		/// <summary>The builds of captures this process began; the first is the process's cold one.</summary>
		private static int processBuilds;
		/// <summary>
		/// Set by a step after which the slice ends, whatever its budget left (docs/COOP.md, "The game's join"): a wait for the level's files, which asks once a slice instead of spending
		/// it, and a construction step that announced a long one next (<see cref="Level.NextStepIsLong"/>), which then starts a slice of its own, as in a park start.
		/// </summary>
		private bool endsSlice;

		public bool Step()
		{
			if ( abandoned || installed )
				throw new InvalidOperationException( "The shared park's build is over." );
			if ( complete )
				return true;
			var started = Stopwatch.GetTimestamp();
			// the budget is the slice's time as before the timings (the scene's resume and pause in it), less the timings' own bookkeeping (the profile's resume and pause among it), so
			// the slices run as they did before them
			double Worked() => (Stopwatch.GetTimestamp() - started - bookkeeping) * 1000d / Stopwatch.Frequency;
			bool OverBudget() => Worked() >= ParkBuildBudgetMilliseconds;
			slice++;
			endsSlice = false;
			// the build's own work only: the live scene's frames between its slices are no part of its sections, which are taken slice by slice
			LoadProfile.Resume();
			var sections = LoadProfile.Snapshot();
			bookkeeping = Stopwatch.GetTimestamp() - started;
			scene?.Resume();
			try
			{
				while ( !OverBudget() )
				{
					StartPart();
					bool more;
					try
					{
						more = steps.MoveNext();
					}
					finally
					{
						Charge( Stopwatch.GetTimestamp() );
					}
					if ( !more )
					{
						complete = true;
						break;
					}
					// the step ended the slice: the rest of the frame is the live scene's
					if ( endsSlice )
						break;
				}
			}
			finally
			{
				// the live scene runs its frames between the build's: the candidate waits set aside, until the swap once it is complete
				scene?.Pause();
				var worked = Worked();
				var counted = Stopwatch.GetTimestamp();
				LoadProfile.Pause();
				// a section that ran in the slice counts, its time even when the clock saw none of it (a browser's clock is coarse)
				// (a helper call: a loop in a finally block overwrote the try's locals in the Mono interpreter's hot tier, tools/test_no_loop_in_finally.py)
				CountSectionsOfSlice( sections );
				Timings.Frames++;
				Timings.LongestFrameMilliseconds = Math.Max( Timings.LongestFrameMilliseconds, worked );
				// the build is over: the profile runs on as it did before it
				if ( complete )
					LoadProfile.Resume();
				bookkeeping += Stopwatch.GetTimestamp() - counted;
			}
			// the check at the cut and the swap get a frame of their own when this one is spent
			return complete && !OverBudget();
		}

		/// <summary>The sections that ran in the slice just taken, against the profile as the slice began, are added to the build's timings.</summary>
		private void CountSectionsOfSlice( IReadOnlyDictionary<string, (double Milliseconds, int Count)> sections )
		{
			foreach ( var (name, section) in LoadProfile.Snapshot() )
			{
				var (before, ran) = sections.TryGetValue( name, out var earlier ) ? (earlier.Milliseconds, earlier.Count) : (0, 0);
				if ( section.Count > ran )
					Timings.Sections[name] = Timings.Sections.GetValueOrDefault( name ) + section.Milliseconds - before;
			}
		}

		/// <summary>
		/// The current stage is charged its part up to <paramref name="now"/> (a part that alone took the budget is listed), the work from here on belongs to the stage <paramref name="next"/>
		/// when one <paramref name="begins"/> (to none when null), and the next part begins after this bookkeeping, which the slice's budget leaves out.
		/// </summary>
		private void Charge( long now, bool begins = false, string? next = null )
		{
			if ( stage != null )
			{
				var milliseconds = Stopwatch.GetElapsedTime( partStarted, now ).TotalMilliseconds;
				stage.Add( milliseconds, slice );
				stage.AddCollections( GC.CollectionCount( 0 ) - partCollections, GC.CollectionCount( GC.MaxGeneration ) - partFullCollections );
				if ( milliseconds >= ParkBuildBudgetMilliseconds )
					Timings.LongSteps.Add( new CoopLongStep( stage.Name, stage.Steps, milliseconds, stage.Name == "level" ? candidate?.ConstructionProgress : null ) );
			}
			if ( begins )
				stage = next == null ? null : Timings.Stage( next );
			bookkeeping += Stopwatch.GetTimestamp() - now;
			StartPart();
		}

		/// <summary>A part of the build's work begins: its time and garbage collections count from here, the readings themselves the timings' bookkeeping.</summary>
		private void StartPart()
		{
			var reading = Stopwatch.GetTimestamp();
			partCollections = GC.CollectionCount( 0 );
			partFullCollections = GC.CollectionCount( GC.MaxGeneration );
			partStarted = Stopwatch.GetTimestamp();
			bookkeeping += partStarted - reading;
		}

		/// <summary>The build's work from here on belongs to the stage <paramref name="name"/>.</summary>
		private void Begin( string name ) => Charge( Stopwatch.GetTimestamp(), begins: true, next: name );

		/// <summary>The current stage ends: until the next begins, the build's work is no stage's (a step that only resumes to begin the next adds it no part).</summary>
		private void End() => Charge( Stopwatch.GetTimestamp(), begins: true );

		private IEnumerable<bool> Steps()
		{
			// the save codec's reflection metadata in steps, one type each, as a park start makes it for its first snapshot (ParkResumeJson.WarmupSteps): a process's first parse
			// makes it in one go otherwise, about 0.3 s of a page's frame; a warm process finds it made
			Begin( "codec" );
			foreach ( var step in ParkResumeJson.WarmupSteps() )
				yield return step;
			// the check at the cut hashes the park's economy with the serializer's default options (WorldStateHash): their metadata in steps too, or the check's frame makes it
			foreach ( var step in WorldStateHash.WarmupSteps() )
				yield return step;
			// the save read over steps: its parse a slice of the bytes each, then its checks in their own steps (ParkResumeSaveFile.DeserializeSteps)
			Begin( "save" );
			ParkResumeSnapshot? parsed = null;
			foreach ( var step in ParkResumeSaveFile.DeserializeSteps( payload.Save, value => parsed = value ) )
				yield return step;
			var saved = parsed!;
			if ( saved.Gate == null || saved.Staff == null )
				throw new InvalidDataException( "The shared park's save is missing its gate or staff-world state." );
			using ( LoadProfile.Measure( "save-mods" ) )
			{
				// a completed comparison: every build of a capture of this session would meet the difference
				if ( !host.ModsMatch() )
					throw new CoopJoinRefusedException( "This game's simulation mods differ from the host's." );
				try { ParkResumeMods.Require( saved.Mods, OpenTpwExtensions.Active ); }
				catch ( InvalidDataException error ) { throw new CoopJoinRefusedException( $"This game's simulation mods differ from the host's park: {error.Message}" ); }
			}
			snapshot = saved;
			End();
			yield return true;
			// a browser copies a level's files on first use: the build waits for them, asking once a slice (the page copies them between frames)
			Begin( "files" );
			while ( LevelDataReady?.Invoke( saved.Theme ) == false )
			{
				endsSlice = true;
				yield return true;
			}
			Begin( "catalogue" );
			var easy = saved.Economy.Easy;
			ObjectCatalog? loaded = null;
			foreach ( var step in ObjectCatalog.LoadSteps( saved.Theme, easy ) )
			{
				loaded = step;
				yield return true;
			}
			var catalog = loaded ?? throw new InvalidOperationException( "The shared park's object catalogue did not load." );
			var settings = BalanceSettings.Load( saved.Theme, easy );
			var economyCatalog = EconomyObjectCatalog.Load( saved.Theme, easy );
			End();
			yield return true;
			IReadOnlyDictionary<string, string> assets;
			Begin( "manifest" );
			if ( flow.joinAssets is { } kept && ReferenceEquals( kept.Catalog, catalog ) && kept.Entries == catalog.Entries.Count )
			{
				assets = kept.Manifest;
				stage!.Status = CoopTimingStatus.Cached;
			}
			else
			{
				var entries = catalog.Entries.Count;
				var manifest = new SortedDictionary<string, string>( StringComparer.Ordinal );
				foreach ( var step in ParkResumeAssetManifest.CaptureSteps( catalog, settings.Theme, settings.IsEasy, manifest ) )
					yield return step;
				assets = manifest;
				// the catalogue did not change while the steps ran between frames
				if ( catalog.Entries.Count == entries )
					flow.joinAssets = (catalog, entries, manifest);
			}
			// both manifests are complete here: a difference is this game's files, and every build of a capture would meet it
			Begin( "validation" );
			try { ParkResumeSaveFile.RequireAssets( saved, catalog, assets ); }
			catch ( InvalidDataException error ) { throw new CoopJoinRefusedException( $"This game's files differ from the host's: {error.Message}" ); }
			foreach ( var step in ParkResumeSaveFile.ValidateSnapshotSteps( saved, catalog, settings, economyCatalog, assets ) )
				yield return step;
			Begin( "economy" );
			var economy = ParkSaveFile.Restore( saved.Economy, settings, economyCatalog );
			End();
			yield return true;
			Begin( "level" );
			scene = new EntityConstructionScope();
			candidate = Level.BeginJoinOverFrames( saved, economy, flow.OnlineFolders, payload.State );
			candidate.ShowDeveloperPanels = false;
			yield return true;
			while ( candidate.Step() )
			{
				LevelSteps++;
				// a step that announced a long one next ends the slice, so the long one starts a slice of its own (as GameFlow.PumpParkBuild does)
				endsSlice = candidate.NextStepIsLong;
				yield return true;
			}
			// in the candidate's scene, as an entry makes them in the park's: the HUD, then a fresh advisor as an entry decides it (the level's last step and the HUD share a step,
			// each charged its own part)
			Begin( "hud" );
			hud = flow.CreateParkHud( candidate, flow.HudStatusFor( candidate ) );
			hudLanguage = GameLanguage.Version;
			End();
			yield return true;
			// a game without its advisor leaves the advisor's stage one that did not run
			if ( flow.AutomaticAdvisorEnabled && !flow.advisorUnavailable )
			{
				Begin( "advisor" );
				// [EXT:park-entry-preparation] its shipped message tables in steps of their own, as a park start's build warms them, before the advisor reads them all; a table that
				// fails is left to the advisor, which meets the same failure as before
				foreach ( var more in Guarded( "advisor's tables", AdvisorTables.WarmSteps() ) )
				{
					endsSlice = !more;
					yield return true;
				}
				try
				{
					advisor = AutomaticAdvisor.TryCreate();
					// an entry's rule: a game whose advisor cannot be made tries no more (the swap records it); a throw below is this build's alone
					advisorTried = true;
					if ( advisor != null )
					{
						// its clock starts with the joined park's first update, not here: the build's frames still to come are no time the advisor waits
						advisor.HoldClock();
						advisor.AttachLevel( candidate );
						advisor.CaptionSink = hud.PostAdvisorCaption;
						advisor.PlayerTickets = flow.PlayerTickets;
					}
				}
				catch ( Exception error ) when ( error is not OutOfMemoryException )
				{
					// the shared park goes on without advice rather than fail its join, and the game's later parks try again
					advisor?.Dispose();
					advisor = null;
					advisorTried = false;
					Log.Warning( $"The shared park's advisor could not start: {error.Message}" );
				}
			}
			// [EXT:park-entry-preparation] the one-time work of the first update of each entity that can do it ahead (its resources, presentation only), so the swap's frame does
			// not; an entity whose preparation fails leaves it to that update, which meets the same failure as before
			Begin( "prepare" );
			foreach ( var entity in Entity.All.ToArray() )
			{
				if ( entity is IFirstFramePreparation prepared )
				{
					foreach ( var step in Guarded( entity.GetType().Name, prepared.PrepareFirstFrame() ) )
						yield return step;
				}
			}
			// the swimmers' sets, read ahead; their renderer is still made in the first frame, in its place among the entities
			foreach ( var step in Guarded( "swim sprites", candidate.PrepareSwimSpriteSteps() ) )
				yield return step;
			// what the joined park's advisor reads for its first line, as a park start's build reads it (AdvisorEntryPreparation), into this build's own store: the live game's
			// frames drop the shared one, and the swap hands this one over to the advisor's first update
			if ( advisor != null )
			{
				foreach ( var step in Guarded( "advisor", AdvisorSteps() ) )
					yield return step;
			}
			// the check at the cut hashes the park's economy: its serialization runs once here first, a part a step (WorldStateHash.SerializationWarmupSteps), so the check's
			// frame runs none of its code for the first time
			if ( candidate.Park?.Economy is { } joinedEconomy )
			{
				Begin( "digest" );
				foreach ( var step in WorldStateHash.SerializationWarmupSteps( joinedEconomy ) )
					yield return step;
			}
		}

		/// <summary>
		/// The joined park's advisor's preparation: its shipped tables, then what its presentation reads (<see cref="AdvisorEntryPreparation.PresentationSteps"/>, a false
		/// step announcing a long one next, which then starts a slice of its own).
		/// </summary>
		private IEnumerable<bool> AdvisorSteps()
		{
			try
			{
				_ = AdvisorResponses.Table;
				_ = AdvisorCaptionTable.Table;
				_ = AdvisorCostumeTable.Table;
			}
			catch ( Exception error ) when ( error is not OutOfMemoryException ) { /* only a head start: the first line meets the same failure and handles it as before */ }
			// the presentation's steps start with the advisor shader's program, a long step
			endsSlice = true;
			yield return true;
			// a fresh advisor makes its presentation, and the global model with it, when it says its first line
			foreach ( var more in AdvisorEntryPreparation.PresentationSteps( FileSystem, candidate!.LevelName, globalRig: true, into: advisorPrepared ) )
			{
				endsSlice = !more;
				yield return true;
			}
			// the lines the joined park's advisor says first (its Welcome), decoded a slice a step in the language it will speak
			foreach ( var more in AdvisorEntryPreparation.LineSteps( FileSystem, candidate.LevelName, GameLanguage.IsSelected ? GameLanguage.Current : null,
				AutomaticAdvisor.GameTypeOf( candidate ), into: advisorPrepared ) )
			{
				endsSlice = !more;
				yield return true;
			}
		}

		/// <summary>A preparation's steps, a failure ending them (logged): what they left undone the park's first frame does, meeting the same failure as before.</summary>
		private static IEnumerable<bool> Guarded( string what, IEnumerable<bool> steps )
		{
			using var parts = steps.GetEnumerator();
			while ( true )
			{
				bool more;
				try { more = parts.MoveNext(); }
				catch ( Exception error ) when ( error is not OutOfMemoryException )
				{
					Log.Warning( $"The shared park's {what} could not prepare its first frame: {error.Message}" );
					yield break;
				}
				if ( !more )
					yield break;
				yield return parts.Current;
			}
		}

		public GameJoinEntry Stage()
		{
			if ( !complete || abandoned || installed || scene == null || candidate == null || hud == null || snapshot == null )
				throw new InvalidOperationException( "The shared park is not built." );
			scene.Resume();
			// the front end's own rebuild may have switched the language since the HUD was made: it is made again in the new one
			if ( hudLanguage != GameLanguage.Version )
			{
				// the park lives on: the old HUD must not go on hearing it
				var retired = hud;
				hud = null;
				retired.Retire();
				hud = flow.CreateParkHud( candidate, flow.HudStatusFor( candidate ) );
				hudLanguage = GameLanguage.Version;
				if ( advisor != null )
					advisor.CaptionSink = hud.PostAdvisorCaption;
			}
			scene.Detach();
			return new GameJoinEntry( scene, candidate, hud, advisor, advisorTried, advisorPrepared, snapshot.Theme, snapshot.SoundSeed, snapshot.StartKind );
		}

		public void MarkInstalled() => installed = true;

		public void Abandon()
		{
			if ( abandoned || installed )
				return;
			abandoned = true;
			// the profile was left paused at the end of the build's last slice
			LoadProfile.Resume();
			try
			{
				advisor?.Dispose();
				hud?.Retire();
			}
			finally
			{
				advisor = null;
				hud = null;
				try
				{
					// what this build read for its advisor goes with it
					advisorPrepared.Drop();
				}
				finally
				{
					steps.Dispose();
					// the scene disposes the candidate and its entities and brings the live scene back, in any state it was left in
					scene?.Dispose();
				}
			}
		}
	}
}

/// <summary>[EXT:online-coop-snapshot-join] What entering a built join candidate needs (<see cref="IGameJoinBuild.Stage"/>).</summary>
internal sealed record GameJoinEntry( EntityConstructionScope Scene, Level Level, ParkHud Hud, AutomaticAdvisor? Advisor, bool AdvisorTried,
	AdvisorEntryPreparation.Store AdvisorPrepared, string Theme, uint SoundSeed, ParkStartKind StartKind );

/// <summary>[EXT:online-coop-snapshot-join] A join's build as the game installs it.</summary>
internal interface IGameJoinBuild : ICoopJoinBuild
{
	/// <summary>
	/// Makes the completed candidate ready to be activated: its scene detached with the live scene current, the HUD made again if the language changed since. Called once, by the swap,
	/// right before the live scene is torn down.
	/// </summary>
	GameJoinEntry Stage();

	/// <summary>The candidate is the game's park now: <see cref="ICoopJoinBuild.Abandon"/> changes nothing from here on.</summary>
	void MarkInstalled();
}

using OpenTPW.Hud;
using OpenTPW.Mods;
using System.Diagnostics;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// [EXT:park-build-over-frames] A park chosen in the front end (or loaded, or visited) is built over several frames while the
/// current scene keeps running, instead of in one frame of about half a second in the browser (docs/BROWSER-PERFORMANCE.md,
/// "Building a park over frames"). The level's construction runs its steps in the same order either way; its scene is staged
/// beside the live one (<see cref="EntityConstructionScope"/>) and replaces it once complete, in the same frame. Meanwhile the
/// live scene draws and plays on, input waits and a progress line shows.
/// </summary>
internal sealed partial class GameFlow
{
	/// <summary>Construction time per frame (with the live scene's own couple of milliseconds still inside 60 Hz); a longer step runs whole.</summary>
	internal const double ParkBuildBudgetMilliseconds = 12;

	private sealed class ParkBuild
	{
		public required EntityConstructionScope Scene { get; init; }
		public required Level Level { get; init; }
		public required string LevelName { get; init; }
		public required bool Original { get; init; }
		public ParkVisitInfo? Visit { get; init; }
		public Action<Level>? Ready { get; init; }
		public Action<Level>? BeforeEnter { get; init; }
		// The load profile's sections of a park built in one go, open across the frames (time between them is left out).
		public LoadProfile.Section StartSection { get; init; }
		public LoadProfile.Section LevelSection { get; init; }
		public List<double> FrameMilliseconds { get; } = new();
		public bool Constructed { get; set; }
		// [EXT:park-entry-preparation] what the entering frame would otherwise do in one go for the park's first restart snapshot, run in steps once the park is built
		public IEnumerator<bool>? Preparation { get; set; }
		public bool Prepared { get; set; }
		public bool PreparationStepped { get; set; }
		// [EXT:park-entry-preparation] the park HUD, made by a preparation step as EnterLevel would make it; EnterLevel takes it
		public ParkHud? Hud { get; set; }
	}

	private ParkBuild? parkBuild;

	/// <summary>
	/// The last park built over frames, for the browser harness: the construction time of each frame, the last one including
	/// retiring the live scene and entering the park.
	/// </summary>
	internal IReadOnlyList<double> LastParkBuildFrames { get; private set; } = Array.Empty<double>();

	/// <summary>True while a park is being built over frames.</summary>
	public bool BuildingPark => parkBuild != null;

	/// <summary>The HUD a build step has made for the park being built, until it is entered (smoke tests).</summary>
	internal ParkHud? PreparedHud => parkBuild?.Hud;

	/// <summary>
	/// <see cref="StartLevel(string, bool, bool, ParkVisitInfo?, ParkStartKind)"/>, built over frames when <paramref name="overFrames"/>
	/// is set; <paramref name="ready"/> runs once the park is entered (at once when it is built in one go).
	/// </summary>
	private void StartLevel( string levelName, bool original, bool developerPanels, bool overFrames, ParkVisitInfo? visit = null,
		ParkStartKind start = ParkStartKind.OriginalSaveReference, Action<Level>? ready = null, Action<Level>? beforeEnter = null )
	{
		RequireProgressSaved();
		// [EXT:online-coop-snapshot-join] the player goes elsewhere: the join they leave ends before this park builds a scene of its own
		LeaveJoinBuild();
		// [EXT:extensions-apply-at-load] from the lobby (no park simulates) a new park is built with the extension choices waiting; a park still running keeps its set until it is left
		if ( Level == null )
			ApplyPendingExtensions();
		// A language chosen just before the park applies before it is built, as the tear-down of a park built in one go applies it;
		// one whose files are still coming waits for that tear-down. (Only a language chosen inside the park being left can still be
		// pending here; the front end applies its choice at once. That park's screens keep their strings for its last frames, and a
		// dropped build always comes with a queued transition that replaces them too.)
		if ( overFrames && pendingLanguage != null )
			ApplyPendingLanguage();
		// [EXT:extensions-apply-at-load] with extension choices waiting while a park runs (a shipped park, the sandbox or a visit started from inside a park), the new park is built in one go, as for a waiting
		// language, so the running park never simulates with the new set: below, after the tear-down, or right before the candidate is built
		if ( !overFrames || pendingLanguage != null || pendingExtensions != null )
		{
			if ( beforeEnter != null )
			{
				using var candidateScene = new EntityConstructionScope();
				Level? candidate = null;
				var live = Level;
				var extensionsBefore = OpenTpwExtensions.Active;
				var extensionsWaiting = pendingExtensions;
				ApplyPendingExtensions();
				try
				{
					candidate = new Level( levelName, loadOriginalLevel: original, visit: visit, onlineFolders: OnlineFolders, start: start ) { ShowDeveloperPanels = developerPanels, ClientToolGlobals = ProcessToolGlobals };
					beforeEnter( candidate );
					RequireProgressSaved();
					candidateScene.Detach();
					TearDown( applyPendingLanguage: false );
					candidateScene.Activate();
					var entered = EnterLevel( candidate, levelName, original, visit );
					ready?.Invoke( entered );
				}
				catch ( Exception error )
				{
					candidate?.Dispose();
					// the park that was running is still the live one: it keeps its set and the choice waits again
					if ( extensionsWaiting != null && ReferenceEquals( Level, live ) )
					{
						SwitchBack( extensionsBefore, error );
						pendingExtensions = extensionsWaiting;
					}
					throw;
				}
				return;
			}
			var level = StartLevel( levelName, original, developerPanels, visit, start );
			ready?.Invoke( level );
			return;
		}
		AbandonParkBuild();
		var startSection = LoadProfile.Measure( "start-level" );
		var levelSection = LoadProfile.Measure( "level" );
		var scene = new EntityConstructionScope();
		try
		{
			var level = Level.BeginOverFrames( levelName, original, visit, OnlineFolders, start );
			level.ShowDeveloperPanels = developerPanels;
			scene.Pause();
			parkBuild = new ParkBuild
			{
				Scene = scene, Level = level, LevelName = levelName, Original = original, Visit = visit, Ready = ready, BeforeEnter = beforeEnter,
				StartSection = startSection, LevelSection = levelSection,
			};
			LoadProfile.Pause();
		}
		catch
		{
			scene.Dispose();
			throw;
		}
	}

	/// <summary>Builds the park for this frame's budget; once it is complete, retires the live scene and enters the park.</summary>
	private void PumpParkBuild()
	{
		var build = parkBuild!;
		var started = Stopwatch.GetTimestamp();
		LoadProfile.Resume();
		build.Scene.Resume();
		bool OverBudget() => Stopwatch.GetElapsedTime( started ).TotalMilliseconds >= ParkBuildBudgetMilliseconds;
		try
		{
			while ( !build.Constructed && !OverBudget() )
			{
				build.Constructed = !build.Level.Step();
				// the step just run announced that the next one is long: it starts a frame of its own (docs/BROWSER-PERFORMANCE.md, "Build steps")
				if ( !build.Constructed && build.Level.NextStepIsLong )
					break;
			}
		}
		catch ( Exception error )
		{
			// Only the guarded New request turns expected delayed construction refusal into its own dialog.
			parkBuild = null;
			DropAdvisorPreparation();
			if ( build.BeforeEnter != null ) build.Level.Dispose();
			build.Scene.Dispose();
			if ( build.BeforeEnter != null && ProgressFailure( error ) )
			{
				build.LevelSection.Dispose(); build.StartSection.Dispose();
				ShowProgressFailure( error );
				return;
			}
			throw;
		}
		// [EXT:park-entry-preparation] the park's first restart snapshot, taken as it is entered, needs the codec's reflection metadata and the hash of the asset manifest:
		// together about 0.5 s in the browser. Both are made here in steps instead, within the frame budget, and the snapshot then finds them ready.
		var preparedNow = false;
		if ( build.Constructed && !build.Prepared )
		{
			build.Preparation ??= PrepareEntry( build ).GetEnumerator();
			while ( !OverBudget() && build.Preparation != null )
			{
				bool more;
				try { more = build.Preparation.MoveNext(); build.PreparationStepped |= more; }
				catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
					or NotSupportedException or ArgumentException )
				{
					// only a head start: the snapshot taken on entering meets the same refusal and reports it as before
					more = false;
				}
				if ( !more )
				{
					build.Preparation = null;
					build.Prepared = true;
					// entering is the heaviest step of all (the snapshot, the advisor, the first update): after preparation work it gets a frame of its own
					preparedNow = build.PreparationStepped;
				}
				// false: the step just run announced that the next one is long, which starts a frame of its own (docs/BROWSER-PERFORMANCE.md, "Build steps")
				else if ( !build.Preparation.Current )
					break;
			}
		}
		// Entering (retiring the live scene, park audio, HUD, then the park's first update and draw) waits for a frame of its own
		// when this one has used its budget.
		if ( !build.Constructed || !build.Prepared || preparedNow || OverBudget() )
		{
			build.Scene.Pause();
			LoadProfile.Pause();
			build.FrameMilliseconds.Add( Stopwatch.GetElapsedTime( started ).TotalMilliseconds );
			return;
		}
		parkBuild = null;
		build.LevelSection.Dispose();
		try
		{
			RequireProgressSaved();
			build.BeforeEnter?.Invoke( build.Level );
			build.Scene.Detach();
			using ( LoadProfile.Measure( "tear-down" ) )
				TearDown( applyPendingLanguage: false, keepAdvisorPreparation: true );
			build.Scene.Activate();
		}
		catch ( Exception error )
		{
			// A profile precommit refusal occurs before the previous world is retired; what the build read for the advisor is not used.
			DropAdvisorPreparation();
			if ( build.BeforeEnter != null || error is PlayerProgressSaveException ) build.Level.Dispose();
			DropPreparedHud( build );
			build.Scene.Dispose();
			if ( error is PlayerProgressSaveException || build.BeforeEnter != null && ProgressFailure( error ) )
			{
				build.StartSection.Dispose();
				ShowProgressFailure( error );
				return;
			}
			throw;
		}
		var level = EnterLevel( build.Level, build.LevelName, build.Original, build.Visit, build.Hud );
		build.StartSection.Dispose();
		build.Ready?.Invoke( level );
		build.FrameMilliseconds.Add( Stopwatch.GetElapsedTime( started ).TotalMilliseconds );
		LastParkBuildFrames = build.FrameMilliseconds;
		Transitioned?.Invoke();
	}

	/// <summary>
	/// The steps that make ready what entering the park would do whole: see <see cref="ParkBuild.Preparation"/>. A step that returns false did its
	/// work and announces that the next one is long: the frame ends after it, and the long step starts the next frame (<see cref="PumpParkBuild"/>).
	/// </summary>
	private IEnumerable<bool> PrepareEntry( ParkBuild build )
	{
		// what an earlier build read and nobody took (one that failed while no park was live) is not this park's
		DropAdvisorPreparation();
		foreach ( var step in PrepareAdvisorAndSnapshot( build ) )
			yield return step;
		// the own player progress a progress park writes as it is entered (PlayerProgressAdmission.BeforeRetirement): its serializer's metadata, as a head start
		if ( build.BeforeEnter != null && HeadStart( "the player progress file's serializer", PlayerTicketFile.WarmUp ) )
			yield return true;
		foreach ( var step in PrepareFirstFrame( build ) )
			yield return step;
	}

	/// <summary>
	/// What the entering frame's first draw and HUD would make (docs/BROWSER-PERFORMANCE.md, "The entering frame"): the level's sky, the park HUD,
	/// created as <see cref="EnterLevel"/> creates it, and the interface textures of the HUD's first draw. The HUD is made last of all the steps, as
	/// in the entering frame it is made after everything the other steps prepare, with the staged scene current as it is there; the level does not
	/// update before it is entered, and input waits while it is built, so the HUD meets the same park. What it reads from the frame's input is read
	/// again on entering (<see cref="ParkHud.RefreshEntryInput"/>).
	/// </summary>
	private IEnumerable<bool> PrepareFirstFrame( ParkBuild build )
	{
		// each part is only a head start: whatever fails here is logged once and left to the entering frame, which meets the same failure there,
		// and the build goes on (an exception leaving a step would leave the staged scene current)
		if ( !HeadStart( "the sky", build.Level.PrepareSky ) )
			yield break;
		yield return true;
		if ( build.Level.IsThemeParkIncSlice )
			yield break;
		if ( !HeadStart( "the park HUD", () =>
		{
			using ( LoadProfile.Measure( "park-hud" ) )
				build.Hud = CreateParkHud( build.Level, HudStatusFor( build.Level ) );
		} ) )
			yield break;
		yield return true;
		List<UiTexture>? textures = null;
		if ( !HeadStart( "the HUD's interface textures", () => textures = build.Hud!.EntryTextures( Context ).ToList() ) )
			yield break;
		foreach ( var texture in textures! )
		{
			// a texture-pack switch, started at any step, reloads the interface images itself
			if ( TexturePackSwitch.Current != null || !HeadStart( "the HUD's interface textures", () => overlayRenderer.Prepare( texture ) ) )
				yield break;
			yield return true;
		}
	}

	/// <summary>Runs one part of <see cref="PrepareFirstFrame"/>; false, with one warning, when it failed.</summary>
	private static bool HeadStart( string what, Action prepare )
	{
		try
		{
			prepare();
			return true;
		}
		catch ( Exception error )
		{
			Log.Warning( $"Park entry preparation: {what} left to the entering frame ({error.GetType().Name}: {error.Message})." );
			return false;
		}
	}

	/// <summary>Discards a HUD prepared for a park that is not entered, as <see cref="TearDown"/> discards the HUD of a park it retires.</summary>
	private static void DropPreparedHud( ParkBuild build )
	{
		build.Hud?.Stack.Clear();
		build.Hud = null;
	}

	/// <summary>The advisor's and the first restart snapshot's preparation. None for a park without the automatic advisor and a restart snapshot.</summary>
	private IEnumerable<bool> PrepareAdvisorAndSnapshot( ParkBuild build )
	{
		var snapshot = build.Original && build.Visit == null && TakesRestartSnapshot( build.Level );
		// the automatic advisor EnterLevel creates and whose first line it says: its settings, its shipped tables and what its presentation reads (AdvisorEntryPreparation)
		if ( AutomaticAdvisorEnabled && build.Original && build.Visit == null && !advisorUnavailable && !build.Level.IsThemeParkIncSlice )
		{
			if ( Advisor == null )
			{
				foreach ( var step in AdvisorTables.WarmSteps() )
					yield return step;
				AdvisorEntryPreparation.Prepare( AdvisorEntryPreparation.BalanceKey, FileSystem, AdvisorBalance.Load );
				yield return true;
			}
			try
			{
				_ = AdvisorResponses.Table;
				_ = AdvisorCaptionTable.Table;
				_ = AdvisorCostumeTable.Table;
			}
			catch ( Exception ) { /* only a head start: the first line meets the same failure and handles it as before */ }
			var globalRig = Advisor?.Presentation == null;
			// false: the presentation's steps start with the advisor shader's program, a long step
			yield return !globalRig;
			// then the lines the park's start says first, decoded a slice a step; after them, a separate boundary announces the economy capture, whose first step is a long one
			var advisorSteps = AdvisorEntryPreparation.PresentationSteps( FileSystem, build.Level.LevelName, globalRig )
				.Concat( AdvisorEntryPreparation.LineSteps( FileSystem, build.Level.LevelName, GameLanguage.IsSelected ? GameLanguage.Current : null,
					AutomaticAdvisor.GameTypeOf( build.Level ) ) );
			foreach ( var step in snapshot ? EndingBeforeLong( advisorSteps ) : advisorSteps )
				yield return step;
		}
		if ( !snapshot )
			yield break;
		foreach ( var step in build.Level.WarmResumeCaptureSteps() )
			yield return step;
		foreach ( var step in ParkResumeJson.WarmupSteps() )
			yield return step;
		// the sliced serializer the snapshot's write starts in the entering frame
		if ( HeadStart( "the sliced serializer", SlicedJsonSerializer.WarmUp ) )
			yield return true;
		foreach ( var step in build.Level.PrepareAutosaveAssets() )
			yield return step;
	}

	/// <summary>Passes through each step without lookahead, then ends the frame before the following long step (<see cref="PumpParkBuild"/>).</summary>
	internal static IEnumerable<bool> EndingBeforeLong( IEnumerable<bool> steps )
	{
		foreach ( var step in steps )
			yield return step;
		// Never advance a side-effecting iterator to discover its last step: that can run the next long step in this frame.
		yield return false;
	}

	/// <summary>Drops what <see cref="PrepareEntry"/> read for the advisor and no advisor took.</summary>
	private static void DropAdvisorPreparation() => AdvisorEntryPreparation.Drop();

	/// <summary>Drops a park still being built (another transition came after it, and the latest wins); the live scene stays current.</summary>
	private void AbandonParkBuild()
	{
		if ( parkBuild is not { } build )
			return;
		parkBuild = null;
		LoadProfile.Resume();
		DropPreparedHud( build );
		build.Scene.Dispose();
		DropAdvisorPreparation();
		Log.Trace( $"Stopped building {build.LevelName}: another transition came first." );
	}

	/// <summary>The progress line while a park is built, in the style of the texture switch's (<see cref="OptionsScreen.DrawProgressLine"/>).</summary>
	private void DrawParkBuildProgress()
	{
		if ( parkBuild is { } build )
			OptionsScreen.DrawProgressLine( Context, Strings.Extra( OpenTpwText.BuildingPark ), build.Level.ConstructionProgress );
	}
}

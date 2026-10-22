using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// Hooks for the browser performance harness (tools/web-perf, docs/BROWSER-PERFORMANCE.md). They drive the game through the
/// same public UI calls as the native front-end smoke test (a click on an element by id, a key, a buy-window selection) and read
/// the managed memory counters; the page measures the frames. Nothing here changes what a player sees unless the harness calls it.
/// </summary>
public static partial class Program
{
	/// <summary>Where the game is: the front-end screen, the park and its HUD, the buy window and its selection, as JSON.</summary>
	[JSExport]
	public static string PerfState()
	{
		var state = new JsonObject
		{
			["started"] = flow != null,
			["menu"] = flow?.Menu?.Stack.Top?.Name,
			["island"] = flow?.Menu?.Selected.Level,
			["level"] = flow?.Level?.OriginalPark?.LevelName,
			["hud"] = flow?.Hud != null,
			["hudScreen"] = flow?.Hud?.Stack.Top?.Name,
			["buyOpen"] = flow?.Hud?.BuyWindowOpen ?? false,
			["selected"] = flow?.Hud?.SelectedBuyItem?.Entry?.ArchiveName,
			["turn"] = flow?.Level?.Park?.Economy.Turn,
		};
		return state.ToJsonString();
	}

	/// <summary>Records where textures made from bytes come from, for <see cref="PerfTextureGroups"/>.</summary>
	[JSExport]
	public static void PerfTrackTextureOrigins( bool enabled ) => Texture.TrackOrigins = enabled;

	/// <summary>Turns the load profile (<see cref="LoadProfile"/>) on, clearing it, or off.</summary>
	[JSExport]
	public static void PerfProfile( bool enabled ) => LoadProfile.Enable( enabled );

	/// <summary>The load profile's sections as JSON: name → total milliseconds and count, largest first.</summary>
	[JSExport]
	public static string PerfLoadProfile()
	{
		var result = new JsonObject();
		foreach ( var (name, (milliseconds, count)) in LoadProfile.Snapshot().OrderByDescending( entry => entry.Value.Milliseconds ) )
			result[name] = new JsonObject { ["ms"] = Math.Round( milliseconds, 1 ), ["count"] = count };
		return result.ToJsonString();
	}

	/// <summary>Frames the renderer has drawn; a frame the cap skips leaves it unchanged, so the page can tell drawn frames from skipped calls.</summary>
	[JSExport]
	public static double PerfFramesDrawn() => renderer?.FramesDrawn ?? 0;

	/// <summary>Clicks the centre of the visible element <paramref name="id"/> on the topmost front-end or HUD screen; false when there is none.</summary>
	[JSExport]
	public static bool PerfClick( string id )
	{
		if ( flow == null )
			return false;
		var screens = new[] { flow.Menu?.Stack.Top, flow.Menu?.Main, flow.Hud?.Stack.Top, flow.Hud?.Screen };
		foreach ( var screen in screens )
		{
			if ( screen?.Find( id ) is { Visible: true } element )
			{
				flow.InjectedInput = UiInput.Click( element.ScreenRect( flow.Context.Canvas ).Center );
				return true;
			}
		}
		return false;
	}

	/// <summary>The construction time of each frame of the last park built over frames (GameFlow.LastParkBuildFrames), as a JSON array of milliseconds.</summary>
	[JSExport]
	public static string PerfParkBuild() => new JsonArray( (flow?.LastParkBuildFrames ?? Array.Empty<double>() ).Select( value => (JsonNode?)Math.Round( value, 2 ) ).ToArray() ).ToJsonString();

	/// <summary>
	/// Turns the per-frame named work (<see cref="FrameMarks"/>) on or off; the page reads it for the frames it finds slow (<see cref="PerfTakeMarks"/>).
	/// The WebGL calls are recorded with it, by kind (<see cref="Veldrid.WebGlMarks"/>: "gl.uploadTexture", "gl.createResourceSet", ...).
	/// </summary>
	[JSExport]
	public static void PerfFrameMarks( bool enabled )
	{
		FrameMarks.Enable( enabled );
		Veldrid.WebGlMarks.Sink = enabled ? FrameMarks.Add : null;
	}

	/// <summary>The named work of the frame just run, as JSON: name → [milliseconds, count], largest first (sections nest, so a parent includes its children).</summary>
	[JSExport]
	public static string PerfTakeMarks() => FrameMarks.Snapshot().ToJsonString();

	/// <summary>Opens a HUD window by its shortcut name (buy, finance, research, map, loans, staff, parkInfo, entryPrice, staffCosts, attractions, visitors); false for another name or outside a park.</summary>
	[JSExport]
	public static bool PerfOpenWindow( string name )
	{
		if ( flow?.Hud is not { } hud )
			return false;
		Action? open = name switch
		{
			"buy" => hud.OpenBuyWindow,
			"finance" => hud.OpenFinanceWindow,
			"research" => hud.OpenResearchWindow,
			"map" => hud.OpenMapWindow,
			"loans" => hud.OpenLoansWindow,
			"staff" => hud.OpenStaffWindow,
			"parkInfo" => hud.OpenParkInfoWindow,
			"entryPrice" => hud.OpenEntryPriceWindow,
			"staffCosts" => hud.OpenStaffCostsWindow,
			"attractions" => hud.OpenAttractionsWindow,
			"visitors" => hud.OpenVisitorWindow,
			_ => null,
		};
		if ( open == null )
			return false;
		open();
		return true;
	}

	/// <summary>Sets the park speed (0 paused, 1 normal, 2 fast, 4 fastest) the way the speed keys do; false for another value or outside a park.</summary>
	[JSExport]
	public static bool PerfSpeed( int speed )
	{
		if ( flow?.Hud is not { } hud || !Enum.IsDefined( typeof( GameSpeed ), speed ) )
			return false;
		hud.SetUserGameSpeed( (GameSpeed)speed );
		return true;
	}

	/// <summary>Posts advisor advice <paramref name="message"/> as game code does (the advisor says it when its queue next serves); false without an advisor.</summary>
	[JSExport]
	public static bool PerfAdvisorAdvice( int message )
	{
		if ( flow?.Advisor is not { IsAttached: true } advisor )
			return false;
		advisor.PostEventAdvice( message, "the perf harness" );
		return true;
	}

	/// <summary>Whether the advisor is speaking now.</summary>
	[JSExport]
	public static bool PerfAdvisorSpeaking() => flow?.Advisor?.IsSpeechActive == true;

	/// <summary>Queues a cheat for the next park turn (kind: money, weather, arrival, research; argument: the weather or vehicle); false when the park refuses it.</summary>
	[JSExport]
	public static bool PerfCheat( string kind, int argument )
	{
		if ( flow?.Level?.Park?.Economy is not { } economy || !Enum.TryParse<CheatKind>( kind, ignoreCase: true, out var parsed ) )
			return false;
		economy.QueueCheat( new CheatAction( CheatKind.Mode ) );
		return economy.QueueCheat( new CheatAction( parsed, argument ) );
	}

	/// <summary>Completes research items as researchers' points would (every category at full effort, <paramref name="points"/> points), with the park's events for them; returns the items completed.</summary>
	[JSExport]
	public static int PerfResearch( int points )
	{
		if ( flow?.Level?.Park?.Economy is not { } economy )
			return -1;
		foreach ( var category in Enum.GetValues<ResearchCategory>() )
			economy.Research.SetEffort( category, 100 );
		var done = economy.Research.AddResearcherPoints( points );
		economy.ApplyResearch( done );
		return done.Count;
	}

	/// <summary>Runs the park's economy forward to <paramref name="ticks"/> fixed ticks before the next month's (or, for "year", the next year's) start; the park's guests and rides do not run meanwhile, so the park is not a real run afterwards. False for another unit or outside a park.</summary>
	[JSExport]
	public static bool PerfSkipToBoundary( string unit, int ticks )
	{
		if ( flow?.Level?.Park?.Economy is not { } economy )
			return false;
		var month = ParkCalendar.MonthIndex( economy.Tick );
		long boundary;
		if ( unit == "month" )
			boundary = ParkCalendar.TickAtMonth( month + 1 );
		else if ( unit == "year" )
			boundary = ParkCalendar.TickAtMonth( (month / 12 + 1) * 12 );
		else
			return false;
		economy.Advance( Math.Max( 0, boundary - ticks - economy.Tick ) );
		return true;
	}

	/// <summary>Adds <paramref name="count"/> guests on the park's walkable paths (<see cref="UmbrellaPerf.Stage"/> without weather or umbrellas); the park is not a real run afterwards. Returns the guests in the park, or -1.</summary>
	[JSExport]
	public static int PerfCrowd( int count ) =>
		flow?.Level?.Guests is { } guests ? UmbrellaPerf.Stage( guests, null, count, 0, false ).Guests : -1;

	/// <summary>
	/// [EXT:online-coop-lockstep] The page runs the performance harness (main.js calls this with <c>?perf=1</c> on a loopback address): its hooks change the park outside the commands,
	/// so this page shares no park (<see cref="CoopSession.CanShare"/>).
	/// </summary>
	[JSExport]
	public static void PerfMode() => CoopSession.ChangesParkOutsideCommands = true;

	/// <summary>
	/// [EXT:online-coop-lockstep] The co-op session digest's cost on the running park (<see cref="CoopDigest.MeasureCost"/>, docs/COOP.md "Measurements"), as JSON: the median of
	/// <paramref name="repeats"/> digests and of as many world hashes in milliseconds, with the park's guests and objects; null outside a park.
	/// </summary>
	[JSExport]
	public static string? PerfSessionDigest( int repeats )
	{
		if ( flow?.Level is not { Guests: { } guests } level || repeats < 1 )
			return null;
		var (digest, worldHash) = CoopDigest.MeasureCost( level, repeats );
		return new JsonObject { ["digestMs"] = Math.Round( digest, 2 ), ["worldHashMs"] = Math.Round( worldHash, 2 ), ["guests"] = guests.Guests.Count, ["objects"] = level.Objects.Objects.Count }.ToJsonString();
	}

	/// <summary>Builds a coaster station with a small circuit near the paths (<see cref="Level.BuildDemoCoaster"/>, finished or left open with the tool active); true when it fitted. The coaster is free and not a real run's.</summary>
	[JSExport]
	public static bool PerfBuildCoaster( bool finished ) => flow?.Level?.BuildDemoCoaster( finished, null ) != null;

	/// <summary>Makes the periodic autosave due now, so its capture and slices run in the frames that follow; false outside a park.</summary>
	[JSExport]
	public static bool PerfAutosaveDue()
	{
		if ( flow?.Level == null )
			return false;
		flow.Autosave.MakeDue();
		return true;
	}

	/// <summary>Whether the park's economy has its door open (null economy: false).</summary>
	[JSExport]
	public static bool PerfParkOpen() => flow?.Level?.Park?.Economy.IsParkOpen == true;

	/// <summary>The guests in the park, as the harness's crowd windows report them.</summary>
	[JSExport]
	public static int PerfGuests() => flow?.Level?.Guests?.Guests.Count ?? -1;

	/// <summary>Builds the item with archive <paramref name="archive"/> at the first free cell of the park, as a click on the map would (the economy charges it); returns "x,y" or an empty string when nothing was built.</summary>
	[JSExport]
	public static string PerfPlaceObject( string category, string archive )
	{
		if ( flow?.Hud is not { } hud || flow.Level is not { } level || !Enum.TryParse<BuildCategory>( category, ignoreCase: true, out var parsed ) )
			return "";
		var item = hud.Catalog.GetItems( parsed ).FirstOrDefault( entry => string.Equals( entry.Entry?.ArchiveName, archive, StringComparison.OrdinalIgnoreCase ) );
		if ( item?.Entry is not { } entry )
			return "";
		var grid = level.Objects.Grid;
		var centreX = grid.Width / 2;
		var centreY = grid.Height / 2;
		for ( var radius = 0; radius < Math.Max( grid.Width, grid.Height ) / 2; radius += 2 )
			for ( var dy = -radius; dy <= radius; dy += 2 )
				for ( var dx = -radius; dx <= radius; dx += 2 )
				{
					if ( Math.Max( Math.Abs( dx ), Math.Abs( dy ) ) != radius )
						continue;
					var x = centreX + dx;
					var y = centreY + dy;
					if ( x < 0 || y < 0 || x >= grid.Width || y >= grid.Height )
						continue;
					var anchor = Level.GetCentredAnchor( entry, x, y, 0 );
					if ( level.Objects.Check( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed )
						continue;
					if ( level.PlaceObject( entry, x, y, 0 ) != null )
						return $"{x},{y}";
				}
		return "";
	}

	/// <summary>Presses a UI key by its <see cref="UiKeys"/> name (Accept, Back, Left, ...); false for an unknown name.</summary>
	[JSExport]
	public static bool PerfKey( string key )
	{
		if ( flow == null || !Enum.TryParse<UiKeys>( key, ignoreCase: true, out var parsed ) )
			return false;
		flow.InjectedInput = UiInput.Key( parsed );
		return true;
	}

	/// <summary>Holds the evening look for the session ("night", "dusk", "day"), or turns the cycle and its lamps "off" or back "on" (the extension stays in the mod set); false for another value (see <c>Level.PerfEvening</c>).</summary>
	[JSExport]
	public static bool PerfEvening( string mode ) => Level.PerfEvening( mode );

	/// <summary>Adds <paramref name="count"/> debug lamp lights around the park camera's target (the browser twin of <c>--debug-evening-lamps</c>); 0 removes them; -1 outside a park (see <c>Level.PerfEveningLamps</c>).</summary>
	[JSExport]
	public static int PerfEveningLamps( int count ) => Level.PerfEveningLamps( count );

	/// <summary>Holds the park camera at a zoom value (the wide range reaches 40..300 with the option on) and returns the zoom it ended at; -1 outside a park.</summary>
	[JSExport]
	public static double PerfZoom( double zoom )
	{
		if ( Camera.Mode is not ParkCameraMode camera )
			return -1;
		camera.SetPose( camera.Target, (float)zoom, camera.Yaw );
		return camera.Height;
	}

	private static Action? umbrellaHook;

	/// <summary>
	/// [EXT:debug-umbrellas] The browser twin of the desktop's Debug-build <c>--debug-umbrellas</c> (a capture aid for tools/web-perf, scenario <c>umbrellas</c>): gives every guest an open umbrella (colour from its id) and keeps it open every frame,
	/// and when <paramref name="zoom"/> is above zero holds the park camera on the lowest-numbered visible guest at that zoom value and <paramref name="yaw"/> (degrees). The guests' own umbrella fields are written directly, so a run that calls this is not the
	/// park's own simulation and nothing may be saved from it. Returns false outside a park or when the umbrellas extension has no renderer.
	/// </summary>
	[JSExport]
	public static bool PerfUmbrellas( double zoom, double yaw )
	{
		if ( flow?.Level is not { UmbrellaRenderer: not null } || renderer == null )
			return false;
		if ( umbrellaHook != null )
			renderer.PostUpdate -= umbrellaHook;
		umbrellaHook = () =>
		{
			if ( flow?.Level is not { Guests: { } simulation } level )
				return;
			if ( zoom > 0 && Camera.Mode is ParkCameraMode camera && level.OriginalPark is { } park )
				if ( simulation.Guests.Where( candidate => candidate.IsVisible && float.IsFinite( candidate.DrawX ) ).OrderBy( candidate => candidate.Id ).FirstOrDefault() is { } followed )
				{
					var spot = GuestRenderer.CellToEngine( park.Heightfield, followed.DrawX, followed.DrawY );
					camera.SetPose( new System.Numerics.Vector3( spot.X, spot.Y, 0 ), (float)zoom, (float)yaw );
				}
			foreach ( var guest in simulation.Guests )
			{
				if ( guest.UmbrellaState == UmbrellaRules.StateOpen )
					continue;
				guest.UmbrellaState = UmbrellaRules.StateOpen;
				guest.UmbrellaType = guest.Id % UmbrellaRules.Colours;
				guest.UmbrellaDueTurn = 0;
			}
		};
		renderer.PostUpdate += umbrellaHook;
		return true;
	}

	/// <summary>
	/// [EXT:debug-umbrellas] Stops <see cref="PerfUmbrellas"/> holding every umbrella open and holding the camera (the umbrellas stay as they are until the simulation or <see cref="PerfUmbrellaCrowd"/> changes them): the winterCost
	/// scenario holds them open for its measured windows and lets go of them before its screenshot.
	/// </summary>
	[JSExport]
	public static void PerfUmbrellasOff()
	{
		if ( umbrellaHook != null && renderer != null )
			renderer.PostUpdate -= umbrellaHook;
		umbrellaHook = null;
	}

	/// <summary>Whether it rains in the park now (the weather's rain flag), so a measured window can say whether it ran in rain.</summary>
	[JSExport]
	public static bool PerfRaining() => flow?.Level?.Weather?.Rain == true;

	private static Action? winterHook;

	/// <summary>
	/// [EXT:debug-winter] The browser twin of the desktop's Debug-build <c>--debug-winter</c> (a capture aid for tools/web-perf, scenario <c>winter</c>): dresses every guest in a head item (by its id) and mittens (all but every fourth guest) every frame,
	/// and when <paramref name="zoom"/> is above zero holds the park camera on a visible guest at that zoom value and <paramref name="yaw"/> (degrees): the lowest-numbered one, or the one <paramref name="follow"/> places further up the order, counted from the
	/// highest-numbered guest down when it is negative (a staged crowd starts under the entrance canopy, and its later guests are those furthest down the road). The guests' item field is written directly, so a run that calls this is not the
	/// park's own simulation and nothing may be saved from it. Returns false outside a park or when the winter extension has no renderer.
	/// </summary>
	[JSExport]
	public static bool PerfWinter( double zoom, double yaw, int follow )
	{
		if ( flow?.Level is not { WarmItemRenderer: not null } || renderer == null )
			return false;
		if ( winterHook != null )
			renderer.PostUpdate -= winterHook;
		winterHook = () =>
		{
			if ( flow?.Level is not { Guests: { } simulation } level )
				return;
			if ( zoom > 0 && Camera.Mode is ParkCameraMode camera && level.OriginalPark is { } park )
				if ( simulation.Guests.Where( candidate => candidate.IsVisible && float.IsFinite( candidate.DrawX ) ).OrderBy( candidate => follow < 0 ? -candidate.Id : candidate.Id ).Skip( Math.Abs( follow ) ).FirstOrDefault() is { } followed )
				{
					var spot = GuestRenderer.CellToEngine( park.Heightfield, followed.DrawX, followed.DrawY );
					camera.SetPose( new System.Numerics.Vector3( spot.X, spot.Y, 0 ), (float)zoom, (float)yaw );
				}
			foreach ( var guest in simulation.Guests )
				guest.WarmItems = WinterRules.HeadItemOf( guest.Id ) | (guest.Id % 4 == 3 ? 0 : WinterRules.Mittens);
		};
		renderer.PostUpdate += winterHook;
		return true;
	}

	/// <summary>How many warm items the renderer drew in the last frame, or -1 without one.</summary>
	[JSExport]
	public static int PerfDrawnWinterItems() => flow?.Level?.WarmItemRenderer?.DrawnItems ?? -1;

	private static Action? snowHook;

	/// <summary>
	/// [EXT:debug-snow-cover] The browser twin of the desktop's <c>--debug-snow-cover</c> (docs/WINTER.md): holds the settled snow cover of the park at <paramref name="cover"/> (0..1) every frame, so a capture shows the snow in
	/// the browser build without a snowing sky. Presentation only. Returns false outside a park or while the winter-snow extension is off (no cover exists).
	/// </summary>
	[JSExport]
	public static bool PerfSnow( double cover )
	{
		if ( flow?.Level?.Snow == null || renderer == null )
			return false;
		if ( snowHook != null )
			renderer.PostUpdate -= snowHook;
		snowHook = () => flow?.Level?.Snow?.Set( cover );
		renderer.PostUpdate += snowHook;
		return true;
	}

	/// <summary>How many umbrellas the renderer drew in the last frame, or -1 without one.</summary>
	[JSExport]
	public static int PerfDrawnUmbrellas() => flow?.Level?.UmbrellaRenderer?.DrawnUmbrellas ?? -1;

	/// <summary>
	/// [EXT:umbrellas-tuning] Fills the park with up to <paramref name="crowd"/> extra guests, applies the weather quality (30 is rain) and gives every guest an open umbrella, or none: the umbrellaCost scenario of the harness
	/// (<see cref="UmbrellaPerf"/>). Returns the result as JSON; the park is no longer a real run afterwards.
	/// </summary>
	[JSExport]
	public static string PerfUmbrellaCrowd( int crowd, int quality, bool umbrellas )
	{
		if ( flow?.Level is not { Guests: { } guests } level )
			return "null";
		var result = UmbrellaPerf.Stage( guests, level.Weather, crowd, quality, umbrellas );
		return new JsonObject { ["guests"] = result.Guests, ["spawned"] = result.Spawned, ["umbrellas"] = result.Umbrellas, ["rain"] = result.Rain, ["renderer"] = level.UmbrellaRenderer != null }.ToJsonString();
	}

	/// <summary>[EXT:umbrellas-tuning] Umbrellas drawn in the last frame (0 without the umbrella renderer).</summary>
	[JSExport]
	public static int PerfUmbrellaCrowdDrawn() => flow?.Level?.UmbrellaRenderer?.DrawnUmbrellas ?? 0;

	/// <summary>The items of a buy-window category (Rides, Shops, Sideshows, Features) as JSON: archive name and shown name.</summary>
	[JSExport]
	public static string PerfBuyItems( string category )
	{
		var items = new JsonArray();
		if ( flow?.Hud is { } hud && Enum.TryParse<BuildCategory>( category, ignoreCase: true, out var parsed ) )
			foreach ( var item in hud.Catalog.GetItems( parsed ) )
				items.Add( new JsonObject { ["archive"] = item.Entry?.ArchiveName, ["name"] = hud.ItemName( item ) } );
		return items.ToJsonString();
	}

	/// <summary>Shows the item with archive <paramref name="archive"/> in the open buy window, as a click on its category and row does; false when it is not listed.</summary>
	[JSExport]
	public static bool PerfShowBuyItem( string category, string archive )
	{
		if ( flow?.Hud is not { BuyWindowOpen: true } hud || !Enum.TryParse<BuildCategory>( category, ignoreCase: true, out var parsed ) )
			return false;
		var item = hud.Catalog.GetItems( parsed ).FirstOrDefault( entry => string.Equals( entry.Entry?.ArchiveName, archive, StringComparison.OrdinalIgnoreCase ) );
		if ( item == null )
			return false;
		hud.SelectCategory( item.Category );
		var index = hud.BuyItems.ToList().IndexOf( item );
		if ( index < 0 )
			return false;
		hud.SelectBuyItem( index );
		return true;
	}

	/// <summary>
	/// Deterministic work whose result must not depend on how the runtime executes it (interpreter or AOT, docs/BROWSER-PERFORMANCE.md):
	/// every texture of a theme decoded and hashed, and the fixed run of the original-data determinism test (the theme's park with its
	/// guests from seed 2026, 3000 ticks at the fastest speed) reduced to its world state hash. Both durations come back too; they are
	/// pure computation, without drawing. Needs the theme's files, so the harness calls it after the park has loaded, as its last step:
	/// the call blocks the page for its duration (the running park drops that time, FixedStepClock) and fills the archive cache. The
	/// fixed run covers the economy and the guests, not rides, object scripts, weather or staff.
	/// </summary>
	[JSExport]
	public static string PerfDigest( string theme )
	{
		var result = new JsonObject();
		var watch = System.Diagnostics.Stopwatch.StartNew();
		var textures = new List<string>();
		CollectTextures( $"levels/{theme}", textures );
		// The listing mixes absolute disk paths and root-relative archive paths; both open as they are, and the key is the same for both.
		var root = Path.TrimEndingDirectorySeparator( FileSystem.GetAbsolutePath( "/" ) ).Replace( '\\', '/' );
		string Key( string path )
		{
			var normalized = path.Replace( '\\', '/' );
			return (normalized.StartsWith( root + "/", StringComparison.Ordinal ) ? normalized[root.Length..] : normalized).TrimStart( '/' );
		}
		textures.Sort( ( left, right ) => string.CompareOrdinal( Key( left ), Key( right ) ) );
		var hash = Fnv1a.Start;
		foreach ( var path in textures )
		{
			hash = Fnv1a.Add( hash, System.Text.Encoding.UTF8.GetBytes( Key( path ) ) );
			try
			{
				using var stream = FileSystem.OpenRead( path );
				var data = new TextureFile( stream ).Data;
				hash = Fnv1a.Add( Fnv1a.Add( Fnv1a.Add( hash, BitConverter.GetBytes( data.Width ) ), BitConverter.GetBytes( data.Height ) ), data.Data );
			}
			catch ( Exception exception )
			{
				// A malformed file must fail the same way in both builds.
				hash = Fnv1a.Add( hash, System.Text.Encoding.UTF8.GetBytes( exception.GetType().Name ) );
			}
		}
		result["textures"] = textures.Count;
		result["textureHash"] = hash.ToString( "X16" );
		result["textureMs"] = watch.ElapsedMilliseconds;

		watch.Restart();
		var simulationHash = CrossRuntimeDigest.FixedParkRun( theme, hireStaff: false );
		result["simulationHash"] = simulationHash.ToString( "X16" );
		result["simulationMs"] = watch.ElapsedMilliseconds;
		return result.ToJsonString();
	}

	/// <summary>
	/// The cross-runtime digest of <paramref name="theme"/> (<see cref="CrossRuntimeDigest"/>, docs/DETERMINISM.md "Browser and native"): the same work as the desktop's
	/// <c>--determinism-digest</c>, as JSON, to compare value by value. The theme's files must be loaded (<see cref="PerfLevelLoaded"/>); the call blocks the page for minutes.
	/// </summary>
	[JSExport]
	public static string PerfDeterminismDigest( string theme, bool includeM4 ) => CrossRuntimeDigest.Compute( theme, includeM4 ).ToJsonString();

	/// <summary>Whether the page has copied the files of <paramref name="level"/> (after <c>opentpwPerf.loadLevel</c>).</summary>
	[JSExport]
	public static bool PerfLevelLoaded( string level ) => loadedLevels.Contains( level );

	/// <summary>Every .wct below <paramref name="directory"/>, archives included, as the file system lists them.</summary>
	private static void CollectTextures( string directory, List<string> found )
	{
		foreach ( var file in FileSystem.GetFiles( directory ) )
			if ( file.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) )
				found.Add( file );
		foreach ( var child in FileSystem.GetDirectories( directory ) )
			CollectTextures( child, found );
	}

	/// <summary>64-bit FNV-1a: enough to tell two builds' outputs apart, and available on every runtime.</summary>
	private static class Fnv1a
	{
		public const ulong Start = 14695981039346656037;

		public static ulong Add( ulong hash, ReadOnlySpan<byte> bytes )
		{
			foreach ( var value in bytes )
				hash = unchecked( (hash ^ value) * 1099511628211 );
			return hash;
		}
	}

	/// <summary>A full garbage collection, so the counters that follow show live memory rather than garbage not collected yet.</summary>
	[JSExport]
	public static void PerfCollect()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	/// <summary>The textures in <see cref="Asset.All"/> grouped by the first two parts of their path (or "(no path)"), as JSON counts.</summary>
	[JSExport]
	public static string PerfTextureGroups()
	{
		var result = new JsonObject();
		foreach ( var group in Asset.All.OfType<Texture>().GroupBy( texture => string.IsNullOrEmpty( texture.Path ) ? $"(no path) {texture.Origin ?? "?"}"
			: string.Join( '/', texture.Path.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries ).Take( 2 ) ) ).OrderByDescending( group => group.Count() ) )
			result[group.Key] = group.Count();
		return result.ToJsonString();
	}

	/// <summary>
	/// The GPU bytes (RGBA8 with its mip chain) of the distinct textures in <see cref="Asset.All"/>, grouped like <see cref="PerfTextureGroups"/>, as JSON
	/// <c>{ group: { count, megabytes } }</c>, largest first; the harness's memory scenarios read it.
	/// </summary>
	[JSExport]
	public static string PerfTextureBytes()
	{
		var result = new JsonObject();
		var seen = new HashSet<Veldrid.Texture>();
		var groups = Asset.All.OfType<Texture>().Where( texture => texture.NativeTexture != null && seen.Add( texture.NativeTexture ) ).GroupBy( texture => string.IsNullOrEmpty( texture.Path ) ? "(no path)"
			: string.Join( '/', texture.Path.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries ).Take( 2 ) ) )
			.Select( group => (group.Key, Count: group.Count(), Bytes: group.Sum( texture => (long)texture.Width * texture.Height * 4 * 4 / 3 ) ) ).OrderByDescending( group => group.Bytes );
		foreach ( var group in groups )
			result[group.Key] = new JsonObject { ["count"] = group.Count, ["megabytes"] = Math.Round( group.Bytes / 1048576d, 2 ) };
		// the largest single textures with their size and where they came from (Origin needs PerfTrackTextureOrigins)
		var largest = new JsonArray();
		foreach ( var texture in Asset.All.OfType<Texture>().Where( texture => texture.NativeTexture != null ).DistinctBy( texture => texture.NativeTexture ).OrderByDescending( texture => (long)texture.Width * texture.Height ).Take( 12 ) )
			largest.Add( $"{texture.Width}x{texture.Height} {(string.IsNullOrEmpty( texture.Path ) ? texture.Origin ?? "(no path)" : texture.Path)}" );
		result["(largest)"] = largest;
		return result.ToJsonString();
	}

	/// <summary>The static fields holding the most array bytes (<see cref="PerfReachability.BytesByStaticRoot"/>), as JSON.</summary>
	[JSExport]
	public static string PerfMemoryRoots()
	{
		var result = new JsonObject();
		try
		{
			PerfReachability.Unreadable.Clear();
			foreach ( var (root, bytes) in PerfReachability.BytesByStaticRoot() )
				result[root] = root.StartsWith( "(objects" ) ? bytes : Math.Round( bytes / 1048576d, 1 );
			if ( PerfReachability.Unreadable.Count > 0 )
				result["(unreadable fields)"] = string.Join( "; ", PerfReachability.Unreadable.Take( 20 ) );
		}
		catch ( Exception exception )
		{
			// a diagnostic: say what went wrong rather than lose the other counters of the step
			result["(error)"] = exception.ToString();
		}
		return result.ToJsonString();
	}

	/// <summary>
	/// What the sound mixer (<see cref="AudioMixer.Current"/>) reaches, as JSON: array bytes by the field that holds them (the nearest
	/// game type's field; collections pass their holder on) and the object counts of the game's types (<see cref="PerfReachability.AudioGraph"/>).
	/// </summary>
	[JSExport]
	public static string PerfAudioBytes()
	{
		var result = new JsonObject();
		try
		{
			if ( AudioMixer.Current is not { } mixer )
				return result.ToJsonString();
			var decodes = new List<string>();
			var (bytes, counts) = PerfReachability.AudioGraph( mixer, decodes );
			var flights = new JsonArray();
			foreach ( var decode in decodes )
				flights.Add( decode );
			result["decodesInFlight"] = flights;
			var holders = new JsonObject();
			foreach ( var (holder, value) in bytes.OrderByDescending( entry => entry.Value ).Take( 25 ) )
				holders[holder] = Math.Round( value / 1048576d, 3 );
			result["megabytesByHolder"] = holders;
			var types = new JsonObject();
			foreach ( var (type, count) in counts.OrderByDescending( entry => entry.Value ).Take( 40 ) )
				types[type] = count;
			result["objectsByType"] = types;
		}
		catch ( Exception exception )
		{
			result["(error)"] = exception.ToString();
		}
		return result.ToJsonString();
	}

	/// <summary>
	/// The bytes behind the runtime's own statics that the game's root walk does not see (<see cref="PerfReachability.BytesByRuntimeRoot"/>):
	/// the shared array pools and the static fields of the framework's assemblies, as JSON megabytes, largest first.
	/// </summary>
	[JSExport]
	public static string PerfRuntimeRoots()
	{
		var result = new JsonObject();
		try
		{
			PerfReachability.Unreadable.Clear();
			foreach ( var (root, bytes) in PerfReachability.BytesByRuntimeRoot() )
				result[root] = root.StartsWith( "(objects" ) ? bytes : Math.Round( bytes / 1048576d, 2 );
			if ( PerfReachability.Unreadable.Count > 0 )
				result["(unreadable fields)"] = string.Join( "; ", PerfReachability.Unreadable.Take( 20 ) );
		}
		catch ( Exception exception )
		{
			result["(error)"] = exception.ToString();
		}
		return result.ToJsonString();
	}

	/// <summary>
	/// The objects the game's and the framework's static roots reach, counted by type (the largest counts, as JSON): two points compared show
	/// which types a change of scene leaves more of, where the array bytes alone do not.
	/// </summary>
	[JSExport]
	public static string PerfObjectCounts()
	{
		var result = new JsonObject();
		try
		{
			PerfReachability.TypeCounts = new();
			PerfReachability.EstimatedBytes = 0;
			PerfReachability.BytesByRuntimeRoot();
			result["(estimated bytes)"] = PerfReachability.EstimatedBytes;
			foreach ( var (type, count) in PerfReachability.TypeCounts.OrderByDescending( entry => entry.Value ).Take( 150 ) )
				result[type.FullName ?? type.Name] = count;
		}
		catch ( Exception exception )
		{
			result["(error)"] = exception.ToString();
		}
		finally
		{
			PerfReachability.TypeCounts = null;
		}
		return result.ToJsonString();
	}

	/// <summary>The array bytes behind the assets in <see cref="Asset.All"/> by type (<see cref="PerfReachability.BytesByAssetType"/>), as JSON <c>{ type: { count, megabytes } }</c>.</summary>
	[JSExport]
	public static string PerfAssetBytes()
	{
		var result = new JsonObject();
		try
		{
			foreach ( var (type, count, bytes) in PerfReachability.BytesByAssetType() )
				result[type] = new JsonObject { ["count"] = count, ["megabytes"] = Math.Round( bytes / 1048576d, 2 ) };
		}
		catch ( Exception exception )
		{
			result["(error)"] = exception.ToString();
		}
		// the shaders by file: every material compiles its own copy, so a count that grows from park to park names the shader of what is not released
		var shaders = new JsonObject();
		foreach ( var group in Asset.All.OfType<Shader>().GroupBy( shader => System.IO.Path.GetFileName( shader.Path ) ).OrderByDescending( group => group.Count() ) )
			shaders[group.Key] = group.Count();
		result["(shaders by file)"] = shaders;
		return result.ToJsonString();
	}

	/// <summary>Paths from static fields to parks other than the current one (<see cref="PerfReachability"/>), as JSON.</summary>
	[JSExport]
	public static string PerfStaleLevelPaths()
	{
		var paths = new JsonArray();
		foreach ( var path in PerfReachability.PathsToStaleLevels() )
			paths.Add( path );
		return paths.ToJsonString();
	}

	/// <summary>Opens the park's pause menu (its buttons are then clicked by id, Exit To Lobby among them); false without a park.</summary>
	[JSExport]
	public static bool PerfOpenPauseMenu()
	{
		if ( flow?.Hud is not { } hud )
			return false;
		hud.OpenPauseMenu();
		return true;
	}

	/// <summary>
	/// Managed memory as JSON: live bytes, the GC heap, bytes allocated since start and the collection counts, and what the largest
	/// known holders keep: archive files read into memory, archive members kept decompressed, decoded sound samples.
	/// </summary>
	[JSExport]
	public static string PerfCounters()
	{
		var info = GC.GetGCMemoryInfo();
		var (archives, members) = WadArchive.MemoryInUse;
		var sound = GameAudio.Counts;
		return new JsonObject
		{
			["entities"] = Entity.All.Count,
			["levelsAlive"] = Level.AliveCount,
			["texturesHeld"] = Asset.All.OfType<Texture>().Count(),
			["archiveBytes"] = archives,
			["archiveMemberBytes"] = members,
			["decodedSampleBytes"] = GameAudio.DecodedSampleBytes,
			// the sound system's own holdings: voices and the distinct PCM they play, and the sample decodes still running (a sample taken during one counts its growing buffer)
			["soundVoices"] = sound?.Voices ?? 0,
			["soundVoiceSampleBytes"] = sound?.VoiceSampleBytes ?? 0,
			["soundDecodesInFlight"] = sound?.DecodesInFlight ?? 0,
			["gpuTextureBytes"] = Veldrid.Texture.LiveGpuBytes,
			["gpuTextures"] = Veldrid.Texture.LiveGpuTextures,
			// GPU deletions the browser's per-frame budget has left for later frames (Renderer.BrowserDeletionBudgetMilliseconds); a settled page has none
			["pendingDeletions"] = renderer?.PendingDeletions ?? 0,
			["managedBytes"] = GC.GetTotalMemory( false ),
			["heapBytes"] = info.HeapSizeBytes,
			["committedBytes"] = info.TotalCommittedBytes,
			["fragmentedBytes"] = info.FragmentedBytes,
			["allocatedBytes"] = GC.GetTotalAllocatedBytes(),
			["gen0"] = GC.CollectionCount( 0 ),
			["gen1"] = GC.CollectionCount( 1 ),
			["gen2"] = GC.CollectionCount( 2 ),
		}.ToJsonString();
	}
}

/// <summary>
/// A diagnostic for the browser harness's memory phase: which static fields of the game's assemblies reach a park (<see cref="Level"/>)
/// other than the current one, by a breadth-first walk over fields, arrays, collections and delegates. Slow and approximate (roots
/// on stacks and in the runtime are not seen); only the harness calls it, while investigating.
/// </summary>
internal static class PerfReachability
{
	/// <summary>
	/// Bytes of the arrays reachable from each static field (an object reached from several counts for the first), largest first: an
	/// approximate map of who holds the memory. Everything that reads files reaches the file system (the sound categories do, for their
	/// banks), so the archive members it keeps read are counted under the first such root (<c>AudioMixer.Current</c> in the lobby).
	/// </summary>
	public static List<(string Root, long Bytes)> BytesByStaticRoot( int top = 15, int maximumObjects = 30_000_000 )
	{
		var seen = new HashSet<object>( ReferenceEqualityComparer.Instance );
		var totals = new List<(string Root, long Bytes)>();
		foreach ( var (root, value) in StaticRoots() )
		{
			if ( !seen.Add( value ) )
				continue;
			totals.Add( (root, ArrayBytesFrom( value, seen, maximumObjects )) );
		}
		totals.Add( ($"(objects visited{(seen.Count >= maximumObjects ? ", cut off" : "")})", seen.Count) );
		return totals.OrderByDescending( entry => entry.Bytes ).Take( top ).ToList();
	}

	/// <summary>
	/// Like <see cref="BytesByStaticRoot"/>, after the game's own roots (so what they reach is not counted again): the shared array pools of
	/// the common element types (their thread-local and per-core stores) and the static fields of the framework's assemblies.
	/// </summary>
	public static List<(string Root, long Bytes)> BytesByRuntimeRoot( int top = 20, int maximumObjects = 30_000_000 )
	{
		var seen = new HashSet<object>( ReferenceEqualityComparer.Instance );
		foreach ( var (_, value) in StaticRoots() )
			if ( seen.Add( value ) )
				ArrayBytesFrom( value, seen, maximumObjects );
		var totals = new List<(string Root, long Bytes)>();
		void Add( string root, object? value )
		{
			if ( value != null && !IsWeakTable( value ) && seen.Add( value ) )
				totals.Add( (root, ArrayBytesFrom( value, seen, maximumObjects )) );
		}
		foreach ( var pool in new object[] { System.Buffers.ArrayPool<byte>.Shared, System.Buffers.ArrayPool<char>.Shared, System.Buffers.ArrayPool<int>.Shared,
			System.Buffers.ArrayPool<float>.Shared, System.Buffers.ArrayPool<short>.Shared, System.Buffers.ArrayPool<ushort>.Shared, System.Buffers.ArrayPool<uint>.Shared } )
		{
			var type = pool.GetType();
			Add( $"{type.Name}<{type.GetGenericArguments().FirstOrDefault()?.Name}>", pool );
			foreach ( var field in type.GetFields( System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public ) )
			{
				try { Add( $"{type.Name}<{type.GetGenericArguments().FirstOrDefault()?.Name}>.{field.Name}", field.GetValue( null ) ); }
				catch ( Exception exception ) { Unreadable.Add( $"{type.Name}.{field.Name}: {exception.GetType().Name}" ); }
			}
		}
		var game = new HashSet<System.Reflection.Assembly> { typeof( Level ).Assembly, typeof( BaseFileSystem ).Assembly, typeof( WadArchive ).Assembly };
		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies().Where( assembly => !game.Contains( assembly ) ) )
		{
			Type[] types;
			try { types = assembly.GetTypes(); } catch { continue; }
			foreach ( var type in types )
			{
				if ( type.ContainsGenericParameters )
					continue;
				System.Reflection.FieldInfo[] fields;
				try { fields = type.GetFields( System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic ); } catch { continue; }
				foreach ( var field in fields )
				{
					try
					{
						if ( field.FieldType.IsPrimitive || field.FieldType.IsPointer || field.IsLiteral )
							continue;
						Add( $"{assembly.GetName().Name}: {type.Name}.{field.Name}", field.GetValue( null ) );
					}
					catch ( Exception exception ) { Unreadable.Add( $"{type.FullName}.{field.Name}: {exception.GetType().Name}" ); }
				}
			}
		}
		totals.Add( ($"(objects visited{(seen.Count >= maximumObjects ? ", cut off" : "")})", seen.Count) );
		return totals.OrderByDescending( entry => entry.Bytes ).Take( top ).ToList();
	}

	/// <summary>When set, the walks count every object they visit by its type (<see cref="Program.PerfObjectCounts"/>).</summary>
	internal static Dictionary<Type, int>? TypeCounts;

	/// <summary>With <see cref="TypeCounts"/>: the estimated bytes of the objects visited (<see cref="EstimateSize"/>).</summary>
	internal static long EstimatedBytes;

	/// <summary>
	/// A rough size of one object on the browser's 32-bit runtime: an 8-byte header plus 4 bytes per instance field (an array: its elements
	/// at their own size, references 4). Boxed copies of struct elements the walk makes count again; only differences between points mean much.
	/// </summary>
	private static long EstimateSize( object value )
	{
		var type = value.GetType();
		if ( value is string text )
			return 12 + text.Length * 2L;
		if ( value is Array array )
		{
			var element = type.GetElementType()!;
			long size = element.IsPrimitive ? System.Runtime.InteropServices.Marshal.SizeOf( element ) : element.IsValueType ? 8 : 4;
			return 16 + array.LongLength * size;
		}
		long fields = 0;
		for ( var walk = type; walk != null && walk != typeof( object ); walk = walk.BaseType )
			fields += walk.GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly ).Length;
		return 8 + 4 * fields;
	}

	/// <summary>
	/// Bytes of the arrays of numbers (and strings) reachable from <paramref name="value"/>, counting only objects not yet in <paramref name="seen"/> (which it fills), so a
	/// caller walking several roots counts every object once.
	/// </summary>
	private static long ArrayBytesFrom( object value, HashSet<object> seen, int maximumObjects )
	{
		long bytes = 0;
		var queue = new Queue<object>();
		queue.Enqueue( value );
		while ( queue.Count > 0 && seen.Count < maximumObjects )
		{
			var current = queue.Dequeue();
			if ( TypeCounts != null )
			{
				TypeCounts[current.GetType()] = TypeCounts.GetValueOrDefault( current.GetType() ) + 1;
				EstimatedBytes += EstimateSize( current );
			}
			if ( current is string text )
			{
				bytes += text.Length * 2L;
				continue;
			}
			// Arrays of numbers count their bytes; arrays of structs (a dictionary's entries) are followed into their fields.
			if ( current is Array array && array.GetType().GetElementType() is { IsPrimitive: true } element )
			{
				bytes += (long)array.Length * System.Runtime.InteropServices.Marshal.SizeOf( element );
				continue;
			}
			foreach ( var child in Children( current, maximumObjects - seen.Count ) )
				if ( seen.Count < maximumObjects && seen.Add( child ) )
					queue.Enqueue( child );
		}
		return bytes;
	}

	/// <summary>
	/// Everything reachable from <paramref name="root"/>: array bytes by holder (<c>Type.field</c> of the nearest object of a game
	/// assembly; a collection or array in between keeps its holder's label) and the count of objects of each game type.
	/// </summary>
	public static (Dictionary<string, long> Bytes, Dictionary<string, int> Counts) AudioGraph( object root, int maximumObjects = 5_000_000 )
		=> AudioGraph( root, new List<string>(), maximumObjects );

	public static (Dictionary<string, long> Bytes, Dictionary<string, int> Counts) AudioGraph( object root, List<string> decodes, int maximumObjects = 5_000_000 )
	{
		var game = new HashSet<System.Reflection.Assembly> { typeof( Level ).Assembly, typeof( BaseFileSystem ).Assembly, typeof( WadArchive ).Assembly };
		var bytes = new Dictionary<string, long>();
		var counts = new Dictionary<string, int>();
		var seen = new HashSet<object>( ReferenceEqualityComparer.Instance ) { root };
		var queue = new Queue<(object Value, string Label)>();
		queue.Enqueue( (root, root.GetType().Name) );
		static string Name( Type type ) => type.IsGenericType ? type.Name.Split( '`' )[0] + "<" + string.Join( ",", type.GetGenericArguments().Select( Name ) ) + ">" : type.Name;
		while ( queue.Count > 0 && seen.Count < maximumObjects )
		{
			var (current, label) = queue.Dequeue();
			var type = current.GetType();
			if ( current is string text )
			{
				bytes[label] = bytes.GetValueOrDefault( label ) + text.Length * 2L;
				continue;
			}
			if ( current is Array array && type.GetElementType() is { IsPrimitive: true } element )
			{
				bytes[label] = bytes.GetValueOrDefault( label ) + (long)array.Length * System.Runtime.InteropServices.Marshal.SizeOf( element );
				continue;
			}
			var own = game.Contains( type.Assembly );
			// an MPEG decode in flight: how far it is (input offset, PCM samples so far), to tell a running decode from a stuck one
			if ( type.Name == "DecodeState" )
			{
				var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
				var offset = type.GetField( "offset", flags )?.GetValue( current );
				var output = type.GetField( "output", flags )?.GetValue( current ) as List<short>;
				decodes.Add( $"{label}: offset {offset}, pcm {output?.Count}" );
			}
			if ( own )
				counts[Name( type )] = counts.GetValueOrDefault( Name( type ) ) + 1;
			void Visit( object? child, string childLabel )
			{
				if ( child == null || child.GetType().IsPrimitive || child.GetType().IsEnum || child is Type || child is System.Reflection.MemberInfo || child is System.Reflection.Assembly || IsWeakTable( child ) )
					return;
				if ( seen.Count < maximumObjects && seen.Add( child ) )
					queue.Enqueue( (child, childLabel) );
			}
			switch ( current )
			{
				case Delegate @delegate:
					foreach ( var single in @delegate.GetInvocationList() )
						Visit( single.Target, $"{label}→{single.Method.DeclaringType?.Name}.{single.Method.Name}" );
					continue;
				case Array items:
					foreach ( var item in items )
						Visit( item, label );
					continue;
			}
			for ( var walk = type; walk != null && walk != typeof( object ); walk = walk.BaseType )
				foreach ( var field in walk.GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly ) )
				{
					if ( field.FieldType.IsPrimitive || field.FieldType.IsPointer )
						continue;
					object? child;
					try { child = field.GetValue( current ); } catch { continue; }
					Visit( child, own ? $"{Name( walk )}.{field.Name}" : label );
				}
		}
		return (bytes, counts);
	}

	/// <summary>The array bytes behind the assets in <see cref="Asset.All"/>, by the asset's type (textures, materials, models, shaders), largest first.</summary>
	public static List<(string Type, int Count, long Bytes)> BytesByAssetType( int maximumObjects = 30_000_000 )
	{
		var seen = new HashSet<object>( ReferenceEqualityComparer.Instance );
		var totals = new List<(string Type, int Count, long Bytes)>();
		foreach ( var group in Asset.All.ToList().GroupBy( asset => asset.GetType().Name ) )
		{
			long bytes = 0;
			foreach ( var asset in group )
				if ( seen.Add( asset ) )
					bytes += ArrayBytesFrom( asset, seen, maximumObjects );
			totals.Add( (group.Key, group.Count(), bytes) );
		}
		return totals.OrderByDescending( entry => entry.Bytes ).ToList();
	}

	private static IEnumerable<(string Root, object Value)> StaticRoots()
	{
		var assemblies = new[] { typeof( Level ).Assembly, typeof( BaseFileSystem ).Assembly, typeof( WadArchive ).Assembly }.Distinct();
		foreach ( var type in assemblies.SelectMany( assembly => { try { return assembly.GetTypes(); } catch { return Array.Empty<Type>(); } } ) )
		{
			if ( type.ContainsGenericParameters )
				continue;
			foreach ( var field in type.GetFields( System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic ) )
			{
				object? value;
				try { value = field.GetValue( null ); } catch { continue; }
				if ( value != null && !field.FieldType.IsPrimitive && !IsWeakTable( value ) )
					yield return ($"{type.Name}.{field.Name}", value);
			}
		}
	}

	/// <summary>The objects <paramref name="value"/> refers to, at most <paramref name="limit"/> (a huge collection does not expand past the walk's budget).</summary>
	private static IEnumerable<object> Children( object value, int limit )
	{
		static bool Skip( object child ) => child.GetType().IsPrimitive || child.GetType().IsEnum || child is Type || child is System.Reflection.MemberInfo || IsWeakTable( child );
		var children = new List<object>();
		try
		{
			switch ( value )
			{
				case Delegate @delegate:
					foreach ( var single in @delegate.GetInvocationList() )
						if ( single.Target != null && !Skip( single.Target ) )
							children.Add( single.Target );
					return children;
				case Array array:
					foreach ( var item in array )
					{
						if ( children.Count >= limit )
							break;
						if ( item != null && !Skip( item ) )
							children.Add( item );
					}
					return children;
			}
		}
		catch ( Exception exception )
		{
			Unreadable.Add( $"{value.GetType().FullName}: {exception.GetType().Name}" );
			return children;
		}
		for ( var type = value.GetType(); type != null && type != typeof( object ); type = type.BaseType )
			foreach ( var field in type.GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly ) )
			{
				object? child;
				try
				{
					if ( field.FieldType.IsPrimitive || field.FieldType.IsPointer )
						continue;
					child = field.GetValue( value );
				}
				catch ( Exception exception )
				{
					// the runtime's reflection cannot read every field (the walk names them instead of giving up)
					Unreadable.Add( $"{type.FullName}.{field?.Name ?? "(null field)"}: {exception.GetType().Name}" );
					continue;
				}
				try
				{
					if ( child != null && !Skip( child ) )
						children.Add( child );
				}
				catch ( Exception exception )
				{
					Unreadable.Add( $"{type.FullName}.{field.Name} (its value): {exception.GetType().Name}" );
				}
			}
		return children;
	}

	/// <summary>
	/// A <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey, TValue}"/> holds its keys weakly, and reading its entries by reflection
	/// reads an ephemeron whose key may be gone (a null reference, or in the browser's runtime memory out of bounds): the walks do not enter one.
	/// </summary>
	internal static bool IsWeakTable( object value )
	{
		var type = value.GetType();
		while ( type.IsArray )
			type = type.GetElementType()!;
		// the table, its nested storage types and the runtime's ephemeron entries
		for ( var outer = type; outer != null; outer = outer.DeclaringType )
			if ( outer.IsGenericType && outer.GetGenericTypeDefinition() == typeof( System.Runtime.CompilerServices.ConditionalWeakTable<,> ) )
				return true;
		return type.FullName?.Contains( "Ephemeron", StringComparison.Ordinal ) == true;
	}

	/// <summary>The fields the last walks could not read, by declaring type and name.</summary>
	public static readonly HashSet<string> Unreadable = new();

	public static List<string> PathsToStaleLevels( int maximumObjects = 3_000_000 )
	{
		var found = new List<string>();
		var seen = new HashSet<object>( ReferenceEqualityComparer.Instance );
		var queue = new Queue<(object Value, string Path)>();
		var assemblies = new[] { typeof( Level ).Assembly, typeof( BaseFileSystem ).Assembly, typeof( WadArchive ).Assembly }.Distinct();
		foreach ( var type in assemblies.SelectMany( assembly => { try { return assembly.GetTypes(); } catch { return Array.Empty<Type>(); } } ) )
		{
			if ( type.ContainsGenericParameters )
				continue;
			foreach ( var field in type.GetFields( System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic ) )
			{
				object? value;
				try { value = field.GetValue( null ); } catch { continue; }
				if ( value != null && !field.FieldType.IsPrimitive && !IsWeakTable( value ) && seen.Add( value ) )
					queue.Enqueue( (value, $"{type.Name}.{field.Name}") );
			}
		}
		while ( queue.Count > 0 && seen.Count < maximumObjects && found.Count < 20 )
		{
			var (value, path) = queue.Dequeue();
			if ( value is Level level && !ReferenceEquals( level, Level.Current ) )
			{
				found.Add( path );
				continue;
			}
			void Visit( object? child, string name )
			{
				if ( seen.Count >= maximumObjects || child == null || child is string || child.GetType().IsPrimitive || child.GetType().IsEnum || child is Type || child is System.Reflection.MemberInfo || IsWeakTable( child ) )
					return;
				if ( seen.Add( child ) )
					queue.Enqueue( (child, $"{path}{name}") );
			}
			switch ( value )
			{
				case Delegate @delegate:
					foreach ( var single in @delegate.GetInvocationList() )
						Visit( single.Target, $" → {single.Method.DeclaringType?.Name}.{single.Method.Name}" );
					continue;
				case Array array when !array.GetType().GetElementType()!.IsPrimitive:
					var index = 0;
					foreach ( var item in array )
						Visit( item, $"[{index++}]" );
					continue;
				case System.Collections.IDictionary dictionary:
					foreach ( System.Collections.DictionaryEntry entry in dictionary )
					{
						Visit( entry.Key, ".key" );
						Visit( entry.Value, $"[{entry.Key}]" );
					}
					break;
				case System.Collections.IEnumerable enumerable when value is not Array:
					var position = 0;
					try
					{
						foreach ( var item in enumerable )
							Visit( item, $"[{position++}]" );
					}
					catch { }
					break;
			}
			for ( var type = value.GetType(); type != null && type != typeof( object ); type = type.BaseType )
			{
				foreach ( var field in type.GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly ) )
				{
					if ( field.FieldType.IsPrimitive || field.FieldType.IsPointer )
						continue;
					object? child;
					try { child = field.GetValue( value ); } catch { continue; }
					Visit( child, $".{field.Name}" );
				}
			}
		}
		return found;
	}
}

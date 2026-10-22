namespace OpenTPW;

/// <summary>
/// Handles the creation and management of various systems, including the game
/// window.
/// </summary>
internal static class Game
{
	public static void Run( string[] args )
	{
		Log = new();
		// [EXT:perf-run] --startup-profile: the start's phases up to the lobby (docs/ENGINE-PERFORMANCE.md, "Start and build hitches"); measurement only
		if ( args.Contains( "--startup-profile" ) )
			StartupProfile.Begin();
		CoopSession.ChangesParkOutsideCommands = ChangesParkOutsideCommands( args );
		SdlMovieAudioOutput.Suppressed = IsSilentRun( args, Environment.GetEnvironmentVariable( "OPENTPW_SILENT" ), Environment.GetEnvironmentVariable( "OPENTPW_OFFSCREEN" ) );
		Window.OffscreenRequested = IsOffscreenRun( args, Environment.GetEnvironmentVariable( "OPENTPW_OFFSCREEN" ) );
		// [EXT:online-coop-commands] a strict run throws on a command applied inside a simulation tick and on one that throws (docs/COOP.md, "No command inside a tick"); only ever
		// switched on here, so a test that runs the game keeps the test run's setting
		if ( Environment.GetEnvironmentVariable( "OPENTPW_STRICT_COMMANDS" ) == "1" )
		{
			ParkCommands.Strict = true;
			Log.Trace( "Strict commands: a command applied inside a simulation tick, or one that throws, throws." );
		}
		var tgqExportIndex = Array.IndexOf(args, "--export-tgq-png");
		if (tgqExportIndex >= 0)
		{
			if (tgqExportIndex + 3 >= args.Length || args.Skip(tgqExportIndex + 1).Take(3).Any(value => value.StartsWith("--")) ||
				!int.TryParse(args[tgqExportIndex + 2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var frameIndex))
				throw new ArgumentException("--export-tgq-png requires <local.tgq> <zero-based video-frame index> <new.png> (docs/TGQ-MOVIES.md).");
			TgqPngExport.Run(args[tgqExportIndex + 1], frameIndex, args[tgqExportIndex + 3]);
			return;
		}
		var sdtExportIndex = Array.IndexOf( args, "--export-sdt-wav" );
		if ( sdtExportIndex >= 0 )
		{
			if ( sdtExportIndex + 3 >= args.Length || args.Skip( sdtExportIndex + 1 ).Take( 3 ).Any( value => value.StartsWith( "--" ) )
				|| !uint.TryParse( args[sdtExportIndex + 2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var sampleNumber ) )
				throw new ArgumentException( "--export-sdt-wav requires <local.sdt> <1-based entry number> <new.wav> (docs/AUDIO.md)." );
			SdtWavExport.Run( args[sdtExportIndex + 1], sampleNumber, args[sdtExportIndex + 3] );
			return;
		}
		var fshExportIndex = Array.IndexOf(args, "--export-fsh-png");
		if (fshExportIndex >= 0)
		{
			if (fshExportIndex + 3 >= args.Length || args.Skip(fshExportIndex + 1).Take(3).Any(value => value.StartsWith("--")) ||
				!int.TryParse(args[fshExportIndex + 2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var imageIndex))
				throw new ArgumentException("--export-fsh-png requires <local.fsh> <zero-based image index> <output.png> (docs/FSH.md).");
			FshPngExport.Run(args[fshExportIndex + 1], imageIndex, args[fshExportIndex + 3]);
			return;
		}
		if ( args.Contains( "--shader-hot-reload" ) )
			Shader.HotReload = true;
		var fontIndex = Array.IndexOf( args, "--inspect-font" );
		if ( fontIndex >= 0 )
		{
			if ( fontIndex + 1 >= args.Length || args[fontIndex + 1].StartsWith( "--" ) )
				throw new ArgumentException( "--inspect-font requires a local BF4 font path." );
			using var stream = File.OpenRead( args[fontIndex + 1] );
			var font = new FontFile( stream );
			Console.WriteLine( $"BF4: {font.Glyphs.Count} entries; header hints {font.HeaderWidthHint}x{font.HeaderHeightHint}; {font.Glyphs.Sum( glyph => glyph.Coverage.Length )} decoded samples." );
			foreach ( var group in font.Glyphs.GroupBy( glyph => glyph.Encoding ).OrderBy( group => group.Key ) )
				Console.WriteLine( $"Encoding {group.Key}: {group.Count()} entries." );
			Console.WriteLine( "Read-only CPU font decoding: nibble samples 0–15; game UI integration and original visual fidelity remain unverified." );
			return;
		}
		var fshIndex = Array.IndexOf( args, "--inspect-fsh" );
		if ( fshIndex >= 0 )
		{
			if ( fshIndex + 1 >= args.Length || args[fshIndex + 1].StartsWith( "--" ) )
				throw new ArgumentException( "--inspect-fsh requires a local .fsh path or archive.wad!member/path.fsh (docs/FSH.md)." );
			var fsh = new FshFile( new MemoryStream( ReadFileOrWadMember( args[fshIndex + 1] ) ) );
			Console.WriteLine( $"SHPI: id {fsh.Id}; {fsh.Images.Count} image(s)." );
			foreach ( var image in fsh.Images )
				Console.WriteLine( $"Image '{image.Tag}' name '{image.Name}': {image.Width}x{image.Height}, code 0x{(image.Compressed ? 0xFB : 0x7B):X2}, palette 0x{(byte)image.PaletteFormat:X2} {image.PaletteFormat} with {image.PaletteEntries} entries; RGBA SHA-256 {Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( image.Rgba ) ).ToLowerInvariant()}." );
			Console.WriteLine( "Read-only CPU decoding of Theme Park Inc textures: 0x24 palettes are decoded opaque, as the original's palette converter does, and the 0x2D palette as A1R5G5B5 (OpenTPW's own reading, [EXT:fsh-0x2d-palette]); no comparison with the original game's rendering." );
			return;
		}
		var ps2Index = Array.IndexOf( args, "--export-ps2" );
		if ( ps2Index >= 0 )
		{
			if ( ps2Index + 2 >= args.Length || args[ps2Index + 1].StartsWith( "--" ) || args[ps2Index + 2].StartsWith( "--" ) )
				throw new ArgumentException( "--export-ps2 requires the PS2 disc's DATA directory and an output directory (docs/PS2.md)." );
			Ps2Export.Run( args[ps2Index + 1], args[ps2Index + 2] );
			return;
		}
		var saveIndex = Array.IndexOf( args, "--inspect-save" );
		if ( saveIndex >= 0 )
		{
			if ( saveIndex + 1 >= args.Length || args[saveIndex + 1].StartsWith( "--" ) )
				throw new ArgumentException( "--inspect-save requires a local original save-container path." );
			using var reader = new SaveReader( args[saveIndex + 1] );
			var header = reader.Inspect();
			var payload = reader.ReadFile();
			Console.WriteLine( $"Offline container: magic 0x{header.Magic:X8}; language {header.Language}; stamp 0x{header.Stamp:X8}; {(header.IsThemeParkInc ? $"version block {header.VersionText?.TrimEnd( '\r', '\n' ) ?? "(none)"}" : "no version block")}; BILZ chunk {header.CompressedChunkLength} bytes; decoded {payload.Length} bytes." );
			Console.WriteLine( $"Container SHA-256: {Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( reader.buffer ) )}" );
			Console.WriteLine( $"Decoded SHA-256: {Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( payload ) )}" );
			Console.WriteLine( "Read-only container decoding only: original park payload semantics and gameplay import remain unverified." );
			return;
		}
		var missionIndex = Array.IndexOf( args, "--inspect-tpi-mission" );
		if ( missionIndex >= 0 )
		{
			if ( missionIndex + 1 >= args.Length || args[missionIndex + 1].StartsWith( "--" ) )
				throw new ArgumentException( "--inspect-tpi-mission requires a local .sam path (docs/TPI-MISSION-INSPECTION.md)." );
			TpiMissionInspection.Run( args[missionIndex + 1] );
			return;
		}
		// Headless guest measurement (docs/SMARTER-GUESTS.md): the synthetic scenario needs no game data, so it runs before the data is set up; level parks run after (below).
		if ( args.Contains( "--guest-compare" ) )
		{
			Environment.ExitCode = GuestReportCompare.RunCommand( args );
			return;
		}
		if ( args.Contains( "--guest-report" ) && !GuestReport.WantsLevels( args ) )
		{
			Environment.ExitCode = GuestReport.RunCommand( args );
			return;
		}
		var pathIndex = Array.IndexOf( args, "--game-path" );
		string? commandLinePath = null;
		if ( pathIndex >= 0 )
		{
			if ( pathIndex + 1 >= args.Length || args[pathIndex + 1].StartsWith( "--" ) )
				throw new ArgumentException( "--game-path requires the original game's installation directory." );
			commandLinePath = args[pathIndex + 1];
		}
		if ( ResolveGameFolder( args, commandLinePath ) is not { } resolvedArgs )
			return;
		args = resolvedArgs;

		//
		// Check if the game data directory exists
		//
		// Any spelling (data, Data, DATA from a mounted CD) on case-sensitive hosts.
		var dataDirectory = GameLanguage.FindEntry( Settings.Default.GamePath, "data", true );
		if ( dataDirectory == null )
			throw new DirectoryNotFoundException( $"Theme Park World data not found in '{Settings.Default.GamePath}'. Use --game-path or OPENTPW_GAME_PATH to select a directory containing data/." );

		// [EXT:tpi-viewer] Theme Park Inc data (docs/THEME-PARK-INC.md): refused for normal starts, viewable with --load-original-level.
		var edition = GameEditions.Detect( dataDirectory );
		GameEditions.Select( edition.Edition );
		if ( GameEditions.IsThemeParkInc )
			RequireThemeParkIncViewer( args, edition );

		// Register game data directory
		FileSystem = CreateGameDataFileSystem( dataDirectory );

		// Debug export of every sign texture in one language (docs/RUNNING.md, "Sign textures"): the language is the option's own, the language data is found as below; no window, audio or park.
		if ( args.Contains( "--export-signs" ) )
		{
			var exportLanguageSettings = LanguageSettings.Load( LanguageSettings.GetDefaultPath(), new List<string>() );
			SignExport.RunCommand( args, dataDirectory,
				LanguageSettings.Choose( GetOption( args, "--language-data", "a directory with the original CD's language data" ),
					Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_DATA" ), exportLanguageSettings.LanguageDataPath, Settings.Default.LanguageDataPath ),
				GetOption( args, "--cd-data", "the original CD's folder" ) ?? Environment.GetEnvironmentVariable( "OPENTPW_CD_DATA" ) );
			return;
		}

		//
		// Select the text language: --language / --language-data, then OPENTPW_LANGUAGE /
		// OPENTPW_LANGUAGE_DATA, then language.json, then the old Language / LanguageDataPath
		// user settings (docs/LANGUAGES.md)
		//
		var languagePhase = StartupProfile.Phase( "language" );
		var languageDiagnostics = new List<string>();
		var languageSettings = LanguageSettings.Load( LanguageSettings.GetDefaultPath(), languageDiagnostics );
		foreach ( var diagnostic in languageDiagnostics )
			Log.Warning( diagnostic );
		var language = LanguageSettings.Choose( GetOption( args, "--language", "a language name such as German" ),
			Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE" ), languageSettings.Language, Settings.Default.Language );
		var languageData = LanguageSettings.Choose( GetOption( args, "--language-data", "a directory with the original CD's language data" ),
			Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_DATA" ), languageSettings.LanguageDataPath, Settings.Default.LanguageDataPath );
		// The CD chosen in Game files (or --cd-data) also holds the other shipped languages: without separate
		// language data, offer those too (the folder beside Data, as on the CD itself, unless --cd-data names one).
		LanguageStartup.Apply( dataDirectory, language, languageData,
			GetOption( args, "--cd-data", "the original CD's folder" ) ?? Environment.GetEnvironmentVariable( "OPENTPW_CD_DATA" ), false, Log.Warning );
		if ( GameLanguage.IsSelected )
		{
			Log.Trace( $"Language: {GameLanguage.Current}" );
			LanguageInterfaceArchive.Apply( FileSystem, GameLanguage.Current );
			if ( LanguageInterfaceArchive.Current != null )
				Log.Trace( $"Interface archive: {LanguageInterfaceArchive.Current} (the language's own ui.wad)" );
		}

		languagePhase.Dispose();

		// Compatibility: --cd-data overlay, media diagnostics, profile/fixes, graphics preset (docs/COMPATIBILITY.md)
		using ( StartupProfile.Phase( "compatibility" ) )
			CompatibilityStartup.Initialize( args, dataDirectory );

		// [EXT:mods] Extensions and data-only mods (docs/MODS.md): resolved once here; mods.json changes apply at the next start.
		using ( StartupProfile.Phase( "mods" ) )
			OpenTPW.Mods.ModStartup.Initialize( args, Log.Trace, Log.Warning );

		// Official bonus objects (docs/OBJECTS.md): --bonus-data, else OPENTPW_BONUS_DATA, else the saved bonus folder, else <config>/bonus.
		var bonusSettings = SetupSettings.Load( SetupSettings.GetDefaultPath() );
		var bonusRoot = BonusContent.ResolveRoot( GetOption( args, "--bonus-data", "the extracted official bonus content directory" ),
			Environment.GetEnvironmentVariable( "OPENTPW_BONUS_DATA" ), bonusSettings.BonusPath, Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )! );
		// [EXT:tpi-viewer] bonus objects are Theme Park World content: not offered to Theme Park Inc worlds
		if ( bonusRoot is { } bonus && !GameEditions.IsThemeParkInc )
		{
			ObjectCatalog.BonusDataRoot = bonus.Path;
			Log.Trace( $"Bonus content: {bonus.Path} ({bonus.Source})." );
		}
		var modelIndex = Array.IndexOf( args, "--inspect-model" );
		if ( modelIndex >= 0 )
		{
			if ( modelIndex + 1 >= args.Length )
				throw new ArgumentException( "--inspect-model requires an asset path." );
			var model = new ModelFile( args[modelIndex + 1] );
			foreach ( var mesh in model.Meshes )
			{
				Log.Trace( $"Mesh {mesh.Name}: {mesh.Vertices.Length} vertices; {mesh.Indices.Length} indices; transform {mesh.TransformMatrix}." );
				foreach ( var material in mesh.Materials )
					Log.Trace( $"Material: {material.Name}" );
			}
			if ( model.Clip is { } clip )
			{
				Log.Trace( $"Animation: {clip.Duration} ticks, {clip.Tracks.Count} records, {clip.TextureFrameTracks.Count} texture-frame tracks, {clip.NodeList.Count} listed nodes." );
				foreach ( var track in clip.Tracks )
				{
					var kinds = new List<string>();
					if ( track.Position != null )
						kinds.Add( "position" );
					if ( track.Rotations.Count > 0 )
						kinds.Add( "rotation" );
					if ( track.Scales.Count > 0 )
						kinds.Add( "scale" );
					if ( track.VertexAnimation != null )
						kinds.Add( "vertex groups" );
					if ( track.VertexPose != null )
						kinds.Add( "12-byte block (shown as stored)" );
					if ( track.UvAnimation != null )
						kinds.Add( "UV tracks" );
					if ( track.NodeFlagToggles.Count > 0 )
						kinds.Add( "node-flag toggles" );
					Log.Trace( $"Record node {track.NodeIndex}: flags 0x{track.Flags:X} ({string.Join( ", ", kinds )}); metadata 0x{track.MetadataBits:X}; unsupported 0x{track.UnsupportedFlags:X}." );
				}
			}
			return;
		}
		var movieName = MovieCommands.GetMovieName( args );
		if ( movieName != null && args.Contains( "--headless" ) )
		{
			MovieCommands.RunHeadless( dataDirectory, movieName, !args.Contains( "--mute" ) );
			return;
		}
		if ( args.Contains( "--build-texture-pack" ) )
		{
			BuildTexturePack( args, dataDirectory );
			return;
		}
		if ( args.Contains( "--validate-assets" ) )
		{
			var globalSettings = new SettingsFile( "/levels/jungle/global.sam" );
			var terrain = new TextureFile( "/levels/jungle/terrain/textures/jgr_bas1.wct" ).Data;
			Log.Trace( $"Asset validation: {globalSettings.Entries.Count()} settings; terrain {terrain.Width}x{terrain.Height}." );
			foreach ( var rideFile in FileSystem.GetFiles( "/levels/jungle/rides" ) )
				Log.Trace( rideFile );
			foreach ( var rideDirectory in FileSystem.GetDirectories( "/levels/jungle/rides" ) )
			{
				var relativeDirectory = FileSystem.GetRelativePath( rideDirectory );
				Log.Trace( $"Ride archive: {relativeDirectory}" );
				if ( !args.Contains( "--inspect-rides" ) )
					continue;
				foreach ( var entry in FileSystem.GetFiles( relativeDirectory ) )
					Log.Trace( entry );
			}
			return;
		}
		// Headless M3 gameplay-loop gate (docs/M3-GATE.md): no window, renderer or audio.
		if ( args.Contains( "--m3-gate" ) )
		{
			Environment.ExitCode = M3Gate.RunCommand( args );
			return;
		}
		// Headless guest report with level parks (docs/SMARTER-GUESTS.md): no window, renderer or audio.
		if ( args.Contains( "--guest-report" ) )
		{
			Environment.ExitCode = GuestReport.RunCommand( args );
			return;
		}
		// Headless M4 attraction/script gate (docs/M4-GATE.md): no window, renderer or audio.
		if ( args.Contains( "--m4-gate" ) )
		{
			Environment.ExitCode = M4Gate.RunCommand( args );
			return;
		}
		// Headless cross-runtime determinism digest (docs/DETERMINISM.md, "Browser and native"): no window, renderer or audio.
		if ( args.Contains( "--determinism-digest" ) )
		{
			Environment.ExitCode = CrossRuntimeDigest.RunCommand( args );
			return;
		}

		// [EXT:extension-online-gate] with opentpw.online disabled the online command-line tools refuse and no online folder is set up
		FeatureGates.RefuseOnlineCommandLine( args );
		var onlineFolders = FeatureGates.Online ? OnlineFolders.FromEnvironment( GetOption( args, "--online-dir", "an online folder outside the original installation" ) ) : null;
		if ( onlineFolders != null && OnlineCommands.Run( args, Settings.Default.GamePath, onlineFolders, Console.WriteLine ) )
			return;

		//
		// Save data beside the game files, or in the user configuration directory when the game
		// folder is read-only (a mounted CD; docs/SETUP.md)
		//
		SaveFileSystem = new BaseFileSystem( GetSaveDirectory() );

		//
		// Custom OpenTPW cache directory (mainly for editor-related stuff), in the user configuration directory: the working
		// directory may be a read-only install folder now that OpenTPW runs without administrator rights
		//
		var cacheDirectory = Path.Combine( Path.GetDirectoryName( SetupSettings.GetDefaultPath() )!, "cache" );
		CacheFileSystem = new BaseFileSystem( cacheDirectory );
		// compiled SPIR-V is kept between runs (ShaderCompiler.CompileStage): the same sources are not compiled again at every start
		ShaderCompiler.SpirvCacheDirectory = Path.Combine( cacheDirectory, "shaders" );

		//
		// Init renderer
		//
		var visitPath = GetOption( args, "--visit-park", "a local .tpwpark file" );
		var visit = visitPath == null ? null : ParkSharing.PrepareVisit( OpenTPW.Online.Packages.ParkPackage.Load( visitPath ) );
		if ( visit != null )
			Log.Trace( ParkSharing.Describe( visit ) );
		using ( StartupProfile.Phase( "renderer" ) )
			Render = DisplayStartup.CreateRenderer( args );
		// [EXT:script-mods] the crash marker was written at startup; it is removed after 15 seconds of frames (docs/SCRIPT-MODS.md)
		if ( OpenTPW.Mods.ScriptSafeMode.Current is { } scriptStartMarker )
			Render.OnUpdate += scriptStartMarker.Update;
		if ( movieName != null )
		{
			MovieCommands.Play( dataDirectory, movieName, !args.Contains( "--mute" ), args.Contains( "--smoke-test" ) );
			return;
		}

		//
		// Create level
		//
		var originalLevelIndex = Array.IndexOf( args, "--load-original-level" );
		if ( originalLevelIndex >= 0 && (originalLevelIndex + 1 >= args.Length || args[originalLevelIndex + 1].StartsWith( "--" )) )
			throw new ArgumentException( "--load-original-level requires a level name such as 'jungle'." );
		// Default: the original-style front end (docs/UI.md). --load-original-level, --sandbox and a
		// plain --smoke-test bypass it as before; --front-end --smoke-test tests the front end.
		GameAudio.Enabled = !args.Contains( "--mute" );
		// --no-advisor turns automatic advice off; the manual --advisor-say/--advisor-response presentation replaces it.
		var flowPhase = StartupProfile.Phase( "game-flow" );
		using var flow = new GameFlow { OnlineFolders = onlineFolders, AutomaticAdvisorEnabled = !args.Contains( "--no-advisor" ) && !args.Contains( "--advisor-say" ) && !args.Contains( "--advisor-response" ) };
		flowPhase.Dispose();
		var capturePath = GetOption( args, "--capture-world", "a .png file for the world-only screenshot" );
		var smoke = args.Contains( "--smoke-test" );
		var parkResumePhase = GetOption( args, "--park-resume-smoke", "'capture' or 'restore'" );
		if ( parkResumePhase != null )
		{
			var parkResumeFile = GetOption( args, "--park-resume-file", "the own-format world save path" );
			var parkResumeEvidence = GetOption( args, "--park-resume-evidence", "an evidence directory" );
			if ( parkResumeFile == null || parkResumeEvidence == null )
				throw new ArgumentException( "--park-resume-smoke also needs --park-resume-file and --park-resume-evidence." );
			ParkResumeSmokeTest.Run( flow, parkResumePhase, parkResumeFile, parkResumeEvidence );
			return;
		}
		// [EXT:online-coop-snapshot-join] the native join smoke (docs/COOP.md, "The game's join"): a host phase and a fresh join phase over recorded frames
		if ( GetOption( args, "--coop-join-smoke", "'host' or 'join'" ) is { } coopJoinPhase )
		{
			var coopJoinEvidence = GetOption( args, "--coop-join-evidence", "an evidence directory" )
				?? throw new ArgumentException( "--coop-join-smoke also needs --coop-join-evidence." );
			CoopJoinSmokeTest.Run( flow, coopJoinPhase, coopJoinEvidence );
			return;
		}
		var startsFrontEnd = visit == null && originalLevelIndex < 0 && !args.Contains( "--advisor-say" ) && !args.Contains( "--advisor-response" ) && !args.Contains( "--sandbox" ) && (!smoke || args.Contains( "--front-end" ));
		// [EXT:tpi-viewer] the experimental Theme Park Inc slice's own native smoke test: injected HUD input
		// drives the real user flow (docs/THEME-PARK-INC.md); it needs the slice, not a Theme Park World park.
		var sliceSmoke = args.Contains( "--tpi-smoke-test" );
		if ( sliceSmoke && !GameEditions.IsThemeParkInc )
			throw new ArgumentException( "--tpi-smoke-test drives the experimental Theme Park Inc slice: start it with --load-original-level and Theme Park Inc data (docs/THEME-PARK-INC.md)." );
		// [EXT:autorun] The CD's launcher window comes first when its Autorun folder is available (docs/AUTORUN.md).
		var autorun = startsFrontEnd && capturePath == null && !GameEditions.IsThemeParkInc ? CreateAutorun( args, bonusSettings, dataDirectory ) : null;
		// The original plays its start-up movies before the front end on every start (docs/TGQ-MOVIES.md); after
		// the launcher's Play when it is shown. Smoke tests, --capture-world and --no-intro / OPENTPW_NO_INTRO go straight to the front end.
		// [EXT:tpi-front-end-phase1] the Theme Park Inc Personnel File plays no start-up movies yet
		var playIntro = !GameEditions.IsThemeParkInc && IntroPlaylist.ShouldPlay( startsFrontEnd, smoke, args, Environment.GetEnvironmentVariable( "OPENTPW_NO_INTRO" ) );
		Func<IDisposable>? startIntro = playIntro ? () => StartIntro( flow, args, dataDirectory ) : null;
		if ( autorun == null && !playIntro )
		{
			Render.OnUpdate += flow.Update;
			Render.OnRender += flow.Render;
		}
		if ( capturePath != null )
		{
			var frames = GetOption( args, "--capture-frames", "a frame count" ) is { } text && int.TryParse( text, out var parsed ) && parsed > 0 ? parsed : 240;
			var poseText = GetOption( args, "--capture-camera", "x,y,height,yaw for the capture camera" );
			var pose = WorldCapture.ParsePose( poseText );
			if ( poseText != null && pose == null )
				throw new ArgumentException( "--capture-camera expects x,y,height,yaw, for example 10,-5,30,-135." );
			var sequenceText = GetOption( args, "--capture-sequence", "count,interval for a numbered capture sequence" );
			var sequence = sequenceText?.Split( ',' ) is [var countText, var intervalText] && int.TryParse( countText, out var count ) && int.TryParse( intervalText, out var interval ) && count > 0 && interval > 0
				? (Count: count, Interval: interval) : sequenceText == null ? (Count: 1, Interval: 1) : throw new ArgumentException( "--capture-sequence expects count,interval, for example 120,2." );
			Render.PostUpdate += new WorldCapture( capturePath, frames, args.Contains( "--capture-interface" ), pose, sequence.Count, sequence.Interval ).Update;
		}
		// [EXT:perf-run] performance runs (docs/ENGINE-PERFORMANCE.md): once the original level is up, hides the developer panels as a front-end park does, opens the park door as the player's button
		// would and runs the simulation at --perf-speed (1, 2 or 4; default 4), with a guest arriving every --perf-arrivals N turns when given. The window closes after that many frames; the guests and
		// the park clock are logged every 600 frames. Combine it with --frame-stats and, for CPU-bound frames, --no-vsync. Nothing else changes.
		if ( GetOption( args, "--perf-run", "a frame count" ) is { } perfText )
		{
			if ( !int.TryParse( perfText, out var perfFrames ) || perfFrames < 1 )
				throw new ArgumentException( "--perf-run expects a frame count, for example 3600." );
			var perfSpeed = 4;
			if ( GetOption( args, "--perf-speed", "1, 2 or 4" ) is { } speedText )
				perfSpeed = int.TryParse( speedText, out var parsedSpeed ) && parsedSpeed is 1 or 2 or 4 ? parsedSpeed : throw new ArgumentException( "--perf-speed expects 1, 2 or 4." );
			int? perfArrivals = null;
			if ( GetOption( args, "--perf-arrivals", "turns between arrivals" ) is { } arrivalText )
				perfArrivals = int.TryParse( arrivalText, out var parsedArrivals ) && parsedArrivals > 0 ? parsedArrivals : throw new ArgumentException( "--perf-arrivals expects a positive number of turns." );
			float? perfZoom = null;
			if ( GetOption( args, "--perf-zoom", "the park camera's zoom value" ) is { } zoomText )
				perfZoom = float.TryParse( zoomText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedZoom ) && float.IsFinite( parsedZoom ) ? parsedZoom : throw new ArgumentException( "--perf-zoom expects a zoom value, for example 300." );
			// [EXT:perf-run] --perf-hash-tick <tick> logs the park's world state hash (Level.ComputeStateHash) at the frame whose park tick is exactly that one, so two runs (the evening extension on and off, say)
			// can be compared on a real level with its placed objects; use --perf-speed 1 so no frame steps over the tick
			long? perfHashTick = GetOption( args, "--perf-hash-tick", "a park tick" ) is { } hashTickText && long.TryParse( hashTickText, out var parsedHashTick ) && parsedHashTick > 0 ? parsedHashTick : null;
			// [EXT:perf-run] --perf-digest adds the co-op session digest's cost on the running park to every 600th frame's line (CoopDigest.MeasureCost, the median of five; docs/COOP.md,
			// "Measurements"); the five digests run inside that frame, so leave it out of frame-time runs
			var perfDigest = args.Contains( "--perf-digest" );
			// [EXT:perf-run] --perf-crowd <n> stages n guests on the park's paths once the door opens (UmbrellaPerf.Stage without weather or umbrellas, as the browser harness's PerfCrowd), so a
			// measurement sees a full park at once; the park is not a real run afterwards
			int? perfCrowd = GetOption( args, "--perf-crowd", "a number of guests" ) is { } perfCrowdText
				? int.TryParse( perfCrowdText, out var parsedPerfCrowd ) && parsedPerfCrowd is > 0 and <= 5000 ? parsedPerfCrowd : throw new ArgumentException( "--perf-crowd expects a number of guests from 1 to 5000." )
				: null;
			int perfWarmup = 0, perfDone = 0;
			var perfOpened = false;
			Render.PostUpdate += () =>
			{
				if ( flow.Level is not { OriginalPark: not null, Park: { } perfPark } perfLevel )
					return;
				if ( !perfOpened )
				{
					if ( ++perfWarmup < 30 )
						return;
					perfOpened = true;
					perfPark.Economy.OpenPark();
					perfPark.Economy.Speed = (GameSpeed)perfSpeed;
					// a park started from the front end hides the developer panels
					perfLevel.ShowDeveloperPanels = false;
					// [EXT:perf-run] --perf-zoom <value> holds the park camera at that zoom value for the whole run (the wide range of GameOptions.WideZoom reaches 40..300)
					if ( perfZoom is { } zoom && Camera.Mode is ParkCameraMode perfCamera )
						perfCamera.SetPose( perfCamera.Target, zoom, perfCamera.Yaw );
					if ( perfArrivals is { } arrivals && perfLevel.Guests != null )
						perfLevel.Guests.Settings.ArrivalTimeBetween = arrivals;
					if ( perfCrowd is { } crowd && perfLevel.Guests != null )
						Log.Trace( $"--perf-crowd: {UmbrellaPerf.Stage( perfLevel.Guests, null, crowd, 0, false ).Guests} guests in the park." );
				}
				if ( perfHashTick is { } hashTick && perfPark.Economy.Tick == hashTick )
				{
					perfHashTick = null;
					Log.Trace( $"--perf-run: world state hash {perfLevel.ComputeStateHash():x16} at park tick {hashTick}, {perfLevel.PointLights.Count} point lights in view." );
				}
				if ( ++perfDone % 600 == 0 )
				{
					var digestCost = perfDigest && CoopDigest.MeasureCost( perfLevel, 5 ) is var (digest, worldHash) ? $", session digest {digest:0.00} ms (world hash {worldHash:0.00} ms) on {perfLevel.Objects.Objects.Count} objects" : "";
					Log.Trace( $"--perf-run: frame {perfDone}: {perfLevel.Guests?.Guests.Count} guests, park tick {perfPark.Economy.Tick}, {(perfPark.Economy.IsParkOpen ? "open" : "closed")}, {perfPark.Economy.Counters[ParkCounters.Admissions]} admissions, balance ${perfPark.Economy.Balance}, {perfLevel.PointLights.Count} point lights in view{digestCost}." );
				}
				if ( perfDone >= perfFrames )
					Render.Window.SdlWindow.Close();
			};
		}
		// [EXT:perf-run] --hitch-report <ms> logs every frame at least that long with the work recorded in it; --perf-build (with --load-original-level) does what a player does when
		// building: the buy window's tabs and rows, then one object of several types placed (PerfBuildScenario); --startup-profile logs the start's phases at the lobby's 120th frame
		// and closes the window (docs/ENGINE-PERFORMANCE.md, "Start and build hitches"). Measurement only.
		HitchReport? hitchReport = null;
		if ( GetOption( args, "--hitch-report", "a frame length in milliseconds" ) is { } hitchText )
		{
			hitchReport = new HitchReport( double.TryParse( hitchText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hitchMilliseconds ) && hitchMilliseconds > 0
				? hitchMilliseconds : throw new ArgumentException( "--hitch-report expects a frame length in milliseconds, for example 50." ) );
			Render.PreUpdate += hitchReport.FrameStarted;
		}
		if ( args.Contains( "--perf-build" ) )
		{
			if ( originalLevelIndex < 0 )
				throw new ArgumentException( "--perf-build needs --load-original-level." );
			var buildPlacements = GetOption( args, "--perf-build-placements", "objects placed per buy tab" ) is { } placementText
				? int.TryParse( placementText, out var parsedPlacements ) && parsedPlacements is >= 0 and <= 50 ? parsedPlacements : throw new ArgumentException( "--perf-build-placements expects 0 to 50." )
				: 3;
			if ( hitchReport == null )
			{
				hitchReport = new HitchReport( 50 );
				Render.PreUpdate += hitchReport.FrameStarted;
			}
			var scenario = new PerfBuildScenario( flow, hitchReport, buildPlacements, () =>
			{
				foreach ( var line in StartupProfile.Sections() )
					Log.Trace( "Build profile:" + line );
				Render.Window.SdlWindow.Close();
			} );
			Render.PostUpdate += scenario.Update;
		}
		if ( StartupProfile.Active && startsFrontEnd )
		{
			var lobbyFrames = new List<double>();
			var firstLobbyFrame = -1d;
			var lastFrame = 0d;
			Render.PostUpdate += () =>
			{
				if ( flow.Lobby == null || flow.Level != null || firstLobbyFrame < 0 && (firstLobbyFrame = StartupProfile.Elapsed) < 0 )
					return;
				var now = StartupProfile.Elapsed;
				if ( lastFrame > 0 )
					lobbyFrames.Add( now - lastFrame );
				lastFrame = now;
				if ( lobbyFrames.Count < 120 )
					return;
				foreach ( var line in StartupProfile.Report( firstLobbyFrame, lobbyFrames ) )
					Log.Trace( line );
				Render.Window.SdlWindow.Close();
			};
		}
		// [EXT:debug-evening] smoke and performance runs (docs/EVENING.md; also in Release builds, unlike most debug options): --evening-pace <minutes10|minutes20|minutes40|day|dusk|night> sets the evening extension's day cycle for this run only (a run-scoped override in Level, never written to the options, so never saved), and
		// --debug-evening-lamps <n> adds n lamp-like lights (the pool lamp's colour and radius) on a grid around the park camera's target that follow the evening look's lamp level, so a performance run
		// has many lamps to carry. Both need the extension on (--enable-mod opentpw.evening).
		if ( GetOption( args, "--evening-pace", "minutes10, minutes20, minutes40, day, dusk or night" ) is { } paceText )
			Level.EveningPaceOverride = paceText.ToLowerInvariant() switch
			{
				"day" or "alwaysday" => EveningPace.AlwaysDay,
				"dusk" or "alwaysdusk" => EveningPace.AlwaysDusk,
				"night" or "alwaysnight" => EveningPace.AlwaysNight,
				var other when Enum.TryParse<EveningPace>( other, ignoreCase: true, out var parsedPace ) && Enum.IsDefined( parsedPace ) && !int.TryParse( other, out _ ) => parsedPace,
				_ => throw new ArgumentException( "--evening-pace expects minutes10, minutes20, minutes40, day, dusk or night." )
			};
		var debugEveningLamps = GetOption( args, "--debug-evening-lamps", "a number of lamp lights" );
		if ( debugEveningLamps != null )
		{
			var lampCount = int.TryParse( debugEveningLamps, out var parsedLamps ) && parsedLamps is > 0 and <= 4096 ? parsedLamps : throw new ArgumentException( "--debug-evening-lamps expects a number from 1 to 4096." );
			var lampFrames = 0;
			Render.PostUpdate += AddDebugEveningLamps;
			void AddDebugEveningLamps()
			{
				if ( flow.Level is not { OriginalPark: not null } level || Camera.Mode is not ParkCameraMode park || ++lampFrames < 5 )
					return;
				Render.PostUpdate -= AddDebugEveningLamps;
				var columns = (int)MathF.Ceiling( MathF.Sqrt( lampCount ) );
				var spacing = 40f / columns;
				level.SetDebugEveningLamps( lampCount, new System.Numerics.Vector3( park.Target.X, park.Target.Y, park.Target.Z ) );
				Log.Trace( $"--debug-evening-lamps: {lampCount} lamp lights on a {columns} x {columns} grid, {spacing:0.##} engine units apart, around the camera target." );
			}
		}
#if DEBUG
		// [EXT:debug-water-track] smoke-only (Debug builds; --debug-kart-track places the go-kart ride instead): once the original level is up, places a track ride with a closed circuit near the
		// park's paths and points the camera at it, so captures show the track tiles and a boat (docs/TRACKS.md).
		if ( args.Contains( "--debug-water-track" ) || args.Contains( "--debug-kart-track" ) )
		{
			var demoFrames = 0;
			var demoType = args.Contains( "--debug-kart-track" ) ? 1 : 2;
			// --debug-track-addon <Info.Id> also places that add-on of the ride on its circuit (docs/TRACKS.md "Add-ons")
			var demoAddOn = GetOption( args, "--debug-track-addon", "a track add-on's Info.Id" ) is { } addOnText && int.TryParse( addOnText, out var parsedAddOn ) ? parsedAddOn : (int?)null;
			Render.PostUpdate += BuildTrackDemo;
			void BuildTrackDemo()
			{
				if ( flow.Level is not { WaterTracks: not null } demoLevel || ++demoFrames < 30 )
					return;
				Render.PostUpdate -= BuildTrackDemo;
				if ( demoLevel.BuildDemoTrackRide( demoType, demoAddOn ) == null )
					Log.Warning( "--debug-water-track / --debug-kart-track: no site with room for a track ride and its circuit." );
			}
		}
		// [EXT:debug-coaster] smoke-only (Debug builds): once the original level is up, builds a small coaster ("built", "open" or "tool": built and taken into the coaster tool, optionally with a coaster
		// Info.Id from --debug-coaster-id) and points the camera at it, so captures show the track, pylons and cars (docs/COASTERS.md).
		var debugCoaster = GetOption( args, "--debug-coaster", "built or open for a coaster capture" );
		if ( debugCoaster != null )
		{
			var coasterFrames = 0;
			var coasterId = GetOption( args, "--debug-coaster-id", "a coaster Info.Id" ) is { } idText && int.TryParse( idText, out var parsedId ) ? parsedId : (int?)null;
			var coasterHeight = GetOption( args, "--debug-coaster-height", "a pylon height" ) is { } heightText && int.TryParse( heightText, out var parsedHeight ) ? parsedHeight : 40;
			Render.PostUpdate += BuildCoasterDemo;
			void BuildCoasterDemo()
			{
				if ( flow.Level is not { OriginalPark: not null } demoLevel || ++coasterFrames < 30 )
					return;
				Render.PostUpdate -= BuildCoasterDemo;
				if ( demoLevel.BuildDemoCoaster( debugCoaster is "built" or "tool", coasterId, coasterHeight, debugCoaster == "tool" ) == null )
					Log.Warning( "--debug-coaster: no site with room for a coaster and its circuit." );
			}
		}
		// [EXT:debug-weather] smoke-only (Debug builds): applies one weather quality through the simulation's own
		// converter as soon as the park's weather exists, so captures can show rain/snow/lightning without waiting
		// for the season's rolls. The drops, bolts and thunder still come from ParkWeather's normal machinery;
		// Release builds never see the flag.
		var debugWeather = GetOption( args, "--debug-weather", "a weather quality 1..100 for smoke captures" );
		if ( debugWeather != null )
		{
			if ( !int.TryParse( debugWeather, out var debugQuality ) || debugQuality is < 1 or > 100 )
				throw new ArgumentException( "--debug-weather expects a quality from 1 to 100." );
			Render.PostUpdate += ApplyDebugWeather;
			void ApplyDebugWeather()
			{
				if ( flow.Level?.Weather is not { } weather )
					return;
				Render.PostUpdate -= ApplyDebugWeather;
				weather.ApplyQuality( debugQuality );
				Log.Trace( $"--debug-weather: quality {debugQuality} applied (snow {weather.Snow}, rain {weather.Rain}, lightning {weather.Lightning}, target {weather.TargetDrops} drops)." );
			}
		}
		// [EXT:debug-snow-cover] smoke-only: sets the settled snow cover (0..1) of the park as soon as it exists, so captures can show full cover (or its melt, with the weather dry) without waiting for
		// the weather's turns; the extension must be on (it is by default). Presentation only: the cover is in no hash and no mod record.
		var debugSnowCover = GetOption( args, "--debug-snow-cover", "a snow cover 0..1 for smoke captures" );
		if ( debugSnowCover != null )
		{
			if ( !double.TryParse( debugSnowCover, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var debugCover ) || debugCover is < 0 or > 1 )
				throw new ArgumentException( "--debug-snow-cover expects a cover from 0 to 1." );
			Render.PostUpdate += ApplyDebugSnowCover;
			void ApplyDebugSnowCover()
			{
				if ( flow.Level?.Snow is not { } snow )
					return;
				Render.PostUpdate -= ApplyDebugSnowCover;
				snow.Set( debugCover );
				Log.Trace( $"--debug-snow-cover: cover {debugCover} set." );
			}
		}
		// [EXT:debug-umbrellas] smoke-only (Debug builds): gives every guest an open umbrella (colour from its id, so all three show) and keeps it open, so a capture shows the umbrellas without a brolly shop,
		// a rain spell (use it with --debug-weather) or a lucky guest. It writes the guests' umbrella fields directly (a debug aid: the simulation of such a run is not the park's own, nothing is saved from it).
		// --debug-umbrellas-follow zoom,yaw holds the park camera on the lowest-numbered guest in view at that zoom value and yaw (degrees) for a close look at one umbrella.
		var followText = GetOption( args, "--debug-umbrellas-follow", "zoom,yaw for the camera that follows the first guest" )?.Split( ',' ).Select( value => float.Parse( value, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
		if ( args.Contains( "--debug-umbrellas" ) )
			Render.PostUpdate += GiveEveryGuestAnUmbrella;
		// [EXT:debug-umbrellas] --debug-umbrellas-crowd N (with --debug-umbrellas, a measurement aid like the browser harness's PerfUmbrellaCrowd): once, when the park's guests exist, adds N guests on the walkable path cells
		// (UmbrellaPerf.Stage, one each and round again), so the frame cost of a crowd of umbrellas can be measured with --perf-run.
		if ( GetOption( args, "--debug-umbrellas-crowd", "a number of extra guests" ) is { } crowdText )
		{
			var crowd = int.TryParse( crowdText, out var parsedCrowd ) && parsedCrowd > 0 ? parsedCrowd : throw new ArgumentException( "--debug-umbrellas-crowd expects a positive number of guests." );
			var crowdStaged = false;
			Render.PostUpdate += () =>
			{
				if ( crowdStaged || flow.Level?.Guests is not { } crowdGuests )
					return;
				crowdStaged = true;
				var staged = UmbrellaPerf.Stage( crowdGuests, null, crowd, 0, args.Contains( "--debug-umbrellas" ) );
				Log.Trace( $"--debug-umbrellas-crowd: {staged.Spawned} guests added, {staged.Guests} in the park, {staged.Umbrellas} with an open umbrella." );
			};
		}
		void GiveEveryGuestAnUmbrella()
		{
			if ( flow.Level?.Guests is not { } umbrellaGuests )
				return;
			if ( followText is [var followZoom, var followYaw] && Camera.Mode is ParkCameraMode followCamera && flow.Level.OriginalPark is { } followPark )
				if ( umbrellaGuests.Guests.Where( candidate => candidate.IsVisible && float.IsFinite( candidate.DrawX ) ).OrderBy( candidate => candidate.Id ).FirstOrDefault() is { } followed )
				{
					var spot = GuestRenderer.CellToEngine( followPark.Heightfield, followed.DrawX, followed.DrawY );
					followCamera.SetPose( new System.Numerics.Vector3( spot.X, spot.Y, 0 ), followZoom, followYaw );
				}
			foreach ( var umbrellaGuest in umbrellaGuests.Guests )
			{
				if ( umbrellaGuest.UmbrellaState == UmbrellaRules.StateOpen )
					continue;
				umbrellaGuest.UmbrellaState = UmbrellaRules.StateOpen;
				umbrellaGuest.UmbrellaType = umbrellaGuest.Id % UmbrellaRules.Colours;
				umbrellaGuest.UmbrellaDueTurn = 0;
			}
		}
		// [EXT:debug-winter] smoke-only (Debug builds): dresses every guest in warm items. --debug-winter alone gives a head item by the guest's id (HeadItemOf) and mittens on all but every fourth guest, so a capture shows hats, earmuffs,
		// mittens and the head item alone; --debug-winter hat|earmuffs|head|mittens|all|costume forces one look (costume: every second guest in costume) (all: the guest's own head item and mittens). With --debug-umbrellas-follow zoom,yaw it holds the park camera on the lowest-numbered guest
		// in view, as --debug-umbrellas does (when both are given the umbrellas' hook moves the camera). It writes the guests' item field directly (a debug aid: the simulation of such a run is not the park's own, nothing is saved from it).
		var winterIndex = Array.IndexOf( args, "--debug-winter" );
		var winterLook = winterIndex >= 0 && winterIndex + 1 < args.Length && !args[winterIndex + 1].StartsWith( "--", StringComparison.Ordinal ) ? args[winterIndex + 1] : "";
		if ( winterIndex >= 0 )
			Render.PostUpdate += DressEveryGuestWarm;
		void DressEveryGuestWarm()
		{
			if ( flow.Level?.Guests is not { } winterGuests )
				return;
			if ( !args.Contains( "--debug-umbrellas" ) && followText is [var winterZoom, var winterYaw] && Camera.Mode is ParkCameraMode winterCamera && flow.Level.OriginalPark is { } winterPark )
				if ( winterGuests.Guests.Where( candidate => candidate.IsVisible && float.IsFinite( candidate.DrawX ) ).OrderBy( candidate => candidate.Id ).FirstOrDefault() is { } followed )
				{
					var spot = GuestRenderer.CellToEngine( winterPark.Heightfield, followed.DrawX, followed.DrawY );
					winterCamera.SetPose( new System.Numerics.Vector3( spot.X, spot.Y, 0 ), winterZoom, winterYaw );
				}
			foreach ( var winterGuest in winterGuests.Guests )
			{
				if ( winterLook == "costume" )
					winterGuest.InCostume = winterGuest.Id % 2 == 0;
				winterGuest.WarmItems = winterLook switch
				{
					"hat" => WinterRules.Hat | WinterRules.Mittens,
					"earmuffs" => WinterRules.Earmuffs | WinterRules.Mittens,
					"head" => WinterRules.HeadItemOf( winterGuest.Id ),
					"mittens" => WinterRules.Mittens,
					"all" => WinterRules.HeadItemOf( winterGuest.Id ) | WinterRules.Mittens,
					"costume" => WinterRules.HeadItemOf( winterGuest.Id ) | WinterRules.Mittens,
					_ => WinterRules.HeadItemOf( winterGuest.Id ) | (winterGuest.Id % 4 == 3 ? 0 : WinterRules.Mittens)
				};
			}
		}
		// [EXT:debug-particle] smoke-only: once the original level is up, starts template effects (ids, comma separated) at an engine position
		// (--debug-particle-at x,y,z, Z up; default the park camera's target) through the same starter an ADDOBJ uses, so captures can show smoke or fireworks
		// without waiting for a ride script to fire them (docs/PARTICLES.md).
		var debugParticle = GetOption( args, "--debug-particle", "effect ids such as 3 or 2,3 for smoke captures" );
		if ( debugParticle != null )
		{
			var debugIds = debugParticle.Split( ',' ).Select( int.Parse ).ToArray();
			var atOption = GetOption( args, "--debug-particle-at", "an engine position x,y,z" );
			var at = atOption?.Split( ',' ).Select( value => float.Parse( value, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
			var debugParticleFrames = 0;
			Render.PostUpdate += StartDebugParticles;
			void StartDebugParticles()
			{
				if ( flow.Level?.OriginalPark == null || ++debugParticleFrames < 30 )
					return;
				Render.PostUpdate -= StartDebugParticles;
				var position = at != null ? new System.Numerics.Vector3( at[0], at[1], at[2] )
					: Camera.Mode is ParkCameraMode park ? park.Target.GetSystemVector3() + new System.Numerics.Vector3( 0, 0, 0.5f ) : new System.Numerics.Vector3( 0, 0, 2 );
				foreach ( var id in debugIds )
					Log.Trace( $"--debug-particle: effect {id} at {position} -> handle {OriginalObjectRuntime.ParticleStarter?.Invoke( new ParticleEventRequest( 0, 0, 1, 0, id, 1000, position, null, -1, -1 ) )}." );
			}
		}
		// [EXT:debug-object-effect] smoke-only: once the original level is up, builds the first buildable ride that names a build effect on a free cell next to
		// the camera target through the build tool's own path (Level.PlaceObject), and for "demolish" removes it again through the remove tool's (Level.RemoveObjectAt),
		// so captures show the type's Create and Destroy particle effects (docs/PARTICLES.md, PTCL-908).
		var debugObjectEffect = GetOption( args, "--debug-object-effect", "build or demolish for effect captures" );
		if ( debugObjectEffect != null )
		{
			var objectFrames = 0;
			var builtAtFrame = 0;
			// the capture camera is held for the last 30 frames before the capture: build once it is in place, so the ride stands in the picture
			var buildFrame = GetOption( args, "--capture-frames", "a frame count" ) is { } frameText && int.TryParse( frameText, out var parsedFrames ) && parsedFrames > 30 ? parsedFrames - 29 : 40;
			OriginalObject? built = null;
			Render.PostUpdate += RunDebugObjectEffect;
			void RunDebugObjectEffect()
			{
				if ( flow.Level is not { OriginalPark: not null } level || Camera.Mode is not ParkCameraMode park || ++objectFrames < buildFrame )
					return;
				if ( built == null )
				{
					var entry = level.Objects.Catalog.Buildable.FirstOrDefault( item => item.Category == ObjectCategory.Ride && item.CreateParticleEffect != 0 );
					if ( entry == null || !level.TryGetGridCell( park.Target.X, park.Target.Y, out var cellX, out var cellY ) )
					{
						Log.Warning( "--debug-object-effect: no buildable ride with a build effect, or the camera target is off the grid." );
						Render.PostUpdate -= RunDebugObjectEffect;
						return;
					}
					for ( var radius = 0; radius < 40 && built == null; radius++ )
						for ( var dx = -radius; dx <= radius && built == null; dx++ )
							for ( var dy = -radius; dy <= radius && built == null; dy++ )
								if ( Math.Max( Math.Abs( dx ), Math.Abs( dy ) ) == radius && level.CheckFreeObject( entry, cellX + dx, cellY + dy, 0 ) == OriginalPlacementResult.Allowed )
									built = level.PlaceObject( entry, cellX + dx, cellY + dy, 0 );
					Log.Trace( $"--debug-object-effect: built {entry.DisplayName} (effect {entry.CreateParticleEffect}): {(built == null ? level.LastActionMessage : "placed")}." );
					builtAtFrame = objectFrames;
					if ( built == null )
						Render.PostUpdate -= RunDebugObjectEffect;
					return;
				}
				if ( debugObjectEffect != "demolish" )
				{
					Render.PostUpdate -= RunDebugObjectEffect;
					return;
				}
				if ( objectFrames - builtAtFrame >= 60 )
				{
					Log.Trace( $"--debug-object-effect: demolished {built.Entry.DisplayName} (effect {built.Entry.DestroyParticleEffect}): {level.RemoveObjectAt( built.Placement.X, built.Placement.Y )}." );
					Render.PostUpdate -= RunDebugObjectEffect;
				}
			}
		}
		// [EXT:debug-light] smoke-only: once the original level is up, adds one fixed point light (docs/LIGHTS.md) at x,y,height relative to the park camera's target, in engine units (a cell is 2), with a radius in
		// engine units (the original engine's default of 100 model units is 20) and an optional colour of three 0..1 channels (default white), so a capture or a look at the park shows what a light does
		var debugLight = GetOption( args, "--debug-light", "x,y,height,radius[,red,green,blue] relative to the camera target, in engine units" );
		if ( debugLight != null )
		{
			var lightValues = debugLight.Split( ',' ).Select( value => float.Parse( value, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
			if ( lightValues.Length is not (4 or 7) )
				throw new ArgumentException( "--debug-light needs x,y,height,radius or x,y,height,radius,red,green,blue." );
			var lightFrames = 0;
			Render.PostUpdate += AddDebugLight;
			void AddDebugLight()
			{
				if ( flow.Level is not { OriginalPark: not null } level || Camera.Mode is not ParkCameraMode park || ++lightFrames < 5 )
					return;
				Render.PostUpdate -= AddDebugLight;
				var colour = lightValues.Length == 7 ? new System.Numerics.Vector3( lightValues[4], lightValues[5], lightValues[6] ) : System.Numerics.Vector3.One;
				// with --capture-camera the held camera's ground target is the origin (the live camera may still be elsewhere when the light is added), as for --debug-pool
				var lightCamera = GetOption( args, "--capture-camera", "x,y,height,yaw" )?.Split( ',' ).Select( value => float.Parse( value, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
				var lightX = lightCamera is { Length: >= 2 } ? lightCamera[0] : park.Target.X;
				var lightY = lightCamera is { Length: >= 2 } ? lightCamera[1] : park.Target.Y;
				var light = new PointLight( new System.Numerics.Vector3( lightX + lightValues[0], lightY + lightValues[1], park.Target.Z + lightValues[2] ), colour, lightValues[3] );
				level.PointLights.Add( new FixedPointLightSource( light ) );
				Log.Trace( FormattableString.Invariant( $"--debug-light: a point light at {light.Position.X:0.##},{light.Position.Y:0.##},{light.Position.Z:0.##}, radius {light.Radius:0.##}, colour {colour.X:0.##},{colour.Y:0.##},{colour.Z:0.##}." ) );
			}
		}
		// [EXT:debug-evening] smoke-only (Debug builds): --debug-torches N places N of the theme's torches (the evening extension's own, docs/EVENING.md "Torches") through the level's own placement, which charges them,
		// on the free cells nearest the camera target (--capture-camera's when given) with three cells between them, so a capture shows the torches, their flames and each one's light
		if ( GetOption( args, "--debug-torches", "the number of torches to place" ) is { } torchText && int.TryParse( torchText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var torchCount ) && torchCount > 0 )
		{
			var torchCamera = GetOption( args, "--capture-camera", "x,y,height,yaw" )?.Split( ',' ).Select( value => float.Parse( value, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
			var torchFrames = 0;
			Render.PostUpdate += PlaceDebugTorches;
			void PlaceDebugTorches()
			{
				if ( flow.Level is not { OriginalPark: not null } level || Camera.Mode is not ParkCameraMode park || ++torchFrames < 5 )
					return;
				Render.PostUpdate -= PlaceDebugTorches;
				var entry = level.Objects.Catalog.Entries.FirstOrDefault( ParkResumeMods.IsEveningObject );
				var searchX = torchCamera is { Length: >= 2 } ? torchCamera[0] : park.Target.X;
				var searchY = torchCamera is { Length: >= 2 } ? torchCamera[1] : park.Target.Y;
				if ( entry == null || !level.TryGetGridCell( searchX, searchY, out var cellX, out var cellY ) )
				{
					Log.Warning( "--debug-torches: the opentpw.evening extension is off (it is on by default), this is a Theme Park Inc run, or the camera target is off the grid." );
					return;
				}
				var placedTorches = new List<string>();
				// three cells between neighbours, so each torch's pool of light shows on its own
				for ( var radius = 0; radius < 24 && placedTorches.Count < torchCount; radius += 4 )
					for ( var dx = -radius; dx <= radius && placedTorches.Count < torchCount; dx += 4 )
						for ( var dy = -radius; dy <= radius && placedTorches.Count < torchCount; dy += 4 )
							if ( Math.Max( Math.Abs( dx ), Math.Abs( dy ) ) == radius && level.CheckObject( entry, cellX + dx, cellY + dy, 0 ) == OriginalPlacementResult.Allowed && level.PlaceObject( entry, cellX + dx, cellY + dy, 0 ) is { } torch )
								placedTorches.Add( FormattableString.Invariant( $"{torch.Placement.X},{torch.Placement.Y} (engine {torch.Placement.ModelToEngine.M41:0.#},{torch.Placement.ModelToEngine.M42:0.#})" ) );
				Log.Trace( $"--debug-torches: placed {placedTorches.Count} of {torchCount} {entry.DisplayName}: {string.Join( "; ", placedTorches )}." );
			}
		}
		// [EXT:debug-pool] smoke-only: once the original level is up and the opentpw.pools extension is on, paints pool cells through the pool tool's own builder
		// (Level.PoolBuilder.PaintCells, charged as the tool does) at the first place near the camera target (--capture-camera's when given) where every cell of the shape can be painted, so a
		// capture shows the water and the edges (docs/POOLS.md). The shape is rectangles "x,y,w,h" relative to that place, separated by ';' (default a 6 x 4 basin
		// plus an L arm and a single cell, which covers straight edges, inner and outer corners and an isolated cell).
		var debugPool = GetOption( args, "--debug-pool", "pool rectangles x,y,w,h;... relative to the camera target" );
		if ( debugPool != null )
		{
			var shapeText = debugPool.Length > 0 && debugPool != "default" ? debugPool : "0,0,6,4;6,2,3,6;11,0,1,1";
			// [EXT:debug-pool] a rectangle written with a leading 'r' ("r0,0,8,1") is painted with the river option ([EXT:pools-river]): its cells carry the lazy river's flag
			var rectangles = shapeText.Split( ';' ).Select( part => part.TrimStart( 'r' ).Split( ',' ).Select( int.Parse ).ToArray() ).ToArray();
			var riverRectangles = shapeText.Split( ';' ).Select( part => part.StartsWith( 'r' ) ).ToArray();
			var shape = rectangles.SelectMany( r => Enumerable.Range( 0, r[2] ).SelectMany( dx => Enumerable.Range( 0, r[3] ).Select( dy => (X: r[0] + dx, Y: r[1] + dy) ) ) ).Distinct().ToArray();
			var riverShape = rectangles.Where( ( _, index ) => riverRectangles[index] ).SelectMany( r => Enumerable.Range( 0, r[2] ).SelectMany( dx => Enumerable.Range( 0, r[3] ).Select( dy => (X: r[0] + dx, Y: r[1] + dy) ) ) ).ToHashSet();
			var poolCamera = GetOption( args, "--capture-camera", "x,y,height,yaw" )?.Split( ',' ).Select( value => float.Parse( value, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
			var poolFrames = 0;
			// [EXT:debug-pool] --debug-pool-lead N: the frames between painting the pool and the capture (default 59), so a capture can show swimmers after they have swum for a while (docs/POOLS.md)
			var poolLead = GetOption( args, "--debug-pool-lead", "frames between the pool and the capture" ) is { } leadText && int.TryParse( leadText, out var parsedLead ) && parsedLead > 59 ? parsedLead : 59;
			var poolFrame = GetOption( args, "--capture-frames", "a frame count" ) is { } poolFrameText && int.TryParse( poolFrameText, out var poolParsed ) && poolParsed > poolLead + 1 ? poolParsed - poolLead : 40;
			// [EXT:debug-pool] --debug-pool-at N builds the pool (and what follows it) at frame N instead of the lead before the capture, so swimmers and riders have time to act
			if ( GetOption( args, "--debug-pool-at", "a frame count" ) is { } poolAtText && int.TryParse( poolAtText, out var poolAt ) && poolAt > 0 )
				poolFrame = poolAt;
			Render.PostUpdate += PaintDebugPool;
			void PaintDebugPool()
			{
				if ( flow.Level is not { OriginalPark: not null } level || Camera.Mode is not ParkCameraMode park || ++poolFrames < poolFrame )
					return;
				Render.PostUpdate -= PaintDebugPool;
				// the held capture camera's target when one is given (the live camera may still be moving at this frame), else the park camera's
				var searchX = poolCamera != null ? poolCamera[0] : park.Target.X;
				var searchY = poolCamera != null ? poolCamera[1] : park.Target.Y;
				if ( level.PoolBuilder is not { } builder || !level.TryGetGridCell( searchX, searchY, out var cellX, out var cellY ) )
				{
					Log.Warning( "--debug-pool: the opentpw.pools extension is off (it is on by default; check mods.json and --disable-extension), this is a Theme Park Inc run, or the camera target is off the grid." );
					return;
				}
				// [EXT:debug-pool] --debug-pool-offset dx,dy moves the place the shape is looked for from the camera target by whole cells (so a capture can frame a changing room beside the pool)
				if ( GetOption( args, "--debug-pool-offset", "dx,dy cells" )?.Split( ',' ) is { Length: 2 } offsetParts && int.TryParse( offsetParts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var offsetX ) && int.TryParse( offsetParts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var offsetY ) )
					(cellX, cellY) = (cellX + offsetX, cellY + offsetY);
				for ( var radius = 0; radius < 40; radius++ )
					for ( var dx = -radius; dx <= radius; dx++ )
						for ( var dy = -radius; dy <= radius; dy++ )
						{
							if ( Math.Max( Math.Abs( dx ), Math.Abs( dy ) ) != radius )
								continue;
							// the shape is centred on the search cell
							var originX = cellX + dx - (int)MathF.Round( (float)shape.Average( cell => cell.X ) );
							var originY = cellY + dy - (int)MathF.Round( (float)shape.Average( cell => cell.Y ) );
							if ( !shape.All( cell => builder.Check( originX + cell.X, originY + cell.Y ) == PoolBuildResult.Ok ) )
								continue;
							var stroke = builder.PaintCells( shape.Select( cell => (originX + cell.X, originY + cell.Y) ) );
							foreach ( var cell in riverShape )
								level.Guests!.Grid.Cells.SetFlags( originX + cell.X, originY + cell.Y, ParkCellFlags.PoolRiver );
							var grid = level.Objects.Grid.Origin;
							var centreX = grid.X + (originX + shape.Average( cell => cell.X ) + 0.5f) * ObjectPlacement.CellSize;
							var centreY = grid.Y + (originY + shape.Average( cell => cell.Y ) + 0.5f) * ObjectPlacement.CellSize;
							Log.Trace( FormattableString.Invariant( $"--debug-pool: painted {stroke.Cells.Count} cells at {originX},{originY} (engine centre {centreX:0.#},{centreY:0.#}), charged {stroke.Charged}, refusal {stroke.FirstRefusal?.Reason.ToString() ?? "none"}." ) );
							// Place the bridge before swimmers: its normal standing guard must never be bypassed.
							var bridgeCells = shape.Select( cell => (X: originX + cell.X, Y: originY + cell.Y) ).ToArray();
							var bridgeDelay = GetOption( args, "--debug-bridge-delay", "frames to wait after swimmers exist before building the bridge" ) is { } delayText
								&& int.TryParse( delayText, out var delay ) && delay > 0 ? delay : 0;
							var debugBridge = args.Contains( "--debug-bridge" ) && bridgeDelay == 0 ? level.BuildDebugPoolBridge( bridgeCells ) : null;
							if ( args.Contains( "--debug-bridge" ) && bridgeDelay > 0 )
							{
								var bridgeFrames = 0;
								Render.PostUpdate += BuildLateBridge;
								void BuildLateBridge()
								{
									if ( flow.Level != level ) { Render.PostUpdate -= BuildLateBridge; return; }
									if ( level.SwimmerRenderer == null || ++bridgeFrames < bridgeDelay ) return;
									Render.PostUpdate -= BuildLateBridge;
									Log.Trace( $"--debug-bridge-delay: building after {bridgeFrames} frames with existing guest/staff/swimmer renderers; swimmers {level.Guests!.Guests.Count( guest => guest.IsSwimming )}." );
									if ( level.BuildDebugPoolBridge( bridgeCells ) is { } lateBridge ) level.StartDebugBridgeWalkers( lateBridge );
								}
							}
							// [EXT:debug-swimmers] smoke-only: --debug-swimmers N builds a changing room beside the pool (through the level's own placement, which charges it and needs the pool at its back) and
							// puts N guests into the water at once, so a capture shows swimmers (docs/POOLS.md)
							if ( GetOption( args, "--debug-swimmers", "guests to put into the pool" ) is { } swimText && int.TryParse( swimText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var swimmerCount ) )
								PlaceDebugChangingRoom( level, shape.Select( cell => (X: originX + cell.X, Y: originY + cell.Y) ).ToArray(), swimmerCount, args.Contains( "--debug-extras" ) );
							// [EXT:debug-attractions] smoke-only: --debug-attractions ropeswing,jets,wavemachine,spa (or all) places those objects beside the pool through the level's own placement (docs/POOLS.md)
							if ( GetOption( args, "--debug-attractions", "kinds: ropeswing,jets,wavemachine,spa or all" ) is { } attractionKinds )
								Log.Trace( $"--debug-attractions: placed {string.Join( ", ", level.BuildPoolAttractionsBeside( shape.Select( cell => (X: originX + cell.X, Y: originY + cell.Y) ).ToArray(), attractionKinds ).Select( placed => $"{placed.Entry.DisplayName} at {placed.Placement.X},{placed.Placement.Y}/{placed.Placement.Rotation}" ) )}." );
							// [EXT:debug-slide] smoke-only: --debug-slide flume|tube builds a slide tower beside the pool and lays a valid slide from it into the water (docs/POOLS.md, "Water slides")
							if ( GetOption( args, "--debug-slide", "flume or tube" ) is { } slideText )
								PlaceDebugSlide( level, shape.Select( cell => (X: originX + cell.X, Y: originY + cell.Y) ).ToArray(), slideText );
							if ( debugBridge != null ) level.StartDebugBridgeWalkers( debugBridge );
							return;
						}
				Log.Warning( "--debug-pool: no place near the camera target takes the whole shape." );
			}
		}
		// [EXT:online-coop-commands] --record-commands <file> records the current park's commands and its world-hash checkpoints into a command log (docs/COOP.md, "Recording and replay"),
		// written every ten seconds and when the park changes; each new park starts a new recording in the same file
		if ( GetOption( args, "--record-commands", "a command log file to write" ) is { } recordPath )
		{
			Level? recording = null;
			var frames = 0;
			void WriteLog( Level level )
			{
				if ( level.CommandLog() is not { } log )
					return;
				var full = Path.GetFullPath( recordPath );
				var temporary = full + ".tmp";
				File.WriteAllBytes( temporary, log.Serialize() );
				File.Move( temporary, full, overwrite: true );
			}
			Render.PostUpdate += () =>
			{
				var current = flow.Level is { OriginalPark: not null, IsReadOnlyVisit: false } live ? live : null;
				if ( !ReferenceEquals( current, recording ) )
				{
					if ( recording != null )
						WriteLog( recording );
					recording = current;
					recording?.StartCommandRecording();
					frames = 0;
				}
				if ( recording != null && ++frames % 600 == 0 )
					WriteLog( recording );
			};
			AppDomain.CurrentDomain.ProcessExit += ( _, _ ) => { if ( recording != null ) WriteLog( recording ); };
		}
		// [EXT:debug-sim-minutes] smoke-only: once the original level is up, runs its fixed-step simulation (guests, ride scripts, economy) for that many park minutes
		// as fast as it goes and prints the scream statistics (docs/AUDIO.md "Scream diagnostics"); use it with --capture-world, which ends the run afterwards.
		var debugSim = GetOption( args, "--debug-sim-minutes", "park minutes to simulate for the audio diagnostics" );
		if ( debugSim != null && double.TryParse( debugSim, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var simMinutes ) )
		{
			var simFrames = 0;
			Render.PostUpdate += RunDebugSim;
			void RunDebugSim()
			{
				if ( flow.Level is not { OriginalPark: not null } level || ++simFrames < 30 )
					return;
				Render.PostUpdate -= RunDebugSim;
				// the park door opens as the player's button would; guests arrive and the rides run their scripts with riders
				level.Park?.Economy.OpenPark();
				GameAudio.Screams.Reset();
				var total = M3Gate.TicksForMinutes( simMinutes );
				var perMinute = M3Gate.TicksForMinutes( 1 );
				var clock = System.Diagnostics.Stopwatch.StartNew();
				for ( long tick = 1; tick <= total; tick++ )
				{
					level.AdvanceSimulation( FixedStepClock.TickDuration );
					if ( tick % (perMinute * 5) == 0 || tick == total )
					{
						var stats = GameAudio.Screams;
						Log.Trace( $"--debug-sim-minutes: minute {tick / perMinute}: {level.Guests?.Guests.Count} guests, park {(level.Park?.Economy.IsParkOpen == true ? "open" : "closed")}, {level.Park?.Economy.Counters[ParkCounters.Admissions]} admissions, {stats.Starts} scream starts, {stats.Refusals} refused (leaked), {stats.Stops} stops, {clock.Elapsed.TotalSeconds:0} s." );
					}
				}
				var final = GameAudio.Screams;
				Log.Trace( $"--debug-sim-minutes: bands (STARTSCREAM operand: count) {string.Join( ", ", final.Bands.Select( pair => $"{pair.Key}: {pair.Value}" ) )}." );
				foreach ( var (name, counts) in final.PerRide.OrderByDescending( pair => pair.Value.Refusals ) )
					Log.Trace( $"--debug-sim-minutes: {name}: {counts.Starts} starts, {counts.Refusals} refused, {counts.Stops} stops." );
			}
		}
		// [EXT:debug-guest-looks] for captures: dresses the park's guests as they appear, so captures can show the balloons and costumes
		// without waiting for shop visits: every guest with an even id gets a balloon of the longest life, every third a costume, each once, and every
		// fourth shows a thought bubble again whenever its last one went, and every fifth drops a litter item (kinds 1-8 in turn) on its cell.
		// The balloon still runs down, rides still put it away and the renderers draw them as for a bought one.
		if ( args.Contains( "--debug-guest-looks" ) )
		{
			// Guests seen in the current park only: a new park starts afresh (its ids restart), and guests who left are forgotten.
			var dressed = new HashSet<Guest>( ReferenceEqualityComparer.Instance );
			GuestSimulation? park = null;
			Render.PostUpdate += () =>
			{
				if ( flow.Level?.Guests is not { } guests )
					return;
				if ( guests != park )
				{
					park = guests;
					dressed.Clear();
				}
				dressed.IntersectWith( guests.Guests );
				foreach ( var guest in guests.Guests )
				{
					// every fourth guest keeps a thought bubble up, cycling through the seventeen guest thoughts, so captures show them
					if ( guest.Id % 4 == 0 && guest.BubbleThought == GuestThought.None )
						(guest.BubbleThought, guest.BubbleTurn) = ((GuestThought)(1 + guest.Id / 4 % 17), guests.ParkTurn);
					if ( !dressed.Add( guest ) )
						continue;
					if ( guest.Id % 2 == 0 )
						(guest.HasBalloon, guest.BalloonLife) = (true, GuestAppearance.MaximumBalloonLife);
					if ( guest.Id % 3 == 0 )
						guest.InCostume = true;
					// every fifth guest litters the cell it stands on, one kind after the other (the drop takes its draws from the park's generator: a capture aid, not a run to compare)
					if ( guest.Id % 5 == 0 && flow.Level.Park?.Economy.Litter is { } litter )
						litter.Drop( (int)guest.X, (int)guest.Y, (LitterType)(1 + guest.Id / 5 % 8) );
				}
			};
		}
		// [EXT:debug-staff] for captures: once the park is up, hires a handyman, a mechanic, a guard and a researcher and puts them on valid cells round the entrance (cell 47, 17) or the cell of --debug-staff-at,
		// as the Place tool does, so captures can show the staff beside the guests; --debug-staff-at x,y picks another centre cell. Staff and guests walk on from there.
		if ( args.Contains( "--debug-staff" ) )
		{
			var staffCentre = GetOption( args, "--debug-staff-at", "x,y cell" )?.Split( ',' ).Select( int.Parse ).ToArray() ?? new[] { 47, 17 };
			var staffFrames = 0;
			var staffPlaced = false;
			Render.PostUpdate += () =>
			{
				if ( staffPlaced || flow.Level is not { StaffPlacement: { } placement, Park: { } park } || ++staffFrames < 60 )
					return;
				staffPlaced = true;
				var used = new List<(int X, int Y)>();
				foreach ( var type in new[] { StaffType.Handyman, StaffType.Mechanic, StaffType.Guard, StaffType.Researcher, StaffType.Handyman, StaffType.Mechanic } )
				{
					var candidate = park.Economy.Staff.Candidates.FirstOrDefault( item => item.Type == type );
					if ( candidate == null || !placement.BeginHire( candidate.Id ) )
						continue;
					for ( var radius = 0; radius < 14; radius++ )
					{
						var spot = Enumerable.Range( staffCentre[0] - radius, 2 * radius + 1 ).SelectMany( x => Enumerable.Range( staffCentre[1] - radius, 2 * radius + 1 ).Select( y => (X: x, Y: y) ) )
							.FirstOrDefault( cell => used.All( other => Math.Abs( other.X - cell.X ) + Math.Abs( other.Y - cell.Y ) >= 2 ) && placement.IsValid( cell.X, cell.Y ) );
						if ( spot == default )
							continue;
						placement.Click( spot.X, spot.Y );
						used.Add( spot );
						Log.Trace( $"--debug-staff: {type} at {spot.X},{spot.Y}." );
						break;
					}
					placement.Cancel();
				}
			};
		}
		// [EXT:debug-forecast] smoke-only (Debug builds): posts one forecast quality through the simulation's own
		// warning schedule as soon as the park's weather has started, so captures can show the forecast banner
		// deterministically. The countdown, apply and the weather itself still come from ParkWeather's machinery;
		// Release builds never see the flag.
		var debugForecast = GetOption( args, "--debug-forecast", "a forecast quality 1..100 for smoke captures" );
		if ( debugForecast != null )
		{
			if ( !int.TryParse( debugForecast, out var forecastQuality ) || forecastQuality is < 1 or > 100 )
				throw new ArgumentException( "--debug-forecast expects a quality from 1 to 100." );
			Render.PostUpdate += PostDebugForecast;
			void PostDebugForecast()
			{
				if ( flow.Level?.Weather is not { Started: true } weather )
					return;
				Render.PostUpdate -= PostDebugForecast;
				weather.DebugPostForecast( forecastQuality );
				Log.Trace( $"--debug-forecast: quality {forecastQuality} posted as a forecast {weather.DaysOfWarning} days out (apply day {weather.ForecastApplyDay})." );
			}
		}
		// [EXT:debug-hud] smoke-only (Debug builds): once the park's HUD is up, runs a list of interface actions for captures (--capture-interface):
		// staff and buy open those windows, buyitem:<info id>[,<yaw degrees>] opens the buy window on that object type's row (making it available as the research cheat does when it is still in the lab, and holding its preview at that turn), msg:<text> posts a message, msgaction:<kind>+<kind> posts one with action buttons, pause and confirm open those menus, select:<n> selects the n-th placed object, waves opens the first wave machine's settings window (waves:select only selects it), help:<text> forces the popup help line, models:<a+b+c> draws ui.wad models, finale starts the end-of-park finale and winningmessage shows the Instant Action completion box.
		var debugHud = GetOption( args, "--debug-hud", "a '|' separated action list for interface captures" );
		if ( debugHud != null )
		{
			var hudFrames = 0;
			Render.PostUpdate += RunDebugHud;
			void RunDebugHud()
			{
				if ( flow.Hud is not { } hud || flow.Level is not { OriginalPark: not null } || ++hudFrames < 40 )
					return;
				Render.PostUpdate -= RunDebugHud;
				foreach ( var action in debugHud.Split( '|' ) )
				{
					var (name, argument) = action.Split( ':', 2) is [var head, var tail] ? (head, tail) : (action, "");
					switch ( name )
					{
						case "staff": hud.OpenStaffWindow(); break;
						case "info": hud.OpenParkInfoWindow(); break;
						case "infoseries": hud.SetParkInfoSeries( (OpenTPW.Hud.ParkInfoSeries)0x1f, false ); hud.SetParkInfoSeries( (OpenTPW.Hud.ParkInfoSeries)int.Parse( argument ), true ); break;
						case "infoperiod": hud.SetParkInfoPeriod( int.Parse( argument ) ); break;
						case "buy": hud.OpenBuyWindow(); break;
						case "buyitem":
							var buyParts = argument.Split( ',' );
							float? buyYaw = buyParts.Length > 1 ? float.Parse( buyParts[1], System.Globalization.CultureInfo.InvariantCulture ) : null;
							if ( !hud.DebugBuyItem( int.Parse( buyParts[0], System.Globalization.CultureInfo.InvariantCulture ), buyYaw ) )
								Log.Warning( $"--debug-hud: no buy row for object type {buyParts[0]}." );
							break;
						// [EXT:debug-evening] the buy window on one of its tabs (rides, sideshows, shops, features), optionally with one row selected and scrolled into view ("buytab:features:Tiki Torch"), for captures such as the torches in the features
						case "buytab":
						{
							var (tab, row) = argument.Split( ':', 2 ) is [var tabName, var rowName] ? (tabName, rowName) : (argument, null);
							hud.OpenBuyWindow();
							hud.SelectCategory( Enum.Parse<OpenTPW.Hud.BuildCategory>( tab, true ) );
							var rowIndex = row == null ? -1 : hud.BuyItems.ToList().FindIndex( item => hud.ListName( item ) == row );
							if ( rowIndex >= 0 )
							{
								hud.SelectBuyItem( rowIndex );
								hud.SetBuyFirstRow( rowIndex - 4 );
							}
							else if ( row != null )
								Log.Warning( $"--debug-hud: no row {row} in the buy window's {tab}." );
							break;
						}
						case "msg": hud.PostGameMessage( argument ); break;
						case "openmsg": hud.OpenPendingMessage( 0 ); break;
						case "msgaction": hud.PostGameMessage( "A message with two actions", argument.Split( '+' ).Select( kind => new OpenTPW.Hud.GameMessageAction( Enum.Parse<OpenTPW.Hud.GameMessageKind>( kind, true ), () => { } ) ).ToArray() ); break;
						case "pause": hud.OpenPauseMenu(); break;
						case "confirm": hud.Stack.Push( OpenTPW.UI.Original.UiDialogs.Message( "debugConfirm", () => "QUIT GAME\n\nAre you sure you want to quit the game ?", (() => "Yes", () => { }), (() => "No", () => { }) ) ); break;
						case "waves": if ( !hud.DebugWaveSettings( argument == "select" ) ) Log.Warning( "--debug-hud: no wave machine to open." ); break;
						case "select": if ( flow.Level.Objects.Objects.Skip( int.Parse( argument ) ).FirstOrDefault() is { } selected ) hud.SelectObject( selected ); break;
						case "help": hud.DebugHelp( argument ); break;
						case "ride": hud.DebugRide( argument ); break;
						case "finale": hud.DebugFinale(); break;
						case "winningmessage": hud.DebugWinningMessage(); break;
						case "models": hud.DebugShowModels( argument.Split( '+' ) ); break;
						default: Log.Warning( $"--debug-hud: unknown action {name}." ); break;
					}
				}
			}
		}
#endif
		if ( startsFrontEnd )
		{
			if ( autorun != null )
			{
				RunAutorun( flow, autorun, smoke, startIntro );
				return;
			}
			if ( startIntro != null )
			{
				using var intro = startIntro();
				Render.Run();
				return;
			}
			if ( GameEditions.IsThemeParkInc )
			{
				// [EXT:tpi-front-end-phase1] a Theme Park Inc start without a world opens its Personnel File (docs/THEME-PARK-INC.md)
				flow.ShowThemeParkIncFrontEnd();
				Render.Run();
				return;
			}
			flow.ShowFrontEnd();
			if ( smoke )
			{
				using var frontEndSmokeTest = new FrontEndSmokeTest( flow );
				Render.PostUpdate += frontEndSmokeTest.Update;
				Render.Run();
				frontEndSmokeTest.VerifyCompleted();
			}
			else
				Render.Run();
			return;
		}
		var level = visit != null
			? flow.StartLevel( visit.Level, original: !visit.IsSandbox, developerPanels: true, visit: visit )
			: originalLevelIndex >= 0
			? flow.StartLevel( args[originalLevelIndex + 1], original: true, developerPanels: !GameEditions.IsThemeParkInc, playerSave: ReadPlayerSave( args ) )
			: flow.StartLevel( "jungle", original: false, developerPanels: true );

		if ( sliceSmoke )
		{
			using var sliceSmokeTest = new ThemeParkIncSmokeTest( flow );
			Render.PostUpdate += sliceSmokeTest.Update;
			Render.Run();
			Render.PostUpdate -= sliceSmokeTest.Update;
			sliceSmokeTest.VerifyCompleted();
			return;
		}

		//
		// Run game loop
		//
		using var advisor = CreateAdvisor( args );
		if ( advisor != null )
			Render.OnRender += advisor.Render;
		if ( visit != null && smoke )
		{
			// --smoke-presence adds scripted online players round the park entrance (VisitPresenceSmoke)
			using var visitSmokeTest = new VisitSmokeTest( level, flow.Hud, input => flow.InjectedInput = input,
				args.Contains( "--smoke-presence" ) ? visited => VisitPresenceSmoke.Attach( visited, flow.AttachPresence ) : null, flow.Context );
			Render.PostUpdate += visitSmokeTest.Update;
			Render.Run();
			visitSmokeTest.VerifyCompleted();
		}
		else if ( advisor != null && args.Contains( "--smoke-test" ) )
		{
			using var advisorSmokeTest = new AdvisorSmokeTest( advisor );
			Render.PostUpdate += advisorSmokeTest.Update;
			Render.Run();
			advisorSmokeTest.VerifyCompleted();
		}
		else if ( args.Contains( "--smoke-test" ) )
		{
			using var smokeTest = new SandboxSmokeTest( level, flow.Hud, flow.Context, input => flow.InjectedInput = input );
			Render.PostUpdate += smokeTest.Update;
			Render.Run();
			smokeTest.VerifyCompleted();
		}
		else
			Render.Run();
	}

	/// <summary>
	/// [EXT:online-coop-lockstep] Whether a run's options may change the park outside the commands, so the game shares no park (<see cref="CoopSession.CanShare"/>): every
	/// <c>--debug-*</c> and <c>--perf-*</c> option and every smoke (<see cref="SmokeFlags"/>).
	/// </summary>
	internal static bool ChangesParkOutsideCommands( string[] args ) =>
		args.Any( arg => arg.StartsWith( "--debug-", StringComparison.Ordinal ) || arg.StartsWith( "--perf-", StringComparison.Ordinal ) ) || args.Intersect( SmokeFlags ).Any();

	/// <summary>The smokes. Each builds or edits its park outside the commands (<see cref="ChangesParkOutsideCommands"/>), and each is an automated run (<see cref="IsAutomatedRun"/>).</summary>
	internal static readonly string[] SmokeFlags = { "--smoke-test", "--tpi-smoke-test", "--park-resume-smoke", "--smoke-presence" };

	/// <summary>
	/// The automated modes that [EXT:silent-automated-runs] and [EXT:offscreen-automated-runs] share, so the two rules cannot drift apart: smokes
	/// (<c>--smoke-test</c>, <c>--tpi-smoke-test</c>, <c>--park-resume-smoke</c>, <c>--smoke-presence</c>), captures (<c>--capture-*</c>), perf runs
	/// (<c>--perf-*</c>), gates, guest reports, asset validation, every <c>--debug-*</c> option (they exist for smoke captures and agent runs, none is a
	/// player option), <c>--offscreen</c> and <c>--headless</c>. A plain interactive launch (the front end, <c>--sandbox</c>, <c>--load-original-level</c>,
	/// <c>--play-movie</c> without <c>--smoke-test</c>, <c>--setup</c>) is none of them.
	/// </summary>
	internal static bool IsAutomatedRun( string[] args ) =>
		args.Any( arg => arg.StartsWith( "--capture-", StringComparison.Ordinal ) || arg.StartsWith( "--perf-", StringComparison.Ordinal ) || arg.StartsWith( "--debug-", StringComparison.Ordinal ) )
		|| args.Intersect( AutomatedFlags ).Any();

	/// <summary>
	/// The automated modes beyond the smokes. [EXT:online-coop-snapshot-join] <c>--coop-join-smoke</c> is one, never a <see cref="SmokeFlags"/> entry: its host phase shares the park it built before
	/// the session started, which a smoke flag's <see cref="ChangesParkOutsideCommands"/> would refuse.
	/// </summary>
	private static readonly string[] AutomatedFlags = SmokeFlags.Concat( new[]
	{
		"--coop-join-smoke",
		"--perf-run", "--perf-hash-tick", "--startup-profile", "--hitch-report",
		"--offscreen", "--headless", "--m3-gate", "--m4-gate", "--determinism-digest", "--guest-report", "--guest-compare", "--validate-assets", "--export-signs",
	} ).ToArray();

	/// <summary>
	/// [EXT:silent-automated-runs] every automated run (<see cref="IsAutomatedRun"/>) opens no audio device, so tests and agents on a developer machine make
	/// no sound. <c>--smoke-audio</c> or <c>--audible</c> opts back in to sound; <c>--silent</c> or <c>OPENTPW_SILENT=1</c> silences any run, and so does
	/// <c>OPENTPW_OFFSCREEN=1</c> unless the window is made visible. A plain interactive launch is never silenced. The game logic and its timing are unchanged.
	/// </summary>
	internal static bool IsSilentRun( string[] args, string? silentEnvironment, string? offscreenEnvironment = null )
	{
		if ( args.Contains( "--silent" ) || silentEnvironment == "1" )
			return true;
		if ( args.Contains( "--smoke-audio" ) || args.Contains( "--audible" ) )
			return false;
		return IsAutomatedRun( args ) || (offscreenEnvironment == "1" && !WantsVisibleWindow( args ));
	}

	/// <summary>
	/// [EXT:offscreen-automated-runs] every automated run (<see cref="IsAutomatedRun"/>: smokes, captures, perf runs, debug runs, gates and the rest) creates
	/// its window hidden and renders into an offscreen framebuffer, so no window appears on the developer's display, focus is not stolen and a locked
	/// screen does not matter (the user's request of 2026-10-19: smoke tests and agent runs on a virtual screen; macOS has no Xvfb, the hidden window and
	/// its framebuffer are that virtual screen). Captures read the same render textures either way, and perf runs are paced at the 60 Hz the documented
	/// baselines were measured with (docs/ENGINE-PERFORMANCE.md). <c>--offscreen</c> or <c>OPENTPW_OFFSCREEN=1</c> does the same for any run;
	/// <c>--no-offscreen</c>, its alias <c>--visible</c> (the window's counterpart of <c>--audible</c>), or <c>OPENTPW_OFFSCREEN=0</c> keeps the visible
	/// window. Sound is a separate choice (<see cref="IsSilentRun"/>): <c>--audible</c> does not show the window and <c>--visible</c> does not unmute.
	/// </summary>
	/// <summary>
	/// The game data's file system, with the WAD and SDT archive handlers. It remembers its path lookups (<see cref="BaseFileSystem.CacheLookups"/>), as the
	/// browser's does: without that every read listed each directory along its path again, 15,770 listings for a park start and a few hundred for an object's
	/// first placement (issue #754, docs/ENGINE-PERFORMANCE.md, "Start and build hitches"). Nothing in a session adds files below the data folder (a new game
	/// folder applies at the next start); what changes what the lookups see invalidates them itself: the mod overrides, the language's interface archive and
	/// the mod content roots.
	/// </summary>
	internal static BaseFileSystem CreateGameDataFileSystem( string dataDirectory )
	{
		var files = new BaseFileSystem( dataDirectory ) { CacheLookups = true };
		files.RegisterArchiveHandler<WadArchive>( ".wad" );
		files.RegisterArchiveHandler<SdtArchive>( ".sdt" );
		return files;
	}

	internal static bool IsOffscreenRun( string[] args, string? offscreenEnvironment )
	{
		if ( WantsVisibleWindow( args ) || offscreenEnvironment == "0" )
			return false;
		return offscreenEnvironment == "1" || IsAutomatedRun( args );
	}

	/// <summary>The opt-out of <see cref="IsOffscreenRun"/>: <c>--no-offscreen</c> or <c>--visible</c>.</summary>
	private static bool WantsVisibleWindow( string[] args ) => args.Contains( "--no-offscreen" ) || args.Contains( "--visible" );

	/// <summary>The CD launcher screen for the game's language, or null when it is disabled or the CD's Autorun files are absent.</summary>
	private static AutorunScreen? CreateAutorun( string[] args, SetupSettings settings, string dataDirectory )
	{
		var cd = GetOption( args, "--cd-data", "the original CD's folder" ) ?? Environment.GetEnvironmentVariable( "OPENTPW_CD_DATA" );
		var language = GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage;
		var assets = AutorunLauncher.FindAssets( args, settings, Settings.Default.GamePath, cd, language );
		return assets == null ? null : AutorunLauncher.Create( assets, Settings.Default.GamePath );
	}

	/// <summary>
	/// Shows the CD launcher, then (on Play) the front end. The hand-over runs after the frame so the front end's first
	/// update precedes its first draw; Exit has already closed the window.
	/// </summary>
	/// <summary>Starts the start-up movies (docs/TGQ-MOVIES.md); the front end follows when they end. Dispose after the loop.</summary>
	private static IDisposable StartIntro( GameFlow flow, string[] args, string dataDirectory )
	{
		var intro = new IntroSequence( dataDirectory, IntroPlaylist.For( DateTime.Now ), !args.Contains( "--mute" ), GameOptions.Current.MovieGain );
		Render.OnUpdate += intro.Update;
		Render.OnRender += intro.Draw;
		intro.Completed += () =>
		{
			Render.OnUpdate -= intro.Update;
			Render.OnRender -= intro.Draw;
			Render.OnUpdate += flow.Update;
			Render.OnRender += flow.Render;
			flow.DiscardHeldInput();
			flow.ShowFrontEnd();
		};
		return intro;
	}

	private static void RunAutorun( GameFlow flow, AutorunScreen autorun, bool smoke, Func<IDisposable>? startIntro )
	{
		using var screen = autorun;
		IDisposable? intro = null;
		FrontEndSmokeTest? frontEndSmokeTest = null;
		AutorunSmokeTest? autorunSmokeTest = null;
		Action? handOver = null;
		handOver = () =>
		{
			if ( screen.Result == AutorunResult.None )
				return;
			Render.PostUpdate -= handOver;
			Render.OnUpdate -= screen.Update;
			Render.OnRender -= screen.Render;
			if ( screen.Result != AutorunResult.Play )
				return;
			Render.WorldScalingAllowed = true;
			// Free the launcher's GPU resources one frame later, after its last frame was submitted.
			Action? release = null;
			release = () =>
			{
				Render.PostUpdate -= release;
				screen.Dispose();
			};
			Render.PostUpdate += release;
			if ( startIntro != null )
			{
				intro = startIntro();
				return;
			}
			Render.OnUpdate += flow.Update;
			Render.OnRender += flow.Render;
			// Enter or Space held to press Play must not count again as the front end's first key.
			flow.DiscardHeldInput();
			flow.ShowFrontEnd();
			if ( smoke )
			{
				frontEndSmokeTest = new FrontEndSmokeTest( flow );
				Render.PostUpdate += frontEndSmokeTest.Update;
			}
		};
		try
		{
			// Like movies, the launcher is not 3D world content: it renders at output size.
			Render.WorldScalingAllowed = false;
			Render.OnUpdate += screen.Update;
			Render.OnRender += screen.Render;
			if ( smoke )
			{
				autorunSmokeTest = new AutorunSmokeTest( screen );
				Render.PostUpdate += autorunSmokeTest.Update;
			}
			Render.PostUpdate += handOver;
			Render.Run();
			if ( smoke )
			{
				autorunSmokeTest!.VerifyCompleted();
				if ( frontEndSmokeTest == null )
					throw new InvalidOperationException( "Native autorun smoke test failed: the front end did not start after Play." );
				frontEndSmokeTest.VerifyCompleted();
			}
		}
		finally
		{
			intro?.Dispose();
			autorunSmokeTest?.Dispose();
			frontEndSmokeTest?.Dispose();
		}
	}

	/// <summary>
	/// Finds the game folder (and the saved CD), or asks for it in the setup wizard (docs/SETUP.md).
	/// Null when the player closes the wizard without choosing one.
	/// </summary>
	private static string[]? ResolveGameFolder( string[] args, string? commandLinePath ) =>
		ResolveDesktopGameFolder( args, commandLinePath, SetupWizard.Run );

	/// <summary>Uses the real starter by default; the callback boundary permits CPU-only startup/cancellation controls.</summary>
	internal static string[]? ResolveDesktopGameFolder( string[] args, string? commandLinePath, Func<SetupSettings, InstallationDiscoveryResult?, SetupWizard.Result?> runWizard,
		Func<InstallationDiscoveryResult>? searchOverride = null )
	{
		if ( args.Contains( "--setup" ) && DesktopSetupSelection.HasExplicitMode( args ) )
			throw new ArgumentException( "Choose --setup or an explicit tool mode (such as --sandbox, --front-end, --load-original-level or --m3-gate); setup cannot replace an explicit tool start." );
		var setupPath = SetupSettings.GetDefaultPath();
		var saved = SetupSettings.Load( setupPath );
		InstallationDiscoveryResult? discovery = null;
		var environmentPath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		// Only --setup opens the wizard on request; an ordinary start uses the saved choice (docs/SETUP.md).
		if ( DesktopSetupSelection.ShouldShowStarter( args ) )
			return RunStarter( args, saved, setupPath, null, runWizard );
		var ordinary = DesktopSetupSelection.CanUseRememberedEdition( args, commandLinePath, environmentPath );
		// [EXT:tpi-viewer] a saved experimental Theme Park Inc world starts directly; a stale folder or world opens the wizard.
		if ( ordinary && saved.SelectedEdition == GameEdition.ThemeParkInc )
		{
			var selection = new DesktopSetupSelection( saved );
			if ( !string.IsNullOrWhiteSpace( selection.Path ) )
			{
				var check = InstallationDiscovery.InspectAsync( selection.Path, GameEdition.ThemeParkInc ).GetAwaiter().GetResult();
				if ( check.Reports.FirstOrDefault() is { } report && selection.AcceptInspection( selection.Revision, report ) && selection.CanLaunch( args ) )
				{
					args = selection.LaunchArguments( args );
					Settings.Default.GamePath = selection.Path;
					Log?.Trace( $"Game folder: {selection.Path} (saved Theme Park Inc {(args.Contains( "--front-end" ) ? "install, Personnel File" : $"world {selection.World}")})." );
					return args;
				}
			}
			// [EXT:offscreen-automated-runs] an offscreen run cannot show the setup window (it would open visibly and wait for input): it stops instead
			if ( Window.OffscreenRequested )
				throw new DirectoryNotFoundException( "The saved Theme Park Inc folder or world is no longer available, and an offscreen run cannot open setup. Use --game-path or OPENTPW_GAME_PATH." );
			Log?.Warning( "The saved Theme Park Inc folder or world is no longer available; opening setup." );
			return RunStarter( args, saved, setupPath, null, runWizard );
		}
		if ( ordinary && !Enum.IsDefined( saved.SelectedEdition ) )
		{
			Log?.Warning( "The saved edition choice is unknown; opening setup." );
			return RunStarter( args, saved, setupPath, null, runWizard );
		}
		var resolved = GamePathResolution.Resolve( commandLinePath, environmentPath, new( null, null ), null, () => Array.Empty<string>() );
		if ( resolved == null )
		{
			// Saved, then legacy, then detected; the saved folder must still pass inspection.
			foreach ( var (path, source) in new[] { (saved.GamePath, GamePathSource.Saved), (LegacySettings.GamePath, GamePathSource.Legacy) } )
			{
				if ( string.IsNullOrWhiteSpace( path ) ) continue;
				// As Theme Park World: a folder with Theme Park Inc data must not pass as TPW and then fail the edition check at start.
				var inspected = InstallationDiscovery.InspectAsync( path, GameEdition.ThemeParkWorld ).GetAwaiter().GetResult();
				if ( inspected.Reports.FirstOrDefault() is { IsUsable: true } report )
				{
					resolved = (report.Path, source);
					break;
				}
			}
			if ( resolved == null )
			{
				discovery = searchOverride?.Invoke() ?? InstallationDiscovery.SearchAsync().GetAwaiter().GetResult();
				if ( discovery.Reports.FirstOrDefault() is { IsUsable: true } report )
					resolved = (report.Path, GamePathSource.Detected);
			}
		}
		if ( resolved == null )
		{
			// [EXT:offscreen-automated-runs] nor does an offscreen run open the setup window (it would appear and wait for input)
			if ( !GamePathResolution.IsInteractive( args ) || DesktopSetupSelection.HasHeadlessGate( args ) || Window.OffscreenRequested )
				throw new DirectoryNotFoundException( "Theme Park World data not found. Use --game-path or OPENTPW_GAME_PATH, or start OpenTPW without arguments to choose the folder in the setup window." );
			return RunStarter( args, saved, setupPath, discovery, runWizard );
		}
		if ( resolved.Value.Source is GamePathSource.Legacy or GamePathSource.Detected )
		{
			saved = saved with { GamePath = resolved.Value.Path };
			TrySave( saved, setupPath );
		}
		Settings.Default.GamePath = resolved.Value.Path;
		Log?.Trace( $"Game folder: {resolved.Value.Path} ({resolved.Value.Source})." );

		// The saved CD stands in for --cd-data, unless the folder came from a developer override or a CD is given.
		if ( resolved.Value.Source is not (GamePathSource.CommandLine or GamePathSource.Environment) )
			args = WithSavedCd( args, saved );
		return args;
	}

	private static string[] WithSavedCd( string[] args, SetupSettings saved ) =>
		saved.CdPath != null && !args.Contains( "--cd-data" ) && string.IsNullOrWhiteSpace( Environment.GetEnvironmentVariable( "OPENTPW_CD_DATA" ) )
			&& InstallationDiscovery.InspectAsync( saved.CdPath ).GetAwaiter().GetResult().Reports.FirstOrDefault()?.DataDirectory != null
			? [.. args, "--cd-data", saved.CdPath] : args;

	/// <summary>
	/// Asks in the setup wizard (<c>--setup</c>, a first start, or a saved folder/world that no longer works), re-inspects
	/// the choice before using or remembering it, and returns the launch arguments. Null when the wizard is closed.
	/// </summary>
	private static string[]? RunStarter( string[] args, SetupSettings saved, string setupPath, InstallationDiscoveryResult? discovery,
		Func<SetupSettings, InstallationDiscoveryResult?, SetupWizard.Result?> runWizard )
	{
		var offered = saved;
		while ( true )
		{
			var chosen = runWizard( offered, discovery );
			if ( chosen == null )
			{
				Log?.Trace( "Setup closed without choosing a game folder." );
				return null;
			}
			var selection = new DesktopSetupSelection( saved );
			selection.Select( chosen.Edition, chosen.GamePath );
			selection.World = chosen.World;
			var check = InstallationDiscovery.InspectAsync( chosen.GamePath, chosen.Edition ).GetAwaiter().GetResult();
			if ( check.Reports.FirstOrDefault() is not { } report || !selection.AcceptInspection( selection.Revision, report ) || !selection.CanStart )
			{
				// A timed-out or stale re-inspection keeps the player in the wizard, with the typed choice and a reason, rather than stopping the app.
				var notice = check.TimedOut ? "Checking the selected folder took too long. Try again." : "The selected edition folder or world is no longer available.";
				Log?.Warning( notice );
				offered = chosen.Edition == GameEdition.ThemeParkInc
					? saved with { SelectedEdition = chosen.Edition, ThemeParkIncPath = chosen.GamePath, ThemeParkIncWorld = chosen.World }
					: saved with { SelectedEdition = chosen.Edition, GamePath = chosen.GamePath };
				discovery = new InstallationDiscoveryResult( Array.Empty<InstallationReport>(), false, null ) { Notice = notice };
				continue;
			}
			args = selection.LaunchArguments( args );
			saved = selection.Remember( saved );
			if ( chosen.Edition == GameEdition.ThemeParkWorld ) saved = saved with { CdPath = chosen.CdPath };
			TrySave( saved, setupPath );
			Settings.Default.GamePath = selection.Path;
			Log?.Trace( $"Game folder: {selection.Path} ({GamePathSource.Wizard})." );
			return chosen.Edition == GameEdition.ThemeParkWorld ? WithSavedCd( args, saved ) : args;
		}
	}

	private static void TrySave( SetupSettings settings, string path )
	{
		try
		{
			settings.Save( path );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Could not store the game folder in {path} ({exception.Message}); setup will ask again next time." );
		}
	}

	private static string GetSaveDirectory() => ResolveSaveDirectory( Settings.Default.GamePath, Path.GetDirectoryName( SetupSettings.GetDefaultPath() )! );

	/// <summary>
	/// The game folder's <c>save</c> directory when files can be written there, else <c>save</c> under <paramref name="fallbackRoot"/>.
	/// OpenTPW runs without administrator rights, so a game folder under Program Files usually is not writable even when its
	/// <c>save</c> directory already exists: a probe file is written and deleted to find out before the first save does. Saves an
	/// earlier, elevated OpenTPW wrote beside such a game are copied into the fallback once (an existing file is never replaced).
	/// </summary>
	internal static string ResolveSaveDirectory( string gamePath, string fallbackRoot )
	{
		var beside = Path.Combine( gamePath, "save" );
		try
		{
			Directory.CreateDirectory( beside );
			using ( new FileStream( Path.Combine( beside, $".opentpw-write-test-{Guid.NewGuid():N}" ), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose ) ) { }
			return beside + Path.DirectorySeparatorChar;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			var fallback = Path.Combine( fallbackRoot, "save" );
			Directory.CreateDirectory( fallback );
			Log.Warning( $"The game folder is read-only ({exception.Message}); saves go to {fallback}." );
			// The marker makes the copy happen once: a save, autosave or restart snapshot deleted later must not come back
			var marker = Path.Combine( fallback, CopiedSavesMarker );
			if ( Directory.Exists( beside ) && !File.Exists( marker ) )
			{
				var copied = CopyMissingSaves( beside, fallback );
				try { File.WriteAllText( marker, $"Earlier saves copied from {beside}{Environment.NewLine}" ); }
				catch ( Exception markerException ) when ( markerException is IOException or UnauthorizedAccessException ) { Log.Warning( $"Could not write {marker} ({markerException.Message})." ); }
				if ( copied > 0 )
					Log.Info( $"Copied {copied} earlier save file(s) from {beside} to {fallback}; the originals are left in place." );
			}
			return fallback + Path.DirectorySeparatorChar;
		}
	}

	/// <summary>Written in the fallback save folder once the earlier saves beside the game have been copied.</summary>
	internal const string CopiedSavesMarker = ".copied-from-game-folder";

	/// <summary>Copies every file under <paramref name="source"/> that <paramref name="destination"/> lacks; unreadable files are skipped.</summary>
	private static int CopyMissingSaves( string source, string destination )
	{
		var copied = 0;
		try
		{
			foreach ( var file in Directory.EnumerateFiles( source, "*", SearchOption.AllDirectories ) )
			{
				var target = Path.Combine( destination, Path.GetRelativePath( source, file ) );
				if ( File.Exists( target ) )
					continue;
				try
				{
					Directory.CreateDirectory( Path.GetDirectoryName( target )! );
					File.Copy( file, target, overwrite: false );
					copied++;
				}
				catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
				{
					Log.Warning( $"Could not copy the earlier save {file} ({exception.Message})." );
				}
			}
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Could not list the earlier saves in {source} ({exception.Message})." );
		}
		return copied;
	}

	/// <summary><c>--build-texture-pack --upscaler &lt;realesrgan-ncnn-vulkan&gt;</c>: builds the optional local texture pack (docs/TEXTURE-PACKS.md).</summary>
	private static void BuildTexturePack( string[] args, string dataDirectory )
	{
		var upscaler = GetOption( args, "--upscaler", "the path of a realesrgan-ncnn-vulkan executable" )
			?? throw new ArgumentException( "--build-texture-pack requires --upscaler <path of realesrgan-ncnn-vulkan> (docs/TEXTURE-PACKS.md)." );
		var model = GetOption( args, "--upscale-model", "a Real-ESRGAN model name such as realesrgan-x4plus" ) ?? "realesrgan-x4plus";
		var packName = GetOption( args, "--texture-pack-name", "a pack name such as enhanced or detailed" ) ?? TexturePack.EnhancedName;
		if ( !TexturePack.IsValidName( packName ) )
			throw new ArgumentException( $"--texture-pack-name must be a plain directory name (letters, digits, '-', '_', '.'), not '{packName}'." );
		var packDirectory = GetOption( args, "--texture-pack-dir", "a directory for the texture pack" ) ?? TexturePack.PackDirectory( packName );
		// Optional 1x de-artifact pass before upscaling (docs/TEXTURE-PACKS.md); without a model nothing changes.
		var prepassModel = GetOption( args, "--prepass-model", "the path of an ONNX de-artifact model" );
		using var prepassTiles = prepassModel == null ? null : new OnnxTileModel( prepassModel );
		var prepass = prepassTiles == null ? null : new TiledPrepass( prepassTiles, Path.GetFileName( prepassModel! ) );
		if ( prepassTiles != null )
			Log.Trace( $"Pre-pass {prepass!.Model} on {prepassTiles.Provider}, {prepassTiles.TileSize}x{prepassTiles.TileSize} tiles." );
		var subtree = GetOption( args, "--texture-pack-subtree", "a data-relative directory such as levels/jungle" ) ?? "";
		// Interface art uses a model suited to drawn art; --texture-pack-no-interface keeps it original.
		var interfaceModel = GetOption( args, "--interface-model", "a Real-ESRGAN model name for interface art" ) ?? "realesrgan-x4plus-anime";
		var interfaceUpscaler = args.Contains( "--texture-pack-no-interface" ) ? null : new RealEsrganUpscaler( upscaler, interfaceModel );
		var options = new TexturePackBuildOptions { Subtree = subtree, InterfaceOnly = args.Contains( "--texture-pack-interface-only" ),
			SpritesOnly = args.Contains( "--texture-pack-sprites-only" ), Merge = args.Contains( "--texture-pack-merge" ),
			PrepassSprites = args.Contains( "--prepass-sprites" ) };
		// Guest sprites are assembled into atlases from the sprite banks; they join the build under their pack keys.
		var sprites = subtree.Length == 0
			? GuestSpriteAtlas.LoadKids().Concat( GuestSpriteAtlas.LoadBalloons() ).Concat( GuestSpriteAtlas.LoadThoughts() )
				.Concat( new[] { "jungle", "fantasy", "hallow", "space" }.SelectMany( theme => GuestSpriteAtlas.LoadCostumes( theme ).Concat( GuestSpriteAtlas.LoadCostumeHeads( theme ) ) ) ).ToList()
			: new List<GuestSpriteAtlas>();
		Log.Trace( $"Building texture pack from {dataDirectory}{(subtree.Length > 0 ? $"/{subtree}" : "")} into {packDirectory}{(options.Merge ? " (merging)" : "")}." );
		var manifest = TexturePackBuilder.Build( TexturePackBuilder.EnumerateGameTextures( dataDirectory, subtree )
			.Concat( sprites.Select( atlas => (atlas.PackKey, (Func<TextureData>)(() => new TextureData( atlas.Width, atlas.Height, atlas.Pixels ))) ) ), packDirectory,
			new RealEsrganUpscaler( upscaler, model ), options, message => Log.Trace( message ), interfaceUpscaler, prepass );
		Log.Trace( $"Done: {manifest.Textures} textures at {manifest.Scale}x. Choose the pack '{packName}' under Game Options -> OpenTPW -> Enhanced textures (or set \"TexturePack\": \"{packName}\" in graphics.json)." );
		if ( !string.Equals( Path.GetFullPath( packDirectory ), Path.GetFullPath( TexturePack.PackDirectory( packName ) ), StringComparison.Ordinal ) )
			Log.Warning( $"The game only loads packs under {TexturePack.PacksDirectory()}; use this one with OPENTPW_TEXTURE_PACK={Path.Combine( packDirectory, TexturePack.TexturesDirectoryName )}." );
	}

	/// <summary>
	/// Theme Park Inc is not a supported game. Its data may only be opened by the experimental slice:
	/// <c>--load-original-level arabian|science|water</c> (docs/THEME-PARK-INC.md, "Theme Park Inc level viewer").
	/// </summary>
	// [EXT:tpi-viewer] experimental slice for Theme Park Inc worlds, not original behaviour and not Theme Park Inc support.
	private static void RequireThemeParkIncViewer( string[] args, GameEditionReport edition )
	{
		if ( GameEditions.ViewerRefusal( args ) is { } refusal )
			throw new ArgumentException( refusal );
		var level = GetOption( args, "--load-original-level", "a Theme Park Inc world such as 'arabian'" );
		Log.Warning( $"Theme Park Inc data detected ({(edition.Evidence == GameEditionEvidence.StandardSamHash ? "retail Standard.sam" : "folder structure")}): experimental slice {(level == null ? "front end (the Personnel File)" : $"for '{level}'")}. "
			+ GameEditions.SliceNotice );
	}

	/// <summary>[EXT:debug-swimmers] Builds a changing room beside the pool cells <paramref name="pool"/> (<see cref="Level.BuildChangingRoomBeside"/>) and starts <paramref name="swimmers"/> guests in its water.</summary>
	private static void PlaceDebugChangingRoom( Level level, IReadOnlyList<(int X, int Y)> pool, int swimmers, bool extras = false )
	{
		if ( level.BuildChangingRoomBeside( pool ) is not { } placed || level.Guests == null )
		{
			Log.Warning( "--debug-swimmers: no changing room could be built beside the pool (the opentpw.pools extension must be on, and the theme must list one)." );
			return;
		}
		// a closed park lets nobody swim (and sends swimmers out of the water): the capture's park door is opened as the player's button would
		level.Park?.Economy.OpenPark();
		// [EXT:debug-pool] --debug-extras: one of each pool extra beside the pool (the shower needs the room first)
		if ( extras )
			foreach ( var extra in level.BuildPoolExtrasBeside( pool ) )
				Log.Trace( FormattableString.Invariant( $"--debug-extras: built the {extra.Entry.DisplayName} at {extra.Placement.X},{extra.Placement.Y} rotation {extra.Placement.Rotation}, engine {extra.Placement.ModelToEngine.M41:0.#},{extra.Placement.ModelToEngine.M42:0.#}." ) );
		var started = level.Guests.DebugStartSwimmers( placed.Visitors, swimmers );
		if ( extras )
		{
			// the first swimmers go straight into a ladder, a board, a lounger and a shower, so a capture shows each
			var ids = new[] { "extra.ladder", "extra.board", "extra.lounger", "extra.shower", "extra.lounger", "extra.ladder" };
			var picked = level.Guests.Guests.Where( guest => guest.IsSwimming ).Take( ids.Length ).ToArray();
			for ( var index = 0; index < picked.Length; index++ )
				Log.Trace( $"--debug-extras: guest {picked[index].Id} {ids[index]}: {level.Guests.DebugStartActivity( picked[index], ids[index] )}." );
		}
		Log.Trace( $"--debug-swimmers: built the {placed.Entry.DisplayName} at {placed.Placement.X},{placed.Placement.Y} rotation {placed.Placement.Rotation}; {started} of {swimmers} guests swim." );
		if ( started == 0 )
			Log.Warning( $"--debug-swimmers: no guest swims: changing room {placed.Visitors.IsChangingRoom}, pool-side cells {string.Join( " ", placed.Visitors.PoolAccessCandidates )}, access {level.Guests.PoolAccessOf( placed.Visitors )}, region {level.Guests.SwimRegionOf( placed.Visitors )?.Cells.Count}, park closed {level.Park?.Economy.IsParkOpen == false}, registered {placed.Visitors.HasCells}." );
	}

	/// <summary>[EXT:debug-slide] Builds a slide tower of the profile beside the pool cells <paramref name="pool"/> and its slide (<see cref="Level.BuildSlideBeside"/>); the swimmers then take it like any guest.</summary>
	private static void PlaceDebugSlide( Level level, IReadOnlyList<(int X, int Y)> pool, string profile )
	{
		var kind = string.Equals( profile, "tube", StringComparison.OrdinalIgnoreCase ) ? PoolSlideProfile.Tube : PoolSlideProfile.Flume;
		if ( level.BuildSlideBeside( pool, kind ) is not { } placed )
		{
			Log.Warning( "--debug-slide: no slide could be built beside the pool (the opentpw.pools extension must be on, the theme must list the tower, and a valid route into the pool must exist)." );
			return;
		}
		var slide = level.PoolSlides?.Find( PoolSlide.KeyOf( placed.Placement.X, placed.Placement.Y ) );
		Log.Trace( $"--debug-slide: built the {placed.Entry.DisplayName} at {placed.Placement.X},{placed.Placement.Y} rotation {placed.Placement.Rotation} with a slide of {slide?.Pieces.Count} pieces (complete {slide?.IsComplete}, slope {slide?.SlopePermille})." );
		// the engine point halfway along the track and the pool's side of it, for --capture-camera
		if ( slide is { IsComplete: true } )
		{
			var origin = level.Objects.Grid.Origin;
			var mid = slide.PointAt( slide.Length / 2 );
			Log.Trace( FormattableString.Invariant( $"--debug-slide: the middle of the track is at engine {origin.X + mid.X / 1024f * ObjectPlacement.CellSize:0.#},{origin.Y + mid.Y / 1024f * ObjectPlacement.CellSize:0.#}; the splash at engine {origin.X + (slide.LandingPoint!.Value.X / 1024f) * ObjectPlacement.CellSize:0.#},{origin.Y + (slide.LandingPoint!.Value.Y / 1024f) * ObjectPlacement.CellSize:0.#}." ) );
		}
	}

	/// <summary>
	/// <c>--original-save &lt;file&gt;</c> with <c>--load-original-level</c>: the decoded payload of a player's own original park save (an <c>autosave.TPWS</c>, a named park or a <c>restart.INTS</c>) of that level, which the park starts from
	/// instead of the shipped Easymode save; null without the option.
	/// </summary>
	private static ReadOnlyMemory<byte>? ReadPlayerSave( string[] args )
	{
		if ( GetOption( args, "--original-save", "an original park save file (.TPWS or .INTS)" ) is not { } path )
			return null;
		using var reader = new SaveReader( path );
		return reader.ReadFile();
	}

	private static string? GetOption( string[] args, string name, string description )
	{
		var index = Array.IndexOf( args, name );
		if ( index < 0 )
			return null;
		if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--" ) )
			throw new ArgumentException( $"{name} requires {description}." );
		return args[index + 1];
	}

	/// <summary>
	/// <c>--advisor-say N</c>: show the original advisor in a corner viewport saying global
	/// speech clip <c>sp_NNN</c> of the selected language with LIP-driven mouth changes (see docs/LIPS.md).
	/// </summary>
	private static Advisor? CreateAdvisor( string[] args )
	{
		if ( GetOption( args, "--advisor-response", "a response ID from content/data/advisor-responses.toml" ) is { } responseText )
		{
			if ( !int.TryParse( responseText, out var responseId ) || !AdvisorResponses.Table.ContainsKey( responseId ) )
				throw new ArgumentException( $"--advisor-response {responseText} is not in {AdvisorResponses.RelativePath}." );
			var level = GetOption( args, "--load-original-level", "a level name such as 'jungle'" ) ?? "jungle";
			var responder = new Advisor();
			responder.SayResponse( responseId, level, GameLanguage.IsSelected ? GameLanguage.Current : null );
			return responder;
		}
		var index = Array.IndexOf( args, "--advisor-say" );
		if ( index < 0 )
			return null;
		if ( index + 1 >= args.Length || !int.TryParse( args[index + 1], out var clip ) || clip < Advisor.FirstClip || clip > Advisor.LastClip )
			throw new ArgumentException( $"--advisor-say requires a speech clip number from {Advisor.FirstClip} to {Advisor.LastClip}." );
		var advisor = new Advisor();
		advisor.Say( clip, GameLanguage.IsSelected ? GameLanguage.Current : null );
		return advisor;
	}

	/// <summary>Reads a loose file, or a member of a WAD archive given as <c>archive.wad!member/path</c> (case-insensitive member path).</summary>
	private static byte[] ReadFileOrWadMember( string path )
	{
		var separator = path.IndexOf( ".wad!", StringComparison.OrdinalIgnoreCase );
		if ( separator < 0 )
			return File.ReadAllBytes( path );
		using var archive = new WadArchive( path[..(separator + 4)] );
		ArchiveItem? item = archive.Root;
		foreach ( var part in path[(separator + 5)..].Split( '/', '\\' ) )
			item = (item as ArchiveDirectory)?.Children.FirstOrDefault( child => string.Equals( child.Name, part, StringComparison.OrdinalIgnoreCase ) );
		return (item as WadArchiveFile)?.GetData() ?? throw new FileNotFoundException( $"No member '{path[(separator + 5)..]}' in {path[..(separator + 4)]}." );
	}
}

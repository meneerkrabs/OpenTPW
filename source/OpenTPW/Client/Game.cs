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
			Console.WriteLine( "Read-only CPU decoding of Theme Park Inc textures: 0x24 palettes are decoded opaque (approximation COMPAT-014) and the 0x2D palette as A1R5G5B5 (approximation COMPAT-015); no comparison with the original game's rendering." );
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
			Console.WriteLine( $"Offline container: magic 0x{header.Magic:X8}; file type 0x{header.FileType:X8}; version {header.Version}; BILZ chunk {header.CompressedChunkLength} bytes; decoded {payload.Length} bytes." );
			Console.WriteLine( $"Container SHA-256: {Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( reader.buffer ) )}" );
			Console.WriteLine( $"Decoded SHA-256: {Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( payload ) )}" );
			Console.WriteLine( "Read-only container decoding only: original park payload semantics and gameplay import remain unverified." );
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

		// Register game data directory
		FileSystem = new BaseFileSystem( dataDirectory );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		FileSystem.RegisterArchiveHandler<SdtArchive>( ".sdt" );

		//
		// Select the text language: --language / --language-data, then OPENTPW_LANGUAGE /
		// OPENTPW_LANGUAGE_DATA, then the Language / LanguageDataPath settings (docs/LANGUAGES.md)
		//
		var language = GetOption( args, "--language", "a language name such as German" )
			?? Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE" ) ?? Settings.Default.Language;
		var languageData = GetOption( args, "--language-data", "a directory with the original CD's language data" )
			?? Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_DATA" ) ?? Settings.Default.LanguageDataPath;
		// The CD chosen in Game files (or --cd-data) also holds the other shipped languages: without separate
		// language data, offer those too.
		if ( string.IsNullOrWhiteSpace( languageData ) && CdLanguageOverlay( args, dataDirectory ) is { } cdLanguages )
			languageData = cdLanguages;
		GameLanguage.AvailableOverlay = string.IsNullOrWhiteSpace( languageData ) ? null : languageData;
		if ( !string.IsNullOrWhiteSpace( language ) || !string.IsNullOrWhiteSpace( languageData ) )
			GameLanguage.Current = GameLanguage.Resolve( dataDirectory, language, languageData );
		else
		{
			try { GameLanguage.Current = GameLanguage.Resolve( dataDirectory, null, null ); }
			catch ( DirectoryNotFoundException exception ) { Log.Warning( exception.Message ); }
		}
		if ( GameLanguage.IsSelected )
			Log.Trace( $"Language: {GameLanguage.Current}" );

		// Compatibility: --cd-data overlay, media diagnostics, profile/fixes, graphics preset (docs/COMPATIBILITY.md)
		CompatibilityStartup.Initialize( args, dataDirectory );

		// Official bonus objects (docs/OBJECTS.md): --bonus-data, else OPENTPW_BONUS_DATA, else the saved bonus folder, else <config>/bonus.
		var bonusSettings = SetupSettings.Load( SetupSettings.GetDefaultPath() );
		var bonusRoot = BonusContent.ResolveRoot( GetOption( args, "--bonus-data", "the extracted official bonus content directory" ),
			Environment.GetEnvironmentVariable( "OPENTPW_BONUS_DATA" ), bonusSettings.BonusPath, Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )! );
		if ( bonusRoot is { } bonus )
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

		var onlineFolders = OnlineFolders.FromEnvironment( GetOption( args, "--online-dir", "an online folder outside the original installation" ) );
		if ( OnlineCommands.Run( args, Settings.Default.GamePath, onlineFolders, Console.WriteLine ) )
			return;

		//
		// Save data beside the game files, or in the user configuration directory when the game
		// folder is read-only (a mounted CD; docs/SETUP.md)
		//
		SaveFileSystem = new BaseFileSystem( GetSaveDirectory() );

		//
		// Custom OpenTPW cache directory (mainly for editor-related stuff)
		//
		CacheFileSystem = new BaseFileSystem( $"./.opentpw" );

		//
		// Init renderer
		//
		var visitPath = GetOption( args, "--visit-park", "a local .tpwpark file" );
		var visit = visitPath == null ? null : ParkSharing.PrepareVisit( OpenTPW.Online.Packages.ParkPackage.Load( visitPath ) );
		if ( visit != null )
			Log.Trace( ParkSharing.Describe( visit ) );
		Render = DisplayStartup.CreateRenderer( args );
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
		using var flow = new GameFlow { OnlineFolders = onlineFolders, AutomaticAdvisorEnabled = !args.Contains( "--no-advisor" ) && !args.Contains( "--advisor-say" ) && !args.Contains( "--advisor-response" ) };
		var capturePath = GetOption( args, "--capture-world", "a .png file for the world-only screenshot" );
		var smoke = args.Contains( "--smoke-test" );
		var startsFrontEnd = visit == null && originalLevelIndex < 0 && !args.Contains( "--advisor-say" ) && !args.Contains( "--advisor-response" ) && !args.Contains( "--sandbox" ) && (!smoke || args.Contains( "--front-end" ));
		// [EXT:autorun] The CD's launcher window comes first when its Autorun folder is available (docs/AUTORUN.md).
		var autorun = startsFrontEnd && capturePath == null ? CreateAutorun( args, bonusSettings, dataDirectory ) : null;
		// The original plays its start-up movies before the front end on every start (docs/TGQ-MOVIES.md); after
		// the launcher's Play when it is shown. Smoke tests, --capture-world and --no-intro / OPENTPW_NO_INTRO go straight to the front end.
		var playIntro = IntroPlaylist.ShouldPlay( startsFrontEnd, smoke, args, Environment.GetEnvironmentVariable( "OPENTPW_NO_INTRO" ) );
		Func<IDisposable>? startIntro = playIntro ? () => StartIntro( flow, args, dataDirectory ) : null;
		if ( autorun == null && !playIntro )
		{
			Render.OnUpdate += flow.Update;
			Render.OnRender += flow.Render;
		}
		if ( capturePath != null )
		{
			var frames = GetOption( args, "--capture-frames", "a frame count" ) is { } text && int.TryParse( text, out var parsed ) && parsed > 0 ? parsed : 240;
			Render.PostUpdate += new WorldCapture( capturePath, frames ).Update;
		}
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
			? flow.StartLevel( args[originalLevelIndex + 1], original: true, developerPanels: true )
			: flow.StartLevel( "jungle", original: false, developerPanels: true );

		//
		// Run game loop
		//
		using var advisor = CreateAdvisor( args );
		if ( advisor != null )
			Render.OnRender += advisor.Render;
		if ( visit != null && smoke )
		{
			using var visitSmokeTest = new VisitSmokeTest( level );
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
			using var smokeTest = new SandboxSmokeTest( level, flow.Hud, flow.Context );
			Render.PostUpdate += smokeTest.Update;
			Render.Run();
			smokeTest.VerifyCompleted();
		}
		else
			Render.Run();
	}

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
	private static string[]? ResolveGameFolder( string[] args, string? commandLinePath )
	{
		var setupPath = SetupSettings.GetDefaultPath();
		var saved = SetupSettings.Load( setupPath );
		InstallationDiscoveryResult? discovery = null;
		var resolved = args.Contains( "--setup" ) ? null
			: GamePathResolution.Resolve( commandLinePath, Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" ), new( null, null ), null, () => Array.Empty<string>() );
		if ( resolved == null && !args.Contains( "--setup" ) )
		{
			foreach ( var (path, source) in new[] { (saved.GamePath, GamePathSource.Saved), (Settings.Default.GamePath, GamePathSource.Legacy) } )
			{
				if ( string.IsNullOrWhiteSpace( path ) ) continue;
				var inspected = InstallationDiscovery.InspectAsync( path ).GetAwaiter().GetResult();
				if ( inspected.Reports.FirstOrDefault() is { IsUsable: true } report )
				{
					resolved = (report.Path, source);
					break;
				}
			}
			if ( resolved == null )
			{
				discovery = InstallationDiscovery.SearchAsync().GetAwaiter().GetResult();
				if ( discovery.Reports.FirstOrDefault() is { IsUsable: true } report )
					resolved = (report.Path, GamePathSource.Detected);
			}
		}
		if ( resolved == null )
		{
			if ( !GamePathResolution.IsInteractive( args ) )
				throw new DirectoryNotFoundException( "Theme Park World data not found. Use --game-path or OPENTPW_GAME_PATH, or start OpenTPW without arguments to choose the folder in the setup window." );
			var chosen = SetupWizard.Run( saved, discovery );
			if ( chosen == null )
			{
				Log.Trace( "Setup closed without choosing a game folder." );
				return null;
			}
			saved = saved with { GamePath = chosen.GamePath, CdPath = chosen.CdPath };
			TrySave( saved, setupPath );
			resolved = (chosen.GamePath, GamePathSource.Wizard);
		}
		else if ( resolved.Value.Source is GamePathSource.Legacy or GamePathSource.Detected )
		{
			saved = saved with { GamePath = resolved.Value.Path };
			TrySave( saved, setupPath );
		}
		Settings.Default.GamePath = resolved.Value.Path;
		Log.Trace( $"Game folder: {resolved.Value.Path} ({resolved.Value.Source})." );

		// The saved CD stands in for --cd-data, unless the folder came from a developer override or a CD is given.
		if ( resolved.Value.Source is not (GamePathSource.CommandLine or GamePathSource.Environment) && saved.CdPath != null && InstallationDiscovery.InspectAsync( saved.CdPath ).GetAwaiter().GetResult().Reports.FirstOrDefault()?.DataDirectory != null
			&& !args.Contains( "--cd-data" ) && string.IsNullOrWhiteSpace( Environment.GetEnvironmentVariable( "OPENTPW_CD_DATA" ) ) )
			args = [.. args, "--cd-data", saved.CdPath];
		return args;
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

	private static string GetSaveDirectory()
	{
		var beside = Path.Combine( Settings.Default.GamePath, "save" );
		try
		{
			Directory.CreateDirectory( beside );
			return beside + Path.DirectorySeparatorChar;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			var fallback = Path.Combine( Path.GetDirectoryName( SetupSettings.GetDefaultPath() )!, "save" );
			Directory.CreateDirectory( fallback );
			Log.Warning( $"The game folder is read-only ({exception.Message}); saves go to {fallback}." );
			return fallback + Path.DirectorySeparatorChar;
		}
	}

	/// <summary><c>--build-texture-pack --upscaler &lt;realesrgan-ncnn-vulkan&gt;</c>: builds the optional local texture pack (docs/TEXTURE-PACKS.md).</summary>
	private static void BuildTexturePack( string[] args, string dataDirectory )
	{
		var upscaler = GetOption( args, "--upscaler", "the path of a realesrgan-ncnn-vulkan executable" )
			?? throw new ArgumentException( "--build-texture-pack requires --upscaler <path of realesrgan-ncnn-vulkan> (docs/TEXTURE-PACKS.md)." );
		var model = GetOption( args, "--upscale-model", "a Real-ESRGAN model name such as realesrgan-x4plus" ) ?? "realesrgan-x4plus";
		var packName = GetOption( args, "--texture-pack-name", "a pack name such as enhanced or detailed" ) ?? TexturePack.DefaultName;
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
			PrepassSprites = args.Contains( "--prepass-sprites" ),
			HeroDirectory = GetOption( args, "--texture-pack-hero-dir", "a directory of hand-made replacement PNGs" ) ?? "" };
		// Guest sprites are assembled into atlases from the sprite banks; they join the build under their pack keys.
		var sprites = subtree.Length == 0 ? GuestSpriteAtlas.LoadKids() : new List<GuestSpriteAtlas>();
		Log.Trace( $"Building texture pack from {dataDirectory}{(subtree.Length > 0 ? $"/{subtree}" : "")} into {packDirectory}{(options.Merge ? " (merging)" : "")}." );
		var manifest = TexturePackBuilder.Build( TexturePackBuilder.EnumerateGameTextures( dataDirectory, subtree )
			.Concat( sprites.Select( atlas => (atlas.PackKey, (Func<TextureData>)(() => new TextureData( atlas.Width, atlas.Height, atlas.Pixels ))) ) ), packDirectory,
			new RealEsrganUpscaler( upscaler, model ), options, message => Log.Trace( message ), interfaceUpscaler, prepass );
		Log.Trace( $"Done: {manifest.Textures} textures at {manifest.Scale}x. Choose the pack '{packName}' under Game Options -> OpenTPW -> Enhanced textures (or set \"TexturePack\": \"{packName}\" in graphics.json)." );
		if ( !string.Equals( Path.GetFullPath( packDirectory ), Path.GetFullPath( TexturePack.PackDirectory( packName ) ), StringComparison.Ordinal ) )
			Log.Warning( $"The game only loads packs under {TexturePack.PacksDirectory()}; use this one with OPENTPW_TEXTURE_PACK={Path.Combine( packDirectory, TexturePack.TexturesDirectoryName )}." );
	}

	/// <summary>The --cd-data / OPENTPW_CD_DATA folder when it adds languages to the installation, else null.</summary>
	private static string? CdLanguageOverlay( string[] args, string dataDirectory )
	{
		var cd = GetOption( args, "--cd-data", "the original CD's folder" ) ?? Environment.GetEnvironmentVariable( "OPENTPW_CD_DATA" );
		if ( string.IsNullOrWhiteSpace( cd ) || !Directory.Exists( cd ) )
			return null;
		try
		{
			return GameLanguage.FindLanguages( dataDirectory, cd ).Count > GameLanguage.FindLanguages( dataDirectory, null ).Count ? cd : null;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return null;
		}
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

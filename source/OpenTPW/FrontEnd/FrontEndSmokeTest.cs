using System.Runtime.InteropServices;
using OpenTPW.FrontEnd;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Native smoke test of the original-style UI (<c>--front-end --smoke-test</c>): the 3D lobby and
/// front-end menu render (GPU readback with BF4 text checked texel by texel), the menu is driven
/// with injected mouse clicks and keys (next island, options open/cancel, enter park, game mode) into
/// the original jungle level, the HUD renders with money/date text verified in readback, a Totem is
/// bought through the catalogue buy window and charged exactly once; a second researched object is
/// built, opened/closed and checked for overlap refusal. The All Visitors window lists seeded
/// visitors (select, wheel, sort, Enter-locate) and the All Attractions window selects, activates and
/// sells a placed fixture with its confirmation; the sandbox developer Totem is placed beside the
/// catalogue objects, listed as one more ride, activated like any row and removed through the
/// prototype removal. The window keys (V and B in English, the language's KEYBOARD.str letters otherwise) are pressed as real one-frame keyboard taps
/// (<see cref="Input.InjectKeyDown"/>) so they run through the same dispatch and gates as a keyboard.
/// The game exits to the lobby, then loads a read-only
/// visit and checks build/open/delete/save mutation boundaries (the attractions window opens through
/// the staff window there and its delete refuses at the handler), then starts jungle again in
/// Instant Action and checks its seed and mode gates. Options are never written and saves go to a temporary directory.
/// </summary>
internal sealed partial class FrontEndSmokeTest : IDisposable
{
	/// <summary>The kid sets (and heads) a park loads: the preset's limit of tp.exe 0x41A9D0 (KidSetLoad), of the eight the game ships.</summary>
	private static int ExpectedKidSets => GameEditions.IsThemeParkInc ? 8 : Math.Min( 8, KidSetLoad.LimitOf( IGraphicsSettings.Instance ) ?? 8 );

	private const int MaximumFrames = 3000;
	private readonly GameFlow flow;
	private readonly BaseFileSystem originalSaveFileSystem;
	private readonly OnlineFolders? originalOnlineFolders;
	private ParkVisitInfo? readOnlyVisit;
	private (ulong Hash, int Entities, int Objects, string GateSign) builtInOneGo, builtOverFrames;
	private int framesBuilding;
	// [EXT:park-entry-preparation] the HUD a build step made, the HUDs' state as each park was entered, and whether the sky was loaded before the first draw
	private ParkHud? preparedHud, droppedHud;
	private string? hudInOneGo, hudOverFrames, hudInOneGoUpdated, hudOverFramesUpdated;
	private bool skyBeforeFirstDraw;
	// runs once at the end of the next frame, after its update (the HUD's first update when it is set as a park is entered)
	private Action? afterNextUpdate;
	private (List<Entity> Entities, int Count, int Materials) lobbyBeforeAbandon;

	/// <summary>The SHA-256 of the park gate sign's two halves (composed in build steps over frames), or "none".</summary>
	private static string GateSignDigest( Level level ) => level.GateSign?.Images is { } images
		? Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( images.Left.Concat( images.Right ).ToArray() ) )[..16] : "none";

	private void RecordBuiltOverFrames()
	{
		if ( flow.Level is { } level )
		{
			builtOverFrames = (level.ComputeStateHash(), Entity.All.Count, level.Objects.Objects.Count, GateSignDigest( level ));
			hudOverFrames = flow.Hud is { } hud ? HudState( hud ) : null;
			skyBeforeFirstDraw = level.SkyLoaded;
			// Transitioned runs inside the entering frame's update, before the HUD's first update; this frame's end comes after it
			afterNextUpdate = () => hudOverFramesUpdated = flow.Hud is { } updated ? HudState( updated ) : null;
		}
	}

	/// <summary>What a HUD shows and holds as a park is entered, before its first update: its screens, its main screen's elements and their state, its messages and modes.</summary>
	private static string HudState( ParkHud hud )
	{
		const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		object? Field( string name ) => typeof( ParkHud ).GetField( name, Private )!.GetValue( hud );
		// the host's connections: which are set (an online entry is null while the extension is off, Restart Park without a restart snapshot)
		var host = (HudHost?)Field( "host" );
		var connections = host == null ? "no host" : string.Join( ",", typeof( HudHost ).GetProperties().Select( property => $"{property.Name}:{property.GetValue( host ) != null}" ) );
		return string.Join( "|", new[]
		{
			string.Join( ",", hud.Stack.Screens.Select( screen => screen.Name ) ),
			string.Join( ",", hud.Screen.Elements.Select( element => $"{element.Id}:{element.Visible}:{element.Enabled}:{element.Bounds}" ) ),
			string.Join( ",", hud.Messages ), $"{hud.Paused} {hud.SimulationTimeScale} {hud.InfoArmOpen} {hud.CamcorderArmed} {hud.Category} {hud.BuySort} {hud.BuySortDescending}",
			$"{Field( "challengeUserPaused" )} {Field( "lastLevelMessage" )}", connections,
		} );
	}
	private OriginalObject? visitedObject;
	private bool visitedOpen;
	private int visitedCount;
	private readonly string temporaryDirectory = Path.Combine( Path.GetTempPath(), $"opentpw-frontend-smoke-{Guid.NewGuid():N}" );
	private readonly Queue<(string Name, Func<bool> Step)> steps = new();
	private int frame;
	private int waitUntil;
	private bool completed;
	private string? startLanguage;
	private long builtCost;
	private bool totemLockedAtStart;
	private long soldFor;
	private int objectsBeforePurchase;
	private BuildItem TotemItem => flow.Hud!.Catalog.GetItems( BuildCategory.Rides ).Single( item => item.InfoId == PrototypeRide.InfoId );
	private OriginalObject? totem;
	private OriginalObject? secondObject;
	private BuildItem? secondItem;
	private bool queueToolAfterSecond;
	private string[] visitBaselineFiles = Array.Empty<string>();
	private int purchaseEvents;
	private int smokeStaffId;
	private Guest? locatedVisitor;
	private OriginalObject? thirdObject;
	private int thirdAttractionId;
	private int visitAttractionRows;
	private string[] visitRowNames = Array.Empty<string>();
	private int ridesBeforePrototype;
	private int objectsBeforePrototype;
	private int prototypeAttractionId;
	private RideVisitorBridge? prototypeVisitors;
	private PrototypeRide? reviewedPrototype;

	public FrontEndSmokeTest( GameFlow flow )
	{
		this.flow = flow;
		flow.PersistSettings = false;
		originalSaveFileSystem = SaveFileSystem;
		Directory.CreateDirectory( temporaryDirectory );
		SaveFileSystem = new BaseFileSystem( temporaryDirectory );
		flow.OptionsPath = Path.Combine( temporaryDirectory, GameOptions.FileName );
		originalOnlineFolders = flow.OnlineFolders;
		flow.OnlineFolders = new OnlineFolders( Path.Combine( temporaryDirectory, "online" ) );
		// Read back the composed output (world after scaling + UI at drawable pixels).
		global::Global.Render.CaptureOutput = true;
		WeatherForecastPresenter.BannerHidden = true;
		Plan();
	}

	private UiContext Context => flow.Context;

	private readonly string smokePacks = Path.Combine( Path.GetTempPath(), $"opentpw-frontend-smoke-packs-{Guid.NewGuid():N}" );
	private string switchPath = "";
	private (uint Width, uint Height, uint Pixel) switchOriginal;
	private TexturePackSwitch? textureSwitch;
	private Asset[] firstParkHeadAssets = Array.Empty<Asset>();
	/// <summary>Materials and shaders alive with the lobby up before the first park (<see cref="Asset.All"/>); -1 until taken.</summary>
	private int lobbyMaterialsBeforePark = -1;
	private static string ShaderCounts() => string.Join( ", ", Asset.All.OfType<Shader>().GroupBy( shader => System.IO.Path.GetFileName( shader.Path ) ).OrderBy( group => group.Key ).Select( group => $"{group.Key} {group.Count()}" ) );
	private string lobbyShadersBeforePark = "";
	private int lobbyTestShadersBeforePark = -1;
	private static int TestShaderCount() => Asset.All.OfType<Shader>().Count( shader => System.IO.Path.GetFileName( shader.Path ) == "test.shader" );
	/// <summary>
	/// The textures alive that were not loaded by path (made from bytes or for a render target), counted by size. The textures kept for the session
	/// on purpose are left out: the type signs of <see cref="ObjectSigns"/>, the bonus-content textures of <see cref="OriginalObject"/>,
	/// <see cref="Texture.Missing"/> and <see cref="Texture.SignBoard"/>. By size rather than in total, so a texture left behind and an unrelated
	/// one released cannot cancel out; the lobby makes its sign textures anew on every return, so the textures themselves differ.
	/// </summary>
	private static Dictionary<string, int> PathlessTextures()
	{
		var kept = ObjectSigns.SessionTextures().Concat( OriginalObject.SessionTextures() ).Append( Texture.Missing ).Append( Texture.SignBoard ).ToHashSet();
		// the lobby's own screen-space particle host (the key panel's effect 0x61, LobbyKeyPanelEffect.CreateHost) owns an atlas while the lobby is up, as the lobby with keys does after a park; the entity's deletion releases it
		return Asset.All.OfType<Texture>().Where( texture => string.IsNullOrEmpty( texture.Path ) && !kept.Contains( texture ) && texture.Origin?.Contains( "CreateScreenRenderer", StringComparison.Ordinal ) != true )
			.GroupBy( texture => $"{texture.Width}x{texture.Height}" ).ToDictionary( group => group.Key, group => group.Count() );
	}

	/// <summary>The sizes whose count differs from <paramref name="before"/>, with where the textures of those sizes alive now were made (empty when none differs).</summary>
	private static string PathlessTextureChanges( Dictionary<string, int> before )
	{
		var now = PathlessTextures();
		var changed = before.Keys.Union( now.Keys ).Where( size => before.GetValueOrDefault( size ) != now.GetValueOrDefault( size ) ).OrderBy( size => size ).ToList();
		return string.Join( "; ", changed.Select( size => $"{size}: {before.GetValueOrDefault( size )} -> {now.GetValueOrDefault( size )} (" + string.Join( ", ",
			Asset.All.OfType<Texture>().Where( texture => string.IsNullOrEmpty( texture.Path ) && $"{texture.Width}x{texture.Height}" == size ).Select( texture => texture.Origin ?? "?" ) ) + ")" ) );
	}

	private Dictionary<string, int> lobbyPathlessTexturesBeforePark = new();
	/// <summary>The textures the screen-space particle renderers own (each made by CreateScreenRenderer: the enter-park effect's, the key panel's host while the lobby holds it), not counting the live key panel host's.</summary>
	private int ScreenRendererTexturesBesidesTheKeyHost()
	{
		var hosts = Asset.All.OfType<Texture>().Count( texture => texture.Origin?.Contains( "CreateScreenRenderer", StringComparison.Ordinal ) == true );
		return hosts - (flow.Menu?.KeyHostLive == true ? 1 : 0);
	}
	private int screenRendererTexturesAtFirstReturn = -1;
	private Asset[] secondParkHeadAssets = Array.Empty<Asset>();
	private Asset[] firstParkGuestAssets = Array.Empty<Asset>();
	private Asset[] secondParkGuestAssets = Array.Empty<Asset>();

	/// <summary>
	/// The lobby's four sign models (Jun_isle, Hal_isle, Fan_gate, Spa_gate) have composed their theme gate's lines within the frames waited, and every one of their
	/// sign slots is bound to that runtime sign: not the plain board, never the shipped "SIGN1 SIGN2" art.
	/// </summary>
	private static void RequireLobbySigns( LobbyScene lobby )
	{
		Require( lobby.PendingSignCount == 0, $"the lobby has composed its signs ({lobby.PendingSignCount} pending)" );
		Require( lobby.SignModels.Count == 4, $"the lobby has its four sign models ({string.Join( ", ", lobby.SignModels )})" );
		foreach ( var model in lobby.SignModels )
		{
			var sources = lobby.SignSlotSources( model );
			Require( sources.Count > 0 && sources.All( source => source == SignSlotSource.RuntimeSign ), $"{model}'s sign slots show its runtime sign ({string.Join( ", ", sources )})" );
		}
	}

	private static bool RendererAssetsReleased( IEnumerable<Asset> assets ) => assets.All( asset => !Asset.All.Contains( asset ) && asset switch
	{
		Texture texture => texture.NativeTexture.IsDisposed && AtlasViewReleased( texture ),
		Material material => material.Pipeline.IsDisposed,
		// a shader gives its reference to the shared native program back (ShaderCompiler.Release); the program goes with its last reference
		Shader shader => !shader.HoldsNativeProgram,
		_ => false
	} );

	private static bool AtlasViewReleased( Texture texture )
	{
		var view = texture.NativeTextureView;
		// Veldrid 4.9.0 MTLTextureView.Dispose does nothing for a full alias: no separate Metal object is owned,
		// so IsDisposed stays false. Accept that exact alias only after its target texture has been disposed.
		// https://github.com/veldrid/veldrid/blob/v4.9.0/src/Veldrid/MTL/MTLTextureView.cs#L15
		return view.IsDisposed || (Device.BackendType == GraphicsBackend.Metal && texture.NativeTexture.IsDisposed
			&& view.Target == texture.NativeTexture && view.BaseMipLevel == 0 && view.MipLevels == view.Target.MipLevels
			&& view.BaseArrayLayer == 0 && view.ArrayLayers == view.Target.ArrayLayers && view.Format == view.Target.Format);
	}

	/// <summary>Real pack-change notifications replace live park head atlases without retaining the previous GPU copies.</summary>
	private void PlanParkHeadTextureSwitch()
	{
		if ( !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" ) ) )
			return;
		foreach ( var name in new[] { "smoke", "" } )
		{
			Asset[] before = Array.Empty<Asset>();
			Asset[] guestsBefore = Array.Empty<Asset>();
			byte[][] beforePixels = Array.Empty<byte[]>();
			Do( $"park head pack '{name}' begins", () =>
			{
				before = flow.Level!.RideHeadRenderer!.OwnedAssets.ToArray();
				guestsBefore = flow.Level.GuestRenderer!.OwnedAssets.ToArray();
				Require( guestsBefore.Length == ExpectedKidSets + 2, "the park owns its kid-set atlas textures (the preset's limit, KidSetLoad), its material and shader before switching" );
				beforePixels = before.OfType<Texture>().Select( texture => texture.ReadPixels() ).ToArray();
				Require( before.OfType<Texture>().Count() == ExpectedKidSets, "the park owns its head atlases (the preset's limit) before switching" );
				textureSwitch = TexturePackSwitch.Begin( name, new List<string>(), smokePacks );
				Require( textureSwitch != null, "the live park texture switch starts" );
			} );
			WaitForLoad( $"park head pack '{name}' completes", () => { textureSwitch!.Pump(); return textureSwitch.Finished; } );
			Wait( $"park head pack '{name}' deletion frames", 3 );
			Do( $"park head pack '{name}' releases old atlases", () =>
			{
				var after = flow.Level!.RideHeadRenderer!.OwnedAssets.ToArray();
				Require( before.Length == after.Length && before.OfType<Material>().Single() == after.OfType<Material>().Single(),
					"a head atlas reload keeps the owned asset count and material stable" );
				Device.WaitForIdle();
				Require( RendererAssetsReleased( before.OfType<Texture>() ), "the replaced head atlases leave Asset.All and release their native resources" );
				Require( after.All( Asset.All.Contains ), "replacement head atlases and material remain live" );
				var afterPixels = after.OfType<Texture>().Select( texture => texture.ReadPixels() ).ToArray();
				Require( beforePixels.Zip( afterPixels ).All( pair => pair.First.AsSpan().SequenceEqual( pair.Second ) ),
					"a pack without head replacements reloads the same head GPU pixels" );
				firstParkHeadAssets = after;
				var guestsAfter = flow.Level.GuestRenderer!.OwnedAssets.ToArray();
				Require( guestsBefore.Length == guestsAfter.Length && guestsBefore.OfType<Material>().Single() == guestsAfter.OfType<Material>().Single(),
					"a guest atlas reload keeps the owned asset count and material stable" );
				Require( RendererAssetsReleased( guestsBefore.OfType<Texture>() ), "the replaced guest atlases leave Asset.All and release their native resources" );
				Require( guestsAfter.All( Asset.All.Contains ), "replacement guest atlases and material remain live" );
				firstParkGuestAssets = guestsAfter;
				Log.Trace( $"Guest lifecycle: pack '{name}' released eight old atlases; {guestsAfter.Length} owned assets live." );
				Log.Trace( $"Ride head lifecycle: pack '{name}' released eight old atlases; {after.Length} owned assets live, head GPU pixels retained." );
			} );
		}
	}


	private static uint CenterPixel( Texture texture )
	{
		var pixels = texture.ReadPixels();
		return BitConverter.ToUInt32( pixels, (int)((texture.Height / 2 * texture.Width + texture.Width / 2) * 4) );
	}

	/// <summary>
	/// [EXT:texture-pack] Switches a throw-away pack on and off while the lobby runs: a lobby texture becomes the pack's solid
	/// 96x96 image (checked by GPU readback), then the original comes back; frames keep rendering in between.
	/// </summary>
	private void PlanTexturePackSwitch()
	{
		// A pack pinned with OPENTPW_TEXTURE_PACK (e.g. to capture screens with a pack) cannot be switched.
		if ( !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" ) ) )
		{
			Log.Trace( "Front-end smoke test: texture pack switch skipped, a pack is pinned with OPENTPW_TEXTURE_PACK." );
			return;
		}
		const uint SolidPixel = 0xFF22C80C; // little-endian RGBA bytes 0C C8 22 FF
		// Outside temporaryDirectory: a later step checks that no .json file is written there.
		var packs = smokePacks;
		Do( "texture pack on", () =>
		{
			var target = Texture.ReloadTargets().FirstOrDefault( entry => entry.Path.Contains( "lobby", StringComparison.OrdinalIgnoreCase ) );
			Require( target.Path != null, "the lobby loaded textures from .wct files" );
			switchPath = target.Path;
			var texture = Texture.FindLoaded( switchPath )!;
			switchOriginal = (texture.Width, texture.Height, CenterPixel( texture ));
			Require( switchOriginal.Width != 96 && switchOriginal.Pixel != SolidPixel, "the original lobby texture differs from the pack's" );
			var pack = Path.Combine( packs, "smoke" );
			var file = Path.Combine( pack, TexturePack.TexturesDirectoryName, TexturePack.RelativeFileName( switchPath ) );
			Directory.CreateDirectory( Path.GetDirectoryName( file )! );
			var solid = new byte[96 * 96 * 4];
			for ( var index = 0; index < solid.Length; index += 4 )
				(solid[index], solid[index + 1], solid[index + 2], solid[index + 3]) = (0x0C, 0xC8, 0x22, 0xFF);
			using ( var stream = File.Create( file ) )
				PngWriter.Write( stream, solid, 96, 96 );
			File.WriteAllText( Path.Combine( pack, TexturePack.ManifestFileName ), System.Text.Json.JsonSerializer.Serialize( new TexturePackManifest { Scale = 1 } ) );
			var diagnostics = new List<string>();
			textureSwitch = TexturePackSwitch.Begin( "smoke", diagnostics, packs );
			Require( textureSwitch != null && diagnostics.Count == 0 && textureSwitch.Total > 0, "the pack switch starts with the loaded textures as its work" );
		} );
		// The switch runs in the background with a progress line at the top; capture it while it is still running.
		Wait( "texture switch progress frames", 2 );
		Do( "texture switch progress", () => { if ( !textureSwitch!.Finished ) CaptureFrame( "texture-switch-progress.png" ); } );
		WaitForLoad( "texture pack loads", () => { textureSwitch!.Pump(); return textureSwitch.Finished; } );
		Wait( "texture pack frames", 3 );
		Do( "texture pack on verified", () =>
		{
			var texture = Texture.FindLoaded( switchPath )!;
			Require( (texture.Width, texture.Height) == (96, 96), "the live texture now has the pack's size" );
			Require( CenterPixel( texture ) == SolidPixel, "GPU readback shows the pack's pixels" );
			CaptureFrame( "texture-pack-on.png" );
			var diagnostics = new List<string>();
			textureSwitch = TexturePackSwitch.Begin( "", diagnostics, packs );
			Require( textureSwitch != null, "switching back starts" );
		} );
		WaitForLoad( "texture pack unloads", () => { textureSwitch!.Pump(); return textureSwitch.Finished; } );
		Wait( "texture pack off frames", 3 );
		Do( "texture pack off verified", () =>
		{
			var texture = Texture.FindLoaded( switchPath )!;
			Require( (texture.Width, texture.Height, CenterPixel( texture )) == switchOriginal, "the original pixels came back" );
			CaptureFrame( "texture-pack-off.png" );
		} );
		// Optional timing with a real pack (read only): OPENTPW_SMOKE_PACKS_DIR=<texture-packs dir> OPENTPW_SMOKE_PACK=<name>.
		var realPacks = Environment.GetEnvironmentVariable( "OPENTPW_SMOKE_PACKS_DIR" );
		var realPack = Environment.GetEnvironmentVariable( "OPENTPW_SMOKE_PACK" );
		if ( !string.IsNullOrEmpty( realPacks ) && !string.IsNullOrEmpty( realPack ) )
		{
			var timer = new System.Diagnostics.Stopwatch();
			long memoryBefore = 0;
			var frames = 0;
			foreach ( var (name, pack) in new[] { ("on", realPack), ("off", "") } )
			{
				Do( $"real pack {name}", () =>
				{
					memoryBefore = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
					frames = frame;
					timer.Restart();
					textureSwitch = TexturePackSwitch.Begin( pack, new List<string>(), realPacks );
					Require( textureSwitch != null, "the real pack switch starts" );
				} );
				WaitForLoad( $"real pack {name} loads", () => { textureSwitch!.Pump(); return textureSwitch.Finished; } );
				Do( $"real pack {name} done", () => Log.Trace( $"Texture pack switch to '{(pack.Length == 0 ? "original" : pack)}': {textureSwitch!.Total} textures in {timer.Elapsed.TotalSeconds:F1} s over {frame - frames} frames; working set {memoryBefore / 1048576} -> {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576} MB." ) );
			}
		}
	}

	private void Plan()
	{
		// the online extension is read once at startup (docs/MODS.md)
		var online = FeatureGates.Online;
		Wait( "lobby settles", 30 );
		PlanMaterialRendering();
		Do( "lobby renders", () =>
		{
			Require( flow.Lobby != null && flow.Lobby.IslandCount == 4 && flow.Lobby.PartCount > 4, "lobby scene has four islands and terrain" );
			Require( flow.Menu != null && flow.Menu.Islands.Count == 4, "front-end menu lists four theme islands" );
			// The jungle's butterflies (10 + 10) and the Halloween bats (50) spawn within the first frames.
			Require( flow.Lobby!.FlyingMeshes is { } flyers && flyers.Groups.Count == 3 && flyers.Groups.Sum( group => group.Instances.Count ) == 70,
				"the lobby flies its 70 bats and butterflies" );
			Require( flow.Lobby.IslandClips.All( clip => clip is 1 or 2 ), "every island model plays its M1 or M2 clip" );
			// the sea plays Basem1 (AnimID 5) on a loop, so its texture coordinates move off the stored ones after the settle frames
			Require( flow.Lobby.TerrainAnimator is { } sea && sea.IsChannelPlaying( 0 ) && sea.GetAnimationId( 0 ) == 5 && sea.GetTick( 0 ) > 0 && Enumerable.Range( 0, flow.Lobby.TerrainMeshCount ).Count( mesh => sea.GetTexCoords( mesh ) != null ) == 2, "the lobby sea plays its Basem1 clip on its two sea meshes" );
			RequireLobbySigns( flow.Lobby );
			var capture = CaptureFrame( "frontend.png" );
			// [BIN:TPW-EXE:0x757f60 lobby panel] the lobby has no title and no menu column: the panel, its four buttons and the island name; Escape opens the menu
			Require( flow.Menu!.Main.Find( "logo" ) == null && flow.Menu.Main.Find( "title" ) == null && flow.Menu.Main.Find( "quit" ) == null, "the lobby has no logo, title or menu column" );
			VerifyText( capture, flow.Menu!.SelectedName, "island name" );
			flow.Menu.ShowMenu();
		} );
		Wait( "lobby menu renders", 3 );
		Do( "lobby menu", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "lobbyMenu", "Escape opens the lobby menu" );
			var capture = CaptureFrame( "lobby-menu.png" );
			VerifyText( capture, flow.Strings[UIStrings.QuitGame], "Quit Game line" );
			VerifyText( capture, flow.Strings[UIStrings.Options], "Options line" );
			flow.Menu.Stack.Pop();
		} );
		// [BIN:TPW-EXE:0x5e0490 enter sequence] the lobby's enter turns the camera, opens the selected island's gate with M1 and zooms in; cancelled here before it reaches the park
		Do( "lobby enter", () =>
		{
			var camera = (LobbyCameraMode)Camera.Mode;
			Require( flow.Lobby!.GateClips.All( clip => clip == 0 ), "the gates start closed" );
			flow.BeginLobbyEnter( flow.Menu!.Selected.Level );
			Require( camera.Entering && flow.LobbyEntering, "the enter sequence starts" );
		} );
		var enterFrames = 0;
		steps.Enqueue( ("lobby enter turns", () =>
		{
			Require( ++enterFrames < 2000, "the enter's turn finishes" );
			return ((LobbyCameraMode)Camera.Mode).Phase == LobbyCameraMode.EnterPhase.Zoom;
		}) );
		Do( "lobby enter zooms", () =>
		{
			var camera = (LobbyCameraMode)Camera.Mode;
			Require( flow.Lobby!.GateClips.Any( clip => clip == 1 ), "the selected island's gate plays its M1 clip" );
			flow.CancelLobbyEnter();
			Require( !flow.LobbyEntering, "a cancelled enter resumes the lobby" );
			Require( flow.Lobby!.GateClips.Any( clip => clip == 2 ), "a cancelled enter closes the opened gate with M2" );
		} );
		Wait( "lobby fade clears", 60 );
		// [BIN:TPW-EXE:0x4b8fd0 enter park] the enter-park routine starts the screen-space particle effect 0x62 over the key panel; the smoke starts it directly (the park start would tear the lobby down)
		Do( "enter-park effect", () => flow.StartEnterParkEffect() );
		var effectFrames = 0;
		steps.Enqueue( ("enter-park effect draws", () =>
		{
			Require( ++effectFrames < 200, "the enter-park effect draws screen-space particles over the lobby" );
			return flow.EnterParkEffectDraws > 0;
		}) );
		Wait( "enter-park effect develops", 20 );
		Do( "enter-park effect shown", () =>
		{
			Require( flow.EnterParkEffectDraws > 0, "the enter-park effect is still drawing a few frames in" );
			CaptureFrame( "lobby-enter-effect.png" );
		} );
		// [BIN:TPW-EXE:0x5D5F80 queue step] the lobby advisor says what its queue holds; the smoke queues the idle line directly (the real one comes after 90 s of silence) and the model shows while it plays
		Do( "lobby advisor line", () => flow.LobbyAdvisorHost?.Queue.Push( LobbyAdvisorQueue.IdleLineEven, flush: true ) );
		var advisorFrames = 0;
		steps.Enqueue( ("lobby advisor shows", () =>
		{
			if ( flow.LobbyAdvisorHost == null )
				return true;
			Require( ++advisorFrames < 600, "the lobby advisor's line shows the advisor over the lobby" );
			return flow.LobbyAdvisorHost.Image != null;
		}) );
		Wait( "lobby advisor develops", 90 );
		Do( "lobby advisor shown", () =>
		{
			if ( flow.LobbyAdvisorHost != null )
			{
				Require( flow.LobbyAdvisorHost.Image != null, "the advisor is still shown a second and a half into its line" );
				CaptureFrame( "lobby-advisor.png" );
			}
		} );
		Do( "click next island", () => Click( flow.Menu!.Main, "nextIsland" ) );
		Wait( "island changes", 3 );
		Do( "island selected", () =>
		{
			Require( flow.Menu!.SelectedIndex == 1, "mouse click on the next-island button selects island 1" );
			flow.InjectedInput = UiInput.Key( UiKeys.Left );
		} );
		Wait( "keyboard previous island", 3 );
		Do( "jungle selected", () => Require( flow.Menu!.Selected.Level == "jungle", "left key returns to the jungle island" ) );
		// With opentpw.online disabled (--disable-extension opentpw.online) the lobby has no globe button and the online screens are skipped.
		if ( !online )
			Do( "no online entries", () => Require( flow.Menu!.Main.Find( "worldView" ) == null, "the disabled online extension hides the lobby's globe button" ) );
		else
		{
			// Original-style online screens (docs/ONLINE.md): Go Online, then each screen, captured offline.
			Do( "go online", () => Click( flow.Menu!.Main, "worldView" ) );
			Wait( "online world opens", 3 );
			Do( "online world renders", () =>
			{
				Require( flow.Menu!.Stack.Top?.Name == "onlineWorld", "Go Online opens the online world" );
				Require( flow.Globe is { Visible: true } && flow.Globe.PartCount > 0 && flow.Lobby is { Visible: false }, "the online world shows the globe instead of the lobby" );
				// the original's city labels stay empty until a city is hovered (tp.exe 0x4B9980), so the screen has no title text to read back
				CaptureFrame( "online-world.png" );
				// Offline there is no server city list; pins for a few cities exercise the flags and the picking.
				flow.Globe.SetCities( new[]
				{
					new Online.Api.CityInfo( 1, GlobeScene.BullfrogCity, "", 90, 0, 3 ), new Online.Api.CityInfo( 2, "London", "", 51.5f, -0.1f, 40 ),
					new Online.Api.CityInfo( 3, "Timbuktu", "", 16.8f, -3, 5 ), new Online.Api.CityInfo( 4, "New York", "", 40.7f, -74, 120 ),
				} );
			} );
			Wait( "globe pins render", 3 );
			Do( "globe pins", () =>
			{
				var london = Camera.Position.GetSystemVector3();
				var target = GlobeScene.Centre.GetSystemVector3() + new System.Numerics.Vector3( GlobeScene.Direction( 51.5f, -0.1f ).X, GlobeScene.Direction( 51.5f, -0.1f ).Z, GlobeScene.Direction( 51.5f, -0.1f ).Y ) * GlobeScene.PickRadius;
				Require( flow.Globe!.Pick( london, System.Numerics.Vector3.Normalize( target - london ) )?.Name == "London", "a ray from the camera to London picks London" );
				CaptureFrame( "online-globe-cities.png" );
				// A city lobby with three parks: their theme islands stand at the first three slots.
				var city = new UI.CityLobbyScreen( new Online.Api.CityInfo( 2, "London", "", 51.5f, -0.1f, 3 ) );
				Online.Api.ParkSummary Park( string id, string level ) => new( id, $"Park {id}", "", "Ann", level, "English", DateTimeOffset.UnixEpoch, 0, 0, false, false, 0, false, 0, 2 );
				city.Parks = new[] { Park( "a", "jungle" ), Park( "b", "space" ), Park( "c", "hallow" ) };
				flow.Menu!.Stack.Push( city );
			} );
			Wait( "city lobby renders", 3 );
			Do( "city lobby", () =>
			{
				Require( flow.CityIslandCount == 3 && flow.Globe is { Visible: false }, "a city lobby shows its parks' theme islands instead of the globe" );
				CaptureFrame( "online-city-lobby.png" );
				flow.Menu!.Stack.Pop();
				Click( flow.Menu.Stack.Top!, "login" );
			} );
			Wait( "login opens", 3 );
			Do( "login typing", () =>
			{
				Require( flow.Menu!.Stack.Top?.Name == "onlineLogin", "the login button opens the login dialog" );
				flow.InjectedInput = UiInput.Type( "Alice" );
			} );
			Wait( "login renders", 3 );
			Do( "login capture", () =>
			{
				var field = flow.Menu!.Stack.Top!.Find( "name" ) as UiTextField;
				Require( field != null && field.Text == "Alice", "typing reaches the focused login name field" );
				VerifyText( CaptureFrame( "online-login.png" ), OnlineStrings.Ui( OnlineStrings.LoginName, "Login name" ), "login name label" );
				var login = flow.Menu.Stack.Top!;
				var loginWindow = login.Find( "window" )!.Bounds;
				foreach ( var id in new[] { "name", "password", "login", "close", } )
					Require( login.Find( id ) is { } control && control.Bounds.X >= loginWindow.X && control.Bounds.Right <= loginWindow.Right && control.Bounds.Y >= loginWindow.Y && control.Bounds.Bottom <= loginWindow.Bottom, $"the login dialog's {id} control lies inside its window" );
				var loginPanel = login.Find( "ownWindow" )!.Bounds;
				foreach ( var id in new[] { "server", "code", "register", "forgot", "status" } )
					Require( login.Find( id ) is { } control && control.Bounds.X >= loginPanel.X && control.Bounds.Right <= loginPanel.Right && control.Bounds.Y >= loginPanel.Y && control.Bounds.Bottom <= loginPanel.Bottom, $"the login dialog's {id} control lies inside its own panel" );
				Require( ( (UiButton)login.Find( "login" )! ).Model == "b_okay" && ( (UiButton)login.Find( "close" )! ).Model == "b_exit", "the login dialog's check and cross draw the original b_okay and b_exit models" );
			} );
			// a refused login (no server address) shows its reason in the panel's status line
			Do( "login refused", () =>
			{
				( (UiTextField)flow.Menu!.Stack.Top!.Find( "server" )! ).Text = "";
				Click( flow.Menu.Stack.Top!, "login" );
			} );
			Wait( "login status renders", 3 );
			Do( "login status capture", () =>
			{
				Require( flow.Menu!.Stack.Top?.Name == "onlineLogin", "a refused login keeps the dialog open" );
				CaptureFrame( "online-login-status.png" );
				flow.Menu.Stack.Pop();
			} );
			var onlineShots = new (string Name, Action<UI.OnlineScreens> Show, string Screen, string? Verify)[]
			{
				("online-news.png", screens => screens.ShowNews(), "onlineNews", null),
				("online-find-parks.png", screens => screens.ShowFindParks(), "findParks", null),
				("online-send-postcard.png", screens => screens.ShowSendPostcard(), "sendPostcard", null),
				("online-outbox.png", screens => screens.ShowOutbox(), "outbox", null),
				("online-inbox.png", screens => screens.ShowInbox(), "inbox", null),
				("online-chat.png", screens => screens.ShowChat(), "chat",
					// The original chat overlay (tp.exe table 0x758AE8) over the chat park view, which answers a click with a walk (UIHELPTEXT 401).
					flow.Strings.Help( 401 )),
				// A satellite's chat park in the park details' short variant (tp.exe 0x4C0AE0, a city with the flag +0x60): name, theme row, description, Enter.
				("online-chat-park.png", screens => screens.ShowChatParkDetails( new Online.Api.ChatParkScene( 1, "The Meeting Place",
					"The place to meet other players: walk around the park together and chat.", "jungle", null ) ), UI.OnlineScreens.ChatParkDetailsScreenName,
					UI.OnlineScreens.ChatParkThemeRow( "jungle" )),
				("online-import.png", screens => screens.ShowImport(), "import", null),
				// A fixed example secret; the capture is checked by decoding its QR code (docs/ONLINE.md, "Two-step login").
				("online-two-step.png", screens =>
				{
					flow.Online.Enrollment = new TotpEnrollment( "Alice", "smoke-ticket", "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP",
						Totp.Uri( "OpenTPW", "Alice", "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP" ), DateTimeOffset.UtcNow.AddMinutes( 10 ) );
					screens.ShowEnrollment();
				}, "twoStepSetup", null),
				("online-two-step-email.png", screens =>
				{
					flow.Online.Enrollment = new TotpEnrollment( "Alice", "smoke-ticket", "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP",
						Totp.Uri( "OpenTPW", "Alice", "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP" ), DateTimeOffset.UtcNow.AddMinutes( 10 ), EmailRequired: true );
					screens.ShowEnrollment();
				}, "twoStepSetup", null),
				("online-recovery.png", screens => screens.ShowRecovery( "Alice" ), "recovery", null),
			};
			foreach ( var shot in onlineShots )
			{
				Do( $"open {shot.Screen}", () => shot.Show( new UI.OnlineScreens( flow.Menu!.Stack, flow.Strings, Context.Models, new UI.OnlineHost { Session = flow.Online } ) ) );
				Wait( $"{shot.Screen} renders", 3 );
				Do( $"{shot.Screen} capture", () =>
				{
					Require( flow.Menu!.Stack.Top?.Name == shot.Screen, $"{shot.Screen} opens" );
					var frame = CaptureFrame( shot.Name );
					// the Back cross covers no other control of its screen
					if ( flow.Menu.Stack.Top!.Find( "back" ) is { } back )
						foreach ( var other in flow.Menu.Stack.Top.Elements.Where( element => element != back && element.Visible && element is UiButton or UiTextField or UiScrollList ) )
							Require( !( back.Bounds.X < other.Bounds.Right && other.Bounds.X < back.Bounds.Right && back.Bounds.Y < other.Bounds.Bottom && other.Bounds.Y < back.Bounds.Bottom ), $"{shot.Screen}: the back button does not cover {other.Id}" );
					if ( shot.Verify != null )
						VerifyText( frame, shot.Verify, $"{shot.Screen} text" );
					flow.Online.Enrollment = null;
					flow.Menu.Stack.Pop();
				} );
			}
			// [EXT:ONLINE-UI] the online menu with a postcard waiting: Publish park is greyed (no park is open) and says why while the pointer rests on it, and the postcard line sits under the last button
			Do( "queue a postcard", () => flow.Online.Folders.Store( flow.Online.Folders.Outbox, OpenTPW.Online.Packages.Postcard.Create( "Alice", new[] { "Bob" }, "Hello", "Smoke", GameLanguage.Current.Name ) ) );
			Do( "open online menu", () => new UI.OnlineScreens( flow.Menu!.Stack, flow.Strings, Context.Models, new UI.OnlineHost { Session = flow.Online } ).ShowMenu() );
			Wait( "online menu renders", 3 );
			// the pointer rests on the greyed line: the help is drawn in the frame after the one that carried the pointer, so the pointer is put there again until the help is on screen
			var hoverFrames = 0;
			steps.Enqueue( ("hover Publish park", () =>
			{
				var menu = flow.Menu!.Stack.Top!;
				Require( menu.Name == "onlineMenu", "the online menu opens" );
				Require( menu.Find( "publish" ) is { Enabled: false }, "Publish park is greyed with no park open" );
				if ( Context.Batch.TextDraws.Any( entry => entry.Text == OnlineStrings.Get( OnlineLabel.PublishNoPark ) ) )
					return true;
				Require( ++hoverFrames < 12, "the reason Publish park is greyed shows while the pointer rests on it" );
				var rect = menu.Find( "publish" )!.ScreenRect( Context.Canvas );
				flow.InjectedInput = UiInput.Idle( new System.Numerics.Vector2( rect.X + rect.Width / 2, rect.Y + rect.Height / 2 ) );
				return false;
			}) );
			Do( "online menu capture", () =>
			{
				var menu = flow.Menu!.Stack.Top!;
				var frame = CaptureFrame( "online-menu.png" );
				VerifyText( frame, OnlineStrings.Get( OnlineLabel.PublishNoPark ), "the reason Publish park is greyed" );
				var waiting = menu.Find( "waiting" )!;
				VerifyText( frame, OnlineStrings.Ui( OnlineStrings.OutboxWaiting, "You have postcards in your outbox, waiting to be sent." ), "the postcard line while logged out" );
				foreach ( var button in menu.Elements.OfType<UiButton>().Where( button => button.Id != "back" ) )
					Require( button.Bounds.Bottom <= waiting.Bounds.Y, $"the postcard line does not overlap {button.Id}" );
				// the buttons keep the menu's own (bubbly) font family: a label box too small for it makes a button fall back to a plain body font (Label, Small)
				foreach ( var button in menu.Elements.OfType<UiButton>().Where( button => button.Id != "back" ) )
				{
					var label = button.Text!();
					var draw = Context.Batch.TextDraws.Last( entry => entry.Text == label );
					var atlas = Context.Batch.Glyphs[draw.FirstGlyph].Atlas;
					Require( ReferenceEquals( atlas, Context.Fonts.Menu ) || Context.Fonts.Smaller( Context.Fonts.Menu ).Any( smaller => ReferenceEquals( atlas, smaller ) ), $"the {button.Id} button '{label}' is drawn in the menu font family" );
				}
				foreach ( var entry in flow.Online.Folders.List( flow.Online.Folders.Outbox ) )
					File.Delete( entry.Path );
				flow.Menu.Stack.Pop();
			} );
			// The outbox opened from OpenTPW's online menu with two waiting cards: the menu gives way to the window, so its buttons do not show around it,
			// and each card's row takes the original's Title/Date/Send/Delete columns, marked for sending (tp.exe 0x4bcaec).
			Do( "open online menu with two cards", () =>
			{
				foreach ( var title in new[] { "Greetings from the jungle", "See you soon" } )
					flow.Online.Folders.Store( flow.Online.Folders.Outbox, Online.Packages.Postcard.Create( "Alice", new[] { "Bobby" }, title, "Postcard", "English" ) );
				new UI.OnlineScreens( flow.Menu!.Stack, flow.Strings, Context.Models, new UI.OnlineHost { Session = flow.Online } ).ShowMenu();
			} );
			Wait( "online menu with two cards renders", 2 );
			Do( "open outbox from the menu", () => Click( flow.Menu!.Stack.Top!, "outbox" ) );
			Wait( "outbox renders", 3 );
			Do( "outbox with cards capture", () =>
			{
				Require( flow.Menu!.Stack.Top?.Name == "outbox", "the menu's Unsent Postcards button opens the outbox" );
				var frame = CaptureFrame( "online-outbox-cards.png" );
				VerifyText( frame, "Greetings from the jungle", "an outbox card's title" );
				Require( !Context.Batch.TextDraws.Any( entry => entry.Text == OnlineStrings.Get( OnlineLabel.ImportFile ) ), "the online menu's buttons do not show around the outbox" );
				foreach ( var (path, _, _) in flow.Online.Folders.List( flow.Online.Folders.Outbox ) )
					File.Delete( path );
			} );
			Do( "leave online", () =>
			{
				while ( flow.Menu!.Stack.Screens.Count > 1 )
					flow.Menu.Stack.Pop();
			} );
		}
		// Options > Game files with a throw-away setup.json.
		Do( "open game files", () => flow.Menu!.Stack.Push( GameFilesScreen.Create( flow.Menu.Stack, flow.Strings, Path.Combine( temporaryDirectory, SetupSettings.FileName ) ) ) );
		Wait( "game files renders", 3 );
		Do( "game files capture", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "gameFiles", "game files screen opens" );
			var files = CaptureFrame( "game-files.png" );
			VerifyText( files, flow.Strings.Extra( OpenTpwText.GameFolder ), "game folder label" );
			VerifyText( files, flow.Strings.Extra( OpenTpwText.BonusFolder ), "bonus content label" );
			flow.Menu.Stack.Pop();
		} );
		// [EXT:mods-ui] Options > Extensions and mods with a throw-away mods.json: both columns render, the online extension is listed and switching it is pending until OK.
		Do( "open mods", () => flow.Menu!.Stack.Push( ModsScreen.Create( flow.Menu.Stack, flow.Strings, new ModsServices { SettingsPath = Path.Combine( temporaryDirectory, OpenTPW.Mods.ModSettings.FileName ) } ) ) );
		Wait( "mods renders", 3 );
		Do( "mods capture", () =>
		{
			var mods = flow.Menu!.Stack.Top!;
			Require( mods.Name == "mods", "mods screen opens" );
			var list = (UiScrollList)mods.Find( "extensions" )!;
			list.Select( 0 );
			Require( list.Rows().Count > 0 && list.Rows()[0].StartsWith( OpenTPW.Mods.OfficialExtensions.Online.Name, StringComparison.Ordinal ) && list.Trailing!( 0 ) == flow.Strings.Extra( OpenTpwText.ModOn ), "the online extension is listed as on" );
			var capture = CaptureFrame( "mods.png" );
			VerifyText( capture, flow.Strings.Extra( OpenTpwText.ModsPage ), "mods title" );
			VerifyText( capture, flow.Strings.Extra( OpenTpwText.ModsExtensions ), "extensions column" );
			VerifyText( capture, flow.Strings.Extra( OpenTpwText.ModsCommunity ), "community column" );
			Click( mods, "cancel" );
		} );
		// [EXT:save-extension-switch] the load dialog for a park whose extensions differ: a rule played with that is off now, a rule on now it was played without, and a content extension to turn on
		Do( "open extension switch", () =>
		{
			var active = OpenTPW.Mods.ModSet.Resolve( Array.Empty<OpenTPW.Mods.DiscoveredMod>(), new OpenTPW.Mods.ModSettings { Enabled = new Dictionary<string, bool>
				{ [OpenTPW.Mods.OfficialExtensions.Pools.Id] = false, [OpenTPW.Mods.OfficialExtensions.RainMood.Id] = true } }, null, GameEdition.ThemeParkWorld, OpenTPW.Mods.OfficialExtensions.All );
			var saved = new[] { new ParkModRecord( OpenTPW.Mods.OfficialExtensions.Pools.Id, "1.0.0", OpenTPW.Mods.ModSet.NoFilesSha256 ), new ParkModRecord( OpenTPW.Mods.OfficialExtensions.WinterCold.Id, "1.0.0", OpenTPW.Mods.ModSet.NoFilesSha256 ) };
			var plan = ParkExtensionSwitch.Plan( saved, active, Array.Empty<string>() );
			Require( plan is { CanSwitch: true } && plan.TurnOn.Count == 1 && plan.ContinueWith.Count == 1 && plan.ContinueWithout.Count == 1, "the switch dialog's plan turns pools on and switches two rules" );
			flow.Menu!.Stack.Push( ExtensionSwitchDialog.Create( flow.Menu.Stack, flow.Strings, plan!, () => { } ) );
		} );
		Wait( "extension switch renders", 3 );
		Do( "extension switch capture", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "extensionSwitch", "the extension switch dialog opens" );
			var capture = CaptureFrame( "extension-switch.png" );
			VerifyText( capture, flow.Strings.Extra( OpenTpwText.ModsExtensions ), "extension switch caption" );
			flow.Menu.Stack.Pop();
		} );
		Wait( "mods cancel", 3 );
		Do( "mods closed", () => Require( flow.Menu!.Stack.Top?.Name != "mods", "cancel leaves the mods screen" ) );
		PlanTexturePackSwitch();
		startLanguage = GameLanguage.Current.Name;
		PlanLanguageSwitch();
		Do( "open lobby menu", () => flow.Menu!.ShowMenu() );
		Wait( "lobby menu opens", 2 );
		Do( "open options", () => Click( flow.Menu!.Stack.Top!, "options" ) );
		Wait( "options open", 3 );
		Do( "options render", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "options", "options screen opens" );
			var effects = GameOptions.Current.SoundEffectsVolume;
			// A click in the middle of the slider's hit region sets the middle step.
			Click( flow.Menu.Stack.Top!, "effects" );
			stepState = effects;
		} );
		Wait( "volume changes", 3 );
		Do( "options capture", () =>
		{
			Require( GameOptions.Current.SoundEffectsVolume != stepState, "clicking a volume slider changes it" );
			var options = flow.Menu!.Stack.Top!;
			var capture = CaptureFrame( "options.png" );
			VerifyText( capture, flow.Strings[UIStrings.GameOptions], "Game Options title" );
			VerifyText( capture, ((UiLabel)options.Find( "resolutionLabel" )!).Text(), "screen resolution label" );
			VerifyText( capture, ((UiLabel)options.Find( "effectsLabel" )!).Text(), "sound effects volume label" );
			VerifyText( capture, ((UiLabel)options.Find( "advisorLabel" )!).Text(), "advisor label" );
			VerifyText( capture, ((UiLabel)options.Find( "scrollLabel" )!).Text(), "scroll label" );
			VerifyText( capture, flow.Strings.Extra( OpenTpwText.OpenTpwPage ), "OpenTPW button" );
			Click( options, "openTpw" );
		} );
		Wait( "OpenTPW page opens", 3 );
		Do( "OpenTPW page capture", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "openTpwOptions", "the OpenTPW button opens its page" );
			var capture = CaptureFrame( "options-opentpw.png" );
			VerifyText( capture, ((UiLabel)flow.Menu.Stack.Top.Find( "upscalingLabel" )!).Text(), "OpenTPW upscaling row" );
			if ( Context.Canvas.TextScale < flow.Display.EffectiveUiScale )
				VerifyText( capture, ((UiLabel)flow.Menu.Stack.Top.Find( "effective" )!).Text(), "effective output and interface-scale fallback" );
			flow.InjectedInput = UiInput.Key( UiKeys.Back );
		} );
		Wait( "OpenTPW page closes", 3 );
		Do( "back on the original page", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "options", "Back returns to the original options page" );
			flow.InjectedInput = UiInput.Key( UiKeys.Back );
		} );
		Wait( "options cancelled", 3 );
		Do( "options reverted", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "lobby" && GameOptions.Current.SoundEffectsVolume == stepState, "Escape cancels options and restores the volume" );
			Require( !File.Exists( flow.OptionsPath ), "cancelled options are not written" );
			lobbyMaterialsBeforePark = Asset.All.Count( asset => asset is Material or Shader );
			lobbyShadersBeforePark = ShaderCounts();
			lobbyTestShadersBeforePark = TestShaderCount();
			// from here on a texture made from bytes records where it was made, so a park that leaves one behind is named
			Texture.TrackOrigins = true;
			lobbyPathlessTexturesBeforePark = PathlessTextures();
			flow.Menu.Main.Focus( flow.Menu.Main.Find( "enterPark" ) );
			flow.InjectedInput = UiInput.Key( UiKeys.Accept );
		} );
		Wait( "game mode opens", 3 );
		PlanFullSimulationStart( () => VerifyText( CaptureFrame( "game-mode.png" ), flow.Strings[UIStrings.InstantAction], "Instant Action button" ) );
		Do( "park loaded", () =>
		{
			Require( flow.Level?.OriginalPark?.LevelName == "jungle" && flow.Hud != null, "menu loads the original jungle level with the HUD" );
			Require( flow.Lobby == null && flow.Menu == null, "front end is torn down" );
			Require( !flow.Level!.ShowDeveloperPanels, "front-end games hide the developer panels" );
			var capture = CaptureFrame( "hud.png" );
			VerifyText( capture, flow.Hud!.MoneyText, "HUD bank balance" );
			VerifyText( capture, flow.Hud.DateText, "HUD date" );
			firstParkHeadAssets = flow.Level!.RideHeadRenderer!.OwnedAssets.ToArray();
			firstParkGuestAssets = flow.Level.GuestRenderer!.OwnedAssets.ToArray();
			Require( firstParkGuestAssets.Length == ExpectedKidSets + 2, "the park owns its kid-set atlas textures (the preset's limit, KidSetLoad), its material and shader" );
			Require( firstParkHeadAssets.Length == ExpectedKidSets + 2, "the park owns its head atlas textures (the preset's limit), its material and shader" );
			var economy = flow.Level!.Park!.Economy;
			Require( flow.StartKind == ParkStartKind.FullSimulation && economy.Mode == ParkGameMode.FullSimulation && !economy.Settings.IsEasy
				&& flow.Level.OriginalPark!.Save == null && flow.Level.Park.Import == null, "Full Simulation starts with standard balance and without the Easymode seed" );
			objectsBeforePurchase = economy.Objects.Count;
			Require( flow.Hud.Status is EconomyParkStatus && flow.Hud.Status.Money == economy.Balance, "HUD bank balance is the park economy's" );
			Require( flow.Hud.DateText == string.Format( flow.Strings.Extra( OpenTpwText.DateFormat ), economy.Date.Year, economy.Date.Month, economy.Date.Day ), "HUD date is the park clock" );
			economy.EventRaised += item =>
			{
				if ( item.Kind == ParkEventKind.ObjectBuilt && item.InfoId == PrototypeRide.InfoId )
					{
						builtCost = item.Amount;
						purchaseEvents++;
					}
			};
			Click( flow.Hud.Screen, "map" );
		} );
		PlanParkHeadTextureSwitch();
		Wait( "map window opens", 3 );
		Do( "map window", () =>
		{
			var hud = flow.Hud!;
			Require( hud.MapWindowOpen && hud.Stack.Top?.Name == "map", "map button opens the original map window" );
			CaptureFrame( "map.png" );
			Require( Context.Batch.Draws.Any( draw => draw.Texture.ImagePath == "/levels/jungle/2dmap.tga" ), "the map uses the shipped jungle image" );
			Click( hud.Stack.Top!, "close" );
		} );
		Wait( "map window closes", 3 );
		// [BIN:TPW-EXE:0x751720 camera panel] the camera button slides out a panel with only the camcorder and postcard buttons (no view-cycle button)
		Do( "camera button", () =>
		{
			Require( !flow.Hud!.MapWindowOpen && flow.Hud.Stack.Top?.Name == "hud", "map close returns to the HUD" );
			Click( flow.Hud.Screen, "camera" );
		} );
		WaitForLoad( "camera panel slides out", () => flow.Hud!.CameraPanelOpen && flow.Hud.CameraSlide.Step == ArmSlide.Steps );
		Do( "camera panel", () =>
		{
			var hud = flow.Hud!;
			foreach ( var id in new[] { "cameraPanel", "cameraCamcorder", "cameraPostcard" } )
				Require( hud.Screen.Find( id ) is { Visible: true }, $"the camera panel shows '{id}'" );
			Require( hud.Screen.Find( "cameraViews" ) == null, "the camera panel has no view-cycle button" );
			CaptureFrame( "camera-panel.png" );
			Click( hud.Screen, "camera" );
		} );
		WaitForLoad( "camera panel slides in", () => !flow.Hud!.CameraPanelOpen && flow.Hud.CameraSlide.Step == 0 );
		Do( "buy after map", () =>
		{
			Require( flow.Hud!.Stack.Top?.Name == "hud", "the HUD is back after the camera panel" );
			Click( flow.Hud.Screen, "finance" );
		} );
		Wait( "finance window opens", 3 );
		Do( "finance window", () =>
		{
			var hud = flow.Hud!;
			Require( hud.FinanceWindowOpen && hud.Stack.Top?.Name == "finance", "finance button opens the original financial graph" );
			var capture = CaptureFrame( "finance.png" );
			VerifyText( capture, flow.Strings[UIStrings.FinancialInformation], "Financial Information title" );
			Click( hud.Stack.Top!, "loans" );
		} );
		Wait( "loans window opens", 3 );
		Do( "loans window", () =>
		{
			var hud = flow.Hud!;
			Require( hud.LoansWindowOpen && hud.Stack.Top?.Name == "loans", "finance loans button opens the original bank window" );
			var capture = CaptureFrame( "loans.png" );
			VerifyText( capture, flow.Strings[UIStrings.GraphAvailableLoans], "Available Loans title" );
			Click( hud.Stack.Top!, "exit" );
		} );
		Wait( "loans window closes", 3 );
		Do( "finance window closes", () =>
		{
			var hud = flow.Hud!;
			Require( !hud.LoansWindowOpen && hud.Stack.Top?.Name == "finance", "loans exit returns to financial information" );
			Click( hud.Stack.Top!, "exit" );
		} );
		Wait( "finance window closes", 3 );
		Do( "research window opens", () =>
		{
			var hud = flow.Hud!;
			Require( !hud.FinanceWindowOpen && hud.Stack.Top == hud.Screen, "finance exit returns to the HUD" );
			// tp.exe refuses the lab without a researcher with a message box that pauses the park (UI-096)
			Click( hud.Screen, "research" );
		} );
		Wait( "research refusal opens", 3 );
		Do( "research refusal", () =>
		{
			var hud = flow.Hud!;
			Require( hud.Stack.Top?.Name == ParkHud.ResearchRefusedScreen && !hud.ResearchWindowOpen, "the lab is refused while no researcher is hired" );
			Require( hud.ClockPaused, "the refusal's message box pauses the park" );
			Click( hud.Stack.Top!, "slot0" );
			var economy = flow.Level!.Park!.Economy;
			economy.Hire( economy.Staff.Candidates.First( candidate => candidate.Type == StaffType.Researcher ).Id );
		} );
		Wait( "research refusal closes", 3 );
		Do( "research window opens", () =>
		{
			var hud = flow.Hud!;
			Require( hud.Stack.Top == hud.Screen && !hud.ClockPaused, "the refusal closes and the park runs again" );
			Click( hud.Screen, "research" );
		} );
		Wait( "research window opens", 3 );
		Do( "research window", () =>
		{
			var hud = flow.Hud!;
			Require( hud.ResearchWindowOpen && hud.Stack.Top?.Name == "research", "research button opens the original research lab" );
			var capture = CaptureFrame( "research.png" );
			VerifyText( capture, flow.Strings[UIStrings.ResearchLab], "Research Lab title" );
			Click( hud.Stack.Top!, "exit" );
		} );
		Wait( "research window closes", 3 );
		Do( "buy window opens", () =>
		{
			var hud = flow.Hud!;
			Require( !hud.ResearchWindowOpen && hud.Stack.Top == hud.Screen, "research exit returns to the HUD" );
			// the smoke's staff steps start from a park without staff: the researcher the lab needed leaves again
			var economy = flow.Level!.Park!.Economy;
			foreach ( var member in economy.Staff.OfType( StaffType.Researcher ).ToArray() )
				economy.Fire( member.Id );
			// The buy list shows only researched items (the filler 0x4aaf70), so the flow researches the whole ride
			// catalogue first; a locked item cannot be listed or clicked, as in the original.
			var research = flow.Level!.Park!.Economy.Research;
			research.Restore( research.Completed.Concat( hud.Catalog.GetItems( BuildCategory.Rides ).Select( item => (item.InfoId, 0) ) ).Distinct().ToArray(), research.ProgressEntries.ToArray(), research.Effort );
			Click( hud.Screen, "buy" );
		} );
		Wait( "buy window opens", 3 );
		Do( "buy window", () =>
		{
			var hud = flow.Hud!;
			Require( hud.BuyWindowOpen && hud.Stack.Top?.Name == "buy", "buy button opens the original buy window" );
			Require( hud.Catalog is OriginalBuildCatalog && hud.BuyItems.Count > 0, "the original ride catalogue lists the researched rides (the buy list filler 0x4aaf70 shows only available items)" );
			var capture = CaptureFrame( "buy-window.png" );
			VerifyText( capture, flow.Strings[UIStrings.BuyRide], "Buy Ride title" );
			VerifyText( capture, flow.Strings[UIStrings.NameAttr], "Name column header" );
			VerifyText( capture, flow.Strings[UIStrings.Price], "Price column header" );
			Click( hud.Stack.Top!, "allStaff" );
		} );
		Wait( "staff window opens", 3 );
		Do( "staff window", () =>
		{
			var hud = flow.Hud!;
			Require( hud.StaffWindowOpen && hud.Stack.Top?.Name == "staff", "buy window staff button opens the original staff window" );
			Require( !hud.ShowingEmployedStaff && hud.StaffCandidates.Count > 0, "the staff window opens its applicant list" );
			smokeStaffId = hud.SelectedCandidateId;
			Require( smokeStaffId != 0, "an applicant is selected" );
			var capture = CaptureFrame( "staff.png" );
			VerifyText( capture, ((UiLabel)hud.Stack.Top!.Find( "staffName" )!).Text(), "selected applicant name" );
			flow.InjectedInput = UiInput.Click( Context.Canvas.Map( new System.Numerics.Vector2( 1240, 441 + ParkHud.StaffRowHeight / 2 ), UiAnchor.Center ) );
		} );
		Wait( "staff applicant hired", 3 );
		Do( "employed staff view", () =>
		{
			var hud = flow.Hud!;
			var level = flow.Level!;
			var placement = level.StaffPlacement;
			Require( placement is { Kind: StaffToolKind.Place } && !hud.StaffWindowOpen, "clicking the selected applicant closes the staff window and starts the Place tool" );
			Require( level.Park!.Economy.Staff.Members.All( member => member.Id != smokeStaffId )
				&& level.Park.Economy.Staff.Candidates.Any( candidate => candidate.Id == smokeStaffId ), "the applicant is not employed until the placement click" );
			var cells = level.Guests!.Grid.Cells;
			(int X, int Y) target = (-1, -1);
			for ( var y = 0; y < cells.Height && target.X < 0; y++ )
				for ( var x = 0; x < cells.Width && target.X < 0; x++ )
					if ( placement!.IsValid( x, y ) )
						target = (x, y);
			Require( target.X >= 0 && placement!.Click( target.X, target.Y ) == StaffClickResult.Placed, "a click on a valid cell hires the applicant" );
			Require( level.Park.Economy.Staff.Members.Any( member => member.Id == smokeStaffId ), "the placed applicant is employed" );
			hud.OpenStaffWindow();
			Click( hud.Stack.Top!, "staffViewExt" );
		} );
		Wait( "employed staff view opens", 3 );
		Do( "dismiss smoke employee", () =>
		{
			var hud = flow.Hud!;
			Require( hud.ShowingEmployedStaff && hud.StaffItems.Count == flow.Level!.Park!.Economy.Staff.Members.Count, "the staff window lists employed staff" );
			Require( hud.SelectedStaffId == smokeStaffId, "the hired smoke-test employee is selected" );
			var capture = CaptureFrame( "staff-employed.png" );
			VerifyText( capture, ((UiLabel)hud.Stack.Top!.Find( "staffName" )!).Text(), "selected employee name" );
			VerifyText( capture, ((UiLabel)hud.Stack.Top.Find( "statValue3" )!).Text(), "selected employee happiness" );
			// a click on the employee's row opens the per-employee window (tp.exe 0x4b6080) in place of the staff window
			flow.InjectedInput = UiInput.Click( Context.Canvas.Map( new System.Numerics.Vector2( 1240, 441 + ParkHud.StaffRowHeight / 2 ), UiAnchor.Center ) );
		} );
		Wait( "employee window opens", 3 );
		Do( "dismiss from the employee window", () =>
		{
			var hud = flow.Hud!;
			Require( hud.EmployeeWindowOpen && !hud.StaffWindowOpen && hud.Stack.Top?.Name == "employee" && hud.Employee?.Id == smokeStaffId,
				"the employee's row opens the per-employee window" );
			var capture = CaptureFrame( "staff-employee-window.png" );
			VerifyText( capture, ((UiLabel)hud.Stack.Top!.Find( "title" )!).Text(), "employee name title" );
			VerifyText( capture, ((UiLabel)hud.Stack.Top.Find( "employedFor" )!).Text(), "employed for" );
			Click( hud.Stack.Top!, "dismissStaff" );
		} );
		Wait( "dismissal confirmation opens", 3 );
		Do( "confirm smoke employee dismissal", () =>
		{
			Require( flow.Hud!.Stack.Top?.Name == "confirmDismissStaff" && !flow.Hud.EmployeeWindowOpen, "dismissal closes the employee window and asks for confirmation" );
			Click( flow.Hud.Stack.Top!, "choice0" );
		} );
		Wait( "smoke employee dismissed", 3 );
		Do( "staff window again", () =>
		{
			Require( flow.Level!.Park!.Economy.Staff.Members.All( member => member.Id != smokeStaffId ), "the dismiss button removes the employee" );
			flow.Hud!.OpenStaffWindow();
		} );
		Wait( "staff window reopens", 3 );
		// The All Visitors window (UI-907..910): visitors seeded through the guest simulation's own
		// spawn, the window opened through the staff window's shipped b_svisit button, then driven with
		// the wheel, the sort header, a row click and Enter.
		Do( "seed smoke visitors", () =>
		{
			var level = flow.Level!;
			var guests = level.Guests!;
			var cells = Enumerable.Range( 0, guests.Grid.CountX * guests.Grid.CountY )
				.Select( index => (X: index % guests.Grid.CountX, Y: index / guests.Grid.CountX) )
				.Where( cell => guests.Grid.IsWalkable( cell.X, cell.Y ) ).ToArray();
			Require( cells.Length > 0, "the park has walkable cells to seed visitors on" );
			for ( var index = 0; index < 14; index++ )
				guests.SpawnInPark( cells[index % cells.Length].X, cells[index % cells.Length].Y );
			Require( flow.Hud!.VisitorItems.Count >= 14, "the seeded visitors are in the park" );
			Log.Trace( $"Front-end smoke: seeded 14 visitors on {cells.Length} path cells for the All Visitors window." );
		} );
		Do( "visitors window opens", () => Click( flow.Hud!.Stack.Top!, "allVisitors" ) );
		Wait( "visitors window renders", 3 );
		Do( "visitors window lists the park", () =>
		{
			var hud = flow.Hud!;
			Require( hud.VisitorWindowOpen && hud.Stack.Top?.Name == "visitors", "the staff window's visitors button opens the All Visitors window" );
			var rows = hud.VisitorItems;
			Require( rows.Count >= 14, "the window lists the park's visitors" );
			Require( rows.Select( guest => (guest.VisitorNumber, guest.Id) ).SequenceEqual( rows.Select( guest => (guest.VisitorNumber, guest.Id) ).OrderBy( key => key.VisitorNumber ).ThenBy( key => key.Id ) ),
				"the visitor numbers are listed in ascending order" );
			Require( hud.SelectedVisitorId == rows[0].Id, "the first row is selected" );
			var capture = CaptureFrame( "visitors.png" );
			VerifyText( capture, flow.Strings[UIStrings.AllVisitors], "All Visitors title" );
			VerifyText( capture, flow.Strings[UIStrings.HappinessVisitor], "Happiness column" );
		} );
		Do( "visitors wheel", () =>
		{
			Require( flow.Hud!.VisitorFirstRow == 0 && flow.Hud.VisitorItems.Count > ParkHud.VisitorVisibleRows, "the visitor list starts scrolled to the top" );
			flow.InjectedInput = UiInput.Idle( Context.Canvas.Map( new System.Numerics.Vector2( 1240, 650 ), UiAnchor.Center ) ) with { Wheel = -1 };
		} );
		Wait( "visitors scrolled", 3 );
		Do( "visitors wheel scrolled", () =>
		{
			Require( flow.Hud!.VisitorFirstRow == 1, "a wheel step scrolls the visitor list one row" );
			Click( flow.Hud.Stack.Top!, "headerNumber" );
		} );
		Wait( "visitors header chosen", 3 );
		Do( "visitors header sorted once", () => Require( flow.Hud!.VisitorItems.Count > 0, "the sorted visitor list is not empty" ) );
		Wait( "visitors sorted", 3 );
		Do( "visitors header sorted", () =>
		{
			var rows = flow.Hud!.VisitorItems;
			Require( rows.Count >= ParkHud.VisitorVisibleRows && rows.Select( guest => guest.Id ).SequenceEqual( rows.Select( guest => guest.Id ).OrderByDescending( id => id ) ),
				"the first press on the initial column (ascending) lists the visitor numbers backwards" );
		} );
		Do( "click a visitor row", () =>
		{
			var hud = flow.Hud!;
			var rows = hud.VisitorItems;
			var visible = Math.Min( 3, rows.Count - hud.VisitorFirstRow - 1 );
			locatedVisitor = rows[hud.VisitorFirstRow + visible];
			flow.InjectedInput = UiInput.Click( Context.Canvas.Map(
				new System.Numerics.Vector2( 1240, ParkHud.VisitorRowsTop + (visible + 0.5f) * ParkHud.VisitorRowHeight ), UiAnchor.Center ) );
		} );
		Wait( "visitor row selected", 3 );
		Do( "visitor row opened the visitor window", () =>
		{
			var hud = flow.Hud!;
			Require( hud.SelectedVisitorId == locatedVisitor!.Id, "clicking a row selects that visitor" );
			Require( hud.VisitorInfoWindowOpen && !hud.VisitorWindowOpen && hud.Stack.Top?.Name == "visitorInfo" && hud.InfoVisitor?.Id == locatedVisitor.Id,
				"the row's left click opens the per-visitor window (tp.exe 0x4b79b0) in place of the list" );
			var capture = CaptureFrame( "visitor-window.png" );
			VerifyText( capture, hud.VisitorInfoTitle, "visitor window title" );
			VerifyText( capture, flow.Strings[UIStrings.CashRemaining], "cash remaining label" );
			Click( hud.Stack.Top!, "allVisitors" );
		} );
		Wait( "visitor window returned to the list", 3 );
		Do( "visitor row selected", () =>
		{
			var hud = flow.Hud!;
			Require( hud.VisitorWindowOpen && !hud.VisitorInfoWindowOpen && hud.SelectedVisitorId == locatedVisitor!.Id, "b_kids returns to the All Visitors list on that visitor" );
			// Park the camera target in a far corner so the locate has to move it to the visitor.
			var camera = (ParkCameraMode)Camera.Mode;
			camera.SetPose( new Vector3( -9999, -9999, 0 ), camera.Height, camera.Yaw );
			flow.InjectedInput = UiInput.Key( UiKeys.Accept );
		} );
		Wait( "visitor located", 3 );
		Do( "visitor located and window closed", () =>
		{
			var hud = flow.Hud!;
			Require( !hud.VisitorWindowOpen && hud.Stack.Top?.Name == "staff", "Enter on the selected row locates the visitor and closes the window over the staff one" );
			var camera = (ParkCameraMode)Camera.Mode;
			var position = GuestRenderer.CellToEngine( flow.Level!.OriginalPark!.Heightfield, locatedVisitor!.X, locatedVisitor!.Y );
			var extent = Math.Max( 0, ParkCameraMode.TargetExtent );
			var expected = new Vector3( Math.Clamp( position.X, -extent, extent ), Math.Clamp( position.Y, -extent, extent ), 0 );
			Require( ( camera.Target - expected ).Length < 2,
				$"the camera focuses the selected visitor's simulated position (target {camera.Target}, expected {expected})" );
		} );
		Do( "staff window exit", () =>
		{
			var hud = flow.Hud!;
			Require( flow.Level!.Park!.Economy.Staff.Members.All( member => member.Id != smokeStaffId ), "the dismiss button removes the selected employee" );
			Click( hud.Stack.Top!, "exit" );
		} );
		Wait( "staff window closes", 3 );
		Do( "shops after staff", () =>
		{
			Require( !flow.Hud!.StaffWindowOpen && flow.Hud.BuyWindowOpen && flow.Hud.Stack.Top?.Name == "buy", "staff close returns to the buy window" );
			Click( flow.Hud.Stack.Top!, "categoryShops" );
		} );
		Wait( "shops listed", 3 );
		Do( "shops category", () =>
		{
			var hud = flow.Hud!;
			Require( hud.Category == BuildCategory.Shops && hud.BuyItems.All( item => item.Category == BuildCategory.Shops ) && hud.SelectedBuyItem != null, "the shops button lists the shops and selects the first" );
			var capture = CaptureFrame( "buy-window-shops.png" );
			VerifyText( capture, flow.Strings[UIStrings.BuyShop], "Buy Shop title" );
			VerifyText( capture, hud.ItemName( hud.SelectedBuyItem! ), "selected shop name" );
			Click( hud.Stack.Top!, "categoryRides" );
		} );
		Wait( "rides listed", 3 );
		Do( "show a ride with a sign", () =>
		{
			var hud = flow.Hud!;
			var aztec = hud.Catalog.GetItems( BuildCategory.Rides ).Single( item => item.Entry != null && item.Entry.ArchiveName.Equals( "tvsim", StringComparison.OrdinalIgnoreCase ) );
			ShowItem( aztec );
			Require( hud.SelectedBuyItem == aztec, "Aztec Mayhem is the selected item" );
			var icon = hud.GetIcon( aztec );
			Require( icon != null && icon.RuntimeTextureTriangleCount > 0, "the Aztec Mayhem preview draws its sign faces with the rendered sign textures, not the placeholders" );
			var sign = ObjectSigns.Get( aztec.Entry! );
			Require( sign != null && sign.TextPixelCount > 0 && (sign.Line1, sign.Line2) == SignCanvas.SplitAtMiddleSpace( hud.ItemName( aztec ) ), "the Aztec Mayhem sign shows its name over two lines" );
		} );
		Wait( "Aztec Mayhem preview renders", 3 );
		Do( "capture the sign preview", () =>
		{
			CaptureFrame( "buy-window-aztec.png" );
			flow.Hud!.SelectBuyItem( 0 );
			Require( flow.Hud.BuyFirstRow == 0, "selecting the first row scrolls the list back" );
		} );
		Do( "scroll the list", () =>
		{
			var hud = flow.Hud!;
			Require( hud.Category == BuildCategory.Rides && hud.BuyFirstRow == 0, "the rides button returns to the rides" );
			Click( hud.Stack.Top!, "scrollDown" );
		} );
		Wait( "list scrolled", 3 );
		Do( "show Totem", () =>
		{
			Require( flow.Hud!.BuyFirstRow == 1, "the down button scrolls the list one row" );
			ShowItem( TotemItem );
		} );
		Wait( "Totem selected", 3 );
		Do( "buy window with Totem", () =>
		{
			Require( flow.Hud!.BuyWindowOpen && flow.Hud.SelectedBuyItem == TotemItem, "the Totem is the selected item" );
			var capture = CaptureFrame( "buy-window-totem.png" );
			VerifyText( capture, flow.Hud.ItemName( TotemItem ), "Totem name" );
			totemLockedAtStart = !flow.Hud.Status.IsAvailable( TotemItem );
			ClickItem( TotemItem );
		} );
		Wait( "item chosen", 3 );
		Do( "research gate", () =>
		{
			if ( !totemLockedAtStart )
				return;
			// The economy has not researched the Totem yet: the HUD must refuse it. Test setup then marks
			// it researched (as if research finished) so the purchase path can be exercised.
			Require( flow.Level!.BuildEntry == null && flow.Hud!.Messages.Contains( flow.Strings.Extra( OpenTpwText.NotAvailable ) ), "an unresearched item cannot be placed" );
			var research = flow.Level.Park!.Economy.Research;
			research.Restore( research.Completed.Append( (PrototypeRide.InfoId, 0) ).Distinct().ToArray(), research.ProgressEntries.ToArray(), research.Effort );
			Require( flow.Hud!.Status.IsAvailable( TotemItem ), "Totem available once researched" );
			Log.Trace( "Front-end smoke: the Totem was not researched at the start; research marked complete for the purchase test." );
			ClickItem( TotemItem );
		} );
		Wait( "placing", 3 );
		Do( "place Totem", () =>
		{
			Require( flow.Level!.BuildEntry == TotemItem.Entry, "choosing the Totem starts catalogue placement" );
			var site = FindObjectSite( flow.Level, TotemItem.Entry! );
			var cash = flow.Level.Park!.Economy.Balance;
			totem = flow.Level.PlaceObject( TotemItem.Entry!, site.X, site.Y, flow.Level.BuildRotation );
			Require( totem != null && cash - flow.Level.Park.Economy.Balance == TotemItem.Cost, "Totem placed and charged exactly once" );
		} );
		Wait( "purchase booked", 3 );
		Do( "purchase charged", () =>
		{
			var economy = flow.Level!.Park!.Economy;
			var price = flow.Hud!.Status.PriceOf( TotemItem );
			Require( builtCost > 0 && builtCost == price, $"the Totem is bought through ParkEconomy.TryBuild at its catalogue price (built {builtCost}, price {price})" );
			Require( flow.Hud.Status.Money == economy.Balance && totem != null, "HUD balance follows the economy after the purchase" );
			Require( economy.Objects.Count == objectsBeforePurchase + 1, "one purchased economy object is created" );
			Require( flow.Level.Park.Guests!.TryGetInstance( totem!.Visitors.AttractionId, out var linked ) && economy.TryGetObject( linked, out var bought ) && bought.TotalSpent == builtCost, "guest payments link to the purchased ride" );
			Log.Trace( $"HUD money: {economy.Balance} after buying the Totem for {builtCost}; park date {economy.Date}." );
			Require( !flow.Hud.BuyWindowOpen, "the buy window closes when placement starts" );
			Require( purchaseEvents == 1 && flow.Level.PlacedRide == null, "catalogue purchase bypasses the developer prototype and HUD wallet" );
			flow.Hud.SelectObject( totem );
		} );
		Wait( "info arm opens", 3 );
		Do( "info arm", () =>
		{
			Require( flow.Hud!.InfoArmOpen, "selecting the ride opens the info arm" );
			Click( flow.Hud.Screen, "speedPaused" );
			var capture = CaptureFrame( "info-arm.png" );
			VerifyText( capture, flow.Strings[UIStrings.Excitement], "Excitement label" );
			VerifyText( capture, flow.Strings[UIStrings.Reliability], "Reliability label" );
		} );
		Wait( "pause button", 3 );
		Do( "economy paused", () =>
		{
			Require( flow.Level!.Park!.Economy.Speed == GameSpeed.Paused && flow.Hud!.Status.TimeScale == 0, "the HUD pause button pauses the park economy" );
			flow.Level.Park.Economy.Speed = GameSpeed.Normal;
			flow.Level.Park.Economy.EventRaised += item =>
			{
				if ( item.Kind == ParkEventKind.ObjectSold && item.InfoId == PrototypeRide.InfoId )
					soldFor = item.Amount;
			};
			Click( flow.Hud!.Screen, "deleteRide" );
		} );
		Wait( "delete confirmation opens", 3 );
		Do( "confirm ride deletion", () =>
		{
			// Game Options' Confirmations is on by default, so the delete button asks first.
			Require( flow.Hud!.Stack.Top?.Name == "confirmDelete", "deleting a ride asks for confirmation" );
			Click( flow.Hud.Stack.Top!, "choice0" );
		} );
		Wait( "ride sold", 3 );
		Do( "ride deleted", () =>
		{
			Require( totem!.IsDeleted && soldFor > 0 && flow.Hud!.Status.Money == flow.Level!.Park!.Economy.Balance, $"deleting the ride sells it for its scrap value (got {soldFor})" );
			Require( flow.Level!.Park!.Economy.Objects.Count == objectsBeforePurchase, "selling removes the purchased economy object" );
			Log.Trace( $"HUD delete sold the Totem for {soldFor}; balance {flow.Level.Park.Economy.Balance}." );
			flow.Hud!.OpenBuyWindow();
			secondItem = flow.Hud.Catalog.GetItems( BuildCategory.Rides ).First( item => item.InfoId != PrototypeRide.InfoId && flow.Hud.Status.IsAvailable( item ) );
			ShowItem( secondItem );
		} );
		Wait( "second build page visible", 3 );
		Do( "choose second object", () => ClickItem( secondItem! ) );
		Wait( "second item chosen", 3 );
		Do( "build second catalogue object", () =>
		{
			var level = flow.Level!;
			Require( level.BuildEntry == secondItem!.Entry, "a second distinct researched catalogue object can be chosen" );
			var site = FindObjectSite( level, secondItem.Entry! );
			var cash = level.Park!.Economy.Balance;
			secondObject = level.PlaceObject( secondItem.Entry!, site.X, site.Y, level.BuildRotation );
			Require( secondObject != null && cash - level.Park.Economy.Balance == secondItem.Cost, "the second catalogue object is charged exactly once" );
			// a built ride with a queue starts closed (RIDES-015, 0x4DB712); the door below then closes an open ride
			Require( secondObject!.IsOpen == !secondItem.Entry!.HasQueue, "the built ride starts closed exactly when its type has a queue" );
			secondObject.Open();
			var instance = level.GetEconomyInstance( secondObject! );
			Require( instance != null && level.Park.Economy.TryGetObject( instance.Value, out var bought ) && bought.TotalSpent == secondItem.Cost, "the second object links to its paid economy record" );
			var after = level.Park.Economy.Balance;
			Require( level.PlaceObject( secondItem.Entry!, site.X, site.Y, level.BuildRotation ) == null && level.Park.Economy.Balance == after, "overlap refusal does not charge" );
			Log.Trace( $"HUD catalogue built distinct objects {TotemItem.InfoId} and {secondItem.InfoId} through the economy." );
			flow.Hud!.SelectObject( secondObject );
		} );
		Wait( "second object info", 3 );
		Do( "second object open and close", () =>
		{
			Require( flow.Hud!.SelectedObject == secondObject && flow.Hud.SelectedInfo!.DisplayName == flow.Hud.ItemName( secondItem! ), "info arm follows the selected catalogue object" );
			Click( flow.Hud.Screen, "openRide" );
		} );
		Wait( "second object closed", 3 );
		// The All Attractions window (UI-905/906/910): a third fixture object is placed, the window is
		// opened through the buy window's button, rows are clicked and activated, and the fixture is sold
		// through the window's delete button with the original ride confirmation.
		Do( "build the attractions fixture", () =>
		{
			var level = flow.Level!;
			var item = flow.Hud!.Catalog.GetItems( BuildCategory.Rides ).First( candidate => candidate.InfoId != PrototypeRide.InfoId
				&& candidate.InfoId != secondItem!.InfoId && flow.Hud.Status.IsAvailable( candidate ) );
			var site = FindObjectSite( level, item.Entry! );
			thirdObject = level.PlaceObject( item.Entry!, site.X, site.Y, level.BuildRotation );
			thirdAttractionId = thirdObject!.Runtime.AttractionId;
			Require( thirdObject != null, "a third catalogue object is placed for the attractions window" );
			// a built ride with a queue starts closed (RIDES-015, 0x4DB712); the attractions window shows the working fixture, so it is opened as the player would
			thirdObject.Open();
			Log.Trace( $"Front-end smoke: placed {flow.Hud.ItemName( item )} as the attractions-window fixture." );
		} );
		Do( "attractions window opens", () =>
		{
			var hud = flow.Hud!;
			Require( hud.Stack.Top == hud.Screen, "no window is open before the attractions flow" );
			Click( hud.Screen, "buy" );
		} );
		Wait( "buy window opens again", 3 );
		Do( "open attractions", () => Click( flow.Hud!.Stack.Top!, "allAttractions" ) );
		Wait( "attractions window renders", 3 );
		Do( "attractions window lists the park", () =>
		{
			var hud = flow.Hud!;
			Require( hud.AttractionsWindowOpen && hud.Stack.Top?.Name == "attractions", "the buy window's all-attractions button opens the window" );
			var rows = hud.AttractionItems;
			Require( rows.All( row => row.Category == BuildCategory.Rides ) && rows.Any( row => row.AttractionId == thirdAttractionId ),
				"the rides tab lists the park's rides, the fixture among them" );
			Require( ParkAttractionRoster.StatusOf( rows.Single( row => row.AttractionId == secondObject!.Runtime.AttractionId ) ) == UIStrings.Closed,
				"the closed second ride shows the original CLOSED status (UITEXT 364)" );
			Require( ParkAttractionRoster.StatusOf( rows.Single( row => row.AttractionId == thirdAttractionId ) ) == UIStrings.Blank,
				"the open working fixture shows no status" );
			Require( hud.SelectedAttractionId == rows[0].AttractionId, "the first row is selected" );
			var capture = CaptureFrame( "attractions.png" );
			VerifyText( capture, flow.Strings[UIStrings.AllRides], "All Rides title" );
		} );
		Do( "click the fixture row", () => ClickAttractionRow( flow.Hud!, thirdAttractionId ) );
		Wait( "fixture row selected", 3 );
		Do( "fixture row selected", () =>
		{
			var hud = flow.Hud!;
			Require( hud.SelectedAttractionId == thirdAttractionId, "clicking a row selects that attraction" );
			Require( hud.SelectedObject == thirdObject && hud.InfoArmOpen && hud.AttractionsWindowOpen,
				"a left click on a row opens the object's info panel and keeps the list (list message 0x400)" );
			// Park the camera target in a far corner so the right click has to move it to the object.
			var camera = (ParkCameraMode)Camera.Mode;
			camera.SetPose( new Vector3( -9999, -9999, 0 ), camera.Height, camera.Yaw );
			ClickAttractionRow( hud, thirdAttractionId, right: true );
		} );
		Wait( "row located", 3 );
		Do( "right click locates the object", () =>
		{
			var hud = flow.Hud!;
			Require( !hud.AttractionsWindowOpen && hud.SelectedObject == thirdObject,
				"a right click on a row centres the camera on the object and closes the list (list message 0x402)" );
			var camera = (ParkCameraMode)Camera.Mode;
			var extent = Math.Max( 0, ParkCameraMode.TargetExtent );
			var expected = new Vector3( Math.Clamp( thirdObject!.Position.X, -extent, extent ), Math.Clamp( thirdObject.Position.Y, -extent, extent ), 0 );
			Require( ( camera.Target - expected ).Length < 2, $"the camera focuses the clicked object (target {camera.Target}, expected {expected})" );
			flow.Hud.OpenBuyWindow();
			Click( flow.Hud.Stack.Top!, "allAttractions" );
		} );
		Wait( "attractions reopens", 3 );
		Do( "attractions reopened", () =>
		{
			var hud = flow.Hud!;
			Require( hud.AttractionsWindowOpen, "the attractions window reopens over the buy window" );
			var rows = hud.AttractionItems;
			Require( rows.Any( row => row.AttractionId == thirdAttractionId ) && hud.SelectedAttractionId == rows[0].AttractionId,
				"reopening resets the selection to the first row" );
			ClickAttractionRow( hud, thirdAttractionId );
		} );
		Wait( "fixture row selected again", 3 );
		Do( "sell through the window", () =>
		{
			var hud = flow.Hud!;
			Require( hud.SelectedAttractionId == thirdAttractionId, "the fixture's row is selected" );
			Require( hud.Stack.Top!.Find( "deleteAttraction" ) is UiButton { Enabled: true }, "the delete button is enabled with a selection" );
			Click( hud.Stack.Top!, "deleteAttraction" );
		} );
		Wait( "attractions delete confirmation", 3 );
		Do( "attractions delete confirmation", () =>
		{
			Require( flow.Hud!.Stack.Top?.Name == "confirmDeleteAttraction", "the window's delete asks for confirmation" );
			Click( flow.Hud.Stack.Top!, "choice0" );
		} );
		Wait( "fixture sold", 3 );
		Do( "fixture sold and selection healed", () =>
		{
			var hud = flow.Hud!;
			Require( thirdObject!.IsDeleted && flow.Level!.Park!.Economy.Objects.Count == objectsBeforePurchase + 1,
				"the window's delete sells the fixture through the park economy (the second object remains)" );
			var rows = hud.AttractionItems;
			Require( rows.All( row => row.AttractionId != thirdAttractionId ) && rows.Count > 0
				&& hud.SelectedAttractionId == rows[0].AttractionId, "the sold attraction leaves the list and the selection heals to the first row" );
			Click( hud.Stack.Top!, "categorySideshows" );
		} );
		Wait( "empty sideshows tab", 3 );
		Do( "empty category stays empty", () =>
		{
			var hud = flow.Hud!;
			Require( hud.AttractionItems.Count == 0 && hud.SelectedAttractionId == 0, "an empty category clears the selection" );
			Require( hud.Stack.Top!.Find( "deleteAttraction" ) is UiButton { Enabled: false }, "the delete button disables without a selection" );
			Click( hud.Stack.Top!, "scrollDown" );
		} );
		Wait( "scroll clamp", 3 );
		Do( "attractions scroll clamps", () =>
		{
			var hud = flow.Hud!;
			Require( hud.AttractionFirstRow == 0, "scrolling a list that fits stays at the top" );
			Click( hud.Stack.Top!, "exit" );
		} );
		Wait( "attractions closes", 3 );
		// The developer prototype Totem joins the same window (Level.ListAttractions): it is placed beside the
		// catalogue objects, listed as one more closed ride, activated like any row, and its delete goes through
		// Level.RemoveRide — selling its economy record — instead of the object world, which does not own it.
		Do( "attractions window opens over the prototype", () =>
		{
			Require( flow.Hud!.BuyWindowOpen && !flow.Hud.AttractionsWindowOpen, "the buy window is open, the attractions window closed" );
			Click( flow.Hud.Stack.Top!, "allAttractions" );
		} );
		Wait( "attractions renders for the prototype", 3 );
		Do( "back to the rides tab", () =>
		{
			var hud = flow.Hud!;
			Require( hud.AttractionsWindowOpen, "the attractions window is open" );
			Click( hud.Stack.Top!, "categoryRides" );
		} );
		Wait( "rides tab again", 3 );
		Do( "rides before the prototype", () =>
		{
			var hud = flow.Hud!;
			var rows = hud.AttractionItems;
			Require( rows.Count > 0 && rows.All( row => row.Category == BuildCategory.Rides ), "the rides tab lists the park's rides" );
			ridesBeforePrototype = rows.Count;
			objectsBeforePrototype = flow.Level!.Park!.Economy.Objects.Count;
		} );
		Do( "place the developer prototype", () =>
		{
			var level = flow.Level!;
			PlacePrototypeAtFirstFreeSite( level );
			Require( level.PlacedRide != null, "the developer prototype Totem is placed beside the catalogue objects" );
			reviewedPrototype = level.PlacedRide;
			prototypeAttractionId = level.PlacedRide.Runtime.AttractionId;
			prototypeVisitors = level.PlacedRide.Visitors;
			Require( level.Park!.Economy.Objects.Count == objectsBeforePrototype + 1,
				"the prototype registers exactly one economy record (bought, or uncharged when refused)" );
		} );
		Wait( "the prototype joins the list", 3 );
		Do( "the prototype is listed once", () =>
		{
			var hud = flow.Hud!;
			var rows = hud.AttractionItems;
			Require( rows.Count == ridesBeforePrototype + 1 && rows.Any( row => row.AttractionId == prototypeAttractionId ),
				"the live list picks the placed prototype up as one more ride" );
			Require( rows.GroupBy( row => row.AttractionId ).All( group => group.Count() == 1 ), "no attraction id is listed twice" );
			var level = flow.Level!;
			Require( ReferenceEquals( level.ListAttractions().Single( item => item.Runtime.AttractionId == prototypeAttractionId ), reviewedPrototype ),
				"enumeration preserves the prototype object identity" );
			// A different object can carry the same id in a different script world: removal must check ownership.
			var foreign = new OriginalObject( reviewedPrototype!.Entry, reviewedPrototype.Placement,
				new RideScriptWorld(), seed: null, open: false, attractionId: prototypeAttractionId, seedScript: true );
			try
			{
				var balance = level.Park!.Economy.Balance;
				Require( !level.RemoveAttraction( foreign ) && !foreign.IsDeleted
					&& ReferenceEquals( level.PlacedRide, reviewedPrototype ) && level.Park.Economy.Balance == balance,
					"a foreign object sharing an attraction id cannot remove or sell the owned prototype" );
			}
			finally { foreign.Delete(); }
			var prototype = rows.Single( row => row.AttractionId == prototypeAttractionId );
			Require( ParkAttractionRoster.StatusOf( prototype ) == UIStrings.Closed, "the prototype starts closed (UITTEXT 364)" );
			Require( prototype.Name == hud.ItemName( TotemItem ), "the prototype row shows the catalogue Totem name" );
			ClickAttractionRow( hud, prototypeAttractionId );
		} );
		Wait( "prototype row selected", 3 );
		Do( "the prototype's details are live", () =>
		{
			var hud = flow.Hud!;
			Require( hud.SelectedAttractionId == prototypeAttractionId, "the prototype's row is selected" );
			var prototype = hud.AttractionItems.Single( row => row.AttractionId == prototypeAttractionId );
			Require( prototype.WearPercent == 100, "the prototype's state of repair is its live wear gauge (a new ride)" );
			flow.Level!.PlacedRide!.Start();
		} );
		Wait( "prototype opens", 3 );
		Do( "the prototype's status follows its door", () =>
		{
			var hud = flow.Hud!;
			Require( flow.Level!.PlacedRide!.IsOpen
				&& ParkAttractionRoster.StatusOf( hud.AttractionItems.Single( row => row.AttractionId == prototypeAttractionId ) ) == UIStrings.Blank,
				"opening the prototype clears its CLOSED status" );
			flow.InjectedInput = UiInput.Key( UiKeys.Accept ); // Enter keeps the select-and-close action ([EXT:overview-select-button])
		} );
		Wait( "prototype row activated", 3 );
		Do( "prototype activation selects the Totem", () =>
		{
			var hud = flow.Hud!;
			Require( !hud.AttractionsWindowOpen && hud.InfoArmOpen && hud.SelectedObject == flow.Level!.PlacedRide,
				"Enter on the prototype's row selects the Totem in the park and closes the window" );
			Require( hud.Stack.Top == hud.Screen, "row activation returns to the HUD before riding" );
		} );
		Wait( "HUD before ride-view gate", 3 );
		Do( "prototype ride view owns keyboard", () =>
		{
			Require( flow.Level!.EnterRideView( reviewedPrototype! ), "the live prototype can enter its ride view" );
			Input.InjectKeyDown( Input.ChordKey( InputButton.AllVisitors ), Input.ChordKey( InputButton.BuyAttractions ) );
		} );
		Wait( "ride-view window key frame", 2 );
		Do( "ride view blocks window keys", () =>
		{
			Require( flow.Level!.RideView != null && !flow.Hud!.VisitorWindowOpen && !flow.Hud.BuyWindowOpen,
				"injected window keys cannot open a window during the ride view" );
			flow.Level.ExitRideView();
			flow.Hud.OpenBuyWindow();
			Click( flow.Hud.Stack.Top!, "allAttractions" );
		} );
		Wait( "attractions reopens over the prototype", 3 );
		Do( "select the prototype row again", () => ClickAttractionRow( flow.Hud!, prototypeAttractionId ) );
		Wait( "prototype row selected again", 3 );
		Do( "delete the prototype", () =>
		{
			var hud = flow.Hud!;
			Require( hud.SelectedAttractionId == prototypeAttractionId
				&& hud.Stack.Top!.Find( "deleteAttraction" ) is UiButton { Enabled: true }, "the delete button is enabled for the prototype" );
			Click( hud.Stack.Top!, "deleteAttraction" );
		} );
		Wait( "prototype delete confirmation", 3 );
		Do( "window keys during prototype confirmation", () => Input.InjectKeyDown( Input.ChordKey( InputButton.AllVisitors ), Input.ChordKey( InputButton.BuyAttractions ) ) );
		Wait( "confirmation owns keyboard", 2 );
		Do( "confirmation blocks window keys", () =>
			Require( flow.Hud!.Stack.Top?.Name == "confirmDeleteAttraction" && !flow.Hud.VisitorWindowOpen,
				"injected window keys cannot replace the delete confirmation" ) );
		Do( "prototype delete confirmation", () =>
		{
			Require( flow.Hud!.Stack.Top?.Name == "confirmDeleteAttraction", "deleting the prototype asks with the ride confirmation" );
			Click( flow.Hud.Stack.Top!, "choice0" );
		} );
		Wait( "prototype removed", 3 );
		Do( "prototype removed through its own seam", () =>
		{
			var hud = flow.Hud!;
			var level = flow.Level!;
			Require( level.PlacedRide == null && level.IsPlacing == false, "the window's delete removes the prototype through Level.RemoveRide" );
			Require( level.Park!.Economy.Objects.Count == objectsBeforePrototype,
				"the prototype's economy record is sold or removed with it (the object world never owned it)" );
			Require( level.Guests?.Attractions.Contains( prototypeVisitors! ) != true, "the prototype's guest queue is unregistered" );
			Require( prototypeVisitors!.Host == null && level.Park.Guests?.TryGetInstance( prototypeAttractionId, out _ ) == false,
				"the removed prototype releases its guest host and payment association" );
			var balance = level.Park.Economy.Balance;
			Require( reviewedPrototype!.IsDeleted && !level.RemoveAttraction( reviewedPrototype )
				&& level.Park.Economy.Balance == balance && level.Park.Economy.Objects.Count == objectsBeforePrototype,
				"repeated removal of the deleted prototype cannot sell it twice" );
			var rows = hud.AttractionItems;
			Require( rows.Count == ridesBeforePrototype && rows.All( row => row.AttractionId != prototypeAttractionId )
				&& hud.SelectedAttractionId == rows[0].AttractionId, "the sold prototype leaves the list and the selection heals" );
			Log.Trace( "Front-end smoke: the developer prototype Totem was listed, activated and removed through the attractions window." );
			Click( hud.Stack.Top!, "exit" );
		} );
		Wait( "attractions closes after the prototype", 3 );
		Do( "buy window closes after attractions", () => Click( flow.Hud!.Stack.Top!, "exit" ) );
		Wait( "buy closed before Escape", 3 );
		// The window keys are the language's KEYBOARD.str letters (Input.ChordKey: Dutch All Visitors is B, Buy is K, All Attractions is V), not English V and B.
		// They go through the real dispatch: Input.InjectKeyDown queues a physical key, the frame's
		// button rebuild consumes it and the HUD shortcut handler sees Input.Pressed exactly once per tap
		// (the same path a real keyboard takes; a Wait between taps gives the one-frame release).
		Do( "press the All Visitors key", () => Input.InjectKeyDown( Input.ChordKey( InputButton.AllVisitors ) ) );
		Wait( "visitors key frame", 2 );
		Do( "the visitors key opens the window", () =>
			Require( flow.Hud!.VisitorWindowOpen && flow.Hud.Stack.Top?.Name == "visitors",
				"the injected V opens the All Visitors window through Input.Pressed" ) );
		Do( "press the All Visitors key again", () => Input.InjectKeyDown( Input.ChordKey( InputButton.AllVisitors ) ) );
		Wait( "visitors key frame again", 2 );
		Do( "the visitors key closes the window", () =>
			Require( !flow.Hud!.VisitorWindowOpen, "a second V toggles the window closed" ) );
		Do( "press the Buy Attractions key", () => Input.InjectKeyDown( Input.ChordKey( InputButton.BuyAttractions ) ) );
		Wait( "buy key frame", 2 );
		Do( "the buy key opens the window", () =>
			Require( flow.Hud!.BuyWindowOpen, "the injected B opens the buy window through Input.Pressed" ) );
		Do( "press the Buy Attractions key again", () => Input.InjectKeyDown( Input.ChordKey( InputButton.BuyAttractions ) ) );
		Wait( "buy key frame again", 2 );
		Do( "the buy key closes the window", () =>
			Require( !flow.Hud!.BuyWindowOpen, "a second B toggles the buy window closed" ) );
		Do( "closed state", () =>
		{
			Require( !secondObject!.IsOpen, "HUD door closes the selected original object" );
			var tool = flow.Level!.CellTool;
			Require( !tool.IsActive || tool.Mode == CellToolMode.Queue, "only the queue tool may follow a catalogue placement (UI-031)" );
			queueToolAfterSecond = tool.IsActive;
			Log.Trace( $"Front-end smoke: {flow.Hud!.ItemName( secondItem! )} {(queueToolAfterSecond ? "left the queue tool active" : "left no tool active")} after placement." );
			flow.InjectedInput = UiInput.Key( UiKeys.Back );
		} );
		Wait( "first Escape", 3 );
		Do( "Escape leaves the queue tool", () =>
		{
			if ( !queueToolAfterSecond )
				return;
			// tp.exe 0x40c180: Escape inside the path/queue tool ends the tool; only the next Escape opens the pause menu.
			Require( !flow.Level!.CellTool.IsActive && !flow.Hud!.Paused, "Escape ends the queue tool before it opens the pause menu (tp.exe 0x40c180)" );
			flow.InjectedInput = UiInput.Key( UiKeys.Back );
		} );
		Wait( "pause opens", 3 );
		long? pausedTick = null;
		GameSpeed? pausedSpeed = null;
		Do( "window keys while paused", () =>
		{
			pausedTick = flow.Level!.Park?.Economy.Tick;
			pausedSpeed = flow.Hud!.Status.Speed;
			Input.InjectKeyDown( Input.ChordKey( InputButton.AllVisitors ), Input.ChordKey( InputButton.BuyAttractions ) );
		} );
		Wait( "pause owns keyboard", 2 );
		Do( "pause blocks window keys", () =>
			Require( flow.Hud!.Paused && flow.Hud.Stack.Top?.Name == "pause"
				&& !flow.Hud.VisitorWindowOpen && !flow.Hud.BuyWindowOpen, "injected window keys leave the pause menu in control" ) );
		// The pause menu pauses the game, and the game pause pauses the advisor's speech (tp.exe 0x598960): a response still being spoken must stand still under the menu.
		// The manual save below leaves a spoken response out of the file either way.
		CheckAdvisorSpeechPaused();
		Do( "pause menu", () =>
		{
			Require( flow.Hud!.Paused, "Escape opens the pause menu" );
			// The original's pause menu pauses the game (Mac 0x10198514 → 0x10110518): the park clock stands still, the speed setting is kept.
			Require( flow.Level!.SimulationTimeScale == 0 && flow.Level.Park?.Economy.Tick == pausedTick && flow.Hud.Status.Speed == pausedSpeed,
				"the pause menu stops the park simulation and keeps the speed setting" );
			var capture = CaptureFrame( "pause.png" );
			// [BIN:TPW-EXE:0x48c830 pause menu] a frameless list of text lines: no window and no title
			Require( flow.Hud.Stack.Top!.Find( "window" ) == null && flow.Hud.Stack.Top.Find( "title" ) == null, "the pause menu has no window frame and no title" );
			VerifyText( capture, flow.Strings[UIStrings.ResumeGame], "Resume Game" );
			VerifyText( capture, flow.Strings[UIStrings.PublishPark], "Publish Park" );
			VerifyText( capture, flow.Strings[UIStrings.ExitToLobby], "Exit To Lobby" );
			var goOnline = flow.Hud.Stack.Top!.Find( nameof( UIStrings.GoOnline ) );
			var publish = flow.Hud.Stack.Top.Find( nameof( UIStrings.PublishPark ) );
			Require( online ? goOnline != null : goOnline == null, online ? "the pause menu offers Go Online" : "the disabled online extension removes the pause menu's Go Online line" );
			Require( online || publish is { Enabled: false }, "the disabled online extension leaves Publish Park as a disabled line" );
			Click( flow.Hud.Stack.Top!, nameof( UIStrings.Save ) );
		} );
		Wait( "save completes", 3 );
		Do( "save completes", () =>
		{
			// The park-resume lane saves a front-end-started original-level park as an own-format world save
			// (GameFlow.Resume.cs SaveCurrentPark); the old read-only refusal is gone.
			var savedPath = Path.Combine( temporaryDirectory, $"opentpw-{flow.Level!.LevelName}-world.json" );
			Log.Trace( $"Front-end smoke: save check paused={flow.Hud!.Paused} file={File.Exists( savedPath )} ({savedPath}) messages=[{string.Join( " | ", flow.Hud.Messages )}]" );
			Require( !flow.Hud!.Paused && File.Exists( savedPath ) && flow.Hud.Messages.Contains( flow.Strings.Extra( OpenTpwText.ParkSaved ) ),
				"the pause-menu Save writes the own-format world save and reports it" );
			flow.Hud.OpenPauseMenu();
		} );
		Wait( "pause again", 3 );
		// The Publish Park form over the pause menu (the pause menu's Publish Park line): the original's tick and close buttons are drawn and the form's own controls stay inside its window.
		if ( online )
		{
			Do( "open publish form", () => new UI.OnlineScreens( flow.Hud!.Stack, flow.Strings, Context.Models, new UI.OnlineHost { Session = flow.Online, Level = () => flow.Level } ).ShowPublish() );
			Wait( "publish form renders", 3 );
			Do( "publish form capture", () =>
			{
				var form = flow.Hud!.Stack.Top!;
				Require( form.Name == "publishPark", "the pause menu's Publish Park opens the publish form" );
				CaptureFrame( "publish-form.png" );
				var window = form.Find( "window" )!.Bounds;
				foreach ( var id in new[] { "publish", "close", "website", "export" } )
					Require( form.Find( id ) is { } control && control.Bounds.X >= window.X && control.Bounds.Right <= window.Right && control.Bounds.Y >= window.Y && control.Bounds.Bottom <= window.Bottom, $"the publish form's {id} control lies inside its window" );
				Require( ( (UiButton)form.Find( "publish" )! ).Model == "b_okay" && ( (UiButton)form.Find( "close" )! ).Model == "b_exit", "the publish form's tick and cross draw the original b_okay and b_exit models" );
				var nameField = (UiTextField)form.Find( "parkName" )!;
				Require( nameField.MaximumLength == 15 && nameField.Text.Length is > 0 and <= 15 && ( (UiTextField)form.Find( "description" )! ).MaximumLength == 126, "the publish form's fields start filled and take 15 and 126 characters" );
				// logged out, the tick stays enabled and opens the login dialog
				Require( form.Find( "publish" )!.Enabled && !flow.Online.IsLoggedIn, "the tick stays enabled while logged out" );
				Click( form, "publish" );
			} );
			Wait( "publish login renders", 3 );
			Do( "publish login capture", () =>
			{
				Require( flow.Hud!.Stack.Top?.Name == "onlineLogin", "the tick opens the login dialog while logged out" );
				CaptureFrame( "publish-login.png" );
				flow.Hud.Stack.Pop();
				flow.Hud.Stack.Pop();
			} );
		}
		// The online globe over a park: it replaces the park and the HUD, and closing it restores the park camera.
		CameraMode? parkCamera = null;
		if ( !online )
			Do( "park camera", () => parkCamera = Camera.Mode );
		else
		{
			// A forecast of other weather puts the forecast banner over the park; the banner waits while the globe takes the park's place (WeatherForecastPresenter.ShowsOnlineGlobe).
			WeatherForecastPresenter? presenter = null;
			Do( "forecast banner in the park", () =>
			{
				Require( flow.Level!.Weather is { Started: true }, "the park's weather has started" );
				var weather = flow.Level.Weather!;
				presenter = Entity.All.OfType<WeatherForecastPresenter>().Single( entity => entity.Level == flow.Level );
				foreach ( var quality in new[] { 90, 10, 50, 70, 30 } )
				{
					weather.DebugPostForecast( quality );
					if ( !weather.ForecastBands.Equals( weather.CurrentBands ) )
						break;
				}
				Require( !weather.ForecastBands.Equals( weather.CurrentBands ), "a forecast of other weather is posted" );
				WeatherForecastPresenter.BannerHidden = false;
			} );
			Wait( "forecast banner renders", 3 );
			Do( "forecast banner draws", () => Require( presenter!.DrawnBanner != null, "the forecast banner draws over the park" ) );
			Do( "online globe in the park", () =>
			{
				parkCamera = Camera.Mode;
				flow.ShowOnline( flow.Hud!.Stack );
			} );
			Wait( "park globe renders", 3 );
			Do( "park globe", () =>
			{
				Require( flow.Globe is { Visible: true } && Camera.Mode is GlobeCameraMode, "Go Online in a park shows the globe with its camera" );
				Require( presenter!.DrawnBanner == null, "the forecast banner waits while the online globe takes the park's place" );
				CaptureFrame( "online-park-globe.png" );
				flow.Hud!.Stack.Pop();
			} );
			Wait( "park returns", 3 );
			Do( "forecast banner returns", () =>
			{
				Require( presenter!.DrawnBanner != null, "the forecast banner is back with the park's view" );
				WeatherForecastPresenter.BannerHidden = true;
			} );
		}
		var parkLanguage = PlanLanguageInPark();
		// ECON-030: the bank loses the park: the loss window (the gate camera, the full-screen banner), Escape into the hidden-HUD overlay, Escape again into the lost park's pause menu
		Do( "close the pause menu", () =>
		{
			while ( flow.Hud!.Stack.Top?.Name == "pause" )
				flow.Hud.Stack.Pop();
			Require( flow.Hud.Stack.Top?.Name == "hud" && flow.Level!.Park != null, "the park's HUD is on top" );
			flow.Level.Park!.Economy.DebugLose();
		} );
		Wait( "loss window opens", 3 );
		Do( "loss window", () =>
		{
			Require( flow.Hud!.LossWindowOpen && flow.Hud.Stack.Top?.Name == ParkHud.LossWindowScreen && flow.Level!.Park!.Economy.IsBankrupt, "the bank's message opens the loss window over the park" );
			Require( flow.Level.SimulationTimeScale > 0 && !flow.Hud.Paused, "the window pauses nothing: the lost park's calendar and bank go on" );
			Require( flow.Level.RideView != null && Level.IsGateObject( flow.Level.RideView.Ride ), "the camera rides the gate's seat" );
			CaptureFrame( "loss-window.png" );
			flow.InjectedInput = UiInput.Key( UiKeys.Back );
		} );
		Wait( "loss window leaves", 3 );
		Do( "loss overlay", () =>
		{
			Require( flow.Hud!.LossOverlayOpen && flow.Level!.LossOverlayActive && flow.Hud.Stack.Top?.Name == ParkHud.LossOverlayScreen, "Escape leaves the window for the hidden-HUD overlay" );
			Require( flow.Level.RideView == null && Camera.Mode is ParkCameraMode, "the gate camera is left and the park camera is the player's again" );
			CaptureFrame( "loss-overlay.png" );
			flow.InjectedInput = UiInput.Key( UiKeys.Back );
		} );
		Wait( "lost park pause menu opens", 3 );
		Do( "lost park pause menu", () =>
		{
			Require( flow.Hud!.Stack.Top?.Name == "pause" && flow.Level!.SimulationTimeScale == 0, "Escape in the overlay opens the pause menu" );
			Require( flow.Hud.Stack.Top!.Find( nameof( UIStrings.Save ) ) == null && flow.Hud.Stack.Top.Find( nameof( UIStrings.ExitToLobby ) ) != null, "the lost park's menu has no Save and keeps Exit To Lobby" );
		} );
		Do( "exit to lobby", () =>
		{
			Require( ReferenceEquals( Camera.Mode, parkCamera ) && (flow.Globe is { Visible: false } || !online), "closing the globe restores the park's camera" );
			// Capture the current generations: lazy pack downloads may have replaced individual atlas textures.
			firstParkHeadAssets = flow.Level!.RideHeadRenderer!.OwnedAssets.ToArray();
			firstParkGuestAssets = flow.Level.GuestRenderer!.OwnedAssets.ToArray();
			// rebuilding the path layer releases what it replaces: the same number of test.shader copies afterwards, however often it runs
			var shadersBeforeRebuild = TestShaderCount();
			flow.Level.OriginalTerrain!.RefreshPaths();
			flow.Level.OriginalTerrain.RefreshPaths();
			Require( TestShaderCount() == shadersBeforeRebuild, "rebuilding the terrain's path layer leaves no material of the replaced parts behind" );
			Require( ParticleRenderer.For( flow.Level ) is not { AtlasPixelsReleased: false }, "the particle renderer dropped its CPU atlas pixels after the upload" );
			Click( flow.Hud!.Stack.Top!, nameof( UIStrings.ExitToLobby ) );
		} );
		Wait( "lobby again", 20 );
		Do( "back in lobby", () =>
		{
			Require( flow.Level == null && flow.Menu != null && flow.Lobby != null, "Exit To Lobby returns to the front end" );
			RequireLobbySigns( flow.Lobby! );
			Require( RendererAssetsReleased( firstParkHeadAssets ), "exit releases every head atlas, material and shader from the first park" );
			Require( RendererAssetsReleased( firstParkGuestAssets ), "exit releases every guest atlas, material and shader from the first park" );
			Log.Trace( "Guest lifecycle: first park exit released all ten owned guest assets." );
			// The lobby scene and the terrain made a material (with its own copy of test.shader) per part and released only their buffers, so every lobby visit left about 160 behind. Exactly as many test.shader
			// copies are alive as before the park; what a park creates for good (the advisor's 43 materials, the particles) stays and uses other shaders.
			var lobbyMaterialsNow = Asset.All.Count( asset => asset is Material or Shader );
			Log.Trace( $"Materials and shaders with the lobby up: {lobbyMaterialsBeforePark} before the first park ({lobbyShadersBeforePark}), {lobbyMaterialsNow} after it ({ShaderCounts()})." );
			Require( lobbyMaterialsBeforePark >= 0 && TestShaderCount() == lobbyTestShadersBeforePark, $"leaving a park releases the materials of its terrain and the lobby's scene ({lobbyMaterialsBeforePark} materials and shaders before: {lobbyShadersBeforePark}; {lobbyMaterialsNow} after: {ShaderCounts()})" );
			// Releasing a material frees only what it owns (its shader copy, pipeline, uniform buffer and layouts), never the textures it draws with: the shared white placeholder and the textures cached by path stay live.
			Require( Texture.Missing.NativeTexture is { IsDisposed: false } && Asset.All.OfType<Texture>().All( texture => texture.NativeTexture is not { IsDisposed: true } ), "no live texture lost its GPU texture when the park's materials were released" );
			// Every park made a new area texture for its blueprint markers and none was released (one more texture per visit for the session):
			// the textures not loaded by path are those of the lobby again, as before the park.
			var changes = PathlessTextureChanges( lobbyPathlessTexturesBeforePark );
			Require( changes.Length == 0, $"leaving a park releases every texture it made from bytes (textures not loaded by path, by size: {changes})" );
			screenRendererTexturesAtFirstReturn = ScreenRendererTexturesBesidesTheKeyHost();
			if ( parkLanguage != null )
				Require( string.Equals( GameLanguage.Current.Name, parkLanguage, StringComparison.OrdinalIgnoreCase ) && flow.Strings.Language == parkLanguage,
					"a language chosen in the park applies once the park is left" );
			Require( flow.Menu!.Selected.Level == "jungle", "lobby returns to the island of the park" );
			CaptureFrame( "frontend-return.png" );
			flow.StartPark( "jungle", GameMode.InstantAction );
		} );
		Wait( "instant action starts", 3 );
		Do( "instant action mode before lobby", () =>
		{
			Require( flow.StartKind == ParkStartKind.InstantAction && flow.Level!.Park!.Economy.Mode == ParkGameMode.InstantAction,
				"selected Instant Action reaches the park economy" );
			Require( Texture.Missing.NativeTexture is { IsDisposed: false } && Asset.All.OfType<Texture>().All( texture => texture.NativeTexture is not { IsDisposed: true } ), "the deferred release of the first park's materials left every live texture's GPU texture alone" );
			secondParkHeadAssets = flow.Level!.RideHeadRenderer!.OwnedAssets.ToArray();
			secondParkGuestAssets = flow.Level.GuestRenderer!.OwnedAssets.ToArray();
			Require( secondParkGuestAssets.Length == ExpectedKidSets + 2 && secondParkGuestAssets.All( Asset.All.Contains ), "a second park owns a fresh guest atlas, material and shader set" );
			flow.ShowFrontEnd( "jungle" );
			Require( secondParkHeadAssets.All( asset => !Asset.All.Contains( asset ) ), "second park exit also removes every owned head asset" );
			Require( secondParkGuestAssets.All( asset => !Asset.All.Contains( asset ) ), "second park exit also removes every owned guest asset" );
		} );
		Wait( "instant action returns to lobby", 3 );
		Do( "load original park after Instant Action", () =>
		{
			var changes = PathlessTextureChanges( lobbyPathlessTexturesBeforePark );
			Require( changes.Length == 0, $"a second park leaves no texture made from bytes behind either (by size, against the lobby before the first park: {changes})" );
			// the key panel's particle host is deleted when the lobby ends: after a second exit and re-entry only the live host's atlas is more than at the first return
			Require( ScreenRendererTexturesBesidesTheKeyHost() == screenRendererTexturesAtFirstReturn, $"lobby exit and re-entry leaves no screen-space particle atlas behind ({screenRendererTexturesAtFirstReturn} at the first return, {ScreenRendererTexturesBesidesTheKeyHost()} now)" );
			Texture.TrackOrigins = false;
			flow.LoadPark( new ParkLoadEntry( "jungle", 0, false ) );
			builtInOneGo = (flow.Level!.ComputeStateHash(), Entity.All.Count, flow.Level.Objects.Objects.Count, GateSignDigest( flow.Level ));
			hudInOneGo = HudState( flow.Hud! );
			afterNextUpdate = () => hudInOneGoUpdated = flow.Hud is { } updated ? HudState( updated ) : null;
			Require( !flow.Level.SkyLoaded, "a park built in one go loads its sky on its first draw" );
		} );
		Wait( "original park loads", 3 );
		Do( "loaded park is the reference start", () =>
		{
			Require( RendererAssetsReleased( secondParkHeadAssets ), "second park exit completes scheduled native head asset disposal" );
			Require( RendererAssetsReleased( secondParkGuestAssets ), "second park exit completes scheduled native guest asset disposal" );
			Log.Trace( "Guest lifecycle: second park exit released all ten owned guest assets." );
			Require( flow.StartKind == ParkStartKind.OriginalSaveReference && flow.Level!.Park!.Economy.Mode == ParkGameMode.FullSimulation
				&& flow.Level.Park.Economy.Settings.IsEasy && flow.Level.OriginalPark!.Save != null,
				"Load Park opens the shipped save as the reference start whatever mode was chosen before ([EXT:no-player-profiles])" );
			flow.ShowFrontEnd( "jungle" );
		} );
		Wait( "lobby before the park built over frames", 3 );
		Do( "start a park over frames that a later transition replaces", () =>
		{
			lobbyBeforeAbandon = (Entity.All, Entity.All.Count, Asset.All.Count( asset => asset is Material or Shader ));
			flow.LoadPark( new ParkLoadEntry( "jungle", 0, false ), overFrames: true );
		} );
		Wait( "the replaced park is being built", 3 );
		Do( "a transition queued during the build replaces it", () =>
		{
			Require( flow.BuildingPark, "the park is still being built after three frames" );
			flow.Queue( () => flow.ShowFrontEnd( "jungle" ) );
			Require( !flow.BuildingPark && flow.Level == null && ReferenceEquals( Entity.All, lobbyBeforeAbandon.Entities )
				&& Entity.All.Count == lobbyBeforeAbandon.Count,
				"dropping the build leaves the lobby's own entities current and untouched" );
			Require( Asset.All.Count( asset => asset is Material or Shader ) == lobbyBeforeAbandon.Materials,
				"the dropped park's materials and shaders are released" );
		} );
		Wait( "the queued transition runs", 3 );
		Do( "start a park over frames that is dropped after its HUD is made", () =>
		{
			lobbyBeforeAbandon = (Entity.All, Entity.All.Count, Asset.All.Count( asset => asset is Material or Shader ));
			flow.LoadPark( new ParkLoadEntry( "jungle", 0, false ), overFrames: true );
		} );
		steps.Enqueue( ("the dropped park's HUD is made", () =>
		{
			if ( flow.PreparedHud is { } hud )
			{
				droppedHud = hud;
				return true;
			}
			Require( flow.BuildingPark, "the park is still being built until a build step makes its HUD" );
			return false;
		}) );
		Do( "a transition queued after the HUD is made drops it", () =>
		{
			Require( flow.BuildingPark, "the park whose HUD is made is not entered yet" );
			flow.Queue( () => flow.ShowFrontEnd( "jungle" ) );
			Require( !flow.BuildingPark && flow.Level == null && flow.Hud == null && ReferenceEquals( Entity.All, lobbyBeforeAbandon.Entities )
				&& Entity.All.Count == lobbyBeforeAbandon.Count, "dropping the build with its HUD made leaves the lobby current and untouched" );
			Require( droppedHud!.Stack.Screens.Count == 0, "the dropped park's prepared HUD is cleared, as a tear-down clears a retired one" );
			Require( Asset.All.Count( asset => asset is Material or Shader ) == lobbyBeforeAbandon.Materials,
				"the dropped park's materials and shaders, its sky's included, are released" );
		} );
		Wait( "the transition after the dropped HUD runs", 3 );
		Do( "load the original park over frames", () =>
		{
			flow.Transitioned += RecordBuiltOverFrames;
			flow.LoadPark( new ParkLoadEntry( "jungle", 0, false ), overFrames: true );
			Require( flow.BuildingPark && flow.Level == null && flow.Lobby != null, "a park built over frames leaves the lobby running" );
		} );
		steps.Enqueue( ("the park built over frames is entered", () =>
		{
			if ( flow.BuildingPark )
			{
				preparedHud ??= flow.PreparedHud;
				framesBuilding++;
				Require( flow.Level == null && flow.Lobby != null && Entity.All.Count > 0, "the lobby stays current while the park is built" );
				return false;
			}
			return true;
		}) );
		Do( "the park built over frames is the park built in one go", () =>
		{
			flow.Transitioned -= RecordBuiltOverFrames;
			Require( framesBuilding >= 2, $"the park is built over several frames ({framesBuilding})" );
			Require( builtOverFrames == builtInOneGo,
				$"built over frames: hash, entities, objects and gate sign pixels {builtOverFrames} equal the park built in one go {builtInOneGo}" );
			Require( preparedHud != null && ReferenceEquals( flow.Hud, preparedHud ), "the park built over frames is entered with the HUD a build step made" );
			Require( hudOverFrames == hudInOneGo, $"the HUD made in a build step is entered as the HUD made on entering: {hudOverFrames} / {hudInOneGo}" );
			Require( hudOverFramesUpdated != null && hudOverFramesUpdated == hudInOneGoUpdated,
				$"after its first update the HUD made in a build step equals the HUD made on entering: {hudOverFramesUpdated} / {hudInOneGoUpdated}" );
			Require( skyBeforeFirstDraw, "the park built over frames has its sky loaded before its first draw" );
			Require( flow.Level != null && flow.Hud != null && flow.Lobby == null && flow.StartKind == ParkStartKind.OriginalSaveReference,
				"the park built over frames is entered with its HUD, and the lobby is gone" );
			Log.Trace( $"Park built over {framesBuilding} frames: hash {builtOverFrames.Hash:X16}, {builtOverFrames.Entities} entities, {builtOverFrames.Objects} objects, gate sign {builtOverFrames.GateSign}, as built in one go." );
			var park = OriginalPark.Load( "jungle" );
			var snapshot = ParkSnapshotBuilder.FromOriginal( park, null );
			readOnlyVisit = ParkSharing.PrepareVisit( ParkSharing.CreatePackage( snapshot, "HUD read-only smoke", "", "OpenTPW", park.Map ) );
			// The own-format world save of the earlier pause-menu Save is legitimate; the visit must add nothing.
			visitBaselineFiles = Directory.EnumerateFiles( temporaryDirectory, "*.json", SearchOption.AllDirectories ).ToArray();
			flow.StartLevel( "jungle", original: true, developerPanels: false, visit: readOnlyVisit );
		} );
		Wait( "read-only visit loads", 40 );
		Do( "read-only visit guards", () =>
		{
			var level = flow.Level!;
			var hud = flow.Hud!;
			Require( level.IsReadOnlyVisit && level.Park == null, "visited parks have no writable economy" );
			visitedCount = level.Objects.Objects.Count;
			visitedObject = level.Objects.Objects.First( item => item.Runtime.IsAttraction && !item.Entry.IsFixedItem );
			var signed = level.Objects.Objects.Where( item => item.Sign != null ).ToArray();
			Require( signed.Length > 0 && signed.All( item => item.Sign!.TextPixelCount > 0 || item.Sign.Line1.Length == 0 ), "the visited park's rides show their own rendered sign text, not the SIGN1/SIGN2 placeholders" );
			Log.Trace( $"Ride signs in the visited park: {string.Join( ", ", signed.Select( item => $"{item.Entry.ArchiveName}: {item.Sign!.Line1} / {item.Sign.Line2}" ) )}" );
			visitedOpen = visitedObject.IsOpen;
			hud.BeginPlacing( hud.Catalog.GetItems( BuildCategory.Rides ).First() );
			Require( level.BuildEntry == null && !level.IsPlacing, "read-only HUD refuses direct placement requests" );
			Require( hud.Screen.Find( "buy" ) is UiButton { Enabled: false }, "build control is disabled while visiting" );
			hud.SelectObject( visitedObject );
		} );
		Wait( "read-only info renders", 3 );
		// The overview windows during a read-only visit: the buy window is disabled while visiting, so the
		// attractions window opens through the staff window's entry (the original staff window navigated
		// to it); the visited park is listed, sorted and scrolled, and the delete refuses at the handler.
		Do( "visit staff window opens", () => flow.Hud!.OpenStaffWindow() );
		Wait( "visit staff window", 3 );
		Do( "open visit attractions", () => Click( flow.Hud!.Stack.Top!, "allAttractions" ) );
		Wait( "visit attractions renders", 3 );
		Do( "visit attractions lists the park", () =>
		{
			var hud = flow.Hud!;
			Require( hud.AttractionsWindowOpen && hud.Stack.Top?.Name == "attractions", "the staff window's all-attractions button opens the window during a visit" );
			visitAttractionRows = hud.AttractionItems.Count;
			visitRowNames = hud.AttractionItems.Select( row => row.Name ).ToArray();
			// The reference save carries one scripted ride besides its features and fixed items.
			Require( visitAttractionRows >= 1, "the visited park's rides are listed" );
			var capture = CaptureFrame( "visit-attractions.png" );
			VerifyText( capture, flow.Strings[UIStrings.AllRides], "All Rides title in the visit" );
			Click( hud.Stack.Top!, "headerNameRides" );
		} );
		Wait( "visit attractions header chosen", 3 );
		Do( "visit attractions header sorted once", () => Require( flow.Hud!.AttractionItems.Count > 0, "the sorted attraction list is not empty" ) );
		Wait( "visit attractions sorted", 3 );
		Do( "visit attractions sorted", () =>
		{
			var hud = flow.Hud!;
			var names = hud.AttractionItems.Select( row => row.Name ).ToArray();
			Require( visitRowNames.Distinct().Count() == visitRowNames.Length
				? names.SequenceEqual( visitRowNames.Reverse() )
				: string.CompareOrdinal( names[0], names[^1] ) >= 0, "the first header press on the initial column reverses the list order" );
			Click( hud.Stack.Top!, "scrollDown" );
		} );
		Wait( "visit attractions scrolled", 3 );
		Do( "visit attractions read-only refusal", () =>
		{
			var hud = flow.Hud!;
			Log.Trace( $"Front-end smoke: the visit's attractions list scrolled to row {hud.AttractionFirstRow} of {visitAttractionRows} rides." );
			Require( hud.SelectedAttractionId != 0, "the visited park's list keeps a selection" );
			Require( hud.Stack.Top!.Find( "deleteAttraction" ) is UiButton { Enabled: false }, "the delete control is disabled during a read-only visit" );
			// Invoke the handler directly: the mutation boundary must refuse even programmatic calls.
			Require( !flow.Level!.RemoveAttraction( visitedObject! ) && !visitedObject!.IsDeleted
				&& flow.Level.Objects.Objects.Count == visitedCount, "the Level removal boundary refuses a read-only visit" );
			( (UiButton)hud.Stack.Top.Find( "deleteAttraction" )! ).Clicked!();
			Require( flow.Level!.Objects.Objects.Count == visitedCount && hud.Stack.Top?.Name == "attractions",
				"the visit's delete handler refuses to sell and opens no confirmation" );
			Click( hud.Stack.Top!, "exit" );
		} );
		Wait( "visit attractions closes", 3 );
		Do( "visit staff closes", () => Click( flow.Hud!.Stack.Top!, "exit" ) );
		Wait( "visit staff closed", 3 );
		Do( "read-only object controls", () =>
		{
			var hud = flow.Hud!;
			var door = (UiButton)hud.Screen.Find( "openRide" )!;
			var erase = (UiButton)hud.Screen.Find( "deleteRide" )!;
			Require( !door.Enabled && !erase.Enabled, "visit object open/delete controls are disabled" );
			// Invoke their callbacks directly to prove the mutation boundary also rejects programmatic calls.
			door.Clicked!();
			erase.Clicked!();
			Require( visitedObject!.IsOpen == visitedOpen && !visitedObject.IsDeleted && flow.Level!.Objects.Objects.Count == visitedCount, "read-only callbacks neither change nor remove objects" );
			hud.OpenPauseMenu();
			// The online game type's pause menu (STP-PPC 0x10197D90) offers no Save or Load during a visit.
			Require( hud.Stack.Top!.Find( nameof( UIStrings.Save ) ) == null && hud.Stack.Top.Find( nameof( UIStrings.Load ) ) == null, "the visit pause menu has no save or load" );
			Require( !Directory.EnumerateFiles( temporaryDirectory, "*.json", SearchOption.AllDirectories ).Except( visitBaselineFiles ).Any(), "a visit writes no save" );
		} );
		// the pause menu lays its lines out from their measured text on its first update (UiDialogs.TextMenu), so it is clicked once it has drawn
		Wait( "visit pause menu", 2 );
		Do( "visit pause menu exits", () =>
		{
			CaptureFrame( "read-only-visit.png" );
			Click( flow.Hud!.Stack.Top!, nameof( UIStrings.ExitToLobby ) );
		} );
		Wait( "lobby after visit", 20 );
		Do( "enter park again", () =>
		{
			Require( flow.Level == null && flow.Menu?.Selected.Level == "jungle", "the visit exits to the jungle island" );
			Click( flow.Menu!.Main, "enterPark" );
		} );
		Wait( "saved park offered", 3 );
		Do( "a saved jungle park offers to continue it", () =>
		{
			// The pause-menu save earlier left an own jungle save: entering the island now offers it (FrontEndMenu.ShowThemeStart).
			Require( flow.Menu!.Stack.Top?.Name == "themeStart", "entering an island with a saved park offers to continue it" );
			// the manual Continue, then the automatic Continue when the last exit left a checkpoint, New park and Back: New is next to last
			var themeStart = flow.Menu.Stack.Top!;
			var choices = Enumerable.Range( 0, 8 ).Count( index => themeStart.Find( $"choice{index}" ) != null );
			Click( themeStart, $"choice{choices - 2}" );
		} );
		Wait( "game mode again", 3 );
		Do( "choose Instant Action", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "gameMode", "enter park asks for the game mode again" );
			Click( flow.Menu.Stack.Top!, "instantAction" );
		} );
		Do( "confirm Instant Action", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "gameMode" && flow.Menu.Stack.Top.Find( "joy" ) is { Visible: true }, "the Instant Action toggle latches and shows e_joy" );
			Click( flow.Menu.Stack.Top!, "ok" );
		} );
		// the lobby's enter sequence (turn, gate, zoom under the fade) plays before the park loads
		WaitForLoad( "Instant Action park loads", () => flow.Level != null && flow.Hud != null && flow.Lobby == null );
		Wait( "Instant Action park settles", 40 );
		Do( "Instant Action start", PlayInstantAction );
	}

	/// <summary>
	/// The Instant Action start from the menu: Easy_ balance and the shipped jungle seed with its imported bank, turn
	/// and staff (research comes from the seed's own researcher), and the loan, research effort, upgrade, ticket and
	/// challenge gates (docs/ECONOMY.md, "Game modes").
	/// </summary>
	private void PlayInstantAction()
	{
		var level = flow.Level!;
		var economy = level.Park!.Economy;
		Require( flow.StartKind == ParkStartKind.InstantAction && economy.Mode == ParkGameMode.InstantAction && economy.Features == ParkModeFeatures.For( ParkGameMode.InstantAction ), "Instant Action button starts an Instant Action park" );
		Require( economy.Settings.IsEasy && level.OriginalPark!.Save != null && level.Park.Import is { ImportedObjects: > 0 }, "Instant Action jungle uses the Easy_ balance and imports the Easymode seed" );
		var seed = level.OriginalPark!.Save!;
		var bank = seed.Economy.Bank;
		// The park has run a few frames since it started: the month's opening balance and the turn show the import.
		Require( economy.Ledger.CurrentOpeningBalance == bank.Balance && economy.EntranceFee == bank.AdmissionFee && economy.Ledger.History.Count == 0
			&& economy.Turn >= seed.World.Variables.GameTick, "the seed's bank balance, admission fee and park turn are imported" );
		Require( economy.Staff.Members.Count == seed.Staff.Count && economy.Staff.OfType( StaffType.Researcher ).Any(), "the seed's staff, including its researcher, are imported" );
		var capture = CaptureFrame( "instant-action-hud.png" );
		VerifyText( capture, flow.Hud!.MoneyText, "Instant Action HUD bank balance" );
		Require( !economy.AvailableLoans.Any(), "Instant Action offers no loans" );
		Refused( () => economy.TakeLoan( 0 ), "taking a loan" );
		Refused( () => economy.SetResearchEffort( ResearchCategory.Ride, 0 ), "changing research effort" );
		var seeded = economy.Objects.First();
		Require( economy.TryBuyUpgrade( seeded.Id ) == ParkEconomy.PurchaseResult.NotAvailableInInstantAction, "upgrades are refused" );
		var completedItems = economy.Research.Completed.Count;
		var progress = economy.Research.ProgressEntries.ToArray();
		var start = economy.Date;
		economy.AdvanceDays( 60 );
		Require( economy.Research.Completed.Count > completedItems || !economy.Research.ProgressEntries.SequenceEqual( progress ), "research advances through the seed's imported researcher" );
		Require( economy.Objectives.GoldenTickets.Count == 0 && economy.Objectives.Current == null, "no golden-ticket checks or challenges in Instant Action" );
		Log.Trace( $"Instant Action jungle: {level.Park.Import!.ImportedObjects} seed objects, {economy.Staff.Members.Count} staff, research advanced {start} -> {economy.Date}; loans, research effort and upgrades refused." );
		Device.WaitForIdle();
		completed = true;
		Log.Trace( $"Native front-end smoke test passed starting in {startLanguage ?? GameLanguage.Current.Name} and ending in {GameLanguage.Current.Name} (the park language choice applies after the park) at {Screen.PixelSize.X}x{Screen.PixelSize.Y} px, UI scale {Context.Canvas.TextScale}: {frame} frames ({loadFrames} of them waiting for asynchronous loading), lobby + menu readback, mouse/keyboard navigation, options cancel, original jungle via game mode, HUD money/date readback, finance/loans/research window captures and title readback, two distinct catalogue objects bought exactly once through the park economy, info arm, economy pause, sale, overlap refusal, open/close, Escape out of the queue tool, pause menu, exit to lobby, read-only visit build/open/delete/save guards, Instant Action return and Load Park reference start, Instant Action seed with imported bank, turn and staff, research and gates. Overview windows: visitors listed, scrolled, sorted and located; attractions listed, selected, activated and a fixture sold; the developer prototype listed once, activated and removed through its own seam with the selection healed; the window keys V and B pressed through Input.Pressed; the visit's attractions listed, sorted and its delete refused." );
		GameFlow.Quit();
	}

	private static void Refused( Action action, string what )
	{
		try
		{
			action();
		}
		catch ( InvalidOperationException )
		{
			return;
		}
		Require( false, $"{what} is refused in Instant Action" );
	}

	private int stepState;
	// Frames spent waiting for asynchronous loading (park, texture packs); they do not count against MaximumFrames either.
	private int loadFrames;
	private System.Diagnostics.Stopwatch? loadWait;
	/// <summary>Wall-clock allowance of one asynchronous load wait; settable so the budget itself can be tested.</summary>
	internal static TimeSpan LoadAllowance { get; set; } = TimeSpan.FromSeconds( 180 );

	/// <summary>With the pause menu open, an advisor response that is being spoken is paused (<c>GamePauseSound.AdvisorSpeechPaused</c>, tp.exe 0x598960); with no handle (a muted run, no mixer voice) the original pauses nothing either.</summary>
	private void CheckAdvisorSpeechPaused() => steps.Enqueue( ("pause menu pauses advisor speech", () =>
	{
		if ( flow.Advisor?.IsSpeechActive == true && SpeechAudioPlayer.AdvisorHandle != null )
			Require( GamePauseSound.AdvisorSpeechPaused, "the pause menu pauses the advisor's speech (0x598960)" );
		return true;
	}) );

	/// <summary>
	/// Waits for asynchronous loading to finish. Loads run on worker threads, so the frames they take depend on the
	/// machine's load (the smokes are paced at 60 Hz, which makes a frame budget a wall-clock budget). Those frames
	/// are left out of the frame budget, which bounds the test's own progress; the wait gets its own time allowance.
	/// </summary>
	private void WaitForLoad( string name, Func<bool> done ) => steps.Enqueue( (name, () =>
	{
		if ( done() )
		{
			if ( loadWait != null )
				Log.Trace( $"Front-end smoke: '{name}' finished after {loadWait.Elapsed.TotalSeconds:F2} s of waiting." );
			loadWait = null;
			return true;
		}
		loadWait ??= System.Diagnostics.Stopwatch.StartNew();
		Require( loadWait.Elapsed < LoadAllowance, $"'{name}' finishes within {LoadAllowance.TotalSeconds:F0} s of wall-clock time (still loading after {loadWait.Elapsed.TotalSeconds:F1} s and {loadFrames} frames)" );
		loadFrames++;
		return false;
	}) );

	private void Wait( string name, int frames ) => steps.Enqueue( (name, () =>
	{
		if ( waitUntil == 0 )
			waitUntil = frame + frames;
		if ( frame < waitUntil )
			return false;
		waitUntil = 0;
		return true;
	} ) );

	private void Do( string name, Action action ) => steps.Enqueue( (name, () => { action(); return true; }) );

	public void Update()
	{
		if ( afterNextUpdate is { } pending )
		{
			afterNextUpdate = null;
			pending();
		}
		++frame;
		Require( frame - loadFrames < MaximumFrames, $"finish within {MaximumFrames} frames (stuck at '{(steps.Count > 0 ? steps.Peek().Name : "end")}')" );
		while ( steps.Count > 0 && steps.Peek().Step() )
		{
			steps.Dequeue();
			if ( flow.InjectedInput != null )
				break;
		}
	}

	/// <summary>
	/// [EXT:live-language] Options → OpenTPW → language row → OK switches the language at once: the front end and the
	/// reopened options show the other language's text, and switching back restores the first one. Skipped with one language.
	/// </summary>
	private void PlanLanguageSwitch()
	{
		var languages = GameLanguage.Choosable();
		if ( languages.Count < 2 )
		{
			Log.Trace( "Front-end smoke test: language switch skipped, only one language is available." );
			return;
		}
		var first = GameLanguage.Current.Name;
		var index = languages.ToList().FindIndex( name => string.Equals( name, first, StringComparison.OrdinalIgnoreCase ) );
		var other = languages[(index + 1) % languages.Count];
		var firstStrings = flow.Strings;
		foreach ( var (from, to) in new[] { (first, other), (other, first) } )
		{
			Do( $"language {to}: open lobby menu", () =>
			{
				if ( flow.Menu!.Stack.Top?.Name == "lobby" )
					flow.Menu.ShowMenu();
			} );
			Wait( $"language {to}: lobby menu opens", 2 );
			Do( $"language {to}: open options", () =>
			{
				if ( flow.Menu!.Stack.Top?.Name == "lobbyMenu" )
					Click( flow.Menu.Stack.Top, "options" );
			} );
			Wait( $"language {to}: options open", 3 );
			Do( $"language {to}: OpenTPW page", () => Click( flow.Menu!.Stack.Top!, "openTpw" ) );
			Wait( $"language {to}: OpenTPW page opens", 3 );
			// The row cycles forward: one step to the next language, the rest of the way round to come back.
			var clicks = to == other ? 1 : languages.Count - 1;
			for ( var click = 0; click < clicks; click++ )
			{
				Do( $"language {to}: cycle {click + 1}", () =>
				{
					var page = flow.Menu!.Stack.Top!;
					Require( page.Name == "openTpwOptions", "the OpenTPW options page opens" );
					var row = page.Elements.OfType<UiButton>().FirstOrDefault( element => element.Id == "language" );
					Require( row != null, "the OpenTPW page has a language row" );
					flow.InjectedInput = UiInput.Click( row!.ScreenRect( Context.Canvas ).Center );
				} );
				Wait( $"language {to}: row changes {click + 1}", 3 );
			}
			Do( $"language {to}: back to the original page", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
			Wait( $"language {to}: original page", 3 );
			Do( $"language {to}: accept", () =>
			{
				Require( flow.Menu!.Stack.Top?.Name == "options", "Back returns to the original options page" );
				Require( GameLanguage.Current.Name == from, "the language changes only when the options are accepted" );
				Click( flow.Menu.Stack.Top!, "ok" );
			} );
			Wait( $"language {to}: switched", 5 );
			Do( $"language {to}: verify", () =>
			{
				Require( string.Equals( GameLanguage.Current.Name, to, StringComparison.OrdinalIgnoreCase ), $"the language is now {to}" );
				Require( flow.Menu!.Stack.Top?.Name == "options", "the options reopen in the new language" );
				Require( flow.Strings.Language == GameLanguage.Current.Name, "the string table follows the language" );
				var capture = CaptureFrame( $"language-{to.ToLowerInvariant()}.png" );
				VerifyText( capture, flow.Strings[UIStrings.GameOptions], $"Game Options title in {to}" );
				// The tables of the two languages must differ somewhere visible, or this check would pass on a stale table.
				var shown = new[] { UIStrings.GameOptions, UIStrings.Options, UIStrings.ExitToLobby, UIStrings.Paused };
				// US English (American) shares the UK tables word for word, so that pair is told apart by the language alone.
				if ( to == other && !new[] { first, other }.All( name => name is "English" or "American" ) )
					Require( shown.Any( id => flow.Strings[id] != firstStrings[id] ), $"{first} and {to} differ in the checked labels" );
				flow.InjectedInput = UiInput.Key( UiKeys.Back );
			} );
			Wait( $"language {to}: options closed", 3 );
		}
		Do( "language restored", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "lobby", "the options close after the switch back" );
			Require( string.Equals( GameLanguage.Current.Name, first, StringComparison.OrdinalIgnoreCase ), "the first language is back" );
		} );
	}

	/// <summary>
	/// [EXT:live-language] In a park the options keep the language until the park is left, and say so; returns the language
	/// expected back in the lobby (null with one language).
	/// </summary>
	private string? PlanLanguageInPark()
	{
		var languages = GameLanguage.Choosable();
		if ( languages.Count < 2 )
			return null;
		var first = GameLanguage.Current.Name;
		var other = languages[(languages.ToList().FindIndex( name => string.Equals( name, first, StringComparison.OrdinalIgnoreCase ) ) + 1) % languages.Count];
		Do( "park language: options", () => Click( flow.Hud!.Stack.Top!, nameof( UIStrings.Options ) ) );
		Wait( "park language: options open", 3 );
		Do( "park language: OpenTPW page", () => Click( flow.Hud!.Stack.Top!, "openTpw" ) );
		Wait( "park language: OpenTPW page opens", 3 );
		Do( "park language: cycle", () =>
		{
			var row = flow.Hud!.Stack.Top!.Elements.OfType<UiButton>().FirstOrDefault( element => element.Id == "language" );
			Require( row != null, "the OpenTPW page has a language row in the park" );
			flow.InjectedInput = UiInput.Click( row!.ScreenRect( Context.Canvas ).Center );
		} );
		Wait( "park language: row changes", 3 );
		Do( "park language: back", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "park language: original page", 3 );
		Do( "park language: accept", () => Click( flow.Hud!.Stack.Top!, "ok" ) );
		Wait( "park language: notice", 3 );
		Do( "park language: notice shown", () =>
		{
			Require( flow.Hud!.Stack.Top?.Name == "languageAfterPark", "the options say the language follows when the park is left" );
			Require( string.Equals( GameLanguage.Current.Name, first, StringComparison.OrdinalIgnoreCase ), "the park keeps its language" );
			VerifyText( CaptureFrame( "language-after-park.png" ), flow.Strings.Extra( OpenTpwText.LanguageAfterPark ), "language notice" );
			Click( flow.Hud.Stack.Top!, "choice0" );
		} );
		Wait( "park language: notice closed", 3 );
		Do( "park language: pause menu", () =>
		{
			if ( flow.Hud!.Stack.Top?.Name != "pause" )
				flow.Hud.OpenPauseMenu();
		} );
		Wait( "park language: pause menu again", 3 );
		return other;
	}

	private void Click( UiScreen screen, string id )
	{
		var element = screen.Find( id );
		Require( element != null && element.Visible, $"element '{id}' exists on screen '{screen.Name}'" );
		flow.InjectedInput = UiInput.Click( element!.ScreenRect( Context.Canvas ).Center );
	}

	private void ShowItem( BuildItem item )
	{
		var hud = flow.Hud!;
		hud.SelectCategory( item.Category );
		hud.SelectBuyItem( hud.BuyItems.ToList().IndexOf( item ) );
	}

	/// <summary>Clicks the list row of <paramref name="item"/>; a click on the already selected row buys it.</summary>
	private void ClickItem( BuildItem item )
	{
		var hud = flow.Hud!;
		Require( hud.BuyWindowOpen, "the buy window is open" );
		hud.SelectBuyItem( hud.BuyItems.ToList().IndexOf( item ) );
		var visible = hud.SelectedBuyIndex - hud.BuyFirstRow;
		Require( visible >= 0 && visible < ParkHud.BuyVisibleRows, "the requested catalogue item is among the visible rows" );
		flow.InjectedInput = UiInput.Click( Context.Canvas.Map( new System.Numerics.Vector2( 1240, 427 + (visible + 0.5f) * ParkHud.BuyRowHeight ), UiAnchor.Center ) );
	}

	/// <summary>Clicks the attractions-window row of <paramref name="attractionId"/>; a right click locates it.</summary>
	private void ClickAttractionRow( ParkHud hud, int attractionId, bool right = false )
	{
		Require( hud.AttractionsWindowOpen, "the attractions window is open" );
		var index = hud.AttractionItems.ToList().FindIndex( row => row.AttractionId == attractionId );
		var visible = index - hud.AttractionFirstRow;
		Require( index >= 0 && visible >= 0 && visible < hud.AttractionVisibleRows, "the requested attraction is among the visible rows" );
		var point = Context.Canvas.Map( new System.Numerics.Vector2( 1240, hud.AttractionRowsTop + (visible + 0.5f) * hud.AttractionRowHeight ), UiAnchor.Center );
		flow.InjectedInput = right ? UiInput.Idle( point ) with { RightPressed = true } : UiInput.Click( point );
	}

	private static (int X, int Y) FindObjectSite( Level level, ObjectCatalogEntry entry ) =>
		Enumerable.Range( 0, level.Objects.Grid.Width * level.Objects.Grid.Height )
			.Select( index => (X: index % level.Objects.Grid.Width, Y: index / level.Objects.Grid.Width) )
			.First( cell => level.CheckFreeObject( entry, cell.X, cell.Y, level.BuildRotation ) == OriginalPlacementResult.Allowed );

	/// <summary>
	/// Places the developer prototype Totem at the first buildable cell centre: <see cref="Level.PlaceRide"/>
	/// itself refuses overlapping sites, so the first call that succeeds is the site (the park keeps enough
	/// free cells beside its catalogue objects).
	/// </summary>
	private static void PlacePrototypeAtFirstFreeSite( Level level )
	{
		var field = level.OriginalPark!.Heightfield;
		for ( var index = 0; index < field.CellCountX * field.CellCountZ; index++ )
		{
			if ( level.PlaceRide( OriginalParkPlacement.GetCellCenter( field, index % field.CellCountX, index / field.CellCountX ) ) )
				return;
		}
		Require( false, "no free site for the developer prototype" );
	}

	/// <summary>
	/// Finds the glyphs drawn for <paramref name="text"/> in the last UI batch and requires every fully
	/// covered atlas texel to appear in the readback in the text colour (point sampled at integer scale).
	/// </summary>
	private void VerifyText( (byte[] Pixels, int Width, int Height) frameCapture, string text, string what )
	{
		var batch = Context.Batch;
		// The topmost (last drawn) occurrence: a dimmed menu button can carry the same text as a window title.
		var drawn = batch.TextDraws.Where( entry => entry.Text == text ).TakeLast( 1 ).ToList();
		Require( drawn.Count > 0, $"{what} ('{text}') is drawn" );
		var checkedTexels = 0;
		var matching = 0;
		foreach ( var entry in drawn )
		{
			foreach ( var glyph in batch.Glyphs.Skip( entry.FirstGlyph ).Take( entry.GlyphCount ).Where( glyph => glyph.Color.A == 255 ) )
			{
				// The renderer draws the smoothed coverage one texel per pixel (FontSmoothing).
				var factor = FontSmoothing.Factor( glyph.Atlas, glyph.Scale );
				if ( factor != glyph.Scale )
					continue;
				var coverage = FontSmoothing.Coverage( glyph.Atlas, factor );
				var stride = glyph.Atlas.Width * factor;
				for ( var row = 0; row < glyph.Height; row++ )
				{
					for ( var column = 0; column < glyph.Width; column++ )
					{
						if ( coverage[(glyph.AtlasY * factor + row) * stride + glyph.AtlasX * factor + column] != 255 )
							continue;
						var x = glyph.X + column;
						var y = glyph.Y + row;
						if ( x < 0 || y < 0 || x >= frameCapture.Width || y >= frameCapture.Height )
							continue;
						var pixel = (y * frameCapture.Width + x) * 4;
						checkedTexels++;
						if ( Math.Abs( frameCapture.Pixels[pixel] - glyph.Color.B ) <= 2 && Math.Abs( frameCapture.Pixels[pixel + 1] - glyph.Color.G ) <= 2 && Math.Abs( frameCapture.Pixels[pixel + 2] - glyph.Color.R ) <= 2 )
							matching++;
					}
				}
			}
		}
		Log.Trace( $"UI text readback '{text}' ({what}): {matching}/{checkedTexels} opaque glyph pixels match." );
		Require( checkedTexels >= 3 && matching >= checkedTexels * 97 / 100, $"{what} text pixels match in GPU readback" );
	}

	internal static (byte[] Pixels, int Width, int Height) CaptureFrame( string name )
	{
		var source = global::Global.Render.OutputCaptureTexture!;
		using var staging = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D( source.Width, source.Height, 1, 1, source.Format, TextureUsage.Staging ) );
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
			Require( (int)source.Width == Screen.PixelSize.X && (int)source.Height == Screen.PixelSize.Y, "UI readback is at the drawable size" );
			var colors = new HashSet<uint>();
			for ( var pixel = 0; pixel < pixels.Length && colors.Count <= 32; pixel += 4 )
				colors.Add( BitConverter.ToUInt32( pixels, pixel ) );
			Require( colors.Count > 32, $"{name} has image detail" );
			var directory = Path.GetFullPath( "artifacts" );
			Directory.CreateDirectory( directory );
			PngWriter.SaveBgra( Path.Combine( directory, $"native-smoke-{GameLanguage.Current.Name.ToLowerInvariant()}-{name}" ), pixels, (int)source.Width, (int)source.Height );
			return (pixels, (int)source.Width, (int)source.Height);
		}
		finally
		{
			Device.Unmap( staging );
		}
	}

	public void VerifyCompleted() => Require( completed, "complete the front-end smoke test" );

	private static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Native front-end smoke test failed: {step}." );
	}

	public void Dispose()
	{
		global::Global.Render.PostUpdate -= Update;
		SaveFileSystem = originalSaveFileSystem;
		flow.OnlineFolders = originalOnlineFolders;
		try { Directory.Delete( temporaryDirectory, true ); }
		catch ( IOException ) { }
		try { Directory.Delete( smokePacks, true ); }
		catch ( IOException ) { }
		catch ( UnauthorizedAccessException ) { }
	}
}

using System.Runtime.InteropServices;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Native smoke test of the original-style UI (<c>--front-end --smoke-test</c>): the 3D lobby and
/// front-end menu render (GPU readback with BF4 text checked texel by texel), the menu is driven
/// with injected mouse clicks and keys (next island, options open/cancel, enter park, game mode) into
/// the original jungle level, the HUD renders with money/date text verified in readback, a Totem is
/// bought through the catalogue build arm and charged exactly once; a second researched object is
/// built, opened/closed and checked for overlap refusal. The game exits to the lobby, then loads a
/// read-only visit and checks build/open/delete/save mutation boundaries, then starts jungle again in
/// Instant Action and checks its seed and mode gates. Options are never written and saves go to a temporary directory.
/// </summary>
internal sealed class FrontEndSmokeTest : IDisposable
{
	private const int MaximumFrames = 3000;
	private readonly GameFlow flow;
	private readonly BaseFileSystem originalSaveFileSystem;
	private readonly OnlineFolders? originalOnlineFolders;
	private ParkVisitInfo? readOnlyVisit;
	private OriginalObject? visitedObject;
	private bool visitedOpen;
	private int visitedCount;
	private readonly string temporaryDirectory = Path.Combine( Path.GetTempPath(), $"opentpw-frontend-smoke-{Guid.NewGuid():N}" );
	private readonly Queue<(string Name, Func<bool> Step)> steps = new();
	private int frame;
	private int waitUntil;
	private bool completed;
	private long builtCost;
	private bool totemLockedAtStart;
	private long soldFor;
	private int objectsBeforePurchase;
	private BuildItem TotemItem => flow.Hud!.Catalog.GetItems( BuildCategory.Rides ).Single( item => item.InfoId == PrototypeRide.InfoId );
	private OriginalObject? totem;
	private OriginalObject? secondObject;
	private BuildItem? secondItem;
	private int purchaseEvents;

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
		Plan();
	}

	private UiContext Context => flow.Context;

	private readonly string smokePacks = Path.Combine( Path.GetTempPath(), $"opentpw-frontend-smoke-packs-{Guid.NewGuid():N}" );
	private string switchPath = "";
	private (uint Width, uint Height, uint Pixel) switchOriginal;
	private TexturePackSwitch? textureSwitch;

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
			using ( var image = Image.LoadPixelData<Rgba32>( solid, 96, 96 ) )
				image.SaveAsPng( file );
			File.WriteAllText( Path.Combine( pack, TexturePack.ManifestFileName ), System.Text.Json.JsonSerializer.Serialize( new TexturePackManifest { Scale = 1 } ) );
			var diagnostics = new List<string>();
			textureSwitch = TexturePackSwitch.Begin( "smoke", diagnostics, packs );
			Require( textureSwitch != null && diagnostics.Count == 0 && textureSwitch.Total > 0, "the pack switch starts with the loaded textures as its work" );
		} );
		steps.Enqueue( ("texture pack loads", () => { textureSwitch!.Pump(); return textureSwitch.Finished; }) );
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
		steps.Enqueue( ("texture pack unloads", () => { textureSwitch!.Pump(); return textureSwitch.Finished; }) );
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
				steps.Enqueue( ($"real pack {name} loads", () => { textureSwitch!.Pump(); return textureSwitch.Finished; } ) );
				Do( $"real pack {name} done", () => Log.Trace( $"Texture pack switch to '{(pack.Length == 0 ? "original" : pack)}': {textureSwitch!.Total} textures in {timer.Elapsed.TotalSeconds:F1} s over {frame - frames} frames; working set {memoryBefore / 1048576} -> {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576} MB." ) );
			}
		}
	}

	private void Plan()
	{
		Wait( "lobby settles", 30 );
		Do( "lobby renders", () =>
		{
			Require( flow.Lobby != null && flow.Lobby.IslandCount == 4 && flow.Lobby.PartCount > 4, "lobby scene has four islands and terrain" );
			Require( flow.Menu != null && flow.Menu.Islands.Count == 4, "front-end menu lists four theme islands" );
			var capture = CaptureFrame( "frontend.png" );
			VerifyText( capture, flow.Strings[UIStrings.ThemeParkWorld], "front-end title" );
			VerifyText( capture, flow.Menu!.SelectedName, "island name" );
			VerifyText( capture, flow.Strings[UIStrings.QuitGame], "Quit Game button" );
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
		// Original-style online screens (docs/ONLINE.md): Go Online, then each screen, captured offline.
		Do( "go online", () => Click( flow.Menu!.Main, "goOnline" ) );
		Wait( "online world opens", 3 );
		Do( "online world renders", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "onlineWorld", "Go Online opens the online world" );
			VerifyText( CaptureFrame( "online-world.png" ), OnlineStrings.Ui( OnlineStrings.OnlineWorld, "Online world" ), "online world title" );
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
		} );
		var onlineShots = new (string Name, Action<UI.OnlineScreens> Show, string Screen)[]
		{
			("online-find-parks.png", screens => screens.ShowFindParks(), "findParks"),
			("online-send-postcard.png", screens => screens.ShowSendPostcard(), "sendPostcard"),
			("online-outbox.png", screens => screens.ShowOutbox(), "outbox"),
			("online-inbox.png", screens => screens.ShowInbox(), "inbox"),
			("online-chat.png", screens => screens.ShowChat(), "chat"),
			("online-import.png", screens => screens.ShowImport(), "import"),
		};
		foreach ( var shot in onlineShots )
		{
			Do( $"open {shot.Screen}", () => shot.Show( new UI.OnlineScreens( flow.Menu!.Stack, flow.Strings, Context.Models, new UI.OnlineHost { Session = flow.Online } ) ) );
			Wait( $"{shot.Screen} renders", 3 );
			Do( $"{shot.Screen} capture", () =>
			{
				Require( flow.Menu!.Stack.Top?.Name == shot.Screen, $"{shot.Screen} opens" );
				CaptureFrame( shot.Name );
				flow.Menu.Stack.Pop();
			} );
		}
		Do( "leave online", () =>
		{
			while ( flow.Menu!.Stack.Screens.Count > 1 )
				flow.Menu.Stack.Pop();
		} );
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
		PlanTexturePackSwitch();
		Do( "open options", () => Click( flow.Menu!.Main, "options" ) );
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
			flow.Menu.Main.Focus( flow.Menu.Main.Find( "enterPark" ) );
			flow.InjectedInput = UiInput.Key( UiKeys.Accept );
		} );
		Wait( "game mode opens", 3 );
		Do( "choose game mode", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "gameMode", "Enter on the focused enter-park button asks for the game mode" );
			VerifyText( CaptureFrame( "game-mode.png" ), flow.Strings[UIStrings.InstantAction], "Instant Action button" );
			Click( flow.Menu.Stack.Top!, "fullSimulation" );
		} );
		Wait( "park loads", 40 );
		Do( "park loaded", () =>
		{
			Require( flow.Level?.OriginalPark?.LevelName == "jungle" && flow.Hud != null, "menu loads the original jungle level with the HUD" );
			Require( flow.Lobby == null && flow.Menu == null, "front end is torn down" );
			Require( !flow.Level!.ShowDeveloperPanels, "front-end games hide the developer panels" );
			var capture = CaptureFrame( "hud.png" );
			VerifyText( capture, flow.Hud!.MoneyText, "HUD bank balance" );
			VerifyText( capture, flow.Hud.DateText, "HUD date" );
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
			Click( flow.Hud.Screen, "buy" );
		} );
		Wait( "build arm opens", 3 );
		Do( "next build page", () => Click( flow.Hud!.Screen, "nextBuildPage" ) );
		Wait( "next build page visible", 3 );
		Do( "previous build page", () =>
		{
			Require( flow.Hud!.BuildPage == 1, "next page button exposes later catalogue items" );
			Click( flow.Hud.Screen, "previousBuildPage" );
		} );
		Wait( "previous build page visible", 3 );
		Do( "show Totem page", () =>
		{
			Require( flow.Hud!.BuildPage == 0, "previous page button returns to the first page" );
			ShowItem( TotemItem );
		} );
		Wait( "Totem page visible", 3 );
		Do( "build arm", () =>
		{
			Require( flow.Hud!.BuildArmOpen, "buy button opens the build arm" );
			Require( flow.Hud.Catalog is OriginalBuildCatalog && flow.Hud.BuildPageCount > 1, "the original catalogue spans build-menu pages" );
			var capture = CaptureFrame( "build-arm.png" );
			VerifyText( capture, flow.Strings[UIStrings.BuyRide], "Buy Ride title" );
			VerifyText( capture, flow.Hud.ItemName( TotemItem ), "Totem build item" );
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
			Require( !flow.Hud.BuildArmOpen, "build arm closes after building" );
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
		Wait( "ride sold", 3 );
		Do( "ride deleted", () =>
		{
			Require( totem!.IsDeleted && soldFor > 0 && flow.Hud!.Status.Money == flow.Level!.Park!.Economy.Balance, $"deleting the ride sells it for its scrap value (got {soldFor})" );
			Require( flow.Level!.Park!.Economy.Objects.Count == objectsBeforePurchase, "selling removes the purchased economy object" );
			Log.Trace( $"HUD delete sold the Totem for {soldFor}; balance {flow.Level.Park.Economy.Balance}." );
			flow.Hud!.SetBuildArm( true );
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
		Do( "closed state", () =>
		{
			Require( !secondObject!.IsOpen, "HUD door closes the selected original object" );
			flow.InjectedInput = UiInput.Key( UiKeys.Back );
		} );
		Wait( "pause opens", 3 );
		Do( "pause menu", () =>
		{
			Require( flow.Hud!.Paused, "Escape opens the pause menu" );
			var capture = CaptureFrame( "pause.png" );
			VerifyText( capture, flow.Strings[UIStrings.Paused], "PAUSED title" );
			VerifyText( capture, flow.Strings[UIStrings.ExitToLobby], "Exit To Lobby" );
			Click( flow.Hud.Stack.Top!, nameof( UIStrings.Save ) );
		} );
		Wait( "save refused", 3 );
		Do( "save refused", () =>
		{
			Require( !flow.Hud!.Paused && flow.Hud.Messages.Contains( flow.Strings.Extra( OpenTpwText.OriginalParkReadOnly ) ), "original parks report that they cannot be saved" );
			flow.Hud.OpenPauseMenu();
		} );
		Wait( "pause again", 3 );
		Do( "exit to lobby", () => Click( flow.Hud!.Stack.Top!, nameof( UIStrings.ExitToLobby ) ) );
		Wait( "lobby again", 20 );
		Do( "back in lobby", () =>
		{
			Require( flow.Level == null && flow.Menu != null && flow.Lobby != null, "Exit To Lobby returns to the front end" );
			Require( flow.Menu!.Selected.Level == "jungle", "lobby returns to the island of the park" );
			CaptureFrame( "frontend-return.png" );
			flow.StartPark( "jungle", GameMode.InstantAction );
		} );
		Wait( "instant action starts", 3 );
		Do( "instant action mode before lobby", () =>
		{
			Require( flow.StartKind == ParkStartKind.InstantAction && flow.Level!.Park!.Economy.Mode == ParkGameMode.InstantAction,
				"selected Instant Action reaches the park economy" );
			flow.ShowFrontEnd( "jungle" );
		} );
		Wait( "instant action returns to lobby", 3 );
		Do( "load original park after Instant Action", () => flow.LoadPark( new ParkLoadEntry( "jungle", 0, false ) ) );
		Wait( "original park loads", 3 );
		Do( "loaded park is the reference start", () =>
		{
			Require( flow.StartKind == ParkStartKind.OriginalSaveReference && flow.Level!.Park!.Economy.Mode == ParkGameMode.FullSimulation
				&& flow.Level.Park.Economy.Settings.IsEasy && flow.Level.OriginalPark!.Save != null,
				"Load Park opens the shipped save as the reference start whatever mode was chosen before (UI-041)" );
			var park = OriginalPark.Load( "jungle" );
			var snapshot = ParkSnapshotBuilder.FromOriginal( park, null );
			readOnlyVisit = ParkSharing.PrepareVisit( ParkSharing.CreatePackage( snapshot, "HUD read-only smoke", "", "OpenTPW", park.Map ) );
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
			visitedOpen = visitedObject.IsOpen;
			hud.BeginPlacing( hud.Catalog.GetItems( BuildCategory.Rides ).First() );
			Require( level.BuildEntry == null && !level.IsPlacing, "read-only HUD refuses direct placement requests" );
			Require( hud.Screen.Find( "buy" ) is UiButton { Enabled: false }, "build control is disabled while visiting" );
			hud.SelectObject( visitedObject );
		} );
		Wait( "read-only info renders", 3 );
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
			var save = (UiButton)hud.Stack.Top!.Find( nameof( UIStrings.Save ) )!;
			Require( !save.Enabled, "visit save control is disabled" );
			save.Clicked!();
			Require( hud.Messages.Contains( OnlineStrings.Get( OnlineLabel.ReadOnlyVisit ) ) && !Directory.EnumerateFiles( temporaryDirectory, "*.json", SearchOption.AllDirectories ).Any(), "read-only save callback refuses writes" );
			CaptureFrame( "read-only-visit.png" );
			Click( hud.Stack.Top!, nameof( UIStrings.ExitToLobby ) );
		} );
		Wait( "lobby after visit", 20 );
		Do( "enter park again", () =>
		{
			Require( flow.Level == null && flow.Menu?.Selected.Level == "jungle", "the visit exits to the jungle island" );
			Click( flow.Menu!.Main, "enterPark" );
		} );
		Wait( "game mode again", 3 );
		Do( "choose Instant Action", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "gameMode", "enter park asks for the game mode again" );
			Click( flow.Menu.Stack.Top!, "instantAction" );
		} );
		Wait( "Instant Action park loads", 40 );
		Do( "Instant Action start", PlayInstantAction );
	}

	/// <summary>
	/// The Instant Action start from the menu: Easy_ balance and the shipped jungle seed, no staff member
	/// invented for the seed's undecoded researcher but research by its ECON-019 stand-in, and the loan,
	/// research effort, upgrade, ticket and challenge gates (docs/ECONOMY.md, "Game modes").
	/// </summary>
	private void PlayInstantAction()
	{
		var level = flow.Level!;
		var economy = level.Park!.Economy;
		Require( flow.StartKind == ParkStartKind.InstantAction && economy.Mode == ParkGameMode.InstantAction && economy.Features == ParkModeFeatures.For( ParkGameMode.InstantAction ), "Instant Action button starts an Instant Action park" );
		Require( economy.Settings.IsEasy && level.OriginalPark!.Save != null && level.Park.Import is { ImportedObjects: > 0 }, "Instant Action jungle uses the Easy_ balance and imports the Easymode seed" );
		Require( economy.Staff.Members.Count == 0 && economy.SeedResearcherStandIn, "the seed's staff are not decoded: no staff member is invented, research uses the ECON-019 stand-in" );
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
		Require( economy.Research.Completed.Count > completedItems || !economy.Research.ProgressEntries.SequenceEqual( progress ), "research advances through the seed researcher's stand-in" );
		Require( economy.Objectives.GoldenTickets.Count == 0 && economy.Objectives.Current == null, "no golden-ticket checks or challenges in Instant Action" );
		Log.Trace( $"Instant Action jungle: {level.Park.Import!.ImportedObjects} seed objects, {economy.Staff.Members.Count} staff, research advanced {start} -> {economy.Date}; loans, research effort and upgrades refused." );
		Device.WaitForIdle();
		completed = true;
		Log.Trace( $"Native front-end smoke test passed in {GameLanguage.Current.Name} at {Screen.PixelSize.X}x{Screen.PixelSize.Y} px, UI scale {Context.Canvas.TextScale}: {frame} frames, lobby + menu readback, mouse/keyboard navigation, options cancel, original jungle via game mode, HUD money/date readback, two distinct catalogue objects bought exactly once through the park economy, info arm, economy pause, sale, overlap refusal, open/close, pause menu, exit to lobby, read-only visit build/open/delete/save guards, Instant Action return and Load Park reference start, Instant Action seed, research stand-in and gates." );
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
		++frame;
		Require( frame < MaximumFrames, $"finish within {MaximumFrames} frames (stuck at '{(steps.Count > 0 ? steps.Peek().Name : "end")}')" );
		while ( steps.Count > 0 && steps.Peek().Step() )
		{
			steps.Dequeue();
			if ( flow.InjectedInput != null )
				break;
		}
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
		var index = hud.Catalog.GetItems( item.Category ).ToList().IndexOf( item );
		hud.ChangeBuildPage( index / 3 );
	}

	private void ClickItem( BuildItem item )
	{
		var index = flow.Hud!.VisibleBuildItems.ToList().IndexOf( item );
		Require( index >= 0, "requested catalogue item is on the visible page" );
		Click( flow.Hud.Screen, $"item{index}" );
	}

	private static (int X, int Y) FindObjectSite( Level level, ObjectCatalogEntry entry ) =>
		Enumerable.Range( 0, level.Objects.Grid.Width * level.Objects.Grid.Height )
			.Select( index => (X: index % level.Objects.Grid.Width, Y: index / level.Objects.Grid.Width) )
			.First( cell => level.CheckObject( entry, cell.X, cell.Y, level.BuildRotation ) == OriginalPlacementResult.Allowed );

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
			foreach ( var glyph in batch.Glyphs.Skip( entry.FirstGlyph ).Take( entry.GlyphCount ).Where( glyph => glyph.Color.A == 255 && glyph.Color != UiColors.Shadow ) )
			{
				for ( var row = 0; row < glyph.Height / glyph.Scale; row++ )
				{
					for ( var column = 0; column < glyph.Width / glyph.Scale; column++ )
					{
						if ( glyph.Atlas.Alpha[(glyph.AtlasY + row) * glyph.Atlas.Width + glyph.AtlasX + column] != 255 )
							continue;
						// Every output pixel of the texel's scale×scale block (pixel-exact integer scaling).
						var inside = true;
						var exact = true;
						for ( var dy = 0; dy < glyph.Scale; dy++ )
						{
							for ( var dx = 0; dx < glyph.Scale; dx++ )
							{
								var x = glyph.X + column * glyph.Scale + dx;
								var y = glyph.Y + row * glyph.Scale + dy;
								if ( x < 0 || y < 0 || x >= frameCapture.Width || y >= frameCapture.Height )
								{
									inside = false;
									continue;
								}
								var pixel = (y * frameCapture.Width + x) * 4;
								exact &= Math.Abs( frameCapture.Pixels[pixel] - glyph.Color.B ) <= 2 && Math.Abs( frameCapture.Pixels[pixel + 1] - glyph.Color.G ) <= 2 && Math.Abs( frameCapture.Pixels[pixel + 2] - glyph.Color.R ) <= 2;
							}
						}
						if ( !inside )
							continue;
						checkedTexels++;
						if ( exact )
							matching++;
					}
				}
			}
		}
		Log.Trace( $"UI text readback '{text}' ({what}): {matching}/{checkedTexels} opaque glyph texels match." );
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
			using var image = Image.LoadPixelData<Bgra32>( pixels, (int)source.Width, (int)source.Height );
			var directory = Path.GetFullPath( "artifacts" );
			Directory.CreateDirectory( directory );
			image.SaveAsPng( Path.Combine( directory, $"native-smoke-{GameLanguage.Current.Name.ToLowerInvariant()}-{name}" ) );
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

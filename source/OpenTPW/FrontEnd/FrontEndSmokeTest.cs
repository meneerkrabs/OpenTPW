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
/// bought through the build arm and charged, its info arm shows, the pause menu opens and the game
/// exits to the lobby. Options are never written and saves go to a temporary directory.
/// </summary>
internal sealed class FrontEndSmokeTest : IDisposable
{
	private const int MaximumFrames = 3000;
	private readonly GameFlow flow;
	private readonly BaseFileSystem originalSaveFileSystem;
	private readonly string temporaryDirectory = Path.Combine( Path.GetTempPath(), $"opentpw-frontend-smoke-{Guid.NewGuid():N}" );
	private readonly Queue<(string Name, Func<bool> Step)> steps = new();
	private int frame;
	private int waitUntil;
	private bool completed;
	private long builtCost;
	private bool totemLockedAtStart;
	private long soldFor;
	private int objectsBeforePurchase;
	private static readonly BuildItem TotemItem = new TotemBuildCatalog().GetItems( BuildCategory.Rides ).Single();

	public FrontEndSmokeTest( GameFlow flow )
	{
		this.flow = flow;
		flow.PersistSettings = false;
		originalSaveFileSystem = SaveFileSystem;
		Directory.CreateDirectory( temporaryDirectory );
		SaveFileSystem = new BaseFileSystem( temporaryDirectory );
		flow.OptionsPath = Path.Combine( temporaryDirectory, GameOptions.FileName );
		// Read back the composed output (world after scaling + UI at drawable pixels).
		global::Global.Render.CaptureOutput = true;
		Plan();
	}

	private UiContext Context => flow.Context;

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
		Do( "open options", () => Click( flow.Menu!.Main, "options" ) );
		Wait( "options open", 3 );
		Do( "options render", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "options", "options screen opens" );
			var effects = GameOptions.Current.SoundEffectsVolume;
			Click( flow.Menu.Stack.Top!, "effects" );
			stepState = effects;
		} );
		Wait( "volume changes", 3 );
		Do( "options capture", () =>
		{
			Require( GameOptions.Current.SoundEffectsVolume != stepState, "clicking a volume row changes it" );
			var capture = CaptureFrame( "options.png" );
			VerifyText( capture, flow.Strings[UIStrings.GameOptions], "Game Options title" );
			VerifyText( capture, flow.Strings[UIStrings.ScreenResolution], "screen resolution row" );
			VerifyText( capture, flow.Strings.Extra( OpenTpwText.Upscaling ), "OpenTPW upscaling row" );
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
			objectsBeforePurchase = economy.Objects.Count;
			Require( flow.Hud.Status is EconomyParkStatus && flow.Hud.Status.Money == economy.Balance, "HUD bank balance is the park economy's" );
			Require( flow.Hud.DateText == string.Format( flow.Strings.Extra( OpenTpwText.DateFormat ), economy.Date.Year, economy.Date.Month, economy.Date.Day ), "HUD date is the park clock" );
			economy.EventRaised += item =>
			{
				if ( item.Kind == ParkEventKind.ObjectBuilt && item.InfoId == PrototypeRide.InfoId )
					builtCost = item.Amount;
			};
			Click( flow.Hud.Screen, "buy" );
		} );
		Wait( "build arm opens", 3 );
		Do( "build arm", () =>
		{
			Require( flow.Hud!.BuildArmOpen, "buy button opens the build arm" );
			var capture = CaptureFrame( "build-arm.png" );
			VerifyText( capture, flow.Strings[UIStrings.BuyRide], "Buy Ride title" );
			VerifyText( capture, flow.Strings.Object( TotemBuildCatalog.ObjectNameIndex ), "Totem build item" );
			totemLockedAtStart = !flow.Hud.Status.IsAvailable( TotemItem );
			Click( flow.Hud.Screen, "item0" );
		} );
		Wait( "item chosen", 3 );
		Do( "research gate", () =>
		{
			if ( !totemLockedAtStart )
				return;
			// The economy has not researched the Totem yet: the HUD must refuse it. Test setup then marks
			// it researched (as if research finished) so the purchase path can be exercised.
			Require( !flow.Level!.IsPlacing && flow.Hud!.Messages.Contains( flow.Strings.Extra( OpenTpwText.NotAvailable ) ), "an unresearched item cannot be placed" );
			var research = flow.Level.Park!.Economy.Research;
			research.Restore( research.Completed.Append( (PrototypeRide.InfoId, 0) ).Distinct().ToArray(), research.ProgressEntries.ToArray(), research.Effort );
			Require( flow.Hud!.Status.IsAvailable( TotemItem ), "Totem available once researched" );
			Log.Trace( "Front-end smoke: the Totem was not researched at the start; research marked complete for the purchase test." );
			Click( flow.Hud.Screen, "item0" );
		} );
		Wait( "placing", 3 );
		Do( "place Totem", () =>
		{
			Require( flow.Level!.IsPlacing, "choosing the Totem starts placement" );
			Require( flow.Level.PlaceRide( FindSite( flow.Level ) ), "Totem placed on a buildable cell" );
		} );
		Wait( "purchase booked", 3 );
		Do( "purchase charged", () =>
		{
			var economy = flow.Level!.Park!.Economy;
			var price = flow.Hud!.Status.PriceOf( TotemItem );
			Require( builtCost > 0 && builtCost == price, $"the Totem is bought through ParkEconomy.TryBuild at its catalogue price (built {builtCost}, price {price})" );
			Require( flow.Hud.Status.Money == economy.Balance && flow.Level.PlacedRide != null, "HUD balance follows the economy after the purchase" );
			Require( economy.Objects.Count == objectsBeforePurchase + 1, "the charged ride replaces its uncharged placeholder" );
			Require( flow.Level.Park.Guests!.TryGetInstance( flow.Level.PlacedRide!.Visitors.AttractionId, out var linked ) && economy.TryGetObject( linked, out var bought ) && bought.TotalSpent == builtCost, "guest payments link to the purchased ride" );
			Log.Trace( $"HUD money: {economy.Balance} after buying the Totem for {builtCost}; park date {economy.Date}." );
			Require( !flow.Hud.BuildArmOpen, "build arm closes after building" );
			flow.Hud.SelectPlacedRide();
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
			Require( flow.Level!.PlacedRide == null && soldFor > 0 && flow.Hud!.Status.Money == flow.Level.Park!.Economy.Balance, $"deleting the ride sells it for its scrap value (got {soldFor})" );
			Require( flow.Level.Park!.Economy.Objects.Count == objectsBeforePurchase, "selling removes the purchased economy object" );
			Log.Trace( $"HUD delete sold the Totem for {soldFor}; balance {flow.Level.Park.Economy.Balance}." );
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
			Device.WaitForIdle();
			completed = true;
			Log.Trace( $"Native front-end smoke test passed in {GameLanguage.Current.Name} at {Screen.PixelSize.X}x{Screen.PixelSize.Y} px, UI scale {Context.Canvas.TextScale}: {frame} frames, lobby + menu readback, mouse/keyboard navigation, options cancel, original jungle via game mode, HUD money/date readback, Totem bought through the park economy, info arm, economy pause, sale, pause menu, exit to lobby." );
			GameFlow.Quit();
		} );
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

	private static Vector3 FindSite( Level level )
	{
		var park = level.OriginalPark!;
		var field = park.Heightfield;
		var cells = Enumerable.Range( 0, field.CellCountX * field.CellCountZ ).Select( index => (X: index % field.CellCountX, Y: index / field.CellCountX) ).ToArray();
		var start = cells.Where( cell => park.Map.GetFlagsAt( cell.X, cell.Y ).HasFlag( MapCellFlags.InitialPath ) ).DefaultIfEmpty( (X: field.CellCountX / 2, Y: field.CellCountZ / 2) ).First();
		var site = cells.OrderBy( cell => Math.Abs( cell.X - start.X ) + Math.Abs( cell.Y - start.Y ) )
			.First( cell => level.CheckOriginalPlacement( OriginalParkPlacement.GetCellCenter( field, cell.X, cell.Y ) ) == OriginalPlacementResult.Allowed );
		return OriginalParkPlacement.GetCellCenter( field, site.X, site.Y );
	}

	/// <summary>
	/// Finds the glyphs drawn for <paramref name="text"/> in the last UI batch and requires every fully
	/// covered atlas texel to appear in the readback in the text colour (point sampled at integer scale).
	/// </summary>
	private void VerifyText( (byte[] Pixels, int Width, int Height) frameCapture, string text, string what )
	{
		var batch = Context.Batch;
		// The topmost (last drawn) occurrence: a dimmed menu button can carry the same text as a window title.
		var drawn = batch.Texts.Where( entry => entry.Text == text ).TakeLast( 1 ).ToList();
		Require( drawn.Count > 0, $"{what} ('{text}') is drawn" );
		var checkedTexels = 0;
		var matching = 0;
		foreach ( var (rect, _) in drawn )
		{
			foreach ( var glyph in batch.Glyphs.Where( glyph => glyph.Color.A == 255 && glyph.Color != UiColors.Shadow && Overlaps( rect, glyph ) ) )
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

	private static bool Overlaps( UiRect rect, UiGlyphQuad glyph ) =>
		glyph.X < rect.Right + 2 && glyph.X + glyph.Width > rect.X - 2 && glyph.Y < rect.Bottom + 2 && glyph.Y + glyph.Height > rect.Y - 2;

	private static (byte[] Pixels, int Width, int Height) CaptureFrame( string name )
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
		try { Directory.Delete( temporaryDirectory, true ); }
		catch ( IOException ) { }
	}
}

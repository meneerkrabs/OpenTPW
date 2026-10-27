using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's park windows send only what its player asked for: the object window's apply on its close, the research workload knob's projection on the window's
/// opening and on a staffing change, and the loans window's offer refresh on its opening and its tabs would each be a request the guest's rights refuse, with a note about a click the player
/// never made. A shop's or a sideshow's apply sends only the commands whose values its player changed, and a typed price or an apply-all, which go when clicked, do not go again at the
/// close.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>
	/// A shop or a sideshow of the test catalogue on <paramref name="park"/>, placed and linked to its economy record as a placed one is, before the session starts: the drinks shop (1203) or
	/// the jungle spray (1303), with <paramref name="settings"/> added to its type's.
	/// </summary>
	private OriginalObject AddPriced( Park park, bool shop, int x, string settings = "" )
	{
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", NonPublic )!.GetValue( fixture )!;
		var (info, category, ui) = shop ? (1203, ObjectCategory.Shop, BuildCategory.Shops) : (1303, ObjectCategory.Sideshow, BuildCategory.Sideshows);
		var entry = new ObjectCatalogEntry( "jungle", category, $"/priced{x}", new ObjectSettings( new[] { new ObjectSettingsFile(
				$"Info.Id {info}\nInfo.Name SyntheticPriced{x}\nInfo.WhichUIType {(int)ui}\nInfo.IsChoosable 1\n{settings}", $"priced{x}.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", System.Array.Empty<string>(), System.Array.Empty<ObjectAnimationFile>(), System.Array.Empty<string>(),
			System.Array.Empty<string>(), new BaseFileSystem( root ), null );
		var placed = park.Level.Objects.TryPlace( entry, x, 9, 0, out _ )!;
		Assert.IsNotNull( placed );
		var state = park.Economy.RegisterExisting( info );
		typeof( Level ).GetMethod( "LinkEconomy", NonPublic )!.Invoke( park.Level, new object[] { placed, state.Id } );
		return placed;
	}

	/// <summary>A lab with a scientist, hired alike on every peer before the session starts: the research window opens only with one (tp.exe 0x4aa480, UITEXT 0x1d3).</summary>
	private static void Staffed( Park park, bool host ) => EconomyTestData.HireBest( park.Economy, StaffType.Researcher );

	/// <summary>The guest's view of a priced object: its window opened and closed as a click and the HUD's next frame do it.</summary>
	private static (System.Action Open, System.Action Close) Window( View view, OriginalObject item ) =>
		(() => { view.Hud.SelectObject( item ); view.Update(); }, () => { view.Hud.SelectObject( null ); view.Update(); });

	[TestMethod]
	public void AGuestsWindowsSendNothingItsPlayerDidNotAskFor()
	{
		var (host, guest, _, _) = Shared( Staffed );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		guest.Enter();
		var hud = view.Hud;
		var refusals = guest.Level.SessionRefusals;
		// the ride's window opened and closed
		hud.SelectObject( guest.One );
		view.Update();
		hud.SelectObject( null );
		view.Update();
		// the research window opened, its staffing changed, and closed
		hud.OpenResearchWindow();
		view.Update();
		var knob = (ParkHud.ResearchWorkloadKnob)hud.Stack.Top!.Find( "workloadKnob" )!;
		// a staffing change reprojects the workload onto a knob that stands elsewhere
		typeof( ParkHud.ResearchWorkloadKnob ).GetField( "ability", NonPublic )!.SetValue( knob, -1 );
		typeof( ParkHud.ResearchWorkloadKnob ).GetProperty( nameof( ParkHud.ResearchWorkloadKnob.Value ), NonPublic | System.Reflection.BindingFlags.Public )!
			.SetValue( knob, knob.Value + 1 );
		view.Update();
		hud.CloseResearchWindow();
		view.Update();
		// the loans window opened, both tabs clicked, and closed
		hud.OpenLoansWindow();
		view.Update();
		foreach ( var tab in new[] { "outstanding", "available" } )
			((UiButton)hud.Stack.Top!.Find( tab )!).Activate();
		view.Update();
		hud.CloseLoansWindow();
		view.Update();
		Assert.AreEqual( refusals, guest.Level.SessionRefusals, "no command went, so nothing was refused" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) + view.Says( OpenTpwText.CoopNotPermitted ) );
		// a change the player made goes, and the guest has no right to make it (C4b: the attractions right is not offered yet)
		hud.SelectObject( guest.One );
		view.Update();
		hud.PendingSettings.Ride = hud.PendingSettings.Ride with { Speed = hud.PendingSettings.Ride.Speed + 1 };
		hud.SelectObject( null );
		view.Update();
		Assert.AreEqual( refusals + 1, guest.Level.SessionRefusals, "the changed ride's apply" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsShopWindowSendsOnlyTheCommandsOfWhatItsPlayerChangedAndNothingTwice()
	{
		OriginalObject? shop = null;
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var placed = AddPriced( park, shop: true, 20 );
			if ( !isHost )
				shop = placed;
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		guest.Enter();
		var hud = view.Hud;
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( view.Strings, new UiFonts( _ => font ), new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) )
		{ Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = _ => null };
		void Give( UiInput input )
		{
			guest.Enter();
			hud.Update( context, input, false, false, false );
		}
		var refusals = guest.Level.SessionRefusals;
		int Sent() => guest.Level.SessionRefusals - refusals;
		var (open, close) = Window( view, shop! );
		// opened and closed: nothing
		open();
		close();
		Assert.AreEqual( 0, Sent(), "nothing changed" );
		// the quality alone: its settings' command, not the price's
		open();
		hud.PendingSettings.QualityStep = (hud.PendingSettings.QualityStep + 1) % 3;
		close();
		Assert.AreEqual( 1, Sent(), "the settings' command alone" );
		// the price alone (the price box's steps): the price's command, not the settings'
		open();
		hud.PendingSettings.Price = (hud.PendingSettings.Price + 1) % ParkHud.MaximumPrice;
		close();
		Assert.AreEqual( 2, Sent(), "the price's command alone" );
		// a typed price goes when typed, and not again at the close (the price action is the park view's; the text field's focus sets the process's text entry, which the game's frame
		// clears and a test must put back)
		var (camera, typing) = (Camera.Mode, Input.TextEntryActive);
		Camera.Mode = new ParkCameraMode();
		try
		{
			open();
			var price = (UiButton)hud.Screen.Find( "shopPrice" )!;
			Assert.IsTrue( price.Visible && price.Enabled, "the price action" );
			hud.Screen.Focus( price );
			Give( UiInput.Key( UiKeys.Accept ) );
			Assert.AreEqual( "shopPrice", hud.Stack.Top!.Name );
			Give( UiInput.Type( $"{(hud.PendingSettings.Price + 7) % ParkHud.MaximumPrice}", 10 ) );
			Give( UiInput.Key( UiKeys.Accept ) );
			Assert.AreEqual( "hud", hud.Stack.Top!.Name, "the editor took the price" );
			Assert.AreEqual( 3, Sent(), "the typed price's command" );
			close();
			Assert.AreEqual( 3, Sent(), "and not again at the close" );
		}
		finally
		{
			(Camera.Mode, Input.TextEntryActive) = (camera, typing);
		}
		// apply-all sends the window's values to every shop of the type, and the close nothing more
		open();
		hud.PendingSettings.Ingredient = (hud.PendingSettings.Ingredient + 10) % 100;
		((UiButton)hud.Screen.Find( "applyAll" )!).Activate();
		Assert.AreEqual( 5, Sent(), "the settings' and the price's command of the one shop" );
		close();
		Assert.AreEqual( 5, Sent(), "nothing again at the close" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsSideshowWindowsSendTheirOneCommandOnlyWhenItsPlayerChangedItsValues()
	{
		OriginalObject? winnable = null, prizeless = null;
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var game = AddPriced( park, shop: false, 20 );
			var spray = AddPriced( park, shop: false, 23, "UsageInfo.NonWinnableSideshow 1\n" );
			if ( !isHost )
				(winnable, prizeless) = (game, spray);
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		guest.Enter();
		var pending = view.Hud.PendingSettings;
		var refusals = guest.Level.SessionRefusals;
		int Sent() => guest.Level.SessionRefusals - refusals;
		// a game: its price, its prize and its chance go in one command, sent when any of them changed
		var (open, close) = Window( view, winnable! );
		open();
		close();
		Assert.AreEqual( 0, Sent(), "nothing changed" );
		foreach ( var (change, what) in new (System.Action, string)[]
		{
			(() => pending.Price = (pending.Price + 1) % ParkHud.MaximumPrice, "the price"),
			(() => pending.Prize = (pending.Prize + 1) % ParkHud.MaximumPrize, "the prize"),
			(() => pending.WinChance = (pending.WinChance + 1) % 100, "the chance")
		} )
		{
			var before = Sent();
			open();
			change();
			close();
			Assert.AreEqual( before + 1, Sent(), $"{what}: the game's one command" );
		}
		// a sideshow without a prize: its price alone
		(open, close) = Window( view, prizeless! );
		open();
		close();
		Assert.AreEqual( 3, Sent(), "nothing changed" );
		open();
		pending.Price = (pending.Price + 1) % ParkHud.MaximumPrice;
		close();
		Assert.AreEqual( 4, Sent(), "the price's command" );
		open();
		pending.Prize = (pending.Prize + 1) % ParkHud.MaximumPrize;
		close();
		Assert.AreEqual( 4, Sent(), "a prize it does not use sends nothing" );
		CatchUpAndCompare( host, guest );
	}
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's park windows send only what its player asked for: the object window's apply on its close, the research workload knob's projection on the window's
/// opening and on a staffing change, and the loans window's offer refresh on its opening and its tabs would each be a request the guest's rights refuse, with a note about a click the player
/// never made. A shop's apply sends only the commands whose values its player changed, and a typed price or an apply-all, which go when clicked, do not go again at the close.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>A shop of the test catalogue's drinks shop (1203) on <paramref name="park"/>, placed and linked to its economy record as a placed shop is, before the session starts.</summary>
	private OriginalObject AddShop( Park park )
	{
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", NonPublic )!.GetValue( fixture )!;
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Shop, "/shop", new ObjectSettings( new[] { new ObjectSettingsFile(
				"Info.Id 1203\nInfo.Name SyntheticShop\nInfo.WhichUIType 1\nInfo.IsChoosable 1\n", "shop.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", System.Array.Empty<string>(), System.Array.Empty<ObjectAnimationFile>(), System.Array.Empty<string>(),
			System.Array.Empty<string>(), new BaseFileSystem( root ), null );
		var shop = park.Level.Objects.TryPlace( entry, 20, 9, 0, out _ )!;
		Assert.IsNotNull( shop );
		var state = park.Economy.RegisterExisting( 1203 );
		typeof( Level ).GetMethod( "LinkEconomy", NonPublic )!.Invoke( park.Level, new object[] { shop, state.Id } );
		return shop;
	}

	[TestMethod]
	public void AGuestsWindowsSendNothingItsPlayerDidNotAskFor()
	{
		var (host, guest, _, _) = Shared();
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
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) );
		// a change the player made goes, and only the host may make it
		hud.SelectObject( guest.One );
		view.Update();
		hud.PendingSettings.Ride = hud.PendingSettings.Ride with { Speed = hud.PendingSettings.Ride.Speed + 1 };
		hud.SelectObject( null );
		view.Update();
		Assert.AreEqual( refusals + 1, guest.Level.SessionRefusals, "the changed ride's apply" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopHostOnly ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsShopWindowSendsOnlyTheCommandsOfWhatItsPlayerChangedAndNothingTwice()
	{
		OriginalObject? shop = null;
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var placed = AddShop( park );
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
		void Input( UiInput input )
		{
			guest.Enter();
			hud.Update( context, input, false, false, false );
		}
		var refusals = guest.Level.SessionRefusals;
		int Sent() => guest.Level.SessionRefusals - refusals;
		void Open()
		{
			hud.SelectObject( shop );
			view.Update();
		}
		void Close()
		{
			hud.SelectObject( null );
			view.Update();
		}
		// opened and closed: nothing
		Open();
		Close();
		Assert.AreEqual( 0, Sent(), "nothing changed" );
		// the quality alone: its settings' command, not the price's
		Open();
		hud.PendingSettings.QualityStep = (hud.PendingSettings.QualityStep + 1) % 3;
		Close();
		Assert.AreEqual( 1, Sent(), "the settings' command alone" );
		// the price alone (the price box's steps): the price's command, not the settings'
		Open();
		hud.PendingSettings.Price = (hud.PendingSettings.Price + 1) % ParkHud.MaximumPrice;
		Close();
		Assert.AreEqual( 2, Sent(), "the price's command alone" );
		// a typed price goes when typed, and not again at the close (the price action is the park view's)
		var camera = Camera.Mode;
		Camera.Mode = new ParkCameraMode();
		try
		{
			Open();
			var price = (UiButton)hud.Screen.Find( "shopPrice" )!;
			Assert.IsTrue( price.Visible && price.Enabled, "the price action" );
			hud.Screen.Focus( price );
			Input( UiInput.Key( UiKeys.Accept ) );
			Assert.AreEqual( "shopPrice", hud.Stack.Top!.Name );
			Input( UiInput.Type( $"{(hud.PendingSettings.Price + 7) % ParkHud.MaximumPrice}", 10 ) );
			Input( UiInput.Key( UiKeys.Accept ) );
			Assert.AreEqual( "hud", hud.Stack.Top!.Name, "the editor took the price" );
			Assert.AreEqual( 3, Sent(), "the typed price's command" );
			Close();
			Assert.AreEqual( 3, Sent(), "and not again at the close" );
		}
		finally
		{
			Camera.Mode = camera;
		}
		// apply-all sends the window's values to every shop of the type, and the close nothing more
		Open();
		hud.PendingSettings.Ingredient = (hud.PendingSettings.Ingredient + 10) % 100;
		((UiButton)hud.Screen.Find( "applyAll" )!).Activate();
		Assert.AreEqual( 5, Sent(), "the settings' and the price's command of the one shop" );
		Close();
		Assert.AreEqual( 5, Sent(), "nothing again at the close" );
		CatchUpAndCompare( host, guest );
	}
}

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's park windows send only what its player asked for: the research workload knob's projection on the window's opening and on a staffing change and the
/// loans window's offer refresh on its opening and its tabs send nothing, and the object window keeps no draft (C4b-2): each change of its controls goes at once as a click, which the
/// session's rights or the queue's room may refuse before anything changes, its controls draw the drawn values, and its close and retarget send nothing.
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

	/// <summary>The guest's object window's slider <paramref name="id"/>.</summary>
	private static UiSlider WindowSlider( View view, string id ) => (UiSlider)view.Hud.Screen.Find( id )!;

	/// <summary>A click on the guest's object window's button <paramref name="id"/>.</summary>
	private static void WindowClick( View view, string id ) => ((UiButton)view.Hud.Screen.Find( id )!).Activate();

	/// <summary>The command of the guest's latest entry of <paramref name="knob"/> on attraction <paramref name="attraction"/>, unsent or open, or null.</summary>
	private static ParkCommand? WindowEntry( Park guest, int attraction, ManagementKnob knob ) =>
		Management( guest ).PendingCommand( ManagementTarget.Attraction( attraction ), knob );

	/// <summary>The economy record of the placed attraction <paramref name="attraction"/> on <paramref name="park"/>.</summary>
	private static ParkObjectState StateOf( Park park, int attraction )
	{
		park.Enter();
		var item = park.Level.Objects.Objects.Single( placed => !placed.IsDeleted && placed.Runtime.AttractionId == attraction );
		Assert.IsTrue( park.Economy.TryGetObject( park.Level.GetEconomyInstance( item )!.Value, out var state ) );
		return state;
	}

	/// <summary><paramref name="commands"/> as <paramref name="seat"/> issued them (a logged command carries its player).</summary>
	private static ParkCommand[] IssuedBy( int seat, params ParkCommand[] commands ) => commands.Select( command => command with { Player = seat } ).ToArray();

	/// <summary>
	/// Runs <paramref name="body"/> with the park camera (the typed price editor is the park view's action) and puts the camera and the process's text entry back after it (the editor's
	/// field sets the text entry, which the game's frame clears and a test must put back).
	/// </summary>
	private static void WithPriceEditor( System.Action body )
	{
		var (camera, typing) = (Camera.Mode, Input.TextEntryActive);
		Camera.Mode = new ParkCameraMode();
		try
		{
			body();
		}
		finally
		{
			(Camera.Mode, Input.TextEntryActive) = (camera, typing);
		}
	}

	/// <summary>Opens the typed price editor from the shown window's price box, as its click does; returns its number field.</summary>
	private static UiTextField OpenPriceEditor( View view )
	{
		var hud = view.Hud;
		// a closed editor's field left the text entry on, which the game's frame clears
		Input.TextEntryActive = false;
		view.Update();
		var price = (UiButton)hud.Screen.Find( "shopPrice" )!;
		Assert.IsTrue( price.Visible && price.Enabled, "the price action" );
		hud.Screen.Focus( price );
		view.Input( UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( "shopPrice", hud.Stack.Top!.Name, "the editor opened" );
		return (UiTextField)hud.Stack.Top.Find( "price" )!;
	}

	/// <summary>Types <paramref name="price"/> into the open editor in place of its text and applies it.</summary>
	private static void TypePrice( View view, int price )
	{
		view.Input( UiInput.Type( $"{price}", 10 ) );
		view.Input( UiInput.Key( UiKeys.Accept ) );
	}

	[TestMethod]
	public void AGuestsWindowsSendNothingItsPlayerDidNotAskFor()
	{
		var (host, guest, _, _) = Shared( Staffed );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		guest.Enter();
		var hud = view.Hud;
		var before = Numbered( guest );
		// the ride's window opened, moved on by the arrows and closed
		hud.SelectObject( guest.One );
		view.Update();
		hud.CycleSelectedObject( 1 );
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
		Assert.AreEqual( before, Numbered( guest ), "nothing went" );
		Assert.AreEqual( (0, 0, 0), (Management( guest ).Pending.Count(), Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals), "nothing queued, refused or said" );
		// a change the player made goes at its click, and without the attractions right it is refused there: the control returns, the rights' note waits until the window's lines can be
		// seen, and the window's close sends nothing
		hud.SelectObject( guest.One );
		view.Update();
		var speed = WindowSlider( view, "rideSpeed" );
		var drawn = speed.Value();
		speed.Changed!( drawn + 1 < speed.Steps() ? drawn + 1 : drawn - 1 );
		Assert.AreEqual( drawn, speed.Value(), "the control returned at once" );
		Assert.AreEqual( new ManagementNote( ManagementNoteKind.Rights, CoopRefusal.NotPermitted ), Management( guest ).PeekNote()?.Note );
		view.Update();
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopNotPermitted ), "not said while the object window hides the lines" );
		hud.SelectObject( null );
		view.Update();
		Assert.AreEqual( (1, before, 0), (view.Says( OpenTpwText.CoopNotPermitted ), Numbered( guest ), guest.Level.SessionRefusals),
			"said once the window closed; nothing sent, and no refusal of the session's immediate path" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsWindowSendsEachChangeAtOnceDrawsItWhileItWaitsAndItsCloseAndArrowsSendNothing()
	{
		OriginalObject? shop = null, other = null;
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var first = AddPriced( park, shop: true, 20, "UsageInfo.SpecialIngredient 2\n" );
			var second = AddPriced( park, shop: true, 23, "UsageInfo.SpecialIngredient 2\n" );
			if ( !isHost )
				(shop, other) = (first, second);
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Attractions );
		var hud = view.Hud;
		var (id, otherId) = (shop!.Runtime.AttractionId, other!.Runtime.AttractionId);
		var parked = StateOf( guest, id );
		var (ingredient0, price0) = (parked.IngredientAmount, parked.Price);
		Assert.IsTrue( price0 < ParkHud.MaximumPrice );
		var (before, refusals) = (Numbered( guest ), guest.Level.SessionRefusals);
		var (open, close) = Window( view, shop );
		open();
		// the quality's step: its group at once, the ingredient as drawn
		var quality = WindowSlider( view, "shopQuality" );
		var step = (quality.Value() + 1) % 3;
		quality.Changed!( step );
		Assert.AreEqual( new SetShopSettingsCommand( id, step * 50, ingredient0 ), WindowEntry( guest, id, ManagementKnob.ShopSettings ) );
		view.Update();
		Assert.AreEqual( step, quality.Value(), "the control draws it while it waits" );
		// a drag of the ingredient in one frame: its steps replace the group's unsent entry at the target's tail, so one entry goes with the latest values
		var ingredient = WindowSlider( view, "shopIngredient" );
		foreach ( var value in new[] { 10, 11, 12 } )
			ingredient.Changed!( value );
		Assert.AreEqual( 1, Management( guest ).Pending.Count() );
		Assert.AreEqual( new SetShopSettingsCommand( id, step * 50, 12 ), WindowEntry( guest, id, ManagementKnob.ShopSettings ) );
		// the price box's click: the price's group, after the settings
		WindowClick( view, "pricePlus" );
		Assert.AreEqual( new SetPriceCommand( id, price0 + 1 ), WindowEntry( guest, id, ManagementKnob.Price ) );
		CollectionAssert.AreEqual( new ParkCommand[] { new SetShopSettingsCommand( id, step * 50, 12 ), new SetPriceCommand( id, price0 + 1 ) },
			Management( guest ).Pending.Select( entry => entry.Command ).ToArray(), "in click order" );
		// closed and opened again: the window draws what waits
		close();
		open();
		Assert.AreEqual( (step, 12, price0 + 1), (quality.Value(), ingredient.Value(), hud.PendingSettings.Price), "the reopened window draws the drawn values" );
		// the arrows' next object draws its own values, and the close sends nothing
		hud.CycleSelectedObject( 1 );
		view.Update();
		Assert.AreSame( other, hud.SelectedObject );
		var otherState = StateOf( guest, otherId );
		Assert.AreEqual( ((otherState.QualityOfGoods & 0xFF) / 50, otherState.IngredientAmount & 0xFF, otherState.Price),
			(hud.PendingSettings.QualityStep, hud.PendingSettings.Ingredient, hud.PendingSettings.Price), "the next shop's own values" );
		close();
		Assert.AreEqual( (2, before), (Management( guest ).Pending.Count(), Numbered( guest )), "nothing more queued, nothing sent before the pump" );
		UntilManaged( host, guest, view );
		Assert.AreEqual( (before + 2, refusals), (Numbered( guest ), guest.Level.SessionRefusals), "two requests, at their clicks" );
		foreach ( var park in new[] { host, guest } )
		{
			var state = StateOf( park, id );
			Assert.AreEqual( (step * 50, 12, price0 + 1), (state.QualityOfGoods, state.IngredientAmount, state.Price), park == host ? "the host's shop" : "the guest's shop" );
		}
		CollectionAssert.AreEqual( IssuedBy( guest.Level.Commands.Player, new SetShopSettingsCommand( id, step * 50, 12 ), new SetPriceCommand( id, price0 + 1 ) ),
			HostCommandsOf( host, guest.Level.Commands.Player ) );
		// the host's window still applies its values on close, as the original's
		var hostView = ViewOf( host );
		var hostShop = host.Level.Objects.Objects.Single( placed => placed.Runtime.AttractionId == id );
		hostView.Hud.SelectObject( hostShop );
		hostView.Update();
		hostView.Hud.PendingSettings.Price = price0 + 5;
		hostView.Hud.SelectObject( null );
		hostView.Update();
		Assert.AreEqual( price0 + 5, StateOf( host, id ).Price, "the host's close applied it" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsTypedPriceGoesWhenAppliedAndARefusalKeepsTheEditorOpenWithItsNote()
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
		GiveRights( host, guest, view, CoopRights.Attractions );
		var hud = view.Hud;
		var id = shop!.Runtime.AttractionId;
		var price0 = StateOf( guest, id ).Price;
		var before = Numbered( guest );
		var (open, close) = Window( view, shop );
		WithPriceEditor( () =>
		{
			open();
			// the box's click first: the editor opens with the drawn price
			WindowClick( view, "pricePlus" );
			Assert.AreEqual( $"{price0 + 1}", OpenPriceEditor( view ).Text );
			// the typed price replaces the box's unsent entry (the same group at the target's tail) and the editor closes
			TypePrice( view, price0 + 7 );
			Assert.AreEqual( "hud", hud.Stack.Top!.Name, "queued: the editor closed" );
			Assert.AreEqual( new SetPriceCommand( id, price0 + 7 ), WindowEntry( guest, id, ManagementKnob.Price ) );
			Assert.AreEqual( (1, price0 + 7), (Management( guest ).Pending.Count(), hud.PendingSettings.Price) );
			close();
			UntilManaged( host, guest, view );
			Assert.AreEqual( (price0 + 7, price0 + 7, before + 1), (StateOf( host, id ).Price, StateOf( guest, id ).Price, Numbered( guest )) );
			// without the right: refused at the click, the editor stays open with the rights' note, nothing queued; the window draws the park's price again
			GiveRights( host, guest, view, CoopRights.Finance );
			open();
			OpenPriceEditor( view );
			TypePrice( view, price0 + 9 );
			Assert.AreEqual( "shopPrice", hud.Stack.Top!.Name, "refused: the editor stays open" );
			Assert.AreEqual( view.Strings.Extra( OpenTpwText.CoopNotPermitted ), ((UiLabel)hud.Stack.Top.Find( "error" )!).Text() );
			Assert.AreEqual( (0, price0 + 7), (Management( guest ).Pending.Count(), hud.PendingSettings.Price) );
			view.Escape();
			Assert.AreEqual( "hud", hud.Stack.Top!.Name );
			Assert.AreEqual( 0, view.Says( OpenTpwText.CoopNotPermitted ), "the editor said it; the lines wait for the window" );
			close();
			Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ), "then the session's note" );
		} );
		Rounds( 30, host, guest );
		Assert.AreEqual( (before + 1, 0), (Numbered( guest ), guest.Level.SessionRefusals) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AWinnableSideshowsTypedPriceGoesAsItsGameAndTheLatestAskedValuesWin()
	{
		OriginalObject? game = null;
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var placed = AddPriced( park, shop: false, 20 );
			if ( !isHost )
				game = placed;
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Attractions );
		var hud = view.Hud;
		var id = game!.Runtime.AttractionId;
		var parked = StateOf( guest, id );
		var (price0, prize0, losing0) = (parked.Price, parked.CostOfGoods, parked.ChanceOfLosingPercent);
		var (open, close) = Window( view, game );
		WithPriceEditor( () =>
		{
			open();
			// the prize box's click goes as the game's group, and is sent
			WindowClick( view, "prizePlus" );
			Assert.AreEqual( new SetGamePrizeCommand( id, price0, prize0 + 1, losing0 ), WindowEntry( guest, id, ManagementKnob.Game ) );
			SendNext( guest );
			Assert.AreEqual( ManagementEntryState.Open, Management( guest ).Pending.Single().State );
			// the typed price while it is open: the game's group again, with the drawn prize and chance, after it
			OpenPriceEditor( view );
			TypePrice( view, price0 + 4 );
			Assert.AreEqual( "hud", hud.Stack.Top!.Name );
			CollectionAssert.AreEqual( new ParkCommand[] { new SetGamePrizeCommand( id, price0, prize0 + 1, losing0 ), new SetGamePrizeCommand( id, price0 + 4, prize0 + 1, losing0 ) },
				Management( guest ).Pending.Select( entry => entry.Command ).ToArray() );
			// the right taken back before the first's record: it is refused there, and the window keeps the latest values asked (the second's), with no revert
			Assert.IsTrue( host.Issue( new SetSeatRightsCommand( guest.Level.Commands.Player, CoopRights.None ) ).Accepted );
			for ( var round = 0; round < 600 && Management( guest ).Pending.Count() > 1; round++ )
				Rounds( 1, host, guest );
			Assert.AreEqual( 1, Management( guest ).Pending.Count(), "the first resolved" );
			view.Update();
			Assert.AreEqual( (price0 + 4, prize0 + 1), (hud.PendingSettings.Price, hud.PendingSettings.Prize), "the latest asked values" );
			// the right back, then the prize's next click: it replaces the typed price's unsent entry with the drawn price
			Assert.IsTrue( host.Issue( new SetSeatRightsCommand( guest.Level.Commands.Player, CoopRights.Attractions ) ).Accepted );
			for ( var round = 0; round < 600 && guest.Level.RightsOf( guest.Level.Commands.Player ) != CoopRights.Attractions; round++ )
				Rounds( 1, host, guest );
			WindowClick( view, "prizePlus" );
			Assert.AreEqual( new SetGamePrizeCommand( id, price0 + 4, prize0 + 2, losing0 ), Management( guest ).Pending.Single().Command );
			close();
		} );
		UntilManaged( host, guest, view );
		foreach ( var park in new[] { host, guest } )
		{
			var state = StateOf( park, id );
			Assert.AreEqual( (price0 + 4, prize0 + 2, losing0), (state.Price, state.CostOfGoods, state.ChanceOfLosingPercent), park == host ? "the host's game" : "the guest's game" );
		}
		CollectionAssert.AreEqual( IssuedBy( guest.Level.Commands.Player, new SetGamePrizeCommand( id, price0, prize0 + 1, losing0 ), new SetGamePrizeCommand( id, price0 + 4, prize0 + 2, losing0 ) ),
			HostCommandsOf( host, guest.Level.Commands.Player ), "the first refused at its record, the second accepted" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void APrizelessSideshowsPriceBoxAndTypedPriceSendItsPriceAlone()
	{
		OriginalObject? spray = null;
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var placed = AddPriced( park, shop: false, 23, "UsageInfo.NonWinnableSideshow 1\n" );
			if ( !isHost )
				spray = placed;
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Attractions );
		var hud = view.Hud;
		var id = spray!.Runtime.AttractionId;
		var (price0, prize0) = (StateOf( guest, id ).Price, StateOf( guest, id ).CostOfGoods);
		var (open, close) = Window( view, spray );
		WithPriceEditor( () =>
		{
			open();
			WindowClick( view, "pricePlus" );
			Assert.AreEqual( new SetPriceCommand( id, price0 + 1 ), WindowEntry( guest, id, ManagementKnob.Price ) );
			Assert.IsNull( WindowEntry( guest, id, ManagementKnob.Game ) );
			SendNext( guest );
			OpenPriceEditor( view );
			TypePrice( view, price0 + 3 );
			CollectionAssert.AreEqual( new ParkCommand[] { new SetPriceCommand( id, price0 + 1 ), new SetPriceCommand( id, price0 + 3 ) },
				Management( guest ).Pending.Select( entry => entry.Command ).ToArray() );
			close();
		} );
		UntilManaged( host, guest, view );
		Assert.AreEqual( (price0 + 3, prize0, price0 + 3), (StateOf( host, id ).Price, StateOf( host, id ).CostOfGoods, StateOf( guest, id ).Price) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsApplyAllGoesWholeOnePerGroupAndObjectInTheObjectsOrder()
	{
		OriginalObject? first = null, second = null;
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var one = AddPriced( park, shop: true, 20 );
			var two = AddPriced( park, shop: true, 23 );
			if ( !isHost )
				(first, second) = (one, two);
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Attractions );
		var hud = view.Hud;
		var (one, two) = (first!.Runtime.AttractionId, second!.Runtime.AttractionId);
		var (open, close) = Window( view, second );
		open();
		var step = (hud.PendingSettings.QualityStep + 1) % 3;
		WindowSlider( view, "shopQuality" ).Changed!( step );
		var (ingredient, price) = (hud.PendingSettings.Ingredient, hud.PendingSettings.Price);
		WindowClick( view, "applyAll" );
		CollectionAssert.AreEqual( new ParkCommand[]
		{
			new SetShopSettingsCommand( two, step * 50, ingredient ),
			new SetShopSettingsCommand( one, step * 50, ingredient ), new SetPriceCommand( one, price ),
			new SetPriceCommand( two, price )
		}, Management( guest ).Pending.Select( entry => entry.Command ).ToArray(), "the change, then the apply-all's entries per object, the shown one's settings coalescing with its change" );
		close();
		UntilManaged( host, guest, view );
		foreach ( var park in new[] { host, guest } )
			foreach ( var attraction in new[] { one, two } )
			{
				var state = StateOf( park, attraction );
				Assert.AreEqual( (step * 50, ingredient, price), (state.QualityOfGoods, state.IngredientAmount, state.Price) );
			}
		CatchUpAndCompare( host, guest );
	}
}

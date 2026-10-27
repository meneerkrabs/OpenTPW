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

	/// <summary>The guest's queue's entry of <paramref name="knob"/> of <paramref name="item"/>'s target, unsent or open, or null.</summary>
	private static ParkCommand? WindowEntry( Park guest, OriginalObject item, ManagementKnob knob ) =>
		Management( guest ).PendingCommand( ManagementTarget.Attraction( item.Runtime.AttractionId ), knob );

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
		// a change the player made goes at its click, and only the host may make it here: refused, the control returns and the window's close sends nothing more
		hud.SelectObject( guest.One );
		view.Update();
		var speed = WindowSlider( view, "rideSpeed" );
		var drawn = speed.Value();
		speed.Changed!( drawn + 1 );
		Assert.AreEqual( drawn, speed.Value(), "the control returned at once" );
		Assert.AreEqual( new ManagementNote( ManagementNoteKind.Rights, CoopRefusal.NotPermitted ), Management( guest ).PeekNote()?.Note );
		view.Update();
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopNotPermitted ), "not said while the object window hides the lines" );
		hud.SelectObject( null );
		view.Update();
		Assert.AreEqual( (1, before), (view.Says( OpenTpwText.CoopNotPermitted ), Numbered( guest )), "said once the window closed, and nothing sent" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsWindowSendsEachChangeAtOnceAndItsCloseAndRetargetSendNothing()
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
		var before = Numbered( guest );
		var (open, close) = Window( view, shop! );
		open();
		// the quality's step: its group at once, the window drawing it while it waits
		var quality = WindowSlider( view, "shopQuality" );
		var step = (quality.Value() + 1) % 3;
		quality.Changed!( step );
		var ingredient = hud.PendingSettings.Ingredient;
		Assert.AreEqual( new SetShopSettingsCommand( shop!.Runtime.AttractionId, step * 50, ingredient ), WindowEntry( guest, shop, ManagementKnob.ShopSettings ) );
		view.Update();
		Assert.AreEqual( step, quality.Value(), "drawn while it waits" );
		// a drag of the ingredient in one frame: one entry with its latest value, after the quality's (the same group's run at the target's tail)
		var drag = WindowSlider( view, "shopIngredient" );
		foreach ( var value in new[] { 10, 11, 12 } )
			drag.Changed!( value );
		Assert.AreEqual( 1, Management( guest ).Pending.Count( entry => entry.Target == ManagementTarget.Attraction( shop.Runtime.AttractionId ) ) );
		Assert.AreEqual( new SetShopSettingsCommand( shop.Runtime.AttractionId, step * 50, 12 ), WindowEntry( guest, shop, ManagementKnob.ShopSettings ) );
		// the price box's click: the price's group, behind the settings
		var price = hud.PendingSettings.Price;
		((UiButton)hud.Screen.Find( "pricePlus" )!).Clicked!();
		Assert.AreEqual( new SetPriceCommand( shop.Runtime.AttractionId, price + 1 ), WindowEntry( guest, shop, ManagementKnob.Price ) );
		// the arrows move on and the window closes: nothing more goes, and nothing is lost
		hud.CycleSelectedObject( 1 );
		view.Update();
		close();
		Assert.AreEqual( 2, Management( guest ).Pending.Count(), "the settings and the price" );
		UntilManaged( host, guest, view );
		Assert.AreEqual( before + 2, Numbered( guest ), "two requests, at their clicks" );
		foreach ( var park in new[] { host, guest } )
		{
			var state = park.Economy.Objects.Single( item => item.Id == park.Level.GetEconomyInstance( park == guest ? shop : park.Level.Objects.Objects.First( placed => placed.Runtime.AttractionId == shop.Runtime.AttractionId ) ) );
			Assert.AreEqual( (step * 50, 12, price + 1), (state.QualityOfGoods, state.IngredientAmount, state.Price), $"{(park == host ? "the host's" : "the guest's")} shop" );
		}
		// the other shop's window draws its own values, untouched
		Window( view, other! ).Open();
		Assert.AreEqual( 50 / 50, hud.PendingSettings.QualityStep );
		Window( view, other ).Close();
		CatchUpAndCompare( host, guest );
	}
}

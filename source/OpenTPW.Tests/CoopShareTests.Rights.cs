using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-permissions] The Build Together screen's rights (C4b; docs/COOP.md "Money and rights (C4)"): a guest's row names the rights the shared park gives it, and its dialog has a
/// switch per offered right that draws and turns over the park's applied bit; the dialog closes, sending nothing, once its member left that seat or its share ended.
/// </summary>
public partial class CoopShareTests
{
	[TestMethod]
	public async Task TheRightsDialogTurnsEachOfferedRightOverAndClosesOnceItsMemberOrItsShareIsGone() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		var other = await players.Add( server, "Other" );
		await CoopLinkTests.Open( guest, other );
		var everyone = new[] { host, guest, other };
		var (level, _) = NewPark();
		var share = Share( level, host );
		var (screens, stack) = Screens( host.Session, new Hosting( () => share ) );
		screens.ShowBuildTogether();
		await Frames( share, () => share.Stage == CoopShareStage.Hosting, "the park to be hosted", everyone );
		Assert.IsTrue( guest.Link.Join( share.State!.Code ) );
		await Frames( share, () => share.State!.Members.Count == 2, "the guest's join", everyone );
		Assert.IsTrue( other.Link.Join( share.State!.Code ) );
		await Frames( share, () => share.State!.Members.Count == 3, "the other's join", everyone );
		Update( stack );
		var seat = share.State!.Members.Single( member => member.Name == "Guest" ).Slot;
		var row = share.State.Members.OrderBy( member => member.Slot ).ToList().FindIndex( member => member.Name == "Guest" );
		// the host's own row has no rights button; a guest's names none yet
		Assert.IsFalse( Element<UiButton>( stack, "rights0" ).Visible );
		Assert.AreEqual( English( OpenTpwText.CoopWatches ), Element<UiButton>( stack, $"rights{row}" ).Text!() );
		Element<UiButton>( stack, $"rights{row}" ).Clicked!();
		Assert.AreEqual( UI.OnlineScreens.SeatRightsScreenName, stack.Top!.Name );
		Assert.AreEqual( string.Format( English( OpenTpwText.CoopRightsTitle ), "Guest" ), Element<UiLabel>( stack, "title" ).Text!() );
		CollectionAssert.AreEqual( new[] { "rightBuild", "rightAttractions", "rightStaff", "rightResearch", "rightFinance" },
			stack.Top.Elements.OfType<UiButton>().Select( button => button.Id ).Where( id => id.StartsWith( "right" ) ).ToArray(), "one switch per offered right" );
		// each switch draws the park's applied bit and turns it over
		foreach ( var right in CoopRightsOffer.Each )
		{
			var button = Element<UiButton>( stack, $"right{right}" );
			Assert.IsFalse( button.Selected!(), $"{right} not given yet" );
			button.Clicked!();
			Assert.IsTrue( level.RightsOf( seat ).HasFlag( right ), $"{right} given" );
			Assert.IsTrue( button.Selected!() );
		}
		Element<UiButton>( stack, "rightResearch" ).Clicked!();
		Element<UiButton>( stack, "rightAttractions" ).Clicked!();
		Assert.AreEqual( CoopRights.Build | CoopRights.Staff | CoopRights.Finance, level.RightsOf( seat ), "research and the attractions taken back alone" );
		// closed, the row names the rights given
		Element<UiButton>( stack, "back" ).Clicked!();
		Assert.AreEqual( UI.OnlineScreens.BuildTogetherScreenName, stack.Top!.Name );
		Update( stack );
		Assert.AreEqual( $"{English( OpenTpwText.CoopRightBuilding )}, {English( OpenTpwText.CoopRightStaff )}, {English( OpenTpwText.CoopRightFinance )}",
			Element<UiButton>( stack, $"rights{row}" ).Text!() );
		// a member that left closes its dialog, which sends nothing more
		Element<UiButton>( stack, $"rights{row}" ).Clicked!();
		Assert.AreEqual( UI.OnlineScreens.SeatRightsScreenName, stack.Top!.Name );
		var dialog = stack.Top;
		Assert.IsTrue( guest.Link.Leave( "The guest went home." ) );
		await Frames( share, () => share.State!.Members.Count == 2, "the guest's leave", everyone );
		Update( stack );
		Assert.AreEqual( UI.OnlineScreens.BuildTogetherScreenName, stack.Top!.Name, "the member's dialog closed" );
		var log = level.Commands.Log.Count( entry => entry.Command is SetSeatRightsCommand );
		((UiButton)dialog.Find( "rightFinance" )!).Clicked!();
		Assert.AreEqual( log, level.Commands.Log.Count( entry => entry.Command is SetSeatRightsCommand ), "a closed dialog's switch sends nothing" );
		// a share that ended closes the dialog of a member still in it
		Update( stack );
		var otherRow = share.State!.Members.OrderBy( member => member.Slot ).ToList().FindIndex( member => member.Name == "Other" );
		Element<UiButton>( stack, $"rights{otherRow}" ).Clicked!();
		Assert.AreEqual( UI.OnlineScreens.SeatRightsScreenName, stack.Top!.Name );
		share.End();
		Update( stack );
		Assert.AreEqual( UI.OnlineScreens.BuildTogetherScreenName, stack.Top!.Name, "the ended share's dialog closed" );
	} );
}

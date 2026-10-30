using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's management queue across a resync (C4b-1, docs/COOP.md "A guest's management windows"): the queue is the session's, so its open entries settle against the
/// swapped-in park's cut, its unsent ones wait through the resync and are asked against the new park's rights, and what was queued meanwhile goes once after the swap.
/// </summary>
public partial class CoopJoinSessionTests
{
	[TestMethod]
	public void AGuestsManagementEntriesSettleAgainstTheResyncsCutAndTheRestGoOnceAgainstTheNewPark()
	{
		var guest = Hosted( 1 ).Single();
		Build( 120, 64 );
		var seat = guest.Link.Slot;
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.Finance | CoopRights.Research ) ).Accepted );
		RoundsUntil( () => guest.Level!.RightsOf( seat ) == (CoopRights.Finance | CoopRights.Research), "the rights reached the guest" );
		var queue = guest.Session!.Management!;
		var waiter = queue.NewWaiter();
		int FromSeat<T>( System.Func<T, bool>? matches = null ) where T : ParkCommand =>
			host.Level!.Commands.Log.Count( entry => entry.Command is T command && command.Player == seat && (matches == null || matches( command )) );
		// an accepted fee, a take of an offer no bank makes (refused at its record) and an effort, whose records the guest never gets: a resync follows
		guest.Link.DropFrame = _ => true;
		var fee = new SetEntranceFeeCommand( 37 );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Finance, ManagementKnob.Fee, fee ) }, waiter ).Queued );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Loans, null, new TakeLoanCommand( 7, 1 ) ) }, waiter ).Queued );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Research, ManagementKnob.Effort( ResearchCategory.Shop ),
			new SetResearchEffortCommand( ResearchCategory.Shop, 21 ) ) } ).Queued );
		RoundsUntil( () => FromSeat<SetEntranceFeeCommand>() == 1 && FromSeat<TakeLoanCommand>() == 1 && FromSeat<SetResearchEffortCommand>() == 1, "the host applied the three" );
		Assert.AreEqual( 3, queue.OpenCount, "their records never applied in the old park" );
		// behind the open ones: a workload whose right the host takes back before the cut, and a refresh unsent on the frame the stream stops
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Research, ManagementKnob.Workload, new SetResearchWorkloadCommand( 90 ) ) } ).Queued );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Loans, ManagementKnob.Refresh, new RefreshLoanOffersCommand() ) } ).Queued );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.Finance ) ).Accepted );
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
		// a fee queued during the resync goes after it, once
		var later = new SetEntranceFeeCommand( 41 );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Finance, ManagementKnob.Fee, later ) } ).Queued );
		var refreshes = FromSeat<RefreshLoanOffersCommand>();
		Rounds( 30 );
		Assert.AreEqual( (0, refreshes), (FromSeat<SetEntranceFeeCommand>( command => command == later with { Player = seat } ), FromSeat<RefreshLoanOffersCommand>()),
			"nothing goes during the resync" );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && !queue.Pending.Any(), "the park was swapped in and every entry settled" );
		// the open ones' records are inside the cut: their outcomes are not known
		var outcomes = queue.TakeOutcomes( waiter );
		Assert.AreEqual( 2, outcomes.Count );
		Assert.IsTrue( outcomes.All( outcome => outcome.Kind == ManagementOutcomeKind.Unknown ), "the fee and the take: not known" );
		// the workload is asked against the new park, which no longer gives research: refused at the send, its note among the session's notes after the effort's, and never through the
		// session's immediate path (C4b-2)
		CollectionAssert.AreEqual( new[] { new ManagementNote( ManagementNoteKind.Unknown ), new ManagementNote( ManagementNoteKind.Rights, CoopRefusal.NotPermitted ) },
			queue.TakeNotes().Select( outcome => outcome.Note ).ToArray(), "the effort's note, then the workload's" );
		Assert.AreEqual( 0, FromSeat<SetResearchWorkloadCommand>() );
		Assert.AreEqual( 0, guest.Level!.SessionRefusals );
		// the refresh and the later fee went once each
		Assert.AreEqual( refreshes + 1, FromSeat<RefreshLoanOffersCommand>() );
		Assert.AreEqual( 1, FromSeat<SetEntranceFeeCommand>( command => command == later with { Player = seat } ) );
		Assert.AreEqual( 1, FromSeat<SetEntranceFeeCommand>( command => command == fee with { Player = seat } ), "the fee inside the cut applied once" );
		Build( 60, 65 );
		CatchUpAndCompare( guest );
	}

	/// <summary>A drinks shop (the test catalogue's 1203) placed at <paramref name="x"/> and linked to its economy record as a placed one is, alike on every park this class builds.</summary>
	private static void AddShop( Level level, UpgradeCatalogueDefaultsTests fixture, int x )
	{
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( fixture )!;
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Shop, $"/priced{x}", new ObjectSettings( new[] { new ObjectSettingsFile(
				$"Info.Id 1203\nInfo.Name SyntheticPriced{x}\nInfo.WhichUIType {(int)BuildCategory.Shops}\nInfo.IsChoosable 1\nUsageInfo.SpecialIngredient 2\n", $"priced{x}.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(),
			Array.Empty<string>(), new BaseFileSystem( root ), null );
		var placed = level.Objects.TryPlace( entry, x, 9, 0, out _ )!;
		Assert.IsNotNull( placed );
		var state = level.Park!.Economy.RegisterExisting( 1203 );
		typeof( Level ).GetMethod( "LinkEconomy", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, new object[] { placed, state.Id } );
	}

	/// <summary>A guest of a session whose parks hold a shop, with the attractions right: the guest, the shop's attraction id and its economy record on the host.</summary>
	private (Peer Guest, int Shop, ParkObjectState State) GuestOfAShop()
	{
		prepareParks = level => AddShop( level, fixture!, 20 );
		var guest = Hosted( 1 ).Single();
		Build( 120, 64 );
		var seat = guest.Link.Slot;
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.Attractions ) ).Accepted );
		RoundsUntil( () => guest.Level!.RightsOf( seat ) == CoopRights.Attractions, "the right reached the guest" );
		host.Enter();
		var item = host.Level!.Objects.Objects.Single( placed => placed.Entry.Category == ObjectCategory.Shop );
		Assert.IsTrue( host.Economy.TryGetObject( host.Level.GetEconomyInstance( item )!.Value, out var state ) );
		return (guest, item.Runtime.AttractionId, state);
	}

	[TestMethod]
	public void AResyncCutBetweenAShopsSettingsAndItsPriceLeavesTheSettingsNotKnownAndSendsThePriceOnceAfterTheSwap()
	{
		var (guest, shop, state) = GuestOfAShop();
		var seat = guest.Link.Slot;
		var queue = guest.Session!.Management!;
		var waiter = queue.NewWaiter();
		var (quality, price) = (state.QualityOfGoods == 100 ? 50 : 100, state.Price + 2);
		var (settings, priced) = (new SetShopSettingsCommand( shop, quality, state.IngredientAmount ), new SetPriceCommand( shop, price ));
		IEnumerable<ParkCommand> FromSeat() => host.Level!.Commands.Log.Where( entry => entry.Command.Player == seat && entry.Command is SetShopSettingsCommand or SetPriceCommand ).Select( entry => entry.Command );
		// the guest's two control entries of one target: the host applies the settings, its record never reaches the guest, and the stream stops before the price is sent
		guest.Link.DropFrame = _ => true;
		var target = ManagementTarget.Attraction( shop );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( target, ManagementKnob.ShopSettings, settings ) }, waiter ).Queued );
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( target, ManagementKnob.Price, priced ) }, waiter ).Queued );
		RoundsUntil( () => FromSeat().Any(), "the host applied the settings" );
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
		CollectionAssert.AreEqual( new ParkCommand[] { settings with { Player = seat } }, FromSeat().ToArray(), "the cut falls between the two: the price was never sent" );
		Assert.AreEqual( 2, queue.Pending.Count(), "the settings are open and the price unsent" );
		Rounds( 30 );
		Assert.AreEqual( 1, FromSeat().Count(), "nothing goes during the resync" );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && !queue.Pending.Any(), "the park was swapped in and both entries settled" );
		var outcomes = queue.TakeOutcomes( waiter );
		CollectionAssert.AreEqual( new[] { ManagementOutcomeKind.Unknown, ManagementOutcomeKind.Accepted }, outcomes.Select( outcome => outcome.Kind ).ToArray(), "the settings not known, the price applied" );
		Assert.AreEqual( new ManagementNote( ManagementNoteKind.Unknown ), outcomes[0].Note, "the settings say it is not known" );
		CollectionAssert.AreEqual( new ParkCommand[] { settings with { Player = seat }, priced with { Player = seat } }, FromSeat().ToArray(), "the price went once, after the settings, and nothing else went" );
		Build( 60, 65 );
		CatchUpAndCompare( guest );
		Assert.AreEqual( price, StateOfShop( guest.Level!, shop ).Price, "the price in the swapped-in park" );
	}

	/// <summary>The economy record of the shop <paramref name="shop"/> in <paramref name="level"/>.</summary>
	private static ParkObjectState StateOfShop( Level level, int shop )
	{
		var item = level.Objects.Objects.Single( placed => !placed.IsDeleted && placed.Runtime.AttractionId == shop );
		Assert.IsTrue( level.Park!.Economy.TryGetObject( level.GetEconomyInstance( item )!.Value, out var state ) );
		return state;
	}

	[TestMethod]
	public void ChangesQueuedWhileTheStreamIsStoppedGoOnceEachAfterTheSwapInClickOrder()
	{
		var (guest, shop, state) = GuestOfAShop();
		var seat = guest.Link.Slot;
		var queue = guest.Session!.Management!;
		var waiter = queue.NewWaiter();
		IEnumerable<ParkCommand> FromSeat() => host.Level!.Commands.Log.Where( entry => entry.Command.Player == seat && entry.Command is SetShopSettingsCommand or SetPriceCommand ).Select( entry => entry.Command );
		guest.Link.DropFrame = _ => true;
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session!.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
		// price, settings, price: nothing between them to coalesce across, so three entries wait unsent
		var target = ManagementTarget.Attraction( shop );
		var (first, settings, last) = (new SetPriceCommand( shop, state.Price + 1 ), new SetShopSettingsCommand( shop, state.QualityOfGoods == 100 ? 50 : 100, state.IngredientAmount ),
			new SetPriceCommand( shop, state.Price + 4 ));
		foreach ( var (knob, command) in new (ManagementKnob, ParkCommand)[] { (ManagementKnob.Price, first), (ManagementKnob.ShopSettings, settings), (ManagementKnob.Price, last) } )
			Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( target, knob, command ) }, waiter ).Queued );
		Assert.AreEqual( 3, queue.Pending.Count(), "three entries wait" );
		Rounds( 30 );
		Assert.AreEqual( 0, FromSeat().Count(), "nothing goes while the stream is stopped" );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && !queue.Pending.Any(), "the park was swapped in and every entry settled" );
		CollectionAssert.AreEqual( new ParkCommand[] { first with { Player = seat }, settings with { Player = seat }, last with { Player = seat } }, FromSeat().ToArray(), "once each, in click order" );
		Assert.IsTrue( queue.TakeOutcomes( waiter ).All( outcome => outcome.Kind == ManagementOutcomeKind.Accepted ), "all applied" );
		Build( 60, 66 );
		CatchUpAndCompare( guest );
		Assert.AreEqual( (last.Price, settings.QualityOfGoods), (StateOfShop( guest.Level!, shop ).Price, StateOfShop( guest.Level!, shop ).QualityOfGoods & 0xFF), "the last values in the swapped-in park" );
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The guest's management queue for attraction windows (C4b-2, <see cref="ManagementTarget.Attraction"/>), headless: the targets side by side, the click order and its
/// tail coalescing, one open-state action per object, the per-target and overall bounds with their edges, the notes that coalesce by their words, <see cref="CoopManagementRequests.HasUnfinished"/>
/// and the rights asked before the send. The fake issuer and the helpers are <see cref="CoopManagementRequestsTests"/>'s: what its tests pin for the finance, loans and research targets (the
/// pacing and the cap of four open, a target's order, a knob's supersession, a waiter's outcomes, the full target and the whole batch, the rights before the send for the loans) is not repeated
/// but for the attraction's own kinds.
/// </summary>
public partial class CoopManagementRequestsTests
{
	private static ManagementTarget Obj( int attraction ) => ManagementTarget.Attraction( attraction );

	private static ManagementItem RideItem( int attraction, int speed ) => new( Obj( attraction ), ManagementKnob.RideSettings, new SetRideSettingsCommand( attraction, speed, 1, 1 ) );

	private static ManagementItem ShopItem( int attraction, int ingredient ) => new( Obj( attraction ), ManagementKnob.ShopSettings, new SetShopSettingsCommand( attraction, 50, ingredient ) );

	private static ManagementItem PriceItem( int attraction, int price ) => new( Obj( attraction ), ManagementKnob.Price, new SetPriceCommand( attraction, price ) );

	private static ManagementItem GameItem( int attraction, int price ) => new( Obj( attraction ), ManagementKnob.Game, new SetGamePrizeCommand( attraction, price, 10, 50 ) );

	private static ManagementItem DoorItem( int attraction, bool open ) => new( Obj( attraction ), ManagementKnob.Door, new SetAttractionOpenCommand( attraction, open ) );

	private static ManagementItem CallItem( int attraction, bool on ) => new( Obj( attraction ), ManagementKnob.Maintenance, new RequestMaintenanceCommand( attraction, on ) );

	private static ManagementItem BuyItem( int attraction ) => new( Obj( attraction ), null, new BuyUpgradeCommand( attraction ) );

	private static ManagementItem CancelItem( int attraction ) => new( Obj( attraction ), null, new CancelUpgradeCommand( attraction ) );

	/// <summary>The four open-state actions of an object (its door, its call, a level bought, a level cancelled), by name.</summary>
	private static readonly (string Name, Func<int, ManagementItem> Make)[] OpenStateActions =
	{
		("door", attraction => DoorItem( attraction, true )),
		("call", attraction => CallItem( attraction, true )),
		("buy", BuyItem),
		("cancel", CancelItem)
	};

	private static ParkCommandResult RightsRefusal( string reason ) => ParkCommandResult.Rejected( reason ) with { RightsRefused = true };

	/// <summary>The reason each <see cref="CoopRefusal"/> is made from (<see cref="CoopBuildRights.RefusalOf"/>).</summary>
	private static readonly (CoopRefusal Refusal, string Reason)[] Refusals =
	{
		(CoopRefusal.HostOnly, "Only the host may do that."),
		(CoopRefusal.NotGranted, CoopBuildRights.NotGrantedReason),
		(CoopRefusal.Claimed, Level.ClaimedReason),
		(CoopRefusal.ClaimLost, CoopBuildRights.ClaimLostReason),
		(CoopRefusal.NotPermitted, CoopBuildRights.NotPermittedReason)
	};

	private static int Unsent( CoopManagementRequests queue ) => queue.Pending.Count( entry => entry.State == ManagementEntryState.Unsent );

	/// <summary>Stalls the pump: four requests open on targets no window has, whose outcomes never come.</summary>
	private static void Stall( CoopManagementRequests queue, Issuer issuer )
	{
		for ( var id = 900; id < 900 + CoopManagementRequests.MaximumOpen; id++ )
			One( queue, new ManagementItem( Synthetic( id ), null, Fee( id ) ) );
		for ( var step = 0; step < CoopManagementRequests.MaximumOpen; step++ )
			queue.Pump( issuer, step );
		Assert.AreEqual( CoopManagementRequests.MaximumOpen, queue.OpenCount, "the pump is stalled" );
	}

	/// <summary>Fills <paramref name="attraction"/>'s target with <see cref="CoopManagementRequests.MaximumUnsent"/> unsent entries by alternating two knobs (the last is a shop settings entry).</summary>
	private static void Fill( CoopManagementRequests queue, int attraction )
	{
		for ( var index = 0; index < CoopManagementRequests.MaximumUnsent; index++ )
			Assert.IsTrue( queue.Enqueue( new[] { index % 2 == 0 ? RideItem( attraction, index ) : ShopItem( attraction, index ) } ).Queued, $"entry {index}" );
	}

	/// <summary>Asserts <paramref name="result"/> is a refusal for want of room that added nothing.</summary>
	private static void AssertFull( CoopManagementRequests queue, ManagementEnqueued result, int unsentBefore, string what )
	{
		Assert.IsTrue( result.Full, $"{what}: full" );
		Assert.IsFalse( result.Busy, $"{what}: not busy" );
		Assert.IsFalse( result.Queued, $"{what}: nothing queued" );
		Assert.AreEqual( unsentBefore, Unsent( queue ), $"{what}: nothing added" );
	}

	[TestMethod]
	public void AttractionTargetsGoSideBySideUnderTheCapOfFourOpenAndAnObjectsEntriesOneAtATime()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var door = One( queue, DoorItem( 1, true ) );
		var price = One( queue, PriceItem( 1, 5 ) );
		var others = Enumerable.Range( 2, 4 ).Select( id => One( queue, RideItem( id, id ) ) ).ToArray();
		for ( var step = 0; step < 8; step++ )
			queue.Pump( issuer, step );
		// the object's own second entry waits for its first; of the others, four in all are open, the oldest first
		CollectionAssert.AreEqual( new[] { door.Command, others[0].Command, others[1].Command, others[2].Command }, issuer.Commands );
		Assert.AreEqual( (ManagementEntryState.Open, ManagementEntryState.Unsent, ManagementEntryState.Unsent), (door.State, price.State, others[3].State) );
		Assert.AreEqual( CoopManagementRequests.MaximumOpen, queue.OpenCount );
		// the open object's first came back: its second goes before the younger fourth other (the oldest unsent head first), and an object of another id is not held by it
		Accepted( queue, issuer.Sent[0] );
		queue.Pump( issuer, 10 );
		Assert.AreEqual( price.Command, issuer.Sent[^1].Command, "the object's next entry, as its first came back" );
		Accepted( queue, issuer.Sent[1] );
		queue.Pump( issuer, 11 );
		Assert.AreEqual( others[3].Command, issuer.Sent[^1].Command, "another object's entry went at the next free place" );
		Assert.AreEqual( 0, queue.TakeNotes().Count, "accepted entries say nothing" );
	}

	[TestMethod]
	public void AGroupsRunCoalescesOnlyAtTheTargetsTailAndNeverAcrossAnotherEntryOrWaiter()
	{
		var queue = new CoopManagementRequests();
		// price, price: one entry with the latest value
		var first = One( queue, PriceItem( 1, 1 ) );
		Assert.AreSame( first, One( queue, PriceItem( 1, 2 ) ) );
		Assert.AreEqual( new SetPriceCommand( 1, 2 ), first.Command );
		Assert.AreEqual( 1, queue.Pending.Count() );
		// price, settings, price: three entries in that order, the first price keeping its value
		var priceA = One( queue, PriceItem( 2, 1 ) );
		var settings = One( queue, ShopItem( 2, 10 ) );
		var priceB = One( queue, PriceItem( 2, 3 ) );
		CollectionAssert.AreEqual( new[] { priceA, settings, priceB }, queue.Pending.Where( entry => entry.Target == Obj( 2 ) ).ToArray(), "three entries in the click order" );
		Assert.AreEqual( 3, new[] { priceA, settings, priceB }.Distinct().Count() );
		Assert.AreEqual( new SetPriceCommand( 2, 1 ), priceA.Command, "no value dropped from between other entries" );
		// price, door, price: the door is an entry of the target too
		var priceC = One( queue, PriceItem( 3, 1 ) );
		var door = One( queue, DoorItem( 3, false ) );
		var priceD = One( queue, PriceItem( 3, 2 ) );
		CollectionAssert.AreEqual( new[] { priceC, door, priceD }, queue.Pending.Where( entry => entry.Target == Obj( 3 ) ).ToArray() );
		Assert.AreEqual( new SetPriceCommand( 3, 1 ), priceC.Command );
		// a run of the same group right after the door is its own run
		Assert.AreSame( priceD, One( queue, PriceItem( 3, 4 ) ) );
		Assert.AreEqual( new SetPriceCommand( 3, 4 ), priceD.Command );
		// a knob's entry of another waiter does not coalesce, nor one without a waiter with one of a waiter's
		var opening = queue.NewWaiter();
		var other = queue.NewWaiter();
		var mine = One( queue, GameItem( 4, 1 ), opening );
		Assert.AreSame( mine, One( queue, GameItem( 4, 2 ), opening ), "the same waiter's run" );
		var theirs = One( queue, GameItem( 4, 3 ), other );
		var bare = One( queue, GameItem( 4, 4 ) );
		Assert.AreEqual( 3, new[] { mine, theirs, bare }.Distinct().Count() );
		CollectionAssert.AreEqual( new[] { mine, theirs, bare }, queue.Pending.Where( entry => entry.Target == Obj( 4 ) ).ToArray() );
		Assert.AreEqual( new SetGamePrizeCommand( 4, 2, 10, 50 ), mine.Command );
		// an open entry is never taken: the next value of its group is appended
		var issuer = new Issuer();
		queue.Pump( issuer, 0 );
		var sent = queue.Pending.First( entry => entry.State == ManagementEntryState.Open );
		var appended = One( queue, PriceItem( sent.Target.Id, 9 ) );
		Assert.AreNotSame( sent, appended );
	}

	[TestMethod]
	public void OnlyOneOpenStateActionOfAnObjectIsUnsentOrOpenAtATime()
	{
		foreach ( var (firstName, makeFirst) in OpenStateActions )
		{
			foreach ( var (secondName, makeSecond) in OpenStateActions )
			{
				var queue = new CoopManagementRequests();
				var issuer = new Issuer();
				var first = One( queue, makeFirst( 1 ) );
				var what = $"{firstName} then {secondName}";
				Assert.IsTrue( queue.OpenStateWaits( Obj( 1 ) ), what );
				Assert.AreEqual( first.Command, queue.OpenStateCommand( Obj( 1 ) ), $"{what}: it names the waiting one" );
				// unsent: the second is no click: nothing added, no note, neither full nor queued
				var busy = queue.Enqueue( new[] { makeSecond( 1 ) } );
				Assert.IsTrue( busy.Busy, $"{what}: busy while unsent" );
				Assert.IsFalse( busy.Full, $"{what}: not full" );
				Assert.IsFalse( busy.Queued, $"{what}: nothing queued" );
				Assert.AreEqual( 1, queue.Pending.Count(), $"{what}: nothing added" );
				Assert.AreEqual( 0, queue.TakeNotes().Count, $"{what}: no note" );
				// another object's is queued, and an object's own settings are no open-state action
				Assert.IsTrue( queue.Enqueue( new[] { makeSecond( 2 ) } ).Queued, $"{what}: another object's" );
				Assert.IsTrue( queue.Enqueue( new[] { PriceItem( 1, 4 ) } ).Queued, $"{what}: a price of the same object" );
				Assert.AreEqual( first.Command, queue.OpenStateCommand( Obj( 1 ) ), $"{what}: still names the same one" );
				// open: busy still
				queue.Pump( issuer, 0 );
				Assert.AreEqual( ManagementEntryState.Open, first.State );
				Assert.IsTrue( queue.Enqueue( new[] { makeSecond( 1 ) } ).Busy, $"{what}: busy while open" );
				Assert.AreEqual( first.Command, queue.OpenStateCommand( Obj( 1 ) ) );
				// once it resolves, a new one is queued
				Accepted( queue, issuer.Sent[0] );
				Assert.IsFalse( queue.OpenStateWaits( Obj( 1 ) ), $"{what}: nothing waits" );
				Assert.IsNull( queue.OpenStateCommand( Obj( 1 ) ), $"{what}: nothing to name" );
				var again = queue.Enqueue( new[] { makeSecond( 1 ) } );
				Assert.IsTrue( again.Queued, $"{what}: queued after the first resolved" );
				Assert.AreEqual( makeSecond( 1 ).Command, queue.OpenStateCommand( Obj( 1 ) ), $"{what}: names the new one" );
			}
		}
		// the finance target's door and a research entry are no attraction's open-state action
		var finance = new CoopManagementRequests();
		One( finance, Door( true ) );
		Assert.IsFalse( finance.OpenStateWaits( ManagementTarget.Finance ) );
		Assert.IsTrue( finance.Enqueue( new[] { Door( false ), Take( 1 ) } ).Queued );
	}

	[TestMethod]
	public void WithThePumpStalledAClickPastSixteenUnsentOnATargetIsRefusedForEveryKindOfClick()
	{
		var clicks = new (string Name, Func<ManagementItem[]> Make)[]
		{
			("a change of the other group", () => new[] { RideItem( 1, 99 ) }),
			("a typed price", () => new[] { PriceItem( 1, 5 ) }),
			("a typed game price", () => new[] { GameItem( 1, 5 ) }),
			("an apply-all over several targets", () => new[] { RideItem( 1, 5 ), RideItem( 2, 5 ), ShopItem( 3, 5 ) }),
			("a door", () => new[] { DoorItem( 1, true ) }),
			("a level row", () => new[] { BuyItem( 1 ) }),
			("a cancel row", () => new[] { CancelItem( 1 ) }),
			("a call", () => new[] { CallItem( 1, true ) })
		};
		foreach ( var (name, make) in clicks )
		{
			var queue = new CoopManagementRequests();
			var issuer = new Issuer();
			Stall( queue, issuer );
			Fill( queue, 1 );
			Assert.AreEqual( CoopManagementRequests.MaximumUnsent, Unsent( queue ) );
			AssertFull( queue, queue.Enqueue( make() ), CoopManagementRequests.MaximumUnsent, name );
			Assert.IsNull( queue.PendingCommand( Obj( 2 ), ManagementKnob.RideSettings ), $"{name}: nothing of another target's part either" );
			Assert.IsNull( queue.PendingCommand( Obj( 3 ), ManagementKnob.ShopSettings ), $"{name}: nothing of another target's part either" );
			Assert.IsFalse( queue.OpenStateWaits( Obj( 1 ) ), $"{name}: no open-state action was recorded" );
			// the full target does not hold another object's click; and a change that replaces the unsent tail goes in whatever the count
			Assert.IsTrue( queue.Enqueue( new[] { RideItem( 2, 1 ) } ).Queued, $"{name}: another object" );
			var tail = queue.Enqueue( new[] { ShopItem( 1, 77 ) } );
			Assert.IsTrue( tail.Queued, $"{name}: a change of the tail's own group replaces it" );
			Assert.AreEqual( CoopManagementRequests.MaximumUnsent + 1, Unsent( queue ), $"{name}: nothing added to the full target" );
		}
	}

	[TestMethod]
	public void TheOverallBoundStopsEntriesOnManyTargetsAt256AndAnApplyAllPastItQueuesNothing()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		Stall( queue, issuer );
		Fill( queue, 1 );
		// the per-target cap refuses at 16 while the queue has room
		AssertFull( queue, queue.Enqueue( new[] { RideItem( 1, 5 ) } ), 16, "the seventeenth of a target" );
		for ( var id = 2; id <= 241; id++ )
			Assert.IsTrue( queue.Enqueue( new[] { PriceItem( id, 1 ) } ).Queued, $"target {id}" );
		Assert.AreEqual( CoopManagementRequests.MaximumUnsentTotal, Unsent( queue ), "256 unsent across the targets" );
		AssertFull( queue, queue.Enqueue( new[] { PriceItem( 242, 1 ) } ), CoopManagementRequests.MaximumUnsentTotal, "the next target" );
		AssertFull( queue, queue.Enqueue( new[] { RideItem( 1, 5 ) } ), CoopManagementRequests.MaximumUnsentTotal, "the full target still refuses" );
		// a batch that would pass the bound is refused whole: an apply-all over more objects than fit
		var apply = new CoopManagementRequests();
		for ( var id = 1; id <= 100; id++ )
			One( apply, PriceItem( id, 1 ) );
		var tooMany = Enumerable.Range( 1000, 80 ).SelectMany( id => new[] { RideItem( id, 5 ), PriceItem( id, 5 ) } ).ToArray();
		AssertFull( apply, apply.Enqueue( tooMany ), 100, "an apply-all of 80 objects of two groups over 100 unsent" );
		Assert.IsNull( apply.PendingCommand( Obj( 1000 ), ManagementKnob.RideSettings ), "none of its targets got an entry" );
		Assert.IsNull( apply.PendingCommand( Obj( 1079 ), ManagementKnob.Price ) );
		var fits = Enumerable.Range( 1000, 78 ).SelectMany( id => new[] { RideItem( id, 5 ), PriceItem( id, 5 ) } ).ToArray();
		Assert.AreEqual( 156, apply.Enqueue( fits ).Entries.Count, "exactly up to 256 goes whole" );
		Assert.AreEqual( CoopManagementRequests.MaximumUnsentTotal, Unsent( apply ) );
		// an apply-all of 128 shops of two groups goes whole into an empty queue, and no 129th
		var shops = new CoopManagementRequests();
		var all = Enumerable.Range( 1, 128 ).SelectMany( id => new[] { ShopItem( id, 5 ), PriceItem( id, 5 ) } ).ToArray();
		Assert.AreEqual( 256, shops.Enqueue( all ).Entries.Count );
		AssertFull( shops, shops.Enqueue( new[] { ShopItem( 129, 5 ), PriceItem( 129, 5 ) } ), CoopManagementRequests.MaximumUnsentTotal, "a 129th shop" );
	}

	[TestMethod]
	public void TheBoundsEdgesAReplacementIsAdmittedAndABatchOfOneNetAdditionIsNot()
	{
		var queue = new CoopManagementRequests();
		var entries = Enumerable.Range( 1, CoopManagementRequests.MaximumUnsentTotal ).Select( id => One( queue, PriceItem( id, 1 ) ) ).ToArray();
		Assert.AreEqual( CoopManagementRequests.MaximumUnsentTotal, Unsent( queue ) );
		// a change that replaces its target's unsent tail is admitted at the bound
		var replaced = queue.Enqueue( new[] { PriceItem( 1, 7 ) } );
		Assert.IsTrue( replaced.Queued );
		Assert.AreSame( entries[0], replaced.Entries.Single(), "the same entry" );
		Assert.AreEqual( new SetPriceCommand( 1, 7 ), entries[0].Command );
		Assert.AreEqual( CoopManagementRequests.MaximumUnsentTotal, Unsent( queue ) );
		// two replacements are no net addition
		var twice = queue.Enqueue( new[] { PriceItem( 1, 8 ), PriceItem( 2, 8 ) } );
		Assert.AreEqual( 2, twice.Entries.Count );
		Assert.AreEqual( CoopManagementRequests.MaximumUnsentTotal, Unsent( queue ) );
		// one replacement and one append: one net addition, refused whole (the replacement does not happen either)
		var mixed = queue.Enqueue( new[] { PriceItem( 1, 9 ), PriceItem( 257, 9 ) } );
		AssertFull( queue, mixed, CoopManagementRequests.MaximumUnsentTotal, "one replacement and one append" );
		Assert.AreEqual( new SetPriceCommand( 1, 8 ), entries[0].Command, "the replacement was not made" );
		Assert.IsNull( queue.PendingCommand( Obj( 257 ), ManagementKnob.Price ) );
		// a sent entry leaves the unsent ones: exactly one place, and its resolution frees no second one
		var issuer = new Issuer();
		queue.Pump( issuer, 0 );
		Assert.AreEqual( CoopManagementRequests.MaximumUnsentTotal - 1, Unsent( queue ) );
		Assert.IsTrue( queue.Enqueue( new[] { PriceItem( 300, 1 ) } ).Queued, "the freed place" );
		AssertFull( queue, queue.Enqueue( new[] { PriceItem( 301, 1 ) } ), CoopManagementRequests.MaximumUnsentTotal, "no second place" );
		Accepted( queue, issuer.Sent[0] );
		AssertFull( queue, queue.Enqueue( new[] { PriceItem( 301, 1 ) } ), CoopManagementRequests.MaximumUnsentTotal, "the open entry's outcome frees no place of the unsent" );
	}

	[TestMethod]
	public void AnUnsentEntryResolvedAtThePumpFreesExactlyOnePlace()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		Stall( queue, issuer );
		for ( var id = 1; id <= CoopManagementRequests.MaximumUnsentTotal; id++ )
			One( queue, PriceItem( id, 1 ) );
		AssertFull( queue, queue.Enqueue( new[] { PriceItem( 300, 1 ) } ), CoopManagementRequests.MaximumUnsentTotal, "at the bound" );
		// the rights take object 1's price back: its entry resolves as refused at the pump (the cap of four open holds the others)
		issuer.Rights = command => command is SetPriceCommand { Attraction: 1 } ? NotPermitted : null;
		queue.Pump( issuer, 10 );
		Assert.AreEqual( 4, issuer.Sent.Count, "nothing more went" );
		Assert.AreEqual( CoopManagementRequests.MaximumUnsentTotal - 1, Unsent( queue ) );
		Assert.IsTrue( queue.Enqueue( new[] { PriceItem( 300, 1 ) } ).Queued, "one place" );
		AssertFull( queue, queue.Enqueue( new[] { PriceItem( 301, 1 ) } ), CoopManagementRequests.MaximumUnsentTotal, "and no second one" );
	}

	[TestMethod]
	public void SixtyFiveAndMoreFailuresLeaveOneNoteOfEachKindOfWordsNoneDropped()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var now = 0.0;
		// the rights refusal of an object's price is the one its id picks
		issuer.Rights = command => command is SetPriceCommand price && price.Attraction is >= 10 and < 15 ? RightsRefusal( Refusals[price.Attraction - 10].Reason ) : null;
		// a rights result gives the rights note, none gives the waiting note
		queue.NoteRefused( RideItem( 1, 1 ), NotPermitted );
		queue.NoteRefused( RideItem( 1, 1 ), null );
		var first = queue.TakeNotes();
		Assert.AreEqual( (ManagementNoteKind.Rights, CoopRefusal.NotPermitted), (first[0].Note.Kind, first[0].Note.Refusal) );
		Assert.AreEqual( ManagementOutcomeKind.Refused, first[0].Kind );
		Assert.AreEqual( (ManagementNoteKind.Waiting, ManagementOutcomeKind.NotQueued), (first[1].Note.Kind, first[1].Kind) );
		var failures = 0;
		for ( var round = 0; round < 10; round++ )
		{
			// not taken, and unknown
			One( queue, new ManagementItem( Synthetic( 1 ), null, Fee( round ) ) );
			queue.Pump( issuer, ++now );
			NotTaken( queue, issuer.Sent[^1] );
			One( queue, new ManagementItem( Synthetic( 1 ), null, Fee( round ) ) );
			queue.Pump( issuer, ++now );
			issuer.Sent[^1].State = CoopGuestRequestState.Unknown;
			queue.Resolved( issuer.Sent[^1] );
			// each rights refusal at the pump, without a request
			var sent = issuer.Sent.Count;
			for ( var which = 0; which < Refusals.Length; which++ )
				One( queue, PriceItem( 10 + which, round ) );
			queue.Pump( issuer, ++now );
			Assert.AreEqual( sent, issuer.Sent.Count, "the rights' refusals sent nothing" );
			// a stale level at its record
			One( queue, BuyItem( 20 ) );
			queue.Pump( issuer, ++now );
			Applied( queue, issuer.Sent[^1], ParkCommandResult.Rejected( ParkSettingCommand.StaleReason ) );
			// refused before queued: the waiting note and a rights note
			queue.NoteRefused( RideItem( 1, round ), null );
			queue.NoteRefused( RideItem( 1, round ), NotPermitted );
			failures += 2 + Refusals.Length + 1 + 2;
		}
		Assert.IsTrue( failures > CoopManagementRequests.MaximumNotes, "more failures than the notes keep" );
		var notes = queue.TakeNotes();
		var expected = new HashSet<(ManagementNoteKind, CoopRefusal)>
		{
			(ManagementNoteKind.NotTaken, CoopRefusal.HostOnly),
			(ManagementNoteKind.Unknown, CoopRefusal.HostOnly),
			(ManagementNoteKind.NotAvailable, CoopRefusal.HostOnly),
			(ManagementNoteKind.Waiting, CoopRefusal.HostOnly)
		};
		foreach ( var (refusal, _) in Refusals )
			expected.Add( (ManagementNoteKind.Rights, refusal) );
		Assert.AreEqual( expected.Count, notes.Count, "one note per kind of words" );
		CollectionAssert.AreEquivalent( expected.ToArray(), notes.Select( note => (note.Note.Kind, note.Note.Refusal) ).ToArray(), "none dropped" );
		Assert.AreEqual( 0, queue.Pending.Count() );
	}

	[TestMethod]
	public void HasUnfinishedIsTrueForWhatMayNotHaveBeenMadeAndNotSaid()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		Assert.IsFalse( queue.HasUnfinished, "nothing" );
		// an entry unsent, then open, then accepted
		var entry = One( queue, PriceItem( 1, 1 ) );
		Assert.IsTrue( queue.HasUnfinished, "unsent" );
		queue.Pump( issuer, 0 );
		Assert.IsTrue( queue.HasUnfinished, "open" );
		Accepted( queue, issuer.Sent[0] );
		Assert.AreEqual( ManagementEntryState.Resolved, entry.State );
		Assert.IsFalse( queue.HasUnfinished, "an accepted entry without a waiter says nothing" );
		// a note not taken
		queue.NoteRefused( RideItem( 1, 1 ), null );
		Assert.IsTrue( queue.HasUnfinished, "a note not taken" );
		queue.TakeNotes();
		Assert.IsFalse( queue.HasUnfinished, "the note taken" );
		// a live waiter's outcome: no success is unfinished until taken, an accepted one is not
		var now = 1.0;
		bool UnfinishedAfter( Action<CoopGuestCommands.Request> resolve, out ManagementWaiter waiter, ManagementWaiter? live = null )
		{
			waiter = live ?? queue.NewWaiter();
			One( queue, PriceItem( 2, 1 ), waiter );
			queue.Pump( issuer, ++now );
			resolve( issuer.Sent[^1] );
			return queue.HasUnfinished;
		}
		Assert.IsFalse( UnfinishedAfter( request => Accepted( queue, request ), out var accepted ), "an accepted outcome waiting" );
		Assert.IsFalse( UnfinishedAfter( request => Accepted( queue, request ), out _, queue.HudWaiter ), "the HUD waiter's accepted outcome" );
		queue.TakeOutcomes( accepted );
		queue.TakeOutcomes( queue.HudWaiter );
		var resolves = new (string Name, Action<CoopGuestCommands.Request> Resolve)[]
		{
			("refused", request => Applied( queue, request, ParkCommandResult.Rejected( "no" ) )),
			("not taken", request => NotTaken( queue, request )),
			("unknown", request =>
			{
				request.State = CoopGuestRequestState.Unknown;
				queue.Resolved( request );
			})
		};
		foreach ( var (name, resolve) in resolves )
		{
			Assert.IsTrue( UnfinishedAfter( resolve, out var waiter ), $"a live waiter's {name} outcome not taken" );
			Assert.AreEqual( 1, queue.TakeOutcomes( waiter ).Count );
			Assert.IsFalse( queue.HasUnfinished, $"the {name} outcome taken" );
		}
		// a detached waiter's outcome becomes a note: unfinished until the note is taken; a detached accepted one never
		var detached = queue.NewWaiter();
		One( queue, RideItem( 3, 1 ), detached );
		queue.Pump( issuer, ++now );
		Applied( queue, issuer.Sent[^1], NotPermitted );
		Assert.IsTrue( queue.HasUnfinished, "its refusal waits in its waiter" );
		queue.Detach( detached );
		Assert.AreEqual( 0, queue.TakeOutcomes( detached ).Count );
		Assert.IsTrue( queue.HasUnfinished, "now a note not taken" );
		queue.TakeNotes();
		Assert.IsFalse( queue.HasUnfinished, "the note taken" );
		var acceptedLate = queue.NewWaiter();
		One( queue, RideItem( 4, 1 ), acceptedLate );
		queue.Pump( issuer, ++now );
		queue.Detach( acceptedLate );
		Accepted( queue, issuer.Sent[^1] );
		Assert.IsFalse( queue.HasUnfinished, "a detached waiter's accepted outcome says nothing" );
	}

	[TestMethod]
	public void ARightsRefusalThatAppearsAfterTheClickResolvesAtThePumpWithoutARequest()
	{
		var queue = new CoopManagementRequests();
		var issuer = new Issuer();
		var waiter = queue.NewWaiter();
		// object 1: a price without a waiter, then a settings entry behind it; object 2: a door of a waiter; object 3: a call that stays allowed
		var price = One( queue, PriceItem( 1, 5 ) );
		var behind = One( queue, RideItem( 1, 2 ) );
		var door = One( queue, DoorItem( 2, true ), waiter );
		var call = One( queue, CallItem( 3, true ) );
		// a claim taken since the click: the rights refuse object 1's price and object 2's door
		issuer.Rights = command => command switch
		{
			SetPriceCommand { Attraction: 1 } => RightsRefusal( Level.ClaimedReason ),
			SetAttractionOpenCommand { Attraction: 2 } => RightsRefusal( Level.ClaimedReason ),
			_ => null
		};
		queue.Pump( issuer, 0 );
		queue.Pump( issuer, 1 );
		queue.Pump( issuer, 2 );
		// the refused ones never made a request; the entry behind the refused price went, as did the other object's call
		CollectionAssert.AreEqual( new[] { behind.Command, call.Command }, issuer.Commands );
		Assert.AreEqual( (ManagementEntryState.Resolved, ManagementEntryState.Resolved), (price.State, door.State) );
		Assert.IsNull( price.Request );
		Assert.IsNull( door.Request );
		var outcome = queue.TakeOutcomes( waiter ).Single();
		Assert.AreSame( door, outcome.Entry );
		Assert.AreEqual( (ManagementOutcomeKind.Refused, ManagementNoteKind.Rights, CoopRefusal.Claimed), (outcome.Kind, outcome.Note.Kind, outcome.Note.Refusal), "its waiter's outcome" );
		var note = queue.TakeNotes().Single();
		Assert.AreSame( price, note.Entry, "the entry without a waiter: its note is the rights'" );
		Assert.AreEqual( (ManagementNoteKind.Rights, CoopRefusal.Claimed), (note.Note.Kind, note.Note.Refusal) );
		Assert.IsFalse( queue.OpenStateWaits( Obj( 2 ) ), "the refused door no longer waits" );
	}
}

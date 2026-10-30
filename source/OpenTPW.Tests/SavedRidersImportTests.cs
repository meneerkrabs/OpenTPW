using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.QueueTests;

namespace OpenTPW.Tests;

/// <summary>
/// The riders of an original save (guests in state 16, SAVE-002) with the walk, bounce and head tables of their ride's script record (DET-013, loader tp.exe <c>0x005597A0</c>): the loader reads the
/// state straight in, the state-16 update does nothing (<c>0x00501A08</c>), and the ride's script owns the rest. No corpus save holds a rider, so these are synthetic saves built from the saver's
/// shapes (the writer <c>0x00559350</c>: the struct, the nine buffers and the object list).
/// </summary>
[TestClass]
public class SavedRidersImportTests
{
	private const int RideThing = 30;
	private const int RideId = 1;
	private const int Turn = 755;
	private const uint Clock = 185_660_000;

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static ushort At( int cell, int sub = 128 ) => (ushort)(cell * 256 + sub);

	private static SaveGuest Decode( SaveTestPayloads.GuestFields fields, int id )
	{
		var body = SaveTestPayloads.Guest( fields );
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		return SaveGuest.Read( payload, new SaveThing( id, SaveThingModel.Guest, 0, 0, 8, body.Length ) );
	}

	private static SaveGuest Rider( int id, ushort ride = RideThing, byte queuePos = 0, int cell = 3 ) =>
		Decode( new SaveTestPayloads.GuestFields { Position = (At( cell ), At( 0 )), State = 16, MajorDest = ride, QueuePos = queuePos }, id );

	private static SaveAttraction Ride( int id = RideThing, ushort first = 0, ushort loading = 0 ) =>
		SaveAttraction.Read( SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields() ), 0, id ) with { FirstInQueue = first, PersonBeingLoaded = loading };

	private static Rig NewRig( RideVisitorKind? kind = null, int headIds = 2 )
	{
		var rig = new Rig( queueCells: 5, kind: kind );
		if ( headIds > 0 )
			rig.Ride.Heads = RideHeadSpace.Of( Enumerable.Range( 10, headIds ).ToArray(), 1 );
		return rig;
	}

	/// <summary>A script record in the loader's layout for the rig's script: the struct, the buffers and the three rider tables.</summary>
	private static SaveRsseRecord Record( RideVM vm, SaveRsseWalkEntry[]? walk = null, SaveRsseBounceEntry[]? bounce = null, uint[]? heads = null, short? bounceCount = null, uint nodeBase = 3, short baseHeight = 0, int headSlots = 2 )
	{
		heads ??= new uint[headSlots];
		var walkSize = vm.Script.WalkSize;
		var bounceSize = vm.Script.BounceSize;
		var walkBytes = new byte[walkSize * SaveRsseRecord.WalkEntryBytes];
		foreach ( var (entry, slot) in (walk ?? Array.Empty<SaveRsseWalkEntry>()).Select( ( entry, slot ) => (entry, slot) ).Where( pair => pair.entry.State != 0 || pair.entry.Guest != 0 ) )
		{
			var at = slot * SaveRsseRecord.WalkEntryBytes;
			void Half( int offset, ushort value ) => BitConverter.GetBytes( value ).CopyTo( walkBytes, at + offset );
			void Word( int offset, uint value ) => BitConverter.GetBytes( value ).CopyTo( walkBytes, at + offset );
			Half( 0, entry.Node1 ); Half( 2, entry.Node2 ); Half( 4, entry.Node3 ); Half( 6, entry.Node4 ); Word( 8, entry.Began ); Word( 12, entry.Ends );
			Word( 0x10, entry.Guest ); Half( 0x14, entry.Heading ); Half( 0x16, entry.Mode ); Half( 0x18, entry.State ); Half( 0x1A, entry.Extra );
		}
		var bounceBytes = new byte[bounceSize * SaveRsseRecord.BounceEntryBytes];
		for ( var slot = 0; slot < (bounce?.Length ?? 0); slot++ )
		{
			var entry = bounce![slot];
			var at = slot * SaveRsseRecord.BounceEntryBytes;
			BitConverter.GetBytes( entry.Guest ).CopyTo( bounceBytes, at );
			BitConverter.GetBytes( entry.Node ).CopyTo( bounceBytes, at + 4 );
			BitConverter.GetBytes( entry.Deadline ).CopyTo( bounceBytes, at + 8 );
			BitConverter.GetBytes( entry.Start ).CopyTo( bounceBytes, at + 12 );
		}
		var headBytes = heads.SelectMany( BitConverter.GetBytes ).ToArray();
		var raw = new byte[SaveRsseRecord.Bytes];
		void Set( int offset, uint value ) => BitConverter.GetBytes( value ).CopyTo( raw, offset );
		Set( 0x08, 7 );
		Set( 0x3C, 0 );
		Set( 0x40, unchecked( (uint)(vm.StackCapacity - 1) ) );
		Set( 0x4C, (uint)heads.Length );
		Set( 0x50, (uint)vm.Script.CodeWordCount );
		Set( 0x54, (uint)vm.StackCapacity );
		Set( 0x58, (uint)vm.LimboCapacity );
		Set( 0x64, (uint)bounceSize );
		BitConverter.GetBytes( bounceCount ?? (short)(bounce?.Count( entry => entry.Guest != 0 ) ?? 0) ).CopyTo( raw, 0x6C );
		BitConverter.GetBytes( baseHeight ).CopyTo( raw, 0x6E );
		Set( 0x70, nodeBase );
		Set( 0x7C, (uint)walkSize );
		Set( 0x8C, (uint)vm.Variables.Length );
		Set( 0x74, 0xFFFFFFFF );
		Set( 0x94, 50 );
		Set( 0x98, 0xFFFFFFFF );
		Set( 0xA8, 0xFFFF );
		Set( 0xC0, 50 );
		raw[0xE6] = 0xFF;
		raw[0xE7] = 0xFF;
		static byte[] Words( int count ) => new byte[count * 4];
		return new SaveRsseRecord
		{
			Raw = raw, Code = new byte[vm.Script.CodeWordCount * 4], Stack = Words( vm.StackCapacity ), Variables = Words( vm.Variables.Length ), Strings = Array.Empty<byte>(),
			Limbo = new byte[vm.LimboCapacity * SaveRsseRecord.LimboEntryBytes], Bounce = bounceBytes, Walk = walkBytes, Nodes = headBytes,
			Folder = "data\\levels\\jungle\\Features\\test\\", Objects = Array.Empty<SaveRsseObject>(),
		};
	}

	private static SaveRsseWalkEntry Walking( uint guest, ushort state, int began, int ends, ushort mode = 1, ushort heading = 0 ) =>
		new( 2, 3, 4, 5, unchecked( Clock + (uint)began ), unchecked( Clock + (uint)ends ), guest, heading, mode, state, 1 );

	private static SaveRsseBounceEntry Bouncing( uint guest, uint node, int deadline, int start ) => new( guest, node, unchecked( Clock + (uint)deadline ), unchecked( Clock + (uint)start ) );

	/// <summary>Takes the record's run state and tables the way the park import does (SavedScriptImport), so the rig's ride holds them.</summary>
	private static void Stage( Rig rig, SaveRsseRecord record, List<string>? notes = null )
	{
		Assert.IsNull( rig.Script.ImportSavedRunState( record, Clock ) );
		SavedScriptImport.StageRiderTables( rig.Script, record, rig.Ride, Clock, notes ?? new List<string>() );
	}

	private static IReadOnlyList<string> Import( Rig rig, SaveGuest[] guests, SaveAttraction[]? rides = null ) =>
		rig.Simulation.ImportOriginal( guests, Turn, thing => thing == RideThing ? RideId : 0, _ => 0, 9, rides ?? new[] { Ride() } );

	private static Guest Of( Rig rig, int id ) => rig.Simulation.Guests.Single( guest => guest.Id == id );

	[TestMethod]
	public void RidersAreImportedAboardWithTheirRidesTablesOnTheScriptsClock()
	{
		var rig = NewRig();
		var record = Record( rig.Script,
			walk: new[] { default, Walking( 41, 2, -5000, -4000 ), default, Walking( 42, 3, 100, 1100, mode: 2, heading: 3 ) },
			bounce: new[] { Bouncing( 43, 3, 12_000, -2000 ) },
			heads: new uint[] { 41, 0 } );
		Stage( rig, record );
		var report = Import( rig, new[] { Rider( 41 ), Rider( 42 ), Rider( 43 ) } );

		foreach ( var id in new[] { 41, 42, 43 } )
		{
			Assert.AreEqual( (GuestState.Using, RideId), (Of( rig, id ).State, Of( rig, id ).AttractionId), $"guest {id} rides" );
			Assert.IsFalse( Of( rig, id ).IsVisible );
		}
		CollectionAssert.AreEqual( new[] { 41, 42, 43 }, rig.Ride.Riders.ToArray(), "aboard in the order of their thing ids" );
		StringAssert.Contains( report.Single( line => line.StartsWith( "Riders imported:", StringComparison.Ordinal ) ), "3 guests in state 16" );
		Assert.IsFalse( report.Any( line => line.Contains( "Guests not imported" ) ), "no rider is dropped" );

		var walkers = new List<RideVisitorBridge.WalkerView>();
		rig.Ride.CollectWalkers( walkers );
		Assert.AreEqual( 2, walkers.Count );
		// the times are the saved game clock's difference: this script's clock starts at the import
		Assert.AreEqual( (41, 2, 1, -5000d, -4000d, 2, 3, 4, 5), (walkers[0].Guest, walkers[0].State, walkers[0].Mode, walkers[0].Began, walkers[0].Ends, walkers[0].U1, walkers[0].U2, walkers[0].U3, walkers[0].U4) );
		Assert.AreEqual( (42, 3, 2, 100d, 1100d, 3), (walkers[1].Guest, walkers[1].State, walkers[1].Mode, walkers[1].Began, walkers[1].Ends, walkers[1].Heading) );
		var bouncing = new List<(int Guest, int Node, double Start)>();
		rig.Ride.CollectBouncing( bouncing );
		CollectionAssert.AreEqual( new[] { (43, 3, -2000d) }, bouncing );
		Assert.AreEqual( 41, rig.Ride.Heads!.VisitorAt( 0 ), "the head table is the saved one" );
		Assert.AreEqual( 0, rig.Ride.Heads.VisitorAt( 1 ) );
		Assert.AreEqual( 3, rig.Ride.BounceNodeBase );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).Errors ) );
	}

	[TestMethod]
	public void AWalkOnThatEndedBeforeTheSaveMovesOnAtTheFirstHostStep()
	{
		var rig = NewRig();
		// walk action 4 hangs the head on head id u2 once the walk-on is over (0x00557E75); this entry's end (clock - 500) is past
		var record = Record( rig.Script, walk: new[] { new SaveRsseWalkEntry( 2, 1, 4, 5, Clock - 1500, Clock - 500, 41, 0, 4, 1, 1 ) } );
		Stage( rig, record );
		Import( rig, new[] { Rider( 41 ) } );
		var hung = new List<int>();
		rig.Ride.Heads!.HeadHung = hung.Add;
		rig.Ride.HostStep();
		var walkers = new List<RideVisitorBridge.WalkerView>();
		rig.Ride.CollectWalkers( walkers );
		Assert.AreEqual( 2, walkers.Single().State, "the walk update moves the entry on board" );
		CollectionAssert.AreEqual( new[] { 41 }, hung, "and the head hangs (the saved walk-on had not reached it)" );
		Assert.AreEqual( 41, rig.Ride.Heads.VisitorAt( 0 ) );
	}

	[TestMethod]
	public void ARiderOfARideWhoseScriptStateWasNotImportedIsNotImported()
	{
		var rig = NewRig();
		var report = Import( rig, new[] { Rider( 41 ), Rider( 42, ride: 99 ) } );
		Assert.AreEqual( 0, rig.Simulation.Guests.Count );
		StringAssert.EndsWith( report.Single( line => line.StartsWith( "[APPROX:SAVE-002] Guests not imported:", StringComparison.Ordinal ) ), "guest 41 (state 16), guest 42 (state 16)." );
		Assert.AreEqual( 0, rig.Ride.Riders.Count );
	}

	[TestMethod]
	public void ATableThatNamesAGuestWhoIsNotAboardIsNotKeptButTheRidersAre()
	{
		var rig = NewRig();
		var record = Record( rig.Script, walk: new[] { Walking( 41, 2, -100, 0 ), Walking( 77, 2, -100, 0 ) }, heads: new uint[] { 0, 41 } );
		Stage( rig, record );
		var report = Import( rig, new[] { Rider( 41 ) } );
		CollectionAssert.AreEqual( new[] { 41 }, rig.Ride.Riders.ToArray() );
		var walkers = new List<RideVisitorBridge.WalkerView>();
		rig.Ride.CollectWalkers( walkers );
		Assert.AreEqual( 0, walkers.Count, "no entry of the refused tables is kept" );
		Assert.AreEqual( 0, rig.Ride.Heads!.VisitorAt( 1 ) );
		StringAssert.Contains( report.Single( line => line.Contains( "name guests who are not aboard" ) ), "77" );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid );
	}

	[TestMethod]
	public void ARideWithoutRidersStillTakesItsStagedState()
	{
		var rig = NewRig();
		Stage( rig, Record( rig.Script ) );
		var report = Import( rig, new[] { Decode( new SaveTestPayloads.GuestFields { Position = (At( 3 ), At( 0 )), State = 6 }, 41 ) } );
		Assert.AreEqual( 0, rig.Ride.Riders.Count );
		Assert.IsFalse( report.Any( line => line.StartsWith( "Riders imported:", StringComparison.Ordinal ) ) );
		Assert.IsTrue( rig.Ride.SavedRunStateImported );
	}

	[TestMethod]
	public void AQueueHeadTheScriptTookBoardsAtTheFirstHostStepAndKeepsItsTableEntries()
	{
		var rig = NewRig();
		// the save: guest 41 is the head in state 14 and the script has taken it (VAR_LETMEON 0 again, a bounce entry already names it): its update would enter state 16 next (0x004E0A70)
		var record = Record( rig.Script, bounce: new[] { Bouncing( 41, 3, 9000, -100 ) } );
		Stage( rig, record );
		var head = Decode( new SaveTestPayloads.GuestFields { Position = (At( 2 ), At( 5 )), Destination = (At( 2 ), At( 6 )), State = 14, MajorDest = RideThing, BeenAdmitted = 1 }, 41 );
		var report = Import( rig, new[] { head }, new[] { Ride( first: 41, loading: 41 ) } );
		Assert.AreEqual( GuestState.WaitingToBoard, Of( rig, 41 ).State, "the guest has not updated since the script took it" );
		Assert.AreEqual( 0, rig.Ride.Riders.Count );
		Assert.IsFalse( report.Any( line => line.Contains( "called but not admitted" ) ) );
		var notes = rig.Simulation.ReconcileOriginalAdmissions();
		Assert.AreEqual( 0, notes.Count, "a ride whose script state was imported demotes nothing" );
		rig.Ride.HostStep();
		Assert.AreEqual( GuestState.Using, Of( rig, 41 ).State, "the first host step boards it, as the state-14 test sends it into state 16" );
		CollectionAssert.AreEqual( new[] { 41 }, rig.Ride.Riders.ToArray() );
		var bouncing = new List<(int Guest, int Node, double Start)>();
		rig.Ride.CollectBouncing( bouncing );
		Assert.AreEqual( 41, bouncing.Single().Guest );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( rig.Simulation.CaptureSnapshot() ).Errors ) );
	}

	[TestMethod]
	public void AHeadWhoseScriptStateWasNotImportedIsStillCalledAgain()
	{
		var rig = NewRig();
		var head = Decode( new SaveTestPayloads.GuestFields { Position = (At( 2 ), At( 5 )), Destination = (At( 2 ), At( 6 )), State = 14, MajorDest = RideThing, BeenAdmitted = 1 }, 41 );
		Import( rig, new[] { head }, new[] { Ride( first: 41, loading: 41 ) } );
		rig.Script[RideVariables.VAR_LETMEON] = 0;
		var notes = rig.Simulation.ReconcileOriginalAdmissions();
		StringAssert.Contains( notes.Single(), "called but not admitted" );
		Assert.AreEqual( GuestState.Boarding, Of( rig, 41 ).State );
	}

	[TestMethod]
	public void ASideshowRidersWinByteIsTheSavedQueuePositionByte()
	{
		var rig = NewRig( kind: RideVisitorKind.Sideshow );
		Stage( rig, Record( rig.Script ) );
		Import( rig, new[] { Rider( 41, queuePos: 1 ), Rider( 42, queuePos: 0 ) } );
		Assert.AreEqual( true, Of( rig, 41 ).SideshowWon, "+0x1F1 holds the win draw (0x004E2670)" );
		Assert.AreEqual( false, Of( rig, 42 ).SideshowWon, "a lost play (0x004FDA0D)" );
	}

	[TestMethod]
	public void ImportedGuestsKeepTheLoadersZeroQueueJoinHappiness()
	{
		var rig = NewRig();
		Stage( rig, Record( rig.Script ) );
		Import( rig, new[] { Rider( 41 ) } );
		Assert.AreEqual( 0f, Of( rig, 41 ).QueueJoinHappiness, "the thing-list loader zeroes +0x20C (0x0051861C) and the serializer does not save it" );
	}

	[TestMethod]
	public void AnImportedRiderIsLetOffByTheScriptAndLeavesTheRide()
	{
		var rig = NewRig();
		Stage( rig, Record( rig.Script, heads: new uint[] { 41, 0 } ) );
		Import( rig, new[] { Rider( 41 ) } );
		rig.Script[RideVariables.VAR_LETMEOFF] = 41;
		for ( var turn = 0; turn < 12 && Of( rig, 41 ).State == GuestState.Using; turn++ )
			rig.RunTurns( 1 );
		Assert.AreNotEqual( GuestState.Using, Of( rig, 41 ).State, "VAR_LETMEOFF (0x004E1410) lets the imported rider off" );
		Assert.AreEqual( 0, rig.Ride.Riders.Count );
		Assert.AreEqual( 0, rig.Ride.Heads!.VisitorAt( 0 ), "its head slot is freed" );
	}

	// ---- The tables read against the writer's shapes ----

	[TestMethod]
	public void TablesAreReadAsSavedAndRefusedWhenTheyDoNotReadLikeTheWriters()
	{
		var rig = NewRig();
		var vm = rig.Script;
		SavedRideTables? Read( SaveRsseRecord record, out string? why, int heads = 2 ) => SavedRideTables.Read( record, vm.Script.WalkSize, vm.Script.BounceSize, heads, Clock, out why );

		var good = Read( Record( vm, walk: new[] { Walking( 41, 1, 0, 10 ) }, bounce: new[] { Bouncing( 42, 3, 5, 6 ) }, heads: new uint[] { 0, 43 } ), out var refusal );
		Assert.IsNotNull( good, refusal );
		CollectionAssert.AreEquivalent( new[] { 41, 42, 43 }, good.Guests.ToArray() );

		Assert.IsNull( Read( Record( vm, walk: new[] { Walking( 41, 5, 0, 10 ) } ), out refusal ) );
		StringAssert.Contains( refusal, "state 5" );
		Assert.IsNull( Read( Record( vm, bounce: new[] { Bouncing( 42, 3, 5, 6 ), Bouncing( 42, 4, 5, 6 ) } ), out refusal ) );
		StringAssert.Contains( refusal, "a second time" );
		Assert.IsNull( Read( Record( vm, bounce: new[] { Bouncing( 42, 3, 5, 6 ) }, bounceCount: 2 ), out refusal ) );
		StringAssert.Contains( refusal, "bounce counter is 2" );
		Assert.IsNull( Read( Record( vm, heads: new uint[] { 0, 43 } ), out refusal, heads: 3 ) );
		StringAssert.Contains( refusal, "head table holds 2 slots" );
		Assert.IsNull( Read( Record( vm, heads: new uint[] { 0, 0x80000000 } ), out refusal ) );
		StringAssert.Contains( refusal, "head slot 1" );
		// a header of another size is another script
		Assert.IsNull( SavedRideTables.Read( Record( vm ), vm.Script.WalkSize + 1, vm.Script.BounceSize, 2, Clock, out refusal ) );
		StringAssert.Contains( refusal, "walk table" );
	}

	[TestMethod]
	public void ARiderHoldingTheSameHeadTwiceIsKeptAsTheOpcodesLeaveIt()
	{
		var rig = NewRig();
		var tables = SavedRideTables.Read( Record( rig.Script, heads: new uint[] { 41, 41 } ), rig.Script.Script.WalkSize, rig.Script.Script.BounceSize, 2, Clock, out var refusal );
		Assert.IsNotNull( tables, refusal );
		CollectionAssert.AreEqual( new[] { 41, 41 }, tables.Heads.ToArray(), "ADDHEAD (0x00554C80) does not test for a head already hung" );
	}

	[TestMethod]
	public void TheRecordsTablesDecodeInTheLoadersLayout()
	{
		var rig = NewRig();
		var record = Record( rig.Script, walk: new[] { Walking( 41, 4, -3, 9, mode: 6, heading: 7 ) }, bounce: new[] { Bouncing( 42, 5, 7, 8 ) }, heads: new uint[] { 0, 43 } );
		var walk = record.WalkEntries[0];
		Assert.AreEqual( (2, 3, 4, 5, 41u, 7, 6, 4, 1), (walk.Node1, walk.Node2, walk.Node3, walk.Node4, walk.Guest, walk.Heading, walk.Mode, walk.State, walk.Extra) );
		Assert.AreEqual( Clock - 3, walk.Began );
		Assert.IsTrue( record.WalkEntries[1].IsFree );
		Assert.AreEqual( new SaveRsseBounceEntry( 42, 5, Clock + 7, Clock + 8 ), record.BounceEntries[0] );
		Assert.IsTrue( record.BounceEntries[1].IsFree );
		CollectionAssert.AreEqual( new uint[] { 0, 43 }, record.HeadSlots.ToArray() );
	}
}

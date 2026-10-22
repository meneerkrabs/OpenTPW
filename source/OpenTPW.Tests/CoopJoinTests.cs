using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-snapshot-join] A join snapshot's parts (docs/COOP.md, "Joining a running session"), with the stream tests' asset-free park: the payload and the session state it carries
/// round-trip and refuse every malformed input, the session state brings a replayed twin's per-player state to the host's, and the join capture changes nothing in the host's park and
/// refuses what a shared park cannot hold.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CoopJoinTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private List<Entity>? entities;
	private Level? current;
	private readonly List<List<Entity>> built = new();

	[TestInitialize]
	public void Initialize()
	{
		entities = Entity.All;
		current = Level.Current;
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var list in built )
		{
			Entity.All = list;
			foreach ( var entity in list.ToArray() )
				entity.Delete();
		}
		Entity.All = new List<Entity>();
		fixture?.Cleanup();
		renderer?.Dispose();
		if ( entities != null )
			Entity.All = entities;
		Level.Current = current!;
	}

	private (Level Level, ParkEconomy Economy, OriginalObject One, List<Entity> Entities) NewPark()
	{
		var park = CoopStreamTests.BuildPark( fixture! );
		built.Add( park.Entities );
		return (park.Level, park.Economy, park.One, park.Entities);
	}

	private static void Enter( (Level Level, ParkEconomy Economy, OriginalObject One, List<Entity> Entities) park )
	{
		Entity.All = park.Entities;
		Level.Current = park.Level;
	}

	private static CoopJoinState SampleState() => new(
		new[] { (0, "jungle/ride.wad#3"), (2, "jungle/shop.wad#1") },
		new[] { (0, 5, 3, 1), (3, 7, -1, 12) },
		9, 4, new[] { 12, 40 }, new DateTime( 2026, 10, 21, 15, 0, 0 ).Ticks,
		new[] { (12, 30, 41, true), (12, 31, 41, false), (40, 5, -2, true) } );

	private static CoopJoinPayload SamplePayload() => new( 7, 120, 3600, 88, 0x1234_5678_9ABC_DEF0, 0x0FED_CBA9_8765_4321, SampleState(),
		Enumerable.Range( 0, 5000 ).Select( index => (byte)(index * 31) ).ToArray() );

	private static void AssertSameState( CoopJoinState expected, CoopJoinState actual )
	{
		CollectionAssert.AreEqual( expected.CarriedEntries.ToArray(), actual.CarriedEntries.ToArray() );
		CollectionAssert.AreEqual( expected.StaffHands.ToArray(), actual.StaffHands.ToArray() );
		Assert.AreEqual( (expected.PendingHire, expected.StaleOnPointer, expected.SessionClockTicks), (actual.PendingHire, actual.StaleOnPointer, actual.SessionClockTicks) );
		CollectionAssert.AreEqual( expected.CoastersBeingEdited.ToArray(), actual.CoastersBeingEdited.ToArray() );
		CollectionAssert.AreEqual( expected.CoasterUnheldCells.ToArray(), actual.CoasterUnheldCells.ToArray() );
	}

	[TestMethod]
	public void ThePayloadRoundTripsAndRefusesEveryMalformedInput()
	{
		var payload = SamplePayload();
		var bytes = payload.Encode();
		var read = CoopJoinPayload.Decode( bytes );
		Assert.AreEqual( (payload.Generation, payload.Records, payload.Ticks, payload.Frame, payload.Digest, payload.WorldHash),
			(read.Generation, read.Records, read.Ticks, read.Frame, read.Digest, read.WorldHash) );
		CollectionAssert.AreEqual( payload.Save, read.Save );
		AssertSameState( payload.State, read.State );
		Assert.IsTrue( bytes.Length < payload.Body().Length, "the body is compressed" );

		// sliced: the same bytes, a chunk of the body a step
		var big = payload with { Save = new byte[3 * CoopJoinPayloadWriter.ChunkBytes + 17] };
		var writer = new CoopJoinPayloadWriter( big );
		var slices = 0;
		while ( !writer.Run( TimeSpan.Zero ) )
			slices++;
		Assert.IsTrue( slices >= 3, "one chunk a slice with no time to spare" );
		CollectionAssert.AreEqual( big.Encode(), writer.Bytes );

		for ( var length = 0; length < bytes.Length; length++ )
			Assert.ThrowsException<InvalidDataException>( () => CoopJoinPayload.Decode( bytes.AsSpan( 0, length ) ), $"cut at {length}" );
		byte[] Patched( Action<byte[]> patch )
		{
			var copy = bytes.ToArray();
			patch( copy );
			return copy;
		}
		Assert.ThrowsException<InvalidDataException>( () => CoopJoinPayload.Decode( Patched( copy => copy[0] = (byte)'X' ) ), "another magic" );
		Assert.ThrowsException<InvalidDataException>( () => CoopJoinPayload.Decode( Patched( copy => copy[8] = 2 ) ), "another version" );
		var body = payload.Body().Length;
		Assert.ThrowsException<InvalidDataException>( () => CoopJoinPayload.Decode( Patched( copy => BinaryPrimitives.WriteUInt32LittleEndian( copy.AsSpan( 14 ), (uint)body - 1 ) ) ),
			"a body longer than it says" );
		Assert.ThrowsException<InvalidDataException>( () => CoopJoinPayload.Decode( Patched( copy => BinaryPrimitives.WriteUInt32LittleEndian( copy.AsSpan( 14 ), (uint)body + 1 ) ) ),
			"a body shorter than it says" );
		Assert.ThrowsException<InvalidDataException>( () => CoopJoinPayload.Decode( Patched( copy => BinaryPrimitives.WriteUInt32LittleEndian( copy.AsSpan( 14 ), uint.MaxValue ) ) ),
			"a declared length past the bound is refused before anything is decompressed" );
		Assert.ThrowsException<InvalidDataException>( () => CoopJoinPayload.Decode( bytes.Concat( new byte[] { 0 } ).ToArray() ), "bytes after the compressed body" );
	}

	[TestMethod]
	public void TheSessionStateRoundTripsAndRefusesEveryMalformedInput()
	{
		var state = SampleState();
		var bytes = state.Encode();
		AssertSameState( state, CoopJoinState.Decode( bytes ) );
		AssertSameState( state with { SessionClockTicks = null, CoastersBeingEdited = Array.Empty<int>() },
			CoopJoinState.Decode( (state with { SessionClockTicks = null, CoastersBeingEdited = Array.Empty<int>() }).Encode() ) );
		for ( var length = 0; length < bytes.Length; length++ )
			Assert.ThrowsException<InvalidDataException>( () => CoopJoinState.Decode( bytes.AsSpan( 0, length ) ), $"cut at {length}" );
		Assert.ThrowsException<InvalidDataException>( () => CoopJoinState.Decode( bytes.Concat( new byte[] { 0 } ).ToArray() ), "a byte after the fields" );
		foreach ( var twice in new[]
		{
			state with { CarriedEntries = new[] { (1, "a"), (1, "b") } },
			state with { StaffHands = new[] { (2, 1, 0, 0), (2, 2, 0, 0) } },
			state with { CoastersBeingEdited = new[] { 5, 5 } },
			state with { CoasterUnheldCells = new[] { (12, 30, 41, true), (12, 30, 41, false) } }
		} )
			Assert.ThrowsException<InvalidDataException>( () => CoopJoinState.Decode( twice.Encode() ), "a player, a coaster or a coaster's cell named twice" );
		Assert.ThrowsException<InvalidOperationException>( () => (state with { CoasterUnheldCells = Enumerable.Range( 0, CoopJoinState.MaximumUnheldCells + 1 )
			.Select( index => (1, index, 0, false) ).ToArray() }).Encode(), "more unheld cells than a join state carries" );
		Assert.ThrowsException<InvalidDataException>( () => CoopJoinState.Decode( (state with { PendingHire = -1 }).Encode() ), "a negative id" );
		// a carried catalogue entry's key may be as long as a command's string (a mod's entry), so every park a command can make is captured
		var longest = state with { CarriedEntries = new[] { (1, new string( 'k', ParkCommandCodec.MaximumString )) } };
		AssertSameState( longest, CoopJoinState.Decode( longest.Encode() ) );
	}

	[TestMethod]
	public void TheSessionStateBringsAReplayedTwinsPerPlayerStateToTheHosts()
	{
		var host = NewPark();
		var twin = NewPark();
		foreach ( var park in new[] { host, twin } )
		{
			Enter( park );
			var commands = park.Level.Commands;
			Assert.IsTrue( commands.Issue( new PickUpObjectCommand( park.One.Visitors.AttractionId ) ).Accepted, "a Move tool's pick-up: the carried entry" );
			// an employee in two players' hands (the hands are the level's state, set as a pick-up's command sets them)
			park.Level.StaffHands!.Carry( 0, park.Economy.Staff.Members.First().Id, (4, 1) );
			park.Level.StaffHands.Carry( 2, park.Economy.Staff.Members.Last().Id, (6, 1) );
			if ( park.Economy.Staff.Candidates.FirstOrDefault() is { } candidate )
				Assert.IsTrue( commands.Issue( new BeginStaffHireCommand( candidate.Id ) ).Accepted, "a pending hire" );
			park.Economy.Staff.StaleOnPointerId = 3;
			Assert.IsTrue( commands.Issue( new SetSessionClockCommand( 63_900_000_000 ) ).Accepted, "the session clock" );
		}
		Enter( host );
		var digest = CoopDigest.Compute( host.Level, out var world );
		Enter( twin );
		Assert.AreEqual( digest, CoopDigest.Compute( twin.Level ), "the twin replayed the host's commands" );

		// what a load of the save resets: the carried entries, the hands, the pending hire and the stale candidate, the session clock
		typeof( Level ).GetField( "carriedEntries", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( twin.Level, null );
		twin.Level.StaffHands!.Clear();
		twin.Economy.Staff.PendingPlacementId = 0;
		twin.Economy.Staff.StaleOnPointerId = 0;
		twin.Level.Objects.ScriptWorld.SessionTime = null;
		Assert.AreNotEqual( digest, CoopDigest.Compute( twin.Level ) );

		Enter( host );
		var state = CoopJoinState.Decode( host.Level.CaptureJoinState().Encode() );
		Assert.AreEqual( 1, state.CarriedEntries.Count );
		Assert.AreEqual( 2, state.StaffHands.Count );
		Assert.IsNotNull( state.SessionClockTicks );
		Enter( twin );
		twin.Level.ApplyJoinState( state );
		Assert.AreEqual( digest, CoopDigest.Compute( twin.Level, out var twinWorld ), "the session state brought back what the digest covers beyond the save" );
		Assert.AreEqual( world, twinWorld );
		Assert.ThrowsException<InvalidDataException>( () => twin.Level.ApplyJoinState( state with { CoastersBeingEdited = new[] { 999 } } ), "a coaster the park does not hold" );
		Assert.ThrowsException<InvalidDataException>( () => twin.Level.ApplyJoinState( state with { CoasterUnheldCells = new[] { (999, 1, 1, true) } } ),
			"marker cells of a coaster the park does not hold" );
	}

	[TestMethod]
	public void TheJoinCaptureChangesNothingInTheHostsParkAndRefusesWhatASharedParkCannotHold()
	{
		var host = NewPark();
		Enter( host );
		var employee = host.Economy.Staff.Members.First();
		host.Level.StaffHands!.Carry( 0, employee.Id, (4, 1) );
		// a hire the player is placing: a save's preparation ends the tool, which cancels it by command
		var candidate = host.Economy.Staff.Candidates.First();
		Assert.IsTrue( host.Level.StaffPlacement!.BeginHire( candidate.Id ) );
		Assert.AreEqual( candidate.Id, host.Economy.Staff.PendingPlacementId );
		var before = (CoopDigest.Compute( host.Level, out var world ), world, host.Level.Commands.Log.Count);
		var save = ParkResumeSaveFile.Serialize( host.Level.CaptureJoinSnapshot() );
		var state = host.Level.CaptureJoinState();
		Assert.AreEqual( before, (CoopDigest.Compute( host.Level, out world ), world, host.Level.Commands.Log.Count),
			"the capture runs no save preparation: no command ends the hire, and the hand keeps its employee" );
		Assert.AreEqual( (employee.Id, candidate.Id), (state.StaffHands.Single().Staff, state.PendingHire) );
		Assert.IsNull( ParkResumeSaveFile.Deserialize( save ).Advisor, "no advisor state: each player's advisor is its own" );

		// a missing economy binding is refused, not repaired: a shared park changes only through its commands
		var bindings = (Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( host.Level )!;
		var record = bindings[host.One];
		bindings.Remove( host.One );
		StringAssert.Contains( Assert.ThrowsException<InvalidOperationException>( () => host.Level.CaptureJoinSnapshot() ).Message, "economy binding" );
		Assert.IsFalse( bindings.ContainsKey( host.One ) );
		bindings[host.One] = record;

		// a park renamed to two empty lines is still captured: the snapshot's display name is the park's level, as every save's is
		typeof( Level ).GetProperty( nameof( Level.ParkName ) )!.SetValue( host.Level, new ParkNameLines( "", "" ) );
		Assert.AreEqual( host.Level.LevelName, ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( host.Level.CaptureJoinSnapshot() ) ).DisplayName );

		// reading the drags makes no set of them on this machine alone, and a set that is empty hashes as none
		Assert.IsNull( typeof( Level ).GetField( "coasterEdits", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( host.Level ) );
		Assert.IsFalse( host.Level.CoasterDragHeld );
		Assert.IsNull( typeof( Level ).GetField( "coasterEdits", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( host.Level ), "the capture's check made no set" );
		var unread = CoopDigest.Compute( host.Level );
		_ = host.Level.CoasterEdits;
		Assert.AreEqual( unread, CoopDigest.Compute( host.Level ), "a set made by a read changes no digest" );

		// a held coaster drag waits: its edited pylon is no part of the save
		var held = (System.Collections.IDictionary)typeof( CoasterEdits ).GetField( "edits", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( host.Level.CoasterEdits )!;
		held.Add( 0, null );
		StringAssert.Contains( Assert.ThrowsException<InvalidOperationException>( () => host.Level.CaptureJoinState() ).Message, "drag" );
	}
}

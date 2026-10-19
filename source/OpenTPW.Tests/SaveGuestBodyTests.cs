using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class SaveGuestBodyTests
{
	internal static IEnumerable<(string Name, byte[] Payload)> Saves()
	{
		yield return ("Easymode", OriginalParkImportTests.ReadEasymodePayload());
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			yield break;
		foreach ( var path in Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ).Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" ).OrderBy( file => file, StringComparer.Ordinal ) )
		{
			using var reader = new SaveReader( path );
			yield return (Path.GetRelativePath( folder, path ), reader.ReadFile());
		}
	}

	private static SaveGuest Decode( SaveTestPayloads.GuestFields fields, int id = 40, int trim = 0 )
	{
		var body = SaveTestPayloads.Guest( fields );
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		return SaveGuest.Read( payload, new SaveThing( id, SaveThingModel.Guest, 0, 0, 8, body.Length - trim ) );
	}

	/// <summary>A guest in the middle of a queue: the links, the queue position, the admission flag, the saved state and the balloon's last position survive a round trip.</summary>
	[TestMethod]
	public void TheQueueAdmissionAndResumeFieldsRoundTrip()
	{
		var saved = Decode( new SaveTestPayloads.GuestFields
		{
			MajorDest = 15, SavedMajorDest = 13, QNext = 41, QPrev = 38, QueuePos = 3, BeenAdmitted = 1, State = 12, SavedState = 7, LastPosX = 0x01020304, LastPosY = 0x05060708,
		} );
		Assert.AreEqual( ((ushort)41, (ushort)38, (byte)3, 1u, 12u, 7u), (saved.QNext, saved.QPrev, saved.QueuePos, saved.BeenAdmitted, saved.State, saved.SavedState) );
		Assert.AreEqual( ((ushort)15, (ushort)13, 0x01020304u, 0x05060708u), (saved.MajorDest, saved.SavedMajorDest, saved.LastPosX, saved.LastPosY) );
		Assert.IsTrue( saved.Admitted );
		Assert.IsTrue( saved.InQueueChain );
		Assert.IsFalse( Decode( new SaveTestPayloads.GuestFields() ).InQueueChain, "a fresh guest has no links and no queue position" );
	}

	/// <summary>In state 14 the queue-position byte is the sideshow-win flag the state's entry drew (0x501EF8 → 0x4E2670); elsewhere it is a position.</summary>
	[TestMethod]
	public void TheQueuePositionByteIsTheSideshowOutcomeInState14()
	{
		Assert.AreEqual( true, Decode( new SaveTestPayloads.GuestFields { QueuePos = 1, State = 14, MajorDest = 15 } ).SideshowWon );
		Assert.AreEqual( false, Decode( new SaveTestPayloads.GuestFields { QueuePos = 0, State = 14, MajorDest = 15 } ).SideshowWon );
		var standing = Decode( new SaveTestPayloads.GuestFields { QueuePos = 1, State = 11, MajorDest = 15 } );
		Assert.IsNull( standing.SideshowWon, "in state 11 the byte is the queue position" );
		Assert.AreEqual( (byte)1, standing.QueuePos );
	}

	/// <summary>The event ring (thought object +4, 32 slots of type and value words; AddEvent 0x50C100 writes slot head and advances with &amp; 0x1F).</summary>
	[TestMethod]
	public void TheEventRingDecodesIntoTypedSlotsNewestFirst()
	{
		var events = new uint[32];
		events[0] = 6u | 15u << 16; // type 6 (dismissed from queue), value 15
		events[1] = 0x13u;
		events[31] = 0x15u | 99u << 16;
		var person = Decode( new SaveTestPayloads.GuestFields { ActionHistIndex = 2, Events = events } ).Person;
		Assert.AreEqual( 2u, person.ActionHistIndex );
		Assert.AreEqual( new SaveThoughtEvent( 6, 15 ), person.ThoughtEvents[0] );
		Assert.AreEqual( new SaveThoughtEvent( 0x13, 0 ), person.ThoughtEvents[1] );
		CollectionAssert.AreEqual( new[] { new SaveThoughtEvent( 0x13, 0 ), new SaveThoughtEvent( 6, 15 ), new SaveThoughtEvent( 0x15, 99 ) }, person.RecentEvents().ToArray(),
			"from the slot before the head, round the ring past slot 0 to slot 31, without the empty slots" );
		Assert.AreEqual( 0, Decode( new SaveTestPayloads.GuestFields() ).Person.RecentEvents().Count(), "the constructor 0x50BA80 leaves an empty ring" );
	}

	[TestMethod]
	public void DamagedGuestFieldsAreRefused()
	{
		var good = new SaveTestPayloads.GuestFields();
		Decode( good );
		Assert.ThrowsException<InvalidDataException>( () => Decode( good with { ActionHistIndex = 32 } ), "the ring wraps at 32" );
		Assert.ThrowsException<InvalidDataException>( () => Decode( good with { Events = new uint[] { 0x1A } } ), "AddEvent is called with types 1 to 0x19" );
		Assert.ThrowsException<InvalidDataException>( () => Decode( good with { State = 22 } ), "the state setter takes 0 to 21" );
		Assert.ThrowsException<InvalidDataException>( () => Decode( good with { SavedState = 99 } ) );
		Assert.ThrowsException<InvalidDataException>( () => Decode( good with { BeenAdmitted = 2 } ), "0x5012B0 and 0x5012F0 write 1 and 0" );
		Assert.ThrowsException<InvalidDataException>( () => Decode( good with { Happiness = -1 } ), "a need is a byte" );
		Assert.ThrowsException<InvalidDataException>( () => Decode( good with { Thirst = float.NaN } ) );
		Assert.ThrowsException<InvalidDataException>( () => Decode( good, trim: 1 ), "a body one byte short" );
		Assert.ThrowsException<InvalidDataException>( () => Decode( good, trim: 100 ), "a body that ends inside the person block" );
		var model = new SaveThing( 40, SaveThingModel.Attraction, 0, 0, 8, 525 );
		Assert.ThrowsException<ArgumentException>( () => SaveGuest.Read( new byte[600], model ), "only a guest thing is read as a guest" );
	}

	/// <summary>
	/// Every guest body of every available save (Easymode and <c>OPENTPW_TPWS_SAVES</c>) is consumed to the last byte by the guest serializer's reader
	/// (<see cref="SaveGuest.Read"/> throws otherwise) and holds only values the loader's own code can have written.
	/// </summary>
	[TestMethod]
	public void EveryGuestBodyOfEverySaveIsConsumedExactly()
	{
		var guests = 0;
		var skipped = new List<string>();
		foreach ( var (name, payload) in Saves() )
		{
			SaveWorld world;
			try
			{
				world = SaveWorld.Parse( payload, SavePayloadLayout.Parse( payload ), 128, 128 );
			}
			catch ( InvalidDataException )
			{
				skipped.Add( name ); // a restart .INTS save the World reader does not take
				continue;
			}
			foreach ( var thing in world.OfModel( SaveThingModel.Guest ) )
			{
				var guest = SaveGuest.Read( payload, thing );
				var where = $"{name} guest {guest.Id}";
				Assert.AreEqual( SaveGuest.GuestBytes + SaveThingChain.PersonBytes, thing.BodyLength, where );
				Assert.IsTrue( guest.State <= SaveGuest.MaximumState && guest.SavedState <= SaveGuest.MaximumState, where );
				Assert.IsTrue( guest.Person.ActionHistIndex < SavePerson.EventHistoryEntries, where );
				Assert.AreEqual( 32, guest.Person.ThoughtEvents.Count, where );
				// every saved guest is still in its arrival phase or leaving: unnumbered, unqueued, never admitted, no balloon position
				Assert.IsFalse( guest.InQueueChain || guest.Admitted, $"{where} is queued or admitted" );
				Assert.AreEqual( (0u, 0u, 0u, 0u), (guest.ArrivalIndex, guest.LastPosX, guest.LastPosY, guest.SavedState - 6), where );
				guests++;
			}
		}
		Assert.IsFalse( skipped.Any( name => name.EndsWith( ".TPWS", StringComparison.OrdinalIgnoreCase ) ), $"skipped: {string.Join( ", ", skipped )}" );
		Assert.IsTrue( guests >= 13, $"Easymode alone holds 13 guests; read {guests}" );
		if ( !string.IsNullOrWhiteSpace( Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" ) ) )
			Assert.AreEqual( 35, guests, "Easymode's 13 plus the 22 of the seven private .TPWS saves" );
	}
}

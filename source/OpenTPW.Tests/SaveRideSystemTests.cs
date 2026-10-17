using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The framed walk of the save's RideSystem block (writer 0x464140, loader 0x4647a0): the counts, the per-slot
/// presence bytes and the typed records. The original Jungle Easymode.TPWI fixture pins the real shape; synthetic
/// blocks prove the record decoding and every refusal; the corpus runner walks every save with the same reader.
/// </summary>
[TestClass]
public class SaveRideSystemTests
{
	[TestMethod]
	public void WalksTheOriginalJungleEasymodeBlockAndDerivesItsControlCounts()
	{
		if ( !File.Exists( Path.Combine( OriginalParkImportTests.OriginalDataPath(), "levels", "jungle", "Easymode.TPWI" ) ) )
			Assert.Inconclusive( "The original Jungle Easymode.TPWI fixture is missing." );
		var payload = OriginalParkImportTests.ReadEasymodePayload();
		var layout = SavePayloadLayout.Parse( payload );
		var block = SaveRideSystemBlock.Parse( payload, layout );

		Assert.AreEqual( 1_577_508, block.Offset, "the RideSystem block starts after the GameSystem delimiter" );
		Assert.AreEqual( (17522u, 161u, 7u, 90u), (block.Length, block.Used, block.Free, block.FirstFreeSlot) );
		Assert.AreEqual( 168, block.SlotCount );
		Assert.AreEqual( 161, block.Records.Length, "every used slot except the seven free holes 90–96" );
		Assert.AreEqual( 0, block.TableControlCounts, "no table was given: the block's one complete parse decides every control count" );
		// Ground truth read with python3 -I over the original save: kind 815 marks the 11 buildable records, 826
		// the 147 scenery records and 865 the 3 fixed items; the packed instance flags are only 0 or 8 and the
		// node-state words keep the writer's bits (the parser refuses the others).
		var kinds = block.Records.GroupBy( record => record.Kind ).OrderBy( group => group.Key )
			.Select( group => (Key: group.Key, Count: group.Count()) ).ToArray();
		CollectionAssert.AreEqual( new[] { (815, 11), (826, 147), (865, 3) }, kinds );
		Assert.IsTrue( block.Records.All( record => record.InstanceFlags is 0 or 8 ) );
		Assert.IsTrue( block.Records.All( record => record.NodeStates.All( state => state.Packed <= SaveRideSystemBlock.ValidNodeStateBits ) ) );
		// Slot 114 holds the Jungle Spray (Info.Id 1303): 31 node states, 12 embedded nodes and three animation
		// time controls — the only real save record whose control count is not 1.
		var jungleSpray = block.Records.Single( record => record.Slot == 114 );
		Assert.AreEqual( (1303, 4, 31, 12, 3), (jungleSpray.InfoId, jungleSpray.Index, jungleSpray.NodeCount, jungleSpray.EmbeddedCount, jungleSpray.Controls.Length) );
		Assert.AreEqual( 160, block.Records.Count( record => record.Controls.Length == 1 ), "the saver stores one control per record with T=1 everywhere else" );
		// A control's words stay raw: word 0 is the flags word, word 1 the animation set, the rest are the
		// animation's times and floats that the loader bounds nowhere.
		foreach ( var record in block.Records )
		{
			foreach ( var control in record.Controls )
			{
				Assert.AreEqual( 11, control.Words.Length );
				Assert.AreEqual( (uint)control.Words[0], control.Flags );
				Assert.AreEqual( (uint)control.Words[1], control.AnimationSet );
			}
		}
	}

	[TestMethod]
	public void OriginalEasymodePayloadRefusesABrokenPresenceByte()
	{
		if ( !File.Exists( Path.Combine( OriginalParkImportTests.OriginalDataPath(), "levels", "jungle", "Easymode.TPWI" ) ) )
			Assert.Inconclusive( "The original Jungle Easymode.TPWI fixture is missing." );
		var payload = OriginalParkImportTests.ReadEasymodePayload();
		var section = SavePayloadLayout.Parse( payload )[SaveBlock.RideSystem];
		var broken = (byte[])payload.Clone();
		broken[section.Offset + 16] = 2; // the first slot's presence byte 1 -> 2
		var error = Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( broken, SavePayloadLayout.Parse( broken ) ) );
		StringAssert.Contains( error.Message, "does not walk" );
	}

	[TestMethod]
	public void DecodesSyntheticRecordsWithEmbeddedNodesNodeStatesAndControls()
	{
		var world = World();
		var payload = SaveTestPayloads.Payload( world, SaveTestPayloads.RideSystem( new[]
		{
			SaveTestPayloads.RideSystemRecord( 1303, 2, 1, 3, 3, SaveObject.PlacedObjectKind, 4, rotation: 90,
				instanceFlags: 8, instanceWord: 55, instanceDword: 0x1234,
				embeddedNodes: new[] { (1u, 2u), (3u, 4u) }, nodeStates: new[] { 0xFu, 0x20u },
				controls: new[]
				{
					SaveTestPayloads.RideSystemControl( 9, 3, 5, 6, 7, 8, 4, 10, 11, 12, 13 ),
					SaveTestPayloads.RideSystemControl( 20, 2, 0, 0, 0, 0, 0, 0, 0, 0, 22 ),
				} ),
			SaveTestPayloads.RideSystemGap(),
			SaveTestPayloads.RideSystemRecord( 1601, 0, 0, 1, 1, SaveObject.FixedItemKind, 5 ),
		} ) );
		var layout = SavePayloadLayout.Parse( payload );
		var block = SaveRideSystemBlock.Parse( payload, layout );

		Assert.AreEqual( (2u, 1u, 1u), (block.Used, block.Free, block.FirstFreeSlot), "the single free hole starts at slot 1" );
		var record = block.Records[0];
		Assert.AreEqual( (0, 1303, 2, 1, 3, 3, 815, 4, 90), (record.Slot, record.InfoId, record.X, record.Y, record.Width, record.Height, record.Kind, record.Index, record.Rotation) );
		Assert.AreEqual( (8u, (ushort)55, 0x1234u), (record.InstanceFlags, record.InstanceWord, record.InstanceDword) );
		Assert.AreEqual( layout[SaveBlock.RideSystem].Offset + 16, record.Offset, "the presence byte sits after the length word and the three counters" );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 4 }, record.EmbeddedNodes.SelectMany( node => new[] { node.First, node.Second } ).ToArray() );
		Assert.AreEqual( (0xFu, 0x18030u), (record.NodeStates[0].Packed, record.NodeStates[0].Flags), "bits 0–3 map to 0x10, 0x20, 0x8000 and 0x10000" );
		Assert.AreEqual( (0x20u, 0x40000u), (record.NodeStates[1].Packed, record.NodeStates[1].Flags), "bit 5 maps to 0x40000" );
		Assert.AreEqual( (9u, 3u), (record.Controls[0].Flags, record.Controls[0].AnimationSet) );
		Assert.AreEqual( (20u, 2u), (record.Controls[1].Flags, record.Controls[1].AnimationSet) );
		var bare = block.Records[1];
		Assert.AreEqual( (2, 1601, 865, 5, 0, 0, 1), (bare.Slot, bare.InfoId, bare.Kind, bare.Index, bare.NodeCount, bare.EmbeddedCount, bare.Controls.Length), "a record without a control list stores the one default control" );
	}

	[TestMethod]
	public void WalksRecordsWhoseControlCountsOnlyOneParseSatisfies()
	{
		var payload = SaveTestPayloads.Payload( World(), SaveTestPayloads.RideSystem( new[]
		{
			SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 1,
				controls: new[] { SaveTestPayloads.RideSystemControl( 1, 12, 2, 2, 2, 2, 2, 2, 2, 2, 2 ) } ),
			SaveTestPayloads.RideSystemRecord( 1100, 2, 2, 1, 1, SaveObject.PlacedObjectKind, 2,
				controls: Enumerable.Range( 0, 3 ).Select( index => SaveTestPayloads.RideSystemControl( 2, (uint)index, 1, 1, 1, 1, 1, 1, 1, 1, 1 ) ).ToArray() ),
		} ) );
		var block = SaveRideSystemBlock.Parse( payload, SavePayloadLayout.Parse( payload ) );
		Assert.AreEqual( (1, 3), (block.Records[0].Controls.Length, block.Records[1].Controls.Length), "the only complete parse" );
		Assert.AreEqual( (1u, 12u), (block.Records[0].Controls[0].Flags, block.Records[0].Controls[0].AnimationSet) );
	}

	/// <summary>
	/// A two-slot frame with two complete parses: record A (one control) then a four-control record B, or record A
	/// with three controls (B's first two controls become A's) then the record hidden at B's offset 88 with two
	/// controls. The old "smallest feasible control count" rule picked the first and called it unique.
	/// </summary>
	private static byte[] AmbiguousBlock()
	{
		var inner = SaveTestPayloads.RideSystemRecord( 1102, 3, 3, 1, 1, SaveObject.PlacedObjectKind, 3,
			controls: new[] { SaveTestPayloads.DefaultRideSystemControl, SaveTestPayloads.DefaultRideSystemControl } );
		var outer = SaveTestPayloads.RideSystemRecord( 1101, 2, 2, 1, 1, SaveObject.PlacedObjectKind, 2,
			controls: Enumerable.Repeat( SaveTestPayloads.DefaultRideSystemControl, 4 ).ToArray() );
		Assert.AreEqual( 1 + 0x36 + 4 * 44, outer.Length );
		inner.CopyTo( outer, 88 );
		return SaveTestPayloads.RideSystem( new[] { SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 1 ), outer } );
	}

	[TestMethod]
	public void RefusesABlockWithTwoCompleteParsesInsteadOfPickingTheSmallest()
	{
		var payload = SaveTestPayloads.Payload( World(), AmbiguousBlock() );
		var layout = SavePayloadLayout.Parse( payload );
		var error = Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( payload, layout ) );
		StringAssert.Contains( error.Message, "more than one way" );
	}

	[TestMethod]
	public void ATableOfControlCountsPicksTheParseItNames()
	{
		var payload = SaveTestPayloads.Payload( World(), AmbiguousBlock() );
		var layout = SavePayloadLayout.Parse( payload );
		var first = SaveRideSystemBlock.Parse( payload, layout, infoId => infoId switch { 1100 => 1, 1101 => 4, _ => null } );
		CollectionAssert.AreEqual( new[] { (1100, 1), (1101, 4) }, first.Records.Select( record => (record.InfoId, record.Controls.Length) ).ToArray() );
		Assert.AreEqual( 2, first.TableControlCounts );
		var second = SaveRideSystemBlock.Parse( payload, layout, infoId => infoId switch { 1100 => 3, 1102 => 2, _ => null } );
		CollectionAssert.AreEqual( new[] { (1100, 3), (1102, 2) }, second.Records.Select( record => (record.InfoId, record.Controls.Length) ).ToArray() );
		// A table that no parse satisfies refuses the block; a table with a missing id leaves that record to the search.
		Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( payload, layout, infoId => infoId == 1100 ? 2 : null ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( payload, layout, _ => null ) );
		var oneKnown = SaveRideSystemBlock.Parse( payload, layout, infoId => infoId == 1100 ? 1 : null );
		Assert.AreEqual( 1, oneKnown.TableControlCounts );
		Assert.AreEqual( 4, oneKnown.Records[1].Controls.Length );
	}

	[TestMethod]
	public void ARecordHasAtLeastOneControl()
	{
		// tp.exe 0x4629d0 replaces a control count of 0 with 1 before it builds the type entry, so no record stores none.
		var payload = SaveTestPayloads.Payload( World(), SaveTestPayloads.RideSystem( new[]
		{
			SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 1, controls: Array.Empty<uint[]>() ),
		} ) );
		var error = Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( payload, SavePayloadLayout.Parse( payload ), _ => 0 ) );
		StringAssert.Contains( error.Message, "does not walk" );
		Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( payload, SavePayloadLayout.Parse( payload ) ) );
	}

	[TestMethod]
	public void SearchesALargeFrameWithoutRecursion()
	{
		// 50,000 absent slots (used 0, free 50,000, first free 0): the old recursive search recursed once per slot.
		var slots = Enumerable.Range( 0, 50_000 ).Select( _ => SaveTestPayloads.RideSystemGap() ).ToArray();
		var payload = SaveTestPayloads.Payload( World(), SaveTestPayloads.RideSystem( slots ) );
		var block = SaveRideSystemBlock.Parse( payload, SavePayloadLayout.Parse( payload ) );
		Assert.AreEqual( (0u, 50_000u, 0u), (block.Used, block.Free, block.FirstFreeSlot) );
		Assert.AreEqual( 0, block.Records.Length );
		// A used record at the end of the same long frame.
		var withRecord = SaveTestPayloads.Payload( World(), SaveTestPayloads.RideSystem( slots.Concat( new[] { SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 1 ) } ).ToArray() ) );
		Assert.AreEqual( 50_000, SaveRideSystemBlock.Parse( withRecord, SavePayloadLayout.Parse( withRecord ) ).Records[0].Slot );
	}

	[TestMethod]
	public void RefusesABlockWhoseSearchExceedsItsBudget()
	{
		// One record followed by 20,000 absent slots and 3,000 spare zero bytes: each of the 64 candidate control counts
		// leaves a distinct chain of 20,000 states, a 1.28-million-state search that the budget refuses (the old search spent
		// ~50,000 calls on 1.5 KiB of such bytes and could recurse without bound).
		var slots = new List<byte[]> { SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 1 ) };
		slots.AddRange( Enumerable.Range( 0, 20_000 ).Select( _ => SaveTestPayloads.RideSystemGap() ) );
		slots.Add( new byte[3_000] );
		var rideSystem = SaveTestPayloads.RideSystem( slots );
		// Fix the counters up: the extra zero bytes are not slots.
		BinaryPrimitives.WriteUInt32LittleEndian( rideSystem.AsSpan( 8 ), 20_000 );
		BinaryPrimitives.WriteUInt32LittleEndian( rideSystem.AsSpan( 12 ), 1 );
		var payload = SaveTestPayloads.Payload( World(), rideSystem );
		var layout = SavePayloadLayout.Parse( payload );
		var error = Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( payload, layout ) );
		StringAssert.Contains( error.Message, "search exceeds" );
		// With the table naming the record's count the same bytes are a plain mismatch, not a search.
		var pinned = Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( payload, layout, _ => 1 ) );
		StringAssert.Contains( pinned.Message, "does not walk" );
	}

	[TestMethod]
	public void RejectsBrokenRideSystemBlocks()
	{
		var world = World();

		void Refuse( byte[] rideSystem, params string[] messageParts )
		{
			var payload = SaveTestPayloads.Payload( world, rideSystem );
			var error = Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( payload, SavePayloadLayout.Parse( payload ) ) );
			foreach ( var part in messageParts )
				StringAssert.Contains( error.Message, part );
		}

		static byte[] Record( uint flags = 0, uint[]? states = null, ushort nodeCount = 0, (uint First, uint Second)[]? embedded = null, int controls = 1 )
		{
			var slot = SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 1, instanceFlags: flags,
				embeddedNodes: embedded, nodeStates: states, controls: Enumerable.Range( 0, Math.Max( controls, 0 ) )
					.Select( _ => SaveTestPayloads.RideSystemControl( 1, 12, 2, 2, 2, 2, 2, 2, 2, 2, 2 ) ).ToArray() );
			if ( nodeCount > 0 )
				BinaryPrimitives.WriteUInt16LittleEndian( slot.AsSpan( 1 + 0x2A ), nodeCount );
			return slot;
		}

		/// <summary>The framed block with one u32 word replaced.</summary>
		static byte[] Word( byte[] bytes, int offset, uint value )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( offset ), value );
			return bytes;
		}

		// The block carries only its length word; the used/free/first-free counts do not fit behind it.
		{
			var payload = SaveTestPayloads.Payload( world, BitConverter.GetBytes( 8u ).Concat( new byte[8] ).ToArray() );
			var error = Assert.ThrowsException<InvalidDataException>( () => SaveRideSystemBlock.Parse( payload, SavePayloadLayout.Parse( payload ) ) );
			StringAssert.Contains( error.Message, "no used, free and first-free counts" );
		}
		// The block's traced width is exactly its length word (SaveBlockWidths 0x4647a0): the split refuses a length
		// word that does not fit the payload, and any other miscount stops at the delimiter check, so the walk only
		// ever runs on a block whose length word already agrees with the bytes.
		var framed = SaveTestPayloads.RideSystem( new[] { Record() } );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () =>
			SavePayloadLayout.Parse( SaveTestPayloads.Payload( world, Word( framed, 0, 9999 ) ) ) ).Message, "truncated" );
		// A presence byte of 2, where a record should follow.
		Refuse( SaveTestPayloads.RideSystem( new[] { Record(), new byte[] { 2 } } ), "does not walk" );
		// A stored first free slot of 0 while the only absent slot is the second.
		Refuse( Word( SaveTestPayloads.RideSystem( new[] { Record(), SaveTestPayloads.RideSystemGap() } ), 12, 0 ), "does not walk" );
		// free = 0 with firstFree = 0, which is not the used count.
		Refuse( Word( SaveTestPayloads.RideSystem( new[] { Record() } ), 12, 0 ), "does not name" );
		// A record that lists more node states than the bound, and one inside the bound with no words behind it.
		Refuse( SaveTestPayloads.RideSystem( new[] { Record( nodeCount: 0x401 ) } ), "does not walk" );
		Refuse( SaveTestPayloads.RideSystem( new[] { Record( nodeCount: 0x400 ) } ), "does not walk" );
		// Instance flags with a bit above bit 8, which the writer never packs.
		Refuse( SaveTestPayloads.RideSystem( new[] { Record( flags: 0x200 ) } ), "does not walk" );
		// A node-state word with bit 12, and one with bit 4 (0x10), which the writer never sets: the bit mask, not a
		// numeric comparison, decides (0x10 is below the sparse mask 0xFEF as a number).
		Refuse( SaveTestPayloads.RideSystem( new[] { Record( states: new uint[] { 0x1001 } ) } ), "does not walk" );
		Refuse( SaveTestPayloads.RideSystem( new[] { Record( states: new uint[] { 0x10 } ) } ), "does not walk" );
		Refuse( SaveTestPayloads.RideSystem( new[] { Record( states: new uint[] { 0x0F, 0x10 } ) } ), "does not walk" );
		// An early record whose flags or node states are invalid cannot be walked even when a later control count would line up.
		Refuse( SaveTestPayloads.RideSystem( new[] { Record( states: new uint[] { 0x10 }, controls: 3 ), Record() } ), "does not walk" );
		// An empty frame is exactly its three zero counters: trailing bytes, or a first free slot other than 0, are refused.
		Refuse( BitConverter.GetBytes( 13u ).Concat( new byte[12] ).Concat( new byte[] { 0 } ).ToArray(), "no slots" );
		Refuse( Word( BitConverter.GetBytes( 12u ).Concat( new byte[12] ).ToArray(), 12, 1 ), "no slots" );
		// Counts whose sum wraps a 32-bit integer: used + free = 2^32 + 1 would pass a wrapped check against the block's bytes.
		Refuse( Word( Word( BitConverter.GetBytes( 12u ).Concat( new byte[12] ).ToArray(), 4, uint.MaxValue ), 8, 2 ), "above the" );
		Refuse( Word( Word( BitConverter.GetBytes( 12u ).Concat( new byte[12] ).ToArray(), 4, int.MaxValue ), 8, int.MaxValue ), "above the" );
		// More slots than the block has bytes for.
		Refuse( Word( Word( BitConverter.GetBytes( 12u ).Concat( new byte[12] ).ToArray(), 4, 5 ), 8, 0 ), "more than its" );

		var clean = SaveTestPayloads.Payload( world, SaveTestPayloads.RideSystem( new[] { Record() } ) );
		var cleanLayout = SavePayloadLayout.Parse( clean );
		Assert.ThrowsException<ArgumentException>( () => SaveRideSystemBlock.Parse( clean.AsSpan( 1 ), cleanLayout ) );
	}

	/// <summary>A minimal full world block over a 3×3 synthetic map (the layout measurer walks it).</summary>
	private static byte[] World() =>
		SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 ) ), SaveTestPayloads.Things( SaveTestPayloads.ParkThings() ) );
}

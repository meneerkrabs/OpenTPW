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
		Assert.IsTrue( block.UniquelyWalked, "every derived control count is strictly minimal" );
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
		Assert.AreEqual( (2, 1601, 865, 5, 0, 0, 0), (bare.Slot, bare.InfoId, bare.Kind, bare.Index, bare.NodeCount, bare.EmbeddedCount, bare.Controls.Length) );
		Assert.IsTrue( block.UniquelyWalked );
	}

	[TestMethod]
	public void WalksEachDerivedControlCountAtItsSmallestValue()
	{
		var payload = SaveTestPayloads.Payload( World(), SaveTestPayloads.RideSystem( new[]
		{
			SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 1,
				controls: new[] { SaveTestPayloads.RideSystemControl( 1, 12, 2, 2, 2, 2, 2, 2, 2, 2, 2 ) } ),
			SaveTestPayloads.RideSystemRecord( 1100, 2, 2, 1, 1, SaveObject.PlacedObjectKind, 2,
				controls: Enumerable.Range( 0, 3 ).Select( index => SaveTestPayloads.RideSystemControl( 2, (uint)index, 1, 1, 1, 1, 1, 1, 1, 1, 1 ) ).ToArray() ),
		} ) );
		var block = SaveRideSystemBlock.Parse( payload, SavePayloadLayout.Parse( payload ) );
		Assert.AreEqual( (1, 3), (block.Records[0].Controls.Length, block.Records[1].Controls.Length), "the walk satisfies the counts with the smallest T per record" );
		Assert.AreEqual( (1u, 12u), (block.Records[0].Controls[0].Flags, block.Records[0].Controls[0].AnimationSet) );
		Assert.IsTrue( block.UniquelyWalked );
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

		static byte[] Record( uint flags = 0, uint[]? states = null, ushort nodeCount = 0, (uint First, uint Second)[]? embedded = null, int controls = 0 )
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
		Refuse( SaveTestPayloads.RideSystem( new[] { Record( flags: 0x200 ) } ), "instance-flag word" );
		// A node-state word with bit 12, which the writer never sets.
		Refuse( SaveTestPayloads.RideSystem( new[] { Record( states: new uint[] { 0x1001 } ) } ), "node state word" );

		var clean = SaveTestPayloads.Payload( world, SaveTestPayloads.RideSystem( new[] { Record() } ) );
		var cleanLayout = SavePayloadLayout.Parse( clean );
		Assert.ThrowsException<ArgumentException>( () => SaveRideSystemBlock.Parse( clean.AsSpan( 1 ), cleanLayout ) );
	}

	/// <summary>A minimal full world block over a 3×3 synthetic map (the layout measurer walks it).</summary>
	private static byte[] World() =>
		SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 ) ), SaveTestPayloads.Things( SaveTestPayloads.ParkThings() ) );
}

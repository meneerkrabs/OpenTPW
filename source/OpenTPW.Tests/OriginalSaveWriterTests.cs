using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The original-format writer on synthetic payloads (no original bytes needed): the block order, the delimiters, the container and the models' strictness.</summary>
[TestClass]
public class OriginalSaveWriterTests
{
	private static OriginalSavePayload Blank() => OriginalSavePayloadFixtures.Blank( OriginalSaveImportTests.Settings() );

	[TestMethod]
	public void ABlankPayloadWritesInTheOriginalBlockOrderAndReadsBack()
	{
		var bytes = Blank().Write();
		var layout = SavePayloadLayout.Parse( bytes );
		Assert.AreEqual( 19, layout.Blocks.Count );
		CollectionAssert.AreEqual( Enum.GetValues<SaveBlock>(), layout.Blocks.Select( block => block.Block ).ToArray() );
		foreach ( var (block, tag) in SavePayloadLayout.Delimiters )
		{
			var stored = Encoding.ASCII.GetBytes( tag ).Reverse().ToArray();
			CollectionAssert.AreEqual( stored, bytes.AsSpan( layout[block].DelimiterOffset, 4 ).ToArray(), $"{tag} is stored byte-reversed after the {block} block" );
		}
		var again = OriginalSavePayload.Read( bytes );
		CollectionAssert.AreEqual( bytes, again.Write() );
		Assert.AreEqual( 2u, again.World.Variables.Version );
		Assert.AreEqual( 0x4000, again.World.Cells.Length );
	}

	[TestMethod]
	public void TheContainerIsReadBackByTheReaderWithTheBilzHeaderTheGameWrites()
	{
		var payload = Blank().Write();
		var fields = new SaveContainerFields( SaveContainerFields.WriterMagic, 4, "A banner of the writer's own", Enumerable.Range( 0, 32 ).Select( value => (byte)value ).ToArray() );
		var file = SaveContainerWriter.Write( fields, payload );
		using var reader = new SaveReader( new MemoryStream( file ) );
		var info = reader.Inspect();
		Assert.AreEqual( (500u, (byte)4, 0x01221985u, payload.Length, file.Length - 0x60d), (info.Magic, info.Language, info.Stamp, info.DecodedLength, info.CompressedChunkLength) );
		CollectionAssert.AreEqual( payload, reader.ReadFile() );
		var header = reader.InspectHeader();
		Assert.AreEqual( fields.Banner, header.Banner );
		CollectionAssert.AreEqual( fields.RequiredContent, header.RequiredContent );
		Assert.AreEqual( 0u, header.OnlineFlag );
		// the chunk header: 'BILZ', the payload length, the chunk length with its 0x1C header, window bits 15, memory level 9, two zero words
		var chunk = 0x60d;
		Assert.AreEqual( "BILZ", Encoding.ASCII.GetString( file, chunk, 4 ) );
		CollectionAssert.AreEqual( new uint[] { (uint)payload.Length, (uint)(file.Length - chunk), 15, 9, 0, 0 },
			Enumerable.Range( 0, 6 ).Select( index => BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( chunk + 4 + index * 4 ) ) ).ToArray() );
		// the banner field holds the text, a NUL and zeros up to 0x500 bytes; the header block is the bitmap then zeros
		Assert.IsTrue( file.AsSpan( 5 + fields.Banner.Length * 2, 0x500 - fields.Banner.Length * 2 ).ToArray().All( value => value == 0 ) );
		Assert.IsTrue( file.AsSpan( 0x505 + 32, 0x100 - 32 ).ToArray().All( value => value == 0 ) );
		CollectionAssert.AreEqual( file, SaveContainerWriter.Write( SaveContainerFields.Of( reader ), payload ), "the fields of a container written again give the same container" );
	}

	[TestMethod]
	public void TheContainerWriterRefusesFieldsTheOriginalCannotHold()
	{
		var payload = Blank().Write();
		var bitmap = new byte[32];
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SaveContainerWriter.Write( new SaveContainerFields( 500, 7, "", bitmap ), payload ) );
		Assert.ThrowsException<ArgumentException>( () => SaveContainerWriter.Write( new SaveContainerFields( 500, 0, new string( 'x', 0x280 ), bitmap ), payload ) );
		Assert.ThrowsException<ArgumentException>( () => SaveContainerWriter.Write( new SaveContainerFields( 500, 0, "", new byte[31] ), payload ) );
	}

	[TestMethod]
	public void TheBanksBodyWritesBackToItsSerializersBytes()
	{
		var settings = OriginalSaveImportTests.Settings();
		var bank = OriginalSavePayloadFixtures.Bank( settings, balance: -1234 ) with { Account = new SaveBankRecord( 0, 25, -1234, 9, false, -500, 77, -4242 ) };
		var body = bank.ToBody();
		Assert.AreEqual( SaveBank.Bytes, body.Length );
		var read = SaveBank.Read( body, new SaveThing( 1, SaveThingModel.Bank, 0, 0, 0, body.Length ) );
		Assert.AreEqual( bank.Account with { Offset = read.Account.Offset }, read.Account );
		CollectionAssert.AreEqual( body, read.ToBody() );
		// the words sit where the serializer puts them: base 8, then fee, balance, batch, withdrawals, last balance, turn entered red, profit
		Assert.AreEqual( 25, BinaryPrimitives.ReadInt32LittleEndian( body.AsSpan( 8 ) ) );
		Assert.AreEqual( -1234, BinaryPrimitives.ReadInt32LittleEndian( body.AsSpan( 12 ) ) );
		Assert.AreEqual( -4242, BinaryPrimitives.ReadInt32LittleEndian( body.AsSpan( 32 ) ) );
	}

	[TestMethod]
	public void ABrokenThingChainOrAShortListIsRefusedAtWriteTime()
	{
		var broken = Blank();
		broken.World.Things.Add( new SaveThingEntry { Id = 9, Next = 0, Model = SaveThingModel.UIMsgReceiver, Body = new byte[8] } );
		Assert.ThrowsException<InvalidOperationException>( () => broken.Write() );
		var lists = Blank();
		lists.MessageCentre.Lists.RemoveAt( 0 );
		Assert.ThrowsException<InvalidOperationException>( () => lists.Write() );
		var scripts = Blank();
		scripts.Scripts.SlotWords[5] = 1;
		Assert.ThrowsException<InvalidOperationException>( () => scripts.Write(), "a nonzero script slot needs its record" );
	}

	[TestMethod]
	public void ModelsReadTheBlocksTheirLoadersReadAndNothingElse()
	{
		var payload = Blank();
		payload.Scripts.SlotWords[3] = 0xDEAD;
		payload.Scripts.Records.Add( Enumerable.Range( 0, SaveBlockWidths.ScriptRecordBytes ).Select( value => (byte)value ).ToArray() );
		payload.Particles = new SaveParticlesData { On = true, Globals = new byte[SaveBlockWidths.ParticleHeaderBytes], Pool = new byte[SaveBlockWidths.ParticleFieldBytes] };
		payload.FlyingRides = new SaveFlyingRidesData { On = true, Array = new byte[8], Words = Enumerable.Range( 0, 10 ).Select( value => value == 9 ? 6u : (uint)value ).ToArray() };
		BinaryPrimitives.WriteUInt32LittleEndian( payload.FlyingRides.Array.AsSpan( 4 ), 1 );
		payload.FlyingRides.Bodies.Add( new byte[] { 1, 2, 3, 4, 5, 6 } );
		payload.MessageCentre.Lists[2] = new ushort[] { 7, 8, 9 };
		payload.Rsse.Records.Add( new SaveRsseRecordData
		{
			Buffers = Enumerable.Range( 0, SaveRsseBlock.BufferCount ).Select( index => index == 6 ? new byte[SaveRsseRecord.WalkEntryBytes * 2] : new byte[index == 0 ? 8 : 3] ).ToArray(),
			Folder = "a\\b\0"u8.ToArray(), Objects = { new byte[SaveRsseObject.Bytes] },
		} );
		payload.Sound = new SaveSoundData { On = true, Word = 4, Settings = new byte[5], ChunkWords = new byte[12], Chunk = new byte[9] };
		payload.Ui = new byte[SaveBlockWidths.UiRecordBytes * 2];
		payload.Coasters.Globals = new uint[] { 1, 2, 3 };
		var bytes = payload.Write();
		var read = OriginalSavePayload.Read( bytes );
		CollectionAssert.AreEqual( bytes, read.Write() );
		Assert.AreEqual( 1, read.Scripts.Records.Count );
		Assert.AreEqual( 1, read.FlyingRides.Bodies.Count );
		CollectionAssert.AreEqual( new ushort[] { 7, 8, 9 }, read.MessageCentre.Lists[2] );
		Assert.AreEqual( 2, read.Ui.Length / SaveBlockWidths.UiRecordBytes );
		Assert.AreEqual( 1, read.Rsse.Records.Count );
		Assert.AreEqual( 9, read.Sound.Chunk.Length );
		// a block with bytes after what its loader reads does not split as the original's
		var padded = bytes.ToList();
		var layout = SavePayloadLayout.Parse( bytes );
		padded.InsertRange( layout[SaveBlock.Cheat].Offset + 2, new byte[] { 0xAA } );
		Assert.ThrowsException<InvalidDataException>( () => OriginalSavePayload.Read( padded.ToArray() ) );
	}
}

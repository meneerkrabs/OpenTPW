using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The typed RSSE block reader (tp.exe writer 0x559350, loader 0x5597A0): synthetic blocks prove each field and refusal; the shipped Jungle Easymode save pins the
/// exact consumption, the match with the level archives' .RSE members and the thing / mesh-instance cross-check.
/// </summary>
[TestClass]
public class SaveRsseTests
{
	private static byte[] Words( params uint[] words ) => words.SelectMany( BitConverter.GetBytes ).ToArray();

	private static byte[] Join( params byte[][] parts ) => parts.SelectMany( part => part ).ToArray();

	/// <summary>One script record in the writer's order: the struct, eight counted buffers, the folder and an object list.</summary>
	internal static byte[] Record( int codeWords = 3, int objects = 1, string folder = "data\\levels\\test\\", int walkEntries = 1, Action<byte[]>? edit = null )
	{
		static byte[] Counted( int length, int unit = 1 ) => Join( Words( (uint)length ), new byte[length * unit] );
		var body = new byte[SaveRsseRecord.Bytes];
		BitConverter.TryWriteBytes( body.AsSpan( 8 ), 7u );
		BitConverter.TryWriteBytes( body.AsSpan( 0x50 ), (uint)codeWords );
		BitConverter.TryWriteBytes( body.AsSpan( 0xAC ), (ushort)11 );
		BitConverter.TryWriteBytes( body.AsSpan( 0xC8 ), 110u );
		edit?.Invoke( body );
		var list = new List<byte[]>();
		for ( var index = 0; index < objects; index++ )
			list.Add( Words( 0, 0, 3, 0x01320063, 0xFFFFFFFF, 0xFFFFFFFF, (uint)index ) );
		var folderBytes = Encoding.ASCII.GetBytes( folder + "\0" );
		return Join( body, Counted( codeWords * 4 ), Counted( 12 ), Counted( 8 ), Counted( 6 ), Counted( 0 ), Counted( 32 ), Counted( walkEntries, SaveRsseRecord.WalkEntryBytes ), Counted( 0 ),
			Words( (uint)folderBytes.Length ), folderBytes, Words( SaveRsseBlock.ObjectsMagic, (uint)objects, SaveRsseObject.Bytes ), Join( list.ToArray() ) );
	}

	/// <summary>A whole RSSE block of the given records, with the 20-byte header and five PAD_ words.</summary>
	internal static byte[] Block( params byte[][] records ) =>
		Join( Words( SaveRsseBlock.Magic, SaveRsseBlock.HeaderBytes, 1, 0x17A7, 0x10, (uint)records.Length, 0x04CEA284 ), Words( 0x5F444150, 0x5F444150, 0x5F444150, 0x5F444150, 0x5F444150 ),
			Words( (uint)records.Length, SaveRsseBlock.RecordBytes ), Join( records ) );

	private static void Rejects( byte[] block, string text )
	{
		var error = Assert.ThrowsException<InvalidDataException>( () => SaveRsseBlock.Parse( block, 0 ) );
		StringAssert.Contains( error.Message, text );
	}

	[TestMethod]
	public void ReadsTheHeaderRecordsBuffersFolderAndObjects()
	{
		var block = Block( Record( edit: body =>
		{
			BitConverter.TryWriteBytes( body.AsSpan( 0x3C ), 46u );
			BitConverter.TryWriteBytes( body.AsSpan( 0x6C ), (short)-2 );
			BitConverter.TryWriteBytes( body.AsSpan( 0x6E ), (short)8 );
			BitConverter.TryWriteBytes( body.AsSpan( 0xE6 ), (short)-1 );
			BitConverter.TryWriteBytes( body.AsSpan( 0xE4 ), (ushort)1000 );
			body[0xB8] = 1;
			body[0xB9] = 2;
		} ), Record( codeWords: 0, objects: 0 ) );
		var read = SaveRsseBlock.Parse( block, 0 );
		Assert.AreEqual( block.Length, read.Length );
		Assert.AreEqual( (1u, 0x17A7u, 0x10u, 2u, 0x04CEA284u), (read.Initialised, read.TickCounter, read.NextScriptId, read.HeaderRecordCount, read.ListHead) );
		Assert.AreEqual( 2, read.Records.Count );
		var first = read.Records[0];
		Assert.AreEqual( (7u, 3u, (ushort)11, 110u, 46u), (first.ScriptId, first.CodeWordCount, first.ThingId, first.MeshInstance, first.ProgramCounter) );
		Assert.AreEqual( ((short)-2, (short)8, (short)-1, (ushort)1000, (byte)1, (byte)2), (first.BounceCount, first.BounceBase, first.Bump, first.AnimationSpeed, first.Turbo, first.DipMusic) );
		Assert.AreEqual( (12, 12, 8, 6, 0, 32, SaveRsseRecord.WalkEntryBytes, 0), (first.Code.Length, first.Stack.Length, first.Variables.Length, first.Strings.Length, first.Limbo.Length, first.Bounce.Length, first.Walk.Length, first.Nodes.Length) );
		Assert.AreEqual( 3, first.CodeWords.Length );
		Assert.AreEqual( "data\\levels\\test\\", first.Folder );
		Assert.AreEqual( new SaveRsseObject( 0, 0, 3, 0x01320063, 0xFFFFFFFF, 0xFFFFFFFF, 0 ), first.Objects.Single() );
		Assert.AreEqual( SaveRsseRecord.Bytes, first.Raw.Length );
		Assert.AreEqual( 0, read.Records[1].Objects.Count );
		Assert.AreEqual( 9, first.BufferPointers.Length );
		Assert.AreEqual( 0, SaveRsseBlock.Parse( Block(), 0 ).Records.Count );
	}

	[TestMethod]
	public void ARecordOffsetIsWhereItsStructStartsInThePayload()
	{
		var block = Join( new byte[5], Block( Record() ) );
		var read = SaveRsseBlock.Parse( block, 5 );
		Assert.AreEqual( 5 + 4 + 4 + 20 + 20 + 8, read.Records[0].Offset );
		Assert.AreEqual( block.Length - 5, read.Length );
	}

	[TestMethod]
	public void MalformedBlocksAreRejected()
	{
		var good = Block( Record(), Record( objects: 2 ) );
		SaveRsseBlock.Parse( good, 0 );
		static byte[] With( byte[] bytes, int at, uint value ) { var copy = bytes.ToArray(); BitConverter.TryWriteBytes( copy.AsSpan( at ), value ); return copy; }
		Rejects( With( good, 0, 0x45535353 ), "'RSSE'" );
		Rejects( With( good, 4, 0x13 ), "header length" );
		Rejects( With( good, 4, 0x15 ), "header length" );
		Rejects( With( good, 52, 0xF3 ), "record size" );
		Rejects( With( good, 48, 0x7FFFFFFF ), "truncated" );
		var recordOffset = 56;
		Rejects( With( good, recordOffset + SaveRsseRecord.Bytes, 0xFFFFFFFF ), "truncated" );
		// a code buffer that is not whole words
		Rejects( With( good, recordOffset + SaveRsseRecord.Bytes, 11 ), "whole words" );
		// the folder: empty, unterminated, with an inner NUL
		var folderLengthAt = good.AsSpan().IndexOf( Words( 18 ) );
		Assert.IsTrue( folderLengthAt > 0 );
		Rejects( With( good, folderLengthAt, 0 ), "folder length" );
		var unterminated = good.ToArray();
		unterminated[folderLengthAt + 4 + 17] = (byte)'x';
		Rejects( unterminated, "NUL-terminated" );
		var inner = good.ToArray();
		inner[folderLengthAt + 4 + 3] = 0;
		Rejects( inner, "NUL-terminated" );
		// the object list: tag and entry size
		var tagAt = good.AsSpan().IndexOf( Words( SaveRsseBlock.ObjectsMagic ) );
		Rejects( With( good, tagAt, 0 ), "'OBJ '" );
		Rejects( With( good, tagAt + 8, 0x1B ), "object size" );
		Rejects( With( good, tagAt + 4, 0x7FFFFFFF ), "truncated" );
		// every strict prefix of the block is truncated
		for ( var length = 0; length < good.Length; length++ )
			Assert.ThrowsException<InvalidDataException>( () => SaveRsseBlock.Parse( good.AsSpan( 0, length ), 0 ), $"prefix of {length} bytes" );
		Assert.ThrowsException<InvalidDataException>( () => SaveRsseBlock.Parse( good, good.Length + 1 ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveRsseBlock.Parse( good, -1 ) );
	}

	[TestMethod]
	public void RandomMutationsOnlyEverFailAsInvalidData()
	{
		var random = new Random( 4 );
		var good = Block( Record(), Record( objects: 2 ) );
		for ( var iteration = 0; iteration < 5000; iteration++ )
		{
			var bytes = good.ToArray();
			for ( var edit = 1 + random.Next( 4 ); edit > 0; edit-- )
				bytes[random.Next( bytes.Length )] = (byte)random.Next( 256 );
			try
			{
				var read = SaveRsseBlock.Parse( bytes, 0 );
				Assert.IsTrue( read.Length <= bytes.Length );
			}
			catch ( InvalidDataException )
			{
			}
		}
	}

	private static byte[] Rse( string folder )
	{
		// The folder 'data\levels\jungle\Rides\bouncy\' is the archive levels/jungle/rides/bouncy.wad.
		var wad = folder.TrimEnd( '\\' ).Replace( '\\', '/' ).ToLowerInvariant();
		Assert.IsTrue( wad.StartsWith( "data/" ), folder );
		var path = Path.Combine( OriginalParkImportTests.OriginalDataPath(), wad["data/".Length..] + ".wad" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( $"Original archive is missing: {path}" );
		using var archive = new WadArchive( path );
		static IEnumerable<ArchiveFile> Files( ArchiveDirectory directory ) =>
			directory.Children.OfType<ArchiveFile>().Concat( directory.Children.OfType<ArchiveDirectory>().SelectMany( Files ) );
		var members = Files( archive.Root ).Where( file => file.Name.EndsWith( ".rse", StringComparison.OrdinalIgnoreCase ) ).ToList();
		Assert.AreEqual( 1, members.Count, $"{path} holds one .RSE member" );
		return members[0].GetData();
	}

	private static (byte[] Code, byte[] Strings) CodeAndStrings( byte[] rse )
	{
		var words = (int)BinaryPrimitives.ReadUInt32LittleEndian( rse.AsSpan( RideScriptFile.HeaderBytes ) );
		var code = rse.AsSpan( RideScriptFile.HeaderBytes + 4, words * 4 ).ToArray();
		var blobAt = RideScriptFile.HeaderBytes + 4 + words * 4;
		var blob = rse.AsSpan( blobAt + 4, (int)BinaryPrimitives.ReadUInt32LittleEndian( rse.AsSpan( blobAt ) ) ).ToArray();
		return (code, blob);
	}

	[TestMethod]
	public void OriginalJungleEasymodeRsseIsConsumedExactlyAndMatchesTheLevelArchives()
	{
		var payload = OriginalParkImportTests.ReadEasymodePayload();
		var layout = SavePayloadLayout.Parse( payload );
		var block = layout[SaveBlock.Rsse];
		Assert.AreEqual( 10856, block.Length );
		var rsse = SaveRsseBlock.Read( payload, layout );
		Assert.AreEqual( 10856, rsse.Length );
		Assert.AreEqual( 14, rsse.Records.Count );
		Assert.AreEqual( (1u, 14u), (rsse.Initialised, rsse.HeaderRecordCount) );
		Assert.AreEqual( 0x10u, rsse.NextScriptId );
		CollectionAssert.AreEqual( new uint[] { 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 4, 3, 2, 1 }, rsse.Records.Select( record => record.ScriptId ).ToArray() );
		var bouncy = rsse.Records.Single( record => record.Folder == "data\\levels\\jungle\\Rides\\bouncy\\" );
		Assert.AreEqual( (3u, (ushort)13, 110u, 208u, 3u, 10u), (bouncy.ScriptId, bouncy.ThingId, bouncy.MeshInstance, bouncy.CodeWordCount, bouncy.StackSize, bouncy.BounceSize) );
		Assert.AreEqual( 50u, bouncy.TimeSlice );
		Assert.IsTrue( rsse.Records.All( record => record.Raw.Length == SaveRsseRecord.Bytes && record.TimeSlice == 50 ) );
		// The objects entries: the first record with a list entry (the fountain) holds one of type 3.
		var fountain = rsse.Records.Single( record => record.Folder.EndsWith( "\\fountain\\", StringComparison.Ordinal ) );
		Assert.AreEqual( (3u, 0xFFFFFFFFu, 0xFFFFFFFFu, 1u), (fountain.Objects[0].Type, fountain.Objects[0].Key, fountain.Objects[0].ModelNode, fountain.Objects[0].Slot) );

		CheckRecordsAgainstTheArchives( rsse, "Easymode" );
	}

	/// <summary>Every record's code words and string blob equal the matching ride's .RSE member, and its sizes follow that member's header; shared with the corpus test.</summary>
	internal static void CheckRecordsAgainstTheArchives( SaveRsseBlock rsse, string name )
	{
		foreach ( var record in rsse.Records )
		{
			var rse = Rse( record.Folder );
			var (code, strings) = CodeAndStrings( rse );
			CollectionAssert.AreEqual( code, record.Code, $"{name + " " + record.Folder} code words" );
			CollectionAssert.AreEqual( strings, record.Strings, $"{name + " " + record.Folder} string blob" );
			var script = new RideScriptFile( new MemoryStream( rse ) );
			Assert.AreEqual( script.CodeWordCount, (int)record.CodeWordCount, name + " " + record.Folder );
			Assert.AreEqual( script.StringBlobLength, (int)record.StringBytes, name + " " + record.Folder );
			Assert.AreEqual( (script.VariableCount, script.StackSize, script.LimboSize, script.BounceSize, script.WalkSize, script.TimeSlice),
				((int)record.VariableCount, (int)record.StackSize, (int)record.LimboSize, (int)record.BounceSize, (int)record.WalkSize, (int)record.TimeSlice), name + " " + record.Folder );
			// The buffers are sized from the same header: words for the stack and variables, 8-byte limbo and 16-byte bounce entries, counted 32-byte walk entries.
			Assert.AreEqual( (script.StackSize * 4, script.VariableCount * 4, script.LimboSize * SaveRsseRecord.LimboEntryBytes, script.BounceSize * SaveRsseRecord.BounceEntryBytes, script.WalkSize * SaveRsseRecord.WalkEntryBytes),
				(record.Stack.Length, record.Variables.Length, record.Limbo.Length, record.Bounce.Length, record.Walk.Length), name + " " + record.Folder );
			Assert.AreEqual( record.Nodes.Length, (int)record.NodeCount * 4, name + " " + record.Folder );
		}
	}

	[TestMethod]
	public void EveryScriptsThingIsAnAttractionWithTheSameMeshInstance()
	{
		var payload = OriginalParkImportTests.ReadEasymodePayload();
		var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ) );
		var park = OriginalParkImport.Import( payload, map );
		Assert.IsNotNull( park.Rsse );
		Assert.AreEqual( 14, park.Rsse!.Records.Count );
		CheckThingsAgainstTheAttractions( park, "Easymode" );
	}

	/// <summary>Every script's thing is an attraction whose mesh instance is the script's; shared with the corpus test.</summary>
	internal static void CheckThingsAgainstTheAttractions( OriginalParkImport park, string name )
	{
		foreach ( var record in park.Rsse!.Records )
		{
			var attraction = park.Attractions.Single( item => item.Id == record.ThingId );
			Assert.AreEqual( attraction.MeshInstanceId, record.MeshInstance, $"{name}: thing {record.ThingId} ({record.Folder})" );
		}
	}
}

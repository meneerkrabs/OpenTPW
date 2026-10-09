using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class MapFileTests
{
	private static byte[] CreateMap( uint width, uint height, byte[] cells, uint? declaredSize = null )
	{
		var data = new byte[36 + 8 + 28 + cells.Length + 8];
		"TP2M"u8.CopyTo( data );
		"Theme Park 2 Attribute Map File"u8.CopyTo( data.AsSpan( 4 ) );
		"MAP "u8.CopyTo( data.AsSpan( 36 ) );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 40 ), declaredSize ?? (uint)(28 + cells.Length) );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 44 ), width );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 48 ), height );
		for ( var index = 0; index < 5; index++ )
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 52 + index * 4 ), (uint)(8 + index) );
		cells.CopyTo( data, 72 );
		"END "u8.CopyTo( data.AsSpan( 72 + cells.Length ) );
		return data;
	}

	[TestMethod]
	public void ReadsHeaderOpaqueValuesAndColumnFastestCells()
	{
		using var stream = new MemoryStream( CreateMap( 3, 2, new byte[] { 0, 1, 2, 3, 144, 255 } ) );
		var map = new MapFile( stream );
		Assert.IsTrue( stream.CanRead );
		Assert.AreEqual( "Theme Park 2 Attribute Map File", map.Title );
		Assert.AreEqual( 3, map.Width );
		Assert.AreEqual( 2, map.Height );
		CollectionAssert.AreEqual( new uint[] { 8, 9, 10, 11, 12 }, map.OpaqueHeaderValues.ToArray() );
		Assert.AreEqual( (byte)2, map.GetCell( 2, 0 ) );
		Assert.AreEqual( (byte)3, map.GetCell( 0, 1 ) );
		Assert.AreEqual( (byte)255, map.GetCell( 2, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => map.GetCell( 3, 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => map.GetCell( 0, -1 ) );
	}

	[TestMethod]
	public void RawBitEnumerationSeesUnverifiedBitThatFlagsMask()
	{
		// Width 3 (game Y), height 2 (game X). Raw 0x04 is set on 5, 148, 4 and 12 but is not a verified flag.
		var map = new MapFile( new MemoryStream( CreateMap( 3, 2, new byte[] { 0, 5, 148, 4, 12, 251 } ) ) );
		CollectionAssert.AreEqual(
			new (int X, int Y, byte Raw)[] { (0, 1, 5), (0, 2, 148), (1, 0, 4), (1, 1, 12) },
			map.EnumerateCellsWithRawBits( 0x04 ).ToArray() );
		Assert.AreEqual( MapCellFlags.Blocked, map.GetFlagsAt( 0, 1 ) );
		Assert.AreEqual( MapCellFlags.EntranceArea | MapCellFlags.FixedWalkway, map.GetFlagsAt( 0, 2 ) );
		Assert.AreEqual( MapCellFlags.None, map.GetFlagsAt( 1, 0 ) );
		Assert.AreEqual( MapCellFlags.InitialPath, map.GetFlagsAt( 1, 1 ) );
		Assert.AreEqual( (byte)4, map.GetCellAt( 1, 0 ) );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 35 )]
	[DataRow( 40 )]
	[DataRow( 60 )]
	[DataRow( 77 )]
	[DataRow( 82 )]
	public void RejectsTruncatedFile( int length )
	{
		var data = CreateMap( 2, 2, new byte[4] );
		Array.Resize( ref data, length );
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void RejectsBadMagicAndUnterminatedTitle()
	{
		var data = CreateMap( 1, 1, new byte[1] );
		data[0] = (byte)'X';
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( data ) ) );
		data = CreateMap( 1, 1, new byte[1] );
		data.AsSpan( 4, 32 ).Fill( (byte)'A' );
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( data ) ) );
	}

	[DataTestMethod]
	[DataRow( 0u, 1u )]
	[DataRow( 1u, 0u )]
	[DataRow( 1025u, 1u )]
	[DataRow( uint.MaxValue, uint.MaxValue )]
	public void RejectsDimensionsBeyondLimitsBeforeAllocating( uint width, uint height )
	{
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( CreateMap( width, height, new byte[1], 29 ) ) ) );
	}

	[DataTestMethod]
	[DataRow( 28u )]
	[DataRow( 33u )]
	[DataRow( uint.MaxValue )]
	public void RejectsChunkSizeInconsistentWithGrid( uint declaredSize )
	{
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( CreateMap( 2, 2, new byte[4], declaredSize ) ) ) );
	}

	[TestMethod]
	public void RejectsMissingNonEmptyOrNonFinalEnd()
	{
		var data = CreateMap( 1, 1, new byte[1] );
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( data[..^8] ) ) );
		var trailing = data.Concat( new byte[] { 0 } ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( trailing ) ) );
		var sized = data.Concat( new byte[] { 0 } ).ToArray();
		sized[^5] = 1;
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( sized ) ) );
	}

	[TestMethod]
	public void RejectsMissingDuplicateAndUnknownChunks()
	{
		var onlyEnd = new byte[44];
		"TP2M"u8.CopyTo( onlyEnd );
		"END "u8.CopyTo( onlyEnd.AsSpan( 36 ) );
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( onlyEnd ) ) );
		var single = CreateMap( 1, 1, new byte[] { 7 } );
		var duplicate = single[..^8].Concat( single[36..] ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( duplicate ) ) );
		var unknown = CreateMap( 1, 1, new byte[1] );
		"HGT "u8.CopyTo( unknown.AsSpan( 36 ) );
		Assert.ThrowsException<NotSupportedException>( () => new MapFile( new MemoryStream( unknown ) ) );
	}

	[TestMethod]
	public void RejectsSoundCatalogMapSignature()
	{
		var catalog = new byte[56];
		Convert.FromHexString( "012C61E9D031D211B40900A0C993F203" ).CopyTo( catalog, 0 );
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( catalog ) ) );
	}

	[TestMethod]
	public void ReadsNonseekableShortReadsWithoutClosingInput()
	{
		using var stream = new ShortReadStream( CreateMap( 2, 1, new byte[] { 1, 17 } ) );
		CollectionAssert.AreEqual( new byte[] { 1, 17 }, new MapFile( stream ).Cells );
		Assert.IsTrue( stream.CanRead );
	}

	[TestMethod]
	public void RejectsOversizedInputWithoutClosingIt()
	{
		using var stream = new MemoryStream( new byte[MapFile.MaximumFileBytes + 1] );
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( stream ) );
		Assert.IsTrue( stream.CanRead );
	}

	private sealed class ShortReadStream : MemoryStream
	{
		public ShortReadStream( byte[] data ) : base( data ) { }
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override int Read( byte[] readBuffer, int offset, int count ) => base.Read( readBuffer, offset, Math.Min( count, 3 ) );
	}

	[DataTestMethod]
	[DataRow( "fantasy", "base.map", "641B1F05FD3F47A4F0FCCA551607C2F6C45B02845A8C7F4871338EAF42597368", "CC2A150E668D42BDB2649C595B3DDDE175BE0BF579782A3FA024362AFC991F36", 1073 )]
	[DataRow( "hallow", "base.map", "C74B5B1B9AA234EBA8D81A20AF9CF7AF597055DA13527805CA800879F50EB398", "4FB515A2FBBE4D9BE7D6A84D6CDFF5FCF6AD502C9D3D60A2E26CA60BCCD05AEF", 1118 )]
	[DataRow( "jungle", "base.map", "ADBC201ACC29E9E81B936DFC9FFCDC403757A920F76C9EF41C435A5417D98364", "9E8E692A2F25E1E0129D7795C5A1B7D1DDBCA71D5A0DB616E4F482010A1492C2", 1495 )]
	[DataRow( "jungle", "terrain.map", "F6BCB05C1E085AEB1F085BF0CD6D672873F1681B0434DDD9BABF929527EC10A1", "D7BDFA1F257BE03A817740C535448A07428F3799F91414517748695A2314E3D5", 805 )]
	[DataRow( "space", "base.map", "CC27092DF61EF6FAE42DC3C17BC49D42F2D87550D6D1F9B7CC2D40044772EC54", "3A8146B640F52C2B9C9CAC6DE572EB13367EB8CCC08DA3D6A33CD6CA41C3A89C", 1196 )]
	public void OriginalTerrainMapMatchesPinnedIdentityAndIndependentCellHash( string level, string member, string fileHash, string cellHash, int nonzeroCells )
	{
		var data = ReadOriginalMember( level, member );
		Assert.AreEqual( 16464, data.Length );
		Assert.AreEqual( fileHash, Convert.ToHexString( SHA256.HashData( data ) ) );
		var map = new MapFile( new MemoryStream( data ) );
		Assert.AreEqual( "Theme Park 2 Attribute Map File", map.Title );
		Assert.AreEqual( 128, map.Width );
		Assert.AreEqual( 128, map.Height );
		CollectionAssert.AreEqual( new uint[] { 8, 8, 8, 8, 8 }, map.OpaqueHeaderValues.ToArray() );
		Assert.AreEqual( cellHash, Convert.ToHexString( SHA256.HashData( map.Cells ) ) );
		Assert.AreEqual( nonzeroCells, map.Cells.Count( cell => cell != 0 ) );
	}

	[TestMethod]
	public void OriginalBaseMapsShareValuePositionsAcrossLevels()
	{
		var reference = new MapFile( new MemoryStream( ReadOriginalMember( "fantasy", "base.map" ) ) );
		foreach ( var level in new[] { "fantasy", "hallow", "jungle", "space" } )
		{
			var map = new MapFile( new MemoryStream( ReadOriginalMember( level, "base.map" ) ) );
			foreach ( var (value, count) in new (byte, int)[] { (17, 490), (144, 48), (148, 14), (8, 10) } )
			{
				var positions = Enumerable.Range( 0, map.Cells.Length ).Where( index => map.Cells[index] == value ).ToArray();
				Assert.AreEqual( count, positions.Length, $"{level} {value}" );
				CollectionAssert.AreEqual( Enumerable.Range( 0, reference.Cells.Length ).Where( index => reference.Cells[index] == value ).ToArray(), positions, $"{level} {value}" );
			}
			Assert.AreEqual( (byte)1, map.GetCell( 0, 0 ) );
			Assert.AreEqual( (byte)148, map.GetCell( 10, 47 ) );
			Assert.AreEqual( (byte)8, map.GetCell( 17, 47 ) );
			for ( var row = 0; row < 128; row++ )
				for ( var column = 0; column < 128; column++ )
					if ( row >= 96 || column >= 85 )
						Assert.AreEqual( (byte)0, map.GetCell( column, row ), level );
		}
	}

	[DataTestMethod]
	[DataRow( "fantasy", 192 )]
	[DataRow( "hallow", 238 )]
	[DataRow( "jungle", 363 )]
	[DataRow( "space", 314 )]
	public void OriginalHeightfieldHolesMatchVerifiedMapFlags( string level, int blockedHoles )
	{
		var map = new MapFile( new MemoryStream( ReadOriginalMember( level, "base.map" ) ) );
		var field = new ModelFile( new MemoryStream( ReadOriginalMember( level, "base.MD2" ) ) ).Heightfield!;
		Assert.AreEqual( (96, 85, 10f, 10f), (field.CellCountX, field.CellCountZ, field.CellSizeX, field.CellSizeZ) );
		var holesOnBlockedOnly = 0;
		for ( var x = 0; x < field.CellCountX; x++ )
		{
			for ( var y = 0; y < field.CellCountZ; y++ )
			{
				var flags = map.GetFlagsAt( x, y );
				// The isolated value 16 at fantasy (27, 17) has no geometry and is not a hole; see docs/MAP.md.
				if ( flags == MapCellFlags.EntranceArea )
					Assert.AreEqual( ("fantasy", 27, 17, false), (level, x, y, field.IsHole( x, y )) );
				else if ( (flags & (MapCellFlags.Water | MapCellFlags.EntranceArea | MapCellFlags.FixedWalkway)) != 0 )
					Assert.IsTrue( field.IsHole( x, y ), $"{level} ({x}, {y}) {flags}" );
				else if ( flags == MapCellFlags.Blocked )
					holesOnBlockedOnly += field.IsHole( x, y ) ? 1 : 0;
				else
					Assert.IsFalse( field.IsHole( x, y ), $"{level} ({x}, {y}) {flags}" );
			}
		}
		Assert.AreEqual( blockedHoles, holesOnBlockedOnly );
		var model = new ModelFile( new MemoryStream( ReadOriginalMember( level, "base.MD2" ) ) );
		var slots = Enumerable.Range( 0, field.CellCountX * field.CellCountZ ).Select( index => field.GetCellTextureSlot( index % field.CellCountX, index / field.CellCountX ) )
			.Where( slot => slot >= 0 ).Distinct().ToArray();
		Assert.AreEqual( 6, slots.Length );
		Assert.IsTrue( slots.All( slot => slot < model.Textures.Count && model.Textures[slot].FrameNames[0].Contains( "_bas", StringComparison.OrdinalIgnoreCase ) ), level );
	}

	[DataTestMethod]
	[DataRow( "fantasy" )]
	[DataRow( "hallow" )]
	[DataRow( "jungle" )]
	[DataRow( "space" )]
	public void OriginalFixedItemSettingsLieOnMatchingMapFlags( string level )
	{
		var map = new MapFile( new MemoryStream( ReadOriginalMember( level, "base.map" ) ) );
		var settingsPath = Directory.EnumerateFiles( Path.Combine( OriginalDataPath(), "levels", level ) )
			.FirstOrDefault( file => string.Equals( Path.GetFileName( file ), "Standard.sam", StringComparison.OrdinalIgnoreCase ) );
		if ( settingsPath == null )
			Assert.Inconclusive( $"Original {level} Standard.sam is missing." );
		var settings = new SettingsFile( new MemoryStream( File.ReadAllBytes( settingsPath ) ) );
		MapCellFlags At( string item ) => map.GetFlagsAt( int.Parse( settings[$"FixedItemInfo.{item}PosX"] ), int.Parse( settings[$"FixedItemInfo.{item}PosY"] ) );
		Assert.AreEqual( "48", settings["MapInfo.FixedItemOriginX"] );
		Assert.AreEqual( "17", settings["MapInfo.FixedItemOriginY"] );
		foreach ( var side in new[] { "A", "B" } )
		{
			Assert.AreEqual( MapCellFlags.InitialPath, At( $"Entrance{side}" ) );
			foreach ( var item in new[] { "TicketBooth", "CrossingParkSide", "CrossingBSSide", "BusStop" } )
				Assert.AreEqual( MapCellFlags.EntranceArea | MapCellFlags.FixedWalkway, At( $"{item}{side}" ), $"{level} {item}{side}" );
		}
		var strikeX = int.Parse( settings["FixedItemInfo.StrikeAreaStartX"] );
		var strikeY = int.Parse( settings["FixedItemInfo.StrikeAreaStartY"] );
		for ( var x = 0; x < int.Parse( settings["FixedItemInfo.StrikeAreaSizeX"] ); x++ )
			Assert.AreEqual( MapCellFlags.EntranceArea | MapCellFlags.FixedWalkway, map.GetFlagsAt( strikeX + x, strikeY ) );
	}

	[DataTestMethod]
	[DataRow( "cat_rideBANK.map", 56 )]
	[DataRow( "cat_rideSFX.map", 364 )]
	public void OriginalSpeakerSoundCatalogMapIsNotATerrainMap( string member, int length )
	{
		var data = ReadOriginalArchiveMember( Path.Combine( "levels", "jungle", "features", "speaker1.wad" ), member );
		Assert.AreEqual( length, data.Length );
		Assert.ThrowsException<InvalidDataException>( () => new MapFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void OriginalLooseSoundCatalogMapsAreNotTerrainMaps()
	{
		var dataPath = OriginalDataPath();
		var files = Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories )
			.Where( file => string.Equals( Path.GetExtension( file ), ".map", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		Assert.AreEqual( 62, files.Length );
		foreach ( var file in files )
		{
			using var stream = File.OpenRead( file );
			Assert.ThrowsException<InvalidDataException>( () => new MapFile( stream ), file );
		}
	}

	[DataTestMethod]
	[DataRow( "fantasy" )]
	[DataRow( "hallow" )]
	[DataRow( "jungle" )]
	[DataRow( "space" )]
	public void OriginalRawBit04IsOnlyTicketLaneCellsWithValue148( string level )
	{
		// Meaning of 0x04 is unknown; this pins where the bit is raw-set, not what it does. See docs/MAP.md.
		var map = new MapFile( new MemoryStream( ReadOriginalMember( level, "base.map" ) ) );
		var expected = Enumerable.Range( 47, 2 ).SelectMany( x => Enumerable.Range( 10, 7 ).Select( y => (X: x, Y: y, Raw: (byte)148) ) ).ToArray();
		CollectionAssert.AreEqual( expected, map.EnumerateCellsWithRawBits( 0x04 ).ToArray(), level );
		CollectionAssert.AreEqual( new byte[] { 148 }, map.Cells.Where( cell => (cell & 0x04) != 0 ).Distinct().ToArray(), level );
	}

	private static byte[] ReadOriginalMember( string level, string member ) => ReadOriginalArchiveMember( Path.Combine( "levels", level, "terrain.wad" ), member );

	private static string OriginalDataPath()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original terrain MAP fixtures." );
		return Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
	}

	private static byte[] ReadOriginalArchiveMember( string relativeArchive, string member )
	{
		var dataPath = OriginalDataPath();
		var archivePath = Path.Combine( dataPath, relativeArchive );
		if ( !File.Exists( archivePath ) )
			Assert.Inconclusive( $"Original archive is missing: {relativeArchive}" );
		using var archive = new WadArchive( archivePath );
		var file = archive.GetFile( member );
		if ( file == null )
			Assert.Inconclusive( $"Original archive member is missing: {relativeArchive}/{member}" );
		return file!.GetData();
	}
}

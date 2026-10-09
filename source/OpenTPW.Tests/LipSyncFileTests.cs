using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LipSyncFileTests
{
	private static byte[] CreateLip( params uint[] words )
	{
		var data = new byte[words.Length * 4];
		for ( var index = 0; index < words.Length; index++ )
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( index * 4 ), words[index] );
		return data;
	}

	[TestMethod]
	public void ReadsMarksBeforeTerminatorWithoutClosingInput()
	{
		using var stream = new MemoryStream( CreateLip( 0, 18276, 2226893, LipSyncFile.Terminator ) );
		var lip = new LipSyncFile( stream );
		CollectionAssert.AreEqual( new uint[] { 0, 18276, 2226893 }, lip.Marks.ToArray() );
		Assert.IsTrue( stream.CanRead );
	}

	[TestMethod]
	public void ReadsSingleMark()
	{
		CollectionAssert.AreEqual( new uint[] { 305804 }, new LipSyncFile( new MemoryStream( CreateLip( 305804, LipSyncFile.Terminator ) ) ).Marks.ToArray() );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 4 )]
	[DataRow( 7 )]
	[DataRow( 12 )]
	public void RejectsLengthsThatAreNotPositiveMultiplesOfEight( int length )
	{
		var data = Enumerable.Repeat( (byte)0xff, length ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => new LipSyncFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void RejectsMissingTerminator()
	{
		Assert.ThrowsException<InvalidDataException>( () => new LipSyncFile( new MemoryStream( CreateLip( 1, 2 ) ) ) );
	}

	[TestMethod]
	public void RejectsEarlyTerminator()
	{
		Assert.ThrowsException<InvalidDataException>( () => new LipSyncFile( new MemoryStream( CreateLip( 1, LipSyncFile.Terminator, 5, LipSyncFile.Terminator ) ) ) );
	}

	[DataTestMethod]
	[DataRow( 5u, 5u )]
	[DataRow( 6u, 5u )]
	public void RejectsMarksThatAreNotStrictlyIncreasing( uint first, uint second )
	{
		Assert.ThrowsException<InvalidDataException>( () => new LipSyncFile( new MemoryStream( CreateLip( 1, first, second, LipSyncFile.Terminator ) ) ) );
	}

	[TestMethod]
	public void ReadsNonseekableShortReads()
	{
		using var stream = new ShortReadStream( CreateLip( 1, 2, 3, LipSyncFile.Terminator ) );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 3 }, new LipSyncFile( stream ).Marks.ToArray() );
		Assert.IsTrue( stream.CanRead );
	}

	[TestMethod]
	public void RejectsOversizedInputWithoutClosingIt()
	{
		using var stream = new MemoryStream( new byte[LipSyncFile.MaximumFileBytes + 8] );
		Assert.ThrowsException<InvalidDataException>( () => new LipSyncFile( stream ) );
		Assert.IsTrue( stream.CanRead );
	}

	private sealed class ShortReadStream : MemoryStream
	{
		public ShortReadStream( byte[] data ) : base( data ) { }
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override int Read( byte[] readBuffer, int offset, int count ) => base.Read( readBuffer, offset, Math.Min( count, 3 ) );
	}

	[TestMethod]
	public void OriginalGlobalLipsWadParsesEveryMember()
	{
		var path = Path.Combine( OriginalDataDirectory(), "global", "Speech", "lips.wad" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The selected global lips.wad is missing." );
		Assert.AreEqual( "F86D74C4B4356AEAA0E6ED2FEB00E80450A9A643A37C7D108E0C2D65C19CC9E1", Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( path ) ) ) );
		using var archive = new WadArchive( path );
		var names = archive.GetFiles( "" );
		Assert.AreEqual( 639, names.Length );
		Assert.AreEqual( 0, archive.GetDirectories( "" ).Length );
		var markCount = 0;
		foreach ( var name in names )
		{
			Assert.IsTrue( name.EndsWith( ".LIP", StringComparison.OrdinalIgnoreCase ), name );
			markCount += new LipSyncFile( new MemoryStream( archive.GetFile( name ).GetData() ) ).Marks.Count;
		}
		Assert.AreEqual( 3223, markCount );
		CollectionAssert.AreEqual( new uint[] { 2226893, 2812380, 4058820 }, new LipSyncFile( new MemoryStream( archive.GetFile( "sp_001.LIP" ).GetData() ) ).Marks.ToArray() );
		CollectionAssert.AreEqual( new uint[] { 305804 }, new LipSyncFile( new MemoryStream( archive.GetFile( "z_z_ouch1.LIP" ).GetData() ) ).Marks.ToArray() );
	}

	[DataTestMethod]
	[DataRow( "fantasy", "5B9010FBF818327D5DE682626787786A869B54206EA8BEDD2D188C5BC2CDD1B7", 27, 1801814u, 21955918u )]
	[DataRow( "hallow", "D6BE53D0ED0E1F6D2DE96983DCC24E62D1980EE72B6042B51E3198551898BEC2", 31, 1409478u, 26529478u )]
	[DataRow( "jungle", "F3455533F1C2C3D214484A40C3A0B712CBBB97FEB06EE877DD0D860E20C2CE2B", 35, 0u, 28575963u )]
	[DataRow( "space", "D7414E592F1B7F5A8E701018375851ED2B8A169E6DE9764C041E9592B2702E6A", 19, 0u, 23893197u )]
	public void OriginalLevelLipMatchesIdentityAndMarks( string level, string fileHash, int count, uint first, uint last )
	{
		var path = Path.Combine( OriginalDataDirectory(), "levels", level, "Speech", "lips", "sp_001.LIP" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( $"The selected {level} LIP is missing." );
		var data = File.ReadAllBytes( path );
		Assert.AreEqual( fileHash, Convert.ToHexString( SHA256.HashData( data ) ) );
		var lip = new LipSyncFile( new MemoryStream( data ) );
		Assert.AreEqual( count, lip.Marks.Count );
		Assert.AreEqual( first, lip.Marks[0] );
		Assert.AreEqual( last, lip.Marks[^1] );
	}

	private static string OriginalDataDirectory()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the selected original LIP corpus." );
		return Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
	}
}

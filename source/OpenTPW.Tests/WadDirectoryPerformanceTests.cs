using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class WadDirectoryPerformanceTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static byte[] ManyMemberArchive( int count )
	{
		var names = Enumerable.Range( 0, count ).Select( index => Encoding.ASCII.GetBytes( $"entry_{index:000}.bin\0" ) ).ToArray();
		var nameOffset = 88 + count * 40;
		var dataOffset = nameOffset + names.Sum( name => name.Length );
		var data = new byte[dataOffset + count * 4];
		Encoding.ASCII.GetBytes( "DWFB" ).CopyTo( data, 0 );
		void U32( int at, int value ) => BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( at ), value );
		U32( 4, 1 );
		U32( 72, count );
		U32( 76, 88 );
		U32( 80, count * 40 );
		for ( var index = 0; index < count; index++ )
		{
			var record = 88 + index * 40;
			U32( record + 4, nameOffset );
			U32( record + 8, names[index].Length );
			U32( record + 12, dataOffset );
			U32( record + 16, 4 );
			U32( record + 24, 4 );
			names[index].CopyTo( data, nameOffset );
			U32( dataOffset, index );
			nameOffset += names[index].Length;
			dataOffset += 4;
		}
		return data;
	}

	[TestMethod]
	public void DirectoryParsingDoesNotForceAFullCollectionForEveryMember()
	{
		const int count = 64;
		var bytes = ManyMemberArchive( count );
		var before = GC.CollectionCount( GC.MaxGeneration );
		using var archive = new WadArchive( new MemoryStream( bytes ) );
		var collections = GC.CollectionCount( GC.MaxGeneration ) - before;
		Assert.AreEqual( count, archive.GetFiles( "" ).Length );
		Assert.IsTrue( collections < count / 4,
			$"Opening {count} tiny entries caused {collections} full collections; the parser must leave collection scheduling to the runtime." );
	}

	[TestMethod]
	public void DirectoryMetadataAndLazyMemberCachingRemainIntact()
	{
		const int count = 64;
		using var archive = new WadArchive( new MemoryStream( ManyMemberArchive( count ) ) );
		CollectionAssert.AreEqual( Enumerable.Range( 0, count ).Select( index => $"entry_{index:000}.bin" ).ToArray(), archive.GetFiles( "" ) );
		var cached = typeof( WadArchiveFile ).GetProperty( "CachedData", BindingFlags.Instance | BindingFlags.NonPublic )!;
		foreach ( var member in archive.Root.Children.OfType<WadArchiveFile>() )
			Assert.IsNull( cached.GetValue( member ), "reading the directory does not decode every member" );
		foreach ( var index in Enumerable.Range( 0, count ).Where( index => index % 8 == 0 ) )
		{
			var member = archive.GetFile( $"entry_{index:000}.bin" );
			var data = member.GetData();
			Assert.AreEqual( index, BinaryPrimitives.ReadInt32LittleEndian( data ) );
			Assert.AreSame( data, member.GetData(), "a requested member retains its existing lazy data cache" );
		}
		Assert.AreEqual( 8, archive.Root.Children.OfType<WadArchiveFile>().Count( member => cached.GetValue( member ) != null ) );
	}

	[TestMethod]
	public void OriginalResponse587KeepsItsPcmAndNineLipMarks()
	{
		var fileSystem = new BaseFileSystem( StringTableTests.OriginalDataDirectory() );
		var response = new AdvisorResponse( 587, 606, 606, -1, 1, false );
		var loaded = Advisor.LoadResponse( fileSystem, response, "jungle" );
		var bank = new SoundBank( File.ReadAllBytes( fileSystem.GetAbsolutePath( "/global/Speech/speechHD.SDT" ) ) );
		var expected = Mp2Decoder.Decode( bank.GetMpeg( 606 )! ).TrimTo( bank.GetPlaybackBytes( 606 ) );
		Assert.AreEqual( 182, loaded.Audio.FrameCount );
		Assert.AreEqual( (expected.SampleRate, expected.Channels, expected.TrailingBytes, expected.CrcChecks, expected.CrcFailures, expected.Emphasis),
			(loaded.Audio.SampleRate, loaded.Audio.Channels, loaded.Audio.TrailingBytes, loaded.Audio.CrcChecks, loaded.Audio.CrcFailures, loaded.Audio.Emphasis) );
		CollectionAssert.AreEqual( expected.Samples, loaded.Audio.Samples );
		CollectionAssert.AreEqual( new uint[] { 1271473, 1572743, 2625759, 2882131, 3632607, 4120136, 5648390, 6627074, 9340000 }, loaded.Timeline.Marks.ToArray() );
		StringAssert.Contains( loaded.Source, "/global/Speech/speechHD.SDT" );
	}
}

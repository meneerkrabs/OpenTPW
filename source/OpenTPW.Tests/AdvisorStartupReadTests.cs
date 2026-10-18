using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class AdvisorStartupReadTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static string WriteBank( string data, bool local )
	{
		var directory = Path.Combine( data, local ? "levels/jungle/Speech" : "global/Speech" );
		Directory.CreateDirectory( Path.Combine( directory, "lips" ) );
		File.WriteAllBytes( Path.Combine( directory, "lips/sp_001.LIP" ), LipSyncFile.Encode( new uint[] { 100, 200, 300 } ) );
		var payload = Mp2DecoderTests.ToneFrame();
		var prefix = new byte[52 + payload.Length];
		BinaryPrimitives.WriteInt32LittleEndian( prefix, 2 );
		BinaryPrimitives.WriteInt32LittleEndian( prefix.AsSpan( 4 ), 12 );
		BinaryPrimitives.WriteInt32LittleEndian( prefix.AsSpan( 8 ), 16 * 1024 * 1024 );
		BinaryPrimitives.WriteInt32LittleEndian( prefix.AsSpan( 12 ), 40 );
		BinaryPrimitives.WriteInt32LittleEndian( prefix.AsSpan( 16 ), payload.Length );
		prefix[12 + 0x1B] = SdtEntryHeader.PlaybackLengthFlag;
		BinaryPrimitives.WriteInt32LittleEndian( prefix.AsSpan( 12 + 0x20 ), 100 );
		payload.CopyTo( prefix, 52 );
		var path = Path.Combine( directory, "speechHD.SDT" );
		using var stream = File.Create( path );
		stream.Write( prefix );
		stream.SetLength( 32 * 1024 * 1024 );
		return path;
	}

	[DataTestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void OneResponseDoesNotAllocateTheWholeBankAndPreservesPcmAndLips( bool local, bool overlay )
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-advisor-startup-" ).FullName;
		try
		{
			var data = Path.Combine( root, "data" );
			Directory.CreateDirectory( Path.Combine( data, "Language/English" ) );
			var selectedData = overlay ? Path.Combine( root, "overlay" ) : data;
			Directory.CreateDirectory( Path.Combine( selectedData, "Language/English" ) );
			var path = WriteBank( selectedData, local );
			// A corrupt base bank proves the explicit language still selects its overlay before the installation.
			if ( overlay )
			{
				var basePath = Path.Combine( data, local ? "levels/jungle/Speech/speechHD.SDT" : "global/Speech/speechHD.SDT" );
				Directory.CreateDirectory( Path.GetDirectoryName( basePath )! );
				File.WriteAllBytes( basePath, Array.Empty<byte>() );
			}
			var language = overlay ? GameLanguage.Resolve( data, "English", selectedData ) : null;
			var fileSystem = new BaseFileSystem( data );
			var response = new AdvisorResponse( 1, 1, 1, 16, 1, local );
			// Compare with the old whole-bank reader, outside the measured allocation interval.
			var bank = new SoundBank( File.ReadAllBytes( path ) );
			var expected = Mp2Decoder.Decode( bank.GetMpeg( 1 )!, bank.GetPlaybackBytes( 1 ) );
			_ = Advisor.LoadResponse( fileSystem, response, "jungle", language );
			var before = GC.GetAllocatedBytesForCurrentThread();
			var loaded = Advisor.LoadResponse( fileSystem, response, "jungle", language );
			var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
			Console.WriteLine( $"Selected advisor entry ({(local ? "local" : "global")}, overlay={overlay}): {allocated} allocated bytes for a 32 MiB synthetic bank." );
			Assert.IsTrue( allocated < 1024 * 1024, $"One tiny response allocated {allocated} bytes; unrelated bank contents must stay unread." );
			CollectionAssert.AreEqual( expected.Samples, loaded.Audio.Samples );
			Assert.AreEqual( (expected.SampleRate, expected.Channels, expected.FrameCount, expected.TrailingBytes, expected.CrcChecks, expected.CrcFailures, expected.Emphasis),
				(loaded.Audio.SampleRate, loaded.Audio.Channels, loaded.Audio.FrameCount, loaded.Audio.TrailingBytes, loaded.Audio.CrcChecks, loaded.Audio.CrcFailures, loaded.Audio.Emphasis) );
			Assert.AreEqual( 50, loaded.Audio.Samples.Length, "The selected entry's PCM playback length is still honored." );
			CollectionAssert.AreEqual( new uint[] { 100, 200, 300 }, loaded.Timeline.Marks.ToArray() );
			StringAssert.Contains( loaded.Source, overlay ? Path.GetFullPath( path ) : local ? "/levels/jungle/Speech" : "/global/Speech" );
			Assert.ThrowsException<FileNotFoundException>( () => Advisor.LoadResponse( fileSystem, response with { Sample = 3 }, "jungle", language ) );
		}
		finally { Directory.Delete( root, true ); }
	}
}

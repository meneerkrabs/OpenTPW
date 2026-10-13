using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class SdtWavExportTests
{
	private string root = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Setup()
	{
		root = Directory.CreateTempSubdirectory( "opentpw-sdt-wav-" ).FullName;
		previousLog = Log;
		Log = new Logger();
	}

	[TestCleanup]
	public void Cleanup()
	{
		Log = previousLog;
		Directory.Delete( root, true );
	}

	private static void Export( string input, uint number, string output )
		=> SdtWavExport.Run( input, number, output );

	private string Bank( byte[] selected )
	{
		var path = Path.Combine( root, "source.sdt" );
		File.WriteAllBytes( path, SdtEntryTests.CreateBank(
			("first.mp2", 22050, 36, 99, Mp2DecoderTests.SilentFrame()),
			("chosen.mp2", 1234, 37, uint.MaxValue, selected) ) );
		return path;
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ExplicitSelectionPreservesDecodedPcmRateChannelsAndSamples( bool stereo )
	{
		var frames = stereo ? Mp2DecoderTests.LayerOneReferenceStream( includeCrc: false )
			: Mp2DecoderTests.ToneFrame().Concat( Mp2DecoderTests.ToneFrame() ).ToArray();
		var input = Bank( frames.Concat( new byte[] { 0 } ).ToArray() );
		var source = File.ReadAllBytes( input );
		var bank = new SoundBank( () => File.OpenRead( input ), source.Length );
		var expected = Mp2Decoder.Decode( bank.GetMpeg( 2 )! );
		Assert.IsTrue( expected.Samples.Any( sample => sample != 0 ), "Selection must distinguish the first silent entry." );
		var output = Path.Combine( root, "nested", "selected.wav" );
		Export( input, 2, output );
		using var wave = new WaveFileReader( output );
		Assert.AreEqual( WaveFormatEncoding.Pcm, wave.WaveFormat.Encoding );
		Assert.AreEqual( 16, wave.WaveFormat.BitsPerSample );
		Assert.AreEqual( expected.SampleRate, wave.WaveFormat.SampleRate );
		Assert.AreEqual( expected.Channels, wave.WaveFormat.Channels );
		Assert.AreEqual( expected.Samples.Length * 2L, wave.Length );
		var data = new byte[wave.Length];
		wave.ReadExactly( data );
		var samples = Enumerable.Range( 0, expected.Samples.Length )
			.Select( index => BinaryPrimitives.ReadInt16LittleEndian( data.AsSpan( index * 2 ) ) ).ToArray();
		CollectionAssert.AreEqual( expected.Samples, samples );
		CollectionAssert.AreEqual( source, File.ReadAllBytes( input ) );
		Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp", SearchOption.AllDirectories ).Length );
	}

	[TestMethod]
	public void DamagedEarlierEntryDoesNotRenumberTheSelectedClip()
	{
		var input = Bank( Mp2DecoderTests.ToneFrame() );
		var bytes = File.ReadAllBytes( input );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 4 ), int.MaxValue );
		File.WriteAllBytes( input, bytes );
		var output = Path.Combine( root, "second.wav" );
		Export( input, 2, output );
		using var wave = new WaveFileReader( output );
		Assert.AreEqual( 22050, wave.WaveFormat.SampleRate );
		Assert.AreEqual( 1152 * 2L, wave.Length );
		Assert.ThrowsException<InvalidDataException>( () => Export( input, 1, Path.Combine( root, "bad", "first.wav" ) ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( root, "bad" ) ) );
	}

	[DataTestMethod]
	[DataRow( 0u )]
	[DataRow( 3u )]
	[DataRow( uint.MaxValue )]
	public void InvalidEntryNumbersNeverCreateAnOutputDirectory( uint number )
	{
		var input = Bank( Mp2DecoderTests.ToneFrame() );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Export( input, number, Path.Combine( root, "new", "sample.wav" ) ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( root, "new" ) ) );
	}

	[DataTestMethod]
	[DataRow( "partial" )]
	[DataRow( "secondPartial" )]
	[DataRow( "nonzeroPad" )]
	[DataRow( "twoZeros" )]
	[DataRow( "partialHeader" )]
	[DataRow( "empty" )]
	[DataRow( "wav" )]
	public void IncompleteOrNonMpegPayloadRefusesBeforeOutputMutation( string malformed )
	{
		var frame = Mp2DecoderTests.ToneFrame();
		var payload = malformed switch
		{
			"partial" => frame[..^1],
			"secondPartial" => frame.Concat( frame[..^1] ).ToArray(),
			"nonzeroPad" => frame.Concat( new byte[] { 1 } ).ToArray(),
			"twoZeros" => frame.Concat( new byte[] { 0, 0 } ).ToArray(),
			"partialHeader" => frame.Concat( new byte[] { 255, 245, 96 } ).ToArray(),
			"wav" => "RIFFnot a supported MPEG frame"u8.ToArray(),
			_ => Array.Empty<byte>()
		};
		var input = Bank( payload );
		Assert.ThrowsException<InvalidDataException>( () => Export( input, 2, Path.Combine( root, "new", "bad.wav" ) ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( root, "new" ) ) );
	}

	[DataTestMethod]
	[DataRow( "layerIII" )]
	[DataRow( "mpeg1LayerII" )]
	[DataRow( "mpeg25" )]
	[DataRow( "free" )]
	[DataRow( "jointLayerII" )]
	public void UnsupportedDecoderContractsDoNotFallBackToAnotherCodec( string unsupported )
	{
		var frame = Mp2DecoderTests.SilentFrame();
		var header = BinaryPrimitives.ReadUInt32BigEndian( frame );
		header = unsupported switch
		{
			"layerIII" => (header & ~(3u << 17)) | 1u << 17,
			"mpeg1LayerII" => (header & ~(3u << 19)) | 3u << 19,
			"mpeg25" => header & ~(3u << 19),
			"free" => header & ~(15u << 12),
			_ => (header & ~(3u << 6)) | 1u << 6
		};
		BinaryPrimitives.WriteUInt32BigEndian( frame, header );
		Assert.ThrowsException<NotSupportedException>( () => Export( Bank( frame ), 2, Path.Combine( root, "new", "bad.wav" ) ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( root, "new" ) ) );
	}

	[TestMethod]
	public void DecoderInputBoundIsCheckedBeforeReadingAClaimedLargeSelectedPayload()
	{
		var input = Path.Combine( root, "large.sdt" );
		using ( var file = File.Create( input ) )
		{
			using var writer = new BinaryWriter( file );
			writer.Write( 1 ); writer.Write( 8 ); writer.Write( 40 ); writer.Write( Mp2Decoder.MaximumInputBytes + 1 );
			file.SetLength( 48L + Mp2Decoder.MaximumInputBytes + 1 );
		}
		var before = GC.GetAllocatedBytesForCurrentThread();
		Assert.ThrowsException<InvalidDataException>( () => Export( input, 1, Path.Combine( root, "new", "big.wav" ) ) );
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( allocated < Mp2Decoder.MaximumInputBytes / 2, $"Refusal allocated {allocated} bytes before decoding." );
		Assert.IsFalse( Directory.Exists( Path.Combine( root, "new" ) ) );
	}

	[TestMethod]
	public void FailedCrcIsRefusedRatherThanExportingThePlayersDamagedFrameOutput()
	{
		var payload = Mp2DecoderTests.LayerOneReferenceStream();
		Assert.IsTrue( Mp2Decoder.Decode( payload ).CrcFailures > 0 );
		Assert.ThrowsException<InvalidDataException>( () => Export( Bank( payload ), 2, Path.Combine( root, "new", "crc.wav" ) ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( root, "new" ) ) );
	}

	[TestMethod]
	public void DefaultReaderRemainsUnrestrictedAndTheExplicitLimitIsInclusive()
	{
		var payload = Mp2DecoderTests.ToneFrame();
		var bank = new SoundBank( File.ReadAllBytes( Bank( payload ) ) );
		CollectionAssert.AreEqual( payload, bank.GetMpeg( 2 )! );
		CollectionAssert.AreEqual( payload, bank.GetMpeg( 2, payload.Length )! );
		Assert.ThrowsException<InvalidDataException>( () => bank.GetMpeg( 2, payload.Length - 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => bank.GetMpeg( 2, -1 ) );
	}

	[TestMethod]
	public void PhysicallyTruncatedSelectedEntryIsNeverExportedAsAPartialClip()
	{
		var input = Bank( Mp2DecoderTests.ToneFrame() );
		var bank = File.ReadAllBytes( input );
		foreach ( var length in new[] { 0, 3, 4, 7, 8, 12, 20, 48, bank.Length - 1 } )
		{
			File.WriteAllBytes( input, bank[..length] );
			try
			{
				Export( input, 2, Path.Combine( root, "new", "partial.wav" ) );
				Assert.Fail( $"A {length}-byte truncated bank was exported." );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException ) { }
			Assert.IsFalse( Directory.Exists( Path.Combine( root, "new" ) ) );
		}
	}

	[TestMethod]
	public void ExistingOutputAndForeignTemporariesArePreserved()
	{
		var input = Bank( Mp2DecoderTests.ToneFrame() );
		var output = Path.Combine( root, "existing.wav" );
		File.WriteAllText( output, "foreign output" );
		File.WriteAllText( output + ".foreign.tmp", "foreign temporary" );
		Assert.ThrowsException<IOException>( () => Export( input, 2, output ) );
		Assert.AreEqual( "foreign output", File.ReadAllText( output ) );
		Assert.AreEqual( "foreign temporary", File.ReadAllText( output + ".foreign.tmp" ) );
	}

	[TestMethod]
	public void LateMoveRefusalCleansOnlyTheOwnedTemporary()
	{
		var input = Bank( Mp2DecoderTests.ToneFrame() );
		var output = Path.Combine( root, "directory.wav" );
		Directory.CreateDirectory( output );
		File.WriteAllText( Path.Combine( output, "foreign" ), "keep" );
		File.WriteAllText( output + ".foreign.tmp", "keep temporary" );
		Assert.ThrowsException<IOException>( () => Export( input, 2, output ) );
		Assert.AreEqual( "keep", File.ReadAllText( Path.Combine( output, "foreign" ) ) );
		CollectionAssert.AreEqual( new[] { output + ".foreign.tmp" }, Directory.GetFiles( root, "*.tmp" ) );
	}

	[TestMethod]
	public void ActualCliExportReturnsBeforeGameAssetsWindowOrProfileSetup()
	{
		var input = Bank( Mp2DecoderTests.ToneFrame() );
		var output = Path.Combine( root, "cli.wav" );
		var profile = Path.Combine( root, "profile" );
		var oldConfig = Environment.GetEnvironmentVariable( "OPENTPW_CONFIG_DIR" );
		var oldRender = Global.Render;
		var oldFiles = FileSystem;
		try
		{
			Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", profile );
			Game.Run( new[] { "--export-sdt-wav", input, "2", output, "--setup", "--game", Path.Combine( root, "no-assets" ), "--shader-hot-reload" } );
			Assert.IsTrue( File.Exists( output ) );
			Assert.AreSame( oldRender, Global.Render );
			Assert.AreSame( oldFiles, FileSystem );
			Assert.IsFalse( Directory.Exists( profile ) );
		}
		finally { Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", oldConfig ); }
	}

	[DataTestMethod]
	[DataRow( "-1" )]
	[DataRow( "4294967296" )]
	[DataRow( "+1" )]
	[DataRow( "1.5" )]
	public void CliRequiresAnExplicitUnsignedEntryNumber( string value )
	{
		Assert.ThrowsException<ArgumentException>( () => Game.Run( new[] { "--export-sdt-wav", "missing.sdt", value, Path.Combine( root, "new", "sample.wav" ) } ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( root, "new" ) ) );
	}
}

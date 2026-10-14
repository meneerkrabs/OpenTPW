using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class SdtWavIndependentReviewTests
{
	private string root = null!;
	private Logger previousLog = null!;
	[TestInitialize]
	public void Setup() { root = Directory.CreateTempSubdirectory( "opentpw-sdt-review-" ).FullName; previousLog = Log; }
	[TestCleanup]
	public void Cleanup() { Log = previousLog; Directory.Delete( root, true ); }
	private string Bank( byte[] payload )
	{
		var input = Path.Combine( root, "selected.sdt" );
		File.WriteAllBytes( input, SdtEntryTests.CreateBank( ("selected", 1, 40, 0, payload) ) );
		return input;
	}
	[TestMethod]
	public void SilentCompletePcmIsValidRatherThanConfusedWithNoFrames()
	{
		var input = Bank( Mp2DecoderTests.SilentFrame() );
		var output = Path.Combine( root, "silent.wav" );
		SdtWavExport.Run( input, 1, output );
		using var wave = new WaveFileReader( output );
		Assert.AreEqual( 1152 * 2L, wave.Length );
		var bytes = new byte[wave.Length]; wave.ReadExactly( bytes );
		Assert.IsTrue( bytes.All( value => value == 0 ) );
	}
	[TestMethod]
	public void ActualCliRetainsThirdIndexPastTwoDamagedEntries()
	{
		var input = Path.Combine( root, "three.sdt" );
		var frames = Mp2DecoderTests.ToneFrame();
		var bytes = SdtEntryTests.CreateBank( ("bad1", 1, 36, 0, frames), ("bad2", 1, 36, 0, frames), ("third", 1, 40, 0, frames) );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 4 ), int.MaxValue );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 8 ), -1 );
		File.WriteAllBytes( input, bytes );
		var output = Path.Combine( root, "third.wav" );
		Game.Run( new[] { "--export-sdt-wav", input, "3", output } );
		using var wave = new WaveFileReader( output );
		var pcm = new byte[wave.Length]; wave.ReadExactly( pcm );
		var expected = Mp2Decoder.Decode( frames ).Samples;
		CollectionAssert.AreEqual( expected, Enumerable.Range( 0, expected.Length ).Select( i => BinaryPrimitives.ReadInt16LittleEndian( pcm.AsSpan( i * 2 ) ) ).ToArray() );
		CollectionAssert.AreEqual( bytes, File.ReadAllBytes( input ) );
	}
	[TestMethod]
	public void MidstreamFormatChangeRefusesWithoutTouchingForeignFiles()
	{
		var frame = Mp2DecoderTests.ToneFrame(); var changed = frame.ToArray();
		var header = BinaryPrimitives.ReadUInt32BigEndian( changed );
		BinaryPrimitives.WriteUInt32BigEndian( changed, (header & ~(3u << 10)) | (1u << 10) );
		var input = Bank( frame.Concat( changed ).ToArray() ); var original = File.ReadAllBytes( input );
		var output = Path.Combine( root, "changed.wav" ); var foreign = output + ".foreign.tmp";
		File.WriteAllText( foreign, "keep" );
		Assert.ThrowsException<NotSupportedException>( () => SdtWavExport.Run( input, 1, output ) );
		Assert.IsFalse( File.Exists( output ) ); Assert.AreEqual( "keep", File.ReadAllText( foreign ) );
		CollectionAssert.AreEqual( original, File.ReadAllBytes( input ) );
		CollectionAssert.AreEqual( new[] { foreign }, Directory.GetFiles( root, "*.tmp" ) );
	}
	[TestMethod]
	public void MissingCliInputDoesNotCreateDestinationOrProfile()
	{
		var output = Path.Combine( root, "absent-output", "missing.wav" );
		Assert.ThrowsException<FileNotFoundException>( () => Game.Run( new[] { "--export-sdt-wav", Path.Combine( root, "absent.sdt" ), "1", output } ) );
		Assert.IsFalse( Directory.Exists( Path.GetDirectoryName( output ) ) );
	}
	[TestMethod]
	public void InvalidSelectionThroughLimitedReaderDoesNotOpenOrAllocatePayload()
	{
		var bytes = File.ReadAllBytes( Bank( Mp2DecoderTests.ToneFrame() ) ); var opens = 0;
		var bank = new SoundBank( () => { opens++; return new MemoryStream( bytes, false ); }, bytes.Length );
		var initial = opens;
		Assert.IsNull( bank.GetMpeg( 0, 0 ) ); Assert.IsNull( bank.GetMpeg( 2, 0 ) );
		Assert.AreEqual( initial, opens );
		Assert.ThrowsException<InvalidDataException>( () => bank.GetMpeg( 1, 0 ) );
		Assert.AreEqual( initial + 1, opens, "Only the small selected header is read before refusing the payload." );
		CollectionAssert.AreEqual( Mp2DecoderTests.ToneFrame(), bank.GetMpeg( 1 )! );
	}
}

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The advisor's response decode over frames (docs/BROWSER-PERFORMANCE.md, "Event hitches"): the length measured from the frame headers and the
/// PCM decoded in slices equal what the one-go decode gives, so the sequence, the span and the audio of a line do not depend on how it was decoded.
/// </summary>
[TestClass]
public class SlicedAdvisorDecodeTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static byte[] LayerTwoFrame( int mode, int emphasis, bool crc )
	{
		var frame = new byte[144 * 48000 / 22050];
		var word = 0xFFE00000u | 2u << 19 | 2u << 17 | (crc ? 0u : 1u) << 16 | 6u << 12 | (uint)mode << 6 | (uint)emphasis;
		for ( var index = 0; index < 4; index++ )
			frame[index] = (byte)(word >> (24 - index * 8));
		return frame;
	}

	private static byte[] Repeat( byte[] frame, int count, int trailing ) =>
		Enumerable.Range( 0, count ).SelectMany( _ => frame ).Concat( frame.Take( trailing ) ).ToArray();

	[TestMethod]
	public void MeasuredLengthAndSlicedPcmEqualTheOneGoDecode()
	{
		foreach ( var layer in new[] { 1, 2 } )
			foreach ( var mode in layer == 1 ? new[] { 3, 0, 1, 2 } : new[] { 3, 0, 2 } )
				foreach ( var emphasis in new[] { 0, 1 } )
					foreach ( var trailing in new[] { 0, 7 } )
						foreach ( int? playback in new int?[] { null, 0, 1000, 3001, 1_000_000 } )
						{
							var frame = layer == 1 ? Layer1TestFrames.Frame( allocation: 1, mode: mode, bitrateIndex: 8, emphasis: emphasis, crc: false ) : LayerTwoFrame( mode, emphasis, false );
							var stream = Repeat( frame, 6, trailing );
							var expected = Mp2Decoder.Decode( stream, playback );
							Assert.IsTrue( Mp2Decoder.TryMeasure( stream, playback, out var rate, out var frames ), $"layer {layer} mode {mode}" );
							Assert.AreEqual( (expected.SampleRate, expected.SampleFrames), (rate, frames), $"layer {layer} mode {mode} emphasis {emphasis} trailing {trailing} playback {playback}" );
							Assert.AreEqual( expected.DurationSeconds, (double)frames / rate );
							var sliced = new Mp2Decoder.SlicedDecode( stream, playback );
							var runs = 0;
							while ( !sliced.Run( TimeSpan.Zero ) )
								runs++;
							Assert.IsTrue( runs >= 2, "a zero budget decodes one frame a slice" );
							CollectionAssert.AreEqual( expected.Samples, sliced.Result!.Samples );
							Assert.AreEqual( (expected.FrameCount, expected.TrailingBytes), (sliced.Result.FrameCount, sliced.Result.TrailingBytes) );
						}
	}

	[TestMethod]
	public void StreamsTheDecoderRejectsAreNotMeasured()
	{
		Assert.IsFalse( Mp2Decoder.TryMeasure( Array.Empty<byte>(), null, out _, out _ ) );
		Assert.IsFalse( Mp2Decoder.TryMeasure( new byte[] { 1, 2, 3, 4, 5, 6 }, null, out _, out _ ) );
		// a frame that changes channel mode mid-stream, which the decoder rejects
		var mixed = LayerTwoFrame( 3, 0, false ).Concat( LayerTwoFrame( 0, 0, false ) ).ToArray();
		Assert.ThrowsException<NotSupportedException>( () => Mp2Decoder.Decode( mixed ) );
		Assert.IsFalse( Mp2Decoder.TryMeasure( mixed, null, out _, out _ ) );
		// only a frame cut short: nothing complete to measure
		Assert.IsFalse( Mp2Decoder.TryMeasure( LayerTwoFrame( 3, 0, false ).AsSpan( 0, 100 ), null, out _, out _ ) );
	}

	[TestMethod]
	public void OriginalResponsesMeasureAndSliceAsTheyDecode()
	{
		var fileSystem = new BaseFileSystem( StringTableTests.OriginalDataDirectory() );
		var table = AdvisorResponses.Table;
		// every seventh response of the global bank, every response of the level's bank and the welcome line (587): a sample that keeps the test short
		var checkedResponses = 0;
		foreach ( var response in table.Values.OrderBy( response => response.Id ).Where( response => response.Id % 7 == 0 || response.Local || response.Id == 587 ) )
		{
			var (mpeg, playback, _, _) = Advisor.LoadResponseParts( fileSystem, response, "jungle" );
			var expected = Advisor.LoadResponse( fileSystem, response, "jungle" ).Audio;
			Assert.IsTrue( Mp2Decoder.TryMeasure( mpeg, playback, out var rate, out var frames ), $"response {response.Id}" );
			Assert.AreEqual( (expected.SampleRate, expected.SampleFrames), (rate, frames), $"response {response.Id}" );
			var sliced = new Mp2Decoder.SlicedDecode( mpeg, playback );
			while ( !sliced.Run( TimeSpan.FromMilliseconds( 2 ) ) ) { }
			CollectionAssert.AreEqual( expected.Samples, sliced.Result!.Samples, $"response {response.Id}" );
			checkedResponses++;
		}
		Assert.IsTrue( checkedResponses > 80 );
	}
}

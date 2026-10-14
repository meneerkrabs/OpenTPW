using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class RuntimeSoundInputPreflightTests
{
	private string root = null!; private Logger previous = null!; private Logger.LogDelegate? oldOnLog; private readonly List<string> messages = new();
	[TestInitialize]
	public void Setup() { root = Directory.CreateTempSubdirectory( "opentpw-runtime-input-" ).FullName; previous = Log; Log = new(); oldOnLog = Logger.OnLog; Logger.OnLog += ( severity, text ) => messages.Add( text ); }
	[TestCleanup]
	public void Cleanup() { Logger.OnLog = oldOnLog; Log = previous; Directory.Delete( root, true ); }
	private SoundCategory Category( SoundBank bank )
	{
		File.WriteAllBytes( Path.Combine( root, "cat_testSFX.map" ), AudioTests.CreateCatalog() ); File.WriteAllBytes( Path.Combine( root, "cat_testBANK.map" ), new byte[28] );
		var category = SoundCategory.TryLoad( new BaseFileSystem( root ), "", "cat_test" )!; Assert.IsNotNull( category );
		((Dictionary<int, SoundBank?>)typeof( SoundCategory ).GetField( "banks", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( category )!)[1] = bank; return category;
	}
	private sealed class SparseBank
	{
		private readonly byte[] header = new byte[48]; private readonly byte[]? payload; public long CurrentLength; public long PayloadBytes; public int Opens;
		public SparseBank( int payloadBytes, byte[]? payload = null ) { this.payload = payload; CurrentLength = 48L + payloadBytes; BinaryPrimitives.WriteInt32LittleEndian( header, 1 ); BinaryPrimitives.WriteInt32LittleEndian( header.AsSpan( 4 ), 8 ); BinaryPrimitives.WriteInt32LittleEndian( header.AsSpan( 8 ), 40 ); BinaryPrimitives.WriteInt32LittleEndian( header.AsSpan( 12 ), payloadBytes ); }
		public SoundBank OpenBank() => new( () => { Opens++; return new Input( this ); }, CurrentLength );
		private sealed class Input( SparseBank bank ) : Stream
		{
			private long position; public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false; public override long Length => bank.CurrentLength; public override long Position { get => position; set => position = value; }
			public override int Read( byte[] bytes, int offset, int count ) => Read( bytes.AsSpan( offset, count ) );
			public override int Read( Span<byte> bytes )
			{
				var count = (int)Math.Min( bytes.Length, Math.Max( 0, Length - position ) ); var result = bytes[..count]; result.Clear(); if ( position < 48 ) { var prefix = (int)Math.Min( count, 48 - position ); bank.header.AsSpan( (int)position, prefix ).CopyTo( result ); }
				if ( position >= 48 ) { bank.PayloadBytes += count; if ( bank.payload != null ) bank.payload.AsSpan( (int)position - 48, count ).CopyTo( result ); }
				position += count; return count;
			}
			public override long Seek( long offset, SeekOrigin origin ) => Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, _ => Length + offset }; public override void Flush() { }
			public override void SetLength( long value ) => throw new NotSupportedException(); public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
		}
	}
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task OversizedRuntimeSampleIsNullLoggedAndNeverAllocatesOrReadsPayload( bool cooperative )
	{
		var sparse = new SparseBank( Mp2Decoder.MaximumInputBytes + 1 ); var category = Category( sparse.OpenBank() ); using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		var before = GC.GetAllocatedBytesForCurrentThread(); var audio = cooperative ? await system.DecodeSentenceAsync( category, sample, cooperative: true ) : system.Decode( category, sample ); var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Console.WriteLine( $"Oversized {(cooperative ? "cooperative" : "synchronous")}: allocated {allocated} bytes, payload read {sparse.PayloadBytes} bytes." ); Assert.IsNull( audio ); Assert.AreEqual( 0L, sparse.PayloadBytes ); Assert.IsTrue( allocated < 128 * 1024, $"Runtime refused only after allocating {allocated} bytes." ); Assert.IsTrue( messages.Any( text => text.Contains( "cat_test" ) && text.Contains( "input bound" ) ) ); Assert.AreEqual( 0, mixer.Voices.Count );
		var opens = sparse.Opens; Assert.IsNull( await system.DecodeSentenceAsync( category, sample, cooperative: true, yieldBatch: () => throw new AssertFailedException( "Cached refusal must not decode again." ) ) ); Assert.AreEqual( opens, sparse.Opens );
	}
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task ExactlyAtExistingBoundStillReadsBeforeContentRefusal( bool cooperative )
	{
		var sparse = new SparseBank( Mp2Decoder.MaximumInputBytes ); var category = Category( sparse.OpenBank() ); using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		var audio = cooperative ? await system.DecodeSentenceAsync( category, sample, cooperative: true ) : system.Decode( category, sample ); Assert.IsNull( audio ); Assert.AreEqual( (long)Mp2Decoder.MaximumInputBytes, sparse.PayloadBytes ); Assert.IsTrue( messages.Any( text => text.Contains( "sync word" ) ) ); Assert.IsFalse( messages.Any( text => text.Contains( "input bound" ) ) );
	}
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task ValidSampleKeepsPcmCacheAndRepeatedDecodeBehavior( bool cooperative )
	{
		var payload = Enumerable.Range( 0, 5 ).SelectMany( _ => Mp2DecoderTests.ToneFrame() ).ToArray(); var sparse = new SparseBank( payload.Length, payload ); var category = Category( sparse.OpenBank() ); using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		var audio = cooperative ? await system.DecodeSentenceAsync( category, sample, cooperative: true, yieldBatch: () => Task.CompletedTask ) : system.Decode( category, sample ); Assert.IsNotNull( audio ); CollectionAssert.AreEqual( Mp2Decoder.Decode( payload ).Samples, audio.Samples ); Assert.AreEqual( (long)payload.Length, sparse.PayloadBytes ); var opens = sparse.Opens;
		Assert.AreSame( audio, await system.DecodeSentenceAsync( category, sample, cooperative: true ) ); Assert.AreEqual( opens, sparse.Opens ); system.Forget( category ); Assert.AreEqual( 0L, system.RetainedSentenceBytes );
	}
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task SourceLengthChangeStillPropagatesIoWithoutReadingPayload( bool cooperative )
	{
		var payload = Mp2DecoderTests.ToneFrame(); var sparse = new SparseBank( payload.Length, payload ); var category = Category( sparse.OpenBank() ); sparse.CurrentLength++; using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		if ( cooperative ) await Assert.ThrowsExceptionAsync<IOException>( () => system.DecodeSentenceAsync( category, sample, cooperative: true ) ); else Assert.ThrowsException<IOException>( () => system.Decode( category, sample ) ); Assert.AreEqual( 0L, sparse.PayloadBytes ); Assert.IsFalse( messages.Any( text => text.Contains( "input bound" ) ) );
	}
	[TestMethod]
	public async Task CooperativeCancellationIsNotCaughtOrCachedAsContentFailure()
	{
		var payload = Enumerable.Range( 0, 5 ).SelectMany( _ => Mp2DecoderTests.ToneFrame() ).ToArray(); var sparse = new SparseBank( payload.Length, payload ); var category = Category( sparse.OpenBank() ); using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		var task = system.DecodeSentenceAsync( category, sample, cooperative: true, yieldBatch: () => throw new OperationCanceledException( "synthetic scheduler cancellation" ) ); await Assert.ThrowsExceptionAsync<OperationCanceledException>( () => task ); Assert.IsTrue( task.IsCanceled );
		var result = await system.DecodeSentenceAsync( category, sample, cooperative: true, yieldBatch: () => Task.CompletedTask ); Assert.IsNotNull( result ); Assert.AreEqual( 5, result.FrameCount ); Assert.IsFalse( messages.Any( text => text.Contains( "input bound" ) ) );
	}
}

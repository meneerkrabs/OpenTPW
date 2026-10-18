using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class StartupAudioSchedulingTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private sealed class Fixture : IDisposable
	{
		private readonly string root = Directory.CreateTempSubdirectory( "opentpw-startup-audio-" ).FullName;
		public readonly byte[] Payload = Enumerable.Range( 0, 5 ).SelectMany( _ => Mp2DecoderTests.ToneFrame() ).ToArray();
		public SoundCategory Category { get; }
		public readonly SoundSample Sample = new( 1, 65535, 0, 1, 0 );
		public int Opens;
		public Action? BeforeOpen;
		public Fixture()
		{
			var bytes = new byte[48 + Payload.Length];
			BinaryPrimitives.WriteInt32LittleEndian( bytes, 1 );
			BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 4 ), 8 );
			BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 8 ), 40 );
			BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 12 ), Payload.Length );
			Payload.CopyTo( bytes, 48 );
			var bank = new SoundBank( () => { BeforeOpen?.Invoke(); Interlocked.Increment( ref Opens ); return new MemoryStream( bytes, writable: false ); }, bytes.Length );
			File.WriteAllBytes( Path.Combine( root, "cat_testSFX.map" ), AudioTests.CreateCatalog() );
			File.WriteAllBytes( Path.Combine( root, "cat_testBANK.map" ), new byte[28] );
			Category = SoundCategory.TryLoad( new BaseFileSystem( root ), "", "cat_test" )!;
			((Dictionary<int, SoundBank?>)typeof( SoundCategory ).GetField( "banks", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( Category )!)[1] = bank;
			Opens = 0;
		}
		public void Dispose() => Directory.Delete( root, true );
	}

	[TestMethod]
	public async Task ColdCooperativeCallYieldsBeforeIoAndSharesOneDecode()
	{
		using var fixture = new Fixture();
		using var mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer, 17 );
		var gate = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
		var yields = 0;
		var first = system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: true,
			yieldBatch: () => ++yields == 1 ? gate.Task : Task.CompletedTask );
		Assert.IsFalse( first.IsCompleted );
		Assert.AreEqual( 0, fixture.Opens, "No bank read runs inline on the caller's startup stack." );
		for ( var index = 0; index < 16; index++ )
			Assert.AreSame( first, system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: true,
				yieldBatch: () => throw new AssertFailedException( "A pending sample must share its first scheduler." ) ) );
		gate.SetResult();
		var audio = await first;
		Assert.IsNotNull( audio );
		CollectionAssert.AreEqual( Mp2Decoder.Decode( fixture.Payload ).Samples, audio.Samples );
		Assert.AreEqual( 2, fixture.Opens, "One entry header and one payload, independent of waiter count." );
		Assert.AreEqual( 2, yields, "Initial yield plus one boundary between five frames in batches of four." );
		Assert.AreEqual( 17u, system.Seed, "Sharing PCM does not draw from the sound generator." );
		Assert.AreSame( audio, await system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: true,
			yieldBatch: () => throw new AssertFailedException( "Cached PCM must return without yielding." ) ) );
	}

	[TestMethod]
	public async Task NativeCallersShareThePendingWorker()
	{
		using var fixture = new Fixture();
		using var mixer = new AudioMixer( null );
		using var gate = new ManualResetEventSlim();
		fixture.BeforeOpen = () => { if ( !gate.Wait( TimeSpan.FromSeconds( 10 ) ) ) throw new TimeoutException(); };
		var system = new SoundEventSystem( mixer );
		var first = system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: false );
		try
		{
			for ( var index = 0; index < 16; index++ )
				Assert.AreSame( first, system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: false ) );
		}
		finally { gate.Set(); }
		var audio = await first;
		Assert.IsNotNull( audio );
		CollectionAssert.AreEqual( Mp2Decoder.Decode( fixture.Payload ).Samples, audio.Samples );
		Assert.AreEqual( 2, fixture.Opens );
	}

	[TestMethod]
	public async Task SharedCancellationAndSourceFailureCanBeRetried()
	{
		using var fixture = new Fixture();
		using var mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer );
		var gate = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
		var first = system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: true, yieldBatch: () => gate.Task );
		var second = system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: true );
		Assert.AreSame( first, second );
		gate.SetException( new OperationCanceledException( "synthetic scheduler cancellation" ) );
		await Assert.ThrowsExceptionAsync<OperationCanceledException>( () => first );
		Assert.IsTrue( second.IsCanceled );
		Assert.AreEqual( 0, fixture.Opens );
		fixture.BeforeOpen = () => throw new IOException( "synthetic source failure" );
		await Assert.ThrowsExceptionAsync<IOException>( () => system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: true, yieldBatch: () => Task.CompletedTask ) );
		fixture.BeforeOpen = null;
		Assert.IsNotNull( await system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: true, yieldBatch: () => Task.CompletedTask ) );
		Assert.AreEqual( 2, fixture.Opens );
	}

	[TestMethod]
	public async Task LeavingDuringSharedDecodeDoesNotRestoreCacheOrStartStoppedVoice()
	{
		using var fixture = new Fixture();
		using var mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer );
		var gate = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
		var task = system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: true,
			yieldBatch: () => gate.Task );
		var stopped = new PlacedSound( mixer, AudioChannel.Effects, 1, null, 0, false, task );
		var live = new PlacedSound( mixer, AudioChannel.Effects, 1, null, 0, false,
			system.DecodeSentenceAsync( fixture.Category, fixture.Sample, cooperative: true ) );
		stopped.Stop();
		system.Forget( fixture.Category );
		gate.SetResult();
		Assert.IsNotNull( await task );
		Assert.AreEqual( 0L, system.DecodedBytes );
		Assert.AreEqual( 0L, system.RetainedSentenceBytes );
		Assert.IsTrue( stopped.Pump() );
		Assert.IsNull( stopped.Voice );
		Assert.IsTrue( live.Pump(), "Stopping one owner cannot cancel PCM needed by another." );
		Assert.IsNotNull( live.Voice );
		Assert.AreEqual( 1, mixer.Voices.Count );
	}
	[TestMethod]
	public async Task NewCategoryDoesNotJoinThePreviousParksDecode()
	{
		using var oldPark = new Fixture();
		using var newPark = new Fixture();
		using var mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer );
		var gate = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
		var oldTask = system.DecodeSentenceAsync( oldPark.Category, oldPark.Sample, cooperative: true, yieldBatch: () => gate.Task );
		system.Forget( oldPark.Category );
		var newTask = system.DecodeSentenceAsync( newPark.Category, newPark.Sample, cooperative: true, yieldBatch: () => Task.CompletedTask );
		Assert.AreNotSame( oldTask, newTask );
		var current = await newTask;
		Assert.IsNotNull( current );
		gate.SetResult();
		Assert.IsNotNull( await oldTask );
		Assert.AreEqual( current.Samples.LongLength * sizeof( short ), system.DecodedBytes );
		Assert.AreSame( current, await system.DecodeSentenceAsync( newPark.Category, newPark.Sample, cooperative: true ) );
	}

}

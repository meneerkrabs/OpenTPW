using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>AUDIO-018 and ADVISOR-012: the QMixer placement of a voice, from the axis mapping of the camera rows to the flat placement of an any-distance event (tp.exe 0x6bc51d, QMixer.dll 0x18007abd, 0x18009dc0).</summary>
[TestClass]
[DoNotParallelize]
public class AudioQmixerPlacementTests
{
	private static Mp2Audio Mono( params short[] samples ) => new( AudioMixer.OutputRate, 1, samples, 1, 0 );

	[TestCleanup]
	public void ResetListener() => AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );

	private static double[] Adapter( double[] v ) => new[] { v[0], v[2], v[1] }; // 0x6d2640 / 0x6d2a10 / 0x6d2b20 with the driver's byte +8 bit 0 clear
	private static double[] Convert( double[] v ) => new[] { v[0], v[1], -v[2] }; // QMixer 0x18001020 with the engine byte +4 set
	private static double[] Cross( double[] a, double[] b ) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
	private static double Dot( double[] a, double[] b ) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
	private static double[] Sub( double[] a, double[] b ) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
	private static double[] Unit( double[] v ) { var length = Math.Sqrt( Dot( v, v ) ); return new[] { v[0] / length, v[1] / length, v[2] / length }; }

	/// <summary>The pan the PC computes: the adapters' swap, QMixer's own reflection, the listener basis row0 = up x front (0x1800ce70), the rotation of the offset (0x1800cf10) and atan2(x, z) (0x1800cb10).</summary>
	private static int PcPan( double[] listener, double[] front, double[] up, double[] source )
	{
		var f = Unit( Convert( Adapter( front ) ) );
		var u = Unit( Convert( Adapter( up ) ) );
		var right = Cross( u, f );
		var offset = Sub( Convert( Adapter( source ) ), Convert( Adapter( listener ) ) );
		var azimuth = Math.Atan2( Dot( offset, right ), Dot( offset, f ) ) * 180 / Math.PI;
		return AudioListener.DirectSoundPan( (float)azimuth );
	}

	private static Vector3 V( double[] v ) => new( (float)v[0], (float)v[1], (float)v[2] );

	[TestMethod]
	public void TheCameraAxesReachQMixerAsARotationSoThePanKeepsItsSide()
	{
		// [BIN:TPW-EXE:0x6d2640,0x6d2a10,0x6d2b20 axis mapping] (x, y, z) -> (x, z, y); [BIN:QMIXER-DLL:0x18001020] the third component negated: together (x, y, z) -> (x, z, -y), a rotation
		var random = new Random( 1018 );
		for ( var trial = 0; trial < 400; trial++ )
		{
			var yaw = (random.NextDouble() * 2 - 1) * Math.PI;
			var pitch = (random.NextDouble() * 2 - 1) * 1.2;
			var right = new[] { Math.Cos( yaw ), 0, -Math.Sin( yaw ) };
			var front = new[] { Math.Sin( yaw ) * Math.Cos( pitch ), Math.Sin( pitch ), Math.Cos( yaw ) * Math.Cos( pitch ) };
			var up = Cross( front, right );
			var listener = new[] { random.NextDouble() * 1000, random.NextDouble() * 20, random.NextDouble() * 1000 };
			var source = new[] { random.NextDouble() * 1000, random.NextDouble() * 60, random.NextDouble() * 1000 };
			AudioListener.Update( V( listener ), V( front ), V( up ) );
			var (_, pan) = AudioListener.Placement( V( source ), 1e6f );
			Assert.AreEqual( PcPan( listener, front, up, source ), pan, 2, $"trial {trial}" );
		}
	}

	[TestMethod]
	public void ASourceToTheRightOfAnUprightCameraPansRightThroughTheWholePipeline()
	{
		double[] listener = { 0, 0, 0 }, front = { 0, 0, 1 }, up = { 0, 1, 0 };
		Assert.AreEqual( 4000, PcPan( listener, front, up, new double[] { 10, 0, 0 } ) );
		Assert.AreEqual( -4000, PcPan( listener, front, up, new double[] { -10, 0, 0 } ) );
		Assert.AreEqual( 0, PcPan( listener, front, up, new double[] { 0, 0, 10 } ) );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ), new Vector3( 0, 1, 0 ) );
		Assert.AreEqual( 4000, AudioListener.Placement( new Vector3( 10, 0, 0 ), 100 ).Pan );
	}

	// ---- AUDIO-018 / ADVISOR-012: the flat placement of an any-distance voice ----

	[TestMethod]
	public void AnAnyDistanceVoiceIsPlacedFlatCentredAndNeverTurnedDownByDistance()
	{
		// [BIN:TPW-EXE:0x6bc51d flat placement] [BIN:QMIXER-DLL:0x18007abd SetPosition polar]: azimuth 0, distance 9.2e-5 inside the minimum distance, so pan 0 and distance gain 1
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		using var mixer = new AudioMixer( null );
		var flat = mixer.Play( Mono( 1000 ), AudioChannel.Effects, 0.5f, new Vector3( 5000, 0, 0 ), 100 );
		flat.PlacedFlat = true;
		CollectionAssert.AreEqual( new short[] { 500, 500 }, mixer.Mix( 1 ), "both channels at the voice's gain, far past its range" );
		using var other = new AudioMixer( null );
		other.Play( Mono( 1000 ), AudioChannel.Effects, 0.5f, new Vector3( 50, 0, 0 ), 100 );
		var placed = other.Mix( 1 );
		Assert.IsTrue( placed[0] < placed[1], "the same voice placed (not flat) is panned to the right" );
	}

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static SoundCategory Category( SoundEventSystem system, ushort flags )
	{
		var sound = new SoundEntry( 100, 100, 0, 0, 0, 0, 1, 10, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		Set( catalog, "Events", new[] { new SoundEvent( 1, 100, flags, 0, new[] { sound } ) } );
		Set( category, "Catalog", catalog );
		system.RememberDecoded( category, sound.Samples[0], new Mp2Audio( AudioMixer.OutputRate, 1, new short[] { 1000, 1000, 1000, 1000 }, 1, 0 ), false );
		return category;
	}

	[DataTestMethod]
	[DataRow( (ushort)0x200, true )]
	[DataRow( (ushort)0x0, false )]
	public void APlacedEventIsFlatExactlyWhenItIsFlaggedAnyDistance( ushort flags, bool flat )
	{
		Log ??= new();
		using var mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer, 7, () => 0 ) { PoolClock = () => 0 };
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		var voice = system.PlayAt( Category( system, flags ), 1, AudioChannel.Effects, new Vector3( 100, 0, 0 ), 10 )!;
		Assert.AreEqual( flat, voice.PlacedFlat );
		system.Pump();
		var pcm = mixer.Mix( 1 );
		if ( flat )
			CollectionAssert.AreEqual( new short[] { 1000, 1000 }, pcm, "audible at any distance, centred, at the voice's gain" );
		else
			CollectionAssert.AreEqual( new short[] { 0, 0 }, pcm, "a placed voice 100 units away with a range of 10 is not heard" );
	}
}

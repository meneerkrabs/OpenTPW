using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

[TestClass]
public class Md2AnimationTests
{
	// Synthetic animation with one record on node 1: Bézier position, eased rotation, linear scale.
	private const int Points = 0xB8;
	private const int PositionTimes = 0xE8;
	private const int PositionHeader = 0xF0;
	private const int RotationKeys = 0x100;
	private const int Curves = 0x13C;
	private const int ScaleKeys = 0x144;
	private const int Records = 0x164;
	private const int Trailer = 0x1E4;
	private static readonly byte[] EaseIn = { 5, 22, 46, 77, 114, 152, 188, 224 };

	internal static byte[] CreateAnimation()
	{
		var data = new byte[Trailer + 72];
		W32( data, 0, ModelFile.Magic );
		W32( data, 4, 221 );
		W32( data, 8, 203 );
		Encoding.ASCII.GetBytes( "synthm1.MD2" ).CopyTo( data, 0x18 );
		W16( data, 0x42, 2 );
		W32( data, 0x98, Trailer );

		WF( data, Points, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0 );
		W32( data, PositionTimes, 0 );
		W32( data, PositionTimes + 4, 100 );
		W32( data, PositionHeader, ModelPositionKeys.BezierKind );
		W16( data, PositionHeader + 4, 4 );
		W16( data, PositionHeader + 6, 2 );
		W32( data, PositionHeader + 8, Points );
		W32( data, PositionHeader + 12, PositionTimes );

		WriteRotation( data, 0, 0, 0, 0, 0, 0, 1 );
		WriteRotation( data, 1, 50, -1, 0, 1, 0, 0 );
		WriteRotation( data, 2, 100, -1, 0, -MathF.Sqrt( 0.5f ), 0, -MathF.Sqrt( 0.5f ) );
		EaseIn.CopyTo( data, Curves );
		W16( data, ScaleKeys, 0 );
		WF( data, ScaleKeys + 4, 1, 1, 1 );
		W16( data, ScaleKeys + 16, 100 );
		WF( data, ScaleKeys + 20, 3, 1, 1 );

		W32( data, Records + 4, 0x199 );
		W32( data, Records + 12, 100 );
		W32( data, Records + 16, 3 | (2 << 16) );
		W32( data, Records + 20, 1 );
		W32( data, Records + 24, PositionHeader );
		W32( data, Records + 28, RotationKeys );
		W32( data, Records + 32, ScaleKeys );
		W32( data, Records + 52, Curves );

		W32( data, Trailer, 4 );
		W32( data, Trailer + 8, 100 );
		W32( data, Trailer + 12, 1 | (2 << 16) );
		W32( data, Trailer + 16, 3 | (1 << 16) );
		W32( data, Trailer + 32, PositionHeader );
		W32( data, Trailer + 40, RotationKeys );
		W32( data, Trailer + 44, Records );
		W32( data, Trailer + 60, Curves );
		return data;
	}

	private static void WriteRotation( byte[] data, int key, ushort time, short ease, float x, float y, float z, float w )
	{
		var entry = RotationKeys + key * 20;
		W16( data, entry, time );
		BinaryPrimitives.WriteInt16LittleEndian( data.AsSpan( entry + 2 ), ease );
		WF( data, entry + 4, x, y, z, w );
	}

	private static void W16( byte[] data, int offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( offset ), value );
	private static void W32( byte[] data, int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset ), value );
	private static void W32( byte[] data, int offset, int value ) => W32( data, offset, (uint)value );

	private static void WF( byte[] data, int offset, params float[] values )
	{
		for ( var index = 0; index < values.Length; index++ )
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( offset + index * 4 ), values[index] );
	}

	private static ModelFile Read( byte[] data ) => new( new MemoryStream( data ) );
	private static void Rejects( byte[] data ) => Assert.ThrowsException<InvalidDataException>( () => Read( data ) );

	private static void AreClose( Quaternion expected, Quaternion actual )
	{
		Assert.AreEqual( 1, MathF.Abs( Quaternion.Dot( expected, actual ) ), 1e-5, $"{expected} != {actual}" );
	}

	[TestMethod]
	public void DecodesPositionRotationAndScaleTracks()
	{
		var clip = Read( CreateAnimation() ).Clip!;
		Assert.AreEqual( 100, clip.Duration );
		var track = clip.Tracks[0];
		Assert.AreEqual( 1, track.NodeIndex );
		Assert.AreEqual( 0x199u, track.Flags );
		Assert.IsFalse( track.HasUndecodedPayload );
		Assert.IsTrue( track.Position!.IsBezier );
		CollectionAssert.AreEqual( new uint[] { 0, 100 }, (System.Collections.ICollection)track.Position.Times );
		Assert.AreEqual( 3, track.Rotations.Count );
		Assert.AreEqual( (short)0, track.Rotations[0].Ease );
		CollectionAssert.AreEqual( EaseIn, track.EaseCurves[0] );
		Assert.AreEqual( 2, track.Scales.Count );
	}

	[TestMethod]
	public void SamplesBezierEasedSlerpAndLinearScale()
	{
		var track = Read( CreateAnimation() ).Clip!.Tracks[0];
		// Bézier P0=C0=C1=0, P1=8: s³·8 at s = 0.5 is 1 (linear interpolation would give 4).
		Assert.AreEqual( new NVector3( 1, 0, 0 ), track.SampleTranslation( 50 ) );
		Assert.AreEqual( new NVector3( 8, 0, 0 ), track.SampleTranslation( 500 ) );
		Assert.AreEqual( new NVector3( 2, 1, 1 ), track.SampleScale( 50 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => track.SampleTranslation( -5 ) );

		// Ease curve: s = 0.5 lies between samples 4/9 (77) and 5/9 (114).
		var eased = (77 + 0.5f * (114 - 77)) / 255f;
		Assert.AreEqual( eased, ModelAnimationTrack.Ease( EaseIn, 0.5f ), 1e-6 );
		Assert.AreEqual( 0, ModelAnimationTrack.Ease( EaseIn, 0 ) );
		// The original scales by 8.999995 (TOC literal 0x410FFFFB), so fraction 1 stays in the last segment just short of 1.
		Assert.AreEqual( 0x410FFFFBu, BitConverter.SingleToUInt32Bits( ModelAnimationTrack.EaseScale ) );
		var local = 8.999995f - 8;
		Assert.AreEqual( MathF.FusedMultiplyAdd( 1 - local, 224 / 255f, local ), ModelAnimationTrack.Ease( EaseIn, 1 ) );
		Assert.AreNotEqual( 1f, ModelAnimationTrack.Ease( EaseIn, 1 ) );
		Assert.AreEqual( 1, ModelAnimationTrack.Ease( EaseIn, 1 ), 1e-5 );
		AreClose( new Quaternion( 0, MathF.Sin( eased * MathF.PI / 2 ), 0, MathF.Cos( eased * MathF.PI / 2 ) ), track.SampleRotation( 25 )!.Value );

		// No hemisphere flip: 180° -> 450° continues forward through 315° instead of going back.
		AreClose( new Quaternion( 0, MathF.Sin( 315 * MathF.PI / 360 ), 0, MathF.Cos( 315 * MathF.PI / 360 ) ), track.SampleRotation( 75 )!.Value );
	}

	[TestMethod]
	public void ChannelsBeforeTheirFirstKeyAreUnchangedAndTheLastRotationHolds()
	{
		var data = CreateAnimation();
		W16( data, RotationKeys, 10 );
		W32( data, PositionTimes, 10 );
		var track = Read( data ).Clip!.Tracks[0];
		Assert.IsNull( track.SampleRotation( 9.9f ) );
		Assert.IsNull( track.SampleTranslation( 9.5f ) );
		AreClose( Quaternion.Identity, track.SampleRotation( 10 )!.Value );
		Assert.AreEqual( new NVector3( 1, 1, 1 ), track.SampleScale( 0 ) );
		AreClose( new Quaternion( 0, -MathF.Sqrt( 0.5f ), 0, -MathF.Sqrt( 0.5f ) ), track.SampleRotation( 150 )!.Value );

		// The player keeps the stored node component for an unchanged channel.
		var model = Read( Md2ModelFileTests.CreateGeometry() );
		var player = new ModelAnimationPlayer( model, Read( data ).Clip!, 30 );
		player.SetTick( 5 );
		Assert.AreEqual( 0, NVector3.Distance( model.Nodes[1].Transform.Translation, player.LocalTransform( 1 ).Translation ), 1e-6 );
	}

	[TestMethod]
	public void PlayerComposesParentRelativeMatricesAndLoops()
	{
		var model = Read( Md2ModelFileTests.CreateGeometry() );
		var player = new ModelAnimationPlayer( model, Read( CreateAnimation() ).Clip!, 50 );
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		Assert.AreEqual( new NVector3( 1, 2, 8 ), rest[1].Translation );

		var world = new Matrix4x4[model.Nodes.Count];
		player.Advance( 1 );
		Assert.AreEqual( 50, player.Tick );
		player.ComputeWorldTransforms( world );
		Assert.AreEqual( new NVector3( 1, 2, 3 ), world[0].Translation );
		Assert.AreEqual( new NVector3( 2, 2, 3 ), world[1].Translation );
		Assert.AreEqual( 2, NVector3.TransformNormal( NVector3.UnitX, world[1] ).Length(), 1e-5 );

		player.Advance( 2 );
		Assert.AreEqual( 50, player.Tick );
		player.Loop = false;
		player.Advance( 10 );
		Assert.AreEqual( 100, player.Tick );
		player.Reset();
		Assert.AreEqual( 0, player.Tick );
	}

	[TestMethod]
	public void PlayerRejectsTracksOutsideTheModel()
	{
		var model = Read( Md2ModelFileTests.CreateGeometry() );
		var data = CreateAnimation();
		W32( data, Records + 20, 2 );
		Assert.ThrowsException<InvalidDataException>( () => new ModelAnimationPlayer( model, Read( data ).Clip!, 30 ) );
		Assert.ThrowsException<ArgumentException>( () => new ModelAnimationPlayer( Read( data ), Read( data ).Clip!, 30 ) );
	}

	[TestMethod]
	public void AnimatorTickRateScalesClipTimeAndDefaultsToThirty()
	{
		var model = Read( Md2ModelFileTests.CreateGeometry() );
		var clip = Read( CreateAnimation() ).Clip!;
		Assert.AreEqual( 30f, ObjectAnimator.DefaultTicksPerSecond );
		Assert.AreEqual( 30f, new ObjectAnimator( model ).TicksPerSecond );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new ObjectAnimator( model, 0 ) );

		var standard = new ObjectAnimator( model );
		var half = new ObjectAnimator( model, 15 );
		// Clip length in seconds is duration / rate, so a 15 ticks/s clock takes twice as long.
		Assert.AreEqual( clip.Duration / 30d, standard.Play( 0, clip, "synth", true ), 1e-6 );
		Assert.AreEqual( clip.Duration / 15d, half.Play( 0, clip, "synth", true ), 1e-6 );

		standard.Advance( 1 );
		half.Advance( 1 );
		Assert.AreEqual( 30f, standard.GetTick( 0 ), 1e-4f );
		Assert.AreEqual( 15f, half.GetTick( 0 ), 1e-4f );
	}

	[TestMethod]
	public void RejectsMalformedTracks()
	{
		var data = CreateAnimation();
		WF( data, RotationKeys + 4, 0, 0, 0, 2 );
		Rejects( data ); // not a unit quaternion
		data = CreateAnimation();
		W16( data, RotationKeys + 20, 0 );
		Rejects( data ); // rotation times must increase
		data = CreateAnimation();
		W32( data, PositionTimes + 4, 0 );
		W32( data, PositionTimes, 5 );
		Rejects( data ); // position times decrease
		data = CreateAnimation();
		W16( data, PositionHeader + 4, 2 );
		Rejects( data ); // Bézier point count must be 3 × keys − 2
		data = CreateAnimation();
		W32( data, PositionHeader, 0x13 );
		Rejects( data ); // unknown position kind
		data = CreateAnimation();
		W32( data, Records + 4, 0x198 );
		Rejects( data ); // flags disagree with position pointer
		data = CreateAnimation();
		W32( data, Records + 28, Trailer - 20 );
		Rejects( data ); // rotation keys run into the trailer
		data = CreateAnimation();
		W32( data, Trailer + 16, 4 | (1 << 16) );
		Rejects( data ); // trailer rotation total
		data = CreateAnimation();
		W32( data, Records, 1 );
		Rejects( data ); // record index
		data = CreateAnimation();
		WriteRotation( data, 0, 0, 200, 0, 0, 0, 1 );
		Rejects( data ); // easing curves run past the payload
	}
}

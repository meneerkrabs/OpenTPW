using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// Synthetic members for the decoder paths read from the Feral Mac build (docs/reverse/PPC-formats.md):
/// quantised vertex groups (record +40), node-flag toggles (record flag 0x20000) and texture-frame
/// tracks (trailer +48). No original vertex arrays are used.
/// </summary>
[TestClass]
public class Md2VertexAnimationTests
{
	private const int Block = 0xB8;
	private const int Groups = 0xE4;
	private const int Group0Indices = 0x120;
	private const int Group0Ticks = 0x124;
	private const int Group0Keys = 0x128;
	private const int StaticIndices = 0x138;
	private const int StaticTicks = 0x13C;
	private const int StaticKeys = 0x140;
	private const int AnimatedIndices = 0x148;
	private const int AnimatedTicks = 0x14C;
	private const int AnimatedKeys = 0x154;
	private const int Toggles = 0x16C;
	private const int FrameTracks = 0x174;
	private const int FrameKeys = 0x17C;
	private const int Records = 0x188;
	private const int Trailer = 0x1C8;

	private static uint Pack( int x, int y, int z ) => (uint)(x & 1023) | (uint)(y & 1023) << 10 | (uint)(z & 1023) << 20;

	private static byte[] CreateAnimation()
	{
		var data = new byte[Trailer + 72];
		W32( data, 0, ModelFile.Magic );
		W32( data, 4, 221 );
		W32( data, 8, 203 );
		Encoding.ASCII.GetBytes( "synthv1.MD2" ).CopyTo( data, 0x18 );
		W16( data, 0x42, 2 );
		W32( data, 0x98, Trailer );

		// Block: flags 1 | static 2, three groups, offset (1, 2, 3), scale (0.5, 0.25, 2).
		W16( data, Block, 3 );
		W16( data, Block + 2, 3 );
		W32( data, Block + 12, Groups );
		WF( data, Block + 20, 1, 2, 3 );
		WF( data, Block + 32, 0.5f, 0.25f, 2 );
		WriteGroup( data, 0, 2, 2, Group0Indices, Group0Ticks, Group0Keys );
		WriteGroup( data, 1, 1, 2, StaticIndices, StaticTicks, StaticKeys );
		WriteGroup( data, 2, 3, 2, AnimatedIndices, AnimatedTicks, AnimatedKeys );

		// Group 0: virtual vertices 4 and 5 (past a four-position mesh), lower and upper corner.
		W16( data, Group0Indices, 4 );
		W16( data, Group0Indices + 2, 5 );
		W16( data, Group0Ticks + 2, 10 );
		W32( data, Group0Keys, Pack( -4, -8, -1 ) );
		W32( data, Group0Keys + 4, Pack( 4, 8, 1 ) );
		W32( data, Group0Keys + 8, Pack( -6, -8, -2 ) );
		W32( data, Group0Keys + 12, Pack( 2, 12, 3 ) );

		// Static group: one key; bits 30-31 are discarded, ±512 limits.
		W16( data, StaticIndices, 0 );
		W16( data, StaticIndices + 2, 1 );
		W32( data, StaticKeys, 0xC0000000 | Pack( 511, 0, 0 ) );
		W32( data, StaticKeys + 4, Pack( -512, 1, -1 ) );

		// Animated group: vertices 2 and 3, ticks 0/4/10, key-major packed words.
		W16( data, AnimatedIndices, 2 );
		W16( data, AnimatedIndices + 2, 3 );
		W16( data, AnimatedTicks + 2, 4 );
		W16( data, AnimatedTicks + 4, 10 );
		uint[] keys = { Pack( 0, 0, 0 ), Pack( 2, 4, 1 ), Pack( 8, 0, 0 ), Pack( 2, 4, 1 ), Pack( 8, 8, 2 ), Pack( -2, -4, -1 ) };
		for ( var index = 0; index < keys.Length; index++ )
			W32( data, AnimatedKeys + index * 4, keys[index] );

		// Toggles 0, +3, -7: set from tick 0, cleared from 3, set again from 7.
		W16( data, Toggles + 2, 3 );
		BinaryPrimitives.WriteInt16LittleEndian( data.AsSpan( Toggles + 4 ), -7 );

		// One texture-frame track on slot 1: (2 → 3), (5 → 0), (5 → 1).
		W16( data, FrameTracks, 1 );
		W16( data, FrameTracks + 2, 3 );
		W32( data, FrameTracks + 4, FrameKeys );
		ushort[] frames = { 2, 3, 5, 0, 5, 1 };
		for ( var index = 0; index < frames.Length; index++ )
			W16( data, FrameKeys + index * 2, frames[index] );

		W32( data, Records + 4, 0x21000 );
		W32( data, Records + 12, 10 );
		W32( data, Records + 20, 1 | (3 << 16) );
		W32( data, Records + 40, Block );
		W32( data, Records + 48, Toggles );

		W32( data, Trailer, 2 );
		W32( data, Trailer + 8, 10 );
		W32( data, Trailer + 16, 1 << 16 );
		W32( data, Trailer + 24, 1 );
		W32( data, Trailer + 44, Records );
		W32( data, Trailer + 48, FrameTracks );
		return data;
	}

	private static void WriteGroup( byte[] data, int group, ushort keys, ushort vertices, int indices, int ticks, int packed )
	{
		var entry = Groups + group * 20;
		W16( data, entry, keys );
		W16( data, entry + 2, vertices );
		W32( data, entry + 4, indices );
		W32( data, entry + 8, ticks );
		W32( data, entry + 12, packed );
	}

	private static void W16( byte[] data, int offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( offset ), value );
	private static void W32( byte[] data, int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset ), value );
	private static void W32( byte[] data, int offset, int value ) => W32( data, offset, (uint)value );

	private static void WF( byte[] data, int offset, params float[] values )
	{
		for ( var index = 0; index < values.Length; index++ )
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( offset + index * 4 ), values[index] );
	}

	private static ModelAnimation Read( byte[] data ) => new ModelFile( new MemoryStream( data ) ).Clip!;
	private static void Rejects( byte[] data ) => Assert.ThrowsException<InvalidDataException>( () => Read( data ) );

	private static NVector3[] Mesh() => Enumerable.Repeat( new NVector3( 10, 10, 10 ), 4 ).ToArray();

	[TestMethod]
	public void DecodesQuantisedBlockLayout()
	{
		var track = Read( CreateAnimation() ).Tracks[0];
		Assert.AreEqual( 0u, track.UnsupportedFlags );
		Assert.IsTrue( track.HasUndecodedPayload, "vertex/toggle records are not rigid-only" );
		var vertex = track.VertexAnimation!;
		Assert.AreEqual( (ushort)3, vertex.Flags );
		Assert.IsTrue( vertex.HasStaticGroup );
		Assert.AreEqual( new NVector3( 1, 2, 3 ), vertex.Offset );
		Assert.AreEqual( new NVector3( 0.5f, 0.25f, 2 ), vertex.Scale );
		Assert.AreEqual( 3, vertex.Groups.Count );
		CollectionAssert.AreEqual( new ushort[] { 4, 5 }, vertex.BoundsGroup.VertexIndices.ToArray() );
		CollectionAssert.AreEqual( new ushort[] { 0 }, vertex.StaticGroup!.Ticks.ToArray() );
		CollectionAssert.AreEqual( new ushort[] { 0, 4, 10 }, vertex.AnimatedGroups.Single().Ticks.ToArray() );
		Assert.AreEqual( Pack( 8, 0, 0 ), vertex.AnimatedGroups.Single().PackedKey( 1, 0 ), "keys are key-major" );
		CollectionAssert.AreEqual( new uint[] { 0, 0, 0, 0 }, vertex.UnreadFields.ToArray() );
	}

	[TestMethod]
	public void UnpacksSignedTenBitFieldsAndDequantisesPerAxis()
	{
		Assert.AreEqual( (511, 0, 0), ModelVertexAnimation.Unpack( 0xC0000000 | Pack( 511, 0, 0 ) ) );
		Assert.AreEqual( (-512, 1, -1), ModelVertexAnimation.Unpack( Pack( -512, 1, -1 ) ) );
		Assert.AreEqual( (-1, -1, -1), ModelVertexAnimation.Unpack( 0x3FFFFFFF ) );
		var vertex = Read( CreateAnimation() ).Tracks[0].VertexAnimation!;
		Assert.AreEqual( new NVector3( 256.5f, 2, 3 ), vertex.Dequantise( Pack( 511, 0, 0 ) ) );
		Assert.AreEqual( new NVector3( -255, 2.25f, 1 ), vertex.Dequantise( Pack( -512, 1, -1 ) ) );
	}

	[TestMethod]
	public void GroupZeroGivesPaddedLowerAndUpperVectors()
	{
		var vertex = Read( CreateAnimation() ).Tracks[0].VertexAnimation!;
		// Key 0 lower (-1, 0, 1) / upper (3, 4, 5); key 1 lower (-2, 0, -1) / upper (2, 5, 9).
		var (lower, upper) = vertex.SampleBounds( 5 );
		Assert.AreEqual( new NVector3( -2.25f, -0.5f, -2.25f ), lower );
		Assert.AreEqual( new NVector3( 3.25f, 5, 9.25f ), upper );
		(lower, upper) = vertex.SampleBounds( 0 );
		Assert.AreEqual( new NVector3( -1.75f, -0.5f, -1.25f ), lower );
		Assert.AreEqual( new NVector3( 3.75f, 4.5f, 7.25f ), upper );
	}

	[TestMethod]
	public void StaticGroupUsesKeyZeroInSetOrAddMode()
	{
		var vertex = Read( CreateAnimation() ).Tracks[0].VertexAnimation!;
		var positions = Mesh();
		vertex.ApplyStaticGroup( positions, add: false );
		Assert.AreEqual( new NVector3( 256.5f, 2, 3 ), positions[0] );
		Assert.AreEqual( new NVector3( -255, 2.25f, 1 ), positions[1] );
		Assert.AreEqual( new NVector3( 10, 10, 10 ), positions[2] );
		positions = Mesh();
		vertex.ApplyStaticGroup( positions, add: true );
		Assert.AreEqual( new NVector3( 266.5f, 12, 13 ), positions[0] );
	}

	[TestMethod]
	public void AnimatedGroupsInterpolateWithForwardCursor()
	{
		var vertex = Read( CreateAnimation() ).Tracks[0].VertexAnimation!;
		var positions = Mesh();
		vertex.ApplyAnimatedGroups( 2, positions, add: false );
		Assert.AreEqual( new NVector3( 3, 2, 3 ), positions[2] );
		Assert.AreEqual( new NVector3( 2, 3, 5 ), positions[3] );
		Assert.AreEqual( new NVector3( 10, 10, 10 ), positions[0], "static and bounds groups are not applied here" );

		// At a key tick the cursor stays on the earlier segment (fraction 1).
		Assert.AreEqual( 0, vertex.AnimatedGroups.Single().FindKey( 4, out var fraction ) );
		Assert.AreEqual( 1f, fraction );
		vertex.ApplyAnimatedGroups( 4, positions, add: false );
		Assert.AreEqual( new NVector3( 5, 2, 3 ), positions[2] );
		vertex.ApplyAnimatedGroups( 7, positions, add: false );
		Assert.AreEqual( new NVector3( 5, 3, 5 ), positions[2] );
		Assert.AreEqual( new NVector3( 1, 2, 3 ), positions[3] );
		vertex.ApplyAnimatedGroups( 10, positions, add: false );
		Assert.AreEqual( new NVector3( 5, 4, 7 ), positions[2] );

		positions = Mesh();
		vertex.ApplyAnimatedGroups( 2, positions, add: true );
		Assert.AreEqual( new NVector3( 13, 12, 13 ), positions[2] );
	}

	[TestMethod]
	public void SamplingOutsideTheTracedDomainIsExplicit()
	{
		var vertex = Read( CreateAnimation() ).Tracks[0].VertexAnimation!;
		// The original cursor search reads past the tick array after the last key.
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => vertex.ApplyAnimatedGroups( 10.5f, Mesh(), false ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => vertex.SampleBounds( float.NaN ) );
		Assert.ThrowsException<ArgumentException>( () => vertex.ApplyAnimatedGroups( 2, new NVector3[3], false ) );
		Assert.ThrowsException<ArgumentException>( () => vertex.ApplyStaticGroup( new NVector3[1], false ) );
	}

	[TestMethod]
	public void NodeFlagTogglesUseTheLastEntryAtOrBeforeTheTick()
	{
		var track = Read( CreateAnimation() ).Tracks[0];
		CollectionAssert.AreEqual( new short[] { 0, 3, -7 }, track.NodeFlagToggles.ToArray() );
		Assert.AreEqual( true, track.SampleNodeFlag( 0 ) );
		Assert.AreEqual( true, track.SampleNodeFlag( 2.9f ) );
		Assert.AreEqual( false, track.SampleNodeFlag( 3 ) );
		Assert.AreEqual( false, track.SampleNodeFlag( 6.99f ) );
		Assert.AreEqual( true, track.SampleNodeFlag( 7 ) );

		var data = CreateAnimation();
		W16( data, Toggles, 2 );
		Assert.IsNull( Read( data ).Tracks[0].SampleNodeFlag( 1 ), "no entry at or before the tick leaves the bit unchanged" );
		BinaryPrimitives.WriteInt16LittleEndian( data.AsSpan( Toggles ), short.MinValue );
		Assert.IsNull( Read( data ).Tracks[0].SampleNodeFlag( 2 ), "-32768 never matches the unsigned compare" );
	}

	[TestMethod]
	public void TextureFrameTracksScanBackwardsAndAreGatedByTrailerFlag()
	{
		var clip = Read( CreateAnimation() );
		Assert.AreEqual( 2u, clip.TrailerFlags );
		Assert.IsTrue( clip.TextureFramesEnabled );
		var track = clip.TextureFrameTracks.Single();
		Assert.AreEqual( (ushort)1, track.Slot );
		Assert.IsNull( track.FrameAt( 1.9f ) );
		Assert.AreEqual( (ushort)3, track.FrameAt( 2 ) );
		Assert.AreEqual( (ushort)3, track.FrameAt( 4.99f ) );
		Assert.AreEqual( (ushort)1, track.FrameAt( 5 ), "the later of two equal ticks wins" );

		var data = CreateAnimation();
		W32( data, Trailer, 0 );
		Assert.IsFalse( Read( data ).TextureFramesEnabled );
	}

	[TestMethod]
	public void TwelveByteVariantAndUnknownBlocksStayUnsupported()
	{
		var data = CreateAnimation();
		W32( data, Records + 4, 0x5000 );
		W32( data, Records + 20, 1 );
		var track = Read( data ).Tracks[0];
		Assert.IsNull( track.VertexAnimation );
		Assert.AreEqual( 0x5000u, track.UnsupportedFlags );

		data = CreateAnimation();
		W32( data, Records + 4, 0x31000 );
		W32( data, Records + 44, Block );
		Assert.AreEqual( 0x10000u, Read( data ).Tracks[0].UnsupportedFlags );
	}

	[TestMethod]
	public void RejectsMalformedVertexBlocksTogglesAndFrameTracks()
	{
		var data = CreateAnimation();
		W32( data, Records + 40, 0 );
		Rejects( data ); // flag 0x1000 without a block
		data = CreateAnimation();
		W16( data, Block + 2, 0 );
		Rejects( data ); // no bounds group
		data = CreateAnimation();
		W16( data, Block + 2, 1 );
		Rejects( data ); // static flag without a static group
		data = CreateAnimation();
		W16( data, Groups + 2, 1 );
		Rejects( data ); // group 0 needs two vertices
		data = CreateAnimation();
		W16( data, Groups + 40, 1 );
		Rejects( data ); // animated group needs two keys
		data = CreateAnimation();
		W16( data, AnimatedTicks + 2, 0 );
		Rejects( data ); // ticks must increase
		data = CreateAnimation();
		W16( data, Groups + 20, 0 );
		Rejects( data ); // static group needs a key
		data = CreateAnimation();
		W32( data, Groups + 52, Trailer - 8 );
		Rejects( data ); // packed keys run into the trailer
		data = CreateAnimation();
		WF( data, Block + 32, float.NaN );
		Rejects( data ); // scale not finite
		data = CreateAnimation();
		W32( data, Records + 48, 0 );
		Rejects( data ); // toggle count without a list
		data = CreateAnimation();
		W32( data, Trailer + 48, 0 );
		Rejects( data ); // frame-track count without a table
		data = CreateAnimation();
		W32( data, FrameTracks + 4, Trailer - 4 );
		Rejects( data ); // frame keys run into the trailer
	}
}

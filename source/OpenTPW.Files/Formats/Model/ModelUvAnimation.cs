using System.Buffers.Binary;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Per-corner texture-coordinate animation of record flag 0x10000 (record +44), read from the Feral Mac
/// build (docs/reverse/ANIM-plan.md §2). 20-byte block: <c>u32 corners, ptr → corners × (u16 first,
/// u16 count), u32 total keys, ptr → u16 ticks[total], ptr → (f32 u, f32 v)[total]</c>. Corner <c>i</c>
/// owns keys <c>first … first + count</c>; its (u, v) replaces corner <c>i</c> of the bound mesh's
/// texture coordinates (<see cref="ModelFile.Mesh.TexCoords"/>, the stored U×4/V×4 blocks at +104).
/// </summary>
public sealed class ModelUvAnimation
{
	public const int HeaderBytes = 20;

	private readonly ushort[] firsts;
	private readonly ushort[] counts;
	private readonly ushort[] ticks;
	private readonly NVector2[] keys;

	internal ModelUvAnimation( ushort[] firsts, ushort[] counts, ushort[] ticks, NVector2[] keys )
	{
		this.firsts = firsts;
		this.counts = counts;
		this.ticks = ticks;
		this.keys = keys;
	}

	/// <summary>Number of corner tracks; the original binds it to the node mesh's corner count (+94).</summary>
	public int CornerCount => firsts.Length;
	public int KeyCount => ticks.Length;
	public IReadOnlyList<ushort> Ticks => ticks;
	public IReadOnlyList<NVector2> Keys => keys;
	public (int First, int Count) GetTrack( int corner ) => (firsts[corner], counts[corner]);

	/// <summary>
	/// Key search 0xa8304: with <c>tick = trunc(time)</c> the first key whose tick is greater than
	/// <c>tick</c> ends the segment (<c>k = i − 1</c>, fraction measured in the float time); if no key is
	/// later the last key holds (<c>k = count − 2</c>, fraction 1). Keys are relative to the corner's track.
	/// A time on a key tick starts the next segment at fraction 0, unlike the vertex cursor 0xa4a58.
	/// </summary>
	public int FindKey( int corner, float time, out float fraction )
	{
		if ( float.IsNaN( time ) )
			throw new ArgumentOutOfRangeException( nameof( time ) );
		var first = firsts[corner];
		var count = counts[corner];
		var tick = ModelAnimationTrack.TruncateTick( time );
		for ( var index = 0; index < count; index++ )
		{
			float value = ticks[first + index];
			if ( tick < ticks[first + index] )
			{
				// Decode requires each track to start at tick 0, so index 0 never ends a segment (the original would read before the track).
				float start = ticks[first + index - 1];
				fraction = (time - start) / (value - start);
				return index - 1;
			}
		}
		fraction = 1f;
		return count - 2;
	}

	/// <summary>0xa83e8 for one corner: <c>(1 − t)·a + t·b</c> per component, <c>t·b</c> rounded first (fmuls, then fmadds).</summary>
	public NVector2 Sample( int corner, float time )
	{
		var key = FindKey( corner, time, out var fraction );
		var first = firsts[corner];
		var from = keys[first + key];
		var to = keys[first + key + 1];
		var inverse = 1 - fraction;
		return new NVector2( MathF.FusedMultiplyAdd( inverse, from.X, fraction * to.X ), MathF.FusedMultiplyAdd( inverse, from.Y, fraction * to.Y ) );
	}

	/// <summary>Writes every corner's sample to <paramref name="texCoords"/> (set mode; the corners must match).</summary>
	public void Apply( float time, Span<NVector2> texCoords )
	{
		if ( texCoords.Length != CornerCount )
			throw new ArgumentException( $"The block animates {CornerCount} corners, the mesh has {texCoords.Length}.", nameof( texCoords ) );
		for ( var corner = 0; corner < firsts.Length; corner++ )
			texCoords[corner] = Sample( corner, time );
	}

	internal static ModelUvAnimation Decode( byte[] data, int block, Func<uint, long, string, int> pointer )
	{
		var corners = U32( data, block );
		var total = U32( data, block + 8 );
		// Every span is checked and charged before its array exists.
		var pairs = corners == 0 ? 0 : pointer( U32( data, block + 4 ), corners * 4L, "UV corner tracks" );
		var tickAt = total == 0 ? 0 : pointer( U32( data, block + 12 ), total * 2L, "UV ticks" );
		var keyAt = total == 0 ? 0 : pointer( U32( data, block + 16 ), total * 8L, "UV keys" );
		var ticks = new ushort[total];
		for ( var key = 0; key < ticks.Length; key++ )
			ticks[key] = U16( data, tickAt + key * 2 );
		var keys = new NVector2[total];
		for ( var key = 0; key < keys.Length; key++ )
		{
			keys[key] = new NVector2( F32( data, keyAt + key * 8 ), F32( data, keyAt + key * 8 + 4 ) );
			if ( !float.IsFinite( keys[key].X ) || !float.IsFinite( keys[key].Y ) )
				throw new InvalidDataException( "MD2 UV animation key is not finite." );
		}
		var firsts = new ushort[corners];
		var counts = new ushort[corners];
		for ( var corner = 0; corner < corners; corner++ )
		{
			firsts[corner] = U16( data, pairs + corner * 4 );
			counts[corner] = U16( data, pairs + corner * 4 + 2 );
			if ( counts[corner] < 2 || firsts[corner] + counts[corner] > total )
				throw new InvalidDataException( $"MD2 UV corner track {corner} has {counts[corner]} keys from {firsts[corner]} of {total}." );
			if ( ticks[firsts[corner]] != 0 )
				throw new InvalidDataException( $"MD2 UV corner track {corner} does not start at tick 0." );
			for ( var key = 1; key < counts[corner]; key++ )
			{
				if ( ticks[firsts[corner] + key] <= ticks[firsts[corner] + key - 1] )
					throw new InvalidDataException( $"MD2 UV corner track {corner} ticks do not increase." );
			}
		}
		return new ModelUvAnimation( firsts, counts, ticks, keys );
	}

	private static ushort U16( byte[] data, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset ) );
	private static uint U32( byte[] data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset ) );
	private static float F32( byte[] data, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( offset ) );
}

/// <summary>
/// The 12-byte block of record flag 0x4000 (record +40): <c>ptr → u32 index[count], u32 count,
/// ptr → count × (f32 x, y, z)</c>, one stored pose. The engine relocation (0x41ac8) and the bind
/// (0xa3e24) read it and set node bits 0x800/0x100, but no reader of the floats was found
/// (docs/reverse/ANIM-plan.md §3), so it is parsed and not played.
/// </summary>
public sealed class ModelVertexPose
{
	public const int HeaderBytes = 12;

	internal ModelVertexPose( uint[] indices, NVector3[] positions )
	{
		Indices = indices;
		Positions = positions;
	}

	public IReadOnlyList<uint> Indices { get; }
	public IReadOnlyList<NVector3> Positions { get; }

	/// <summary>Why the block does not fit a mesh of <paramref name="positionCount"/> positions, or null (the bind's count is the mesh's +88).</summary>
	public string? GetLimitation( int positionCount )
	{
		if ( Indices.Count != positionCount )
			return $"the 12-byte block lists {Indices.Count} positions, the mesh has {positionCount}";
		foreach ( var index in Indices )
		{
			if ( index >= positionCount )
				return $"12-byte block index {index} lies outside the {positionCount} mesh positions";
		}
		return null;
	}

	internal static ModelVertexPose Decode( byte[] data, int block, Func<uint, long, string, int> pointer )
	{
		var count = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block + 4 ) );
		var indexAt = count == 0 ? 0 : pointer( BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block ) ), count * 4L, "12-byte block indices" );
		var floatAt = count == 0 ? 0 : pointer( BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block + 8 ) ), count * 12L, "12-byte block positions" );
		var indices = new uint[count];
		var positions = new NVector3[count];
		for ( var index = 0; index < count; index++ )
		{
			indices[index] = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( indexAt + index * 4 ) );
			var at = floatAt + index * 12;
			positions[index] = new NVector3( BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at ) ), BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at + 4 ) ),
				BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at + 8 ) ) );
			if ( !float.IsFinite( positions[index].X ) || !float.IsFinite( positions[index].Y ) || !float.IsFinite( positions[index].Z ) )
				throw new InvalidDataException( "MD2 12-byte block position is not finite." );
		}
		return new ModelVertexPose( indices, positions );
	}
}

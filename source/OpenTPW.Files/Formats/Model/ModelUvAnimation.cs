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

	/// <summary>
	/// Adds every corner's sample to <paramref name="texCoords"/> (add mode, the PC UV pass 0x4745c0 for a relative model: the pylons' clips of the coaster mesh builder): the key segment is found as
	/// <see cref="FindKey"/> does and the sample is <c>a × (1 − t) + b × t</c> in single steps (no fused multiply-add), added to the corner's stored coordinate.
	/// </summary>
	// [BIN:TPW-EXE:0x4745c0 UV add pass] for every corner of the block the clip time at +0x20 is truncated (ftol 0x474623), the first key whose tick is above it ends the segment (0x47462d..0x474650, the key before it starts it, t = (time − tick before) / (tick − tick before), 0x474745..0x47477f) or the last segment holds with t = 1 (0x474652); u = u1 × t + u0 × (1 − t) and v the same (0x474663..0x4746a3), and the add path (the model's relative flag, argument 4) adds them to the corner's stored U and V (0x4746ae..0x4746c0: the coordinates live in groups of four, the four Us then the four Vs, 0x20 bytes per group); the set path 0x4746d6 stores them
	// [BIN:TPW-EXE:0x47219a UV pass gate] 0x471860 calls the UV pass for a record with flag 0x10000 (0x4721b7) in add mode always (0x4721aa: the model's relative flag at +0x30 bit 2), in set mode only while the global option 0x7b4980 bit 4 and the instance flag 0x4000 are clear (0x47219a..0x4721a8)
	public void AddTo( float time, Span<NVector2> texCoords )
	{
		if ( texCoords.Length != CornerCount )
			throw new ArgumentException( $"The block animates {CornerCount} corners, the mesh has {texCoords.Length}.", nameof( texCoords ) );
		for ( var corner = 0; corner < firsts.Length; corner++ )
		{
			var key = FindKey( corner, time, out var fraction );
			var first = firsts[corner];
			var from = keys[first + key];
			var to = keys[first + key + 1];
			var inverse = 1f - fraction;
			texCoords[corner] = new NVector2( texCoords[corner].X + (to.X * fraction + from.X * inverse), texCoords[corner].Y + (to.Y * fraction + from.Y * inverse) );
		}
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
/// ptr → floats</c>, read by the PC sampler. The float table holds one pose of <c>count</c> positions
/// (f32 x, y, z) per tick of the record, <c>duration + 1</c> poses in all (the 30 corpus blocks end exactly
/// where the next table starts). At clip time <c>t</c> the sampler takes pose <c>trunc(t)</c> and writes vertex <c>i</c> from entry
/// <c>index[i]</c> of it; the original's blend of two poses multiplies one and the same address by <c>t</c> and <c>1 − t</c>,
/// so the result is the stored position without blending.
/// </summary>
// [BIN:TPW-EXE:0x004718F7 12-byte vertex block] in the sampler 0x471860, record flags 0x1000 with 0x4000 take frame = ftol(clip time at +0x20) and base = count · frame (imul at 0x471922); vertex i reads entry j = (index pointer ? index[i] : i) at floats[(base + j) · 12 bytes]; in set mode (model header flag 4 clear, 0x471aa1) the vertex is replaced, in add mode (0x471943) the position is added; the "interpolation" multiplies the same address (0x471ac5/0x471ad2) by t and 1 − t
public sealed class ModelVertexPose
{
	public const int HeaderBytes = 12;

	internal ModelVertexPose( uint[] indices, NVector3[] positions, int frames )
	{
		Indices = indices;
		Positions = positions;
		FrameCount = frames;
	}

	/// <summary>Per vertex of the node: the entry of a pose that supplies it (record +40 index table).</summary>
	public IReadOnlyList<uint> Indices { get; }
	/// <summary>All poses one after another: <see cref="FrameCount"/> × <see cref="Count"/> positions.</summary>
	public IReadOnlyList<NVector3> Positions { get; }
	/// <summary>Number of poses stored (<c>duration + 1</c> in the corpus).</summary>
	public int FrameCount { get; }
	/// <summary>Positions per pose.</summary>
	public int Count => Indices.Count;

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

	/// <summary>
	/// The pose at <paramref name="time"/>: pose <c>trunc(time)</c> written to <paramref name="positions"/> (set mode, no blending).
	/// A time past the last stored pose holds the last one (an OpenTPW guard; the original would read beyond the table).
	/// </summary>
	public void ApplyPose( float time, Span<NVector3> positions )
	{
		var frame = (int)Math.Min( ModelAnimationTrack.TruncateTick( time ), (uint)(FrameCount - 1) );
		var count = Indices.Count;
		for ( var vertex = 0; vertex < count && vertex < positions.Length; vertex++ )
			positions[vertex] = Positions[frame * count + (int)Indices[vertex]];
	}

	internal static ModelVertexPose Decode( byte[] data, int block, Func<uint, long, string, int> pointer, long frames )
	{
		var count = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block + 4 ) );
		var indexAt = count == 0 ? 0 : pointer( BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block ) ), count * 4L, "12-byte block indices" );
		var floatPointer = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block + 8 ) );
		var floatAt = 0;
		var stored = 1;
		if ( count > 0 )
		{
			// OpenTPW guard: a table that does not hold duration + 1 poses (not in the corpus) is read as far as it fits, at least one pose.
			try
			{
				floatAt = pointer( floatPointer, count * 12L * Math.Max( 1, frames ), "12-byte block positions" );
				stored = (int)Math.Max( 1, frames );
			}
			catch ( InvalidDataException ) when ( frames > 1 )
			{
				floatAt = pointer( floatPointer, count * 12L, "12-byte block positions" );
			}
		}
		var indices = new uint[count];
		for ( var index = 0; index < count; index++ )
			indices[index] = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( indexAt + index * 4 ) );
		var positions = new NVector3[count * stored];
		for ( var index = 0; index < positions.Length; index++ )
		{
			var at = floatAt + index * 12;
			positions[index] = new NVector3( BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at ) ), BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at + 4 ) ),
				BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at + 8 ) ) );
			if ( !float.IsFinite( positions[index].X ) || !float.IsFinite( positions[index].Y ) || !float.IsFinite( positions[index].Z ) )
				throw new InvalidDataException( "MD2 12-byte block position is not finite." );
		}
		return new ModelVertexPose( indices, positions, stored );
	}
}

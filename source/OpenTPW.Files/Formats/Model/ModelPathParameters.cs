using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// The path-parameter block of a flag-0x200 animation record (record +36): <c>u32 start tick, u32 count,
/// u32 reserved, ptr → f32[count]</c>, one value per tick. In all 71 corpus blocks the start tick and the
/// reserved word are 0, the count is the record duration + 1 and the table follows the 16-byte header.
/// <para>
/// [BIN:TPW-EXE:0x00471ECF path parameter] The PC sampler 0x471860 reads the block only when the record has
/// flag 0x200, no position keys (flag 0x1 takes precedence) and the node's parent is a type-0x8 path dummy: while the clip time lies in
/// <c>[start, start + count]</c>, 0x474EA0 interpolates the table at <c>time − start</c> (linearly between
/// neighbouring ticks, wrapping when the node's path is closed, across the 100 → 0 seam when two ticks are more
/// than 50 apart; <see cref="Sample"/>). The result plus 1000, modulo 100, times 0.01 is the position as a
/// fraction of the segments of the path the node's parent dummy selects (<see cref="ModelPath"/>; flag 0x400
/// also turns the node along it). So the values are percentages of the path (go-karts climb by 100 per lap).
/// </para>
/// </summary>
public sealed class ModelPathParameters
{
	public const int HeaderBytes = 16;

	internal ModelPathParameters( uint startTick, uint reserved, float[] values )
	{
		StartTick = startTick;
		Reserved = reserved;
		Values = values;
	}

	/// <summary>Clip tick of the first value (+0; 0 in the corpus).</summary>
	public uint StartTick { get; }
	/// <summary>Header word +8, not read by the sampler (0 in the corpus).</summary>
	public uint Reserved { get; }
	/// <summary>Path position per tick, in percent of the path (+12 pointer, +4 count).</summary>
	public IReadOnlyList<float> Values { get; }

	/// <summary>
	/// The path position at clip time <paramref name="time"/> (0x474EA0), or null outside <c>[start, start + count]</c>
	/// (the sampler then leaves the node where it is). With <c>i = trunc(local)</c> and fraction <c>f</c> (at
	/// <c>local = count</c> the last pair with f + 1), values i and i + 1 blend linearly; on a
	/// <paramref name="closedPath"/> i + 1 wraps to 0, on an open one the last value holds. Two values more than 50
	/// apart cross the 100 → 0 seam: the smaller one gains 100 and the blend is taken modulo 100.
	/// </summary>
	public float? Sample( float time, bool closedPath )
	{
		var count = Values.Count;
		if ( count == 0 || !float.IsFinite( time ) || time < StartTick || time > StartTick + (double)count )
			return null;
		var local = (double)(time - StartTick);
		var index = (int)Math.Truncate( local );
		var fraction = (float)(local - index);
		if ( index == count )
		{
			fraction += 1;
			index--;
		}
		var a = Values[index];
		var b = closedPath ? Values[(index + 1) % count] : index < count - 1 ? Values[index + 1] : Values[index];
		if ( MathF.Abs( a - b ) <= 50 )
			return (1 - fraction) * a + fraction * b;
		if ( a < b )
			a += 100;
		else
			b += 100;
		return (float)(((1 - fraction) * a + fraction * b) % 100.0);
	}

	internal static ModelPathParameters Decode( byte[] data, int block, Func<uint, long, string, int> pointer )
	{
		var start = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block ) );
		var count = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block + 4 ) );
		var reserved = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block + 8 ) );
		// The table is bounded and charged before anything is allocated for it.
		var at = count == 0 ? 0 : pointer( BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( block + 12 ) ), count * 4L, "path parameters" );
		var values = new float[count];
		if ( count > 0 )
		{
			for ( var index = 0; index < values.Length; index++ )
			{
				values[index] = BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at + index * 4 ) );
				if ( !float.IsFinite( values[index] ) )
					throw new InvalidDataException( "MD2 path parameter is not finite." );
			}
		}
		return new ModelPathParameters( start, reserved, values );
	}
}

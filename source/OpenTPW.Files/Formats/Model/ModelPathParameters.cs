using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// The path-parameter block of a flag-0x200 animation record (record +36): <c>u32 start tick, u32 count,
/// u32 reserved, ptr → f32[count]</c>, one value per tick. In all 71 corpus blocks the start tick and the
/// reserved word are 0, the count is the record duration + 1 and the table follows the 16-byte header.
/// <para>
/// [BIN:TPW-EXE:0x00471ECF path parameter] The PC sampler 0x471860 reads the block only when the record has
/// flag 0x200 and the bound node has bit 0x8 in its first byte: while the clip time lies in
/// <c>[start, start + count]</c>, 0x474EA0 interpolates the table at <c>time − start</c> (linearly between
/// neighbouring ticks, wrapping when the node's path is closed; a jump of more than 50 between two ticks is
/// not blended). The result plus 1000, modulo 100, times 0.01 is the position along the node's path as a
/// fraction of its segments (path table at model instance +0xAC, 16-byte entries indexed by node +0x52;
/// flag 0x400 also takes the path's orientation). So the values are percentages of the path (go-karts climb
/// by 100 per lap). The path geometry itself is not decoded, so these values are read but not played.
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

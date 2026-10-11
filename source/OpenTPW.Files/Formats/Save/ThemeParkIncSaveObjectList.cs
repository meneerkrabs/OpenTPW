using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Reads the observed Theme Park Inc SYSG object header. Its leading u8 is followed by an
/// additional u32 before the seven fields shared with Theme Park World saves; rotation is at +43.
/// The additional word and variable-length record body remain opaque.
/// </summary>
public static class ThemeParkIncSaveObjectList
{
	public const int RecordHeaderBytes = 45;

	public static IReadOnlyList<SaveObject> Parse( ReadOnlySpan<byte> payload, SavePayloadLayout layout, int countX, int countY )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		var section = layout[SaveBlock.RideSystem];
		var data = payload.Slice( section.Offset, section.Length );
		var result = new List<SaveObject>();
		var lastIndex = 0;
		for ( var offset = 0; offset <= data.Length - RecordHeaderBytes; offset++ )
		{
			if ( data[offset] != 1 )
				continue;
			var id = U32( data, offset + 5 );
			var x = U32( data, offset + 9 );
			var y = U32( data, offset + 13 );
			var width = U32( data, offset + 17 );
			var height = U32( data, offset + 21 );
			var kind = U32( data, offset + 25 );
			var index = U32( data, offset + 29 );
			var rotation = BinaryPrimitives.ReadUInt16LittleEndian( data[(offset + 43)..] );
			if ( id == 0 || id > ushort.MaxValue || x >= (uint)countX || y >= (uint)countY
				|| width == 0 || width > SaveObjectList.MaximumFootprint || height == 0 || height > SaveObjectList.MaximumFootprint
				|| (kind != SaveObject.FixedItemKind && kind != SaveObject.PlacedObjectKind)
				|| index <= (uint)lastIndex || index > ushort.MaxValue || rotation % 90 != 0 || rotation >= 360 )
				continue;
			result.Add( new SaveObject( section.Offset + offset, (int)id, (int)x, (int)y,
				(int)width, (int)height, (int)kind, (int)index, rotation ) );
			lastIndex = (int)index;
			offset += RecordHeaderBytes - 1;
		}
		return result.AsReadOnly();
	}

	private static uint U32( ReadOnlySpan<byte> data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data[offset..] );
}

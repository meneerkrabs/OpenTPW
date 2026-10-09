using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// A placed-object record from the save's <c>SYSG</c> section (see docs/TPWS-PAYLOAD.md):
/// <c>u8 1</c>, then little-endian <c>u32</c> Info.Id, X, Y, width, height, kind and index, and a
/// <c>u16</c> rotation in degrees 38 bytes after the Info.Id. The rest of each record (variable
/// length) is not decoded. <see cref="InfoId"/> matches the <c>Info.Id</c> of the object's
/// <c>.sam</c> in the level archives.
/// </summary>
public readonly record struct SaveObject( int Offset, int InfoId, int X, int Y, int Width, int Height, int Kind, int Index, int Rotation )
{
	/// <summary>Kind of the gates, traffic lights and bus: fixed items placed from MapInfo.FixedItemOrigin.</summary>
	public const int FixedItemKind = 865;
	/// <summary>Kind of every buildable ride, shop, sideshow and feature in Easymode.</summary>
	public const int PlacedObjectKind = 815;

	public bool IsFixedItem => Kind == FixedItemKind;

	/// <summary>
	/// Footprint cells for the cases verified against the save cell grid: rotation 0 covers
	/// [X, X+Width) × [Y, Y+Height); rotation 90 of a square covers [X, X+Width) × (Y-Height, Y];
	/// any rotation of a 1×1 object covers its cell. Other combinations are unverified and return false.
	/// </summary>
	public bool TryGetFootprint( out int minX, out int minY, out int maxX, out int maxY )
	{
		minX = X;
		maxX = X + Width - 1;
		if ( Rotation == 0 || (Width == 1 && Height == 1) )
		{
			minY = Y;
			maxY = Y + Height - 1;
			return true;
		}
		if ( Rotation == 90 && Width == Height )
		{
			minY = Y - Height + 1;
			maxY = Y;
			return true;
		}
		minY = maxY = 0;
		return false;
	}
}

/// <summary>
/// Signature scan of the <c>SYSG</c> section for <see cref="SaveObject"/> records. A candidate
/// must have the leading byte 1, coordinates inside the grid, a 1–16 cell footprint, a known kind
/// (815 or 865), a rotation of 0/90/180/270 and an index greater than the previous record's.
/// Callers should cross-check footprints against <see cref="SaveCellGrid"/> occupancy.
/// </summary>
public static class SaveObjectList
{
	public const string SectionTag = "SYSG";
	public const int RecordHeaderBytes = 41;
	public const int MaximumFootprint = 16;

	public static IReadOnlyList<SaveObject> Parse( ReadOnlySpan<byte> payload, SavePayloadLayout layout, int countX, int countY )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		var section = layout.Sections.Single( candidate => candidate.Tag == SectionTag );
		var data = payload.Slice( section.Offset + SavePayloadLayout.MarkerBytes, section.Length - SavePayloadLayout.MarkerBytes );
		var result = new List<SaveObject>();
		var lastIndex = 0;
		for ( var offset = 0; offset <= data.Length - RecordHeaderBytes; offset++ )
		{
			if ( data[offset] != 1 )
				continue;
			var id = U32( data, offset + 1 );
			var x = U32( data, offset + 5 );
			var y = U32( data, offset + 9 );
			var width = U32( data, offset + 13 );
			var height = U32( data, offset + 17 );
			var kind = U32( data, offset + 21 );
			var index = U32( data, offset + 25 );
			var rotation = BinaryPrimitives.ReadUInt16LittleEndian( data[(offset + 39)..] );
			if ( id == 0 || id > ushort.MaxValue || x >= (uint)countX || y >= (uint)countY
				|| width == 0 || width > MaximumFootprint || height == 0 || height > MaximumFootprint
				|| (kind != SaveObject.FixedItemKind && kind != SaveObject.PlacedObjectKind)
				|| index <= (uint)lastIndex || index > ushort.MaxValue || rotation % 90 != 0 || rotation >= 360 )
				continue;
			result.Add( new SaveObject( section.Offset + SavePayloadLayout.MarkerBytes + offset, (int)id, (int)x, (int)y,
				(int)width, (int)height, (int)kind, (int)index, rotation ) );
			lastIndex = (int)index;
			offset += RecordHeaderBytes - 1;
		}
		return result.AsReadOnly();
	}

	private static uint U32( ReadOnlySpan<byte> data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data[offset..] );
}

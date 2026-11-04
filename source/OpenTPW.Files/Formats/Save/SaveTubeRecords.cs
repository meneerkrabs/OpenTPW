using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// One message tag of the UI block: the tube's saver writes one 0x21C-byte record per occupied slot, and the loader reads them back through the tube's add routine.
/// </summary>
/// <param name="Tag">The tag id (record +0).</param>
/// <param name="Text">The tag's text, up to the first zero character of the 256-wide-character buffer at +4 (the buffer holds stack leftovers after it).</param>
/// <param name="Lifetime">The tag's full lifetime in milliseconds (+0x204), not the time left; 0 keeps the tag until it is deleted.</param>
/// <param name="Icon">The icon class word (+0x208).</param>
/// <param name="FirstAction">The first action id (+0x20C).</param>
/// <param name="SecondAction">The second action id (+0x210); 0x1B makes the tag an offer.</param>
/// <param name="ContextShort">The context word (+0x214).</param>
/// <param name="ContextInt">The context dword (+0x218).</param>
// [BIN:TPW-EXE:0x48EE50 tube saver] the count of occupied slots, then per slot a 0x21C-byte record: the tag (the window's property 1), the text copied from the slot's buffer (a 256-wide-character field with the tag in front, zero-terminated by the copy only),
// the lifetime (property 7), the icon word (4), the two action ids (5, 6), the context word (3) and the context dword (8); the loader 0x48EF70 hands each record to the add routine 0x48EB20, the word at record +0x24 of its own copy staying 0
public readonly record struct SaveTubeRecord( int Tag, string Text, int Lifetime, short Icon, int FirstAction, int SecondAction, short ContextShort, int ContextInt )
{
	public const int Bytes = SaveBlockWidths.UiRecordBytes;
	public const int TextCharacters = 256;
	public const int TextOffset = 4;
	public const int LifetimeOffset = 0x204;

	/// <summary>Reads the <paramref name="count"/> records of a UI block's bytes (after the count); a text that does not end in its field is cut at 255 characters, as the add routine cuts it.</summary>
	public static IReadOnlyList<SaveTubeRecord> Read( ReadOnlySpan<byte> records, int count )
	{
		if ( count < 0 || (long)count * Bytes != records.Length )
			throw new InvalidDataException( $"The UI block holds {records.Length} bytes for {count} records of {Bytes}." );
		var result = new List<SaveTubeRecord>( count );
		for ( var index = 0; index < count; index++ )
		{
			var record = records.Slice( index * Bytes, Bytes );
			var length = 0;
			while ( length < TextCharacters - 1 && BinaryPrimitives.ReadUInt16LittleEndian( record.Slice( TextOffset + length * 2, 2 ) ) != 0 )
				length++;
			var text = new char[length];
			for ( var character = 0; character < length; character++ )
				text[character] = (char)BinaryPrimitives.ReadUInt16LittleEndian( record.Slice( TextOffset + character * 2, 2 ) );
			result.Add( new SaveTubeRecord( BinaryPrimitives.ReadInt32LittleEndian( record ), new string( text ), BinaryPrimitives.ReadInt32LittleEndian( record.Slice( LifetimeOffset, 4 ) ),
				BinaryPrimitives.ReadInt16LittleEndian( record.Slice( 0x208, 2 ) ), BinaryPrimitives.ReadInt32LittleEndian( record.Slice( 0x20C, 4 ) ), BinaryPrimitives.ReadInt32LittleEndian( record.Slice( 0x210, 4 ) ),
				BinaryPrimitives.ReadInt16LittleEndian( record.Slice( 0x214, 2 ) ), BinaryPrimitives.ReadInt32LittleEndian( record.Slice( 0x218, 4 ) ) ) );
		}
		return result;
	}
}

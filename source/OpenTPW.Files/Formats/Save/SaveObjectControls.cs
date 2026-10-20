namespace OpenTPW;

/// <summary>
/// One object control of the control manager's table (tp.exe <c>0x4D3AA0</c>, 32 bytes): the record <c>CControlManager::GetObjectControl</c> (<c>0x4D3D10</c>) finds by the id in its
/// first 16 bits. <see cref="Researched"/> is the byte at <c>+0x10</c>, which three places write: the research lab's init (<c>0x5031A0</c>, 1 for every object without a research cost),
/// the research step that reaches a project's cost (<c>0x504630</c>, 1 and <c>+0x14</c> = the project's upgrade level) and the research cheat (<c>0x503FF0</c>, 1 for every research item's object). The other words keep their native offsets.
/// </summary>
public readonly record struct SaveObjectControl( int Index, int Id, uint Word4, uint Word8, uint WordC, bool Researched, uint Word14, uint Word18, uint Word1C )
{
	public const int Bytes = 32;
}

/// <summary>
/// The control manager's object-control table at the start of the World block's control and macro-AI part, right after the 64 variable bytes (loader <c>0x5179C0</c> calls
/// <c>0x4D3AA0</c> first): 150 records of 32 bytes (<c>mObjectControls[i]</c>), <c>u32 mNumObjectControls</c> (the used records come first) and <c>u16 mPreviousSearchKey</c>.
/// </summary>
public sealed record SaveObjectControls( int Offset, IReadOnlyList<SaveObjectControl> Controls, int PreviousSearchKey )
{
	public const int Slots = 150;
	public const int Bytes = Slots * SaveObjectControl.Bytes + 4 + 2;

	/// <summary>The used controls by object id (the first record of an id wins, like the lookup 0x4D3D10).</summary>
	public SaveObjectControl? Find( int id ) => Controls.Where( control => control.Id == id ).Cast<SaveObjectControl?>().FirstOrDefault();

	/// <summary>Reads the table at <paramref name="offset"/>; null when fewer than <see cref="Bytes"/> bytes precede <paramref name="end"/>, the count is above 150 or a researched byte is not 0 or 1.</summary>
	internal static SaveObjectControls? TryRead( ReadOnlySpan<byte> payload, int offset, int end )
	{
		if ( offset < 0 || end > payload.Length || end - offset < Bytes )
			return null;
		var reader = new SaveFieldReader( payload[..end], offset + Slots * SaveObjectControl.Bytes, "object controls" );
		var count = reader.U32();
		if ( count > Slots )
			return null;
		var key = reader.U16();
		var controls = new List<SaveObjectControl>( (int)count );
		for ( var index = 0; index < count; index++ )
		{
			var record = new SaveFieldReader( payload[..end], offset + index * SaveObjectControl.Bytes, "object control" );
			var id = record.U32();
			var cost = record.U32();
			var word8 = record.U32();
			var wordC = record.U32();
			var researched = record.U8();
			record.Skip( 3 );
			if ( id == 0 || id > ushort.MaxValue || researched > 1 )
				return null;
			controls.Add( new SaveObjectControl( index, (int)id, cost, word8, wordC, researched != 0, record.U32(), record.U32(), record.U32() ) );
		}
		return new SaveObjectControls( offset, controls.AsReadOnly(), key );
	}
}

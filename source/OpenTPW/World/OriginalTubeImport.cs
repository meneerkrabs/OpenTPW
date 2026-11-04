namespace OpenTPW;

/// <summary>
/// The message tube of an original save (the UI block, tp.exe's loader <c>0x48EF70</c>) as the tube rows OpenTPW restores. The original hands each record to its add routine, so a row gets its full lifetime again and plays
/// the new-message cue; the rows come back in the order the saver walked the slots.
/// </summary>
internal static class OriginalTubeImport
{
	/// <summary>
	/// The rows of <paramref name="records"/> (at most the tube's ten): an offer for a record whose second action is the decline, a terminal row for one with a lifetime, a tag row otherwise. A record whose tag the table does not
	/// hold, whose text is blank or whose lifetime is out of range is left out and named in <paramref name="problems"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x48EF70 tube loader] count, then per record the tag, the text, the lifetime, the icon word, the two actions and the context word and dword; each goes through the add routine 0x48EB20 (a record the tube refuses, the eleventh on, is dropped)
	public static MessageTubeSaveData? Rows( IReadOnlyList<SaveTubeRecord> records, ICollection<string>? problems = null )
	{
		var rows = new List<MessageTubeRowSave>();
		foreach ( var record in records )
		{
			if ( rows.Count >= MessageTubeSaveData.MaximumRows )
			{
				problems?.Add( "more tag records than the tube's ten slots" );
				break;
			}
			if ( MessageTagTable.For( record.Tag ) is null )
			{
				problems?.Add( $"tag {record.Tag} is not in the tag table" );
				continue;
			}
			if ( string.IsNullOrWhiteSpace( record.Text ) || record.Lifetime is < 0 or > MessageTubeSaveData.MaximumLifetime )
			{
				problems?.Add( $"tag {record.Tag} has no text or a lifetime of {record.Lifetime} ms" );
				continue;
			}
			var kind = record.SecondAction == MessageTag.DeclineAction ? MessageTubeRowSave.Offer
				: record.Lifetime > 0 ? MessageTubeRowSave.Terminal : MessageTubeRowSave.Progress;
			var row = new MessageTubeRowSave( kind, record.Text, record.Tag, record.ContextShort, record.ContextInt, kind == MessageTubeRowSave.Terminal ? record.Lifetime : 0, false );
			if ( row.Problem() is { } problem )
			{
				problems?.Add( $"tag {record.Tag}: {problem}" );
				continue;
			}
			rows.Add( row );
		}
		return rows.Count == 0 ? null : new MessageTubeSaveData { Rows = rows };
	}
}

using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>
/// The park HUD's message tube as a park-resume save holds it: the occupied slots, oldest first (the lowest icon first). tp.exe saves its tag tube with the park (the tube's saver 0x48EE50: the count of
/// occupied slots, then per slot the tag id, the text, the lifetime and the tag's context words); the tube is presentation, so nothing here reaches a simulation or a hash.
/// </summary>
internal sealed record MessageTubeSaveData
{
	/// <summary>The most slots a tube holds (the original's configured count, 0x750610 = 10, read by the tube's add routine 0x48EB20).</summary>
	public const int MaximumRows = 10;
	public const int MaximumTextLength = 4096;
	public const int MaximumLifetime = 600_000;

	public required IReadOnlyList<MessageTubeRowSave> Rows { get; init; }

	/// <summary>The reason this tube cannot be restored, or null when it can.</summary>
	public string? Problem()
	{
		if ( Rows == null || Rows.Count > MaximumRows )
			return $"the message tube holds more than {MaximumRows} rows";
		foreach ( var row in Rows )
		{
			if ( row == null )
				return "a message tube row is missing";
			if ( row.Problem() is { } problem )
				return problem;
		}
		return null;
	}
}

/// <summary>One occupied tube slot.</summary>
/// <param name="Kind">0 a plain message, 1 a challenge offer (its Accept and Decline actions are rebuilt), 2 a challenge progress row, 3 a challenge terminal row with its 30 s lifetime.</param>
/// <param name="Text">The message text.</param>
/// <param name="Tag">The tag id of a challenge row (0 for the others).</param>
/// <param name="ContextShort">The short context word of a challenge row.</param>
/// <param name="ContextInt">The int context word of a challenge row.</param>
/// <param name="Lifetime">A terminal row's lifetime in unscaled milliseconds (0 for the others).</param>
/// <param name="Read">The terminal row was read: its timers are stopped (the loader 0x0048EF70 sends such a record through the selection routine 0x0048D9C0); an unread row restarts both timers from the full lifetime.</param>
[JsonConverter( typeof( MessageTubeRowSaveConverter ) )]
internal sealed record MessageTubeRowSave( int Kind, string Text, int Tag, short ContextShort, int ContextInt, int Lifetime, bool Read )
{
	public const int Message = 0, Offer = 1, Progress = 2, Terminal = 3;
	public string? Problem()
	{
		if ( Kind is < Message or > Terminal )
			return $"the message tube row kind {Kind} is unknown";
		if ( string.IsNullOrWhiteSpace( Text ) || Text.Length > MessageTubeSaveData.MaximumTextLength )
			return "a message tube row has no text or text longer than " + MessageTubeSaveData.MaximumTextLength + " characters";
		var challenge = Kind is Progress or Terminal;
		if ( !challenge && (Tag != 0 || ContextShort != 0 || ContextInt != 0) )
			return "a plain message tube row carries a challenge identity";
		if ( challenge && Tag <= 0 )
			return "a challenge tube row has no tag";
		if ( Kind == Terminal )
		{
			if ( Lifetime is <= 0 or > MessageTubeSaveData.MaximumLifetime )
				return "a terminal tube row's lifetime is out of range";
			return null;
		}
		return Lifetime != 0 || Read ? "a tube row that is no terminal carries timer state" : null;
	}
}

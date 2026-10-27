namespace OpenTPW;

/// <summary>
/// The park HUD's message tube as a park-resume save holds it: the occupied slots, oldest first (the lowest icon first). tp.exe saves its tag tube with the park (SaveTags 0x48EE50: the count of
/// occupied slots, then per slot the tag id, the text, the lifetime and the tag's context words); the tube is presentation, so nothing here reaches a simulation or a hash.
/// </summary>
internal sealed record MessageTubeSaveData
{
	/// <summary>The most slots a tube holds (the original's configured count, 0x750610 = 10, read by AddATag 0x48EB20).</summary>
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
			if ( row?.Problem() is { } problem )
				return problem;
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
/// <param name="ElapsedMs">How much of the lifetime had passed, in unscaled milliseconds (the original's tag timers, 0x80080 at the lifetime and 0x80081 5 s before it).</param>
/// <param name="Active">The terminal row's expiry timer still runs (it stops when the row is read).</param>
/// <param name="PreExpiryEnabled">The warning timer (5 s before the expiry) has not fired and was not stopped.</param>
/// <param name="PreExpiryReached">The warning timer has fired.</param>
internal sealed record MessageTubeRowSave( int Kind, string Text, int Tag, short ContextShort, int ContextInt, int Lifetime, long ElapsedMs, bool Active, bool PreExpiryEnabled, bool PreExpiryReached )
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
			if ( Lifetime is <= 0 or > MessageTubeSaveData.MaximumLifetime || ElapsedMs < 0 || ElapsedMs > Lifetime + MessageTubeSaveData.MaximumLifetime )
				return "a terminal tube row's lifetime or elapsed time is out of range";
			return null;
		}
		return Lifetime != 0 || ElapsedMs != 0 || Active || PreExpiryEnabled || PreExpiryReached ? "a tube row that is no terminal carries timer state" : null;
	}
}

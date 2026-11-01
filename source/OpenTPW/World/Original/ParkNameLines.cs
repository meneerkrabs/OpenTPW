namespace OpenTPW;

/// <summary>
/// The park's name as the original keeps it: the two sign lines of the park gate's placed object (thing +0x3C8 and +0x40A),
/// which the gate draws and the rename dialog edits as two fields. They are created with the gate from its OBJECT_NAMES row
/// (<see cref="Default"/>) and saved with the park.
/// </summary>
// [BIN:TPW-EXE:0x004DB6F9 placed object sign lines] a placed object copies line 1 (0x413020) and line 2 (0x4130B0) as 32 characters each from the first and the second OBJECT_NAMES text of its row in the table 0x7488D8; for the gate 1601 that is entries 85 and 86 ("LOST", "Kingdom")
// [BIN:TPW-EXE:0x004E2950 set sign line 1] the setters copy at most 0x21 characters into the 33-character line (line 2: 0x4E2A50 into +0x40A)
public sealed record ParkNameLines( string Line1, string Line2 )
{
	/// <summary>Characters a line holds (the 32-character copy at placement; the line buffer has one more for its terminator).</summary>
	public const int MaximumLength = BonusNames.SignLineCharacters;

	/// <summary>Characters a field of the rename dialog accepts.</summary>
	// [BIN:TPW-EXE:0x004990F0 park rename dialog] each edit field is bound to its buffer (0x7C2668, 0x7C2688) with a limit of 0xE (0x6662B7 stores it at +0x13C); a typed character is inserted only while the text is shorter than that limit (0x667833)
	public const int DialogFieldLength = 14;

	/// <summary>
	/// True for a line a save may hold: at most <see cref="MaximumLength"/> UTF-16 units, no control characters and no
	/// unpaired surrogate. The original's edit field refuses carriage return, tab, backspace and escape (0x667833) and the
	/// line ends at a NUL; OpenTPW's text field drops every control character, so no saved line can hold one.
	/// </summary>
	public static bool IsValidLine( string? line )
	{
		if ( line == null || line.Length > MaximumLength )
			return false;
		for ( var i = 0; i < line.Length; i++ )
		{
			var character = line[i];
			if ( char.IsControl( character ) )
				return false;
			if ( char.IsHighSurrogate( character ) )
			{
				if ( i + 1 >= line.Length || !char.IsLowSurrogate( line[i + 1] ) )
					return false;
				i++;
			}
			else if ( char.IsLowSurrogate( character ) )
				return false;
		}
		return true;
	}

	/// <summary>The park name an original save's gate holds: its two sign lines (both blank is a name too: the rename dialog's OK 0x4993E0 tests no emptiness), or null when they hold what a sign cannot (<see cref="IsValidLine"/>).</summary>
	public static ParkNameLines? FromSavedGate( string? line1, string? line2 ) =>
		IsValidLine( line1 ) && IsValidLine( line2 ) ? new ParkNameLines( line1!, line2! ) : null;

	/// <summary>Both lines valid (<see cref="IsValidLine"/>).</summary>
	public bool IsValid => IsValidLine( Line1 ) && IsValidLine( Line2 );

	/// <summary>
	/// The park's name on one line, as the ticket price window shows it: line 2 alone when line 1 is empty, line 1 without its
	/// trailing '-' joined to line 2 when line 1 ends in '-', line 1 alone when line 2 is empty, else both with a space.
	/// </summary>
	// [BIN:TPW-EXE:0x004E2780 thing name] the joined lines; the ticket price window 0x498D80 shows it on its control 0x4F3AF through 0x486280
	public string DisplayName
	{
		get
		{
			if ( Line1.Length == 0 )
				return Line2;
			if ( Line1.EndsWith( '-' ) )
				return Line1[..^1] + Line2;
			return Line2.Length == 0 ? Line1 : $"{Line1} {Line2}";
		}
	}

	/// <summary>The lines as the sign rebuild after loading a park draws them: line 1 through 0x4E2860, which drops the last dash of a trailing "--".</summary>
	// [BIN:TPW-EXE:0x00468500 sign rebuild] after a park is loaded (0x415140) each placed object of a type with flag 0x8000000 redraws its type's sign from line 1 through 0x4E2860 and line 2 through 0x4E2920 (0x4682B0)
	public (string Line1, string Line2) ReloadedSignLines => (BonusNames.StripTrailingDash( Line1 ), Line2);

	/// <summary>The fields the rename dialog opens with: line 1 through 0x4E2860 (the last dash of a trailing "--" dropped) and line 2, each cut to the field length.</summary>
	// [BIN:TPW-EXE:0x004990F0 park rename dialog] field 0x3943C is filled from 0x4E2860 and field 0x3943D from 0x4E2920; how a longer line is cut to the 14-character field is not read, OpenTPW keeps the first 14 characters
	public (string Line1, string Line2) DialogFields => (Cut( BonusNames.StripTrailingDash( Line1 ) ), Cut( Line2 ));

	private static string Cut( string line ) => line.Length <= DialogFieldLength ? line
		: line[..(char.IsHighSurrogate( line[DialogFieldLength - 1] ) ? DialogFieldLength - 1 : DialogFieldLength)];

	/// <summary>
	/// The lines a gate starts with: the first and the second OBJECT_NAMES text of its Info.Id row (index 1 is the empty
	/// text), each cut to <see cref="MaximumLength"/>; null when the id is not in the table or a text is missing.
	/// </summary>
	public static ParkNameLines? Default( int gateInfoId, IReadOnlyList<string> objectNames )
	{
		ArgumentNullException.ThrowIfNull( objectNames );
		if ( !ObjectNameTable.TryGet( gateInfoId, out var first, out var second ) || first < 0 || second < 0
			|| first >= objectNames.Count || second >= objectNames.Count )
			return null;
		var lines = new ParkNameLines( BonusNames.Truncate( objectNames[first] ), BonusNames.Truncate( objectNames[second] ) );
		return lines.IsValid ? lines : null;
	}
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// One record of the message tube's tag table: what a tag shows and how long its row lives.
/// </summary>
/// <param name="Id">The tag id (field +0).</param>
/// <param name="Replaces">The tag whose rows this tag replaces (+4): 0x17F is none, the tag's own id refreshes its rows in place, another id removes them.</param>
/// <param name="Icon">The icon class of the row's tube icon (+8).</param>
/// <param name="Text">The entry of <c>TAG_SYSTEM.str</c> the tag shows (+0xC).</param>
/// <param name="FirstAction">The action id of the panel's first button (+0x10), 0 for none.</param>
/// <param name="SecondAction">The action id of the second button (+0x14), 0 for none; 0x1B (the decline) makes the row an offer.</param>
/// <param name="Lifetime">The row's lifetime in milliseconds (+0x18): 30,000 for most tags, 90,000 for the offers, 0 keeps the row until it is deleted.</param>
/// <param name="Kind">The case of the caption formatter 0x509E10 that fills the text's parameter slots (+0x1C).</param>
internal readonly record struct MessageTag( int Id, int Replaces, int Icon, int Text, int FirstAction, int SecondAction, int Lifetime, int Kind )
{
	/// <summary>The id of no tag (0x17F): the tube's own record builder starts from it, and a tag that replaces it replaces nothing.</summary>
	public const int NoTag = 0x17F;

	/// <summary>The second action of a challenge offer (0x1B, the decline): such a row keeps its timers when it is read and declines when it is removed.</summary>
	public const int DeclineAction = 0x1B;

	/// <summary>The first action of a challenge offer (0x1A, the accept).</summary>
	public const int AcceptAction = 0x1A;

	public bool IsOffer => SecondAction == DeclineAction;
}

/// <summary>
/// The tag table of the message tube (<c>content/data/message-tags.toml</c>), copied for interoperability from the Windows executable by
/// <c>tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py</c>: every message the game posts to the tube names a tag, and the tag's record decides its text entry,
/// its panel actions and its lifetime.
/// </summary>
// [BIN:TPW-EXE:0x50AF40 tag lookup] the record at the id's own position of the table at 0x75ECA8 when its id matches, else the first record with the id; 0x17E and 0x17F have none
internal static class MessageTagTable
{
	public const string RelativePath = "content/data/message-tags.toml";
	private static readonly Regex Row = new( @"^\s*\{ tag = (\d+), replaces = (\d+), icon = (\d+), text = (\d+), a1 = (\d+), a2 = (\d+), life = (\d+), kind = (\d+) \},?\s*$", RegexOptions.CultureInvariant );
	private static IReadOnlyDictionary<int, MessageTag>? loaded;

	/// <summary>The table shipped with OpenTPW (loaded on first use; empty when the file is missing or damaged).</summary>
	public static IReadOnlyDictionary<int, MessageTag> Table => loaded ??= LoadShipped();

	/// <summary>The record of a tag, null for an id the table does not hold (and for 0x17E and 0x17F, which the original's lookup refuses).</summary>
	public static MessageTag? For( int tag ) => tag is MessageTag.NoTag or 0x17E ? null : Table.TryGetValue( tag, out var record ) ? record : null;

	private static IReadOnlyDictionary<int, MessageTag> LoadShipped()
	{
		var path = Path.Combine( AppContext.BaseDirectory, RelativePath );
		try
		{
			return Parse( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Message tags unavailable ({path}): {exception.Message}" );
			return new Dictionary<int, MessageTag>();
		}
	}

	public static IReadOnlyDictionary<int, MessageTag> Parse( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var table = new Dictionary<int, MessageTag>();
		var inArray = false;
		var closed = false;
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( !inArray && !closed && line.Trim() == "tags = [" )
			{
				inArray = true;
				continue;
			}
			if ( inArray && line.Trim() == "]" )
			{
				inArray = false;
				closed = true;
				continue;
			}
			var match = Row.Match( line );
			if ( !inArray || !match.Success )
				throw new InvalidDataException( $"Message tags line {lineNumber} is not a tag row." );
			int Value( int group ) => int.Parse( match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture );
			var record = new MessageTag( Value( 1 ), Value( 2 ), Value( 3 ), Value( 4 ), Value( 5 ), Value( 6 ), Value( 7 ), Value( 8 ) );
			if ( !table.TryAdd( record.Id, record ) )
				throw new InvalidDataException( $"Message tag {record.Id} appears twice." );
		}
		if ( !closed )
			throw new InvalidDataException( "Message tags have no closed 'tags' array." );
		return table;
	}
}

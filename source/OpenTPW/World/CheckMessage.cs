using System.Runtime.CompilerServices;

namespace OpenTPW;

/// <summary>
/// The message of a check that is formatted only when the check fails (<c>Require( condition, $"..." )</c> in a snapshot's validation): a check that holds evaluates none of the
/// message's holes and makes no string. A save's cell map holds thousands of records with about ten checks each, and formatting every message cost a browser's join more than the checks
/// themselves (docs/COOP.md, "Measurements"). The text of a failed check's message is the one the interpolated string would have made.
/// </summary>
[InterpolatedStringHandler]
internal ref struct CheckMessage
{
	private DefaultInterpolatedStringHandler text;

	public CheckMessage( int literalLength, int formattedCount, bool condition, out bool failed )
	{
		failed = !condition;
		text = failed ? new DefaultInterpolatedStringHandler( literalLength, formattedCount ) : default;
	}

	public void AppendLiteral( string value ) => text.AppendLiteral( value );

	public void AppendFormatted<T>( T value ) => text.AppendFormatted( value );

	public void AppendFormatted<T>( T value, string? format ) => text.AppendFormatted( value, format );

	public void AppendFormatted<T>( T value, int alignment ) => text.AppendFormatted( value, alignment );

	public void AppendFormatted<T>( T value, int alignment, string? format ) => text.AppendFormatted( value, alignment, format );

	public void AppendFormatted( string? value ) => text.AppendFormatted( value );

	public void AppendFormatted( ReadOnlySpan<char> value ) => text.AppendFormatted( value );

	/// <summary>The failed check's message.</summary>
	public string ToStringAndClear() => text.ToStringAndClear();
}

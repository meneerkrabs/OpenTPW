using System.Globalization;
using System.Text;

namespace OpenTPW.Hud;

/// <summary>Strict, keyed formatting of challenge TAG_SYSTEM templates using runtime localized names.</summary>
public static class ChallengeMessageText
{
	public const int MaximumLength = 255;
	private const int MaximumObjectNameLength = 63;

	/// <summary>
	/// Fills parameter IDs, not parameter positions. The staff resolver receives the localized plural entry (1, 3, 5, 7 or 9).
	/// Resolvers return null or empty for unavailable names. Only names actually used by the template are required.
	/// A missing or unsupported template/argument/name returns false and empty text, with no positional or generic fallback.
	/// </summary>
	public static bool TryFormat( StringFile? templates, ChallengePresentation payload, Func<int, string?> objectName,
		Func<int, string?> staffEntry, out string text )
	{
		text = "";
		if ( templates == null || !ChallengeMessageRecords.TryGet( payload.Type, payload.Kind, out var record )
			|| record.Entry < 0 || record.Entry >= templates.Templates.Length
			|| templates.Templates[record.Entry] is not { Parts: { Count: > 0 } } template )
			return false;

		var supplied = SuppliedArguments( record.Selector );
		if ( supplied == 0 )
			return false;
		var values = new string?[8];
		var builder = new StringBuilder( MaximumLength );
		foreach ( var part in template.Parts )
		{
			string? value;
			if ( part.ParameterType == -1 )
				value = part.Text;
			else
			{
				var id = part.ParameterType;
				if ( id is < 1 or > 7 || (supplied & (1 << id)) == 0 )
					return false;
				value = values[id] ??= Argument( id, record.Selector, payload, objectName, staffEntry );
				if ( string.IsNullOrEmpty( value ) || id >= 6 && string.IsNullOrWhiteSpace( value ) )
					return false;
			}
			if ( value == null )
				return false;
			// Keep validating the remaining parts even after the visible buffer is full.
			builder.Append( value, 0, Math.Min( value.Length, MaximumLength - builder.Length ) );
		}
		if ( builder.Length > 0 && char.IsHighSurrogate( builder[builder.Length - 1] ) )
			builder.Length--;
		text = builder.ToString();
		return text.Length > 0;
	}

	// [BIN:TPW-EXE:0x50a1d5 challenge formatting] selectors supply keyed argument records, which a template may reorder or omit.
	private static int SuppliedArguments( int selector ) => selector switch
	{
		9 or 11 => 0b00001110,
		10 or 12 or 13 or 14 => 0b01001110,
		15 => 0b11001100,
		16 or 18 => 0b00111010,
		17 or 19 or 20 or 21 => 0b01111010,
		22 => 0b11101000,
		>= 23 and <= 29 => 0b00001000,
		>= 30 and <= 36 => 0b00010010,
		_ => 0
	};

	private static string? Argument( int id, int selector, ChallengePresentation value, Func<int, string?> objectName, Func<int, string?> staffEntry ) => id switch
	{
		1 => Integer( value.TargetValue ),
		2 => Integer( value.TargetTime ),
		3 => Integer( value.Prize ),
		4 => Integer( value.RawValue ),
		5 => Integer( value.DaysLeft ),
		6 when selector is 12 or 13 or 19 or 20 => value.TargetStaffType is >= 0 and <= 4 ? staffEntry?.Invoke( value.TargetStaffType * 2 + 1 ) : null,
		6 => ObjectName( objectName?.Invoke( value.TargetObject ) ),
		7 => ObjectName( objectName?.Invoke( value.TargetObject2 ) ),
		_ => null
	};

	// [BIN:TPW-EXE:0x6add40 integer emitter] only the stored 32-bit magnitude is emitted as a signed decimal; the sign flag is not read.
	private static string Integer( int value ) => unchecked(value < 0 ? -value : value).ToString( CultureInfo.InvariantCulture );

	// [BIN:TPW-EXE:0x412fb0 object name] the challenge formatter supplies a 64-unit destination, including its terminator.
	private static string? ObjectName( string? name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;
		var length = Math.Min( name.Length, MaximumObjectNameLength );
		if ( length > 0 && char.IsHighSurrogate( name[length - 1] ) )
			length--;
		return name[..length];
	}
}

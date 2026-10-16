using System.Globalization;

namespace OpenTPW.Mods;

/// <summary>
/// A mod or extension version: <c>MAJOR.MINOR.PATCH</c> with an optional <c>-prerelease</c> tag (docs/MODS.md).
/// A version with a tag sorts before the same version without one; tags compare ordinally.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public readonly record struct ModVersion( int Major, int Minor, int Patch, string? Prerelease = null ) : IComparable<ModVersion>
{
	public const int MaximumLength = 64;

	public static bool TryParse( string? text, out ModVersion version )
	{
		version = default;
		if ( string.IsNullOrEmpty( text ) || text.Length > MaximumLength )
			return false;
		var dash = text.IndexOf( '-' );
		var core = dash < 0 ? text : text[..dash];
		string? prerelease = null;
		if ( dash >= 0 )
		{
			prerelease = text[(dash + 1)..];
			if ( prerelease.Length == 0 || !prerelease.All( c => char.IsAsciiLetterOrDigit( c ) || c is '.' or '-' ) )
				return false;
		}
		var parts = core.Split( '.' );
		if ( parts.Length != 3 )
			return false;
		var numbers = new int[3];
		for ( var index = 0; index < 3; index++ )
		{
			var part = parts[index];
			// no signs, no leading zeros (except a single 0) and no whitespace
			if ( part.Length == 0 || part.Length > 9 || !part.All( char.IsAsciiDigit ) || (part.Length > 1 && part[0] == '0') )
				return false;
			numbers[index] = int.Parse( part, NumberStyles.None, CultureInfo.InvariantCulture );
		}
		version = new ModVersion( numbers[0], numbers[1], numbers[2], prerelease );
		return true;
	}

	public static ModVersion Parse( string text ) =>
		TryParse( text, out var version ) ? version : throw new FormatException( $"'{text}' is not a version such as 1.0.0." );

	public int CompareTo( ModVersion other )
	{
		var result = Major.CompareTo( other.Major );
		if ( result == 0 ) result = Minor.CompareTo( other.Minor );
		if ( result == 0 ) result = Patch.CompareTo( other.Patch );
		if ( result != 0 )
			return result;
		if ( Prerelease == other.Prerelease )
			return 0;
		if ( Prerelease == null )
			return 1;
		if ( other.Prerelease == null )
			return -1;
		return string.CompareOrdinal( Prerelease, other.Prerelease );
	}

	public static bool operator <( ModVersion left, ModVersion right ) => left.CompareTo( right ) < 0;
	public static bool operator >( ModVersion left, ModVersion right ) => left.CompareTo( right ) > 0;
	public static bool operator <=( ModVersion left, ModVersion right ) => left.CompareTo( right ) <= 0;
	public static bool operator >=( ModVersion left, ModVersion right ) => left.CompareTo( right ) >= 0;

	public override string ToString() => Prerelease == null
		? string.Create( CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}" )
		: string.Create( CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{Prerelease}" );
}

/// <summary>
/// A version range: space-separated comparators that must all hold, such as <c>&gt;=0.9.0 &lt;0.10.0</c>.
/// Comparators are <c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&lt;</c> and <c>=</c>; a bare version means <c>=</c>,
/// and <c>*</c> alone matches every version.
/// </summary>
public sealed class ModVersionRange
{
	public const int MaximumLength = 128;
	private const int MaximumComparators = 8;

	private readonly (string Operator, ModVersion Version)[] comparators;

	private ModVersionRange( string text, (string, ModVersion)[] comparators )
	{
		Text = text;
		this.comparators = comparators;
	}

	public string Text { get; }

	public static ModVersionRange Any { get; } = new( "*", Array.Empty<(string, ModVersion)>() );

	public static bool TryParse( string? text, out ModVersionRange range )
	{
		range = Any;
		if ( string.IsNullOrWhiteSpace( text ) || text.Length > MaximumLength )
			return false;
		var trimmed = text.Trim();
		if ( trimmed == "*" )
			return true;
		var parts = trimmed.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length > MaximumComparators )
			return false;
		var parsed = new (string, ModVersion)[parts.Length];
		for ( var index = 0; index < parts.Length; index++ )
		{
			var part = parts[index];
			var op = part.StartsWith( ">=" ) ? ">=" : part.StartsWith( "<=" ) ? "<=" : part.StartsWith( '>' ) ? ">" : part.StartsWith( '<' ) ? "<" : part.StartsWith( '=' ) ? "=" : "";
			if ( !ModVersion.TryParse( part[op.Length..], out var version ) )
				return false;
			parsed[index] = (op.Length == 0 ? "=" : op, version);
		}
		range = new ModVersionRange( trimmed, parsed );
		return true;
	}

	public static ModVersionRange Parse( string text ) =>
		TryParse( text, out var range ) ? range : throw new FormatException( $"'{text}' is not a version range such as >=1.0.0 <2.0.0." );

	public bool Contains( ModVersion version ) => comparators.All( comparator => comparator.Operator switch
	{
		">=" => version >= comparator.Version,
		">" => version > comparator.Version,
		"<=" => version <= comparator.Version,
		"<" => version < comparator.Version,
		_ => version.CompareTo( comparator.Version ) == 0,
	} );

	public override string ToString() => Text;
}

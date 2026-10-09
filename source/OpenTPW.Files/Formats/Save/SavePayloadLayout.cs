using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// One marker-delimited region of a decoded TPWI/TPWS payload. <see cref="Tag"/> is the four
/// ASCII bytes exactly as stored; <see cref="Value"/> is the same bytes read as a little-endian
/// uint32. <see cref="Length"/> is the distance to the next known marker (or end of payload),
/// including the 4-byte marker itself. It is not a length field read from the file.
/// </summary>
public readonly record struct SavePayloadSection( string Tag, uint Value, int Offset, int Length );

/// <summary>
/// Bounded, read-only marker inventory for a decoded save payload (see docs/TPWS-PAYLOAD.md).
/// The payload has no observed length-prefixed chunk framing: the only verified structure is a
/// set of 17 four-byte section markers that also appear, contiguously and in a fixed table, in
/// the original program's static data. Section contents stay opaque.
/// </summary>
public sealed class SavePayloadLayout
{
	public const int MaximumPayloadBytes = SaveReader.MaximumDecodedBytes;
	public const int MarkerBytes = 4;

	/// <summary>The 17 marker byte sequences, in the order of the original static table.</summary>
	public static IReadOnlyList<string> KnownSectionTags { get; } = Array.AsReadOnly( new[]
	{
		"DLRW", "CSPS", "TRAP", "SSEM", "KART", "RYLF", "ESSR", "EMAK", "KOLC",
		"TNAV", "SYSG", "SYSR", "SAOC", "NUOS", "SVDA", "STHC", "CSDA",
	} );

	/// <summary>Marker order observed in the only available fixture, jungle Easymode.TPWI.</summary>
	public static IReadOnlyList<string> ObservedEasymodeOrder { get; } = Array.AsReadOnly( new[]
	{
		"DLRW", "CSPS", "TRAP", "SSEM", "KOLC", "TNAV", "SYSG", "SYSR", "KART",
		"RYLF", "ESSR", "EMAK", "SAOC", "SVDA", "NUOS", "STHC", "CSDA",
	} );

	public int PayloadLength { get; }

	/// <summary>Untagged bytes before the first marker; contents not decoded.</summary>
	public int PrefixLength { get; }

	/// <summary>Sections sorted by offset.</summary>
	public IReadOnlyList<SavePayloadSection> Sections { get; }

	public bool MatchesObservedEasymodeOrder => Sections.Select( section => section.Tag ).SequenceEqual( ObservedEasymodeOrder );

	private SavePayloadLayout( int payloadLength, int prefixLength, IReadOnlyList<SavePayloadSection> sections )
	{
		PayloadLength = payloadLength;
		PrefixLength = prefixLength;
		Sections = sections;
	}

	public static SavePayloadLayout Parse( SaveReader reader )
	{
		ArgumentNullException.ThrowIfNull( reader );
		return Parse( reader.ReadFile() );
	}

	/// <summary>
	/// Locates every known marker. Each must occur exactly once anywhere in the payload, so an
	/// accidental byte match in opaque data is rejected instead of silently choosing one.
	/// </summary>
	public static SavePayloadLayout Parse( ReadOnlySpan<byte> payload )
	{
		if ( payload.Length > MaximumPayloadBytes )
			throw new InvalidDataException( $"Save payload exceeds the {MaximumPayloadBytes}-byte limit." );
		var found = new List<(string Tag, int Offset)>( KnownSectionTags.Count );
		Span<byte> marker = stackalloc byte[MarkerBytes];
		foreach ( var tag in KnownSectionTags )
		{
			Encoding.ASCII.GetBytes( tag, marker );
			var first = payload.IndexOf( marker );
			if ( first < 0 )
				throw new InvalidDataException( $"Save payload is missing section marker '{tag}'." );
			if ( payload[(first + 1)..].IndexOf( marker ) >= 0 )
				throw new InvalidDataException( $"Save payload contains section marker '{tag}' more than once; layout is ambiguous." );
			found.Add( (tag, first) );
		}
		found.Sort( ( left, right ) => left.Offset.CompareTo( right.Offset ) );
		var sections = new SavePayloadSection[found.Count];
		for ( var index = 0; index < found.Count; index++ )
		{
			var (tag, offset) = found[index];
			var end = index + 1 < found.Count ? found[index + 1].Offset : payload.Length;
			if ( end - offset < MarkerBytes )
				throw new InvalidDataException( $"Save payload section marker '{tag}' overlaps the next marker." );
			sections[index] = new SavePayloadSection( tag, BinaryPrimitives.ReadUInt32LittleEndian( payload[offset..] ), offset, end - offset );
		}
		return new SavePayloadLayout( payload.Length, found[0].Offset, Array.AsReadOnly( sections ) );
	}
}

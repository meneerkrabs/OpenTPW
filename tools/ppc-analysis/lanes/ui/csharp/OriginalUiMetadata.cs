using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenTPW.Reverse.Ui;

public enum OriginalUiEdition { FeralMacAmerican, WindowsBaselineEnglish }
public enum OriginalUiLabel
{
	EndOfYearSummary, ThisYear, LastYear, ParkRating, GameOptions, ScreenResolution,
	AudioQuality, EffectsVolume, MusicVolume, SpeechVolume, MovieVolume, PopupHelp,
	SecondaryScrollMechanism
}

public sealed record OriginalLabelIdentity( OriginalUiEdition Edition, string TextSha256,
	string CharacterTableSha256, int EntryCount );

public sealed record OriginalLabelReference( OriginalLabelIdentity Identity, OriginalUiLabel Semantic,
	int RawIndex, string MappingEvidence );

/// <summary>
/// Edition-aware label metadata only: no PC UIStrings enum is modified and no
/// global index shift is assumed. Unknown resources fail identity validation.
/// </summary>
public static class OriginalUiLabelResolver
{
	private static readonly OriginalLabelIdentity Mac = new( OriginalUiEdition.FeralMacAmerican,
		"c3768d1f448c0f85952ae3edda41da750081e85996eb76750df53fad7688bdef",
		"f682ed03507d6ea20f5f1689c321f32b52f8a21e838e9440fbc3207e7236b67d", 474 );
	private static readonly OriginalLabelIdentity Windows = new( OriginalUiEdition.WindowsBaselineEnglish,
		"3fe8b89c994bdd177b7226a51f24222621cc7e27beee94668942dc1f821137cf",
		"69f23492ef61a27ed79dd4df67978536720d403f7e2733acb6b43e6f4f78c587", 473 );

	public static OriginalLabelIdentity Identify( ReadOnlySpan<byte> strings, ReadOnlySpan<byte> characters )
	{
		if ( strings.Length is < 12 or > 16 * 1024 * 1024 || characters.Length is < 8 or > 518 )
			throw new FormatException( "Label resource exceeds its byte bounds." );
		if ( !strings[..4].SequenceEqual( "BFST"u8 ) || !characters[..4].SequenceEqual( "BFMU"u8 ) )
			throw new FormatException( "Label resource requires BFST and its own BFMU." );
		var count = BinaryPrimitives.ReadUInt32LittleEndian( strings.Slice( 8, 4 ) );
		var characterCount = BinaryPrimitives.ReadUInt16LittleEndian( characters.Slice( 6, 2 ) );
		if ( count > 8192 || 12L + count * 4L > strings.Length || characterCount > 255
			|| 8L + characterCount * 2L != characters.Length )
			throw new FormatException( "Label offset or character table is outside its bounds." );
		var textHash = Convert.ToHexString( SHA256.HashData( strings ) ).ToLowerInvariant();
		var characterHash = Convert.ToHexString( SHA256.HashData( characters ) ).ToLowerInvariant();
		foreach ( var known in new[] { Mac, Windows } )
			if ( textHash == known.TextSha256 && characterHash == known.CharacterTableSha256 && count == known.EntryCount )
				return known;
		throw new FormatException( "Unknown label identity: select an explicitly evidenced edition mapping." );
	}

	public static OriginalLabelReference Describe( OriginalLabelIdentity identity, OriginalUiLabel semantic )
	{
		ArgumentNullException.ThrowIfNull( identity );
		if ( identity != Mac && identity != Windows )
			throw new FormatException( "Unknown label identity metadata." );
		var earlyIndex = semantic switch
		{
			OriginalUiLabel.EndOfYearSummary => 186,
			OriginalUiLabel.LastYear => 187,
			OriginalUiLabel.ThisYear => 188,
			OriginalUiLabel.ParkRating => 190,
			_ => -1
		};
		var indexes = semantic switch
		{
			OriginalUiLabel.GameOptions => (315, 314),
			OriginalUiLabel.ScreenResolution => (319, 318),
			OriginalUiLabel.AudioQuality => (320, 319),
			OriginalUiLabel.EffectsVolume => (321, 320),
			OriginalUiLabel.MusicVolume => (322, 321),
			OriginalUiLabel.SpeechVolume => (323, 322),
			OriginalUiLabel.MovieVolume => (324, 323),
			OriginalUiLabel.PopupHelp => (327, 326),
			OriginalUiLabel.SecondaryScrollMechanism => (330, 329),
			_ when earlyIndex >= 0 => (earlyIndex, earlyIndex),
			_ => throw new ArgumentOutOfRangeException( nameof( semantic ), "No evidenced semantic mapping." )
		};
		var index = identity.Edition == OriginalUiEdition.FeralMacAmerican ? indexes.Item1 : indexes.Item2;
		return new OriginalLabelReference( identity, semantic, index,
			semantic == OriginalUiLabel.SecondaryScrollMechanism
				? "Mac Ctrl-click and PC right-button descriptions are edition-specific."
				: "Explicitly observed indexes; no general late-index offset rule." );
	}

	public static string ReadText( OriginalLabelReference reference, ReadOnlySpan<byte> strings, ReadOnlySpan<byte> characters )
	{
		ArgumentNullException.ThrowIfNull( reference );
		var actual = Identify( strings, characters );
		if ( actual != reference.Identity || Describe( actual, reference.Semantic ).RawIndex != reference.RawIndex )
			throw new FormatException( "Label reference and resource identity/index disagree." );
		var relative = BinaryPrimitives.ReadUInt32LittleEndian( strings.Slice( 12 + reference.RawIndex * 4, 4 ) );
		var position = 12L + relative;
		if ( position > strings.Length - 4 ) throw new FormatException( "Selected label offset is out of bounds." );
		var at = (int)position;
		if ( strings[at] != 1 ) throw new FormatException( "Selected label marker is unproved." );
		var length = strings[at + 1] | strings[at + 2] << 8 | strings[at + 3] << 16;
		if ( length > 512 || (long)at + 4 + length > strings.Length )
			throw new FormatException( "Selected label length is outside its bounds." );
		var result = new char[length];
		var characterCount = BinaryPrimitives.ReadUInt16LittleEndian( characters.Slice( 6, 2 ) );
		for ( var i = 0; i < length; i++ )
		{
			var value = strings[at + 4 + i];
			if ( value < 1 || value > characterCount ) throw new FormatException( "Selected label character is outside its own BFMU." );
			result[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian( characters.Slice( 8 + (value - 1) * 2, 2 ) );
		}
		return new string( result );
	}
}

public readonly record struct OriginalTextColor( byte Red, byte Green, byte Blue, byte Alpha );
public sealed record OriginalFontBinding( int ControlIndex, int FontSlot, OriginalTextColor Color, string ConstructorEvidence );

/// <summary>Separate constructor evidence; the layout grammar itself does not contain font assignments.</summary>
public static class OriginalLayoutFontMetadata
{
	private const string AppSha = "04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5";
	private const string MainHudTableSha = "257c12198a4121275bd57566ff31d6c00cf8b8e64ba51a92dad6f297221c994d";

	public static IReadOnlyList<OriginalFontBinding> Describe( OriginalLayoutDocument document )
	{
		ArgumentNullException.ThrowIfNull( document );
		if ( !string.Equals( document.Origin.ExecutableSha256, AppSha, StringComparison.OrdinalIgnoreCase ) || document.Origin.SectionIndex != 1
			|| document.Origin.DataOffset != 0x4ab38 || document.TableSha256 != MainHudTableSha )
			return Array.Empty<OriginalFontBinding>();
		var bindings = new List<OriginalFontBinding>();
		foreach ( var control in document.Controls )
		{
			var value = control.Id switch
			{
				47 => new OriginalFontBinding( control.Index, 1, new( 255, 255, 255, 255 ), "code:0x157030..0x157068" ),
				48 => new OriginalFontBinding( control.Index, 2, new( 255, 255, 0, 255 ), "code:0x1570c8..0x157100" ),
				32 => new OriginalFontBinding( control.Index, 3, new( 0, 0, 0, 255 ), "code:0x1573ac..0x1573dc" ),
				_ => null
			};
			if ( value is not null ) bindings.Add( value );
		}
		return bindings.AsReadOnly();
	}
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SignFileTests
{
	internal static byte[] CreateSign( params (int Style, string Face, string File, int Scale, int OffsetY, int Height)[] slots )
	{
		var data = new byte[SignFile.HeaderBytes + SignFile.SlotBytes * 2 + 12];
		BinaryPrimitives.WriteUInt32LittleEndian( data, 101 );
		data[8] = 1;
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 9 ), 2 );
		for ( var i = 0; i < slots.Length; i++ )
		{
			var offset = SignFile.HeaderBytes + i * SignFile.SlotBytes;
			var slot = slots[i];
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( offset ), slot.Style );
			Encoding.Latin1.GetBytes( slot.Face ).CopyTo( data, offset + 4 );
			Encoding.Latin1.GetBytes( slot.File ).CopyTo( data, offset + 68 );
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( offset + 328 ), slot.Scale );
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( offset + 332 ), slot.OffsetY );
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( offset + 336 ), slot.Height );
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( offset + 352 ), 400 );
			data[offset + 359] = 1;
			data[offset + 360] = 4;
			data[offset + 362] = 4;
			Encoding.Latin1.GetBytes( slot.Face ).CopyTo( data, offset + 364 );
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( offset + 408 ), 0.5f );
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( offset + 412 ), 1.0f );
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( offset + 416 ), 2.0f );
		}
		return data;
	}

	[TestMethod]
	public void ReadsHeaderTextSlotsAndLogFont()
	{
		var sign = new SignFile( CreateSign( (2, "Young Itch AOE", "YOUNIA__.TTF", 100, -25, -144), (11, "Clunker AOE", "CLUNA___.TTF", 100, 128, -111) ) );
		Assert.AreEqual( (101u, 0u, (byte)1, 2u), (sign.Version, sign.HeaderFlag, sign.ExtraImageFlag, sign.HeaderCount) );
		Assert.AreEqual( 2, sign.Slots.Count );
		var first = sign.Slots[0];
		Assert.AreEqual( (2, "Young Itch AOE", "YOUNIA__.TTF", 100, -25), (first.StyleId, first.FaceName, first.FontFileName, first.HorizontalScalePercent, first.OffsetY) );
		Assert.AreEqual( (-144, 400, (byte)1, (byte)4, (byte)4), (first.LogFont.Height, first.LogFont.Weight, first.LogFont.CharSet, first.LogFont.OutPrecision, first.LogFont.Quality) );
		Assert.AreEqual( 144, first.EmHeightPixels );
		Assert.AreEqual( ((byte)128, (byte)255, (byte)255), SignCanvas.SlotColor( first ), "parameters 2..4 as clamped RGB" );
		Assert.AreEqual( 12, sign.Remainder.Length );
	}

	[TestMethod]
	public void RejectsTruncatedUnknownOrInconsistentSigns()
	{
		var valid = CreateSign( (1, "Haunt AOE", "HAUNTAOE.TTF", 100, 0, -100), (2, "Haunt AOE", "HAUNTAOE.TTF", 100, 128, -90) );
		Assert.ThrowsException<InvalidDataException>( () => new SignFile( valid[..500] ) );
		var version = (byte[])valid.Clone();
		version[0] = 7;
		Assert.ThrowsException<InvalidDataException>( () => new SignFile( version ) );
		var mismatch = CreateSign( (1, "Haunt AOE", "HAUNTAOE.TTF", 100, 0, -100), (2, "Haunt AOE", "HAUNTAOE.TTF", 100, 128, -90) );
		mismatch[SignFile.HeaderBytes + 364] = (byte)'X';
		Assert.ThrowsException<InvalidDataException>( () => new SignFile( mismatch ) );
		var notTtf = CreateSign( (1, "Haunt AOE", "HAUNTAOE.FON", 100, 0, -100), (2, "Haunt AOE", "HAUNTAOE.TTF", 100, 128, -90) );
		Assert.ThrowsException<InvalidDataException>( () => new SignFile( notTtf ) );
	}

	[TestMethod]
	public void ObjectNameLinesArePairsOfEntries()
	{
		var names = new[] { "", "", "DinoKarts", "", "Gorilla", "Thrilla" };
		Assert.AreEqual( ("Gorilla", "Thrilla"), SignCanvas.ObjectNameLines( names, 2 ) );
		Assert.AreEqual( ("DinoKarts", ""), SignCanvas.ObjectNameLines( names, 1 ) );
		Assert.AreEqual( ("", ""), SignCanvas.ObjectNameLines( names, 40 ) );
	}

	[TestMethod]
	public void ComposesSignHalvesAndReportsMissingFonts()
	{
		var builder = new TrueTypeFontTests.FontBuilder();
		builder.AddSimple( 1000, 'A', new[] { (0, 0, true), (0, 700, true), (1000, 700, true), (1000, 0, true) } );
		var library = SignFontLibrary.FromFiles( new[] { ("TEST____.TTF", builder.Build()) } );
		var sign = new SignFile( CreateSign( (1, "Test", "TEST____.TTF", 100, 20, -100), (2, "Gone", "GONE____.TTF", 100, 128, -90) ) );
		var diagnostics = new List<string>();
		var canvas = SignCanvas.Compose( sign, library, new[] { "AA", "A" }, (0, 0, 0, 255), diagnostics );
		Assert.AreEqual( SignCanvas.Width * SignCanvas.Height * 4, canvas.Length );
		Assert.AreEqual( 1, diagnostics.Count );
		StringAssert.Contains( diagnostics[0], "GONE____.TTF" );
		var (left, right) = SignCanvas.SplitHalves( canvas );
		Assert.AreEqual( SignCanvas.HalfWidth * SignCanvas.Height * 4, left.Length );
		// "AA" (2 x 100 px) is centred: it spans both halves, and the second slot's line is absent.
		Assert.IsTrue( left.Where( ( value, index ) => index % 4 == 1 ).Any( value => value == 255 ) );
		Assert.IsTrue( right.Where( ( value, index ) => index % 4 == 1 ).Any( value => value == 255 ) );
		var rowBelowSecondSlot = (200 * SignCanvas.Width + 256) * 4;
		Assert.AreEqual( 0, canvas[rowBelowSecondSlot + 1] );
	}

	// ---- Original .sgn corpus (OPENTPW_GAME_PATH) ----

	internal static IEnumerable<(string Path, byte[] Data)> OriginalSignMembers( string dataDirectory )
	{
		var archives = Directory.EnumerateDirectories( Path.Combine( dataDirectory, "levels" ) )
			.SelectMany( level => new[] { "rides", "features" }.Select( category => Path.Combine( level, category ) ) )
			.Where( Directory.Exists )
			.SelectMany( directory => Directory.EnumerateFiles( directory, "*.wad" ) )
			.Append( Path.Combine( dataDirectory, "lobby.wad" ) )
			.Where( File.Exists )
			.OrderBy( path => path, StringComparer.Ordinal );
		foreach ( var archivePath in archives )
		{
			var archive = new WadArchive( archivePath );
			var stack = new Stack<(ArchiveDirectory Directory, string Prefix)>();
			stack.Push( (archive.Root, "") );
			while ( stack.Count > 0 )
			{
				var (directory, prefix) = stack.Pop();
				foreach ( var item in directory.Children )
				{
					if ( item is ArchiveDirectory child )
						stack.Push( (child, prefix + child.Name + "/") );
					else if ( item is ArchiveFile file && file.Name.EndsWith( ".sgn", StringComparison.OrdinalIgnoreCase ) )
						yield return ($"{Path.GetRelativePath( dataDirectory, archivePath ).Replace( '\\', '/' )}/{prefix}{file.Name}", file.GetData());
				}
			}
		}
	}

	[TestMethod]
	public void EveryOriginalSignParsesAndNamesShippedFonts()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var signs = OriginalSignMembers( data ).Select( member => (member.Path, Sign: new SignFile( member.Data )) ).ToList();
		Assert.AreEqual( 84, signs.Count );
		Assert.AreEqual( 45, signs.Count( sign => sign.Sign.Version == 101 ) );
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var library = SignFontLibrary.Load( stream );
		var unresolved = signs.SelectMany( sign => sign.Sign.Slots.Select( ( slot, index ) => (sign.Path, index, slot) ) )
			.Where( entry => library.Find( entry.slot.FontFileName ) == null ).Select( entry => $"{entry.Path}#{entry.index}:{entry.slot.FontFileName}:{entry.slot.FaceName}" ).ToArray();
		// The only reference to a font that fonts.wad does not ship (data defect, see docs/COMPATIBILITY.md).
		CollectionAssert.AreEqual( new[] { "levels/space/rides/megacost.wad/megacost.sgn#1:EGGII___.TTF:EggIt Italic" }, unresolved );
		Assert.IsTrue( signs.All( sign => sign.Sign.Slots.All( slot => slot.LogFont.Height is < -50 and > -260 && slot.LogFont.Quality == 4 ) ) );

		var gate = signs.Single( sign => sign.Path == "levels/jungle/features/gates.wad/gates.sgn" ).Sign;
		Assert.AreEqual( ("YOUNIA__.TTF", "Young Itch AOE", -144, -25), (gate.Slots[0].FontFileName, gate.Slots[0].FaceName, gate.Slots[0].LogFont.Height, gate.Slots[0].OffsetY) );
		Assert.AreEqual( ("CLUNA___.TTF", "Clunker AOE", -119, 97), (gate.Slots[1].FontFileName, gate.Slots[1].FaceName, gate.Slots[1].LogFont.Height, gate.Slots[1].OffsetY) );
		var gates = signs.Where( sign => sign.Path.EndsWith( "/features/gates.wad/gates.sgn", StringComparison.Ordinal ) ).ToDictionary( sign => sign.Path.Split( '/' )[1], sign => sign.Sign.Slots[0].FontFileName );
		CollectionAssert.AreEquivalent( new[] { "fantasy:BIGLA___.TTF", "hallow:HAUNTAOE.TTF", "jungle:YOUNIA__.TTF", "space:GARGSA__.TTF" }, gates.Select( pair => $"{pair.Key}:{pair.Value}" ).ToArray() );
	}

	[DataTestMethod]
	[DynamicData( nameof( LanguageTests.ShippedLanguages ), typeof( LanguageTests ) )]
	public void EverySignRelevantCharacterHasAGlyphInEveryFont( string name )
	{
		var language = LanguageTests.OriginalLanguagePublic( name );
		var data = StringTableTests.OriginalDataDirectory();
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var library = SignFontLibrary.Load( stream );
		var text = new[] { "OBJECT_NAMES.str", "THEMENAMES.str" }.SelectMany( file => language.LoadStrings( file ).Entries ).ToArray();
		Assert.IsTrue( text.Length > 300 );
		foreach ( var font in library.Fonts )
		{
			var missing = text.SelectMany( entry => SignTextLayout.FindMissing( font, entry ) ).Distinct().ToArray();
			Assert.AreEqual( 0, missing.Length, $"{name} {font.SourceName}: {string.Join( ", ", missing.Select( code => $"U+{code:X4}" ) )}" );
		}
	}
}

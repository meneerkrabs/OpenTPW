using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class ObjectSignTextTests
{
	[TestMethod]
	[DataRow( "Temple Of Gloom", "Temple", "Of Gloom" )]
	[DataRow( "Sun God", "Sun", "God" )]
	[DataRow( "Belly Bounce", "Belly", "Bounce" )]
	[DataRow( "Aztec Mayhem", "Aztec", "Mayhem" )]
	[DataRow( "Inca Totem", "Inca", "Totem" )]
	public void ASingleNameSplitsAtTheSpaceNearestTheMiddle( string name, string first, string second )
	{
		Assert.AreEqual( (first, second), SignCanvas.SplitAtMiddleSpace( name ) );
	}

	[TestMethod]
	public void TheProbeOrderIsMiddleThenLeftThenRightAndWidensOutwards()
	{
		// "ab cd ef": middle 4, probes 4, 3, 5: the space at 5 (offset +1) is found before the one at 2 (offset -2).
		Assert.AreEqual( ("ab cd", "ef"), SignCanvas.SplitAtMiddleSpace( "ab cd ef" ) );
		// "abc de fg": middle 4, probes 4, 3: the space at 3 (offset -1) wins over the one at 6 (offset +2).
		Assert.AreEqual( ("abc", "de fg"), SignCanvas.SplitAtMiddleSpace( "abc de fg" ) );
		// "a b c d": middle 3 is a space itself.
		Assert.AreEqual( ("a b", "c d"), SignCanvas.SplitAtMiddleSpace( "a b c d" ) );
		// Only the one space dropped: a double space leaves the second one at the start of line 2.
		Assert.AreEqual( ("abc", " de"), SignCanvas.SplitAtMiddleSpace( "abc  de" ) );
	}

	[TestMethod]
	public void ANameWithoutASpaceIsOneLineAndEmptyStaysEmpty()
	{
		Assert.AreEqual( ("DinoKarts", ""), SignCanvas.SplitAtMiddleSpace( "DinoKarts" ) );
		Assert.AreEqual( ("", ""), SignCanvas.SplitAtMiddleSpace( "" ) );
		Assert.AreEqual( ("", "x"), SignCanvas.SplitAtMiddleSpace( " x" ) );
		Assert.AreEqual( ("x", ""), SignCanvas.SplitAtMiddleSpace( "x " ) );
	}

	[TestMethod]
	public void NameEntriesJoinWithASpaceOrWithoutWhenTheFirstEndsInADash()
	{
		Assert.AreEqual( "Sun God", ObjectNames.Join( new[] { "Sun", "God" } ) );
		Assert.AreEqual( "Mumbo", ObjectNames.Join( new[] { "Mumbo", "" } ) );
		Assert.AreEqual( "Hundekarts", ObjectNames.Join( new[] { "Hunde-", "karts" } ) );
		Assert.AreEqual( "Solo-", ObjectNames.Join( new[] { "Solo-" } ) );
	}

	[TestMethod]
	public void SlotNamesMatchWithOrWithoutExtension()
	{
		Assert.AreEqual( 0, ObjectSigns.SlotOf( "sign1" ) );
		Assert.AreEqual( 1, ObjectSigns.SlotOf( "SIGN2.tga\0" ) );
		Assert.AreEqual( -1, ObjectSigns.SlotOf( "sign3" ) );
		Assert.IsFalse( ObjectSigns.IsSignSlot( "totem" ) );
	}
}

/// <summary>Sign textures of the original objects; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectSignCorpusTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the object sign corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	private static bool HasSignFaces( ObjectCatalogEntry entry )
	{
		using var stream = entry.FileSystem.OpenRead( entry.ModelPath );
		return new ModelFile( stream ).Meshes.Any( mesh => mesh.Materials.Any( material => ObjectSigns.IsSignSlot( material.Name ) ) );
	}

	private static int MagentaPixels( byte[] rgba )
	{
		var count = 0;
		for ( var i = 0; i < rgba.Length; i += 4 )
			count += rgba[i] == 255 && rgba[i + 1] == 0 && rgba[i + 2] == 255 ? 1 : 0;
		return count;
	}

	[TestMethod]
	public void EveryBuildableObjectWithSignFacesGetsNonEmptySignTextures()
	{
		var withFaces = 0;
		var failures = new List<string>();
		var silent = new List<string>();
		foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( entry => entry.IsBuildable && !entry.IsBonus ) )
			{
				if ( !HasSignFaces( entry ) )
					continue;
				withFaces++;
				var images = ObjectSigns.Get( entry );
				if ( images == null )
					failures.Add( $"{theme}/{entry.ArchiveName}: no sign images" );
				else if ( images.TextPixelCount == 0 )
					silent.Add( $"{theme}/{entry.ArchiveName}" );
				else if ( images.Left.Length != SignCanvas.HalfWidth * SignCanvas.Height * 4 || images.Right.Length != images.Left.Length )
					failures.Add( $"{theme}/{entry.ArchiveName}: wrong texture size" );
			}
		}
		Assert.IsTrue( withFaces > 10, $"expected many objects with sign faces, found {withFaces}" );
		CollectionAssert.AreEqual( Array.Empty<string>(), failures, string.Join( "\n", failures ) );
		// The three signs whose both text slots have colour mode 0 draw no text (the native compositor paints only modes 1 and 2); they show the board colour.
		CollectionAssert.AreEquivalent( new[] { "hallow/c_scat", "fantasy/jelly", "space/zob" }, silent );
	}

	[TestMethod]
	public void SignTexturesAreNotThePlaceholder()
	{
		// The shipped sign1.wct/sign2.wct placeholders are opaque magenta; a composed sign is the board colour plus text.
		var totem = ObjectCatalog.Load( "jungle" ).Entries.Single( entry => entry.ArchiveName.Equals( "totem", StringComparison.OrdinalIgnoreCase ) );
		var images = ObjectSigns.Get( totem )!;
		Assert.AreEqual( 0, MagentaPixels( images.Left ) + MagentaPixels( images.Right ) );
		var board = ObjectSigns.LightBoard; // the Totem's text is black
		Assert.AreEqual( (board.R, board.G, board.B, board.A), (images.Left[0], images.Left[1], images.Left[2], images.Left[3]) );
		Assert.AreSame( images, ObjectSigns.Get( totem ), "one composition per archive and text" );
	}

	[TestMethod]
	public void TheJungleRidesCarryTheirNamesOnTwoLines()
	{
		var catalog = ObjectCatalog.Load( "jungle" );
		var totem = catalog.Entries.Single( entry => entry.ArchiveName.Equals( "totem", StringComparison.OrdinalIgnoreCase ) );
		Assert.AreEqual( ("Inca", "Totem"), ObjectSigns.Lines( totem ) );
		var images = ObjectSigns.Get( totem );
		Assert.IsNotNull( images );
		Assert.IsTrue( images.TextPixelCount > 1000 );
	}

	[TestMethod]
	public void EveryObjectNameOfEveryLanguageComposesWithoutThrowing()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var overlay = Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_DATA" );
		var usable = !string.IsNullOrWhiteSpace( overlay ) && Directory.Exists( overlay ) ? overlay : null;
		var sign = SignTextRenderer.LoadSignFile( "/levels/jungle/rides/totem" )!;
		var composedAny = false;
		var withoutGlyphs = new List<string>();
		foreach ( var name in GameLanguage.FindLanguages( data, usable ) )
		{
			var entries = GameLanguage.Resolve( data, name, name == "English" ? null : usable ).LoadStrings( ObjectNames.FileName ).Entries;
			foreach ( var entry in entries.Where( entry => entry.Trim().Length > 0 ) )
			{
				var (first, second) = SignCanvas.SplitAtMiddleSpace( entry.Trim() );
				var composed = SignTextRenderer.Shared.ComposeSign( sign, new[] { first, second }, OriginalGateSign.Background );
				composedAny = true;
				withoutGlyphs.AddRange( composed.Diagnostics.Where( diagnostic => diagnostic.Contains( "no glyph" ) ).Select( diagnostic => $"{name} \"{entry}\": {diagnostic}" ) );
			}
		}
		Assert.IsTrue( composedAny );
		// Characters outside a font's cmap are reported (drawn as .notdef), never thrown; the count is informational.
		Console.WriteLine( $"{withoutGlyphs.Count} object names have characters without glyphs in the totem sign fonts." );
	}

	[TestMethod]
	public void CharactersOutsideTheFontAreReportedNotThrown()
	{
		var sign = SignTextRenderer.LoadSignFile( "/levels/jungle/rides/totem" )!;
		var composed = SignTextRenderer.Shared.ComposeSign( sign, new[] { "\u4e2dmega", "R\u00eede" }, OriginalGateSign.Background );
		Assert.IsTrue( composed.Diagnostics.Any( diagnostic => diagnostic.Contains( "U+4E2D" ) ) );
		Assert.IsTrue( composed.TextPixelCount > 0 );
	}
}

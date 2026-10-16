using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenTPW.Tests;

/// <summary>
/// The park name as the gate's two sign lines (COMPAT-007): the gate's OBJECT_NAMES row as default (tp.exe 0x4DB6F9), the
/// one-line form of the ticket price window (0x4E2780), the rename dialog's fields (0x4990F0) and the optional save part.
/// </summary>
[TestClass]
public class ParkNameTests
{
	private static string[] Names()
	{
		var names = Enumerable.Range( 0, 400 ).Select( index => $"name{index}" ).ToArray();
		names[1] = "";
		names[85] = "LOST";
		names[86] = "Kingdom";
		names[272] = "Wonderland";
		names[273] = "";
		return names;
	}

	[TestMethod]
	public void TheGateStartsWithTheTwoTextsOfItsRow()
	{
		// Rows of the executable's table 0x7488D8: 1601 -> (85, 86), 4601 -> (272, 273), 1203 -> (44, 1).
		Assert.AreEqual( new ParkNameLines( "LOST", "Kingdom" ), ParkNameLines.Default( 1601, Names() ) );
		Assert.AreEqual( new ParkNameLines( "Wonderland", "" ), ParkNameLines.Default( 4601, Names() ), "an empty second text leaves line 2 empty" );
		Assert.AreEqual( new ParkNameLines( "name44", "" ), ParkNameLines.Default( 1203, Names() ), "index 1 is the empty text" );
		Assert.IsNull( ParkNameLines.Default( 99999, Names() ), "an id outside the table has no row" );
		var longNames = Names();
		longNames[85] = new string( 'x', 40 );
		Assert.AreEqual( ParkNameLines.MaximumLength, ParkNameLines.Default( 1601, longNames )!.Line1.Length, "each line is cut to 32 characters" );
	}

	[TestMethod]
	public void TheOneLineNameJoinsTheLinesAsTheThingNameDoes()
	{
		Assert.AreEqual( "LOST Kingdom", new ParkNameLines( "LOST", "Kingdom" ).DisplayName );
		Assert.AreEqual( "Kingdom", new ParkNameLines( "", "Kingdom" ).DisplayName );
		Assert.AreEqual( "LOST", new ParkNameLines( "LOST", "" ).DisplayName );
		Assert.AreEqual( "Hundekarts", new ParkNameLines( "Hunde-", "karts" ).DisplayName );
	}

	[TestMethod]
	public void LinesAreBoundedAndHoldNoControlCharacters()
	{
		Assert.IsTrue( ParkNameLines.IsValidLine( "" ) );
		Assert.IsTrue( ParkNameLines.IsValidLine( new string( 'a', ParkNameLines.MaximumLength ) ) );
		Assert.IsFalse( ParkNameLines.IsValidLine( new string( 'a', ParkNameLines.MaximumLength + 1 ) ) );
		Assert.IsFalse( ParkNameLines.IsValidLine( null ) );
		Assert.IsFalse( ParkNameLines.IsValidLine( "a\tb" ) );
		Assert.IsFalse( ParkNameLines.IsValidLine( "a\0" ) );
		Assert.IsFalse( ParkNameLines.IsValidLine( "\ud800x" ), "an unpaired surrogate" );
		Assert.IsTrue( ParkNameLines.IsValidLine( "Café 🎢" ) );
	}

	[TestMethod]
	public void TheDialogAndTheReloadDropOneDashOfATrailingDoubleDashFromLineOne()
	{
		var lines = new ParkNameLines( "Super--", "Park" );
		Assert.AreEqual( ("Super-", "Park"), lines.ReloadedSignLines );
		Assert.AreEqual( ("Super-", "Park"), lines.DialogFields );
		var longLines = new ParkNameLines( "abcdefghijklmnopqrst", "x" );
		Assert.AreEqual( ParkNameLines.DialogFieldLength, longLines.DialogFields.Line1.Length, "the dialog field holds 14 characters" );
	}

	[TestMethod]
	public void TheSaveCarriesTheLinesOptionallyAndRejectsInvalidOnes()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope() with { ParkName = new ParkNameSaveData( "Kevin", "Cosmic" ) };
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		Assert.AreEqual( new ParkNameSaveData( "Kevin", "Cosmic" ), restored.ParkName );

		// A save from before the part has no member at all and loads with no park name (the gate's default).
		var json = JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() ) ) )!.AsObject();
		json.Remove( "ParkName" );
		Assert.IsNull( ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ).ParkName );

		void Rejects( ParkNameSaveData name )
		{
			var exception = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( snapshot with { ParkName = name } ) );
			StringAssert.Contains( exception.Message, "park name" );
		}
		Rejects( new ParkNameSaveData( new string( 'a', 33 ), "" ) );
		Rejects( new ParkNameSaveData( "a\nb", "" ) );
		Rejects( new ParkNameSaveData( null!, "" ) );
	}

	[TestMethod]
	public void RenamedSignImagesKeepTheirIdentityAndTakeTheNewHalves()
	{
		var size = SignSurfaceComposer.HalfWidth * SignSurfaceComposer.Height * 4;
		var images = new ObjectSignImages( "LOST", "Kingdom", new byte[size], new byte[size], 10, Array.Empty<string>() );
		var left = new byte[size];
		left[3] = 255;
		images.Replace( "Kevin", "Park", left, new byte[size], 20, Array.Empty<string>() );
		Assert.AreEqual( ("Kevin", "Park", 20), (images.Line1, images.Line2, images.TextPixelCount) );
		Assert.AreSame( left, images.Left );
		Assert.ThrowsException<ArgumentException>( () => images.Replace( "a", "b", new byte[4], new byte[size], 0, Array.Empty<string>() ) );
	}
}

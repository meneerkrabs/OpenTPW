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

	/// <summary>
	/// The save field of the gate lines (COMPAT-007): the gate's attraction record carries them as <c>mNameA</c>/<c>mNameB</c> (the thing's +0x3C8 and +0x40A, written by
	/// the setters 0x4E2950 and 0x4E2A50); the shipped jungle Easymode save holds LOST / Kingdom, the gate's default row.
	/// </summary>
	[TestMethod]
	public void ASavedGatesTwoLinesAreTheParkNameUnlessEmptyOrUnholdable()
	{
		Assert.AreEqual( new ParkNameLines( "Wonder", "Land" ), ParkNameLines.FromSavedGate( "Wonder", "Land" ) );
		Assert.AreEqual( new ParkNameLines( "", "Land" ), ParkNameLines.FromSavedGate( "", "Land" ) );
		Assert.AreEqual( new ParkNameLines( "", "" ), ParkNameLines.FromSavedGate( "", "" ), "a blank name is a name: the rename dialog's OK tests no emptiness (0x4993E0)" );
		Assert.IsNull( ParkNameLines.FromSavedGate( null, "Land" ) );
		Assert.IsNull( ParkNameLines.FromSavedGate( new string( 'x', ParkNameLines.MaximumLength + 1 ), "Land" ), "a line longer than the gate holds is refused" );
		Assert.IsNull( ParkNameLines.FromSavedGate( "Wonder\u0007", "Land" ), "a control character is refused" );
	}

	[TestMethod]
	public void AnIdleAttractionSavedWithItsOpenWordClearIsClosedAndTheOthersAreNot()
	{
		Assert.IsTrue( RideOpening.ClosedByImportedOpenWord( 0, RideObjectStates.Idle ) );
		Assert.IsFalse( RideOpening.ClosedByImportedOpenWord( 1, RideObjectStates.Idle ) );
		foreach ( var state in new[] { RideObjectStates.BrokenDown, RideObjectStates.Upgrading, RideObjectStates.Condemned, RideObjectStates.NotAnAttraction } )
			Assert.IsFalse( RideOpening.ClosedByImportedOpenWord( 0, state ), $"state {state} is imported by its own rules" );
	}

	[TestMethod]
	public void TheShippedEasymodeSaveCarriesTheGateLines()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game." );
		try
		{
			var gate = OriginalPark.Load( "jungle" ).Save!.Attractions.Single( attraction => attraction.InfoId == 1601 );
			Assert.AreEqual( ("LOST", "Kingdom"), (gate.NameLine1, gate.NameLine2) );
		}
		finally
		{
			FileSystem = previous;
		}
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

/// <summary>A renamed park survives a world save and resume through <see cref="Level"/> (capture, codec, candidate setup, recapture).</summary>
[TestClass, DoNotParallelize]
public class ParkNameResumeTests
{
	private UpgradeCatalogueDefaultsTests fixture = null!;
	private const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	[TestMethod]
	public void ARenamedParkSurvivesSaveAndResume()
	{
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))typeof( UpgradeCatalogueDefaultsTests )
			.GetMethod( "World", Private )!.Invoke( fixture, new object[] { "", 0, false } )!;
		var assets = (System.Collections.Generic.Dictionary<string, string>)typeof( UpgradeCatalogueDefaultsTests )
			.GetMethod( "PrepareCapture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
		var level = world.Level;
		// The synthetic world has no original gate: give it the jungle gate's default lines, as SetupParkName would.
		typeof( Level ).GetProperty( nameof( Level.ParkName ) )!.SetValue( level, new ParkNameLines( "LOST", "Kingdom" ) );
		Assert.AreEqual( new ParkNameSaveData( "LOST", "Kingdom" ), level.CaptureResumeSnapshot( null, "before", assets ).ParkName );
		Assert.IsFalse( level.RenamePark( "a\tb", "" ), "a control character is refused" );
		Assert.IsTrue( level.RenamePark( "Kevin", "Cosmic Park" ) );

		var saved = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( level.CaptureResumeSnapshot( null, "renamed", assets ) ) );
		Assert.AreEqual( new ParkNameSaveData( "Kevin", "Cosmic Park" ), saved.ParkName );

		var candidate = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		typeof( Level ).GetField( "resumeState", Private )!.SetValue( candidate, saved );
		typeof( Level ).GetMethod( "SetupParkName", Private )!.Invoke( candidate, null );
		Assert.AreEqual( new ParkNameLines( "Kevin", "Cosmic Park" ), candidate.ParkName );
		Assert.AreEqual( "Kevin Cosmic Park", candidate.ParkName!.DisplayName );

		// A save from before the part restores without a saved name (the gate's default lines come from the gate itself).
		var older = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		typeof( Level ).GetField( "resumeState", Private )!.SetValue( older, saved with { ParkName = null } );
		typeof( Level ).GetMethod( "SetupParkName", Private )!.Invoke( older, null );
		Assert.IsNull( older.ParkName );
	}
}

/// <summary>The text field the rename dialog uses never leaves half a surrogate pair in its text.</summary>
[TestClass]
public class UiTextFieldSurrogateTests
{
	[TestMethod]
	public void TypingKeepsSurrogatePairsWholeAndBackspaceRemovesThemWhole()
	{
		var field = new OpenTPW.UI.Original.UiTextField { MaximumLength = 3 };
		field.Type( "ab🎢", 0 );
		Assert.AreEqual( "ab", field.Text, "a pair that does not fit is not inserted half" );
		field.Type( "", 1 );
		field.Type( "🎢", 0 );
		Assert.AreEqual( "a🎢", field.Text );
		field.Type( "", 1 );
		Assert.AreEqual( "a", field.Text, "backspace removes the whole pair" );
		field.Type( "\ud800x\udc00", 0 );
		Assert.AreEqual( "ax", field.Text, "unpaired surrogates are dropped" );
		Assert.IsTrue( ParkNameLines.IsValidLine( field.Text ) );
	}
}

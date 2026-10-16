using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The info-record name a catalogue sign draws (tp.exe 0x4629D0 -> 0x412F50 -> 0x467A80, the name filled by 0x412FB0 /
/// 0x4146B0 over the Info.Id table 0x7488D8): the joined OBJECT_NAMES entries of the object's row, or empty for an id
/// outside the table. The display name used elsewhere keeps its .sam fallback.
/// </summary>
[TestClass]
public class ObjectSignNameTests
{
	private static ObjectCatalogEntry Entry( int id, string name ) => new( "jungle", ObjectCategory.Feature, "/levels/jungle/features/x", new ObjectSettings( new[]
	{
		new ObjectSettingsFile( $"Info.Id\t{id}\nInfo.Name\t\"{name}\"\n", "x.sam" )
	} ), ObjectShape.Single, "/x.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(),
		Array.Empty<string>(), new BaseFileSystem( Path.GetTempPath() ), null );

	private static string[] Names()
	{
		// Index 0 and 1 are empty (1 marks "no second part"); 28/29 are the Inca Totem's row (1110), 44 the row of 1203.
		var names = Enumerable.Range( 0, 400 ).Select( index => $"name{index}" ).ToArray();
		names[0] = "";
		names[1] = "";
		names[28] = "Inca";
		names[29] = "Totem";
		names[44] = "Wall";
		return names;
	}

	[TestMethod]
	public void AnIdOutsideTheTableHasAnEmptySignNameButKeepsItsDisplayName()
	{
		var totem = Entry( 1110, "unrelated" );
		var wall = Entry( 1203, "x" );
		var unlisted = Entry( 99999, "Mystery Ride" );
		var end = Entry( 1605, "End Item" );
		var entries = new[] { totem, wall, unlisted, end };
		ObjectNames.Assign( entries, ObjectNames.Bind( entries ), Names(), infoIdTable: true );

		Assert.AreEqual( "Inca Totem", totem.SignName );
		Assert.AreEqual( "Inca Totem", totem.DisplayName );
		Assert.AreEqual( "Wall", wall.SignName );
		Assert.AreEqual( "", unlisted.SignName, "0x4146B0 formats an empty name for an id outside the table" );
		Assert.AreEqual( "Mystery Ride", unlisted.DisplayName, "the build list keeps the .sam name" );
		Assert.AreEqual( "", end.SignName, "the row names the empty entry 0" );
		Assert.AreEqual( "End Item", end.DisplayName );

		Assert.AreEqual( ("Inca", "Totem"), ObjectSigns.Lines( totem ) );
		Assert.AreEqual( ("", ""), ObjectSigns.Lines( unlisted ), "the sign draws the empty name, not the .sam name" );
	}

	[TestMethod]
	public void WithoutTheInfoIdTableTheDisplayNameStandsIn()
	{
		var unlisted = Entry( 99999, "Mystery Ride" );
		var entries = new[] { unlisted };
		ObjectNames.Assign( entries, Array.Empty<(ObjectCatalogEntry, int, int)>(), Names(), infoIdTable: false );
		Assert.IsNull( unlisted.SignName );
		Assert.AreEqual( ("Mystery", "Ride"), ObjectSigns.Lines( unlisted ) );
	}
}

/// <summary>An empty info name on an original sign; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectSignEmptyNameCorpusTests
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

	[TestMethod]
	public void AnEmptyInfoNameComposesTheBoardWithoutText()
	{
		// The jungle gate's sign has a board (a version-101 extra image): an empty name still composes, showing the board alone.
		var gate = ObjectCatalog.Load( "jungle" ).Entries.Single( entry => entry.ArchiveName.Equals( "gates", StringComparison.OrdinalIgnoreCase ) );
		var previous = gate.SignName;
		try
		{
			gate.SignName = "";
			Assert.AreEqual( ("", ""), ObjectSigns.Lines( gate ) );
			var images = ObjectSigns.Get( gate );
			Assert.IsNotNull( images, "0x467A80 only skips a null name; an empty one is generated" );
			Assert.AreEqual( 0, images.TextPixelCount );
			Assert.IsTrue( images.Left.Where( ( value, index ) => index % 4 == 3 && value > 0 ).Any(), "the board is drawn" );
		}
		finally
		{
			gate.SignName = previous;
		}
	}
}

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The controller types of the shipped rides against the controllers OpenTPW builds (QUEUE-004): tp.exe's placement 0x00529E10 builds a controller
/// through 0x00545890 for any nonzero ride type word +0xA0 (<c>Bumper.BumperType</c>), and the live excitement 0x004E0560 then scores 60 % of the
/// level plus the circuit counts. Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
public class BumperTypeCoverageTests
{
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original ride archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	/// <summary>
	/// Every catalogue entry of the four themes (bonus entries included) with a nonzero type word: the types are −1 and −4..−14 (the original's
	/// placement would also build −2 and −3, which no ride uses), a type belongs to one track family (WhichTrackType 0 bumper cars, 1 karts,
	/// 2 water: no ride mixes a type with another family), and every one gets the controller of its template.
	/// </summary>
	[TestMethod]
	public void EveryShippedRideWithAControllerTypeGetsAControllerTemplate()
	{
		var typed = ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries )
			.Select( entry => (Entry: entry, Track: entry.Settings.GetInt( "Bumper.WhichTrackType" ), Type: entry.Settings.GetInt( "Bumper.BumperType" )) )
			.Where( item => item.Type != 0 )
			.ToArray();
		Assert.AreEqual( 12, typed.Length, "three rides of each of the four themes" );
		CollectionAssert.AreEquivalent( new[] { -1, -4, -5, -6, -7, -8, -9, -10, -11, -12, -13, -14 }, typed.Select( item => item.Type ).ToArray(),
			"the types the shipped rides use; the original's −2 and −3 are not used" );
		foreach ( var (entry, track, type) in typed )
		{
			Assert.AreEqual( ObjectCategory.Ride, entry.Category, entry.DisplayName );
			var template = BumperTemplates.ForRide( track, type );
			Assert.IsNotNull( template, $"{entry.DisplayName}: WhichTrackType {track} with BumperType {type} has a template, so its controller is built" );
			Assert.AreEqual( type, template.Type, entry.DisplayName );
		}
	}
}

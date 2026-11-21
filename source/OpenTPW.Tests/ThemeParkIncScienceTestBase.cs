using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>A test class over the retail Theme Park Inc Science economy (OPENTPW_TPI_PATH): it selects the edition and the game files for a test and restores them after it.</summary>
public abstract class ThemeParkIncScienceTestBase
{
	private BaseFileSystem? previous;
	private Logger? previousLog;

	[TestInitialize]
	public void InitializeScience() => previous = FileSystem;

	[TestCleanup]
	public void CleanupScience()
	{
		FileSystem = previous!;
		if ( previousLog != null )
			Log = previousLog;
		previousLog = null;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	/// <summary>The economy of the prebuilt Science park, as a Theme Park Inc level builds it (its career, staff and mission controller).</summary>
	protected ParkEconomy ScienceEconomy()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		previousLog ??= Log;
		Log = new();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		return ParkEconomyRuntime.ForThemeParkIncLevel( OriginalPark.Load( "science" ) ).Economy;
	}
}

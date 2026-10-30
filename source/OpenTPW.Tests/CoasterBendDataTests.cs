using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The bend cars of the PC coasters (inconclusive without OPENTPW_GAME_PATH): COAST-034, tp.exe <c>0x438800</c>, <c>0x438ab0</c>.</summary>
[TestClass]
public class CoasterBendDataTests
{
	private BaseFileSystem previousFileSystem = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		previousLog = Log;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original coaster archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem;
		Log = previousLog;
	}

	private static IEnumerable<ObjectCatalogEntry> PcCoasters() =>
		ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries ).Where( entry => !entry.IsBonus && CoasterDefinition.IsCoaster( entry.Settings ) );

	/// <summary>
	/// A vertex of a bend car beyond the tables reads the neighbouring stack arrays in the original (0x438800 has no bound); the PC bend cars (CrocCar, maggot) never do: every vertex's table position lies
	/// inside the entries the car's extents give, so CoasterCarBend.Bend's clamp is not reached on PC data (COAST-034).
	/// </summary>
	[TestMethod]
	public void EveryVertexOfABendCarLiesInsideTheTablesOfItsExtents()
	{
		var checkedCars = new List<string>();
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromCatalogEntry( entry );
			using var assets = new CoasterAssets( entry, definition );
			for ( var index = 0; index < definition.CarTypes.Types.Count; index++ )
			{
				var type = definition.CarTypes.Types[index];
				if ( !type.Bend )
					continue;
				var rig = assets.GetCar( index );
				Assert.IsNotNull( rig, $"{entry.InfoId} car {index}" );
				var metrics = definition.CarTypeMetrics( index );
				var rear = (int)(metrics.RearExtent / 3f) + 1;
				var count = rear + 1 + (int)(metrics.FrontExtent / 3f) + 1;
				var vertices = 0;
				// the bend body is the one record with the most points; these cars have a single mesh node, so every part is it
				Assert.AreEqual( 1, rig!.Model.Meshes.Count, $"{entry.InfoId}: one mesh node" );
				foreach ( var part in rig.Parts.Where( part => part.Local != null ) )
				{
					foreach ( var vertex in part.Local! )
					{
						var position = (rear * 3f + vertex.Z) / 3f;
						var whole = (int)MathF.Truncate( position );
						Assert.IsTrue( position >= 0 && whole <= count - 2, $"{entry.InfoId} {type.MeshName}: a vertex at table position {position} against {count} entries" );
						vertices++;
					}
				}
				Assert.IsTrue( vertices > 0, $"{entry.InfoId}: bent vertices" );
				checkedCars.Add( type.MeshName );
			}
		}
		CollectionAssert.AreEquivalent( new[] { "CrocCar", "maggot" }, checkedCars, "the Croc and the Maggot flume boats are the PC's bend cars" );
	}
}

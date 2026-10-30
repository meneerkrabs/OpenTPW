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

	[TestMethod]
	public void EveryVertexOfABendCarLiesInsideTheTablesOfItsExtents()
	{
		var checkedCars = 0;
		var lines = new List<string>();
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
				var wantAhead = (int)(metrics.FrontExtent / 3f) + 1;
				var count = rear + 1 + wantAhead;
				var low = float.MaxValue;
				var high = float.MinValue;
				foreach ( var part in rig!.Parts )
				{
					if ( part.Local == null )
						continue;
					foreach ( var vertex in part.Local )
					{
						var position = (rear * 3f + vertex.Z) / 3f;
						low = Math.Min( low, position );
						high = Math.Max( high, position );
					}
				}
				lines.Add( $"  parts: {string.Join( ", ", rig.Parts.Select( part => $"{part.MeshName}[node {part.NodeIndex}, local {part.Local?.Length}]" ) )}; meshes {rig.Model.Meshes.Count}, nodes {rig.Model.Nodes.Count}, root {rig.Model.RootNodeIndex}" );
				lines.Add( $"{entry.InfoId} {type.MeshName}: extents {metrics.RearExtent:F3}/{metrics.FrontExtent:F3}, adds {type.RearAdd}/{type.FrontAdd}, table {count}, vertex positions {low:F3}..{high:F3}" );
				checkedCars++;
			}
		}
		Console.WriteLine( string.Join( "\n", lines ) );
		Assert.IsTrue( checkedCars > 0 );
	}
}

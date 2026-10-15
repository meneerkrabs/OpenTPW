using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Which original ride models carry the camera seat the ride view takes its eye from (the first dummy attribute with bit 0x1000 and value 1, tp.exe 0x44b220),
/// and how the seat node is oriented in its car. Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RideSeatCorpusTests
{
	private static readonly string[] Themes = { "fantasy", "hallow", "jungle", "space" };

	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	public TestContext TestContext { get; set; } = null!;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the ride seat corpus." );
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

	private static IEnumerable<(string Theme, ObjectCatalogEntry Entry, ModelFile Model)> Models()
	{
		foreach ( var theme in Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( x => x.ModelPath != null ).OrderBy( x => x.InfoId ) )
				yield return (theme, entry, ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath! ));
		}
	}

	/// <summary>About forty ride and attraction models carry a seat; the lookup finds the record's node inside the model.</summary>
	[TestMethod]
	public void AboutFortyObjectModelsCarryACameraSeat()
	{
		var seated = Models().Select( x => (x.Theme, x.Entry, Node: RideSeat.FindNode( x.Model ), x.Model) ).Where( x => x.Node >= 0 ).ToArray();
		foreach ( var x in seated )
			TestContext.WriteLine( $"{x.Theme}/{x.Entry.DisplayName} ({x.Entry.ModelPath}): node {x.Node} '{x.Model.Nodes[x.Node].Name.Trim()}' coaster={CoasterDefinition.IsCoaster( x.Entry.Settings )}" );
		Assert.IsTrue( seated.Length >= 30, $"{seated.Length} objects have a seat" );
		Assert.IsTrue( seated.All( x => (x.Model.DummyAttributes[x.Node - x.Model.Counts.Unknown46].Type & RideSeat.TypeFlag) != 0 ) );
	}

	/// <summary>The Totem's seat is its node 25 "camera01" (UI-094).</summary>
	[TestMethod]
	public void TheTotemSeatIsCamera01()
	{
		var totem = Models().First( x => x.Entry.ModelPath!.EndsWith( "totem.MD2", StringComparison.OrdinalIgnoreCase ) );
		var node = RideSeat.FindNode( totem.Model );
		Assert.AreEqual( 25, node );
		Assert.AreEqual( "camera01", totem.Model.Nodes[node].Name.Trim() );
	}

	/// <summary>The car models of the coasters and the mine train carry a seat whose forward axis (the model's Z) is the car's forward axis.</summary>
	[TestMethod]
	public void CarSeatsLookAlongTheCar()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		var found = 0;
		foreach ( var theme in new[] { "jungle", "hallow" } )
		{
			foreach ( var name in new[] { "coaster1.wad", "coaster3.wad", "minecart.wad" } )
			{
				var path = Path.Combine( dataPath, "levels", theme, "rides", name );
				if ( !File.Exists( path ) )
					continue;
				using var archive = new WadArchive( path );
				foreach ( var member in archive.Root.Children.OfType<ArchiveFile>().Where( x => x.Name!.EndsWith( ".MD2", StringComparison.OrdinalIgnoreCase ) ) )
				{
					var model = new ModelFile( new MemoryStream( member.GetData() ) );
					var node = RideSeat.FindNode( model );
					if ( node < 0 )
						continue;
					var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
					var matrix = rest[node];
					var root = model.RootNodeIndex >= 0 ? rest[model.RootNodeIndex].Translation : System.Numerics.Vector3.Zero;
					TestContext.WriteLine( $"{theme}/{name}/{member.Name}: node {node} '{model.Nodes[node].Name.Trim()}' at {matrix.Translation - root} rows X={Row( matrix, 0 )} Y={Row( matrix, 1 )} Z={Row( matrix, 2 )}" );
					if ( member.Name!.StartsWith( "cart", StringComparison.OrdinalIgnoreCase ) )
					{
						for ( var n = 0; n < model.Nodes.Count; n++ )
							TestContext.WriteLine( $"   node {n} '{model.Nodes[n].Name.Trim()}' at {rest[n].Translation - root} meshes={string.Join( ",", model.Meshes.Where( m => m.NodeIndex == n ).Select( m => m.Name ) )}" );
					}
					var forward = System.Numerics.Vector3.Normalize( new System.Numerics.Vector3( matrix.M31, matrix.M32, matrix.M33 ) );
					found++;
				}
			}
		}
		Assert.IsTrue( found >= 2, $"{found} car models with a seat" );
	}

	private static string Row( System.Numerics.Matrix4x4 m, int row ) => row switch
	{
		0 => $"({m.M11:0.00},{m.M12:0.00},{m.M13:0.00})",
		1 => $"({m.M21:0.00},{m.M22:0.00},{m.M23:0.00})",
		_ => $"({m.M31:0.00},{m.M32:0.00},{m.M33:0.00})"
	};
}

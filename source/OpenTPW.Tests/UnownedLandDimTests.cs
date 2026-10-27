using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The darkening of the land outside the park (tp.exe 0x450D10, 0x56F670, 0x44DC28): the cell rule, the clamped map cell, the lighting option and the corner flags of the procedural mesh. Synthetic level, no game data.</summary>
[TestClass]
[DoNotParallelize]
public class UnownedLandDimTests
{
	private const int Size = 16;

	private sealed class Pages : IProceduralPages
	{
		public byte[] Scratch { get; } = new byte[Size * Size * 4];
		public int PageOf( int slot ) => 0;
		public (float U0, float V0, float U1, float V1) CoordinatesOf( int slot ) => (0.1f, 0.2f, 0.3f, 0.4f);
		public void Upload( int slot, byte[] rgba ) { }
	}

	[TestMethod]
	public void OnlyUnownedCellsOutsideTheEntranceAreaDim()
	{
		Assert.IsFalse( UnownedLandDim.Dims( ParkCellFlags.None ) );
		Assert.IsTrue( UnownedLandDim.Dims( ParkCellFlags.Unowned ) );
		Assert.IsTrue( UnownedLandDim.Dims( ParkCellFlags.Unowned | ParkCellFlags.NoModify ) );
		Assert.IsFalse( UnownedLandDim.Dims( ParkCellFlags.Unowned | ParkCellFlags.EntranceArea ), "0x80 clears the bit again (0x450DAC)" );
		Assert.IsFalse( UnownedLandDim.Dims( ParkCellFlags.EntranceArea ) );
	}

	[TestMethod]
	public void TheMapCellIsClampedToTheMap()
	{
		Assert.AreEqual( (0, 0), UnownedLandDim.MapCell( -3, -1, 95, 84 ) );
		Assert.AreEqual( (94, 83), UnownedLandDim.MapCell( 95, 84, 95, 84 ), "the heightfield's extra column and row read the map's last" );
		Assert.AreEqual( (94, 83), UnownedLandDim.MapCell( 200, 300, 95, 84 ) );
		Assert.AreEqual( (10, 20), UnownedLandDim.MapCell( 10, 20, 95, 84 ) );
	}

	[TestMethod]
	public void TheLightingOptionOnlyTurnsTheDimmingOffAtZero()
	{
		Assert.IsTrue( UnownedLandDim.Enabled( 1 ) );
		Assert.IsFalse( UnownedLandDim.Enabled( 0 ) );
		Assert.IsTrue( UnownedLandDim.Enabled( 2 ), "other values leave bit 0x800 as the start had it, clear" );
	}

	[TestMethod]
	public void TheProceduralMeshFlagsTheCornersOfADimCellAndRewritesWhenTheAnswersChange()
	{
		var data = SyntheticLnd.Build( 3, 3, [48, 40, 40, 90, 68, 40, 97, 30, 55, 50], ( x, y, channel ) => channel == 0 ? (byte)255 : (byte)(x * 11 + y * 7 + channel),
			( size, image, x, y, channel ) => (byte)(size + image * 5 + x * 3 + y * 11 + channel) );
		var field = new ModelHeightfield( 5, 4, 10, 10, 0, 0, Array.Empty<uint>(), new float[6 * 5], new uint[20] );
		var terrain = new ProceduralTerrain( field, new LndFile( new MemoryStream( data ) ), new ProceduralTerrainRectangle( 0, 0, 3, 2 ), new ProceduralTerrainSettings( Size, 40 ), 2, new Pages() );
		var view = Matrix4x4.CreateLookAt( new System.Numerics.Vector3( 0, 0, 40 ), new System.Numerics.Vector3( 0, 0, 0 ), System.Numerics.Vector3.UnitY );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( 90 * MathF.PI / 180, 4f / 3, 0.1f, 1000 );
		terrain.Refresh( view, projection, null );
		Assert.IsTrue( terrain.Vertices.Take( terrain.DrawnCells * 4 ).All( vertex => (vertex.MatFlags & TextureSlotAddressing.LandDimFlag) == 0 ) );
		terrain.LandDim = ( x, z ) => x == 2;
		Assert.IsFalse( terrain.Refresh( view, projection, null ), "nothing told the terrain: the mesh stays" );
		terrain.MarkMeshStale();
		Assert.IsTrue( terrain.Refresh( view, projection, null ) );
		var cells = terrain.VisibleCells.ToList();
		for ( var index = 0; index < cells.Count; index++ )
		{
			var expected = cells[index].X == 2;
			for ( var corner = 0; corner < 4; corner++ )
				Assert.AreEqual( expected, (terrain.Vertices[index * 4 + corner].MatFlags & TextureSlotAddressing.LandDimFlag) != 0, $"cell {cells[index]} corner {corner}" );
		}
		Assert.IsTrue( cells.Any( cell => cell.X == 2 ) && cells.Any( cell => cell.X != 2 ) );
	}

	[TestMethod]
	public void AStaticCellTakesTheFlagOnEveryCorner()
	{
		var field = new ModelHeightfield( 5, 4, 10, 10, 0, 0, Array.Empty<uint>(), new float[6 * 5], new uint[20] );
		var vertices = new List<Vertex>();
		OriginalTerrain.AddCell( vertices, new List<uint>(), field, 1, 1, 0, 0, TextureSlotAddressing.LandDimFlag );
		Assert.IsTrue( vertices.All( vertex => vertex.MatFlags == TextureSlotAddressing.LandDimFlag ) );
	}

	[TestMethod]
	public void TheDimCellsAreTheUnownedOnesOutsideAParksFence()
	{
		var cells = new ParkCellMap( 64, 64 );
		var land = ParkLand.FromClicks( new ParkLandSetup( (21, 11), (13, 11), new (int X, int Y)[] { (0, 0), (4, 0), (4, 10), (-12, 10), (-12, 0), (-8, 0) } ) );
		land.ApplyToNewPark( cells );
		cells.SetLand( land );
		Assert.IsFalse( UnownedLandDim.Dims( cells.FlagsAt( 17, 16 ) ), "inside the rectangle park" );
		Assert.IsTrue( UnownedLandDim.Dims( cells.FlagsAt( 40, 40 ) ), "far outside" );
		Assert.IsTrue( UnownedLandDim.Dims( cells.FlagsAt( 17, 30 ) ), "south of the fence" );
		var dim = Enumerable.Range( 0, 64 ).SelectMany( x => Enumerable.Range( 0, 64 ).Select( y => (x, y) ) ).Count( cell => UnownedLandDim.Dims( cells.FlagsAt( cell.x, cell.y ) ) );
		Assert.IsTrue( dim > 64 * 64 / 2 && dim < 64 * 64 );
	}

	[TestMethod]
	public void TheLevelIsRoundedClampedThenHalved()
	{
		Assert.AreEqual( 0, UnownedLandDim.Level( 0f ) );
		Assert.AreEqual( 127, UnownedLandDim.Level( 1f ), "255 >> 1" );
		Assert.AreEqual( 127, UnownedLandDim.Level( 1.6f ), "light above 1 clamps to 255 first (halving the unclamped 408 would give 204)" );
		Assert.AreEqual( 0, UnownedLandDim.Level( -0.5f ) );
		Assert.AreEqual( 51, UnownedLandDim.Level( 0.4f ), "102 >> 1" );
		Assert.AreEqual( 0, UnownedLandDim.Level( 1f / 255 ), "level 1 halves to 0" );
		Assert.AreEqual( 64, UnownedLandDim.Level( 0.5f ), "127.5 rounds to the even 128, then 64" );
	}

	[TestMethod]
	public void TheShaderConstantIsTheVertexFlagAndItsArithmeticIsTheOriginals()
	{
		var text = File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", "test.shader" ) ).ReplaceLineEndings( "\n" );
		StringAssert.Contains( text, $"const uint FLAG_LAND_DIM = 0x{TextureSlotAddressing.LandDimFlag:X8}u;" );
		StringAssert.Contains( text, "vShading = floor( clamp( roundEven( vShading * 255.0 ), 0.0, 255.0 ) * 0.5 ) / 255.0;" );
	}

	[TestMethod]
	public void WithoutOriginalDetailDataTheDimmingStaysOnLikeTheOriginalsZeroedFlags()
	{
		Assert.IsTrue( UnownedLandDim.Enabled( GraphicsSettingsService.FallbackDetail.Lighting ) );
		Assert.IsFalse( UnownedLandDim.Enabled( new OriginalDetailSettings().Lighting ), "a bare record is LIGHTING 0, which is why the fallback sets it" );
	}

	[TestMethod]
	public void LightingZeroSelectsTheUnlitConstantAndNoDimming()
	{
		Assert.IsTrue( UnownedLandDim.Unlit( 0 ) );
		Assert.IsFalse( UnownedLandDim.Unlit( 1 ) );
		Assert.IsFalse( UnownedLandDim.Unlit( 2 ) );
		Assert.AreEqual( 0xFFFFFFFFu, UnownedLandDim.UnlitColour, "the light object's +0x2C (0x574013)" );
		for ( var lighting = 0; lighting < 3; lighting++ )
			Assert.AreNotEqual( UnownedLandDim.Unlit( lighting ), UnownedLandDim.Enabled( lighting ), "bit 0x800 stops the dimming and selects the constant together" );
		Assert.IsFalse( UnownedLandDim.Unlit( GraphicsSettingsService.FallbackDetail.Lighting ), "no data: lit, as the zeroed flags" );
	}

	[TestMethod]
	public void TheShaderShadesAnUnlitTerrainCornerWithWhiteAndTheEveningTintOnTop()
	{
		var text = File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", "test.shader" ) ).ReplaceLineEndings( "\n" );
		StringAssert.Contains( text, $"const uint FLAG_TERRAIN_UNLIT = 0x{TextureSlotAddressing.TerrainUnlitFlag:X8}u;" );
		StringAssert.Contains( text, "if ( terrainUnlit )\n            vShading = vec3( 1.0 );" );
		StringAssert.Contains( text, "|| terrainUnlit ) && vAmbientTint.x", "the extension's night tint applies on top" );
		Assert.AreEqual( 0u, TextureSlotAddressing.TerrainUnlitFlag & (TextureSlotAddressing.LandDimFlag | TextureSlotAddressing.SelfLitFlag | TextureSlotAddressing.LightSourceFlag | TextureSlotAddressing.SlushFlag | TextureSlotAddressing.ClampFlag | 0xFFFFu) );
	}

	[TestMethod]
	public void TheProceduralMeshCarriesTheUnlitFlagOnEveryCorner()
	{
		var data = SyntheticLnd.Build( 3, 3, [48, 40, 40, 90, 68, 40, 97, 30, 55, 50], ( x, y, channel ) => channel == 0 ? (byte)255 : (byte)(x * 11 + y * 7 + channel),
			( size, image, x, y, channel ) => (byte)(size + image * 5 + x * 3 + y * 11 + channel) );
		var field = new ModelHeightfield( 5, 4, 10, 10, 0, 0, Array.Empty<uint>(), new float[6 * 5], new uint[20] );
		var terrain = new ProceduralTerrain( field, new LndFile( new MemoryStream( data ) ), new ProceduralTerrainRectangle( 0, 0, 3, 2 ), new ProceduralTerrainSettings( Size, 40 ), 2, new Pages() ) { Unlit = true };
		var view = Matrix4x4.CreateLookAt( new System.Numerics.Vector3( 0, 0, 40 ), new System.Numerics.Vector3( 0, 0, 0 ), System.Numerics.Vector3.UnitY );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( 90 * MathF.PI / 180, 4f / 3, 0.1f, 1000 );
		terrain.Refresh( view, projection, null );
		Assert.IsTrue( terrain.DrawnCells > 0 );
		Assert.IsTrue( terrain.Vertices.Take( terrain.DrawnCells * 4 ).All( vertex => (vertex.MatFlags & TextureSlotAddressing.TerrainUnlitFlag) != 0 ) );
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !Directory.Exists( Path.Combine( directory.FullName, "content", "shaders" ) ) )
			directory = directory.Parent;
		return directory?.FullName ?? throw new InvalidOperationException( "repository root not found" );
	}
}

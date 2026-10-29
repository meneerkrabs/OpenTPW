using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>The harbour's waves and flags (tp.exe <c>0x53C3F0</c>, <see cref="HarbourDynamics"/>): the wave and cloth grids, the sea's patches, the flags and the phases.</summary>
[TestClass]
public class HarbourDynamicsTests
{
	private const float TableTolerance = 0.002f;

	[TestMethod]
	public void TheWaveGridRollsATravellingCosineAcrossTheSea()
	{
		var (positions, normals) = HarbourDynamics.WaveGrid( 0 );
		Assert.AreEqual( (16, 19), (positions.GetLength( 0 ), positions.GetLength( 1 )) );
		// 0x53D380: the points stand 20 apart, x across a row, z down the rows; at the origin the angle is 0, the crest 1.7 high and the normal straight up
		Assert.AreEqual( new N.Vector3( 0, 1.7f, 0 ), positions[0, 0] );
		Assert.AreEqual( new N.Vector3( 300, positions[15, 18].Y, 360 ), positions[15, 18] );
		Assert.AreEqual( N.Vector3.UnitY, normals[0, 0] );
		var k = 2 * Math.PI / 3;
		// the direction's sine and cosine come from the 4096-entry table too (sin and cos of 114 steps, not of exactly 10 degrees)
		double sin = OriginalSineTable.Sin( 0.17453300f ), cos = OriginalSineTable.Cos( 0.17453300f );
		foreach ( var phase in new[] { 0f, 1.2f, 7.6f } )
		{
			(positions, normals) = HarbourDynamics.WaveGrid( phase );
			for ( var column = 0; column < 16; column += 5 )
			{
				for ( var row = 0; row < 19; row += 6 )
				{
					var angle = column * 20 * k * cos - row * 20 * k * sin - phase * 0.5;
					Assert.AreEqual( 1.7 * Math.Cos( angle ), positions[column, row].Y, 1.7 * TableTolerance + 1e-3, $"({column}, {row}) at {phase}" );
					var slope = (float)Math.Sin( angle );
					var expected = N.Vector3.Normalize( new N.Vector3( slope, 1, slope ) );
					Assert.IsTrue( N.Vector3.Distance( expected, normals[column, row] ) < 0.01f, $"normal of ({column}, {row}) at {phase}" );
				}
			}
		}
	}

	[TestMethod]
	public void TheClothHangsFromThePoleAndWavesTowardItsFreeEnd()
	{
		foreach ( var phase in new[] { 0f, 3.3f, 11f } )
		{
			var (positions, normals) = HarbourDynamics.ClothGrid( phase );
			Assert.AreEqual( (7, 5), (positions.GetLength( 0 ), positions.GetLength( 1 )) );
			// 0x53D570: the first column's reach is 0, so it neither waves nor sags: it hangs down the pole, 1.8 a row, and faces straight out
			for ( var row = 0; row < 5; row++ )
			{
				Assert.AreEqual( new N.Vector3( 0, -1.8f * row, 0 ), positions[0, row], $"row {row} at {phase}" );
				Assert.AreEqual( N.Vector3.UnitZ, normals[0, row] );
			}
			// the reach grows with x up to (7 - 4) x 1.8 and stays 1 beyond: the out-of-plane wave is at most 0.3
			Assert.IsTrue( Enumerable.Range( 0, 7 ).SelectMany( column => Enumerable.Range( 0, 5 ).Select( row => positions[column, row] ) ).All( point => Math.Abs( point.Z ) <= 0.3f + 1e-4f ) );
		}
		var (still, _) = HarbourDynamics.ClothGrid( 0 );
		var (moved, _) = HarbourDynamics.ClothGrid( 2 );
		Assert.AreNotEqual( still[6, 2], moved[6, 2], "the free end waves with the phase" );
		// the free end (x = 10.8) sags by 0.02 x^2 below the row
		Assert.AreEqual( 2 * 0.3 * Math.Cos( 2 * Math.PI * 0.25 * (Math.Sqrt( 10.8 * 10.8 + 9.0 * 9.0 )) ) - 0.02 * 10.8 * 10.8, still[6, 0].Y, 0.01 );
	}

	[TestMethod]
	public void TheSeaIsTwoPatchesOfTheGridFromTheWaterStartInTheTablesTextures()
	{
		var start = new N.Vector3( 380, -5, -180 );
		var (positions, normals) = HarbourDynamics.WaveGrid( 0.4f );
		var table = HarbourDynamics.SeaTextureTable();
		var faces = HarbourDynamics.SeaFaces( start, positions, normals, table ).ToList();
		// 0x53CAB0 twice: 10 x 11 cells from the grid's corner and 4 x 6 from column 10, 134 faces (the WaterMesh's 0x86)
		Assert.AreEqual( 134, faces.Count );
		var first = faces[0];
		Assert.AreEqual( (positions[0, 0] + start, positions[1, 0] + start, positions[0, 1] + start, positions[1, 1] + start), (first.A, first.B, first.C, first.D) );
		Assert.AreEqual( (normals[0, 0], normals[1, 0], normals[0, 1], normals[1, 1]), (first.NormalA, first.NormalB, first.NormalC, first.NormalD) );
		Assert.AreEqual( (new Vector2( 0, 0 ), new Vector2( 1, 0 ), new Vector2( 0, 1 ), new Vector2( 1, 1 )), (first.UvA, first.UvB, first.UvC, first.UvD) );
		var second = faces[110];
		Assert.AreEqual( positions[10, 0] + start, second.A, "the second patch starts at column 10" );
		Assert.AreEqual( positions[14, 6] + start, faces[^1].D, "and ends at column 14, row 6" );
		// each patch counts its faces from the table's start
		CollectionAssert.AreEqual( table.ToArray(), faces.Take( 110 ).Select( face => face.Slot ).ToArray() );
		CollectionAssert.AreEqual( table.Take( 24 ).ToArray(), faces.Skip( 110 ).Select( face => face.Slot ).ToArray() );
	}

	[TestMethod]
	public void TheSeaTexturesAreTheRuntimesRandModThree()
	{
		var table = HarbourDynamics.SeaTextureTable();
		Assert.AreEqual( 110, table.Length );
		// the C runtime's rand() from seed 1: 41, 18467, 6334, 26500, 19169
		CollectionAssert.AreEqual( new[] { 41 % 3, 18467 % 3, 6334 % 3, 26500 % 3, 19169 % 3 }, table.Take( 5 ).ToArray() );
		Assert.IsTrue( table.All( slot => slot is >= 0 and < 3 ) );
	}

	[TestMethod]
	public void AFlagIsSixByFourCellsOfFrontAndBackTurnedToTheWindAtItsPole()
	{
		var (positions, normals) = HarbourDynamics.ClothGrid( 1.5f );
		var pole = HarbourDynamics.FlagPoles[0];
		var faces = HarbourDynamics.FlagFaces( pole, positions, normals, upper: true, angle: 0 ).ToList();
		Assert.AreEqual( 48, faces.Count );
		// with no turn a corner sits 0.5 out along x from the pole top
		var front = faces[0];
		var back = faces[1];
		Assert.AreEqual( pole + positions[0, 0] + new N.Vector3( 0.5f, 0, 0 ), back.A );
		Assert.AreEqual( (back.B, back.A, back.D, back.C), (front.A, front.B, front.C, front.D), "the front face runs the back face's corners mirrored" );
		Assert.AreEqual( (-back.NormalB, -back.NormalA), (front.NormalA, front.NormalB), "with the normals reversed" );
		// the upper texture runs from v 1 down 0.55 over the four rows; u in sixths
		Assert.AreEqual( (new Vector2( 0, 1 ), new Vector2( 1 / 6f, 1 ), new Vector2( 0, 1 - 0.55f / 4 ), new Vector2( 1 / 6f, 1 - 0.55f / 4 )), (back.UvA, back.UvB, back.UvC, back.UvD) );
		var lower = HarbourDynamics.FlagFaces( pole, positions, normals, upper: false, angle: 0 ).ToList();
		Assert.AreEqual( 0.55f, lower[1].UvA.Y );
		Assert.AreEqual( 0.55f - 0.55f, lower[^1].UvD.Y, 1e-5f, "the lower texture ends at v 0" );
		// a quarter turn swings the flag from +x to +z about the pole
		var turned = HarbourDynamics.FlagFaces( pole, positions, normals, upper: true, angle: MathF.PI / 2 ).ToList();
		var offset = turned[1].A - pole;
		Assert.AreEqual( 0, offset.X, 0.01f );
		Assert.AreEqual( 0.5f, offset.Z, 0.01f );
	}

	[TestMethod]
	public void AFrameHasTheSeaAndEightAlternatingFlags()
	{
		var faces = HarbourDynamics.Faces( new N.Vector3( 380, -5, -180 ), HarbourDynamics.SeaTextureTable(), 0, 0, ParkWeather.InitialWindDirection );
		Assert.AreEqual( 134 + 8 * 48, faces.Count );
		Assert.IsTrue( faces.Skip( 134 ).All( face => face.Slot == HarbourDynamics.FlagSlot ) );
		// the flags alternate the upper (texture 18) and lower (19) half of gte_lgo1, starting with the upper
		var tops = Enumerable.Range( 0, 8 ).Select( flag => faces[134 + flag * 48 + 1].UvA.Y ).ToArray();
		CollectionAssert.AreEqual( new[] { 1f, 0.55f, 1f, 0.55f, 1f, 0.55f, 1f, 0.55f }, tops );
	}

	[TestMethod]
	public void EveryFaceIsDrawnFromTheSideItsNormalsPointTo()
	{
		var field = new ModelHeightfield( 96, 85, 10, 10, 0, 0, Array.Empty<uint>(), new float[97 * 86], new uint[96 * 85] );
		var faces = HarbourDynamics.Faces( new N.Vector3( 380, -5, -180 ), HarbourDynamics.SeaTextureTable(), 3, 5, 135 );
		var vertices = HarbourDynamics.Vertices( field, faces );
		var indices = HarbourDynamics.Indices( faces.Count );
		Assert.AreEqual( faces.Count * 4, vertices.Length );
		CollectionAssert.AreEqual( new uint[] { 2, 1, 0, 1, 2, 3, 6, 5, 4, 5, 6, 7 }, indices.Take( 12 ).ToArray() );
		// the engine placement mirrors (y and z swap), so a kept triangle turns against its normals in engine coordinates; the sea's normals point up
		static N.Vector3 V( Vector3 value ) => new( value.X, value.Y, value.Z );
		for ( var face = 0; face < faces.Count; face++ )
		{
			var (a, b, c) = (V( vertices[indices[face * 6]].Position ), V( vertices[indices[face * 6 + 1]].Position ), V( vertices[indices[face * 6 + 2]].Position ));
			var normal = vertices.Skip( face * 4 ).Take( 4 ).Aggregate( N.Vector3.Zero, ( sum, vertex ) => sum + V( vertex.Normal ) );
			Assert.IsTrue( N.Vector3.Dot( N.Vector3.Cross( b - a, c - a ), normal ) <= 0, $"face {face}" );
		}
		Assert.IsTrue( vertices.Take( 134 * 4 ).All( vertex => vertex.Normal.Z > 0.5f ), "the sea faces up (engine z)" );
	}

	[TestMethod]
	public void ThePhasesStepEveryThirtiethOfASecondByTheGameSpeedAndStandStillWhilePaused()
	{
		var phases = new HarbourPhases();
		phases.Advance( 1f / 30 + 1e-4f, paused: false, timeScale: 1 );
		Assert.AreEqual( (0.4f, 0.4f), (phases.Waves, phases.Flags) );
		phases.Advance( 1f, paused: true, timeScale: 1 );
		Assert.AreEqual( 0.4f, phases.Waves, "paused" );
		phases.Advance( 2f / 30, paused: false, timeScale: 2 );
		Assert.AreEqual( 0.4f + 2 * 0.8f, phases.Waves, 1e-4f, "two steps at double speed" );
		phases.Advance( 10f, paused: false, timeScale: 1 );
		Assert.AreEqual( 2f + 7 * 0.4f, phases.Waves, 0.41f, "at most a quarter second at once" );
	}
}

/// <summary>The harbour against the original data (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
public class HarbourDataTests
{
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original level data." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	/// <summary>
	/// Every theme's water starts at (380, -5, -180), above the terrain's static sea (y -11 to -10), and its terrain stands a flag pole under each of the eight flags of tp.exe's table
	/// (<c>0x763CF4</c>); the dynamic textures the meshes take are in the generic folder.
	/// </summary>
	[TestMethod]
	public void EveryThemesHarbourHasItsWaterStartFlagPolesAndTextures()
	{
		foreach ( var theme in new[] { "jungle", "hallow", "space", "fantasy" } )
		{
			Assert.AreEqual( new N.Vector3( 380, -5, -180 ), HarbourDynamics.LoadWaterStart( theme ), theme );
			var terrain = new ModelFile( $"/levels/{theme}/terrain/base.MD2" );
			var poles = terrain.Nodes.Where( node => node.Name.StartsWith( "flag_pole", StringComparison.OrdinalIgnoreCase ) )
				.Select( node => (X: MathF.Round( node.Transform.Translation.X, 2 ), Z: MathF.Round( node.Transform.Translation.Z, 2 )) ).ToHashSet();
			Assert.AreEqual( 8, poles.Count, theme );
			foreach ( var flag in HarbourDynamics.FlagPoles )
				Assert.IsTrue( poles.Contains( (flag.X, flag.Z) ), $"{theme}: a pole under the flag at ({flag.X}, {flag.Z})" );
		}
		foreach ( var name in new[] { "gby_sur1", "gby_sur2", "gby_sur3", "gte_lgo1" } )
			Assert.IsTrue( FileSystem.FileExists( $"/generic/dynamic/textures/{name}.wct" ), name );
	}
}

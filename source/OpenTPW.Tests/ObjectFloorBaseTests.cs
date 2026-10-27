using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The skirt base of a placed object without game data (docs/OBJECTS.md, "Floors"): the composite tail of tp.exe's build routine 0x55F780 against the original on synthetic levels and art
/// (<see cref="ProceduralTerrainComposeGolden"/>), the orientation table, the skirt words and their turns, the targets of the entries and the landscape words an object writes and restores.
/// </summary>
[TestClass]
public class ObjectFloorBaseTests
{
	private static string Sha( ReadOnlySpan<byte> bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();

	/// <summary>The top byte of state × 1664525 + 1013904223 (mod 2³²), as the generator of the goldens (terrain_reading.Lcg).</summary>
	private sealed class Lcg( uint seed )
	{
		private uint state = seed;

		public byte Next()
		{
			state = unchecked( state * 1664525u + 1013904223u );
			return (byte)(state >> 24);
		}
	}

	/// <summary>The art of the goldens (terrain_reading.synthetic_art) as RGBA: blue, green and red from the generator, the alpha by mode.</summary>
	internal static byte[] SyntheticArt( int seed, int width, int height, string alphaMode )
	{
		var lcg = new Lcg( (uint)seed );
		var rgba = new byte[width * height * 4];
		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				var offset = (y * width + x) * 4;
				var blue = lcg.Next();
				var green = lcg.Next();
				var red = lcg.Next();
				rgba[offset] = red;
				rgba[offset + 1] = green;
				rgba[offset + 2] = blue;
				rgba[offset + 3] = alphaMode switch { "zero" => 0, "full" => 255, "mid" => 127, "ramp" => (byte)(x * 255 / Math.Max( 1, width - 1 )), "path" => (byte)(x < 3 || y < 3 || x >= width - 3 || y >= height - 3 ? 0 : 252), _ => lcg.Next() };
			}
		}
		return rgba;
	}

	private static (int Width, int Height) ArtSize( string kind, int size ) => kind switch
	{
		"n" => (size, size),
		"double" => (2 * size, 2 * size),
		"odd" => (size + 7, size + 13),
		"big" => (128, 128),
		_ => (size / 2, size / 2)
	};

	[TestMethod]
	public void TheCompositeMatchesTheOriginalOnSyntheticLevelsAndArt()
	{
		var levels = new Dictionary<int, LndFile>();
		foreach ( var (seed, size, x, z, kind, artSeed, word, alpha, sha) in ProceduralTerrainComposeGolden.Cases )
		{
			if ( !levels.TryGetValue( seed, out var lnd ) )
				levels[seed] = lnd = SyntheticTerrain.Load( seed, "C", false );
			var texturer = new ProceduralTerrainTexturer( lnd, size );
			var baseRgba = new byte[texturer.TextureBytes];
			texturer.BuildRgba( x, z, baseRgba );
			var (width, height) = ArtSize( kind, size );
			var rgba = new byte[texturer.TextureBytes];
			ProceduralTerrainTexturer.Compose( baseRgba, size, SyntheticArt( artSeed, width, height, alpha ), width, height, word, rgba );
			// the original writes [0, R, G, B] per pixel
			var original = new byte[rgba.Length];
			for ( var index = 0; index < rgba.Length; index += 4 )
			{
				original[index + 1] = rgba[index];
				original[index + 2] = rgba[index + 1];
				original[index + 3] = rgba[index + 2];
				Assert.AreEqual( 255, rgba[index + 3] );
			}
			Assert.AreEqual( sha, Sha( original ), $"seed {seed} N={size} cell ({x}, {z}) art {kind} word 0x{word:X} alpha {alpha}" );
		}
	}

	[TestMethod]
	public void TheGoldenCoversEveryOrientationAlphaModeAndArtSize()
	{
		var cases = ProceduralTerrainComposeGolden.Cases;
		CollectionAssert.IsSubsetOf( new[] { 0x00, 0x08, 0x10, 0x20, 0x40, 0x48, 0x50, 0x60 }, cases.Select( c => c.Word & 0x78 ).Distinct().ToArray() );
		CollectionAssert.AreEquivalent( new[] { "zero", "full", "mid", "ramp", "random", "path" }, cases.Select( c => c.Alpha ).Distinct().ToArray() );
		CollectionAssert.AreEquivalent( new[] { "n", "double", "odd", "big", "small" }, cases.Select( c => c.ArtSize ).Distinct().ToArray() );
		CollectionAssert.AreEquivalent( new[] { 16, 32, 64 }, cases.Select( c => c.Size ).Distinct().ToArray() );
	}

	[TestMethod]
	public void TheBaseIsReadInTheEightOrientationsOfTheWord()
	{
		const int n = 4;
		// output (row Y, column X) reads the base pixel (row, column): the table of docs/OBJECTS.md
		var expected = new (int Word, Func<int, int, (int, int)> Read)[]
		{
			(0x00, ( y, x ) => (y, x)),
			(0x08, ( y, x ) => (n - 1 - x, y)),
			(0x10, ( y, x ) => (n - 1 - y, n - 1 - x)),
			(0x20, ( y, x ) => (x, n - 1 - y)),
			(0x40, ( y, x ) => (x, y)),
			(0x48, ( y, x ) => (n - 1 - y, x)),
			(0x50, ( y, x ) => (n - 1 - x, n - 1 - y)),
			(0x60, ( y, x ) => (y, n - 1 - x))
		};
		foreach ( var (word, read) in expected )
			for ( var y = 0; y < n; y++ )
				for ( var x = 0; x < n; x++ )
					Assert.AreEqual( read( y, x ), ProceduralTerrainTexturer.BaseTexel( word, n, y, x ), $"word 0x{word:X} at ({y}, {x})" );
		// every orientation is a permutation of the base's pixels
		foreach ( var (word, _) in expected )
			Assert.AreEqual( n * n, Enumerable.Range( 0, n * n ).Select( index => ProceduralTerrainTexturer.BaseTexel( word, n, index / n, index % n ) ).Distinct().Count(), $"0x{word:X}" );
	}

	[TestMethod]
	public void TheBitsOfTheWordAreTestedInTheOriginalsOrderAndTheRestIsIgnored()
	{
		const int n = 8;
		// 0x40 first, then 0x08, 0x10, 0x20: a word with two orientation bits reads as the first of them, and the other bits of the word (0x281, 0x2 and 0x4) change nothing
		Assert.AreEqual( ProceduralTerrainTexturer.BaseTexel( 0x08, n, 3, 5 ), ProceduralTerrainTexturer.BaseTexel( 0x18, n, 3, 5 ) );
		Assert.AreEqual( ProceduralTerrainTexturer.BaseTexel( 0x08, n, 3, 5 ), ProceduralTerrainTexturer.BaseTexel( 0x28, n, 3, 5 ) );
		Assert.AreEqual( ProceduralTerrainTexturer.BaseTexel( 0x10, n, 3, 5 ), ProceduralTerrainTexturer.BaseTexel( 0x30, n, 3, 5 ) );
		Assert.AreEqual( ProceduralTerrainTexturer.BaseTexel( 0x48, n, 3, 5 ), ProceduralTerrainTexturer.BaseTexel( 0x58, n, 3, 5 ) );
		Assert.AreEqual( ProceduralTerrainTexturer.BaseTexel( 0x48, n, 3, 5 ), ProceduralTerrainTexturer.BaseTexel( 0x78, n, 3, 5 ) );
		Assert.AreEqual( ProceduralTerrainTexturer.BaseTexel( 0x50, n, 3, 5 ), ProceduralTerrainTexturer.BaseTexel( 0x281 | 0x50, n, 3, 5 ) );
		Assert.AreEqual( ProceduralTerrainTexturer.BaseTexel( 0, n, 3, 5 ), ProceduralTerrainTexturer.BaseTexel( 0x281 | 6, n, 3, 5 ) );
	}

	[TestMethod]
	public void AnOpaquePixelKeepsOneTwoFiftySixthOfTheBaseAndATransparentOneTheWholeBase()
	{
		const int n = 16;
		var baseRgba = new byte[n * n * 4];
		for ( var index = 0; index < baseRgba.Length; index += 4 )
		{
			baseRgba[index] = 200;
			baseRgba[index + 1] = 100;
			baseRgba[index + 2] = 0;
			baseRgba[index + 3] = 255;
		}
		var art = new byte[n * n * 4];
		var output = new byte[n * n * 4];
		// transparent art of any colour: the base
		for ( var index = 0; index < art.Length; index += 4 )
			(art[index], art[index + 1], art[index + 2], art[index + 3]) = (1, 2, 3, 0);
		ProceduralTerrainTexturer.Compose( baseRgba, n, art, n, n, 0, output );
		Assert.AreEqual( (200, 100, 0, 255), (output[0], output[1], output[2], output[3]) );
		// opaque white art: base + ((255 - base) * 255 >> 8), one 256th of the way short
		for ( var index = 0; index < art.Length; index += 4 )
			(art[index], art[index + 1], art[index + 2], art[index + 3]) = (255, 255, 255, 255);
		ProceduralTerrainTexturer.Compose( baseRgba, n, art, n, n, 0, output );
		Assert.AreEqual( (254, 254, 254, 255), (output[0], output[1], output[2], output[3]) );
		// opaque black art over a base of 200: 200 + ((0 - 200) * 255 >> 8) = 200 - 200 = 0 (the shift floors a negative product)
		for ( var index = 0; index < art.Length; index += 4 )
			(art[index], art[index + 1], art[index + 2], art[index + 3]) = (0, 0, 0, 255);
		ProceduralTerrainTexturer.Compose( baseRgba, n, art, n, n, 0, output );
		Assert.AreEqual( (0, 0, 0, 255), (output[0], output[1], output[2], output[3]), "200 + ((0 - 200) * 255 >> 8) = 200 - 200: the shift floors a negative product" );
	}

	[TestMethod]
	public void TheArtIsSampledByIntegerDivisionsAndAnArtSmallerThanTheTextureRepeatsItsFirstTexel()
	{
		const int n = 16;
		var baseRgba = new byte[n * n * 4];
		var output = new byte[n * n * 4];
		// 40 × 24 art: steps 2 across and 1 down; the pixel (Y, X) is read at row Y, column 2 X
		var art = new byte[40 * 24 * 4];
		for ( var y = 0; y < 24; y++ )
			for ( var x = 0; x < 40; x++ )
				(art[(y * 40 + x) * 4], art[(y * 40 + x) * 4 + 1], art[(y * 40 + x) * 4 + 2], art[(y * 40 + x) * 4 + 3]) = ((byte)x, (byte)y, 0, 255);
		ProceduralTerrainTexturer.Compose( baseRgba, n, art, 40, 24, 0, output );
		var pixel = (5 * n + 7) * 4;
		Assert.AreEqual( (14 * 255 >> 8, 5 * 255 >> 8), ((int)output[pixel], (int)output[pixel + 1]), "base 0, art (14, 5), alpha 255: (art * 255) >> 8" );
		// 8 × 8 art under N = 16: the steps are 0, so every pixel reads the art's first texel
		var tiny = new byte[8 * 8 * 4];
		for ( var index = 0; index < tiny.Length; index += 4 )
			(tiny[index], tiny[index + 1], tiny[index + 2], tiny[index + 3]) = ((byte)(index + 10), 20, 30, 255);
		ProceduralTerrainTexturer.Compose( baseRgba, n, tiny, 8, 8, 0, output );
		Assert.IsTrue( Enumerable.Range( 0, n * n ).All( index => output[index * 4] == output[0] && output[index * 4 + 1] == output[1] ) );
	}

	[TestMethod]
	public void TheArtIsTurnedOverRowByRowAndACompositeIsReadAsItIsStored()
	{
		var rgba = new byte[4 * 4 * 4];
		for ( var row = 0; row < 4; row++ )
			for ( var column = 0; column < 4; column++ )
				rgba[(row * 4 + column) * 4] = (byte)(row * 10 + column);
		var flipped = ObjectFloorBase.FlipRows( rgba, 4, 4 );
		for ( var row = 0; row < 4; row++ )
			for ( var column = 0; column < 4; column++ )
				Assert.AreEqual( (3 - row) * 10 + column, flipped[(row * 4 + column) * 4] );
		Assert.AreEqual( 0, rgba[0], "the input is not changed" );
		// an odd height keeps its middle row; a width that names a row the image does not have leaves the swap out
		var odd = new byte[3 * 3 * 4];
		for ( var row = 0; row < 3; row++ )
			odd[row * 12] = (byte)(row + 1);
		var oddFlipped = ObjectFloorBase.FlipRows( odd, 3, 3 );
		Assert.AreEqual( (3, 2, 1), (oddFlipped[0], oddFlipped[12], oddFlipped[24]) );
		var wide = new byte[8 * 2 * 4];
		wide[0] = 7;
		Assert.AreEqual( 7, ObjectFloorBase.FlipRows( wide, 8, 2 )[0], "row 0 would swap with row 7 of a 2 row image: left as it is" );
	}

	[TestMethod]
	public void TheOrientationOfAnEntryTurnsWithTheInstanceAsTheOriginalsTablesTurnIt()
	{
		// 0x46395C (90), 0x4639CC (180), 0x463994 (270); other bits of the code give no orientation after a turn
		CollectionAssert.AreEqual( new[] { 0x00, 0x08, 0x10, 0x20 }, new[] { 0x00, 0x08, 0x10, 0x20 }.Select( code => OriginalCellWords.TurnedOrientation( code, 0 ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0x08, 0x10, 0x20, 0x00 }, new[] { 0x00, 0x08, 0x10, 0x20 }.Select( code => OriginalCellWords.TurnedOrientation( code, 90 ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0x10, 0x20, 0x00, 0x08 }, new[] { 0x00, 0x08, 0x10, 0x20 }.Select( code => OriginalCellWords.TurnedOrientation( code, 180 ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0x20, 0x00, 0x08, 0x10 }, new[] { 0x00, 0x08, 0x10, 0x20 }.Select( code => OriginalCellWords.TurnedOrientation( code, 270 ) ).ToArray() );
		foreach ( var rotation in new[] { 90, 180, 270 } )
			Assert.AreEqual( 0, OriginalCellWords.TurnedOrientation( 0x18, rotation ), "0x18 is no orientation of the tables" );
		// four quarter turns bring every orientation back
		foreach ( var code in new[] { 0x00, 0x08, 0x10, 0x20 } )
		{
			var turned = code;
			for ( var quarter = 0; quarter < 4; quarter++ )
				turned = OriginalCellWords.TurnedOrientation( turned, 90 );
			Assert.AreEqual( code, turned );
		}
	}

	[TestMethod]
	public void ASkirtWordKeepsTheDiagonalBitsTakesTheTurnedOrientationTheFlipAndTheSlot()
	{
		var entry = new ObjectSkirtEntry( 0xC8, 11 );
		Assert.AreEqual( 0x000B0000u | 0x40 | 0x08 | 0x281 | 4, OriginalCellWords.SkirtWord( 0x0004_0005, entry, 0 ) );
		Assert.AreEqual( 0x000B0000u | 0x40 | 0x10 | 0x281, OriginalCellWords.SkirtWord( 0xFFFF_FFF9, entry, 90 ), "bits other than 2 and 4 of the old word, and its slot, are dropped" );
		Assert.AreEqual( 0x000B0000u | 0x40 | 0x281 | 6, OriginalCellWords.SkirtWord( 6, new ObjectSkirtEntry( 0xC0, 11 ), 0 ) );
		Assert.AreEqual( 0x281u, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x80, 0 ), 0 ) );
		// 0x281: the hole bit, the has-art bit and the skirt bit, as the composite scan 0x57ACE0 wants (0x80 and 0x200)
		Assert.AreEqual( 0x80u | 0x200u | 1u, OriginalCellWords.SkirtBits );
	}

	[TestMethod]
	public void TheEntriesLandOnTheCellsTheFootprintStampsThemOn()
	{
		var shape = new ObjectShape( new[] { "*8*", "**.", "***" } );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var seen = new HashSet<(int, int)>();
			for ( var row = 0; row < 3; row++ )
				for ( var column = 0; column < 3; column++ )
				{
					var target = ObjectFloorBase.Target( 20, 30, rotation, column, row );
					Assert.AreEqual( ObjectFootprint.ToGrid( 20, 30, rotation, column, row ), target, $"rotation {rotation} cell ({column}, {row})" );
					seen.Add( target );
				}
			Assert.AreEqual( 9, seen.Count );
			var box = ObjectFloorBase.Box( 3, 3, rotation, 20, 30 );
			Assert.IsTrue( seen.All( cell => cell.Item1 >= box.X0 && cell.Item1 <= box.X1 && cell.Item2 >= box.Z0 && cell.Item2 <= box.Z1 ) );
			Assert.AreEqual( (3, 3), (box.X1 - box.X0 + 1, box.Z1 - box.Z0 + 1) );
		}
		// a 4 × 2 shape turned by 90 spans 2 columns and 4 rows
		var turned = ObjectFloorBase.Box( 4, 2, 90, 20, 30 );
		Assert.AreEqual( (2, 4), (turned.X1 - turned.X0 + 1, turned.Z1 - turned.Z0 + 1) );
		Assert.AreEqual( 20, turned.X0 );
		Assert.AreEqual( 27, turned.Z0 );
		Assert.IsNotNull( shape );
	}

	[TestMethod]
	public void TheOriginCellOfAnInstanceIsItsAnchorAtEveryTurn()
	{
		var terrain = new HoardingTerrain( 0, 0, 10, 10, 95, 85 );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
			foreach ( var (x, y) in new[] { (0, 0), (20, 30), (94, 84) } )
				Assert.AreEqual( (x, y), ObjectFloorBase.OriginCell( new ObjectPlacement( x, y, rotation, N.Vector2.Zero, 0 ), terrain ), $"rotation {rotation} anchor ({x}, {y})" );
	}

	private static ModelHeightfield Field( int width = 12, int height = 10 ) => new( width, height, 10, 10, 0, 0, Array.Empty<uint>(), new float[(width + 1) * (height + 1)], new uint[width * height] );

	[TestMethod]
	public void TheLandscapeWordsKeepTheBackupAndRestoreIt()
	{
		var words = new OriginalCellWords( Field() );
		Assert.AreEqual( 0u, words.Word( 3, 4 ) );
		words.Write( 3, 4, 0x000B0289 );
		Assert.AreEqual( 0x000B0289u, words.Word( 3, 4 ) );
		Assert.AreEqual( 0u, words.Backup( 3, 4 ), "the backup is the file's" );
		Assert.IsTrue( words.IsWritten( 3, 4 ) );
		Assert.IsTrue( words.Restore( 3, 4 ) );
		Assert.AreEqual( 0u, words.Word( 3, 4 ) );
		Assert.IsFalse( words.Restore( 3, 4 ), "a cell that was not written has nothing to restore" );
		// outside the grid there is no word and nothing is written
		words.Write( -1, 0, 5 );
		words.Write( 12, 0, 5 );
		Assert.AreEqual( 0, words.WrittenCells );
		Assert.AreEqual( 0u, words.Word( 99, 99 ) );
	}

	[TestMethod]
	public void TheMapTypesThatDrawNoGroundAreTheOriginalsEight()
	{
		var holes = Enumerable.Range( 0, 64 ).Where( type => ObjectCellHoles.IsHoleType( (byte)type ) ).ToArray();
		CollectionAssert.AreEqual( new[] { 2, 4, 7, 9, 10, 0x15, 0x1E }, holes );
		Assert.IsFalse( ObjectCellHoles.IsHoleType( 0 ), "empty ground" );
		Assert.IsFalse( ObjectCellHoles.IsHoleType( 1 ), "a path" );
		Assert.IsFalse( ObjectCellHoles.IsHoleType( 3 ), "a queue cell keeps its ground here" );
		Assert.IsFalse( ObjectCellHoles.IsHoleType( ParkCellTypes.PoolWater ), "a pool's hole is the pools extension's" );
	}

	private static ProceduralTerrainTexturer Texturer( int size ) => new( SyntheticTerrain.Load( 1000, "C", false ), size );

	private static ObjectFloorBase.SlotArt Art( int seed, string alpha, int width = 16, int height = 16 ) => new( SyntheticArt( seed, width, height, alpha ), width, height );

	[TestMethod]
	public void TheCompositesAreMadeOverTheBoxForTheCellsWithASkirtWordAndNoOther()
	{
		const int n = 16;
		var words = new OriginalCellWords( Field() );
		var texturer = Texturer( n );
		words.Write( 1, 1, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x80, 2 ), 0 ) );
		words.Write( 2, 1, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x88, 0 ), 0 ) );
		// a cell with the has-art bit and no skirt bit (a path's art word) and a hole word without art: not composed
		words.Write( 1, 2, 0x0001_0080 );
		words.Write( 2, 2, 0x0001_0101 );
		var composites = ObjectFloorBase.Compose( words, (0, 0, 3, 3), 4, texturer, slot => Art( 40 + slot, "ramp" ) );
		CollectionAssert.AreEqual( new[] { (1, 1, 2), (2, 1, 0) }, composites.Select( c => (c.X, c.Z, c.Slot) ).ToArray(), "rows outer, columns inner" );
		Assert.IsTrue( composites.All( c => c.Rgba.Length == n * n * 4 ) );
		// the same box without the words composes nothing
		Assert.AreEqual( 0, ObjectFloorBase.Compose( new OriginalCellWords( Field() ), (0, 0, 3, 3), 4, texturer, slot => Art( 1, "full" ) ).Count );
	}

	[TestMethod]
	public void ANeighboursSkirtWordInsideTheBoxIsComposedWithThisInstancesSlotAndASlotItLacksIsLeftOut()
	{
		const int n = 16;
		var words = new OriginalCellWords( Field() );
		var texturer = Texturer( n );
		// at a park load (every instance is made before the cells are refreshed) the cell (1, 1) is another object's skirt cell naming slot 3, which this instance's model has (4 slots); (2, 1) names slot 9, which it does not (the original reads past its slot table)
		words.Write( 1, 1, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x80, 3 ), 0 ) );
		words.Write( 2, 1, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x80, 9 ), 0 ) );
		var composites = ObjectFloorBase.Compose( words, (0, 0, 3, 3), 4, texturer, slot => Art( 5, "full" ) );
		Assert.AreEqual( 1, composites.Count );
		Assert.AreEqual( (1, 1, 3), (composites[0].X, composites[0].Z, composites[0].Slot) );
	}

	[TestMethod]
	public void ARefreshedFootprintCellIsAHoleWithoutTheArtBitSoALaterObjectDoesNotComposeIt()
	{
		// the placement runs the constructor (the skirt words and the composites) and only then refreshes every footprint cell (0x5365D0, mode 8 of 0x46DF50: |= 0x101, &= ~0x80)
		const int n = 16;
		var words = new OriginalCellWords( Field() );
		var texturer = Texturer( n );
		words.Write( 1, 1, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x88, 3 ), 0 ) );
		Assert.AreEqual( 1, ObjectFloorBase.Compose( words, (0, 0, 3, 3), 4, texturer, slot => Art( 5, "full" ) ).Count, "before the refresh the cell is open to a scan" );
		words.MarkHole( 1, 1 );
		Assert.AreEqual( 0x0003_0309u, words.Word( 1, 1 ), "the slot, the orientation 8, 0x200 and the hole bits 0x101 stay; 0x80 is gone" );
		Assert.AreEqual( 0, ObjectFloorBase.Compose( words, (0, 0, 3, 3), 4, texturer, slot => Art( 5, "full" ) ).Count, "after it a later object's scan skips the cell" );
		// a footprint cell without a skirt word becomes a hole word of its own; the removal restores both
		words.MarkHole( 2, 2 );
		Assert.AreEqual( 0x101u, words.Word( 2, 2 ) );
		Assert.IsTrue( words.Restore( 1, 1 ) && words.Restore( 2, 2 ) );
		Assert.AreEqual( 0, words.WrittenCells );
		Assert.AreEqual( 0x101u, OriginalCellWords.HoleWord( 0 ) );
		Assert.AreEqual( 0x0003_0301u | 0x40, OriginalCellWords.HoleWord( 0x0003_0281u | 0x40 ) );
	}

	[TestMethod]
	public void ASlotNamedByTwoCellsGivesTheSecondCellTheCompositeOfTheFirstAsItsArt()
	{
		const int n = 16;
		var words = new OriginalCellWords( Field() );
		var texturer = Texturer( n );
		words.Write( 1, 1, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x80, 1 ), 0 ) );
		words.Write( 2, 1, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x80, 1 ), 0 ) );
		var composites = ObjectFloorBase.Compose( words, (0, 0, 3, 3), 2, texturer, slot => Art( 77, "full" ) );
		Assert.AreEqual( 2, composites.Count );
		// the first composite is stored with alpha 0 (the original's texture store), so the second cell blends nothing over its base: the composite is its base read in its orientation
		var baseRgba = new byte[texturer.TextureBytes];
		texturer.BuildRgba( 2, 1, baseRgba );
		for ( var index = 0; index < n * n; index++ )
		{
			var (row, column) = ProceduralTerrainTexturer.BaseTexel( (int)(composites[1].Word & 0xFFFF), n, index / n, index % n );
			var source = (row * n + column) * 4;
			Assert.AreEqual( (baseRgba[source], baseRgba[source + 1], baseRgba[source + 2]), (composites[1].Rgba[index * 4], composites[1].Rgba[index * 4 + 1], composites[1].Rgba[index * 4 + 2]) );
		}
		// and the first, over opaque art, is not the base
		Assert.AreNotEqual( Sha( baseRgba ), Sha( composites[0].Rgba ) );
	}

	[TestMethod]
	public void ACellOutsideTheWeightGridGetsNoComposite()
	{
		var words = new OriginalCellWords( Field( 12, 10 ) );
		var texturer = Texturer( 16 );
		// the synthetic level has 3 × 3 cells: the cells at column 7 and row 7 are outside it
		words.Write( 7, 1, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x80, 0 ), 0 ) );
		words.Write( 1, 7, OriginalCellWords.SkirtWord( 0, new ObjectSkirtEntry( 0x80, 0 ), 0 ) );
		Assert.AreEqual( 0, ObjectFloorBase.Compose( words, (0, 0, 9, 9), 1, texturer, slot => Art( 1, "full" ) ).Count );
	}
}

/// <summary>What the procedural terrain does when a cell stops being a hole (docs/OBJECTS.md, "Floors": the demolished object's cells take their backup word again with the dirty bit).</summary>
[TestClass]
[DoNotParallelize]
public class ProceduralTerrainHoleRestoreTests
{
	private const int Size = 16;

	private sealed class FakePages : IProceduralPages
	{
		public readonly List<int> Uploads = new();
		public byte[] Scratch { get; } = new byte[Size * Size * 4];
		public int PageOf( int slot ) => slot / 5;
		public (float U0, float V0, float U1, float V1) CoordinatesOf( int slot ) => (0, 0, 1, 1);
		public void Upload( int slot, byte[] rgba ) => Uploads.Add( slot );
	}

	private static ProceduralTerrain Make( FakePages pages )
	{
		var data = SyntheticLnd.Build( 3, 3, [48, 40, 40, 90, 68, 40, 97, 30, 55, 50], ( x, y, channel ) => channel == 0 ? (byte)255 : (byte)(x * 11 + y * 7 + channel),
			( size, image, x, y, channel ) => (byte)(size + image * 5 + x * 3 + y * 11 + channel) );
		var field = new ModelHeightfield( 5, 4, 10, 10, 0, 0, Array.Empty<uint>(), new float[6 * 5], new uint[20] );
		return new ProceduralTerrain( field, new LndFile( new MemoryStream( data ) ), new ProceduralTerrainRectangle( 0, 0, 3, 2 ), new ProceduralTerrainSettings( Size, 40 ), 2, pages );
	}

	private static bool Refresh( ProceduralTerrain terrain, Func<int, int, bool> hole, ulong version )
	{
		var view = System.Numerics.Matrix4x4.CreateLookAt( new System.Numerics.Vector3( 0, 0, 40 ), System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY );
		var projection = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView( 90 * MathF.PI / 180, 4f / 3, 0.1f, 1000 );
		return terrain.Refresh( view, projection, hole, version );
	}

	[TestMethod]
	public void ACellThatStopsBeingAHoleIsRebuiltOnceAndACellThatWasNeverOneIsNot()
	{
		var pages = new FakePages();
		var terrain = Make( pages );
		Refresh( terrain, ( x, z ) => false, 1 );
		var built = terrain.Builds;
		Assert.AreEqual( 11, built, "12 cells of the rectangle less the corner cell (3, 0), whose samples run past a grid this small" );
		// an object takes the cell (2, 0): a hole from the next frame, nothing is built
		Assert.IsTrue( Refresh( terrain, ( x, z ) => x == 2 && z == 0, 2 ) );
		Assert.AreEqual( built, terrain.Builds );
		Assert.IsFalse( terrain.VisibleCells.Contains( (2, 0) ) );
		// the object is demolished: the cell takes its backup word with the dirty bit, so its texture is rebuilt, once
		Refresh( terrain, ( x, z ) => false, 3 );
		Assert.AreEqual( built + 1, terrain.Builds, "the cell's cache slot still holds it, and the restore's dirty bit makes the hit build again" );
		Assert.IsTrue( terrain.VisibleCells.Contains( (2, 0) ) );
		Refresh( terrain, ( x, z ) => false, 3 );
		Refresh( terrain, ( x, z ) => false, 4 );
		Assert.AreEqual( built + 1, terrain.Builds, "clean again" );
	}

	[TestMethod]
	public void AHoleThatChangesWithoutALookAtTheCellLeavesNothingDirty()
	{
		var pages = new FakePages();
		var terrain = Make( pages );
		Refresh( terrain, ( x, z ) => x == 2 && z == 0, 1 );
		var built = terrain.Builds;
		// the cell was a hole from the first frame on: when it is released it is built as any new cell, once
		Refresh( terrain, ( x, z ) => false, 2 );
		Assert.AreEqual( built + 1, terrain.Builds );
		Refresh( terrain, ( x, z ) => false, 3 );
		Assert.AreEqual( built + 1, terrain.Builds );
	}
}

/// <summary>The skirt base against the shipped objects; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectFloorBaseCorpusTests
{
	private static readonly string[] Themes = { "jungle", "fantasy", "hallow", "space" };
	private static readonly int[] Rotations = { 0, 90, 180, 270 };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the floor base corpus." );
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

	private static float[] InstanceMatrix( ModelFile model, int node, N.Matrix4x4 root )
	{
		var path = new List<int>();
		for ( var current = node; current >= 0; current = model.Nodes[current].ParentIndex )
			path.Insert( 0, current );
		var matrix = ObjectFloorMath.ToArray( root );
		foreach ( var step in path )
			matrix = ObjectFloorMath.Chain( ObjectFloorMath.ToArray( model.Nodes[step].Transform ), matrix );
		return matrix;
	}

	/// <summary>One skirt cell's pixels: for each texel of an N × N texture the base pixel the geometry says (the ground under the texel) and the one the orientation word reads.</summary>
	internal readonly record struct Sample( string Name, int Rotation, int Word, int Row, int Column, float Lx, float Lz, bool Found, float U, float V, int X, int Z, int Slot );

	private static IEnumerable<Sample> CellSamples( string theme, ObjectCatalogEntry entry, ModelFile model, ObjectSkirt skirt, int rotation, int size )
	{
		var field = ObjectSkirtCorpusTests.FieldOf( theme );
		var terrain = HoardingTerrain.Of( field );
		var placement = new ObjectPlacement( 20, 20, rotation, N.Vector2.Zero, 0 );
		var root = ObjectGroundConform.RootPlacement( placement, terrain );
		var (originX, originZ) = ObjectFloorBase.OriginCell( placement, terrain );
		var mesh = model.Meshes[skirt.GroundMesh];
		var matrix = InstanceMatrix( model, skirt.GroundNode, root );
		var positions = mesh.Positions.Select( p => ObjectFloorMath.TransformVertex( matrix, new N.Vector3( p.X, p.Y, p.Z ) ) ).ToArray();
		var uvs = skirt.TexCoords!;
		foreach ( var cell in ObjectFloorBase.SkirtWords( skirt, originX, originZ, rotation, ( x, z ) => 0 ) )
		{
			// the ground faces of the cell's slot whose centre lies in the cell
			var faces = new List<(int A, int B, int C)>();
			foreach ( var group in mesh.Materials )
			{
				if ( group.TextureIndex != cell.Entry.Slot )
					continue;
				for ( var face = (int)group.StartIndex; face < group.EndIndex && face * 3 + 2 < mesh.Indices.Length; face++ )
				{
					var (a, b, c) = ((int)mesh.Indices[face * 3], (int)mesh.Indices[face * 3 + 1], (int)mesh.Indices[face * 3 + 2]);
					var centre = (positions[mesh.CornerPositionIndices[a]] + positions[mesh.CornerPositionIndices[b]] + positions[mesh.CornerPositionIndices[c]]) / 3;
					if ( (int)MathF.Floor( centre.X / terrain.CellSizeX ) == cell.X && (int)MathF.Floor( centre.Z / terrain.CellSizeZ ) == cell.Z )
						faces.Add( (a, b, c) );
				}
			}
			for ( var row = 0; row < size; row++ )
			{
				for ( var column = 0; column < size; column++ )
				{
					var u = (column + 0.5f) / size;
					var v = (row + 0.5f) / size;
					var found = false;
					float lx = 0, lz = 0;
					foreach ( var (a, b, c) in faces )
					{
						var (ua, ub, uc) = (uvs[a], uvs[b], uvs[c]);
						var d = (ub.Y - uc.Y) * (ua.X - uc.X) + (uc.X - ub.X) * (ua.Y - uc.Y);
						if ( MathF.Abs( d ) < 1e-9f )
							continue;
						var w0 = ((ub.Y - uc.Y) * (u - uc.X) + (uc.X - ub.X) * (v - uc.Y)) / d;
						var w1 = ((uc.Y - ua.Y) * (u - uc.X) + (ua.X - uc.X) * (v - uc.Y)) / d;
						var w2 = 1 - w0 - w1;
						if ( w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f )
							continue;
						var p = positions[mesh.CornerPositionIndices[a]] * w0 + positions[mesh.CornerPositionIndices[b]] * w1 + positions[mesh.CornerPositionIndices[c]] * w2;
						lx = p.X / terrain.CellSizeX - cell.X;
						lz = p.Z / terrain.CellSizeZ - cell.Z;
						found = true;
						break;
					}
					yield return new Sample( entry.ArchiveName, rotation, (int)(cell.Word & 0xFFFF), row, column, lx, lz, found, u, v, cell.X, cell.Z, cell.Entry.Slot );
				}
			}
		}
	}

	/// <summary>The N × N base the level's texturer builds for a cell (the original's buffer, rows as the build routine writes them) and the texturer.</summary>
	private static ProceduralTerrainTexturer TexturerOf( string theme, int size )
	{
		using var stream = FileSystem.OpenRead( $"/levels/{theme}/terrain/base.lnd" );
		return new ProceduralTerrainTexturer( new LndFile( stream ), size );
	}

	[TestMethod]
	public void TheOrientationWordPutsTheBaseUnderTheGroundOfTheTexelAtEveryTurnOfEveryShippedFloor()
	{
		// The cells' texture coordinates say which ground a texel of the cell's texture lies on (row = v, column = u, the original's device order); the orientation word, turned with the instance,
		// must read the base pixel of that ground (row = z across the cell, column = x). Every other reading of the base is far worse: the data pins the table of docs/OBJECTS.md.
		const int size = 16;
		var tally = new Dictionary<string, (int Hit, int Total)>();
		foreach ( var theme in Themes )
		{
			var terrain = HoardingTerrain.Of( ObjectSkirtCorpusTests.FieldOf( theme ) );
			foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( theme ) )
			{
				var model = ObjectSkirtCorpusTests.ModelOf( entry );
				var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
				if ( skirt.GroundMesh < 0 || !skirt.Cells.Any() )
					continue;
				foreach ( var rotation in Rotations )
				{
					foreach ( var sample in CellSamples( theme, entry, model, skirt, rotation, size ) )
					{
						if ( !sample.Found )
							continue;
						for ( var flipRow = 0; flipRow < 2; flipRow++ )
							for ( var flipZ = 0; flipZ < 2; flipZ++ )
								for ( var flipX = 0; flipX < 2; flipX++ )
									for ( var swap = 0; swap < 2; swap++ )
									{
										// the hypotheses: the texel row follows v or 1 - v, the base row follows z or 1 - z, its column x or 1 - x, with or without exchanging the two
										var texelRow = flipRow == 0 ? sample.Row : size - 1 - sample.Row;
										var lz = flipZ == 0 ? sample.Lz : 1 - sample.Lz;
										var lx = flipX == 0 ? sample.Lx : 1 - sample.Lx;
										var expectedRow = Math.Clamp( (int)(lz * size), 0, size - 1 );
										var expectedColumn = Math.Clamp( (int)(lx * size), 0, size - 1 );
										if ( swap == 1 )
											(expectedRow, expectedColumn) = (expectedColumn, expectedRow);
										var (baseRow, baseColumn) = ProceduralTerrainTexturer.BaseTexel( sample.Word, size, texelRow, sample.Column );
										var key = $"flipRow={flipRow} flipZ={flipZ} flipX={flipX} swap={swap}";
										tally.TryGetValue( key, out var count );
										tally[key] = (count.Hit + (Math.Abs( baseRow - expectedRow ) <= 1 && Math.Abs( baseColumn - expectedColumn ) <= 1 ? 1 : 0), count.Total + 1);
									}
					}
				}
			}
		}
		const string reading = "flipRow=0 flipZ=0 flipX=0 swap=0";
		var rate = (double)tally[reading].Hit / tally[reading].Total;
		Assert.IsTrue( rate > 0.97, $"the table's reading: {rate:P1} of {tally[reading].Total} texels (a few flared cells of Belly Bounce, the totem and the lookout stretch the art over more than their cell)" );
		foreach ( var (key, (hit, total)) in tally.Where( pair => pair.Key != reading ) )
			Assert.IsTrue( (double)hit / total < 0.55, $"{key}: {(double)hit / total:P1}" );
	}

	[TestMethod]
	public void TheArtTurnedOverIsTransparentAtTheEdgesToTheOpenGroundAndOpaqueAtTheEdgesToTheFloor()
	{
		// OPEN-F1: 0x590DA0 turns the art over before the composite reads it. In the original's order (row = v) the art's alpha is low along a cell edge that borders open ground (not
		// a footprint cell) and high along one that borders the floor only when read that way up; the file's rows as decoded do not separate the two.
		const int size = 32;
		var sums = new Dictionary<string, (double Alpha, int Count)>();
		foreach ( var theme in Themes )
		{
			var terrain = HoardingTerrain.Of( ObjectSkirtCorpusTests.FieldOf( theme ) );
			foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( theme ) )
			{
				var model = ObjectSkirtCorpusTests.ModelOf( entry );
				var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
				if ( skirt.GroundMesh < 0 || !skirt.Cells.Any() )
					continue;
				var arts = new Dictionary<int, ObjectFloorBase.SlotArt?>();
				var footprint = ObjectFootprint.GetCells( entry.Shape, 20, 20, 0 ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
				foreach ( var sample in CellSamples( theme, entry, model, skirt, 0, size ) )
				{
					if ( !sample.Found )
						continue;
					if ( !arts.TryGetValue( sample.Slot, out var art ) )
						arts[sample.Slot] = art = OriginalObject.DecodeSlotArt( entry, model.Textures[sample.Slot].FrameNames[0] );
					if ( art is not { } slotArt )
						continue;
					var stepX = slotArt.Width / size;
					var stepY = slotArt.Height / size;
					for ( var turned = 0; turned < 2; turned++ )
					{
						var artRow = turned == 0 ? sample.Row * stepY : slotArt.Height - 1 - sample.Row * stepY;
						var alpha = slotArt.Rgba[(artRow * slotArt.Width + sample.Column * stepX) * 4 + 3];
						void Add( bool exposed )
						{
							var key = $"turned={turned} {(exposed ? "open" : "floor")}";
							sums.TryGetValue( key, out var total );
							sums[key] = (total.Alpha + alpha, total.Count + 1);
						}
						foreach ( var (near, neighbour) in new[] { (sample.Lx < 0.12f, (sample.X - 1, sample.Z)), (sample.Lx > 0.88f, (sample.X + 1, sample.Z)), (sample.Lz < 0.12f, (sample.X, sample.Z - 1)), (sample.Lz > 0.88f, (sample.X, sample.Z + 1)) } )
							if ( near )
								Add( !footprint.Contains( neighbour ) );
					}
				}
			}
		}
		double Mean( string key ) => sums[key].Alpha / sums[key].Count;
		Assert.IsTrue( Mean( "turned=1 open" ) < 80 && Mean( "turned=1 floor" ) > 170, $"turned over: open {Mean( "turned=1 open" ):F0}, floor {Mean( "turned=1 floor" ):F0}" );
		Assert.IsTrue( Math.Abs( Mean( "turned=0 open" ) - Mean( "turned=0 floor" ) ) < 40, $"as decoded: open {Mean( "turned=0 open" ):F0}, floor {Mean( "turned=0 floor" ):F0}" );
	}

	[TestMethod]
	public void TheFringeOfAnEdgeCellContinuesTheNeighbouringGroundAcrossTheEdge()
	{
		// the seam: where a composite's art is transparent along an edge that borders open ground, the texel IS the base pixel of the ground at that place, so the cell's texture goes on
		// the neighbouring terrain cell's (the base is read from the same texturer as the neighbour's texture)
		const int size = 32;
		var checkedTexels = 0;
		var continuous = 0;
		foreach ( var theme in Themes )
		{
			var field = ObjectSkirtCorpusTests.FieldOf( theme );
			var terrain = HoardingTerrain.Of( field );
			var texturer = TexturerOf( theme, size );
			var baseRgba = new byte[texturer.TextureBytes];
			foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( theme ).Take( 24 ) )
			{
				var model = ObjectSkirtCorpusTests.ModelOf( entry );
				var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
				if ( skirt.GroundMesh < 0 || !skirt.Cells.Any() )
					continue;
				foreach ( var rotation in new[] { 0, 90, 180, 270 } )
				{
					var placement = new ObjectPlacement( 20, 20, rotation, N.Vector2.Zero, 0 );
					var (originX, originZ) = ObjectFloorBase.OriginCell( placement, terrain );
					var words = new OriginalCellWords( field );
					foreach ( var cell in ObjectFloorBase.SkirtWords( skirt, originX, originZ, rotation, words.Word ) )
						words.Write( cell.X, cell.Z, cell.Word );
					var box = ObjectFloorBase.Box( skirt.Width, skirt.Height, rotation, originX, originZ );
					var arts = new Dictionary<int, ObjectFloorBase.SlotArt?>();
					var composites = ObjectFloorBase.Compose( words, box, model.Textures.Count, texturer, slot => arts.TryGetValue( slot, out var art ) ? art : arts[slot] = OriginalObject.DecodeSlotArt( entry, model.Textures[slot].FrameNames[0] ) );
					var footprint = ObjectFootprint.GetCells( entry.Shape, 20, 20, rotation ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
					foreach ( var sample in CellSamples( theme, entry, model, skirt, rotation, size ) )
					{
						if ( !sample.Found || composites.LastOrDefault( c => c.X == sample.X && c.Z == sample.Z && c.Slot == sample.Slot ) is not { } composite )
							continue;
						var open = (sample.Lx < 0.05f && !footprint.Contains( (sample.X - 1, sample.Z) )) || (sample.Lx > 0.95f && !footprint.Contains( (sample.X + 1, sample.Z) ))
							|| (sample.Lz < 0.05f && !footprint.Contains( (sample.X, sample.Z - 1) )) || (sample.Lz > 0.95f && !footprint.Contains( (sample.X, sample.Z + 1) ));
						if ( !open )
							continue;
						var art = ObjectFloorBase.FlipRows( arts[sample.Slot]!.Value.Rgba, arts[sample.Slot]!.Value.Width, arts[sample.Slot]!.Value.Height );
						var width = arts[sample.Slot]!.Value.Width;
						var height = arts[sample.Slot]!.Value.Height;
						if ( art[(sample.Row * (height / size) * width + sample.Column * (width / size)) * 4 + 3] != 0 )
							continue;
						texturer.BuildRgba( sample.X, sample.Z, baseRgba );
						var expectedRow = Math.Clamp( (int)(sample.Lz * size), 0, size - 1 );
						var expectedColumn = Math.Clamp( (int)(sample.Lx * size), 0, size - 1 );
						var texel = (sample.Row * size + sample.Column) * 4;
						var source = (expectedRow * size + expectedColumn) * 4;
						checkedTexels++;
						// within one texel of the ground's place: the cell's edge texels (the base's neighbours along the edge differ by about as much as the texels around them)
						var hit = false;
						for ( var dr = -1; dr <= 1 && !hit; dr++ )
							for ( var dc = -1; dc <= 1 && !hit; dc++ )
							{
								var r = Math.Clamp( expectedRow + dr, 0, size - 1 );
								var c = Math.Clamp( expectedColumn + dc, 0, size - 1 );
								hit = composite.Rgba[texel] == baseRgba[(r * size + c) * 4] && composite.Rgba[texel + 1] == baseRgba[(r * size + c) * 4 + 1] && composite.Rgba[texel + 2] == baseRgba[(r * size + c) * 4 + 2];
							}
						if ( hit )
							continuous++;
					}
				}
			}
		}
		Assert.IsTrue( checkedTexels > 10000, $"{checkedTexels} texels along open edges" );
		Assert.IsTrue( (double)continuous / checkedTexels > 0.95, $"{continuous} of {checkedTexels}" );
	}

	[TestMethod]
	public void EverySkirtCellOfEveryShippedTypeGetsACompositeAndTheSlotsAreTheEntriesSlots()
	{
		foreach ( var theme in Themes )
		{
			var field = ObjectSkirtCorpusTests.FieldOf( theme );
			var terrain = HoardingTerrain.Of( field );
			var texturer = TexturerOf( theme, 16 );
			foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( theme ) )
			{
				var model = ObjectSkirtCorpusTests.ModelOf( entry );
				var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
				if ( !skirt.Cells.Any() )
					continue;
				var words = new OriginalCellWords( field );
				var placement = new ObjectPlacement( 30, 30, 90, N.Vector2.Zero, 0 );
				var (originX, originZ) = ObjectFloorBase.OriginCell( placement, terrain );
				foreach ( var cell in ObjectFloorBase.SkirtWords( skirt, originX, originZ, 90, words.Word ) )
					words.Write( cell.X, cell.Z, cell.Word );
				var composites = ObjectFloorBase.Compose( words, ObjectFloorBase.Box( skirt.Width, skirt.Height, 90, originX, originZ ), model.Textures.Count, texturer,
					slot => new ObjectFloorBase.SlotArt( new byte[16 * 16 * 4], 16, 16 ) );
				Assert.AreEqual( skirt.Cells.Count(), composites.Count, $"{theme}/{entry.ArchiveName}" );
				CollectionAssert.AreEquivalent( skirt.Cells.Select( cell => cell.Entry.Slot ).ToArray(), composites.Select( composite => composite.Slot ).ToArray(), $"{theme}/{entry.ArchiveName}" );
				Assert.IsTrue( composites.All( composite => (composite.Word & 0x281) == 0x281 ) );
			}
		}
	}

	/// <summary>The footprint cells of a placed type that no face of any of its meshes covers (a face counts for the cell its centre lies in), per theme: the ground under such a cell is a hole and nothing stands in it.</summary>
	private static List<string> UncoveredFootprintCells( string theme )
	{
		var result = new List<string>();
		var terrain = HoardingTerrain.Of( ObjectSkirtCorpusTests.FieldOf( theme ) );
		foreach ( var entry in ObjectSkirtCorpusTests.NonFixedTypes( theme ) )
		{
			var model = ObjectSkirtCorpusTests.ModelOf( entry );
			var placement = new ObjectPlacement( 20, 20, 0, N.Vector2.Zero, 0 );
			var root = ObjectGroundConform.RootPlacement( placement, terrain );
			var covered = new HashSet<(int, int)>();
			for ( var node = 0; node < model.Nodes.Count; node++ )
			{
				var meshIndex = model.Nodes[node].MeshIndex;
				if ( meshIndex < 0 || meshIndex >= model.Meshes.Count )
					continue;
				var mesh = model.Meshes[meshIndex];
				var matrix = InstanceMatrix( model, node, root );
				var positions = mesh.Positions.Select( p => ObjectFloorMath.TransformVertex( matrix, new N.Vector3( p.X, p.Y, p.Z ) ) ).ToArray();
				for ( var face = 0; face * 3 + 2 < mesh.Indices.Length; face++ )
				{
					var centre = (positions[mesh.CornerPositionIndices[mesh.Indices[face * 3]]] + positions[mesh.CornerPositionIndices[mesh.Indices[face * 3 + 1]]] + positions[mesh.CornerPositionIndices[mesh.Indices[face * 3 + 2]]]) / 3;
					covered.Add( ((int)MathF.Floor( centre.X / terrain.CellSizeX ), (int)MathF.Floor( centre.Z / terrain.CellSizeZ )) );
				}
			}
			var missing = ObjectFootprint.GetCells( entry.Shape, 20, 20, 0 ).Where( cell => !covered.Contains( (cell.X, cell.Y) ) ).Select( cell => $"({cell.X - 20},{cell.Y - 20})" ).ToList();
			if ( missing.Count > 0 )
				result.Add( $"{entry.ArchiveName}: {string.Join( " ", missing )}" );
		}
		return result;
	}

	[TestMethod]
	public void EveryFootprintCellOfEveryShippedTypeHasAFaceOverIt()
	{
		// every footprint cell is a hole in the ground (0x535DD0, mode 8), so a cell no face covers would be a see-through gap, as in the original; the shipped types have none
		foreach ( var theme in Themes )
			CollectionAssert.AreEqual( Array.Empty<string>(), UncoveredFootprintCells( theme ), theme );
	}
}

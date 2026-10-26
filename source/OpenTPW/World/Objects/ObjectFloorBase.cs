namespace OpenTPW;

/// <summary>What a placed object needs to put its skirt on the landscape: the level's landscape words, its procedural ground and the terrain grid.</summary>
internal sealed record FloorBaseContext( OriginalCellWords Words, ProceduralTerrain Ground, HoardingTerrain Terrain, Action ReleaseArt );

/// <summary>One composite texture of a placed object: the cell it covers, the cell's word and the slot of the object's model whose texture it replaces.</summary>
internal sealed record FloorComposite( int X, int Z, uint Word, int Slot, byte[] Rgba );

/// <summary>
/// The skirt cells of a placed object and the procedural base under them (docs/OBJECTS.md, "Floors"): where the skirt entries land on the landscape for the instance's turn
/// (tp.exe <c>0x463060</c>, 0x46336C to 0x463580), the footprint box the composites are made over (<c>0x467030</c>, <c>0x57ACE0</c>) and the composites themselves. Presentation only.
/// </summary>
internal static class ObjectFloorBase
{
	/// <summary>
	/// The landscape cell the entry of shape cell (<paramref name="column"/>, <paramref name="row"/>) lands on for an instance whose origin cell (<see cref="OriginCell"/>) is (<paramref name="originX"/>, <paramref name="originZ"/>):
	/// no bounds test, as in the original.
	/// </summary>
	// [BIN:TPW-EXE:0x00463424 skirt cell target] 0 degrees (X + column, Z + row), 90 (X + row, Z - column), 180 (X - column, Z - row), 270 (X - row, Z + column)
	public static (int X, int Z) Target( int originX, int originZ, int rotation, int column, int row ) => rotation switch
	{
		0 => (originX + column, originZ + row),
		90 => (originX + row, originZ - column),
		180 => (originX - column, originZ - row),
		270 => (originX - row, originZ + column),
		_ => throw new ArgumentOutOfRangeException( nameof( rotation ), "Rotations are multiples of 90 degrees." )
	};

	/// <summary>
	/// The origin cell of an instance (<c>0x463060</c> at 0x4633A0 to 0x4633DF): <c>ftol(translation / cell size)</c> of the instance root, one less in x for 180 and 270 and one less in z for 90 and 180 (the root's
	/// translation carries the extent of the first cell of a turned shape). It is the anchor cell of the placement.
	/// </summary>
	// [BIN:TPW-EXE:0x004633A0 skirt origin] X = ftol(root +0x40 / landscape +0x10), Z = ftol(root +0x48 / landscape +0x14); angle 0x5A: Z -= 1; 0xB4: X -= 1 and Z -= 1; 0x10E: X -= 1
	public static (int X, int Z) OriginCell( ObjectPlacement placement, HoardingTerrain terrain )
	{
		var root = ObjectGroundConform.RootPlacement( placement, terrain );
		var x = SkirtReading.FloatToInt( root.M41 / terrain.CellSizeX );
		var z = SkirtReading.FloatToInt( root.M43 / terrain.CellSizeZ );
		return placement.Rotation switch
		{
			90 => (x, z - 1),
			180 => (x - 1, z - 1),
			270 => (x - 1, z),
			_ => (x, z)
		};
	}

	/// <summary>The cells of the skirt with the word each takes, in the original's order (rows, then columns): the landscape cell, the shape cell and the new word.</summary>
	public static IEnumerable<(int X, int Z, int Column, int Row, ObjectSkirtEntry Entry, uint Word)> SkirtWords( ObjectSkirt skirt, int originX, int originZ, int rotation, Func<int, int, uint> oldWord )
	{
		for ( var row = 0; row < skirt.Height; row++ )
		{
			for ( var column = 0; column < skirt.Width; column++ )
			{
				var entry = skirt.Entries[row * skirt.Width + column];
				if ( !entry.HasSkirt )
					continue;
				var (x, z) = Target( originX, originZ, rotation, column, row );
				yield return (x, z, column, row, entry, OriginalCellWords.SkirtWord( oldWord( x, z ), entry, rotation ));
			}
		}
	}

	/// <summary>
	/// The footprint box of an instance, inclusive (<c>0x467030</c> stores it in the instance's +0xC0 and +0xC4): the corner cells of the shape's width × height rectangle, whatever its cells hold.
	/// </summary>
	// [BIN:TPW-EXE:0x00467030 footprint box] x0 and z0 are the minimum corner (0x466F90), x1 = x0 + width - 1 and z1 = z0 + height - 1 with the width and height swapped for 90 and 270
	public static (int X0, int Z0, int X1, int Z1) Box( int width, int height, int rotation, int originX, int originZ )
	{
		var (ax, az) = Target( originX, originZ, rotation, 0, 0 );
		var (bx, bz) = Target( originX, originZ, rotation, width - 1, height - 1 );
		return (Math.Min( ax, bx ), Math.Min( az, bz ), Math.Max( ax, bx ), Math.Max( az, bz ));
	}

	/// <summary>The art of one slot of the object's model: RGBA texels in the order the file's image decodes (<see cref="Composite"/>: a composite already made, which reads as it is stored).</summary>
	internal readonly record struct SlotArt( byte[] Rgba, int Width, int Height, bool Composite = false );

	/// <summary>
	/// The art the way the composite reads it (tp.exe <c>0x590DA0</c>, for a texture with the flag 0x40000, which every floor art has): the rows turned upside down. The routine swaps row r with row
	/// <c>width - 1 - r</c> for the first <c>height / 2</c> rows (the art is square: 128 × 128), so a non-square image would swap with a row that is not there; such a row is left as it is.
	/// </summary>
	// [BIN:TPW-EXE:0x00590DA0 art rows] the art buffer of a texture with the flag 0x40000 has its rows swapped, row r with row (width - 1 - r), height / 2 times; a texture without the flag (a composite made by 0x590A80, flags 0x1000000) is read as stored. The art the composite blends over the base is the file's image upside down: the exposed edges of a skirt cell are then transparent and the edges against the floor opaque (the data seam test)
	public static byte[] FlipRows( byte[] rgba, int width, int height )
	{
		var flipped = (byte[])rgba.Clone();
		var rowBytes = width * ProceduralTerrainTexturer.BytesPerPixel;
		for ( var row = 0; row < (height & ~1) / 2; row++ )
		{
			var other = width - 1 - row;
			if ( other < 0 || other >= height || other == row )
				continue;
			Buffer.BlockCopy( rgba, row * rowBytes, flipped, other * rowBytes, rowBytes );
			Buffer.BlockCopy( rgba, other * rowBytes, flipped, row * rowBytes, rowBytes );
		}
		return flipped;
	}

	/// <summary>
	/// The composites of an instance (<c>0x57ACE0</c>): over the footprint box, row by row, every cell whose word has bits 0x80 and 0x200 (a skirt word: this instance's, or a neighbour's inside the box)
	/// takes the art of the slot its word names and the cell's procedural base, blended by <see cref="ProceduralTerrainTexturer.Compose"/>. The composite then stands in for the slot's texture, so a
	/// later cell of the same slot reads it as its art.
	/// </summary>
	/// <param name="words">The landscape words, with this instance's skirt words written.</param>
	/// <param name="texturer">The level's procedural texturer (N × N bases).</param>
	/// <param name="art">The art of a slot of the model as the file decodes it (<see cref="FlipRows"/> turns it the way the composite reads it), or null when the data has none (the original would read garbage; the cell gets no composite).</param>
	/// <param name="slotCount">The slots of the instance's model: a word naming a slot past them (a neighbour's cell in the box) is skipped, where the original reads past its slot table.</param>
	// [BIN:TPW-EXE:0x0057ACE0 floor composite] per cell of the box (rows outer, columns inner) with the word bits 0x80 and 0x200: the slot record is the instance's slot table entry (word >> 16), the art the slot's current texture (0x590DA0), the descriptor 32-bit (4 bytes per pixel) with the word's low half; 0x572630 builds the base with 0x55F780 and blends; the slot's texture handle is redirected to the new texture
	public static List<FloorComposite> Compose( OriginalCellWords words, (int X0, int Z0, int X1, int Z1) box, int slotCount, ProceduralTerrainTexturer texturer, Func<int, SlotArt?> art )
	{
		var composites = new List<FloorComposite>();
		var current = new Dictionary<int, SlotArt?>();
		var size = texturer.Size;
		var baseRgba = new byte[texturer.TextureBytes];
		for ( var z = box.Z0; z <= box.Z1; z++ )
		{
			for ( var x = box.X0; x <= box.X1; x++ )
			{
				var word = words.Word( x, z );
				if ( (word & 0x80) == 0 || (word & 0x200) == 0 )
					continue;
				var slot = (int)(word >> 16);
				if ( slot >= slotCount || !texturer.CanBuild( x, z ) )
					continue;
				if ( !current.TryGetValue( slot, out var source ) )
					current[slot] = source = art( slot ) is { } decoded ? new SlotArt( FlipRows( decoded.Rgba, decoded.Width, decoded.Height ), decoded.Width, decoded.Height ) : null;
				if ( source is not { } slotArt )
					continue;
				texturer.BuildRgba( x, z, baseRgba );
				var pixels = slotArt.Rgba;
				if ( slotArt.Composite )
				{
					// a composite stored as the original keeps it: A8R8G8B8 with alpha 0, so a cell that reads it as art gets only the base
					pixels = (byte[])pixels.Clone();
					for ( var index = 3; index < pixels.Length; index += 4 )
						pixels[index] = 0;
				}
				var composite = new byte[size * size * ProceduralTerrainTexturer.BytesPerPixel];
				ProceduralTerrainTexturer.Compose( baseRgba, size, pixels, slotArt.Width, slotArt.Height, (int)(word & 0xFFFF), composite );
				composites.Add( new FloorComposite( x, z, word, slot, composite ) );
				current[slot] = new SlotArt( composite, size, size, Composite: true );
			}
		}
		return composites;
	}
}

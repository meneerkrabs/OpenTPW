namespace OpenTPW;

/// <summary>One queue cell's model: an index into <see cref="QueueModels.Names"/> and a yaw in degrees (0, 90, 180, 270).</summary>
public readonly record struct QueueModelTile( int Model, int Yaw );

/// <summary>
/// The queue cells' models (tp.exe): the tile chooser <c>0x535DD0</c> gives a queue cell a class-2 result from its link byte, and the tile
/// refresh <c>0x5365D0</c> places that queue model on the cell (<c>0x5229E0</c>) instead of a ground texture. The models are the theme's
/// <c>queue</c> archive's <see cref="Names"/>, loaded by <c>0x522900</c>.
/// </summary>
// [BIN:TPW-EXE:0x00522900 queue models] the eight entries of the table 0x763388 ({handle, name, flags}) are loaded from "%s\queue" of the level folder: quedead, quedead, questra, quebnd2, quebnd1, queend, quebin1, quebin2 (.md2); the entries whose flag byte +9 is set (both quedead) load with the flags 0x112423, the others with 0x12423
// [BIN:TPW-EXE:0x005365D0 tile refresh] a class-2 result removes the cell's previous model (0x522A90), places model (index, rotation) with 0x5229E0, and gives the ground the result (0, 8, 0), the value the chooser returns for cells without a path tile
// [BIN:TPW-EXE:0x005229E0 queue model] the model goes on the cell with the yaw 360 − rotation (0 for 0) through the model placement 0x463060 (flags 0x33A), the routine and the yaw meaning objects are placed with (0x4DD500 passes a thing's rotation +0x10), so ObjectPlacement's rotation applies
public static class QueueModels
{
	/// <summary>The table 0x763388's models in index order.</summary>
	public static readonly IReadOnlyList<string> Names = new[] { "quedead", "quedead", "questra", "quebnd2", "quebnd1", "queend", "quebin1", "quebin2" };

	/// <summary>The load flags the loader <c>0x522900</c> hands <c>0x4629D0</c> for a model of the table: 0x12423, and 0x112423 for the rows whose flag word is 0x100 (the two <c>quedead</c> entries, <see cref="QueueModelPulse"/>).</summary>
	// [BIN:TPW-EXE:0x00522951 load flags] mov al, [esi + 5]; edi = 0x12423, or 0x112423 when the byte is not 0 (0x522954..0x52295D); the rows' flag words are 0x100 for rows 0 and 1 and 0 for the other six (0x763390, 0x76339C, 0x7633A8..0x7633E4)
	public static uint LoadFlags( int model ) => QueueModelPulse.Pulses( model ) ? 0x112423u : 0x12423u;

	/// <summary>The loader's own flag word for a raw flag word (the mapping of <c>0x4629D0</c>, 0x4629F5..0x462A9A) that <c>0x461F10</c> reads as its second argument.</summary>
	// [BIN:TPW-EXE:0x004629D0 loader flag mapping] raw 0x10 -> 2, 0x20 -> 0x10, 0x40 -> 0x20, 0x2 -> 0x40, 0x100 -> 1, 0x200 -> 0x100, 0x4 -> 0x80, 0x4000 -> 0x400, 0x10000 -> 0x800, 0x40000 -> 0x1000, 0x100000 -> 0x2000, 0x200000 -> 0x4000, 0x400000 -> 0x8000, 0x1000000 -> 0x20000
	public static uint InnerLoaderFlags( uint raw )
	{
		(uint From, uint To)[] map = [(0x10, 2), (0x20, 0x10), (0x40, 0x20), (0x2, 0x40), (0x100, 1), (0x200, 0x100), (0x4, 0x80), (0x4000, 0x400), (0x10000, 0x800), (0x40000, 0x1000), (0x100000, 0x2000), (0x200000, 0x4000), (0x400000, 0x8000), (0x1000000, 0x20000)];
		var inner = 0u;
		foreach ( var (from, to) in map )
			if ( (raw & from) != 0 )
				inner |= to;
		return inner;
	}

	/// <summary>
	/// Whether the loader reads a queue model's animation files and creates its channels' clips: only when the inner flags have bit 1 or 8 (the clip loop of <c>0x461F10</c>, test <c>(flags &amp; 9)</c>) or 2 or 8 (<c>0x473E30</c>, test <c>(flags &amp; 10)</c>).
	/// The queue models' inner flags have none, so the <c>queendm</c>, <c>quebin1m</c> and <c>quebin2m</c> clips of the archive (the fire0..fire04 torch frames) are never loaded and the torches stand on frame 0.
	/// </summary>
	// [BIN:TPW-EXE:0x00462269 clip loop gate] the loop over the twelve animation letters (%s%s%c.md2 and %s%s%c%d.md2, 0x4623B4 and 0x4623E0) runs only for flags & 9 (and eax, 9 at 0x462269, tested at 0x46233A, skipped by the jump to 0x46238E); 0x473E30 for flags & 10 (and ebp, 0xA at 0x46274A, call 0x462760) in the tail of 0x461F10; the creator's own 0x473E30 call (0x4631D6) only resets the instance's one channel to the none animation 12 (0x473E4B) and sets 0x10 when no animation has clips
	public static bool LoadsAnimation( int model ) => (InnerLoaderFlags( LoadFlags( model ) ) & 0xB) != 0;

	/// <summary>The queue table 0x7630B0: {model index (class 2), rotation, link mask}.</summary>
	private static readonly (int Model, int Rotation, byte Mask)[] Table =
	{
		(2, 0, 0x11), (2, 90, 0x44),
		(1, 0, 0x10), (1, 90, 0x40), (1, 180, 0x01), (1, 270, 0x04),
		(3, 0, 0x14), (3, 90, 0x50), (3, 180, 0x41), (3, 270, 0x05),
		(0, 0, 0x00)
	};

	/// <summary>The cardinal link bits set (<c>0x522810</c>).</summary>
	private static int CardinalCount( byte links ) => System.Numerics.BitOperations.PopCount( (uint)(links & 0x55) );

	/// <summary>
	/// The chooser's class-2 result for the queue cell (<paramref name="x"/>, <paramref name="y"/>) and the yaw its model is placed with.
	/// </summary>
	// [BIN:TPW-EXE:0x00535DD0 queue chooser] for type 3 the table 0x7630B0 (11 rows) is matched against +0x0C, first exactly, then the first row whose mask the byte contains; a straight (two cardinal links, opposite) adds 180 to the rotation unless the queue link +0x0D is 0x40 or 0x10 (0x535E28..0x535E5C); a corner whose queue link and links are 0x40/0x50, 0x10/0x14, 0x01/0x41 or 0x04/0x05 takes the next model (0x535F6A..0x535FC5); then for each cardinal link to a type-1 cell that links back the model index rises by 3 while the cell's second-layer record (0x4D0AF0, the owner record for the types 0xC and 0x11) has none of the bits 0x0F at +0x0E (0x536037..0x5360DD)
	public static QueueModelTile Choose( ParkCellMap map, int x, int y )
	{
		ArgumentNullException.ThrowIfNull( map );
		var links = map.LinksAt( x, y );
		var queueLink = map.QueueLinkAt( x, y );
		var straight = CardinalCount( links ) == 2 && (((links & 0x01) != 0 && (links & 0x10) != 0) || ((links & 0x04) != 0 && (links & 0x40) != 0));
		var offset = straight && queueLink is not (0x40 or 0x10) ? 180 : 0;
		(int Model, int Rotation, byte Mask)? row = null;
		foreach ( var entry in Table )
		{
			if ( entry.Mask == links )
			{
				row = entry;
				break;
			}
		}
		if ( row == null )
		{
			foreach ( var entry in Table )
			{
				if ( (links & entry.Mask) == entry.Mask )
				{
					row = entry;
					break;
				}
			}
		}
		var model = row!.Value.Model;
		var rotation = row.Value.Rotation + offset;
		var corner = CardinalCount( links ) == 2 && ((links & 0x01) != 0 || (links & 0x10) != 0) && ((links & 0x04) != 0 || (links & 0x40) != 0);
		if ( corner && ((queueLink == 0x40 && links == 0x50) || (queueLink == 0x10 && links == 0x14) || (queueLink == 0x01 && links == 0x41) || (queueLink == 0x04 && links == 0x05)) )
			model++;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var bit = GuestPathGrid.LinkValue( direction );
			if ( (links & bit) == 0 )
				continue;
			var (nx, ny) = (x + GuestPathGrid.Directions[direction].DX, y + GuestPathGrid.Directions[direction].DY);
			// OpenTPW's track layer holds no track on a queue cell, so its second-layer record never has the bits 0x0F.
			if ( map.InBounds( nx, ny ) && map.TypeAt( nx, ny ) == ParkCellType.Path && (map.LinksAt( nx, ny ) & PathLinkWriter.Opposite( bit )) != 0 )
				model += 3;
		}
		var yaw = 360 - rotation % 360;
		return new QueueModelTile( model, yaw == 360 ? 0 : yaw );
	}
}

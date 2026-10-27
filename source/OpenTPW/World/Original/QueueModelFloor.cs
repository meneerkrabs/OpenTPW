namespace OpenTPW;

/// <summary>
/// The floor of a queue model, which the original gives a queue piece as it gives an object (docs/PATHS.md, "Queue models on the terrain"; docs/OBJECTS.md, "Floors"): the skirt read at load
/// (<see cref="ObjectSkirt"/>: the opaque slot, the clamped wrap bits, the base mesh's normalised texture coordinates), and, with the procedural terrain textures on, the skirt word of the cell and the composite
/// of the base art over the cell's procedural ground (<see cref="ObjectFloorBase.Compose"/>). Presentation only.
/// </summary>
// [BIN:TPW-EXE:0x00522900 queue models] the queue models are loaded with load flag 1 (0x12423 / 0x112423) and the footprint {1, 1} at 0x816D68: 0x46A5A0 skips its footprint-mask scan (the loader's drape flag has set header bit 0x80, so DAT_007afe18 = 0) and 0x46AA10 is run with the 0x80 argument, which wants every cell of the 1 x 1 grid: one entry for the base mesh
// [BIN:TPW-EXE:0x00463060 queue floor] the placement with the creation flags 0x33A (bit 8) writes the skirt word of the entry's cell (the instance's cell) and makes the composite of the footprint box (0x57ACE0) while the texturing is on and the base record has flag 0x40; the refresh 0x5365D0 then writes the hole (0x46DF50 mode 8)
internal sealed class QueueModelFloor
{
	private QueueModelFloor( ModelFile model, ObjectSkirt skirt )
	{
		Model = model;
		Skirt = skirt;
		Floor = ObjectFloor.ForSkirt( model, skirt );
	}

	public ModelFile Model { get; }

	public ObjectSkirt Skirt { get; }

	/// <summary>The skirt applied to the model's meshes (the opaque and clamped slots, the normalised coordinates of the base mesh).</summary>
	public ObjectFloor Floor { get; }

	/// <summary>The queue model's single skirt entry (the 1 x 1 grid's cell).</summary>
	public ObjectSkirtEntry Entry => Skirt.EntryAt( 0, 0 );

	/// <summary>Whether a placed instance makes a composite: the base flag and an entry with a skirt.</summary>
	public bool HasComposite => Skirt.HasBase && Entry.HasSkirt;

	/// <summary>The floor of <paramref name="model"/> over the level's terrain.</summary>
	public static QueueModelFloor Create( ModelFile model, ModelHeightfield field )
	{
		// the footprint {1, 1} of 0x816D68; the drape load flag has set the header bit the file lacks
		var shape = ObjectMapGrid.Parse( new[] { "X" }, _ => (4, (byte)0) )!;
		return new QueueModelFloor( model, ObjectSkirt.Build( shape, model, HoardingTerrain.Of( field ), drapeLoaded: true ) );
	}

	/// <summary>
	/// The composite of the instance on cell (<paramref name="x"/>, <paramref name="y"/>) turned by <paramref name="yaw"/> (the original's 0x463060 then 0x57ACE0): the skirt word written to the cell, then the
	/// base art blended over the cell's procedural ground, N × N RGBA in the original's device order; null without a composite.
	/// </summary>
	public byte[]? Compose( ModelHeightfield field, int x, int y, int yaw, ProceduralTerrainTexturer texturer, Func<int, ObjectFloorBase.SlotArt?> art )
	{
		if ( !HasComposite )
			return null;
		var words = new OriginalCellWords( field );
		var (cellX, cellZ) = ObjectFloorBase.Target( x, y, yaw, 0, 0 );
		words.Write( cellX, cellZ, OriginalCellWords.SkirtWord( words.Word( cellX, cellZ ), Entry, yaw ) );
		var composites = ObjectFloorBase.Compose( words, (x, y, x, y), Model.Textures.Count, texturer, art );
		return composites.Count > 0 ? composites[^1].Rgba : null;
	}
}

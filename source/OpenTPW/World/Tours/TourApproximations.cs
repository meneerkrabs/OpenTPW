namespace OpenTPW;

/// <summary>
/// The tour-ride slice's approximation register (docs/TOURS.md). Each entry is tagged <c>[APPROX:TOUR-NNN]</c> at its code site; the words before the
/// colon name the topic. Everything else of the slice is traced from the Windows binary (BIN labels at the code sites).
/// </summary>
public static class TourApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("TOUR-005", "TOUR-GROUND: the cars clear the height map (.hmp) of the mesh instance on a cell, its byte x 100/255 plus the instance height, as the cell lookup 0x00452AE0 reads it: the second-layer piece instance first (0x0053BF30: a kart or water track tile, its 2 x 2 map indexed from the instance corner two cells over by the engine rotation, 0x00522B90 / 0x00522D50 / 0x00466F90, on the tile height 0x00466D70; PieceGround) and else the object that owns the cell, found by the cell's footprint as tp.exe finds it by the owner word of a type-4, 9 or 10 cell and the class-3 thing there (0x527E80, 0x527D60), its map read at the object's rotation; every shipped object map has its shape's size, so the original's rebuild of a mis-sized map from the mesh (0x00451880) never runs for the catalogue; a cell with no such instance gives the terrain as the original does (every tour caller passes no owner out-pointer). The park fence's posts are the second layer's family 5 (hoarding, kind 25: the selector 0x53A540, the instancer 0x53AFA0 through 0x522F20 from table 0x763608, ho_fos1, ho_end1 and ho_fob1): 0x53BF30 answers a cell's record handle for any kind (an extension, 0xC or 0x11, through its parent word, a parent word of 0 logs and answers none), so a tour car over a fence post's cell or one of the three cells beside it is lifted by the hoarding instance's own map, whose 2 x 2 file ships in every theme (every hoarding mesh has one, so the rebuild 0x451880 never runs for them); OpenTPW draws no park fence mesh, so it has no instance to sample and such a cell answers the object or the terrain. Station tiles (kinds 13 and 18) take family 1, for which 0x53AFA0 builds nothing: the record's instance is stored only from the refresh's three instancers (0x522B90, 0x522D50, 0x522F20), so a station cell's own handle is none. Open: the park fence's instances (the post's piece, its transform and height) and the expected size the mesh loader 0x4629D0 passes to the map loader 0x451640 for track pieces (the shipped piece maps are the tile's 2 x 2 cells; the rebuild runs only for another size)"),
	};
}

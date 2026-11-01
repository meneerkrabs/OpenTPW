using Veldrid;

namespace OpenTPW.Hud;

/// <summary>
/// An object (a thing of type 3) the map's cell classes name: its category (<c>Info.WhichUIType</c>, type record <c>+0x4AC</c>), the thing flag bits the class writer tests (<c>+0x32</c>), and the values the
/// renderer reads from it when the map is drawn. The values are taken again at every render event, as the original's renderer reads the thing then.
/// </summary>
internal sealed class ParkMapThing
{
	/// <summary>A number of the object that differs from every other object's (the thing id word the original keeps in its class table).</summary>
	public required int Id { get; init; }
	/// <summary>The kind word <c>+0x4AC</c>: 0 rides, 1 shops, 2 sideshows, 3 features; 4 and above (fixed items) have no class.</summary>
	public required int Category { get; init; }
	/// <summary>Thing flag <c>+0x32</c> bit 1 (UsageInfo.ProvidesRelief, the toilets).</summary>
	public bool Relief { get; init; }
	/// <summary>Thing flag <c>+0x32</c> bit 0x40 (UsageInfo.HoldsLitter, the litter bins).</summary>
	public bool Litter { get; init; }
	/// <summary>Thing flag <c>+0x32</c> bit 0x10 (UsageInfo.ProvidesSecurity, the security cameras).</summary>
	public bool Security { get; init; }
	/// <summary>Thing flag <c>+0x32</c> bit 2 (UsageInfo.ChillsYouOut, the staff rooms).</summary>
	public bool StaffRoom { get; init; }
	/// <summary>The placed object, null for a stand-in of a test.</summary>
	public OriginalObject? Item { get; init; }

	/// <summary>Whether the open word <c>+0x68</c> is 0 (<c>0x4E0440</c>): the outline of a closed object is black, that of an open one green.</summary>
	public bool Closed { get; set; }
	/// <summary>The fill of the object's repair float <c>+0x44</c> (default value mode); the host fallback while the economy holds no current state of it.</summary>
	public RgbaByte Repair { get; set; }
	/// <summary>The low byte of <c>0x4E0860(0)</c>, the excitement value mode's value; null while the economy cannot supply it.</summary>
	public int? Excitement { get; set; }
	/// <summary>The value of <c>0x4E1E30</c>, the satisfaction value mode's value; null while the economy cannot supply it.</summary>
	public int? Satisfaction { get; set; }
}

/// <summary>The map cells as the class writer <c>0x5F2050</c> reads them.</summary>
internal interface IParkMapCells
{
	int Width { get; }
	int Height { get; }
	/// <summary>The cell's own type (<c>+8</c>), as the cell predicates 0x536310..0x5363E0 read it.</summary>
	byte Type( int x, int y );
	/// <summary>Flag <c>0x40</c> of the flag word <c>+0xE</c> (<c>0x535DB0(0x40)</c>): land the player does not own.</summary>
	bool Unowned( int x, int y );
	/// <summary>The cell's structure record is a fence post, or the extension of one (<c>0x536450</c>).</summary>
	bool FencePost( int x, int y );
	/// <summary>The cell's structure record is a kart or water track tile (a track tile type, or the extension of one with an owner; <c>0x5363F0</c>).</summary>
	bool TrackStructure( int x, int y );
	/// <summary>The objects found through the structure record's owner (the ride the tile belongs to), in thing-list order.</summary>
	IEnumerable<ParkMapThing> TrackOwners( int x, int y );
	/// <summary>The objects on the thing list of the cell the owner word <c>+0x10</c> of (<paramref name="x"/>, <paramref name="y"/>) names, in thing-list order.</summary>
	IEnumerable<ParkMapThing> ThingsOfOwnerWord( int x, int y );
}

/// <summary>The map window's cell classes and layers (tp.exe <c>0x5F2050</c> writes the classes, <c>0x5F2380</c> shows them).</summary>
internal static class ParkMapClasses
{
	public const int None = 0;
	/// <summary>The first object class: a ride (thing kind 0); the classes run 1 rides, 2 shops, 3 sideshows, 4 to 7 features, 8 track tiles of a kart or water ride.</summary>
	public const int Ride = 1;
	public const int Shop = 2;
	public const int Sideshow = 3;
	public const int Feature = 4;
	/// <summary>A feature with flag 0x41 (a toilet or a litter bin), shown with the handymen.</summary>
	public const int FeatureHandyman = 5;
	/// <summary>A feature with flag 0x10 (a security camera), shown with the guards.</summary>
	public const int FeatureGuard = 6;
	/// <summary>A feature with flag 2 (a staff room), shown with every staff role.</summary>
	public const int FeatureStaffRoom = 7;
	public const int TrackTile = 8;
	/// <summary>A cell whose structure record is a fence post or its extension: always shown.</summary>
	public const int Fence = 9;
	public const int Path = 10;
	public const int Queue = 11;
	public const int Unowned = 12;

	/// <summary>The layer word bits of the category buttons (<c>[map+0x18]</c>): rides 1, shops 2, sideshows 4, features 8, tracks 0x10, visitors 0x20.</summary>
	public const uint RidesLayer = 1, ShopsLayer = 2, SideshowsLayer = 4, FeaturesLayer = 8, TracksLayer = 0x10, VisitorsLayer = 0x20;

	/// <summary>
	/// The class writer. Per cell, in this order: a fence record is class 9; a cell of type 4, 9 or 10, a kart or water tile or a type 0x15 cell belongs to the first object with a kind 0 to 3 on the
	/// thing list of its owner's cell (class 1 to 7, and 8 for a tile or a type 0x15 cell of a ride); a path (type 1) is class 10, a queue (type 3) 11, and land flagged 0x40 class 12.
	/// </summary>
	// [BIN:TPW-EXE:0x5f2050 map class writer] per cell [esi]/[esi+4] are zeroed, then 0x536450 -> 9 (0x5f20f6); 0x536380 (type 4), 0x536340 (type 9), 0x536350 (type 10), 0x5363f0 or 0x5363e0 (type 0x15) enter the object path at 0x5f2180, else 0x536310 (type 1) -> 10, 0x536320 (type 3 or 9) -> 11, 0x535db0(0x40) -> 12; the object path reads the owner word (the structure record's for a 0x5363f0 cell, else the cell's +0x10), takes the first thing of type 3 on that cell's list (+0x24, next +0xa) and files the cell under the kind of its type record +0x4ac: 0 -> 1 (8 when the cell is not type 4 and is a 0x5363f0 or 0x5363e0 cell), 1 -> 2, 2 -> 3, 3 -> 4..7 by the thing flags +0x32 (0x41 -> 5, 0x10 -> 6, 2 -> 7, else 4), a kind above 3 leaves the cell to the next thing
	public static void Write( IParkMapCells cells, int[] classes, ParkMapThing?[] owners )
	{
		var width = cells.Width;
		var height = cells.Height;
		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				var index = y * width + x;
				classes[index] = None;
				owners[index] = null;
				if ( cells.FencePost( x, y ) )
				{
					classes[index] = Fence;
					continue;
				}
				var type = cells.Type( x, y );
				var tile = cells.TrackStructure( x, y );
				if ( type is ParkCellTypes.Object or ParkCellTypes.Entrance or ParkCellTypes.Exit || tile || type == ParkCellTypes.TrackConnection )
				{
					foreach ( var thing in tile ? cells.TrackOwners( x, y ) : cells.ThingsOfOwnerWord( x, y ) )
					{
						var kind = thing.Category switch
						{
							0 => type != ParkCellTypes.Object && (tile || type == ParkCellTypes.TrackConnection) ? TrackTile : Ride,
							1 => Shop,
							2 => Sideshow,
							3 => thing.Relief || thing.Litter ? FeatureHandyman : thing.Security ? FeatureGuard : thing.StaffRoom ? FeatureStaffRoom : Feature,
							_ => None
						};
						if ( kind == None )
							continue;
						classes[index] = kind;
						owners[index] = thing;
						break;
					}
				}
				else if ( type == (byte)ParkCellType.Path )
					classes[index] = Path;
				else if ( type is (byte)ParkCellType.Queue )
					classes[index] = Queue;
				else if ( cells.Unowned( x, y ) )
					classes[index] = Unowned;
			}
		}
	}

	/// <summary>Whether a class is drawn by the layer word and the staff role the map holds (the jump table at <c>0x5F2F9C</c>); the path, queue, unowned and fence classes are always drawn.</summary>
	// [BIN:TPW-EXE:0x5f2f9c class layers] class 1 tests [map+0x18] bit 1, 2 bit 2, 3 bit 4, 4 bit 8, 5 [map+0x1c] == 1, 6 [map+0x1c] == 3, 7 [map+0x1c] != 0, 8 bit 0x10; the classes 9 to 12 go straight to their records (0xf86b48, 0xf86b00, 0xf86b24, 0xf86b6c)
	public static bool Shown( int cls, uint layers, int staff ) => cls switch
	{
		Ride => (layers & RidesLayer) != 0,
		Shop => (layers & ShopsLayer) != 0,
		Sideshow => (layers & SideshowsLayer) != 0,
		Feature => (layers & FeaturesLayer) != 0,
		FeatureHandyman => staff == 1,
		FeatureGuard => staff == 3,
		FeatureStaffRoom => staff != 0,
		TrackTile => (layers & TracksLayer) != 0,
		Fence or Path or Queue or Unowned => true,
		_ => false
	};

	/// <summary>Whether the excitement and satisfaction value modes colour the class (classes 1, 2, 3 and 8, whatever the layers say; <c>0x5F2461..0x5F2596</c>).</summary>
	// [BIN:TPW-EXE:0x5f2465 value modes] the class is tested against 1, 2, 3 and 8 (0x5f246a..0x5f2490): then [map+0x20] 2 fills with the ramp of 0x4e0860(0) and 3 with the ramp of 0x4e1e30, and the class switch (and its layer test) is skipped
	public static bool TakesValueMode( int cls ) => cls is Ride or Shop or Sideshow or TrackTile;
}

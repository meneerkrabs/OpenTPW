namespace OpenTPW;

/// <summary>A chosen track piece: the family (3 kart, 4 water, 0 none), the piece index of the family table and the rotation in degrees.</summary>
public readonly record struct TrackPiece( int Family, int Piece, int Rotation )
{
	public static TrackPiece None => default;
	public bool IsNone => Piece == 0;
}

/// <summary>One entry of a piece table (original: 12 bytes, handle, name, arm byte): the mesh stem and the arm mask the instancer shows.</summary>
public readonly record struct TrackPieceEntry( int Index, string Name, byte ArmMask );

/// <summary>
/// Q1 of the kart and water track trace: which piece a track cell draws. A table per family maps the connection mask to a
/// piece and rotation (an exact match first, else the first entry whose mask is a subset), then four adjustments follow in
/// order (flow rotation of straights, bridge pieces, the second corner and the second crossing family).
/// </summary>
public static class TrackPieceSelector
{
	public const int PieceNone = 0;
	public const int PieceEnd = 1;
	public const int PieceStraight = 2;
	public const int PieceCorner = 3;
	public const int PieceCrossing = 4;
	public const int PieceBridgeStem = 9;
	public const int PieceBridgeUp = 12;
	public const int PieceBridgeDown = 15;
	public const int PieceBridgeAbove = 18;
	public const int PieceCorner2 = 19;
	public const int SecondFamilyOffset = 16;
	public const int KartPieceCount = 20;
	public const int WaterPieceCount = 25;

	// [BIN:STP-PPC:0x1008A7C4 piece selection] the kart/water tables (data:0x3D9EC, 0x3DAB0): first exact mask match wins, else the first entry whose mask is a subset of the cell mask
	private static readonly (byte Mask, int Piece, int Rotation)[] Table =
	{
		(0x11, PieceStraight, 0), (0x44, PieceStraight, 90),
		(0x10, PieceEnd, 0), (0x40, PieceEnd, 90), (0x01, PieceEnd, 180), (0x04, PieceEnd, 270),
		(0x14, PieceCorner, 0), (0x50, PieceCorner, 90), (0x41, PieceCorner, 180), (0x05, PieceCorner, 270),
		(0x15, 5, 0), (0x45, 6, 0), (0x51, 7, 0), (0x54, 8, 0),
		(0x55, PieceCrossing, 0),
		(0x00, PieceNone, 0)
	};

	/// <summary>The mask table as (mask, piece, rotation) in lookup order.</summary>
	public static IReadOnlyList<(byte Mask, int Piece, int Rotation)> MaskTable => Table;

	/// <summary>
	/// The piece table of a family: name stems (the mesh and height profile are <c>prefix + stem</c>) and the arm masks, as the original's
	/// 12-byte entries hold them. An entry without a name of its own reuses the previous one's mesh.
	/// </summary>
	// [BIN:STP-PPC:0x1008A7C4 piece tables] kart 20 entries (data:0x3DEAC), water 25 (data:0x3DF9C): 0 none; 1 Q; 2 S; 3 B; 4 X; 5–8 X with arm masks; 9–11 H; 12–14 H_U; 15–17 H_D; 18 H_A; 19 B2; 20–24 X2 (water only)
	public static IReadOnlyList<TrackPieceEntry> PieceTable( TrackFamily family )
	{
		var count = family switch
		{
			TrackFamily.Kart => KartPieceCount,
			TrackFamily.Water => WaterPieceCount,
			_ => throw new ArgumentOutOfRangeException( nameof( family ) )
		};
		var entries = new List<TrackPieceEntry>( count );
		for ( var index = 0; index < count; index++ )
			entries.Add( new TrackPieceEntry( index, StemOf( index ), ArmOf( index ) ) );
		return entries;
	}

	private static string StemOf( int piece ) => piece switch
	{
		0 => "",
		1 => "Q",
		2 => "S",
		3 => "B",
		>= 4 and <= 8 => "X",
		>= 9 and <= 11 => "H",
		>= 12 and <= 14 => "H_U",
		>= 15 and <= 17 => "H_D",
		18 => "H_A",
		19 => "B2",
		>= 20 and <= 24 => "X2",
		_ => throw new ArgumentOutOfRangeException( nameof( piece ) )
	};

	/// <summary>The arm mask the instancer passes for a piece (which arms of the mesh show).</summary>
	public static byte ArmOf( int piece ) => piece switch
	{
		1 or 10 or 13 or 16 => 0x01,
		2 or 11 or 14 or 17 => 0x11,
		4 or 20 => 0x55,
		5 or 21 => 0x15,
		6 or 22 => 0x54,
		7 or 23 => 0x51,
		8 or 24 => 0x45,
		_ => 0
	};

	/// <summary>
	/// The piece a cell of <paramref name="type"/> with connection <paramref name="mask"/>, flow <paramref name="link"/> and
	/// <paramref name="flags"/> draws. Types 3 and 11 use the kart table (family 3), 16 and 25 the water table (family 4); every
	/// other type, the station tiles included, draws nothing (the original's code 8: the station's own mesh covers it).
	/// </summary>
	public static TrackPiece Select( byte type, byte mask, byte link, byte flags )
	{
		int family = type switch
		{
			3 or TrackCellTypes.KartOrigin => 3,
			TrackCellTypes.WaterOrigin or TrackCellTypes.WaterAlternate => 4,
			_ => 0
		};
		if ( family == 0 )
			return TrackPiece.None;
		var piece = Lookup( mask, out var rotation );
		// 1. a straight whose flow runs along −X or +Y turns 180° so its direction marker follows the flow
		if ( piece == PieceStraight && link is TrackHeading.NegX or TrackHeading.PosY )
			rotation = (rotation + 180) % 360;
		// 2. pieces that are neither corners nor junctions become bridge pieces by the flag bits
		if ( piece <= PieceStraight )
		{
			if ( (flags & TrackCell.FlagBridge) != 0 )
				piece += PieceBridgeStem;
			else if ( (flags & TrackCell.FlagBridgeUp) != 0 )
				piece += PieceBridgeUp;
			else if ( (flags & TrackCell.FlagBridgeDown) != 0 )
				piece += PieceBridgeDown;
			else if ( (flags & TrackCell.FlagBridgeAbove) != 0 )
				piece = PieceBridgeAbove;
		}
		// 3. the second corner mesh serves the flow that runs the other way around the bend (types 11 and 16)
		if ( type is TrackCellTypes.KartOrigin or TrackCellTypes.WaterOrigin && piece == PieceCorner
			&& (link, mask) is (TrackHeading.NegX, 0x50) or (TrackHeading.PosY, 0x14) or (TrackHeading.NegY, 0x41) or (TrackHeading.PosX, 0x05) )
			piece += SecondFamilyOffset;
		// 4. water crossings and tees by their flow bits (multi-bit links come from the line op that crosses two lines, TRACK-004)
		if ( type == TrackCellTypes.WaterOrigin && piece is >= PieceCrossing and <= 8 )
		{
			// [BIN:TPW-EXE:0x00539670 junction links] the multi-bit links come from the line op (op 0x82 of 0x53AAC0, handler 0x539670): a tile with more than two arms ORs the line's direction into +0xD (a crossing of two lines gets 0x14, 0x41, 0x50 or 0x05), a tile with two arms or fewer takes the direction alone; the circuit walk 0x52A970 writes +0xD only at corners (0x52B1A9..0x52B1F5)
			// [BIN:TPW-EXE:0x0053B280 clear op] the clear op (ClearCell 0x53B280 -> 0x5227F0) ANDs the line's direction bits out of a junction's link and, in its (-1, -1) form, clears the other axis' bit pair (0x522730, linked by 0x522850); TrackEditor.ClearCell ports it
			if ( link == 0x14 )
				rotation = 90;
			else if ( link == 0x41 )
				piece += SecondFamilyOffset;
			else if ( link == 0x50 )
			{
				rotation = 270;
				piece += SecondFamilyOffset;
			}
		}
		return new TrackPiece( family, piece, rotation );
	}

	/// <summary>Table lookup: the exact mask, else the first entry whose mask is a subset of <paramref name="mask"/>.</summary>
	public static int Lookup( byte mask, out int rotation )
	{
		foreach ( var (entryMask, piece, entryRotation) in Table )
		{
			if ( entryMask == mask )
			{
				rotation = entryRotation;
				return piece;
			}
		}
		foreach ( var (entryMask, piece, entryRotation) in Table )
		{
			if ( (entryMask & ~mask) == 0 )
			{
				rotation = entryRotation;
				return piece;
			}
		}
		rotation = 0;
		return PieceNone;
	}
}

namespace OpenTPW;

/// <summary>
/// The add-on object a kind 12 or 13 section points at (section +0xC its instance, +0x10 its type id): its type and its animation channel 0, which the
/// Space Racers Teleport's vehicle hook drives (<see cref="BumpController"/>).
/// </summary>
public interface ITrackAddOn
{
	/// <summary>The add-on's type id (<c>Info.Id</c>).</summary>
	int TypeId { get; }
	/// <summary>The AnimID channel 0 plays (the channel record's +4, read by 0x00473FB0).</summary>
	int Animation { get; }
	/// <summary>The AnimID queued on channel 0 (+0x24, read by 0x00473FF0); <see cref="ObjectAnimator.NoAnimation"/> when none waits.</summary>
	int DeferredAnimation { get; }
	/// <summary>Drops the clip queued on channel 0 (0x00473270).</summary>
	void FlushDeferred();
	/// <summary>Triggers an animation on channel 0 without the at-once flag 2 (0x004732A0): it waits behind a clip that still runs.</summary>
	void Queue( int animation, bool loop );
}

/// <summary>
/// The track add-ons (docs/TRACKS.md "Add-ons"): the objects of <c>AddOn.UpgradeType</c> 1 (jumps, tunnels, the teleport) that stand on a kart or water ride's
/// straight tiles in place of their pieces. This class holds the rules that need no level: the build tool's hover position and the open ends a circuit walk
/// shows or hides; <see cref="Level"/> offers, places and links the add-ons.
/// </summary>
public static class TrackAddOns
{
	/// <summary><c>AddOn.UpgradeType</c> of a track add-on; type 2 (the Tubes Of Zob Extra Tube) is placed as an ordinary object.</summary>
	public const int TrackUpgradeType = 1;
	/// <summary>The Space Racers Teleport's type id (0xDAC, 3500), the one add-on whose type the vehicles test.</summary>
	public const int TeleportTypeId = 0xDAC;
	/// <summary>Within this many section units of the teleport's first tile's centre a vehicle vanishes; this far past the second tile's centre it shows again.</summary>
	public const int TeleportReach = 0x4B0;
	/// <summary>The teleport's animation as a vehicle passes (its <c>m</c> clip) and its idle loop (its <c>i</c> clip).</summary>
	public const int PassAnimation = 5, IdleAnimation = 2;
	/// <summary>The class-1 dummy numbers of an add-on's open ends (the record's third word): 1 the end behind, 0x10 the end ahead.</summary>
	public const uint EndBehind = 0x01, EndAhead = 0x10;

	/// <summary>
	/// The model node of an add-on's open end <paramref name="number"/> (<see cref="EndBehind"/> or <see cref="EndAhead"/>): the node header+0x46 + i of the first dummy record i
	/// with class bit 1 and that number in its third word, or −1 when the model has none.
	/// </summary>
	// [BIN:TPW-EXE:0x0044B3C0 dummy by number] the lookup takes class 1 when the asked class has none of 0x3DA1F82, and returns the first record of the 5-word table +0x7C (count +0x48) whose word 0 has the class and word 2 the number; the walk adds the header word +0x46 to find the node (0x0052AFA1)
	public static int EndNode( ModelFile model, uint number )
	{
		ArgumentNullException.ThrowIfNull( model );
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			var record = model.DummyAttributes[index];
			if ( (record.Type & 1) == 0 || record.Reserved.Length < 4 || BitConverter.ToUInt32( record.Reserved, 0 ) != number )
				continue;
			var node = model.Counts.Unknown46 + index;
			return node < model.Nodes.Count ? node : -1;
		}
		return -1;
	}

	/// <summary>Whether <paramref name="entry"/> is offered as an add-on of a ride: its <c>AddOn.UpgradesId</c> names one.</summary>
	public static bool IsAddOn( ObjectCatalogEntry entry ) => entry.Settings.GetInt( "AddOn.UpgradesId" ) != 0;

	/// <summary>Whether <paramref name="entry"/> is a track add-on: an add-on of <c>AddOn.UpgradeType</c> 1, placed on its ride's track by the add-on rule.</summary>
	// [BIN:TPW-EXE:0x0046D605 add-on tool] the build tool's type setter 0x0052F880 gets the upgrade selection (type +0x9C, WhichTrackType) only for a type whose +0x288 (AddOn.UpgradeType) is 1, and 0 for every other type; the click (0x00524B45) and the hover (0x0052369D) take the add-on branch while that selection is set
	public static bool IsTrackAddOn( ObjectCatalogEntry entry ) => IsAddOn( entry ) && entry.Settings.GetInt( "AddOn.UpgradeType" ) == TrackUpgradeType;

	/// <summary>
	/// The cell and angle the build tool places a track add-on at with the pointer on cell (<paramref name="x"/>, <paramref name="y"/>), for the ride whose placement anchor
	/// is (<paramref name="anchorX"/>, <paramref name="anchorY"/>) turned <paramref name="rideRotation"/>: the cell snaps to the even cells counted from the anchor; when the
	/// tile there is a track tile, the angle follows its flow link (0x01 → 0, 0x04 → 270, 0x10 → 180, 0x40 → 90; any other link keeps <paramref name="rotation"/>); then the
	/// cell moves by the add-on's <c>Bumper.*Adjust</c> of the angle (North at 0, West at 90, South at 180, East at 270) and by the ride's rotation (90: x − 1; 180: x − 1 and
	/// y + 1; 270: y + 1). The result is a build click: the placement centres the footprint on it (<see cref="Level.GetCentredAnchor(ObjectShape, int, int, int)"/>), so the
	/// add-on's '+' cells cover the hovered tile and, for a two-tile add-on, the next tile towards +X or +Y.
	/// </summary>
	// [BIN:TPW-EXE:0x005236B0 add-on hover] with the upgrade selection set and the pointer on the 128 x 128 map (0x004D8300), the cell becomes anchor + 2 · floor((cell − anchor) / 2) on both axes (0x005236E3..0x0052374D, the anchor being the ride's cell 0x7639A4/A8); on the map again, the cell or its head (an extension cell 0x0053AD90 names its head) that is a track cell (0x0053AD30) gives its flow byte +0xD (0x00522850) to the byte table 0x00524750 and the jump table 0x0052473C (1 → 0, 4 → 0x10E, 0x10 → 0xB4, 0x40 → 0x5A, the rest keep the angle), set by 0x0052F1B0; then angle 0, 0x5A, 0xB4 and 0x10E add the type's +0xA4/+0xA8, +0xBC/+0xC0, +0xB4/+0xB8 and +0xAC/+0xB0 (North, West, South and East X/YAdjust, 0x00523804..0x005238A5) and the ride's angle +0x10 of 0x8186AC adds (−1, 0) at 0x5A, (−1, +1) at 0xB4 and (0, +1) at 0x10E (0x005238AB..0x005238D7); the cell is the click's (0x8186B8/B4) and the placement 0x00528A70 checks it with the type 4 and that angle (0x0052390E)
	public static (int X, int Y, int Rotation) HoverTarget( ObjectSettings settings, TrackCellLayer tracks, int anchorX, int anchorY, int rideRotation, int x, int y, int rotation )
	{
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( tracks );
		if ( !OnMap( x, y ) )
			return (x, y, rotation);
		x = anchorX + 2 * FloorHalf( x - anchorX );
		y = anchorY + 2 * FloorHalf( y - anchorY );
		if ( !OnMap( x, y ) )
			return (x, y, rotation);
		if ( x < tracks.Width && y < tracks.Height )
		{
			var cell = tracks.Get( x, y );
			if ( cell.IsExtension )
			{
				var (headX, headY) = tracks.CellOf( cell.Parent );
				cell = tracks.Get( headX, headY );
			}
			if ( TrackCellTypes.IsWalkable( cell.Type ) )
				rotation = cell.Link switch
				{
					TrackHeading.NegY => 0,
					TrackHeading.PosX => 270,
					TrackHeading.PosY => 180,
					TrackHeading.NegX => 90,
					_ => rotation
				};
		}
		var side = rotation switch { 0 => "North", 90 => "West", 180 => "South", 270 => "East", _ => null };
		if ( side != null )
		{
			x += settings.GetInt( $"Bumper.{side}XAdjust" );
			y += settings.GetInt( $"Bumper.{side}YAdjust" );
		}
		switch ( rideRotation )
		{
			case 90:
				x--;
				break;
			case 180:
				x--;
				y++;
				break;
			case 270:
				y++;
				break;
		}
		return (x, y, rotation);
	}

	/// <summary>The original's map test 0x004D8300: both coordinates in 0..127.</summary>
	private static bool OnMap( int x, int y ) => x is >= 0 and < 0x80 && y is >= 0 and < 0x80;

	/// <summary>Half a cell offset rounded towards minus infinity, as the hover rounds an odd negative offset down before halving (0x00523701..0x0052373C).</summary>
	private static int FloorHalf( int offset ) => offset >= 0 ? offset / 2 : (offset - 1) / 2;

	/// <summary>One add-on's open ends after a circuit walk: the add-on on the tile (<see cref="X"/>, <see cref="Y"/>) hides its end behind (dummy 1) and its end ahead (dummy 0x10) or shows them.</summary>
	public readonly record struct OpenEnds( int X, int Y, bool HideBehind, bool HideAhead );

	/// <summary>
	/// The open ends a circuit walk sets: every add-on tile adds whether the tile two cells behind and ahead along its flow link is a track tile with a piece of its own or a
	/// station (bit 1 behind, 0x10 ahead), and the second tile of a run of add-on tiles hides the end on each side that has such a tile and shows the other. The bits are not
	/// cleared between runs: the walk resets them only where its run counter would come back from −1, which no write of the counter does, so a later add-on takes the bits of
	/// every add-on before it in the walk. A one-tile add-on (the jumps) never reaches a second tile and keeps its ends as they are. <paramref name="hasAddOn"/> says whether an
	/// add-on object stands on a cell; a tile without one only advances the run.
	/// </summary>
	// [BIN:TPW-EXE:0x0052ADE3 add-on ends] for every tile with flag 0x10 the walk raises the run counter [esp+0x20] (the reset of the bits [esp+0x2C] at 0x0052AE1E runs only when the raised counter is 0, and the counter's writes are 0 at the start, 0 for a corner, a crossing or an unflagged straight, 0x0052A996/0x0052ABE4/0x0052AC3C/0x0052AD1E, and the raise), finds the cell's owner thing (0x00527D60; none skips the rest, 0x0052AE93); the cell two ahead along +0xD (0x0050C3E0) and the cell two behind (0x004D8C00 the opposite) each set their bit (0x10 ahead, 1 behind, 0x0052AF07/0x0052AF4D) when it is a track tile of type 0xB or 0x10 (0x0053AD70) with a piece instance (+4) or a station of type 0xD or 0x12; at a counter of 2 the jump table 0x0052B378 (indices 0x0052B38C) shows (clears node flag 0x10) or hides (sets 0x80000010) the owner's class-1 dummies 1 and 0x10 (0x0044B3C0): bits 0 show both, 1 hide 1 and show 0x10, 0x10 show 1 and hide 0x10, 0x11 hide both
	public static IReadOnlyList<OpenEnds> OpenEndsOf( TrackCellLayer tracks, IReadOnlyList<TrackStep> steps, Func<int, int, bool> hasAddOn )
	{
		ArgumentNullException.ThrowIfNull( tracks );
		ArgumentNullException.ThrowIfNull( steps );
		ArgumentNullException.ThrowIfNull( hasAddOn );
		var ends = new List<OpenEnds>();
		var bits = 0;
		var counter = 0;
		foreach ( var step in steps )
		{
			if ( !step.AddOn )
			{
				counter = 0;
				continue;
			}
			counter++;
			if ( !hasAddOn( step.X, step.Y ) )
				continue;
			var link = tracks.Get( step.X, step.Y ).Link;
			if ( ContinuesTrack( tracks, step.X, step.Y, link ) )
				bits |= EndAheadBit;
			if ( ContinuesTrack( tracks, step.X, step.Y, Opposite( link ) ) )
				bits |= EndBehindBit;
			if ( counter == 2 )
				ends.Add( new OpenEnds( step.X, step.Y, (bits & EndBehindBit) != 0, (bits & EndAheadBit) != 0 ) );
		}
		return ends;
	}

	private const int EndBehindBit = 0x01, EndAheadBit = 0x10;

	/// <summary>The original's opposite heading (0x004D8C00): a low nibble shifted up, a high nibble down.</summary>
	private static byte Opposite( byte heading ) => heading == 0 ? (byte)0 : heading < 0x10 ? (byte)(heading << 4) : (byte)(heading >> 4);

	/// <summary>Whether the cell two cells from (<paramref name="x"/>, <paramref name="y"/>) along <paramref name="heading"/> is a track tile with a piece of its own or a station.</summary>
	private static bool ContinuesTrack( TrackCellLayer tracks, int x, int y, byte heading )
	{
		// the neighbour lookup 0x0050C3E0 answers nothing (0) for a heading that is not a single direction bit
		if ( heading is not (TrackHeading.NegY or TrackHeading.PosX or TrackHeading.PosY or TrackHeading.NegX) )
			return false;
		var (nx, ny) = (x + TrackStation.TileCells * TrackHeading.Dx( heading ), y + TrackStation.TileCells * TrackHeading.Dy( heading ));
		if ( nx < 0 || ny < 0 || nx >= tracks.Width || ny >= tracks.Height )
			return false;
		var cell = tracks.Get( nx, ny );
		if ( cell.Type is TrackCellTypes.KartOrigin or TrackCellTypes.WaterOrigin )
			return cell.Piece != 0 && (cell.Flags & TrackCell.FlagAddOn) == 0;
		return cell.Type is TrackCellTypes.KartStart or TrackCellTypes.WaterStart;
	}
}

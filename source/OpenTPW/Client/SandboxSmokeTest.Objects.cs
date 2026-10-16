namespace OpenTPW;

/// <summary>Original-object checks of the native smoke test: several different objects run and animate.</summary>
internal sealed partial class SandboxSmokeTest
{
	/// <summary>
	/// Sun God, Crazy Ape, Tom Tom Twister and Eruption: Jungle rides whose scripts move model nodes without
	/// visitors (many idle loops are vertex animations, which are not decoded), one per rotation.
	/// </summary>
	private static readonly (int InfoId, int X, int Y, int Rotation)[] SmokeObjects = { (1106, 5, 5, 0), (1101, 5, 26, 90), (1113, 26, 5, 180), (1112, 26, 26, 270) };
	private readonly Dictionary<OriginalObject, System.Numerics.Matrix4x4[]> objectPoses = new();
	private const int BellyBounceInfoId = 1100;

	private void PlaceObjects()
	{
		if ( level.OriginalPark != null )
		{
			// The Easymode records replace the old footprint markers with original models and scripts.
			var save = level.OriginalPark.Save;
			if ( save != null )
				Require( level.Objects.Objects.Count == save.PlacedObjects.Count + save.FixedItems.Count, "import every Easymode object with its original model" );
			Require( level.Objects.Objects.All( item => item.PartCount > 0 ), "every imported object has drawable parts" );
			if ( save != null && level.Guests != null )
			{
				// Give every live imported attraction a visitor at its real path entrance. The normal queue,
				// original RSE and payment bridge perform boarding; this avoids depending on which attraction
				// the heuristic guest score happens to choose after the catalogue purchase mix changes.
				foreach ( var item in level.Objects.Objects.Where( item => item.Visitors.HasCells && item.Runtime.IsAttraction ) )
				{
					var entrance = item.Visitors.EntranceCell;
					var guest = level.Guests.SpawnInPark( entrance.X, entrance.Y );
					guest.AttractionId = item.Visitors.AttractionId;
					guest.State = GuestState.GoingToRide;
				}
			}
			// Build the same rides at the nearest buildable sites around the Totem.
			Require( level.TryGetGridCell( rideSite.X, rideSite.Y, out var siteX, out var siteY ), "Totem site lies on the grid" );
			// Rides the park economy lets us buy (researched, affordable): the preferred jungle rides first, then
			// objects with the most original clips (excluding multi-part track rides). Each purchase is charged through ParkEconomy.TryBuild.
			var economy = level.Park?.Economy;
			var preferred = SmokeObjects.Select( item => level.Objects.Catalog.Find( item.InfoId ) ).OfType<ObjectCatalogEntry>();
			var candidates = preferred.Concat( level.Objects.Catalog.Buildable.Where( entry => entry.Animations.Count > 0 && entry.AuxiliaryModels.Count <= 1 )
					.OrderByDescending( entry => entry.Animations.Count ).ThenBy( entry => entry.InfoId ) )
				.Distinct().Where( entry => economy == null || economy.Research.IsAvailable( entry.InfoId ) ).Take( 6 ).ToArray();
			var balanceBefore = economy?.Balance ?? 0;
			var built = 0;
			foreach ( var (entry, index) in candidates.Select( ( entry, index ) => (entry, index) ) )
			{
				var rotation = index % 4 * 90;
				var site = Enumerable.Range( 0, 41 * 41 ).Select( cellIndex => (X: siteX - 20 + cellIndex % 41, Y: siteY - 20 + cellIndex / 41) )
					.OrderBy( cell => Math.Abs( cell.X - siteX ) + Math.Abs( cell.Y - siteY ) )
					.FirstOrDefault( cell => cell.X >= 0 && cell.Y >= 0 && level.CheckFreeObject( entry, cell.X, cell.Y, rotation ) == OriginalPlacementResult.Allowed, (X: -1, Y: -1) );
				if ( site.X < 0 )
					continue;
				var cash = economy?.Balance ?? 0;
				var placed = level.PlaceObject( entry, site.X, site.Y, rotation );
				if ( placed == null )
					continue;
				// a built ride with a queue starts closed (RIDES-015, 0x4DB712): the smoke opens it as the player would, so its script runs and it animates
				placed.Open();
				built++;
				if ( economy == null )
					continue;
				Require( cash - economy.Balance == entry.BuildCost, "charge exactly the catalog build cost" );
				Require( level.GetEconomyInstance( placed ) is int instance && economy.TryGetObject( instance, out _ ), "link built object to its economy instance" );
				Require( level.Park!.Guests!.TryGetInstance( placed.Visitors.AttractionId, out var paymentInstance ) && paymentInstance == level.GetEconomyInstance( placed ), "link guest payments to the bought object" );
				var afterBuild = economy.Balance;
				Require( level.PlaceObject( entry, site.X, site.Y, rotation ) == null && economy.Balance == afterBuild, "reject overlap without charging" );
				if ( built != 1 )
					continue;
				var id = level.GetEconomyInstance( placed )!.Value;
				Require( economy.TryGetObject( id, out var bought ), "find bought object before removal" );
				var refund = economy.ScrapValue( bought! );
				var removalCell = placed.Cells.First();
				Require( level.RemoveObjectAt( removalCell.X, removalCell.Y ), "remove bought object" );
				Require( economy.Balance == afterBuild + refund && !economy.TryGetObject( id, out _ ), "credit the economy scrap value exactly once" );
				Require( !level.Park.Guests.TryGetInstance( placed.Visitors.AttractionId, out _ ), "unlink payments after removal" );
				Require( level.PlaceObject( entry, site.X, site.Y, rotation ) != null, "rebuild on freed footprint through the economy" );
			}
			Require( built >= 2, "build original objects through the park economy in the original level" );
			PlaceModObjects( economy, siteX, siteY );
			if ( economy != null )
			{
				Log.Trace( $"Built {built} original objects for ${balanceBefore - economy.Balance} through the park economy." );
				Require( economy.Balance < balanceBefore, "building charges the original Upgrades[0].CostOfUpgrade" );
			}
			return;
		}
		foreach ( var (infoId, x, y, rotation) in SmokeObjects )
		{
			var builtObject = level.PlaceObject( level.Objects.Catalog.Get( infoId ), x, y, rotation );
			Require( builtObject != null, $"build original object {infoId}" );
			// a built ride with a queue starts closed (RIDES-015, 0x4DB712): the smoke opens it as the player would, so its script runs and it animates
			builtObject!.Open();
		}
		// With --bonus-data / OPENTPW_BONUS_DATA, also build one official bonus ride (textures from the bonus root).
		var bonus = level.Objects.Catalog.Buildable.FirstOrDefault( entry => entry.IsBonus && entry.Category == ObjectCategory.Ride );
		if ( bonus != null )
		{
			var builtBonus = level.PlaceObject( bonus, 16, 5, 0 );
			Require( builtBonus is { PartCount: > 0 }, $"build bonus object {bonus.ArchiveName}" );
			builtBonus!.Open();
		}
		// the sandbox has no park economy: a mod's objects are built free, at sites searched from the middle of the grid
		PlaceModObjects( null, 16, 16 );
		var first = level.Objects.Objects[0];
		var cell = first.Cells.First();
		Require( level.PlaceObject( first.Entry, cell.X, cell.Y, 0 ) == null, "reject overlapping footprints" );
	}

	/// <summary>
	/// With a mod that adds objects (docs/MODS.md, <c>scripts/smoke-tpi-rides.sh</c>) the smoke also builds up to four of them through the park economy
	/// and the normal placement path, opens them, and requires them to be drawn, linked to the economy and the guests and, with the checks that follow
	/// for every placed object, to run their scripts without faults. Without such a mod nothing is placed and nothing changes.
	/// </summary>
	// [EXT:mods] OpenTPW's own mod system; the original game had no mods
	private void PlaceModObjects( ParkEconomy? economy, int siteX, int siteY )
	{
		var candidates = level.Objects.Catalog.Buildable.Where( entry => entry.ModId != null && entry.ScriptPath != null
			&& (economy == null || economy.Research.IsAvailable( entry.InfoId ) && economy.Catalog.TryGet( entry.InfoId, out _ )) )
			.OrderBy( entry => entry.Category ).ThenBy( entry => entry.InfoId ).Take( 4 ).ToArray();
		var any = level.Objects.Catalog.Entries.Any( entry => entry.ModId != null );
		if ( !any )
			return;
		Require( candidates.Length > 0, "a mod's objects are buildable in the original level (enable the mod, its extension and its input)" );
		var built = 0;
		foreach ( var (entry, index) in candidates.Select( ( entry, index ) => (entry, index) ) )
		{
			var rotation = index % 4 * 90;
			var site = Enumerable.Range( 0, 61 * 61 ).Select( cellIndex => (X: siteX - 30 + cellIndex % 61, Y: siteY - 30 + cellIndex / 61) )
				.OrderBy( cell => Math.Abs( cell.X - siteX ) + Math.Abs( cell.Y - siteY ) )
				.FirstOrDefault( cell => cell.X >= 0 && cell.Y >= 0 && level.CheckFreeObject( entry, cell.X, cell.Y, rotation ) == OriginalPlacementResult.Allowed, (X: -1, Y: -1) );
			Require( site.X >= 0, $"find a site for the mod's {entry.DisplayName}" );
			var cash = economy?.Balance ?? 0;
			var placed = level.PlaceObject( entry, site.X, site.Y, rotation );
			Require( placed is { PartCount: > 0 }, $"build the mod's {entry.DisplayName} ({entry.ModId}, Info.Id {entry.InfoId}) with drawable parts" );
			placed!.Open();
			built++;
			if ( economy != null )
			{
				Require( cash - economy.Balance == entry.BuildCost, $"charge the mod's {entry.DisplayName} exactly its catalog build cost" );
				Require( level.GetEconomyInstance( placed ) is int instance && economy.TryGetObject( instance, out _ ), $"link the mod's {entry.DisplayName} to its economy instance" );
			}
		}
		Log.Trace( $"Mod objects built {( economy == null ? "free (no park economy)" : "through the park economy" )}: {string.Join( ", ", candidates.Select( entry => $"{entry.DisplayName} ({entry.ModId} {entry.InfoId})" ) )}." );
	}

	private int rideHeadFrames;
	private int maximumDrawnRideHeads;

	/// <summary>Counts frames in which the head renderer submitted a rider head quad on a ride that runs ADDHEAD.</summary>
	private void TrackRideHeads()
	{
		if ( level.RideHeadRenderer == null )
			return;
		var drawn = level.RideHeadRenderer.DrawnHeads;
		if ( drawn > 0 )
			rideHeadFrames++;
		maximumDrawnRideHeads = Math.Max( maximumDrawnRideHeads, drawn );
	}

	/// <summary>An original level must submit a rider head quad on some ride that places heads (the Jungle rides of the import, which guests board).</summary>
	private void VerifyRideHeads()
	{
		Require( rideHeadFrames > 0, "submit a rider head quad on a ride that uses ADDHEAD" );
		Require( rideHeadPixelsVerified, "complete the isolated rider-head GPU pixel comparison" );
		Log.Trace( $"Ride heads: quads submitted in {rideHeadFrames} frames, at most {maximumDrawnRideHeads} at once on rides that run ADDHEAD." );
	}

	private void SnapshotObjects()
	{
		foreach ( var item in level.Objects.Objects )
			objectPoses[item] = item.NodeTransforms.ToArray();
	}

	// Every original object's model-node motion over the whole run: the number of frames in which any node transform
	// changed from the previous frame. A fixed 20-frame sample depended on where each ride stood in its cycle: in the
	// ANIM-M merge, Crazy Ape and Aztec Mayhem animated for thousands of frames but were between cycles in that sample.
	private readonly Dictionary<OriginalObject, (System.Numerics.Matrix4x4[] Pose, int MovedFrames)> objectMotion = new();

	/// <summary>
	/// Frames an object must have moved in to count as animating. Pose noise is one frame (Drinks Shop, Security Camera
	/// and Round Fountain settle once after import); objects that run their original cycles move in hundreds to thousands
	/// of frames by the check (main 78989f9, jungle: Small Toilet 219, Bus 406, Aztec Mayhem 2,045, Crazy Ape 3,188,
	/// Belly Bounce 4,578; sandbox: Crazy Ape 311, Eruption 706, Sun God 716, Tom Tom Twister 930).
	/// </summary>
	private const int MinimumMovedFrames = 100;

	/// <summary>Counts, every frame, the frames in which each placed original object's model nodes changed.</summary>
	private void TrackObjectMotion()
	{
		foreach ( var item in level.Objects.Objects )
		{
			if ( item.IsDeleted || item is PrototypeRide )
				continue;
			var pose = item.NodeTransforms.ToArray();
			var moved = objectMotion.TryGetValue( item, out var seen ) ? seen.MovedFrames : 0;
			if ( seen.Pose != null && seen.Pose.Length == pose.Length && pose.Where( ( transform, index ) => !transform.Equals( seen.Pose[index] ) ).Any() )
				moved++;
			objectMotion[item] = (pose, moved);
		}
	}

	/// <summary>
	/// Requires the Totem to animate (VerifyPoseChanged, just before), in an original level the imported Belly Bounce
	/// to move between the two sample frames, and at least three other different original objects to each have moved in
	/// at least <see cref="MinimumMovedFrames"/> frames of the run so far.
	/// </summary>
	private void VerifyObjectsAnimate()
	{
		var sampled = objectPoses.Where( pair => !pair.Key.IsDeleted && pair.Key.Script?.State != RideVMState.Faulted
			&& pair.Key.NodeTransforms.Where( ( transform, index ) => !transform.Equals( pair.Value[index] ) ).Any() )
			.Select( pair => pair.Key.Entry.InfoId ).Distinct().ToArray();
		var counts = objectMotion.Where( pair => !pair.Key.IsDeleted && pair.Key.Script?.State != RideVMState.Faulted )
			.GroupBy( pair => pair.Key.Entry.InfoId )
			.Select( group => (InfoId: group.Key, MovedFrames: group.Max( pair => pair.Value.MovedFrames )) )
			.OrderByDescending( item => item.MovedFrames ).ToArray();
		var animating = counts.Where( item => item.MovedFrames >= MinimumMovedFrames ).Select( item => item.InfoId ).ToArray();
		string Names( IEnumerable<int> ids ) => string.Join( ", ", ids.Select( id => level.Objects.Catalog.Find( id )?.DisplayName ?? id.ToString() ) );
		Log.Trace( $"Original object moved frames by frame {frame}: {string.Join( ", ", counts.Select( item => $"{level.Objects.Catalog.Find( item.InfoId )?.DisplayName ?? item.InfoId.ToString()} {item.MovedFrames}" ) )}." );
		Require( level.Objects.Objects.All( item => item.Script?.State != RideVMState.Faulted ), "original object scripts run without faults" );
		Require( animating.Length >= 3, $"at least three different original objects besides the Totem each move in at least {MinimumMovedFrames} frames (animating: {Names( animating )})" );
		if ( level.OriginalPark != null )
			Require( sampled.Contains( BellyBounceInfoId ), $"the imported Belly Bounce animates between the sample frames (moving: {Names( sampled )})" );
		if ( level.Guests != null )
		{
			// Guests use the imported Easymode attractions through their own scripts, entering at the shape's entrance cell.
			var used = level.Objects.Objects.Where( item => item.Visitors.BoardedTotal > 0 ).ToArray();
			Log.Trace( $"Guests used original objects: {string.Join( ", ", used.Select( item => $"{item.Entry.DisplayName} ({item.Visitors.BoardedTotal} boarded, {item.Visitors.ReleasedTotal} released)" ) )}; {level.Guests.Attractions.Count} attractions registered." );
			if ( level.OriginalPark?.Save != null )
			{
				var imported = level.OriginalPark.Save.PlacedObjects.Select( item => item.Record.InfoId ).ToHashSet();
				Require( used.Select( item => item.Entry.InfoId ).Where( imported.Contains ).Distinct().Count() >= 2, "guests ride at least two different imported original objects" );
			}
		}
		Log.Trace( $"Original objects animating: {Names( animating )} and the Totem; between the sample frames: {Names( sampled )}." );
	}
}

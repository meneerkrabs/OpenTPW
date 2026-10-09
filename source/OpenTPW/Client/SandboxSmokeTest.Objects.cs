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
					.FirstOrDefault( cell => cell.X >= 0 && cell.Y >= 0 && level.CheckObject( entry, cell.X, cell.Y, rotation ) == OriginalPlacementResult.Allowed, (X: -1, Y: -1) );
				if ( site.X < 0 )
					continue;
				var cash = economy?.Balance ?? 0;
				var placed = level.PlaceObject( entry, site.X, site.Y, rotation );
				if ( placed == null )
					continue;
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
			if ( economy != null )
			{
				Log.Trace( $"Built {built} original objects for ${balanceBefore - economy.Balance} through the park economy." );
				Require( economy.Balance < balanceBefore, "building charges the original Upgrades[0].CostOfUpgrade" );
			}
			return;
		}
		foreach ( var (infoId, x, y, rotation) in SmokeObjects )
			Require( level.PlaceObject( level.Objects.Catalog.Get( infoId ), x, y, rotation ) != null, $"build original object {infoId}" );
		// With --bonus-data / OPENTPW_BONUS_DATA, also build one official bonus ride (textures from the bonus root).
		var bonus = level.Objects.Catalog.Buildable.FirstOrDefault( entry => entry.IsBonus && entry.Category == ObjectCategory.Ride );
		if ( bonus != null )
			Require( level.PlaceObject( bonus, 16, 5, 0 ) is { PartCount: > 0 }, $"build bonus object {bonus.ArchiveName}" );
		var first = level.Objects.Objects[0];
		var cell = first.Cells.First();
		Require( level.PlaceObject( first.Entry, cell.X, cell.Y, 0 ) == null, "reject overlapping footprints" );
	}

	private void SnapshotObjects()
	{
		foreach ( var item in level.Objects.Objects )
			objectPoses[item] = item.NodeTransforms.ToArray();
	}

	/// <summary>Requires at least three different objects (the Totem included) to have moved model nodes since the snapshot.</summary>
	private void VerifyObjectsAnimate()
	{
		var moving = objectPoses.Where( pair => !pair.Key.IsDeleted && pair.Key.Script?.State != RideVMState.Faulted
			&& pair.Key.NodeTransforms.Where( ( transform, index ) => !transform.Equals( pair.Value[index] ) ).Any() )
			.Select( pair => pair.Key.Entry.InfoId )
			// The Totem's pose change was verified just before (VerifyPoseChanged).
			.Concat( level.PlacedRide != null ? new[] { PrototypeRide.InfoId } : Array.Empty<int>() ).Distinct().ToArray();
		Require( level.Objects.Objects.All( item => item.Script?.State != RideVMState.Faulted ), "original object scripts run without faults" );
		Require( moving.Length >= 3, $"at least three different original objects animate (moving: {string.Join( ", ", moving )})" );
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
		Log.Trace( $"Original objects animating between frames: {string.Join( ", ", moving.Select( id => level.Objects.Catalog.Find( id )?.DisplayName ?? PrototypeRide.DisplayName ) )}." );
	}
}

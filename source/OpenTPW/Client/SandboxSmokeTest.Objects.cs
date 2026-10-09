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
			// Build the same rides at the nearest buildable sites around the Totem.
			Require( level.TryGetGridCell( rideSite.X, rideSite.Y, out var siteX, out var siteY ), "Totem site lies on the grid" );
			// Other themes: their non-track rides with the most original clips.
			var choices = level.Objects.Catalog.Theme == "jungle"
				? SmokeObjects.Select( item => (Entry: level.Objects.Catalog.Get( item.InfoId ), item.Rotation) ).ToArray()
				: level.Objects.Catalog.Buildable.Where( entry => entry.Category == ObjectCategory.Ride && entry.AuxiliaryModels.Count <= 1 )
					.OrderByDescending( entry => entry.Animations.Count ).ThenBy( entry => entry.InfoId ).Take( 6 ).Select( ( entry, index ) => (Entry: entry, Rotation: index % 4 * 90) ).ToArray();
			foreach ( var (entry, rotation) in choices )
			{
				var infoId = entry.InfoId;
				var site = Enumerable.Range( 0, 41 * 41 ).Select( index => (X: siteX - 20 + index % 41, Y: siteY - 20 + index / 41) )
					.OrderBy( cell => Math.Abs( cell.X - siteX ) + Math.Abs( cell.Y - siteY ) )
					.FirstOrDefault( cell => cell.X >= 0 && cell.Y >= 0 && level.PlaceObject( entry, cell.X, cell.Y, rotation ) != null, (X: -1, Y: -1) );
				Require( site.X >= 0, $"build original object {infoId} in the original level" );
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

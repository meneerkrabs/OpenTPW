using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>A package checked against the local installation, ready for a read-only visit.</summary>
public sealed record ParkVisitInfo(
	ParkPackage Package,
	MinimalParkPayload Payload,
	IReadOnlyList<RequiredContent> MissingContent,
	IReadOnlyList<string> Warnings,
	bool MatchesOriginalImport )
{
	public string Level => Payload.Level;
	public bool IsSandbox => Payload.Source == MinimalParkPayload.SourceSandbox;
}

/// <summary>File-based sharing: export the current park to <c>.tpwpark</c> and prepare visits.</summary>
public static class ParkSharing
{
	public static ParkPackage CreatePackage( ParkSnapshot snapshot, string parkName, string description, string author, MapFile? map,
		Func<float, float, (int X, int Y)?>? rideCell = null )
	{
		var payload = MinimalParkPayload.FromSnapshot( snapshot );
		var thumbnail = ParkSnapshotBuilder.RenderThumbnail( map, payload, rideCell );
		var language = GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage;
		return ParkPackage.Create( snapshot, new ParkInfo( parkName, description, author, snapshot.Level ), new GameInfo( ParkSnapshotBuilder.Edition, language ), thumbnail );
	}

	/// <summary>
	/// Why a park cannot be exported while simulation-changing extensions or mods are active, or null when it can: a park package does
	/// not record mods yet, so such a park would play differently for whoever visits it (docs/MODS.md).
	/// </summary>
	// [EXT:mods] OpenTPW's own mod system; the original game had no mods
	public static string? ModsRefusal( Level? level = null )
	{
		// [EXT:pools-save] opentpw.pools counts only while the park has pool cells, as a save records it (ParkResumeMods.Capture)
		var inUse = ParkResumeMods.InUse( level?.Guests?.Grid.Cells, level?.Objects?.Objects.Any( item => !item.IsDeleted && ParkResumeMods.IsPoolsObject( item.Entry ) ) == true );
		var mods = OpenTPW.Mods.OpenTpwExtensions.Active.SimulationMods.Where( mod => !ParkResumeMods.UsedOnlyExtensionIds.Contains( mod.Id ) || inUse.Contains( mod.Id ) ).ToArray();
		return mods.Length == 0 ? null
			: $"This park records mods ({string.Join( ", ", mods.Select( mod => $"{mod.Id} {mod.Version}" ) )}) and cannot be exported yet; turn them off to share a park without mods.";
	}

	/// <summary>Exports the live level (used by the in-game panel).</summary>
	public static ParkPackage ExportLevel( Level level, string parkName, string author, string description = "" )
	{
		if ( level.IsReadOnlyVisit )
			throw new InvalidOperationException( "A visited park cannot be exported as the visitor's own park." );
		if ( ModsRefusal( level ) is { } refusal )
			throw new InvalidOperationException( refusal );
		var snapshot = ParkSnapshotBuilder.FromLevel( level );
		MapFile? map = level.OriginalPark?.Map;
		Func<float, float, (int X, int Y)?>? rideCell = level.OriginalPark == null ? null : ( x, y ) =>
			OriginalParkPlacement.TryGetCell( level.OriginalPark.Heightfield, x, y, out var cx, out var cy ) ? (cx, cy) : null;
		return CreatePackage( snapshot, parkName, description, author, map, rideCell );
	}

	/// <summary>
	/// Checks a package against the local data: payload format, level presence and MAP hash, object Info.Ids
	/// in the level catalog, bonus archives. Missing content does not block the visit (the original asked
	/// "MISSING RIDES … Continue visiting this park ?", UITEXT 410); an unknown level or payload does.
	/// </summary>
	public static ParkVisitInfo PrepareVisit( ParkPackage package )
	{
		var payload = MinimalParkPayload.FromSnapshot( package.ToSnapshot() );
		if ( !FileSystem.DirectoryExists( $"/levels/{payload.Level}" ) )
			throw new InvalidDataException( $"This park needs the '{payload.Level}' level, which this installation does not have." );
		var warnings = new List<string>();
		var missing = new List<RequiredContent>();
		var catalog = OriginalPark.LoadCatalog( payload.Level );
		foreach ( var content in package.Manifest.RequiredContent )
		{
			switch ( content.Kind )
			{
				case ContentKinds.Level:
					if ( content.Id != payload.Level )
						warnings.Add( $"Package lists level '{content.Id}' but its payload is '{payload.Level}'." );
					else if ( content.Sha256 != null && ParkSnapshotBuilder.LevelHash( payload.Level ) is { } local && local != content.Sha256 )
						warnings.Add( "The park was made with a different version of this level's terrain map; paths and objects may be misplaced." );
					break;
				case ContentKinds.Object:
					if ( !int.TryParse( content.Id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id ) || !catalog.ContainsKey( id ) )
						missing.Add( content );
					break;
				case ContentKinds.BonusArchive:
					if ( !FileSystem.FileExists( "/" + content.Id ) && !FileSystem.DirectoryExists( "/" + content.Id ) )
						missing.Add( content );
					break;
			}
		}
		if ( payload.Source == MinimalParkPayload.SourceSandbox && (payload.PathCells.Count > 0 || payload.Objects.Count > 0) )
			warnings.Add( "Shared sandbox path/object layouts cannot be drawn by this build; only the prototype ride is shown." );
		var matches = false;
		if ( payload.Source == MinimalParkPayload.SourceOriginalLevel && !payload.HasBuiltLayout )
		{
			var local = ParkSnapshotBuilder.FromOriginal( OriginalPark.Load( payload.Level ), payload.PrototypeRide );
			var localPayload = MinimalParkPayload.FromSnapshot( local );
			matches = localPayload.PathCells.SequenceEqual( payload.PathCells ) && localPayload.Objects.SequenceEqual( payload.Objects );
			if ( !matches )
				warnings.Add( "This version 1 park's paths/objects differ from the original level import. This build draws the original import and lists the shared layout in the Online panel; ask the owner to republish for the built layout." );
		}
		return new ParkVisitInfo( package, payload, missing, warnings, matches );
	}

	public static string Describe( ParkVisitInfo visit )
	{
		var manifest = visit.Package.Manifest;
		var payload = visit.Payload;
		var lines = new List<string>
		{
			$"Park '{manifest.Park.Name}' by {(manifest.Park.Author.Length > 0 ? manifest.Park.Author : "unknown")} ({manifest.Park.Level}, {manifest.Game.Language}), created {manifest.CreatedUtc:u}.",
			$"Payload {manifest.Payload.Format} v{manifest.Payload.Version}: {payload.PathCells.Count} path cells, {payload.Objects.Count} objects"
				+ (payload.PrototypeRide != null ? ", prototype ride" : "") + "; flags: " + string.Join( ", ", manifest.Flags ) + ".",
		};
		if ( payload.HasBuiltLayout )
		{
			var queues = payload.Cells!.Count( cell => cell.Type == (byte)ParkCellType.Queue );
			lines.Add( $"Built layout: {payload.Cells.Count - queues} path cells and {queues} queue cells, {payload.Coasters?.Count ?? 0} coaster(s), digest {payload.LayoutDigest}." );
			if ( payload.Stats is { } stats )
				lines.Add( $"Published park: rating {stats.Rating}, value ${stats.Value}, balance ${stats.Balance}, {stats.Visitors} visitors, {stats.Date}; shown read-only." );
		}
		if ( visit.MissingContent.Count > 0 )
			lines.Add( $"Missing content ({visit.MissingContent.Count}): " + string.Join( ", ", visit.MissingContent.Select( item => $"{item.Kind} {item.Id}" ) ) );
		lines.AddRange( visit.Warnings );
		return string.Join( "\n", lines );
	}
}

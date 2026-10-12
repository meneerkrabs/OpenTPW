namespace OpenTPW;

public partial class Level
{
	/// <summary>Captures a writable fixed-ride park between simulation ticks, including its direct instance links.</summary>
	internal ParkResumeSnapshot CaptureResumeSnapshot( AutomaticAdvisor? advisor, string displayName,
		IReadOnlyDictionary<string, string>? verifiedAssets = null )
	{
		if ( IsReadOnlyVisit || GameEditions.IsThemeParkInc || OriginalPark == null || Park == null || Guests == null )
			throw new InvalidOperationException( "Only a writable Theme Park World park can be saved here." );
		if ( PlacedRide != null || Coasters.Count != 0 )
			throw new NotSupportedException( "Saving prototype rides and roller coasters is not supported yet." );
		var runtimes = Objects.Objects.Select( item =>
		{
			var state = item.Runtime.CaptureSnapshot();
			RequireResumableAudio( state );
			return new ParkResumeRuntime( state.HasScript && !state.Stopped ? item.Runtime.Script!.ScriptId : 0, state );
		} ).ToArray();
		var assets = verifiedAssets ?? ParkResumeAssetManifest.Capture( Objects.Catalog, Park.Economy.Settings );
		var random = CaptureRandomState();
		var snapshot = new ParkResumeSnapshot
		{
			Format = ParkResumeSaveFile.FormatName,
			Version = ParkResumeSaveFile.CurrentVersion,
			Theme = LevelName,
			StartKind = Park.Start.Kind,
			Seed = Seed.Value,
			DisplayName = displayName,
			SavedUtc = DateTimeOffset.UtcNow,
			Economy = ParkSaveFile.Capture( Park.Economy, random ),
			Layout = ParkLayoutSnapshot.CaptureSnapshot( Guests.Grid.Cells, Objects ),
			Guests = Guests.CaptureSnapshot(),
			Scripts = Objects.ScriptWorld.CaptureSnapshot(),
			Runtimes = runtimes,
			EconomyBindings = Objects.Objects.Where( economyInstances.ContainsKey )
				.Select( item => new ParkResumeEconomyBinding( item.Visitors.AttractionId, economyInstances[item] ) ).ToArray(),
			Clock = simulationClock.CaptureSnapshot(),
			SoundSeed = random.SoundSeed,
			Weather = Weather?.CaptureSnapshot(),
			Gate = Gate?.CaptureSnapshot(),
			Staff = StaffWorld?.CaptureSnapshot(),
			Advisor = advisor?.CaptureSnapshot(),
			AssetHashes = assets
		};
		ParkResumeSaveFile.ValidateSnapshot( snapshot, Objects.Catalog, Park.Economy.Settings, Park.Economy.Catalog, assets );
		return snapshot;
	}

	internal static void RequireResumableAudio( OriginalObjectRuntimeSnapshot runtime )
	{
		if ( runtime.HeldScream || runtime.OwnedScreamCount != 0 )
			throw new NotSupportedException( "An attraction is still playing a scream. Save again after it finishes." );
	}

	/// <summary>Called only during construction of a staged candidate, before it can simulate or replace the live park.</summary>
	private void RestoreResumeSimulation( ParkResumeSnapshot snapshot )
	{
		if ( Guests == null || Park == null || snapshot.Runtimes.Count != Objects.Objects.Count )
			throw new InvalidDataException( "The saved park cannot be rebuilt with the current objects." );
		var roots = new Dictionary<int, RideVM>();
		for ( var index = 0; index < Objects.Objects.Count; index++ )
		{
			var saved = snapshot.Runtimes[index];
			RequireResumableAudio( saved.State );
			var runtime = Objects.Objects[index].Runtime;
			var gaps = runtime.RestoreSnapshot( saved.State );
			if ( gaps.Count != 0 ) throw new InvalidDataException( string.Join( "; ", gaps ) );
			if ( saved.RootScriptId != 0 ) roots.Add( saved.RootScriptId, runtime.Script! );
		}
		Objects.ScriptWorld.RestoreSnapshot( snapshot.Scripts, roots );
		ValidateResumeQueueGeometry( snapshot.Guests, Guests );
		Guests.RestoreSnapshot( snapshot.Guests );
		simulationClock.RestoreSnapshot( snapshot.Clock );
		if ( snapshot.Weather != null )
		{
			Weather = new ParkWeather( Park.Economy.Settings.Standard, Seed.WeatherStream, Guests.Grid.CountX, Guests.Grid.CountY );
			Weather.RestoreSnapshot( snapshot.Weather );
		}
		if ( snapshot.Gate != null )
			(Gate ?? throw new InvalidDataException( "The saved park gate is unavailable." )).RestoreSnapshot( snapshot.Gate );
		if ( snapshot.Staff != null )
			(StaffWorld ?? throw new InvalidDataException( "The saved staff world is unavailable." )).RestoreSnapshot( snapshot.Staff );
		renderedCellVersion = Guests.Grid.Cells.Version;
	}

	/// <summary>Current cached queues must agree with the restored cells; a cache from before the last edit refreshes on the next tick.</summary>
	internal static void ValidateResumeQueueGeometry( GuestSimulationSnapshot snapshot, GuestSimulation target )
	{
		var validation = target.ValidateSnapshot( snapshot );
		if ( !validation.IsValid ) throw new InvalidDataException( string.Join( "; ", validation.Errors ) );
		if ( snapshot.SeenGridVersion > target.Grid.Version )
			throw new InvalidDataException( "The guests refer to a cell-map version newer than the saved layout." );
		if ( snapshot.SeenGridVersion != target.Grid.Version ) return;
		var bridges = target.Attractions.OfType<RideVisitorBridge>().ToDictionary( item => item.AttractionId );
		foreach ( var saved in snapshot.Attractions )
		{
			var bridge = bridges[saved.AttractionId];
			if ( !saved.QueueCells.SequenceEqual( bridge.QueueCells ) || saved.JoinCell != bridge.JoinCell )
				throw new InvalidDataException( $"Attraction {saved.AttractionId}'s queue does not match the saved cell map." );
		}
	}
}

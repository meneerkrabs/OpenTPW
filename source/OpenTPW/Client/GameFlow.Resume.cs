using OpenTPW.Hud;

namespace OpenTPW;

internal sealed partial class GameFlow
{
	internal void SaveCurrentPark()
	{
		var level = Level ?? throw new InvalidOperationException( "There is no park to save." );
		if ( level.OriginalPark == null )
		{
			level.SaveSandbox();
			return;
		}
		var snapshot = level.CaptureResumeSnapshot( Advisor?.IsAttached == true ? Advisor : null, level.LevelName,
			savedUtc: DateTimeOffset.UtcNow );
		ParkResumeSaveFile.Save( SaveFileSystem.GetAbsolutePath( $"opentpw-{level.LevelName}-world.json" ), snapshot );
		Log.Trace( $"Saved {level.LevelName} park: {snapshot.Layout.Objects.Count} objects, {snapshot.Guests.Guests.Length} guests." );
	}

	/// <summary>Preflights and builds an isolated candidate before retiring the live park or lobby.</summary>
	internal Level ResumePark( ParkResumeSnapshot snapshot )
	{
		if ( GameEditions.IsThemeParkInc ) throw new NotSupportedException( "Theme Park Inc park saves are not supported." );
		var settings = BalanceSettings.Load( snapshot.Theme, snapshot.Economy.Easy );
		var catalog = ObjectCatalog.Load( snapshot.Theme, snapshot.Economy.Easy );
		var economyCatalog = EconomyObjectCatalog.Load( snapshot.Theme, snapshot.Economy.Easy );
		var assets = ParkResumeAssetManifest.Capture( catalog, settings );
		ParkResumeSaveFile.ValidateSnapshot( snapshot, catalog, settings, economyCatalog, assets );
		if ( snapshot.Gate == null || snapshot.Staff == null )
			throw new InvalidDataException( "The saved writable park is missing its gate or staff-world state." );
		foreach ( var runtime in snapshot.Runtimes ) global::OpenTPW.Level.RequireResumableAudio( runtime.State );
		var economy = ParkSaveFile.Restore( snapshot.Economy, settings, economyCatalog );
		AutomaticAdvisor? candidateAdvisor = null;
		try
		{
			if ( snapshot.Advisor != null )
			{
				if ( !AutomaticAdvisorEnabled ) throw new InvalidOperationException( "Enable the advisor to resume this park's saved advice." );
				candidateAdvisor = AutomaticAdvisor.TryCreate()
					?? throw new InvalidDataException( "The saved park's advisor settings are unavailable." );
				candidateAdvisor.ValidateSnapshot( snapshot.Advisor );
			}
			AbandonParkBuild();
			using var scene = new EntityConstructionScope();
			var candidate = new Level( snapshot, economy, OnlineFolders ) { ShowDeveloperPanels = false };
			var candidateHud = CreateParkHud( candidate, EconomyParkStatus.ForLevel( candidate ) );
			if ( candidateAdvisor != null )
			{
				candidateAdvisor.AttachLevel( candidate );
				candidateAdvisor.RestoreSnapshot( snapshot.Advisor! );
			}
			scene.Detach();
			var retiredAdvisor = Advisor;
			TearDown( applyPendingLanguage: false );
			scene.Activate();
			Level = candidate;
			Hud = candidateHud;
			StartKind = snapshot.StartKind;
			Advisor = candidateAdvisor;
			candidateAdvisor = null;
			advisorUnavailable = false;
			retiredAdvisor?.Dispose();
			try
			{
				GameAudio.EnterPark( snapshot.Theme, snapshot.SoundSeed );
				GameAudio.Events?.Reseed( snapshot.SoundSeed );
			}
			catch ( Exception error ) when ( error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException )
			{
				Log.Warning( $"Park resumed; audio could not start: {error.Message}" );
			}
			Log.Trace( $"Resumed {snapshot.Theme} park: {candidate.Objects.Objects.Count} objects, {candidate.Guests!.Guests.Count} guests." );
			return candidate;
		}
		finally { candidateAdvisor?.Dispose(); }
	}
}

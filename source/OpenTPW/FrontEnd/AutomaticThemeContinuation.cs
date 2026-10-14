using System.Security.Cryptography;

namespace OpenTPW.FrontEnd;

/// <summary>Transient selection of a shape-checked automatic slot, not a persisted profile identity.</summary>
internal sealed record AutomaticParkContinueEntry( BaseFileSystem Namespace, string Theme, string Path,
	ParkStartKind StartKind, ParkGameMode Mode, bool Easy, string DisplayName, DateTimeOffset SavedUtc, string Digest );

internal static class AutomaticThemeContinuation
{
	internal static string Theme( string theme ) => ObjectCatalog.Themes.FirstOrDefault( known => known.Equals( theme, StringComparison.OrdinalIgnoreCase ) )
		?? throw new InvalidDataException( "An automatic checkpoint requires a known Theme Park World theme." );

	internal static string Slot( BaseFileSystem files, string theme ) => files.GetAbsolutePath( $"opentpw-{Theme( theme )}-automatic-world.json" );

	internal static void RequireSupported( ParkResumeSnapshot snapshot, string theme )
	{
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || !GameEditions.Capabilities.ParkSaves )
			throw new NotSupportedException( "Automatic world checkpoints are available only for Theme Park World." );
		if ( snapshot.Theme != Theme( theme ) || snapshot.StartKind is not (ParkStartKind.FullSimulation or ParkStartKind.InstantAction)
			|| snapshot.Economy.Mode != (snapshot.StartKind == ParkStartKind.InstantAction ? ParkGameMode.InstantAction : ParkGameMode.FullSimulation)
			|| snapshot.Gate == null || snapshot.Staff == null )
			throw new InvalidDataException( "The automatic checkpoint is not a resumable writable park of the selected theme and mode." );
	}

	// Hash the already decoded own envelope; no original asset is opened or hashed here.
	private static string Digest( ParkResumeSnapshot snapshot ) => Convert.ToHexString( SHA256.HashData( ParkResumeSaveFile.Serialize( snapshot ) ) );

	internal static AutomaticParkContinueEntry? Probe( BaseFileSystem files, string theme )
	{
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || !GameEditions.Capabilities.ParkSaves ) return null;
		try
		{
			var canonical = Theme( theme ); var path = Slot( files, canonical );
			if ( !File.Exists( path ) ) return null;
			var snapshot = ParkResumeSaveFile.Load( path );
			RequireSupported( snapshot, canonical );
			return new( files, canonical, path, snapshot.StartKind, snapshot.Economy.Mode, snapshot.Economy.Easy,
				snapshot.DisplayName, snapshot.SavedUtc, Digest( snapshot ) );
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException )
		{
			return null;
		}
	}

	internal static ParkResumeSnapshot Read( BaseFileSystem files, AutomaticParkContinueEntry entry )
	{
		ArgumentNullException.ThrowIfNull( entry );
		if ( !ReferenceEquals( files, entry.Namespace ) || entry.Path != Slot( files, entry.Theme ) )
			throw new InvalidDataException( "The automatic checkpoint selection belongs to another save namespace or slot." );
		var snapshot = ParkResumeSaveFile.Load( entry.Path );
		RequireSupported( snapshot, entry.Theme );
		if ( snapshot.StartKind != entry.StartKind || snapshot.Economy.Mode != entry.Mode || snapshot.Economy.Easy != entry.Easy
			|| snapshot.DisplayName != entry.DisplayName || snapshot.SavedUtc != entry.SavedUtc || Digest( snapshot ) != entry.Digest )
			throw new InvalidDataException( "The selected automatic checkpoint changed. Reopen the theme choice." );
		return snapshot;
	}
}

using System.Security.Cryptography;

namespace OpenTPW.FrontEnd;

/// <summary>Which automatic slot a continuation reads: the Exit To Lobby checkpoint or the periodic autosave ([EXT:periodic-autosave]).</summary>
internal enum AutomaticSlotKind { Checkpoint, Autosave }

/// <summary>Transient selection of a shape-checked automatic slot, not a persisted profile identity.</summary>
internal sealed record AutomaticParkContinueEntry( BaseFileSystem Namespace, string Theme, string Path,
	ParkStartKind StartKind, ParkGameMode Mode, bool Easy, string DisplayName, DateTimeOffset SavedUtc, string Digest, string? Requires = null,
	AutomaticSlotKind Kind = AutomaticSlotKind.Checkpoint, bool Cheated = false )
{
	/// <summary>[EXT:save-extension-switch] The extension switch the player confirmed for this continuation; null continues strictly.</summary>
	public ParkExtensionSwitch? Switch { get; init; }
}

internal static class AutomaticThemeContinuation
{
	internal static string Theme( string theme ) => ObjectCatalog.Themes.FirstOrDefault( known => known.Equals( theme, StringComparison.OrdinalIgnoreCase ) )
		?? throw new InvalidDataException( "An automatic checkpoint requires a known Theme Park World theme." );

	internal static string Slot( BaseFileSystem files, string theme ) => Slot( files, theme, AutomaticSlotKind.Checkpoint );

	/// <summary>The checkpoint slot, or [EXT:periodic-autosave] the periodic autosave's own slot: neither is the manual <c>opentpw-{theme}-world.json</c>.</summary>
	internal static string Slot( BaseFileSystem files, string theme, AutomaticSlotKind kind ) =>
		files.GetAbsolutePath( $"opentpw-{Theme( theme )}-{( kind == AutomaticSlotKind.Autosave ? "autosave" : "automatic" )}-world.json" );

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

	internal static AutomaticParkContinueEntry? Probe( BaseFileSystem files, string theme, AutomaticSlotKind kind = AutomaticSlotKind.Checkpoint )
	{
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || !GameEditions.Capabilities.ParkSaves ) return null;
		try
		{
			var canonical = Theme( theme ); var path = Slot( files, canonical, kind );
			if ( !File.Exists( path ) ) return null;
			var snapshot = ParkResumeSaveFile.Load( path );
			RequireSupported( snapshot, canonical );
			return new( files, canonical, path, snapshot.StartKind, snapshot.Economy.Mode, snapshot.Economy.Easy,
				snapshot.DisplayName, snapshot.SavedUtc, Digest( snapshot ), ParkExtensionSwitch.RequiresText( snapshot.Mods, Mods.OpenTpwExtensions.Active ), kind, snapshot.Economy.Cheats?.Mode == true );
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException )
		{
			return null;
		}
	}

	internal static ParkResumeSnapshot Read( BaseFileSystem files, AutomaticParkContinueEntry entry )
	{
		ArgumentNullException.ThrowIfNull( entry );
		if ( !ReferenceEquals( files, entry.Namespace ) || entry.Path != Slot( files, entry.Theme, entry.Kind ) )
			throw new InvalidDataException( "The automatic checkpoint selection belongs to another save namespace or slot." );
		var snapshot = ParkResumeSaveFile.Load( entry.Path );
		RequireSupported( snapshot, entry.Theme );
		if ( snapshot.StartKind != entry.StartKind || snapshot.Economy.Mode != entry.Mode || snapshot.Economy.Easy != entry.Easy
			|| snapshot.DisplayName != entry.DisplayName || snapshot.SavedUtc != entry.SavedUtc || Digest( snapshot ) != entry.Digest )
			throw new InvalidDataException( "The selected automatic checkpoint changed. Reopen the theme choice." );
		return snapshot;
	}

	/// <summary>
	/// [EXT:periodic-autosave] The automatic entry the theme menu offers: the newer of the Exit To Lobby checkpoint and the periodic autosave, where the autosave counts
	/// only when it is also newer than the manual save (a manual save made after the last autosave is the player's latest word; the autosave is only for recovery after a crash).
	/// </summary>
	internal static AutomaticParkContinueEntry? Newest( BaseFileSystem files, string theme )
	{
		var checkpoint = Probe( files, theme );
		var autosave = Probe( files, theme, AutomaticSlotKind.Autosave );
		if ( autosave == null ) return checkpoint;
		if ( checkpoint != null && checkpoint.SavedUtc >= autosave.SavedUtc ) return checkpoint;
		return ManualSavedUtc( files, theme ) is { } manual && manual >= autosave.SavedUtc ? checkpoint : autosave;
	}

	/// <summary>When the manual world save of a theme was written, or null without a readable one.</summary>
	internal static DateTimeOffset? ManualSavedUtc( BaseFileSystem files, string theme )
	{
		try
		{
			var path = files.GetAbsolutePath( $"opentpw-{Theme( theme )}-world.json" );
			return File.Exists( path ) ? ParkResumeSaveFile.Load( path ).SavedUtc : null;
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException ) { return null; }
	}
}

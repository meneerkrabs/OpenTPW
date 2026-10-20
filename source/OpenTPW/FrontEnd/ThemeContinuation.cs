namespace OpenTPW.FrontEnd;

/// <summary>Menu metadata from an existing application-owned save; never a new save or an original-format import.</summary>
public sealed record ParkContinueEntry( ParkLoadEntry Park, ParkStartKind StartKind, ParkGameMode Mode, bool Easy, string? Requires = null );

internal static class ThemeContinuation
{
	private static string Theme( string theme ) => ObjectCatalog.Themes.FirstOrDefault( known => known.Equals( theme, StringComparison.OrdinalIgnoreCase ) )
		?? throw new InvalidDataException( "Only a Theme Park World theme can continue an own world save." );

	private static string Slot( BaseFileSystem files, string theme ) => files.GetAbsolutePath( $"opentpw-{Theme( theme )}-world.json" );

	internal static ParkContinueEntry? Probe( BaseFileSystem files, string theme )
	{
		if ( !GameEditions.Capabilities.ParkSaves ) return null;
		try
		{
			var path = Slot( files, theme );
			if ( !File.Exists( path ) ) return null;
			var saved = ParkResumeSaveFile.Load( path );
			if ( !saved.Theme.Equals( Theme( theme ), StringComparison.OrdinalIgnoreCase ) ) return null;
			// [EXT:mods] the entry stays offered and names the mods it needs when they are not all active
			// [EXT:save-extension-switch] differences the load can switch are asked about, not listed as needs
			var requires = ParkExtensionSwitch.RequiresText( saved.Mods, Mods.OpenTpwExtensions.Active );
			return new( new ParkLoadEntry( saved.Theme, -1, false, path, saved.DisplayName, Requires: requires, Cheated: saved.Economy.Cheats?.Mode == true ), saved.StartKind, saved.Economy.Mode, saved.Economy.Easy, requires );
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException )
		{
			return null;
		}
	}

	/// <summary>Re-read the trusted slot at activation; changed theme/mode metadata refuses before the existing staged resume.</summary>
	internal static ParkResumeSnapshot Read( BaseFileSystem files, ParkContinueEntry entry )
	{
		ArgumentNullException.ThrowIfNull( entry );
		if ( entry.Park == null ) throw new InvalidDataException( "Continue has no selected park." );
		if ( !GameEditions.Capabilities.ParkSaves ) throw new NotSupportedException( "Theme Park Inc layout checkpoints are separate from Theme Park World continuation." );
		var path = Slot( files, entry.Park.Level );
		if ( entry.Park.Sandbox || entry.Park.OwnSavePath != path ) throw new InvalidDataException( "Continue requires the selected theme's existing own-world slot." );
		var saved = ParkResumeSaveFile.Load( path );
		if ( !saved.Theme.Equals( Theme( entry.Park.Level ), StringComparison.OrdinalIgnoreCase ) || saved.StartKind != entry.StartKind ||
			saved.Economy.Mode != entry.Mode || saved.Economy.Easy != entry.Easy )
			throw new InvalidDataException( "The saved theme or game mode changed after Continue was offered. Reopen the theme choice." );
		return saved;
	}
}

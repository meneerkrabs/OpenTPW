using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The selected language's own interface archive (<c>ui.wad</c>) in place of the game's. tp.exe itself only reads the fixed <c>data\ui.wad</c> of its one
/// data folder; this models the language pack's install instruction (copy the language's <c>data</c> folder over the game's) and a single-language CD's
/// own <c>Data</c> folder (docs/LANGUAGES.md, "Files beside the text"). The pack's US English archive differs only in <c>tpw_logo.wct</c> (both sizes),
/// the Sim Theme Park logo of the lobby and the player screen; the Korean CD's has its own logo and an IME list frame.
/// </summary>
// [EXT:language-settings] swapping the archive per selected language, and on a live switch, is OpenTPW's: the original has one install and one data\ui.wad
public static class LanguageInterfaceArchive
{
	/// <summary>Raised when the archive in use changes (a live language switch): drawn interface images are reloaded.</summary>
	public static event Action? Changed;

	/// <summary>The language's own archive in use, or null for the game's.</summary>
	public static string? Current { get; private set; }

	/// <summary>Points the game data's <c>ui.wad</c> at <paramref name="language"/>'s own one, or back at the game's.</summary>
	public static void Apply( BaseFileSystem files, GameLanguage language )
	{
		ArgumentNullException.ThrowIfNull( files );
		ArgumentNullException.ThrowIfNull( language );
		var own = language.OwnInterfaceArchive;
		var game = GameLanguage.FindEntry( language.BaseDataDirectory, "ui.wad", false );
		var replacement = own != null && game != null && !string.Equals( Path.GetFullPath( own ), Path.GetFullPath( game ), StringComparison.OrdinalIgnoreCase ) ? own : null;
		if ( string.Equals( replacement, Current, StringComparison.Ordinal ) && (replacement == null) == (files.ArchiveRedirect == null) )
			return;
		Current = replacement;
		// the archive that stops being read keeps no file open
		WadArchive.CloseFileHandles();
		files.ArchiveRedirect = replacement == null ? null : candidate => string.Equals( Path.GetFullPath( candidate ), Path.GetFullPath( game! ), StringComparison.OrdinalIgnoreCase ) ? replacement : null;
		files.InvalidateLookups();
		UiImages.ResetListings();
		Changed?.Invoke();
	}
}

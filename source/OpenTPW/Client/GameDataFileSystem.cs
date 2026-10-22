namespace OpenTPW;

/// <summary>The file system of the game's data folder, as the desktop and the browser start make it.</summary>
internal static class GameDataFileSystem
{
	/// <summary>
	/// The game data's file system, with the WAD and SDT archive handlers. It remembers its path lookups (<see cref="BaseFileSystem.CacheLookups"/>): without that
	/// every read listed each directory along its path again, 15,770 listings for a desktop park start and a few hundred for an object's first placement (issue
	/// #754, docs/ENGINE-PERFORMANCE.md, "Remembered path lookups"). On the desktop nothing in a session adds files below the data folder (a new game folder
	/// applies at the next start); in the browser the files arrive through its AddFile, which invalidates the lookups. What changes what the lookups see
	/// invalidates them itself: the mod overrides, the language's interface archive and the mod content roots.
	/// </summary>
	public static BaseFileSystem Create( string dataDirectory )
	{
		var files = new BaseFileSystem( dataDirectory ) { CacheLookups = true };
		files.RegisterArchiveHandler<WadArchive>( ".wad" );
		files.RegisterArchiveHandler<SdtArchive>( ".sdt" );
		return files;
	}
}

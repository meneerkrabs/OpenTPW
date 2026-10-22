namespace OpenTPW;

/// <summary>
/// The object type's own sound list: a sound category an object archive may carry next to its models and scripts (<c>cat_rideSFX.map</c>,
/// <c>cat_rideBANK.map</c> and <c>BankHD.sdt</c>), registered when the type loads and played by ride-script <c>EVENT</c> and <c>ADDOBJ</c> type 10
/// (<c>OriginalObjectRuntime</c>). Of the shipped archives only Jungle's loudspeaker (<c>features/speaker1</c>) carries one, and no shipped script plays it.
/// </summary>
public static class ObjectTypeSounds
{
	/// <summary>The category name the type loader registers (tp.exe <c>0x74787C</c>).</summary>
	public const string CategoryName = "cat_ride";

	/// <summary>The archive members the type loader requires before it registers the category (tp.exe <c>0x41F420</c>).</summary>
	public static readonly string[] RequiredFiles = { "cat_rideBANK.map", "cat_rideSFX.map", "BankHD.sdt" };

	/// <summary>The scoped particle template whose slot is the bank's (<c>+0x4DC + 4 x 5</c>): an archive member <c>*.emt</c> whose name before the extension ends in this digit.</summary>
	public const char CollidingTemplateDigit = '5';

	/// <summary>
	/// The type's own category, or null when the archive lacks one of <see cref="RequiredFiles"/>, its maps cannot be read, or a scoped particle template
	/// numbered 5 takes the slot.
	/// </summary>
	// [BIN:TPW-EXE:0x413C10 object loader] after the type's settings the loader asks 0x41F420 whether the archive holds the custom bank and, when it does, registers it (0x41408D-0x4140A6) before the scoped particle templates (0x41F630, 0x414420 at 0x4140BC)
	// [BIN:TPW-EXE:0x41F420 custom bank gate] three lookups through the archive's file system (vf+4): the static strings 0x78C438, 0x78C240 and 0x78C4E0 (their text pointers at +8: 0x78C440, 0x78C248, 0x78C4E8), built at 0x41ADA0, 0x41ADD0 and 0x41AE00 from "cat_rideBANK.map" (0x747888), "cat_rideSFX.map" (0x74789C) and "BankHD.sdt" (0x7478AC); all three must be found
	// [BIN:TPW-EXE:0x41408D custom bank load] 0x51B4F0(the archive's folder (vf+0x18), "cat_ride" 0x74787C) registers the category with the sound library (vf+4, the loader 0x6BF330 every category goes through) and the type record keeps the handle at +0x4F0 (0x4140A6); 0x51B4F0 returns 0 with the sound system off (0x802BC8, 0x802BD4), which OpenTPW's silent audio leaves to the submit
	// The category is read with the loader every other category uses (SoundCategory.TryLoad), so its banks follow the same folder rule; Jungle's loudspeaker names its bank "Speaker\Bank", which that rule looks for at /levels/jungle/features/Speaker/BankHD.sdt, and the shipped data has the bank inside the archive instead; the folder 0x51B4F0 receives is the same archive object's (esi+0x2C, vf+0x18, 0x413DDA/0x41409A), so the original most likely misses that bank as well (a data mismatch, not reached: no shipped script plays type 10)
	public static SoundCategory? Load( ObjectCatalogEntry entry )
	{
		var fileSystem = entry.FileSystem;
		var archive = entry.ArchivePath.TrimEnd( '/' );
		string[] members;
		try
		{
			// the members of an archive (a .wad) are listed by the file system; its FileExists sees only loose files
			members = fileSystem.GetFiles( archive ).Select( path => Path.GetFileName( path ) ).ToArray();
		}
		catch ( DirectoryNotFoundException )
		{
			return null;
		}
		catch ( Exception exception )
		{
			// any failure, not only IO: the list loads once per type (a Lazy), and a kept exception would be rethrown by every later type-10 sound
			Log?.Warning( $"Object {entry.ArchivePath}: its own sound list cannot be looked up: {exception.Message}" );
			return null;
		}
		bool Has( string name ) => members.Contains( name, StringComparer.OrdinalIgnoreCase );
		if ( !RequiredFiles.All( Has ) || members.Any( TakesTheSlot ) )
			return null;
		return SoundCategory.TryLoad( fileSystem, archive, CategoryName, path => Has( Path.GetFileName( path ) ) );
	}

	/// <summary>Whether an archive member is a scoped particle template that the loader stores in the bank's slot after the bank (a <c>*.emt</c> member whose stem ends in 5).</summary>
	// [BIN:TPW-EXE:0x414420 scoped templates] for every "*.emt" member (the pattern 0x788F40, built at 0x411970 from 0x747AD4; 0x41F630 tests the same string 0x78BCF0 first) the stem without ".emt" (0x424D30, compared with _stricmp) is cut to its last character (0x5F7D90 Right(1)), atoi (0x67A480) gives n and the template id 0x51FA20 returns is stored at +0x4DC + 4n (0x41455A): n = 5 overwrites the custom bank at +0x4F0, and EVENT type 10 then submits the template id as the category handle
	// [APPROX:PTCL-908] OpenTPW does not load the scoped templates, so their ids and the category a template id would name are not modelled: a type whose archive has such a template plays nothing for type 10 (no shipped archive has one)
	internal static bool TakesTheSlot( string member ) =>
		member.Length > 4 && member.EndsWith( ".emt", StringComparison.OrdinalIgnoreCase ) && member[^5] == CollidingTemplateDigit;
}

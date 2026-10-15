using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>The game's own pointers: a white glove in the park, a yellow arrow over windows, a red no-entry sign over a spot that refuses the action.</summary>
public sealed partial class ParkHud
{
	private NVector2? pointer;
	private bool pointerOverUi;
	private bool placementRefused;
	private string? cursorPath;
	private static readonly Dictionary<Input.CursorTypes, string?> cursorArt = new();
	private static BaseFileSystem? cursorFileSystem;

	// [DATA:video 15:20, 1:50, 12:00] in the park the pointer is a white glove, over windows a yellow arrow, over an invalid build spot a red no-entry sign; a placement shows the placing pointer; in the camcorder and the ride view a yellow "+" crosshair (77:10)
	// [BIN:TPW-EXE:0x489639 cursor loader] the original loads its pointers from Data/ui/cursors with LoadCursorFromFileA (cursor object 0x661935); init 0x489ca0 loads the .ani set and the .tga set (0x4899c0) and hides the system pointer
	// [BIN:TPW-EXE:0x52f950 tool pointer] the 22 pointer ids and the tool-to-pointer rules are OriginalPointers; the tool modes 1 (path), 3 (queue), 4 (place), 0xb and 0x10 (track) are the ones OpenTPW has
	// [APPROX:UI-100] the pointers are the four-frame colour strips of the same folder (Cnor.tga, Cnog.tga, Cpic.tga ...) drawn by the UI at the pointer's hot spot (top left; the .ani files' own hot spots are 0,0) at 6 frames a second, the system pointer being hidden, instead of the .ani files as OS cursors (the .ani frames are monochrome, the video shows the colour art); the conditions behind the link, end and cash pointers (0x816d60, 0x816d4c, 0x816d5c) are not traced, so those pointers are never chosen, and the crosshair's art (CCro.tga) is not in the game data, so the system pointer stays in the camcorder and the ride view; the coaster tool uses the track pointer, the pylon and move tools and the erase, height, rotate, pivot and swap pointers have no OpenTPW tool
	private void UpdateCursor( UiInput input, bool overUi, bool uiCapturesMouse )
	{
		pointer = input.Mouse.X >= 0 && input.Mouse.Y >= 0 ? input.Mouse : null;
		pointerOverUi = overUi;
		var type = ChooseCursor();
		Input.CursorType = type;
		cursorPath = CursorArt( type );
		// the system pointer stays unless the game's own pointer will be drawn this frame: its art is loaded and decodes, the pointer is in the window and no ImGui panel wants the mouse
		Input.SoftwareCursorRequested = ShouldDrawSoftwareCursor( cursorPath, pointer != null, uiCapturesMouse );
	}

	/// <summary>Whether the game's own pointer replaces the system pointer this frame.</summary>
	internal static bool ShouldDrawSoftwareCursor( string? artPath, bool pointerInWindow, bool uiCapturesMouse ) => artPath != null && pointerInWindow && !uiCapturesMouse;

	private Input.CursorTypes ChooseCursor()
	{
		if ( pointerOverUi )
			return Input.CursorTypes.Normal;
		// the camcorder's view and the ride view show the crosshair; the armed camcorder the camera pointer
		if ( IsRiding || level.FreeWalk != null )
			return OriginalPointers.TypeOf( 21 );
		if ( CamcorderArmed )
			return OriginalPointers.TypeOf( 19 );
		if ( level.StaffPlacement is { IsActive: true } staffTool )
			return OriginalPointers.TypeOf( OriginalPointers.ForTool( 0, noPlace: staffTool.HoverCell != null && !staffTool.HoverValid ) ?? 9 );
		// the tool mode of the original: 4 places a blueprint, the cell tool's own mode is the cell type it writes (path 1, queue 3, kart track 11, water track 16)
		var mode = level.BuildEntry != null ? 4 : level.CellTool.IsActive ? (int)level.CellTool.Mode : level.IsCoasterToolActive ? 11 : 0;
		var pointer = OriginalPointers.ForTool( mode, noPlace: level.BuildEntry != null && placementRefused );
		return OriginalPointers.TypeOf( pointer ?? 2 );
	}

	/// <summary>Draws the game's pointer; the game flow calls it after every other layer so nothing covers it.</summary>
	public void DrawCursor( UiContext context )
	{
		if ( cursorPath == null || pointer is not { X: >= 0, Y: >= 0 } at )
			return;
		var size = 32f * Math.Max( 1, context.Canvas.TextScale );
		var frame = (int)(context.Time * 6f) % 4;
		context.Batch.AddQuad( UiTexture.Image( cursorPath ), new UiRect( at.X, at.Y, size, size ), new NVector2( frame * 0.25f, 0 ), new NVector2( frame * 0.25f + 0.25f, 1 ), RgbaByte.White );
	}

	/// <summary>
	/// The pointer art of <paramref name="type"/>: the file found case-insensitively in the game's Data/ui/cursors through the game file system, kept per type until the game folder changes,
	/// or null when it is missing or does not decode (the system pointer then stays).
	/// </summary>
	internal static string? CursorArt( Input.CursorTypes type )
	{
		var files = FileSystem;
		if ( !ReferenceEquals( files, cursorFileSystem ) )
		{
			cursorArt.Clear();
			cursorFileSystem = files;
		}
		if ( cursorArt.TryGetValue( type, out var known ) )
			return known;
		return cursorArt[type] = LoadCursorArt( files, CursorFile( type ) );
	}

	private static string? LoadCursorArt( BaseFileSystem? files, string? name )
	{
		if ( files == null || name == null )
			return null;
		try
		{
			// The game file system reads Data/ui as the archive ui.wad, so the cursor folder is walked on the real disk, matching every name case-insensitively (a case-sensitive disk or the browser's file system may spell it Data/UI/Cursors).
			var directory = files.GetAbsolutePath( "/" );
			foreach ( var part in new[] { "ui", "cursors" } )
				directory = Directory.EnumerateDirectories( directory ).FirstOrDefault( candidate => string.Equals( Path.GetFileName( candidate ), part, StringComparison.OrdinalIgnoreCase ) ) ?? "";
			if ( directory.Length == 0 )
				return null;
			var path = Directory.EnumerateFiles( directory ).FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name + ".tga", StringComparison.OrdinalIgnoreCase ) );
			if ( path == null )
				return null;
			UiImages.Load( path );
			return path;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException or ArgumentException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	private static string? CursorFile( Input.CursorTypes type ) => OriginalPointers.IdOf( type ) is >= 0 and var id ? OriginalPointers.File( id ) : null;
}

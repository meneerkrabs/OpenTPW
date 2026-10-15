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

	// [DATA:video 15:20, 1:50, 12:00] in the park the pointer is a white glove, over windows a yellow arrow, over an invalid build spot a red no-entry sign; a placement shows the placing pointer (the original's Data/ui/cursors set: Cpic, Cnor, Cnog, Cpla, Ccar, Cpat)
	// [BIN:TPW-EXE:0x489639 cursor loader] the original loads its pointers from Data/ui/cursors with LoadCursorFromFileA (cursor object 0x661935)
	// [APPROX:UI-100] the pointers are the four-frame colour strips of the same folder (Cnor.tga, Cnog.tga, Cpic.tga ...) drawn by the UI at the pointer's hot spot (top left; the .ani files' own hot spots are 0,0) at 6 frames a second, the system pointer being hidden, instead of the .ani files as OS cursors (the .ani frames are monochrome, the video shows the colour art); which cursor the original uses for each tool state beyond the video's cases (glove, arrow, no-entry, place, carry, path) is not traced
	private void UpdateCursor( UiInput input, bool overUi, bool uiCapturesMouse )
	{
		pointer = input.Mouse.X >= 0 && input.Mouse.Y >= 0 ? input.Mouse : null;
		pointerOverUi = overUi;
		var type = ChooseCursor();
		Input.CursorType = type;
		cursorPath = CursorArt( type );
		if ( IsRiding )
		{
			// the ride view's crosshair is drawn from rectangles (its art is not in the data files), so the system pointer is hidden whenever the pointer is in the window
			Input.SoftwareCursorRequested = pointer != null && !uiCapturesMouse;
			return;
		}
		// the system pointer stays unless the game's own pointer will be drawn this frame: its art is loaded and decodes, the pointer is in the window and no ImGui panel wants the mouse
		Input.SoftwareCursorRequested = ShouldDrawSoftwareCursor( cursorPath, pointer != null, uiCapturesMouse );
	}

	/// <summary>Whether the game's own pointer replaces the system pointer this frame.</summary>
	internal static bool ShouldDrawSoftwareCursor( string? artPath, bool pointerInWindow, bool uiCapturesMouse ) => artPath != null && pointerInWindow && !uiCapturesMouse;

	private Input.CursorTypes ChooseCursor()
	{
		if ( IsRiding )
			return Input.CursorTypes.Crosshair;
		if ( pointerOverUi )
			return Input.CursorTypes.Normal;
		if ( level.StaffPlacement is { IsActive: true } staffTool )
			return staffTool.HoverCell != null && !staffTool.HoverValid ? Input.CursorTypes.NoGo : Input.CursorTypes.Carry;
		if ( level.BuildEntry != null )
			return placementRefused ? Input.CursorTypes.NoGo : Input.CursorTypes.Place;
		if ( level.CellTool.IsActive )
			return Input.CursorTypes.Path;
		return Input.CursorTypes.Pick;
	}

	/// <summary>Draws the game's pointer; the game flow calls it after every other layer so nothing covers it.</summary>
	public void DrawCursor( UiContext context )
	{
		if ( IsRiding && pointer is { X: >= 0, Y: >= 0 } crosshairAt )
		{
			DrawCrosshair( context, crosshairAt );
			return;
		}
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

	private static string? CursorFile( Input.CursorTypes type ) => type switch
	{
		Input.CursorTypes.Normal => "Cnor",
		Input.CursorTypes.NoGo => "Cnog",
		Input.CursorTypes.Pick => "Cpic",
		Input.CursorTypes.Place => "Cpla",
		Input.CursorTypes.Carry => "Ccar",
		Input.CursorTypes.Path => "Cpat",
		_ => null
	};
}

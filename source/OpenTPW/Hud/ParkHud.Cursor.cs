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
	private static readonly Dictionary<Input.CursorTypes, bool> cursorFiles = new();

	// [DATA:video 15:20, 1:50, 12:00] in the park the pointer is a white glove, over windows a yellow arrow, over an invalid build spot a red no-entry sign; a placement shows the placing pointer (the original's Data/ui/cursors set: Cpic, Cnor, Cnog, Cpla, Ccar, Cpat)
	// [BIN:TPW-EXE:0x489639 cursor loader] the original loads its pointers from Data/ui/cursors with LoadCursorFromFileA (cursor object 0x661935)
	// [APPROX:UI-100] the pointers are the four-frame colour strips of the same folder (Cnor.tga, Cnog.tga, Cpic.tga ...) drawn by the UI at the pointer's hot spot (top left; the .ani files' own hot spots are 0,0) at 6 frames a second, the system pointer being hidden, instead of the .ani files as OS cursors (the .ani frames are monochrome, the video shows the colour art); which cursor the original uses for each tool state beyond the video's cases (glove, arrow, no-entry, place, carry, path) is not traced
	private void UpdateCursor( UiInput input, bool overUi )
	{
		pointer = input.Mouse;
		pointerOverUi = overUi;
		Input.SoftwareCursorRequested = true;
		Input.CursorType = ChooseCursor();
	}

	private Input.CursorTypes ChooseCursor()
	{
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

	private void DrawCursor( UiContext context )
	{
		if ( pointer is not { X: >= 0, Y: >= 0 } at )
			return;
		var name = CursorFile( Input.CursorType );
		if ( name == null || !CursorExists( Input.CursorType, name ) )
			return;
		var size = 32f * Math.Max( 1, context.Canvas.TextScale );
		var frame = (int)(context.Time * 6f) % 4;
		var path = GameDir.GetPath( $"/data/ui/cursors/{name}.tga" );
		context.Batch.AddQuad( UiTexture.Image( path ), new UiRect( at.X, at.Y, size, size ), new NVector2( frame * 0.25f, 0 ), new NVector2( frame * 0.25f + 0.25f, 1 ), RgbaByte.White );
	}

	private static bool CursorExists( Input.CursorTypes type, string name )
	{
		if ( !cursorFiles.TryGetValue( type, out var exists ) )
			cursorFiles[type] = exists = TryExists( name );
		return exists;
	}

	private static bool TryExists( string name )
	{
		try { return File.Exists( GameDir.GetPath( $"/data/ui/cursors/{name}.tga" ) ); }
		catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or ArgumentException or NullReferenceException ) { return false; }
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

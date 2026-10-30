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
	// [BIN:TPW-EXE:0x52f950 tool pointer] the 22 pointer ids and the tool-to-pointer rules are OriginalPointers; the tool modes 1 (path), 3 (queue), 4 (place), 0xb and 0x10 (track) and 0x39 (Buy Land) are the ones OpenTPW has
	// [EXT:software-pointers] the pointers are the four-frame colour strips of the same folder (Cnor.tga, Cnog.tga, Cpic.tga ...) drawn by the UI at the pointer's hot spot (top left; the .ani files' own hot spots are 0,0) at 6 frames a second, the system pointer being hidden, instead of the .ani files as OS cursors, a design choice: the host platforms and the browser cannot load animated Windows cursors, and the .ani frames are monochrome where the video shows the colour art; the link pointer is chosen for completed path/queue previews that visit their link branch (docs/CURSOR-LINK-PREVIEW.md); end pointers and refused-preview arbitration remain unmodelled (their writers are listed at OriginalPointers), the cash pointer (0x816d5c) is chosen for Buy Land's, the blueprint's and the cell lines' (path, queue, track) money refusals (the other writer 0x539fb6 is not modelled), and the crosshair's art (CCro.tga) is not in the game data, so the system pointer stays in the camcorder and the ride view; the coaster tool uses the track pointer, the pylon and move tools and the erase, height, rotate, pivot and swap pointers have no OpenTPW tool
	private void UpdateCursor( UiInput input, bool overUi, bool uiCapturesMouse )
	{
		pointer = input.Mouse.X >= 0 && input.Mouse.Y >= 0 ? input.Mouse : null;
		pointerOverUi = overUi;
		var type = ChooseCursor();
		Input.CursorType = type;
		cursorPath = CursorArt( type );
		if ( RideCrosshair )
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

	/// <summary>Whether the ride view's crosshair is the pointer: while riding, but not under the winning screen, whose window sets no pointer of its own (the camcorder window that sets the crosshair, 0x7CB2B0, is hidden then).</summary>
	// [BIN:TPW-EXE:0x0048AF0C winning pointer] the winning screen hides the camcorder window (message 6), the window that gives the ride view its crosshair (pointer 0x15, 0x65F11D at 0x48A20B), and creates its own full-screen window 0x7472 with no pointer call, so the crosshair is not the pointer over the winning camera; OpenTPW shows the window pointer there
	private bool RideCrosshair => IsRiding && finaleScreen == null;

	private Input.CursorTypes ChooseCursor()
	{
		if ( RideCrosshair )
			return Input.CursorTypes.Crosshair;
		if ( pointerOverUi )
			return Input.CursorTypes.Normal;
		// the camcorder's view and the ride view show the crosshair; the armed camcorder the camera pointer
		if ( IsRiding || level.FreeWalk != null )
			return OriginalPointers.TypeOf( 21 );
		if ( CamcorderArmed )
			return OriginalPointers.TypeOf( 19 );
		if ( level.StaffPlacement is { IsActive: true } staffTool )
			return OriginalPointers.TypeOf( OriginalPointers.ForTool( 0, noPlace: staffTool.HoverCell != null && !staffTool.HoverValid ) ?? 9 );
		// [BIN:TPW-EXE:0x5323c1 land cash pointer] the Buy Land validator's money refusal sets 0x816d5c, which the pointer switch 0x52f950 turns into the cash pointer (20)
		if ( level.BuyLandTool is { IsActive: true } landTool )
			return OriginalPointers.TypeOf( OriginalPointers.ForTool( 0x39, cash: landTool.HoverTooDear ) ?? 2 );
		if ( level.AreaClearTool is { IsActive: true } )
			return OriginalPointers.TypeOf( OriginalPointers.ForTool( 0x3A ) ?? 2 );
		// [EXT:pools-tool] the pool tool draws the path tool's pointer, and the cash pointer when the cell under it cannot be paid for; the original has no pool pointer
		if ( level.PoolTool is { IsActive: true } poolTool )
			return OriginalPointers.TypeOf( OriginalPointers.ForTool( 1, cash: poolTool.HoverResult == PoolBuildResult.NotEnoughMoney ) ?? 2 );
		// the tool mode of the original: 4 places a blueprint, the cell tool's own mode is the cell type it writes (path 1, queue 3, kart track 11, water track 16)
		var mode = level.BuildEntry != null ? (level.IsMoving ? MoveToolMode : 4) : level.CellTool.IsActive ? (int)level.CellTool.Mode : level.IsCoasterToolActive ? 11 : 0;
		var ghost = level.CellTool.IsActive ? level.CellTool.Ghost : null;
		var pointer = OriginalPointers.ForTool( mode, link: LinkPointerShown( mode, level.CellTool.Ghost ), end: EndPointerShown( mode, ghost ), noPlace: level.BuildEntry != null && placementRefused || GhostRefused( mode, ghost ),
			cash: CashPointerShown( mode ) );
		return OriginalPointers.TypeOf( pointer ?? 2 );
	}

	/// <summary>The bounded link indication of a completed current path or queue preview.</summary>
	internal static bool LinkPointerShown( int mode, SegmentResult? ghost ) =>
		mode is (int)CellToolMode.Path or (int)CellToolMode.Queue && ghost is { Completed: true, LinkPointer: true };

	/// <summary>The end indication of a completed current path or queue preview: the validator's code 0xB, a last cell of the tool's own kind.</summary>
	// [BIN:TPW-EXE:0x52F96D tool pointer] mode 1 and 3 (and 11, 16) take the link pointer (5) first and then the end pointer (0x12) from the flags 0x816D60 and 0x816D4C, which the validator 0x535670 sets at 0x535C72/0x535D02 (code 8) and 0x535AE5/0x535B3D (code 0xB), as the path and queue ghosts report them
	internal static bool EndPointerShown( int mode, SegmentResult? ghost ) =>
		mode is (int)CellToolMode.Path or (int)CellToolMode.Queue && ghost is { Completed: true, EndPointer: true };

	/// <summary>Whether the line preview has a refused cell, which the pointer switch turns into the no-place pointer (0x527950 reads the latch 0x816D48 that 0x533933 sets when the validator refuses a cell).</summary>
	// [BIN:TPW-EXE:0x537A1B track ghost latch] the kart and water line modes set the same latch when their ghost routine 0x5370E0 refuses a tile (0x537A1B -> 0x527940(1)), as the path and queue validator does at 0x533933 inside 0x532FC0
	// [BIN:TPW-EXE:0x52F9F3 no-place pointer] after the tool's own pointer the switch asks 0x527950 (the ghost's refusal latch 0x816D48) and sets pointer 8, then lets the cash pointer (0x816D5C) win; a ghost that stopped only at OpenTPW's own queue-length bound is no refusal of the validator
	internal static bool GhostRefused( int mode, SegmentResult? ghost ) =>
		mode is (int)CellToolMode.Path or (int)CellToolMode.Queue or (int)CellToolMode.KartTrack or (int)CellToolMode.WaterTrack && ghost is { Completed: false, StoppedByOwnLimit: false };

	/// <summary>
	/// Whether the tool under the pointer costs more than the bank holds: the blueprint tool when its price exceeds the balance, the path and queue tools when the line
	/// under the pointer is stopped for money.
	/// </summary>
	// [BIN:TPW-EXE:0x523662 blueprint cash flag] the hover handler 0x5234d0 clears the flags 0x816d4c, 0x816d50, 0x816d5c and 0x816d60 on entry; in the place mode (4) it reads the tool's cost (0x52fbc0, the price 0x52fb90 stored from +0x1b8) and the balance (0x6ad810) and sets 0x816d50 and the cash flag 0x816d5c when the cost is greater, the replay flag 0x785a2c being clear
	// [BIN:TPW-EXE:0x53593f line cash flag] the cell validator 0x535670 sets the same flags and refuses the cell (returns 1) when the line's cost so far exceeds the balance (0x535927: cost > balance, equal allowed); the pointer switch 0x52f950 lets the cash flag override the tool's own pointer and noplace
	private bool CashPointerShown( int mode ) => CashPointerShown( mode, BuildActionCost, Status.Money, level.CellTool.IsActive ? level.CellTool.Ghost : null );

	/// <summary>The interface mode of the Move tool (<c>0x3b</c>), which draws the build tool's placing pointer (<c>0x52f950</c>: 4 and 0x3b share it) but never the cash pointer: its hover (<c>0x523940</c>) has no cash flag.</summary>
	internal const int MoveToolMode = 0x3b;

	internal static bool CashPointerShown( int mode, long? cost, long balance, SegmentResult? ghost ) => mode switch
	{
		4 => cost is { } price && price > balance,
		(int)CellToolMode.Path or (int)CellToolMode.Queue or (int)CellToolMode.KartTrack or (int)CellToolMode.WaterTrack => ghost is { StoppedBy: CellBuildResult.NotEnoughMoney },
		_ => false,
	};

	/// <summary>Draws the game's pointer; the game flow calls it after every other layer so nothing covers it.</summary>
	public void DrawCursor( UiContext context )
	{
		if ( RideCrosshair && pointer is { X: >= 0, Y: >= 0 } crosshairAt )
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

	private static string? CursorFile( Input.CursorTypes type ) => OriginalPointers.IdOf( type ) is >= 0 and var id ? OriginalPointers.File( id ) : null;
}

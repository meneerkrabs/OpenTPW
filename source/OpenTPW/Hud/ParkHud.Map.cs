using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>The original map window (Mac table <c>data:0x5265e</c>) with live paths and objects over the shipped theme map.</summary>
public sealed partial class ParkHud
{
	// [BIN:TPW-EXE:0x985] map zoom control b_minus (0x985) subtracts and 0x986 adds half a step, clamped to 1.0..3.0.
	private const float MapZoomStep = 0.5f;
	private UiScreen? mapScreen;
	private MapViewState mapView = MapViewState.Default;
	// [BIN:TPW-EXE:0x5f186c] each map category button XORs its own layer bit (the bits are 0x20, 8, 1, 2, 4 and 0x10,
	// picked through the index table at 0x5f186c); the layers are independent toggles, not an exclusive filter, and each
	// category is drawn from its own bit only. The layer word ([object+0x18]) is set to 1 by the map object's static
	// constructor (0x5f06a9-0x5f06c3), i.e. only the layer of control 2439 (bit 1) is on at the start.
	// [BIN:TPW-EXE:0x774da0 map table] the category controls are 2439 b_sride (64,1409)-(167,1512) help 267 → bit 1, 2452 (184,1409) → bit 4,
	// 2451 (303,1409) → bit 2 and 2434 (423,1409) → bit 8, beside the track control 2441 (542,1409) → 0x10 and the visitor control 2435 (1390,1409) → 0x20
	// (the handler 0x5f14f1-0x5f1628 and the rectangles of the PC map table), so only the rides layer is on when the map first opens.
	/// <summary>The category layers the map starts with: the original's layer word 1, the rides button's bit.</summary>
	public static IReadOnlyCollection<BuildCategory> InitialMapCategories { get; } = new[] { BuildCategory.Rides };
	// [BIN:TPW-EXE:0x5f06a9 map object constructor] the static map object starts with the layer word [+0x18] 1, the staff role [+0x1c] 1 (the handymen) and the value mode [+0x20] 1 (happiness), and keeps them between openings
	private uint mapLayers = ParkMapClasses.RidesLayer;
	private int mapStaff = 1;
	private int mapValue = 1;
	private ParkMapCanvas? mapCanvas;

	/// <summary>Opens the map with the layer word, staff role and value mode of <paramref name="settings"/> ("layers=63,staff=1,value=2") for the interface captures of <c>--debug-hud</c>.</summary>
	internal void DebugMap( string settings )
	{
		foreach ( var part in settings.Split( ',', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var pair = part.Split( '=', 2 );
			if ( pair.Length != 2 || !int.TryParse( pair[1], System.Globalization.CultureInfo.InvariantCulture, out var number ) )
				continue;
			switch ( pair[0] )
			{
				case "layers": mapLayers = (uint)number; break;
				case "staff": mapStaff = number; break;
				case "value": mapValue = number; break;
			}
		}
		OpenMapWindow();
	}

	/// <summary>The layer word bit of a category button: rides 1, shops 2, sideshows 4, features 8.</summary>
	private static uint LayerOf( BuildCategory category ) => category switch
	{
		BuildCategory.Rides => ParkMapClasses.RidesLayer,
		BuildCategory.Shops => ParkMapClasses.ShopsLayer,
		BuildCategory.Sideshows => ParkMapClasses.SideshowsLayer,
		BuildCategory.Features => ParkMapClasses.FeaturesLayer,
		_ => 0
	};

	/// <summary>The staff role selector the role's button sets ([map+0x1c]): handymen 1, mechanics 2, guards 3, entertainers 4; 0 for a role the map has no button for.</summary>
	// [BIN:TPW-EXE:0x5f1406 staff role controls] controls 2446 (handymen) sets [map+0x1c] 1, 2447 (mechanics) 2, 2444 (guards) 3 and 2445 (entertainers) 4; a button that is switched off sets 0 (0x5f14b6); the four buttons are one radio group (0x98b, 0x66a295)
	internal static int StaffMode( StaffType role ) => role switch { StaffType.Handyman => 1, StaffType.Mechanic => 2, StaffType.Guard => 3, StaffType.Entertainer => 4, _ => 0 };

	public bool MapStaffRole( StaffType type ) => StaffMode( type ) is not 0 and var mode && mapStaff == mode;
	/// <summary>The staff role the map shows (0 none, 1 handymen, 2 mechanics, 3 guards, 4 entertainers).</summary>
	public int MapStaffMode => mapStaff;
	public void ToggleMapStaff( StaffType type )
	{
		if ( StaffMode( type ) is var mode and not 0 )
			SetMapStaffMode( mapStaff == mode ? 0 : mode );
	}
	public void SetMapStaffMode( int mode )
	{
		mapStaff = mode;
		RenderMap();
	}

	/// <summary>The value mode the map shows ([map+0x20]): 0 none, 1 happiness, 2 excitement, 3 satisfaction.</summary>
	// [BIN:TPW-EXE:0x5f1406 value mode controls] controls 2456 (happiness) sets [map+0x20] 1, 2455 (excitement) 2 and 2457 (satisfaction) 3, off sets 0; the three buttons are one radio group (0x996)
	public int MapValueMode => mapValue;
	public void SetMapValueMode( int mode )
	{
		mapValue = mode;
		RenderMap();
	}
	public bool MapSatisfaction => mapValue == 3;
	public bool MapExcitement => mapValue == 2;
	public bool MapHappiness => mapValue == 1;
	public void ToggleMapSatisfaction() => SetMapValueMode( mapValue == 3 ? 0 : 3 );
	public void ToggleMapExcitement() => SetMapValueMode( mapValue == 2 ? 0 : 2 );
	public void ToggleMapHappiness() => SetMapValueMode( mapValue == 1 ? 0 : 1 );
	public bool MapVisitors => (mapLayers & ParkMapClasses.VisitorsLayer) != 0;
	public void ToggleMapVisitors() => ToggleLayer( ParkMapClasses.VisitorsLayer );
	public bool MapTracks => (mapLayers & ParkMapClasses.TracksLayer) != 0;
	public void ToggleMapTracks() => ToggleLayer( ParkMapClasses.TracksLayer );

	private void ToggleLayer( uint bit )
	{
		mapLayers ^= bit;
		RenderMap();
	}

	public bool MapWindowOpen => mapScreen != null && Stack.Screens.Contains( mapScreen );
	public float MapZoom => mapView.Zoom;
	public bool MapCategoryShown( BuildCategory category ) => (mapLayers & LayerOf( category )) != 0;
	public void ToggleMapCategory( BuildCategory category )
	{
		if ( LayerOf( category ) is var bit and not 0 )
			ToggleLayer( bit );
	}

	public void OpenMapWindow()
	{
		if ( MapWindowOpen )
			return;
		if ( level.OriginalPark is { } park && !FileSystem.FileExists( $"/levels/{park.LevelName}/2dmap.tga" ) )
		{
			var message = $"Map image missing for {park.LevelName}.";
			Log?.Warning( message );
			PostMessage( message );
			return;
		}
		mapScreen ??= CreateMapScreen();
		// [BIN:TPW-EXE:0x5f0fa7 map open] the open handler writes the cell classes (0x5f2050) and the dot items (0x5f3710) once, loads the art (0x5f1ea0) and draws the surface (0x5f2380); the classes and the dots then stay as they were while the window is open
		mapCanvas = BuildMapCanvas();
		RenderMap();
		Stack.Push( mapScreen );
		mapScreen.Focus( mapScreen.Find( "close" ) );
		// [BIN:TPW-EXE:0x4a1737 drawer open] opening the map/research drawer (message 0xA) plays cat_ui event 0x1C
		GameAudio.PlayUi( GameAudio.DrawerOpenEvent );
	}

	public void CloseMapWindow()
	{
		if ( mapScreen == null || !MapWindowOpen )
			return;
		while ( Stack.Top != mapScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == mapScreen )
			Stack.Pop();
		// [BIN:TPW-EXE:0x4a186f drawer close] a close plays cat_ui 0x1C when it starts and 0xC2 when the slide finishes (0x4a1947); OpenTPW has no slide, so both play at once
		// [EXT:drawer-window] the map is a window that opens and closes at once where the original slides a drawer out of the main panel (docs/AUDIO.md; UI register)
		GameAudio.PlayDrawerClose();
	}

	public void ZoomMap( int direction )
	{
		mapView = mapView.ZoomBy( direction * MapZoomStep );
	}

	public void PanMap( int x, int y )
	{
		// [BIN:TPW-EXE:0x5f35b0 map pan] the pan buttons and arrow keys move the view centre by one map unit (cell) whatever the zoom,
		// clamped to 0..dimension-1.
		PanMap( (float)x, y );
	}

	private void PanMap( float x, float y )
	{
		var (width, height) = MapDimensions();
		mapView = mapView.PanBy( x, y, width, height );
	}

	// [EXT:frame-driven-at-60hz] the original steps the edge scroll once per drawn frame (message 0x1e); OpenTPW takes the elapsed time as that many 60 Hz reference frames
	private readonly ReferenceFrameClock mapEdgeClock = new( 0.25f );

	/// <summary>
	/// The view centre's move, in map cells (x right, y down on the map), of one reference frame with the pointer at (<paramref name="x"/>, <paramref name="y"/>)
	/// as shares of the window (0..1): zero in the inner 98 per cent, else 250 cells per unit of the pointer's depth into the outer 1 per cent.
	/// </summary>
	// [BIN:TPW-EXE:0x5f1130 map edge scroll] on every message 0x1e, while the scroll option 0x78d910 is Pushscroll (0; the options toggle 0x4a385a sets it to 1 for Right button), the map view reads its pointer (messages 0x10001 and 0x10003, X over 2048 and Y over 1536, clamped to 0..1) and calls 0x5f35b0 with direction 0 (x less) by (0.01 - X) x 250 below 0.01, 1 (x more) by (X - 0.99) x 250 above 0.99, 2 (the up button's, y more) by (0.01 - Y) x 250 below 0.01 and 3 (down) by (Y - 0.99) x 250 above 0.99; 0x5f35b0 clamps the centre to 0..size-1
	internal static (float X, float Y) MapEdgeStep( float x, float y )
	{
		static float Axis( float share )
		{
			share = Math.Clamp( share, 0f, 1f );
			return share < 0.01f ? -(0.01f - share) * 250f : share > 0.99f ? (share - 0.99f) * 250f : 0f;
		}
		return (Axis( x ), Axis( y ));
	}

	/// <summary>The view after <paramref name="frames"/> reference frames of edge scroll with the pointer at <paramref name="pointer"/> in a window of <paramref name="width"/> x <paramref name="height"/>; unchanged unless the scroll option is Pushscroll and the pointer is inside.</summary>
	internal static MapViewState EdgeScrolled( MapViewState view, int frames, NVector2 pointer, float width, float height, ScrollMode scroll, int mapWidth, int mapHeight )
	{
		if ( scroll != ScrollMode.Pushscroll || frames <= 0 || width <= 0 || height <= 0 || pointer.X < 0 || pointer.Y < 0 || pointer.X >= width || pointer.Y >= height )
			return view;
		var (x, y) = MapEdgeStep( pointer.X / width, pointer.Y / height );
		return x == 0 && y == 0 ? view : view.PanBy( x * frames, y * frames, mapWidth, mapHeight );
	}

	private void StepMapEdgeScroll( NVector2 pointer, float width, float height, float deltaSeconds, ScrollMode scroll )
	{
		var (mapWidth, mapHeight) = MapDimensions();
		mapView = EdgeScrolled( mapView, mapEdgeClock.Advance( deltaSeconds ), pointer, width, height, scroll, mapWidth, mapHeight );
	}

	private (int Width, int Height) MapDimensions()
	{
		var grid = level.Objects.Grid;
		var park = level.OriginalPark;
		return (Math.Min( grid.Width, park?.Heightfield.CellCountX ?? grid.Width ), Math.Min( grid.Height, park?.Heightfield.CellCountZ ?? grid.Height ));
	}

	private UiScreen CreateMapScreen()
	{
		var screen = new MapScreen( this );
		void Model( string id, string model, UiRect rect ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = rect, Anchor = UiAnchor.Center } );
		void Button( string id, string model, int help, UiRect rect, Action action ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Clicked = action, Bounds = rect, Anchor = UiAnchor.Center
		} );
		// [DATA:Mac map table 0x5265e:2432,2450,2448,2449,2453] root, map content and three lower panels.
		Model( "window", "map_win", Rect( 0, 0, 2047, 1537 ) );
		screen.Add( new ParkMapView( this ) { Id = "mapView", Bounds = Rect( 49, 33, 1877, 1364 ), Anchor = UiAnchor.Center } );
		Model( "leftPanel", "buttpan1", Rect( 35, 1387, 681, 1533 ) );
		Model( "textPanel", "textpan", Rect( 688, 1395, 1351, 1525 ) );
		Model( "rightPanel", "buttpan2", Rect( 1356, 1387, 1909, 1533 ) );
		// [DATA:Mac map table 0x5265e:2433,2436-2438,2440,2442] pan and zoom controls at table rectangles.
		Button( "up", "nuup", 263, Rect( 80, 1079, 237, 1236 ), () => PanMap( 0, -1 ) );
		Button( "down", "nudown", 264, Rect( 80, 1236, 237, 1393 ), () => PanMap( 0, 1 ) );
		Button( "left", "nuleft", 265, Rect( 1, 1157, 158, 1315 ), () => PanMap( -1, 0 ) );
		Button( "right", "nuright", 266, Rect( 158, 1157, 315, 1315 ), () => PanMap( 1, 0 ) );
		Button( "zoomOut", "b_minus", 262, Rect( 32, 1327, 90, 1385 ), () => ZoomMap( -1 ) );
		Button( "zoomIn", "b_plus", 261, Rect( 218, 1327, 275, 1384 ), () => ZoomMap( 1 ) );
		// [DATA:Mac map table 0x5265e:2434,2439,2451,2452] object-category map buttons.
		// [BIN:TPW-EXE:0x5f186c] every category button toggles its own layer bit; an object is drawn from its category's bit only.
		CategoryButton( "rides", "b_sride", BuildCategory.Rides, 267, Rect( 64, 1409, 167, 1512 ) );
		CategoryButton( "sideshows", "b_sshow", BuildCategory.Sideshows, 271, Rect( 184, 1409, 286, 1512 ) );
		CategoryButton( "shops", "b_sshop", BuildCategory.Shops, 269, Rect( 303, 1409, 406, 1512 ) );
		CategoryButton( "features", "b_sfeature", BuildCategory.Features, 273, Rect( 423, 1409, 525, 1512 ) );
		// [DATA:Mac map table 0x5265e:2441,2435,2444-2447,2455-2457] track, actor and metric controls.
		// [BIN:TPW-EXE:0x5f14f1 map layer buttons] every layer button toggles its bit of the layer word and draws the map again (0x5f1ea0, 0x5f2380)
		MetricLayer( "tracks", "b_strack", 275, Rect( 542, 1409, 644, 1512 ), ToggleMapTracks, () => MapTracks );
		Layer( "visitors", "b_svisit", 277, Rect( 1390, 1409, 1493, 1512 ), ToggleMapVisitors, () => MapVisitors );
		StaffLayer( "handymen", "b_shandy", 279, Rect( 1883, 443, 1985, 546 ), StaffType.Handyman );
		StaffLayer( "mechanics", "b_smech", 281, Rect( 1883, 592, 1985, 694 ), StaffType.Mechanic );
		StaffLayer( "entertainers", "b_senter", 285, Rect( 1883, 740, 1985, 842 ), StaffType.Entertainer );
		StaffLayer( "guards", "b_sguard", 283, Rect( 1883, 885, 1985, 987 ), StaffType.Guard );
		MetricLayer( "satisfaction", "b_ssatis", 291, Rect( 1526, 1409, 1629, 1512 ), ToggleMapSatisfaction, () => MapSatisfaction );
		Layer( "happiness", "b_shap", 287, Rect( 1646, 1409, 1748, 1512 ), ToggleMapHappiness, () => MapHappiness );
		MetricLayer( "excitement", "b_sexcit", 289, Rect( 1766, 1409, 1868, 1512 ), ToggleMapExcitement, () => MapExcitement );
		// [BIN:TPW-EXE:0x5f1cde map text] the text control 0x991 of the middle panel shows the name of the item or object under the pointer
		screen.Add( new UiLabel { Id = "mapHover", Text = () => mapHoverText, Font = fonts => fonts.Label, Fit = true, Bounds = Rect( 700, 1400, 1340, 1520 ), Anchor = UiAnchor.Center } );
		// [DATA:Mac map table 0x5265e:-1] b_okay closes the map (help 1).
		Button( "close", "b_okay", 1, Rect( 1941, 1339, 2024, 1422 ), CloseMapWindow );
		screen.Back = CloseMapWindow;
		return screen;

		void CategoryButton( string id, string model, BuildCategory category, int help, UiRect rect ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Bounds = rect, Anchor = UiAnchor.Center,
			Clicked = () => ToggleMapCategory( category ), Selected = () => MapCategoryShown( category )
		} );
		void MetricLayer( string id, string model, int help, UiRect rect, Action action, Func<bool> selected ) => screen.Add( new UiButton
		{ Id = id, Model = model, Help = strings.Help( help ), Bounds = rect, Anchor = UiAnchor.Center, Clicked = action, Selected = selected } );
		void StaffLayer( string id, string model, int help, UiRect rect, StaffType role ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Bounds = rect, Anchor = UiAnchor.Center,
			Clicked = () => ToggleMapStaff( role ), Selected = () => MapStaffRole( role ), Enabled = level.StaffWorld != null
		} );
		void Layer( string id, string model, int help, UiRect rect, Action action, Func<bool> selected ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Bounds = rect, Anchor = UiAnchor.Center,
			Clicked = action, Selected = selected, Enabled = level.Guests != null
		} );

	}

	private static readonly string[] GuestLayerIds = { "visitors", "happiness" };
	private static readonly string[] StaffLayerIds = { "handymen", "mechanics", "entertainers", "guards" };

	private sealed class MapScreen( ParkHud owner ) : UiScreen( "map" )
	{
		public override bool Update( UiContext context, UiInput input )
		{
			owner.StepMapEdgeScroll( input.Mouse, context.Canvas.Width, context.Canvas.Height, context.Delta, GameOptions.Current.Scroll );
			foreach ( var id in GuestLayerIds )
				if ( Find( id ) is UiButton button ) button.Enabled = owner.level.Guests != null;
			foreach ( var id in StaffLayerIds )
				if ( Find( id ) is UiButton button ) button.Enabled = owner.level.StaffWorld != null;
			if ( Find( "mapView" ) is ParkMapView view )
				owner.UpdateMapHover( view.Geometry, input.Mouse );
			return base.Update( context, input );
		}
	}

	private MapDot? mapHoverDot;
	private ParkMapThing? mapHoverThing;
	private string mapHoverText = "";

	/// <summary>
	/// The pointer over the map (message 0x10001, <c>0x5F198E</c>): the nearest shown item within 5 pixels of it (the first of equal distances) becomes the item under the pointer, and the object of
	/// the cell below the pointer is kept; when the item changes and either is a staff member the surface is drawn again (his area shows in the brighter blue), and the text panel names the item's
	/// thing, or else the cell's object.
	/// </summary>
	// [BIN:TPW-EXE:0x5f1a21 map hover] the loop over the items takes those shown (+0x2c) with |dx| and |dy| below 5.0 (0x703040) pixels of the pointer and keeps the one whose distance is below the best so far (1e8 at first); the item under the pointer is cleared first; a change that involves a thing of type 4 to 8 draws the surface again (0x5f1af8); the cell is ftol((pointer - screen centre) / (480 / width x zoom) + the view centre) and its class record's thing id word is kept (+0x20064); the text is the staff member's name (0x506860), the visitor's text (UITEXT 0x1c8 with the number, 0x1c9 without) or the name of the thing of the cell (0x4e2780)
	internal void UpdateMapHover( (UiRect Image, NVector2 Uv0, float Scale)? geometry, NVector2 mouse )
	{
		if ( mapCanvas is not { } canvas || geometry is not { } view )
			return;
		MapDot? best = null;
		ParkMapThing? cellThing = null;
		var surface = canvas.Surface;
		var inside = view.Image.Contains( mouse );
		if ( inside )
		{
			// the pointer in surface pixels; 5 pixels of the original's screen are 5 / zoom surface pixels, as the surface is shown one pixel to one at zoom 1
			var x = ((mouse.X - view.Image.X) / view.Scale + view.Uv0.X) * MapSurface.ImagePixels;
			var y = ((mouse.Y - view.Image.Y) / view.Scale + view.Uv0.Y) * MapSurface.ImagePixels;
			var limit = 5f / mapView.Zoom;
			var distance = float.MaxValue;
			foreach ( var dot in canvas.Dots )
			{
				if ( !ParkMapDots.Visible( dot, mapLayers, mapStaff, mapValue ) )
					continue;
				var (px, py) = surface.Position( dot.X, dot.Y );
				var (dx, dy) = (MathF.Abs( x - px ), MathF.Abs( y - py ));
				if ( dx < limit && dy < limit && MathF.Sqrt( dx * dx + dy * dy ) is var d && d < distance )
				{
					distance = d;
					best = dot;
				}
			}
			var cx = (int)((x - surface.OriginX) / (surface.Original ? 480f / surface.Width : surface.CellWidth));
			var cy = (int)((y - surface.OriginY) / (surface.Original ? 425f / surface.Height : surface.CellHeight));
			if ( x >= surface.OriginX && y >= surface.OriginY && (uint)cx < (uint)surface.Width && (uint)cy < (uint)surface.Height )
				cellThing = canvas.Owners[cy * surface.Width + cx];
		}
		var previous = canvas.Hovered;
		var changed = !ReferenceEquals( previous, best );
		canvas.Hovered = best;
		if ( changed && (IsStaffDot( previous ) || IsStaffDot( best )) )
			RenderMap();
		if ( changed || !ReferenceEquals( mapHoverThing, cellThing ) || !ReferenceEquals( mapHoverDot, best ) )
		{
			mapHoverDot = best;
			mapHoverThing = cellThing;
			mapHoverText = MapHoverText( best, cellThing );
		}
	}

	private static bool IsStaffDot( MapDot? dot ) => dot?.Staff != null;

	private string MapHoverText( MapDot? dot, ParkMapThing? cellThing )
	{
		if ( dot?.Staff is { } member )
			return StaffName( member );
		if ( dot?.Guest is { } guest )
			return guest.VisitorNumber > 0 ? strings[UIStrings.VisitorCount] + guest.VisitorNumber.ToString( System.Globalization.CultureInfo.InvariantCulture ) : strings[UIStrings.NewVisitor];
		return cellThing?.Item is { } item ? EntryName( item.Entry ) : "";
	}

	/// <summary>The map object's classes and drawing for the level as it is now; the window takes it again every time it opens.</summary>
	private ParkMapCanvas BuildMapCanvas()
	{
		var (width, height) = MapDimensions();
		var canvas = new ParkMapCanvas( new MapSurface( width, height, level.OriginalPark != null ) );
		mapThings = new LevelMapCells( level, level.Paths?.Cells, width, height, MapThingOf );
		canvas.WriteClasses( mapThings );
		canvas.SpritesAvailable = FileSystem is { } files && ParkMapDots.Images.All( files.FileExists );
		BuildMapDots( canvas );
		return canvas;
	}

	/// <summary>
	/// The items of the map window's list, written once when it opens (<c>0x5F3940</c>): two for each guest (a ring for the visitors layer and a dot for the happiness mode), one for each staff member
	/// with a second one under an entertainer or a guard (the glow of their effect), one for each littered cell and one for each security camera.
	/// </summary>
	// [BIN:TPW-EXE:0x5f3940 map items] per thing: type 1 gives (rsprite, kind 1, mode 1, value 0x20) and (vsprite, kind 0, mode 3, value 1); type 4 (mechanic), 5 (handyman), 6 (entertainer) and 7 (guard) give (ssprite, kind 2, mode 2, value 2, 1, 4, 3), the entertainer (esprite, kind 4, mode 2, value 4, scale RegionFX[0] radius × 0.5) and the guard (gsprite, kind 5, mode 2, value 3, scale RegionFX[3] radius × 0.5) a second one; then per cell with litter (+0x2c) (lsprite, kind 3, mode 2, value 1, scale 1 × 0.5, or the radius of RegionFX[2] for vomit 7 and RegionFX[5] for a stinkbomb 8 × 0.5) at the cell's centre; then per object with thing flag 0x10 (gsprite, kind 5, mode 2, value 3, scale RegionFX[4] radius × 0.5) at its cell's centre; a thing's position is its cell byte + its sub-cell byte / 256
	private void BuildMapDots( ParkMapCanvas canvas )
	{
		var dots = canvas.Dots;
		var effects = level.Guests?.Litter?.Environment.Effects;
		float Radius( int row ) => effects is { Length: > RegionEffect.Count - 1 } table ? table[row].Radius * 0.5f : 0f;
		static float Cell( int fixedValue ) => ((fixedValue >> 16) & 0xFF) + ((fixedValue >> 8) & 0xFF) / 256f;
		if ( level.Guests is { } guests )
		{
			foreach ( var guest in guests.Guests )
			{
				var (x, y) = (Cell( guest.Navigation.X ), Cell( guest.Navigation.Y ));
				dots.Add( new MapDot { X = x, Y = y, Image = 1, Kind = 1, Mode = 1, Value = (int)ParkMapClasses.VisitorsLayer, Guest = guest } );
				dots.Add( new MapDot { X = x, Y = y, Image = 0, Kind = 0, Mode = 3, Value = 1, Guest = guest } );
			}
		}
		foreach ( var member in level.Park?.Economy.Staff.Members ?? Array.Empty<StaffMember>() )
		{
			if ( member.CellX is not { } px || member.CellY is not { } py || StaffMode( member.Type ) is not (> 0 and var role) )
				continue;
			var (x, y) = (MathF.Floor( px * 256f ) / 256f, MathF.Floor( py * 256f ) / 256f);
			dots.Add( new MapDot { X = x, Y = y, Image = 2, Kind = 2, Mode = 2, Value = role, Staff = member } );
			if ( member.Type == StaffType.Entertainer )
				dots.Add( new MapDot { X = x, Y = y, Image = 4, Kind = 4, Mode = 2, Value = role, Scale = Radius( RegionEffect.Entertainer ), Staff = member } );
			else if ( member.Type == StaffType.Guard )
				dots.Add( new MapDot { X = x, Y = y, Image = 5, Kind = 5, Mode = 2, Value = role, Scale = Radius( RegionEffect.Guard ), Staff = member } );
		}
		if ( level.Guests?.Litter is { } litter )
		{
			for ( var y = 0; y < litter.Height; y++ )
				for ( var x = 0; x < litter.Width; x++ )
					if ( litter.TypeAt( x, y ) is var type and not LitterType.None )
						dots.Add( new MapDot { X = x + 0.5f, Y = y + 0.5f, Image = 3, Kind = 3, Mode = 2, Value = 1,
							Scale = type == LitterType.Stinkbomb ? Radius( RegionEffect.Stinkbomb ) : type == LitterType.Vomit ? Radius( RegionEffect.Vomit ) : 0.5f } );
		}
		foreach ( var item in level.Objects.Objects )
			if ( !item.IsDeleted && item.Entry.Settings.GetBool( "UsageInfo.ProvidesSecurity" ) )
				dots.Add( new MapDot { X = item.Placement.X + 0.5f, Y = item.Placement.Y + 0.5f, Image = 5, Kind = 5, Mode = 2, Value = 3, Scale = Radius( RegionEffect.Camera ) } );
	}

	private LevelMapCells? mapThings;

	private ParkMapThing MapThingOf( OriginalObject item, int number )
	{
		var settings = item.Entry.Settings;
		return new ParkMapThing
		{
			Id = number, Category = item.Entry.WhichUIType, Item = item,
			Relief = settings.GetBool( "UsageInfo.ProvidesRelief" ), Litter = settings.GetBool( "UsageInfo.HoldsLitter" ),
			Security = settings.GetBool( "UsageInfo.ProvidesSecurity" ), StaffRoom = settings.GetBool( "UsageInfo.ChillsYouOut" )
		};
	}

	/// <summary>
	/// A render event of the map (the window opening, a layer or mode button): the values the renderer reads from the objects are taken again and the surface is drawn again. A map that has
	/// not been opened yet has nothing to draw.
	/// </summary>
	internal void RenderMap()
	{
		if ( mapCanvas is not { } canvas )
			return;
		if ( mapThings != null )
			foreach ( var thing in mapThings.Things.Values )
				RefreshMapThing( thing );
		canvas.RenderCells( mapLayers, mapStaff, mapValue );
		if ( (mapLayers & ParkMapClasses.TracksLayer) != 0 )
			RenderMapTracks( canvas );
		canvas.RenderItems( mapLayers, mapStaff, mapValue );
	}

	/// <summary>The track layer: the curve of every coaster, in the colour of its repair.</summary>
	private void RenderMapTracks( ParkMapCanvas canvas )
	{
		foreach ( var ride in level.Coasters )
		{
			var color = ParkMapPalette.UnavailableObject;
			if ( mapThings != null )
				foreach ( var (item, thing) in mapThings.Things )
					if ( ReferenceEquals( item.Runtime.Coaster, ride ) )
					{
						color = thing.Repair;
						break;
					}
			canvas.RenderTrack( Enumerable.Range( 0, ride.CurvePairCount ).Select( ride.CurvePair ), color );
		}
	}

	/// <summary>Reads the values of one object the renderer uses: its open word, its repair, its excitement and its satisfaction.</summary>
	private void RefreshMapThing( ParkMapThing thing )
	{
		var item = thing.Item!;
		// [BIN:TPW-EXE:0x4e0440 open word] the outline style asks whether the open word +0x68 is 0
		thing.Closed = !item.IsOpen;
		thing.Repair = MapRepairFill( item );
		thing.Excitement = MapExcitementByte( item );
		// [BIN:TPW-EXE:0x4e1e30 satisfaction] 50 plus the mean of the object's 30 day history (the same value as the shop and sideshow windows' customer satisfaction gauge)
		thing.Satisfaction = MapObjectState( item )?.SatisfactionRing.Satisfaction;
	}

	/// <summary>The economy record of an object, only through the current level's exact binding of the current economy, read only; null otherwise.</summary>
	private ParkObjectState? MapObjectState( OriginalObject item )
	{
		if ( !item.IsDeleted && !level.IsReadOnlyVisit && Status?.EconomySource is ParkEconomy economy
			&& ReferenceEquals( economy, level.Park?.Economy ) && level.GetEconomyInstance( item ) is int instance
			&& economy.TryGetObject( instance, out var state ) && state.Id == instance && state.InfoId == item.Entry.InfoId )
			return state;
		return null;
	}

	/// <summary>Only the current level's exact object binding can supply a default repair colour.</summary>
	private RgbaByte MapRepairFill( OriginalObject item )
	{
		if ( MapObjectState( item ) is { } state && ParkMapPalette.RepairFill( state.Repair ) is { } fill )
			return fill;
		return ParkMapPalette.UnavailableObject;
	}

	/// <summary>The low byte of <c>0x4E0860(0)</c> for an object: 0 when its type's excitement level (type <c>+0x13C</c>) is 0, else the excitement of the ride or sideshow (<c>0x4E0560</c>); null when the host cannot supply it.</summary>
	// [BIN:TPW-EXE:0x4e0860 excitement] with the argument 0 a type whose +0x13C (UsageInfo.ExcitementLevel) is 0 answers 0, a sideshow (+0x4ac 2) answers 0x4e0560 of its prize, price and win chance, any other type 0x4e0560 of its ride values
	private int? MapExcitementByte( OriginalObject item )
	{
		if ( item.Entry.Settings.GetInt( "UsageInfo.ExcitementLevel" ) == 0 )
			return 0;
		return Status == null ? null : ExcitementPercent( item, null );
	}

	private sealed class ParkMapView : UiElement
	{
		private readonly ParkHud owner;
		public ParkMapView( ParkHud owner ) => this.owner = owner;

		/// <summary>Where the last draw put the image (its rectangle, the surface's top-left in the image's units and the scale of one image unit), for the pointer.</summary>
		public (UiRect Image, NVector2 Uv0, float Scale)? Geometry { get; private set; }

		/// <summary>Adds a textured quad cut to the image's rectangle, its texture coordinates cut with it.</summary>
		private static void AddClipped( UiBatch batch, UiTexture texture, UiRect rect, UiRect image, RgbaByte color )
		{
			var left = Math.Max( image.X, rect.X );
			var top = Math.Max( image.Y, rect.Y );
			var right = Math.Min( image.Right, rect.Right );
			var bottom = Math.Min( image.Bottom, rect.Bottom );
			if ( right <= left || bottom <= top )
				return;
			batch.AddQuad( texture, new UiRect( left, top, right - left, bottom - top ),
				new NVector2( (left - rect.X) / rect.Width, (top - rect.Y) / rect.Height ), new NVector2( (right - rect.X) / rect.Width, (bottom - rect.Y) / rect.Height ), color );
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var content = ScreenRect( context.Canvas );
			var side = Math.Min( content.Height - context.Canvas.Scale * 48f, content.Width - context.Canvas.Scale * 160f );
			var image = new UiRect( content.X + (content.Width - side) / 2, content.Y + (content.Height - side) / 2, side, side );
			var park = owner.level.OriginalPark;
			if ( owner.mapCanvas == null )
			{
				owner.mapCanvas = owner.BuildMapCanvas();
				owner.RenderMap();
			}
			var canvas = owner.mapCanvas;
			var surface = canvas.Surface;
			var (uv0, uv1) = owner.mapView.Source( surface.Width, surface.Height, park != null );
			var imagePath = park == null ? null : $"/levels/{park.LevelName}/2dmap.tga";
			if ( imagePath != null )
				context.Batch.AddQuad( UiTexture.Image( imagePath ), image, uv0, uv1, RgbaByte.White );
			else
				context.Batch.AddRectangle( image, new RgbaByte( 15, 81, 26, 255 ) );

			var scale = owner.mapView.Zoom * side;
			Geometry = (image, uv0, scale);
			void Mark( float x, float y, float width, float height, RgbaByte color )
			{
				var rect = new UiRect( image.X + (x / MapSurface.ImagePixels - uv0.X) * scale, image.Y + (y / MapSurface.ImagePixels - uv0.Y) * scale,
					width / MapSurface.ImagePixels * scale, height / MapSurface.ImagePixels * scale );
				var left = Math.Max( image.X, rect.X );
				var top = Math.Max( image.Y, rect.Y );
				var right = Math.Min( image.Right, rect.Right );
				var bottom = Math.Min( image.Bottom, rect.Bottom );
				if ( right > left && bottom > top )
					context.Batch.AddRectangle( new UiRect( left, top, right - left, bottom - top ), color );
			}
			if ( imagePath == null )
			{
				// [EXT:sandbox-map] the generic sandbox has no shipped theme map image; its live grid and heights form a simple terrain base (OpenTPW content)
				var grid = owner.level.Objects.Grid;
				for ( var y = 0; y < surface.Height; y++ )
					for ( var x = 0; x < surface.Width; x++ )
					{
						var shade = (byte)Math.Clamp( 75 + grid.GetCellHeight( x, y ) * 3 + (x + y) % 2 * 5, 35, 150 );
						Mark( surface.X( x ), surface.Y( y ), surface.CellWidth, surface.CellHeight, new RgbaByte( 20, shade, 30, 255 ) );
					}
			}
			// [BIN:TPW-EXE:0x5f2380 map surface] the cells, outlines, tracks and areas the last render drew into the surface, in the order it drew them
			// [APPROX:UI-086] see the register: the map's dot sizes and its pointer and text behaviour are not all read
			var prims = canvas.Prims;
			for ( var index = 0; index < prims.Count; index++ )
			{
				var prim = prims[index];
				Mark( prim.X, prim.Y, prim.Width, prim.Height, prim.Color );
			}
			// [BIN:TPW-EXE:0x5f4180 map dots] the sprites lie over the surface at the item's cell (15 + x * 480 / width, 42 + y * 425 / height in surface pixels), centred, the kinds above 2 behind the others
			if ( canvas.SpritesAvailable )
				for ( var pass = 0; pass < 2; pass++ )
					for ( var index = 0; index < canvas.Dots.Count; index++ )
					{
						var dot = canvas.Dots[index];
						if ( ParkMapDots.IsBehind( dot ) != (pass == 0) || !ParkMapDots.Visible( dot, owner.mapLayers, owner.mapStaff, owner.mapValue ) )
							continue;
						var size = ParkMapDots.SurfaceSize( dot );
						var (px, py) = surface.Position( dot.X, dot.Y );
						var rect = new UiRect( image.X + ((px - size / 2) / MapSurface.ImagePixels - uv0.X) * scale, image.Y + ((py - size / 2) / MapSurface.ImagePixels - uv0.Y) * scale,
							size / MapSurface.ImagePixels * scale, size / MapSurface.ImagePixels * scale );
						AddClipped( context.Batch, UiTexture.Image( ParkMapDots.Images[dot.Image] ), rect, image, dot.Color );
					}
		}
	}
}

/// <summary>
/// Map view shared by the map buttons and renderer: zoom 1..3 and a centre in map cells (clamped to 0..dimension-1, as in the original);
/// the image window around it is clamped to stay inside the image.
/// </summary>
internal readonly record struct MapViewState( float Zoom, NVector2 Cell )
{
	public const float MinZoom = 1f, MaxZoom = 3f;
	/// <summary>Zoom 1 centred on the middle of the map (NaN cell = middle until the first pan).</summary>
	public static MapViewState Default => new( 1f, new NVector2( float.NaN ) );

	public MapViewState ZoomBy( float step ) => this with { Zoom = Math.Clamp( Zoom + step, MinZoom, MaxZoom ) };

	public MapViewState PanBy( float x, float y, int width, int height )
	{
		var cell = Centre( width, height ) + new NVector2( x, y );
		return this with { Cell = NVector2.Clamp( cell, NVector2.Zero, new NVector2( Math.Max( 0, width - 1 ), Math.Max( 0, height - 1 ) ) ) };
	}

	public NVector2 Centre( int width, int height ) =>
		float.IsNaN( Cell.X ) || float.IsNaN( Cell.Y ) ? new NVector2( (width - 1) / 2f, (height - 1) / 2f ) : Cell;

	/// <summary>The visible window of the 512-pixel image as normalised corners.</summary>
	public (NVector2 Min, NVector2 Max) Source( int width, int height, bool original )
	{
		var centre = Centre( width, height );
		// The centre may be fractional (the default middle of an even-sized map): interpolate between cell centres instead of rounding.
		var cellX = (int)MathF.Floor( centre.X );
		var cellY = (int)MathF.Floor( centre.Y );
		var rect = ParkMapGeometry.CellImageRect( width, height, original, cellX, cellY );
		var half = 0.5f / Zoom;
		var middle = NVector2.Clamp( new NVector2( rect.X + rect.Width * (0.5f + centre.X - cellX), rect.Y + rect.Height * (0.5f + centre.Y - cellY) ) / ParkMapGeometry.ImagePixels,
			new NVector2( half ), new NVector2( 1f - half ) );
		return (middle - new NVector2( half ), middle + new NVector2( half ));
	}
}

/// <summary>The original map's object-free fills and default placed-object repair ramp.</summary>
// [BIN:TPW-EXE:0x5f0700 map colour records] the records 0xf86b00 (31,166,31,255), 0xf86b24 (37,198,37,255) and 0xf86b6c (0,23,0,128) are written by immediates in 0x5f0700-0x5f0964 through the helper 0x5f0970 (blue, green, red, alpha)
internal static class ParkMapPalette
{
	public static readonly RgbaByte Path = new( 31, 166, 31, 255 );
	public static readonly RgbaByte Queue = new( 37, 198, 37, 255 );
	public static readonly RgbaByte Unowned = new( 0, 23, 0, 128 );
	/// <summary>The fence posts' record 0xf86b48 (0, 23, 0, 255), class 9.</summary>
	public static readonly RgbaByte Fence = new( 0, 23, 0, 255 );
	// [BIN:TPW-EXE:0x5f0700 map style records] the outline styles are 0xf86a28 (0, 0, 0, 255) for a closed object and 0xf86a4c (0, 255, 0, 255) for an open one
	public static readonly RgbaByte OutlineClosed = new( 0, 0, 0, 255 );
	public static readonly RgbaByte OutlineOpen = new( 0, 255, 0, 255 );
	// [BIN:TPW-EXE:0x5f0700 dot and area records] the dots are 0xf86a04 (white), 0xf86cb0 (0, 0, 0), 0xf86cd4 (255, 0, 0) and 0xf86cf8 (200, 250, 200); the happiness ramp runs over 0xf86c44 (32, 168, 248), 0xf86c68 (248, 132, 0) and 0xf86c8c (248, 252, 0); a staff member's area is 0xf86b90 (25, 25, 150, alpha 200), or 0xf86bb4 (25, 25, 250, alpha 200) while it is the item under the pointer
	public static readonly RgbaByte DotWhite = new( 255, 255, 255, 255 );
	public static readonly RgbaByte DotBlack = new( 0, 0, 0, 255 );
	public static readonly RgbaByte DotRed = new( 255, 0, 0, 255 );
	public static readonly RgbaByte DotPaleGreen = new( 200, 250, 200, 255 );
	public static readonly RgbaByte Zone = new( 25, 25, 150, 200 );
	public static readonly RgbaByte ZoneHovered = new( 25, 25, 250, 200 );

	/// <summary>The three-record ramp of <c>0x5F09A0</c>: the value clamps to 0..100, below 50 it runs from the first record to the second, from 50 on to the third, in 16.16 steps with arithmetic channel shifts.</summary>
	public static RgbaByte Ramp( int value, RgbaByte first, RgbaByte second, RgbaByte third )
	{
		value = Math.Clamp( value, 0, 100 );
		var upper = value >= 50;
		var fraction = ((upper ? value - 50 : value) << 16) / 50;
		var (from, to) = upper ? (second, third) : (first, second);
		static byte Channel( byte from, byte to, int fraction ) => (byte)(from + (((to - from) * fraction) >> 16));
		return new RgbaByte( Channel( from.R, to.R, fraction ), Channel( from.G, to.G, fraction ), Channel( from.B, to.B, fraction ), 255 );
	}

	/// <summary>The happiness dots' ramp over 0xf86c44, 0xf86c68 and 0xf86c8c.</summary>
	public static RgbaByte DotRamp( int value ) => Ramp( value, new RgbaByte( 32, 168, 248, 255 ), new RgbaByte( 248, 132, 0, 255 ), new RgbaByte( 248, 252, 0, 255 ) );

	/// <summary>The low byte of a float's truncation (<c>__ftol</c>, then <c>and 0xff</c>); null for a value the host cannot truncate to 32 bits.</summary>
	public static int? RampByte( float value )
	{
		if ( !float.IsFinite( value ) || (double)value < int.MinValue || (double)value > int.MaxValue )
			return null;
		return (int)value & 255;
	}

	/// <summary>Existing host fill when no supported current repair state is available.</summary>
	public static readonly RgbaByte UnavailableObject = new( 207, 67, 198, 235 );

	/// <summary>The original stored float, truncated to an integer and masked to its low byte before the ramp clamp.</summary>
	// [BIN:TPW-EXE:0x5f2602 default map repair] object +0x44 is SaveAttraction.StateOfRepair (OriginalEconomyImport -> Repair); the draw calls __ftol then AND 255 before 0x5f09a0. Other default object classes repeat this sequence.
	public static RgbaByte? RepairFill( double repair )
	{
		var stored = (float)repair;
		// Host malformed-input fallback, not an emulation of undefined x87 conversions. Widen for the bounds
		// comparison: int.MaxValue rounds UP to 2147483648 as a float, which must not reach the integer cast.
		if ( !float.IsFinite( stored ) || (double)stored < int.MinValue || (double)stored > int.MaxValue )
			return null;
		return RepairRamp( (int)stored & 255 );
	}

	/// <summary>Clamped integer map ramp, with a quantized 16.16 fraction and arithmetic channel shifts.</summary>
	// [BIN:TPW-EXE:0x5f09a0 map gradient] constructor 0x5f08a5..0x5f08df supplies RGB (248,0,0), (248,200,0), (48,252,0); clamp 0..100, split at 50, fraction=(distance<<16)/50, signed channel products >>16, alpha 255.
	public static RgbaByte RepairRamp( int value )
	{
		value = Math.Clamp( value, 0, 100 );
		var upper = value >= 50;
		var fraction = ((upper ? value - 50 : value) << 16) / 50;
		return upper
			? new RgbaByte( (byte)(248 + ((-200 * fraction) >> 16)), (byte)(200 + ((52 * fraction) >> 16)), 0, 255 )
			: new RgbaByte( 248, (byte)((200 * fraction) >> 16), 0, 255 );
	}

	/// <summary>The fill of an object-free cell: a path, else a queue, else unowned land; null leaves the map art.</summary>
	public static RgbaByte? CellFill( ParkCellType type, bool unowned ) => type switch
	{
		ParkCellType.Path => Path,
		ParkCellType.Queue => Queue,
		_ => unowned ? Unowned : null
	};
}

internal static class ParkMapGeometry
{
	public const float ImagePixels = 512f;
	private const float OriginalCellPixels = 5f;

	/// <summary>
	/// The pixels of the map image that a live overlay cell covers. On a theme map these are the original's: cells of trunc(480 / width) by
	/// trunc(425 / height) pixels from (15, 42), which sits one pixel left of and above the shipped art's terrain (16, 43); the sandbox fills the image.
	/// </summary>
	// [BIN:TPW-EXE:0x5f2380 map overlay] the map draws its cells into the surface holding the level's 2dmap.tga (0x5f1ea0) at x = 15 + column × trunc(480.0 / width)
	// and y = 42 + row × trunc(425.0 / height) (0x5f23dc-0x5f2400, the floats 0x703044 and 0x703048; start values 0xf and 0x2a), each cell's rectangle ending where the
	// next begins (its outline's far edges are drawn at start + size - 1), and a placed object's footprint over the same grid (0x5f2ea9-0x5f2ecd: columns (index & 0x7f) and rows (index >> 7))
	public static UiRect OverlayCellRect( int width, int height, bool original, int x, int y )
	{
		if ( !original || width <= 0 || height <= 0 )
			return CellImageRect( width, height, original, x, y );
		var cellWidth = (int)(480f / width);
		var cellHeight = (int)(425f / height);
		return new UiRect( 15 + x * cellWidth, 42 + y * cellHeight, cellWidth, cellHeight );
	}

	public static UiRect CellImageRect( int width, int height, bool original, int x, int y )
	{
		var cellWidth = original ? OriginalCellPixels : ImagePixels / width;
		var cellHeight = original ? OriginalCellPixels : ImagePixels / height;
		return new UiRect( (ImagePixels - width * cellWidth) / 2 + x * cellWidth,
			(ImagePixels - height * cellHeight) / 2 + y * cellHeight, cellWidth, cellHeight );
	}
}

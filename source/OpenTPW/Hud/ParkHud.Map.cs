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
	private readonly HashSet<BuildCategory> mapCategories = new( InitialMapCategories );
	private readonly HashSet<StaffType> mapStaff = new();
	public bool MapStaffRole( StaffType type ) => mapStaff.Contains( type );
	public void ToggleMapStaff( StaffType type ) { if ( !mapStaff.Remove( type ) ) mapStaff.Add( type ); }
	private bool mapTracks;
	public bool MapTracks => mapTracks;
	public void ToggleMapTracks() => mapTracks = !mapTracks;
	private bool mapVisitors;
	private bool mapHappiness;
	private bool mapSatisfaction;
	private bool mapExcitement;
	public bool MapSatisfaction => mapSatisfaction;
	public bool MapExcitement => mapExcitement;
	public void ToggleMapSatisfaction() => mapSatisfaction = !mapSatisfaction;
	public void ToggleMapExcitement() => mapExcitement = !mapExcitement;
	public bool MapVisitors => mapVisitors;
	public bool MapHappiness => mapHappiness;
	public void ToggleMapVisitors() => mapVisitors = !mapVisitors;
	public void ToggleMapHappiness() => mapHappiness = !mapHappiness;

	public bool MapWindowOpen => mapScreen != null && Stack.Screens.Contains( mapScreen );
	public float MapZoom => mapView.Zoom;
	public bool MapCategoryShown( BuildCategory category ) => mapCategories.Contains( category );
	public void ToggleMapCategory( BuildCategory category ) { if ( !mapCategories.Remove( category ) ) mapCategories.Add( category ); }
	/// <summary>Whether placed objects of a build category are drawn: only those whose own layer is on.</summary>
	public bool MapShowsCategory( int whichUiType ) => mapCategories.Contains( (BuildCategory)whichUiType );

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
		// [APPROX:UI-103] the original's map view also scrolls on every drawn frame while the pointer is in the outer 1 per cent of the canvas, by (pointer - 0.99) x 250 or (0.01 - pointer) x 250 map units (0x5f1130, message 0x1e); not built
		// [BIN:TPW-EXE:0x5f35b0 map pan] the pan buttons and arrow keys move the view centre by one map unit (cell) whatever the zoom,
		// clamped to 0..dimension-1.
		var (width, height) = MapDimensions();
		mapView = mapView.PanBy( x, y, width, height );
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
		// [PROVISIONAL:MAP] Layer toggles render available live geometry and observations; an empty collection leaves the base map visible.
		MetricLayer( "tracks", "b_strack", 275, Rect( 542, 1409, 644, 1512 ), ToggleMapTracks, () => mapTracks );
		Layer( "visitors", "b_svisit", 277, Rect( 1390, 1409, 1493, 1512 ), ToggleMapVisitors, () => mapVisitors );
		StaffLayer( "handymen", "b_shandy", 279, Rect( 1883, 443, 1985, 546 ), StaffType.Handyman );
		StaffLayer( "mechanics", "b_smech", 281, Rect( 1883, 592, 1985, 694 ), StaffType.Mechanic );
		StaffLayer( "entertainers", "b_senter", 285, Rect( 1883, 740, 1985, 842 ), StaffType.Entertainer );
		StaffLayer( "guards", "b_sguard", 283, Rect( 1883, 885, 1985, 987 ), StaffType.Guard );
		MetricLayer( "satisfaction", "b_ssatis", 291, Rect( 1526, 1409, 1629, 1512 ), ToggleMapSatisfaction, () => mapSatisfaction );
		Layer( "happiness", "b_shap", 287, Rect( 1646, 1409, 1748, 1512 ), ToggleMapHappiness, () => mapHappiness );
		MetricLayer( "excitement", "b_sexcit", 289, Rect( 1766, 1409, 1868, 1512 ), ToggleMapExcitement, () => mapExcitement );
		// [DATA:Mac map table 0x5265e:-1] b_okay closes the map (help 1).
		Button( "close", "b_okay", 1, Rect( 1941, 1339, 2024, 1422 ), CloseMapWindow );
		// [PROVISIONAL:MAP] Host metric legend qualifies observed thoughts and attraction ratings; original legend handler is untraced.
		screen.Add( new UiLabel { Id = "metricLegend", Text = MapMetricLegend, Font = fonts => fonts.Label, Fit = true,
			Bounds = Rect( 700, 1400, 1340, 1520 ), Anchor = UiAnchor.Center } );
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

	private string MapMetricLegend()
	{
		var text = new List<string>();
		if ( mapSatisfaction )
		{
			var totals = ParkMapMetrics.OutcomeTotals( level.Guests?.Guests ?? Array.Empty<Guest>(), level.Objects.Grid.Width, level.Objects.Grid.Height );
			text.Add( strings.Extra( OpenTpwText.MapRecordedVisitorReactions ) + "\n" + (totals.Pleased == 0 && totals.Dissatisfied == 0 ? strings.Extra( OpenTpwText.MapNoObservations ) :
				string.Format( strings.Extra( OpenTpwText.MapReactionLegend ), totals.Pleased, totals.Dissatisfied )) );
		}
		if ( mapExcitement ) text.Add( strings.Extra( OpenTpwText.MapAttractionExcitement ) + "\n" +
			strings.Extra( level.Guests?.Attractions.Count > 0 ? OpenTpwText.MapExcitementLegend : OpenTpwText.MapNoObservations ) );
		return string.Join( "\n", text );
	}

	private sealed class MapScreen( ParkHud owner ) : UiScreen( "map" )
	{
		public override bool Update( UiContext context, UiInput input )
		{
			foreach ( var id in new[] { "visitors", "happiness" } )
				if ( Find( id ) is UiButton button ) button.Enabled = owner.level.Guests != null;
			foreach ( var id in new[] { "handymen", "mechanics", "entertainers", "guards" } )
				if ( Find( id ) is UiButton button ) button.Enabled = owner.level.StaffWorld != null;
			return base.Update( context, input );
		}
	}

	private sealed class ParkMapView : UiElement
	{
		private readonly ParkHud owner;
		private readonly ParkMapMetrics.OutcomeScratch outcomeCells = new();
		public ParkMapView( ParkHud owner ) => this.owner = owner;

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var content = ScreenRect( context.Canvas );
			var side = Math.Min( content.Height - context.Canvas.Scale * 48f, content.Width - context.Canvas.Scale * 160f );
			var image = new UiRect( content.X + (content.Width - side) / 2, content.Y + (content.Height - side) / 2, side, side );
			var park = owner.level.OriginalPark;
			var cells = owner.level.Paths?.Cells;
			var grid = owner.level.Objects.Grid;
			var width = Math.Min( grid.Width, park?.Heightfield.CellCountX ?? grid.Width );
			var height = Math.Min( grid.Height, park?.Heightfield.CellCountZ ?? grid.Height );
			var (uv0, uv1) = owner.mapView.Source( width, height, park != null );
			var imagePath = park == null ? null : $"/levels/{park.LevelName}/2dmap.tga";
			if ( imagePath != null )
				context.Batch.AddQuad( UiTexture.Image( imagePath ), image, uv0, uv1, RgbaByte.White );
			else
				context.Batch.AddRectangle( image, new RgbaByte( 15, 81, 26, 255 ) );

			UiRect CellRect( int x, int y )
			{
				var cell = ParkMapGeometry.OverlayCellRect( width, height, park != null, x, y );
				var left = cell.X / ParkMapGeometry.ImagePixels;
				var top = cell.Y / ParkMapGeometry.ImagePixels;
				var scale = owner.mapView.Zoom * side;
				return new UiRect( image.X + (left - uv0.X) * scale, image.Y + (top - uv0.Y) * scale,
					cell.Width / ParkMapGeometry.ImagePixels * scale, cell.Height / ParkMapGeometry.ImagePixels * scale );
			}
			void Mark( int x, int y, RgbaByte color )
			{
				var rect = CellRect( x, y );
				var left = Math.Max( image.X, rect.X );
				var top = Math.Max( image.Y, rect.Y );
				var right = Math.Min( image.Right, rect.Right );
				var bottom = Math.Min( image.Bottom, rect.Bottom );
				if ( right > left && bottom > top )
					context.Batch.AddRectangle( new UiRect( left, top, right - left, bottom - top ), color );
			}
			// [BIN:TPW-EXE:0x5f2050 map cell classes] the writer gives a cell without an object the class 10 (path, 0x536310), 11 (queue, 0x536320) or 12 (unowned land, flag 0x40) in that order, filled with the records 0xf86b00 (31,166,31), 0xf86b24 (37,198,37) and 0xf86b6c (0,23,0 at alpha 128) by 0x5f2380
			// [APPROX:UI-086] placed-object cells and footprints use one plain colour over the original map art (and the staff, visitor, track and happiness layers theirs); the original gives an object cell the class of its WhichUIType (1 rides, 2 shops, 3 sideshows, 4-7 features, 8 track cells), colours it from the 0xf86bd8/0xf86bfc/0xf86c20 ramp (red 0, orange 50, green 100, 0x5f09a0) of a value (+0x44 of its object record, not identified) and outlines it where a neighbour differs (0x5f2380); its footprints (kind 2 items, 0xf86b90 / 0xf86bb4) and the layer buttons' land and value modes (2444-2447, 2455-2457) are not ported
			if ( imagePath == null )
			{
				// [EXT:sandbox-map] the generic sandbox has no shipped theme map image; its live grid and heights form a simple terrain base (OpenTPW content)
				for ( var y = 0; y < height; y++ )
					for ( var x = 0; x < width; x++ )
					{
						var shade = (byte)Math.Clamp( 75 + grid.GetCellHeight( x, y ) * 3 + (x + y) % 2 * 5, 35, 150 );
						Mark( x, y, new RgbaByte( 20, shade, 30, 255 ) );
					}
			}
			if ( cells != null )
				for ( var y = 0; y < Math.Min( cells.Height, height ); y++ )
					for ( var x = 0; x < Math.Min( cells.Width, width ); x++ )
						if ( ParkMapPalette.CellFill( cells.TypeAt( x, y ), cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) ) is { } fill )
							Mark( x, y, fill );
			foreach ( var item in owner.level.Objects.Objects.Where( item => !item.IsDeleted &&
				owner.MapShowsCategory( item.Entry.WhichUIType ) ) )
				foreach ( var cell in item.Cells )
					Mark( cell.X, cell.Y, new RgbaByte( 207, 67, 198, 235 ) );
			// [PROVISIONAL:MAP] Host overlay projects actual COAST samples (original x/z ground plane) onto map cells; unfinished tracks show the authored node chain.
			if ( owner.mapTracks )
				foreach ( var coaster in owner.level.Coasters )
					foreach ( var cell in coaster.Samples.Count > 0
						? ParkMapTracks.SampleCells( coaster.Samples.Select( sample => sample.Position ), width, height )
						: ParkMapTracks.NodeCells( coaster.Track.Nodes.Select( node => (node.X, node.Y) ), width, height ) )
						Mark( cell.X, cell.Y, new RgbaByte( 65, 220, 235, 240 ) );
			// [PROVISIONAL:MAP] Attraction ratings use registered bridge data, never visitor preference or a synthetic scalar.
			if ( owner.mapExcitement )
				foreach ( var item in owner.level.Objects.Objects.Where( item => !item.IsDeleted && owner.level.Guests?.Attractions.Contains( item.Visitors ) == true ) )
					foreach ( var cell in item.Cells ) Mark( cell.X, cell.Y, ParkMapMetrics.ExcitementColor( item.Visitors.ExcitementLevel ) );
			if ( owner.mapExcitement && owner.level.PlacedRide is { } ride && owner.level.OriginalPark is { } original && owner.level.Guests?.Attractions.Contains( ride.Visitors ) == true &&
				OriginalParkPlacement.TryGetCell( original.Heightfield, ride.Position.X, ride.Position.Y, out var rideX, out var rideY ) )
				Mark( rideX, rideY, ParkMapMetrics.ExcitementColor( ride.Visitors.ExcitementLevel ) );
			foreach ( var actor in owner.level.StaffWorld?.Snapshots ?? Array.Empty<StaffWorldSnapshot>() )
				if ( owner.mapStaff.Contains( actor.Type ) && float.IsFinite( actor.CellX ) && float.IsFinite( actor.CellY ) && actor.CellX >= 0 && actor.CellY >= 0 && actor.CellX < width && actor.CellY < height )
					Mark( (int)MathF.Floor( actor.CellX ), (int)MathF.Floor( actor.CellY ), new RgbaByte( 255, 255, 255, 245 ) );
			// [PROVISIONAL:MAP] Live visitor cells and mean happiness use real simulation values; original overlay colouring is untraced.
			if ( owner.mapVisitors || owner.mapHappiness )
				foreach ( var cell in ParkMapGuestLayers.Cells( owner.level.Guests?.Guests ?? Array.Empty<Guest>(), width, height ) )
					Mark( cell.X, cell.Y, owner.mapHappiness ? ParkMapGuestLayers.HappinessColor( cell.Happiness ) : new RgbaByte( 75, 180, 255, 240 ) );
			// [PROVISIONAL:MAP] A thought outcome is an observation proxy, not the original game's satisfaction score.
			var outcomes = outcomeCells.Rebuild( owner.level.Guests?.Guests ?? Array.Empty<Guest>(), width, height, owner.mapSatisfaction );
			for ( var index = 0; index < outcomes.Count; index++ )
			{
				var cell = outcomes[index];
				Mark( cell.X, cell.Y, cell.Pleased > 0 && cell.Dissatisfied > 0 ? new RgbaByte( 255, 165, 35, 240 ) : cell.Pleased > 0 ? new RgbaByte( 35, 235, 35, 240 ) : new RgbaByte( 235, 35, 35, 240 ) );
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

	public MapViewState PanBy( int x, int y, int width, int height )
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

/// <summary>Projects live park cells onto the shipped 512-pixel map image or a sandbox grid.</summary>
/// <summary>The fills of the original map's cell classes 10 to 12.</summary>
// [BIN:TPW-EXE:0x5f0700 map colour records] the records 0xf86b00 (31,166,31,255), 0xf86b24 (37,198,37,255) and 0xf86b6c (0,23,0,128) are written by immediates in 0x5f0700-0x5f0964 through the helper 0x5f0970 (blue, green, red, alpha)
internal static class ParkMapPalette
{
	public static readonly RgbaByte Path = new( 31, 166, 31, 255 );
	public static readonly RgbaByte Queue = new( 37, 198, 37, 255 );
	public static readonly RgbaByte Unowned = new( 0, 23, 0, 128 );

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

internal static class ParkMapGuestLayers
{
	public static IEnumerable<(int X, int Y, float Happiness)> Cells( IEnumerable<Guest> guests, int width, int height ) =>
		guests.Where( guest => guest.IsVisible && float.IsFinite( guest.X ) && float.IsFinite( guest.Y ) && guest.X >= 0 && guest.Y >= 0 && guest.X < width && guest.Y < height )
			.GroupBy( guest => ((int)MathF.Floor( guest.X ), (int)MathF.Floor( guest.Y )) )
			.Select( group => (group.Key.Item1, group.Key.Item2, group.Average( guest => guest.Happiness )) );
	public static RgbaByte HappinessColor( float happiness ) => new( (byte)(255 * (1 - Math.Clamp( happiness, 0, 100 ) / 100)), (byte)(255 * Math.Clamp( happiness, 0, 100 ) / 100), 35, 235 );
}

internal static class ParkMapMetrics
{
	public static IEnumerable<(int X, int Y, int Pleased, int Dissatisfied)> Outcomes( IEnumerable<Guest> guests, int width, int height ) =>
		guests.Where( guest => IsObservedOutcome( guest, width, height ) )
			.GroupBy( guest => ((int)MathF.Floor( guest.X ), (int)MathF.Floor( guest.Y )) )
			.Select( group => (group.Key.Item1, group.Key.Item2, group.Count( guest => guest.Thought == GuestThought.Pleased ), group.Count( guest => guest.Thought == GuestThought.Dissatisfied )) );

	/// <summary>View-owned counts rebuilt each draw, in the first encounter order of the live guests.</summary>
	internal sealed class OutcomeScratch
	{
		private readonly Dictionary<(int X, int Y), int> indexes = new();
		private readonly List<(int X, int Y, int Pleased, int Dissatisfied)> cells = new();

		public IReadOnlyList<(int X, int Y, int Pleased, int Dissatisfied)> Rebuild( IReadOnlyList<Guest> guests, int width, int height, bool enabled )
		{
			indexes.Clear();
			cells.Clear();
			if ( !enabled ) return cells;
			for ( var index = 0; index < guests.Count; index++ )
			{
				var guest = guests[index];
				if ( !IsObservedOutcome( guest, width, height ) ) continue;
				var key = ((int)MathF.Floor( guest.X ), (int)MathF.Floor( guest.Y ));
				if ( !indexes.TryGetValue( key, out var cellIndex ) )
				{
					cellIndex = cells.Count;
					indexes.Add( key, cellIndex );
					cells.Add( (key.Item1, key.Item2, 0, 0) );
				}
				var cell = cells[cellIndex];
				if ( guest.Thought == GuestThought.Pleased ) cell.Pleased = checked(cell.Pleased + 1);
				else cell.Dissatisfied = checked(cell.Dissatisfied + 1);
				cells[cellIndex] = cell;
			}
			return cells;
		}
	}

	/// <summary>Fresh legend totals; no cell grouping is needed when positions are not drawn.</summary>
	public static (int Pleased, int Dissatisfied) OutcomeTotals( IEnumerable<Guest> guests, int width, int height )
	{
		var pleased = 0;
		var dissatisfied = 0;
		foreach ( var guest in guests )
		{
			if ( !IsObservedOutcome( guest, width, height ) ) continue;
			if ( guest.Thought == GuestThought.Pleased ) pleased = checked(pleased + 1);
			else dissatisfied = checked(dissatisfied + 1);
		}
		return (pleased, dissatisfied);
	}

	private static bool IsObservedOutcome( Guest guest, int width, int height ) => guest.IsVisible
		&& (guest.Thought is GuestThought.Pleased or GuestThought.Dissatisfied)
		&& float.IsFinite( guest.X ) && float.IsFinite( guest.Y ) && guest.X >= 0 && guest.Y >= 0 && guest.X < width && guest.Y < height;

	public static RgbaByte ExcitementColor( int rating ) => ParkMapGuestLayers.HappinessColor( rating );
}

internal static class ParkMapTracks
{
	public static IEnumerable<(int X, int Y)> SampleCells( IEnumerable<System.Numerics.Vector3> samples, int width, int height ) =>
		samples.Where( sample => float.IsFinite( sample.X ) && float.IsFinite( sample.Z ) && sample.X >= 0 && sample.Z >= 0 && sample.X < width * CoasterTrack.CellUnits && sample.Z < height * CoasterTrack.CellUnits )
			.Select( sample => ((int)MathF.Floor( sample.X / CoasterTrack.CellUnits ), (int)MathF.Floor( sample.Z / CoasterTrack.CellUnits )) ).Distinct();

	public static IEnumerable<(int X, int Y)> NodeCells( IEnumerable<(int X, int Y)> nodes, int width, int height )
	{
		var result = new HashSet<(int X, int Y)>();
		(int X, int Y)? previous = null;
		foreach ( var node in nodes )
		{
			if ( node.X < 0 || node.Y < 0 || node.X >= width || node.Y >= height ) { previous = null; continue; }
			result.Add( node );
			if ( previous is { } from )
			{
				var steps = Math.Max( Math.Abs( node.X - from.X ), Math.Abs( node.Y - from.Y ) );
				for ( var step = 1; step < steps; step++ )
					result.Add( ((int)MathF.Round( from.X + (node.X - from.X) * (step / (float)steps) ), (int)MathF.Round( from.Y + (node.Y - from.Y) * (step / (float)steps) )) );
			}
			previous = node;
		}
		return result;
	}
}

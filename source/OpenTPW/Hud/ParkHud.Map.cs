using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>The original map window (Mac table <c>data:0x5265e</c>) with live paths and objects over the shipped theme map.</summary>
public sealed partial class ParkHud
{
	private const float MapZoomStep = 0.5f;
	private UiScreen? mapScreen;
	private MapViewState mapView = MapViewState.Default;
	private BuildCategory? mapCategory;
	private readonly HashSet<StaffType> mapStaff = new();
	public bool MapStaffRole( StaffType type ) => mapStaff.Contains( type );
	public void ToggleMapStaff( StaffType type ) { if ( !mapStaff.Remove( type ) ) mapStaff.Add( type ); }
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
	public BuildCategory? MapCategory => mapCategory;

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
	}

	public void CloseMapWindow()
	{
		if ( mapScreen == null || !MapWindowOpen )
			return;
		while ( Stack.Top != mapScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == mapScreen )
			Stack.Pop();
	}

	public void ZoomMap( int direction )
	{
		// [APPROX:UI-087] the table has zoom buttons but no decoded zoom factor; half steps keep the visible area usable.
		mapView = mapView.ZoomBy( direction * MapZoomStep );
	}

	public void PanMap( int x, int y )
	{
		// [APPROX:UI-087] one map pan moves 1/8 of the currently visible image.
		mapView = mapView.PanBy( x, y );
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
		// [APPROX:UI-086] clicking a category filters placed-object footprints to that type; clicking it again restores all types.
		CategoryButton( "rides", "b_sride", BuildCategory.Rides, 267, Rect( 64, 1409, 167, 1512 ) );
		CategoryButton( "sideshows", "b_sshow", BuildCategory.Sideshows, 271, Rect( 184, 1409, 286, 1512 ) );
		CategoryButton( "shops", "b_sshop", BuildCategory.Shops, 269, Rect( 303, 1409, 406, 1512 ) );
		CategoryButton( "features", "b_sfeature", BuildCategory.Features, 273, Rect( 423, 1409, 525, 1512 ) );
		// [DATA:Mac map table 0x5265e:2441,2435,2444-2447,2455-2457] track, actor and metric controls.
		// [APPROX:UI-088] controls for unavailable live layers remain visible but disabled.
		Disabled( "tracks", "b_strack", 275, Rect( 542, 1409, 644, 1512 ) );
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
			Clicked = () => mapCategory = mapCategory == category ? null : category, Selected = () => mapCategory == category
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
		void Disabled( string id, string model, int help, UiRect rect ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Bounds = rect, Anchor = UiAnchor.Center, Enabled = false
		} );
	}

	private string MapMetricLegend()
	{
		var text = new List<string>();
		if ( mapSatisfaction )
		{
			var outcomes = ParkMapMetrics.Outcomes( level.Guests?.Guests ?? Array.Empty<Guest>(), level.Objects.Grid.Width, level.Objects.Grid.Height ).ToArray();
			text.Add( strings.Extra( OpenTpwText.MapRecordedVisitorReactions ) + "\n" + (outcomes.Length == 0 ? strings.Extra( OpenTpwText.MapNoObservations ) :
				string.Format( strings.Extra( OpenTpwText.MapReactionLegend ), outcomes.Sum( cell => cell.Pleased ), outcomes.Sum( cell => cell.Dissatisfied ) )) );
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
		public ParkMapView( ParkHud owner ) => this.owner = owner;

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var content = ScreenRect( context.Canvas );
			var side = Math.Min( content.Height - context.Canvas.Scale * 48f, content.Width - context.Canvas.Scale * 160f );
			var image = new UiRect( content.X + (content.Width - side) / 2, content.Y + (content.Height - side) / 2, side, side );
			var uv0 = owner.mapView.SourceMin;
			var uv1 = owner.mapView.SourceMax;
			var park = owner.level.OriginalPark;
			var imagePath = park == null ? null : $"/levels/{park.LevelName}/2dmap.tga";
			if ( imagePath != null )
				context.Batch.AddQuad( UiTexture.Image( imagePath ), image, uv0, uv1, RgbaByte.White );
			else
				context.Batch.AddRectangle( image, new RgbaByte( 15, 81, 26, 255 ) );

			var cells = owner.level.Paths?.Cells;
			var grid = owner.level.Objects.Grid;
			var width = Math.Min( grid.Width, park?.Heightfield.CellCountX ?? grid.Width );
			var height = Math.Min( grid.Height, park?.Heightfield.CellCountZ ?? grid.Height );
			// [APPROX:UI-085] the shipped 512px theme image places 5px terrain cells centrally; compare with original map overlays.
			UiRect CellRect( int x, int y )
			{
				var cell = ParkMapGeometry.CellImageRect( width, height, park != null, x, y );
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
			// [APPROX:UI-086] live paths/queues and placed-object footprints use plain coloured cells over the original map art.
			if ( imagePath == null )
			{
				// [APPROX:UI-089] the generic sandbox has no 2dmap.tga; use its live grid and height as a simple terrain base.
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
						switch ( cells.TypeAt( x, y ) )
					{
							case ParkCellType.Path: Mark( x, y, new RgbaByte( 242, 218, 90, 220 ) ); break;
							case ParkCellType.Queue: Mark( x, y, new RgbaByte( 235, 139, 55, 220 ) ); break;
					}
			foreach ( var item in owner.level.Objects.Objects.Where( item => !item.IsDeleted &&
				(owner.mapCategory == null || item.Entry.WhichUIType == (int)owner.mapCategory) ) )
				foreach ( var cell in item.Cells )
					Mark( cell.X, cell.Y, new RgbaByte( 207, 67, 198, 235 ) );
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
			if ( owner.mapSatisfaction )
				foreach ( var cell in ParkMapMetrics.Outcomes( owner.level.Guests?.Guests ?? Array.Empty<Guest>(), width, height ) )
					Mark( cell.X, cell.Y, cell.Pleased > 0 && cell.Dissatisfied > 0 ? new RgbaByte( 255, 165, 35, 240 ) : cell.Pleased > 0 ? new RgbaByte( 35, 235, 35, 240 ) : new RgbaByte( 235, 35, 35, 240 ) );
		}
	}
}

/// <summary>Clamped source-image view shared by the map buttons and map renderer.</summary>
internal readonly record struct MapViewState( float Zoom, NVector2 Center )
{
	public static MapViewState Default => new( 1f, new NVector2( 0.5f ) );
	public NVector2 SourceMin => Center - new NVector2( 0.5f / Zoom );
	public NVector2 SourceMax => Center + new NVector2( 0.5f / Zoom );

	public MapViewState ZoomBy( float step ) => Clamped( Math.Clamp( Zoom + step, 1f, 4f ), Center );
	public MapViewState PanBy( int x, int y ) => Clamped( Zoom, Center + new NVector2( x, y ) / (8f * Zoom) );

	private static MapViewState Clamped( float zoom, NVector2 center )
	{
		var half = 0.5f / zoom;
		return new MapViewState( zoom, NVector2.Clamp( center, new NVector2( half ), new NVector2( 1f - half ) ) );
	}
}

/// <summary>Projects live park cells onto the shipped 512-pixel map image or a sandbox grid.</summary>
internal static class ParkMapGeometry
{
	public const float ImagePixels = 512f;
	private const float OriginalCellPixels = 5f;

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
		guests.Where( guest => guest.IsVisible && guest.Thought is GuestThought.Pleased or GuestThought.Dissatisfied &&
			float.IsFinite( guest.X ) && float.IsFinite( guest.Y ) && guest.X >= 0 && guest.Y >= 0 && guest.X < width && guest.Y < height )
			.GroupBy( guest => ((int)MathF.Floor( guest.X ), (int)MathF.Floor( guest.Y )) )
			.Select( group => (group.Key.Item1, group.Key.Item2, group.Count( guest => guest.Thought == GuestThought.Pleased ), group.Count( guest => guest.Thought == GuestThought.Dissatisfied )) );
	public static RgbaByte ExcitementColor( int rating ) => ParkMapGuestLayers.HappinessColor( rating );
}

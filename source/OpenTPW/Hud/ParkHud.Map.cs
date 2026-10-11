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
		var screen = new UiScreen( "map" );
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
		// [DATA:Mac map table 0x5265e:2441,2435,2444-2447,2455-2457] these layers have no per-cell location or metric data yet.
		// [APPROX:UI-088] controls for unavailable live layers remain visible but disabled.
		Disabled( "tracks", "b_strack", 275, Rect( 542, 1409, 644, 1512 ) );
		Disabled( "visitors", "b_svisit", 277, Rect( 1390, 1409, 1493, 1512 ) );
		Disabled( "handymen", "b_shandy", 279, Rect( 1883, 443, 1985, 546 ) );
		Disabled( "mechanics", "b_smech", 281, Rect( 1883, 592, 1985, 694 ) );
		Disabled( "entertainers", "b_senter", 285, Rect( 1883, 740, 1985, 842 ) );
		Disabled( "guards", "b_sguard", 283, Rect( 1883, 885, 1985, 987 ) );
		Disabled( "satisfaction", "b_ssatis", 291, Rect( 1526, 1409, 1629, 1512 ) );
		Disabled( "happiness", "b_shap", 287, Rect( 1646, 1409, 1748, 1512 ) );
		Disabled( "excitement", "b_sexcit", 289, Rect( 1766, 1409, 1868, 1512 ) );
		// [DATA:Mac map table 0x5265e:-1] b_okay closes the map (help 1).
		Button( "close", "b_okay", 1, Rect( 1941, 1339, 2024, 1422 ), CloseMapWindow );
		screen.Back = CloseMapWindow;
		return screen;

		void CategoryButton( string id, string model, BuildCategory category, int help, UiRect rect ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Bounds = rect, Anchor = UiAnchor.Center,
			Clicked = () => mapCategory = mapCategory == category ? null : category, Selected = () => mapCategory == category
		} );
		void Disabled( string id, string model, int help, UiRect rect ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Bounds = rect, Anchor = UiAnchor.Center, Enabled = false
		} );
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

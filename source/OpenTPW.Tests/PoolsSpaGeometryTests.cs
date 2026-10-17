using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The geometry of the spa pool (docs/POOLS.md, "Spa pool"): a seated guest is a camera-facing sprite (slot 12, the torso at the water line) in the tub's basin, so the sprite must fit between the basin's
/// walls and sit at the water surface of the shipped model. Everything is measured from the committed files (the four <c>content/pools/objects/&lt;theme&gt;/spa/spa.MD2</c> and the swim sprite sets), not from numbers
/// restated here. Units: a model unit is 0.2 engine units, a cell is 10 model units (2 engine units).
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolsSpaGeometryTests
{
	private const float ModelUnit = 0.2f;
	private const float CellModelUnits = 10f;
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };

	private static string RepositoryFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			if ( File.Exists( Path.Combine( directory.FullName, "content", "pools", "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "pools" ) ) )
				return directory.FullName;
		throw new FileNotFoundException( "content/pools not found above the test binaries." );
	}

	/// <summary>The measured tub, in model units: its middle, the circumradius and the flat-side distance of the basin, the water surface, the rim and the floor.</summary>
	private readonly record struct Tub( System.Numerics.Vector2 Middle, float Circumradius, float Apothem, float Water, float Rim, float Floor );

	private static Tub Measure( string root, string theme, string kind = "spa" )
	{
		var folder = Path.Combine( root, "content", "pools", "objects", theme, kind );
		var model = ObjectAssets.LoadModel( new BaseFileSystem( folder ), kind + ".MD2" );
		Assert.AreEqual( 1, model.Meshes.Count, theme );
		var mesh = model.Meshes[0];
		var world = System.Numerics.Matrix4x4.Identity;
		for ( var index = mesh.NodeIndex; index >= 0; index = model.Nodes[index].ParentIndex )
			world *= model.Nodes[index].Transform;
		var points = mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), world ) ).ToArray();
		// the water polygon: the vertices carrying the water material (its texture name ends in _water), all at one height
		var waterVertices = mesh.Vertices.Select( ( vertex, index ) => (vertex, index) )
			.Where( pair => mesh.Materials[pair.vertex.TextureIndex].Name.EndsWith( "_water", StringComparison.Ordinal ) )
			.Select( pair => points[pair.index] ).ToArray();
		Assert.IsTrue( waterVertices.Length >= 8, $"{theme}: the water surface" );
		var water = waterVertices.Max( p => p.Y );
		Assert.AreEqual( water, waterVertices.Min( p => p.Y ), 1e-3f, $"{theme}: the water is flat" );
		var middle = new System.Numerics.Vector2( waterVertices.Average( p => p.X ), waterVertices.Average( p => p.Z ) );
		var circumradius = waterVertices.Max( p => System.Numerics.Vector2.Distance( new System.Numerics.Vector2( p.X, p.Z ), middle ) );
		Assert.AreEqual( circumradius, waterVertices.Min( p => System.Numerics.Vector2.Distance( new System.Numerics.Vector2( p.X, p.Z ), middle ) ), 1e-2f, $"{theme}: a regular octagon of water" );
		// the basin's wall: the vertices on the water's circle (the inner wall, from the floor to the rim); the prop in the corner is further out
		var wall = points.Where( p => Math.Abs( System.Numerics.Vector2.Distance( new System.Numerics.Vector2( p.X, p.Z ), middle ) - circumradius ) < 0.02f ).ToArray();
		return new Tub( middle, circumradius, circumradius * MathF.Cos( MathF.PI / 8 ), water, wall.Max( p => p.Y ), wall.Min( p => p.Y ) );
	}

	/// <summary>How far each side of its hotspot the seated sprite reaches below the height <paramref name="aboveHotspot"/> (engine units, up from the hotspot), over every set and direction, and how deep it hangs.</summary>
	private static (float HalfWidth, float Depth) SpriteBelow( float aboveHotspot )
	{
		var halfWidth = 0f;
		var depth = 0f;
		var sets = PoolSwimSprites.Load();
		Assert.IsTrue( sets.Count > 0, "the swim sets" );
		foreach ( var set in sets )
		{
			Assert.IsTrue( set.HasArt( PoolSwimSlots.SittingInWater ), $"{set.Name} has the sitting art" );
			var pixels = set.Pixels;
			for ( var direction = 0; direction <= 4; direction++ )
				for ( var step = 0; step < set.FramesPerDirection( PoolSwimSlots.SittingInWater ); step++ )
				{
					var frame = set.Frames[set.GetFrame( PoolSwimSlots.SittingInWater, direction, step )];
					var unitX = GuestRenderer.UnitsPerPixel * frame.ScaleX;
					var unitY = GuestRenderer.UnitsPerPixel * frame.ScaleY;
					for ( var row = 0; row < frame.Height; row++ )
					{
						var up = -(row + frame.OriginY) * unitY;
						if ( up > aboveHotspot )
							continue;
						for ( var column = 0; column < frame.Width; column++ )
						{
							if ( pixels[((frame.Y + row) * set.Width + frame.X + column) * 4 + 3] <= 40 )
								continue;
							halfWidth = MathF.Max( halfWidth, MathF.Max( MathF.Abs( (column + frame.OriginX) * unitX ), MathF.Abs( (column + 1 + frame.OriginX) * unitX ) ) );
							depth = MathF.Max( depth, -up );
						}
					}
				}
		}
		return (halfWidth, depth);
	}

	[TestMethod]
	public void EverySeatOfEveryThemesSpaHoldsTheSeatedSpriteInsideTheBasinAtTheWaterLine()
	{
		// the small spa: one tile, one seat; the large spa: 2 x 2 tiles, four seats
		foreach ( var (kind, tiles, seatCount) in new[] { ("spa", 1, PoolAttractionSettings.SpaSeats), ("bigspa", 2, PoolAttractionSettings.BigSpaSeats) } )
			CheckSpa( kind, tiles, seatCount );
	}

	private static void CheckSpa( string kind, int tiles, int seatCount )
	{
		var root = RepositoryFolder();
		PoolContent.DirectoryOverride = Path.Combine( root, "content", "pools" );
		try
		{
			var seats = Enumerable.Range( 0, seatCount ).Select( index => SpaActivity.Seat( (0, 0), index, seatCount ) ).ToArray();
			foreach ( var theme in Themes )
			{
				var label = $"{theme} {kind}";
				var tub = Measure( root, theme, kind );
				// the tub is centred on its footprint (so it is on the middle of it, whatever the object's rotation: turning is about that middle)
				Assert.AreEqual( tiles * CellModelUnits / 2, tub.Middle.X, 1e-2f, $"{label}: centred across" );
				Assert.AreEqual( tiles * CellModelUnits / 2, tub.Middle.Y, 1e-2f, $"{label}: centred along" );
				Assert.IsTrue( tub.Rim > tub.Water && tub.Water > tub.Floor, $"{label}: water below the rim, above the floor ({tub})" );
				Assert.IsTrue( tub.Circumradius < tiles * CellModelUnits / 2, $"{label}: the basin is inside the footprint" );

				// the sprite's part that is below the rim is what meets the wall; above it the head and arms are free
				var (halfWidth, depth) = SpriteBelow( (tub.Rim - tub.Water) * ModelUnit );
				var halfWidthUnits = halfWidth / ModelUnit;
				foreach ( var (x, y) in seats )
				{
					var seat = MathF.Sqrt( (x / 65536f * CellModelUnits) * (x / 65536f * CellModelUnits) + (y / 65536f * CellModelUnits) * (y / 65536f * CellModelUnits) );
					Assert.IsTrue( seat + halfWidthUnits <= tub.Apothem, $"{label}: a seat {seat:F2} model units from the middle plus the sprite's {halfWidthUnits:F2} each side is inside the basin's {tub.Apothem:F2}" );
				}
				// seated guests are kept apart: neighbours no closer than 0.65 cell (the sprite is 0.73 cell wide at the water line, so they overlap by about a tenth at most)
				for ( var i = 0; i < seats.Length; i++ )
					for ( var j = i + 1; j < seats.Length; j++ )
					{
						var distance = MathF.Sqrt( MathF.Pow( (seats[i].X - seats[j].X) / 65536f, 2 ) + MathF.Pow( (seats[i].Y - seats[j].Y) / 65536f, 2 ) );
						Assert.IsTrue( distance >= 0.65f, $"{label}: seats {i} and {j} are {distance:F3} cell apart" );
					}
				// the seated height is the water surface (torso at the water line) and the sprite does not hang below the floor
				Assert.AreEqual( tub.Water * ModelUnit, PoolAttractionSettings.SpaWaterHeight, 0.011f, $"{label}: seated at the water surface" );
				Assert.IsTrue( tub.Water - depth / ModelUnit >= tub.Floor - 1e-3f, $"{label}: the sprite's lower end ({tub.Water - depth / ModelUnit:F2}) stays above the floor ({tub.Floor:F2})" );
			}
		}
		finally
		{
			PoolContent.DirectoryOverride = null;
		}
	}

	[TestMethod]
	public void AFifthSeatInTheLargeSpaWouldNotFitWithoutCrowding()
	{
		// the argument for four: five seats on the same circle are 0.56 cell apart, closer than the 0.65 the four keep
		var five = Enumerable.Range( 0, 5 ).Select( index => (X: MathF.Cos( 2 * MathF.PI * index / 5 ) * PoolAttractionSettings.BigSpaSeatRadius / 65536f, Y: MathF.Sin( 2 * MathF.PI * index / 5 ) * PoolAttractionSettings.BigSpaSeatRadius / 65536f) ).ToArray();
		Assert.IsTrue( MathF.Sqrt( MathF.Pow( five[0].X - five[1].X, 2 ) + MathF.Pow( five[0].Y - five[1].Y, 2 ) ) < 0.65f );
	}

	[TestMethod]
	public void TheSeatedGuestIsDrawnAtTheWaterSurfaceAboveTheDeck()
	{
		var activity = new SpaActivity( new PoolAttractionSites( () => Array.Empty<PoolAttractionSite>() ) );
		var guest = new Guest( 1, 0 ) { PoolActivityPhase = 1 };
		Assert.AreEqual( 10f + PoolAttractionSettings.SpaWaterHeight, activity.FeetHeight( null!, guest, new PoolFeetHeights( 0f, 0f, 10f ) )!.Value, 1e-5f );
		guest.PoolActivityPhase = 0;
		Assert.IsNull( activity.FeetHeight( null!, guest, new PoolFeetHeights( 0f, 0f, 10f ) ), "swimming to the spa: the normal height" );
	}
}

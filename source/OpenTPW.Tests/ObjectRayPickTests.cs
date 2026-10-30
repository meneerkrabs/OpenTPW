using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The object half of the original's cursor pick (tp.exe 0x45bf90's cell walk, 0x45cdc0, the model test 0x45d090) and the park click it feeds: a click on
/// the visible top of a tall object picks that object's cell, not the terrain behind it.
/// </summary>
[TestClass, DoNotParallelize]
public class ObjectRayPickTests
{
	/// <summary>An axis box in engine space, its faces as triangles, drawn between two heights.</summary>
	private sealed class Box( NVector3 min, NVector3 max ) : IObjectPickShape
	{
		public int Tests { get; private set; }

		public float PickBottom => min.Z;

		/// <summary>The owner as a cell names it: the box with its top as the cell's height.</summary>
		public ObjectRayPick.Owner Owner => new( this, max.Z );

		public bool TryHitPickLine( NVector3 origin, NVector3 direction, out NVector3 hit )
		{
			Tests++;
			foreach ( var (a, b, c) in Faces() )
				if ( ObjectRayPick.TryHitFace( origin, direction, a, b, c, NVector3.Cross( b - a, c - a ), out hit ) )
					return true;
			hit = default;
			return false;
		}

		/// <summary>The front (low Y) face first, then the back, top, bottom and sides.</summary>
		private IEnumerable<(NVector3, NVector3, NVector3)> Faces()
		{
			NVector3 P( int x, int y, int z ) => new( x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z );
			var quads = new[]
			{
				(P( 0, 0, 0 ), P( 1, 0, 0 ), P( 1, 0, 1 ), P( 0, 0, 1 )),
				(P( 0, 1, 0 ), P( 1, 1, 0 ), P( 1, 1, 1 ), P( 0, 1, 1 )),
				(P( 0, 0, 1 ), P( 1, 0, 1 ), P( 1, 1, 1 ), P( 0, 1, 1 )),
				(P( 0, 0, 0 ), P( 1, 0, 0 ), P( 1, 1, 0 ), P( 0, 1, 0 )),
				(P( 0, 0, 0 ), P( 0, 1, 0 ), P( 0, 1, 1 ), P( 0, 0, 1 )),
				(P( 1, 0, 0 ), P( 1, 1, 0 ), P( 1, 1, 1 ), P( 1, 0, 1 )),
			};
			foreach ( var (a, b, c, d) in quads )
			{
				yield return (a, b, c);
				yield return (a, c, d);
			}
		}
	}

	/// <summary>A 20 x 20 grid of 2-unit cells from the engine origin.</summary>
	private static readonly ObjectRayPick.Grid Grid = new( NVector2.Zero, 2, 20, 20 );

	/// <summary>A tower on cell (10, 10): a 1.6 x 1.6 column, 20 units tall, inset by 0.2 in its cell.</summary>
	private static Box Tower() => new( new NVector3( 20.2f, 20.2f, 0 ), new NVector3( 21.8f, 21.8f, 20 ) );

	/// <summary>The camera south of the tower, looking down at its top part: the ray passes the top and meets the ground far behind the tower.</summary>
	private static (NVector3 Eye, NVector3 Direction, NVector3 Ground) TopRay()
	{
		var eye = new NVector3( 21, 0, 30 );
		var direction = new NVector3( 21, 21, 18 ) - eye;
		// the flat ground (Z = 0) under the ray
		var ground = eye + direction * (eye.Z / -direction.Z);
		return (eye, direction, ground);
	}

	private static Func<int, int, ObjectRayPick.Owner?> OwnerAt( Box shape, int x, int y ) => ( cx, cy ) => cx == x && cy == y ? shape.Owner : null;

	[TestMethod]
	public void ARayOverATallObjectsTopMeetsTheTerrainBehindItsFootprint()
	{
		// the bug's geometry: the terrain point under the cursor is many cells behind the tower, so the terrain pick alone picks an empty cell
		var (_, _, ground) = TopRay();
		Assert.AreEqual( 10, (int)(ground.X / 2) );
		Assert.IsTrue( ground.Y / 2 >= 13, $"the ground point is behind the tower's cell: {ground}" );
	}

	[TestMethod]
	public void TheObjectsModelTakesTheCursorPointBeforeTheTerrainBehindIt()
	{
		var (eye, direction, ground) = TopRay();
		var tower = Tower();
		Assert.IsTrue( ObjectRayPick.TryPick( eye, direction, ground, Grid, OwnerAt( tower, 10, 10 ), out var hit, out var owner ) );
		Assert.AreSame( tower, owner );
		Assert.AreEqual( (10, 10), ((int)(hit.X / 2), (int)(hit.Y / 2)), $"the cursor cell is the tower's: {hit}" );
		Assert.AreEqual( 20.2f, hit.Y, 1e-3f, "the first face in the walk order is the front face" );
		Assert.AreEqual( 1, tower.Tests, "the tower's model is tested once, however many passes cross its cell" );
	}

	[TestMethod]
	public void AnEmptyGroundStaysTheTerrainPoint()
	{
		var (eye, direction, ground) = TopRay();
		Assert.IsFalse( ObjectRayPick.TryPick( eye, direction, ground, Grid, ( _, _ ) => null, out _, out var owner ) );
		Assert.IsNull( owner );
		// a ray beside the tower: the tower's cell is not crossed, or its model is missed
		var beside = new NVector3( 30, 21, 18 ) - eye;
		var besideGround = eye + beside * (eye.Z / -beside.Z);
		Assert.IsFalse( ObjectRayPick.TryPick( eye, beside, besideGround, Grid, OwnerAt( Tower(), 10, 10 ), out _, out _ ) );
	}

	[TestMethod]
	public void AnObjectBehindTheTerrainHitIsNotPicked()
	{
		// a hill in front of the tower: the terrain hit (cell 10, 6, height 12) is reached before the tower; the walk stops below it
		var (eye, direction, _) = TopRay();
		var hill = eye + direction * 0.3f;
		var tower = Tower();
		Assert.IsFalse( ObjectRayPick.TryPick( eye, direction, hill, Grid, OwnerAt( tower, 10, 10 ), out _, out _ ) );
		Assert.AreEqual( 0, tower.Tests, "the walk ends before the tower's cell" );
	}

	[TestMethod]
	public void WithoutATerrainHitTheObjectPointIsTaken()
	{
		var (eye, direction, _) = TopRay();
		Assert.IsTrue( ObjectRayPick.TryPick( eye, direction, null, Grid, OwnerAt( Tower(), 10, 10 ), out var hit, out _ ) );
		Assert.AreEqual( (10, 10), ((int)(hit.X / 2), (int)(hit.Y / 2)) );
	}

	[TestMethod]
	public void TheHeightWindowSkipsAnOwnerTheRayPassesHighAbove()
	{
		// a low object on a cell the ray crosses far above it: its model is not tested there (the window), and the tower behind it is picked
		var (eye, direction, ground) = TopRay();
		var low = new Box( new NVector3( 20.2f, 10.2f, 0 ), new NVector3( 21.8f, 11.8f, 1 ) );
		var tower = Tower();
		Assert.IsTrue( ObjectRayPick.TryPick( eye, direction, ground, Grid, ( x, y ) => (x, y) == (10, 5) ? low.Owner : (x, y) == (10, 10) ? tower.Owner : null, out _, out var owner ) );
		Assert.AreSame( tower, owner );
		Assert.AreEqual( 0, low.Tests );
	}

	[TestMethod]
	public void AWalkFromOffTheGridTestsTheClampedEdgeCells()
	{
		// the camera south of the grid: the first passes clamp to row 0, where a tall wall stands in the ray's way
		var eye = new NVector3( 21, -20, 30 );
		var target = new NVector3( 21, 1, 10 );
		var direction = target - eye;
		var ground = eye + direction * (eye.Z / -direction.Z);
		var wall = new Box( new NVector3( 20.2f, 0.2f, 0 ), new NVector3( 21.8f, 1.8f, 25 ) );
		Assert.IsTrue( ObjectRayPick.TryPick( eye, direction, ground, Grid, OwnerAt( wall, 10, 0 ), out var hit, out _ ) );
		Assert.AreEqual( 0.2f, hit.Y, 1e-3f );
	}

	[TestMethod]
	public void ARayThatDoesNotFallWalksNoCell()
	{
		// the picker never runs with such a ray (the park camera looks down at 35 degrees or more, its frustum's top edge 26.57 degrees above the view): no walk
		var tower = Tower();
		Assert.IsFalse( ObjectRayPick.TryPick( new NVector3( 21, 0, 10 ), new NVector3( 0, 1, 0.1f ), null, Grid, OwnerAt( tower, 10, 10 ), out _, out _ ) );
		Assert.IsFalse( ObjectRayPick.TryPick( new NVector3( 21, 0, 10 ), new NVector3( 0, 1, 0 ), null, Grid, OwnerAt( tower, 10, 10 ), out _, out _ ) );
		Assert.AreEqual( 0, tower.Tests );
	}

	[TestMethod]
	public void TheParkCamerasLowestPitchLeavesEveryFrustumRayFalling()
	{
		// the lowest pitch of the zoom line (40 with the wide range) against the frustum's half height 0.5 at unit depth
		Assert.AreEqual( 35f, ParkCameraMode.PitchOf( 40 ) * 180 / MathF.PI, 0.01f );
		Assert.IsTrue( ParkCameraMode.PitchOf( 40 ) > MathF.Atan( 0.5f ) );
	}

	[TestMethod]
	public void TheFaceTestAcceptsThePlaneHitInsideTheFaceByItsAngleSum()
	{
		NVector3 a = new( 0, 0, 0 ), b = new( 1, 0, 0 ), c = new( 0, 1, 0 ), normal = new( 0, 0, 2 );
		Assert.IsTrue( ObjectRayPick.TryHitFace( new NVector3( 0.25f, 0.25f, 5 ), new NVector3( 0, 0, -1 ), a, b, c, normal, out var hit ) );
		Assert.AreEqual( new NVector3( 0.25f, 0.25f, 0 ), hit );
		// the whole line counts: the face behind the origin is hit too (no sign test on t, 0x45d090)
		Assert.IsTrue( ObjectRayPick.TryHitFace( new NVector3( 0.25f, 0.25f, 5 ), new NVector3( 0, 0, 1 ), a, b, c, normal, out _ ) );
		Assert.IsFalse( ObjectRayPick.TryHitFace( new NVector3( 0.75f, 0.75f, 5 ), new NVector3( 0, 0, -1 ), a, b, c, normal, out _ ), "outside the hypotenuse" );
		Assert.IsFalse( ObjectRayPick.TryHitFace( new NVector3( 0.25f, 0.25f, 5 ), new NVector3( 1, 0, 0 ), a, b, c, normal, out _ ), "a line along the plane meets it nowhere" );
		Assert.AreEqual( 6.2821856f, ObjectRayPick.InsideAngleSum );
	}

	// ---- The model test over a real MD2 model --------------------------------------------------------

	/// <summary>A tower model: a base slab mesh and, as its child, a column mesh 100 MD2 units tall, both inset by 1 unit in a one-cell footprint.</summary>
	internal static ModelFile TowerModel()
	{
		var builder = new ModelBuilder { SourceName = "tower" };
		var slab = builder.AddMesh( "slab" );
		var column = builder.AddMesh( "column", slab );
		void Cuboid( ModelNodeBuilder node, float bottom, float top )
		{
			var material = node.Mesh!.AddMaterial( "quad_tex", wrap: true );
			NVector3 P( int x, int y, int z ) => new( x == 0 ? 1 : 9, y == 0 ? bottom : top, z == 0 ? 1 : 9 );
			var quads = new[]
			{
				(P( 0, 0, 0 ), P( 1, 0, 0 ), P( 1, 1, 0 ), P( 0, 1, 0 )),
				(P( 1, 0, 1 ), P( 0, 0, 1 ), P( 0, 1, 1 ), P( 1, 1, 1 )),
				(P( 0, 1, 0 ), P( 1, 1, 0 ), P( 1, 1, 1 ), P( 0, 1, 1 )),
				(P( 0, 0, 1 ), P( 0, 0, 0 ), P( 0, 1, 0 ), P( 0, 1, 1 )),
				(P( 1, 0, 0 ), P( 1, 0, 1 ), P( 1, 1, 1 ), P( 1, 1, 0 )),
			};
			foreach ( var (a, b, c, d) in quads )
			{
				var normal = NVector3.Normalize( NVector3.Cross( b - a, c - a ) );
				material.AddQuad( material.AddVertex( a, normal, NVector2.Zero ), material.AddVertex( b, normal, NVector2.UnitX ), material.AddVertex( c, normal, NVector2.One ), material.AddVertex( d, normal, NVector2.UnitY ) );
			}
		}
		Cuboid( slab, 0, 2 );
		Cuboid( column, 2, 100 );
		return builder.Build();
	}

	[TestMethod]
	public void TheModelTestWalksTheNodesAndSkipsAHiddenNodesFacesButNotItsChildren()
	{
		var model = TowerModel();
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		var placement = new ObjectPlacement( 10, 10, 0, NVector2.Zero, 0 ).ModelToEngine;
		var (eye, direction, _) = TopRay();
		IReadOnlyList<NVector3>? NoVertices( int mesh ) => null;
		Assert.IsTrue( ObjectPickModel.TryHitLine( model, rest, _ => false, NoVertices, placement, eye, direction, out var hit ) );
		Assert.AreEqual( (10, 10), ((int)(hit.X / 2), (int)(hit.Y / 2)), $"{hit}" );
		Assert.IsTrue( hit.Z > 2, "the column, high above the slab" );
		var slab = model.Nodes.Single( node => node.Name == "slab" ).Index;
		var column = model.Nodes.Single( node => node.Name == "column" ).Index;
		Assert.IsTrue( ObjectPickModel.TryHitLine( model, rest, node => node == slab, NoVertices, placement, eye, direction, out _ ), "a hidden slab still walks its child column" );
		Assert.IsFalse( ObjectPickModel.TryHitLine( model, rest, node => node == column, NoVertices, placement, eye, direction, out _ ), "with the column hidden the line passes over the slab" );
	}

	[TestMethod]
	public void TheFacesAreWalkedMaterialByMaterial()
	{
		var mesh = new ModelFile.Mesh { Materials = new[] { new ModelFile.MaterialData { a = 0, b = 2 }, new ModelFile.MaterialData { a = 0, b = 2 } } };
		CollectionAssert.AreEqual( new[] { 0, 1, 0, 1 }, ObjectPickModel.FaceOrder( mesh, 4 ), "every range from its start word, also 0 twice" );
		mesh = new ModelFile.Mesh { Materials = new[] { new ModelFile.MaterialData { a = 2, b = 2 }, new ModelFile.MaterialData { a = 0, b = 2 } } };
		CollectionAssert.AreEqual( new[] { 2, 3, 0, 1 }, ObjectPickModel.FaceOrder( mesh, 4 ), "the original walks material by material" );
		mesh = new ModelFile.Mesh { Materials = new[] { new ModelFile.MaterialData { a = 1, b = 2 }, new ModelFile.MaterialData { a = 2, b = 1 } } };
		CollectionAssert.AreEqual( new[] { 1, 2, 2 }, ObjectPickModel.FaceOrder( mesh, 4 ), "a shared face twice, an uncovered face never" );
		mesh = new ModelFile.Mesh { Materials = new[] { new ModelFile.MaterialData { a = 3, b = 2 } } };
		CollectionAssert.AreEqual( new[] { 3 }, ObjectPickModel.FaceOrder( mesh, 4 ), "a range cut at the last face" );
	}

	[TestMethod]
	public void EveryShippedModelIsWalkedByItsMaterialRangesAndItsObjectsCanBeHitFromAbove()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the shipped models." );
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			int models = 0, meshes = 0, reordered = 0, hit = 0, mapped = 0;
			var heightMaps = new ObjectHeightMaps();
			foreach ( var theme in ObjectCatalog.Themes )
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( candidate => !candidate.IsFixedItem ) )
				{
					var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
					models++;
					foreach ( var mesh in model.Meshes.Where( candidate => candidate.Indices.Length > 0 ) )
					{
						// the walk goes material by material through the ranges (start a, count b), which are often out of the stored order (jungle bouncy's
						// jb_floor: ... (36,4) (48,2) (50,2) (40,4) (44,4)); every shipped range lies within its mesh
						var faces = mesh.Indices.Length / 3;
						var ranges = mesh.Materials.SelectMany( material => Enumerable.Range( material.a, material.b ) ).ToArray();
						CollectionAssert.AreEqual( ranges, ObjectPickModel.FaceOrder( mesh, faces ), $"{theme}/{entry.ArchiveName}/{mesh.Name}" );
						if ( !ranges.SequenceEqual( Enumerable.Range( 0, faces ) ) )
							reordered++;
						meshes++;
					}
					if ( heightMaps.MapOf( entry ) != null )
						mapped++;
					// a vertical line through the middle of each footprint cell at the rest pose: an object whose model is met by none is not pickable from above
					var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
					var placement = new ObjectPlacement( 20, 20, 0, NVector2.Zero, 0 );
					var cells = ObjectFootprint.GetCells( entry.Shape, 20, 20, 0 ).ToList();
					if ( cells.Any( cell => ObjectPickModel.TryHitLine( model, rest, _ => false, _ => null, placement.ModelToEngine,
						new NVector3( (cell.X + 0.5f) * ObjectPlacement.CellSize, (cell.Y + 0.5f) * ObjectPlacement.CellSize, 100 ), new NVector3( 0, 0, -1 ), out _ ) ) )
						hit++;
				}
			Console.WriteLine( $"object pick: {models} models, {meshes} meshes ({reordered} walked out of their stored face order), {mapped} with a height map, {hit} met from above" );
			Assert.IsTrue( models >= 200 && reordered > 0 && hit >= models * 9 / 10, $"{hit} of {models}" );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	// ---- The park click ---------------------------------------------------------------------------------

	private string root = null!;
	private List<Entity> entities = null!;
	private Logger log = null!;
	private CameraMode camera = null!;
	private NMatrix4x4 view, projection;
	private bool text;
	private List<InputButton> down = null!, last = null!;

	private static void Set( object o, string f, object v ) => o.GetType().GetField( f, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, v );
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private sealed class EmptyCatalog : IBuildCatalog { public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>(); }

	private sealed class TowerCatalog( ObjectCatalogEntry entry ) : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => category == BuildCategory.Rides
			? new[] { new BuildItem( "tower", 1100, BuildCategory.Rides, 0, 5000, null, Array.Empty<string>(), null, entry ) } : Array.Empty<BuildItem>();
	}

	/// <summary>A status with an economy whose balance is <paramref name="money"/>; every item is available at its catalogue price.</summary>
	private sealed class MoneyStatus( long money ) : IHudParkStatus
	{
		public bool HasEconomy => true;
		public long Money => money;
		public ParkDate Date => default;
		public GameSpeed Speed { get; set; } = GameSpeed.Normal;
		public float TimeScale => 1f;
		public void Update( float realSeconds ) { }
		public long? PriceOf( BuildItem item ) => item.Cost;
		public bool IsAvailable( BuildItem item ) => true;
	}

	[TestInitialize]
	public void Setup()
	{
		entities = Entity.All; Entity.All = new(); log = Log; Log = new(); camera = Camera.Mode; view = Camera.ViewMatrix; projection = Camera.ProjMatrix;
		text = Input.TextEntryActive; Input.TextEntryActive = false; down = Input.KeysDown; last = Input.LastKeysDown; Input.KeysDown = new(); Input.LastKeysDown = new();
		root = Path.Combine( Path.GetTempPath(), "opentpw-pick-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		// a model without meshes (RideUpgradeIngressTests): the object draws nothing, so it needs no graphics device; the test stands in the tower's shape for it
		var model = Md2ModelFileTests.CreateGeometry();
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 ); System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 ); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "tower.MD2" ), model );
		// the tower's height map beside its mesh: one cell whose maximum byte 255 is 100 model units (20 engine units) above its base
		Directory.CreateDirectory( Path.Combine( root, "tower" ) );
		File.WriteAllBytes( Path.Combine( root, "tower", "tower.hmp" ), HeightMap( 255 ) );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
	}

	/// <summary>A one-cell height map (HmpFileTests' layout) with the cell's maximum byte.</summary>
	private static byte[] HeightMap( byte maximum )
	{
		var data = new byte[48 + 25 + 2];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 0 ), 3 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 2 ), 0xAB1E );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 4 ), 5 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), 100 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 8 ), 1 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 10 ), 1 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 12 ), 48 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 16 ), 48 + 25 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 20 ), 48 + 26 );
		for ( var index = 0; index < 6; index++ )
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 24 + index * 4 ), index < 3 ? 0 : 100f );
		data[48 + 25] = maximum;
		return data;
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() ) entity.Delete();
		Entity.All = entities; Log = log; Camera.Mode = camera; Camera.ViewMatrix = view; Camera.ProjMatrix = projection;
		Input.TextEntryActive = text; Input.KeysDown = down; Input.LastKeysDown = last; Input.UpdateButtons();
		Directory.Delete( root, true );
	}

	/// <summary>A sandbox park with a tower ride on cell (16, 16) and the HUD over it, the camera south of the tower looking down at it.</summary>
	private ParkHud Park( out Level level, out OriginalObject tower, IHudParkStatus? status = null, bool catalog = false, int rotation = 0 )
	{
		VisitorWorldSelectionTests.Fixture( out var guests, out _ );
		level = Empty<Level>();
		var objects = new ParkObjects( null!, new SandboxParkGrid() );
		Set( level, "<Objects>k__BackingField", objects ); Set( level, "<Guests>k__BackingField", guests ); Set( level, "<CellTool>k__BackingField", new CellBuildTool() );
		Set( level, "<LastActionMessage>k__BackingField", "" );
		Set( level, "tourHeightMaps", new ObjectHeightMaps() );
		Set( level, "<Paths>k__BackingField", new ParkPathBuilder( guests.Grid.Cells, guests.Grid, (ParkEconomy?)null, new SandboxParkGrid() ) );
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\nInfo.Name tower\nInfo.IsChoosable 1\nInfo.WhichUIType 0\n", "tower.sam" ) } );
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/tower", settings, new ObjectShape( new[] { "*" } ), "/tower.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
		tower = objects.Add( entry, 16, 16, rotation );
		// the tower's model: a 1.6 x 1.6 column 20 units tall inset in its cell, as TowerModel draws it at rest
		var corner = new NVector3( ParkPlacement.MinimumCoordinate + 16 * ObjectPlacement.CellSize, ParkPlacement.MinimumCoordinate + 16 * ObjectPlacement.CellSize, 0 );
		var shape = new Box( corner + new NVector3( 0.2f, 0.2f, 0 ), corner + new NVector3( 1.8f, 1.8f, 20 ) );
		var placed = tower;
		level.PickShapeOf = item => item == placed ? shape : null;
		var hud = new ParkHud( level, OriginalUiTests.FakeStrings(), status ?? new NoEconomyStatus(), catalog ? new TowerCatalog( entry ) : new EmptyCatalog(), new HudHost() );
		// the camera: south of the tower and above it, looking at the tower's top
		var cell = new NVector3( ParkPlacement.MinimumCoordinate + 16.5f * ObjectPlacement.CellSize, ParkPlacement.MinimumCoordinate + 16.5f * ObjectPlacement.CellSize, 0 );
		Camera.Mode = new ParkCameraMode();
		Camera.ViewMatrix = NMatrix4x4.CreateLookAt( cell + new NVector3( 0, -20, 30 ), cell + new NVector3( 0, 0, 12 ), NVector3.UnitZ );
		Camera.ProjMatrix = NMatrix4x4.CreatePerspectiveFieldOfView( 0.8f, Canvas.X / Canvas.Y, 0.5f, 500 );
		return hud;
	}

	private static readonly NVector2 Canvas = new( 2048, 1536 );

	/// <summary>The screen point of an engine point under the test camera.</summary>
	private static NVector2 Screen( NVector3 point )
	{
		var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( point, 1 ), Camera.ViewMatrix * Camera.ProjMatrix );
		return new NVector2( (clip.X / clip.W + 1) / 2 * Canvas.X, (1 - clip.Y / clip.W) / 2 * Canvas.Y );
	}

	private static NVector3 TowerTop() => new( ParkPlacement.MinimumCoordinate + 16.5f * ObjectPlacement.CellSize, ParkPlacement.MinimumCoordinate + 16.2f * ObjectPlacement.CellSize, 17 );

	/// <summary>A press and a release at <paramref name="mouse"/> in the park, as the input source reports a click.</summary>
	private static void Click( ParkHud hud, UiContext context, NVector2 mouse )
	{
		Input.UpdateButtons();
		hud.Update( context, new UiInput( mouse, true, true, false, false, UiKeys.None ), false, false, false );
		hud.Update( context, UiInput.Click( mouse ), false, false, false );
	}

	[TestMethod]
	public void AClickOnATallRidesTopSelectsTheRideInsteadOfStartingAPathBehindIt()
	{
		var hud = Park( out var level, out var tower );
		var context = RideUpgradeIngressTests.Context();
		var mouse = Screen( TowerTop() );
		// the report's geometry: the terrain under that pixel is an empty cell behind the ride (the terrain-only pick that the park click used)
		Assert.IsTrue( ParkPlacement.TryGetGroundPoint( mouse, Canvas, Camera.ViewMatrix, Camera.ProjMatrix, out var ground ) );
		Assert.IsTrue( level.TryGetGridCell( ground.X, ground.Y, out var groundX, out var groundY ) );
		Assert.IsTrue( groundY > 16 && level.Objects.FindAt( groundX, groundY ) == null, $"the terrain behind the ride, cell ({groundX}, {groundY}), is empty" );
		// the pick now meets the ride's model first: the cursor cell is the ride's
		Assert.IsTrue( level.TryGetGridCell( mouse, Canvas, out var x, out var y ) );
		Assert.AreEqual( (16, 16), (x, y) );
		hud.SelectObject( null );
		Click( hud, context, mouse );
		Assert.AreSame( tower, hud.SelectedObject, "the click selects the ride" );
		Assert.IsFalse( level.CellTool.IsActive, "and starts no path tool behind it" );
	}

	/// <summary>
	/// [BIN:TPW-EXE:0x488760 left press] the click dispatcher 0x4879D0 runs on the press (message 0x10005), before the mode object's press slot: in the idle mode the window of the hovered object opens
	/// there, with the button still down, and the release does nothing in the idle mode (slot +8 of the idle object, 0x6B70D0).
	/// </summary>
	[TestMethod]
	public void ThePressOpensTheRidesWindowAtOnceAndTheReleaseChangesNothing()
	{
		var hud = Park( out var level, out var tower );
		var context = RideUpgradeIngressTests.Context();
		var mouse = Screen( TowerTop() );
		hud.SelectObject( null );
		Input.UpdateButtons();
		hud.Update( context, new UiInput( mouse, true, true, false, false, UiKeys.None ), false, false, false );
		Assert.AreSame( tower, hud.SelectedObject, "the press selects the ride" );
		Assert.IsFalse( level.CellTool.IsActive, "and starts no path tool behind it" );
		hud.SelectObject( null );
		hud.Update( context, UiInput.Click( mouse ), false, false, false );
		Assert.IsNull( hud.SelectedObject, "the release selects nothing: the pick is the press's" );
	}

	/// <summary>
	/// [BIN:TPW-EXE:0x4879D0 object copy] Ctrl at the press on a placed object arms a copy of it (the mode 4 build tool of its Info id, 0x46C5A0) at the object's own angle (0x52F1B0) and opens no window.
	/// </summary>
	[TestMethod]
	public void CtrlAndAPressOnAnObjectArmsACopyAtItsAngleAndOpensNoWindow()
	{
		var hud = Park( out var level, out var tower, new MoneyStatus( 10_000 ), catalog: true, rotation: 90 );
		var context = RideUpgradeIngressTests.Context();
		var mouse = Screen( TowerTop() );
		hud.SelectObject( null );
		Input.SetFrameForTests( new[] { Veldrid.Key.ControlLeft }, null );
		try
		{
			hud.Update( context, new UiInput( mouse, true, true, false, false, UiKeys.None ), false, false, false );
		}
		finally { Input.ResetFrameStateForTests(); }
		Assert.IsNull( hud.SelectedObject, "no window opens" );
		Assert.AreSame( tower.Entry, level.BuildEntry, "the tool of the clicked object's type is armed" );
		Assert.AreEqual( 90, level.BuildRotation, "at the object's own angle" );
	}

	/// <summary>A balance below the price plays the error cue and arms nothing; the click opens no window either (0x487bf1 returns before 0x486920).</summary>
	[TestMethod]
	public void CtrlAndAPressWithTooLittleMoneyArmsNothingAndPlaysTheErrorCue()
	{
		var hud = Park( out var level, out _, new MoneyStatus( 4_999 ), catalog: true );
		var context = RideUpgradeIngressTests.Context();
		var mouse = Screen( TowerTop() );
		var cues = new List<uint>();
		GameAudio.UiSoundObserver = cues.Add; GameAudio.UiGlobalSoundObserver = cues.Add;
		hud.SelectObject( null );
		Input.SetFrameForTests( new[] { Veldrid.Key.ControlRight }, null );
		try
		{
			hud.Update( context, new UiInput( mouse, true, true, false, false, UiKeys.None ), false, false, false );
		}
		finally { Input.ResetFrameStateForTests(); GameAudio.UiSoundObserver = null; GameAudio.UiGlobalSoundObserver = null; }
		Assert.IsNull( hud.SelectedObject );
		Assert.IsNull( level.BuildEntry );
		CollectionAssert.Contains( cues, GameAudio.ErrorEvent );
	}

	/// <summary>A read-only visit builds nothing, so Ctrl does not swallow the click: it opens the object's window as without Ctrl.</summary>
	[TestMethod]
	public void CtrlAndAPressInAReadOnlyVisitStillOpensTheWindow()
	{
		var hud = Park( out var level, out var tower, new MoneyStatus( 10_000 ), catalog: true );
		Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( OpenTPW.ParkVisitInfo ) ) );
		var context = RideUpgradeIngressTests.Context();
		hud.SelectObject( null );
		Input.SetFrameForTests( new[] { Veldrid.Key.ControlLeft }, null );
		try
		{
			hud.Update( context, new UiInput( Screen( TowerTop() ), true, true, false, false, UiKeys.None ), false, false, false );
		}
		finally { Input.ResetFrameStateForTests(); }
		Assert.AreSame( tower, hud.SelectedObject );
		Assert.IsNull( level.BuildEntry );
	}

	/// <summary>Without Ctrl the same press opens the window and arms nothing.</summary>
	[TestMethod]
	public void APressWithoutCtrlStillOpensTheWindowAndArmsNoCopy()
	{
		var hud = Park( out var level, out var tower, new MoneyStatus( 10_000 ), catalog: true );
		var context = RideUpgradeIngressTests.Context();
		hud.SelectObject( null );
		Input.UpdateButtons();
		hud.Update( context, new UiInput( Screen( TowerTop() ), true, true, false, false, UiKeys.None ), false, false, false );
		Assert.AreSame( tower, hud.SelectedObject );
		Assert.IsNull( level.BuildEntry );
	}

	/// <summary>The pick has no click-or-drag test: a press that goes on to be dragged away has opened the window all the same.</summary>
	[TestMethod]
	public void ARidePressThatBecomesADragHasAlreadySelectedTheRide()
	{
		var hud = Park( out _, out var tower );
		var context = RideUpgradeIngressTests.Context();
		var mouse = Screen( TowerTop() );
		hud.SelectObject( null );
		Input.UpdateButtons();
		hud.Update( context, new UiInput( mouse, true, true, false, false, UiKeys.None ), false, false, false );
		hud.Update( context, new UiInput( mouse + new NVector2( 40, 0 ), true, false, false, false, UiKeys.None ), false, false, false );
		hud.Update( context, UiInput.Click( mouse + new NVector2( 40, 0 ) ) with { NotClick = true }, false, false, false );
		Assert.AreSame( tower, hud.SelectedObject );
	}

	/// <summary>
	/// [BIN:TPW-EXE:0x524AA7 refusal cue] a refused build click plays cat_ui 0xAF at its release; the "Cannot build" message that follows then adds no error feedback 0x1D (<c>[EXT:cannot-build-sound]</c> keeps
	/// it for a refusal no cue preceded).
	/// </summary>
	[TestMethod]
	public void ARefusedBuildClickPlaysTheRefusalCueAtItsReleaseAndItsMessageAddsNoErrorFeedback()
	{
		var hud = Park( out var level, out var tower );
		var context = RideUpgradeIngressTests.Context();
		var mouse = Screen( TowerTop() );
		var cues = new List<uint>(); var global = new List<uint>();
		GameAudio.UiSoundObserver = cues.Add; GameAudio.UiGlobalSoundObserver = global.Add;
		try
		{
			level.BuildEntry = tower.Entry;
			Click( hud, context, mouse );
			CollectionAssert.AreEqual( new[] { GameAudio.PathRefusedEvent }, cues, "the tower's own cell refuses the ghost: the cue at the release" );
			typeof( Level ).GetField( "<LastActionMessage>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, "Cannot build: Occupied." );
			hud.Update( context, UiInput.Idle( mouse ), false, false, false );
			Assert.IsFalse( global.Contains( GameAudio.ErrorEvent ), "the message after a cue adds no 0x1D" );
			typeof( Level ).GetField( "<LastActionMessage>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, "Cannot build: Blocked." );
			hud.Update( context, UiInput.Idle( mouse ), false, false, false );
			Assert.IsTrue( global.Contains( GameAudio.ErrorEvent ), "a refusal no cue preceded keeps the error feedback" );
		}
		finally { GameAudio.UiSoundObserver = null; GameAudio.UiGlobalSoundObserver = null; }
	}

	[TestMethod]
	public void ATerrainOnlyPickSendsTheSameClickToAPathBehindTheRide()
	{
		// the reported bug, as the terrain-only pick behaved: with no model for the ray to meet, the click on the ride's top lands on the empty cell behind it
		var hud = Park( out var level, out _ );
		level.PickShapeOf = null;
		var context = RideUpgradeIngressTests.Context();
		var mouse = Screen( TowerTop() );
		Assert.IsTrue( level.TryGetGridCell( mouse, Canvas, out var x, out var y ) );
		Assert.IsTrue( y > 16, $"the cell behind the ride: ({x}, {y})" );
		hud.SelectObject( null );
		Click( hud, context, mouse );
		Assert.IsNull( hud.SelectedObject, "nothing is selected" );
		Assert.IsTrue( level.CellTool.IsActive, "the path tool starts behind the ride" );
	}

	[TestMethod]
	public void AnObjectWithoutAHeightMapIsNotTested()
	{
		// the cell lookup 0x452ae0 names no instance for an object without a height map, so the walk passes over it
		File.Delete( Path.Combine( root, "tower", "tower.hmp" ) );
		Park( out var level, out _ );
		Assert.IsTrue( level.TryGetGridCell( Screen( TowerTop() ), Canvas, out _, out var y ) );
		Assert.IsTrue( y > 16, "the terrain behind the tower" );
	}

	[TestMethod]
	public void APointerWhosePickTestsAModelIsNamedWhateverThePose()
	{
		Park( out var level, out _ );
		var mouse = Screen( TowerTop() );
		Assert.IsTrue( level.CursorPickTestsObjectModels( mouse, Canvas ), "the walk tests the tower's model under its top" );
		// the model moves out of the ray (an animated part, here the tower's own mesh-less model): the pick turns to the terrain behind it, the answer stays
		level.PickShapeOf = null;
		Assert.IsTrue( level.TryGetGridCell( mouse, Canvas, out _, out var y ) && y > 16, "the pick is the terrain behind the tower now" );
		Assert.IsTrue( level.CursorPickTestsObjectModels( mouse, Canvas ), "the answer does not follow the model's pose" );
		// the ground well to the side of the tower: no owner on the walk, the pick is the terrain's whatever the models do
		var side = new NVector3( ParkPlacement.MinimumCoordinate + 22.5f * ObjectPlacement.CellSize, ParkPlacement.MinimumCoordinate + 18.5f * ObjectPlacement.CellSize, 0 );
		Assert.IsFalse( level.CursorPickTestsObjectModels( Screen( side ), Canvas ) );
	}

	[TestMethod]
	public void AClickOnEmptyGroundStillStartsThePathTool()
	{
		var hud = Park( out var level, out _ );
		var context = RideUpgradeIngressTests.Context();
		// the ground well to the side of the ride
		var side = new NVector3( ParkPlacement.MinimumCoordinate + 22.5f * ObjectPlacement.CellSize, ParkPlacement.MinimumCoordinate + 18.5f * ObjectPlacement.CellSize, 0 );
		var mouse = Screen( side );
		Assert.IsTrue( level.TryGetGridCell( mouse, Canvas, out var x, out var y ) );
		Assert.AreEqual( (22, 18), (x, y) );
		hud.SelectObject( null );
		Click( hud, context, mouse );
		Assert.IsNull( hud.SelectedObject );
		Assert.IsTrue( level.CellTool.IsActive, "a press on an empty owned cell starts the path tool (help 441/442)" );
	}
}

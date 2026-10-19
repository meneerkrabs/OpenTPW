using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>Actual employee callback and CPU Draw, plus independent widget controls. No GPU or original media.</summary>
[TestClass, DoNotParallelize]
public class EmployeePortraitGeometryTests
{
	private List<Entity> previousEntities = null!;
	private Level previousLevel = null!;
	private Renderer previousRenderer = null!;
	private CameraMode previousCamera = null!;
	private N.Matrix4x4 previousView, previousProjection;
	private float previousNow, previousDelta;
	private bool previousTextEntry;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Setup()
	{
		previousEntities = Entity.All; previousLevel = Level.Current; previousRenderer = Global.Render;
		previousCamera = Camera.Mode; previousView = Camera.ViewMatrix; previousProjection = Camera.ProjMatrix;
		previousNow = Time.Now; previousDelta = Time.Delta; previousTextEntry = Input.TextEntryActive; previousLog = Log;
		Entity.All = new(); Camera.Mode = new CameraMode(); Camera.ViewMatrix = Camera.ProjMatrix = N.Matrix4x4.Identity;
		Time.Now = Time.Delta = 0; Input.TextEntryActive = false; Log ??= new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		Entity.All = previousEntities; Level.Current = previousLevel; Global.Render = previousRenderer;
		Camera.Mode = previousCamera; Camera.ViewMatrix = previousView; Camera.ProjMatrix = previousProjection;
		Time.Now = previousNow; Time.Delta = previousDelta; Input.TextEntryActive = previousTextEntry; Log = previousLog;
	}

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static FieldInfo Field( object owner, string name ) => owner.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public )!;
	private static void Set( object owner, string name, object? value ) => Field( owner, name ).SetValue( owner, value );
	private static T Get<T>( object owner, string name ) => (T)Field( owner, name ).GetValue( owner )!;
	private static UiSpriteFigure Figure( ParkHud hud ) => (UiSpriteFigure)hud.Stack.Top!.Find( "portraitFigure" )!;

	// Constructor-free managed identity sentinel only. Never invoke native operations or Dispose.
	private static Texture TextureSentinel()
	{
		var nativeType = typeof( Veldrid.Texture ).Assembly.GetType( "Veldrid.Vk.VkTexture", throwOnError: true )!;
		var texture = Empty<Texture>(); texture.NativeTexture = (Veldrid.Texture)RuntimeHelpers.GetUninitializedObject( nativeType );
		return texture;
	}

	private static GuestSpriteAtlas Atlas( int directions, int a, int b )
	{
		var builder = new SpriteSetBuilder( "SPR_EG" ) { FullDetail = false };
		foreach ( var (slot, count) in new[] { (0, 1), (1, 8), (4, 8) } )
		{
			var frames = Enumerable.Range( 0, directions ).Select( direction => (IReadOnlyList<SpriteRgbaFrame>)Enumerable.Range( 0, count )
				.Select( step => new SpriteRgbaFrame( 20, 40, Enumerable.Range( 0, 800 )
					.SelectMany( _ => new byte[] { (byte)(30 + slot), (byte)(50 + direction), (byte)(70 + step), 255 } ).ToArray(), -8, -38 ) ).ToArray() ).ToArray();
			builder.AddAnimation( slot, frames );
		}
		var data = builder.Build();
		var atlas = new GuestSpriteAtlas( "SPR_EG", new SpriteBankFile( new MemoryStream( data.Files[".TPC"] ) ), new SpriteAnimationFile( new MemoryStream( data.Files[".ESP"] ) ) );
		for ( var index = 0; index < atlas.Frames.Length; index++ )
			atlas.Frames[index] = atlas.Frames[index] with { PixelsPerUnitX = (ushort)a, PixelsPerUnitY = (ushort)b };
		return atlas;
	}

	private sealed class Rig
	{
		public readonly ParkEconomy Economy = EconomyTestData.Park();
		public readonly StaffMember Member = new() { Id = 1, ThingId = 101, Type = StaffType.Handyman, NameIndex = 0, Grade = 1,
			Variant = 0, CellX = 4, CellY = 4, State = StaffState.Working, Job = StaffJob.Sweeping };
		public readonly ParkHud Hud;
		public readonly Level Level;
		public readonly StaffRenderer Renderer;
		public readonly GuestSpriteAtlas Atlas;
		public readonly StaffSpriteSet Sprites;
		public List<Texture> Textures => Get<List<Texture>>( Renderer, "textures" );
		public object View => ((IDictionary)Field( Renderer, "views" ).GetValue( Renderer )!)[Member]!;
		public SpritePlayer Player => Get<SpritePlayer>( View, "Player" );
		public Rig( int directions = 5, int a = 128, int b = 128 )
		{
			((List<StaffMember>)Economy.Staff.Members).Add( Member );
			Hud = StaffWindowTests.Hud( Economy ); Level = Get<Level>( Hud, "level" ); OpenTPW.Level.Current = Level;
			Atlas = EmployeePortraitGeometryTests.Atlas( directions, a, b );
			Sprites = (StaffSpriteSet)Activator.CreateInstance( typeof( StaffSpriteSet ), BindingFlags.Instance | BindingFlags.NonPublic, null,
				new object[] { new[] { Atlas }, Enumerable.Repeat( (0, 1), 5 ).ToArray() }, null )!;
			Renderer = new StaffRenderer( () => Economy.Staff.Members, new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] ),
				StaffSpriteSet.Empty, new StaffPlacement( () => null, () => null, (_, _) => false ) );
			Set( Renderer, "sprites", Sprites ); Set( Renderer, "material", Empty<Material<ObjectUniformBuffer>>() );
			Textures.Add( TextureSentinel() ); Set( Level, "<StaffRenderer>k__BackingField", Renderer );
			Assert.IsTrue( Hud.OpenEmployeeWindow( Member ) );
		}
		public int Prepare( float now )
		{
			OpenTPW.Level.Current = Level; Time.Delta = 0.07f; Time.Now = now;
			return Renderer.PrepareWorld();
		}
		public UiSpriteFrame Expected( int slot, int step, int direction, bool mirror )
		{
			var frame = Atlas.Frames[Atlas.GetFrame( slot, direction, step )];
			return new UiSpriteFrame( Textures[0].NativeTexture, Atlas.Width, Atlas.Height, frame.X, frame.Y, frame.Width, frame.Height,
				frame.OriginX, frame.OriginY, mirror, frame.ScaleX, frame.ScaleY );
		}
		public string State()
		{
			var state = View.GetType().GetFields().Where( field => field.Name is not ("Player" or "Published") )
				.Select( field => $"{field.Name}={field.GetValue( View )}" );
			return string.Join( ";", state ) + $";steps={Player.Steps};interval={Player.Interval};due={Get<long>( Player, "due" )};member={JsonSerializer.Serialize( Member )}";
		}
	}

	private static UiContext Context( int width, int height )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( width, height ) };
	}

	[DataTestMethod]
	[DataRow( 386, 280, 650, 544, 526, 476 )]
	[DataRow( 385, 279, 650, 544, 525, 476 )]
	[DataRow( 10, 20, 23, 35, 24, 30 )]
	[DataRow( 20, 30, 7, 15, 21, 17 )]
	public void IntegerEndpointsUseSignedShiftsAndEmployeeOnlyCloneOffset( int left, int top, int right, int bottom, int x, int y )
	{
		var expected = new N.Vector2( x, y );
		Assert.AreEqual( expected, EmployeePortraitGeometry.Feet( left, top, right, bottom ) );
		Assert.AreEqual( expected, EmployeePortraitGeometry.Feet( new UiRect( left, top, right - left, bottom - top ) ) );
		Assert.AreNotEqual( HirePortraitGeometry.Feet( left, top, right, bottom ), expected, "equal primary scales do not imply equal clone-helper offsets" );
		Assert.AreNotEqual( VisitorInfoRules.PortraitFeet( new UiRect( left, top, right - left, bottom - top ) ), expected );
	}

	[TestMethod]
	public void ActualEmployeeWidgetInheritsInnerBoundsWithoutMovingItsFrameArt()
	{
		var rig = new Rig(); var figure = Figure( rig.Hud ); var portrait = OriginalLayoutTables.EmployeeWindow.Control( 0x511 );
		Assert.IsNull( portrait.Inner ); Assert.AreEqual( new UiRect( 386, 280, 264, 264 ), portrait.Rect );
		Assert.AreEqual( portrait.Rect, figure.Bounds );
		Assert.AreEqual( portrait.Rect, rig.Hud.Stack.Top!.Find( "portraitFrame" )!.Bounds );
		Assert.AreEqual( UiAnchor.Center, rig.Hud.Stack.Top!.Find( "portraitFrame" )!.Anchor );
		Assert.AreEqual( new N.Vector2( 526, 476 ), figure.FeetInCanvas!.Value );
		Assert.AreEqual( 2.4f, figure.CanvasUnitsPerPixel ); Assert.AreEqual( UiAnchor.Center, figure.Anchor );
	}

	[DataTestMethod]
	[DataRow( 2048, 1536 )]
	[DataRow( 640, 480 )]
	[DataRow( 1280, 720 )]
	[DataRow( 1920, 1080 )]
	[DataRow( 1000, 1000 )]
	[DataRow( 801, 601 )]
	[DataRow( 64, 48 )]
	public void ActualCallbackDrawsLiveNonWalkPhaseAndExistingFallbackAtEmployeeGeometry( int width, int height )
	{
		foreach ( var directions in new[] { 5, 8 } )
			foreach ( var heading in new[] { -1, 1 } )
				foreach ( var (a, b) in new[] { (128, 128), (256, 64) } )
				{
					var rig = new Rig( directions, a, b ); var figure = Figure( rig.Hud );
					Time.Now = 10; Assert.IsNull( rig.Renderer.EmployeeFrame( rig.Member ) );
					Assert.AreEqual( rig.Expected( 1, 0, directions == 5 ? 3 : 5, directions == 5 ), figure.Frame()!.Value );
					AssertDraw( figure, width, height, a, b, 526, 476, 2.4f );
					foreach ( var now in new[] { 10f, 10.07f, 10.14f, 10.21f } )
					{
						if ( now > 10 ) rig.Member.CellX += heading * 0.1f;
						Assert.AreEqual( 12, rig.Prepare( now ) );
					}
					Assert.IsTrue( rig.Player.Steps > 0 );
					var (slot, step) = StaffSpriteRules.ResolveSteps( StaffSpriteRules.Sweep, rig.Player.Steps, rig.Atlas.Animations.Animations );
					Assert.AreEqual( 4, slot ); Assert.IsTrue( step > 0, "exercise a nonzero live non-walk phase" );
					Assert.AreEqual( (StaffSpriteRules.Sweep.Script << 8) | 4, Get<int>( rig.View, "ClipKey" ) );
					var mirror = heading < 0 && directions == 5; var direction = heading < 0 && directions == 8 ? 6 : 2;
					Assert.AreEqual( rig.Expected( slot, step, direction, mirror ), figure.Frame()!.Value );
					AssertDraw( figure, width, height, a, b, 526, 476, 2.4f );
					Assert.AreEqual( 10f, Get<float>( rig.Hud, "employeePortraitStart" ) );
					rig.Member.State = StaffState.Resting; rig.Prepare( 10.4f );
					Assert.IsNull( rig.Renderer.EmployeeFrame( rig.Member ) );
					Assert.AreEqual( rig.Expected( 1, 6, directions == 5 ? 3 : 5, directions == 5 ), figure.Frame()!.Value,
						"source loss keeps the established fallback phase instead of freezing or restarting" );
					AssertDraw( figure, width, height, a, b, 526, 476, 2.4f );
					Assert.AreEqual( 10f, Get<float>( rig.Hud, "employeePortraitStart" ) );
				}
	}

	[TestMethod]
	public void RepeatedActualCallbackReadsAndDrawsLeaveEconomyRngPlayerMovementAndNextWorldOutputUnchanged()
	{
		var observed = new Rig(); var figure = Figure( observed.Hud ); var control = new Rig();
		foreach ( var now in new[] { 0f, 0.07f, 0.14f } )
		{
			if ( now > 0 ) { observed.Member.CellX -= 0.1f; control.Member.CellX -= 0.1f; }
			observed.Prepare( now ); control.Prepare( now );
		}
		observed.Member.CellY += 0.25f; control.Member.CellY += 0.25f;
		Set( observed.View, "StepLength", 5432f ); Set( control.View, "StepLength", 5432f );
		Level.Current = observed.Level; var before = observed.State();
		var save = ParkSaveFile.Serialize( observed.Economy ); var random = observed.Economy.World.State;
		var controlSave = ParkSaveFile.Serialize( control.Economy ); var controlRandom = control.Economy.World.State;
		var frame = figure.Frame(); Assert.IsNotNull( frame );
		Assert.AreEqual( observed.Renderer.EmployeeFrame( observed.Member ), frame );
		for ( var index = 0; index < 100; index++ )
		{
			Assert.AreEqual( frame, figure.Frame() ); AssertDraw( figure, 801, 601, 128, 128, 526, 476, 2.4f );
		}
		Assert.AreEqual( before, observed.State() ); Assert.AreEqual( control.State(), observed.State() );
		Assert.AreEqual( save, ParkSaveFile.Serialize( observed.Economy ) ); Assert.AreEqual( random, observed.Economy.World.State );
		Assert.AreEqual( controlSave, ParkSaveFile.Serialize( control.Economy ) ); Assert.AreEqual( controlRandom, control.Economy.World.State );
		observed.Prepare( 0.21f ); control.Prepare( 0.21f );
		Assert.AreEqual( control.State(), observed.State() );
		CollectionAssert.AreEqual( Get<Vertex[]>( control.Renderer, "vertices" ).Take( 12 ).ToArray(),
			Get<Vertex[]>( observed.Renderer, "vertices" ).Take( 12 ).ToArray(), "the next actual world quad is identical to the no-read control" );
	}

	[DataTestMethod]
	[DataRow( 2048, 1536 )]
	[DataRow( 640, 480 )]
	[DataRow( 1280, 720 )]
	[DataRow( 1920, 1080 )]
	[DataRow( 1000, 1000 )]
	[DataRow( 801, 601 )]
	[DataRow( 64, 48 )]
	public void ActualThreeWidgetsAndDefaultDrawKeepIndependentBoundsFeetAndScale( int width, int height )
	{
		var rig = new Rig(); var employee = Figure( rig.Hud );
		rig.Hud.CloseEmployeeWindow(); rig.Hud.OpenStaffWindow(); var hire = Figure( rig.Hud );
		var visitorHud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var guest = new Guest( 7, 0 ) { State = GuestState.WalkingAround };
		((List<Guest>)simulation.Guests).Add( guest ); Assert.IsTrue( visitorHud.OpenVisitorInfoWindow( guest ) );
		var visitor = Figure( visitorHud );
		var generic = new UiSpriteFigure { Bounds = new UiRect( 386, 280, 264, 264 ), Anchor = UiAnchor.Center };
		Assert.AreEqual( new UiRect( 386, 280, 264, 264 ), employee.Bounds );
		Assert.AreEqual( new UiRect( 513, 202, 210, 213 ), hire.Bounds );
		Assert.AreEqual( new UiRect( 477, 281, 227, 229 ), visitor.Bounds );
		Assert.AreEqual( new UiRect( 484, 179, 264, 264 ), rig.Hud.Stack.Top!.Find( "staffPicture" )!.Bounds );
		Assert.IsNull( generic.FeetInCanvas ); Assert.AreEqual( 3.2f, generic.CanvasUnitsPerPixel );
		foreach ( var (figure, x, y, pixel) in new[] { (employee, 526, 476, 2.4f), (hire, 626, 362, 2.4f), (visitor, 598, 450, 3.6f), (generic, 518, 478, 3.2f) } )
		{
			Assert.AreEqual( pixel, figure.CanvasUnitsPerPixel ); Assert.AreEqual( UiAnchor.Center, figure.Anchor );
			if ( figure != generic ) Assert.AreEqual( new N.Vector2( x, y ), figure.FeetInCanvas!.Value );
			foreach ( var mirror in new[] { false, true } )
				foreach ( var (a, b) in new[] { (128, 128), (256, 64) } )
				{
					var frame = new UiSpriteFrame( rig.Textures[0].NativeTexture, 128, 128, 10, 20, 20, 40, -8, -38, mirror, 128f / a, 128f / b );
					var calls = 0; figure.Frame = () => { calls++; return frame; };
					AssertDraw( figure, width, height, a, b, x, y, pixel );
					Assert.AreEqual( 2, calls, "the oracle reads once, and Draw reads once without preparing a world frame" );
				}
		}
	}

	[TestMethod]
	public void MissingRendererBankAndTextureKeepExistingNullDrawAndFallbackClock()
	{
		var rig = new Rig(); var figure = Figure( rig.Hud ); Time.Now = 5; Assert.IsNotNull( figure.Frame() );
		void EmptyDraw()
		{
			Assert.IsNull( figure.Frame() ); var context = Context( 1280, 720 ); figure.Draw( context, false, false );
			Assert.AreEqual( 0, context.Batch.Draws.Count ); Assert.AreEqual( 5f, Get<float>( rig.Hud, "employeePortraitStart" ) );
		}
		Time.Now = 5.2f; Set( rig.Level, "<StaffRenderer>k__BackingField", null ); EmptyDraw();
		Set( rig.Level, "<StaffRenderer>k__BackingField", rig.Renderer );
		Set( rig.Renderer, "sprites", StaffSpriteSet.Empty ); EmptyDraw(); Set( rig.Renderer, "sprites", rig.Sprites );
		var texture = rig.Textures.Single(); rig.Textures.Clear(); EmptyDraw(); rig.Textures.Add( texture );
		Time.Now = 5.4f; Assert.AreEqual( rig.Expected( 1, 6, 3, true ), figure.Frame()!.Value );
		AssertDraw( figure, 1280, 720, 128, 128, 526, 476, 2.4f );
	}

	private static void AssertDraw( UiSpriteFigure figure, int width, int height, int a, int b, int x, int y, float pixel )
	{
		var frame = figure.Frame()!.Value; var context = Context( width, height ); figure.Draw( context, false, false );
		Assert.AreEqual( 1, context.Batch.Draws.Count ); var draw = context.Batch.Draws.Single(); Assert.AreEqual( 6, draw.Vertices.Count );
		Assert.AreEqual( (128f / a, 128f / b), (frame.ScaleX, frame.ScaleY) ); Assert.AreSame( frame.Texture, draw.Texture.Rendered );
		// Independent oracle: do not call EmployeePortraitGeometry, Canvas.Map, Feet or Layout here.
		var s = MathF.Max( 0.05f, MathF.Min( width / 2048f, height / 1536f ) );
		var feet = new N.Vector2( width / 2f + (x - 1024) * s, height / 2f + (y - 768) * s );
		var px = pixel * s * 128 / a; var py = pixel * s * 128 / b;
		var left = feet.X + (frame.Mirror ? -12 : -8) * px; var top = feet.Y - 38 * py;
		var u0 = (frame.X + (frame.Mirror ? 20 : 0)) / (float)frame.AtlasWidth;
		var u1 = (frame.X + (frame.Mirror ? 0 : 20)) / (float)frame.AtlasWidth;
		var v0 = frame.Y / (float)frame.AtlasHeight; var v1 = (frame.Y + 40) / (float)frame.AtlasHeight;
		var corners = new[] { (0, 0), (0, 1), (1, 0), (1, 0), (0, 1), (1, 1) };
		for ( var index = 0; index < corners.Length; index++ )
		{
			var (right, bottom) = corners[index];
			AssertPoint( new N.Vector2( left + right * 20 * px, top + bottom * 40 * py ), draw.Vertices[index].Position );
			AssertPoint( new N.Vector2( right == 0 ? u0 : u1, bottom == 0 ? v0 : v1 ), draw.Vertices[index].TexCoords );
			Assert.AreEqual( Veldrid.RgbaByte.White, draw.Vertices[index].Color );
		}
	}

	private static void AssertPoint( N.Vector2 expected, N.Vector2 actual )
	{
		Assert.AreEqual( expected.X, actual.X, 0.0002f ); Assert.AreEqual( expected.Y, actual.Y, 0.0002f );
	}
}

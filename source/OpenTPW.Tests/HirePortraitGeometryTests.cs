using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>Actual hire callback and CPU Draw with synthetic banks. No graphics device or original media.</summary>
[TestClass, DoNotParallelize]
public class HirePortraitGeometryTests
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

	// A constructor-free Vulkan texture is only a managed identity sentinel. No GPU operation or Dispose is called.
	private static Texture TextureSentinel()
	{
		var nativeType = typeof( Veldrid.Texture ).Assembly.GetType( "Veldrid.Vk.VkTexture", throwOnError: true )!;
		var texture = Empty<Texture>();
		texture.NativeTexture = (Veldrid.Texture)RuntimeHelpers.GetUninitializedObject( nativeType );
		return texture;
	}

	private static GuestSpriteAtlas Atlas( int index, int directions, int a, int b )
	{
		var name = $"SPR_H{index}"; var builder = new SpriteSetBuilder( name ) { FullDetail = false };
		foreach ( var (slot, count) in new[] { (0, 1), (1, 8) } )
		{
			var frames = Enumerable.Range( 0, directions ).Select( direction => (IReadOnlyList<SpriteRgbaFrame>)Enumerable.Range( 0, count )
				.Select( step => new SpriteRgbaFrame( 20, 40, Enumerable.Range( 0, 800 )
					.SelectMany( _ => new byte[] { (byte)(30 + index), (byte)(50 + direction), (byte)(70 + step + slot * 8), 255 } ).ToArray(), -8, -38 ) ).ToArray() ).ToArray();
			builder.AddAnimation( slot, frames );
		}
		var data = builder.Build();
		var atlas = new GuestSpriteAtlas( name, new SpriteBankFile( new MemoryStream( data.Files[".TPC"] ) ), new SpriteAnimationFile( new MemoryStream( data.Files[".ESP"] ) ) );
		for ( var i = 0; i < atlas.Frames.Length; i++ ) atlas.Frames[i] = atlas.Frames[i] with { PixelsPerUnitX = (ushort)a, PixelsPerUnitY = (ushort)b };
		return atlas;
	}

	private sealed class Rig
	{
		public readonly ParkEconomy Economy = EconomyTestData.Park();
		public readonly ParkHud Hud;
		public readonly Level Level;
		public readonly StaffRenderer Renderer;
		public readonly GuestSpriteAtlas[] Atlases;
		public readonly StaffSpriteSet Sprites;
		public List<StaffCandidate> Candidates => (List<StaffCandidate>)Economy.Staff.Candidates;
		public List<Texture> Textures => Get<List<Texture>>( Renderer, "textures" );
		public UiSpriteFigure Figure => (UiSpriteFigure)Hud.Stack.Top!.Find( "portraitFigure" )!;
		public Rig( int directions = 5, int a = 128, int b = 128 )
		{
			Candidates.Clear();
			foreach ( var role in Enum.GetValues<StaffType>() )
			{
				for ( var variant = 0; variant < 2; variant++ )
				{
					Candidates.Add( new StaffCandidate( (int)role * 3 + variant + 1, role, variant, variant + 1, 100000, variant ) );
					((List<StaffMember>)Economy.Staff.Members).Add( new StaffMember { Id = 100 + (int)role * 2 + variant, Type = role,
						NameIndex = variant, Grade = 1, Variant = variant, State = StaffState.Working, Job = StaffJob.Sweeping } );
				}
				Candidates.Add( new StaffCandidate( (int)role * 3 + 3, role, 2, 3, 100000, 0 ) );
			}
			Hud = StaffWindowTests.Hud( Economy ); Level = Get<Level>( Hud, "level" ); OpenTPW.Level.Current = Level;
			Atlases = Enumerable.Range( 0, 10 ).Select( index => Atlas( index, directions, a, b ) ).ToArray();
			Sprites = (StaffSpriteSet)Activator.CreateInstance( typeof( StaffSpriteSet ), BindingFlags.Instance | BindingFlags.NonPublic, null,
				new object[] { Atlases, Enumerable.Range( 0, 5 ).Select( role => (role * 2, 2) ).ToArray() }, null )!;
			Renderer = new StaffRenderer( () => Economy.Staff.Members, new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] ),
				StaffSpriteSet.Empty, new StaffPlacement( () => null, () => null, (_, _) => false ) );
			Set( Renderer, "sprites", Sprites ); Textures.AddRange( Atlases.Select( _ => TextureSentinel() ) );
			Set( Level, "<StaffRenderer>k__BackingField", Renderer ); Hud.OpenStaffWindow();
		}
		public void Select( StaffType type, int variant, bool duplicate = false )
		{
			Hud.FilterStaff( type );
			var id = (int)type * 3 + (duplicate ? 3 : variant + 1);
			Hud.SelectStaff( Hud.StaffCandidates.ToList().FindIndex( candidate => candidate.Id == id ) );
		}
		public UiSpriteFrame Expected( StaffType type, int variant, int step )
		{
			var index = (int)type * 2 + variant; var atlas = Atlases[index];
			var mirror = atlas.DirectionsOf( 1 ) == 5; var direction = mirror ? 3 : 5;
			var frame = atlas.Frames[atlas.GetFrame( 1, direction, step )];
			return new UiSpriteFrame( Textures[index].NativeTexture, atlas.Width, atlas.Height, frame.X, frame.Y, frame.Width, frame.Height,
				frame.OriginX, frame.OriginY, mirror, frame.ScaleX, frame.ScaleY );
		}
	}

	private static UiContext Context( int width, int height )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( width, height ) };
	}

	[DataTestMethod]
	[DataRow( 513, 202, 723, 415, 626, 362 )]
	[DataRow( 511, 201, 724, 416, 625, 363 )]
	[DataRow( 10, 20, 23, 35, 24, 32 )]
	[DataRow( 20, 30, 7, 15, 21, 19 )]
	public void IntegerEndpointsUseSignedShiftsBeforeCanvasMapping( int left, int top, int right, int bottom, int x, int y )
	{
		Assert.AreEqual( new N.Vector2( x, y ), HirePortraitGeometry.Feet( left, top, right, bottom ) );
		// In particular, height 213 gives 362, not the floating-quarter result 361.75; there is no visitor clone's -3 Y.
	}

	[DataTestMethod]
	[DataRow( 2048, 1536, 5 )]
	[DataRow( 2048, 1536, 8 )]
	[DataRow( 640, 480, 5 )]
	[DataRow( 1280, 720, 8 )]
	[DataRow( 1920, 1080, 5 )]
	[DataRow( 1000, 1000, 8 )]
	[DataRow( 801, 601, 5 )]
	public void ActualHireCallbackAndDrawUseInnerFeetAndBothFrameDensitiesForEveryRoleAndVariant( int width, int height, int directions )
	{
		foreach ( var (a, b) in new[] { (128, 128), (256, 64) } )
		{
			var rig = new Rig( directions, a, b ); var figure = rig.Figure;
			Assert.AreEqual( new UiRect( 484, 179, 264, 264 ), rig.Hud.Stack.Top!.Find( "staffPicture" )!.Bounds );
			Assert.AreEqual( new UiRect( 513, 202, 210, 213 ), figure.Bounds );
			Assert.AreEqual( new N.Vector2( 626, 362 ), figure.FeetInCanvas!.Value );
			Assert.AreEqual( 2.4f, figure.CanvasUnitsPerPixel ); Assert.AreEqual( UiAnchor.Center, figure.Anchor );
			var save = ParkSaveFile.Serialize( rig.Economy ); var random = rig.Economy.World.State;
			foreach ( var role in Enum.GetValues<StaffType>() )
				for ( var variant = 0; variant < 2; variant++ )
				{
					Time.Now = 10 + (int)role * 2 + variant; rig.Select( role, variant );
					Assert.AreEqual( rig.Expected( role, variant, 0 ), figure.Frame()!.Value );
					Time.Now += 0.2f;
					Assert.AreEqual( rig.Expected( role, variant, 3 ), figure.Frame()!.Value, "the real callback still selects walk slot 1, direction 5 and the role's bank" );
					AssertDraw( figure, width, height, a, b, directions == 5 );
				}
			Assert.AreEqual( save, ParkSaveFile.Serialize( rig.Economy ), "portrait selection and drawing are observational" );
			Assert.AreEqual( random, rig.Economy.World.State );
		}
	}

	private static void AssertDraw( UiSpriteFigure figure, int width, int height, int a, int b, bool mirror )
	{
		var context = Context( width, height ); figure.Draw( context, false, false ); var draw = context.Batch.Draws.Single();
		var frame = figure.Frame()!.Value; Assert.AreEqual( mirror, frame.Mirror );
		Assert.AreEqual( (128f / a, 128f / b), (frame.ScaleX, frame.ScaleY) );
		// Independent arithmetic oracle, without calling the geometry helper, Canvas.Map or Layout under test.
		var scale = MathF.Min( width / 2048f, height / 1536f );
		var feet = new N.Vector2( width / 2f + (626 - 1024) * scale, height / 2f + (362 - 768) * scale );
		var px = 2.4f * scale * 128 / a; var py = 2.4f * scale * 128 / b;
		var left = feet.X + (mirror ? -12 : -8) * px; var top = feet.Y - 38 * py;
		AssertPoint( new N.Vector2( left, top ), draw.Vertices[0].Position );
		AssertPoint( new N.Vector2( left, top + 40 * py ), draw.Vertices[1].Position );
		AssertPoint( new N.Vector2( left + 20 * px, top ), draw.Vertices[2].Position );
		AssertPoint( new N.Vector2( left + 20 * px, top + 40 * py ), draw.Vertices[5].Position );
		AssertPoint( new N.Vector2( (frame.X + (mirror ? 20 : 0)) / (float)frame.AtlasWidth, frame.Y / (float)frame.AtlasHeight ), draw.Vertices[0].TexCoords );
		AssertPoint( new N.Vector2( (frame.X + (mirror ? 0 : 20)) / (float)frame.AtlasWidth, (frame.Y + 40) / (float)frame.AtlasHeight ), draw.Vertices[5].TexCoords );
		Assert.AreSame( frame.Texture, draw.Texture.Rendered ); Assert.AreEqual( 6, draw.Vertices.Count );
	}

	[TestMethod]
	public void HostSelectionSortFilterEmployedExtensionAndReopenKeepTheirExistingClockPolicy()
	{
		var rig = new Rig(); var hud = rig.Hud; var figure = rig.Figure; var screen = hud.Stack.Top;
		var save = ParkSaveFile.Serialize( rig.Economy ); var random = rig.Economy.World.State;
		rig.Select( StaffType.Handyman, 0 );
		Assert.AreEqual( rig.Expected( StaffType.Handyman, 0, 0 ), figure.Frame()!.Value, "existing host frame zero is immediate, not native delayed FRAME parity" );
		Time.Now = 0.2f; rig.Select( StaffType.Handyman, 0, duplicate: true );
		Assert.AreEqual( rig.Expected( StaffType.Handyman, 0, 3 ), figure.Frame()!.Value, "applicant identity is not the phase key" );
		var selected = hud.SelectedCandidateId; hud.SortStaff( true ); hud.SortStaff( true );
		Assert.AreEqual( selected, hud.SelectedCandidateId );
		Assert.AreEqual( rig.Expected( StaffType.Handyman, 0, 3 ), figure.Frame()!.Value );
		Time.Now = 0.4f; rig.Select( StaffType.Handyman, 1 );
		Assert.AreEqual( rig.Expected( StaffType.Handyman, 1, 0 ), figure.Frame()!.Value, "variant changes restart the host loop" );
		Time.Now = 0.6f; rig.Select( StaffType.Mechanic, 1 );
		Assert.AreEqual( rig.Expected( StaffType.Mechanic, 1, 0 ), figure.Frame()!.Value, "role changes restart even with the same variant" );
		Time.Now = 0.8f; rig.Select( StaffType.Handyman, 0 ); figure.Frame();
		Time.Now = 1f; hud.ShowEmployedStaff( true );
		hud.SelectStaff( hud.StaffItems.ToList().FindIndex( member => member.Variant == 0 ) );
		Assert.IsNull( rig.Renderer.EmployeeFrame( hud.SelectedStaff ), "the employed extension does not need a live world publication" );
		Assert.AreEqual( rig.Expected( StaffType.Handyman, 0, 3 ), figure.Frame()!.Value, "same role/variant keeps the hire loop across tabs" );
		AssertDraw( figure, 1920, 1080, 128, 128, true );
		Time.Now = 1.1f; hud.ShowEmployedStaff( false ); rig.Select( StaffType.Handyman, 0 );
		Assert.AreEqual( 0.8f, Get<float>( hud, "portraitStart" ) ); figure.Frame();
		hud.CloseStaffWindow(); Time.Now = 1.2f; hud.OpenStaffWindow();
		Assert.AreSame( screen, hud.Stack.Top ); Assert.AreSame( figure, rig.Figure );
		// Reopen chooses the first sorted applicant; select the same key before observing the callback again.
		rig.Select( StaffType.Handyman, 0 );
		Assert.AreEqual( rig.Expected( StaffType.Handyman, 0, 6 ), figure.Frame()!.Value, "same-key reopen retains existing elapsed time, a qualified native lifecycle difference" );
		Assert.AreEqual( 0.8f, Get<float>( hud, "portraitStart" ) );
		Assert.AreEqual( save, ParkSaveFile.Serialize( rig.Economy ) ); Assert.AreEqual( random, rig.Economy.World.State );
	}

	[TestMethod]
	public void MissingRendererBankTextureAndInvalidSelectionRetainTheExistingHostFallbackPolicy()
	{
		var rig = new Rig(); var figure = rig.Figure; rig.Select( StaffType.Handyman, 0 ); figure.Frame();
		var save = ParkSaveFile.Serialize( rig.Economy ); var random = rig.Economy.World.State;
		void EmptyDraw()
		{
			Assert.IsNull( figure.Frame() ); var context = Context( 1280, 720 ); figure.Draw( context, false, false ); Assert.AreEqual( 0, context.Batch.Draws.Count );
		}
		Time.Now = 0.1f; Set( rig.Level, "<StaffRenderer>k__BackingField", null ); EmptyDraw();
		Set( rig.Level, "<StaffRenderer>k__BackingField", rig.Renderer );
		Time.Now = 0.2f; Assert.AreEqual( rig.Expected( StaffType.Handyman, 0, 3 ), figure.Frame()!.Value );
		Set( rig.Renderer, "sprites", StaffSpriteSet.Empty ); EmptyDraw(); Set( rig.Renderer, "sprites", rig.Sprites );
		var textures = rig.Textures.ToArray(); rig.Textures.Clear(); EmptyDraw(); rig.Textures.AddRange( textures );
		Time.Now = 0.3f; Assert.AreEqual( rig.Expected( StaffType.Handyman, 0, 4 ), figure.Frame()!.Value, "resource absence did not restart the selection key" );
		Set( rig.Hud, "selectedStaffId", -1 ); EmptyDraw();
		Time.Now = 0.4f; rig.Select( StaffType.Handyman, 0 );
		Assert.AreEqual( rig.Expected( StaffType.Handyman, 0, 0 ), figure.Frame()!.Value, "host invalid selection clears the key, unlike native player retention" );
		Assert.AreEqual( save, ParkSaveFile.Serialize( rig.Economy ) ); Assert.AreEqual( random, rig.Economy.World.State );
	}

	private static void AssertPoint( N.Vector2 expected, N.Vector2 actual )
	{
		Assert.AreEqual( expected.X, actual.X, 0.0002f ); Assert.AreEqual( expected.Y, actual.Y, 0.0002f );
	}
}

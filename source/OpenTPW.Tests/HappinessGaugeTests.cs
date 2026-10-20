using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>Real HUD composition and sampling, plus asset-free vertical meter batch geometry. No original art or GPU is required.</summary>
[TestClass, DoNotParallelize]
public class HappinessGaugeTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static void Set( object owner, string field, object? value ) => owner.GetType().GetField( field, Private )!.SetValue( owner, value );
	private static T Get<T>( object owner, string field ) => (T)owner.GetType().GetField( field, Private )!.GetValue( owner )!;
	private sealed class EmptyCatalog : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>();
	}
	private sealed record World( ParkHud Hud, Level Level, ParkEconomy Economy, GuestSimulation Guests, UiContext Context );

	private static World Create( int width = 2048, int height = 1536 )
	{
		var fixture = VisitorWorldSelectionTests.Fixture( out var guests, out _ );
		var level = Get<Level>( fixture, "level" );
		var park = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( park, "<Heightfield>k__BackingField", new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] ) );
		Set( level, "<OriginalPark>k__BackingField", park );
		Set( level, "<PlacedRide>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( PrototypeRide ) ) );
		var economy = EconomyTestData.Park();
		economy.OpenPark();
		var hud = new ParkHud( level, OriginalUiTests.FakeStrings(), new EconomyParkStatus( () => economy ), new EmptyCatalog(), new HudHost() );
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name =>
		{
			var data = Md2ModelFileTests.CreateGeometry();
			Array.Clear( data, 0x1a4, 12 );
			Encoding.ASCII.GetBytes( name + "\0" ).CopyTo( data, 0x1a4 );
			return new UiModel( new ModelFile( new MemoryStream( data ) ), name );
		} ) ) { Canvas = new UiCanvas( width, height ), PopupHelp = false, ResolveTexture = _ => "/synthetic/shell.png" };
		return new( hud, level, economy, guests, context );
	}

	private static UiDraw DrawFill( World w )
	{
		w.Context.Batch.Clear();
		w.Hud.Screen.Find( "gaugeFill" )!.Draw( w.Context, false, false );
		return w.Context.Batch.Draws.Single();
	}

	[DataTestMethod]
	[DataRow( -1, 0f )] [DataRow( 0, 0f )] [DataRow( 1, 1 / 1024f )]
	[DataRow( 256, .25f )] [DataRow( 511, 511 / 1024f )] [DataRow( 512, .5f )]
	[DataRow( 768, .75f )] [DataRow( 1023, 1023 / 1024f )] [DataRow( 1024, 1f )] [DataRow( 2048, 1f )]
	public void MeterCropsBothUprightHalvesAtOneSharedBoundary( int raw, float q )
	{
		var batch = new UiBatch();
		UiVerticalGauge.Draw( batch, new UiRect( 85, 1100, 59, 224 ), raw, ParkHud.HappinessGaugeFill.ArtPath );
		CheckMeter( batch.Draws.Single(), new UiRect( 85, 1100, 59, 224 ), q );
	}

	private static void CheckMeter( UiDraw draw, UiRect bounds, float q )
	{
		Assert.AreEqual( "/ui/textures/meter.wct", draw.Texture.ImagePath );
		Assert.IsNull( draw.Texture.Atlas ); Assert.IsNull( draw.Texture.Rendered );
		var vertices = draw.Vertices;
		Assert.AreEqual( q is 0 or 1 ? 6 : 12, vertices.Count, "zero-height regions submit no triangles" );
		Assert.IsTrue( vertices.All( vertex => vertex.Color.Equals( RgbaByte.White ) ), "the art owns its colour and alpha, without a host tint" );
		var split = bounds.Y + bounds.Height * (1 - q);
		if ( q < 1 ) CheckQuad( vertices.Take( 6 ).ToArray(), new UiRect( bounds.X, bounds.Y, bounds.Width, split - bounds.Y ), new( .5f, 0 ), new( 1, 1 - q ) );
		if ( q > 0 ) CheckQuad( vertices.Skip( q < 1 ? 6 : 0 ).ToArray(), new UiRect( bounds.X, split, bounds.Width, bounds.Bottom - split ), new( 0, 1 - q ), new( .5f, 1 ) );
	}

	private static void CheckQuad( UiVertex[] vertices, UiRect bounds, N.Vector2 uv0, N.Vector2 uv1 )
	{
		Assert.AreEqual( 6, vertices.Length );
		Assert.AreEqual( bounds.X, vertices.Min( vertex => vertex.Position.X ), .0001f );
		Assert.AreEqual( bounds.Right, vertices.Max( vertex => vertex.Position.X ), .0001f );
		Assert.AreEqual( bounds.Y, vertices.Min( vertex => vertex.Position.Y ), .0001f );
		Assert.AreEqual( bounds.Bottom, vertices.Max( vertex => vertex.Position.Y ), .0001f );
		Assert.AreEqual( uv0, vertices[0].TexCoords ); Assert.AreEqual( uv1, vertices[^1].TexCoords );
		Assert.IsTrue( vertices.Where( vertex => vertex.Position.Y == bounds.Y ).All( vertex => vertex.TexCoords.Y == uv0.Y ) );
		Assert.IsTrue( vertices.Where( vertex => vertex.Position.Y == bounds.Bottom ).All( vertex => vertex.TexCoords.Y == uv1.Y ) );
	}

	[DataTestMethod]
	[DataRow( 2048, 1536, 85f, 1100f, 59f, 224f )]
	[DataRow( 1024, 768, 42.5f, 550f, 29.5f, 112f )]
	[DataRow( 1280, 720, 39.84375f, 515.625f, 27.65625f, 105f )]
	[DataRow( 1280, 1024, 53.125f, 751.5f, 36.875f, 140f )]
	public void ActualHudKeepsTheShellAndMapsItsIndependentChildOnce( int width, int height, float x, float y, float w, float h )
	{
		var world = Create( width, height );
		var shell = (UiModelImage)world.Hud.Screen.Find( "gauge" )!;
		var fill = world.Hud.Screen.Find( "gaugeFill" )!;
		Assert.AreEqual( "gauge", shell.Model ); Assert.AreEqual( "help477", shell.CurrentHelp );
		Assert.AreEqual( new UiRect( 67, 1080, 96, 265 ), shell.Bounds );
		Assert.AreEqual( new UiRect( 85, 1100, 59, 224 ), fill.Bounds );
		Assert.AreEqual( UiAnchor.BottomLeft, shell.Anchor ); Assert.AreEqual( UiAnchor.BottomLeft, fill.Anchor );
		Assert.IsFalse( fill.Focusable, "drawing does not acquire an input or help-routing role" );
		Assert.AreEqual( 1, world.Hud.Screen.Elements.Count( element => element.Id == "gaugeFill" ) );
		var bounds = new UiRect( x, y, w, h );
		Assert.AreEqual( bounds, fill.ScreenRect( world.Context.Canvas ) );
		CheckMeter( DrawFill( world ), bounds, 0 );
		foreach ( var (mean, raw) in new[] { (0f, 0), (50f, 511), (100f, 1023), (120f, 1024) } )
		{
			Set( world.Hud, "parkHappiness", mean );
			CheckMeter( DrawFill( world ), bounds, raw / 1024f );
		}
		world.Context.Batch.Clear();
		shell.Draw( world.Context, false, false ); fill.Draw( world.Context, false, false );
		Assert.AreEqual( 2, world.Context.Batch.Draws.Count, "one synthetic shell and one meter; no solid-colour overlay" );
		Assert.AreEqual( "/synthetic/shell.png", world.Context.Batch.Draws[0].Texture.ImagePath );
		Assert.AreEqual( ParkHud.HappinessGaugeFill.ArtPath, world.Context.Batch.Draws[1].Texture.ImagePath );
		var shellVertices = world.Context.Batch.Draws[0].Vertices;
		Assert.AreEqual( shell.ScreenRect( world.Context.Canvas ).X, shellVertices.Min( vertex => vertex.Position.X ), .001f );
		Assert.AreEqual( shell.ScreenRect( world.Context.Canvas ).Bottom, shellVertices.Max( vertex => vertex.Position.Y ), .001f );
	}

	[TestMethod]
	public void RealSamplingKeepsTwoSecondCadenceAndDrawNeverAdvancesTheSimulation()
	{
		var world = Create();
		var guest = new Guest( 1, 0 ) { State = GuestState.WalkingAround, Happiness = 50.9f };
		var outside = new Guest( 2, 0 ) { State = GuestState.WaitingAtBooth, Happiness = 0 };
		Set( world.Guests, "guests", new List<Guest> { guest, outside } );
		var before = (world.Economy.Tick, world.Economy.Balance, world.Economy.Research.Workload, world.Guests.TickCount);
		void Sample( float delta ) => typeof( ParkHud ).GetMethod( "SampleHappiness", Private )!.Invoke( world.Hud, new object[] { delta } );
		Sample( 0 );
		Assert.AreEqual( 50f, world.Hud.ParkHappiness );
		CheckMeter( DrawFill( world ), new( 85, 1100, 59, 224 ), 511 / 1024f );
		guest.Happiness = 100;
		Sample( 1 ); Sample( .75f );
		Assert.AreEqual( 50f, world.Hud.ParkHappiness );
		var snapshot = JsonSerializer.Serialize( world.Guests.CaptureSnapshot() );
		var random = world.Guests.RandomState;
		var sources = new WorldStateSources { Guests = world.Guests, Economy = world.Economy };
		var hash = WorldStateHash.Compute( sources );
		for ( var frame = 0; frame < 3; frame++ ) DrawFill( world );
		Assert.AreEqual( snapshot, JsonSerializer.Serialize( world.Guests.CaptureSnapshot() ) );
		Assert.AreEqual( random, world.Guests.RandomState );
		Assert.AreEqual( hash, WorldStateHash.Compute( sources ), "repeated meter draws cannot change authoritative guest/economy state" );
		Assert.AreEqual( .25f, Get<float>( world.Hud, "happinessSampled" ), "drawing never resamples or spends the timer" );
		Sample( .25f ); Assert.AreEqual( 100f, world.Hud.ParkHappiness );
		CheckMeter( DrawFill( world ), new( 85, 1100, 59, 224 ), 1023 / 1024f );
		world.Economy.ClosePark(); Sample( 2 );
		Assert.IsNull( world.Hud.ParkHappiness ); CheckMeter( DrawFill( world ), new( 85, 1100, 59, 224 ), 0 );
		world.Economy.OpenPark(); guest.State = GuestState.Gone; Sample( 2 );
		Assert.IsNull( world.Hud.ParkHappiness ); CheckMeter( DrawFill( world ), new( 85, 1100, 59, 224 ), 0 );
		guest.State = GuestState.WalkingAround; guest.Happiness = .9f; Sample( 2 );
		Assert.AreEqual( 0f, world.Hud.ParkHappiness ); CheckMeter( DrawFill( world ), new( 85, 1100, 59, 224 ), 0 );
		Set( world.Level, "<Guests>k__BackingField", null ); Sample( 2 );
		Assert.IsNull( world.Hud.ParkHappiness ); CheckMeter( DrawFill( world ), new( 85, 1100, 59, 224 ), 0 );
		Assert.AreEqual( before, (world.Economy.Tick, world.Economy.Balance, world.Economy.Research.Workload, world.Guests.TickCount) );
		Assert.AreEqual( .9f, guest.Happiness ); Assert.AreEqual( 0f, outside.Happiness );
	}

	[DataTestMethod]
	[DataRow( false )] [DataRow( true )]
	public void MissingOrMalformedMeterIsSkippedWithoutInventingArtOrBreakingSampling( bool malformed )
	{
		var previousFiles = FileSystem; var previousPack = TexturePack.Directory;
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-meter-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( directory, "ui", "textures" ) );
		try
		{
			FileSystem = new BaseFileSystem( directory ); TexturePack.Directory = null;
			if ( malformed ) File.WriteAllBytes( Path.Combine( directory, "ui", "textures", "meter.wct" ), new byte[] { 0 } );
			// Only the CPU resource-failure branch is exercised; a GPU would be required if the image incorrectly succeeded.
			var renderer = (UiRenderer)RuntimeHelpers.GetUninitializedObject( typeof( UiRenderer ) );
			Set( renderer, "textures", new Dictionary<object, (Veldrid.Texture, ResourceSet)>() );
			Set( renderer, "failedImages", new HashSet<string>( StringComparer.OrdinalIgnoreCase ) );
			var load = typeof( UiRenderer ).GetMethod( "GetResourceSet", Private )!;
			var texture = UiTexture.Image( ParkHud.HappinessGaugeFill.ArtPath );
			Assert.IsNull( load.Invoke( renderer, new object[] { texture } ) );
			Assert.IsNull( load.Invoke( renderer, new object[] { texture } ), "the failure remains contained on another frame" );
			CollectionAssert.AreEqual( new[] { ParkHud.HappinessGaugeFill.ArtPath }, Get<HashSet<string>>( renderer, "failedImages" ).ToArray() );
			Assert.AreEqual( 0, Get<Dictionary<object, (Veldrid.Texture, ResourceSet)>>( renderer, "textures" ).Count );
			var world = Create();
			typeof( ParkHud ).GetMethod( "SampleHappiness", Private )!.Invoke( world.Hud, new object[] { 2f } );
			CheckMeter( DrawFill( world ), new( 85, 1100, 59, 224 ), 0 );
			world.Context.Batch.Clear(); world.Hud.Screen.Find( "gauge" )!.Draw( world.Context, false, false );
			Assert.AreEqual( "/synthetic/shell.png", world.Context.Batch.Draws.Single().Texture.ImagePath );
			Assert.IsNull( world.Hud.ParkHappiness );
		}
		finally { FileSystem = previousFiles; TexturePack.Directory = previousPack; Directory.Delete( directory, true ); }
	}
}

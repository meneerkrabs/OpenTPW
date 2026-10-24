using System;
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

/// <summary>Synthetic CPU world output and the actual visitor HUD callback/Draw; no graphics device or original media.</summary>
[TestClass, DoNotParallelize]
public class VisitorLivePortraitTests
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
	private static Guest Visitor( int id = 1, int type = 0 ) => new( id, type )
	{
		State = GuestState.WalkingAround, DrawX = 4, DrawY = 4, HeadingX = -1, HeadingY = 0, VisitorNumber = id,
		Money = 100 + id, Happiness = 50,
	};
	private static GuestSimulation Simulation()
	{
		var settings = GuestTests.CreateSettings();
		settings.Types.Clear();
		settings.Types.AddRange( Enumerable.Range( 0, 8 ).Select( index => new GuestType( 40 + index, 300 + index, 30 ) ) );
		return new GuestSimulation( new GuestPathGrid( 32, 32 ), settings, 1 ) { ArrivalsEnabled = false };
	}

	// A constructor-free Vulkan object is only a managed lifetime sentinel. Never Dispose it or call a native operation.
	// Veldrid 4.9 Texture's private-protected DisposeCore prevents an ordinary fake subclass in this assembly.
	private static Texture TextureSentinel()
	{
		var nativeType = typeof( Veldrid.Texture ).Assembly.GetType( "Veldrid.Vk.VkTexture", throwOnError: true )!;
		var texture = Empty<Texture>();
		texture.NativeTexture = (Veldrid.Texture)RuntimeHelpers.GetUninitializedObject( nativeType );
		Assert.IsFalse( texture.NativeTexture.IsDisposed );
		return texture;
	}
	private static void MarkDisposed( Texture texture, bool disposed = true ) => Set( texture.NativeTexture, "_destroyed", disposed );

	private static GuestSpriteAtlas Atlas( int index = 0, int directions = 5 )
	{
		var name = $"SPR_T{index}";
		var builder = new SpriteSetBuilder( name ) { FullDetail = false };
		foreach ( var (slot, count) in new[] { (0, 1), (1, 8), (2, 8), (14, 2) } )
		{
			var frames = Enumerable.Range( 0, directions ).Select( direction => (IReadOnlyList<SpriteRgbaFrame>)Enumerable.Range( 0, count )
				.Select( step => new SpriteRgbaFrame( 3, 5, Enumerable.Range( 0, 15 ).SelectMany( _ => new byte[] { (byte)(30 + index), (byte)(40 + direction), (byte)(50 + step), 255 } ).ToArray(), 2, -4 ) ).ToArray() ).ToArray();
			builder.AddAnimation( slot, frames );
		}
		var data = builder.Build();
		return new GuestSpriteAtlas( name, new SpriteBankFile( new MemoryStream( data.Files[".TPC"] ) ), new SpriteAnimationFile( new MemoryStream( data.Files[".ESP"] ) ) );
	}

	private sealed class Rig
	{
		public readonly GuestSimulation Simulation;
		public readonly Level Level;
		public readonly GuestRenderer Renderer;
		public readonly List<GuestSpriteAtlas> Atlases;
		public List<Guest> Guests => (List<Guest>)Simulation.Guests;
		public List<Texture> Textures => Get<List<Texture>>( Renderer, "textures" );
		public Rig( Guest guest, bool costume = false, int directions = 5, int banks = 3, Level? level = null, GuestSimulation? simulation = null )
		{
			Simulation = simulation ?? VisitorLivePortraitTests.Simulation();
			if ( !Guests.Contains( guest ) ) Guests.Add( guest );
			Level = level ?? Empty<Level>(); OpenTPW.Level.Current = Level;
			Set( Level, "<Guests>k__BackingField", Simulation );
			Atlases = Enumerable.Range( 0, banks ).Select( index => Atlas( index, directions ) ).ToList();
			Renderer = new GuestRenderer( Simulation, new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] ), Array.Empty<GuestSpriteAtlas>() )
			{
				Draws = item => item.InCostume == costume,
				DrawsRideBody = item => item.BodySpriteInCostume == costume,
			};
			Set( Renderer, "atlases", Atlases );
			Set( Renderer, "material", Empty<Material<ObjectUniformBuffer>>() );
			Textures.AddRange( Atlases.Select( _ => TextureSentinel() ) );
			Set( Level, costume ? "<CostumeRenderer>k__BackingField" : "<GuestRenderer>k__BackingField", Renderer );
		}
		public int Prepare( double seconds = 0 )
		{
			Time.Now = (float)seconds; Time.Delta = 0.07f;
			Set( Simulation, "<TimeSeconds>k__BackingField", seconds );
			return Renderer.PrepareWorld();
		}
		public UiSpriteFrame Expected( Guest guest, int slot, int step, float? headingX = null, float? headingY = null )
		{
			var index = GuestAppearance.KidSet( guest.Id, Atlases.Count ); var atlas = Atlases[index];
			var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( headingX ?? guest.HeadingX, headingY ?? guest.HeadingY, 0f, atlas.DirectionsOf( slot ) );
			var frame = atlas.Frames[atlas.GetFrame( slot, direction, step % atlas.FramesPerDirection( slot ) )];
			return new UiSpriteFrame( Textures[index].NativeTexture, atlas.Width, atlas.Height, frame.X, frame.Y, frame.Width, frame.Height,
				frame.OriginX, frame.OriginY, mirror, frame.ScaleX, frame.ScaleY );
		}
	}

	// No original media, so no UI image resolves. A button without its model falls back to the purple_button art, and the default resolver would list
	// /ui/textures through the global game-data FileSystem: null when this class runs alone, otherwise whatever an earlier test left there, and a
	// listing made from that stays cached for the later tests until UiImages.ResetListings.
	private static UiContext Context( float delta = 0, int width = 1000, int height = 1000 )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( width, height ), Delta = delta, ResolveTexture = _ => null };
	}
	private static (ParkHud Hud, Rig Rig) HudRig( params Guest[] guests )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var list = (List<Guest>)simulation.Guests; list.Clear(); list.AddRange( guests );
		return (hud, new Rig( guests[0], level: Get<Level>( hud, "level" ), simulation: simulation ));
	}
	private static UiSpriteFigure Figure( ParkHud hud ) => (UiSpriteFigure)hud.Stack.Top!.Find( "portraitFigure" )!;
	private static Action Arrow( ParkHud hud, bool forward ) => ((UiButton)hud.Stack.Top!.Find( forward ? "nextVisitor" : "previousVisitor" )!).Clicked!;
	private static void Update( ParkHud hud, float delta = 0 ) => hud.Stack.Top!.Update( Context( delta ), UiInput.Idle( N.Vector2.One ) );

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void EveryPeepTypeAndIdDerivedBankUsesItsActualWalkingWaitingAndStandingOutput( bool costume )
	{
		var ids = Enumerable.Range( 1, 100 ).GroupBy( id => GuestAppearance.KidSet( id, 3 ) ).Select( group => group.First() ).ToArray();
		Assert.AreEqual( 3, ids.Length, "the fixture exercises every synthetic bank" );
		foreach ( var type in Enumerable.Range( 0, 8 ) )
			foreach ( var id in ids )
			{
				var guest = Visitor( id, type ); guest.InCostume = costume;
				var rig = new Rig( guest, costume );
				Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
				guest.IsMoving = true; guest.DrawDistanceWalked = 5.25f / GuestRenderer.WalkFramesPerCell;
				Assert.AreEqual( 12, rig.Prepare( 2.25 ) );
				Assert.AreEqual( rig.Expected( guest, 1, 5 ), rig.Renderer.VisitorFrame( guest )!.Value, $"type {type}, id {id}, costume {costume}" );
				var selected = rig.Renderer.SelectSprite( guest, N.Vector3.UnitX, -N.Vector3.UnitZ );
				Assert.AreEqual( GuestAppearance.KidSet( id, 3 ), selected.Atlas );
				Assert.AreEqual( selected.Atlas, Get<Vertex[]>( rig.Renderer, "vertices" )[0].TexIndex, "published source was prepared into the real world quad" );
				guest.IsMoving = false;
				foreach ( var state in new[] { GuestState.Queueing, GuestState.WaitingToBoard, GuestState.WaitingAtBooth, GuestState.AtTicketBooth, GuestState.WaitingToGoHome } )
				{
					guest.State = state; rig.Prepare( 2.25 );
					Assert.AreEqual( rig.Expected( guest, 14, (int)(2.25 * 2 + id) % 2 ), rig.Renderer.VisitorFrame( guest )!.Value, state.ToString() );
				}
				guest.State = GuestState.DoingNothing; rig.Prepare( 2.25 );
				Assert.AreEqual( rig.Expected( guest, 0, 0 ), rig.Renderer.VisitorFrame( guest )!.Value );
				Assert.AreEqual( 1, rig.Renderer.DrawnGuests );
			}
	}

	/// <summary>
	/// A guest whose object's UsageInfo.RideHandlesSprite kept its body (tp.exe 0x0050212B, which tests no object kind, and 0x004FA087) is drawn standing where it stands while it uses the object, whether
	/// a shop, a sideshow, a toilet or a ride without a pose; an object that deletes the body and a pools changing room draw nothing.
	/// </summary>
	[TestMethod]
	public void AGuestWhoseBodyTheObjectKeepsIsDrawnStandingWhateverTheObjectsKind()
	{
		foreach ( var (kind, keeps, changingRoom, drawn) in new[] { (RideVisitorKind.Shop, true, false, 1), (RideVisitorKind.Shop, false, false, 0), (RideVisitorKind.Sideshow, true, false, 1), (RideVisitorKind.Sideshow, false, false, 0),
			(RideVisitorKind.Facility, true, false, 1), (RideVisitorKind.Facility, false, false, 0), (RideVisitorKind.Ride, true, false, 1), (RideVisitorKind.Ride, false, false, 0), (RideVisitorKind.Shop, true, true, 0) } )
		{
			var guest = Visitor();
			var rig = new Rig( guest );
			var shop = new RideVisitorBridge( 7, "Kept body", kind, 1, 0, 0 ) { EntranceCell = (4, 4), ExitCell = (4, 4), RideHandlesSprite = keeps, IsChangingRoom = changingRoom };
			rig.Simulation.Register( shop );
			guest.State = GuestState.Using;
			guest.AttractionId = shop.AttractionId;
			if ( keeps )
				guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Stand, 0 );
			Assert.AreEqual( drawn == 1, rig.Simulation.ShowsKeptBody( guest ), $"{kind}, keeps {keeps}, changing room {changingRoom}" );
			rig.Prepare( 2.25 );
			Assert.AreEqual( drawn, rig.Renderer.DrawnGuests, $"{kind}, keeps {keeps}, changing room {changingRoom}" );
		}
	}

	/// <summary>
	/// A kept body stays where the walk update or the bounce pass last placed it once its entry is gone (tp.exe's sprite pass 0x00542010 draws a sprite at its stored position; only 0x004F9E60 and, after the
	/// state-15 entry, 0x004FA087 store it), with its player's frames, until the guest is let off or the body is deleted.
	/// </summary>
	[TestMethod]
	public void AKeptBodyStaysWhereItsWalkOrBounceLastPlacedIt()
	{
		foreach ( var kind in new[] { RideVisitorKind.Sideshow, RideVisitorKind.Shop, RideVisitorKind.Facility, RideVisitorKind.Ride } )
		{
			var guest = Visitor();
			var rig = new Rig( guest );
			Get<Dictionary<int, Guest>>( rig.Simulation, "byId" )[guest.Id] = guest;
			var bridge = new RideVisitorBridge( 7, "Kept body", kind, 1, 0, 0 ) { EntranceCell = (4, 4), ExitCell = (4, 4), RideHandlesSprite = true };
			rig.Simulation.Register( bridge );
			guest.State = GuestState.Using;
			guest.AttractionId = bridge.AttractionId;
			guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Walk, 0 ); guest.BodySprite.Tick( 63 );
			var output = guest.BodySprite.Player!.Output!.Value;
			var walked = new BounceRiderPose( guest.Id, bridge.AttractionId, new N.Vector3( 3, 4, 5 ), 6, false, 2, GuestSpriteAtlas.WalkSlot );
			var placed = new Dictionary<int, BounceRiderPose> { [guest.Id] = walked };
			var prepared = new Dictionary<int, BounceRiderPose>();
			FlatRideBounceRiders.HoldKeptBodies( prepared, placed, rig.Simulation );
			Assert.AreEqual( walked with { Slot = output.Slot, Step = output.Frame, UsesSpriteState = true }, prepared[guest.Id], $"{kind}: held where it was placed, with the player's frame" );
			Assert.AreEqual( prepared[guest.Id], placed[guest.Id], "the next pass holds it again" );
			prepared.Clear();
			FlatRideBounceRiders.HoldKeptBodies( prepared, placed, rig.Simulation );
			Assert.AreEqual( new N.Vector3( 3, 4, 5 ), prepared[guest.Id].Feet, $"{kind}: still held" );
			var posed = walked with { Feet = new N.Vector3( 7, 8, 9 ) };
			prepared.Clear(); prepared[guest.Id] = posed;
			FlatRideBounceRiders.HoldKeptBodies( prepared, placed, rig.Simulation );
			Assert.AreEqual( posed, prepared[guest.Id], "a pose of this pass wins" );
			prepared.Clear();
			guest.State = GuestState.ExitingRide;
			FlatRideBounceRiders.HoldKeptBodies( prepared, placed, rig.Simulation );
			Assert.AreEqual( 0, prepared.Count, $"{kind}: the let-off ends the hold" );
			Assert.AreEqual( 0, placed.Count );
			guest.State = GuestState.Using;
			placed[guest.Id] = walked;
			guest.BodySprite.DeletePlayer();
			FlatRideBounceRiders.HoldKeptBodies( prepared, placed, rig.Simulation );
			Assert.AreEqual( 0, prepared.Count, $"{kind}: a deleted body is not held" );
		}
	}

	/// <summary>A body the object does not keep is never held, and a pose of another attraction is not this guest's.</summary>
	[TestMethod]
	public void ABodyTheObjectDoesNotKeepOrAnotherAttractionsPoseIsNotHeld()
	{
		var guest = Visitor();
		var rig = new Rig( guest );
		Get<Dictionary<int, Guest>>( rig.Simulation, "byId" )[guest.Id] = guest;
		var bridge = new RideVisitorBridge( 7, "Deletes", RideVisitorKind.Sideshow, 1, 0, 0 ) { EntranceCell = (4, 4), ExitCell = (4, 4), RideHandlesSprite = false };
		rig.Simulation.Register( bridge );
		guest.State = GuestState.Using;
		guest.AttractionId = bridge.AttractionId;
		guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Walk, 0 );
		var prepared = new Dictionary<int, BounceRiderPose>();
		var placed = new Dictionary<int, BounceRiderPose> { [guest.Id] = new BounceRiderPose( guest.Id, bridge.AttractionId, N.Vector3.One, 0, false, 0 ) };
		FlatRideBounceRiders.HoldKeptBodies( prepared, placed, rig.Simulation );
		Assert.AreEqual( 0, prepared.Count );
		bridge.RideHandlesSprite = true;
		placed[guest.Id] = new BounceRiderPose( guest.Id, 99, N.Vector3.One, 0, false, 0 );
		FlatRideBounceRiders.HoldKeptBodies( prepared, placed, rig.Simulation );
		Assert.AreEqual( 0, prepared.Count );
	}

	/// <summary>What a guest holds (umbrella, warm items) mounts on a kept body that stands where it stood, and not on one a ride pose draws, as for any rider.</summary>
	[TestMethod]
	public void AKeptBodyStandingInPlaceMountsAndAPosedOneDoesNot()
	{
		var guest = Visitor();
		var rig = new Rig( guest );
		var bridge = new RideVisitorBridge( 7, "Kept body", RideVisitorKind.Sideshow, 1, 0, 0 ) { EntranceCell = (4, 4), ExitCell = (4, 4), RideHandlesSprite = true };
		rig.Simulation.Register( bridge );
		guest.State = GuestState.Using;
		guest.AttractionId = bridge.AttractionId;
		guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Stand, 0 );
		var (right, up, forward, eye) = (N.Vector3.UnitX, N.Vector3.UnitZ, N.Vector3.UnitY, new N.Vector3( 0, -50, 50 ));
		Assert.IsTrue( rig.Renderer.TryMount( guest, right, up, forward, eye, out _ ), "standing in place" );
		var source = new RiderSource { Pose = new BounceRiderPose( guest.Id, bridge.AttractionId, N.Vector3.Zero, 6, false, 0, GuestSpriteAtlas.WalkSlot ) };
		rig.Renderer.BounceRiders = source;
		Assert.IsFalse( rig.Renderer.TryMount( guest, right, up, forward, eye, out _ ), "posed by the walk" );
		source.Pose = source.Pose with { AttractionId = 99 };
		Assert.IsTrue( rig.Renderer.TryMount( guest, right, up, forward, eye, out _ ), "another attraction's pose is not this guest's" );
		guest.BodySprite.DeletePlayer();
		rig.Renderer.BounceRiders = null;
		Assert.IsFalse( rig.Renderer.TryMount( guest, right, up, forward, eye, out _ ), "deleted" );
	}

	/// <summary>A kept body the script deleted (the hidden walk actions 1, 2 and 4, 0x004F9ED0) has no player and is not drawn, whatever the object.</summary>
	[TestMethod]
	public void AKeptBodyTheScriptDeletedIsNotDrawn()
	{
		foreach ( var kind in new[] { RideVisitorKind.Shop, RideVisitorKind.Sideshow, RideVisitorKind.Facility } )
		{
			var guest = Visitor();
			var rig = new Rig( guest );
			var shop = new RideVisitorBridge( 7, "Kept body", kind, 1, 0, 0 ) { EntranceCell = (4, 4), ExitCell = (4, 4), RideHandlesSprite = true };
			rig.Simulation.Register( shop );
			guest.State = GuestState.Using;
			guest.AttractionId = shop.AttractionId;
			guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Stand, 0 );
			guest.BodySprite.DeletePlayer();
			Assert.IsFalse( rig.Simulation.ShowsKeptBody( guest ), kind.ToString() );
			rig.Prepare( 2.25 );
			Assert.AreEqual( 0, rig.Renderer.DrawnGuests, kind.ToString() );
		}
	}

	[TestMethod]
	public void SelectionAloneAndMissingFirstOutputNeverPublishButTheFirstPreparedQuadIsImmediatelyReadable()
	{
		var guest = Visitor(); var (hud, rig) = HudRig( guest );
		Assert.IsTrue( hud.OpenVisitorInfoWindow( guest ) ); var figure = Figure( hud );
		Assert.IsNull( figure.Frame() );
		var context = Context(); figure.Draw( context, false, false ); Assert.AreEqual( 0, context.Batch.Draws.Count );
		_ = rig.Renderer.SelectSprite( guest, N.Vector3.UnitX, N.Vector3.UnitY );
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ), "selection is not successful world preparation" );
		rig.Renderer.Visible = false; Assert.AreEqual( 0, rig.Prepare() ); rig.Renderer.Visible = true;
		Assert.IsNull( figure.Frame(), "restoring visibility cannot manufacture a source" );
		Assert.AreEqual( 12, rig.Prepare() );
		Assert.AreEqual( rig.Expected( guest, 0, 0 ), figure.Frame()!.Value, "world-before-HUD is visible without an intervening UI Update" );
		figure.Draw( context, false, false ); Assert.AreEqual( 6, context.Batch.Draws.Single().Vertices.Count );
	}

	[DataTestMethod]
	[DataRow( 3 )]
	[DataRow( 5 )]
	[DataRow( 8 )]
	public void PortraitResolvesCapturedHeadingAtLiteralYawZeroWithLimitedBankMirroring( int directions )
	{
		var guest = Visitor(); guest.IsMoving = true; guest.DrawDistanceWalked = 5.25f / GuestRenderer.WalkFramesPerCell;
		var rig = new Rig( guest, directions: directions ); rig.Prepare();
		var expected = rig.Expected( guest, 1, 5 );
		var before = Get<Vertex[]>( rig.Renderer, "vertices" )[0].TexCoords;
		Assert.AreEqual( directions < 8, expected.Mirror );
		Assert.AreEqual( expected, rig.Renderer.VisitorFrame( guest )!.Value );
		Camera.ViewMatrix = N.Matrix4x4.CreateRotationZ( -MathF.PI / 2 ); rig.Prepare();
		Assert.AreNotEqual( before, Get<Vertex[]>( rig.Renderer, "vertices" )[0].TexCoords );
		Assert.AreEqual( expected, rig.Renderer.VisitorFrame( guest )!.Value );
		guest.HeadingX = 1; guest.HeadingY = 0; guest.DrawDistanceWalked = 6.25f / GuestRenderer.WalkFramesPerCell;
		Assert.AreEqual( expected, rig.Renderer.VisitorFrame( guest )!.Value, "reads retain the heading and phase of the latest prepared output" );
		rig.Prepare(); Assert.AreEqual( rig.Expected( guest, 1, 6 ), rig.Renderer.VisitorFrame( guest )!.Value );
	}

	[TestMethod]
	public void SameLookingVisitorsKeepDifferentPhasesAndExactReferencesCannotBeReplacedByReusedIds()
	{
		var first = Visitor(); var secondId = Enumerable.Range( 2, 100 ).First( id => GuestAppearance.KidSet( id, 3 ) == GuestAppearance.KidSet( first.Id, 3 ) );
		var second = Visitor( secondId ); first.IsMoving = second.IsMoving = true;
		first.DrawDistanceWalked = 2.25f / GuestRenderer.WalkFramesPerCell; second.DrawDistanceWalked = 6.25f / GuestRenderer.WalkFramesPerCell;
		var rig = new Rig( first ); rig.Guests.Add( second ); Assert.AreEqual( 24, rig.Prepare() );
		Assert.AreEqual( rig.Expected( first, 1, 2 ), rig.Renderer.VisitorFrame( first )!.Value );
		Assert.AreEqual( rig.Expected( second, 1, 6 ), rig.Renderer.VisitorFrame( second )!.Value );
		Assert.AreNotEqual( rig.Renderer.VisitorFrame( first ), rig.Renderer.VisitorFrame( second ) );
		var replacement = Visitor( first.Id ); rig.Guests.Remove( first ); rig.Guests.Add( replacement );
		Assert.IsNull( rig.Renderer.VisitorFrame( first ) ); Assert.IsNull( rig.Renderer.VisitorFrame( replacement ) );
		rig.Prepare(); Assert.AreEqual( rig.Expected( replacement, 0, 0 ), rig.Renderer.VisitorFrame( replacement )!.Value );
		Assert.IsNull( rig.Renderer.VisitorFrame( first ) );
	}

	[DataTestMethod]
	[DataRow( "hidden" )]
	[DataRow( "material" )]
	[DataRow( "bank" )]
	[DataRow( "texture" )]
	[DataRow( "disposed" )]
	[DataRow( "held" )]
	[DataRow( "gone" )]
	[DataRow( "costume" )]
	public void UnavailableSourcesInvalidateBeforeAndAfterSkippedPreparation( string reason )
	{
		var guest = Visitor(); var rig = new Rig( guest ); rig.Prepare();
		var material = Get<object>( rig.Renderer, "material" ); var textures = rig.Textures.ToArray();
		Assert.IsNotNull( rig.Renderer.VisitorFrame( guest ) );
		switch ( reason )
		{
			case "hidden": rig.Renderer.Visible = false; break;
			case "material": Set( rig.Renderer, "material", null ); break;
			case "bank": Set( rig.Renderer, "atlases", Array.Empty<GuestSpriteAtlas>() ); break;
			case "texture": rig.Textures.Clear(); break;
			case "disposed": foreach ( var texture in rig.Textures ) MarkDisposed( texture ); break;
			case "held": guest.State = GuestState.HeldByGuard; break;
			case "gone": guest.State = GuestState.Gone; break;
			case "costume": guest.InCostume = true; break;
		}
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ), "current source eligibility is checked before another world pass" );
		rig.Prepare();
		rig.Renderer.Visible = true; Set( rig.Renderer, "material", material ); Set( rig.Renderer, "atlases", rig.Atlases );
		if ( rig.Textures.Count == 0 ) rig.Textures.AddRange( textures );
		foreach ( var texture in textures ) MarkDisposed( texture, false );
		guest.State = GuestState.WalkingAround; guest.InCostume = false;
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ), "restoring a source cannot resurrect an older renderer pass" );
		rig.Prepare(); Assert.IsNotNull( rig.Renderer.VisitorFrame( guest ) );
	}

	[TestMethod]
	public void OwningLevelSimulationRegistrationAndEntirelyOmittedWorldPassesRejectOldOutput()
	{
		var guest = Visitor(); var rig = new Rig( guest ); rig.Prepare();
		Level.Current = Empty<Level>(); Assert.IsNull( rig.Renderer.VisitorFrame( guest ) ); Level.Current = rig.Level;
		rig.Renderer.Level = Empty<Level>(); Assert.IsNull( rig.Renderer.VisitorFrame( guest ) ); rig.Renderer.Level = rig.Level;
		Set( rig.Level, "<Guests>k__BackingField", Simulation() ); Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
		Set( rig.Level, "<Guests>k__BackingField", rig.Simulation );
		Entity.All.Remove( rig.Renderer ); Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
		rig.Level.Render(); Entity.All.Add( rig.Renderer );
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ), "re-registration cannot reuse an omitted earlier level pass" );
		rig.Prepare(); Assert.IsNotNull( rig.Renderer.VisitorFrame( guest ) );
		rig.Renderer.Visible = false; rig.Level.Render(); rig.Renderer.Visible = true;
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ), "the actual OnRender early return also invalidates" );
		rig.Prepare(); Assert.IsNotNull( rig.Renderer.VisitorFrame( guest ) );
		var replacement = new Rig( guest, level: rig.Level, simulation: rig.Simulation );
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ) ); Assert.IsNull( replacement.Renderer.VisitorFrame( guest ) );
		replacement.Prepare(); Assert.IsNotNull( replacement.Renderer.VisitorFrame( guest ) );
	}

	[TestMethod]
	public void CategoryChangesMoveBetweenRegisteredKidAndCostumeSourcesWithoutBorrowingOldOutput()
	{
		var guest = Visitor(); var kids = new Rig( guest ); var costumes = new Rig( guest, costume: true, level: kids.Level, simulation: kids.Simulation );
		kids.Prepare(); costumes.Prepare(); Assert.IsNotNull( kids.Renderer.VisitorFrame( guest ) ); Assert.IsNull( costumes.Renderer.VisitorFrame( guest ) );
		guest.InCostume = true;
		Assert.IsNull( kids.Renderer.VisitorFrame( guest ) ); Assert.IsNull( costumes.Renderer.VisitorFrame( guest ) );
		kids.Prepare(); costumes.Prepare();
		Assert.IsNull( kids.Renderer.VisitorFrame( guest ) ); Assert.AreEqual( costumes.Expected( guest, 0, 0 ), costumes.Renderer.VisitorFrame( guest )!.Value );
		guest.InCostume = false;
		Assert.IsNull( costumes.Renderer.VisitorFrame( guest ) ); Assert.IsNull( kids.Renderer.VisitorFrame( guest ) );
		kids.Prepare(); Assert.IsNotNull( kids.Renderer.VisitorFrame( guest ) );
	}

	[TestMethod]
	public void PartialAtlasReloadInvalidatesAllPublicationsBeforeTheFirstReplacementAndDeletionDetachesResources()
	{
		var first = Visitor(); var rig = new Rig( first );
		var second = Visitor( Enumerable.Range( 2, 100 ).First( id => GuestAppearance.KidSet( id, 3 ) != GuestAppearance.KidSet( first.Id, 3 ) ) );
		rig.Guests.Add( second ); rig.Prepare();
		var firstIndex = GuestAppearance.KidSet( first.Id, 3 ); var old = rig.Textures[firstIndex]; var replacement = TextureSentinel();
		var replace = typeof( GuestRenderer ).GetMethod( "ReplaceAtlas", BindingFlags.Instance | BindingFlags.NonPublic )!;
		Assert.AreSame( old, replace.Invoke( rig.Renderer, new object[] { firstIndex, replacement } ) ); MarkDisposed( old );
		Assert.IsNull( rig.Renderer.VisitorFrame( first ) );
		Assert.IsNull( rig.Renderer.VisitorFrame( second ), "even an unchanged atlas cannot cross the partial-reload generation" );
		rig.Prepare(); Assert.AreSame( replacement.NativeTexture, rig.Renderer.VisitorFrame( first )!.Value.Texture );
		Assert.IsNotNull( rig.Renderer.VisitorFrame( second ) );
		var atlas = rig.Atlases[firstIndex]; rig.Atlases[firstIndex] = Atlas( firstIndex );
		Assert.IsNull( rig.Renderer.VisitorFrame( first ), "same index with a different bank is not the captured resource" );
		rig.Atlases[firstIndex] = atlas; rig.Prepare();
		Set( rig.Renderer, "material", null ); Global.Render = Empty<Renderer>(); Set( Global.Render, "_deletionQueue", new List<Action>() );
		rig.Renderer.Delete(); MarkDisposed( replacement );
		Assert.AreEqual( 0, rig.Textures.Count ); Assert.AreEqual( 1, Get<List<Action>>( Global.Render, "_deletionQueue" ).Count );
		Assert.IsNull( rig.Renderer.VisitorFrame( first ) );
		Entity.All.Add( rig.Renderer ); Assert.IsNull( rig.Renderer.VisitorFrame( first ), "a deleted renderer cannot be re-registered into availability" );
	}

	[DataTestMethod]
	[DataRow( "x" )]
	[DataRow( "y" )]
	[DataRow( "headingX" )]
	[DataRow( "headingY" )]
	[DataRow( "distance" )]
	[DataRow( "negativeDistance" )]
	[DataRow( "frames" )]
	public void MalformedGuestOrFrameDataIsRejectedByTheObserver( string reason )
	{
		var guest = Visitor(); guest.IsMoving = true; var rig = new Rig( guest ); rig.Prepare();
		switch ( reason )
		{
			case "x": guest.DrawX = float.NaN; break;
			case "y": guest.DrawY = float.PositiveInfinity; break;
			case "headingX": guest.HeadingX = float.NaN; break;
			case "headingY": guest.HeadingY = float.NegativeInfinity; break;
			case "distance": guest.DrawDistanceWalked = float.NaN; break;
			case "negativeDistance": guest.DrawDistanceWalked = -100; break;
			case "frames": foreach ( var atlas in rig.Atlases ) Set( atlas, "<Frames>k__BackingField", Array.Empty<GuestAtlasFrame>() ); break;
		}
		for ( var index = 0; index < 20; index++ ) Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
		// The observer safely rejects malformed data without exercising or changing legacy world-selection behavior.
	}

	[DataTestMethod]
	[DataRow( "slot" )]
	[DataRow( "feet" )]
	[DataRow( "depth" )]
	[DataRow( "pullSlots" )]
	[DataRow( "exact" )]
	[DataRow( "rotation" )]
	[DataRow( "bottom" )]
	[DataRow( "swimFallback" )]
	public void SpecializedPresentationSourcesAreExcludedAndUiReadsNeverCallTheirDelegates( string hook )
	{
		var guest = Visitor(); var rig = new Rig( guest ); var calls = 0;
		switch ( hook )
		{
			case "slot": Set( rig.Renderer, "<SlotChooser>k__BackingField", (Func<Guest, (int Slot, int Step)?>)(_ => { calls++; return (1, 3); }) ); break;
			case "feet": Set( rig.Renderer, "<FeetChooser>k__BackingField", (Func<Guest, N.Vector3>)(_ => { calls++; return N.Vector3.Zero; }) ); break;
			case "depth": Set( rig.Renderer, "<DepthPullChooser>k__BackingField", (Func<Guest, float>)(_ => { calls++; return 0; }) ); break;
			case "pullSlots": Set( rig.Renderer, "<DepthPulledSlots>k__BackingField", (Func<int, bool>)(_ => { calls++; return true; }) ); break;
			case "exact": Set( rig.Renderer, "<ExactDirections>k__BackingField", true ); break;
			case "rotation": Set( rig.Renderer, "<RotatedSlots>k__BackingField", new HashSet<int> { 0 } ); break;
			case "bottom": Set( rig.Renderer, "<BottomAnchoredSlots>k__BackingField", new HashSet<int> { 0 } ); break;
			case "swimFallback": Set( rig.Renderer, "<SwimSlotFallback>k__BackingField", true ); break;
		}
		Assert.AreEqual( 12, rig.Prepare(), "the specialized world renderer still works" );
		var afterWorld = calls;
		for ( var index = 0; index < 20; index++ ) Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
		Assert.AreEqual( afterWorld, calls, "an observational UI read cannot run arbitrary presentation callbacks" );
	}

	private sealed class RiderSource : IBounceRiderSource
	{
		public BounceRiderPose Pose;
		public int CollectCalls;
		public void Collect( Dictionary<int, BounceRiderPose> poses ) { CollectCalls++; poses[Pose.Visitor] = Pose; }
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void BothRideBodySourcesRemainExcludedAndPortraitReadsDoNotCollectOrRequestBodies( bool stateful )
	{
		var guest = Visitor(); guest.State = GuestState.Using; guest.AttractionId = 77;
		var source = new RiderSource { Pose = new BounceRiderPose( guest.Id, 77, N.Vector3.Zero, 6, false, 4, 2, true, stateful ) };
		var rig = new Rig( guest ); rig.Renderer.BounceRiders = source;
		guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Run, 0 ); guest.BodySprite.Tick( 63 );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Cheer ); guest.BodySprite.QueueInterval( 90 );
		var body = guest.BodySprite.CaptureSnapshot();
		Assert.AreEqual( 12, rig.Prepare() ); Assert.AreEqual( 1, rig.Renderer.DrawnGuests );
		var calls = source.CollectCalls;
		for ( var index = 0; index < 30; index++ ) Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
		Assert.AreEqual( calls, source.CollectCalls ); Assert.AreEqual( body, guest.BodySprite.CaptureSnapshot() );
		guest.AttractionId = 88; Assert.AreEqual( 0, rig.Prepare() ); Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
	}

	[DataTestMethod]
	[DataRow( GuestState.Swimming )]
	[DataRow( GuestState.ClimbingOut )]
	[DataRow( GuestState.ChangingOut )]
	[DataRow( GuestState.PoolActivity )]
	public void PoolSourcesCannotBecomeOrdinaryVisitorPortraits( GuestState state )
	{
		var guest = Visitor(); var rig = new Rig( guest ); rig.Prepare(); guest.State = state;
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ) ); rig.Prepare(); Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
	}

	[TestMethod]
	public void PresentationOnlyFiguresAndUnregisteredRenderersCannotPublishVisitorBodies()
	{
		var guest = Visitor(); guest.State = GuestState.HeldByGuard; var rig = new Rig( guest );
		rig.Renderer.Figures = () => new[] { new SpriteFigure( 4, 4, -1, 0, 0.25f, true, guest.Id ) };
		Assert.AreEqual( 12, rig.Prepare() ); Assert.AreEqual( 0, rig.Renderer.DrawnGuests ); Assert.AreEqual( 1, rig.Renderer.DrawnFigures );
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
		guest.State = GuestState.WalkingAround;
		Set( rig.Level, "<GuestRenderer>k__BackingField", null ); rig.Prepare();
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ), "an extension renderer is not a registered ordinary body source" );
	}

	[TestMethod]
	public void RepeatedActualHudReadsAndDrawsPreserveSnapshotsRandomBodyRequestsPoseCacheAndRenderCounters()
	{
		var guest = Visitor(); guest.IsMoving = true; guest.DrawDistanceWalked = 3.25f / GuestRenderer.WalkFramesPerCell;
		var (hud, rig) = HudRig( guest );
		guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Walk, 0 ); guest.BodySprite.Tick( 63 );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Cheer ); guest.BodySprite.QueueInterval( 123 );
		var draws = 0;
		Set( rig.Renderer, "<Draws>k__BackingField", (Func<Guest, bool>)(_ => { draws++; return true; }) );
		rig.Prepare( 2.25 ); hud.OpenVisitorInfoWindow( guest ); var figure = Figure( hud );
		var snapshot = JsonSerializer.Serialize( rig.Simulation.CaptureSnapshot() ); var hash = rig.Simulation.ComputeStateHash(); var random = rig.Simulation.RandomState;
		var body = guest.BodySprite.CaptureSnapshot(); var vertices = Get<Vertex[]>( rig.Renderer, "vertices" ).Take( 12 ).ToArray();
		var counters = (rig.Renderer.DrawnGuests, rig.Renderer.DrawnFigures, rig.Level.RenderPass, Get<long>( rig.Renderer, "rendererPass" ), Get<long>( rig.Renderer, "assetGeneration" ));
		var poses = Get<Dictionary<int, BounceRiderPose>>( rig.Renderer, "bounceRiders" ).ToArray(); var afterWorld = draws;
		var frame = figure.Frame(); Assert.IsNotNull( frame ); var context = Context();
		for ( var index = 0; index < 100; index++ )
		{
			Assert.AreEqual( frame, figure.Frame() ); context.Batch.Clear(); figure.Draw( context, false, false );
			Assert.AreEqual( 6, context.Batch.Draws.Single().Vertices.Count );
		}
		Assert.AreEqual( afterWorld, draws ); Assert.AreEqual( body, guest.BodySprite.CaptureSnapshot() );
		Assert.AreEqual( snapshot, JsonSerializer.Serialize( rig.Simulation.CaptureSnapshot() ) ); Assert.AreEqual( hash, rig.Simulation.ComputeStateHash() ); Assert.AreEqual( random, rig.Simulation.RandomState );
		Assert.AreEqual( counters, (rig.Renderer.DrawnGuests, rig.Renderer.DrawnFigures, rig.Level.RenderPass, Get<long>( rig.Renderer, "rendererPass" ), Get<long>( rig.Renderer, "assetGeneration" )) );
		CollectionAssert.AreEqual( poses, Get<Dictionary<int, BounceRiderPose>>( rig.Renderer, "bounceRiders" ).ToArray() );
		CollectionAssert.AreEqual( vertices, Get<Vertex[]>( rig.Renderer, "vertices" ).Take( 12 ).ToArray() );
		rig.Prepare( 2.25 ); CollectionAssert.AreEqual( vertices, Get<Vertex[]>( rig.Renderer, "vertices" ).Take( 12 ).ToArray(), "the next world output is unchanged" );
		Assert.AreEqual( body, guest.BodySprite.CaptureSnapshot() );
	}

	[TestMethod]
	public void ActualArrowsInvalidateOldSessionCallbacksWhileTheFourSecondTimerRefreshesOnlyStatistics()
	{
		var first = Visitor( 2 ); var second = Visitor( 9 ); first.IsMoving = second.IsMoving = true;
		first.DrawDistanceWalked = 2.25f / GuestRenderer.WalkFramesPerCell; second.DrawDistanceWalked = 6.25f / GuestRenderer.WalkFramesPerCell;
		var (hud, rig) = HudRig( first, second ); rig.Simulation.Things.Restore( new[] { new ThingSlot( 2, SaveThingModel.Guest ), new ThingSlot( 9, SaveThingModel.Guest ) } );
		rig.Prepare(); Assert.IsTrue( hud.OpenVisitorInfoWindow( first ) ); var figure = Figure( hud ); var firstCallback = figure.Frame;
		var screen = hud.Stack.Top; var before = JsonSerializer.Serialize( rig.Simulation.CaptureSnapshot() );
		Update( hud, 3 ); Assert.AreEqual( 3f, Get<float>( hud, "visitorInfoAge" ) );
		Arrow( hud, true )(); Assert.AreSame( second, hud.InfoVisitor ); Assert.AreSame( screen, hud.Stack.Top );
		Assert.AreEqual( 0f, Get<float>( hud, "visitorInfoAge" ) ); Assert.IsNull( firstCallback() );
		Assert.AreEqual( rig.Renderer.VisitorFrame( second ), figure.Frame() ); var secondCallback = figure.Frame;
		Arrow( hud, false )(); Assert.AreSame( first, hud.InfoVisitor ); Assert.IsNull( secondCallback() ); Assert.IsNull( firstCallback(), "cycling back does not revive a prior selection session" );
		Assert.AreEqual( rig.Renderer.VisitorFrame( first ), figure.Frame() );
		Assert.AreEqual( before, JsonSerializer.Serialize( rig.Simulation.CaptureSnapshot() ) );
		var active = figure.Frame; var portrait = active(); var cash = hud.VisitorInfoShown.Cash;
		first.Money += 50; Update( hud, 3.9f ); Assert.AreEqual( cash, hud.VisitorInfoShown.Cash );
		first.DrawDistanceWalked = 4.25f / GuestRenderer.WalkFramesPerCell; rig.Prepare( 3.9 );
		Assert.AreNotEqual( portrait, active(), "a world phase change is visible before the statistics timer" );
		Update( hud, 0.1f ); Assert.AreEqual( first.Money, hud.VisitorInfoShown.Cash );
		Assert.AreSame( active, figure.Frame, "statistics refresh must not rebind or restart the portrait" );
		Assert.AreEqual( rig.Renderer.VisitorFrame( first ), active() );
	}

	[TestMethod]
	public void SingletonCloseReopenAndLowerScreenDrawDoNotReuseStaleDelegatesOrRequireUiUpdates()
	{
		var guest = Visitor(); var (hud, rig) = HudRig( guest ); rig.Prepare(); hud.OpenVisitorInfoWindow( guest );
		var figure = Figure( hud ); var callback = figure.Frame;
		Update( hud, 3 ); Arrow( hud, true )(); Arrow( hud, false )();
		Assert.AreSame( callback, figure.Frame ); Assert.AreEqual( 3f, Get<float>( hud, "visitorInfoAge" ) );
		hud.CloseVisitorInfoWindow(); Assert.IsNull( callback() ); Assert.IsNull( figure.Frame() );
		hud.OpenVisitorInfoWindow( guest ); Assert.AreSame( figure, Figure( hud ) ); Assert.IsNull( callback() );
		Assert.AreEqual( rig.Renderer.VisitorFrame( guest ), figure.Frame(), "a current publication can be reused at reopen without creating a native clone" );
		var reopened = figure.Frame; hud.Stack.Push( new UiScreen( "modal" ) );
		guest.IsMoving = true; guest.DrawDistanceWalked = 5.25f / GuestRenderer.WalkFramesPerCell; rig.Prepare( 10 );
		Assert.AreEqual( rig.Expected( guest, 1, 5 ), reopened()!.Value, "lower-screen reads do not need its Update under a modal" );
		var context = Context(); hud.Stack.Draw( context );
		Assert.AreEqual( 6, context.Batch.Draws.Single( draw => ReferenceEquals( draw.Texture.Rendered, reopened()!.Value.Texture ) ).Vertices.Count );
		hud.CloseVisitorInfoWindow(); Assert.IsNull( reopened() ); Assert.IsFalse( hud.VisitorInfoWindowOpen );
	}

	[TestMethod]
	public void SelectedObjectRemovalAndReusedIdNeverRetargetItsOldPortraitCallback()
	{
		var guest = Visitor(); var (hud, rig) = HudRig( guest ); rig.Prepare(); hud.OpenVisitorInfoWindow( guest );
		var figure = Figure( hud ); var stale = figure.Frame; var replacement = Visitor( guest.Id );
		rig.Guests.Remove( guest ); rig.Guests.Add( replacement ); rig.Prepare();
		Assert.IsNull( stale() ); Assert.IsNull( figure.Frame() );
		Update( hud, 4 ); Assert.IsNull( stale() ); Assert.IsNull( figure.Frame(), "a stats refresh cannot adopt the replacement body's output" );
		Assert.AreSame( replacement, hud.InfoVisitor ); Assert.IsTrue( hud.VisitorInfoWindowOpen, "the existing ID-based statistics/navigation semantics stay unchanged" );
		Assert.IsTrue( hud.OpenVisitorInfoWindow( replacement ) ); Assert.IsNull( stale() );
		Assert.AreSame( replacement, hud.InfoVisitor ); Assert.AreEqual( rig.Renderer.VisitorFrame( replacement ), figure.Frame() );
		replacement.State = GuestState.Gone; Assert.IsNull( figure.Frame() ); Update( hud ); Assert.IsFalse( hud.VisitorInfoWindowOpen );
	}

	[TestMethod]
	public void ActualCallbackDropsLostSourcesAndShowsLaterSuccessfulPublicationWithoutAStatRefresh()
	{
		var guest = Visitor(); var (hud, rig) = HudRig( guest ); hud.OpenVisitorInfoWindow( guest ); var figure = Figure( hud );
		Assert.IsNull( figure.Frame() ); rig.Prepare(); Assert.IsNotNull( figure.Frame() );
		var callback = figure.Frame; var body = guest.BodySprite.CaptureSnapshot();
		guest.State = GuestState.HeldByGuard; Assert.IsNull( callback() ); rig.Prepare();
		guest.State = GuestState.WalkingAround; Assert.IsNull( callback() );
		guest.IsMoving = true; guest.DrawDistanceWalked = 7.25f / GuestRenderer.WalkFramesPerCell; rig.Prepare();
		Assert.AreEqual( rig.Expected( guest, 1, 7 ), callback()!.Value, "this is the bounded host source-return policy, not native clone-epoch behavior" );
		Assert.AreSame( callback, figure.Frame ); Assert.AreEqual( body, guest.BodySprite.CaptureSnapshot() );
	}

	[DataTestMethod]
	[DataRow( 2048, 1536 )]
	[DataRow( 1024, 768 )]
	[DataRow( 1920, 1080 )]
	[DataRow( 3440, 1440 )]
	[DataRow( 777, 1000 )]
	public void ActualFigureUsesIntegerInnerFeetBeforeCanvasMappingAndNominalVisitorScale( int width, int height )
	{
		var guest = Visitor(); var (hud, rig) = HudRig( guest );
		foreach ( var atlas in rig.Atlases )
			for ( var index = 0; index < atlas.Frames.Length; index++ )
				atlas.Frames[index] = atlas.Frames[index] with { PixelsPerUnitX = 64, PixelsPerUnitY = 256 };
		rig.Prepare(); hud.OpenVisitorInfoWindow( guest ); var figure = Figure( hud );
		var portrait = OriginalLayoutTables.VisitorInfoWindow.Control( 0x3722 ); var inner = portrait.Inner!.Value;
		Assert.AreEqual( new UiRect( 477, 281, 227, 229 ), inner ); Assert.AreEqual( inner, figure.Bounds );
		Assert.AreEqual( portrait.Rect, hud.Stack.Top!.Find( "portraitFrame" )!.Bounds );
		Assert.AreEqual( new N.Vector2( 598, 450 ), VisitorInfoRules.PortraitFeet( inner ) );
		Assert.AreEqual( new N.Vector2( 598, 450 ), figure.FeetInCanvas!.Value );
		Assert.AreEqual( 3.6f, figure.CanvasUnitsPerPixel ); Assert.AreEqual( UiAnchor.Center, figure.Anchor );
		var context = Context( width: width, height: height ); figure.Draw( context, false, false );
		var draw = context.Batch.Draws.Single(); var frame = figure.Frame()!.Value;
		Assert.AreEqual( (2f, 0.5f), (frame.ScaleX, frame.ScaleY) ); Assert.IsTrue( frame.Mirror );
		var feet = context.Canvas.Map( new N.Vector2( 598, 450 ), UiAnchor.Center ); var pixel = 3.6f * context.Canvas.Scale;
		var left = feet.X - (frame.OriginX + frame.Width) * pixel * 2;
		var top = feet.Y + frame.OriginY * pixel * 0.5f;
		Assert.AreSame( frame.Texture, draw.Texture.Rendered ); Assert.AreEqual( 6, draw.Vertices.Count );
		AssertPoint( new N.Vector2( left, top ), draw.Vertices[0].Position );
		AssertPoint( new N.Vector2( left, top + frame.Height * pixel * 0.5f ), draw.Vertices[1].Position );
		AssertPoint( new N.Vector2( left + frame.Width * pixel * 2, top ), draw.Vertices[2].Position );
		AssertPoint( new N.Vector2( (frame.X + frame.Width) / (float)frame.AtlasWidth, frame.Y / (float)frame.AtlasHeight ), draw.Vertices[0].TexCoords );
		AssertPoint( new N.Vector2( frame.X / (float)frame.AtlasWidth, (frame.Y + frame.Height) / (float)frame.AtlasHeight ), draw.Vertices[5].TexCoords );
	}

	[TestMethod]
	public void VisitorIntegerFeetHandleOddAndEvenRectanglesWithoutChangingSharedFigureDefaults()
	{
		Assert.AreEqual( new N.Vector2( 598, 450 ), VisitorInfoRules.PortraitFeet( new UiRect( 477, 281, 227, 229 ) ) );
		Assert.AreEqual( new N.Vector2( 598, 449 ), VisitorInfoRules.PortraitFeet( new UiRect( 477, 281, 226, 228 ) ) );
		Assert.AreEqual( new N.Vector2( 24, 29 ), VisitorInfoRules.PortraitFeet( new UiRect( 10, 20, 13, 15 ) ) );
		var figure = new UiSpriteFigure(); Assert.AreEqual( 3.2f, figure.CanvasUnitsPerPixel ); Assert.IsNull( figure.FeetInCanvas );
		Assert.AreEqual( new N.Vector2( 616, 377 ), UiSpriteFigure.Feet( new UiRect( 484, 179, 264, 264 ) ) );
	}

	[DataTestMethod]
	[DataRow( "wrapper" )]
	[DataRow( "native" )]
	[DataRow( "nullNative" )]
	public void TextureIdentityChangesCannotReuseAnEarlierWorldPublication( string kind )
	{
		var guest = Visitor(); var rig = new Rig( guest ); rig.Prepare();
		var index = GuestAppearance.KidSet( guest.Id, rig.Atlases.Count ); var old = rig.Textures[index];
		var originalNative = old.NativeTexture; var replacement = TextureSentinel();
		switch ( kind )
		{
			case "wrapper": rig.Textures[index] = replacement; break;
			case "native": old.NativeTexture = replacement.NativeTexture; break;
			case "nullNative": old.NativeTexture = null!; break;
		}
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
		rig.Prepare();
		if ( kind == "nullNative" )
		{
			Assert.IsNull( rig.Renderer.VisitorFrame( guest ) ); old.NativeTexture = originalNative;
			Assert.IsNull( rig.Renderer.VisitorFrame( guest ), "a skipped resource cannot revive an earlier publication" ); rig.Prepare();
		}
		Assert.AreSame( rig.Textures[index].NativeTexture, rig.Renderer.VisitorFrame( guest )!.Value.Texture );
	}

	[DataTestMethod]
	[DataRow( "zeroWidth" )]
	[DataRow( "outsideAtlas" )]
	[DataRow( "invalidTime" )]
	public void MalformedPresentationCannotPublishEvenWhenLegacyWorldCodePreparesAQuad( string reason )
	{
		var guest = Visitor(); var rig = new Rig( guest ); rig.Prepare();
		if ( reason == "invalidTime" ) Set( rig.Simulation, "<TimeSeconds>k__BackingField", double.PositiveInfinity );
		else foreach ( var atlas in rig.Atlases )
			for ( var index = 0; index < atlas.Frames.Length; index++ )
				atlas.Frames[index] = reason == "zeroWidth" ? atlas.Frames[index] with { Width = 0 } : atlas.Frames[index] with { X = atlas.Width + 1 };
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
		Assert.AreEqual( 12, rig.Renderer.PrepareWorld(), "existing world preparation semantics remain unchanged" );
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
	}

	[DataTestMethod]
	[DataRow( 0, 0 )]
	[DataRow( -1, 0 )]
	[DataRow( 1, -1 )]
	public void MalformedActorIdentityDoesNotPublish( int id, int type )
	{
		var guest = Visitor( id, type ); var rig = new Rig( guest ); rig.Prepare();
		Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
	}

	[TestMethod]
	public void ReplacingTheRidePoseSourceInvalidatesAnOrdinaryPublicationWithoutCollectingFromUi()
	{
		var guest = Visitor(); var rig = new Rig( guest ); rig.Prepare();
		var source = new RiderSource { Pose = new BounceRiderPose( guest.Id, 77, N.Vector3.Zero, 6, false, 4 ) };
		rig.Renderer.BounceRiders = source; Assert.IsNull( rig.Renderer.VisitorFrame( guest ) ); Assert.AreEqual( 0, source.CollectCalls );
		rig.Prepare(); Assert.IsNotNull( rig.Renderer.VisitorFrame( guest ), "an unmatched pose does not replace an ordinary visible body" );
		var collections = source.CollectCalls;
		for ( var index = 0; index < 20; index++ ) Assert.IsNotNull( rig.Renderer.VisitorFrame( guest ) );
		Assert.AreEqual( collections, source.CollectCalls );
		rig.Renderer.BounceRiders = null; Assert.IsNull( rig.Renderer.VisitorFrame( guest ) );
	}

	[DataTestMethod]
	[DataRow( "pop" )]
	[DataRow( "clear" )]
	[DataRow( "remove" )]
	public void GenericStackRemovalInvalidatesBothCurrentAndRetainedPortraitCallbacks( string method )
	{
		var guest = Visitor(); var (hud, rig) = HudRig( guest ); rig.Prepare(); hud.OpenVisitorInfoWindow( guest );
		var figure = Figure( hud ); var screen = hud.Stack.Top!; var callback = figure.Frame; Assert.IsNotNull( callback() );
		switch ( method )
		{
			case "pop": hud.Stack.Pop(); break;
			case "clear": hud.Stack.Clear(); break;
			case "remove": hud.Stack.RemoveWhere( item => ReferenceEquals( item, screen ) ); break;
		}
		Assert.IsNull( callback() ); Assert.IsNull( figure.Frame() );
		// Reinserting an old screen does not replace the explicit opener/session binding.
		hud.Stack.Push( screen ); Assert.IsNull( callback() ); Assert.IsNull( figure.Frame() );
	}

	[TestMethod]
	public void ObservedAndNoReadControlsHaveTheSameNextWorldQuadAndSimulationState()
	{
		var first = Visitor(); var second = Visitor(); first.IsMoving = second.IsMoving = true;
		first.DrawDistanceWalked = second.DrawDistanceWalked = 3.25f / GuestRenderer.WalkFramesPerCell;
		var (hud, observed) = HudRig( first ); var (_, control) = HudRig( second );
		Level.Current = observed.Level; observed.Prepare( 2 ); hud.OpenVisitorInfoWindow( first );
		Level.Current = control.Level; control.Prepare( 2 );
		var player = first.BodySprite.CreatePlayer( VisitorSpriteTemplate.Walk, 0 ); player.Tick( 63 );
		first.BodySprite.QueueTemplate( VisitorSpriteTemplate.Cheer ); first.BodySprite.QueueInterval( 111 );
		second.BodySprite.RestoreSnapshot( first.BodySprite.CaptureSnapshot() );
		Level.Current = observed.Level; var figure = Figure( hud );
		for ( var index = 0; index < 100; index++ ) Assert.IsNotNull( figure.Frame() );
		Assert.AreEqual( JsonSerializer.Serialize( control.Simulation.CaptureSnapshot() ), JsonSerializer.Serialize( observed.Simulation.CaptureSnapshot() ) );
		Assert.AreEqual( control.Simulation.ComputeStateHash(), observed.Simulation.ComputeStateHash() );
		Assert.AreEqual( control.Simulation.RandomState, observed.Simulation.RandomState );
		first.DrawDistanceWalked = second.DrawDistanceWalked = 6.25f / GuestRenderer.WalkFramesPerCell;
		Level.Current = observed.Level; observed.Prepare( 3 ); Level.Current = control.Level; control.Prepare( 3 );
		CollectionAssert.AreEqual( Get<Vertex[]>( control.Renderer, "vertices" ).Take( 12 ).ToArray(), Get<Vertex[]>( observed.Renderer, "vertices" ).Take( 12 ).ToArray() );
		Assert.AreEqual( second.BodySprite.CaptureSnapshot(), first.BodySprite.CaptureSnapshot() );
		Assert.AreEqual( JsonSerializer.Serialize( control.Simulation.CaptureSnapshot() ), JsonSerializer.Serialize( observed.Simulation.CaptureSnapshot() ) );
	}

	[DataTestMethod]
	[DataRow( false, 2048, 1536 )]
	[DataRow( true, 2048, 1536 )]
	[DataRow( false, 1920, 1080 )]
	[DataRow( true, 1920, 1080 )]
	public void ActualHireAndEmployeeFiguresKeepTheirSeparateGeometryWhenDrawing( bool employee, int width, int height )
	{
		var economy = EconomyTestData.Park(); var member = EconomyTestData.HireBest( economy, StaffType.Handyman );
		var hud = StaffWindowTests.Hud( economy );
		if ( employee ) Assert.IsTrue( hud.OpenEmployeeWindow( member ) ); else hud.OpenStaffWindow();
		var figure = Figure( hud ); Assert.AreEqual( 2.4f, figure.CanvasUnitsPerPixel );
		if ( employee ) Assert.AreEqual( new N.Vector2( 526, 476 ), figure.FeetInCanvas!.Value );
		else Assert.AreEqual( new N.Vector2( 626, 362 ), figure.FeetInCanvas!.Value );
		var texture = TextureSentinel(); var frame = new UiSpriteFrame( texture.NativeTexture, 128, 64, 10, 20, 3, 5, 2, -4, true, 2, 0.5f );
		// Test the actual screen's existing widget with a managed frame; live staff callbacks have their own integration tests.
		figure.Frame = () => frame; var context = Context( width: width, height: height ); figure.Draw( context, false, false );
		var bounds = context.Canvas.Map( figure.Bounds, figure.Anchor );
		var feet = context.Canvas.Map( employee ? new N.Vector2( 526, 476 ) : new N.Vector2( 626, 362 ), UiAnchor.Center );
		var pixel = 2.4f * bounds.Width / figure.Bounds.Width; var draw = context.Batch.Draws.Single();
		AssertPoint( new N.Vector2( feet.X - 5 * pixel * 2, feet.Y - 4 * pixel * 0.5f ), draw.Vertices[0].Position );
		AssertPoint( new N.Vector2( feet.X - 2 * pixel * 2, feet.Y + pixel * 0.5f ), draw.Vertices[5].Position );
		Assert.AreSame( texture.NativeTexture, draw.Texture.Rendered ); Assert.AreEqual( 6, draw.Vertices.Count );
	}

	private static void AssertPoint( N.Vector2 expected, N.Vector2 actual )
	{
		Assert.AreEqual( expected.X, actual.X, 0.0002f ); Assert.AreEqual( expected.Y, actual.Y, 0.0002f );
	}
}

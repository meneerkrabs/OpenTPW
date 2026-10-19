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

/// <summary>Actual CPU source/clone owners, world quad preparation, resource resolution and HUD Draw. No GPU or original media.</summary>
[TestClass, DoNotParallelize]
public class EmployeeLivePortraitTests
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

	// A constructor-free Vulkan object is a managed identity sentinel. No native method or disposal is executed.
	private static Texture TextureSentinel()
	{
		var type = typeof( Veldrid.Texture ).Assembly.GetType( "Veldrid.Vk.VkTexture", throwOnError: true )!;
		var texture = Empty<Texture>(); texture.NativeTexture = (Veldrid.Texture)RuntimeHelpers.GetUninitializedObject( type );
		return texture;
	}
	private static StaffSpriteSet SetOf( GuestSpriteAtlas atlas ) => (StaffSpriteSet)Activator.CreateInstance( typeof( StaffSpriteSet ),
		BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { new[] { atlas }, Enumerable.Repeat( (0, 1), 5 ).ToArray() }, null )!;

	private sealed class Rig : IDisposable
	{
		public readonly ParkEconomy Economy = EconomyTestData.Park();
		public readonly StaffMember Member;
		public readonly StaffSpriteSet Sprites;
		public readonly GuestSpriteAtlas Atlas;
		public readonly StaffSpritePresentation Presentation;
		public readonly ParkHud Hud;
		public readonly Level Level;
		public readonly StaffRenderer Renderer;
		public StaffBodySprite Body => Member.BodySprite!;
		public EmployeePortraitOwner Portrait => Hud.EmployeePortrait!;
		public UiSpriteFigure Figure => (UiSpriteFigure)Hud.Stack.Screens.First( screen => screen.Find( "portraitFigure" ) != null ).Find( "portraitFigure" )!;
		public List<Texture> Textures => Get<List<Texture>>( Renderer, "textures" );
		public Rig( StaffType type = StaffType.Handyman, int directions = 5, int pixelX = 128, int pixelY = 128 )
		{
			Member = new StaffMember { Id = 1, ThingId = 0, Type = type, Variant = 0, NameIndex = 0, Grade = 1, CellX = 4, CellY = 4 };
			((List<StaffMember>)Economy.Staff.Members).Add( Member );
			Hud = StaffWindowTests.Hud( Economy ); Level = Get<Level>( Hud, "level" ); OpenTPW.Level.Current = Level;
			Atlas = StaffSpriteDrawRecordTests.Atlas( directions: directions, pixelX: pixelX, pixelY: pixelY ); Sprites = SetOf( Atlas );
			Presentation = new StaffSpritePresentation( new StaffSpriteMetadataCatalog( Sprites ), () => Economy.Staff.Members );
			Presentation.AttachRoster( Economy.Staff ); Body.CreateSource();
			Set( Level, "<StaffSprites>k__BackingField", Presentation );
			Renderer = new StaffRenderer( () => Economy.Staff.Members,
				new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] ),
				StaffSpriteSet.Empty, new StaffPlacement( () => null, () => null, (_, _) => false ), Presentation.Catalog,
				member => member.BodySprite?.DrawRecord, () => Presentation.Session );
			Set( Renderer, "sprites", Sprites ); Set( Renderer, "material", Empty<Material<ObjectUniformBuffer>>() );
			Textures.Add( TextureSentinel() ); Set( Level, "<StaffRenderer>k__BackingField", Renderer );
		}
		public void Start( int request = 17 )
		{
			Body.RequestTemplate( request ); Body.ApplyPending(); Presentation.Registry.Schedule( 63 );
			Presentation.Present(); Assert.IsTrue( Hud.OpenEmployeeWindow( Member ) );
		}
		public string State() => JsonSerializer.Serialize( Body.ReadSourceState() ) + ";" + JsonSerializer.Serialize( Portrait.ReadCloneState() ) + ";" + Portrait.Epoch;
		public UiSpriteFrame? Frame() => Figure.Frame();
		public void Dispose() { Hud.CloseEmployeeWindow(); Presentation.Dispose(); }
	}
	private static UiContext Context( int width = 1280, int height = 720 )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( width, height ) };
	}

	[DataTestMethod]
	[DataRow( StaffType.Handyman, 17, 4 )]
	[DataRow( StaffType.Mechanic, 18, 4 )]
	[DataRow( StaffType.Entertainer, 13, 4 )]
	[DataRow( StaffType.Guard, 22, 7 )]
	[DataRow( StaffType.Researcher, 10, 4 )]
	public void EveryRoleDrawsItsActualProgramWithoutAnyWorldRender( StaffType type, int request, int slot )
	{
		using var rig = new Rig( type ); rig.Start( request );
		Assert.AreEqual( (uint)slot, rig.Portrait.DrawRecord!.Slot ); Assert.IsNotNull( rig.Frame() );
		Assert.AreEqual( 0, rig.Renderer.DrawnStaff, "the portrait never needed a world publication" );
		var before = rig.State(); var save = ParkSaveFile.Serialize( rig.Economy ); var rng = rig.Economy.World.State;
		for ( var index = 0; index < 40; index++ )
		{
			Camera.ViewMatrix = N.Matrix4x4.CreateRotationZ( index / 4f );
			Assert.AreEqual( rig.Renderer.EmployeeFrame( rig.Portrait.DrawRecord! ), rig.Frame() );
			var context = Context(); rig.Figure.Draw( context, false, false ); Assert.AreEqual( 1, context.Batch.Draws.Count );
		}
		Assert.AreEqual( before, rig.State() ); Assert.AreEqual( save, ParkSaveFile.Serialize( rig.Economy ) ); Assert.AreEqual( rng, rig.Economy.World.State );
	}

	[TestMethod]
	public void OpeningWithoutSourceStaysBlankAfterSourceReturnUntilRealFullRefresh()
	{
		using var rig = new Rig(); rig.Body.DeleteSource(); Assert.IsTrue( rig.Hud.OpenEmployeeWindow( rig.Member ) );
		Assert.AreEqual( EmployeePortraitState.OpenUnbound, rig.Portrait.State ); Assert.IsNull( rig.Frame() );
		rig.Body.CreateSource(); rig.Presentation.Registry.Schedule( 63 ); rig.Presentation.Present( rig.Hud.RefreshEmployeePortrait );
		Assert.IsNull( rig.Frame() ); Assert.IsNull( rig.Portrait.CloneTicket );
		rig.Hud.CloseEmployeeWindow(); rig.Hud.OpenEmployeeWindow( rig.Member ); Assert.IsNotNull( rig.Frame() );
	}

	[TestMethod]
	public void WorldRenderAndRepeatedHudDrawsCannotScheduleOrInferGameplayAnimation()
	{
		using var rig = new Rig( StaffType.Mechanic ); rig.Member.BusyTicks = 0; rig.Start( 18 );
		var before = rig.State(); var frame = rig.Frame();
		rig.Member.BusyTicks = 500; rig.Member.Job = StaffJob.Idle; rig.Member.CellX = 9;
		for ( var index = 0; index < 20; index++ )
		{
			Time.Now += 100; Time.Delta = 0.25f;
			Assert.AreEqual( 12, rig.Renderer.PrepareWorld() ); Assert.AreEqual( frame, rig.Frame() );
		}
		Assert.AreEqual( before, rig.State(), "renderer cannot infer clips, intervals, headings, positions or requests" );
		Assert.IsTrue( rig.Renderer.TryGetDrawCell( rig.Member, out var x, out var y ) ); Assert.AreEqual( 4f, x ); Assert.AreEqual( 4f, y );
	}

	[TestMethod]
	public void HiddenWorldAndSkippedPassesKeepPortraitAndCloneClockAlive()
	{
		using var rig = new Rig(); rig.Start( 1 ); rig.Renderer.Visible = false;
		Assert.AreEqual( 0, rig.Renderer.PrepareWorld() ); Assert.IsNotNull( rig.Frame() );
		var ticket = rig.Portrait.CloneTicket; rig.Presentation.Registry.Schedule( 126 ); rig.Hud.RefreshEmployeePortrait();
		Assert.AreEqual( 1u, rig.Portrait.DrawRecord!.Frame ); Assert.IsNotNull( rig.Frame() ); Assert.AreEqual( ticket, rig.Portrait.CloneTicket );
		rig.Renderer.Visible = true; Assert.AreEqual( 12, rig.Renderer.PrepareWorld() );
	}

	[TestMethod]
	public void SourceLossWorldRouteSuppressesActualHudQuadAndReturnRestoresSameClone()
	{
		using var rig = new Rig(); rig.Start( 1 ); var ticket = rig.Portrait.CloneTicket; var epoch = rig.Portrait.Epoch;
		var initial = rig.Portrait.DrawRecord!; rig.Body.DeleteSource();
		Assert.IsNotNull( rig.Frame(), "loss alone does not alter screen flags" );
		for ( var step = 2; step <= 10; step++ ) rig.Presentation.Registry.Schedule( 63 * step );
		var record = rig.Portrait.DrawRecord!;
		Assert.AreEqual( StaffSpriteDiagnostic.WorldRouteNotImplemented, rig.Renderer.ResolveRecord( record ).Diagnostic );
		Assert.IsNull( rig.Frame() ); var context = Context(); rig.Figure.Draw( context, false, false ); Assert.AreEqual( 0, context.Batch.Draws.Count );
		Assert.AreEqual( initial.X, record.X ); Assert.AreEqual( initial.Y, record.Y ); Assert.AreEqual( initial.Z, record.Z );
		Assert.AreEqual( initial.PrimaryScale, record.PrimaryScale );
		rig.Body.CreateSource(); rig.Presentation.Registry.Schedule( 700 ); rig.Hud.RefreshEmployeePortrait();
		Assert.AreEqual( ticket, rig.Portrait.CloneTicket ); Assert.AreEqual( epoch, rig.Portrait.Epoch ); Assert.IsNotNull( rig.Frame() );
	}

	[TestMethod]
	public void AbsentSourcePerformanceLoopKeepsDrawingItsIndependentScreenClone()
	{
		using var rig = new Rig( StaffType.Entertainer ); rig.Start( 13 );
		var ticket = rig.Portrait.CloneTicket; rig.Body.DeleteSource();
		for ( var step = 2; step <= 25; step++ )
		{
			rig.Presentation.Registry.Schedule( 63 * step );
			Assert.AreEqual( StaffSpriteRoute.Screen, rig.Portrait.DrawRecord!.Route ); Assert.IsNotNull( rig.Frame() );
			var context = Context(); rig.Figure.Draw( context, false, false ); Assert.AreEqual( 1, context.Batch.Draws.Count );
		}
		Assert.AreEqual( ticket, rig.Portrait.CloneTicket );
	}

	[DataTestMethod]
	[DataRow( StaffSpriteRegister.Alpha, 254L )]
	[DataRow( StaffSpriteRegister.SecondaryScale, 1073741824L )]
	[DataRow( StaffSpriteRegister.RotationZ, 1L )]
	public void UnsupportedTransformProducesNoActualHudQuadAndKeepsRawWords( StaffSpriteRegister register, long word )
	{
		using var rig = new Rig(); rig.Start(); Assert.IsTrue( rig.Portrait.TryResolve( out var clone ) );
		clone[register] = (uint)word; var before = rig.State();
		Assert.AreEqual( StaffSpriteDiagnostic.UnsupportedTransform, rig.Renderer.ResolveRecord( rig.Portrait.DrawRecord! ).Diagnostic );
		var context = Context(); rig.Figure.Draw( context, false, false ); Assert.AreEqual( 0, context.Batch.Draws.Count );
		Assert.AreEqual( before, rig.State() ); Assert.AreEqual( (uint)word, clone[register] );
	}

	[TestMethod]
	public void MatchingTextureRecoveryRequiresNoWorldPassAndNeverMutatesVmHistory()
	{
		using var rig = new Rig(); rig.Start(); var before = rig.State(); var first = rig.Frame()!.Value;
		var old = rig.Textures[0]; Set( old.NativeTexture, "_destroyed", true );
		Assert.IsNull( rig.Frame() ); Assert.AreEqual( StaffSpriteDiagnostic.TextureUnavailable, rig.Renderer.ResolveRecord( rig.Portrait.DrawRecord! ).Diagnostic );
		var replacement = TextureSentinel();
		var replace = typeof( StaffRenderer ).GetMethod( "ReplaceAtlas", BindingFlags.Instance | BindingFlags.NonPublic )!;
		Assert.AreSame( old, replace.Invoke( rig.Renderer, new object[] { 0, replacement } ) );
		Assert.AreSame( replacement.NativeTexture, rig.Frame()!.Value.Texture ); Assert.AreNotSame( first.Texture, rig.Frame()!.Value.Texture );
		Assert.AreEqual( before, rig.State() ); Assert.AreEqual( 0, rig.Renderer.DrawnStaff );
	}

	[TestMethod]
	public void ResourceIdentityAndLayoutMismatchAreRejectedWithoutHotFillingCatalog()
	{
		using var rig = new Rig(); rig.Start(); var before = rig.State();
		Set( rig.Renderer, "sprites", SetOf( StaffSpriteDrawRecordTests.Atlas( "SPR_OTHER" ) ) );
		Assert.AreEqual( StaffSpriteDiagnostic.ResourceMismatch, rig.Renderer.ResolveRecord( rig.Portrait.DrawRecord! ).Diagnostic ); Assert.IsNull( rig.Frame() );
		Set( rig.Renderer, "sprites", rig.Sprites ); rig.Atlas.Frames[0] = rig.Atlas.Frames[0] with { OriginX = 17 };
		Assert.AreEqual( StaffSpriteDiagnostic.ResourceMismatch, rig.Renderer.ResolveRecord( rig.Portrait.DrawRecord! ).Diagnostic );
		Assert.AreEqual( before, rig.State() );
	}

	[TestMethod]
	public void RendererDeletionClearsHandlesAndDoesNotRetireCpuSourceOrClone()
	{
		using var rig = new Rig(); rig.Start(); var before = rig.State(); var record = rig.Portrait.DrawRecord!;
		Set( rig.Renderer, "material", null ); Global.Render = Empty<Renderer>(); Set( Global.Render, "_deletionQueue", new List<Action>() );
		rig.Renderer.Delete(); Assert.AreEqual( 0, rig.Textures.Count ); Assert.AreEqual( 1, Get<List<Action>>( Global.Render, "_deletionQueue" ).Count );
		Assert.IsNull( rig.Renderer.EmployeeFrame( record ) ); Entity.All.Add( rig.Renderer ); Assert.IsNull( rig.Renderer.EmployeeFrame( record ) );
		Assert.AreEqual( before, rig.State() ); rig.Presentation.Registry.Schedule( 126 ); Assert.AreEqual( 1u, rig.Body.DrawRecord!.OutputValid );
	}

	[TestMethod]
	public void RemovedIdentityAndReusedNumbersCannotKeepOldHudOrSourceBinding()
	{
		using var rig = new Rig(); rig.Start(); var oldBody = rig.Body;
		rig.Economy.Staff.Fire( rig.Member.Id ); Assert.IsNull( rig.Hud.Employee ); Assert.IsNull( rig.Frame() ); Assert.IsNull( oldBody.DrawRecord );
		var replacement = new StaffMember { Id = rig.Member.Id, ThingId = rig.Member.ThingId, Type = rig.Member.Type, Variant = 0, CellX = 4, CellY = 4 };
		((List<StaffMember>)rig.Economy.Staff.Members).Add( replacement ); rig.Presentation.Attach( replacement ).CreateSource();
		Assert.IsNull( rig.Hud.Employee ); Assert.IsNull( rig.Frame() ); Assert.AreEqual( 0u, replacement.BodySprite!.ReadSourceState()!.OutputValid );
	}

	[TestMethod]
	public void ForeignSessionAndDetachedRendererCannotResolveClone()
	{
		using var rig = new Rig(); rig.Start(); var current = rig.Portrait.DrawRecord!;
		var stale = new StaffSpriteDrawRecord( rig.Portrait.ReadCloneState()!, current.Session + 1, current.RegistrySlot, current.Generation, true );
		Assert.AreEqual( StaffSpriteDiagnostic.StaleIdentity, rig.Renderer.ResolveRecord( stale ).Diagnostic );
		Entity.All.Remove( rig.Renderer ); Assert.IsNull( rig.Frame() ); Entity.All.Add( rig.Renderer ); Assert.IsNotNull( rig.Frame() );
		Level.Current = Empty<Level>(); Assert.IsNull( rig.Frame() ); Level.Current = rig.Level;
		Set( rig.Level, "<StaffRenderer>k__BackingField", null ); Assert.IsNull( rig.Renderer.EmployeeFrame( current ) );
	}
}

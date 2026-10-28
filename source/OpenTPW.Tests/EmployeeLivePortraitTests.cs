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

/// <summary>Synthetic CPU world preparation and the real HUD callback. No graphics device or original media.</summary>
[TestClass, DoNotParallelize]
public class EmployeeLivePortraitTests
{
	private List<Entity> previousEntities = null!;
	private Level previousLevel = null!;
	private Renderer previousRenderer = null!;
	private CameraMode previousCamera = null!;
	private N.Matrix4x4 previousView;
	private float previousNow, previousDelta;
	private bool previousTextEntry;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Setup()
	{
		previousEntities = Entity.All;
		previousLevel = Level.Current;
		previousRenderer = Global.Render;
		previousCamera = Camera.Mode;
		previousView = Camera.ViewMatrix;
		previousNow = Time.Now;
		previousDelta = Time.Delta;
		previousTextEntry = Input.TextEntryActive;
		previousLog = Log;
		Entity.All = new();
		Camera.Mode = new CameraMode();
		Camera.ViewMatrix = N.Matrix4x4.Identity;
		Time.Now = Time.Delta = 0;
		Input.TextEntryActive = false;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Entity.All = previousEntities;
		Level.Current = previousLevel;
		Global.Render = previousRenderer;
		Camera.Mode = previousCamera;
		Camera.ViewMatrix = previousView;
		Time.Now = previousNow;
		Time.Delta = previousDelta;
		Input.TextEntryActive = previousTextEntry;
		Log = previousLog;
	}

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static FieldInfo Field( object owner, string name ) => owner.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public )!;
	private static void Set( object owner, string name, object? value ) => Field( owner, name ).SetValue( owner, value );
	private static T Get<T>( object owner, string name ) => (T)Field( owner, name ).GetValue( owner )!;
	private static StaffMember Member( StaffType type = StaffType.Handyman, int id = 1 ) =>
		new() { Id = id, ThingId = id + 100, Type = type, NameIndex = 0, Grade = 1, Variant = 0, CellX = 4, CellY = 4 };

	// Veldrid 4.9's abstract Texture has a private-protected DisposeCore, so it cannot be subclassed here.
	// A constructor-free Vulkan texture is only a lifetime sentinel: IsDisposed reads its managed _destroyed flag.
	// Never call Dispose or any native operation on it; no graphics device, GPU object or Vulkan entry point is created.
	private static Texture TextureSentinel()
	{
		var nativeType = typeof( Veldrid.Texture ).Assembly.GetType( "Veldrid.Vk.VkTexture", throwOnError: true )!;
		var texture = Empty<Texture>();
		texture.NativeTexture = (Veldrid.Texture)RuntimeHelpers.GetUninitializedObject( nativeType );
		Assert.IsFalse( texture.NativeTexture.IsDisposed );
		return texture;
	}
	private static void MarkDisposed( Texture texture )
	{
		Set( texture.NativeTexture, "_destroyed", true );
		Assert.IsTrue( texture.NativeTexture.IsDisposed );
	}

	private static GuestSpriteAtlas Atlas( int directions )
	{
		var builder = new SpriteSetBuilder( "SPR_ZZ" ) { FullDetail = false };
		foreach ( var (slot, count) in new[] { (0, 1), (1, 8), (2, 8), (4, 8), (5, 4), (6, 5), (7, 8), (8, 8) } )
		{
			var frames = Enumerable.Range( 0, directions ).Select( direction => (IReadOnlyList<SpriteRgbaFrame>)Enumerable.Range( 0, count )
				.Select( step => new SpriteRgbaFrame( 2, 2, new byte[] { 30, 60, 90, 255, 30, 60, 90, 255, 30, 60, 90, 255, 30, 60, 90, 255 }, -1, -2 ) ).ToArray() ).ToArray();
			builder.AddAnimation( slot, frames );
		}
		builder.SetPerformance( 0, 0, 4, 2, 2 );
		var data = builder.Build();
		return new GuestSpriteAtlas( "SPR_ZZ", new SpriteBankFile( new MemoryStream( data.Files[".TPC"] ) ), new SpriteAnimationFile( new MemoryStream( data.Files[".ESP"] ) ) );
	}

	private sealed class Rig
	{
		public readonly List<StaffMember> Members;
		public readonly GuestSpriteAtlas Atlas;
		public readonly Level Level;
		public readonly StaffRenderer Renderer;
		public readonly StaffPlacement Placement = StaffPlacementRig.Create( () => null, () => null, (_, _) => false );
		public List<Texture> Textures => Get<List<Texture>>( Renderer, "textures" );
		public Rig( StaffMember member, int directions = 5, Level? level = null, List<StaffMember>? members = null )
		{
			Members = members ?? new() { member };
			Atlas = EmployeeLivePortraitTests.Atlas( directions );
			Level = level ?? Empty<Level>();
			OpenTPW.Level.Current = Level;
			var set = (StaffSpriteSet)Activator.CreateInstance( typeof( StaffSpriteSet ), BindingFlags.Instance | BindingFlags.NonPublic, null,
				new object[] { new[] { Atlas }, Enumerable.Repeat( (0, 1), 5 ).ToArray() }, null )!;
			// Construct the ordinary no-assets renderer, then install synthetic CPU banks and managed resource sentinels.
			Renderer = new StaffRenderer( () => Members, new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] ), StaffSpriteSet.Empty, Placement );
			Set( Renderer, "sprites", set );
			Set( Renderer, "material", Empty<Material<ObjectUniformBuffer>>() );
			Textures.Add( TextureSentinel() );
			Set( Level, "<StaffRenderer>k__BackingField", Renderer );
		}
		public int Prepare( float now )
		{
			Time.Delta = 0.07f;
			Time.Now = now;
			return Renderer.PrepareWorld();
		}
		public object View( StaffMember member ) => ((IDictionary)Field( Renderer, "views" ).GetValue( Renderer )!)[member]!;
		public SpritePlayer Player( StaffMember member ) => Get<SpritePlayer>( View( member ), "Player" );
		public string State( StaffMember member )
		{
			var view = View( member );
			var state = view.GetType().GetFields().Where( field => field.Name is not ("Player" or "Published") )
				.Select( field => $"{field.Name}={field.GetValue( view )}" );
			var player = Player( member );
			return string.Join( ";", state ) + $";steps={player.Steps};interval={player.Interval};due={Get<long>( player, "due" )};member={JsonSerializer.Serialize( member )}";
		}
		public UiSpriteFrame Expected( int slot, int step, float headingX = 0, float headingY = 1 )
		{
			var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( headingX, headingY, 0f, Atlas.DirectionsOf( slot ) );
			var frame = Atlas.Frames[Atlas.GetFrame( slot, direction, step % Atlas.FramesPerDirection( slot ) )];
			return new UiSpriteFrame( Textures[0].NativeTexture, Atlas.Width, Atlas.Height, frame.X, frame.Y, frame.Width, frame.Height,
				frame.OriginX, frame.OriginY, mirror, frame.ScaleX, frame.ScaleY );
		}
	}

	[DataTestMethod]
	[DataRow( "stand" )]
	[DataRow( "walk" )]
	[DataRow( "sweep" )]
	[DataRow( "repair" )]
	[DataRow( "strike" )]
	[DataRow( "research" )]
	[DataRow( "performance" )]
	public void SuccessfulWorldPreparationPublishesTheExactClipAndPhase( string kind )
	{
		var type = kind switch { "repair" => StaffType.Mechanic, "research" => StaffType.Researcher, "performance" => StaffType.Entertainer, _ => StaffType.Handyman };
		var member = Member( type );
		member.Job = kind switch { "sweep" => StaffJob.Sweeping, "research" => StaffJob.Researching, "performance" => StaffJob.Performing, _ => StaffJob.Idle };
		if ( kind == "repair" ) member.BusyTicks = 5;
		if ( kind == "strike" ) member.State = StaffState.OnStrike;
		var rig = new Rig( member );
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ), "no first world output yet" );
		Assert.AreEqual( 12, rig.Prepare( 0 ) );
		foreach ( var now in new[] { 0.07f, 0.14f, 0.21f } )
		{
			if ( kind == "walk" ) member.Step( -0.1f );
			Assert.AreEqual( 12, rig.Prepare( now ) );
		}
		var clip = kind switch { "walk" => StaffSpriteRules.Walk, "sweep" => StaffSpriteRules.Sweep, "repair" => StaffSpriteRules.Repair,
			"strike" => StaffSpriteRules.Strike, "research" => StaffSpriteRules.Research, "performance" => new StaffClip( 13, 4, 2, 2 ), _ => StaffSpriteRules.Stand };
		Assert.IsTrue( rig.Player( member ).Steps > 0 );
		var (slot, step) = StaffSpriteRules.ResolveSteps( clip, rig.Player( member ).Steps, rig.Atlas.Animations.Animations );
		Assert.AreEqual( kind == "walk" ? rig.Expected( slot, step, -1, 0 ) : rig.Expected( slot, step ), rig.Renderer.EmployeeFrame( member )!.Value );
		Assert.AreEqual( 1, rig.Renderer.DrawnStaff );
		Assert.AreEqual( clip.Script << 8 | clip.Slot, Get<int>( rig.View( member ), "ClipKey" ) );
	}

	[TestMethod]
	public void RepeatedReadsLeavePendingMovementClockClipAndNextWorldOutputIdentical()
	{
		var observedMember = Member(); var controlMember = Member();
		var observed = new Rig( observedMember ); var control = new Rig( controlMember );
		Level.Current = observed.Level;
		foreach ( var now in new[] { 0f, 0.07f, 0.14f } )
		{
			if ( now > 0 ) { observedMember.Step( -0.1f ); controlMember.Step( -0.1f ); }
			observed.Prepare( now ); control.Prepare( now );
		}
		observedMember.Step( 0, 0.25f ); controlMember.Step( 0, 0.25f );
		observedMember.PendingInterval = 148; controlMember.PendingInterval = 148;
		var before = observed.State( observedMember );
		var frame = observed.Renderer.EmployeeFrame( observedMember );
		Assert.IsNotNull( frame );
		for ( var index = 0; index < 100; index++ ) Assert.AreEqual( frame, observed.Renderer.EmployeeFrame( observedMember ) );
		Assert.AreEqual( before, observed.State( observedMember ) );
		Assert.AreEqual( control.State( controlMember ), observed.State( observedMember ) );
		observed.Prepare( 0.21f ); control.Prepare( 0.21f );
		Assert.AreEqual( control.State( controlMember ), observed.State( observedMember ) );
		CollectionAssert.AreEqual( Get<Vertex[]>( control.Renderer, "vertices" ).Take( 12 ).ToArray(), Get<Vertex[]>( observed.Renderer, "vertices" ).Take( 12 ).ToArray(), "subsequent actual quad output is unchanged" );
	}

	[DataTestMethod]
	[DataRow( 5 )]
	[DataRow( 8 )]
	[DataRow( 3 )]
	public void CameraRotationChangesWorldBankButNeverPortraitYawOrMirror( int directions )
	{
		var member = Member(); var rig = new Rig( member, directions );
		rig.Prepare( 0 ); member.Step( -0.1f ); rig.Prepare( 0.07f );
		rig.Prepare( 0.14f );
		var expected = rig.Expected( 1, 1, -1, 0 );
		var world = Get<Vertex[]>( rig.Renderer, "vertices" )[0].TexCoords;
		Assert.AreEqual( (directions == 8 ? 6 : 2, directions < 8), GuestSpriteAtlas.SelectOriginalDirection( -1f, 0f, 0f, directions ) );
		Assert.AreEqual( expected, rig.Renderer.EmployeeFrame( member )!.Value );
		// A positive quarter-turn maps heading sector 6 to sector 4. For three directions, mirrored
		// direction 4 clamps to 2 and legitimately repeats the initial UV. The negative turn reaches
		// sector 7 instead: stored direction 1 mirrored, or direction 7 in the full eight-direction bank.
		Camera.ViewMatrix = N.Matrix4x4.CreateRotationZ( -MathF.PI / 2 );
		var view = Camera.ViewMatrix;
		var yaw = GuestSpriteAtlas.CameraYaw( new N.Vector3( view.M11, view.M21, view.M31 ), -new N.Vector3( view.M13, view.M23, view.M33 ), parkCamera: false );
		Assert.AreEqual( -MathF.PI / 2, yaw, 0.0001f );
		var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( -1f, 0f, yaw, directions );
		Assert.AreEqual( (directions == 8 ? 7 : 1, directions < 8), (direction, mirror) );
		rig.Prepare( 0.14f ); // Same clock: camera only, no extra player step.
		var rotated = Get<Vertex[]>( rig.Renderer, "vertices" )[0].TexCoords;
		Assert.AreNotEqual( world, rotated );
		var frame = rig.Atlas.Frames[rig.Atlas.GetFrame( 1, direction, 1 )];
		Assert.AreEqual( (frame.X + (mirror ? frame.Width : 0)) / (float)rig.Atlas.Width, rotated.X, "actual world quad uses the rotated bank and mirror" );
		Assert.AreEqual( frame.Y / (float)rig.Atlas.Height, rotated.Y );
		Assert.AreEqual( expected, rig.Renderer.EmployeeFrame( member )!.Value );
		Assert.AreEqual( directions < 8, expected.Mirror );
	}

	[TestMethod]
	public void SameRoleMembersAndReusedIdsNeverInheritAnotherObjectsOutput()
	{
		var first = Member(); var second = Member( id: 2 );
		var rig = new Rig( first ); rig.Prepare( 0 );
		first.Step( -0.1f ); rig.Prepare( 0.07f ); rig.Prepare( 0.14f );
		rig.Members.Add( second ); rig.Prepare( 0.21f );
		var firstFrame = rig.Renderer.EmployeeFrame( first ); var secondFrame = rig.Renderer.EmployeeFrame( second );
		Assert.IsNotNull( firstFrame ); Assert.IsNotNull( secondFrame ); Assert.AreNotEqual( firstFrame, secondFrame );
		Assert.AreEqual( firstFrame, rig.Renderer.EmployeeFrame( first ) );
		rig.Members.Remove( first );
		Assert.IsNull( rig.Renderer.EmployeeFrame( first ) );
		var replacement = Member(); rig.Members.Add( replacement );
		Assert.IsNull( rig.Renderer.EmployeeFrame( replacement ), "same Id/ThingId/role/variant has no publication" );
		rig.Prepare( 0.28f );
		Assert.AreEqual( rig.Expected( 0, 0 ), rig.Renderer.EmployeeFrame( replacement )!.Value );
		replacement.ThingId++;
		Assert.IsNull( rig.Renderer.EmployeeFrame( replacement ), "same object cannot use a previous thing generation" );
	}

	[TestMethod]
	public void DescribeIsNeverAWorldPublisherEvenWithAnExistingView()
	{
		var member = Member(); var rig = new Rig( member );
		member.CellX = null; rig.Prepare( 0 );
		member.CellX = 4;
		Assert.IsNotNull( rig.Renderer.Describe( member, N.Vector3.UnitX, N.Vector3.UnitY ) );
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ) );
		rig.Prepare( 0.07f );
		Assert.IsNotNull( rig.Renderer.EmployeeFrame( member ) );
	}

	[DataTestMethod]
	[DataRow( "carried" )]
	[DataRow( "position" )]
	[DataRow( "resting" )]
	[DataRow( "picked-job" )]
	[DataRow( "hidden" )]
	[DataRow( "material" )]
	[DataRow( "bank" )]
	[DataRow( "texture" )]
	public void AnUnavailableWorldPassInvalidatesUntilTheNextSuccessfulPublication( string reason )
	{
		var member = Member(); var rig = new Rig( member ); rig.Prepare( 0 );
		var material = Get<object>( rig.Renderer, "material" ); var sprites = Get<object>( rig.Renderer, "sprites" );
		Assert.IsNotNull( rig.Renderer.EmployeeFrame( member ) );
		switch ( reason )
		{
			case "carried": Set( rig.Placement, "<Carried>k__BackingField", member ); break;
			case "position": member.CellX = null; break;
			case "resting": member.State = StaffState.Resting; break;
			case "picked-job": member.Job = StaffJob.PickedUp; break;
			case "hidden": rig.Renderer.Visible = false; break;
			case "material": Set( rig.Renderer, "material", null ); break;
			case "bank": Set( rig.Renderer, "sprites", StaffSpriteSet.Empty ); break;
			case "texture": MarkDisposed( rig.Textures[0] ); break;
		}
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ), "current eligibility rejects a source even before another pass" );
		Assert.AreEqual( 0, rig.Prepare( 0.07f ) );
		Set( rig.Placement, "<Carried>k__BackingField", null ); member.CellX = 4; member.State = StaffState.Idle; member.Job = StaffJob.Idle;
		rig.Renderer.Visible = true; Set( rig.Renderer, "material", material ); Set( rig.Renderer, "sprites", sprites );
		if ( reason == "texture" ) Set( rig.Textures[0].NativeTexture, "_destroyed", false );
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ), "restoring eligibility does not revive a skipped pass" );
		rig.Prepare( 0.14f ); Assert.IsNotNull( rig.Renderer.EmployeeFrame( member ) );
	}

	[TestMethod]
	public void TheAccessorUsesTheExistingWorldPositionGateRatherThanAddingANewOne()
	{
		var member = Member(); var rig = new Rig( member ); rig.Prepare( 0 );
		member.CellY = null; // The established world pass skips only null X; Track retains the last position.
		Assert.AreEqual( 12, rig.Prepare( 0.07f ) );
		Assert.AreEqual( rig.Expected( 0, 0 ), rig.Renderer.EmployeeFrame( member )!.Value );
		member.CellX = null;
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ) );
		Assert.AreEqual( 0, rig.Prepare( 0.14f ) );
	}

	[TestMethod]
	public void RegistrationOwningLevelAndEntirelySkippedPassesRejectStaleOutput()
	{
		var member = Member(); var rig = new Rig( member ); rig.Prepare( 0 );
		Level.Current = Empty<Level>(); Assert.IsNull( rig.Renderer.EmployeeFrame( member ) );
		Level.Current = rig.Level;
		var owner = rig.Renderer.Level; rig.Renderer.Level = Empty<Level>();
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ) ); rig.Renderer.Level = owner;
		Entity.All = new(); Assert.IsNull( rig.Renderer.EmployeeFrame( member ) );
		rig.Level.Render(); // Real Level entry with no StaffRenderer invocation.
		Entity.All.Add( rig.Renderer );
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ), "re-registration cannot reuse an earlier world pass" );
		rig.Prepare( 0.07f ); Assert.IsNotNull( rig.Renderer.EmployeeFrame( member ) );
		rig.Renderer.Visible = false; rig.Level.Render(); rig.Renderer.Visible = true;
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ), "actual Entity.Render -> OnRender early-return invalidates" );
		rig.Prepare( 0.14f ); Assert.IsNotNull( rig.Renderer.EmployeeFrame( member ) );
		var replacement = new Rig( member, level: rig.Level );
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ) ); Assert.IsNull( replacement.Renderer.EmployeeFrame( member ) );
		replacement.Prepare( 0.21f ); Assert.IsNotNull( replacement.Renderer.EmployeeFrame( member ) );
	}

	[TestMethod]
	public void ReloadReplacementAndDeletionNeverReturnRetiredTextureHandles()
	{
		var member = Member(); var rig = new Rig( member ); rig.Prepare( 0 );
		var before = rig.Renderer.EmployeeFrame( member )!.Value; var old = rig.Textures[0]; var replacement = TextureSentinel();
		var replace = typeof( StaffRenderer ).GetMethod( "ReplaceAtlas", BindingFlags.Instance | BindingFlags.NonPublic )!;
		Assert.AreSame( old, replace.Invoke( rig.Renderer, new object[] { 0, replacement } ) );
		MarkDisposed( old );
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ), "reload invalidates before any new world output" );
		rig.Prepare( 0.07f );
		Assert.AreSame( replacement.NativeTexture, rig.Renderer.EmployeeFrame( member )!.Value.Texture );
		Assert.AreNotSame( before.Texture, rig.Renderer.EmployeeFrame( member )!.Value.Texture );
		// Delete's real invalidation/texture-detachment path, without running GPU disposal on sentinels.
		Set( rig.Renderer, "material", null );
		Global.Render = Empty<Renderer>(); Set( Global.Render, "_deletionQueue", new List<Action>() );
		rig.Renderer.Delete(); MarkDisposed( replacement );
		Assert.AreEqual( 0, rig.Textures.Count );
		Assert.AreEqual( 1, Get<List<Action>>( Global.Render, "_deletionQueue" ).Count );
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ) );
		Assert.IsNull( rig.Renderer.PortraitFrame( member.Type, 0, 1 ) );
		Entity.All.Add( rig.Renderer );
		Assert.IsNull( rig.Renderer.EmployeeFrame( member ), "deleted renderer cannot be resurrected by registration" );
	}

	[TestMethod]
	public void HireCallbackIsUnchangedAndLiveRoleChangesStillUpdateTheEmployeeFallbackClock()
	{
		var economy = EconomyTestData.Park();
		var handyman = EconomyTestData.HireBest( economy, StaffType.Handyman );
		var mechanic = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		handyman.CellX = handyman.CellY = mechanic.CellX = mechanic.CellY = 4;
		handyman.Job = StaffJob.Sweeping; mechanic.BusyTicks = 5;
		var hud = StaffWindowTests.Hud( economy );
		var rig = new Rig( handyman, level: Get<Level>( hud, "level" ), members: economy.Staff.Members.ToList() );
		rig.Prepare( 0 );
		hud.OpenStaffWindow();
		var candidate = hud.SelectedCandidate!;
		var hire = (UiSpriteFigure)hud.Stack.Top!.Find( "portraitFigure" )!;
		Assert.AreEqual( rig.Renderer.PortraitFrame( candidate.Type, StaffSpriteRules.KeyOf( candidate ), 0 ), hire.Frame() );
		rig.Prepare( 0.2f );
		Assert.AreEqual( rig.Renderer.PortraitFrame( candidate.Type, StaffSpriteRules.KeyOf( candidate ), 0.2f ), hire.Frame() );
		hud.OpenEmployeeWindow( handyman );
		var employee = (UiSpriteFigure)hud.Stack.Top!.Find( "portraitFigure" )!;
		Assert.AreEqual( rig.Renderer.EmployeeFrame( handyman ), employee.Frame() );
		Assert.AreEqual( 0.2f, Get<float>( hud, "employeePortraitStart" ) );
		rig.Prepare( 0.3f ); hud.OpenEmployeeWindow( mechanic );
		Assert.AreEqual( rig.Renderer.EmployeeFrame( mechanic ), employee.Frame() );
		Assert.AreEqual( 0.3f, Get<float>( hud, "employeePortraitStart" ), "a live role change must still reset the old fallback key/clock" );
		mechanic.State = StaffState.Resting; rig.Prepare( 0.5f );
		Assert.AreEqual( rig.Renderer.PortraitFrame( mechanic.Type, StaffSpriteRules.KeyOf( mechanic ), 0.5f - 0.3f ), employee.Frame() );
		Assert.AreEqual( rig.Renderer.PortraitFrame( candidate.Type, StaffSpriteRules.KeyOf( candidate ), 0.5f ), hire.Frame(), "employee reads never change the hire callback's independent clock" );
	}

	[TestMethod]
	public void ActualEmployeeCallbackTracksSelectionReopenRenameModalAndFallbackClock()
	{
		var economy = EconomyTestData.Park();
		var first = EconomyTestData.HireBest( economy, StaffType.Handyman );
		var second = EconomyTestData.HireBest( economy, StaffType.Handyman );
		first.CellX = first.CellY = second.CellX = second.CellY = 4;
		var hud = StaffWindowTests.Hud( economy );
		var level = Get<Level>( hud, "level" );
		var rig = new Rig( first, level: level, members: economy.Staff.Members.ToList() );
		first.Job = StaffJob.Sweeping;
		rig.Prepare( 0 ); rig.Prepare( 0.07f ); rig.Prepare( 0.14f );
		Assert.IsTrue( hud.OpenEmployeeWindow( first ) );
		var screen = hud.Stack.Top!; var figure = (UiSpriteFigure)screen.Find( "portraitFigure" )!;
		Assert.AreEqual( rig.Renderer.EmployeeFrame( first ), figure.Frame() );
		Assert.AreEqual( Time.Now, Get<float>( hud, "employeePortraitStart" ), "clock was initialized despite a live output" );
		var state = rig.State( first );
		var saved = ParkSaveFile.Serialize( economy );
		var random = economy.World.State;
		for ( var index = 0; index < 100; index++ ) Assert.AreEqual( rig.Renderer.EmployeeFrame( first ), figure.Frame() );
		Assert.AreEqual( saved, ParkSaveFile.Serialize( economy ), "repeated actual HUD reads preserve the complete economy save state" );
		Assert.AreEqual( random, economy.World.State );
		Assert.IsTrue( hud.CycleEmployee( true ) ); Assert.AreSame( second, hud.Employee );
		Assert.AreEqual( rig.Renderer.EmployeeFrame( second ), figure.Frame() );
		Assert.IsTrue( hud.CycleEmployee( true ) ); Assert.AreSame( first, hud.Employee );
		Assert.AreEqual( rig.Renderer.EmployeeFrame( first ), figure.Frame() );
		hud.CloseEmployeeWindow(); Assert.IsNull( figure.Frame() );
		Time.Now = 0.25f;
		hud.OpenEmployeeWindow( first ); Assert.AreEqual( rig.Renderer.EmployeeFrame( first ), figure.Frame() );
		var start = Get<float>( hud, "employeePortraitStart" ); Assert.AreEqual( 0.25f, start );
		hud.OpenEmployeeRenameDialog(); Assert.AreEqual( "employeeRename", hud.Stack.Top!.Name );
		Time.Now = 0.39f;
		Assert.AreEqual( rig.Renderer.EmployeeFrame( first ), figure.Frame(), "lower employee callback needs no employee Update under a modal" );
		((UiTextField)hud.Stack.Top.Find( "name" )!).Text = "Renamed";
		hud.Stack.Top.Find( "apply" )!.Activate();
		Assert.AreEqual( "Renamed", first.Name );
		Assert.AreEqual( rig.Renderer.EmployeeFrame( first ), figure.Frame() );
		Assert.AreEqual( start, Get<float>( hud, "employeePortraitStart" ), "name-only refresh retains the existing fallback clock" );
		first.Name = null; Assert.AreEqual( state, rig.State( first ), "UI actions did not advance or restart world presentation" );
		Assert.AreEqual( saved, ParkSaveFile.Serialize( economy ), "navigation and portrait reads leave no simulation changes after reverting the requested rename" );
		Assert.AreEqual( random, economy.World.State );
		first.State = StaffState.Resting; rig.Prepare( 0.46f );
		Assert.AreEqual( rig.Renderer.PortraitFrame( first.Type, StaffSpriteRules.KeyOf( first ), 0.46f - start ), figure.Frame(), "source loss resumes the exact existing hire-loop phase" );
		first.State = StaffState.Idle; rig.Prepare( 0.53f );
		Assert.AreEqual( rig.Renderer.EmployeeFrame( first ), figure.Frame() );
	}
}

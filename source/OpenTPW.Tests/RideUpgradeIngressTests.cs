using System;
using System.Buffers.Binary;
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

[TestClass, DoNotParallelize]
public class RideUpgradeIngressTests
{
	private string root = null!;
	private List<Entity> entities = null!;
	private Logger log = null!;
	private CameraMode camera = null!;
	private GameEdition edition;
	private bool text;
	private List<InputButton> down = null!, last = null!;
	private static void Set( object o, string f, object v ) => o.GetType().GetField( f, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, v );
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private sealed class EmptyCatalog : IBuildCatalog { public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>(); }

	[TestInitialize]
	public void Setup()
	{
		entities = Entity.All; Entity.All = new(); log = Log; Log = new(); camera = Camera.Mode; Camera.Mode = new ParkCameraMode(); edition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld );
		text = Input.TextEntryActive; Input.TextEntryActive = false; down = Input.KeysDown; last = Input.LastKeysDown; Input.KeysDown = new(); Input.LastKeysDown = new();
		root = Path.Combine( Path.GetTempPath(), "opentpw-price-" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( root );
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
	}
	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() ) entity.Delete(); Entity.All = entities; Log = log; Camera.Mode = camera; GameEditions.Select( edition ); Input.TextEntryActive = text; Input.KeysDown = down; Input.LastKeysDown = last; Directory.Delete( root, true );
	}
	private OriginalObject Object( int info, BuildCategory category, RideScriptWorld world, string extra = "" )
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( extra + $"Info.Id {info}\nInfo.Name synthetic\nInfo.IsChoosable 1\nInfo.WhichUIType {(int)category}\nUsageInfo.InitPricePerUse 30\nUpgrades[0].InitCapacity 1\nUpgrades[0].InitDuration 10\nUpgrades[1].InitCapacity 3\nUpgrades[1].InitDuration 20\nUpgrades[1].InitSpeed 100\nUpgrades[1].QueueWaitTimeConstant 200\n", "synthetic.sam" ) } );
		var entry = new ObjectCatalogEntry( "jungle", category == BuildCategory.Shops ? ObjectCategory.Shop : category == BuildCategory.Sideshows ? ObjectCategory.Sideshow : ObjectCategory.Ride, "/synthetic", settings, new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
		return new OriginalObject( entry, new ObjectPlacement( 1, 1, 0, N.Vector2.Zero, 0 ), world );
	}
	private ParkHud Fixture( BuildCategory category, out Level level, out ParkEconomy economy, out OriginalObject first, out OriginalObject second, out GuestEconomyBridge bridge, ParkGameMode mode = ParkGameMode.FullSimulation, string extra = "" )
	{
		VisitorWorldSelectionTests.Fixture( out var guests, out _ );
		level = Empty<Level>(); var objects = new ParkObjects( null!, new SandboxParkGrid() );
		Set( level, "<Objects>k__BackingField", objects ); Set( level, "<Guests>k__BackingField", guests ); Set( level, "<CellTool>k__BackingField", new CellBuildTool() ); Set( level, "<LastActionMessage>k__BackingField", "" );
		Set( level, "<Paths>k__BackingField", new ParkPathBuilder( guests.Grid.Cells, guests.Grid, (ParkEconomy?)null, new SandboxParkGrid() ) );
		economy = EconomyTestData.Park( mode: mode ); var info = category == BuildCategory.Shops ? 1203 : category == BuildCategory.Sideshows ? 1303 : 1100;
		first = Object( info, category, objects.ScriptWorld, extra ); second = Object( info, category, objects.ScriptWorld );
		((List<OriginalObject>)objects.Objects).AddRange( new[] { first, second } );
		var one = economy.RegisterExisting( info ); var two = economy.RegisterExisting( info );
		Set( level, "economyInstances", new Dictionary<OriginalObject, int> { { first, one.Id }, { second, two.Id } } );
		Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, mode == ParkGameMode.InstantAction ? ParkStartKind.InstantAction : ParkStartKind.FullSimulation ) );
		typeof( Level ).GetMethod( "LinkEconomy", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, new object[] { first, one.Id } );
		typeof( Level ).GetMethod( "LinkEconomy", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, new object[] { second, two.Id } );
		typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, Array.Empty<object>() );
		var current = economy; bridge = new GuestEconomyBridge( () => current, guests ); bridge.Link( first.Runtime.AttractionId, one.Id ); bridge.Link( second.Runtime.AttractionId, two.Id );
		var hud = new ParkHud( level, OriginalUiTests.FakeStrings(), new EconomyParkStatus( () => current ), new EmptyCatalog(), new HudHost() ); hud.SelectObject( first ); return hud;
	}
	private static UiContext Context()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( n => throw new FileNotFoundException( n ) ) ) { Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = _ => null };
	}
	private static void Open( ParkHud hud, UiContext context )
	{
		hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		var button = hud.Screen.Find( "rideUpgrade" ) as UiButton; Assert.IsNotNull( button, "Selected ride has no upgrade action." ); Assert.IsTrue( button.Visible && button.Enabled );
		hud.Screen.Focus( button ); hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false ); Assert.AreEqual( "rideUpgrade", hud.Stack.Top!.Name );
	}

	[TestMethod]
	public void SelectedRideConfirmationQueuesExactOwnerAndMechanicAppliesDefaults()
	{
		var hud = Fixture( BuildCategory.Rides, out var level, out var economy, out var first, out var second, out _ ); var context = Context();
		EconomyTestData.HireBest( economy, StaffType.Researcher ); economy.SetResearchEffort( ResearchCategory.Ride, 0 );
		for ( var i = 0; i < 100 && !economy.Research.IsAvailable( 1100, 1 ); i++ ) economy.AdvanceDays( 1 );
		Assert.IsTrue( economy.Research.IsAvailable( 1100, 1 ) ); var cash = economy.Balance; var ledger = economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherCosts );
		Open( hud, context ); Assert.AreEqual( cash, economy.Balance ); StringAssert.Contains( ((UiLabel)hud.Stack.Top!.Find( "preview" )!).Text(), "ui29 1" ); StringAssert.Contains( ((UiLabel)hud.Stack.Top.Find( "preview" )!).Text(), "400" );
		hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false );
		economy.TryGetObject( level.GetEconomyInstance( first )!.Value, out var state ); economy.TryGetObject( level.GetEconomyInstance( second )!.Value, out var other );
		Assert.AreEqual( cash - 400, economy.Balance ); Assert.AreEqual( ledger + 400, economy.Ledger.CurrentTotals[LedgerCategory.OtherCosts] ); Assert.AreEqual( 400L, state.TotalSpent ); Assert.AreEqual( 1, state.PendingLevel ); Assert.AreEqual( 0, state.Level ); Assert.AreEqual( 0, other.PendingLevel ); Assert.IsFalse( first.IsOpen ); Assert.IsTrue( second.IsOpen );
		economy.AdvanceDays( 1 ); Assert.AreEqual( 0, state.Level, "No mechanic means pending, not refusal/refund." );
		EconomyTestData.HireBest( economy, StaffType.Mechanic ); for ( var i = 0; i < 20 && state.Level < 1; i++ ) economy.AdvanceDays( 1 );
		Assert.AreEqual( 1, state.Level ); Assert.AreEqual( 3, first.Visitors.Capacity ); Assert.AreEqual( 1, second.Visitors.Capacity ); Assert.AreEqual( 1, first.Runtime.GetVariable( RideVariables.VAR_CAPACITY ), "Live CAP is not reset on completion." );
	}
	private static void Research( ParkEconomy economy )
	{
		EconomyTestData.HireBest( economy, StaffType.Researcher ); economy.SetResearchEffort( ResearchCategory.Ride, 0 );
		for ( var i = 0; i < 100 && !economy.Research.IsAvailable( 1100, 1 ); i++ ) economy.AdvanceDays( 1 );
		Assert.IsTrue( economy.Research.IsAvailable( 1100, 1 ) );
	}
	[DataTestMethod, DataRow( "research" ), DataRow( "instant" ), DataRow( "pending" ), DataRow( "funds" ), DataRow( "bankrupt" )]
	public void ExistingPurchaseRefusalsLeaveLedgerAndDefaultsUnchanged( string refusal )
	{
		var hud = Fixture( BuildCategory.Rides, out var level, out var economy, out var owner, out _, out _, refusal == "instant" ? ParkGameMode.InstantAction : ParkGameMode.FullSimulation ); var context = Context();
		if ( refusal is "funds" or "bankrupt" ) Research( economy );
		economy.TryGetObject( level.GetEconomyInstance( owner )!.Value, out var state );
		if ( refusal == "pending" ) state.PendingLevel = 1;
		if ( refusal == "funds" ) economy.Ledger.Post( LedgerCategory.OtherCosts, economy.Balance );
		if ( refusal == "bankrupt" ) Set( economy, "<IsBankrupt>k__BackingField", true );
		var cash = economy.Balance; var spent = state.TotalSpent; var p = owner.Visitors.Parameters; var vars = owner.Script!.Variables.ToArray(); var pending = state.PendingLevel;
		Open( hud, context ); hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false );
		Assert.AreEqual( "rideUpgrade", hud.Stack.Top!.Name ); Assert.AreEqual( cash, economy.Balance ); Assert.AreEqual( spent, state.TotalSpent ); Assert.AreEqual( pending, state.PendingLevel ); Assert.AreSame( p, owner.Visitors.Parameters ); CollectionAssert.AreEqual( vars, owner.Script.Variables );
		Assert.IsTrue( ((UiLabel)hud.Stack.Top.Find( "error" )!).Text().Length > 0 );
	}
	[DataTestMethod, DataRow( "custom" ), DataRow( "stop" ), DataRow( "sparse" ), DataRow( "coaster" ), DataRow( "prototype" ), DataRow( "tpi" ), DataRow( "readonly" ), DataRow( "noeconomy" ), DataRow( "delete" ), DataRow( "unmap" ), DataRow( "ambiguous" ), DataRow( "removedDuplicate" ), DataRow( "fault" ), DataRow( "tierMismatch" ), DataRow( "full" )]
	public void UnsupportedTargetRefusesBeforeCharge( string gate )
	{
		if ( gate == "coaster" ) File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		var hud = Fixture( BuildCategory.Rides, out var level, out var economy, out var owner, out var other, out _, extra: gate == "coaster" ? "Bumper.WhichTrackType 3\n" : "" ); var context = Context();
		economy.TryGetObject( level.GetEconomyInstance( owner )!.Value, out var state ); var map = (Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )!;
		if ( gate == "custom" ) owner.Visitors.Parameters = owner.Visitors.Parameters with { }; if ( gate == "stop" ) owner.Runtime.Stop(); if ( gate == "tpi" ) GameEditions.Select( GameEdition.ThemeParkInc );
		if ( gate == "readonly" ) Set( level, "<Visit>k__BackingField", Empty<ParkVisitInfo>() ); if ( gate == "noeconomy" ) Set( hud, "<Status>k__BackingField", new NoEconomyStatus() ); if ( gate == "delete" ) owner.Delete(); if ( gate == "unmap" ) map.Remove( owner ); if ( gate is "ambiguous" or "removedDuplicate" ) map[other] = state.Id; if ( gate == "removedDuplicate" ) other.Delete(); if ( gate == "fault" ) owner.Script!.RestoreStoppedFault( "synthetic" );
		if ( gate == "tierMismatch" ) state.Level = 1; if ( gate == "full" ) { state.Level = 1; Assert.IsTrue( owner.Runtime.TryApplyCatalogueTierDefaults( 1, out _ ) ); }
		if ( gate == "sparse" ) Set( owner.Entry, "<Upgrades>k__BackingField", owner.Entry.Upgrades.Where( row => row.Level != 1 ).ToArray() );
		if ( gate == "coaster" ) Assert.IsNotNull( owner.Runtime.Coaster );
		if ( gate == "prototype" ) { var prototype = Empty<PrototypeRide>(); typeof( OriginalObject ).GetField( "<Runtime>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( prototype, owner.Runtime ); hud.SelectObject( prototype ); }
		var cash = economy.Balance; hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		var button = (UiButton)hud.Screen.Find( "rideUpgrade" )!; if ( gate is not "ambiguous" and not "removedDuplicate" ) Assert.IsFalse( button.Visible && button.Enabled ); button.Activate(); Assert.AreEqual( "hud", hud.Stack.Top!.Name ); Assert.AreEqual( cash, economy.Balance ); Assert.AreEqual( 0, state.PendingLevel );
	}
	[TestMethod]
	public void TheInfoArmsTrackButtonOpensTheCoasterToolOnASelectedCoasterAndOnlyThere()
	{
		var hud = Fixture( BuildCategory.Rides, out var level, out _, out var owner, out var other, out _, extra: "Bumper.WhichTrackType 3\n" ); var context = Context();
		Assert.IsNotNull( owner.Runtime.Coaster );
		Set( level, "coasterBuilders", new Dictionary<OriginalObject, CoasterBuilder>() );
		hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		var button = (UiButton)hud.Screen.Find( "editTrack" )!;
		Assert.IsTrue( button.Visible, "a selected coaster shows the ride window's b_track button (0x3e2a, shown for a ride with a track type, 0x4ad53a)" );
		Assert.AreEqual( "b_track", button.Model );
		button.Activate();
		Assert.IsTrue( level.IsCoasterToolActive && level.CoasterToolStation == owner, "0x4af200 → 0x4988c0 → 0x4474d0 opens the coaster tool on the selected ride" );
		Assert.IsNotNull( level.CoasterPanel );
		Assert.AreEqual( CoasterToolButton.Build, level.CoasterPanel!.Selected, "an unfinished track opens on the build button" );
		Assert.IsFalse( owner.IsOpen, "the tool closes the ride object" );
		hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		Assert.IsFalse( button.Visible, "the arm closes with the tool" );
		Assert.IsTrue( hud.Screen.Find( "coasterBuild" )!.Visible && hud.Screen.Find( "coasterPanelShield" )!.Visible, "the panel shows" );
		level.ExitCoasterTool();
		Assert.IsFalse( hud.Screen.Find( "coasterBuild" )!.Visible is true && level.IsCoasterToolActive );
		// a ride without a track has no button
		hud.SelectObject( null );
		hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		Assert.IsFalse( ((UiButton)hud.Screen.Find( "editTrack" )!).Visible );
	}

	[DataTestMethod, DataRow( "cancel" ), DataRow( "right" ), DataRow( "selection" ), DataRow( "delete" ), DataRow( "remap" ), DataRow( "replace" ), DataRow( "custom" ), DataRow( "stop" ), DataRow( "retired" ), DataRow( "otherTop" ), DataRow( "readonly" ), DataRow( "tool" ), DataRow( "camera" ), DataRow( "retract" ), DataRow( "capture" ), DataRow( "duplicate" ), DataRow( "removedDuplicate" )]
	public void CancelOrStaleConfirmationNeverCharges( string gate )
	{
		var hud = Fixture( BuildCategory.Rides, out var level, out var economy, out var owner, out var other, out _ ); var context = Context(); Research( economy );
		economy.TryGetObject( level.GetEconomyInstance( owner )!.Value, out var state ); Open( hud, context ); var apply = (UiButton)hud.Stack.Top!.Find( "apply" )!; var cash = economy.Balance;
		if ( gate == "selection" ) hud.SelectObject( other ); if ( gate == "delete" ) owner.Delete(); if ( gate == "custom" ) owner.Visitors.Parameters = owner.Visitors.Parameters with { }; if ( gate == "stop" ) owner.Runtime.Stop();
		if ( gate is "duplicate" or "removedDuplicate" ) { ((Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )!)[other] = state.Id; if ( gate == "removedDuplicate" ) other.Delete(); }
		if ( gate == "remap" ) ((Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )!)[owner] = level.GetEconomyInstance( other )!.Value;
		if ( gate == "replace" ) ((SortedDictionary<int, ParkObjectState>)typeof( ParkEconomy ).GetField( "objects", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( economy )!)[state.Id] = new ParkObjectState { Id = state.Id, InfoId = state.InfoId, Kind = state.Kind };
		if ( gate == "readonly" ) Set( level, "<Visit>k__BackingField", Empty<ParkVisitInfo>() ); if ( gate == "tool" ) level.IsPlacing = true; if ( gate == "camera" ) Camera.Mode = new CameraMode(); if ( gate == "retract" ) hud.SetInfoArm( false );
		if ( gate == "retired" ) hud.Stack.Clear(); if ( gate == "otherTop" ) hud.Stack.Push( new UiScreen( "other" ) );
		if ( gate is "retired" or "otherTop" ) apply.Activate(); else hud.Update( context, new UiInput( new N.Vector2( -1 ), false, false, false, gate == "right", UiKeys.Accept | (gate == "cancel" ? UiKeys.Back : UiKeys.None) ), gate == "capture", false, false );
		Assert.AreEqual( cash, economy.Balance ); Assert.AreEqual( 0, state.PendingLevel ); Assert.AreEqual( 0, state.TotalSpent );
		if ( gate is "cancel" or "right" ) { Assert.AreEqual( "hud", hud.Stack.Top!.Name ); apply.Activate(); Assert.AreEqual( cash, economy.Balance ); }
	}
	[DataTestMethod, DataRow( "mouse" ), DataRow( "keyboard" ), DataRow( "text" ), DataRow( "overlay" ), DataRow( "retract" ), DataRow( "tool" )]
	public void CapturedOrHiddenActionCannotOpen( string gate )
	{
		var hud = Fixture( BuildCategory.Rides, out var level, out _, out _, out _, out _ ); var context = Context(); if ( gate == "overlay" ) hud.Stack.Push( new UiScreen( "overlay" ) ); if ( gate == "retract" ) hud.SetInfoArm( false ); if ( gate == "tool" ) level.IsPlacing = true;
		hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), gate == "mouse", gate == "keyboard", gate == "text" );
		((UiButton)hud.Screen.Find( "rideUpgrade" )!).Activate(); Assert.IsFalse( hud.Stack.Screens.Any( s => s.Name == "rideUpgrade" ) );
	}

	[TestMethod]
	public void TheCallButtonCallsAMechanicClosingTheRideAndCancelsTheCall()
	{
		var hud = Fixture( BuildCategory.Rides, out var level, out var economy, out var first, out _, out _ ); var context = Context();
		void Frame() => hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		Frame();
		var button = (UiButton)hud.Screen.Find( "callStaff" )!;
		Assert.IsTrue( button.Visible ); Assert.AreEqual( "b_callmech", button.Model );
		Assert.IsFalse( button.Enabled, "no mechanic hired (UIHELPTEXT 574)" );
		EconomyTestData.HireBest( economy, StaffType.Mechanic ); Frame();
		Assert.IsTrue( button.Enabled );
		Assert.IsTrue( economy.TryGetObject( level.GetEconomyInstance( first )!.Value, out var state ) );
		Assert.IsTrue( first.IsOpen );
		button.Activate(); Frame();
		Assert.IsTrue( state.MaintenanceRequested ); Assert.IsFalse( first.IsOpen, "the call closes the ride (0x004DFE30)" ); Assert.IsTrue( button.Selected!() );
		button.Activate(); Frame();
		Assert.IsFalse( state.MaintenanceRequested ); Assert.IsTrue( first.IsOpen, "cancelled, it reopens" );
	}

	[TestMethod]
	public void SimultaneousMouseApplyAndAcceptPostsOnlyOneUpgrade()
	{
		var hud = Fixture( BuildCategory.Rides, out _, out var economy, out _, out _, out _ ); var context = Context(); Research( economy ); var cash = economy.Balance; Open( hud, context );
		var apply = (UiButton)hud.Stack.Top!.Find( "apply" )!; var at = apply.ScreenRect( context.Canvas ).Center;
		hud.Update( context, new UiInput( at, true, true, false, false, UiKeys.None ), false, false, false );
		hud.Update( context, new UiInput( at, false, false, true, false, UiKeys.Accept ), false, false, false );
		Assert.AreEqual( cash - 400, economy.Balance ); Assert.AreEqual( "hud", hud.Stack.Top!.Name );
	}
	[TestMethod]
	public void RefusedPurchaseCanRetryAfterResearchAndOldCallbacksCannotAffectFreshModal()
	{
		var hud = Fixture( BuildCategory.Rides, out _, out var economy, out _, out _, out _ ); var context = Context(); Open( hud, context ); var old = hud.Stack.Top!;
		hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false ); Assert.AreSame( old, hud.Stack.Top );
		Research( economy ); var cash = economy.Balance; hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false ); Assert.AreEqual( cash - 400, economy.Balance );
		Open( hud, context ); var fresh = hud.Stack.Top!; ((UiButton)old.Find( "apply" )!).Activate(); old.Back!(); Assert.AreSame( fresh, hud.Stack.Top ); Assert.AreEqual( cash - 400, economy.Balance );
	}
	[TestMethod]
	public void CaptureHeldUntilReleaseCannotOpenTheActionAndShopSlotIsExclusive()
	{
		var hud = Fixture( BuildCategory.Rides, out _, out _, out _, out _, out _ ); var context = Context(); hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		var action = (UiButton)hud.Screen.Find( "rideUpgrade" )!; Assert.IsFalse( hud.Screen.Find( "shopPrice" )!.Visible ); var at = action.ScreenRect( context.Canvas ).Center;
		hud.Update( context, new UiInput( at, true, true, false, false, UiKeys.None ), true, false, false ); hud.Update( context, new UiInput( at, true, false, false, false, UiKeys.None ), false, false, false ); hud.Update( context, new UiInput( at, false, false, true, false, UiKeys.None ), false, false, false ); Assert.AreEqual( "hud", hud.Stack.Top!.Name );
		hud.Update( context, new UiInput( at, true, true, false, false, UiKeys.None ), false, false, false ); hud.Update( context, new UiInput( at, false, false, true, false, UiKeys.None ), false, false, false ); Assert.AreEqual( "rideUpgrade", hud.Stack.Top!.Name );
	}

	[TestMethod]
	public void PurePreflightRefusesInvalidTierWithoutChangingVmOrQueueState()
	{
		Fixture( BuildCategory.Rides, out var level, out var economy, out var owner, out _, out _ ); economy.TryGetObject( level.GetEconomyInstance( owner )!.Value, out var state );
		var vm = System.Text.Json.JsonSerializer.Serialize( owner.Script!.CaptureSnapshot() ); var parameters = owner.Visitors.Parameters;
		Assert.IsTrue( level.CanBuyCatalogueUpgrade( owner, state ) ); Assert.IsFalse( owner.Runtime.CanBuyCatalogueTier( -1, 0 ) ); Assert.IsFalse( owner.Runtime.CanBuyCatalogueTier( int.MaxValue, int.MinValue ) ); Assert.IsFalse( owner.Runtime.CanBuyCatalogueTier( 0, 2 ) );
		Assert.AreEqual( vm, System.Text.Json.JsonSerializer.Serialize( owner.Script.CaptureSnapshot() ) ); Assert.AreSame( parameters, owner.Visitors.Parameters ); Assert.AreEqual( 0, state.PendingLevel );
	}
	[TestMethod]
	public void PriceAndReadOnlyRideButtonsCannotShareVisibleUpgradeGeometry()
	{
		var hud = Fixture( BuildCategory.Shops, out _, out _, out _, out _, out _ ); var context = Context(); hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		Assert.IsTrue( hud.Screen.Find( "shopPrice" )!.Visible ); Assert.IsFalse( hud.Screen.Find( "rideUpgrade" )!.Visible );
	}

	[DataTestMethod, DataRow( "mouse" ), DataRow( "keyboard" ), DataRow( "text" )]
	public void MidGestureCaptureConsumesReleaseBeforeFreshAction( string capture )
	{
		var hud = Fixture( BuildCategory.Rides, out _, out _, out _, out _, out _ ); var context = Context(); hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		var at = hud.Screen.Find( "rideUpgrade" )!.ScreenRect( context.Canvas ).Center;
		hud.Update( context, new UiInput( at, true, true, false, false, UiKeys.None ), false, false, false );
		hud.Update( context, new UiInput( at, true, false, false, false, UiKeys.None ), capture == "mouse", capture == "keyboard", capture == "text" );
		hud.Update( context, new UiInput( at, false, false, true, false, UiKeys.None ), false, false, false ); Assert.AreEqual( "hud", hud.Stack.Top!.Name );
		hud.Update( context, new UiInput( at, true, true, false, false, UiKeys.None ), false, false, false ); hud.Update( context, new UiInput( at, false, false, true, false, UiKeys.None ), false, false, false ); Assert.AreEqual( "rideUpgrade", hud.Stack.Top!.Name );
	}

	[TestMethod]
	public void PerFrameExactTierLookupAddsNoDefaultParameterAllocations()
	{
		Fixture( BuildCategory.Rides, out _, out _, out var owner, out _, out _ );
		for ( var i = 0; i < 64; i++ ) { Assert.IsTrue( owner.Runtime.CanBuyCatalogueTier( 0, 1 ) ); Assert.IsTrue( owner.Runtime.CanCaptureCatalogueDefaults( 0, out _ ) ); }
		// Each loop is measured three times and the smallest count kept: a one-off runtime allocation on the thread (a JIT
		// tier-up, say) can land in a single pass, while a per-call allocation shows in every pass.
		var available = true;
		long bytes = long.MaxValue, inheritedBytes = long.MaxValue;
		for ( var pass = 0; pass < 3; pass++ )
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			for ( var i = 0; i < 2048; i++ ) available &= owner.Runtime.CanBuyCatalogueTier( 0, 1 );
			bytes = Math.Min( bytes, GC.GetAllocatedBytesForCurrentThread() - before );
			var baseline = GC.GetAllocatedBytesForCurrentThread();
			for ( var i = 0; i < 2048; i++ ) available &= owner.Runtime.CanCaptureCatalogueDefaults( 0, out _ );
			inheritedBytes = Math.Min( inheritedBytes, GC.GetAllocatedBytesForCurrentThread() - baseline );
		}
		Assert.IsTrue( available ); Assert.AreEqual( inheritedBytes, bytes, "Next-tier lookup must add no QueueParameters or captured predicates to the existing eligibility getters." );
	}

}

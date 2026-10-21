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
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>Synthetic fixed models and a looping duck script exercise the real object lifecycle without original assets or a GPU.</summary>
[TestClass, DoNotParallelize]
public class ParkFinaleTests
{
	private string root = null!;
	private List<Entity> entities = null!;
	private Logger log = null!;
	private CameraMode camera = null!;
	private GameEdition edition;
	private BaseFileSystem files = null!;
	private Level currentLevel = null!;
	private bool text;
	private ushort duck;
	private List<InputButton> down = null!, last = null!;
	private Key[] frameKeys = null!;
	private static HashSet<Key> FrameKeys => (HashSet<Key>)typeof( Input ).GetField( "frameKeys", BindingFlags.Static | BindingFlags.NonPublic )!.GetValue( null )!;
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object owner, string field, object value ) => owner.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( owner, value );
	private static void Invoke( object owner, string method, params object[] arguments ) => owner.GetType().GetMethod( method, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( owner, arguments );
	private sealed class EmptyCatalog : IBuildCatalog { public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>(); }

	[TestInitialize]
	public void Initialize()
	{
		entities = Entity.All; Entity.All = new(); log = Log; Log = new(); files = FileSystem;
		currentLevel = Level.Current; Level.Current = null!;
		camera = Camera.Mode; Camera.Mode = new ParkCameraMode(); edition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld );
		text = Input.TextEntryActive; Input.TextEntryActive = false; down = Input.KeysDown; last = Input.LastKeysDown; Input.KeysDown = new(); Input.LastKeysDown = new();
		frameKeys = FrameKeys.ToArray(); FrameKeys.Clear();
		duck = GameAudio.MusicDuckRequest;
		root = Directory.CreateTempSubdirectory( "opentpw-finale-" ).FullName;
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.DIPMUSIC, 1 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( Enum.GetNames<RideVariables>() ) );
		FileSystem = new BaseFileSystem( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() ) entity.Delete();
		Entity.All = entities; Level.Current = currentLevel; Log = log; FileSystem = files; Camera.Mode = camera; GameEditions.Select( edition );
		Input.TextEntryActive = text; Input.KeysDown = down; Input.LastKeysDown = last; GameAudio.SetMusicDuck( duck );
		FrameKeys.Clear(); FrameKeys.UnionWith( frameKeys );
		Directory.Delete( root, true );
	}

	private ObjectCatalogEntry Entry( string name = "End", bool fixedItem = true, int id = 1605, string archive = "/synthetic" ) => new( "jungle", ObjectCategory.Feature, archive,
		new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name {name}\nInfo.DontApplyOffset {(fixedItem ? 1 : 0)}\n", "synthetic.sam" ) } ),
		new ObjectShape( new[] { "." } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), FileSystem, null );

	private static ParkObjects Objects( params ObjectCatalogEntry[] entries )
	{
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "jungle", entries }, null )!;
		return new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 123 ) );
	}

	private Level LevelFor( ParkObjects objects, ParkGameMode mode = ParkGameMode.InstantAction, ParkEconomy? economy = null )
	{
		var level = Empty<Level>();
		var guests = new GuestSimulation( new GuestPathGrid( 32, 32 ), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		Set( level, "<Guests>k__BackingField", guests );
		Set( level, "<Paths>k__BackingField", new ParkPathBuilder( guests.Grid.Cells, guests.Grid, (ParkEconomy?)null, new SandboxParkGrid() ) );
		Set( level, "<Objects>k__BackingField", objects );
		Set( level, "<CellTool>k__BackingField", new CellBuildTool() );
		Set( level, "<LastActionMessage>k__BackingField", "" );
		Set( level, "economyInstances", new Dictionary<OriginalObject, int>() );
		Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy ?? EconomyTestData.Park( mode: mode ), mode == ParkGameMode.InstantAction ? ParkStartKind.InstantAction : ParkStartKind.FullSimulation ) );
		Invoke( level, "SetupParkGate" );
		return level;
	}

	/// <summary>An economy whose catalogue also knows the Seaplane and the finale's End, as the shipped Features.sam files declare them (Info.WhichUIType 4: fixed items).</summary>
	private static ParkEconomy FixedItemEconomy( ParkGameMode mode )
	{
		EconomyObjectInfo Fixed( int id, string name, string archive ) => new( id, name, ParkObjectKind.FixedItem, ResearchCategory.Ride, 0,
			new[] { new UpgradeLevelInfo( 0, 0, 0, 0, 0, new[] { 50, 30, 20, 10 } ) }, null, null, null, 0, 0, 0, 0, 0, 0, archive );
		return new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( EconomyTestData.Catalog().Objects.Concat( new[] { Fixed( 1602, "Seaplane", "features/seaplane.wad" ),
			Fixed( 1605, "End", "features/end.wad" ) } ) ), mode, 7 );
	}

	/// <summary>The fixed item's economy record: bound, of its type, uncharged; and the save's repair finds nothing left to bind (a shared park's capture refuses an unbound object).</summary>
	private static ParkObjectState RecordOf( Level level, OriginalObject item, int infoId )
	{
		Assert.IsTrue( level.GetEconomyInstance( item ) is int instance && level.Park!.Economy.TryGetObject( instance, out _ ), $"{item.Entry.DisplayName} is bound to an economy record" );
		level.Park!.Economy.TryGetObject( level.GetEconomyInstance( item )!.Value, out var state );
		Assert.AreEqual( infoId, state!.InfoId );
		Assert.IsTrue( state.Imported, "the record is uncharged: nothing was bought" );
		var unbound = (IEnumerable<OriginalObject>)typeof( Level ).GetMethod( "UnboundObjects", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null )!;
		Assert.IsFalse( unbound.Any(), "no placed object is left without its binding" );
		return state;
	}

	private static void Complete( Level level ) => Invoke( level.Park!, "ForwardEvent", new ParkEvent( 0, ParkEventKind.ResearchAllDone, 0, 0, 0, "" ) );
	private static UiContext Context( float time = 0 )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
		{ Canvas = new UiCanvas( 2048, 1536 ), Time = time, ResolveTexture = _ => null };
	}
	private static ParkHud Hud( Level level ) => new( level, OriginalUiTests.FakeStrings(), new EconomyParkStatus( () => level.Park!.Economy ), new EmptyCatalog(), new HudHost() );

	[TestMethod]
	public void MissingDefinitionAndOtherModesConsumeNoObjectsOrSeeds()
	{
		var objects = Objects();
		Assert.IsNull( ParkFinale.StartForResearchCompletion( objects, ParkGameMode.InstantAction ) );
		Assert.AreEqual( 1, objects.NextPlacementSeed );
		objects = Objects( Entry() );
		Assert.IsNull( ParkFinale.StartForResearchCompletion( objects, ParkGameMode.FullSimulation ) );
		Assert.IsNull( ParkFinale.StartForResearchCompletion( objects, (ParkGameMode)99 ) );
		Assert.AreEqual( 0, objects.Objects.Count ); Assert.AreEqual( 1, objects.NextPlacementSeed );
	}

	[TestMethod]
	public void NameMatchIsCaseInsensitiveFixedAndIndependentOfLocalizedDisplayName()
	{
		var ordinary = Entry( "End", false, 1401 );
		var wrong = Entry( "Endless", true, 1606 ); wrong.DisplayName = "End";
		var end = Entry( "eNd" ); end.DisplayName = "Localized finale";
		var objects = Objects( ordinary, wrong, end );
		var placed = ParkFinale.StartForResearchCompletion( objects, ParkGameMode.InstantAction )!;
		Assert.AreSame( end, placed.Entry );
		Assert.AreEqual( (0, 0, 0, 0f), (placed.Placement.X, placed.Placement.Y, placed.Placement.Rotation, placed.Placement.BaseHeight) );
		Assert.AreEqual( objects.Grid.Origin, placed.Placement.GridOrigin );
		Assert.AreEqual( 1 ^ objects.ObjectScriptKey, placed.CreationSeed );
		Assert.AreEqual( 2, objects.NextPlacementSeed );
		Assert.AreSame( placed, ParkFinale.StartForResearchCompletion( objects, ParkGameMode.InstantAction ) );
		Assert.AreEqual( 1, objects.Objects.Count ); Assert.AreEqual( 2, objects.NextPlacementSeed );
	}

	[TestMethod]
	public void ExistingAndRestoredFinalesAreAdoptedWithoutASecondAllocation()
	{
		var entry = Entry(); var objects = Objects( entry );
		var restored = objects.AddRestored( entry, 50, 60, 90, true, 9 ^ objects.ObjectScriptKey, 25 );
		objects.RestoreSeedAllocator( 10 );
		Assert.IsTrue( ParkFinale.IsActive( objects ) );
		Assert.AreSame( restored, ParkFinale.StartForResearchCompletion( objects, ParkGameMode.InstantAction ) );
		Assert.AreEqual( 10, objects.NextPlacementSeed ); Assert.AreEqual( 26, objects.NextAttractionId );
		var level = LevelFor( objects );
		Assert.IsTrue( level.ResearchFinaleActive ); Assert.IsTrue( level.Park!.Economy.WinningSequenceActive!() );
		Complete( level ); Assert.AreEqual( 1, objects.Objects.Count );
	}

	[TestMethod]
	public void CompletionNotificationCreatesOnlyOneUnchargedObjectAndBindingAloneCreatesNothing()
	{
		var level = LevelFor( Objects( Entry() ) ); var economy = level.Park!.Economy;
		var cash = economy.Balance; var records = economy.Objects.Count;
		Assert.IsFalse( level.ResearchFinaleActive ); Assert.IsFalse( economy.WinningSequenceActive!() );
		Complete( level ); Complete( level );
		Assert.AreEqual( 1, level.Objects.Objects.Count ); Assert.IsTrue( economy.WinningSequenceActive!() );
		Assert.AreEqual( cash, economy.Balance ); Assert.AreEqual( records, economy.Objects.Count );
	}

	[TestMethod]
	public void TheFinalesEndGetsItsUnchargedEconomyRecordAsTheOriginalsConstructorGivesIt()
	{
		var level = LevelFor( Objects( Entry() ), economy: FixedItemEconomy( ParkGameMode.InstantAction ) ); var economy = level.Park!.Economy;
		var cash = economy.Balance; var records = economy.Objects.Count;
		Complete( level ); Complete( level );
		var state = RecordOf( level, level.Objects.Objects.Single(), 1605 );
		Assert.AreEqual( records + 1, economy.Objects.Count, "the second notification adopts End and makes no second record" );
		Assert.AreEqual( cash, economy.Balance );
		Assert.AreEqual( 1, economy.Counters[ParkCounters.Built( 1605 )], "the constructor counts the type" );
		Assert.AreEqual( economy.Tick, state.BuiltTick );
	}

	[TestMethod]
	public void AnArrivalVehicleGetsItsUnchargedEconomyRecordAtItsFirstTrip()
	{
		var level = LevelFor( Objects( Entry( "Plane", id: 1602, archive: "/seaplane" ) ), ParkGameMode.FullSimulation, FixedItemEconomy( ParkGameMode.FullSimulation ) );
		Set( level, "demolishedQueues", new HashSet<OriginalObject>() );
		// the level's own wiring makes the vehicles' host
		Invoke( level, "ConnectObjectsToGuests" );
		var economy = level.Park!.Economy; var cash = economy.Balance; var records = economy.Objects.Count;
		Assert.IsTrue( level.Guests!.ArrivalVehicles!.Create( 1 ) );
		var state = RecordOf( level, level.Objects.Objects.Single(), 1602 );
		Assert.AreEqual( records + 1, economy.Objects.Count );
		Assert.AreEqual( cash, economy.Balance );
		Assert.AreEqual( 1, economy.Counters[ParkCounters.Built( 1602 )], "the constructor counts the type" );
		Assert.AreEqual( economy.Tick, state.BuiltTick );
		// a save made now has nothing to repair
		Invoke( level, "RepairMissingEconomyBindings" );
		Assert.AreEqual( records + 1, economy.Objects.Count );
	}

	[TestMethod]
	public void FullSimulationVisitsAndThemeParkIncDoNotStartOrGuardFinales()
	{
		var full = LevelFor( Objects( Entry() ), ParkGameMode.FullSimulation ); Complete( full );
		Assert.IsFalse( full.ResearchFinaleActive ); Assert.AreEqual( 0, full.Objects.Objects.Count );
		var visit = LevelFor( Objects( Entry() ) ); Set( visit, "<Visit>k__BackingField", Empty<ParkVisitInfo>() ); Complete( visit );
		Assert.IsFalse( visit.ResearchFinaleActive ); Assert.AreEqual( 0, visit.Objects.Objects.Count );
		var inc = LevelFor( Objects( Entry() ) ); GameEditions.Select( GameEdition.ThemeParkInc ); Complete( inc );
		Assert.IsFalse( inc.ResearchFinaleActive ); Assert.AreEqual( 0, inc.Objects.Objects.Count );
		Assert.IsFalse( inc.Park!.Economy.WinningSequenceActive!() );
	}

	[TestMethod]
	public void DismissalStopsTheLoopReleasesMusicAndParticlesAndIsIdempotent()
	{
		var objects = Objects( Entry() ); var end = ParkFinale.StartForResearchCompletion( objects, ParkGameMode.InstantAction )!;
		end.Script!.Advance( FixedStepClock.TickDuration );
		Assert.AreEqual( 1, GameAudio.MusicDuckRequest ); Assert.AreEqual( 1, end.Script.MusicDuckByte );
		var killed = new List<int>(); var previousKiller = OriginalObjectRuntime.ParticleKiller; var previousAlive = OriginalObjectRuntime.ParticleAlive;
		OriginalObjectRuntime.ParticleKiller = killed.Add; OriginalObjectRuntime.ParticleAlive = _ => true;
		try
		{
			end.Runtime.RestoreSnapshot( end.Runtime.CaptureSnapshot() with { ParticleObjects = new[] { new ParticleObjectRecord { Slot = 1, Handle = 0x10001 } } } );
			Assert.IsTrue( ParkFinale.Dismiss( objects ) );
			Assert.IsFalse( ParkFinale.Dismiss( objects ) ); Assert.IsFalse( ParkFinale.IsActive( objects ) );
			Assert.IsTrue( end.IsDeleted ); Assert.AreEqual( RideVMState.Halted, end.Script.State );
			Assert.AreEqual( 0, objects.ScriptWorld.Scripts.Count ); Assert.AreEqual( 0, GameAudio.MusicDuckRequest );
			CollectionAssert.AreEqual( new[] { 0x10001 }, killed );
		}
		finally { OriginalObjectRuntime.ParticleKiller = previousKiller; OriginalObjectRuntime.ParticleAlive = previousAlive; }
	}


	[TestMethod]
	public void DismissalStopsARealHeldSoundFromTheFinaleScript()
	{
		var saved = new Dictionary<FieldInfo, object?>();
		void Audio( string field, object value )
		{
			var target = typeof( GameAudio ).GetField( field, BindingFlags.NonPublic | BindingFlags.Static )!;
			saved.Add( target, target.GetValue( null ) ); target.SetValue( null, value );
		}
		using var mixer = new AudioMixer( null );
		try
		{
			var system = new SoundEventSystem( mixer, 7 ); var category = Empty<SoundCategory>(); var catalog = Empty<SoundCatalog>();
			var sound = new SoundEntry( 100, 100, 0, 0, 0, 0, 1, 100, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
			Set( catalog, "<Events>k__BackingField", new[] { new SoundEvent( 44, 0, 8, 0, new[] { sound } ) } );
			Set( category, "<Catalog>k__BackingField", catalog );
			var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
			decoded[(category, 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, 200 ).ToArray(), 1, 0 );
			Audio( "events", system ); Audio( "<LevelRides>k__BackingField", category ); Audio( "<Rides>k__BackingField", category );
			File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ADDOBJ, 5, -1, 44, 1 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( Enum.GetNames<RideVariables>() ) );
			var objects = Objects( Entry() ); var end = ParkFinale.StartForResearchCompletion( objects, ParkGameMode.InstantAction )!;
			end.Script!.Advance( FixedStepClock.TickDuration ); Assert.AreEqual( 1, end.Runtime.HeldSounds.Count );
			var voice = mixer.Voices.Single(); Assert.IsTrue( voice.Loops && !voice.IsFinished );
			ParkFinale.Dismiss( objects ); Assert.IsTrue( voice.IsFinished ); Assert.AreEqual( 0, end.Runtime.HeldSounds.Count );
		}
		finally { foreach ( var pair in saved ) pair.Key.SetValue( null, pair.Value ); }
	}

	[TestMethod]
	public void DeletedAndDuplicateRestoredObjectsCannotKeepTheGuardAlive()
	{
		var entry = Entry(); var objects = Objects( entry );
		var dead = objects.Add( entry, 0, 0, 0 ); dead.Delete();
		Assert.IsFalse( ParkFinale.IsActive( objects ) );
		var live = ParkFinale.StartForResearchCompletion( objects, ParkGameMode.InstantAction ); Assert.AreNotSame( dead, live );
		objects.Add( entry, 0, 0, 0 ); Assert.IsTrue( ParkFinale.Dismiss( objects ) );
		Assert.IsFalse( ParkFinale.IsActive( objects ) );
	}

	[TestMethod]
	public void BankGuardEndsWithTheActualObjectAndBindsToAReplacementEconomy()
	{
		var level = LevelFor( Objects( Entry() ) ); Complete( level );
		var path = Path.Combine( root, "economy.json" ); var runtime = level.Park!; var old = runtime.Economy;
		runtime.Save( path ); runtime.Load( path ); Invoke( level, "BuildParkGate" );
		Assert.AreNotSame( old, runtime.Economy ); Assert.IsTrue( runtime.Economy.WinningSequenceActive!() );
		Complete( level ); Assert.AreEqual( 1, level.Objects.Objects.Count );
		Assert.IsTrue( level.DismissResearchFinale() ); Assert.IsFalse( runtime.Economy.WinningSequenceActive!() );
		Invoke( level, "BuildParkGate" ); Assert.AreEqual( 0, level.Objects.Objects.Count, "rebinding a loaded economy never replays completion" );
	}

	[TestMethod]
	public void ActualBankruptcyMessageIsSuppressedOnlyUntilDismissal()
	{
		var level = LevelFor( Objects( Entry() ) ); Complete( level ); var economy = level.Park!.Economy;
		Invoke( economy, "HandleBankruptcyMessage" ); Assert.IsFalse( economy.IsBankrupt );
		level.DismissResearchFinale(); Invoke( economy, "HandleBankruptcyMessage" ); Assert.IsTrue( economy.IsBankrupt );
	}

	[TestMethod]
	public void WinningScreenOwnsPauseClicksAndEscapeWithoutLeakingIntoTheHud()
	{
		var level = LevelFor( Objects( Entry() ) ); Complete( level ); var hud = Hud( level ); var context = Context();
		var speed = hud.Status.Speed;
		Assert.IsTrue( hud.Update( context, UiInput.Key( UiKeys.Pause ), false, false, false ) );
		Assert.AreEqual( "researchFinale", hud.Stack.Top!.Name ); Assert.IsFalse( hud.Paused ); Assert.AreEqual( speed, hud.Status.Speed );
		Assert.IsTrue( ((ParkCameraMode)Camera.Mode).ModalOpen );
		foreach ( var modifier in new[] { Key.ControlLeft, Key.ShiftRight, Key.AltLeft } )
		{
			FrameKeys.Add( modifier ); hud.Update( context, UiInput.Key( UiKeys.Back ), false, false, false );
			Assert.IsTrue( level.ResearchFinaleActive, $"{modifier}+Escape is not the winning handler's exit" ); FrameKeys.Clear();
		}
		Assert.IsTrue( hud.Update( context, UiInput.Click( new N.Vector2( 20, 20 ) ), false, false, false ) );
		Assert.IsTrue( level.ResearchFinaleActive );
		Assert.IsTrue( hud.Update( context, UiInput.Key( UiKeys.Back | UiKeys.Pause ), false, false, false ) );
		Assert.IsFalse( level.ResearchFinaleActive ); Assert.AreEqual( "hud", hud.Stack.Top!.Name ); Assert.AreEqual( speed, hud.Status.Speed );
		hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
		Assert.IsFalse( ((ParkCameraMode)Camera.Mode).ModalOpen ); Assert.IsFalse( ((ParkCameraMode)Camera.Mode).UiCapturesKeyboard );
	}

	[TestMethod]
	public void WinningScreenPumpsTerminalExpiryAndDelayedReleaseWhileItOwnsCapturedInput()
	{
		var level = LevelFor( Objects( Entry() ) ); Complete( level ); var hud = Hud( level ); var context = Context();
		long now = 0;
		hud.ChallengeMessageClock = () => now;
		Assert.IsTrue( ChallengeMessageRecords.TryGet( 1, ChallengePresentationKind.Success, out var record ) );
		Assert.IsTrue( hud.PostChallengeTerminal( record, "synthetic terminal" ) );
		var speed = hud.Status.Speed;
		Assert.IsTrue( hud.Update( context, UiInput.Key( UiKeys.Pause ), false, false, false ) );
		Assert.AreEqual( speed, hud.Status.Speed, "the finale keeps ownership of Pause" );
		now = 25000;
		hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), true, true, true );
		Assert.IsTrue( hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached );
		now = 30000;
		hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), true, true, true );
		Assert.IsFalse( hud.IsPendingMessageVisible( 0 ) );
		Assert.AreEqual( 1, hud.PendingMessages.Count );
		now = 30499;
		hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), true, true, true );
		Assert.AreEqual( 1, hud.PendingMessages.Count );
		now = 30500;
		hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), true, true, true );
		Assert.AreEqual( 0, hud.PendingMessages.Count );
		Assert.IsTrue( level.ResearchFinaleActive, "timer delivery does not dismiss or bypass the full-screen owner" );
	}

	[TestMethod]
	public void WinningScreenStillReleasesAnAlreadyDeletedTerminalDuringExplicitUserPause()
	{
		var level = LevelFor( Objects( Entry() ) ); var hud = Hud( level ); var context = Context();
		long now = 0;
		hud.ChallengeMessageClock = () => now;
		Assert.IsTrue( ChallengeMessageRecords.TryGet( 1, ChallengePresentationKind.Failure, out var record ) );
		Assert.IsTrue( hud.PostChallengeTerminal( record, "synthetic terminal" ) );
		hud.OpenPendingMessage( 0 );
		hud.DeleteOpenedMessage();
		hud.SetUserGameSpeed( GameSpeed.Paused );
		Complete( level );
		now = 500;
		Assert.IsTrue( hud.Update( context, UiInput.Key( UiKeys.Pause ), false, false, false ) );
		Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
		Assert.AreEqual( 0, hud.PendingMessages.Count, "delayed final release does not consult lifetime suspension" );
		Assert.IsTrue( level.ResearchFinaleActive );
	}

	[TestMethod]
	public void ResumeButtonDismissesAndAStaleScreenCannotDismissANewFinale()
	{
		var level = LevelFor( Objects( Entry() ) ); Complete( level ); var hud = Hud( level ); var context = Context();
		hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), false, false, false ); var previous = hud.Stack.Top!;
		hud.Update( context, UiInput.Click( new N.Vector2( 1000, 1440 ) ), false, false, false ); Assert.IsFalse( level.ResearchFinaleActive );
		Complete( level ); hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
		previous.Back!(); Assert.IsTrue( level.ResearchFinaleActive ); Assert.AreEqual( "researchFinale", hud.Stack.Top!.Name );
	}

	/// <summary>[EXT:online-coop-commands] The winning screen's exit leaves the HUD as a <see cref="DismissFinaleCommand"/>; with no finale left a second one is refused and logged.</summary>
	[TestMethod]
	public void TheWinningScreensExitIsADismissFinaleCommand()
	{
		var level = LevelFor( Objects( Entry() ) ); Complete( level ); var hud = Hud( level ); var context = Context();
		hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
		hud.Update( context, UiInput.Click( new N.Vector2( 1000, 1440 ) ), false, false, false ); Assert.IsFalse( level.ResearchFinaleActive );
		Assert.AreEqual( 1, level.Commands.Log.Count ); Assert.IsInstanceOfType( level.Commands.Log[0].Command, typeof( DismissFinaleCommand ) );
		Assert.IsFalse( level.Commands.Issue( new DismissFinaleCommand() ).Accepted ); Assert.AreEqual( 2, level.Commands.Log.Count );
	}

	[TestMethod]
	public void NativeInputCapturesCannotDismissTheFinale()
	{
		var level = LevelFor( Objects( Entry() ) ); Complete( level ); var hud = Hud( level ); var context = Context();
		hud.Update( context, UiInput.Key( UiKeys.Back ), false, true, false ); Assert.IsTrue( level.ResearchFinaleActive );
		hud.Update( context, UiInput.Key( UiKeys.Back ), false, false, true ); Assert.IsTrue( level.ResearchFinaleActive );
		hud.Update( context, UiInput.Click( new N.Vector2( 1000, 1440 ) ), true, false, false ); Assert.IsTrue( level.ResearchFinaleActive );
		hud.Update( context, UiInput.Key( UiKeys.Back ), false, false, false ); Assert.IsFalse( level.ResearchFinaleActive );
	}

	[TestMethod]
	public void TheWinningRightClickWaitsAndExternalRemovalRetiresItsScreen()
	{
		var previous = GameOptions.Current.RmbCancel;
		try
		{
			GameOptions.Current.RmbCancel = true;
			var level = LevelFor( Objects( Entry() ) ); Complete( level ); var hud = Hud( level ); var context = Context();
			var right = new UiInput( new N.Vector2( 20, 20 ), false, false, false, true, UiKeys.None );
			hud.Update( context, right, false, false, false ); Assert.IsTrue( level.ResearchFinaleActive );
			context.Time = 2; hud.Update( context, right, false, false, false ); Assert.IsTrue( level.ResearchFinaleActive );
			context.Time = 3; hud.Update( context, right, false, false, false ); Assert.IsFalse( level.ResearchFinaleActive );
			Complete( level ); hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
			level.Objects.Remove( level.Objects.Objects.Single() );
			Assert.IsTrue( hud.Update( context, UiInput.Key( UiKeys.Back ), false, false, false ) );
			Assert.AreEqual( "hud", hud.Stack.Top!.Name, "external deletion consumes the frame instead of opening the pause menu" );
			hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
			Assert.IsFalse( ((ParkCameraMode)Camera.Mode).ModalOpen ); Assert.IsFalse( ((ParkCameraMode)Camera.Mode).UiCapturesKeyboard );
		}
		finally { GameOptions.Current.RmbCancel = previous; }
	}

	[TestMethod]
	public void DismissalQueuesTheFinaleThingForNormalEndOfTurnDestruction()
	{
		var objects = Objects( Entry() ); objects.Things = new ThingPool();
		var end = ParkFinale.StartForResearchCompletion( objects, ParkGameMode.InstantAction )!;
		Assert.AreNotEqual( 0, end.ThingId ); Assert.IsTrue( objects.Things.IsUsed( end.ThingId ) );
		Assert.IsTrue( ParkFinale.Dismiss( objects ) ); Assert.IsFalse( ParkFinale.IsActive( objects ) );
		Assert.IsTrue( objects.Things.IsUsed( end.ThingId ), "the slot remains allocated until the normal deferred-destroy pass" );
		objects.Things.DestroyQueued(); Assert.IsFalse( objects.Things.IsUsed( end.ThingId ) );
	}

	[DataTestMethod]
	[DataRow( false, true, 3f, false )]
	[DataRow( true, false, 3f, false )]
	[DataRow( true, true, 2f, false )]
	[DataRow( true, true, 2.001f, true )]
	public void RightClickRequiresTheOptionAndStrictlyMoreThanTwoSeconds( bool pressed, bool enabled, float elapsed, bool allowed ) =>
		Assert.AreEqual( allowed, ParkHud.FinaleRightClickAllowed( pressed, enabled, elapsed ) );
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>S01/M04: actual CPU level setup, original import and save restoration, without original assets or a graphics device.</summary>
[TestClass, DoNotParallelize]
public class StaffSpriteReconstructionTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly StaffType[] Roles = Enum.GetValues<StaffType>();
	private readonly List<StaffSpritePresentation> presentations = new();
	private UpgradeCatalogueDefaultsTests fixture = null!;
	private BaseFileSystem previousFiles = null!;

	[TestInitialize]
	public void Setup()
	{
		// The shared synthetic fixture saves/restores Entity.All, Log and the selected edition.
		previousFiles = FileSystem;
		fixture = new();
		fixture.Setup();
		FileSystem = null!;
	}

	[TestCleanup]
	public void Cleanup()
	{
		try
		{
			foreach ( var presentation in presentations ) presentation.Dispose();
			fixture.Cleanup();
		}
		finally { FileSystem = previousFiles; }
	}

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string field, object? value ) => target.GetType().GetField( field, Private )!.SetValue( target, value );
	private static void Call( object target, string method, params object[] args ) => target.GetType().GetMethod( method, Private )!.Invoke( target, args );
	private static FixedStepClock Clock( Level level ) => (FixedStepClock)typeof( Level ).GetField( "simulationClock", Private )!.GetValue( level )!;
	private static void Tick( ParkEconomy economy, long tick ) => Set( economy, "<Tick>k__BackingField", tick );

	private static StaffMember[] AddRoles( ParkEconomy economy ) => Roles.Select( role =>
	{
		var member = economy.Staff.AddImported( role, 1, 0, 90, StaffState.Idle, 0 );
		member.CellX = 4.25f + (int)role;
		member.CellY = 9.75f;
		return member;
	} ).ToArray();

	private static Level BareLevel( ParkEconomy economy )
	{
		var level = Empty<Level>();
		var grid = new GuestPathGrid( 32, 32 );
		grid.SetPath( 2, 3, true );
		Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		Set( level, "<Guests>k__BackingField", new GuestSimulation( grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false } );
		Set( level, "<Objects>k__BackingField", new ParkObjects( null!, new SandboxParkGrid() ) );
		Set( level, "<LevelName>k__BackingField", "jungle" );
		Set( level, "<Seed>k__BackingField", WorldSeed.Default );
		Set( level, "simulationClock", new FixedStepClock() );
		Set( level, "economyInstances", new Dictionary<OriginalObject, int>() );
		level.Streams.LinkGenerator();
		return level;
	}

	private StaffSpritePresentation SetupStaff( Level level, bool syntheticMetadata = false )
	{
		// The full-world fixture has a placeholder terrain only to satisfy save eligibility.
		// Remove it during actual SetupStaff so this test never constructs StaffRenderer.
		var original = level.OriginalPark;
		Set( level, "<OriginalPark>k__BackingField", null );
		try { Call( level, "SetupStaff" ); }
		finally { Set( level, "<OriginalPark>k__BackingField", original ); }
		var presentation = level.StaffSprites!;
		presentations.Add( presentation );
		Assert.IsNotNull( presentation );
		Assert.IsNull( level.StaffRenderer );
		Assert.IsFalse( presentation.Ready );
		Assert.AreEqual( 0, presentation.Registry.Count );
		if ( syntheticMetadata )
		{
			presentation.Dispose();
			presentation = new StaffSpritePresentation( StaffOwnerFixture.Catalog(), () => level.Park!.Economy.Staff.Members,
				level.Park!.Economy.Tick, ready: false );
			presentation.AttachRoster( level.Park.Economy.Staff );
			Set( level, "<StaffSprites>k__BackingField", presentation );
			presentations.Add( presentation );
		}
		return presentation;
	}

	private static void AssertUnready( Level level )
	{
		Assert.IsFalse( level.StaffSprites!.Ready );
		foreach ( var member in level.Park!.Economy.Staff.Members )
		{
			Assert.IsNotNull( member.BodySprite );
			member.BodySprite!.EnsureInitialized();
			member.BodySprite.CreateSource();
			Assert.IsFalse( member.BodySprite.Initialized );
			Assert.IsNull( member.BodySprite.SourceTicket, "setup and reconciliation cannot allocate before final restoration" );
		}
		Assert.AreEqual( 0, level.StaffSprites.Registry.Count );
	}

	private static void AssertFresh( StaffMember member, StaffSpritePresentation presentation, int pending, bool missing = false )
	{
		var body = member.BodySprite!;
		Assert.IsTrue( body.Initialized );
		Assert.IsTrue( body.Reconstructed );
		Assert.IsTrue( body.IsLive );
		Assert.IsNotNull( body.SourceTicket );
		var ticket = body.SourceTicket!.Value;
		Assert.AreEqual( presentation.Session, ticket.Session );
		Assert.AreEqual( StaffSpriteOwnerKind.Source, ticket.Kind );
		Assert.AreSame( member, ticket.Identity.Member );
		Assert.AreEqual( (member.Id, member.ThingId), (ticket.Identity.Id, ticket.Identity.ThingId) );
		Assert.IsTrue( presentation.Registry.TryResolve( ticket, out var state ) );
		Assert.AreEqual( (uint)ticket.Slot, state.Handle );
		Assert.AreEqual( unchecked( GuestSimulation.BodySpriteSampleAtTick( presentation.EpochHostTick ) + 62 ), state.Due );
		Assert.AreEqual( 62, state.Interval );
		Assert.AreEqual( 0u, state.OutputValid );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Stand, 0 ), state.CurrentPc );
		Assert.AreEqual( state.CurrentPc, state.RestartPc );
		Assert.AreEqual( 0u, state.LastPc.Word );
		Assert.AreEqual( StaffSpriteRunState.Running, state.RunState );
		Assert.AreEqual( StaffSpriteVmState.StackCapacity, state.StackPointer );
		Assert.AreEqual( (0, 0, 0), (state.IfDepth, state.CallDepth, state.LoopDepth) );
		Assert.IsTrue( state.Stack.All( word => word == 0 ) && state.DrawCache.All( word => word == 0 ), "no old VM stack or draw history is reconstructed" );
		Assert.AreEqual( StaffSpriteMetadataCatalog.KindOf( member.Type ), state[StaffSpriteRegister.Kind] );
		Assert.AreEqual( missing ? 0u : 8u, state[StaffSpriteRegister.FrameCount] );
		foreach ( var register in new[] { StaffSpriteRegister.X, StaffSpriteRegister.Y, StaffSpriteRegister.Z, StaffSpriteRegister.Heading,
			StaffSpriteRegister.Flags, StaffSpriteRegister.Slot, StaffSpriteRegister.Frame, StaffSpriteRegister.Scratch, StaffSpriteRegister.Intro, StaffSpriteRegister.Hold } )
			Assert.AreEqual( 0u, state[register], $"{register} is fresh before publication" );
		Assert.AreEqual( 255u, state[StaffSpriteRegister.Alpha] );
		Assert.AreEqual( BitConverter.SingleToUInt32Bits( 1f ), state[StaffSpriteRegister.PrimaryScale] );
		Assert.AreEqual( BitConverter.SingleToUInt32Bits( 1f ), state[StaffSpriteRegister.SecondaryScale] );
		Assert.AreEqual( pending, body.PendingTemplate );
		Assert.AreEqual( 0, body.PendingInterval );
		Assert.AreEqual( missing ? StaffSpriteDiagnostic.MetadataUnavailable : StaffSpriteDiagnostic.None, body.Diagnostic );
	}

	[TestMethod]
	public void ActualSetupWithoutOriginalParkOrFileSystemDefersAllFiveSourcesUntilFinalClockAndPosition()
	{
		var economy = EconomyTestData.Park();
		var members = AddRoles( economy );
		var level = BareLevel( economy );
		var random = economy.World.State;
		var presentation = SetupStaff( level );
		Assert.IsNull( level.OriginalPark );
		Assert.IsNull( FileSystem );
		AssertUnready( level );
		Tick( economy, 1234 );
		for ( var index = 0; index < members.Length; index++ )
		{
			members[index].CellX = 12.125f + index;
			members[index].CellY = 17.875f;
		}
		Call( level, "InitializeStaffSprites" );
		Assert.IsTrue( presentation.Ready );
		Assert.AreEqual( 1234L, presentation.EpochHostTick );
		Assert.AreEqual( members.Length, presentation.Registry.Count );
		foreach ( var member in members ) AssertFresh( member, presentation, 3, missing: true );
		var tickets = members.Select( member => member.BodySprite!.SourceTicket ).ToArray();
		var states = members.Select( member => StaffOwnerFixture.Words( member.BodySprite!.ReadSourceState()! ) ).ToArray();
		Call( level, "InitializeStaffSprites" );
		for ( var index = 0; index < members.Length; index++ )
		{
			Assert.AreEqual( tickets[index], members[index].BodySprite!.SourceTicket );
			CollectionAssert.AreEqual( states[index], StaffOwnerFixture.Words( members[index].BodySprite!.ReadSourceState()! ) );
		}
		level.PresentStaffSprites();
		foreach ( var member in members )
		{
			var state = member.BodySprite!.ReadSourceState()!;
			Assert.AreEqual( BitConverter.SingleToUInt32Bits( member.CellX!.Value ), state[StaffSpriteRegister.X] );
			Assert.AreEqual( BitConverter.SingleToUInt32Bits( member.CellY!.Value ), state[StaffSpriteRegister.Y] );
		}
		Assert.AreEqual( random, economy.World.State );
	}

	[TestMethod]
	public void ActualLegacyEconomyLoadReplacesAllRoleTicketsAndDiscardsOldVmAndWindowHistory()
	{
		var economy = EconomyTestData.Park();
		var oldMembers = AddRoles( economy );
		Tick( economy, 321 );
		var level = BareLevel( economy );
		var previous = SetupStaff( level, syntheticMetadata: true );
		Call( level, "InitializeStaffSprites" );
		using var window = new EmployeePortraitOwner( previous );
		window.FullRefresh( oldMembers[0], -0.5f, 0.4f, 0.975f );
		var oldTickets = oldMembers.Select( member => member.BodySprite!.SourceTicket!.Value ).ToArray();
		foreach ( var member in oldMembers )
		{
			Assert.IsTrue( member.BodySprite!.TryResolve( out var state ) );
			StaffOwnerFixture.Stamp( state, 0x7100 );
		}
		var random = economy.World.State;
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-staff-reconstruction-{Guid.NewGuid():N}.json" );
		try
		{
			level.SavePark( path );
			level.LoadPark( path );
			var presentation = level.StaffSprites!;
			presentations.Add( presentation );
			Assert.IsTrue( previous.IsDisposed );
			Assert.AreEqual( EmployeePortraitState.Closed, window.State );
			Assert.IsNull( window.CloneTicket );
			Assert.AreNotEqual( previous.Session, presentation.Session );
			Assert.AreEqual( Roles.Length, presentation.Registry.Count );
			foreach ( var ticket in oldTickets ) Assert.IsFalse( presentation.Registry.TryResolve( ticket, out _ ) );
			foreach ( var member in level.Park!.Economy.Staff.Members )
			{
				Assert.IsFalse( oldMembers.Any( old => ReferenceEquals( old, member ) ) );
				AssertFresh( member, presentation, 3 );
				var old = oldMembers.Single( item => item.Id == member.Id );
				Assert.AreEqual( (old.CellX, old.CellY), (member.CellX, member.CellY) );
			}
			Assert.AreEqual( random, level.Park.Economy.World.State );
		}
		finally { File.Delete( path ); }
	}

	private (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) World() =>
		((Level, ParkEconomy, OriginalObject, OriginalObject, ParkObjectState))typeof( UpgradeCatalogueDefaultsTests )
			.GetMethod( "World", Private )!.Invoke( fixture, new object[] { "", 0, false } )!;

	private static Dictionary<string, string> Prepare( (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) world ) =>
		(Dictionary<string, string>)typeof( UpgradeCatalogueDefaultsTests ).GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!
			.Invoke( null, new object[] { world } )!;

	[TestMethod]
	public void ActualWholeWorldRestoreFinishesClockAndStaffPositionsBeforeFreshReconstruction()
	{
		var source = World();
		var members = AddRoles( source.Economy );
		var assets = Prepare( source );
		Tick( source.Economy, 543 );
		Clock( source.Level ).RestoreSnapshot( new FixedStepClockSnapshot( 543, 0.005, 1.25 ) );
		var oldPresentation = SetupStaff( source.Level, syntheticMetadata: true );
		Call( source.Level, "InitializeStaffSprites" );
		using var oldWindow = new EmployeePortraitOwner( oldPresentation );
		oldWindow.FullRefresh( members[0], -0.5f, 0.4f, 0.975f );
		foreach ( var member in members )
		{
			Assert.IsTrue( member.BodySprite!.TryResolve( out var state ) );
			StaffOwnerFixture.Stamp( state, 0x8200 );
		}
		var saved = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( source.Level.CaptureResumeSnapshot( null, "staff-reconstruction", assets ) ) );
		Assert.IsNotNull( saved.Staff );
		Assert.AreEqual( Roles.Length, saved.Staff!.Actors.Count );
		var economy = ParkSaveFile.Restore( saved.Economy, source.Economy.Settings, source.Economy.Catalog );
		var candidate = Empty<Level>();
		var objects = new ParkObjects( null!, new SandboxParkGrid() );
		Set( candidate, "<Objects>k__BackingField", objects );
		Set( candidate, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, saved.StartKind ) );
		Set( candidate, "economyInstances", new Dictionary<OriginalObject, int>() );
		Set( candidate, "resumeState", saved );
		var one = new OriginalObject( source.One.Entry, source.One.Placement, objects.ScriptWorld, 1 ^ objects.ObjectScriptKey, true, source.One.Runtime.AttractionId );
		var two = new OriginalObject( source.Two.Entry, source.Two.Placement, objects.ScriptWorld, 2 ^ objects.ObjectScriptKey, true, source.Two.Runtime.AttractionId );
		((List<OriginalObject>)objects.Objects).AddRange( new[] { one, two } );
		Set( objects, "nextSeed", 3 );
		Prepare( (candidate, economy, one, two, economy.Objects.Single( item => item.Id == source.State.Id )) );
		Call( candidate, "ConnectObjectsToEconomy" );
		Call( candidate, "SetupParkGate" );
		foreach ( var member in economy.Staff.Members ) { member.CellX = 1.5f; member.CellY = 1.5f; }
		var presentation = SetupStaff( candidate, syntheticMetadata: true );
		AssertUnready( candidate );
		Assert.AreEqual( 0L, Clock( candidate ).TickCount );
		var random = economy.World.State;
		// These are the actual candidate stages, excluding the GPU/menu construction shell.
		Call( candidate, "RestoreResumeSimulation", saved );
		Assert.AreEqual( saved.Clock, Clock( candidate ).CaptureSnapshot() );
		AssertUnready( candidate );
		foreach ( var actor in saved.Staff.Actors )
		{
			var member = economy.Staff.Members.Single( item => item.Id == actor.Id );
			Assert.AreEqual( (actor.CellX, actor.CellY, actor.TravelSeconds), (member.CellX, member.CellY, member.TravelSeconds) );
		}
		Call( candidate, "InitializeStaffSprites" );
		Assert.AreEqual( 543L, presentation.EpochHostTick );
		Assert.AreEqual( Roles.Length, presentation.Registry.Count );
		foreach ( var member in economy.Staff.Members ) AssertFresh( member, presentation, 3 );
		using var newWindow = new EmployeePortraitOwner( presentation );
		Assert.AreEqual( EmployeePortraitState.Closed, newWindow.State );
		Assert.IsNull( newWindow.CloneTicket );
		Assert.AreNotEqual( oldPresentation.Session, presentation.Session );
		Assert.AreEqual( random, economy.World.State );
	}

	[TestMethod]
	public void ActualSyntheticOriginalImportReconstructsEveryRoleWithImportedIdentityAndNoElapsedHistory()
	{
		var imported = ((ParkEconomy Park, OriginalParkImport Save, OriginalEconomyImport Import))typeof( StaffRoleOrderTests )
			.GetMethod( "Original", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object?[] { null, null } )!;
		Assert.AreEqual( 15, imported.Import.ImportedStaff );
		var level = BareLevel( imported.Park );
		var random = imported.Park.World.State;
		var presentation = SetupStaff( level, syntheticMetadata: true );
		AssertUnready( level );
		Call( level, "InitializeStaffSprites" );
		Assert.AreEqual( imported.Park.Tick, presentation.EpochHostTick );
		Assert.AreEqual( 15, presentation.Registry.Count );
		foreach ( var role in Roles )
		{
			var members = imported.Park.Staff.Members.Where( member => member.Type == role ).ToArray();
			Assert.AreEqual( 3, members.Length );
			foreach ( var member in members )
			{
				var saved = imported.Save.Staff.Single( item => item.Id == member.ThingId );
				Assert.AreNotEqual( member.Id, member.ThingId );
				Assert.AreEqual( (saved.Base.X / 256f, saved.Base.Y / 256f), (member.CellX!.Value, member.CellY!.Value) );
				Assert.AreEqual( StaffJob.Patrol, member.Job, "the original fixture supplies a real imported patrol state" );
				AssertFresh( member, presentation, 9 );
			}
		}
		Assert.AreEqual( random, imported.Park.World.State );
	}

	[TestMethod]
	public void UnknownRestoredPerformanceQueuesEntryThirteenOnlyWithoutAnExtraRandomDraw()
	{
		var source = EconomyTestData.Park();
		var performer = AddRoles( source ).Single( item => item.Type == StaffType.Entertainer );
		performer.Job = StaffJob.Performing;
		performer.State = StaffState.Working;
		performer.JobStamp = 17;
		Tick( source, 1200 );
		var saved = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( source ) ) );
		var economy = ParkSaveFile.Restore( saved, source.Settings, source.Catalog );
		var member = economy.Staff.Members.Single( item => item.Id == performer.Id );
		Assert.AreEqual( StaffJob.Performing, member.Job );
		Assert.IsNull( member.BodySprite );
		var level = BareLevel( economy );
		var presentation = SetupStaff( level, syntheticMetadata: true );
		AssertUnready( level );
		var random = economy.World.State;
		Call( level, "InitializeStaffSprites" );
		AssertFresh( member, presentation, 13 );
		var ticket = member.BodySprite!.SourceTicket;
		var due = member.BodySprite.ReadSourceState()!.Due;
		Call( level, "InitializeStaffSprites" );
		Assert.AreEqual( 13, member.BodySprite.PendingTemplate );
		member.BodySprite.ApplyPending();
		var state = member.BodySprite.ReadSourceState()!;
		Assert.AreEqual( StaffSpriteProgram.Performance0, state.RestartPc.Program );
		Assert.AreEqual( 0, member.BodySprite.PendingTemplate );
		Assert.AreEqual( 0, member.BodySprite.PendingInterval );
		Assert.AreEqual( ticket, member.BodySprite.SourceTicket );
		Assert.AreEqual( due, state.Due );
		Assert.AreEqual( 0u, state.OutputValid );
		Assert.AreEqual( 0u, state[StaffSpriteRegister.Intro] );
		Assert.AreEqual( 0u, state[StaffSpriteRegister.Hold] );
		Assert.AreEqual( random, economy.World.State );
		Assert.AreEqual( source.World.State, economy.World.State );
	}
}

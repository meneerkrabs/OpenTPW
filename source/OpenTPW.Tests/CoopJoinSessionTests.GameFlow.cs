using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-snapshot-join] The game's side of a join (GameFlow.CoopJoin.cs) with an uninitialized <see cref="GameFlow"/>, as the ingress tests make one, whose build is the replay
/// twin staged in a scene set aside, as the game's build holds its candidate: the swap's order, a resync over the old park, the transitions that end a join, and a join that ends
/// without a park.
/// </summary>
public partial class CoopJoinSessionTests
{
	/// <summary>An entity of the live scene: the swap's teardown retires it.</summary>
	private sealed class SceneMarker : Entity
	{
		public int Deletes { get; private set; }
		protected override void OnDelete() => Deletes++;
	}

	/// <summary>
	/// A flow member's build, by replay: a scene set aside from its first step, as the game's build holds one between its slices; at its last step the twin staged in it, with a park
	/// camera of its own and a bare HUD.
	/// </summary>
	private sealed class FlowBuild( CoopJoinSessionTests tests, Peer member, CoopJoinPayload payload, int steps ) : IGameJoinBuild
	{
		private int done;
		private EntityConstructionScope? scene;
		private Level? candidate;
		private ParkHud? hud;
		private bool abandoned, installed;

		public Level Candidate => candidate ?? throw new InvalidOperationException( "The park is not built yet." );

		public bool Step()
		{
			if ( candidate != null )
				return true;
			if ( scene == null )
			{
				scene = new EntityConstructionScope();
				scene.Pause();
			}
			if ( ++done < steps )
				return false;
			scene.Resume();
			try
			{
				Camera.Mode = new ParkCameraMode();
				candidate = tests.Twin( payload, Entity.All ).Level;
				hud = BareHud( candidate );
			}
			finally { scene.Pause(); }
			return true;
		}

		public GameJoinEntry Stage()
		{
			if ( member.StageFails )
			{
				member.StageFails = false;
				throw new InvalidOperationException( "The park's renderer could not start." );
			}
			scene!.Resume();
			scene.Detach();
			return new GameJoinEntry( scene, candidate!, hud!, null, false, new AdvisorEntryPreparation.Store(), candidate!.LevelName, 7, ParkStartKind.FullSimulation );
		}

		public void MarkInstalled() => installed = true;

		public void Abandon()
		{
			if ( abandoned || installed )
				return;
			abandoned = true;
			member.FlowAbandoned++;
			hud?.Stack.Clear();
			scene?.Dispose();
		}
	}

	private static void SetField( object target, string field, object? value ) =>
		target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static object? GetField( object target, string field ) =>
		target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( target );

	private static ParkHud BareHud( Level level )
	{
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		SetField( hud, "level", level );
		SetField( hud, "strings", OriginalUiTests.FakeStrings() );
		SetField( hud, "host", new HudHost() );
		SetField( hud, "<Stack>k__BackingField", new UiScreenStack() );
		var screen = new UiScreen( "hud" ) { Modal = false };
		SetField( hud, "hud", screen );
		hud.Stack.Push( screen );
		return hud;
	}

	/// <summary>An uninitialized game, as the ingress tests make one: its sessions run on the tests' clock and its join builds are <see cref="FlowBuild"/>s; no sound starts.</summary>
	private GameFlow NewFlow( Peer member )
	{
		audioWas ??= GameAudio.Enabled;
		GameAudio.Enabled = false;
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		SetField( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() );
		flow.CoopSessionClock = () => clock;
		flow.CoopJoinBuildOverride = payload => new FlowBuild( this, member, payload, member.FlowSteps( ++member.FlowBuilds ) );
		return flow;
	}

	/// <summary>A member that joins the running session from the front end through the game's join; one live entity stands for the front end's scene.</summary>
	private (Peer Member, SceneMarker FrontEnd) FlowJoiner()
	{
		var link = network.Add();
		var peer = new Peer { Link = link };
		peers.Add( peer );
		built.Add( peer.Entities );
		peer.Enter();
		var frontEnd = new SceneMarker();
		peer.Flow = NewFlow( peer );
		link.Deliver();
		peer.Session = peer.Flow.JoinSharedPark( link, () => true );
		return (peer, frontEnd);
	}

	private static void TearDown( GameFlow flow, bool keepJoin )
	{
		try { typeof( GameFlow ).GetMethod( "TearDown", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( flow, new object[] { false, false, keepJoin } ); }
		catch ( TargetInvocationException error ) { ExceptionDispatchInfo.Capture( error.InnerException! ).Throw(); }
	}

	private static readonly Type ParkBuildType = typeof( GameFlow ).GetNestedType( "ParkBuild", BindingFlags.NonPublic )!;

	[TestMethod]
	public void TheGamesJoinBuildsBesideTheFrontEndAndItsSwapRetiresItWithoutEndingTheSession()
	{
		Hosted();
		Build( 120, 60 );
		var (joiner, frontEnd) = FlowJoiner();
		var flow = joiner.Flow!;
		Assert.AreSame( joiner.Session, flow.HeldJoin, "a join without a park is the game's to hold" );
		joiner.FlowSteps = _ => 30;
		RoundsUntil( () => joiner.Session!.IsBuilding, "the build began" );
		Rounds( 10 );
		// between its slices the build's scene waits set aside: the front end's is the live one, whole
		Assert.IsTrue( joiner.Session!.IsBuilding );
		CollectionAssert.AreEqual( new Entity[] { frontEnd }, joiner.Entities.ToArray() );
		Assert.IsNull( flow.Level );
		RoundsUntil( () => joiner.Level != null, "the join swapped the host's park in" );
		Assert.AreSame( flow.Level, joiner.Level );
		Assert.IsNull( flow.HeldJoin, "the park holds the session now" );
		Assert.AreSame( joiner.Session, joiner.Level!.CoopSession );
		Assert.IsTrue( joiner.Level.IsCoopGuestPark );
		Assert.IsNull( joiner.Session.EndedWith, "the swap's teardown did not end the session it swapped in for" );
		Assert.IsNull( joiner.Link.LeftWith );
		Assert.AreEqual( CoopGuestPhase.Following, joiner.Session.Phase );
		Assert.AreEqual( 1, frontEnd.Deletes, "the front end retired" );
		Assert.IsNotNull( flow.Hud );
		Assert.AreEqual( ParkStartKind.FullSimulation, flow.StartKind );
		Assert.AreEqual( (1, 0), (joiner.FlowBuilds, joiner.FlowAbandoned) );
		Build( 120, 61 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void AResyncThroughTheGameReplacesTheOldParkAndKeepsTheView()
	{
		var guest = Hosted( 1, flowGuests: true ).Single();
		var flow = guest.Flow!;
		Build( 120, 62 );
		var (oldPark, oldScene) = (guest.Level!, guest.Entities);
		var view = new ParkCameraMode();
		view.SetPose( new Vector3( 12, 9, 0 ), 95, 30 );
		Camera.Mode = view;
		// a fault only the session digest sees: the guest's park drifts from the host's
		guest.Enter();
		guest.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1 );
		RoundsUntil( () => guest.Session!.Phase == CoopGuestPhase.Resyncing, "the guest found the desync" );
		RoundsUntil( () => guest.Level != oldPark, "the host's park swapped in over the old one" );
		Assert.AreSame( flow.Level, guest.Level );
		Assert.AreSame( guest.Session, guest.Level!.CoopSession );
		Assert.IsNull( oldPark.CoopSession );
		Assert.IsNull( guest.Session!.EndedWith );
		Assert.AreEqual( CoopGuestPhase.Following, guest.Session.Phase );
		Assert.AreEqual( 0, oldScene.Count, "the old park's scene retired" );
		Assert.IsInstanceOfType( Camera.Mode, typeof( ParkCameraMode ) );
		var fresh = (ParkCameraMode)Camera.Mode;
		Assert.AreNotSame( view, fresh, "the new park's own camera" );
		Assert.AreEqual( (view.Target, view.Height, view.Yaw), (fresh.Target, fresh.Height, fresh.Yaw), "with the view the player had" );
		Build( 120, 63 );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void TheGameRefusesAJoinWhileAParkIsOpenAJoinIsHeldOrAParkIsStarted()
	{
		Hosted();
		var (joiner, _) = FlowJoiner();
		var other = network.Add();
		other.Deliver();
		Assert.ThrowsException<InvalidOperationException>( () => joiner.Flow!.JoinSharedPark( other, () => true ), "a join is held" );
		Assert.ThrowsException<InvalidOperationException>( () => joiner.Flow!.FollowSharedPark( other, () => true ), "a join is held, and no park is open to follow with" );
		var open = NewFlow( joiner );
		SetField( open, "<Level>k__BackingField", host.Level );
		Assert.ThrowsException<InvalidOperationException>( () => open.JoinSharedPark( other, () => true ), "a park is open" );
		var building = NewFlow( joiner );
		SetField( building, "parkBuild", RuntimeHelpers.GetUninitializedObject( ParkBuildType ) );
		Assert.ThrowsException<InvalidOperationException>( () => building.JoinSharedPark( other, () => true ), "a park is being built" );
		var queued = NewFlow( joiner );
		queued.Queue( () => { } );
		Assert.ThrowsException<InvalidOperationException>( () => queued.JoinSharedPark( other, () => true ), "a transition waits" );
		Assert.IsTrue( new[] { open, building, queued }.All( flow => flow.HeldJoin == null ), "a refused join holds nothing" );
		Assert.AreSame( joiner.Session, joiner.Flow!.HeldJoin );
	}

	[TestMethod]
	public void TheGamesSessionStepWaitsWhileATransitionWaitsOrAParkIsBuilt()
	{
		Hosted();
		Build( 60, 64 );
		var (joiner, _) = FlowJoiner();
		var flow = joiner.Flow!;
		flow.Queue( () => { } );
		Rounds( 600 );
		Assert.AreEqual( 0, joiner.FlowBuilds, "no build while a transition waits" );
		SetField( flow, "pending", null );
		SetField( flow, "parkBuild", RuntimeHelpers.GetUninitializedObject( ParkBuildType ) );
		Rounds( 600 );
		Assert.AreEqual( 0, joiner.FlowBuilds, "nor while a park is built over frames" );
		SetField( flow, "parkBuild", null );
		RoundsUntil( () => joiner.Level != null, "the join went on" );
		Assert.IsNull( joiner.Session!.EndedWith );
	}

	[TestMethod]
	public void AParkStartDuringTheJoinsBuildEndsTheSessionBeforeItBuildsItsOwnScene()
	{
		Hosted();
		Build( 60, 65 );
		var (joiner, frontEnd) = FlowJoiner();
		var flow = joiner.Flow!;
		joiner.FlowSteps = _ => 600;
		RoundsUntil( () => joiner.Session!.IsBuilding, "the build began" );
		Rounds( 5 );
		joiner.Enter();
		var startLevel = typeof( GameFlow ).GetMethod( "StartLevel", BindingFlags.Instance | BindingFlags.NonPublic,
			new[] { typeof( string ), typeof( bool ), typeof( bool ), typeof( bool ), typeof( ParkVisitInfo ), typeof( ParkStartKind ), typeof( Action<Level> ), typeof( Action<Level> ) } )!;
		// the asset-free game has no such level: the park's own build fails, after it began its scene
		var error = Assert.ThrowsException<TargetInvocationException>( () => startLevel.Invoke( flow, new object?[] { "nowhere", true, false, true, null, ParkStartKind.FullSimulation, null, null } ) )
			.InnerException!;
		Assert.IsFalse( error.Message.Contains( "already active", StringComparison.Ordinal ), $"the join's build still held the construction scene: {error}" );
		Assert.AreEqual( GameFlow.LeftSharedPark, joiner.Session!.EndedWith, "the join ended first" );
		Assert.AreEqual( GameFlow.LeftSharedPark, joiner.Link.LeftWith );
		Assert.IsNull( flow.HeldJoin );
		Assert.AreEqual( 1, joiner.FlowAbandoned );
		Assert.AreEqual( 0, frontEnd.Deletes, "the front end is still the live scene" );
		Assert.IsNull( GetField( flow, "pending" ), "the player left: no failure is shown" );
		using ( new EntityConstructionScope() ) { }
	}

	[TestMethod]
	public void AParkStartDuringAResyncsBuildEndsItsSessionFirst()
	{
		var guest = Hosted( 1, flowGuests: true ).Single();
		var flow = guest.Flow!;
		Build( 60, 70 );
		guest.FlowSteps = _ => 600;
		guest.Enter();
		guest.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1 );
		RoundsUntil( () => guest.Session!.IsBuilding, "the resync's build began" );
		guest.Enter();
		var startLevel = typeof( GameFlow ).GetMethod( "StartLevel", BindingFlags.Instance | BindingFlags.NonPublic,
			new[] { typeof( string ), typeof( bool ), typeof( bool ), typeof( bool ), typeof( ParkVisitInfo ), typeof( ParkStartKind ), typeof( Action<Level> ), typeof( Action<Level> ) } )!;
		var error = Assert.ThrowsException<TargetInvocationException>( () => startLevel.Invoke( flow, new object?[] { "nowhere", true, false, true, null, ParkStartKind.FullSimulation, null, null } ) )
			.InnerException!;
		Assert.IsFalse( error.Message.Contains( "already active", StringComparison.Ordinal ), $"the resync's build still held the construction scene: {error}" );
		Assert.AreEqual( GameFlow.LeftSharedPark, guest.Session!.EndedWith );
		Assert.AreEqual( 1, guest.FlowAbandoned );
		Assert.AreSame( guest.Level, flow.Level, "the old park is the live one until the new park replaces it" );
	}

	[TestMethod]
	public void TheFrontEndsOwnRebuildKeepsAHeldJoinAndItsBuild()
	{
		Hosted();
		Build( 60, 66 );
		var (joiner, frontEnd) = FlowJoiner();
		var flow = joiner.Flow!;
		joiner.FlowSteps = _ => 60;
		RoundsUntil( () => joiner.Session!.IsBuilding, "the build began" );
		// a language change rebuilds the front end: the join and its build go on
		joiner.Enter();
		TearDown( flow, keepJoin: true );
		Assert.AreEqual( 1, frontEnd.Deletes );
		Assert.AreSame( joiner.Session, flow.HeldJoin );
		Assert.IsNull( joiner.Session!.EndedWith );
		Assert.IsTrue( joiner.Session.IsBuilding );
		RoundsUntil( () => joiner.Level != null, "the join swapped in after the rebuild" );
		Assert.AreEqual( 0, joiner.FlowAbandoned );
		Build( 60, 67 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void AnyOtherTeardownEndsAHeldJoinWithoutAFailure()
	{
		Hosted();
		Build( 60, 71 );
		var (joiner, _) = FlowJoiner();
		var flow = joiner.Flow!;
		joiner.FlowSteps = _ => 60;
		RoundsUntil( () => joiner.Session!.IsBuilding, "the build began" );
		joiner.Enter();
		TearDown( flow, keepJoin: false );
		Assert.AreEqual( "The park was closed.", joiner.Session!.EndedWith );
		Assert.AreEqual( "The park was closed.", joiner.Link.LeftWith );
		Assert.IsNull( flow.HeldJoin );
		Assert.AreEqual( 1, joiner.FlowAbandoned, "its build released" );
		Assert.IsNull( GetField( flow, "pending" ), "the player left: no failure is shown" );
		using ( new EntityConstructionScope() ) { }
	}

	[TestMethod]
	public void AJoinThatEndsWithoutAParkShowsWhyInTheFrontEnd()
	{
		Hosted();
		Build( 60, 68 );
		var (joiner, _) = FlowJoiner();
		var flow = joiner.Flow!;
		var menu = (FrontEndMenu)RuntimeHelpers.GetUninitializedObject( typeof( FrontEndMenu ) );
		SetField( menu, "<Stack>k__BackingField", new UiScreenStack() );
		SetField( flow, "<Menu>k__BackingField", menu );
		Rounds( 30 );
		// the server tells the joiner the session ended (the host left)
		joiner.Link.End( "The host closed the park." );
		RoundsUntil( () => joiner.Session!.EndedWith != null, "the joiner heard the session end" );
		Assert.AreEqual( "The host closed the park.", joiner.Session!.EndedWith );
		Assert.IsNull( flow.HeldJoin );
		Assert.AreEqual( "coopJoinEnded", menu.Stack.Top?.Name, "the reason is shown in the front end" );
		Assert.IsNull( GetField( flow, "pending" ) );
	}

	[TestMethod]
	public void AFailedSwapReleasesTheCandidateEndsTheJoinAndQueuesTheFrontEndWithWhyWhenNoMenuIsLeft()
	{
		Hosted();
		Build( 60, 69 );
		var (joiner, frontEnd) = FlowJoiner();
		var flow = joiner.Flow!;
		joiner.StageFails = true;
		RoundsUntil( () => joiner.Session!.EndedWith != null, "the swap failed" );
		StringAssert.StartsWith( joiner.Session!.EndedWith, "The shared park could not be entered" );
		Assert.AreEqual( 1, joiner.FlowAbandoned, "the candidate released" );
		Assert.AreEqual( 0, frontEnd.Deletes, "nothing of the live scene was touched" );
		Assert.IsNull( flow.HeldJoin );
		Assert.IsNull( flow.Level );
		Assert.IsNotNull( GetField( flow, "pending" ), "the front end is queued with the reason" );
		using ( new EntityConstructionScope() ) { }
	}

	/// <summary>The words of the dialog on top of <paramref name="flow"/>'s park HUD (its body).</summary>
	private static string EndWords( GameFlow flow )
	{
		Assert.AreEqual( GameFlow.GuestEndedScreenName, flow.Hud!.Stack.Top?.Name, "the end dialog over the park" );
		return ((UiLabel)flow.Hud.Stack.Top!.Find( "message" )!).Text();
	}

	/// <summary>A flow guest following the session from its open park, with a HUD as the game's park has one.</summary>
	private Peer FlowGuestWithHud()
	{
		var guest = Hosted( 1, flowGuests: true ).Single();
		SetField( guest.Flow!, "<Hud>k__BackingField", BareHud( guest.Level! ) );
		return guest;
	}

	/// <summary>
	/// [EXT:online-coop-commands] The guest's end dialog adds that some of its last changes may not have been made (C4b-2) when the session ends with a management entry not resolved, a live
	/// waiter's failure not taken, or a note not said; with none of them it says only why the session ended.
	/// </summary>
	[DataTestMethod]
	[DataRow( "entry" )]
	[DataRow( "waiter" )]
	[DataRow( "note" )]
	[DataRow( "none" )]
	public void AGuestsEndDialogSaysWhenSomeOfItsLastChangesMayNotHaveBeenMade( string left )
	{
		var guest = FlowGuestWithHud();
		var flow = guest.Flow!;
		Build( 60, 72 );
		var queue = guest.Session!.Management!;
		var fee = new ManagementItem( ManagementTarget.Finance, ManagementKnob.Fee, new SetEntranceFeeCommand( 7 ) );
		switch ( left )
		{
			case "entry":
				// queued in the frame the end arrives: still unsent when the session ends
				Assert.IsTrue( queue.Enqueue( new[] { fee } ).Queued );
				break;
			case "waiter":
				// refused before its send (no finance right), its outcome in a window's waiter nobody took
				var waiter = queue.NewWaiter();
				Assert.IsTrue( queue.Enqueue( new[] { fee }, waiter ).Queued );
				RoundsUntil( () => !queue.Pending.Any(), "the rights refused it" );
				Assert.AreEqual( 0, queue.TakeNotes().Count, "the waiter holds its outcome" );
				break;
			case "note":
				// nothing left in the queue but a note of an outcome not known, not said yet
				var entry = new ManagementEntry( queue, 0, fee.Target, fee.Knob, fee.Command, null ) { State = ManagementEntryState.Resolved };
				queue.AddNote( new ManagementOutcome( entry, ManagementOutcomeKind.Unknown ) );
				break;
		}
		guest.Link.End( "The host closed the park." );
		RoundsUntil( () => guest.Session.EndedWith != null, "the guest heard the session end" );
		// the in-memory link's end is a lost link: its words, and the sentence after them in the dialog's body
		var strings = OriginalUiTests.FakeStrings();
		var why = strings.Extra( OpenTpwText.SharedParkLinkLost );
		Assert.AreEqual( left == "none" ? why : $"{why} {strings.Extra( OpenTpwText.CoopChangesNotMade )}", EndWords( flow ) );
	}

	[TestMethod]
	public void AResyncWhoseSwapFailsShowsTheEndOverTheOldParkWithTheLineForChangesQueued()
	{
		var guest = FlowGuestWithHud();
		var flow = guest.Flow!;
		Build( 60, 73 );
		var oldPark = guest.Level!;
		guest.StageFails = true;
		guest.Enter();
		guest.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1 );
		RoundsUntil( () => guest.Session!.Phase == CoopGuestPhase.Resyncing, "the guest found the desync" );
		// a change queued while the host's park is built: it is still unsent when the swap fails
		Assert.IsTrue( guest.Session!.Management!.Enqueue( new[] { new ManagementItem( ManagementTarget.Finance, ManagementKnob.Fee, new SetEntranceFeeCommand( 7 ) ) } ).Queued );
		RoundsUntil( () => guest.Session.EndedWith != null, "the swap failed" );
		Assert.AreSame( oldPark, flow.Level, "the old park is still the game's park" );
		Assert.IsNull( oldPark.CoopSession, "detached from the session" );
		var words = EndWords( flow );
		StringAssert.Contains( words, OriginalUiTests.FakeStrings().Extra( OpenTpwText.CoopChangesNotMade ), words );
	}
}

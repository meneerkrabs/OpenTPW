using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Phase 0b of the smarter-guests plan (docs/SMARTER-GUESTS.md): the two official extensions, the pure decision layer with its original extraction, the birth id and the extension's park state
/// with its own save section. The pins of the determinism tests prove the off path; these prove the on path.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SmarterGuestsTests
{
	/// <summary>The default mod set with <c>opentpw.smarter-guests</c> switched on.</summary>
	internal static ModSet On() => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
		new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.SmarterGuests.Id] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	// ---- The extensions -----------------------------------------------------------------------

	[TestMethod]
	public void BothExtensionsAreRegisteredOffByDefaultAndTheSmarterOneIsAtVersion130()
	{
		var smarter = OfficialExtensions.SmarterGuests;
		var insights = OfficialExtensions.GuestInsights;
		Assert.AreEqual( "opentpw.smarter-guests", smarter.Id );
		Assert.AreEqual( "opentpw.guest-insights", insights.Id );
		Assert.IsTrue( smarter.AffectsSimulation );
		Assert.IsFalse( insights.AffectsSimulation, "the insights only read the park" );
		Assert.IsFalse( smarter.DefaultEnabled );
		Assert.IsFalse( insights.DefaultEnabled );
		Assert.AreEqual( new ModVersion( 1, 3, 0 ), smarter.Version, "the version is the algorithm identity" );
		Assert.AreEqual( new ModVersion( 1, 0, 0 ), insights.Version );
		CollectionAssert.IsSubsetOf( new[] { smarter, insights }, OfficialExtensions.All.ToArray() );
		Assert.AreSame( smarter, OfficialExtensions.Find( smarter.Id ) );
	}

	[TestMethod]
	public void TheGatesFollowTheActiveSet()
	{
		Assert.IsFalse( FeatureGates.SmarterGuests );
		Assert.IsFalse( FeatureGates.GuestInsights );
		using ( OpenTpwExtensions.Use( On() ) )
		{
			Assert.IsTrue( FeatureGates.SmarterGuests );
			Assert.IsFalse( FeatureGates.GuestInsights );
		}
		var insightsOnly = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.GuestInsights.Id] = true } },
			null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		using ( OpenTpwExtensions.Use( insightsOnly ) )
		{
			Assert.IsFalse( FeatureGates.SmarterGuests );
			Assert.IsTrue( FeatureGates.GuestInsights );
		}
		Assert.IsFalse( FeatureGates.SmarterGuests );
	}

	[TestMethod]
	public void OnlyTheSmarterExtensionIsRecordedInParkSaves()
	{
		var everything = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings
		{
			Enabled = OfficialExtensions.All.ToDictionary( extension => extension.Id, _ => true )
		}, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		var records = ParkResumeMods.Capture( everything )!;
		Assert.IsTrue( records.Contains( new ParkModRecord( "opentpw.smarter-guests", "1.3.0", ModSet.NoFilesSha256 ) ) );
		Assert.IsFalse( records.Any( record => record.Id == "opentpw.guest-insights" ), "a presentation-only extension is never recorded" );
		Assert.IsNull( ParkResumeMods.Capture( ModSet.Defaults( OfficialExtensions.All ) ), "off by default: no record, so the save keeps its bytes" );
	}

	[TestMethod]
	public void AParkRefusesAMismatchedSmarterGuestsSet()
	{
		var record = new ParkModRecord( "opentpw.smarter-guests", "1.3.0", ModSet.NoFilesSha256 );
		var off = ModSet.Defaults( OfficialExtensions.All );
		var on = On();
		ParkResumeMods.Require( [record], on );
		ParkResumeMods.Require( null, off );
		var missing = Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( [record], off ) );
		StringAssert.Contains( missing.Message, "needs opentpw.smarter-guests 1.3.0" );
		var extra = Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( null, on ) );
		StringAssert.Contains( extra.Message, "was saved without opentpw.smarter-guests 1.3.0" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( [record with { Version = "0.9.0" }], on ) );
		StringAssert.Contains( older.Message, "needs opentpw.smarter-guests 0.9.0, not 1.3.0" );
	}

	// ---- The seam -----------------------------------------------------------------------------

	[TestMethod]
	public void OffTheGateIsOffAndThePolicyIsTheOriginal()
	{
		Assert.IsFalse( FeatureGates.SmarterGuests );
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		Assert.IsInstanceOfType( simulation.Decisions, typeof( OriginalGuestDecisions ) );
		Assert.IsNull( simulation.SmarterGuests );
		Assert.AreSame( OriginalGuestDecisions.Instance, simulation.Decisions );
	}

	[TestMethod]
	public void OnTheGateIsOnAndThePolicyIsTheSmarterOne()
	{
		using var scope = OpenTpwExtensions.Use( On() );
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		Assert.IsTrue( FeatureGates.SmarterGuests );
		Assert.IsInstanceOfType( simulation.Decisions, typeof( SmarterGuestDecisions ) );
		Assert.IsNotNull( simulation.SmarterGuests );
		Assert.AreEqual( 5UL, simulation.SmarterGuests!.ParkSeed );
	}

	[TestMethod]
	public void TheGateIsReadOnceWhenTheSimulationIsBuilt()
	{
		GuestSimulation built;
		using ( OpenTpwExtensions.Use( On() ) )
			built = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		Assert.IsFalse( FeatureGates.SmarterGuests );
		for ( var tick = 0; tick < 400; tick++ )
			built.Tick( 1f / FixedStepClock.TicksPerSecond );
		Assert.IsInstanceOfType( built.Decisions, typeof( SmarterGuestDecisions ), "the gate going off later changes nothing" );
		GuestSimulation off = new( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		using ( OpenTpwExtensions.Use( On() ) )
			off.Tick( 1f / FixedStepClock.TicksPerSecond );
		Assert.IsInstanceOfType( off.Decisions, typeof( OriginalGuestDecisions ), "the gate coming on later changes nothing either" );
		Assert.IsNull( off.SmarterGuests );
	}

	[TestMethod]
	public void AnExplicitPolicyOverridesTheGate()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5, new SmarterGuestDecisions() );
		Assert.IsNotNull( simulation.SmarterGuests );
		using var scope = OpenTpwExtensions.Use( On() );
		var original = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5, OriginalGuestDecisions.Instance );
		Assert.IsNull( original.SmarterGuests );
	}

	/// <summary>A policy that delegates every call to another one; the test policies override what they watch or change.</summary>
	internal class DelegatingDecisions( IGuestDecisions inner ) : IGuestDecisions
	{
		public virtual bool UsesExtensionState => inner.UsesExtensionState;
		public virtual bool UsesValueForMoney => inner.UsesValueForMoney;
		public virtual bool UsesSpreading => inner.UsesSpreading;
		public virtual int AlternativeLookAfterTurns => inner.AlternativeLookAfterTurns;
		public virtual bool SwitchesTarget( in DecisionContext context, in GuestView guest, in AttractionView target, uint targetScore, in AttractionView candidate, uint candidateScore ) =>
			inner.SwitchesTarget( context, guest, target, targetScore, candidate, candidateScore );
		public virtual bool PriceTurnedAway( in DecisionContext context, in GuestView guest, in AttractionView attraction ) => inner.PriceTurnedAway( context, guest, attraction );
		public virtual PriceStallDecision DecidePriceStall( in DecisionContext context, in GuestView guest, int consecutive ) => inner.DecidePriceStall( context, guest, consecutive );
		public virtual BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth ) => inner.DecideBooth( context, guest, booth );
		public virtual uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction ) => inner.ScoreAttraction( context, guest, attraction );
		public virtual bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => inner.BeatsInMajorDecision( context, guest, score, best );
		public virtual bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => inner.BeatsInMinorDecision( context, guest, score, best );
		public virtual QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission ) => inner.DecideAdmission( context, guest, admission );
		public virtual QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue ) => inner.DecideQueuePatience( context, guest, queue );
		public virtual PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride ) => inner.RememberUse( context, guest, ride );
		public virtual GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal ) => inner.ChooseWanderGoal( context, guest, proposal );
	}

	/// <summary>Counts every call, to show the host takes each decision through the layer.</summary>
	private sealed class CountingDecisions( IGuestDecisions inner ) : DelegatingDecisions( inner )
	{
		public readonly Dictionary<string, int> Calls = new();
		private void Count( string name ) => Calls[name] = Calls.GetValueOrDefault( name ) + 1;
		public override BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth ) { Count( "booth" ); return base.DecideBooth( context, guest, booth ); }
		public override uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction ) { Count( "score" ); return base.ScoreAttraction( context, guest, attraction ); }
		public override bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) { Count( "major" ); return base.BeatsInMajorDecision( context, guest, score, best ); }
		public override bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) { Count( "minor" ); return base.BeatsInMinorDecision( context, guest, score, best ); }
		public override QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission ) { Count( "admission" ); return base.DecideAdmission( context, guest, admission ); }
		public override QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue ) { Count( "patience" ); return base.DecideQueuePatience( context, guest, queue ); }
		public override PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride ) { Count( "memory" ); return base.RememberUse( context, guest, ride ); }
		public override GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal ) { Count( "wander" ); return base.ChooseWanderGoal( context, guest, proposal ); }
	}

	[TestMethod]
	public void EveryDecisionPointGoesThroughTheLayerAndADelegatingPolicyChangesNothing()
	{
		var plain = new ParkScenario( new WorldSeed( 1 ) );
		plain.Run( 9000 );
		var counting = new CountingDecisions( OriginalGuestDecisions.Instance );
		var spied = new ParkScenario( new WorldSeed( 1 ), decisions: counting );
		spied.Run( 9000 );
		Assert.AreEqual( plain.Hash, spied.Hash, "the guests' state hash" );
		Assert.AreEqual( plain.Guests.RandomState, spied.Guests.RandomState, "the draws, in the same order" );
		foreach ( var point in new[] { "booth", "score", "major", "minor", "admission", "patience", "memory", "wander" } )
			Assert.IsTrue( counting.Calls.GetValueOrDefault( point ) > 0, $"{point} was decided through the layer ({string.Join( ", ", counting.Calls )})" );
	}

	/// <summary>Lets a queued guest leave once it has waited long enough: a stand-in for the later patience rule.</summary>
	private sealed class ImpatientDecisions( int turns ) : DelegatingDecisions( OriginalGuestDecisions.Instance )
	{
		public override QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue ) =>
			context.ParkTurn - queue.JoinTurn > turns ? new QueuePatienceDecision( true, GuestThought.QueueTooLong ) : QueuePatienceDecision.Stay;
	}

	private sealed class EndRecorder : IGuestObserver
	{
		public readonly List<(QueueEndReason Reason, long Waited)> Ends = new();
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) => Ends.Add( (reason, waitTurns) );
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}

	[TestMethod]
	public void ThePatienceHookCanSendAWaitingGuestAwayAndTheOriginalNeverDoes()
	{
		var recorder = new EndRecorder();
		var impatient = new ParkScenario( new WorldSeed( 1 ), decisions: new ImpatientDecisions( 30 ) );
		impatient.Guests.Observer = recorder;
		impatient.Run( 12000 );
		var left = recorder.Ends.Where( end => end.Reason == QueueEndReason.Impatient ).ToList();
		Assert.IsTrue( left.Count > 0, "a waiting guest left through the hook" );
		Assert.IsTrue( left.All( end => end.Waited > 30 ), "only after the patience ran out" );
		Assert.IsTrue( QueueEndReasons.IsAbandonment( QueueEndReason.Impatient ), "the guest left of its own accord" );
		var original = new EndRecorder();
		var plain = new ParkScenario( new WorldSeed( 1 ) );
		plain.Guests.Observer = original;
		plain.Run( 12000 );
		Assert.IsFalse( original.Ends.Any( end => end.Reason == QueueEndReason.Impatient ) );
		Assert.AreNotEqual( plain.Hash, impatient.Hash );
	}

	[TestMethod]
	public void TheOriginalPolicyIsTheOriginalLogic()
	{
		var settings = GuestTests.CreateSettings();
		var context = new DecisionContext( settings, 11, false );
		var view = new GuestView( 7, 0, 0, 60, 10, 10, 10, 0, 100, 3, 3, new int[4], new int[4] );
		foreach ( var (points, fee, draw) in new[] { (0u, 0u, 0u), (500u, 3u, 7u), (500u, 80u, 11u), (50u, 600u, 5u), (5000u, 100u, 2u) } )
			Assert.AreEqual( BoothVerdict.Decide( points, fee, draw, settings ), OriginalGuestDecisions.Instance.DecideBooth( context, view, new BoothView( points, fee, draw ) ).Verdict );
		// admission: the gate, then the limit
		var attraction = default( AttractionView );
		Assert.AreEqual( QueueJoinResult.Joined, OriginalGuestDecisions.Instance.DecideAdmission( context, view, new AdmissionView( attraction, 50, 0, 3, 100 ) ).Result );
		Assert.AreEqual( QueueJoinResult.TooExciting, OriginalGuestDecisions.Instance.DecideAdmission( context, view, new AdmissionView( attraction, 50, 45, 3, 100 ) ).Result );
		Assert.AreEqual( QueueJoinResult.NotExcitingEnough, OriginalGuestDecisions.Instance.DecideAdmission( context, view, new AdmissionView( attraction, 50, -45, 3, 100 ) ).Result );
		Assert.AreEqual( QueueJoinResult.QueueTooLong, OriginalGuestDecisions.Instance.DecideAdmission( context, view, new AdmissionView( attraction, 50, 0, 100, 100 ) ).Result );
		Assert.AreEqual( QueueJoinResult.Joined, OriginalGuestDecisions.Instance.DecideAdmission( context, view, new AdmissionView( attraction, 0, 99, 3, 100 ) ).Result, "no excitement word: no gate" );
		// patience stays, the use is remembered, the wander proposal stands
		Assert.IsFalse( OriginalGuestDecisions.Instance.DecideQueuePatience( context, view, new QueuePatienceView( attraction, 0, 5, 9 ) ).Leave );
		Assert.IsTrue( OriginalGuestDecisions.Instance.RememberUse( context, view, attraction ).PushUsedHistory );
		var proposal = new GuestWander.Destination( (4, 5), 17, 99 );
		Assert.AreEqual( proposal, OriginalGuestDecisions.Instance.ChooseWanderGoal( context, view, proposal ) );
	}

	[TestMethod]
	public void TheSmarterPolicyDelegatesEverythingButTheBoothAndThePricedScore()
	{
		var settings = GuestTests.CreateSettings();
		var context = new DecisionContext( settings, 12, true );
		var view = new GuestView( 7, 3, 1, 40, 80, 20, 10, 0, 100, 3, 3, new int[4], new int[4] );
		var smarter = new SmarterGuestDecisions();
		var original = OriginalGuestDecisions.Instance;
		// no open ride or sideshow is the booth's 100 percent, so the verdict is the original's; an unpriced attraction scores as the original scores it
		var booth = new BoothView( 500, 80, 11, 0 );
		Assert.AreEqual( original.DecideBooth( context, view, booth ), smarter.DecideBooth( context, view, booth ) );
		Assert.AreEqual( original.ScoreAttraction( context, view, default ), smarter.ScoreAttraction( context, view, default ) );
		var admission = new AdmissionView( default, 50, 46, 3, 100 );
		Assert.AreEqual( original.DecideAdmission( context, view, admission ), smarter.DecideAdmission( context, view, admission ) );
		var proposal = new GuestWander.Destination( (1, 2), 30, 40 );
		Assert.AreEqual( original.ChooseWanderGoal( context, view, proposal ), smarter.ChooseWanderGoal( context, view, proposal ) );
		Assert.AreEqual( original.RememberUse( context, view, default ), smarter.RememberUse( context, view, default ) );
		Assert.AreEqual( original.DecideQueuePatience( context, view, default ), smarter.DecideQueuePatience( context, view, default ) );
		foreach ( var (score, best) in new[] { (5u, 0u), (10u, 0u), (30u, 20u), (20u, 20u), (20u, 30u), (uint.MaxValue, 5u), (5u, uint.MaxValue) } )
		{
			Assert.AreEqual( original.BeatsInMajorDecision( context, view, score, best ), smarter.BeatsInMajorDecision( context, view, score, best ) );
			Assert.AreEqual( original.BeatsInMinorDecision( context, view, score, best ), smarter.BeatsInMinorDecision( context, view, score, best ) );
		}
		Assert.IsTrue( smarter.UsesValueForMoney );
		Assert.IsFalse( ((IGuestDecisions)original).UsesValueForMoney );
	}

	[TestMethod]
	public void TheOriginalComparisonRulesAreTheChoiceLoopsOwn()
	{
		var settings = GuestTests.CreateSettings();
		var view = new GuestView( 7, 0, 0, 60, 10, 10, 10, 0, 100, 3, 3, Array.AsReadOnly( new int[4] ), Array.AsReadOnly( new int[4] ) );
		var (odd, even) = (new DecisionContext( settings, 11, false ), new DecisionContext( settings, 10, false ));
		var rules = OriginalGuestDecisions.Instance;
		Assert.IsFalse( rules.BeatsInMajorDecision( odd, view, 9, 0 ), "below the floor of 10" );
		Assert.IsTrue( rules.BeatsInMajorDecision( odd, view, 10, 0 ) );
		Assert.IsTrue( rules.BeatsInMajorDecision( even, view, 11, 10 ) );
		Assert.IsFalse( rules.BeatsInMajorDecision( even, view, 10, 10 ), "a tie on an even turn keeps the first" );
		Assert.IsTrue( rules.BeatsInMajorDecision( odd, view, 10, 10 ), "a tie on an odd turn takes the later one" );
		Assert.IsFalse( rules.BeatsInMajorDecision( odd, view, 10, 11 ) );
		Assert.IsFalse( rules.BeatsInMajorDecision( odd, view, 0x8000_0000u, 5 ), "the major loop compares signed: a negative score does not beat 5" );
		Assert.IsTrue( rules.BeatsInMinorDecision( odd, view, 0x8000_0000u, 5 ), "the minor loop compares unsigned" );
		Assert.IsTrue( rules.BeatsInMinorDecision( odd, view, 0, 0 ), "a tie at 0 on an odd turn" );
		Assert.IsFalse( rules.BeatsInMinorDecision( even, view, 0, 0 ) );
		Assert.IsFalse( rules.BeatsInMinorDecision( even, view, 7, 7 ) );
	}

	[TestMethod]
	public void NoEffectfulCallbackReachesThePolicy()
	{
		// no method of the interface takes a delegate and no view holds one: the host alone routes, draws and edits
		bool IsCallback( Type type ) => typeof( Delegate ).IsAssignableFrom( type ) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof( IEnumerable<> );
		foreach ( var method in typeof( IGuestDecisions ).GetMethods() )
			foreach ( var parameter in method.GetParameters() )
				Assert.IsFalse( IsCallback( parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType ), $"{method.Name}({parameter.Name})" );
		foreach ( var type in new[] { typeof( DecisionContext ), typeof( GuestView ), typeof( AttractionView ), typeof( BoothView ), typeof( AdmissionView ), typeof( QueuePatienceView ) } )
			foreach ( var field in type.GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic ) )
				Assert.IsFalse( typeof( Delegate ).IsAssignableFrom( field.FieldType ), $"{type.Name}.{field.Name}" );
	}

	/// <summary>Records what a policy can see of the histories.</summary>
	private sealed class HistoryProbe : DelegatingDecisions
	{
		public bool CastableToArray;
		public int Seen;
		public HistoryProbe() : base( OriginalGuestDecisions.Instance ) { }
		public override uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction )
		{
			Seen++;
			CastableToArray |= guest.UsedHistory is int[] || guest.TemporaryHistory is int[];
			return base.ScoreAttraction( context, guest, attraction );
		}
	}

	[TestMethod]
	public void TheHistoriesCannotBeCastBackToTheGuestsArrays()
	{
		var probe = new HistoryProbe();
		new ParkScenario( new WorldSeed( 1 ), decisions: probe ).Run( 6000 );
		Assert.IsTrue( probe.Seen > 0 );
		Assert.IsFalse( probe.CastableToArray );
	}

	[TestMethod]
	public void TheContextGivesTheSeedAndTheTraitWithoutAGenerator()
	{
		var settings = GuestTests.CreateSettings();
		var context = new DecisionContext( settings, 3, false, 99 );
		var guest = new GuestView( 1, 12, 0, 50, 0, 0, 0, 0, 10, 0, 0, Array.AsReadOnly( new int[4] ), Array.AsReadOnly( new int[4] ) );
		Assert.AreEqual( SmarterGuestsState.Trait( 99, 12, 4 ), context.Trait( guest, 4 ) );
		using var scope = OpenTpwExtensions.Use( On() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		Assert.AreEqual( park.Guests.SmarterGuests!.ParkSeed, park.Seed.GuestStream );
	}

	/// <summary>Returns what it is told for the one decision under test; everything else is the original.</summary>
	private sealed class BadPolicy( Func<QueueAdmission>? admission = null, Func<BoothDecision>? booth = null, Func<GuestWander.Destination>? wander = null ) : DelegatingDecisions( OriginalGuestDecisions.Instance )
	{
		public override QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView view ) => admission?.Invoke() ?? base.DecideAdmission( context, guest, view );
		public override BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView view ) => booth?.Invoke() ?? base.DecideBooth( context, guest, view );
		public override GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal ) => wander?.Invoke() ?? base.ChooseWanderGoal( context, guest, proposal );
	}

	[TestMethod]
	public void TheHostRefusesAPolicyResultItCannotActOn()
	{
		void Refuses( BadPolicy policy )
		{
			var exception = Assert.ThrowsException<InvalidOperationException>( () => new ParkScenario( new WorldSeed( 1 ), decisions: policy ).Run( 9000 ) );
			StringAssert.Contains( exception.Message, nameof( BadPolicy ) );
		}
		Refuses( new BadPolicy( admission: () => new QueueAdmission( QueueJoinResult.Closed ) ) );
		Refuses( new BadPolicy( admission: () => new QueueAdmission( QueueJoinResult.NoRoom ) ) );
		Refuses( new BadPolicy( admission: () => new QueueAdmission( QueueJoinResult.AlreadyQueued ) ) );
		Refuses( new BadPolicy( booth: () => new BoothDecision( (BoothVerdictKind)42 ) ) );
		Refuses( new BadPolicy( wander: () => new GuestWander.Destination( (500, 500), 10, 10 ) ) );
		Refuses( new BadPolicy( wander: () => new GuestWander.Destination( (3, 5), 400, 10 ) ) );
		// the three refusals a join may end in are accepted
		foreach ( var result in new[] { QueueJoinResult.TooExciting, QueueJoinResult.NotExcitingEnough, QueueJoinResult.QueueTooLong } )
			new ParkScenario( new WorldSeed( 1 ), decisions: new BadPolicy( admission: () => new QueueAdmission( result ) ) ).Run( 6000 );
	}

	/// <summary>APIs a decision must not touch: they differ between runs, machines or processes.</summary>
	private static readonly string[] BannedApis =
	{
		"HashCode", "GetHashCode", "DateTime", "Stopwatch", "Environment.TickCount", "Guid", "Random", "WorldRng", "ScriptRandom", "HashSet", "Dictionary", "Task", "Thread", "Parallel", "Environment."
	};

	private static IEnumerable<string> CodeLines( string text ) =>
		text.Split( '\n' ).Select( line => line.TrimStart() ).Where( line => !line.StartsWith( "//" ) );

	[TestMethod]
	public void ThePolicySourcesUseNoNondeterministicApi()
	{
		var files = Directory.GetFiles( FindDirectory( "World/Guests/Decisions" ), "*.cs" ).Append( Path.Combine( FindDirectory( "World/Guests/SmarterGuests" ), "SmarterGuestsState.cs" ) ).ToArray();
		Assert.IsTrue( files.Length >= 5, "the policy sources were found" );
		foreach ( var file in files )
		{
			foreach ( var api in BannedApis )
				Assert.IsFalse( CodeLines( File.ReadAllText( file ) ).Any( line => line.Contains( api ) ), $"{Path.GetFileName( file )} must not use {api}" );
			Assert.IsFalse( CodeLines( File.ReadAllText( file ) ).Any( line => System.Text.RegularExpressions.Regex.IsMatch( line, @"\bstatic\b(?!.*(readonly|const|\())" ) && line.Contains( ';' ) && !line.Contains( "=>" ) && !line.Contains( "{ get; }" ) ),
				$"{Path.GetFileName( file )} must not hold a static mutable field" );
		}
	}

	[TestMethod]
	public void ThePolicyTypesHoldNoMutableState()
	{
		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
		var types = typeof( IGuestDecisions ).Assembly.GetTypes().Where( type => typeof( IGuestDecisions ).IsAssignableFrom( type ) && !type.IsInterface ).ToArray();
		CollectionAssert.IsSubsetOf( new[] { typeof( OriginalGuestDecisions ), typeof( SmarterGuestDecisions ) }, types );
		foreach ( var type in types )
		{
			Assert.IsTrue( type.IsSealed, $"{type.Name} is sealed" );
			foreach ( var field in type.GetFields( flags ) )
				Assert.IsTrue( field.IsInitOnly || field.IsLiteral, $"{type.Name}.{field.Name} is mutable" );
		}
	}

	private static string FindDirectory( string relative )
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = Path.Combine( directory.FullName, "source", "OpenTPW", relative );
			if ( Directory.Exists( candidate ) )
				return candidate;
			candidate = Path.Combine( directory.FullName, "OpenTPW", relative );
			if ( Directory.Exists( candidate ) )
				return candidate;
		}
		Assert.Fail( $"The source folder {relative} was not found above {AppContext.BaseDirectory}: the purity checks read the sources of a repository checkout." );
		return "";
	}

	// ---- Birth ids and traits -----------------------------------------------------------------

	/// <summary>A fresh scenario (under the active set) restored from <paramref name="from"/>'s thing pool and guest snapshot, as a resumed park is.</summary>
	internal static ParkScenario Fresh( ParkScenario from )
	{
		var fresh = new ParkScenario( from.Seed );
		fresh.Things.RestoreSnapshot( from.Things.CaptureSnapshot() );
		fresh.Guests.RestoreSnapshot( from.Guests.CaptureSnapshot() );
		return fresh;
	}

	/// <summary>The identity half of the extension and nothing else: the guest identity state, with the original's decisions. What version 1.0.0 of the extension was.</summary>
	internal sealed class IdentityOnlyDecisions : DelegatingDecisions
	{
		public IdentityOnlyDecisions() : base( OriginalGuestDecisions.Instance ) { }
		public override bool UsesExtensionState => true;
	}

	/// <summary>A scenario run with the guest identity state and the original decisions, so its guests are the off run's (the tests of the state, the section and the hash use it).</summary>
	private static ParkScenario OnScenario( ulong seed, long ticks )
	{
		var park = new ParkScenario( new WorldSeed( seed ), decisions: new IdentityOnlyDecisions() );
		park.Run( ticks );
		return park;
	}

	[TestMethod]
	public void EveryGuestGetsABirthIdAtCreationAndIdsNeverRepeat()
	{
		using var scope = OpenTpwExtensions.Use( On() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		var seen = new HashSet<ulong>();
		var perTick = 0;
		for ( var tick = 0; tick < 24000; tick++ )
		{
			park.Run( 1 );
			foreach ( var guest in park.Guests.Guests )
			{
				Assert.AreNotEqual( 0UL, guest.BirthId, $"guest {guest.Id} has an id from its creation" );
				seen.Add( guest.BirthId );
			}
			perTick = Math.Max( perTick, park.Guests.Guests.Count );
		}
		var state = park.Guests.SmarterGuests!;
		Assert.IsTrue( state.Bootstrapped );
		Assert.AreEqual( (ulong)seen.Count + 1, state.NextBirthId, "one id per guest created, in order, with no gaps" );
		Assert.IsTrue( seen.Count > 20 && seen.Count > perTick, $"{seen.Count} guests were born, more than ever stood in the park at once ({perTick})" );
		Assert.AreEqual( (ulong)seen.Count, seen.Max(), "ids run from 1 without a gap" );
	}

	[TestMethod]
	public void TheBirthIdIsGivenBeforeTheBoothDecision()
	{
		using var scope = OpenTpwExtensions.Use( On() );
		var seen = new List<(int Id, ulong BirthId)>();
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: new RecordingBoothDecisions( seen ) );
		park.Run( 6000 );
		Assert.IsTrue( seen.Count > 0, "guests reached the booth" );
		Assert.IsTrue( seen.All( item => item.BirthId != 0 ), "the booth sees every guest with its id" );
	}

	private sealed class RecordingBoothDecisions( List<(int Id, ulong BirthId)> seen ) : DelegatingDecisions( new SmarterGuestDecisions() )
	{
		public override BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth ) { seen.Add( (guest.Id, guest.BirthId) ); return base.DecideBooth( context, guest, booth ); }
	}

	[TestMethod]
	public void GuestsWithoutAnIdGetOneOnTheFirstTickInThingListOrder()
	{
		// a park that ran with the extension off, saved as a guest snapshot: an older save, or an original one
		var plain = new ParkScenario( new WorldSeed( 1 ) );
		plain.Run( 4000 );
		var snapshot = plain.Guests.CaptureSnapshot();
		Assert.IsTrue( snapshot.Guests.Length >= 3 );
		ulong[] Ids( out int[] order )
		{
			using var scope = OpenTpwExtensions.Use( On() );
			var resumed = Fresh( plain );
			Assert.IsTrue( resumed.Guests.Guests.All( guest => guest.BirthId == 0 ), "the guest snapshot carries no ids" );
			Assert.IsFalse( resumed.Guests.SmarterGuests!.Bootstrapped );
			resumed.Guests.Tick( 1f / FixedStepClock.TicksPerSecond );
			Assert.IsTrue( resumed.Guests.SmarterGuests.Bootstrapped );
			order = resumed.Things.UsedNewestFirst().Where( slot => slot.Model == SaveThingModel.Guest ).Select( slot => slot.Id ).Where( id => resumed.Guests.Guests.Any( guest => guest.Id == id ) ).ToArray();
			var byId = resumed.Guests.Guests.ToDictionary( guest => guest.Id );
			return order.Select( id => byId[id].BirthId ).ToArray();
		}
		var first = Ids( out var order );
		CollectionAssert.AreEqual( first, Ids( out _ ), "the same park gives the same ids" );
		CollectionAssert.AreEqual( Enumerable.Range( 1, order.Length ).Select( index => (ulong)index ).ToArray(), first, "ids follow the thing list, newest guest first" );
	}

	[TestMethod]
	public void TheCounterIsRefusedAtItsLimitAndNeverWraps()
	{
		var state = new SmarterGuestsState( 1 );
		state.Restore( 1, ulong.MaxValue - 1, true );
		Assert.AreEqual( ulong.MaxValue - 1, state.AllocateBirthId() );
		Assert.ThrowsException<OverflowException>( () => state.AllocateBirthId() );
		Assert.AreEqual( ulong.MaxValue, state.NextBirthId, "a refused allocation changes nothing" );
		Assert.ThrowsException<OverflowException>( () => state.AllocateBirthId() );
	}

	[TestMethod]
	public void ACreationAtTheCounterLimitFailsInsteadOfWrapping()
	{
		using var scope = OpenTpwExtensions.Use( On() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		park.Guests.SmarterGuests!.Restore( 1, ulong.MaxValue, true );
		Assert.ThrowsException<OverflowException>( () => park.Run( 20000 ) );
		Assert.AreEqual( park.Guests.Guests.Count, park.Things.UsedNewestFirst().Count( slot => slot.Model == SaveThingModel.Guest ), "the refused creation took no thing slot" );
		Assert.AreEqual( 0, park.Guests.Guests.Count, "and no guest" );
	}

	[TestMethod]
	public void TheTraitHashIsStatelessAndWellMixed()
	{
		Assert.AreEqual( SmarterGuestsState.Trait( 7, 12, 3 ), SmarterGuestsState.Trait( 7, 12, 3 ) );
		Assert.AreNotEqual( SmarterGuestsState.Trait( 7, 12, 3 ), SmarterGuestsState.Trait( 8, 12, 3 ), "the park seed" );
		Assert.AreNotEqual( SmarterGuestsState.Trait( 7, 12, 3 ), SmarterGuestsState.Trait( 7, 13, 3 ), "the birth id" );
		Assert.AreNotEqual( SmarterGuestsState.Trait( 7, 12, 3 ), SmarterGuestsState.Trait( 7, 12, 4 ), "the trait index" );
		Assert.AreNotEqual( SmarterGuestsState.Trait( 7, 12, 3 ), SmarterGuestsState.Trait( 12, 7, 3 ), "the seed and the id are not interchangeable" );
		Assert.AreEqual( new SmarterGuestsState( 7 ).Trait( 12, 3 ), SmarterGuestsState.Trait( 7, 12, 3 ) );
		var values = Enumerable.Range( 1, 20000 ).Select( id => SmarterGuestsState.Trait( 99, (ulong)id, 0 ) ).ToArray();
		Assert.AreEqual( values.Length, values.Distinct().Count(), "no collisions over consecutive ids" );
		for ( var bit = 0; bit < 64; bit++ )
		{
			var set = values.Count( value => (value >> bit & 1) != 0 );
			Assert.IsTrue( set > 9500 && set < 10500, $"bit {bit} is set in {set} of {values.Length} values" );
		}
		// each trait index is its own stream
		var next = Enumerable.Range( 1, 5000 ).Select( id => SmarterGuestsState.Trait( 99, (ulong)id, 1 ) ).ToArray();
		Assert.IsFalse( values.Take( 5000 ).Zip( next ).Any( pair => pair.First == pair.Second ) );
		// the mix of the lowest bits of the first trait does not correlate with the second
		var agree = values.Take( 5000 ).Zip( next ).Count( pair => (pair.First & 1) == (pair.Second & 1) );
		Assert.IsTrue( agree > 2300 && agree < 2700, $"{agree} of 5000 agree" );
	}

	// ---- Persistence --------------------------------------------------------------------------

	[TestMethod]
	public void TheSectionIsNullWhileTheExtensionIsOff()
	{
		var park = new ParkScenario( new WorldSeed( 1 ) );
		park.Run( 3000 );
		Assert.IsNull( SmarterGuestsSave.Capture( park.Guests ) );
		Assert.IsNull( park.Guests.SmarterGuests );
	}

	[TestMethod]
	public void ASaveRoundTripsAndRestoresIntoAFreshInstance()
	{
		var park = OnScenario( 1, 5000 );
		var section = SmarterGuestsSave.Capture( park.Guests )!;
		Assert.IsTrue( section.Births.Count > 0 && section.Bootstrapped && section.NextBirthId > 1 );
		CollectionAssert.AreEqual( section.Births.Select( birth => birth.GuestId ).Order().ToArray(), section.Births.Select( birth => birth.GuestId ).ToArray(), "in guest-id order" );
		var again = ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( section ) );
		Assert.AreEqual( section.NextBirthId, again.NextBirthId );
		CollectionAssert.AreEqual( section.Births.ToArray(), again.Births.ToArray() );
		Assert.IsNull( SmarterGuestsSave.SectionProblem( again, park.Guests.Guests.Select( guest => guest.Id ) ) );

		using var scope = OpenTpwExtensions.Use( On() );
		var fresh = Fresh( park );
		SmarterGuestsSave.Restore( fresh.Guests, again );
		CollectionAssert.AreEqual( park.Guests.Guests.Select( guest => (guest.Id, guest.BirthId) ).ToArray(), fresh.Guests.Guests.Select( guest => (guest.Id, guest.BirthId) ).ToArray() );
		Assert.AreEqual( park.Guests.SmarterGuests!.NextBirthId, fresh.Guests.SmarterGuests!.NextBirthId );
		Assert.IsTrue( fresh.Guests.SmarterGuests.Bootstrapped );
		Assert.AreEqual( JsonOf( section ), JsonOf( SmarterGuestsSave.Capture( fresh.Guests )! ), "capturing the restored park gives the same section" );
	}

	private static string JsonOf( SmarterGuestsSaveData section ) => Encoding.UTF8.GetString( ParkResumeJson.Serialize( section ) );

	[TestMethod]
	public void RestoringASectionNeedsTheExtension()
	{
		var park = OnScenario( 1, 3000 );
		var section = SmarterGuestsSave.Capture( park.Guests )!;
		var off = Fresh( park );
		var exception = Assert.ThrowsException<InvalidDataException>( () => SmarterGuestsSave.Restore( off.Guests, section ) );
		StringAssert.Contains( exception.Message, "opentpw.smarter-guests" );
		SmarterGuestsSave.Restore( off.Guests, null );
	}

	[TestMethod]
	public void TheSectionIsValidatedAgainstItsGuestsAndTheRecordedMods()
	{
		var record = new[] { new ParkModRecord( "opentpw.smarter-guests", "1.3.0", ModSet.NoFilesSha256 ) };
		SmarterGuestsSaveData Section( ulong next, bool bootstrapped, params (int, ulong)[] births ) =>
			new() { ParkSeed = 5, NextBirthId = next, Bootstrapped = bootstrapped, Births = births.Select( item => new SmarterGuestBirth( item.Item1, item.Item2 ) ).ToArray() };
		string? Problem( SmarterGuestsSaveData? section, params int[] guests ) => SmarterGuestsSave.ShapeProblem( section, guests, record );
		Assert.IsNull( Problem( Section( 3, true, (10, 1), (11, 2) ), 10, 11 ) );
		Assert.IsNull( Problem( Section( 1, false ), 10, 11 ), "before the first tick no guest need have an id" );
		Assert.IsNull( Problem( Section( 3, false, (10, 1) ), 10, 11 ), "a guest created before the first tick has its id already" );
		StringAssert.Contains( Problem( null, 10 )!, "no SmarterGuests section" );
		StringAssert.Contains( SmarterGuestsSave.ShapeProblem( Section( 2, true, (10, 1) ), new[] { 10 }, null )!, "does not record" );
		StringAssert.Contains( Problem( Section( 0, false ) )!, "counter" );
		StringAssert.Contains( Problem( Section( ulong.MaxValue, false ) )!, "counter", "a used-up counter is not a state a save can hold" );
		StringAssert.Contains( Problem( Section( 3, true, (10, 1), (12, 2) ), 10, 11 )!, "guest 12" );
		StringAssert.Contains( Problem( Section( 3, true, (10, 1), (10, 2) ), 10, 11 )!, "two birth ids" );
		StringAssert.Contains( Problem( Section( 3, true, (10, 0), (11, 2) ), 10, 11 )!, "birth id 0" );
		StringAssert.Contains( Problem( Section( 3, true, (10, 3), (11, 2) ), 10, 11 )!, "birth id 3" );
		StringAssert.Contains( Problem( Section( 3, true, (10, 2), (11, 2) ), 10, 11 )!, "two guests" );
		StringAssert.Contains( Problem( Section( 3, true, (10, 1) ), 10, 11 )!, "only 1 of 2" );
	}

	[TestMethod]
	public void TheEnvelopeCarriesTheSectionOnlyWithTheRecordedExtension()
	{
		var plain = ParkResumeSaveFileTests.Envelope();
		var json = JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( plain ) ) )!.AsObject();
		Assert.IsFalse( json.ContainsKey( "SmarterGuests" ), "an off save has no member" );
		Assert.IsFalse( json.ContainsKey( "Mods" ) );
		Assert.IsNull( ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( plain ) ).SmarterGuests );

		var ids = plain.Guests.Guests.Select( guest => guest.Id ).ToArray();
		var section = new SmarterGuestsSaveData
		{
			ParkSeed = 42, NextBirthId = (ulong)ids.Length + 1, Bootstrapped = true,
			Births = ids.OrderBy( id => id ).Select( ( id, index ) => new SmarterGuestBirth( id, (ulong)index + 1 ) ).ToArray()
		};
		var mods = new[] { new ParkModRecord( "opentpw.smarter-guests", "1.3.0", ModSet.NoFilesSha256 ) };
		var with = plain with { SmarterGuests = section, Mods = mods };
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( with ) );
		Assert.AreEqual( 42UL, restored.SmarterGuests!.ParkSeed );
		CollectionAssert.AreEqual( section.Births.ToArray(), restored.SmarterGuests.Births.ToArray() );
		Assert.AreEqual( JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( with ) ) )!["Guests"]!.ToJsonString(), JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( plain ) ) )!["Guests"]!.ToJsonString(),
			"the guest snapshot is untouched" );

		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( plain with { SmarterGuests = section } ), "a section without the recorded extension" );
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( plain with { Mods = mods } ), "the recorded extension without a section" );
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( with with { SmarterGuests = section with { Births = section.Births.Append( new SmarterGuestBirth( 99999, 99 ) ).ToArray() } } ) );
	}

	[TestMethod]
	public void AnOlderBuildRefusesTheSectionBecauseTheCodecRejectsUnknownMembers()
	{
		var plain = ParkResumeSaveFileTests.Envelope();
		var ids = plain.Guests.Guests.Select( guest => guest.Id ).ToArray();
		var section = new SmarterGuestsSaveData { ParkSeed = 1, NextBirthId = 1, Bootstrapped = false, Births = Array.Empty<SmarterGuestBirth>() };
		var bytes = ParkResumeSaveFile.Serialize( plain with { SmarterGuests = section, Mods = [new ParkModRecord( "opentpw.smarter-guests", "1.3.0", ModSet.NoFilesSha256 )] } );
		var json = JsonNode.Parse( Encoding.UTF8.GetString( bytes ) )!.AsObject();
		json.Remove( "SmarterGuests" );
		json["SomethingFromTheFuture"] = 1;
		// an unknown member is refused by the strict codec: that is what a build from before the section does with it
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
		Assert.IsTrue( ids.Length > 0 );
	}

	// ---- The world hash -----------------------------------------------------------------------

	[TestMethod]
	public void TheExtensionJoinsTheWorldHashOnlyWhileItIsOnSoTheSchemaVersionStays()
	{
		Assert.AreEqual( 21, WorldStateHash.SchemaVersion, "the off-path hash is unchanged, so no bump" );
		var seed = new WorldSeed( 1 );
		ulong Hash( GuestSimulation guests ) => WorldStateHash.Compute( new WorldStateSources { Seed = seed, Guests = guests } );
		var off = new ParkScenario( seed );
		off.Run( 4000 );
		var on = OnScenario( 1, 4000 );
		Assert.AreEqual( off.Hash, on.Hash, "the guests' own hash is the same" );
		Assert.AreNotEqual( Hash( off.Guests ), Hash( on.Guests ), "the extension's state joins the world hash" );
		var before = Hash( on.Guests );
		on.Guests.Guests[0].BirthId += 1000;
		Assert.AreNotEqual( before, Hash( on.Guests ), "a birth id is part of it" );
		on.Guests.Guests[0].BirthId -= 1000;
		Assert.AreEqual( before, Hash( on.Guests ) );
		on.Guests.SmarterGuests!.Restore( 1234, on.Guests.SmarterGuests.NextBirthId, true );
		Assert.AreNotEqual( before, Hash( on.Guests ), "so is the park seed" );
	}

	[TestMethod]
	public void TheGuestHashAndTheGuestSnapshotDoNotSeeTheExtension()
	{
		var off = new ParkScenario( new WorldSeed( 1 ) );
		off.Run( 5000 );
		var on = OnScenario( 1, 5000 );
		Assert.AreEqual( off.Hash, on.Hash );
		Assert.AreEqual( off.Guests.RandomState, on.Guests.RandomState );
		Assert.AreEqual( System.Text.Json.JsonSerializer.Serialize( off.Guests.CaptureSnapshot() ), System.Text.Json.JsonSerializer.Serialize( on.Guests.CaptureSnapshot() ), "the guest snapshot DTO is byte-identical" );
	}

	// ---- The report ---------------------------------------------------------------------------

	/// <summary>
	/// The compare test of the value-for-money rule (docs/SMARTER-GUESTS.md, phase 2) at a small scale: the report with the extension on shows far fewer price rejections than the default, beyond 3 standard errors,
	/// without losing happiness, revenue per admitted guest or admissions beyond 3 standard errors, and says which extension set it ran. (The wait and stuck-guest metrics move too: see the page's results.)
	/// </summary>
	[TestMethod]
	public void TheReportWithTheExtensionOnShowsFewerPriceRejectionsAndNoLossOfHappinessRevenueOrAdmissions()
	{
		var options = new GuestReportOptions { Seeds = new ulong[] { 1, 2, 3, 4 }, WarmupDays = 10, MeasuredDays = 30, Performance = false };
		var off = GuestReport.ToJsonObject( GuestReport.Run( options ) );
		var on = GuestReport.ToJsonObject( GuestReport.Run( options with { Extensions = GuestReport.ResolveExtensions( new[] { "smarter-guests" } ) } ) );
		Assert.IsFalse( FeatureGates.SmarterGuests, "the extension is off again after the run" );
		Assert.AreEqual( 0, off["extensions"]!.AsArray().Count );
		Assert.AreEqual( "opentpw.smarter-guests", (string)on["extensions"]![0]! );
		var result = GuestReportCompare.Compare( off, on, ["queue.rejected.price.rate"] );
		StringAssert.Contains( result.Notes, "extensions: baseline none, candidate opentpw.smarter-guests" );
		var scenario = result.Rows.Where( row => row.Scenario == "park-scenario" ).ToDictionary( row => row.Metric );
		Assert.AreEqual( "improved", scenario["queue.rejected.price.rate"].Verdict, $"{scenario["queue.rejected.price.rate"]}" );
		Assert.IsTrue( scenario["queue.rejected.price.rate"].Candidate < scenario["queue.rejected.price.rate"].Baseline / 4, "far fewer price rejections" );
		foreach ( var metric in new[] { "happiness.guestTime", "revenue.perAdmittedGuest", "admissions" } )
			Assert.AreNotEqual( "regressed", scenario[metric].Verdict, $"{metric}: {scenario[metric]}" );
		Assert.IsTrue( scenario["happiness.guestTime"].Candidate >= scenario["happiness.guestTime"].Baseline, "guests are not less happy" );
		Assert.AreEqual( 0, scenario["queue.superseded"].Candidate, "no attempt ends without a report" );
		StringAssert.Contains( GuestReport.ToTable( GuestReport.Run( options with { Seeds = new ulong[] { 1 }, Extensions = new[] { "opentpw.smarter-guests" } } ) ), "Extensions on: opentpw.smarter-guests" );
	}

	[TestMethod]
	public void TheReportRefusesAnExtensionItCannotSwitchOn()
	{
		Assert.ThrowsException<ArgumentException>( () => GuestReport.ResolveExtensions( new[] { "opentpw.online" } ) );
		Assert.ThrowsException<ArgumentException>( () => GuestReport.ResolveExtensions( new[] { "nonsense" } ) );
		CollectionAssert.AreEqual( new[] { "opentpw.smarter-guests" }, GuestReport.ResolveExtensions( new[] { "smarter-guests", "opentpw.smarter-guests" } ).ToArray() );
	}

	// ---- Resume equivalence -------------------------------------------------------------------

	[TestMethod]
	public void AResumedParkContinuesLikeTheUninterruptedOneWithTheExtensionOn()
	{
		using var scope = OpenTpwExtensions.Use( On() );
		var seed = new WorldSeed( 7 );
		ulong Hash( GuestSnapshotTests.Rig rig ) => WorldStateHash.Compute( new WorldStateSources { Seed = seed, Guests = rig.Simulation } );
		var park = new GuestSnapshotTests.Rig();
		Assert.IsInstanceOfType( park.Simulation.Decisions, typeof( SmarterGuestDecisions ) );
		var tick = GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.TickCount >= 4200, 0, 4201 );
		tick = GuestSnapshotTests.RunUntil( park, GuestSnapshotTests.RichMoment, tick, 9000 );
		Assert.IsTrue( GuestSnapshotTests.RichMoment( park ) );
		// the value-for-money runs travel with the section: two guests have a run of price-emptied decisions (it ends when a guest chooses something or leaves)
		var counting = park.Simulation.Guests.Where( guest => guest.BirthId != 0 ).OrderBy( guest => guest.BirthId ).Take( 2 ).ToArray();
		Assert.AreEqual( 2, counting.Length );
		park.Simulation.SmarterGuests!.Stalls.Increment( counting[0].BirthId );
		park.Simulation.SmarterGuests.Stalls.Increment( counting[0].BirthId );
		park.Simulation.SmarterGuests.Stalls.Increment( counting[1].BirthId );
		var snapshot = park.Simulation.CaptureSnapshot();
		var section = ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( SmarterGuestsSave.Capture( park.Simulation )! ) );
		Assert.IsTrue( section.Births.Count > 3 );
		Assert.AreEqual( 2, section.Stalls!.Count, "the runs are in the section" );
		// the spreading rules' throughput estimates travel with it (phase 3), and a resumed park keeps updating them exactly as the uninterrupted one does
		Assert.IsTrue( section.Throughputs is { Count: > 0 } && section.Throughputs.Any( entry => entry.Samples > 0 ), "the estimates are in the section" );

		var resumed = new GuestSnapshotTests.Rig();
		resumed.Simulation.RestoreSnapshot( snapshot );
		SmarterGuestsSave.Restore( resumed.Simulation, section );
		foreach ( var attraction in snapshot.Attractions )
		{
			var script = resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script;
			script[RideVariables.VAR_LETMEON] = attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
		}
		Assert.AreEqual( Hash( park ), Hash( resumed ), "the resumed park hashes like the saved one, extension state included" );
		var last = tick + 1500;
		for ( ; tick <= last; tick++ )
		{
			park.Step();
			GuestSnapshotTests.Drive( park, tick );
			resumed.Step();
			GuestSnapshotTests.Drive( resumed, tick );
			Assert.AreEqual( Hash( park ), Hash( resumed ), $"the parks diverged at tick {tick}" );
		}
		Assert.AreEqual( park.Simulation.SmarterGuests!.NextBirthId, resumed.Simulation.SmarterGuests!.NextBirthId );
		CollectionAssert.AreEqual( park.Simulation.SmarterGuests.Stalls.Entries.ToArray(), resumed.Simulation.SmarterGuests.Stalls.Entries.ToArray(), "the runs continued the same" );
		CollectionAssert.AreEqual( park.Simulation.SmarterGuests.Throughputs.Entries.ToArray(), resumed.Simulation.SmarterGuests.Throughputs.Entries.ToArray(), "the throughput estimates continued the same" );
		Assert.IsTrue( park.Simulation.SmarterGuests.Throughputs.Entries.Zip( section.Throughputs! ).Any( pair => pair.First != pair.Second ), "and moved after the save point" );
		Assert.IsTrue( park.Simulation.SmarterGuests.NextBirthId > section.NextBirthId, "guests were born after the save point" );
	}

	[TestMethod]
	public void ResumingWithoutTheSectionStartsTheExtensionFreshAndStillDeterministic()
	{
		using var scope = OpenTpwExtensions.Use( On() );
		var seed = new WorldSeed( 7 );
		var park = new GuestSnapshotTests.Rig();
		var tick = GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.TickCount >= 3000, 0, 3001 );
		var snapshot = park.Simulation.CaptureSnapshot();
		GuestSnapshotTests.Rig Resume()
		{
			var rig = new GuestSnapshotTests.Rig();
			rig.Simulation.RestoreSnapshot( snapshot );
			foreach ( var attraction in snapshot.Attractions )
				rig.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script[RideVariables.VAR_LETMEON] = attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
			return rig;
		}
		var (first, second) = (Resume(), Resume());
		for ( var step = tick; step < tick + 800; step++ )
		{
			first.Step();
			GuestSnapshotTests.Drive( first, step );
			second.Step();
			GuestSnapshotTests.Drive( second, step );
		}
		Assert.AreEqual( WorldStateHash.Compute( new WorldStateSources { Seed = seed, Guests = first.Simulation } ), WorldStateHash.Compute( new WorldStateSources { Seed = seed, Guests = second.Simulation } ) );
		Assert.IsTrue( first.Simulation.Guests.All( guest => guest.BirthId != 0 ) );
	}
}

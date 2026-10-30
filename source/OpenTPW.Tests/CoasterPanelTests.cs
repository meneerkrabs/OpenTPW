using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The coaster tool's panel (COAST-028): the nine buttons, their enable mask, the drags of the height, rotate and tilt buttons, the move button, the stack swaps and the demolish button's cut,
/// which reopens a finished circuit so it can be extended again, without game data (tp.exe <c>0x4982a0</c>, <c>0x446fa0</c>, <c>0x445910</c>, <c>0x446300</c>, <c>0x446740</c>).
/// </summary>
[TestClass]
public class CoasterPanelTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private const int First = CoasterTrack.StationNodeCount;

	[TestInitialize]
	public void Init() => Log = new();

	private static (CoasterBuilder Builder, CoasterRide Ride) NewCoaster( Dictionary<string, string>? settings = null )
	{
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ?? CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		return (builder, ride);
	}

	private static (CoasterBuilder Builder, CoasterRide Ride) Finished()
	{
		var (builder, ride) = NewCoaster();
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		return (builder, ride);
	}

	// ---- The buttons --------------------------------------------------------------------------------------

	[TestMethod]
	public void TheMaskEnablesBuildOnAnUnfinishedTrackAndDemolishOnAFinishedOne()
	{
		var (builder, ride) = NewCoaster();
		var panel = CoasterPanelRig.Create( builder );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected, "0x4455c0: an unfinished track opens on the build button" );
		const CoasterToolButton Edits = CoasterToolButton.Move | CoasterToolButton.Tilt | CoasterToolButton.Height | CoasterToolButton.Rotate | CoasterToolButton.StackUp | CoasterToolButton.StackDown;
		Assert.AreEqual( Edits | CoasterToolButton.Build | CoasterToolButton.Loop, panel.EnabledMask, "0x1fc plus build and loop; demolish is off while the circuit is not finished" );

		var (finished, _) = Finished();
		var done = CoasterPanelRig.Create( finished );
		Assert.AreEqual( CoasterToolButton.Height, done.Selected, "a finished circuit opens on the height button" );
		Assert.IsFalse( done.ShowsActionCost, "mode 8 is hidden before a height drag begins" );
		Assert.AreEqual( Edits | CoasterToolButton.Demolish, done.EnabledMask, "a complete track clears the two build buttons and keeps the demolish button" );
		Assert.IsFalse( done.Select( CoasterToolButton.Build ), "a disabled button does nothing" );
		Assert.AreEqual( CoasterToolButton.Height, done.Selected );
		Assert.IsTrue( done.Select( CoasterToolButton.Demolish ) );
		Assert.IsFalse( done.ShowsActionCost, "the demolition mode does not set the height-drag bit" );
	}

	[TestMethod]
	public void TheLoopButtonStartsAPairAndNeedsTheSecondPylonBeforeBuildComesBack()
	{
		var (builder, _) = NewCoaster();
		var panel = CoasterPanelRig.Create( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.IsTrue( builder.LoopMode, "0x442cd0: the pair counter is 2" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 26 ) );
		Assert.IsTrue( panel.PairPending );
		Assert.IsFalse( panel.IsEnabled( CoasterToolButton.Build ), "0x445910: the first pylon of a pair is placed, only the loop button builds" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20, 29 ) );
		Assert.IsFalse( builder.LoopMode, "the second pylon ends the pair" );
		Assert.IsTrue( panel.IsEnabled( CoasterToolButton.Build ) );
	}

	private static (CoasterBuilder Builder, CoasterToolPanel Panel) PendingLoopPair()
	{
		var (builder, _) = NewCoaster();
		var panel = CoasterPanelRig.Create( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 26 ) );
		return (builder, panel);
	}

	/// <summary>COAST-035: opening the panel resumes the chain endpoint's second pylon (0x4455c0), independently of the cleared tool mode.</summary>
	[TestMethod]
	public void ReopeningThePanelResumesTheSecondLoopPylonAndItsPreview()
	{
		var (builder, panel) = PendingLoopPair();
		panel.Close();
		Assert.IsFalse( builder.LoopMode );
		Assert.IsTrue( panel.PairPending, "the endpoint's 0x80 survives leaving the tool" );
		Assert.IsFalse( panel.IsEnabled( CoasterToolButton.Build ) );
		var selectionOnClose = panel.Selected;
		panel.Refresh();
		Assert.AreEqual( selectionOnClose, panel.Selected, "refresh does not reopen a closed tool" );
		Assert.IsFalse( builder.LoopMode );
		Assert.IsTrue( panel.PairPending );
		panel.Open();
		Assert.AreEqual( CoasterToolButton.Loop, panel.Selected );
		Assert.IsTrue( builder.LoopMode );
		Assert.IsFalse( builder.FirstOfPair, "the resumed counter is 1" );
		Assert.IsFalse( panel.Select( CoasterToolButton.Build ) );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.IsFalse( builder.FirstOfPair, "repeated selection must not reset the counter to 2" );
		var preview = builder.Track.BuildPreviewChain( (20, 29), builder.Height, builder.LoopMode, builder.FirstOfPair );
		var ghost = preview!.Value.Chain.Single( node => (node.Flags & CoasterNodeFlags.Cursor) != 0 );
		Assert.AreEqual( CoasterNodeFlags.Loop, ghost.Flags & (CoasterNodeFlags.Loop | CoasterNodeFlags.LoopFirst) );
		Assert.AreEqual( 0.5f, ghost.LoopRotate );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20, 29 ) );
		var second = builder.Track.Nodes[builder.Track.LastIndex];
		Assert.AreEqual( CoasterNodeFlags.Loop, second.Flags & (CoasterNodeFlags.Loop | CoasterNodeFlags.LoopFirst) );
		Assert.AreEqual( 0.5f, second.LoopRotate );
		Assert.IsFalse( builder.LoopMode );
		Assert.IsFalse( panel.PairPending );
		Assert.IsTrue( panel.IsEnabled( CoasterToolButton.Build ) );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ), "the still-selected loop button starts another pair after completion" );
		Assert.IsTrue( builder.FirstOfPair );
	}

	[DataTestMethod]
	[DataRow( CoasterToolButton.Height )]
	[DataRow( CoasterToolButton.Move )]
	public void AnEditToolKeepsBuildDisabledUntilThePendingLoopPairResumes( CoasterToolButton edit )
	{
		var (builder, panel) = PendingLoopPair();
		Assert.IsTrue( panel.Select( edit ) );
		Assert.IsFalse( builder.LoopMode );
		Assert.IsTrue( panel.PairPending );
		Assert.IsFalse( panel.IsEnabled( CoasterToolButton.Build ) );
		Assert.IsFalse( panel.Select( CoasterToolButton.Build ) );
		Assert.IsTrue( panel.Press( builder.Track.LastIndex, NVector2.Zero, 24, 26 ) );
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( builder.Track.LastIndex, (24, 26) ) );
		Assert.IsTrue( panel.PairPending );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.IsTrue( builder.LoopMode );
		Assert.IsFalse( builder.FirstOfPair );
		Assert.IsFalse( panel.IsEnabled( CoasterToolButton.Build ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20, 29 ) );
		Assert.IsFalse( builder.LoopMode );
		Assert.IsFalse( panel.PairPending );
	}

	[TestMethod]
	public void CancellingAnUnplacedLoopPairLeavesAnOrdinaryEndpoint()
	{
		var (builder, _) = NewCoaster();
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		var panel = CoasterPanelRig.Create( builder );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.IsTrue( builder.FirstOfPair );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.IsTrue( builder.FirstOfPair, "an already active fresh pair stays at 2" );
		Assert.IsFalse( panel.PairPending );
		Assert.IsTrue( panel.Select( CoasterToolButton.Build ) );
		Assert.IsFalse( builder.LoopMode );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		panel.Close();
		panel.Open();
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected );
		Assert.IsFalse( builder.LoopMode );
		Assert.IsFalse( panel.PairPending );
	}

	[TestMethod]
	public void APendingCutPairUsesThePredecessorInsteadOfThePhysicalTail()
	{
		var (builder, _) = Finished();
		Assert.AreEqual( CoasterTrack.CutResult.Cut, builder.Cut( First, First + 3 ) );
		var track = builder.Track;
		Assert.IsTrue( track.IsComplete && track.IsCut );
		Assert.AreNotEqual( track.Nodes.Count - 1, track.LastIndex );
		// Synthetic flag-only endpoint: 0x444c00 tests 0x80, without a node 0x40 or active-mode requirement.
		track.Nodes[track.LastIndex].Flags |= CoasterNodeFlags.LoopFirst;
		Assert.AreEqual( CoasterNodeFlags.None, track.Nodes[track.LastIndex].Flags & CoasterNodeFlags.Loop );
		var panel = CoasterPanelRig.Create( builder );
		Assert.IsTrue( panel.PairPending );
		Assert.AreEqual( CoasterToolButton.Loop, panel.Selected );
		Assert.IsTrue( builder.LoopMode );
		Assert.IsFalse( builder.FirstOfPair );
		Assert.IsFalse( panel.IsEnabled( CoasterToolButton.Build ) );
		track.Nodes[track.LastIndex].Flags &= ~CoasterNodeFlags.LoopFirst;
		track.Nodes[^1].Flags |= CoasterNodeFlags.LoopFirst;
		panel.Open();
		Assert.IsFalse( panel.PairPending, "the physical tail is not the open chain's build endpoint" );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected );
		Assert.IsFalse( builder.LoopMode );
	}

	[TestMethod]
	public void AFinishedUncutTrackNeverResumesAnEndpointPair()
	{
		var (builder, _) = Finished();
		builder.Track.Nodes[^1].Flags |= CoasterNodeFlags.LoopFirst;
		var panel = CoasterPanelRig.Create( builder );
		Assert.IsFalse( panel.PairPending );
		Assert.IsFalse( builder.LoopMode );
		Assert.AreEqual( CoasterToolButton.Height, panel.Selected );
		Assert.IsFalse( panel.Select( CoasterToolButton.Build ) );
		Assert.IsFalse( panel.Select( CoasterToolButton.Loop ) );
	}

	[TestMethod]
	public void RefreshResumesAPendingPairWhenBuildBecomesDisabled()
	{
		var (builder, _) = NewCoaster();
		var panel = CoasterPanelRig.Create( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		builder.LoopMode = true;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 26 ) );
		builder.LoopMode = false;
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected );
		panel.Refresh();
		Assert.AreEqual( CoasterToolButton.Loop, panel.Selected );
		Assert.IsTrue( builder.LoopMode );
		Assert.IsFalse( builder.FirstOfPair );
		panel.Refresh();
		Assert.IsFalse( builder.FirstOfPair );
	}

	/// <summary>
	/// COAST-035: the loop mode needs the definition's flag 2 (a loop pylon control, tp.exe 0x42fd57; 0x442cd0, 0x442d10, 0x444c00 and 0x4445b0 test it), but the panel's loop button
	/// does not (0x4980D6, 0x4987f0 look at the ride's level only): on a coaster without a loop pylon the button is shown and enabled, its selection starts no loop mode, a stray node flag 0x80
	/// is no pending pair, and the build button stays on.
	/// </summary>
	[TestMethod]
	public void ACoasterWithoutALoopPylonKeepsTheLoopButtonButStartsNoLoopMode()
	{
		var settings = CoasterTests.PcSettings();
		foreach ( var key in settings.Keys.Where( key => key.StartsWith( "asPylonControls[1].", StringComparison.Ordinal ) ).ToArray() )
			settings.Remove( key );
		var (builder, _) = NewCoaster( settings );
		var panel = CoasterPanelRig.Create( builder );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected );
		Assert.IsTrue( panel.LoopButtonShown );
		Assert.IsFalse( panel.LoopUnlocked );
		Assert.AreEqual( CoasterToolButton.Loop, panel.EnabledMask & CoasterToolButton.Loop );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.AreEqual( CoasterToolButton.Loop, panel.Selected );
		Assert.IsFalse( builder.LoopMode, "0x442cd0 and 0x4445b0 do nothing without the definition's flag 2" );
		Assert.AreEqual( 0, builder.LoopPylonsLeft );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		Assert.IsFalse( builder.Track.Nodes[builder.Track.LastIndex].LoopPylon, "the pylon is a standard one" );
		builder.Track.Nodes[builder.Track.LastIndex].Flags |= CoasterNodeFlags.LoopFirst;
		panel.Refresh();
		Assert.IsFalse( panel.PairPending, "0x444c00 returns false without the flag" );
		Assert.AreEqual( CoasterToolButton.Build, panel.EnabledMask & CoasterToolButton.Build );
		Assert.AreEqual( CoasterToolButton.Loop, panel.Selected );
		panel.Open();
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected );
		Assert.IsFalse( builder.LoopMode );
	}

	[TestMethod]
	public void APendingEndpointDoesNotBypassTheUpgradeLevelGate()
	{
		var economy = EconomyTestData.Park( initialCash: 50000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1100, out var state ) );
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ), () => economy, () => state!.Id );
		CoasterTests.PlaceStation( builder, (20, 20) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		builder.LoopMode = true;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 26 ) );
		var panel = CoasterPanelRig.Create( builder );
		Assert.AreEqual( 0, builder.UpgradeLevel );
		Assert.IsTrue( panel.PairPending );
		Assert.AreEqual( CoasterToolButton.Height, panel.Selected );
		Assert.IsFalse( builder.LoopMode );
		Assert.IsFalse( panel.Select( CoasterToolButton.Build ) );
		Assert.IsFalse( panel.Select( CoasterToolButton.Loop ) );
		state!.Level = 1;
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.IsFalse( builder.FirstOfPair );
		state.Level = 0;
		panel.Refresh();
		Assert.AreEqual( CoasterToolButton.Height, panel.Selected );
		Assert.IsFalse( builder.LoopMode );
		Assert.IsTrue( panel.PairPending );
	}

	[TestMethod]
	public void ASavedPendingPairResumesFromItsExistingNodeFlags()
	{
		var (builder, panel) = PendingLoopPair();
		panel.Close();
		var captured = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( CoasterSave.CaptureRide( builder.Ride, attractionId: 7 ) ) );
		var (resumed, ride) = NewCoaster();
		ride.RestoreSnapshot( captured );
		Assert.IsFalse( resumed.LoopMode );
		var restoredPanel = CoasterPanelRig.Create( resumed );
		Assert.IsTrue( restoredPanel.PairPending );
		Assert.AreEqual( CoasterToolButton.Loop, restoredPanel.Selected );
		Assert.IsTrue( resumed.LoopMode );
		Assert.IsFalse( resumed.FirstOfPair );
		Assert.AreEqual( CoasterPlaceResult.Ok, resumed.Place( 20, 29 ) );
		Assert.AreEqual( 0.5f, resumed.Track.Nodes[resumed.Track.LastIndex].LoopRotate );
		Assert.IsFalse( restoredPanel.PairPending );
	}

	// ---- Drags --------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheHeightDragFollowsTheMouseAtHalfAPercentPerPixelWithinItsRangeAndSettlesTheMoneyOnRelease()
	{
		var (builder, ride) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Height );
		var node = First;
		var start = builder.Track.Nodes[node].T;
		var (low, high) = builder.Track.LoftRange( builder.Site, node );
		Assert.IsTrue( low <= start && start <= high );
		Assert.IsTrue( panel.Press( node, new NVector2( 500, 400 ), 24, 20 ) );
		Assert.IsTrue( panel.IsDragging );

		panel.Drag( new NVector2( 500, 380 ), 0, null );
		Assert.AreEqual( Math.Clamp( start + 20 * CoasterToolPanel.HeightRate, low, high ), builder.Track.Nodes[node].T, 1e-5f, "dragging up 20 px adds 20 × 0.005 to the loft parameter" );
		panel.Drag( new NVector2( 500, 400 ), 0, null );
		Assert.AreEqual( start, builder.Track.Nodes[node].T, 1e-5f, "back at the press position: the start value" );

		// A drag far beyond the range stops at its end, and the origin follows: coming back by a few pixels changes the value at once.
		panel.Drag( new NVector2( 500, 400 - 10000 ), 0, null );
		Assert.AreEqual( high, builder.Track.Nodes[node].T, 1e-5f );
		panel.Drag( new NVector2( 500, 400 - 10000 + 10 ), 0, null );
		Assert.AreEqual( high - 10 * CoasterToolPanel.HeightRate, builder.Track.Nodes[node].T, 1e-5f, "a clamped value moves the origin (0x4434d5)" );
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( node, (24, 20) ) );
		Assert.IsFalse( panel.IsDragging );
	}

	[TestMethod]
	public void TheHeightReleaseRefundsTheOldCostAndChargesTheNewOne()
	{
		var economy = EconomyTestData.Park( initialCash: 50000 );
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ), () => economy );
		CoasterTests.PlaceStation( builder, (20, 20) );
		builder.Height = 20;
		builder.Place( 24, 20 );
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Height );
		var balance = economy.Balance;
		var oldCost = CoasterTrack.Truncate( builder.Track.NodeCost( First, false ) );
		Assert.IsTrue( panel.Press( First, new NVector2( 100, 100 ), 24, 20 ) );
		panel.Drag( new NVector2( 100, 40 ), 0, null );
		Assert.IsTrue( panel.DragCost > 0, "a higher pylon costs more: the HUD shows the difference" );
		Assert.AreEqual( balance, economy.Balance, "nothing moves while the drag runs" );
		var newCost = CoasterTrack.RoundUp( builder.Track.NodeCost( First, false ) );
		panel.Release( First, (24, 20) );
		Assert.AreEqual( balance + oldCost - newCost, economy.Balance, "0x441a00: credit trunc(old cost), debit ceil(new cost)" );
	}

	[TestMethod]
	public void TheRotateDragTurnsThePylonWithinTheDeviationAndSetsFlag1()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		builder.Place( 24, 28 );
		var track = builder.Track;
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Rotate );
		var automatic = track.AutomaticYaw( builder.Site, First + 1 );
		var range = track.Definition.StandardPylon.MaxRotationDeviance;
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 300, 300 ), 28, 24 ) );
		panel.Drag( new NVector2( 290, 300 ), 0, null );
		var node = track.Nodes[First + 1];
		Assert.AreEqual( CoasterNodeFlags.ManualYaw, node.Flags & CoasterNodeFlags.ManualYaw, "0x441e80 sets node flag 1" );
		Assert.AreEqual( automatic + 10 * CoasterToolPanel.AngleRate, node.Yaw, 1e-4f, "10 px to the left add 10 × 0.02 radians (start − mouse)" );
		var knots = track.BuildKnots( builder.Site );
		Assert.AreEqual( node.Yaw, MathF.Atan2( knots[First + 1].Direction.X, knots[First + 1].Direction.Z ), 1e-4f, "the knot direction takes the hand-set yaw" );
		panel.Drag( new NVector2( 300 - 5000, 300 ), 0, null );
		Assert.AreEqual( automatic + range, track.Nodes[First + 1].Yaw, 1e-4f, "stops at the automatic yaw plus fMaxRotnAngleDeviance" );
		var cost = builder.BuildCost;
		panel.Release( First + 1, (28, 24) );
		Assert.AreEqual( cost, builder.BuildCost, "rotating moves no money" );
	}

	[TestMethod]
	public void TheTiltDragSetsTheBankAndTheInclineAndStopsTheAutoBank()
	{
		var (builder, _) = Finished();
		var track = builder.Track;
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Tilt );
		var index = First + 2;
		var auto = track.Nodes[index].Bank;
		Assert.IsTrue( panel.Press( index, new NVector2( 400, 400 ), 0, 0 ) );
		// The camera looks along the pylon's yaw: the horizontal drag banks and the vertical one inclines.
		var yaw = track.AutomaticYaw( builder.Site, index );
		panel.Drag( new NVector2( 390, 390 ), yaw, null );
		var node = track.Nodes[index];
		Assert.AreEqual( CoasterNodeFlags.NoAutoBank | CoasterNodeFlags.ManualIncline, node.Flags & (CoasterNodeFlags.NoAutoBank | CoasterNodeFlags.ManualIncline), "flags 4 and 2" );
		Assert.AreEqual( auto + 10 * CoasterToolPanel.AngleRate, node.Bank, 1e-4f, "the drag counts from the bank the node has" );
		Assert.AreEqual( 10 * CoasterToolPanel.AngleRate, node.Incline, 1e-4f );
		var knots = track.BuildKnots( builder.Site );
		Assert.AreEqual( node.Bank, knots[index].Roll, 1e-4f, "a node with flag 4 keeps its hand-set bank through the auto-bank pass" );
		panel.Drag( new NVector2( 400 - 10000, 400 - 10000 ), yaw, null );
		Assert.AreEqual( CoasterToolPanel.TiltLimit, track.Nodes[index].Bank, 1e-4f );
		Assert.AreEqual( CoasterToolPanel.TiltLimit, track.Nodes[index].Incline, 1e-4f, "±π" );
		panel.Release( index, null );
	}

	[TestMethod]
	public void TheLoopTiltDragMovesTheLoopRotationWithinItsRange()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.LoopMode = true;
		builder.Place( 24, 26 );
		builder.Place( 20, 29 );
		var track = builder.Track;
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Tilt );
		Assert.AreEqual( 0f, track.Nodes[First + 1].LoopRotate, "the first pylon of a pair starts at 0" );
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 100, 100 ), 24, 26 ) );
		panel.Drag( new NVector2( 100 - 10000, 100 ), 0, null );
		Assert.AreEqual( 0.35f, track.Nodes[First + 1].LoopRotate, 1e-5f, "the first of a pair stops at 0.35" );
		panel.Release( First + 1, null );
		Assert.IsTrue( panel.Press( First + 2, new NVector2( 100, 100 ), 20, 29 ) );
		panel.Drag( new NVector2( 100 + 10000, 100 ), 0, null );
		Assert.AreEqual( 0.15f, track.Nodes[First + 2].LoopRotate, 1e-5f, "any other loop pylon stops at 0.15" );
		panel.Drag( new NVector2( 100 - 10000, 100 ), 0, null );
		Assert.AreEqual( 0.85f, track.Nodes[First + 2].LoopRotate, 1e-5f );
	}

	[TestMethod]
	public void ThePanelRefusesWhatThePylonModelCannotDo()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Track.Definition.StandardPylon.Capabilities = CoasterPylonCapabilities.Loft;
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Rotate );
		Assert.IsFalse( panel.Press( First, NVector2.Zero, 24, 20 ), "no rotate clip, no rotate drag" );
		panel.Select( CoasterToolButton.Tilt );
		Assert.IsFalse( panel.Press( First, NVector2.Zero, 24, 20 ), "neither incline nor bank clip" );
		panel.Select( CoasterToolButton.Height );
		Assert.IsTrue( panel.Press( First, NVector2.Zero, 24, 20 ) );
		panel.Release( First, null );
		for ( var station = 0; station < First; station++ )
			Assert.IsFalse( panel.Press( station, NVector2.Zero, 0, 0 ), "the station's nodes cannot be edited" );
	}

	[TestMethod]
	public void TheMoveDragFollowsTheCellAndDropsOrReturns()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		builder.Place( 24, 28 );
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Move );
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 50, 50 ), 28, 24 ) );
		panel.Drag( new NVector2( 60, 60 ), 0, (29, 25) );
		Assert.AreEqual( (29, 25), (builder.Track.Nodes[First + 1].X, builder.Track.Nodes[First + 1].Y), "the node follows the hovered cell" );
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( First + 1, (29, 25) ) );
		Assert.AreEqual( (29, 25), (builder.Track.Nodes[First + 1].X, builder.Track.Nodes[First + 1].Y) );

		Assert.IsTrue( panel.Press( First + 1, new NVector2( 50, 50 ), 29, 25 ) );
		panel.Drag( new NVector2( 70, 70 ), 0, (30, 26) );
		panel.Release( First + 1, null );
		Assert.AreEqual( (29, 25), (builder.Track.Nodes[First + 1].X, builder.Track.Nodes[First + 1].Y), "released over no cell: the node goes back" );
	}

	// ---- Stacks -------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheStackButtonsSwapAPieceWithItsNeighbourInTheCellStack()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 20;
		foreach ( var (x, y) in new[] { (24, 20), (28, 24), (24, 28), (20, 24) } )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( x, y ) );
		builder.Height = 60;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		var track = builder.Track;
		var below = First;
		var above = First + 4;
		Assert.AreEqual( (0, 1), (track.Nodes[below].StackIndex, track.Nodes[above].StackIndex) );
		var (belowT, aboveT) = (track.Nodes[below].T, track.Nodes[above].T);
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.StackDown );
		Assert.AreEqual( CoasterPanelResult.Nothing, panel.Release( below, null ), "the lowest piece cannot go down" );
		Assert.AreEqual( CoasterPanelResult.Swapped, panel.Release( above, null ) );
		Assert.AreEqual( (1, 0), (track.Nodes[below].StackIndex, track.Nodes[above].StackIndex), "the two exchange their stack index" );
		Assert.AreEqual( (aboveT, belowT), (track.Nodes[below].T, track.Nodes[above].T), "and their loft parameters" );
		Assert.AreEqual( (First, First + 4), (Array.FindIndex( track.Nodes.ToArray(), node => ReferenceEquals( node, track.Nodes[below] ) ), Array.FindIndex( track.Nodes.ToArray(), node => ReferenceEquals( node, track.Nodes[above] ) )), "the chain order does not change" );
		panel.Select( CoasterToolButton.StackUp );
		Assert.AreEqual( CoasterPanelResult.Nothing, panel.Release( below, null ), "the piece now on top cannot go up" );
		Assert.AreEqual( CoasterPanelResult.Swapped, panel.Release( above, null ) );
		Assert.AreEqual( (0, 1), (track.Nodes[below].StackIndex, track.Nodes[above].StackIndex) );
	}

	// ---- Demolish: the cut --------------------------------------------------------------------------------

	[TestMethod]
	public void ACutToTheEndOfAFinishedCircuitReopensItSoItCanBeExtendedAndCompletedAgain()
	{
		var (builder, ride) = Finished();
		// the ride runs with guests when the tool opens
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		ride.Command( 6, 6 );
		var boarded = new List<int>();
		for ( var guest = 101; guest < 107; guest++ )
		{
			ride.Board( guest );
			boarded.Add( guest );
		}
		for ( var tick = 0; tick < 240 * 60 && ride.RidersOnBoard == 0; tick++ )
			ride.Advance( Tick );
		Assert.IsTrue( ride.RidersOnBoard > 0, "a train seated guests" );

		ride.EnterTool();
		Assert.AreEqual( CoasterControllerState.Built, ride.State );
		var panel = CoasterPanelRig.Create( builder );
		Assert.AreEqual( CoasterToolButton.Height, panel.Selected );
		var track = builder.Track;
		var cut = First + 2;
		var removed = CoasterTests.Circuit.Skip( 3 ).ToArray();
		Assert.IsTrue( panel.Select( CoasterToolButton.Demolish ) );
		Assert.AreEqual( CoasterPanelResult.CutStarted, panel.Release( cut, null ), "the first release over a pylon starts the range" );
		Assert.AreEqual( First + 2, panel.CutStart );
		Assert.AreEqual( CoasterPanelResult.Reopened, panel.Release( 0, null ), "the second over S0 (ordinal 0 stands for the last node) cuts to the end" );

		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State, "0x446740 sets state 1: the trains are removed" );
		Assert.AreEqual( 0, ride.Trains.Count );
		Assert.IsFalse( track.IsComplete || track.IsClosed || track.IsCut );
		Assert.AreEqual( cut + 1, track.Nodes.Count, "the pylons after the cut and the station end node are gone" );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected, "the cut reselects the build button" );
		Assert.IsTrue( panel.IsEnabled( CoasterToolButton.Build ) && !panel.IsEnabled( CoasterToolButton.Demolish ) );

		// Extend again with the pylons the cut removed, close, complete.
		builder.Height = 40;
		foreach ( var (dx, dy) in removed )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		Assert.IsTrue( track.IsRunnable && ride.IsOperational );
		Assert.AreEqual( CoasterControllerState.Built, ride.State, "completion sets state 2" );
		ride.Command( 5, 0 );
		Assert.AreEqual( CoasterControllerState.Open, ride.State );

		// Nobody is stranded: every guest the ride took leaves through COAST 3 exactly once.
		var released = new List<int>();
		for ( var guest = ride.Command( 3, 0 ); guest != 0; guest = ride.Command( 3, 0 ) )
			released.Add( guest );
		CollectionAssert.AreEquivalent( boarded.ToArray(), released.ToArray(), "boarded == released" );
	}

	[TestMethod]
	public void ACutInTheMiddleOpensAGapThatNewPylonsFillAndTheCircuitClosesOnTheChainStartAgain()
	{
		var (builder, ride) = Finished();
		ride.EnterTool();
		var track = builder.Track;
		var before = track.Nodes.Select( node => (node.X, node.Y) ).ToArray();
		var panel = CoasterPanelRig.Create( builder );
		Assert.IsTrue( panel.Select( CoasterToolButton.Demolish ) );
		// Cut from the second pylon to the fifth: the third and fourth go.
		panel.Release( First + 1, null );
		Assert.AreEqual( CoasterPanelResult.Cut, panel.Release( First + 4, null ) );
		Assert.AreEqual( First + 1, track.CutAfter );
		Assert.AreEqual( before.Length - 2, track.Nodes.Count );
		Assert.IsFalse( track.IsRunnable );
		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State );
		Assert.IsTrue( (track.Nodes[First + 1].Flags & CoasterNodeFlags.CutEnd) != 0 && (track.Nodes[First + 2].Flags & CoasterNodeFlags.CutStart) != 0 );
		Assert.IsTrue( track.IsChainStart( before[First + 4].X, before[First + 4].Y ), "the node after the cut is the chain start now" );
		Assert.IsFalse( track.IsChainStart( before[0].X, before[0].Y ), "and S0 is not" );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected );

		// The knots do not run through the gap.
		var knots = track.BuildKnots( builder.Site );
		var gapDirectionBefore = knots[First + 1].Direction;
		Assert.AreEqual( SVector3.Normalize( knots[First + 1].Position - knots[First].Position ), gapDirectionBefore, "the node before the cut has no next neighbour: it leaves away from the previous one" );

		// Fill the gap with the cells the cut removed, then close on the chain start.
		builder.Height = 40;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( before[First + 2].X, before[First + 2].Y ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( before[First + 3].X, before[First + 3].Y ) );
		Assert.AreEqual( First + 3, track.CutAfter, "new pylons go into the gap" );
		Assert.IsNotNull( track.BuildPreviewChain( (30, 30), 40, false ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( before[First + 4].X, before[First + 4].Y ), "a click on the chain start closes the gap" );
		Assert.IsFalse( track.IsCut );
		Assert.IsTrue( track.IsRunnable && ride.IsOperational );
		Assert.AreEqual( CoasterControllerState.Built, ride.State, "0x442c70 sets state 2" );
		CollectionAssert.AreEqual( before, track.Nodes.Select( node => (node.X, node.Y) ).ToArray() );
	}

	[TestMethod]
	public void TheCutNeverSplitsALoopPairAndWidensASingleNodeToItsNeighbours()
	{
		var (builder, ride) = NewCoaster();
		builder.Height = 40;
		var circuit = CoasterTests.Circuit;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				builder.LoopMode = true;
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + circuit[index].X, 20 + circuit[index].Y ) );
		}
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		var track = builder.Track;
		var count = track.Nodes.Count;

		// the same node twice cuts that node out
		Assert.AreEqual( CoasterTrack.CutResult.Cut, builder.Cut( First + 4, First + 4 ) );
		Assert.AreEqual( count - 1, track.Nodes.Count );
		Assert.AreEqual( First + 3, track.CutAfter );
		var (again, _) = NewCoaster();
		again.Height = 40;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				again.LoopMode = true;
			again.Place( 20 + circuit[index].X, 20 + circuit[index].Y );
		}
		CoasterTests.CloseAndComplete( again );

		// the pair itself widens to the nodes around it
		Assert.AreEqual( CoasterTrack.CutResult.Cut, again.Cut( First + 1, First + 2 ) );
		Assert.AreEqual( count - 2, again.Track.Nodes.Count, "both pylons of the pair go, whichever of them was clicked" );
		Assert.AreEqual( First, again.Track.CutAfter );

		// an end that touches only the second pylon of the pair moves back over the first
		var (third, _) = NewCoaster();
		third.Height = 40;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				third.LoopMode = true;
			third.Place( 20 + circuit[index].X, 20 + circuit[index].Y );
		}
		CoasterTests.CloseAndComplete( third );
		Assert.AreEqual( CoasterTrack.CutResult.Cut, third.Cut( First + 1, First + 4 ) );
		Assert.AreEqual( First, third.Track.CutAfter, "a cut that would start between the pair's pylons moves back over the first" );
		var (fourth, _) = NewCoaster();
		fourth.Height = 40;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				fourth.LoopMode = true;
			fourth.Place( 20 + circuit[index].X, 20 + circuit[index].Y );
		}
		CoasterTests.CloseAndComplete( fourth );
		Assert.AreEqual( CoasterTrack.CutResult.Cut, fourth.Cut( First, First + 2 ) );
		Assert.AreEqual( (First, count - 2), (fourth.Track.CutAfter, fourth.Track.Nodes.Count), "an end on the second pylon of a pair moves on over it: the whole pair goes" );
	}

	[TestMethod]
	public void ASecondReleaseOnTheSameStationNodeIsNoCutAndNoCutStartsInsideTheStation()
	{
		var (builder, ride) = Finished();
		ride.EnterTool();
		var track = builder.Track;
		var count = track.Nodes.Count;
		// 0x44678c: the second release on the node the range started on, when that is a station node, cuts nothing
		Assert.AreEqual( CoasterTrack.CutResult.Refused, builder.Cut( 0, 0 ), "S0 twice would have removed S1 and S2" );
		Assert.AreEqual( CoasterTrack.CutResult.Refused, builder.Cut( 2, 2 ), "S2 twice" );
		Assert.AreEqual( count, track.Nodes.Count );
		Assert.IsTrue( track.IsRunnable, "the circuit is as it was" );
		Assert.AreEqual( CoasterControllerState.Built, ride.State );
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Demolish );
		Assert.AreEqual( CoasterPanelResult.CutStarted, panel.Release( 0, null ) );
		Assert.AreEqual( CoasterPanelResult.Nothing, panel.Release( 0, null ), "the panel path refuses it too" );
		Assert.AreEqual( count, track.Nodes.Count );
		// a range that would start before S2 is refused whatever the ends
		Assert.AreEqual( CoasterTrack.CutResult.Refused, builder.Cut( 1, First + 2 ) );
		Assert.AreEqual( CoasterTrack.CutResult.Cut, builder.Cut( 2, First + 2 ), "from S2 to the third pylon is fine: the pylons between go" );
		Assert.AreEqual( 2, track.CutAfter );
	}

	[TestMethod]
	public void DeletingThePylonADragHoldsEndsTheDragAndAPendingCutIsDropped()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Height );
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 100, 100 ), 28, 24 ) );
		Assert.IsTrue( builder.RemoveLast() );
		panel.Drag( new NVector2( 100, 60 ), 0, null );
		Assert.IsFalse( panel.IsDragging, "the node the drag held is gone: it ends where it stands" );
		Assert.AreEqual( CoasterPanelResult.Nothing, panel.Release( null, null ) );

		// a pending cut does not survive an edit between its two releases
		var (finished, ride) = Finished();
		ride.EnterTool();
		var cutting = CoasterPanelRig.Create( finished );
		cutting.Select( CoasterToolButton.Demolish );
		Assert.AreEqual( CoasterPanelResult.CutStarted, cutting.Release( First + 2, null ) );
		finished.Track.Revalidate( finished.Site, null, null );
		Assert.AreEqual( CoasterPanelResult.CutStarted, cutting.Release( First + 4, null ), "the second release starts a new range instead of cutting from a stale one" );
	}

	[TestMethod]
	public void AHeightDragMayPassTheModelsTopWhenTheMaximumHeightIsAboveItAndTheTrackStillSavesAndLoads()
	{
		var (builder, ride) = NewCoaster();
		var pylon = builder.Track.Definition.StandardPylon;
		pylon.MeshHeightLow = 10;
		pylon.MeshHeightHigh = 95.56f;
		pylon.MaxHeight = 100;
		builder.Height = 40;
		builder.Place( 24, 20 );
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Height );
		var (_, high) = builder.Track.LoftRange( builder.Site, First );
		Assert.IsTrue( high > 1f, "(fMaxHeight − top)/(h1 − h0) lets t pass 1: 0x434680 has no clamp" );
		Assert.IsTrue( panel.Press( First, new NVector2( 0, 5000 ), 24, 20 ) );
		panel.Drag( new NVector2( 0, 0 ), 0, null );
		Assert.AreEqual( high, builder.Track.Nodes[First].T, 1e-4f );
		Assert.AreEqual( 100f, CoasterTrack.PylonHeight( pylon, builder.Track.Nodes[First].T ), 0.05f, "the node stands at fMaxHeight" );
		panel.Release( First, null );
		var captured = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( CoasterSave.CaptureRide( ride, attractionId: 7 ) ) );
		Assert.AreEqual( builder.Track.Nodes[First].T, captured.Track.Nodes[First].T );
		Assert.IsTrue( captured.Track.Nodes[First].T <= CoasterSave.MaximumLoft );
	}

	[TestMethod]
	public void MovingAStackedPylonAwayAndBackKeepsTheStackWhole()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 20;
		foreach ( var (x, y) in new[] { (24, 20), (28, 24), (24, 28), (20, 24) } )
			builder.Place( x, y );
		builder.Height = 60;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		var track = builder.Track;
		Assert.AreEqual( (0, 1), (track.Nodes[First].StackIndex, track.Nodes[First + 4].StackIndex) );
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Move );
		// pick the lower piece up and carry it over another cell: the upper piece comes down
		Assert.IsTrue( panel.Press( First, new NVector2( 0, 0 ), 24, 20 ) );
		panel.Drag( new NVector2( 5, 5 ), 0, (25, 21) );
		Assert.AreEqual( 0, track.Nodes[First + 4].StackIndex, "the piece above takes the lower place" );
		panel.Drag( new NVector2( 6, 6 ), 0, (24, 20) );
		Assert.AreEqual( (0, 1), (track.Nodes[First].StackIndex, track.Nodes[First + 4].StackIndex), "back on its cell the piece takes its old place and the other goes up again" );
		panel.Release( First, (24, 20) );
		Assert.AreEqual( (0, 1), (track.Nodes[First].StackIndex, track.Nodes[First + 4].StackIndex) );
	}

	[TestMethod]
	public void ClosingTheCircuitMovesTheSelectionOffTheButtonThatWentGrey()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			builder.Place( 20 + dx, 20 + dy );
		var panel = CoasterPanelRig.Create( builder );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected );
		CoasterTests.CloseAndComplete( builder );
		panel.Refresh();
		Assert.AreEqual( CoasterToolButton.Height, panel.Selected, "the build button is off on a finished circuit" );
	}

	[TestMethod]
	public void AFlumesTroughPylonsAreNoLoopPairsForTheCut()
	{
		var settings = CoasterTests.PcSettings();
		settings["sCoasterType.bSpecialIsTrough"] = "1";
		var (builder, _) = NewCoaster( settings );
		builder.Height = 40;
		var circuit = CoasterTests.Circuit;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				builder.LoopMode = true;
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + circuit[index].X, 20 + circuit[index].Y ) );
		}
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		var count = builder.Track.Nodes.Count;
		Assert.AreEqual( CoasterTrack.CutResult.Cut, builder.Cut( First, First + 3 ) );
		Assert.AreEqual( count - 2, builder.Track.Nodes.Count, "the two trough pylons between go and nothing else widens the range" );
	}

	[TestMethod]
	public void ACutLeavesTheTrainsGoneAndEveryGuestLeavingWhateverStateTheRideWasIn()
	{
		var (builder, ride) = Finished();
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		ride.Command( 6, 6 );
		for ( var guest = 301; guest < 305; guest++ )
			ride.Board( guest );
		ride.EnterTool();
		Assert.AreEqual( CoasterControllerState.Built, ride.State );
		Assert.AreEqual( CoasterTrack.CutResult.Reopened, builder.Cut( First, 0 ) );
		var off = new List<int>();
		for ( var guest = ride.Command( 3, 0 ); guest != 0; guest = ride.Command( 3, 0 ) )
			off.Add( guest );
		CollectionAssert.AreEquivalent( new[] { 301, 302, 303, 304 }, off.ToArray() );
	}

	[TestMethod]
	public void ACutOrAnEditWhileTheGameIsPausedStillEndsAReleasedDrag()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Move );
		Assert.IsTrue( panel.Press( First + 1, NVector2.Zero, 28, 24 ) );
		panel.Drag( new NVector2( 4, 4 ), 0, (29, 25) );
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( null, null ), "a release over nothing (the pause screen) puts the node back" );
		Assert.AreEqual( (28, 24), (builder.Track.Nodes[First + 1].X, builder.Track.Nodes[First + 1].Y) );
		Assert.IsFalse( panel.IsDragging );
	}

	[TestMethod]
	public void ACutLeavesAFinishedCircuitsRidersAndTheDragDoesNotRefitUntilTheRelease()
	{
		var (builder, ride) = Finished();
		ride.EnterTool();
		var panel = CoasterPanelRig.Create( builder );
		var samples = ride.Samples.Count;
		var version = builder.Track.Nodes[First + 1].T;
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 0, 400 ), 0, 0 ) );
		var firstSample = ride.Samples.Count > 0 ? ride.Samples[0].Position : default;
		panel.Drag( new NVector2( 0, 300 ), 0, null );
		panel.Drag( new NVector2( 0, 250 ), 0, null );
		Assert.AreNotEqual( version, builder.Track.Nodes[First + 1].T, "the node follows the pointer" );
		Assert.AreEqual( samples, ride.Samples.Count, "the controller is fitted once, on the release" );
		panel.Release( First + 1, null );
		Assert.IsTrue( ride.IsOperational );
	}

	[TestMethod]
	public void ACutOfAFinishedCircuitEndedByTheStationBeingRemovedLeavesNoStalePanel()
	{
		var (builder, ride) = Finished();
		ride.EnterTool();
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Demolish );
		panel.Release( First + 2, null );
		panel.Close();
		Assert.IsNull( panel.CutStart );
		Assert.IsFalse( panel.IsDragging );
	}

	[TestMethod]
	public void ACutOfAFinishedCircuitIsNotRefusedForRidersAndTheyLeaveWithTheTrains()
	{
		var (builder, ride) = Finished();
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		ride.Command( 6, 6 );
		ride.Board( 401 );
		for ( var tick = 0; tick < 240 * 60 && ride.RidersOnBoard == 0; tick++ )
			ride.Advance( Tick );
		Assert.IsTrue( ride.RidersOnBoard > 0 );
		// 0x446740 has no rider test (the tool's state 2 lets them off at its entry); the cut's own state 1 takes the trains away with whoever is still aboard
		Assert.AreEqual( CoasterTrack.CutResult.Reopened, builder.Cut( First, 0 ), "the cut goes ahead" );
		Assert.AreEqual( (CoasterControllerState.Incomplete, 0), (ride.State, ride.RidersOnBoard), "no train is left to carry them" );
	}

	/// <summary>The cells of the map (inside the placement radius of 12) where moving node <paramref name="index"/> gives each verdict.</summary>
	private static Dictionary<(int X, int Y), CoasterPlaceResult> ScanMove( CoasterBuilder builder, CoasterRide ride, int index )
	{
		var verdicts = new Dictionary<(int X, int Y), CoasterPlaceResult>();
		for ( var x = 8; x <= 40; x++ )
		{
			for ( var y = 8; y <= 40; y++ )
				verdicts[(x, y)] = builder.Track.CheckPlacement( builder.Site, ride.World, ride, x, y, false, moving: index );
		}
		return verdicts;
	}

	/// <summary>
	/// In move mode the validator skips the turn test of a node whose neighbours carry the cut ends 0x10000 and 0x20000 (<c>0x4348af..0x4348d8</c>): moving the first node after the
	/// gap of a cut circuit is never refused for a turn at the last node before it, where the same move on an uncut chain is.
	/// </summary>
	[TestMethod]
	public void MovingAcrossTheCutSkipsTheTurnTestAtTheCutEnd()
	{
		// the uncut chain: station, (24,20), (27,24): moving the second pylon is refused for its turn at the first for some cells
		var (plain, plainRide) = NewCoaster();
		plain.Height = 40;
		Assert.AreEqual( CoasterPlaceResult.Ok, plain.Place( 24, 20 ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, plain.Place( 27, 24 ) );
		var uncut = ScanMove( plain, plainRide, First + 1 );
		var turned = uncut.Where( pair => pair.Value is CoasterPlaceResult.TurnTooSharp or CoasterPlaceResult.TurnTooWide ).Select( pair => pair.Key ).ToList();
		Assert.IsTrue( turned.Count > 5, "an uncut chain refuses cells for the turn at its first pylon" );

		// the cut chain: the same first pylon is the cut end, the pylon after the gap the moved node
		var (builder, ride) = Finished();
		Assert.AreEqual( CoasterTrack.CutResult.Cut, builder.Cut( First, First + 3 ) );
		var track = builder.Track;
		Assert.AreEqual( (24, 20), (track.Nodes[First].X, track.Nodes[First].Y) );
		Assert.AreEqual( First, track.CutAfter );
		Assert.IsTrue( (track.Nodes[First].Flags & CoasterNodeFlags.CutEnd) != 0 && (track.Nodes[First + 1].Flags & CoasterNodeFlags.CutStart) != 0 );
		var cut = ScanMove( builder, ride, First + 1 );
		foreach ( var cell in turned )
			Assert.IsFalse( cut[cell] is CoasterPlaceResult.TurnTooSharp or CoasterPlaceResult.TurnTooWide, $"cell {cell}: no turn test across the cut, got {cut[cell]}" );
		Assert.IsTrue( turned.Any( cell => cut[cell] == CoasterPlaceResult.Ok ), "some cell the turn test refused is now fine" );
	}

	/// <summary>
	/// After a move the validator re-validates the next node unless the moved node carries the cut end (<c>0x434e06</c>): moving the last pylon before the gap never breaks a link across it.
	/// </summary>
	[TestMethod]
	public void MovingTheCutEndDoesNotRevalidateTheLinkAcrossTheCut()
	{
		var (plain, plainRide) = NewCoaster();
		plain.Height = 40;
		foreach ( var (x, y) in new[] { (24, 20), (27, 24), (16, 28) } )
			plain.Place( x, y );
		var broken = ScanMove( plain, plainRide, First + 1 ).Where( pair => pair.Value == CoasterPlaceResult.BreaksNextLink ).Select( pair => pair.Key ).ToList();
		Assert.IsTrue( broken.Count > 5, "on an uncut chain a move can break the link to the next pylon" );

		var (builder, ride) = Finished();
		// the cut leaves (24,20), (27,24) | (16,28): the second pylon is the cut end and the third the cut start
		Assert.AreEqual( CoasterTrack.CutResult.Cut, builder.Cut( First + 1, First + 3 ) );
		Assert.AreEqual( First + 1, builder.Track.CutAfter );
		var cut = ScanMove( builder, ride, First + 1 );
		foreach ( var cell in broken )
			Assert.AreNotEqual( CoasterPlaceResult.BreaksNextLink, cut[cell], $"cell {cell}: the node after the gap is not re-validated" );
	}

	[TestMethod]
	public void ACutNeedsAFinishedCircuitAndPylonEnds()
	{
		var (builder, ride) = NewCoaster();
		builder.Place( 24, 20 );
		Assert.AreEqual( CoasterTrack.CutResult.Refused, builder.Cut( First, 0 ), "an unfinished circuit has nothing to cut" );
		var (finished, _) = Finished();
		Assert.AreEqual( CoasterTrack.CutResult.Refused, finished.Cut( 1, First + 2 ), "S1 is pylon-less and cannot border a cut" );
		Assert.AreEqual( CoasterTrack.CutResult.Refused, finished.Cut( finished.Track.Nodes.Count - 1, First + 2 ), "the station end node cannot either" );
	}

	[TestMethod]
	public void HandEditsAndACutCircuitRoundTripThroughTheParkSave()
	{
		var (builder, ride) = Finished();
		ride.EnterTool();
		var panel = CoasterPanelRig.Create( builder );
		panel.Select( CoasterToolButton.Rotate );
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 100, 100 ), 0, 0 ) );
		panel.Drag( new NVector2( 80, 100 ), 0, null );
		panel.Release( First + 1, null );
		panel.Select( CoasterToolButton.Tilt );
		Assert.IsTrue( panel.Press( First + 2, new NVector2( 100, 100 ), 0, 0 ) );
		panel.Drag( new NVector2( 90, 95 ), 0, null );
		panel.Release( First + 2, null );
		panel.Select( CoasterToolButton.Demolish );
		panel.Release( First + 3, null );
		Assert.AreEqual( CoasterPanelResult.Cut, panel.Release( First + 5, null ) );

		var captured = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( CoasterSave.CaptureRide( ride, attractionId: 7 ) ) );
		Assert.AreEqual( builder.Track.CutAfter, captured.Track.CutAfter );
		var resumed = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Test Coaster" );
		var other = new CoasterBuilder( resumed, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( other, (20, 20) );
		resumed.RestoreSnapshot( captured );
		var (a, b) = (builder.Track, other.Track);
		Assert.AreEqual( a.CutAfter, b.CutAfter );
		Assert.AreEqual( a.Nodes.Count, b.Nodes.Count );
		for ( var index = 0; index < a.Nodes.Count; index++ )
		{
			Assert.AreEqual( a.Nodes[index].Flags, b.Nodes[index].Flags, $"node {index}" );
			Assert.AreEqual( (a.Nodes[index].Yaw, a.Nodes[index].Incline, a.Nodes[index].Bank), (b.Nodes[index].Yaw, b.Nodes[index].Incline, b.Nodes[index].Bank), $"node {index}" );
		}
		Assert.AreEqual( CoasterControllerState.Incomplete, resumed.State );
		var (x, y) = (a.Nodes[a.CutAfter!.Value + 1].X, a.Nodes[a.CutAfter!.Value + 1].Y);
		Assert.IsTrue( b.IsChainStart( x, y ) );
	}

	// ---- The cursor and the placement area ----------------------------------------------------------------

	[TestMethod]
	public void ThePlacementAreaIsTheBoxAroundTheLastNodeMinusWhatTheValidatorOrOccupancyRefuses()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		var occupied = new HashSet<(int, int)> { (26, 20) };
		var panel = CoasterPanelRig.Create( builder, ( x, y ) => !occupied.Contains( (x, y) ) );
		var pylon = builder.Track.Definition.StandardPylon;
		var half = pylon.MaxPlaceDistance + 1;
		var area = panel.PlacementArea;
		Assert.IsTrue( area.Count > 0 );
		Assert.IsTrue( area.All( cell => cell.X >= 24 - half && cell.X < 24 + half && cell.Y >= 20 - half && cell.Y < 20 + half ), "half size = max place distance + 1, upper edges exclusive" );
		Assert.IsTrue( area.All( cell => builder.Check( cell.X, cell.Y ) == CoasterPlaceResult.Ok ) );
		Assert.IsFalse( area.Contains( (26, 20) ), "an occupied cell is not marked" );
		Assert.IsFalse( area.Contains( (24, 20) ), "the last node's own cell is the same cell" );
		Assert.IsTrue( area.Contains( (28, 24) ) );
		panel.Select( CoasterToolButton.Height );
		Assert.AreEqual( 0, panel.PlacementArea.Count, "only the build buttons mark the area" );
	}

	[TestMethod]
	public void TheCursorShowsTheValidatorsVerdictTheCostAndTheClosingTarget()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		var panel = CoasterPanelRig.Create( builder );
		panel.Hover( 24, 20 );
		Assert.AreEqual( CoasterPlaceResult.Ok, panel.Cursor!.Value.Result );
		Assert.IsTrue( panel.Cursor.Value.Cost > 0 );
		panel.Hover( 40, 40 );
		Assert.AreEqual( CoasterPlaceResult.TooFar, panel.Cursor!.Value.Result );
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		panel.Hover( 17, 20 );
		Assert.IsTrue( panel.Cursor!.Value.Closing, "S0's cell is the closing target" );
		panel.Select( CoasterToolButton.Move );
		panel.Hover( 24, 20 );
		Assert.IsNull( panel.Cursor, "other buttons show no cursor" );
	}

	/// <summary>COAST-035: a hovered cell whose corner lies more than 300 units from the last node leaves the cursor where it was (tp.exe 0x435e2a).</summary>
	[TestMethod]
	public void TheCursorStaysWithinThreeHundredUnitsOfTheLastNode()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		var panel = CoasterPanelRig.Create( builder );
		panel.Hover( 24, 20 );
		var before = panel.Cursor;
		Assert.IsNotNull( before );
		var last = builder.Track.NodePosition( builder.Site, builder.Track.Nodes[builder.Track.LastIndex] );
		var far = ((int)(last.X / 10) + 31, (int)(last.Z / 10));
		Assert.IsFalse( CoasterToolPanel.WithinReach( last, far.Item1, far.Item2 ) );
		panel.Hover( far.Item1, far.Item2 );
		Assert.AreEqual( before, panel.Cursor, "an out-of-reach cell keeps the last cursor" );
		Assert.IsTrue( CoasterToolPanel.WithinReach( new System.Numerics.Vector3( 205, 0, 205 ), 50, 20 ), "(500 − 205)² + (200 − 205)² = 87050" );
		Assert.IsFalse( CoasterToolPanel.WithinReach( new System.Numerics.Vector3( 205, 0, 205 ), 51, 20 ), "(510 − 205)² = 93025" );
	}

	/// <summary>COAST-035: the build cursor of a loop pylon carries node flag 0x80 only while it stands for the first pylon of the pair (counter 0x79c664 = 2, tp.exe 0x4362f6).</summary>
	[TestMethod]
	public void TheLoopCursorIsMarkedAsFirstOfPairOnlyWhileTheCounterIsTwo()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		Assert.IsFalse( builder.FirstOfPair, "no loop mode, no pair" );
		builder.LoopMode = true;
		Assert.IsTrue( builder.FirstOfPair );
		var first = builder.Track.BuildPreviewChain( (24, 20), 40, true, builder.FirstOfPair );
		var ghost = first!.Value.Chain.Single( node => (node.Flags & CoasterNodeFlags.Cursor) != 0 );
		Assert.AreEqual( CoasterNodeFlags.Loop | CoasterNodeFlags.LoopFirst, ghost.Flags & (CoasterNodeFlags.Loop | CoasterNodeFlags.LoopFirst) );
		builder.Place( 24, 20 );
		Assert.IsFalse( builder.FirstOfPair, "the counter is 1 for the second pylon of the pair" );
		var second = builder.Track.BuildPreviewChain( (28, 24), 40, true, builder.FirstOfPair );
		var next = second!.Value.Chain.Single( node => (node.Flags & CoasterNodeFlags.Cursor) != 0 );
		Assert.AreEqual( CoasterNodeFlags.Loop, next.Flags & (CoasterNodeFlags.Loop | CoasterNodeFlags.LoopFirst) );
		var plain = builder.Track.BuildPreviewChain( (28, 24), 40, false, false );
		Assert.AreEqual( CoasterNodeFlags.None, plain!.Value.Chain.Single( node => (node.Flags & CoasterNodeFlags.Cursor) != 0 ).Flags & (CoasterNodeFlags.Loop | CoasterNodeFlags.LoopFirst) );
	}

	/// <summary>COAST-028: the loop button needs the ride's level byte (thing +0x50) to be at least 1 (tp.exe 0x4980D6, hidden by 0x49880F); a station without an economy is unlocked.</summary>
	[TestMethod]
	public void TheLoopButtonNeedsTheRideToBeUpgraded()
	{
		var economy = EconomyTestData.Park( initialCash: 50000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1100, out var state ) );
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ), () => economy, () => state!.Id );
		CoasterTests.PlaceStation( builder, (20, 20) );
		var panel = CoasterPanelRig.Create( builder );
		Assert.IsTrue( builder.Ride.Definition.HasLoopPylon );
		Assert.AreEqual( 0, builder.UpgradeLevel );
		Assert.IsFalse( panel.LoopUnlocked );
		Assert.IsFalse( panel.LoopButtonShown );
		Assert.AreEqual( CoasterToolButton.None, panel.EnabledMask & CoasterToolButton.Loop, "level 0 hides the loop button" );
		state!.Level = 1;
		Assert.IsTrue( panel.LoopUnlocked );
		Assert.IsTrue( panel.LoopButtonShown );
		Assert.AreEqual( CoasterToolButton.Loop, panel.EnabledMask & CoasterToolButton.Loop );
		var (sandbox, _) = NewCoaster();
		Assert.IsNull( sandbox.UpgradeLevel );
		Assert.IsTrue( CoasterPanelRig.Create( sandbox ).LoopUnlocked, "no economy, no level gate" );
	}
}

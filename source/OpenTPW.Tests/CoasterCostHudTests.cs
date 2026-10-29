using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class CoasterCostHudTests
{
	private const int First = CoasterTrack.StationNodeCount;
	private static void Set( object target, string field, object? value ) =>
		target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static (ParkHud Hud, Level Level, CoasterToolPanel Panel) Fixture()
	{
		Log = new();
		var hud = StaffWindowTests.Hud( EconomyTestData.Park( initialCash: 50000 ) );
		var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Synthetic" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		builder.Height = 40;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		var panel = CoasterPanelRig.Create( builder );
		Set( level, "<CoasterToolStation>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( OriginalObject ) ) );
		Set( level, "<CoasterPanel>k__BackingField", panel );
		return (hud, level, panel);
	}

	[DataTestMethod]
	[DataRow( CoasterToolButton.Build, true )]
	[DataRow( CoasterToolButton.Loop, true )]
	[DataRow( CoasterToolButton.Move, true )]
	[DataRow( CoasterToolButton.Height, false )]
	[DataRow( CoasterToolButton.Rotate, false )]
	[DataRow( CoasterToolButton.Tilt, false )]
	[DataRow( CoasterToolButton.StackUp, false )]
	[DataRow( CoasterToolButton.StackDown, false )]
	public void IdlePanelCostFollowsTheOriginalModeClass( CoasterToolButton button, bool visible )
	{
		var (hud, _, panel) = Fixture();
		Assert.IsTrue( panel.Select( button ) );
		Assert.AreEqual( visible, panel.ShowsActionCost );
		Assert.AreEqual( visible, hud.BuildActionCost.HasValue );
		if ( visible ) Assert.AreEqual( 0L, hud.BuildActionCost!.Value, "an active zero-cost tool is still visible" );
	}

	[TestMethod]
	public void HeightCostAppearsOnSuccessfulPressAndDisappearsOnReleaseCancelAndModeChange()
	{
		var (hud, level, panel) = Fixture();
		panel.Select( CoasterToolButton.Height );
		Assert.IsNull( hud.BuildActionCost );
		Assert.IsFalse( panel.Press( -1, new NVector2( 100, 100 ), 24, 20 ) );
		Assert.IsNull( hud.BuildActionCost );
		Assert.IsTrue( panel.Press( First, new NVector2( 100, 100 ), 24, 20 ) );
		Assert.AreEqual( 0L, hud.BuildActionCost!.Value );
		panel.Drag( new NVector2( 100, 80 ), 0, null );
		Assert.AreEqual( panel.DragCost, hud.BuildActionCost );
		panel.Release( First, (24, 20) );
		Assert.IsNull( hud.BuildActionCost );
		Assert.IsTrue( panel.Press( First, new NVector2( 100, 100 ), 24, 20 ) );
		panel.Select( CoasterToolButton.Rotate );
		Assert.IsFalse( panel.IsDragging );
		Assert.IsNull( hud.BuildActionCost );
		panel.Select( CoasterToolButton.Height );
		Assert.IsTrue( panel.Press( First, new NVector2( 100, 100 ), 24, 20 ) );
		panel.Close();
		Assert.IsNull( hud.BuildActionCost );
		Set( level, "<CoasterToolStation>k__BackingField", null );
		Assert.IsNull( hud.BuildActionCost, "outside the tool keeps its existing hidden result" );
	}

	[TestMethod]
	public void MissingPanelAndNoEconomyKeepTheirExistingFallbacks()
	{
		var (hud, level, _) = Fixture();
		Set( level, "<CoasterPanel>k__BackingField", null );
		Assert.AreEqual( 0L, hud.BuildActionCost!.Value );
		Set( hud, "<Status>k__BackingField", new NoEconomyStatus() );
		Assert.IsNull( hud.BuildActionCost );
	}

	[DataTestMethod]
	[DataRow( CoasterToolButton.Rotate )]
	[DataRow( CoasterToolButton.Tilt )]
	public void OtherEditDragsDoNotBorrowTheHeightBit( CoasterToolButton button )
	{
		var (hud, _, panel) = Fixture();
		panel.Select( button );
		Assert.IsTrue( panel.Press( First, new NVector2( 100, 100 ), 24, 20 ) );
		Assert.IsTrue( panel.IsDragging );
		Assert.IsNull( hud.BuildActionCost );
		panel.Release( First, (24, 20) );
		Assert.IsNull( hud.BuildActionCost );
	}

	[TestMethod]
	public void TheLandAndClearAreaToolsShowTheirCostAsEveryTypeThreeModeDoes()
	{
		var (hud, level, _) = Fixture();
		Set( level, "<CoasterPanel>k__BackingField", null );
		Set( level, "<CoasterToolStation>k__BackingField", null );
		Assert.IsNull( hud.BuildActionCost );
		var clear = new AreaClearTool( ( _, _ ) => ParkCommandResult.Rejected( "synthetic" ) );
		Set( level, "<AreaClearTool>k__BackingField", clear );
		clear.Begin();
		Assert.AreEqual( 0L, hud.BuildActionCost, "the Clear Area tool writes no cost: the line shows 0 (0x4a0cdd shows every type-3 mode)" );
		clear.Cancel();
		Assert.IsNull( hud.BuildActionCost );
	}
}

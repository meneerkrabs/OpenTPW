using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>One park tool at a time: a staff tool ends the build tools and a build tool ends the staff tool (a carried member goes back).</summary>
[TestClass]
public class StaffToolExclusivityTests
{
	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly StaffPlacement Placement;
		public readonly Level Level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		public readonly StaffMember Member;

		private static void Set( object target, string field, object value ) =>
			target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

		public Fixture()
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			var world = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (0, 5) );
			Park.StaffWorld = world;
			Member = EconomyTestData.HireBest( Park, StaffType.Handyman );
			world.Synchronize();
			Placement = StaffPlacementRig.Create( () => Park, () => Grid.Cells, ( _, _ ) => false );
			Set( Level, "<StaffPlacement>k__BackingField", Placement );
			Set( Level, "<CellTool>k__BackingField", new CellBuildTool() );
			Set( Level, "<LastActionMessage>k__BackingField", "" );
			Set( Level, "<Paths>k__BackingField", new ParkPathBuilder( Grid.Cells, Grid, (ParkEconomy?)null, new SandboxParkGrid() ) );
		}
	}

	[TestMethod]
	public void AStaffToolEndsAnActivePathTool()
	{
		var f = new Fixture();
		Assert.IsTrue( f.Level.EnterPathTool( 3, 8 ) );
		Assert.IsTrue( f.Level.CellTool.IsActive );
		Assert.IsTrue( f.Placement.PickUp( f.Member ) );
		f.Level.EndBuildTools();
		Assert.IsFalse( f.Level.CellTool.IsActive, "the path tool ended, so a Backspace or the next click cannot resume it after the drop" );
		Assert.AreEqual( StaffToolKind.Carry, f.Placement.Kind, "ending the build tools leaves the staff tool alone" );
		Assert.AreEqual( StaffClickResult.Dropped, f.Placement.Click( 5, 8 ) );
		Assert.IsFalse( f.Level.CellTool.IsActive );
	}

	[TestMethod]
	public void ABuildToolEndsTheStaffToolAndACarriedMemberGoesBack()
	{
		var f = new Fixture();
		Assert.IsTrue( f.Placement.PickUp( f.Member ) );
		Assert.IsTrue( f.Level.EnterPathTool( 3, 8 ) );
		Assert.IsFalse( f.Placement.IsActive, "starting the path tool ends the Carry tool" );
		Assert.AreEqual( (StaffState.Idle, (0.5f, 5.5f)), (f.Member.State, (f.Member.CellX!.Value, f.Member.CellY!.Value)), "the member is back on its old cell" );

		Assert.IsTrue( f.Placement.PickUp( f.Member ) );
		f.Level.IsRemovingObjects = true;
		Assert.IsFalse( f.Placement.IsActive, "the remove tool ends it" );

		Assert.IsTrue( f.Placement.PickUp( f.Member ) );
		f.Level.IsPlacing = true;
		Assert.IsFalse( f.Placement.IsActive, "placing the prototype ride ends it" );
		Assert.AreEqual( StaffState.Idle, f.Member.State );
	}

	[TestMethod]
	public void ABuildToolEndsAPendingHire()
	{
		var f = new Fixture();
		var candidate = f.Park.Staff.Candidates.First( item => item.Type == StaffType.Guard );
		Assert.IsTrue( f.Placement.BeginHire( candidate.Id ) );
		Assert.IsTrue( f.Level.EnterPathTool( 3, 8 ) );
		Assert.IsFalse( f.Placement.IsActive );
		Assert.AreEqual( 0, f.Park.Staff.PendingPlacementId );
		Assert.IsTrue( f.Park.Staff.Candidates.Any( item => item.Id == candidate.Id ), "nobody was hired" );
	}
}

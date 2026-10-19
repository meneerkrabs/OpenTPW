using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class StaffSpriteHudOwnerTests
{
	private bool oldTextEntry;
	private Logger oldLog = null!;
	[TestInitialize] public void Setup() { oldTextEntry = Input.TextEntryActive; oldLog = Log; Input.TextEntryActive = false; Log ??= new(); }
	[TestCleanup] public void Cleanup() { Input.TextEntryActive = oldTextEntry; Log = oldLog; }
	private static void Set( object owner, string field, object? value ) => owner.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( owner, value );
	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) };
	private sealed class Rig : IDisposable
	{
		internal readonly ParkEconomy Economy = EconomyTestData.Park();
		internal readonly StaffMember Member = StaffOwnerFixture.Member();
		internal readonly ParkHud Hud;
		internal readonly Level Level;
		internal readonly StaffSpritePresentation Presentation;
		internal Rig()
		{
			((List<StaffMember>)Economy.Staff.Members).Add( Member );
			Presentation = new StaffSpritePresentation( StaffOwnerFixture.Catalog(), () => Economy.Staff.Members ); Presentation.AttachRoster( Economy.Staff ); Member.BodySprite!.CreateSource();
			Hud = StaffWindowTests.Hud( Economy ); Level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( Hud )!;
			Set( Level, "<StaffSprites>k__BackingField", Presentation );
			Assert.IsTrue( Hud.OpenEmployeeWindow( Member ) );
		}
		internal UiScreen Rename() { Hud.Stack.Top!.Find( "title" )!.Activate(); Assert.AreEqual( "employeeRename", Hud.Stack.Top!.Name ); return Hud.Stack.Top; }
		public void Dispose() { Hud.Stack.Clear(); Presentation.Dispose(); }
	}

	[TestMethod]
	public void ReopenSelfCycleResizeAndRepaintDoNotReplaceOrTickButDifferentSelectionsFullyReclone()
	{
		using var rig = new Rig(); var owner = rig.Hud.EmployeePortrait!; var ticket = owner.CloneTicket; var epoch = owner.Epoch;
		var state = StaffOwnerFixture.Words( owner.ReadCloneState()! );
		Assert.IsTrue( rig.Hud.OpenEmployeeWindow( rig.Member ) ); Assert.IsFalse( rig.Hud.CycleEmployee( true ) );
		for ( var index = 0; index < 10; index++ ) { _ = ((UiSpriteFigure)rig.Hud.Stack.Top!.Find( "portraitFigure" )!).Frame(); _ = owner.DrawRecord; }
		Assert.AreEqual( ticket, owner.CloneTicket ); Assert.AreEqual( epoch, owner.Epoch ); CollectionAssert.AreEqual( state, StaffOwnerFixture.Words( owner.ReadCloneState()! ) );
		var second = StaffOwnerFixture.Member( 2 ); ((List<StaffMember>)rig.Economy.Staff.Members).Add( second ); rig.Presentation.Attach( second ).CreateSource();
		Assert.IsTrue( rig.Hud.SelectEmployee( second ) ); var secondTicket = owner.CloneTicket; Assert.AreNotEqual( ticket, secondTicket );
		Assert.IsTrue( rig.Hud.SelectEmployee( rig.Member ) ); Assert.AreNotEqual( secondTicket, owner.CloneTicket ); Assert.AreEqual( epoch + 2, owner.Epoch );
	}

	[TestMethod]
	public void RenameCancelPreservesEpochAndSuccessfulUnchangedConfirmationFullyRefreshesExactlyOnce()
	{
		using var rig = new Rig(); var owner = rig.Hud.EmployeePortrait!; var epoch = owner.Epoch; var ticket = owner.CloneTicket;
		var cancelled = rig.Rename(); cancelled.Find( "cancel" )!.Activate();
		Assert.AreEqual( epoch, owner.Epoch ); Assert.AreEqual( ticket, owner.CloneTicket );
		var confirmed = rig.Rename(); confirmed.Find( "apply" )!.Activate();
		Assert.AreEqual( epoch + 1, owner.Epoch ); Assert.AreNotEqual( ticket, owner.CloneTicket );
		confirmed.Find( "apply" )!.Activate(); Assert.AreEqual( epoch + 1, owner.Epoch, "stale child callback cannot repeat the event" );
	}

	[TestMethod]
	public void PausedOuterCoordinatorCopiesUnderRenameWithoutSchedulingAndRepaintMutatesNothing()
	{
		using var rig = new Rig(); var owner = rig.Hud.EmployeePortrait!; rig.Rename();
		var source = rig.Member.BodySprite!; source.Moving( 0, 0, 100, 0, false, false );
		var due = owner.ReadCloneState()!.Due; var currentPc = owner.ReadCloneState()!.CurrentPc;
		rig.Level.PresentStaffSprites( rig.Hud.RefreshEmployeePortrait, 0 );
		Assert.AreEqual( 0u, owner.ReadCloneState()![StaffSpriteRegister.Heading] );
		rig.Level.PresentStaffSprites( rig.Hud.RefreshEmployeePortrait, 0 );
		Assert.AreEqual( 2u, owner.ReadCloneState()![StaffSpriteRegister.Heading] ); Assert.AreEqual( due, owner.ReadCloneState()!.Due ); Assert.AreEqual( currentPc, owner.ReadCloneState()!.CurrentPc );
		var before = StaffOwnerFixture.Words( owner.ReadCloneState()! ); for ( var index = 0; index < 50; index++ ) _ = owner.DrawRecord;
		CollectionAssert.AreEqual( before, StaffOwnerFixture.Words( owner.ReadCloneState()! ) );
		rig.Hud.CloseEmployeeWindow(); rig.Level.PresentStaffSprites( rig.Hud.RefreshEmployeePortrait, 0 ); Assert.AreEqual( 1, rig.Presentation.Registry.Count );
	}

	[DataTestMethod]
	[DataRow( "pop" )]
	[DataRow( "remove" )]
	[DataRow( "clear" )]
	[DataRow( "replace" )]
	[DataRow( "session" )]
	public void ActualRemovalPathsReleaseCloneAndStaleRenameCannotResurrectIt( string path )
	{
		using var rig = new Rig(); var owner = rig.Hud.EmployeePortrait!; var root = rig.Hud.Stack.Top!; var child = rig.Rename();
		switch ( path )
		{
			case "pop": rig.Hud.Stack.Pop(); rig.Hud.Stack.Pop(); break;
			case "remove": rig.Hud.Stack.RemoveWhere( screen => ReferenceEquals( screen, root ) ); break;
			case "clear": rig.Hud.Stack.Clear(); break;
			case "replace": rig.Hud.Stack.Pop(); rig.Hud.Stack.Replace( new UiScreen( "replacement" ) ); break;
			case "session": rig.Presentation.Dispose(); break;
		}
		Assert.AreEqual( EmployeePortraitState.Closed, owner.State ); Assert.IsNull( owner.CloneTicket );
		var epoch = owner.Epoch; child.Find( "apply" )!.Activate(); rig.Hud.CloseEmployeeWindow(); owner.Close();
		Assert.AreEqual( epoch, owner.Epoch ); Assert.IsNull( owner.DrawRecord );
	}

	[TestMethod]
	public void DirectFireImmediatelyRejectsUiIdentityAndCapturedRenameDespiteNumericReplacement()
	{
		using var rig = new Rig(); var child = rig.Rename(); var originalName = rig.Member.Name;
		rig.Economy.Staff.Fire( rig.Member.Id ); Assert.IsNull( rig.Hud.Employee ); Assert.AreEqual( 0, rig.Presentation.Registry.Count );
		var replacement = StaffOwnerFixture.Member(); ((List<StaffMember>)rig.Economy.Staff.Members).Add( replacement ); rig.Presentation.Attach( replacement ).CreateSource();
		((UiTextField)child.Find( "name" )!).Text = "Stale write"; child.Find( "apply" )!.Activate();
		Assert.AreEqual( originalName, rig.Member.Name ); Assert.IsNull( replacement.Name ); Assert.IsNull( rig.Hud.Employee );
	}

	[TestMethod]
	public void HudAuthoredRawCloneCoordinatesArePreservedByPeriodicCopy()
	{
		using var rig = new Rig(); var owner = rig.Hud.EmployeePortrait!;
		foreach ( var state in new[] { owner.ReadCloneState()!, AfterPartial() } )
		{
			Assert.AreEqual( BitConverter.SingleToUInt32Bits( -498f / 1024 ), state[StaffSpriteRegister.X] );
			Assert.AreEqual( BitConverter.SingleToUInt32Bits( 292f / 768 ), state[StaffSpriteRegister.Y] );
			Assert.AreEqual( BitConverter.SingleToUInt32Bits( 1f - 1f / 40 ), state[StaffSpriteRegister.Z] );
		}
		StaffSpriteVmState AfterPartial() { rig.Level.PresentStaffSprites( rig.Hud.RefreshEmployeePortrait ); return owner.ReadCloneState()!; }
	}
}

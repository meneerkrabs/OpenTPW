using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>Boundary and ordered-event acceptance against actual owners and CPU HUD/render adapters, with synthetic resources.</summary>
[TestClass, DoNotParallelize]
public class StaffSpriteAcceptanceEdgeTests
{
	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 4 )]
	public void PerformanceChooserExtremeWordsUseUnsignedCountMinusOneWithoutDrawingRandom( int count )
	{
		var member = StaffOwnerFixture.Member( type: StaffType.Entertainer ); var members = new List<StaffMember> { member };
		using var presentation = new StaffSpritePresentation( StaffOwnerFixture.Catalog( performanceCount: count ), () => members );
		member.BodySprite!.CreateSource();
		// uint.MaxValue is an API arithmetic boundary, not a claim that WorldRng's absolute-value result emits it.
		foreach ( var word in new[] { 0u, uint.MaxValue } )
		{
			var expected = count == 1 ? 0 : (int)(word % (uint)(count - 1));
			member.BodySprite.RequestPerformance( word ); Assert.AreEqual( 13 + expected, member.BodySprite.PendingTemplate );
			member.BodySprite.ApplyPending(); Assert.AreEqual( (StaffSpriteProgram)(256 + expected), member.BodySprite.ReadSourceState()!.RestartPc.Program );
			Assert.AreEqual( 0, member.BodySprite.PendingTemplate ); Assert.IsTrue( expected < 3 );
		}
	}

	[TestMethod]
	public void PerformanceHoleIsConsumedAndMatchingResourceObservationCannotReplayOrRepairIt()
	{
		var atlas = StaffSpriteDrawRecordTests.Atlas();
		var entries = new[] { new SpritePerformance( 0, 4, 0, 0 ), new SpritePerformance( 9, 4, 0, 0 ), new SpritePerformance( 2, 4, 0, 0 ) };
		var bank = new StaffSpriteBankMetadata( 4, 0, 0, atlas.Name, atlas.Width, atlas.Height, atlas.Animations.Animations, atlas.Frames, entries, 3 );
		Assert.IsTrue( bank.Available ); Assert.IsFalse( bank.TryPerformance( 1, out _ ) );
		var member = StaffOwnerFixture.Member( type: StaffType.Entertainer ); var members = new List<StaffMember> { member };
		using var presentation = new StaffSpritePresentation( new StaffSpriteMetadataCatalog( new[] { bank } ), () => members );
		var body = member.BodySprite!; body.CreateSource(); presentation.Registry.Schedule( 63 );
		using var window = new EmployeePortraitOwner( presentation ); window.FullRefresh( member, -.5f, .4f, .975f );
		var before = StaffOwnerFixture.Words( body.ReadSourceState()! ); var clone = StaffOwnerFixture.Words( window.ReadCloneState()! ); var epoch = window.Epoch;
		body.RequestTemplate( 14 ); body.ApplyPending(); Assert.AreEqual( StaffSpriteDiagnostic.InvalidPerformance, body.Diagnostic );
		Assert.AreEqual( 0, body.PendingTemplate ); CollectionAssert.AreEqual( before, StaffOwnerFixture.Words( body.ReadSourceState()! ) );
		entries[1] = new SpritePerformance( 1, 4, 0, 0 ); // Caller-side recovery cannot hot-fill frozen metadata.
		for ( var index = 0; index < 20; index++ )
		{
			Assert.IsTrue( bank.Matches( atlas ), "matching texture layout is independent of the invalid performance entry" );
			Assert.IsTrue( presentation.Catalog.ResolveFrame( window.DrawRecord! ).CanDraw ); body.ApplyPending();
		}
		Assert.IsFalse( bank.TryPerformance( 1, out _ ) ); Assert.AreEqual( epoch, window.Epoch );
		CollectionAssert.AreEqual( before, StaffOwnerFixture.Words( body.ReadSourceState()! ) ); CollectionAssert.AreEqual( clone, StaffOwnerFixture.Words( window.ReadCloneState()! ) );
	}

	// Reuse the real CPU draw rig, including its managed texture sentinel. No graphics device method is invoked.
	private sealed class DrawRig : IDisposable
	{
		private readonly EmployeeLivePortraitTests fixture = new();
		private readonly object rig;
		private readonly Type type = typeof( EmployeeLivePortraitTests ).GetNestedType( "Rig", BindingFlags.NonPublic )!;
		internal DrawRig()
		{
			fixture.Setup();
			rig = Activator.CreateInstance( type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
				new object[] { StaffType.Handyman, 5, 128, 128 }, null )!;
			type.GetMethod( "Start" )!.Invoke( rig, new object[] { 1 } );
		}
		internal T Field<T>( string name ) => (T)type.GetField( name )!.GetValue( rig )!;
		internal EmployeePortraitOwner Window => (EmployeePortraitOwner)type.GetProperty( "Portrait" )!.GetValue( rig )!;
		internal StaffBodySprite Body => (StaffBodySprite)type.GetProperty( "Body" )!.GetValue( rig )!;
		internal UiSpriteFigure Figure => (UiSpriteFigure)type.GetProperty( "Figure" )!.GetValue( rig )!;
		public void Dispose() { ((IDisposable)rig).Dispose(); fixture.Cleanup(); }
	}

	[TestMethod]
	public void ActualCanvasResizesAndFarCameraRepaintsDoNotReplaceScheduleOrMutateOwners()
	{
		using var rig = new DrawRig(); var owner = rig.Window; var ticket = owner.CloneTicket; var epoch = owner.Epoch;
		var source = StaffOwnerFixture.Words( rig.Body.ReadSourceState()! ); var clone = StaffOwnerFixture.Words( owner.ReadCloneState()! );
		var schedules = 0; rig.Field<StaffSpritePresentation>( "Presentation" ).Registry.Scheduled += (_, _, _) => schedules++;
		var renderer = rig.Field<StaffRenderer>( "Renderer" ); var hud = rig.Field<ParkHud>( "Hud" ); var member = rig.Field<StaffMember>( "Member" );
		Camera.ViewMatrix = N.Matrix4x4.CreateTranslation( -1_000_000, -1_000_000, -1_000_000 );
		Camera.ProjMatrix = N.Matrix4x4.CreatePerspectiveFieldOfView( 1, 1, .1f, 10 );
		foreach ( var (width, height) in new[] { (2048, 1536), (640, 480), (1920, 1080), (801, 601) } )
		{
			var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( width, height ) };
			rig.Figure.Draw( context, false, false ); Assert.AreEqual( 1, context.Batch.Draws.Count );
			_ = renderer.PrepareWorld(); Assert.IsTrue( hud.OpenEmployeeWindow( member ) ); Assert.IsFalse( hud.CycleEmployee( true ) );
		}
		Assert.AreEqual( 0, schedules ); Assert.AreEqual( ticket, owner.CloneTicket ); Assert.AreEqual( epoch, owner.Epoch );
		CollectionAssert.AreEqual( source, StaffOwnerFixture.Words( rig.Body.ReadSourceState()! ) ); CollectionAssert.AreEqual( clone, StaffOwnerFixture.Words( owner.ReadCloneState()! ) );
	}

	private sealed record EventRow( long Session, StaffMember Person, int PersonId, int ThingId, int Batch, string Event, int? Sample,
		StaffSpriteTicket? SourceTicket, StaffSpriteTicket? CloneTicket, long Epoch, int Angle, uint? Heading,
		uint[] SourceBefore, uint[] SourceAfter, uint[] CloneBefore, uint[] CloneAfter, string Result );

	[TestMethod]
	public void ActualHudOpenDeleteCloseReopenRenameTapeRetainsEveryExplicitSampleBarrierAndRawState()
	{
		using var rig = new DrawRig(); var hud = rig.Field<ParkHud>( "Hud" ); var level = rig.Field<Level>( "Level" );
		var presentation = rig.Field<StaffSpritePresentation>( "Presentation" ); var member = rig.Field<StaffMember>( "Member" ); var body = rig.Body;
		hud.CloseEmployeeWindow(); var tape = new List<EventRow>(); var scheduled = new List<int>();
		presentation.Registry.Scheduled += (_, sample, _) => scheduled.Add( sample );
		uint[] Source() => body.ReadSourceState() is { } state ? StaffOwnerFixture.Words( state ) : Array.Empty<uint>();
		uint[] Clone() => hud.EmployeePortrait?.ReadCloneState() is { } state ? StaffOwnerFixture.Words( state ) : Array.Empty<uint>();
		void Record( int batch, string kind, Action action, int? sample = null )
		{
			var beforeSource = Source(); var beforeClone = Clone(); action();
			var owner = hud.EmployeePortrait;
			tape.Add( new( presentation.Session, member, member.Id, member.ThingId, batch, kind, sample, body.SourceTicket, owner?.CloneTicket,
				owner?.Epoch ?? 0, body.PersonAngle, body.ReadSourceState()?[StaffSpriteRegister.Heading], beforeSource, Source(), beforeClone, Clone(), owner?.State.ToString() ?? "Closed" ) );
		}
		Record( 1, "Open", () => Assert.IsTrue( hud.OpenEmployeeWindow( member ) ) ); var initial = hud.EmployeePortrait!.CloneTicket;
		Record( 1, "Delete", () => body.DeleteSource() );
		Record( 1, "S", () => presentation.Registry.Schedule( 126 ), 126 );
		Assert.AreEqual( initial, hud.EmployeePortrait!.CloneTicket );
		Record( 2, "Close", hud.CloseEmployeeWindow );
		Record( 2, "S", () => presentation.Registry.Schedule( 189 ), 189 );
		Record( 2, "Reopen", () => Assert.IsTrue( hud.OpenEmployeeWindow( member ) ) );
		Assert.AreEqual( EmployeePortraitState.OpenUnbound, hud.EmployeePortrait!.State );
		Record( 2, "Return", () => body.CreateSource() );
		Record( 2, "P/H", () => level.PresentStaffSprites( hud.RefreshEmployeePortrait ) );
		Assert.AreEqual( EmployeePortraitState.OpenUnbound, hud.EmployeePortrait!.State );
		Record( 3, "RenameOpen", () => hud.Stack.Top!.Find( "title" )!.Activate() );
		Record( 3, "RenameConfirm", () => hud.Stack.Top!.Find( "apply" )!.Activate() );
		Assert.AreEqual( EmployeePortraitState.OpenBound, hud.EmployeePortrait!.State );
		Record( 3, "S", () => presentation.Registry.Schedule( 252 ), 252 );
		Record( 3, "P/H", () => level.PresentStaffSprites( hud.RefreshEmployeePortrait ) );
		CollectionAssert.AreEqual( new[] { "Open", "Delete", "S", "Close", "S", "Reopen", "Return", "P/H", "RenameOpen", "RenameConfirm", "S", "P/H" }, tape.Select( row => row.Event ).ToArray() );
		CollectionAssert.AreEqual( new[] { 126, 252, 252 }, scheduled );
		CollectionAssert.AreEqual( new int?[] { 126, 189, 252 }, tape.Where( row => row.Event == "S" ).Select( row => row.Sample ).ToArray() );
		Assert.IsTrue( tape.All( row => row.Session == presentation.Session && ReferenceEquals( row.Person, member ) && row.PersonId == member.Id && row.ThingId == member.ThingId ) );
		Assert.IsTrue( tape.SelectMany( row => new[] { row.SourceBefore, row.SourceAfter, row.CloneBefore, row.CloneAfter } ).All( words => words.Length is 0 or 70 ) );
		Assert.AreNotEqual( initial, hud.EmployeePortrait!.CloneTicket );
		var preserved = (uint[])tape[0].CloneAfter.Clone(); body.Moving( 0, 0, 100, 0, false, false ); presentation.Present( hud.RefreshEmployeePortrait );
		CollectionAssert.AreEqual( preserved, tape[0].CloneAfter, "event tape arrays are immutable snapshots, not live VM aliases" );
	}
}

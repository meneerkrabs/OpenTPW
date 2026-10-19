using System.Diagnostics;
using System.Text.Json;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using NMatrix = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// Actual OpenTPW GPU consumption, separate from native fidelity and natural gameplay-trigger coverage.
/// Work programs are labelled request fixtures; placement, pickup/return, rename and pause use real host routes.
/// All scheduling and P/H publication remain in GameFlow. No original executable is run by this smoke.
/// </summary>
internal sealed partial class FrontEndSmokeTest
{
	private const int MaximumStaffSmokeFrames = 1500;
	private int staffSmokeFrames;
	private StaffMember? staffSmokeMember;
	private StaffSpritePresentation? staffSmokePresentation;
	private GameSpeed staffSmokeSpeed;
	private (Vector3 Target, float Height, float Yaw)? staffSmokeCamera;
	private Action? restoreStaffVisibility;
	private bool staffSmokeActive;
	private bool staffSmokeBuyOpen;
	private int staffSmokeSchedules;
	private int staffSmokeCloneYields;
	private int staffSmokeResult;
	private object? staffSmokeReference;
	private string StaffEvidencePath => Path.Combine( Path.GetFullPath( "artifacts" ), $"native-smoke-{GameLanguage.Current.Name.ToLowerInvariant()}-staff-owners.jsonl" );
	private ParkEconomy StaffEconomy => flow.Level!.Park!.Economy;
	private StaffRenderer StaffRenderer => flow.Level!.StaffRenderer!;
	private EmployeePortraitOwner StaffPortrait => flow.Hud!.EmployeePortrait!;
	private UiSpriteFigure StaffFigure => (UiSpriteFigure)flow.Hud!.Stack.Screens.Single( screen => screen.Name == "employee" ).Find( "portraitFigure" )!;

	// The bounded mode keeps the same real menu/start route and render/update loop, but avoids unrelated later park rebuilds.
	private void PlanStaffOwnerOnly()
	{
		Wait( "staff owner lobby settles", 3 );
		Do( "staff owner enters jungle", () =>
		{
			Require( flow.Menu?.Stack.Top?.Name == "lobby" && flow.Menu.Selected.Level == "jungle", "bounded staff smoke begins on jungle in the real lobby" );
			flow.Menu!.Main.Focus( flow.Menu.Main.Find( "enterPark" ) );
			flow.InjectedInput = UiInput.Key( UiKeys.Accept );
		} );
		Wait( "staff owner game mode opens", 3 );
		PlanFullSimulationStart( () => CaptureFrame( "staff-owner-game-mode.png" ) );
		Wait( "staff owner jungle renders", 3 );
		Do( "staff owner verifies prerequisite park", () =>
		{
			Require( flow.Level?.OriginalPark?.LevelName == "jungle" && flow.Hud != null && !flow.BuildingPark
				&& flow.StartKind == ParkStartKind.FullSimulation && StaffEconomy.Mode == ParkGameMode.FullSimulation,
				"bounded staff smoke uses the actual fresh Full Simulation jungle and HUD" );
			CaptureFrame( "staff-owner-park.png" );
		} );
		PlanStaffOwners();
		Do( "bounded staff owner smoke passes", () =>
		{
			Device.WaitForIdle(); completed = true;
			Log.Trace( $"Native bounded staff owner smoke test passed: five roles, actual source/clone GPU pairs and lifecycle checks in {staffSmokeFrames} normal outer frames. Full front-end smoke and original raster parity are separate gates." );
			GameFlow.Quit();
		} );
	}

	private void PlanStaffOwners()
	{
		Do( "staff owner smoke starts", () =>
		{
			Require( flow.Level?.StaffSprites != null && flow.Level.StaffRenderer != null && Camera.Mode is ParkCameraMode,
				"staff owner GPU smoke requires the real level owner, renderer and park camera" );
			staffSmokeActive = true; staffSmokeSpeed = StaffEconomy.Speed;
			var camera = (ParkCameraMode)Camera.Mode;
			staffSmokeCamera = (camera.Target, camera.Height, camera.Yaw);
			staffSmokePresentation = flow.Level!.StaffSprites!;
			staffSmokePresentation.Registry.Scheduled += StaffSmokeScheduled;
			flow.Hud!.CloseStaffWindow();
			staffSmokeBuyOpen = flow.Hud.BuyWindowOpen; flow.Hud.CloseBuyWindow();
			camera.SetPose( flow.Level.PathFocus, ParkCameraMode.MaximumHeight, ParkCameraMode.HomeYaw );
			Directory.CreateDirectory( Path.GetDirectoryName( StaffEvidencePath )! );
			File.WriteAllText( StaffEvidencePath, "" );
			StaffEvidence( "scope", "start", new { Backend = Device.BackendType.ToString(),
				Assembly = typeof( FrontEndSmokeTest ).Assembly.GetName().Version?.ToString(), CameraHeight = camera.Height, CameraYaw = camera.Yaw,
				Qualification = "OpenTPW GPU smoke; request fixtures do not prove natural work triggers or original raster parity. Build checkout and driver provenance are supplied by the runner." } );
		} );
		Wait( "staff smoke camera settles", 3 );
		foreach ( var role in Enum.GetValues<StaffType>() )
		{
			Do( $"staff owner hire {role}", () => HireStaffSmoke( role ) );
			WaitStaffOwner( $"{role} actual placement initializes navigation", () => staffSmokeMember!.Navigation != null );
			PlanStaffProgram( role, "stand", StaffSpriteProgram.Stand );
			if ( role == StaffType.Handyman ) PlanStaffTextureReload();
			PlanStaffProgram( role, "walk-request", StaffSpriteProgram.Walk );
			switch ( role )
			{
				case StaffType.Handyman: PlanStaffProgram( role, "sweep-request", StaffSpriteProgram.Sweep ); break;
				case StaffType.Mechanic: PlanStaffProgram( role, "repair-request", StaffSpriteProgram.Repair ); break;
				case StaffType.Researcher: PlanStaffProgram( role, "research-request", StaffSpriteProgram.Research ); break;
				case StaffType.Entertainer: PlanStaffProgram( role, "performance-request", null ); PlanStaffLoss( retainedPerformance: true ); break;
				case StaffType.Guard:
					PlanStaffProgram( role, "catch-request", StaffSpriteProgram.Catch );
					PlanStaffProgram( role, "catch-to-hold", StaffSpriteProgram.Hold, request: false );
					PlanStaffProgram( role, "release-request", StaffSpriteProgram.Template24 );
					break;
			}
			if ( role == StaffType.Handyman ) { PlanStaffRename(); PlanStaffProgram( role, "stand-before-pickup", StaffSpriteProgram.Stand ); PlanStaffLoss( retainedPerformance: false ); }
			Do( $"staff owner dismiss fixture {role}", () =>
			{
				flow.Hud!.CloseEmployeeWindow(); flow.Level!.StaffPlacement!.Cancel();
				StaffEconomy.Fire( staffSmokeMember!.Id ); staffSmokeMember = null;
				StaffEconomy.Speed = GameSpeed.Normal;
			} );
		}
		Do( "staff owner smoke completes", () =>
		{
			StaffEvidence( "scope", "complete", new { RequiredRoles = 5, Qualification = "GPU gate still requires inspection of these captures and exact-build provenance." } );
			RestoreStaffSmoke(); if ( staffSmokeBuyOpen ) flow.Hud!.OpenBuyWindow(); flow.Hud!.OpenStaffWindow();
		} );
		Wait( "staff window restored after owner smoke", 3 );
	}

	private void HireStaffSmoke( StaffType role )
	{
		var hud = flow.Hud!; var level = flow.Level!; var placement = level.StaffPlacement!;
		StaffEconomy.Speed = GameSpeed.Normal;
		hud.OpenStaffWindow(); hud.FilterStaff( role ); hud.ShowEmployedStaff( false ); hud.SelectStaff( 0 );
		Require( hud.StaffCandidates.Count > 0 && hud.SelectedCandidate != null, $"required {role} applicant exists" );
		var bank = staffSmokePresentation!.Catalog.Select( role, StaffSpriteRules.KeyOf( hud.SelectedCandidate! ) );
		Require( bank.Available && !bank.Multipart, $"required {role} bank is available and supported; otherwise the GPU gate remains open" );
		Require( hud.HireSelectedStaff(), $"required {role} applicant can be hired" );
		var cells = level.Guests!.Grid.Cells;
		// Use the entire selected bank (all frames and both mirrors), actual interpolated ground and the same
		// billboard projection as the final draw check. A fixed pixel envelope or mean cell height is insufficient.
		var envelope = StaffFrameEnvelope( bank.Frames );
		var employeeBounds = OriginalLayoutTables.EmployeeWindow.All.Select( node => Context.Canvas.Map( node.Rect, UiAnchor.Center ) ).ToArray();
		bool ClearPlacement( UiRect rect ) => rect.Width > 0 && rect.Height > 0 && rect.X >= 0 && rect.Right <= Context.Canvas.Width
			&& rect.Y >= 0 && rect.Bottom <= Context.Canvas.Height
			&& employeeBounds.All( bounds => StaffRectsSeparate( rect, bounds ) ) && StaffUiClear( rect );
		(int X, int Y)? chosen = null;
		for ( var y = 0; y < cells.Height && chosen == null; y++ )
			for ( var x = 0; x < cells.Width && chosen == null; x++ )
			{
				if ( !placement.IsValid( x, y ) ) continue;
				var feet = GuestRenderer.CellToEngine( level.OriginalPark!.Heightfield, x + 0.5f, y + 0.5f );
				if ( StaffProjectFrameRect( feet, envelope, Camera.ViewMatrix, Camera.ProjMatrix,
					Context.Canvas.Width, Context.Canvas.Height, pulled: true ) is not { } rect ) continue;
				if ( ClearPlacement( rect )
					&& StaffEconomy.Staff.Members.All( member => member.CellX == null || Math.Abs( member.CellX.Value - x ) + Math.Abs( member.CellY!.Value - y ) > 6 ) ) chosen = (x, y);
			}
		if ( chosen == null )
		{
			StaffEvidence( "placement-failure", role.ToString(), new { Envelope = new[] { envelope.Left, envelope.Right, envelope.Top, envelope.Bottom },
				Context.Canvas.Width, Context.Canvas.Height, CameraHeight = ((ParkCameraMode)Camera.Mode).Height } );
			CaptureFrame( "staff-placement-failure.png" );
		}
		Require( chosen != null, $"an isolated visible placement cell exists for required {role}" );
		Require( placement.Click( chosen!.Value.X, chosen.Value.Y ) == StaffClickResult.Placed, $"actual placement hires {role}" );
		staffSmokeMember = placement.LastPlaced!;
		Require( staffSmokeMember.Type == role && staffSmokeMember.BodySprite?.SourceTicket != null, $"{role} has its actual source owner" );
		// The same labelled idle request-fixture setup is installed before the first navigation turn, avoiding placement drift.
		staffSmokeMember.Job = StaffJob.Idle; staffSmokeMember.State = StaffState.Idle; staffSmokeMember.IdleStamp = StaffEconomy.Turn + 100_000;
		Require( hud.OpenEmployeeWindow( staffSmokeMember ), "actual employee root opens" );
		StaffEvidence( "lifecycle", $"{role}-hire-place", new { staffSmokeMember.Id, staffSmokeMember.ThingId, bank.Kind, bank.Variant, Slots = bank.Slots.Count, Frames = bank.Frames.Count } );
		StaffEvidence( "request-fixture", $"{role}-placement-idle", new { IdleSetupOnly = true, RandomDrawAdded = false } );
	}

	private void PlanStaffProgram( StaffType role, string label, StaffSpriteProgram? program, bool request = true )
	{
		var name = $"{role.ToString().ToLowerInvariant()}-{label}";
		StaffSpriteProgram expected = default;
		Do( $"staff request fixture {name}", () =>
		{
			var member = staffSmokeMember!;
			// Explicit smoke setup only: keep this newly hired fixture stationary while normal turns consume its requests.
			// Natural role-state transitions are covered by StaffSourceEventIntegrationTests, not inferred from these shots.
			member.Job = StaffJob.Idle; member.State = StaffState.Idle; member.IdleStamp = StaffEconomy.Turn + 100_000;
			if ( program is { } ordinary ) { expected = ordinary; if ( request ) member.BodySprite!.RequestTemplate( (int)ordinary ); }
			else
			{
				var bank = staffSmokePresentation!.Catalog.Select( role, StaffSpriteRules.KeyOf( member ) );
				Require( bank.TryPerformance( 0, out var performance ), "required entertainer performance entry zero is supported" );
				expected = (StaffSpriteProgram)(256 + performance.Program); member.BodySprite!.RequestPerformance( 0 );
			}
			StaffEconomy.Speed = GameSpeed.Normal;
			StaffEvidence( "request-fixture", name, new { Program = expected.ToString(), Requested = request, RandomDrawAdded = false } );
		} );
		WaitStaffOwner( $"normal scheduling publishes {name}", () =>
		{
			var state = staffSmokeMember!.BodySprite!.ReadSourceState()!;
			var current = state.CurrentPc.Program;
			var matches = request ? state.RestartPc.Program == expected && (program == null || current == expected) : current == expected;
			if ( !matches || state.CurrentPc.Instruction == 0 || (program == null && current != (StaffSpriteProgram)(272 + (int)expected - 256))
				|| state.OutputValid == 0 || staffSmokeMember.BodySprite.PendingTemplate != 0
				|| staffSmokeMember.BodySprite.DrawRecord is not { } record || !StaffRenderer.ResolveRecord( record ).CanDraw ) return false;
			StaffEconomy.Speed = GameSpeed.Paused;
			return true;
		} );
		Do( $"actual root reopen after {name}", () =>
		{
			flow.Hud!.CloseEmployeeWindow(); Require( flow.Hud.OpenEmployeeWindow( staffSmokeMember! ), "a real open fully copies the published work program" );
		} );
		StabilizeStaffOwner( name );
		PlanStaffPair( name + "-world", portrait: false );
		PlanStaffPair( name + "-portrait", portrait: true );
		Do( $"measure staff observation {name}", () => MeasureStaffObservers( name ) );
	}

	// Finite frame waits use the existing post-draw queue. No direct Tick, Schedule, ApplyPending or Present calls occur here.
	private void WaitStaffOwner( string name, Func<bool> done, int maximumFrames = 180 )
	{
		var started = -1;
		steps.Enqueue( (name, () =>
		{
			if ( started < 0 ) started = frame;
			if ( done() ) return true;
			Require( frame - started < maximumFrames, $"staff smoke '{name}' completes within {maximumFrames} normal outer frames" );
			return false;
		}) );
	}

	private void StabilizeStaffOwner( string name )
	{
		string? previous = null; var consecutive = 0; var lastFrame = -1;
		WaitStaffOwner( $"{name} normal paused P/H becomes idempotent", () =>
		{
			Require( StaffEconomy.Speed == GameSpeed.Paused, "paired capture setup pauses through the real economy speed gate" );
			if ( frame == lastFrame ) return false;
			lastFrame = frame;
			var current = StaffObservation();
			consecutive = current == previous ? consecutive + 1 : 0; previous = current;
			if ( consecutive < 3 ) return false;
			StaffEvidence( "observation", name + "-stable", new { ConsecutiveNormalOuterUpdates = consecutive }, current );
			return true;
		} );
	}

	private void PlanStaffPair( string name, bool portrait, bool expectContribution = true, Func<UiRect>? fixedRoi = null,
		Action<(byte[] Pixels, int Width, int Height)>? observeVisible = null )
	{
		string? before = null; UiRect roi = default;
		(byte[] Pixels, int Width, int Height) visible = default, hidden = default;
		Do( $"{name} visible readback", () =>
		{
			try
			{
				Require( restoreStaffVisibility == null, "one visibility pair owns its restoration" );
				roi = StaffEvidenceRect( fixedRoi?.Invoke(), () => portrait ? expectContribution ? StaffPortraitRect() : StaffFigure.ScreenRect( Context.Canvas ) : StaffWorldRect( staffSmokeMember! ) );
				Require( roi.Width > 0 && roi.Height > 0 && roi.X >= 0 && roi.Y >= 0 && roi.Right <= Context.Canvas.Width && roi.Bottom <= Context.Canvas.Height,
					$"{name} expected sprite ROI is wholly visible" );
				if ( !portrait ) RequireIsolatedStaffRect( roi );
				before = staffResourceObservation ?? StaffObservation();
				Require( before == StaffObservation(), $"{name} retains the original resource-sequence baseline" );
				visible = CaptureFrame( name + "-visible.png" );
				observeVisible?.Invoke( visible );
				Require( before == StaffObservation(), $"{name} GPU readback does not mutate owners or gameplay" );
				if ( portrait )
				{
					var figure = StaffFigure; var wasVisible = figure.Visible; Require( wasVisible, "employee figure begins visible" );
					restoreStaffVisibility = () => figure.Visible = wasVisible; figure.Visible = false;
				}
				else
				{
					var renderer = StaffRenderer; var wasVisible = renderer.Visible; Require( wasVisible, "staff renderer begins visible" );
					restoreStaffVisibility = () => renderer.Visible = wasVisible; renderer.Visible = false;
				}
				Require( before == StaffObservation(), $"{name} visibility-only toggle preserves layout/text and complete state" );
			}
			catch { RestoreStaffVisibility(); throw; }
		} );
		Wait( $"{name} hidden frame", 1 );
		Do( $"{name} hidden readback", () =>
		{
			try
			{
				Require( before == StaffObservation(), $"{name} camera, viewport, RNG, hash and all VM words remain identical across normal P/H" );
				hidden = CaptureFrame( name + "-hidden.png" );
				Require( before == StaffObservation(), $"{name} hidden readback is observational" );
				var changed = StaffPixelChanges( visible, hidden, roi );
				Require( expectContribution ? changed > 0 : changed == 0,
					$"{name} {(expectContribution ? "contributes nonzero pixels" : "contributes no pixels")} inside its expected ROI" );
			}
			finally { RestoreStaffVisibility(); }
		} );
		Wait( $"{name} restored frame", 1 );
		Do( $"{name} restored readback", () =>
		{
			try
			{
				Require( before == StaffObservation(), $"{name} restored pair retains the complete observation fingerprint" );
				var restored = CaptureFrame( name + "-restored.png" );
				Require( before == StaffObservation(), $"{name} restored readback is observational" );
				Require( StaffPixelChanges( visible, restored, roi ) == 0, $"{name} background and visible contribution are stable, excluding unrelated time-driven pixels" );
				StaffEvidence( "gpu-pair", name, new { Portrait = portrait, ExpectedContribution = expectContribution, Roi = new[] { roi.X, roi.Y, roi.Width, roi.Height }, ChangedPixels = StaffPixelChanges( visible, hidden, roi ) }, before );
			}
			finally { RestoreStaffVisibility(); }
		} );
	}

	private void PlanStaffLoss( bool retainedPerformance )
	{
		var label = retainedPerformance ? "performer-retained-screen" : "stand-screen-to-world";
		StaffSpriteTicket? source = null, clone = null; int scheduleBefore = 0; long epoch = 0;
		Do( label + " actual pickup", () =>
		{
			var state = StaffPortrait.ReadCloneState()!;
			if ( !retainedPerformance ) Require( state.CurrentPc == new StaffSpritePc( StaffSpriteProgram.Stand, 3 ) && state.RestartPc.Program == StaffSpriteProgram.Stand,
				"Stand has normally yielded immediately before its JMP then SET flags 0x1200" );
			else Require( state.CurrentPc.Program is StaffSpriteProgram.Performance0Loop or StaffSpriteProgram.Performance1Loop or StaffSpriteProgram.Performance2Loop or StaffSpriteProgram.Performance3Loop,
				"the performer is already inside a loop without another flags SET" );
			Require( StaffPortrait.DrawRecord?.Route == StaffSpriteRoute.Screen, "the clone starts in Screen space" );
			source = staffSmokeMember!.BodySprite!.SourceTicket; clone = StaffPortrait.CloneTicket; epoch = StaffPortrait.Epoch;
			Require( flow.Level!.StaffPlacement!.PickUp( staffSmokeMember ), "actual pickup succeeds while the root remains open for the source-loss observation" );
			Require( staffSmokeMember.BodySprite.SourceTicket == null && StaffPortrait.CloneTicket == clone && StaffPortrait.Epoch == epoch, "pickup deletes only the source allocation" );
			StaffEvidence( "lifecycle", label + "-pickup", new { Via = "StaffPlacement.PickUp; root intentionally remains open" } );
		} );
		StabilizeStaffOwner( label + "-absent-paused" );
		PlanStaffPair( label + "-absent-paused-portrait", portrait: true );
		Do( label + " resume normal scheduler", () => { scheduleBefore = staffSmokeCloneYields; StaffEconomy.Speed = GameSpeed.Normal; } );
		WaitStaffOwner( label + " scheduled clone route", () =>
		{
			if ( staffSmokeCloneYields == scheduleBefore || StaffPortrait.DrawRecord is not { } record ) return false;
			if ( retainedPerformance )
			{
				Require( record.Route == StaffSpriteRoute.Screen, "the absent performer's loop retains Screen flags after real scheduling" );
				if ( !StaffRenderer.ResolveRecord( record ).CanDraw ) return false;
			}
			else if ( record.Route != StaffSpriteRoute.World ) return false;
			StaffEconomy.Speed = GameSpeed.Paused;
			Require( StaffPortrait.CloneTicket == clone && StaffPortrait.Epoch == epoch, "the independent clone keeps its allocation and epoch after source loss" );
			if ( !retainedPerformance ) Require( StaffRenderer.ResolveRecord( record ).Diagnostic == StaffSpriteDiagnostic.WorldRouteNotImplemented && StaffFigure.Frame() == null,
				"next Stand SET 0x1200 selects the explicit undrawn clone world route" );
			StaffEvidence( "lifecycle", label + "-scheduled-route", new { Route = record.Route.ToString(), Diagnostic = StaffRenderer.ResolveRecord( record ).Diagnostic.ToString() } );
			return true;
		} );
		StabilizeStaffOwner( label + "-scheduled" );
		if ( retainedPerformance ) PlanStaffPair( label + "-scheduled-portrait", portrait: true );
		else PlanStaffPair( label + "-undrawn", portrait: true, expectContribution: false );
		Do( label + " actual return", () =>
		{
			flow.Level!.StaffPlacement!.Cancel();
			Require( staffSmokeMember!.BodySprite!.SourceTicket != null && staffSmokeMember.BodySprite.SourceTicket != source && StaffPortrait.CloneTicket == clone && StaffPortrait.Epoch == epoch,
				"actual carry cancellation creates a fresh source and retains the existing clone" );
			StaffEconomy.Speed = GameSpeed.Normal;
		} );
		WaitStaffOwner( label + " return output", () =>
		{
			if ( staffSmokeMember!.BodySprite!.DrawRecord is not { } record || !StaffRenderer.ResolveRecord( record ).CanDraw || StaffFigure.Frame() == null ) return false;
			staffSmokeMember.Job = StaffJob.Idle; staffSmokeMember.State = StaffState.Idle; staffSmokeMember.IdleStamp = StaffEconomy.Turn + 100_000;
			StaffEconomy.Speed = GameSpeed.Paused; return true;
		} );
		StabilizeStaffOwner( label + "-returned" );
		PlanStaffPair( label + "-returned-world", portrait: false );
		PlanStaffPair( label + "-returned-portrait", portrait: true );
		Do( label + " actual HUD pickup closes root", () =>
		{
			Require( flow.Hud!.PickUpEmployee() && !flow.Hud.EmployeeWindowOpen && flow.Hud.EmployeePortrait == null, "actual employee pickup handler closes and releases the clone" );
			flow.Level!.StaffPlacement!.Cancel(); Require( flow.Hud.OpenEmployeeWindow( staffSmokeMember! ), "employee root reopens after actual return" );
			StaffEvidence( "lifecycle", label + "-hud-pickup-and-return", null );
		} );
	}

	private void PlanStaffRename()
	{
		UiScreen? child = null; long epoch = 0; string? pauseObservation = null;
		StaffMember? captured = null, other = null; string? otherName = null;
		Do( "prepare captured-target rename comparison", () => { captured = staffSmokeMember; HireStaffSmoke( StaffType.Guard ); other = staffSmokeMember; otherName = other!.Name; } );
		WaitStaffOwner( "rename comparison employee initializes", () => other!.Navigation != null );
		Do( "rename comparison requests Stand", () =>
		{
			other!.Job = StaffJob.Idle; other.State = StaffState.Idle; other.IdleStamp = StaffEconomy.Turn + 100_000;
			other.BodySprite!.RequestTemplate( (int)StaffSpriteProgram.Stand );
			StaffEvidence( "request-fixture", "rename-comparison-stand", null );
		} );
		WaitStaffOwner( "rename comparison source publishes", () =>
		{
			if ( other!.BodySprite!.ReadSourceState() is not { } state || state.CurrentPc != new StaffSpritePc( StaffSpriteProgram.Stand, 3 )
				|| other.BodySprite.PendingTemplate != 0 || other.BodySprite.DrawRecord is not { } record || !StaffRenderer.ResolveRecord( record ).CanDraw ) return false;
			StaffEconomy.Speed = GameSpeed.Paused; staffSmokeMember = captured; Require( flow.Hud!.SelectEmployee( captured! ), "rename starts on its captured employee" ); return true;
		} );
		Do( "staff actual rename cancel", () =>
		{
			flow.Hud!.Screen.Find( "speedNormal" )!.Activate(); flow.Hud.Screen.Find( "speedPaused" )!.Activate();
			Require( StaffEconomy.Speed == GameSpeed.Paused, "the actual HUD speed button handler pauses the park" );
			epoch = StaffPortrait.Epoch; flow.Hud!.Stack.Top!.Find( "title" )!.Activate(); child = flow.Hud.Stack.Top;
			Require( child!.Name == "employeeRename" && StaffPortrait.Epoch == epoch, "rename opening keeps the clone epoch" );
			child.Find( "cancel" )!.Activate(); Require( StaffPortrait.Epoch == epoch, "rename cancellation keeps the clone epoch" );
			flow.Hud.Stack.Top!.Find( "title" )!.Activate(); child = flow.Hud.Stack.Top;
			child!.Find( "apply" )!.Activate(); Require( StaffPortrait.Epoch == epoch + 1, "unchanged successful confirmation is exactly one full event" );
			child.Find( "apply" )!.Activate(); Require( StaffPortrait.Epoch == epoch + 1, "stale confirmation cannot apply twice" );
			flow.Hud.Stack.Top!.Find( "title" )!.Activate(); child = flow.Hud.Stack.Top;
			((UiTextField)child!.Find( "name" )!).Text = "Owner GPU smoke";
			StaffEvidence( "rename", "cancel-unchanged-stale", null );
		} );
		StabilizeStaffOwner( "paused-rename-modal" );
		Do( "staff paused rename baseline", () => { pauseObservation = StaffObservation(); CaptureFrame( "staff-paused-rename.png" ); } );
		Wait( "staff paused rename normal outer frames", 6 );
		Do( "staff paused rename remains stable then applies", () =>
		{
			Require( pauseObservation == StaffObservation(), "paused rename keeps full VM state, scheduler count, RNG, gameplay and camera while normal P/H runs" );
			epoch = StaffPortrait.Epoch;
			Require( flow.Hud!.SelectEmployee( other! ) && ReferenceEquals( flow.Hud.Stack.Top, child ) && StaffPortrait.Epoch == epoch + 1,
				"selection changes fully refresh the current clone without dismissing the captured rename" );
			child!.Find( "apply" )!.Activate();
			Require( captured!.Name == "Owner GPU smoke" && other!.Name == otherName && ReferenceEquals( flow.Hud.Employee, other )
				&& ReferenceEquals( StaffPortrait.BoundIdentity?.Member, other ) && StaffPortrait.Epoch == epoch + 2,
				"changed rename writes its captured member and fully refreshes the current selection exactly once" );
			child.Find( "apply" )!.Activate(); Require( StaffPortrait.Epoch == epoch + 2, "captured stale callback cannot repeat the full event" );
			StaffEvidence( "rename-pause", "captured-target-changed-confirmation", new { PausedOuterFrames = 6 } );
			Require( flow.Hud.SelectEmployee( captured ), "restore the selected fixture after captured-target rename" ); StaffEconomy.Fire( other.Id );
		} );
	}

	private void StaffSmokeScheduled( StaffSpriteTicket ticket, int now, StaffSpriteTickResult result )
	{
		staffSmokeSchedules++;
		if ( ticket.Identity.Member != staffSmokeMember || ticket.Kind != StaffSpriteOwnerKind.EmployeeClone || result.Status == StaffSpriteTickStatus.NotDue ) return;
		if ( result.Status == StaffSpriteTickStatus.Yielded ) staffSmokeCloneYields++;
		StaffEvidence( "normal-scheduler", "employee-clone", new { now, Slot = ticket.Slot, ticket.Generation, Status = result.Status.ToString() } );
	}

	private string StaffObservation() => JsonSerializer.Serialize( new
	{
		Hash = flow.Level!.ComputeStateHash().ToString( "x16" ), Random = flow.Level.CaptureRandomState(), Sample = staffSmokePresentation!.LastSample,
		Schedules = staffSmokeSchedules, CloneYields = staffSmokeCloneYields, Session = staffSmokePresentation.Session, RegistryCount = staffSmokePresentation.Registry.Count,
		View = StaffMatrixWords( Camera.ViewMatrix ), Projection = StaffMatrixWords( Camera.ProjMatrix ),
		Viewport = new[] { Screen.PixelSize.X, Screen.PixelSize.Y, Context.Canvas.Width, Context.Canvas.Height },
		WorldViewport = new[] { global::Global.Render.MultisampledFramebuffer.Width, global::Global.Render.MultisampledFramebuffer.Height },
		Sources = StaffEconomy.Staff.Members.Select( member => new { member.Id, member.ThingId,
			Ticket = StaffTicket( member.BodySprite?.SourceTicket ), Angle = member.BodySprite?.PersonAngle, PendingTemplate = member.BodySprite?.PendingTemplate, PendingInterval = member.BodySprite?.PendingInterval,
			Words = StaffWords( member.BodySprite?.ReadSourceState() ) } ).ToArray(),
		Clone = new { Ticket = StaffTicket( flow.Hud!.EmployeePortrait?.CloneTicket ), Epoch = flow.Hud.EmployeePortrait?.Epoch,
			State = flow.Hud.EmployeePortrait?.State.ToString(), Words = StaffWords( flow.Hud.EmployeePortrait?.ReadCloneState() ) },
		Layout = flow.Hud.Stack.Screens.Where( screen => screen.Name is "employee" or "employeeRename" ).Select( screen => new { screen.Name,
			Elements = screen.Elements.Select( element => new { element.Id, Bounds = new[] { element.Bounds.X, element.Bounds.Y, element.Bounds.Width, element.Bounds.Height },
				Anchor = element.Anchor.ToString(), Text = element is UiLabel label ? label.Text() : element is UiTextField text ? text.Text : null } ).ToArray() } ).ToArray()
	} );

	private static object? StaffTicket( StaffSpriteTicket? ticket ) => ticket is { } value
		? new { value.Session, value.Slot, value.Generation, Kind = value.Kind.ToString(), value.Identity.Id, value.Identity.ThingId } : null;
	internal static uint[] StaffWords( StaffSpriteVmState? state ) => state == null ? Array.Empty<uint>() : new[]
	{
		state.OpaqueWord, state.Handle, state.CurrentPc.Word, state.RestartPc.Word, state.LastPc.Word, state.ProgramTable,
		(uint)state.RunState, unchecked( (uint)state.StackPointer )
	}.Concat( state.Stack ).Concat( new[] { unchecked( (uint)state.IfDepth ), unchecked( (uint)state.CallDepth ), unchecked( (uint)state.LoopDepth ),
		unchecked( (uint)state.Due ), unchecked( (uint)state.Interval ) } ).Concat( state.Registers ).Concat( state.DrawCache ).Append( state.OutputValid ).ToArray();
	private static uint[] StaffMatrixWords( NMatrix value ) => new[] { value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24,
		value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44 }.Select( BitConverter.SingleToUInt32Bits ).ToArray();

	private void StaffEvidence( string category, string stage, object? details, string? observation = null )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( StaffEvidencePath )! );
		File.AppendAllText( StaffEvidencePath, JsonSerializer.Serialize( new { Category = category, Stage = stage, OuterFrame = frame, Details = details,
			Observation = JsonSerializer.Deserialize<JsonElement>( observation ?? StaffObservation() ) } ) + Environment.NewLine );
	}

	private UiRect StaffPortraitRect()
	{
		var figure = StaffFigure; var value = figure.Frame(); Require( value != null, "required employee clone resolves a screen frame" );
		var rect = figure.ScreenRect( Context.Canvas );
		var feet = figure.FeetInCanvas is { } authored ? Context.Canvas.Map( authored, figure.Anchor ) : UiSpriteFigure.Feet( rect );
		return UiSpriteFigure.Layout( value!.Value, feet, figure.CanvasUnitsPerPixel * rect.Width / figure.Bounds.Width ).Rect;
	}

	private UiRect StaffWorldRect( StaffMember member )
	{
		var record = member.BodySprite?.DrawRecord; Require( record != null && record.Route == StaffSpriteRoute.World, "required world source has output" );
		var view = Camera.ViewMatrix; var right = new NVector3( view.M11, view.M21, view.M31 );
		var forward = -new NVector3( view.M13, view.M23, view.M33 );
		var resolved = StaffRenderer.ResolveRecord( record!, GuestSpriteAtlas.CameraYaw( right, forward, true ) );
		Require( resolved.CanDraw, $"required source resource resolves ({resolved.Diagnostic})" );
		var feet = GuestRenderer.CellToEngine( flow.Level!.OriginalPark!.Heightfield, record!.PositionX, record.PositionY, record.PositionZ );
		var rect = StaffProjectFrameRect( feet, resolved.Frame.Extents( resolved.Mirror, GuestRenderer.UnitsPerPixel ), view, Camera.ProjMatrix,
			Context.Canvas.Width, Context.Canvas.Height, (record.Flags & 0x200) != 0 );
		Require( rect != null, "source quad is finite and in front of the camera" );
		return rect!.Value;
	}

	internal static (float Left, float Right, float Top, float Bottom) StaffFrameEnvelope( IReadOnlyList<GuestAtlasFrame> frames )
	{
		Require( frames.Count > 0, "placement envelope requires a real bank" );
		var extents = frames.SelectMany( value => new[] { value.Extents( false, GuestRenderer.UnitsPerPixel ), value.Extents( true, GuestRenderer.UnitsPerPixel ) } ).ToArray();
		return (extents.Min( value => value.X0 ), extents.Max( value => value.X1 ), extents.Max( value => value.Top ), extents.Min( value => value.Bottom ));
	}

	internal static UiRect? StaffProjectFrameRect( NVector3 feet, (float Left, float Right, float Top, float Bottom) extents,
		NMatrix view, NMatrix projection, int width, int height, bool pulled )
	{
		var right = new NVector3( view.M11, view.M21, view.M31 ); var up = new NVector3( view.M12, view.M22, view.M32 );
		var forward = -new NVector3( view.M13, view.M23, view.M33 );
		var share = 1f; if ( pulled ) (feet, share) = GuestRenderer.PullTowardsEye( feet, GuestRenderer.Eye( view ), forward );
		NVector2? Project( float x, float y )
		{
			var clip = NVector4.Transform( new NVector4( feet + right * (x * share) + up * (y * share), 1 ), view * projection );
			if ( !(clip.W > 0) ) return null;
			var point = new NVector2( (clip.X / clip.W + 1) * 0.5f * width, (1 - clip.Y / clip.W) * 0.5f * height );
			return float.IsFinite( point.X ) && float.IsFinite( point.Y ) ? point : null;
		}
		var corners = new[] { Project( extents.Left, extents.Top ), Project( extents.Right, extents.Top ), Project( extents.Left, extents.Bottom ), Project( extents.Right, extents.Bottom ) };
		if ( corners.Any( point => point == null ) ) return null;
		var x0 = corners.Min( point => point!.Value.X ); var y0 = corners.Min( point => point!.Value.Y );
		return new( x0, y0, corners.Max( point => point!.Value.X ) - x0, corners.Max( point => point!.Value.Y ) - y0 );
	}

	private void RequireIsolatedStaffRect( UiRect target )
	{
		Require( flow.Level!.StaffPlacement!.Kind == StaffToolKind.None, "world contribution pair excludes the dangling sprite" );
		var staff = new List<object>();
		foreach ( var other in StaffEconomy.Staff.Members.Where( member => member != staffSmokeMember ) )
		{
			if ( other.BodySprite?.DrawRecord is not { Route: StaffSpriteRoute.World } record || !StaffRenderer.ResolveRecord( record ).CanDraw ) continue;
			var rect = StaffWorldRect( other );
			if ( !(rect.Right < target.X - 2 || rect.X > target.Right + 2 || rect.Bottom < target.Y - 2 || rect.Y > target.Bottom + 2) )
				staff.Add( new { other.Id, other.ThingId, Rect = rect } );
		}
		var ui = flow.Hud!.Stack.Screens.SelectMany( screen => screen.Elements.Where( element => element.Visible )
			.Select( element => new { Screen = screen.Name, Element = element.Id, Type = element.GetType().Name, Rect = element.ScreenRect( Context.Canvas ) } ) )
			.Where( element => !StaffRectsSeparate( target, element.Rect ) ).ToArray();
		if ( staff.Count > 0 || ui.Length > 0 )
		{
			var details = new { Roi = target, Staff = staff, Ui = ui };
			StaffEvidence( "isolation-failure", "world", details );
			Log.Warning( $"Staff world ROI isolation failed: {JsonSerializer.Serialize( details )}" );
			try { CaptureFrame( "staff-isolation-failure.png" ); }
			catch ( Exception exception ) { Log.Warning( $"Could not retain staff isolation failure capture: {exception.Message}" ); }
		}
		Require( staff.Count == 0, "no other staff quad overlaps the target ROI when the whole staff renderer is hidden" );
		Require( StaffUiClear( target ), "no active window or HUD element overlaps the isolated world sprite ROI" );
	}

	private bool StaffUiClear( UiRect target ) => flow.Hud!.Stack.Screens
		.SelectMany( screen => screen.Elements.Where( element => element.Visible ) )
		.All( element => StaffRectsSeparate( target, element.ScreenRect( Context.Canvas ) ) );
	internal static bool StaffRectsSeparate( UiRect target, UiRect other ) => other.Width <= 0 || other.Height <= 0
		|| other.Right < target.X - 2 || other.X > target.Right + 2 || other.Bottom < target.Y - 2 || other.Y > target.Bottom + 2;

	internal static int StaffPixelChanges( (byte[] Pixels, int Width, int Height) a, (byte[] Pixels, int Width, int Height) b, UiRect roi )
	{
		Require( a.Width == b.Width && a.Height == b.Height, "paired readback dimensions are identical" );
		var count = 0;
		for ( var y = Math.Max( 0, (int)Math.Floor( roi.Y ) ); y < Math.Min( a.Height, (int)Math.Ceiling( roi.Bottom ) ); y++ )
			for ( var x = Math.Max( 0, (int)Math.Floor( roi.X ) ); x < Math.Min( a.Width, (int)Math.Ceiling( roi.Right ) ); x++ )
				if ( !a.Pixels.AsSpan( (y * a.Width + x) * 4, 3 ).SequenceEqual( b.Pixels.AsSpan( (y * a.Width + x) * 4, 3 ) ) ) count++;
		return count;
	}

	private void MeasureStaffObservers( string stage )
	{
		var before = StaffObservation(); var body = staffSmokeMember!.BodySprite!; var record = body.DrawRecord!; var clone = StaffPortrait.DrawRecord!;
		var bank = staffSmokePresentation!.Catalog.Resolve( record.Kind, record.Variant );
		void Measure( string path, Action action, int calls )
		{
			var thread = Environment.CurrentManagedThreadId;
			for ( var warm = 0; warm < 64; warm++ ) action();
			var nanos = new double[21]; var allocations = new long[21];
			for ( var batch = 0; batch < nanos.Length; batch++ )
			{
				var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
				for ( var call = 0; call < calls; call++ ) action();
				var elapsed = Stopwatch.GetTimestamp() - start; allocations[batch] = GC.GetAllocatedBytesForCurrentThread() - allocated;
				nanos[batch] = elapsed * (1_000_000_000d / Stopwatch.Frequency) / calls;
			}
			Require( thread == Environment.CurrentManagedThreadId && before == StaffObservation(), "warmed synchronous observation batches preserve all VM words/RNG/hash on one thread" );
			Array.Sort( nanos ); Array.Sort( allocations );
			StaffEvidence( "microbenchmark", stage, new { Path = path, CallsPerBatch = calls, Batches = nanos.Length, WarmupCalls = 64,
				MedianNanosecondsPerCall = nanos[10], P95NanosecondsPerCall = nanos[19], MedianBytesPerCall = allocations[10] / (double)calls,
				bank.Kind, bank.Variant, Slots = bank.Slots.Count, BankFrames = bank.Frames.Count,
				Qualification = "Same-thread call cost only; repeated observations are not staff population or FPS. Readback and logging excluded." }, before );
		}
		Measure( "fresh-DrawRecord", () => staffSmokeReference = body.DrawRecord, 200 );
		foreach ( var calls in new[] { 5, 50, 200 } ) Measure( "cached-record-ResolveRecord-including-layout-match", () => staffSmokeResult = StaffRenderer.ResolveRecord( record ).FrameIndex, calls );
		Measure( "actual-PrepareWorld", () => staffSmokeResult = StaffRenderer.PrepareWorld(), 50 );
		Measure( "EmployeeFrame", () => staffSmokeResult = StaffRenderer.EmployeeFrame( clone )?.Width ?? 0, 200 );
		GC.KeepAlive( staffSmokeReference ); GC.KeepAlive( staffSmokeResult );
	}

	private void RestoreStaffVisibility()
	{
		try { restoreStaffVisibility?.Invoke(); }
		finally { restoreStaffVisibility = null; }
	}
	private void RestoreStaffSmoke()
	{
		try { RestoreStaffVisibility(); }
		finally
		{
			RestoreStaffResources();
			if ( staffSmokeActive )
			{
				if ( staffSmokePresentation != null ) staffSmokePresentation.Registry.Scheduled -= StaffSmokeScheduled;
				if ( flow.Level?.Park != null ) flow.Level.Park.Economy.Speed = staffSmokeSpeed;
				if ( staffSmokeCamera is { } pose && Camera.Mode is ParkCameraMode camera ) camera.SetPose( pose.Target, pose.Height, pose.Yaw );
				staffSmokeActive = false;
			}
		}
	}
}

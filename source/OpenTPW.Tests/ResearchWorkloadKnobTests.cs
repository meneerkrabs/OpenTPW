using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>Research workload input through the real screen, persistent raw state, lifecycle and actual gauge draw geometry; no original assets.</summary>
[TestClass, DoNotParallelize]
public class ResearchWorkloadKnobTests
{
	private static void Set( object owner, string field, object? value ) => owner.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( owner, value );
	private static T Get<T>( object owner, string field ) => (T)owner.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( owner )!;

	private sealed record World( ParkHud Hud, ParkEconomy Economy, Level Level, UiContext Context )
	{
		public UiScreen Screen => Hud.Stack.Top!;
		public ParkHud.ResearchWorkloadKnob Knob => (ParkHud.ResearchWorkloadKnob)Screen.Find( "workloadKnob" )!;
		public NVector2 At( float x, float y ) => Context.Canvas.Map( new NVector2( x, y ), UiAnchor.Center );
		public void Frame( UiInput input ) => Hud.Stack.Update( Context, input );
		public void Down( float x, float y ) => Frame( new UiInput( At( x, y ), true, true, false, false, UiKeys.None ) );
		public void Hold( float x, float y ) => Frame( new UiInput( At( x, y ), true, false, false, false, UiKeys.None ) );
		public void Up( float x, float y, bool notClick = false ) => Frame( UiInput.Click( At( x, y ) ) with { NotClick = notClick } );
		public void Click( float x, float y ) { Down( x, y ); Up( x, y ); }
		public void Idle() => Frame( UiInput.Idle( At( 1000, 1100 ) ) );
	}

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name =>
	{
		var data = Md2ModelFileTests.CreateGeometry();
		Array.Clear( data, 0x1a4, 12 );
		Encoding.ASCII.GetBytes( name + "\0" ).CopyTo( data, 0x1a4 );
		return new UiModel( new ModelFile( new MemoryStream( data ) ), name );
	} ) ) { Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = _ => null };

	private static World Build( int workload = 85, bool fullRange = true )
	{
		// c=0, divisor33 yields limit1024 exactly; ordinary balance settings instead yield563.
		var extra = fullRange ? "StaffPoolInfo.MaxResearchersInPark 33\nResearcherConstsPerGrade[4].ResearchAbility 1\n" : "";
		var economy = new ParkEconomy( EconomyTestData.Settings( extra: extra ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		economy.Research.SetWorkload( workload );
		var hud = StaffWindowTests.Hud( economy );
		Set( hud, "researchFluids", Enumerable.Range( 0, 5 ).Select( _ => new ResearchFluid() ).ToArray() );
		Set( hud, "researchFluidClock", new ReferenceFrameClock( 0.25f ) );
		hud.OpenResearchWindow();
		return new World( hud, economy, Get<Level>( hud, "level" ), Context() );
	}

	[TestMethod]
	public void TrackClickStepsOnceOnQualifiedReleaseAndNeverRecentresDuringHold()
	{
		var w = Build();
		Assert.AreEqual( (1024, 512), (w.Knob.Limit, w.Knob.Value) );
		w.Down( 300, 300 );
		Assert.AreEqual( 512, w.Knob.Value, "button-down only arms the page" );
		w.Hold( 300, 600 );
		w.Hold( 300, 450 );
		Assert.AreEqual( 512, w.Knob.Value, "crossing the ball never turns Page into Grab" );
		w.Up( 300, 300, notClick: true );
		Assert.AreEqual( 512, w.Knob.Value, "a moved release does not page" );
		w.Click( 300, 300 );
		Assert.AreEqual( 513, w.Knob.Value );
		w.Up( 300, 300 );
		Assert.AreEqual( 513, w.Knob.Value, "an unpaired release cannot repeat a captured page" );
		w.Click( 300, 600 );
		Assert.AreEqual( 512, w.Knob.Value );
	}

	[TestMethod]
	public void RawSubWorkloadStepsSurviveOrdinaryFramesDrawsEffortAndTargetChanges()
	{
		var w = Build();
		for ( var step = 1; step <= 35; step++ )
		{
			w.Click( 300, 300 );
			w.Economy.SetResearchEffort( ResearchCategory.Ride, step );
			w.Economy.Research.MoveCurrent( ResearchCategory.Ride, 1 );
			w.Idle();
			w.Context.Batch.Clear();
			w.Knob.Draw( w.Context, false, false );
			Assert.AreEqual( 512 + step, w.Knob.Value );
			Assert.AreEqual( 70 + (512 + step) * 30 / 1024, w.Economy.Research.Workload );
		}
		Assert.AreEqual( (547, 86), (w.Knob.Value, w.Economy.Research.Workload) );
	}

	[DataTestMethod]
	[DataRow( 329f, 335f, 1 )]
	[DataRow( 330f, 329f, 0 )]
	[DataRow( 272f, 274f, 0 )]
	[DataRow( 273f, 268f, 1 )]
	[DataRow( 354f, 354f, 0 )]
	public void PageEligibilityUsesTheSavedPressWithinTheNarrowerTrack( float pressX, float releaseX, int step )
	{
		var w = Build();
		w.Down( pressX, 300 );
		w.Up( releaseX, 300 );
		Assert.AreEqual( 512 + step, w.Knob.Value );
	}

	[TestMethod]
	public void QualifiedReleaseOverTheBallStillPagesAndBallProtrusionGrabs()
	{
		var w = Build();
		var top = w.Knob.BallTop;
		w.Down( 300, top - 1 );
		w.Up( 300, top + 1 );
		Assert.AreEqual( 513, w.Knob.Value, "the saved track recipient remains authoritative" );
		top = w.Knob.BallTop;
		w.Down( 268, top + 20 );
		Assert.AreEqual( 513, w.Knob.Value, "a stationary press does not quantize a stored raw value" );
		w.Hold( 268, top + 30 );
		Assert.AreEqual( top + 10, w.Knob.BallTop );
		var value = w.Knob.Value;
		w.Up( 268, top + 30, notClick: true );
		Assert.AreEqual( value, w.Knob.Value, "a drag release is not a page" );
	}

	[DataTestMethod]
	[DataRow( 0, 1 )]
	[DataRow( 1, -1 )]
	public void PageDirectionIncludesTheCurrentBallBottomEquality( int below, int step )
	{
		var w = Build();
		w.Click( 300, w.Knob.BallRect.Bottom + below );
		Assert.AreEqual( 512 + step, w.Knob.Value );
	}

	[TestMethod]
	public void OvershootAndReverseUseTheOriginalPressRectangleAtBothBounds()
	{
		var w = Build();
		var top = w.Knob.BallTop;
		w.Down( 300, top + 20 );
		w.Hold( 300, 1000 );
		Assert.AreEqual( 634f, w.Knob.BallTop );
		w.Hold( 300, 990 );
		Assert.AreEqual( 634f, w.Knob.BallTop, "a small reversal remains beyond the press-relative lower saturation" );
		w.Hold( 300, 650 );
		Assert.AreEqual( 630f, w.Knob.BallTop );
		w.Hold( 300, -100 );
		Assert.AreEqual( 220f, w.Knob.BallTop );
		w.Hold( 300, -90 );
		Assert.AreEqual( 220f, w.Knob.BallTop, "the mirror overshoot also retains its original baseline" );
		w.Hold( 300, 250 );
		Assert.AreEqual( 230f, w.Knob.BallTop );
		w.Up( 300, 250, notClick: true );
		Assert.AreEqual( ParkHud.WorkloadKnobTop( w.Knob.Value ), w.Knob.BallTop );
		var nextTop = w.Knob.BallTop;
		w.Down( 300, nextTop + 10 );
		w.Hold( 300, nextTop + 15 );
		Assert.AreEqual( nextTop + 5, w.Knob.BallTop, "the next press saves the newly normalized rectangle" );
	}

	[TestMethod]
	public void DrawKeepsTheMovedRectangleUntilReleaseExplicitlyNormalizesIt()
	{
		var w = Build();
		w.Down( 300, 450 );
		w.Hold( 300, 450.5f );
		Assert.AreEqual( 426.5f, w.Knob.BallTop );
		Assert.AreNotEqual( w.Knob.BallTop, ParkHud.WorkloadKnobTop( w.Knob.Value ) );
		w.Knob.Draw( w.Context, false, true );
		var vertices = w.Context.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray();
		Assert.AreEqual( 426.5f, vertices.Min( vertex => vertex.Position.Y ), 0.001f );
		Assert.AreEqual( 493.5f, vertices.Max( vertex => vertex.Position.Y ), 0.001f );
		w.Up( 300, 450.5f );
		Assert.AreEqual( ParkHud.WorkloadKnobTop( w.Knob.Value ), w.Knob.BallTop );
	}

	[TestMethod]
	public void AbilityRefreshDuringGrabKeepsThePressBaselineAndIgnoresStationaryHeldFrames()
	{
		var w = Build( fullRange: false );
		var pressTop = w.Knob.BallTop;
		var pressY = pressTop + 20;
		w.Down( 300, pressY );
		var oldWorkload = w.Economy.Research.Workload;
		EconomyTestData.HireBest( w.Economy, StaffType.Researcher );
		w.Hold( 300, pressY );
		var projected = ParkHud.KnobFromWorkload( oldWorkload, w.Knob.Limit );
		Assert.AreEqual( projected, w.Knob.Value );
		Assert.AreEqual( ParkHud.WorkloadKnobTop( projected ), w.Knob.BallTop );
		w.Hold( 300, pressY );
		Assert.AreEqual( projected, w.Knob.Value, "a second ordinary held frame must not reproject the rounded workload or replay the grab" );
		w.Hold( 300, pressY + 1 );
		Assert.AreEqual( pressTop + 1, w.Knob.BallTop, "the old press rectangle survives the ability refresh" );
		Assert.AreEqual( ParkHud.KnobValueAt( pressTop + 1 + 33.5f ), w.Knob.Value );
	}

	[TestMethod]
	public void AReducedStaffingLimitClampsTheNextPressRelativeMoveWithoutRebasingIt()
	{
		var w = Build( workload: 100, fullRange: false );
		for ( var count = 0; count < 3; count++ ) EconomyTestData.HireBest( w.Economy, StaffType.Researcher ).Grade = 4;
		w.Idle();
		Assert.AreEqual( (768, 768, 323f), (w.Knob.Limit, w.Knob.Value, w.Knob.BallTop) );
		w.Down( 300, 350 );
		foreach ( var member in w.Economy.Staff.OfType( StaffType.Researcher ).ToArray() ) w.Economy.Fire( member.Id );
		w.Hold( 300, 350 );
		Assert.AreEqual( (563, 563, 406f), (w.Knob.Limit, w.Knob.Value, w.Knob.BallTop) );
		w.Hold( 300, 351 );
		Assert.AreEqual( (563, 406f), (w.Knob.Value, w.Knob.BallTop), "the original baseline would exceed the new cap, whose setter repositions the ball" );
		w.Hold( 300, 500 );
		Assert.AreEqual( 473f, w.Knob.BallTop, "a later in-range move still uses top323 + (500-350)" );
		Assert.AreEqual( 397, w.Knob.Value );
	}

	[DataTestMethod]
	[DataRow( 70 )] [DataRow( 40 )]
	public void UnchangedInitialRawZeroKeepsSavedWorkloadAndTheExplicitHostStartupGeometry( int workload )
	{
		var w = Build( workload );
		Assert.AreEqual( 0, w.Knob.Value );
		Assert.AreEqual( workload, w.Economy.Research.Workload, "an unchanged setter does not synthesize a workload notification" );
		Assert.AreEqual( 634f, w.Knob.BallTop, "this retained host startup position is still the documented UI-064 residual" );
	}

	[TestMethod]
	public void PageDirectionComparesSavedPointWithBallAfterAbilityRefresh()
	{
		var w = Build( workload: 100, fullRange: false );
		var savedY = w.Knob.BallTop - 1;
		w.Down( 300, savedY );
		for ( var count = 0; count < 3; count++ ) EconomyTestData.HireBest( w.Economy, StaffType.Researcher ).Grade = 4;
		w.Hold( 300, savedY );
		var value = w.Knob.Value;
		Assert.IsTrue( savedY > w.Knob.BallRect.Bottom, "more staffing moved the current ball above the saved point" );
		w.Up( 300, savedY );
		Assert.AreEqual( value - 1, w.Knob.Value );
	}

	[TestMethod]
	public void ClampsKeepRawRangeCapacityAndProtectZeroDivisors()
	{
		foreach ( var fullRange in new[] { true, false } )
		{
			var w = Build( fullRange: fullRange );
			w.Down( 300, w.Knob.BallTop + 20 );
			w.Hold( 300, -100 );
			w.Up( 300, -100, notClick: true );
			Assert.AreEqual( Math.Min( w.Knob.Limit, 1023 ), w.Knob.Value );
			for ( var count = 0; count < 3; count++ ) w.Click( 300, 221 );
			Assert.AreEqual( Math.Min( w.Knob.Limit, 1023 ), w.Knob.Value );
			w.Down( 300, w.Knob.BallTop + 20 );
			w.Hold( 300, 1200 );
			w.Up( 300, 1200, notClick: true );
			Assert.AreEqual( 0, w.Knob.Value );
			for ( var count = 0; count < 3; count++ ) w.Click( 300, 700 );
			Assert.AreEqual( 0, w.Knob.Value );
		}
		Assert.AreEqual( 1023, ParkHud.WorkloadKnobLimit( 0, 0, 6 ) );
		Assert.AreEqual( 100, ParkHud.WorkloadFromKnob( 100, 0 ) );
	}

	[TestMethod]
	public void KeyboardExtensionSynchronizesRawOnceWithoutRoundingItsRequestedWorkloadDown()
	{
		var w = Build( fullRange: false );
		w.Screen.Focus( w.Knob );
		var wanted = w.Economy.Research.Workload + 1;
		w.Frame( UiInput.Key( UiKeys.Right ) );
		var value = ParkHud.KnobFromWorkload( wanted, w.Knob.Limit );
		Assert.AreEqual( wanted, w.Economy.Research.Workload );
		Assert.AreEqual( value, w.Knob.Value );
		w.Idle();
		Assert.AreEqual( (wanted, value), (w.Economy.Research.Workload, w.Knob.Value) );
	}

	[TestMethod]
	public void ReopenUsesFreshControlsAndRemovalCancelsAnUnfinishedPageOrGrab()
	{
		var w = Build();
		w.Click( 300, 300 );
		var old = w.Screen;
		var oldKnob = w.Knob;
		w.Down( 300, 300 );
		w.Hud.CloseResearchWindow();
		Assert.IsTrue( old.IsRemoved );
		w.Hud.OpenResearchWindow();
		Assert.AreNotSame( old, w.Screen );
		Assert.AreEqual( 512, w.Knob.Value, "reopen projects persisted workload85, not the old raw513" );
		w.Up( 300, 300 );
		Assert.AreEqual( 512, w.Knob.Value );
		oldKnob.ClickCaptured( w.Context.Canvas, w.At( 300, 300 ) );
		Assert.AreEqual( 512, w.Knob.Value );
		w.Down( 300, w.Knob.BallTop + 20 );
		w.Hold( 300, 700 );
		w.Hud.Stack.Pop();
		Assert.IsFalse( w.Hud.ResearchWindowOpen );
		Assert.IsNull( Get<SoundSentence?>( w.Hud, "researchHum" ) );
		Assert.IsNull( Get<SoundSentence?>( w.Hud, "researchSecondHum" ) );
		w.Hud.OpenResearchWindow();
		var reopened = w.Knob.Value;
		w.Up( 300, 600 );
		Assert.AreEqual( reopened, w.Knob.Value );
	}

	[TestMethod]
	public void ReadOnlyAndDisabledTransitionsCancelCapturedInputAndKeyboardChanges()
	{
		var w = Build();
		w.Down( 300, 300 );
		Set( w.Level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		w.Up( 300, 300 );
		Assert.AreEqual( 512, w.Knob.Value );
		w.Knob.Adjust( 1 );
		Assert.AreEqual( 85, w.Economy.Research.Workload );
		Set( w.Level, "<Visit>k__BackingField", null );
		w.Idle();
		w.Up( 300, 300 );
		Assert.AreEqual( 512, w.Knob.Value, "reenabling does not resurrect the cancelled click" );
		w.Screen.Updating = null;
		w.Down( 300, 300 );
		w.Knob.Enabled = false;
		w.Up( 300, 300 );
		w.Knob.Adjust( 1 );
		Assert.AreEqual( (512, 85), (w.Knob.Value, w.Economy.Research.Workload) );
	}

	[TestMethod]
	public void LosingPointerCaptureCancelsThePendingPageBeforeAnyLaterRelease()
	{
		var w = Build();
		w.Down( 300, 300 );
		w.Idle();
		w.Up( 300, 300 );
		Assert.AreEqual( 512, w.Knob.Value );
		w.Down( 300, w.Knob.BallTop + 20 );
		w.Hold( 300, 450.5f );
		w.Idle();
		Assert.AreEqual( ParkHud.WorkloadKnobTop( w.Knob.Value ), w.Knob.BallTop );
		var value = w.Knob.Value;
		w.Up( 300, 600 );
		Assert.AreEqual( value, w.Knob.Value );
	}

	[TestMethod]
	public void RawChangesUpdateBothHumBindingsAndUnchangedFramesDoNotNotify()
	{
		var w = Build();
		var first = new SoundSentence( null!, null!, new SoundEvent( 154, 0, 6, 12, Array.Empty<SoundEntry>() ), AudioChannel.Effects, () => 0 );
		var second = new SoundSentence( null!, null!, new SoundEvent( 155, 0, 6, 13, Array.Empty<SoundEntry>() ), AudioChannel.Effects, () => 0 );
		Set( w.Hud, "researchHum", first );
		Set( w.Hud, "researchSecondHum", second );
		w.Click( 300, 300 );
		Assert.AreEqual( 50, first.Parameter );
		Assert.AreEqual( 50, second.Parameter );
		first.Parameter = second.Parameter = -1;
		w.Idle();
		w.Down( 300, w.Knob.BallTop + 20 );
		w.Hold( 300, w.Knob.BallTop + 20 );
		w.Up( 300, w.Knob.BallTop + 20 );
		Assert.AreEqual( -1, first.Parameter, "stationary press/release only normalizes; it does not notify a raw change" );
		Assert.AreEqual( -1, second.Parameter );
		w.Click( 300, 300 );
		Assert.AreEqual( ParkHud.ResearchHumParameter( w.Knob.Value ), first.Parameter );
		Assert.AreEqual( ParkHud.ResearchHumParameter( w.Knob.Value ), second.Parameter );
		w.Hud.CloseResearchWindow();
		Assert.IsTrue( first.IsStopped );
		Assert.IsTrue( second.IsStopped );
	}

	[TestMethod]
	public void TimeoutAndMovedOutAndBackKeepTheSharedInputCancellation()
	{
		var originalMouse = Input.Mouse;
		try
		{
			var w = Build();
			long now = 0;
			var source = new UiInputSource { Clock = () => now };
			void Frame( bool down, float y )
			{
				Input.Mouse = new Input.MouseInfo { Left = down, Position = w.At( 300, y ) };
				w.Frame( source.Poll( 1, 1 ) );
			}
			Frame( true, 300 );
			Frame( true, 300 );
			Frame( true, 300 );
			now = 501;
			Frame( false, 300 );
			Assert.AreEqual( 512, w.Knob.Value );
			Frame( true, 300 );
			Frame( true, 310 );
			Frame( true, 300 );
			now += 100;
			Frame( false, 300 );
			Assert.AreEqual( 512, w.Knob.Value, "returning inside the slop does not restore the click" );
		}
		finally { Input.Mouse = originalMouse; }
	}

	[DataTestMethod]
	[DataRow( 70, 0 )] [DataRow( 73, 0 )] [DataRow( 74, 1 )] [DataRow( 76, 1 )]
	[DataRow( 77, 2 )] [DataRow( 80, 2 )] [DataRow( 81, 3 )] [DataRow( 83, 3 )]
	[DataRow( 84, 4 )] [DataRow( 87, 4 )] [DataRow( 88, 5 )] [DataRow( 90, 5 )]
	[DataRow( 91, 6 )] [DataRow( 94, 6 )] [DataRow( 95, 7 )] [DataRow( 97, 7 )]
	[DataRow( 98, 8 )] [DataRow( 100, 8 )]
	public void TheExistingCameoSelectsEverySourceDerivedStateBoundary( int workload, int frame )
	{
		var w = Build();
		w.Economy.Research.SetWorkload( workload );
		Assert.AreEqual( frame, ((UiModelImage)w.Screen.Find( "cameo" )!).Frame() );
		Assert.AreEqual( 1, w.Screen.Elements.Count( element => element.Id == "cameo" ) );
	}

	[DataTestMethod]
	[DataRow( -1, 0f )] [DataRow( 0, 0f )] [DataRow( 256, 0.25f )] [DataRow( 512, 0.5f )]
	[DataRow( 1, 1 / 1024f )] [DataRow( 511, 511 / 1024f )] [DataRow( 1023, 1023 / 1024f )]
	[DataRow( 768, 0.75f )] [DataRow( 1024, 1f )] [DataRow( 2048, 1f )]
	public void CapacityGaugeDrawsCroppedUprightTextureHalvesWithSharedBottomUpSplit( int value, float q )
	{
		var batch = new UiBatch();
		ParkHud.ResearchCapacityGauge.DrawGauge( batch, new UiRect( 354, 238, 68, 445 ), value );
		Assert.AreEqual( 1, batch.Draws.Count );
		Assert.AreEqual( ParkHud.ResearchCapacityGauge.ArtPath, batch.Draws[0].Texture.ImagePath );
		var vertices = batch.Draws[0].Vertices;
		Assert.IsTrue( vertices.All( vertex => vertex.Color.Equals( Veldrid.RgbaByte.White ) ) );
		Assert.AreEqual( q is 0 or 1 ? 6 : 12, vertices.Count );
		var split = 683 - 445 * q;
		if ( q < 1 ) CheckQuad( vertices.Take( 6 ).ToArray(), new UiRect( 354, 238, 68, split - 238 ), new NVector2( .5f, 0 ), new NVector2( 1, 1 - q ) );
		if ( q > 0 ) CheckQuad( vertices.Skip( q < 1 ? 6 : 0 ).ToArray(), new UiRect( 354, split, 68, 683 - split ), new NVector2( 0, 1 - q ), new NVector2( .5f, 1 ) );
	}

	private static void CheckQuad( UiVertex[] vertices, UiRect rect, NVector2 uv0, NVector2 uv1 )
	{
		Assert.AreEqual( rect.X, vertices.Min( vertex => vertex.Position.X ) );
		Assert.AreEqual( rect.Right, vertices.Max( vertex => vertex.Position.X ) );
		Assert.AreEqual( rect.Y, vertices.Min( vertex => vertex.Position.Y ) );
		Assert.AreEqual( rect.Bottom, vertices.Max( vertex => vertex.Position.Y ) );
		Assert.AreEqual( uv0, vertices[0].TexCoords );
		Assert.AreEqual( uv1, vertices[^1].TexCoords );
		Assert.IsTrue( vertices.Where( vertex => vertex.Position.Y == rect.Y ).All( vertex => vertex.TexCoords.Y == uv0.Y ) );
		Assert.IsTrue( vertices.Where( vertex => vertex.Position.Y == rect.Bottom ).All( vertex => vertex.TexCoords.Y == uv1.Y ) );
	}

	[TestMethod]
	public void ActualCapacityGaugeIgnoresWorkloadAndRefreshesOnlyWithStaffing()
	{
		var w = Build( fullRange: false );
		var gauge = (ParkHud.ResearchCapacityGauge)w.Screen.Find( "capacityGauge" )!;
		gauge.Draw( w.Context, false, false );
		var first = w.Context.Batch.Draws.Single().Vertices.ToArray();
		w.Click( 300, 300 );
		w.Economy.SetResearchEffort( ResearchCategory.Ride, 15 );
		w.Idle();
		w.Context.Batch.Clear();
		gauge.Draw( w.Context, false, false );
		CollectionAssert.AreEqual( first, w.Context.Batch.Draws.Single().Vertices.ToArray() );
		EconomyTestData.HireBest( w.Economy, StaffType.Researcher );
		w.Idle();
		w.Context.Batch.Clear();
		gauge.Draw( w.Context, false, false );
		Assert.AreNotEqual( first[1].Position.Y, w.Context.Batch.Draws.Single().Vertices[1].Position.Y );
	}
}

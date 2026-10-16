using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>The variable box's b_minus and b_plus while held (tp.exe 0x668dd9, 0x66a7e1, 0x66aa61; UI-024).</summary>
[TestClass]
public class VariableBoxHoldTests
{
	[TestMethod]
	public void TheHoldStartStepsOnlyThePlusButtonAndTheIncrementGrowsTenfoldEveryTwentyTicks()
	{
		var hold = new ParkHud.VariableBoxHold();
		Assert.AreEqual( 11, hold.Start( 10, plus: true, 0, 500 ), "0x66a7e1 case 3: value + step at the hold start" );
		Assert.AreEqual( 10, new ParkHud.VariableBoxHold().Start( 10, plus: false, 0, 500 ), "case 2 only arms the count" );
		var value = 11;
		for ( var tick = 1; tick <= 19; tick++ )
			value = hold.Tick( value, true, 0, 10000 );
		Assert.AreEqual( 11 + 19, value, "twenty ticks at an increment of 1 ..." );
		value = hold.Tick( value, true, 0, 10000 );
		Assert.AreEqual( 11 + 20, value, "... the twentieth still adds 1" );
		Assert.AreEqual( 10, hold.Increment, "and then the increment is 10" );
		Assert.AreEqual( 20, hold.Counter );
		value = hold.Tick( value, true, 0, 10000 );
		Assert.AreEqual( 31 + 10, value );
		for ( var tick = 2; tick <= 20; tick++ )
			value = hold.Tick( value, true, 0, 10000 );
		Assert.AreEqual( 100, hold.Increment, "the next twenty ticks end in another tenfold step" );
	}

	[TestMethod]
	public void TheMinusButtonCountsDownAndEveryValueStaysInTheBoxsRange()
	{
		var hold = new ParkHud.VariableBoxHold();
		hold.Start( 5, plus: false, 0, 500 );
		Assert.AreEqual( 4, hold.Tick( 5, false, 0, 500 ) );
		Assert.AreEqual( 0, hold.Tick( 1, false, 0, 500 ) );
		Assert.AreEqual( 0, hold.Tick( 0, false, 0, 500 ), "0x66a71c raises a value below the minimum to it" );
		Assert.AreEqual( 500, new ParkHud.VariableBoxHold().Start( 500, true, 0, 500 ) );
	}

	private static (UiScreen Screen, UiButton Button, List<string> Log) Fixture()
	{
		var log = new List<string>();
		var screen = new UiScreen( "box" );
		var button = new UiButton
		{
			Id = "plus", Model = null, Bounds = new UiRect( 0, 0, 100, 100 ), Anchor = UiAnchor.TopLeft, Clicked = () => log.Add( "click" ),
			Repeat = new UiButtonRepeat( 0.2f, 0.05f, () => log.Add( "start" ), () => log.Add( "tick" ) )
		};
		screen.Add( button );
		return (screen, button, log);
	}

	private static void Frame( UiScreen screen, float time, bool down = false, bool pressed = false, bool released = false )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( n => throw new System.IO.FileNotFoundException( n ) ) )
			{ Canvas = new UiCanvas( 2048, 1536 ), Time = time, ResolveTexture = _ => null };
		screen.Update( context, new UiInput( new NVector2( 50, 50 ), down || pressed, pressed, released, false, UiKeys.None ) );
	}

	[TestMethod]
	public void AQuickPressIsAClickAndNothingRepeats()
	{
		var (screen, _, log) = Fixture();
		Frame( screen, 0, pressed: true );
		Frame( screen, 0.1f, down: true );
		Frame( screen, 0.15f, released: true );
		CollectionAssert.AreEqual( new[] { "click" }, log );
	}

	[TestMethod]
	public void AHeldButtonStartsAfter200MsTicksOverFiftyMsApartAndLettingGoIsNoClick()
	{
		var (screen, button, log) = Fixture();
		Frame( screen, 0, pressed: true );
		Frame( screen, 0.1f, down: true );
		Frame( screen, 0.19f, down: true );
		Assert.AreEqual( 0, log.Count, "nothing before the delay" );
		Frame( screen, 0.2f, down: true );
		CollectionAssert.AreEqual( new[] { "start" }, log );
		Frame( screen, 0.24f, down: true );
		Assert.AreEqual( 1, log.Count, "not yet 50 ms after the start" );
		Frame( screen, 0.25f, down: true );
		Assert.AreEqual( 1, log.Count, "exactly 50 ms is not more than the interval" );
		Frame( screen, 0.26f, down: true );
		CollectionAssert.AreEqual( new[] { "start", "tick" }, log );
		Frame( screen, 0.33f, down: true );
		Frame( screen, 0.33f, released: true );
		CollectionAssert.AreEqual( new[] { "start", "tick", "tick" }, log, "the release of a repeating button posts no click (0x668eff)" );
		Assert.AreEqual( 0, button.HoldState );
	}
}

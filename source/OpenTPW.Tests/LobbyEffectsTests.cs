using System.Linq;
using OpenTPW.FrontEnd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LobbyEffectsTests
{
	private static LobbyIslandInfo Island( string level, (byte, byte, byte) sky, bool rainy = false, int lightning = 0 ) =>
		new( 0, level, level, "island", "gate", level, 0, 0, sky, System.Array.Empty<string>(), rainy, lightning );

	/// <summary>One draw of tp.exe's lobby generator from <paramref name="state"/>: the LCG step rotated right by 13 bits.</summary>
	internal static uint NextDraw( uint state ) => System.Numerics.BitOperations.RotateRight( unchecked(state * 0x19660Du + 0x3C6EF35Fu), 13 );

	[TestMethod]
	public void TheGeneratorReturnsTheLcgStepRotatedRightByThirteenAndKeepsIt()
	{
		var effects = new LobbyEffects( 1 );
		var first = unchecked(1u * 0x19660Du + 0x3C6EF35Fu);
		Assert.AreEqual( (first >> 13) | (first << 19), effects.Draw() );
		var second = unchecked(((first >> 13) | (first << 19)) * 0x19660Du + 0x3C6EF35Fu);
		Assert.AreEqual( (second >> 13) | (second << 19), effects.Draw(), "the rotated value is the next step's state" );
	}

	[TestMethod]
	public void TheRainCountFollowsTheMeanSquaredDistanceToTheRainyIslands()
	{
		Assert.AreEqual( 0, LobbyEffects.RainCount( null ), "no rainy island, no rain" );
		Assert.AreEqual( 400, LobbyEffects.RainCount( 63.0 * 63 ), "close to hallow the count is capped at 400" );
		Assert.AreEqual( 173, LobbyEffects.RainCount( 70.0 * 70 ), "at the orbit radius 10^17 / 70^8" );
		Assert.AreEqual( 0, LobbyEffects.RainCount( 92.0 * 92 ), "below 20 drops none fall" );
	}

	[TestMethod]
	public void TheSkyStartsBlackAndShowsTheFogColourWhileItEqualsItsTarget()
	{
		var effects = new LobbyEffects( 7 );
		Assert.AreEqual( LobbyEffects.ClearColour, effects.Sky, "before any tick the grid keeps the fog colour" );
		Assert.AreEqual( ((byte)0x44, (byte)0xDD, (byte)0xFF), LobbyEffects.ClearColour );
		var black = Island( "jungle", (0, 0, 0) );
		effects.Step( LobbyEffects.TickSeconds, new[] { new LobbyEffects.Island( black, 0, 0 ) }, 0, 0 );
		Assert.AreEqual( LobbyEffects.ClearColour, effects.Sky, "the colour starts black, so a black target is already reached" );
	}

	[TestMethod]
	public void TheCloudGridKeepsTheFogColourWhileSkyQualityIsBelowTwo()
	{
		// 0x5d96c0: the eased colour needs SKYQUALITY ([0x785888]) above 1; otherwise the grid is the fog colour and the easing does not step
		var effects = new LobbyEffects( 7 ) { EasedSky = false };
		var jungle = new LobbyEffects.Island( Island( "jungle", (243, 203, 191) ), 0, 0 );
		for ( var tick = 0; tick < 50; tick++ )
			effects.Step( LobbyEffects.TickSeconds, new[] { jungle }, 0, 0 );
		Assert.AreEqual( LobbyEffects.ClearColour, effects.Sky );
		effects.EasedSky = true;
		effects.Step( LobbyEffects.TickSeconds, new[] { jungle }, 0, 0 );
		Assert.AreEqual( ((byte)11, (byte)12, (byte)11), effects.Sky, "the easing starts from black when the gate opens: it did not step while closed" );
	}

	[TestMethod]
	public void TheSkyEasesASixteenthOfTheWayWithTheRedChannelTowardTheBlueTarget()
	{
		var effects = new LobbyEffects( 7 );
		var jungle = new LobbyEffects.Island( Island( "jungle", (243, 203, 191) ), 0, 0 );
		effects.Step( LobbyEffects.TickSeconds, new[] { jungle }, 0, 0 );
		// From black: (191 << 16) >> 4 = 191 × 4096 → 11; (203 << 16) >> 4 → 12.
		Assert.AreEqual( ((byte)11, (byte)12, (byte)11), effects.Sky, "red moves toward the blue target" );
		for ( var tick = 0; tick < 400; tick++ )
			effects.Step( LobbyEffects.TickSeconds, new[] { jungle }, 0, 0 );
		Assert.AreEqual( ((byte)190, (byte)202, (byte)190), effects.Sky, "the shift stops 15/65536 short, and red settles at the blue value" );
		var space = new LobbyEffects.Island( Island( "space", (125, 8, 8) ), 0, 0 );
		effects.Step( LobbyEffects.TickSeconds, new[] { space }, 0, 0 );
		// Down from 190.99…: the arithmetic shift rounds toward minus infinity.
		Assert.IsTrue( effects.Sky.R < 190 && effects.Sky.R == effects.Sky.B, $"red and blue fall together toward 8: {effects.Sky}" );
		effects.StepGlobe( 1f );
		Assert.AreEqual( 0, effects.RainDrops );
	}

	[TestMethod]
	public void TheSkyShiftRoundsTowardMinusInfinity()
	{
		var effects = new LobbyEffects( 7 );
		var white = new LobbyEffects.Island( Island( "fantasy", (255, 255, 255) ), 0, 0 );
		for ( var tick = 0; tick < 400; tick++ )
			effects.Step( LobbyEffects.TickSeconds, new[] { white }, 0, 0 );
		var black = new LobbyEffects.Island( Island( "hallow", (0, 0, 0) ), 0, 0 );
		for ( var tick = 0; tick < 2000; tick++ )
			effects.Step( LobbyEffects.TickSeconds, new[] { black }, 0, 0 );
		Assert.AreEqual( LobbyEffects.ClearColour, effects.Sky, "going down, sar 4 keeps stepping by at least −1 until the target is reached exactly" );
	}

	[TestMethod]
	public void ALightningIslandStrikesWhenTheDrawMatchesAndPlacesTheBoltAroundIt()
	{
		// A seed whose first draw has (draw & 63) == 1: hallow's LIGHTNING 63 strikes on the first tick.
		var seed = Enumerable.Range( 0, 100000 ).Select( value => (uint)value ).First( value => (NextDraw( value ) & 63) == 1 );
		var effects = new LobbyEffects( seed );
		var hallow = new LobbyEffects.Island( Island( "hallow", (4, 44, 12), rainy: true, lightning: 63 ), 400, 300 );
		var (thunders, _) = effects.Step( LobbyEffects.TickSeconds, new[] { hallow }, 400, 370 );
		Assert.AreEqual( 1, thunders );
		Assert.IsTrue( effects.Flash, "the strike flashes the screen for a frame" );
		var bolt = effects.Bolts.Single();
		Assert.AreEqual( 500f, bolt.Top.Z );
		Assert.AreEqual( 0f, bolt.Bottom.Z );
		Assert.IsTrue( bolt.Top.X >= 350 && bolt.Top.X < 450 && bolt.Top.Y >= 250 && bolt.Top.Y < 350, "the top within ±50 of the island" );
		Assert.IsTrue( System.Math.Abs( bolt.Bottom.X - bolt.Top.X ) <= 10 && System.Math.Abs( bolt.Bottom.Y - bolt.Top.Y ) <= 10, "the bottom within ±10 of the top" );
		Assert.AreEqual( LobbyEffects.RainCount( 70.0 * 70 ), effects.RainDrops, "the camera 70 units from the rainy island" );
		effects.Step( LobbyEffects.BoltSeconds, new[] { hallow }, 400, 370 );
		Assert.IsFalse( effects.Bolts.Any( candidate => candidate.Top == bolt.Top && candidate.Age >= LobbyEffects.BoltSeconds ), "a bolt lives 500 ms" );
	}

	[TestMethod]
	public void TheWeatherRendererReadsTheLobbyStrikesInOriginalAxes()
	{
		var seed = Enumerable.Range( 0, 100000 ).Select( value => (uint)value ).First( value => (NextDraw( value ) & 63) == 1 );
		var effects = new LobbyEffects( seed );
		var source = new LobbyWeatherSource( effects );
		var hallow = new LobbyEffects.Island( Island( "hallow", (4, 44, 12), rainy: true, lightning: 63 ), 400, 300 );
		effects.Step( LobbyEffects.TickSeconds, new[] { hallow }, 400, 370 );
		Assert.AreEqual( effects.RainDrops, source.DropCount );
		Assert.IsFalse( source.Snow );
		Assert.AreEqual( 0.5f, source.BoltLifetimeSeconds );
		var bolt = effects.Bolts.Single();
		var strike = source.TakeStrikes().Single();
		Assert.AreEqual( new System.Numerics.Vector3( bolt.Top.X, 500, bolt.Top.Y ), strike.Top, "the lobby's height is the original's Y" );
		Assert.AreEqual( new System.Numerics.Vector3( bolt.Bottom.X, 0, bolt.Bottom.Y ), strike.Bottom );
		Assert.AreEqual( 0, source.TakeStrikes().Count, "each strike is handed over once" );
	}

	[TestMethod]
	public void IslandOneShotsAreEventsThreeToFiveAboutOneTickInSixteen()
	{
		var effects = new LobbyEffects( 12345 );
		var jungle = new LobbyEffects.Island( Island( "jungle", (243, 203, 191) ), 0, 0 );
		var shots = Enumerable.Range( 0, 3200 ).SelectMany( _ => effects.Step( LobbyEffects.TickSeconds, new[] { jungle }, 0, 70 ).OneShots ).ToList();
		Assert.IsTrue( shots.All( sound => sound is 3 or 4 or 5 ) );
		Assert.IsTrue( shots.Count is > 120 and < 280, $"{shots.Count} one-shots in 3200 ticks" );
		Assert.AreEqual( 0, effects.RainDrops, "no rainy island" );
		Assert.AreEqual( 0, effects.Bolts.Count, "no lightning island" );
	}

	[TestMethod]
	public void TicksFollowWallClockTimeAtSixtyASecond()
	{
		// [EXT:frame-driven-at-60hz] the islands update runs once per rendered frame in the original; OpenTPW runs it per 60 Hz reference frame
		Assert.AreEqual( ReferenceFrameClock.FrameSeconds, LobbyEffects.TickSeconds );
		var effects = new LobbyEffects( 3 );
		var jungle = new LobbyEffects.Island( Island( "jungle", (0, 0, 0) ), 0, 0 );
		effects.Draw();
		var reference = new LobbyEffects( 3 );
		reference.Draw();
		effects.Step( 1f / 120f, new[] { jungle }, 0, 0 );
		Assert.AreEqual( reference.Draw(), effects.Draw(), "half a tick draws nothing" );
	}

	/// <summary>[BIN:TPW-EXE:0x57ee30] the flash quad is drawn before the bolts and the rain, so they stay bright on the inverted frame.</summary>
	[TestMethod]
	public void TheFlashQuadIsDrawnAfterTheEntitiesAndBeforeTheWeather()
	{
		var order = new System.Collections.Generic.List<string>();
		GameFlow.RenderLobbyLayers( new System.Action[] { () => order.Add( "a" ), () => order.Add( "b" ) }, () => order.Add( "weather" ), flash: true, () => order.Add( "flash" ) );
		CollectionAssert.AreEqual( new[] { "a", "b", "flash", "weather" }, order );

		order.Clear();
		GameFlow.RenderLobbyLayers( new System.Action[] { () => order.Add( "a" ) }, () => order.Add( "weather" ), flash: false, () => order.Add( "flash" ) );
		CollectionAssert.AreEqual( new[] { "a", "weather" }, order, "without a strike there is no quad" );

		order.Clear();
		GameFlow.RenderLobbyLayers( new System.Action[] { () => order.Add( "a" ) }, null, flash: true, () => order.Add( "flash" ) );
		CollectionAssert.AreEqual( new[] { "a", "flash" }, order, "a lobby without a weather renderer still flashes" );
	}
}

using System.Linq;
using OpenTPW.FrontEnd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LobbyEffectsTests
{
	private static LobbyIslandInfo Island( string level, (byte, byte, byte) sky, bool rainy = false, int lightning = 0 ) =>
		new( 0, level, level, "island", "gate", level, 0, 0, sky, System.Array.Empty<string>(), rainy, lightning );

	[TestMethod]
	public void TheGeneratorReturnsTheFullLcgState()
	{
		var effects = new LobbyEffects( 1 );
		Assert.AreEqual( unchecked(1u * 0x19660Du + 0x3C6EF35Fu), effects.Draw() );
		var next = unchecked((1u * 0x19660Du + 0x3C6EF35Fu) * 0x19660Du + 0x3C6EF35Fu);
		Assert.AreEqual( next, effects.Draw() );
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
	public void TheSkyEasesASixteenthOfTheWayEachTick()
	{
		var effects = new LobbyEffects( 7 );
		var dry = Island( "jungle", (0, 0, 0) );
		effects.Step( LobbyEffects.TickSeconds, new[] { new LobbyEffects.Island( dry, 0, 0 ) }, 0, 0 );
		Assert.IsTrue( effects.HasSky );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0), effects.Sky, "the first target is taken at once" );
		var bright = Island( "fantasy", (160, 160, 160) );
		effects.Step( LobbyEffects.TickSeconds, new[] { new LobbyEffects.Island( bright, 0, 0 ) }, 0, 0 );
		Assert.AreEqual( ((byte)10, (byte)10, (byte)10), effects.Sky, "1/16 of 160" );
		effects.StepGlobe( 1f );
		Assert.AreEqual( 0, effects.RainDrops );
		Assert.AreNotEqual( effects.Sky, ((byte)10, (byte)10, (byte)10), "the globe eases toward its own sky" );
	}

	[TestMethod]
	public void ALightningIslandStrikesWhenTheDrawMatchesAndPlacesTheBoltAroundIt()
	{
		// A seed whose first draw has (state & 63) == 1: hallow's LIGHTNING 63 strikes on the first tick.
		var seed = Enumerable.Range( 0, 100000 ).Select( value => (uint)value ).First( value => (unchecked(value * 0x19660Du + 0x3C6EF35Fu) & 63) == 1 );
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
		var seed = Enumerable.Range( 0, 100000 ).Select( value => (uint)value ).First( value => (unchecked(value * 0x19660Du + 0x3C6EF35Fu) & 63) == 1 );
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
	public void TicksFollowWallClockTimeAtThirtyASecond()
	{
		var effects = new LobbyEffects( 3 );
		var jungle = new LobbyEffects.Island( Island( "jungle", (0, 0, 0) ), 0, 0 );
		effects.Draw();
		var reference = new LobbyEffects( 3 );
		reference.Draw();
		effects.Step( 1f / 60f, new[] { jungle }, 0, 0 );
		Assert.AreEqual( reference.Draw(), effects.Draw(), "half a tick draws nothing" );
	}
}

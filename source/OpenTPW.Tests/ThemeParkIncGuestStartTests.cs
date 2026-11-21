using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public sealed class ThemeParkIncGuestStartTests
{
	// Independently captured from the constructor fragment and its real RNG by tpi_guest_profile_oracle.py.
	[DataTestMethod]
	[DataRow(1u, -1, 70, 20, 10, 2, 392, 377, 0, 88, 42, 532749348u, 1, 0, 50, 1, 38, 9, 0, 3741260791u)]
	[DataRow(7u, -1, 70, 20, 10, 14, 377, 516, 1, 76, 54, 3084350239u, 7, 100, 50, 3, 23, 19, 7, 3437138582u)]
	[DataRow(10u, -1, 0, 0, 100, 17, 409, 620, 2, 73, 57, 1501731059u, 10, 100, 50, 42, 20, 19, 8, 3618845337u)]
	[DataRow(7u, 2, 100, 0, 0, 22, 377, 658, 2, 68, 62, 3084350239u, 7, 100, 50, 3, 23, 19, 7, 3437138582u)]
	[DataRow(4u, 0, 0, 0, 100, 0, 394, 306, 0, 90, 40, 1414695650u, 4, 100, 50, 0, 35, 22, 3, 3560538661u)]
	[DataRow(1u, -1, 0, 0, 0, 18, 392, 732, 2, 72, 58, 532749348u, 1, 0, 50, 1, 38, 9, 0, 3741260791u)]
	[DataRow(4294967295u, -1, 0, 100, 0, 9, 374, 489, 1, 81, 49, 571828063u, 65535, 100, 50, 30, 48, 16, 7, 3411994105u)]
	[DataRow(18u, -1, 70, 20, 10, 1, 399, 310, 0, 89, 41, 1574526933u, 18, 0, 50, 31, 41, 12, 0, 3741587723u)]
	[DataRow(57u, -1, 70, 20, 10, 7, 399, 501, 0, 83, 47, 1504808787u, 57, 100, 50, 18, 1, 19, 4, 1638869035u)]
	[DataRow(6u, -1, 70, 20, 10, 10, 372, 485, 1, 80, 50, 4019090503u, 6, 100, 50, 5, 38, 0, 7, 2562612182u)]
	[DataRow(26u, -1, 70, 20, 10, 15, 412, 510, 1, 75, 55, 2435031094u, 26, 100, 50, 28, 9, 15, 7, 4056635123u)]
	[DataRow(31u, -1, 70, 20, 10, 23, 424, 744, 2, 67, 63, 1625880666u, 31, 100, 50, 7, 35, 20, 8, 2945021927u)]
	[DataRow(200u, -1, 70, 20, 10, 21, 419, 820, 2, 69, 61, 387299335u, 200, 100, 50, 44, 29, 12, 8, 2518391766u)]
	public void TheProfileMetersAndGeneratorMatchTheNativeConstructor( uint seed, int forced, int kids, int parents, int grandparents,
		int type, int exit, int money, int generation, int preferred, int boredom, uint profileState,
		int thingId, int prankChance, int happiness, int thirst, int hunger, int toilet, int prank, uint finalState )
	{
		var types = Enumerable.Range( 0, 24 ).Select( i => new ThemeParkIncGuestType( 90 - i, 300 + 20 * i, 40 + i, i / 8 ) ).ToArray();
		var world = new WorldRng( seed );
		var result = ThemeParkIncGuestStart.Read( types, 400, 30, 15, kids, parents, grandparents, world.NextWord, forced < 0 ? null : forced );
		Assert.AreEqual( new ThemeParkIncGuestProfile( type, exit, money, generation, preferred, boredom ), result );
		Assert.AreEqual( profileState, world.State );
		var meters = ThemeParkIncGuestStart.ReadMeters( result.Generation, prankChance, (ushort)thingId, world.NextWord );
		Assert.AreEqual( new ThemeParkIncStartingMeters( happiness, thirst, hunger, toilet, prank ), meters );
		Assert.AreEqual( finalState, world.State );
	}

	[DataTestMethod]
	[DataRow(0, 0, -1, 0, 0, 2600067959u)]
	[DataRow(1, 1, -1, 2, 6, 3412190274u)]
	[DataRow(65535, 2, -1, 1, 9, 1788154644u)]
	[DataRow(42, 1, 3, 3, 7, 42u)]
	[DataRow(190, 2, 0, 0, 8, 190u)]
	[DataRow(513, 0, 2, 2, 2, 513u)]
	public void AppearanceReseedsAndOnlyDrawsWhenNotForced( int thingId, int generation, int forced, int sprite, int appearance, uint finalState )
	{
		var world = new WorldRng( 0x12345678 );
		var actual = ThemeParkIncGuestStart.SelectAppearance( (ushort)thingId, generation, generation + 2, world, forced < 0 ? null : forced );
		Assert.AreEqual( (sprite, appearance), actual );
		Assert.AreEqual( finalState, world.State );
	}

	[DataTestMethod]
	[DataRow(60, 1064514355u, 57, 56, 56)]
	[DataRow(80, 1055286886u, 36, 35, 35)]
	[DataRow(100, 1063507722u, 89, 88, 88)]
	[DataRow(120, 1051931443u, 42, 41, 41)]
	[DataRow(140, 1051260355u, 46, 46, 46)]
	[DataRow(100, 1065353216u, 100, 100, 100)]
	public void GuestBaseSpeedMatchesNativeTruncationAtEachPrecision( int speed, uint multiplierBits, int single, int doublePrecision, int extended )
	{
		var multiplier = System.BitConverter.UInt32BitsToSingle( multiplierBits );
		Assert.AreEqual( single, (int)ThemeParkIncGuestStart.ScaleBaseSpeed( (ushort)speed, multiplier, singlePrecision: true ) );
		Assert.AreEqual( doublePrecision, (int)ThemeParkIncGuestStart.ScaleBaseSpeed( (ushort)speed, multiplier, singlePrecision: false ) );
		Assert.AreEqual( doublePrecision, extended );
	}

	[TestMethod]
	public void LiveTpiBirthUsesNativeProfileAndContinuesAcrossASave()
	{
		var park = MakePark( tpi: true );
		// The leading base-speed draw leaves state 7, matching the native profile case above.
		park.World.Reseed( 0x73826fe5 );
		var guest = park.SpawnInPark( 1, 1 );
		Assert.AreEqual( 1, guest.Id );
		Assert.AreEqual( (14, 377, 516), (guest.Type, guest.ExitLevel, guest.Money) );
		Assert.AreEqual( (50f, 3f, 23f, 19f), (guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet) );
		Assert.AreEqual( 8, guest.TpiPrankCode );
		Assert.AreEqual( 0, guest.PrankFlag, "A TPI code is not a TPW prank." );
		Assert.AreEqual( 3412190274u, park.World.State );
		Assert.AreEqual( 76, Preferred( park, guest ) );

		var bytes = ParkResumeJson.Serialize( park.CaptureSnapshot() );
		var saved = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( bytes )!;
		var resumed = MakePark( tpi: true );
		resumed.RestoreSnapshot( saved );
		Assert.AreEqual( park.ComputeStateHash(), resumed.ComputeStateHash() );
		Assert.AreEqual( 76, Preferred( resumed, resumed.Guests.Single() ) );
		park.SpawnInPark( 1, 1 );
		resumed.SpawnInPark( 1, 1 );
		Assert.AreEqual( park.ComputeStateHash(), resumed.ComputeStateHash() );
		Assert.AreEqual( park.World.State, resumed.World.State );

		Assert.ThrowsException<System.IO.InvalidDataException>( () => MakePark( tpi: false ).RestoreSnapshot( saved ) );
	}

	[TestMethod]
	public void LegacyTpiGuestUsesItsSavedStateAndOldPreferenceAlongsideNewBirths()
	{
		var old = MakePark( tpi: false );
		var legacy = old.SpawnInPark( 1, 1 );
		var bytes = ParkResumeJson.Serialize( old.CaptureSnapshot() );
		Assert.IsFalse( System.Text.Encoding.UTF8.GetString( bytes ).Contains( nameof( GuestSnapshot.TpiPrankCode ) ) );
		var resumed = MakePark( tpi: true );
		resumed.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( bytes )! );
		var restored = resumed.Guests.Single();
		Assert.IsNull( restored.TpiPrankCode );
		Assert.AreEqual( legacy.Money, restored.Money );
		Assert.AreEqual( old.World.State, resumed.World.State );
		Assert.AreEqual( 50, Preferred( resumed, restored ) );
		resumed.World.Reseed( 0x73826fe5 );
		Assert.AreEqual( 76, Preferred( resumed, resumed.SpawnInPark( 1, 1 ) ) );
		Assert.AreEqual( 50, Preferred( resumed, restored ) );
	}

	private static int Preferred( GuestSimulation park, Guest guest ) =>
		OriginalGuestDecisions.ScoreInputOf( new DecisionContext( park.Settings, 0, false ), park.ViewOf( guest ), default ).PreferredExcitement;

	[TestMethod]
	public void ALiveTpiGuestsBaseSpeedIsTheDrawnWordTimesItsProfilesMultiplier()
	{
		// [BIN:TPI-EXE:0x608FA4 base speed draw] [BIN:TPI-EXE:0x60C712 guest base speed] the first draw picks the word, the profile's SpeedMultiplier scales it, truncated
		var park = MakePark( tpi: true, speedMultiplier: 0.41f );
		park.World.Reseed( 0x73826fe5 );
		var replay = new WorldRng( 0x73826fe5 );
		var word = GuestSpeed.BaseSpeeds[(int)replay.Next( (uint)GuestSpeed.BaseSpeeds.Length )];
		var guest = park.SpawnInPark( 1, 1 );
		Assert.AreEqual( (int)(word * (double)0.41f), guest.BaseSpeed );
		Assert.AreNotEqual( word, guest.BaseSpeed, "the multiplier applies" );
		var tpw = MakePark( tpi: false ).SpawnInPark( 1, 1 ).BaseSpeed;
		Assert.IsTrue( GuestSpeed.BaseSpeeds.ToArray().Any( value => value == tpw ), "a Theme Park World guest keeps a table word" );
	}

	private static GuestSimulation MakePark( bool tpi, float speedMultiplier = 1f )
	{
		var grid = new GuestPathGrid( 4, 4 );
		grid.SetPath( 1, 1, true );
		var settings = new GuestSettings();
		if ( tpi )
			settings.TpiGuestStart = new( Enumerable.Range( 0, 24 ).Select( i => new ThemeParkIncGuestType( 90 - i, 300 + 20 * i, 40 + i, i / 8, speedMultiplier ) ).ToArray(),
				400, 30, 15, 70, 20, 10, 100 );
		return new GuestSimulation( grid, settings, 1 );
	}
}

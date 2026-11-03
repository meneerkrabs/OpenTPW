using System;
using System.Buffers.Binary;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>ARRIVAL-008: Theme Park Inc's attraction update closes a ride in a spell its ClosedDuring* word names and opens it again when the spell ends (0x5DED90).</summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncWeatherClosingTests
{
	[TestMethod]
	public void ASpellTheTypeNamesClosesTheAttractionAndTheEndOfItOpensItAgain()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-weather-closing-" ).FullName;
		var previousEntities = Entity.All;
		Entity.All = new();
		try
		{
			var bytes = Md2ModelFileTests.CreateGeometry();
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 );
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
			foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
			File.WriteAllBytes( Path.Combine( root, "model.MD2" ), bytes );
			File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
			// [BIN:TPI-EXE:0x5DEE1F weather closing] ClosedDuringRain (type word +0x104) is 1; the other spells are not named
			var runtime = new OriginalObjectRuntime( CheatsRideVehicleTests.SyntheticRide( root, 1100, "rain", "WeatherModifier.ClosedDuringRain 1\n" ), seed: 1, open: true );
			Assert.IsTrue( runtime.IsOpen );
			runtime.ApplyWeatherClosing( 0 );
			runtime.ApplyWeatherClosing( 1 );
			Assert.IsTrue( runtime.IsOpen, "a heat wave (1) is not named" );
			runtime.ApplyWeatherClosing( 2 );
			Assert.IsFalse( runtime.IsOpen, "rain (2) closes it" );
			Assert.IsTrue( runtime.ClosedDueToWeather );
			runtime.ApplyWeatherClosing( 2 );
			Assert.IsFalse( runtime.IsOpen );
			runtime.ApplyWeatherClosing( 3 );
			Assert.IsTrue( runtime.IsOpen, "the first update in another weather opens it again" );
			Assert.IsFalse( runtime.ClosedDueToWeather );
			runtime.Close();
			runtime.ApplyWeatherClosing( 0 );
			Assert.IsFalse( runtime.IsOpen, "a ride the owner closed is left alone when the flag is clear" );
		}
		finally
		{
			foreach ( var item in Entity.All.ToArray() ) item.Delete();
			Entity.All = previousEntities;
			Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void ARideBuiltMidSpellStaysOpenUntilTheNextSpellStarts()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-weather-closing-" ).FullName;
		var previousEntities = Entity.All;
		Entity.All = new();
		try
		{
			var bytes = Md2ModelFileTests.CreateGeometry();
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 );
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
			foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
			File.WriteAllBytes( Path.Combine( root, "model.MD2" ), bytes );
			File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
			// [BIN:TPI-EXE:0x641FF3 weather message] the attractions are asked once per spell start (Level.SyncObjectEconomy reads SpellStarts)
			var weather = new ThemeParkIncWeatherSpells( new ThemeParkIncWeatherSettings( new[] { 0, 100, 0, 0, 0, 100, 0, 0, 0, 100, 0, 0, 0, 100, 0, 0 }, new[] { 4, 0, 0 }, 0, (40, 95), (95, 100), (0, 20), (0, 40), (0, 15) ) );
			var seen = 0;
			void Tick( OriginalObjectRuntime? ride )
			{
				if ( weather.SpellStarts == seen ) return;
				seen = weather.SpellStarts;
				ride?.ApplyWeatherClosing( weather.State );
			}
			var months = 0;
			void Month() { weather.OnMonth( months % 12 + 1, months / 12, bound => bound == 100 ? 0 : 0 ); months++; }
			// run to the first rain spell (a draw of 0 takes the first non-zero chance, rain, in the third month of a quarter from year 0)
			while ( weather.State != 2 && months < 36 ) { Month(); Tick( null ); }
			Assert.AreEqual( 2, weather.State, "a rain spell is running" );
			var starts = weather.SpellStarts;
			var ride = new OriginalObjectRuntime( CheatsRideVehicleTests.SyntheticRide( root, 1100, "rain", "WeatherModifier.ClosedDuringRain 1\n" ), seed: 1, open: true );
			for ( var turn = 0; turn < 50; turn++ ) Tick( ride );
			Assert.IsTrue( ride.IsOpen, "built mid-spell, it stays open while no spell starts" );
			Assert.AreEqual( starts, weather.SpellStarts );
			while ( weather.SpellStarts == starts && months < 96 ) { Month(); Tick( ride ); }
			Assert.AreNotEqual( starts, weather.SpellStarts, "the next spell starts" );
			Assert.IsTrue( weather.State != 2 || !ride.IsOpen );
			Assert.AreEqual( weather.State == 2, ride.ClosedDueToWeather );
		}
		finally
		{
			foreach ( var item in Entity.All.ToArray() ) item.Delete();
			Entity.All = previousEntities;
			Directory.Delete( root, true );
		}
	}
}

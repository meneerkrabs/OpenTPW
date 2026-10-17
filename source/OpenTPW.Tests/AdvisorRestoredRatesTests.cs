using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// How often the restored advice speaks in a park whose conditions hold all the time (the worst case for nagging), against the original's own persistent line, over one real hour at normal speed
/// (14516 turns of 248 ms). The group repeat intervals are the shipped file's (120, 1800, 900 and 600 groups of four turns).
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorRestoredRatesTests
{
	public const int TurnsPerRealHour = 14516;

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static string Sam => AdvisorRestoredAdviceTests.SamText
		.Replace( "MessageGroups[0].MinTimeSameMessage		10", "MessageGroups[0].MinTimeSameMessage		120" )
		.Replace( "MessageGroups[3].MinTimeSameMessage		30", "MessageGroups[3].MinTimeSameMessage		1800" )
		.Replace( "MessageGroups[4].MinTimeSameMessage		20", "MessageGroups[4].MinTimeSameMessage		900" )
		.Replace( "MessageGroups[5].MinTimeSameMessage		15", "MessageGroups[5].MinTimeSameMessage		600" ) + "\nTakeLoan.Score 71\n";

	/// <summary>Lines played per message over <paramref name="turns"/> turns.</summary>
	internal static Dictionary<int, int> Run( int turns, IRestoredAdviceState state, bool arrivals )
	{
		var controller = new AdvisorController( AdvisorBalance.Parse( System.Text.Encoding.ASCII.GetBytes( Sam ), "rates.sam" ) );
		var money = new AdvisorRestoredAdviceTests.MoneyFake { Balance = -1, LoanAvailable = true };
		uint tick = 0;
		var driver = new RestoredAdviceDriver( controller, () => tick, () => 0, () => true );
		var plays = new Dictionary<int, int>();
		var rows = AdvisorDescriptorTable.Rows;
		for ( tick = 1; tick <= turns; tick++ )
		{
			controller.ScanOnce( money, tick, 0, true, state );
			driver.Poll( state );
			if ( arrivals )
				driver.ObserveVehicle( tick % 240 < 100 ? ArrivalVehicleKind.Bus : -1 );
			var now = tick;
			controller.Update( () => now * 248u, () => now, response =>
			{
				var message = rows.First( row => response >= row.FirstResponseId && response < row.FirstResponseId + row.ResponseCount ).MessageId;
				plays[message] = plays.GetValueOrDefault( message ) + 1;
				return 8000u;
			} );
		}
		return plays;
	}

	[TestMethod]
	public void MeasureTheRatesOfAParkWhoseConditionsAllHold()
	{
		var state = AdvisorRestoredAdviceTests.TypicalState();
		var plays = Run( TurnsPerRealHour, state, arrivals: true );
		var restored = plays.Where( pair => RestoredAdvice.IsRestored( pair.Key ) ).ToDictionary( pair => pair.Key, pair => pair.Value );
		Console.WriteLine( $"RATES restored {restored.Values.Sum()} {string.Join( ",", restored.OrderBy( pair => pair.Key ).Select( pair => $"{pair.Key}x{pair.Value}" ) )}; originals {plays.Where( pair => !RestoredAdvice.IsRestored( pair.Key ) ).Sum( pair => pair.Value )}" );
		// before the repeat rules: 144 restored lines against 24 original ones in the busy park, and 24 plays of 25 in the park without security
		Assert.IsTrue( restored.Values.Sum() <= 8, "the restored lines do not dominate the hour" );
		Assert.IsTrue( restored.Values.Sum() * 2 <= plays.Where( pair => !RestoredAdvice.IsRestored( pair.Key ) ).Sum( pair => pair.Value ), "the originals keep the floor" );
		var bare = Run( TurnsPerRealHour, AdvisorRestoredAdviceTests.NoSecurityState(), arrivals: false );
		Assert.IsTrue( bare.GetValueOrDefault( 25 ) <= 1, "no security is said once an hour or less" );
		Assert.IsTrue( bare.GetValueOrDefault( 102 ) >= 20, "and the original's line keeps its rate" );
		Console.WriteLine( $"RATES nosecurity restored {bare.Where( pair => RestoredAdvice.IsRestored( pair.Key ) ).Sum( pair => pair.Value )} {string.Join( ",", bare.OrderBy( pair => pair.Key ).Select( pair => $"{pair.Key}x{pair.Value}" ) )}" );
	}
}

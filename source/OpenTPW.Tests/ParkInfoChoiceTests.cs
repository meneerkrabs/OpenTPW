using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The park information window's series mask and period (tp.exe 0x753C48, 0x7CB39C) outlive a park: they live on one host object the game flow shares.</summary>
[TestClass]
public class ParkInfoChoiceTests
{
	private static ParkHud HudOn( HudHost host )
	{
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		typeof( ParkHud ).GetField( "host", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( hud, host );
		return hud;
	}

	[TestMethod]
	public void TheChoiceCarriesToTheNextParkOfTheSameHostAndAFreshHostStartsOverAtTheDefaults()
	{
		var shared = new ParkInfoChoice();
		var first = HudOn( new HudHost { ParkInfoChoice = shared } );
		Assert.AreEqual( ParkInfoGraph.InitialSeries, first.ParkInfoSelectedSeries );
		Assert.AreEqual( ParkInfoGraph.InitialPeriod, first.ParkInfoPeriod );
		first.SetParkInfoSeries( ParkInfoSeries.Arrivals, true );
		first.SetParkInfoPeriod( 2 );
		var second = HudOn( new HudHost { ParkInfoChoice = shared } );
		Assert.IsTrue( second.ParkInfoSelectedSeries.HasFlag( ParkInfoSeries.Arrivals ), "the next park's window keeps the toggles" );
		Assert.AreEqual( 2, second.ParkInfoPeriod, "and the period" );
		var other = HudOn( new HudHost { ParkInfoChoice = new ParkInfoChoice() } );
		Assert.AreEqual( ParkInfoGraph.InitialSeries, other.ParkInfoSelectedSeries );
		Assert.AreEqual( ParkInfoGraph.InitialPeriod, other.ParkInfoPeriod );
	}

	[TestMethod]
	public void TheGameFlowHandsEveryParkTheSameChoiceObject()
	{
		// source-text contract, CRLF-safe: one readonly field of the game flow goes into every host it builds, so no park starts a fresh choice
		var path = "";
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null && path.Length == 0; directory = directory.Parent )
			if ( File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW", "Client", "GameFlow.cs" ) ) )
				path = Path.Combine( directory.FullName, "source", "OpenTPW", "Client", "GameFlow.cs" );
		if ( path.Length == 0 )
			Assert.Inconclusive( "the source tree is not next to the test binaries" );
		var text = File.ReadAllText( path ).Replace( "\r\n", "\n" );
		StringAssert.Contains( text, "private readonly ParkInfoChoice parkInfoChoice = new();" );
		Assert.AreEqual( 1, System.Text.RegularExpressions.Regex.Matches( text, @"ParkInfoChoice = parkInfoChoice," ).Count );
		Assert.IsFalse( text.Contains( "ParkInfoChoice = new" ), "no host takes a fresh choice" );
	}
}

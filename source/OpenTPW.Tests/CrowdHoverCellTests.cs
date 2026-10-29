using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The crowd sentence's guest count takes the hover cell 0x7B05CC (tp.exe 0x54f875), not the camera's target.</summary>
[TestClass, DoNotParallelize]
public class CrowdHoverCellTests
{
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );

	private static int Count( Level level ) => (int)typeof( GameFlow ).GetMethod( "GuestsNearCamera", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { level } )!;

	private static Level LevelWith( params (int X, int Y)[] cells )
	{
		var simulation = new GuestSimulation( new GuestPathGrid( 32, 32 ), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		var guests = new List<Guest>();
		foreach ( var (x, y) in cells )
			guests.Add( new Guest( guests.Count + 1, 0 ) { State = GuestState.WalkingAround, CellX = x, CellY = y } );
		typeof( GuestSimulation ).GetField( "guests", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( simulation, guests );
		var level = Empty<Level>();
		typeof( Level ).GetField( "<Guests>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, simulation );
		return level;
	}

	[TestMethod]
	public void TheCountIsTheSquareOfFourCellsAroundTheHoverCell()
	{
		var level = LevelWith( (0, 0), (4, 4), (5, 4), (4, 5), (20, 20) );
		// the cursor object's constructor sets the word to 1, cell (0, 0) (0x46c0b0), the property's initial value (an uninitialised test level skips it)
		typeof( Level ).GetProperty( "HoverCell", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, ((int, int)?)(0, 0) );
		Assert.AreEqual( 2, Count( level ), "(0, 0) and (4, 4) lie in the square [0, 4] x [0, 4]; the Chebyshev radius is 4 (0x4c8d30)" );
		typeof( Level ).GetProperty( "HoverCell", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, ((int, int)?)(20, 20) );
		Assert.AreEqual( 1, Count( level ) );
		typeof( Level ).GetProperty( "HoverCell", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, ((int, int)?)(5, 5) );
		Assert.AreEqual( 3, Count( level ) );
	}

	[TestMethod]
	public void ACursorRayOffTheMapCountsNobody()
	{
		var level = LevelWith( (0, 0), (1, 1) );
		typeof( Level ).GetProperty( "HoverCell", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, null );
		Assert.AreEqual( 0, Count( level ), "the word is 0 and the counter's rectangle is empty (0x45d601, 0x4c8d30)" );
	}
}

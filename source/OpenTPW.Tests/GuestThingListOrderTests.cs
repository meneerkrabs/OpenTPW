using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The order the renderers walk the guests in: the original's live thing list, the newest thing first (tp.exe 0x00518F90).</summary>
[TestClass]
public class GuestThingListOrderTests
{
	[TestMethod]
	public void TheGuestsComeNewestFirst()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var first = simulation.SpawnInPark( 2, 5 );
		var second = simulation.SpawnInPark( 2, 5 );
		var third = simulation.SpawnInPark( 2, 5 );
		var order = new List<Guest>();
		simulation.FillInThingListOrder( order );
		CollectionAssert.AreEqual( new[] { third, second, first }, order );
	}
}

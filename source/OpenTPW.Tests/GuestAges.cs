using System;
namespace OpenTPW.Tests;

/// <summary>Sets a guest's age the way the simulation keeps it (UMB-003): the seconds in the park and the integer turn count the original's day gate subtracts (world turn minus creation stamp).</summary>
internal static class GuestAges
{
	public static void Set( Guest guest, double seconds )
	{
		guest.TimeInParkSeconds = seconds;
		guest.DisplayTimeInParkTurns = (long)Math.Floor( seconds * 1000.0 / ParkCalendar.TurnMilliseconds );
	}
}

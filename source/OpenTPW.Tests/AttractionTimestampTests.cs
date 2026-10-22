using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The eight time-stamp words <c>tv[t]</c> of an attraction record, read by the Theme Park World and the Theme Park Inc reader on synthetic bodies. The serializer
/// writes the fields of FileTimeToSystemTime on the thing's park time: year, month, day, day of the week, hour, minute, second and milliseconds (tp.exe 0x4DB8EE,
/// Theme Park Inc 0x5DC56E). The loader rebuilds the time with SystemTimeToFileTime from every word but the day of the week (tp.exe 0x4DC8A1, Theme Park Inc 0x5DD68E).
/// </summary>
[TestClass]
public class AttractionTimestampTests
{
	/// <summary>Sunday 2000-02-20 17:42:30.</summary>
	private static readonly uint[] Sunday = { 2000, 2, 20, 0, 17, 42, 30, 0 };

	private static SaveTimestamp WorldStamp( uint[] tv ) =>
		SaveAttraction.Read( SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { Stamp = tv } ), 0, 1 ).Timestamp;

	private static SaveTimestamp IncStamp( uint[] tv ) =>
		ThemeParkIncAttraction.Read( ThemeParkIncAttractionHistoryTests.Body( 100f, 100f, 100f, null, tv ), 0, 14 ).Timestamp;

	[TestMethod]
	public void TheWordsAreTheSystemTimeFieldsWithTheDayOfTheWeekFourth()
	{
		var read = WorldStamp( Sunday );
		Assert.AreEqual( new SaveTimestamp( 2000, 2, 20, 0, 17, 42, 30, 0 ), read );
		Assert.AreEqual( (17, 42, 30), (read.Hour, read.Minute, read.Second), "the fifth to seventh words are the time of day; the fourth, 0, is Sunday" );
		Assert.AreEqual( new DateTime( 2000, 2, 20, 17, 42, 30, DateTimeKind.Utc ), read.ToDateTime() );
		Assert.AreEqual( read, IncStamp( Sunday ) );
		var last = new uint[] { 2000, 2, 20, 0, 23, 59, 59, 999 };
		Assert.AreEqual( new SaveTimestamp( 2000, 2, 20, 0, 23, 59, 59, 999 ), WorldStamp( last ), "the eighth word is the milliseconds" );
		Assert.AreEqual( new SaveTimestamp( 2000, 2, 20, 0, 23, 59, 59, 999 ), IncStamp( last ) );
	}

	[TestMethod]
	public void TheDayOfTheWeekIsKeptAsReadBecauseTheLoaderSkipsIt()
	{
		foreach ( var day in new uint[] { 6, 7, 24, uint.MaxValue } )
		{
			var tv = new uint[] { 2000, 2, 20, day, 17, 42, 30, 0 };
			Assert.AreEqual( new SaveTimestamp( 2000, 2, 20, day, 17, 42, 30, 0 ), WorldStamp( tv ), $"day word {day}" );
			Assert.AreEqual( new SaveTimestamp( 2000, 2, 20, day, 17, 42, 30, 0 ), IncStamp( tv ), $"day word {day}" );
		}
	}

	[TestMethod]
	public void AWordTheLoaderConvertsOutsideTheCalendarOrTheClockIsRefused()
	{
		foreach ( var (index, value) in new (int Index, uint Value)[] { (0, 10_000), (1, 0), (1, 13), (2, 0), (2, 32), (4, 24), (5, 60), (6, 60), (7, 1000) } )
		{
			var tv = (uint[])Sunday.Clone();
			tv[index] = value;
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => WorldStamp( tv ) ).Message, "game-time stamp", $"tv[{index}] = {value}" );
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => IncStamp( tv ) ).Message, "game-time stamp", $"tv[{index}] = {value}" );
		}
	}
}

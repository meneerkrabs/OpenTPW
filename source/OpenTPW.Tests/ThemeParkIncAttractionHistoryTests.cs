using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The meaning of the Theme Park Inc attraction's history rings and gauges (SAVE-940): the ring table against the in-memory layout of the history object (0x5ea020, allocated with
/// 0x14c8 bytes by 0x5db5b9), the order of the three gauge words, and the way the game reads a ring back. Synthetic bodies only: no Theme Park Inc save holds a ring with entries
/// (the prebuilt's are all empty).
/// </summary>
[TestClass]
public class ThemeParkIncAttractionHistoryTests
{
	/// <summary>
	/// The attraction body of the serializer 0x5dc430 in load order, 312 bytes before any history; <paramref name="stamp"/> is the eight words <c>tv[t]</c>
	/// (year, month, day, day of the week, hour, minute, second, milliseconds), by default Tuesday 2000-01-04 09:30.
	/// </summary>
	internal static byte[] Body( float unread, float life, float repair, IReadOnlyList<(int Current, bool Wrapped, int Running, int[] Entries)>? rings, uint[]? stamp = null )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( (ushort)(5 * 256 + 128) ); writer.Write( (ushort)(7 * 256 + 128) ); writer.Write( (ushort)0 ); writer.Write( (ushort)0 );
		writer.Write( 90u );
		writer.Write( (ushort)1001 );
		foreach ( var word in stamp ?? new uint[] { 2000, 1, 4, 2, 9, 30, 0, 0 } )
			writer.Write( word );
		writer.Write( 77u );
		writer.Write( (ushort)0 );
		for ( var index = 0; index < ThemeParkIncAttraction.NameCharacters; index++ )
		{
			writer.Write( (ushort)(index == 0 ? 'A' : 0) );
			writer.Write( (ushort)0 );
		}
		writer.Write( 0u ); writer.Write( 0u ); writer.Write( 2u );
		for ( var index = 0; index < 5; index++ )
			writer.Write( (ushort)0 );
		writer.Write( 0u );
		writer.Write( (ushort)0 ); writer.Write( (ushort)0 );
		writer.Write( 0u );
		writer.Write( (ushort)0 );
		writer.Write( 25 ); writer.Write( 3u );
		writer.Write( (byte)5 ); writer.Write( (byte)6 );
		writer.Write( 0u );
		writer.Write( (ushort)0 );
		writer.Write( 10 ); writer.Write( 50 ); writer.Write( 0 ); writer.Write( 100u ); writer.Write( 50 ); writer.Write( 50 );
		writer.Write( (byte)0 );
		writer.Write( 0u );
		writer.Write( unread ); writer.Write( life ); writer.Write( repair );
		writer.Write( 0u ); writer.Write( 0u ); writer.Write( 0u );
		writer.Write( (byte)0 );
		writer.Write( (ushort)0 ); writer.Write( (ushort)0 );
		writer.Write( (byte)0 ); writer.Write( (byte)0 ); writer.Write( (byte)0 );
		writer.Write( (ushort)0 );
		writer.Write( 0L );
		writer.Write( (byte)0 ); writer.Write( (byte)0 ); writer.Write( (byte)0 ); writer.Write( (byte)0 );
		writer.Write( (byte)(rings != null ? 1 : 0) );
		if ( rings != null )
		{
			foreach ( var (ring, info) in rings.Select( ( ring, index ) => (ring, ThemeParkIncAttraction.HistoryRings[index]) ) )
			{
				writer.Write( ring.Current );
				writer.Write( info.Capacity );
				writer.Write( ring.Wrapped ? (byte)1 : (byte)0 );
				writer.Write( ring.Running );
				for ( var index = 0; index < info.Capacity; index++ )
					writer.Write( index < ring.Entries.Length ? ring.Entries[index] : 0 );
			}
		}
		return stream.ToArray();
	}

	private static (int Current, bool Wrapped, int Running, int[] Entries)[] EmptyRings() =>
		ThemeParkIncAttraction.HistoryRings.Select( info => (-1, false, 0, Array.Empty<int>()) ).ToArray();

	[TestMethod]
	public void RingTableMatchesTheHistoryObjectLayout()
	{
		var rings = ThemeParkIncAttraction.HistoryRings;
		Assert.AreEqual( ThemeParkIncAttraction.ShortRings + ThemeParkIncAttraction.LongRings, rings.Count );
		for ( var index = 0; index < rings.Count; index++ )
			Assert.AreEqual( (ThemeParkIncAttractionRing)index, rings[index].Ring );
		// a daily ring is 0x88 bytes in memory (temp, 30 entries, current entry, entry count, wrapped flag), a monthly one 0x250 (temp, 144 entries, three words)
		var offset = 0;
		foreach ( var info in rings )
		{
			Assert.AreEqual( offset, info.MemoryOffset, info.Ring.ToString() );
			Assert.AreEqual( info.Period == ThemeParkIncHistoryPeriod.Day ? ThemeParkIncAttraction.ShortRingEntries : ThemeParkIncAttraction.LongRingEntries, info.Capacity );
			offset += 4 + 4 * info.Capacity + 12;
			Assert.IsFalse( string.IsNullOrWhiteSpace( info.Evidence ) );
		}
		// the history object is allocated with 0x14c8 bytes (0x5db5b9)
		Assert.AreEqual( 0x14c8, offset );
		Assert.AreEqual( 13, rings.Count( info => info.Period == ThemeParkIncHistoryPeriod.Day ) );
		Assert.AreEqual( 6, rings.Count( info => info.Period == ThemeParkIncHistoryPeriod.Month ) );
		Assert.AreEqual( 0x14c8 - 4 - 4 * 144 - 12, rings[^1].MemoryOffset );
	}

	[TestMethod]
	public void TheThreeGaugeWordsAreTheUnreadGaugeTheLifeGaugeAndTheStateOfRepair()
	{
		var attraction = ThemeParkIncAttraction.Read( Body( 7f, 33f, 250f, null ), 0, 14 );
		Assert.AreEqual( ThemeParkIncAttraction.BodyBytes, attraction.Length );
		Assert.AreEqual( 7f, attraction.UnreadGauge );
		Assert.AreEqual( 33f, attraction.LifeGauge );
		Assert.AreEqual( 250f, attraction.StateOfRepair );
		Assert.IsFalse( attraction.HasHistory );
		Assert.IsNull( attraction.Ring( ThemeParkIncAttractionRing.DailyTakings ) );
	}

	[TestMethod]
	public void GaugesOutsideTheByteRangeTheOriginalSavesAreRefused()
	{
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncAttraction.Read( Body( 256f, 1f, 1f, null ), 0, 14 ) );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncAttraction.Read( Body( 1f, float.NaN, 1f, null ), 0, 14 ) );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncAttraction.Read( Body( 1f, 1f, -1f, null ), 0, 14 ) );
	}

	[TestMethod]
	public void EveryRingIsReachableByItsName()
	{
		var rings = EmptyRings();
		for ( var index = 0; index < rings.Length; index++ )
			rings[index] = (0, false, 1000 + index, new[] { 100 + index });
		var attraction = ThemeParkIncAttraction.Read( Body( 100f, 100f, 100f, rings ), 0, 14 );
		Assert.AreEqual( ThemeParkIncAttraction.BodyBytes + ThemeParkIncAttraction.HistoryBytes, attraction.Length );
		foreach ( var ring in Enum.GetValues<ThemeParkIncAttractionRing>() )
		{
			var read = attraction.Ring( ring )!;
			Assert.AreEqual( 1000 + (int)ring, read.Current, ring.ToString() );
			Assert.AreEqual( 100 + (int)ring, read.Entries[0], ring.ToString() );
		}
		Assert.AreEqual( ThemeParkIncAttraction.HistoryRings[(int)ThemeParkIncAttractionRing.MonthlyProfit].Capacity, attraction.Ring( ThemeParkIncAttractionRing.MonthlyProfit )!.Entries.Count );
	}

	/// <summary>The game's reader of a ring (0x5eb090, count 0x5eb2f0): the entry n periods ago is the one n before the current entry, wrapping once the ring has been full.</summary>
	[TestMethod]
	public void RingsReadBackNewestFirstAsTheOriginalDoes()
	{
		var empty = new SaveAttractionHistory( -1, false, 0, new int[30] );
		Assert.AreEqual( 0, ThemeParkIncAttraction.NewestFirst( empty ).Count );

		var entries = Enumerable.Range( 0, 30 ).Select( index => index * 10 ).ToArray();
		var partial = new SaveAttractionHistory( 2, false, 0, entries );
		CollectionAssert.AreEqual( new[] { 20, 10, 0 }, ThemeParkIncAttraction.NewestFirst( partial ).ToArray() );

		var wrapped = new SaveAttractionHistory( 2, true, 0, entries );
		var read = ThemeParkIncAttraction.NewestFirst( wrapped );
		Assert.AreEqual( 30, read.Count );
		CollectionAssert.AreEqual( new[] { 20, 10, 0, 290, 280 }, read.Take( 5 ).ToArray() );
		Assert.AreEqual( 30, read.Distinct().Count() );
	}

	[TestMethod]
	public void TheHistoryRingsOfAnAttractionWithoutHistoryAreAbsentNotZero()
	{
		var attraction = ThemeParkIncAttraction.Read( Body( 100f, 100f, 100f, null ), 0, 14 );
		Assert.AreEqual( 0, attraction.Histories.Count );
		foreach ( var ring in Enum.GetValues<ThemeParkIncAttractionRing>() )
			Assert.IsNull( attraction.Ring( ring ) );
	}
}

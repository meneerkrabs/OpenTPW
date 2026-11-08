using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Replays the isolated native merge <c>0x58EAE0</c> (ORACLE.md X92): the emission order and the returned area of every synthetic case.
/// The fixture holds native results only, generated independently of <see cref="OriginalPageListMerge"/>.
/// </summary>
[TestClass]
public class UiPageMergeGoldenTests
{
	private sealed record Case( string Name, int Device, List<Record> Existing, List<Record> Pending, Expected Expected );

	private sealed record Record( uint Id, uint Flags, short U, short V, short Width, short Height );

	private sealed record Expected( uint[] Order, int Area );

	private static List<Case> Cases()
	{
		using var packed = new MemoryStream( Convert.FromBase64String( UiPageMergeGolden.Packed ) );
		using var inflated = new ZLibStream( packed, CompressionMode.Decompress );
		using var document = JsonDocument.Parse( inflated );
		var root = document.RootElement.Clone();
		return root.EnumerateArray().Select( row => new Case(
			row.GetProperty( "name" ).GetString()!,
			row.GetProperty( "device" ).GetInt32(),
			Records( row.GetProperty( "existing" ) ),
			Records( row.GetProperty( "pending" ) ),
			new Expected(
				row.GetProperty( "expected" ).GetProperty( "order" ).EnumerateArray().Select( value => value.GetUInt32() ).ToArray(),
				row.GetProperty( "expected" ).GetProperty( "area" ).GetInt32() ) ) ).ToList();
	}

	private static List<Record> Records( JsonElement rows ) => rows.EnumerateArray().Select( row => new Record(
		row.GetProperty( "id" ).GetUInt32(), row.GetProperty( "flags" ).GetUInt32(), (short)row.GetProperty( "u" ).GetInt32(),
		(short)row.GetProperty( "v" ).GetInt32(), (short)row.GetProperty( "width" ).GetInt32(),
		(short)row.GetProperty( "height" ).GetInt32() ) ).ToList();

	private static PageTextureNode? Chain( List<Record> records )
	{
		PageTextureNode? head = null;
		for ( var index = records.Count - 1; index >= 0; index-- )
		{
			var record = records[index];
			head = new PageTextureNode
			{
				Id = record.Id, Flags = record.Flags, U = record.U, V = record.V,
				Width = record.Width, Height = record.Height, Next = head,
			};
		}
		return head;
	}

	[TestMethod]
	public void EveryNativeMergeCaseIsReproduced()
	{
		Assert.AreEqual( UiPageMergeGolden.RowCount, Cases().Count, "the fixture row count" );
		foreach ( var test in Cases() )
		{
			var (order, area) = OriginalPageListMerge.Merge( Chain( test.Existing ), Chain( test.Pending ), test.Device );
			CollectionAssert.AreEqual( test.Expected.Order, order.Select( node => node.Id ).ToArray(), $"{test.Name}: emission order" );
			Assert.AreEqual( test.Expected.Area, area, $"{test.Name}: returned area" );
		}
	}

	[TestMethod]
	public void TheFixtureCoversTheMergeBoundaries()
	{
		var cases = Cases();
		Assert.IsTrue( cases.Any( c => c.Existing.Count == 0 && c.Pending.Count == 0 ), "both lists empty" );
		Assert.IsTrue( cases.Any( c => c.Existing.Count == 0 && c.Pending.Count > 0 ), "pending only" );
		Assert.IsTrue( cases.Any( c => c.Existing.Count > 0 && c.Pending.Count == 0 ), "existing only" );
		Assert.IsTrue( cases.Any( c => c.Existing.Any( r => (r.Flags & OriginalPageListMerge.ExistingOnly) == 0 )
			&& c.Expected.Order.Length < c.Existing.Count ), "existing records filtered without the flag" );
		Assert.IsTrue( cases.Any( c => c.Expected.Order.Length > 0 && c.Expected.Order.Length < c.Existing.Count + c.Pending.Count ),
			"some records filtered out" );
		Assert.IsTrue( cases.Any( c => c.Device == 1 && c.Existing.Count > 0 && (c.Existing[0].Flags & 0x30000) != 0
			&& !c.Expected.Order.Contains( c.Existing[0].Id ) ), "a device dependent leading existing record is skipped" );
		Assert.IsTrue( cases.Any( c => c.Pending.Any( r => r.V < 0 ) || c.Existing.Any( r => r.V < 0 ) ), "negative ordering words" );
		Assert.IsTrue( cases.Any( c => c.Expected.Order.Length > 1 ), "more than one emitted record" );
	}

	[TestMethod]
	public void FullTiesEmitTheExistingRecordFirstAndTheAreaWraps()
	{
		var existing = new PageTextureNode { Id = 1, Flags = OriginalPageListMerge.ExistingOnly, U = 1, V = 2, Width = 3, Height = 4 };
		var pending = new PageTextureNode { Id = 2, U = 1, V = 2, Width = 5, Height = 6 };
		var (order, area) = OriginalPageListMerge.Merge( existing, pending, 0 );
		CollectionAssert.AreEqual( new uint[] { 1, 2 }, order.Select( node => node.Id ).ToArray() );
		Assert.AreEqual( 3 * 4 + 5 * 6, area );

		// the product is a signed 32-bit multiply and the sum is unchecked
		var big = new PageTextureNode { Id = 3, Width = 30000, Height = 30000 };
		var bigger = new PageTextureNode { Id = 4, Width = 30000, Height = 30000 };
		var (_, wrapped) = OriginalPageListMerge.Merge( null, big, 0 );
		Assert.AreEqual( unchecked(30000 * 30000), wrapped );
		big.Next = bigger;
		Assert.AreEqual( unchecked(30000 * 30000 * 2), OriginalPageListMerge.Merge( null, big, 0 ).Area );
	}

	[TestMethod]
	public void ALoneDeviceDependentPendingRecordIsDroppedButExistingNeedsItsFlag()
	{
		var dependent = new PageTextureNode { Id = 1, Flags = 0x30000 };
		Assert.AreEqual( 0, OriginalPageListMerge.Merge( null, dependent, 1 ).Order.Length, "device dependent pending on device 1" );
		Assert.AreEqual( 1, OriginalPageListMerge.Merge( null, dependent, 0 ).Order.Length, "the same record on another device" );

		var flagged = new PageTextureNode { Id = 2, Flags = OriginalPageListMerge.ExistingOnly };
		var unflagged = new PageTextureNode { Id = 3 };
		flagged.Next = unflagged;
		var (order, _) = OriginalPageListMerge.Merge( flagged, null, 0 );
		CollectionAssert.AreEqual( new uint[] { 2 }, order.Select( node => node.Id ).ToArray(), "only the flagged existing record is emitted" );
	}
}

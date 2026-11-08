using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The page rectangle allocator (tp.exe 0x58ECA0 with its key sort 0x57D090 / 0x57CED0, UI-004) against the cases the x86 oracle ran: every native observable of every case is compared, including the
/// rank keys the downsample rewrites and the order the request list is left in. The branch test checks the fixture's categories only; it does not prove every control-flow edge.
/// </summary>
[TestClass]
public class UiPageAllocGoldenTests
{
	private sealed record Case( string Name, int PageWidth, int PageHeight, uint Flags, int Mask, bool AlignmentFits, int Device, long[][] Requests, JsonElement Expected );

	private static List<Case> Cases()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( UiPageAllocGolden.Packed ) ), CompressionMode.Decompress ) );
		using var document = JsonDocument.Parse( unpacked.ReadToEnd() );
		return document.RootElement.Clone().EnumerateArray().Select( item => new Case(
			item.GetProperty( "name" ).GetString()!,
			item.GetProperty( "page" )[0].GetInt32(), item.GetProperty( "page" )[1].GetInt32(),
			item.GetProperty( "flags" ).GetUInt32(), item.GetProperty( "mask" ).GetInt32(),
			item.GetProperty( "align" ).GetInt32() != 0, item.GetProperty( "device" ).GetInt32(),
			item.GetProperty( "requests" ).EnumerateArray().Select( request => request.EnumerateArray().Select( value => value.GetInt64() ).ToArray() ).ToArray(),
			item.GetProperty( "expected" ) ) ).ToList();
	}

	private static PageTexture[] Requests( Case c ) => c.Requests.Select( request => new PageTexture
	{
		Id = (uint)request[0], Width = (short)request[1], Height = (short)request[2], Flags = (uint)request[3],
		Priority = BitConverter.UInt32BitsToSingle( (uint)request[4] ), SortKey = (uint)request[5],
	} ).ToArray();

	private static (PageAllocation Result, PageAllocatorCache Cache, PageTexture[] Requests) Run( Case c )
	{
		var cache = new PageAllocatorCache { Flags = c.Flags, AlignmentMask = c.Mask, AlignmentFits = c.AlignmentFits };
		var requests = Requests( c );
		return (OriginalPageAllocator.Allocate( cache, c.PageWidth, c.PageHeight, requests, c.Device ), cache, requests);
	}

	[TestMethod]
	public void EveryNativeObservableOfEveryCaseIsReproduced()
	{
		var cases = Cases();
		Assert.IsTrue( cases.Count > 0 );
		var mismatches = new List<string>();
		foreach ( var c in cases )
		{
			var (result, cache, requests) = Run( c );
			var expected = c.Expected;
			void Check( string what, object? want, object? got )
			{
				if ( !Equals( want, got ) )
					mismatches.Add( $"{c.Name}: {what} expected {want} got {got}" );
			}
			void CheckSequence( string what, IEnumerable<long> want, IEnumerable<long> got )
			{
				var left = string.Join( ",", want );
				var right = string.Join( ",", got );
				if ( left != right )
					mismatches.Add( $"{c.Name}: {what} expected [{left}] got [{right}]" );
			}

			Check( "success", expected.GetProperty( "success" ).GetInt32() != 0, result.Success );
			Check( "max width", expected.GetProperty( "max" )[0].GetInt32(), cache.MaxWidth );
			Check( "max height", expected.GetProperty( "max" )[1].GetInt32(), cache.MaxHeight );
			Check( "retry", expected.GetProperty( "retry" ).GetInt32() != 0, cache.Retry );
			Check( "terminator", expected.GetProperty( "terminator" ).GetInt64(), (long)result.Terminator );
			Check( "placed", expected.GetProperty( "placed" ).GetInt32(), result.Placements.Length );
			CheckSequence( "placements", expected.GetProperty( "placements" ).EnumerateArray().SelectMany( p => p.EnumerateArray().Select( v => v.GetInt64() ) ),
				result.Placements.SelectMany( p => new long[] { p.X, p.Y, p.Width, p.Height, p.Id } ) );
			CheckSequence( "order", expected.GetProperty( "order" ).EnumerateArray().Select( v => v.GetInt64() ), result.Order.Select( id => (long)id ) );
			// the mutated request records, in the order they were handed in
			CheckSequence( "textures", expected.GetProperty( "textures" ).EnumerateArray().SelectMany( t => t.EnumerateArray().Select( v => v.GetInt64() ) ),
				Requests( c ).Join( requests, original => original.Id, request => request.Id, ( original, request ) => request )
					.SelectMany( t => new long[] { t.Id, t.Width, t.Height, t.Flags, t.SortKey } ) );
		}
		Assert.AreEqual( 0, mismatches.Count, string.Join( "\n", mismatches.Take( 12 ) ) );
	}

	[TestMethod]
	public void TheFixtureIncludesAllocatorBoundaryAndRetryCases()
	{
		var cases = Cases();
		var runs = cases.Select( c => (Case: c, Run: Run( c ), Original: Requests( c )) ).ToList();
		bool Any( Func<(Case Case, (PageAllocation Result, PageAllocatorCache Cache, PageTexture[] Requests) Run, PageTexture[] Original), bool> predicate ) => runs.Any( predicate );

		Assert.IsTrue( Any( r => r.Run.Result.Success && r.Run.Result.Placements.Length > 0 ), "successful packing" );
		Assert.IsTrue( Any( r => !r.Run.Result.Success && r.Run.Result.Placements.Length < OriginalPageAllocator.MaxPlacements ), "a failed packing below the cap" );
		Assert.IsTrue( Any( r => r.Run.Result.Placements.Length == OriginalPageAllocator.MaxPlacements ), "the 127 placement cap" );
		Assert.IsTrue( Any( r => r.Run.Cache.Retry ), "a downsample" );
		Assert.IsTrue( Any( r => r.Run.Cache.Retry && !r.Run.Result.Order.SequenceEqual( r.Original.Select( t => t.Id ) ) ), "a downsample that reorders the list" );
		Assert.IsTrue( Any( r => r.Run.Cache.Retry && r.Run.Result.Order.SequenceEqual( r.Original.Select( t => t.Id ) ) ), "a downsample that keeps the list" );
		Assert.IsTrue( Any( r => r.Case.AlignmentFits ), "alignment gate on" );
		Assert.IsTrue( Any( r => !r.Case.AlignmentFits ), "alignment gate off" );
		Assert.IsTrue( Any( r => (r.Case.Flags & 2) != 0 ), "padding on" );
		Assert.IsTrue( Any( r => (r.Case.Flags & 2) == 0 ), "padding off" );
		Assert.IsTrue( Any( r => (r.Case.Flags & 1) != 0 ), "auto shrink on" );
		Assert.IsTrue( Any( r => (r.Case.Flags & 1) == 0 ), "auto shrink off" );
		Assert.IsTrue( Any( r => r.Case.Device == 1 && r.Original.Any( t => (t.Flags & 0x30000) != 0 ) ), "device dependent textures on device kind 1" );
		Assert.IsTrue( Any( r => r.Case.Device != 1 ), "another device kind" );
		Assert.IsTrue( Any( r => r.Case.Requests.Length == 0 ), "an empty request list" );
	}

	[TestMethod]
	public void TheRankKeyIsThePriorityKeyOfTheOriginal()
	{
		// 1.5 - 0.5 = 1.0, * 2047 = 2047 = 0x7FF; ((0x7FF * 256 + 20) * 256 + 10)
		var texture = new PageTexture { Id = 1, Width = 20, Height = 10, Priority = 0.5f };
		OriginalPageAllocator.RefreshRank( texture );
		Assert.AreEqual( ((0x7FFu * 256 + 20) * 256) + 10, texture.SortKey );
		texture.Flags = 0x4000 | 0x80000;
		OriginalPageAllocator.RefreshRank( texture );
		Assert.AreEqual( (((0x7FFu * 256 + 20) * 256) + 10) | 0x60000000u, texture.SortKey );
		// truncation toward zero and the 12 bit mask: (1.5 - 1.5) * 2047 = 0; (1.5 + 0.5) * 2047 = 4094 = 0xFFE
		texture.Flags = 0;
		texture.Priority = 1.5f;
		OriginalPageAllocator.RefreshRank( texture );
		Assert.AreEqual( 20u * 256 + 10, texture.SortKey );
		texture.Priority = -0.5f;
		OriginalPageAllocator.RefreshRank( texture );
		Assert.AreEqual( ((0xFFEu * 256 + 20) * 256) + 10, texture.SortKey );
	}

	[TestMethod]
	public void LeftoversAboveEightStayFreeAndAnEmptyListSucceeds()
	{
		var cache = new PageAllocatorCache();
		Assert.IsTrue( OriginalPageAllocator.Allocate( cache, 64, 64, Array.Empty<PageTexture>(), 0 ).Success );

		// 56 x 56 in a 64 page leaves 8: both remainders are dropped, the single free rectangle is consumed
		var exact = OriginalPageAllocator.Allocate( cache, 64, 64, new[] { new PageTexture { Id = 1, Width = 56, Height = 56 }, new PageTexture { Id = 2, Width = 1, Height = 1 } }, 0 );
		Assert.IsFalse( exact.Success, "no free rectangle is left for the second request" );
		Assert.AreEqual( 1, exact.Placements.Length );

		// 55 x 55 leaves 9: the bottom rectangle (55 wide, 9 high) is appended, the right one (9 wide, full height) stays; the second request takes the LAST free rectangle after the sort
		var split = OriginalPageAllocator.Allocate( cache, 64, 64, new[] { new PageTexture { Id = 1, Width = 55, Height = 55 }, new PageTexture { Id = 2, Width = 9, Height = 64 } }, 0 );
		Assert.IsTrue( split.Success );
		Assert.AreEqual( new PagePlacement( 55, 0, 9, 64, 2 ), split.Placements[1] );
	}

	[TestMethod]
	public void TheFreeRectangleSortLeavesTheOriginalsTiePermutationAndComparesUnsigned()
	{
		for ( var count = 2; count <= 40; count++ )
		{
			var rects = Enumerable.Range( 0, count ).Select( index => new PageFreeRect { X = index, Key = 7 } ).ToArray();
			OriginalPageAllocator.SortDescending( rects );
			CollectionAssert.AreEqual( OpenTPW.UI.Original.OriginalDepthSort.TieOrder( count ), rects.Select( rect => rect.X ).ToArray(), $"{count} ties" );
		}
		var unsigned = new[] { new PageFreeRect { X = 0, Key = 5 }, new PageFreeRect { X = 1, Key = 0x80000000 }, new PageFreeRect { X = 2, Key = 0xFFFFFFFF } };
		OriginalPageAllocator.SortDescending( unsigned );
		CollectionAssert.AreEqual( new[] { 2, 1, 0 }, unsigned.Select( rect => rect.X ).ToArray() );
	}
}

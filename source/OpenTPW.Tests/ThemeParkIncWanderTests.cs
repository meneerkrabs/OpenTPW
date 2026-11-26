using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's wander and patrol destination <c>0x609F70</c> with the patrol rectangle <c>0x62C610</c> and the fallback <c>0x62C720</c> (NV4, docs/reverse/TPI-NAVIGATOR.md), and the route
/// requests <c>0x60B7F0</c>, <c>0x60B8E0</c> and the staff route <c>0x62EB10</c> (NV3), against Game.exe's results on the navigator tests' map with its cells linked to their path neighbours
/// (<c>tools/ppc-analysis/oracle_x86/tpi_wander_golden.py</c>, which checks walk_reading.py against the run first). The staff take <see cref="ThemeParkIncPatrol"/>, the guests
/// <c>GuestSimulation.Wander</c>, <c>RequestRoute</c> and <c>RouteToTpiPoint</c>.
/// </summary>
[TestClass]
public class ThemeParkIncWanderTests
{
	private sealed record Wander( string Name, int Answer, uint Rng, int Counter, int Mark, int Mask, string Thoughts, string Record );

	private static readonly (int Kind, int X, int Y, int Counter, int Mark, (int, int, int, int) Area, uint Rng, bool Closed)[] Cases =
	[
		(1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 7, 0, Whole, 0x12345678, false),
		(1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 20, 15, Whole, 0x0BADF00D, false),
		(1, (47 << 16) + 0x4000, (40 << 16) + 0xC000, 30, 50, Whole, 0x00C0FFEE, false),
		(1, (42 << 16) + 0x8000, (41 << 16) + 0x8000, 3, 0, Whole, 0x7FFFFFFF, false),
		(5, (47 << 16) + 0x8000, (40 << 16) + 0x8000, 11, 0, Whole, 0x2468ACE0, false),
		(0x14, (45 << 16) + 0x8000, (40 << 16) + 0x8000, 11, 0, (45, 40, 50, 42), 0x13579BDF, false),
		(1, (49 << 16) + 0x5000, (42 << 16) + 0xA000, 12, 0, Whole, 0x0D15EA5E, false),
		(4, (41 << 16) + 0x8000, (40 << 16) + 0x8000, 4, 0, (50, 44, 55, 49), 0x0F1E2D3C, false),
		(4, (41 << 16) + 0x8000, (40 << 16) + 0x8000, 4, 0, (54, 49, 50, 45), 0x0F1E2D3C, false),
		(7, (42 << 16) + 0x8000, (40 << 16) + 0x8000, 4, 0, (44, 40, 41, 40), 0x55555555, false),
		(6, (47 << 16) + 0x8000, (40 << 16) + 0x8000, 4, 0, (44, 40, 41, 40), 0x31415926, false),
		(8, (40 << 16) + 0x8000, (40 << 16) + 0x8000, 9, 0, (40, 40, 40, 40), 0x27182818, false),
		(7, (42 << 16) + 0x8000, (41 << 16) + 0x8000, 2, 0, Whole, 0x1F2E3D4C, false),
		(5, (52 << 16) + 0x8000, (48 << 16) + 0x8000, 6, 0, Whole, 0x600DCAFE, true),
	];

	private static (int, int, int, int) Whole => (0, 0, 0x7F, 0x7F);

	/// <summary>Game.exe's answer, the generator's state after the draws, the counter, the mark, the walk mask, the thoughts asked for (presentation) and the record's SHA-256.</summary>
	private static readonly Wander[] Expected =
	[
		new( "guest", 1, 0x6211AF57, 10, 0, 0, "", "3f8296b7da4f9f7ff2be49e74c4388b7b90af0b35f774df12bc0363941b1ef4b" ),
		new( "guest-held", 0, 0x0BADF00D, 21, 15, 0, "11", "cbd34b550a89cc8061f87ef8190f1c62dfefbc3fb2e9a9b4f3a4b543d6281ef5" ),
		new( "guest-stale-mark", 1, 0xB21D5103, 33, 0, 0, "", "4a15ac9ee9bdf03048ef06c9857fee938f5a5e6f5acef5401e097f87f49105c4" ),
		new( "guest-off-path", 1, 0x6A95E2AC, 6, 0, 0, "", "10925fac6dec617574add9f8fe4754683e35fe25639779306a98317d826d3c8c" ),
		new( "handyman", 1, 0x4B069BE8, 14, 0, 0, "", "872f5a6bd4b0a3434b0e082cf7b059bcdf4565d4b060b8635a66f418aaf8ee8b" ),
		new( "gardener-area", 1, 0xA71C5CF3, 14, 0, 0, "", "777a28b6751119b27408f5b340421fbda4b51084f0e1b309f2da806e6bfaad66" ),
		new( "guest-junction", 1, 0xDCE57021, 15, 0, 0, "", "a0a0dd1bdd7889df8b8462ae81edead9a3ff85b900e69f37da194be2abc3fbf3" ),
		new( "mechanic-outside", 1, 0x349CCFA9, 7, 0, 0, "", "86aec5435ec901d6fd97f47a0344c11709532ea32a487f78a02a433944246549" ),
		new( "mechanic-outside-reversed", 1, 0x2F387FCD, 7, 0, 0, "1e", "f95fb944cea7481bc2d2c072ef8e13955a8d3eaf9b27e3335e5b364b4e3aa672" ),
		new( "guard-reversed-inside", 1, 0x4556E319, 7, 0, 0, "", "d7db35295e55926e1bd2dd3c40013f4e86d42c1341cc42d6274c0dc450f09645" ),
		new( "entertainer-reversed-outside", 1, 0x1C2D6DBF, 7, 0, 0, "1e", "8ee3e48414a55e1801180b0e806471a26ce38d03f9ea9d05a3f7812f1aa3a483" ),
		new( "researcher-one-cell", 1, 0x37034C38, 12, 0, 0, "", "894236c169610311ec0f2b4ef910a58e5776d1d04d7935367ab28ef4d1322048" ),
		new( "guard-off-path", 1, 0xC1D9D502, 5, 0, 0, "", "0f2df89268450ce0a31ac16881f189cac3f72048baabc136436ee979442e525c" ),
		new( "handyman-closed", 1, 0xB22B768B, 9, 0, 0, "", "30574c57c59aaa76ad98c6983c349714ce47c8f00d1ee0b8f2dd8a39a74e0ff8" ),
	];

	private sealed record Requests( string Name, string Rows );

	private static readonly (int Kind, int X, int Y, int Counter, int Mark, (int Bx, int By, int Stamp)[] Stamps, bool Closed, (char Kind, int A, int B)[] Requests)[] RequestCases =
	[
		(1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 5, 0, [], false, [('c', 49, 44), ('c', 42, 41), ('c', 55, 48)]),
		(1, (49 << 16) + 0x5000, (42 << 16) + 0xA000, 40, 0, [], false, [('p', (54 << 8) | 0x33, (48 << 8) | 0xC0), ('p', (40 << 8) | 0x05, (49 << 8) | 0x7B)]),
		(1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 20, 15, [(2, 2, 9)], false, [('c', 49, 44), ('p', (44 << 8) | 0x80, (46 << 8) | 0x80)]),
		(1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 20, 15, [(2, 2, 17)], false, [('c', 49, 44)]),
		(1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 20, 90, [], false, [('p', (52 << 8) | 0x40, (41 << 8) | 0x40)]),
		(5, (47 << 16) + 0x8000, (40 << 16) + 0x8000, 3, 0, [], false, [('s', (55 << 8) | 0x80, (49 << 8) | 0x80), ('s', (42 << 8) | 0x80, (41 << 8) | 0x80)]),
		(7, (47 << 16) + 0x8000, (40 << 16) + 0x8000, 3, 0, [], true, [('s', (0x2F << 8) | 0x80, (0x11 << 8) | 0x80), ('s', (0x30 << 8) | 0x10, (0x11 << 8) | 0xF0), ('s', (44 << 8) | 0x80, (46 << 8) | 0x80)]),
	];

	/// <summary>Per request "answer:counter:mark:mask:record SHA-256 (first 16 hex digits)".</summary>
	private static readonly Requests[] ExpectedRequests =
	[
		new( "guest-cell", "1:7:0:0:479ce02fb80af8c7,0:8:0:0:fb930992a52552de,1:10:0:0:cea4d4aa01e03dc1" ),
		new( "guest-point", "1:42:0:0:818c9cc39b1be440,1:44:0:0:b00b31bef3eb98de" ),
		new( "guest-held", "0:21:15:0:cbd34b550a89cc80,0:22:15:0:cbd34b550a89cc80" ),
		new( "guest-released", "1:22:0:0:289baac0938d45b6" ),
		new( "guest-stale", "0:21:0:0:548fd27831205e89" ),
		new( "staff-open", "1:5:0:0:96c5afb8f24ef34d,1:7:0:1:fb30d21671aeead9" ),
		new( "staff-closed", "0:3:0:0:6f655a850969990e,0:3:0:0:6f655a850969990e,1:5:0:1:23a09191353be6d2" ),
	];

	/// <summary>The navigator tests' map with every path and queue cell linked to its path and queue neighbours only, the counter and the block stamps given.</summary>
	private static ParkCellMap Map( int counter, (int Bx, int By, int Stamp)[]? stamps = null )
	{
		var map = ThemeParkIncNavigatorTests.Map();
		for ( var y = 0; y < 128; y++ )
		{
			for ( var x = 0; x < 128; x++ )
			{
				if ( map.RawTypeAt( x, y ) is not (1 or 3) )
					continue;
				var links = 0;
				foreach ( var (bit, dx, dy) in new[] { (1, 0, -1), (4, 1, 0), (0x10, 0, 1), (0x40, -1, 0) } )
					if ( map.InBounds( x + dx, y + dy ) && map.RawTypeAt( x + dx, y + dy ) is 1 or 3 )
						links |= bit;
				map.SetLinks( x, y, (byte)links );
			}
		}
		var blocks = new int[map.BlocksX * map.BlocksY];
		foreach ( var (bx, by, stamp) in stamps ?? [] )
			blocks[by * map.BlocksX + bx] = stamp;
		map.RestoreBlockStamps( counter, blocks.Any( stamp => stamp != 0 ) ? blocks : null );
		return map;
	}

	private static GuestNavigation Walk( int x, int y ) => new() { X = x, Y = y, MaxSpeed = 13107, MaxForce = 26214 };

	private static string Digest( GuestNavigation walk ) => Convert.ToHexStringLower( SHA256.HashData( walk.ToRecord().Raw ) );

	/// <summary>A guest of a simulation on <paramref name="map"/> standing at (<paramref name="x"/>, <paramref name="y"/>) with a fresh navigator (the constructor's members), the generator attached.</summary>
	private static (GuestSimulation Simulation, Guest Guest) Guest( ParkCellMap map, int x, int y, int counter, int mark, WorldRng world, (int Bx, int By, int Stamp)[]? stamps = null )
	{
		var simulation = new GuestSimulation( new GuestPathGrid( map ), GuestTests.CreateSettings(), 7 );
		var guest = simulation.SpawnInPark( 40, 40 );
		(guest.CellX, guest.CellY) = (x >> 16, y >> 16);
		guest.Navigation.LoadRecord( Walk( x, y ).ToRecord() );
		guest.StrandedMark = mark;
		simulation.AttachWorld( world );
		var blocks = new int[map.BlocksX * map.BlocksY];
		foreach ( var (bx, by, stamp) in stamps ?? [] )
			blocks[by * map.BlocksX + bx] = stamp;
		map.RestoreBlockStamps( counter, blocks.Any( stamp => stamp != 0 ) ? blocks : null );
		return (simulation, guest);
	}

	private static object? Call( GuestSimulation simulation, string name, params object[] arguments ) =>
		typeof( GuestSimulation ).GetMethod( name, BindingFlags.NonPublic | BindingFlags.Instance )!.Invoke( simulation, arguments );

	[TestMethod]
	public void TheWanderAndThePatrolFollowGameExe()
	{
		Assert.AreEqual( Cases.Length, Expected.Length );
		for ( var index = 0; index < Cases.Length; index++ )
		{
			var (kind, x, y, counter, mark, area, rng, closed) = Cases[index];
			var expected = Expected[index];
			var world = new WorldRng( rng );
			var map = Map( counter );
			bool answer;
			GuestNavigation walk;
			int markAfter;
			if ( kind == 1 )
			{
				var (simulation, guest) = Guest( map, x, y, counter, mark, world );
				answer = (bool)Call( simulation, "Wander", guest )!;
				(walk, markAfter) = (guest.Navigation, guest.StrandedMark);
			}
			else
			{
				walk = Walk( x, y );
				answer = ThemeParkIncPatrol.Destination( new GuestPathGrid( map ), walk, area, world, parkOpen: !closed );
				markAfter = 0;
			}
			Assert.AreEqual( (expected.Answer, expected.Rng, expected.Counter, expected.Mark, expected.Mask), (answer ? 1 : 0, world.State, map.StampCounter, markAfter, walk.Mask), expected.Name );
			Assert.AreEqual( expected.Record, Digest( walk ), expected.Name );
		}
	}

	[TestMethod]
	public void TheRouteRequestsAndTheStaffRouteFollowGameExe()
	{
		Assert.AreEqual( RequestCases.Length, ExpectedRequests.Length );
		for ( var index = 0; index < RequestCases.Length; index++ )
		{
			var (kind, x, y, counter, mark, stamps, closed, requests) = RequestCases[index];
			var map = Map( counter, stamps );
			var rows = new System.Collections.Generic.List<string>();
			if ( kind == 1 )
			{
				var (simulation, guest) = Guest( map, x, y, counter, mark, new WorldRng( 1 ), stamps );
				foreach ( var (request, a, b) in requests )
				{
					var answer = request == 'c'
						? (bool)Call( simulation, "RequestRoute", guest, (a, b), null! )!
						: (bool)Call( simulation, "RouteToTpiPoint", guest, (a >> 8, b >> 8), a & 0xFF, b & 0xFF )!;
					rows.Add( $"{(answer ? 1 : 0)}:{map.StampCounter}:{guest.StrandedMark}:{guest.Navigation.Mask}:{Digest( guest.Navigation )[..16]}" );
				}
			}
			else
			{
				var walk = Walk( x, y );
				foreach ( var (_, a, b) in requests )
				{
					var answer = ThemeParkIncPatrol.StaffRoute( new GuestPathGrid( map ), walk, !closed, (a >> 8, b >> 8), a & 0xFF, b & 0xFF );
					rows.Add( $"{(answer ? 1 : 0)}:{map.StampCounter}:0:{walk.Mask}:{Digest( walk )[..16]}" );
				}
			}
			Assert.AreEqual( ExpectedRequests[index].Rows, string.Join( ',', rows ), ExpectedRequests[index].Name );
		}
	}

	[TestMethod]
	public void ThePatrolRectangleOrdersItsCorners()
	{
		// [BIN:TPI-EXE:0x62C610 patrol rectangle] W3: the corners are ordered before the test (tp.exe's 0x00506ED0 orders nothing)
		Assert.IsTrue( ThemeParkIncPatrol.InArea( (44, 40, 41, 42), (42, 41) ) );
		Assert.IsTrue( ThemeParkIncPatrol.InArea( (41, 42, 44, 40), (44, 40) ) );
		Assert.IsFalse( ThemeParkIncPatrol.InArea( (44, 40, 41, 42), (45, 41) ) );
		Assert.IsTrue( ThemeParkIncPatrol.InArea( (0, 0, 0x7F, 0x7F), (0x7F * 128 + 0x7F + 1) ), "a cell word" );
	}

	[TestMethod]
	public void ReversedCornersOneCellApartCannotBeRestored()
	{
		// the fallback 0x62C720 divides by x1 - x0 + 1 and y1 - y0 + 1 (unordered): zero is a divide error in the original, so a restore refuses such a rectangle
		var world = new WorldRng( 5 );
		Assert.ThrowsException<DivideByZeroException>( () => ThemeParkIncPatrol.Fallback( new GuestPathGrid( Map( 0 ) ), Walk( (42 << 16) + 0x8000, (40 << 16) + 0x8000 ), (44, 40, 43, 40), world, true ) );
		var validate = typeof( ThemeParkIncStaffMember ).GetMethod( "IsWalkableRectangle", BindingFlags.NonPublic | BindingFlags.Static )!;
		Assert.IsFalse( (bool)validate.Invoke( null, [new[] { 44, 40, 43, 40 }] )! );
		Assert.IsFalse( (bool)validate.Invoke( null, [new[] { 40, 44, 40, 43 }] )! );
		Assert.IsTrue( (bool)validate.Invoke( null, [new[] { 44, 40, 42, 40 }] )!, "reversed by two: a negative width, an unsigned divisor above every draw" );
	}
}

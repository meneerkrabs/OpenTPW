using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's native walk storage (navigator phase NV5, docs/reverse/TPI-NAVIGATOR.md): the person block of the person serializer <c>0x609170</c> (<see cref="ThemeParkIncPersonRecord"/>), its
/// load and save semantics with the re-route <c>0x609B55</c> (<see cref="ThemeParkIncNativeWalk"/>) against Game.exe's results (<c>tools/ppc-analysis/oracle_x86/tpi_person_golden.py</c>, which checks
/// walk_reading.py against the run first), and OpenTPW's own Theme Park Inc resume of native guests and staff with the walk stored natively and every other walk field derived.
/// </summary>
[TestClass]
public class ThemeParkIncNativeWalkTests
{
	/// <summary>A walker of the semantics with the person words as plain fields.</summary>
	private sealed class Walker : IThemeParkIncWalker
	{
		public GuestNavigation Walk { get; } = new();
		public int AccurateDestX { get; set; }
		public int AccurateDestY { get; set; }
		public int StrandedTime { get; set; }
		public int PreviousX { get; set; }
		public int PreviousY { get; set; }
		public int BaseSpeedWord { get; set; }
		public int PurposeSpeed { get; set; }
		public ushort AdjustorSpeed { get; set; }
		public float PreviousSpeed { get; set; }
		public int SpriteAngle { get; set; }
		public bool SetDestSuccessfully { get; set; }
	}

	/// <summary>
	/// One golden case: the park's counter and block stamps at the load (a fresh process: 0 and none), the block, and Game.exe's state after the load half's re-route (counter, mark, the record's
	/// SHA-256), the SHA-256 of the bytes its save half wrote, and the state after the save's re-route.
	/// </summary>
	private sealed record Golden( string Name, int Counter, string Stamps, string Block, int LoadCounter, int LoadMark, string LoadRecord, string Written, int SaveCounter, int SaveMark,
		string SaveRecord );

	private static readonly Golden[] Cases =
	[
	// walking-fresh
	new( "walking-fresh", 0, "",
		"632b8d2886dcd0e2d297d725121e5d71fc7599f35036a0306dfb92014a641b7e56360200000000000000000000000000000000f5020100d7feffff29010000f50201000000010066660000333300000000000000000000050000000000000000af0e00000000000000001100000004000050360000a030000600000006000000002823000e632b00b38d2800333300000080370000802800000009000080370000803100000002000080350000803100000005000080350000802c00000001000080360000802c000000000097330000c5ffffffaa7e549d772f2b00ee8d2800000000004c2a000000005d848d740d000000837877137318b05925c402d6e42a5e16e43147843e239c273f29225c4ea4fef54b63f16b030b6032c60d7188c02d20fd3748cec6edf4ef5ef44759e10971f757a4655a38e7fb24bb6ce20ee09cf953e5584e2a3fc15ff3ecd9e7b5b99e12e2b49ecaf43ac947f227c9888186173eb11e1e7adbf1496c8c59b28b311e828c080e11b98fa8d4ee0edb33e7f463af47cc08db03177c62f5d3e203b6",
		2, 0, "f20bb55df77ff16c9eddd28351c89798c1c941057eeadf64c0b368d52fb3b0c2", "828c26231bad460ee872682fe577ce45d3b95c363c47a9b90a602bd4b1d57340", 4, 0, "a3c223e3388dc24a3a96715df0d25302ca6310de6c7953415bd55bf87f64a1ae" ),
	// walking-running
	new( "walking-running", 40, "3,2,33;2,2,39",
		"632b8d2881cd0f260067014e37bc84c3b6024f635036a030e621e892e5a31fd7cba36700000000000000000000000000000000f5020100d7feffff29010000f50201000000010066660000333300000000000000000000050000000000000000af0e00000000000000001100000004000050360000a030001f00000006000000002823000e632b00b38d2800333300000080370000802800000009000080370000803100000002000080350000803100000005000080350000802c00000001000080360000802c000000000097330000c5ffffff77bd5ac6772f2b00ee8d280000000000849801000000d2ea991ccf0200009fbb4632b3a63c7de0c3a08b2b79635e7867594ed3d38c77278504b7c8ad5afbc26ccc40e18ccc7efa5695a8a498a47a173b178b32a0f9237ec50392e8af32ae2c7adbb62d1acc704fc8e7c03bfa178535a3a3bd3a75a803662668a6d09dc62574570aae1d228196fd57ae1fc8c953ae80d676343e7a3c86fb2b9ec69956ecc6e5fac9792665b16fbd9902aba17e69c1c57357ad3d616f961efa",
		42, 0, "283cfc218c3e1170e6f8fde85bb3fde031a42ae71d8ca05775a76bc0cd06db6c", "8ffc2a76345c80314c0c3e7e778897418422b454a48488cf55f90728ae693659", 44, 0, "20e2796e2842a0fc5fd2c6f2c03c1c3ab66240cb104743eca5f4b512174239b3" ),
	// mark-fresh
	new( "mark-fresh", 0, "",
		"402fc028227ed6ea767fe810f1a2339182a82629802c802e981ca53fd6648254202045000000000000000000000000000000000000000000000000000000000000000000000100666600003333000001000000000000000000000000000000010000010007000000000000000000000000802c0000802e0000000000000000000000000000402f0000c028003333000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000d3b3f7e00402f0000c028000f000000f77f000000001b9152307e0000007334480e2d19719d36979d290669bce731d93fbbb78e2e42530fc8abc49c349bd05baa69ab644d6a5b80e26a5f866d9d5411749effe5820b2d4bcf1ccbbaadc6eb7b8e2e475600e17aed1b0c901a99a066eed622401c7ab133cc313ee554100050b5520e30be87a95f82efb17bf01315f5d3750960717dec8eee3a6273ac5f058fbe0dc25369f6c395791989f509e157689a0206cdb18afd0896",
		1, 0, "c86f79bffc93425e1088281b43ebf6617a20676426996f859a15f9c635343f6a", "62377ebadd1ff4076c82fd89f9d30b9fc71f0fdf4b9322700ca27cc664bd86dd", 2, 0, "c86f79bffc93425e1088281b43ebf6617a20676426996f859a15f9c635343f6a" ),
	// mark-held
	new( "mark-held", 20, "2,2,9",
		"942a8d289406af8dd94b4981d4d884ec1ee53fa08031802cc9b8e2678dd76c37a335b400000000000000000000000000000000f002010000feffff00020000f00201000000010066660000333300000000000000000000020000000000000000801900000000000000000400000000000080310000802c000d0000000200000000280c00ca942a009d8d2800333300000080310000802800000004000080310000802c0000000000000000000000000000000000000000000000000000000000000000000000000000000000963300009affffff47ac2d7334612a00038e28000f0000004ecb01000000648590e7c2010000939ff7e1b30c82e54987c3ee9235ea2c2e2d20b72e69969e5dc8b05943ba560a603f5391410c3ac2d160fb2982aaebc6bcdd9e611bdf87d2e62dacb8ea8f040145a13ed0bd289682d7b8bbaf69af48a6741ec7175f116ae5e458722b336a7f2338164d79e16fe32cca0d55ab1fc80ef70c7b630ed42c4dc4ca3bb1e6e0c57fc463877a7673e309ea0e1e4ed75cce297660b5c6b27769b1eedf9b",
		21, 15, "856fe2f12a28334a61ac4a02cc2343ccc564e86b430c0309b32bdc6a78247fd4", "3b2a8e107cdf81160fec58b14d5be29830b29e5495facbdcdb4395c5a5c23451", 22, 15, "856fe2f12a28334a61ac4a02cc2343ccc564e86b430c0309b32bdc6a78247fd4" ),
	// mark-released
	new( "mark-released", 20, "2,2,17",
		"942a8d2827ccd5fcce3946ead9854b43dbba11e38031802c40fdb52547893cb3e174c700000000000000000000000000000000f002010000feffff00020000f00201000000010066660000333300000000000000000000020000000000000000801900000000000000000400000000000080310000802c000d0000000200000000280c00ca942a009d8d2800333300000080310000802800000004000080310000802c0000000000000000000000000000000000000000000000000000000000000000000000000000000000963300009affffff287702c434612a00038e28000f000000a1550100000093ca76fc32000000f07160ee9bcb140cb1bac2a24df8a51d62646a193eab50d2b9c517280443c8c723f218082637574ec355b78844d31528e3d2775f21c2f90dc3f398998de7fca083b8f183a907c180d3cb945dee09fcd409df6b084efd72236c56690249a8998630e054b453371360d95323b3b6f4514d164d228a68728b515f32664fe49c44f2f24c3a35f2e0cc3da9a555298e48c119dddf58e4d2a424df12bb",
		22, 0, "4f1a24767316fd88105886c6d9d42bdf0d297b61b39aa6a95b6a14e132e4c588", "3f99715e70985b7b0e18a7b9527f51b6f558d3c3d5aea5336ec2afc1114a78bc", 24, 0, "2b3ce27c1caf74b7c12092c5ede7bea50a606dadfe70e16816f1b4989384daaf" ),
	// mark-above-counter
	new( "mark-above-counter", 20, "",
		"942a8d28244b682ca95f951dcbf5b5e6425faa558031802ca10541a03f2f96c919243100000000000000000000000000000000f002010000feffff00020000f00201000000010066660000333300000000000000000000020000000000000000801900000000000000000400000000000080310000802c000d0000000200000000280c00ca942a009d8d2800333300000080310000802800000004000080310000802c0000000000000000000000000000000000000000000000000000000000000000000000000000000000963300009affffff6f0392c234612a00038e28005a000000f67301000000278c05dbc5030000556db483e078f63074300bd9a083b658158f003a2eb1affd092562d790f61c85e63ffddfcb615775b9ee6096b4954bfd0ace6470c81144d52a5ca5a422a3fe2e48f2e514ab55f6a68c6d4877248ffbedd5bb6fc8a407e8ccde9ba3747681c362994f9f3e91f087cd2a4b7ab3523acd71102ef952f2313f7e2993ec70aa985a3228f81b169616f548a1951ab3143fd36d83ae7c7d5a791b87be9d",
		22, 0, "4f1a24767316fd88105886c6d9d42bdf0d297b61b39aa6a95b6a14e132e4c588", "b6619275852a291a6501fbffcf49bd6e70c29963775fba48761e0a8247e5b49a", 24, 0, "2b3ce27c1caf74b7c12092c5ede7bea50a606dadfe70e16816f1b4989384daaf" ),
	// unreachable
	new( "unreachable", 10, "",
		"5031a02aa517cc60997042011cb2d239c200d963802b802bf597e055fe859039954c6d00000000000000000000000000000000000000000000000000000000000000000000010066660000333300000100000000000000000000000000000001000001007f000000000000000000000000403400004029000000000000000000000000000050310000a02a00333300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ef7ce6960050310000a02a0000000000c06500000000f7d1a5595b030000436f0bd4f4c5b2932aa98d832fbe143930b1391525fa0bed2c19b67d8ce9dcf4870764c5f46a33138ff4a613500176fb5122c863d0768773edc63b7c34f0a800bc5d701a1f3c389ef81b1ad2021dd256d3e40f6edb3a20539afe8b15eb41be17f75617136d7cd16217f012f85accaaa1387985afaa29d19c4eb99d25a9b01290bf5ad94be4a6b9738ae885337c88e9976f34ada465f4a6fb8fa7",
		11, 0, "9903453033adde01358e26952bb0094cca7bf33b6cf6db8ffbbbe35d6bb9ff3a", "e538569b9b6f97f28c8da9e1ae61b00baeb5f945881487c078b6613fff1aa6ff", 12, 0, "9903453033adde01358e26952bb0094cca7bf33b6cf6db8ffbbbe35d6bb9ff3a" ),
	// arrived
	new( "arrived", 0, "",
		"60299028ddffa6f3cbda55640d234fb01f532b1b402ac029ffd5eef1e7ea8fbc02d8eb0000000000000000000000000000000000000000000000000000000000000000000001006666000033330000010000000000000000000000000000000100000100ffffffff000000000000000000402a0000c029000000000000000000000000000060290000902800333300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ee2e4289006029000090280000000000659a00000000784325dc5401000079cdb74fe8e2b6338bf18a296cf188c64136702c6415fca7b8c90318c57a670b1263de7ef86d19a84b5403887a707b3802ddf943b121b6067c1a093801f1f24edfdd7e52a1c0ff04dcf0dd615320792e20b4cbcbb790942dd1b2e8410170c5b836f3bb071b6f9ca03a3c915d6400b8f560b1654b21149e6b2a612fe957d9b5b3230caf847b6d41387f25319ab48c25b7dfeb0f917f44f8e51421",
		1, 0, "613c9535f5d43627e9863ee2866a4cc0382de1b1bbdee59d3dbc33372e94913f", "9f748099e1021b28753eafcdf6ac9996faab96f2941b03d8653244103b2a27d4", 2, 0, "613c9535f5d43627e9863ee2866a4cc0382de1b1bbdee59d3dbc33372e94913f" ),
	// leaving-mask
	new( "leaving-mask", 7, "",
		"812d422c7855d9ece05898fbe73ac049a0561a0d202c7029a7776032973d58aba368c6000000000000000000000000000000009bfdffffd2fcfeff2e0301009bfdffff0000010066660000333300000000000001000000030000000100000000e468000000000000000001000000000000202c00007029000300000003000000006006007d812d0094422c003333000000802d0000802d000000040000802d00008029000000010000802c00008029000000000000000000000000000000000000000000000000000000000086ffffff5eccffffb9ab12f9f7812d0036762c0000000000a2fe00000000a70a50a2c301000027261250d9b394a54a31c67d2e40e5b69aaf2aff279248ca0f7dc40e27134db248156c018b577924d514e56cb70249786c40b052ef474ed6629869cdeaf2d7c01e281a765e6f8a2f26fe99f7f7c660c07974c0300b7c20a1b21bffdcec6045c5bc938a4b5f9cbd3fcabbad896c43efb1eb404b0da5c03390177f0dfdef443503f27599d1f3d4faa2d96210c9661d19ec3a36ca9f5514b7fb6310",
		9, 0, "f062f7ed7fc0ebaee720722caf0f1fb047ba2864ca4dbc0d66c69cfb24126644", "a9534b8bcab5cfd936814acc5547f58dacc70a9586ba6c005f66cf12d4117dc3", 11, 0, "3149e3d5168c720a5d9c4c057cc111129fa92e85a1fa01ad83dca594944e837b" ),
	// another-dest
	new( "another-dest", 0, "",
		"9b3286282405ed9a634e3900a1c74a2811e7003205287b3113c1b307a19af794269b2d00000000000000000000000000000000910201009bf8ffff650700009102010000000100666600003333000000000000000000000500000001000000000c1d00000000000000001000000005000033360000c03000090000000700000000381d00fb9b3200b1862800333300000080310000802800000006000080370000802800000009000080370000803100000002000080350000803100000005000080350000802c00000000008333000087feffff3d99398b786832002a882800000000005d8c01000000c9fe40505e000000564151b5c875b492d1f52bbaa3a2ad07aed440c10b700ab389dbee6120e841d0e0df9c39da78dcbdb97f8dc568b50a5fc89870a02edc090008c3934b67eeec3ded25795cdbd9bc32f1c61c1e0b38cea6e138e586cd40e2c3977a59f34c8f6f3cec786fac8232e74920483e895de34027f66801d0d8836db4d519d00dc9cb80d2e1eaa5cf9b8bad4dad38de2bf0f07f4aaf8e2b9b20b15d3cc367",
		2, 0, "964bc02b60112ed2e855cfdd9137565169c7eabd74f36d86b2f6fbc75268c682", "825f5f0a0589affa102f8249dfaef1f5d3395a2a22896c73c3400f069ca408cf", 4, 0, "185d50426d99be3f085cc93d69cbbe104811ad1260655414434faee3d4a54a08" ),
	];

	private static string Sha( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );

	private static GuestPathGrid Grid( Golden golden )
	{
		var stamps = golden.Stamps.Length == 0 ? [] : golden.Stamps.Split( ';' ).Select( stamp => stamp.Split( ',' ).Select( int.Parse ).ToArray() ).Select( s => (s[0], s[1], s[2]) ).ToArray();
		return new GuestPathGrid( ThemeParkIncWanderTests.Map( golden.Counter, stamps ) );
	}

	/// <summary>T2's load semantics and N-1's save side against Game.exe: the members verbatim, the re-route with the counter as the load finds it, then the save writes the loaded person and re-routes again.</summary>
	[TestMethod]
	public void TheLoadAndSaveSemanticsFollowGameExe()
	{
		foreach ( var golden in Cases )
		{
			var grid = Grid( golden );
			var walker = new Walker();
			var block = ThemeParkIncPersonRecord.Read( Convert.FromHexString( golden.Block ) );
			ThemeParkIncNativeWalk.Load( grid, walker, block );
			Assert.AreEqual( (golden.LoadCounter, golden.LoadMark, golden.LoadRecord), (grid.Cells.StampCounter, walker.StrandedTime, Sha( walker.Walk.ToRecord().Raw )), $"{golden.Name}: the load" );
			Assert.AreEqual( (block.AccurateDestX, block.AccurateDestY), ((ushort)walker.AccurateDestX, (ushort)walker.AccurateDestY), $"{golden.Name}: the re-route keeps the words" );
			var written = ThemeParkIncNativeWalk.SaveAndReRoute( grid, walker, block );
			Assert.AreEqual( golden.Written, Sha( written.Write() ), $"{golden.Name}: the save writes the loaded person" );
			Assert.AreEqual( (golden.SaveCounter, golden.SaveMark, golden.SaveRecord), (grid.Cells.StampCounter, walker.StrandedTime, Sha( walker.Walk.ToRecord().Raw )), $"{golden.Name}: the save's re-route" );
		}
	}

	/// <summary>The cases reach a fresh process and a running one, a held mark, a cleared one, a re-route that finds a way and one that does not.</summary>
	[TestMethod]
	public void TheGoldenCasesReachEveryBranch()
	{
		Assert.IsTrue( Cases.Any( golden => golden.Counter == 0 ) && Cases.Any( golden => golden.Counter != 0 ) );
		var marks = Cases.Select( golden => (golden, mark: ThemeParkIncPersonRecord.Read( Convert.FromHexString( golden.Block ) ).StrandedTime) ).ToArray();
		Assert.IsTrue( marks.Any( pair => pair.mark != 0 && pair.golden.LoadMark == pair.mark ), "a held mark" );
		Assert.IsTrue( marks.Any( pair => pair.mark != 0 && pair.golden.LoadMark == 0 ), "a cleared mark" );
		Assert.IsTrue( Cases.Any( golden => golden.LoadCounter == golden.Counter + 2 ), "a re-route that sets a walk up (the counter raised twice)" );
		Assert.IsTrue( Cases.Any( golden => golden.LoadCounter == golden.Counter + 1 && golden.LoadMark == 0 ), "a re-route that finds no way" );
	}

	/// <summary>A load with a fresh process starts the counter at 0 and zeroes the block stamps (the map load 0x5D7126) before the persons are read and re-routed in order.</summary>
	[TestMethod]
	public void ALoadOfAFreshProcessStartsTheCounterAtZero()
	{
		var golden = Cases.First( golden => golden.Name == "walking-running" );
		var grid = Grid( golden );
		var first = new Walker();
		var second = new Walker();
		var block = ThemeParkIncPersonRecord.Read( Convert.FromHexString( golden.Block ) );
		ThemeParkIncNativeWalk.LoadAll( grid, [(first, block), (second, block)], freshProcess: true );
		Assert.AreEqual( 0, grid.Cells.BlockStamp( 2, 2 ), "the map load zeroed the stamps" );
		var fresh = Cases.First( other => other.Name == "walking-fresh" );
		Assert.AreEqual( fresh.LoadRecord, Sha( first.Walk.ToRecord().Raw ), "the first person re-routes as in a fresh process" );
		Assert.AreEqual( fresh.LoadCounter + 2, grid.Cells.StampCounter, "the second person counts on from the first (one request, one set-up)" );
		var running = Grid( golden );
		ThemeParkIncNativeWalk.LoadAll( running, [(new Walker(), block)], freshProcess: false );
		Assert.AreEqual( golden.Counter + 2, running.Cells.StampCounter, "a load inside a running game keeps the counter" );
	}

	/// <summary>The block's layout is the person serializer's (the guest and staff bodies' first 396 bytes, <see cref="ThemeParkIncBodyLayout"/>), and reading then writing gives the bytes back (T3, T4's C# half).</summary>
	[TestMethod]
	public void ThePersonBlockIsTheBodiesFirstPartAndRoundTrips()
	{
		var random = new Random( 5 );
		foreach ( var model in new[] { ThemeParkIncThingModel.Guest, ThemeParkIncThingModel.Mechanic, ThemeParkIncThingModel.Gardener } )
		{
			var body = ThemeParkIncThingBodyTests.Body( model, 0, 0, 0, 0, 0, 0 );
			var fields = new ThemeParkIncBody( model, body ).Layout.Fields( body ).Where( field => field.Offset < ThemeParkIncPersonRecord.Bytes ).ToList();
			Assert.AreEqual( ThemeParkIncPersonRecord.Bytes, fields[^1].Offset + fields[^1].Size, $"{model}: the person part ends at 396" );
			var offsets = fields.ToDictionary( field => field.Path, field => field.Offset );
			Assert.AreEqual( (20, 22, 24, 26, 29, 33), (offsets["mAccurateDestX"], offsets["mAccurateDestY"], offsets["mAdjustorSpeed"], offsets["mBaseSpeed"], offsets["mESPSprite"], offsets["mLastRecordedMapId"]) );
			Assert.AreEqual( ThemeParkIncPersonRecord.NavigatorOffset, offsets["force"], "the navigator follows" );
			Assert.AreEqual( (212, 216, 220, 224, 228, 230, 234), (offsets["mPreviousSpeed"], offsets["mPreviousX"], offsets["mPreviousY"], offsets["mStrandedTime"], offsets["mPurposeSpeed"],
				offsets["mSetDestSuccessfully"], offsets["mSpriteAngle"]) );
			var bytes = new byte[ThemeParkIncPersonRecord.Bytes];
			random.NextBytes( bytes );
			CollectionAssert.AreEqual( bytes, ThemeParkIncPersonRecord.Read( bytes ).Write(), $"{model}: read then write" );
		}
	}

	/// <summary>A native park of the native guests' rig with six arrivals, run until they walk, wander and queue.</summary>
	private static GuestSimulation PlayedPark()
	{
		var (simulation, _, _) = ThemeParkIncNativeGuestTests.Rig();
		for ( var arrival = 0; arrival < 6; arrival++ )
		{
			simulation.SpawnArrival();
			ThemeParkIncNativeGuestTests.Turns( simulation, 25 );
		}
		return simulation;
	}

	private static GuestSimulation Restored( GuestSimulationSnapshot saved )
	{
		var (simulation, _, _) = ThemeParkIncNativeGuestTests.Rig();
		simulation.RestoreSnapshot( saved );
		return simulation;
	}

	/// <summary>Both parks go on turn by turn with the same state hash.</summary>
	private static void GoOnAlike( GuestSimulation expected, GuestSimulation actual, int turns, string what )
	{
		for ( var turn = 0; turn < turns; turn++ )
		{
			ThemeParkIncNativeGuestTests.Turns( expected, 1 );
			ThemeParkIncNativeGuestTests.Turns( actual, 1 );
			Assert.AreEqual( expected.ComputeStateHash(), actual.ComputeStateHash(), $"{what}: turn {turn}" );
		}
	}

	/// <summary>
	/// OpenTPW's own Theme Park Inc resume (the JSON continuation until the park writer's N10) restores native guests verbatim from the native walk storage alone: no walk field of OpenTPW's own kind is
	/// written, a restored park saves the same bytes again (T3), has the same state hash and goes on exactly as the live one.
	/// </summary>
	[TestMethod]
	public void NativeGuestsResumeVerbatimFromTheNativeWalkStorage()
	{
		var live = PlayedPark();
		var walking = 0;
		for ( var point = 0; point < 5; point++ )
		{
			ThemeParkIncNativeGuestTests.Turns( live, 37 );
			var json = ParkResumeJson.Serialize( live.CaptureSnapshot() );
			var saved = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( json )!;
			Assert.AreEqual( GuestSimulationSnapshot.CurrentThemeParkIncWalkStorage, saved.ThemeParkIncWalkStorage );
			Assert.IsTrue( saved.Guests.Length > 0 && saved.Guests.All( guest => guest.PersonWalk != null && !guest.HasAnyOwnWalkField ), "only the native walk storage is written" );
			walking += live.Guests.Count( guest => !guest.Navigation.Arrived );
			var resumed = Restored( saved );
			CollectionAssert.AreEqual( json, ParkResumeJson.Serialize( resumed.CaptureSnapshot() ), $"save {point}: a restored park saves the same bytes" );
			Assert.AreEqual( live.ComputeStateHash(), resumed.ComputeStateHash(), $"save {point}: the same state" );
			GoOnAlike( live, resumed, 23, $"save {point}" );
		}
		Assert.IsTrue( walking > 0, "the saves caught guests mid-walk" );
	}

	/// <summary>
	/// T2. The original's load semantics on a played park: each native guest's person block as the save half writes it (<see cref="ThemeParkIncNativeWalk.Write"/>), loaded in a fresh process (the counter 0,
	/// the block stamps zeroed, each guest re-routed in order). Loading the blocks onto guests whose walk and person words were blanked gives the park that loading them onto the restored guests gives,
	/// turn by turn: the blocks carry the whole walk. The counter counts the re-routes from 0.
	/// </summary>
	[TestMethod]
	public void APlayedParkLoadsWithTheOriginalsSemanticsFromItsPersonBlocks()
	{
		var live = PlayedPark();
		ThemeParkIncNativeGuestTests.Turns( live, 41 );
		var saved = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( live.CaptureSnapshot() ) )!;
		var blocks = live.Guests.Select( guest => ThemeParkIncNativeWalk.Write( guest ) ).ToArray();
		var reference = Restored( saved );
		ThemeParkIncNativeWalk.LoadAll( reference.Grid, reference.Guests.Zip( blocks, ( guest, block ) => ((IThemeParkIncWalker)guest, block) ), freshProcess: true );
		var reroutes = reference.Grid.Cells.StampCounter;
		Assert.IsTrue( reroutes >= blocks.Length && reroutes <= 2 * blocks.Length, "a fresh process counts each re-route from 0 (once, or twice with a set-up)" );
		var subject = Restored( saved );
		foreach ( IThemeParkIncWalker guest in subject.Guests )
		{
			guest.Walk.RestoreNativeRecord( new GuestNavigation().ToRecord().Raw );
			(guest.AccurateDestX, guest.AccurateDestY, guest.StrandedTime, guest.PreviousX, guest.PreviousY, guest.SpriteAngle) = (0, 0, 0, 0, 0, 0);
		}
		ThemeParkIncNativeWalk.LoadAll( subject.Grid, subject.Guests.Zip( blocks, ( guest, block ) => ((IThemeParkIncWalker)guest, block) ), freshProcess: true );
		Assert.AreEqual( reroutes, subject.Grid.Cells.StampCounter );
		Assert.AreEqual( reference.ComputeStateHash(), subject.ComputeStateHash(), "the blocks restore the walk" );
		GoOnAlike( reference, subject, 40, "after the native load" );
	}

	/// <summary>
	/// T1. The Science prebuilt holds no guest and no staff member (no person block to import and export: its navigators are the empty set); a person block a live walker writes, appended to the
	/// prebuilt as a guest and as a mechanic, is read back by the save reader and imported onto a walker, and exported again byte for byte.
	/// </summary>
	[TestMethod]
	public void ThePrebuiltHoldsNoPersonAndAnAppendedPersonImportsAndExportsUnchanged()
	{
		var prebuilt = ThemeParkIncThingBodyTests.Prebuilt();
		var world = ThemeParkIncThingBodyTests.World( prebuilt );
		var persons = new[] { ThemeParkIncThingModel.Guest, ThemeParkIncThingModel.Mechanic, ThemeParkIncThingModel.Handyman, ThemeParkIncThingModel.Entertainer, ThemeParkIncThingModel.Guard,
			ThemeParkIncThingModel.Researcher, ThemeParkIncThingModel.Gardener };
		Assert.AreEqual( 0, world.Things.Count( thing => persons.Contains( thing.Model ) ), "the prebuilt Science park holds no person" );
		var golden = Cases.First( golden => golden.Name == "leaving-mask" );
		var block = Convert.FromHexString( golden.Block );
		var free = Enumerable.Range( 1, 4000 ).Where( id => world.Things.All( thing => thing.Id != id ) ).Take( 2 ).ToArray();
		var guestBody = ThemeParkIncThingBodyTests.Body( ThemeParkIncThingModel.Guest );
		var staffBody = ThemeParkIncThingBodyTests.Body( ThemeParkIncThingModel.Mechanic, 0, 0, 0, 0, 0, 0 );
		block.CopyTo( guestBody, 0 );
		block.CopyTo( staffBody, 0 );
		var payload = ThemeParkIncThingBodyTests.WithThings( prebuilt, (free[0], ThemeParkIncThingModel.Guest, guestBody), (free[1], ThemeParkIncThingModel.Mechanic, staffBody) );
		var parsed = ThemeParkIncThingBodyTests.World( payload );
		foreach ( var thing in parsed.Things.Where( thing => persons.Contains( thing.Model ) ) )
		{
			var imported = ThemeParkIncPersonRecord.Read( parsed.BodyOf( thing ).Span[..ThemeParkIncPersonRecord.Bytes] );
			var walker = new Walker();
			ThemeParkIncNativeWalk.Apply( walker, imported );
			var exported = ThemeParkIncNativeWalk.Write( walker, imported ).Write();
			CollectionAssert.AreEqual( block, exported, $"{thing.Model}: import then export gives the block back" );
		}
		Assert.AreEqual( 2, parsed.Things.Count( thing => persons.Contains( thing.Model ) ) );
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The coaster presentation on the original PC data (inconclusive without OPENTPW_GAME_PATH): every theme's coaster wad has cross sections, textures, pylon and car models that pose.</summary>
[TestClass]
public class CoasterPresentationAssetTests
{
	private BaseFileSystem previousFileSystem = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		previousLog = Log;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original coaster archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	// Other tests read the global FileSystem, so the original game data must not outlive the test.
	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem;
		Log = previousLog;
	}

	private static IEnumerable<ObjectCatalogEntry> PcCoasters() =>
		ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries ).Where( entry => !entry.IsBonus && CoasterDefinition.IsCoaster( entry.Settings ) );

	private static ModelFile? Find( ObjectCatalogEntry entry, string stem )
	{
		var path = entry.FileSystem.GetFiles( entry.ArchivePath ).FirstOrDefault( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase )
			&& string.Equals( Path.GetFileNameWithoutExtension( file ), stem, StringComparison.OrdinalIgnoreCase ) );
		return path == null ? null : ObjectAssets.LoadModel( entry.FileSystem, path );
	}

	/// <summary>Every coaster has cross sections whose edges name real points and whose track textures resolve in its archive.</summary>
	[TestMethod]
	public void EveryCoasterHasCrossSectionsAndResolvableTrackTextures()
	{
		var count = 0;
		foreach ( var entry in PcCoasters() )
		{
			count++;
			var sections = CoasterCrossSections.FromSettings( entry.CoasterSettings );
			Assert.IsFalse( sections.IsEmpty, $"{entry.InfoId} has a cross section" );
			Assert.IsTrue( sections.Textures.Count >= 1, $"{entry.InfoId} has track textures" );
			foreach ( var texture in sections.Textures )
				Assert.IsNotNull( ObjectAssets.ResolveTexture( entry, texture.FileName ), $"{entry.InfoId}: track texture {texture.FileName}" );
			for ( var index = 0; index < sections.Sections.Count; index++ )
			{
				var (points, edges) = sections.Resolve( index );
				Assert.IsTrue( points.Count >= 2 && edges.Count >= 1, $"{entry.InfoId} section {index} has points and edges" );
				foreach ( var edge in edges )
				{
					Assert.IsTrue( edge.From < points.Count && edge.To < points.Count, $"{entry.InfoId} section {index}: an edge names a missing point" );
					Assert.IsTrue( edge.Texture < sections.Textures.Count, $"{entry.InfoId} section {index}: an edge names a missing texture" );
				}
			}
			Assert.IsTrue( sections.SelectSection( 0, winch: false ) >= 0, $"{entry.InfoId} selects a section" );
		}
		Assert.AreEqual( 12, count );
	}

	/// <summary>
	/// A non-bend car type plays its model's animation id 5 (the member <c>&lt;model&gt;m</c>: the mine cart's <c>cartm</c> and the Candy coaster's <c>carm</c>) at the type's factor times the train's speed;
	/// the bend cars of the two flumes (the Croc and Maggot cars) do not, and the coasters whose archive has no such member have none.
	/// </summary>
	[TestMethod]
	public void NonBendCarsPlayTheirWheelClipAtTheTrainsSpeed()
	{
		var withClip = new[] { 1180, 2180, 4190 };
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromCatalogEntry( entry );
			using var assets = new CoasterAssets( entry, definition );
			var rig = assets.GetCar( definition.CarTypes.CentreIndex )!;
			var bend = definition.CarTypes.Types[definition.CarTypes.CentreIndex].Bend;
			Assert.AreEqual( entry.InfoId is 1185 or 2185, bend, $"{entry.InfoId}: the flumes' cars bend" );
			Assert.AreEqual( withClip.Contains( entry.InfoId ), rig.WheelClip != null, $"{entry.InfoId}: the wheel clip" );
			if ( rig.WheelClip is not { } clip )
				continue;
			// the PC clips animate the wheel meshes' texture coordinates (UV tracks, no node tracks): four wheel parts move their UV, the body keeps its own
			var wheels = Enumerable.Range( 0, rig.Parts.Count ).Where( rig.AnimatesUv ).ToList();
			Assert.AreEqual( 4, wheels.Count, $"{entry.InfoId}: four wheels" );
			Assert.IsTrue( wheels.All( index => rig.Parts[index].MeshName.StartsWith( "mc_wheel", StringComparison.OrdinalIgnoreCase ) ), $"{entry.InfoId}: the wheels" );
			Assert.IsTrue( rig.WheelPartMatrices( 3 ).All( matrix => matrix == System.Numerics.Matrix4x4.Identity ), $"{entry.InfoId}: no node moves" );
			foreach ( var wheel in wheels )
			{
				var start = rig.WheelVertices( wheel, 0 )!;
				var turned = rig.WheelVertices( wheel, clip.Duration / 2f )!;
				Assert.AreEqual( rig.Parts[wheel].Vertices.Length, turned.Length );
				Assert.IsTrue( Enumerable.Range( 0, turned.Length ).Any( corner => turned[corner].TexCoords != start[corner].TexCoords ), $"{entry.InfoId}: wheel {wheel} turns" );
				Assert.IsTrue( Enumerable.Range( 0, turned.Length ).All( corner => turned[corner].Position == rig.Parts[wheel].Vertices[corner].Position ), $"{entry.InfoId}: only the coordinates move" );
			}
		}
	}

	/// <summary>The existing wheel UV tracks take the slot-owned epoch even when their assets first load after setup.</summary>
	[TestMethod]
	public void DelayedWheelAssetsUseTheSamePhaseAsContinuouslyPresentedAssets()
	{
		var clips = 0;
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromCatalogEntry( entry );
			using var visibleAssets = new CoasterAssets( entry, definition );
			var visible = visibleAssets.GetCar( definition.CarTypes.CentreIndex )!;
			if ( visible.WheelClip is not { } clip )
				continue;
			clips++;
			var ride = CoasterPresentationTests.WheelRide();
			var renderer = CoasterPresentationTests.WheelRenderer( ride );
			try
			{
				var wheels = Enumerable.Range( 0, visible.Parts.Count ).Where( visible.AnimatesUv ).ToArray();
				Assert.IsTrue( wheels.Length > 0 );
				foreach ( var wheel in wheels )
					Assert.IsNotNull( visible.WheelVertices( wheel, renderer.WheelTick( ride, 0, clip.Duration ) ) );
				ride.Advance( 1.0 );
				ride.Trains[0].Speed = clip.Duration / 6f;
				var tick = renderer.WheelTick( ride, 0, clip.Duration );
				Assert.AreEqual( clip.Duration / 2f, tick, 1e-4f, $"{entry.InfoId}: 1,000 ms since slot setup reaches half the clip" );
				using var delayedAssets = new CoasterAssets( entry, definition );
				var delayed = delayedAssets.GetCar( definition.CarTypes.CentreIndex )!;
				foreach ( var wheel in wheels )
				{
					var expected = visible.WheelVertices( wheel, tick )!;
					var actual = delayed.WheelVertices( wheel, renderer.WheelTick( ride, 0, clip.Duration ) )!;
					CollectionAssert.AreEqual( expected.Select( vertex => vertex.TexCoords ).ToArray(), actual.Select( vertex => vertex.TexCoords ).ToArray(),
						$"{entry.InfoId}: fresh assets for wheel {wheel} do not restart its phase" );
				}
			}
			finally
			{
				renderer.Delete();
			}
		}
		Assert.AreEqual( 3, clips, "the three non-bend coaster types with wheel clips" );
	}

	/// <summary>
	/// The car's root node pose replaces the model's root rest matrix (<c>0x43d8f0</c>), so every car's camera seat looks along the car's +Z, the way the car runs, whether or not its model's root is
	/// turned half a circle (the mine carts', the Candy cars' and the caterpillar's are).
	/// </summary>
	[TestMethod]
	public void EveryCarsCameraSeatLooksAlongTheTrackOnceTheRootPoseReplacesTheRootRest()
	{
		var turnedRoots = 0;
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromCatalogEntry( entry );
			using var assets = new CoasterAssets( entry, definition );
			var rig = assets.GetCar( definition.CarTypes.CentreIndex )!;
			var root = rig.Model.Nodes[rig.Model.RootNodeIndex].Transform;
			turnedRoots += root.M33 < 0 ? 1 : 0;
			var seat = rig.CameraSeat ?? throw new AssertFailedException( $"{entry.InfoId} has a camera seat" );
			Assert.IsTrue( seat.M33 > 0.99f, $"{entry.InfoId}: the seat looks along +Z of the car (M33 {seat.M33})" );
			Assert.IsTrue( seat.M22 > 0.99f, $"{entry.InfoId}: up stays up" );
		}
		Assert.AreEqual( 5, turnedRoots, "the mine carts (1180, 2180, 3190), the Candy coaster (4190) and the caterpillar (4185) have a root turned half a circle" );
	}

	/// <summary>The two flumes' boats bend: their parts draw from vertices of their own, which the bend tables move (the elevation lifts the whole boat on a straight track).</summary>
	[TestMethod]
	public void TheFlumeBoatsBendTheirVerticesAlongTheTrack()
	{
		foreach ( var entry in PcCoasters().Where( entry => entry.InfoId is 1185 or 2185 ) )
		{
			var definition = CoasterDefinition.FromCatalogEntry( entry );
			using var assets = new CoasterAssets( entry, definition );
			var rig = assets.GetCar( definition.CarTypes.CentreIndex )!;
			Assert.IsTrue( rig.Bend, $"{entry.InfoId}: bBendCar" );
			Assert.IsNull( rig.WheelClip, $"{entry.InfoId}: a bend car plays no wheel clip" );
			Assert.IsTrue( definition.BendElevationLow > 0 || definition.BendElevationHigh > 0, $"{entry.InfoId}: the flume names its bend elevation ({definition.BendElevationLow}, {definition.BendElevationHigh})" );
			var samples = Enumerable.Range( 0, 20 ).Select( index => new CoasterSample { Position = new System.Numerics.Vector3( 0, 4, index * 3f ) } ).ToArray();
			var metrics = definition.CarTypeMetrics( definition.CarTypes.CentreIndex );
			var tables = CoasterCarBend.Tables( samples, 3f, definition.BendElevationLow, definition.BendElevationHigh, metrics.RearExtent, metrics.FrontExtent, System.Numerics.Vector3.UnitZ, 10.5f );
			for ( var part = 0; part < rig.Parts.Count; part++ )
			{
				Assert.IsTrue( rig.IsDynamic( part ), $"{entry.InfoId}: part {part} has a model of its own" );
				var bent = rig.BendVertices( part, tables, 3f )!;
				Assert.AreEqual( rig.Parts[part].Vertices.Length, bent.Length );
				for ( var corner = 0; corner < bent.Length; corner++ )
				{
					var rest = rig.Parts[part].Vertices[corner].Position;
					Assert.AreEqual( rest.X, bent[corner].Position.X, 1e-2f, $"{entry.InfoId}: straight track keeps x" );
					Assert.AreEqual( rest.Y, bent[corner].Position.Y, 1e-2f, $"{entry.InfoId}: straight track keeps the forward axis" );
					Assert.AreEqual( rest.Z + definition.BendElevationLow, bent[corner].Position.Z, 0.05f, $"{entry.InfoId}: lifted by the elevation (engine up is Z)" );
				}
			}
		}
	}

	/// <summary>The clip frame is the milliseconds since the start times speed (0.1 × the train's) times 0.03, wrapped into the clip.</summary>
	[TestMethod]
	public void TheWheelFrameIsTheElapsedTimeTimesTheScaledTrainSpeed()
	{
		Assert.AreEqual( 0f, CoasterCarRig.WheelTick( 5000, 0f, 20 ), "a train at rest holds the first frame" );
		Assert.AreEqual( 1000 * (0.1f * 10f) * 0.03f % 20, CoasterCarRig.WheelTick( 1000, 10f, 20 ), 1e-4f, "one second at speed 10: 30 × 0.1 × 10 = 30 ticks, wrapped" );
		Assert.AreEqual( 2000 * (0.1f * 4f) * 0.03f, CoasterCarRig.WheelTick( 2000, 4f, 90 ), 1e-4f );
		Assert.AreEqual( 0f, CoasterCarRig.WheelTick( 100, 5f, 0 ), "no clip, no frame" );
	}

	/// <summary>The two flume sections: climbing is the dry section, flat track the wet one, both with the same points.</summary>
	[TestMethod]
	public void TheFlumesPickTheDrySectionWhenClimbing()
	{
		var flume = PcCoasters().Single( entry => entry.InfoId == 1185 );
		var sections = CoasterCrossSections.FromSettings( flume.CoasterSettings );
		var dry = sections.SelectSection( -0.3f, winch: false );
		var wet = sections.SelectSection( 0, winch: false );
		Assert.AreNotEqual( dry, wet );
		Assert.AreEqual( sections.Sections[dry].PointBlock, sections.Sections[wet].PointBlock );
		Assert.AreNotEqual( sections.Resolve( dry ).Edges.Select( edge => edge.Texture ).Max(), sections.Resolve( wet ).Edges.Select( edge => edge.Texture ).Max() );
		Assert.IsTrue( sections.Textures.Any( texture => texture.ScrollRate != 0 ), "the flume's water and ratchet scroll" );
	}

	/// <summary>
	/// Every pylon poses with its five clips (<c>0x430ca0</c>): the loft clip raises its tag to h0 + t(h1 − h0) and stops at the loft's ends, the incline
	/// and bank clips tilt the tag, and it has drawable parts.
	/// </summary>
	[TestMethod]
	public void EveryPylonPosesItsClipsToTheNodeValues()
	{
		var inclines = 0;
		var banks = 0;
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromEntry( entry );
			foreach ( var loop in new[] { false, true } )
			{
				if ( loop && !definition.HasLoopPylon )
					continue;
				var control = definition.Pylon( loop );
				var name = control.MeshName;
				var geometry = Find( entry, name );
				if ( geometry == null )
				{
					Assert.IsTrue( loop && definition.IsTrough, $"{entry.InfoId}: the pylon model {name} exists" );
					continue;
				}
				var rig = new CoasterPylonRig( geometry, Find( entry, name + "L" )?.Clip, (Find( entry, name + "R" ) ?? Find( entry, name + "R1" ))?.Clip, control,
					Find( entry, name + "I" )?.Clip, Find( entry, name + "B" )?.Clip, Find( entry, name + "R2" )?.Clip );
				// Some loop pylons (the Space themes') carry no tag motion in their loft clip.
				Assert.IsTrue( rig.HasLoft || loop, $"{entry.InfoId} {name}: the loft clip raises the track tag ({rig.TagLow}..{rig.TagHigh})" );
				if ( !rig.HasLoft )
				{
					Assert.IsTrue( rig.Pose( new CoasterPylonPose( 0.2f ) ).Count > 0 );
					continue;
				}
				float Tag( CoasterPylonPose pose ) => rig.NodeMatrix( rig.TagNode, pose ).Translation.Y;
				foreach ( var t in new[] { 0f, 0.5f, 1f } )
					Assert.AreEqual( rig.TagLow + t * (rig.TagHigh - rig.TagLow), Tag( new CoasterPylonPose( t ) ), 1e-2f, $"{entry.InfoId} {name}: the tag stands at h0 + t(h1 − h0) for t {t}" );
				Assert.AreEqual( rig.TagHigh, Tag( new CoasterPylonPose( 1.3f ) ), 1e-3f, $"{entry.InfoId} {name}: a parameter above 1 stops the pylon at the loft's top (0x430ca0)" );
				Assert.AreEqual( rig.TagLow, Tag( new CoasterPylonPose( -0.4f ) ), 1e-3f, "and one below 0 at its bottom" );
				var level = rig.NodeMatrix( rig.TagNode, new CoasterPylonPose( 0.5f ) );
				if ( rig.HasIncline )
				{
					var tilted = rig.NodeMatrix( rig.TagNode, new CoasterPylonPose( 0.5f, Pitch: 0.4f ) );
					Assert.IsTrue( MathF.Abs( tilted.M32 - level.M32 ) > 0.05f, $"{entry.InfoId} {name}: the incline clip tips the tag's forward axis" );
					inclines++;
				}
				if ( rig.HasBank )
				{
					var banked = rig.NodeMatrix( rig.TagNode, new CoasterPylonPose( 0.5f, Bank: 0.3f ) );
					Assert.IsTrue( MathF.Abs( banked.M12 - level.M12 ) > 0.05f, $"{entry.InfoId} {name}: the bank clip rolls the tag's side axis" );
					banks++;
				}
				foreach ( var t in new[] { 0f, 0.5f, 1f } )
				{
					var posed = rig.Pose( new CoasterPylonPose( t, 0.7f ) );
					var height = rig.TagLow + t * (rig.TagHigh - rig.TagLow);
					// A lone pylon hides its stack parts 1 (the bridge a pylon above stands on) and 3 (the connector onto a pylon below), 0x432df0.
					foreach ( var value in new[] { 1, 3 } )
						Assert.IsFalse( rig.StackPart( value ) >= 0 && posed.Any( part => rig.Model.Meshes.Any( mesh => mesh.Name == part.MeshName && mesh.NodeIndex == rig.StackPart( value ) ) ), $"{entry.InfoId} {name}: stack part {value} is hidden" );
					if ( !loop && entry.InfoId is 1180 or 2180 or 3190 or 4180 or 4190 )
						Assert.AreEqual( height, posed.SelectMany( part => part.Vertices ).Max( vertex => vertex.Position.GetSystemVector3().Z ), 1f, $"{entry.InfoId} {name}: the pylon's top meets the node at {height}" );
					Assert.IsTrue( rig.Pose( new CoasterPylonPose( t, 0.7f ), new CoasterStackPlace( Bottom: true, Top: false ) ).Count >= posed.Count - (rig.StackPart( 2 ) >= 0 ? 1 : 0), "a lower node of a stack shows part 1 (the bridge) and hides part 2" );
				}
				var parts = rig.Pose( new CoasterPylonPose( 0.5f, 1.0f ) );
				Assert.IsTrue( parts.Count > 0 && parts.All( part => part.Vertices.Length > 0 && part.Indices.Length % 3 == 0 ) );
				var top = parts.SelectMany( part => part.Vertices ).Max( vertex => vertex.Position.GetSystemVector3().Z );
				var tall = rig.Pose( new CoasterPylonPose( 1f ) ).SelectMany( part => part.Vertices ).Max( vertex => vertex.Position.GetSystemVector3().Z );
				Assert.IsTrue( tall > top + 1, $"{entry.InfoId} {name}: a higher node gives a taller pylon" );
			}
		}
		Assert.IsTrue( inclines >= 10 && banks >= 5, $"{inclines} incline and {banks} bank clips were posed" );
	}

	/// <summary>
	/// The stack parts (<c>0x430590</c>, <c>0x432df0</c>): the Gorilla Thrilla pylon's roots (part 4) stand only on the ground and its stacker (part 3) only on a pylon below; the
	/// bridge (part 1) of the Temple Of Gloom pylon is drawn only under a pylon with a model.
	/// </summary>
	[TestMethod]
	public void StackPartsShowByThePylonsPlaceInItsStack()
	{
		CoasterPylonRig Rig( int id )
		{
			var entry = PcCoasters().Single( candidate => candidate.InfoId == id );
			var control = CoasterDefinition.FromEntry( entry ).StandardPylon;
			var name = control.MeshName;
			return new CoasterPylonRig( Find( entry, name )!, Find( entry, name + "L" )?.Clip, (Find( entry, name + "R" ) ?? Find( entry, name + "R1" ))?.Clip, control,
				Find( entry, name + "I" )?.Clip, Find( entry, name + "B" )?.Clip, Find( entry, name + "R2" )?.Clip );
		}
		string[] Drawn( CoasterPylonRig rig, CoasterStackPlace place ) => rig.Pose( new CoasterPylonPose( 0.5f ), place ).Select( part => part.MeshName ).Distinct().ToArray();
		var gorilla = Rig( 1190 );
		Assert.AreEqual( "ROOTS", gorilla.Model.Nodes[gorilla.StackPart( 4 )].Name );
		Assert.AreEqual( "STACKER", gorilla.Model.Nodes[gorilla.StackPart( 3 )].Name );
		CollectionAssert.Contains( Drawn( gorilla, CoasterStackPlace.Alone ), "ROOTS" );
		CollectionAssert.DoesNotContain( Drawn( gorilla, CoasterStackPlace.Alone ), "STACKER" );
		var stacked = new CoasterStackPlace( Bottom: false, Top: true, CarriesModel: false );
		CollectionAssert.DoesNotContain( Drawn( gorilla, stacked ), "ROOTS" );
		CollectionAssert.Contains( Drawn( gorilla, stacked ), "STACKER" );
		var temple = Rig( 1180 );
		Assert.AreEqual( "mc_bridge", temple.Model.Nodes[temple.StackPart( 1 )].Name );
		CollectionAssert.DoesNotContain( Drawn( temple, CoasterStackPlace.Alone ), "mc_bridge", "the top of a stack hides part 1" );
		CollectionAssert.Contains( Drawn( temple, new CoasterStackPlace( Bottom: true, Top: false, CarriesModel: true ) ), "mc_bridge", "under a pylon it shows" );
		CollectionAssert.DoesNotContain( Drawn( temple, new CoasterStackPlace( Bottom: true, Top: false, CarriesModel: false ) ), "mc_bridge", "under the cursor's model-less node it stays as the last pass left it" );
	}

	/// <summary>
	/// The pylon's ground node (model +0x2c, <c>0x46a5a0</c>): a bottom node conforms its four ground vertices to the terrain's corners (<c>0x469940</c>) and a node stacked above hides it unless
	/// the pylon has flag 0x10 (<c>0x432df0</c>); no PC coaster sets that flag, and the part's flag hides only its own mesh.
	/// </summary>
	[TestMethod]
	public void ThePylonsGroundNodeFollowsTheTerrainAndHidesOnAStackedNode()
	{
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromEntry( entry );
			foreach ( var control in new[] { definition.StandardPylon, definition.LoopPylon } )
			{
				if ( string.IsNullOrEmpty( control.MeshName ) )
					continue;
				var name = control.MeshName;
				var rig = new CoasterPylonRig( Find( entry, name )!, Find( entry, name + "L" )?.Clip, (Find( entry, name + "R" ) ?? Find( entry, name + "R1" ))?.Clip, control,
					Find( entry, name + "I" )?.Clip, Find( entry, name + "B" )?.Clip, Find( entry, name + "R2" )?.Clip );
				Assert.IsTrue( rig.GroundNode >= 0, $"{entry.InfoId} {name} has a ground node" );
				Assert.AreEqual( 0, (int)(control.Flags & CoasterPylonFlags.KeepBaseOnStack), $"{entry.InfoId} {name} does not keep its base on a stack" );
				var groundMesh = rig.Model.Meshes[rig.Model.Nodes[rig.GroundNode].MeshIndex].Name;
				var pose = new CoasterPylonPose( 0.5f );
				var bottom = rig.Pose( pose, new CoasterStackPlace( Bottom: true, Top: true ) );
				Assert.IsTrue( bottom.Any( part => part.MeshName == groundMesh ), $"{entry.InfoId} {name} shows the ground node on the ground" );
				Assert.IsFalse( rig.Pose( pose, new CoasterStackPlace( Bottom: false, Top: true ) ).Any( part => part.MeshName == groundMesh ), $"{entry.InfoId} {name} hides it on a stacked node" );
				var flat = rig.Pose( pose, new CoasterStackPlace( Bottom: true, Top: true ), ( _, _ ) => 0f );
				var sloped = rig.Pose( pose, new CoasterStackPlace( Bottom: true, Top: true ), ( column, row ) => column == 1 && row == 1 ? 5f : 0f );
				float Peak( IReadOnlyList<CoasterModelPart> parts, int axis ) => parts.Where( part => part.MeshName == groundMesh ).SelectMany( part => part.Vertices )
					.Max( vertex => axis == 0 ? vertex.Position.X : axis == 1 ? vertex.Position.Y : vertex.Position.Z );
				var raised = Enumerable.Range( 0, 3 ).Count( axis => MathF.Abs( Peak( sloped, axis ) - Peak( flat, axis ) - 5f ) < 0.01f );
				Assert.AreEqual( 1, raised, $"{entry.InfoId} {name}: the corner at (1, 1) takes its terrain height on exactly one axis" );
				var other = Enumerable.Range( 0, 3 ).Count( axis => MathF.Abs( Peak( sloped, axis ) - Peak( flat, axis ) ) > 0.01f );
				Assert.AreEqual( 1, other, $"{entry.InfoId} {name}: nothing else moves" );
			}
		}
	}

	/// <summary>Every car type loads, has extents along its length and a seat socket per head.</summary>
	[TestMethod]
	public void EveryCarTypeLoadsWithExtentsAndSeats()
	{
		foreach ( var entry in PcCoasters() )
		{
			var types = CoasterCarTypes.FromSettings( entry.CoasterSettings );
			Assert.IsTrue( types.Types.Count >= 1, $"{entry.InfoId} has a car type" );
			foreach ( var index in new[] { types.FrontIndex, types.CentreIndex, types.RearIndex }.Distinct() )
			{
				var type = types.Types[index];
				var geometry = Find( entry, type.MeshName );
				Assert.IsNotNull( geometry, $"{entry.InfoId}: car model {type.MeshName}" );
				var rig = new CoasterCarRig( geometry! );
				Assert.IsTrue( rig.Parts.Count > 0, $"{entry.InfoId} {type.MeshName} has mesh parts" );
				Assert.IsTrue( rig.FrontExtent + rig.RearExtent > 1, $"{entry.InfoId} {type.MeshName} has a length" );
				Assert.AreEqual( rig.HeadSockets, rig.Seats.Count, $"{entry.InfoId} {type.MeshName}: a socket per 0x80 record, in record order (0x4301f5)" );
				foreach ( var part in rig.Parts )
				{
					foreach ( var name in part.Textures )
						Assert.IsNotNull( ObjectAssets.ResolveTexture( entry, name ), $"{entry.InfoId} {type.MeshName}: texture {name}" );
				}
			}
		}
	}

	/// <summary>
	/// The pylons' loft clips carry texture-coordinate keys (record flag 0x10000) which the add mode adds to the stored coordinates (0x4745c0): the Jungle pylon's loft adds 14.985 to the V of its top
	/// corners, so its coordinates run past 1. Whether they wrap or clamp follows the shared texture's accumulated flags (0x575E10): a corner clamps only when no model of the ride wraps its texture.
	/// </summary>
	[TestMethod]
	public void ALoftedPylonsTextureCoordinatesRunPastOne()
	{
		var entry = PcCoasters().Single( entry => entry.InfoId == 1180 );
		var definition = CoasterDefinition.FromEntry( entry );
		using var assets = new CoasterAssets( entry, definition );
		var rig = assets.GetPylon( false )!;
		float MaxV( float loft ) => rig.Pose( new CoasterPylonPose( loft ) ).SelectMany( part => part.Vertices ).Max( vertex => vertex.TexCoords.Y );
		float MinV( float loft ) => rig.Pose( new CoasterPylonPose( loft ) ).SelectMany( part => part.Vertices ).Min( vertex => vertex.TexCoords.Y );
		Assert.IsTrue( MaxV( 1f ) - MaxV( 0f ) > 10f, $"the V of the top corners grows by the loft clip's key: {MaxV( 0f )} to {MaxV( 1f )}" );
		Assert.AreEqual( MinV( 0f ), MinV( 1f ), 1e-3f, "the bottom corners' keys stay at 0" );
		Assert.AreEqual( 14.985f, MaxV( 1f ) - MaxV( 0f ), 0.5f );
		foreach ( var part in rig.Pose( new CoasterPylonPose( 1f ) ) )
		{
			foreach ( var vertex in part.Vertices )
				Assert.AreEqual( !assets.WrapNames.Contains( part.Textures[vertex.TexIndex] ), (vertex.MatFlags & TextureSlotAddressing.ClampFlag) != 0, part.Textures[vertex.TexIndex] );
		}
	}

	/// <summary>
	/// The texture cache shares one object per (directory, name) and the wrap bits are never cleared (0x575E10, 0x575C60): where one pylon model of a ride wraps a texture its slot clamps in the other, the
	/// clamping model wraps too. The ride's own pylons: 4185 trunk03, 3180 S_RAIL_TRIANGLE, 2190 TESTWall (StdPylon 0x1 or LoopPylon 0x1, the other model 0x21 or 0x31) and 1190 A_TY_ON.
	/// </summary>
	[TestMethod]
	public void ASlotThatClampsWrapsWhenAnotherModelOfTheRideWrapsTheSameTexture()
	{
		foreach ( var (id, texture) in new[] { (4185, "trunk03"), (3180, "S_RAIL_TRIANGLE"), (2190, "TESTWall"), (1190, "A_TY_ON") } )
		{
			var entry = PcCoasters().Single( entry => entry.InfoId == id );
			var definition = CoasterDefinition.FromEntry( entry );
			using var assets = new CoasterAssets( entry, definition );
			Assert.IsTrue( assets.WrapNames.Contains( texture ), $"{id}: {texture} wraps in one model of the ride" );
			var seen = 0;
			foreach ( var loop in new[] { false, true } )
			{
				if ( assets.GetPylon( loop ) is not { } rig )
					continue;
				foreach ( var part in rig.Pose( new CoasterPylonPose( 0.5f ) ) )
				{
					for ( var slot = 0; slot < part.Textures.Length; slot++ )
					{
						if ( !string.Equals( part.Textures[slot], texture, StringComparison.OrdinalIgnoreCase ) )
							continue;
						foreach ( var vertex in part.Vertices.Where( vertex => vertex.TexIndex == slot ) )
						{
							Assert.AreEqual( 0u, vertex.MatFlags & TextureSlotAddressing.ClampFlag, $"{id} {texture}: wraps in both pylons" );
							seen++;
						}
					}
				}
			}
			Assert.IsTrue( seen > 0, $"{id}: a pylon draws {texture}" );
		}
	}
}

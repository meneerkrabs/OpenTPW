using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The coaster presentation without game data (docs/COASTERS.md): the cross sections of coaster.sam, their extrusion along the curve slices, the track under
/// construction, the pylon height fit and the car pose. The archive models are covered by CoasterPresentationAssetTests.
/// </summary>
[TestClass]
public class CoasterPresentationTests
{
	/// <summary>The scrolling textures move by the power factor × elapsed seconds (0x435adf..0x435b20): the same shift for every scrolling texture, wrapped, stopped by a breakdown.</summary>
	[TestMethod]
	public void ScrollingTexturesAdvanceByThePowerFactorTimesElapsedTime()
	{
		var phase = 0f;
		for ( var frame = 0; frame < 3; frame++ )
			phase = CoasterRenderer.AdvanceScroll( phase, 1f, 0.25f );
		Assert.AreEqual( 0.75f, phase, 1e-6f );
		Assert.AreEqual( 0.25f, CoasterRenderer.AdvanceScroll( phase, 1f, 0.5f ), 1e-6f, "wraps above 1" );
		Assert.AreEqual( phase, CoasterRenderer.AdvanceScroll( phase, 0f, 10f ), "mode 64: the power factor is 0 and the texture stands still" );
		Assert.AreEqual( 0.6f, CoasterRenderer.AdvanceScroll( 0.5f, 0.5f, 0.2f ), 1e-6f );
	}

	[TestInitialize]
	public void Init() => Log = new();

	/// <summary>A two-point, one-edge section with a winch variant: the minimum that shows points, edges, textures and the winch select.</summary>
	private static Dictionary<string, string> SectionSettings() => new( StringComparer.OrdinalIgnoreCase )
	{
		["asTextureData[0].pcTextureFilename"] = "\"rail.tga\"",
		["asTextureData[1].pcTextureFilename"] = "\"chain.tga\"",
		["asTextureData[1].fScrollRate"] = "1.0",
		["asCrossSectionPoints1[0].fX"] = "-2", ["asCrossSectionPoints1[0].fY"] = "0.5", ["asCrossSectionPoints1[0].fU"] = "0", ["asCrossSectionPoints1[0].fNx"] = "0", ["asCrossSectionPoints1[0].fNy"] = "1",
		["asCrossSectionPoints1[1].fX"] = "2", ["asCrossSectionPoints1[1].fY"] = "0.5", ["asCrossSectionPoints1[1].fU"] = "1", ["asCrossSectionPoints1[1].fNx"] = "0", ["asCrossSectionPoints1[1].fNy"] = "1",
		["asCrossSectionEdges1[0].usTexture"] = "0", ["asCrossSectionEdges1[0].usCSPoint1"] = "0", ["asCrossSectionEdges1[0].usCSPoint2"] = "1",
		["asCrossSectionEdges2[0].usTexture"] = "1", ["asCrossSectionEdges2[0].usCSPoint1"] = "0", ["asCrossSectionEdges2[0].usCSPoint2"] = "1",
		["asCrossSectionEdges2[1].usTexture"] = "1", ["asCrossSectionEdges2[1].usCSPoint1"] = "1", ["asCrossSectionEdges2[1].usCSPoint2"] = "0",
		["asCrossSections[0].usCSPointBlockIndex"] = "1", ["asCrossSections[0].usCSEdgeBlockIndex"] = "1",
		["asCrossSections[1].usCSPointBlockIndex"] = "1", ["asCrossSections[1].usCSEdgeBlockIndex"] = "2",
		["asCrossSectionSelects[0].usCrossSectionIndex"] = "1",
		["sCoasterType.uiWinchCrossSection"] = "2",
		["sCoasterType.fTrackHeightAdjust"] = "-1.5"
	};

	private static CoasterSlice[] StraightSlices( int count, float roll = 0, float pitch = 0 ) =>
		Enumerable.Range( 0, count ).Select( index => new CoasterSlice( new SVector3( 10, 20, index * 3f ), 0, pitch, roll, 0, 0 ) ).ToArray();

	[TestMethod]
	public void TheCrossSectionLoadsPointsEdgesTexturesAndTheHeightAdjust()
	{
		var sections = CoasterCrossSections.FromValues( SectionSettings() );
		Assert.IsFalse( sections.IsEmpty );
		Assert.AreEqual( 2, sections.Textures.Count );
		Assert.AreEqual( "rail.tga", sections.Textures[0].FileName );
		Assert.AreEqual( 1f, sections.Textures[1].ScrollRate );
		Assert.AreEqual( 2, sections.Sections.Count );
		var (points, edges) = sections.Resolve( 0 );
		Assert.AreEqual( 2, points.Count );
		Assert.AreEqual( -1f, points[0].Y, "fTrackHeightAdjust is added to every point Y (0x42d6d0)" );
		Assert.AreEqual( 1, edges.Count );
		Assert.AreEqual( 2, sections.Resolve( 1 ).Edges.Count );
		Assert.AreEqual( 2, sections.WinchSection );
	}

	[DataTestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void TrackVerticesCarrySelfLightingForFixedAndScrollingTextures( bool selfLit, bool scrolling )
	{
		var values = SectionSettings();
		values["asTextureData[0].bIsSelfIlluminating"] = selfLit ? "1" : "0";
		values["asTextureData[1].bIsSelfIlluminating"] = selfLit ? "1" : "0";
		values["asTextureData[0].fScrollRate"] = scrolling ? "1" : "0";
		values["asTextureData[1].fScrollRate"] = scrolling ? "1" : "0";
		values["asTextureData[1].pcTextureFilename"] = "\"GEM_GLOW.tga\"";
		var sections = CoasterCrossSections.FromValues( values );
		for ( var texture = 0; texture < 2; texture++ )
		{
			var mesh = CoasterTrackMesh.Build( sections, StraightSlices( 2 ), closed: false, winch: texture == 1 ? new HashSet<int> { 0 } : null );
			Assert.IsTrue( mesh.Vertices.Count > 0 );
			Assert.IsTrue( mesh.Vertices.All( source => source.Texture == texture ) );
			var expected = selfLit ? TextureSlotAddressing.SelfLitFlag | (texture == 1 ? TextureSlotAddressing.LightSourceFlag : 0u) : 0u;
			foreach ( var source in mesh.Vertices )
			{
				var vertex = CoasterRenderer.TrackVertex( source, sections );
				Assert.AreEqual( expected, vertex.MatFlags, "only self-lit textures gain lighting flags; glow is a light source only when self-lit" );
				AssertTrackVertexGeometry( source, vertex );
			}
		}
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	public void EveryAppendedTrackMarkerIsSelfLitButNotALightSource( int variant )
	{
		var sections = CoasterCrossSections.FromValues( SectionSettings() );
		var mesh = CoasterTrackMesh.Build( sections, StraightSlices( 2 ), closed: false, marker: ( _, _ ) => variant );
		Assert.IsTrue( mesh.Vertices.Count > 0 );
		Assert.IsTrue( mesh.Vertices.All( source => source.Texture == CoasterTrackMesh.MarkerTexture( sections.Textures.Count, variant ) ) );
		foreach ( var source in mesh.Vertices )
		{
			var vertex = CoasterRenderer.TrackVertex( source, sections );
			Assert.AreEqual( TextureSlotAddressing.SelfLitFlag, vertex.MatFlags, "m_erase/red/blue ignore scene lighting but keep the evening extension's non-light-source tint" );
			AssertTrackVertexGeometry( source, vertex );
		}
	}

	[TestMethod]
	public void UnknownTrackTexturesDoNotBecomeSelfLit()
	{
		var sections = CoasterCrossSections.FromValues( SectionSettings() );
		foreach ( var texture in new[] { -1, sections.Textures.Count + CoasterTrackMesh.MarkerTextureNames.Length, int.MaxValue } )
		{
			var source = new CoasterMeshVertex( new SVector3( 1, 2, 3 ), new SVector3( 4, 5, 6 ), 0.25f, 0.75f, texture );
			var vertex = CoasterRenderer.TrackVertex( source, sections );
			Assert.AreEqual( 0u, vertex.MatFlags );
			AssertTrackVertexGeometry( source, vertex );
		}
		var empty = CoasterCrossSections.FromValues( new Dictionary<string, string>() );
		var fallback = new CoasterMeshVertex( SVector3.Zero, SVector3.UnitY, 0, 0, 0 );
		Assert.AreEqual( 0u, CoasterRenderer.TrackVertex( fallback, empty ).MatFlags, "the mesh's ordinary missing-texture fallback is not a marker" );
	}

	private static void AssertTrackVertexGeometry( CoasterMeshVertex source, Vertex vertex )
	{
		Assert.AreEqual( source.Position.X, vertex.Position.X );
		Assert.AreEqual( source.Position.Z, vertex.Position.Y );
		Assert.AreEqual( source.Position.Y, vertex.Position.Z );
		Assert.AreEqual( source.Normal.X, vertex.Normal.X );
		Assert.AreEqual( source.Normal.Z, vertex.Normal.Y );
		Assert.AreEqual( source.Normal.Y, vertex.Normal.Z );
		Assert.AreEqual( source.U, vertex.TexCoords.X );
		Assert.AreEqual( source.V, vertex.TexCoords.Y );
		Assert.AreEqual( source.Texture, vertex.TexIndex );
	}

	[TestMethod]
	public void TheSelectUsesTheWinchSectionAndTheClimbingPitchWindow()
	{
		var values = SectionSettings();
		values["asCrossSectionSelects[0].fPitchRangeMin"] = "-90";
		values["asCrossSectionSelects[0].fPitchRangeMax"] = "-1";
		values["asCrossSectionSelects[1].usCrossSectionIndex"] = "2";
		var sections = CoasterCrossSections.FromValues( values );
		Assert.AreEqual( 0, sections.SelectSection( -0.3f, winch: false ), "climbing (negative pitch) takes the first window" );
		Assert.AreEqual( 1, sections.SelectSection( 0.3f, winch: false ), "descending falls through to the default select" );
		Assert.AreEqual( 1, sections.SelectSection( -0.3f, winch: true ), "a chain-lift segment uses uiWinchCrossSection" );
	}

	/// <summary>
	/// The extrusion (<c>0x43f4e0</c>, <c>0x43fd60</c>): one ring of vertices per slice that the strips before and after it share, two triangles per edge and
	/// segment wound as the emitter winds them (the diagonal alternating with the parity flag), the points placed by the slice frame.
	/// </summary>
	[TestMethod]
	public void ExtrusionSharesARingPerSliceAndWindsLikeTheOriginal()
	{
		var sections = CoasterCrossSections.FromValues( SectionSettings() );
		var mesh = CoasterTrackMesh.Build( sections, StraightSlices( 5 ), closed: false );
		Assert.AreEqual( 4, mesh.Segments );
		Assert.AreEqual( 5, mesh.Rings );
		Assert.AreEqual( 5 * 2, mesh.Vertices.Count, "one vertex per section point and ring" );
		Assert.AreEqual( 4 * 2, mesh.TriangleCount );
		Assert.AreEqual( 12f, mesh.Length, 1e-4f );
		var first = mesh.Vertices[0];
		Assert.AreEqual( 8f, first.Position.X, 1e-4f, "yaw 0: the right axis is +X, the point X = -2 lies at 10 - 2" );
		Assert.AreEqual( 19f, first.Position.Y, 1e-4f, "Y = 20 + (0.5 - 1.5)" );
		Assert.AreEqual( 0f, first.Position.Z, 1e-4f );
		Assert.AreEqual( 0f, first.U );
		Assert.AreEqual( 0f, first.V );
		Assert.AreEqual( 3f / CoasterTrackMesh.MetersPerRepeat, mesh.Vertices[2].V, 1e-5f, "V grows by the slice distance over six" );
		Assert.AreEqual( SVector3.UnitY, first.Normal );
		// Ring 0 is vertices 0 (from) and 1 (to), ring 1 vertices 2 and 3: (Pf, Cf, Pt), (Ct, Pt, Cf), then with the parity set (Pf, Cf, Ct), (Ct, Pt, Pf).
		CollectionAssert.AreEqual( new uint[] { 0, 2, 1, 3, 1, 2, 2, 4, 5, 5, 3, 2 }, mesh.Indices.Take( 12 ).ToArray() );
		var closed = CoasterTrackMesh.Build( sections, StraightSlices( 5 ), closed: true );
		Assert.AreEqual( 5, closed.Segments, "a closed chain joins its last slice to the first" );
	}

	[TestMethod]
	public void ARollTurnsTheSectionInItsPlane()
	{
		var (right, up) = CoasterTrackMesh.Frame( 0, 0, MathF.PI / 2 );
		Assert.AreEqual( 0f, (right - SVector3.UnitY).Length(), 1e-5f, "a quarter roll puts the right axis up" );
		Assert.AreEqual( 0f, (up + SVector3.UnitX).Length(), 1e-5f );
		var sections = CoasterCrossSections.FromValues( SectionSettings() );
		var banked = CoasterTrackMesh.Build( sections, StraightSlices( 2, roll: MathF.PI / 2 ), closed: false );
		Assert.AreEqual( 11f, banked.Vertices[0].Position.X, 1e-4f, "the point's Y (-1) goes along the rolled up axis (-X)" );
		Assert.AreEqual( 18f, banked.Vertices[0].Position.Y, 1e-4f, "the point's X (-2) goes along the rolled right axis (+Y)" );
	}

	[TestMethod]
	public void TheFrameFollowsYawAndPitchLikeTheCurveOrientation()
	{
		var direction = SVector3.Normalize( new SVector3( 1, -0.5f, 2 ) );
		var (yaw, pitch) = HermiteCoasterCurve.Orientation( direction );
		var (right, up) = CoasterTrackMesh.Frame( yaw, pitch, 0 );
		var forward = SVector3.Cross( right, up );
		Assert.AreEqual( 0f, (forward - direction).Length(), 1e-3f, "right × up is the tangent" );
		Assert.AreEqual( 0f, SVector3.Dot( right, direction ), 1e-3f );
		Assert.IsTrue( up.Y > 0 );
	}

	/// <summary>
	/// A winch link takes uiWinchCrossSection, and a marker link (a crossing, the build cursor's link) the copy of the invalid cross section with every edge in the marker
	/// texture (<c>0x43f4e0</c>); a change of section emits the previous slice's ring again for the new section.
	/// </summary>
	[TestMethod]
	public void AWinchLinkAndAMarkerLinkUseTheirOwnSections()
	{
		var sections = CoasterCrossSections.FromValues( SectionSettings() );
		var slices = StraightSlices( 3 );
		var winch = CoasterTrackMesh.Build( sections, slices, closed: false, winch: new HashSet<int> { 0 } );
		Assert.AreEqual( 3 * 2, winch.Vertices.Count, "the winch section's two edges share the ring's two points (one texture)" );
		Assert.AreEqual( 2 * 2 * 2, winch.TriangleCount );
		Assert.IsTrue( winch.Vertices.All( vertex => vertex.Texture == 1 ) );
		var marked = CoasterTrackMesh.Build( sections, slices, closed: false, marker: ( _, _ ) => CoasterTrackMesh.RefusedMarker );
		Assert.AreEqual( 0, sections.InvalidSectionIndex, "no uiInvalidCrossSection: the first section is copied" );
		Assert.IsTrue( marked.Vertices.All( vertex => vertex.Texture == CoasterTrackMesh.MarkerTexture( 2, CoasterTrackMesh.RefusedMarker ) ), "red.tga follows the coaster's two textures and m_erase.tga" );
		Assert.AreEqual( 3, CoasterTrackMesh.MarkerTexture( 2, CoasterTrackMesh.RefusedMarker ) );
		Assert.AreEqual( 4, CoasterTrackMesh.MarkerTexture( 2, CoasterTrackMesh.CursorMarker ), "blue.tga" );
		var changing = CoasterTrackMesh.Build( sections, StraightSlices( 4 ), closed: false, marker: ( node, _ ) => null, winch: new HashSet<int>() );
		Assert.AreEqual( 4, changing.Rings );
		var switched = CoasterTrackMesh.Build( sections, StraightSlices( 4 ).Select( ( slice, index ) => slice with { Section = index } ).ToArray(), closed: false, marker: ( node, _ ) => node == 1 ? CoasterTrackMesh.CursorMarker : null );
		Assert.AreEqual( 4 + 2, switched.Rings, "the ring is emitted again where the marker run starts and where it ends" );
	}

	// ---- A track under construction ----------------------------------------------------------------

	private static CoasterBuilder OpenTrack( int pylons )
	{
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		builder.Height = 40;
		for ( var index = 0; index < pylons; index++ )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + CoasterTests.Circuit[index].X, 20 + CoasterTests.Circuit[index].Y ) );
		return builder;
	}

	[TestMethod]
	public void AnOpenTrackIsDrawnAsTheCurveThroughItsNodesAndTheCursor()
	{
		var builder = OpenTrack( 3 );
		var track = builder.Track;
		var preview = track.BuildPreviewChain( (-4 + 20, 8 + 20), 40, false );
		Assert.IsNotNull( preview );
		Assert.IsFalse( preview!.Value.Closed );
		Assert.AreEqual( track.Nodes.Count + 1, preview.Value.Chain.Count, "the ghost pylon at the cursor is the last node" );
		var knots = track.BuildKnots( builder.Site, preview.Value.Chain, writeBanks: false );
		var slices = HermiteCoasterCurve.Slices( knots, builder.Ride.Definition.Curviness, closed: false );
		Assert.AreEqual( (knots.Count - 1) * HermiteCoasterCurve.SliceSegments + 1, slices.Length );
		Assert.AreEqual( 0f, (slices[0].Position - knots[0].Position).Length(), 1e-3f, "the first slice is the first station node" );
		Assert.AreEqual( 0f, (slices[^1].Position - knots[^1].Position).Length(), 1e-3f, "the last slice is the cursor pylon" );
		// The curve bends: an interior slice leaves the straight chord between its two knots.
		var first = CoasterTrack.StationNodeCount;
		var chordMid = (knots[first].Position + knots[first + 1].Position) / 2;
		Assert.IsTrue( (slices[first * HermiteCoasterCurve.SliceSegments + 5].Position - chordMid).Length() > 0.1f );
		Assert.IsTrue( track.Nodes.All( node => node.Bank == 0 ), "a preview leaves the nodes' banks untouched" );
		var mesh = CoasterTrackMesh.Build( CoasterCrossSections.FromValues( SectionSettings() ), slices, closed: false );
		Assert.AreEqual( slices.Length - 1, mesh.Segments );
	}

	[TestMethod]
	public void TheChainStartUnderTheCursorIsTheClosingTargetAndItsOwnPairsTakeTheMarker()
	{
		// tp.exe 0x4360c4 sets 0x800000 (0x804000 when the closing link is refused) on the start; 0x43f4e0 marks a node's own tag pairs while that bit is set
		var builder = OpenTrack( 3 );
		var track = builder.Track;
		var start = track.Nodes[0];
		var valid = track.BuildPreviewChain( (start.X, start.Y), 40, false, valid: true );
		Assert.IsNotNull( valid );
		Assert.AreEqual( track.Nodes.Count, valid!.Value.Chain.Count, "the closing target adds no cursor node" );
		Assert.AreEqual( CoasterNodeFlags.ClosingTarget, valid.Value.Chain[0].Flags & (CoasterNodeFlags.ClosingTarget | CoasterNodeFlags.Invalid) );
		Assert.AreEqual( CoasterNodeFlags.None, track.Nodes[0].Flags & CoasterNodeFlags.ClosingTarget, "the track's own node keeps its flags" );
		var none = Array.Empty<IReadOnlyList<int>>();
		Assert.AreEqual( CoasterTrackMesh.CursorMarker, CoasterRenderer.LinkMarker( valid.Value.Chain, none, true, 0, 0 ), "blue" );
		Assert.IsNull( CoasterRenderer.LinkMarker( valid.Value.Chain, none, true, 1, 1 ), "other nodes' own pairs stay plain" );
		Assert.IsNull( CoasterRenderer.LinkMarker( valid.Value.Chain, none, true, 0, 1 ), "the link out of the start is plain" );
		var refused = track.BuildPreviewChain( (start.X, start.Y), 40, false, valid: false );
		Assert.AreEqual( CoasterTrackMesh.RefusedMarker, CoasterRenderer.LinkMarker( refused!.Value.Chain, none, false, 0, 0 ), "red" );
	}

	[TestMethod]
	public void AClosedUnfinishedTrackEndsInTheStationEndNode()
	{
		var builder = OpenTrack( CoasterTests.Circuit.Length );
		Assert.IsTrue( builder.Close() );
		var preview = builder.Track.BuildPreviewChain( null, 40, false );
		Assert.IsNotNull( preview );
		Assert.IsTrue( preview!.Value.Closed );
		Assert.IsTrue( preview.Value.Chain[^1].IsStationEnd );
		Assert.AreEqual( builder.Track.Nodes.Count + 1, preview.Value.Chain.Count );
		builder.Complete();
		Assert.IsNull( builder.Track.BuildPreviewChain( null, 40, false ), "a completed track is drawn from the controller" );
	}

	[TestMethod]
	public void TheClosedKnotsOfTheControllerAreUnchangedByTheOpenChainSupport()
	{
		var builder = OpenTrack( CoasterTests.Circuit.Length );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		var ride = builder.Ride;
		var again = builder.Track.BuildKnots( builder.Site );
		var chain = builder.Track.BuildKnots( builder.Site, builder.Track.Nodes, writeBanks: false );
		CollectionAssert.AreEqual( again.ToArray(), chain.ToArray() );
		Assert.IsTrue( ride.IsOperational );
	}

	// These tests exercise the renderer's phase query without a graphics device or original models. The asset tests
	// apply that phase to the shipped wheel UV tracks; no GPU/visual parity claim follows from these CPU fixtures.
	internal static CoasterRide WheelRide( double setupSeconds = 2 )
	{
		var builder = OpenTrack( CoasterTests.Circuit.Length );
		builder.Ride.Advance( setupSeconds );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		Assert.IsTrue( builder.Ride.Trains.Count > 0 );
		return builder.Ride;
	}

	internal static CoasterRenderer WheelRenderer( CoasterRide ride ) => new( () => new[] { ride }, () => default, 0.2f,
		_ => null, _ => null, _ => 0 );

	[TestMethod]
	public void WheelPhaseMatchesWhetherTheTrainWasSampledContinuouslyOrFirstSeenLater()
	{
		var visible = WheelRide();
		var offscreen = WheelRide();
		var continuous = WheelRenderer( visible );
		CoasterRenderer? firstSeen = null;
		try
		{
			Assert.AreEqual( 0f, continuous.WheelTick( visible, 0, 20 ) );
			var expected = 0f;
			for ( var step = 0; step < 8; step++ )
			{
				visible.Advance( 0.03125 );
				offscreen.Advance( 0.03125 );
				visible.Trains[0].Speed = offscreen.Trains[0].Speed = 10f;
				expected = continuous.WheelTick( visible, 0, 20 );
			}
			firstSeen = WheelRenderer( offscreen );
			Assert.AreEqual( 7.5f, expected, 1e-5f, "250 ms × (0.1 × 10) × 0.03, from slot setup at 2 s" );
			Assert.AreEqual( expected, firstSeen.WheelTick( offscreen, 0, 20 ), "first visibility cannot restart the wheel clip" );
			Assert.AreEqual( 2.0, offscreen.Trains[0].WheelAnimationStartSeconds );
		}
		finally
		{
			continuous.Delete();
			firstSeen?.Delete();
		}
	}

	[TestMethod]
	public void RecreatingTheRendererKeepsTheTrainSlotsWheelEpoch()
	{
		var ride = WheelRide( 4 );
		var renderer = WheelRenderer( ride );
		try
		{
			ride.Advance( 0.125 );
			ride.Trains[0].Speed = 10f;
			Assert.AreEqual( 3.75f, renderer.WheelTick( ride, 0, 20 ), 1e-5f );
			renderer.Delete();
			ride.Advance( 0.125 );
			ride.Trains[0].Speed = 10f;
			renderer = WheelRenderer( ride );
			Assert.AreEqual( 7.5f, renderer.WheelTick( ride, 0, 20 ), 1e-5f, "recreating presentation keeps the start at 4 s" );
			Assert.AreEqual( 4.0, ride.Trains[0].WheelAnimationStartSeconds );
		}
		finally
		{
			renderer.Delete();
		}
	}

	[TestMethod]
	public void ReinitializingTheSlotsRestartsWheelsEvenWhenTheRendererAndTrainCountStayTheSame()
	{
		var ride = WheelRide();
		var renderer = WheelRenderer( ride );
		try
		{
			var oldTrain = ride.Trains[0];
			var count = ride.Trains.Count;
			ride.Advance( 0.125 );
			oldTrain.Speed = 10f;
			Assert.AreEqual( 3.75f, renderer.WheelTick( ride, 0, 20 ), 1e-5f );
			ride.EnterState( CoasterControllerState.Incomplete );
			Assert.AreEqual( 0, ride.Trains.Count );
			ride.Rebuild();
			Assert.AreEqual( count, ride.Trains.Count );
			Assert.AreNotSame( oldTrain, ride.Trains[0] );
			Assert.AreEqual( 2.125, ride.Trains[0].WheelAnimationStartSeconds );
			Assert.AreEqual( 0f, renderer.WheelTick( ride, 0, 20 ), "a reused train index reads the new slot's epoch" );
			ride.Advance( 0.125 );
			ride.Trains[0].Speed = 10f;
			Assert.AreEqual( 3.75f, renderer.WheelTick( ride, 0, 20 ), 1e-5f );
		}
		finally
		{
			renderer.Delete();
		}
	}

	[TestMethod]
	public void WheelFrameArithmeticKeepsMillisecondTruncationCurrentSpeedAndLooping()
	{
		Assert.AreEqual( 0f, CoasterCarRig.WheelTick( 5000, 0f, 20 ) );
		Assert.AreEqual( 10f, CoasterCarRig.WheelTick( 1000, 10f, 20 ), 1e-4f );
		Assert.AreEqual( 24f, CoasterCarRig.WheelTick( 2000, 4f, 90 ), 1e-4f );
		Assert.AreEqual( CoasterCarRig.WheelTick( 125, 10f, 20 ), CoasterCarRig.WheelTick( 125.75, 10f, 20 ), "the animation clock uses whole milliseconds" );
		Assert.AreEqual( 0f, CoasterCarRig.WheelTick( -1, 10f, 20 ) );
		Assert.AreEqual( 0f, CoasterCarRig.WheelTick( 100, 5f, 0 ) );
	}

	// ---- Pylons and cars ------------------------------------------------------------------------------

	/// <summary>The clip fractions of the pose routine (<c>0x430ed0</c>, <c>0x430ca0</c>) with a coaster's clip maps.</summary>
	[TestMethod]
	public void APylonsClipFractionsFollowThePoseRoutine()
	{
		var settings = CoasterTests.PcSettings();
		settings["asPylonControls[0].fInclineAnimMultiplier"] = "4.5";
		settings["asPylonControls[0].fBankAnimMultiplier"] = "-4.5";
		var control = CoasterDefinition.FromValues( settings ).StandardPylon;
		Assert.AreEqual( new CoasterClipMap( 0, 1, 4.5f, 0 ), control.InclineMap );
		Assert.AreEqual( new CoasterClipMap( 0, 1, -4.5f, 0 ), control.BankMap );
		Assert.AreEqual( CoasterClipMap.Identity, control.RotateMap, "the other maps keep the defaults 0, 1, 1, 0 (0x42ff1a)" );
		var map = new CoasterClipMap( 0.2f, 0.1f, 1, 0 );
		Assert.AreEqual( 0.1f, map.Fraction( 0.05f ), "the start cap is applied first, the end cap last" );

		var (loft, rotate, incline, bank, loopTilt) = CoasterPylonRig.Fractions( control, new CoasterPylonPose( 1.4f, -MathF.PI / 2, 0.2f, 0.3f, 1.25f ) );
		Assert.AreEqual( 1f, loft, "the height parameter is clamped to [0, 1]" );
		Assert.AreEqual( 0.75f, rotate, 1e-6f, "frac(yaw / 2π), plus 1 when negative" );
		Assert.AreEqual( 0.2f * 0.15915494f * 4.5f + 0.5f, incline, 1e-6f, "pitch / 2π through the map, centred on 0.5" );
		Assert.AreEqual( 0.5f - 0.3f * 0.15915494f * 4.5f, bank, 1e-5f );
		Assert.AreEqual( 0.25f, loopTilt, 1e-6f, "the loop rotate parameter wrapped into [0, 1)" );
		Assert.AreEqual( 0f, CoasterPylonRig.Fractions( control, new CoasterPylonPose( 0, Bank: 2f ) ).Bank, "a bank far past the map's range stops at its start cap" );
	}

	/// <summary>A suspended coaster's pylon stands on the outside of a turn (node flag 0x10000000, 0x431660): its rotate clip takes the yaw half a turn on.</summary>
	[TestMethod]
	public void ASuspendedCoastersPylonTurnsToTheOutsideOfItsCurve()
	{
		var previous = new SVector3( 0, 0, -10 );
		var position = SVector3.Zero;
		var right = new SVector3( 10, 0, 10 );
		var left = new SVector3( -10, 0, 10 );
		Assert.IsTrue( CoasterTrack.PylonTurnedBack( previous, position, right, -0.999f, false, suspended: true, loopNode: false ), "b.x·a.z − b.z·a.x = 10·(−10) − 10·0 < 0" );
		Assert.IsFalse( CoasterTrack.PylonTurnedBack( previous, position, left, -0.999f, false, suspended: true, loopNode: false ), "the other turn clears it" );
		Assert.IsFalse( CoasterTrack.PylonTurnedBack( previous, position, right, -0.999f, false, suspended: false, loopNode: false ), "only a suspended coaster (definition flag 8)" );
		Assert.IsFalse( CoasterTrack.PylonTurnedBack( previous, position, right, -0.999f, false, suspended: true, loopNode: true ), "not on a node with flag 0x40" );
		Assert.IsFalse( CoasterTrack.PylonTurnedBack( previous, position, new SVector3( 0, 0, 10 ), -0.999f, false, suspended: true, loopNode: false ), "not on a straight" );
		Assert.IsFalse( CoasterTrack.PylonTurnedBack( previous, position, right, -0.999f, true, suspended: true, loopNode: false ), "not on a loop pylon" );
		Assert.AreEqual( 0.75f, CoasterPylonRig.Fractions( null, new CoasterPylonPose( 0, MathF.PI / 2, TurnedBack: true ) ).Rotate, 1e-6f, "frac(¼ + ½)" );
	}

	/// <summary>
	/// The flag lives in the node (0x431660): the curve branch sets or clears it, a straight node or a loop pylon keeps the half turn of its last curve pass, and the
	/// controller's rebuild writes the verdicts while a preview pass leaves the nodes untouched.
	/// </summary>
	[TestMethod]
	public void AStraightNodeKeepsTheHalfTurnOfItsLastCurvePass()
	{
		Assert.IsNull( CoasterTrack.PylonTurnedBackInCurve( new SVector3( 0, 0, -10 ), SVector3.Zero, new SVector3( 0, 0, 10 ), -0.999f, false, suspended: true, loopNode: false ), "a straight leaves the flag" );
		Assert.IsNull( CoasterTrack.PylonTurnedBackInCurve( new SVector3( 0, 0, -10 ), SVector3.Zero, new SVector3( 10, 0, 10 ), -0.999f, true, suspended: true, loopNode: false ), "so does the loop-pylon branch" );
		Assert.AreEqual( false, CoasterTrack.PylonTurnedBackInCurve( new SVector3( 0, 0, -10 ), SVector3.Zero, new SVector3( 10, 0, 10 ), -0.999f, false, suspended: false, loopNode: false ), "a curve of a coaster that does not hang clears it" );
		var settings = CoasterTests.PcSettings();
		settings["sCoasterType.bIsSuspendedCoaster"] = "1";
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ), new CoasterWorld( 1 ), "Suspended" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		builder.Height = 40;
		// (24, 20) turns toward (28, 24); (28, 24) runs on straight to (32, 28).
		foreach ( var (x, y) in new[] { (24, 20), (28, 24), (32, 28), (36, 28) } )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( x, y ) );
		var track = builder.Track;
		var chain = track.Nodes;
		var positions = chain.Select( node => track.NodePosition( builder.Site, node ) ).ToArray();
		var straight = chain.ToList().FindIndex( node => node.X == 28 && node.Y == 24 );
		var curve = chain.ToList().FindIndex( node => node.X == 24 && node.Y == 20 );
		var preview = track.PylonsTurnedBack( chain, positions );
		Assert.IsFalse( preview[straight], "a straight node without the flag stays without it" );
		Assert.IsTrue( chain.All( node => (node.Flags & CoasterNodeFlags.TurnedBack) == 0 ), "a pass that does not write leaves the nodes alone" );
		chain[straight].Flags |= CoasterNodeFlags.TurnedBack;
		chain[curve].Flags |= CoasterNodeFlags.TurnedBack;
		var verdict = CoasterTrack.PylonTurnedBack( positions[curve - 1], positions[curve], positions[curve + 1], -0.8f, false, suspended: true, loopNode: false );
		var written = track.PylonsTurnedBack( chain, positions, write: true );
		Assert.IsTrue( written[straight], "the straight node keeps the half turn it holds" );
		Assert.IsTrue( (chain[straight].Flags & CoasterNodeFlags.TurnedBack) != 0 );
		Assert.AreEqual( verdict, written[curve], "the curve node takes the curve branch's verdict" );
		Assert.AreEqual( verdict, (chain[curve].Flags & CoasterNodeFlags.TurnedBack) != 0, "and the rebuild stores it" );
		Assert.IsTrue( (CoasterSave.KnownNodeFlags & (uint)CoasterNodeFlags.TurnedBack) != 0, "a save may carry the flag" );
	}

	[TestMethod]
	public void ACarSitsOnTheInterpolatedSampleWithItsDirectionAndBank()
	{
		var samples = new[]
		{
			new CoasterSample { Position = new SVector3( 0, 0, 0 ), Direction = SVector3.UnitZ, Bank = 0 },
			new CoasterSample { Position = new SVector3( 0, 0, 3 ), Direction = SVector3.UnitX, Bank = 0.4f },
			new CoasterSample { Position = new SVector3( 3, 0, 3 ), Direction = SVector3.UnitX, Bank = 0.4f }
		};
		var pose = CoasterCarRig.PoseAt( samples, 0.5f );
		Assert.AreEqual( 1.5f, pose.Position.Z, 1e-4f );
		Assert.AreEqual( MathF.PI / 4, pose.Yaw, 1e-3f, "halfway between +Z and +X" );
		Assert.AreEqual( 0.2f, pose.Roll, 1e-5f );
		var wrapped = CoasterCarRig.PoseAt( samples, -0.5f );
		Assert.IsTrue( wrapped.Position.X > 1f, "a position before the start wraps to the end of the closed track" );
		var matrix = CoasterCarRig.Matrix( new CoasterCarPose( new SVector3( 5, 6, 7 ), 0, 0, 0 ), 0.5f );
		Assert.AreEqual( new SVector3( 5, 6.5f, 7 ), matrix.Translation, "the height offset runs along the car's up axis" );
		Assert.AreEqual( SVector3.UnitZ, new SVector3( matrix.M31, matrix.M32, matrix.M33 ), "row 3 is the forward axis" );
		var pitched = CoasterCarRig.Matrix( new CoasterCarPose( SVector3.Zero, 0, 0.5f, 0 ), 0 );
		Assert.IsTrue( pitched.M32 < 0, "a positive pitch tilts the forward axis downwards, as in the curve orientation" );
	}

	[TestMethod]
	public void TheTrainTypesFollowFrontCentreRear()
	{
		var types = CoasterCarTypes.FromValues( new Dictionary<string, string>
		{
			["asCarTypes[0].pcMeshFilename"] = "\"head\"", ["asCarTypes[0].fFrontDistanceAdd"] = "0.5", ["asCarTypes[0].fYOffset"] = "-1",
			["asCarTypes[1].pcMeshFilename"] = "\"body\"", ["asCarTypes[2].pcMeshFilename"] = "\"tail\"", ["asCarTypes[2].bBendCar"] = "1",
			["sTrainType.uiFrontCarTypeIndex"] = "0", ["sTrainType.uiCentreCarTypeIndex"] = "1", ["sTrainType.uiRearCarTypeIndex"] = "2"
		} );
		Assert.AreEqual( 3, types.Types.Count );
		Assert.AreEqual( ("head", 0.5f, -1f), (types.Types[0].MeshName, types.Types[0].FrontAdd, types.Types[0].YOffset) );
		Assert.IsTrue( types.Types[2].Bend );
		CollectionAssert.AreEqual( new[] { 0, 1, 1, 2 }, Enumerable.Range( 0, 4 ).Select( car => types.TypeIndexFor( car, 4 ) ).ToArray() );
		Assert.AreEqual( 0, types.TypeIndexFor( 0, 1 ), "a one-car train is a front car" );
	}
}

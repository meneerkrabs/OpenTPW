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
	[TestInitialize]
	public void Init()
	{
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
			var sections = CoasterCrossSections.FromSettings( entry.Settings );
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

	/// <summary>The two flume sections: climbing is the dry section, flat track the wet one, both with the same points.</summary>
	[TestMethod]
	public void TheFlumesPickTheDrySectionWhenClimbing()
	{
		var flume = PcCoasters().Single( entry => entry.InfoId == 1185 );
		var sections = CoasterCrossSections.FromSettings( flume.Settings );
		var dry = sections.SelectSection( -0.3f, winch: false );
		var wet = sections.SelectSection( 0, winch: false );
		Assert.AreNotEqual( dry, wet );
		Assert.AreEqual( sections.Sections[dry].PointBlock, sections.Sections[wet].PointBlock );
		Assert.AreNotEqual( sections.Resolve( dry ).Edges.Select( edge => edge.Texture ).Max(), sections.Resolve( wet ).Edges.Select( edge => edge.Texture ).Max() );
		Assert.IsTrue( sections.Textures.Any( texture => texture.ScrollRate != 0 ), "the flume's water and ratchet scroll" );
	}

	/// <summary>Every standard pylon poses: its tag reaches the node height, stretched by the loft clip, and it has drawable parts.</summary>
	[TestMethod]
	public void EveryPylonPosesToTheNodeHeight()
	{
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromSettings( entry.Settings );
			foreach ( var loop in new[] { false, true } )
			{
				if ( loop && !definition.HasLoopPylon )
					continue;
				var name = definition.Pylon( loop ).MeshName;
				var geometry = Find( entry, name );
				if ( geometry == null )
				{
					Assert.IsTrue( loop && definition.IsTrough, $"{entry.InfoId}: the pylon model {name} exists" );
					continue;
				}
				var rig = new CoasterPylonRig( geometry, Find( entry, name + "L" )?.Clip, (Find( entry, name + "R" ) ?? Find( entry, name + "R1" ))?.Clip );
				// Some loop pylons (the Space themes') carry no tag motion in their loft clip: they are scaled to the node instead.
				Assert.IsTrue( rig.HasLoft || loop, $"{entry.InfoId} {name}: the loft clip raises the track tag ({rig.TagLow}..{rig.TagHigh})" );
				if ( !rig.HasLoft )
				{
					Assert.IsTrue( rig.Pose( 20, 0 ).Count > 0 );
					continue;
				}
				foreach ( var height in new[] { rig.TagLow, (rig.TagLow + rig.TagHigh) / 2, rig.TagHigh, rig.TagHigh + 3 } )
					Assert.AreEqual( height, rig.PosedTagHeight( height ), 1e-2f, $"{entry.InfoId} {name}: the posed tag stands at {height}" );
				// The pylon ends at the track: no part floats above the node, and a bridge-type pylon's top is the node height.
				foreach ( var height in new[] { MathF.Max( definition.Pylon( loop ).MinHeight, rig.TagLow ), 40f, 70f, MathF.Min( definition.Pylon( loop ).MaxHeight, rig.TagHigh ) } )
				{
					var posed = rig.Pose( height, 0.7f );
					Assert.IsTrue( posed.All( part => part.Vertices.Min( vertex => vertex.Position.GetSystemVector3().Z ) <= height + 0.05f ), $"{entry.InfoId} {name}: no part above the node at {height}" );
					if ( !loop && entry.InfoId is 1180 or 2180 or 3190 or 4180 or 4190 )
						Assert.AreEqual( height, posed.SelectMany( part => part.Vertices ).Max( vertex => vertex.Position.GetSystemVector3().Z ), 1f, $"{entry.InfoId} {name}: the pylon's top meets the node at {height}" );
					Assert.IsTrue( rig.Pose( height, 0.7f, stackedAbove: true ).Count >= posed.Count );
				}
				var parts = rig.Pose( (rig.TagLow + rig.TagHigh) / 2, 1.0f );
				Assert.IsTrue( parts.Count > 0 && parts.All( part => part.Vertices.Length > 0 && part.Indices.Length % 3 == 0 ) );
				var top = parts.SelectMany( part => part.Vertices ).Max( vertex => vertex.Position.GetSystemVector3().Z );
				var tall = rig.Pose( rig.TagHigh, 0 ).SelectMany( part => part.Vertices ).Max( vertex => vertex.Position.GetSystemVector3().Z );
				Assert.IsTrue( tall > top + 1, $"{entry.InfoId} {name}: a higher node gives a taller pylon" );
			}
		}
	}

	/// <summary>Every car type loads, has extents along its length and a seat socket per head.</summary>
	[TestMethod]
	public void EveryCarTypeLoadsWithExtentsAndSeats()
	{
		foreach ( var entry in PcCoasters() )
		{
			var types = CoasterCarTypes.FromSettings( entry.Settings );
			Assert.IsTrue( types.Types.Count >= 1, $"{entry.InfoId} has a car type" );
			foreach ( var index in new[] { types.FrontIndex, types.CentreIndex, types.RearIndex }.Distinct() )
			{
				var type = types.Types[index];
				var geometry = Find( entry, type.MeshName );
				Assert.IsNotNull( geometry, $"{entry.InfoId}: car model {type.MeshName}" );
				var rig = new CoasterCarRig( geometry! );
				Assert.IsTrue( rig.Parts.Count > 0, $"{entry.InfoId} {type.MeshName} has mesh parts" );
				Assert.IsTrue( rig.FrontExtent + rig.RearExtent > 1, $"{entry.InfoId} {type.MeshName} has a length" );
				foreach ( var part in rig.Parts )
				{
					foreach ( var name in part.Textures )
						Assert.IsNotNull( ObjectAssets.ResolveTexture( entry, name ), $"{entry.InfoId} {type.MeshName}: texture {name}" );
				}
			}
		}
	}
}

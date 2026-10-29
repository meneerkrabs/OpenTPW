using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class Md2CorpusTests
{
	[TestMethod]
	public void EveryOriginalWadMd2MemberParsesExceptTheTwoVersion207Members()
	{
		var dataPath = OriginalDataPath();
		var wads = Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories )
			.Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		Assert.AreEqual( 312, wads.Length );
		int members = 0, geometry = 0, animation = 0;
		long sharedTables = 0, tableBytes = 0;
		long tracks = 0, trsTracks = 0, undecodedTracks = 0, bezierTracks = 0, linearTracks = 0, positionKeys = 0, rotationKeys = 0, easedKeys = 0, scaleKeys = 0;
		long meshes = 0, nodes = 0, faces = 0, corners = 0, positions = 0, materials = 0, untextured = 0, textureSlots = 0, multiFrame = 0, wrappingSlots = 0, clampedCornersOutside = 0, wrappedCornersOutside = 0, extra = 0, attributes = 0;
		var unsupported = new List<string>();
		var failures = new List<string>();
		foreach ( var wad in wads )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in Md2Members( archive.Root, "" ) )
			{
				members++;
				var label = $"{Path.GetRelativePath( dataPath, wad ).Replace( '\\', '/' )}/{name}";
				try
				{
					var model = new ModelFile( new MemoryStream( file.GetData() ) );
					if ( model.Kind == ModelFileKind.Animation )
					{
						animation++;
						// No table reuses payload bytes, so the decode budget leaves 4× headroom.
						sharedTables += model.Clip!.TableBytes > model.Animation!.Offset - ModelFile.HeaderBytes ? 1 : 0;
						tableBytes += model.Clip.TableBytes;
						foreach ( var track in model.Clip!.Tracks )
						{
							tracks++;
							trsTracks += track.Position != null || track.Rotations.Count > 0 || track.Scales.Count > 0 ? 1 : 0;
							undecodedTracks += track.HasUndecodedPayload ? 1 : 0;
							bezierTracks += track.Position?.IsBezier == true ? 1 : 0;
							linearTracks += track.Position?.IsBezier == false ? 1 : 0;
							positionKeys += track.Position?.Times.Count ?? 0;
							rotationKeys += track.Rotations.Count;
							easedKeys += track.Rotations.Count( key => key.Ease >= 0 );
							scaleKeys += track.Scales.Count;
						}
						continue;
					}
					geometry++;
					meshes += model.Meshes.Count;
					nodes += model.Nodes.Count;
					faces += model.Meshes.Sum( mesh => mesh.FaceCount );
					corners += model.Meshes.Sum( mesh => mesh.Vertices.Length );
					positions += model.Meshes.Sum( mesh => mesh.Positions.Length );
					materials += model.Meshes.Sum( mesh => mesh.Materials.Length );
					untextured += model.Meshes.Sum( mesh => mesh.Materials.Count( material => material.TextureIndex < 0 ) );
					textureSlots += model.Textures.Count;
					multiFrame += model.Textures.Count( texture => texture.FrameNames.Count > 1 );
					wrappingSlots += model.Textures.Count( texture => TextureSlotAddressing.Wraps( texture.Flags ) );
					foreach ( var mesh in model.Meshes )
						for ( var corner = 0; corner < mesh.Vertices.Length && corner < mesh.TexCoords.Length; corner++ )
						{
							var material = mesh.Vertices[corner].TextureIndex;
							var uv = mesh.TexCoords[corner];
							if ( material >= mesh.Materials.Length || mesh.Materials[material].TextureIndex < 0 || (uv.X >= 0f && uv.X <= 1f && uv.Y >= 0f && uv.Y <= 1f) )
								continue;
							if ( TextureSlotAddressing.Wraps( mesh.Materials[material].Flags ) )
								wrappedCornersOutside++;
							else
								clampedCornersOutside++;
						}
					extra += model.ExtraRecords.Count;
					attributes += model.DummyAttributes.Count;
				}
				catch ( NotSupportedException )
				{
					unsupported.Add( label );
				}
				catch ( Exception exception )
				{
					failures.Add( $"{label}: {exception.Message}" );
				}
				finally
				{
					file.Free();
				}
			}
		}
		Assert.AreEqual( 0, failures.Count, string.Join( Environment.NewLine, failures ) );
		Assert.AreEqual( 2118, members );
		Assert.AreEqual( 838, geometry );
		Assert.AreEqual( 1278, animation );
		CollectionAssert.AreEquivalent( new[] { "levels/jungle/rides/wateride.wad/wr_tunnel.md2", "levels/jungle/rides/wateride.wad/wr_tunnelm.md2" },
			unsupported.Select( label => label.ToLowerInvariant() ).ToArray() );
		Assert.AreEqual( 4914, meshes );
		Assert.AreEqual( 7520, nodes );
		Assert.AreEqual( 210034, faces );
		Assert.AreEqual( 303199, corners );
		Assert.AreEqual( 133707, positions );
		Assert.AreEqual( 12951, materials );
		Assert.AreEqual( 3, untextured );
		Assert.AreEqual( 7282, textureSlots );
		Assert.AreEqual( 124, multiFrame );
		// tp.exe wraps a slot with flag 0x10 or 0x20 and clamps the rest (TextureSlotAddressing): only wrapping slots have corners
		// outside 0..1, so clamping changes no tiled texture, only how a clamped slot's edge texels filter (no bleed from the opposite edge).
		Assert.AreEqual( 2457, wrappingSlots );
		Assert.AreEqual( 67039, wrappedCornersOutside );
		Assert.AreEqual( 0, clampedCornersOutside );
		Assert.AreEqual( 27, extra );
		Assert.AreEqual( 2452, attributes );
		Assert.AreEqual( 6549, tracks );
		Assert.AreEqual( 3475, trsTracks );
		// Records with more than rigid tracks; the never-read metadata bits (0x8000 on 18 rigid records) do not count.
		Assert.AreEqual( 4064, undecodedTracks );
		Assert.AreEqual( 785, bezierTracks );
		Assert.AreEqual( 431, linearTracks );
		Assert.AreEqual( 8703, positionKeys );
		Assert.AreEqual( 26233, rotationKeys );
		Assert.AreEqual( 12451, easedKeys );
		Assert.AreEqual( 2832, scaleKeys );
		Assert.AreEqual( 0, sharedTables );
		// Includes the texture-coordinate, 12-byte and path-parameter tables (RIDES-041: a 12-byte block's float table holds duration + 1 poses, all read; the 71 path blocks add 78,144 bytes).
		Assert.AreEqual( 8619956, tableBytes );
	}

	/// <summary>
	/// Animations bind to the geometry member whose name is the longest proper prefix of theirs
	/// (header counts are not reliable: spider animations repeat Pspider's 19-node header but
	/// target spider.MD2's 65 nodes). Sampled at 9 ticks with parent-relative composition, base
	/// mesh positions stay within the base header bounds widened by one extent for 1,237 of
	/// 1,275 animations (1,172 when matrices are treated as absolute), matching Python.
	/// </summary>
	[TestMethod]
	public void EveryNamePairedAnimationPlaysAgainstItsBaseModel()
	{
		var dataPath = OriginalDataPath();
		int paired = 0, unpaired = 0, outOfRange = 0, bounded = 0;
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			var models = new List<(string Directory, string Name, ModelFile Model)>();
			foreach ( var (name, file) in Md2Members( archive.Root, "" ) )
			{
				try
				{
					models.Add( (Path.GetDirectoryName( name ) ?? "", Path.GetFileNameWithoutExtension( name ).ToLowerInvariant(), new ModelFile( new MemoryStream( file.GetData() ) )) );
				}
				catch ( NotSupportedException )
				{
				}
				finally
				{
					file.Free();
				}
			}
			foreach ( var clip in models.Where( entry => entry.Model.Kind == ModelFileKind.Animation ) )
			{
				var bases = models.Where( entry => entry.Model.Kind == ModelFileKind.Geometry && entry.Directory == clip.Directory
					&& clip.Name.Length > entry.Name.Length && clip.Name.StartsWith( entry.Name, StringComparison.Ordinal ) ).ToArray();
				if ( bases.Length == 0 )
				{
					unpaired++;
					continue;
				}
				var model = bases.MaxBy( entry => entry.Name.Length ).Model;
				ModelAnimationPlayer player;
				try
				{
					player = new ModelAnimationPlayer( model, clip.Model.Clip!, 30 );
				}
				catch ( InvalidDataException )
				{
					outOfRange++;
					continue;
				}
				paired++;
				bounded += StaysBounded( model, player ) ? 1 : 0;
			}
		}
		Assert.AreEqual( 1275, paired );
		Assert.AreEqual( 2, unpaired );
		Assert.AreEqual( 1, outOfRange );
		// The 36 bus, ferry and seaplane clips (four copies each) follow routes hundreds of units long, far outside their vehicle's box.
		Assert.AreEqual( 1201, bounded );
	}

	private static bool StaysBounded( ModelFile model, ModelAnimationPlayer player )
	{
		var extent = Math.Max( model.BoundsMax.X - model.BoundsMin.X, Math.Max( model.BoundsMax.Y - model.BoundsMin.Y, model.BoundsMax.Z - model.BoundsMin.Z ) );
		var min = new System.Numerics.Vector3( model.BoundsMin.X, model.BoundsMin.Y, model.BoundsMin.Z ) - new System.Numerics.Vector3( extent );
		var max = new System.Numerics.Vector3( model.BoundsMax.X, model.BoundsMax.Y, model.BoundsMax.Z ) + new System.Numerics.Vector3( extent );
		var world = new System.Numerics.Matrix4x4[model.Nodes.Count];
		player.Loop = false;
		for ( var step = 0; step <= 8; step++ )
		{
			player.SetTick( player.Animation.Duration * step / 8f );
			player.ComputeWorldTransforms( world );
			foreach ( var mesh in model.Meshes )
			{
				var stride = Math.Max( 1, mesh.Positions.Length / 8 );
				for ( var index = 0; index < mesh.Positions.Length; index += stride )
				{
					var position = mesh.Positions[index];
					var point = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( position.X, position.Y, position.Z ), world[mesh.NodeIndex] );
					if ( point != System.Numerics.Vector3.Clamp( point, min, max ) )
						return false;
				}
			}
		}
		return true;
	}

	/// <summary>
	/// Vertex, toggle and texture-frame decoding against the paired base models (layouts from the
	/// Feral Mac record sampler, docs/reverse/PPC-formats.md): group 0 holds the two virtual vertices
	/// past the mesh and brackets every animated key at its ticks, the other groups cover the mesh,
	/// every block samples over the whole clip, and texture-frame tracks follow trailer bit 0x2.
	/// </summary>
	[TestMethod]
	public void OriginalVertexTogglesAndTextureFramesDecodeAgainstPairedBases()
	{
		var dataPath = OriginalDataPath();
		int blocks = 0, twelveByte = 0, unsupported = 0, paired = 0, virtualPair = 0, covering = 0, sampled = 0, keysInside = 0, keysOutside = 0;
		int toggleLists = 0, togglesWithinDuration = 0, clips = 0, flagMatchesTracks = 0, frameTracks = 0, framesInRange = 0;
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			var models = new List<(string Directory, string Name, ModelFile Model)>();
			foreach ( var (name, file) in Md2Members( archive.Root, "" ) )
			{
				try
				{
					models.Add( (Path.GetDirectoryName( name ) ?? "", Path.GetFileNameWithoutExtension( name ).ToLowerInvariant(), new ModelFile( new MemoryStream( file.GetData() ) )) );
				}
				catch ( NotSupportedException )
				{
				}
				finally
				{
					file.Free();
				}
			}
			foreach ( var entry in models.Where( entry => entry.Model.Kind == ModelFileKind.Animation ) )
			{
				var clip = entry.Model.Clip!;
				clips++;
				flagMatchesTracks += clip.TextureFramesEnabled == (clip.TextureFrameTracks.Count > 0) ? 1 : 0;
				var bases = models.Where( other => other.Model.Kind == ModelFileKind.Geometry && other.Directory == entry.Directory
					&& entry.Name.Length > other.Name.Length && entry.Name.StartsWith( other.Name, StringComparison.Ordinal ) ).ToArray();
				var model = bases.Length > 0 ? bases.MaxBy( other => other.Name.Length ).Model : null;
				foreach ( var track in clip.TextureFrameTracks )
				{
					frameTracks++;
					if ( model != null && track.Slot < model.Textures.Count && track.Keys.All( key => key.Frame < model.Textures[track.Slot].FrameNames.Count ) )
						framesInRange++;
				}
				foreach ( var track in clip.Tracks )
				{
					unsupported += track.UnsupportedFlags != 0 ? 1 : 0;
					twelveByte += (track.Flags & ModelAnimationTrack.VertexLayoutFlag) != 0 ? 1 : 0;
					if ( (track.Flags & ModelAnimationTrack.NodeFlagToggleFlag) != 0 )
					{
						toggleLists++;
						togglesWithinDuration += track.NodeFlagToggles.All( value => Math.Abs( (int)value ) <= track.Duration ) ? 1 : 0;
					}
					var vertex = track.VertexAnimation;
					if ( vertex == null )
						continue;
					blocks++;
					var node = model?.Nodes.FirstOrDefault( candidate => candidate.StoredIndex == track.NodeIndex );
					if ( model == null || node == null || node.MeshIndex < 0 )
						continue;
					paired++;
					var count = model.Meshes[node.MeshIndex].Positions.Length;
					virtualPair += vertex.BoundsGroup.VertexIndices.SequenceEqual( new[] { (ushort)count, (ushort)(count + 1) } ) ? 1 : 0;
					covering += vertex.Groups.Skip( 1 ).Sum( group => group.VertexIndices.Count ) == count ? 1 : 0;
					var positions = new System.Numerics.Vector3[count];
					if ( vertex.HasStaticGroup )
						vertex.ApplyStaticGroup( positions, false );
					foreach ( var time in new[] { 0f, clip.Duration / 2f, clip.Duration } )
						vertex.ApplyAnimatedGroups( time, positions, false );
					sampled++;
					foreach ( var tick in vertex.BoundsGroup.Ticks )
					{
						var (lower, upper) = vertex.SampleBounds( tick );
						foreach ( var group in vertex.AnimatedGroups )
						{
							var key = group.Ticks.ToList().IndexOf( tick );
							if ( key < 0 )
								continue;
							for ( var index = 0; index < group.VertexIndices.Count; index++ )
							{
								var value = vertex.Dequantise( group.PackedKey( key, index ) );
								if ( value == System.Numerics.Vector3.Clamp( value, lower, upper ) )
									keysInside++;
								else
									keysOutside++;
							}
						}
					}
				}
			}
		}
		Assert.AreEqual( 1736, blocks );
		Assert.AreEqual( 30, twelveByte );
		Assert.AreEqual( 1735, paired );
		Assert.AreEqual( 1735, virtualPair );
		Assert.AreEqual( 1735, covering );
		Assert.AreEqual( 1735, sampled );
		Assert.AreEqual( 0, keysOutside );
		Assert.AreEqual( 713683, keysInside );
		// None: path parameters (0x200/0x400) play along the parent dummy's path, the 30 12-byte records since RIDES-041; 0x10000 is decoded and 0x2000 is metadata.
		Assert.AreEqual( 0, unsupported );
		Assert.AreEqual( 2536, toggleLists );
		Assert.AreEqual( 2536, togglesWithinDuration );
		Assert.AreEqual( 1278, clips );
		Assert.AreEqual( 1278, flagMatchesTracks );
		Assert.AreEqual( 459, frameTracks );
		Assert.AreEqual( 458, framesInRange );
	}

	/// <summary>
	/// The record kinds read in ANIM-R (docs/reverse/ANIM-plan.md §1–§3), as relations over the corpus: every
	/// texture-coordinate block on a paired mesh lists that mesh's corners, its tracks start at 0 and reach the clip
	/// end, and sampling holds the last key there; every 12-byte block matches its mesh's positions; every unread bit
	/// occurs only with its read parent; node lists mostly name nodes of the paired model.
	/// </summary>
	[TestMethod]
	public void OriginalUvBlocksTwelveByteBlocksAndMetadataBitsHoldTheirInvariants()
	{
		var dataPath = OriginalDataPath();
		int uvBlocks = 0, uvPaired = 0, cornerTracks = 0, twelveByte = 0, twelvePaired = 0, poseDiffers = 0, metadataRecords = 0, listedNodes = 0;
		int tickZeroWithinStep = 0, tickZeroCorners = 0, listedOutside = 0;
		var violations = new List<string>();
		var parents = new Dictionary<uint, uint> { [0x10] = 0x8, [0x20] = 0x8, [0x40] = 0x8, [0x8000] = 0x8, [0x100] = 0x80, [0x800] = 0x600, [0x2000] = 0x1000 };
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			var models = new List<(string Directory, string Name, ModelFile Model)>();
			foreach ( var (name, file) in Md2Members( archive.Root, "" ) )
			{
				try
				{
					models.Add( (Path.GetDirectoryName( name ) ?? "", Path.GetFileNameWithoutExtension( name ).ToLowerInvariant(), new ModelFile( new MemoryStream( file.GetData() ) )) );
				}
				catch ( NotSupportedException )
				{
				}
				finally
				{
					file.Free();
				}
			}
			foreach ( var entry in models.Where( entry => entry.Model.Kind == ModelFileKind.Animation ) )
			{
				var clip = entry.Model.Clip!;
				var label = $"{Path.GetFileName( wad )}/{entry.Name}";
				var bases = models.Where( other => other.Model.Kind == ModelFileKind.Geometry && other.Directory == entry.Directory
					&& entry.Name.Length > other.Name.Length && entry.Name.StartsWith( other.Name, StringComparison.Ordinal ) ).ToArray();
				var model = bases.Length > 0 ? bases.MaxBy( other => other.Name.Length ).Model : null;
				if ( model != null )
				{
					// A few entries lie past the paired model (the runtime skips them; ObjectAnimator.ApplyNodeList).
					foreach ( var node in clip.NodeList )
					{
						listedNodes++;
						listedOutside += node >= model.Nodes.Count ? 1 : 0;
					}
				}
				foreach ( var track in clip.Tracks )
				{
					if ( track.MetadataBits != 0 )
						metadataRecords++;
					foreach ( var (bit, parent) in parents )
					{
						if ( (track.Flags & bit) != 0 && (track.Flags & parent) == 0 )
							violations.Add( $"{label}: record {track.NodeIndex} has 0x{bit:X} without 0x{parent:X}" );
					}
					var mesh = model != null && track.NodeIndex < model.Meshes.Count ? model.Meshes[track.NodeIndex] : null;
					if ( track.VertexPose is { } pose )
					{
						twelveByte++;
						if ( (track.Flags & ModelAnimationTrack.VertexAnimationFlag) == 0 )
							violations.Add( $"{label}: 12-byte block without 0x1000" );
						if ( mesh != null )
						{
							twelvePaired++;
							if ( pose.GetLimitation( mesh.Positions.Length ) is { } limitation )
								violations.Add( $"{label}: {limitation}" );
							// RIDES-041: the float table holds one pose per tick, duration + 1 of them (the table ends where the next one starts).
							else
							{
								if ( pose.FrameCount != track.Duration + 1 )
									violations.Add( $"{label}: the 12-byte block holds {pose.FrameCount} poses for duration {track.Duration}" );
								var shown = new System.Numerics.Vector3[mesh.Positions.Length];
								pose.ApplyPose( 0, shown );
								poseDiffers += shown.Where( ( position, index ) => ( position - mesh.Positions[index].GetSystemVector3() ).Length() > 1e-3f ).Any() ? 1 : 0;
							}
						}
					}
					if ( track.UvAnimation is not { } uv )
						continue;
					uvBlocks++;
					if ( mesh == null )
						continue;
					uvPaired++;
					if ( uv.CornerCount != mesh.TexCoords.Length )
					{
						violations.Add( $"{label}: UV block has {uv.CornerCount} corners, mesh {mesh.TexCoords.Length}" );
						continue;
					}
					for ( var corner = 0; corner < uv.CornerCount; corner++ )
					{
						cornerTracks++;
						var (first, count) = uv.GetTrack( corner );
						if ( uv.Ticks[first + count - 1] < clip.Duration )
							violations.Add( $"{label}: UV corner {corner} ends before the clip" );
						// At the last key the search holds it (count − 2, fraction 1).
						if ( uv.FindKey( corner, uv.Ticks[first + count - 1], out var fraction ) != count - 2 || fraction != 1f )
							violations.Add( $"{label}: UV corner {corner} does not hold its last key" );
						var sample = uv.Sample( corner, 0 );
						var stored = mesh.TexCoords[corner];
						tickZeroCorners++;
						if ( MathF.Abs( sample.X - stored.X ) <= OneStep( stored.X ) && MathF.Abs( sample.Y - stored.Y ) <= OneStep( stored.Y ) )
							tickZeroWithinStep++;
					}
				}
			}
		}
		Assert.AreEqual( 0, violations.Count, string.Join( Environment.NewLine, violations.Take( 20 ) ) );
		// 19 of the 30 records start on a pose that differs from the stored mesh, which is why showing the stored mesh was wrong.
		Assert.AreEqual( (30, 19), (twelvePaired, poseDiffers) );
		Assert.IsTrue( uvBlocks > 0 && uvPaired > 0 && cornerTracks > 0 && twelvePaired > 0 && metadataRecords > 0 && listedNodes > 0 );
		Assert.IsTrue( uvPaired <= uvBlocks && twelvePaired <= twelveByte && listedOutside < listedNodes );
		// Measured, not pinned (ANIM-plan §13 leaves it open): how often a corner's tick-0 key equals the stored coordinate.
		Console.WriteLine( $"UV blocks {uvBlocks}, paired {uvPaired}, corner tracks {cornerTracks}; tick 0 within one float step of the stored UV: {tickZeroWithinStep}/{tickZeroCorners}; 12-byte blocks {twelveByte} ({twelvePaired} paired); metadata records {metadataRecords}; node-list entries {listedNodes} ({listedOutside} outside the model)" );
	}

	/// <summary>
	/// Path-parameter blocks (record +36, flag 0x200; [BIN:TPW-EXE:0x00471ECF path parameter]): one value per tick
	/// from tick 0, the header word +8 unused, and every flag-0x200 record has one. 0x400 never occurs without 0x200.
	/// </summary>
	[TestMethod]
	public void OriginalPathParameterBlocksHoldOneValuePerTick()
	{
		var dataPath = OriginalDataPath();
		int blocks = 0, orientation = 0;
		var violations = new List<string>();
		IReadOnlyList<float>? kart = null;
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in Md2Members( archive.Root, "" ) )
			{
				ModelFile model;
				try
				{
					model = new ModelFile( new MemoryStream( file.GetData() ) );
				}
				catch ( NotSupportedException )
				{
					continue;
				}
				finally
				{
					file.Free();
				}
				if ( model.Kind != ModelFileKind.Animation )
					continue;
				foreach ( var track in model.Clip!.Tracks )
				{
					var label = $"{Path.GetFileName( wad )}/{name} record {track.NodeIndex}";
					if ( (track.Flags & 0x400) != 0 && (track.Flags & 0x200) == 0 )
						violations.Add( $"{label}: 0x400 without 0x200" );
					orientation += (track.Flags & 0x400) != 0 ? 1 : 0;
					if ( ((track.Flags & 0x200) != 0) != (track.PathParameters != null) )
						violations.Add( $"{label}: flag 0x200 and the path block disagree" );
					if ( track.PathParameters is not { } path )
						continue;
					blocks++;
					if ( name.Equals( "gokartsm.md2", StringComparison.OrdinalIgnoreCase ) && track.NodeIndex == 5 )
						kart = path.Values;
					if ( path.StartTick != 0 || path.Reserved != 0 || path.Values.Count != track.Duration + 1 )
						violations.Add( $"{label}: start {path.StartTick}, reserved {path.Reserved}, {path.Values.Count} values for duration {track.Duration}" );
					if ( track.Words[9] + ModelPathParameters.HeaderBytes != BitConverter.ToUInt32( file.GetData(), (int)track.Words[9] + 12 ) )
						violations.Add( $"{label}: the value table does not follow the header" );
				}
			}
		}
		Assert.AreEqual( 0, violations.Count, string.Join( Environment.NewLine, violations.Take( 20 ) ) );
		Assert.AreEqual( (71, 62), (blocks, orientation) );
		// The go-kart clip goes once round its track: its values climb from 13.68 to 113.68, one path (100 %) further.
		Assert.IsNotNull( kart );
		Assert.AreEqual( 201, kart.Count );
		Assert.AreEqual( 13.682f, kart[0], 0.001f );
		Assert.AreEqual( 100f, kart[^1] - kart[0], 0.01f );
	}

	/// <summary>
	/// Paths ([BIN:TPW-EXE:0x00471F1D path evaluation]): every 0xAC record is a path, every flag-0x200 node hangs under a
	/// type-0x208 dummy that selects one, and where a clip starts the node on its stored pose the evaluated path agrees
	/// with that pose: the position within 0.02 and, with flag 0x400, the facing within a few degrees.
	/// </summary>
	[TestMethod]
	public void OriginalPathsStartFlagged200NodesOnTheirStoredPose()
	{
		var dataPath = OriginalDataPath();
		int paths = 0, followers = 0, posed = 0;
		var violations = new List<string>();
		// Path count per distinct geometry member name (the same model recurs in several themes' archives).
		var distinct = new Dictionary<string, int>( StringComparer.Ordinal );
		// Clips whose first tick shows the stored pose (the others start elsewhere on the route).
		var startOnPose = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { "hauntB.MD2", "hauntE.MD2", "hauntM.MD2", "hauntS.MD2", "Busm1.MD2", "slidec.MD2" };
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			var models = new List<(string Name, ModelFile Model)>();
			foreach ( var (name, file) in Md2Members( archive.Root, "" ) )
			{
				try
				{
					models.Add( (name, new ModelFile( new MemoryStream( file.GetData() ) )) );
				}
				catch ( NotSupportedException )
				{
				}
				finally
				{
					file.Free();
				}
			}
			foreach ( var (name, model) in models.Where( entry => entry.Model.Kind == ModelFileKind.Geometry && entry.Model.Paths.Count > 0 ) )
			{
				distinct[Path.GetFileName( name )] = model.Paths.Count;
				foreach ( var path in model.Paths )
				{
					paths++;
					if ( path.Kind is not (2 or 3 or 8) || path.Keys != 0 || path.Reserved != 0 || path.SegmentCount <= 0 )
						violations.Add( $"{name}: path kind {path.Kind}, {path.Points.Count} points, keys {path.Keys}, reserved {path.Reserved}" );
				}
				var stem = Path.GetFileNameWithoutExtension( name );
				foreach ( var (clipName, clip) in models.Where( entry => entry.Model.Kind == ModelFileKind.Animation
					&& Path.GetFileNameWithoutExtension( entry.Name ).StartsWith( stem, StringComparison.OrdinalIgnoreCase ) ) )
				{
					foreach ( var track in clip.Clip!.Tracks.Where( track => (track.Flags & ModelAnimationTrack.PathParameterFlag) != 0 ) )
					{
						followers++;
						if ( model.GetPathOf( track.NodeIndex ) is not { } path )
						{
							violations.Add( $"{clipName} node {track.NodeIndex}: no path dummy above it" );
							continue;
						}
						if ( !track.TrySamplePath( path, 0, out var position, out var direction ) )
						{
							violations.Add( $"{clipName} node {track.NodeIndex}: no path pose at tick 0" );
							continue;
						}
						if ( !startOnPose.Contains( Path.GetFileName( clipName ) ) )
							continue;
						posed++;
						var stored = model.Nodes[track.NodeIndex].Transform;
						if ( System.Numerics.Vector3.Distance( position, stored.Translation ) > 0.02f )
							violations.Add( $"{clipName} node {track.NodeIndex}: tick 0 at {position}, stored at {stored.Translation}" );
						if ( direction is { } along && System.Numerics.Vector3.Dot( along, System.Numerics.Vector3.Normalize( new System.Numerics.Vector3( stored.M31, stored.M32, stored.M33 ) ) ) < 0.99f )
							violations.Add( $"{clipName} node {track.NodeIndex}: faces {along}, stored row 2 {stored.M31}, {stored.M32}, {stored.M33}" );
					}
				}
			}
		}
		Assert.AreEqual( 0, violations.Count, string.Join( Environment.NewLine, violations.Take( 20 ) ) );
		Console.WriteLine( $"{paths} paths, {followers} path-following records, {posed} checked against the stored pose" );
		Assert.AreEqual( (15, 18), (distinct.Count, distinct.Values.Sum()), string.Join( ", ", distinct.Select( entry => $"{entry.Key}:{entry.Value}" ) ) );
		Assert.IsTrue( paths >= 18 && followers >= 71 && posed >= 18, $"{paths} paths, {followers} followers, {posed} posed" );
	}

	/// <summary>
	/// A placed Haunted House plays hauntB: each kart (nodes 12–15, children of the Kart_path dummies) is posed on its
	/// path by the object animator, facing along it, and moves as the clip runs.
	/// </summary>
	[TestMethod]
	public void PlacedHauntedHouseKartsFollowTheirPaths()
	{
		var model = new ModelFile( new MemoryStream( ReadMember( "levels/hallow/rides/haunt.wad", "haunt.MD2" ) ) );
		var clip = new ModelFile( new MemoryStream( ReadMember( "levels/hallow/rides/haunt.wad", "hauntB.MD2" ) ) ).Clip!;
		var animator = new ObjectAnimator( model );
		animator.Play( 0, clip, "hauntB", loop: true );
		var previous = new System.Numerics.Vector3[4];
		foreach ( var seconds in new[] { 0.5, 1.75, 2.0 } )
		{
			animator.Advance( seconds );
			var tick = animator.GetTick( 0 );
			for ( var kart = 0; kart < 4; kart++ )
			{
				var node = 12 + kart;
				var path = model.GetPathOf( node )!;
				Assert.IsTrue( clip.Tracks.Single( track => track.NodeIndex == node ).TrySamplePath( path, tick, out var expected, out var direction ) );
				System.Numerics.Matrix4x4.Invert( animator.NodeTransforms[model.Nodes[node].ParentIndex], out var toDummy );
				var local = animator.NodeTransforms[node] * toDummy;
				Assert.IsTrue( System.Numerics.Vector3.Distance( expected, local.Translation ) < 1e-3f, $"kart {kart} at tick {tick}: {local.Translation}, path {expected}" );
				Assert.IsTrue( System.Numerics.Vector3.Dot( direction!.Value, new System.Numerics.Vector3( local.M31, local.M32, local.M33 ) ) > 0.999f, $"kart {kart} faces along the path" );
				Assert.AreNotEqual( previous[kart], local.Translation, $"kart {kart} moves" );
				previous[kart] = local.Translation;
			}
		}

		// The clip player (advisor, prototype ride) poses path followers the same way.
		var player = new ModelAnimationPlayer( model, clip, 30 );
		player.Advance( 1.5f );
		Assert.IsTrue( clip.Tracks.Single( track => track.NodeIndex == 13 ).TrySamplePath( model.GetPathOf( 13 )!, player.Tick, out var onPath, out _ ) );
		Assert.IsTrue( System.Numerics.Vector3.Distance( onPath, player.LocalTransform( 13 ).Translation ) < 1e-4f );
	}

	private static float OneStep( float value ) => MathF.BitIncrement( MathF.Abs( value ) ) - MathF.Abs( value );

	[TestMethod]
	public void OriginalTotemAnimationSamplesMatchIndependentDecode()
	{
		var model = new ModelFile( new MemoryStream( ReadMember( "levels/jungle/rides/totem.wad", "totem.MD2" ) ) );
		var clip = new ModelFile( new MemoryStream( ReadMember( "levels/jungle/rides/totem.wad", "totemm1.MD2" ) ) ).Clip!;
		Assert.AreEqual( 430, clip.Duration );
		CollectionAssert.AreEqual( new[] { "tp_cart", "tp_cog", "tp_cog01" }, clip.Tracks.Select( track => model.Nodes[track.NodeIndex].Name ).ToArray() );
		var cart = clip.Tracks[0];
		Assert.IsTrue( cart.Position!.IsBezier );
		Assert.AreEqual( 25, cart.Position.Points.Count );
		CollectionAssert.AreEqual( new uint[] { 0, 177, 185, 211, 214, 257, 280, 380, 430 }, cart.Position.Times.ToArray() );
		Assert.AreEqual( 0, System.Numerics.Vector3.Distance( model.Nodes[2].Transform.Translation, cart.SampleTranslation( 0 )!.Value ), 1e-5 ); // starts at the rest pose
		// Expected values from a separate Python decoder over the raw bytes.
		foreach ( var (tick, height) in new[] { (50f, 8.412308f), (88.5f, 24.917468f), (177f, 47.565502f), (181f, 46.635943f), (235.5f, 6.341847f), (300f, -33.524913f), (430f, -2.404270f) } )
			Assert.AreEqual( height, cart.SampleTranslation( tick )!.Value.Y, 1e-4, $"tick {tick}" );
		var cog = clip.Tracks[1];
		Assert.AreEqual( 11, cog.Rotations.Count );
		Assert.AreEqual( 3, cog.EaseCurves.Count );
		foreach ( var (tick, y, w) in new[] { (50f, 0.9555728f, -0.2947551f), (88.5f, -0.1675061f, -0.9858710f), (181f, 0.5440980f, 0.8390217f), (300f, 0.7829279f, -0.6221125f) } )
		{
			var rotation = cog.SampleRotation( tick )!.Value;
			// The decoder's values use exact sines; the clip sampler blends with the table sines of 0x4742A0 (ModelRotationBlend), which differ by up to a few 1e-4.
			Assert.AreEqual( y, rotation.Y, 1e-3, $"tick {tick}" );
			Assert.AreEqual( w, rotation.W, 1e-3, $"tick {tick}" );
		}
		// At a key tick the fraction is 0 and the blend returns the key itself: no table-sine error, so these hold to 1e-4.
		foreach ( var key in cog.Rotations.Take( cog.Rotations.Count - 1 ) )
		{
			var atKey = cog.SampleRotation( key.Time )!.Value;
			Assert.AreEqual( 1f, MathF.Abs( System.Numerics.Quaternion.Dot( System.Numerics.Quaternion.Normalize( key.Rotation ), atKey ) ), 1e-4, $"key at tick {key.Time}" );
		}
		Assert.AreEqual( -clip.Tracks[1].SampleRotation( 50 )!.Value.Y, clip.Tracks[2].SampleRotation( 50 )!.Value.Y, 1e-6 );

		// Cart children (seat dummies) follow the animated cart because matrices are parent-relative.
		var player = new ModelAnimationPlayer( model, clip, 30 );
		var world = new System.Numerics.Matrix4x4[model.Nodes.Count];
		player.SetTick( 177 );
		player.ComputeWorldTransforms( world );
		var head = model.Nodes.Single( node => node.Name == "Head01" );
		Assert.AreEqual( 47.565502f + head.Transform.Translation.Y, world[head.Index].Translation.Y, 1e-3 );
	}

	[DataTestMethod]
	[DataRow( "levels/jungle/rides/totem.wad", "totem.MD2", "1B499E5A2E43EBB3AC4E9252A24A66F4A2EFE0285AFE522A573CB226438BA42A", 13, 31, 34, 316, "E0B32787AA1916F403C4E952B2F7025E65866BFCF25F8EC98297967E1EBB4F52" )]
	[DataRow( "global/advisor.wad", "Advisor.MD2", "F518A91BCEBD1770A2927A0143549A547D02EED5EE91705E626FC08C2BC3B51C", 25, 29, 41, 1321, "A0174988C4A8D423F329C85BB7B5187F9F71CF157BFE8CAD82AB4030D57FE733" )]
	[DataRow( "levels/fantasy/features/gates.wad", "gates.MD2", "1F3013C8140BE310AE885FF240D96BE228B18DB4B36120A91217B081BF9A454D", 2, 5, 9, 544, "66DA2A022E956B79081EAB4FFEB48B9593DBCAB66270987FC9FDF967DB39532D" )]
	[DataRow( "levels/fantasy/terrain.wad", "base.MD2", "1B783EE1735855575E6F8D2BBFA694B4A27D2F04EC979F4ADDC9DA049514F495", 195, 196, 40, 17450, "3AD596D88E48ACA3BB05365D806E23517B29EEE152779C7568C64A7B090F586E" )]
	public void OriginalGeometryMatchesIdentityAndIndependentGeometryHash( string wad, string member, string fileHash, int meshCount, int nodeCount, int textureCount, int faceCount, string geometryHash )
	{
		var data = ReadMember( wad, member );
		Assert.AreEqual( fileHash, Convert.ToHexString( SHA256.HashData( data ) ) );
		var model = new ModelFile( new MemoryStream( data ) );
		Assert.AreEqual( ModelFileKind.Geometry, model.Kind );
		Assert.AreEqual( meshCount, model.Meshes.Count );
		Assert.AreEqual( nodeCount, model.Nodes.Count );
		Assert.AreEqual( textureCount, model.Textures.Count );
		Assert.AreEqual( faceCount, model.Meshes.Sum( mesh => (int)mesh.FaceCount ) );
		Assert.AreEqual( geometryHash, GeometryHash( model ) );
	}

	[TestMethod]
	public void OriginalTotemDetailsAndAnimationSignature()
	{
		var model = new ModelFile( new MemoryStream( ReadMember( "levels/jungle/rides/totem.wad", "totem.MD2" ) ) );
		Assert.AreEqual( "tp_ground", model.Nodes[model.RootNodeIndex].Name );
		var cart = model.Meshes.Single( mesh => mesh.Name == "tp_cart" );
		var cartDummies = model.Nodes.Where( node => node.ParentIndex == cart.NodeIndex && node.MeshIndex < 0 ).Select( node => node.Name ).ToArray();
		CollectionAssert.AreEquivalent( Enumerable.Range( 1, 12 ).Select( index => $"Head{index:00}" ).Append( "camera01" ).ToArray(), cartDummies );
		Assert.AreEqual( 5, model.Nodes.Count( node => node.ParentIndex == model.RootNodeIndex && node.MeshIndex < 0 ) );
		Assert.AreEqual( 18, model.DummyAttributes.Count );
		Assert.AreEqual( 5, model.Textures.Count( texture => texture.FrameNames[0].Equals( "jt_f1.TGA", StringComparison.Ordinal ) ) );

		var data = ReadMember( "levels/jungle/rides/totem.wad", "totemm1.MD2" );
		Assert.AreEqual( "6467B35FAADD40FEF639C2B10F3F0BB2754675F15509172778EF399B74AE76FE", Convert.ToHexString( SHA256.HashData( data ) ) );
		var animation = new ModelFile( new MemoryStream( data ) );
		Assert.AreEqual( ModelFileKind.Animation, animation.Kind );
		Assert.AreEqual( model.Counts, animation.Counts );
		Assert.AreEqual( model.BoundsMin, animation.BoundsMin );
		Assert.AreEqual( 0x4F0, animation.Animation!.Offset );
		Assert.AreEqual( 430u, animation.Animation.Words[2] );
	}

	/// <summary>
	/// Per mesh: name + NUL; per corner float32 position X/Y/Z and U/V; per face uint32 corner indices;
	/// per material int32 texture slot and uint16 start/end. Matches an independent Python calculation.
	/// </summary>
	private static string GeometryHash( ModelFile model )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream, Encoding.ASCII, true );
		foreach ( var mesh in model.Meshes )
		{
			writer.Write( Encoding.ASCII.GetBytes( mesh.Name ) );
			writer.Write( (byte)0 );
			for ( var corner = 0; corner < mesh.Vertices.Length; corner++ )
			{
				writer.Write( mesh.Vertices[corner].Position.X );
				writer.Write( mesh.Vertices[corner].Position.Y );
				writer.Write( mesh.Vertices[corner].Position.Z );
				writer.Write( mesh.TexCoords[corner].X );
				writer.Write( mesh.TexCoords[corner].Y );
			}
			foreach ( var index in mesh.Indices )
				writer.Write( index );
			foreach ( var material in mesh.Materials )
			{
				writer.Write( material.TextureIndex );
				writer.Write( material.StartIndex );
				writer.Write( material.EndIndex );
			}
		}
		writer.Flush();
		return Convert.ToHexString( SHA256.HashData( stream.ToArray() ) );
	}

	internal static IEnumerable<(string Name, WadArchiveFile File)> Md2Members( ArchiveDirectory directory, string prefix )
	{
		foreach ( var child in directory.Children )
		{
			if ( child is ArchiveDirectory subdirectory )
			{
				foreach ( var entry in Md2Members( subdirectory, $"{prefix}{subdirectory.Name}/" ) )
					yield return entry;
			}
			else if ( child is WadArchiveFile file && file.Name!.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) )
			{
				yield return ($"{prefix}{file.Name}", file);
			}
		}
	}

	private static byte[] ReadMember( string wad, string member )
	{
		var path = Path.Combine( OriginalDataPath(), wad );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( $"Original archive is missing: {wad}" );
		using var archive = new WadArchive( path );
		var file = Md2Members( archive.Root, "" ).Where( entry => entry.Name == member ).Select( entry => entry.File ).FirstOrDefault();
		if ( file == null )
			Assert.Inconclusive( $"Original member is missing: {wad}/{member}" );
		return file!.GetData();
	}

	internal static string OriginalDataPath()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original MD2 corpus." );
		return Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
	}
}

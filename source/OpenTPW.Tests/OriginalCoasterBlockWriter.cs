using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// Test-only writer of an original Coasters block: the inverse of the decoded reader (<see cref="SaveCoasterBlock"/>), field for field in the order tp.exe's saver
/// <c>0x437300</c> writes them (docs/TPWS-PAYLOAD.md "Coasters block"), from a coaster OpenTPW built. The node flags the original keeps on a node and OpenTPW derives
/// instead are written as the original holds them: the cut's link bits with its markers (<c>0x446740</c> marks 0x50000 and 0xA0000), the zero-bank bit on every node of
/// a stack but the top one (the stack pass <c>0x432D50</c>). Nothing writes an original chunk in production; this only makes synthetic blocks for the import tests.
/// </summary>
internal static class OriginalCoasterBlockWriter
{
	/// <summary>The header words the runtime does not hold.</summary>
	internal sealed record HeaderWords( ushort StationMesh, ushort Slot = 1, ushort TypeId = 19, ushort Placement1 = 69, ushort Placement2 = 65, uint TrackFlags = 0 );

	/// <summary>One coaster's bytes (header, auxiliary block, sections, every train slot) as the saver writes them.</summary>
	internal static byte[] Coaster( CoasterRide ride, HeaderWords words, Func<int, int, IReadOnlyList<int>>? riders = null )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		var track = ride.Track;
		var nodes = track.Nodes;
		var lists = ride.CollisionLists;
		IReadOnlyList<int> ListOf( int index ) => index < lists.Count ? lists[index] : Array.Empty<int>();
		// the saver skips the station nodes (descriptor 0x12) and the end node (0x13): section i is chain node 3 + i
		var sections = Enumerable.Range( 0, nodes.Count ).Where( index => !nodes[index].IsStation && !nodes[index].IsStationEnd ).ToList();
		var wire = (track.IsComplete ? 0x1u : 0) | (track.IsCut ? 0x2u : 0) | words.TrackFlags;
		var mode = ride.BreakdownState switch { 1 => 0x40u, 2 => 0x20u, _ => 0x10u };
		writer.Write( wire );
		writer.Write( (uint)ride.State | mode );
		writer.Write( words.TypeId );
		writer.Write( words.StationMesh );
		writer.Write( words.Slot );
		writer.Write( words.Placement1 );
		writer.Write( words.Placement2 );
		writer.Write( ride.PowerFactor );
		writer.Write( (ushort)sections.Count );
		writer.Write( (ushort)ride.Trains.Count );
		writer.Write( (ushort)ride.SeatsPerTrain );
		writer.Write( (uint)ride.IntersectionCount );
		// the auxiliary block: station node S2's collision list, written where the chain walk meets the node with node+0 == 2
		var auxiliary = ListOf( CoasterTrack.StationNodeCount - 1 );
		writer.Write( (uint)auxiliary.Count );
		foreach ( var ordinal in auxiliary )
			writer.Write( (ushort)ordinal );
		foreach ( var index in sections )
		{
			var node = nodes[index];
			var flags = node.Flags;
			if ( (flags & CoasterNodeFlags.CutEnd) != 0 )
				flags |= CoasterNodeFlags.NoNextLink;
			if ( (flags & CoasterNodeFlags.CutStart) != 0 )
				flags |= CoasterNodeFlags.NoPreviousLink;
			if ( nodes.Any( other => !other.IsPylonless && other.X == node.X && other.Y == node.Y && other.StackIndex > node.StackIndex ) )
				flags |= CoasterNodeFlags.ZeroBank;
			var loopNode = (node.Flags & CoasterNodeFlags.Loop) != 0;
			writer.Write( SaveCoasterSectionFlags.ToWire( (uint)flags ) );
			writer.Write( 1000u + (uint)index );
			writer.Write( (ushort)node.X );
			writer.Write( (ushort)node.Y );
			writer.Write( (byte)node.StackIndex );
			writer.Write( node.T );
			// node +0xD8, +0xDC: a loop node holds neither (0x444270), its loop rotate +0xF8 is the record's last float; any other node's +0xF8 is 0
			writer.Write( loopNode ? 0f : node.Bank );
			writer.Write( loopNode ? 0f : node.Incline );
			writer.Write( node.Yaw );
			writer.Write( loopNode ? node.LoopRotate : 0f );
			var collisions = ListOf( index );
			writer.Write( (byte)collisions.Count );
			foreach ( var ordinal in collisions )
				writer.Write( (ushort)ordinal );
		}
		var definition = ride.Definition;
		for ( var slot = 0; slot < definition.MaxTrains; slot++ )
		{
			var train = slot < ride.Trains.Count ? ride.Trains[slot] : null;
			var carRiders = Enumerable.Range( 0, definition.CarCount ).Select( car => train == null ? Array.Empty<int>() : riders?.Invoke( slot, car ) ?? Array.Empty<int>() ).ToList();
			// a slot the rating pass set up carries bits 0 and 2 (0x43CAD6) and 0x2 from the slot set-up (0x43BE10); the loader ORs 4 again
			writer.Write( train == null ? 0u : 0x7u | (uint)train.Flags );
			writer.Write( (byte)definition.CarCount );
			writer.Write( BitConverter.SingleToUInt32Bits( train?.Position ?? 0f ) );
			writer.Write( BitConverter.SingleToUInt32Bits( train?.Speed ?? 0f ) );
			writer.Write( 0u );
			writer.Write( 0u );
			writer.Write( 0u );
			writer.Write( (ushort)carRiders.Sum( list => list.Count ) );
			for ( var car = 0; car < definition.CarCount; car++ )
			{
				writer.Write( train == null ? 0u : 1u );
				writer.Write( (byte)carRiders[car].Count );
				writer.Write( (byte)0 );
				writer.Write( (byte)(train?.Seats[car] ?? 0) );
				writer.Write( 2000u + (uint)(slot * 16 + car) );
				foreach ( var guest in carRiders[car] )
					writer.Write( (uint)guest );
			}
		}
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>A whole Coasters block: the count, the slot allocator's globals, then the coasters.</summary>
	internal static byte[] Block( uint global1, uint global2, uint global3, params byte[][] coasters )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( (uint)coasters.Length );
		writer.Write( global1 );
		writer.Write( global2 );
		writer.Write( global3 );
		foreach ( var coaster in coasters )
			writer.Write( coaster );
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>Parses <paramref name="block"/> with the coaster type's train layout, as the park import does.</summary>
	internal static SaveCoasterBlock Parse( byte[] block, CoasterDefinition definition ) =>
		SaveCoasterBlock.ParseBlock( block, 128, 128, 0, _ => new SaveCoasterTrainLayout( definition.MaxTrains, definition.CarCount ) );
}

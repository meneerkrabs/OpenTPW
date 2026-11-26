using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>A 16.16 fixed-point number as the navigation reader stores it (cells).</summary>
public readonly record struct SaveFixed( int Raw )
{
	public double Value => Raw / 65536.0;
	public override string ToString() => Value.ToString( System.Globalization.CultureInfo.InvariantCulture );
}

/// <summary>A pair of 16.16 fixed-point numbers (x then y, in cells).</summary>
public readonly record struct SaveFixedVector( SaveFixed X, SaveFixed Y );

/// <summary>One of the five sub-path points of the navigation state: the buffer position and the distance to it.</summary>
public readonly record struct SaveSubpath( SaveFixedVector Buffer, SaveFixed Distance );

/// <summary>
/// The guest/staff navigation state (177 bytes, tp.exe reader <c>0x50D150</c>, field names from the reader's own log strings): every value is 16.16 fixed point in cells
/// unless noted. Unused sub-path distances hold the debug fill <c>0xCDCDCDCD</c>. <see cref="Raw"/> keeps the 177 bytes as read. Theme Park Inc's serializer <c>0x63AD30</c> has the
/// same order and widths (docs/reverse/TPI-NAVIGATOR.md); <see cref="Write"/> is its save half and <c>GuestNavigation.ToRecord</c> / <c>LoadRecord</c> map it onto the walk.
/// </summary>
// [BIN:TPW-EXE:0x50D150 navigation state reader] force, formation_pos, local_xaxis, local_yaxis, mass, max_force, max_speed, mCantReachDest, nav_mode, path_buffer_count, path_count, path_finished, path_last_progress, path_stuck_buffer, path_subpath_dist, path_tail_dist, path_target_pos, path_timestamp, path_total_count, path_total_dist, pos, radius, 5 x (subpath_buffer, subpath_dist), vel
public sealed record SaveNavigation(
	SaveFixedVector Force, SaveFixedVector FormationPos, SaveFixedVector LocalXAxis, SaveFixedVector LocalYAxis,
	SaveFixed Mass, SaveFixed MaxForce, SaveFixed MaxSpeed, int CantReachDest, int NavMode, int PathBufferCount, int PathCount, byte PathFinished,
	SaveFixed PathLastProgress, SaveFixed PathStuckBuffer, SaveFixed PathSubpathDist, SaveFixed PathTailDist, SaveFixedVector PathTargetPos,
	int PathTimestamp, int PathTotalCount, SaveFixed PathTotalDist, SaveFixedVector Pos, SaveFixed Radius, IReadOnlyList<SaveSubpath> Subpaths,
	SaveFixedVector Vel, byte[] Raw )
{
	public const int Bytes = 177;
	public const int SubpathCount = 5;
	/// <summary>The fill of an unused sub-path distance.</summary>
	public const uint UnusedDistance = 0xCDCDCDCD;

	public static SaveNavigation Read( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length != Bytes )
			throw new InvalidDataException( $"Save navigation state is {bytes.Length} bytes; expected {Bytes}." );
		var data = bytes.ToArray();
		var position = 0;
		int I32()
		{
			var value = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( position ) );
			position += 4;
			return value;
		}
		SaveFixed Fixed() => new( I32() );
		SaveFixedVector Vector() => new( Fixed(), Fixed() );
		var (force, formation, xAxis, yAxis) = (Vector(), Vector(), Vector(), Vector());
		var (mass, maxForce, maxSpeed) = (Fixed(), Fixed(), Fixed());
		var (cantReach, navMode, bufferCount, pathCount) = (I32(), I32(), I32(), I32());
		var finished = data[position++];
		var (lastProgress, stuckBuffer, subpathDist, tailDist) = (Fixed(), Fixed(), Fixed(), Fixed());
		var target = Vector();
		var (timestamp, totalCount) = (I32(), I32());
		var totalDist = Fixed();
		var pos = Vector();
		var radius = Fixed();
		var subpaths = new SaveSubpath[SubpathCount];
		for ( var index = 0; index < subpaths.Length; index++ )
			subpaths[index] = new SaveSubpath( Vector(), Fixed() );
		var vel = Vector();
		if ( position != Bytes )
			throw new InvalidOperationException( "The navigation reader does not cover the navigation state." );
		return new SaveNavigation( force, formation, xAxis, yAxis, mass, maxForce, maxSpeed, cantReach, navMode, bufferCount, pathCount, finished, lastProgress, stuckBuffer,
			subpathDist, tailDist, target, timestamp, totalCount, totalDist, pos, radius, subpaths, vel, bytes.ToArray() );
	}

	/// <summary>
	/// The 177 bytes of the members (not <see cref="Raw"/>), in the order and widths <see cref="Read"/> takes them: the save half of the serializer (Theme Park Inc <c>0x63AD30</c> mode 1,
	/// tp.exe <c>0x50D150</c>), which writes every member under its name, alphabetically, the five (subpath_buffer[p], subpath_dist[p]) pairs interleaved. Read(Write()) gives the members back.
	/// </summary>
	// [BIN:TPI-EXE:0x0063AD30 navigator serializer] mode 1 (save, versions 0..1): force 8, formation_pos 8, local_xaxis 8, local_yaxis 8, mass 4, max_force 4, max_speed 4, mCantReachDest 4, nav_mode 4, path_buffer_count 4, path_count 4, path_finished 1, path_last_progress 4, path_stuck_buffer 4, path_subpath_dist 4, path_tail_dist 4, path_target_pos 8, path_timestamp 4, path_total_count 4, path_total_dist 4, pos 8, radius 4, 5 x (subpath_buffer[p] 8, subpath_dist[p] 4), vel 8 through the stream's write 0x7CC960, each under its member name (0x441DA0); a short transfer returns 1; mode 0 (0x63B583) reads the same and zeroes the unnamed +0x30 (0x63B9AB, 0x63B64A)
	public byte[] Write()
	{
		if ( Subpaths.Count != SubpathCount )
			throw new InvalidOperationException( $"The navigation state has {Subpaths.Count} sub-paths; the record holds {SubpathCount}." );
		var data = new byte[Bytes];
		var position = 0;
		void I32( int value )
		{
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( position ), value );
			position += 4;
		}
		void Fixed( SaveFixed value ) => I32( value.Raw );
		void Vector( SaveFixedVector value )
		{
			Fixed( value.X );
			Fixed( value.Y );
		}
		Vector( Force );
		Vector( FormationPos );
		Vector( LocalXAxis );
		Vector( LocalYAxis );
		Fixed( Mass );
		Fixed( MaxForce );
		Fixed( MaxSpeed );
		I32( CantReachDest );
		I32( NavMode );
		I32( PathBufferCount );
		I32( PathCount );
		data[position++] = PathFinished;
		Fixed( PathLastProgress );
		Fixed( PathStuckBuffer );
		Fixed( PathSubpathDist );
		Fixed( PathTailDist );
		Vector( PathTargetPos );
		I32( PathTimestamp );
		I32( PathTotalCount );
		Fixed( PathTotalDist );
		Vector( Pos );
		Fixed( Radius );
		foreach ( var subpath in Subpaths )
		{
			Vector( subpath.Buffer );
			Fixed( subpath.Distance );
		}
		Vector( Vel );
		if ( position != Bytes )
			throw new InvalidOperationException( "The navigation writer does not cover the navigation state." );
		return data;
	}
}

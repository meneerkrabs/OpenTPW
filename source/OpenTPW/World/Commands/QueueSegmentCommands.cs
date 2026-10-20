namespace OpenTPW;

/// <summary>Version2 native ordered queue passes. Version1 LayLine remains the legacy per-cell operation.</summary>
public sealed record QueueSegmentCommand( int Ride, int StartX, int StartY, int EndX, int EndY ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.QueueSegment;
	internal override bool IsWellFormed => Ride >= 0 && IsCell( StartX, StartY ) && IsCell( EndX, EndY );
	internal override void WriteFields( ParkCommandWriter writer )
	{ writer.Int( Ride ); writer.Int( StartX ); writer.Int( StartY ); writer.Int( EndX ); writer.Int( EndY ); }
}

/// <summary>An active tool's popped endpoint and prior corner, not an arbitrary raw-state restore.</summary>
public sealed record UndoQueueSegmentCommand( int Ride, int StartX, int StartY, int EndX, int EndY ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.UndoQueueSegment;
	internal override bool IsWellFormed => Ride >= 0 && IsCell( StartX, StartY ) && IsCell( EndX, EndY );
	internal override void WriteFields( ParkCommandWriter writer )
	{ writer.Int( Ride ); writer.Int( StartX ); writer.Int( StartY ); writer.Int( EndX ); writer.Int( EndY ); }
}

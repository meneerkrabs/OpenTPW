using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>What the tangent routine does to a node's half-turn flag (node flag <c>0x10000000</c>).</summary>
internal enum CoasterTurnVerdict
{
	/// <summary>The branch does not touch the flag: the node keeps the half turn of its last curve pass.</summary>
	Unchanged,
	Set,
	Clear
}

/// <summary>The inputs of the node tangent routine (<c>0x431660</c>): the node and its flag-masked neighbours and the pylon definitions it tests.</summary>
internal readonly record struct CoasterNodeTangentInput(
	SVector3 Position,
	SVector3? Previous,
	SVector3? Next,
	/// <summary>The previous node's own previous link (the raw +0x68 link of node +0x68), read by the last node's loop-pylon rule.</summary>
	SVector3? PreviousOfPrevious,
	/// <summary>The node's pylon definition exists (<c>[[node+0x100]+0x18]</c>); false for a pylon-less node.</summary>
	bool HasPylonDefinition,
	/// <summary>Pylon flag 0x4000 of the node's definition.</summary>
	bool LoopPylon,
	/// <summary>Pylon +0xb0: the dot product below which the node's turn is straight.</summary>
	float StraightDot,
	/// <summary>Node flag 0x80.</summary>
	bool Reversed,
	/// <summary>Node flag 0x40.</summary>
	bool LoopNode,
	/// <summary>Pylon flag 0x4000 of the raw previous and next links' definitions.</summary>
	bool PreviousLoopPylon,
	bool NextLoopPylon,
	float StraightDivisor,
	float Curviness,
	/// <summary>Definition flag 8: the suspended coaster.</summary>
	bool Suspended );

/// <summary>The node's two tangents (node <c>+0x28</c> towards the previous node, <c>+0x34</c> towards the next), its turn (<c>+0x94</c>) and the half-turn verdict.</summary>
internal readonly record struct CoasterNodeTangentResult( SVector3 Back, SVector3 Forward, float Turn, CoasterTurnVerdict TurnedBack );

/// <summary>
/// The node tangent routine <c>0x431660</c> in the order the PC executable evaluates it (COAST-032): every operation is one single-precision operation, the lengths and dot products
/// are the scalar sequences of <see cref="CoasterX87"/>, and the branches are its own. The routine is replayed against the x86 oracle (ORACLE.md X40).
/// </summary>
internal static class CoasterNodeTangent
{
	// [BIN:TPW-EXE:0x00431660 node tangent] the routine's outputs are the back tangent (+0x28), the forward tangent (+0x34), the turn (+0x94) and, in the curve branch of a node without a loop pylon, the half-turn flag; with neither neighbour it writes none of them
	public static CoasterNodeTangentResult? Compute( in CoasterNodeTangentInput i )
	{
		var d = i.StraightDivisor;
		if ( i.Previous is not { } previous && i.Next is not { } )
			return null;
		var position = i.Position;
		if ( i.Next is not { } next )
		{
			// [BIN:TPW-EXE:0x00431753 last node] no next neighbour: with a loop pylon and no node flag 0x80 the dot product (prev − prevprev)·(prev − P), summed in the order y, z, x (0x431776..0x4317b4), below the pylon's threshold gives the forward tangent a/SD (0x4317c1), else the back tangent is a/SD (0x4317e7); both turns are 0 and the loop pylon branches do not zero a height; any other node takes a/SD as its back tangent (0x431838) and zeroes both heights next to a loop pylon (0x43188f..0x4318d0)
			var back = Divide( Sub( i.Previous!.Value, position ), d );
			if ( i.HasPylonDefinition && i.LoopPylon && !i.Reversed )
			{
				if ( i.PreviousOfPrevious is { } twice )
				{
					var p = i.Previous!.Value;
					var dot = (p.Y - twice.Y) * (p.Y - position.Y) + (p.Z - twice.Z) * (p.Z - position.Z) + (p.X - twice.X) * (p.X - position.X);
					if ( dot < i.StraightDot )
						return new CoasterNodeTangentResult( Negate( back ), back, 0f, CoasterTurnVerdict.Unchanged );
				}
				return new CoasterNodeTangentResult( back, Negate( back ), 0f, CoasterTurnVerdict.Unchanged );
			}
			var forward = Negate( back );
			if ( ZeroHeights( i ) )
				return new CoasterNodeTangentResult( back with { Y = 0f }, forward with { Y = 0f }, 0f, CoasterTurnVerdict.Unchanged );
			return new CoasterNodeTangentResult( back, forward, 0f, CoasterTurnVerdict.Unchanged );
		}
		if ( i.Previous is not { } before )
		{
			// [BIN:TPW-EXE:0x004318eb first node] no previous neighbour: the forward tangent is b/SD and the back tangent its negation, the turn 0
			var forward = Divide( Sub( next, position ), d );
			return new CoasterNodeTangentResult( Negate( forward ), forward, 0f, CoasterTurnVerdict.Unchanged );
		}

		// both neighbours (0x431940..0x4320d4)
		var a = Sub( before, position );
		var b = Sub( next, position );
		var la = CoasterX87.Length( a );
		var lb = CoasterX87.Length( b );
		var ua = Divide( a, la );
		var ub = Divide( b, lb );
		var dot3 = ua.X * ub.X + ua.Y * ub.Y + ua.Z * ub.Z;
		// [BIN:TPW-EXE:0x00431a9d turn angle] the two unit vectors are divided by the length of their x/z part (0x4387a0), the turn is acos of the clamped dot product of those, negative when z·x' − x·z' of the pair is below 0 (0x431aca..0x431b54)
		var xa = CoasterX87.NormalizeXz( ua );
		var xb = CoasterX87.NormalizeXz( ub );
		var flat = xa.X * xb.X + xa.Z * xb.Z;
		if ( -1f > flat )
			flat = -1f;
		if ( 1f < flat || float.IsNaN( flat ) )
			flat = 1f;
		var cross = xa.Z * xb.X - xa.X * xb.Z;
		var turn = CoasterX87.Acos( flat );
		if ( !float.IsNaN( cross ) && cross < 0f )
			turn = -turn;

		var threshold = i.HasPylonDefinition ? i.StraightDot : -0.999f;
		// a NaN dot product (a neighbour on the node) takes the straight branch: its compares set the carry flag
		var straight = !(dot3 >= threshold) || dot3 > 0.999f;
		var c = i.Curviness;
		if ( straight )
		{
			if ( i.HasPylonDefinition && i.LoopPylon )
			{
				// [BIN:TPW-EXE:0x00431bac straight loop pylon] the half difference of the unit vectors over SD times the neighbour's length: (ua − ub)/2/SD·lb ahead, (ub − ua)/2/SD·la behind, swapped by node flag 0x80
				var toward = Scale( Divide( Scale( Sub( i.Reversed ? ub : ua, i.Reversed ? ua : ub ), 0.5f ), d ), lb );
				var away = Scale( Divide( Scale( Sub( i.Reversed ? ua : ub, i.Reversed ? ub : ua ), 0.5f ), d ), la );
				return new CoasterNodeTangentResult( away, toward, turn, CoasterTurnVerdict.Unchanged );
			}
			// [BIN:TPW-EXE:0x00431d28 straight] a/SD behind and b/SD ahead, the heights zeroed next to a loop pylon (0x431d73..0x431dd3)
			var back = Divide( a, d );
			var forward = Divide( b, d );
			if ( ZeroHeights( i ) )
				(back, forward) = (back with { Y = 0f }, forward with { Y = 0f });
			return new CoasterNodeTangentResult( back, forward, turn, CoasterTurnVerdict.Unchanged );
		}

		var bisector = new SVector3( ua.X + ub.X, ua.Y + ub.Y, ua.Z + ub.Z );
		if ( i.HasPylonDefinition && i.LoopPylon )
		{
			// [BIN:TPW-EXE:0x00431e19 loop pylon curve] the bisector ua + ub times (length · fCurviness): behind with the previous neighbour's length, negated ahead with the next one's (0x431e4c..0x431ea6)
			var away = Scale( bisector, la * c );
			var toward = Scale( Negate( bisector ), lb * c );
			return new CoasterNodeTangentResult( away, toward, turn, CoasterTurnVerdict.Unchanged );
		}

		// [BIN:TPW-EXE:0x00431eb1 curve] n = a × b, the half turn verdict from n.y (0x431f06..0x431f34), then the cross product of the unit bisector and the unit normal times (length · fCurviness)
		var nx = a.Y * b.Z - a.Z * b.Y;
		var ny = a.Z * b.X - a.X * b.Z;
		var nz = a.X * b.Y - a.Y * b.X;
		var verdict = i.Suspended && !i.LoopNode && ny < 0f ? CoasterTurnVerdict.Set : CoasterTurnVerdict.Clear;
		var bn = Divide( bisector, CoasterX87.Length( bisector ) );
		var nn = Divide( new SVector3( nx, ny, nz ), CoasterX87.Length( nx, ny, nz ) );
		var around = new SVector3( bn.Y * nn.Z - bn.Z * nn.Y, bn.Z * nn.X - bn.X * nn.Z, bn.X * nn.Y - bn.Y * nn.X );
		var curveBack = Scale( around, la * c );
		var curveForward = Scale( Negate( around ), lb * c );
		if ( ZeroHeights( i ) )
			(curveBack, curveForward) = (curveBack with { Y = 0f }, curveForward with { Y = 0f });
		return new CoasterNodeTangentResult( curveBack, curveForward, turn, verdict );
	}

	/// <summary>The straight and curve branches of a node without a loop pylon zero both heights when the raw previous or next link has one (<c>0x431d73..0x431dd3</c>, <c>0x4320d6..0x432113</c>).</summary>
	private static bool ZeroHeights( in CoasterNodeTangentInput i ) => i.HasPylonDefinition && !i.LoopPylon && (i.PreviousLoopPylon || i.NextLoopPylon);

	private static SVector3 Sub( SVector3 p, SVector3 q ) => new( p.X - q.X, p.Y - q.Y, p.Z - q.Z );
	private static SVector3 Negate( SVector3 v ) => new( -v.X, -v.Y, -v.Z );
	private static SVector3 Divide( SVector3 v, float s ) => new( v.X / s, v.Y / s, v.Z / s );
	private static SVector3 Scale( SVector3 v, float s ) => new( v.X * s, v.Y * s, v.Z * s );
}

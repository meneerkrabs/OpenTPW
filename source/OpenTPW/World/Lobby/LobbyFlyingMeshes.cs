using OpenTPW.FrontEnd;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The lobby's flying meshes (the bats over Halloween World, the butterflies over Lost Kingdom), stepped the way tp.exe's
/// flying-mesh system does: one group per <c>FLYINGMESH</c> line spawns its instances over the first frames, and every
/// instance steers toward a random point of the group's box, turning a tenth of the way per step, and picks a new point
/// once it is within √500 units of it. Positions and directions are in MD2 lobby axes (x, y up, z). Every random
/// number comes from the lobby generator (<see cref="LobbyEffects.Draw"/>). GPU-free; <see cref="LobbyScene"/> draws
/// the instances.
/// </summary>
// [BIN:TPW-EXE:0x5da200 flying-mesh system] a lobby subsystem (vtable 0x702b08, created by the lobby init 0x5d5aed) whose update (vf2 0x5da3a0) runs every group's update 0x5da020 in the order the groups were added
public sealed class LobbyFlyingMeshes
{
	/// <summary>Squared distance under which an instance has reached its point (the group's 500.0, pushed by the parser at 0x5e37e7).</summary>
	public const float ArrivalDistanceSquared = 500f;
	/// <summary>Share of the way a direction turns toward its point per step (0.1 × step, 0x5d9b50).</summary>
	public const float TurnPerStep = 0.1f;
	/// <summary>Height of the group's box centre above the island's camera target (0x5e36d8 subtracts −50.0).</summary>
	public const float CentreHeight = 50f;

	/// <summary>One flying instance: where it is, where it flies and the unit direction it faces.</summary>
	public sealed class Instance
	{
		public NVector3 Position;
		public NVector3 Target;
		public NVector3 Direction;

		/// <summary>
		/// The instance's model matrix in MD2 axes (row vectors): x along (d.z, 0, −d.x) normalised, y along d × x
		/// normalised, z along the direction, translated to the position.
		/// </summary>
		// [BIN:TPW-EXE:0x5d81c0 flying-mesh orientation] rows +0x10/+0x20/+0x30 of the instance matrix are (d.z, 0, −d.x)/|…|, the normalised cross product of the direction with it, and the direction; 0x5d8190 writes the position at +0x40
		public NMatrix4x4 Matrix
		{
			get
			{
				var d = Direction;
				var side = new NVector3( d.Z, 0, -d.X );
				side /= side.Length();
				var up = NVector3.Cross( d, side );
				up /= up.Length();
				return new NMatrix4x4(
					side.X, side.Y, side.Z, 0,
					up.X, up.Y, up.Z, 0,
					d.X, d.Y, d.Z, 0,
					Position.X, Position.Y, Position.Z, 1 );
			}
		}
	}

	/// <summary>One <c>FLYINGMESH</c> line placed at an island: its box and the instances spawned so far.</summary>
	public sealed class Group
	{
		internal Group( LobbyFlyingMeshInfo info, NVector3 centre, int count )
		{
			Info = info;
			Centre = centre;
			Count = count;
		}

		public LobbyFlyingMeshInfo Info { get; }
		/// <summary>The box centre in MD2 axes: the island's camera target raised by <see cref="CentreHeight"/>.</summary>
		public NVector3 Centre { get; }
		/// <summary>How many instances the group spawns.</summary>
		public int Count { get; }
		public List<Instance> Instances { get; } = new();
	}

	private readonly Func<uint> draw;
	private readonly List<Group> groups = new();

	/// <param name="draw">The lobby generator (<see cref="LobbyEffects.Draw"/>), which the islands' effects share.</param>
	/// <param name="countPercent">The share of each group's count that is spawned, in percent: the group keeps count x this / 100, at least 1 and at most the count.</param>
	// [BIN:TPW-EXE:0x5d9e22 count share] the share is the global [0x7858dc], the LOBBYOBJECTS setting (entry 29 of the settings table at 0x7442c0, range 0 to 100); the graphics presets give 10, 70 and 100 (COMPATIBILITY.md)
	// [BIN:TPW-EXE:0x5d9db0 flying-mesh group] count = count x [0x7858dc] / 100 with signed integer division, then at least 1 and at most the count
	public LobbyFlyingMeshes( Func<uint> draw, int countPercent = 100 )
	{
		this.draw = draw ?? throw new ArgumentNullException( nameof( draw ) );
		CountPercent = countPercent;
	}

	/// <summary>The share of each group's count that is spawned (LOBBYOBJECTS).</summary>
	public int CountPercent { get; }

	public IReadOnlyList<Group> Groups => groups;

	/// <summary>Adds a group for <paramref name="info"/> at the island whose camera target is <paramref name="islandTarget"/> (MD2 axes).</summary>
	// [BIN:TPW-EXE:0x5d9db0 flying-mesh group] count = count × [0x7858dc] / 100, then at least 1 and at most the count; the centre and extent are copied, the speed and arrival distance stored; 0x5e381d shows the group at once
	public Group Add( LobbyFlyingMeshInfo info, NVector3 islandTarget )
	{
		ArgumentNullException.ThrowIfNull( info );
		var count = info.Count * CountPercent / 100;
		count = count < 1 ? 1 : Math.Min( count, info.Count );
		var group = new Group( info, islandTarget + new NVector3( 0, CentreHeight, 0 ), count );
		groups.Add( group );
		return group;
	}

	/// <summary>A draw as a fraction: (draw &amp; 0x3FFFFFFF) × 2⁻³⁰, in [0, 1).</summary>
	private float Fraction() => (draw() & 0x3FFFFFFF) / 1073741824f;

	/// <summary>A random point of the group's box, one draw per axis in x, y, z order.</summary>
	// [BIN:TPW-EXE:0x5d98b3 flying-mesh point] each coordinate is (draw & 0x3fffffff) × 2^-30 × extent − extent × 0.5 + centre
	private NVector3 RandomPoint( Group group )
	{
		var extent = group.Info.Extent;
		var x = Fraction() * extent.X - extent.X * 0.5f + group.Centre.X;
		var y = Fraction() * extent.Y - extent.Y * 0.5f + group.Centre.Y;
		var z = Fraction() * extent.Z - extent.Z * 0.5f + group.Centre.Z;
		return new NVector3( x, y, z );
	}

	/// <summary>
	/// Advances every group by one lobby step of <paramref name="step"/> (the frame's milliseconds, at most 500, × 0.01):
	/// unspawned instances appear first, then every instance of the group moves.
	/// </summary>
	public void Step( float step )
	{
		foreach ( var group in groups )
		{
			Spawn( group );
			foreach ( var instance in group.Instances )
				Move( group, instance, step );
		}
	}

	/// <summary>The instances still to come appear one after another until all are there or a draw with (draw &amp; 7) == 1 ends the frame's spawning.</summary>
	// [BIN:TPW-EXE:0x5da020 flying-mesh group update] while spawned < count: build an instance (0x5d9830), count it, and stop when all are there or the next draw has (draw & 7) == 1 (0x5da0de); then every instance updates (0x5d9b50)
	private void Spawn( Group group )
	{
		while ( group.Instances.Count < group.Count )
		{
			group.Instances.Add( Create( group ) );
			if ( group.Instances.Count >= group.Count )
				break;
			if ( (draw() & 7) == 1 )
				break;
		}
	}

	// [BIN:TPW-EXE:0x5d9830 flying-mesh instance] the position and the first point are random points of the box (six draws), the direction the normalised difference
	private Instance Create( Group group )
	{
		var position = RandomPoint( group );
		var target = RandomPoint( group );
		var direction = target - position;
		return new Instance { Position = position, Target = target, Direction = direction / direction.Length() };
	}

	// [BIN:TPW-EXE:0x5d9b50 flying-mesh move] wanted = normalised (point − position); direction += (wanted − direction) × 0.1 × step, normalised; position += direction × speed × step; within 500 (squared) of the point three draws pick the next point
	private void Move( Group group, Instance instance, float step )
	{
		var wanted = instance.Target - instance.Position;
		wanted /= wanted.Length();
		var turn = step * TurnPerStep;
		var direction = (wanted - instance.Direction) * turn + instance.Direction;
		instance.Direction = direction / direction.Length();
		instance.Position += instance.Direction * (group.Info.Speed * step);
		if ( NVector3.DistanceSquared( instance.Target, instance.Position ) < ArrivalDistanceSquared )
			instance.Target = RandomPoint( group );
	}
}

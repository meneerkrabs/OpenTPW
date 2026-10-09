using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Plays original MD2 animation clips on an object's node hierarchy in several channels
/// (<see cref="OriginalObjectEffects"/> uses one main channel for TRIGANIM/WAITANIM/LOOPANIM/... and the
/// <c>_CH</c> opcodes' channel numbers; <c>UsageInfo.NumSimultAnims</c> allows up to 4). When several playing clips animate the same node, the
/// most recently started one wins; a finished clip holds its last pose until its channel is replaced or
/// flushed. Channel mixing, holding and the tick rate are OpenTPW choices (docs/OBJECTS.md).
/// </summary>
public sealed class ObjectAnimator
{
	/// <summary>Sandbox choice shared with the Totem prototype; the original tick rate is not verified.</summary>
	public const float TicksPerSecond = 30f;

	private sealed class Channel
	{
		public required ModelAnimationPlayer Player { get; init; }
		public required bool[] Animates { get; init; }
		public required string Clip { get; init; }
		public bool Loop { get; init; }
		public long Serial { get; init; }
		public double Elapsed { get; set; }
		public bool Finished { get; set; }
	}

	private readonly ModelFile model;
	private readonly Dictionary<int, Channel> channels = new();
	private readonly Matrix4x4[] rest;
	private readonly Matrix4x4[] world;
	private readonly int[] order;
	private long serial;

	public ObjectAnimator( ModelFile model )
	{
		ArgumentNullException.ThrowIfNull( model );
		if ( model.Kind != ModelFileKind.Geometry )
			throw new ArgumentException( "Animations play against a geometry model.", nameof( model ) );
		this.model = model;
		rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		world = (Matrix4x4[])rest.Clone();
		var sorted = new List<int>( model.Nodes.Count );
		var pending = new Stack<int>();
		if ( model.RootNodeIndex >= 0 )
			pending.Push( model.RootNodeIndex );
		var children = model.Nodes.ToLookup( node => node.ParentIndex, node => node.Index );
		while ( pending.Count > 0 )
		{
			var node = pending.Pop();
			sorted.Add( node );
			foreach ( var child in children[node] )
				pending.Push( child );
		}
		// Unreachable nodes (none in the corpus) keep their rest matrices.
		order = sorted.ToArray();
	}

	public IReadOnlyList<Matrix4x4> RestTransforms => rest;
	/// <summary>Model-space node matrices of the current pose.</summary>
	public IReadOnlyList<Matrix4x4> NodeTransforms => world;
	public bool IsPlaying => channels.Values.Any( channel => !channel.Finished );
	public int ActiveChannels => channels.Count;

	public bool IsChannelPlaying( int channel ) => channels.TryGetValue( channel, out var state ) && !state.Finished;

	public bool IsChannelLooping( int channel, string clip ) =>
		channels.TryGetValue( channel, out var state ) && state.Loop && string.Equals( state.Clip, clip, StringComparison.OrdinalIgnoreCase );

	/// <summary>Current tick of a channel's clip, or 0.</summary>
	public float GetTick( int channel ) => channels.TryGetValue( channel, out var state ) ? state.Player.Tick : 0;

	public string? GetClip( int channel ) => channels.TryGetValue( channel, out var state ) ? state.Clip : null;

	/// <summary>Starts <paramref name="clip"/> on a channel and returns its length in seconds.</summary>
	public double Play( int channel, ModelAnimation clip, string clipName, bool loop )
	{
		var player = new ModelAnimationPlayer( model, clip, TicksPerSecond ) { Loop = loop };
		var animates = new bool[model.Nodes.Count];
		foreach ( var track in clip.Tracks )
			animates[track.NodeIndex] = true;
		channels[channel] = new Channel { Player = player, Animates = animates, Clip = clipName, Loop = loop, Serial = ++serial };
		Update();
		return clip.Duration / TicksPerSecond;
	}

	public void Stop( int channel )
	{
		if ( channels.Remove( channel ) )
			Update();
	}

	public void StopAll()
	{
		channels.Clear();
		Update();
	}

	public void Advance( double seconds )
	{
		if ( !double.IsFinite( seconds ) || seconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( seconds ) );
		foreach ( var channel in channels.Values )
		{
			if ( channel.Finished )
				continue;
			channel.Elapsed += seconds;
			var tick = (float)(channel.Elapsed * TicksPerSecond);
			channel.Player.SetTick( tick );
			if ( !channel.Loop && tick >= channel.Player.Animation.Duration )
				channel.Finished = true;
		}
		Update();
	}

	private void Update()
	{
		var active = channels.Values.OrderByDescending( channel => channel.Serial ).ToArray();
		foreach ( var node in order )
		{
			var local = model.Nodes[node].Transform;
			foreach ( var channel in active )
			{
				if ( channel.Animates[node] )
				{
					local = channel.Player.LocalTransform( node );
					break;
				}
			}
			var parent = model.Nodes[node].ParentIndex;
			world[node] = parent < 0 ? local : local * world[parent];
		}
	}
}

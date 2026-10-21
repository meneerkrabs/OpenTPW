using System;
using System.Collections.Generic;
using System.Linq;
using OpenTPW.Online.Coop;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-lockstep] An in-memory co-op server for the stream tests, with the real server's guarantees (docs/ONLINE.md, "Shared parks"): one session, the host's frames
/// taken only in order (a frame out of turn is refused with the number expected) and relayed in order to every connected guest, messages relayed at most once, a lost socket losing
/// what was sent to it, a resume that names the last relayed frame. What a member receives waits in its inbox until its game frame delivers it, so a test decides when (and how
/// much) arrives; it can also drop single frames on their way to one guest, as a lost socket would.
/// </summary>
internal sealed class CoopTestNetwork
{
	private readonly List<Member> members = new();
	private uint nextId = 1;

	/// <summary>The last frame the server took from the host.</summary>
	public ulong LastFrame { get; private set; }

	/// <summary>Whether <see cref="LastFrame"/> is exact (false after a restart that lost it: the next frame at any later number is taken).</summary>
	public bool SeqKnown { get; private set; } = true;

	public Member Host => members.First( member => member.IsHost );

	public Member Add( bool host = false )
	{
		var member = new Member( this, nextId++, (byte)members.Count, host );
		member.JoinedAt = LastFrame;
		members.Add( member );
		foreach ( var other in members.Where( other => other != member ) )
			other.Enqueue( () => other.RaiseMemberChanged( new CoopMemberChanged( member.Info, CoopMemberState.Joined ) ) );
		return member;
	}

	private CoopSessionState State( Member you, ulong lastFrame ) => new( "s", "ABCDEF", "Our park", false, 4, you.Id, Host.Id, lastFrame, SeqKnown,
		members.Select( member => member.Info ).ToArray() );

	/// <summary>The server restarted: the members' sockets drop and resume, and its stored frame number is <paramref name="lastFrame"/>, exact or not.</summary>
	public void Restart( ulong lastFrame, bool seqKnown )
	{
		foreach ( var member in members )
			member.Drop();
		LastFrame = lastFrame;
		SeqKnown = seqKnown;
		foreach ( var member in members )
			member.Resume();
	}

	private void Frame( Member host, ulong seq, ReadOnlyMemory<byte> payload )
	{
		if ( SeqKnown ? seq != LastFrame + 1 : seq <= LastFrame )
		{
			var expected = LastFrame + 1;
			host.Enqueue( () => host.RaiseFrameRefused( new CoopFrameRefused( expected ) ) );
			return;
		}
		LastFrame = seq;
		SeqKnown = true;
		var copy = payload.ToArray();
		foreach ( var guest in members.Where( member => !member.IsHost && member.Connected ) )
		{
			guest.Frames++;
			if ( guest.DropFrame?.Invoke( seq ) == true )
				continue;
			guest.Enqueue( () => guest.RaiseFrame( new CoopFrame( seq, copy ) ) );
		}
	}

	private bool Relay( Member from, uint to, ReadOnlyMemory<byte> payload )
	{
		var copy = payload.ToArray();
		var targets = to == CoopProtocol.EveryOtherMember
			? from.IsHost ? members.Where( member => member != from ).ToList() : new List<Member> { Host }
			: members.Where( member => member.Id == to ).ToList();
		if ( targets.Count == 0 || targets.Any( target => !target.Connected ) && to != CoopProtocol.EveryOtherMember )
			return false;
		foreach ( var target in targets.Where( target => target.Connected ) )
		{
			if ( target.DropMessage?.Invoke( from.Id, copy ) == true )
				continue;
			target.Enqueue( () => target.RaiseMessage( new CoopRelayed( from.Id, copy ) ) );
		}
		return true;
	}

	/// <summary>One member's link: the server's side of its socket and the events its game frame raises.</summary>
	internal sealed class Member : ICoopStreamTransport
	{
		private readonly CoopTestNetwork network;
		private readonly Queue<Action> inbox = new();

		public Member( CoopTestNetwork network, uint id, byte slot, bool host )
		{
			this.network = network;
			Id = id;
			Slot = slot;
			IsHost = host;
		}

		public uint Id { get; }
		public byte Slot { get; }
		public bool IsHost { get; }
		public bool Connected { get; private set; } = true;
		public ulong JoinedAt { get; set; }
		/// <summary>The frames the server relayed toward this guest (dropped ones included).</summary>
		public int Frames { get; set; }
		/// <summary>Drops the frame of the given number on its way to this guest (a lost socket's loss, without the socket).</summary>
		public Func<ulong, bool>? DropFrame { get; set; }
		/// <summary>Drops a message from the given member on its way to this one (a message is at most once).</summary>
		public Func<uint, byte[], bool>? DropMessage { get; set; }
		/// <summary>The messages this member sent, decoded (the stream's messages), with their addressee.</summary>
		public List<(uint To, CoopStreamMessage Message)> Sent { get; } = new();
		public string? LeftWith { get; private set; }
		public int Waiting => inbox.Count;

		public CoopMemberInfo Info => new( Id, Slot, IsHost ? "Host" : $"Guest{Id}", Connected );

		public CoopSessionState? Session => network.State( this, IsHost ? network.LastFrame : JoinedAt );
		public bool IsOpen => Connected;

		public event Action<CoopSessionState>? Resumed;
		public event Action<CoopFrameRefused>? FrameRefused;
		public event Action<CoopMemberChanged>? MemberChanged;
		public event Action<CoopFrame>? FrameReceived;
		public event Action<CoopRelayed>? MessageReceived;

		public bool SendFrame( ulong seq, ReadOnlyMemory<byte> payload )
		{
			if ( !Connected || !IsHost )
				return false;
			network.Frame( this, seq, payload );
			return true;
		}

		public bool Send( uint to, ReadOnlyMemory<byte> payload )
		{
			if ( !Connected )
				return false;
			Sent.Add( (to, CoopStreamMessage.Decode( payload.Span )) );
			return network.Relay( this, to, payload );
		}

		public bool Leave( string reason )
		{
			LeftWith = reason;
			return true;
		}

		internal void Enqueue( Action raise ) => inbox.Enqueue( raise );

		/// <summary>Raises what arrived, as the game's pump does before its frame; at most <paramref name="limit"/> items.</summary>
		public void Deliver( int limit = int.MaxValue )
		{
			for ( var count = 0; count < limit && inbox.TryDequeue( out var raise ); count++ )
				raise();
		}

		/// <summary>The socket drops: what was on its way is lost, and the others see it go.</summary>
		public void Drop()
		{
			if ( !Connected )
				return;
			Connected = false;
			inbox.Clear();
			foreach ( var other in network.members.Where( other => other != this && other.Connected ) )
				other.Enqueue( () => other.RaiseMemberChanged( new CoopMemberChanged( Info, CoopMemberState.Disconnected ) ) );
		}

		/// <summary>A new socket resumes the session: the state names the server's last frame.</summary>
		public void Resume()
		{
			if ( Connected )
				return;
			Connected = true;
			var state = network.State( this, network.LastFrame );
			Enqueue( () => Resumed?.Invoke( state ) );
			foreach ( var other in network.members.Where( other => other != this && other.Connected ) )
				other.Enqueue( () => other.RaiseMemberChanged( new CoopMemberChanged( Info, CoopMemberState.Connected ) ) );
		}

		internal void RaiseMemberChanged( CoopMemberChanged change ) => MemberChanged?.Invoke( change );
		internal void RaiseFrameRefused( CoopFrameRefused refused ) => FrameRefused?.Invoke( refused );
		internal void RaiseFrame( CoopFrame frame ) => FrameReceived?.Invoke( frame );
		internal void RaiseMessage( CoopRelayed message ) => MessageReceived?.Invoke( message );
	}
}

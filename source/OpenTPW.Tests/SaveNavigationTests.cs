using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The 177-byte guest/staff navigation state (tp.exe reader 0x50D150): the field order on a synthetic block, the length check and the shipped Jungle Easymode save.</summary>
[TestClass]
public class SaveNavigationTests
{
	/// <summary>The state with every byte a distinct small value so a misplaced field shows.</summary>
	private static byte[] Synthetic()
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		void Fixed( double value ) => writer.Write( (int)Math.Round( value * 65536 ) );
		foreach ( var value in new[] { 1.0, 2, 3, 4, 5, 6, 7, 8 } ) // force, formation_pos, local_xaxis, local_yaxis
			Fixed( value );
		Fixed( 1.0 ); // mass
		Fixed( 2.5 ); // max_force
		Fixed( 0.75 ); // max_speed
		writer.Write( 1 ); // mCantReachDest
		writer.Write( 2 ); // nav_mode
		writer.Write( 3 ); // path_buffer_count
		writer.Write( 4 ); // path_count
		writer.Write( (byte)1 ); // path_finished
		Fixed( 0.5 ); // path_last_progress
		Fixed( 0.25 ); // path_stuck_buffer
		Fixed( 9.0 ); // path_subpath_dist
		Fixed( 10.0 ); // path_tail_dist
		Fixed( 11.0 );
		Fixed( 12.0 ); // path_target_pos
		writer.Write( 1234 ); // path_timestamp
		writer.Write( 56 ); // path_total_count
		Fixed( 13.0 ); // path_total_dist
		Fixed( 47.5 );
		Fixed( 17.25 ); // pos
		Fixed( 0.2 ); // radius
		for ( var index = 0; index < 5; index++ )
		{
			Fixed( 100 + index );
			Fixed( 200 + index );
			writer.Write( index == 0 ? 0x00018000 : SaveNavigation.UnusedDistance );
		}
		Fixed( -0.5 );
		Fixed( 0.125 ); // vel
		writer.Flush();
		return stream.ToArray();
	}

	[TestMethod]
	public void ReadsEveryFieldInTheReadersOrder()
	{
		var bytes = Synthetic();
		Assert.AreEqual( SaveNavigation.Bytes, bytes.Length );
		var state = SaveNavigation.Read( bytes );
		Assert.AreEqual( (1.0, 2.0), (state.Force.X.Value, state.Force.Y.Value) );
		Assert.AreEqual( (3.0, 4.0, 5.0, 6.0, 7.0, 8.0), (state.FormationPos.X.Value, state.FormationPos.Y.Value, state.LocalXAxis.X.Value, state.LocalXAxis.Y.Value, state.LocalYAxis.X.Value, state.LocalYAxis.Y.Value) );
		Assert.AreEqual( (1.0, 2.5, 0.75), (state.Mass.Value, state.MaxForce.Value, state.MaxSpeed.Value) );
		Assert.AreEqual( (1, 2, 3, 4, (byte)1), (state.CantReachDest, state.NavMode, state.PathBufferCount, state.PathCount, state.PathFinished) );
		Assert.AreEqual( (0.5, 0.25, 9.0, 10.0), (state.PathLastProgress.Value, state.PathStuckBuffer.Value, state.PathSubpathDist.Value, state.PathTailDist.Value) );
		Assert.AreEqual( (11.0, 12.0), (state.PathTargetPos.X.Value, state.PathTargetPos.Y.Value) );
		Assert.AreEqual( (1234, 56, 13.0), (state.PathTimestamp, state.PathTotalCount, state.PathTotalDist.Value) );
		Assert.AreEqual( (47.5, 17.25), (state.Pos.X.Value, state.Pos.Y.Value) );
		Assert.AreEqual( 0.2, state.Radius.Value, 1.0 / 65536 );
		Assert.AreEqual( 5, state.Subpaths.Count );
		Assert.AreEqual( (102.0, 202.0), (state.Subpaths[2].Buffer.X.Value, state.Subpaths[2].Buffer.Y.Value) );
		Assert.AreEqual( 1.5, state.Subpaths[0].Distance.Value );
		Assert.AreEqual( SaveNavigation.UnusedDistance, (uint)state.Subpaths[4].Distance.Raw );
		Assert.AreEqual( (-0.5, 0.125), (state.Vel.X.Value, state.Vel.Y.Value) );
		CollectionAssert.AreEqual( bytes, state.Raw );
	}

	[TestMethod]
	public void AWrongLengthIsRejected()
	{
		Assert.ThrowsException<InvalidDataException>( () => SaveNavigation.Read( new byte[176] ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveNavigation.Read( new byte[178] ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveNavigation.Read( ReadOnlySpan<byte>.Empty ) );
	}

	/// <summary>The checks every original person must pass; shared with the corpus test.</summary>
	internal static void CheckPerson( SavePerson person, string name )
	{
		var state = person.NavigationState;
		CollectionAssert.AreEqual( person.Navigation, state.Raw, name );
		Assert.AreEqual( 5, state.Subpaths.Count, name );
		// pos is the thing's position in cells; the base thing keeps it in 1/256 cell.
		Assert.AreEqual( person.Base.X / 256.0, state.Pos.X.Value, 1.0 / 256, $"{name}: pos x" );
		Assert.AreEqual( person.Base.Y / 256.0, state.Pos.Y.Value, 1.0 / 256, $"{name}: pos y" );
		Assert.AreEqual( 0.2, state.Radius.Value, 0.001, $"{name}: radius" );
		Assert.AreEqual( 1.0, state.Mass.Value, $"{name}: mass" );
		foreach ( var subpath in state.Subpaths )
			Assert.IsTrue( (uint)subpath.Distance.Raw == SaveNavigation.UnusedDistance || Math.Abs( subpath.Distance.Value ) < 1000, $"{name}: sub-path distance {subpath.Distance}" );
	}

	[TestMethod]
	public void OriginalJungleEasymodePeopleHaveTheirPositionRadiusAndMass()
	{
		var payload = OriginalParkImportTests.ReadEasymodePayload();
		var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ) );
		var park = OriginalParkImport.Import( payload, map );
		Assert.IsTrue( park.Guests.Count + park.Staff.Count > 0 );
		foreach ( var guest in park.Guests )
			CheckPerson( guest.Person, $"guest {guest.Id}" );
		foreach ( var staff in park.Staff )
			CheckPerson( staff.Person, $"staff {staff.Id}" );
	}
}

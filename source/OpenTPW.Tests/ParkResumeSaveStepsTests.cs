using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A world save read over steps (<see cref="ParkResumeSaveFile.DeserializeSteps"/>, a co-op join's build): its parse a slice of the bytes a step
/// (<see cref="SlicedJsonDeserializer{T}"/>, the serializer's own asynchronous reader), then its checks in their steps. It reads what <see cref="ParkResumeSaveFile.Deserialize"/>
/// reads and refuses what it refuses, with the same message, and a parse given up part way leaves nothing behind. Over the miniature park's envelope
/// (<see cref="ParkResumeSaveFileTests.Envelope"/>).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkResumeSaveStepsTests
{
	private static (ParkResumeSnapshot? Snapshot, int Steps) Stepped( byte[] bytes, int slice )
	{
		ParkResumeSnapshot? parsed = null;
		var steps = ParkResumeSaveFile.DeserializeSteps( bytes, value => parsed = value, slice ).Count();
		return (parsed, steps);
	}

	private static string RefusalOf( Action read ) => Assert.ThrowsException<InvalidDataException>( read ).Message;

	[TestMethod]
	public void ASaveReadOverStepsIsTheSaveReadWholeWhateverTheSlice()
	{
		var bytes = ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() );
		var whole = ParkResumeSaveFile.Serialize( ParkResumeSaveFile.Deserialize( bytes ) );
		Assert.IsTrue( bytes.Length > 20_000, $"a save of some size ({bytes.Length} bytes)" );
		foreach ( var slice in new[] { 1, 7, 257, 4096, SlicedJsonDeserializer.SliceBytes, bytes.Length + 1 } )
		{
			var (snapshot, steps) = Stepped( bytes, slice );
			Assert.IsNotNull( snapshot, $"slice {slice}" );
			CollectionAssert.AreEqual( whole, ParkResumeSaveFile.Serialize( snapshot ), $"slice {slice}: the same save, byte for byte" );
			// a step a slice at least, then the version's check and the shape's steps
			Assert.IsTrue( steps >= (bytes.Length + slice - 1) / slice, $"slice {slice}: {steps} steps" );
		}
	}

	[TestMethod]
	public void ASaveReadOverStepsRefusesWhatTheWholeReadRefusesWithItsMessage()
	{
		var bytes = ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() );
		var json = Encoding.UTF8.GetString( bytes );
		var cases = new Dictionary<string, byte[]>
		{
			["cut short"] = bytes[..(bytes.Length / 2)],
			["an unknown member"] = Encoding.UTF8.GetBytes( json.Replace( "\"Format\":", "\"Extra\":1,\"Format\":" ) ),
			["a number for a string"] = Encoding.UTF8.GetBytes( json.Replace( "\"DisplayName\":\"", "\"DisplayName\":1,\"Unused\":\"" ) ),
			["null"] = Encoding.UTF8.GetBytes( "null" ),
			["another format"] = Encoding.UTF8.GetBytes( json.Replace( $"\"Format\":\"{ParkResumeSaveFile.FormatName}\"", "\"Format\":\"something else\"" ) ),
			["an inconsistent shape"] = Encoding.UTF8.GetBytes( json.Replace( "\"DisplayName\":\"", "\"DisplayName\":\"\\u0001" ) ),
			// a stream's reader skips a leading byte order mark that a span's refuses: such bytes are left to the whole parse
			["a byte order mark first"] = [.. "\uFEFF"u8.ToArray(), .. bytes],
			["a value after the value"] = [.. bytes, .. " {}"u8.ToArray()],
			["a comment"] = Encoding.UTF8.GetBytes( json.Replace( "\"Format\":", "/* a note */ \"Format\":" ) ),
			["an invalid token at the end"] = [.. bytes[..^1], .. "x"u8.ToArray()],
		};
		foreach ( var (name, input) in cases )
		{
			var whole = RefusalOf( () => ParkResumeSaveFile.Deserialize( input ) );
			foreach ( var slice in new[] { 1, 13, SlicedJsonDeserializer.SliceBytes } )
				Assert.AreEqual( whole, RefusalOf( () => Stepped( input, slice ) ), $"{name}, slice {slice}" );
		}
	}

	/// <summary>A context that holds whatever is posted to it and never runs it.</summary>
	private sealed class HoldingContext : System.Threading.SynchronizationContext
	{
		public int Held;
		public override void Post( System.Threading.SendOrPostCallback callback, object? state ) => Held++;
		public override void Send( System.Threading.SendOrPostCallback callback, object? state ) => Held++;
	}

	[TestMethod]
	public void TheParseRunsInTheStepsThatFeedItWhateverContextIsCurrent()
	{
		var bytes = ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() );
		var previous = System.Threading.SynchronizationContext.Current;
		var context = new HoldingContext();
		System.Threading.SynchronizationContext.SetSynchronizationContext( context );
		try
		{
			// a read's continuation posted elsewhere would leave the parse neither ended nor waiting, which a step refuses
			var (snapshot, _) = Stepped( bytes, 4096 );
			Assert.IsNotNull( snapshot );
			Assert.AreEqual( 0, context.Held, "no part of the parse waited for another place" );
		}
		finally
		{
			System.Threading.SynchronizationContext.SetSynchronizationContext( previous );
		}
	}

	[TestMethod]
	public void AParseGivenUpPartWayEndsAndLeavesNothing()
	{
		var bytes = ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() );
		ParkResumeSnapshot? parsed = null;
		using ( var steps = ParkResumeSaveFile.DeserializeSteps( bytes, value => parsed = value, 1024 ).GetEnumerator() )
		{
			Assert.IsTrue( steps.MoveNext() );
			Assert.IsTrue( steps.MoveNext() );
		}
		Assert.IsNull( parsed, "nothing handed over" );
		// the parse itself: disposed part way, it has ended
		var parse = ParkResumeJson.SliceDeserialize<ParkResumeSnapshot>( bytes, 1024 );
		Assert.IsFalse( parse.Step() );
		parse.Dispose();
		Assert.IsTrue( parse.Done, "a parse given up has ended" );
		Assert.ThrowsException<OperationCanceledException>( () => parse.Result );
	}
}

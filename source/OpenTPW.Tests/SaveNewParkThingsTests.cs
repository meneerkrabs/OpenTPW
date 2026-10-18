using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The theme-independent things of a new park built from the constructors (<see cref="SaveNewParkThings"/>) equal the bodies the original wrote in its four <c>restart.INTS</c> (one per theme), byte for byte.
/// </summary>
[TestClass]
public class SaveNewParkThingsTests
{
	[TestMethod]
	public void TheBodiesHaveTheSizesTheSerializersGive()
	{
		Assert.AreEqual( 73_536, SaveNewParkThings.ParkAnalyser( 4, 6 ).Length );
		Assert.AreEqual( SaveAdvisorController.Bytes, SaveNewParkThings.AdvisorController( 2, 4 ).Length );
		Assert.AreEqual( 8, SaveNewParkThings.TagSystem( 3, 5 ).Length );
		Assert.AreEqual( 8, SaveNewParkThings.UIMsgReceiver( 8, 10 ).Length );
		Assert.AreEqual( 10, SaveNewParkThings.MechanicHq( 1, 3 ).Length );
		Assert.AreEqual( 95, SaveNewParkThings.StaffHq( 0, 2 ).Length );
	}

	[TestMethod]
	public void TheAnalysersCellRecordsCoverTheGridInTheSetupsOrder()
	{
		var body = SaveNewParkThings.ParkAnalyser( 4, 6 );
		int Word( int offset ) => BitConverter.ToInt32( body, offset );
		Assert.AreEqual( (4, 32, 32, 128, 128), (Word( 8 ), Word( 12 ), Word( 16 ), Word( 20 ), Word( 24 )) );
		// the cell records follow the five grid words (28), 61 bytes each
		(int, int, int, int) Cell( int index ) => (Word( 28 + index * 61 ), Word( 32 + index * 61 ), Word( 36 + index * 61 ), Word( 40 + index * 61 ));
		Assert.AreEqual( (0, 0, 4, 4), Cell( 0 ) );
		Assert.AreEqual( (4, 0, 4, 4), Cell( 1 ) );
		Assert.AreEqual( (0, 4, 4, 4), Cell( 32 ) );
		Assert.AreEqual( (124, 124, 4, 4), Cell( 1023 ) );
	}

	[TestMethod]
	public void TheChainLinksAreTheBasesChildAndParent()
	{
		var tag = SaveNewParkThings.TagSystem( 3, 5 );
		Assert.AreEqual( (128, 128, 3, 5), (BitConverter.ToUInt16( tag, 0 ), BitConverter.ToUInt16( tag, 2 ), BitConverter.ToUInt16( tag, 4 ), BitConverter.ToUInt16( tag, 6 )) );
	}

	[TestMethod]
	public void EveryRestartSavesFixedThingsAreWhatTheConstructorsBuild()
	{
		var files = OriginalSaveWriterCorpusTests.CorpusFiles().Where( file => file.Name.EndsWith( ".INTS", StringComparison.Ordinal ) ).ToList();
		Assert.IsTrue( files.Count >= 4 );
		foreach ( var (name, path) in files )
		{
			using var reader = new SaveReader( path );
			var payload = OriginalSavePayload.Read( reader.ReadFile() );
			var things = payload.World.Things.Where( thing => thing.Model != SaveThingModel.Attraction ).ToDictionary( thing => thing.Model );
			void Check( SaveThingModel model, Func<ushort, ushort, byte[]> build )
			{
				var thing = things[model];
				// the chain of a new park lists the ids downwards; a thing's base holds the id after it and the id before it
				var expected = build( (ushort)thing.Next, (ushort)(thing.Id + 1) );
				CollectionAssert.AreEqual( expected, thing.Body, $"{name}: {model}" );
			}
			Check( SaveThingModel.ParkAnalyser, SaveNewParkThings.ParkAnalyser );
			Check( SaveThingModel.AdvisorController, SaveNewParkThings.AdvisorController );
			Check( SaveThingModel.TagSystem, SaveNewParkThings.TagSystem );
			Check( SaveThingModel.UIMsgReceiver, SaveNewParkThings.UIMsgReceiver );
			Check( SaveThingModel.MechanicHq, SaveNewParkThings.MechanicHq );
			Check( SaveThingModel.StaffHq, SaveNewParkThings.StaffHq );
		}
	}
}

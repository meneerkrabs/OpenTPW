using System;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SandboxSaveTests
{
	private string directory = null!;
	private string path = null!;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine( Path.GetTempPath(), $"OpenTPW-sandbox-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		path = Path.Combine( directory, "park.sandbox.json" );
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete( directory, true );

	[TestMethod]
	public void RoundTripsAndAtomicallyReplacesOwnSave()
	{
		var empty = SandboxSave.CreateEmpty();
		Assert.AreEqual( new SandboxSave( 1, 0, 0, false, false ), empty );
		SandboxSave.Save( path, empty );
		Assert.AreEqual( empty, SandboxSave.Load( path ) );
		var placed = new SandboxSave( SandboxSave.CurrentVersion, -12, 8, true, true );
		SandboxSave.Save( path, placed );
		Assert.AreEqual( placed, SandboxSave.Load( path ) );
		Assert.AreEqual( 1, Directory.GetFiles( directory ).Length );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 2 )]
	public void RejectsUnsupportedVersion( int version )
	{
		File.WriteAllText( path, JsonSerializer.Serialize( SandboxSave.CreateEmpty() with { Version = version } ) );
		Assert.ThrowsException<InvalidDataException>( () => SandboxSave.Load( path ) );
	}

	[DataTestMethod]
	[DataRow( -33.01f )]
	[DataRow( 31.01f )]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	public void InvalidCoordinatesNeverReplacePreviousSave( float coordinate )
	{
		var previous = SandboxSave.CreateEmpty();
		SandboxSave.Save( path, previous );
		var contents = File.ReadAllBytes( path );
		Assert.ThrowsException<InvalidDataException>( () => SandboxSave.Save( path, previous with { RideX = coordinate } ) );
		Assert.ThrowsException<InvalidDataException>( () => SandboxSave.Save( path, previous with { RideY = coordinate } ) );
		CollectionAssert.AreEqual( contents, File.ReadAllBytes( path ) );
		Assert.AreEqual( 0, Directory.GetFiles( directory, "*.tmp" ).Length );
	}

	[DataTestMethod]
	[DataRow( "{\"Version\":1,\"RideX\":-34,\"RideY\":0,\"HasRide\":true,\"IsRunning\":false}" )]
	[DataRow( "{\"Version\":1,\"RideX\":0,\"RideY\":32,\"HasRide\":false,\"IsRunning\":false}" )]
	[DataRow( "{\"Version\":1,\"RideX\":1e100,\"RideY\":0,\"HasRide\":true,\"IsRunning\":false}" )]
	[DataRow( "{\"Version\":1,\"RideX\":0,\"RideY\":-1e100,\"HasRide\":true,\"IsRunning\":false}" )]
	[DataRow( "{\"Version\":1,\"RideX\":0,\"RideY\":0,\"HasRide\":false,\"IsRunning\":true}" )]
	[DataRow( "{\"Version\":1,\"RideX\":0,\"RideY\":0,\"HasRide\":1,\"IsRunning\":false}" )]
	[DataRow( "{\"Version\":1,\"RideX\":0,\"RideY\":0,\"HasRide\":true,\"IsRunning\":\"false\"}" )]
	[DataRow( "{\"Version\":1,\"RideX\":\"NaN\",\"RideY\":0,\"HasRide\":true,\"IsRunning\":false}" )]
	[DataRow( "{\"Version\":1,\"RideX\":0,\"RideY\":0,\"HasRide\":null,\"IsRunning\":false}" )]
	[DataRow( "{\"Version\":1}" )]
	[DataRow( "{\"Version\":1,\"Version\":1,\"RideX\":0,\"RideY\":0,\"HasRide\":false,\"IsRunning\":false}" )]
	[DataRow( "{\"Version\":1,\"RideX\":0,\"RideY\":0,\"HasRide\":false,\"IsRunning\":false,\"Extra\":0}" )]
	[DataRow( "null" )]
	public void RejectsInvalidSchemaAndState( string json )
	{
		File.WriteAllText( path, json );
		Assert.ThrowsException<InvalidDataException>( () => SandboxSave.Load( path ) );
	}

	[DataTestMethod]
	[DataRow( "" )]
	[DataRow( "{\"Version\":1," )]
	[DataRow( "{\"Version\":1,\"RideX\":NaN}" )]
	[DataRow( "{\"Version\":1,\"RideX\":Infinity}" )]
	public void RejectsTruncatedOrMalformedJson( string json )
	{
		File.WriteAllText( path, json );
		Assert.ThrowsException<InvalidDataException>( () => SandboxSave.Load( path ) );
	}

	[TestMethod]
	public void RejectsOversizedFiles()
	{
		File.WriteAllText( path, new string( (char)32, 4097 ) );
		Assert.ThrowsException<InvalidDataException>( () => SandboxSave.Load( path ) );
	}

	[TestMethod]
	public void AcceptsParkBoundaries()
	{
		var state = new SandboxSave( 1, ParkPlacement.MinimumCoordinate, ParkPlacement.MaximumCoordinate, false, false );
		SandboxSave.Save( path, state );
		Assert.AreEqual( state, SandboxSave.Load( path ) );
	}

	[TestMethod]
	public void FailedWritePreservesPreviousFile()
	{
		SandboxSave.Save( path, SandboxSave.CreateEmpty() );
		var previous = File.ReadAllBytes( path );
		using ( var locked = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.None ) )
			Assert.ThrowsException<IOException>( () => SandboxSave.Save( path, new SandboxSave( 1, 2, 4, true, true ) ) );
		CollectionAssert.AreEqual( previous, File.ReadAllBytes( path ) );
		Assert.AreEqual( 0, Directory.GetFiles( directory, "*.tmp" ).Length );
	}

	[TestMethod]
	public void FailedReplacementCleansTemporaryFile()
	{
		Directory.CreateDirectory( path );
		if ( OperatingSystem.IsWindows() )
			Assert.ThrowsException<UnauthorizedAccessException>( () => SandboxSave.Save( path, SandboxSave.CreateEmpty() ) );
		else
			Assert.ThrowsException<IOException>( () => SandboxSave.Save( path, SandboxSave.CreateEmpty() ) );
		Assert.IsTrue( Directory.Exists( path ) );
		Assert.AreEqual( 0, Directory.GetFiles( directory, "*.tmp" ).Length );
	}

	[TestMethod]
	public void UnrelatedStaleTemporaryFilesAreNotImportedOrDeleted()
	{
		var stale = path + ".stale.tmp";
		File.WriteAllText( stale, "incomplete" );
		SandboxSave.Save( path, SandboxSave.CreateEmpty() );
		Assert.AreEqual( SandboxSave.CreateEmpty(), SandboxSave.Load( path ) );
		Assert.AreEqual( "incomplete", File.ReadAllText( stale ) );
		Assert.AreEqual( 1, Directory.GetFiles( directory, "*.tmp" ).Length );
	}

	[TestMethod]
	public void NeverOverwritesOriginalOrUnrecognizedFiles()
	{
		File.WriteAllText( path, "TPWSoriginal data" );
		Assert.ThrowsException<InvalidDataException>( () => SandboxSave.Save( path, SandboxSave.CreateEmpty() ) );
		Assert.AreEqual( "TPWSoriginal data", File.ReadAllText( path ) );
		var originalPath = Path.Combine( directory, "original.TPWS" );
		File.WriteAllText( originalPath, "TPWSoriginal data" );
		Assert.ThrowsException<InvalidDataException>( () => SandboxSave.Save( originalPath, SandboxSave.CreateEmpty() ) );
		Assert.ThrowsException<InvalidDataException>( () => SandboxSave.Load( originalPath ) );
		Assert.AreEqual( "TPWSoriginal data", File.ReadAllText( originalPath ) );
	}
}

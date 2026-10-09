using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class InstallationFinderTests
{
	/// <summary>An in-memory folder tree; paths are compared case-insensitively, as on Windows and most discs.</summary>
	private sealed class FakeFileSystem
	{
		public readonly HashSet<string> Directories = new( StringComparer.OrdinalIgnoreCase );
		public readonly HashSet<string> Files = new( StringComparer.OrdinalIgnoreCase );

		public bool DirectoryExists( string path ) => Directories.Contains( path );

		public bool FileExists( string path ) => Files.Contains( path );

		public IEnumerable<string> ListDirectories( string path ) =>
			Directories.Where( directory => string.Equals( Path.GetDirectoryName( directory ), path, StringComparison.OrdinalIgnoreCase ) ).ToArray();
	}

	private const string ProgramFiles = @"C:\Program Files";
	private const string ProgramFilesX86 = @"C:\Program Files (x86)";

	private static InstallationSearchContext CreateContext(
		InstallationPlatform platform,
		FakeFileSystem fileSystem,
		string home = "/home/player",
		string executableDirectory = "/unused/bin",
		IEnumerable<string>? drives = null,
		IEnumerable<string>? volumes = null,
		IEnumerable<string>? programFiles = null,
		Func<IEnumerable<string>>? registry = null )
	{
		return new InstallationSearchContext(
			platform,
			home,
			executableDirectory,
			(drives ?? Array.Empty<string>()).ToArray(),
			(volumes ?? Array.Empty<string>()).ToArray(),
			(programFiles ?? Array.Empty<string>()).ToArray(),
			registry ?? (() => Array.Empty<string>()),
			fileSystem.DirectoryExists,
			fileSystem.FileExists,
			fileSystem.ListDirectories );
	}

	private static List<string> Candidates( InstallationSearchContext context ) => InstallationFinder.GetCandidates( context ).ToList();

	[TestMethod]
	public void ExecutableDirectoryComesFirstFollowedByItsParent()
	{
		var fileSystem = new FakeFileSystem();
		var parent = "/home/player/Games/Theme Park World";
		var executable = Path.Combine( parent, "OpenTPW" );
		fileSystem.Directories.Add( parent );
		fileSystem.Directories.Add( executable );
		var homeFolder = Path.Combine( "/home/player", "Theme Park World" );
		fileSystem.Directories.Add( homeFolder );

		var candidates = Candidates( CreateContext( InstallationPlatform.Linux, fileSystem, executableDirectory: executable ) );

		CollectionAssert.AreEqual( new[] { executable, parent, homeFolder }, candidates );
	}

	[TestMethod]
	public void WindowsProgramFilesSubfoldersAreIncludedInOrder()
	{
		var fileSystem = new FakeFileSystem();
		var sim = Path.Combine( ProgramFiles, "EA Games", "Sim Theme Park" );
		var bullfrog = Path.Combine( ProgramFiles, "Bullfrog", "Theme Park World" );
		var x86 = Path.Combine( ProgramFilesX86, "Electronic Arts", "Theme Park World" );
		fileSystem.Directories.Add( sim );
		fileSystem.Directories.Add( bullfrog );
		fileSystem.Directories.Add( x86 );

		var candidates = Candidates( CreateContext( InstallationPlatform.Windows, fileSystem, programFiles: new[] { ProgramFiles, ProgramFilesX86 } ) );

		// Program Files is listed before Program Files (x86), and the folders of each root follow the order in GameFolders.
		CollectionAssert.AreEqual( new[] { bullfrog, sim, x86 }, candidates );
	}

	[TestMethod]
	public void WindowsRegistryValuesAreIncludedBeforeProgramFiles()
	{
		var fileSystem = new FakeFileSystem();
		var registryFolder = @"D:\Old Games\Theme Park World";
		var registryExecutable = Path.Combine( @"E:\Installs\TPW", "TP.exe" );
		var programFilesFolder = Path.Combine( ProgramFiles, "Bullfrog", "Theme Park World" );
		fileSystem.Directories.Add( registryFolder );
		fileSystem.Directories.Add( @"E:\Installs\TPW" );
		fileSystem.Directories.Add( programFilesFolder );

		var registry = new[] { registryFolder, registryExecutable, @"C:\Missing\Folder", "1.0" };
		var candidates = Candidates( CreateContext( InstallationPlatform.Windows, fileSystem, programFiles: new[] { ProgramFiles }, registry: () => registry ) );

		// Registry folders come first; a value that names an .exe contributes its folder; missing paths are skipped.
		CollectionAssert.AreEqual( new[] { registryFolder, @"E:\Installs\TPW", programFilesFolder }, candidates );
	}

	[TestMethod]
	public void WindowsDriveRootWithGameMarkerIsIncluded()
	{
		var fileSystem = new FakeFileSystem();
		fileSystem.Directories.Add( "D:\\" );
		fileSystem.Files.Add( Path.Combine( "D:\\", "TP.ICD" ) );
		fileSystem.Directories.Add( "E:\\" );
		fileSystem.Directories.Add( "F:\\" );
		fileSystem.Files.Add( Path.Combine( "F:\\", "TP.exe" ) );

		var candidates = Candidates( CreateContext( InstallationPlatform.Windows, fileSystem, drives: new[] { "D:\\", "E:\\", "F:\\" } ) );

		CollectionAssert.AreEqual( new[] { "D:\\", "F:\\" }, candidates );
	}

	[TestMethod]
	public void LinuxWinePrefixIsIncluded()
	{
		var fileSystem = new FakeFileSystem();
		var wine = Path.Combine( "/home/player", ".wine", "drive_c", "Program Files (x86)", "Bullfrog", "Theme Park World" );
		fileSystem.Directories.Add( wine );

		var candidates = Candidates( CreateContext( InstallationPlatform.Linux, fileSystem ) );

		CollectionAssert.AreEqual( new[] { wine }, candidates );
	}

	[TestMethod]
	public void MacOsCrossOverBottleIsIncluded()
	{
		var fileSystem = new FakeFileSystem();
		var bottles = Path.Combine( "/Users/player", "Library", "Application Support", "CrossOver", "Bottles" );
		var bottle = Path.Combine( bottles, "Theme Park" );
		var folder = Path.Combine( bottle, "drive_c", "Program Files", "EA Games", "Theme Park World" );
		fileSystem.Directories.Add( bottle );
		fileSystem.Directories.Add( folder );

		var candidates = Candidates( CreateContext( InstallationPlatform.MacOS, fileSystem, home: "/Users/player" ) );

		CollectionAssert.AreEqual( new[] { folder }, candidates );
	}

	[TestMethod]
	public void MacOsVolumeWithGameMarkersIsIncludedAndEmptyVolumeIsNot()
	{
		var fileSystem = new FakeFileSystem();
		fileSystem.Directories.Add( "/Volumes/TPW" );
		fileSystem.Files.Add( Path.Combine( "/Volumes/TPW", "TP.ICD" ) );
		fileSystem.Directories.Add( "/Volumes/Backup" );
		fileSystem.Directories.Add( "/Volumes/Backup/Photos" );
		fileSystem.Directories.Add( "/Volumes/Copy" );
		fileSystem.Directories.Add( Path.Combine( "/Volumes/Copy", "Data" ) );

		var candidates = Candidates( CreateContext( InstallationPlatform.MacOS, fileSystem, volumes: new[] { "/Volumes/TPW", "/Volumes/Backup", "/Volumes/Copy" } ) );

		CollectionAssert.AreEqual( new[] { "/Volumes/TPW", "/Volumes/Copy" }, candidates );
	}

	[TestMethod]
	public void DuplicatesAreRemovedOrdinallyOnMacOsAndCaseInsensitivelyOnWindows()
	{
		var linuxFileSystem = new FakeFileSystem();
		var folder = Path.Combine( "/home/player", "Games", "Theme Park World" );
		linuxFileSystem.Directories.Add( folder );

		// The executable folder, its parent and the ~/Games entry all name the same folder.
		var linux = Candidates( CreateContext( InstallationPlatform.Linux, linuxFileSystem, executableDirectory: Path.Combine( folder, "bin" ) ) );
		Assert.AreEqual( 1, linux.Count( path => path == folder ) );

		var windowsFileSystem = new FakeFileSystem();
		var programFiles = Path.Combine( ProgramFiles, "Bullfrog", "Theme Park World" );
		windowsFileSystem.Directories.Add( programFiles );
		var lowerCase = programFiles.ToLowerInvariant();

		var windows = Candidates( CreateContext( InstallationPlatform.Windows, windowsFileSystem, programFiles: new[] { ProgramFiles }, registry: () => new[] { lowerCase } ) );

		CollectionAssert.AreEqual( new[] { lowerCase }, windows );
	}

	[TestMethod]
	public void MissingDirectoriesAreSkipped()
	{
		var fileSystem = new FakeFileSystem();

		var candidates = Candidates( CreateContext( InstallationPlatform.Linux, fileSystem, volumes: new[] { "/mnt/disc" } ) );

		CollectionAssert.AreEqual( Array.Empty<string>(), candidates );
	}

	[TestMethod]
	public void FailingRegistryDoesNotStopOtherCandidates()
	{
		var fileSystem = new FakeFileSystem();
		var folder = Path.Combine( ProgramFiles, "Bullfrog", "Theme Park World" );
		fileSystem.Directories.Add( folder );

		var candidates = Candidates( CreateContext( InstallationPlatform.Windows, fileSystem, programFiles: new[] { ProgramFiles }, registry: () => throw new UnauthorizedAccessException() ) );

		CollectionAssert.AreEqual( new[] { folder }, candidates );
	}

	[TestMethod]
	public void UnreadableVolumeDoesNotStopOtherVolumes()
	{
		var fileSystem = new FakeFileSystem();
		fileSystem.Directories.Add( "/Volumes/TPW" );
		fileSystem.Files.Add( Path.Combine( "/Volumes/TPW", "TP.exe" ) );
		var listing = new Func<string, IEnumerable<string>>( path => path == "/Volumes/Broken" ? throw new IOException() : fileSystem.ListDirectories( path ) );

		var context = new InstallationSearchContext(
			InstallationPlatform.MacOS,
			"/Users/player",
			"/unused/bin",
			Array.Empty<string>(),
			new[] { "/Volumes/Broken", "/Volumes/TPW" },
			Array.Empty<string>(),
			() => Array.Empty<string>(),
			fileSystem.DirectoryExists,
			fileSystem.FileExists,
			listing );
		var candidates = Candidates( context );

		CollectionAssert.AreEqual( new[] { "/Volumes/TPW" }, candidates );
	}

	[TestMethod]
	public void PublicGetCandidatesRunsOnThisHostAndReturnsExistingDirectories()
	{
		var candidates = InstallationFinder.GetCandidates().ToArray();

		Assert.IsNotNull( candidates );
		foreach ( var candidate in candidates )
			Assert.IsTrue( Directory.Exists( candidate ), $"'{candidate}' is not an existing directory." );
	}
}

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StbImageSharp;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class FshPngExportTests
{
	private string root = null!;
	private Logger previousLog = null!;
	[TestInitialize]
	public void Setup() { previousLog = Log; Log = new Logger(); root = Directory.CreateTempSubdirectory("opentpw-fsh-export-").FullName; }
	[TestCleanup]
	public void Cleanup() { Log = previousLog; Directory.Delete(root, true); }
	private string Input(bool compressed = false, byte palette = 0x2A)
	{
		var path = Path.Combine(root, "source.fsh"); File.WriteAllBytes(path, FshFileTests.CreateExportFixture(compressed, palette)); return path;
	}
	[DataTestMethod]
	[DataRow(false, (byte)0x24)][DataRow(true, (byte)0x24)]
	[DataRow(false, (byte)0x2A)][DataRow(true, (byte)0x2A)]
	[DataRow(false, (byte)0x2D)][DataRow(true, (byte)0x2D)]
	public void ExplicitSelectedImageDecodesAndExportsExactPngPixels(bool compressed, byte palette)
	{
		var input = Input(compressed, palette); var original = File.ReadAllBytes(input);
		var expected = new FshFile(new MemoryStream(original)).Images[1]; var output = Path.Combine(root, "nested", "selected.png");
		FshPngExport.Run(input, 1, output);
		var decoded = ImageResult.FromMemory(File.ReadAllBytes(output), ColorComponents.RedGreenBlueAlpha);
		Assert.AreEqual((2, 3), (decoded.Width, decoded.Height)); CollectionAssert.AreEqual(expected.Rgba, decoded.Data);
		CollectionAssert.AreEqual(original, File.ReadAllBytes(input)); Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length);
		if (palette == 0x2A) CollectionAssert.AreEqual(new byte[] { 40, 50, 60, 173, 10, 20, 30, 0 }, decoded.Data.AsSpan(0, 8).ToArray());
	}
	[TestMethod]
	public void ActualCliRunsBeforeWindowGameAssetsOrProfileSetup()
	{
		var input = Input(true); var output = Path.Combine(root, "cli.png");
		Game.Run(new[] { "--export-fsh-png", input, "0", output });
		var decoded = ImageResult.FromMemory(File.ReadAllBytes(output), ColorComponents.RedGreenBlueAlpha);
		Assert.AreEqual((1, 1), (decoded.Width, decoded.Height)); CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 0 }, decoded.Data);
	}
	[DataTestMethod]
	[DataRow(-1)][DataRow(2)][DataRow(int.MaxValue)]
	public void InvalidIndexCreatesNoOutputOrParentDirectory(int index)
	{
		var input = Input(); var original = File.ReadAllBytes(input); var parent = Path.Combine(root, "absent");
		Assert.ThrowsException<ArgumentOutOfRangeException>(() => FshPngExport.Run(input, index, Path.Combine(parent, "bad.png")));
		Assert.IsFalse(Directory.Exists(parent)); CollectionAssert.AreEqual(original, File.ReadAllBytes(input));
	}
	[TestMethod]
	public void TruncatedInputAndExistingOutputCannotChangeFiles()
	{
		var input = Input(); var original = File.ReadAllBytes(input); var output = Path.Combine(root, "old.png"); File.WriteAllText(output, "previous");
		Assert.ThrowsException<IOException>(() => FshPngExport.Run(input, 1, output)); Assert.AreEqual("previous", File.ReadAllText(output));
		File.WriteAllBytes(input, original[..^1]); var truncated = File.ReadAllBytes(input); var parent = Path.Combine(root, "absent");
		Assert.ThrowsException<InvalidDataException>(() => FshPngExport.Run(input, 1, Path.Combine(parent, "bad.png")));
		Assert.IsFalse(Directory.Exists(parent)); Assert.AreEqual("previous", File.ReadAllText(output)); CollectionAssert.AreEqual(truncated, File.ReadAllBytes(input));
		Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
	}
	[TestMethod]
	public void LateMoveRefusalCleansOnlyOwnedTemporaryFile()
	{
		var input = Input(); var original = File.ReadAllBytes(input); var output = Path.Combine(root, "directory.png"); Directory.CreateDirectory(output);
		var foreign = output + ".foreign.tmp"; File.WriteAllText(foreign, "foreign"); var refused = false;
		try { FshPngExport.Run(input, 1, output); }
		catch (IOException) { refused = true; }
		catch (UnauthorizedAccessException) { refused = true; }
		Assert.IsTrue(refused); Assert.IsTrue(Directory.Exists(output)); Assert.AreEqual("foreign", File.ReadAllText(foreign));
		CollectionAssert.AreEqual(original, File.ReadAllBytes(input));
		CollectionAssert.AreEqual(new[] { foreign }, Directory.GetFiles(root, "*.tmp"));
	}
	[TestMethod]
	public void BadCliArgumentsAndOutputSuffixFailWithoutWrites()
	{
		var input = Input();
		foreach (var args in new[] { new[] { "--export-fsh-png" }, new[] { "--export-fsh-png", input, "-1", "out.png" }, new[] { "--export-fsh-png", input, "one", "out.png" } })
			Assert.ThrowsException<ArgumentException>(() => Game.Run(args));
		Assert.ThrowsException<ArgumentException>(() => FshPngExport.Run(input, 0, Path.Combine(root, "native.fsh")));
		Assert.AreEqual(1, Directory.GetFiles(root).Length);
	}
	[TestMethod]
	public void ExistingDecoderInputBoundRejectsOversizedFileBeforeExport()
	{
		var input = Input(); using (var stream = File.OpenWrite(input)) stream.SetLength(FshFile.MaximumFileBytes + 1L);
		var output = Path.Combine(root, "large.png"); Assert.ThrowsException<InvalidDataException>(() => FshPngExport.Run(input, 0, output));
		Assert.IsFalse(File.Exists(output)); Assert.AreEqual(FshFile.MaximumFileBytes + 1L, new FileInfo(input).Length);
	}
}

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The wait for a chosen language's files (browser, docs/WEB.md), with fake <c>LanguageDataReady</c> / <c>LanguageDataFailed</c>
/// answers: text and speech change together, a park waits for the choice, a failed copy leaves the current language whole.
/// </summary>
[TestClass]
public class PendingLanguageTests
{
	[TestMethod]
	public void NoChoiceIsAlwaysSettled()
	{
		string? pending = null;
		Assert.IsTrue( PendingLanguage.Settle( ref pending, "English", _ => false, _ => true, _ => Assert.Fail() ) );
		Assert.IsNull( pending );
	}

	[TestMethod]
	public void TheDesktopHasNothingToWaitFor()
	{
		string? pending = "Dutch";
		Assert.IsTrue( PendingLanguage.Settle( ref pending, "English", null, null, _ => Assert.Fail() ) );
		Assert.AreEqual( "Dutch", pending, "settled and kept: it is applied now" );
	}

	[TestMethod]
	public void TheChoiceWaitsUntilItsFilesAreIn()
	{
		var ready = false;
		string? pending = "Dutch";
		var warnings = new List<string>();
		Assert.IsFalse( PendingLanguage.Settle( ref pending, "English", name => name == "Dutch" && ready, _ => false, warnings.Add ), "files still coming: a park waits too" );
		Assert.AreEqual( "Dutch", pending );
		ready = true;
		Assert.IsTrue( PendingLanguage.Settle( ref pending, "English", name => name == "Dutch" && ready, _ => false, warnings.Add ) );
		Assert.AreEqual( "Dutch", pending, "applied by the caller" );
		Assert.AreEqual( 0, warnings.Count );
	}

	[TestMethod]
	public void AFailedCopyDropsTheChoiceWithAWarningAndKeepsTheCurrentLanguage()
	{
		var failed = true;
		string? pending = "Dutch";
		var warnings = new List<string>();
		Assert.IsTrue( PendingLanguage.Settle( ref pending, "English", _ => false, _ => failed, warnings.Add ), "the park may start: nothing is left to wait for" );
		Assert.IsNull( pending, "the switch is not applied: text and speech stay English" );
		Assert.AreEqual( 1, warnings.Count );
		StringAssert.Contains( warnings[0], "Dutch" );
		StringAssert.Contains( warnings[0], "keeping English" );

		// Choosing it again (the failure forgotten, files on their way) waits again.
		failed = false;
		pending = "Dutch";
		Assert.IsFalse( PendingLanguage.Settle( ref pending, "English", _ => false, _ => failed, warnings.Add ) );
		Assert.AreEqual( "Dutch", pending );
	}
}

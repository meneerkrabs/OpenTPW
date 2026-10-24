## Every test fails when it leaves a global file system changed (October 24, 2026)

PRs #818 and #819 fixed 32 test classes that left their own game-data `FileSystem` behind as the global one, so later tests passed only in the full suite. An uncommitted probe found them, and nothing stopped a new class from doing the same. Now every test runs inside a guard that fails it when it ends with another global file system than it started with.

- **How the guard reaches every test.**
  - Every test class is in the namespace `OpenTPW.Tests`, and C# finds a type of the enclosing namespace before one that a using directive imports.
  - So `[TestClass]` there now means `OpenTPW.Tests.TestClassAttribute` (`source/OpenTPW.Tests/GlobalStateGuard.cs`), which derives from MSTest's.
  - MSTest 2.2.7 asks the class attribute for the attribute that runs each test method. This one wraps it in `GlobalStateGuard`, whose `Execute` covers the test's `[TestInitialize]`, the method and its `[TestCleanup]`, once per `[DataRow]`.
  - No test file changed.
- **What it checks:** `FileSystem`, `SaveFileSystem` and `CacheFileSystem`, by reference, before and after each run. A new file system at the same folder counts as a leak too, because it carries its own overrides and remembered lookups.
- **What a leak does.**
  - A passing or inconclusive test fails. The message names the global and the folder the test left it at, for example `FileSystem was null and is now the file system at /var/folders/…/opentpw-guard-demo-VS27MJ`, and says how to fix it.
  - A test that already failed keeps its own message and stack trace, and the leak is added to its standard error output.
  - The guard then puts the old value back, so only the leaking test fails and the tests after it start as they would alone.
- **`Log` is not checked.** A `Logger` keeps no state of its own: every instance writes through the same static methods. About 150 test files set one (`Log ??= new()` or `Log = new()`) and never put it back. A check by reference, or of null against set, would fail them, although nothing they leave changes what a later test sees except whether `Log` is null.
- **Why not something less magic.**
  - MSTest 2.2.7 has no per-test hook for a whole assembly.
  - A base class or a renamed attribute would change nearly 900 test files, and every new class would still have to remember it.
  - Shadowing the attribute changes no test file. `GlobalStateGuardTests.EveryTestClassOfThisProjectRunsItsTestsThroughTheGuard` fails when a test class escapes it, for example through another namespace or a fully qualified `[Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]`.
- **The proof** is `GlobalStateGuardTests` (13 tests).
  - It runs stand-in tests through the guard, nested inside the real test. A passing test that leaves any of the three globals at a new folder fails, naming that folder, and the guard puts the old value back.
  - An inconclusive test that leaks fails and keeps its reason. A failing one keeps its own failure and gets the leak in its error output.
  - A new file system at the same folder is a leak. A test that puts the value back keeps its result. A runner that throws still has its leak put back.
  - Its own `[TestInitialize]` and `[TestCleanup]`, a plain test and each `[DataRow]` (with its own arguments) check that MSTest runs them inside the guard.

**Verified:**
- **The with-data suite ran once, inside a slot,** with the game data, Theme Park Inc data and the TPWS saves: 11,014 tests, 10,823 passed, 191 skipped, none failed. No test output carried a guard message, so with #819's fixes no test leaves any of the three file systems changed, and the save and cache checks keep the suite green.
- **A positive control on main** (c85211222, before #819) with only the guard added: the 31 classes #819 fixed, with data. The guard caught all 31: 169 tests failed with its message, for example `FileSystem was null and is now the file system at …/theme park 2/Data`. The other 48 tests of those classes, which leave `FileSystem` alone, passed, and nothing else failed.
- **An uncommitted demo class** ran real leaking tests through MSTest.
  - Each of these failed, naming the folder: a passing test, an async test, an inconclusive test, a `[TestInitialize]` that then stops with `Assert.Inconclusive`, and one of two `[DataRow]`s.
  - A failing test kept its own message and stack trace, with the leak under "Standard Error Messages".
  - The other row, a test with `[ExpectedException]` and a test that put the value back passed. A test after a leak saw null again.
- Without data, the 188 classes that assign one of the three globals: 2,068 tests, 1,528 passed, 540 skipped, none failed.
- The browser determinism pins build (`-p:OpenTpwDeterminismPins=true`) compiles the two new files. It runs its tests without MSTest, so the guard does not apply there.
- Repository checks: `fidelity_register.py --write` and `--check`, `progress.py --check` and the 22 tool contract suites from `build.yml` pass.

Still open:
- The guard covers the three file systems only. Other globals that tests replace, such as `ObjectCatalog.BonusDataRoot` and `Input.TextEntryActive` (#798), are not checked.
- A test with `[Timeout]` that times out is abandoned by MSTest 2.2.7 while it still runs; whatever it changes after that is not seen. No test uses `[Timeout]` today.

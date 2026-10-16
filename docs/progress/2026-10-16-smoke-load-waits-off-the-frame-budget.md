## Smoke load waits leave the frame budget (October 16, 2026)

The native front-end smoke is paced at 60 Hz, so its 3000-frame budget is effectively a wall-clock budget. Under heavy machine load (several full test suites in parallel) the asynchronous loads it waits for, such as the Instant Action park and the texture-pack switches, took long enough to exhaust it ("stuck at 'Instant Action park loads'", once at 'park head pack completes').

- **Load waits** (park load, jungle park construction, park head pack, texture pack on/off and the optional real-pack timing) now go through one `WaitForLoad` step, like the advisor speech wait: their frames are counted separately and no longer count against the step budget.
- **A real hang still fails**: each wait has its own wall-clock allowance of 180 s, and the message names the step and the time and frames spent.
- No functional assertion changed. The sandbox, Theme Park Inc and autorun smokes have no asynchronous waits under a frame budget and are unchanged.

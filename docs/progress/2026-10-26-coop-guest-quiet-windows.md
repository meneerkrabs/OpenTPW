## A guest's windows no longer complain about clicks nobody made (October 26, 2026)

In a shared park, a guest who only looked at a ride, a shop, the research lab or the loans window was told "Only the host can do that for now." when the window closed or opened, although the player had changed nothing ([COOP.md](../COOP.md), "Nothing the player did not ask for"; `[EXT:online-coop-commands]`).

- **The object window** sends a guest's settings only when the player changed one. The original applies the window's values on every close; for a guest that was a request the host's rules refuse.
- **The research window's workload knob** shows the lab's workload on a guest's screen without writing it back.
- **The loans window** sends no offer refresh when a guest opens it or switches its tab. The list shows the host's offers.
- A change the guest did make is still sent, and its refusal is still said.

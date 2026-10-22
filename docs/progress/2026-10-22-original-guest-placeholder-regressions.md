## Original guest placeholder import regressions (October 22, 2026)

Added synthetic, asset-free tests for the existing SAVE-002 unsupported-state rejection
boundary. Seven parameterized cases cover states 11–17 with a retained state-6 guest and
a foreign-model slot control. A mixed-state case checks saved used-list ordering. Each
case checks held slots, the complete free-list against an independently constructed
ascending-ID oracle, pool snapshot restoration, and allocations extending beyond the
reserved IDs. Production behavior is unchanged.

This removes the original-media dependency from this specific importer/allocator
integration coverage; the existing Easymode corpus test remains useful separately.
Source/API and static checks were performed, but compilation and C# test execution are
pending. No original executable or game assets were used.

SAVE-002 and proposal #536 remain open. These tests do not restore queue, rider or guard
links, prove native parity, or replace the missing linked-state fixtures and continuation
contract. They only protect the current loss-containment behavior while that work remains.

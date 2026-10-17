-- Patient guests: an example script mod for OpenTPW (docs/SCRIPT-MODS.md).
-- Both hooks receive the host's own answer as `prev` and return the answer the game should use.
-- The view fields used here (queueTurns, patience, queueLength, happiness) are a sketch until the hooks are wired in.

local extraPatience = 40   -- turns a guest waits beyond the game's own limit
local longQueue = 25       -- queue length the game normally balks at
local extraQueue = 15      -- how many more guests a happy guest will queue behind

-- Called after the game's own leave tests: only leave when the extra patience is also used up.
function leaveQueue(guest, attraction, prev)
  if not prev then
    return false
  end
  return guest.queueTurns > guest.patience + extraPatience
end

-- Called when a guest reaches the back of a queue: happy guests accept a somewhat longer one.
function joinQueue(guest, attraction, prev)
  if prev then
    return true
  end
  return guest.happiness > 50 and attraction.queueLength < longQueue + extraQueue
end

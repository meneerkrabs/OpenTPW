-- Patient guests: an example script mod for OpenTPW (docs/SCRIPT-MODS.md, "Hook API v1").
--
-- Both hooks receive the answer the game (and the mods before this one) would give as `prev`, and return the answer to use.
-- Views are read-only tables; the fields used here are listed in docs/SCRIPT-MODS.md:
--   guest:      happiness (0 to 100), ...
--   attraction: joinDifference, queueCount, queueLimit (joinQueue); waitedTurns (leaveQueue)

local tolerance = 20       -- how much further from its taste than the game allows (45) a happy guest still joins a ride
local mood = 50            -- a guest needs at least this happiness to be that tolerant
local patience = 600       -- park turns a guest waits in a queue before it gives up (the game itself never loses patience)

-- Counts an event in the mod's saved `state`.
function count(name)
  if has(state, name) then
    state[name] = state[name] + 1
  else
    state[name] = 1
  end
end

-- Called when a guest reaches the back of a queue; `prev` is true when the game lets it join.
-- A guest the game turned away only because the ride is a little too exciting or not exciting enough joins anyway,
-- as long as it is in a good mood and the queue has room.
function joinQueue(guest, attraction, prev)
  if prev then
    return true
  end
  if guest.happiness >= mood and attraction.joinDifference < 45 + tolerance and attraction.queueCount < attraction.queueLimit then
    count("tolerated")
    return true
  end
  return false
end

-- Called after the game's own leave tests; `prev` is true when the game lets the guest leave (it never does).
-- Patient guests stay for a long time, but not forever.
function leaveQueue(guest, attraction, prev)
  if prev then
    return true
  end
  if attraction.waitedTurns > patience then
    count("gaveUp")
    return true
  end
  return false
end

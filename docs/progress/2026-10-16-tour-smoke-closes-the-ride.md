## The tour smoke closes the ride before it waits for the release (October 16, 2026)

The jungle sandbox smoke had outgrown its 180 s watchdog. Its tour stage waited until Jurassic Tours had released every rider while the
ride stayed open. A state dump of the controller and the script showed nothing stuck: the single car flew, returned, landed, was unloaded
and left again on a steady cycle of about 75 s (a 39 s flight, then the return, staging, approach and loading). But a guest was already
waiting in `VAR_LETMEON` at every landing, so the car always left with somebody aboard and "everybody released" never held. Five
departures in 400 s matches the runs that looked stuck. The controller follows tp.exe's `TOUR 11` rule (`0x0055DD80`): the script decides
when to leave, and `TourRide.RSE` loads until the car is full or ten seconds pass without a boarding.

The stage now closes the ride once the read-back car is airborne, as a player would. `TOUR 14 0` ends the flight, the car lands, the
script unloads it and boards nobody, and the queue is sent away. It never closes while a car stands docked after `TOUR 11` but before
lift-off, because the original leaves such a car standing with its riders (docs/TOURS.md). The tour stage now ends about 20 s after the
close, and the whole jungle smoke runs in about 85 s. A failure in this stage now reports the full controller and script state.

A failed kart or water track preview now also reports what stopped it. (The kart step had failed on main because its site search
accepted unowned land; #238 fixed that site search in parallel.)

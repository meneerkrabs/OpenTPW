## The entering frame's first draw and park HUD (October 21, 2026)

The browser frame that enters a park fell from 83 to 60 ms (published AOT build, Jungle, medians of five runs). Its first draw fell from 19 to 8 ms, and the park HUD's creation (7 ms) left it.

The harness first had to name the cost:
- the CPU profile of a slow frame is now read over the frame the profile itself saw (its clock was 28-30 ms off the page's);
- frame marks cover the draw's parts, each entity type, the interface textures made on first use and the browser's WebGL calls by kind.

They showed first-time work in the first draw: the level's sky was created on its first draw (4-8 ms), and the HUD's interface images and font atlas were decoded and uploaded (5-6 ms). The rest of the draw and the HUD's first update are the runtime's first-call work.

Two behaviour-preserving changes:
- the park build now loads the sky, creates the park HUD and makes the textures of its first draw in build steps, before the park is entered; the HUD reads the camcorder key and mouse button again on entering;
- the browser spreads scheduled GPU deletions over frames, at most 1 ms per frame.

The front-end smoke checks that the park built over frames is entered with the HUD a build step made, in the state of a HUD made on entering, with the same world hash.

The entering frame is no longer the largest frame of the park start; two build steps are (71-77 ms). Still in the entering frame: the advisor (about 15 ms), the HUD's first update (7 ms) and the first draws of the entities, the advisor model and the HUD (about 6 ms), all first calls, and the live capture (7 ms). Details in docs/BROWSER-PERFORMANCE.md, "The first draw and the park HUD".

## Lobby sky and buy-window previews (October 30, 2026)

- **The sky is no longer cut by the far plane.** Since the lobby takes its span
  selection's far plane (#910), that plane is often nearer than the sky's
  cylinder. On Direct3D and Vulkan the cylinder's front was clipped there, so the
  island screen showed a trapezoid of flat clear colour in the middle of the sky
  (reported in #829). tp.exe never clips the sky by the far plane: its sky draw
  (`0x5863C0`) passes the clipper the mask that leaves the far plane's outcode
  out. The clouds and the cylinder now take the view's projection without its far
  plane; the moons keep the view's own.
- **Buy-window previews keep their shape while their clip plays.** A clip's
  vertex animation moves a mesh's stored positions, which its corners share. The
  preview read them by corner, so meshes with fewer positions than corners (the
  balloon shop, many others) turned into scrambled polygons. Each corner now takes
  the position it was built from, as the park's parts already did (reported in
  #829).

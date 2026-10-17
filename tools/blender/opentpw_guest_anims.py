"""Pose generators for the guests' land animation slots, for the rig of ``opentpw_kid.py``.

Each slot of the original guest sprite sets is a short loop of poses seen from five directions. The motions here were
recreated by looking at the originals' frames (never by copying them): the walk and run cycles, the two gestures, the
cheer, the stumble/sick doubling over and the hands-on-hips idle. A pose is the dict ``opentpw_kid.apply_pose`` takes;
``grounded`` puts the lower foot on the floor so no pose floats or sinks.

Slot tables (docs/CONTENT-KIT.md, "Guest sprite sets"): the numbers are the original ESP slot numbers.
"""
import math

# (slot, name, frames per direction), in the original ESP's order of slot numbers.
TPW_KID_SLOTS = [(0, "stand", 1), (1, "walk", 8), (2, "run", 8), (4, "gesture-a", 4), (6, "gesture-b", 4), (11, "stumble", 4), (12, "cheer", 4), (14, "hands-on-hips", 2)]
TPI_KID_SLOTS = [(0, "stand", 1), (1, "walk", 8), (2, "run", 8), (4, "gesture-a", 4), (6, "gesture-b", 4), (12, "cheer", 4), (15, "idle-gesture", 4)]
TPI_ADULT_SLOTS = [(0, "stand", 1), (1, "walk", 8), (4, "gesture-a", 4), (6, "gesture-b", 4), (12, "cheer", 4), (15, "idle-gesture", 4)]


def grounded(kid, pose, lift=0.0):
	"""Root height at which the lower foot of the pose touches the floor (flat-foot approximation), plus ``lift``."""
	lengths = []
	for s in (-1, 1):
		a = math.radians(pose.get("leg", {}).get(s, (0, 0))[0])
		k = math.radians(pose.get("knee", {}).get(s, 0))
		lengths.append(kid.leg_t * math.cos(a) + kid.leg_s * math.cos(a - k))
	return -kid.hip_z + max(lengths) + 0.14 + lift


def make(kid, stoop=0.0, yaw=0.0, roll=0.0, head_yaw=0.0, head_pitch=0.0, arms=((0, 14, 6), (0, 14, 6)), legs=((0, 5, 0), (0, 5, 0)), lift=0.0, feet_flat=True):
	"""Builds a pose from per-side tuples: arms (swing, abduction, elbow), legs (swing, abduction, knee); index 0 is side -1."""
	pose = {"stoop": stoop + kid.stoop, "roll": roll, "head_yaw": head_yaw, "head_pitch": head_pitch, "arm": {}, "elbow": {}, "leg": {}, "knee": {}, "ankle": {}}
	for i, s in enumerate((-1, 1)):
		pose["arm"][s] = (arms[i][0], arms[i][1])
		pose["elbow"][s] = arms[i][2]
		pose["leg"][s] = (legs[i][0], legs[i][1])
		pose["knee"][s] = legs[i][2]
		pose["ankle"][s] = max(-50.0, min(50.0, (legs[i][2] - legs[i][0]) * 0.85)) if feet_flat else 0.0
	pose["root"] = (0, 0, grounded(kid, pose, lift))
	pose["yaw"] = yaw
	return pose


def stand(kid, f=0, n=1):
	return make(kid, arms=((0, 14, 6), (0, 14, 6)))


def _gait(kid, f, n, hip, knee0, knee1, arm, elbow0, elbow1, stoop, hop, bob):
	tau = 2 * math.pi
	ph = f / n
	arms, legs = [], []
	for s in (-1, 1):
		th = tau * (ph + (0.0 if s < 0 else 0.5))
		a = hip * math.sin(th)
		k = knee0 + knee1 * max(0.0, math.cos(th)) ** 1.4
		legs.append((a, 4, k))
		arms.append((-arm * math.sin(th), 10, elbow0 + elbow1 * max(0.0, -math.sin(th))))
	lift = hop * (0.5 - 0.5 * math.cos(tau * 2 * ph)) + bob * math.sin(tau * 2 * ph + 0.6)
	return make(kid, stoop=stoop, roll=5 * math.sin(tau * ph), head_pitch=-stoop * 0.6, arms=tuple(arms), legs=tuple(legs), lift=lift)


def walk(kid, f, n=8):
	return _gait(kid, f, n, hip=44, knee0=8, knee1=52, arm=40, elbow0=12, elbow1=26, stoop=5, hop=0.0, bob=0.04)


def run(kid, f, n=8):
	return _gait(kid, f, n, hip=66, knee0=26, knee1=88, arm=66, elbow0=82, elbow1=12, stoop=16, hop=0.24, bob=0.0)


def gesture_a(kid, f, n=4):
	"""Turn and point out, spread both arms wide, then a forward bow."""
	if f == 0:
		return make(kid)
	if f == 1:
		return make(kid, yaw=16, head_yaw=-14, arms=((10, 16, 8), (62, 32, 38)))
	if f == 2:
		return make(kid, stoop=-6, head_pitch=8, arms=((8, 84, 10), (8, 84, 10)))
	return make(kid, stoop=32, head_pitch=-14, arms=((36, 18, 34), (36, 18, 34)), legs=((0, 6, 8), (0, 6, 8)))


def gesture_b(kid, f, n=4):
	"""A hand to the chin (thinking), the head tilting, then a deep nod."""
	if f == 0:
		return make(kid)
	if f == 1:
		return make(kid, head_yaw=-8, arms=((0, 14, 6), (108, 8, 100)))
	if f == 2:
		return make(kid, yaw=-6, roll=4, head_yaw=-14, head_pitch=-6, arms=((0, 14, 6), (116, 6, 112)))
	return make(kid, stoop=40, head_pitch=-20, arms=((42, 14, 30), (42, 14, 30)), legs=((0, 6, 10), (0, 6, 10)))


def stumble(kid, f, n=4):
	"""Hunching over, a hand on the stomach (the sick and stumble frames)."""
	if f == 0:
		return make(kid)
	if f == 1:
		return make(kid, stoop=16, head_pitch=-8, arms=((22, 14, 20), (22, 14, 20)), legs=((0, 5, 8), (0, 5, 8)))
	if f == 2:
		return make(kid, stoop=36, head_pitch=-18, arms=((74, 6, 84), (74, 6, 84)), legs=((20, 8, 46), (20, 8, 46)))
	return make(kid, stoop=30, head_pitch=-24, roll=3, arms=((66, 6, 78), (30, 14, 40)), legs=((14, 8, 34), (14, 8, 34)))


def cheer(kid, f, n=4):
	"""Arms up with fists, a hop, and down again."""
	if f == 0:
		return make(kid)
	if f == 1:
		return make(kid, head_pitch=14, arms=((124, 52, 56), (124, 52, 56)), legs=((0, 6, 14), (0, 6, 14)))
	if f == 2:
		return make(kid, head_pitch=22, arms=((160, 56, 12), (160, 56, 12)), legs=((0, 4, 0), (0, 4, 0)), lift=0.32, feet_flat=False)
	return make(kid, head_pitch=20, arms=((142, 58, 18), (142, 58, 18)), legs=((0, 6, 20), (0, 6, 20)), lift=0.08)


def hands_on_hips(kid, f, n=2):
	"""Waiting with the hands at the hips, shifting a little."""
	sway = (-1, 1)[f % 2]
	return make(kid, roll=2 * sway, head_yaw=6 * sway, arms=((-14, 46, 70), (-14, 46, 70)), legs=((0, 5, 0), (0, 8 if f else 5, 0)))


def idle_gesture(kid, f, n=4):
	"""The TPI idle: one arm points out and sweeps, with a small turn."""
	if f == 0:
		return make(kid)
	if f == 1:
		return make(kid, yaw=10, head_yaw=-10, arms=((0, 14, 6), (78, 24, 12)))
	if f == 2:
		return make(kid, yaw=16, head_yaw=-18, arms=((0, 14, 6), (86, 52, 8)))
	return make(kid, yaw=6, head_yaw=-6, arms=((0, 14, 6), (44, 20, 20)))


SLOT_POSES = {"stand": stand, "walk": walk, "run": run, "gesture-a": gesture_a, "gesture-b": gesture_b, "stumble": stumble,
	"cheer": cheer, "hands-on-hips": hands_on_hips, "idle-gesture": idle_gesture}

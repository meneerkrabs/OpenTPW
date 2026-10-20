"""Build the original pool bridge, independently of concept scenery and guests.

Run from the repository root:
    blender -b --factory-startup --python tools/blender/opentpw_pool_bridge.py -- --out content/pools/bridge
Add --glb /tmp/bridge.glb for the conventional content-kit GLB export, or
--blend /tmp/bridge.blend to inspect the authored scene. No original game art is used.

Coordinates are Blender Z-up, in model units (10 per cell): X 0..70,
Y 0..10. The three-cell clear span is X 20..50, with two-cell land
approaches on each end. Deck top is Z 10; every bearing is wholly on land.
The JSON is a baked, indexed triangle mesh per textured material. Each vertex
is [x,y,z,nx,ny,nz,u,v], with counter-clockwise winding and UV seams preserved.
Runtime consumers scale positions by cellSize / unitsPerCell and transform
positions AND normals into the bridge's orientation; they must not mirror
axes without reversing winding. PNG rows use the usual image-file order.
"""

import argparse
import json
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_changing_room as cabin
import opentpw_conventions as conventions
import opentpw_export
import opentpw_pool_extras as extras


def box(name, location, dimensions, material, bevel=0.04):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    if bevel:
        modifier = obj.modifiers.new("Authored edge bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
    return obj


def bar(name, start, end, width, material):
    direction = Vector(end) - Vector(start)
    obj = box(name, (Vector(start) + Vector(end)) / 2,
              (width, width, direction.length), material)
    obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
    return obj


def build():
    """Create only the bridge's render geometry and its procedural textures."""
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    images = extras.make_textures("jungle", ("deck", "frame"))
    images["accent"] = cabin._plain("bridge_accent", (0.075, 0.36, 0.36), 97)
    materials = {name: cabin.make_material(name, image) for name, image in images.items()}
    wood, frame, accent = (materials[name] for name in ("deck", "frame", "accent"))
    for i in range(30):
        box("Deck plank", (20.5 + i, 5, 9.6), (0.94, 10, 0.8), wood)
    for y in (0.5, 9.5):
        box("Clear span beam", (35, y, 9.05), (30, 0.65, 0.3), frame)
        for x in (25, 35, 45):
            box("Span rail post", (x, y, 12.7), (0.6, 0.6, 5.4), frame)
        bar("Span handrail", (20, y, 15.4), (50, y, 15.4), 0.6, frame)
        bar("Span middle rail", (20, y, 12.5), (50, y, 12.5), 0.36, frame)
        for x in (20, 50):
            box("Teal cap", (x, y, 15.65), (0.9, 0.9, 0.35), accent, 0.06)
    for side in (-1, 1):
        # A one-unit riser per two-unit tread follows the approved concept.
        for i in range(10):
            x = 35 + side * (16 + 2 * i)
            box("Approach step", (x, 5, 9.5 - i), (2, 10, 1), wood)
        for y in (0.5, 9.5):
            inner, outer = 35 + side * 15, 35 + side * 34.65
            bar("Approach stringer", (35 + side * 15.35, y, 8.5), (outer, y, 0.35), 0.65, frame)
            for distance, height in ((15, 10), (25, 5), (34.65, 0)):
                box("Approach rail post", (35 + side * distance, y, height + 2.75),
                    (0.65, 0.65, 5.5), frame)
            bar("Approach handrail", (inner, y, 15.4), (outer, y, 5.4), 0.6, frame)
            bar("Approach middle rail", (inner, y, 12.5), (outer, y, 2.5), 0.36, frame)
            # Bearing bounds stop 0.15 model units short of the water-cell edge.
            box("Land-only bank bearing", (35 + side * 15.5, y, 4.5),
                (0.7, 0.7, 9), frame)
    return images


def rounded(values):
    return [0.0 if abs(value) < 0.000005 else round(float(value), 5) for value in values]


def export_mesh(out, images):
    """Bake Blender modifiers and split corner normals into a compact runtime mesh."""
    objects = sorted((obj for obj in bpy.context.scene.objects if obj.type == "MESH"), key=lambda obj: obj.name)
    problems = conventions.validate_scene(objects)
    if problems:
        raise ValueError("; ".join(problems))
    parts = {name: {"name": name, "texture": name + ".png", "vertices": [], "indices": []}
             for name in images}
    indices = {name: {} for name in images}
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        normal_matrix = evaluated.matrix_world.to_3x3().inverted().transposed()
        uv = mesh.uv_layers.active.data
        for triangle in mesh.loop_triangles:
            material = mesh.materials[triangle.material_index].name
            part, lookup = parts[material], indices[material]
            for loop_index in triangle.loops:
                corner = mesh.loops[loop_index]
                position = evaluated.matrix_world @ mesh.vertices[corner.vertex_index].co
                normal = (normal_matrix @ mesh.corner_normals[loop_index].vector).normalized()
                vertex = tuple(rounded((*position, *normal, *uv[loop_index].uv)))
                if vertex not in lookup:
                    lookup[vertex] = len(part["vertices"])
                    part["vertices"].append(vertex)
                part["indices"].append(lookup[vertex])
        evaluated.to_mesh_clear()
    # Keep placement, navigation clearance and render data in agreement. A low
    # triangle may touch a bank boundary, but must never cross into the span.
    for part in parts.values():
        vertices = part["vertices"]
        for vertex in vertices:
            x, y, z, nx, ny, nz, _, _ = vertex
            if not (0 <= x <= 70 and 0 <= y <= 10 and z >= 0):
                raise ValueError("Bridge geometry exceeds its seven-by-one-cell footprint")
            if abs(nx * nx + ny * ny + nz * nz - 1) > 0.0001:
                raise ValueError("Bridge vertex normal is not unit length")
        for index in range(0, len(part["indices"]), 3):
            triangle = [vertices[i] for i in part["indices"][index:index + 3]]
            # Clip to the open water interval before testing clearance: sloped
            # approach rails cross the bank only at their high end.
            clipped = [Vector(v[:3]) for v in triangle]
            for edge, sign in ((20.00001, 1), (49.99999, -1)):
                result = []
                for a, b in zip(clipped, clipped[1:] + clipped[:1]):
                    a_inside, b_inside = sign * (a.x - edge) >= 0, sign * (b.x - edge) >= 0
                    if a_inside:
                        result.append(a)
                    if a_inside != b_inside:
                        result.append(a.lerp(b, (edge - a.x) / (b.x - a.x)))
                clipped = result
            if clipped and min(v.z for v in clipped) < 8.89:
                raise ValueError("A low support intrudes into the swimming span")
            a, b, c = [Vector(v[:3]) for v in triangle]
            if (b - a).cross(c - a).dot(Vector(triangle[0][3:6])) <= 0:
                raise ValueError("Degenerate or inward-facing bridge triangle")
    document = {"version": 1, "unitsPerCell": 10, "spanStart": 20, "spanEnd": 50,
                "deckHeight": 10, "parts": list(parts.values())}
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "bridge.json"), "w", encoding="utf-8") as stream:
        json.dump(document, stream, separators=(",", ":"))
        stream.write("\n")
    for name, image in images.items():
        image.filepath_raw = os.path.abspath(os.path.join(out, name + ".png"))
        image.file_format = "PNG"
        image.save()
    print("Bridge exported: " + ", ".join(f"{p['name']}: {len(p['indices']) // 3} triangles" for p in parts.values()))


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True)
    parser.add_argument("--glb")
    parser.add_argument("--blend")
    options = parser.parse_args(args)
    images = build()
    export_mesh(options.out, images)
    if options.glb and not opentpw_export.export(options.glb):
        raise RuntimeError("GLB export failed validation")
    if options.blend:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(options.blend))


if __name__ == "__main__":
    main()

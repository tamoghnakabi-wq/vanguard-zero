"""
VANGUARD ZERO — Shared Blender 5.2 AAA Hard-Surface Modeling, 2K PBR Texture
Synthesis + Cycles Metal GPU Baking, Unity FBX Export, and Studio Render Library.

Coordinate Convention:
- Author in Blender: +X = Right, +Y = Forward (muzzle / character facing), +Z = Up.
- Units: Meters (1.0 Blender unit = 1.0 Unity meter).
- Export FBX: axis_forward='Z', axis_up='Y', bake_space_transform=True (rigid/hierarchical)
  => In Unity (left-handed): +X = Right, +Y = Up, +Z = Forward, scale = (1,1,1), rot = (0,0,0).
"""

import os
import json
import math
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix, Euler

ROOT_DIR = "/Users/tamoghnakabi/.gemini/antigravity/scratch/vanguard-zero/blender-assets"
SCENES_DIR = os.path.join(ROOT_DIR, "scenes")
EXPORTS_DIR = os.path.join(ROOT_DIR, "exports")
TEXTURES_DIR = os.path.join(ROOT_DIR, "textures")
RENDERS_DIR = os.path.join(ROOT_DIR, "renders")
MANIFEST_PATH = os.path.join(ROOT_DIR, "manifest.json")


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    setup_cycles_metal(scene)
    return scene


def setup_cycles_metal(scene, samples=64):
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'GPU'
    prefs = bpy.context.preferences.addons.get('cycles')
    if prefs:
        cprefs = prefs.preferences
        cprefs.compute_device_type = 'METAL'
        cprefs.get_devices()
        for d in cprefs.devices:
            d.use = (d.type == 'METAL')
    scene.cycles.samples = samples
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = 0.02
    scene.cycles.use_denoising = True
    try:
        scene.cycles.denoiser = 'OPENIMAGEDENOISE'
    except Exception:
        pass
    scene.cycles.max_bounces = 8
    scene.cycles.diffuse_bounces = 4
    scene.cycles.glossy_bounces = 4
    scene.cycles.transmission_bounces = 4
    scene.view_settings.view_transform = 'AgX'
    try:
        scene.view_settings.look = 'AgX - Punchy'
    except Exception:
        pass
    scene.view_settings.exposure = 0.0


# ---------------------------------------------------------------------------
# Mesh & Hard-Surface Modeling Utilities
# ---------------------------------------------------------------------------

def create_mesh_object(name, bm, parent=None, material=None):
    mesh = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    if material is not None:
        obj.data.materials.append(material)
    return obj


def create_empty(name, location=(0, 0, 0), rotation=(0, 0, 0), parent=None, display_size=0.05):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = display_size
    obj.location = Vector(location)
    obj.rotation_euler = Euler(rotation, 'XYZ')
    bpy.context.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj


def shade_hard_surface(obj, angle_deg=35.0, bevel_width=0.0, bevel_segments=2, weighted_normals=True):
    """Apply crisp AAA hard-surface bevels + sharp-edge smooth shading + weighted normals."""
    if obj.type != 'MESH':
        return
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    if bevel_width > 0.0:
        bev = obj.modifiers.new(name="Bevel", type='BEVEL')
        bev.width = bevel_width
        bev.segments = bevel_segments
        bev.limit_method = 'ANGLE'
        bev.angle_limit = math.radians(32.0)
        bev.harden_normals = False
        bev.use_clamp_overlap = True
        bpy.ops.object.modifier_apply(modifier=bev.name)

    # Mark sharp edges by angle on the actual mesh & triangulate N-gons (>4 verts) for clean FBX tangents
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
    angle_rad = math.radians(angle_deg)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2:
            ang = e.calc_face_angle(0.0)
            e.smooth = (ang < angle_rad)
        else:
            e.smooth = False
    bm.to_mesh(mesh)
    bm.free()

    if weighted_normals:
        wn = obj.modifiers.new(name="WeightedNormal", type='WEIGHTED_NORMAL')
        wn.keep_sharp = True
        wn.weight = 50
        bpy.ops.object.modifier_apply(modifier=wn.name)


def join_objects(objs, name, parent=None):
    """Join a list of mesh objects into a single cleanly-named object, preserving materials."""
    valid = [o for o in objs if o is not None and o.type == 'MESH']
    if not valid:
        return None
    if len(valid) == 1:
        obj = valid[0]
        obj.name = name
        if parent is not None:
            obj.parent = parent
        return obj
    bpy.ops.object.select_all(action='DESELECT')
    for o in valid:
        o.select_set(True)
    bpy.context.view_layer.objects.active = valid[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    if parent is not None:
        obj.parent = parent
    return obj


def apply_transform(obj, location=False, rotation=True, scale=True):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=location, rotation=rotation, scale=scale)


def bm_add_box(bm, center=(0, 0, 0), size=(1, 1, 1), rot=(0, 0, 0), bevel=0.0, segments=2, mat_idx=0):
    """Add a beveled/chamfered box to a bmesh."""
    sx, sy, sz = size
    ret = bmesh.ops.create_cube(bm, size=1.0)
    verts = ret['verts']
    # Scale before bevel so bevel is uniform in world meters
    scale_m = Matrix.Diagonal((sx, sy, sz, 1.0))
    bmesh.ops.transform(bm, matrix=scale_m, verts=verts)
    if bevel > 0.0:
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(
            bm, geom=edges, offset=bevel, offset_type='OFFSET',
            segments=segments, profile=0.5, affect='EDGES', clamp_overlap=True
        )
        # Re-collect all connected verts
        verts = list({v for e in edges for v in e.verts})
        # Better: collect via linked faces from any surviving vert
        # Use transform on newly created geometry via a temporary sub-bmesh for exact isolation
    # To guarantee exact transform isolation, build in temp bmesh then merge:
    return verts


def make_beveled_box(name, center=(0, 0, 0), size=(1, 1, 1), rot=(0, 0, 0),
                     bevel=0.002, segments=2, material=None):
    """Create a standalone beveled box object with clean topology and normals."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel > 0.0:
        bmesh.ops.bevel(
            bm, geom=list(bm.edges), offset=bevel, offset_type='OFFSET',
            segments=segments, profile=0.5, affect='EDGES', clamp_overlap=True
        )
    rot_m = Euler(rot, 'XYZ').to_matrix().to_4x4()
    trans_m = Matrix.Translation(Vector(center))
    bmesh.ops.transform(bm, matrix=trans_m @ rot_m, verts=bm.verts)
    return create_mesh_object(name, bm, material=material)


def make_cylinder(name, center=(0, 0, 0), radius1=0.02, radius2=None, length=0.1,
                  segs=24, axis='Y', rot=(0, 0, 0), cap_ends=True, bevel=0.0, material=None):
    """Create a cylinder oriented along X, Y, or Z with optional end bevels."""
    if radius2 is None:
        radius2 = radius1
    bm = bmesh.new()
    bmesh.ops.create_cone(
        bm, cap_ends=cap_ends, cap_tris=False, segments=segs,
        radius1=radius1, radius2=radius2, depth=length
    )
    if bevel > 0.0 and cap_ends:
        cap_edges = [e for e in bm.edges if len(e.link_faces) == 2 and
                     abs(e.verts[0].co.z - e.verts[1].co.z) < 1e-5]
        if cap_edges:
            bmesh.ops.bevel(
                bm, geom=cap_edges, offset=bevel, offset_type='OFFSET',
                segments=2, profile=0.5, affect='EDGES', clamp_overlap=True
            )
    if axis == 'Y':
        bmesh.ops.rotate(bm, matrix=Matrix.Rotation(math.radians(90), 3, 'X'), verts=bm.verts)
    elif axis == 'X':
        bmesh.ops.rotate(bm, matrix=Matrix.Rotation(math.radians(90), 3, 'Y'), verts=bm.verts)
    if any(abs(r) > 1e-6 for r in rot):
        bmesh.ops.rotate(bm, matrix=Euler(rot, 'XYZ').to_matrix(), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return create_mesh_object(name, bm, material=material)


def make_tube(name, center=(0, 0, 0), outer_r=0.02, inner_r=0.015, length=0.1,
              segs=28, axis='Y', rot=(0, 0, 0), bevel=0.0006, material=None):
    """Create a hollow tube (for barrels, suppressors, shrouds, optics)."""
    bm = bmesh.new()
    half = length * 0.5
    outer_bot, outer_top, inner_bot, inner_top = [], [], [], []
    for i in range(segs):
        a = 2.0 * math.pi * i / segs
        ca, sa = math.cos(a), math.sin(a)
        outer_bot.append(bm.verts.new((outer_r * ca, outer_r * sa, -half)))
        outer_top.append(bm.verts.new((outer_r * ca, outer_r * sa, +half)))
        inner_bot.append(bm.verts.new((inner_r * ca, inner_r * sa, -half)))
        inner_top.append(bm.verts.new((inner_r * ca, inner_r * sa, +half)))
    for i in range(segs):
        j = (i + 1) % segs
        bm.faces.new((outer_bot[i], outer_bot[j], outer_top[j], outer_top[i]))
        bm.faces.new((inner_bot[j], inner_bot[i], inner_top[i], inner_top[j]))
        bm.faces.new((outer_top[i], outer_top[j], inner_top[j], inner_top[i]))
        bm.faces.new((inner_bot[i], inner_bot[j], outer_bot[j], outer_bot[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bevel > 0.0:
        rim_edges = [e for e in bm.edges if abs(abs(e.verts[0].co.z) - half) < 1e-5
                     and abs(abs(e.verts[1].co.z) - half) < 1e-5]
        if rim_edges:
            bmesh.ops.bevel(
                bm, geom=rim_edges, offset=bevel, offset_type='OFFSET',
                segments=2, profile=0.5, affect='EDGES', clamp_overlap=True
            )
    if axis == 'Y':
        bmesh.ops.rotate(bm, matrix=Matrix.Rotation(math.radians(90), 3, 'X'), verts=bm.verts)
    elif axis == 'X':
        bmesh.ops.rotate(bm, matrix=Matrix.Rotation(math.radians(90), 3, 'Y'), verts=bm.verts)
    if any(abs(r) > 1e-6 for r in rot):
        bmesh.ops.rotate(bm, matrix=Euler(rot, 'XYZ').to_matrix(), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return create_mesh_object(name, bm, material=material)


def make_profile_extrude(name, profile_2d, width, plane='YZ', center=(0, 0, 0),
                         bevel=0.0015, segments=2, material=None):
    """
    Extrude a 2D polygon profile cleanly with beveled edges.
    plane='YZ': profile_2d is list of (y, z) extruded along X from -width/2 to +width/2.
    plane='XY': profile_2d is list of (x, y) extruded along Z from -width/2 to +width/2.
    plane='XZ': profile_2d is list of (x, z) extruded along Y from -width/2 to +width/2.
    """
    bm = bmesh.new()
    half = width * 0.5
    v_a, v_b = [], []
    for u, v in profile_2d:
        if plane == 'YZ':
            v_a.append(bm.verts.new((-half, u, v)))
            v_b.append(bm.verts.new((+half, u, v)))
        elif plane == 'XY':
            v_a.append(bm.verts.new((u, v, -half)))
            v_b.append(bm.verts.new((u, v, +half)))
        else:
            v_a.append(bm.verts.new((u, -half, v)))
            v_b.append(bm.verts.new((u, +half, v)))
    bm.faces.new(v_a)
    bm.faces.new(list(reversed(v_b)))
    n = len(profile_2d)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((v_a[i], v_b[i], v_b[j], v_a[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bevel > 0.0:
        bmesh.ops.bevel(
            bm, geom=list(bm.edges), offset=bevel, offset_type='OFFSET',
            segments=segments, profile=0.5, affect='EDGES', clamp_overlap=True
        )
    if any(abs(c) > 1e-6 for c in center):
        bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return create_mesh_object(name, bm, material=material)


def make_picatinny_rail(name, y_start, y_end, z_base, width=0.0212, height=0.0085,
                        side='TOP', x_offset=0.0, material=None):
    """
    Authentic MIL-STD-1913 Picatinny rail with trapezoidal dovetail base and
    spaced cross-slot teeth (10mm pitch, 5.2mm slot).
    side: 'TOP', 'BOTTOM', 'LEFT', 'RIGHT'.
    """
    objs = []
    length = y_end - y_start
    y_mid = 0.5 * (y_start + y_end)

    # Dovetail cross-section in XZ (extruded along Y)
    hw = width * 0.5
    nw = width * 0.36
    base_prof = [
        (-nw, 0.0), (nw, 0.0),
        (hw, height * 0.45), (hw * 0.92, height * 0.62),
        (-hw * 0.92, height * 0.62), (-hw, height * 0.45),
    ]
    base = make_profile_extrude(
        f"{name}_base", base_prof, length, plane='XZ',
        center=(0.0, y_mid, 0.0), bevel=0.0005, segments=1, material=material
    )
    objs.append(base)

    # Rail teeth (recoil lugs)
    pitch = 0.010
    tooth_len = 0.0048
    n_teeth = max(1, int((length - 0.004) / pitch))
    start_t = y_mid - (n_teeth - 1) * pitch * 0.5
    for i in range(n_teeth):
        ty = start_t + i * pitch
        tooth_prof = [
            (-hw * 0.92, height * 0.58), (hw * 0.92, height * 0.58),
            (hw, height * 0.78), (hw * 0.88, height),
            (-hw * 0.88, height), (-hw, height * 0.78),
        ]
        t = make_profile_extrude(
            f"{name}_t{i}", tooth_prof, tooth_len, plane='XZ',
            center=(0.0, ty, 0.0), bevel=0.00035, segments=1, material=material
        )
        objs.append(t)

    rail = join_objects(objs, name)
    if side == 'TOP':
        rail.location = Vector((x_offset, 0.0, z_base))
    elif side == 'BOTTOM':
        rail.rotation_euler = Euler((0.0, math.pi, 0.0), 'XYZ')
        rail.location = Vector((x_offset, 0.0, z_base))
    elif side == 'RIGHT':
        rail.rotation_euler = Euler((0.0, math.pi * 0.5, 0.0), 'XYZ')
        rail.location = Vector((x_offset, 0.0, z_base))
    elif side == 'LEFT':
        rail.rotation_euler = Euler((0.0, -math.pi * 0.5, 0.0), 'XYZ')
        rail.location = Vector((x_offset, 0.0, z_base))
    apply_transform(rail, location=True, rotation=True, scale=True)
    return rail


def make_lathe(name, profile_rz, segs=32, axis='Y', center=(0, 0, 0),
               rot=(0, 0, 0), cap_start=True, cap_end=True, material=None):
    """Revolve a list of (radius, axial_pos) around axis to create barrels, suppressors, flashlights, shells."""
    bm = bmesh.new()
    rings = []
    for r, z in profile_rz:
        ring = []
        r_safe = max(r, 1e-5)
        for i in range(segs):
            a = 2.0 * math.pi * i / segs
            ring.append(bm.verts.new((r_safe * math.cos(a), r_safe * math.sin(a), z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        r0, r1 = rings[k], rings[k + 1]
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    if cap_start and profile_rz[0][0] > 1e-4:
        bm.faces.new(list(reversed(rings[0])))
    if cap_end and profile_rz[-1][0] > 1e-4:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if axis == 'Y':
        bmesh.ops.rotate(bm, matrix=Matrix.Rotation(math.radians(-90), 3, 'X'), verts=bm.verts)
    elif axis == 'X':
        bmesh.ops.rotate(bm, matrix=Matrix.Rotation(math.radians(90), 3, 'Y'), verts=bm.verts)
    if any(abs(r) > 1e-6 for r in rot):
        bmesh.ops.rotate(bm, matrix=Euler(rot, 'XYZ').to_matrix(), verts=bm.verts)
    if any(abs(c) > 1e-6 for c in center):
        bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return create_mesh_object(name, bm, material=material)


def make_curved_sweep(name, spine_pts, section_2d, material=None, cap_ends=True):
    """
    Sweep a 2D cross-section (x, normal_z) along a list of 3D spine points (in YZ plane).
    Ideal for curved magazines, ergonomic grips, angled triggers, hose/cables.
    """
    bm = bmesh.new()
    rings = []
    n_pts = len(spine_pts)
    for idx, p in enumerate(spine_pts):
        p = Vector(p)
        if idx == 0:
            tangent = (Vector(spine_pts[1]) - p).normalized()
        elif idx == n_pts - 1:
            tangent = (p - Vector(spine_pts[idx - 1])).normalized()
        else:
            tangent = (Vector(spine_pts[idx + 1]) - Vector(spine_pts[idx - 1])).normalized()
        right = Vector((1.0, 0.0, 0.0))
        normal = tangent.cross(right).normalized()
        ring = []
        for sx, sn in section_2d:
            pos = p + right * sx + normal * sn
            ring.append(bm.verts.new(pos))
        rings.append(ring)
    m = len(section_2d)
    for k in range(n_pts - 1):
        r0, r1 = rings[k], rings[k + 1]
        for i in range(m):
            j = (i + 1) % m
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    if cap_ends:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return create_mesh_object(name, bm, material=material)


# ---------------------------------------------------------------------------
# UV Unwrapping
# ---------------------------------------------------------------------------

def smart_uv_unwrap(objs, island_margin=0.015, angle_limit_deg=60.0, cube_size=0.35):
    """Fast, uniform-texel-density Cube UV projection for crisp AAA hard-surface PBR mapping."""
    if not isinstance(objs, (list, tuple)):
        objs = [objs]
    for obj in objs:
        if obj is None or obj.type != 'MESH':
            continue
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        if not obj.data.uv_layers:
            obj.data.uv_layers.new(name="UVMap")
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.cube_project(
            cube_size=cube_size,
            correct_aspect=True,
            clip_to_bounds=False,
            scale_to_bounds=False
        )
        bpy.ops.object.mode_set(mode='OBJECT')


# ---------------------------------------------------------------------------
# Fast Vectorized NumPy 2K Procedural PBR Map Synthesis + Image I/O
# ---------------------------------------------------------------------------

def _fbm_2d(res, octaves=(8, 16, 32, 64, 128), weights=(0.35, 0.25, 0.18, 0.12, 0.10), seed=42):
    """Fast tileable multi-octave value noise on a res x res grid using NumPy."""
    rng = np.random.default_rng(seed)
    acc = np.zeros((res, res), dtype=np.float32)
    y_lin = np.linspace(0, 1, res, endpoint=False, dtype=np.float32)
    x_lin = np.linspace(0, 1, res, endpoint=False, dtype=np.float32)
    for freq, w in zip(octaves, weights):
        grid = rng.random((freq, freq), dtype=np.float32)
        gx = (x_lin * freq)
        gy = (y_lin * freq)
        x0 = np.floor(gx).astype(np.int32) % freq
        y0 = np.floor(gy).astype(np.int32) % freq
        x1 = (x0 + 1) % freq
        y1 = (y0 + 1) % freq
        fx = gx - np.floor(gx)
        fy = gy - np.floor(gy)
        # Quintic smoothstep
        sx = fx * fx * fx * (fx * (fx * 6 - 15) + 10)
        sy = fy * fy * fy * (fy * (fy * 6 - 15) + 10)
        v00 = grid[y0[:, None], x0[None, :]]
        v10 = grid[y0[:, None], x1[None, :]]
        v01 = grid[y1[:, None], x0[None, :]]
        v11 = grid[y1[:, None], x1[None, :]]
        nx0 = v00 * (1 - sx[None, :]) + v10 * sx[None, :]
        nx1 = v01 * (1 - sx[None, :]) + v11 * sx[None, :]
        acc += w * (nx0 * (1 - sy[:, None]) + nx1 * sy[:, None])
    acc = (acc - acc.min()) / max(acc.max() - acc.min(), 1e-6)
    return acc


def _height_to_normal(height, strength=2.5):
    """Convert a 2D float heightfield [0..1] into OpenGL (+Y up) tangent-space RGB normal map [0..1]."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * strength
    nx = -dx
    ny = -dy
    nz = np.ones_like(height)
    inv_len = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz + 1e-8)
    rgb = np.stack([
        (nx * inv_len) * 0.5 + 0.5,
        (ny * inv_len) * 0.5 + 0.5,
        (nz * inv_len) * 0.5 + 0.5,
    ], axis=-1)
    return np.clip(rgb, 0.0, 1.0).astype(np.float32)


def save_png(filepath, arr, colorspace='sRGB'):
    """
    Save HxWx3 or HxWx4 float32 [0..1] array to PNG via Blender's native Image writer.
    Origin: Blender image row 0 is bottom; arr row 0 is bottom.
    """
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    h, w = arr.shape[:2]
    if arr.ndim == 2:
        rgba = np.stack([arr, arr, arr, np.ones_like(arr)], axis=-1)
    elif arr.shape[2] == 3:
        rgba = np.concatenate([arr, np.ones((h, w, 1), dtype=np.float32)], axis=-1)
    else:
        rgba = arr.astype(np.float32)
    rgba = np.clip(rgba, 0.0, 1.0).astype(np.float32)

    img = bpy.data.images.new(os.path.basename(filepath), width=w, height=h, alpha=True, float_buffer=False)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = filepath
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)
    return filepath


def load_image_cached(filepath, colorspace='sRGB'):
    img = bpy.data.images.load(filepath, check_existing=True)
    img.colorspace_settings.name = colorspace
    return img


def generate_pbr_texture_set(out_dir, prefix, preset="gunmetal", res=2048, seed=101,
                             base_rgb=None, accent_rgb=None, extra_emission=None):
    """
    Synthesize a complete, realistic 2K Unity-URP-ready PBR texture set:
      - {prefix}_Albedo.png            (sRGB)
      - {prefix}_Normal.png            (Non-Color, OpenGL +Y)
      - {prefix}_MetallicSmoothness.png (Non-Color, RGB=Metallic, A=Smoothness=1-Roughness)
      - {prefix}_AO.png                (Non-Color grayscale)
      - {prefix}_Emission.png          (optional, sRGB)
    Presets:
      gunmetal, polymer_dark, polymer_fde, chrome_steel, shotgun_red_brass,
      tactical_fabric, helmet_armor, leather_rubber, corrugated_olive,
      concrete_weathered, concrete_hazard, pelican_case, radar_steel,
      server_rack, burlap_sandbag, optic_reticle, tritium_green, visor_cyan
    """
    os.makedirs(out_dir, exist_ok=True)
    n1 = _fbm_2d(res, (6, 14, 32, 64, 128), (0.32, 0.25, 0.20, 0.13, 0.10), seed=seed)
    n2 = _fbm_2d(res, (16, 48, 128, 256), (0.25, 0.30, 0.25, 0.20), seed=seed + 17)
    yy, xx = np.mgrid[0:res, 0:res].astype(np.float32) / res

    albedo = np.zeros((res, res, 3), dtype=np.float32)
    height = np.zeros((res, res), dtype=np.float32)
    metallic = np.zeros((res, res), dtype=np.float32)
    smoothness = np.zeros((res, res), dtype=np.float32)
    ao = np.clip(0.82 + 0.18 * n1, 0.0, 1.0)
    emission = None
    normal_strength = 2.2

    if preset == "gunmetal":
        # Crisp CNC-machined anodized gunmetal: fine horizontal micro-brushing + subtle edge scratches (NO wavy lumps!)
        streak = np.mean(np.stack([np.roll(n2, k, axis=1) for k in (-6, -3, 0, 3, 6)], axis=0), axis=0)
        scratches = np.clip((n2 - 0.80) * 6.5, 0.0, 1.0) * (n1 > 0.56)
        c = np.array(base_rgb or [0.11, 0.12, 0.135], dtype=np.float32)
        wear_c = np.array([0.45, 0.48, 0.52], dtype=np.float32)
        albedo = c[None, None, :] * (0.88 + 0.22 * n1[:, :, None]) + scratches[:, :, None] * (wear_c - c)[None, None, :] * 0.60
        metallic = np.clip(0.86 + 0.12 * scratches + 0.04 * (n1 - 0.5), 0.78, 0.98)
        smoothness = np.clip(0.60 + 0.12 * (1.0 - n1) + 0.16 * scratches - 0.06 * streak, 0.35, 0.86)
        height = 0.75 * streak + 0.25 * scratches
        normal_strength = 0.55

    elif preset in ("polymer_dark", "polymer_fde"):
        # Tactical stippled glass-filled nylon polymer (grip & handguard/stock)
        stipple = np.sin(xx * 2.0 * math.pi * 240.0) * np.sin(yy * 2.0 * math.pi * 240.0) * 0.5 + 0.5
        fine_grain = n2 * 0.6 + stipple * 0.4
        if preset == "polymer_fde":
            c = np.array(base_rgb or [0.27, 0.23, 0.175], dtype=np.float32)
        else:
            c = np.array(base_rgb or [0.065, 0.07, 0.078], dtype=np.float32)
        scuff = np.clip((n1 - 0.72) * 3.5, 0.0, 1.0)
        albedo = c[None, None, :] * (0.88 + 0.22 * n1[:, :, None] + 0.08 * fine_grain[:, :, None]) + scuff[:, :, None] * 0.04
        metallic = np.full((res, res), 0.04, dtype=np.float32)
        smoothness = np.clip(0.28 + 0.12 * scuff - 0.08 * fine_grain, 0.14, 0.48)
        height = 0.65 * fine_grain + 0.35 * n1
        ao = np.clip(0.78 + 0.22 * fine_grain, 0.0, 1.0)
        normal_strength = 2.8

    elif preset == "chrome_steel":
        # Brushed machined bolt / fluted barrel steel
        brush_h = np.mean(np.stack([np.roll(n2, k, axis=1) for k in (-4, -2, 0, 2, 4)], axis=0), axis=0)
        c = np.array(base_rgb or [0.58, 0.60, 0.63], dtype=np.float32)
        albedo = c[None, None, :] * (0.90 + 0.18 * brush_h[:, :, None])
        metallic = np.full((res, res), 0.96, dtype=np.float32)
        smoothness = np.clip(0.72 + 0.14 * brush_h - 0.08 * n1, 0.50, 0.92)
        height = brush_h
        normal_strength = 1.2

    elif preset == "shotgun_red_brass":
        # Crimson tactical hull + brass accent (used on shotgun shells)
        c = np.array(base_rgb or [0.52, 0.06, 0.05], dtype=np.float32)
        ribs = np.sin(xx * 2.0 * math.pi * 64.0) * 0.5 + 0.5
        albedo = c[None, None, :] * (0.88 + 0.20 * n1[:, :, None])
        metallic = np.full((res, res), 0.08, dtype=np.float32)
        smoothness = np.clip(0.45 + 0.12 * n2, 0.30, 0.65)
        height = 0.55 * ribs + 0.45 * n2
        normal_strength = 1.8

    elif preset == "brass":
        c = np.array(base_rgb or [0.76, 0.58, 0.24], dtype=np.float32)
        albedo = c[None, None, :] * (0.88 + 0.20 * n1[:, :, None])
        metallic = np.full((res, res), 0.95, dtype=np.float32)
        smoothness = np.clip(0.74 + 0.12 * n2 - 0.10 * n1, 0.45, 0.90)
        height = n2 * 0.6 + n1 * 0.4
        normal_strength = 1.1

    elif preset == "tactical_fabric":
        # Ripstop Cordura tactical uniform / plate carrier nylon weave
        warp = np.sin(xx * 2.0 * math.pi * 192.0) * 0.5 + 0.5
        weft = np.sin(yy * 2.0 * math.pi * 192.0) * 0.5 + 0.5
        ripstop_grid = ((np.mod(xx * 48.0, 1.0) < 0.08) | (np.mod(yy * 48.0, 1.0) < 0.08)).astype(np.float32)
        weave = 0.45 * warp + 0.45 * weft + 0.15 * ripstop_grid
        c = np.array(base_rgb or [0.14, 0.155, 0.145], dtype=np.float32)
        camo_var = np.where(n1 > 0.58, 0.85, np.where(n1 < 0.35, 1.14, 1.0)).astype(np.float32)
        albedo = c[None, None, :] * camo_var[:, :, None] * (0.86 + 0.22 * weave[:, :, None])
        metallic = np.full((res, res), 0.02, dtype=np.float32)
        smoothness = np.clip(0.16 + 0.08 * n1, 0.08, 0.28)
        height = 0.65 * weave + 0.35 * n1
        ao = np.clip(0.76 + 0.24 * weave, 0.0, 1.0)
        normal_strength = 3.0

    elif preset == "helmet_armor":
        # Matte ballistic Kevlar/carbon composite with tactical micro-pebble & battle scuffs
        scuff = np.clip((n2 - 0.74) * 4.5, 0.0, 1.0)
        c = np.array(base_rgb or [0.12, 0.135, 0.13], dtype=np.float32)
        albedo = c[None, None, :] * (0.86 + 0.24 * n1[:, :, None]) + scuff[:, :, None] * 0.08
        metallic = np.clip(0.15 + 0.35 * scuff, 0.10, 0.55)
        smoothness = np.clip(0.32 + 0.16 * scuff - 0.06 * n2, 0.18, 0.52)
        height = 0.6 * n2 + 0.4 * n1
        normal_strength = 2.0

    elif preset == "leather_rubber":
        # Combat boot leather / tactical glove rubber palm
        c = np.array(base_rgb or [0.055, 0.055, 0.06], dtype=np.float32)
        albedo = c[None, None, :] * (0.85 + 0.28 * n1[:, :, None])
        metallic = np.full((res, res), 0.04, dtype=np.float32)
        smoothness = np.clip(0.30 + 0.18 * n1 - 0.10 * n2, 0.12, 0.52)
        height = 0.55 * n2 + 0.45 * n1
        normal_strength = 2.5

    elif preset == "corrugated_olive":
        # Weathered military olive/slate painted shipping container steel with rust streaks & chipped paint
        rust_mask = np.clip((n1 * 0.7 + n2 * 0.5 - 0.72) * 4.2, 0.0, 1.0)
        grime = np.clip((1.0 - yy) * 0.45 + (n1 - 0.5) * 0.35, 0.0, 1.0)
        paint_c = np.array(base_rgb or [0.145, 0.18, 0.14], dtype=np.float32)
        rust_c = np.array([0.24, 0.105, 0.055], dtype=np.float32)
        albedo = paint_c[None, None, :] * (0.78 + 0.30 * n1[:, :, None]) * (1.0 - 0.25 * grime[:, :, None])
        albedo = albedo * (1.0 - rust_mask[:, :, None]) + rust_c[None, None, :] * rust_mask[:, :, None]
        # Subtle stencil stripe
        stripe = ((yy > 0.46) & (yy < 0.50) & (n2 > 0.22)).astype(np.float32)
        albedo = albedo * (1.0 - 0.45 * stripe[:, :, None]) + np.array([0.72, 0.68, 0.52])[None, None, :] * 0.45 * stripe[:, :, None]
        metallic = np.clip(0.52 + 0.32 * (1.0 - rust_mask) - 0.30 * rust_mask, 0.18, 0.88)
        smoothness = np.clip(0.42 - 0.24 * rust_mask + 0.12 * n2, 0.12, 0.62)
        height = 0.5 * n1 + 0.35 * n2 + 0.25 * rust_mask
        normal_strength = 2.6

    elif preset in ("concrete_weathered", "concrete_hazard"):
        # Poured brutalist reinforced concrete with form-tie pits, cracks, rainwater streaks & optional hazard stripe
        rain_streak = np.mean(np.stack([np.roll(n1, k, axis=0) for k in range(-8, 9, 2)], axis=0), axis=0)
        pits = (n2 < 0.16).astype(np.float32)
        c = np.array(base_rgb or [0.34, 0.35, 0.345], dtype=np.float32)
        albedo = c[None, None, :] * (0.74 + 0.36 * n1[:, :, None]) * (0.85 + 0.20 * rain_streak[:, :, None])
        albedo *= (1.0 - 0.22 * pits[:, :, None])
        if preset == "concrete_hazard":
            diag = np.mod((xx + yy) * 10.0, 1.0)
            band = (yy > 0.72) & (yy < 0.90)
            yellow = band & (diag < 0.5) & (n2 > 0.18)
            black = band & (diag >= 0.5) & (n2 > 0.18)
            albedo[yellow] = np.array([0.74, 0.54, 0.06], dtype=np.float32) * (0.85 + 0.2 * n1[yellow, None])
            albedo[black] = np.array([0.05, 0.05, 0.055], dtype=np.float32)
        metallic = np.full((res, res), 0.02, dtype=np.float32)
        smoothness = np.clip(0.18 + 0.14 * rain_streak - 0.10 * pits, 0.06, 0.38)
        height = 0.45 * n1 + 0.45 * n2 - 0.35 * pits
        ao = np.clip(0.72 + 0.28 * n1 - 0.25 * pits, 0.0, 1.0)
        normal_strength = 3.5

    elif preset == "pelican_case":
        # Heavy molded military OD/charcoal polymer crate with ribs & subtle dust
        c = np.array(base_rgb or [0.15, 0.175, 0.13], dtype=np.float32)
        scuff = np.clip((n2 - 0.75) * 4.0, 0.0, 1.0)
        albedo = c[None, None, :] * (0.85 + 0.25 * n1[:, :, None]) + scuff[:, :, None] * 0.06
        metallic = np.full((res, res), 0.06, dtype=np.float32)
        smoothness = np.clip(0.34 + 0.14 * scuff - 0.08 * n2, 0.16, 0.54)
        height = 0.55 * n2 + 0.45 * n1
        normal_strength = 2.4

    elif preset == "radar_steel":
        # Industrial weathered galvanized/painted structural steel for radar dish & floodlight stanchions
        rust = np.clip((n1 * 0.65 + n2 * 0.55 - 0.74) * 4.5, 0.0, 1.0)
        c = np.array(base_rgb or [0.36, 0.38, 0.40], dtype=np.float32)
        albedo = c[None, None, :] * (0.82 + 0.28 * n1[:, :, None]) * (1.0 - rust[:, :, None]) + np.array([0.26, 0.12, 0.06])[None, None, :] * rust[:, :, None]
        metallic = np.clip(0.75 - 0.45 * rust + 0.12 * n2, 0.20, 0.92)
        smoothness = np.clip(0.50 - 0.28 * rust + 0.14 * n2, 0.15, 0.72)
        height = 0.5 * n1 + 0.5 * n2
        normal_strength = 2.2

    elif preset == "server_rack":
        # Brushed matte black anodized server chassis + subtle perforated vent grid + LED emission texture
        c = np.array(base_rgb or [0.06, 0.068, 0.078], dtype=np.float32)
        albedo = c[None, None, :] * (0.88 + 0.22 * n1[:, :, None])
        metallic = np.full((res, res), 0.72, dtype=np.float32)
        smoothness = np.clip(0.48 + 0.12 * n2, 0.30, 0.66)
        height = 0.5 * n2 + 0.5 * n1
        normal_strength = 1.6

    elif preset == "burlap_sandbag":
        # Coarse woven burlap / hessian tactical sandbag fabric with dirt/mud staining
        warp = np.sin(xx * 2.0 * math.pi * 140.0) * 0.5 + 0.5
        weft = np.sin(yy * 2.0 * math.pi * 140.0) * 0.5 + 0.5
        weave = 0.5 * warp + 0.5 * weft
        c = np.array(base_rgb or [0.42, 0.36, 0.26], dtype=np.float32)
        dirt = np.clip((n1 - 0.45) * 1.4, 0.0, 0.5)
        albedo = c[None, None, :] * (0.78 + 0.34 * weave[:, :, None]) * (1.0 - 0.35 * dirt[:, :, None])
        metallic = np.full((res, res), 0.0, dtype=np.float32)
        smoothness = np.clip(0.10 + 0.06 * n1, 0.04, 0.20)
        height = 0.70 * weave + 0.30 * n1
        ao = np.clip(0.68 + 0.32 * weave, 0.0, 1.0)
        normal_strength = 4.0

    elif preset == "optic_reticle":
        # Crisp holographic EOTech-style red circle-dot tactical reticle!
        albedo = np.full((res, res, 3), 0.04, dtype=np.float32)
        metallic = np.full((res, res), 0.1, dtype=np.float32)
        smoothness = np.full((res, res), 0.95, dtype=np.float32)
        height = np.full((res, res), 0.5, dtype=np.float32)
        ao = np.ones((res, res), dtype=np.float32)
        cx, cy = xx - 0.5, yy - 0.5
        r = np.sqrt(cx * cx + cy * cy)
        dot = np.exp(-(r / 0.014) ** 2)
        ring = np.exp(-((r - 0.16) / 0.010) ** 2)
        tick_h = ((np.abs(cy) < 0.008) & (r > 0.13) & (r < 0.21)).astype(np.float32)
        tick_v = ((np.abs(cx) < 0.008) & (r > 0.13) & (r < 0.21)).astype(np.float32)
        glow = np.clip(dot * 1.6 + ring * 1.1 + (tick_h + tick_v) * 1.0, 0.0, 1.0)
        rc = np.array(accent_rgb or [1.0, 0.12, 0.06], dtype=np.float32)
        emission = glow[:, :, None] * rc[None, None, :]
        albedo = np.clip(albedo + emission * 0.5, 0.0, 1.0)
        normal_strength = 0.1

    elif preset in ("visor_cyan", "tritium_green", "emissive_panel"):
        rc = np.array(accent_rgb or (
            [0.10, 0.82, 1.0] if preset == "visor_cyan" else
            [0.18, 1.0, 0.32] if preset == "tritium_green" else
            [0.15, 0.85, 1.0]
        ), dtype=np.float32)
        if preset == "emissive_panel":
            # Tactical diagnostic screen / server rack LED rows
            albedo = np.full((res, res, 3), 0.04, dtype=np.float32)
            emission = np.zeros((res, res, 3), dtype=np.float32)
            # Horizontal scanlines + status bar blocks
            scan = 0.75 + 0.25 * np.sin(yy * 2.0 * math.pi * 120.0)
            grid_x = np.mod(xx * 24.0, 1.0)
            grid_y = np.mod(yy * 16.0, 1.0)
            led = ((grid_x > 0.25) & (grid_x < 0.75) & (grid_y > 0.30) & (grid_y < 0.70)).astype(np.float32)
            led_on = led * (n1 > 0.38).astype(np.float32)
            amber_on = led * (n1 > 0.76).astype(np.float32)
            emission = (
                led_on[:, :, None] * np.array([0.08, 0.85, 1.0], dtype=np.float32)[None, None, :] * 0.9
                + amber_on[:, :, None] * np.array([1.0, 0.45, 0.05], dtype=np.float32)[None, None, :]
            ) * scan[:, :, None]
            albedo = np.clip(albedo + emission * 0.3, 0.0, 1.0)
        else:
            albedo = np.broadcast_to(rc[None, None, :] * 0.35, (res, res, 3)).copy()
            emission = rc[None, None, :] * (0.85 + 0.15 * n1[:, :, None])
        metallic = np.full((res, res), 0.85 if preset == "visor_cyan" else 0.15, dtype=np.float32)
        smoothness = np.full((res, res), 0.92, dtype=np.float32)
        height = np.full((res, res), 0.5, dtype=np.float32)
        ao = np.ones((res, res), dtype=np.float32)
        normal_strength = 0.2

    normal_rgb = _height_to_normal(height, strength=normal_strength)
    met_smooth = np.stack([metallic, metallic, metallic, smoothness], axis=-1)

    paths = {}
    paths["albedo"] = save_png(os.path.join(out_dir, f"{prefix}_Albedo.png"), albedo, 'sRGB')
    paths["normal"] = save_png(os.path.join(out_dir, f"{prefix}_Normal.png"), normal_rgb, 'Non-Color')
    paths["metallic_smoothness"] = save_png(
        os.path.join(out_dir, f"{prefix}_MetallicSmoothness.png"), met_smooth, 'Non-Color'
    )
    paths["ao"] = save_png(os.path.join(out_dir, f"{prefix}_AO.png"), ao, 'Non-Color')
    if emission is not None:
        paths["emission"] = save_png(os.path.join(out_dir, f"{prefix}_Emission.png"), emission, 'sRGB')
    elif extra_emission is not None:
        paths["emission"] = save_png(os.path.join(out_dir, f"{prefix}_Emission.png"), extra_emission, 'sRGB')
    return paths


def build_pbr_material(name, tex_paths, uv_scale=1.0, emission_strength=0.0, alpha=1.0,
                       normal_strength=1.0, ao_factor=0.35):
    """
    Create a Blender Principled BSDF material wired directly to the exported 2K PNG PBR maps,
    unpacking Unity's MetallicSmoothness (R=Metallic, 1-A=Roughness) so Cycles renders match Unity URP.
    """
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    nt = mat.node_tree
    nodes = nt.nodes
    links = nt.links
    nodes.clear()

    out = nodes.new('ShaderNodeOutputMaterial')
    out.location = (600, 0)
    bsdf = nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.location = (250, 0)
    links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])

    tex_coord = nodes.new('ShaderNodeTexCoord')
    tex_coord.location = (-900, 0)
    mapping = nodes.new('ShaderNodeMapping')
    mapping.location = (-700, 0)
    mapping.inputs['Scale'].default_value = (uv_scale, uv_scale, uv_scale)
    links.new(tex_coord.outputs['UV'], mapping.inputs['Vector'])

    # Albedo + AO multiply
    alb_node = nodes.new('ShaderNodeTexImage')
    alb_node.location = (-400, 320)
    alb_node.image = load_image_cached(tex_paths["albedo"], 'sRGB')
    links.new(mapping.outputs['Vector'], alb_node.inputs['Vector'])

    if "ao" in tex_paths:
        ao_node = nodes.new('ShaderNodeTexImage')
        ao_node.location = (-400, 60)
        ao_node.image = load_image_cached(tex_paths["ao"], 'Non-Color')
        links.new(mapping.outputs['Vector'], ao_node.inputs['Vector'])

        mix_ao = nodes.new('ShaderNodeMix')
        mix_ao.data_type = 'RGBA'
        mix_ao.blend_type = 'MULTIPLY'
        mix_ao.location = (-60, 240)
        mix_ao.inputs['Factor'].default_value = ao_factor
        links.new(alb_node.outputs['Color'], mix_ao.inputs['A'])
        links.new(ao_node.outputs['Color'], mix_ao.inputs['B'])
        links.new(mix_ao.outputs['Result'], bsdf.inputs['Base Color'])
    else:
        links.new(alb_node.outputs['Color'], bsdf.inputs['Base Color'])

    # MetallicSmoothness -> Metallic (Color/R) + Roughness (1.0 - Alpha)
    if "metallic_smoothness" in tex_paths:
        ms_node = nodes.new('ShaderNodeTexImage')
        ms_node.location = (-400, -220)
        ms_node.image = load_image_cached(tex_paths["metallic_smoothness"], 'Non-Color')
        links.new(mapping.outputs['Vector'], ms_node.inputs['Vector'])

        sep = nodes.new('ShaderNodeSeparateColor')
        sep.location = (-120, -160)
        links.new(ms_node.outputs['Color'], sep.inputs['Color'])
        links.new(sep.outputs['Red'], bsdf.inputs['Metallic'])

        inv = nodes.new('ShaderNodeMath')
        inv.operation = 'SUBTRACT'
        inv.location = (-120, -320)
        inv.inputs[0].default_value = 1.0
        links.new(ms_node.outputs['Alpha'], inv.inputs[1])
        links.new(inv.outputs['Value'], bsdf.inputs['Roughness'])

    # Normal Map
    if "normal" in tex_paths:
        nrm_tex = nodes.new('ShaderNodeTexImage')
        nrm_tex.location = (-400, -520)
        nrm_tex.image = load_image_cached(tex_paths["normal"], 'Non-Color')
        links.new(mapping.outputs['Vector'], nrm_tex.inputs['Vector'])

        nrm_map = nodes.new('ShaderNodeNormalMap')
        nrm_map.location = (-60, -520)
        nrm_map.inputs['Strength'].default_value = normal_strength
        links.new(nrm_tex.outputs['Color'], nrm_map.inputs['Color'])
        links.new(nrm_map.outputs['Normal'], bsdf.inputs['Normal'])

    # Emission
    if "emission" in tex_paths and emission_strength > 0.0:
        em_node = nodes.new('ShaderNodeTexImage')
        em_node.location = (-400, -820)
        em_node.image = load_image_cached(tex_paths["emission"], 'sRGB')
        links.new(mapping.outputs['Vector'], em_node.inputs['Vector'])
        links.new(em_node.outputs['Color'], bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = emission_strength

    if alpha < 0.999:
        bsdf.inputs['Alpha'].default_value = alpha
        if 'Transmission Weight' in bsdf.inputs:
            bsdf.inputs['Transmission Weight'].default_value = 0.65

    return mat


def bake_ao_map(objs, out_path, res=1024, samples=32):
    """Bake real Cycles GPU Ambient Occlusion onto an object's UV map and save PNG."""
    scene = bpy.context.scene
    old_samples = scene.cycles.samples
    scene.cycles.samples = samples
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img = bpy.data.images.new("AO_Bake_Tmp", width=res, height=res, alpha=False)
    img.colorspace_settings.name = 'Non-Color'

    added_nodes = []
    if not isinstance(objs, (list, tuple)):
        objs = [objs]
    for obj in objs:
        if obj.type != 'MESH':
            continue
        for mat in obj.data.materials:
            if mat and mat.use_nodes:
                nt = mat.node_tree
                node = nt.nodes.new('ShaderNodeTexImage')
                node.image = img
                nt.nodes.active = node
                added_nodes.append((nt, node))

    bpy.ops.object.select_all(action='DESELECT')
    mesh_objs = [o for o in objs if o.type == 'MESH']
    for o in mesh_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = mesh_objs[0]

    scene.render.bake.margin = 8
    scene.render.bake.use_clear = True
    bpy.ops.object.bake(type='AO')

    img.filepath_raw = out_path
    img.file_format = 'PNG'
    img.save()

    for nt, node in added_nodes:
        nt.nodes.remove(node)
    bpy.data.images.remove(img)
    scene.cycles.samples = old_samples
    return out_path


# ---------------------------------------------------------------------------
# Studio Lighting & Multi-Angle Rendering for Visual QA + Website Showcase
# ---------------------------------------------------------------------------

def _get_hierarchy_bounds(root_obj):
    mins = Vector((1e9, 1e9, 1e9))
    maxs = Vector((-1e9, -1e9, -1e9))
    objs = [root_obj] + list(root_obj.children_recursive)
    found = False
    for o in objs:
        if o.type == 'MESH' and len(o.data.vertices) > 0:
            found = True
            for corner in o.bound_box:
                wc = o.matrix_world @ Vector(corner)
                mins.x = min(mins.x, wc.x)
                mins.y = min(mins.y, wc.y)
                mins.z = min(mins.z, wc.z)
                maxs.x = max(maxs.x, wc.x)
                maxs.y = max(maxs.y, wc.y)
                maxs.z = max(maxs.z, wc.z)
    if not found:
        return Vector((0, 0, 0)), Vector((1, 1, 1))
    return mins, maxs


def render_studio_shots(root_obj, out_dir, prefix, angles=None, res_qa=(960, 640),
                        res_hero=(1600, 900), samples_qa=48, samples_hero=80):
    """
    Create a temporary AAA tactical dark-obsidian studio rig (3-point rim lighting + dark floor),
    render requested camera angles + Hero 16:9 showcase, then cleanly delete studio objects
    so the .blend / .fbx export stays pristine!
    """
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    bpy.context.view_layer.update()

    mins, maxs = _get_hierarchy_bounds(root_obj)
    center = (mins + maxs) * 0.5
    extent = maxs - mins
    radius = max(extent.length * 0.5, 0.15)

    studio_objs = []

    # Dark moody obsidian studio world
    world = bpy.data.worlds.new("StudioWorld")
    world.use_nodes = True
    nt = world.node_tree
    bg = next(n for n in nt.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (0.015, 0.020, 0.028, 1.0)
    bg.inputs['Strength'].default_value = 0.85
    old_world = scene.world
    scene.world = world

    # Seamless large studio ground plane just below object bottom
    floor_size = max(radius * 40.0, 20.0)
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=floor_size)
    bmesh.ops.translate(bm, vec=Vector((center.x, center.y, mins.z - 0.002)), verts=bm.verts)
    floor_mat = bpy.data.materials.new("StudioFloor")
    floor_mat.use_nodes = True
    f_bsdf = next(n for n in floor_mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    f_bsdf.inputs['Base Color'].default_value = (0.018, 0.022, 0.028, 1.0)
    f_bsdf.inputs['Metallic'].default_value = 0.10
    f_bsdf.inputs['Roughness'].default_value = 0.48
    floor_obj = create_mesh_object("_StudioFloor", bm, material=floor_mat)
    studio_objs.append(floor_obj)

    def add_area_light(lname, loc, target, energy, color, size_x, size_y):
        ldata = bpy.data.lights.new(lname, type='AREA')
        ldata.energy = energy
        ldata.color = color
        ldata.shape = 'RECTANGLE'
        ldata.size = size_x
        ldata.size_y = size_y
        lobj = bpy.data.objects.new(lname, ldata)
        bpy.context.collection.objects.link(lobj)
        lobj.location = Vector(loc)
        direction = (Vector(target) - lobj.location).normalized()
        lobj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
        studio_objs.append(lobj)
        return lobj

    # Scale light power with radius^2 so both 22cm pistol and 6m container are evenly lit!
    p = (radius / 0.5) ** 2
    # Key light (crisp cool tactical softbox front-right-top)
    add_area_light("_KeyLight",
                   center + Vector((radius * 2.2, radius * 1.4, radius * 1.9)),
                   center, 110.0 * p, (0.90, 0.95, 1.0), radius * 2.4, radius * 1.2)
    # Warm tactical amber rim light (back-left)
    add_area_light("_RimWarm",
                   center + Vector((-radius * 2.4, -radius * 1.6, radius * 1.3)),
                   center, 85.0 * p, (1.0, 0.72, 0.42), radius * 2.0, radius * 0.9)
    # Cool cyan edge kicker (back-right)
    add_area_light("_RimCool",
                   center + Vector((radius * 2.0, -radius * 2.2, radius * 0.7)),
                   center, 55.0 * p, (0.35, 0.72, 1.0), radius * 1.6, radius * 0.8)
    # Overhead specular strip light (highlights top rails, metal bevels, helmet!)
    add_area_light("_TopStrip",
                   center + Vector((0.0, 0.0, radius * 2.8)),
                   center, 65.0 * p, (0.95, 0.98, 1.0), radius * 3.5, radius * 0.5)
    # Soft front fill so dark gunmetal details stay readable
    add_area_light("_FillLight",
                   center + Vector((-radius * 2.0, radius * 2.0, radius * 0.4)),
                   center, 35.0 * p, (0.78, 0.86, 0.98), radius * 2.5, radius * 1.8)

    cam_data = bpy.data.cameras.new("_StudioCam")
    cam_data.lens = 55.0
    cam_data.clip_start = 0.01
    cam_data.clip_end = 500.0
    cam_obj = bpy.data.objects.new("_StudioCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    studio_objs.append(cam_obj)
    scene.camera = cam_obj

    if angles is None:
        angles = [
            ("hero", (2.05, 1.15, 0.62), 55.0, True),
            ("left", (-2.35, 0.0, 0.15), 60.0, False),
            ("right", (2.35, 0.0, 0.15), 60.0, False),
            ("threequarter_back", (-1.85, -1.35, 0.75), 52.0, False),
        ]

    rendered = {}
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'

    for tag, rel_vec, lens, is_hero in angles:
        cam_data.lens = lens
        cam_obj.location = center + Vector(rel_vec) * radius
        direction = (center - cam_obj.location).normalized()
        cam_obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
        w, h = res_hero if is_hero else res_qa
        scene.render.resolution_x = w
        scene.render.resolution_y = h
        scene.render.resolution_percentage = 100
        scene.cycles.samples = samples_hero if is_hero else samples_qa
        out_path = os.path.join(out_dir, f"{prefix}_{tag}.png")
        scene.render.filepath = out_path
        bpy.ops.render.render(write_still=True)
        rendered[tag] = out_path

    # Clean up temporary studio objects & world
    scene.world = old_world
    bpy.data.worlds.remove(world)
    for o in studio_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.materials.remove(floor_mat)
    return rendered


# ---------------------------------------------------------------------------
# Unity-Ready FBX Export + Scene Save + Manifest Tracking
# ---------------------------------------------------------------------------

def export_unity_fbx(root_obj, fbx_path, blend_path=None, is_skinned=False):
    """
    Save .blend source file and export Unity-ready .fbx with exact axis conversion:
      Blender (+X right, +Y forward, +Z up) -> Unity (+X right, +Y up, +Z forward).
    Returns detailed asset metadata for manifest.json.
    """
    os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
    if blend_path:
        os.makedirs(os.path.dirname(blend_path), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    bpy.context.view_layer.update()
    objs = [root_obj] + list(root_obj.children_recursive)

    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root_obj

    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='Z',
        axis_up='Y',
        use_space_transform=True,
        bake_space_transform=(not is_skinned),
        object_types={'EMPTY', 'ARMATURE', 'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        use_tspace=True,
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        use_armature_deform_only=True,
        bake_anim=False,
        path_mode='AUTO',
        embed_textures=False,
    )

    # Compute exact stats & anchors in Unity coordinates (+X=right, +Y=Blender +Z, +Z=Blender +Y)
    total_tris = 0
    total_verts = 0
    submeshes = []
    anchors = {}
    materials = []
    mins, maxs = _get_hierarchy_bounds(root_obj)

    for o in objs:
        if o.type == 'MESH':
            me = o.data
            me.calc_loop_triangles()
            tris = len(me.loop_triangles)
            verts = len(me.vertices)
            total_tris += tris
            total_verts += verts
            submeshes.append({"name": o.name, "triangles": tris, "vertices": verts})
            for m in me.materials:
                if m and m.name not in materials:
                    materials.append(m.name)
        elif o.type == 'EMPTY' and o != root_obj:
            wp = o.matrix_world.translation
            anchors[o.name] = [round(wp.x, 4), round(wp.z, 4), round(wp.y, 4)]

    size_b = maxs - mins
    meta = {
        "name": root_obj.name,
        "fbx_path": fbx_path,
        "blend_path": blend_path,
        "file_size_bytes": os.path.getsize(fbx_path),
        "triangles": total_tris,
        "vertices": total_verts,
        "unity_bounds_meters": {
            "width_x": round(size_b.x, 4),
            "height_y": round(size_b.z, 4),
            "length_z": round(size_b.y, 4),
        },
        "unity_orientation": "+Z forward, +Y up, +X right, 1 unit = 1 meter",
        "pivot": "origin (0,0,0)",
        "submeshes": submeshes,
        "anchors_unity_xyz": anchors,
        "materials": materials,
    }
    return meta


def update_manifest(category, asset_key, entry):
    data = {}
    if os.path.exists(MANIFEST_PATH):
        with open(MANIFEST_PATH, "r") as f:
            data = json.load(f)
    data.setdefault("project", "VANGUARD ZERO")
    data.setdefault("blender_version", "5.2.1 LTS")
    data.setdefault("unity_conventions", {
        "units": "meters (1:1)",
        "axes": "+Z forward, +Y up, +X right",
        "urp_pbr_packing": {
            "albedo": "sRGB _BaseMap",
            "normal": "Linear OpenGL (+Y) _BumpMap",
            "metallic_smoothness": "Linear RGBA (R=Metallic, A=Smoothness=1-Roughness) _MetallicGlossMap",
            "ao": "Linear Grayscale _OcclusionMap",
            "emission": "sRGB _EmissionMap"
        }
    })
    data.setdefault("assets", {}).setdefault(category, {})[asset_key] = entry
    with open(MANIFEST_PATH, "w") as f:
        json.dump(data, f, indent=2)
    return MANIFEST_PATH

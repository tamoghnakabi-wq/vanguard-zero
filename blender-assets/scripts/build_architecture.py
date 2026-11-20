"""
VANGUARD ZERO v2.0 — Batch 2: High-Detail Blender 5.2 Architectural Kit (BLACKSITE-09)
Textured using REAL 2K Poly Haven CC0 photogrammetry PBR scans from blender-assets/pbr_scans/.
Eliminates EVERY raw Unity Cube from Blacksite09!
Exports:
  1. architecture/Wall_BlastSection.fbx
  2. architecture/Bunker_ModularFacade.fbx
  3. architecture/Catwalk_Section.fbx
  4. architecture/Catwalk_Stairs.fbx
  5. architecture/Fence_SecuritySection.fbx
  6. architecture/Watchtower.fbx
  7. architecture/Rooftop_HVAC.fbx
  8. props/ShippingContainer_Military.fbx (v2 photoreal real scanned corrugated steel!)
"""

import os
import sys
import math
import bpy
import bmesh
from mathutils import Vector, Euler

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

import vanguard_lib as vl

ARCH_EXPORT_DIR = os.path.join(vl.EXPORTS_DIR, "architecture")
ARCH_RENDER_DIR = os.path.join(vl.RENDERS_DIR, "architecture")
PBR_DIR = os.path.join(vl.ROOT_DIR, "pbr_scans")


def scan_paths(name):
    sub = os.path.join(PBR_DIR, name)
    return {
        "albedo": os.path.join(sub, f"{name}_Albedo.png"),
        "normal": os.path.join(sub, f"{name}_Normal.png"),
        "metallic_smoothness": os.path.join(sub, f"{name}_MetallicSmoothness.png"),
        "ao": os.path.join(sub, f"{name}_AO.png"),
    }


def load_arch_materials():
    t_conc = scan_paths("Concrete_PouredBrutalist")
    t_floor = scan_paths("Concrete_FloorWorn")
    t_corrug = scan_paths("Metal_CorrugatedSteel")
    t_rust = scan_paths("Metal_RustedStructural")
    t_steel = scan_paths("Metal_BrushedGunmetal")

    em_dir = os.path.join(vl.TEXTURES_DIR, "ArchEmissive")
    t_amber = vl.generate_pbr_texture_set(
        em_dir, "Arch_BulkheadAmber", preset="tritium_green", res=512, seed=701,
        accent_rgb=[1.0, 0.56, 0.14]
    )
    t_cyan = vl.generate_pbr_texture_set(
        em_dir, "Arch_KeycardCyan", preset="visor_cyan", res=512, seed=702,
        accent_rgb=[0.08, 0.85, 1.0]
    )

    mats = {
        "conc": vl.build_pbr_material("Arch_ConcreteBrutalist", t_conc, uv_scale=1.0, normal_strength=1.25),
        "floor": vl.build_pbr_material("Arch_ConcreteFloor", t_floor, uv_scale=1.0, normal_strength=1.2),
        "corrug": vl.build_pbr_material("Arch_CorrugatedSteel", t_corrug, uv_scale=1.0, normal_strength=1.1),
        "rust": vl.build_pbr_material("Arch_RustedSteel", t_rust, uv_scale=1.2, normal_strength=1.15),
        "steel": vl.build_pbr_material("Arch_BrushedSteel", t_steel, uv_scale=1.2, normal_strength=0.85),
        "amber": vl.build_pbr_material("Arch_BulkheadAmber", t_amber, uv_scale=1.0, emission_strength=9.0, normal_strength=0.1),
        "cyan": vl.build_pbr_material("Arch_KeycardCyan", t_cyan, uv_scale=1.0, emission_strength=7.0, normal_strength=0.1),
    }
    tex_map = {
        "Arch_ConcreteBrutalist": t_conc,
        "Arch_ConcreteFloor": t_floor,
        "Arch_CorrugatedSteel": t_corrug,
        "Arch_RustedSteel": t_rust,
        "Arch_BrushedSteel": t_steel,
        "Arch_BulkheadAmber": t_amber,
        "Arch_KeycardCyan": t_cyan,
    }
    return mats, tex_map


def make_strut(name, p0, p1, radius=0.03, segs=8, material=None):
    v0, v1 = Vector(p0), Vector(p1)
    diff = v1 - v0
    length = diff.length
    if length < 1e-4:
        return None
    mid = (v0 + v1) * 0.5
    rot_q = diff.normalized().to_track_quat('Z', 'Y')
    return vl.make_cylinder(
        name, tuple(mid), radius, radius, length, segs=segs, axis='Z',
        rot=tuple(rot_q.to_euler('XYZ')), bevel=0.0, material=material
    )


def finish_arch_asset(root, mesh_objs, name, tex_keys, tex_map, cube_size=2.5,
                      export_dir=ARCH_EXPORT_DIR, category="architecture",
                      cam_vec=(2.1, -1.9, 0.75)):
    vl.smart_uv_unwrap(mesh_objs, cube_size=cube_size)
    renders = vl.render_studio_shots(
        root, ARCH_RENDER_DIR, name,
        angles=[("hero", cam_vec, 46.0, True)],
        res_hero=(1280, 720), samples_hero=44
    )
    meta = vl.export_unity_fbx(
        root,
        os.path.join(export_dir, f"{name}.fbx"),
        os.path.join(vl.SCENES_DIR, f"{name}.blend"),
    )
    meta["textures"] = {k: tex_map[k] for k in tex_keys if k in tex_map}
    meta["renders"] = renders
    vl.update_manifest(category, name, meta)
    print(f"  -> {name}.fbx: {meta['triangles']} tris, {meta['file_size_bytes']/1024:.0f} KB")
    return meta


def build_wall_blast_section():
    print("\n=== Building Wall_BlastSection ===")
    vl.clear_scene()
    mats, tex_map = load_arch_materials()
    root = vl.create_empty("Wall_BlastSection", (0, 0, 0), display_size=0.4)
    parts = []
    # 6.0m wide (X) x 0.75m deep (Y) x 4.2m tall (Z)
    # Splayed trapezoidal T-wall base plinth
    prof_yz = [
        (-0.52, 0.0), (0.52, 0.0),
        (0.52, 0.18), (0.34, 0.55),
        (0.30, 3.85), (0.38, 3.92),
        (0.38, 4.18), (-0.38, 4.18),
        (-0.38, 3.92), (-0.30, 3.85),
        (-0.34, 0.55), (-0.52, 0.18),
    ]
    parts.append(vl.make_profile_extrude(
        "BlastWallCore", prof_yz, 6.0, plane='YZ', center=(0.0, 0.0, 0.0),
        bevel=0.025, segments=2, material=mats["conc"]
    ))
    # 3 heavy vertical structural concrete buttress pilasters (ends + center)
    for px in (-2.80, 0.0, 2.80):
        parts.append(vl.make_beveled_box(
            f"Pilaster_{px}", (px, 0.0, 2.05), (0.44, 0.88, 3.92), bevel=0.025, material=mats["conc"]
        ))
        # Steel tie-plate & bolts at pilaster base & mid
        for tz in (0.75, 2.45, 3.65):
            parts.append(vl.make_beveled_box(
                f"TiePlate_{px}_{tz}", (px, 0.0, tz), (0.30, 0.92, 0.16), bevel=0.008, material=mats["rust"]
            ))
    # Horizontal ribbed acoustic/blast relief bands across both faces
    for rz in (1.15, 2.05, 2.95):
        parts.append(vl.make_beveled_box(
            f"BlastRib_{rz}", (0.0, 0.0, rz), (5.50, 0.68, 0.12), bevel=0.015, material=mats["conc"]
        ))
    # Exterior steel cable conduit tray & pipes along front face (-Y)
    parts.append(vl.make_beveled_box("CableTray", (0.0, -0.37, 3.35), (5.92, 0.10, 0.14), bevel=0.008, material=mats["rust"]))
    for cz in (3.15, 3.52):
        parts.append(vl.make_cylinder(f"Conduit_{cz}", (0.0, -0.36, cz), 0.028, 0.028, 5.95, segs=12, axis='X', material=mats["steel"]))
    # Angled top security barbed-wire outrigger brackets + 3 wire strands
    for bx in (-2.75, -1.40, 0.0, 1.40, 2.75):
        parts.append(make_strut(f"OutrigA_{bx}", (bx, -0.22, 4.16), (bx, -0.58, 4.62), radius=0.024, segs=8, material=mats["rust"]))
        parts.append(make_strut(f"OutrigB_{bx}", (bx, 0.22, 4.16), (bx, 0.58, 4.62), radius=0.024, segs=8, material=mats["rust"]))
    for sy_sign in (-1.0, 1.0):
        for t in (0.35, 0.68, 0.96):
            wy = sy_sign * (0.22 + t * 0.36)
            wz = 4.16 + t * 0.46
            parts.append(vl.make_cylinder(f"Wire_{sy_sign}_{t}", (0.0, wy, wz), 0.007, 0.007, 5.95, segs=6, axis='X', material=mats["steel"]))

    mesh = vl.join_objects(parts, "Wall_BlastSection_Mesh", parent=root)
    vl.shade_hard_surface(mesh, angle_deg=36.0, weighted_normals=True)
    finish_arch_asset(
        root, [mesh], "Wall_BlastSection",
        ["Arch_ConcreteBrutalist", "Arch_RustedSteel", "Arch_BrushedSteel"], tex_map, cube_size=3.2
    )


def build_bunker_facade():
    print("\n=== Building Bunker_ModularFacade ===")
    vl.clear_scene()
    mats, tex_map = load_arch_materials()
    root = vl.create_empty("Bunker_ModularFacade", (0, 0, 0), display_size=0.4)
    parts = []
    # 8.0m wide (X) x 1.6m deep (Y) x 4.6m tall (Z), front facing -Y
    # Left & right thick brutalist bunker piers flanking recessed blast doorway
    for sx in (-2.75, 2.75):
        parts.append(vl.make_beveled_box(f"BunkerPier_{sx}", (sx, 0.0, 2.30), (2.50, 1.40, 4.60), bevel=0.035, material=mats["conc"]))
        # Sloped armored plinth base
        parts.append(vl.make_beveled_box(f"PierBase_{sx}", (sx, -0.10, 0.25), (2.62, 1.62, 0.50), bevel=0.03, material=mats["conc"]))
    # Heavy overhanging cantilevered concrete header lintel & parapet above doorway
    parts.append(vl.make_beveled_box("DoorLintel", (0.0, -0.18, 3.85), (8.20, 1.85, 1.50), bevel=0.04, material=mats["conc"]))
    parts.append(vl.make_beveled_box("RoofParapet", (0.0, 0.0, 4.68), (8.30, 1.60, 0.26), bevel=0.025, material=mats["conc"]))
    # Recessed inner doorway surround steel casing
    parts.append(vl.make_beveled_box("DoorFrameL", (-1.42, -0.42, 1.55), (0.18, 0.65, 3.10), bevel=0.015, material=mats["rust"]))
    parts.append(vl.make_beveled_box("DoorFrameR", (1.42, -0.42, 1.55), (0.18, 0.65, 3.10), bevel=0.015, material=mats["rust"]))
    parts.append(vl.make_beveled_box("DoorFrameTop", (0.0, -0.42, 3.05), (3.02, 0.65, 0.20), bevel=0.015, material=mats["rust"]))
    # Armored Recessed Double Steel Blast Doors (with stiffener ribs & heavy hinges)
    for dx in (-0.66, 0.66):
        parts.append(vl.make_beveled_box(f"BlastDoor_{dx}", (dx, -0.25, 1.48), (1.28, 0.16, 2.94), bevel=0.012, material=mats["steel"]))
        for rz in (0.45, 1.15, 1.85, 2.55):
            parts.append(vl.make_beveled_box(f"DoorRib_{dx}_{rz}", (dx, -0.35, rz), (1.16, 0.06, 0.12), bevel=0.008, material=mats["rust"]))
        # Heavy cylindrical hinges
        for hz in (0.55, 2.40):
            hx = -1.28 if dx < 0 else 1.28
            parts.append(vl.make_cylinder(f"Hinge_{dx}_{hz}", (hx, -0.37, hz), 0.045, 0.045, 0.22, segs=12, axis='Z', material=mats["rust"]))
    # Horizontal panic crash bar + wheel locking latch
    parts.append(vl.make_beveled_box("CrashBar", (0.0, -0.40, 1.08), (1.90, 0.06, 0.08), bevel=0.008, material=mats["rust"]))
    # Overhead armored bulkhead warning lamp (glowing warm amber!) + Cyan Keycard Reader!
    parts.append(vl.make_beveled_box("LampCage", (0.0, -0.95, 3.32), (0.65, 0.24, 0.22), bevel=0.015, material=mats["rust"]))
    parts.append(vl.make_beveled_box("LampGlow", (0.0, -1.04, 3.30), (0.52, 0.08, 0.12), bevel=0.008, material=mats["amber"]))
    parts.append(vl.make_beveled_box("KeycardBox", (1.68, -0.74, 1.38), (0.24, 0.12, 0.36), bevel=0.01, material=mats["steel"]))
    parts.append(vl.make_beveled_box("KeycardScreen", (1.68, -0.805, 1.44), (0.16, 0.02, 0.12), bevel=0.004, material=mats["cyan"]))
    # Side Louvered Industrial Exhaust Grilles (left & right bunker piers)
    for sx in (-2.75, 2.75):
        parts.append(vl.make_beveled_box(f"VentFrame_{sx}", (sx, -0.72, 2.25), (1.35, 0.16, 0.95), bevel=0.015, material=mats["rust"]))
        for li in range(7):
            lz = 1.88 + li * 0.125
            parts.append(vl.make_beveled_box(
                f"Louver_{sx}_{li}", (sx, -0.78, lz), (1.22, 0.10, 0.05),
                rot=(0.45, 0, 0), bevel=0.004, material=mats["steel"]
            ))
        # Vertical steel conduit pipes & junction box
        parts.append(vl.make_cylinder(f"VertPipe_{sx}", (sx + (0.85 if sx < 0 else -0.85), -0.74, 2.30), 0.04, 0.04, 4.10, segs=12, axis='Z', material=mats["steel"]))

    mesh = vl.join_objects(parts, "Bunker_ModularFacade_Mesh", parent=root)
    vl.shade_hard_surface(mesh, angle_deg=36.0, weighted_normals=True)
    finish_arch_asset(
        root, [mesh], "Bunker_ModularFacade",
        ["Arch_ConcreteBrutalist", "Arch_RustedSteel", "Arch_BrushedSteel", "Arch_BulkheadAmber", "Arch_KeycardCyan"],
        tex_map, cube_size=3.2, cam_vec=(1.9, -2.2, 0.65)
    )


def build_catwalk_section():
    print("\n=== Building Catwalk_Section ===")
    vl.clear_scene()
    mats, tex_map = load_arch_materials()
    root = vl.create_empty("Catwalk_Section", (0, 0, 0), display_size=0.35)
    parts = []
    # 4.0m long (Y) x 1.8m wide (X); deck walking surface cleanly at Z=0.0!
    L, W = 4.0, 1.8
    # Ribbed steel grating deck + structural cross-ribs underneath
    parts.append(vl.make_beveled_box("DeckPlate", (0.0, 0.0, -0.025), (W - 0.06, L, 0.05), bevel=0.006, material=mats["steel"]))
    # Longitudinal steel I-beam girders along both edges
    for sx in (-W * 0.5 + 0.06, W * 0.5 - 0.06):
        parts.append(vl.make_beveled_box(f"IBeamWeb_{sx}", (sx, 0.0, -0.18), (0.04, L, 0.28), bevel=0.004, material=mats["rust"]))
        parts.append(vl.make_beveled_box(f"IBeamTop_{sx}", (sx, 0.0, -0.04), (0.14, L, 0.03), bevel=0.004, material=mats["rust"]))
        parts.append(vl.make_beveled_box(f"IBeamBot_{sx}", (sx, 0.0, -0.32), (0.14, L, 0.03), bevel=0.004, material=mats["rust"]))
        # Safety toe kickplate along deck edge
        parts.append(vl.make_beveled_box(f"Kickplate_{sx}", (sx, 0.0, 0.075), (0.025, L, 0.15), bevel=0.004, material=mats["rust"]))
        # Top & mid tubular safety handrails
        for hz in (0.58, 1.12):
            parts.append(vl.make_cylinder(f"Rail_{sx}_{hz}", (sx, 0.0, hz), 0.022, 0.022, L, segs=12, axis='Y', material=mats["steel"]))
        # Vertical stanchion posts with bolted flanges
        for py in (-1.92, -0.64, 0.64, 1.92):
            parts.append(vl.make_cylinder(f"Post_{sx}_{py}", (sx, py, 0.56), 0.024, 0.024, 1.16, segs=12, axis='Z', material=mats["rust"]))
            parts.append(vl.make_beveled_box(f"Flange_{sx}_{py}", (sx, py, 0.01), (0.10, 0.10, 0.025), bevel=0.004, material=mats["steel"]))
    # Transverse floor crossbeams underneath
    for cy in (-1.8, -0.9, 0.0, 0.9, 1.8):
        parts.append(vl.make_beveled_box(f"CrossBeam_{cy}", (0.0, cy, -0.16), (W - 0.16, 0.08, 0.18), bevel=0.006, material=mats["rust"]))

    mesh = vl.join_objects(parts, "Catwalk_Section_Mesh", parent=root)
    vl.shade_hard_surface(mesh, angle_deg=36.0, weighted_normals=True)
    finish_arch_asset(
        root, [mesh], "Catwalk_Section",
        ["Arch_RustedSteel", "Arch_BrushedSteel"], tex_map, cube_size=1.8
    )


def build_catwalk_stairs():
    print("\n=== Building Catwalk_Stairs ===")
    vl.clear_scene()
    mats, tex_map = load_arch_materials()
    root = vl.create_empty("Catwalk_Stairs", (0, 0, 0), display_size=0.35)
    parts = []
    # Run = 4.2m along +Y, Rise = 3.5m along +Z, Width = 1.6m along X
    W, RUN, RISE = 1.6, 4.2, 3.5
    n_steps = 14
    for i in range(n_steps):
        t = (i + 0.5) / n_steps
        sy = t * RUN
        sz = (i + 1) * (RISE / n_steps)
        parts.append(vl.make_beveled_box(f"Tread_{i}", (0.0, sy, sz - 0.025), (W - 0.12, 0.30, 0.045), bevel=0.005, material=mats["steel"]))
    # Twin inclined steel C-channel stringers + sloped handrails on both sides
    for sx in (-W * 0.5 + 0.04, W * 0.5 - 0.04):
        parts.append(make_strut(f"Stringer_{sx}", (sx, 0.0, 0.05), (sx, RUN, RISE - 0.08), radius=0.09, segs=6, material=mats["rust"]))
        parts.append(make_strut(f"TopRail_{sx}", (sx, 0.0, 1.10), (sx, RUN, RISE + 1.10), radius=0.024, segs=10, material=mats["steel"]))
        parts.append(make_strut(f"MidRail_{sx}", (sx, 0.0, 0.58), (sx, RUN, RISE + 0.58), radius=0.018, segs=10, material=mats["steel"]))
        for k in range(5):
            u = k / 4.0
            py = u * RUN
            pz = u * RISE
            parts.append(vl.make_cylinder(f"StairPost_{sx}_{k}", (sx, py, pz + 0.55), 0.024, 0.024, 1.12, segs=10, axis='Z', material=mats["rust"]))

    mesh = vl.join_objects(parts, "Catwalk_Stairs_Mesh", parent=root)
    vl.shade_hard_surface(mesh, angle_deg=36.0, weighted_normals=True)
    finish_arch_asset(
        root, [mesh], "Catwalk_Stairs",
        ["Arch_RustedSteel", "Arch_BrushedSteel"], tex_map, cube_size=1.8
    )


def build_security_fence():
    print("\n=== Building Fence_SecuritySection ===")
    vl.clear_scene()
    mats, tex_map = load_arch_materials()
    root = vl.create_empty("Fence_SecuritySection", (0, 0, 0), display_size=0.35)
    parts = []
    # 4.0m wide (X) x 3.2m tall (Z)
    # Concrete security curb base
    parts.append(vl.make_beveled_box("CurbBase", (0.0, 0.0, 0.22), (4.0, 0.34, 0.44), bevel=0.02, material=mats["conc"]))
    # Heavy vertical end & mid galvanized posts + top/mid/bottom rails
    for px in (-1.94, 0.0, 1.94):
        parts.append(vl.make_cylinder(f"FencePost_{px}", (px, 0.0, 1.65), 0.042, 0.042, 2.55, segs=12, axis='Z', material=mats["rust"]))
        # Angled Y barbed-wire outriggers
        parts.append(make_strut(f"ArmL_{px}", (px, 0.0, 2.86), (px, -0.38, 3.28), radius=0.022, segs=8, material=mats["rust"]))
        parts.append(make_strut(f"ArmR_{px}", (px, 0.0, 2.86), (px, 0.38, 3.28), radius=0.022, segs=8, material=mats["rust"]))
    for rz in (0.50, 1.68, 2.84):
        parts.append(vl.make_cylinder(f"HorizRail_{rz}", (0.0, 0.0, rz), 0.024, 0.024, 3.94, segs=10, axis='X', material=mats["steel"]))
    # High-security welded vertical picket/mesh bars (reads crisply at distance without alpha artifacts!)
    for i in range(25):
        bx = -1.84 + i * (3.68 / 24.0)
        parts.append(vl.make_cylinder(f"MeshV_{i}", (bx, 0.0, 1.67), 0.008, 0.008, 2.32, segs=6, axis='Z', material=mats["steel"]))
    for j in range(10):
        bz = 0.62 + j * 0.23
        parts.append(vl.make_cylinder(f"MeshH_{j}", (0.0, 0.0, bz), 0.006, 0.006, 3.88, segs=6, axis='X', material=mats["steel"]))
    # Triple barbed-wire strands along both angled arms
    for sy_sign in (-1.0, 1.0):
        for t in (0.35, 0.68, 0.96):
            parts.append(vl.make_cylinder(f"BarbWire_{sy_sign}_{t}", (0.0, sy_sign * 0.38 * t, 2.86 + 0.42 * t), 0.007, 0.007, 3.96, segs=6, axis='X', material=mats["steel"]))

    mesh = vl.join_objects(parts, "Fence_SecuritySection_Mesh", parent=root)
    vl.shade_hard_surface(mesh, angle_deg=36.0, weighted_normals=True)
    finish_arch_asset(
        root, [mesh], "Fence_SecuritySection",
        ["Arch_ConcreteBrutalist", "Arch_RustedSteel", "Arch_BrushedSteel"], tex_map, cube_size=2.0
    )


def build_watchtower():
    print("\n=== Building Watchtower ===")
    vl.clear_scene()
    mats, tex_map = load_arch_materials()
    root = vl.create_empty("Watchtower", (0, 0, 0), display_size=0.6)
    parts = []
    # 3.4m x 3.4m footprint, 7.6m tall
    # 4 concrete footings + structural steel legs + X-bracing (Z: 0..4.2m)
    legs = [(-1.35, -1.35), (1.35, -1.35), (1.35, 1.35), (-1.35, 1.35)]
    for idx, (lx, ly) in enumerate(legs):
        parts.append(vl.make_beveled_box(f"Footing_{idx}", (lx, ly, 0.20), (0.58, 0.58, 0.40), bevel=0.025, material=mats["conc"]))
        parts.append(vl.make_beveled_box(f"Leg_{idx}", (lx, ly, 2.30), (0.18, 0.18, 3.90), bevel=0.01, material=mats["rust"]))
    for ci in range(4):
        cj = (ci + 1) % 4
        x0, y0 = legs[ci]
        x1, y1 = legs[cj]
        for z_lo, z_hi in [(0.45, 2.30), (2.30, 4.15)]:
            parts.append(make_strut(f"BraceA_{ci}_{z_lo}", (x0, y0, z_lo), (x1, y1, z_hi), radius=0.036, segs=8, material=mats["rust"]))
            parts.append(make_strut(f"BraceB_{ci}_{z_lo}", (x1, y1, z_lo), (x0, y0, z_hi), radius=0.036, segs=8, material=mats["rust"]))
            parts.append(make_strut(f"Girt_{ci}_{z_hi}", (x0, y0, z_hi), (x1, y1, z_hi), radius=0.045, segs=8, material=mats["rust"]))

    # Elevated Armored Guard Booth Deck (Z: 4.20m) + Waist-High Ballistic Parapet (Z: 4.25..5.45m)
    parts.append(vl.make_beveled_box("DeckFloor", (0.0, 0.0, 4.25), (3.60, 3.60, 0.18), bevel=0.02, material=mats["steel"]))
    # 4 waist-high armored booth wall panels with recessed gun-port vision slits!
    for sx in (-1.42, 1.42):
        parts.append(vl.make_beveled_box(f"BoothWallX_{sx}", (sx, 0.0, 4.92), (0.16, 2.84, 1.22), bevel=0.015, material=mats["corrug"]))
    for sy in (-1.42, 1.42):
        parts.append(vl.make_beveled_box(f"BoothWallY_{sy}", (0.0, sy, 4.92), (2.84, 0.16, 1.22), bevel=0.015, material=mats["corrug"]))
    # 4 Corner steel roof columns (Z: 5.45..6.85m) leaving open 360-degree sniper observation band!
    for idx, (lx, ly) in enumerate(legs):
        parts.append(vl.make_beveled_box(f"RoofCol_{idx}", (lx, ly, 5.60), (0.14, 0.14, 2.55), bevel=0.01, material=mats["rust"]))
    # Cantilevered Corrugated Metal Sloped Roof Overhang (Z: 6.85..7.18m)
    parts.append(vl.make_beveled_box("RoofFrame", (0.0, 0.0, 6.90), (4.10, 4.10, 0.16), rot=(0.06, 0, 0), bevel=0.02, material=mats["rust"]))
    parts.append(vl.make_beveled_box("RoofCorrug", (0.0, 0.0, 7.02), (4.25, 4.25, 0.10), rot=(0.06, 0, 0), bevel=0.015, material=mats["corrug"]))
    # Rooftop comms whip antenna + glowing amber aviation beacon + perimeter exterior ladder
    parts.append(vl.make_cylinder("TowerAntenna", (1.25, 1.25, 7.85), 0.022, 0.010, 1.65, segs=10, axis='Z', material=mats["steel"]))
    parts.append(vl.make_cylinder("TowerBeacon", (1.25, 1.25, 8.70), 0.055, 0.045, 0.12, segs=12, axis='Z', material=mats["amber"]))
    # Access ladder on +Y face
    for lx in (-0.22, 0.22):
        parts.append(vl.make_cylinder(f"LadderSide_{lx}", (lx, 1.62, 2.25), 0.024, 0.024, 4.40, segs=8, axis='Z', material=mats["steel"]))
    for r in range(14):
        parts.append(vl.make_cylinder(f"LadderRung_{r}", (0.0, 1.62, 0.35 + r * 0.30), 0.014, 0.014, 0.44, segs=8, axis='X', material=mats["steel"]))

    mesh = vl.join_objects(parts, "Watchtower_Mesh", parent=root)
    vl.shade_hard_surface(mesh, angle_deg=36.0, weighted_normals=True)
    finish_arch_asset(
        root, [mesh], "Watchtower",
        ["Arch_ConcreteBrutalist", "Arch_CorrugatedSteel", "Arch_RustedSteel", "Arch_BrushedSteel", "Arch_BulkheadAmber"],
        tex_map, cube_size=2.6, cam_vec=(2.2, -2.0, 0.55)
    )


def build_rooftop_hvac():
    print("\n=== Building Rooftop_HVAC ===")
    vl.clear_scene()
    mats, tex_map = load_arch_materials()
    root = vl.create_empty("Rooftop_HVAC", (0, 0, 0), display_size=0.3)
    parts = [
        # Steel mounting skid rails
        vl.make_beveled_box("SkidL", (0.0, -0.78, 0.07), (2.65, 0.14, 0.14), bevel=0.01, material=mats["rust"]),
        vl.make_beveled_box("SkidR", (0.0, 0.78, 0.07), (2.65, 0.14, 0.14), bevel=0.01, material=mats["rust"]),
        # Main industrial air-handler cabinet
        vl.make_beveled_box("HVACBody", (0.0, 0.0, 0.78), (2.50, 1.65, 1.28), bevel=0.025, material=mats["steel"]),
        # Recessed electrical disconnect panel on front
        vl.make_beveled_box("ControlPanel", (-0.82, -0.84, 0.80), (0.58, 0.08, 0.72), bevel=0.01, material=mats["rust"]),
        # Insulated side exhaust duct elbow
        vl.make_beveled_box("DuctOut", (1.52, 0.0, 0.72), (0.60, 0.76, 0.62), bevel=0.018, material=mats["corrug"]),
        vl.make_beveled_box("DuctFlange", (1.28, 0.0, 0.72), (0.08, 0.86, 0.72), bevel=0.008, material=mats["rust"]),
    ]
    # Front intake louver grilles
    for li in range(8):
        parts.append(vl.make_beveled_box(
            f"HVACLouver_{li}", (0.40, -0.84, 0.36 + li * 0.115), (1.35, 0.08, 0.045),
            rot=(0.40, 0, 0), bevel=0.004, material=mats["rust"]
        ))
    # Dual Top Industrial Exhaust Fan Cowls + Hub & Blades
    for fx in (-0.60, 0.60):
        parts.append(vl.make_tube(f"FanShroud_{fx}", (fx, 0.0, 1.50), 0.50, 0.44, 0.18, segs=24, axis='Z', bevel=0.008, material=mats["rust"]))
        parts.append(vl.make_cylinder(f"FanHub_{fx}", (fx, 0.0, 1.48), 0.12, 0.10, 0.12, segs=16, axis='Z', material=mats["steel"]))
        for b in range(5):
            ang = b * 2.0 * math.pi / 5.0
            bx = fx + math.cos(ang) * 0.26
            by = math.sin(ang) * 0.26
            parts.append(vl.make_beveled_box(
                f"Blade_{fx}_{b}", (bx, by, 1.48), (0.32, 0.10, 0.02),
                rot=(0.35, 0, ang), bevel=0.003, material=mats["steel"]
            ))

    mesh = vl.join_objects(parts, "Rooftop_HVAC_Mesh", parent=root)
    vl.shade_hard_surface(mesh, angle_deg=36.0, weighted_normals=True)
    finish_arch_asset(
        root, [mesh], "Rooftop_HVAC",
        ["Arch_CorrugatedSteel", "Arch_RustedSteel", "Arch_BrushedSteel"], tex_map, cube_size=1.8
    )


def rebuild_shipping_container_v2():
    """Rebuild ShippingContainer_Military.fbx with REAL Poly Haven Metal_CorrugatedSteel + RustedSteel scans (zero aliasing!)."""
    print("\n=== Rebuilding ShippingContainer_Military v2 (Real Scanned PBR) ===")
    vl.clear_scene()
    mats, tex_map = load_arch_materials()
    root = vl.create_empty("ShippingContainer_Military", (0, 0, 0), display_size=0.5)
    parts = []
    W, L, H = 2.44, 6.06, 2.59
    hw, hl = W * 0.5, L * 0.5

    parts.append(vl.make_beveled_box("CoreHull", (0.0, 0.0, H * 0.5), (W - 0.10, L - 0.12, H - 0.10), bevel=0.015, material=mats["corrug"]))
    for sx in (-hw + 0.07, hw - 0.07):
        parts.append(vl.make_beveled_box(f"TopRail_{sx}", (sx, 0.0, H - 0.08), (0.14, L - 0.30, 0.16), bevel=0.012, material=mats["rust"]))
        parts.append(vl.make_beveled_box(f"BotRail_{sx}", (sx, 0.0, 0.08), (0.14, L - 0.30, 0.16), bevel=0.012, material=mats["rust"]))
    for sy in (-hl + 0.08, hl - 0.08):
        parts.append(vl.make_beveled_box(f"EndTop_{sy}", (0.0, sy, H - 0.09), (W - 0.30, 0.16, 0.18), bevel=0.012, material=mats["rust"]))
        parts.append(vl.make_beveled_box(f"EndBot_{sy}", (0.0, sy, 0.09), (W - 0.30, 0.16, 0.18), bevel=0.012, material=mats["rust"]))
    for sx in (-hw + 0.085, hw - 0.085):
        for sy in (-hl + 0.085, hl - 0.085):
            parts.append(vl.make_beveled_box(f"CornerPost_{sx}_{sy}", (sx, sy, H * 0.5), (0.17, 0.17, H - 0.36), bevel=0.012, material=mats["rust"]))
            for sz in (0.095, H - 0.095):
                parts.append(vl.make_beveled_box(f"ISOCast_{sx}_{sy}_{sz}", (sx, sy, sz), (0.19, 0.19, 0.19), bevel=0.014, material=mats["steel"]))
    n_ribs = 20
    for i in range(n_ribs):
        ry = -hl + 0.32 + i * ((L - 0.64) / (n_ribs - 1))
        for sx in (-hw + 0.035, hw - 0.035):
            parts.append(vl.make_beveled_box(f"SideRib_{sx}_{i}", (sx, ry, H * 0.5), (0.07, 0.125, H - 0.34), bevel=0.018, material=mats["corrug"]))
        parts.append(vl.make_beveled_box(f"RoofRib_{i}", (0.0, ry, H - 0.028), (W - 0.28, 0.13, 0.056), bevel=0.014, material=mats["corrug"]))
    for sx in (-0.55, 0.55):
        parts.append(vl.make_beveled_box(f"DoorPanel_{sx}", (sx, hl - 0.06, H * 0.5), (1.06, 0.06, H - 0.36), bevel=0.012, material=mats["corrug"]))
    for lx in (-0.78, -0.26, 0.26, 0.78):
        parts.append(vl.make_cylinder(f"LockBar_{lx}", (lx, hl + 0.015, H * 0.5), 0.022, 0.022, H - 0.24, segs=12, axis='Z', material=mats["steel"]))
        parts.append(vl.make_beveled_box(f"LockHandle_{lx}", (lx + 0.08, hl + 0.045, 1.05), (0.22, 0.035, 0.045), bevel=0.006, material=mats["steel"]))

    mesh = vl.join_objects(parts, "ShippingContainer_Mesh", parent=root)
    vl.shade_hard_surface(mesh, angle_deg=36.0, weighted_normals=True)
    finish_arch_asset(
        root, [mesh], "ShippingContainer_Military",
        ["Arch_CorrugatedSteel", "Arch_RustedSteel", "Arch_BrushedSteel"], tex_map,
        cube_size=3.0, export_dir=os.path.join(vl.EXPORTS_DIR, "props"), category="props",
        cam_vec=(2.15, 1.65, 0.75)
    )


if __name__ == "__main__":
    build_wall_blast_section()
    build_bunker_facade()
    build_catwalk_section()
    build_catwalk_stairs()
    build_security_fence()
    build_watchtower()
    build_rooftop_hvac()
    rebuild_shipping_container_v2()
    print("\n=== BATCH 2 ARCHITECTURE COMPLETE ===")

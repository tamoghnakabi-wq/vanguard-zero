"""
VANGUARD ZERO — Batch 3: Environment & Tactical Props Builder (BLACKSITE-09)
Generates all 7 AAA environment props (+ .blend, 2K PBR maps, studio renders):
  1. ShippingContainer_Military.fbx
  2. ConcreteBarrier_Jersey.fbx
  3. TacticalSupplyCrate.fbx
  4. RadarDishTower.fbx
  5. FloodlightTower.fbx
  6. ServerRack_Terminal.fbx
  7. SandbagWall.fbx
All props rest flat on Z=0 in Blender (Y=0 ground in Unity), meters scale 1:1.
"""

import os
import sys
import math
import random
import bpy
import bmesh
from mathutils import Vector, Matrix, Euler

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

import vanguard_lib as vl


def make_strut(name, p0, p1, radius=0.04, segs=8, material=None):
    """Create a cylindrical or square structural steel strut cleanly bridging p0 -> p1."""
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


def build_shipping_container():
    print("\n=== Building ShippingContainer_Military ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "ShippingContainer")
    t_olive = vl.generate_pbr_texture_set(
        tex_dir, "Container_OliveSteel", preset="corrugated_olive", res=2048, seed=501,
        base_rgb=[0.145, 0.178, 0.140]
    )
    t_steel = vl.generate_pbr_texture_set(
        tex_dir, "Container_SteelHardware", preset="radar_steel", res=1024, seed=502,
        base_rgb=[0.38, 0.40, 0.42]
    )
    t_dark = vl.generate_pbr_texture_set(
        tex_dir, "Container_RubberSeal", preset="polymer_dark", res=512, seed=503
    )

    m_olive = vl.build_pbr_material("Container_OliveSteel", t_olive, uv_scale=1.0, normal_strength=1.1)
    m_steel = vl.build_pbr_material("Container_SteelHardware", t_steel, uv_scale=1.5, normal_strength=0.9)
    m_dark = vl.build_pbr_material("Container_RubberSeal", t_dark, uv_scale=2.0, normal_strength=0.8)

    root = vl.create_empty("ShippingContainer_Military", (0, 0, 0), display_size=0.5)
    parts = []
    # ISO 20ft dimensions: W=2.44m (X), L=6.06m (Y), H=2.59m (Z), resting on Z=0
    W, L, H = 2.44, 6.06, 2.59
    hw, hl = W * 0.5, L * 0.5

    # Core inner hull box (recessed slightly inside corrugated ribs)
    parts.append(vl.make_beveled_box(
        "CoreHull", (0.0, 0.0, H * 0.5), (W - 0.10, L - 0.12, H - 0.10), bevel=0.015, material=m_olive
    ))
    # Heavy top & bottom longitudinal side rails + end header/sill beams
    for sx in (-hw + 0.07, hw - 0.07):
        parts.append(vl.make_beveled_box(f"TopRail_{sx}", (sx, 0.0, H - 0.08), (0.14, L - 0.30, 0.16), bevel=0.01, material=m_olive))
        parts.append(vl.make_beveled_box(f"BotRail_{sx}", (sx, 0.0, 0.08), (0.14, L - 0.30, 0.16), bevel=0.01, material=m_olive))
    for sy in (-hl + 0.08, hl - 0.08):
        parts.append(vl.make_beveled_box(f"EndTop_{sy}", (0.0, sy, H - 0.09), (W - 0.30, 0.16, 0.18), bevel=0.01, material=m_olive))
        parts.append(vl.make_beveled_box(f"EndBot_{sy}", (0.0, sy, 0.09), (W - 0.30, 0.16, 0.18), bevel=0.01, material=m_olive))
    # 4 vertical corner posts + 8 ISO heavy corner castings
    for sx in (-hw + 0.085, hw - 0.085):
        for sy in (-hl + 0.085, hl - 0.085):
            parts.append(vl.make_beveled_box(f"CornerPost_{sx}_{sy}", (sx, sy, H * 0.5), (0.17, 0.17, H - 0.36), bevel=0.01, material=m_olive))
            for sz in (0.095, H - 0.095):
                parts.append(vl.make_beveled_box(f"ISOCast_{sx}_{sy}_{sz}", (sx, sy, sz), (0.19, 0.19, 0.19), bevel=0.012, material=m_steel))
    # Corrugated vertical steel ribs along both long sides (X = ±hw)
    n_ribs = 22
    for i in range(n_ribs):
        ry = -hl + 0.30 + i * ((L - 0.60) / (n_ribs - 1))
        for sx in (-hw + 0.035, hw - 0.035):
            parts.append(vl.make_beveled_box(
                f"SideRib_{sx}_{i}", (sx, ry, H * 0.5), (0.07, 0.115, H - 0.34), bevel=0.015, material=m_olive
            ))
        # Roof transverse corrugation ribs
        parts.append(vl.make_beveled_box(
            f"RoofRib_{i}", (0.0, ry, H - 0.028), (W - 0.28, 0.12, 0.056), bevel=0.012, material=m_olive
        ))
    # Rear wall corrugation ribs (Y = -hl)
    for i in range(8):
        rx = -hw + 0.30 + i * ((W - 0.60) / 7.0)
        parts.append(vl.make_beveled_box(
            f"RearRib_{i}", (rx, -hl + 0.035, H * 0.5), (0.11, 0.07, H - 0.34), bevel=0.014, material=m_olive
        ))
    # Recessed forklift pockets on lower side rails
    for fx in (-hw + 0.03, hw - 0.03):
        for fy in (-0.95, 0.95):
            parts.append(vl.make_beveled_box(
                f"ForkPocket_{fx}_{fy}", (fx, fy, 0.09), (0.09, 0.36, 0.12), bevel=0.008, material=m_dark
            ))
    # Front Double Cargo Doors (Y = +hl) + Rubber center gasket + 4 Vertical Steel Locking Bars + Handles + Hinges
    for sx in (-0.55, 0.55):
        parts.append(vl.make_beveled_box(
            f"DoorPanel_{sx}", (sx, hl - 0.06, H * 0.5), (1.06, 0.06, H - 0.36), bevel=0.010, material=m_olive
        ))
        # Horizontal door stamping ribs
        for dz in (0.48, 0.96, 1.44, 1.92, 2.28):
            parts.append(vl.make_beveled_box(
                f"DoorRib_{sx}_{dz}", (sx, hl - 0.035, dz), (0.94, 0.04, 0.10), bevel=0.010, material=m_olive
            ))
    parts.append(vl.make_beveled_box("DoorCenterSeal", (0.0, hl - 0.03, H * 0.5), (0.045, 0.05, H - 0.36), bevel=0.004, material=m_dark))
    # 4 vertical galvanized cam-locking rods + handles
    for lx in (-0.78, -0.26, 0.26, 0.78):
        parts.append(vl.make_cylinder(f"LockBar_{lx}", (lx, hl + 0.015, H * 0.5), 0.022, 0.022, H - 0.24, segs=12, axis='Z', material=m_steel))
        parts.append(vl.make_beveled_box(f"LockHandle_{lx}", (lx + 0.08, hl + 0.045, 1.05), (0.22, 0.035, 0.045), bevel=0.006, material=m_steel))
        for bz in (0.30, 1.28, 2.25):
            parts.append(vl.make_beveled_box(f"BarBracket_{lx}_{bz}", (lx, hl - 0.005, bz), (0.08, 0.06, 0.06), bevel=0.006, material=m_steel))

    mesh_obj = vl.join_objects(parts, "ShippingContainer_Mesh", parent=root)
    vl.shade_hard_surface(mesh_obj, angle_deg=36.0, weighted_normals=True)
    vl.smart_uv_unwrap([mesh_obj], cube_size=2.8)

    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "props"), "ShippingContainer_Military",
        angles=[("hero", (2.15, 1.65, 0.75), 45.0, True)],
        res_hero=(1280, 720), samples_hero=48
    )
    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "props", "ShippingContainer_Military.fbx"),
        os.path.join(vl.SCENES_DIR, "ShippingContainer_Military.blend"),
    )
    meta["textures"] = {"Container_OliveSteel": t_olive, "Container_SteelHardware": t_steel}
    meta["renders"] = renders
    vl.update_manifest("props", "ShippingContainer_Military", meta)
    print(f"ShippingContainer done: {meta['triangles']} tris")


def build_concrete_barrier():
    print("\n=== Building ConcreteBarrier_Jersey ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "ConcreteBarrier")
    t_conc = vl.generate_pbr_texture_set(
        tex_dir, "Jersey_ConcreteHazard", preset="concrete_hazard", res=2048, seed=511
    )
    t_rebar = vl.generate_pbr_texture_set(
        tex_dir, "Jersey_RebarSteel", preset="radar_steel", res=512, seed=512,
        base_rgb=[0.24, 0.22, 0.20]
    )
    m_conc = vl.build_pbr_material("Jersey_ConcreteHazard", t_conc, uv_scale=0.9, normal_strength=1.4)
    m_rebar = vl.build_pbr_material("Jersey_RebarSteel", t_rebar, uv_scale=2.0, normal_strength=1.0)

    root = vl.create_empty("ConcreteBarrier_Jersey", (0, 0, 0), display_size=0.3)
    # Authentic Jersey barrier cross-section in XZ extruded along Y (2.40m long, 0.62m wide, 0.86m tall)
    prof_xz = [
        (-0.31, 0.00), (0.31, 0.00),
        (0.31, 0.09), (0.16, 0.30),
        (0.095, 0.84), (0.075, 0.86),
        (-0.075, 0.86), (-0.095, 0.84),
        (-0.16, 0.30), (-0.31, 0.09),
    ]
    parts = [
        vl.make_profile_extrude(
            "JerseyMain", prof_xz, 2.40, plane='XZ', center=(0.0, 0.0, 0.0),
            bevel=0.014, segments=2, material=m_conc
        ),
        # Forklift/drainage recessed slot block shadow under center
        vl.make_beveled_box("DrainSlot", (0.0, 0.0, 0.045), (0.64, 0.42, 0.09), bevel=0.008, material=m_rebar),
    ]
    # Twin rebar lifting loops on top of barrier
    for ly in (-0.68, 0.68):
        loop_pts = [
            (0.0, ly - 0.06, 0.82), (0.0, ly - 0.05, 0.92),
            (0.0, ly + 0.05, 0.92), (0.0, ly + 0.06, 0.82)
        ]
        loop_sec = [(-0.009, -0.009), (0.009, -0.009), (0.009, 0.009), (-0.009, 0.009)]
        parts.append(vl.make_curved_sweep(f"RebarLoop_{ly}", loop_pts, loop_sec, material=m_rebar))

    mesh_obj = vl.join_objects(parts, "ConcreteBarrier_Mesh", parent=root)
    vl.shade_hard_surface(mesh_obj, angle_deg=38.0, weighted_normals=True)
    vl.smart_uv_unwrap([mesh_obj], cube_size=1.2)

    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "props"), "ConcreteBarrier_Jersey",
        angles=[("hero", (2.15, 1.35, 0.82), 48.0, True)],
        res_hero=(1280, 720), samples_hero=48
    )
    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "props", "ConcreteBarrier_Jersey.fbx"),
        os.path.join(vl.SCENES_DIR, "ConcreteBarrier_Jersey.blend"),
    )
    meta["textures"] = {"Jersey_ConcreteHazard": t_conc, "Jersey_RebarSteel": t_rebar}
    meta["renders"] = renders
    vl.update_manifest("props", "ConcreteBarrier_Jersey", meta)
    print(f"ConcreteBarrier done: {meta['triangles']} tris")


def build_tactical_supply_crate():
    print("\n=== Building TacticalSupplyCrate ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "SupplyCrate")
    t_case = vl.generate_pbr_texture_set(
        tex_dir, "Crate_PelicanOD", preset="pelican_case", res=2048, seed=521,
        base_rgb=[0.16, 0.185, 0.135]
    )
    t_trim = vl.generate_pbr_texture_set(
        tex_dir, "Crate_DarkHardware", preset="gunmetal", res=1024, seed=522,
        base_rgb=[0.09, 0.095, 0.105]
    )
    t_led = vl.generate_pbr_texture_set(
        tex_dir, "Crate_StatusLED", preset="tritium_green", res=512, seed=523,
        accent_rgb=[0.12, 0.95, 0.72]
    )
    m_case = vl.build_pbr_material("Crate_PelicanOD", t_case, uv_scale=1.5, normal_strength=1.1)
    m_trim = vl.build_pbr_material("Crate_DarkHardware", t_trim, uv_scale=2.0, normal_strength=0.7)
    m_led = vl.build_pbr_material("Crate_StatusLED", t_led, uv_scale=1.0, emission_strength=6.5, normal_strength=0.1)

    root = vl.create_empty("TacticalSupplyCrate", (0, 0, 0), display_size=0.2)
    parts = [
        # Main lower tub + recessed gasket seam + heavy lid (0.88m x 0.54m x 0.38m)
        vl.make_beveled_box("CrateBase", (0.0, 0.0, 0.145), (0.84, 0.50, 0.25), bevel=0.018, segments=2, material=m_case),
        vl.make_beveled_box("CrateSeal", (0.0, 0.0, 0.275), (0.86, 0.52, 0.025), bevel=0.005, material=m_trim),
        vl.make_beveled_box("CrateLid", (0.0, 0.0, 0.330), (0.85, 0.51, 0.095), bevel=0.018, segments=2, material=m_case),
    ]
    # Raised structural reinforcement ribs across lid & body
    for rx in (-0.30, -0.15, 0.0, 0.15, 0.30):
        parts.append(vl.make_beveled_box(f"LidRib_{rx}", (rx, 0.0, 0.382), (0.045, 0.44, 0.022), bevel=0.005, material=m_case))
        parts.append(vl.make_beveled_box(f"SideRibF_{rx}", (rx, -0.258, 0.145), (0.042, 0.028, 0.22), bevel=0.005, material=m_case))
        parts.append(vl.make_beveled_box(f"SideRibB_{rx}", (rx, 0.258, 0.145), (0.042, 0.028, 0.22), bevel=0.005, material=m_case))
    # Corner rubber impact bumpers
    for sx in (-0.42, 0.42):
        for sy in (-0.25, 0.25):
            parts.append(vl.make_beveled_box(f"Bumper_{sx}_{sy}", (sx, sy, 0.19), (0.065, 0.065, 0.37), bevel=0.012, material=m_trim))
    # 4 Dual-throw front & rear latches + glowing cyan-green status LED display bar
    for lx in (-0.22, 0.22):
        parts.append(vl.make_beveled_box(f"LatchF_{lx}", (lx, -0.272, 0.255), (0.068, 0.032, 0.115), bevel=0.006, material=m_trim))
        parts.append(vl.make_beveled_box(f"HingeB_{lx}", (lx, 0.268, 0.275), (0.075, 0.032, 0.072), bevel=0.006, material=m_trim))
    # Tactical fold-down carry handles on sides & front
    for sx in (-0.445, 0.445):
        parts.append(vl.make_beveled_box(f"SideHandle_{sx}", (sx, 0.0, 0.185), (0.032, 0.18, 0.045), bevel=0.008, material=m_trim))
    parts.append(vl.make_beveled_box("FrontHandle", (0.0, -0.275, 0.145), (0.20, 0.035, 0.045), bevel=0.008, material=m_trim))
    # Purge valve + glowing tactical diagnostic LED panel
    parts.append(vl.make_cylinder("PurgeValve", (-0.09, -0.265, 0.275), 0.020, 0.020, 0.022, segs=16, axis='Y', material=m_trim))
    parts.append(vl.make_beveled_box("StatusHousing", (0.05, -0.265, 0.275), (0.11, 0.022, 0.038), bevel=0.004, material=m_trim))
    parts.append(vl.make_beveled_box("StatusLED", (0.05, -0.278, 0.275), (0.075, 0.006, 0.014), bevel=0.002, material=m_led))

    mesh_obj = vl.join_objects(parts, "TacticalSupplyCrate_Mesh", parent=root)
    vl.shade_hard_surface(mesh_obj, angle_deg=36.0, weighted_normals=True)
    vl.smart_uv_unwrap([mesh_obj], cube_size=0.6)

    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "props"), "TacticalSupplyCrate",
        angles=[("hero", (1.95, -1.75, 1.15), 50.0, True)],
        res_hero=(1280, 720), samples_hero=48
    )
    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "props", "TacticalSupplyCrate.fbx"),
        os.path.join(vl.SCENES_DIR, "TacticalSupplyCrate.blend"),
    )
    meta["textures"] = {"Crate_PelicanOD": t_case, "Crate_DarkHardware": t_trim, "Crate_StatusLED": t_led}
    meta["renders"] = renders
    vl.update_manifest("props", "TacticalSupplyCrate", meta)
    print(f"TacticalSupplyCrate done: {meta['triangles']} tris")


def build_radar_dish_tower():
    print("\n=== Building RadarDishTower ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "RadarTower")
    t_steel = vl.generate_pbr_texture_set(
        tex_dir, "Radar_Steel", preset="radar_steel", res=2048, seed=531,
        base_rgb=[0.30, 0.33, 0.35]
    )
    t_dish = vl.generate_pbr_texture_set(
        tex_dir, "Radar_DishWhite", preset="radar_steel", res=2048, seed=532,
        base_rgb=[0.56, 0.58, 0.57]
    )
    t_conc = vl.generate_pbr_texture_set(
        tex_dir, "Radar_ConcreteBase", preset="concrete_weathered", res=1024, seed=533
    )
    t_beacon = vl.generate_pbr_texture_set(
        tex_dir, "Radar_BeaconRed", preset="tritium_green", res=512, seed=534,
        accent_rgb=[1.0, 0.10, 0.05]
    )

    m_steel = vl.build_pbr_material("Radar_Steel", t_steel, uv_scale=1.2, normal_strength=1.0)
    m_dish = vl.build_pbr_material("Radar_DishWhite", t_dish, uv_scale=1.2, normal_strength=0.8)
    m_conc = vl.build_pbr_material("Radar_ConcreteBase", t_conc, uv_scale=1.0, normal_strength=1.4)
    m_beacon = vl.build_pbr_material("Radar_BeaconRed", t_beacon, uv_scale=1.0, emission_strength=9.0, normal_strength=0.1)

    root = vl.create_empty("RadarDishTower", (0, 0, 0), display_size=0.8)
    parts = []

    # 1. Reinforced octagonal concrete foundation plinth (Z: 0..0.45m) + equipment junction cabinet
    parts.append(vl.make_cylinder("FoundationBase", (0.0, 0.0, 0.225), 2.15, 1.95, 0.45, segs=8, axis='Z', bevel=0.04, material=m_conc))
    parts.append(vl.make_beveled_box("ControlBox", (1.25, 0.0, 1.05), (0.55, 0.72, 1.25), bevel=0.02, material=m_steel))

    # 2. Tapered 4-Legged Open Lattice Steel Truss Tower (Z: 0.42 .. 6.20m)
    levels = [
        (0.42, 1.25),
        (1.86, 1.04),
        (3.30, 0.85),
        (4.74, 0.68),
        (6.18, 0.55),
    ]
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for li in range(len(levels) - 1):
        z0, r0 = levels[li]
        z1, r1 = levels[li + 1]
        pts0 = [(cx * r0, cy * r0, z0) for cx, cy in corners]
        pts1 = [(cx * r1, cy * r1, z1) for cx, cy in corners]
        for ci in range(4):
            cj = (ci + 1) % 4
            # Main corner leg
            parts.append(make_strut(f"Leg_{li}_{ci}", pts0[ci], pts1[ci], radius=0.065, segs=10, material=m_steel))
            # Horizontal girt
            parts.append(make_strut(f"Horiz_{li}_{ci}", pts1[ci], pts1[cj], radius=0.036, segs=8, material=m_steel))
            # X-bracing diagonals!
            parts.append(make_strut(f"DiagA_{li}_{ci}", pts0[ci], pts1[cj], radius=0.026, segs=8, material=m_steel))
            parts.append(make_strut(f"DiagB_{li}_{ci}", pts0[cj], pts1[ci], radius=0.026, segs=8, material=m_steel))

    # 3. Upper Circular Maintenance Deck & Safety Handrails (Z: 6.20 .. 7.25m)
    parts.append(vl.make_cylinder("MaintPlatform", (0.0, 0.0, 6.24), 1.38, 1.38, 0.12, segs=16, axis='Z', bevel=0.015, material=m_steel))
    for k in range(12):
        a0 = 2.0 * math.pi * k / 12
        a1 = 2.0 * math.pi * ((k + 1) % 12) / 12
        r = 1.32
        p_bot = (r * math.cos(a0), r * math.sin(a0), 6.28)
        p_top = (r * math.cos(a0), r * math.sin(a0), 7.28)
        p_top_next = (r * math.cos(a1), r * math.sin(a1), 7.28)
        p_mid = (r * math.cos(a0), r * math.sin(a0), 6.78)
        p_mid_next = (r * math.cos(a1), r * math.sin(a1), 6.78)
        parts.append(make_strut(f"RailPost_{k}", p_bot, p_top, radius=0.024, segs=8, material=m_steel))
        parts.append(make_strut(f"RailTop_{k}", p_top, p_top_next, radius=0.022, segs=8, material=m_steel))
        parts.append(make_strut(f"RailMid_{k}", p_mid, p_mid_next, radius=0.018, segs=8, material=m_steel))

    # Vertical Access Ladder along +Y face
    for lx in (-0.22, 0.22):
        parts.append(make_strut(f"LadderRail_{lx}", (lx, 1.15, 0.42), (lx, 0.65, 6.25), radius=0.022, segs=8, material=m_steel))
    for r_idx in range(18):
        t = r_idx / 17.0
        rz = 0.60 + t * 5.45
        ry = 1.15 - t * 0.50
        parts.append(make_strut(f"LadderRung_{r_idx}", (-0.22, ry, rz), (0.22, ry, rz), radius=0.014, segs=8, material=m_steel))

    # 4. Motorized Azimuth/Elevation Pedestal (Z: 6.30 .. 8.35m — clears maintenance railings cleanly!)
    parts.append(vl.make_cylinder("AzimuthDrum", (0.0, 0.0, 7.15), 0.52, 0.45, 1.70, segs=20, axis='Z', bevel=0.03, material=m_steel))
    parts.append(vl.make_beveled_box("ElevYokeL", (-0.42, 0.0, 8.20), (0.18, 0.46, 0.65), bevel=0.02, material=m_steel))
    parts.append(vl.make_beveled_box("ElevYokeR", (0.42, 0.0, 8.20), (0.18, 0.46, 0.65), bevel=0.02, material=m_steel))
    parts.append(vl.make_cylinder("ElevAxle", (0.0, 0.0, 8.35), 0.14, 0.14, 1.12, segs=16, axis='X', bevel=0.015, material=m_steel))

    # 5. HERO PARABOLIC RADAR DISH (4.0m diameter, tilted upward 32 deg toward +Y!)
    # Build dish in local space around origin facing +Z, then rotate & position at (0, 0, 7.95)
    dish_parts = []
    dish_r = 2.05
    # Lathe concave parabolic reflector with thickened outer rim & rear hub
    parab_prof = []
    steps = 12
    for i in range(steps + 1):
        t = i / steps
        r = 0.02 + t * dish_r
        z = 0.18 * (t ** 2) * 3.4  # concave dish depth ~0.61m
        parab_prof.append((r, z))
    # Outer rolled rim + back surface return
    parab_prof.append((dish_r + 0.05, 0.63))
    parab_prof.append((dish_r + 0.05, 0.54))
    for i in range(steps, -1, -1):
        t = i / steps
        r = 0.02 + t * dish_r
        z = 0.18 * (t ** 2) * 3.4 - 0.07
        parab_prof.append((r, z))
    dish_parts.append(vl.make_lathe("DishReflector", parab_prof, segs=36, axis='Z', cap_start=False, cap_end=False, material=m_dish))
    # Structural rear backing ring & radial ribs
    dish_parts.append(vl.make_cylinder("DishBackHub", (0.0, 0.0, -0.22), 0.58, 0.48, 0.36, segs=18, axis='Z', bevel=0.02, material=m_steel))
    for k in range(8):
        a = k * math.pi / 4.0
        p0 = (0.35 * math.cos(a), 0.35 * math.sin(a), -0.18)
        p1 = (1.75 * math.cos(a), 1.75 * math.sin(a), 0.34)
        dish_parts.append(make_strut(f"DishRib_{k}", p0, p1, radius=0.042, segs=8, material=m_steel))
    # Quad RF feedhorn support struts + central feedhorn receiver pod
    for k in range(4):
        a = k * math.pi / 2.0 + math.pi / 4.0
        p0 = (1.45 * math.cos(a), 1.45 * math.sin(a), 0.30)
        p1 = (0.14 * math.cos(a), 0.14 * math.sin(a), 1.68)
        dish_parts.append(make_strut(f"FeedStrut_{k}", p0, p1, radius=0.028, segs=8, material=m_steel))
    dish_parts.append(vl.make_cylinder("FeedHornPod", (0.0, 0.0, 1.66), 0.22, 0.15, 0.44, segs=18, axis='Z', bevel=0.02, material=m_steel))
    # Glowing red aviation warning beacon atop feedhorn & dish rim
    dish_parts.append(vl.make_cylinder("BeaconTop", (0.0, 0.0, 1.94), 0.08, 0.06, 0.14, segs=14, axis='Z', material=m_beacon))

    dish_asm = vl.join_objects(dish_parts, "RadarDishAssembly", parent=root)
    dish_asm.location = Vector((0.0, 0.18, 8.50))
    dish_asm.rotation_euler = Euler((math.radians(-58.0), 0.0, 0.0), 'XYZ')
    vl.shade_hard_surface(dish_asm, angle_deg=38.0, weighted_normals=True)

    tower_mesh = vl.join_objects(parts, "TowerStructure", parent=root)
    vl.shade_hard_surface(tower_mesh, angle_deg=38.0, weighted_normals=True)
    vl.smart_uv_unwrap([tower_mesh, dish_asm], cube_size=2.5)

    # Anchor for optional slow radar rotation script in Unity!
    vl.create_empty("DishPivot", (0.0, 0.18, 8.50), parent=root)

    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "props"), "RadarDishTower",
        angles=[("hero", (2.25, 1.85, 0.45), 42.0, True)],
        res_hero=(1280, 960), samples_hero=48
    )
    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "props", "RadarDishTower.fbx"),
        os.path.join(vl.SCENES_DIR, "RadarDishTower.blend"),
    )
    meta["textures"] = {"Radar_Steel": t_steel, "Radar_DishWhite": t_dish, "Radar_ConcreteBase": t_conc, "Radar_BeaconRed": t_beacon}
    meta["renders"] = renders
    vl.update_manifest("props", "RadarDishTower", meta)
    print(f"RadarDishTower done: {meta['triangles']} tris")


def build_floodlight_tower():
    print("\n=== Building FloodlightTower ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "Floodlight")
    t_steel = vl.generate_pbr_texture_set(
        tex_dir, "Floodlight_Steel", preset="gunmetal", res=1024, seed=541,
        base_rgb=[0.13, 0.145, 0.155]
    )
    t_hazard = vl.generate_pbr_texture_set(
        tex_dir, "Floodlight_Yellow", preset="pelican_case", res=1024, seed=542,
        base_rgb=[0.74, 0.54, 0.08]
    )
    t_lens = vl.generate_pbr_texture_set(
        tex_dir, "Floodlight_LED", preset="tritium_green", res=512, seed=543,
        accent_rgb=[1.0, 0.92, 0.78]
    )
    m_steel = vl.build_pbr_material("Floodlight_Steel", t_steel, uv_scale=1.8, normal_strength=0.7)
    m_yellow = vl.build_pbr_material("Floodlight_Yellow", t_hazard, uv_scale=1.5, normal_strength=0.8)
    m_lens = vl.build_pbr_material("Floodlight_LED", t_lens, uv_scale=1.0, emission_strength=12.0, normal_strength=0.1)

    root = vl.create_empty("FloodlightTower", (0, 0, 0), display_size=0.3)
    parts = [
        # Heavy tactical portable power generator / weighted stanchion base (Z: 0..0.52m)
        vl.make_beveled_box("GenBase", (0.0, 0.0, 0.26), (0.62, 0.78, 0.44), bevel=0.025, material=m_steel),
        vl.make_beveled_box("GenBumperTop", (0.0, 0.0, 0.49), (0.66, 0.82, 0.045), bevel=0.012, material=m_yellow),
        # 4 Stabilizer outrigger legs with ground pads
        make_strut("Outrig1", (0.28, 0.32, 0.22), (0.68, 0.68, 0.04), radius=0.025, segs=8, material=m_steel),
        make_strut("Outrig2", (-0.28, 0.32, 0.22), (-0.68, 0.68, 0.04), radius=0.025, segs=8, material=m_steel),
        make_strut("Outrig3", (0.28, -0.32, 0.22), (0.68, -0.68, 0.04), radius=0.025, segs=8, material=m_steel),
        make_strut("Outrig4", (-0.28, -0.32, 0.22), (-0.68, -0.68, 0.04), radius=0.025, segs=8, material=m_steel),
        # Telescoping 3-stage steel mast (Z: 0.48 .. 2.72m)
        vl.make_cylinder("MastLower", (0.0, 0.0, 1.15), 0.048, 0.048, 1.35, segs=16, axis='Z', material=m_steel),
        vl.make_cylinder("MastCollar1", (0.0, 0.0, 1.82), 0.062, 0.062, 0.09, segs=16, axis='Z', bevel=0.008, material=m_yellow),
        vl.make_cylinder("MastUpper", (0.0, 0.0, 2.26), 0.036, 0.036, 0.92, segs=16, axis='Z', material=m_steel),
        # Top T-crossbar
        vl.make_beveled_box("CrossBar", (0.0, 0.0, 2.70), (0.96, 0.065, 0.065), bevel=0.01, material=m_yellow),
    ]
    for sx, sy in ((0.68, 0.68), (-0.68, 0.68), (0.68, -0.68), (-0.68, -0.68)):
        parts.append(vl.make_beveled_box(f"FootPad_{sx}_{sy}", (sx, sy, 0.02), (0.14, 0.14, 0.04), bevel=0.006, material=m_yellow))

    # Dual Armored LED Floodlight Heads tilted downward -18 deg toward +Y!
    for hx in (-0.32, 0.32):
        # Mounting yoke bracket
        parts.append(vl.make_beveled_box(f"Yoke_{hx}", (hx, 0.0, 2.79), (0.36, 0.04, 0.16), bevel=0.008, material=m_steel))
        # Armored finned LED lamp housing
        parts.append(vl.make_beveled_box(
            f"LampBody_{hx}", (hx, 0.03, 2.86), (0.34, 0.16, 0.24),
            rot=(-0.30, 0, 0), bevel=0.014, material=m_steel
        ))
        # Rear aluminum heatsink cooling fins
        for fi in range(6):
            fz = 2.77 + fi * 0.035
            parts.append(vl.make_beveled_box(
                f"Fin_{hx}_{fi}", (hx, -0.06, fz), (0.30, 0.035, 0.012),
                rot=(-0.30, 0, 0), bevel=0.002, material=m_steel
            ))
        # Glowing Emissive LED Lens Panel facing +Y!
        parts.append(vl.make_beveled_box(
            f"LampLens_{hx}", (hx, 0.112, 2.84), (0.29, 0.018, 0.19),
            rot=(-0.30, 0, 0), bevel=0.004, material=m_lens
        ))

    mesh_obj = vl.join_objects(parts, "FloodlightTower_Mesh", parent=root)
    vl.shade_hard_surface(mesh_obj, angle_deg=36.0, weighted_normals=True)
    vl.smart_uv_unwrap([mesh_obj], cube_size=1.0)
    vl.create_empty("LightOrigin", (0.0, 0.18, 2.82), parent=root)

    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "props"), "FloodlightTower",
        angles=[("hero", (2.15, 1.75, 0.55), 46.0, True)],
        res_hero=(1280, 960), samples_hero=48
    )
    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "props", "FloodlightTower.fbx"),
        os.path.join(vl.SCENES_DIR, "FloodlightTower.blend"),
    )
    meta["textures"] = {"Floodlight_Steel": t_steel, "Floodlight_Yellow": t_hazard, "Floodlight_LED": t_lens}
    meta["renders"] = renders
    vl.update_manifest("props", "FloodlightTower", meta)
    print(f"FloodlightTower done: {meta['triangles']} tris")


def build_server_rack_terminal():
    print("\n=== Building ServerRack_Terminal ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "ServerRack")
    t_rack = vl.generate_pbr_texture_set(
        tex_dir, "Server_Anodized", preset="server_rack", res=2048, seed=551
    )
    t_steel = vl.generate_pbr_texture_set(
        tex_dir, "Server_TrimSteel", preset="chrome_steel", res=1024, seed=552,
        base_rgb=[0.42, 0.45, 0.48]
    )
    t_panel = vl.generate_pbr_texture_set(
        tex_dir, "Server_EmissivePanel", preset="emissive_panel", res=1024, seed=553
    )
    m_rack = vl.build_pbr_material("Server_Anodized", t_rack, uv_scale=1.5, normal_strength=0.9)
    m_steel = vl.build_pbr_material("Server_TrimSteel", t_steel, uv_scale=1.5, normal_strength=0.6)
    m_panel = vl.build_pbr_material("Server_EmissivePanel", t_panel, uv_scale=1.0, emission_strength=4.5, normal_strength=0.1)

    root = vl.create_empty("ServerRack_Terminal", (0, 0, 0), display_size=0.3)
    parts = [
        # Main 42U Blacksite Server Cabinet (0.82m W x 0.92m D x 2.15m H, front facing +Y)
        vl.make_beveled_box("CabinetBody", (0.0, 0.0, 1.09), (0.82, 0.90, 2.08), bevel=0.016, material=m_rack),
        # Top cooling exhaust plenum
        vl.make_beveled_box("TopPlenum", (0.0, 0.0, 2.16), (0.74, 0.80, 0.09), bevel=0.012, material=m_rack),
        # Front vertical steel rack bezel posts
        vl.make_beveled_box("FrontPostL", (-0.38, 0.455, 1.09), (0.06, 0.04, 2.04), bevel=0.008, material=m_steel),
        vl.make_beveled_box("FrontPostR", (0.38, 0.455, 1.09), (0.06, 0.04, 2.04), bevel=0.008, material=m_steel),
    ]
    # 4 heavy leveling feet
    for sx in (-0.34, 0.34):
        for sy in (-0.38, 0.38):
            parts.append(vl.make_cylinder(f"Foot_{sx}_{sy}", (sx, sy, 0.025), 0.04, 0.035, 0.05, segs=12, axis='Z', material=m_steel))
    # Stacked Rackmount Blade Servers + Glowing Status LED Strips on Front (+Y)
    for i in range(10):
        bz = 0.22 + i * 0.185
        if abs(bz - 1.15) < 0.18:
            continue  # Leave space for tactical terminal console shelf!
        parts.append(vl.make_beveled_box(f"Blade_{i}", (0.0, 0.462, bz), (0.68, 0.035, 0.155), bevel=0.006, material=m_rack))
        parts.append(vl.make_beveled_box(f"BladeHandleL_{i}", (-0.31, 0.488, bz), (0.022, 0.028, 0.11), bevel=0.004, material=m_steel))
        parts.append(vl.make_beveled_box(f"BladeHandleR_{i}", (0.31, 0.488, bz), (0.022, 0.028, 0.11), bevel=0.004, material=m_steel))
        parts.append(vl.make_beveled_box(f"BladeLEDs_{i}", (0.0, 0.482, bz + 0.042), (0.52, 0.008, 0.032), bevel=0.002, material=m_panel))

    # Pull-out Tactical Operations Console Shelf + Keyboard + Glowing Angled Monitor!
    parts.append(vl.make_beveled_box("ConsoleShelf", (0.0, 0.64, 1.06), (0.70, 0.38, 0.04), bevel=0.008, material=m_rack))
    parts.append(vl.make_beveled_box("TacticalKeyboard", (0.0, 0.68, 1.095), (0.48, 0.20, 0.025), rot=(-0.10, 0, 0), bevel=0.005, material=m_steel))
    parts.append(vl.make_beveled_box("MonitorHousing", (0.0, 0.50, 1.32), (0.62, 0.06, 0.36), rot=(-0.18, 0, 0), bevel=0.012, material=m_rack))
    parts.append(vl.make_beveled_box("MonitorScreen", (0.0, 0.532, 1.325), (0.54, 0.012, 0.29), rot=(-0.18, 0, 0), bevel=0.003, material=m_panel))

    mesh_obj = vl.join_objects(parts, "ServerRack_Mesh", parent=root)
    vl.shade_hard_surface(mesh_obj, angle_deg=36.0, weighted_normals=True)
    vl.smart_uv_unwrap([mesh_obj], cube_size=1.0)

    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "props"), "ServerRack_Terminal",
        angles=[("hero", (1.95, 2.05, 0.55), 48.0, True)],
        res_hero=(1280, 960), samples_hero=48
    )
    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "props", "ServerRack_Terminal.fbx"),
        os.path.join(vl.SCENES_DIR, "ServerRack_Terminal.blend"),
    )
    meta["textures"] = {"Server_Anodized": t_rack, "Server_TrimSteel": t_steel, "Server_EmissivePanel": t_panel}
    meta["renders"] = renders
    vl.update_manifest("props", "ServerRack_Terminal", meta)
    print(f"ServerRack_Terminal done: {meta['triangles']} tris")


def build_sandbag_wall():
    print("\n=== Building SandbagWall ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "SandbagWall")
    t_burlap = vl.generate_pbr_texture_set(
        tex_dir, "Sandbag_Burlap", preset="burlap_sandbag", res=2048, seed=561
    )
    m_burlap = vl.build_pbr_material("Sandbag_Burlap", t_burlap, uv_scale=1.8, normal_strength=1.5)

    root = vl.create_empty("SandbagWall", (0, 0, 0), display_size=0.3)
    rng = random.Random(77)

    def make_single_sandbag(name, loc, rot_z=0.0, scale_xyz=(0.54, 0.30, 0.165)):
        """Sculpt an organic plump burlap sandbag with pinched tied ends & subtle sag."""
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=3, radius=0.5)
        sx, sy, sz = scale_xyz
        for v in bm.verts:
            x, y, z = v.co.x * 2.0, v.co.y * 2.0, v.co.z * 2.0  # -1..1
            # Flatten bottom slightly & puff middle, pinch ends along X!
            pinch = 1.0 - 0.52 * (abs(x) ** 2.4)
            z_flat = z * (0.72 if z < 0 else 1.0) * pinch
            y_puff = y * (0.85 + 0.18 * (1.0 - x * x)) * (0.88 + 0.12 * math.cos(x * 3.0))
            # Subtle ear corners at pinched ends
            ear = 0.08 * (abs(x) ** 3) * (abs(y) ** 1.5)
            # Small deterministic organic fabric lumpiness
            bump = 1.0 + 0.05 * math.sin(x * 7.0 + y * 5.0) * math.cos(z * 6.0)
            v.co = Vector((
                x * sx * 0.5 * bump,
                (y_puff + (ear if y > 0 else -ear)) * sy * 0.5 * bump,
                (z_flat + 0.06) * sz * 0.5 * bump
            ))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.rotate(bm, matrix=Euler((rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04), rot_z), 'XYZ').to_matrix(), verts=bm.verts)
        bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
        return vl.create_mesh_object(name, bm, material=m_burlap)

    parts = []
    # 5 running-bond double-thick bottom -> tapered top tactical sandbag courses (2.25m wide x 1.02m tall)
    course_specs = [
        (0.085, 5, [-0.14, 0.14], 0.0),
        (0.240, 4, [-0.13, 0.13], 0.22),
        (0.395, 5, [-0.12, 0.12], 0.0),
        (0.550, 4, [-0.10, 0.10], 0.22),
        (0.705, 4, [0.0], 0.0),
        (0.855, 3, [0.0], 0.22),
    ]
    bag_i = 0
    for cz, n_bags, y_rows, x_shift in course_specs:
        span = (n_bags - 1) * 0.45
        for ry in y_rows:
            for bi in range(n_bags):
                bx = -span * 0.5 + bi * 0.45 + rng.uniform(-0.02, 0.02)
                by = ry + rng.uniform(-0.02, 0.02)
                # Slight tactical curve (gentle U-arc shielding shooter!)
                by -= 0.06 * (bx ** 2)
                yaw = -bx * 0.12 + rng.uniform(-0.07, 0.07)
                parts.append(make_single_sandbag(
                    f"Bag_{bag_i}", (bx, by, cz), rot_z=yaw,
                    scale_xyz=(0.50 + rng.uniform(-0.02, 0.03), 0.30, 0.175)
                ))
                bag_i += 1

    mesh_obj = vl.join_objects(parts, "SandbagWall_Mesh", parent=root)
    # Smooth shading across organic sandbags
    vl.shade_hard_surface(mesh_obj, angle_deg=75.0, weighted_normals=False)
    vl.smart_uv_unwrap([mesh_obj], cube_size=0.65)

    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "props"), "SandbagWall",
        angles=[("hero", (1.95, 1.55, 0.95), 48.0, True)],
        res_hero=(1280, 720), samples_hero=48
    )
    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "props", "SandbagWall.fbx"),
        os.path.join(vl.SCENES_DIR, "SandbagWall.blend"),
    )
    meta["textures"] = {"Sandbag_Burlap": t_burlap}
    meta["renders"] = renders
    vl.update_manifest("props", "SandbagWall", meta)
    print(f"SandbagWall done: {meta['triangles']} tris")


if __name__ == "__main__":
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    builders = {
        "container": build_shipping_container,
        "barrier": build_concrete_barrier,
        "crate": build_tactical_supply_crate,
        "radar": build_radar_dish_tower,
        "floodlight": build_floodlight_tower,
        "server": build_server_rack_terminal,
        "sandbag": build_sandbag_wall,
    }
    for key, fn in builders.items():
        if not only or key in only:
            fn()
    print("\n=== ALL ENVIRONMENT PROPS BUILD COMPLETE ===")

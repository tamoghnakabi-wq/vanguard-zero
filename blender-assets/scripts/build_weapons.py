"""
VANGUARD ZERO — Batch 1: AAA Hero Weapons Builder
Generates:
  1. VX47_AssaultRifle.fbx (+ .blend, 2K PBR maps, studio renders)
  2. M890_TacticalShotgun.fbx (+ .blend, 2K PBR maps, studio renders)
  3. M19_TacticalPistol.fbx (+ .blend, 2K PBR maps, studio renders)
"""

import os
import sys
import math
import bpy
import bmesh
from mathutils import Vector, Matrix, Euler

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

import vanguard_lib as vl


def make_optic_glass_quad(name, center, width, height, material):
    """Create a clean double-sided glass pane facing -Y/+Y with 0..1 UVs for holographic reticle."""
    bm = bmesh.new()
    hw, hh = width * 0.5, height * 0.5
    cx, cy, cz = center
    v0 = bm.verts.new((cx - hw, cy, cz - hh))
    v1 = bm.verts.new((cx + hw, cy, cz - hh))
    v2 = bm.verts.new((cx + hw, cy, cz + hh))
    v3 = bm.verts.new((cx - hw, cy, cz + hh))
    f = bm.faces.new((v0, v1, v2, v3))
    uv_layer = bm.loops.layers.uv.new("UVMap")
    uvs = [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]
    for loop, uv in zip(f.loops, uvs):
        loop[uv_layer].uv = uv
    # Also add slight thickness (1mm) so visible from both sides cleanly
    ret = bmesh.ops.extrude_face_region(bm, geom=[f])
    verts_ext = [g for g in ret['geom'] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=Vector((0.0, 0.0012, 0.0)), verts=verts_ext)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return vl.create_mesh_object(name, bm, material=material)


def build_vx47():
    print("\n=== Building VX47_AssaultRifle ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "VX47")

    # Generate 2K Unity-URP PBR texture sets
    t_gunmetal = vl.generate_pbr_texture_set(
        tex_dir, "VX47_Gunmetal", preset="gunmetal", res=2048, seed=101,
        base_rgb=[0.115, 0.125, 0.14]
    )
    t_fde = vl.generate_pbr_texture_set(
        tex_dir, "VX47_PolymerFDE", preset="polymer_fde", res=2048, seed=102,
        base_rgb=[0.28, 0.235, 0.175]
    )
    t_poly_dark = vl.generate_pbr_texture_set(
        tex_dir, "VX47_PolymerDark", preset="polymer_dark", res=2048, seed=103,
        base_rgb=[0.06, 0.064, 0.072]
    )
    t_steel = vl.generate_pbr_texture_set(
        tex_dir, "VX47_BoltSteel", preset="chrome_steel", res=1024, seed=104,
        base_rgb=[0.52, 0.54, 0.57]
    )
    t_reticle = vl.generate_pbr_texture_set(
        tex_dir, "VX47_OpticReticle", preset="optic_reticle", res=1024, seed=105,
        accent_rgb=[1.0, 0.12, 0.05]
    )

    m_gun = vl.build_pbr_material("VX47_Gunmetal", t_gunmetal, uv_scale=1.6, normal_strength=0.45)
    m_fde = vl.build_pbr_material("VX47_PolymerFDE", t_fde, uv_scale=1.8, normal_strength=0.75)
    m_dark = vl.build_pbr_material("VX47_PolymerDark", t_poly_dark, uv_scale=2.2, normal_strength=0.9)
    m_steel = vl.build_pbr_material("VX47_BoltSteel", t_steel, uv_scale=1.6, normal_strength=0.45)
    m_reticle = vl.build_pbr_material(
        "VX47_OpticReticle", t_reticle, uv_scale=1.0, emission_strength=6.5, alpha=0.38, normal_strength=0.1
    )

    root = vl.create_empty("VX47_AssaultRifle", (0, 0, 0), display_size=0.10)
    recv_parts = []

    # 1. LOWER RECEIVER (sculpted FDE tactical receiver + flared magwell + trigger guard)
    lower_prof = [
        (-0.048, 0.008), (0.128, 0.008), (0.128, -0.044),
        (0.056, -0.044), (0.046, -0.022), (-0.018, -0.022),
        (-0.048, -0.036),
    ]
    recv_parts.append(vl.make_profile_extrude(
        "LowerBody", lower_prof, 0.034, plane='YZ', bevel=0.0022, segments=2, material=m_fde
    ))
    # Flared beveled magwell collar
    magwell_prof = [
        (0.052, -0.028), (0.132, -0.028), (0.135, -0.050),
        (0.048, -0.050)
    ]
    recv_parts.append(vl.make_profile_extrude(
        "MagwellFlare", magwell_prof, 0.040, plane='YZ', bevel=0.002, segments=2, material=m_fde
    ))
    # Trigger guard bow
    tg_pts = [
        (0.0, -0.018, -0.026), (0.0, -0.010, -0.054),
        (0.0, 0.038, -0.054), (0.0, 0.055, -0.032)
    ]
    tg_sec = [(-0.0055, -0.003), (0.0055, -0.003), (0.0055, 0.003), (-0.0055, 0.003)]
    recv_parts.append(vl.make_curved_sweep("TriggerGuard", tg_pts, tg_sec, material=m_dark))
    # Takedown pins, fire selector, mag release, bolt catch
    for py, pz in [(-0.034, -0.002), (0.116, -0.002)]:
        recv_parts.append(vl.make_cylinder(
            f"Pin_{py}", (0.0, py, pz), 0.0042, 0.0042, 0.039, segs=16, axis='X', bevel=0.0007, material=m_dark
        ))
    # Fire selector levers (both sides)
    for sx in (-0.020, 0.020):
        recv_parts.append(vl.make_beveled_box(
            f"Selector_{sx}", (sx, -0.014, -0.008), (0.006, 0.018, 0.006),
            rot=(0.35, 0, 0), bevel=0.001, material=m_dark
        ))
    # Mag release button (right) & bolt catch paddle (left)
    recv_parts.append(vl.make_beveled_box(
        "MagRelease", (0.019, 0.072, -0.014), (0.005, 0.015, 0.009), bevel=0.001, material=m_dark
    ))
    recv_parts.append(vl.make_beveled_box(
        "BoltCatch", (-0.019, 0.068, 0.002), (0.005, 0.012, 0.020), bevel=0.001, material=m_dark
    ))

    # 2. UPPER RECEIVER (forged chamfered gunmetal upper + brass deflector + forward assist)
    upper_xz = [
        (-0.0185, 0.007), (0.0185, 0.007),
        (0.0205, 0.024), (0.0185, 0.044),
        (0.0125, 0.054), (-0.0125, 0.054),
        (-0.0185, 0.044), (-0.0205, 0.024),
    ]
    recv_parts.append(vl.make_profile_extrude(
        "UpperBody", upper_xz, 0.194, plane='XZ', center=(0.0, 0.044, 0.0),
        bevel=0.0018, segments=2, material=m_gun
    ))
    # Recessed ejection port frame (dark backing plate inside right side + dust cover below)
    recv_parts.append(vl.make_beveled_box(
        "EjectPortCavity", (0.0175, 0.056, 0.030), (0.008, 0.072, 0.017), bevel=0.0008, material=m_dark
    ))
    # Hinged dust cover flipped down
    recv_parts.append(vl.make_beveled_box(
        "DustCover", (0.0215, 0.056, 0.014), (0.0025, 0.070, 0.013),
        rot=(0.0, 0.12, 0.0), bevel=0.0006, material=m_gun
    ))
    # Brass deflector wedge behind ejection port
    recv_parts.append(vl.make_beveled_box(
        "BrassDeflector", (0.023, 0.007, 0.030), (0.011, 0.015, 0.018),
        rot=(0.0, 0.0, -0.35), bevel=0.0018, material=m_gun
    ))
    # Forward assist housing & plunger
    recv_parts.append(vl.make_cylinder(
        "FwdAssist", (0.021, -0.022, 0.022), 0.007, 0.006, 0.034, segs=16, axis='Y',
        rot=(0.0, 0.0, -0.30), bevel=0.001, material=m_gun
    ))
    # Rear T-charging handle latch
    recv_parts.append(vl.make_beveled_box(
        "ChargingHandle", (0.0, -0.056, 0.044), (0.044, 0.014, 0.009), bevel=0.0015, material=m_dark
    ))

    # 3. FULL-LENGTH TOP PICATINNY RAIL
    recv_parts.append(vl.make_picatinny_rail(
        "TopRail", y_start=-0.048, y_end=0.398, z_base=0.0535, width=0.0212, height=0.0085,
        side='TOP', material=m_gun
    ))

    # 4. OCTAGONAL M-LOK TACTICAL HANDGUARD + RAIL SECTIONS + LASER MODULE
    hg_xz = [
        (-0.013, 0.004), (0.013, 0.004),
        (0.023, 0.014), (0.023, 0.041),
        (0.014, 0.0525), (-0.014, 0.0525),
        (-0.023, 0.041), (-0.023, 0.014),
    ]
    recv_parts.append(vl.make_profile_extrude(
        "HandguardShell", hg_xz, 0.260, plane='XZ', center=(0.0, 0.270, 0.0),
        bevel=0.0018, segments=2, material=m_gun
    ))
    # Handguard mounting barrel-nut collar with Allen bolts
    recv_parts.append(vl.make_beveled_box(
        "HandguardCollar", (0.0, 0.156, 0.026), (0.048, 0.034, 0.050), bevel=0.0022, material=m_gun
    ))
    for bz in (0.012, 0.036):
        recv_parts.append(vl.make_cylinder(
            f"CollarBolt_{bz}", (0.0, 0.156, bz), 0.0036, 0.0036, 0.051, segs=12, axis='X', bevel=0.0005, material=m_steel
        ))
    # Side M-LOK ventilation slot dark insets + raised grip ridges (left & right, upper & lower)
    for sx in (-1.0, 1.0):
        for i in range(6):
            sy = 0.188 + i * 0.034
            # Recessed dark M-LOK vent slot
            recv_parts.append(vl.make_beveled_box(
                f"MLOK_{sx}_{i}", (sx * 0.0228, sy, 0.028), (0.003, 0.024, 0.0075),
                bevel=0.0008, material=m_dark
            ))
            # Diagonal upper cooling slot
            recv_parts.append(vl.make_beveled_box(
                f"VentUp_{sx}_{i}", (sx * 0.0182, sy + 0.010, 0.044), (0.003, 0.018, 0.005),
                rot=(0.0, sx * 0.72, 0.0), bevel=0.0006, material=m_dark
            ))
    # Side rubber rail covers (FDE panels for crisp visual breakup!)
    for sx in (-1.0, 1.0):
        recv_parts.append(vl.make_beveled_box(
            f"RailPanel_{sx}", (sx * 0.0242, 0.275, 0.028), (0.004, 0.115, 0.017),
            bevel=0.0014, material=m_fde
        ))
    # Bottom rail + short front side rails
    recv_parts.append(vl.make_picatinny_rail(
        "BottomRail", y_start=0.175, y_end=0.390, z_base=0.004, width=0.020, height=0.007,
        side='BOTTOM', material=m_gun
    ))
    for side_name, sx in [("RightRail", 0.023), ("LeftRail", -0.023)]:
        recv_parts.append(vl.make_picatinny_rail(
            side_name, y_start=0.335, y_end=0.392, z_base=0.028, width=0.018, height=0.006,
            side='RIGHT' if sx > 0 else 'LEFT', x_offset=sx, material=m_gun
        ))

    # PEQ-15 Tactical Laser/IR Module mounted on top-front rail (offsets slightly right so ADS stays clear!)
    recv_parts.append(vl.make_beveled_box(
        "PEQ_Body", (0.010, 0.342, 0.068), (0.044, 0.072, 0.019), bevel=0.0022, material=m_fde
    ))
    recv_parts.append(vl.make_cylinder(
        "PEQ_Emitter1", (0.022, 0.380, 0.068), 0.0065, 0.0065, 0.012, segs=16, axis='Y', bevel=0.0008, material=m_dark
    ))
    recv_parts.append(vl.make_cylinder(
        "PEQ_Emitter2", (0.004, 0.379, 0.068), 0.0052, 0.0052, 0.010, segs=16, axis='Y', bevel=0.0007, material=m_steel
    ))
    recv_parts.append(vl.make_cylinder(
        "PEQ_Dial", (0.010, 0.335, 0.078), 0.007, 0.006, 0.005, segs=14, axis='Z', bevel=0.0006, material=m_dark
    ))

    # 5. HEAVY FREE-FLOAT BARREL + GAS BLOCK + TACTICAL SUPPRESSOR/COMPENSATOR
    # Exposed barrel & gas block
    recv_parts.append(vl.make_tube(
        "Barrel", (0.0, 0.295, 0.029), outer_r=0.0105, inner_r=0.0052, length=0.310,
        segs=24, axis='Y', bevel=0.0005, material=m_gun
    ))
    recv_parts.append(vl.make_cylinder(
        "GasTube", (0.0, 0.255, 0.044), 0.0032, 0.0032, 0.210, segs=12, axis='Y', material=m_steel
    ))
    # Fluted tactical QD suppressor / compensator (Y: 0.440..0.576)
    supp_prof = [
        (0.012, 0.434), (0.0175, 0.440), (0.0195, 0.446),
        (0.0195, 0.460), (0.0172, 0.464), (0.0185, 0.468),
        (0.0185, 0.556), (0.0168, 0.562), (0.0178, 0.568),
        (0.0155, 0.576), (0.0065, 0.576), (0.0065, 0.545),
    ]
    recv_parts.append(vl.make_lathe(
        "Suppressor", supp_prof, segs=32, axis='Y', center=(0.0, 0.0, 0.029),
        cap_start=True, cap_end=False, material=m_gun
    ))
    # Suppressor heat-dissipation radial flutes / collar ribs
    for k in range(8):
        ang = k * math.pi / 4.0
        fx = math.cos(ang) * 0.0182
        fz = 0.029 + math.sin(ang) * 0.0182
        recv_parts.append(vl.make_beveled_box(
            f"SuppFlute_{k}", (fx, 0.512, fz), (0.0032, 0.072, 0.0032),
            rot=(0.0, ang, 0.0), bevel=0.0006, material=m_dark
        ))

    # 6. ERGONOMIC TACTICAL PISTOL GRIP
    grip_spine = [
        (0.0, -0.026, -0.022), (0.0, -0.036, -0.052),
        (0.0, -0.048, -0.088), (0.0, -0.062, -0.128),
    ]
    grip_sec = [
        (-0.0145, -0.020), (0.0145, -0.020),
        (0.0165, -0.004), (0.0140, 0.018),
        (0.0085, 0.024), (-0.0085, 0.024),
        (-0.0140, 0.018), (-0.0165, -0.004),
    ]
    recv_parts.append(vl.make_curved_sweep("PistolGrip", grip_spine, grip_sec, material=m_dark))
    # Grip base cap + front finger swell ribs
    recv_parts.append(vl.make_beveled_box(
        "GripCap", (0.0, -0.063, -0.130), (0.031, 0.048, 0.009),
        rot=(-0.32, 0, 0), bevel=0.0018, material=m_fde
    ))

    # 7. ANGLED TACTICAL FOREGRIP (AFG)
    afg_prof = [
        (0.222, -0.002), (0.338, -0.002), (0.338, -0.020),
        (0.320, -0.042), (0.295, -0.050), (0.250, -0.036),
        (0.225, -0.018)
    ]
    recv_parts.append(vl.make_profile_extrude(
        "AngledForegrip", afg_prof, 0.030, plane='YZ', bevel=0.0022, segments=2, material=m_fde
    ))
    recv_parts.append(vl.make_beveled_box(
        "AFG_Clamp", (0.0, 0.280, -0.005), (0.033, 0.108, 0.012), bevel=0.0014, material=m_dark
    ))

    # 8. COLLAPSIBLE SKELETAL TACTICAL STOCK
    # Buffer tube / receiver extension
    recv_parts.append(vl.make_cylinder(
        "BufferTube", (0.0, -0.140, 0.028), 0.0165, 0.0165, 0.195, segs=24, axis='Y', bevel=0.0012, material=m_gun
    ))
    recv_parts.append(vl.make_cylinder(
        "CastleNut", (0.0, -0.056, 0.028), 0.020, 0.020, 0.016, segs=16, axis='Y', bevel=0.001, material=m_dark
    ))
    # Main sculpted cheek-rest stock body (FDE)
    stock_prof = [
        (-0.088, 0.048), (-0.244, 0.048), (-0.246, -0.072),
        (-0.220, -0.072), (-0.198, -0.018), (-0.112, -0.016),
        (-0.088, 0.012)
    ]
    recv_parts.append(vl.make_profile_extrude(
        "StockBody", stock_prof, 0.038, plane='YZ', bevel=0.0028, segments=2, material=m_fde
    ))
    # Diagonal lower stock strut cleanly bridging rear lower toe (-0.228, -0.062) up to front collar (-0.106, -0.006)
    recv_parts.append(vl.make_beveled_box(
        "StockStrut", (0.0, -0.167, -0.034), (0.022, 0.138, 0.015),
        rot=(0.43, 0, 0), bevel=0.002, material=m_dark
    ))
    # Stock latch release lever tucked neatly inside triangle
    recv_parts.append(vl.make_beveled_box(
        "StockLatch", (0.0, -0.148, -0.014), (0.026, 0.056, 0.014), bevel=0.0018, material=m_dark
    ))
    # Ribbed rubber tactical buttpad
    recv_parts.append(vl.make_beveled_box(
        "Buttpad", (0.0, -0.252, -0.012), (0.040, 0.016, 0.122), bevel=0.003, material=m_dark
    ))
    for i in range(7):
        recv_parts.append(vl.make_beveled_box(
            f"ButtRib_{i}", (0.0, -0.260, -0.062 + i * 0.017), (0.038, 0.004, 0.007),
            bevel=0.0008, material=m_dark
        ))
    # QD sling swivel metal cup on stock
    recv_parts.append(vl.make_cylinder(
        "StockQD", (0.0, -0.222, 0.016), 0.0075, 0.0075, 0.042, segs=16, axis='X', bevel=0.0008, material=m_steel
    ))

    # 9. HOLOGRAPHIC TACTICAL RED-DOT OPTIC (EOTech EXPS3 inspired, open armored hood!)
    # QD base rail clamp
    recv_parts.append(vl.make_beveled_box(
        "OpticBase", (0.0, 0.066, 0.068), (0.036, 0.088, 0.014), bevel=0.0018, material=m_dark
    ))
    # Front battery housing
    recv_parts.append(vl.make_cylinder(
        "OpticBattery", (0.0, 0.098, 0.082), 0.012, 0.012, 0.038, segs=18, axis='X', bevel=0.0015, material=m_gun
    ))
    # Armored outer U-hood (sleek low-profile EOTech EXPS3 / UH-1 silhouette with angled chamfers)
    for sx in (-0.020, 0.020):
        recv_parts.append(vl.make_beveled_box(
            f"OpticWall_{sx}", (sx, 0.056, 0.094), (0.005, 0.058, 0.036), bevel=0.0015, material=m_gun
        ))
    recv_parts.append(vl.make_beveled_box(
        "OpticRoof", (0.0, 0.056, 0.113), (0.045, 0.060, 0.005), bevel=0.0016, material=m_gun
    ))
    # Inner slim optic bezel frame
    for sx in (-0.0155, 0.0155):
        recv_parts.append(vl.make_beveled_box(
            f"OpticInnerW_{sx}", (sx, 0.058, 0.094), (0.0028, 0.034, 0.030), bevel=0.0006, material=m_dark
        ))
    recv_parts.append(vl.make_beveled_box(
        "OpticInnerTop", (0.0, 0.058, 0.109), (0.034, 0.034, 0.003), bevel=0.0006, material=m_dark
    ))
    # Side windage/elevation turrets & QD lever
    for ty in (0.046, 0.072):
        recv_parts.append(vl.make_cylinder(
            f"OpticTurret_{ty}", (0.025, ty, 0.084), 0.0048, 0.0048, 0.007, segs=14, axis='X', bevel=0.0006, material=m_steel
        ))
    recv_parts.append(vl.make_beveled_box(
        "OpticQD", (-0.022, 0.052, 0.068), (0.006, 0.032, 0.010), bevel=0.001, material=m_steel
    ))

    # Backup folding iron sights (front & rear folded low on rail)
    recv_parts.append(vl.make_beveled_box(
        "RearBUIS", (0.0, -0.032, 0.068), (0.028, 0.028, 0.013), bevel=0.0012, material=m_dark
    ))
    recv_parts.append(vl.make_beveled_box(
        "FrontBUIS", (0.0, 0.386, 0.068), (0.026, 0.024, 0.013), bevel=0.0012, material=m_dark
    ))

    receiver = vl.join_objects(recv_parts, "Receiver", parent=root)
    vl.shade_hard_surface(receiver, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: CURVED 30-ROUND MAGAZINE (pivot at MagPivot) ---
    mag_pivot_pos = Vector((0.0, 0.088, -0.018))
    mag_parts = []
    mag_spine = [
        (0.0, 0.000, 0.010),
        (0.0, 0.002, -0.032),
        (0.0, 0.008, -0.078),
        (0.0, 0.018, -0.125),
        (0.0, 0.034, -0.175),
    ]
    mag_sec = [
        (-0.0118, -0.028), (0.0118, -0.028),
        (0.0130, -0.014), (0.0130, 0.018),
        (0.0105, 0.028), (-0.0105, 0.028),
        (-0.0130, 0.018), (-0.0130, -0.014),
    ]
    mag_parts.append(vl.make_curved_sweep("MagBody", mag_spine, mag_sec, material=m_fde))
    # Horizontal tactical grip ribs hugging curved magazine (+tilt matches forward +Y curve!)
    for idx, (my, mz, tilt) in enumerate([
        (0.008, -0.078, 0.14), (0.013, -0.102, 0.20),
        (0.020, -0.126, 0.25), (0.027, -0.150, 0.30)
    ]):
        mag_parts.append(vl.make_beveled_box(
            f"MagRib_{idx}", (0.0, my, mz), (0.0275, 0.056, 0.006),
            rot=(+tilt, 0, 0), bevel=0.001, material=m_dark
        ))
    # Flared rubberized ranger floorplate seated flush with bottom tangent (+0.32 rad)
    mag_parts.append(vl.make_beveled_box(
        "MagFloorplate", (0.0, 0.036, -0.178), (0.029, 0.064, 0.012),
        rot=(+0.32, 0, 0), bevel=0.0018, material=m_dark
    ))
    magazine = vl.join_objects(mag_parts, "Magazine", parent=root)
    magazine.location = mag_pivot_pos
    vl.shade_hard_surface(magazine, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: BOLT CARRIER ---
    bolt_parts = [
        vl.make_beveled_box("BoltCarrier", (0.0, 0.0, 0.0), (0.014, 0.078, 0.016), bevel=0.0012, material=m_steel),
        vl.make_beveled_box("BoltCutout", (0.007, 0.008, 0.0), (0.003, 0.024, 0.008), bevel=0.0005, material=m_dark),
    ]
    bolt = vl.join_objects(bolt_parts, "Bolt", parent=root)
    bolt.location = Vector((0.011, 0.056, 0.030))
    vl.shade_hard_surface(bolt, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: TRIGGER ---
    trig_pts = [(0.0, 0.0, 0.0), (0.0, -0.003, -0.012), (0.0, 0.002, -0.024)]
    trig_sec = [(-0.0026, -0.003), (0.0026, -0.003), (0.0026, 0.003), (-0.0026, 0.003)]
    trigger = vl.make_curved_sweep("Trigger", trig_pts, trig_sec, material=m_steel)
    trigger.parent = root
    trigger.location = Vector((0.0, 0.016, -0.021))
    vl.shade_hard_surface(trigger, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE CHILD: OPTIC GLASS (emissive holographic red-dot reticle!) ---
    optic_glass = make_optic_glass_quad("OpticGlass", (0.0, 0.058, 0.094), 0.028, 0.025, m_reticle)
    optic_glass.parent = root

    # --- UNITY ANCHOR EMPTIES ---
    vl.create_empty("MuzzlePoint", (0.0, 0.578, 0.029), parent=root)
    vl.create_empty("EjectPort", (0.024, 0.056, 0.030), parent=root)
    vl.create_empty("MagPivot", tuple(mag_pivot_pos), parent=root)
    vl.create_empty("OpticCenter", (0.0, 0.058, 0.094), parent=root)
    vl.create_empty("LeftHandGrip", (0.0, 0.285, -0.036), parent=root)
    vl.create_empty("RightHandGrip", (0.0, -0.038, -0.072), parent=root)

    # Fast uniform-texel Cube UV projection on all opaque mesh parts (preserve OpticGlass exact 0..1 UVs!)
    vl.smart_uv_unwrap([receiver, magazine, bolt, trigger], cube_size=0.35)

    # Render multi-angle QA + Hero 16:9 showcase shots (full-frame silhouette!)
    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "weapons"), "VX47_AssaultRifle",
        angles=[
            ("hero", (2.55, 1.05, 0.62), 50.0, True),
            ("left", (-2.85, 0.05, 0.12), 54.0, False),
            ("right", (2.85, 0.05, 0.12), 54.0, False),
            ("ads_rear", (0.14, -2.15, 0.42), 55.0, False),
        ],
        res_qa=(960, 600), res_hero=(1600, 900), samples_qa=40, samples_hero=72
    )

    # Save .blend and export Unity-ready .fbx
    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "weapons", "VX47_AssaultRifle.fbx"),
        os.path.join(vl.SCENES_DIR, "VX47_AssaultRifle.blend"),
    )
    meta["textures"] = {
        "VX47_Gunmetal": t_gunmetal,
        "VX47_PolymerFDE": t_fde,
        "VX47_PolymerDark": t_poly_dark,
        "VX47_BoltSteel": t_steel,
        "VX47_OpticReticle": t_reticle,
    }
    meta["renders"] = renders
    vl.update_manifest("weapons", "VX47_AssaultRifle", meta)
    print(f"VX47 done: {meta['triangles']} tris, {meta['file_size_bytes']} bytes")


def build_m890():
    print("\n=== Building M890_TacticalShotgun ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "M890")

    t_steel = vl.generate_pbr_texture_set(
        tex_dir, "M890_OiledSteel", preset="gunmetal", res=2048, seed=201,
        base_rgb=[0.095, 0.102, 0.112]
    )
    t_poly = vl.generate_pbr_texture_set(
        tex_dir, "M890_Polymer", preset="polymer_dark", res=2048, seed=202,
        base_rgb=[0.062, 0.065, 0.070]
    )
    t_chrome = vl.generate_pbr_texture_set(
        tex_dir, "M890_ChromeBolt", preset="chrome_steel", res=1024, seed=203,
        base_rgb=[0.62, 0.64, 0.67]
    )
    t_shell_red = vl.generate_pbr_texture_set(
        tex_dir, "M890_ShellRed", preset="shotgun_red_brass", res=1024, seed=204,
        base_rgb=[0.56, 0.055, 0.045]
    )
    t_brass = vl.generate_pbr_texture_set(
        tex_dir, "M890_ShellBrass", preset="brass", res=1024, seed=205
    )
    t_tritium = vl.generate_pbr_texture_set(
        tex_dir, "M890_Tritium", preset="tritium_green", res=512, seed=206,
        accent_rgb=[0.18, 1.0, 0.28]
    )

    m_steel = vl.build_pbr_material("M890_OiledSteel", t_steel, uv_scale=2.2, normal_strength=0.95)
    m_poly = vl.build_pbr_material("M890_Polymer", t_poly, uv_scale=2.8, normal_strength=1.2)
    m_chrome = vl.build_pbr_material("M890_ChromeBolt", t_chrome, uv_scale=2.0, normal_strength=0.6)
    m_red = vl.build_pbr_material("M890_ShellRed", t_shell_red, uv_scale=2.0, normal_strength=0.9)
    m_brass = vl.build_pbr_material("M890_ShellBrass", t_brass, uv_scale=2.0, normal_strength=0.6)
    m_trit = vl.build_pbr_material(
        "M890_Tritium", t_tritium, uv_scale=1.0, emission_strength=7.0, alpha=0.92, normal_strength=0.1
    )

    root = vl.create_empty("M890_TacticalShotgun", (0, 0, 0), display_size=0.10)
    recv_parts = []

    # 1. HEAVY MILLED STEEL RECEIVER
    rec_xz = [
        (-0.017, -0.022), (0.017, -0.022),
        (0.020, -0.008), (0.020, 0.036),
        (0.014, 0.048), (-0.014, 0.048),
        (-0.020, 0.036), (-0.020, -0.008),
    ]
    recv_parts.append(vl.make_profile_extrude(
        "ReceiverBody", rec_xz, 0.205, plane='XZ', center=(0.0, 0.072, 0.0),
        bevel=0.0022, segments=2, material=m_steel
    ))
    # Right-side ejection port recess
    recv_parts.append(vl.make_beveled_box(
        "EjectRecess", (0.018, 0.086, 0.026), (0.007, 0.076, 0.020), bevel=0.0008, material=m_poly
    ))
    # Oversized gloved trigger guard housing
    tg_pts = [
        (0.0, -0.014, -0.020), (0.0, -0.004, -0.056),
        (0.0, 0.044, -0.056), (0.0, 0.058, -0.022)
    ]
    tg_sec = [(-0.007, -0.0035), (0.007, -0.0035), (0.007, 0.0035), (-0.007, 0.0035)]
    recv_parts.append(vl.make_curved_sweep("TriggerGuard", tg_pts, tg_sec, material=m_poly))
    # Top Picatinny rail + armored Ghost-Ring rear sight
    recv_parts.append(vl.make_picatinny_rail(
        "TopRail", y_start=-0.012, y_end=0.148, z_base=0.048, width=0.021, height=0.008,
        side='TOP', material=m_steel
    ))
    # Ghost-ring sight protective steel wings + aperture ring
    recv_parts.append(vl.make_beveled_box(
        "GhostBase", (0.0, -0.002, 0.059), (0.028, 0.026, 0.008), bevel=0.001, material=m_steel
    ))
    for sx in (-0.012, 0.012):
        recv_parts.append(vl.make_beveled_box(
            f"GhostWing_{sx}", (sx, -0.002, 0.071), (0.004, 0.020, 0.018),
            rot=(0.0, sx * 10.0, 0.0), bevel=0.0008, material=m_steel
        ))
    recv_parts.append(vl.make_tube(
        "GhostRing", (0.0, -0.002, 0.070), outer_r=0.0072, inner_r=0.0048, length=0.008,
        segs=20, axis='Y', bevel=0.0005, material=m_steel
    ))

    # 2. HEAVY 18.5" BARREL + VENTED HEAT SHIELD SHROUD + BREACHER CHOKE
    recv_parts.append(vl.make_tube(
        "Barrel", (0.0, 0.398, 0.030), outer_r=0.0132, inner_r=0.0094, length=0.455,
        segs=28, axis='Y', bevel=0.0005, material=m_steel
    ))
    # Perforated heat shield barrel shroud over top 240 deg of barrel (ribbed shroud + recessed vent holes)
    recv_parts.append(vl.make_tube(
        "HeatShield", (0.0, 0.355, 0.031), outer_r=0.0168, inner_r=0.0145, length=0.335,
        segs=24, axis='Y', bevel=0.0006, material=m_steel
    ))
    # Recessed dark cooling vent holes arranged along heat shield (3 staggered rows)
    for i in range(12):
        vy = 0.210 + i * 0.026
        for ang_deg in (-55, 0, 55):
            a = math.radians(ang_deg)
            vx = math.sin(a) * 0.0165
            vz = 0.031 + math.cos(a) * 0.0165
            recv_parts.append(vl.make_cylinder(
                f"Vent_{i}_{ang_deg}", (vx, vy + (0.012 if ang_deg == 0 else 0.0), vz),
                0.0036, 0.0036, 0.003, segs=10, axis='Z',
                rot=(0.0, a, 0.0), material=m_poly
            ))
    # Standoff breacher muzzle choke with aggressive crenellated teeth!
    choke_prof = [
        (0.0135, 0.595), (0.0165, 0.600), (0.0168, 0.625),
        (0.0155, 0.630), (0.0162, 0.642), (0.0102, 0.642), (0.0102, 0.605)
    ]
    recv_parts.append(vl.make_lathe(
        "BreacherChoke", choke_prof, segs=24, axis='Y', center=(0.0, 0.0, 0.030),
        cap_start=True, cap_end=False, material=m_steel
    ))
    for k in range(6):
        ang = k * math.pi / 3.0
        tx = math.cos(ang) * 0.0135
        tz = 0.030 + math.sin(ang) * 0.0135
        recv_parts.append(vl.make_beveled_box(
            f"BreachTooth_{k}", (tx, 0.645, tz), (0.0045, 0.010, 0.0045),
            rot=(0.0, ang, 0.0), bevel=0.0005, material=m_chrome
        ))
    # Front winged sight tower + glowing green fiber-optic rod
    recv_parts.append(vl.make_beveled_box(
        "FrontSightBase", (0.0, 0.578, 0.048), (0.018, 0.028, 0.016), bevel=0.0012, material=m_steel
    ))
    for sx in (-0.0085, 0.0085):
        recv_parts.append(vl.make_beveled_box(
            f"FrontSightEar_{sx}", (sx, 0.578, 0.062), (0.003, 0.016, 0.016), bevel=0.0006, material=m_steel
        ))
    recv_parts.append(vl.make_cylinder(
        "FrontFiberOptic", (0.0, 0.578, 0.065), 0.0022, 0.0022, 0.014, segs=12, axis='Y', material=m_trit
    ))

    # 3. FULL-LENGTH UNDERBARREL TUBULAR MAGAZINE + BARREL CLAMP
    recv_parts.append(vl.make_cylinder(
        "MagTube", (0.0, 0.378, -0.001), 0.0126, 0.0126, 0.415, segs=24, axis='Y', bevel=0.001, material=m_steel
    ))
    # Knurled magazine end cap
    recv_parts.append(vl.make_cylinder(
        "MagTubeCap", (0.0, 0.592, -0.001), 0.0142, 0.0132, 0.024, segs=20, axis='Y', bevel=0.0015, material=m_steel
    ))
    # Figure-8 steel barrel/magazine clamp with sling mount
    recv_parts.append(vl.make_beveled_box(
        "BarrelClamp", (0.0, 0.538, 0.015), (0.032, 0.024, 0.056), bevel=0.0025, material=m_steel
    ))
    recv_parts.append(vl.make_cylinder(
        "ClampBolt", (0.0, 0.538, 0.015), 0.004, 0.004, 0.036, segs=12, axis='X', bevel=0.0005, material=m_chrome
    ))

    # 4. REINFORCED TACTICAL PISTOL-GRIP BUTTSTOCK
    # Contoured grip
    grip_spine = [
        (0.0, -0.034, -0.014), (0.0, -0.048, -0.048),
        (0.0, -0.066, -0.088), (0.0, -0.084, -0.125),
    ]
    grip_sec = [
        (-0.015, -0.021), (0.015, -0.021),
        (0.017, -0.004), (0.014, 0.018),
        (0.008, 0.023), (-0.008, 0.023),
        (-0.014, 0.018), (-0.017, -0.004),
    ]
    recv_parts.append(vl.make_curved_sweep("ShotgunGrip", grip_spine, grip_sec, material=m_poly))
    # Heavy contoured tactical stock with raised cheek saddle
    stock_prof = [
        (-0.030, 0.040), (-0.080, 0.036), (-0.155, 0.034),
        (-0.320, 0.028), (-0.328, -0.095), (-0.285, -0.095),
        (-0.185, -0.048), (-0.088, -0.028), (-0.030, -0.018),
    ]
    recv_parts.append(vl.make_profile_extrude(
        "StockBody", stock_prof, 0.040, plane='YZ', bevel=0.0045, segments=2, material=m_poly
    ))
    # Thick rubber vented recoil pad
    recv_parts.append(vl.make_beveled_box(
        "RecoilPad", (0.0, -0.334, -0.034), (0.042, 0.024, 0.126), bevel=0.004, material=m_poly
    ))
    # Rear sling loop
    recv_parts.append(vl.make_cylinder(
        "StockSlingPin", (0.0, -0.295, -0.072), 0.006, 0.006, 0.044, segs=14, axis='X', bevel=0.0008, material=m_chrome
    ))

    # 5. LEFT-SIDE 6-SHELL TACTICAL SIDE SADDLE CARRIER (signature shotgun silhouette!)
    recv_parts.append(vl.make_beveled_box(
        "SideSaddlePlate", (-0.022, 0.075, 0.010), (0.005, 0.152, 0.036), bevel=0.0012, material=m_steel
    ))
    for i in range(6):
        sy = 0.013 + i * 0.0245
        # Retention loop
        recv_parts.append(vl.make_beveled_box(
            f"ShellLoop_{i}", (-0.031, sy, 0.010), (0.018, 0.016, 0.026), bevel=0.0015, material=m_poly
        ))
        # 12-gauge crimson hull
        recv_parts.append(vl.make_cylinder(
            f"ShellHull_{i}", (-0.031, sy, 0.004), 0.0096, 0.0096, 0.050, segs=16, axis='Z',
            bevel=0.0008, material=m_red
        ))
        # Gleaming brass rimmed head
        recv_parts.append(vl.make_cylinder(
            f"ShellBrass_{i}", (-0.031, sy, 0.035), 0.0099, 0.0104, 0.015, segs=16, axis='Z',
            bevel=0.0008, material=m_brass
        ))

    receiver = vl.join_objects(recv_parts, "Receiver", parent=root)
    vl.shade_hard_surface(receiver, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: PUMP FOREND (slides along -Y/+Y) ---
    pump_pos = Vector((0.0, 0.340, -0.001))
    pump_parts = []
    # Contoured cylindrical/octagonal heavy ribbed pump body
    pump_prof = [
        (0.022, -0.105), (0.024, -0.095), (0.0215, -0.082),
        (0.0215, 0.082), (0.025, 0.095), (0.022, 0.105),
    ]
    pump_parts.append(vl.make_lathe(
        "PumpBody", pump_prof, segs=24, axis='Y', center=(0.0, 0.0, 0.0), material=m_poly
    ))
    # Deep horizontal traction ribs along forend
    for i in range(11):
        ry = -0.074 + i * 0.0148
        pump_parts.append(vl.make_cylinder(
            f"PumpRib_{i}", (0.0, ry, -0.002), 0.0235, 0.0235, 0.007, segs=20, axis='Y',
            bevel=0.001, material=m_poly
        ))
    # Bottom short Picatinny rail on forend
    pump_parts.append(vl.make_picatinny_rail(
        "PumpBottomRail", y_start=-0.065, y_end=0.065, z_base=-0.023, width=0.018, height=0.006,
        side='BOTTOM', material=m_steel
    ))
    # Twin steel action bars extending back toward receiver
    for sx in (-0.014, 0.014):
        pump_parts.append(vl.make_beveled_box(
            f"ActionBar_{sx}", (sx, -0.135, 0.004), (0.0035, 0.120, 0.009), bevel=0.0005, material=m_chrome
        ))
    # Integrated tactical weapon flashlight mounted on right side of pump
    pump_parts.append(vl.make_beveled_box(
        "LightMount", (0.021, 0.048, 0.002), (0.016, 0.036, 0.016), bevel=0.0015, material=m_steel
    ))
    pump_parts.append(vl.make_cylinder(
        "LightBody", (0.032, 0.056, 0.002), 0.0115, 0.0135, 0.092, segs=20, axis='Y',
        bevel=0.0012, material=m_steel
    ))
    pump_forend = vl.join_objects(pump_parts, "PumpForend", parent=root)
    pump_forend.location = pump_pos
    vl.shade_hard_surface(pump_forend, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: CHROME BREECH BOLT ---
    bolt = vl.make_beveled_box("Bolt", (0.0, 0.0, 0.0), (0.015, 0.072, 0.018), bevel=0.0014, material=m_chrome)
    bolt.parent = root
    bolt.location = Vector((0.011, 0.086, 0.026))
    vl.shade_hard_surface(bolt, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: TRIGGER ---
    trig_pts = [(0.0, 0.0, 0.0), (0.0, -0.004, -0.013), (0.0, 0.003, -0.026)]
    trig_sec = [(-0.003, -0.003), (0.003, -0.003), (0.003, 0.003), (-0.003, 0.003)]
    trigger = vl.make_curved_sweep("Trigger", trig_pts, trig_sec, material=m_steel)
    trigger.parent = root
    trigger.location = Vector((0.0, 0.016, -0.020))
    vl.shade_hard_surface(trigger, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: MAGAZINE LOADING GATE + SHELL ---
    mag_parts = [
        vl.make_beveled_box("LoadingGate", (0.0, 0.0, 0.0), (0.020, 0.064, 0.004), bevel=0.0008, material=m_chrome),
        vl.make_cylinder("ChamberShellHull", (0.0, 0.006, 0.009), 0.0092, 0.0092, 0.044, segs=14, axis='Y', material=m_red),
        vl.make_cylinder("ChamberShellBrass", (0.0, -0.020, 0.009), 0.0096, 0.0096, 0.012, segs=14, axis='Y', material=m_brass),
    ]
    magazine = vl.join_objects(mag_parts, "Magazine", parent=root)
    magazine.location = Vector((0.0, 0.095, -0.022))
    vl.shade_hard_surface(magazine, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE CHILD: OPTIC GLASS (Ghost-Ring Tritium Sight Marker) ---
    optic_glass = make_optic_glass_quad("OpticGlass", (0.0, -0.002, 0.070), 0.008, 0.008, m_trit)
    optic_glass.parent = root

    # --- UNITY ANCHOR EMPTIES ---
    vl.create_empty("MuzzlePoint", (0.0, 0.648, 0.030), parent=root)
    vl.create_empty("EjectPort", (0.024, 0.086, 0.026), parent=root)
    vl.create_empty("MagPivot", (0.0, 0.095, -0.022), parent=root)
    vl.create_empty("OpticCenter", (0.0, -0.002, 0.070), parent=root)
    vl.create_empty("LeftHandGrip", (0.0, 0.0, -0.018), parent=pump_forend)
    vl.create_empty("RightHandGrip", (0.0, -0.055, -0.068), parent=root)

    vl.smart_uv_unwrap([receiver, pump_forend, bolt, trigger, magazine], cube_size=0.35)

    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "weapons"), "M890_TacticalShotgun",
        angles=[
            ("hero", (-2.95, 1.15, 0.65), 46.0, True),   # Full silhouette from recoil pad to breacher teeth!
            ("left", (-3.15, 0.08, 0.10), 50.0, False),
            ("right", (3.15, 0.08, 0.10), 50.0, False),
            ("ads_rear", (0.14, -2.35, 0.48), 52.0, False),
        ],
        res_qa=(960, 600), res_hero=(1600, 900), samples_qa=40, samples_hero=72
    )

    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "weapons", "M890_TacticalShotgun.fbx"),
        os.path.join(vl.SCENES_DIR, "M890_TacticalShotgun.blend"),
    )
    meta["textures"] = {
        "M890_OiledSteel": t_steel,
        "M890_Polymer": t_poly,
        "M890_ChromeBolt": t_chrome,
        "M890_ShellRed": t_shell_red,
        "M890_ShellBrass": t_brass,
        "M890_Tritium": t_tritium,
    }
    meta["renders"] = renders
    vl.update_manifest("weapons", "M890_TacticalShotgun", meta)
    print(f"M890 done: {meta['triangles']} tris, {meta['file_size_bytes']} bytes")


def build_m19():
    print("\n=== Building M19_TacticalPistol ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "M19")

    t_slide = vl.generate_pbr_texture_set(
        tex_dir, "M19_SlideDLC", preset="gunmetal", res=2048, seed=301,
        base_rgb=[0.13, 0.14, 0.155]
    )
    t_frame = vl.generate_pbr_texture_set(
        tex_dir, "M19_FramePolymer", preset="polymer_fde", res=2048, seed=302,
        base_rgb=[0.19, 0.175, 0.145]
    )
    t_dark = vl.generate_pbr_texture_set(
        tex_dir, "M19_DarkTrim", preset="polymer_dark", res=1024, seed=303,
        base_rgb=[0.055, 0.058, 0.065]
    )
    t_barrel = vl.generate_pbr_texture_set(
        tex_dir, "M19_BarrelSteel", preset="chrome_steel", res=1024, seed=304,
        base_rgb=[0.46, 0.44, 0.38]  # subtle TiN bronze/steel sheen!
    )
    t_tritium = vl.generate_pbr_texture_set(
        tex_dir, "M19_Tritium", preset="tritium_green", res=512, seed=305,
        accent_rgb=[0.18, 1.0, 0.30]
    )
    t_reticle = vl.generate_pbr_texture_set(
        tex_dir, "M19_OpticReticle", preset="optic_reticle", res=1024, seed=306,
        accent_rgb=[1.0, 0.15, 0.05]
    )

    m_slide = vl.build_pbr_material("M19_SlideDLC", t_slide, uv_scale=2.5, normal_strength=0.9)
    m_frame = vl.build_pbr_material("M19_FramePolymer", t_frame, uv_scale=3.2, normal_strength=1.25)
    m_dark = vl.build_pbr_material("M19_DarkTrim", t_dark, uv_scale=3.0, normal_strength=1.1)
    m_barrel = vl.build_pbr_material("M19_BarrelSteel", t_barrel, uv_scale=2.2, normal_strength=0.7)
    m_trit = vl.build_pbr_material(
        "M19_Tritium", t_tritium, uv_scale=1.0, emission_strength=8.0, normal_strength=0.1
    )
    m_reticle = vl.build_pbr_material(
        "M19_OpticReticle", t_reticle, uv_scale=1.0, emission_strength=6.5, alpha=0.35, normal_strength=0.1
    )

    root = vl.create_empty("M19_TacticalPistol", (0, 0, 0), display_size=0.05)
    recv_parts = []

    # 1. ERGONOMIC TACTICAL POLYMER FRAME + BEAVERTAIL + UNDERCUT TRIGGER GUARD + DUST COVER RAIL
    frame_prof = [
        (-0.032, 0.017), (0.154, 0.017), (0.154, -0.005),
        (0.072, -0.005), (0.066, 0.000), (0.022, 0.000),
        (0.014, -0.004), (-0.018, 0.004), (-0.032, 0.010),
    ]
    recv_parts.append(vl.make_profile_extrude(
        "FrameRailBody", frame_prof, 0.0275, plane='YZ', bevel=0.0018, segments=2, material=m_frame
    ))
    # Contoured stippled tactical grip (angled at ~18 degrees)
    grip_spine = [
        (0.0, -0.003, 0.006),
        (0.0, -0.012, -0.028),
        (0.0, -0.022, -0.066),
        (0.0, -0.032, -0.104),
    ]
    grip_sec = [
        (-0.014, -0.023), (0.014, -0.023),
        (0.0155, -0.005), (0.0135, 0.019),
        (0.0085, 0.024), (-0.0085, 0.024),
        (-0.0135, 0.019), (-0.0155, -0.005),
    ]
    recv_parts.append(vl.make_curved_sweep("PistolGrip", grip_spine, grip_sec, material=m_frame))
    # Flared aluminum tactical magwell funnel at grip base
    recv_parts.append(vl.make_beveled_box(
        "MagwellFunnel", (0.0, -0.033, -0.105), (0.035, 0.056, 0.011),
        rot=(-0.28, 0, 0), bevel=0.0022, material=m_dark
    ))
    # Squared combat trigger guard
    tg_pts = [
        (0.0, 0.014, -0.012), (0.0, 0.022, -0.034),
        (0.0, 0.068, -0.034), (0.0, 0.076, -0.004),
    ]
    tg_sec = [(-0.0055, -0.0028), (0.0055, -0.0028), (0.0055, 0.0028), (-0.0055, 0.0028)]
    recv_parts.append(vl.make_curved_sweep("TriggerGuard", tg_pts, tg_sec, material=m_frame))
    # Controls: takedown lever, slide release, mag release button
    recv_parts.append(vl.make_beveled_box(
        "TakedownLever", (-0.0155, 0.064, 0.009), (0.004, 0.014, 0.006), bevel=0.0008, material=m_dark
    ))
    recv_parts.append(vl.make_beveled_box(
        "SlideStop", (-0.0152, 0.026, 0.014), (0.0035, 0.024, 0.005), bevel=0.0007, material=m_dark
    ))
    recv_parts.append(vl.make_beveled_box(
        "MagRelease", (-0.015, 0.006, -0.014), (0.004, 0.009, 0.008), bevel=0.0008, material=m_dark
    ))
    # Underbarrel Picatinny rail + sleek compact tactical laser/light module (X300/TLR style)
    recv_parts.append(vl.make_picatinny_rail(
        "UnderRail", y_start=0.088, y_end=0.148, z_base=-0.005, width=0.021, height=0.0065,
        side='BOTTOM', material=m_dark
    ))
    recv_parts.append(vl.make_beveled_box(
        "TacLightBody", (0.0, 0.118, -0.021), (0.029, 0.066, 0.022), bevel=0.0024, material=m_dark
    ))
    recv_parts.append(vl.make_cylinder(
        "TacLightBezel", (0.0, 0.158, -0.022), 0.0125, 0.0135, 0.024, segs=22, axis='Y',
        bevel=0.0012, material=m_slide
    ))
    # Glowing red laser emitter diode underneath
    recv_parts.append(vl.make_cylinder(
        "LaserDiode", (0.0, 0.166, -0.032), 0.0032, 0.0032, 0.012, segs=14, axis='Y', material=m_reticle
    ))
    # Twin rear light toggle paddles
    for sx in (-0.013, 0.013):
        recv_parts.append(vl.make_beveled_box(
            f"LightPaddle_{sx}", (sx, 0.080, -0.021), (0.004, 0.014, 0.010), bevel=0.0008, material=m_dark
        ))

    # 2. MATCH-GRADE THREADED BARREL + KNURLED THREAD PROTECTOR
    recv_parts.append(vl.make_tube(
        "ThreadedBarrel", (0.0, 0.112, 0.030), outer_r=0.0072, inner_r=0.0046, length=0.154,
        segs=24, axis='Y', bevel=0.0004, material=m_barrel
    ))
    # Knurled thread protector at muzzle tip
    recv_parts.append(vl.make_tube(
        "ThreadProtector", (0.0, 0.184, 0.030), outer_r=0.0086, inner_r=0.0052, length=0.015,
        segs=18, axis='Y', bevel=0.0007, material=m_dark
    ))
    # Exposed barrel chamber hood block visible through slide ejection port
    recv_parts.append(vl.make_beveled_box(
        "BarrelChamberHood", (0.001, 0.072, 0.033), (0.021, 0.034, 0.017), bevel=0.0014, material=m_barrel
    ))

    receiver = vl.join_objects(recv_parts, "Receiver", parent=root)
    vl.shade_hard_surface(receiver, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: CNC MACHINED SLIDE + SERRATIONS + TRITIUM SIGHTS + MICRO-OPTIC ---
    slide_parts = []
    # Main chamfered slide cross-section (split into rear + front around ejection port!)
    slide_xz = [
        (-0.0138, 0.017), (0.0138, 0.017),
        (0.0138, 0.041), (0.0095, 0.048),
        (-0.0095, 0.048), (-0.0138, 0.041),
    ]
    # Rear slide section (Y: -0.026 .. 0.056)
    slide_parts.append(vl.make_profile_extrude(
        "SlideRear", slide_xz, 0.082, plane='XZ', center=(0.0, 0.015, 0.0),
        bevel=0.0014, segments=2, material=m_slide
    ))
    # Front slide section (Y: 0.090 .. 0.172)
    slide_parts.append(vl.make_profile_extrude(
        "SlideFront", slide_xz, 0.082, plane='XZ', center=(0.0, 0.131, 0.0),
        bevel=0.0014, segments=2, material=m_slide
    ))
    # Left bridge & lower bridge connecting front & rear slide across ejection port
    slide_parts.append(vl.make_beveled_box(
        "SlideBridgeLeft", (-0.0115, 0.073, 0.032), (0.005, 0.040, 0.029), bevel=0.001, material=m_slide
    ))
    slide_parts.append(vl.make_beveled_box(
        "SlideBridgeLow", (0.006, 0.073, 0.021), (0.015, 0.040, 0.008), bevel=0.0008, material=m_slide
    ))
    # Aggressive CNC front & rear angled cocking serrations (both sides!)
    for sx in (-0.0142, 0.0142):
        for sy in (-0.012, -0.004, 0.004, 0.012, 0.020, 0.122, 0.130, 0.138, 0.146):
            slide_parts.append(vl.make_beveled_box(
                f"Serr_{sx}_{sy}", (sx, sy, 0.030), (0.0018, 0.0032, 0.018),
                rot=(-0.18, 0, 0), bevel=0.0003, material=m_dark
            ))
    # Suppressor-height iron sights + glowing green 3-dot tritium inserts!
    # Rear U-notch sight
    for sx in (-0.0065, 0.0065):
        slide_parts.append(vl.make_beveled_box(
            f"RearSight_{sx}", (sx, -0.016, 0.053), (0.0055, 0.009, 0.011), bevel=0.0006, material=m_dark
        ))
        slide_parts.append(vl.make_cylinder(
            f"RearTrit_{sx}", (sx, -0.0208, 0.055), 0.0016, 0.0016, 0.002, segs=12, axis='Y', material=m_trit
        ))
    # Front sight blade + tritium dot
    slide_parts.append(vl.make_beveled_box(
        "FrontSight", (0.0, 0.160, 0.053), (0.0036, 0.012, 0.011), bevel=0.0005, material=m_dark
    ))
    slide_parts.append(vl.make_cylinder(
        "FrontTrit", (0.0, 0.1538, 0.0555), 0.0016, 0.0016, 0.002, segs=12, axis='Y', material=m_trit
    ))
    # Compact RMR-style micro-reflex red-dot hood mounted on slide optic cut
    slide_parts.append(vl.make_beveled_box(
        "RMR_Base", (0.0, 0.018, 0.050), (0.027, 0.044, 0.006), bevel=0.001, material=m_dark
    ))
    for sx in (-0.012, 0.012):
        slide_parts.append(vl.make_beveled_box(
            f"RMR_Wall_{sx}", (sx, 0.018, 0.062), (0.0032, 0.022, 0.020), bevel=0.0008, material=m_dark
        ))
    slide_parts.append(vl.make_beveled_box(
        "RMR_Top", (0.0, 0.018, 0.072), (0.027, 0.022, 0.0032), bevel=0.0008, material=m_dark
    ))

    slide = vl.join_objects(slide_parts, "Slide", parent=root)
    vl.shade_hard_surface(slide, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: MAGAZINE (extended basepad) ---
    mag_pivot_pos = Vector((0.0, -0.020, -0.060))
    mag_parts = [
        vl.make_beveled_box(
            "MagTube", (0.0, 0.002, 0.004), (0.020, 0.032, 0.094),
            rot=(-0.28, 0, 0), bevel=0.0012, material=m_slide
        ),
        vl.make_beveled_box(
            "MagBasepad", (0.0, -0.014, -0.056), (0.029, 0.046, 0.019),
            rot=(-0.28, 0, 0), bevel=0.002, material=m_dark
        ),
    ]
    magazine = vl.join_objects(mag_parts, "Magazine", parent=root)
    magazine.location = mag_pivot_pos
    vl.shade_hard_surface(magazine, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE ANIMATED CHILD: FLAT-FACED TACTICAL TRIGGER ---
    trigger = vl.make_beveled_box(
        "Trigger", (0.0, 0.0, -0.011), (0.006, 0.005, 0.022),
        rot=(-0.15, 0, 0), bevel=0.0008, material=m_dark
    )
    trigger.parent = root
    trigger.location = Vector((0.0, 0.044, -0.005))
    vl.shade_hard_surface(trigger, angle_deg=36.0, weighted_normals=True)

    # --- SEPARATE CHILD: OPTIC GLASS (Micro-Reflex Red-Dot Reticle, parented to Slide!) ---
    optic_glass = make_optic_glass_quad("OpticGlass", (0.0, 0.018, 0.062), 0.021, 0.017, m_reticle)
    optic_glass.parent = slide

    # --- UNITY ANCHOR EMPTIES ---
    vl.create_empty("MuzzlePoint", (0.0, 0.193, 0.030), parent=root)
    vl.create_empty("EjectPort", (0.016, 0.072, 0.034), parent=root)
    vl.create_empty("MagPivot", tuple(mag_pivot_pos), parent=root)
    vl.create_empty("OpticCenter", (0.0, 0.018, 0.062), parent=root)
    vl.create_empty("LeftHandGrip", (0.0, -0.014, -0.056), parent=root)
    vl.create_empty("RightHandGrip", (0.0, -0.018, -0.052), parent=root)

    vl.smart_uv_unwrap([receiver, slide, magazine, trigger], cube_size=0.18)

    renders = vl.render_studio_shots(
        root, os.path.join(vl.RENDERS_DIR, "weapons"), "M19_TacticalPistol",
        angles=[
            ("hero", (3.05, 1.35, 0.72), 48.0, True),
            ("left", (-3.15, 0.05, 0.14), 50.0, False),
            ("right", (3.15, 0.05, 0.14), 50.0, False),
            ("ads_rear", (0.10, -2.85, 0.95), 50.0, False),
        ],
        res_qa=(960, 600), res_hero=(1600, 900), samples_qa=40, samples_hero=72
    )

    meta = vl.export_unity_fbx(
        root,
        os.path.join(vl.EXPORTS_DIR, "weapons", "M19_TacticalPistol.fbx"),
        os.path.join(vl.SCENES_DIR, "M19_TacticalPistol.blend"),
    )
    meta["textures"] = {
        "M19_SlideDLC": t_slide,
        "M19_FramePolymer": t_frame,
        "M19_DarkTrim": t_dark,
        "M19_BarrelSteel": t_barrel,
        "M19_Tritium": t_tritium,
        "M19_OpticReticle": t_reticle,
    }
    meta["renders"] = renders
    vl.update_manifest("weapons", "M19_TacticalPistol", meta)
    print(f"M19 done: {meta['triangles']} tris, {meta['file_size_bytes']} bytes")


if __name__ == "__main__":
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not only or "vx47" in only:
        build_vx47()
    if not only or "m890" in only:
        build_m890()
    if not only or "m19" in only:
        build_m19()
    print("\n=== ALL WEAPONS BUILD COMPLETE ===")

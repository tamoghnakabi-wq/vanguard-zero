"""
VANGUARD ZERO — Batch 2: AAA Tactical Operator & First-Person Tactical Arms Builder
Generates:
  1. TacticalOperator.fbx (+ .blend, 2K PBR maps, studio renders)
     - Exact 22-bone Unity HumanBodyBones hierarchy in T-Pose (1.80m tall, facing -Y in Blender -> +Z in Unity)
     - Cleanly skinned with deterministic anatomical & armor-rigid vertex weights
     - Sockets: RightHandWeaponMount, HeadEyePoint
     - Dedicated Operator_Visor material slot for Team Alpha (Cyan) vs Team Bravo (Amber) runtime tinting
  2. FP_TacticalArms.fbx (+ .blend, studio renders)
     - Detailed first-person tactical sleeves, forearm GPS/watch, and hard-knuckle gloves rigged with
       LeftUpperArm/LeftLowerArm/LeftHand + RightUpperArm/RightLowerArm/RightHand + RightHandWeaponMount
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


def make_lofted_limb(name, stations, segs=20, material=None, cap_ends=True):
    """
    Create an anatomical lofted mesh across a list of stations:
      stations: list of (center_xyz, rx, ry, rz_rot_or_normal_axis)
      Wait: simpler & ultra-flexible:
      stations is list of (center_vec, radius_u, radius_v, axis_u_vec, axis_v_vec)
    """
    bm = bmesh.new()
    rings = []
    for c, ru, rv, ax_u, ax_v in stations:
        c = Vector(c)
        au = Vector(ax_u).normalized()
        av = Vector(ax_v).normalized()
        ring = []
        for i in range(segs):
            a = 2.0 * math.pi * i / segs
            p = c + au * (ru * math.cos(a)) + av * (rv * math.sin(a))
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for k in range(len(rings) - 1):
        r0, r1 = rings[k], rings[k + 1]
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    if cap_ends and len(rings) >= 2:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return vl.create_mesh_object(name, bm, material=material)


def build_humanoid_armature(name="TacticalOperator"):
    """
    Create the locked 22-bone Unity HumanBodyBones armature in T-Pose (1.80m tall, facing -Y in Blender).
    Left = +X in Blender (-X in Unity), Right = -X in Blender (+X in Unity).
    """
    arm_data = bpy.data.armatures.new(f"{name}_Armature")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    eb = arm_data.edit_bones

    def add_bone(bname, head, tail, parent_name=None, connected=False, roll=0.0):
        b = eb.new(bname)
        b.head = Vector(head)
        b.tail = Vector(tail)
        b.roll = roll
        if parent_name and parent_name in eb:
            b.parent = eb[parent_name]
            b.use_connect = connected
        return b

    # Core Spine & Head chain
    add_bone("Hips",       (0.0, 0.0, 0.96), (0.0, 0.0, 1.07))
    add_bone("Spine",      (0.0, 0.0, 1.07), (0.0, 0.0, 1.20), "Hips", True)
    add_bone("Chest",      (0.0, 0.0, 1.20), (0.0, 0.0, 1.34), "Spine", True)
    add_bone("UpperChest", (0.0, 0.0, 1.34), (0.0, 0.0, 1.48), "Chest", True)
    add_bone("Neck",       (0.0, 0.0, 1.48), (0.0, 0.0, 1.56), "UpperChest", True)
    add_bone("Head",       (0.0, 0.0, 1.56), (0.0, 0.0, 1.78), "Neck", True)

    # Left Arm (+X in Blender)
    add_bone("LeftShoulder", (0.04, 0.0, 1.44), (0.17, 0.0, 1.44), "UpperChest", False)
    add_bone("LeftUpperArm", (0.17, 0.0, 1.44), (0.45, 0.0, 1.44), "LeftShoulder", True)
    add_bone("LeftLowerArm", (0.45, 0.0, 1.44), (0.71, 0.0, 1.44), "LeftUpperArm", True)
    add_bone("LeftHand",     (0.71, 0.0, 1.44), (0.83, 0.0, 1.44), "LeftLowerArm", True)

    # Right Arm (-X in Blender)
    add_bone("RightShoulder", (-0.04, 0.0, 1.44), (-0.17, 0.0, 1.44), "UpperChest", False)
    add_bone("RightUpperArm", (-0.17, 0.0, 1.44), (-0.45, 0.0, 1.44), "RightShoulder", True)
    add_bone("RightLowerArm", (-0.45, 0.0, 1.44), (-0.71, 0.0, 1.44), "RightUpperArm", True)
    add_bone("RightHand",     (-0.71, 0.0, 1.44), (-0.83, 0.0, 1.44), "RightLowerArm", True)

    # Left Leg (+X in Blender)
    add_bone("LeftUpperLeg", (0.105, 0.0, 0.94), (0.105, 0.0, 0.51), "Hips", False)
    add_bone("LeftLowerLeg", (0.105, 0.0, 0.51), (0.105, 0.0, 0.09), "LeftUpperLeg", True)
    add_bone("LeftFoot",     (0.105, 0.0, 0.09), (0.105, -0.13, 0.03), "LeftLowerLeg", True)
    add_bone("LeftToes",     (0.105, -0.13, 0.03), (0.105, -0.22, 0.02), "LeftFoot", True)

    # Right Leg (-X in Blender)
    add_bone("RightUpperLeg", (-0.105, 0.0, 0.94), (-0.105, 0.0, 0.51), "Hips", False)
    add_bone("RightLowerLeg", (-0.105, 0.0, 0.51), (-0.105, 0.0, 0.09), "RightUpperLeg", True)
    add_bone("RightFoot",     (-0.105, 0.0, 0.09), (-0.105, -0.13, 0.03), "RightLowerLeg", True)
    add_bone("RightToes",     (-0.105, -0.13, 0.03), (-0.105, -0.22, 0.02), "RightFoot", True)

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm_obj


def attach_bone_socket(name, arm_obj, bone_name, world_pos, display_size=0.05):
    """Create an Empty socket cleanly bone-parented at exact world_pos with world-aligned orientation."""
    obj = vl.create_empty(name, (0, 0, 0), parent=arm_obj, display_size=display_size)
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    bpy.context.view_layer.update()
    obj.matrix_world = Matrix.Translation(Vector(world_pos))
    bpy.context.view_layer.update()
    return obj


def assign_rigid_weight(obj, bone_name):
    """Tag all vertices of an object with 100% weight to bone_name."""
    vg = obj.vertex_groups.get(bone_name) or obj.vertex_groups.new(name=bone_name)
    idxs = [v.index for v in obj.data.vertices]
    if idxs:
        vg.add(idxs, 1.0, 'REPLACE')


def assign_gradient_chain_weights(obj, axis, chain_stops):
    """
    Smoothly skin a lofted limb/torso along axis ('X' or 'Z') across bone stops:
      chain_stops: list of (coord_start, coord_end, bone_name)
      Blends smoothly across boundaries (+/- 0.035m overlap).
    """
    for _, _, bname in chain_stops:
        if not obj.vertex_groups.get(bname):
            obj.vertex_groups.new(name=bname)
    blend_w = 0.038
    for v in obj.data.vertices:
        wc = (obj.matrix_world @ v.co)
        c = wc.x if axis == 'X' else (wc.y if axis == 'Y' else wc.z)
        weights = {}
        for s0, s1, bname in chain_stops:
            lo, hi = min(s0, s1), max(s0, s1)
            if c < lo - blend_w or c > hi + blend_w:
                continue
            w = 1.0
            if c < lo + blend_w:
                w = min(w, (c - (lo - blend_w)) / (2.0 * blend_w))
            if c > hi - blend_w:
                w = min(w, ((hi + blend_w) - c) / (2.0 * blend_w))
            w = max(0.0, min(1.0, w))
            if w > 1e-3:
                weights[bname] = weights.get(bname, 0.0) + w
        if not weights:
            # Clamp to nearest segment endpoint
            best_b = min(chain_stops, key=lambda item: min(abs(c - item[0]), abs(c - item[1])))[2]
            weights[best_b] = 1.0
        tot = sum(weights.values())
        for bname, w in weights.items():
            obj.vertex_groups[bname].add([v.index], w / tot, 'REPLACE')


def create_operator_materials(tex_dir):
    t_fabric = vl.generate_pbr_texture_set(
        tex_dir, "Operator_Fabric", preset="tactical_fabric", res=2048, seed=401,
        base_rgb=[0.088, 0.098, 0.108]
    )
    t_armor = vl.generate_pbr_texture_set(
        tex_dir, "Operator_Armor", preset="helmet_armor", res=2048, seed=402,
        base_rgb=[0.245, 0.212, 0.155]  # Contrasting Tactical Coyote/FDE plate carrier & FAST helmet
    )
    t_dark_gear = vl.generate_pbr_texture_set(
        tex_dir, "Operator_GearDark", preset="leather_rubber", res=2048, seed=403,
        base_rgb=[0.055, 0.058, 0.064]
    )
    t_metal = vl.generate_pbr_texture_set(
        tex_dir, "Operator_Metal", preset="gunmetal", res=1024, seed=404,
        base_rgb=[0.16, 0.17, 0.19]
    )
    t_visor_alpha = vl.generate_pbr_texture_set(
        tex_dir, "Operator_VisorCyan", preset="visor_cyan", res=1024, seed=405,
        accent_rgb=[0.08, 0.85, 1.0]
    )
    # Also save Team Bravo Amber emission texture in textures/Operator/ so Unity can swap or tint!
    t_visor_bravo = vl.generate_pbr_texture_set(
        tex_dir, "Operator_VisorAmber", preset="visor_cyan", res=1024, seed=406,
        accent_rgb=[1.0, 0.42, 0.06]
    )

    m_fabric = vl.build_pbr_material("Operator_Fabric", t_fabric, uv_scale=4.5, normal_strength=1.35)
    m_armor = vl.build_pbr_material("Operator_Armor", t_armor, uv_scale=2.8, normal_strength=1.1)
    m_gear = vl.build_pbr_material("Operator_GearDark", t_dark_gear, uv_scale=3.2, normal_strength=1.1)
    m_metal = vl.build_pbr_material("Operator_Metal", t_metal, uv_scale=2.5, normal_strength=0.8)
    m_visor = vl.build_pbr_material(
        "Operator_Visor", t_visor_alpha, uv_scale=1.0, emission_strength=5.5, normal_strength=0.2
    )
    tex_dict = {
        "Operator_Fabric": t_fabric,
        "Operator_Armor": t_armor,
        "Operator_GearDark": t_dark_gear,
        "Operator_Metal": t_metal,
        "Operator_VisorCyan": t_visor_alpha,
        "Operator_VisorAmber": t_visor_bravo,
    }
    return (m_fabric, m_armor, m_gear, m_metal, m_visor), tex_dict


def build_arm_meshes(side_sign, m_fabric, m_armor, m_gear, m_metal, include_shoulder_pad=True):
    """
    Build detailed anatomical left (+1) or right (-1) tactical sleeve, elbow pad, wrist watch/GPS,
    and hard-knuckle tactical glove along ±X at Z=1.44m, skinned to Shoulder/UpperArm/LowerArm/Hand.
    """
    s = float(side_sign)
    prefix = "Left" if s > 0 else "Right"
    parts = []

    # 1. Anatomical combat sleeve (from shoulder x=0.14 to wrist x=0.71)
    ax_u = (0.0, 1.0, 0.0)
    ax_v = (0.0, 0.0, 1.0)
    sleeve_stations = [
        ((s * 0.14, 0.00, 1.44), 0.066, 0.072, ax_u, ax_v),
        ((s * 0.21, 0.00, 1.44), 0.068, 0.072, ax_u, ax_v),  # Deltoid swell
        ((s * 0.31, 0.00, 1.44), 0.060, 0.062, ax_u, ax_v),  # Biceps
        ((s * 0.43, 0.00, 1.44), 0.052, 0.054, ax_u, ax_v),  # Elbow taper
        ((s * 0.47, 0.00, 1.44), 0.055, 0.056, ax_u, ax_v),  # Forearm flexor swell
        ((s * 0.56, 0.00, 1.44), 0.050, 0.052, ax_u, ax_v),
        ((s * 0.65, 0.00, 1.44), 0.043, 0.040, ax_u, ax_v),
        ((s * 0.71, 0.00, 1.44), 0.040, 0.035, ax_u, ax_v),  # Wrist cuff
    ]
    sleeve = make_lofted_limb(f"{prefix}Sleeve", sleeve_stations, segs=20, material=m_fabric)
    assign_gradient_chain_weights(sleeve, 'X', [
        (s * 0.04, s * 0.17, f"{prefix}Shoulder"),
        (s * 0.17, s * 0.45, f"{prefix}UpperArm"),
        (s * 0.45, s * 0.71, f"{prefix}LowerArm"),
    ])
    parts.append(sleeve)

    # 2. Ballistic shoulder deltoid armor pad + velcro patch panel
    if include_shoulder_pad:
        spad = vl.make_beveled_box(
            f"{prefix}ShoulderPad", (s * 0.22, 0.0, 1.465), (0.11, 0.145, 0.095),
            rot=(0.0, s * 0.35, 0.0), bevel=0.015, segments=2, material=m_armor
        )
        assign_rigid_weight(spad, f"{prefix}UpperArm")
        parts.append(spad)

    # 3. Molded hard-shell elbow pad + forearm strap
    epad = vl.make_beveled_box(
        f"{prefix}ElbowPad", (s * 0.45, 0.038, 1.44), (0.085, 0.065, 0.075),
        bevel=0.012, segments=2, material=m_gear
    )
    assign_rigid_weight(epad, f"{prefix}LowerArm")
    parts.append(epad)

    # 4. Left wrist tactical GPS/comms computer & watch
    if s > 0:
        gps = vl.make_beveled_box(
            "WristGPS", (0.64, -0.01, 1.478), (0.068, 0.056, 0.022), bevel=0.004, material=m_gear
        )
        assign_rigid_weight(gps, "LeftLowerArm")
        parts.append(gps)
    else:
        watch = vl.make_cylinder(
            "TacticalWatch", (-0.66, 0.0, 1.472), 0.022, 0.022, 0.016, segs=16, axis='Z', bevel=0.002, material=m_metal
        )
        assign_rigid_weight(watch, "RightLowerArm")
        parts.append(watch)

    # 5. Sculpted Hard-Knuckle Tactical Glove (wrist cuff + palm + carbon knuckle ridge + thumb + fingers)
    glove_parts = [
        vl.make_beveled_box(f"{prefix}GloveCuff", (s * 0.72, 0.0, 1.44), (0.045, 0.084, 0.066), bevel=0.008, material=m_gear),
        vl.make_beveled_box(f"{prefix}GlovePalm", (s * 0.765, 0.0, 1.44), (0.065, 0.088, 0.036), bevel=0.008, material=m_gear),
        vl.make_beveled_box(f"{prefix}KnuckleGuard", (s * 0.785, 0.0, 1.460), (0.028, 0.082, 0.014), bevel=0.004, material=m_armor),
        # Thumb pointing forward (-Y)
        vl.make_beveled_box(
            f"{prefix}Thumb", (s * 0.755, -0.058, 1.432), (0.048, 0.036, 0.024),
            rot=(0.0, 0.0, s * 0.45), bevel=0.006, material=m_gear
        ),
    ]
    # 4 Individual tactical glove fingers
    for idx, fy in enumerate((-0.032, -0.011, 0.011, 0.032)):
        flen = 0.056 if idx in (1, 2) else 0.048
        glove_parts.append(vl.make_beveled_box(
            f"{prefix}Finger_{idx}", (s * (0.795 + flen * 0.45), fy, 1.438),
            (flen, 0.0185, 0.022), bevel=0.005, material=m_gear
        ))
    glove = vl.join_objects(glove_parts, f"{prefix}Glove")
    assign_rigid_weight(glove, f"{prefix}Hand")
    parts.append(glove)

    return parts


def build_tactical_operator():
    print("\n=== Building TacticalOperator ===")
    vl.clear_scene()
    tex_dir = os.path.join(vl.TEXTURES_DIR, "Operator")
    (m_fabric, m_armor, m_gear, m_metal, m_visor), tex_dict = create_operator_materials(tex_dir)

    arm_obj = build_humanoid_armature("TacticalOperator")
    body_parts = []

    ax_x = (1.0, 0.0, 0.0)
    ax_y = (0.0, 1.0, 0.0)

    # 1. ANATOMICAL TORSO + PELVIS + NECK (Z: 0.78 .. 1.58, seamless pelvic taper into thighs!)
    torso_stations = [
        ((0.0, 0.00, 0.78), 0.075, 0.085, ax_x, ax_y),  # Lower pelvic bridge between thighs
        ((0.0, 0.00, 0.87), 0.158, 0.112, ax_x, ax_y),  # Pelvis
        ((0.0, 0.00, 0.96), 0.178, 0.125, ax_x, ax_y),  # Hips / belt line
        ((0.0, 0.00, 1.08), 0.158, 0.115, ax_x, ax_y),  # Waist
        ((0.0, -0.01, 1.22), 0.178, 0.132, ax_x, ax_y), # Ribcage
        ((0.0, -0.01, 1.36), 0.198, 0.138, ax_x, ax_y), # Upper chest
        ((0.0, 0.00, 1.45), 0.188, 0.118, ax_x, ax_y),  # Clavicle/shoulder line
        ((0.0, 0.01, 1.50), 0.076, 0.074, ax_x, ax_y),  # Collar base
        ((0.0, 0.00, 1.58), 0.062, 0.064, ax_x, ax_y),  # Neck top
    ]
    torso = make_lofted_limb("TorsoMesh", torso_stations, segs=24, material=m_fabric)
    assign_gradient_chain_weights(torso, 'Z', [
        (0.75, 1.07, "Hips"),
        (1.07, 1.20, "Spine"),
        (1.20, 1.34, "Chest"),
        (1.34, 1.48, "UpperChest"),
        (1.48, 1.60, "Neck"),
    ])
    body_parts.append(torso)

    # 2. TACTICAL BATTLE BELT + BALLISTIC GROIN PROTECTOR + POUCHES (Hips / Spine)
    belt_parts = [
        vl.make_cylinder("BattleBelt", (0.0, 0.0, 0.985), 0.188, 0.188, 0.068, segs=24, axis='Z', bevel=0.008, material=m_gear),
        vl.make_beveled_box("BeltBuckle", (0.0, -0.138, 0.985), (0.058, 0.026, 0.056), bevel=0.004, material=m_metal),
        # Ballistic front groin armor flap (hides hip crease & adds authentic spec-ops silhouette!)
        vl.make_beveled_box("GroinArmor", (0.0, -0.122, 0.885), (0.165, 0.036, 0.145), bevel=0.014, segments=2, material=m_armor),
        vl.make_beveled_box("RearHipArmor", (0.0, 0.120, 0.895), (0.195, 0.036, 0.125), bevel=0.014, segments=2, material=m_armor),
        # Left hip dump pouch & 2 pistol mag pouches
        vl.make_beveled_box("DumpPouch", (0.185, 0.04, 0.94), (0.055, 0.095, 0.135), bevel=0.010, material=m_armor),
        vl.make_beveled_box("PistolMag1", (0.165, -0.075, 0.99), (0.040, 0.042, 0.095), bevel=0.006, material=m_gear),
        vl.make_beveled_box("PistolMag2", (0.135, -0.105, 0.99), (0.040, 0.042, 0.095), bevel=0.006, material=m_gear),
        # Rear IFAK medkit pouch
        vl.make_beveled_box("IFAK_Pouch", (0.0, 0.145, 0.99), (0.135, 0.070, 0.095), bevel=0.010, material=m_armor),
    ]
    belt_obj = vl.join_objects(belt_parts, "BattleBeltRig")
    assign_rigid_weight(belt_obj, "Hips")
    body_parts.append(belt_obj)

    # 3. JPC / AVS PLATE CARRIER VEST + MAG POUCHES + RADIO + ANTENNA + BACKPACK (Chest / UpperChest)
    vest_parts = [
        # Front & rear sculpted SAPI ballistic plate bags
        vl.make_beveled_box("FrontPlateBag", (0.0, -0.118, 1.285), (0.295, 0.055, 0.310), bevel=0.018, segments=2, material=m_armor),
        vl.make_beveled_box("RearPlateBag", (0.0, 0.115, 1.295), (0.290, 0.055, 0.320), bevel=0.018, segments=2, material=m_armor),
        # Cummerbund side straps
        vl.make_beveled_box("CummerbundL", (0.172, 0.0, 1.215), (0.055, 0.220, 0.135), bevel=0.010, material=m_armor),
        vl.make_beveled_box("CummerbundR", (-0.172, 0.0, 1.215), (0.055, 0.220, 0.135), bevel=0.010, material=m_armor),
        # Padded shoulder straps over trapezius
        vl.make_beveled_box("ShoulderStrapL", (0.105, 0.0, 1.455), (0.068, 0.235, 0.036), bevel=0.008, material=m_armor),
        vl.make_beveled_box("ShoulderStrapR", (-0.105, 0.0, 1.455), (0.068, 0.235, 0.036), bevel=0.008, material=m_armor),
        # 3 Front Triple-Rifle Magazine Pouches + protruding curved mags!
        vl.make_beveled_box("FrontAdminPouch", (0.0, -0.150, 1.375), (0.195, 0.032, 0.085), bevel=0.006, material=m_gear),
        # Tactical PTT (Push-To-Talk) disc on upper left chest
        vl.make_cylinder("ChestPTT", (0.085, -0.155, 1.395), 0.024, 0.024, 0.018, segs=16, axis='Y', bevel=0.003, material=m_gear),
        # Tactical AN/PRC-152 Radio in left cummerbund pouch + tall whip antenna!
        vl.make_beveled_box("RadioPouch", (0.155, -0.105, 1.235), (0.068, 0.058, 0.145), bevel=0.008, material=m_gear),
        vl.make_cylinder("RadioAntenna", (0.158, -0.105, 1.465), 0.0045, 0.003, 0.340, segs=10, axis='Z', material=m_gear),
        # Tactical Assault Hydration Pack on back
        vl.make_beveled_box("BackPackMain", (0.0, 0.168, 1.300), (0.255, 0.095, 0.310), bevel=0.020, segments=2, material=m_armor),
        vl.make_beveled_box("BackPackPocket", (0.0, 0.218, 1.255), (0.215, 0.045, 0.175), bevel=0.012, material=m_gear),
    ]
    for idx, mx in enumerate((-0.082, 0.0, 0.082)):
        vest_parts.append(vl.make_beveled_box(
            f"RifleMagPouch_{idx}", (mx, -0.156, 1.215), (0.072, 0.048, 0.130), bevel=0.007, material=m_armor
        ))
        vest_parts.append(vl.make_beveled_box(
            f"RifleMagTop_{idx}", (mx, -0.156, 1.292), (0.060, 0.032, 0.048), bevel=0.004, material=m_gear
        ))
    vest_obj = vl.join_objects(vest_parts, "PlateCarrierVest")
    assign_gradient_chain_weights(vest_obj, 'Z', [
        (1.08, 1.33, "Chest"),
        (1.33, 1.65, "UpperChest"),
    ])
    body_parts.append(vest_obj)

    # 4. HEAD, BALACLAVA, FAST BALLISTIC HELMET, COMTAC HEADSET, QUAD NVG & GLOWING TEAM VISOR (Head bone)
    head_parts = [
        # Sculpted balaclava head
        make_lofted_limb("HeadBalaclava", [
            ((0.0, 0.00, 1.54), 0.065, 0.068, ax_x, ax_y),
            ((0.0, -0.01, 1.60), 0.086, 0.098, ax_x, ax_y),
            ((0.0, -0.01, 1.67), 0.094, 0.106, ax_x, ax_y),
            ((0.0, 0.00, 1.74), 0.088, 0.098, ax_x, ax_y),
            ((0.0, 0.01, 1.78), 0.055, 0.062, ax_x, ax_y),
        ], segs=22, material=m_gear),
        # Armored lower-face respirator/mandible mask
        vl.make_beveled_box("FaceMandible", (0.0, -0.078, 1.605), (0.135, 0.095, 0.082), bevel=0.016, segments=2, material=m_gear),
        # Tactical Ballistic Goggles frame + Glowing Team Visor Lens!
        vl.make_beveled_box("GoggleFrame", (0.0, -0.088, 1.662), (0.168, 0.068, 0.056), bevel=0.012, segments=2, material=m_gear),
        vl.make_beveled_box("VisorLens", (0.0, -0.102, 1.662), (0.152, 0.048, 0.036), bevel=0.008, segments=2, material=m_visor),
        # High-Cut FAST Ballistic Helmet dome
        make_lofted_limb("FastHelmetDome", [
            ((0.0, 0.005, 1.665), 0.112, 0.124, ax_x, ax_y),
            ((0.0, 0.005, 1.710), 0.116, 0.128, ax_x, ax_y),
            ((0.0, 0.010, 1.755), 0.105, 0.116, ax_x, ax_y),
            ((0.0, 0.015, 1.795), 0.072, 0.080, ax_x, ax_y),
            ((0.0, 0.015, 1.810), 0.025, 0.028, ax_x, ax_y),
        ], segs=24, material=m_armor),
        # Helmet side ARC rails + Rear Counterweight battery box
        vl.make_beveled_box("HelmetRailL", (0.112, -0.01, 1.705), (0.018, 0.135, 0.028), bevel=0.004, material=m_gear),
        vl.make_beveled_box("HelmetRailR", (-0.112, -0.01, 1.705), (0.018, 0.135, 0.028), bevel=0.004, material=m_gear),
        vl.make_beveled_box("HelmetBattery", (0.0, 0.125, 1.700), (0.085, 0.035, 0.058), bevel=0.006, material=m_gear),
        # Helmet team identification beacon strip on top/rear
        vl.make_beveled_box("HelmetStrobe", (0.0, 0.085, 1.775), (0.042, 0.055, 0.020), bevel=0.004, material=m_visor),
        # Peltor ComTac Tactical Headset Ear-Cups (left & right)
        vl.make_beveled_box("ComtacL", (0.108, 0.010, 1.645), (0.036, 0.078, 0.092), bevel=0.012, material=m_gear),
        vl.make_beveled_box("ComtacR", (-0.108, 0.010, 1.645), (0.036, 0.078, 0.092), bevel=0.012, material=m_gear),
        # Front Wilcox NVG Mount Shroud + Flipped-Up Quad-Tube GPNVG-18 Night Vision Goggles!
        vl.make_beveled_box("NVG_Shroud", (0.0, -0.118, 1.725), (0.056, 0.025, 0.052), bevel=0.004, material=m_metal),
        vl.make_beveled_box("NVG_Arm", (0.0, -0.145, 1.748), (0.032, 0.048, 0.042), bevel=0.004, material=m_metal),
        vl.make_beveled_box("NVG_Bridge", (0.0, -0.165, 1.768), (0.168, 0.032, 0.026), bevel=0.004, material=m_armor),
    ]
    # 4 Quad-NVG optical tubes with glowing objective lenses
    for nx in (-0.068, -0.026, 0.026, 0.068):
        head_parts.append(vl.make_cylinder(
            f"NVG_Tube_{nx}", (nx, -0.175, 1.748), 0.016, 0.018, 0.062, segs=16, axis='Z',
            rot=(0.35, 0, 0), bevel=0.002, material=m_gear
        ))
        head_parts.append(vl.make_cylinder(
            f"NVG_Lens_{nx}", (nx, -0.186, 1.720), 0.014, 0.014, 0.006, segs=16, axis='Z',
            rot=(0.35, 0, 0), material=m_visor
        ))
    head_obj = vl.join_objects(head_parts, "HeadHelmetRig")
    assign_rigid_weight(head_obj, "Head")
    body_parts.append(head_obj)

    # 5. LEFT & RIGHT ARMS (T-Pose along ±X)
    body_parts.extend(build_arm_meshes(+1, m_fabric, m_armor, m_gear, m_metal, include_shoulder_pad=True))
    body_parts.extend(build_arm_meshes(-1, m_fabric, m_armor, m_gear, m_metal, include_shoulder_pad=True))

    # 6. LEFT & RIGHT LEGS, HOLSTER, HARD KNEE PADS & COMBAT BOOTS
    for side_sign in (+1, -1):
        s = float(side_sign)
        prefix = "Left" if s > 0 else "Right"
        lx = s * 0.105

        leg_stations = [
            ((lx, 0.00, 0.94), 0.095, 0.102, ax_x, ax_y),  # Upper thigh
            ((lx, 0.00, 0.78), 0.088, 0.092, ax_x, ax_y),  # Mid thigh
            ((lx, 0.00, 0.62), 0.074, 0.078, ax_x, ax_y),  # Lower thigh
            ((lx, 0.00, 0.51), 0.066, 0.070, ax_x, ax_y),  # Knee
            ((lx, 0.01, 0.38), 0.068, 0.074, ax_x, ax_y),  # Calf swell
            ((lx, 0.00, 0.22), 0.054, 0.058, ax_x, ax_y),  # Lower shin
            ((lx, 0.00, 0.11), 0.048, 0.052, ax_x, ax_y),  # Ankle cuff
        ]
        leg = make_lofted_limb(f"{prefix}LegPants", leg_stations, segs=20, material=m_fabric)
        assign_gradient_chain_weights(leg, 'Z', [
            (0.08, 0.51, f"{prefix}LowerLeg"),
            (0.51, 0.96, f"{prefix}UpperLeg"),
        ])
        body_parts.append(leg)

        # Thigh cargo pocket + Right drop-leg tactical pistol holster
        cargo = vl.make_beveled_box(
            f"{prefix}CargoPocket", (lx + s * 0.082, 0.0, 0.72), (0.032, 0.125, 0.155), bevel=0.008, material=m_fabric
        )
        assign_rigid_weight(cargo, f"{prefix}UpperLeg")
        body_parts.append(cargo)

        if s < 0:
            # Right thigh drop-leg Kydex holster + retention straps
            holster_parts = [
                vl.make_beveled_box("HolsterPlate", (lx - 0.090, -0.01, 0.74), (0.025, 0.115, 0.175), bevel=0.006, material=m_gear),
                vl.make_beveled_box("HolsterBody", (lx - 0.112, -0.02, 0.74), (0.042, 0.075, 0.155), rot=(0.15, 0, 0), bevel=0.008, material=m_armor),
                vl.make_cylinder("HolsterStrap1", (lx, 0.0, 0.78), 0.095, 0.095, 0.028, segs=20, axis='Z', material=m_gear),
                vl.make_cylinder("HolsterStrap2", (lx, 0.0, 0.69), 0.086, 0.086, 0.028, segs=20, axis='Z', material=m_gear),
            ]
            holster = vl.join_objects(holster_parts, "DropLegHolster")
            assign_rigid_weight(holster, "RightUpperLeg")
            body_parts.append(holster)

        # Hard-shell Tactical Knee Pad + straps
        kpad_parts = [
            vl.make_beveled_box(f"{prefix}KneeCap", (lx, -0.068, 0.505), (0.102, 0.052, 0.125), bevel=0.016, segments=2, material=m_gear),
            vl.make_beveled_box(f"{prefix}KneeShield", (lx, -0.082, 0.505), (0.082, 0.028, 0.088), bevel=0.010, material=m_armor),
            vl.make_cylinder(f"{prefix}KneeStrap", (lx, 0.0, 0.505), 0.072, 0.072, 0.036, segs=18, axis='Z', material=m_gear),
        ]
        kpad = vl.join_objects(kpad_parts, f"{prefix}KneePad")
        assign_rigid_weight(kpad, f"{prefix}LowerLeg")
        body_parts.append(kpad)

        # Detailed Salomon/Lowa Tactical Combat Boot (skinned across Foot & Toes!)
        boot_parts = [
            vl.make_beveled_box(f"{prefix}BootShaft", (lx, 0.00, 0.115), (0.102, 0.112, 0.130), bevel=0.016, segments=2, material=m_gear),
            vl.make_beveled_box(f"{prefix}BootHeel", (lx, -0.025, 0.048), (0.106, 0.155, 0.088), bevel=0.016, segments=2, material=m_gear),
            vl.make_beveled_box(f"{prefix}BootToe", (lx, -0.135, 0.036), (0.104, 0.145, 0.068), bevel=0.018, segments=2, material=m_gear),
            vl.make_beveled_box(f"{prefix}BootSole", (lx, -0.078, 0.014), (0.112, 0.258, 0.028), bevel=0.006, segments=2, material=m_armor),
        ]
        boot = vl.join_objects(boot_parts, f"{prefix}Boot")
        assign_gradient_chain_weights(boot, 'Y', [
            (-0.12, 0.08, f"{prefix}Foot"),
            (-0.25, -0.12, f"{prefix}Toes"),
        ])
        body_parts.append(boot)

    # Combine all skinned body parts into a single clean SkinnedMeshRenderer mesh `OperatorMesh`
    operator_mesh = vl.join_objects(body_parts, "OperatorMesh", parent=arm_obj)
    vl.shade_hard_surface(operator_mesh, angle_deg=42.0, weighted_normals=True)
    vl.smart_uv_unwrap([operator_mesh], cube_size=0.45)

    # Add Armature Modifier binding OperatorMesh -> TacticalOperator armature
    arm_mod = operator_mesh.modifiers.new(name="Armature", type='ARMATURE')
    arm_mod.object = arm_obj
    arm_mod.use_vertex_groups = True

    # Add Bone-Parented Socket Empties per @motion-lead's locked rig contract (#14)
    attach_bone_socket("RightHandWeaponMount", arm_obj, "RightHand", (-0.76, -0.02, 1.43), display_size=0.06)
    attach_bone_socket("HeadEyePoint", arm_obj, "Head", (0.0, -0.10, 1.66), display_size=0.05)

    # Render multi-angle Cycles Metal studio shots (full-height 16:9 Hero + front/back!)
    renders = vl.render_studio_shots(
        arm_obj, os.path.join(vl.RENDERS_DIR, "characters"), "TacticalOperator",
        angles=[
            ("hero", (-1.85, -3.15, 0.35), 48.0, True),
            ("front", (0.0, -3.05, 0.12), 52.0, False),
            ("back", (0.55, 2.95, 0.20), 52.0, False),
            ("upper_body", (-0.85, -1.75, 0.65), 62.0, False),
        ],
        res_qa=(900, 900), res_hero=(1600, 900), samples_qa=44, samples_hero=72
    )

    # Export Unity-ready Skinned FBX (character faces -Y in Blender -> export with axis_forward='-Z', axis_up='Y')
    fbx_path = os.path.join(vl.EXPORTS_DIR, "characters", "TacticalOperator.fbx")
    blend_path = os.path.join(vl.SCENES_DIR, "TacticalOperator.blend")
    os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    bpy.ops.object.select_all(action='DESELECT')
    for o in [arm_obj] + list(arm_obj.children_recursive):
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj

    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        use_space_transform=True,
        bake_space_transform=False,
        object_types={'EMPTY', 'ARMATURE', 'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        use_tspace=True,
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        use_armature_deform_only=False,
        bake_anim=False,
    )

    me = operator_mesh.data
    me.calc_loop_triangles()
    bones_list = [b.name for b in arm_obj.data.bones]
    meta = {
        "name": "TacticalOperator",
        "fbx_path": fbx_path,
        "blend_path": blend_path,
        "file_size_bytes": os.path.getsize(fbx_path),
        "triangles": len(me.loop_triangles),
        "vertices": len(me.vertices),
        "height_meters": 1.80,
        "rest_pose": "T-Pose (Unity HumanBodyBones auto-map compatible, 22 bones)",
        "unity_orientation": "+Z forward, +Y up, +X right",
        "bones": bones_list,
        "sockets": ["RightHandWeaponMount", "HeadEyePoint"],
        "team_readability_material": "Operator_Visor (Operator_VisorCyan vs Operator_VisorAmber)",
        "textures": tex_dict,
        "renders": renders,
    }
    vl.update_manifest("characters", "TacticalOperator", meta)
    print(f"TacticalOperator done: {meta['triangles']} tris, {len(bones_list)} bones")
    return tex_dict


def build_fp_tactical_arms(tex_dict):
    print("\n=== Building FP_TacticalArms ===")
    vl.clear_scene()
    m_fabric = vl.build_pbr_material("Operator_Fabric", tex_dict["Operator_Fabric"], uv_scale=4.5, normal_strength=1.35)
    m_armor = vl.build_pbr_material("Operator_Armor", tex_dict["Operator_Armor"], uv_scale=2.8, normal_strength=1.1)
    m_gear = vl.build_pbr_material("Operator_GearDark", tex_dict["Operator_GearDark"], uv_scale=3.2, normal_strength=1.1)
    m_metal = vl.build_pbr_material("Operator_Metal", tex_dict["Operator_Metal"], uv_scale=2.5, normal_strength=0.8)

    arm_data = bpy.data.armatures.new("FP_TacticalArms_Armature")
    arm_obj = bpy.data.objects.new("FP_TacticalArms", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def add_bone(bname, head, tail, parent_name=None, connected=False):
        b = eb.new(bname)
        b.head = Vector(head)
        b.tail = Vector(tail)
        if parent_name and parent_name in eb:
            b.parent = eb[parent_name]
            b.use_connect = connected
        return b

    add_bone("UpperChest", (0.0, 0.0, 1.34), (0.0, 0.0, 1.48))
    add_bone("LeftShoulder", (0.04, 0.0, 1.44), (0.17, 0.0, 1.44), "UpperChest", False)
    add_bone("LeftUpperArm", (0.17, 0.0, 1.44), (0.45, 0.0, 1.44), "LeftShoulder", True)
    add_bone("LeftLowerArm", (0.45, 0.0, 1.44), (0.71, 0.0, 1.44), "LeftUpperArm", True)
    add_bone("LeftHand",     (0.71, 0.0, 1.44), (0.83, 0.0, 1.44), "LeftLowerArm", True)

    add_bone("RightShoulder", (-0.04, 0.0, 1.44), (-0.17, 0.0, 1.44), "UpperChest", False)
    add_bone("RightUpperArm", (-0.17, 0.0, 1.44), (-0.45, 0.0, 1.44), "RightShoulder", True)
    add_bone("RightLowerArm", (-0.45, 0.0, 1.44), (-0.71, 0.0, 1.44), "RightUpperArm", True)
    add_bone("RightHand",     (-0.71, 0.0, 1.44), (-0.83, 0.0, 1.44), "RightLowerArm", True)
    bpy.ops.object.mode_set(mode='OBJECT')

    parts = []
    parts.extend(build_arm_meshes(+1, m_fabric, m_armor, m_gear, m_metal, include_shoulder_pad=False))
    parts.extend(build_arm_meshes(-1, m_fabric, m_armor, m_gear, m_metal, include_shoulder_pad=False))

    arms_mesh = vl.join_objects(parts, "FP_ArmsMesh", parent=arm_obj)
    vl.shade_hard_surface(arms_mesh, angle_deg=42.0, weighted_normals=True)
    vl.smart_uv_unwrap([arms_mesh], cube_size=0.35)

    arm_mod = arms_mesh.modifiers.new(name="Armature", type='ARMATURE')
    arm_mod.object = arm_obj
    arm_mod.use_vertex_groups = True

    attach_bone_socket("RightHandWeaponMount", arm_obj, "RightHand", (-0.76, -0.02, 1.43), display_size=0.05)

    renders = vl.render_studio_shots(
        arm_obj, os.path.join(vl.RENDERS_DIR, "characters"), "FP_TacticalArms",
        angles=[
            ("hero", (-1.15, -1.85, 0.65), 55.0, True),
            ("top_fp", (0.0, 0.65, 2.25), 50.0, False),
        ],
        res_qa=(960, 600), res_hero=(1600, 900), samples_qa=40, samples_hero=64
    )

    fbx_path = os.path.join(vl.EXPORTS_DIR, "characters", "FP_TacticalArms.fbx")
    blend_path = os.path.join(vl.SCENES_DIR, "FP_TacticalArms.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    bpy.ops.object.select_all(action='DESELECT')
    for o in [arm_obj] + list(arm_obj.children_recursive):
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj

    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        use_space_transform=True,
        bake_space_transform=False,
        object_types={'EMPTY', 'ARMATURE', 'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        use_tspace=True,
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        use_armature_deform_only=False,
        bake_anim=False,
    )

    me = arms_mesh.data
    me.calc_loop_triangles()
    meta = {
        "name": "FP_TacticalArms",
        "fbx_path": fbx_path,
        "blend_path": blend_path,
        "file_size_bytes": os.path.getsize(fbx_path),
        "triangles": len(me.loop_triangles),
        "vertices": len(me.vertices),
        "bones": [b.name for b in arm_obj.data.bones],
        "sockets": ["RightHandWeaponMount"],
        "textures": tex_dict,
        "renders": renders,
    }
    vl.update_manifest("characters", "FP_TacticalArms", meta)
    print(f"FP_TacticalArms done: {meta['triangles']} tris")


if __name__ == "__main__":
    tex_dict = build_tactical_operator()
    build_fp_tactical_arms(tex_dict)
    print("\n=== ALL CHARACTERS BUILD COMPLETE ===")

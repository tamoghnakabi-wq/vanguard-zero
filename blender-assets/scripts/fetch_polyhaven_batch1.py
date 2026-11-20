"""
VANGUARD ZERO v2.0 — Batch 1: Poly Haven CC0 4K HDRI, 2K Scanned PBR Textures,
and Photogrammetry 3D Models Downloader + Blender 5.2 Unity URP Packer & FBX Exporter.

Run via:
  /Applications/Blender.app/Contents/MacOS/Blender --background --python fetch_polyhaven_batch1.py
"""

import os
import sys
import json
import math
import urllib.request
import concurrent.futures
import bpy
import numpy as np
from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

import vanguard_lib as vl

ROOT = vl.ROOT_DIR
RAW_DIR = os.path.join(ROOT, "polyhaven_raw")
HDRI_DIR = os.path.join(ROOT, "hdri")
PBR_DIR = os.path.join(ROOT, "pbr_scans")
PHOTO_EXPORT_DIR = os.path.join(ROOT, "exports", "photogrammetry")
PHOTO_TEX_DIR = os.path.join(ROOT, "textures", "photogrammetry")
PHOTO_RENDER_DIR = os.path.join(ROOT, "renders", "photogrammetry")

UA = {"User-Agent": "VanguardZeroAssetPipeline/2.0 (Blender 5.2.1)"}

HDRIS = [
    ("evening_road_01_puresky", "4k"),
    ("belfast_sunset_puresky", "4k"),
]

# (polyhaven_id, friendly_name, default_metallic_if_absent)
PBR_TEXTURES = [
    ("asphalt_02", "Asphalt_Weathered", 0.0),
    ("concrete_wall_007", "Concrete_PouredBrutalist", 0.0),
    ("concrete_floor_worn_001", "Concrete_FloorWorn", 0.0),
    ("corrugated_iron", "Metal_CorrugatedSteel", 0.78),
    ("rusty_metal_02", "Metal_RustedStructural", 0.65),
    ("metal_plate", "Metal_BrushedGunmetal", 0.88),
    ("rock_face_03", "Terrain_AlpineCliffRock", 0.0),
    ("aerial_rocks_04", "Terrain_WetAlpineGravel", 0.0),
    ("denim_fabric", "Fabric_TacticalRipstop", 0.0),
]

# 12 real CC0 photogrammetry 3D props for Blacksite-09 + alpine perimeter
PHOTO_MODELS = [
    ("Barrel_01", "PG_SteelBarrel_01"),
    ("barrel_03", "PG_SteelBarrel_02"),
    ("concrete_road_barrier", "PG_ConcreteRoadBarrier"),
    ("wooden_crate_01", "PG_WoodenCrate"),
    ("power_box_01", "PG_IndustrialPowerBox"),
    ("utility_box_02", "PG_UtilityBox"),
    ("exterior_aircon_unit", "PG_ExteriorHVAC"),
    ("security_camera_01", "PG_SecurityCamera"),
    ("modular_chainlink_fence", "PG_ChainlinkFence"),
    ("street_lamp_01", "PG_IndustrialLamp"),
    ("boulder_01", "PG_AlpineBoulder"),
    ("fir_tree_01", "PG_AlpineFirTree"),
]


def fetch_json(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def download_file(url, dest):
    import time
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    if os.path.exists(dest) and os.path.getsize(dest) > 512:
        return dest
    last_err = None
    for attempt in range(4):
        try:
            req = urllib.request.Request(url, headers=UA)
            with urllib.request.urlopen(req, timeout=60) as resp:
                data = resp.read()
            tmp = dest + ".part"
            with open(tmp, "wb") as f:
                f.write(data)
            os.replace(tmp, dest)
            return dest
        except Exception as e:
            last_err = e
            time.sleep(1.0 + attempt * 1.5)
    raise last_err


def load_img_np(path):
    """Load any image format Blender supports (JPG, PNG, EXR) into float32 HxWx4 numpy array [0..1]."""
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size[0], img.size[1]
    arr = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(arr)
    bpy.data.images.remove(img)
    return arr.reshape((h, w, 4))


def resize_nearest_or_match(arr, target_h, target_w):
    h, w = arr.shape[:2]
    if h == target_h and w == target_w:
        return arr
    yi = np.clip((np.linspace(0, h - 1, target_h)).astype(np.int32), 0, h - 1)
    xi = np.clip((np.linspace(0, w - 1, target_w)).astype(np.int32), 0, w - 1)
    return arr[yi[:, None], xi[None, :]]


def pick_url(map_dict, preferred_res=("2k", "1k"), preferred_ext=("png", "jpg", "exr")):
    if not isinstance(map_dict, dict):
        return None, None
    for res in preferred_res:
        if res in map_dict and isinstance(map_dict[res], dict):
            for ext in preferred_ext:
                if ext in map_dict[res] and isinstance(map_dict[res][ext], dict) and "url" in map_dict[res][ext]:
                    return map_dict[res][ext]["url"], ext
    return None, None


def download_hdris():
    print("\n=== [Batch 1A] Downloading 4K Poly Haven Pure Sky HDRIs ===")
    os.makedirs(HDRI_DIR, exist_ok=True)
    results = {}
    for asset_id, res in HDRIS:
        files = fetch_json(f"https://api.polyhaven.com/files/{asset_id}")
        hdri_info = files.get("hdri", {}).get(res, {})
        ext = "hdr" if "hdr" in hdri_info else ("exr" if "exr" in hdri_info else None)
        if not ext:
            print(f"  WARN: No {res} HDRI for {asset_id}")
            continue
        url = hdri_info[ext]["url"]
        dest = os.path.join(HDRI_DIR, f"{asset_id}_{res}.{ext}")
        download_file(url, dest)
        results[asset_id] = {
            "path": dest,
            "resolution": res,
            "format": ext,
            "file_size_bytes": os.path.getsize(dest),
            "source": f"Poly Haven CC0 ({asset_id})",
        }
        print(f"  Downloaded {os.path.basename(dest)} ({os.path.getsize(dest)/1024/1024:.1f} MB)")
    return results


def download_and_pack_pbr_textures():
    print("\n=== [Batch 1B] Downloading & Packing 2K Poly Haven PBR Scans for Unity URP ===")
    os.makedirs(PBR_DIR, exist_ok=True)
    tasks = []
    meta_per_tex = {}

    for asset_id, friendly, default_met in PBR_TEXTURES:
        files = fetch_json(f"https://api.polyhaven.com/files/{asset_id}")
        raw_sub = os.path.join(RAW_DIR, "textures", asset_id)
        os.makedirs(raw_sub, exist_ok=True)

        # Poly Haven keys: Diffuse, nor_gl, Rough, AO, arm (AO+Rough+Metal!), Metal
        wanted = {}
        for k_out, candidates in [
            ("diffuse", ["Diffuse", "col", "Albedo"]),
            ("nor_gl", ["nor_gl", "Normal"]),
            ("rough", ["Rough", "Roughness"]),
            ("ao", ["AO", "ao"]),
            ("arm", ["arm"]),
            ("metal", ["Metal", "Metallic"]),
        ]:
            for c in candidates:
                if c in files:
                    u, ext = pick_url(files[c], ("2k", "1k"), ("jpg", "png", "exr"))
                    if u:
                        wanted[k_out] = (u, os.path.join(raw_sub, f"{asset_id}_{k_out}.{ext}"))
                        break

        meta_per_tex[friendly] = (asset_id, default_met, wanted)
        for u, dst in wanted.values():
            tasks.append((u, dst))

    print(f"  Downloading {len(tasks)} texture map files in parallel...")
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as ex:
        futs = [ex.submit(download_file, u, dst) for u, dst in tasks]
        for f in concurrent.futures.as_completed(futs):
            f.result()

    packed = {}
    for friendly, (asset_id, default_met, wanted) in meta_per_tex.items():
        out_sub = os.path.join(PBR_DIR, friendly)
        os.makedirs(out_sub, exist_ok=True)
        p_alb = os.path.join(out_sub, f"{friendly}_Albedo.png")
        p_nrm = os.path.join(out_sub, f"{friendly}_Normal.png")
        p_ms = os.path.join(out_sub, f"{friendly}_MetallicSmoothness.png")
        p_ao = os.path.join(out_sub, f"{friendly}_AO.png")
        if all(os.path.exists(p) and os.path.getsize(p) > 5000 for p in (p_alb, p_nrm, p_ms, p_ao)):
            packed[friendly] = {
                "albedo": p_alb, "normal": p_nrm, "metallic_smoothness": p_ms, "ao": p_ao,
                "source": f"Poly Haven CC0 ({asset_id})", "resolution": "2048x2048",
            }
            continue
        print(f"  Packing {friendly} ({asset_id}) -> Unity URP PNGs...")

        alb = load_img_np(wanted["diffuse"][1])[:, :, :3]
        h, w = alb.shape[:2]

        # Normal GL (+Y)
        if "nor_gl" in wanted:
            nrm = resize_nearest_or_match(load_img_np(wanted["nor_gl"][1])[:, :, :3], h, w)
        else:
            nrm = np.full((h, w, 3), [0.5, 0.5, 1.0], dtype=np.float32)

        # Roughness, AO, Metallic (prefer dedicated maps, fallback to ARM = R:AO, G:Rough, B:Metal)
        arm = resize_nearest_or_match(load_img_np(wanted["arm"][1]), h, w) if "arm" in wanted else None

        if "rough" in wanted:
            rough = resize_nearest_or_match(load_img_np(wanted["rough"][1]), h, w)[:, :, 0]
        elif arm is not None:
            rough = arm[:, :, 1]
        else:
            rough = np.full((h, w), 0.65, dtype=np.float32)

        if "ao" in wanted:
            ao = resize_nearest_or_match(load_img_np(wanted["ao"][1]), h, w)[:, :, 0]
        elif arm is not None:
            ao = arm[:, :, 0]
        else:
            ao = np.ones((h, w), dtype=np.float32)

        if "metal" in wanted:
            met = resize_nearest_or_match(load_img_np(wanted["metal"][1]), h, w)[:, :, 0]
        elif arm is not None and default_met > 0.05:
            met = np.clip(arm[:, :, 2] * 0.7 + default_met * 0.3, 0.0, 1.0)
        else:
            met = np.full((h, w), default_met, dtype=np.float32)

        smooth = np.clip(1.0 - rough, 0.02, 0.98)
        met_smooth = np.stack([met, met, met, smooth], axis=-1)

        paths = {
            "albedo": vl.save_png(os.path.join(out_sub, f"{friendly}_Albedo.png"), alb, 'sRGB'),
            "normal": vl.save_png(os.path.join(out_sub, f"{friendly}_Normal.png"), nrm, 'Non-Color'),
            "metallic_smoothness": vl.save_png(os.path.join(out_sub, f"{friendly}_MetallicSmoothness.png"), met_smooth, 'Non-Color'),
            "ao": vl.save_png(os.path.join(out_sub, f"{friendly}_AO.png"), ao, 'Non-Color'),
            "source": f"Poly Haven CC0 ({asset_id})",
            "resolution": f"{w}x{h}",
        }
        packed[friendly] = paths
    return packed


def download_and_convert_photogrammetry_models():
    print("\n=== [Batch 1C] Downloading & Converting 12 Real Photogrammetry 3D Models via Blender 5.2 ===")
    os.makedirs(PHOTO_EXPORT_DIR, exist_ok=True)
    os.makedirs(PHOTO_TEX_DIR, exist_ok=True)
    os.makedirs(PHOTO_RENDER_DIR, exist_ok=True)

    # First download all GLTF + includes in parallel
    gltf_paths = {}
    dl_tasks = []
    for asset_id, friendly in PHOTO_MODELS:
        files = fetch_json(f"https://api.polyhaven.com/files/{asset_id}")
        gltf_dict = files.get("gltf", {})
        res = "2k" if "2k" in gltf_dict else ("1k" if "1k" in gltf_dict else next(iter(gltf_dict.keys()), None))
        if not res or "gltf" not in gltf_dict[res]:
            print(f"  WARN: No GLTF for {asset_id}")
            continue
        entry = gltf_dict[res]["gltf"]
        raw_sub = os.path.join(RAW_DIR, "models", asset_id)
        os.makedirs(raw_sub, exist_ok=True)
        main_gltf = os.path.join(raw_sub, os.path.basename(entry["url"]))
        dl_tasks.append((entry["url"], main_gltf))
        for rel_path, inc_info in entry.get("include", {}).items():
            dst = os.path.join(raw_sub, rel_path)
            dl_tasks.append((inc_info["url"], dst))
        gltf_paths[friendly] = (asset_id, main_gltf, raw_sub)

    print(f"  Downloading {len(dl_tasks)} photogrammetry GLTF/bin/texture files in parallel...")
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as ex:
        futs = [ex.submit(download_file, u, dst) for u, dst in dl_tasks]
        for f in concurrent.futures.as_completed(futs):
            f.result()

    results = {}
    for friendly, (asset_id, gltf_file, raw_sub) in gltf_paths.items():
        print(f"  Processing {friendly} ({asset_id})...")
        vl.clear_scene()
        bpy.ops.import_scene.gltf(filepath=gltf_file)

        mesh_objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        if not mesh_objs:
            print(f"  WARN: No mesh imported for {asset_id}")
            continue

        # Clear any parent transforms while preserving world transforms, then ground-align on Z=0
        root = vl.create_empty(friendly, (0, 0, 0), display_size=0.3)
        for o in list(bpy.context.scene.objects):
            if o != root and o.parent is None:
                o.parent = root
        bpy.context.view_layer.update()

        mins, maxs = vl._get_hierarchy_bounds(root)
        cx = 0.5 * (mins.x + maxs.x)
        cy = 0.5 * (mins.y + maxs.y)
        shift = Vector((-cx, -cy, -mins.z))
        # Shift direct children of root so root stays at (0,0,0) and model sits cleanly centered on Z=0!
        for c in list(root.children):
            c.location += shift
        bpy.context.view_layer.update()

        # Optimize excessive photogrammetry polycounts cleanly (keep <= 18k tris for props, <= 24k for trees!)
        total_tris = 0
        for m in mesh_objs:
            m.data.calc_loop_triangles()
            total_tris += len(m.data.loop_triangles)
        budget = 22000 if "Tree" in friendly else 14000
        if total_tris > budget:
            ratio = max(0.15, budget / float(total_tris))
            for m in mesh_objs:
                bpy.ops.object.select_all(action='DESELECT')
                m.select_set(True)
                bpy.context.view_layer.objects.active = m
                dec = m.modifiers.new("Decimate", type='DECIMATE')
                dec.ratio = ratio
                bpy.ops.object.modifier_apply(modifier=dec.name)

        # Pack textures from raw_sub/textures into Unity URP format!
        out_tex_sub = os.path.join(PHOTO_TEX_DIR, friendly)
        os.makedirs(out_tex_sub, exist_ok=True)
        tex_files_raw = []
        for dirpath, _, filenames in os.walk(raw_sub):
            for fn in filenames:
                if fn.lower().endswith((".jpg", ".jpeg", ".png", ".exr")):
                    tex_files_raw.append(os.path.join(dirpath, fn))

        # Group by texture prefix (diffuse / nor_gl / arm / rough)
        diff_f = next((p for p in tex_files_raw if "diff" in os.path.basename(p).lower()), None)
        norm_f = next((p for p in tex_files_raw if "nor_gl" in os.path.basename(p).lower() or "normal" in os.path.basename(p).lower()), None)
        arm_f = next((p for p in tex_files_raw if "arm" in os.path.basename(p).lower()), None)
        rough_f = next((p for p in tex_files_raw if "rough" in os.path.basename(p).lower()), None)

        tex_entry = {}
        if diff_f:
            alb_rgba = load_img_np(diff_f)
            h, w = alb_rgba.shape[:2]
            # Preserve alpha channel for foliage/fences!
            has_alpha = np.min(alb_rgba[:, :, 3]) < 0.99
            tex_entry["albedo"] = vl.save_png(
                os.path.join(out_tex_sub, f"{friendly}_Albedo.png"),
                alb_rgba if has_alpha else alb_rgba[:, :, :3], 'sRGB'
            )
            if norm_f:
                nrm = resize_nearest_or_match(load_img_np(norm_f)[:, :, :3], h, w)
                tex_entry["normal"] = vl.save_png(os.path.join(out_tex_sub, f"{friendly}_Normal.png"), nrm, 'Non-Color')
            if arm_f:
                arm = resize_nearest_or_match(load_img_np(arm_f), h, w)
                ao = arm[:, :, 0]
                smooth = np.clip(1.0 - arm[:, :, 1], 0.02, 0.96)
                met = arm[:, :, 2]
                ms = np.stack([met, met, met, smooth], axis=-1)
                tex_entry["metallic_smoothness"] = vl.save_png(os.path.join(out_tex_sub, f"{friendly}_MetallicSmoothness.png"), ms, 'Non-Color')
                tex_entry["ao"] = vl.save_png(os.path.join(out_tex_sub, f"{friendly}_AO.png"), ao, 'Non-Color')
            elif rough_f:
                r = resize_nearest_or_match(load_img_np(rough_f), h, w)[:, :, 0]
                smooth = np.clip(1.0 - r, 0.02, 0.96)
                met = np.zeros_like(smooth)
                ms = np.stack([met, met, met, smooth], axis=-1)
                tex_entry["metallic_smoothness"] = vl.save_png(os.path.join(out_tex_sub, f"{friendly}_MetallicSmoothness.png"), ms, 'Non-Color')
            # Also copy any secondary texture sets (e.g. tree leaves vs bark!) untouched so Unity has 100% of maps
            extra_copied = []
            for raw_p in tex_files_raw:
                bn = os.path.basename(raw_p)
                if raw_p not in (diff_f, norm_f, arm_f, rough_f):
                    dst_p = os.path.join(out_tex_sub, bn)
                    if not os.path.exists(dst_p):
                        with open(raw_p, "rb") as sf, open(dst_p, "wb") as df:
                            df.write(sf.read())
                    extra_copied.append(dst_p)
            if extra_copied:
                tex_entry["extra_maps"] = extra_copied

        # Render quick Cycles studio QA thumbnail for visual verification
        renders = vl.render_studio_shots(
            root, PHOTO_RENDER_DIR, friendly,
            angles=[("hero", (2.1, -1.9, 0.85), 48.0, True)],
            res_hero=(800, 600), samples_hero=32
        )

        # Export Unity-ready FBX (embed_textures=True for multi-material GLTFs like trees/fences so Unity extracts all sub-materials automatically!)
        fbx_path = os.path.join(PHOTO_EXPORT_DIR, f"{friendly}.fbx")
        blend_path = os.path.join(vl.SCENES_DIR, f"{friendly}.blend")
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)

        bpy.ops.object.select_all(action='DESELECT')
        all_objs = [root] + list(root.children_recursive)
        for o in all_objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = root

        bpy.ops.export_scene.fbx(
            filepath=fbx_path,
            use_selection=True,
            global_scale=1.0,
            apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL',
            axis_forward='-Z',
            axis_up='Y',
            use_space_transform=True,
            bake_space_transform=True,
            object_types={'EMPTY', 'MESH'},
            use_mesh_modifiers=True,
            mesh_smooth_type='FACE',
            use_tspace=True,
            bake_anim=False,
            path_mode='COPY',
            embed_textures=True,
        )

        final_tris = 0
        mats = []
        for o in all_objs:
            if o.type == 'MESH':
                o.data.calc_loop_triangles()
                final_tris += len(o.data.loop_triangles)
                for m in o.data.materials:
                    if m and m.name not in mats:
                        mats.append(m.name)
        mins2, maxs2 = vl._get_hierarchy_bounds(root)
        sz = maxs2 - mins2
        meta = {
            "name": friendly,
            "source": f"Poly Haven CC0 ({asset_id})",
            "fbx_path": fbx_path,
            "blend_path": blend_path,
            "file_size_bytes": os.path.getsize(fbx_path),
            "triangles": final_tris,
            "unity_bounds_meters": {
                "width_x": round(sz.x, 3),
                "height_y": round(sz.z, 3),
                "length_z": round(sz.y, 3),
            },
            "materials": mats,
            "embedded_textures": True,
            "textures": tex_entry,
            "renders": renders,
        }
        vl.update_manifest("photogrammetry", friendly, meta)
        results[friendly] = meta
        print(f"    -> {friendly}.fbx: {final_tris} tris, {meta['file_size_bytes']/1024:.0f} KB, bounds={meta['unity_bounds_meters']}")

    return results


if __name__ == "__main__":
    hdris = download_hdris()
    pbr = download_and_pack_pbr_textures()
    models = download_and_convert_photogrammetry_models()

    # Store HDRIs & PBR Scans in manifest.json too
    with open(vl.MANIFEST_PATH, "r") as f:
        m = json.load(f)
    m["v2_photorealism"] = {
        "hdris": hdris,
        "pbr_scans": pbr,
    }
    with open(vl.MANIFEST_PATH, "w") as f:
        json.dump(m, f, indent=2)
    print("\n=== BATCH 1 COMPLETE ===")

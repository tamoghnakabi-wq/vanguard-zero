#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace VanguardZero.Animation.Editor
{
    /// <summary>
    /// Imports all 18 NVIDIA MotionBricks-SOMA-RP-v1 neural animation clips (`*.mbclip.json`)
    /// into native Unity 60 Hz `.anim` assets retargeted with 0-roll-error onto `TacticalOperator.fbx`'s
    /// live rest-pose bone transforms, bakes neural FootContact curves & Footstep AnimationEvents,
    /// and generates `UpperBody_CombatMask.mask` + `VanguardOperator.controller`.
    ///
    /// CLI Entrypoint:
    ///   VanguardZero.Animation.Editor.MotionBricksUnityImporter.ImportAllFromMotionPipeline
    /// </summary>
    public static class MotionBricksUnityImporter
    {
        private const string OutputRoot = "Assets/Animation/MotionBricks";
        private const string ClipsDir = OutputRoot + "/Clips";
        private const string MasksDir = OutputRoot + "/Masks";
        private const string ControllersDir = OutputRoot + "/Controllers";

        private static readonly (string bone, string parent)[] CanonicalHierarchy =
        {
            ("Hips", null),
            ("Spine", "Hips"),
            ("Chest", "Spine"),
            ("UpperChest", "Chest"),
            ("Neck", "UpperChest"),
            ("Head", "Neck"),
            ("LeftShoulder", "UpperChest"),
            ("LeftUpperArm", "LeftShoulder"),
            ("LeftLowerArm", "LeftUpperArm"),
            ("LeftHand", "LeftLowerArm"),
            ("RightShoulder", "UpperChest"),
            ("RightUpperArm", "RightShoulder"),
            ("RightLowerArm", "RightUpperArm"),
            ("RightHand", "RightLowerArm"),
            ("LeftUpperLeg", "Hips"),
            ("LeftLowerLeg", "LeftUpperLeg"),
            ("LeftFoot", "LeftLowerLeg"),
            ("LeftToes", "LeftFoot"),
            ("RightUpperLeg", "Hips"),
            ("RightLowerLeg", "RightUpperLeg"),
            ("RightFoot", "RightLowerLeg"),
            ("RightToes", "RightFoot"),
        };

        [MenuItem("VANGUARD ZERO/MotionBricks/Import All Motion Clips & Build Animator")]
        public static void ImportAllFromMotionPipeline()
        {
            string sourceDir = ResolveMbClipsDirectory();
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                Debug.LogError("[MotionBricksImporter] Could not locate mbclips directory. Expected Assets/Animation/MotionBricks/Data or ../motion-pipeline/exports/mbclips.");
                return;
            }

            EnsureFolder(OutputRoot);
            EnsureFolder(ClipsDir);
            EnsureFolder(MasksDir);
            EnsureFolder(ControllersDir);

            RigPoseReference rigRef = InspectOperatorRigReference();
            Debug.Log($"[MotionBricksImporter] Using rig reference: {rigRef.SourceDescription} (rootPrefix='{rigRef.RootPrefix}', hipsRestY={rigRef.HipsRestY:F3}m)");

            string[] files = Directory.GetFiles(sourceDir, "*.mbclip.json");
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0)
            {
                Debug.LogError($"[MotionBricksImporter] No *.mbclip.json files found in {sourceDir}");
                return;
            }

            var clipsByName = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                string jsonText = File.ReadAllText(file, Encoding.UTF8);
                var parsed = ParseMbClip(jsonText);
                AnimationClip clip = BuildUnityClip(parsed, rigRef);
                string assetPath = $"{ClipsDir}/{parsed.Name}.anim";

                AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(clip, existing);
                    EditorUtility.SetDirty(existing);
                    clipsByName[parsed.Name] = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(clip, assetPath);
                    clipsByName[parsed.Name] = clip;
                }
            }

            AvatarMask upperMask = BuildUpperBodyMask(rigRef);
            AnimatorController controller = BuildAnimatorController(clipsByName, upperMask);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MotionBricksImporter] SUCCESS: Imported {clipsByName.Count} MotionBricks clips, built {AssetDatabase.GetAssetPath(upperMask)} and {AssetDatabase.GetAssetPath(controller)}.");
        }

        private static string ResolveMbClipsDirectory()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] candidates =
            {
                Path.Combine(Application.dataPath, "Animation", "MotionBricks", "Data"),
                Path.Combine(projectRoot, "..", "motion-pipeline", "exports", "mbclips"),
            };
            foreach (string c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (Directory.Exists(full) && Directory.GetFiles(full, "*.mbclip.json").Length > 0)
                    return full;
            }
            return null;
        }

        private sealed class RigPoseReference
        {
            public string SourceDescription = "Canonical T-Pose Fallback";
            public string RootPrefix = "Armature/";
            public float HipsRestY = 0.96f;
            public readonly Dictionary<string, string> BonePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, Quaternion> RestWorldRot = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            public readonly Dictionary<string, Vector3> RestLocalPos = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            public Quaternion RootParentWorldRot = Quaternion.identity;
        }

        private static RigPoseReference InspectOperatorRigReference()
        {
            var rigRef = new RigPoseReference();

            // Populate canonical defaults first
            var canonicalPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (bone, parent) in CanonicalHierarchy)
            {
                canonicalPaths[bone] = parent == null ? $"Armature/{bone}" : $"{canonicalPaths[parent]}/{bone}";
                rigRef.BonePaths[bone] = canonicalPaths[bone];
                rigRef.RestWorldRot[bone] = Quaternion.identity;
                rigRef.RestLocalPos[bone] = bone == "Hips" ? new Vector3(0f, 0.96f, 0f) : Vector3.zero;
            }

            // Look for TacticalOperator.fbx in Assets
            string[] guids = AssetDatabase.FindAssets("TacticalOperator t:Model");
            if (guids == null || guids.Length == 0)
                return rigRef;

            string fbxPath = AssetDatabase.GUIDToAssetPath(guids[0]);

            // IMPORTANT: MotionBricks clips write direct 1-to-1 Transform localPosition/localRotation
            // curves matched to TacticalOperator.fbx's exact 22-bone hierarchy. Unity's Animator ignores
            // Transform curves on Humanoid avatars, so enforce Generic + CreateFromThisModel Avatar here!
            if (AssetImporter.GetAtPath(fbxPath) is ModelImporter modelImporter)
            {
                bool needsReimport = false;
                if (modelImporter.animationType != ModelImporterAnimationType.Generic)
                {
                    Debug.Log($"[MotionBricksImporter] Configuring '{fbxPath}' from {modelImporter.animationType} -> Generic Avatar so Transform-curve MotionBricks clips drive all 22 bones cleanly.");
                    modelImporter.animationType = ModelImporterAnimationType.Generic;
                    needsReimport = true;
                }
                if (modelImporter.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                {
                    modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    needsReimport = true;
                }
                if (needsReimport)
                    modelImporter.SaveAndReimport();
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (prefab == null)
                return rigRef;

            GameObject temp = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var found = new Dictionary<string, Transform>(StringComparer.Ordinal);
                CollectTransforms(temp.transform, found);

                if (found.TryGetValue("Hips", out Transform hipsT))
                {
                    rigRef.SourceDescription = fbxPath;
                    rigRef.HipsRestY = hipsT.position.y > 0.2f ? hipsT.position.y : 0.96f;
                    rigRef.RootParentWorldRot = hipsT.parent != null ? hipsT.parent.rotation : Quaternion.identity;

                    string hipsPath = GetRelativePath(temp.transform, hipsT);
                    int slash = hipsPath.LastIndexOf('/');
                    rigRef.RootPrefix = slash >= 0 ? hipsPath.Substring(0, slash + 1) : "";

                    foreach (var (bone, _) in CanonicalHierarchy)
                    {
                        if (found.TryGetValue(bone, out Transform bt))
                        {
                            rigRef.BonePaths[bone] = GetRelativePath(temp.transform, bt);
                            rigRef.RestWorldRot[bone] = bt.rotation;
                            rigRef.RestLocalPos[bone] = bt.localPosition;
                        }
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temp);
            }

            return rigRef;
        }

        private static AnimationClip BuildUnityClip(ParsedClip data, RigPoseReference rigRef)
        {
            var clip = new AnimationClip
            {
                name = data.Name,
                frameRate = data.Fps,
                legacy = false,
            };

            int frameCount = data.FrameCount;
            int boneCount = data.BoneNames.Length;
            float dt = 1f / Mathf.Max(data.Fps, 1f);
            float hipsScale = rigRef.HipsRestY / 0.96f;

            var nameToIdx = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < boneCount; i++)
                nameToIdx[data.BoneNames[i]] = i;

            var parentMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (b, p) in CanonicalHierarchy)
                parentMap[b] = p;

            // Hips localPosition curves
            var px = new Keyframe[frameCount];
            var py = new Keyframe[frameCount];
            var pz = new Keyframe[frameCount];

            // Per-bone localRotation curves
            var rx = new Keyframe[boneCount][];
            var ry = new Keyframe[boneCount][];
            var rz = new Keyframe[boneCount][];
            var rw = new Keyframe[boneCount][];
            for (int bi = 0; bi < boneCount; bi++)
            {
                rx[bi] = new Keyframe[frameCount];
                ry[bi] = new Keyframe[frameCount];
                rz[bi] = new Keyframe[frameCount];
                rw[bi] = new Keyframe[frameCount];
            }

            // Neural foot-contact curves for MotionBricksFootIK
            var leftContactKeys = new Keyframe[frameCount];
            var rightContactKeys = new Keyframe[frameCount];

            var worldRots = new Quaternion[boneCount];
            var prevLocalQ = new Quaternion[boneCount];
            Quaternion invRootParent = Quaternion.Inverse(rigRef.RootParentWorldRot);

            for (int f = 0; f < frameCount; f++)
            {
                float t = f * dt;

                // Hips position in root parent local space
                Vector3 hipsWorldPos = data.HipsPositions[f] * hipsScale;
                Vector3 hipsLocalPos = invRootParent * hipsWorldPos;
                px[f] = new Keyframe(t, hipsLocalPos.x);
                py[f] = new Keyframe(t, hipsLocalPos.y);
                pz[f] = new Keyframe(t, hipsLocalPos.z);

                // Compute exact world orientation for every bone:
                // worldRot[b] = deltaWorldRot[f, b] * restWorldRot[b]
                for (int bi = 0; bi < boneCount; bi++)
                {
                    string bName = data.BoneNames[bi];
                    Quaternion restW = rigRef.RestWorldRot.TryGetValue(bName, out Quaternion rw0) ? rw0 : Quaternion.identity;
                    Quaternion deltaW = data.WorldDeltaQuats[f][bi];
                    worldRots[bi] = NormalizeSafe(deltaW * restW);
                }

                // Convert world orientations -> exact parent-relative localRotation with hemisphere continuity
                for (int bi = 0; bi < boneCount; bi++)
                {
                    string bName = data.BoneNames[bi];
                    Quaternion parentW = rigRef.RootParentWorldRot;
                    if (parentMap.TryGetValue(bName, out string pName) && pName != null && nameToIdx.TryGetValue(pName, out int pIdx))
                        parentW = worldRots[pIdx];

                    Quaternion localQ = NormalizeSafe(Quaternion.Inverse(parentW) * worldRots[bi]);
                    if (f > 0 && Quaternion.Dot(prevLocalQ[bi], localQ) < 0f)
                        localQ = new Quaternion(-localQ.x, -localQ.y, -localQ.z, -localQ.w);
                    prevLocalQ[bi] = localQ;

                    rx[bi][f] = new Keyframe(t, localQ.x);
                    ry[bi][f] = new Keyframe(t, localQ.y);
                    rz[bi][f] = new Keyframe(t, localQ.z);
                    rw[bi][f] = new Keyframe(t, localQ.w);
                }

                // Neural foot contacts: max(heel, toe) per foot
                Vector4 fc = data.FootContacts[f];
                leftContactKeys[f] = new Keyframe(t, Mathf.Clamp01(Mathf.Max(fc.x, fc.y)));
                rightContactKeys[f] = new Keyframe(t, Mathf.Clamp01(Mathf.Max(fc.z, fc.w)));
            }

            if (rigRef.BonePaths.TryGetValue("Hips", out string hipsPath))
            {
                clip.SetCurve(hipsPath, typeof(Transform), "localPosition.x", new AnimationCurve(px));
                clip.SetCurve(hipsPath, typeof(Transform), "localPosition.y", new AnimationCurve(py));
                clip.SetCurve(hipsPath, typeof(Transform), "localPosition.z", new AnimationCurve(pz));
            }

            for (int bi = 0; bi < boneCount; bi++)
            {
                string bName = data.BoneNames[bi];
                if (!rigRef.BonePaths.TryGetValue(bName, out string bPath))
                    continue;
                clip.SetCurve(bPath, typeof(Transform), "localRotation.x", new AnimationCurve(rx[bi]));
                clip.SetCurve(bPath, typeof(Transform), "localRotation.y", new AnimationCurve(ry[bi]));
                clip.SetCurve(bPath, typeof(Transform), "localRotation.z", new AnimationCurve(rz[bi]));
                clip.SetCurve(bPath, typeof(Transform), "localRotation.w", new AnimationCurve(rw[bi]));
            }

            // Custom Animator float curves read by MotionBricksFootIK
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "LeftFootContact"), new AnimationCurve(leftContactKeys));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RightFootContact"), new AnimationCurve(rightContactKeys));

            // Neural footstep AnimationEvents
            if (data.FootstepEvents != null && data.FootstepEvents.Count > 0)
            {
                var animEvents = new AnimationEvent[data.FootstepEvents.Count];
                for (int i = 0; i < data.FootstepEvents.Count; i++)
                {
                    animEvents[i] = new AnimationEvent
                    {
                        time = data.FootstepEvents[i].Time,
                        functionName = "OnMotionBricksFootstep",
                        stringParameter = data.FootstepEvents[i].Foot,
                        floatParameter = data.FootstepEvents[i].Intensity,
                    };
                }
                AnimationUtility.SetAnimationEvents(clip, animEvents);
            }

            clip.EnsureQuaternionContinuity();

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = data.IsLoop;
            settings.loopBlend = data.IsLoop;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            return clip;
        }

        private static AvatarMask BuildUpperBodyMask(RigPoseReference rigRef)
        {
            string maskPath = $"{MasksDir}/UpperBody_CombatMask.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, maskPath);
            }

            // Humanoid body parts
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);

            // Transform mask for Generic rigs as well
            var upperBones = new HashSet<string>(StringComparer.Ordinal)
            {
                "Spine", "Chest", "UpperChest", "Neck", "Head",
                "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
                "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            };
            mask.transformCount = CanonicalHierarchy.Length;
            for (int i = 0; i < CanonicalHierarchy.Length; i++)
            {
                string bName = CanonicalHierarchy[i].bone;
                mask.SetTransformPath(i, rigRef.BonePaths[bName]);
                mask.SetTransformActive(i, upperBones.Contains(bName));
            }

            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static AnimatorController BuildAnimatorController(Dictionary<string, AnimationClip> clips, AvatarMask upperMask)
        {
            string ctrlPath = $"{ControllersDir}/VanguardOperator.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) != null)
                AssetDatabase.DeleteAsset(ctrlPath);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);

            // Parameters
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "LocomotionSpeedMult", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter("IsCrouching", AnimatorControllerParameterType.Bool);
            controller.AddParameter(new AnimatorControllerParameter { name = "IsGrounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            controller.AddParameter("IsSliding", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("JumpVault", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("TurnLeft90", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("TurnRight90", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HitReact", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("LeftFootContact", AnimatorControllerParameterType.Float);
            controller.AddParameter("RightFootContact", AnimatorControllerParameterType.Float);

            // Layer 0: Base Locomotion
            AnimatorStateMachine rootSm = controller.layers[0].stateMachine;

            // Standing 2D Freeform Directional BlendTree
            BlendTree standingTree;
            AnimatorState standingState = controller.CreateBlendTreeInController("Locomotion_Standing", out standingTree, 0);
            standingTree.blendType = BlendTreeType.FreeformDirectional2D;
            standingTree.blendParameter = "MoveX";
            standingTree.blendParameterY = "MoveZ";
            AddChildIfPresent(standingTree, clips, "Tactical_Idle_Aim", new Vector2(0f, 0f));
            AddChildIfPresent(standingTree, clips, "Tactical_Walk_Fwd", new Vector2(0f, 0.5f));
            AddChildIfPresent(standingTree, clips, "Tactical_Jog_Fwd", new Vector2(0f, 0.75f));
            AddChildIfPresent(standingTree, clips, "Tactical_Sprint_Fwd", new Vector2(0f, 1.0f));
            AddChildIfPresent(standingTree, clips, "Tactical_Walk_Bwd", new Vector2(0f, -0.5f));
            AddChildIfPresent(standingTree, clips, "Tactical_Strafe_Left", new Vector2(-0.6f, 0f));
            AddChildIfPresent(standingTree, clips, "Tactical_Strafe_Right", new Vector2(0.6f, 0f));
            standingState.speedParameterActive = true;
            standingState.speedParameter = "LocomotionSpeedMult";
            rootSm.defaultState = standingState;

            // Crouching 2D Freeform Directional BlendTree
            BlendTree crouchTree;
            AnimatorState crouchState = controller.CreateBlendTreeInController("Locomotion_Crouching", out crouchTree, 0);
            crouchTree.blendType = BlendTreeType.FreeformDirectional2D;
            crouchTree.blendParameter = "MoveX";
            crouchTree.blendParameterY = "MoveZ";
            AddChildIfPresent(crouchTree, clips, "Tactical_Crouch_Idle", new Vector2(0f, 0f));
            AddChildIfPresent(crouchTree, clips, "Tactical_Crouch_Walk_Fwd", new Vector2(0f, 0.5f));
            AddChildIfPresent(crouchTree, clips, "Tactical_Crouch_Walk_Bwd", new Vector2(0f, -0.5f));
            AddChildIfPresent(crouchTree, clips, "Tactical_Crouch_Strafe_Left", new Vector2(-0.5f, 0f));
            AddChildIfPresent(crouchTree, clips, "Tactical_Crouch_Strafe_Right", new Vector2(0.5f, 0f));
            crouchState.speedParameterActive = true;
            crouchState.speedParameter = "LocomotionSpeedMult";

            // Tactical Action States
            AnimatorState slideState = AddClipState(rootSm, "Tactical_Slide", clips);
            AnimatorState jumpState = AddClipState(rootSm, "Tactical_Jump_Vault", clips);
            AnimatorState turnLState = AddClipState(rootSm, "Tactical_Turn_Left_90", clips);
            AnimatorState turnRState = AddClipState(rootSm, "Tactical_Turn_Right_90", clips);
            AnimatorState deathState = AddClipState(rootSm, "Tactical_Death_Fall", clips);

            // Standing <-> Crouching
            AddBoolTransition(standingState, crouchState, "IsCrouching", true, 0.14f);
            AddBoolTransition(crouchState, standingState, "IsCrouching", false, 0.14f);

            // Slide
            AddBoolTransition(standingState, slideState, "IsSliding", true, 0.08f);
            AddBoolTransition(slideState, crouchState, "IsSliding", false, 0.14f);
            AddExitTransition(slideState, crouchState, 0.88f, 0.14f);

            // Jump / Vault
            AddTriggerTransition(standingState, jumpState, "JumpVault", 0.08f);
            AddTriggerTransition(crouchState, jumpState, "JumpVault", 0.08f);
            AddExitTransition(jumpState, standingState, 0.85f, 0.14f);

            // Turn-in-Place
            AddTriggerTransition(standingState, turnLState, "TurnLeft90", 0.10f);
            AddTriggerTransition(standingState, turnRState, "TurnRight90", 0.10f);
            AddExitTransition(turnLState, standingState, 0.85f, 0.12f);
            AddExitTransition(turnRState, standingState, 0.85f, 0.12f);

            // Death (from AnyState)
            var toDeath = rootSm.AddAnyStateTransition(deathState);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.08f;
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");

            // Layer 1: Upper-Body Combat Hit-React Layer
            var hitLayer = new AnimatorControllerLayer
            {
                name = "CombatHitReact",
                defaultWeight = 1f,
                avatarMask = upperMask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = new AnimatorStateMachine { name = "CombatHitReact", hideFlags = HideFlags.HideInHierarchy },
            };
            AssetDatabase.AddObjectToAsset(hitLayer.stateMachine, controller);
            AnimatorState emptyCombat = hitLayer.stateMachine.AddState("Combat_Empty");
            AnimatorState hitReactState = AddClipState(hitLayer.stateMachine, "Tactical_HitReact_Torso", clips);
            hitLayer.stateMachine.defaultState = emptyCombat;
            AddTriggerTransition(emptyCombat, hitReactState, "HitReact", 0.04f);
            AddExitTransition(hitReactState, emptyCombat, 0.90f, 0.12f);
            controller.AddLayer(hitLayer);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState AddClipState(AnimatorStateMachine sm, string clipName, Dictionary<string, AnimationClip> clips)
        {
            AnimatorState st = sm.AddState(clipName);
            if (clips.TryGetValue(clipName, out AnimationClip clip))
                st.motion = clip;
            return st;
        }

        private static void AddChildIfPresent(BlendTree tree, Dictionary<string, AnimationClip> clips, string clipName, Vector2 pos)
        {
            if (clips.TryGetValue(clipName, out AnimationClip clip))
                tree.AddChild(clip, pos);
        }

        private static void AddBoolTransition(AnimatorState from, AnimatorState to, string param, bool value, float duration)
        {
            var tr = from.AddTransition(to);
            tr.hasExitTime = false;
            tr.duration = duration;
            tr.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
        }

        private static void AddTriggerTransition(AnimatorState from, AnimatorState to, string trigger, float duration)
        {
            var tr = from.AddTransition(to);
            tr.hasExitTime = false;
            tr.duration = duration;
            tr.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void AddExitTransition(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            var tr = from.AddTransition(to);
            tr.hasExitTime = true;
            tr.exitTime = exitTime;
            tr.duration = duration;
        }

        private static Quaternion NormalizeSafe(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return mag > 1e-6f ? new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag) : Quaternion.identity;
        }

        private static void CollectTransforms(Transform current, Dictionary<string, Transform> map)
        {
            if (!map.ContainsKey(current.name))
                map[current.name] = current;
            for (int i = 0; i < current.childCount; i++)
                CollectTransforms(current.GetChild(i), map);
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            Transform cur = target;
            while (cur != null && cur != root)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string folder = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder);
        }

        // =========================================================================
        // Zero-Dependency Lightweight JSON Parser for *.mbclip.json
        // =========================================================================
        private sealed class ParsedFootstep { public float Time; public string Foot; public float Intensity; }
        private sealed class ParsedClip
        {
            public string Name;
            public float Fps;
            public int FrameCount;
            public bool IsLoop;
            public string[] BoneNames;
            public Vector3[] HipsPositions;
            public Quaternion[][] WorldDeltaQuats;
            public Vector4[] FootContacts;
            public List<ParsedFootstep> FootstepEvents = new List<ParsedFootstep>();
        }

        private static ParsedClip ParseMbClip(string json)
        {
            var root = (Dictionary<string, object>)MiniJson.Deserialize(json);
            var clip = new ParsedClip
            {
                Name = Convert.ToString(root["name"], CultureInfo.InvariantCulture),
                Fps = Convert.ToSingle(root["fps"], CultureInfo.InvariantCulture),
                FrameCount = Convert.ToInt32(root["frameCount"], CultureInfo.InvariantCulture),
                IsLoop = Convert.ToBoolean(root["isLoop"], CultureInfo.InvariantCulture),
            };

            var bList = (List<object>)root["boneNames"];
            clip.BoneNames = new string[bList.Count];
            for (int i = 0; i < bList.Count; i++)
                clip.BoneNames[i] = Convert.ToString(bList[i], CultureInfo.InvariantCulture);

            var hpList = (List<object>)root["hipsPositions"];
            clip.HipsPositions = new Vector3[hpList.Count];
            for (int f = 0; f < hpList.Count; f++)
            {
                var v = (List<object>)hpList[f];
                clip.HipsPositions[f] = new Vector3(ToF(v[0]), ToF(v[1]), ToF(v[2]));
            }

            var qFrames = (List<object>)root["worldDeltaQuats"];
            clip.WorldDeltaQuats = new Quaternion[qFrames.Count][];
            for (int f = 0; f < qFrames.Count; f++)
            {
                var bArr = (List<object>)qFrames[f];
                clip.WorldDeltaQuats[f] = new Quaternion[bArr.Count];
                for (int b = 0; b < bArr.Count; b++)
                {
                    var q = (List<object>)bArr[b];
                    clip.WorldDeltaQuats[f][b] = new Quaternion(ToF(q[0]), ToF(q[1]), ToF(q[2]), ToF(q[3]));
                }
            }

            var fcList = (List<object>)root["footContacts"];
            clip.FootContacts = new Vector4[fcList.Count];
            for (int f = 0; f < fcList.Count; f++)
            {
                var v = (List<object>)fcList[f];
                clip.FootContacts[f] = new Vector4(ToF(v[0]), ToF(v[1]), ToF(v[2]), ToF(v[3]));
            }

            if (root.TryGetValue("footstepEvents", out object evObj) && evObj is List<object> evList)
            {
                foreach (object item in evList)
                {
                    var d = (Dictionary<string, object>)item;
                    clip.FootstepEvents.Add(new ParsedFootstep
                    {
                        Time = ToF(d["time"]),
                        Foot = Convert.ToString(d["foot"], CultureInfo.InvariantCulture),
                        Intensity = ToF(d["intensity"]),
                    });
                }
            }

            return clip;
        }

        private static float ToF(object o) => Convert.ToSingle(o, CultureInfo.InvariantCulture);

        private sealed class MiniJson
        {
            private readonly string _s;
            private int _i;

            private MiniJson(string s) { _s = s; }

            public static object Deserialize(string json) => new MiniJson(json).ParseValue();

            private void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private object ParseValue()
            {
                SkipWs();
                char c = _s[_i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') { _i += 4; return true; }
                if (c == 'f') { _i += 5; return false; }
                if (c == 'n') { _i += 4; return null; }
                return ParseNumber();
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++; // '{'
                while (true)
                {
                    SkipWs();
                    if (_s[_i] == '}') { _i++; return dict; }
                    string key = ParseString();
                    SkipWs();
                    _i++; // ':'
                    dict[key] = ParseValue();
                    SkipWs();
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return dict; }
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _i++; // '['
                while (true)
                {
                    SkipWs();
                    if (_s[_i] == ']') { _i++; return list; }
                    list.Add(ParseValue());
                    SkipWs();
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                }
            }

            private string ParseString()
            {
                _i++; // '"'
                var sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\' && _i < _s.Length)
                    {
                        char e = _s[_i++];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber));
                                _i += 4;
                                break;
                            default: sb.Append(e); break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            private double ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length && ("-0123456789.eE+".IndexOf(_s[_i]) >= 0)) _i++;
                return double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture);
            }
        }
    }
}
#endif

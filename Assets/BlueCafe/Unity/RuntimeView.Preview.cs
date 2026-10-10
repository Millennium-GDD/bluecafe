using System;
using System.Collections.Generic;
using System.IO;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlueCafe.Unity
{
    public sealed partial class RuntimeView
    {
        GameObject placementPreview;
        Instance placementPreviewInstance;
        bool placementPreviewValid;
        readonly Dictionary<Material, Color> previewMaterialColors = new Dictionary<Material, Color>();

        public bool HasPlacementPreview { get { CheckThread(); return placementPreview; } }
        public bool PlacementPreviewValid { get { CheckThread(); return placementPreview && placementPreviewValid; } }
        public Instance PlacementPreviewInstance { get { CheckThread(); return Copies.Copy(placementPreviewInstance); } }
        public Bounds? PlacementPreviewBounds
        {
            get
            {
                CheckThread(); if (!placementPreview) return null;
                var renderers = placementPreview.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return null;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                return bounds;
            }
        }

        /// <summary>Transient original-asset projection. The caller supplies placement validity;
        /// no document command, selection object, or persistent unavailable diagnostic is created.</summary>
        public void ShowPlacementPreview(Instance instance, bool valid)
        {
            CheckThread();
            try
            {
                if (instance == null) throw new ArgumentNullException(nameof(instance));
                if (string.IsNullOrEmpty(instance.furnitureId) || !items.TryGetValue(instance.furnitureId, out var item))
                    throw new InvalidDataException("UNKNOWN_ITEM: preview furniture id is absent from the validated pack.");
                if (!Finite(instance.position.x) || !Finite(instance.position.y) || !Finite(instance.position.z) || instance.quarterTurns < 0 || instance.quarterTurns > 3)
                    throw new ArgumentException("INVALID_PREVIEW_TRANSFORM: preview needs a finite position and quarterTurns from 0 to 3.", nameof(instance));
                if (!placementPreview || placementPreviewInstance.furnitureId != instance.furnitureId)
                {
                    ClearPlacementPreview();
                    var availability = pack.Availability(item.id);
                    if (!availability.Available) throw new InvalidDataException("PREVIEW_UNAVAILABLE: " + availability.Reason);
                    if (Placement.IsRoomStyle(item) && room == null)
                        throw new InvalidOperationException("PROJECTION_REQUIRED: synchronize a room before previewing a room style.");
                    // Keep the ghost under the resource root so its original meshes and
                    // textures remain live; explicitly exclude it from framing and picking.
                    placementPreview = NewObject((item.name ?? item.id) + " [placement preview]", root.transform);
                    if (Placement.IsRoomStyle(item)) AssembleRoomStyle(item, placementPreview.transform);
                    else Assemble(item, placementPreview.transform);
                    ApplyNativePivot(item, placementPreview);
                    ClonePreviewMaterials();
                    TintPreview(valid);
                }
                else if (placementPreviewValid != valid) TintPreview(valid);
                placementPreview.transform.localPosition = new Vector3(instance.position.x, instance.position.y, instance.position.z);
                placementPreview.transform.localRotation = Quaternion.Euler(0, instance.quarterTurns * 90, 0);
                placementPreviewInstance = Copies.Copy(instance);
                placementPreviewValid = valid;
            }
            catch
            {
                ClearPlacementPreview();
                throw;
            }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        bool IsPlacementPreviewRenderer(Renderer renderer) => placementPreview && renderer.transform.IsChildOf(placementPreview.transform);

        void ClonePreviewMaterials()
        {
            var clones = new Dictionary<Material, Material>();
            foreach (var renderer in placementPreview.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var original = materials[i];
                    if (!original) throw new InvalidDataException("MATERIAL_UNAVAILABLE: preview original material is missing.");
                    if (!clones.TryGetValue(original, out var clone))
                    {
                        clone = new Material(original) { name = original.name + " [placement preview]", hideFlags = HideFlags.DontSave };
                        previewMaterialColors.Add(clone, original.color);
                        clones.Add(original, clone);
                        // Preserve source alpha policy, cutoff, stencil, culling and textures.
                        // Invisible stencil helpers stay invisible; visible ghost geometry
                        // blends without writing depth. Queue ordering is shifted below.
                        bool mask = original.GetInt("_SrcBlend") == (int)BlendMode.Zero && original.GetInt("_DstBlend") == (int)BlendMode.One;
                        if (!mask)
                        {
                            clone.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                            clone.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                            clone.SetInt("_ZWrite", 0);
                            clone.SetFloat("_PreviewOpacity", .58f);
                        }
                    }
                    materials[i] = clone;
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            // All committed runtime materials render at or before 3000. Keep exact
            // native ordering within the ghost, including mask/scenery/frame passes,
            // but run them afterward so preview stencil writes cannot mask a real wall.
            // Existing wall depth still occludes ghost scenery behind that wall.
            int firstQueue = int.MaxValue;
            foreach (var pair in clones) firstQueue = Math.Min(firstQueue, pair.Key.renderQueue);
            int queueOffset = Math.Max(0, 3100 - firstQueue);
            foreach (var pair in clones) pair.Value.renderQueue = pair.Key.renderQueue + queueOffset;
        }

        void TintPreview(bool valid)
        {
            var tint = valid ? new Color(.76f, .88f, 1f, 1f) : new Color(1f, .57f, .53f, 1f);
            foreach (var pair in previewMaterialColors) pair.Key.color = pair.Value * tint;
        }

        public void ClearPlacementPreview()
        {
            CheckThread();
            bool hadPreview = placementPreview || previewMaterialColors.Count != 0;
            if (placementPreview)
            {
                foreach (var renderer in placementPreview.GetComponentsInChildren<Renderer>()) framingHelpers.Remove(renderer);
                RuntimeResources.Release(placementPreview);
            }
            placementPreview = null;
            placementPreviewInstance = null;
            placementPreviewValid = false;
            foreach (var material in previewMaterialColors.Keys) RuntimeResources.Release(material);
            previewMaterialColors.Clear();
            if (hadPreview) resources.Trim(root);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlueCafe
{
    /// <summary>Audited native render states; acquired catalog and geometry remain immutable.</summary>
    public static class CafeWindowRendering
    {
        public const string WindowId = "210394";
        public const string WindowPrefab = "Assets/_MX/AddressableAsset/Cafe/my_event088_retractwindow.prefab";
        public const string MaskShaderId = "5288470620394130926";
        public const string SceneryShaderId = "-7760562892369693708";
        public const string StandardShaderId = "7599522721102763880";
        public const string Evidence = "evidence/core-reconstruction-stage2-2026-10-09/window/native-render-state.json";
        public const string RenderStatus = "derived-stencil-static-pose; native-animation-and-lighting-unverified";

        sealed class SurfaceProfile
        {
            public readonly string prefab, frameRendererName, cover, hole;
            public readonly string[] scenery, visible;
            public SurfaceProfile(string prefab, string frameRendererName, string cover, string hole, string[] scenery, string[] visible)
            { this.prefab=prefab; this.frameRendererName=frameRendererName; this.cover=cover; this.hole=hole; this.scenery=scenery; this.visible=visible; }
            public bool Mask(CafeMaterial spec) => spec != null && spec.sourceShader == MaskShaderId && (spec.name == cover || spec.name == hole);
            public bool Scenery(CafeMaterial spec) => spec != null && spec.sourceShader == SceneryShaderId && Array.IndexOf(scenery,spec.name)>=0;
        }
        // Every entry is backed by original native material state; names including the
        // original wildhunt "wilndow" typo are intentional. Mesh identity selects the
        // actual architectural frame, avoiding animated door leaves with shared material.
        public const string AdditionalEvidence = "evidence/core-reconstruction-stage2-2026-10-09/inventory-repair/special-surfaces/native-render-state.json";
        static readonly Dictionary<string,SurfaceProfile> Profiles = new Dictionary<string,SurfaceProfile> {
            { "210394", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_event088_retractwindow.prefab", "OL_my_event088_retractwindow_325704fc2195.mesh", "my_event088_retractwindow_cover", "my_event088_retractwindow_hole", new[] { "my_event088_retractwindow_out" }, new[] { "my_event088_retractwindow", "my_event088_retractwindow_a" }) },
            { "22116", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_wildhunt_01_window_01.prefab", "OL_my_wildhunt_01_window_01_fa4a471f5287.mesh", "my_wildhunt_01_wilndow_01_cover", "my_wildhunt_01_wilndow_01_hole", new[] { "my_wildhunt_01_wilndow_01_out" }, new[] { "my_wildhunt_01_window_01" }) },
            { "19212", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_millenniumlounge_01_window_02.prefab", "OL_my_millenniumlounge_01_window_02_01727585341e.mesh", "my_millenniumlounge_01_window_02_cover", "my_millenniumlounge_01_window_02_hole", new[] { "my_millenniumlounge_01_window_02_out" }, new[] { "my_millenniumlounge_01_window_02" }) },
            { "23309", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_carrier_01_wallcc_03.prefab", "OL_my_carrier_01_wallcc_03_78066a403da0.mesh", "my_carrier_01_wallcc_03_cover", "my_carrier_01_wallcc_03_hole", new[] { "my_carrier_01_wallcc_03_out_01", "my_carrier_01_wallcc_03_out_02" }, new[] { "my_carrier_01_wallcc_03" }) },
            { "23302", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_carrier_01_bridge_01.prefab", "OL_my_carrier_01_bridge_01_ff4c81d48d3d.mesh", "my_carrier_01_bridge_01_cover", "my_carrier_01_bridge_01_hole", new[] { "my_carrier_01_bridge_01_out" }, new[] { "my_carrier_01_bridge_01" }) },
            { "15300", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_gehennaparty_01_balcony_01.prefab", "OL_my_gehennaparty_01_balcony_01_acd1c6e1caa2.mesh", "my_gehennaparty_01_balcony_01_cover", "my_gehennaparty_01_balcony_01_hole", new[] { "my_gehennaparty_01_balcony_01_out", "my_gehennaparty_01_balcony_01_sky", "my_gehennaparty_01_balcony_01_outtable" }, new[] { "my_gehennaparty_01_balcony_01" }) },
            { "22309", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_wildhunt_01_wallcc_03.prefab", "OL_my_wildhunt_01_wallcc_03_572ed4d36149.mesh", "my_wildhunt_01_wallcc_03_cover", "my_wildhunt_01_wallcc_03_hole", new[] { "my_wildhunt_01_wallcc_03_out" }, new[] { "my_wildhunt_01_wallcc_03" }) },
            { "18304", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_hyakkiyako_01_balcony_01.prefab", "OL_my_hyakkiyako_01_balcony_01_d3b83af3b9f2.mesh", "my_hyakkiyako_01_balcony_01_cover", "my_gehennaparty_01_balcony_01_hole", new[] { "my_hyakkiyako_01_balcony_01_out", "my_hyakkiyako_01_balcony_01_sky" }, new[] { "my_hyakkiyako_01_balcony_01" }) },
            { "23304", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_carrier_01_kitchen_01.prefab", "OL_my_carrier_01_kitchen_01_61b5aaec54ab.mesh", "my_carrier_01_kitchen_01_cover", "my_carrier_01_kitchen_01_hole", new[] { "my_carrier_01_kitchen_01_out" }, new[] { "my_carrier_01_kitchen_01" }) },
            { "23112", new SurfaceProfile("Assets/_MX/AddressableAsset/Cafe/my_carrier_01_door_01.prefab", "OL_my_event088_door_01_d67185e56286.mesh", "my_carrier_01_door_01_cover", "my_carrier_01_door_01_hole", new[] { "my_carrier_01_door_01_out" }, new[] { "my_carrier_01_door_01" }) }
        };
        static SurfaceProfile Profile(CafeItem item) => item != null && item.id != null && Profiles.TryGetValue(item.id,out var p) && item.sourcePrefab == p.prefab ? p : null;
        public static bool IsWindow(CafeItem item) => item != null && item.id == WindowId && item.sourcePrefab == WindowPrefab;
        public static bool IsAuditedSurface(CafeItem item) => Profile(item) != null;
        public static bool IsFrameRenderer(CafeItem item, Renderer renderer) => renderer && Profile(item)?.frameRendererName == renderer.name;
        public static bool IsMask(CafeMaterial spec)
        { foreach(var p in Profiles.Values) if(p.Mask(spec))return true; return false; }
        // Helpers still rasterize and remain in full geometry bounds. Their unmasked
        // AABBs do not describe the visible aperture and must not frame or steal clicks.
        public static bool IsFramingHelper(CafeItem item, CafePart part)
        {
            var p=Profile(item);
            return p != null && part?.materials != null && Array.Exists(part.materials,s=>p.Mask(s)||p.Scenery(s));
        }

        static readonly HashSet<string> AuditedWallpaperMaterials = new HashSet<string> {
            "my_abydosclassroom_01_wall",
            "my_ariussummer_01_wall",
            "my_autumnsports_01_wall",
            "my_cafe_01_b_wall_02",
            "my_carrier_01_wall",
            "my_chocolatecafe_01_p_wall",
            "my_defaultroom_wall",
            "my_game_01_p_wall_01",
            "my_game_01_p_wall_01_emission 1",
            "my_gamedevdept_01_wall",
            "my_gehennaparty_01_wall",
            "my_halloween_01_p_wall",
            "my_highlander_01_wall",
            "my_hotsprings_01_wall",
            "my_hyakkiyako_01_wall",
            "my_military_01_wall",
            "my_millenniumlounge_01_wall",
            "my_millenniumlounge_01_wall_e",
            "my_newyearpath_wall",
            "my_poolparty_01_b_wall",
            "my_seahouse_01_wall",
            "my_shoppingmall_01_wall",
            "my_sunshineresort_01_wall",
            "my_teaparty_01_wall",
            "my_trinityclassroom_01_wall",
            "my_valkyrieacademy_01_wall",
            "my_wildhunt_01_wall"
        };

        public static void Apply(CafeItem item, CafeMaterial spec, Material material)
        {
            if (item.placement == "wallpaper" && spec.sourceShader == SceneryShaderId && AuditedWallpaperMaterials.Contains(spec.name))
            {
                material.SetInt("_StencilComp", (int)CompareFunction.NotEqual);
                material.SetInt("_StencilRef", 7);
                material.renderQueue = 1989;
                // Preserve existing double-sided wallpaper display. Window mask and
                // scenery culling are separately recovered from native state.
                return;
            }
            var profile = Profile(item);
            if (profile == null) return;
            if (profile.Mask(spec))
            {
                material.SetInt("_SrcBlend", (int)BlendMode.Zero);
                material.SetInt("_DstBlend", (int)BlendMode.One);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_ZTest", (int)CompareFunction.Disabled);
                material.SetInt("_StencilComp", (int)CompareFunction.GreaterEqual);
                material.SetInt("_StencilPass", (int)StencilOp.Replace);
                material.SetInt("_StencilRef", spec.name == profile.cover ? 4 : 7);
                material.SetInt("_Cull", (int)CullMode.Back);
                material.renderQueue = spec.name == profile.cover ? 1974 : 1979;
            }
            else if (profile.Scenery(spec))
            {
                material.SetInt("_StencilComp", (int)CompareFunction.NotEqual);
                material.SetInt("_StencilRef", 4);
                material.SetInt("_Cull", (int)CullMode.Back);
                material.renderQueue = 1984;
            }
            else if (spec.sourceShader == StandardShaderId && Array.IndexOf(profile.visible,spec.name)>=0)
            {
                material.SetInt("_Cull", (int)CullMode.Back);
                material.renderQueue = spec.transparent ? 2900 : 2000;
            }
        }
    }
}

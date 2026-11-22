using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Alta.Networking;
using ATT.Saving;
using MelonLoader;
using SyncLib.Items;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PlayerArmorMod
{
    internal static class ArmorStandPrefab
    {
        internal const string Name = "ATT_ArmorStand";
        const string Resource = "PlayerArmor.Stand";

        internal static NetworkPrefab Register(MelonMod mod, GameObject staging, ArmorPrefabRegistry assets)
        {
            var template = BuildTemplate(staging, assets);
            var registered = mod.RegisterCustomPrefab(template, Name);
            if (registered == null) throw new InvalidOperationException("SyncLib did not register " + Name);
            registered.SaveMode = SaveMode.Base;
            if (!registered.CanBeSpawnedThroughCommand || PrefabManager.GetPrefab(registered.Hash) != registered)
                throw new InvalidOperationException(Name + " is unavailable in the native spawn list");
            Core.Log("PLAYER_ARMOR_STAND_REGISTERED name=" + Name + " hash=" + registered.Hash + " slots=" + registered.Embedded.Count);
            return registered;
        }

        static GameObject BuildTemplate(GameObject staging, ArmorPrefabRegistry assets)
        {
            if (staging == null || staging.activeInHierarchy)
                throw new InvalidOperationException("Stand templates require an inactive staging parent");

            var root = LoadTemplate(staging.transform);
            root.name = Name;
            var entity = root.AddComponent<NetworkEntity>();
            var prefab = root.AddComponent<NetworkPrefab>();
            ArmorNative.Set(prefab, "entity", entity);
            var mounts = new Transform[ArmorCatalog.SlotCount];
            foreach (var piece in ArmorCatalog.Pieces)
            {
                var mount = root.transform.Find("Mounts/" + piece.Slot);
                if (mount == null)
                    throw new InvalidDataException("Missing armor stand mount for " + piece.Slot);
                mounts[(int)piece.Slot] = mount;
            }

            // Only the authored wooden frame is solid. Each native dock sets
            // its own interaction layers and is saved as an embedded entity.
            int ground = LayerMask.NameToLayer("Ground");
            if (ground < 0) throw new InvalidOperationException("Native Ground layer is unavailable");
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.gameObject.layer = ground;
            if(Core.IsServer)
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(filter);
            }
            else
            {
                CombineJoinery(root);
                ArmorStereo.Configure(root);
            }
            ArmorStandDocks.Install(root, prefab, mounts, assets);
            ArmorNative.Bind(root);
            root.SetActive(true);
            return root;
        }

        internal static void CombineJoinery(GameObject root)
        {
            var joinery = root.transform.Find("Joinery");
            if (joinery == null) return;
            var renderers = joinery.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length < 2) return;
            var first = renderers[0];
            if (renderers.Any(renderer => !MatchesJoinery(first, renderer))) return;
            var filters = renderers.Select(renderer => renderer.GetComponent<MeshFilter>()).ToArray();
            if (filters.Any(filter => filter == null || filter.sharedMesh == null ||
                !filter.sharedMesh.isReadable || filter.sharedMesh.subMeshCount != 1)) return;
            var mesh = new Mesh { name = "Armor stand fitted joinery", hideFlags = HideFlags.DontSave };
            GameObject node = null;
            try
            {
                if (filters.Sum(filter => filter.sharedMesh.vertexCount) > ushort.MaxValue)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(filters.Select(filter => new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    transform = joinery.worldToLocalMatrix * filter.transform.localToWorldMatrix
                }).ToArray(), true, true);
                mesh.RecalculateBounds();
                node = new GameObject("Fitted stand frame");
                node.transform.SetParent(joinery, false);
                node.layer = first.gameObject.layer;
                node.AddComponent<MeshFilter>().sharedMesh = mesh;
                var combined = node.AddComponent<MeshRenderer>();
                combined.sharedMaterial = first.sharedMaterial;
                combined.shadowCastingMode = first.shadowCastingMode;
                combined.receiveShadows = first.receiveShadows;
                combined.lightProbeUsage = first.lightProbeUsage;
                combined.reflectionProbeUsage = first.reflectionProbeUsage;
                combined.probeAnchor = first.probeAnchor;
                combined.lightProbeProxyVolumeOverride = first.lightProbeProxyVolumeOverride;
                combined.motionVectorGenerationMode = first.motionVectorGenerationMode;
                combined.allowOcclusionWhenDynamic = first.allowOcclusionWhenDynamic;
                combined.renderingLayerMask = first.renderingLayerMask;
                combined.sortingLayerID = first.sortingLayerID;
                combined.sortingOrder = first.sortingOrder;
                foreach (var renderer in renderers) Object.DestroyImmediate(renderer);
                foreach (var filter in filters) Object.DestroyImmediate(filter);
            }
            catch
            {
                if (node != null) Object.DestroyImmediate(node);
                Object.DestroyImmediate(mesh);
                throw;
            }
        }

        static bool MatchesJoinery(MeshRenderer first, MeshRenderer renderer)
        {
            return renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeSelf && !renderer.HasPropertyBlock() &&
                !renderer.isPartOfStaticBatch && renderer.additionalVertexStreams == null &&
                renderer.sharedMaterials.Length == 1 && renderer.sharedMaterial == first.sharedMaterial &&
                renderer.gameObject.layer == first.gameObject.layer && renderer.shadowCastingMode == first.shadowCastingMode &&
                renderer.receiveShadows == first.receiveShadows && renderer.lightProbeUsage == first.lightProbeUsage &&
                renderer.reflectionProbeUsage == first.reflectionProbeUsage && renderer.probeAnchor == first.probeAnchor &&
                renderer.lightProbeProxyVolumeOverride == first.lightProbeProxyVolumeOverride &&
                renderer.motionVectorGenerationMode == first.motionVectorGenerationMode &&
                renderer.allowOcclusionWhenDynamic == first.allowOcclusionWhenDynamic &&
                renderer.renderingLayerMask == first.renderingLayerMask && renderer.sortingLayerID == first.sortingLayerID &&
                renderer.sortingOrder == first.sortingOrder && renderer.lightmapIndex < 0 && renderer.realtimeLightmapIndex < 0;
        }

        static GameObject LoadTemplate(Transform parent)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (stream == null) throw new InvalidDataException("Missing embedded armor stand bundle");
                var bundle = AssetBundle.LoadFromStream(stream);
                if (bundle == null) throw new InvalidDataException("Invalid armor stand bundle");
                try
                {
                    var path = bundle.GetAllAssetNames().Single(p => p.EndsWith("/att_armorstand.prefab", StringComparison.OrdinalIgnoreCase));
                    var model = bundle.LoadAsset<GameObject>(path);
                    if (model == null) throw new InvalidDataException("Missing armor stand model in bundle");
                    return Object.Instantiate(model, parent, false);
                }
                finally { bundle.Unload(false); }
            }
        }
    }
}

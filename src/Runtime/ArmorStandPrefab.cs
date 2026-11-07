using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Alta.Networking;
using ATT.Saving;
using MelonLoader;
using SyncLib.Items;
using UnityEngine;
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
            else ArmorStereo.Configure(root);
            ArmorStandDocks.Install(root, prefab, mounts, assets);
            ArmorNative.Bind(root);
            root.SetActive(true);
            return root;
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

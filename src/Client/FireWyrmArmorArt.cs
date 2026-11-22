using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PlayerArmorMod
{
    internal sealed class FireWyrmArmorArt : IDisposable
    {
        readonly GameObject staging;
        readonly List<Object> owned = new List<Object>();
        internal GameObject Template { get; }

        internal FireWyrmArmorArt(GameObject crystalTemplate)
        {
            staging = new GameObject("Player Armor Fire Wyrm art");
            staging.SetActive(false);
            Object.DontDestroyOnLoad(staging);
            try
            {
                Template = Object.Instantiate(crystalTemplate, staging.transform, false);
                Template.name = "ATT_FireWyrmArmor_Visuals";
                var shell = LoadTexture("Shell");
                var glow = LoadTexture("Glow");
                var materials = new Dictionary<Material, Material>();
                foreach (var renderer in Template.GetComponentsInChildren<Renderer>(true))
                {
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var source = slots[i];
                        if (source == null) continue;
                        if (!materials.TryGetValue(source, out var warmed))
                            materials.Add(source, warmed = WarmMaterial(source, shell, glow));
                        slots[i] = warmed;
                    }
                    renderer.sharedMaterials = slots;
                }
                ArmorStereo.Configure(Template);
            }
            catch { Dispose(); throw; }
        }

        Material WarmMaterial(Material source, Texture shell, Texture glow)
        {
            var material = new Material(source) { name = "Fire Wyrm " + source.name, hideFlags = HideFlags.DontSave };
            owned.Add(material);
            switch (source.name)
            {
                case "Native Crystal Wyrm chitin and crystal":
                    material.SetTexture("_MainTex", shell);
                    material.SetTexture("_EmissionMap", glow);
                    material.SetFloat("_EmissionStrength", .55f);
                    break;
                case "Violet Crystal Wyrm eyes":
                    material.color = new Color32(255, 177, 58, 255);
                    material.SetColor("_SelfLight", new Color(.45f, .14f, .018f));
                    break;
                case "Crystal Wyrm rear guard backing":
                    material.color = new Color32(189, 125, 99, 255);
                    break;
                case "Weathered pewter fittings":
                    material.color = new Color32(189, 137, 91, 255);
                    break;
                case "Charcoal glove lining":
                    material.color = new Color32(121, 83, 71, 255);
                    break;
                case "Graphite shell lining":
                    material.color = new Color32(86, 60, 52, 255);
                    break;
            }
            return material;
        }

        Texture2D LoadTexture(string name)
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("PlayerArmor.FireWyrm." + name))
            using (var bytes = new MemoryStream())
            {
                if (resource == null) throw new InvalidDataException("Missing Fire Wyrm armor " + name + " texture.");
                resource.CopyTo(bytes);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                    { name = "Fire Wyrm armor " + name, hideFlags = HideFlags.DontSave, anisoLevel = 4 };
                owned.Add(texture);
                if (!texture.LoadImage(bytes.ToArray(), true)) throw new InvalidDataException("Invalid Fire Wyrm armor " + name + " texture.");
                return texture;
            }
        }

        public void Dispose()
        {
            if (staging != null) Object.Destroy(staging);
            foreach (var asset in owned)
            {
                if (asset is Material material) ArmorStereo.Unregister(material);
                if (asset != null) Object.Destroy(asset);
            }
            owned.Clear();
        }
    }
}

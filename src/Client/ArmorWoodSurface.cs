using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorWoodSurface
    {
        // The current Woodland art uses this texture for all timber faces and
        // edges. Other materials on a primary part can be hide or hardware.
        const string AuthoredWoodTexture = "gotera_oak_LOD0_d";

        internal static Mesh Build(Mesh source, Material[] original, Material[] woods, out Material[] materials, out Material[] owned)
        {
            owned = Array.Empty<Material>();
            var clones = new Dictionary<Material, Material>();
            try
            {
                Mesh result = ArmorSurfaceMesh.Build(source, original, IsWood, Select, out materials);
                owned = new Material[clones.Count];
                clones.Values.CopyTo(owned, 0);
                return result;
            }
            catch
            {
                foreach (Material clone in clones.Values) Release(clone);
                throw;
            }

            Material Select(Material authored, Vector4 part, int first, int second, int third)
            {
                int index = (int)part.y;
                if (part.x != ArmorSemanticLayout.Primary || index >= woods.Length) return null;
                Material wood = woods[index];
                if (wood == null || wood.mainTexture == null) return null;
                if (!clones.TryGetValue(wood, out Material surface))
                {
                    surface = new Material(authored);
                    clones.Add(wood, surface);
                    Configure(surface, wood);
                }
                return surface;
            }
        }

        static bool IsWood(Material material)
        {
            Texture texture = material == null ? null : material.mainTexture;
            return texture != null && (texture.name == AuthoredWoodTexture || texture.name == "WoodlandBucketWood");
        }

        static void Configure(Material surface, Material wood)
        {
            surface.name = "Player Armor wood / " + wood.name;
            surface.hideFlags = HideFlags.DontSave;
            surface.mainTexture = wood.mainTexture;
            surface.mainTextureScale = wood.mainTextureScale;
            surface.mainTextureOffset = wood.mainTextureOffset;
            surface.color = wood.HasProperty("_Color") ? wood.color : Color.white;
            surface.SetFloat("_UseAtlas", 0);
            surface.SetFloat("_UseVertexColor", 0);
            surface.SetFloat("_Glow", 0);
            ArmorStereo.Register(surface);
        }

        internal static void Release(Material material)
        {
            ArmorStereo.Unregister(material);
            ArmorSurfaceMesh.Destroy(material);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorLeatherSurface
    {
        const string Sampler = "_Sampler";
        const uint DaisLeather = 33384;

        sealed class PackedAtlas
        {
            internal Texture Source;
            internal Texture2D Texture;
            internal int Users;
            internal bool Retired;
        }

        static readonly Dictionary<Texture, PackedAtlas> atlases = new Dictionary<Texture, PackedAtlas>();
        static readonly Dictionary<Material, PackedAtlas> owners = new Dictionary<Material, PackedAtlas>();

        internal static Mesh Build(Mesh source, Material[] original, Material[] leathers, int primaryCount,
            ArmorStyle style, out Material[] materials, out Material[] owned)
        {
            owned = Array.Empty<Material>();
            materials = original;
            if (!Array.Exists(original, IsHide) || !Array.Exists(leathers, IsDye)) return null;

            Vector2[] uvs = source.uv;
            var neutral = new List<Vector2>();
            source.GetUVs(5, neutral);
            if (neutral.Count != 0 && neutral.Count != source.vertexCount)
                throw new ArgumentException("Armor soft-hide metadata has an incomplete vertex count");

            var clones = new Dictionary<(Material Dye, Texture Atlas), Material>();
            try
            {
                Mesh mesh = ArmorSurfaceMesh.Build(source, original, IsHide, Select, out materials);
                owned = new Material[clones.Count];
                clones.Values.CopyTo(owned, 0);
                return mesh;
            }
            catch
            {
                foreach (Material clone in clones.Values) Release(clone);
                throw;
            }

            Material Select(Material authored, Vector4 part, int first, int second, int third)
            {
                int index = (int)part.y;
                if (part.x == ArmorSemanticLayout.Fastener) index += primaryCount;
                else if (style != ArmorStyle.Leather) return null;
                if (index >= leathers.Length || !IsDye(leathers[index])) return null;

                if (style == ArmorStyle.Leather && uvs.Length == source.vertexCount &&
                    ArmorMaterialMesh.Hardware(uvs[first]) && ArmorMaterialMesh.Hardware(uvs[second]) &&
                    ArmorMaterialMesh.Hardware(uvs[third])) return null;
                if (neutral.Count != 0)
                {
                    bool skip = ArmorMaterialMesh.NeutralHide(neutral[first]);
                    if (skip != ArmorMaterialMesh.NeutralHide(neutral[second]) || skip != ArmorMaterialMesh.NeutralHide(neutral[third]))
                        throw new ArgumentException("Armor triangle crosses soft-hide tint boundaries");
                    if (skip) return null;
                }

                var key = (leathers[index], authored.mainTexture);
                if (!clones.TryGetValue(key, out Material result))
                {
                    result = CreateMaterial(key.Item1, key.Item2);
                    clones.Add(key, result);
                }
                return result;
            }
        }

        static bool IsHide(Material material)
        {
            Texture texture = material == null ? null : material.mainTexture;
            return texture != null && (texture.name == "leather_armor_d" || texture.name == "ATT_Leather_Albedo");
        }

        static bool IsDye(Material material) => material != null && material.shader != null &&
            material.shader.name == "Township Tale/RampLerp3" && material.HasProperty(Sampler);

        static Material CreateMaterial(Material dye, Texture source)
        {
            if (!atlases.TryGetValue(source, out PackedAtlas atlas))
            {
                atlas = new PackedAtlas { Source = source, Texture = PackTones(source) };
                atlases.Add(source, atlas);
            }
            var clone = new Material(dye) { name = "Player Armor leather / " + dye.name, hideFlags = HideFlags.DontSave };
            try
            {
                // Preserve the three native colors, ramp pivot, and lighting. The armor
                // atlas supplies the same kind of red-channel tones in its own UV layout.
                clone.SetTexture(Sampler, atlas.Texture);
                clone.SetTextureScale(Sampler, Vector2.one);
                clone.SetTextureOffset(Sampler, Vector2.zero);
                owners.Add(clone, atlas);
                atlas.Users++;
                atlas.Retired = false;
                return clone;
            }
            catch
            {
                ArmorSurfaceMesh.Destroy(clone);
                throw;
            }
        }

        static Texture2D PackTones(Texture source)
        {
            var previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D packed = null;
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                packed = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, true)
                {
                    name = "Player Armor leather tones / " + source.name,
                    hideFlags = HideFlags.DontSave,
                    filterMode = source.filterMode,
                    wrapMode = source.wrapMode,
                    anisoLevel = source.anisoLevel
                };
                packed.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                var pixels = packed.GetPixels32();
                var histogram = new int[256];
                for (int i = 0; i < pixels.Length; i++) histogram[LuminanceByte(pixels[i])]++;
                int median = 0, count = histogram[0];
                while (median < 255 && count < pixels.Length / 2) count += histogram[++median];

                // Match a typical native roll tone while retaining the shading, seams
                // and wear that are painted into the armor-specific atlas.
                const float nativeMedianTone = .38f;
                float low = 0f, middle = .5f, high = 1f, pivot = .5f;
#if !UNITY_EDITOR
                Material dais = ArmorMaterialPalette.ResolveLeather(DaisLeather);
                if (IsDye(dais))
                {
                    float start = ArmorMaterialMesh.Luminance(ArmorMaterialMesh.ShaderColor(dais, "_Color"));
                    float mid = ArmorMaterialMesh.Luminance(ArmorMaterialMesh.ShaderColor(dais, "_Color1"));
                    float end = ArmorMaterialMesh.Luminance(ArmorMaterialMesh.ShaderColor(dais, "_Color2"));
                    if (start < mid && mid < end)
                    {
                        low = start;
                        middle = mid;
                        high = end;
                        pivot = Mathf.Clamp(dais.GetFloat("_Color1Position"), .01f, .99f);
                    }
                }
#endif
                float reference = nativeMedianTone < pivot
                    ? Mathf.Lerp(low, middle, nativeMedianTone / pivot)
                    : Mathf.Lerp(middle, high, (nativeMedianTone - pivot) / (1 - pivot));
                float gain = reference / Math.Max(1, median);
                var tones = new byte[256];
                for (int i = 0; i < tones.Length; i++)
                {
                    float luminance = i * gain;
                    float tone = luminance < middle
                        ? pivot * Mathf.InverseLerp(low, middle, luminance)
                        : pivot + (1 - pivot) * Mathf.InverseLerp(middle, high, luminance);
                    tones[i] = (byte)Mathf.RoundToInt(tone * 255f);
                }
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte tone = tones[LuminanceByte(pixels[i])];
                    pixels[i] = new Color32(tone, tone, 0, 255);
                }
                packed.SetPixels32(pixels);
                packed.Apply(true, true);
                return packed;
            }
            catch
            {
                ArmorSurfaceMesh.Destroy(packed);
                throw;
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static int LuminanceByte(Color32 pixel) => (54 * pixel.r + 183 * pixel.g + 19 * pixel.b + 128) >> 8;

        internal static void Release(Material material)
        {
            if (!ReferenceEquals(material, null) && owners.TryGetValue(material, out PackedAtlas atlas))
            {
                owners.Remove(material);
                if (--atlas.Users == 0 && atlas.Retired) RemoveAtlas(atlas);
            }
            ArmorSurfaceMesh.Destroy(material);
        }

        internal static void Clear()
        {
            foreach (PackedAtlas atlas in new List<PackedAtlas>(atlases.Values))
            {
                if (atlas.Users == 0) RemoveAtlas(atlas);
                else atlas.Retired = true;
            }
        }

        static void RemoveAtlas(PackedAtlas atlas)
        {
            atlases.Remove(atlas.Source);
            ArmorSurfaceMesh.Destroy(atlas.Texture);
        }
    }
}

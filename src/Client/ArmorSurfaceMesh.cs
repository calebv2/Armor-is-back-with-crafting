using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PlayerArmorMod
{
    // Split draw calls by material without recreating vertices, UVs or skinning.
    internal static class ArmorSurfaceMesh
    {
        internal delegate Material SelectSurface(Material original, Vector4 part, int first, int second, int third);

        sealed class Surface
        {
            internal Material Material;
            internal MeshTopology Topology;
            internal int[] OriginalIndices;
            internal readonly List<int> Triangles = new List<int>();
        }

        internal static Mesh Build(Mesh source, Material[] original, Predicate<Material> eligible, SelectSurface select,
            out Material[] materials)
        {
            materials = original;
            if (!Array.Exists(original, eligible)) return null;
            if (original.Length != source.subMeshCount)
                throw new ArgumentException("Armor mesh and material counts differ");

            var parts = ArmorSemanticLayout.Read(source);
            var surfaces = new List<Surface>();
            var replacements = new Dictionary<Material, Surface>();
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                Material authored = original[submesh];
                MeshTopology topology = source.GetTopology(submesh);
                int[] indices = source.GetIndices(submesh);
                if (!eligible(authored) || topology != MeshTopology.Triangles)
                {
                    surfaces.Add(new Surface { Material = authored, Topology = topology, OriginalIndices = indices });
                    continue;
                }

                var unchanged = new Surface { Material = authored, Topology = topology };
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int first = indices[i], second = indices[i + 1], third = indices[i + 2];
                    Vector4 part = parts[first];
                    if (part != parts[second] || part != parts[third])
                        throw new InvalidOperationException("Armor triangle crosses construction parts");

                    Material replacement = select(authored, part, first, second, third);
                    Surface surface = unchanged;
                    if (replacement != null)
                    {
                        if (!replacements.TryGetValue(replacement, out surface))
                        {
                            surface = new Surface { Material = replacement, Topology = topology };
                            replacements.Add(replacement, surface);
                        }
                    }
                    surface.Triangles.Add(first);
                    surface.Triangles.Add(second);
                    surface.Triangles.Add(third);
                }
                if (unchanged.Triangles.Count > 0) surfaces.Add(unchanged);
            }

            if (replacements.Count == 0) return null;
            surfaces.AddRange(replacements.Values);
            Mesh result = Object.Instantiate(source);
            try
            {
                result.name = source.name + " (material surfaces)";
                result.hideFlags = HideFlags.DontSave;
                if (source.vertexCount > ushort.MaxValue) result.indexFormat = IndexFormat.UInt32;
                result.subMeshCount = surfaces.Count;
                var assigned = new Material[surfaces.Count];
                for (int i = 0; i < surfaces.Count; i++)
                {
                    Surface surface = surfaces[i];
                    result.SetIndices(surface.OriginalIndices ?? surface.Triangles.ToArray(), surface.Topology, i, false);
                    assigned[i] = surface.Material;
                }
                result.bounds = source.bounds;
                materials = assigned;
                return result;
            }
            catch
            {
                Destroy(result);
                throw;
            }
        }

        internal static void Destroy(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}

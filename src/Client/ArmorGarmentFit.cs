using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    // A fitted version of the original skinned cloth. The fit is computed once
    // in the shared ATT bind pose, before native packing and atlas remapping.
    internal static class ArmorGarmentFit
    {
        const float TransitionSlope = .48f;
        const float MinimumRegionWeight = .24f;
        const float MaximumTransition = .055f;
        const float MaximumRelativeStretch = .75f;

        struct Sample
        {
            internal Vector3 Position, Inward;
            internal int Slot;
            internal float Required, Allowable;
        }

        internal static uint[] BoneRegions(Dictionary<Transform, int> bones, int count)
        {
            var result = new uint[count];
            foreach (var entry in bones) result[entry.Value] = Region(entry.Key);
            return result;
        }

        static uint Flag(ArmorSlot slot) => 1u << (int)slot;

        internal static bool CoveredSleeves(Mesh[] lods, uint armorMask, uint[] boneMasks)
        {
            if (boneMasks == null) return true;
            uint sleeves = Flag(ArmorSlot.LeftShoulder) | Flag(ArmorSlot.RightShoulder) |
                Flag(ArmorSlot.LeftForearm) | Flag(ArmorSlot.RightForearm);
            uint uncovered = sleeves & ~armorMask;
            if (uncovered == 0) return true;
            foreach (var mesh in lods)
            {
                if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) continue;
                foreach (var weight in mesh.boneWeights)
                    if (Weight(weight, boneMasks, uncovered) >= MinimumRegionWeight) return false;
                return true;
            }
            return true;
        }

        static uint Region(Transform bone)
        {
            for (var node = bone; node != null; node = node.parent)
                switch (node.name)
                {
                    // Garment cuffs can reach the wrist, but a finger or palm
                    // cosmetic should never be pulled up the forearm.
                    case "wrist_L_jnt": return bone == node ? Flag(ArmorSlot.LeftForearm) : 0;
                    case "wrist_R_jnt": return bone == node ? Flag(ArmorSlot.RightForearm) : 0;
                    case "forearm_L_jnt":return Flag(ArmorSlot.LeftForearm);
                    case "forearm_R_jnt":return Flag(ArmorSlot.RightForearm);
                    case "elbow_L_jnt": return Flag(ArmorSlot.LeftForearm) | Flag(ArmorSlot.LeftShoulder);
                    case "elbow_R_jnt": return Flag(ArmorSlot.RightForearm) | Flag(ArmorSlot.RightShoulder);
                    case "shoulder_L_jnt": return Flag(ArmorSlot.LeftShoulder);
                    case "shoulder_R_jnt": return Flag(ArmorSlot.RightShoulder);
                    case "clavicle_L_jnt": return Flag(ArmorSlot.LeftShoulder) | Flag(ArmorSlot.Chest);
                    case "clavicle_R_jnt": return Flag(ArmorSlot.RightShoulder) | Flag(ArmorSlot.Chest);
                    case "head_jnt": return 0;
                    case "chest_jnt":
                    case "spine_02_jnt":
                    case "spine_01_jnt": return Flag(ArmorSlot.Chest);
                    case "hipBase_jnt":
                    case "spine_root_jnt":
                    case "cog_jnt": return Flag(ArmorSlot.Chest) | Flag(ArmorSlot.Hips);
                }
            return 0;
        }

        internal static bool Apply(Mesh source, Vector3[] vertices, uint mask, ArmorCosmeticShell[] armor,
            ArmorCosmeticShell body, uint[] boneMasks)
        {
            var weights = source.boneWeights;
            if (weights.Length != vertices.Length) return false;
            var samples = new Sample[vertices.Length];
            var representatives = new int[vertices.Length];
            var locations = new Dictionary<Vector3, int>();
            bool changed = false;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (locations.TryGetValue(vertices[i], out var locationsIndex))
                {
                    representatives[i] = locationsIndex;
                    continue;
                }
                locations.Add(vertices[i], i);
                representatives[i] = i;
                var sample = new Sample { Position = vertices[i], Slot = -1 };
                float strongest = 0f;
                for (int slot = (int)ArmorSlot.Chest; slot < armor.Length; slot++)
                {
                    uint flag = 1u << slot;
                    if ((mask & flag) == 0 || armor[slot] == null) continue;
                    float weight = Weight(weights[i], boneMasks, flag);
                    if (weight < MinimumRegionWeight ||
                        !armor[slot].ClothClearance(vertices[i], body, out var inward, out float allowable, out float required)) continue;
                    if (sample.Slot >= 0 && (required < sample.Required || (required == sample.Required && weight <= strongest))) continue;
                    sample.Slot = slot;
                    sample.Inward = inward;
                    sample.Required = required;
                    sample.Allowable = allowable;
                    strongest = weight;
                }
                samples[i] = sample;
                changed |= sample.Required > .0001f;
            }
            if (!changed) return false;

            var distances = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++) distances[i] = samples[i].Required;
            // Carry the tuck a short distance across neighboring cloth so
            // sleeves meet the plate with a taper. UV splits share the same
            // displacement and the body clearance remains a hard boundary.
            var edges = Edges(source, representatives);
            for (int pass = 0; pass < 5; pass++)
            {
                bool extended = false;
                foreach (var edge in edges)
                {
                    extended |= Taper(edge.Item1, edge.Item2);
                    extended |= Taper(edge.Item2, edge.Item1);
                }
                if (!extended) break;
            }

            // Where two plate regions meet, their inward directions can
            // diverge abruptly. Limit each affected edge locally so one
            // awkward seam cannot loosen sleeves elsewhere on the garment.
            for (int pass = 0; pass < 12; pass++)
            {
                bool adjusted = false;
                foreach (var edge in edges) adjusted |= LimitEdge(edge.Item1, edge.Item2);
                if (!adjusted) break;
            }
            for (int i = 0; i < vertices.Length; i++)
            {
                int shared = representatives[i];
                vertices[i] += samples[shared].Inward * distances[shared];
            }
            return true;

            bool Taper(int from, int to)
            {
                if (from == to || samples[from].Slot < 0 || samples[to].Slot < 0 ||
                    Vector3.Dot(samples[from].Inward, samples[to].Inward) < .5f) return false;
                float spacing = Vector3.Distance(samples[from].Position, samples[to].Position);
                if (spacing > MaximumTransition) return false;
                float next = Mathf.Min(samples[to].Allowable, Mathf.Min(MaximumTransition, distances[from]) - spacing * TransitionSlope);
                if (next <= distances[to] + .0001f) return false;
                distances[to] = next;
                return true;
            }

            bool LimitEdge(int first, int second)
            {
                if (distances[first] == 0f && distances[second] == 0f) return false;
                if (distances[first] < distances[second])
                {
                    int smaller = first;
                    first = second;
                    second = smaller;
                }
                float spacing = Vector3.Distance(samples[first].Position, samples[second].Position);
                float limit = spacing * MaximumRelativeStretch;
                Vector3 firstAxis = samples[first].Inward;
                Vector3 secondOffset = samples[second].Inward * distances[second];
                float difference = Vector3.Distance(firstAxis * distances[first], secondOffset);
                if (difference <= limit + .00001f) return false;

                float along = Vector3.Dot(firstAxis, secondOffset);
                float slack = limit * limit - (secondOffset.sqrMagnitude - along * along);
                if (slack >= 0f && along + Mathf.Sqrt(slack) >= 0f)
                    distances[first] = Mathf.Min(distances[first], along + Mathf.Sqrt(slack));
                else
                {
                    float scale = limit / difference;
                    distances[first] *= scale;
                    distances[second] *= scale;
                }
                return true;
            }
        }

        static float Weight(BoneWeight value, uint[] regions, uint flag)
        {
            return Influence(value.boneIndex0, value.weight0) + Influence(value.boneIndex1, value.weight1)
                + Influence(value.boneIndex2, value.weight2) + Influence(value.boneIndex3, value.weight3);

            float Influence(int index, float weight) => index >= 0 && index < regions.Length && (regions[index] & flag) != 0 ? weight : 0;
        }

        static HashSet<(int, int)> Edges(Mesh source, int[] representatives)
        {
            var edges = new HashSet<(int, int)>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles) continue;
                var indices = source.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Edge(indices[i], indices[i + 1]);
                    Edge(indices[i + 1], indices[i + 2]);
                    Edge(indices[i + 2], indices[i]);
                }
            }
            return edges;

            void Edge(int a, int b)
            {
                a = representatives[a];
                b = representatives[b];
                if (a != b) edges.Add(a < b ? (a, b) : (b, a));
            }
        }
    }
}

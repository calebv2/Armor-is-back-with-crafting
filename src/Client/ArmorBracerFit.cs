using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorBracerFit
    {
        static readonly Dictionary<Mesh, Mesh> corrected = new Dictionary<Mesh, Mesh>();

        internal static void Apply(GameObject visual, List<OriginalArmorArt.Frame> frames, ArmorStyle style, bool left)
        {
            // The native elbow frames mirror both radial coordinates. Center
            // each shell on the outer side of the forearm in the neutral pose.
            float degrees;
            switch (style)
            {
                case ArmorStyle.Woodland:
                case ArmorStyle.Hebios: degrees = 95f; break;
                case ArmorStyle.Mushroom: degrees = 96f; break;
                default: return;
            }
            string side = left ? "L" : "R";
            string elbow = "elbow_" + side + "_jnt", forearm = "forearm_" + side + "_jnt";
            var outward = Quaternion.AngleAxis(degrees, Vector3.right);
            foreach (var frame in frames)
            {
                string bone = frame.Target.name;
                if (bone == elbow || bone == forearm) frame.Visual.localRotation = outward;
            }

            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var source = skin.sharedMesh;
                if (source == null) continue;
                if (!corrected.TryGetValue(source, out var mesh))
                {
                    mesh = RotateArmPlates(skin, elbow, forearm, outward);
                    corrected.Add(source, mesh);
                }
                if (mesh == null) continue;
                skin.sharedMesh = mesh;
                var bounds = skin.localBounds;
                bounds.Encapsulate(mesh.bounds);
                skin.localBounds = bounds;
            }
        }

        static Mesh RotateArmPlates(SkinnedMeshRenderer skin, string elbow, string forearm, Quaternion outward)
        {
            var source = skin.sharedMesh;
            var bones = skin.bones;
            var bindPoses = source.bindposes;
            var rotations = new Dictionary<int, Matrix4x4>();
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && (bones[i].name == elbow || bones[i].name == forearm))
                    rotations.Add(i, bindPoses[i].inverse * Matrix4x4.Rotate(outward) * bindPoses[i]);
            if (rotations.Count == 0) return null;

            var points = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var weights = source.boneWeights;
            bool changed = false;
            for (int i = 0; i < weights.Length; i++)
            {
                // Hebios bakes its shell into the articulated glove mesh. Its
                // plates have one rigid arm weight. Leave the moving hand and
                // the blended glove skin in their original poses.
                var weight = weights[i];
                if (weight.weight0 < .9999f || !rotations.TryGetValue(weight.boneIndex0, out var rotation)) continue;
                changed = true;
                points[i] = rotation.MultiplyPoint3x4(points[i]);
                if (normals.Length == points.Length) normals[i] = rotation.MultiplyVector(normals[i]).normalized;
                if (tangents.Length == points.Length)
                {
                    var tangent = tangents[i];
                    var direction = rotation.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
                }
            }
            if (!changed) return null;

            var mesh = Object.Instantiate(source);
            mesh.name = source.name + " worn bracer";
            mesh.hideFlags = HideFlags.DontSave;
            mesh.vertices = points;
            if (normals.Length == points.Length) mesh.normals = normals;
            if (tangents.Length == points.Length) mesh.tangents = tangents;
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static void Dispose()
        {
            foreach (var mesh in corrected.Values) if (mesh != null) Object.Destroy(mesh);
            corrected.Clear();
        }
    }
}

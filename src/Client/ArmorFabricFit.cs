using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorFabricFit
    {
        const float FirstSlice = 1.005f;
        const float SliceStep = .035f;
        const float FabricClearance = .008f;
        const float MaximumCompression = .065f;

        // Native skin cross-sections sampled from body_default_13_07_cutoff_LOD0.
        // MeshPacker consumes the native bind-space vertices directly: X is
        // left/right, Y is front/back and Z is up. Radii are in millimetres,
        // from -Y through +X to +Y in 11.25-degree steps, mirrored across X.
        static readonly byte[,] TorsoRadii =
        {
            { 137, 138, 143, 148, 155, 159, 161, 163, 158, 155, 146, 144, 138, 135, 131, 128, 128 },
            { 121, 122, 127, 132, 137, 144, 149, 152, 150, 150, 142, 140, 134, 132, 128, 125, 126 },
            { 107, 108, 112, 116, 122, 129, 135, 138, 139, 142, 136, 135, 131, 128, 126, 124, 124 },
            { 99, 101, 106, 111, 117, 123, 130, 132, 133, 135, 132, 132, 129, 129, 126, 124, 124 },
            { 98, 100, 106, 111, 118, 125, 132, 134, 135, 137, 134, 135, 133, 133, 130, 129, 129 },
            { 101, 104, 111, 116, 124, 132, 140, 142, 142, 143, 139, 141, 137, 137, 135, 133, 134 },
            { 110, 114, 122, 127, 135, 144, 151, 151, 149, 149, 145, 145, 140, 139, 136, 134, 135 },
            { 118, 124, 132, 138, 147, 156, 162, 162, 158, 157, 149, 147, 141, 138, 134, 131, 131 },
            { 125, 132, 142, 149, 159, 169, 174, 172, 167, 166, 158, 157, 149, 141, 133, 127, 125 },
            { 130, 139, 150, 160, 171, 183, 185, 182, 178, 178, 175, 171, 156, 144, 132, 125, 123 },
            { 136, 145, 156, 167, 180, 198, 209, 205, 196, 192, 188, 171, 150, 135, 124, 117, 114 }
        };

        internal static bool TryFit(Mesh source, out Vector3[] positions)
        {
            positions = null;
            if (source == null || !source.isReadable) return false;

            var vertices = source.vertices;
            bool changed = false;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!TryFitVertex(vertices[i], out var fitted)) continue;
                vertices[i] = fitted;
                changed = true;
            }
            if (changed) positions = vertices;
            return changed;
        }

        static bool TryFitVertex(Vector3 source, out Vector3 fitted)
        {
            fitted = source;
            float width = Mathf.Abs(source.x);
            float height = source.z;
            // Fade toward the hem, collar and sleeves to keep their original
            // outline and avoid pulling the exposed edges toward the armor.
            if (!(height > .995f && height < 1.355f && width < .205f)) return false;
            float radialSquared = source.x * source.x + source.y * source.y;
            if (!(radialSquared > .0064f && radialSquared < .25f)) return false;

            float radius = Mathf.Sqrt(radialSquared);
            float limit = SkinRadius(height, Mathf.Atan2(source.y, width)) + FabricClearance;
            if (radius <= limit) return false;

            float strength = Feather((height - .995f) / .085f) *
                             Feather((1.355f - height) / .06f) *
                             Feather((.205f - width) / .052f);
            float compression = Mathf.Min(radius - limit, MaximumCompression) * strength;
            if (compression < .0001f) return false;

            float scale = (radius - compression) / radius;
            fitted.x *= scale;
            fitted.y *= scale;
            return true;
        }

        static float SkinRadius(float height, float angle)
        {
            int lastHeight = TorsoRadii.GetLength(0) - 1;
            int lastAngle = TorsoRadii.GetLength(1) - 1;
            float h = Mathf.Clamp((height - FirstSlice) / SliceStep, 0f, lastHeight);
            float a = Mathf.Clamp((angle / Mathf.PI + .5f) * lastAngle, 0f, lastAngle);
            int row = Mathf.Min((int)h, lastHeight - 1);
            int column = Mathf.Min((int)a, lastAngle - 1);
            float low = Mathf.Lerp(TorsoRadii[row, column], TorsoRadii[row, column + 1], a - column);
            float high = Mathf.Lerp(TorsoRadii[row + 1, column], TorsoRadii[row + 1, column + 1], a - column);
            return Mathf.Lerp(low, high, h - row) * .001f;
        }

        static float Feather(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }
    }
}

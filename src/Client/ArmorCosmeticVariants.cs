using Alta.Customization;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorCosmeticVariants
    {
        internal static Mesh[] SelectLods(ICosmeticMeshPartBase part, uint armorMask, bool nativeImpeded)
        {
            if (part == null) return null;
            if (nativeImpeded) return part.ImpededLods;
            var normal = part.Lods;
            if (armorMask == 0 || part is SkinMeshPart || part.IsTextureSharingForced || !HasCompleteAlternative(part))
                return normal;

            var ownSlots = part.Slots;
            var regions = ownSlots != null && ownSlots.Length != 0 ? ownSlots : part.Visual?.PrimarySlots;
            if (regions == null) return normal;
            bool hasRegion = false;
            foreach (int region in regions)
            {
                // A native compact mesh can change the whole garment. Every
                // declared region must be armored before selecting it. This
                // keeps a one-sided pauldron from shortening both shirt sleeves.
                if (!ArmorCosmeticCoverage.CoversSlot(armorMask, region)) return normal;
                hasRegion = true;
            }
            return hasRegion ? part.ImpededLods : normal;
        }

        static bool HasCompleteAlternative(ICosmeticMeshPartBase part)
        {
            var normal = part.Lods;
            var compact = part.ImpededLods;
            if (normal == null || compact == null || normal.Length == 0 || compact.Length != normal.Length)
                return false;
            bool changed = false;
            for (int i = 0; i < normal.Length; i++)
            {
                if (normal[i] == null)
                {
                    if (compact[i] != null) return false;
                    continue;
                }
                // Missing or empty impeded meshes mean native hiding, rather
                // than a usable fitted garment. Preserve every visible LOD.
                if (compact[i] == null || compact[i].vertexCount == 0 || !compact[i].isReadable) return false;
                changed |= compact[i] != normal[i];
            }
            return changed;
        }
    }
}

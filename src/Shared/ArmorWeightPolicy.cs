using System;

namespace PlayerArmorMod
{
    internal static class ArmorWeightPolicy
    {
        internal static float BaseWeight(ArmorStyle style, ArmorSlot slot)
        {
            if (ArmorCatalog.IsStandaloneHelmet(style))
            {
                if (slot != ArmorSlot.Head) throw new ArgumentOutOfRangeException(nameof(slot));
                return style==ArmorStyle.NewsboyHelmet?.5f:1.5f;
            }
            float setWeight;
            switch (style)
            {
                case ArmorStyle.Metal: setWeight = 14f; break;
                case ArmorStyle.Forgeguard: setWeight = 24f; break;
                case ArmorStyle.Hebios: setWeight = 12f; break;
                case ArmorStyle.Wyrm:
                case ArmorStyle.CrystalWyrm: setWeight = 9f; break;
                case ArmorStyle.Woodland: setWeight = 8f; break;
                case ArmorStyle.Leather: setWeight = 6f; break;
                case ArmorStyle.Mushroom: setWeight = 5f; break;
                default: throw new ArgumentOutOfRangeException(nameof(style));
            }

            float share;
            switch (slot)
            {
                case ArmorSlot.Head: share = 1.5f; break;
                case ArmorSlot.Chest: share = 3f; break;
                case ArmorSlot.LeftShoulder:
                case ArmorSlot.RightShoulder: share = 1.25f; break;
                case ArmorSlot.LeftForearm:
                case ArmorSlot.RightForearm: share = 1f; break;
                case ArmorSlot.Hips: share = 2f; break;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
            return setWeight * share / 11f;
        }

        internal static float MaterialMultiplier(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0 ? value : 1f;
        }

        internal static float EffectiveWeight(float baseWeight, float materialMultiplier)
        {
            // Clamp invalid extremes from external material definitions before assigning physics mass.
            return (float)Math.Max(.01, Math.Min(100, (double)baseWeight * MaterialMultiplier(materialMultiplier)));
        }

        internal static int PlateCount(ArmorStyle style, ArmorSlot slot, uint layout, int paletteCount)
        {
            if (!ArmorCatalog.UsesMetalMaterials(style) || paletteCount < 1 || paletteCount > ArmorMaterialPolicy.MaximumPrimaryMaterials)
                return 0;
            if (layout == ArmorMaterialPolicy.LegacyBands) return paletteCount;
            if (layout != ArmorMaterialPolicy.SemanticParts) return 0;
            int primaryCount = ArmorConstructionPlan.PrimaryCount(style, slot);
            int withFastener = primaryCount + (ArmorCatalog.TintsFasteners(style) ? ArmorConstructionPlan.FastenerCount(style, slot) : 0);
            return paletteCount == primaryCount || paletteCount == withFastener ? primaryCount : 0;
        }
    }
}

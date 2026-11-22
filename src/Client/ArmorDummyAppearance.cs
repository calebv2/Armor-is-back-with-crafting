using System;
using System.Reflection;
using Alta.Customization;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorDummyAppearance
    {
        static readonly FieldInfo OpaqueMaterial = AccessTools.Field(typeof(CosmeticPacker), "material");
        static readonly FieldInfo TransparentMaterial = AccessTools.Field(typeof(CosmeticPacker), "blendMaterial");

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var materialize = AccessTools.Method(typeof(RendererMaterializer), nameof(RendererMaterializer.MaterializeRenderers));
            if (materialize == null || OpaqueMaterial == null || TransparentMaterial == null)
            {
                Core.Error("Player Armor could not find the native Equipment dummy materials.");
                return;
            }

            harmony.Patch(materialize, prefix: new HarmonyMethod(typeof(ArmorDummyAppearance), nameof(AddAppearanceRestore)));
        }

        static void AddAppearanceRestore(RendererMaterializer __instance, ref Action animationFinishedCallback)
        {
            var room = __instance.GetComponentInParent<EquipmentCustomizationRoom>();
            if (room == null || room.Player == null || !ReferenceEquals(room.Player, Player.Current) || room.Dummy == null) return;

            // The native fade saves materials before the async cosmetic pack can
            // finish. Its reset can then replace the packed atlas with the prefab
            // material. Reapply the packer's own materials after that reset.
            Action previous = animationFinishedCallback;
            animationFinishedCallback = () =>
            {
                if (room != null && room.Dummy != null && room.Dummy.Manager != null && ReferenceEquals(room.Player, Player.Current))
                    RestorePackedMaterials(room.Dummy.CosmeticPacker, room.Dummy.Clothes);
                previous?.Invoke();
            };
        }

        static void RestorePackedMaterials(CosmeticPacker packer, Alta.Character.ClothesSettings clothes)
        {
            if (packer == null || clothes == null) return;
            if (OpaqueMaterial.GetValue(packer) is Material opaque && opaque != null)
                clothes.SetMaterialInstance(opaque);
            if (TransparentMaterial.GetValue(packer) is Material transparent && transparent != null)
                clothes.SetTransparentMaterialInstance(transparent);
        }
    }
}

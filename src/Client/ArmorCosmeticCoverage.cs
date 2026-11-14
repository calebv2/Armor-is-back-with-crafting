using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Alta.Character;
using Alta.Customization;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorCosmeticCoverage
    {
        sealed class Coverage
        {
            internal uint Mask;
            internal readonly ArmorCosmeticShell[] Shells = new ArmorCosmeticShell[PlayerArmorRigBinding.SlotCount];
            internal readonly Dictionary<(Mesh, uint), Mesh> Fitted = new Dictionary<(Mesh, uint), Mesh>();

            internal void ClearFitted()
            {
                foreach (var mesh in Fitted.Values)
                    if (mesh != null) UnityEngine.Object.Destroy(mesh);
                Fitted.Clear();
            }
        }

        static readonly FieldInfo BindPosesField = AccessTools.Field(typeof(CosmeticPacker), "defaultBindposes");
        static ConditionalWeakTable<CosmeticPacker, Coverage> packers = new ConditionalWeakTable<CosmeticPacker, Coverage>();
        static ConditionalWeakTable<CosmeticPacker.Input, Coverage> inputs = new ConditionalWeakTable<CosmeticPacker.Input, Coverage>();
        static HarmonyLib.Harmony harmony;
        static MethodInfo scheduler;
        static MethodInfo pack;
        static MethodInfo destroy;
        static bool warned;

        internal static void Install(HarmonyLib.Harmony owner)
        {
            if (harmony != null) return;
            try
            {
                scheduler = AccessTools.Method(typeof(CosmeticPacker), nameof(CosmeticPacker.PackFromScheduler));
                destroy = AccessTools.DeclaredMethod(typeof(CosmeticPacker), "OnDestroy");
                pack = AccessTools.Method(typeof(MeshPacker), nameof(MeshPacker.Pack), new[]
                {
                    typeof(Dictionary<CosmeticPacker.PartId, ICosmeticMeshPartBase>), typeof(Matrix4x4[]),
                    typeof(IEnumerable<CosmeticPacker.Input>), typeof(Mesh[]), typeof(bool?),
                    typeof(bool), typeof(int), typeof(CancellationToken)
                });
                if (scheduler == null || pack == null || destroy == null)
                    throw new MissingMethodException("Native cosmetic packing entry points");
                harmony = owner;
                owner.Patch(scheduler, prefix: new HarmonyMethod(typeof(ArmorCosmeticCoverage), nameof(BeforeScheduledPack)));
                owner.Patch(pack, prefix: new HarmonyMethod(typeof(ArmorCosmeticCoverage), nameof(BeforeMeshPack)));
                owner.Patch(destroy, prefix: new HarmonyMethod(typeof(ArmorCosmeticCoverage), nameof(BeforePackerDestroy)));
            }
            catch (Exception error)
            {
                Dispose();
                Core.Error("Player Armor cosmetic coverage unavailable: " + error.GetBaseException().Message);
            }
        }

        internal static Matrix4x4[] BindPoses(CosmeticPacker packer) =>
            packer == null ? null : BindPosesField?.GetValue(packer) as Matrix4x4[];

        internal static Dictionary<Transform, int> BoneIndices(CharacterDummy dummy, int bindPoseCount)
        {
            if (dummy?.Clothes == null || bindPoseCount == 0) return null;
            foreach (var renderer in dummy.Clothes.Renderers)
            {
                if (!(renderer is SkinnedMeshRenderer skin)) continue;
                var bones = skin.bones;
                if (bones.Length != bindPoseCount) continue;
                var indices = new Dictionary<Transform, int>();
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null && !indices.ContainsKey(bones[i])) indices.Add(bones[i], i);
                if (indices.Count != 0) return indices;
            }
            return null;
        }

        internal static void Set(CosmeticPacker packer, uint mask, ArmorCosmeticShell[] shells)
        {
            if (harmony == null || packer == null) return;
            if (!packers.TryGetValue(packer, out var coverage))
            {
                if (mask == 0) return;
                coverage = new Coverage();
                packers.Add(packer, coverage);
            }
            bool changed = coverage.Mask != mask;
            for (int i = 0; i < coverage.Shells.Length; i++)
            {
                var shell = (mask & (1u << i)) != 0 ? shells[i] : null;
                if (ReferenceEquals(coverage.Shells[i], shell)) continue;
                coverage.Shells[i] = shell;
                changed = true;
            }
            if (!changed) return;
            coverage.Mask = mask;
            coverage.ClearFitted();
            if (mask != 0) TrackInputs(packer, coverage);
            packer.PackAndApply();
        }

        static void BeforePackerDestroy(CosmeticPacker __instance)
        {
            if (!packers.TryGetValue(__instance, out var coverage)) return;
            coverage.Mask = 0;
            coverage.ClearFitted();
            packers.Remove(__instance);
        }

        // ATT snapshots inputs at the start of a pack. Tag the current inputs
        // here as well so wardrobe edits and newly loaded cosmetics are covered
        // on their first pack, including while an avatar is already armored.
        static void BeforeScheduledPack(CosmeticPacker __instance)
        {
            if (packers.TryGetValue(__instance, out var coverage) && coverage.Mask != 0)
                TrackInputs(__instance, coverage);
        }

        static void TrackInputs(CosmeticPacker packer, Coverage coverage)
        {
            foreach (var input in packer.Inputs.Values)
            {
                if (input == null) continue;
                if (inputs.TryGetValue(input, out var previous))
                {
                    if (ReferenceEquals(previous, coverage)) continue;
                    inputs.Remove(input);
                }
                inputs.Add(input, coverage);
            }
        }

        // The temporary map supplies fitted meshes. Native inputs, colors,
        // topology, UVs, skin weights and the shared asset stay intact.
        static void BeforeMeshPack(ref Dictionary<CosmeticPacker.PartId, ICosmeticMeshPartBase> map,
            IEnumerable<CosmeticPacker.Input> parts, bool? isUsingHeadParts, bool isAlphaBlending,
            CancellationToken cancellation)
        {
            if (map == null || parts == null || cancellation.IsCancellationRequested) return;
            Dictionary<CosmeticPacker.PartId, ICosmeticMeshPartBase> fittedMap = null;
            foreach (var input in parts)
            {
                if (input?.Arguments == null || input.IsAlphaBlending != isAlphaBlending ||
                    (isUsingHeadParts.HasValue && input.IsOnHead != isUsingHeadParts.Value) ||
                    !inputs.TryGetValue(input, out var coverage) || coverage.Mask == 0 ||
                    !map.TryGetValue(input.PartId, out var part)) continue;

                uint mask = CoveredSlots(coverage.Mask, part);
                if (mask == 0) continue;
                bool impeded = input.Arguments.IsImpeded;
                var lods = impeded ? part.ImpededLods : part.Lods;
                if (lods == null) continue;
                Mesh[] fittedLods = null;
                for (int i = 0; i < lods.Length; i++)
                {
                    var fitted = TryFit(coverage, lods[i], mask);
                    if (fitted == null) continue;
                    if (fittedLods == null) fittedLods = (Mesh[])lods.Clone();
                    fittedLods[i] = fitted;
                }
                if (fittedLods == null) continue;
                if (fittedMap == null)
                    fittedMap = new Dictionary<CosmeticPacker.PartId, ICosmeticMeshPartBase>(map, map.Comparer);
                fittedMap[input.PartId] = new FittedPart(part, fittedLods, impeded);
            }
            if (fittedMap != null) map = fittedMap;
        }

        static Mesh TryFit(Coverage coverage, Mesh source, uint mask)
        {
            try { return Fit(coverage, source, mask); }
            catch (Exception error)
            {
                if (!warned)
                {
                    warned = true;
                    Core.Error("Player Armor could not fit a cosmetic mesh: " + error.GetBaseException().Message);
                }
                return null;
            }
        }

        static Mesh Fit(Coverage coverage, Mesh source, uint mask)
        {
            if (source == null || !source.isReadable) return null;
            var key = (source, mask);
            if (coverage.Fitted.TryGetValue(key, out var cached)) return cached;

            var vertices = source.vertices;
            bool changed = false;
            for (int i = 0; i < vertices.Length; i++)
            {
                for (int slot = 0; slot < coverage.Shells.Length; slot++)
                {
                    var shell = coverage.Shells[slot];
                    if ((mask & (1u << slot)) == 0 || shell == null ||
                        !shell.TryTuck(vertices[i], out var tucked)) continue;
                    vertices[i] = tucked;
                    changed = true;
                }
            }

            Mesh fitted = null;
            if (coverage.Fitted.Count >= 96) coverage.ClearFitted();
            if (changed)
            {
                fitted = UnityEngine.Object.Instantiate(source);
                try
                {
                    fitted.hideFlags = HideFlags.DontSave;
                    fitted.vertices = vertices;
                    fitted.RecalculateBounds();
                }
                catch
                {
                    UnityEngine.Object.Destroy(fitted);
                    throw;
                }
            }
            coverage.Fitted.Add(key, fitted);
            return fitted;
        }

        static uint CoveredSlots(uint mask, ICosmeticMeshPartBase part)
        {
            if (part == null || part is SkinMeshPart || part.IsTextureSharingForced) return 0;
            var slots = part.Slots;
            // Secondary visual parts can omit their own slots.
            var regions = slots != null && slots.Length != 0 ? slots : part.Visual?.PrimarySlots;
            if (regions == null) return 0;
            uint covered = 0;
            foreach (int slot in regions)
                for (uint flag = 1; flag <= PlayerArmorRigBinding.AllSlots; flag <<= 1)
                    if ((mask & flag) != 0 && CoversSlot(flag, slot)) covered |= flag;
            return covered;
        }

        internal static bool CoversSlot(uint mask, int slot)
        {
            // Native PlayerCustomizationSlot values are ordered by body region.
            if ((mask & (1u << (int)ArmorSlot.Head)) != 0 &&
                (Between(slot, 201, 275) || Between(slot, 1, 5) || slot == (int)PlayerCustomizationSlot.Hair)) return true;
            if ((mask & (1u << (int)ArmorSlot.Chest)) != 0 &&
                (Between(slot, 1, 55) || Between(slot, 181, 195) || Between(slot, 271, 285) || slot == (int)PlayerCustomizationSlot.Sash)) return true;
            if ((mask & (1u << (int)ArmorSlot.LeftShoulder)) != 0 &&
                (Between(slot, 81, 85) || Between(slot, 101, 105))) return true;
            if ((mask & (1u << (int)ArmorSlot.RightShoulder)) != 0 &&
                (Between(slot, 91, 95) || Between(slot, 111, 115))) return true;
            if ((mask & (1u << (int)ArmorSlot.LeftForearm)) != 0 &&
                (Between(slot, 121, 125) || Between(slot, 141, 145) || Between(slot, 161, 165))) return true;
            if ((mask & (1u << (int)ArmorSlot.RightForearm)) != 0 &&
                (Between(slot, 131, 135) || Between(slot, 151, 155) || Between(slot, 171, 175))) return true;
            return (mask & (1u << (int)ArmorSlot.Hips)) != 0 &&
                (Between(slot, 51, 75) || slot == (int)PlayerCustomizationSlot.Toolbelt);
        }

        static bool Between(int value, int first, int last) => value >= first && value <= last;

        internal static void Dispose()
        {
            if (harmony != null)
            {
                harmony.Unpatch(scheduler, AccessTools.Method(typeof(ArmorCosmeticCoverage), nameof(BeforeScheduledPack)));
                harmony.Unpatch(pack, AccessTools.Method(typeof(ArmorCosmeticCoverage), nameof(BeforeMeshPack)));
                harmony.Unpatch(destroy, AccessTools.Method(typeof(ArmorCosmeticCoverage), nameof(BeforePackerDestroy)));
            }
            harmony = null;
            scheduler = null;
            pack = null;
            destroy = null;
            packers = new ConditionalWeakTable<CosmeticPacker, Coverage>();
            inputs = new ConditionalWeakTable<CosmeticPacker.Input, Coverage>();
            warned = false;
        }

        sealed class FittedPart : ICosmeticMeshPartBase
        {
            readonly ICosmeticMeshPartBase source;

            internal FittedPart(ICosmeticMeshPartBase source, Mesh[] lods, bool impeded)
            {
                this.source = source;
                Lods = impeded ? source.Lods : lods;
                ImpededLods = impeded ? lods : source.ImpededLods;
            }

            public Mesh[] Lods { get; }
            public Mesh[] ImpededLods { get; }
            public Texture2D Normal => source.Normal;
            public Texture2D Texture => source.Texture;
            public int WorkingIndex { get => source.WorkingIndex; set => source.WorkingIndex = value; }
            public bool IsGeneratingBackFaces => source.IsGeneratingBackFaces;
            public bool IsAlphaBlending => source.IsAlphaBlending;
            public bool IsTextureSharingForced => source.IsTextureSharingForced;
            public string name => source.name;
            public ICosmeticMeshPartBase CopyColorFrom => source.CopyColorFrom;
            public CosmeticPacker.PartId PartId => source.PartId;
            public bool IsDisabledOnAndroid => source.IsDisabledOnAndroid;
            public ICustomizationVisual Visual { get => source.Visual; set => source.Visual = value; }
            public int[] Slots => source.Slots;
            public int PartIndex => source.PartIndex;
            public bool IsPrimary => source.IsPrimary;

            public bool ApplyColorToArguments(CosmeticColor color, bool isPrimary, CustomizationArguments[] arguments) =>
                source.ApplyColorToArguments(color, isPrimary, arguments);
            public void GetCustomPickupColors(out CosmeticColor primary, out CosmeticColor secondary) =>
                source.GetCustomPickupColors(out primary, out secondary);
            public void Show(IEnumerable<ICustomizationHandler> handlers, CustomizationArguments arguments) => source.Show(handlers, arguments);
            public void ChangeArguments(IEnumerable<ICustomizationHandler> handlers, CustomizationArguments arguments) => source.ChangeArguments(handlers, arguments);
            public void Hide(IEnumerable<ICustomizationHandler> handlers) => source.Hide(handlers);
            public void UpdateFirstPerson(ICustomizationHandler handler, CustomizationArguments arguments) => source.UpdateFirstPerson(handler, arguments);
            public Task PostRandomize(PlayerCosmeticWrapper cosmeticWrapper) => source.PostRandomize(cosmeticWrapper);
        }
    }
}

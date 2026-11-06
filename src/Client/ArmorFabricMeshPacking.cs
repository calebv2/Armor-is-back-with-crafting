using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Alta.Customization;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorFabricMeshPacking
    {
        const int MaxCachedMeshes = 96;

        sealed class FittedMesh
        {
            internal Mesh Mesh;
            internal long LastUsed;
        }

        static readonly object Eligible = new object();
        static ConditionalWeakTable<CosmeticPacker.Input, object> inputs =
            new ConditionalWeakTable<CosmeticPacker.Input, object>();
        static readonly Dictionary<Mesh, FittedMesh> cache = new Dictionary<Mesh, FittedMesh>();
        static HarmonyLib.Harmony harmony;
        static MethodInfo target;
        static long sequence;
        static bool warned;

        internal static void Install(HarmonyLib.Harmony owner)
        {
            if (target != null) return;
            try
            {
                var pack = AccessTools.Method(typeof(MeshPacker), nameof(MeshPacker.Pack), new[]
                {
                    typeof(Dictionary<CosmeticPacker.PartId, ICosmeticMeshPartBase>), typeof(Matrix4x4[]),
                    typeof(IEnumerable<CosmeticPacker.Input>), typeof(Mesh[]), typeof(bool?),
                    typeof(bool), typeof(int), typeof(CancellationToken)
                });
                if (pack == null) throw new MissingMethodException(typeof(MeshPacker).FullName, nameof(MeshPacker.Pack));
                owner.Patch(pack, prefix: new HarmonyMethod(typeof(ArmorFabricMeshPacking), nameof(BeforePack)));
                harmony = owner;
                target = pack;
            }
            catch (Exception error)
            {
                Core.Error("Player Armor fabric fitting unavailable: " + error.GetBaseException().Message);
            }
        }

        internal static bool Set(CosmeticPacker.Input input)
        {
            if (target == null || input == null) return false;
            inputs.GetValue(input, _ => Eligible);
            return true;
        }

        internal static void Remove(CosmeticPacker.Input input)
        {
            if (input != null) inputs.Remove(input);
        }

        internal static void Dispose()
        {
            if (target != null)
                harmony.Unpatch(target, AccessTools.Method(typeof(ArmorFabricMeshPacking), nameof(BeforePack)));
            target = null;
            harmony = null;
            inputs = new ConditionalWeakTable<CosmeticPacker.Input, object>();
            foreach (var fitted in cache.Values)
                if (fitted.Mesh != null) UnityEngine.Object.Destroy(fitted.Mesh);
            cache.Clear();
            sequence = 0;
            warned = false;
        }

        static void BeforePack(ref Dictionary<CosmeticPacker.PartId, ICosmeticMeshPartBase> map,
            IEnumerable<CosmeticPacker.Input> parts, bool? isUsingHeadParts, bool isAlphaBlending,
            CancellationToken cancellation)
        {
            if (map == null || parts == null || cancellation.IsCancellationRequested) return;
            Dictionary<CosmeticPacker.PartId, ICosmeticMeshPartBase> fittedMap = null;
            foreach (var input in parts)
            {
                if (input == null || input.Arguments == null || input.Arguments.IsImpeded ||
                    input.IsAlphaBlending != isAlphaBlending ||
                    (isUsingHeadParts.HasValue && input.IsOnHead != isUsingHeadParts.Value) ||
                    !inputs.TryGetValue(input, out _) || !map.TryGetValue(input.PartId, out var part) || part == null)
                    continue;

                var lods = part.Lods;
                if (lods == null) continue;
                Mesh[] fittedLods = null;
                for (int i = 0; i < lods.Length; i++)
                {
                    var fitted = GetFittedMesh(lods[i]);
                    if (fitted == null) continue;
                    if (fittedLods == null) fittedLods = (Mesh[])lods.Clone();
                    fittedLods[i] = fitted;
                }
                if (fittedLods == null) continue;
                if (fittedMap == null)
                    fittedMap = new Dictionary<CosmeticPacker.PartId, ICosmeticMeshPartBase>(map, map.Comparer);
                fittedMap[input.PartId] = new FittedPart(part, fittedLods);
            }

            // Keep the native atlas and addressable parts intact. The game only
            // uses this local map to pack vertices into the selected avatar.
            if (fittedMap != null) map = fittedMap;
        }

        static Mesh GetFittedMesh(Mesh source)
        {
            if (source == null) return null;
            if (cache.TryGetValue(source, out var entry))
            {
                entry.LastUsed = ++sequence;
                return entry.Mesh;
            }

            Mesh fitted = null;
            try
            {
                if (ArmorFabricFit.TryFit(source, out var positions))
                {
                    fitted = UnityEngine.Object.Instantiate(source);
                    fitted.hideFlags = HideFlags.DontSave;
                    fitted.vertices = positions;
                    fitted.RecalculateBounds();
                }
            }
            catch (Exception error)
            {
                if (fitted != null) UnityEngine.Object.Destroy(fitted);
                fitted = null;
                if (!warned)
                {
                    warned = true;
                    Core.Error("Player Armor could not fit a cosmetic mesh: " + error.GetBaseException().Message);
                }
            }

            if (cache.Count >= MaxCachedMeshes)
            {
                Mesh oldest = null;
                long lastUse = long.MaxValue;
                foreach (var pair in cache)
                {
                    if (pair.Value.LastUsed >= lastUse) continue;
                    oldest = pair.Key;
                    lastUse = pair.Value.LastUsed;
                }
                if (!ReferenceEquals(oldest, null))
                {
                    var expired = cache[oldest].Mesh;
                    cache.Remove(oldest);
                    // Unity releases the object at frame end, after any native
                    // packing calls using this temporary mesh have returned.
                    if (expired != null) UnityEngine.Object.Destroy(expired);
                }
            }
            cache.Add(source, new FittedMesh { Mesh = fitted, LastUsed = ++sequence });
            return fitted;
        }

        sealed class FittedPart : ICosmeticMeshPartBase
        {
            readonly ICosmeticMeshPartBase source;

            internal FittedPart(ICosmeticMeshPartBase source, Mesh[] lods)
            {
                this.source = source;
                Lods = lods;
            }

            public Mesh[] Lods { get; }
            public Mesh[] ImpededLods => source.ImpededLods;
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

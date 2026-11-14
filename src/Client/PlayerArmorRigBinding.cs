using System;
using System.Collections.Generic;
using Alta.Character;
using Alta.Customization;
using Alta.Networking;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayerArmorMod
{
    /// <summary>
    /// Client-only presentation for an explicitly authorized, real player.
    /// This class owns its empty sockets and armor instances, never the player
    /// skeleton, native clothing, source prefab meshes, or shared materials.
    /// It does not grant armor, consume inventory, or publish equipment state.
    /// </summary>
    internal sealed class PlayerArmorRigBinding : IDisposable
    {
        internal const int SlotCount = 7;
        internal const uint AllSlots = (1u << SlotCount) - 1u;
        internal static readonly string[] SlotNames =
        {
            "Head", "Chest", "LeftShoulder", "RightShoulder", "LeftForearm", "RightForearm", "Hips"
        };
        internal static readonly string[] BoneNames =
        {
            "head_jnt", "chest_jnt", "shoulder_L_jnt", "shoulder_R_jnt",
            "forearm_L_jnt", "forearm_R_jnt", "hipBase_jnt"
        };

        private sealed class TemplatePart
        {
            internal Transform Source;
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
        }
        private sealed class VisualPart
        {
            internal Transform Bone, Socket;
            internal GameObject Visual;
            internal Renderer[] Renderers;
            internal ShadowCastingMode[] OriginalShadows;
            internal List<OriginalArmorArt.Frame> OriginalFrames;
            internal bool Leather;
            internal ArmorStyle Style;
            internal ArmorMaterialAppearance Appearance;
            internal ArmorMaterialState MaterialState;
            internal uint MaterialPickup,MaterialRevision=uint.MaxValue;
            internal uint MaterialLayout=1;
            internal uint[] MaterialHashes=new uint[0];
            internal float NextMaterialLookup;
            internal float NextAppearanceRetry;
        }
        private sealed class HandlerBinding
        {
            internal PlayerCustomizationHandler Handler;
            internal CharacterDummy Dummy;
            internal CosmeticPacker Packer;
            internal Matrix4x4[] CosmeticBindPoses;
            internal Dictionary<Transform, int> CosmeticBones;
            internal readonly ArmorCosmeticShell[] CosmeticShells = new ArmorCosmeticShell[SlotCount];
            internal readonly GameObject[] FittedVisuals = new GameObject[SlotCount];
            internal Transform Root;
            internal readonly VisualPart[] Parts = new VisualPart[SlotCount];
        }

        private readonly TemplatePart[] templates = new TemplatePart[SlotCount];
        private readonly TemplatePart[] leatherTemplates = new TemplatePart[SlotCount];
        private readonly TemplatePart[] woodlandTemplates = new TemplatePart[SlotCount];
        private readonly TemplatePart[] hebiosTemplates = new TemplatePart[SlotCount];
        private readonly TemplatePart[] mushroomTemplates = new TemplatePart[SlotCount];
        private readonly TemplatePart[] wyrmTemplates = new TemplatePart[SlotCount];
        private readonly TemplatePart[] forgeguardTemplates = new TemplatePart[SlotCount];
        private readonly TemplatePart[] crystalWyrmTemplates = new TemplatePart[SlotCount];
        private readonly TemplatePart[] standaloneHelmets = new TemplatePart[ArmorCatalog.StandaloneHelmets.Count];
        private readonly Dictionary<PlayerCustomizationHandler, HandlerBinding> handlers =
            new Dictionary<PlayerCustomizationHandler, HandlerBinding>();
        private readonly Dictionary<Material, Material> nonEmissiveCopies = new Dictionary<Material, Material>();
        private readonly HashSet<string> warnings = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<PlayerCustomizationHandler> stale = new List<PlayerCustomizationHandler>();
        private readonly HashSet<PlayerCustomizationHandler> desired = new HashSet<PlayerCustomizationHandler>();
        private readonly Action<string> logger;
        private PlayerCharacter player;
        private object networkPlayer;
        private uint mask;
        private uint appliedLeatherMask;
        private uint appliedStyleBits;
        private readonly uint[] pickupIds=new uint[SlotCount];
        private float nextReconcileAt;
        private bool disposed;

        internal PlayerArmorRigBinding(GameObject template, Action<string> log = null, GameObject wyrmTemplate = null,
            GameObject crystalWyrmTemplate = null, GameObject standaloneHelmetsTemplate = null, GameObject forgeguardTemplate = null)
        {
            if (template == null) throw new ArgumentNullException("template");
            logger = log;
            Transform[] source = template.GetComponentsInChildren<Transform>(true);
            Transform leather = template.transform.Find("Leather");
            Transform pickups = template.transform.Find("Pickups");
            for (int slot = 0; slot < SlotCount; slot++)
            {
                Transform part = template.transform.Find("Armor_"+SlotNames[slot])??FindTemplate(source, slot, leather, pickups);
                if (part == null) throw new ArgumentException("Armor template has no part for " + SlotNames[slot], "template");
                templates[slot] = ReadTemplate(part, SlotNames[slot]);
                Transform leatherPart = leather == null ? null : leather.Find("Armor_" + SlotNames[slot]);
                if (leatherPart != null) leatherTemplates[slot] = ReadTemplate(leatherPart, "Leather/" + SlotNames[slot]);
                string woodlandPath="Woodland/Armor_"+SlotNames[slot];
                Transform woodlandPart=slot==(int)ArmorSlot.Head&&standaloneHelmetsTemplate!=null
                    ? standaloneHelmetsTemplate.transform.Find(woodlandPath)
                    : template.transform.Find(woodlandPath);
                Transform hebiosPart=template.transform.Find("Hebios/Armor_"+SlotNames[slot]);
                Transform mushroomPart=template.transform.Find("Mushroom/Armor_"+SlotNames[slot]);
                if(woodlandPart!=null)woodlandTemplates[slot]=ReadTemplate(woodlandPart,"Woodland/"+SlotNames[slot]);
                if(hebiosPart!=null)hebiosTemplates[slot]=ReadTemplate(hebiosPart,"Hebios/"+SlotNames[slot]);
                if(mushroomPart!=null)mushroomTemplates[slot]=ReadTemplate(mushroomPart,"Mushroom/"+SlotNames[slot]);
                var wyrmPart=wyrmTemplate==null?null:wyrmTemplate.transform.Find("Wyrm/Armor_"+SlotNames[slot]);
                if(wyrmPart!=null)wyrmTemplates[slot]=ReadTemplate(wyrmPart,"Wyrm/"+SlotNames[slot]);
                var crystalWyrmPart = crystalWyrmTemplate == null ? null : crystalWyrmTemplate.transform.Find("Crystal/Armor_" + SlotNames[slot]);
                if (crystalWyrmPart != null) crystalWyrmTemplates[slot] = ReadTemplate(crystalWyrmPart, "Crystal/" + SlotNames[slot]);
                var forgeguardPart=forgeguardTemplate==null?null:forgeguardTemplate.transform.Find("Forgeguard/Armor_"+SlotNames[slot]);
                if(forgeguardPart!=null)forgeguardTemplates[slot]=ReadTemplate(forgeguardPart,"Forgeguard/"+SlotNames[slot]);
            }
            if (standaloneHelmetsTemplate != null)
                foreach (var definition in ArmorCatalog.StandaloneHelmets)
                {
                    var part = standaloneHelmetsTemplate.transform.Find(definition.ArtRootName);
                    if (part != null) standaloneHelmets[ArmorCatalog.HelmetIndex(definition.Style)] = ReadTemplate(part, definition.ArtRootName);
                }
        }

        private static TemplatePart ReadTemplate(Transform part, string name)
        {
            // Asset export must strip preview/custom scripts. Do not clone
            // an executable script under the live player's skeleton.
            if (part.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                throw new ArgumentException("Armor template part contains scripts: " + name, "template");
            return new TemplatePart
            {
                Source = part, Position = part.localPosition,
                Rotation = part.localRotation, Scale = part.localScale
            };
        }

        /// <summary>
        /// Apply the server-authorized seven-bit equipment mask. Uniform player
        /// scale is inherited from the native bones; never multiply it twice.
        /// Refreshing the same owner/mask does not duplicate armor instances.
        /// Leather bits select the style of occupied slots only; style changes
        /// replace the owned visual and use that variant's bone-local TRS.
        /// </summary>
        internal void Update(PlayerController controller, uint equippedMask, uint leatherMask = 0, IList<uint> itemIds = null,uint? styleBits=null)
        {
            if (disposed) return;
            PlayerCharacter actual = controller as PlayerCharacter;
            // A clone/NPC may expose the same named rig. It is deliberately
            // insufficient: only a linked PlayerCharacter is eligible here.
            if (actual == null || actual.NetworkPlayer == null || actual.Entity == null)
            {
                ClearBindings(); player = null; networkPlayer = null; mask = appliedLeatherMask = 0;appliedStyleBits=0; return;
            }
            bool ownerChanged = !object.ReferenceEquals(player, actual) || !object.ReferenceEquals(networkPlayer, actual.NetworkPlayer);
            uint requestedMask = actual.IsSpirit ? 0u : equippedMask & AllSlots;
            uint requestedLeather = leatherMask & requestedMask;
            uint requestedStyles=actual.IsSpirit?0u:(styleBits??ArmorCatalog.PackLegacyLeather((byte)requestedLeather));
            if(!ArmorCatalog.ValidStyles((byte)requestedMask,requestedStyles)){ClearBindings();mask=appliedLeatherMask=0;appliedStyleBits=0;return;}
            bool changed = ownerChanged || mask != requestedMask || appliedStyleBits!=requestedStyles;
            if (ownerChanged)
            {
                ClearBindings(); player = actual; networkPlayer = actual.NetworkPlayer; nextReconcileAt = 0;
            }
            mask = requestedMask; appliedLeatherMask = requestedLeather;appliedStyleBits=requestedStyles;
            for(int slot=0;slot<SlotCount;slot++)pickupIds[slot]=(requestedMask&(1u<<slot))!=0&&itemIds!=null&&itemIds.Count==SlotCount?itemIds[slot]:0;
            if (changed || Time.unscaledTime >= nextReconcileAt || HasLostRig())
            {
                nextReconcileAt = Time.unscaledTime + .35f;
                Reconcile();
            }
            foreach (HandlerBinding binding in handlers.Values) UpdatePresentation(binding);
        }

        internal Renderer[] GetDummyRenderers(Transform dummyRoot,int slot,uint expectedPickup)
        {
            if(disposed||dummyRoot==null||slot<0||slot>=SlotCount||expectedPickup==0||
               pickupIds[slot]!=expectedPickup||(mask&(1u<<slot))==0)return null;
            foreach(var binding in handlers.Values)
            {
                var part=binding.Parts[slot];
                if(part!=null&&part.Bone!=null&&part.Bone.IsChildOf(dummyRoot)&&part.Visual!=null&&part.Visual.activeInHierarchy)
                    return part.Renderers;
            }
            return null;
        }

        /// <summary>Primary live player socket, suitable for local proximity UI.</summary>
        internal bool TryGetConnectionPoint(int slot, out Transform point)
        {
            point = null;
            if (disposed || player == null || slot < 0 || slot >= SlotCount) return false;
            PlayerCustomizationHandler primary = player.CustomizationHandler;
            HandlerBinding binding;
            if (primary != null && handlers.TryGetValue(primary, out binding))
            {
                VisualPart part = binding.Parts[slot];
                if (part != null && part.Socket != null && part.Bone != null)
                {
                    point = part.Socket; return true;
                }
            }
            return false;
        }

        internal void RefreshNow()
        {
            if (disposed || player == null) return;
            nextReconcileAt = 0;
            Reconcile();
            foreach (HandlerBinding binding in handlers.Values) UpdatePresentation(binding);
        }

        private static Transform FindTemplate(Transform[] transforms, int slot, Transform leather, Transform pickups)
        {
            string flat = "Armor_" + SlotNames[slot];
            foreach (Transform part in transforms) if (part.name == flat &&
                (leather == null || !part.IsChildOf(leather)) && (pickups == null || !part.IsChildOf(pickups))) return part;
            // Compatibility with the editable source set: its sockets are
            // children of the correct native bones and preserve bone-local TRS.
            foreach (Transform part in transforms)
                if (part.parent != null && part.parent.name == BoneNames[slot] &&
                    (leather == null || !part.IsChildOf(leather)) &&
                    (pickups == null || !part.IsChildOf(pickups)) &&
                    part.name.StartsWith("Iron Armor - ", StringComparison.Ordinal)) return part;
            return null;
        }

        private bool HasLostRig()
        {
            foreach (HandlerBinding binding in handlers.Values)
            {
                if (!StillMatches(binding)) return true;
            }
            return false;
        }

        private void Reconcile()
        {
            desired.Clear();
            if (player == null || player.NetworkPlayer == null) { ClearBindings(); return; }
            PlayerCustomizationHandler primary = player.CustomizationHandler;
            AddDesired(primary);
            // Native customization can render the same actual player in a
            // mirror or another view. Those handlers share the player's manager.
            var manager = player.NetworkPlayer.Customization;
            if (manager != null)
                foreach (ICustomizationHandler candidate in manager.Handlers)
                    AddDesired(candidate as PlayerCustomizationHandler);
            stale.Clear();
            foreach (KeyValuePair<PlayerCustomizationHandler, HandlerBinding> entry in handlers)
            {
                HandlerBinding value = entry.Value;
                if (!desired.Contains(entry.Key) || !StillMatches(value)) stale.Add(entry.Key);
            }
            foreach (PlayerCustomizationHandler key in stale)
            {
                Release(handlers[key]); handlers.Remove(key);
            }
            foreach (PlayerCustomizationHandler handler in desired)
            {
                if (handlers.ContainsKey(handler)) continue;
                HandlerBinding binding = CreateBinding(handler);
                if (binding != null) handlers.Add(handler, binding);
            }
        }

        private void AddDesired(PlayerCustomizationHandler handler)
        {
            if (handler == null || handler.IsDestroyed || !handler.gameObject.activeInHierarchy) return;
            try
            {
                if (handler.CharacterDummy != null && handler.RootBone != null) desired.Add(handler);
            }
            catch (Exception) { /* Character dummy pointers can be between rigs during a clothing swap. */ }
        }

        private static bool StillMatches(HandlerBinding binding)
        {
            try
            {
                if (binding.Handler == null || binding.Handler.IsDestroyed || binding.Dummy == null || binding.Root == null) return false;
                if (binding.Handler.CharacterDummy != binding.Dummy || binding.Handler.RootBone != binding.Root) return false;
                if (binding.Dummy.CosmeticPacker != binding.Packer) return false;
                foreach (VisualPart part in binding.Parts)
                    if (part == null || part.Bone == null || part.Socket == null || !part.Bone.IsChildOf(binding.Root)) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        private HandlerBinding CreateBinding(PlayerCustomizationHandler handler)
        {
            var binding = new HandlerBinding { Handler = handler, Dummy = handler.CharacterDummy, Root = handler.RootBone };
            binding.Packer = binding.Dummy.CosmeticPacker;
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in binding.Root.GetComponentsInChildren<Transform>(true))
                if (!bone.name.StartsWith("PlayerArmor.", StringComparison.Ordinal) && !OriginalArmorArt.IsSourceBone(bone) && !bones.ContainsKey(bone.name)) bones.Add(bone.name, bone);
            // Require every native anchor before creating anything. This avoids
            // half-bound armor or accidentally using a clothing-preview rig.
            for (int slot = 0; slot < SlotCount; slot++)
                if (!bones.ContainsKey(BoneNames[slot]))
                {
                    Warn("Missing " + BoneNames[slot] + " on player armor rig; waiting for the complete player body.");
                    return null;
                }
            try
            {
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    TemplatePart template = templates[slot];
                    Transform socket = new GameObject("PlayerArmor.Socket." + SlotNames[slot]).transform;
                    socket.SetParent(bones[BoneNames[slot]], false);
                    socket.localPosition = template.Position;
                    socket.localRotation = template.Rotation;
                    socket.localScale = template.Scale;
                    binding.Parts[slot] = new VisualPart { Bone = bones[BoneNames[slot]], Socket = socket };
                }
                return binding;
            }
            catch (Exception error)
            {
                Release(binding); Warn("Could not create player armor sockets: " + error.GetBaseException().Message); return null;
            }
        }

        private void UpdatePresentation(HandlerBinding binding)
        {
            if (!StillMatches(binding)) return;
            int layer = BodyLayer(binding.Dummy);
            bool firstPerson = binding.Handler.IsFirstPerson;
            uint visibleArmor = 0;
            for (int slot = 0; slot < SlotCount; slot++)
            {
                VisualPart part = binding.Parts[slot];
                bool worn = (mask & (1u << slot)) != 0;
                ArmorStyle style=ArmorCatalog.StyleAt(appliedStyleBits,(ArmorSlot)slot);
                if (worn && part.Visual != null && part.Style != style) ReleaseVisual(part);
                if (worn && part.Visual == null) CreateVisual(binding, part, slot, layer, style);
                if (part.Visual == null) continue;
                if (worn) visibleArmor |= 1u << slot;
                if(part.OriginalFrames!=null)foreach(var frame in part.OriginalFrames)frame.Update();
                if (part.Visual.activeSelf != worn) part.Visual.SetActive(worn);
                if(part.OriginalFrames!=null)foreach(var frame in part.OriginalFrames)frame.SetVisible(worn);
                for (int i = 0; i < part.Renderers.Length; i++)
                {
                    Renderer renderer = part.Renderers[i]; if (renderer == null) continue;
                    renderer.gameObject.layer = layer;
                    // Same rule as native ClothesSettings.ToggleHeadRenderers:
                    // the local helmet casts shadows without filling the eyes.
                    renderer.shadowCastingMode = slot == 0 && firstPerson ? ShadowCastingMode.ShadowsOnly : part.OriginalShadows[i];
                }
                // Color is optional presentation. Activate the base armor and
                // preserve native frame/shadow rules even if a palette fails.
                try{UpdateMaterial(part,slot,style);}
                catch(Exception error)
                {
                    Warn("PLAYER_ARMOR_COLOR_FALLBACK style="+style+" slot="+SlotNames[slot]+" pickup="+pickupIds[slot]+" layout="+part.MaterialLayout+" materials="+part.MaterialHashes.Length+" reason="+error.GetType().Name+": "+error.Message);
                }
            }
            UpdateCosmeticFit(binding, ArmorSettings.Current.cosmeticCompatibilityEnabled ? visibleArmor : 0);
        }

        private void UpdateCosmeticFit(HandlerBinding binding, uint visibleArmor)
        {
            if (visibleArmor != 0 && binding.CosmeticBones == null)
            {
                binding.CosmeticBindPoses = ArmorCosmeticCoverage.BindPoses(binding.Packer);
                binding.CosmeticBones = ArmorCosmeticCoverage.BoneIndices(binding.Dummy, binding.CosmeticBindPoses?.Length ?? 0);
            }
            if (binding.CosmeticBones != null)
                for (int i = 0; i < SlotCount; i++)
                {
                    var part = binding.Parts[i];
                    if ((visibleArmor & (1u << i)) == 0 || part.Visual == binding.FittedVisuals[i]) continue;
                    binding.FittedVisuals[i] = part.Visual;
                    binding.CosmeticShells[i] = null;
                    try
                    {
                        binding.CosmeticShells[i] = ArmorCosmeticShell.Build((ArmorSlot)i, part.Bone, part.Renderers,
                            binding.CosmeticBones, binding.CosmeticBindPoses);
                    }
                    catch (Exception error)
                    {
                        Warn("Player Armor could not fit cosmetics for " + part.Style + " " + SlotNames[i] + ": " +
                            error.GetBaseException().Message);
                    }
                }
            ArmorCosmeticCoverage.Set(binding.Packer, visibleArmor, binding.CosmeticShells);
        }

        private void CreateVisual(HandlerBinding binding, VisualPart part, int slot, int layer, ArmorStyle style)
        {
            TemplatePart template;
            switch(style)
            {
                case ArmorStyle.Metal: template=templates[slot]; break;
                case ArmorStyle.Leather: template=leatherTemplates[slot]; break;
                case ArmorStyle.Woodland: template=woodlandTemplates[slot]; break;
                case ArmorStyle.Hebios: template=hebiosTemplates[slot]; break;
                case ArmorStyle.Mushroom: template=mushroomTemplates[slot]; break;
                case ArmorStyle.Wyrm: template=wyrmTemplates[slot]; break;
                case ArmorStyle.CrystalWyrm: template=crystalWyrmTemplates[slot]; break;
                case ArmorStyle.Forgeguard: template=forgeguardTemplates[slot]; break;
                default:
                    template=slot==(int)ArmorSlot.Head&&ArmorCatalog.IsStandaloneHelmet(style)
                        ? standaloneHelmets[ArmorCatalog.HelmetIndex(style)] : null;
                    break;
            }
            if (template == null)
            {
                Warn("Armor template has no "+style+" part for " + SlotNames[slot] + "; leaving the requested slot hidden.");
                return;
            }
            GameObject instance = null;
            try
            {
                part.Socket.localPosition = template.Position;
                part.Socket.localRotation = template.Rotation;
                part.Socket.localScale = template.Scale;
                instance = UnityEngine.Object.Instantiate(template.Source.gameObject);
                instance.name = "PlayerArmor.Visual." + SlotNames[slot];
                instance.SetActive(false);
                instance.transform.SetParent(part.Socket, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false; UnityEngine.Object.Destroy(collider);
                }
                foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.detectCollisions = false; body.isKinematic = true; UnityEngine.Object.Destroy(body);
                }
                foreach (Light light in instance.GetComponentsInChildren<Light>(true)) { light.enabled = false; UnityEngine.Object.Destroy(light); }
                foreach (Transform child in instance.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                var original = new ShadowCastingMode[renderers.Length];
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer renderer = renderers[index]; original[index] = renderer.shadowCastingMode;
                    Material[] materials = renderer.sharedMaterials;
                    bool replaced = false;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        Material safe = NonEmissive(materials[m]);
                        if (safe != materials[m]) { materials[m] = safe; replaced = true; }
                    }
                    if (replaced) renderer.sharedMaterials = materials;
                }
                part.OriginalFrames=OriginalArmorArt.Bind(instance,binding.Root,true);
                if (slot == (int)ArmorSlot.LeftForearm || slot == (int)ArmorSlot.RightForearm)
                    ArmorBracerFit.Apply(instance, part.OriginalFrames, style, slot == (int)ArmorSlot.LeftForearm);
                part.Renderers = renderers; part.OriginalShadows = original; part.Visual = instance; part.Leather = style==ArmorStyle.Leather;part.Style=style;
            }
            catch (Exception error)
            {
                if(part.OriginalFrames!=null)foreach(var frame in part.OriginalFrames)frame.Dispose();
                part.OriginalFrames=null;
                if (instance != null) { instance.SetActive(false); UnityEngine.Object.Destroy(instance); }
                Warn("Could not show " + SlotNames[slot] + " on player: " + error.GetBaseException().Message);
            }
        }

        private void UpdateMaterial(VisualPart part,int slot,ArmorStyle style)
        {
            if(!ArmorCatalog.UsesCraftMaterials(style))return;
            if(part.Appearance==null)
            {
                if(Time.unscaledTime<part.NextAppearanceRetry)return;
                part.NextAppearanceRetry=Time.unscaledTime+1f;
                part.Appearance=new ArmorMaterialAppearance(part.Renderers,style,ArmorMaterialPalette.Resolve);
                part.NextAppearanceRetry=0;
            }
            uint id=pickupIds[slot];
            if(id!=part.MaterialPickup){part.MaterialPickup=id;part.MaterialState=null;part.MaterialRevision=uint.MaxValue;part.MaterialHashes=new uint[0];part.NextMaterialLookup=0;part.Appearance.Restore();}
            if(part.MaterialState==null&&id!=0&&Time.unscaledTime>=part.NextMaterialLookup)
            {
                part.NextMaterialLookup=Time.unscaledTime+.35f;var scene=NetworkSceneManager.Current;var entity=scene==null?null:scene.GetEntity(id);
                var marker=entity==null?null:entity.GetComponent<ArmorPickup>();
                if(marker!=null&&marker.Style==style&&marker.Slot==slot)
                {
                    var resolved=marker.GetComponent<ArmorMaterialState>();
                    if(!object.ReferenceEquals(part.MaterialState,resolved)){part.MaterialState=resolved;part.MaterialRevision=uint.MaxValue;part.MaterialHashes=new uint[0];part.Appearance.Restore();}
                }
            }
            if(part.MaterialState!=null&&part.MaterialRevision!=part.MaterialState.Revision){part.MaterialRevision=part.MaterialState.Revision;part.MaterialHashes=part.MaterialState.PrimaryMaterialHashes;part.MaterialLayout=part.MaterialState.LayoutVersion;}
            part.Appearance.Update(part.MaterialHashes,part.MaterialRevision,part.MaterialLayout,ArmorConstructionPlan.PrimaryCount(style,(ArmorSlot)slot),ArmorCatalog.TintsFasteners(style));
        }

        private static void RebindSkin(SkinnedMeshRenderer renderer, Transform root)
        {
            Transform[] targets = root.GetComponentsInChildren<Transform>(true);
            Transform[] sources = renderer.bones;
            var mapped = new Transform[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == null) throw new InvalidOperationException("Armor skin has an unresolved source bone.");
                foreach (Transform target in targets) if (target.name == sources[i].name) { mapped[i] = target; break; }
                if (mapped[i] == null) throw new InvalidOperationException("Armor skin is missing native bone " + sources[i].name);
            }
            renderer.bones = mapped; renderer.rootBone = root; renderer.updateWhenOffscreen = true;
        }

        private Material NonEmissive(Material source)
        {
            if (source == null) return null;
            Color emission = source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.black;
            if (!source.IsKeywordEnabled("_EMISSION") && Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b)) <= .00001f) return source;
            Material copy;
            if (nonEmissiveCopies.TryGetValue(source, out copy) && copy != null) return copy;
            copy = new Material(source) { name = source.name + " (player armor)", hideFlags = HideFlags.DontSave };
            if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
            copy.DisableKeyword("_EMISSION"); nonEmissiveCopies[source] = copy; return copy;
        }

        internal static int BodyLayer(CharacterDummy dummy)
        {
            foreach (Renderer renderer in dummy.MainRenderers) if (renderer != null) return renderer.gameObject.layer;
            foreach (Renderer renderer in dummy.Renderers) if (renderer != null) return renderer.gameObject.layer;
            return dummy.gameObject.layer;
        }

        private void Warn(string text)
        {
            if (logger == null || !warnings.Add(text)) return;
            try { logger(text); } catch (Exception) { }
        }
        private static void Release(HandlerBinding binding)
        {
            ArmorCosmeticCoverage.Set(binding.Packer, 0, null);
            foreach (VisualPart part in binding.Parts)
                if (part != null)
                {
                    ReleaseVisual(part);
                    if(part.Socket!=null){part.Socket.gameObject.SetActive(false);UnityEngine.Object.Destroy(part.Socket.gameObject);}
                }
        }
        private static void ReleaseVisual(VisualPart part)
        {
            if(part.Appearance!=null)part.Appearance.Dispose();part.Appearance=null;part.MaterialState=null;part.MaterialHashes=new uint[0];part.MaterialPickup=0;part.MaterialRevision=uint.MaxValue;part.NextAppearanceRetry=0;
            if(part.OriginalFrames!=null)foreach(var frame in part.OriginalFrames)frame.Dispose();
            if (part.Visual != null)
            {
                part.Visual.SetActive(false);
                part.Visual.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(part.Visual);
            }
            part.Visual = null; part.Renderers = null; part.OriginalShadows = null; part.OriginalFrames = null;
        }
        private void ClearBindings()
        {
            foreach (HandlerBinding binding in handlers.Values) Release(binding);
            handlers.Clear(); desired.Clear(); stale.Clear();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; ClearBindings(); player = null; networkPlayer = null; mask = appliedLeatherMask = 0;appliedStyleBits=0;
            foreach (Material material in nonEmissiveCopies.Values) if (material != null) UnityEngine.Object.Destroy(material);
            nonEmissiveCopies.Clear(); warnings.Clear();
        }
    }
}

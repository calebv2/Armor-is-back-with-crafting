using System;
using Alta.Inventory;
using Alta.Networking;
using UnityEngine;

namespace PlayerArmorMod
{
    // These sockets are world inventory. Each is a separate embedded entity so
    // the native dock serializer can save and restore the actual armor child.
    internal static class ArmorStandDocks
    {
        internal static void Install(GameObject root, NetworkPrefab prefab, Transform[] mounts, ArmorPrefabRegistry assets)
        {
            if (root == null || root.activeInHierarchy || prefab == null || prefab.gameObject != root)
                throw new ArgumentException("Armor stand docks require an inactive prefab root.");
            if (mounts == null || mounts.Length != ArmorCatalog.SlotCount || prefab.Embedded.Count != 0)
                throw new ArgumentException("Armor stand needs seven unused mount frames.");

            for (int i = 0; i < mounts.Length; ++i)
            {
                Transform mount = mounts[i];
                if (mount == null || !mount.IsChildOf(root.transform) || mount.name != ((ArmorSlot)i).ToString()
                    || mount.GetComponent<NetworkEntity>() != null)
                    throw new InvalidOperationException("Invalid armor stand bone frame for " + (ArmorSlot)i);

                var settings = ScriptableObject.CreateInstance<PickupDockSettings>();
                settings.name = "Armor stand " + (ArmorSlot)i;
                ArmorNative.Set(settings, "isStackingDisabled", true);
                ArmorNative.Set(settings, "allowPhysicalJoints", false);
                ArmorNative.Set(settings, "hoverOn", AnimationCurve.Linear(0, 1, .1f, 1));
                ArmorNative.Set(settings, "hoverOff", AnimationCurve.Linear(0, 1, .1f, 1));

                // The stock item uses an identity normal dock pose. Its real
                // grip and its art use the same bone frame, including gauntlets.
                Bounds envelope = new Bounds();
                int interactionLayer = -1;
                foreach (var definition in ArmorCatalog.AllPieces)
                {
                    if ((int)definition.Slot != i || definition.Style == ArmorStyle.Wyrm) continue;
                    var armor = assets.PiecesFor(definition.Style)[i];
                    var pickup = armor == null ? null : armor.GetComponent<Pickup>();
                    var box = armor == null ? null : armor.GetComponent<BoxCollider>();
                    if (box == null || pickup == null || pickup.Item == null)
                        throw new InvalidOperationException("Missing stand item " + definition.PrefabName);
                    var bounds = new Bounds(box.center, box.size);
                    if (interactionLayer < 0)
                    {
                        envelope = bounds;
                        interactionLayer = box.gameObject.layer;
                    }
                    else envelope.Encapsulate(bounds);
                    settings.IncludedItems.Add(pickup.Item);
                }
                if (settings.IncludedItems.Count == 0)
                    throw new InvalidOperationException("Empty armor stand slot " + (ArmorSlot)i);

                // Put the range and dock on the same object. Both the hand's
                // overlap query and PickupDockingChecker resolve it directly.
                var socket = new GameObject("Armor socket - " + (ArmorSlot)i);
                socket.transform.SetParent(mount, false);
                socket.layer = interactionLayer;
                var entity = socket.AddComponent<NetworkEntity>();
                ArmorNative.Set(entity, "hash", i + 1);
                var range = socket.AddComponent<BoxCollider>();
                envelope.Expand(.08f);
                range.center = envelope.center;
                range.size = envelope.size;
                range.isTrigger = true;
                var dock = socket.AddComponent<ArmorStandDock>();
                ArmorNative.Set(dock, "entity", entity);
                ArmorNative.Set(dock, "settings", settings);
                ArmorNative.Set(dock, "snapPoint", socket.transform);
                ArmorNative.Set(dock, "pickupRange", range);
                dock.Channels = InteractionChannel.Normal;
                // The collider stays discoverable when empty for depositing.
                // Native Dock/Undock explicitly toggles hand interaction.
                dock.IsInteractable = false;
                prefab.Embedded.Add(entity);
            }
        }

        // The saved stand keeps its existing registration ahead of the Wyrm
        // prefabs. Extend the same embedded sockets once those items have IDs.
        internal static void AddWyrm(NetworkPrefab stand, NetworkPrefab[] pieces)
        {
            for (int slot = 0; slot < ArmorCatalog.SlotCount; slot++)
            {
                var dock = stand.Embedded[slot].GetComponent<ArmorStandDock>();
                var armor = pieces[slot];
                var box = armor.GetComponent<BoxCollider>();
                var range = dock.GetComponent<BoxCollider>();
                var extra = new Bounds(box.center, box.size);
                extra.Expand(.08f);
                var combined = new Bounds(range.center, range.size);
                combined.Encapsulate(extra);
                range.center = combined.center;
                range.size = combined.size;
                dock.Settings.IncludedItems.Add(armor.GetComponent<Pickup>().Item);
            }
        }
    }

    public sealed class ArmorStandDock : PickupDock
    {
        public override bool AssessScore(Interactor interactor, out float score)
        {
            // Overlapping chest/shoulder ranges should offer whichever visible
            // piece the palm actually touches. Native pickup scoring consults
            // the same per-mesh surface query as it will use for the hand joint.
            if (!base.AssessScore(interactor, out score) || !HasDockedPickup) return false;
            return DockedPickup.AssessScore(interactor, out score) && score <= .13f * .13f;
        }
    }
}

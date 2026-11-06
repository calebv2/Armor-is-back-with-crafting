using System;
using Alta.Networking;
using UnityEngine;

namespace PlayerArmorMod
{
    // GoldCoin's stock prefab contains a separate tight-grip Rigidbody and an
    // embedded heat-point entity. Only its root native pickup implementation is
    // reused. The donor asset itself must never be normalized or modified.
    internal static class ArmorDonorLayout
    {
        internal static void Validate(NetworkPrefab donor)
        {
            if(donor==null)throw new ArgumentNullException("donor");
            var root=donor.gameObject;
            RequireRoot(root);
            if(root.GetComponent<NetworkPrefab>()!=donor)
                throw new InvalidOperationException("Armor donor must be the root NetworkPrefab.");
        }

        internal static void Normalize(GameObject clonedRoot)
        {
            if(clonedRoot==null)throw new ArgumentNullException("clonedRoot");
            RequireRoot(clonedRoot);
            // Clear the serialized references before destroying their targets;
            // NetworkPrefab.Initialize traverses Embedded during registration.
            clonedRoot.GetComponent<NetworkPrefab>().Embedded.Clear();
            while(clonedRoot.transform.childCount>0)
                UnityEngine.Object.DestroyImmediate(clonedRoot.transform.GetChild(0).gameObject);
            ValidateNormalized(clonedRoot);
        }

        internal static void ValidateNormalized(GameObject root)
        {
            if(root==null)throw new ArgumentNullException("root");
            RequireRoot(root);
            if(root.GetComponentsInChildren<NetworkPrefab>(true).Length!=1
                ||root.GetComponentsInChildren<NetworkEntity>(true).Length!=1
                ||root.GetComponentsInChildren<Pickup>(true).Length!=1
                ||root.GetComponentsInChildren<Rigidbody>(true).Length!=1
                ||root.GetComponent<NetworkPrefab>().Embedded.Count!=0)
                throw new InvalidOperationException("Normalized armor donor still contains child networking or physics.");
        }

        static void RequireRoot(GameObject root)
        {
            int prefabs=root.GetComponents<NetworkPrefab>().Length;
            int entities=root.GetComponents<NetworkEntity>().Length;
            int pickups=root.GetComponents<Pickup>().Length;
            int bodies=root.GetComponents<Rigidbody>().Length;
            if(prefabs!=1||entities!=1||pickups!=1||bodies!=1)
                throw new InvalidOperationException("Native armor pickup donor root is unsupported: NetworkPrefab="+prefabs
                    +", NetworkEntity="+entities+", Pickup="+pickups+", Rigidbody="+bodies
                    +"; expected one of each on the root.");
        }
    }
}

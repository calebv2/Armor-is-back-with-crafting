using System;
using System.Linq;
using System.Reflection;
using Alta.Impact;
using Alta.Networking;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorNative
    {
        internal static FieldInfo Field(Type type,string name)
        {
            for(var current=type;current!=null;current=current.BaseType)
            {
                var field=current.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                if(field!=null)return field;
            }
            throw new MissingFieldException(type.FullName,name);
        }
        internal static void Set(object target,string name,object value){Field(target.GetType(),name).SetValue(target,value);}
        internal static void Bind(GameObject root)
        {
            if(root==null)throw new ArgumentNullException("root");
            // The native prefab donor may have cached colliders that were removed
            // with its art. These groups and Pickup's Awake cache must describe
            // the replacement armor geometry before a spawned clone activates.
            foreach(var group in root.GetComponentsInChildren<ColliderGroup>(true))
                group.GetChildComponents(false);
            foreach(var pickup in root.GetComponentsInChildren<Pickup>(true))
            {
                var colliders=pickup.GetComponentsInChildren<Collider>(true);
                Set(pickup,"childrenColliders",colliders);
                pickup.Colliders.Clear();foreach(var collider in colliders)if(collider!=null)pickup.Colliders.Add(collider);
                Set(pickup,"rigidbody",pickup.GetComponent<Rigidbody>());
                Set(pickup,"networkRigidbody",pickup.GetComponent<NetworkRigidbody>());
            }
            foreach(var behaviour in root.GetComponentsInChildren<NetworkEntityBehaviour>(true))
            {
                var entity=behaviour.GetComponent<NetworkEntity>();
                if(entity==null)throw new InvalidOperationException("Armor behaviour has no entity: "+behaviour.GetType().Name);
                Set(behaviour,"entity",entity);
            }
            // These implement INetworkEntityBehaviour directly and are therefore
            // absent from the NetworkEntityBehaviour traversal above.
            foreach(var group in root.GetComponentsInChildren<NetworkColliderGroup>(true))
            {
                var entity=group.GetComponent<NetworkEntity>();
                if(entity==null)throw new InvalidOperationException("Armor network collider group has no entity: "+group.name);
                Set(group,"entity",entity);
            }
            foreach(var prefab in root.GetComponentsInChildren<NetworkPrefab>(true))
            {
                var entity=prefab.GetComponent<NetworkEntity>();
                if(entity==null)throw new InvalidOperationException("Armor network prefab has no entity: "+prefab.name);
                Set(prefab,"entity",entity);
            }
            foreach(var body in root.GetComponentsInChildren<NetworkRigidbody>(true))
            {
                // A freshly added SyncLib NetworkRigidbody needs this serialized
                // reference even though its runtime Topic cache is not serialized.
                Set(body,"cachedTransform",body.transform);body.UpdateRigidbody();
            }
            foreach(var entity in root.GetComponentsInChildren<NetworkEntity>(true))
            {
                var behaviours=entity.GetComponents<MonoBehaviour>().Where(b=>b is INetworkEntityBehaviour).ToArray();
                Set(entity,"networkEntityBehaviours",behaviours);Set(entity,"hasNetworkBehaviours",behaviours.Length>0);
                Set(entity,"prefab",entity.GetComponent<NetworkPrefab>());
                Set(entity,"commonPickup",entity.GetComponent<Pickup>());Set(entity,"commonInteractable",entity.GetComponent<Interactable>());
                Set(entity,"commonImpactTool",entity.GetComponent<ImpactTool>());
            }
        }
        internal static Bounds LocalBounds(GameObject art)
        {
            bool found=false;Bounds result=new Bounds();
            foreach(var filter in art.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null)continue;var bounds=filter.sharedMesh.bounds;
                for(int corner=0;corner<8;corner++)
                {
                    var point=bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                    point=art.transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if(!found){result=new Bounds(point,Vector3.zero);found=true;}else result.Encapsulate(point);
                }
            }
            foreach(var skin in art.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(skin.sharedMesh==null)continue;
                var baked=new Mesh();
                try
                {
                    skin.BakeMesh(baked);
                    foreach(var vertex in baked.vertices)
                    {
                        var point=art.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        if(!found){result=new Bounds(point,Vector3.zero);found=true;}else result.Encapsulate(point);
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(baked);}
            }
            if(!found)throw new InvalidOperationException("Armor art has no mesh bounds: "+art.name);
            return result;
        }
    }
}

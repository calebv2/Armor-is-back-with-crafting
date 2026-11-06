using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayerArmorMod
{
    // Queried only on a grip press at the active equipment dummy. No dummy
    // colliders or interactables enter the player's ordinary hand overlap list.
    internal sealed class ArmorDummyMeshSelection
    {
        readonly List<Vector3> vertices=new List<Vector3>();
        readonly List<int> indices=new List<int>();
        Mesh baked;

        internal float? Distance(Renderer[] renderers,Vector3 hand)
        {
            if(renderers==null)return null;
            float best=float.PositiveInfinity;
            bool hasSurface=false;
            foreach(var renderer in renderers)
            {
                if(renderer==null||!renderer.enabled||!renderer.gameObject.activeInHierarchy||
                   renderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly)continue;
                var skin=renderer as SkinnedMeshRenderer;
                var filter=skin==null?renderer.GetComponent<MeshFilter>():null;
                var mesh=skin!=null?skin.sharedMesh:filter!=null?filter.sharedMesh:null;
                if(mesh==null||(skin==null&&!mesh.isReadable))continue;
                if(renderer.bounds.SqrDistance(hand)>ArmorDummyInteraction.TakeRadius*ArmorDummyInteraction.TakeRadius)
                {
                    hasSurface=true;continue;
                }
                if(skin!=null)
                {
                    if(baked==null)baked=new Mesh();
                    skin.BakeMesh(baked);
                    mesh=baked;
                }
                mesh.GetVertices(vertices);
                var matrix=renderer.transform.localToWorldMatrix;
                for(int i=0;i<vertices.Count;i++)vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);
                for(int submesh=0;submesh<mesh.subMeshCount;submesh++)
                {
                    if(mesh.GetTopology(submesh)!=MeshTopology.Triangles)continue;
                    mesh.GetTriangles(indices,submesh);
                    for(int i=0;i+2<indices.Count;i+=3)
                    {
                        hasSurface=true;
                        var point=ArmorCraftingMesh.ClosestTrianglePoint(hand,vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]]);
                        float distance=(point-hand).sqrMagnitude;
                        if(distance<best)best=distance;
                    }
                }
            }
            return hasSurface?(float?)Mathf.Sqrt(best):null;
        }

        internal void Clear()
        {
            if(baked!=null)Object.Destroy(baked);
            baked=null;vertices.Clear();indices.Clear();
        }
    }
}

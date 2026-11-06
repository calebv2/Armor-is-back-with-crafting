using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayerArmorMod
{
    // Temporary placement/fastening cue only. The existing native workbench
    // and workpiece renderers remain the complete crafting presentation.
    public static class ArmorCraftingJigVisuals
    {
        static bool Finite(Vector3 p){return !(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z));}
        static void DestroyOwned(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        public sealed class View:IDisposable
        {
            public GameObject Root{get;private set;}
            Mesh mesh;Material material;GameObject marker;bool disposed;
            internal View(Transform parent,Material source)
            {
                if(parent==null||source==null)throw new ArgumentNullException("Crafting cue needs a parent and material");
                Root=new GameObject("PlayerArmor.CraftingGuide");Root.layer=parent.gameObject.layer;Root.transform.SetParent(parent,false);
                try
                {
                    material=new Material(source){name="Armor crafting joint cue",hideFlags=HideFlags.DontSave};material.enableInstancing=true;
                    material.mainTexture=Texture2D.whiteTexture;material.color=new Color(1f,.56f,.15f,1);
                    foreach(var name in new[]{"_UseVertexColor","_UseAtlas","_Glow"})if(material.HasProperty(name))material.SetFloat(name,0);
                    if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",Color.black);material.DisableKeyword("_EMISSION");
                    var vertices=new List<Vector3>();var indices=new List<int>();var uv=new List<Vector2>();const int segments=32,sides=6;
                    for(int i=0;i<segments;i++)
                    {
                        float a=i*Mathf.PI*2/segments;var radial=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                        for(int j=0;j<sides;j++){float b=j*Mathf.PI*2/sides;vertices.Add(radial*(.016f+Mathf.Cos(b)*.0013f)+Vector3.forward*(Mathf.Sin(b)*.0013f));uv.Add(new Vector2((float)i/segments,(float)j/sides));}
                    }
                    for(int i=0;i<segments;i++)for(int j=0;j<sides;j++){int a=i*sides+j,b=((i+1)%segments)*sides+j,c=((i+1)%segments)*sides+(j+1)%sides,d=i*sides+(j+1)%sides;indices.AddRange(new[]{a,c,b,a,d,c});}
                    mesh=new Mesh{name="Armor crafting joint cue",hideFlags=HideFlags.DontSave};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                    marker=new GameObject("Current joint cue");marker.layer=Root.layer;marker.transform.SetParent(Root.transform,false);marker.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=marker.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;marker.SetActive(false);
                }
                catch{Dispose();throw;}
            }
            public void SetGuidePoint(Vector3 point,Vector3 outward,bool shown)
            {
                if(disposed||marker==null)return;shown=shown&&Finite(point)&&Finite(outward)&&outward.sqrMagnitude>.00001f;
                marker.SetActive(shown);if(!shown)return;var normal=outward.normalized;marker.transform.localPosition=point+normal*.0025f;marker.transform.localRotation=Quaternion.FromToRotation(Vector3.forward,normal);
            }
            public void SetVisible(bool visible){if(!disposed&&Root!=null&&Root.activeSelf!=visible)Root.SetActive(visible);}
            public void Tick(float now){if(!disposed&&material!=null&&material.HasProperty("_Glow"))material.SetFloat("_Glow",.10f+.07f*(.5f+.5f*Mathf.Sin(now*3f)));}
            public void Dispose(){if(disposed)return;disposed=true;if(Root!=null)Root.SetActive(false);DestroyOwned(Root);DestroyOwned(mesh);DestroyOwned(material);Root=null;marker=null;mesh=null;material=null;}
        }
        public static View Create(Transform parent,Material source){return new View(parent,source);}
    }
}
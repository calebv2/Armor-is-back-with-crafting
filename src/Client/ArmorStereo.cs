using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace PlayerArmorMod
{
    internal static class ArmorStereo
    {
        const string Keyword="PLAYER_ARMOR_STEREO";
        static readonly HashSet<Material> materials=new HashSet<Material>();
        static readonly Stack<bool> cameras=new Stack<bool>();
        static bool installed,active;
        internal static void Register(Material material)
        {
            if(material==null)return;material.enableInstancing=true;
            if(active)material.EnableKeyword(Keyword);else material.DisableKeyword(Keyword);
            materials.Add(material);
        }
        internal static void Unregister(Material material)
        {if(!object.ReferenceEquals(material,null))materials.Remove(material);}
        internal static void Configure(GameObject template)
        {
            foreach(var renderer in template.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.sharedMaterials)
                if(material!=null&&material.shader!=null&&(material.shader.name=="PlayerArmor/Lit"||material.shader.name=="PlayerArmor/WyrmLit"||material.shader.name=="PlayerArmor/WorkshopLit"))
                Register(material);
            if(!installed){Camera.onPreCull+=Before;Camera.onPostRender+=After;installed=true;}
        }
        static void Before(Camera camera)
        {
            cameras.Push(active);
            Set(camera!=null&&camera.stereoEnabled&&XRSettings.stereoRenderingMode==XRSettings.StereoRenderingMode.SinglePassInstanced);
        }
        static void After(Camera camera){Set(cameras.Count==0?false:cameras.Pop());}
        static void Set(bool value)
        {
            if(value==active)return;active=value;
            foreach(var material in materials)if(material!=null){if(value)material.EnableKeyword(Keyword);else material.DisableKeyword(Keyword);}
        }
        internal static void Dispose()
        {
            if(installed){Camera.onPreCull-=Before;Camera.onPostRender-=After;installed=false;}
            Set(false);materials.Clear();cameras.Clear();
        }
    }
}

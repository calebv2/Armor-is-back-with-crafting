using System;
using UnityEngine;

namespace PlayerArmorMod
{
    // Owns only this local cue. The bundle's already-loaded material keeps its
    // shader alive even when Unity's global shader-name lookup cannot find it.
    internal sealed class ArmorConnectionGuide : IDisposable
    {
        internal const int PointCount=24;
        readonly Vector3[] positions=new Vector3[PointCount];
        readonly Action<string> log;
        LineRenderer renderer;
        Material material;
        float nextAttempt,nextLog;
        int failures;
        bool disposed;

        internal ArmorConnectionGuide(Action<string> logger){log=logger;}

        internal void Show(GameObject template,Vector3 center,Vector3 right,bool ready,float distance,float now)
        {
            if(disposed)return;
            if(distance>=.65f){Hide();return;}
            for(int i=0;i<PointCount;i++)
            {
                float angle=i*Mathf.PI*2/PointCount;
                positions[i]=center+.06f*(right*Mathf.Cos(angle)+Vector3.up*Mathf.Sin(angle));
            }
            if(renderer==null&&!Create(template,now))return;
            try
            {
                // Repair a stale/partial count before every bulk write. Nothing
                // can publish a zero-position renderer from failed creation.
                renderer.positionCount=PointCount;
                renderer.SetPositions(positions);
                Color color=ready?new Color(.35f,.9f,.50f,1):new Color(.9f,.65f,.30f,1);
                renderer.startColor=renderer.endColor=color;
                renderer.enabled=true;
            }
            catch(Exception error){Release();Failed(error,now);}
        }

        bool Create(GameObject template,float now)
        {
            if(now<nextAttempt)return false;
            GameObject created=null;Material owned=null;
            try
            {
                Material source=FindSource(template);
                if(source==null)throw new InvalidOperationException("Armor template has no loaded guide-compatible material");
                owned=new Material(source){name="PlayerArmor connection guide (owned)",hideFlags=HideFlags.DontSave};
                owned.SetColor("_Color",Color.white);
                owned.SetFloat("_Glow",.5f);owned.SetFloat("_UseAtlas",0);owned.SetFloat("_UseVertexColor",1);
                owned.SetFloat("_Cutoff",0);
                owned.SetTexture("_MainTex",Texture2D.whiteTexture);
                owned.mainTextureScale=Vector2.one;owned.mainTextureOffset=Vector2.zero;
                created=new GameObject("PlayerArmor local connection guide - no collider");created.SetActive(false);
                var line=created.AddComponent<LineRenderer>();
                line.enabled=false;line.sharedMaterial=owned;
                line.useWorldSpace=true;line.loop=true;line.widthMultiplier=.006f;
                line.generateLightingData=true;line.positionCount=PointCount;line.SetPositions(positions);
                ArmorStereo.Register(owned);
                created.SetActive(true);
                // Publish together only after all native initialization succeeds.
                renderer=line;material=owned;failures=0;nextAttempt=0;
                return true;
            }
            catch(Exception error)
            {
                if(created!=null)UnityEngine.Object.Destroy(created);
                if(owned!=null)UnityEngine.Object.Destroy(owned);
                Failed(error,now);return false;
            }
        }

        static Material FindSource(GameObject template)
        {
            if(template==null)return null;
            foreach(var sourceRenderer in template.GetComponentsInChildren<Renderer>(true))
                foreach(var source in sourceRenderer.sharedMaterials)
                    if(source!=null&&source.shader!=null&&source.HasProperty("_Glow")
                        &&source.HasProperty("_UseAtlas")&&source.HasProperty("_UseVertexColor")
                        &&source.HasProperty("_Color")&&source.HasProperty("_MainTex")&&source.HasProperty("_Cutoff"))
                        return source;
            return null;
        }

        void Failed(Exception error,float now)
        {
            failures=Math.Min(failures+1,5);nextAttempt=now+Math.Min(10f,Mathf.Pow(2,failures-1));
            if(log==null||now<nextLog)return;
            nextLog=now+30;
            try{log("PLAYER_ARMOR_GUIDE_DEFERRED "+error.GetBaseException().Message+"; retrying without interrupting equipment");}catch(Exception){}
        }
        internal void Hide(){if(renderer!=null)renderer.enabled=false;}
        void Release()
        {
            if(renderer!=null){renderer.enabled=false;UnityEngine.Object.Destroy(renderer.gameObject);renderer=null;}
            if(material!=null){UnityEngine.Object.Destroy(material);material=null;}
        }
        public void Dispose(){if(disposed)return;disposed=true;Release();}
    }
}

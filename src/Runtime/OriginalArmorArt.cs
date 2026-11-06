using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    // Compatibility port of CharacterPart.SetupRenderers / PlayerMeshRenderer's
    // bone-name mapping. No old gameplay scripts accompany the original art.
    public static class OriginalArmorArt
    {
        public const string BonePrefix="ATTBone.";
        public sealed class Frame
        {
            public Transform Visual,Target;
            internal bool Attached;
            public void SetVisible(bool shown){if(Attached&&Visual!=null&&Visual.gameObject.activeSelf!=shown)Visual.gameObject.SetActive(shown);}
            public void Dispose()
            {
                if(!Attached||Visual==null)return;
                var owned=Visual.gameObject;owned.SetActive(false);Visual.SetParent(null,false);
                if(Application.isPlaying)UnityEngine.Object.Destroy(owned);else UnityEngine.Object.DestroyImmediate(owned);
                Visual=null;Target=null;
            }
            public void Update()
            {
                if(Visual==null||Target==null||Visual.parent==null)return;
                if(Attached)return;
                Visual.SetPositionAndRotation(Target.position,Target.rotation);
                Vector3 a=Target.lossyScale,b=Visual.parent.lossyScale;
                Visual.localScale=new Vector3(Ratio(a.x,b.x),Ratio(a.y,b.y),Ratio(a.z,b.z));
            }
            static float Ratio(float a,float b){return Mathf.Abs(b)>.00001f?a/b:1f;}
        }
        public static bool IsSourceBone(Transform t)
        {for(var p=t;p!=null;p=p.parent)if(p.name=="OriginalSourceBones")return true;return false;}
        public static List<Frame> Bind(GameObject visual,Transform nativeRoot,bool nativeAttachments=false)
        {
            var result=new List<Frame>();var bones=new Dictionary<string,Transform>(StringComparer.Ordinal);
            foreach(var bone in nativeRoot.GetComponentsInChildren<Transform>(true))
                if(!IsSourceBone(bone)&&!bone.IsChildOf(visual.transform)&&!bones.ContainsKey(bone.name))bones.Add(bone.name,bone);
            foreach(var frame in visual.GetComponentsInChildren<Transform>(true))
            {
                if(!frame.name.StartsWith(BonePrefix,StringComparison.Ordinal))continue;
                string name=frame.name.Substring(BonePrefix.Length);Transform target;
                if(!bones.TryGetValue(name,out target))throw new InvalidOperationException("Original armor bone missing: "+name);
                var link=new Frame{Visual=frame,Target=target};link.Update();result.Add(link);
            }
            foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var source=skin.bones;var mapped=new Transform[source.Length];var weighted=new HashSet<int>();
                foreach(var w in skin.sharedMesh.boneWeights)
                {if(w.weight0>0)weighted.Add(w.boneIndex0);if(w.weight1>0)weighted.Add(w.boneIndex1);if(w.weight2>0)weighted.Add(w.boneIndex2);if(w.weight3>0)weighted.Add(w.boneIndex3);}
                for(int i=0;i<source.Length;i++)
                {
                    Transform target;
                    if(source[i]!=null&&bones.TryGetValue(source[i].name,out target))mapped[i]=target;
                    else if(!weighted.Contains(i))mapped[i]=nativeRoot;
                    else throw new InvalidOperationException("Original armor weighted bone missing: "+(source[i]==null?"null":source[i].name));
                }
                skin.bones=mapped;skin.rootBone=nativeRoot;skin.updateWhenOffscreen=true;
            }
            // Native CharacterPart moves rigid renderers to their owning joints.
            // In live play inherit the final native hand/IK pose directly rather
            // than sampling it once in a mod LateUpdate before all hand updates.
            // Export/bake callers keep their self-contained source hierarchy.
            if(nativeAttachments)try
            {
                foreach(var link in result)
                {
                    link.Visual.SetParent(link.Target,false);link.Attached=true;
                    link.Visual.localPosition=Vector3.zero;link.Visual.localRotation=Quaternion.identity;link.Visual.localScale=Vector3.one;
                }
            }
            catch{foreach(var link in result)link.Dispose();throw;}
            // Once bound, original-pose placeholders must not pollute later
            // native IK/customization searches with duplicate joint names.
            foreach(var t in visual.GetComponentsInChildren<Transform>(true))
                if(t!=null&&t.name=="OriginalSourceBones")
                {if(Application.isPlaying)UnityEngine.Object.Destroy(t.gameObject);else UnityEngine.Object.DestroyImmediate(t.gameObject);}
            return result;
        }
    }
}

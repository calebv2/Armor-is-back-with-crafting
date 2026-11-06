using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Crafting;
using Alta.Networking;
using UnityEngine;

namespace PlayerArmorMod
{
    // Client-local presentation of an owned native armor project. Project data,
    // pickup acceptance, native levels, colliders and save/network formats stay native.
    internal static class ArmorCraftingJig
    {
        sealed class Entry:IDisposable
        {
            internal IProject Project;
            internal ArmorJigPlan Plan;
            internal Vector3[] RootSeats;
            internal ArmorCraftingJigVisuals.View View;
            internal Material[] StereoMaterials;
            public void Dispose(){if(StereoMaterials!=null)foreach(var material in StereoMaterials)ArmorStereo.Unregister(material);StereoMaterials=null;if(View!=null)View.Dispose();View=null;Project=null;Plan=null;RootSeats=null;}
        }
        static readonly FieldInfo projectField=ArmorNative.Field(typeof(GenericCraftingStation),"currentProject");
        static readonly FieldInfo nailMeshField=ArmorNative.Field(typeof(GenericCraftingNail),"meshTransform");
        static readonly FieldInfo renderDistance=ArmorNative.Field(typeof(GenericCraftingStation),"maxDistanceToRenderProjectSquared");
        static readonly FieldInfo ghostDistance=ArmorNative.Field(typeof(GenericCraftingStation),"maxDistanceToRenderProjectGhostSquared");
        static readonly Dictionary<GenericCraftingStation,Entry> entries=new Dictionary<GenericCraftingStation,Entry>();
        static readonly List<GenericCraftingStation> remove=new List<GenericCraftingStation>();
        static readonly HashSet<string> warnings=new HashSet<string>(StringComparer.Ordinal);
        static GameObject template;static Material metal;static float nextScan;

        internal static void Configure(GameObject visualTemplate)
        {
            if(object.ReferenceEquals(template,visualTemplate))return;Dispose();template=visualTemplate;if(template==null)return;
            var materials=template.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();

            metal=materials.FirstOrDefault(m=>m.mainTexture!=null&&m.mainTexture.name.IndexOf("IronPanels",StringComparison.OrdinalIgnoreCase)>=0);

            if(metal==null)Warn("Armor joint cue material is unavailable; keeping the ordinary native project view");
        }
        static void Warn(string message)
        {if(warnings.Count<32&&warnings.Add(message))Core.Log("PLAYER_ARMOR_JIG_PRESENTATION "+message);}
        static Matrix4x4 Pose(GenericCraftingPieceDefinition definition)
        {return Matrix4x4.TRS(definition.position.position,definition.position.rotation,definition.position.scale);}
        static Bounds AssemblyBounds(GenericCraftingRecipe recipe)
        {
            bool first=true;var bounds=new Bounds();
            foreach(var definition in recipe.Pieces)
            {
                if(definition==null||definition.mesh==null)continue;var matrix=Pose(definition);
                foreach(var point in definition.mesh.vertices){var p=matrix.MultiplyPoint3x4(point);if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}
            }
            if(first)throw new InvalidOperationException("Armor jig project has no assembly mesh");return bounds;
        }
        static Vector3[] RootSupports(GenericCraftingRecipe recipe,ArmorJigPlan plan)
        {
            var contacts=new List<Vector3>();
            // Independent primary parts each need their own actual mesh seat.
            // In particular, native metal hips have two separate wings rather
            // than a cross-body belt that could support the second wing.
            for(int i=0;i<recipe.Pieces.Length;i++)
            {
                if(plan.Parents[i]>=0||plan.Levels[i]<0)continue;var definition=recipe.Pieces[i];
                if(definition==null||definition.mesh==null)continue;var matrix=Pose(definition);var vertices=definition.mesh.vertices;
                float bottom=float.PositiveInfinity;for(int p=0;p<vertices.Length;p++){vertices[p]=matrix.MultiplyPoint3x4(vertices[p]);bottom=Mathf.Min(bottom,vertices[p].y);}
                bool found=false;Vector3 first=Vector3.zero;
                foreach(var p in vertices)if(p.y<=bottom+.004f&&(!found||p.x<first.x)){first=p;found=true;}
                if(!found)continue;contacts.Add(first);Vector3 second=first;float spread=0;
                foreach(var p in vertices)if(p.y<=bottom+.004f){var delta=p-first;float distance=delta.x*delta.x+delta.z*delta.z;if(distance>spread){spread=distance;second=p;}}
                if(spread>.0036f)contacts.Add(second);
            }
            return contacts.ToArray();
        }
        static Vector3 RootSeat(GenericCraftingPieceDefinition definition)
        {
            var matrix=Pose(definition);var points=definition.mesh.vertices;var center=matrix.MultiplyPoint3x4(definition.mesh.bounds.center);float bottom=float.PositiveInfinity;
            for(int i=0;i<points.Length;i++){points[i]=matrix.MultiplyPoint3x4(points[i]);bottom=Mathf.Min(bottom,points[i].y);}
            var result=center;float nearest=float.PositiveInfinity;
            foreach(var p in points)if(p.y<=bottom+.004f){var delta=p-center;float distance=delta.x*delta.x+delta.z*delta.z;if(distance<nearest){nearest=distance;result=p;}}
            return result;
        }
        static Entry Create(GenericCraftingStation station,IProject project)
        {
            var plan=ArmorCrafting.JigPlanFor(project.Recipe);if(plan==null||plan.Levels==null)return null;int first=Array.IndexOf(plan.Levels,0);
            if(first<0||first>=project.Recipe.Pieces.Length)return null;var definition=project.Recipe.Pieces[first];
            var seats=new Vector3[project.Recipe.Pieces.Length];for(int i=0;i<seats.Length;i++)if(plan.Parents[i]<0)seats[i]=RootSeat(project.Recipe.Pieces[i]);
            var entry=new Entry{Project=project,Plan=plan,RootSeats=seats,View=ArmorCraftingJigVisuals.Create(station.PiecesParent,metal)};
            entry.StereoMaterials=entry.View.Root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.shader!=null&&m.shader.name=="PlayerArmor/Lit").Distinct().ToArray();
            foreach(var material in entry.StereoMaterials)ArmorStereo.Register(material);
            return entry;
        }
        static void Present(GenericCraftingStation station,Entry entry,float now,Vector3 head)
        {
            var parent=station.PiecesParent;if(parent==null)return;
            float distance=(head-parent.position).sqrMagnitude,mainLimit=(float)renderDistance.GetValue(station),ghostLimit=(float)ghostDistance.GetValue(station);
            entry.View.SetVisible(distance<=mainLimit);entry.View.Tick(now);
            int selected=-1;var definitions=entry.Project.Recipe.Pieces;
            for(int i=0;i<definitions.Length;i++)
            {
                GenericCraftingPiece piece;if(!entry.Project.MappedPieces.TryGetValue(definitions[i].hash,out piece)||piece==null||!piece.IsEnabled||piece.IsComplete)continue;
                if(selected<0||entry.Plan.Levels[i]<entry.Plan.Levels[selected])selected=i;
            }
            if(selected<0||distance>ghostLimit){entry.View.SetGuidePoint(Vector3.zero,Vector3.up,false);return;}
            var current=entry.Project.MappedPieces[definitions[selected].hash];Vector3 point,normal;
            if(!current.IsFilled)
            {
                bool root=entry.Plan.Parents[selected]<0;point=root?entry.RootSeats[selected]:entry.Plan.JointPoints[selected];normal=root?Vector3.up:-(entry.Plan.NailRotations[selected]*Vector3.forward);
                entry.View.SetGuidePoint(point,normal,true);return;
            }
            // Once filled, point only at a real unfinished native nail; the
            // normal follows its current deviance and the cue follows its head.
            if(current.Nails!=null)foreach(var nail in current.Nails)
            {
                if(nail==null||nail.IsDone)continue;var mesh=nailMeshField.GetValue(nail) as Transform;if(mesh==null)continue;
                point=parent.InverseTransformPoint(mesh.position);normal=parent.InverseTransformDirection(-nail.DeviantDirection);entry.View.SetGuidePoint(point,normal,true);return;
            }
            entry.View.SetGuidePoint(Vector3.zero,Vector3.up,false);
        }
        internal static void Update()
        {
            if(template==null||metal==null||NetworkSceneManager.Current==null){ClearViews();return;}
            float now=Time.unscaledTime;
            var controller=PlayerController.Current;if(controller==null||controller.Head==null){ClearViews();return;}var head=controller.Head.position;
            // Discovery is slow and bounded. Existing nearby views follow their
            // native project state and animate smoothly on every client frame.
            if(now>=nextScan)
            {
                nextScan=now+1f;
                foreach(var station in UnityEngine.Object.FindObjectsOfType<GenericCraftingStation>())
                {
                    try
                    {
                        if(entries.ContainsKey(station))continue;var project=projectField.GetValue(station) as IProject;
                        if(project==null||project.IsComplete||!ArmorCrafting.OwnsJigRecipe(project.Recipe)||station.PiecesParent==null)continue;
                        float limit=(float)renderDistance.GetValue(station);if((head-station.PiecesParent.position).sqrMagnitude>limit)continue;
                        var entry=Create(station,project);if(entry!=null)entries.Add(station,entry);
                    }
                    catch(Exception error){Warn(error.GetBaseException().Message);}
                }
            }
            remove.Clear();
            foreach(var pair in entries)
            {
                var station=pair.Key;var entry=pair.Value;
                try
                {
                    var project=station==null?null:projectField.GetValue(station) as IProject;
                    if(project==null||project.IsComplete||!object.ReferenceEquals(project,entry.Project)||!ArmorCrafting.OwnsJigRecipe(project.Recipe)||station.PiecesParent==null)
                    {entry.Dispose();remove.Add(station);continue;}
                    if((head-station.PiecesParent.position).sqrMagnitude>(float)renderDistance.GetValue(station))
                    {entry.Dispose();remove.Add(station);continue;}
                    Present(station,entry,now,head);
                }
                catch(Exception error){Warn(error.GetBaseException().Message);entry.Dispose();remove.Add(station);}
            }
            foreach(var station in remove)entries.Remove(station);remove.Clear();
        }
        static void ClearViews(){foreach(var entry in entries.Values)entry.Dispose();entries.Clear();remove.Clear();nextScan=0;}
        internal static void Dispose()
        {
            ClearViews();warnings.Clear();template=null;metal=null;nextScan=0;
        }
    }
}

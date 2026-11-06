using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    // Indices always refer to the existing native definition array. Assembly
    // order is independent of ingredient/hash/material-channel identity.
    internal sealed class ArmorJigPlan
    {
        internal int[] Levels,Parents;
        internal bool[] RequiresNails;
        internal Vector3[] NailPositions,JointPoints,SupportPoints;
        internal Quaternion[] NailRotations;
        internal float[] JointGaps;
    }

    internal static partial class ArmorCraftingMesh
    {
        // This is a seam tolerance in the final, scaled workbench coordinates,
        // not a permission to connect separated parts through their bounds.
        internal const float JigMaximumJointGap=.015f;
        const int JigLeafSize=12,JigCandidateLimit=1536,JigMaximumEdgeChoices=192,JigSearchBudget=6000;
        sealed class JigTriangle
        {
            internal Vector3 A,B,C,Normal;
            internal Bounds Bounds;
            internal int Index;
        }
        sealed class JigTree
        {
            internal Bounds Bounds;
            internal JigTree Left,Right;
            internal JigTriangle[] Triangles;
        }
        sealed class JigSeam
        {
            internal Vector3 Point,Support,Normal;
            internal float Gap,Score;
            internal int Face,OtherFace;
        }
        sealed class JigEdge
        {
            internal int Child,Parent;
            internal List<JigSeam> Seams;
            internal float NearestGap;
            internal bool Expanded;
            internal Dictionary<string,List<JigChoice>> Choices=new Dictionary<string,List<JigChoice>>();
        }
        sealed class JigChoice
        {
            internal JigEdge Edge;
            internal JigSeam Seam;
            internal Vector3 Basis;
            internal Quaternion Rotation;
            internal int Clearance;
        }

        internal static ArmorJigPlan BuildJigPlan(Mesh[] visuals,Mesh[] collisions,bool[] primary,bool[] ordinaryNails,ArmorSlot slot,bool twoWingJig=false)
        {
            if(visuals==null||collisions==null||primary==null||ordinaryNails==null||visuals.Length==0
                ||visuals.Length!=collisions.Length||visuals.Length!=primary.Length||visuals.Length!=ordinaryNails.Length)
                throw new ArgumentException("Armor jig inputs must describe the same nonempty definition array");
            int count=visuals.Length;var trees=new JigTree[count];var hulls=new Vector4[count][];bool hasAnchor=false;
            for(int i=0;i<count;i++)
            {
                if(visuals[i]==null||collisions[i]==null)throw new ArgumentException("Armor jig has a missing visual/collision mesh at "+i);
                trees[i]=JigBuildTree(visuals[i]);hulls[i]=HullPlanes(collisions[i]);hasAnchor|=primary[i];
            }
            if(!hasAnchor)throw new InvalidOperationException("Armor jig has no primary piece to support");
            int primaryCount=0;foreach(bool value in primary)if(value)primaryCount++;
            if(twoWingJig&&(slot!=ArmorSlot.Hips||count!=5||primaryCount!=4))throw new ArgumentException("Two-wing jig is reserved for the four-panel Metal Hips layout");
            var edges=new JigEdge[count,count];
            // Populate lazily; failed root attempts reuse exactly the same
            // surface graph, rather than changing geometry or ingredient order.
            var diagnostics=new List<string>();
            if(twoWingJig)
            {
                // Both native wings must be seated before any joining begins.
                // A late second anchor would let an unfastened last panel end
                // the recipe, so this exception has parallel level-zero roots.
                for(int left=0;left<count;left++)if(primary[left]&&JigWing(left,primary)==0)
                for(int right=0;right<count;right++)if(primary[right]&&JigWing(right,primary)==1)
                {
                    var wingPlan=JigEmptyPlan(count);wingPlan.Levels[left]=wingPlan.Levels[right]=0;
                    var wingClearance=new int[count];for(int i=0;i<count;i++)wingClearance[i]=-2;
                    int wingBudget=JigSearchBudget;var wingStates=new HashSet<string>();
                    if(JigSearch(wingPlan,wingClearance,1,primary,ordinaryNails,true,edges,trees,hulls,wingStates,ref wingBudget))
                    {LastNailClearanceDegrees=wingClearance;return wingPlan;}
                    diagnostics.Add("parallelWingRoots="+left+","+right+" fallbackStates="+(JigSearchBudget-wingBudget)+(wingBudget<=0?"(budget exhausted)":""));
                }
                LastNailClearanceDegrees=new int[count];for(int i=0;i<count;i++)LastNailClearanceDegrees[i]=-1;
                throw new InvalidOperationException("Armor jig has no supported, hammer-accessible two-wing sequence for "+slot+"; "+string.Join("; ",diagnostics.ToArray())+"; "+JigEdgeDiagnostics(edges));
            }
            for(int anchor=0;anchor<count;anchor++)if(primary[anchor])
            {
                var plan=JigEmptyPlan(count);plan.Levels[anchor]=0;
                var clearance=new int[count];for(int i=0;i<count;i++)clearance[i]=-2;
                bool complete=true;string diagnostic=null;
                for(int level=1;level<count;level++)
                {
                    JigChoice best=null;
                    for(int child=0;child<count;child++)if(plan.Levels[child]<0)
                    for(int parent=0;parent<count;parent++)if(plan.Levels[parent]>=0)
                    {
                        var edge=edges[child,parent];
                        if(edge==null)edges[child,parent]=edge=JigBuildEdge(child,parent,trees[child],trees[parent],hulls);
                        var choice=JigChoose(edge,JigStageHulls(hulls,plan,child),ordinaryNails[child],plan);
                        if(choice!=null&&(best==null||JigCompareChoice(choice,best)<0))best=choice;
                    }
                    if(best==null)
                    {
                        complete=false;var pending=new List<string>();
                        for(int child=0;child<count;child++)if(plan.Levels[child]<0)
                        {
                            float gap=float.PositiveInfinity;int candidates=0;
                            for(int parent=0;parent<count;parent++)if(plan.Levels[parent]>=0)
                            {var edge=edges[child,parent];if(edge!=null){gap=Mathf.Min(gap,edge.NearestGap);candidates+=edge.Seams.Count;}}
                            pending.Add(child+"(gap="+(float.IsPositiveInfinity(gap)?"outside seam range":(gap*1000).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"mm")+",surfaceCandidates="+candidates+")");
                        }
                        diagnostic="anchor="+anchor+" level="+level+" pending="+string.Join(",",pending.ToArray());break;
                    }
                    int item=best.Edge.Child;plan.Levels[item]=level;plan.Parents[item]=best.Edge.Parent;
                    plan.RequiresNails[item]=ordinaryNails[item];plan.JointPoints[item]=best.Seam.Point;plan.SupportPoints[item]=best.Seam.Support;
                    plan.JointGaps[item]=best.Seam.Gap;plan.NailPositions[item]=best.Basis;plan.NailRotations[item]=best.Rotation;
                    clearance[item]=ordinaryNails[item]?best.Clearance:-2;
                }
                if(complete&&!twoWingJig){LastNailClearanceDegrees=clearance;return plan;}
                if(complete)diagnostic="anchor="+anchor+" needs its second explicit wing clamp";
                // Keep the normal 28-layout path inexpensive. Only a dead end
                // searches alternate seam witnesses, parents and part orders.
                // This is especially important for two straps sharing a small
                // pad: a valid early nail must not reserve the only later site.
                plan=JigEmptyPlan(count);plan.Levels[anchor]=0;
                for(int i=0;i<count;i++)clearance[i]=-2;
                int budget=JigSearchBudget;var failedStates=new HashSet<string>();
                if(JigSearch(plan,clearance,1,primary,ordinaryNails,twoWingJig,edges,trees,hulls,failedStates,ref budget))
                {LastNailClearanceDegrees=clearance;return plan;}
                diagnostics.Add(diagnostic+" fallbackStates="+(JigSearchBudget-budget)+(budget<=0?"(budget exhausted)":""));
            }
            LastNailClearanceDegrees=new int[count];for(int i=0;i<count;i++)LastNailClearanceDegrees[i]=-1;
            throw new InvalidOperationException("Armor jig has no supported, hammer-accessible seam sequence for "+slot+"; "+string.Join("; ",diagnostics.ToArray())
                +"; maximum real surface gap="+(JigMaximumJointGap*1000).ToString("F0",System.Globalization.CultureInfo.InvariantCulture)+"mm");
        }
        static bool JigSearch(ArmorJigPlan plan,int[] clearance,int level,bool[] primary,bool[] ordinaryNails,bool twoWingJig,JigEdge[,] edges,JigTree[] trees,Vector4[][] hulls,HashSet<string> failedStates,ref int budget)
        {
            if(JigPlacedCount(plan)==plan.Levels.Length)return !twoWingJig||JigRootCount(plan)==2;if(budget--<=0)return false;
            string state=JigState(plan);if(failedStates.Contains(state))return false;var options=new List<JigChoice>();
            for(int child=0;child<plan.Levels.Length;child++)if(plan.Levels[child]<0)
            for(int parent=0;parent<plan.Levels.Length;parent++)if(plan.Levels[parent]>=0)
            {
                if(twoWingJig)
                {
                    if(primary[child])
                    {
                        // Each wing's two metal panels need their own real
                        // hinge rivet. A disconnected closure island is not a
                        // substitute for joining that wing to its clamp root.
                        if(!primary[parent]||plan.Parents[parent]>=0||JigWing(child,primary)!=JigWing(parent,primary))continue;
                    }
                }
                var edge=edges[child,parent];
                if(edge==null||!edge.Expanded)edges[child,parent]=edge=JigBuildEdge(child,parent,trees[child],trees[parent],hulls,true);
                string mask=JigStageMask(plan,child);List<JigChoice> valid;
                if(!edge.Choices.TryGetValue(mask,out valid))edge.Choices.Add(mask,valid=JigBuildChoices(edge,JigStageHulls(hulls,plan,child),ordinaryNails[child]));
                foreach(var choice in valid)if(!ordinaryNails[child]||JigSeparate(choice.Basis,plan))options.Add(choice);
            }
            options.Sort(JigCompareChoice);
            foreach(var choice in options)
            {
                int item=choice.Edge.Child;plan.Levels[item]=level;plan.Parents[item]=choice.Edge.Parent;plan.RequiresNails[item]=ordinaryNails[item];
                plan.JointPoints[item]=choice.Seam.Point;plan.SupportPoints[item]=choice.Seam.Support;plan.JointGaps[item]=choice.Seam.Gap;
                plan.NailPositions[item]=choice.Basis;plan.NailRotations[item]=choice.Rotation;clearance[item]=plan.RequiresNails[item]?choice.Clearance:-2;
                if(JigSearch(plan,clearance,level+1,primary,ordinaryNails,twoWingJig,edges,trees,hulls,failedStates,ref budget))return true;
                plan.Levels[item]=-1;plan.Parents[item]=-1;plan.RequiresNails[item]=false;clearance[item]=-2;
                plan.JointPoints[item]=plan.SupportPoints[item]=plan.NailPositions[item]=Vector3.zero;plan.JointGaps[item]=0;plan.NailRotations[item]=Quaternion.identity;
                if(budget<=0)break;
            }
            failedStates.Add(state);return false;
        }
        static int JigRootCount(ArmorJigPlan plan)
        {int count=0;for(int i=0;i<plan.Levels.Length;i++)if(plan.Levels[i]>=0&&plan.Parents[i]<0)count++;return count;}
        static int JigPlacedCount(ArmorJigPlan plan)
        {int count=0;foreach(int level in plan.Levels)if(level>=0)count++;return count;}
        static string JigStageMask(ArmorJigPlan plan,int child)
        {var mask=new char[plan.Levels.Length];for(int i=0;i<mask.Length;i++)mask[i]=i==child||plan.Levels[i]>=0?'1':'0';return new string(mask);}
        static Vector4[][] JigStageHulls(Vector4[][] hulls,ArmorJigPlan plan,int child)
        {
            // Native Setup leaves future ghosts' colliders disabled; only a
            // filled current piece and completed earlier levels are solid.
            // Both parallel starting pieces are active before level one.
            var active=new List<Vector4[]>();for(int i=0;i<hulls.Length;i++)if(i==child||plan.Levels[i]>=0)active.Add(hulls[i]);return active.ToArray();
        }
        static string JigEdgeDiagnostics(JigEdge[,] edges)
        {
            var result=new List<string>();for(int child=0;child<edges.GetLength(0);child++)for(int parent=0;parent<edges.GetLength(1);parent++)
            {
                var edge=edges[child,parent];if(edge==null)continue;var masks=new List<string>(edge.Choices.Keys);masks.Sort(StringComparer.Ordinal);var counts=new List<string>();
                foreach(var mask in masks)counts.Add(mask+":"+edge.Choices[mask].Count);
                result.Add(child+">"+parent+" seams="+edge.Seams.Count+" validByActiveMask["+string.Join(",",counts.ToArray())+"]");
            }
            return "edgeOptions["+string.Join(";",result.ToArray())+"]";
        }
        static int JigWing(int index,bool[] primary)
        {if(!primary[index])return -1;int ordinal=0;for(int i=0;i<index;i++)if(primary[i])ordinal++;return ordinal<2?0:1;}
        static string JigState(ArmorJigPlan plan)
        {
            var values=new List<string>();for(int i=0;i<plan.Levels.Length;i++)
            {if(plan.Levels[i]<0){values.Add("-");continue;}var p=plan.NailPositions[i];values.Add(plan.RequiresNails[i]?Mathf.RoundToInt(p.x*1000000)+","+Mathf.RoundToInt(p.y*1000000)+","+Mathf.RoundToInt(p.z*1000000):"P");}
            return string.Join("/",values.ToArray());
        }
        static bool JigSeparate(Vector3 basis,ArmorJigPlan plan)
        {for(int i=0;i<plan.RequiresNails.Length;i++)if(plan.RequiresNails[i]&&(basis-plan.NailPositions[i]).sqrMagnitude<.000625f)return false;return true;}
        static List<JigChoice> JigBuildChoices(JigEdge edge,Vector4[][] hulls,bool requiresNail)
        {
            var choices=new List<JigChoice>();if(edge.Seams.Count==0)return choices;
            if(!requiresNail){var seam=edge.Seams[0];choices.Add(new JigChoice{Edge=edge,Seam=seam,Basis=seam.Point+seam.Normal*.025f,Rotation=Quaternion.LookRotation(-seam.Normal),Clearance=-2});return choices;}
            // Cache the geometric strike proof once per directed edge. Search
            // states only check spacing against their previously selected nails.
            var cells=new HashSet<string>();
            foreach(var seam in edge.Seams)foreach(float extra in new[]{0f,.005f,.010f})
            {
                var basis=seam.Point+seam.Normal*(.025f+extra);var rotation=Quaternion.LookRotation(-seam.Normal);int clearance=-1;
                foreach(int angle in new[]{45,20,0})if(Fits(hulls,basis,rotation,angle)){clearance=angle;break;}
                if(clearance<0)continue;string cell=Mathf.RoundToInt(basis.x*200)+":"+Mathf.RoundToInt(basis.y*200)+":"+Mathf.RoundToInt(basis.z*200)+":"
                    +Mathf.RoundToInt(seam.Normal.x*10)+":"+Mathf.RoundToInt(seam.Normal.y*10)+":"+Mathf.RoundToInt(seam.Normal.z*10);
                if(cells.Add(cell))choices.Add(new JigChoice{Edge=edge,Seam=seam,Basis=basis,Rotation=rotation,Clearance=clearance});
            }
            choices.Sort(JigCompareChoice);
            if(choices.Count>JigMaximumEdgeChoices)
            {
                // Keep spatially spread choices before score-only leftovers,
                // so a densely tessellated corner cannot exhaust the budget.
                var spread=new List<JigChoice>();var used=new HashSet<JigChoice>();var coarse=new HashSet<string>();
                foreach(var choice in choices){var p=choice.Basis;string key=Mathf.RoundToInt(p.x*40)+":"+Mathf.RoundToInt(p.y*40)+":"+Mathf.RoundToInt(p.z*40);if(coarse.Add(key)){spread.Add(choice);used.Add(choice);if(spread.Count==JigMaximumEdgeChoices)break;}}
                foreach(var choice in choices)if(spread.Count<JigMaximumEdgeChoices&&used.Add(choice))spread.Add(choice);
                spread.Sort(JigCompareChoice);choices=spread;
            }
            return choices;
        }
        static ArmorJigPlan JigEmptyPlan(int count)
        {
            var result=new ArmorJigPlan{Levels=new int[count],Parents=new int[count],RequiresNails=new bool[count],NailPositions=new Vector3[count],
                JointPoints=new Vector3[count],SupportPoints=new Vector3[count],NailRotations=new Quaternion[count],JointGaps=new float[count]};
            for(int i=0;i<count;i++){result.Levels[i]=-1;result.Parents[i]=-1;result.NailRotations[i]=Quaternion.identity;}return result;
        }
        static JigTree JigBuildTree(Mesh mesh)
        {
            return JigBuildTree(mesh.vertices,mesh.triangles,mesh.name);
        }
        static JigTree JigBuildTree(Vector3[] points,int[] indices,string name)
        {
            var triangles=new List<JigTriangle>();
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=points[indices[i]];var b=points[indices[i+1]];var c=points[indices[i+2]];var cross=Vector3.Cross(b-a,c-a);
                if(cross.sqrMagnitude<1e-18f)continue;var bounds=new Bounds(a,Vector3.zero);bounds.Encapsulate(b);bounds.Encapsulate(c);
                triangles.Add(new JigTriangle{A=a,B=b,C=c,Normal=cross.normalized,Bounds=bounds,Index=i/3});
            }
            if(triangles.Count==0)throw new InvalidOperationException("Armor visual has no nondegenerate triangles: "+name);
            return JigBuildTree(triangles.ToArray());
        }
        static JigTree JigBuildTree(JigTriangle[] triangles)
        {
            var bounds=triangles[0].Bounds;for(int i=1;i<triangles.Length;i++)bounds.Encapsulate(triangles[i].Bounds);
            var tree=new JigTree{Bounds=bounds};if(triangles.Length<=JigLeafSize){tree.Triangles=triangles;return tree;}
            int axis=bounds.size.x>=bounds.size.y&&bounds.size.x>=bounds.size.z?0:bounds.size.y>=bounds.size.z?1:2;
            Array.Sort(triangles,(a,b)=>{int order=a.Bounds.center[axis].CompareTo(b.Bounds.center[axis]);return order!=0?order:a.Index.CompareTo(b.Index);});
            int middle=triangles.Length/2;var left=new JigTriangle[middle];var right=new JigTriangle[triangles.Length-middle];
            Array.Copy(triangles,0,left,0,left.Length);Array.Copy(triangles,middle,right,0,right.Length);
            tree.Left=JigBuildTree(left);tree.Right=JigBuildTree(right);return tree;
        }
        static float JigBoundsGapSquared(Bounds a,Bounds b)
        {
            var amin=a.min;var amax=a.max;var bmin=b.min;var bmax=b.max;float distance=0;
            for(int axis=0;axis<3;axis++){float gap=Mathf.Max(0,Mathf.Max(amin[axis]-bmax[axis],bmin[axis]-amax[axis]));distance+=gap*gap;}return distance;
        }
        static JigEdge JigBuildEdge(int child,int parent,JigTree a,JigTree b,Vector4[][] hulls,bool expanded=false)
        {
            var edge=new JigEdge{Child=child,Parent=parent,Seams=new List<JigSeam>(),NearestGap=float.PositiveInfinity,Expanded=expanded};
            var seen=new Dictionary<string,JigSeam>();JigFindSeams(a,b,edge,seen,hulls,expanded);
            edge.Seams.AddRange(seen.Values);edge.Seams.Sort(JigCompareSeam);
            if(edge.Seams.Count>JigCandidateLimit)edge.Seams.RemoveRange(JigCandidateLimit,edge.Seams.Count-JigCandidateLimit);return edge;
        }
        static void JigFindSeams(JigTree a,JigTree b,JigEdge edge,Dictionary<string,JigSeam> seen,Vector4[][] hulls,bool expanded)
        {
            if(JigBoundsGapSquared(a.Bounds,b.Bounds)>JigMaximumJointGap*JigMaximumJointGap)return;
            if(a.Triangles==null){JigFindSeams(a.Left,b,edge,seen,hulls,expanded);JigFindSeams(a.Right,b,edge,seen,hulls,expanded);return;}
            if(b.Triangles==null){JigFindSeams(a,b.Left,edge,seen,hulls,expanded);JigFindSeams(a,b.Right,edge,seen,hulls,expanded);return;}
            foreach(var face in a.Triangles)foreach(var other in b.Triangles)
            {
                if(JigBoundsGapSquared(face.Bounds,other.Bounds)>JigMaximumJointGap*JigMaximumJointGap)continue;
                Vector3 point,support;JigClosestTriangles(face,other,out point,out support);JigAddSeam(point,support,face,other,edge,seen,hulls);
                if(expanded)
                {
                    foreach(var sample in JigSamples(face))JigAddSeam(sample,JigClosestPoint(sample,other),face,other,edge,seen,hulls);
                    foreach(var sample in JigSamples(other)){var contact=JigClosestPoint(sample,face);JigAddSeam(contact,JigClosestPoint(contact,other),face,other,edge,seen,hulls);}
                }
            }
        }
        static Vector3[] JigSamples(JigTriangle face)
        {return new[]{face.A,face.B,face.C,(face.A+face.B)*.5f,(face.B+face.C)*.5f,(face.C+face.A)*.5f,(face.A+face.B+face.C)/3};}
        static void JigAddSeam(Vector3 point,Vector3 support,JigTriangle face,JigTriangle other,JigEdge edge,Dictionary<string,JigSeam> seen,Vector4[][] hulls)
        {
                float gap=(point-support).magnitude;edge.NearestGap=Mathf.Min(edge.NearestGap,gap);if(gap>JigMaximumJointGap)return;
                var normal=face.Normal;if(normal.y<-.3f)return;
                // A nearby tangential face is not a backing plate. Prove that
                // the rivet's inward shaft reaches the actual parent triangle;
                // only a real <=2mm touching seam can substitute for that ray.
                Vector3 shaftSupport;
                if(JigSegmentTriangle(point+normal*.0005f,point-normal*JigMaximumJointGap,other,out shaftSupport))
                {support=shaftSupport;gap=(point-support).magnitude;}
                else if(gap>.002f)return;
                if(Vector3.Dot(support-point,normal)>.0005f)return;
                var basis=point+normal*.025f;float exterior=float.PositiveInfinity;
                foreach(var hull in hulls)exterior=Mathf.Min(exterior,Outside(hull,basis+normal*.0184f));
                var candidate=new JigSeam{Point=point,Support=support,Normal=normal,Gap=gap,Face=face.Index,OtherFace=other.Index,
                    Score=Mathf.Clamp(exterior,-.25f,.08f)*8+normal.y*.08f+point.y*.03f-gap*2};
                // Quantized keys only bound the candidate set. Distances and
                // final support points retain their actual surface precision.
                string key=Mathf.RoundToInt(point.x*500)+":"+Mathf.RoundToInt(point.y*500)+":"+Mathf.RoundToInt(point.z*500)+":"
                    +Mathf.RoundToInt(normal.x*20)+":"+Mathf.RoundToInt(normal.y*20)+":"+Mathf.RoundToInt(normal.z*20);
                JigSeam previous;if(!seen.TryGetValue(key,out previous)||JigCompareSeam(candidate,previous)<0)seen[key]=candidate;
        }
        static int JigCompareSeam(JigSeam a,JigSeam b)
        {
            int order=Mathf.RoundToInt(b.Score*100000).CompareTo(Mathf.RoundToInt(a.Score*100000));if(order!=0)return order;
            order=Mathf.RoundToInt(a.Gap*1000000).CompareTo(Mathf.RoundToInt(b.Gap*1000000));if(order!=0)return order;
            for(int axis=0;axis<3;axis++){order=Mathf.RoundToInt(a.Point[axis]*1000000).CompareTo(Mathf.RoundToInt(b.Point[axis]*1000000));if(order!=0)return order;}
            for(int axis=0;axis<3;axis++){order=Mathf.RoundToInt(a.Normal[axis]*1000000).CompareTo(Mathf.RoundToInt(b.Normal[axis]*1000000));if(order!=0)return order;}
            order=a.Face.CompareTo(b.Face);return order!=0?order:a.OtherFace.CompareTo(b.OtherFace);
        }
        static JigChoice JigChoose(JigEdge edge,Vector4[][] hulls,bool requiresNail,ArmorJigPlan plan)
        {
            if(edge.Seams.Count==0)return null;
            if(!requiresNail){var seam=edge.Seams[0];return new JigChoice{Edge=edge,Seam=seam,Basis=seam.Point+seam.Normal*.025f,Rotation=Quaternion.LookRotation(-seam.Normal),Clearance=-2};}
            foreach(int angle in new[]{45,20,0})foreach(float extra in new[]{0f,.005f,.010f})foreach(var seam in edge.Seams)
            {
                var basis=seam.Point+seam.Normal*(.025f+extra);bool separate=true;
                for(int i=0;i<plan.RequiresNails.Length;i++)if(plan.RequiresNails[i]&&(basis-plan.NailPositions[i]).sqrMagnitude<.000625f){separate=false;break;}
                if(!separate)continue;var rotation=Quaternion.LookRotation(-seam.Normal);
                if(Fits(hulls,basis,rotation,angle))return new JigChoice{Edge=edge,Seam=seam,Basis=basis,Rotation=rotation,Clearance=angle};
            }
            return null;
        }
        static int JigCompareChoice(JigChoice a,JigChoice b)
        {
            // A wider safe strike wins; no-nail bindings remain eligible early
            // instead of being forced behind every plate by ingredient order.
            int ac=a.Clearance<0?45:a.Clearance,bc=b.Clearance<0?45:b.Clearance;
            int order=bc.CompareTo(ac);if(order!=0)return order;order=JigCompareSeam(a.Seam,b.Seam);if(order!=0)return order;
            order=a.Edge.Child.CompareTo(b.Edge.Child);if(order!=0)return order;order=a.Edge.Parent.CompareTo(b.Edge.Parent);if(order!=0)return order;
            for(int axis=0;axis<3;axis++){order=Mathf.RoundToInt(a.Basis[axis]*1000000).CompareTo(Mathf.RoundToInt(b.Basis[axis]*1000000));if(order!=0)return order;}return 0;
        }

        // Exact triangle features, rather than nearest bounds/vertices. This
        // catches long plate overlaps even when their vertices are far apart.
        static void JigClosestTriangles(JigTriangle a,JigTriangle b,out Vector3 point,out Vector3 support)
        {
            var av=new[]{a.A,a.B,a.C};var bv=new[]{b.A,b.B,b.C};point=a.A;support=b.A;float best=float.PositiveInfinity;
            for(int i=0;i<3;i++)
            {
                Vector3 intersection;
                if(JigSegmentTriangle(av[i],av[(i+1)%3],b,out intersection)||JigSegmentTriangle(bv[i],bv[(i+1)%3],a,out intersection))
                {point=intersection;support=intersection;return;}
                JigKeepPair(av[i],JigClosestPoint(av[i],b),ref best,ref point,ref support);
                JigKeepPair(JigClosestPoint(bv[i],a),bv[i],ref best,ref point,ref support);
                for(int j=0;j<3;j++){Vector3 p,q;JigClosestSegments(av[i],av[(i+1)%3],bv[j],bv[(j+1)%3],out p,out q);JigKeepPair(p,q,ref best,ref point,ref support);}
            }
        }
        static void JigKeepPair(Vector3 a,Vector3 b,ref float best,ref Vector3 point,ref Vector3 support)
        {float distance=(a-b).sqrMagnitude;if(distance<best){best=distance;point=a;support=b;}}
        static bool JigSegmentTriangle(Vector3 start,Vector3 end,JigTriangle triangle,out Vector3 point)
        {
            point=Vector3.zero;var direction=end-start;var e1=triangle.B-triangle.A;var e2=triangle.C-triangle.A;var cross=Vector3.Cross(direction,e2);float determinant=Vector3.Dot(e1,cross);
            if(Mathf.Abs(determinant)<1e-12f)return false;float inverse=1/determinant;var relative=start-triangle.A;float u=Vector3.Dot(relative,cross)*inverse;if(u<0||u>1)return false;
            var q=Vector3.Cross(relative,e1);float v=Vector3.Dot(direction,q)*inverse;if(v<0||u+v>1)return false;float t=Vector3.Dot(e2,q)*inverse;if(t<0||t>1)return false;point=start+direction*t;return true;
        }
        static Vector3 JigClosestPoint(Vector3 point,JigTriangle triangle)
        { return ClosestTrianglePoint(point,triangle.A,triangle.B,triangle.C); }
        internal static Vector3 ClosestTrianglePoint(Vector3 point,Vector3 a,Vector3 b,Vector3 c)
        {
            var ab=b-a;var ac=c-a;var ap=point-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);if(d1<=0&&d2<=0)return a;
            var bp=point-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
            float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
            var cp=point-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
            float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/(d4-d3+d5-d6));
            float denominator=1/(va+vb+vc);return a+ab*(vb*denominator)+ac*(vc*denominator);
        }
        static void JigClosestSegments(Vector3 a,Vector3 b,Vector3 c,Vector3 d,out Vector3 p,out Vector3 q)
        {
            var first=b-a;var second=d-c;var relative=a-c;float aa=Vector3.Dot(first,first),ee=Vector3.Dot(second,second),ff=Vector3.Dot(second,relative),s,t;
            if(aa<=1e-16f&&ee<=1e-16f){p=a;q=c;return;}
            if(aa<=1e-16f){s=0;t=Mathf.Clamp01(ff/ee);}
            else{float cc=Vector3.Dot(first,relative);if(ee<=1e-16f){t=0;s=Mathf.Clamp01(-cc/aa);}else{float bb=Vector3.Dot(first,second),denominator=aa*ee-bb*bb;s=denominator!=0?Mathf.Clamp01((bb*ff-cc*ee)/denominator):0;t=(bb*s+ff)/ee;if(t<0){t=0;s=Mathf.Clamp01(-cc/aa);}else if(t>1){t=1;s=Mathf.Clamp01((bb-cc)/aa);}}}
            p=a+first*s;q=c+second*t;
        }
    }
}

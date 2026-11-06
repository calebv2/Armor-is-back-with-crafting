using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static partial class ArmorCraftingMesh
    {
        // Geometry-only authoring rebakes must retain the released native piece
        // keys, starting seat, stage order and fixed nail arrays. Only real seam
        // witnesses and their accessible strike poses may be selected again.
        internal static ArmorJigPlan BuildJigPlanWithTopology(Mesh[] visuals,Mesh[] collisions,bool[] primary,bool[] ordinaryNails,
            ArmorSlot slot,int[] oldLevels,int[] oldParents,bool[] oldRequiresNails)
        {
            if(visuals==null||collisions==null||primary==null||ordinaryNails==null||visuals.Length==0
                ||visuals.Length!=collisions.Length||visuals.Length!=primary.Length||visuals.Length!=ordinaryNails.Length)
                throw new ArgumentException("Frozen armor jig inputs must describe the same nonempty definition array");
            int count=visuals.Length;
            if(oldLevels==null||oldParents==null||oldRequiresNails==null||oldLevels.Length!=count||oldParents.Length!=count||oldRequiresNails.Length!=count)
                throw new ArgumentException("Frozen armor jig topology must retain every native definition index");
            var levels=(int[])oldLevels.Clone();var parents=(int[])oldParents.Clone();var requires=(bool[])oldRequiresNails.Clone();
            int roots=0;
            for(int i=0;i<count;i++)
            {
                if(visuals[i]==null||collisions[i]==null)throw new ArgumentException("Frozen armor jig has a missing visual/collision mesh at "+i);
                if(parents[i]<-1||parents[i]>=count||parents[i]==i)throw new ArgumentException("Frozen armor jig has an invalid parent at "+i);
                if(parents[i]<0)
                {
                    roots++;
                    if(!primary[i]||levels[i]!=0||requires[i])throw new ArgumentException("Frozen armor jig root "+i+" must remain a zero-nail primary seat at level0");
                }
                else if(levels[i]<=0||levels[parents[i]]>=levels[i]||requires[i]!=ordinaryNails[i])
                    throw new ArgumentException("Frozen armor jig part "+i+" must retain a supported earlier parent and its native nail mask");
            }
            if(roots==0)throw new ArgumentException("Frozen armor jig has no starting seat");
            int lastLevel=count-roots;var atLevel=new int[lastLevel+1];var stageCounts=new int[lastLevel+1];
            for(int i=0;i<count;i++)
            {
                if(levels[i]<0||levels[i]>lastLevel)throw new ArgumentException("Frozen armor jig has an invalid stage at "+i);
                stageCounts[levels[i]]++;atLevel[levels[i]]=i;
            }
            if(stageCounts[0]!=roots)throw new ArgumentException("Frozen armor jig has a joining part at its starting level");
            for(int level=1;level<=lastLevel;level++)if(stageCounts[level]!=1)
                throw new ArgumentException("Frozen armor jig joining stages must remain contiguous and unique");

            var trees=new JigTree[count];var hulls=new Vector4[count][];
            for(int i=0;i<count;i++){trees[i]=JigBuildTree(visuals[i]);hulls[i]=HullPlanes(collisions[i]);}
            var plan=JigEmptyPlan(count);var clearance=new int[count];
            for(int i=0;i<count;i++)
            {
                clearance[i]=-2;
                if(parents[i]<0)plan.Levels[i]=0;
            }
            // Pending pieces deliberately retain level -1 and no nail mask.
            // JigStageHulls and JigSeparate then see the actual native stage,
            // rather than future ghosts or unselected zero-valued nail poses.
            var edges=new JigEdge[count];var choices=new List<JigChoice>[count];int budget=JigSearchBudget,deepest=0;
            if(JigSearchFrozen(plan,clearance,1,atLevel,parents,requires,edges,choices,trees,hulls,ref budget,ref deepest))
            {LastNailClearanceDegrees=clearance;return plan;}
            LastNailClearanceDegrees=new int[count];for(int i=0;i<count;i++)LastNailClearanceDegrees[i]=-1;
            var diagnostics=new List<string>();
            for(int level=1;level<=lastLevel;level++)
            {
                int child=atLevel[level];var edge=edges[child];if(edge==null)continue;
                diagnostics.Add("stage="+level+" child="+child+" parent="+parents[child]+" surfaceCandidates="+edge.Seams.Count
                    +" strikeChoices="+(choices[child]==null?0:choices[child].Count)+" nearestGap="
                    +(float.IsPositiveInfinity(edge.NearestGap)?"outside 15mm seam range":(edge.NearestGap*1000).ToString("F2",CultureInfo.InvariantCulture)+"mm"));
            }
            throw new InvalidOperationException("Armor jig cannot rebake frozen topology for "+slot+"; deepestStage="+deepest
                +" searchedStates="+(JigSearchBudget-budget)+(budget<=0?" (budget exhausted)":"")+"; "+string.Join("; ",diagnostics.ToArray())
                +". Retain the released child/parent contact within 15mm and an accessible, separated nail site; root, order, parents and nail mask cannot change.");
        }

        static bool JigSearchFrozen(ArmorJigPlan plan,int[] clearance,int level,int[] atLevel,int[] parents,bool[] requires,
            JigEdge[] edges,List<JigChoice>[] choices,JigTree[] trees,Vector4[][] hulls,ref int budget,ref int deepest)
        {
            if(level==atLevel.Length)return true;
            if(budget--<=0)return false;
            deepest=Math.Max(deepest,level);int child=atLevel[level],parent=parents[child];
            if(plan.Levels[parent]<0)throw new InvalidOperationException("Frozen armor jig parent was not completed before its child");
            if(edges[child]==null)edges[child]=JigBuildEdge(child,parent,trees[child],trees[parent],hulls,true);
            if(choices[child]==null)choices[child]=JigBuildChoices(edges[child],JigStageHulls(hulls,plan,child),requires[child]);
            foreach(var choice in choices[child])
            {
                if(requires[child]&&!JigSeparate(choice.Basis,plan))continue;
                plan.Levels[child]=level;plan.Parents[child]=parent;plan.RequiresNails[child]=requires[child];
                plan.JointPoints[child]=choice.Seam.Point;plan.SupportPoints[child]=choice.Seam.Support;plan.JointGaps[child]=choice.Seam.Gap;
                plan.NailPositions[child]=choice.Basis;plan.NailRotations[child]=choice.Rotation;clearance[child]=requires[child]?choice.Clearance:-2;
                if(JigSearchFrozen(plan,clearance,level+1,atLevel,parents,requires,edges,choices,trees,hulls,ref budget,ref deepest))return true;
                plan.Levels[child]=-1;plan.Parents[child]=-1;plan.RequiresNails[child]=false;clearance[child]=-2;
                plan.JointPoints[child]=plan.SupportPoints[child]=plan.NailPositions[child]=Vector3.zero;plan.JointGaps[child]=0;plan.NailRotations[child]=Quaternion.identity;
                if(budget<=0)break;
            }
            return false;
        }
    }
}

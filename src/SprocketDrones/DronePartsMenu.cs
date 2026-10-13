using DesignerFramework.AssetManagement;
using DesignerFramework.UI;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Selection;

namespace SprocketDrones;

[HarmonyPatch]
internal static class DronePartsMenu
{
    [HarmonyPrefix,HarmonyPatch(typeof(SelectionPanel),nameof(SelectionPanel.SetDisplayOptions))]
    private static void Groups(ref Il2CppReferenceArray<ISelectionGroup> __0,ref int __1)
    {
        if(__0==null) return;
        try
        {
            var groups=new List<ISelectionGroup>(); var drones=new List<Option>(); var seen=new HashSet<IntPtr>();
            int oldIndex=0, active=-1; bool selectedDrone=false;
            foreach(var item in __0)
            {
                bool selected=oldIndex++==__1;
                var group=item?.TryCast<OptionGroup>();
                if(group?.items==null) { if(selected) active=groups.Count; groups.Add(item!); continue; }
                var retained=new List<Option>(); bool changed=false;
                foreach(var option in group.items)
                {
                    if(option?.Tags?.Any(t=>t=="sdronePart")==true)
                    { if(seen.Add(option.Pointer)) drones.Add(option); changed=true; }
                    else retained.Add(option!);
                }
                if(!changed) { if(selected) active=groups.Count; groups.Add(item!); continue; }
                if(retained.Count>0)
                {
                    var copy=new OptionGroup(group.Name,retained.ToArray()){Flags=group.Flags,InteractableTest=group.InteractableTest};
                    if(selected) active=groups.Count; groups.Add(copy.Cast<ISelectionGroup>());
                }
                else if(selected) selectedDrone=true;
            }
            if(drones.Count==0) return;
            groups.Add(new OptionGroup("Drones",drones.ToArray()).Cast<ISelectionGroup>());
            __0=new Il2CppReferenceArray<ISelectionGroup>(groups.ToArray());
            __1=selectedDrone || active<0 ? groups.Count-1 : active;
        }
        catch(Exception ex) { Plugin.Instance.Log.LogWarning("[Drones] Menu grouping failed; native options retained: "+ex.Message); }
    }
}

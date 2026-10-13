using System.Reflection;
using HarmonyLib;
using SprocketKeybinds;
using Sprocket.VehicleControl;
using UnityEngine.InputSystem;

namespace SprocketDrones;

[HarmonyPatch]
internal static class DroneInput
{
    private static readonly Dictionary<IntPtr,InputAction> disabled=new();
    // Shared Keybinds otherwise re-enables leased actions each frame, fighting Disable and flooding the log.
    internal static bool EnsureKeys(ModKeybind __instance) => !DroneRuntime.Piloting || __instance.Id.StartsWith(DroneBindings.Owner+"/",StringComparison.Ordinal);
    internal static void Update()
    {
        if(!DroneRuntime.Piloting) { Release(); return; }
        var actions=InputSystem.ListEnabledActions();
        for(int i=0;i<actions.Count;i++)
        {
            var action=actions[i]; if(action==null) continue;
            bool escape=false;
            var bindings=action.bindings;
            for(int b=0;b<bindings.Count;b++) if(bindings[b].effectivePath?.EndsWith("/escape",StringComparison.OrdinalIgnoreCase)==true) { escape=true; break; }
            if(!RuntimeRules.BlockAction(action.actionMap?.asset?.name,action.actionMap?.name,action.name,escape)) continue;
            disabled.TryAdd(action.Pointer,action); action.Disable();
        }
    }
    internal static void Release()
    {
        foreach(var action in disabled.Values)
            try { action.Enable(); } catch(Exception ex) { Plugin.Instance.Log.LogWarning("[Drones] Input restore: "+ex.Message); }
        disabled.Clear();
    }
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> ButtonTargets()
    {
        // Block all native tank command buttons, including scopes, ignition, lights, abandon and orders.
        foreach(var method in typeof(VehicleInputSystemInputs).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
            if(method.ReturnType==typeof(bool)) yield return method;
        foreach(string name in new[]{"IsHeld","WasPressedThisFrame","WasReleasedThisFrame"})
            yield return AccessTools.PropertyGetter(typeof(ModKeybind),name);
        // Installed legacy APS polls raw keyboard buttons. Its input helper is separate from automatic defence.
        var apsType=FindType("SprocketAdvancedAPS.Core.APSCore");
        var aps=apsType==null?null:AccessTools.Method(apsType,"Pressed");
        if(aps!=null) yield return aps;
        // The optional Mod API dispatches raw-key events; gate dispatch rather than only its polled handles.
        var gateType=FindType("SprocketModAPI.ActionGateEvaluator");
        var gate=gateType==null?null:AccessTools.Method(gateType,"IsAllowed");
        if(gate!=null) yield return gate;
    }
    // Exact lookup avoids Harmony scanning every IL2CPP assembly (some contain unloadable metadata types).
    private static Type? FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null);
    [HarmonyPrefix]
    private static bool Buttons(object? __instance,ref bool __result)
    {
        if(__instance is ModKeybind key && key.Id.StartsWith(DroneBindings.Owner+"/",StringComparison.Ordinal)) return true;
        if(!DroneRuntime.CapturingInput) return true;
        __result=false; return false;
    }
}

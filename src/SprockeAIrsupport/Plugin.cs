using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Sprocket;
using UnityEngine;
using UnityEngine.InputSystem;
using SprocketKeybinds;
using Controller = Sprocket.Gameplay.VehicleControl.VehicleController;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using Il2CppInterop.Runtime;

namespace SprocketDrones;

[BepInPlugin("sprocket.drones", "Sprocket Drones", "0.12.0")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.6 <0.2.0")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<float> MouseSensitivity = null!;
    internal static ConfigEntry<float> HeatPenetration = null!;
    internal static ConfigEntry<float> LauncherHeight = null!;
    private UnityAction<Scene,LoadSceneMode>? loadedScene;
    private UnityAction<Scene>? unloadedScene;
    private Harmony? harmony;
    public override void Load()
    {
        Instance = this;
        Enabled = Config.Bind("General", "Enabled", true, "Enable scout and kamikaze drones during combat.");
        MouseSensitivity=Config.Bind("Flight","MouseSensitivity",.12f,new ConfigDescription("Mouse aim degrees per pixel; move mouse to steer, wheel for speed.",new AcceptableValueRange<float>(.01f,1)));
        HeatPenetration=Config.Bind("Warhead","HeatPenetrationMm",DroneCombatRules.DefaultPenetration,new ConfigDescription("Single forward HEAT jet penetration budget in mm RHA (gameplay tuning).",new AcceptableValueRange<float>(50,1000)));
        LauncherHeight=Config.Bind("Launcher","RaisedHeight",LauncherRules.RaisedHeight,new ConfigDescription("Launcher floor height above the compartment mounting plane in metres (10-25 cm).",new AcceptableValueRange<float>(.10f,.25f)));
        try
        {
            DroneBindings.Initialize();
            harmony = new Harmony("sprocket.drones"); harmony.PatchAll(typeof(DroneHooks)); harmony.PatchAll(typeof(DroneWeapons));
            harmony.PatchAll(typeof(DroneParts)); harmony.PatchAll(typeof(DronePartsMenu)); harmony.PatchAll(typeof(DronePartIcons));
            harmony.PatchAll(typeof(DroneInput));
            harmony.Patch(AccessTools.Method(typeof(ModKeybind),"EnsureEnabled"),prefix:new HarmonyMethod(typeof(DroneInput),nameof(DroneInput.EnsureKeys)));
            DroneWeapons.CheckCalibration();
            AddComponent<DroneDriver>();
            loadedScene=DelegateSupport.ConvertDelegate<UnityAction<Scene,LoadSceneMode>>(new Action<Scene,LoadSceneMode>((scene,_)=>DroneRuntime.SceneChanged(scene.name)));
            unloadedScene=DelegateSupport.ConvertDelegate<UnityAction<Scene>>(new Action<Scene>(scene=>DroneRuntime.SceneChanged(scene.name)));
            SceneManager.sceneLoaded+=loadedScene;SceneManager.sceneUnloaded+=unloadedScene;
            Log.LogInfo("[Drones] Loaded 0.12.0: native paint, articulated scout catapult, 150 degree hatches, 10-25 cm lift, camera-return arming and 480 mm HEAT; bachvnju > drone (8 actions).");
        }
        catch (Exception ex) { DroneRuntime.Clear(); harmony?.UnpatchSelf(); DroneBindings.Release(); Log.LogError(ex); }
    }
    public override bool Unload() { if(loadedScene!=null) SceneManager.sceneLoaded-=loadedScene;if(unloadedScene!=null) SceneManager.sceneUnloaded-=unloadedScene;DroneRuntime.Clear(); harmony?.UnpatchSelf(); DroneDriver.Instance?.Dispose(); DroneParts.Clear(); DroneModels.Clear(); DronePartIcons.Clear(); DroneModels.ReleaseMaterials(); DroneHud.Dispose(); DroneBindings.Release(); return true; }
}

public sealed class DroneDriver : MonoBehaviour
{
    internal static DroneDriver? Instance;
    private bool disposed;
    private bool hudFailed;
    public DroneDriver(IntPtr ptr) : base(ptr) { Instance = this; }
    public void Update() { if (!disposed) try { DroneRuntime.Tick(); DroneInput.Update(); } catch (Exception ex) { Fail(ex); } }
    public void LateUpdate()
    {
        if(disposed) return;
        try { DroneParts.FollowMounts();DroneRuntime.View(); } catch(Exception ex) { Fail(ex); }
        if(hudFailed) return;
        try { DroneRuntime.RenderHud(); }
        catch(Exception ex) { hudFailed=true; DroneHud.Dispose(); Plugin.Instance.Log.LogError("[Drones] HUD disabled; flight retained: "+ex); }
    }
    public void OnDestroy() { DroneRuntime.Clear(); Instance = null; }
    internal void Dispose() { disposed = true; DroneRuntime.Clear(); Destroy(this); Instance = null; }
    private static void Fail(Exception ex) { DroneRuntime.Clear(); Plugin.Instance.Log.LogError("[Drones] Runtime stopped and camera restored: " + ex); }
}

[HarmonyPatch]
internal static class DroneHooks
{
    [HarmonyPrefix, HarmonyPatch(typeof(Controller), nameof(Controller.UpdateControl))]
    private static bool Control(Controller __instance, GameTime __0, Camera __3)
    {
        try
        {
            DroneRuntime.Observe(__instance, __0, __3);
            if (!DroneRuntime.Piloting || !DroneRuntime.Owns(__instance)) return true;
            __instance.ResetDriveInputs(); __instance.cruiseControlLevel = 0; __instance.firing = false;
            if (__instance.driver != null) __instance.driver.Solution = Sprocket.VehicleControl.DriveSolution.Idle;
            if (__instance.gunners != null) foreach (var gun in __instance.gunners) if (gun != null) gun.Firing = false;
            if (__instance.gunLayers != null) foreach (var layer in __instance.gunLayers) if (layer != null) layer.ClearTarget();
            return false;
        }
        catch (Exception ex) { DroneRuntime.Clear(); Plugin.Instance.Log.LogError(ex); return true; }
    }
    [HarmonyPrefix, HarmonyPatch(typeof(Controller), nameof(Controller.Release), new Type[] { })]
    private static void Release(Controller __instance) { if (DroneRuntime.Owns(__instance)) DroneRuntime.Clear(); }
    [HarmonyPrefix, HarmonyPatch(typeof(Controller), nameof(Controller.OnAbandon))]
    private static void Abandon(Controller __instance) { if (DroneRuntime.Owns(__instance)) DroneRuntime.Clear(); }
    [HarmonyPrefix, HarmonyPatch(typeof(Controller), nameof(Controller.UpdateOrders))]
    private static bool Orders(Controller __instance) => !DroneRuntime.Piloting || !DroneRuntime.Owns(__instance);
}

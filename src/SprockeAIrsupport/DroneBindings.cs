using SprocketKeybinds;

namespace SprocketDrones;

internal static class DroneBindings
{
    internal const string Owner = "sprocket.drones";
    private static readonly Dictionary<string, ModKeybind> bindings = new();
    internal static void Initialize()
    {
        Keybinds.RegisterModMetadata(Owner,"bachvnju","drone");
        // API 0.1.6 supports buttons only. A rebindable modifier gates all shortcuts.
        Register("modifier","Hold shortcut modifier (Alt / change to Shift)","leftAlt","rightAlt");
        Register("deploy-scout","Modifier + key: launch / select scout","h");
        Register("deploy-kamikaze","Modifier + key: launch / select kamikaze","k");
        Register("toggle-control","Modifier + key: switch tank / drone","j");
        Register("return","Modifier + key: disarm and recover","l");
        Register("loiter","Modifier + key: toggle orbit / mouse flight","g");
        Register("camera","Modifier + key: sensor / flight camera","c");
        Register("context","Modifier + key: scout lock + mark / arm kamikaze","x");
    }
    private static void Register(string id,string label,string key,string? secondary=null) =>
        bindings.Add(id,Keybinds.RegisterButton(Owner,"drone",id,label,"<Keyboard>/"+key,secondary==null?null:"<Keyboard>/"+secondary));
    internal static bool Pressed(string id) => bindings["modifier"].IsHeld && bindings[id].WasPressedThisFrame;
    internal static bool LaunchRequested => bindings.Count==8 && (Pressed("deploy-scout") || Pressed("deploy-kamikaze"));
    internal static string Label(string id)
    {
        string modifier=bindings["modifier"].GetBindingDisplayString();
        if(modifier.Length==0) modifier=bindings["modifier"].GetBindingDisplayString(1);
        return modifier+" + "+bindings[id].GetBindingDisplayString();
    }
    internal static void Release()
    {
        foreach(var id in bindings.Keys.ToArray()) Keybinds.Unregister(Owner,id);
        bindings.Clear();
    }
}

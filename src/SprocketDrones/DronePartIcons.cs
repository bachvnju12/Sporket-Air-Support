using HarmonyLib;
using Sprocket;
using Sprocket.PartImporting;
using UnityEngine;

namespace SprocketDrones;

[HarmonyPatch]
internal static class DronePartIcons
{
    private static readonly Dictionary<string,(Sprite Sprite,Texture2D Texture)> icons=new();
    private static Sprite? Load(string? key)
    {
        string? id=key==DronePartCatalog.ScoutId || key==DronePartCatalog.ScoutGuid?DronePartCatalog.ScoutId:
            key==DronePartCatalog.StrikeId || key==DronePartCatalog.StrikeGuid?DronePartCatalog.StrikeId:null;
        if(id==null) return null;
        if(icons.TryGetValue(id,out var cached)) return cached.Sprite;
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
        try
        {
            var path=Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,"icons",id+".png");
            if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false)) throw new InvalidDataException("Invalid drone icon");
            var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
            sprite.hideFlags=HideFlags.HideAndDontSave;
            icons.Add(id,(sprite,texture)); return sprite;
        }
        catch(Exception ex) { UnityEngine.Object.Destroy(texture); Plugin.Instance.Log.LogWarning("[Drones] Icon load: "+ex.Message); return null; }
    }
    [HarmonyPostfix,HarmonyPatch(typeof(GameIcons),nameof(GameIcons.GetOrDefault))]
    private static void Lookup(string __0,ref Sprite __result) { var icon=Load(__0); if(icon!=null) __result=icon; }
    [HarmonyPostfix,HarmonyPatch(typeof(PartDefinitionCardFactory),nameof(PartDefinitionCardFactory.CreateCard))]
    private static void Created(PartDisplayCard __result)
    { if(__result==null) return; var icon=Load(__result.Identifier)??Load(__result.PartGuid); if(icon!=null) __result.Icon=icon; }
    internal static void Clear()
    { foreach(var pair in icons.Values) { UnityEngine.Object.Destroy(pair.Sprite); UnityEngine.Object.Destroy(pair.Texture); } icons.Clear(); }
}

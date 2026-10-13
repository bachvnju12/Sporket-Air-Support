using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace SprocketDrones;

// Native Canvas/TMP telemetry. No IMGUI: stripped GUI methods can throw in this game build.
internal static class DroneHud
{
    private static GameObject? root;
    private static Canvas? canvas;
    private static Material? fontMaterial;
    private static TMP_FontAsset? font;
    private static TextMeshProUGUI? identity,mode,status,speed,altitude,link,energy,health,prompt,message,range,markerLabel;
    private static RectTransform? healthFill;
    private static RectTransform? aim,nose,marker,crosshair;
    private static readonly List<(RectTransform Rect,TextMeshProUGUI Text)> headings=new();
    private static readonly List<GameObject> flightOnly=new();
    private static float nextText;
    private static bool sensorColors;
    private static readonly Color Ink=new(.95f,.96f,.93f,1),Amber=new(1,.69f,.25f,1);
    private static RectTransform Node(string name,Transform parent,Vector2 anchor,Vector2 offset,Vector2 size)
    {
        var go=new GameObject(name){layer=5}; go.transform.SetParent(parent,false);
        var rect=go.AddComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=anchor; rect.pivot=new(.5f,.5f); rect.anchoredPosition=offset; rect.sizeDelta=size;
        return rect;
    }
    private static TextMeshProUGUI Text(string name,Vector2 anchor,Vector2 offset,Vector2 size,float fontSize,TextAlignmentOptions align=TextAlignmentOptions.Center)
    {
        var rect=Node(name,root!.transform,anchor,offset,size);
        var text=rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font=font!; text.fontSharedMaterial=fontMaterial!;
        text.fontSize=fontSize; text.alignment=align; text.color=Ink; text.raycastTarget=false; text.enableWordWrapping=false;
        text.overflowMode=TextOverflowModes.Ellipsis; text.text="---";
        flightOnly.Add(rect.gameObject); return text;
    }
    private static RectTransform Line(Transform parent,string name,Vector2 position,Vector2 size,float angle=0)
    {
        var rect=Node(name,parent,new(.5f,.5f),position,size); rect.localRotation=Quaternion.Euler(0,0,angle);
        var image=rect.gameObject.AddComponent<Image>(); image.color=Ink; image.raycastTarget=false; return rect;
    }
    private static RectTransform Ring(string name,float radius,int segments)
    {
        var rect=Node(name,root!.transform,new(.5f,.5f),Vector2.zero,new(radius*2,radius*2));
        for(int i=0;i<segments;i++)
        { float a=i*Mathf.PI*2/segments; Line(rect,name+i,new(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius),new(radius*Mathf.PI*2/segments+1,1.4f),a*Mathf.Rad2Deg+90); }
        flightOnly.Add(rect.gameObject); return rect;
    }
    internal static bool Warm()
    {
        if(root!=null) return true;
        font=TMP_Settings.defaultFontAsset ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
        if(font==null) return false;
        root=new GameObject("Drone telemetry"){layer=5}; Object.DontDestroyOnLoad(root);
        canvas=root.AddComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=260;
        var scaler=root.AddComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new(1600,900); scaler.matchWidthOrHeight=1;
        fontMaterial=new Material(font.material){hideFlags=HideFlags.HideAndDontSave};
        fontMaterial.SetColor("_OutlineColor",new(0,0,0,.85f)); fontMaterial.SetFloat("_OutlineWidth",.18f); fontMaterial.EnableKeyword("OUTLINE_ON");
        identity=Text("Aircraft",new(0,1),new(160,-55),new(250,28),18,TextAlignmentOptions.Left);
        mode=Text("Flight mode",new(0,1),new(160,-84),new(250,25),15,TextAlignmentOptions.Left);
        speed=Text("Speed",new(0,.5f),new(135,0),new(190,36),26);
        altitude=Text("AGL altitude",new(1,.5f),new(-135,0),new(190,36),26);
        link=Text("Datalink",new(1,1),new(-175,-55),new(285,26),17,TextAlignmentOptions.Right);
        energy=Text("Battery",new(1,1),new(-175,-83),new(285,26),17,TextAlignmentOptions.Right);
        health=Text("Airframe health",new(0,1),new(160,-113),new(250,24),15,TextAlignmentOptions.Left);
        var healthBar=Node("Airframe health bar",root.transform,new(0,1),new(86,-135),new(100,3)); flightOnly.Add(healthBar.gameObject);
        var healthBg=healthBar.gameObject.AddComponent<Image>(); healthBg.color=new(.1f,.1f,.1f,.6f); healthBg.raycastTarget=false;
        healthFill=Line(healthBar,"Health fill",Vector2.zero,new(100,3)); healthFill.pivot=new(0,.5f); healthFill.anchoredPosition=new(-50,0);
        status=Text("Payload",new(.5f,1),new(0,-102),new(350,28),17);
        prompt=Text("Contextual shortcuts",new(.5f,0),new(0,45),new(1100,26),15);
        range=Text("Sensor range",new(.5f,.5f),new(150,-55),new(260,24),16,TextAlignmentOptions.Left);
        message=Text("Notice",new(.5f,0),new(0,83),new(1100,30),17); flightOnly.Remove(message.gameObject);
        markerLabel=Text("Recon marker",new(.5f,.5f),Vector2.zero,new(100,23),14); flightOnly.Remove(markerLabel.gameObject);
        crosshair=Node("Sensor crosshair",root.transform,new(.5f,.5f),Vector2.zero,new(80,80)); flightOnly.Add(crosshair.gameObject);
        foreach(int side in new[]{-1,1}) { Line(crosshair,"Horizontal",new(side*27,0),new(16,1.5f)); Line(crosshair,"Vertical",new(0,side*27),new(1.5f,16)); }
        aim=Ring("Mouse direction",18,40); nose=Ring("Flight path",7,24);
        Line(nose,"Wings",new(-16,0),new(15,1.4f)); Line(nose,"Wings",new(16,0),new(15,1.4f)); Line(nose,"Tail",new(0,12),new(1.4f,10));
        marker=Node("Recon diamond",root.transform,new(.5f,.5f),Vector2.zero,new(20,20));
        for(int i=0;i<4;i++) { float a=(i*90+45)*Mathf.Deg2Rad; Line(marker,"Diamond",new(Mathf.Cos(a)*10,Mathf.Sin(a)*10),new(15,1.5f),i*90+135); }
        for(int i=-4;i<=4;i++)
        {
            var label=Text("Heading",new(.5f,1),new(i*58,-44),new(65,20),14);
            var rect=label.GetComponent<RectTransform>(); headings.Add((rect,label));
            var tick=Line(rect,"Heading tick",new(0,-18),new(1.3f,8));
        }
        var index=Node("Heading index",root.transform,new(.5f,1),new(0,-77),new(20,10)); flightOnly.Add(index.gameObject);
        Line(index,"Index",new(-4,0),new(10,1.5f),-45); Line(index,"Index",new(4,0),new(10,1.5f),45);
        foreach(var node in new[]{range!.gameObject,crosshair.gameObject,aim.gameObject,nose.gameObject}) flightOnly.Remove(node);
        root.SetActive(false);
        Plugin.Instance.Log.LogInfo("[Drones] Native Canvas/TMP HUD initialized; IMGUI disabled."); return true;
    }
    private static void Project(RectTransform rect,Vector3 point,Camera camera,float scale,float width,float height)
    {
        var screen=camera.WorldToScreenPoint(point); rect.gameObject.SetActive(screen.z>0);
        if(screen.z>0) rect.anchoredPosition=new(Math.Clamp(screen.x/scale,35,width-35)-width/2,Math.Clamp(screen.y/scale,35,height-35)-height/2);
    }
    internal static void Update(Drone? drone,bool piloting,Camera? camera,Vector3 owner,Vector3? recon,string notice)
    {
        if(!Warm()) return;
        bool visible=camera!=null && (piloting || recon.HasValue || notice.Length>0 || drone!=null);
        root!.SetActive(visible); if(!visible) return;
        foreach(var node in flightOnly) node.SetActive(piloting && drone!=null);
        marker!.gameObject.SetActive(recon.HasValue); markerLabel!.gameObject.SetActive(recon.HasValue);
        float scale=Math.Max(.01f,canvas!.scaleFactor),width=Screen.width/scale,height=Screen.height/scale;
        if(recon.HasValue)
        { Project(marker,recon.Value,camera!,scale,width,height); markerLabel.GetComponent<RectTransform>().anchoredPosition=marker.anchoredPosition+new Vector2(0,-27); markerLabel.text="RECON"; }
        message!.text=notice.Length>0?notice:!piloting && drone!=null?"UAV airborne   "+DroneBindings.Label("toggle-control")+"  return to drone":"";
        bool flying=piloting && drone!=null,sensor=flying && drone!.Sensor && drone.Flight.Kind==DroneKind.Scout;
        crosshair!.gameObject.SetActive(sensor); aim!.gameObject.SetActive(flying && !sensor); nose!.gameObject.SetActive(flying && !sensor); range!.gameObject.SetActive(sensor);
        if(sensor!=sensorColors)
        {
            sensorColors=sensor; var tint=sensor?new Color(.43f,.88f,.39f,1):Ink;
            foreach(var image in crosshair.GetComponentsInChildren<Image>(true)) image.color=tint;
            foreach(var item in headings) { item.Text.color=tint; foreach(var image in item.Rect.GetComponentsInChildren<Image>(true)) image.color=tint; }
            range.color=tint;
        }
        if(!flying || drone==null) return;
        if(!sensor)
        {
            var direction=Quaternion.Euler(-drone.Flight.AimPitch,drone.Flight.AimHeading,0)*Vector3.forward;
            if(drone.Flight.Mode!=FlightMode.Manual) direction=DroneRuntime.ToU(drone.Flight.Forward);
            Project(aim,drone.Position+direction*100,camera!,scale,width,height);
            Project(nose,drone.Position+DroneRuntime.ToU(drone.Flight.Forward)*100,camera!,scale,width,height);
        }
        if(Time.unscaledTime<nextText) return; nextText=Time.unscaledTime+.1f;
        identity!.text=drone.Flight.Kind==DroneKind.Scout?"SCOUT":"KAMIKAZE";
        mode!.text=drone.Launching?"LAUNCHING":sensor?"STABILIZED TV":(drone.Sensor?"FPV  /  ":"FLIGHT  /  ")+drone.Flight.Mode.ToString().ToUpperInvariant();
        speed!.text=$"{drone.Flight.Speed*3.6f:0} <size=14>km/h</size>";
        float agl=Physics.Raycast(drone.Position,Vector3.down,out var ground,5000,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)?ground.distance:drone.Position.y;
        altitude!.text=$"{agl:0} <size=14>m AGL</size>";
        link!.text=$"LINK  {Vector3.Distance(drone.Position,owner):0} m"; energy!.text=$"BAT  {drone.Flight.Battery/60:0.0} min";
        health!.text=$"HP  {drone.Health:0}/{drone.MaxHealth:0}"; health.color=drone.Health/drone.MaxHealth<=.3f?Amber:Ink;
        healthFill!.sizeDelta=new(100*Math.Clamp(drone.Health/drone.MaxHealth,0,1),3); healthFill.GetComponent<Image>().color=health.color;
        status!.text=drone.Flight.Kind==DroneKind.Kamikaze?(drone.Flight.Armed?"ARMED  /  IMPACT":"SAFE  /  "+DroneBindings.Label("context")+" ARM"):"";
        status.color=drone.Flight.Armed?Amber:Ink;
        prompt!.text=DroneBindings.Label("camera")+" Camera     "+DroneBindings.Label("loiter")+" Orbit     "+DroneBindings.Label("toggle-control")+" Tank";
        if(sensor) { float distance=drone.Lock.HasValue?Vector3.Distance(drone.Position,drone.Lock.Value):DroneRuntime.Aim(drone,out var target)?Vector3.Distance(drone.Position,target):0; range.text=$"{(drone.Lock.HasValue?"LOCK":"FREE")}  {distance:0} m  {60/drone.Fov:0.0}x"; }
        float heading=(drone.Flight.Heading+360)%360,baseHeading=MathF.Floor(heading/10)*10;
        for(int i=0;i<headings.Count;i++)
        { var (rect,label)=headings[i]; float value=baseHeading+(i-4)*10; label.text=$"{(value+360)%360:000}"; rect.anchoredPosition=new((value-heading)*5.8f,-44); }
    }
    internal static void Hide() { if(root!=null) root.SetActive(false); }
    internal static void Dispose()
    {
        if(root!=null) Object.Destroy(root); if(fontMaterial!=null) Object.Destroy(fontMaterial);
        root=null; canvas=null; fontMaterial=null; font=null; flightOnly.Clear(); headings.Clear();
        sensorColors=false;
    }
}

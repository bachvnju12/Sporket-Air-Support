namespace SprocketDrones;

public static class LauncherRules
{
    public const float UnlockTime=.18f,HatchOpenTime=.80f,LiftTime=1.80f,RaiseTime=2.45f,LidOpenTime=2.85f,ReleaseTime=3.35f,FlightTime=3.95f,CloseTime=4.35f,LowerTime=5f,StowTime=6f,HatchCloseTime=6.65f,RelockTime=6.85f;
    public const float StowedHeight=-.405f,RaisedHeight=.20f,HatchOpenAngle=150;
    public static float PoseAge(float elapsed) => elapsed<0?-1:Math.Min(elapsed,RelockTime);
    public static bool PayloadVisible(float elapsed) => elapsed>=0 && elapsed<ReleaseTime && HatchAngle(elapsed)>.5f;
    // Zero velocity and acceleration at both ends avoids a jerk when mechanical stages start/stop.
    private static float Smooth(float t) { t=Math.Clamp(t,0,1); return t>.5f?1-Smooth(1-t):t*t*t*(10+t*(-15+6*t)); }
    public static float LatchAngle(float elapsed) => elapsed<0?0:75*Smooth(elapsed/UnlockTime)*(1-Smooth((elapsed-HatchCloseTime)/(RelockTime-HatchCloseTime)));
    public static float Elevation(float elapsed)
    {
        if(elapsed<0) return 0;
        return 60*(elapsed<RaiseTime?Smooth((elapsed-LiftTime)/(RaiseTime-LiftTime)):1-Smooth((elapsed-CloseTime)/(LowerTime-CloseTime)));
    }
    public static float Elevation(DroneKind kind,float elapsed) => Elevation(elapsed)*(kind==DroneKind.Scout?.25f:1);
    public static float HatchAngle(float elapsed) => elapsed<0?0:HatchOpenAngle*(elapsed<HatchOpenTime?Smooth((elapsed-UnlockTime)/(HatchOpenTime-UnlockTime)):1-Smooth((elapsed-StowTime)/(HatchCloseTime-StowTime)));
    public static float LiftHeight(float elapsed,float raisedHeight=RaisedHeight)
    {
        float raised=elapsed<0?0:elapsed<LiftTime?Smooth((elapsed-HatchOpenTime)/(LiftTime-HatchOpenTime)):1-Smooth((elapsed-LowerTime)/(StowTime-LowerTime));
        return StowedHeight+(raisedHeight-StowedHeight)*raised;
    }
    public static float LidAngle(float elapsed)
    {
        if(elapsed<0) return 0;
        float fraction=elapsed<LidOpenTime?(elapsed-RaiseTime)/(LidOpenTime-RaiseTime):elapsed<FlightTime?1:1-(elapsed-FlightTime)/(CloseTime-FlightTime);
        return 120*Smooth(fraction);
    }
    public static float EjectionDistance(float elapsed)
    {
        float t=Math.Max(0,elapsed-ReleaseTime),u=Math.Max(0,t-.10f);
        return t<.10f?60*t*t*t:.06f+1.8f*u+18*u*u;
    }
    public static float EjectionSpeed(float elapsed)
    {
        float t=Math.Max(0,elapsed-ReleaseTime); return t<.10f?180*t*t:1.8f+36*(t-.10f);
    }
    public static float SledDistance(float elapsed) => Math.Min(.36f,EjectionDistance(elapsed))*(1-Smooth((elapsed-CloseTime)/(LowerTime-CloseTime)));
    public static float WingDeployment(float elapsed,int stage=0) => Smooth((elapsed-ReleaseTime-.33f-stage*.025f)/.22f);
    public static float WingDeployment(DroneKind kind,float elapsed,int stage=0) => kind==DroneKind.Scout?Smooth((elapsed-LidOpenTime-stage*.04f)/.40f):WingDeployment(elapsed,stage);
    public static float PropellerSpeed(float elapsed) => 1600*Smooth((elapsed-ReleaseTime-.20f)/.40f);
    public static float PropellerAngle(float elapsed)
    {
        float t=Math.Max(0,elapsed-ReleaseTime-.20f),u=Math.Min(1,t/.40f);
        return 1600*(.40f*u*u*u*u*(2.5f-3*u+u*u)+Math.Max(0,t-.40f));
    }
    public static float CameraBlend(float elapsed) => Smooth((elapsed-ReleaseTime-.20f)/.40f);
}

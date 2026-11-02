using UnityEngine;

/// <summary>A distance-sampled open arch with straight ground-entry stems and rounded crown corners.</summary>
public sealed class StalkerFlightPath
{
    private Vector3 _origin, _landing, _forward, _normal;
    private float _radius, _riseStem, _fallStem, _quarter, _crown, _height, _width;
    public Vector3 ApexPosition { get; private set; }
    public float ApexDistance { get; private set; }
    public float Length { get; private set; }
    public float AuthoredFallDistance { get; private set; }

    public void Configure(Vector3 origin, Vector3 apex, Vector3 landing, float bodyLength)
    {
        _origin=origin; _landing=landing;
        Vector3 planar=Vector3.ProjectOnPlane(landing-origin,Vector3.up);
        _width=Mathf.Max(.01f,planar.magnitude);
        _forward=planar.sqrMagnitude>.000001f?planar.normalized:Vector3.forward;
        _normal=Vector3.Cross(Vector3.up,_forward).normalized;
        AuthoredFallDistance=PreviousDiveDistance(origin,apex,landing,bodyLength,_forward);
        _height=Mathf.Max(apex.y,Mathf.Max(origin.y,landing.y)+.01f);
        // Fit the largest useful rounded corner inside the actual route. There are no
        // backward handles, loops or overshoot beyond either gameplay endpoint.
        _radius=Mathf.Max(.005f,Mathf.Min(_width*.5f,bodyLength*.2f,
            _height-origin.y,_height-landing.y));
        _riseStem=_height-origin.y-_radius;
        _fallStem=_height-landing.y-_radius;
        _quarter=Mathf.PI*.5f*_radius;
        _crown=Mathf.Max(0,_width-2*_radius);
        float apexX=Mathf.Clamp(Vector3.Dot(apex-origin,_forward),_radius,_width-_radius);
        ApexPosition=origin+_forward*apexX+Vector3.up*(_height-origin.y);
        ApexDistance=_riseStem+_quarter+apexX-_radius;
        Length=_riseStem+2*_quarter+_crown+_fallStem;
    }

    public Vector3 GetPosition(float distance)
    {
        if(distance<=0) return _origin+Vector3.up*distance;
        if(distance>=Length) return _landing-Vector3.up*(distance-Length);
        if(distance<_riseStem) return _origin+Vector3.up*distance;
        distance-=_riseStem;
        if(distance<_quarter)
        {
            float angle=distance/_radius;
            return _origin+_forward*(_radius*(1-Mathf.Cos(angle)))
                +Vector3.up*(_riseStem+_radius*Mathf.Sin(angle));
        }
        distance-=_quarter;
        if(distance<_crown) return _origin+_forward*(_radius+distance)+Vector3.up*(_height-_origin.y);
        distance-=_crown;
        if(distance<_quarter)
        {
            float angle=distance/_radius;
            return _origin+_forward*(_width-_radius+_radius*Mathf.Sin(angle))
                +Vector3.up*(_height-_origin.y-_radius*(1-Mathf.Cos(angle)));
        }
        return _landing+Vector3.up*(_fallStem-(distance-_quarter));
    }

    public Vector3 GetTangent(float distance)
    {
        if(distance<=_riseStem) return Vector3.up;
        distance-=_riseStem;
        if(distance<_quarter)
        {
            float angle=distance/_radius;
            return _forward*Mathf.Sin(angle)+Vector3.up*Mathf.Cos(angle);
        }
        distance-=_quarter;
        if(distance<_crown) return _forward;
        distance-=_crown;
        if(distance<_quarter)
        {
            float angle=distance/_radius;
            return _forward*Mathf.Cos(angle)-Vector3.up*Mathf.Sin(angle);
        }
        return Vector3.down;
    }

    public Quaternion GetRotation(float distance)
    {
        Vector3 up=GetTangent(distance);
        return Quaternion.LookRotation(Vector3.Cross(_normal,up),up);
    }

    // Timing calibration only: retain the previous authored dive duration when the
    // corrected forward-only path is shorter. No part of the body follows this curve.
    private static float PreviousDiveDistance(Vector3 origin,Vector3 apex,Vector3 landing,float bodyLength,Vector3 forward)
    {
        float rise=Mathf.Max(.5f,apex.y-origin.y),fall=Mathf.Max(.5f,apex.y-landing.y);
        float handle=Mathf.Clamp(bodyLength*.45f,2f,Mathf.Max(2f,Mathf.Min(rise,fall)*.55f));
        Vector3 b=apex+forward*handle,c=landing+Vector3.up*(fall*.7f)+forward*(handle*.25f);
        Vector3 previous=apex;float distance=0;
        for(int i=1;i<=128;i++)
        {
            float t=i/128f,s=1-t;
            Vector3 point=s*s*s*apex+3*s*s*t*b+3*s*t*t*c+t*t*t*landing;
            distance+=Vector3.Distance(previous,point);previous=point;
        }
        return Mathf.Max(.01f,distance);
    }
}

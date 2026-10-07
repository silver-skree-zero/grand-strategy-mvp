using System;
using Unity.VisualScripting;
using UnityEngine;

public class UniversalDateTime : MonoBehaviour
{
    public static UniversalDateTime Instance { get; private set; }

    [SerializeField] private Light sun = null;
    [SerializeField] private Transform earthTransform;
    [SerializeField] private float calibrationOffset;
    [SerializeField] private Material atmosphereMaterial;
    [SerializeField] private Simulator simulator;

    private Transform sunTransform;

    public DateTime Utc
    {
        get
        {
            return simulator.Utc;
        }
    }

    public float SolarDeclination
    {
        get
        {
            double dayOfYear = Utc.DayOfYear;

            double angle =
                2.0 * Math.PI * (dayOfYear - 80.0) / 365.2422;

            return 23.44f * (float)Math.Sin(angle);
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        DateTime currentTime = Utc;
        double hour = currentTime.Hour;
        double minute = currentTime.Minute;
        double second = currentTime.Second;

        earthTransform.SetPositionAndRotation(new Vector3(0,0,0), Quaternion.Euler(new Vector3(0, calibrationOffset + -360.0f * (float)((hour / 24) + ((minute / 24) / 60) + ((second / 24) / 3600)),0)));
        
        sunTransform = sun.transform;
        sunTransform.SetPositionAndRotation(sunTransform.position, Quaternion.Euler(new Vector3()));
    }

    private void Update()
    {
        DateTime currentTime = Utc;
        double hour = currentTime.Hour;
        double minute = currentTime.Minute;
        double second = currentTime.Second;

        float rotation = calibrationOffset + 360.0f * (float)((hour / 24) + ((minute / 24) / 60) + ((second / 24) / 3600));

        earthTransform.SetPositionAndRotation(new Vector3(0,0,0), Quaternion.Euler(new Vector3(0,0,0)));

        sunTransform.SetPositionAndRotation(sunTransform.position, Quaternion.Euler(new Vector3(SolarDeclination, rotation, 0)));

        Vector3 sunDirection = -sunTransform.forward;

        RenderSettings.skybox.SetFloat("_Rotation", -rotation);

        atmosphereMaterial.SetVector(
            "_SunDirection",
            sunDirection
        );
    }
}
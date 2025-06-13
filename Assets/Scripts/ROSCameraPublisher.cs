using System;
using UnityEngine;
using Unity.Robotics.Core;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Sensor;

public class ROSCameraPublisher : MonoBehaviour
{
    [SerializeField]
    string m_ImageTopic = "camera_test";

    [SerializeField] 
    double m_PublishRateHz = 1f;

    [SerializeField]
    RenderTexture m_ImageSource;

    double m_LastPublishTimeSeconds;

    long frameId;

    ROSConnection m_ROS;

    double PublishPeriodSeconds => 1.0f / m_PublishRateHz;
    bool ShouldPublishMessage => Clock.FrameStartTimeInSeconds - PublishPeriodSeconds > m_LastPublishTimeSeconds;

    public ROSCameraPublisher()
    {
        m_LastPublishTimeSeconds = 0;
        frameId = 0;
    }

    public Texture2D ReadRenderTexture(RenderTexture renderTexture)
    {
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture.active = renderTexture;

        Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        tex.Apply();

        RenderTexture.active = currentRT;
        return tex;
    }


    void OnValidate()
    {
        var cameras = FindObjectsOfType<ROSCameraPublisher>();
        if (cameras.Length > 1)
        {
            Debug.LogWarning("Found too many camera publishers in the scene, there should only be one!");
        }
    }

    void Start()
    {
        m_ROS = ROSConnection.GetOrCreateInstance();
        m_ROS.RegisterPublisher<ImageMsg>(m_ImageTopic);
    }

    void PublishMessage()
    {
        var publishTime = Clock.time;

        Texture2D texture = ReadRenderTexture(m_ImageSource);
        byte[] bytes = texture.GetRawTextureData();

        var imageMsg = new ImageMsg
        {
            header = new RosMessageTypes.Std.HeaderMsg() {
                stamp = new TimeMsg
                {
                    sec = (int)publishTime,
                    nanosec = (uint)((publishTime - Math.Floor(publishTime)) * Clock.k_NanoSecondsInSeconds)
                },
                frame_id = frameId.ToString()
            },
            encoding = "rgb8",
            width = (uint)texture.width,
            height = (uint)texture.height,
            data = bytes,
        };
        
        m_LastPublishTimeSeconds = publishTime;
        m_ROS.Publish(m_ImageTopic, imageMsg);
    }

    void Update()
    {
        if (ShouldPublishMessage)
        {
            PublishMessage();
        }
    }
}

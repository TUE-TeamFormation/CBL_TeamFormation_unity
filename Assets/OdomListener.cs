using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using System.Linq;

using System.IO;
using RosMessageTypes.Nav;           // nav_msgs/Odometry
using RosMessageTypes.Sensor;        // sensor_msgs/JointState (optional)



[System.Serializable]                 // so JsonUtility can serialise it
public class StampedPose
{
    public double rosStamp;           //  UNIX seconds from /odom header
    public float timeInUnity;             //  Time.realtimeSinceStartup (s)
    public float timeTillPreviousCallback; // delta_t since *previous* callback (s)

    public Vector3 position;          //  Unity world metres
    public Quaternion rotation;       //  already FLU->Unity corrected
}

[System.Serializable]
class PoseLogWrapper                   // wrapper because JsonUtility
{                                      // can't serialise bare List<>
    public List<StampedPose> poses = new();
}


public class OdomListener : MonoBehaviour
{
    static readonly Quaternion ROS_TO_UNITY_YAW_OFFSET = Quaternion.Euler(0f, 0f, 0f);
    static readonly Vector3 ROS_TO_UNITY_POS_OFFSET = new Vector3(0f, 0.02f, 0f);

    [SerializeField] string odomTopic = "/odom";
    ROSConnection ros;

    PoseLogWrapper log = new();
    float m_LastOdomCallbackTime = -1f;

    Transform transformComponent = null;

    Vector3 m_TwistLinearVel = Vector3.zero;
    float m_TwistAngularVel = 0.0f; // yaw

    Quaternion m_LastPoseRotation = Quaternion.identity; // for drift snap (yaw)
    Quaternion m_DeltaPoseRotation = Quaternion.identity; // for drift snap (yaw)
    Quaternion m_AcumPoseRotation = Quaternion.identity; // for drift snap (yaw)

    Vector3 m_LastPosePosition = Vector3.zero;
    Vector3 m_DeltaPosePosition = Vector3.zero;
    Vector3 m_AccumPosePosition = Vector3.zero;

    bool m_HaveFirstPose = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<OdometryMsg>(odomTopic, OdomCallback);

        transformComponent = GetComponent<Transform>();
        InitialYPosition = transformComponent.position.y;
    }

    

    public float BlendFactorRotation = 0.512f;
    public float BlendFactorPosition = 0.5f;
    private float InitialYPosition = 0.0f;

    void OdomCallback(OdometryMsg msg)
    {
        // Get Linear/Angualar velocities
        m_TwistLinearVel = msg.twist.twist.linear.From<FLU>();
        m_TwistAngularVel = (float)msg.twist.twist.angular.z;

        // Get robot's yaw
        Quaternion currentPoseYaw = msg.pose.pose.orientation.From<FLU>().projectYaw();
        currentPoseYaw = ROS_TO_UNITY_YAW_OFFSET * currentPoseYaw;

        if (!m_HaveFirstPose)
        {
            m_LastPoseRotation = currentPoseYaw;
        }
        else
        {
            Quaternion lastAcumPoseRotation = m_AcumPoseRotation;

            m_DeltaPoseRotation = currentPoseYaw * Quaternion.Inverse(m_LastPoseRotation);
            m_AcumPoseRotation = m_DeltaPoseRotation * m_AcumPoseRotation;
            m_LastPoseRotation = currentPoseYaw;
        }

        // Get robot's position
        Vector3 currentPosePosition = msg.pose.pose.position.From<FLU>();
        currentPosePosition = new Vector3(currentPosePosition.x, 0f, currentPosePosition.z);

        if (!m_HaveFirstPose)
        {
            m_LastPosePosition = currentPosePosition;
            m_HaveFirstPose = true;
        }
        else
        {
            m_DeltaPosePosition = currentPosePosition - m_LastPosePosition;
            m_AccumPosePosition += m_DeltaPosePosition;
            m_LastPosePosition = currentPosePosition;
        }

        // LOG
        var entry = new StampedPose();
        entry.rosStamp = msg.header.stamp.sec + msg.header.stamp.nanosec * 1e-9;
        entry.timeInUnity = Time.realtimeSinceStartup;
        entry.position = currentPosePosition;
        entry.rotation = currentPoseYaw;

        entry.timeTillPreviousCallback = (m_LastOdomCallbackTime < 0f) ? 0f : entry.timeInUnity - m_LastOdomCallbackTime;
        m_LastOdomCallbackTime = entry.timeInUnity;

        log.poses.Add(entry);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;


        /* ---------- 1. rotation: integrate, then drift-correct ---------- */
        //float deltaYawDeg = -m_TwistAngularVel * Mathf.Rad2Deg * dt; // negate for ROS->Unity handedness
        //Quaternion deltaRotation = Quaternion.AngleAxis(deltaYawDeg, Vector3.up);  // world-up

        //// Apply increment
        //Quaternion predictedRotation = deltaRotation * transform.rotation;
        //transform.rotation = Quaternion.Slerp(predictedRotation, m_LastPoseRotation, BlendFactorRotation);

        // Method 2
        //Quaternion rotationQ = Quaternion.Slerp(transform.rotation, transform.rotation * deltaPoseYaw, blendFactor); // soft snap
        //transform.rotation = rotationQ;


        // Method 3
        transform.rotation = Quaternion.Slerp(transform.rotation, m_AcumPoseRotation, BlendFactorRotation); // soft snap


        /* ---------- 2. position: integrate, then drift-correct ---------- */
        Vector3 deltaWorld = transform.TransformDirection(m_TwistLinearVel) * dt;
        Vector3 predictedPos = transform.position + deltaWorld;
        
        transform.position = Vector3.Lerp(predictedPos, m_AccumPosePosition, BlendFactorPosition);
        transform.position = new Vector3(transform.position.x, InitialYPosition, transform.position.z);
    }

    void OnApplicationQuit()           // called in Editor and builds
    {
        string json = JsonUtility.ToJson(log, true);

        string stamp = System.DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss.fff");
        string filePath = $"Log_Poses/odom_{stamp}.json";

        File.WriteAllText(filePath, json);
        Debug.Log($"[OdomRecorder] Saved {log.poses.Count} poses to {filePath}");
    }
}

/* Helper: keep only yaw component of a quaternion (world-up in Unity) */
public static class QuaternionExtensions
{
    public static Quaternion projectYaw(this Quaternion q)
    {
        Vector3 fwd = q * Vector3.forward;
        fwd.y = 0f;                     // drop pitch
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        return Quaternion.LookRotation(fwd, Vector3.up);
    }
}
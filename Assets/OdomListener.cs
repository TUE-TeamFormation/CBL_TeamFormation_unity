using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using System.Linq;

using RosMessageTypes.Nav;           // nav_msgs/Odometry
using RosMessageTypes.Sensor;        // sensor_msgs/JointState (optional)

public class OdomListener : MonoBehaviour
{
    [SerializeField] string odomTopic = "/odom";
    ROSConnection ros;

    Transform transformComponent = null;

    Vector3 linearVel = Vector3.zero;
    float angularVelZ = 0.0f; // yaw

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<OdometryMsg>(odomTopic, OdomCallback);

        transformComponent = GetComponent<Transform>();
    }

    void OdomCallback(OdometryMsg msg)
    {
        // Convert ROS-FLU -> Unity-FLU
        linearVel = msg.twist.twist.linear.From<FLU>();
        angularVelZ = (float)msg.twist.twist.angular.z;
    }

    void FixedUpdate()
    {
        float dt = Time.deltaTime;


        Vector3 deltaWorld = transform.rotation * linearVel * dt;
        transform.position += deltaWorld;

        float deltaYawDeg = -angularVelZ * Mathf.Rad2Deg * dt; // negate for ROS->Unity handedness
        transform.Rotate(0f, deltaYawDeg, 0f, Space.World);
    }
}


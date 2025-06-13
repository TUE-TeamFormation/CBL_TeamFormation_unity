using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

class BoundingBox
{
    public Vector2 Min, Max;

    public BoundingBox(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }
}

class DetectedObjectList
{
    public List<BoundingBox> Objects = new List<BoundingBox>();

    // Translate data from python class to C# class
    public DetectedObjectList(float[] data)
    {
        if (data.Length % 4 != 0) return;

        for (int i = 0; i < data.Length; i = i + 4)
        {
            Objects.Add(new BoundingBox(
                new Vector2(data[i],     data[i + 1]),
                new Vector2(data[i + 2], data[i + 3])
            ));
        }
    }
}

public class ObjectDetectionFramework : MonoBehaviour
{
    // Optional layer mask so you can limit what counts as “clickable”
    [SerializeField] private LayerMask pickableLayers = ~0;   // “~0” = Everything

    [SerializeField] string detectedObjectsTopic = "/detected_objects_boxes";
    ROSConnection ros;


    // Start is called before the first frame update
    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<Float32MultiArrayMsg>(detectedObjectsTopic, DetectedObjectsCallback);
    }

    void DetectedObjectsCallback(Float32MultiArrayMsg msg)
    {
        float[] copy = (float[])msg.data.Clone();

        DetectedObjectList detectedObjectList = new DetectedObjectList(copy);
        foreach (BoundingBox bb in detectedObjectList.Objects)
        {
            Vector2 centerOfBox = bb.Min + ((bb.Max - bb.Min) / 2);

            //Debug.Log($"CENTER: {centerOfBox.x}, {centerOfBox.y}");
            //Debug.Log($"MIN: {bb.Min.x}, {bb.Min.y}");
            //Debug.Log($"MAX: {bb.Max.x}, {bb.Max.y}");

            // Get the object using raycasting
            GameObject detectedObject = GetEntityFromScreenCoord(centerOfBox);

            if (detectedObject != null)
            {
                // Insert into DB
            }
        }
        //Debug.Log($"===========================");
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            GameObject clickedObject = GetEntityFromScreenCoord(Input.mousePosition);
            Transform tr = clickedObject.transform;
            Debug.Log($"Clicked {clickedObject.name} at world pos {tr.position}");
        }
    }

    GameObject GetEntityFromScreenCoord(Vector2 inputScreenPosition)
    {
        Camera cam = GetComponent<Camera>();
        Ray ray = cam.ScreenPointToRay(inputScreenPosition);

        if (Physics.Raycast(ray, out RaycastHit hitInfo, Mathf.Infinity, pickableLayers))
        {
            GameObject detectedObject = hitInfo.collider.gameObject;
            return detectedObject;
        }

        return null;
    }
}

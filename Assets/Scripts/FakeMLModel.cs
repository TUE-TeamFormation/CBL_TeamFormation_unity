using Newtonsoft.Json;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Robotics.Core;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public class FakeMLModel : MonoBehaviour
{

    [SerializeField]
    Clock.ClockMode m_ClockMode;

    [SerializeField, HideInInspector]
    Clock.ClockMode m_LastSetClockMode;

    [SerializeField]
    GameObject turtleBotBaseFootprint;

    [SerializeField]
    GameObject[] wallsX;

    [SerializeField]
    GameObject[] wallsZ;

    [SerializeField]
    GameObject[] trackedWalls;

    [SerializeField]
    GameObject[] trackedTrashEntities;

    [SerializeField]
    GameObject[] trackedNonTrashEntities;

    [SerializeField]
    double m_PublishRateHz = 1f;

    [SerializeField] 
    string detectedObjectsTopic = "/detected_objects_boxes";

    double m_LastPublishTimeSeconds;
    double PublishPeriodSeconds => 1.0f / m_PublishRateHz;
    bool ShouldPublishMessage => Clock.FrameStartTimeInSeconds - PublishPeriodSeconds > m_LastPublishTimeSeconds;

    ROSConnection m_ROS;

    DetectedObjectList objectList = new DetectedObjectList();

    void OnValidate()
    {
        var clocks = FindObjectsOfType<FakeMLModel>();
        if (clocks.Length > 1)
        {
            Debug.LogWarning("Found too many fake ml models in the scene, there should only be one!");
        }

        if (Application.isPlaying && m_LastSetClockMode != m_ClockMode)
        {
            Debug.LogWarning("Can't change ClockMode during simulation! Setting it back...");
            m_ClockMode = m_LastSetClockMode;
        }
        
        SetClockMode(m_ClockMode);
    }

    void SetClockMode(Clock.ClockMode mode)
    {
        Clock.Mode = mode;
        m_LastSetClockMode = mode;
    }

    // Start is called before the first frame update
    void Start()
    {
        SetClockMode(m_ClockMode);
        m_LastPublishTimeSeconds = Clock.time;

        m_ROS = ROSConnection.GetOrCreateInstance();
        m_ROS.RegisterPublisher<Float32MultiArrayMsg>(detectedObjectsTopic);
    }

    void Update()
    {
        if (ShouldPublishMessage)
        {
            m_LastPublishTimeSeconds = Clock.time;
            GenerateEntry();
            // Send the message to the ROS network
            m_ROS.Publish(detectedObjectsTopic, new Float32MultiArrayMsg(new MultiArrayLayoutMsg(), objectList.ToFloatArray()));
        }
    }

    Bounds GetBoundsForObject(GameObject obj)
    {
        if (obj != turtleBotBaseFootprint)
            return obj.GetComponent<Collider>().bounds;

        Bounds combinedBounds = new Bounds(obj.transform.position, Vector3.zero);
        combinedBounds.Expand(0.14f);

        return combinedBounds;
    }

    void GenerateEntry()
    {
        // Do the work
        // First determine exterior wall bounds
        SegmentProfiler.Start("Randomizing positions");

        float minX = Math.Min(wallsX[0].transform.position.x, wallsX[1].transform.position.x);
        float minZ = Math.Min(wallsZ[0].transform.position.z, wallsZ[1].transform.position.z);
        float maxX = Math.Max(wallsX[0].transform.position.x, wallsX[1].transform.position.x);
        float maxZ = Math.Max(wallsZ[0].transform.position.z, wallsZ[1].transform.position.z);

        // Try scatter objects
        System.Random r = new System.Random();
        List<GameObject> existingObjects = new List<GameObject>();
        existingObjects.AddRange(trackedWalls);
        existingObjects.AddRange(wallsX);
        existingObjects.AddRange(wallsZ);

        // Raycasting time!
        // First fetch the camera and set texture for Unity to reference
        SegmentProfiler.Start("Generating bounding boxes");
        Camera camera = GetComponent<Camera>();
        RenderTexture oldActive = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        objectList.Objects.Clear();

        foreach (GameObject trackedEntity in trackedTrashEntities.Union(trackedNonTrashEntities))
        {
            // Get position of entity in world space
            Bounds bounds = trackedEntity.GetComponent<Collider>().bounds;

            ///region Raycasting to see if wall is in front of the entity
            // Calculate the world center of the bounding box
            Vector3 worldCenter = bounds.center;

            // Raycast from the camera to the entity's center
            Vector3 rayOrigin = camera.transform.position;
            Vector3 direction = worldCenter - rayOrigin;

            Ray ray = new Ray(rayOrigin, direction);
            RaycastHit[] hits = Physics.RaycastAll(ray, direction.magnitude);

            // Sort hits by distance
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // Check if a wall is hit before the entity
            bool occludedByWall = false;
            foreach (var hit in hits)
            {
                GameObject hitObject = hit.collider.gameObject;

                if (trackedWalls.Contains(hitObject))
                {
                    occludedByWall = true;
                    break; // Wall is in front — skip this object
                }

                if (hitObject == trackedEntity)
                {
                    break; // Reached entity without hitting a wall
                }
            }

            if (occludedByWall)
            {
                //Debug.Log("Entity is occluded by a wall.");
                continue; // Skip this entity
            }
            ///endregion

            Vector3 minBounds = bounds.min;
            Vector3 maxBounds = bounds.max;

            Vector3[] worldCorners = new Vector3[]
            {
                new Vector3(minBounds.x, minBounds.y, minBounds.z),
                new Vector3(maxBounds.x, minBounds.y, minBounds.z),
                new Vector3(minBounds.x, maxBounds.y, minBounds.z),
                new Vector3(maxBounds.x, maxBounds.y, minBounds.z),
                new Vector3(minBounds.x, minBounds.y, maxBounds.z),
                new Vector3(maxBounds.x, minBounds.y, maxBounds.z),
                new Vector3(minBounds.x, maxBounds.y, maxBounds.z),
                new Vector3(maxBounds.x, maxBounds.y, maxBounds.z),
            };

            // Convert those coordinates into screen space
            // and check if they are in front or behind camera
            Vector3[] screenCorners = worldCorners
                .Select(v => camera.WorldToScreenPoint(v))
                .Where(v => v.z > 0)
                .ToArray();

            if (screenCorners.Length == 0)
            {
                //Debug.Log("Object is completely behind camera.");
                continue;
            }

            // Get the furthest endpoints of the object in screen space
            Vector2 min = screenCorners[0];
            Vector2 max = screenCorners[0];
            foreach (Vector3 p in screenCorners)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            // Create the bounding box in screen space (before clamping)
            Rect rawBoundingBox = Rect.MinMaxRect(min.x, min.y, max.x, max.y);

            // Check if it's outside the screen completely
            Rect screenRect = new Rect(0, 0, camera.targetTexture.width, camera.targetTexture.height);

            if (!rawBoundingBox.Overlaps(screenRect))
            {
                //Debug.Log("Object is completely outside of camera view.");
                continue;
            }

            // Clamp to screen edges
            float clampedXMin = Mathf.Clamp(rawBoundingBox.xMin, 0, screenRect.width);
            float clampedYMin = Mathf.Clamp(rawBoundingBox.yMin, 0, screenRect.height);
            float clampedXMax = Mathf.Clamp(rawBoundingBox.xMax, 0, screenRect.width);
            float clampedYMax = Mathf.Clamp(rawBoundingBox.yMax, 0, screenRect.height);
            Rect screenBoundingBox = Rect.MinMaxRect(clampedXMin, clampedYMin, clampedXMax, clampedYMax);

            // Image is flipped vertically
            float height = screenBoundingBox.height;
            screenBoundingBox.y = camera.targetTexture.height - screenBoundingBox.y - screenBoundingBox.height;
            screenBoundingBox.height = height;
            if (trackedTrashEntities.Contains(trackedEntity))
                objectList.Objects.Add(new BoundingBox(screenBoundingBox.min, screenBoundingBox.max));
        }

        // Reset active texture
        RenderTexture.active = oldActive;
        SegmentProfiler.End();
    }


    void OnGUI()
    {
        GUI.color = Color.white;
        Texture texture = GetComponent<Camera>().targetTexture;

        float texWidth = texture.width;
        float texHeight = texture.height;

        // Offset to draw in bottom-right corner
        float xOffset = Screen.width - texWidth;
        float yOffset = Screen.height - texHeight;

        // Draw the texture in bottom-right
        GUI.DrawTexture(new Rect(xOffset, yOffset, texWidth, texHeight), texture);

        // Visualize bounding boxes on top
        GUI.color = Color.green;
        foreach (BoundingBox bb in objectList.Objects)
        {
            float boxX = xOffset + bb.Min.x;
            float boxY = yOffset + bb.Min.y;
            float boxWidth = bb.Max.x - bb.Min.x;
            float boxHeight = bb.Max.y - bb.Min.y;

            GUI.Box(new Rect(boxX, boxY, boxWidth, boxHeight), "TRASH");
        }
    }

    void DrawBounds(Bounds b, Color color, float delay = 0)
    {
        // bottom
        var p1 = new Vector3(b.min.x, b.min.y, b.min.z);
        var p2 = new Vector3(b.max.x, b.min.y, b.min.z);
        var p3 = new Vector3(b.max.x, b.min.y, b.max.z);
        var p4 = new Vector3(b.min.x, b.min.y, b.max.z);

        Debug.DrawLine(p1, p2, color, delay);
        Debug.DrawLine(p2, p3, color, delay);
        Debug.DrawLine(p3, p4, color, delay);
        Debug.DrawLine(p4, p1, color, delay);

        // top
        var p5 = new Vector3(b.min.x, b.max.y, b.min.z);
        var p6 = new Vector3(b.max.x, b.max.y, b.min.z);
        var p7 = new Vector3(b.max.x, b.max.y, b.max.z);
        var p8 = new Vector3(b.min.x, b.max.y, b.max.z);

        Debug.DrawLine(p5, p6, color, delay);
        Debug.DrawLine(p6, p7, color, delay);
        Debug.DrawLine(p7, p8, color, delay);
        Debug.DrawLine(p8, p5, color, delay);

        // sides
        Debug.DrawLine(p1, p5, color, delay);
        Debug.DrawLine(p2, p6, color, delay);
        Debug.DrawLine(p3, p7, color, delay);
        Debug.DrawLine(p4, p8, color, delay);
    }

    private void OnDrawGizmos()
    {
        // Draws boxes around the relevant objects for this script
        foreach (GameObject obj in trackedTrashEntities.Union(trackedNonTrashEntities))
            DrawBounds(obj.GetComponent<Collider>().bounds, Color.magenta);
        DrawBounds(GetBoundsForObject(turtleBotBaseFootprint), Color.green);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Robotics.Core;

using Newtonsoft.Json;
using System.IO;

public class ModelDataGenerator : MonoBehaviour
{
    public class SerializableBB
    {
        [JsonProperty(PropertyName = "class")]
        public string classification;
        public int[] bbox;

        [JsonIgnore]
        public int x => bbox[0];
        [JsonIgnore]
        public int y => bbox[1];
        [JsonIgnore]
        public int width => bbox[2] - bbox[0];
        [JsonIgnore]
        public int height => bbox[3] - bbox[1];

        public static SerializableBB FromRectAndLabel(Rect rect, string label)
        {
            return new SerializableBB()
            {
                bbox = new int[]
                {
                    (int)rect.xMin,
                    (int)rect.yMin,
                    (int)rect.xMax,
                    (int)rect.yMax,
                },
                classification = label
            };
        }
    }

    public class ImageEntry
    {
        //public string file;
        public long offset;
        public long size;
        public List<SerializableBB> objects;
    }

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

    // Mode 1: 'Headless', generates headlessCount entries as fast as possible
    //         Temporarily disables a lot of the Unity engine components
    // DO NOT RUN IN UNITY EDITOR, ONLY FOR BUILT VERSION OF PROJECT
    // Instead, build the project to Windows .exe or Mac .app, and run this:
    // macOS: ./Model_gen.app/Contents/MacOS/My\ project -batchmode -logFile -
    // Windows: whateverMainExeIs.exe -batchmode -logFile output.txt
    // Note that for Windows you cannot output logs to stdout, only to a file
    [SerializeField]
    bool runHeadless;

    [SerializeField]
    int headlessCount;

    // Mode 2: 'Headed', generates an image each time 'G' is pressed (interval of m_PublishRateHz)
    // Intended for testing in Unity editor. Also draws the image + the boxes on screen
    [SerializeField]
    double m_PublishRateHz = 4f;

    int iterationCount = 0;
    string outputDir = "/Volumes/Untitled/ml_training/images/";
    FileStream imageFile;

    List<SerializableBB> boundingBoxes = new List<SerializableBB>();
    List<ImageEntry> entries = new List<ImageEntry>();

    double m_LastPublishTimeSeconds;
    double PublishPeriodSeconds => 1.0f / m_PublishRateHz;
    bool ShouldPublishMessage => Clock.FrameStartTimeInSeconds - PublishPeriodSeconds > m_LastPublishTimeSeconds;

    void OnValidate()
    {
        var clocks = FindObjectsOfType<ModelDataGenerator>();
        if (clocks.Length > 1)
        {
            Debug.LogWarning("Found too many training data generators in the scene, there should only be one!");
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
        if (runHeadless)
        {
            Directory.CreateDirectory(outputDir);

            GetComponent<Camera>().enabled = false;
            Time.fixedDeltaTime = Mathf.Infinity;
            Application.targetFrameRate = -1; // No limit
            QualitySettings.vSyncCount = 0;   // Disable vSync

            imageFile = File.Create(Path.Combine(outputDir, "data.bin"));
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

        // This doesn't fully work
        // It can still place objects that overlap because the Colliders aren't updated
        // Syncing the physics engine each iteration could work, but is very expensive
        foreach (GameObject gameObj in trackedTrashEntities.Union(trackedNonTrashEntities).Append(turtleBotBaseFootprint))
        {
            // Try random places until it doesn't collide
            bool validPosition = false;
            int tries = 0;
            while (validPosition == false && tries < 10000)
            {
                float newX = (float)r.NextDouble() * (maxX - minX) + minX;
                float newZ = (float)r.NextDouble() * (maxZ - minZ) + minZ;

                Bounds newBounds = GetBoundsForObject(gameObj);
                float newCenterX = newX + newBounds.extents.x;
                float newCenterZ = newZ + newBounds.extents.z;

                Vector3 newCenter = newBounds.center;
                newCenter.x = newCenterX;
                newCenter.z = newCenterZ;
                newBounds.center = newCenter;

                validPosition = true;
                foreach (GameObject existingObj in existingObjects)
                {
                    if (GetBoundsForObject(existingObj).Intersects(newBounds))
                    {
                        validPosition = false;
                        break;
                    }
                }

                if (validPosition)
                {
                    Vector3 transform = gameObj.transform.position;
                    transform.x = newX;
                    transform.z = newZ;
                    gameObj.transform.position = transform;

                    // We need to rotate the camera in different angles
                    // Also: maybe adjust this to favor center
                    if (gameObj == turtleBotBaseFootprint)
                    {
                        Quaternion rotation = gameObj.transform.rotation;
                        Vector3 angles = rotation.eulerAngles;
                        angles.y = (float)(r.NextDouble() * 360);
                        rotation.eulerAngles = angles;
                        gameObj.transform.rotation = rotation;
                    }
                }

                tries++;
            }

            // Rare, but after 10k tries it is best to restart.
            if (!validPosition)
            {
                Debug.Log("UNABLE TO FIND POSITION");
                SegmentProfiler.End();
                return;
            }

            existingObjects.Add(gameObj);
        }

        // Raycasting time!
        // First fetch the camera and set texture for Unity to reference
        SegmentProfiler.Start("Generating bounding boxes");
        Camera camera = GetComponent<Camera>();
        RenderTexture oldActive = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        boundingBoxes.Clear();

        // Very important: update colliders!
        // This is necessary because transforms were adjusted further up in this function
        Physics.SyncTransforms();

        foreach (GameObject trackedEntity in trackedTrashEntities.Union(trackedNonTrashEntities))
        {
            // Get position of entity in world space
            Bounds bounds = trackedEntity.GetComponent<Collider>().bounds;

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
            screenBoundingBox.y = camera.activeTexture.height - screenBoundingBox.y - screenBoundingBox.height;
            screenBoundingBox.height = height;
            boundingBoxes.Add(SerializableBB.FromRectAndLabel(screenBoundingBox,
                trackedTrashEntities.Contains(trackedEntity) ? "trash" : "not trash"));
        }

        // Image is automatically rendered in headed mode
        // It also doesn't need to be saved
        if (runHeadless)
        {
            // Render the actual camera image on the GPU
            SegmentProfiler.Start("Rendering camera texture");
            camera.Render();

            // Copy the image from the GPU buffers into a texture in CPU memory
            SegmentProfiler.Start("Blitting texture data GPU -> CPU");
            Texture2D tex = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, camera.targetTexture.width, camera.targetTexture.height), 0, 0);
            tex.Apply();

            // Write the file
            SegmentProfiler.Start("Copying texture to byte array");
            byte[] texData = tex.GetRawTextureData();
            SegmentProfiler.Start("Starting disk write");
            imageFile.WriteAsync(texData);

            SegmentProfiler.Start("Creating data entries");
            ImageEntry entry = new ImageEntry()
            {
                offset = imageFile.Position,
                size = texData.Length,
                objects = boundingBoxes.ToList()
            };
            entries.Add(entry);

            iterationCount++;
            if (iterationCount == headlessCount)
            {
                SegmentProfiler.Start("Writing entry table to JSON file");
                JsonSerializer serializer = new JsonSerializer();
                serializer.Formatting = Formatting.Indented;
                StreamWriter writer = File.CreateText(Path.Combine(outputDir, "data.json"));
                serializer.Serialize(writer, entries);
                writer.Close();
            }
        }

        // Reset active texture
        RenderTexture.active = oldActive;
        SegmentProfiler.End();
    }

    void Update()
    {
        if (runHeadless && iterationCount < headlessCount)
        {
            // Show progress report every 5%
            DateTime startTime = DateTime.Now;
            int steps = headlessCount / 20;
            while (iterationCount < headlessCount)
            {
                if (iterationCount % steps == 0)
                {
                    Debug.Log($"Status: {iterationCount * 100 / headlessCount}%");
                }
                GenerateEntry();
            }
            SegmentProfiler.Start("Flushing data to disk");
            imageFile.Flush();
            imageFile.Close();

            SegmentProfiler.End();
            Debug.Log("Done in: " + (DateTime.Now - startTime).TotalSeconds);
            SegmentProfiler.PrintReport();
        }

        if (!runHeadless && ShouldPublishMessage && Input.GetKey(KeyCode.G))
        {
            m_LastPublishTimeSeconds = Clock.time;
            GenerateEntry();
        }
    }


    void OnGUI()
    {
        if (runHeadless) return;

        // Render the generated image to the screen
        GUI.color = Color.white;
        Texture texture = GetComponent<Camera>().targetTexture;
        GUI.DrawTexture(new Rect(0, 0, texture.width, texture.height), texture);

        // Visualize the bounding boxes on top of the rendered texture
        GUI.color = Color.green;
        foreach (SerializableBB bb in boundingBoxes)
        {
            GUI.Box(new Rect(bb.x, bb.y, bb.width, bb.height), bb.classification);
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
        if (runHeadless) return;

        // Draws boxes around the relevant objects for this script
        foreach (GameObject obj in trackedTrashEntities.Union(trackedNonTrashEntities))
            DrawBounds(obj.GetComponent<Collider>().bounds, Color.magenta);
        DrawBounds(GetBoundsForObject(turtleBotBaseFootprint), Color.green);
    }
}

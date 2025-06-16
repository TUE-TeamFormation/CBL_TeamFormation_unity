using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using CBLSqlConnectionWrapper;
using Codice.Client.BaseCommands;

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

    public DetectedObjectList()
    {

    }

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

    public float[] ToFloatArray()
    {
        float[] data = new float[Objects.Count * 4];
        for (int i = 0; i < Objects.Count; i++)
        {
            BoundingBox bb = Objects[i];
            data[i * 4]     = bb.Min.x;
            data[i * 4 + 1] = bb.Min.y;
            data[i * 4 + 2] = bb.Max.x;
            data[i * 4 + 3] = bb.Max.y;
        }
        return data;
    }
}

public class ObjectDetectionFramework : MonoBehaviour
{
    // Optional layer mask so you can limit what counts as “clickable”
    [SerializeField] private LayerMask pickableLayers = ~0;   // “~0” = Everything

    [SerializeField] string detectedObjectsTopic = "/detected_objects_boxes";
    ROSConnection ros;

    [SerializeField] public Camera cameraMachineLearning = null;


    private Dictionary<int, TrashEntry> TrashEntriesDB = new Dictionary<int, TrashEntry>();
    private int LatestTrashID = 0;


    // Start is called before the first frame update
    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<Float32MultiArrayMsg>(detectedObjectsTopic, DetectedObjectsCallback);

        // TODO: Technically we should not reset the table each time we start the app
        //       since the trash must be just stored and later to be picked.
        ResetTable();
    }

    void DetectedObjectsCallback(Float32MultiArrayMsg msg)
    {
        float[] copy = (float[])msg.data.Clone();

        List<GameObject> detectedGameObjects = new List<GameObject>();

        DetectedObjectList detectedObjectList = new DetectedObjectList(copy);
        foreach (BoundingBox bb in detectedObjectList.Objects)
        {
            float minY = cameraMachineLearning.pixelHeight - bb.Max.y;
            float maxY = cameraMachineLearning.pixelHeight - bb.Min.y;
            bb.Min.y = minY;
            bb.Max.y = maxY;

            Vector2 centerOfBox = bb.Min + ((bb.Max - bb.Min) / 2);

            //Debug.Log($"CENTER: {centerOfBox.x}, {centerOfBox.y}");
            //Debug.Log($"MIN: {bb.Min.x}, {bb.Min.y}");
            //Debug.Log($"MAX: {bb.Max.x}, {bb.Max.y}");

            // Get the object using raycasting
            GameObject detectedGameObject = GetEntityFromScreenCoord(centerOfBox);

            if (detectedGameObject != null)
            {
                if(detectedGameObject.name.Contains("trash"))
                {
                    detectedGameObjects.Add(detectedGameObject);
                }
            }
        }

        // Insert all the detected objects in the DB
        // TODO: Uncomment this when the ML is ready
        CheckAndInsertTrashData(detectedGameObjects);
    }

    private void InsertToLocalDB(TrashEntry trashEntry)
    {
        TrashEntriesDB.Add(LatestTrashID, trashEntry);
        LatestTrashID++;
    }

    private int GetCountInRadius(TrashEntry trashEntry, double radius)
    {
        int entitiesInRadius = 0;
        
        foreach (var kv in TrashEntriesDB)
        {
            //Vector3 trashEntryPos = new Vector3((float)kv.Value.X, (float)kv.Value.Y, (float)kv.Value.Z);
            double range = Math.Sqrt(
                Math.Pow(trashEntry.X - kv.Value.X, 2) +
                Math.Pow(trashEntry.Y - kv.Value.Y, 2) +
                Math.Pow(trashEntry.Z - kv.Value.Z, 2)
            );

            if (range <= radius)
            {
                entitiesInRadius++;
            }
        }

        return entitiesInRadius;
    }

    async void CheckAndInsertTrashData(List<GameObject> detectedObjects)
    {
        SqlConnectionWrapper connection = new SqlConnectionWrapper();

        // Open SQL connection (not necessary, automatic)
        await connection.OpenConnectionAsync();

        foreach (GameObject detectedObject in detectedObjects)
        {
            Vector3 detectedObjectPos = detectedObject.transform.position;

            TrashEntry detectedTrashEntry = new TrashEntry() {
                X = (double) detectedObjectPos.x,
                Y = (double) detectedObjectPos.y,
                Z = (double) detectedObjectPos.z,
                DetectionTime = Time.realtimeSinceStartup,
                TimeStamp = DateTime.Now
            };

            
            //int entitiesInRadius = await connection.GetCountInRadiusAsync(detectedTrashEntry, 0.1);
            
            // Waiting for the DB is too slow, so instead we are going to use a local DB with the current made changes
            int entitiesInRadius = GetCountInRadius(detectedTrashEntry, 0.1);

            if (entitiesInRadius == 0)
            {
                InsertToLocalDB(detectedTrashEntry);
                await connection.InsertEntriesAsync(detectedTrashEntry);

                List<TrashEntry> entries = await connection.GetAllEntries();
                Debug.Log("ID  |X   |Y   |Z   |DT     |Timestamp");
                foreach (var entry in entries)
                {
                    //Debug.Log(string.Format("{0}|{1}|{2}|{3}|{4}|{5}",
                    //    entry.ID.ToString().PadRight(4),
                    //    entry.X.ToString("f1").PadLeft(4),
                    //    entry.Y.ToString("f1").PadLeft(4),
                    //    entry.Z.ToString("f1").PadLeft(4),
                    //    entry.X.ToString("f1").PadLeft(7),
                    //    entry.TimeStamp.ToString()));
                    Debug.Log($"{entry.ID}, {entry.X}, {entry.Y}, {entry.Z}, {entry.DetectionTime}, {entry.TimeStamp.ToString()}");
                }
            }

            
        }

        // Close SQL connection (not necessary, automatic)
        connection.CloseConnection();
    }

    async void ResetTable()
    {
        SqlConnectionWrapper connection = new SqlConnectionWrapper();
        await connection.ResetTable();
        connection.CloseConnection();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            //GameObject clickedObject = GetEntityFromScreenCoord(Input.mousePosition);
            //Transform tr = clickedObject.transform;
            //Debug.Log($"Clicked {clickedObject.name} at world pos {tr.position}");


            //Debug.Log($"{Input.mousePosition.x}, {Input.mousePosition.y}");

            //List<GameObject> testList = new List<GameObject>();
            //testList.Add(clickedObject);

            // TODO: Comment this when doing ML
            //CheckAndInsertTrashData(testList);
        }
    }

    GameObject GetEntityFromScreenCoord(Vector2 inputScreenPosition)
    {
        //Camera cam = GetComponent<Camera>();
        Ray ray = cameraMachineLearning.ScreenPointToRay(inputScreenPosition);

        if (Physics.Raycast(ray, out RaycastHit hitInfo, Mathf.Infinity, pickableLayers))
        {
            GameObject detectedObject = hitInfo.collider.gameObject;
            return detectedObject;
        }

        return null;
    }
}

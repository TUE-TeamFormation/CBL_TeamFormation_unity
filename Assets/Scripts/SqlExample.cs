using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CBLSqlConnectionWrapper;
using System.Threading.Tasks;
using System;

public class SqlExample : MonoBehaviour
{
    SqlConnectionWrapper wrapper;

    bool ranAlready = false;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    async void Update()
    {
        if (!ranAlready)
        {
            ranAlready = true;

            wrapper = new SqlConnectionWrapper();
            // Open SQL connection (not necessary, automatic)
            await wrapper.OpenConnectionAsync();

            // Reset the table 
            await wrapper.ResetTable();

            // Add new entries
            TrashEntry[] newEntries =
            {
                new TrashEntry() { X = 1, Y = 2, Z = 3, DetectionTime = 69, TimeStamp = DateTime.Now + TimeSpan.FromDays(1)},
                new TrashEntry() { X = 2, Y = 3, Z = 4, DetectionTime = 690, TimeStamp = DateTime.Now + TimeSpan.FromDays(2)},
                new TrashEntry() { X = 3, Y = 4, Z = 5, DetectionTime = 6900, TimeStamp = DateTime.Now + TimeSpan.FromDays(3)},
                new TrashEntry() { X = 4, Y = 5, Z = 6, DetectionTime = 69000, TimeStamp = DateTime.Now + TimeSpan.FromDays(4)},
            };

            await wrapper.InsertEntriesAsync(newEntries);

            // Retrieve all entries
            Task<List<TrashEntry>> entryTask = wrapper.GetAllEntries();
            List<TrashEntry> entries = await entryTask;
            Debug.Log("ID  |X   |Y   |Z   |DT     |Timestamp");
            foreach (var entry in entries)
            {
                Debug.Log(string.Format("{0}|{1}|{2}|{3}|{4}|{5}",
                    entry.ID.ToString().PadRight(4),
                    entry.X.ToString("f1").PadLeft(4),
                    entry.Y.ToString("f1").PadLeft(4),
                    entry.Z.ToString("f1").PadLeft(4),
                    entry.X.ToString("f1").PadLeft(7),
                    entry.TimeStamp.ToString()));
            }

            // Close SQL connection (not necessary, automatic)
            wrapper.CloseConnection();
        }
    }
}

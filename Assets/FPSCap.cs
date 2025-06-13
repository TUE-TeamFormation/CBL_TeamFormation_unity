using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class FPSCap : MonoBehaviour
{
    [SerializeField] private int frameRate = 60;

    // Start is called before the first frame update
    void Start()
    {
#if UNITY_EDITOR
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = frameRate;
#endif
    }

    // Update is called once per frame
    void Update()
    {
        return;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPicker : MonoBehaviour
{
    // Optional layer mask so you can limit what counts as “clickable”
    [SerializeField] private LayerMask pickableLayers = ~0;   // “~0” = Everything


    // Start is called before the first frame update
    void Start()
    {
        
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

    GameObject GetEntityFromScreenCoord(Vector3 inputScreenPosition)
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

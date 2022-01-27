using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace andywiecko.Flocking
{
    public class ControlTarget : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetMouseButton(0))
            {
                var mousePos = Input.mousePosition;
                var eventSystem = EventSystem.current;
                var data = new PointerEventData(eventSystem);
                data.position = mousePos;
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(data, results);

                foreach (var result in results)
                {
                    if (result.gameObject.layer == 5) // UI
                    {
                        return;
                    }
                }

                float3 pxyz = Camera.main.ScreenPointToRay(mousePos).origin;
                pxyz.z = 0;
                transform.position = pxyz;
            }
        }
    }
}
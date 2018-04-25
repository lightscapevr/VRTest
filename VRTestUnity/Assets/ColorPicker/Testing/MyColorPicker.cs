using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BaroqueUI;


public class MyColorPicker : MonoBehaviour
{
    public VRColorPicker vrColorPicker;


    bool trigger_down;

    private void Start()
    {
        var ht = Controller.HoverTracker(this);
        ht.onControllersUpdate += Ht_onControllersUpdate;
        ht.onLeave += (ctrl) => { vrColorPicker.MouseOver(new Vector3[0]); };
        ht.onTriggerDown += (ctrl) => { trigger_down = true; };
        ht.onTriggerDrag += (ctrl) => { vrColorPicker.MouseDrag(ctrl.position); };
        ht.onTriggerUp += (ctrl) => { trigger_down = false; vrColorPicker.MouseRelease(); };
    }

    private void Ht_onControllersUpdate(Controller[] controllers)
    {
        if (!trigger_down)
            vrColorPicker.MouseOver(controllers.Select(ctrl => ctrl.position));
    }
}

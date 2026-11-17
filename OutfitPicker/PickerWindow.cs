using System;
using UnityEngine;

namespace OutfitPicker;

// Deliberately tiny: an IL2CPP-injected class should only expose Unity messages.
// Everything else (state, logging, error handling) lives in the static PickerUi.
public class PickerWindow : MonoBehaviour
{
    public PickerWindow(IntPtr ptr) : base(ptr) { }

    public void Update() => PickerUi.SafeUpdate();

    public void OnGUI() => PickerUi.SafeDraw();
}

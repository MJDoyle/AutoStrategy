using UnityEngine;

[CreateAssetMenu(menuName = "AutoStrategy/Fog Settings", fileName = "FogSettings")]
public class FogSettings : ScriptableObject
{
    [Tooltip("Fog circle centre in tile cx/cy space. (0,0) = use ClearingCenter() automatically.")]
    public Vector2 fogCenterOverride = Vector2.zero;
}

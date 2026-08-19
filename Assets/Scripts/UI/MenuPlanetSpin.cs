#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    // Very slow axial rotation for the menu planet.
    //
    // Deliberately slow enough that it is not read as motion while looking at
    // it - only as the scene being alive when you glance back a minute later.
    // Anything faster turns a planet into a spinning prop and gives away the
    // scale, because a body this size cannot visibly turn in seconds.
    //
    // Play-mode only, and pointedly NOT [ExecuteAlways]: rotating in the editor
    // would drift the pose BuildMenuSceneryRig sets and leave the scene
    // permanently dirty, so the rig would stop being reproducible.
    public sealed class MenuPlanetSpin : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Degrees per second around the planet's own axis.")]
        private float _degreesPerSecond = 0.35f;

        // Rotation is applied around the body's local up, so it follows the
        // axial tilt the rig sets rather than the world axis - a planet turning
        // about anything other than its own axis reads as wrong immediately.
        private void Update()
        {
            if (Mathf.Approximately(_degreesPerSecond, 0f))
            {
                return;
            }

            transform.Rotate(Vector3.up, _degreesPerSecond * Time.unscaledDeltaTime, Space.Self);
        }
    }
}

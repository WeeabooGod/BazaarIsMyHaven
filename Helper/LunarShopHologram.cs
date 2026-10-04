using RoR2.Hologram;
using UnityEngine;

namespace BazaarIsMyHaven
{
    // Local presentation only. PurchaseInteraction already supplies the networked price and availability.
    public class LunarShopHologram : MonoBehaviour
    {
        // Adjust this offset after finding a suitable local position with the in-game debug tools.
        private static readonly Vector3 HologramLocalPosition = new Vector3(-0.175f, 1f, 1f);
        private static readonly Vector3 HologramLocalEulerAngles = new Vector3(0f, 180f, 180f);
        private const string PivotName = "BazaarCostHologramPivot";

        public static void AddTo(GameObject terminal)
        {
            // Scale updates can be retried; do not create duplicate projectors or pivots.
            if (!terminal.GetComponent<LunarShopHologram>())
            {
                terminal.AddComponent<LunarShopHologram>();
            }
        }

        private void Awake()
        {
            var pivot = new GameObject(PivotName).transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = HologramLocalPosition;
            pivot.localRotation = Quaternion.Euler(HologramLocalEulerAngles);

            var projector = GetComponent<HologramProjector>();
            if (!projector)
            {
                projector = gameObject.AddComponent<HologramProjector>();
            }
            else
            {
                // Rebuild any existing display beneath our editable attachment point.
                projector.DestroyHologram();
            }

            // The projector uses this terminal's PurchaseInteraction to create the game's cost hologram.
            projector.hologramPivot = pivot;

            // Match lunar buds: keep the display fixed instead of turning it toward the viewer.
            projector.disableHologramRotation = true;
            projector.enabled = true;
        }
    }
}

using Fodinae.Scripts.UI.HUD.Inventory.View;
using UnityEngine;

namespace Fodinae.Scripts.UI.HUD.Inventory.Presenter
{
    [RequireComponent(typeof(InventoryView))]
    public class InventoryPresenter : MonoBehaviour
    {
        private InventoryView _view;

        private void Start()
        {
            _view = GetComponent<InventoryView>();
        }
    }
}

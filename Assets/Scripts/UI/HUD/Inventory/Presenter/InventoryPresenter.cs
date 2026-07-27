using Fodinae.Scripts.UI.HUD.Inventory.Interfaces;
using Fodinae.Scripts.UI.HUD.Inventory.Model;
using Fodinae.Scripts.UI.HUD.Inventory.View;
using UnityEngine;
using VContainer;

namespace Fodinae.Scripts.UI.HUD.Inventory.Presenter
{
    [RequireComponent(typeof(InventoryView))]
    public class InventoryPresenter : MonoBehaviour
    {
        private InventoryView _view;
        [Inject]
        private IInventoryModel _model = null!;

        private void Start()
        {
            _view = GetComponent<InventoryView>();
        }
    }
}

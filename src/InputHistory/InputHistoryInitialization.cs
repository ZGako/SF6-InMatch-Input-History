using SF6_Plugin_Core.UI;

namespace SF6_MIH.InputHistory;

partial class InputHistoryClass : IDisposable
{
    private InputHistoryController? _controller;
    private InputHistoryView? _view;

    private ManagedObject? _gameObjectMo;

    private bool _disposed = false;

    // TODO: see if it makes sense to initialize part of the config here and/or in the constructor
    // for example: the online vs local gamemode can be determined that way.

    public InputHistoryClass()
    {
        UIPrefabFactory.CreateUIPrefab("Product/GUI/gm/ui11200/ui11254/ui11254.pfb", OnPrefabReady);
    }

    private void OnPrefabReady(ManagedObject prefabMo)
    {
        // in case the object was disposed before the prefab was ready, we need to clean up and not proceed with initialization
        if (_disposed)
        {
            via.GameObject.destroy(prefabMo.As<via.GameObject>());
            prefabMo.ReleaseIfGlobalized();
            return;
        }

        _gameObjectMo = prefabMo;

        var uiAgentComponent = _gameObjectMo.As<via.GameObject>()?.getComponent(app.UIAgent.REFType.RuntimeType.As<_System.Type>());
        var uiAgent = ManagedProxy<app.UIAgent>.Create(uiAgentComponent);

        if (uiAgent == null)
        {
            throw new InvalidOperationException("Failed to retrieve UIAgent from the instantiated prefab.");
        }

        BattleHudDispatcher.RegisterBattleHudActivationCallbacks(Show, Hide);

        _view = new InputHistoryView(this, uiAgent);
        _controller = new InputHistoryController(this);
    }

    private void Show(app.BattleHudManager _)
    {
        API.LogInfo("Showing InMatchInputHistory UI.");
    }

    private void Hide()
    {
        API.LogInfo("Hiding InMatchInputHistory UI.");
    }

    public void Dispose()
    {
        _disposed = true;
        _controller?.Dispose();
        _view?.Dispose();
        via.GameObject.destroy(_gameObjectMo?.As<via.GameObject>());
        _gameObjectMo?.ReleaseIfGlobalized();

        BattleHudDispatcher.UnregisterBattleHudCallbacks(onActivate: Show, onDeactivate: Hide);
    }
}
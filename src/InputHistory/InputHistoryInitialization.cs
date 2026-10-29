using SF6_Plugin_Core.UI.Helpers;

namespace SF6_MIH.InputHistory;

internal partial class InputHistoryClass : IDisposable
{
    private readonly bool _isDummy;
    private InputHistoryController? _controller;

    private bool _disposed;

    private ManagedObject? _gameObjectMo;
    private InputHistoryView? _view;

    // TODO: see if it makes sense to initialize part of the config here and/or in the constructor
    // for example: the online vs local gamemode can be determined that way.

    public InputHistoryClass(bool isDummy = false)
    {
        _isDummy = isDummy;
        UIPrefabLoader.CreateUIPrefab("Product/GUI/gm/ui11200/ui11254/ui11254.pfb", OnPrefabReady);
    }

    public void Dispose()
    {
        _disposed = true;
        _controller?.Dispose();
        _view?.Dispose();
        via.GameObject.destroy(_gameObjectMo?.As<via.GameObject>());
        _gameObjectMo?.ReleaseIfGlobalized();
        ModConfig.OnConfigChanged -= OnLiveConfigChanged;
    }

    public void SetPreviewForceMode(PreviewForceMode mode)
    {
        _controller?.SetPreviewForceMode(mode);
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

        var uiAgentComponent = _gameObjectMo.As<via.GameObject>()
            ?.getComponent(app.UIAgent.REFType.RuntimeType.As<_System.Type>());
        var uiAgent = ManagedProxy<app.UIAgent>.Create(uiAgentComponent) ??
                      throw new InvalidOperationException("Failed to retrieve UIAgent from the instantiated prefab.");

        _view = new InputHistoryView(this, uiAgent);
        _controller = new InputHistoryController(this, _isDummy);

        if (_isDummy)
        {
            uiAgent.Settings.Layer = app.UILayer.OnMenu;
            _controller.ActivateDummyPreview();
        }

        ModConfig.OnConfigChanged += OnLiveConfigChanged;
    }

    private void OnLiveConfigChanged(ModConfig config)
    {
        _controller?.ApplyConfig(config);
    }
}
using SF6_Plugin_Core.UI;

namespace SF6_MIH.InputHistory;

partial class InputHistoryClass : IDisposable
{
    private readonly InputHistoryController _controller;
    private readonly InputHistoryView _view;

    private readonly ManagedObject _gameObjectMo;

    public InputHistoryClass()
    {
        // Initialize the view and controller
        _gameObjectMo = UIPrefabFactory.CreateUIPrefab("Product/GUI/gm/ui11200/ui11254/ui11254.pfb");

        var uiAgentComponent = _gameObjectMo.As<via.GameObject>()?.getComponent(app.UIAgent.REFType.RuntimeType.As<_System.Type>());
        var uiAgent = ManagedProxy<app.UIAgent>.Create(uiAgentComponent);

        if (uiAgent == null)
        {
            throw new InvalidOperationException("Failed to retrieve UIAgent from the instantiated prefab.");
        }


        _view = new InputHistoryView(this, uiAgent);
        _controller = new InputHistoryController(this);

        // Additional initialization logic can go here
    }

    public void Dispose()
    {
        _controller.Dispose();
        _view.Dispose();
        via.GameObject.destroy(_gameObjectMo?.As<via.GameObject>());
    }
}
using SF6_Plugin_Core.Memory;
using SF6_Plugin_Core.UI;


namespace SF6_MIH.InputHistory;

//FIXME: refactor
partial class InputHistoryClass
{
    // write the view part of the class (maybe an internal class?)
    private class InputHistoryView : IDisposable
    {

        private class HistoryRow(via.gui.SelectItem item)
        {
            public readonly via.gui.Text FrameText = UIHelpers.GetGuiChild(item, "e_text_left").As<via.gui.Text>()
                    ?? throw new InvalidOperationException("Failed to retrieve e_text_left.");
            public readonly via.gui.Text DirectionText = UIHelpers.GetGuiChild(item, "e_text_center").As<via.gui.Text>()
                    ?? throw new InvalidOperationException("Failed to retrieve e_text_center.");
            public readonly via.gui.Text ButtonText = UIHelpers.GetGuiChild(item, "e_text_right").As<via.gui.Text>()
                    ?? throw new InvalidOperationException("Failed to retrieve e_text_right.");

            public string CachedFrame = string.Empty;
            public string CachedDirection = string.Empty;
            public string CachedButtons = string.Empty;
        }

        private class HudSidePanel
        {
            public readonly via.gui.ScrollList ScrollList;
            public readonly List<HistoryRow> Rows = [];
            public readonly List<via.gui.Texture> BackgroundElements = [];

            public readonly via.vec3 BasePosition;
            public readonly via.vec3 BaseScale;

            // -1 = Hidden, 0 = Bound to P1, 1 = Bound to P2
            public int BoundPlayerIndex { get; set; } = -1;
            public uint LastHeadVersion { get; set; } = 0;
            public uint LastShiftVersion { get; set; } = 0;

            public HudSidePanel(via.gui.ScrollList scrollList)
            {
                ScrollList = scrollList;
                BasePosition = scrollList.Position;
                BaseScale = scrollList.Scale;

                var children = UIHelpers.GetGuiChildrenOfType(scrollList, via.gui.SelectItem.REFType);
                for (int i = children.Length - 1; i >= 0; i--)
                {
                    if (children[i] is ManagedObject childMo)
                    {
                        var selectItem = childMo.As<via.gui.SelectItem>();
                        Rows.Add(new HistoryRow(selectItem));

                        // Optional: cache background Rect/Texture elements inside each SelectItem if you want to toggle them
                        var texture = UIHelpers.GetGuiChild(selectItem, "e_tex_line").As<via.gui.Texture>();
                        BackgroundElements.Add(texture);
                    }
                }
            }
        }

        private readonly EngineStringPool _engineStringPool = new();

        private readonly InputHistoryClass _parent;

        private readonly HudSidePanel _leftPanel;
        private readonly HudSidePanel _rightPanel;

        private bool _isHudVisible = false;

        public InputHistoryView(InputHistoryClass parent, app.UIAgent uiAgent)
        {
            _parent = parent;

            var partsManager = uiAgent._PartsManager;
            var left = (partsManager._List[0] as IObject)?.As<app.UIPartsScrollList>();
            var leftScrollList = left?._List ?? throw new InvalidOperationException("Failed to retrieve the left side ScrollList from the UIAgent.");

            var right = (partsManager._List[1] as IObject)?.As<app.UIPartsScrollList>();
            var rightScrollList = right?._List ?? throw new InvalidOperationException("Failed to retrieve the right side ScrollList from the UIAgent.");

            _leftPanel = new HudSidePanel(leftScrollList);
            _rightPanel = new HudSidePanel(rightScrollList);

            var showState = app.UIAgent.State.Show.REFType.CreateInstance(0);
            uiAgent._StateManager._Next = showState.As<app.UIStateBase>();

            Hide();
        }

        private void SetText(via.gui.Text textComponent, string message)
        {
            (textComponent as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate(message));
        }

        private void SetRowValues(HistoryRow row, string frame, string direction, string buttons)
        {
            if (row.CachedFrame != frame)
            {
                row.CachedFrame = frame;
                SetText(row.FrameText, frame);
            }
            if (row.CachedDirection != direction)
            {
                row.CachedDirection = direction;
                SetText(row.DirectionText, direction);
            }
            if (row.CachedButtons != buttons)
            {
                row.CachedButtons = buttons;
                SetText(row.ButtonText, buttons);
            }
        }

        private void ShiftHistory(HudSidePanel panel)
        {
            for (int i = panel.Rows.Count - 1; i > 0; i--)
            {
                var current = panel.Rows[i];
                var previous = panel.Rows[i - 1];

                SetRowValues(current, previous.CachedFrame, previous.CachedDirection, previous.CachedButtons);
            }
        }

        public void SetPanelBindings(int leftPlayerIdx, int rightPlayerIdx)
        {
            UpdatePanelBinding(_leftPanel, leftPlayerIdx);
            UpdatePanelBinding(_rightPanel, rightPlayerIdx);
            UpdateVisibility();
        }

        private void UpdatePanelBinding(HudSidePanel panel, int newPlayerIdx)
        {
            if (panel.BoundPlayerIndex == newPlayerIdx) return;

            panel.BoundPlayerIndex = newPlayerIdx;

            if (newPlayerIdx == -1)
            {
                ResetPanel(panel);
            }
            else if (_parent._controller != null)
            {
                RebuildPanel(panel, _parent._controller);
            }
        }

        private void RebuildPanel(HudSidePanel panel, InputHistoryController controller)
        {
            int playerIdx = panel.BoundPlayerIndex;
            if (playerIdx == -1) return;

            var commandType = controller.GetCommandType(playerIdx);

            for (int offset = 0; offset < panel.Rows.Count; offset++)
            {
                var state = controller.GetInputHistory(playerIdx, offset);
                var row = panel.Rows[offset];

                if (state.FrameCount == 0)
                {
                    SetRowValues(row, string.Empty, string.Empty, string.Empty);
                }
                else
                {
                    string frameStr = $"{state.FrameCount}";
                    string dirStr = ParseInputDirection(state.InputValue);
                    string btnStr = ParseInputButtons(state.InputValue, commandType);
                    SetRowValues(row, frameStr, dirStr, btnStr);
                }
            }

            // If the match hasn't recorded its first frame yet, keep versions at 0 so frame 1 triggers an initial write
            var newestState = controller.GetInputHistory(playerIdx, 0);
            if (newestState.FrameCount == 0)
            {
                panel.LastHeadVersion = 0;
                panel.LastShiftVersion = 0;
            }
            else
            {
                panel.LastHeadVersion = controller.GetHeadVersion(playerIdx);
                panel.LastShiftVersion = controller.GetShiftVersion(playerIdx);
            }
        }

        public void ApplyVisualConfig(ModConfig config)
        {
            ApplyPanelTransform(_leftPanel, config.LeftPanelTransform, config.ShowBackgroundTexture);
            ApplyPanelTransform(_rightPanel, config.RightPanelTransform, config.ShowBackgroundTexture);
        }

        private static void ApplyPanelTransform(HudSidePanel panel, PanelTransformConfig transform, bool showBackground)
        {
            var pos = panel.BasePosition;
            pos.x += transform.OffsetX;
            pos.y += transform.OffsetY;
            panel.ScrollList.Position = pos;

            var scale = panel.BaseScale * transform.Scale;
            panel.ScrollList.Scale = scale;

            foreach (var bg in panel.BackgroundElements)
            {
                bg.Visible = showBackground;
            }
        }

        private static string ParseInputDirection(ushort inputValue)
        {
            var truncatedInputValue = (ushort)(inputValue & 0xF); // Mask to get only the lower 4 bits
            var inputKey = truncatedInputValue switch
            {
                0 => "5",
                1 << 0 => "8", // up
                1 << 1 => "2", // down
                1 << 2 => "4", // left
                1 << 3 => "6", //right
                1 << 0 | 1 << 2 => "7", // up-left
                1 << 0 | 1 << 3 => "9", // up-right
                1 << 1 | 1 << 2 => "1", // down-left
                1 << 1 | 1 << 3 => "3", // down-right
                _ => throw new InvalidOperationException($"This should never happen. Invalid truncated input value: {truncatedInputValue}"),
            };
            return $"<ICON {inputKey}>";
        }

        private static string ParseInputButtons(ushort inputValue, app.EConfigInputType inputType)
        {
            var buttonMask = (ushort)(inputValue >> 4); // Shift right to get the upper 12 bits
            var buttons = new List<string>();

            // TODO - add support for modern vs classic vs dynamic
            switch (inputType)
            {
                case app.EConfigInputType.NORMAL:
                    if ((buttonMask & (1 << 0)) != 0) buttons.Add("<ICON lp>");
                    if ((buttonMask & (1 << 1)) != 0) buttons.Add("<ICON mp>");
                    if ((buttonMask & (1 << 2)) != 0) buttons.Add("<ICON hp>");
                    if ((buttonMask & (1 << 3)) != 0) buttons.Add("<ICON lk>");
                    if ((buttonMask & (1 << 4)) != 0) buttons.Add("<ICON mk>");
                    if ((buttonMask & (1 << 5)) != 0) buttons.Add("<ICON hk>");
                    break;
                case app.EConfigInputType.CASUAL:
                    if ((buttonMask & (1 << 0)) != 0) buttons.Add("<ICON ls>");
                    if ((buttonMask & (1 << 1)) != 0) buttons.Add("<ICON sm>");
                    if ((buttonMask & (1 << 2)) != 0) buttons.Add("<ICON dp>");
                    if ((buttonMask & (1 << 3)) != 0) buttons.Add("<ICON ms>");
                    if ((buttonMask & (1 << 4)) != 0) buttons.Add("<ICON hs>");
                    if ((buttonMask & (1 << 5)) != 0) buttons.Add("<ICON auto>");
                    if ((buttonMask & (1 << 8)) != 0) buttons.Add("<ICON di>");
                    if ((buttonMask & (1 << 9)) != 0) buttons.Add("<ICON tr>");
                    break;
                case app.EConfigInputType.SUPER_EASY:
                    if ((buttonMask & (1 << 0)) != 0) buttons.Add("<ICON ls>");
                    if ((buttonMask & (1 << 1)) != 0) buttons.Add("<ICON di>");
                    if ((buttonMask & (1 << 2)) != 0) buttons.Add("<ICON dp>");
                    if ((buttonMask & (1 << 3)) != 0) buttons.Add("<ICON ms>");
                    if ((buttonMask & (1 << 4)) != 0) buttons.Add("<ICON hs>");
                    if ((buttonMask & (1 << 5)) != 0) buttons.Add("<ICON sa>");
                    if ((buttonMask & (1 << 8)) != 0) buttons.Add("<ICON tr>");
                    if ((buttonMask & (1 << 9)) != 0) buttons.Add("<ICON od>");
                    break;
                default:
                    break;
            }

            return string.Join("", buttons);
        }

        private void SetFirstItem(via.gui.SelectItem selectItem, via.gui.Text frameText, ushort inputValue, app.EConfigInputType inputType)
        {
            // set the message of the text component to the input value
            var directionText = UIHelpers.GetGuiChild(selectItem, "e_text_center").As<via.gui.Text>();

            var buttonText = UIHelpers.GetGuiChild(selectItem, "e_text_right").As<via.gui.Text>();

            (frameText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate("1"));
            (directionText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate(ParseInputDirection(inputValue)));
            (buttonText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate(ParseInputButtons(inputValue, inputType)));
        }

        public void UpdateUI()
        {
            var controller = _parent._controller ?? throw new InvalidOperationException("InputHistoryController is not initialized.");

            UpdatePanel(_leftPanel, controller);
            UpdatePanel(_rightPanel, controller);
        }

        private void UpdatePanel(HudSidePanel panel, InputHistoryController controller)
        {
            int playerIdx = panel.BoundPlayerIndex;
            if (playerIdx == -1) return;

            uint targetShift = controller.GetShiftVersion(playerIdx);
            uint targetHead = controller.GetHeadVersion(playerIdx);

            if (targetShift > panel.LastShiftVersion)
            {
                ShiftHistory(panel);
                panel.LastShiftVersion = targetShift;

                var headState = controller.GetInputHistory(playerIdx, 0);
                var cmdType = controller.GetCommandType(playerIdx);

                SetRowValues(
                    panel.Rows[0],
                    $"{headState.FrameCount}",
                    ParseInputDirection(headState.InputValue),
                    ParseInputButtons(headState.InputValue, cmdType)
                );

                panel.LastHeadVersion = targetHead;
            }
            else if (targetHead > panel.LastHeadVersion)
            {
                var headState = controller.GetInputHistory(playerIdx, 0);
                string frameStr = $"{headState.FrameCount}";
                panel.Rows[0].CachedFrame = frameStr;
                SetText(panel.Rows[0].FrameText, frameStr);

                panel.LastHeadVersion = targetHead;
            }
        }

        private void ResetPanel(HudSidePanel panel)
        {
            foreach (var row in panel.Rows)
            {
                SetRowValues(row, string.Empty, string.Empty, string.Empty);
            }
            panel.LastHeadVersion = 0;
            panel.LastShiftVersion = 0;
        }

        public void ResetHistory()
        {
            ResetPanel(_leftPanel);
            ResetPanel(_rightPanel);
        }

        private void UpdateVisibility()
        {
            _leftPanel.ScrollList.Visible = _isHudVisible && (_leftPanel.BoundPlayerIndex != -1);
            _rightPanel.ScrollList.Visible = _isHudVisible && (_rightPanel.BoundPlayerIndex != -1);
        }

        public void Show()
        {
            _isHudVisible = true;
            UpdateVisibility();
        }

        public void Hide()
        {
            _isHudVisible = false;
            UpdateVisibility();
        }

        public void Dispose()
        {
            _leftPanel.ScrollList.Visible = false;
            _rightPanel.ScrollList.Visible = false;
            _engineStringPool.Dispose();
        }
    }
}
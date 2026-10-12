using SF6_Plugin_Core.Memory;
using SF6_Plugin_Core.UI;


namespace SF6_MIH.InputHistory;

//FIXME: refactor
partial class InputHistoryClass
{
    // write the view part of the class (maybe an internal class?)
    private class InputHistoryView : IDisposable
    {

        private readonly EngineStringPool _engineStringPool = new();

        private readonly InputHistoryClass _parent;

        // Add methods to display the input history, update the UI, and handle user interactions.
        // private readonly app.UIStateManager _uiStateManager;

        private readonly via.gui.ScrollList _leftSideScrollList;
        private readonly via.gui.ScrollList _rightSideScrollList;

        private readonly List<via.gui.SelectItem> _leftSelectItems = [];
        private readonly List<via.gui.SelectItem> _rightSelectItems = [];

        // hold references to these as they update basically each frame
        private readonly via.gui.Text _firstLeftItemText;
        private readonly via.gui.Text _firstRightItemText;

        // head versioning for each player, for when the input doesn't change and we need to +1 the current frame count
        private uint _p1HeadVersion = 1;
        private uint _p2HeadVersion = 1;

        // shift versioning for each player, for when the input changes and we shift down the history
        private uint _p1ShiftVersion = 0;
        private uint _p2ShiftVersion = 0;

        public InputHistoryView(InputHistoryClass parent, app.UIAgent uiAgent)
        {
            _parent = parent;

            // _uiStateManager = uiAgent._StateManager;
            var partsManager = uiAgent._PartsManager;
            var left = (partsManager._List[0] as IObject)?.As<app.UIPartsScrollList>();
            _leftSideScrollList = left?._List ?? throw new InvalidOperationException("Failed to retrieve the left side ScrollList from the UIAgent.");

            var right = (partsManager._List[1] as IObject)?.As<app.UIPartsScrollList>();
            _rightSideScrollList = right?._List ?? throw new InvalidOperationException("Failed to retrieve the right side ScrollList from the UIAgent.");

            // here we get all the children of the left and assign them to our list
            var leftChildren = UIHelpers.GetGuiChildrenOfType(_leftSideScrollList, via.gui.SelectItem.REFType);
            for (int i = leftChildren.Length - 1; i >= 0; i--)
            {
                // iterate backwards to reverse the order of the items, as they are added in reverse order in the prefab
                var childMo = leftChildren[i] as ManagedObject;
                if (childMo == null) continue;
                _leftSelectItems.Add(childMo.As<via.gui.SelectItem>());
            }

            var rightChildren = UIHelpers.GetGuiChildrenOfType(_rightSideScrollList, via.gui.SelectItem.REFType);
            for (int i = rightChildren.Length - 1; i >= 0; i--)
            {
                // iterate backwards to reverse the order of the items, as they are added in reverse order in the prefab
                var childMo = rightChildren[i] as ManagedObject;
                if (childMo == null) continue;
                _rightSelectItems.Add(childMo.As<via.gui.SelectItem>());
            }

            // get a handle to the text components of the first items in each list, so we can update them easily
            _firstLeftItemText = UIHelpers.GetGuiChild(_leftSelectItems[0], "e_text_left").As<via.gui.Text>();
            _firstRightItemText = UIHelpers.GetGuiChild(_rightSelectItems[0], "e_text_left").As<via.gui.Text>();


            var showState = app.UIAgent.State.Show.REFType.CreateInstance(0);

            // set it once, it seems that setting this multiple times doesn't work properly.
            // fallback to just changing the visibility of the elements instead
            uiAgent._StateManager._Next = showState.As<app.UIStateBase>();

            // still hide the actual elements
            Hide();
        }

        private void ShiftHistory(List<via.gui.SelectItem> selectItems)
        {
            for (int i = selectItems.Count - 1; i > 0; i--)
            {
                var currentItem = selectItems[i];
                var previousItem = selectItems[i - 1];

                // get the text elements of the current and previous items
                var textChildrenOfCurrent = UIHelpers.GetGuiChildrenOfType(currentItem, via.gui.Text.REFType);
                var textChildrenOfPrevious = UIHelpers.GetGuiChildrenOfType(previousItem, via.gui.Text.REFType);

                if (textChildrenOfCurrent.Length == 0 || textChildrenOfPrevious.Length == 0)
                {
                    throw new InvalidOperationException("Failed to retrieve Text components from SelectItem.");
                }

                for (int j = 0; j < textChildrenOfCurrent.Length; j++)
                {
                    var currentText = textChildrenOfCurrent[j].GetObjectAs<via.gui.Text>("Failed to retrieve Text component from SelectItem.");
                    var previousText = textChildrenOfPrevious[j].GetObjectAs<via.gui.Text>("Failed to retrieve Text component from SelectItem.");

                    try
                    {
                        string prevMessage = previousText.Message ?? string.Empty;
                        (currentText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate(prevMessage));
                    }
                    catch (Exception ex)
                    {
                        API.LogError($"Failed to assign text during ShiftHistory: {ex.Message}");
                    }
                }
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

        private static string ParseInputButtons(ushort inputValue)
        {
            var buttonMask = (ushort)(inputValue >> 4); // Shift right to get the upper 12 bits
            var buttons = new List<string>();

            // TODO - add support for modern vs classic vs dynamic

            if ((buttonMask & (1 << 0)) != 0) buttons.Add("<ICON lp>");
            if ((buttonMask & (1 << 1)) != 0) buttons.Add("<ICON mp>");
            if ((buttonMask & (1 << 2)) != 0) buttons.Add("<ICON hp>");
            if ((buttonMask & (1 << 3)) != 0) buttons.Add("<ICON lk>");
            if ((buttonMask & (1 << 4)) != 0) buttons.Add("<ICON mk>");
            if ((buttonMask & (1 << 5)) != 0) buttons.Add("<ICON hk>");

            return string.Join("", buttons);
        }

        private void SetFirstItem(via.gui.SelectItem selectItem, via.gui.Text frameText, ushort inputValue)
        {
            // set the message of the text component to the input value
            var directionText = UIHelpers.GetGuiChild(selectItem, "e_text_center").As<via.gui.Text>();

            var buttonText = UIHelpers.GetGuiChild(selectItem, "e_text_right").As<via.gui.Text>();

            (frameText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate("1"));
            (directionText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate(ParseInputDirection(inputValue)));
            (buttonText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate(ParseInputButtons(inputValue)));
        }

        public void UpdateUI()
        {
            var controller = _parent._controller ?? throw new InvalidOperationException("InputHistoryController is not initialized.");
            if (controller.P1ShiftVersion > _p1ShiftVersion)
            {
                // shift the left side UI down
                ShiftHistory(_leftSelectItems);
                _p1ShiftVersion = controller.P1ShiftVersion;

                // here we update the head item
                SetFirstItem(_leftSelectItems[0], _firstLeftItemText, controller.GetP1InputHistory(0).InputValue);
            }

            if (controller.P2ShiftVersion > _p2ShiftVersion)
            {
                // shift the right side UI down
                ShiftHistory(_rightSelectItems);
                _p2ShiftVersion = controller.P2ShiftVersion;

                // here we update the head item
                SetFirstItem(_rightSelectItems[0], _firstRightItemText, controller.GetP2InputHistory(0).InputValue);
            }

            if (controller.P1HeadVersion > _p1HeadVersion)
            {
                // update the left side UI
                var p1Head = controller.GetP1InputHistory(0);
                (_firstLeftItemText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate($"{p1Head.FrameCount}"));
                _p1HeadVersion = controller.P1HeadVersion;
            }

            if (controller.P2HeadVersion > _p2HeadVersion)
            {
                // update the right side UI
                var p2Head = controller.GetP2InputHistory(0);
                (_firstRightItemText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate($"{p2Head.FrameCount}"));
                _p2HeadVersion = controller.P2HeadVersion;
            }


        }

        private void ResetList(List<via.gui.SelectItem> list)
        {
            foreach (var item in list)
            {
                // get the text elements of the current and previous items
                var textChildrenOfCurrent = UIHelpers.GetGuiChildrenOfType(item, via.gui.Text.REFType);

                if (textChildrenOfCurrent.Length == 0)
                {
                    throw new InvalidOperationException("Failed to retrieve Text components from SelectItem.");
                }

                for (int j = 0; j < textChildrenOfCurrent.Length; j++)
                {
                    var currentText = textChildrenOfCurrent[j].GetObjectAs<via.gui.Text>("Failed to retrieve Text component from SelectItem.");
                    (currentText as IObject)?.Call("set_Message", _engineStringPool.GetOrCreate(string.Empty));
                }
            }
        }

        public void ResetHistory()
        {
            ResetList(_leftSelectItems);
            ResetList(_rightSelectItems);

            _p1HeadVersion = 0;
            _p2HeadVersion = 0;
            _p1ShiftVersion = 0;
            _p2ShiftVersion = 0;
        }

        public void Show()
        {
            _leftSideScrollList.Visible = true;
            _rightSideScrollList.Visible = true;
        }

        public void Hide()
        {
            _leftSideScrollList.Visible = false;
            _rightSideScrollList.Visible = false;
        }

        public void Dispose()
        {
            _leftSideScrollList.Visible = false;
            _rightSideScrollList.Visible = false;

            _engineStringPool.Dispose();
        }
    }
}
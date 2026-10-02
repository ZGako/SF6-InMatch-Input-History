
using SF6_Plugin_Core;
using SF6_Plugin_Core.UI;


namespace SF6_MIH.InputHistory;

partial class InputHistoryClass
{
    // write the view part of the class (maybe an internal class?)
    private class InputHistoryView : IDisposable
    {

        private readonly EngineStringTracker _engineStringTracker = new();

        private readonly InputHistoryClass _parent;

        // Add methods to display the input history, update the UI, and handle user interactions.
        private readonly app.UIStateManager _uiStateManager;

        private readonly via.gui.ScrollList _leftSideScrollList;
        private readonly via.gui.ScrollList _rightSideScrollList;

        private readonly List<via.gui.SelectItem> _leftSelectItems = [];
        private readonly List<via.gui.SelectItem> _rightSelectItems = [];

        // hold references to these as they update basically each frame
        private readonly via.gui.Text _firstLeftItemText;
        private readonly via.gui.Text _firstRightItemText;

        // head versioning for each player, for when the input doesn't change and we need to +1 the current frame count
        private uint _p1HeadVersion = 0;
        private uint _p2HeadVersion = 0;

        // shift versioning for each player, for when the input changes and we shift down the history
        private uint _p1ShiftVersion = 0;
        private uint _p2ShiftVersion = 0;

        public InputHistoryView(InputHistoryClass parent, app.UIAgent uiAgent)
        {
            _parent = parent;

            _uiStateManager = uiAgent._StateManager;
            var partsManager = uiAgent._PartsManager;
            var left = (partsManager._List[0] as IObject)?.As<app.UIPartsScrollList>();
            _leftSideScrollList = left?._List ?? throw new InvalidOperationException("Failed to retrieve the left side ScrollList from the UIAgent.");

            var pos = via.vec3.REFType.CreateValueType().As<via.vec3>();
            pos.x = _leftSideScrollList.Position.x;
            pos.y = _leftSideScrollList.Position.y;
            pos.z = _leftSideScrollList.Position.z;
            pos.x += 100;
            _leftSideScrollList.Position = pos;

            var right = (partsManager._List[1] as IObject)?.As<app.UIPartsScrollList>();
            _rightSideScrollList = right?._List ?? throw new InvalidOperationException("Failed to retrieve the right side ScrollList from the UIAgent.");

            var pos2 = via.vec3.REFType.CreateValueType().As<via.vec3>();
            pos2.x = _rightSideScrollList.Position.x;
            pos2.y = _rightSideScrollList.Position.y;
            pos2.z = _rightSideScrollList.Position.z;
            pos2.x -= 100;
            _rightSideScrollList.Position = pos2;

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

            _uiStateManager._Next = showState.As<app.UIStateBase>();
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
                    var currentTextMo = textChildrenOfCurrent[j] as ManagedObject;
                    var previousTextMo = textChildrenOfPrevious[j] as ManagedObject;

                    if (currentTextMo == null || previousTextMo == null)
                    {
                        throw new InvalidOperationException("Failed to cast child to ManagedObject.");
                    }

                    var currentText = currentTextMo.As<via.gui.Text>();
                    var previousText = previousTextMo.As<via.gui.Text>();

                    if (currentText == null || previousText == null)
                    {
                        throw new InvalidOperationException("Failed to cast ManagedObject to via.gui.Text.");
                    }

                    // copy the message from the previous item to the current item
                    // currentText.Message = previousText.Message;
                    try
                    {
                        string prevMessage = previousText.Message ?? "";
                        _engineStringTracker.AssignText(currentText, prevMessage, (str) => (currentText as IObject)?.Call("set_Message", str));
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

            _engineStringTracker.AssignText(frameText, "1", (str) => (frameText as IObject)?.Call("set_Message", str));
            _engineStringTracker.AssignText(directionText, ParseInputDirection(inputValue), (str) => (directionText as IObject)?.Call("set_Message", str));
            _engineStringTracker.AssignText(buttonText, ParseInputButtons(inputValue), (str) => (buttonText as IObject)?.Call("set_Message", str));
        }

        public void UpdateUI()
        {
            var controller = _parent._controller;

            if (controller.P1HeadVersion > _p1HeadVersion)
            {
                // update the left side UI
                var p1Head = controller.GetP1InputHistory(0);
                _engineStringTracker.AssignText(_firstLeftItemText, $"{p1Head.FrameCount}", (str) => (_firstLeftItemText as IObject)?.Call("set_Message", str));
                _p1HeadVersion = controller.P1HeadVersion;
            }

            if (controller.P2HeadVersion > _p2HeadVersion)
            {
                // update the right side UI
                var p2Head = controller.GetP2InputHistory(0);
                _engineStringTracker.AssignText(_firstRightItemText, $"{p2Head.FrameCount}", (str) => (_firstRightItemText as IObject)?.Call("set_Message", str));
                _p2HeadVersion = controller.P2HeadVersion;
            }

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
        }

        public void Dispose()
        {
            var hideState = app.UIAgent.State.Show.REFType.CreateInstance(0);

            _uiStateManager._Curt = hideState.As<app.UIStateBase>();
            _uiStateManager._Next = hideState.As<app.UIStateBase>();

            _engineStringTracker.Clear();
        }
    }
}
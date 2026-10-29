using app;
using SF6_Plugin_Core.Memory;
using SF6_Plugin_Core.Services;
using SF6_Plugin_Core.UI;

namespace SF6_MIH.InputHistory;

//FIXME: refactor
internal partial class InputHistoryClass
{
    public enum PreviewForceMode
    {
        None,
        ForceLeft,
        ForceRight
    }

    private class InputHistoryController : IDisposable
    {
        private static InputHistoryController? s_instance;

        private static readonly Field InputNewField = nBattle.cPlayer.REFType.GetField("pl_input_new");
        private static readonly Field FightStatusField = nBattle.sGame.REFType.GetField("fight_st");
        private readonly BattleSetting? _battleSetting;

        private readonly BattleSystem? _battleSystem;
        private readonly ManagedObject? _gameMo;

        private readonly bool _isDummy;

        private readonly InputState[] _p1InputBuffer = new InputState[19];
        private readonly InputState[] _p2InputBuffer = new InputState[19];
        private readonly InputHistoryClass _parent;
        private readonly ManagedObject? _player1Mo;
        private readonly ManagedObject? _player2Mo;

        private bool _firstActivation = true;
        private bool _isOnline;
        private int _onlineSelfPlayerIndex; // 0 = P1, 1 = P2

        // Cached match state for player 1 (index 0) and player 2 (index 1)
        private EConfigInputType _p1CommandType = EConfigInputType.NOT_SPECIFIED;
        private int _p1HeadIndex;
        private bool _p1IsCpu;
        private EConfigInputType _p2CommandType = EConfigInputType.NOT_SPECIFIED;
        private int _p2HeadIndex;
        private bool _p2IsCpu;
        private PreviewForceMode _previewForceMode = PreviewForceMode.None;

        // fight status of the previous frame
        private nBattle.FIGHT_ST _previousFightStatus;

        public InputHistoryController(InputHistoryClass parent, bool isDummy = false)
        {
            _parent = parent;
            _isDummy = isDummy;

            if (_isDummy)
            {
                _isOnline = true;
                _p1IsCpu = false; // P1 as Player
                _p2IsCpu = false; // P2 as CPU
                _p1CommandType = EConfigInputType.NORMAL;
                _p2CommandType = EConfigInputType.NORMAL;

                PopulateDummyHistory();
                return;
            }

            // get the gBattle 
            var battle = API.GetTDB().FindType("gBattle");

            var statics = battle?.Statics ?? throw new InvalidOperationException("Failed to retrieve gBattle statics.");


            _battleSystem = statics.GetField("System")
                .GetObjectAs<BattleSystem>("Failed to retrieve BattleSystem from gBattle statics.");
            _battleSetting = statics.GetField("Setting")
                .GetObjectAs<BattleSetting>("Failed to retrieve BattleSetting from gBattle statics.");

            _gameMo = statics.GetField("Game") as ManagedObject ??
                      throw new InvalidOperationException("Failed to retrieve sGame from gBattle statics.");

            var playerMo = statics.GetField("Player") as ManagedObject ??
                           throw new InvalidOperationException("Failed to retrieve Player from gBattle statics.");
            var playerArray = (playerMo as IObject).GetField("mcPlayer")
                .GetObjectAs<_System.Array>("Failed to retrieve cPlayer_Array1D from Player.");

            _player1Mo = playerArray[0] as ManagedObject ??
                         throw new InvalidOperationException("Failed to retrieve Player 1 from cPlayer_Array1D.");
            _player2Mo = playerArray[1] as ManagedObject ??
                         throw new InvalidOperationException("Failed to retrieve Player 2 from cPlayer_Array1D.");

            BattleHudHooks.RegisterBattleHudActivationCallbacks(Show, OnHudDeactivate);

            s_instance = this;
        }

        // head versioning for each player, for when the input doesn't change and we need to +1 the current frame count
        public uint P1HeadVersion { get; private set; }
        public uint P2HeadVersion { get; private set; }

        // shift versioning for each player, for when the input changes and we shift down the history
        public uint P1ShiftVersion { get; private set; } = 1;
        public uint P2ShiftVersion { get; private set; } = 1;


        public void Dispose()
        {
            if (!_isDummy)
            {
                BattleHudHooks.UnregisterBattleHudCallbacks(onActivate: Show, onDeactivate: OnHudDeactivate);
                s_instance = null;
            }
        }

        public void SetPreviewForceMode(PreviewForceMode mode)
        {
            if (!_isDummy) return;
            _previewForceMode = mode;
            ApplyConfig(ModConfig.Instance);
        }

        private void PopulateDummyHistory()
        {
            // Player 1 inputs (Left side, from top to bottom)
            (uint Frames, ushort Input)[] p1Inputs =
            [
                (20, 0), // 5 (Neutral)
                (1, 1 << 1), // 2 (Down)
                (6, (1 << 1) | (1 << 3)), // 3 (Down-Right)
                (1, 1 << 1), // 2 (Down)
                (4, (1 << 1) | (1 << 5) | (1 << 8)), // 2 (Down) + MP + MK
                (8, (1 << 5) | (1 << 8)), // 5 (Neutral) + MP + MK
                (1, 0), // 5 (Neutral)
                (2, 1 << 3), // 6 (Right)
                (3, (1 << 1) | (1 << 3)), // 3 (Down-Right)
                (2, (1 << 1) | (1 << 6)), // 2 (Down) + HP
                (5, 1 << 6), // 5 (Neutral) + HP
                (6, 0), // 5 (Neutral)
                (1, 1 << 1), // 2 (Down)
                (45, (1 << 1) | (1 << 2)), // 1 (Down-Left)
                (4, (1 << 1) | (1 << 2) | (1 << 4)), // 1 (Down-Left) + LP
                (11, (1 << 1) | (1 << 2) | (1 << 4) | (1 << 7)), // 1 (Down-Left) + LP + LK
                (6, (1 << 1) | (1 << 2)) // 1 (Down-Left)
            ];

            // Player 2 inputs (Right side, from top to bottom)
            (uint Frames, ushort Input)[] p2Inputs =
            [
                (46, (1 << 1) | (1 << 3)), // 3 (Down-Right)
                (17, 1 << 3), // 6 (Right)
                (16, (1 << 1) | (1 << 3)), // 3 (Down-Right)
                (10, (1 << 1) | (1 << 3) | (1 << 5)), // 3 (Down-Right) + MP
                (6, 1 << 3), // 6 (Right)
                (9, 1 << 3), // 6 (Right)
                (6, (1 << 3) | (1 << 4)), // 6 (Right) + LP
                (30, 1 << 3), // 6 (Right)
                (25, 1 << 3), // 6 (Right)
                (1, (1 << 1) | (1 << 3)), // 3 (Down-Right)
                (2, (1 << 1) | (1 << 3) | (1 << 5) | (1 << 8)), // 3 (Down-Right) + MP + MK
                (13, 1 << 3), // 6 (Right)
                (19, 0), // 5 (Neutral)
                (4, 1 << 5), // 5 (Neutral) + MP
                (16, 0), // 5 (Neutral)
                (1, 0), // 5 (Neutral)
                (7, (1 << 1) | (1 << 7)), // 2 (Down) + LK
                (20, 0) // 5 (Neutral)
            ];

            for (var i = 0; i < _p1InputBuffer.Length; i++)
            {
                var p1Sample = p1Inputs[i % p1Inputs.Length];
                _p1InputBuffer[i] = new InputState { FrameCount = p1Sample.Frames, InputValue = p1Sample.Input };

                var p2Sample = p2Inputs[i % p2Inputs.Length];
                _p2InputBuffer[i] = new InputState { FrameCount = p2Sample.Frames, InputValue = p2Sample.Input };
            }

            _p1HeadIndex = _p1InputBuffer.Length - 1;
            _p2HeadIndex = _p2InputBuffer.Length - 1;
            P1HeadVersion = 1;
            P2HeadVersion = 1;
            P1ShiftVersion = 1;
            P2ShiftVersion = 1;
        }

        private static ref InputState GetInputFromNewest(InputState[] buffer, int headIndex, int offset)
        {
            var index = headIndex - offset;

            // If the offset pushes us below 0, wrap around to the end of the array
            if (index < 0) index += buffer.Length;

            return ref buffer[index];
        }

        // Add methods to handle user input, update the view, and manage the input history data.

        public ref InputState GetInputHistory(int playerIndex, int offset)
        {
            return ref playerIndex == 0
                ? ref GetInputFromNewest(_p1InputBuffer, _p1HeadIndex, offset)
                : ref GetInputFromNewest(_p2InputBuffer, _p2HeadIndex, offset);
        }

        public uint GetHeadVersion(int playerIndex)
        {
            return playerIndex == 0 ? P1HeadVersion : P2HeadVersion;
        }

        public uint GetShiftVersion(int playerIndex)
        {
            return playerIndex == 0 ? P1ShiftVersion : P2ShiftVersion;
        }

        public EConfigInputType GetCommandType(int playerIndex)
        {
            return playerIndex == 0 ? _p1CommandType : _p2CommandType;
        }

        [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
        public static void OnUpdateBehaviorCallback()
        {
            // Runs every frame before the engine update
            if (s_instance == null) return;

            // check the fight status to determine if we should be tracking inputs or not
            var gameAddress = s_instance._gameMo!.GetAddress();
            var fightStatusByte = (sbyte)Marshal.ReadByte((nint)FightStatusField!.GetDataRaw(gameAddress, false));
            var fightStatus = (nBattle.FIGHT_ST)fightStatusByte;

            if (fightStatus != s_instance._previousFightStatus)
                // API.LogInfo($"Fight status changed from {s_instance._previousFightStatus} to {fightStatus}.");
                // status changed
                switch (fightStatus)
                {
                    case nBattle.FIGHT_ST.READY:
                        s_instance.ResetHistory();
                        // if it changed from something else to ready, reset the history
                        break;
                    case nBattle.FIGHT_ST.FINISH:
                        // if it changed to finish, hide the UI as the game won't call Unload on its own for some reason
                        s_instance.Hide();
                        break;
                    case nBattle.FIGHT_ST.STAGE_INIT:
                    case nBattle.FIGHT_ST.ROUND_INIT:
                    case nBattle.FIGHT_ST.APPEAR:
                    case nBattle.FIGHT_ST.NOW:
                    case nBattle.FIGHT_ST.WIN_WAIT:
                    case nBattle.FIGHT_ST.WIN:
                        // keep them separate just in case I want to add something later
                        break;
                }

            s_instance._previousFightStatus = fightStatus;

            // double check for ease
            if (fightStatus != nBattle.FIGHT_ST.READY && fightStatus != nBattle.FIGHT_ST.NOW) return;

            // pause check
            if (s_instance._battleSystem!._IsPause) return;

            var p1Addr = s_instance._player1Mo!.GetAddress();
            var p2Addr = s_instance._player2Mo!.GetAddress();

            var p1_input_new = (ushort)Marshal.ReadInt16((nint)InputNewField!.GetDataRaw(p1Addr, false));
            // ushort p1_input_old = (ushort)Marshal.ReadInt16((nint)s_inputOldField!.GetDataRaw(p1Addr, false));
            ref var p1_input_old = ref GetInputFromNewest(s_instance._p1InputBuffer, s_instance._p1HeadIndex, 0);

            var p2_input_new = (ushort)Marshal.ReadInt16((nint)InputNewField!.GetDataRaw(p2Addr, false));
            // ushort p2_input_old = (ushort)Marshal.ReadInt16((nint)s_inputOldField!.GetDataRaw(p2Addr, false));
            ref var p2_input_old = ref GetInputFromNewest(s_instance._p2InputBuffer, s_instance._p2HeadIndex, 0);

            if (p1_input_new == p1_input_old.InputValue)
            {
                // input stayed the same, increment the frame count of the current head
                if (p1_input_old.FrameCount < 99)
                {
                    s_instance.P1HeadVersion++;
                    p1_input_old.FrameCount++;
                }
            }
            else
            {
                // input changed, shift the history down and add the new input to the head
                s_instance.P1ShiftVersion++;
                s_instance._p1HeadIndex = (s_instance._p1HeadIndex + 1) % s_instance._p1InputBuffer.Length;
                ref var head = ref GetInputFromNewest(s_instance._p1InputBuffer, s_instance._p1HeadIndex, 0);
                head.FrameCount = 1;
                head.InputValue = p1_input_new;
            }

            if (p2_input_new == p2_input_old.InputValue)
            {
                // input stayed the same, increment the frame count of the current head
                if (p2_input_old.FrameCount < 99)
                {
                    s_instance.P2HeadVersion++;
                    p2_input_old.FrameCount++;
                }
            }
            else
            {
                // input changed, shift the history down and add the new input to the head
                s_instance.P2ShiftVersion++;
                s_instance._p2HeadIndex = (s_instance._p2HeadIndex + 1) % s_instance._p2InputBuffer.Length;
                ref var head = ref GetInputFromNewest(s_instance._p2InputBuffer, s_instance._p2HeadIndex, 0);
                head.FrameCount = 1;
                head.InputValue = p2_input_new;
            }

            try
            {
                s_instance._parent._view?.UpdateUI();
            }
            catch (Exception ex)
            {
                API.LogError($"Error updating input history UI: {ex.Message}");
            }
        }

        private void ResetHistory()
        {
            for (var i = 0; i < 19; i++)
            {
                _p1InputBuffer[i] = new InputState();
                _p2InputBuffer[i] = new InputState();
            }

            _p1HeadIndex = 0;
            _p2HeadIndex = 0;
            P1HeadVersion = 0;
            P2HeadVersion = 0;
            P1ShiftVersion = 1;
            P2ShiftVersion = 1;

            _parent._view?.ResetHistory();
        }

        private static void RouteSinglePlayer(int playerIdx, OnlinePosition position, out int leftPanelPlayerIdx,
            out int rightPanelPlayerIdx)
        {
            leftPanelPlayerIdx = -1;
            rightPanelPlayerIdx = -1;

            switch (position)
            {
                case OnlinePosition.FOLLOWPLAYER:
                    if (playerIdx == 0) leftPanelPlayerIdx = 0;
                    else rightPanelPlayerIdx = 1;
                    break;

                case OnlinePosition.ALWAYSP1:
                    // Always show your own inputs on the Left (P1) side
                    leftPanelPlayerIdx = playerIdx;
                    break;

                case OnlinePosition.ALWAYSP2:
                    // Always show your own inputs on the Right (P2) side
                    rightPanelPlayerIdx = playerIdx;
                    break;
            }
        }

        public void ApplyConfig(ModConfig config)
        {
            var view = _parent._view;
            if (view == null) return;

            // -1 = Hidden, 0 = Show Player 1's inputs, 1 = Show Player 2's inputs
            int leftPanelPlayerIdx;
            int rightPanelPlayerIdx;

            if (_isOnline)
            {
                RouteSinglePlayer(_onlineSelfPlayerIndex, config.DisplayPosition, out leftPanelPlayerIdx,
                    out rightPanelPlayerIdx);
            }
            else
            {
                var showP1 = !_p1IsCpu || config.OfflineShowCPU;
                var showP2 = !_p2IsCpu || config.OfflineShowCPU;

                // If only one player is shown (1 Human vs 1 Hidden CPU) and ApplyPositionAgainstCPU is enabled,
                // apply DisplayPosition (FOLLOWPLAYER, ALWAYSP1, ALWAYSP2) to that human player.
                if (showP1 != showP2 && config.ApplyPositionAgainstCPU)
                {
                    var activePlayerIdx = showP1 ? 0 : 1;
                    RouteSinglePlayer(activePlayerIdx, config.DisplayPosition, out leftPanelPlayerIdx,
                        out rightPanelPlayerIdx);
                }
                else
                {
                    leftPanelPlayerIdx = showP1 ? 0 : -1;
                    rightPanelPlayerIdx = showP2 ? 1 : -1;
                }
            }

            // Additive preview override for the Options Menu:
            // Keeps whatever panel is currently enabled by DisplayPosition, and ALSO enables
            // the panel being adjusted using the remaining dummy player's input buffer.
            if (_isDummy && _previewForceMode != PreviewForceMode.None)
            {
                if (_previewForceMode == PreviewForceMode.ForceLeft && leftPanelPlayerIdx == -1)
                    // If Right is showing P1 (0) (e.g., Always Right), show P2 (1) on the Left!
                    // Otherwise default to P1 (0).
                    leftPanelPlayerIdx = rightPanelPlayerIdx == 0 ? 1 : 0;
                else if (_previewForceMode == PreviewForceMode.ForceRight && rightPanelPlayerIdx == -1)
                    // If Left is showing P1 (0) (e.g., Same as Player / Always Left), show P2 (1) on the Right!
                    // Otherwise default to P2 (1) or P1 (0).
                    rightPanelPlayerIdx = leftPanelPlayerIdx == 1 ? 0 : 1;
            }

            view.SetPanelBindings(leftPanelPlayerIdx, rightPanelPlayerIdx);
            view.ApplyVisualConfig(config);
        }


        private void Show(BattleHudManager _)
        {
            if (_firstActivation)
            {
                var player1 = _battleSetting!.Player1P();
                var player2 = _battleSetting.Player2P();

                _p1CommandType = player1.CommandType;
                _p2CommandType = player2.CommandType;
                _p1IsCpu = player1.IsCPU;
                _p2IsCpu = player2.IsCPU;
                _isOnline = _battleSetting.IsOnline;

                if (_isOnline)
                {
                    var selfInfo = GameSingletonManager.NetworkManager?.Session?.FGBattle?.SelfBattleMemberInfo;
                    if (selfInfo != null) _onlineSelfPlayerIndex = selfInfo.MemberIndex == 0 ? 0 : 1;
                }

                ApplyConfig(ModConfig.Instance);
                _firstActivation = false;
            }

            _parent._view?.Show();
        }

        private void Hide()
        {
            _parent._view?.Hide();
        }

        private void OnHudDeactivate()
        {
            // nothing test
        }

        public void ActivateDummyPreview()
        {
            if (!_isDummy) return;
            ApplyConfig(ModConfig.Instance);
            _parent._view?.Show();
        }

        public struct InputState
        {
            public uint FrameCount = 0;
            public ushort InputValue = 0;

            public InputState()
            {
            }
        }
    }
}
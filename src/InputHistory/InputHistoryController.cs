
using SF6_Plugin_Core.Memory;
using SF6_Plugin_Core.Services;
using SF6_Plugin_Core.UI;

namespace SF6_MIH.InputHistory;

//FIXME: refactor
partial class InputHistoryClass
{
    private class InputHistoryController : IDisposable
    {
        private static InputHistoryController? s_instance;
        private readonly InputHistoryClass _parent;

        private bool _firstActivation = true;

        private readonly app.BattleSystem _battleSystem;
        private readonly app.BattleSetting _battleSetting;
        private readonly ManagedObject _gameMo;
        private readonly ManagedObject _player1Mo;
        private readonly ManagedObject _player2Mo;

        private static readonly Field InputNewField = nBattle.cPlayer.REFType.GetField("pl_input_new");
        private static readonly Field FightStatusField = nBattle.sGame.REFType.GetField("fight_st");

        public struct InputState
        {
            public uint FrameCount = 0;
            public ushort InputValue = 0;

            public InputState()
            {
            }
        }

        private static ref InputState GetInputFromNewest(InputState[] buffer, int headIndex, int offset)
        {
            int index = headIndex - offset;

            // If the offset pushes us below 0, wrap around to the end of the array
            if (index < 0)
            {
                index += buffer.Length;
            }

            return ref buffer[index];
        }

        private readonly InputState[] _p1InputBuffer = new InputState[19];
        private int _p1HeadIndex = 0;
        private readonly InputState[] _p2InputBuffer = new InputState[19];
        private int _p2HeadIndex = 0;

        // Cached match state for player 1 (index 0) and player 2 (index 1)
        private app.EConfigInputType _p1CommandType = app.EConfigInputType.NOT_SPECIFIED;
        private app.EConfigInputType _p2CommandType = app.EConfigInputType.NOT_SPECIFIED;
        private bool _p1IsCpu = false;
        private bool _p2IsCpu = false;
        private bool _isOnline = false;
        private int _onlineSelfPlayerIndex = 0; // 0 = P1, 1 = P2

        // head versioning for each player, for when the input doesn't change and we need to +1 the current frame count
        public uint P1HeadVersion { get; private set; } = 0;
        public uint P2HeadVersion { get; private set; } = 0;

        // shift versioning for each player, for when the input changes and we shift down the history
        public uint P1ShiftVersion { get; private set; } = 1;
        public uint P2ShiftVersion { get; private set; } = 1;

        // fight status of the previous frame
        private nBattle.FIGHT_ST _previousFightStatus;

        // Add methods to handle user input, update the view, and manage the input history data.

        public ref InputState GetInputHistory(int playerIndex, int offset) =>
                    ref (playerIndex == 0
                        ? ref GetInputFromNewest(_p1InputBuffer, _p1HeadIndex, offset)
                        : ref GetInputFromNewest(_p2InputBuffer, _p2HeadIndex, offset));

        public uint GetHeadVersion(int playerIndex) => playerIndex == 0 ? P1HeadVersion : P2HeadVersion;
        public uint GetShiftVersion(int playerIndex) => playerIndex == 0 ? P1ShiftVersion : P2ShiftVersion;
        public app.EConfigInputType GetCommandType(int playerIndex) => playerIndex == 0 ? _p1CommandType : _p2CommandType;

        public InputHistoryController(InputHistoryClass parent)
        {
            _parent = parent;

            // get the gBattle 
            var battle = API.GetTDB().FindType("gBattle");

            var statics = (battle?.Statics) ?? throw new InvalidOperationException("Failed to retrieve gBattle statics.");


            _battleSystem = statics.GetField("System").GetObjectAs<app.BattleSystem>("Failed to retrieve BattleSystem from gBattle statics.");
            _battleSetting = statics.GetField("Setting").GetObjectAs<app.BattleSetting>("Failed to retrieve BattleSetting from gBattle statics.");

            _gameMo = statics.GetField("Game") as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve sGame from gBattle statics.");

            var playerMo = statics.GetField("Player") as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve Player from gBattle statics.");
            var playerArray = (playerMo as IObject).GetField("mcPlayer").GetObjectAs<_System.Array>("Failed to retrieve cPlayer_Array1D from Player.");

            _player1Mo = playerArray[0] as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve Player 1 from cPlayer_Array1D.");
            _player2Mo = playerArray[1] as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve Player 2 from cPlayer_Array1D.");

            BattleHudHooks.RegisterBattleHudActivationCallbacks(Show, OnHudDeactivate);

            s_instance = this;
        }

        [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
        public static void OnUpdateBehaviorCallback()
        {
            // Runs every frame before the engine update
            if (s_instance == null) return;

            // check the fight status to determine if we should be tracking inputs or not
            var gameAddress = s_instance._gameMo.GetAddress();
            sbyte fightStatusByte = (sbyte)Marshal.ReadByte((nint)FightStatusField!.GetDataRaw(gameAddress, false));
            var fightStatus = (nBattle.FIGHT_ST)fightStatusByte;

            if (fightStatus != s_instance._previousFightStatus)
            {
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
            }

            s_instance._previousFightStatus = fightStatus;

            // double check for ease
            if (fightStatus != nBattle.FIGHT_ST.READY && fightStatus != nBattle.FIGHT_ST.NOW) return;

            // pause check
            if (s_instance._battleSystem._IsPause) return;

            ulong p1Addr = s_instance._player1Mo.GetAddress();
            ulong p2Addr = s_instance._player2Mo.GetAddress();

            ushort p1_input_new = (ushort)Marshal.ReadInt16((nint)InputNewField!.GetDataRaw(p1Addr, false));
            // ushort p1_input_old = (ushort)Marshal.ReadInt16((nint)s_inputOldField!.GetDataRaw(p1Addr, false));
            ref InputState p1_input_old = ref GetInputFromNewest(s_instance._p1InputBuffer, s_instance._p1HeadIndex, 0);

            ushort p2_input_new = (ushort)Marshal.ReadInt16((nint)InputNewField!.GetDataRaw(p2Addr, false));
            // ushort p2_input_old = (ushort)Marshal.ReadInt16((nint)s_inputOldField!.GetDataRaw(p2Addr, false));
            ref InputState p2_input_old = ref GetInputFromNewest(s_instance._p2InputBuffer, s_instance._p2HeadIndex, 0);

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
            for (int i = 0; i < 19; i++)
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

        private static void RouteSinglePlayer(int playerIdx, OnlinePosition position, out int leftPanelPlayerIdx, out int rightPanelPlayerIdx)
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
                RouteSinglePlayer(_onlineSelfPlayerIndex, config.DisplayPosition, out leftPanelPlayerIdx, out rightPanelPlayerIdx);
            }
            else
            {
                bool showP1 = !_p1IsCpu || config.OfflineShowCPU;
                bool showP2 = !_p2IsCpu || config.OfflineShowCPU;

                // If only one player is shown (1 Human vs 1 Hidden CPU) and ApplyPositionAgainstCPU is enabled,
                // apply DisplayPosition (FOLLOWPLAYER, ALWAYSP1, ALWAYSP2) to that human player.
                if (showP1 != showP2 && config.ApplyPositionAgainstCPU)
                {
                    int activePlayerIdx = showP1 ? 0 : 1;
                    RouteSinglePlayer(activePlayerIdx, config.DisplayPosition, out leftPanelPlayerIdx, out rightPanelPlayerIdx);
                }
                else
                {
                    leftPanelPlayerIdx = showP1 ? 0 : -1;
                    rightPanelPlayerIdx = showP2 ? 1 : -1;
                }
            }

            view.SetPanelBindings(leftPanelPlayerIdx, rightPanelPlayerIdx);
            view.ApplyVisualConfig(config);
        }


        private void Show(app.BattleHudManager _)
        {
            if (_firstActivation)
            {
                var player1 = _battleSetting.Player1P();
                var player2 = _battleSetting.Player2P();

                _p1CommandType = player1.CommandType;
                _p2CommandType = player2.CommandType;
                _p1IsCpu = player1.IsCPU;
                _p2IsCpu = player2.IsCPU;
                _isOnline = _battleSetting.IsOnline;

                if (_isOnline)
                {
                    var selfInfo = GameSingletonManager.NetworkManager?.Session?.FGBattle?.SelfBattleMemberInfo;
                    if (selfInfo != null)
                    {
                        _onlineSelfPlayerIndex = (selfInfo.MemberIndex == 0) ? 0 : 1;
                    }
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


        public void Dispose()
        {
            BattleHudHooks.UnregisterBattleHudCallbacks(onActivate: Show, onDeactivate: OnHudDeactivate);
            s_instance = null;
        }
    }

}
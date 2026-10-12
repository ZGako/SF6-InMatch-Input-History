

using SF6_Plugin_Core.Memory;
using SF6_Plugin_Core.UI;

namespace SF6_MIH.InputHistory;

//FIXME: refactor
partial class InputHistoryClass
{
    private class InputHistoryController : IDisposable
    {
        private static InputHistoryController? s_instance;
        private readonly InputHistoryClass _parent;

        private readonly app.BattleSystem _battleSystem;
        private readonly app.BattleSetting _battleSetting;
        private readonly ManagedObject _gameMo;
        private readonly ManagedObject _player1Mo;
        private readonly ManagedObject _player2Mo;

        private static readonly Field InputNewField = nBattle.cPlayer.REFType.GetField("pl_input_new");
        // private static readonly Field InputOldField = nBattle.cPlayer.REFType.GetField("pl_input_old");
        private static readonly Field FightStatusField = nBattle.sGame.REFType.GetField("fight_st");

        public struct InputState
        {
            public uint FrameCount = 0;
            public ushort InputValue = 0;

            public InputState()
            {
            }
        }

        private struct Config
        {
            public enum OnlinePosition
            {
                MATCHPLAYER,
                ALWAYSP1,
                ALWAYSP2
            }

            public OnlinePosition DisplayPosition;

            public bool OfflineShowCPU;
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



        public ref InputState GetP1InputHistory(int index) => ref GetInputFromNewest(_p1InputBuffer, _p1HeadIndex, index);
        public ref InputState GetP2InputHistory(int index) => ref GetInputFromNewest(_p2InputBuffer, _p2HeadIndex, index);

        // TODO: add a config thing



        // head versioning for each player, for when the input doesn't change and we need to +1 the current frame count
        public uint P1HeadVersion { get; private set; } = 0;
        public uint P2HeadVersion { get; private set; } = 0;

        // shift versioning for each player, for when the input changes and we shift down the history
        public uint P1ShiftVersion { get; private set; } = 1;
        public uint P2ShiftVersion { get; private set; } = 1;

        // fight status of the previous frame
        private nBattle.FIGHT_ST _previousFightStatus;

        // Add methods to handle user input, update the view, and manage the input history data.

        public InputHistoryController(InputHistoryClass parent)
        {
            _parent = parent;

            // get the gBattle 
            var battle = API.GetTDB().FindType("gBattle");

            var statics = battle?.Statics;
            if (statics == null)
            {
                throw new InvalidOperationException("Failed to retrieve gBattle statics.");
            }

            _battleSystem = statics.GetField("System").GetObjectAs<app.BattleSystem>("Failed to retrieve BattleSystem from gBattle statics.");

            _battleSetting = statics.GetField("Setting").GetObjectAs<app.BattleSetting>("Failed to retrieve BattleSetting from gBattle statics.");

            var player1 = _battleSetting.Player1P();
            var player2 = _battleSetting.Player2P();

            _parent._view?.SetInputTypes(player1.CommandType, player2.CommandType);

            _gameMo = statics.GetField("Game") as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve sGame from gBattle statics.");

            var playerMo = statics.GetField("Player") as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve Player from gBattle statics.");
            var playerArray = (playerMo as IObject).GetField("mcPlayer").GetObjectAs<_System.Array>("Failed to retrieve cPlayer_Array1D from Player.");

            _player1Mo = playerArray[0] as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve Player 1 from cPlayer_Array1D.");
            _player2Mo = playerArray[1] as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve Player 2 from cPlayer_Array1D.");

            BattleHudDispatcher.RegisterBattleHudActivationCallbacks(Show, () =>
            {
                // nothing test
            });

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
                GetInputFromNewest(s_instance._p1InputBuffer, s_instance._p1HeadIndex, 0).FrameCount = 1;
                GetInputFromNewest(s_instance._p1InputBuffer, s_instance._p1HeadIndex, 0).InputValue = p1_input_new;
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
                GetInputFromNewest(s_instance._p2InputBuffer, s_instance._p2HeadIndex, 0).FrameCount = 1;
                GetInputFromNewest(s_instance._p2InputBuffer, s_instance._p2HeadIndex, 0).InputValue = p2_input_new;
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


            var player1 = _battleSetting.Player1P();
            var player2 = _battleSetting.Player2P();

            _parent._view?.SetInputTypes(player1.CommandType, player2.CommandType);

            _parent._view?.ResetHistory();
        }


        private void Show(app.BattleHudManager _)
        {
            _parent._view?.Show();
        }

        // TODO: call this function also when inputs aren't active (like inbetween round and shit)
        private void Hide()
        {
            _parent._view?.Hide();
        }


        public void Dispose()
        {
            BattleHudDispatcher.UnregisterBattleHudCallbacks(onActivate: Show, onDeactivate: Hide);
            s_instance = null;
        }
    }

}
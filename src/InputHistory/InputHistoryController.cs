

namespace SF6_MIH.InputHistory;

partial class InputHistoryClass
{
    // write the controller part of the class (maybe an internal class?)
    private class InputHistoryController : IDisposable
    {
        private static InputHistoryController? s_instance;
        private readonly InputHistoryClass _parent;

        private readonly app.BattleSetting _battleSetting;
        private readonly ManagedObject _player1Mo;
        private readonly ManagedObject _player2Mo;

        private static Field? s_inputNewField;
        private static Field? s_inputOldField;

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



        public ref InputState GetP1InputHistory(int index) => ref GetInputFromNewest(_p1InputBuffer, _p1HeadIndex, index);
        public ref InputState GetP2InputHistory(int index) => ref GetInputFromNewest(_p2InputBuffer, _p2HeadIndex, index);

        // TODO: add a config thing


        // head versioning for each player, for when the input doesn't change and we need to +1 the current frame count
        public uint P1HeadVersion { get; private set; } = 0;
        public uint P2HeadVersion { get; private set; } = 0;

        // shift versioning for each player, for when the input changes and we shift down the history
        public uint P1ShiftVersion { get; private set; } = 1;
        public uint P2ShiftVersion { get; private set; } = 1;

        // Add methods to handle user input, update the view, and manage the input history data.

        public InputHistoryController(InputHistoryClass parent)
        {
            _parent = parent;

            // get the gBattle 
            var battle = API.GetTDB().FindType("gBattle");

            var statics = battle?.Statics;

            var settingsMo = statics?.GetField("Setting") as ManagedObject;
            var settings = settingsMo?.As<app.BattleSetting>();
            if (settings == null)
            {
                throw new InvalidOperationException("Failed to retrieve BattleSetting from gBattle statics.");
            }

            var playerMo = statics?.GetField("Player") as ManagedObject;
            if (playerMo == null)
            {
                throw new InvalidOperationException("Failed to retrieve Player from gBattle statics.");
            }

            var playerArrayMo = (playerMo as IObject).GetField("mcPlayer") as ManagedObject;
            var playerArray = playerArrayMo?.As<_System.Array>();

            if (playerArray == null)
            {
                throw new InvalidOperationException("Failed to retrieve cPlayer_Array1D from Player.");
            }

            _player1Mo = playerArray[0] as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve Player 1 from cPlayer_Array1D.");
            _player2Mo = playerArray[1] as ManagedObject ?? throw new InvalidOperationException("Failed to retrieve Player 2 from cPlayer_Array1D.");

            _battleSetting = settings;

            s_inputNewField = nBattle.cPlayer.REFType.GetField("pl_input_new");
            s_inputOldField = nBattle.cPlayer.REFType.GetField("pl_input_old");

            if (s_inputNewField == null || s_inputOldField == null)
            {
                throw new InvalidOperationException("Failed to retrieve pl_input_new or pl_input_old fields from cPlayer.");
            }

            s_instance = this;
        }



        // [Callback(typeof(UpdateGUI), CallbackType.Pre)]
        [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
        public static void OnUpdateBehaviorCallback()
        {
            // Runs every frame before the engine update
            if (s_instance == null) return;

            ulong p1Addr = s_instance._player1Mo.GetAddress();
            ulong p2Addr = s_instance._player2Mo.GetAddress();

            ushort p1_input_new = (ushort)Marshal.ReadInt16((nint)s_inputNewField!.GetDataRaw(p1Addr, false));
            // ushort p1_input_old = (ushort)Marshal.ReadInt16((nint)s_inputOldField!.GetDataRaw(p1Addr, false));
            ref InputState p1_input_old = ref GetInputFromNewest(s_instance._p1InputBuffer, s_instance._p1HeadIndex, 0);

            ushort p2_input_new = (ushort)Marshal.ReadInt16((nint)s_inputNewField!.GetDataRaw(p2Addr, false));
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

            s_instance._parent._view.UpdateUI();
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
            P1ShiftVersion = 0;
            P2ShiftVersion = 0;
        }

        public void Dispose()
        {
            s_instance = null;

            // other stuff probably
            s_inputNewField = null;
            s_inputOldField = null;
        }
    }

}
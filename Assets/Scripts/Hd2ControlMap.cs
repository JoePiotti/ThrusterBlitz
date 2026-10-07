using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Which action each controller button performs. Defaults match the current game.
/// The left menu button is not in this table. It always opens this player's menu.
/// </summary>
[DefaultExecutionOrder(-100)]
public class Hd2ControlMap : MonoBehaviour
{
    public enum Action
    {
        None,
        FireLeft,
        ReloadLeft,
        LeftPointer,
        FireRight,
        ReloadRight,
        RightPointer,
        Jump,
        Sprint,
        Microphone,
        Crouch
    }

    public enum Button
    {
        LeftStickPress,
        LeftTrigger,
        LeftGrip,
        LeftX,
        LeftY,
        RightStickPress,
        RightTrigger,
        RightGrip,
        RightA,
        RightB
    }

    public enum MicMode
    {
        PushToMute,
        PushToTalk,
        Toggle
    }

    public const int ActionCount = 11;
    public const int ButtonCount = 10;

    readonly Action[] bound = new Action[ButtonCount];
    readonly InputAction[] input = new InputAction[ButtonCount];
    readonly bool[] consumed = new bool[ButtonCount];
    readonly bool[] pressed = new bool[ButtonCount];
    readonly bool[] released = new bool[ButtonCount];
    readonly bool[] held = new bool[ButtonCount];
    MicMode micMode = MicMode.PushToTalk;
    bool micOn;

    public event System.Action Changed;

    public MicMode MicrophoneMode
    {
        get { return micMode; }
        set
        {
            if (micMode == value)
                return;
            micMode = value;
            Changed?.Invoke();
        }
    }

    public bool MenuOpen { get; set; }

    public bool MicOpen
    {
        get
        {
            bool held = Held(Action.Microphone);
            switch (micMode)
            {
                case MicMode.PushToTalk:
                    return held;
                case MicMode.PushToMute:
                    return !held;
                default:
                    return micOn;
            }
        }
    }

    void Awake()
    {
        SetDefaults();
    }

    void OnEnable()
    {
        for (int i = 0; i < ButtonCount; i++)
        {
            var button = (Button)i;
            var action = new InputAction("Hd2Btn" + button, InputActionType.Button);
            action.AddBinding(Path(button));
            action.Enable();
            input[i] = action;
        }
    }

    void OnDisable()
    {
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == null)
                continue;
            input[i].Disable();
            input[i].Dispose();
            input[i] = null;
        }
    }

    void Update()
    {
        for (int i = 0; i < ButtonCount; i++)
        {
            if (input[i] == null)
            {
                pressed[i] = false;
                released[i] = false;
                held[i] = false;
                continue;
            }

            pressed[i] = input[i].WasPressedThisFrame();
            released[i] = input[i].WasReleasedThisFrame();
            held[i] = input[i].IsPressed();
        }

        if (micMode == MicMode.Toggle && Pressed(Action.Microphone))
            micOn = !micOn;
    }

    void LateUpdate()
    {
        for (int i = 0; i < consumed.Length; i++)
            consumed[i] = false;
    }

    public void SetDefaults()
    {
        for (int i = 0; i < bound.Length; i++)
            bound[i] = Action.None;
        bound[(int)Button.LeftStickPress] = Action.Sprint;
        bound[(int)Button.LeftTrigger] = Action.FireLeft;
        bound[(int)Button.LeftGrip] = Action.ReloadLeft;
        bound[(int)Button.LeftX] = Action.LeftPointer;
        bound[(int)Button.RightTrigger] = Action.FireRight;
        bound[(int)Button.RightGrip] = Action.ReloadRight;
        bound[(int)Button.RightA] = Action.RightPointer;
        bound[(int)Button.RightB] = Action.Jump;
        bound[(int)Button.RightStickPress] = Action.Crouch;
        Changed?.Invoke();
    }

    public Action Binding(Button button)
    {
        return bound[(int)button];
    }

    public void SetBinding(Button button, Action action)
    {
        if (bound[(int)button] == action)
            return;
        bound[(int)button] = action;
        Changed?.Invoke();
    }

    public void Consume(Button button)
    {
        consumed[(int)button] = true;
    }

    public bool Pressed(Action action)
    {
        return Scan(action, 0);
    }

    public bool Released(Action action)
    {
        return Scan(action, 1);
    }

    public bool Held(Action action)
    {
        return Scan(action, 2);
    }

    public bool ButtonPressed(Button button)
    {
        int index = (int)button;
        return pressed[index] && !consumed[index];
    }

    bool Scan(Action action, int kind)
    {
        if (action == Action.None)
            return false;
        for (int i = 0; i < bound.Length; i++)
        {
            if (bound[i] != action || consumed[i])
                continue;
            bool hit = kind == 0 ? pressed[i] : kind == 1 ? released[i] : held[i];
            if (hit)
                return true;
        }

        return false;
    }

    public static string ActionLabel(Action action)
    {
        switch (action)
        {
            case Action.FireLeft: return "Fire left weapon";
            case Action.ReloadLeft: return "Reload left weapon";
            case Action.LeftPointer: return "Left pointer";
            case Action.FireRight: return "Fire right weapon";
            case Action.ReloadRight: return "Reload right weapon";
            case Action.RightPointer: return "Right pointer";
            case Action.Jump: return "Jump";
            case Action.Sprint: return "Sprint";
            case Action.Microphone: return "Microphone";
            case Action.Crouch: return "Crouch";
            default: return "None";
        }
    }

    public static string ButtonLabel(Button button)
    {
        switch (button)
        {
            case Button.LeftStickPress:
            case Button.RightStickPress:
                return "Stick press";
            case Button.LeftTrigger:
            case Button.RightTrigger:
                return "Trigger";
            case Button.LeftGrip:
            case Button.RightGrip:
                return "Grip";
            case Button.LeftX: return "X";
            case Button.LeftY: return "Y";
            case Button.RightA: return "A";
            case Button.RightB: return "B";
            default: return button.ToString();
        }
    }

    public static string MicLabel(MicMode mode)
    {
        switch (mode)
        {
            case MicMode.PushToMute: return "Push to mute";
            case MicMode.Toggle: return "Toggle";
            default: return "Push to talk";
        }
    }

    static string Path(Button button)
    {
        switch (button)
        {
            case Button.LeftStickPress: return "<XRController>{LeftHand}/thumbstickClicked";
            case Button.LeftTrigger: return "<XRController>{LeftHand}/triggerPressed";
            case Button.LeftGrip: return "<XRController>{LeftHand}/gripPressed";
            case Button.LeftX: return "<XRController>{LeftHand}/primaryButton";
            case Button.LeftY: return "<XRController>{LeftHand}/secondaryButton";
            case Button.RightStickPress: return "<XRController>{RightHand}/thumbstickClicked";
            case Button.RightTrigger: return "<XRController>{RightHand}/triggerPressed";
            case Button.RightGrip: return "<XRController>{RightHand}/gripPressed";
            case Button.RightA: return "<XRController>{RightHand}/primaryButton";
            default: return "<XRController>{RightHand}/secondaryButton";
        }
    }
}

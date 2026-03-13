// Copyright (c) 2025 Synty Studios Limited. All rights reserved.
//
// Use of this software is subject to the terms and conditions of the End User Licence Agreement (EULA) 
// of the store at which you purchased this asset. 
//
// Synty assets are available at:
// https://www.syntystore.com
// https://assetstore.unity.com/publishers/5217
// https://www.fab.com/sellers/Synty%20Studios
//
// Sample scripts are included only as examples and are not intended as production-ready.

using UnityEngine;
using UnityEngine.EventSystems;



#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
// compile errors?
using UnityEngine.InputSystem.UI;
// compile errors?
// if you are getting a compile error here you likely need to import the Input System package (com.unity.inputsystem) in the package manager or change the input setting in player settings back to 'Input Manager (Old)'
#endif


namespace Synty.Interface.DarkFantasyHUD.Samples
{
    public enum UnifiedKey
    {
        None,

        // Letters
        A, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

        // Digits
        Digit0, Digit1, Digit2, Digit3, Digit4,
        Digit5, Digit6, Digit7, Digit8, Digit9,

        // Numpad
        Numpad0, Numpad1, Numpad2, Numpad3, Numpad4,
        Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,

        // Arrows
        UpArrow, DownArrow, LeftArrow, RightArrow,

        // Function Keys
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

        // Controls
        Space, Enter, Tab, Backspace, Escape,
        LeftShift, RightShift, LeftCtrl, RightCtrl, LeftAlt, RightAlt,
    }


    public static class UnifiedKeyConverter
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        public static KeyCode? ToKeyCode(this UnifiedKey key)
        {
            switch (key)
            {
                case UnifiedKey.A: return KeyCode.A;
                case UnifiedKey.B: return KeyCode.B;
                case UnifiedKey.C: return KeyCode.C;
                case UnifiedKey.D: return KeyCode.D;
                case UnifiedKey.E: return KeyCode.E;
                case UnifiedKey.F: return KeyCode.F;
                case UnifiedKey.G: return KeyCode.G;
                case UnifiedKey.H: return KeyCode.H;
                case UnifiedKey.I: return KeyCode.I;
                case UnifiedKey.J: return KeyCode.J;
                case UnifiedKey.K: return KeyCode.K;
                case UnifiedKey.L: return KeyCode.L;
                case UnifiedKey.M: return KeyCode.M;
                case UnifiedKey.N: return KeyCode.N;
                case UnifiedKey.O: return KeyCode.O;
                case UnifiedKey.P: return KeyCode.P;
                case UnifiedKey.Q: return KeyCode.Q;
                case UnifiedKey.R: return KeyCode.R;
                case UnifiedKey.S: return KeyCode.S;
                case UnifiedKey.T: return KeyCode.T;
                case UnifiedKey.U: return KeyCode.U;
                case UnifiedKey.V: return KeyCode.V;
                case UnifiedKey.W: return KeyCode.W;
                case UnifiedKey.X: return KeyCode.X;
                case UnifiedKey.Y: return KeyCode.Y;
                case UnifiedKey.Z: return KeyCode.Z;

                case UnifiedKey.Digit0: return KeyCode.Alpha0;
                case UnifiedKey.Digit1: return KeyCode.Alpha1;
                case UnifiedKey.Digit2: return KeyCode.Alpha2;
                case UnifiedKey.Digit3: return KeyCode.Alpha3;
                case UnifiedKey.Digit4: return KeyCode.Alpha4;
                case UnifiedKey.Digit5: return KeyCode.Alpha5;
                case UnifiedKey.Digit6: return KeyCode.Alpha6;
                case UnifiedKey.Digit7: return KeyCode.Alpha7;
                case UnifiedKey.Digit8: return KeyCode.Alpha8;
                case UnifiedKey.Digit9: return KeyCode.Alpha9;

                case UnifiedKey.Numpad0: return KeyCode.Keypad0;
                case UnifiedKey.Numpad1: return KeyCode.Keypad1;
                case UnifiedKey.Numpad2: return KeyCode.Keypad2;
                case UnifiedKey.Numpad3: return KeyCode.Keypad3;
                case UnifiedKey.Numpad4: return KeyCode.Keypad4;
                case UnifiedKey.Numpad5: return KeyCode.Keypad5;
                case UnifiedKey.Numpad6: return KeyCode.Keypad6;
                case UnifiedKey.Numpad7: return KeyCode.Keypad7;
                case UnifiedKey.Numpad8: return KeyCode.Keypad8;
                case UnifiedKey.Numpad9: return KeyCode.Keypad9;

                case UnifiedKey.UpArrow: return KeyCode.UpArrow;
                case UnifiedKey.DownArrow: return KeyCode.DownArrow;
                case UnifiedKey.LeftArrow: return KeyCode.LeftArrow;
                case UnifiedKey.RightArrow: return KeyCode.RightArrow;

                case UnifiedKey.F1: return KeyCode.F1;
                case UnifiedKey.F2: return KeyCode.F2;
                case UnifiedKey.F3: return KeyCode.F3;
                case UnifiedKey.F4: return KeyCode.F4;
                case UnifiedKey.F5: return KeyCode.F5;
                case UnifiedKey.F6: return KeyCode.F6;
                case UnifiedKey.F7: return KeyCode.F7;
                case UnifiedKey.F8: return KeyCode.F8;
                case UnifiedKey.F9: return KeyCode.F9;
                case UnifiedKey.F10: return KeyCode.F10;
                case UnifiedKey.F11: return KeyCode.F11;
                case UnifiedKey.F12: return KeyCode.F12;

                case UnifiedKey.Space: return KeyCode.Space;
                case UnifiedKey.Enter: return KeyCode.Return;
                case UnifiedKey.Tab: return KeyCode.Tab;
                case UnifiedKey.Backspace: return KeyCode.Backspace;
                case UnifiedKey.Escape: return KeyCode.Escape;

                case UnifiedKey.LeftShift: return KeyCode.LeftShift;
                case UnifiedKey.RightShift: return KeyCode.RightShift;
                case UnifiedKey.LeftCtrl: return KeyCode.LeftControl;
                case UnifiedKey.RightCtrl: return KeyCode.RightControl;
                case UnifiedKey.LeftAlt: return KeyCode.LeftAlt;
                case UnifiedKey.RightAlt: return KeyCode.RightAlt;

                default: return null;
            }
        }
#endif

#if ENABLE_INPUT_SYSTEM
        public static Key? ToInputSystemKey(this UnifiedKey key)
        {
            switch (key)
            {
                case UnifiedKey.A: return Key.A;
                case UnifiedKey.B: return Key.B;
                case UnifiedKey.C: return Key.C;
                case UnifiedKey.D: return Key.D;
                case UnifiedKey.E: return Key.E;
                case UnifiedKey.F: return Key.F;
                case UnifiedKey.G: return Key.G;
                case UnifiedKey.H: return Key.H;
                case UnifiedKey.I: return Key.I;
                case UnifiedKey.J: return Key.J;
                case UnifiedKey.K: return Key.K;
                case UnifiedKey.L: return Key.L;
                case UnifiedKey.M: return Key.M;
                case UnifiedKey.N: return Key.N;
                case UnifiedKey.O: return Key.O;
                case UnifiedKey.P: return Key.P;
                case UnifiedKey.Q: return Key.Q;
                case UnifiedKey.R: return Key.R;
                case UnifiedKey.S: return Key.S;
                case UnifiedKey.T: return Key.T;
                case UnifiedKey.U: return Key.U;
                case UnifiedKey.V: return Key.V;
                case UnifiedKey.W: return Key.W;
                case UnifiedKey.X: return Key.X;
                case UnifiedKey.Y: return Key.Y;
                case UnifiedKey.Z: return Key.Z;

                case UnifiedKey.Digit0: return Key.Digit0;
                case UnifiedKey.Digit1: return Key.Digit1;
                case UnifiedKey.Digit2: return Key.Digit2;
                case UnifiedKey.Digit3: return Key.Digit3;
                case UnifiedKey.Digit4: return Key.Digit4;
                case UnifiedKey.Digit5: return Key.Digit5;
                case UnifiedKey.Digit6: return Key.Digit6;
                case UnifiedKey.Digit7: return Key.Digit7;
                case UnifiedKey.Digit8: return Key.Digit8;
                case UnifiedKey.Digit9: return Key.Digit9;

                case UnifiedKey.Numpad0: return Key.Numpad0;
                case UnifiedKey.Numpad1: return Key.Numpad1;
                case UnifiedKey.Numpad2: return Key.Numpad2;
                case UnifiedKey.Numpad3: return Key.Numpad3;
                case UnifiedKey.Numpad4: return Key.Numpad4;
                case UnifiedKey.Numpad5: return Key.Numpad5;
                case UnifiedKey.Numpad6: return Key.Numpad6;
                case UnifiedKey.Numpad7: return Key.Numpad7;
                case UnifiedKey.Numpad8: return Key.Numpad8;
                case UnifiedKey.Numpad9: return Key.Numpad9;

                case UnifiedKey.UpArrow: return Key.UpArrow;
                case UnifiedKey.DownArrow: return Key.DownArrow;
                case UnifiedKey.LeftArrow: return Key.LeftArrow;
                case UnifiedKey.RightArrow: return Key.RightArrow;

                case UnifiedKey.F1: return Key.F1;
                case UnifiedKey.F2: return Key.F2;
                case UnifiedKey.F3: return Key.F3;
                case UnifiedKey.F4: return Key.F4;
                case UnifiedKey.F5: return Key.F5;
                case UnifiedKey.F6: return Key.F6;
                case UnifiedKey.F7: return Key.F7;
                case UnifiedKey.F8: return Key.F8;
                case UnifiedKey.F9: return Key.F9;
                case UnifiedKey.F10: return Key.F10;
                case UnifiedKey.F11: return Key.F11;
                case UnifiedKey.F12: return Key.F12;

                case UnifiedKey.Space: return Key.Space;
                case UnifiedKey.Enter: return Key.Enter;
                case UnifiedKey.Tab: return Key.Tab;
                case UnifiedKey.Backspace: return Key.Backspace;
                case UnifiedKey.Escape: return Key.Escape;

                case UnifiedKey.LeftShift: return Key.LeftShift;
                case UnifiedKey.RightShift: return Key.RightShift;
                case UnifiedKey.LeftCtrl: return Key.LeftCtrl;
                case UnifiedKey.RightCtrl: return Key.RightCtrl;
                case UnifiedKey.LeftAlt: return Key.LeftAlt;
                case UnifiedKey.RightAlt: return Key.RightAlt;

                default: return null;
            }
        }
#endif
    }

    public static class UnifiedInput
    {
        public static bool WasPressedThisFrame(UnifiedKey unifiedKey)
        {
#if ENABLE_INPUT_SYSTEM
            var key = unifiedKey.ToInputSystemKey();
            if(key.HasValue) return Keyboard.current[key.Value].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            var keyCode = unifiedKey.ToKeyCode();
            if(keyCode.HasValue) return Input.GetKeyDown(keyCode.Value);
#else
            Debug.LogWarning("No input system enabled in project settings.");
#endif
            return false;
        }
        
        public static bool IsPressed(UnifiedKey unifiedKey)
        {
#if ENABLE_INPUT_SYSTEM
            var key = unifiedKey.ToInputSystemKey();
            if (key.HasValue) return Keyboard.current[key.Value].isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            var keyCode = unifiedKey.ToKeyCode();
            if(keyCode.HasValue) return Input.GetKey(keyCode.Value);
#else
            Debug.LogWarning("No input system enabled in project settings.");
#endif
            return false;
        }
    }

    /// <summary>
    ///     Sample script that helps automatically select the correct input event module depending on your project's settings.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(EventSystem))]
    public class SampleAutoInputModule : MonoBehaviour
    {
        void OnEnable()
        {
            UpdateInputModule();
        }

        void UpdateInputModule()
        {
#if ENABLE_INPUT_SYSTEM
            // New Input System only
            if (GetComponent<InputSystemUIInputModule>() == null)
            {
                // Remove any existing modules
                foreach (var module in GetComponents<BaseInputModule>())
                {
                    DestroyImmediate(module);
                }
                gameObject.AddComponent<InputSystemUIInputModule>();
                if(!Application.isPlaying) Debug.Log("Added InputSystemUIInputModule (new input system)");
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            // Old Input Manager only
            if (GetComponent<StandaloneInputModule>() == null)
            {
                // Remove any existing modules
                foreach (var module in GetComponents<BaseInputModule>())
                {
                    DestroyImmediate(module);
                }
                gameObject.AddComponent<StandaloneInputModule>();
                if(!Application.isPlaying) Debug.Log("Added StandaloneInputModule (old input manager)");
            }
#else
            Debug.LogWarning("No input system enabled in project settings.");
#endif
        }
    }
}

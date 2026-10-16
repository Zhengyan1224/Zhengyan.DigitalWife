using Android.Content;
using Android.Text;
using Android.Views.InputMethods;
using Android.Widget;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>A masked password editor using the regular IME password keyboard.</summary>
internal sealed class AndroidPasswordEditText : EditText
{
    public AndroidPasswordEditText(Context context) : base(context)
    {
        Id = GenerateViewId();
        Focusable = true;
        FocusableInTouchMode = true;
        SetSingleLine(true);
        InputType = InputTypes.ClassText | InputTypes.TextVariationPassword | InputTypes.TextFlagNoSuggestions;
        ImeOptions = ImeAction.Done | (ImeAction)(ImeFlags.NoExtractUi | ImeFlags.NoFullscreen);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            ImeOptions |= (ImeAction)ImeFlags.NoPersonalizedLearning;
        SetSelectAllOnFocus(false);
        SetText(string.Empty, BufferType.Editable);
        SetSelection(0);
    }

    public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
    {
        if (outAttrs is null || !Enabled) return null;
        IInputConnection? connection = base.OnCreateInputConnection(outAttrs);
        // Some OEM secure keyboards (reported on Redmi Note 12 Pro) show up
        // for TYPE_TEXT_VARIATION_PASSWORD but cannot edit this dialog, while
        // the regular IME works. Request its password layout instead of the
        // OEM secure-keyboard handoff. This changes only EditorInfo: TextView's
        // actual InputType/transformation remain PASSWORD, so text stays masked
        // and native password handling (including surrounding-text setup) stays
        // intact. Keep suggestions/personalized learning disabled as well.
        outAttrs.InputType = InputTypes.ClassText | InputTypes.TextVariationVisiblePassword | InputTypes.TextFlagNoSuggestions;
        outAttrs.ImeOptions = (ImeFlags)ImeOptions;
        return connection is null ? null : new AndroidTextInputConnection(connection);
    }

    public override void OnWindowFocusChanged(bool hasWindowFocus)
    {
        base.OnWindowFocusChanged(hasWindowFocus);
        if (!hasWindowFocus) return;
        RequestFocus();
        Post(() =>
        {
            if (!IsAttachedToWindow || !HasWindowFocus || !HasFocus) return;
            // Android creates/reconnects the input connection when focus changes.
            // Do not restart it again here and invalidate the IME's new session.
            ((InputMethodManager?)Context?.GetSystemService(global::Android.Content.Context.InputMethodService))
                ?.ShowSoftInput(this, ShowFlags.Implicit);
        });
    }
}

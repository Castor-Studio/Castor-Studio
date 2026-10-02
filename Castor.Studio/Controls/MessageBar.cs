using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace CastorApplication.Controls;

// The one way a status or error line looks in the app: a tinted box, red for an error and
// neutral for a confirmation, with a ✕ when the message can be dismissed. It hides itself
// while Text is empty, so views bind the message and nothing else. Template and colours are
// in Styles/Controls.axaml.
//
//   <controls:MessageBar Text="{Binding CreateSceneError}"/>
//   <controls:MessageBar Text="{Binding SceneIoStatus.Text}"
//                        IsError="{Binding SceneIoStatus.IsError}"
//                        DismissCommand="{Binding SceneIoStatus.DismissCommand}"/>
public class MessageBar : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MessageBar, string?>(nameof(Text));

    public static readonly StyledProperty<bool> IsErrorProperty =
        AvaloniaProperty.Register<MessageBar, bool>(nameof(IsError), true);

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<MessageBar, ICommand?>(nameof(DismissCommand));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsError
    {
        get => GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public MessageBar()
    {
        SetCurrentValue(IsVisibleProperty, false);
        UpdatePseudoClasses();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            SetCurrentValue(IsVisibleProperty, !string.IsNullOrEmpty(Text));
        if (change.Property == IsErrorProperty || change.Property == DismissCommandProperty)
            UpdatePseudoClasses();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":error", IsError);
        PseudoClasses.Set(":dismissable", DismissCommand != null);
    }
}

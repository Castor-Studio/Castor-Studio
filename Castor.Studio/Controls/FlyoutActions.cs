using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.Controls;

// Confirm actions inside a flyout: run the command, wait for it to finish, then close the
// flyout (and any flyout it is nested in) unless the command reported an error. Without
// this the flyout stays open after success and users confirm twice.
//
//   <StackPanel controls:FlyoutActions.Error="{Binding CreateSceneError}">
//       <TextBox controls:FlyoutActions.EnterCommand="{Binding CreateSceneCommand}"/>
//       <Button Command="{Binding CreateSceneCommand}" controls:FlyoutActions.CloseOnSuccess="True"/>
//   </StackPanel>
public static class FlyoutActions
{
    // Error message of the action, inherited by the flyout content. Non-empty once the
    // command has completed means failure: the flyout stays open to show it.
    public static readonly AttachedProperty<string?> ErrorProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Error", typeof(FlyoutActions), inherits: true);

    // On a Button: runs its Command/CommandParameter instead of letting the Button do it.
    public static readonly AttachedProperty<bool> CloseOnSuccessProperty =
        AvaloniaProperty.RegisterAttached<Button, bool>("CloseOnSuccess", typeof(FlyoutActions));

    // On a TextBox: command run when Enter is pressed.
    public static readonly AttachedProperty<ICommand?> EnterCommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("EnterCommand", typeof(FlyoutActions));

    public static readonly AttachedProperty<object?> EnterCommandParameterProperty =
        AvaloniaProperty.RegisterAttached<TextBox, object?>("EnterCommandParameter", typeof(FlyoutActions));

    static FlyoutActions()
    {
        CloseOnSuccessProperty.Changed.AddClassHandler<Button>((button, e) =>
        {
            button.Click -= OnButtonClick;
            if (e.NewValue is true) button.Click += OnButtonClick;
        });
        EnterCommandProperty.Changed.AddClassHandler<TextBox>((textBox, e) =>
        {
            textBox.KeyDown -= OnTextBoxKeyDown;
            if (e.NewValue != null) textBox.KeyDown += OnTextBoxKeyDown;
        });
    }

    public static string? GetError(Control element) => element.GetValue(ErrorProperty);
    public static void SetError(Control element, string? value) => element.SetValue(ErrorProperty, value);
    public static bool GetCloseOnSuccess(Button element) => element.GetValue(CloseOnSuccessProperty);
    public static void SetCloseOnSuccess(Button element, bool value) => element.SetValue(CloseOnSuccessProperty, value);
    public static ICommand? GetEnterCommand(TextBox element) => element.GetValue(EnterCommandProperty);
    public static void SetEnterCommand(TextBox element, ICommand? value) => element.SetValue(EnterCommandProperty, value);
    public static object? GetEnterCommandParameter(TextBox element) => element.GetValue(EnterCommandParameterProperty);
    public static void SetEnterCommandParameter(TextBox element, object? value) => element.SetValue(EnterCommandParameterProperty, value);

    private static async void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        // Button only executes its Command when Click was not handled.
        e.Handled = true;
        await ExecuteAndCloseAsync(button, button.Command, button.CommandParameter);
    }

    private static async void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || e.Key != Key.Enter) return;
        e.Handled = true;
        await ExecuteAndCloseAsync(textBox, GetEnterCommand(textBox), GetEnterCommandParameter(textBox));
    }

    private static async Task ExecuteAndCloseAsync(Control source, ICommand? command, object? parameter)
    {
        if (command == null || !command.CanExecute(parameter)) return;

        if (command is IAsyncRelayCommand asyncCommand)
        {
            // The command surfaces its own failures through the Error binding.
            try { await asyncCommand.ExecuteAsync(parameter); }
            catch (OperationCanceledException) { return; }
        }
        else
        {
            command.Execute(parameter);
        }

        if (!string.IsNullOrEmpty(GetError(source))) return;
        HideContainingFlyouts(source);
    }

    // Flyout content is hosted in a Popup whose PlacementTarget is the control owning the
    // flyout; walking up from there reaches the parent flyout of a nested menu.
    private static void HideContainingFlyouts(Control source)
    {
        Control? current = source;
        while (current != null)
        {
            var popup = current.FindLogicalAncestorOfType<Popup>();
            if ((popup?.PlacementTarget ?? popup?.Parent) is not Control owner) return;

            var flyout = (owner as Button)?.Flyout ?? FlyoutBase.GetAttachedFlyout(owner);
            if (flyout is { IsOpen: true }) flyout.Hide();
            current = owner;
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using XXX.TestBench.App;
using XXX.TestBench.App.Views;

namespace XXX.TestBench.App.Tests;

public sealed class LoginViewInputTests
{
    private static readonly object AvaloniaSetupLock = new();
    private static bool _avaloniaConfigured;

    [Fact]
    public void CredentialFieldsDisableAlternativeTextInput()
    {
        EnsureAvaloniaConfigured();

        var view = new LoginView();
        var loginNameBox = view.FindControl<TextBox>("LoginNameBox");
        var passwordBox = view.FindControl<TextBox>("PasswordBox");

        Assert.NotNull(loginNameBox);
        Assert.NotNull(passwordBox);
        Assert.False(InputMethod.GetIsInputMethodEnabled(loginNameBox));
        Assert.False(InputMethod.GetIsInputMethodEnabled(passwordBox));
    }

    private static void EnsureAvaloniaConfigured()
    {
        lock (AvaloniaSetupLock)
        {
            if (_avaloniaConfigured)
                return;

            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .SetupWithoutStarting();
            _avaloniaConfigured = true;
        }
    }
}

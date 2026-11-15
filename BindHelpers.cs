using Microsoft.UI.Xaml;

namespace WinExpander;

/// <summary>Small x:Bind function helpers.</summary>
public static class BindHelpers
{
    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}

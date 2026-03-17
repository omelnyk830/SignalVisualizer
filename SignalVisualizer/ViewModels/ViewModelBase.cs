using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SignalVisualizer.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public static FuncValueConverter<bool, string> PauseConverter { get; } =
        new(paused => paused ? "Resume" : "Pause");

    public static FuncValueConverter<bool, Color> ConnectionColorConverter { get; } =
        new(connected => connected ? Color.Parse("#2E7D32") : Color.Parse("#888888"));
}
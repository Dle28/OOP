using CommunityToolkit.Mvvm.ComponentModel;

namespace OsSim.Wpf.ViewModels;

public sealed partial class SimulationNavigationItem : ObservableObject
{
    public SimulationNavigationItem(SimulationArea area, string index, string title, string description)
    {
        Area = area;
        Index = index;
        Title = title;
        Description = description;
    }

    public SimulationArea Area { get; }

    public string Index { get; }

    public string Title { get; }

    public string Description { get; }

    [ObservableProperty]
    private bool isSelected;

    internal void SetSelected(bool value) => IsSelected = value;
}

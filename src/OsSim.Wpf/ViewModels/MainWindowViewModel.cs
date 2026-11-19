using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsSim.Application;

namespace OsSim.Wpf.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(SimulationCatalog catalog)
    {
        Cpu = new CpuSchedulerViewModel(catalog);
        Synchronization = new SynchronizationViewModel(catalog);
        Memory = new VirtualMemoryViewModel(catalog);
        ResourceAllocation = new BankerViewModel(catalog);
        NavigationItems =
        [
            new SimulationNavigationItem(SimulationArea.Cpu, "01", "Lập lịch CPU", "Gantt · ready queue"),
            new SimulationNavigationItem(SimulationArea.Synchronization, "02", "Đồng bộ", "Actor · buffer"),
            new SimulationNavigationItem(SimulationArea.Memory, "03", "Bộ nhớ ảo", "MMU · swap"),
            new SimulationNavigationItem(SimulationArea.ResourceAllocation, "04", "Khóa & Deadlock", "Banker · RAG")
        ];
        NavigationItems[0].SetSelected(true);
    }

    public CpuSchedulerViewModel Cpu { get; }

    public SynchronizationViewModel Synchronization { get; }

    public VirtualMemoryViewModel Memory { get; }

    public BankerViewModel ResourceAllocation { get; }

    public IReadOnlyList<SimulationNavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private SimulationArea selectedArea = SimulationArea.Cpu;

    public ISimulationAreaViewModel ActiveViewModel => SelectedArea switch
    {
        SimulationArea.Synchronization => Synchronization,
        SimulationArea.Memory => Memory,
        SimulationArea.ResourceAllocation => ResourceAllocation,
        _ => Cpu
    };

    [RelayCommand]
    private void NavigateTo(SimulationNavigationItem item)
    {
        SelectedArea = item.Area;
    }

    partial void OnSelectedAreaChanged(SimulationArea value)
    {
        foreach (var item in NavigationItems)
        {
            item.SetSelected(item.Area == value);
        }

        OnPropertyChanged(nameof(ActiveViewModel));
    }
}

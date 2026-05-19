using Avalonia.Controls;
using Avalonia.Input;
using DkcDesktopClient.App.ViewModels;

namespace DkcDesktopClient.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Initialization is handled by App.axaml.cs via the splash window sequence.
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Escape – close any open form/detail panel in the current view
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel vm)
        {
            switch (vm.CurrentView)
            {
                case MmViewModel mmVm when mmVm.IsFormVisible:
                    mmVm.CancelFormCommand.Execute(null);
                    e.Handled = true;
                    break;
                case NeaViewModel neaVm when neaVm.IsSystemFormVisible:
                    neaVm.CancelSystemFormCommand.Execute(null);
                    e.Handled = true;
                    break;
                case NeaViewModel neaVm when neaVm.IsInspectionFormVisible:
                    neaVm.CancelInspectionFormCommand.Execute(null);
                    e.Handled = true;
                    break;
                case BuildingViewModel bVm when bVm.IsBuildingFormVisible:
                    bVm.CancelBuildingFormCommand.Execute(null);
                    e.Handled = true;
                    break;
                case BuildingViewModel bVm when bVm.IsInspectionFormVisible:
                    bVm.CancelInspectionFormCommand.Execute(null);
                    e.Handled = true;
                    break;
                case KlimaViewModel kVm when kVm.IsControlPanelVisible:
                    kVm.HideDeviceControlCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }
}

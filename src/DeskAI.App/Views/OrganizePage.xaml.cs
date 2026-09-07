using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

public sealed partial class OrganizePage : Page
{
    public OrganizePage(OrganizeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

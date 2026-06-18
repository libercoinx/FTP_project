using System.Windows;

namespace FtpClient.App.Dialogs;

public enum ConflictChoice
{
    Cancel,
    Resume,
    Overwrite,
    Rename
}

public partial class ConflictDialog : Window
{
    public ConflictDialog(string detail)
    {
        InitializeComponent();
        DetailText.Text = detail;
    }

    public ConflictChoice Choice { get; private set; } = ConflictChoice.Cancel;

    private void Resume_OnClick(object sender, RoutedEventArgs e) => CloseWith(ConflictChoice.Resume);
    private void Overwrite_OnClick(object sender, RoutedEventArgs e) => CloseWith(ConflictChoice.Overwrite);
    private void Rename_OnClick(object sender, RoutedEventArgs e) => CloseWith(ConflictChoice.Rename);
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => CloseWith(ConflictChoice.Cancel);

    private void CloseWith(ConflictChoice choice)
    {
        Choice = choice;
        DialogResult = choice != ConflictChoice.Cancel;
    }
}

using System.Windows;
using FiveHourKeeper.ViewModels;

namespace FiveHourKeeper.Views;

public partial class ModelEditDialog : Window
{
    public ModelEditViewModel ViewModel { get; }
    public bool Saved { get; private set; }

    /// <summary>true 表示已经过用户“取消/保存”按钮明确处理，跳过二次确认。</summary>
    private bool _explicitAction;

    public ModelEditDialog(ModelEditViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        DataContext = this;
        ApiKeyBox.PasswordChanged += (_, _) => vm.ApiKey = ApiKeyBox.Password;
        if (!string.IsNullOrEmpty(vm.ApiKey))
            ApiKeyBox.Password = vm.ApiKey;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsValid())
        {
            ShowWarning("请补全标红的必填字段");
            return;
        }
        var rowErrors = ViewModel.CollectScheduleErrors();
        if (rowErrors is not null)
        {
            ShowWarning("重置时间有误：\n" + rowErrors);
            return;
        }
        Saved = true;
        _explicitAction = true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        // 取消按钮本身就是“明确放弃”，不再追问。
        Saved = false;
        _explicitAction = true;
        DialogResult = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 只有既非保存又非显式取消（例如点右上角 X / Alt+F4）时才询问一次。
        if (!_explicitAction && DialogResult != true)
        {
            var r = MessageBox.Show(this, "有尚未保存的修改，确定要关闭吗？",
                "未保存", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (r != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnClosing(e);
    }

    private void ShowWarning(string text) =>
        MessageBox.Show(this, text, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
}
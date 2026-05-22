using System.Windows.Controls;
using System.Windows.Input;
using WpfChatClient.ViewModels;

namespace WpfChatClient.Views
{
    public partial class ConnectView : UserControl
    {
        public ConnectView()
        {
            InitializeComponent();
        }

        private void AvatarBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ConnectViewModel vm)
            {
                vm.PickAvatarCommand.Execute(null);
            }
        }
    }
}

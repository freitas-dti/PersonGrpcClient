using PersonGrpcClient.ViewModels;

namespace PersonGrpcClient
{
    public partial class MainPage : ContentPage
    {

        private readonly MainPageViewModel _viewModel;

        public MainPage(MainPageViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _viewModel?.Dispose();
        }
    }
}